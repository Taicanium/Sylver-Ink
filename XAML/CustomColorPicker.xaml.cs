using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static SylverInk.CommonUtils;
using static SylverInk.XAMLUtils.SettingsUtils;

namespace SylverInk.XAML;

/// <summary>
/// Interaction logic for CustomColorPicker.xaml
/// </summary>
public partial class CustomColorPicker : UserControl
{
	public string? ColorTag { get; set; }
	public Brush? LastColorSelection { get; set; }
	public RichTextBox? TextTarget { get; set; }

	public CustomColorPicker()
	{
		InitializeComponent();
	}

	private void CCBKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key is not Key.Enter)
			return;

		CustomColorSelection.IsOpen = false;
	}

	private void CustomColorFinished(object? sender, EventArgs e)
	{
		if (LastColorSelection is null)
			return;

		ColorChanged(ColorTag, LastColorSelection, TextTarget);
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
			"PT" => TextTarget?.Foreground ?? Brushes.Transparent,
			_ => Brushes.Transparent
		};
		CustomColor.Fill = color;
		CustomColorBox.Text = BytesFromBrush(color)[2..8];
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
}
