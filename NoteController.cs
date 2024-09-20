using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SylverInk
{
	partial class NoteController
	{
		private static int _nextIndex = 0;

		public static int RecordCount => Records.Count;
		private static List<NoteRecord> Records { get; } = [];
		public static Dictionary<string, uint> WordPercentages { get; } = [];

		private static int NextIndex
		{
			get
			{
				_nextIndex++;
				return _nextIndex - 1;
			}
			set { _nextIndex = value; }
		}

		public enum SortType
		{
			ByIndex,
			ByChange,
			ByCreation
		}

		private static int AddRecord(NoteRecord record)
		{
			Records.Add(record);
			return record.GetIndex();
		}

		public static int CreateRecord(string entry, bool dummy = false)
		{
			int Index = NextIndex;
			Records.Add(new(Index, entry, dummy ? DateTime.UtcNow.AddMinutes(new Random().NextDouble() * 43200.0 - 43200.0).ToBinary() : -1));
			Common.DatabaseChanged = true;
			return Index;
		}

		public static void CreateRevision(int index, string NewVersion)
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

			Records[index].Add(new()
			{
				_created = DateTime.UtcNow.ToBinary(),
				_startIndex = StartIndex,
				_substring = StartIndex >= NewVersion.Length ? string.Empty : NewVersion[StartIndex..]
			});

			Common.DatabaseChanged = true;
		}

		public static void DeleteRecord(int index)
		{
			Records[index].Delete();
			Records.RemoveAt(index);
			PropagateIndices();
			Common.DatabaseChanged = true;
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

			Common.DeferUpdateRecentNotes();
		}

		public static NoteRecord GetRecord(int RecordIndex) => Records[RecordIndex];

		public static void InitializeRecords(bool newDatabase = true, bool dummyData = true)
		{
			if (!newDatabase)
			{
				DeserializeRecords();
				return;
			}

			Records.Clear();

			if (dummyData)
			{
				var newCount = new Random().Next(40, 160);
				for (int i = 0; i < newCount; i++)
				{
					var newText = Common.MakeDummySearchResult();
					CreateRecord(newText, true);
				}
			}

			PropagateIndices();
			Common.DatabaseChanged = true;
		}

		private static void PropagateIndices()
		{
			for (int i = 0; i < RecordCount; i++)
				Records[i].OverwriteIndex(i);

			_nextIndex = RecordCount;
		}

		public static (int, int) Replace(string oldText, string newText)
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
				CreateRevision(record.GetIndex(), newVersion);
			}

			Common.DatabaseChanged = Common.DatabaseChanged || ReplaceCount > 0;
			PropagateIndices();
			return (ReplaceCount, NoteCount);
		}

		public static void Revert(DateTime targetDate)
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
						Records[i].DeleteRevision(j - 1);
				}
			}

			PropagateIndices();
			Common.DeferUpdateRecentNotes();
		}

		public static void SerializeRecords()
		{
			PropagateIndices();

			Serializer.WriteInt32(Records.Count);
			for (int i = 0; i < Records.Count; i++)
				Records[i].Serialize();
		}

		public static void Sort(SortType type = SortType.ByIndex)
		{
			switch (type)
			{
				case SortType.ByIndex:
					Records.Sort(new Comparison<NoteRecord>(
						(_rev1, _rev2) => _rev1.GetIndex().CompareTo(_rev2.GetIndex())
						));
					return;
				case SortType.ByChange:
					Records.Sort(new Comparison<NoteRecord>(
						(_rev2, _rev1) => _rev1.GetLastChangeObject().CompareTo(_rev2.GetLastChangeObject())
						));
					return;
				case SortType.ByCreation:
					Records.Sort(new Comparison<NoteRecord>(
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

				Serializer.WriteInt32(Records.Count);
				for (int i = 0; i < Records.Count; i++)
					Records[i].Serialize();

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

			foreach (NoteRecord record in Records)
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