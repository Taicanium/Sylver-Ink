using SylverInk.Notes;
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SylverInk;

/// <summary>
/// Interaction logic for Properties.xaml
/// </summary>
public partial class Properties : Window
{
	private bool HourSelected;
	private bool MinuteSelected;

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
		var minute = (ComboBoxItem)Minute.SelectedItem;

		var hourValue = int.Parse((string)hour.Content, NumberFormatInfo.InvariantInfo);
		var hourIndex = Hour.SelectedIndex;

		SelectedTime.Content = $"{(hourValue == 0 ? 12 : hourValue < 13 ? hourValue : hourValue - 12)}:{minute.Content} {(hourIndex < 12 ? "AM" : "PM")}";

		if (HourSelected && MinuteSelected)
			TimeSelector.IsOpen = false;
	}

	private void CloseClick(object? sender, RoutedEventArgs e) => Close();

	private void Drag(object? sender, MouseButtonEventArgs e) => DragMove();

	private void Hour_Selected(object? sender, RoutedEventArgs e)
	{
		HourSelected = true;
		ApplyTime();
	}

	private void InitializeProperties()
	{
		DBCreatedLabel.Content = DB?.GetCreated();
		DBFormatLabel.Content = $"SIDB v.{DB?.Format}";
		DBNameLabel.ToolTip = DBNameLabel.Text = DB?.Name;
		DBNotesLabel.Content = $"{DB?.RecordCount:N0} notes";
		DBPathLabel.ToolTip = DBPathLabel.Text = $"{DB?.DBFile}";

		double noteAvgC = 0.0;
		double noteAvgW = 0.0;
		int noteLongestC = 0;
		int noteLongestW = 0;
		int noteTotalC = 0;
		int noteTotalW = 0;

		for (int i = 0; i < DB?.RecordCount; i++)
		{
			var record = DB?.GetRecord(i).ToString();
			var length = record?.Length ?? 0;
			var wordCount = NotWhitespace().Matches(record ?? string.Empty).Count;

			noteAvgC += length;
			noteAvgW += wordCount;

			// The 'longest' note is qualified strictly by character count.
			if (noteLongestC <= length)
			{
				noteLongestC = length;
				noteLongestW = wordCount;
			}

			noteTotalC += length;
			noteTotalW += wordCount;
		}

		noteAvgC /= DB?.RecordCount ?? 1.0;
		noteAvgW /= DB?.RecordCount ?? 1.0;

		DBAvgLabel.Content = $"{noteAvgW:N1} words ({noteAvgC:N1} chars.)";
		DBLongestLabel.Content = $"{noteLongestW:N1} words ({noteLongestC:N0} chars.)";
		DBTotalLabel.Content = $"{noteTotalW:N1} words ({noteTotalC:N0} chars.)";
	}

	private void Minute_Selected(object? sender, RoutedEventArgs e)
	{
		MinuteSelected = true;
		ApplyTime();
	}

	private void Properties_Loaded(object? sender, RoutedEventArgs e)
	{
		InitializeProperties();

		for (int i = 0; i < 24; i++)
			Hour.Items.Add(new ComboBoxItem() { Content = $"{i:0,0}" });

		for (int i = 0; i < 60; i++)
			Minute.Items.Add(new ComboBoxItem() { Content = $"{i:0,0}" });

		Hour.SelectedIndex = 0;
		Minute.SelectedIndex = 0;
	}

	private void RestoreClick(object? sender, RoutedEventArgs e)
	{
		if (ReversionDate.SelectedDate is null)
			return;

		if (MessageBox.Show("Are you sure you want to revert the database to the selected date and time?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
			return;

		var hour = (ComboBoxItem)Hour.SelectedItem;
		var minute = (ComboBoxItem)Minute.SelectedItem;

		var hourValue = int.Parse((string)hour.Content, NumberFormatInfo.InvariantInfo);
		var minuteValue = int.Parse((string)minute.Content, NumberFormatInfo.InvariantInfo);

		DateTime reversion = ReversionDate?.SelectedDate ?? DateTime.Now;
		reversion = reversion.Date.AddHours(hourValue).AddMinutes(minuteValue);

		DB?.Revert(reversion);
		InitializeProperties();
	}

	private void SelectedDateChanged(object? sender, SelectionChangedEventArgs e) => RestoreButton.IsEnabled = ReversionDate.SelectedDate is not null;

	private void SelectTime(object? sender, RoutedEventArgs e)
	{
		HourSelected = false;
		MinuteSelected = false;
		TimeSelector.IsOpen = true;
	}

	[GeneratedRegex(@"\S+")]
	private static partial Regex NotWhitespace();
}