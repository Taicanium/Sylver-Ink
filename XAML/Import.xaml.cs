using SylverInk.XAMLUtils;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static SylverInk.FileIO.FileUtils;

namespace SylverInk;

public partial class Import : Window
{
	public bool AdaptiveImport { get; set; }
	public string AdaptivePredicate { get; set; } = string.Empty;
	public List<string> DataLines { get; } = [];
	public int Imported { get; set; }
	public double RunningAverage { get; set; }
	public int RunningCount { get; set; }

	public Import()
	{
		InitializeComponent();
		DataContext = CommonUtils.Settings;
	}

	private async void AdaptiveChecked(object? sender, RoutedEventArgs e)
	{
		AdaptiveCheckBox.IsEnabled = false;
		DoImport.Content = "Scanning...";
		DoImport.IsEnabled = false;
		LTPanel.IsEnabled = false;

		await this.Measure(AdaptiveCheckBox.IsChecked is true);

		AdaptiveCheckBox.IsEnabled = true;
		DoImport.Content = "Import";
		LTPanel.IsEnabled = AdaptiveCheckBox.IsChecked is false;
	}

	private void CloseClick(object? sender, RoutedEventArgs e) => Close();

	private void Drag(object? sender, MouseButtonEventArgs e) => DragMove();

	private async void Finalize_Click(object? sender, RoutedEventArgs e)
	{
		AdaptiveCheckBox.IsEnabled = false;
		DoImport.Content = "Importing...";
		DoImport.IsEnabled = false;
		LTPanel.IsEnabled = false;

		await this.Import();

		AdaptiveCheckBox.IsEnabled = true;
		DoImport.Content = "Import";
		CommonUtils.Settings.ImportTarget = string.Empty;
		LTPanel.IsEnabled = true;
	}

	private async void LineToleranceChanged(object? sender, RoutedEventArgs e)
	{
		CommonUtils.Settings.LineTolerance += ((Button?)sender)?.Content.Equals("-") is true ? -1 : 1;
		await this.Measure(AdaptiveCheckBox.IsChecked is true);
	}

	private async void Open_Click(object? sender, RoutedEventArgs e)
	{
		CommonUtils.Settings.ImportTarget = DialogFileSelect();

		await this.Refresh(AdaptiveCheckBox.IsChecked is true);
	}

	private void Target_TextChanged(object? sender, RoutedEventArgs e)
	{
		DoImport.IsEnabled = !string.IsNullOrWhiteSpace(CommonUtils.Settings.ImportTarget);
	}
}
