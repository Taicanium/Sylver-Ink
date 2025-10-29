using static SylverInk.Notes.DatabaseUtils;
using static SylverInk.XAMLUtils.DataUtils;

namespace SylverInk.XAMLUtils;

public static class ReplaceUtils
{
	public static void PerformReplace(this Replace window)
	{
		if (CurrentDatabase is null)
			return;

		window.Counts = CurrentDatabase.Replace(window.OldText.Text, window.NewText.Text);

		CommonUtils.Settings.NumReplacements = $"Replaced {window.Counts.Item1:N0} occurrences in {window.Counts.Item2:N0} notes.";
		DeferUpdateRecentNotes();

		window.DoReplace.Content = "Replace";
		window.DoReplace.IsEnabled = true;
	}
}
