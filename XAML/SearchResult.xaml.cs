using SylverInk.Net;
using SylverInk.Notes;
using SylverInk.XAMLUtils;
using System;
using System.ComponentModel;
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

	[DllImport("user32.dll")]
	static extern int GetWindowLong(IntPtr hwnd, int index);

	[DllImport("user32.dll")]
	static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

	private readonly BackgroundWorker EnterTask;
	private const int GWL_EXSTYLE = -20;
	private IntPtr hWnd;
	private readonly BackgroundWorker LeaveTask;
	private bool MouseInside;
	private readonly DispatcherTimer MouseMonitor;
	private bool NeedsAutosave;
	private double StartOpacity;
	private DateTime TimeSinceAutosave = DateTime.UtcNow;
	private const int WS_EX_LAYERED = 0x00080000;
	private const int WS_EX_TRANSPARENT = 0x00000020;

	public bool Dragging { get; private set; }
	public Point DragMouseCoords { get; private set; } = new(0, 0);
	public bool Edited { get; set; }
	public bool FinishedLoading { get; set; }
	public int OriginalBlockCount { get; set; } = -1;
	public int OriginalRevisionCount { get; set; }
	public string OriginalText { get; set; } = string.Empty;
	public Database? ResultDatabase { get; set; }
	public NoteRecord? ResultRecord { get; set; }
	public double SnapTolerance { get; } = 20.0;

	public SearchResult()
	{
		InitializeComponent();
		DataContext = CommonUtils.Settings;

		EnterTask = new()
		{
			WorkerSupportsCancellation = true
		};

		LeaveTask = new()
		{
			WorkerSupportsCancellation = true
		};

		EnterTask.DoWork += (sender, _) =>
		{
			if (sender is not BackgroundWorker worker)
				return;

			var EnterTime = DateTime.UtcNow;
			var Seconds = 0.0;
			while ((Seconds = DateTime.UtcNow.Subtract(EnterTime).TotalMilliseconds * 0.001) < CommonUtils.Settings.NoteClickthrough)
			{
				if (worker.CancellationPending)
					return;

				var lerpValue = Lerp(StartOpacity, 1.0, Seconds / CommonUtils.Settings.NoteClickthrough);
				Concurrent(() => Opacity = lerpValue);
			}

			Concurrent(UnsetWindowExTransparent);
		};

		LeaveTask.DoWork += (sender, _) =>
		{
			if (sender is not BackgroundWorker worker)
				return;

			Concurrent(SetWindowExTransparent);

			var LeaveTime = DateTime.UtcNow;
			var Seconds = 0.0;
			while ((Seconds = DateTime.UtcNow.Subtract(LeaveTime).TotalMilliseconds * 0.001) < CommonUtils.Settings.NoteClickthrough)
			{
				if (worker.CancellationPending)
					return;

				var lerpValue = Lerp(StartOpacity, 1.0 - (CommonUtils.Settings.NoteTransparency * 0.01), Seconds / CommonUtils.Settings.NoteClickthrough);
				Concurrent(() => Opacity = lerpValue);
			}
		};

		MouseMonitor = new()
		{
			Interval = new TimeSpan(0, 0, 0, 0, 150)
		};

		MouseMonitor.Tick += WindowMouseMonitor;
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

		if (EnterTask.IsBusy)
			EnterTask.CancelAsync();

		if (LeaveTask.IsBusy)
			LeaveTask.CancelAsync();

		MouseMonitor.Stop();
		PreviousOpenNote = ResultRecord;

		Close();
	}

	public void Dispose()
	{
		EnterTask?.Dispose();
		LeaveTask?.Dispose();
		GC.SuppressFinalize(this);
	}

	private void Result_Closed(object? sender, EventArgs e)
	{
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
		if (NeedsAutosave)
			return;

		NeedsAutosave = true;
		Task.Factory.StartNew(() =>
		{
			SpinWait.SpinUntil(() => (DateTime.UtcNow - TimeSinceAutosave).Seconds >= 5);

			this.Autosave();
			NeedsAutosave = false;
			RecentNotesDirty = true;
			TimeSinceAutosave = DateTime.UtcNow;
			return;
		}, TaskCreationOptions.LongRunning);
	}

	public bool SetWindowExTransparent()
	{
		var extendedStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
		return SetWindowLong(hWnd, GWL_EXSTYLE, extendedStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT) != 0;
	}

	public bool UnsetWindowExTransparent()
	{
		int extendedStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
		return SetWindowLong(hWnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_LAYERED & ~WS_EX_TRANSPARENT) != 0;
	}

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

		UnsetWindowExTransparent();
	}

	private void WindowDeactivated(object? sender, EventArgs e)
	{
		CloseButton.IsEnabled = false;
		Opacity = 1.0 - (CommonUtils.Settings.NoteTransparency * 0.01);
		ViewButton.IsEnabled = false;

		SetWindowExTransparent();
	}

	private void WindowLoaded(object? sender, RoutedEventArgs e)
	{
		this.Construct();

		hWnd = new WindowInteropHelper(this).Handle;
		MouseMonitor.Start();
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

		if (EnterTask.IsBusy)
			return;

		if (CommonUtils.Settings.NoteTransparency == 0.0)
			return;

		if (LeaveTask.IsBusy)
			LeaveTask?.CancelAsync();

		StartOpacity = Opacity;

		if (StartOpacity == 1.0)
			return;

		EnterTask.RunWorkerAsync();
	}

	private void WindowMouseLeave(object sender, MouseEventArgs e)
	{
		if (IsActive)
			return;

		if (LeaveTask.IsBusy)
			return;

		if (CommonUtils.Settings.NoteTransparency == 0.0)
			return;

		if (EnterTask.IsBusy)
			EnterTask?.CancelAsync();

		StartOpacity = Opacity;

		if (StartOpacity == 1.0 - (CommonUtils.Settings.NoteTransparency * 0.01))
			return;

		LeaveTask.RunWorkerAsync();
	}

	private void WindowMouseMonitor(object? sender, EventArgs e)
	{
		Concurrent(() =>
		{
			if (!GetCursorPos(out SearchResultUtils.SimplePoint screenPosition))
				return;

			var eventArgs = new MouseEventArgs(Mouse.PrimaryDevice, 0);
			var position = PointFromScreen(new(screenPosition.X, screenPosition.Y));

			if (position.X > 0.0
			&& position.Y > 0.0
			&& position.X <= Width
			&& position.Y <= Height)
			{
				if (MouseInside)
					return;

				MouseInside = true;

				eventArgs.RoutedEvent = Mouse.MouseEnterEvent;
				RaiseEvent(eventArgs);
			}
			else
			{
				if (!MouseInside)
					return;

				MouseInside = false;

				eventArgs.RoutedEvent = Mouse.MouseLeaveEvent;
				RaiseEvent(eventArgs);
			}
		});
	}

	private void WindowMouseUp(object? sender, MouseButtonEventArgs e)
	{
		ReleaseMouseCapture();
		DragMouseCoords = new(0, 0);
		Dragging = false;
	}
}
