using SylverInk.Notes;
using System;
using System.Threading.Tasks;
using System.Windows.Documents;
using static SylverInk.CommonUtils;
using static SylverInk.Notes.DatabaseUtils;

namespace SylverInk.XAMLUtils;

public static class SearchUtils
{
	public static async Task PerformSearch(this Search window)
	{
		window.DBMatches.Clear();

		foreach (Database db in Databases)
			await window.SearchDatabase(db);

		window.ResultsList.Sort(new Comparison<NoteRecord>((r1, r2) => r2.MatchTags(window.Query).CompareTo(r1.MatchTags(window.Query))));
	}

	public static void PostResults(this Search window)
	{
		CommonUtils.Settings.SearchResults.Clear();

		for (int i = 0; i < window.ResultsList.Count; i++)
			CommonUtils.Settings.SearchResults.Add(window.ResultsList[i]);

		window.DoQuery.Content = "Query";
		window.DoQuery.IsEnabled = true;
	}

	public static async Task SearchDatabase(this Search window, Database db)
	{
		db.UpdateWordPercentages();

		for (int i = 0; i < db.RecordCount; i++)
		{
			if (db.GetRecord(i) is not NoteRecord newRecord)
				continue;

			bool textFound = await SearchRecord(window, newRecord);

			if (!textFound)
				continue;

			window.ResultsList.Add(newRecord);
			window.DBMatches.TryAdd(newRecord, db);
		}
	}

	private static async Task<bool> SearchRecord(this Search window, NoteRecord record) => await Task.Run(() =>
	{
		var document = Concurrent(record.GetDocument);
		TextPointer? pointer = document.ContentStart;
		while (pointer is not null && pointer.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.None)
		{
			while (pointer is not null && pointer.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
				pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);

			if (pointer is null)
				break;

			string recordText = pointer.GetTextInRun(LogicalDirection.Forward);
			if (recordText.Contains(window.Query, StringComparison.OrdinalIgnoreCase))
				return true;

			while (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
				pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
		}

		return false;
	});
}
