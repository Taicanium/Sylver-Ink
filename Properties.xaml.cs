using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static SylverInk.Common;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for Properties.xaml
	/// </summary>
	public partial class Properties : Window
	{
		private bool HourSelected = false;
		private bool MinuteSelected = false;

		public Database? DB { get; set; }

		public Properties()
		{
			InitializeComponent();
			DataContext = Common.Settings;
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

			if (HourSelected && MinuteSelected)
				TimeSelector.IsOpen = false;
		}

		private void CloseClick(object sender, RoutedEventArgs e) => Close();

		private void Drag(object sender, MouseButtonEventArgs e) => DragMove();

		private void Hour_Selected(object sender, RoutedEventArgs e)
		{
			HourSelected = true;
			ApplyTime();
		}

		private void InitializeProperties()
		{
			DBNameLabel.ToolTip = DBNameLabel.Text = DB?.Name;
			DBCreatedLabel.Content = DB?.GetCreated();
			DBFormatLabel.Content = $"SIDB v.{DB?.Controller.Format}";
			DBNotesLabel.Content = $"{DB?.RecordCount:N0} notes";

			double noteAvg = 0.0;
			int noteLongest = 0;
			int noteShortest = int.MaxValue;
			int noteTotal = 0;
			for (int i = 0; i < DB?.RecordCount; i++)
			{
				var length = DB?.GetRecord(i).ToString().Length ?? 0;
				noteAvg += length;
				noteLongest = Math.Max(noteLongest, length);
				noteShortest = Math.Min(noteLongest, length);
				noteTotal += length;
			}
			noteAvg /= DB?.RecordCount ?? 1.0;

			DBAvgLabel.Content = $"{noteAvg:N1} characters";
			DBLongestLabel.Content = $"{noteLongest:N0} characters";
			DBShortestLabel.Content = $"{noteShortest:N0} characters";
			DBTotalLabel.Content = $"{noteTotal:N0} characters";
		}

		private void Minute_Selected(object sender, RoutedEventArgs e)
		{
			MinuteSelected = true;
			ApplyTime();
		}
		private void Properties_Loaded(object sender, RoutedEventArgs e)
		{
			InitializeProperties();

			for (int i = 0; i < 24; i++)
				Hour.Items.Add(new ComboBoxItem() { Content = $"{i:0,0}" });

			for (int i = 0; i < 60; i++)
				Minute.Items.Add(new ComboBoxItem() { Content = $"{i:0,0}" });

			Hour.SelectedIndex = 0;
			Minute.SelectedIndex = 0;
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

			DB?.Revert(reversion);
			InitializeProperties();
		}

		private void SelectedDateChanged(object sender, SelectionChangedEventArgs e) => RestoreButton.IsEnabled = ReversionDate.SelectedDate is not null;

		private void SelectTime(object sender, RoutedEventArgs e)
		{
			TimeSelector.IsOpen = true;
			HourSelected = false;
			MinuteSelected = false;
		}
	}
}