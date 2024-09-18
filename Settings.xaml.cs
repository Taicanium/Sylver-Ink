using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for Settings.xaml
	/// </summary>
	public partial class Settings : Window
	{
		private bool hourSelected = false;
		private bool minuteSelected = false;

		public Settings()
		{
			InitializeComponent();
		}

		private void ApplyTime()
		{
			if (Hour.SelectedItem is null || Minute.SelectedItem is null)
				return;

			var hour = (ComboBoxItem)Hour.SelectedItem;
			var hourValue = int.Parse((string)hour.Content);
			var minute = (ComboBoxItem)Minute.SelectedItem;
			var hourIndex = Hour.SelectedIndex;
			SelectedTime.Content = $"{(hourValue == 0 ? 12 : hourValue < 13 ? hourValue : hourValue - 12)}:{minute.Content} {(hourIndex < 12 ? "AM" : "PM")}";

			if (hourSelected && minuteSelected)
				TimeSelector.IsOpen = false;
		}

		private void CloseClick(object sender, RoutedEventArgs e) => Close();

		private void EraseClick(object sender, RoutedEventArgs e)
		{
			if (MessageBox.Show("Are you sure you want to erase your notes and create a new database?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
				return;

			NoteController.EraseDatabase();
		}

		private void Hour_Selected(object sender, RoutedEventArgs e)
		{
			hourSelected = true;
			ApplyTime();
		}

		private void Minute_Selected(object sender, RoutedEventArgs e)
		{
			minuteSelected = true;
			ApplyTime();
		}

		private void RestoreClick(object sender, RoutedEventArgs e)
		{
			if (ReversionDate.SelectedDate is null)
				return;

			if (MessageBox.Show("Are you sure you want to revert the database to the selected date and time?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
				return;

			var hour = (ComboBoxItem)Hour.SelectedItem;
			var hourValue = int.Parse((string)hour.Content);
			var minute = (ComboBoxItem)Minute.SelectedItem;
			var minuteValue = int.Parse((string)minute.Content);

			DateTime reversion = ReversionDate?.SelectedDate ?? DateTime.Now;
			reversion = reversion.Date.AddHours(hourValue).AddMinutes(minuteValue);

			NoteController.Revert(reversion);
		}

		private void SelectedDateChanged(object sender, SelectionChangedEventArgs e)
		{
			if (ReversionDate.SelectedDate is null)
				RestoreButton.IsEnabled = false;
			else
				RestoreButton.IsEnabled = true;
		}

		private void SelectTime(object sender, RoutedEventArgs e)
		{
			TimeSelector.IsOpen = true;
			hourSelected = false;
			minuteSelected = false;
        }

		private void Settings_Loaded(object sender, RoutedEventArgs e)
		{
			for (int i = 0; i < 24; i++)
			{
				ComboBoxItem item = new()
				{
					Content = $"{i:0,0}"
				};
				Hour.Items.Add(item);
			}

			for (int i = 0; i < 60; i++)
			{
				ComboBoxItem item = new()
				{
					Content = $"{i:0,0}"
				};
				Minute.Items.Add(item);
			}

			Hour.SelectedIndex = 0;
			Minute.SelectedIndex = 0;
		}
	}
}
