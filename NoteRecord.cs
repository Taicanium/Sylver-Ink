using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using static SylverInk.Common;

namespace SylverInk
{
	public struct NoteRevision(long _created = -1, int _startIndex = -1, string? _substring = null, string? _uuid = null)
	{
		public long _created = _created;
		public int _startIndex = _startIndex;
		public string? _substring = _substring;
		public string? _uuid = _uuid ?? MakeUUID(UUIDType.Revision);
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
		public bool Locked { get; private set; } = false;
		public string? UUID;

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

				PreviewWidth = (int)Math.Round(double.Parse(value));

				var width = MeasureTextWidth(PreviewText);
				while (width > PreviewWidth)
				{
					PreviewText = PreviewText[..^1];
					Dirty = true;
					width = MeasureTextWidth(PreviewText);
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
				var dtObject = RecentEntriesSortMode switch
				{
					SortType.ByCreation => GetCreatedObject(),
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
			UUID = MakeUUID();
		}

		public NoteRecord(int Index, string Initial, long Created = -1, string? UUID = null)
		{
			this.Created = Created == -1 ? DateTime.UtcNow.ToBinary() : Created;
			this.Index = Index;
			this.Initial = Initial;
			LastChange = this.Created;
			LastChangeObject = DateTime.FromBinary(LastChange);
			this.UUID = UUID ?? MakeUUID();
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
			if (_serializer?.DatabaseFormat >= 5)
				_serializer?.ReadString(ref UUID);
			_serializer?.ReadLong(ref Created);
			_serializer?.ReadInt32(ref Index);
			_serializer?.ReadString(ref Initial);
			_serializer?.ReadLong(ref LastChange);

			int RevisionsCount = 0;
			_serializer?.ReadInt32(ref RevisionsCount);
			for (int i = 0; i < RevisionsCount; i++)
			{
				NoteRevision _revision = new();
				if (_serializer?.DatabaseFormat >= 7)
					_serializer?.ReadString(ref _revision._uuid);
				_serializer?.ReadLong(ref _revision._created);
				_serializer?.ReadInt32(ref _revision._startIndex);
				_serializer?.ReadString(ref _revision._substring);
				Revisions.Add(_revision);
			}

			return this;
		}

		public override bool Equals(object? obj)
		{
			if (!GetType().Equals(obj?.GetType()))
				return false;

			var recordObj = (NoteRecord?)obj;
			return base.Equals(obj) ||
				(UUID?.Equals(recordObj?.UUID ?? string.Empty) is true) ||
				(Created.Equals(recordObj?.Created) && Index.Equals(recordObj?.Index) && Initial?.Equals(recordObj?.Initial) is true && LastChange.Equals(recordObj?.LastChange));
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

					if (!CurrentDatabase.WordPercentages.ContainsKey(val))
						continue;

					// To be treated as a tag, a word must be less common than 0.2% of all words across the entire database.
					if (CurrentDatabase.WordPercentages[val] < Math.Max(0.2, 100.0 - CurrentDatabase.WordPercentages.Count))
						Tags.Add(val);
				}
			}

			TagsDirty = false;
			return Tags.Count;
		}

		public string GetCreated() => GetCreatedObject().ToString("yyyy-MM-dd HH:mm:ss");

		public DateTime GetCreatedObject() => DateTime.FromBinary(Created);

		public override int GetHashCode() => int.Parse((UUID ??= MakeUUID())[^4..], System.Globalization.NumberStyles.HexNumber);

		public DateTime GetLastChangeObject() => DateTime.FromBinary(LastChange);

		public string GetLastChange() => GetLastChangeObject().ToString("yyyy-MM-dd HH:mm:ss");

		public int GetNumRevisions() => Revisions.Count;

		public NoteRevision GetRevision(uint index) => Revisions[Revisions.Count - 1 - (int)index];

		public string GetRevisionTime(uint index) => DateTime.FromBinary(GetRevision(index)._created).ToString("yyyy-MM-dd HH:mm:ss");

		public void Lock()
		{
			Locked = true;
		}

		public int MatchTags(string text)
		{
			var format = text.Trim();
			var matches = Lowercase().Matches(format.ToLower());
			int outCount = 0;

			ExtractTags();

			foreach (Match match in matches)
				foreach (Group group in match.Groups.Values)
					if (Tags.Contains(group.Value.ToLower()))
						outCount++;

			return LastMatchCount = outCount;
		}

		public void OverwriteIndex(int Index) => this.Index = Index;

		/// <summary>
		/// <para>Reverts this record to a previous state by applying each of its stored revisions while leaving a requested count undone, specified by <paramref name="backsteps"/>.</para>
		/// </summary>
		/// <param name="backsteps">The number of revisions to undo, or 0 for the current state of the record.</param>
		/// <returns>The text of this record after undoing the requested number of revisions.</returns>
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
			if (_serializer?.DatabaseFormat >= 5)
				_serializer?.WriteString(UUID);
			_serializer?.WriteLong(Created);
			_serializer?.WriteInt32(Index);
			_serializer?.WriteString(Initial);
			_serializer?.WriteLong(LastChange);

			_serializer?.WriteInt32(Revisions.Count);
			for (int i = 0; i < Revisions.Count; i++)
			{
				if (_serializer?.DatabaseFormat >= 7)
					_serializer?.WriteString(Revisions[i]._uuid);
				_serializer?.WriteLong(Revisions[i]._created);
				_serializer?.WriteInt32(Revisions[i]._startIndex);
				_serializer?.WriteString(Revisions[i]._substring);
			}
		}

		public override string ToString() => Reconstruct(0U);

		public void Unlock()
		{
			Locked = false;

			foreach (var query in OpenQueries)
			{
				if (query.ResultRecord?.Equals(Index) is true)
				{
					query.LastChangedLabel.Content = "Last modified: " + CurrentDatabase.GetRecord(Index).GetLastChange();
					query.ResultBlock.IsEnabled = true;
				}
			}

			foreach (var item in OpenTabs)
			{
				if (Index != (int)(item.Tab.Tag ?? -1))
					continue;

				var grid = (Grid)item.Tab.Content;
				foreach (UIElement child in grid.Children)
				{
					child.SetValue(UIElement.IsEnabledProperty, true);
					if (child.GetType().Equals(typeof(Label)))
						((Label)child).Content = "Entry last modified: " + GetLastChange();
				}
			}
		}

		[GeneratedRegex(@"(\p{Ll}+)")]
		private static partial Regex Lowercase();
	}
}
