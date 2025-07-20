using SylverInk.Notes;
using SylverInk.XAMLUtils;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SylverInk;

/// <summary>
/// Interaction logic for Properties.xaml
/// </summary>
public partial class Properties : Window
{
	public Database? DB { get; set; }
	public bool HourSelected { get; set; }
	public bool MinuteSelected { get; set; }

	public Properties()
	{
		InitializeComponent();
		DataContext = CommonUtils.Settings;
	}

	private void CloseClick(object? sender, RoutedEventArgs e) => Close();

	private void Drag(object? sender, MouseButtonEventArgs e) => DragMove();

	private void Hour_Selected(object? sender, RoutedEventArgs e)
	{
		HourSelected = true;
		this.ApplyTime();
	}

	private void Minute_Selected(object? sender, RoutedEventArgs e)
	{
		MinuteSelected = true;
		this.ApplyTime();
	}

	private void Properties_Loaded(object? sender, RoutedEventArgs e)
	{
		this.InitializeProperties();

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

		this.Revert();
	}

	private void SelectedDateChanged(object? sender, SelectionChangedEventArgs e) => RestoreButton.IsEnabled = ReversionDate.SelectedDate is not null;

	private void SelectTime(object? sender, RoutedEventArgs e)
	{
		HourSelected = false;
		MinuteSelected = false;
		TimeSelector.IsOpen = true;
	}
}