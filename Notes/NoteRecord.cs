using SylverInk.FileIO;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using static SylverInk.Common;

namespace SylverInk.Notes;

public struct NoteRevision(long created = -1, int startIndex = -1, string? substring = null, string? uuid = null)
{
	public long _created = created;
	public int _startIndex = startIndex;
	public string? _substring = substring;
	public string? _uuid = uuid ?? MakeUUID(UUIDType.Revision);
}

public partial class NoteRecord
{
	private long Created = -1;
	private bool Dirty = true;
	private string? Initial = string.Empty;
	private long LastChange = -1;
	private DateTime LastChangeObject = DateTime.UtcNow;
	private string LastQuery = string.Empty;
	private string PreviewText = string.Empty;
	private int PreviewWidth = 375;
	private readonly List<NoteRevision> Revisions = [];
	private readonly List<string> Tags = [];
	private bool TagsDirty = true;

	public int Index = -1;
	public int LastMatchCount { get; private set; }
	public bool Locked { get; private set; }
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
			var diff = DateTime.UtcNow - dtObject;

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

	public void Add(NoteRevision revision)
	{
		Revisions.Add(revision);
		TagsDirty = true;

		if (revision._created == -1)
			revision._created = DateTime.UtcNow.ToBinary();

		var revisionTime = DateTime.FromBinary(revision._created);

		if (revisionTime.CompareTo(LastChangeObject) > 0)
		{
			LastChange = revision._created;
			LastChangeObject = DateTime.FromBinary(LastChange);
		}

		var reconstructed = Reconstruct();
	}

	public void Delete()
	{
		Index = 0;
		Initial = string.Empty;
		LastChange = DateTime.UtcNow.ToBinary();
		Revisions.Clear();
		TagsDirty = true;
	}

	// In its current state, this function is only well-behaved when also removing all revisions following it.
	public void DeleteRevision(int index)
	{
		if (index >= GetNumRevisions())
			return;

		Revisions.RemoveAt(index);
		LastChange = GetNumRevisions() == 0 ? Created : Revisions[GetNumRevisions() - 1]._created;
		LastChangeObject = DateTime.FromBinary(LastChange);
	}

	public NoteRecord Deserialize(Serializer? serializer)
	{
		if (serializer?.DatabaseFormat >= 5)
			serializer?.ReadString(ref UUID);
		serializer?.ReadLong(ref Created);
		serializer?.ReadInt32(ref Index);
		serializer?.ReadString(ref Initial);
		serializer?.ReadLong(ref LastChange);

		int RevisionsCount = 0;
		serializer?.ReadInt32(ref RevisionsCount);
		for (int i = 0; i < RevisionsCount; i++)
		{
			NoteRevision _revision = new();
			if (serializer?.DatabaseFormat >= 7)
				serializer?.ReadString(ref _revision._uuid);
			serializer?.ReadLong(ref _revision._created);
			serializer?.ReadInt32(ref _revision._startIndex);
			serializer?.ReadString(ref _revision._substring);
			Add(_revision);
		}

		// SIDB v.9 introduced XAML rich text formatting. Its absence in earlier versions must be accounted for.
		if (serializer?.DatabaseFormat < 9)
		{
			List<long> CreatedTags = [];
			var ParsedInitial = XamlWriter.Save(PlaintextToFlowDocument(Initial ?? string.Empty));
			var RCount = Revisions.Count;
			List<string> ReconstructedSubstrings = [];

			for (int i = RCount - 1; i > -1; i--)
			{
				var oldText = Reconstruct((uint)i);
				CreatedTags.Add(Revisions[i]._created);
				ReconstructedSubstrings.Add(XamlWriter.Save(PlaintextToFlowDocument(oldText)));
			}

			Revisions.Clear();
			Initial = ParsedInitial;

			for (int i = 0; i < ReconstructedSubstrings.Count; i++)
			{
				var Created = CreatedTags[i];
				var RString = ReconstructedSubstrings[i];
				var StartIndex = -1;
				var Substring = string.Empty;
				var ToCompare = i == 0 ? ParsedInitial : ReconstructedSubstrings[i - 1];
				for (int j = 0; j < ToCompare.Length; j++)
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
					_created = Created,
					_startIndex = StartIndex,
					_substring = Substring
				});
			}
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

	public string GetCreated() => GetCreatedObject().ToLocalTime().ToString(DateFormat);

	public DateTime GetCreatedObject() => DateTime.FromBinary(Created);

	public override int GetHashCode() => int.Parse((UUID ??= MakeUUID())[^4..], System.Globalization.NumberStyles.HexNumber);

	public DateTime GetLastChangeObject() => DateTime.FromBinary(LastChange);

	public string GetLastChange() => GetLastChangeObject().ToLocalTime().ToString(DateFormat);

	public int GetNumRevisions() => Revisions.Count;

	public NoteRevision GetRevision(uint index) => Revisions[Revisions.Count - 1 - (int)index];

	public string GetRevisionTime(uint index) => DateTime.FromBinary(GetRevision(index)._created).ToLocalTime().ToString(DateFormat);

	public void Lock()
	{
		Locked = true;
	}

	public int MatchTags(string text)
	{
		var format = text.Trim();
		if (!TagsDirty && format.Equals(LastQuery))
			return LastMatchCount;

		var matches = Lowercase().Matches(format.ToLower());
		int outCount = 0;

		ExtractTags();

		foreach (Match match in matches)
			foreach (Group group in match.Groups.Values)
				if (Tags.Contains(group.Value.ToLower()))
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
			if (Revisions[i]._startIndex > -1 && Revisions[i]._startIndex < latest.Length)
				latest = latest[..Revisions[i]._startIndex];

			latest += Revisions[i]._substring;
		}

		return latest ?? string.Empty;
	}

	public void Serialize(Serializer? serializer)
	{
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
				serializer?.WriteString(Revisions[i]._uuid);
			serializer?.WriteLong(Revisions[i]._created);
			serializer?.WriteInt32(Revisions[i]._startIndex);
			serializer?.WriteString(Revisions[i]._substring);
		}
	}

	public override string ToString()
	{
		return FlowDocumentToPlaintext((FlowDocument)XamlReader.Parse(Reconstruct()));
	}

	public string ToXaml()
	{
		return Reconstruct(0U);
	}

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
