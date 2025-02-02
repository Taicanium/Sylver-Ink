using SylverInk.Net;
using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static SylverInk.Common;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for SearchResult.xaml
	/// </summary>
	public partial class SearchResult : Window
	{
		private readonly BackgroundWorker? AutosaveThread;
		private bool Dragging = false;
		private Point DragMouseCoords = new(0, 0);
		private bool Edited = false;
		private string OriginalText = string.Empty;
		public string Query = string.Empty;
		public int ResultDatabase = 0;
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
			};
		}

		public void AddTabToRibbon()
		{
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
						Edited = false;
						ResultText = OriginalText;
						SaveRecord();
						break;
				}
			}

			Close();
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

			CurrentDatabase.Transmit(Network.MessageType.RecordUnlock, IntToBytes(ResultRecord?.Index ?? 0));

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
				LastChangedLabel.Content = "Note locked by another user";
				ResultBlock.IsEnabled = false;
			}
			else
			{
				LastChangedLabel.Content = "Last modified: " + ResultRecord?.GetLastChange();
				CurrentDatabase.Transmit(Network.MessageType.RecordUnlock, IntToBytes(ResultRecord?.Index ?? 0));
			}

			if (ResultText.Equals(string.Empty))
				ResultText = ResultRecord?.ToString() ?? string.Empty;
			
			Edited = false;
			ResultBlock.Text = ResultText;
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
			var senderObject = sender as TextBox;
			ResultText = senderObject?.Text ?? string.Empty;
			Edited = !ResultText.Equals(ResultRecord?.ToString());
			TimeSinceAutosave = DateTime.Now;
			if (!AutosaveThread?.IsBusy is true)
				AutosaveThread?.RunWorkerAsync();
		}

		private void SaveRecord()
		{
			if (ResultRecord is null)
				return;

			CurrentDatabase.CreateRevision(ResultRecord, ResultText);
			LastChangedLabel.Content = "Last modified: " + ResultRecord?.GetLastChange();
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
				Point LT2A = new(other.Left + 16.0, other.Top + 9.0);
				Point RB2A = new(other.Left + other.Width - 16.0, other.Top + other.Height - 9.0);

				var dLR = Math.Abs(LT1.X - RB2A.X);
				var dRL = Math.Abs(RB1.X - LT2A.X);
				var dTB = Math.Abs(LT1.Y - RB2A.Y);
				var dBT = Math.Abs(RB1.Y - LT2A.Y);

				var dLL = Math.Abs(LT1.X - LT2.X);
				var dRR = Math.Abs(RB1.X - RB2.X);
				var dTT = Math.Abs(LT1.Y - LT2.Y);
				var dBB = Math.Abs(RB1.Y - RB2.Y);

				var XTolerance = (LT1.X >= LT2A.X && LT1.X <= RB2A.X)
					|| (RB1.X >= LT2A.X && RB1.X <= RB2A.X)
					|| (LT2A.X >= LT1.X && LT2A.X <= RB1.X)
					|| (RB2A.X >= LT1.X && RB2A.X <= RB1.X);

				var YTolerance = (LT1.Y >= LT2A.Y && LT1.Y <= RB2A.Y)
					|| (RB1.Y >= LT2A.Y && RB1.Y <= RB2A.Y)
					|| (LT2A.Y >= LT1.Y && LT2A.Y <= RB1.Y)
					|| (RB2A.Y >= LT1.Y && RB2A.Y <= RB1.Y);

				if (dLR < SnapTolerance && YTolerance && !Snapped.Item1)
				{
					Coords.X = RB2A.X;
					Snapped = (true, Snapped.Item2);
				}

				if (dRL < SnapTolerance && YTolerance && !Snapped.Item1)
				{
					Coords.X = LT2A.X - Width;
					Snapped = (true, Snapped.Item2);
				}

				if (dTB < SnapTolerance && XTolerance && !Snapped.Item2)
				{
					Coords.Y = RB2A.Y;
					Snapped = (Snapped.Item1, true);
				}

				if (dBT < SnapTolerance && XTolerance && !Snapped.Item2)
				{
					Coords.Y = LT2A.Y - Height;
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
}
