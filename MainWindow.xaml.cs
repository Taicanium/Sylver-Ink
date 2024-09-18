using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private bool _firstSize = true;

		public MainWindow()
		{
			InitializeComponent();
			DataContext = Common.Settings;
		}

		private void Button_Click(object sender, RoutedEventArgs e)
		{
			var senderObject = (Button)sender;

			switch (senderObject.Content)
			{
				case "Import":
					Common.Settings.ImportTarget = string.Empty;
					Common.Settings.ReadyToFinalize = false;

					Common.ImportWindow = new();
					break;
				case "Replace":
					Common.Settings.NumReplacements = string.Empty;

					Common.ReplaceWindow = new();
					break;
				case "Search":
					Common.SearchWindow = new();
					break;
				case "Settings":
					Common.SettingsWindow = new();
					break;
				case "Exit":
					Close();
					break;
			}
		}

		private void ExitComplete(object? sender, RunWorkerCompletedEventArgs e)
		{
			Common.CloseOnce = false;
			Common.DatabaseChanged = false;
			ExitButton.Content = "Exit";
			ExitButton.FontWeight = FontWeights.Normal;
			MainGrid.IsEnabled = true;

			if (Common.ForceClose)
			{
				SaveUserSettings();
				Application.Current.Shutdown();
			}
		}

		private static string FindBackup()
		{
			for (int i = 1; i < 4; i++)
			{
				string backup = $"{Common.DatabaseFile}{i}.sibk";
				if (File.Exists(backup))
					return backup;
			}

			return string.Empty;
		}

		private static void FindDatabase()
		{
			if (!Serializer.OpenRead($"{Common.DatabaseFile}.sidb"))
			{
				Serializer.Close();

				string backup;
				if ((backup = FindBackup()).Equals(string.Empty))
				{
					var result = MessageBox.Show($"Unable to load database file: {Common.DatabaseFile}.sidb\n\nCreate placeholder data for your new database?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Question);
					NoteController.InitializeRecords(dummyData: result == MessageBoxResult.Yes);
					Serializer.Close();
					return;
				}
				else
				{
					var result = MessageBox.Show($"Unable to load database file: {Common.DatabaseFile}.sidb\n\nLoad your most recent backup?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Question);
					if (result == MessageBoxResult.Yes)
					{
						NoteController.InitializeRecords(!Serializer.OpenRead(backup));
						Serializer.Close();
						return;
					}
				}
			}

			NoteController.InitializeRecords(false, false);
			Serializer.Close();
		}

		private static void LoadUserSettings()
		{
			if (!File.Exists("settings.sis"))
				return;

			string[] settings = File.ReadAllLines("settings.sis");

			for (int i = 0; i < settings.Length; i++)
			{
				var setting = settings[i].Trim();
				var keyValue = setting.Split(':');
				switch (keyValue[0])
				{
					case "FontFamily":
						Common.Settings.MainFontFamily = new(keyValue[1]);
						break;
				}
			}
		}

		private void MainWindow_Closing(object sender, CancelEventArgs e)
		{
			if (!Common.ForceClose)
			{
				if (Common.CloseOnce)
					return;

				if (Common.DatabaseChanged)
				{
					switch (MessageBox.Show("Do you want to save your work before exiting?", "Sylver Ink: Notification", MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
					{
						case MessageBoxResult.Cancel:
							e.Cancel = true;
							return;
						case MessageBoxResult.No:
							Common.DatabaseChanged = false;
							break;
					}
				}
			}

			Common.CloseOnce = true;

			if (Common.DatabaseChanged)
			{
				e.Cancel = true;

				ExitButton.FontWeight = FontWeights.Bold;
				ExitButton.Content = "Saving...";

				MainGrid.IsEnabled = false;

				BackgroundWorker exitTask = new();
				exitTask.DoWork += SaveDatabase;
				exitTask.RunWorkerCompleted += ExitComplete;
				exitTask.RunWorkerAsync();

				return;
			}

			SaveUserSettings();
			Application.Current.Shutdown();
		}

		private void MainWindow_Loaded(object sender, RoutedEventArgs e)
		{
			LoadUserSettings();

			FindDatabase();

			Common.Settings.MainTypeFace = new(Common.Settings.MainFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
			Common.PPD = VisualTreeHelper.GetDpi(RecentNotes).PixelsPerDip;
			Common.Settings.SearchTabHeight = Height - 300.0;
			UpdateRecentNotes();
		}

		private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			Common.Settings.SearchTabHeight = e.NewSize.Height - 300.0;
			if (!_firstSize)
				UpdateRecentNotes();
		}

		private void NewNote(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
			{
				NoteController.CreateRecord(NewNoteBox.Text);
				Common.UpdateRecentNotes();
				NewNoteBox.Text = string.Empty;
			}
		}

		private void SaveDatabase(object? sender, DoWorkEventArgs e)
		{
			Common.MakeBackups();

			if (Serializer.DatabaseFormat == 2 && !NoteController.TestCanCompress())
				Serializer.DatabaseFormat = 1;

			if (!Serializer.OpenWrite($"{Common.DatabaseFile}.sidb"))
			{
				Common.CloseOnce = false;
				return;
			}

			NoteController.SerializeRecords();
			Serializer.Close();

			Common.ForceClose = true;
		}

		private static void SaveUserSettings()
		{
			string[] settings = [
				$"FontFamily:{Common.Settings.MainFontFamily?.Source}"
			];

			File.WriteAllLines("settings.sis", settings);
		}

		private void UpdateRecentNotes()
		{
			if (!_firstSize)
			{
				Common.WindowHeight = RecentNotes.ActualHeight;
				Common.WindowWidth = RecentNotes.ActualWidth;
			}
			_firstSize = false;
			Common.UpdateRecentNotes();
			RecentNotes.Items.Refresh();
		}
	}
}
