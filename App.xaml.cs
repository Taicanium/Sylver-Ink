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

			SearchResult resultWindow = new()
			{
				Query = record.ToString(),
				ResultRecord = index
			};
			resultWindow.Show();
			resultWindow.NoteSettings.AccentBackground = Common.Settings.AccentBackground;
			resultWindow.NoteSettings.AccentForeground = Common.Settings.AccentForeground;
			resultWindow.NoteSettings.ListBackground = Common.Settings.ListBackground;
			resultWindow.NoteSettings.ListForeground = Common.Settings.ListForeground;
			resultWindow.NoteSettings.MainFontFamily = Common.Settings.MainFontFamily;
			resultWindow.NoteSettings.MainFontSize = Common.Settings.MainFontSize;
			resultWindow.NoteSettings.MenuBackground = Common.Settings.MenuBackground;
			resultWindow.NoteSettings.MenuForeground = Common.Settings.MenuForeground;

			Common.OpenQueries.Add(resultWindow);
			box.SelectedItem = null;
		}
	}
}
