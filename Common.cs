using Microsoft.Win32;
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
using System.Windows.Documents;
using System.Windows.Media;

namespace SylverInk;

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

	public static bool CanResize { get; set; }
	public static BackgroundWorker CheckInit { get; } = new();
	public static Database CurrentDatabase { get; set; } = new();
	public static bool DatabaseChanged { get; set; }
	public static List<string> DatabaseFiles { get => Databases.ToList().ConvertAll(new Converter<Database, string>(db => db.DBFile)); }
	public static int DatabaseCount { get; set; }
	public static ObservableCollection<Database> Databases { get; set; } = [];
	public static string DateFormat { get; } = "yyyy-MM-dd HH:mm:ss";
	public static string DefaultDatabase { get; } = "New";
	public static bool DelayVisualUpdates { get; set; } = false;
	public static string DocumentsFolder { get; } = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sylver Ink");
	public static bool FirstRun { get; set; } = true;
	public static int HighestFormat { get; } = 10;
	public static Import? ImportWindow { get => _import; set { _import?.Close(); _import = value; _import?.Show(); } }
	public static bool InitComplete { get; set; }
	public static char[] InvalidPathChars { get; } = ['/', '\\', ':', '*', '"', '?', '<', '>', '|'];
	public static string LastActiveDatabase { get; set; } = string.Empty;
	public static List<string> LastActiveNotes { get; set; } = [];
	public static Dictionary<string, double> LastActiveNotesHeight { get; set; } = [];
	public static Dictionary<string, double> LastActiveNotesLeft { get; set; } = [];
	public static Dictionary<string, double> LastActiveNotesTop { get; set; } = [];
	public static Dictionary<string, double> LastActiveNotesWidth { get; set; } = [];
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
	public static bool SettingsLoaded { get; set; }
	public static Settings? SettingsWindow { get => _settings; set { _settings?.Close(); _settings = value; _settings?.Show(); } }
	public static Dictionary<string, string> Subfolders { get; } = new([
		new("Databases", Path.Join(DocumentsFolder, "Databases"))
		]);
	private static double TextHeight { get; set; }
	public static bool UpdatesChecked { get; set; }
	private static BackgroundWorker? UpdateTask { get; set; }
	public static double WindowHeight { get; set; } = 275.0;
	public static double WindowWidth { get; set; } = 330.0;

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
				index = int.Parse(match.Groups[1].Value);
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

		UpdateDatabaseMenu();
		DeferUpdateRecentNotes();
	}

	public static void Autosave()
	{
		var lockFile = GetLockFile();
		Erase(lockFile);
		CurrentDatabase.Save(lockFile);
	}

	public static SolidColorBrush? BrushFromBytes(string data)
	{
		if (data.Length == 6)
			data = "FF" + data;

		if (data.Length != 8)
			return Brushes.Transparent;

		try
		{
			return new(new()
			{
				A = byte.Parse(data[..2], NumberStyles.HexNumber),
				R = byte.Parse(data[2..4], NumberStyles.HexNumber),
				G = byte.Parse(data[4..6], NumberStyles.HexNumber),
				B = byte.Parse(data[6..8], NumberStyles.HexNumber)
			});
		}
		catch { return Brushes.Transparent; }
	}

	public static string BytesFromBrush(Brush? brush, int colors = 4)
	{
		var data = brush as SolidColorBrush;
		if (colors == 4)
			return $"{data?.Color.A:X2}{data?.Color.R:X2}{data?.Color.G:X2}{data?.Color.B:X2}";
		return $"{data?.Color.R:X2}{data?.Color.G:X2}{data?.Color.B:X2}";
	}

	public static void ColorChanged(string? ColorTag, Brush ColorSelection)
	{
		if (ColorTag is null)
			return;

		switch (ColorTag)
		{
			case "P1F":
				Settings.MenuForeground = ColorSelection;
				break;
			case "P1B":
				Settings.MenuBackground = ColorSelection;
				break;
			case "P2F":
				Settings.ListForeground = ColorSelection;
				break;
			case "P2B":
				Settings.ListBackground = ColorSelection;
				break;
			case "P3F":
				Settings.AccentForeground = ColorSelection;
				break;
			case "P3B":
				Settings.AccentBackground = ColorSelection;
				break;
		}
	}

	public static void Concurrent(Action callback) => Application.Current.Dispatcher.Invoke(callback);

	public static T Concurrent<T>(Func<T> callback) => Application.Current.Dispatcher.Invoke(callback);

	public static void DeferUpdateRecentNotes(bool RepeatUpdate = false)
	{
		if (!CanResize)
			return;

		if (DelayVisualUpdates)
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
		deferUpdateTask.DoWork += (_, _) => SpinWait.SpinUntil(new(() => !MeasureTask?.IsBusy is true && !UpdateTask?.IsBusy is true), 100);
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

	/// <summary>
	/// Delete a file if it exists.
	/// </summary>
	/// <param name="filename">The file to be deleted.</param>
	/// <returns>'true' if the file existed and was deleted; else, 'false'</returns>
	public static bool Erase(string filename)
	{
		if (!File.Exists(filename))
			return false;

		File.Delete(filename);
		return true;
	}

	public static string FlowDocumentToPlaintext(FlowDocument document)
	{
		try
		{
			return new TextRange(document.ContentStart, document.ContentEnd).Text;
		}
		catch // System.ExecutionEngineException - occurs rarely when resizing the window too quickly
		{
			SpinWait.SpinUntil(new(() => false), 150);
			return new TextRange(document.ContentStart, document.ContentEnd).Text;
		}
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

	public static string GetLockFile(string? dbFile = null) => Path.Join(Path.GetDirectoryName(dbFile ?? CurrentDatabase.DBFile) ?? ".", "~lock.sidb");

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

	public static uint HSVFromRGB(SolidColorBrush brush)
	{
		const double fInv = 1.0 / 255.0;
		var (r_, g_, b_) = (brush.Color.R * fInv, brush.Color.G * fInv, brush.Color.B * fInv);
		var Cmax = Math.Max(r_, Math.Max(g_, b_));
		var Cmin = Math.Min(r_, Math.Min(g_, b_));
		var delta = Cmax - Cmin;
		var _h = 0.0;
		var _s = Cmax == 0.0 ? 0.0 : (delta / Cmax);
		var _v = Cmax;
		if (delta != 0.0)
		{
			delta = 60.0 / delta;
			if (Cmax == r_)
				_h = delta * (g_ - b_) + 360.0;
			if (Cmax == g_)
				_h = delta * (b_ - r_) + 120.0;
			if (Cmax == b_)
				_h = delta * (r_ - g_) + 240.0;
		}
		var H = (uint)(_h % 360.0 * 0.7083333333);
		var S = (uint)(_s * 255.0);
		var V = (uint)(_v * 255.0);
		return (H << 16) + (S << 8) + V;
	}

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
		var micro = time.Microsecond;

		var seed = (int)(time.Ticks & int.MaxValue);
		var rnd = new Random(seed);

		long mac = rnd.Next();
		for (double i = 4.2; i < 5.0; i += rnd.NextDouble())
			mac += (long)Math.Floor(mac / (rnd.NextDouble() + i));

		mac |= (long)(rnd.Next() & 0xFFFF) << 32;

		var uuid = $"{(binary >> 32) & 0xFFFF_FFFF:X8}-{(binary >> 16) & 0xFFFF:X4}-{binary & 0xFFFF:X4}-{(micro & 0xFF) >> 2:X2}{(byte)type:X2}-{mac & 0xFFFF_FFFF_FFFF:X12}";
		return uuid;
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
			if (result.ResultDatabase?.Equals(CurrentDatabase) is true && result.ResultRecord?.Equals(record) is true)
				return result;

		var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");

		SearchResult resultWindow = new()
		{
			ResultDatabase = (Database)((TabItem)control.SelectedItem).Tag,
			ResultRecord = record,
			ResultText = record.Reconstruct()
		};

		if (show)
		{
			resultWindow.Show();
			OpenQueries.Add(resultWindow);
		}

		return resultWindow;
	}

	public static SearchResult? OpenQuery(Database db, NoteRecord record, bool show = true)
	{
		foreach (SearchResult result in OpenQueries)
			if (result.ResultDatabase?.Equals(db) is true && result.ResultRecord?.Equals(record) is true)
				return result;

		SearchResult resultWindow = new()
		{
			ResultDatabase = db,
			ResultRecord = record,
			ResultText = record.Reconstruct()
		};

		if (show)
		{
			resultWindow.Show();
			OpenQueries.Add(resultWindow);
		}

		return resultWindow;
	}

	public static FlowDocument PlaintextToFlowDocument(string content)
	{
		FlowDocument document = new();
		TextPointer pointer = document.ContentStart;
		var lineSplit = content.Replace("\r", string.Empty).Split('\n') ?? [];
		for (int i = 0; i < lineSplit.Length; i++)
		{
			var line = lineSplit[i];
			if (!string.IsNullOrEmpty(line))
			{
				pointer.InsertTextInRun(line);
				while (pointer.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.ElementEnd)
					pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
				if (i < lineSplit.Length - 1)
				{
					if (string.IsNullOrEmpty(lineSplit[i + 1]))
						pointer = pointer.InsertParagraphBreak();
					else
						pointer = pointer.InsertLineBreak();
				}
				continue;
			}
		}
		return document;
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

		UpdateDatabaseMenu();
	}

	public static void UpdateDatabaseMenu()
	{
		var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");
		var menu = (Menu)Application.Current.MainWindow.FindName("DatabaseMenu");

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
