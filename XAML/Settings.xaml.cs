using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static SylverInk.Common;

namespace SylverInk;

/// <summary>
/// Interaction logic for Settings.xaml
/// </summary>
public partial class Settings : Window
{
	private static int ArialIndex;
	private readonly List<SolidColorBrush> AvailableBrushes = [];
	private readonly List<FontFamily> AvailableFonts = [];
	private string? ColorTag;

	public Brush? LastColorSelection { get; set; }

	public Settings()
	{
		InitializeComponent();
		DataContext = Common.Settings;
	}

	private void CloseClick(object? sender, RoutedEventArgs e) => Close();

	private static void ColorChanged(string? ColorTag, Brush ColorSelection)
	{
		if (ColorTag is null)
			return;

		switch (ColorTag)
		{
			case "P1F":
				Common.Settings.MenuForeground = ColorSelection;
				break;
			case "P1B":
				Common.Settings.MenuBackground = ColorSelection;
				break;
			case "P2F":
				Common.Settings.ListForeground = ColorSelection;
				break;
			case "P2B":
				Common.Settings.ListBackground = ColorSelection;
				break;
			case "P3F":
				Common.Settings.AccentForeground = ColorSelection;
				break;
			case "P3B":
				Common.Settings.AccentBackground = ColorSelection;
				break;
		}
	}

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
			"P1F" => Common.Settings.MenuForeground,
			"P1B" => Common.Settings.MenuBackground,
			"P2F" => Common.Settings.ListForeground,
			"P2B" => Common.Settings.ListBackground,
			"P3F" => Common.Settings.AccentForeground,
			"P3B" => Common.Settings.AccentBackground,
			_ => Brushes.Transparent
		};
		CustomColor.Fill = color;
		CustomColorBox.Text = BytesFromBrush(color, 3);
	}

	private void Drag(object? sender, MouseButtonEventArgs e) => DragMove();

	private void FontSizeChanged(object? sender, RoutedEventArgs e)
	{
		var button = (Button?)sender;
		Common.Settings.MainFontSize += button?.Content.Equals("-") is true ? -0.5 : 0.5;
		DeferUpdateRecentNotes();
	}

	private static uint HSVFromRGB(SolidColorBrush brush)
	{
		const double fInv = 1.0 / 255.0;
		var (r_, g_, b_) = (brush.Color.R * fInv, brush.Color.G * fInv, brush.Color.B * fInv);
		var Cmax = Math.Max(r_, Math.Max(g_, b_));
		var Cmin = Math.Min(r_, Math.Min(g_, b_));
		var delta = Cmax - Cmin;
		var _h = 0.0;
		var _s = Cmax == 0.0 ? 0.0 : (delta / Cmax);
		var _v = Cmax;
		if (delta != 0.0)
		{
			delta = 60.0 / delta;
			if (Cmax == r_)
				_h = delta * (g_ - b_) + 360.0;
			if (Cmax == g_)
				_h = delta * (b_ - r_) + 120.0;
			if (Cmax == b_)
				_h = delta * (r_ - g_) + 240.0;
		}
		var H = (uint)(_h % 360.0 * 0.7083333333);
		var S = (uint)(_s * 255.0);
		var V = (uint)(_v * 255.0);
		return (H << 16) + (S << 8) + V;
	}

	private void InitBrushes()
	{
		var column = 1;
		var row = 0;

		foreach (var property in typeof(Brushes)?.GetProperties() ?? [])
		{
			if (property.GetMethod?.Invoke(null, null) is not SolidColorBrush brush)
				continue;

			if (brush.Color.A < 0xFF)
				continue;

			SolidColorBrush brushCopy = new(brush.Color);
			brushCopy.SetValue(TagProperty, Uppercase().Replace(property.Name, new MatchEvaluator(match => " " + match.Value)).Trim());
			AvailableBrushes.Add(brushCopy);
		}

		AvailableBrushes.Sort(new Comparison<SolidColorBrush>((brush1, brush2) =>
		{
			var HSV1 = HSVFromRGB(brush1);
			var HSV2 = HSVFromRGB(brush2);
			return HSV1.CompareTo(HSV2);
		}));

		for (int i = 0; i < AvailableBrushes.Count; i++)
		{
			var brush = AvailableBrushes[i];

			System.Windows.Shapes.Rectangle colorRect = new()
			{
				Fill = brush,
				Margin = new(-1),
				Stretch = Stretch.UniformToFill,
				ToolTip = brush.GetValue(TagProperty) as string
			};

			Button option = new()
			{
				Content = colorRect,
				Height = 20,
				Margin = new(2.5),
				Width = 20,
			};

			option.Click += (sender, _) =>
			{
				var button = (Button)sender;
				LastColorSelection = ((System.Windows.Shapes.Rectangle)button.Content).Fill;
				ColorChanged(ColorTag, LastColorSelection);
			};

			colorRect.SetValue(ToolTipService.InitialShowDelayProperty, 250);

			ColorGrid.Children.Add(option);

			Grid.SetColumn(option, column);
			Grid.SetRow(option, row);

			column++;
			if (column >= 10)
			{
				column = 0;
				row++;
			}
		}
	}

	private void InitColorGrid()
	{
		for (int i = 0; i < 10; i++)
			ColorGrid.ColumnDefinitions.Add(new() { Width = new(1.0, GridUnitType.Star) });

		for (int i = 0; i < 15; i++)
			ColorGrid.RowDefinitions.Add(new() { Height = new(1.0, GridUnitType.Star) });

		System.Windows.Shapes.Rectangle rainbowRect = new()
		{
			Fill = new LinearGradientBrush([
				new(Colors.Red, 0.0),
				new(Colors.Orange, 0.167),
				new(Colors.Yellow, 0.33),
				new(Colors.Green, 0.5),
				new(Colors.Blue, 0.667),
				new(Colors.Violet, 0.833),
			], new(0, 0), new(1, 1)),
			Margin = new(-1),
			Stretch = Stretch.UniformToFill,
			ToolTip = "Custom color..."
		};

		Button customOption = new()
		{
			Content = rainbowRect,
			Height = 20,
			Margin = new(2.5),
			Width = 20
		};

		customOption.Click += (_, _) =>
		{
			ColorSelection.IsOpen = false;
			CustomColorSelection.IsOpen = true;
		};

		ColorGrid.Children.Add(customOption);
	}

	private void InitFonts()
	{
		foreach (var font in Fonts.SystemFontFamilies)
			AvailableFonts.Add(font);

		AvailableFonts.Sort(new Comparison<FontFamily>((f1, f2) => f1.Source.CompareTo(f2.Source)));

		for (int i = 0; i < AvailableFonts.Count; i++)
		{
			var font = AvailableFonts[i];
			ComboBoxItem item = new()
			{
				Content = font.Source,
				FontFamily = font
			};
			MenuFont.Items.Add(item);

			if (font.Source.Equals(Common.Settings.MainFontFamily?.Source))
				MenuFont.SelectedItem = item;

			if (font.Source.Equals("Arial"))
				ArialIndex = i;
		}

		if (MenuFont.SelectedItem is null)
			MenuFont.SelectedIndex = ArialIndex;
	}

	private void MenuFontChanged(object? sender, SelectionChangedEventArgs e)
	{
		var item = (ComboBoxItem)MenuFont.SelectedItem;
		Common.Settings.MainFontFamily = item.FontFamily;
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
				note.Opacity = 1.0 - (e.NewValue / 100.0);

		e.Handled = true;
	}

	private void ResetClick(object? sender, RoutedEventArgs e)
	{
		Common.Settings.AccentBackground = Brushes.PaleGoldenrod;
		Common.Settings.AccentForeground = Brushes.Blue;
		Common.Settings.ListBackground = Brushes.White;
		Common.Settings.ListForeground = Brushes.Black;
		MenuFont.SelectedIndex = ArialIndex;
		Common.Settings.MainFontFamily = ((ComboBoxItem)MenuFont.SelectedItem).FontFamily;
		Common.Settings.MainFontSize = 11.0;
		Common.Settings.MenuBackground = Brushes.Beige;
		Common.Settings.MenuForeground = Brushes.Black;
		RecentEntriesSortMode = SortType.ByChange;
		RibbonTabContent = DisplayType.Content;
		Common.Settings.SearchResultsOnTop = false;
		Common.Settings.SnapSearchResults = true;

		DeferUpdateRecentNotes();
	}

	private void Settings_Loaded(object? sender, RoutedEventArgs e)
	{
		InitColorGrid();
		InitBrushes();
		InitFonts();

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

	[GeneratedRegex(@"\p{Lu}")]
	private static partial Regex Uppercase();
}
