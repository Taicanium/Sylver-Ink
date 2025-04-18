﻿using Microsoft.Win32;
using SylverInk.Notes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SylverInk
{
	/// <summary>
	/// Static helper functions serving multi- or general-purpose needs across the entire project.
	/// </summary>
	public static partial class Common
	{
		public enum DisplayType
		{
			Content,
			Change,
			Creation,
			Index
		}

		public enum SortType
		{
			ByIndex,
			ByChange,
			ByCreation
		}

		public enum UUIDType
		{
			Database,
			Record,
			Revision
		}

		private static Import? _import;
		private static Replace? _replace;
		private static Search? _search;
		private static Settings? _settings;

		public static bool CanResize { get; set; } = false;
		public static Database CurrentDatabase { get; set; } = new();
		public static bool DatabaseChanged { get; set; } = false;
		public static List<string> DatabaseFiles { get => Databases.ToList().ConvertAll(new Converter<Database, string>(db => db.DBFile)); }
		public static ObservableCollection<Database> Databases { get; set; } = [];
		public static string DateFormat { get; } = "yyyy-MM-dd HH:mm:ss";
		public static string DefaultDatabase { get; } = "New";
		public static string DocumentsFolder { get; } = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sylver Ink");
		public static bool FirstRun { get; set; } = true;
		public static int HighestFormat { get; } = 8;
		public static Import? ImportWindow { get => _import; set { _import?.Close(); _import = value; _import?.Show(); } }
		private static BackgroundWorker? MeasureTask { get; set; }
		public static List<SearchResult> OpenQueries { get; } = [];
		public static List<NoteTab> OpenTabs { get; } = [];
		public static double PPD { get; set; } = 1.0;
		public static NoteRecord? PreviousOpenNote { get; set; }
		private static int RecentEntries { get; set; } = 10;
		public static SortType RecentEntriesSortMode { get; set; } = SortType.ByChange;
		public static Replace? ReplaceWindow { get => _replace; set { _replace?.Close(); _replace = value; _replace?.Show(); } }
		public static DisplayType RibbonTabContent { get; set; } = DisplayType.Change;
		public static Search? SearchWindow { get => _search; set { _search?.Close(); _search = value; _search?.Show(); } }
		public static ContextSettings Settings { get; } = new();
		public static string SettingsFile { get; } = Path.Join(DocumentsFolder, "settings.sis");
		public static Settings? SettingsWindow { get => _settings; set { _settings?.Close(); _settings = value; _settings?.Show(); } }
		public static Dictionary<string, string> Subfolders { get; } = new([
			new("Databases", Path.Join(DocumentsFolder, "Databases"))
			]);
		private static double TextHeight { get; set; } = 0.0;
		private static BackgroundWorker? UpdateTask { get; set; }
		public static double WindowHeight { get; set; } = 275.0;
		public static double WindowWidth { get; set; } = 330.0;

		public static void AddDatabase(Database db)
		{
			static object PanelLabel(TabItem item) => ((Label)((StackPanel)item.Header).Children[0]).Content;

			var template = (DataTemplate)Application.Current.MainWindow.TryFindResource("DatabaseContentTemplate");
			var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");
			var tabs = control.Items.Cast<TabItem>();

			if ((db.Name ?? string.Empty).Equals(string.Empty))
				db.Name = DefaultDatabase;

			if (tabs.Where(item => PanelLabel(item).Equals(db.Name)).Any())
			{
				var index = 1;
				Match match;
				if ((match = IndexDigits().Match(db.Name ?? string.Empty)).Success)
					index = int.Parse(match.Groups[1].Value);

				while (tabs.Where(item => PanelLabel(item).Equals($"{db.Name} ({index})")).Any())
					index++;
				db.Name = $"{db.Name} ({index})";
			}

			if (db.DBFile.Equals(string.Empty))
				db.DBFile = GetDatabasePath(db);

			var _name = db.Name ?? string.Empty;

			TabItem item = new()
			{
				Content = template.LoadContent(),
				Header = db.GetHeader(),
				Tag = db,
			};

			Databases.Add(db);
			CurrentDatabase = db;
			CurrentDatabase.Sort();

			var newControl = (TabControl)item.Content;
			newControl.Tag = _name;
			item.MouseRightButtonDown += (_, _) => control.SelectedItem = item;
			control.Items.Add(item);
			control.SelectedItem = item;

			UpdateContextMenu();
			DeferUpdateRecentNotes();
		}

		public static SolidColorBrush? BrushFromBytes(string data)
		{
			var hex = NumberStyles.HexNumber;

			if (data.Length == 6)
				data = "FF" + data;

			try
			{
				return new(new()
				{
					A = byte.Parse(data[..2], hex),
					R = byte.Parse(data[2..4], hex),
					G = byte.Parse(data[4..6], hex),
					B = byte.Parse(data[6..8], hex)
				});
			}
			catch { return Brushes.Transparent; }
		}

		public static string BytesFromBrush(Brush? brush, int colors = 4)
		{
			var data = brush as SolidColorBrush;
			if (colors == 4)
				return string.Format("{0:X2}{1:X2}{2:X2}{3:X2}", data?.Color.A, data?.Color.R, data?.Color.G, data?.Color.B);
			return string.Format("{0:X2}{1:X2}{2:X2}", data?.Color.R, data?.Color.G, data?.Color.B);
		}

		public static void Concurrent(Action callback) => Application.Current.Dispatcher.Invoke(callback);

		public static T Concurrent<T>(Func<T> callback) => Application.Current.Dispatcher.Invoke(callback);

		public static void DeferUpdateRecentNotes(bool RepeatUpdate = false)
		{
			if (!CanResize)
				return;

			if (UpdateTask is null)
			{
				UpdateTask = new() { WorkerSupportsCancellation = true };
				UpdateTask.DoWork += (_, _) => UpdateRecentNotes();
				UpdateTask.RunWorkerCompleted += (_, _) =>
				{
					var panel = GetChildPanel("DatabasesPanel");
					var RecentBox = (ListBox?)panel.Dispatcher.Invoke(() => panel.FindName("RecentNotes"));
					var ChangesBox = (ListBox?)panel.Dispatcher.Invoke(() => panel.FindName("ShortChanges"));

					if ((RecentBox ?? ChangesBox) is null)
						return;

					Settings.RecentNotes.Clear();
					CurrentDatabase.Sort(RecentEntriesSortMode);
					for (int i = 0; i < Math.Min(RecentEntries, CurrentDatabase.RecordCount); i++)
						Settings.RecentNotes.Add(CurrentDatabase.GetRecord(i));
					CurrentDatabase.Sort();
					RecentBox?.Items.Refresh();
					ChangesBox?.Items.Refresh();
					UpdateRibbonTabs(RibbonTabContent);
				};
			}

			if (MeasureTask is null)
			{
				MeasureTask = new();
				MeasureTask.DoWork += (_, _) =>
				{
					var panel = GetChildPanel("DatabasesPanel");
					var RecentBox = (ListBox?)panel.Dispatcher.Invoke(() => panel.FindName("RecentNotes"));
					var ChangesBox = (ListBox?)panel.Dispatcher.Invoke(() => panel.FindName("ShortChanges"));

					if ((RecentBox ?? ChangesBox) is null)
						return;

					WindowHeight = (RecentBox?.ActualHeight ?? Application.Current.MainWindow.ActualHeight) - 35.0;
					WindowWidth = (RecentBox?.ActualWidth ?? Application.Current.MainWindow.ActualWidth) - 40.0;
				};
				MeasureTask.RunWorkerCompleted += (_, _) => UpdateTask?.RunWorkerAsync();
			}

			BackgroundWorker deferUpdateTask = new();
			deferUpdateTask.DoWork += (_, _) => SpinWait.SpinUntil(new(() => !MeasureTask?.IsBusy is true && !UpdateTask?.IsBusy is true), 1000);
			deferUpdateTask.RunWorkerCompleted += (_, _) =>
			{
				if (MeasureTask?.IsBusy is false && UpdateTask?.IsBusy is false)
					MeasureTask?.RunWorkerAsync();

				if (RepeatUpdate)
					DeferUpdateRecentNotes();
			};

			Concurrent(deferUpdateTask.RunWorkerAsync);
		}

		public static string DialogFileSelect(bool outgoing = false, int filterIndex = 3, string? defaultName = null)
		{
			FileDialog dialog = outgoing ? new SaveFileDialog()
			{
				FileName = defaultName ?? DefaultDatabase,
				Filter = "Sylver Ink backup files (*.sibk)|*.sibk|Sylver Ink database files (*.sidb)|*.sidb|All files (*.*)|*.*",
			} : new OpenFileDialog()
			{
				CheckFileExists = true,
				Filter = "Sylver Ink backup files (*.sibk)|*.sibk|Sylver Ink database files (*.sidb)|*.sidb|Text files (*.txt)|*.txt|All files (*.*)|*.*",
				InitialDirectory = Subfolders["Databases"],
			};

			dialog.FilterIndex = filterIndex;
			dialog.ValidateNames = true;

			return dialog.ShowDialog() is true ? dialog.FileName : string.Empty;
		}

		public static string GetBackupPath(Database db) => Path.Join(Subfolders["Databases"], db.Name, db.Name);

		public static TabControl GetChildPanel(string basePanel) => Concurrent(() =>
		{
			var db = (TabControl)Application.Current.MainWindow.FindName(basePanel);
			var dbItem = (TabItem)db.SelectedItem;
			return (TabControl)dbItem.Content;
		});

		public static string GetDatabasePath(Database db)
		{
			var index = 0;
			Match match;
			if ((match = IndexDigits().Match(db.Name ?? string.Empty)).Success)
				index = int.Parse(match.Groups[1].Value);

			var path = Path.Join(Subfolders["Databases"], db.Name);
			var dbFile = Path.Join(path, $"{db.Name}.sidb");
			var uuidFile = Path.Join(path, "uuid.dat");

			while (File.Exists(dbFile))
			{
				if (File.Exists(uuidFile) && File.ReadAllText(uuidFile).Equals(db.UUID))
					return dbFile;

				if (!File.Exists(uuidFile))
				{
					Database tmpDB = new();
					tmpDB.Load(dbFile);
					if (tmpDB.UUID?.Equals(db.UUID) is true)
						return dbFile;
				}

				index++;
				db.Name = $"{db.Name} ({index})";
				dbFile = Path.Join(path, $"{db.Name}.sidb");
				uuidFile = Path.Join(path, "uuid.dat");
			}

			return dbFile;
		}

		public static Label GetRibbonHeader(NoteRecord record)
		{
			var tooltip = GetRibbonTooltip(record);
			var content = tooltip;

			if (content.Contains('\n'))
				content = content[..content.IndexOf('\n')];

			if (content.Length >= 13)
				content = $"{content[..10]}...";

			return new()
			{
				Content = content,
				Margin = new(0),
				ToolTip = tooltip,
			};
		}

		private static string GetRibbonTooltip(NoteRecord record) => RibbonTabContent switch
		{
			DisplayType.Change => $"{record.ShortChange} — {record.Preview}",
			DisplayType.Content => record.Preview,
			DisplayType.Creation => $"{record.GetCreated()} — {record.Preview}",
			DisplayType.Index => $"Note #{record.Index + 1:N0} — {record.Preview}",
			_ => record.Preview
		};

		public static int IntFromBytes(byte[] data) =>
			(data[0] << 24)
			+ (data[1] << 16)
			+ (data[2] << 8)
			+ data[3];

		public static byte[] IntToBytes(int data) => [
			(byte)((data >> 24) & 0xFF),
			(byte)((data >> 16) & 0xFF),
			(byte)((data >> 8) & 0xFF),
			(byte)(data & 0xFF)
		];

		public static string MakeUUID(UUIDType type = UUIDType.Record)
		{
			var time = DateTime.UtcNow;

			var binary = time.ToBinary();
			var nano = time.Nanosecond;

			var mac = binary;
			for (double i = 1.2; i < 2.0; i += 0.1)
				mac -= Math.Sign(mac) * (long)Math.Floor(mac / (new Random().NextDouble() + i));

			return string.Format("{0:X8}-{1:X4}-{2:X4}-{3:X2}{4:X2}-{5:X12}", (binary >> 32) & 0xFFFF_FFFF, (binary >> 16) & 0xFFFF, binary & 0xFFFF, (nano & 0x3FC) >> 2, (byte)type, mac & 0xFFFF_FFFF_FFFF);
		}

		private static double MeasureTextHeight(string text)
		{
			FormattedText ft = new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Settings.MainTypeFace, Settings.MainFontSize, Brushes.Black, PPD);
			return ft.Height;
		}

		public static double MeasureTextWidth(string text)
		{
			FormattedText ft = new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Settings.MainTypeFace, Settings.MainFontSize, Brushes.Black, PPD);
			return ft.Width;
		}

		public static SearchResult OpenQuery(NoteRecord record, bool show = true)
		{
			foreach (SearchResult result in OpenQueries)
				if (result.ResultRecord?.Equals(record) is true)
					return result;

			var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");

			SearchResult resultWindow = new()
			{
				Query = record.ToString(),
				ResultDatabase = control.SelectedIndex,
				ResultRecord = record,
				ResultText = CurrentDatabase.GetRecord(record.Index).ToString()
			};

			if (show)
			{
				resultWindow.Show();
				OpenQueries.Add(resultWindow);
			}

			return resultWindow;
		}

		public static void RemoveDatabase(Database db)
		{
			var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");

			for (int i = OpenQueries.Count - 1; i > -1; i--)
				if (OpenQueries[i].ResultDatabase == control.SelectedIndex)
					OpenQueries[i].Close();

			for (int i = Databases.Count - 1; i > -1; i--)
				if ((Databases[i].Name ?? string.Empty).Equals(db.Name))
					Databases.RemoveAt(i);

			if (control.Items.Count == 1)
				AddDatabase(new());

			control.Items.RemoveAt(control.SelectedIndex);
			control.SelectedIndex = Math.Max(0, Math.Min(control.Items.Count - 1, control.SelectedIndex));

			UpdateContextMenu();
		}

		public static void UpdateContextMenu()
		{
			var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");
			var menu = (Menu)Application.Current.MainWindow.FindName("DatabaseContextMenu");

			foreach (MenuItem tab in menu.Items)
			{
				foreach (MenuItem mItem in tab.Items)
				{
					var tag = mItem.GetValue(FrameworkElement.TagProperty) ?? string.Empty;
					if (tag.Equals("Always"))
						continue;

					var client = CurrentDatabase.Client?.Active is true;
					var server = CurrentDatabase.Server?.Active is true;

					var enable = tag switch
					{
						"Connected" => client && !server,
						"NotConnected" => !client && !server,
						"NotServing" => !client && !server,
						"Serving" => !client && server,
						_ => control.Items.Count != 1
					};

					mItem.SetValue(UIElement.IsEnabledProperty, enable);
					mItem.SetValue(UIElement.VisibilityProperty, enable ? Visibility.Visible : Visibility.Collapsed);
				}
			}
		}

		private static void UpdateRecentNotes()
		{
			Application.Current.Resources["MainFontFamily"] = Settings.MainFontFamily;
			Application.Current.Resources["MainFontSize"] = Settings.MainFontSize;

			RecentEntries = 0;
			TextHeight = 0.0;
			CurrentDatabase.Sort(RecentEntriesSortMode);

			while (RecentEntries < CurrentDatabase.RecordCount && TextHeight < WindowHeight)
			{
				var record = CurrentDatabase.GetRecord(RecentEntries);
				record.Preview = $"{WindowWidth}";

				RecentEntries++;
				TextHeight += MeasureTextHeight(record.Preview) * 1.25;
			}

			CurrentDatabase.Sort();
		}

		public static void UpdateRecentNotesSorting(SortType protocol)
		{
			if (protocol.Equals(string.Empty))
				return;

			RecentEntriesSortMode = protocol;
			DeferUpdateRecentNotes(true);
		}

		public static void UpdateRibbonTabs(DisplayType protocol)
		{
			RibbonTabContent = protocol;

			foreach (var item in OpenTabs)
			{
				var tag = item.Tab.Tag;
				if (tag is null)
					continue;

				item.Tab.Header = GetRibbonHeader((NoteRecord)tag);
			}
		}

		[GeneratedRegex(@"\((\p{Nd}+)\)$")]
		private static partial Regex IndexDigits();
	}
}
