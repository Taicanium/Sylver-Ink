using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for Search.xaml
	/// </summary>
	public partial class Search : Window
	{
		private string _query = string.Empty;
		private NoteRecord _recentSelection = new();
		private readonly List<NoteRecord> _results = [];
		private string _width = string.Empty;

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

			var button = (Button)FindName("DoQuery");
			button.Content = "Query";
			button.IsEnabled = true;
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

			Common.Settings.SearchResults.RemoveAt(Common.Settings.SearchResults.ToList().FindIndex((result) => result.Index == index));
			Common.CurrentDatabase.Controller.DeleteRecord(index);
			Results.Items.Refresh();
		}

		private void NoteOpen(object sender, RoutedEventArgs e)
		{
			var item = (MenuItem)sender;
			var menu = (ContextMenu)item.Parent;
			SearchResult result;
			if (menu.DataContext.GetType() == typeof(NoteRecord))
			{
				var record = (NoteRecord)menu.DataContext;
				result = Common.OpenQuery(record, false);
			}
			else
				result = Common.OpenQuery(_recentSelection, false);

			result.AddTabToRibbon();
		}

		private void PerformSearch(object? sender, DoWorkEventArgs e)
		{
			Common.CurrentDatabase.Controller.UpdateWordPercentages();

			for (int i = 0; i < Common.CurrentDatabase.Controller.RecordCount; i++)
			{
				var newRecord = Common.CurrentDatabase.Controller.GetRecord(i);
				if (!newRecord.ToString().Contains(_query, StringComparison.OrdinalIgnoreCase))
					continue;

				newRecord.Preview = _width;
				var matches = newRecord.MatchTags(_query);
				var matched = false;

				for (int j = 0; j < _results.Count; j++)
				{
					if (_results[j].LastMatchCount <= matches)
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
			_results.Clear();
			_width = $"{Math.Floor(Width - 115.0)}";

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

			Common.OpenQuery(_recentSelection);
			box.SelectedItem = null;
		}
	}
}
