using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static SylverInk.CommonUtils;
using static SylverInk.FileIO.FileUtils;
using static SylverInk.XAMLUtils.DataUtils;

namespace SylverInk.Notes;

/// <summary>
/// Static functions serving general-purpose access to the roster of databases.
/// </summary>
public static class DatabaseUtils
{
	private static Database? _currentDatabase;

	public static Database CurrentDatabase { get => _currentDatabase ??= new(); set => _currentDatabase = value; }
	public static bool DatabaseChanged { get; set; }
	public static List<string> DatabaseFiles { get => [.. Databases.Select(db => db.DBFile)]; }
	public static List<Database> Databases { get; } = [];
	public static string DefaultDatabase { get; } = "New";
	public static string ShellDB { get; set; } = string.Empty;

	public static void AddDatabase(Database db)
	{
		static object PanelLabel(TabItem item) => ((Label)((StackPanel)item.Header).Children[0]).Content;

		if (Application.Current.MainWindow.TryFindResource("DatabaseContentTemplate") is not DataTemplate template)
			return;

		if (Application.Current.MainWindow.FindName("DatabasesPanel") is not TabControl control)
			return;

		var tabs = control.Items.Cast<TabItem>();

		if (string.IsNullOrWhiteSpace(db.Name))
			db.Name = DefaultDatabase;

		if (tabs.Where(item => PanelLabel(item).Equals(db.Name)).Any())
		{
			var index = 1;
			Match match = IndexDigits().Match(db.Name);
			if (match.Success)
				index = int.Parse(match.Groups[1].Value, NumberFormatInfo.InvariantInfo);
			while (tabs.Where(item => PanelLabel(item).Equals($"{db.Name} ({index})")).Any())
				index++;
			db.Name = $"{db.Name} ({index})";
		}

		if (string.IsNullOrWhiteSpace(db.DBFile))
			db.DBFile = GetDatabasePath(db);

		TabItem item = new()
		{
			Content = template.LoadContent(),
			Header = db.GetHeader(),
			Tag = db,
		};

		Databases.Add(db);
		db.Sort();

		var newControl = (TabControl)item.Content;
		newControl.Tag = db.Name;
		item.MouseRightButtonDown += (_, _) => control.SelectedItem = item;
		control.Items.Add(item);
		control.SelectedItem = item;

		RecentNotesDirty = true;
		DeferUpdateRecentNotes();
	}

	public static Database? GetDatabaseFromRecord(NoteRecord target)
	{
		foreach (Database db in Databases)
			for (int i = 0; i < db.RecordCount; i++)
				if (db.GetRecord(i).Equals(target))
					return db;

		return null;
	}

	public static void RemoveDatabase(Database db)
	{
		if (Application.Current.MainWindow.FindName("DatabasesPanel") is not TabControl control)
			return;

		if (control.SelectedItem is not TabItem item)
			return;

		if (item.Tag is not Database tabDB)
			return;

		if (control.Items.Count < 2)
			AddDatabase(new());

		if (tabDB.Equals(db))
		{
			control.Items.RemoveAt(control.SelectedIndex);
			control.SelectedIndex = Math.Max(0, Math.Min(control.Items.Count - 1, control.SelectedIndex));
		}

		for (int i = OpenQueries.Count - 1; i > -1; i--)
			if (OpenQueries[i].ResultDatabase?.Equals(db) is true)
				OpenQueries[i].Close();

		for (int i = Databases.Count - 1; i > -1; i--)
			if ((Databases[i].Name ?? string.Empty).Equals(db.Name))
				Databases.RemoveAt(i);

		RecentNotesDirty = true;
		DeferUpdateRecentNotes();
	}

	public static async Task SaveDatabases()
	{
		foreach (Database db in Databases)
			await Task.Run(db.Save);
	}

	public static void SwitchDatabase(Database db)
	{
		if (Application.Current.MainWindow.FindName("DatabasesPanel") is not TabControl control)
			return;

		foreach (TabItem item in control.Items)
		{
			if (item.Tag is not Database tabDB)
				continue;

			if (!tabDB.Equals(db))
				continue;

			control.SelectedItem = item;
			CurrentDatabase = tabDB;
		}
	}

	public static void SwitchDatabase(string dbID)
	{
		var div = dbID.Split(':', 2);

		foreach (Database db in Databases)
		{
			var tag = div[0] switch
			{
				"~N" => db.Name,
				"~F" => Path.GetFullPath(db.DBFile),
				_ => string.Empty
			};

			if (div[1].Equals(tag))
				SwitchDatabase(db);
		}
	}
}
