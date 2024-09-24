using System.Windows;
using System.Windows.Controls;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		/// <summary>
		/// Spawn a subwindow displaying the selected result from a search query.
		/// </summary>
		/// <param name="sender">The ListBox containing the search result; either the list of results in the Search window, or the Recent Notes box in the main window.</param>
		private void SearchResultFocus(object sender, RoutedEventArgs e)
		{
			var box = (ListBox)sender;
			if (box.SelectedItem is null)
				return;

			var record = (NoteRecord)box.SelectedItem;
			var index = record.GetIndex();
			foreach (SearchResult result in Common.OpenQueries)
				if (result.ResultRecord == index)
					return;

			var control = (TabControl)Current.MainWindow.FindName("DatabasesPanel");

			SearchResult resultWindow = new()
			{
				Query = record.ToString(),
				ResultDatabase = control.SelectedIndex,
				ResultRecord = index
			};
			resultWindow.Show();

			Common.OpenQueries.Add(resultWindow);
			box.SelectedItem = null;
		}
	}
}
