using SylverInk.XAMLUtils;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static SylverInk.Common;

namespace SylverInk;

public partial class Import : Window
{
	public Import()
	{
		InitializeComponent();
		DataContext = Common.Settings;

		Common.Settings.ImportTarget = string.Empty;
		Common.Settings.ReadyToFinalize = false;
	}

	private async void AdaptiveChecked(object? sender, RoutedEventArgs e)
	{
		AdaptiveCheckBox.IsEnabled = false;
		CloseButton.IsEnabled = false;
		DoImport.Content = "Scanning...";
		LTPanel.IsEnabled = false;
		Common.Settings.ReadyToFinalize = false;

		await ImportUtils.Measure(AdaptiveCheckBox.IsChecked is true);

		AdaptiveCheckBox.IsEnabled = true;
		CloseButton.IsEnabled = true;
		DoImport.Content = "Import";
		LTPanel.IsEnabled = AdaptiveCheckBox.IsChecked is false;
	}

	private void CloseClick(object? sender, RoutedEventArgs e) => Close();

	private void Drag(object? sender, MouseButtonEventArgs e) => DragMove();

	private async void Finalize_Click(object? sender, RoutedEventArgs e)
	{
		AdaptiveCheckBox.IsEnabled = false;
		CloseButton.IsEnabled = false;
		DoImport.Content = "Importing...";
		LTPanel.IsEnabled = false;

		await ImportUtils.Import();

		AdaptiveCheckBox.IsEnabled = true;
		CloseButton.IsEnabled = true;
		DoImport.Content = "Import";
		Common.Settings.ImportTarget = string.Empty;
		LTPanel.IsEnabled = true;
	}

	private async void LineToleranceChanged(object? sender, RoutedEventArgs e)
	{
		Common.Settings.LineTolerance += ((Button?)sender)?.Content.Equals("-") is true ? -1 : 1;
		await ImportUtils.Measure(AdaptiveCheckBox.IsChecked is true);
	}

	private async void Open_Click(object? sender, RoutedEventArgs e)
	{
		Common.Settings.ImportTarget = DialogFileSelect();
		Common.Settings.ImportData = string.Empty;

		await ImportUtils.Refresh(AdaptiveCheckBox.IsChecked is true);
	}

	private void Target_TextChanged(object? sender, RoutedEventArgs e)
	{
		Common.Settings.ReadyToFinalize = !string.IsNullOrWhiteSpace(Common.Settings.ImportTarget);
	}
}
