﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SylverInk
{
	/// <summary>
	/// Static helper functions serving multi- or general-purpose needs across the entire project.
	/// </summary>
	public static class Common
	{
		public enum UUIDType
		{
			Database,
			Record,
			Revision,
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
		public static string DefaultDatabase { get; } = "New";
		public static string DocumentsFolder { get; } = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sylver Ink");
		public static Dictionary<string, string> DocumentsSubfolders { get; } = new([
			new("Main", DocumentsFolder),
			new("Databases", Path.Join(DocumentsFolder, "Databases"))
			]);
		public static bool ForceClose { get; set; } = false;
		public static Import? ImportWindow { get => _import; set { _import?.Close(); _import = value; _import?.Show(); } }
		private static BackgroundWorker? MeasureTask { get; set; }
		public static List<SearchResult> OpenQueries { get; } = [];
		public static double PPD { get; set; } = 1.0;
		private static int RecentEntries { get; set; } = 10;
		public static NoteController.SortType RecentEntriesSortMode { get; set; } = NoteController.SortType.ByChange;
		public static Replace? ReplaceWindow { get => _replace; set { _replace?.Close(); _replace = value; _replace?.Show(); } }
		public static string RibbonTabContent { get; set; } = "CONTENT";
		public static Search? SearchWindow { get => _search; set { _search?.Close(); _search = value; _search?.Show(); } }
		public static ContextSettings Settings { get; } = new();
		public static string SettingsFile { get; } = Path.Join(DocumentsFolder, "settings.sis");
		public static Settings? SettingsWindow { get => _settings; set { _settings?.Close(); _settings = value; _settings?.Show(); } }
		private static double TextHeight { get; set; } = 0.0;
		private static BackgroundWorker? UpdateTask { get; set; }
		public static double WindowHeight { get; set; } = 275.0;
		public static double WindowWidth { get; set; } = 330.0;

		public static void AddDatabase(Database db)
		{
			var template = (DataTemplate)Application.Current.MainWindow.TryFindResource("DatabaseContentTemplate");
			var menu = (ContextMenu)Application.Current.MainWindow.TryFindResource("DatabaseContextMenu");
			var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");
			var tabs = control.Items.Cast<TabItem>();

			if ((db.Name ?? string.Empty).Equals(string.Empty))
				db.Name = DefaultDatabase;

			if (tabs.Where(item => item.Header.Equals(db.Name)).Any())
			{
				var index = 1;
				while (tabs.Where(item => item.Header.Equals($"{db.Name} ({index})")).Any())
					index++;
				db.Name = $"{db.Name} ({index})";
			}

			if (db.DBFile.Equals(string.Empty))
				db.DBFile = GetDatabasePath(db);

			var _name = db.Name ?? string.Empty;

			TabItem item = new()
			{
				Content = template.LoadContent(),
				ContextMenu = menu,
				Header = db.GetHeader(),
				Tag = db,
			};

			item.MouseRightButtonDown += (_, _) => control.SelectedItem = item;
			control.Items.Add(item);
			control.SelectedItem = item;

			Databases.Add(db);
			CurrentDatabase = db;

			DeferUpdateRecentNotes(true);
		}

		public static SolidColorBrush? BrushFromBytes(string data)
		{
			var hex = NumberStyles.HexNumber;
			Color color;
			if (data.Length < 6)
				return Brushes.Transparent;

			try
			{
				if (data.Length == 8)
				{
					color = new Color()
					{
						A = byte.Parse(data[..2], hex),
						R = byte.Parse(data[2..4], hex),
						G = byte.Parse(data[4..6], hex),
						B = byte.Parse(data[6..8], hex)
					};
					return new(color);
				}

				color = new Color()
				{
					A = 0xFF,
					R = byte.Parse(data[..2], hex),
					G = byte.Parse(data[2..4], hex),
					B = byte.Parse(data[4..6], hex)
				};
				return new(color);
			}
			catch
			{
				return null;
			}
		}

		public static string BytesFromBrush(Brush? brush, int colors = 4)
		{
			var data = brush as SolidColorBrush;
			if (colors == 4)
				return string.Format("{0:X2}{1:X2}{2:X2}{3:X2}", data?.Color.A, data?.Color.R, data?.Color.G, data?.Color.B);
			return string.Format("{0:X2}{1:X2}{2:X2}", data?.Color.R, data?.Color.G, data?.Color.B);
		}

		public static void DeferUpdateRecentNotes(bool RepeatUpdate = false)
		{
			if (!CanResize)
				return;

			var RecentBox = (ListBox?)GetChildPanel("DatabasesPanel").FindName("RecentNotes");
			var ChangesBox = (ListBox?)GetChildPanel("DatabasesPanel").FindName("ShortChanges");

			if ((RecentBox ?? ChangesBox) is null)
				return;

			if (UpdateTask is null)
			{
				UpdateTask = new();
				UpdateTask.DoWork += (_, _) => UpdateRecentNotes();
				UpdateTask.RunWorkerCompleted += (_, _) =>
				{
					CurrentDatabase.Sort(RecentEntriesSortMode);
					Settings.RecentNotes.Clear();
					for (int i = 0; i < Math.Min(RecentEntries, CurrentDatabase.RecordCount); i++)
						Settings.RecentNotes.Add(CurrentDatabase.GetRecord(i));
					CurrentDatabase.Sort();
					RecentBox?.Items.Refresh();
					ChangesBox?.Items.Refresh();
					UpdateRibbonTabs(RibbonTabContent);
				};
				UpdateTask.WorkerSupportsCancellation = true;
			}

			if (MeasureTask is null)
			{
				MeasureTask = new();
				MeasureTask.DoWork += (_, _) =>
				{
					SpinWait.SpinUntil(() => RecentBox?.IsMeasureValid ?? true, 1000);
					WindowHeight = RecentBox?.ActualHeight ?? Application.Current.MainWindow.ActualHeight - 225;
					WindowWidth = RecentBox?.ActualWidth ?? Application.Current.MainWindow.ActualWidth - 75;

					SpinWait.SpinUntil(() => !UpdateTask?.IsBusy ?? true, 200);
					if (UpdateTask?.IsBusy ?? false)
						UpdateTask?.CancelAsync();

					SpinWait.SpinUntil(() => !UpdateTask?.IsBusy ?? true, 200);
					if (UpdateTask?.IsBusy ?? false)
						UpdateTask?.Dispose();
				};
				MeasureTask.RunWorkerCompleted += (_, _) => UpdateTask?.RunWorkerAsync();
			}

			if (!MeasureTask.IsBusy)
				MeasureTask.RunWorkerAsync();

			if (RepeatUpdate)
			{
				var RepeatTask = new BackgroundWorker();
				RepeatTask.DoWork += (_, _) => SpinWait.SpinUntil(() => !MeasureTask.IsBusy, 1500);
				RepeatTask.RunWorkerCompleted += (_, _) => DeferUpdateRecentNotes();
				RepeatTask.RunWorkerAsync();
			}
		}

		public static string DialogFileSelect(bool outgoing = false, int filterIndex = 3, string? defaultName = null)
		{
			FileDialog dialog;

			if (outgoing)
			{
				dialog = new SaveFileDialog()
				{
					FileName = defaultName ?? DefaultDatabase,
					Filter = "Sylver Ink backup files (*.sibk)|*.sibk|Sylver Ink database files (*.sidb)|*.sidb|All files (*.*)|*.*",
					FilterIndex = filterIndex,
					ValidateNames = true,
				};

				return dialog.ShowDialog() is true ? dialog.FileName : string.Empty;
			}

			dialog = new OpenFileDialog()
			{
				CheckFileExists = true,
				Filter = "Sylver Ink backup files (*.sibk)|*.sibk|Sylver Ink database files (*.sidb)|*.sidb|Text files (*.txt)|*.txt|All files (*.*)|*.*",
				FilterIndex = filterIndex,
				InitialDirectory = DocumentsSubfolders["Databases"],
				ValidateNames = true,
			};

			return dialog.ShowDialog() is true ? dialog.FileName : string.Empty;
		}

		public static string GetBackupPath(Database db) => Path.Join(DocumentsSubfolders["Databases"], db.Name, db.Name);

		public static TabControl GetChildPanel(string basePanel)
		{
			var db = (TabControl)Application.Current.MainWindow.FindName(basePanel);
			var dbItem = (TabItem)db.SelectedItem;
			var tabPanel = (TabControl)dbItem.Content;
			return tabPanel;
		}

		public static string GetDatabasePath(Database db) => Path.Join(DocumentsSubfolders["Databases"], db.Name, $"{db.Name}.sidb");

		public static Label GetRibbonHeader(int recordIndex)
		{
			string content = string.Empty;
			var record = CurrentDatabase.GetRecord(recordIndex);
			switch (RibbonTabContent)
			{
				case "CREATED":
					content = record.GetCreated();
					break;
				case "CHANGED":
					content = record.ShortChange;
					break;
				case "CONTENT":
					content = record.Preview;
					break;
				case "INDEX":
					content = $"Note #{recordIndex + 1:N0}";
					break;
			}

			var headerContent = content;

			if (headerContent.Contains('\n'))
				headerContent = headerContent[..headerContent.IndexOf('\n')];

			if (headerContent.Length >= 13)
				headerContent = $"{headerContent[..10]}...";

			return new()
			{
				Content = headerContent,
				Margin = new(0),
				ToolTip = content,
			};
		}

		private static string GetRibbonTooltip(int recordIndex)
		{
			string content = string.Empty;
			var record = CurrentDatabase.GetRecord(recordIndex);
			switch (RibbonTabContent)
			{
				case "CREATED":
					content = $"{record.GetCreated()} — {record.Preview}";
					break;
				case "CHANGED":
					content = $"{record.ShortChange} — {record.Preview}";
					break;
				case "CONTENT":
					content = record.Preview;
					break;
				case "INDEX":
					content = $"Note #{recordIndex + 1:N0} — {record.Preview}";
					break;
			}

			return content;
		}

		public static string MakeUUID(UUIDType type = UUIDType.Record)
		{
			var time = DateTime.UtcNow;

			var binary = time.ToBinary();
			var nano = time.Nanosecond;

			var mac = binary;
			for (int i = 2; i < 10; i++)
				mac -= Math.Sign(mac.CompareTo(0)) * (long)Math.Floor(mac / (new Random().NextDouble() + 1.0 + (i / 10.0)));

			var uuid = string.Format("{0:X8}-{1:X4}-{2:X4}-{3:X2}{4:X2}-{5:X12}", (binary >> 32) & 0xFFFFFFFF, (binary >> 16) & 0xFFFF, binary & 0xFFFF, nano & 0xFFF, (byte)type, mac & 0xFFFFFFFFFFFF);

			return uuid;
		}

		public static double MeasureTextSize(string text)
		{
			FormattedText ft = new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Settings.MainTypeFace, Settings.MainFontSize * 4.0 / 3.0, Brushes.Black, PPD);
			return ft.Width;
		}

		private static double MeasureTextHeight(string text)
		{
			FormattedText ft = new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Settings.MainTypeFace, Settings.MainFontSize * 4.0 / 3.0, Brushes.Black, PPD);
			return ft.Height;
		}

		public static SearchResult OpenQuery(NoteRecord record, bool show = true)
		{
			foreach (SearchResult result in OpenQueries)
				if (result.ResultRecord == record.Index)
					return result;

			var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");

			SearchResult resultWindow = new()
			{
				Query = record.ToString(),
				ResultDatabase = control.SelectedIndex,
				ResultRecord = record.Index,
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

			for (int i = OpenQueries.Count; i > 0; i--)
				if (OpenQueries[i - 1].ResultDatabase == control.SelectedIndex)
					OpenQueries[i - 1].Close();

			for (int i = Databases.Count; i > 0; i--)
				if ((Databases[i - 1].Name ?? string.Empty).Equals(db.Name))
					Databases.RemoveAt(i - 1);

			if (control.Items.Count == 1)
				AddDatabase(new());

			control.Items.RemoveAt(control.SelectedIndex);
			control.SelectedIndex = Math.Max(0, Math.Min(control.Items.Count - 1, control.SelectedIndex));

			UpdateContextMenu();
		}

		public static void UpdateContextMenu()
		{
			var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");
			var menu = (ContextMenu)Application.Current.MainWindow.TryFindResource("DatabaseContextMenu");

			foreach (DependencyObject mItem in menu.Items)
			{
				var tag = mItem.GetValue(FrameworkElement.TagProperty) ?? string.Empty;

				switch (tag)
				{
					case "Always":
						continue;
					case "Connected":
						mItem.SetValue(UIElement.IsEnabledProperty, CurrentDatabase.Client?.Active is true && !CurrentDatabase.Server?.Active is true);
						break;
					case "NotConnected":
						mItem.SetValue(UIElement.IsEnabledProperty, !CurrentDatabase.Client?.Active is true && !CurrentDatabase.Server?.Active is true);
						break;
					case "NotServing":
						mItem.SetValue(UIElement.IsEnabledProperty, !CurrentDatabase.Client?.Active is true && !CurrentDatabase.Server?.Active is true);
						break;
					case "Serving":
						mItem.SetValue(UIElement.IsEnabledProperty, !CurrentDatabase.Client?.Active is true && CurrentDatabase.Server?.Active is true);
						break;
					default:
						mItem.SetValue(UIElement.IsEnabledProperty, control.Items.Count != 1);
						break;
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

			while (RecentEntries < CurrentDatabase.RecordCount && TextHeight < WindowHeight - 25.0)
			{
				var record = CurrentDatabase.GetRecord(RecentEntries);
				record.Preview = $"{WindowWidth:N0}";

				RecentEntries++;
				var height = MeasureTextHeight(record.Preview);
				TextHeight += Math.Ceiling(height);
			}

			CurrentDatabase.Sort();
		}

		public static void UpdateRecentNotesSorting(string protocol)
		{
			if (protocol.Equals(string.Empty))
				return;

			EnumConverter cv = new(typeof(NoteController.SortType));
			RecentEntriesSortMode = (NoteController.SortType?)cv.ConvertFromString(protocol) ?? NoteController.SortType.ByChange;

			DeferUpdateRecentNotes(true);
		}

		public static void UpdateRibbonTabs(string protocol)
		{
			if (protocol.Equals(string.Empty))
				return;

			RibbonTabContent = protocol;
			var control = GetChildPanel("DatabasesPanel");
			if (control is null)
				return;

			foreach (TabItem item in control.Items)
			{
				var tag = item.Tag;
				if (tag is null)
					continue;

				item.Header = GetRibbonHeader((int)tag);
				item.ToolTip = GetRibbonTooltip((int)tag);
			}
		}
	}
}
