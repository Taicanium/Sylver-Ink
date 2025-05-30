using SylverInk.Net;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using static SylverInk.Common;

namespace SylverInk.Notes;

public class NoteTab
{
	private readonly Grid ButtonGrid;
	private readonly Button DeleteButton;
	private readonly Grid MainGrid;
	private readonly Button NextButton;
	private readonly RichTextBox NoteBox;
	private readonly NoteRecord Record;
	private readonly Button PreviousButton;
	private readonly Button ReturnButton;
	private readonly Label RevisionLabel;
	private readonly Button SaveButton;
	public TabItem Tab;

	public NoteTab(NoteRecord Record, string? content = null)
	{
		this.Record = Record;
		var ChildPanel = GetChildPanel("DatabasesPanel");

		for (int i = OpenTabs.Count - 1; i > -1; i--)
		{
			var tab = OpenTabs[i];
			if (!tab.Record.Equals(Record))
				continue;

			OpenTabs.RemoveAt(i);
			tab.Deconstruct();
		}

		ButtonGrid = new()
		{
			Margin = new(0, 20, 0, 20),
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		DeleteButton = new()
		{
			Content = "Delete",
			Margin = new(0, 0, 20, 0),
			Tag = ChildPanel.SelectedIndex
		};
		MainGrid = new() { Margin = new(2) };
		NextButton = new()
		{
			Content = "\u2192",
			HorizontalAlignment = HorizontalAlignment.Left,
			Width = 50
		};
		NoteBox = new()
		{
			AcceptsReturn = true,
			Height = double.NaN,
			Margin = new(5),
			Tag = (0U, Record),
			VerticalContentAlignment = VerticalAlignment.Top,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		};
		PreviousButton = new()
		{
			Content = "\u2190",
			HorizontalAlignment = HorizontalAlignment.Right,
			Width = 50
		};
		ReturnButton = new()
		{
			Content = "Close",
			Margin = new(20, 0, 0, 0),
			Tag = ChildPanel.SelectedIndex
		};
		RevisionLabel = new()
		{
			Content = "Entry last modified: " + Record.GetLastChange(),
			FontStyle = FontStyles.Italic,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new(0, 0, 10, 0),
			Tag = Record,
			VerticalAlignment = VerticalAlignment.Bottom
		};
		SaveButton = new() { Content = "Save" };
		Tab = new()
		{
			Content = MainGrid,
			Header = GetRibbonHeader(Record),
			Tag = Record
		};

		NoteBox.Document = XamlToFlowDocument(content ?? string.Empty);
		ChildPanel.SelectedIndex = ChildPanel.Items.Add(Tab);
	}

	public void Construct()
	{
		var ChildPanel = GetChildPanel("DatabasesPanel");

		MainGrid.RowDefinitions.Add(new() { Height = GridLength.Auto, });
		MainGrid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
		MainGrid.RowDefinitions.Add(new() { Height = GridLength.Auto, });

		NextButton.IsEnabled = false;
		PreviousButton.IsEnabled = Record.GetNumRevisions() > 0;
		SaveButton.IsEnabled = false;

		NoteBox.TextChanged += (sender, e) =>
		{
			var senderObject = (RichTextBox)sender;
			var tag = ((uint, NoteRecord))senderObject.Tag;
			SaveButton.IsEnabled = !FlowDocumentToPlaintext(senderObject.Document).Equals(tag.Item2.ToString());
		};

		NextButton.Click += (sender, _) =>
		{
			var tag = ((uint, NoteRecord))NoteBox.Tag;
			string revisionTime = tag.Item2.GetCreated();

			tag.Item1 -= 1U;
			revisionTime = tag.Item1 == 0U ? tag.Item2.GetLastChange() : tag.Item2.GetRevisionTime(tag.Item1);

			NoteBox.Document = tag.Item2.GetDocument(tag.Item1);
			NoteBox.Tag = (tag.Item1, tag.Item2);
			NoteBox.IsReadOnly = tag.Item1 != 0;
			((Button)sender).IsEnabled = tag.Item1 > 0;
			PreviousButton.IsEnabled = tag.Item1 < tag.Item2.GetNumRevisions();
			RevisionLabel.Content = (tag.Item1 == 0U ? "Entry last modified: " : $"Revision {tag.Item2.GetNumRevisions() - tag.Item1} from ") + revisionTime;
			SaveButton.Content = tag.Item1 == 0 ? "Save" : "Restore";
			SaveButton.IsEnabled = !tag.Item2.ToString().Equals(FlowDocumentToPlaintext(NoteBox.Document));
		};

		PreviousButton.Click += (sender, _) =>
		{
			var tag = ((uint, NoteRecord))NoteBox.Tag;
			string revisionTime = tag.Item2.GetLastChange();

			tag.Item1 += 1U;
			revisionTime = tag.Item1 == tag.Item2.GetNumRevisions() ? tag.Item2.GetCreated() : tag.Item2.GetRevisionTime(tag.Item1);

			NoteBox.Tag = (tag.Item1, tag.Item2);
			NoteBox.Document = (FlowDocument)XamlReader.Parse(tag.Item2.Reconstruct(tag.Item1));
			NoteBox.IsReadOnly = tag.Item1 != 0;
			((Button)sender).IsEnabled = tag.Item1 + 1 <= tag.Item2.GetNumRevisions();
			RevisionLabel.Content = (tag.Item1 == tag.Item2.GetNumRevisions() ? "Entry created " : $"Revision {tag.Item2.GetNumRevisions() - tag.Item1} from ") + revisionTime;
			NextButton.IsEnabled = tag.Item1 > 0;
			SaveButton.Content = "Restore";
			SaveButton.IsEnabled = !tag.Item2.ToString().Equals(FlowDocumentToPlaintext(NoteBox.Document));
		};

		ReturnButton.Click += (sender, _) =>
		{
			var tag = ((uint, NoteRecord))NoteBox.Tag;

			if (SaveButton.IsEnabled && SaveButton.Content.Equals("Save"))
			{
				switch (MessageBox.Show("You have unsaved changes. Save before closing this note?", "Sylver Ink: Notification", MessageBoxButton.YesNoCancel, MessageBoxImage.Information))
				{
					case MessageBoxResult.Cancel:
						return;
					case MessageBoxResult.Yes:
						CurrentDatabase.CreateRevision(tag.Item2, FlowDocumentToXaml(NoteBox.Document));
						DeferUpdateRecentNotes();
						break;
				}
			}
			CurrentDatabase.Transmit(Network.MessageType.RecordUnlock, IntToBytes(tag.Item2.Index));
			PreviousOpenNote = Record;
			Deconstruct();
		};

		DeleteButton.Click += (sender, _) =>
		{
			if (MessageBox.Show("Are you sure you want to permanently delete this note?", "Sylver Ink: Notification", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.No)
				return;

			var tag = ((uint, NoteRecord))NoteBox.Tag;
			Concurrent(() => CurrentDatabase.DeleteRecord(tag.Item2));
			Deconstruct();
			DeferUpdateRecentNotes();
		};

		SaveButton.Click += (sender, _) =>
		{
			var tag = ((uint, NoteRecord))NoteBox.Tag;

			CurrentDatabase.CreateRevision(tag.Item2, FlowDocumentToXaml(NoteBox.Document));
			DeferUpdateRecentNotes();

			NoteBox.Tag = (0U, tag.Item2);
			NoteBox.IsEnabled = true;
			PreviousButton.IsEnabled = true;
			NextButton.IsEnabled = false;
			RevisionLabel.Content = "Entry last modified: " + tag.Item2.GetLastChange();
			((Button)sender).IsEnabled = false;
		};

		ButtonGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
		ButtonGrid.RowDefinitions.Add(new() { Height = new(30) });
		ButtonGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
		ButtonGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
		ButtonGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
		ButtonGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });

		ButtonGrid.Children.Add(DeleteButton);
		ButtonGrid.Children.Add(NextButton);
		ButtonGrid.Children.Add(PreviousButton);
		ButtonGrid.Children.Add(ReturnButton);
		ButtonGrid.Children.Add(SaveButton);

		MainGrid.Children.Add(ButtonGrid);
		MainGrid.Children.Add(NoteBox);
		MainGrid.Children.Add(RevisionLabel);

		Grid.SetColumn(SaveButton, 1);
		Grid.SetColumn(NextButton, 2);
		Grid.SetColumn(ReturnButton, 2);

		Grid.SetRow(DeleteButton, 2);
		Grid.SetRow(ReturnButton, 2);
		Grid.SetRow(SaveButton, 2);

		Grid.SetRow(RevisionLabel, 0);
		Grid.SetRow(NoteBox, 1);
		Grid.SetRow(ButtonGrid, 2);

		if (Record.Locked)
		{
			NoteBox.IsEnabled = false;
			RevisionLabel.Content = "Note locked by another user";
		}

		OpenTabs.Add(this);
	}

	public void Deconstruct()
	{
		if (!Record.Locked)
			GetDatabaseFromRecord(Record)?.Unlock(Record.Index, true);

		var ChildPanel = GetChildPanel("DatabasesPanel");

		for (int i = OpenTabs.Count - 1; i > -1; i--)
		{
			var tab = OpenTabs[i];
			if (!tab.Record.Equals(Record))
				continue;

			OpenTabs.RemoveAt(i);
			tab.Deconstruct();
		}

		for (int i = ChildPanel.Items.Count - 1; i > 0; i--)
		{
			var item = (TabItem)ChildPanel.Items[i];

			if (item.Tag is not NoteRecord record)
				continue;

			if (!record.Equals(Record))
				continue;

			if (ChildPanel.SelectedIndex == i)
				ChildPanel.SelectedIndex = Math.Max(0, Math.Min(i - 1, ChildPanel.Items.Count - 1));

			ChildPanel.Items.RemoveAt(i);
		}
	}
}
