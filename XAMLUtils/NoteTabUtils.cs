using SylverInk.Notes;
using SylverInk.XAML;
using System;
using System.Windows.Controls;
using static SylverInk.FileIO.FileUtils;
using static SylverInk.Notes.DatabaseUtils;
using static SylverInk.XAMLUtils.DataUtils;
using static SylverInk.XAMLUtils.TextUtils;

namespace SylverInk.XAMLUtils;

public static class NoteTabUtils
{
	public static void Autosave(this NoteTab tab)
	{
		if (GetDatabaseFromRecord(tab.Record) is not Database db)
			return;

		var lockFile = GetLockFile(db.DBFile);
		Erase(lockFile);
		db.Save(lockFile);
	}

	public static void Construct(this NoteTab tab)
	{
		if (tab.FinishedLoading)
			return;

		tab.NextButton.IsEnabled = false;
		tab.NoteBox.Document = tab.Record.GetDocument();
		tab.NoteBox.IsEnabled = !tab.Record.Locked;
		tab.OriginalBlockCount = tab.NoteBox.Document.Blocks.Count;
		tab.OriginalText = FlowDocumentToXaml(tab.NoteBox.Document);
		tab.PreviousButton.IsEnabled = tab.Record.GetNumRevisions() > 0;
		tab.RevisionLabel.Content = tab.Record.Locked ? "Note locked by another user" : tab.Record.GetNumRevisions() == 0 ? $"Entry created: {tab.Record.GetCreated()}" : $"Entry last modified: {tab.Record.GetLastChange()}";
		tab.SaveButton.IsEnabled = false;

		tab.FinishedLoading = true;
	}

	public static void Deconstruct(this NoteTab tab)
	{
		if (!tab.Record.Locked)
			GetDatabaseFromRecord(tab.Record)?.Unlock(tab.Record.Index, true);

		var ChildPanel = GetChildPanel("DatabasesPanel");

		for (int i = OpenTabs.Count - 1; i > -1; i--)
		{
			var item = OpenTabs[i];

			if (item.Content is not NoteTab otherTab)
				continue;

			if (!otherTab.Record.Equals(tab.Record))
				continue;

			OpenTabs.RemoveAt(i);
			tab.Deconstruct();
		}

		for (int i = ChildPanel.Items.Count - 1; i > 0; i--)
		{
			var item = (TabItem)ChildPanel.Items[i];

			if (item.Content is not NoteTab otherTab)
				continue;

			if (!otherTab.Record.Equals(tab.Record))
				continue;

			if (ChildPanel.SelectedIndex == i)
				ChildPanel.SelectedIndex = Math.Max(0, Math.Min(i - 1, ChildPanel.Items.Count - 1));

			ChildPanel.Items.RemoveAt(i);
		}
	}
}
