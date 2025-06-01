using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static SylverInk.Notes.DatabaseUtils;
using static SylverInk.XAMLUtils.DataUtils;

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
		DataContext = CommonUtils.Settings;

		CommonUtils.Settings.NumReplacements = string.Empty;
	}

	private void CloseClick(object? sender, RoutedEventArgs e) => Close();

	private void Drag(object? sender, MouseButtonEventArgs e) => DragMove();

	private void FinishReplace()
	{
		CommonUtils.Settings.NumReplacements = $"Replaced {_counts.Item1:N0} occurrences in {_counts.Item2:N0} notes.";
		DeferUpdateRecentNotes();

		DoReplace.Content = "Replace";
		DoReplace.IsEnabled = true;
	}

	private async Task PerformReplace() => await Task.Run(() => _counts = CurrentDatabase.Replace(_oldText, _newText));

	private void ReplaceTextChanged(object? sender, TextChangedEventArgs e) => CommonUtils.Settings.ReadyToReplace = !string.IsNullOrWhiteSpace(OldText.Text);

	private async void Replace_Click(object? sender, RoutedEventArgs e)
	{
		if (sender is not Button button)
			return;

		button.Content = "Replacing...";
		button.IsEnabled = false;

		_counts = (0, 0);
		_newText = NewText.Text;
		_oldText = OldText.Text;

		await PerformReplace();
		FinishReplace();
	}
}
