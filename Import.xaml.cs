using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static SylverInk.Common;

namespace SylverInk
{
	public partial class Import : Window
	{
		private bool Adaptive = false;
		private string AdaptivePredicate = string.Empty;
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

			Common.Settings.ImportTarget = string.Empty;
			Common.Settings.ReadyToFinalize = false;
		}

		private void AdaptiveChecked(object sender, RoutedEventArgs e)
		{
			Adaptive = AdaptiveCheckBox.IsChecked is true;
			if (Adaptive)
			{
				LTPanel.IsEnabled = false;
				DoMeasureTask();
				return;
			}
			LTPanel.IsEnabled = true;
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
					Common.Settings.ImportData = $"Estimated new notes: {RunningCount:N0}\nAverage length: {RunningAverage:N0} characters per note\n\nRemember to press Import to finalize your changes!";
					((Button)FindName("DoImport")).Content = "Import";
					((Button)FindName("LTLess")).IsEnabled = true;
					((Button)FindName("LTMore")).IsEnabled = true;
					Common.Settings.ReadyToFinalize = RunningCount > 0;
				};
			}

			if (MeasureTask.IsBusy)
				return;

			((Button)FindName("DoImport")).Content = "Scanning...";
			((Button)FindName("LTLess")).IsEnabled = false;
			((Button)FindName("LTMore")).IsEnabled = false;
			Common.Settings.ReadyToFinalize = false;

			MeasureTask.RunWorkerAsync();
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
			DeferUpdateRecentNotes(true);
			DataLines.Clear();
		}

		private void LineToleranceChanged(object? sender, RoutedEventArgs e)
		{
			Common.Settings.LineTolerance += ((Button?)sender)?.Content.Equals("-") is true ? -1 : 1;
			DoMeasureTask();
		}

		private void MeasureNotes()
		{
			if (Target.Equals(string.Empty))
				return;

			if (Adaptive)
			{
				using StreamReader? fileStream = new(Target);
				if (fileStream is null || fileStream.EndOfStream)
				{
					Common.Settings.ImportTarget = string.Empty;
					Target = string.Empty;
					return;
				}

				// Letters, numbers, spaces, and punctuation, respectively.
				string[] classes = [@"\p{L}+", @"\p{Nd}+", @"[\p{Zs}\t]+", @"[\p{P}\p{S}]+"];
				Dictionary<string, double> frequencies = [];
				DataLines.Clear();

				while (fileStream?.EndOfStream is false)
				{
					string line = fileStream?.ReadLine() ?? string.Empty;
					DataLines.Add(line);
				}

				for (int length = 3; length <= 30; length++)
				{
					frequencies.Clear();
					double total = 0.0;

					try
					{
						foreach (string key in DataLines)
						{
							total++;

							string pattern = string.Empty;
							for (int c = 0; c < Math.Max(0, Math.Min(key.Length, length)); c++)
								foreach (string type in classes)
									if (!pattern.EndsWith(type) && Regex.IsMatch(key.AsSpan(c, 1), type))
										pattern += type;

							if (!pattern.Trim().Equals(string.Empty) && !frequencies.TryAdd(pattern, 1.0))
								frequencies[pattern] += 1.0;
						}
					}
					catch
					{
						continue;
					}

					foreach (string key in frequencies.Keys)
						frequencies[key] /= total;

					// tl;dr: We search for note boundaries based on certain strings of characters appearing much more frequently than others at the start of lines.
					// Think timestamps, for instance.
					// And to be exact, we're looking for sequences that occur in at least 5% of all lines.
					var ordered = frequencies.OrderByDescending(pair => pair.Value).First();
					if (ordered.Value >= 0.05)
						AdaptivePredicate = "^" + ordered.Key;
				}

				if (!AdaptivePredicate.Trim().Equals(string.Empty))
				{
					string recordData = string.Empty;
					RunningAverage = 0.0;
					RunningCount = 0;

					for (int i = 0; i < DataLines.Count; i++)
					{
						var line = DataLines[i];
						if (Regex.IsMatch(line, AdaptivePredicate))
						{
							if (!recordData.Trim().Equals(string.Empty))
							{
								RunningAverage += recordData.Length;
								RunningCount++;
							}
							recordData = line;
						}
						else
							recordData += "\r\n" + line;
					}

					RunningAverage /= RunningCount;
					return;
				}

				MessageBox.Show("Failed to autodetect the note format.", "Sylver Ink: Error", MessageBoxButton.OK);
				Adaptive = false;
				AdaptiveCheckBox.IsChecked = false;
				AdaptivePredicate = string.Empty;
			}

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
			}
		}

		private void Open_Click(object sender, RoutedEventArgs e)
		{
			Common.Settings.ImportTarget = DialogFileSelect();
			Common.Settings.ImportData = string.Empty;

			if (Target.EndsWith(".sidb") || Target.EndsWith(".sibk"))
			{
				var result = MessageBox.Show("You have selected an existing Sylver Ink database. Its contents will be merged with your current database.\n\nDo you want to overwrite your current database instead?", "Sylver Ink: Warning", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

				if (result == MessageBoxResult.Cancel)
					return;

				if (result == MessageBoxResult.Yes)
				{
					CurrentDatabase.MakeBackup(true);
					CurrentDatabase.Erase();
				}

				if (!CurrentDatabase.Open(Target))
				{
					MessageBox.Show($"Failed to import the selected file.", "Sylver Ink: Error", MessageBoxButton.OK);
					return;
				}

				CurrentDatabase.Initialize(false);

				Imported = CurrentDatabase.RecordCount;
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

				if (Adaptive)
				{
					if (Regex.IsMatch(line, AdaptivePredicate) && !recordData.Trim().Equals(string.Empty))
					{
						CurrentDatabase.CreateRecord(recordData);
						Imported++;
						recordData = string.Empty;
					}
					recordData += line + "\r\n";
					continue;
				}

				recordData += line + "\r\n";
				if (line.Length == 0)
					blankCount++;
				else
					blankCount = 0;

				if (recordData.Length > 0 && (blankCount >= Common.Settings.LineTolerance || i >= DataLines.Count - 1))
				{
					CurrentDatabase.CreateRecord(recordData);
					Imported++;
					recordData = string.Empty;
					blankCount = 0;
				}
			}
		}

		private void Target_TextChanged(object sender, RoutedEventArgs e)
		{
			Target = Common.Settings.ImportTarget;
			Common.Settings.ReadyToFinalize = !Target.Equals(string.Empty);
		}
	}
}
