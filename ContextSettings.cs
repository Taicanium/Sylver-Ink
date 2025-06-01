using SylverInk.Notes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using static SylverInk.Common;

namespace SylverInk;

public partial class ContextSettings : INotifyPropertyChanged
{
	private Brush? _accentBackgound = Brushes.PaleGoldenrod;
	private Brush? _accentForegound = Brushes.Blue;
	private string _importData = string.Empty;
	private string _importTarget = string.Empty;
	private readonly List<string> _lastDatabases = [];
	private int _lineTolerance = 2;
	private Brush? _listBackgound = Brushes.White;
	private Brush? _listForegound = Brushes.Black;
	private FontFamily? _mainFontFamily = new("Arial");
	private double _mainFontSize = 11.0;
	private Typeface? _mainTypeFace;
	private Brush? _menuBackgound = Brushes.Beige;
	private Brush? _menuForegound = Brushes.DimGray;
	private double _noteTransparency = 100.0;
	private string _numReplacements = string.Empty;
	public event PropertyChangedEventHandler? PropertyChanged;
	private bool _readyToReplace;
	private readonly ObservableCollection<NoteRecord> _recentNotes = [];
	private readonly ObservableCollection<NoteRecord> _searchResults = [];
	private bool _searchResultsOnTop;
	private bool _searchResultsInTaskbar;
	private bool _snapSearchResults = true;

	public Brush? AccentBackground { get => _accentBackgound; set { _accentBackgound = value; OnPropertyChanged(); } }
	public Brush? AccentForeground { get => _accentForegound; set { _accentForegound = value; OnPropertyChanged(); } }
	public string ImportData { get => _importData; set { _importData = value; OnPropertyChanged(); } }
	public string ImportTarget { get => _importTarget; set { _importTarget = value; OnPropertyChanged(); } }
	public List<string> LastDatabases { get => _lastDatabases; }
	public int LineTolerance { get => _lineTolerance; set { _lineTolerance = Math.Min(36, Math.Max(0, value)); OnPropertyChanged(); } }
	public Brush? ListBackground { get => _listBackgound; set { _listBackgound = value; OnPropertyChanged(); } }
	public Brush? ListForeground { get => _listForegound; set { _listForegound = value; OnPropertyChanged(); } }
	public FontFamily? MainFontFamily { get => _mainFontFamily; set { _mainFontFamily = value; MainTypeFace = new(value, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal); OnPropertyChanged(); } }
	public double MainFontSize { get => _mainFontSize; set { _mainFontSize = Math.Min(24.0, Math.Max(10.0, value)); OnPropertyChanged(); } }
	public Typeface? MainTypeFace { get => _mainTypeFace; set { _mainTypeFace = value; OnPropertyChanged(); } }
	public Brush? MenuBackground { get => _menuBackgound; set { _menuBackgound = value; OnPropertyChanged(); } }
	public Brush? MenuForeground { get => _menuForegound; set { _menuForegound = value; OnPropertyChanged(); } }
	public double NoteTransparency { get => _noteTransparency; set { _noteTransparency = value; OnPropertyChanged(); } }
	public string NumReplacements { get => _numReplacements; set { _numReplacements = value; OnPropertyChanged(); } }
	public bool ReadyToReplace { get => _readyToReplace; set { _readyToReplace = value; OnPropertyChanged(); } }
	public ObservableCollection<NoteRecord> RecentNotes { get => _recentNotes; }
	public ObservableCollection<NoteRecord> SearchResults { get => _searchResults; }
	public bool SearchResultsOnTop { get => _searchResultsOnTop; set { _searchResultsOnTop = value; SearchResultsInTaskbar = !value; OnPropertyChanged(); } }
	public bool SearchResultsInTaskbar { get => _searchResultsInTaskbar; private set { _searchResultsInTaskbar = value; OnPropertyChanged(); } }
	public bool SnapSearchResults { get => _snapSearchResults; set { _snapSearchResults = value; OnPropertyChanged(); } }
	public string VersionString { get; } = $"v. {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} © Taica, {GetBuildYear(Assembly.GetExecutingAssembly())}";

	private static int GetBuildYear(Assembly assembly)
	{
		const string prefix = "+build";
		var attr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

		string? value = attr?.InformationalVersion;
		int index = value?.IndexOf(prefix) ?? 0;
		if (index > 0 && DateTime.TryParseExact(value?[(index + prefix.Length)..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
			return result.Year;

		return default;
	}

	public async Task Load()
	{
		if (!File.Exists(SettingsFile))
			return;

		foreach (string setting in File.ReadAllLines(SettingsFile))
		{
			var keyValue = setting.Trim().Split(':', 2);
			switch (keyValue[0])
			{
				case "AccentBackground":
					AccentBackground = BrushFromBytes(keyValue[1]);
					break;
				case "AccentForeground":
					AccentForeground = BrushFromBytes(keyValue[1]);
					break;
				case "FontFamily":
					MainFontFamily = new(keyValue[1]);
					break;
				case "FontSize":
					MainFontSize = double.Parse(keyValue[1], CultureInfo.InvariantCulture);
					break;
				case "LastActiveDatabase":
					LastActiveDatabase = keyValue[1];
					foreach (var db in Databases)
						if (LastActiveDatabase.Equals(db.Name))
							CurrentDatabase = db;
					break;
				case "LastActiveNotes":
					var notes = keyValue[1].Split(';').Distinct();
					foreach (var note in notes)
						LastActiveNotes.Add(note);
					break;
				case "LastActiveNotesHeight":
					var nHeight = keyValue[1].Split(';').Distinct();
					foreach (var sHeight in nHeight)
					{
						var hSplit = sHeight.Split(':');
						if (hSplit.Length < 3)
							continue;
						if (int.TryParse(hSplit[2], out var dHeight))
							LastActiveNotesHeight.TryAdd(hSplit[0] + ":" + hSplit[1], dHeight);
					}
					break;
				case "LastActiveNotesLeft":
					var nLeft = keyValue[1].Split(';').Distinct();
					foreach (var sLeft in nLeft)
					{
						var lSplit = sLeft.Split(':');
						if (lSplit.Length < 3)
							continue;
						if (int.TryParse(lSplit[2], out var dLeft))
							LastActiveNotesLeft.TryAdd(lSplit[0] + ":" + lSplit[1], dLeft);
					}
					break;
				case "LastActiveNotesTop":
					var nTop = keyValue[1].Split(';').Distinct();
					foreach (var sTop in nTop)
					{
						var tSplit = sTop.Split(':');
						if (tSplit.Length < 3)
							continue;
						if (int.TryParse(tSplit[2], out var dTop))
							LastActiveNotesTop.TryAdd(tSplit[0] + ":" + tSplit[1], dTop);
					}
					break;
				case "LastActiveNotesWidth":
					var nWidth = keyValue[1].Split(';').Distinct();
					foreach (var sWidth in nWidth)
					{
						var wSplit = sWidth.Split(':');
						if (wSplit.Length < 3)
							continue;
						if (int.TryParse(wSplit[2], out var dWidth))
							LastActiveNotesWidth.TryAdd(wSplit[0] + ":" + wSplit[1], dWidth);
					}
					break;
				case "LastDatabases":
					FirstRun = false;
					LastDatabases.AddRange(keyValue[1].Replace("?\\", DocumentsFolder).Split(';').Distinct().Where(File.Exists));
					DatabaseCount = Math.Max(1, LastDatabases.Count);
					foreach (var file in LastDatabases)
						await Database.Create(file);
					if (LastDatabases.Count == 0 && Databases.Count == 0)
					{
						DatabaseCount = 1;
						await Database.Create(Path.Join(Subfolders["Databases"], DefaultDatabase, $"{DefaultDatabase}.sidb"));
					}
					break;
				case "ListBackground":
					ListBackground = BrushFromBytes(keyValue[1]);
					break;
				case "ListForeground":
					ListForeground = BrushFromBytes(keyValue[1]);
					break;
				case "MenuBackground":
					MenuBackground = BrushFromBytes(keyValue[1]);
					break;
				case "MenuForeground":
					MenuForeground = BrushFromBytes(keyValue[1]);
					break;
				case "NoteTransparency":
					if (!double.TryParse(keyValue[1], out var transparency))
						transparency = 100.0;
					NoteTransparency = transparency;
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
					SearchResultsOnTop = bool.Parse(keyValue[1]);
					break;
				case "SnapSearchResults":
					SnapSearchResults = bool.Parse(keyValue[1]);
					break;
				default:
					break;
			}
		}
	}

	protected void OnPropertyChanged([CallerMemberName] string? name = null) => Concurrent(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));

	public void Save() => File.WriteAllLines(SettingsFile, [
		$"AccentBackground:{BytesFromBrush(AccentBackground)}",
		$"AccentForeground:{BytesFromBrush(AccentForeground)}",
		$"FontFamily:{MainFontFamily?.Source}",
		$"FontSize:{MainFontSize}",
		$"LastActiveDatabase:{CurrentDatabase.Name}",
		$"LastActiveNotes:{string.Join(';', OpenQueries.Select(query => query.ResultDatabase?.Name + ":" + query.ResultRecord?.Index))}",
		$"LastActiveNotesHeight:{string.Join(';', OpenQueries.Select(query => query.ResultDatabase?.Name + ":" + $"{query.ResultRecord?.Index}:{query.Height}"))}",
		$"LastActiveNotesLeft:{string.Join(';', OpenQueries.Select(query => query.ResultDatabase?.Name + ":" + $"{query.ResultRecord?.Index}:{query.Left}"))}",
		$"LastActiveNotesTop:{string.Join(';', OpenQueries.Select(query => query.ResultDatabase?.Name + ":" + $"{query.ResultRecord?.Index}:{query.Top}"))}",
		$"LastActiveNotesWidth:{string.Join(';', OpenQueries.Select(query => query.ResultDatabase?.Name + ":" + $"{query.ResultRecord?.Index}:{query.Width}"))}",
		$"LastDatabases:{string.Join(';', DatabaseFiles.Distinct().Where(File.Exists)).Replace(DocumentsFolder, "?\\")}",
		$"ListBackground:{BytesFromBrush(ListBackground)}",
		$"ListForeground:{BytesFromBrush(ListForeground)}",
		$"MenuBackground:{BytesFromBrush(MenuBackground)}",
		$"MenuForeground:{BytesFromBrush(MenuForeground)}",
		$"NoteTransparency:{(int)NoteTransparency}",
		$"RecentNotesSortMode:{(int)RecentEntriesSortMode}",
		$"RibbonDisplayMode:{(int)RibbonTabContent}",
		$"SearchResultsOnTop:{SearchResultsOnTop}",
		$"SnapSearchResults:{SnapSearchResults}",
	]);
}
