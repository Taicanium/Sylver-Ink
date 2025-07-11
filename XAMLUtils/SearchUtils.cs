﻿using SylverInk.Notes;
using System;
using System.Threading.Tasks;
using System.Windows.Documents;
using static SylverInk.CommonUtils;

namespace SylverInk.XAMLUtils;

public static class SearchUtils
{
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
