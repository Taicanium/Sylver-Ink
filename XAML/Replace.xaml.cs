using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static SylverInk.XAMLUtils.ReplaceUtils;

namespace SylverInk;

/// <summary>
/// Interaction logic for Replace.xaml
/// </summary>
public partial class Replace : Window
{
	public (int, int) Counts { get; set; }

	public Replace()
	{
		InitializeComponent();
		DataContext = CommonUtils.Settings;

		CommonUtils.Settings.NumReplacements = string.Empty;
	}

	private void CloseClick(object? sender, RoutedEventArgs e) => Close();

	private void Drag(object? sender, MouseButtonEventArgs e) => DragMove();

	private void ReplaceTextChanged(object? sender, TextChangedEventArgs e) => CommonUtils.Settings.ReadyToReplace = !string.IsNullOrWhiteSpace(OldText.Text);

	private void Replace_Click(object? sender, RoutedEventArgs e)
	{
		if (sender is not Button button)
			return;

		button.Content = "Replacing...";
		button.IsEnabled = false;

		Counts = (0, 0);

		this.PerformReplace();
		this.FinishReplace();
	}
}
