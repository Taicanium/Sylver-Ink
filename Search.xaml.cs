using System.Windows;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for Search.xaml
	/// </summary>
	public partial class Search : Window
	{
		public Search()
		{
			InitializeComponent();
			DataContext = Common.Settings;
		}

		private void Query_Click(object sender, RoutedEventArgs e)
		{
			string query = SearchText.Text ?? string.Empty;
			Common.Settings.SearchResults.Clear();
			NoteController.UpdateWordPercentages();

			for (int i = 0; i < NoteController.RecordCount; i++)
			{
				var newRecord = NoteController.GetRecord(i);
				var recordText = newRecord.ToString();
				if (!recordText.Contains(query, System.StringComparison.OrdinalIgnoreCase))
					continue;

				newRecord.Preview = $"{Width - 115.0}";
				var matches = newRecord.MatchTags(query);
				var matched = false;

				for (int j = 0; j < Common.Settings.SearchResults.Count; j++)
				{
					var result = Common.Settings.SearchResults[j];
					if (result.LastMatchCount <= matches)
					{
						Common.Settings.SearchResults.Insert(j, newRecord);
						matched = true;
						break;
					}
				}

				if (!matched)
					Common.Settings.SearchResults.Add(newRecord);
			}
		}

		private void Search_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			foreach (NoteRecord record in Common.Settings.SearchResults)
				record.Preview = $"{Width - 115.0}";
			Results.Items.Refresh();
		}
	}
}
