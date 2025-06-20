using SylverInk.FileIO;
using SylverInk.XAMLUtils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using static SylverInk.CommonUtils;
using static SylverInk.FileIO.FileUtils;
using static SylverInk.Notes.DatabaseUtils;
using static SylverInk.XAMLUtils.DataUtils;
using static SylverInk.XAMLUtils.TextUtils;

namespace SylverInk.Notes;

public partial class NoteController : IDisposable
{
	private short _canCompress; // -1 = Cannot compress, 1 = Can compress, 0 = Not tested.
	private bool _changed;
	private int _nextIndex;
	private readonly List<NoteRecord> Records = [];
	private Serializer? _serializer;
	private byte? Structure;

	public bool Changed
	{
		get => _changed;
		set
		{
			_changed = value;
			DatabaseChanged = DatabaseChanged || value;
		}
	}

	public bool EnforceNoForwardCompatibility { get; private set; }
	public int Format { get; set; } = HighestSIDBFormat;
	public bool Loaded { get; set; }
	public string? Name { get; set; }
	public int RecordCount => Records.Count;
	public string UUID { get; set; } = MakeUUID(UUIDType.Database);
	public Dictionary<string, double> WordPercentages { get; } = [];

	private int NextIndex
	{
		get
		{
			_nextIndex++;
			return _nextIndex - 1;
		}
		set => _nextIndex = value;
	}

	public NoteController()
	{
		InitializeRecords();
		Loaded = true;
	}

	public NoteController(string dbFile)
	{
		ReloadSerializer();

		if (!File.Exists(dbFile) || !_serializer?.OpenRead(dbFile) is true)
		{
			string backup = FindBackup(dbFile);
			if (!string.IsNullOrWhiteSpace(backup))
			{
				ReloadSerializer();
				if (!_serializer?.OpenRead(backup) is true)
				{
					MessageBox.Show($"Unable to load database file: {dbFile}", "Sylver Ink: Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
					return;
				}
			}
		}

		InitializeRecords();
		ReloadSerializer();
		Loaded = true;
	}

	private int AddRecord(NoteRecord record)
	{
		RecentNotesDirty = true;
		Records.Add(record);
		return record.Index;
	}

	public int CreateRecord(string entry)
	{
		Changed = true;
		return AddRecord(new(NextIndex, PlaintextToXaml(entry)));
	}

	public void CreateRevision(int index, string NewVersion) => CreateRevision(GetRecord(index), NewVersion);

	public void CreateRevision(NoteRecord record, string NewVersion)
	{
		string Current = record.ToXaml();
		int StartIndex = 0;

		if (NewVersion.Equals(Current))
			return;

		for (int i = 0; i < Math.Min(Current.Length, NewVersion.Length); i++)
		{
			if (!Current[i].Equals(NewVersion[i]))
				break;
			StartIndex = i;
		}

		Changed = true;
		record.Add(new()
		{
			Created = DateTime.UtcNow.ToBinary(),
			StartIndex = StartIndex,
			Substring = StartIndex >= NewVersion.Length ? string.Empty : NewVersion[StartIndex..],
			Uuid = MakeUUID(UUIDType.Revision)
		});
	}

	public void DeleteRecord(int index)
	{
		var recordIndex = Records.FindIndex(new(record => record.Index == index));
		Records[recordIndex].Delete();
		Records.RemoveAt(recordIndex);

		PropagateIndices();
		Changed = true;
	}

	public void DeserializeRecords(List<byte>? inMemory = null)
	{
		if (_serializer is null)
			ReloadSerializer();

		if (inMemory is not null)
			_serializer?.OpenRead(string.Empty, inMemory);

		Format = _serializer?.DatabaseFormat ?? HighestSIDBFormat;

		if (Format > HighestSIDBFormat)
		{
			EnforceNoForwardCompatibility = true;
			_serializer?.Close();
			MessageBox.Show($"This database was created in a newer format than the current version of Sylver Ink supports. Please update your installation before opening this database.", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
			return;
		}

		if (Format >= 7)
			UUID = _serializer?.ReadString() ?? MakeUUID(UUIDType.Database);

		if (!_serializer?.Headless is true)
			Name = _serializer?.ReadString();

		if (Format >= 9)
			Structure = _serializer?.ReadByte();

		int recordCount = _serializer?.ReadInt32() ?? 0;
		for (int i = 0; i < recordCount; i++)
		{
			NoteRecord record = new();
			AddRecord(record.Deserialize(_serializer));
		}

		_serializer?.Close();
		PropagateIndices();
		Changed = false;
	}

	public void Dispose()
	{
		_serializer?.Dispose();
		GC.SuppressFinalize(this);
	}

	public override bool Equals(object? obj)
	{
		if (obj is Database otherDB)
		{
			if (!otherDB.Name?.Equals(Name) is true)
				return false;

			if (!otherDB.UUID.Equals(UUID))
				return false;

			return true;
		}

		if (obj is NoteController otherController)
		{
			if (!otherController.Name?.Equals(Name) is true)
				return false;

			if (!otherController.UUID.Equals(UUID))
				return false;

			return true;
		}

		return false;
	}

	public void EraseDatabase()
	{
		PropagateIndices();
		while (RecordCount > 0)
			DeleteRecord(0);

		DeferUpdateRecentNotes();
	}

	private static string FindBackup(string dbFile)
	{
		var Extensionless = Path.GetFileNameWithoutExtension(dbFile);
		for (int i = 1; i < 4; i++)
		{
			string backup = Path.Join(Path.GetDirectoryName(dbFile), $"{Extensionless}_{i}.sibk");
			if (File.Exists(backup))
				return backup;
		}

		return string.Empty;
	}

	public override int GetHashCode() => int.Parse(UUID.Replace("-", string.Empty)[^8..], NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo);

	public NoteRecord GetRecord(int RecordIndex) => RecordIndex < Records.Count && RecordIndex > -1 ? Records[RecordIndex] : new();

	public bool HasRecord(int index)
	{
		try
		{
			if (index < 0)
				return false;

			if (index >= Records.Count)
				return false;

			return Records.ElementAt(index) != null;	
		}
		catch
		{
			return false;
		}
	}

	public void InitializeRecords(bool newDatabase = true)
	{
		for (int i = (OpenQueries ?? []).Count; i > 0; i--)
			if (UUID.Equals(OpenQueries?[i - 1].ResultDatabase?.UUID))
				OpenQueries?[i - 1].Close();

		if (newDatabase)
			Records.Clear();

		DeserializeRecords();
		return;
	}

	public void MakeBackup()
	{
		ReloadSerializer();

		string file = DialogFileSelect(true, 1, Name);

		if (!_serializer?.OpenWrite($"{file}") is true)
			return;

		SerializeRecords();
		ReloadSerializer();
	}
	
	public bool Open(string path, bool writing = false)
	{
		_serializer = new();

		if (writing)
			return _serializer.OpenWrite(path);

		return _serializer.OpenRead(path);
	}

	public void PropagateIndices()
	{
		for (int i = 0; i < RecordCount; i++)
			Records[i].OverwriteIndex(i);

		_nextIndex = RecordCount;
	}

	public void ReloadSerializer()
	{
		_serializer?.Close();
		_serializer = new() { DatabaseFormat = (byte)Format };

		if (_canCompress == -1 || (_canCompress == 0 && !TestCanCompress()))
			_serializer.DatabaseFormat--;
	}

	public (int, int) Replace(string oldText, string newText)
	{
		var newVersion = string.Empty;
		int NoteCount = 0;
		int ReplaceCount = 0;

		for (int i = 0; i < Records.Count; i++)
		{
			var record = Records[i];
			var recordText = record.ToXaml();
			if (!recordText.Contains(oldText, StringComparison.OrdinalIgnoreCase))
				continue;

			for (int j = OpenQueries.Count - 1; j > -1; j--)
			{
				if (record.Equals(OpenQueries[j].ResultRecord))
				{
					Concurrent(OpenQueries[j].SaveRecord);
					Concurrent(OpenQueries[j].Close);
				}
			}

			var document = XamlToFlowDocument(recordText);
			TextPointer? pointer = document.ContentStart;
			while (pointer is not null && pointer.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.None)
			{
				while (pointer is not null && pointer?.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
					pointer = pointer?.GetNextContextPosition(LogicalDirection.Forward);

				if (pointer is null)
					continue;

				var text = pointer.GetTextInRun(LogicalDirection.Forward);
				var textLength = pointer.GetTextRunLength(LogicalDirection.Forward);
				newVersion = text.Replace(oldText, newText, StringComparison.OrdinalIgnoreCase);
				pointer.DeleteTextInRun(textLength);
				pointer.InsertTextInRun(newVersion);
				while (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
					pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
			}
			newVersion = FlowDocumentToXaml(document);
			if (!newVersion.Equals(recordText))
			{
				CreateRevision(record.Index, newVersion);
				NoteCount++;
				ReplaceCount += (recordText.Length - recordText.Replace(oldText, string.Empty, StringComparison.OrdinalIgnoreCase).Length) / oldText.Length;
			}
		}

		Changed = Changed || ReplaceCount > 0;
		return (ReplaceCount, NoteCount);
	}

	public void Revert(DateTime targetDate)
	{
		for (int i = RecordCount - 1; i > -1; i--)
		{
			for (int j = OpenQueries.Count - 1; j > -1; j--)
				if (GetRecord(i).Equals(OpenQueries[j].ResultRecord))
					Concurrent(OpenQueries[j].Close);

			var RecordDate = Records[i].GetCreatedObject().ToLocalTime();
			var comparison = RecordDate.CompareTo(targetDate);
			if (comparison > 0)
			{
				DeleteRecord(i);
				continue;
			}

			for (int j = Records[i].GetNumRevisions(); j > 0; j--)
			{
				var RevisionDate = DateTime.FromBinary(Records[i].GetRevision((uint)j - 1U).Created).ToLocalTime();
				comparison = RevisionDate.CompareTo(targetDate);
				if (comparison <= 0)
					continue;

				Records[i].DeleteRevision(j - 1);
				Changed = true;
			}
		}

		RecentNotesDirty = true;
		PropagateIndices();
		DeferUpdateRecentNotes();
	}

	public List<byte>? SerializeRecords(bool inMemory = false)
	{
		PropagateIndices();

		if (inMemory)
		{
			ReloadSerializer();
			_serializer?.OpenWrite(string.Empty, true);
		}

		if (_serializer?.DatabaseFormat >= 7)
			_serializer?.WriteString(UUID);

		if (!_serializer?.Headless is true)
			_serializer?.WriteString(Name);

		if (_serializer?.DatabaseFormat >= 9)
			_serializer?.WriteByte(Structure ??= 0);

		_serializer?.WriteInt32(Records.Count);
		for (int i = 0; i < Records.Count; i++)
			Records[i].Serialize(_serializer);

		if (inMemory)
			return _serializer?.GetOutgoingStream();

		Changed = false;
		ReloadSerializer();
		return null;
	}

	public void Sort(SortType type = SortType.ByIndex) => Records.Sort(new Comparison<NoteRecord>((_rev1, _rev2) => type switch
	{
		SortType.ByChange => _rev2.GetLastChangeObject().CompareTo(_rev1.GetLastChangeObject()),
		SortType.ByCreation => _rev2.GetCreatedObject().CompareTo(_rev1.GetCreatedObject()),
		SortType.ByIndex => _rev1.Index.CompareTo(_rev2.Index),
		_ => _rev1.Index.CompareTo(_rev2.Index),
	}));

	public bool TestCanCompress()
	{
		if (Changed)
			_canCompress = 0;

		if (_canCompress != 0)
			return _canCompress == 1;

		try
		{
			string? _name = Name;
			byte? _structure = Structure;
			int recordCount = 0;

			_serializer?.BeginCompressionTest();

			_serializer?.WriteInt32(Records.Count);
			for (int i = 0; i < Records.Count; i++)
				Records[i].Serialize(_serializer);
			_serializer?.WriteString(_name);
			_serializer?.WriteByte(_structure ??= 1);

			_serializer?.EndCompressionTest();

			recordCount = _serializer?.ReadInt32() ?? 0;
			for (int i = 0; i < recordCount; i++)
			{
				NoteRecord record = new();
				record.Deserialize(_serializer);
			}
			_serializer?.ReadString();
			_structure = _serializer?.ReadByte();
		}
		catch
		{
			_serializer?.ClearCompressionTest();
			_canCompress = -1;
			ReloadSerializer();
			return false;
		}

		_serializer?.ClearCompressionTest();
		_canCompress = 1;
		ReloadSerializer();
		return true;
	}

	public void UpdateWordPercentages()
	{
		uint total = 0U;
		WordPercentages.Clear();

		foreach (NoteRecord record in Records)
		{
			string recordText = record.ToString();
			var matches = Lowercase().Matches(recordText.ToLowerInvariant());
			foreach (Match m in matches)
			{
				foreach (Group group in m.Groups.Values)
				{
					WordPercentages.TryAdd(group.Value, 0.0);
					WordPercentages[group.Value]++;
					total++;
				}
			}
		}

		foreach (string key in WordPercentages.Keys.ToList())
		{
			double value = WordPercentages[key];
			WordPercentages[key] = 100.0 * value / total;
		}
	}

	[GeneratedRegex(@"(\p{Ll}+)")]
	private partial Regex Lowercase();
}