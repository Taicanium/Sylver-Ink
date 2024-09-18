﻿using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for Import.xaml
	/// </summary>
	public partial class Import : Window
	{
		private int _imported = 0;
		private string _target = string.Empty;

		public Import()
		{
			InitializeComponent();
			DataContext = Common.Settings;
		}

		private void CloseClick(object sender, RoutedEventArgs e) => Close();

		private static string DialogFileSelect()
		{
			OpenFileDialog dialog = new()
			{
				CheckFileExists = true,
				Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
				ValidateNames = true,
			};

			return dialog.ShowDialog() is true ? dialog.FileName : string.Empty;
		}

		private void FinishImport(object? sender, RunWorkerCompletedEventArgs e)
		{
			Common.Settings.ImportData = $"Notes imported: {_imported:N0}";
			var button = (Button)FindName("DoImport");
			button.Content = "Import";
			button.IsEnabled = true;

			Common.Settings.ImportTarget = string.Empty;
			Common.Settings.ReadyToFinalize = false;
			Common.UpdateRecentNotes();
		}

		private void Finalize_Click(object sender, RoutedEventArgs e)
		{
			var button = (Button)sender;
			button.Content = "Importing...";
			button.IsEnabled = false;

			_target = Common.Settings.ImportTarget;

			if (_target.EndsWith(".sidb") || _target.EndsWith(".sibk"))
			{
				if (!Serializer.OpenRead(_target))
					return;

				var result = MessageBox.Show("You have selected an existing Sylver Ink database. Its contents will be merged with your current database.\n\nDo you want to overwrite your current database instead?", "Sylver Ink: Warning", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

				if (result == MessageBoxResult.Cancel)
					return;

				if (result == MessageBoxResult.Yes)
				{
					Common.MakeBackups();
					NoteController.EraseDatabase();
				}

				NoteController.InitializeRecords(false, false);
				Serializer.Close();

				_imported = NoteController.RecordCount;
				Common.Settings.ImportData = $"Notes imported: {_imported:N0}";

				Common.UpdateRecentNotes();

				return;
			}

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

		private void Open_Click(object sender, RoutedEventArgs e)
		{
			Common.Settings.ImportTarget = DialogFileSelect();
			Common.Settings.ImportData = "";
		}

		private void PerformImport(object? sender, DoWorkEventArgs e)
		{
			using StreamReader? fileStream = new(_target);
			string recordData = string.Empty;
			int blankCount = 0;

			while (fileStream?.EndOfStream is false)
			{
				string line = fileStream?.ReadLine() ?? string.Empty;

				if (line.Trim().Length == 0)
				{
					recordData += "\r\n";
					blankCount++;
				}
				else
				{
					recordData += line + "\r\n";
					blankCount = 0;
				}

				if (recordData.Trim().Length > 0 && (blankCount >= 2 || fileStream?.EndOfStream is true))
				{
					NoteController.CreateRecord(recordData.Trim());
					_imported++;
					recordData = string.Empty;
					blankCount = 0;
				}
			}
		}

		private void Target_TextChanged(object sender, RoutedEventArgs e)
		{
			Common.Settings.ReadyToFinalize = !Common.Settings.ImportTarget.Equals(string.Empty);
		}
	}
}