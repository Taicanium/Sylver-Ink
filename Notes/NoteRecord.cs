using SylverInk.FileIO;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using static SylverInk.CommonUtils;
using static SylverInk.Notes.DatabaseUtils;
using static SylverInk.XAMLUtils.DataUtils;
using static SylverInk.XAMLUtils.TextUtils;

namespace SylverInk.Notes;

public struct NoteRevision(long created = -1, int startIndex = -1, string? substring = null, string? uuid = null)
{
	public long Created { get; set; } = created;
	public int StartIndex { get; set; } = startIndex;
	public string? Substring { get; set; } = substring;
	public string? Uuid { get; set; } = uuid ?? MakeUUID(UUIDType.Revision);
}

public partial class NoteRecord
{
	private long Created = -1;
	private string? Initial = string.Empty;
	private long LastChange = -1;
	private DateTime LastChangeObject = DateTime.UtcNow;
	private string LastQuery = string.Empty;
	private readonly List<NoteRevision> Revisions = [];
	private readonly List<string> Tags = [];
	private bool TagsDirty = true;

	private int index = -1;
	public int LastMatchCount { get; set; }
	public bool Locked { get; set; }
	private string? uuid;

	public string FullDateChange
	{
		get
		{
			LastChangeObject = DateTime.FromBinary(LastChange);

			var dtObject = RecentEntriesSortMode switch
			{
				SortType.ByCreation => GetCreatedObject(),
				_ => LastChangeObject,
			};

			dtObject = dtObject.ToLocalTime();

			return $"{dtObject.ToShortDateString()} {dtObject.ToShortTimeString()}";
		}
	}

	public string Preview
	{
		get => GetPlaintext().Replace("\r", string.Empty).Replace('\n', ' ').Replace('\t', ' ');
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

			var diff = DateTime.UtcNow - dtObject;

			if (diff.TotalHours < 24.0)
				return dtObject.ToLocalTime().ToShortTimeString();

			if (diff.TotalHours < 168.0)
				return $"{diff.Days} day{(diff.Days > 1 ? "s" : string.Empty)} ago";

			return dtObject.ToLocalTime().ToShortDateString();
		}
	}

	public int Index { get => index; set => index = value; }
	public string? UUID { get => uuid; set => uuid = value; }

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

	public void Add(NoteRevision revision)
	{
		if (revision.Created == -1)
			revision.Created = DateTime.UtcNow.ToBinary();

		if (DateTime.FromBinary(revision.Created).CompareTo(LastChangeObject) > 0)
		{
			LastChange = revision.Created;
			LastChangeObject = DateTime.FromBinary(LastChange);
		}

		revision.Uuid ??= MakeUUID(UUIDType.Revision);

		Revisions.Add(revision);
		TagsDirty = true;

		RecentNotesDirty = true;
		DeferUpdateRecentNotes();
	}

	public void Delete()
	{
		Index = 0;
		Initial = string.Empty;
		LastChange = DateTime.UtcNow.ToBinary();
		Revisions.Clear();
		TagsDirty = true;

		RecentNotesDirty = true;
		DeferUpdateRecentNotes();
	}

	// In its current state, this function is only well-behaved when removing all subsequent revisions in addition to the one marked for deletion.
	public void DeleteRevision(int index)
	{
		if (index >= GetNumRevisions())
			return;

		Revisions.RemoveAt(index);

		LastChange = GetNumRevisions() == 0 ? Created : Revisions[GetNumRevisions() - 1].Created;
		LastChangeObject = DateTime.FromBinary(LastChange);

		RecentNotesDirty = true;
		DeferUpdateRecentNotes();
	}

	public NoteRecord Deserialize(Serializer? serializer)
	{
		if (serializer?.DatabaseFormat >= 5)
			UUID = serializer?.ReadString();
		Created = serializer?.ReadLong() ?? DateTime.UtcNow.ToBinary();
		Index = serializer?.ReadInt32() ?? -1;
		Initial = serializer?.ReadString();
		LastChange = serializer?.ReadLong() ?? DateTime.UtcNow.ToBinary();

		int RevisionsCount = serializer?.ReadInt32() ?? 0;
		for (int i = 0; i < RevisionsCount; i++)
		{
			NoteRevision _revision = new();
			if (serializer?.DatabaseFormat >= 7)
				_revision.Uuid = serializer?.ReadString();
			_revision.Created = serializer?.ReadLong() ?? DateTime.UtcNow.ToBinary();
			_revision.StartIndex = serializer?.ReadInt32() ?? 0;
			_revision.Substring = serializer?.ReadString();
			Add(_revision);
		}

		// SIDB v.9 introduced XAML rich text formatting. Its absence in earlier versions must be accounted for.
		if (serializer?.DatabaseFormat < 9)
			TargetXaml();

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
		var matches = Lowercase().Matches(recordText.ToLowerInvariant());
		foreach (Match match in matches)
		{
			foreach (Group group in match.Groups.Values)
			{
				var val = group.Value.ToLowerInvariant();

				if (Tags.Contains(val))
					continue;

				foreach (Database db in Databases)
				{
					if (!db.WordPercentages.ContainsKey(val))
						continue;

					// To be treated as a tag, a word must be less common than 0.1% of all words in at least one database.
					if (db.WordPercentages[val] < Math.Max(0.1, 100.0 - db.WordPercentages.Count))
						Tags.Add(val);
				}
			}
		}

		TagsDirty = false;
		return Tags.Count;
	}

	public string GetCreated() => GetCreatedObject().ToLocalTime().ToString(DateFormat, CultureInfo.InvariantCulture);

	public DateTime GetCreatedObject() => DateTime.FromBinary(Created);

	public override int GetHashCode() => int.Parse((UUID ??= MakeUUID())[^8..], NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo);

	public DateTime GetLastChangeObject() => DateTime.FromBinary(LastChange);

	public string GetLastChange() => GetLastChangeObject().ToLocalTime().ToString(DateFormat, CultureInfo.InvariantCulture);

	public FlowDocument GetDocument(uint backsteps = 0U) => XamlToFlowDocument(Reconstruct(backsteps));

	public int GetNumRevisions() => Revisions.Count;

	private string GetPlaintext() => XamlToPlaintext(Reconstruct());

	public NoteRevision GetRevision(uint index) => Revisions[Revisions.Count - 1 - (int)index];

	public string GetRevisionTime(uint index) => DateTime.FromBinary(GetRevision(index).Created).ToLocalTime().ToString(DateFormat, CultureInfo.InvariantCulture);

	public void Lock()
	{
		Locked = true;
	}

	public int MatchTags(string text)
	{
		var format = text.Trim();
		if (!TagsDirty && format.Equals(LastQuery))
			return LastMatchCount;

		var matches = Lowercase().Matches(format.ToLowerInvariant());
		int outCount = 0;

		ExtractTags();

		foreach (Match match in matches)
			foreach (Group group in match.Groups.Values)
				if (Tags.Contains(group.Value.ToLowerInvariant()))
					outCount++;

		LastQuery = format;
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
		var latest = Initial ?? string.Empty;
		if (Revisions.Count == 0)
			return latest;

		for (int i = 0; i < Revisions.Count - Math.Min(backsteps, Revisions.Count); i++)
		{
			if (Revisions[i].StartIndex > -1 && Revisions[i].StartIndex < latest.Length)
				latest = latest[..Revisions[i].StartIndex];

			latest += Revisions[i].Substring;
		}

		return latest ?? string.Empty;
	}

	private void ReconstructRevisions(List<long> CreatedTags, List<string> Substrings)
	{
		for (int i = 0; i < Substrings.Count; i++)
		{
			var Created = CreatedTags[i];
			var RString = Substrings[i];
			var StartIndex = -1;
			var Substring = string.Empty;
			var ToCompare = i == 0 ? Initial : Substrings[i - 1];
			for (int j = 0; j < ToCompare?.Length; j++)
			{
				if (j >= RString.Length)
					break;

				if (!RString[j].Equals(ToCompare[j]))
					break;

				StartIndex = j + 1;
				if (StartIndex < RString.Length)
					Substring = RString[StartIndex..];
			}

			Add(new NoteRevision()
			{
				Created = Created,
				StartIndex = StartIndex,
				Substring = Substring,
				Uuid = MakeUUID(UUIDType.Revision)
			});
		}
	}

	public void Serialize(Serializer? serializer)
	{
		if (serializer?.DatabaseFormat < 9)
			TargetPlaintext();

		if (serializer?.DatabaseFormat >= 5)
			serializer?.WriteString(UUID);
		serializer?.WriteLong(Created);
		serializer?.WriteInt32(Index);
		serializer?.WriteString(Initial);
		serializer?.WriteLong(LastChange);

		serializer?.WriteInt32(Revisions.Count);
		for (int i = 0; i < Revisions.Count; i++)
		{
			if (serializer?.DatabaseFormat >= 7)
				serializer?.WriteString(Revisions[i].Uuid);
			serializer?.WriteLong(Revisions[i].Created);
			serializer?.WriteInt32(Revisions[i].StartIndex);
			serializer?.WriteString(Revisions[i].Substring);
		}
	}

	private void TargetPlaintext()
	{
		List<long> CreatedTags = [];
		var ParsedInitial = XamlToPlaintext(Initial ??= string.Empty);
		var RCount = Revisions.Count;
		List<string> ReconstructedSubstrings = [];

		for (int i = RCount - 1; i > -1; i--)
		{
			var oldText = Reconstruct((uint)i);
			CreatedTags.Add(Revisions[i].Created);
			ReconstructedSubstrings.Add(XamlToPlaintext(oldText));
		}

		Revisions.Clear();
		Initial = ParsedInitial;

		ReconstructRevisions(CreatedTags, ReconstructedSubstrings);
	}

	private void TargetXaml()
	{
		List<long> CreatedTags = [];
		var ParsedInitial = PlaintextToXaml(Initial ?? string.Empty);
		var RCount = Revisions.Count;
		List<string> ReconstructedSubstrings = [];

		for (int i = RCount - 1; i > -1; i--)
		{
			var oldText = Reconstruct((uint)i);
			CreatedTags.Add(Revisions[i].Created);
			ReconstructedSubstrings.Add(PlaintextToXaml(oldText));
		}

		Revisions.Clear();
		Initial = ParsedInitial;

		ReconstructRevisions(CreatedTags, ReconstructedSubstrings);
	}

	public override string ToString() => GetPlaintext();

	public string ToXaml() => Reconstruct(0U);

	public void Unlock()
	{
		Locked = false;

		foreach (var query in OpenQueries)
		{
			if (!query.ResultRecord?.Equals(Index) is true)
				continue;

			query.LastChangedLabel.Content = query.ResultDatabase?.GetRecord(Index).GetLastChange();
			query.ResultBlock.IsEnabled = true;
		}

		foreach (var item in OpenTabs)
		{
			if (!item.Tab.Tag.Equals(this))
				continue;

			var grid = (Grid)item.Tab.Content;
			foreach (UIElement child in grid.Children)
			{
				child.SetValue(UIElement.IsEnabledProperty, true);
				if (child is Label label)
					label.Content = "Entry last modified: " + GetLastChange();
			}
		}
	}

	[GeneratedRegex(@"(\p{Ll}+)")]
	private static partial Regex Lowercase();
}
