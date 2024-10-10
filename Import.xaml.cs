using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SylverInk
{
	public partial class Import : Window
	{
		private List<string> DataLines { get; } = [];
		private int Imported = 0;
		private BackgroundWorker? MeasureTask;
		private double RunningAverage = 0.0;
		private int RunningCount = 0;
		private string Target = string.Empty;

		public Import()
		{
			InitializeComponent();
			DataContext = Common.Settings;
		}

		private void CloseClick(object sender, RoutedEventArgs e) => Close();

		private void DoMeasureTask()
		{
			if (Target.Equals(string.Empty))
				return;

			if (MeasureTask is null)
			{
				MeasureTask = new();
				MeasureTask.DoWork += (_, _) => MeasureNotes();
				MeasureTask.RunWorkerCompleted += (_, _) =>
				{
					Common.Settings.ImportData = $"Estimated new notes: {RunningCount}\nAverage length: {RunningAverage:N0} characters per note\n\nRemember to press Import to finalize your changes!";
					((Button)FindName("DoImport")).Content = "Import";
					((Button)FindName("LTLess")).IsEnabled = true;
					((Button)FindName("LTMore")).IsEnabled = true;
					Common.Settings.ReadyToFinalize = RunningCount > 0;
				};
			}

			if (!MeasureTask.IsBusy)
			{
				((Button)FindName("DoImport")).Content = "Scanning...";
				((Button)FindName("LTLess")).IsEnabled = false;
				((Button)FindName("LTMore")).IsEnabled = false;
				Common.Settings.ReadyToFinalize = false;

				MeasureTask.RunWorkerAsync();
			}
		}

		private void Drag(object sender, MouseButtonEventArgs e) => DragMove();

		private void Finalize_Click(object sender, RoutedEventArgs e)
		{
			var button = (Button)sender;
			button.Content = "Importing...";
			Common.Settings.ImportTarget = string.Empty;

			try
			{
				BackgroundWorker importTask = new();
				importTask.DoWork += PerformImport;
				importTask.RunWorkerCompleted += FinishImport;
				importTask.RunWorkerAsync();
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to import the selected file: {ex.Message}", "Sylver Ink: Error", MessageBoxButton.OK);
			}
		}

		private void FinishImport(object? sender, RunWorkerCompletedEventArgs? e)
		{
			Common.Settings.ImportData = $"Notes imported: {Imported:N0}";
			var button = (Button)FindName("DoImport");
			button.Content = "Import";

			Common.Settings.ImportTarget = string.Empty;
			Common.Settings.ReadyToFinalize = false;
			Common.DeferUpdateRecentNotes(true);
			DataLines.Clear();
		}

		private void LineToleranceChanged(object? sender, RoutedEventArgs e)
		{
			var button = (Button?)sender;
			switch (button?.Content)
			{
				case "-":
					Common.Settings.LineTolerance -= 1;
					break;
				case "+":
					Common.Settings.LineTolerance += 1;
					break;
			}
			DoMeasureTask();
		}

		private void MeasureNotes()
		{
			if (Target.Equals(string.Empty))
				return;

			try
			{
				using StreamReader? fileStream = new(Target);
				if (fileStream is null || fileStream.EndOfStream)
				{
					Common.Settings.ImportTarget = string.Empty;
					Target = string.Empty;
					return;
				}

				int blankCount = 0;
				string recordData = string.Empty;
				RunningAverage = 0.0;
				RunningCount = 0;
				DataLines.Clear();

				while (fileStream?.EndOfStream is false)
				{
					string line = fileStream?.ReadLine() ?? string.Empty;
					DataLines.Add(line);
					recordData += line + "\r\n";

					if (line.Trim().Length == 0)
						blankCount++;
					else
						blankCount = 0;

					if (recordData.Length > 0 && (blankCount >= Common.Settings.LineTolerance || fileStream?.EndOfStream is true))
					{
						blankCount = 0;
						RunningAverage += recordData.Length;
						recordData = string.Empty;
						RunningCount++;
					}
				}

				RunningAverage /= RunningCount;
			}
			catch
			{
				MessageBox.Show($"Could not open file: {Target}", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}
		}

		private void Open_Click(object sender, RoutedEventArgs e)
		{
			Common.Settings.ImportTarget = Common.DialogFileSelect();
			Common.Settings.ImportData = string.Empty;

			if (Target.EndsWith(".sidb") || Target.EndsWith(".sibk"))
			{
				var result = MessageBox.Show("You have selected an existing Sylver Ink database. Its contents will be merged with your current database.\n\nDo you want to overwrite your current database instead?", "Sylver Ink: Warning", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

				if (result == MessageBoxResult.Cancel)
					return;

				if (result == MessageBoxResult.Yes)
				{
					Common.CurrentDatabase.MakeBackup(true);
					Common.CurrentDatabase.Erase();
				}

				if (!Common.CurrentDatabase.Open(Target))
				{
					MessageBox.Show($"Failed to import the selected file.", "Sylver Ink: Error", MessageBoxButton.OK);
					return;
				}

				Common.CurrentDatabase.Initialize(false);

				Imported = Common.CurrentDatabase.RecordCount;
				FinishImport(sender, null);

				return;
			}

			DoMeasureTask();
		}

		private void PerformImport(object? sender, DoWorkEventArgs e)
		{
			int blankCount = 0;
			Imported = 0;
			string recordData = string.Empty;

			for (int i = 0; i < DataLines.Count; i++)
			{
				string line = DataLines[i];
				recordData += line + "\r\n";

				if (line.Length == 0)
					blankCount++;
				else
					blankCount = 0;

				if (recordData.Length > 0 && (blankCount >= Common.Settings.LineTolerance || i >= DataLines.Count - 1))
				{
					Common.CurrentDatabase.CreateRecord(recordData);
					Imported++;
					recordData = string.Empty;
					blankCount = 0;
				}
			}
		}

		private void Target_TextChanged(object sender, RoutedEventArgs e)
		{
			Target = Common.Settings.ImportTarget;
			Common.Settings.ReadyToFinalize = !Common.Settings.ImportTarget.Equals(string.Empty);
		}
	}
}
