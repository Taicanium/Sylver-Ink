using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for Replace.xaml
	/// </summary>
	public partial class Replace : Window
	{
		private (int, int) _counts;
		private string _newText = string.Empty;
		private string _oldText = string.Empty;

		public Replace()
		{
			InitializeComponent();
			DataContext = Common.Settings;
		}

		private void CloseClick(object sender, RoutedEventArgs e) => Close();

		private void Drag(object sender, MouseButtonEventArgs e) => DragMove();

		private void FinishReplace(object? sender, RunWorkerCompletedEventArgs e)
		{
			Common.Settings.NumReplacements = $"Replaced {_counts.Item1:N0} occurrences in {_counts.Item2:N0} notes.";
			Common.DeferUpdateRecentNotes();

			var button = (Button)FindName("DoReplace");
			button.Content = "Replace";
			button.IsEnabled = true;
		}

		private void PerformReplace(object? sender, DoWorkEventArgs e)
		{
			_counts = NoteController.Replace(_oldText ?? string.Empty, _newText ?? string.Empty);
		}

		private void ReplaceTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Common.Settings.ReadyToReplace = OldText.Text.Equals(string.Empty) is false;

		private void Replace_Click(object sender, RoutedEventArgs e)
		{
			var button = (Button)sender;
			button.Content = "Replacing...";
			button.IsEnabled = false;

			_counts = (0, 0);
			_newText = NewText.Text;
			_oldText = OldText.Text;

			BackgroundWorker replaceTask = new();
			replaceTask.DoWork += PerformReplace;
			replaceTask.RunWorkerCompleted += FinishReplace;
			replaceTask.RunWorkerAsync();
		}
	}
}
