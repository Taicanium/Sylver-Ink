using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SylverInk
{
	partial class NoteController
	{
		private static int _nextIndex = 0;
		private readonly static List<NoteRecord> _records = [];
		public readonly static Dictionary<string, uint> WordPercentages = [];

		public static int RecordCount => _records.Count;

		public enum SortType
		{
			ByIndex,
			ByChange,
			ByCreation
		}

		public static int NextIndex
		{
			get
			{
				_nextIndex++;
				return _nextIndex - 1;
			}
			set { _nextIndex = value; }
		}

		public static int AddRecord(NoteRecord record)
		{
			_records.Add(record);
			return record.GetIndex();
		}

		public static int CreateRecord(string entry)
		{
			int Index = NextIndex;
			_records.Add(new(Index, entry));
			Common.DatabaseChanged = true;
			return Index;
		}

		public static void CreateRevision(int index, string NewVersion)
		{
			string Current = _records[index].ToString();
			int StartIndex = 0;

			if (NewVersion.Equals(Current))
				return;

			for (int i = 0; i < Math.Min(Current.Length, NewVersion.Length); i++)
			{
				if (!Current[i].Equals(NewVersion[i]))
					break;
				StartIndex = i + 1;
			}

			_records[index].Add(new()
			{
				_created = DateTime.UtcNow.ToBinary(),
				_startIndex = StartIndex,
				_substring = StartIndex >= NewVersion.Length ? string.Empty : NewVersion[StartIndex..]
			});

			Common.DatabaseChanged = true;
		}

		public static void DeleteRecord(int index)
		{
			_records[index].Delete();
			_records.RemoveAt(index);
			PropagateIndices();
		}

		private static void DeserializeRecords()
		{
			int recordCount = 0;
			Serializer.ReadInt32(ref recordCount);
			for (int i = 0; i < recordCount; i++)
			{
				NoteRecord record = new();
				AddRecord(record.Deserialize());
			}

			PropagateIndices();
		}

		public static void EraseDatabase()
		{
			while (RecordCount > 0)
				DeleteRecord(0);
		}

		public static NoteRecord GetRecord(int RecordIndex) => _records[RecordIndex];

		public static void InitializeRecords(bool newDatabase = true, bool dummyData = true)
		{
			if (!newDatabase)
			{
				DeserializeRecords();
				return;
			}

			_records.Clear();

			if (dummyData)
			{
				var newCount = new Random().Next(15, 100);
				for (int i = 0; i < newCount; i++)
				{
					var newText = Common.MakeDummySearchResult();
					CreateRecord(newText);
				}
			}

			PropagateIndices();
			Common.DatabaseChanged = true;
		}

		private static void PropagateIndices()
		{
			for (int i = 0; i < RecordCount; i++)
				_records[i].OverwriteIndex(i);

			_nextIndex = RecordCount;
		}

		public static (int, int) Replace(string oldText, string newText)
		{
			int NoteCount = 0;
			int ReplaceCount = 0;
			foreach (NoteRecord record in _records)
			{
				var recordText = record.ToString();
				if (!recordText.Contains(oldText, StringComparison.OrdinalIgnoreCase))
					continue;

				var newVersion = recordText.Replace(oldText, newText, StringComparison.OrdinalIgnoreCase);
				ReplaceCount += (recordText.Length - recordText.Replace(oldText, string.Empty, StringComparison.OrdinalIgnoreCase).Length) / oldText.Length;
				NoteCount++;
				CreateRevision(record.GetIndex(), newVersion);
			}

			Common.DatabaseChanged = Common.DatabaseChanged || ReplaceCount > 0;
			PropagateIndices();
			return (ReplaceCount, NoteCount);
		}

		public static void SerializeRecords()
		{
			PropagateIndices();

			Serializer.WriteInt32(_records.Count);
			for (int i = 0; i < _records.Count; i++)
				_records[i].Serialize();
		}

		public static void Sort(SortType type = SortType.ByIndex)
		{
			switch (type)
			{
				case SortType.ByIndex:
					_records.Sort(new Comparison<NoteRecord>(
						(_rev1, _rev2) => _rev1.GetIndex().CompareTo(_rev2.GetIndex())
						));
					return;
				case SortType.ByChange:
					_records.Sort(new Comparison<NoteRecord>(
						(_rev2, _rev1) => _rev1.GetLastChangeObject().CompareTo(_rev2.GetLastChangeObject())
						));
					return;
				case SortType.ByCreation:
					_records.Sort(new Comparison<NoteRecord>(
						(_rev1, _rev2) => _rev1.GetCreatedObject().CompareTo(_rev2.GetCreatedObject())
						));
					return;
			}
		}

		public static bool TestCanCompress()
		{
			try
			{
				Serializer.BeginCompressionTest();

				Serializer.WriteInt32(_records.Count);
				for (int i = 0; i < _records.Count; i++)
					_records[i].Serialize();

				Serializer.EndCompressionTest();

				int recordCount = 0;
				Serializer.ReadInt32(ref recordCount);
				for (int i = 0; i < recordCount; i++)
				{
					NoteRecord record = new();
					record.Deserialize();
				}
			}
			catch (ApplicationException)
			{
				Serializer.ClearCompressionTest();
				return false;
			}

			Serializer.ClearCompressionTest();
			return true;
		}

		public static void UpdateWordPercentages()
		{
			uint total = 0U;
			WordPercentages.Clear();

			foreach (NoteRecord record in _records)
			{
				string recordText = record.ToString();
				var matches = NonWhitespace().Matches(recordText);
				foreach (Match match in matches)
				{
					foreach (Group group in match.Groups.Values)
					{
						if (!WordPercentages.ContainsKey(group.Value))
							WordPercentages.Add(group.Value, 0);

						WordPercentages[group.Value]++;
						total++;
					}
				}
			}

			foreach (string key in WordPercentages.Keys.ToList())
			{
				double value = WordPercentages[key];
				WordPercentages[key] = (uint)Math.Floor(value/total);
			}
		}

		[GeneratedRegex(@"(\S+)")]
		private static partial Regex NonWhitespace();
	}
}