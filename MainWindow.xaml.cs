using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private bool CloseOnce = false;
		private static bool FirstRun = true;
		private NoteRecord RecentSelection = new();

		public MainWindow()
		{
			InitializeComponent();
			DataContext = Common.Settings;
		}

		private void Button_Click(object sender, RoutedEventArgs e)
		{
			var senderObject = (Button)sender;

			switch (senderObject.Content)
			{
				case "Import":
					Common.Settings.ImportTarget = string.Empty;
					Common.Settings.ReadyToFinalize = false;

					Common.ImportWindow = new();
					break;
				case "Replace":
					Common.Settings.NumReplacements = string.Empty;

					Common.ReplaceWindow = new();
					break;
				case "Search":
					Common.SearchWindow = new();
					break;
				case "Settings":
					Common.SettingsWindow = new();
					break;
				case "Exit":
					Close();
					break;
			}
		}

		private void DatabaseBackup(object sender, RoutedEventArgs e)
		{
			Common.CurrentDatabase.MakeBackup();
		}

		private void DatabaseClose(object sender, RoutedEventArgs e)
		{
			Common.RemoveDatabase(Common.CurrentDatabase);
			Common.DeferUpdateRecentNotes();
		}

		private void DatabaseCreate(object sender, RoutedEventArgs e)
		{
			Database db = new();
			if (db.Loaded)
				Common.AddDatabase(db);
			DatabasesPanel.SelectedIndex = DatabasesPanel.Items.Count - 1;
			Common.DeferUpdateRecentNotes();
		}

		private void DatabaseDelete(object sender, RoutedEventArgs e)
		{
			if (MessageBox.Show("Are you sure you want to permanently delete this database?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
				return;

			if (File.Exists(Common.CurrentDatabase.DBFile))
				File.Delete(Common.CurrentDatabase.DBFile);
			Common.RemoveDatabase(Common.CurrentDatabase);
			Common.DeferUpdateRecentNotes();
		}

		private void DatabaseOpen(object sender, RoutedEventArgs e)
		{
			string dbFile = Common.DialogFileSelect(filterIndex: 2);
			if (dbFile.Equals(string.Empty))
				return;

			var path = Path.GetFullPath(dbFile);

			if (Common.DatabaseFiles.Contains(path))
			{
				var items = DatabasesPanel.Items.Cast<TabItem>().ToList();
				var predicate = new Predicate<TabItem>((item) => {
					var innerDB = (Database)item.Tag;
					return Path.GetFullPath(innerDB.DBFile).Equals(path);
				});
				var db = items?.FindIndex(predicate);
				DatabasesPanel.SelectedIndex = db ?? DatabasesPanel.SelectedIndex;
				return;
			}

			Database.Create(dbFile, true);
			Common.DeferUpdateRecentNotes();
		}

		private void DatabaseRename(object sender, RoutedEventArgs e)
		{
			RenameDatabase.IsOpen = true;
			DatabaseNameBox.Text = Common.CurrentDatabase.Name;
		}

		private void DatabaseSaveAs(object sender, RoutedEventArgs e)
		{
			var newPath = Common.DialogFileSelect(true, 2, Common.CurrentDatabase.Name);
			if (!newPath.Equals(string.Empty))
				Common.CurrentDatabase.DBFile = newPath;
		}

		private void Drag(object sender, MouseButtonEventArgs e) => DragMove();

		private void ExitComplete(object? sender, RunWorkerCompletedEventArgs e)
		{
			CloseOnce = false;
			Common.DatabaseChanged = false;
			MainGrid.IsEnabled = true;

			if (Common.ForceClose)
			{
				SaveUserSettings();
				Application.Current.Shutdown();
			}
		}

		private static void LoadUserSettings()
		{
			if (!File.Exists("settings.sis"))
				return;

			string[] settings = File.ReadAllLines("settings.sis");

			for (int i = 0; i < settings.Length; i++)
			{
				var setting = settings[i].Trim();
				var keyValue = setting.Split(':', 2);
				switch (keyValue[0])
				{
					case "FontFamily":
						Common.Settings.MainFontFamily = new(keyValue[1]);
						break;
					case "FontSize":
						Common.Settings.MainFontSize = double.Parse(keyValue[1]);
						break;
					case "SearchResultsOnTop":
						Common.Settings.SearchResultsOnTop = bool.Parse(keyValue[1]);
						break;
					case "RibbonDisplayMode":
						Common.RibbonTabContent = keyValue[1];
						break;
					case "LastDatabases":
						FirstRun = false;
						var files = keyValue[1].Split(';').Distinct().Where(File.Exists);
						foreach (var file in files)
							Database.Create(file, true);
						if (!files.Any())
							Database.Create($"{Common.DefaultDatabase}.sidb");
						break;
					case "MenuForeground":
						Common.Settings.MenuForeground = Common.BrushFromBytes(keyValue[1]);
						break;
					case "MenuBackground":
						Common.Settings.MenuBackground = Common.BrushFromBytes(keyValue[1]);
						break;
					case "ListForeground":
						Common.Settings.ListForeground = Common.BrushFromBytes(keyValue[1]);
						break;
					case "ListBackground":
						Common.Settings.ListBackground = Common.BrushFromBytes(keyValue[1]);
						break;
					case "AccentForeground":
						Common.Settings.AccentForeground = Common.BrushFromBytes(keyValue[1]);
						break;
					case "AccentBackground":
						Common.Settings.AccentBackground = Common.BrushFromBytes(keyValue[1]);
						break;
					default:
						break;
				}
			}
		}

		private void MainWindow_Closing(object sender, CancelEventArgs e)
		{
			if (!Common.ForceClose)
			{
				if (CloseOnce)
					return;

				if (Common.DatabaseChanged)
				{
					switch (MessageBox.Show("Do you want to save your work before exiting?", "Sylver Ink: Notification", MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
					{
						case MessageBoxResult.Cancel:
							e.Cancel = true;
							return;
						case MessageBoxResult.No:
							Common.DatabaseChanged = false;
							break;
					}
				}
			}

			CloseOnce = true;

			if (Common.DatabaseChanged)
			{
				e.Cancel = true;
				MainGrid.IsEnabled = false;

				BackgroundWorker exitTask = new();
				exitTask.DoWork += SaveDatabases;
				exitTask.RunWorkerCompleted += ExitComplete;
				exitTask.RunWorkerAsync();

				return;
			}

			SaveUserSettings();
			Application.Current.Shutdown();
		}

		private void MainWindow_Loaded(object sender, RoutedEventArgs e)
		{
			LoadUserSettings();

			DatabasesPanel.SelectedIndex = 0;

			if (FirstRun)
				Database.Create($"{Common.DefaultDatabase}.sidb");

			BackgroundWorker worker = new();
			worker.DoWork += (_, _) => SpinWait.SpinUntil(() => Common.Databases.Count > 0);
			worker.RunWorkerCompleted += (_, _) =>
			{
				Common.CanResize = true;
				Common.Settings.MainTypeFace = new(Common.Settings.MainFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
				Common.PPD = VisualTreeHelper.GetDpi(this).PixelsPerDip;
				Common.DeferUpdateRecentNotes(true);
			};
			worker.RunWorkerAsync();
		}

		private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e) => Common.DeferUpdateRecentNotes();

		private void NewNote(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
			{
				var box = (TextBox)sender;
				Common.CurrentDatabase.Controller.CreateRecord(box.Text);
				Common.DeferUpdateRecentNotes();
				box.Text = string.Empty;
			}
		}

		private void NoteDelete(object sender, RoutedEventArgs e)
		{
			var item = (MenuItem)sender;
			var menu = (ContextMenu)item.Parent;
			if (menu.DataContext.GetType() == typeof(NoteRecord))
			{
				var record = (NoteRecord)menu.DataContext;
				Common.CurrentDatabase.Controller.DeleteRecord(record.GetIndex());
				Common.DeferUpdateRecentNotes();
			}
			else if (menu.DataContext.GetType() == typeof(ContextSettings))
			{
				Common.CurrentDatabase.Controller.DeleteRecord(RecentSelection.GetIndex());
				Common.DeferUpdateRecentNotes();
			}
		}

		private void NoteOpen(object sender, RoutedEventArgs e)
		{
			var item = (MenuItem)sender;
			var menu = (ContextMenu)item.Parent;
			if (menu.DataContext.GetType() == typeof(NoteRecord))
			{
				var record = (NoteRecord)menu.DataContext;
				SearchResult result = Common.OpenQuery(record, false);
				result.AddTabToRibbon();
			}
			else if (menu.DataContext.GetType() == typeof(ContextSettings))
			{
				SearchResult result = Common.OpenQuery(RecentSelection, false);
				result.AddTabToRibbon();
			}
		}

		private void RenameClosed(object sender, EventArgs e)
		{
			if (DatabaseNameBox.Text.Equals(string.Empty))
				return;

			if (Common.CurrentDatabase.Name != DatabaseNameBox.Text)
				Common.DatabaseChanged = true;

			Common.CurrentDatabase.Name = DatabaseNameBox.Text;
			var currentTab = (TabItem)DatabasesPanel.SelectedItem;
			currentTab.Header = Common.CurrentDatabase.Name;
			currentTab.ToolTip = Common.CurrentDatabase.Name;
		}

		private void RenameKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
				RenameDatabase.IsOpen = false;
		}

		private static void SaveDatabase(Database db)
		{
			db.Save();
		}

		private void SaveDatabases(object? sender, DoWorkEventArgs e)
		{
			foreach (Database db in Common.Databases)
				SaveDatabase(db);

			Common.ForceClose = true;
		}

		private void SaveNewName(object? sender, RoutedEventArgs e)
		{
			RenameDatabase.IsOpen = false;
		}

		private static void SaveUserSettings()
		{
			var files = Common.DatabaseFiles.Distinct().Where(File.Exists);

			string[] settings = [
				$"FontFamily:{Common.Settings.MainFontFamily?.Source}",
				$"FontSize:{Common.Settings.MainFontSize}",
				$"SearchResultsOnTop:{Common.Settings.SearchResultsOnTop}",
				$"RibbonDisplayMode:{Common.RibbonTabContent}",
				$"LastDatabases:{string.Join(';', files)}",
				$"MenuForeground:{Common.BytesFromBrush(Common.Settings.MenuForeground)}",
				$"MenuBackground:{Common.BytesFromBrush(Common.Settings.MenuBackground)}",
				$"ListForeground:{Common.BytesFromBrush(Common.Settings.ListForeground)}",
				$"ListBackground:{Common.BytesFromBrush(Common.Settings.ListBackground)}",
				$"AccentForeground:{Common.BytesFromBrush(Common.Settings.AccentForeground)}",
				$"AccentBackground:{Common.BytesFromBrush(Common.Settings.AccentBackground)}"
			];

			File.WriteAllLines("settings.sis", settings);
		}

		private void SublistChanged(object sender, RoutedEventArgs e)
		{
			var box = (ListBox)sender;
			var grid = (Grid)box.Parent;
			RecentSelection = (NoteRecord)box.SelectedItem;

			foreach (ListBox item in grid.Children)
			{
				if (item.SelectedIndex != box.SelectedIndex)
					item.SelectedIndex = box.SelectedIndex;
			}
		}

		private void SublistOpen(object sender, RoutedEventArgs e)
		{
			if (Mouse.RightButton == MouseButtonState.Pressed)
				return;

			var box = (ListBox)sender;
			if (box.SelectedItem is null)
				return;

			Common.OpenQuery(RecentSelection);
			box.SelectedItem = null;
		}

		private void TabChanged(object sender, SelectionChangedEventArgs e)
		{
			var control = (TabControl)sender;
			if (control.Name.Equals("DatabasesPanel"))
			{
				var item = (TabItem)control.SelectedItem;
				var newDB = (Database)item.Tag;
				if (newDB.Equals(Common.CurrentDatabase))
					return;
				Common.CurrentDatabase = newDB;
				Common.Settings.SearchResults.Clear();
				Common.DeferUpdateRecentNotes(true);
			}
		}
	}
}
