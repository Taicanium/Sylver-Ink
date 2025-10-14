using SylverInk.Net;
using SylverInk.Notes;
using SylverInk.XAMLUtils;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using static SylverInk.CommonUtils;
using static SylverInk.XAMLUtils.DataUtils;
using static SylverInk.XAMLUtils.TextUtils;

namespace SylverInk;

/// <summary>
/// Interaction logic for SearchResult.xaml
/// </summary>
public partial class SearchResult : Window, IDisposable
{
	[DllImport("user32.dll")]
	static extern bool GetCursorPos(out SearchResultUtils.SimplePoint pPoint);

	private bool Autosaving;
	private bool MouseInside;
	private DateTime TimeSinceAutosave = DateTime.UtcNow;

	public bool Dragging { get; private set; }
	public Point DragMouseCoords { get; private set; } = new(0, 0);
	public bool Edited { get; set; }
	public DispatcherTimer? EnterMonitor { get; set; }
	public long EnterTime { get; set; }
	public bool FinishedLoading { get; set; }
	public nint HWnd { get; set; }
	public DispatcherTimer? LeaveMonitor { get; set; }
	public long LeaveTime { get; set; }
	public DispatcherTimer? MouseMonitor { get; set; }
	public int OriginalBlockCount { get; set; } = -1;
	public int OriginalRevisionCount { get; set; }
	public string OriginalText { get; set; } = string.Empty;
	public Database? ResultDatabase { get; set; }
	public NoteRecord? ResultRecord { get; set; }
	public double SnapTolerance { get; } = 20.0;
	public double StartOpacity { get; set; }

	public SearchResult()
	{
		InitializeComponent();
		DataContext = CommonUtils.Settings;

		this.InitMonitors();
	}

	private void CloseClick(object? sender, RoutedEventArgs e)
	{
		if (Edited)
		{
			switch(MessageBox.Show("You have unsaved changes. Save before closing this note?", "Sylver Ink: Notification", MessageBoxButton.YesNoCancel, MessageBoxImage.Information))
			{
				case MessageBoxResult.Cancel:
					return;
				case MessageBoxResult.No:
					Edited = false;
					for (int i = (ResultRecord?.GetNumRevisions() ?? 1) - 1; i >= OriginalRevisionCount; i--)
						ResultRecord?.DeleteRevision(i);
					RecentNotesDirty = true;
					DeferUpdateRecentNotes();
					break;
			}
		}

		Close();
	}

	public void Dispose()
	{
		this.StopMonitors();
		GC.SuppressFinalize(this);
	}

	private void Result_Closed(object? sender, EventArgs e)
	{
		this.StopMonitors();
		PreviousOpenNote = ResultRecord;

		if (Edited)
			this.SaveRecord();

		ResultDatabase?.Transmit(NetworkUtils.MessageType.RecordUnlock, IntToBytes(ResultRecord?.Index ?? 0));

		foreach (SearchResult result in OpenQueries)
		{
			if (result.ResultRecord != ResultRecord)
				continue;

			OpenQueries.Remove(result);
			return;
		}
	}

	private void ResultBlock_TextChanged(object? sender, TextChangedEventArgs e)
	{
		if (!FinishedLoading)
			return;

		Edited = ResultBlock.Document.Blocks.Count != OriginalBlockCount || !OriginalText.Equals(FlowDocumentToXaml(ResultBlock.Document));
		if (Autosaving)
			return;

		Autosaving = true;
		Task.Factory.StartNew(() =>
		{
			SpinWait.SpinUntil(() => (DateTime.UtcNow - TimeSinceAutosave).Seconds >= 5);

			Concurrent(() => ResultRecord?.Autosave(ResultBlock.Document));
			RecentNotesDirty = true;
			TimeSinceAutosave = DateTime.UtcNow;
			Autosaving = false;
			return;
		}, TaskCreationOptions.LongRunning);
	}

	public void ScrollToText(string text) => TextUtils.ScrollToText(ResultBlock, text); 

	private void ViewClick(object? sender, RoutedEventArgs e)
	{
		SearchWindow?.Close();
		this.AddTabToRibbon();
	}

	private void WindowActivated(object? sender, EventArgs e)
	{
		CloseButton.IsEnabled = true;
		Opacity = 1.0;
		ViewButton.IsEnabled = true;

		this.UnsetWindowExTransparent();
	}

	private void WindowDeactivated(object? sender, EventArgs e)
	{
		CloseButton.IsEnabled = false;
		Opacity = 1.0 - (CommonUtils.Settings.NoteTransparency * 0.01);
		ViewButton.IsEnabled = false;

		this.SetWindowExTransparent();
	}

	private void WindowLoaded(object? sender, RoutedEventArgs e)
	{
		this.Construct();

		HWnd = new WindowInteropHelper(this).Handle;
		MouseMonitor?.Start();
	}

	private void WindowMove(object? sender, MouseEventArgs e) => this.Drag(sender, e);

	private void WindowMouseDown(object? sender, MouseButtonEventArgs e)
	{
		var n = PointToScreen(e.GetPosition(null));
		CaptureMouse();
		DragMouseCoords = new(Left - n.X, Top - n.Y);
		Dragging = true;
	}

	private void WindowMouseEnter(object sender, MouseEventArgs e)
	{
		if (IsActive)
			return;

		if (EnterMonitor?.IsEnabled is true)
			return;

		if (CommonUtils.Settings.NoteTransparency == 0.0)
			return;

		LeaveMonitor?.Stop();

		StartOpacity = Opacity;

		if (StartOpacity == 1.0)
			return;

		EnterTime = DateTime.UtcNow.Ticks;
		EnterMonitor?.Start();
	}

	private void WindowMouseLeave(object sender, MouseEventArgs e)
	{
		if (IsActive)
			return;

		if (LeaveMonitor?.IsEnabled is true)
			return;

		if (CommonUtils.Settings.NoteTransparency == 0.0)
			return;

		EnterMonitor?.Stop();

		StartOpacity = Opacity;

		if (StartOpacity == 1.0 - (CommonUtils.Settings.NoteTransparency * 0.01))
			return;

		Concurrent(this.SetWindowExTransparent);
		LeaveTime = DateTime.UtcNow.Ticks;
		LeaveMonitor?.Start();
	}

	public void WindowMouseMonitor(object? sender, EventArgs e)
	{
		Concurrent(() =>
		{
			if (!GetCursorPos(out SearchResultUtils.SimplePoint screenPosition))
				return;

			var eventArgs = new MouseEventArgs(Mouse.PrimaryDevice, 0);
			Point position;

			try
			{
				position = PointFromScreen(new(screenPosition.X, screenPosition.Y));
			}
			catch
			{
				return;
			}

			if (position.X > 0.0
			&& position.Y > 0.0
			&& position.X <= Width
			&& position.Y <= Height)
			{
				if (MouseInside)
					return;

				MouseInside = true;
				eventArgs.RoutedEvent = Mouse.MouseEnterEvent;
			}
			else
			{
				if (!MouseInside)
					return;

				MouseInside = false;
				eventArgs.RoutedEvent = Mouse.MouseLeaveEvent;
			}

			RaiseEvent(eventArgs);
		});
	}

	private void WindowMouseUp(object? sender, MouseButtonEventArgs e)
	{
		ReleaseMouseCapture();
		DragMouseCoords = new(0, 0);
		Dragging = false;
	}
}
