using SylverInk.Notes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using static SylverInk.Common;

namespace SylverInk;

/// <summary>
/// Interaction logic for Search.xaml
/// </summary>
public partial class Search : Window
{
	private string _query = string.Empty;
	private NoteRecord _recentSelection = new();
	private readonly List<NoteRecord> _results = [];
	private string _width = string.Empty;

	// TODO: Search capability across all open databases, not just the current one.

	public Search()
	{
		InitializeComponent();
		DataContext = Common.Settings;
	}

	private void CloseClick(object sender, RoutedEventArgs e) => Close();

	private void Drag(object sender, MouseButtonEventArgs e) => DragMove();

	private void FinishSearch(object? sender, RunWorkerCompletedEventArgs e)
	{
		Common.Settings.SearchResults.Clear();

		for (int i = 0; i < _results.Count; i++)
			Common.Settings.SearchResults.Add(_results[i]);

		DoQuery.Content = "Query";
		DoQuery.IsEnabled = true;
	}

	private void NoteDelete(object sender, RoutedEventArgs e)
	{
		var item = (MenuItem)sender;
		var menu = (ContextMenu)item.Parent;
		int index;
		if (menu.DataContext.GetType() == typeof(NoteRecord))
		{
			var record = (NoteRecord)menu.DataContext;
			index = record.Index;
		}
		else
			index = _recentSelection.Index;

		Common.Settings.SearchResults.RemoveAt(Common.Settings.SearchResults.ToList().FindIndex(result => result.Index == index));
		CurrentDatabase.DeleteRecord(index);
		Results.Items.Refresh();
	}

	private void NoteOpen(object sender, RoutedEventArgs e)
	{
		var item = (MenuItem)sender;
		var menu = (ContextMenu)item.Parent;
		SearchResult result = menu.DataContext.GetType() == typeof(NoteRecord) ? OpenQuery((NoteRecord)menu.DataContext, false) : OpenQuery(_recentSelection, false);
		result.AddTabToRibbon();
	}

	private void PerformSearch(object? sender, DoWorkEventArgs e)
	{
		CurrentDatabase.UpdateWordPercentages();

		for (int i = 0; i < CurrentDatabase.RecordCount; i++)
		{
			var newRecord = CurrentDatabase.GetRecord(i);
			
			if (newRecord is null)
				continue;

			FlowDocument document = (FlowDocument)XamlReader.Parse(newRecord.ToXaml());
			TextPointer? pointer = document.ContentStart;
			bool textFound = false;
			while (pointer is not null && pointer.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.None)
			{
				while (pointer is not null && pointer.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
					pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);

				if (pointer is null)
					break;

				string recordText = pointer.GetTextInRun(LogicalDirection.Forward);
				if (recordText.Contains(_query, StringComparison.OrdinalIgnoreCase))
				{
					textFound = true;
					break;
				}

				while (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
					pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
			}

			if (!textFound)
				continue;

			newRecord.Preview = _width;
			_results.Add(newRecord);
		}

		_results.Sort(new Comparison<NoteRecord>((r1, r2) => r2.MatchTags(_query).CompareTo(r1.MatchTags(_query))));
	}

	private void QueryClick(object sender, RoutedEventArgs e)
	{
		var button = (Button)sender;
		button.Content = "Querying...";
		button.IsEnabled = false;

		_query = SearchText.Text ?? string.Empty;
		_results.Clear();
		_width = $"{Math.Floor(Width - 115.0)}";

		BackgroundWorker queryTask = new();
		queryTask.DoWork += PerformSearch;
		queryTask.RunWorkerCompleted += FinishSearch;
		queryTask.RunWorkerAsync();
	}

	private void SublistChanged(object sender, RoutedEventArgs e)
	{
		var box = (ListBox)sender;
		_recentSelection = (NoteRecord)box.SelectedItem;
	}

	private void SublistOpen(object sender, RoutedEventArgs e)
	{
		if (Mouse.RightButton == MouseButtonState.Pressed)
			return;

		var box = (ListBox)sender;
		if (box.SelectedItem is null)
			return;

		OpenQuery(_recentSelection);
		box.SelectedItem = null;
	}
}
