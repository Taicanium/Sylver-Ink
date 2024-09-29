using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for SearchResult.xaml
	/// </summary>
	public partial class SearchResult : Window
	{
		private bool Dragging = false;
		private Point DragMouseCoords = new(0, 0);
		private bool Edited = false;
		public string Query = string.Empty;
		public int ResultDatabase = 0;
		public int ResultRecord = -1;
		public string ResultText = string.Empty;
		private readonly double SnapTolerance = 20.0;

		public SearchResult()
		{
			InitializeComponent();
			DataContext = Common.Settings;
		}

		public void AddTabToRibbon()
		{
			var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");
			var tabPanel = Common.GetChildPanel("DatabasesPanel");

			foreach (TabItem item in tabPanel.Items)
			{
				if (ResultRecord == (int)(item.Tag ?? -1))
				{
					tabPanel.SelectedIndex = tabPanel.Items.IndexOf(item);
					Close();
					return;
				}
			}

			TabItem newTab = new()
			{
				Header = Common.GetRibbonHeader(ResultRecord),
				Tag = ResultRecord,
				ToolTip = Common.GetRibbonTooltip(ResultRecord)
			};

			control.SelectedIndex = ResultDatabase;
			tabPanel.SelectedIndex = tabPanel.Items.Add(newTab);

			Grid grid = new() { Margin = new(2.0, 2.0, 2.0, 2.0), };

			grid.RowDefinitions.Add(new() { Height = GridLength.Auto, });
			grid.RowDefinitions.Add(new() { Height = new(1.0, GridUnitType.Star) });
			grid.RowDefinitions.Add(new() { Height = GridLength.Auto, });

			Label revisionLabel = new()
			{
				Content = "Entry last modified: " + Common.CurrentDatabase.Controller.GetRecord(ResultRecord).GetLastChange(),
				FontStyle = FontStyles.Italic,
				HorizontalAlignment = HorizontalAlignment.Right,
				Margin = new(0.0, 0.0, 10.0, 0.0),
				VerticalAlignment = VerticalAlignment.Bottom
			};

			TextBox noteBox = new()
			{
				AcceptsReturn = true,
				Height = double.NaN,
				Margin = new(5.0),
				Tag = (0U, ResultRecord),
				Text = ResultText,
				TextWrapping = TextWrapping.WrapWithOverflow,
				VerticalContentAlignment = VerticalAlignment.Top,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto
			};

			Button nextButton = new()
			{
				Content = "\u2192",
				HorizontalAlignment = HorizontalAlignment.Left,
				Width = 50
			};

			Button previousButton = new()
			{
				Content = "\u2190",
				HorizontalAlignment = HorizontalAlignment.Right,
				Width = 50
			};

			Button deleteButton = new()
			{
				Content = "Delete",
				Margin = new(0, 0, 20, 0),
				Tag = tabPanel.SelectedIndex
			};

			Button returnButton = new()
			{
				Content = "Close",
				Margin = new(20, 0, 0, 0),
				Tag = tabPanel.SelectedIndex
			};

			Button saveButton = new() { Content = "Save" };

			nextButton.IsEnabled = false;
			previousButton.IsEnabled = Common.CurrentDatabase.Controller.GetRecord(ResultRecord).GetNumRevisions() > 0;
			saveButton.IsEnabled = false;

			noteBox.TextChanged += (sender, _) =>
			{
				var senderObject = (TextBox)sender;
				var tag = ((uint, int))senderObject.Tag;
				var record = Common.CurrentDatabase.Controller.GetRecord(tag.Item2);
				saveButton.IsEnabled = !senderObject.Text.Equals(record.ToString());
			};

			nextButton.Click += (sender, _) =>
			{
				var senderObject = (Button)sender;
				var tabPanel = Common.GetChildPanel("DatabasesPanel");
				var tag = ((uint, int))noteBox.Tag;
				var record = Common.CurrentDatabase.Controller.GetRecord(tag.Item2);
				string revisionTime = record.GetCreated();

				tag.Item1 -= 1U;
				if (tag.Item1 == 0U)
					revisionTime = record.GetLastChange();
				else
					revisionTime = record.GetRevisionTime(tag.Item1);

				noteBox.Tag = (tag.Item1, tag.Item2);
				noteBox.Text = record.Reconstruct(tag.Item1);
				noteBox.IsReadOnly = tag.Item1 != 0;
				senderObject.IsEnabled = tag.Item1 > 0;
				previousButton.IsEnabled = tag.Item1 < record.GetNumRevisions();
				revisionLabel.Content = (tag.Item1 == 0U ? "Entry last modified: " : $"Revision {record.GetNumRevisions() - tag.Item1} from ") + revisionTime;
				saveButton.Content = tag.Item1 == 0 ? "Save" : "Restore";
				var latestText = record.ToString();
				saveButton.IsEnabled = !latestText.Equals(noteBox.Text);
			};

			previousButton.Click += (sender, _) =>
			{
				var senderObject = (Button)sender;
				var tabPanel = Common.GetChildPanel("DatabasesPanel");
				var tag = ((uint, int))noteBox.Tag;
				var record = Common.CurrentDatabase.Controller.GetRecord(tag.Item2);
				string revisionTime = record.GetLastChange();

				tag.Item1 += 1U;
				if (tag.Item1 == record.GetNumRevisions())
					revisionTime = record.GetCreated();
				else
					revisionTime = record.GetRevisionTime(tag.Item1);

				noteBox.Tag = (tag.Item1, tag.Item2);
				noteBox.Text = record.Reconstruct(tag.Item1);
				noteBox.IsReadOnly = tag.Item1 != 0;
				senderObject.IsEnabled = tag.Item1 + 1 <= record.GetNumRevisions();
				revisionLabel.Content = (tag.Item1 == record.GetNumRevisions() ? "Entry created " : $"Revision {record.GetNumRevisions() - tag.Item1} from ") + revisionTime;
				nextButton.IsEnabled = tag.Item1 > 0;
				saveButton.Content = "Restore";
				var latestText = record.ToString();
				saveButton.IsEnabled = !latestText.Equals(noteBox.Text);
			};

			returnButton.Click += (sender, _) =>
			{
				var senderObject = (Button)sender;
				var tabPanel = Common.GetChildPanel("DatabasesPanel");
				var tag = ((uint, int))noteBox.Tag;
				var tabIndex = (int)senderObject.Tag;

				if (saveButton.IsEnabled && saveButton.Content.Equals("Save"))
				{
					switch (MessageBox.Show("You have unsaved changes. Save before closing this note?", "Sylver Ink: Notification", MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
					{
						case MessageBoxResult.Cancel:
							return;
						case MessageBoxResult.Yes:
							Common.CurrentDatabase.Controller.CreateRevision(tag.Item2, noteBox.Text);
							Common.DeferUpdateRecentNotes();
							break;
					}
				}

				tabPanel.SelectedIndex = 0;
				tabPanel.Items.RemoveAt(tabIndex);
			};

			deleteButton.Click += (sender, _) =>
			{
				var senderObject = (Button)sender;
				var tabPanel = Common.GetChildPanel("DatabasesPanel");
				var tag = ((uint, int))noteBox.Tag;
				var tabIndex = (int)senderObject.Tag;

				if (MessageBox.Show("Are you sure you want to permanently delete this note?", "Sylver Ink: Notification", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
					return;

				Common.CurrentDatabase.Controller.DeleteRecord(tag.Item2);
				Common.DeferUpdateRecentNotes();
			};

			saveButton.Click += (sender, _) =>
			{
				var senderObject = (Button)sender;
				var tag = ((uint, int))noteBox.Tag;

				Common.CurrentDatabase.Controller.CreateRevision(tag.Item2, noteBox.Text);
				Common.DeferUpdateRecentNotes();

				noteBox.Tag = (0U, tag.Item2);
				noteBox.IsEnabled = true;
				previousButton.IsEnabled = true;
				nextButton.IsEnabled = false;
				revisionLabel.Content = "Entry last modified: " + Common.CurrentDatabase.Controller.GetRecord(tag.Item2).GetLastChange();
				senderObject.IsEnabled = false;
			};

			Grid buttonGrid = new()
			{
				Margin = new(0, 20, 0, 20),
				HorizontalAlignment = HorizontalAlignment.Center,
			};

			buttonGrid.RowDefinitions.Add(new() { Height = GridLength.Auto, });
			buttonGrid.RowDefinitions.Add(new() { Height = new(30.0), });
			buttonGrid.RowDefinitions.Add(new() { Height = GridLength.Auto, });
			buttonGrid.ColumnDefinitions.Add(new() { Width = new(1.0, GridUnitType.Star), });
			buttonGrid.ColumnDefinitions.Add(new() { Width = new(1.0, GridUnitType.Star), });
			buttonGrid.ColumnDefinitions.Add(new() { Width = new(1.0, GridUnitType.Star), });

			buttonGrid.Children.Add(previousButton);
			buttonGrid.Children.Add(nextButton);
			buttonGrid.Children.Add(deleteButton);
			buttonGrid.Children.Add(saveButton);
			buttonGrid.Children.Add(returnButton);

			grid.Children.Add(revisionLabel);
			grid.Children.Add(noteBox);
			grid.Children.Add(buttonGrid);

			Grid.SetColumn(saveButton, 1);
			Grid.SetColumn(nextButton, 2);
			Grid.SetColumn(returnButton, 2);

			Grid.SetRow(deleteButton, 2);
			Grid.SetRow(returnButton, 2);
			Grid.SetRow(saveButton, 2);

			Grid.SetRow(revisionLabel, 0);
			Grid.SetRow(noteBox, 1);
			Grid.SetRow(buttonGrid, 2);

			newTab.Content = grid;
		}

		private void CloseClick(object sender, RoutedEventArgs e)
		{
			if (Edited)
			{
				var result = MessageBox.Show("You have unsaved changes. Save before closing this note?", "Sylver Ink: Notification", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

				if (result == MessageBoxResult.Cancel)
					return;

				if (result == MessageBoxResult.Yes)
					SaveRecord();
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

			Left = Math.Max(0.0, newCoords.X);
			Top = Math.Max(0.0, newCoords.Y);
		}

		private void Result_Closed(object sender, EventArgs e)
		{
			if (Edited)
				SaveRecord();

			foreach (SearchResult result in Common.OpenQueries)
			{
				if (result.ResultRecord == ResultRecord)
				{
					Common.OpenQueries.Remove(result);
					return;
				}
			}
		}

		private void Result_Loaded(object sender, RoutedEventArgs e)
		{
			LastChangedLabel.Content = "Last modified: " + Common.CurrentDatabase.Controller.GetRecord(ResultRecord).GetLastChange();
			if (ResultText.Equals(string.Empty))
				ResultText = Common.CurrentDatabase.Controller.GetRecord(ResultRecord).ToString();
			ResultBlock.Text = ResultText;
			Edited = false;

			var tabPanel = Common.GetChildPanel("DatabasesPanel");
			for (int i = tabPanel.Items.Count - 1; i > 0; i--)
			{
				var item = (TabItem)tabPanel.Items[i];

				if ((int?)item.Tag == ResultRecord)
					tabPanel.Items.RemoveAt(i);
			}
		}

		private void ResultBlock_TextChanged(object sender, TextChangedEventArgs e)
		{
			var senderObject = sender as TextBox;
			ResultText = senderObject?.Text ?? string.Empty;
			Edited = !ResultText.Equals(Common.CurrentDatabase.Controller.GetRecord(ResultRecord).ToString());
		}

		private void SaveClick(object sender, RoutedEventArgs e)
		{
			SaveRecord();

			Common.DeferUpdateRecentNotes();
			Close();
		}

		private void SaveRecord()
		{
			Common.CurrentDatabase.Controller.CreateRevision(ResultRecord, ResultText);
			LastChangedLabel.Content = "Last modified: " + Common.CurrentDatabase.Controller.GetRecord(ResultRecord).GetLastChange();
		}

		private Point Snap(ref Point Coords)
		{
			var Snapped = (false, false);

			foreach (SearchResult other in Common.OpenQueries)
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
			AddTabToRibbon();

			Common.SearchWindow?.Close();
			Close();
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
