using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static SylverInk.Common;

namespace SylverInk;

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

		Common.Settings.NumReplacements = string.Empty;
	}

	private void CloseClick(object? sender, RoutedEventArgs e) => Close();

	private void Drag(object? sender, MouseButtonEventArgs e) => DragMove();

	private void FinishReplace(object? sender, RunWorkerCompletedEventArgs e)
	{
		Common.Settings.NumReplacements = $"Replaced {_counts.Item1:N0} occurrences in {_counts.Item2:N0} notes.";
		DeferUpdateRecentNotes();

		DoReplace.Content = "Replace";
		DoReplace.IsEnabled = true;
	}

	private void PerformReplace(object? sender, DoWorkEventArgs e) => _counts = CurrentDatabase.Replace(_oldText, _newText);

	private void ReplaceTextChanged(object? sender, TextChangedEventArgs e) => Common.Settings.ReadyToReplace = !string.IsNullOrWhiteSpace(OldText.Text);

	private void Replace_Click(object? sender, RoutedEventArgs e)
	{
		var button = (Button?)sender;
		if (button is null)
			return;

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
