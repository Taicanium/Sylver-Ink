using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for Import.xaml
	/// </summary>
	public partial class Import : Window
	{
		public Import()
		{
			InitializeComponent();
			DataContext = Common.Settings;
		}

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

		public static void FinalizeImport()
		{
			var target = Common.Settings.ImportTarget;
			var imported = 0;

			if (target.EndsWith(".sidb") || target.EndsWith(".sibk"))
			{
				var result = MessageBox.Show("You have selected an existing Sylver Ink database. Its contents will be merged with your current database.\nDo you want to overwrite your current database instead?", "Sylver Ink: Warning", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

				if (result == MessageBoxResult.Cancel)
					return;

				if (result == MessageBoxResult.Yes)
				{
					Common.MakeBackups();
					File.Copy(target, $"{Common.DatabaseFile}.sidb", true);
					target = $"{Common.DatabaseFile}.sidb";
				}

				if (!Serializer.OpenRead(target))
					return;

				NoteController.InitializeRecords(false, false);
				Serializer.Close();

				imported = NoteController.RecordCount;
				Common.Settings.ImportData = $"Notes imported: {imported:N0}";

				return;
			}

			string[] lines = File.ReadAllLines(target);
			int blankCount = 0;
			string recordData = string.Empty;

			for (int i = 0; i < lines.Length; i++)
			{
				var line = lines[i];
				if (line.Trim().Length == 0)
					blankCount++;
				else
				{
					recordData += line + "\r\n";
					blankCount = 0;
				}

				if (blankCount >= 2 || i == lines.Length - 1)
				{
					NoteController.CreateRecord(recordData);
					imported++;
					recordData = string.Empty;
					blankCount = 0;
				}
			}

			Common.Settings.ImportData = $"Notes imported: {imported:N0}";
		}

		private void Finalize_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				FinalizeImport();
				Common.UpdateRecentNotes();
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to import the selected file: {ex.Message}", "Sylver Ink: Error", MessageBoxButton.OK);
			}

			Common.Settings.ImportTarget = string.Empty;
			Common.Settings.ReadyToFinalize = false;
		}

		private void Open_Click(object sender, RoutedEventArgs e)
		{
			PrepareImport();
		}

		public static void PrepareImport()
		{
			Common.Settings.ImportTarget = DialogFileSelect();
			Common.Settings.ImportData = "";
		}

		private void Target_TextChanged(object sender, RoutedEventArgs e)
		{
			Common.Settings.ReadyToFinalize = !Common.Settings.ImportTarget.Equals(string.Empty);
		}
	}
}
