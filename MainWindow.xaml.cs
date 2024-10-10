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

		private void AddressKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
				ConnectAddress.IsOpen = false;
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

		private void CodePopupClosed(object sender, EventArgs e) => Clipboard.SetText(CodeBox.Text);

		private void CopyCode(object sender, RoutedEventArgs e)
		{
			Clipboard.SetData(DataFormats.UnicodeText, CodeBox.Text);
			CodePopup.IsOpen = false;
		}

		private void DatabaseBackup(object sender, RoutedEventArgs e) => Common.CurrentDatabase.MakeBackup();

		private void DatabaseClose(object sender, RoutedEventArgs e)
		{
			Common.RemoveDatabase(Common.CurrentDatabase);
			Common.DeferUpdateRecentNotes();
		}

		private void DatabaseConnect(object sender, RoutedEventArgs e)
		{
			ConnectAddress.IsOpen = true;
			AddressBox.Text = string.Empty;
		}

		private void DatabaseCreate(object sender, RoutedEventArgs e)
		{
			Common.AddDatabase(new());
			DatabasesPanel.SelectedIndex = DatabasesPanel.Items.Count - 1;
			Common.DeferUpdateRecentNotes();
		}

		private void DatabaseDelete(object sender, RoutedEventArgs e)
		{
			if (MessageBox.Show("Are you sure you want to permanently delete this database?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
				return;

			if (File.Exists(Common.CurrentDatabase.DBFile))
				File.Delete(Common.CurrentDatabase.DBFile);

			var BKPath = Path.GetDirectoryName(Common.GetBackupPath(Common.CurrentDatabase));
			if (Directory.Exists(BKPath))
				Directory.Delete(BKPath, true);

			Common.RemoveDatabase(Common.CurrentDatabase);
			Common.DeferUpdateRecentNotes();
		}

		private void DatabaseDisconnect(object sender, RoutedEventArgs e) => Common.RemoveDatabase(Common.CurrentDatabase);

		private void DatabaseOpen(object sender, RoutedEventArgs e)
		{
			string dbFile = Common.DialogFileSelect(filterIndex: 2);
			if (dbFile.Equals(string.Empty))
				return;

			var path = Path.GetFullPath(dbFile);

			if (Common.DatabaseFiles.Contains(path))
			{
				var items = DatabasesPanel.Items.Cast<TabItem>().ToList();
				var predicate = new Predicate<TabItem>(item => {
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
			DatabaseNameBox.Focus();
			DatabaseNameBox.CaretIndex = DatabaseNameBox.Text?.Length ?? 0;
		}

		private void DatabaseSaveAs(object sender, RoutedEventArgs e)
		{
			var newPath = Common.DialogFileSelect(true, 2, Common.CurrentDatabase.Name);
			if (!newPath.Equals(string.Empty))
				Common.CurrentDatabase.DBFile = newPath;
		}

		private void DatabaseSaveLocal(object sender, RoutedEventArgs e) => Common.CurrentDatabase.DBFile = Path.Join(Common.DocumentsSubfolders["Databases"], Path.GetFileNameWithoutExtension(Common.CurrentDatabase.DBFile), Path.GetFileName(Common.CurrentDatabase.DBFile));

		private void DatabaseServe(object sender, RoutedEventArgs e) => Common.CurrentDatabase.Server?.Serve(0);

		private void DatabaseUnserve(object sender, RoutedEventArgs e) => Common.CurrentDatabase.Server?.Close();

		private void Drag(object sender, MouseButtonEventArgs e) => DragMove();

		private void ExitComplete(object? sender, RunWorkerCompletedEventArgs e)
		{
			CloseOnce = false;
			Common.DatabaseChanged = false;
			MainGrid.IsEnabled = true;
			SaveUserSettings();

			if (Common.ForceClose)
				Application.Current.Shutdown();
		}

		private static void LoadUserSettings()
		{
			if (!File.Exists(Common.SettingsFile))
				return;

			string[] settings = File.ReadAllLines(Common.SettingsFile);

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
					case "RecentNotesSortMode":
						Common.RecentEntriesSortMode = (NoteController.SortType)int.Parse(keyValue[1]);
						break;
					case "LastDatabases":
						FirstRun = false;
						var files = keyValue[1].Split(';').Distinct().Where(File.Exists);
						foreach (var file in files)
							Database.Create(file, true);
						if (!files.Any())
							Database.Create(Path.Join(Common.DocumentsSubfolders["Databases"], $"{Common.DefaultDatabase}", $"{Common.DefaultDatabase}.sidb"));
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

			foreach (var folder in Common.DocumentsSubfolders)
				if (!Directory.Exists(folder.Value))
					Directory.CreateDirectory(folder.Value);

			if (FirstRun)
				Database.Create(Path.Join(Common.DocumentsSubfolders["Databases"], $"{Common.DefaultDatabase}", $"{Common.DefaultDatabase}.sidb"));

			DatabasesPanel.SelectedIndex = 0;

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
				Common.CurrentDatabase.CreateRecord(box.Text);
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
				Common.CurrentDatabase.DeleteRecord(record.Index);
			}
			else
				Common.CurrentDatabase.DeleteRecord(RecentSelection.Index);

			Common.DeferUpdateRecentNotes();
		}

		private void NoteOpen(object sender, RoutedEventArgs e)
		{
			var item = (MenuItem)sender;
			var menu = (ContextMenu)item.Parent;
			SearchResult result;
			if (menu.DataContext.GetType() == typeof(NoteRecord))
			{
				var record = (NoteRecord)menu.DataContext;
				result = Common.OpenQuery(record, false);
			}
			else
				result = Common.OpenQuery(RecentSelection, false);

			result.AddTabToRibbon();
		}

		private void RenameClosed(object sender, EventArgs e)
		{
			if (DatabaseNameBox.Text.Equals(string.Empty))
				return;

			if (DatabaseNameBox.Text.Equals(Common.CurrentDatabase.Name))
				return;

			foreach (Database db in Common.Databases)
			{
				if (DatabaseNameBox.Text.Equals(db.Name))
				{
					MessageBox.Show("A database already exists with the provided name.", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
					return;
				}
			}

			Common.CurrentDatabase.Changed = true;
			var oldName = Common.CurrentDatabase.Name;
			Common.CurrentDatabase.Name = DatabaseNameBox.Text;
			var overwrite = false;

			var oldFile = Common.CurrentDatabase.DBFile;
			var oldPath = Path.GetDirectoryName(oldFile);
			Common.CurrentDatabase.DBFile = Common.GetDatabasePath(Common.CurrentDatabase);
			var newFile = Common.CurrentDatabase.DBFile;
			var newPath = Path.GetDirectoryName(newFile);

			var currentTab = (TabItem)DatabasesPanel.SelectedItem;
			currentTab.Header = Common.CurrentDatabase.GetHeader();

			if (!File.Exists(oldFile))
				return;

			if (!Directory.Exists(oldPath))
				return;

			var directorySearch = Directory.GetDirectories(Common.DocumentsSubfolders["Databases"], "*", new EnumerationOptions() { IgnoreInaccessible = true, RecurseSubdirectories = true, MaxRecursionDepth = 3 });
			if (oldPath is not null && newPath is not null && directorySearch.Contains(oldPath))
			{
				if (Directory.Exists(newPath))
				{
					if (MessageBox.Show($"A database with that name already exists in {newPath}.\n\nDo you want to overwrite it?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
					{
						Common.CurrentDatabase.DBFile = oldFile;
						Common.CurrentDatabase.Name = oldName;
						return;
					}
					Directory.Delete(newPath, true);
					overwrite = true;
				}	
				else
					Directory.Move(oldPath, newPath);
			}

			var adjustedPath = Path.Join(Path.GetDirectoryName(newFile), Path.GetFileName(oldFile));

			if (File.Exists(adjustedPath))
			{
				if (File.Exists(newFile) && !overwrite)
				{
					if (MessageBox.Show($"A database with that name already exists at {newFile}.\n\nDo you want to overwrite it?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
					{
						Common.CurrentDatabase.DBFile = oldFile;
						Common.CurrentDatabase.Name = oldName;
						return;
					}
					overwrite = true;
				}
				if (File.Exists(newFile) && overwrite)
					File.Delete(newFile);
				File.Move(adjustedPath, newFile);
			}
		}

		private void RenameKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
				RenameDatabase.IsOpen = false;
		}

		private void SaveAddress(object sender, RoutedEventArgs e)
		{
			ConnectAddress.IsOpen = false;

			var addr = AddressBox.Text;
			BackgroundWorker worker = new();
			worker.DoWork += (_, _) => Common.CurrentDatabase.Client?.Connect(addr);
			worker.RunWorkerAsync();
		}

		private void SaveDatabases(object? sender, DoWorkEventArgs e)
		{
			foreach (Database db in Common.Databases)
				db.Save();

			Common.ForceClose = true;
		}

		private void SaveNewName(object? sender, RoutedEventArgs e) => RenameDatabase.IsOpen = false;

		private static void SaveUserSettings()
		{
			var files = Common.DatabaseFiles.Distinct().Where(File.Exists);

			string[] settings = [
				$"FontFamily:{Common.Settings.MainFontFamily?.Source}",
				$"FontSize:{Common.Settings.MainFontSize}",
				$"SearchResultsOnTop:{Common.Settings.SearchResultsOnTop}",
				$"RibbonDisplayMode:{Common.RibbonTabContent}",
				$"RecentNotesSortMode:{(int)Common.RecentEntriesSortMode}",
				$"LastDatabases:{string.Join(';', files)}",
				$"MenuForeground:{Common.BytesFromBrush(Common.Settings.MenuForeground)}",
				$"MenuBackground:{Common.BytesFromBrush(Common.Settings.MenuBackground)}",
				$"ListForeground:{Common.BytesFromBrush(Common.Settings.ListForeground)}",
				$"ListBackground:{Common.BytesFromBrush(Common.Settings.ListBackground)}",
				$"AccentForeground:{Common.BytesFromBrush(Common.Settings.AccentForeground)}",
				$"AccentBackground:{Common.BytesFromBrush(Common.Settings.AccentBackground)}"
			];

			File.WriteAllLines(Common.SettingsFile, settings);
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
				if (item is null)
					return;

				var newDB = (Database)item.Tag;
				if (newDB.Equals(Common.CurrentDatabase))
					return;

				Common.CurrentDatabase = newDB;
				Common.Settings.SearchResults.Clear();
				Common.UpdateContextMenu();
				Common.DeferUpdateRecentNotes(true);
			}
		}
	}
}
