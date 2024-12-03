using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using static SylverInk.Common;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		[DllImport("User32.dll")]
		private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

		[DllImport("User32.dll")]
		private static extern bool UnregisterHotKey(nint hWnd, int id);

		private static bool FirstRun = true;
		private const int HotKeyID = 5192;
		private NoteRecord RecentSelection = new();
		private HwndSource? WindowSource;

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
					ImportWindow = new();
					break;
				case "Replace":
					ReplaceWindow = new();
					break;
				case "Search":
					SearchWindow = new();
					break;
				case "Settings":
					SettingsWindow = new();
					break;
				case "Exit":
					Close();
					break;
			}
		}

		private void CodePopupClosed(object sender, EventArgs e) => Clipboard.SetText(CodeBox.Text);

		private void CopyCode(object sender, RoutedEventArgs e) => CodePopup.IsOpen = false;

		private void DatabaseBackup(object sender, RoutedEventArgs e) => CurrentDatabase.MakeBackup();

		private void DatabaseClose(object sender, RoutedEventArgs e)
		{
			if (CurrentDatabase.Changed)
			{
				var res = MessageBox.Show("Do you want to save your changes?", "Sylver Ink: Warning", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
				if (res == MessageBoxResult.Cancel)
					return;
				if (res == MessageBoxResult.Yes)
					CurrentDatabase.Save();
			}

			RemoveDatabase(CurrentDatabase);
			DeferUpdateRecentNotes();
		}

		private void DatabaseConnect(object sender, RoutedEventArgs e)
		{
			ConnectAddress.IsOpen = true;
			AddressBox.Text = string.Empty;
		}

		private void DatabaseCreate(object sender, RoutedEventArgs e)
		{
			AddDatabase(new());
			DatabasesPanel.SelectedIndex = DatabasesPanel.Items.Count - 1;
			DeferUpdateRecentNotes();
		}

		private void DatabaseDelete(object sender, RoutedEventArgs e)
		{
			if (MessageBox.Show("Are you sure you want to permanently delete this database?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
				return;

			if (File.Exists(CurrentDatabase.DBFile))
				File.Delete(CurrentDatabase.DBFile);

			var BKPath = Path.GetDirectoryName(GetBackupPath(CurrentDatabase));
			if (Directory.Exists(BKPath))
				Directory.Delete(BKPath, true);

			RemoveDatabase(CurrentDatabase);
			DeferUpdateRecentNotes();
		}

		private void DatabaseDisconnect(object sender, RoutedEventArgs e)
		{
			CurrentDatabase.Client?.Disconnect();
			CurrentDatabase.Changed = true;
		}

		private void DatabaseOpen(object sender, RoutedEventArgs e)
		{
			string dbFile = DialogFileSelect(filterIndex: 2);
			if (dbFile.Equals(string.Empty))
				return;

			var path = Path.GetFullPath(dbFile);

			if (DatabaseFiles.Contains(path))
			{
				var items = DatabasesPanel.Items.Cast<TabItem>().ToList();
				var db = items?.FindIndex(new(item => {
					var innerDB = (Database)item.Tag;
					return Path.GetFullPath(innerDB.DBFile).Equals(path);
				}));

				DatabasesPanel.SelectedIndex = db ?? DatabasesPanel.SelectedIndex;
				return;
			}

			Database.Create(dbFile, true);
			DeferUpdateRecentNotes();
		}

		private void DatabaseProperties(object sender, RoutedEventArgs e)
		{
			Properties window = new() { DB = CurrentDatabase };
			window.Show();
		}

		private void DatabaseRename(object sender, RoutedEventArgs e)
		{
			RenameDatabase.IsOpen = true;
			DatabaseNameBox.Text = CurrentDatabase.Name;
			DatabaseNameBox.Focus();
			DatabaseNameBox.CaretIndex = DatabaseNameBox.Text?.Length ?? 0;
		}

		private void DatabaseSaveAs(object sender, RoutedEventArgs e)
		{
			var newPath = DialogFileSelect(true, 2, CurrentDatabase.Name);
			if (!newPath.Equals(string.Empty))
				CurrentDatabase.DBFile = newPath;
			CurrentDatabase.Format = HighestFormat;
		}

		private void DatabaseSaveLocal(object sender, RoutedEventArgs e)
		{
			CurrentDatabase.DBFile = Path.Join(Subfolders["Databases"], Path.GetFileNameWithoutExtension(CurrentDatabase.DBFile), Path.GetFileName(CurrentDatabase.DBFile));
			CurrentDatabase.Format = HighestFormat;
		}

		private void DatabaseServe(object sender, RoutedEventArgs e) => CurrentDatabase.Server?.Serve(0);

		private void DatabaseUnserve(object sender, RoutedEventArgs e) => CurrentDatabase.Server?.Close();

		private void Drag(object sender, MouseButtonEventArgs e) => DragMove();

		private void ExitComplete(object? sender, RunWorkerCompletedEventArgs e)
		{
			DatabaseChanged = false;
			MainGrid.IsEnabled = true;
			SaveUserSettings();
			Application.Current.Shutdown();
		}

		private nint HwndHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
		{
			if (msg != 0x0312) // WM_HOTKEY
				return default;

			if (wParam.ToInt32() != HotKeyID)
				return default;

			OnHotKeyPressed();
			handled = true;
			return default;
		}

		private static void LoadUserSettings()
		{
			if (!File.Exists(SettingsFile))
				return;

			string[] settings = File.ReadAllLines(SettingsFile);

			for (int i = 0; i < settings.Length; i++)
			{
				var setting = settings[i].Trim();
				var keyValue = setting.Split(':', 2);
				switch (keyValue[0])
				{
					case "AccentBackground":
						Common.Settings.AccentBackground = BrushFromBytes(keyValue[1]);
						break;
					case "AccentForeground":
						Common.Settings.AccentForeground = BrushFromBytes(keyValue[1]);
						break;
					case "FontFamily":
						Common.Settings.MainFontFamily = new(keyValue[1]);
						break;
					case "FontSize":
						Common.Settings.MainFontSize = double.Parse(keyValue[1]);
						break;
					case "LastDatabases":
						FirstRun = false;
						var files = keyValue[1].Split(';').Distinct().Where(File.Exists);
						foreach (var file in files)
							Database.Create(file, true);
						if (!files.Any())
							Database.Create(Path.Join(Subfolders["Databases"], $"{DefaultDatabase}", $"{DefaultDatabase}.sidb"));
						break;
					case "ListBackground":
						Common.Settings.ListBackground = BrushFromBytes(keyValue[1]);
						break;
					case "ListForeground":
						Common.Settings.ListForeground = BrushFromBytes(keyValue[1]);
						break;
					case "MenuBackground":
						Common.Settings.MenuBackground = BrushFromBytes(keyValue[1]);
						break;
					case "MenuForeground":
						Common.Settings.MenuForeground = BrushFromBytes(keyValue[1]);
						break;
					case "RecentNotesSortMode":
						if (!int.TryParse(keyValue[1], out var sortMode))
							sortMode = 0;
						RecentEntriesSortMode = (SortType)sortMode;
						break;
					case "RibbonDisplayMode":
						if (!int.TryParse(keyValue[1], out var displayMode))
							displayMode = 0;
						RibbonTabContent = (DisplayType)displayMode;
						break;
					case "SearchResultsOnTop":
						Common.Settings.SearchResultsOnTop = bool.Parse(keyValue[1]);
						break;
					case "SnapSearchResults":
						Common.Settings.SnapSearchResults = bool.Parse(keyValue[1]);
						break;
					default:
						break;
				}
			}
		}

		private void MainWindow_Closing(object sender, CancelEventArgs e)
		{
			if (DatabaseChanged)
			{
				switch (MessageBox.Show("Do you want to save your work before exiting?", "Sylver Ink: Notification", MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
				{
					case MessageBoxResult.Cancel:
						e.Cancel = true;
						return;
					case MessageBoxResult.Yes:
						e.Cancel = true;
						MainGrid.IsEnabled = false;

						BackgroundWorker exitTask = new();
						exitTask.DoWork += SaveDatabases;
						exitTask.RunWorkerCompleted += ExitComplete;
						exitTask.RunWorkerAsync();
						return;
				}
			}

			SaveUserSettings();
			Application.Current.Shutdown();
		}

		private void MainWindow_Loaded(object sender, RoutedEventArgs e)
		{
			LoadUserSettings();

			foreach (var folder in Subfolders)
				if (!Directory.Exists(folder.Value))
					Directory.CreateDirectory(folder.Value);

			if (FirstRun)
				Database.Create(Path.Join(Subfolders["Databases"], $"{DefaultDatabase}", $"{DefaultDatabase}.sidb"));

			DatabasesPanel.SelectedIndex = 0;

			BackgroundWorker worker = new();
			worker.DoWork += (_, _) => SpinWait.SpinUntil(new(() => Databases.Count > 0));
			worker.RunWorkerCompleted += (_, _) =>
			{
				CanResize = true;
				ResizeMode = ResizeMode.CanResize;
				Common.Settings.MainTypeFace = new(Common.Settings.MainFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
				PPD = VisualTreeHelper.GetDpi(this).PixelsPerDip;
				DeferUpdateRecentNotes(true);
			};
			worker.RunWorkerAsync();
		}

		private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e) => DeferUpdateRecentNotes(true);

		private void NewNote_Keydown(object sender, KeyEventArgs e)
		{
			if (e.Key != Key.Enter)
				return;

			var box = (TextBox)sender;
			CurrentDatabase.CreateRecord(box.Text);
			box.Text = string.Empty;
			DeferUpdateRecentNotes();
		}

		protected override void OnClosed(EventArgs e)
		{
			WindowSource?.RemoveHook(HwndHook);
			WindowSource = null;
			UnregisterHotKey();
			base.OnClosed(e);
		}

		private void OnHotKeyPressed()
		{
			var lastRecord = CurrentDatabase.GetRecord(0);
			if (lastRecord.ToString().Equals(string.Empty))
			{
				OpenQuery(lastRecord);
				return;
			}

			lastRecord = CurrentDatabase.GetRecord(CurrentDatabase.RecordCount - 1);
			if (lastRecord.ToString().Equals(string.Empty))
			{
				OpenQuery(lastRecord);
				return;
			}

			int entry = CurrentDatabase.CreateRecord(string.Empty);
			OpenQuery(CurrentDatabase.GetRecord(entry));
		}

		protected override void OnSourceInitialized(EventArgs e)
		{
			base.OnSourceInitialized(e);
			var helper = new WindowInteropHelper(this);
			WindowSource = HwndSource.FromHwnd(helper.Handle);
			WindowSource.AddHook(HwndHook);
			RegisterHotKey();
		}

		private void RegisterHotKey()
		{
			var helper = new WindowInteropHelper(this);
			if (!RegisterHotKey(helper.Handle, HotKeyID, 2, (uint)KeyInterop.VirtualKeyFromKey(Key.N)))
				MessageBox.Show("Failed to register hotkey.", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
		}

		private void RenameClosed(object sender, EventArgs e)
		{
			if (DatabaseNameBox.Text.Equals(string.Empty))
				return;

			if (DatabaseNameBox.Text.Equals(CurrentDatabase.Name))
				return;

			foreach (Database db in Databases)
			{
				if (!DatabaseNameBox.Text.Equals(db.Name))
					continue;

				MessageBox.Show("A database already exists with the provided name.", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			CurrentDatabase.Changed = true;
			var oldName = CurrentDatabase.Name;
			CurrentDatabase.Name = DatabaseNameBox.Text;
			var overwrite = false;

			var oldFile = CurrentDatabase.DBFile;
			var oldPath = Path.GetDirectoryName(oldFile);
			CurrentDatabase.DBFile = GetDatabasePath(CurrentDatabase);
			var newFile = CurrentDatabase.DBFile;
			var newPath = Path.GetDirectoryName(newFile);

			var currentTab = (TabItem)DatabasesPanel.SelectedItem;
			currentTab.Header = CurrentDatabase.GetHeader();

			if (!File.Exists(oldFile))
				return;

			if (!Directory.Exists(oldPath))
				return;

			var directorySearch = Directory.GetDirectories(Subfolders["Databases"], "*", new EnumerationOptions() { IgnoreInaccessible = true, RecurseSubdirectories = true, MaxRecursionDepth = 3 });
			if (oldPath is not null && newPath is not null && directorySearch.Contains(oldPath))
			{
				if (Directory.Exists(newPath))
				{
					if (MessageBox.Show($"A database with that name already exists in {newPath}.\n\nDo you want to overwrite it?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
					{
						CurrentDatabase.DBFile = oldFile;
						CurrentDatabase.Name = oldName;
						currentTab.Header = CurrentDatabase.GetHeader();
						return;
					}
					Directory.Delete(newPath, true);
					overwrite = true;
				}	
				else
					Directory.Move(oldPath, newPath);
			}

			var adjustedPath = Path.Join(Path.GetDirectoryName(newFile), Path.GetFileName(oldFile));

			if (!File.Exists(adjustedPath))
				return;

			if (File.Exists(newFile) && !overwrite)
			{
				if (MessageBox.Show($"A database with that name already exists at {newFile}.\n\nDo you want to overwrite it?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
				{
					CurrentDatabase.DBFile = oldFile;
					CurrentDatabase.Name = oldName;
					currentTab.Header = CurrentDatabase.GetHeader();
					return;
				}
				overwrite = true;
			}

			if (File.Exists(newFile) && overwrite)
				File.Delete(newFile);

			File.Move(adjustedPath, newFile);
		}

		private void RenameKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
				RenameDatabase.IsOpen = false;
		}

		private void SaveAddress(object sender, RoutedEventArgs e)
		{
			ConnectAddress.IsOpen = false;

			Database newDB = new();
			AddDatabase(newDB);

			var addr = AddressBox.Text;
			BackgroundWorker worker = new();
			worker.DoWork += (_, _) => newDB.Client?.Connect(addr);
			worker.RunWorkerAsync();
		}

		private void SaveDatabases(object? sender, DoWorkEventArgs e)
		{
			foreach (Database db in Databases)
				db.Save();
		}

		private void SaveNewName(object? sender, RoutedEventArgs e) => RenameDatabase.IsOpen = false;

		private static void SaveUserSettings()
		{
			string[] settings = [
				$"AccentBackground:{BytesFromBrush(Common.Settings.AccentBackground)}",
				$"AccentForeground:{BytesFromBrush(Common.Settings.AccentForeground)}",
				$"FontFamily:{Common.Settings.MainFontFamily?.Source}",
				$"FontSize:{Common.Settings.MainFontSize}",
				$"LastDatabases:{string.Join(';', DatabaseFiles.Distinct().Where(File.Exists))}",
				$"ListBackground:{BytesFromBrush(Common.Settings.ListBackground)}",
				$"ListForeground:{BytesFromBrush(Common.Settings.ListForeground)}",
				$"MenuBackground:{BytesFromBrush(Common.Settings.MenuBackground)}",
				$"MenuForeground:{BytesFromBrush(Common.Settings.MenuForeground)}",
				$"RecentNotesSortMode:{(int)RecentEntriesSortMode}",
				$"RibbonDisplayMode:{(int)RibbonTabContent}",
				$"SearchResultsOnTop:{Common.Settings.SearchResultsOnTop}",
				$"SnapSearchResults:{Common.Settings.SnapSearchResults}",
			];

			File.WriteAllLines(SettingsFile, settings);
		}

		private void SublistChanged(object sender, RoutedEventArgs e)
		{
			var box = (ListBox)sender;
			var grid = (Grid)box.Parent;
			RecentSelection = (NoteRecord)box.SelectedItem;

			foreach (ListBox item in grid.Children)
				item.SelectedIndex = box.SelectedIndex;
		}

		private void SublistOpen(object sender, RoutedEventArgs e)
		{
			if (Mouse.RightButton == MouseButtonState.Pressed)
				return;

			var box = (ListBox)sender;
			if (box.SelectedItem is null)
				return;

			OpenQuery(RecentSelection);
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
				if (newDB.Equals(CurrentDatabase))
					return;

				CurrentDatabase = newDB;
				Common.Settings.SearchResults.Clear();
			}

			UpdateContextMenu();
			DeferUpdateRecentNotes(true);
		}

		private void UnregisterHotKey()
		{
			var helper = new WindowInteropHelper(this);
			UnregisterHotKey(helper.Handle, HotKeyID);
		}
	}
}
