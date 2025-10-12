using SylverInk.Notes;
using SylverInk.XAMLUtils;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static SylverInk.CommonUtils;

namespace SylverInk;

/// <summary>
/// Interaction logic for Search.xaml
/// </summary>
public partial class Search : Window
{
	public Dictionary<NoteRecord, Database> DBMatches { get; } = [];
	public string Query { get; private set; } = string.Empty;
	public List<NoteRecord> ResultsList { get; } = [];

	public Search()
	{
		InitializeComponent();
		DataContext = CommonUtils.Settings;
	}

	private void CloseClick(object? sender, RoutedEventArgs e) => Close();

	private void Drag(object? sender, MouseButtonEventArgs e) => DragMove();

	private void OnClose(object? sender, EventArgs e)
	{
		CommonUtils.Settings.SearchResults.Clear();
	}

	private async void QueryClick(object? sender, RoutedEventArgs e)
	{
		if (sender is not Button button)
			return;

		button.Content = "Querying...";
		button.IsEnabled = false;

		Query = SearchText.Text ?? string.Empty;
		ResultsList.Clear();

		await this.PerformSearch();
		this.PostResults();
	}

	private void SublistChanged(object? sender, RoutedEventArgs e)
	{
		if (Mouse.RightButton == MouseButtonState.Pressed)
			return;

		if (sender is not ListBoxItem box)
			return;

		if (box.DataContext is not NoteRecord record)
			return;

		OpenQuery(record)?.ScrollToText(Query);
	}
}
