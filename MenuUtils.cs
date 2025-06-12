using SylverInk.Notes;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static SylverInk.CommonUtils;
using static SylverInk.FileIO.FileUtils;
using static SylverInk.Notes.DatabaseUtils;
using static SylverInk.XAMLUtils.DataUtils;

namespace SylverInk;

/// <summary>
/// Extension methods serving needs tied to context menus (and the application toolbar) on the main application window.
/// </summary>
public static class MenuUtils
{
	public static void MenuBackup(this MainWindow window, object? sender, RoutedEventArgs e) => CurrentDatabase.MakeBackup();

	public static void MenuClose(this MainWindow window, object? sender, RoutedEventArgs e)
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

	public static void MenuConnect(this MainWindow window, object? sender, RoutedEventArgs e)
	{
		window.ConnectAddress.IsOpen = true;
		window.AddressBox.Text = string.Empty;
	}

	public static void MenuCopyCode(this MainWindow window, object? sender, RoutedEventArgs e) => Clipboard.SetText(CurrentDatabase.Server?.AddressCode);

	public static void MenuCreate(this MainWindow window, object? sender, RoutedEventArgs e)
	{
		AddDatabase(new());
		window.DatabasesPanel.SelectedIndex = window.DatabasesPanel.Items.Count - 1;
		DeferUpdateRecentNotes();
	}

	public static void MenuDelete(this MainWindow window, object? sender, RoutedEventArgs e)
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

	public static void MenuDisconnect(this MainWindow window, object? sender, RoutedEventArgs e)
	{
		CurrentDatabase.Client.Disconnect();
		CurrentDatabase.Changed = true;
	}

	public static async void MenuOpen(this MainWindow window, object? sender, RoutedEventArgs e)
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

		await Database.Create(dbFile);
		DeferUpdateRecentNotes();
	}

	public static void MenuProperties(this MainWindow window, object? sender, RoutedEventArgs e)
	{
		Properties properties = new() { DB = CurrentDatabase };
		properties.Show();
	}

	public static void MenuRename(this MainWindow window, object? sender, RoutedEventArgs e)
	{
		window.RenameDatabase.IsOpen = true;
		window.DatabaseNameBox.Text = CurrentDatabase.Name;
		window.DatabaseNameBox.Focus();
		window.DatabaseNameBox.CaretIndex = window.DatabaseNameBox.Text?.Length ?? 0;
	}

	public static void MenuSaveAs(this MainWindow window, object? sender, RoutedEventArgs e)
	{
		var newPath = DialogFileSelect(true, 2, CurrentDatabase.Name);
		if (!string.IsNullOrWhiteSpace(newPath))
			CurrentDatabase.DBFile = newPath;
		CurrentDatabase.Format = HighestSIDBFormat;
	}

	public static void MenuSaveLocal(this MainWindow window, object? sender, RoutedEventArgs e)
	{
		CurrentDatabase.Changed = true;
		CurrentDatabase.DBFile = Path.Join(Subfolders["Databases"], Path.GetFileNameWithoutExtension(CurrentDatabase.DBFile), Path.GetFileName(CurrentDatabase.DBFile));
		CurrentDatabase.Format = HighestSIDBFormat;
		CurrentDatabase.Save();
	}

	public static void MenuServe(this MainWindow window, object? sender, RoutedEventArgs e) => CurrentDatabase.Server.Serve(0);

	public static void MenuShowAbout(this MainWindow window, object? sender, RoutedEventArgs e) => new About().Show();

	public static void MenuSublistChanged(this MainWindow window, object? sender, RoutedEventArgs e)
	{
		if (Mouse.RightButton == MouseButtonState.Pressed)
			return;

		if (sender is not ListBoxItem box)
			return;

		if (box.DataContext is not NoteRecord record)
			return;

		RecentSelection = record;
		OpenQuery(RecentSelection);
	}

	public static void MenuTabChanged(this MainWindow window, object? sender, SelectionChangedEventArgs e)
	{
		if (sender is not TabControl control)
			return;

		if (!control.Name.Equals("DatabasesPanel"))
			return;

		if (control.SelectedItem is not TabItem item)
			return;

		if (item.Tag is not Database newDB)
			return;

		if (newDB.Equals(CurrentDatabase))
			return;

		CurrentDatabase = newDB;
		RecentNotesDirty = true;
		CommonUtils.Settings.SearchResults.Clear();

		DeferUpdateRecentNotes();
	}

	public static void MenuUnserve(this MainWindow window, object? sender, RoutedEventArgs e) => CurrentDatabase.Server.Close();
}
