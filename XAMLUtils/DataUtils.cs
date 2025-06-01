using SylverInk.Notes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static SylverInk.CommonUtils;
using static SylverInk.Notes.DatabaseUtils;

namespace SylverInk.XAMLUtils;

/// <summary>
/// Static functions aiding in the display of specific data to the main application window.
/// </summary>
public static class DataUtils
{
	public static bool DelayVisualUpdates { get; set; }
	public static DisplayType RibbonTabContent { get; set; } = DisplayType.Change;
	public static List<NoteTab> OpenTabs { get; } = [];
	public static double PPD { get; set; } = 1.0;

	public static async void DeferUpdateRecentNotes()
	{
		if (!CanResize)
			return;

		if (DelayVisualUpdates)
			return;

		DelayVisualUpdates = true;

		var panel = GetChildPanel("DatabasesPanel");

		if (panel.Dispatcher.Invoke(() => panel.FindName("RecentNotes")) is not ListBox RecentBox)
			return;

		try
		{
			await Task.Run(() =>
			{
				do
				{
					WindowHeight = double.IsNaN(RecentBox.ActualHeight) ? Application.Current.MainWindow.ActualHeight : RecentBox.ActualHeight;
					WindowWidth = double.IsNaN(RecentBox.ActualWidth) ? Application.Current.MainWindow.ActualWidth : RecentBox.ActualWidth;
				} while (WindowHeight <= 0);
			});

			await UpdateRecentNotes();

			Concurrent(UpdateDatabaseMenu);
			Concurrent(UpdateRibbonTabs);
		}
		catch
		{
			return;
		}
		finally
		{
			DelayVisualUpdates = false;
		}
	}

	public static TabControl GetChildPanel(string basePanel) => Concurrent(() =>
	{
		var db = (TabControl)Application.Current.MainWindow.FindName(basePanel);
		var dbItem = (TabItem)db.SelectedItem;
		return (TabControl)dbItem.Content;
	});

	public static Label GetRibbonHeader(NoteRecord record)
	{
		var tooltip = GetRibbonTooltip(record);
		var content = tooltip;

		if (content.Contains('\n'))
			content = content[..content.IndexOf('\n')];

		if (content.Length >= 13)
			content = $"{content[..10]}...";

		return new()
		{
			Content = content,
			Margin = new(0),
			ToolTip = tooltip,
		};
	}

	private static string GetRibbonTooltip(NoteRecord record) => RibbonTabContent switch
	{
		DisplayType.Change => $"{record.ShortChange} — {record.Preview}",
		DisplayType.Content => record.Preview,
		DisplayType.Creation => $"{record.GetCreated()} — {record.Preview}",
		DisplayType.Index => $"Note #{record.Index + 1:N0} — {record.Preview}",
		_ => record.Preview
	};

	private static void UpdateDatabaseMenu()
	{
		var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");
		var menu = (Menu)Application.Current.MainWindow.FindName("DatabaseMenu");

		foreach (MenuItem tab in menu.Items)
		{
			foreach (MenuItem mItem in tab.Items)
			{
				var tag = mItem.GetValue(FrameworkElement.TagProperty) ?? string.Empty;
				if (tag.Equals("Always"))
					continue;

				var client = CurrentDatabase.Client.Active;
				var server = CurrentDatabase.Server.Active;

				var enable = tag switch
				{
					"Connected" => client && !server,
					"NotConnected" => !client && !server,
					"NotServing" => !client && !server,
					"Serving" => !client && server,
					_ => control.Items.Count != 1
				};

				mItem.SetValue(UIElement.IsEnabledProperty, enable);
				mItem.SetValue(UIElement.VisibilityProperty, enable ? Visibility.Visible : Visibility.Collapsed);
			}
		}
	}

	private static async Task UpdateRecentNotes()
	{
		if (CommonUtils.Settings.MainTypeFace is null)
			return;

		Application.Current.Resources["MainFontFamily"] = CommonUtils.Settings.MainFontFamily;
		Application.Current.Resources["MainFontSize"] = CommonUtils.Settings.MainFontSize;

		if (RecentNotesDirty)
			Concurrent(CommonUtils.Settings.RecentNotes.Clear);

		await Task.Run(() =>
		{
			var DpiInfo = Concurrent(() => VisualTreeHelper.GetDpi(Application.Current.MainWindow));
			var PixelRatio = CommonUtils.Settings.MainFontSize * DpiInfo.PixelsPerInchY * 0.013888888889;
			var LineHeight = PixelRatio * CommonUtils.Settings.MainTypeFace.FontFamily.LineSpacing;
			var LineRatio = Math.Max(1.0, (WindowHeight / LineHeight) - 0.5);

			CurrentDatabase.Sort(RecentEntriesSortMode);

			Concurrent(() =>
			{
				while (CommonUtils.Settings.RecentNotes.Count < LineRatio && CommonUtils.Settings.RecentNotes.Count < CurrentDatabase.RecordCount)
					CommonUtils.Settings.RecentNotes.Add(CurrentDatabase.GetRecord(CommonUtils.Settings.RecentNotes.Count));

				while (CommonUtils.Settings.RecentNotes.Count > LineRatio)
					CommonUtils.Settings.RecentNotes.RemoveAt(CommonUtils.Settings.RecentNotes.Count - 1);
			});

			CurrentDatabase.Sort();
		});

		RecentNotesDirty = false;
	}

	public static void UpdateRibbonTabs()
	{
		foreach (var item in OpenTabs)
		{
			if (item.Tab.Tag is not NoteRecord tag)
				continue;

			item.Tab.Header = GetRibbonHeader(tag);
		}
	}
}
