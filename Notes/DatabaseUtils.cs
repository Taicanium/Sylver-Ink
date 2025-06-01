using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using static SylverInk.CommonUtils;
using static SylverInk.FileIO.FileUtils;
using static SylverInk.XAMLUtils.DataUtils;

namespace SylverInk.Notes;

/// <summary>
/// Static functions serving general-purpose access to the roster of databases.
/// </summary>
public static partial class DatabaseUtils
{
	public static Database CurrentDatabase { get; set; } = new();
	public static bool DatabaseChanged { get; set; }
	public static int DatabaseCount { get; set; }
	public static List<string> DatabaseFiles { get => [.. Databases.Select(db => db.DBFile)]; }
	public static ObservableCollection<Database> Databases { get; } = [];
	public static string DefaultDatabase { get; } = "New";
	public static string LastActiveDatabase { get; set; } = string.Empty;

	public static void AddDatabase(Database db)
	{
		static object PanelLabel(TabItem item) => ((Label)((StackPanel)item.Header).Children[0]).Content;

		var template = (DataTemplate)Application.Current.MainWindow.TryFindResource("DatabaseContentTemplate");
		var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");
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
		var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");

		for (int i = OpenQueries.Count - 1; i > -1; i--)
			if (OpenQueries[i].ResultDatabase?.Equals((Database)((TabItem)control.SelectedItem).Tag) is true)
				OpenQueries[i].Close();

		for (int i = Databases.Count - 1; i > -1; i--)
			if ((Databases[i].Name ?? string.Empty).Equals(db.Name))
				Databases.RemoveAt(i);

		if (control.Items.Count == 1)
			AddDatabase(new());

		control.Items.RemoveAt(control.SelectedIndex);
		control.SelectedIndex = Math.Max(0, Math.Min(control.Items.Count - 1, control.SelectedIndex));

		RecentNotesDirty = true;
		DeferUpdateRecentNotes();
	}

	public static void SaveDatabases()
	{
		foreach (Database db in Databases)
			db.Save();
	}

	public static void SwitchDatabase(Database db)
	{
		var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");
		foreach (TabItem item in control.Items)
		{
			if (((Database)item.Tag).Equals(db))
			{
				control.SelectedItem = item;
				CurrentDatabase = (Database)item.Tag;
			}
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
				"~F" => db.DBFile,
				_ => string.Empty
			};

			if (div[1].Equals(tag))
				SwitchDatabase(db);
		}
	}
}
