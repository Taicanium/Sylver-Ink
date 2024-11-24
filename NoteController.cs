using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using static SylverInk.Common;

namespace SylverInk
{
	public partial class NoteController
	{
		private short _canCompress = 0; // -1 = Cannot compress, 1 = Can compress, 0 = Not tested.
		private bool _changed = false;
		private int _nextIndex = 0;
		private Serializer? _serializer;

		public bool Changed
		{
			get => _changed;
			set
			{
				_changed = value;
				DatabaseChanged = DatabaseChanged || value;
			}
		}

		public int Format = HighestFormat;
		public bool Loaded = false;
		public string? Name;
		public int RecordCount => Records.Count;
		private List<NoteRecord> Records { get; } = [];
		public string? UUID { get; set; } = MakeUUID(UUIDType.Database);
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
				if (!backup.Equals(string.Empty))
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
			Records.Add(record);
			return record.Index;
		}

		public int CreateRecord(string entry)
		{
			int Index = NextIndex;
			Records.Add(new(Index, entry));
			Changed = true;
			return Index;
		}

		public void CreateRevision(int index, string NewVersion)
		{
			string Current = Records[index].ToString();
			int StartIndex = 0;

			if (NewVersion.Equals(Current))
				return;

			for (int i = 0; i < Math.Min(Current.Length, NewVersion.Length); i++)
			{
				if (!Current[i].Equals(NewVersion[i]))
					break;
				StartIndex = i + 1;
			}

			Changed = true;
			Records[index].Add(new()
			{
				_created = DateTime.UtcNow.ToBinary(),
				_startIndex = StartIndex,
				_substring = StartIndex >= NewVersion.Length ? string.Empty : NewVersion[StartIndex..]
			});
		}

		public void CreateRevision(NoteRecord record, string NewVersion)
		{
			string Current = record.ToString();
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
				_created = DateTime.UtcNow.ToBinary(),
				_startIndex = StartIndex,
				_substring = StartIndex >= NewVersion.Length ? string.Empty : NewVersion[StartIndex..]
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

			Format = _serializer?.DatabaseFormat ?? HighestFormat;

			if (Format >= 7)
			{
				string? _uuid = string.Empty;
				UUID = _serializer?.ReadString(ref _uuid);
			}

			if (!_serializer?.Headless is true)
			{
				string? _name = string.Empty;
				Name = _serializer?.ReadString(ref _name);
			}

			int recordCount = 0;
			_serializer?.ReadInt32(ref recordCount);
			for (int i = 0; i < recordCount; i++)
			{
				NoteRecord record = new();
				AddRecord(record.Deserialize(_serializer));
			}

			PropagateIndices();
			Changed = false;
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

		public NoteRecord GetRecord(int RecordIndex) => RecordIndex < Records.Count ? Records[RecordIndex] : new();

		public void InitializeRecords(bool newDatabase = true)
		{
			for (int i = (OpenQueries ?? []).Count; i > 0; i--)
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
			int NoteCount = 0;
			int ReplaceCount = 0;
			foreach (NoteRecord record in Records)
			{
				var recordText = record.ToString();
				if (!recordText.Contains(oldText, StringComparison.OrdinalIgnoreCase))
					continue;

				var newVersion = recordText.Replace(oldText, newText, StringComparison.OrdinalIgnoreCase);
				ReplaceCount += (recordText.Length - recordText.Replace(oldText, string.Empty, StringComparison.OrdinalIgnoreCase).Length) / oldText.Length;
				NoteCount++;
				CreateRevision(record.Index, newVersion);
			}

			Changed = Changed || ReplaceCount > 0;
			return (ReplaceCount, NoteCount);
		}

		public void Revert(DateTime targetDate)
		{
			for (int i = RecordCount - 1; i > -1; i--)
			{
				var RecordDate = Records[i].GetCreatedObject().ToLocalTime();
				var comparison = RecordDate.CompareTo(targetDate);
				if (comparison > 0)
				{
					DeleteRecord(i);
					continue;
				}

				for (int j = Records[i].GetNumRevisions(); j > 0; j--)
				{
					var RevisionDate = DateTime.FromBinary(Records[i].GetRevision((uint)j - 1U)._created).ToLocalTime();
					comparison = RevisionDate.CompareTo(targetDate);
					if (comparison > 0)
					{
						Records[i].DeleteRevision(j - 1);
						Changed = true;
					}
				}
			}

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

			_serializer?.WriteInt32(Records.Count);
			for (int i = 0; i < Records.Count; i++)
				Records[i].Serialize(_serializer);

			if (inMemory)
				return _serializer?.GetStream();

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
				int recordCount = 0;

				_serializer?.BeginCompressionTest();

				_serializer?.WriteInt32(Records.Count);
				for (int i = 0; i < Records.Count; i++)
					Records[i].Serialize(_serializer);
				_serializer?.WriteString(_name);

				_serializer?.EndCompressionTest();

				_serializer?.ReadInt32(ref recordCount);
				for (int i = 0; i < recordCount; i++)
				{
					NoteRecord record = new();
					record.Deserialize(_serializer);
				}
				_serializer?.ReadString(ref _name);
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
				var matches = Lowercase().Matches(recordText.ToLower());
				foreach (Match match in matches)
				{
					foreach (Group group in match.Groups.Values)
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
				double ratio = 100.0 * value / total;
				WordPercentages[key] = ratio;
			}
		}

		[GeneratedRegex(@"(\p{Ll}+)")]
		private partial Regex Lowercase();
	}
}