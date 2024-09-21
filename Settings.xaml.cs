using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for Settings.xaml
	/// </summary>
	public partial class Settings : Window
	{
		private bool HourSelected = false;
		private bool MinuteSelected = false;

		private static int ArialIndex = 0;
		private static string? ColorTag;
		public Brush? LastColorSelection { get; set; }
		private static List<SolidColorBrush> AvailableBrushes { get; } = [];
		private static List<FontFamily> AvailableFonts { get; } = [];

		public Settings()
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

		private void BackupClick(object sender, RoutedEventArgs e)
		{
			var filename = Common.DialogFileSelect(true);
			Common.MakeBackup(filename);
		}

		private void CloseClick(object sender, RoutedEventArgs e) => Close();

		public void ColorChanged()
		{
			switch (ColorTag)
			{
				case "P1F":
					Common.Settings.MenuForeground = LastColorSelection;
					break;
				case "P1B":
					Common.Settings.MenuBackground = LastColorSelection;
					break;
				case "P2F":
					Common.Settings.ListForeground = LastColorSelection;
					break;
				case "P2B":
					Common.Settings.ListBackground = LastColorSelection;
					break;
				case "P3F":
					Common.Settings.AccentForeground = LastColorSelection;
					break;
				case "P3B":
					Common.Settings.AccentBackground = LastColorSelection;
					break;
			}
		}

		private void ColorPopup(object sender, RoutedEventArgs e)
		{
			var button = (Button)sender;
			ColorTag = (string)button.Tag;
			ColorSelection.IsOpen = true;
		}

		private void CustomColorFinished(object sender, EventArgs e)
		{
			if (LastColorSelection is null)
				return;
			ColorChanged();
		}

		private void CustomColorOpened(object sender, EventArgs e)
		{
			Brush? color = Brushes.Transparent;
			switch (ColorTag)
			{
				case "P1F":
					color = Common.Settings.MenuForeground;
					break;
				case "P1B":
					color = Common.Settings.MenuBackground;
					break;
				case "P2F":
					color = Common.Settings.ListForeground;
					break;
				case "P2B":
					color = Common.Settings.ListBackground;
					break;
				case "P3F":
					color = Common.Settings.AccentForeground;
					break;
				case "P3B":
					color = Common.Settings.AccentBackground;
					break;
			}
			CustomColor.Fill = color;
			CustomColorBox.Text = Common.BytesFromBrush(color, 3);
		}

		private void EraseClick(object sender, RoutedEventArgs e)
		{
			if (MessageBox.Show("Are you sure you want to erase your notes and create a new database?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
				return;

			NoteController.EraseDatabase();
		}

		private void FontSizeChanged(object sender, RoutedEventArgs e)
		{
			var button = (Button)sender;
			if (button.Content.Equals("-"))
				Common.Settings.MainFontSize -= 0.5;
			else
				Common.Settings.MainFontSize += 0.5;
			Common.DeferUpdateRecentNotes();
		}

		private void Hour_Selected(object sender, RoutedEventArgs e)
		{
			HourSelected = true;
			ApplyTime();
		}

		private static uint HSVFromRGB(SolidColorBrush brush)
		{
			double r_ = brush.Color.R / 255.0;
			double g_ = brush.Color.G / 255.0;
			double b_ = brush.Color.B / 255.0;
			var Cmax = Math.Max(r_, Math.Max(g_, b_));
			var Cmin = Math.Min(r_, Math.Min(g_, b_));
			var delta = Cmax - Cmin;
			var _h = 0.0;
			if (delta != 0.0)
			{
				if (Cmax == r_)
					_h = (60.0 * ((g_ - b_) / delta) + 360.0) % 360.0;
				if (Cmax == g_)
					_h = (60.0 * ((b_ - r_) / delta) + 120.0) % 360.0;
				if (Cmax == b_)
					_h = (60.0 * ((r_ - g_) / delta) + 240.0) % 360.0;
			}
			var _s = Cmax == 0.0 ? 0.0 : (delta / Cmax);
			var _v = Cmax;
			var H = (uint)(_h * 255.0 / 360.0);
			var S = (uint)(_s * 255.0);
			var V = (uint)(_v * 255.0);
			return (H << 16) + (S << 8) + V;
		}

		private void MenuFontChanged(object sender, SelectionChangedEventArgs e)
		{
			var item = (ComboBoxItem)MenuFont.SelectedItem;
			Common.Settings.MainFontFamily = item.FontFamily;
			Common.DeferUpdateRecentNotes();
		}

		private void Minute_Selected(object sender, RoutedEventArgs e)
		{
			MinuteSelected = true;
			ApplyTime();
		}

		private void NewCustomColor(object sender, TextChangedEventArgs e)
		{
			var box = (TextBox)sender;
			var text = box.Text.StartsWith('#') ? box.Text[1..] : box.Text;
			var brush = Common.BrushFromBytes(text);

			CustomColor.Fill = brush ?? Brushes.Transparent;
			LastColorSelection = brush;
		}

		private void ResetClick(object sender, RoutedEventArgs e)
		{
			Common.Settings.MenuBackground = Brushes.Beige;
			Common.Settings.MenuForeground = Brushes.Black;
			Common.Settings.ListBackground = Brushes.White;
			Common.Settings.ListForeground = Brushes.Black;
			Common.Settings.AccentBackground = Brushes.Khaki;
			Common.Settings.AccentForeground = Brushes.Blue;
			Common.Settings.MainFontSize = 11.0;
			MenuFont.SelectedIndex = ArialIndex;
			Common.Settings.MainFontFamily = ((ComboBoxItem)MenuFont.SelectedItem).FontFamily;

			Common.DeferUpdateRecentNotes(true);
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

			NoteController.Revert(reversion);
		}

		private void SelectedDateChanged(object sender, SelectionChangedEventArgs e)
		{
			if (ReversionDate.SelectedDate is null)
				RestoreButton.IsEnabled = false;
			else
				RestoreButton.IsEnabled = true;
		}

		private void SelectTime(object sender, RoutedEventArgs e)
		{
			TimeSelector.IsOpen = true;
			HourSelected = false;
			MinuteSelected = false;
		}

		private void Settings_Loaded(object sender, RoutedEventArgs e)
		{
			if (AvailableFonts.Count == 0)
			{
				var fonts = Fonts.SystemFontFamilies;

				foreach (var font in fonts)
					AvailableFonts.Add(font);

				AvailableFonts.Sort(new Comparison<FontFamily>((f1, f2) => f1.Source.CompareTo(f2.Source)));

				foreach (var property in typeof(Brushes)?.GetProperties() ?? [])
				{
					if (property.GetMethod?.Invoke(null, null) is not SolidColorBrush brush)
						continue;

					if (brush.Color.A < 0xFF)
						continue;

					SolidColorBrush brushCopy = new(brush.Color);
					brushCopy.SetValue(TagProperty, UppercaseLetters().Replace(property.Name, new MatchEvaluator((match) => " " + match.Value)).Trim());
					AvailableBrushes.Add(brushCopy);
				}

				AvailableBrushes.Sort(new Comparison<SolidColorBrush>((brush1, brush2) =>
				{
					var HSV1 = HSVFromRGB(brush1);
					var HSV2 = HSVFromRGB(brush2);
					return HSV1 > HSV2 ? 1 : (HSV1 < HSV2 ? -1 : 0);
				}));
			}

			for (int i = 0; i < 10; i++)
				ColorGrid.ColumnDefinitions.Add(new() { Width = new(1.0, GridUnitType.Star) });

			for (int i = 0; i < 15; i++)
				ColorGrid.RowDefinitions.Add(new() { Height = new(1.0, GridUnitType.Star) });

			var column = 1;
			var row = 0;

			System.Windows.Shapes.Rectangle rainbowRect = new()
			{
				Fill = new LinearGradientBrush([
					new(Colors.Red, 0.0),
					new(Colors.Yellow, 0.25),
					new(Colors.Green, 0.5),
					new(Colors.Blue, 0.75),
					new(Colors.Violet, 1.0),
				], 45.0),
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

			customOption.Click += (sender, e) =>
			{
				ColorSelection.IsOpen = false;
				CustomColorSelection.IsOpen = true;
			};

			ColorGrid.Children.Add(customOption);

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

				option.Click += (sender, e) => {
					var button = (Button)sender;
					LastColorSelection = ((System.Windows.Shapes.Rectangle)button.Content).Fill;
					ColorChanged();
				};

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

			for (int i = 0; i < 24; i++)
			{
				ComboBoxItem item = new()
				{
					Content = $"{i:0,0}"
				};
				Hour.Items.Add(item);
			}

			for (int i = 0; i < 60; i++)
			{
				ComboBoxItem item = new()
				{
					Content = $"{i:0,0}"
				};
				Minute.Items.Add(item);
			}

			Hour.SelectedIndex = 0;
			Minute.SelectedIndex = 0;
		}

		[GeneratedRegex(@"\p{Lu}")]
		private static partial Regex UppercaseLetters();
	}
}
