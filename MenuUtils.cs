using SylverInk.Notes;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static SylverInk.Common;

namespace SylverInk
{
	public static class MenuUtils
	{
		public static void CopyCode(this MainWindow window, object? sender, RoutedEventArgs e) => window.CodePopup.IsOpen = false;

		public static void DatabaseRename(this MainWindow window, object? sender, RoutedEventArgs e) => window.RenameDatabase.IsOpen = false;

		public static void Menu_ShowAbout(this MainWindow window, object? sender, RoutedEventArgs e) => new About().Show();

		public static void Menu_Backup(this MainWindow window, object? sender, RoutedEventArgs e) => CurrentDatabase.MakeBackup();

		public static void Menu_Close(this MainWindow window, object? sender, RoutedEventArgs e)
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

		public static void Menu_Connect(this MainWindow window, object? sender, RoutedEventArgs e)
		{
			window.ConnectAddress.IsOpen = true;
			window.AddressBox.Text = string.Empty;
		}

		public static void Menu_Create(this MainWindow window, object? sender, RoutedEventArgs e)
		{
			AddDatabase(new());
			window.DatabasesPanel.SelectedIndex = window.DatabasesPanel.Items.Count - 1;
			DeferUpdateRecentNotes();
		}

		public static void Menu_Delete(this MainWindow window, object? sender, RoutedEventArgs e)
		{
			if (MessageBox.Show("Are you sure you want to permanently delete this database?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
				return;

			Erase(CurrentDatabase.DBFile);

			var BKPath = Path.GetDirectoryName(GetBackupPath(CurrentDatabase));
			if (Directory.Exists(BKPath))
				Directory.Delete(BKPath, true);

			RemoveDatabase(CurrentDatabase);
			DeferUpdateRecentNotes();
		}

		public static void Menu_Disconnect(this MainWindow window, object? sender, RoutedEventArgs e)
		{
			CurrentDatabase.Client?.Disconnect();
			CurrentDatabase.Changed = true;
		}

		public static void Menu_Open(this MainWindow window, object? sender, RoutedEventArgs e)
		{
			string dbFile = DialogFileSelect(filterIndex: 2);
			if (string.IsNullOrWhiteSpace(dbFile))
				return;

			var path = Path.GetFullPath(dbFile);

			if (DatabaseFiles.Contains(path))
			{
				var items = window.DatabasesPanel.Items.Cast<TabItem>().ToList();
				var db = items?.FindIndex(new(item => {
					var innerDB = (Database)item.Tag;
					return Path.GetFullPath(innerDB.DBFile).Equals(path);
				}));

				window.DatabasesPanel.SelectedIndex = db ?? window.DatabasesPanel.SelectedIndex;
				return;
			}

			Database.Create(dbFile, true);
			DeferUpdateRecentNotes();
		}

		public static void Menu_Properties(this MainWindow window, object? sender, RoutedEventArgs e)
		{
			Properties properties = new() { DB = CurrentDatabase };
			properties.Show();
		}

		public static void Menu_Rename(this MainWindow window, object? sender, RoutedEventArgs e)
		{
			window.RenameDatabase.IsOpen = true;
			window.DatabaseNameBox.Text = CurrentDatabase.Name;
			window.DatabaseNameBox.Focus();
			window.DatabaseNameBox.CaretIndex = window.DatabaseNameBox.Text?.Length ?? 0;
		}

		public static void Menu_SaveAs(this MainWindow window, object? sender, RoutedEventArgs e)
		{
			var newPath = DialogFileSelect(true, 2, CurrentDatabase.Name);
			if (!string.IsNullOrWhiteSpace(newPath))
				CurrentDatabase.DBFile = newPath;
			CurrentDatabase.Format = HighestFormat;
		}

		public static void Menu_SaveLocal(this MainWindow window, object? sender, RoutedEventArgs e)
		{
			CurrentDatabase.Changed = true;
			CurrentDatabase.DBFile = Path.Join(Subfolders["Databases"], Path.GetFileNameWithoutExtension(CurrentDatabase.DBFile), Path.GetFileName(CurrentDatabase.DBFile));
			CurrentDatabase.Format = HighestFormat;
			CurrentDatabase.Save();
		}

		public static void Menu_Serve(this MainWindow window, object? sender, RoutedEventArgs e) => CurrentDatabase.Server?.Serve(0);

		public static void Menu_SublistChanged(this MainWindow window, object? sender, RoutedEventArgs e)
		{
			if (Mouse.RightButton == MouseButtonState.Pressed)
				return;

			var box = (ListBox?)sender;
			if (box?.SelectedItem is null)
				return;

			var grid = (Grid)box.Parent;
			RecentSelection = (NoteRecord)box.SelectedItem;

			foreach (ListBox item in grid.Children)
				item.SelectedIndex = box.SelectedIndex;

			OpenQuery(RecentSelection);
		}

		public static void Menu_TabChanged(this MainWindow window, object? sender, SelectionChangedEventArgs e)
		{
			var control = (TabControl?)sender;
			if (control is null)
				return;

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

			UpdateDatabaseMenu();
			DeferUpdateRecentNotes(true);
		}

		public static void Menu_Unserve(this MainWindow window, object? sender, RoutedEventArgs e) => CurrentDatabase.Server?.Close();
	}
}
