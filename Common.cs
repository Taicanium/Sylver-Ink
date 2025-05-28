using Microsoft.Win32;
using SylverInk.Notes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
	public static Database CurrentDatabase { get; set; } = new();
	public static bool DatabaseChanged { get; set; }
	public static List<string> DatabaseFiles { get => [.. Databases.Select(db => db.DBFile)]; }
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
	public static List<SearchResult> OpenQueries { get; } = [];
	public static List<NoteTab> OpenTabs { get; } = [];
	public static double PPD { get; set; } = 1.0;
	public static NoteRecord? PreviousOpenNote { get; set; }
	public static bool RecentNotesDirty { get; set; }
	public static NoteRecord? RecentSelection { get; set; }
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
	public static double WindowHeight { get; set; } = 0.0;
	public static double WindowWidth { get; set; } = 0.0;

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

	/// <summary>
	/// Dispatch an action to the main thread for synchronous execution.
	/// </summary>
	/// <param name="callback">The action to be performed on the main thread</param>
	public static void Concurrent(Action callback) => Application.Current.Dispatcher.Invoke(callback);

	/// <summary>
	/// Dispatch a function with no arguments to the main thread for synchronous execution, and return the result of that execution.
	/// </summary>
	/// <typeparam name="T">Type of object returned by <paramref name="callback"/></typeparam>
	/// <param name="callback">The function to be executed on the main thread</param>
	/// <returns></returns>
	public static T Concurrent<T>(Func<T> callback) => Application.Current.Dispatcher.Invoke(callback);

	public static async void DeferUpdateRecentNotes()
	{
		if (!CanResize)
			return;

		if (DelayVisualUpdates)
			return;

		DelayVisualUpdates = true;

		var panel = GetChildPanel("DatabasesPanel");

		if (panel.Dispatcher.Invoke(() => panel.FindName("RecentNotes")) is not ListBox RecentBox)
			return;

		try
		{
			await Task.Run(() =>
			{
				do
				{
					WindowHeight = RecentBox.ActualHeight == double.NaN ? Application.Current.MainWindow.ActualHeight : RecentBox.ActualHeight;
					WindowWidth = RecentBox.ActualWidth == double.NaN ? Application.Current.MainWindow.ActualWidth : RecentBox.ActualWidth;
				} while (WindowHeight <= 0);
			});

			await UpdateRecentNotes();
			Concurrent(UpdateRibbonTabs);
		}
		catch
		{
			return;
		}
		finally
		{
			DelayVisualUpdates = false;
		}
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
	/// Deletes a file if it exists.
	/// </summary>
	/// <param name="filename">The file to be deleted.</param>
	/// <returns><c>true</c> if the file existed and was deleted; else, <c>false</c>.</returns>
	public static bool Erase(string filename)
	{
		if (!File.Exists(filename))
			return false;

		File.Delete(filename);
		return true;
	}

	public static string FlowDocumentToPlaintext(FlowDocument? document)
	{
		try
		{
			if (document is null)
				return string.Empty;

			if (!document.IsInitialized)
				return string.Empty;

			var begun = false;
			var content = string.Empty;
			var pointer = document.ContentStart;

			while (pointer is not null && pointer.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.None)
			{
				switch (pointer.GetPointerContext(LogicalDirection.Forward))
				{
					case TextPointerContext.Text:
						content += pointer.GetTextInRun(LogicalDirection.Forward).Replace("{}{", "{"); // Xaml escape sequences aren't handled by XamlReader.Parse, which is very frustrating.
						pointer = pointer.GetPositionAtOffset(pointer.GetTextRunLength(LogicalDirection.Forward));
						break;
					case TextPointerContext.ElementStart:
						if (pointer.GetAdjacentElement(LogicalDirection.Forward) is LineBreak)
							content += "\r\n";
						if (pointer.GetAdjacentElement(LogicalDirection.Forward) is Paragraph)
						{
							if (begun)
								content += "\r\n\r\n";
							begun = true;
						}
						pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
						break;
					default:
						pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
						break;
				}

				if (pointer is null)
					continue;
			}

			return content.Trim();
		}
		catch
		{
			return string.Empty;
		}
	}

	public static string GetBackupPath(Database db) => Path.Join(Subfolders["Databases"], db.Name, db.Name);

	public static TabControl GetChildPanel(string basePanel) => Concurrent(() =>
	{
		var db = (TabControl)Application.Current.MainWindow.FindName(basePanel);
		var dbItem = (TabItem)db.SelectedItem;
		return (TabControl)dbItem.Content;
	});

	public static Database? GetDatabaseFromRecord(NoteRecord target)
	{
		foreach (Database db in Databases)
			for (int i = 0; i < db.RecordCount; i++)
				if (db.GetRecord(i).Equals(target))
					return db;

		return null;
	}

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
				try
				{
					tmpDB.Load(dbFile);
					if (tmpDB.UUID?.Equals(db.UUID) is true)
						return dbFile;
				}
				catch
				{
					return string.Empty;
				}
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

	public static SearchResult? OpenQuery(NoteRecord record, bool show = true)
	{
		var db = GetDatabaseFromRecord(record);

		foreach (SearchResult result in OpenQueries)
		{
			if (result.ResultDatabase?.Equals(db) is true && result.ResultRecord?.Equals(record) is true)
			{
				result.Activate();
				result.Focus();
				return result;
			}
		}

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
			if (!record.Locked)
				db?.Lock(record.Index, true);
		}

		DeferUpdateRecentNotes();

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

				var client = CurrentDatabase.Client.Active;
				var server = CurrentDatabase.Server.Active;

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

	private static async Task UpdateRecentNotes()
	{
		if (Settings.MainTypeFace is null)
			return;

		Application.Current.Resources["MainFontFamily"] = Settings.MainFontFamily;
		Application.Current.Resources["MainFontSize"] = Settings.MainFontSize;

		if (RecentNotesDirty)
			Concurrent(Settings.RecentNotes.Clear);

		await Task.Run(() =>
		{
			var DpiInfo = Concurrent(() => VisualTreeHelper.GetDpi(Application.Current.MainWindow));
			var PixelRatio = Settings.MainFontSize * DpiInfo.PixelsPerInchY * 0.013888888889;
			var LineHeight = PixelRatio * Settings.MainTypeFace.FontFamily.LineSpacing;
			var LineRatio = Math.Max(1.0, (WindowHeight / LineHeight) - 0.5);

			CurrentDatabase.Sort(RecentEntriesSortMode);

			Concurrent(() =>
			{
				while (Settings.RecentNotes.Count < LineRatio && Settings.RecentNotes.Count < CurrentDatabase.RecordCount)
					Settings.RecentNotes.Add(CurrentDatabase.GetRecord(Settings.RecentNotes.Count));

				while (Settings.RecentNotes.Count > LineRatio)
					Settings.RecentNotes.RemoveAt(Settings.RecentNotes.Count - 1);
			});

			CurrentDatabase.Sort();
		});

		RecentNotesDirty = false;
	}

	public static void UpdateRibbonTabs()
	{
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
