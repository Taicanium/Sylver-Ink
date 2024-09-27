using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SylverInk
{
	public partial class Import : Window
	{
		private int Imported = 0;
		private string Target = string.Empty;

		public Import()
		{
			InitializeComponent();
			DataContext = Common.Settings;
		}

		private void CloseClick(object sender, RoutedEventArgs e) => Close();

		private void Drag(object sender, MouseButtonEventArgs e) => DragMove();

		private void Finalize_Click(object sender, RoutedEventArgs e)
		{
			var button = (Button)sender;
			button.Content = "Importing...";
			Target = Common.Settings.ImportTarget;
			Common.Settings.ImportTarget = string.Empty;

			if (Target.EndsWith(".sidb") || Target.EndsWith(".sibk"))
			{
				var result = MessageBox.Show("You have selected an existing Sylver Ink database. Its contents will be merged with your current database.\n\nDo you want to overwrite your current database instead?", "Sylver Ink: Warning", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

				if (result == MessageBoxResult.Cancel)
					return;

				if (result == MessageBoxResult.Yes)
				{
					Common.CurrentDatabase.MakeBackup(true);
					Common.CurrentDatabase.Controller.EraseDatabase();
				}

				if (!Common.CurrentDatabase.Controller.Open(Target))
				{
					MessageBox.Show($"Failed to import the selected file.", "Sylver Ink: Error", MessageBoxButton.OK);
					return;
				}

				Common.CurrentDatabase.Controller.InitializeRecords(false, false);

				Imported = Common.CurrentDatabase.Controller.RecordCount;
				FinishImport(sender, null);

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

		private void FinishImport(object? sender, RunWorkerCompletedEventArgs? e)
		{
			Common.Settings.ImportData = $"Notes imported: {Imported:N0}";
			var button = (Button)FindName("DoImport");
			button.Content = "Import";

			Common.Settings.ImportTarget = string.Empty;
			Common.Settings.ReadyToFinalize = false;
			Common.DeferUpdateRecentNotes();
		}

		private void Open_Click(object sender, RoutedEventArgs e)
		{
			Common.Settings.ImportTarget = Common.DialogFileSelect();
			Common.Settings.ImportData = "";
		}

		private void PerformImport(object? sender, DoWorkEventArgs e)
		{
			using StreamReader? fileStream = new(Target);
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
					Common.CurrentDatabase.Controller.CreateRecord(recordData.Trim());
					Imported++;
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
