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

					Common.Import = new();
					break;
				case "Replace":
					Common.Settings.NumReplacements = string.Empty;

					Common.Replace = new();
					break;
				case "Search":
					Common.Search = new();
					break;
				case "Exit":
					Close();
					break;
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
					var result = MessageBox.Show($"Unable to load database file: {Common.DatabaseFile}.sidb\n\nCreate placeholder data for your new database?", "Sylver Ink: Warning", MessageBoxButton.YesNo);
					NoteController.InitializeRecords(dummyData: result == MessageBoxResult.Yes);
					Serializer.Close();
					return;
				}
				else
				{
					var result = MessageBox.Show($"Unable to load database file: {Common.DatabaseFile}.sidb\n\nLoad your most recent backup?", "Sylver Ink: Warning", MessageBoxButton.YesNo);
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

		private void MainWindow_Closing(object sender, CancelEventArgs e)
		{
			if (Common.CloseOnce)
				return;
			Common.CloseOnce = true;

			if (Common.DatabaseChanged)
			{
				Common.MakeBackups();

				if (!Serializer.OpenWrite($"{Common.DatabaseFile}.sidb"))
				{
					File.Copy($"{Common.DatabaseFile}1.sibk", $"{Common.DatabaseFile}.sidb", true);
					e.Cancel = Common.ForceClose;
					Common.CloseOnce = false;
					return;
				}

				NoteController.SerializeRecords();
				Serializer.Close();
			}

			Application.Current.Shutdown();
		}

		private void MainWindow_Loaded(object sender, RoutedEventArgs e)
		{
			Common.PPD = VisualTreeHelper.GetDpi(RecentNotes).PixelsPerDip;
			Common.Settings.SearchTabHeight = Height - 300.0;
			Common.WindowHeight = RecentNotes.ActualHeight;
			Common.WindowWidth = RecentNotes.ActualWidth;

			FindDatabase();
			Common.UpdateRecentNotes();
		}

		private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			Common.Settings.SearchTabHeight = e.NewSize.Height - 300.0;
			Common.WindowHeight = RecentNotes.ActualHeight;
			Common.WindowWidth = RecentNotes.ActualWidth;
			Common.UpdateRecentNotes();
			RecentNotes.Items.Refresh();
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
	}
}
