using SylverInk.Notes;
using System.ComponentModel;
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

	private void AdaptiveChecked(object sender, RoutedEventArgs e)
	{
		AdaptiveCheckBox.IsEnabled = false;
		CloseButton.IsEnabled = false;
		DoImport.Content = "Scanning...";
		LTPanel.IsEnabled = false;
		Common.Settings.ReadyToFinalize = false;

		BackgroundWorker worker = new();
		worker.DoWork += (_, _) => Concurrent(() => ImportUtils.Measure(AdaptiveCheckBox.IsChecked is true));
		worker.RunWorkerCompleted += (_, _) =>
		{
			AdaptiveCheckBox.IsEnabled = true;
			CloseButton.IsEnabled = true;
			DoImport.Content = "Import";
			LTPanel.IsEnabled = AdaptiveCheckBox.IsChecked is false;
		};
		worker.RunWorkerAsync();
	}

	private void CloseClick(object sender, RoutedEventArgs e) => Close();

	private void Drag(object sender, MouseButtonEventArgs e) => DragMove();

	private void Finalize_Click(object sender, RoutedEventArgs e)
	{
		AdaptiveCheckBox.IsEnabled = false;
		CloseButton.IsEnabled = false;
		DoImport.Content = "Importing...";
		LTPanel.IsEnabled = false;

		BackgroundWorker worker = new();
		worker.DoWork += (_, _) => ImportUtils.Import();
		worker.RunWorkerCompleted += (_, _) =>
		{
			AdaptiveCheckBox.IsEnabled = true;
			CloseButton.IsEnabled = true;
			DoImport.Content = "Import";
			Common.Settings.ImportTarget = string.Empty;
			LTPanel.IsEnabled = true;
		};
		worker.RunWorkerAsync();
	}

	private void LineToleranceChanged(object? sender, RoutedEventArgs e)
	{
		Common.Settings.LineTolerance += ((Button?)sender)?.Content.Equals("-") is true ? -1 : 1;
		ImportUtils.Measure(AdaptiveCheckBox.IsChecked is true);
	}

	private void Open_Click(object? sender, RoutedEventArgs e)
	{
		Common.Settings.ImportTarget = DialogFileSelect();
		Common.Settings.ImportData = string.Empty;

		ImportUtils.Refresh(AdaptiveCheckBox.IsChecked is true);
	}

	private void Target_TextChanged(object? sender, RoutedEventArgs e)
	{
		Common.Settings.ReadyToFinalize = !string.IsNullOrWhiteSpace(Common.Settings.ImportTarget);
	}
}
