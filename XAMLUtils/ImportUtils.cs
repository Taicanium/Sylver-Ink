using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using static SylverInk.Common;

namespace SylverInk.XAMLUtils;

public static class ImportUtils
{
	private static bool AdaptiveImport = false;
	private static string AdaptivePredicate = string.Empty;
	private static List<string> DataLines { get; } = [];
	private static int Imported;
	private static double RunningAverage;
	private static int RunningCount;

	private static void AppendLine(ref string RecordData, string Line, int LineIndex)
	{
		if (LineIndex > 0)
			RecordData += "\r\n";
		RecordData += Line;
	}

	private static void FinishImport()
	{
		Common.Settings.ImportData = $"Notes imported: {Imported:N0}";
		Common.Settings.ReadyToFinalize = false;

		DataLines.Clear();
		DeferUpdateRecentNotes();
	}

	public static void Import()
	{
		try
		{
			Common.Settings.ImportTarget = string.Empty;
			PerformImport();
			Concurrent(FinishImport);
		}
		catch (Exception ex)
		{
			MessageBox.Show($"Failed to import the selected file: {ex.Message}", "Sylver Ink: Error", MessageBoxButton.OK);
		}
	}

	public static void Measure(bool Adaptive = false)
	{
		if (string.IsNullOrWhiteSpace(Common.Settings.ImportTarget))
			return;

		AdaptiveImport = Adaptive;
		Common.Settings.ReadyToFinalize = false;

		if (!Adaptive || !MeasureNotesAdaptive())
			MeasureNotesManual();

		ReportMeasurement();
	}

	private static bool MeasureNotesAdaptive()
	{
		// Letters, numbers, spaces, and punctuation; respectively.
		string[] classes = [@"\p{L}+", @"\p{Nd}+", @"[\p{Zs}\t]+", @"[\p{P}\p{S}]+"];
		Dictionary<string, double> frequencies = [];
		Dictionary<string, int> tokenCounts = [];

		if (!ReadFromStream(Common.Settings.ImportTarget))
		{
			Common.Settings.ImportTarget = string.Empty;
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

				for (var (c, t) = (0, 0); c < Math.Max(0, Math.Min(key.Length, length) - 1); t++)
				{
					if (t >= classes.Length)
					{
						c++;
						t = 0;
					}

					string type = classes[t];

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

						if (string.IsNullOrWhiteSpace(pBrute.Trim()))
							continue;

						total++;

						if (frequencies.TryAdd(pBrute, 1.0))
						{
							tokenCounts.TryAdd(pBrute, tokenCounts[pattern] + 1);
							continue;
						}

						frequencies[pBrute] += 1.0;
						frequencies.Remove(pattern);
						tokenCounts.Remove(pattern);
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
				var line = DataLines[i].Trim();
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
					AppendLine(ref recordData, line, i);
			}

			RunningAverage /= RunningCount;
			return true;
		}

		MessageBox.Show("Failed to autodetect the note format.", "Sylver Ink: Error", MessageBoxButton.OK);
		AdaptivePredicate = string.Empty;
		return false;
	}

	private static void MeasureNotesManual()
	{
		try
		{
			if (!ReadFromStream(Common.Settings.ImportTarget))
			{
				Common.Settings.ImportTarget = string.Empty;
				return;
			}

			int blankCount = 0;
			string recordData = string.Empty;
			RunningAverage = 0.0;
			RunningCount = 0;
			ReadFromStream(Common.Settings.ImportTarget);

			for (int i = 0; i < DataLines.Count; i++)
			{
				var line = DataLines[i];
				AppendLine(ref recordData, line, i);

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
			MessageBox.Show($"Could not open file: {Common.Settings.ImportTarget}", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	private static void PerformImport()
	{
		int blankCount = 0;
		DelayVisualUpdates = true;
		Imported = 0;
		string recordData = string.Empty;

		for (int i = 0; i < DataLines.Count; i++)
		{
			string line = DataLines[i];

			if (AdaptiveImport)
			{
				if (Regex.IsMatch(line, AdaptivePredicate) && !string.IsNullOrWhiteSpace(recordData.Trim()))
				{
					CurrentDatabase.CreateRecord(recordData);
					Imported++;
					recordData = string.Empty;
				}
				AppendLine(ref recordData, line, i);
				continue;
			}

			AppendLine(ref recordData, line, i);
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

		if (!string.IsNullOrWhiteSpace(recordData))
		{
			CurrentDatabase.CreateRecord(recordData);
			Imported++;
		}

		DelayVisualUpdates = false;
	}

	private static bool ReadFromStream(string filename)
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

	public static void Refresh(bool Adaptive)
	{
		if (Common.Settings.ImportTarget.EndsWith(".sidb") || Common.Settings.ImportTarget.EndsWith(".sibk"))
		{
			var result = MessageBox.Show("You have selected an existing Sylver Ink database. Its contents will be merged with your current database.\n\nDo you want to overwrite your current database instead?", "Sylver Ink: Warning", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

			if (result == MessageBoxResult.Cancel)
				return;

			if (result == MessageBoxResult.Yes)
			{
				CurrentDatabase.MakeBackup(true);
				CurrentDatabase.Erase();
			}

			if (!CurrentDatabase.Open(Common.Settings.ImportTarget))
			{
				MessageBox.Show($"Failed to import the selected file.", "Sylver Ink: Error", MessageBoxButton.OK);
				return;
			}

			CurrentDatabase.Initialize(false);

			Imported = CurrentDatabase.RecordCount;
			FinishImport();

			return;
		}

		Measure(Adaptive);
	}

	public static void ReportMeasurement()
	{
		Common.Settings.ImportData = $"Estimated new notes: {RunningCount:N0}\nAverage length: {RunningAverage:N0} characters per note\n\nRemember to press Import to finalize your changes!";
		Common.Settings.ReadyToFinalize = RunningCount > 0;
	}
}
