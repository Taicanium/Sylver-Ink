using System.ComponentModel;
using System.IO;
using System.Linq;
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
		}

		private void DatabaseCreate(object sender, RoutedEventArgs e)
		{
			Database db = new();
			if (db.Loaded)
				Common.AddDatabase(db);
		}

		private void DatabaseDelete(object sender, RoutedEventArgs e)
		{
			if (MessageBox.Show("Are you sure you want to permanently delete this database?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
				return;

			if (File.Exists(Common.CurrentDatabase.DBFile))
				File.Delete(Common.CurrentDatabase.DBFile);
			Common.RemoveDatabase(Common.CurrentDatabase);
		}

		private void DatabaseOpen(object sender, RoutedEventArgs e)
		{
			string dbFile = Common.DialogFileSelect(filterIndex: 2);
			if (dbFile.Equals(string.Empty))
				return;

			Database db = new(dbFile);
			if (db.Loaded)
				Common.AddDatabase(db);
		}

		private void DatabaseRename(object sender, RoutedEventArgs e)
		{
			RenameDatabase.IsOpen = true;
			DatabaseNameBox.Text = Common.CurrentDatabase.Name;
		}

		private void DatabaseSaveAs(object sender, RoutedEventArgs e)
		{
			Common.CurrentDatabase.DBFile = Common.DialogFileSelect(true, 1, Common.CurrentDatabase.Name);
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
						var files = keyValue[1].Split(';').Distinct();
						foreach (var file in files)
						{
							Database db = new(file);
							if (db.Loaded)
								Common.AddDatabase(db);
						}
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

			if (Common.Databases.Count == 0)
			{
				Database db = new("New 1.sidb");
				if (db.Loaded)
					Common.AddDatabase(db);
			}

			DatabasesPanel.SelectedIndex = 0;

			Common.CanResize = true;
			Common.CurrentDatabase = Common.Databases[0];
			Common.Settings.MainTypeFace = new(Common.Settings.MainFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
			Common.PPD = VisualTreeHelper.GetDpi(this).PixelsPerDip;

			Common.DeferUpdateRecentNotes(true);
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

		private void RenameDatabase_Closed(object sender, System.EventArgs e)
		{
			Common.CurrentDatabase.Name = DatabaseNameBox.Text;
			var currentTab = (TabItem)DatabasesPanel.SelectedItem;
			currentTab.Header = Common.CurrentDatabase.Name;
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
			string[] settings = [
				$"FontFamily:{Common.Settings.MainFontFamily?.Source}",
				$"FontSize:{Common.Settings.MainFontSize}",
				$"SearchResultsOnTop:{Common.Settings.SearchResultsOnTop}",
				$"RibbonDisplayMode:{Common.RibbonTabContent}",
				$"LastDatabases:{string.Join(';', Common.DatabaseFiles.Distinct())}",
				$"MenuForeground:{Common.BytesFromBrush(Common.Settings.MenuForeground)}",
				$"MenuBackground:{Common.BytesFromBrush(Common.Settings.MenuBackground)}",
				$"ListForeground:{Common.BytesFromBrush(Common.Settings.ListForeground)}",
				$"ListBackground:{Common.BytesFromBrush(Common.Settings.ListBackground)}",
				$"AccentForeground:{Common.BytesFromBrush(Common.Settings.AccentForeground)}",
				$"AccentBackground:{Common.BytesFromBrush(Common.Settings.AccentBackground)}"
			];

			File.WriteAllLines("settings.sis", settings);
		}

		private void TabChanged(object sender, SelectionChangedEventArgs e)
		{
			var control = (TabControl)sender;
			if (control.Name.Equals("DatabasesPanel"))
			{
				var item = (TabItem)control.SelectedItem;
				Common.CurrentDatabase = (Database)item.Tag;
			}
			Common.DeferUpdateRecentNotes(true);
		}
	}
}
