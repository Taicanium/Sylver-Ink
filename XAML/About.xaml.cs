using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace SylverInk
{
	/// <summary>
	/// Interaction logic for Help.xaml
	/// </summary>
	public partial class About : Window
	{
		public About()
		{
			InitializeComponent();
			DataContext = Common.Settings;
		}

		private void CloseClick(object sender, RoutedEventArgs e)
		{
			Close();
		}

		private void Drag(object sender, MouseButtonEventArgs e) => DragMove();

		private void FollowHyperlink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
		{
			Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
			e.Handled = true;
		}
	}
}
