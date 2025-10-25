using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static SylverInk.XAMLUtils.SettingsUtils;

namespace SylverInk.XAML;

/// <summary>
/// Interaction logic for ColorPicker.xaml
/// </summary>
public partial class ColorPicker : UserControl
{
	public List<SolidColorBrush> AvailableBrushes { get; } = [];

	public ColorPicker()
	{
		InitializeComponent();
	}

	public void InitBrushes(RichTextBox? textTarget = null)
	{
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

		InitColorGrid(textTarget);
	}

	private void InitColorGrid(RichTextBox? textTarget = null)
	{
		var column = 3;
		var row = 0;

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
				CustomColorPicker.LastColorSelection = textTarget?.Selection.IsEmpty is false ? ((System.Windows.Shapes.Rectangle)button.Content).Fill : null;
				ColorChanged(CustomColorPicker.ColorTag, CustomColorPicker.LastColorSelection, textTarget);
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

		System.Windows.Shapes.Rectangle clearRect = new()
		{
			Fill = new LinearGradientBrush([
				new(Colors.White, 0.0),
				new(Colors.White, 0.45),
				new(Colors.Red, 0.5),
				new(Colors.White, 0.55),
				new(Colors.White, 1.0)
			], new(0, 0), new(1, 1)),
			Margin = new(-1),
			Stretch = Stretch.UniformToFill,
			ToolTip = "Default color"
		};

		Button customOption = new()
		{
			Content = rainbowRect,
			Height = 20,
			Margin = new(2.5),
			Width = 20
		};

		Button clearOption = new()
		{
			Content = clearRect,
			Height = 20,
			Margin = new(2.5),
			Width = 20
		};

		customOption.Click += (_, _) =>
		{
			ColorSelection.IsOpen = false;
			CustomColorPicker.CustomColorSelection.IsOpen = true;
		};

		clearOption.Click += (_, _) =>
		{
			CustomColorPicker.LastColorSelection = null;
			ColorChanged(CustomColorPicker.ColorTag, null, textTarget);
		};

		ColorGrid.Children.Add(customOption);
		ColorGrid.Children.Add(clearOption);

		Grid.SetColumn(customOption, 1);
	}

	[GeneratedRegex(@"\p{Lu}")]
	private static partial Regex Uppercase();
}
