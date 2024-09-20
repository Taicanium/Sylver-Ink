using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for Search.xaml
	/// </summary>
	public partial class Search : Window
	{
		private string _query = string.Empty;
		private readonly List<NoteRecord> _results = [];
		private string _width = string.Empty;

		public Search()
		{
			InitializeComponent();
			DataContext = Common.Settings;
		}

		private void CloseClick(object sender, RoutedEventArgs e) => Close();

		private void FinishSearch(object? sender, RunWorkerCompletedEventArgs e)
		{
			for (int i = 0; i < _results.Count; i++)
				Common.Settings.SearchResults.Add(_results[i]);

			var button = (Button)FindName("DoQuery");
			button.Content = "Query";
			button.IsEnabled = true;
		}

		private void PerformSearch(object? sender, DoWorkEventArgs e)
		{
			NoteController.UpdateWordPercentages();

			for (int i = 0; i < NoteController.RecordCount; i++)
			{
				var newRecord = NoteController.GetRecord(i);
				var recordText = newRecord.ToString();
				if (!recordText.Contains(_query, StringComparison.OrdinalIgnoreCase))
					continue;

				newRecord.Preview = _width;
				var matches = newRecord.MatchTags(_query);
				var matched = false;

				for (int j = 0; j < _results.Count; j++)
				{
					var result = _results[j];
					if (result.LastMatchCount <= matches)
					{
						_results.Insert(j, newRecord);
						matched = true;
						break;
					}
				}

				if (!matched)
					_results.Add(newRecord);
			}
		}

		private void QueryClick(object sender, RoutedEventArgs e)
		{
			var button = (Button)sender;
			button.Content = "Querying...";
			button.IsEnabled = false;

			_query = SearchText.Text ?? string.Empty;
			Common.Settings.SearchResults.Clear();

			_width = $"{Math.Floor(Width - 115.0)}";
			List<NoteRecord> _results = [];

			BackgroundWorker queryTask = new();
			queryTask.DoWork += PerformSearch;
			queryTask.RunWorkerCompleted += FinishSearch;
			queryTask.RunWorkerAsync();
		}

		private void Search_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			foreach (NoteRecord record in Common.Settings.SearchResults)
				record.Preview = $"{Math.Floor(Width - 115.0)}";
			Results.Items.Refresh();
		}
	}
}
