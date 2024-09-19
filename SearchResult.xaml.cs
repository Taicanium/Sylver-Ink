using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for SearchResult.xaml
	/// </summary>
	public partial class SearchResult : Window
	{
		private readonly NoteSettings _noteSettings = new();

		public NoteSettings NoteSettings { get { return _noteSettings; } }
		public string Query { get; set; } = string.Empty;
		public int ResultRecord { get; set; } = -1;
		public string ResultText { get; set; } = string.Empty;

		public SearchResult()
		{
			InitializeComponent();
			DataContext = NoteSettings;
		}

		private void AddTabToRibbon()
		{
			var tabPanel = (TabControl)Application.Current.MainWindow.FindName("MainTabPanel");

			foreach (TabItem item in tabPanel.Items)
			{
				if (ResultRecord == (int)(item.Tag ?? -1))
				{
					tabPanel.SelectedIndex = tabPanel.Items.IndexOf(item);
					Close();
					return;
				}
			}

			int abbrevLen = Math.Min(13, Query.Length);
			string abbrev = Query.Length >= 13 ? $"{Query[..10]}..." : Query;

			TabItem newTab = new()
			{
				Header = abbrev.Replace("\t", " ").Replace("\r", string.Empty).Replace("\n", " "),
				Tag = ResultRecord
			};

			tabPanel.SelectedIndex = tabPanel.Items.Add(newTab);

			Grid grid = new() { Margin = new(2.0, 2.0, 2.0, 2.0), };

			grid.RowDefinitions.Add(new() { Height = GridLength.Auto, });
			grid.RowDefinitions.Add(new() { Height = new(1.0, GridUnitType.Star) });
			grid.RowDefinitions.Add(new() { Height = GridLength.Auto, });

			Label revisionLabel = new()
			{
				Foreground = Brushes.Gray,
				Margin = new(0.0, 0.0, 10.0, 0.0),
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Bottom,
				FontStyle = FontStyles.Italic,
				Content = "Entry last modified: " + NoteController.GetRecord(ResultRecord).GetLastChange()
			};

			TextBox noteBox = new()
			{
				TextWrapping = TextWrapping.WrapWithOverflow,
				Background = Brushes.White,
				Height = 20,
				Margin = new(5.0),
				AcceptsReturn = true,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				Text = ResultText,
				Tag = (0U, ResultRecord)
			};

			Button nextButton = new()
			{
				Content = "\u2192",
				Width = 50,
				HorizontalAlignment = HorizontalAlignment.Left,
			};

			Button previousButton = new()
			{
				Content = "\u2190",
				Width = 50,
				HorizontalAlignment = HorizontalAlignment.Right,
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
			previousButton.IsEnabled = NoteController.GetRecord(ResultRecord).GetNumRevisions() > 0;
			saveButton.IsEnabled = false;

			noteBox.TextChanged += (sender, e) =>
			{
				var senderObject = (TextBox)sender;
				var tag = ((uint, int))senderObject.Tag;
				var record = NoteController.GetRecord(tag.Item2);
				saveButton.IsEnabled = !senderObject.Text.Equals(record.ToString());
			};

			nextButton.Click += (sender, e) =>
			{
				var senderObject = (Button)sender;
				var tabPanel = (TabControl)Application.Current.MainWindow.FindName("MainTabPanel");
				var tag = ((uint, int))noteBox.Tag;
				var record = NoteController.GetRecord(tag.Item2);
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

			previousButton.Click += (sender, e) =>
			{
				var senderObject = (Button)sender;
				var tabPanel = (TabControl)Application.Current.MainWindow.FindName("MainTabPanel");
				var tag = ((uint, int))noteBox.Tag;
				var record = NoteController.GetRecord(tag.Item2);
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

			returnButton.Click += (sender, e) =>
			{
				var senderObject = (Button)sender;
				var tabPanel = (TabControl)Application.Current.MainWindow.FindName("MainTabPanel");
				var tag = ((uint, int))noteBox.Tag;
				var tabIndex = (int)senderObject.Tag;

				if (saveButton.IsEnabled && saveButton.Content.Equals("Save"))
				{
					switch (MessageBox.Show("You have unsaved changes. Save before closing this note?", "Sylver Ink: Notification", MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
					{
						case MessageBoxResult.Cancel:
							return;
						case MessageBoxResult.Yes:
							NoteController.CreateRevision(tag.Item2, noteBox.Text);
							Common.DeferUpdateRecentNotes();
							break;
					}
				}

				tabPanel.SelectedIndex = 0;
				tabPanel.Items.RemoveAt(tabIndex);
			};

			deleteButton.Click += (sender, e) =>
			{
				var senderObject = (Button)sender;
				var tabPanel = (TabControl)Application.Current.MainWindow.FindName("MainTabPanel");
				var tag = ((uint, int))noteBox.Tag;
				var tabIndex = (int)senderObject.Tag;

				if (MessageBox.Show("Are you sure you want to permanently delete this note?", "Sylver Ink: Notification", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
					return;

				NoteController.DeleteRecord(tag.Item2);
				tabPanel.SelectedIndex = 0;
				tabPanel.Items.RemoveAt(tabIndex);
				Common.DeferUpdateRecentNotes();
			};

			saveButton.Click += (sender, e) =>
			{
				var senderObject = (Button)sender;
				var tag = ((uint, int))noteBox.Tag;

				NoteController.CreateRevision(tag.Item2, noteBox.Text);
				Common.DeferUpdateRecentNotes();

				noteBox.Tag = (0U, tag.Item2);
				noteBox.IsEnabled = true;
				previousButton.IsEnabled = true;
				nextButton.IsEnabled = false;
				revisionLabel.Content = "Entry last modified: " + NoteController.GetRecord(tag.Item2).GetLastChange();
				senderObject.IsEnabled = false;
			};

			Grid buttonGrid = new()
			{
				Margin = new(0, 40, 0, 80),
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

			noteBox.SetBinding(HeightProperty, new Binding()
			{
				Mode = BindingMode.OneWay,
				Path = new("SearchTabHeight"),
			});
		}

		private void CloseClick(object sender, RoutedEventArgs e)
		{
			if (NoteSettings.Edited)
			{
				var result = MessageBox.Show("You have unsaved changes. Save before closing this note?", "Sylver Ink: Notification", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

				if (result == MessageBoxResult.Cancel)
					return;

				if (result == MessageBoxResult.Yes)
					SaveRecord();
			}

			Close();
		}

		private void Result_Closed(object sender, EventArgs e)
		{
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
			ResultText = NoteController.GetRecord(ResultRecord).ToString();
			ResultBlock.Text = ResultText;
			NoteSettings.LastChanged = "Entry last modified: " + NoteController.GetRecord(ResultRecord).GetLastChange();
			NoteSettings.Edited = false;

			var tabPanel = (TabControl)Application.Current.MainWindow.FindName("MainTabPanel");
			for (int i = tabPanel.Items.Count - 1; i > 0; i--)
			{
				var item = (TabItem)tabPanel.Items[i];

				if ((int?)item.Tag == ResultRecord)
					tabPanel.Items.RemoveAt(i);
			}
		}

		private void ResultBlock_TextChanged(object sender, TextChangedEventArgs e)
		{
			NoteSettings.Edited = true;
			var senderObject = sender as TextBox;
			ResultText = senderObject?.Text ?? string.Empty;
		}

		private void SaveClick(object sender, RoutedEventArgs e)
		{
			SaveRecord();

			Common.DeferUpdateRecentNotes();
			Close();
		}

		private void SaveRecord()
		{
			NoteController.CreateRevision(ResultRecord, ResultText);
			NoteSettings.LastChanged = "Entry last modified: " + NoteController.GetRecord(ResultRecord).GetLastChange();
			NoteSettings.Edited = false;
		}

		private void ViewClick(object sender, RoutedEventArgs e)
		{
			AddTabToRibbon();

			Common.SearchWindow?.Close();
			Close();
		}
	}
}
