using SylverInk.Notes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using static SylverInk.Notes.DatabaseUtils;
using static SylverInk.XAMLUtils.DataUtils;

namespace SylverInk;

/// <summary>
/// Static helper functions and properties serving multi- or general-purpose needs across the entire project.
/// </summary>
public static partial class CommonUtils
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

	public static string DateFormat { get; } = "yyyy-MM-dd HH:mm:ss";
	public static bool FirstRun { get; set; } = true;
	public static Import? ImportWindow { get => _import; set { _import?.Close(); _import = value; _import?.Show(); } }
	public static bool InitComplete { get; set; }
	public static List<string> LastActiveNotes { get; } = [];
	public static Dictionary<string, double> LastActiveNotesHeight { get; } = [];
	public static Dictionary<string, double> LastActiveNotesLeft { get; } = [];
	public static Dictionary<string, double> LastActiveNotesTop { get; } = [];
	public static Dictionary<string, double> LastActiveNotesWidth { get; } = [];
	public static List<SearchResult> OpenQueries { get; } = [];
	public static NoteRecord? PreviousOpenNote { get; set; }
	public static NoteRecord? RecentSelection { get; set; }
	public static Replace? ReplaceWindow { get => _replace; set { _replace?.Close(); _replace = value; _replace?.Show(); } }
	public static Search? SearchWindow { get => _search; set { _search?.Close(); _search = value; _search?.Show(); } }
	public static ContextSettings Settings { get; } = new();
	public static bool SettingsLoaded { get; set; }
	public static Settings? SettingsWindow { get => _settings; set { _settings?.Close(); _settings = value; _settings?.Show(); } }
	public static bool UpdatesChecked { get; set; }
	public static double WindowHeight { get; set; }
	public static double WindowWidth { get; set; }

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
				A = byte.Parse(data[..2], NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo),
				R = byte.Parse(data[2..4], NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo),
				G = byte.Parse(data[4..6], NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo),
				B = byte.Parse(data[6..8], NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo)
			});
		}
		catch { return Brushes.Transparent; }
	}

	public static string BytesFromBrush(Brush? brush)
	{
		var scb = brush as SolidColorBrush;
		return $"{scb?.Color.A:X2}{scb?.Color.R:X2}{scb?.Color.G:X2}{scb?.Color.B:X2}";
	}

	/// <summary>
	/// Dispatch an action to the main thread for synchronous execution.
	/// </summary>
	/// <param name="callback">The action to be performed on the main thread</param>
	public static void Concurrent(Action callback) => Application.Current.Dispatcher.Invoke(callback);

	/// <summary>
	/// Dispatch a function with no arguments to the main thread for synchronous execution, and return the result of that execution.
	/// </summary>
	/// <param name="callback">The function to be executed on the main thread</param>
	public static T Concurrent<T>(Func<T> callback) => Application.Current.Dispatcher.Invoke(callback);

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

	public static double Lerp(double x, double y, double a) => (y * a) + ((1.0 - a) * x);

	public static string MakeUUID(UUIDType type = UUIDType.Record)
	{
		var time = DateTime.UtcNow;

		var micro = time.Microsecond;

		var seed = (int)(time.Ticks & int.MaxValue);
		var rnd = new Random(seed);

		long mac = rnd.Next();
		for (double i = 4.2; i < 5.0; i += rnd.NextDouble())
			mac += (long)Math.Floor(mac / (rnd.NextDouble() + i));

		mac |= (long)(rnd.Next() & 0xFFFF) << 32;

		var uuid = Guid.NewGuid().ToString();

		uuid = uuid[..^17].ToUpper(CultureInfo.InvariantCulture) + $"{(byte)(micro % 256):X2}{(byte)type:X2}-{mac & 0xFFFF_FFFF_FFFF:X12}";
		return uuid;
	}

	public static SearchResult? OpenQuery(NoteRecord record, bool show = true)
	{
		var db = GetDatabaseFromRecord(record);

		foreach (SearchResult result in OpenQueries)
		{
			if (result.ResultDatabase is not Database rDB)
				continue;

			if (result.ResultRecord is not NoteRecord rNote)
				continue;

			if (!(rDB.Equals(db) && rNote.Equals(record)))
				continue;

			result.Activate();
			result.Focus();
			return result;
		}

		SearchResult resultWindow = new()
		{
			ResultDatabase = db,
			ResultRecord = record
		};

		if (!show)
			return resultWindow;

		resultWindow.Show();
		OpenQueries.Add(resultWindow);
		if (!record.Locked)
			db?.Lock(record.Index, true);

		DeferUpdateRecentNotes();

		return resultWindow;
	}

	public static int ShortFromBytes(byte[] data) =>
		(data[0] << 8)
		+ data[1];

	public static byte[] ShortToBytes(short data) => [
		(byte)((data >> 8) & 0xFF),
		(byte)(data & 0xFF)
	];

	[GeneratedRegex(@"\((\p{Nd}+)\)$")]
	public static partial Regex IndexDigits();
}
