using SylverInk.Notes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
	private readonly Dictionary<NoteRecord, Database> _dbMatches = [];
	private string _query = string.Empty;
	private readonly List<NoteRecord> _results = [];
	private string _width = string.Empty;

	public Search()
	{
		InitializeComponent();
		DataContext = Common.Settings;
	}

	private void CloseClick(object? sender, RoutedEventArgs e) => Close();

	private void Drag(object? sender, MouseButtonEventArgs e) => DragMove();

	private void FinishSearch(object? sender, RunWorkerCompletedEventArgs e)
	{
		Common.Settings.SearchResults.Clear();

		for (int i = 0; i < _results.Count; i++)
			Common.Settings.SearchResults.Add(_results[i]);

		DoQuery.Content = "Query";
		DoQuery.IsEnabled = true;
	}

	private void PerformSearch(object? sender, DoWorkEventArgs e)
	{
		_dbMatches.Clear();

		foreach (Database db in Databases)
		{
			db.UpdateWordPercentages();

			for (int i = 0; i < db.RecordCount; i++)
			{
				var newRecord = db.GetRecord(i);

				if (newRecord is null)
					continue;

				var document = (FlowDocument)XamlReader.Parse(newRecord.ToXaml());
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

				_results.Add(newRecord);
				_dbMatches.TryAdd(newRecord, db);
			}
		}

		_results.Sort(new Comparison<NoteRecord>((r1, r2) => r2.MatchTags(_query).CompareTo(r1.MatchTags(_query))));
	}

	private void QueryClick(object? sender, RoutedEventArgs e)
	{
		var button = (Button?)sender;
		if (button is null)
			return;

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

	private void SublistChanged(object? sender, RoutedEventArgs e)
	{
		if (Mouse.RightButton == MouseButtonState.Pressed)
			return;

		var box = (ListBoxItem?)sender;
		if (box is null)
			return;

		if (box.DataContext is null)
			return;

		if (box.DataContext is not NoteRecord)
			return;

		var _record = (NoteRecord)box.DataContext;

		if (_dbMatches.TryGetValue(_record, out var _db))
		{
			OpenQuery(_db, _record);
			return;
		}

		OpenQuery(_record);
	}
}
