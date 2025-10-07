using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static SylverInk.CommonUtils;
using static SylverInk.XAMLUtils.DataUtils;
using static SylverInk.XAMLUtils.SettingsUtils;

namespace SylverInk;

/// <summary>
/// Interaction logic for Settings.xaml
/// </summary>
public partial class Settings : Window
{
	public int ArialIndex { get; set; }
	public List<SolidColorBrush> AvailableBrushes { get; } = [];
	public List<FontFamily> AvailableFonts { get; } = [];
	public string? ColorTag { get; set; }

	public Brush? LastColorSelection { get; set; }

	public Settings()
	{
		InitializeComponent();
		DataContext = CommonUtils.Settings;
	}

	private void CloseClick(object? sender, RoutedEventArgs e) => Close();

	private void ColorPopup(object? sender, RoutedEventArgs e)
	{
		var button = (Button?)sender;
		ColorTag = (string?)button?.Tag;
		ColorSelection.IsOpen = true;
	}

	private void CustomColorFinished(object? sender, EventArgs e)
	{
		if (LastColorSelection is null)
			return;
		ColorChanged(ColorTag, LastColorSelection);
	}

	private void CustomColorOpened(object? sender, EventArgs e)
	{
		Brush? color = ColorTag switch
		{
			"P1F" => CommonUtils.Settings.MenuForeground,
			"P1B" => CommonUtils.Settings.MenuBackground,
			"P2F" => CommonUtils.Settings.ListForeground,
			"P2B" => CommonUtils.Settings.ListBackground,
			"P3F" => CommonUtils.Settings.AccentForeground,
			"P3B" => CommonUtils.Settings.AccentBackground,
			_ => Brushes.Transparent
		};
		CustomColor.Fill = color;
		CustomColorBox.Text = BytesFromBrush(color)[2..8];
	}

	private void Drag(object? sender, MouseButtonEventArgs e) => DragMove();

	private void FontSizeChanged(object? sender, RoutedEventArgs e)
	{
		var button = (Button?)sender;
		CommonUtils.Settings.MainFontSize += button?.Content.Equals("-") is true ? -0.5 : 0.5;
		DeferUpdateRecentNotes();
	}

	private void MenuFontChanged(object? sender, SelectionChangedEventArgs e)
	{
		var item = (ComboBoxItem)MenuFont.SelectedItem;
		CommonUtils.Settings.MainFontFamily = item.FontFamily;
		DeferUpdateRecentNotes();
	}

	private void NewCustomColor(object? sender, TextChangedEventArgs e)
	{
		if (sender is not TextBox box)
			return;

		var text = box.Text.StartsWith('#') ? box.Text[1..] : box.Text;
		var brush = BrushFromBytes(text);

		CustomColor.Fill = brush ?? Brushes.Transparent;
		LastColorSelection = brush;
	}

	private void NTS_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
	{
		foreach (SearchResult note in OpenQueries)
			if (!note.IsActive)
				note.Opacity = 1.0 - (e.NewValue * 0.01);

		e.Handled = true;
	}

	private void ResetClick(object? sender, RoutedEventArgs e)
	{
		CommonUtils.Settings.AccentBackground = Brushes.PaleGoldenrod;
		CommonUtils.Settings.AccentForeground = Brushes.Blue;
		CommonUtils.Settings.ListBackground = Brushes.White;
		CommonUtils.Settings.ListForeground = Brushes.Black;
		MenuFont.SelectedIndex = ArialIndex;
		CommonUtils.Settings.MainFontFamily = ((ComboBoxItem)MenuFont.SelectedItem).FontFamily;
		CommonUtils.Settings.MainFontSize = 11.0;
		CommonUtils.Settings.MenuBackground = Brushes.Beige;
		CommonUtils.Settings.MenuForeground = Brushes.Black;
		CommonUtils.Settings.NoteClickthrough = 0.25;
		CommonUtils.Settings.NoteTransparency = 95.0;
		RecentEntriesSortMode = SortType.ByChange;
		RibbonTabContent = DisplayType.Content;
		CommonUtils.Settings.SearchResultsOnTop = false;
		CommonUtils.Settings.SnapSearchResults = true;

		DeferUpdateRecentNotes();
	}

	private void Settings_Loaded(object? sender, RoutedEventArgs e)
	{
		this.InitColorGrid();
		this.InitBrushes();
		this.InitFonts();

		if (RibbonBox.SelectedItem is null)
			foreach (ComboBoxItem item in RibbonBox.Items)
				if (item.Tag.Equals(RibbonTabContent.ToString()))
					RibbonBox.SelectedItem = item;

		if (SortBox.SelectedItem is null)
			foreach (ComboBoxItem item in SortBox.Items)
				if (item.Tag.Equals(RecentEntriesSortMode.ToString()))
					SortBox.SelectedItem = item;
	}

	private void SortRibbonChanged(object? sender, SelectionChangedEventArgs e)
	{
		var box = (ComboBox?)sender;
		var item = (ComboBoxItem?)box?.SelectedItem;

		EnumConverter cv = new(typeof(SortType));
		var tag = (SortType?)cv.ConvertFromString((string?)item?.Tag ?? "ByChange") ?? SortType.ByChange;

		RecentEntriesSortMode = tag;
		RecentNotesDirty = true;
		DeferUpdateRecentNotes();
	}

	private void StickyRibbonChanged(object? sender, SelectionChangedEventArgs e)
	{
		var box = (ComboBox?)sender;
		var item = (ComboBoxItem?)box?.SelectedItem;

		EnumConverter ev = new(typeof(DisplayType));
		var tag = (DisplayType?)ev.ConvertFromString((string?)item?.Tag ?? "Content");
		RibbonTabContent = tag ?? DisplayType.Content;

		UpdateRibbonTabs();
	}
}
