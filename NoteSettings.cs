using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace SylverInk
{
	public class NoteSettings : INotifyPropertyChanged
	{
		private Brush? _accentBackgound = Brushes.Khaki;
		private Brush? _accentForegound = Brushes.Blue;
		private bool _edited = false;
		private FontFamily? _fontFamily = new("Arial");
		private double _fontSize = 11.0;
		private string _lastChanged = string.Empty;
		private Brush? _listBackgound = Brushes.White;
		private Brush? _listForegound = Brushes.Black;
		private Brush? _menuBackgound = Brushes.Beige;
		private Brush? _menuForegound = Brushes.Gray;
		public event PropertyChangedEventHandler? PropertyChanged;

		public Brush? AccentBackground { get => _accentBackgound; set { _accentBackgound = value; OnPropertyChanged(); } }
		public Brush? AccentForeground { get => _accentForegound; set { _accentForegound = value; OnPropertyChanged(); } }
		public bool Edited { get => _edited; set { _edited = value; OnPropertyChanged(); } }
		public Brush? ListBackground { get => _listBackgound; set { _listBackgound = value; OnPropertyChanged(); } }
		public Brush? ListForeground { get => _listForegound; set { _listForegound = value; OnPropertyChanged(); } }
		public FontFamily? MainFontFamily { get => _fontFamily; set { _fontFamily = value; OnPropertyChanged(); } }
		public double MainFontSize { get => _fontSize; set { _fontSize = value; OnPropertyChanged(); } }
		public Brush? MenuBackground { get => _menuBackgound; set { _menuBackgound = value; OnPropertyChanged(); } }
		public Brush? MenuForeground { get => _menuForegound; set { _menuForegound = value; OnPropertyChanged(); } }
		public string LastChanged { get => _lastChanged; set { _lastChanged = value; OnPropertyChanged(); } }

		protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
