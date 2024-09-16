using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SylverInk
{
	public class NoteSettings : INotifyPropertyChanged
	{
		private bool _edited = false;
		private string _lastChanged = string.Empty;
		public event PropertyChangedEventHandler? PropertyChanged;

		public bool Edited { get => _edited; set { _edited = value; OnPropertyChanged(); } }
		public string LastChanged { get => _lastChanged; set { _lastChanged = value; OnPropertyChanged(); } }

		protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
