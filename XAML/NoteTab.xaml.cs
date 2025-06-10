using SylverInk.Net;
using SylverInk.Notes;
using System;
using System.Windows;
using System.Windows.Controls;
using static SylverInk.CommonUtils;
using static SylverInk.Notes.DatabaseUtils;
using static SylverInk.XAMLUtils.DataUtils;
using static SylverInk.XAMLUtils.TextUtils;

namespace SylverInk.XAML
{
	/// <summary>
	/// Interaction logic for NoteTab.xaml
	/// </summary>
	public partial class NoteTab : UserControl
	{
		private bool FinishedLoading { get; set; }
		private string OriginalText { get; set; } = string.Empty;
		public required NoteRecord Record { get; set; }
		private uint RevisionIndex { get; set; }

		public NoteTab()
		{
			InitializeComponent();
		}

		private void ClickDelete(object sender, RoutedEventArgs e)
		{
			if (MessageBox.Show("Are you sure you want to permanently delete this note?", "Sylver Ink: Notification", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.No)
				return;

			Concurrent(() => CurrentDatabase.DeleteRecord(Record));
			Deconstruct();
			DeferUpdateRecentNotes();
		}

		private void ClickNext(object sender, RoutedEventArgs e)
		{
			if (sender is not Button button)
				return;

			RevisionIndex -= 1U;
			string revisionTime = RevisionIndex == 0U ? Record.GetLastChange() : Record.GetRevisionTime(RevisionIndex);

			NoteBox.Document = Record.GetDocument(RevisionIndex);
			NoteBox.IsReadOnly = RevisionIndex != 0;
			button.IsEnabled = RevisionIndex > 0;
			PreviousButton.IsEnabled = RevisionIndex < Record.GetNumRevisions();
			RevisionLabel.Content = (RevisionIndex == 0U ? "Entry last modified: " : $"Revision {Record.GetNumRevisions() - RevisionIndex} from ") + revisionTime;
			SaveButton.Content = RevisionIndex == 0 ? "Save" : "Restore";
			SaveButton.IsEnabled = !OriginalText.Equals(FlowDocumentToXaml(NoteBox.Document));
		}

		private void ClickPrevious(object sender, RoutedEventArgs e)
		{
			if (sender is not Button button)
				return;

			RevisionIndex += 1U;
			string revisionTime = RevisionIndex == Record.GetNumRevisions() ? Record.GetCreated() : Record.GetRevisionTime(RevisionIndex);

			NoteBox.Document = Record.GetDocument(RevisionIndex);
			NoteBox.IsReadOnly = RevisionIndex != 0;
			button.IsEnabled = RevisionIndex + 1 <= Record.GetNumRevisions();
			RevisionLabel.Content = (RevisionIndex == Record.GetNumRevisions() ? "Entry created " : $"Revision {Record.GetNumRevisions() - RevisionIndex} from ") + revisionTime;
			NextButton.IsEnabled = RevisionIndex > 0;
			SaveButton.Content = "Restore";
			SaveButton.IsEnabled = !OriginalText.Equals(FlowDocumentToXaml(NoteBox.Document));
		}

		private void ClickReturn(object sender, RoutedEventArgs e)
		{
			if (SaveButton.IsEnabled && SaveButton.Content.Equals("Save"))
			{
				switch (MessageBox.Show("You have unsaved changes. Save before closing this note?", "Sylver Ink: Notification", MessageBoxButton.YesNoCancel, MessageBoxImage.Information))
				{
					case MessageBoxResult.Cancel:
						return;
					case MessageBoxResult.Yes:
						CurrentDatabase.CreateRevision(Record, FlowDocumentToXaml(NoteBox.Document));
						DeferUpdateRecentNotes();
						break;
				}
			}
			CurrentDatabase.Transmit(NetworkUtils.MessageType.RecordUnlock, IntToBytes(Record.Index));
			PreviousOpenNote = Record;
			Deconstruct();
		}

		private void ClickSave(object sender, RoutedEventArgs e)
		{
			if (sender is not Button button)
				return;

			CurrentDatabase.CreateRevision(Record, FlowDocumentToXaml(NoteBox.Document));
			DeferUpdateRecentNotes();

			RevisionIndex = 0U;
			NoteBox.IsEnabled = true;
			PreviousButton.IsEnabled = true;
			NextButton.IsEnabled = false;
			RevisionLabel.Content = "Entry last modified: " + Record.GetLastChange();
			button.IsEnabled = false;
		}

		private void Construct(object sender, RoutedEventArgs e)
		{
			if (FinishedLoading)
				return;

			NextButton.IsEnabled = false;
			SaveButton.IsEnabled = false;
			PreviousButton.IsEnabled = Record.GetNumRevisions() > 0;

			if (Record.Locked)
			{
				NoteBox.IsEnabled = false;
				RevisionLabel.Content = "Note locked by another user";
			}

			OriginalText = Record.ToXaml();
			NoteBox.Document = Record.GetDocument();

			FinishedLoading = true;
		}

		public void Deconstruct()
		{
			if (!Record.Locked)
				GetDatabaseFromRecord(Record)?.Unlock(Record.Index, true);

			var ChildPanel = GetChildPanel("DatabasesPanel");

			for (int i = OpenTabs.Count - 1; i > -1; i--)
			{
				var item = OpenTabs[i];

				if (item.Content is not NoteTab tab)
					continue;

				if (!tab.Record.Equals(Record))
					continue;

				OpenTabs.RemoveAt(i);
				tab.Deconstruct();
			}

			for (int i = ChildPanel.Items.Count - 1; i > 0; i--)
			{
				var item = (TabItem)ChildPanel.Items[i];

				if (item.Content is not NoteTab tab)
					continue;

				if (!tab.Record.Equals(Record))
					continue;

				if (ChildPanel.SelectedIndex == i)
					ChildPanel.SelectedIndex = Math.Max(0, Math.Min(i - 1, ChildPanel.Items.Count - 1));

				ChildPanel.Items.RemoveAt(i);
			}
		}

		private void NoteBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (sender is not RichTextBox box)
				return;

			SaveButton.IsEnabled = !OriginalText.Equals(FlowDocumentToXaml(box.Document));
		}
	}
}
