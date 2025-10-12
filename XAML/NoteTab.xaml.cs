using SylverInk.Net;
using SylverInk.Notes;
using SylverInk.XAMLUtils;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using static SylverInk.CommonUtils;
using static SylverInk.Notes.DatabaseUtils;
using static SylverInk.XAMLUtils.DataUtils;
using static SylverInk.XAMLUtils.NoteTabUtils;
using static SylverInk.XAMLUtils.TextUtils;

namespace SylverInk.XAML;

/// <summary>
/// Interaction logic for NoteTab.xaml
/// </summary>
public partial class NoteTab : UserControl
{
	public bool Autosaving { get; set; }
	public bool FinishedLoading { get; set; }
	public required TextPointer InitialPointer { get; set; }
	public int OriginalBlockCount { get; set; }
	public string OriginalText { get; set; } = string.Empty;
	public required NoteRecord Record { get; set; }
	public uint RevisionIndex { get; set; }
	public DateTime TimeSinceAutosave { get; set; } = DateTime.UtcNow;

	public NoteTab()
	{
		InitializeComponent();
		DataContext = CommonUtils.Settings;
	}

	private void ClickDelete(object sender, RoutedEventArgs e)
	{
		if (MessageBox.Show("Are you sure you want to permanently delete this note?", "Sylver Ink: Notification", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.No)
			return;

		Concurrent(() => CurrentDatabase.DeleteRecord(Record));
		this.Deconstruct();
	}

	private void ClickNext(object sender, RoutedEventArgs e)
	{
		if (sender is not Button button)
			return;

		RevisionIndex -= 1U;
		string revisionTime = RevisionIndex == 0U ? Record.GetLastChange() : Record.GetRevisionTime(RevisionIndex);

		NoteBox.Document = Record.GetDocument(RevisionIndex);
		NoteBox.IsReadOnly = RevisionIndex != 0;
		PreviousButton.IsEnabled = RevisionIndex < Record.GetNumRevisions();
		RevisionLabel.Content = (RevisionIndex == 0U ? "Entry last modified: " : $"Revision {Record.GetNumRevisions() - RevisionIndex} from ") + revisionTime;
		SaveButton.Content = RevisionIndex == 0 ? "Save" : "Restore";
		SaveButton.IsEnabled = true;

		button.IsEnabled = RevisionIndex > 0;
	}

	private void ClickPrevious(object sender, RoutedEventArgs e)
	{
		if (sender is not Button button)
			return;

		RevisionIndex += 1U;
		string revisionTime = RevisionIndex == Record.GetNumRevisions() ? Record.GetCreated() : Record.GetRevisionTime(RevisionIndex);

		NextButton.IsEnabled = RevisionIndex > 0;
		NoteBox.Document = Record.GetDocument(RevisionIndex);
		NoteBox.IsReadOnly = RevisionIndex != 0;
		RevisionLabel.Content = (RevisionIndex == Record.GetNumRevisions() ? "Entry created " : $"Revision {Record.GetNumRevisions() - RevisionIndex} from ") + revisionTime;
		SaveButton.Content = "Restore";
		SaveButton.IsEnabled = true;

		button.IsEnabled = RevisionIndex + 1 <= Record.GetNumRevisions();
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
		this.Deconstruct();
	}

	private void ClickSave(object sender, RoutedEventArgs e)
	{
		if (sender is not Button button)
			return;

		Record.CreateRevision(FlowDocumentToXaml(NoteBox.Document));
		DeferUpdateRecentNotes();

		NextButton.IsEnabled = false;
		NoteBox.IsEnabled = true;
		PreviousButton.IsEnabled = true;
		RevisionIndex = 0U;
		RevisionLabel.Content = "Entry last modified: " + Record.GetLastChange();
		button.IsEnabled = false;
	}

	private void CloseISP(object sender, RoutedEventArgs e)
	{
		InternalSearchPopup.IsOpen = false;
	}

	private void FindNext(object sender, RoutedEventArgs e)
	{
		ScrollToText(NoteBox, ISPText.Text);
	}

	private void FindPrevious(object sender, RoutedEventArgs e)
	{
		ScrollToText(NoteBox, ISPText.Text, LogicalDirection.Backward);
	}

	private void NoteBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (!FinishedLoading)
			return;

		if (sender is not RichTextBox)
			return;

		TextColorButton.Background = TextColorPicker.CustomColorPicker.LastColorSelection ?? CommonUtils.Settings.MenuBackground;

		SaveButton.IsEnabled = NoteBox.Document.Blocks.Count != OriginalBlockCount || !FlowDocumentToXaml(NoteBox.Document).Equals(OriginalText);
		if (Autosaving)
			return;

		Autosaving = true;
		Task.Factory.StartNew(() =>
		{
			SpinWait.SpinUntil(() => (DateTime.UtcNow - TimeSinceAutosave).Seconds >= 5);

			Concurrent(() => Record.Autosave(NoteBox.Document));
			Autosaving = false;
			RecentNotesDirty = true;
			TimeSinceAutosave = DateTime.UtcNow;
			return;
		}, TaskCreationOptions.LongRunning);
	}

	private void NoteTab_Loaded(object sender, RoutedEventArgs e)
	{
		this.Construct();
		TextColorButton.Background = CommonUtils.Settings.MenuBackground;
		TextColorPicker.InitBrushes(NoteBox);
	}

	private void SelectColor(object? sender, RoutedEventArgs e)
	{
		TextColorPicker.CustomColorPicker.ColorTag = "PT";
		TextColorPicker.ColorSelection.IsOpen = true;
	}
}
