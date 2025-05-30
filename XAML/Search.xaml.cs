using SylverInk.Notes;
using SylverInk.XAMLUtils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static SylverInk.Common;

namespace SylverInk;

/// <summary>
/// Interaction logic for Search.xaml
/// </summary>
public partial class Search : Window
{
	public readonly Dictionary<NoteRecord, Database> DBMatches = [];
	public string Query = string.Empty;
	public readonly List<NoteRecord> ResultsList = [];

	public Search()
	{
		InitializeComponent();
		DataContext = Common.Settings;
	}

	private void CloseClick(object? sender, RoutedEventArgs e) => Close();

	private void Drag(object? sender, MouseButtonEventArgs e) => DragMove();

	private void OnClose(object? sender, EventArgs e)
	{
		Common.Settings.SearchResults.Clear();
	}

	private async Task PerformSearch()
	{
		DBMatches.Clear();

		foreach (Database db in Databases)
			await this.SearchDatabase(db);

		ResultsList.Sort(new Comparison<NoteRecord>((r1, r2) => r2.MatchTags(Query).CompareTo(r1.MatchTags(Query))));
	}

	private void PostResults()
	{
		Common.Settings.SearchResults.Clear();

		for (int i = 0; i < ResultsList.Count; i++)
			Common.Settings.SearchResults.Add(ResultsList[i]);

		DoQuery.Content = "Query";
		DoQuery.IsEnabled = true;
	}

	private async void QueryClick(object? sender, RoutedEventArgs e)
	{
		if (sender is not Button button)
			return;

		button.Content = "Querying...";
		button.IsEnabled = false;

		Query = SearchText.Text ?? string.Empty;
		ResultsList.Clear();

		await PerformSearch();
		PostResults();
	}

	private void SublistChanged(object? sender, RoutedEventArgs e)
	{
		if (Mouse.RightButton == MouseButtonState.Pressed)
			return;

		if (sender is not ListBoxItem box)
			return;

		if (box.DataContext is not NoteRecord record)
			return;

		OpenQuery(record);
	}
}
