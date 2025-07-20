using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;
using static SylverInk.CommonUtils;

namespace SylverInk.XAMLUtils;

public static partial class PropertiesUtils
{
	public static void ApplyTime(this Properties window)
	{
		if (window.Hour.SelectedItem is null || window.Minute.SelectedItem is null)
			return;

		var hour = (ComboBoxItem)window.Hour.SelectedItem;
		var minute = (ComboBoxItem)window.Minute.SelectedItem;

		var hourValue = int.Parse((string)hour.Content, NumberFormatInfo.InvariantInfo);
		var hourIndex = window.Hour.SelectedIndex;

		window.SelectedTime.Content = $"{(hourValue == 0 ? 12 : hourValue < 13 ? hourValue : hourValue - 12)}:{minute.Content} {(hourIndex < 12 ? "AM" : "PM")}";

		if (window.HourSelected && window.MinuteSelected)
			window.TimeSelector.IsOpen = false;
	}

	public static async void InitializeProperties(this Properties window)
	{
		window.DBAvgLabel.Content = "...";
		window.DBCreatedLabel.Content = window.DB?.GetCreated();
		window.DBFormatLabel.Content = $"SIDB v.{window.DB?.Format}";
		window.DBLongestLabel.Content = "...";
		window.DBNameLabel.ToolTip = window.DBNameLabel.Text = window.DB?.Name;
		window.DBNotesLabel.Content = $"{window.DB?.RecordCount:N0} notes";
		window.DBPathLabel.ToolTip = window.DBPathLabel.Text = $"{window.DB?.DBFile}";
		window.DBTotalLabel.Content = "...";

		double noteAvgC = 0.0;
		double noteAvgW = 0.0;
		int noteLongestC = 0;
		int noteLongestW = 0;
		int noteTotalC = 0;
		int noteTotalW = 0;

		await Task.Run(() =>
		{
			for (int i = 0; i < window.DB?.RecordCount; i++)
			{
				var record = Concurrent(window.DB.GetRecord(i).ToString);
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

			noteAvgC /= window.DB?.RecordCount ?? 1.0;
			noteAvgW /= window.DB?.RecordCount ?? 1.0;
		});

		window.DBAvgLabel.Content = $"{noteAvgW:N1} words\n({noteAvgC:N1} chars.)";
		window.DBLongestLabel.Content = $"{noteLongestW:N0} words\n({noteLongestC:N0} chars.)";
		window.DBTotalLabel.Content = $"{noteTotalW:N0} words\n({noteTotalC:N0} chars.)";
	}

	public static void Revert(this Properties window)
	{
		var hour = (ComboBoxItem)window.Hour.SelectedItem;
		var minute = (ComboBoxItem)window.Minute.SelectedItem;

		var hourValue = int.Parse((string)hour.Content, NumberFormatInfo.InvariantInfo);
		var minuteValue = int.Parse((string)minute.Content, NumberFormatInfo.InvariantInfo);

		DateTime reversion = window.ReversionDate.SelectedDate ?? DateTime.Now;
		reversion = reversion.Date.AddHours(hourValue).AddMinutes(minuteValue);

		window.CloseButton.IsEnabled = false;
		window.RestoreButton.IsEnabled = false;
		window.ReversionDate.IsEnabled = false;
		window.SelectedTime.IsEnabled = false;

		window.DB?.Revert(reversion);
		window.InitializeProperties();
	}


	[GeneratedRegex(@"\S+")]
	private static partial Regex NotWhitespace();
}
