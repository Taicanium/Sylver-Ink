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
		private long _created = -1;
		private bool _dirty = true;
		private int _index = -1;
		private string? _initial = string.Empty;
		private long _lastChange = -1;
		private DateTime _lastChangeObject = DateTime.UtcNow;
		private string _latest = string.Empty;
		private string _previewText = string.Empty;
		private int _previewWidth = 375;
		private readonly List<NoteRevision> _revisions = [];
		private readonly List<string> _tags = [];
		private bool _tagsDirty = true;

		public int LastMatchCount { get; private set; } = 0;

		public string Preview
		{
			get => _previewText;

			set
			{
				_dirty = false;
				_previewText = ToString().Replace("\r", string.Empty).Replace("\n", " ").Replace("\t", " ");
				if (_previewText.Length > 150)
				{
					_previewText = _previewText[..150];
					_dirty = true;
				}

				_previewWidth = int.Parse(value);

				while (Common.MeasureTextSize(_previewText) > _previewWidth)
				{
					_previewText = _previewText[..^4];
					_dirty = true;
				}

				if (_dirty)
					_previewText += "...";
			}
		}

		public string ShortChange
		{
			get
			{
				_lastChangeObject = DateTime.FromBinary(_lastChange);
				var now = DateTime.UtcNow;
				var diff = now - _lastChangeObject;

				if (diff.TotalHours < 24.0)
					return _lastChangeObject.ToLocalTime().ToShortTimeString();

				if (diff.TotalHours < 168.0)
					return $"{diff.Days} day{(diff.Days > 1 ? "s" : string.Empty)} ago";

				return _lastChangeObject.ToLocalTime().ToShortDateString();
			}
		}

		public NoteRecord()
		{
			_created = DateTime.UnixEpoch.ToBinary();
			_index = -1;
			_initial = string.Empty;
			_lastChange = _created;
			_tagsDirty = true;
		}

		public NoteRecord(int _index, string _initial, long _created = -1)
		{
			this._created = _created == -1 ? DateTime.UtcNow.ToBinary() : _created;
			this._index = _index;
			this._initial = _initial;
			_lastChange = this._created;
			_tagsDirty = true;
		}

		public void Add(NoteRevision _revision)
		{
			_revisions.Add(_revision);
			_tagsDirty = true;
			var revisionTime = DateTime.FromBinary(_revision._created);
			var noteTime = DateTime.FromBinary(_lastChange);

			_revisions.Sort(new Comparison<NoteRevision>(
				(_rev1, _rev2) => DateTime.FromBinary(_rev1._created).CompareTo(DateTime.FromBinary(_rev2._created))
				));

			if (revisionTime.CompareTo(noteTime) > 0)
			{
				_lastChange = _revision._created;
				_lastChangeObject = DateTime.FromBinary(_lastChange);
			}
		}

		public void Delete()
		{
			_index = 0;
			_initial = string.Empty;
			_lastChange = DateTime.UtcNow.ToBinary();
			_revisions.Clear();
			_tagsDirty = true;
		}

		public void DeleteRevision(int index)
		{
			if (index >= GetNumRevisions())
				return;

			_revisions.RemoveAt(index);
			Common.DatabaseChanged = true;
		}

		public NoteRecord Deserialize()
		{
			Serializer.ReadLong(ref _created);
			Serializer.ReadInt32(ref _index);
			Serializer.ReadString(ref _initial);
			Serializer.ReadLong(ref _lastChange);

			int _revisionsCount = 0;
			Serializer.ReadInt32(ref _revisionsCount);
			for (int i = 0; i < _revisionsCount; i++)
			{
				NoteRevision _revision = new();
				Serializer.ReadLong(ref _revision._created);
				Serializer.ReadInt32(ref _revision._startIndex);
				Serializer.ReadString(ref _revision._substring);
				_revisions.Add(_revision);
			}

			return this;
		}

		private int ExtractTags()
		{
			if (!_tagsDirty)
				return _tags.Count;

			_tags.Clear();

			var recordText = ToString();
			var matches = NonWhitespace().Matches(recordText);
			foreach (Match match in matches)
			{
				foreach (Group group in match.Groups.Values)
				{
					var val = group.Value.ToLower();
					if (!NoteController.WordPercentages.ContainsKey(val))
						continue;

					if (NoteController.WordPercentages[val] < Math.Max(20, 100 - NoteController.WordPercentages.Count))
						_tags.Add(val);
				}
			}

			_tagsDirty = false;
			return _tags.Count;
		}

		public string GetCreated() => GetCreatedObject().ToString("yyyy-MM-dd HH:mm:ss");

		public DateTime GetCreatedObject() => DateTime.FromBinary(_created);

		public int GetIndex() => _index;

		public DateTime GetLastChangeObject() => DateTime.FromBinary(_lastChange);

		public string GetLastChange() => GetLastChangeObject().ToString("yyyy-MM-dd HH:mm:ss");

		public int GetNumRevisions() => _revisions.Count;

		public NoteRevision GetRevision(uint index) => _revisions[_revisions.Count - 1 - (int)index];

		public string GetRevisionTime(uint index) => DateTime.FromBinary(GetRevision(index)._created).ToString("yyyy-MM-dd HH:mm:ss");

		public int MatchTags(string text)
		{
			var format = text.Trim();
			var matches = NonWhitespace().Matches(format);
			int outCount = 0;

			ExtractTags();

			foreach (Match match in matches)
			{
				foreach (Group group in match.Groups.Values)
				{
					if (_tags.Contains(group.Value.ToLower()))
						outCount++;
				}
			}

			return LastMatchCount = outCount;
		}

		public int OverwriteIndex(int _index)
		{
			this._index = _index;
			return this._index;
		}

		public string Reconstruct(uint backsteps = 0U)
		{
			_latest = _initial ?? string.Empty;
			if (_revisions.Count == 0)
				return _latest;

			for (int i = 0; i < _revisions.Count - Math.Min(backsteps, _revisions.Count); i++)
			{
				if (_revisions[i]._startIndex < _latest?.Length)
					_latest = _latest.Remove(_revisions[i]._startIndex);
				_latest += _revisions[i]._substring;
			}

			return _latest ?? string.Empty;
		}

		public void Serialize()
		{
			Serializer.WriteLong(_created);
			Serializer.WriteInt32(_index);
			Serializer.WriteString(_initial);
			Serializer.WriteLong(_lastChange);

			Serializer.WriteInt32(_revisions.Count);
			for (int i = 0; i < _revisions.Count; i++)
			{
				Serializer.WriteLong(_revisions[i]._created);
				Serializer.WriteInt32(_revisions[i]._startIndex);
				Serializer.WriteString(_revisions[i]._substring);
			}
		}

		public override string ToString() => Reconstruct();

		[GeneratedRegex(@"\S+")]
		private static partial Regex NonWhitespace();
	}
}
