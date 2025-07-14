using SylverInk.Net;
using SylverInk.Notes;
using SylverInk.XAMLUtils;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static SylverInk.CommonUtils;
using static SylverInk.XAMLUtils.DataUtils;
using static SylverInk.XAMLUtils.TextUtils;

namespace SylverInk;

/// <summary>
/// Interaction logic for SearchResult.xaml
/// </summary>
public partial class SearchResult : Window, IDisposable
{
	private Task? EnterTask;
	private DateTime EnterTime;
	private Task? LeaveTask;
	private DateTime LeaveTime;
	private bool NeedsAutosave;
	private double StartOpacity;
	private DateTime TimeSinceAutosave = DateTime.UtcNow;
	private CancellationTokenSource? TokenSource;

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

		PreviousOpenNote = ResultRecord;
		Close();
	}

	public void Dispose()
	{
		EnterTask?.Dispose();
		LeaveTask?.Dispose();
		TokenSource?.Dispose();
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

	private void Result_Loaded(object? sender, RoutedEventArgs e)
	{
		this.Construct();
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
	}

	private void WindowDeactivated(object? sender, EventArgs e)
	{
		CloseButton.IsEnabled = false;
		Opacity = 1.0 - (CommonUtils.Settings.NoteTransparency / 100.0);
		ViewButton.IsEnabled = false;
	}

	private void WindowMove(object? sender, MouseEventArgs e) => this.Drag(sender, e);

	private void WindowMouseDown(object? sender, MouseButtonEventArgs e)
	{
		if (!EnterTask?.IsCompleted is true)
			TokenSource?.Cancel();

		var n = PointToScreen(e.GetPosition(null));
		CaptureMouse();
		DragMouseCoords = new(Left - n.X, Top - n.Y);
		Dragging = true;
	}

	private void WindowMouseEnter(object sender, MouseEventArgs e)
	{
		if (IsActive)
			return;

		if (!LeaveTask?.IsCompleted is true)
			TokenSource?.Cancel();

		EnterTime = DateTime.UtcNow;
		StartOpacity = Opacity;

		TokenSource?.Dispose();
		TokenSource = new();

		EnterTask = Task.Factory.StartNew((tokenObject) => {
			if (tokenObject is not CancellationToken token)
				return;

			var Seconds = DateTime.UtcNow.Subtract(EnterTime).Milliseconds / 1000.0;
			while (!token.IsCancellationRequested && Seconds < 0.25)
			{
				var lerpValue = Lerp(StartOpacity, 1.0, Seconds * 4.0);
				Concurrent(() => Opacity = lerpValue);
				Seconds = DateTime.UtcNow.Subtract(EnterTime).Milliseconds / 1000.0;
			}
		}, TokenSource.Token);
	}

	private void WindowMouseLeave(object sender, MouseEventArgs e)
	{
		if (IsActive)
			return;

		if (!EnterTask?.IsCompleted is true)
			TokenSource?.Cancel();

		LeaveTime = DateTime.UtcNow;
		StartOpacity = Opacity;

		TokenSource?.Dispose();
		TokenSource = new();

		LeaveTask = Task.Factory.StartNew((tokenObject) => {
			if (tokenObject is not CancellationToken token)
				return;

			var Seconds = DateTime.UtcNow.Subtract(LeaveTime).Milliseconds / 1000.0;
			while (!token.IsCancellationRequested && Seconds < 0.25)
			{
				var lerpValue = Lerp(StartOpacity, 1.0 - (CommonUtils.Settings.NoteTransparency / 100.0), Seconds * 4.0);
				Concurrent(() => Opacity = lerpValue);
				Seconds = DateTime.UtcNow.Subtract(LeaveTime).Milliseconds / 1000.0;
			}
		}, TokenSource.Token);
	}

	private void WindowMouseUp(object? sender, MouseButtonEventArgs e)
	{
		ReleaseMouseCapture();
		DragMouseCoords = new(0, 0);
		Dragging = false;
	}
}
