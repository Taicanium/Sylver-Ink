using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SylverInk
{
	public struct NoteRevision
	{
		public long _created;
		public int _startIndex;
		public string? _substring;
	}

	public partial class NoteRecord
	{
		private long Created = -1;
		private bool Dirty = true;
		private string? Initial = string.Empty;
		private long LastChange = -1;
		private DateTime LastChangeObject = DateTime.UtcNow;
		private string Latest = string.Empty;
		private string PreviewText = string.Empty;
		private int PreviewWidth = 375;
		private readonly List<NoteRevision> Revisions = [];
		private readonly List<string> Tags = [];
		private bool TagsDirty = true;

		public int Index = -1;
		public int LastMatchCount { get; private set; } = 0;

		public string Preview
		{
			get => PreviewText;

			set
			{
				Dirty = false;
				PreviewText = ToString().Replace("\r", string.Empty).Replace("\n", " ").Replace("\t", " ");
				if (PreviewText.Length > 225)
				{
					PreviewText = PreviewText[..225];
					Dirty = true;
				}

				PreviewWidth = int.Parse(value);

				while (Common.MeasureTextSize(PreviewText) > PreviewWidth)
				{
					PreviewText = PreviewText[..^4];
					Dirty = true;
				}

				if (Dirty)
					PreviewText += "...";
			}
		}

		public string ShortChange
		{
			get
			{
				LastChangeObject = DateTime.FromBinary(LastChange);
				var dtObject = Common.RecentEntriesSortMode switch
				{
					NoteController.SortType.ByCreation => GetCreatedObject(),
					_ => LastChangeObject,
				};
				var now = DateTime.UtcNow;
				var diff = now - dtObject;

				if (diff.TotalHours < 24.0)
					return dtObject.ToLocalTime().ToShortTimeString();

				if (diff.TotalHours < 168.0)
					return $"{diff.Days} day{(diff.Days > 1 ? "s" : string.Empty)} ago";

				return dtObject.ToLocalTime().ToShortDateString();
			}
		}

		public NoteRecord()
		{
			Created = DateTime.UnixEpoch.ToBinary();
			Index = -1;
			Initial = string.Empty;
			LastChange = Created;
			LastChangeObject = DateTime.FromBinary(LastChange);
			TagsDirty = true;
		}

		public NoteRecord(int Index, string Initial, long Created = -1)
		{
			this.Created = Created == -1 ? DateTime.UtcNow.ToBinary() : Created;
			this.Index = Index;
			this.Initial = Initial;
			LastChange = this.Created;
			LastChangeObject = DateTime.FromBinary(LastChange);
			TagsDirty = true;
		}

		public void Add(NoteRevision _revision)
		{
			Revisions.Add(_revision);
			TagsDirty = true;
			
			if (_revision._created == -1)
				_revision._created = DateTime.UtcNow.ToBinary();

			var revisionTime = DateTime.FromBinary(_revision._created);

			Revisions.Sort(new Comparison<NoteRevision>(
				(_rev1, _rev2) => DateTime.FromBinary(_rev1._created).CompareTo(DateTime.FromBinary(_rev2._created))
				));

			if (revisionTime.CompareTo(LastChangeObject) > 0)
			{
				LastChange = _revision._created;
				LastChangeObject = DateTime.FromBinary(LastChange);
			}
		}

		public void Delete()
		{
			Index = 0;
			Initial = string.Empty;
			LastChange = DateTime.UtcNow.ToBinary();
			Revisions.Clear();
			TagsDirty = true;
		}

		public void DeleteRevision(int index)
		{
			if (index >= GetNumRevisions())
				return;

			Revisions.RemoveAt(index);
		}

		public NoteRecord Deserialize(Serializer? _serializer)
		{
			_serializer?.ReadLong(ref Created);
			_serializer?.ReadInt32(ref Index);
			_serializer?.ReadString(ref Initial);
			_serializer?.ReadLong(ref LastChange);

			int RevisionsCount = 0;
			_serializer?.ReadInt32(ref RevisionsCount);
			for (int i = 0; i < RevisionsCount; i++)
			{
				NoteRevision _revision = new();
				_serializer?.ReadLong(ref _revision._created);
				_serializer?.ReadInt32(ref _revision._startIndex);
				_serializer?.ReadString(ref _revision._substring);
				Revisions.Add(_revision);
			}

			return this;
		}

		private int ExtractTags()
		{
			if (!TagsDirty)
				return Tags.Count;

			Tags.Clear();

			var recordText = ToString();
			var matches = Lowercase().Matches(recordText.ToLower());
			foreach (Match match in matches)
			{
				foreach (Group group in match.Groups.Values)
				{
					var val = group.Value.ToLower();

					if (Tags.Contains(val))
						continue;

					if (!Common.CurrentDatabase.Controller.WordPercentages.ContainsKey(val))
						continue;

					if (Common.CurrentDatabase.Controller.WordPercentages[val] < Math.Max(0.2, 100.0 - Common.CurrentDatabase.Controller.WordPercentages.Count))
						Tags.Add(val);
				}
			}

			TagsDirty = false;
			return Tags.Count;
		}

		public string GetCreated() => GetCreatedObject().ToString("yyyy-MM-dd HH:mm:ss");

		public DateTime GetCreatedObject() => DateTime.FromBinary(Created);

		public DateTime GetLastChangeObject() => DateTime.FromBinary(LastChange);

		public string GetLastChange() => GetLastChangeObject().ToString("yyyy-MM-dd HH:mm:ss");

		public int GetNumRevisions() => Revisions.Count;

		public NoteRevision GetRevision(uint index) => Revisions[Revisions.Count - 1 - (int)index];

		public string GetRevisionTime(uint index) => DateTime.FromBinary(GetRevision(index)._created).ToString("yyyy-MM-dd HH:mm:ss");

		public int MatchTags(string text)
		{
			var format = text.Trim();
			var matches = Lowercase().Matches(format.ToLower());
			int outCount = 0;

			ExtractTags();

			foreach (Match match in matches)
			{
				foreach (Group group in match.Groups.Values)
				{
					if (Tags.Contains(group.Value.ToLower()))
						outCount++;
				}
			}

			return LastMatchCount = outCount;
		}

		public int OverwriteIndex(int Index)
		{
			this.Index = Index;
			return this.Index;
		}

		public string Reconstruct(uint backsteps = 0U)
		{
			Latest = Initial ?? string.Empty;
			if (Revisions.Count == 0)
				return Latest;

			for (int i = 0; i < Revisions.Count - Math.Min(backsteps, Revisions.Count); i++)
			{
				if (Revisions[i]._startIndex > -1 && Revisions[i]._startIndex < Latest?.Length)
					Latest = Latest.Remove(Revisions[i]._startIndex);
				Latest += Revisions[i]._substring;
			}

			return Latest ?? string.Empty;
		}

		public void Serialize(Serializer? _serializer)
		{
			_serializer?.WriteLong(Created);
			_serializer?.WriteInt32(Index);
			_serializer?.WriteString(Initial);
			_serializer?.WriteLong(LastChange);

			_serializer?.WriteInt32(Revisions.Count);
			for (int i = 0; i < Revisions.Count; i++)
			{
				_serializer?.WriteLong(Revisions[i]._created);
				_serializer?.WriteInt32(Revisions[i]._startIndex);
				_serializer?.WriteString(Revisions[i]._substring);
			}
		}

		public override string ToString() => Reconstruct();

		[GeneratedRegex(@"(\p{Ll}+)")]
		private static partial Regex Lowercase();
	}
}
