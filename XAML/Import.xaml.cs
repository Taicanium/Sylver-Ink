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

namespace SylverInk;

public partial class Import : Window, IDisposable
{
	private bool Adaptive;
	private string AdaptivePredicate = string.Empty;
	private List<string> DataLines { get; } = [];
	private int Imported;
	private BackgroundWorker? MeasureTask;
	private double RunningAverage;
	private int RunningCount;
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
		DoMeasureTask();
	}

	private void CloseClick(object sender, RoutedEventArgs e) => Close();

	public void Dispose()
	{
		MeasureTask?.Dispose();
		GC.SuppressFinalize(this);
	}

	private void DoMeasureTask()
	{
		if (string.IsNullOrWhiteSpace(Target))
			return;

		if (MeasureTask is null)
		{
			MeasureTask = new();
			MeasureTask.DoWork += (_, _) => MeasureNotes();
			MeasureTask.RunWorkerCompleted += (_, _) =>
			{
				Common.Settings.ImportData = $"Estimated new notes: {RunningCount:N0}\nAverage length: {RunningAverage:N0} characters per note\n\nRemember to press Import to finalize your changes!";
				AdaptiveCheckBox.IsEnabled = true;
				CloseButton.IsEnabled = true;
				DoImport.Content = "Import";
				LTPanel.IsEnabled = !Adaptive;
				Common.Settings.ReadyToFinalize = RunningCount > 0;
			};
		}

		if (MeasureTask.IsBusy)
			return;

		AdaptiveCheckBox.IsEnabled = false;
		CloseButton.IsEnabled = false;
		DoImport.Content = "Scanning...";
		LTPanel.IsEnabled = false;
		Common.Settings.ReadyToFinalize = false;

		MeasureTask.RunWorkerAsync();
	}

	private void Drag(object sender, MouseButtonEventArgs e) => DragMove();

	private void Finalize_Click(object sender, RoutedEventArgs e)
	{
		var button = (Button)sender;
		button.Content = "Importing...";

		AdaptiveCheckBox.IsEnabled = false;
		CloseButton.IsEnabled = false;
		Common.Settings.ImportTarget = string.Empty;
		LTPanel.IsEnabled = false;

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
		AdaptiveCheckBox.IsEnabled = true;
		CloseButton.IsEnabled = true;
		DoImport.Content = "Import";
		Common.Settings.ImportData = $"Notes imported: {Imported:N0}";
		LTPanel.IsEnabled = true;
		Common.Settings.ReadyToFinalize = false;

		DataLines.Clear();
		DeferUpdateRecentNotes(true);
	}

	private void LineToleranceChanged(object? sender, RoutedEventArgs e)
	{
		Common.Settings.LineTolerance += ((Button?)sender)?.Content.Equals("-") is true ? -1 : 1;
		DoMeasureTask();
	}

	private bool MeasureNotesAdaptive()
	{
		if (!Adaptive)
			return false;

		// Letters, numbers, spaces, and punctuation; respectively.
		string[] classes = [@"\p{L}+", @"\p{Nd}+", @"[\p{Zs}\t]+", @"[\p{P}\p{S}]+"];
		Dictionary<string, double> frequencies = [];
		Dictionary<string, int> tokenCounts = [];
		
		if (!ReadFromStream(Target))
		{
			Common.Settings.ImportTarget = string.Empty;
			Target = string.Empty;
			return false;
		}

		int LastPredicateSequence = 0;
		double LastPredicateValue = 0.0;
		double LineTotal = 0.0;

		for (int length = 3; length <= 35; length++)
		{
			double total = 0.0;

			frequencies.Clear();
			frequencies.Add(string.Empty, 0.0);

			tokenCounts.Clear();
			tokenCounts.Add(string.Empty, 0);

			for (int line = 0; line < DataLines.Count; line++, LineTotal++)
			{
				Common.Settings.ImportData = $"Adaptive scanning...";

				var key = DataLines[line];
				if (string.IsNullOrWhiteSpace(key.Trim()))
					continue;

				for (int c = 0; c < Math.Max(0, Math.Min(key.Length, length)); c++)
				{
					foreach (string type in classes)
					{
						if (!Regex.IsMatch(key.AsSpan(c, 1), type))
							continue;

						for (int k = frequencies.Keys.Count - 1; k > -1; k--)
						{
							var pattern = frequencies.Keys.ElementAt(k);
							if (c + 1 < tokenCounts[pattern])
								continue;

							if (pattern.EndsWith(type))
							{
								frequencies[pattern] += 1.0;
								total++;
								continue;
							}

							var pBrute = pattern + type;
							var keySpan = key.AsSpan(0, Math.Min(c + 1, key.Length));

							if (!Regex.IsMatch(keySpan, pBrute))
								continue;

							if (!string.IsNullOrWhiteSpace(pBrute.Trim()))
							{
								total++;

								if (!frequencies.TryAdd(pBrute, 1.0))
								{
									frequencies[pBrute] += 1.0;
									frequencies.Remove(pattern);
									tokenCounts.Remove(pattern);
									continue;
								}

								tokenCounts.TryAdd(pBrute, tokenCounts[pattern] + 1);
							}
						}
					}
				}
			}

			if (frequencies.Count == 0)
				continue;

			foreach (string key in frequencies.Keys)
				frequencies[key] /= total;

			// tl;dr: We search for note boundaries based on certain strings of characters appearing much more frequently than others at the start of lines.
			// Think timestamps, for instance.
			// And to be exact, we're looking for sequences that occur in at least 5% of all lines.
			var ordered = frequencies.OrderByDescending(pair => pair.Value).First();
			if (ordered.Value >= 0.05 && ordered.Value >= LastPredicateValue)
			{
				var NewPredicate = "^" + ordered.Key;
				if (AdaptivePredicate.Equals(NewPredicate))
				{
					LastPredicateSequence++;
					continue;
				}
				AdaptivePredicate = NewPredicate;
				LastPredicateSequence = 0;
				LastPredicateValue = ordered.Value;
			}
			else
				LastPredicateSequence++;

			if (LastPredicateSequence > 5)
				break;
		}

		if (!string.IsNullOrWhiteSpace(AdaptivePredicate.Trim()))
		{
			string recordData = string.Empty;
			RunningAverage = 0.0;
			RunningCount = 0;

			for (int i = 0; i < DataLines.Count; i++)
			{
				var line = DataLines[i];
				if (Regex.IsMatch(line, AdaptivePredicate))
				{
					if (!string.IsNullOrWhiteSpace(recordData.Trim()))
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
			return true;
		}

		MessageBox.Show("Failed to autodetect the note format.", "Sylver Ink: Error", MessageBoxButton.OK);
		Adaptive = false;
		AdaptiveCheckBox.IsChecked = false;
		AdaptivePredicate = string.Empty;
		return false;
	}

	private void MeasureNotesManual()
	{
		try
		{
			if (!ReadFromStream(Target))
			{
				Common.Settings.ImportTarget = string.Empty;
				Target = string.Empty;
				return;
			}

			int blankCount = 0;
			string recordData = string.Empty;
			RunningAverage = 0.0;
			RunningCount = 0;
			ReadFromStream(Target);

			for (int i = 0; i < DataLines.Count; i++)
			{
				var line = DataLines[i];
				recordData += line + "\r\n";

				if (line.Trim().Length == 0)
					blankCount++;
				else
					blankCount = 0;

				Common.Settings.ImportData = $"{i * 100.0 / DataLines.Count:N2}% scanned...";

				if (recordData.Length == 0 || blankCount < Common.Settings.LineTolerance)
					continue;

				blankCount = 0;
				RunningAverage += recordData.Length;
				recordData = string.Empty;
				RunningCount++;
			}

			if (!recordData.Equals(string.Empty))
			{
				RunningAverage += recordData.Length;
				RunningCount++;
			}

			RunningAverage /= RunningCount;
		}
		catch
		{
			MessageBox.Show($"Could not open file: {Target}", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	private void MeasureNotes()
	{
		if (string.IsNullOrWhiteSpace(Target))
			return;

		if (!MeasureNotesAdaptive())
			MeasureNotesManual();
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
				if (Regex.IsMatch(line, AdaptivePredicate) && !string.IsNullOrWhiteSpace(recordData.Trim()))
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

			Common.Settings.ImportData = $"{i * 100.0 / DataLines.Count:N2}% imported...";

			if (blankCount < Common.Settings.LineTolerance && i < DataLines.Count - 1)
				continue;

			CurrentDatabase.CreateRecord(recordData);
			Imported++;
			recordData = string.Empty;
			blankCount = 0;
		}

		if (!recordData.Equals(string.Empty))
		{
			CurrentDatabase.CreateRecord(recordData);
			Imported++;
		}
	}

	private bool ReadFromStream(string filename)
	{
		using StreamReader? fileStream = new(filename);
		if (fileStream is null || fileStream.EndOfStream)
			return false;

		var lines = fileStream?.ReadToEnd().Split('\n');
		if (lines is null)
			return false;

		DataLines.Clear();
		DataLines.AddRange(lines);
		return true;
	}

	private void Target_TextChanged(object sender, RoutedEventArgs e)
	{
		Target = Common.Settings.ImportTarget;
		Common.Settings.ReadyToFinalize = !string.IsNullOrWhiteSpace(Target);
	}
}
