using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SylverInk.XAMLUtils;

public static partial class SettingsUtils
{
	public static void ColorChanged(string? ColorTag, Brush ColorSelection)
	{
		if (ColorTag is null)
			return;

		switch (ColorTag)
		{
			case "P1F":
				CommonUtils.Settings.MenuForeground = ColorSelection;
				break;
			case "P1B":
				CommonUtils.Settings.MenuBackground = ColorSelection;
				break;
			case "P2F":
				CommonUtils.Settings.ListForeground = ColorSelection;
				break;
			case "P2B":
				CommonUtils.Settings.ListBackground = ColorSelection;
				break;
			case "P3F":
				CommonUtils.Settings.AccentForeground = ColorSelection;
				break;
			case "P3B":
				CommonUtils.Settings.AccentBackground = ColorSelection;
				break;
		}
	}

	public static uint HSVFromRGB(SolidColorBrush brush)
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

	public static void InitBrushes(this Settings window)
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
			brushCopy.SetValue(FrameworkElement.TagProperty, Uppercase().Replace(property.Name, new MatchEvaluator(match => " " + match.Value)).Trim());
			window.AvailableBrushes.Add(brushCopy);
		}

		window.AvailableBrushes.Sort(new Comparison<SolidColorBrush>((brush1, brush2) =>
		{
			var HSV1 = HSVFromRGB(brush1);
			var HSV2 = HSVFromRGB(brush2);
			return HSV1.CompareTo(HSV2);
		}));

		for (int i = 0; i < window.AvailableBrushes.Count; i++)
		{
			var brush = window.AvailableBrushes[i];

			System.Windows.Shapes.Rectangle colorRect = new()
			{
				Fill = brush,
				Margin = new(-1),
				Stretch = Stretch.UniformToFill,
				ToolTip = brush.GetValue(FrameworkElement.TagProperty) as string
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
				window.LastColorSelection = ((System.Windows.Shapes.Rectangle)button.Content).Fill;
				ColorChanged(window.ColorTag, window.LastColorSelection);
			};

			colorRect.SetValue(ToolTipService.InitialShowDelayProperty, 250);

			window.ColorGrid.Children.Add(option);

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

	public static void InitColorGrid(this Settings window)
	{
		for (int i = 0; i < 10; i++)
			window.ColorGrid.ColumnDefinitions.Add(new() { Width = new(1.0, GridUnitType.Star) });

		for (int i = 0; i < 15; i++)
			window.ColorGrid.RowDefinitions.Add(new() { Height = new(1.0, GridUnitType.Star) });

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
			window.ColorSelection.IsOpen = false;
			window.CustomColorSelection.IsOpen = true;
		};

		window.ColorGrid.Children.Add(customOption);
	}

	public static void InitFonts(this Settings window)
	{
		foreach (var font in Fonts.SystemFontFamilies)
			window.AvailableFonts.Add(font);

		window.AvailableFonts.Sort(new Comparison<FontFamily>((f1, f2) => f1.Source.CompareTo(f2.Source)));

		for (int i = 0; i < window.AvailableFonts.Count; i++)
		{
			var font = window.AvailableFonts[i];
			ComboBoxItem item = new()
			{
				Content = font.Source,
				FontFamily = font
			};
			window.MenuFont.Items.Add(item);

			if (font.Source.Equals(CommonUtils.Settings.MainFontFamily?.Source))
				window.MenuFont.SelectedItem = item;

			if (font.Source.Equals("Arial"))
				window.ArialIndex = i;
		}

		if (window.MenuFont.SelectedItem is null)
			window.MenuFont.SelectedIndex = window.ArialIndex;
	}

	[GeneratedRegex(@"\p{Lu}")]
	private static partial Regex Uppercase();
}
