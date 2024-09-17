using System.Windows;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for Replace.xaml
	/// </summary>
	public partial class Replace : Window
	{
		public Replace()
		{
			InitializeComponent();
			DataContext = Common.Settings;
		}

		private void CloseClick(object sender, RoutedEventArgs e) => Close();

		private void ReplaceTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Common.Settings.ReadyToReplace = OldText.Text.Equals(string.Empty) is false;

		private void Replace_Click(object sender, RoutedEventArgs e)
		{
			var counts = NoteController.Replace(OldText.Text ?? string.Empty, NewText.Text ?? string.Empty);
			Common.Settings.NumReplacements = $"Replaced {counts.Item1:N0} occurrences in {counts.Item2:N0} notes.";
			Common.UpdateRecentNotes();
		}
    }
}
