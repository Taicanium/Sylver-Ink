using System;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SylverInk.XAMLUtils;

public static partial class SettingsUtils
{
	public static void ColorChanged(string? ColorTag, Brush ColorSelection, RichTextBox? TextTarget = null)
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
			case "PT":
				if (TextTarget is null)
					break;

				TextTarget.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, ColorSelection);
				TextTarget.Foreground = ColorSelection;

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
}
