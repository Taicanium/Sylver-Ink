using SylverInk.Net;
using SylverInk.Notes;
using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using static SylverInk.Common;

namespace SylverInk;

/// <summary>
/// Interaction logic for SearchResult.xaml
/// </summary>
public partial class SearchResult : Window, IDisposable
{
	private readonly BackgroundWorker? AutosaveThread;
	private bool Dragging;
	private Point DragMouseCoords = new(0, 0);
	private bool Edited;
	private int OriginalRevisionCount = 0;
	private string OriginalText = string.Empty;
	public Database? ResultDatabase;
	public NoteRecord? ResultRecord;
	public string ResultText = string.Empty;
	private readonly double SnapTolerance = 20.0;
	private DateTime TimeSinceAutosave = DateTime.Now;

	public SearchResult()
	{
		InitializeComponent();
		DataContext = Common.Settings;

		AutosaveThread = new();
		AutosaveThread.DoWork += (_, _) =>
		{
			SpinWait.SpinUntil(new(() => DateTime.Now.Subtract(TimeSinceAutosave).Seconds >= 5.0));
			Concurrent(SaveRecord);
			Concurrent(Autosave);
		};
	}

	public void AddTabToRibbon()
	{
		ResultText = XamlWriter.Save(ResultBlock.Document);
		NoteTab newTab = new(ResultRecord ?? new(), ResultText);
		newTab.Construct();
		Close();
	}

	private void CloseClick(object sender, RoutedEventArgs e)
	{
		if (Edited)
		{
			switch(MessageBox.Show("You have unsaved changes. Save before closing this note?", "Sylver Ink: Notification", MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
			{
				case MessageBoxResult.Cancel:
					return;
				case MessageBoxResult.No:
					ResultBlock.Document = (FlowDocument)XamlReader.Parse(OriginalText);
					ResultText = OriginalText;
					Edited = false;
					for (int i = (ResultRecord?.GetNumRevisions() ?? 1) - 1; i >= OriginalRevisionCount; i--)
						ResultRecord?.DeleteRevision(i);
					DeferUpdateRecentNotes(true);
					break;
			}
		}

		PreviousOpenNote = ResultRecord;
		Close();
	}

	public void Dispose()
	{
		AutosaveThread?.Dispose();
		GC.SuppressFinalize(this);
	}

	private void Drag(object sender, MouseEventArgs e)
	{
		if (!Dragging)
			return;

		var mouse = PointToScreen(e.GetPosition(null));
		var newCoords = new Point()
		{
			X = DragMouseCoords.X + mouse.X,
			Y = DragMouseCoords.Y + mouse.Y
		};

		if (Common.Settings.SnapSearchResults)
			Snap(ref newCoords);

		Left = newCoords.X;
		Top = newCoords.Y;
	}

	private void Result_Closed(object sender, EventArgs e)
	{
		if (Edited)
			SaveRecord();

		ResultDatabase?.Transmit(Network.MessageType.RecordUnlock, IntToBytes(ResultRecord?.Index ?? 0));

		foreach (SearchResult result in OpenQueries)
		{
			if (result.ResultRecord != ResultRecord)
				continue;

			OpenQueries.Remove(result);
			return;
		}
	}

	private void Result_Loaded(object sender, RoutedEventArgs e)
	{
		if (ResultRecord?.Locked is true)
		{
			LastChangedLabel.Content = "Locked by another user";
			ResultBlock.IsEnabled = false;
		}
		else
		{
			LastChangedLabel.Content = ResultRecord?.GetLastChange();
			ResultDatabase?.Transmit(Network.MessageType.RecordUnlock, IntToBytes(ResultRecord?.Index ?? 0));
		}

		try
		{
			ResultBlock.Document = (FlowDocument)XamlReader.Parse(ResultText);
		}
		catch
		{
			ResultText = ResultRecord?.ToXaml() ?? string.Empty;
			ResultBlock.Document = (FlowDocument)XamlReader.Parse(ResultText);
		}

		Edited = false;
		OriginalRevisionCount = ResultRecord?.GetNumRevisions() ?? 0;
		OriginalText = ResultText;

		var tabPanel = GetChildPanel("DatabasesPanel");
		for (int i = tabPanel.Items.Count - 1; i > 0; i--)
		{
			var item = (TabItem)tabPanel.Items[i];

			if (((NoteRecord?)item.Tag)?.Equals(ResultRecord) is true)
				tabPanel.Items.RemoveAt(i);
		}
	}

	private void ResultBlock_TextChanged(object sender, TextChangedEventArgs e)
	{
		var plainText = FlowDocumentToPlaintext(ResultBlock.Document);
		Edited = !plainText.Equals(ResultRecord?.ToString());
		if (!Edited)
			return;
		if (AutosaveThread?.IsBusy is true)
			return;
		TimeSinceAutosave = DateTime.Now;
		AutosaveThread?.RunWorkerAsync();
	}

	private void SaveRecord()
	{
		if (ResultRecord is null)
			return;

		ResultText = XamlWriter.Save(ResultBlock.Document);
		ResultDatabase?.CreateRevision(ResultRecord, ResultText);
		LastChangedLabel.Content = ResultRecord?.GetLastChange();
		DeferUpdateRecentNotes(true);
	}

	private Point Snap(ref Point Coords)
	{
		var Snapped = (false, false);

		foreach (SearchResult other in OpenQueries)
		{
			if (Snapped.Item1 && Snapped.Item2)
				return Coords;

			if (other.ResultRecord == ResultRecord)
				continue;

			Point LT1 = new(Coords.X, Coords.Y);
			Point RB1 = new(Coords.X + Width, Coords.Y + Height);
			Point LT2 = new(other.Left, other.Top);
			Point RB2 = new(other.Left + other.Width, other.Top + other.Height);

			var dLR = Math.Abs(LT1.X - RB2.X);
			var dRL = Math.Abs(RB1.X - LT2.X);
			var dTB = Math.Abs(LT1.Y - RB2.Y);
			var dBT = Math.Abs(RB1.Y - LT2.Y);

			var dLL = Math.Abs(LT1.X - LT2.X);
			var dRR = Math.Abs(RB1.X - RB2.X);
			var dTT = Math.Abs(LT1.Y - LT2.Y);
			var dBB = Math.Abs(RB1.Y - RB2.Y);

			var XTolerance = (LT1.X >= LT2.X && LT1.X <= RB2.X)
				|| (RB1.X >= LT2.X && RB1.X <= RB2.X)
				|| (LT2.X >= LT1.X && LT2.X <= RB1.X)
				|| (RB2.X >= LT1.X && RB2.X <= RB1.X);

			var YTolerance = (LT1.Y >= LT2.Y && LT1.Y <= RB2.Y)
				|| (RB1.Y >= LT2.Y && RB1.Y <= RB2.Y)
				|| (LT2.Y >= LT1.Y && LT2.Y <= RB1.Y)
				|| (RB2.Y >= LT1.Y && RB2.Y <= RB1.Y);

			if (dLR < SnapTolerance && YTolerance && !Snapped.Item1)
			{
				Coords.X = RB2.X;
				Snapped = (true, Snapped.Item2);
			}

			if (dRL < SnapTolerance && YTolerance && !Snapped.Item1)
			{
				Coords.X = LT2.X - Width;
				Snapped = (true, Snapped.Item2);
			}

			if (dTB < SnapTolerance && XTolerance && !Snapped.Item2)
			{
				Coords.Y = RB2.Y;
				Snapped = (Snapped.Item1, true);
			}

			if (dBT < SnapTolerance && XTolerance && !Snapped.Item2)
			{
				Coords.Y = LT2.Y - Height;
				Snapped = (Snapped.Item1, true);
			}

			if (dLL < SnapTolerance && !Snapped.Item1 && Snapped.Item2)
			{
				Coords.X = LT2.X;
				Snapped = (true, true);
			}

			if (dRR < SnapTolerance && !Snapped.Item1 && Snapped.Item2)
			{
				Coords.X = RB2.X - Width;
				Snapped = (true, true);
			}

			if (dTT < SnapTolerance && Snapped.Item1 && !Snapped.Item2)
			{
				Coords.Y = LT2.Y;
				Snapped = (true, true);
			}

			if (dBB < SnapTolerance && Snapped.Item1 && !Snapped.Item2)
			{
				Coords.Y = RB2.Y - Height;
				Snapped = (true, true);
			}
		}

		return Coords;
	}

	private void ViewClick(object sender, RoutedEventArgs e)
	{
		SearchWindow?.Close();
		AddTabToRibbon();
	}

	private void WindowActivated(object sender, EventArgs e)
	{
		CloseButton.IsEnabled = true;
		Opacity = 1.0;
		ViewButton.IsEnabled = true;
	}

	private void WindowDeactivated(object sender, EventArgs e)
	{
		CloseButton.IsEnabled = false;
		Opacity = 1.0 - (Common.Settings.NoteTransparency / 100.0);
		ViewButton.IsEnabled = false;
	}

	private void WindowMove(object sender, MouseEventArgs e) => Drag(sender, e);

	private void WindowMouseDown(object sender, MouseButtonEventArgs e)
	{
		var n = PointToScreen(e.GetPosition(null));
		CaptureMouse();
		DragMouseCoords = new(Left - n.X, Top - n.Y);
		Dragging = true;
	}

	private void WindowMouseUp(object sender, MouseButtonEventArgs e)
	{
		ReleaseMouseCapture();
		DragMouseCoords = new(0, 0);
		Dragging = false;
	}
}
