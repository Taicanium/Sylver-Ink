﻿using System.Windows;
using System.Windows.Controls;
using static SylverInk.Common;

namespace SylverInk
{
	public class NoteTab
	{
		private readonly Grid ButtonGrid;
		private readonly Button DeleteButton;
		private readonly Grid MainGrid;
		private readonly Button NextButton;
		private readonly TextBox NoteBox;
		private int NoteIndex = default;
		private readonly Button PreviousButton;
		private readonly Button ReturnButton;
		private readonly Label RevisionLabel;
		private readonly Button SaveButton;
		public TabItem Tab;

		public NoteTab(int noteIndex, string? content = null)
		{
			NoteIndex = noteIndex;
			var ChildPanel = GetChildPanel("DatabasesPanel");

			for (int i = OpenTabs.Count - 1; i > -1; i--)
			{
				var tab = OpenTabs[i];
				if (tab.NoteIndex == NoteIndex)
				{
					OpenTabs.RemoveAt(i);
					tab.Deconstruct();
				}
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
				Tag = (0U, NoteIndex),
				Text = content ?? string.Empty,
				TextWrapping = TextWrapping.WrapWithOverflow,
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
				Content = "Entry last modified: " + CurrentDatabase.GetRecord(NoteIndex).GetLastChange(),
				FontStyle = FontStyles.Italic,
				HorizontalAlignment = HorizontalAlignment.Right,
				Margin = new(0, 0, 10, 0),
				Tag = NoteIndex,
				VerticalAlignment = VerticalAlignment.Bottom
			};
			SaveButton = new() { Content = "Save" };
			Tab = new() {
				Content = MainGrid,
				Header = GetRibbonHeader(NoteIndex),
				Tag = NoteIndex
			};

			ChildPanel.SelectedIndex = ChildPanel.Items.Add(Tab);
		}

		public void Construct()
		{
			var ChildPanel = GetChildPanel("DatabasesPanel");

			MainGrid.RowDefinitions.Add(new() { Height = GridLength.Auto, });
			MainGrid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
			MainGrid.RowDefinitions.Add(new() { Height = GridLength.Auto, });

			NextButton.IsEnabled = false;
			PreviousButton.IsEnabled = CurrentDatabase.GetRecord(NoteIndex).GetNumRevisions() > 0;
			SaveButton.IsEnabled = false;

			NoteBox.TextChanged += (sender, e) =>
			{
				var senderObject = (TextBox)sender;
				var tag = ((uint, int))senderObject.Tag;
				var record = CurrentDatabase.GetRecord(tag.Item2);
				SaveButton.IsEnabled = !senderObject.Text.Equals(record.ToString());
			};

			NextButton.Click += (sender, _) =>
			{
				var tag = ((uint, int))NoteBox.Tag;
				var record = CurrentDatabase.GetRecord(tag.Item2);
				string revisionTime = record.GetCreated();

				tag.Item1 -= 1U;
				if (tag.Item1 == 0U)
					revisionTime = record.GetLastChange();
				else
					revisionTime = record.GetRevisionTime(tag.Item1);

				NoteBox.Tag = (tag.Item1, tag.Item2);
				NoteBox.Text = record.Reconstruct(tag.Item1);
				NoteBox.IsReadOnly = tag.Item1 != 0;
				((Button)sender).IsEnabled = tag.Item1 > 0;
				PreviousButton.IsEnabled = tag.Item1 < record.GetNumRevisions();
				RevisionLabel.Content = (tag.Item1 == 0U ? "Entry last modified: " : $"Revision {record.GetNumRevisions() - tag.Item1} from ") + revisionTime;
				SaveButton.Content = tag.Item1 == 0 ? "Save" : "Restore";
				SaveButton.IsEnabled = !record.ToString().Equals(NoteBox.Text);
			};

			PreviousButton.Click += (sender, _) =>
			{
				var tag = ((uint, int))NoteBox.Tag;
				var record = CurrentDatabase.GetRecord(tag.Item2);
				string revisionTime = record.GetLastChange();

				tag.Item1 += 1U;
				if (tag.Item1 == record.GetNumRevisions())
					revisionTime = record.GetCreated();
				else
					revisionTime = record.GetRevisionTime(tag.Item1);

				NoteBox.Tag = (tag.Item1, tag.Item2);
				NoteBox.Text = record.Reconstruct(tag.Item1);
				NoteBox.IsReadOnly = tag.Item1 != 0;
				((Button)sender).IsEnabled = tag.Item1 + 1 <= record.GetNumRevisions();
				RevisionLabel.Content = (tag.Item1 == record.GetNumRevisions() ? "Entry created " : $"Revision {record.GetNumRevisions() - tag.Item1} from ") + revisionTime;
				NextButton.IsEnabled = tag.Item1 > 0;
				SaveButton.Content = "Restore";
				var latestText = record.ToString();
				SaveButton.IsEnabled = !latestText.Equals(NoteBox.Text);
			};

			ReturnButton.Click += (sender, _) =>
			{
				var tag = ((uint, int))NoteBox.Tag;

				if (SaveButton.IsEnabled && SaveButton.Content.Equals("Save"))
				{
					switch (MessageBox.Show("You have unsaved changes. Save before closing this note?", "Sylver Ink: Notification", MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
					{
						case MessageBoxResult.Cancel:
							return;
						case MessageBoxResult.Yes:
							CurrentDatabase.CreateRevision(tag.Item2, NoteBox.Text);
							DeferUpdateRecentNotes();
							break;
					}
				}
				CurrentDatabase.Transmit(Network.MessageType.RecordUnlock, IntToBytes(tag.Item2));
				Deconstruct();
			};

			DeleteButton.Click += (sender, _) =>
			{
				if (MessageBox.Show("Are you sure you want to permanently delete this note?", "Sylver Ink: Notification", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
					return;

				var tag = ((uint, int))NoteBox.Tag;
				CurrentDatabase.DeleteRecord(tag.Item2);
				Deconstruct(true);
				DeferUpdateRecentNotes();
			};

			SaveButton.Click += (sender, _) =>
			{
				var tag = ((uint, int))NoteBox.Tag;

				CurrentDatabase.CreateRevision(tag.Item2, NoteBox.Text);
				DeferUpdateRecentNotes();

				NoteBox.Tag = (0U, tag.Item2);
				NoteBox.IsEnabled = true;
				PreviousButton.IsEnabled = true;
				NextButton.IsEnabled = false;
				RevisionLabel.Content = "Entry last modified: " + CurrentDatabase.GetRecord(tag.Item2).GetLastChange();
				((Button)sender).IsEnabled = false;
			};

			ButtonGrid.RowDefinitions.Add(new() { Height = GridLength.Auto, });
			ButtonGrid.RowDefinitions.Add(new() { Height = new(30), });
			ButtonGrid.RowDefinitions.Add(new() { Height = GridLength.Auto, });
			ButtonGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star), });
			ButtonGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star), });
			ButtonGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star), });

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

			if (CurrentDatabase.GetRecord(NoteIndex).Locked)
			{
				NoteBox.IsEnabled = false;
				RevisionLabel.Content = "Note locked by another user";
			}

			OpenTabs.Add(this);
		}

		public void Deconstruct(bool delete = false)
		{
			var ChildPanel = GetChildPanel("DatabasesPanel");

			// OpenTabs has a count of 0 after removing 2 of 3 open tabs.
			// Possibly related to note indexes.

			for (int i = OpenTabs.Count - 1; i > -1; i--)
			{
				var tab = OpenTabs[i];
				if (tab.NoteIndex == NoteIndex)
				{
					OpenTabs.RemoveAt(i);
					tab.Deconstruct();
				}
				else if (delete && tab.NoteIndex > NoteIndex)
					tab.NoteIndex--;
			}

			for (int i = ChildPanel.Items.Count - 1; i > 0; i--)
			{
				var item = (TabItem)ChildPanel.Items[i];

				if (item.Tag is null)
					continue;

				if ((int)item.Tag == NoteIndex)
				{
					if (ChildPanel.SelectedIndex == i)
						ChildPanel.SelectedIndex = 0;

					ChildPanel.Items.RemoveAt(i);
				}
				else if (delete && (int)item.Tag > NoteIndex)
					item.Tag = (int)item.Tag - 1;
			}
		}
	}
}
