using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SylverInk
{
	public class ContextSettings : INotifyPropertyChanged
	{
		private string _importData = string.Empty;
		private string _importTarget = string.Empty;
		private string _numReplacements = string.Empty;
		public event PropertyChangedEventHandler? PropertyChanged;
		private bool _readyToFinalize = false;
		private bool _readyToReplace = false;
		private ObservableCollection<NoteRecord> _recentNotes = [];
		private ObservableCollection<NoteRecord> _searchResults = [];
		private double _searchTabHeight = 0.0;
		private readonly string _versionString = "Sylver Ink — Version " + Assembly.GetExecutingAssembly().GetName().Version + " © Taica, " + GetBuildYear(Assembly.GetExecutingAssembly());

		public string ImportData { get => _importData; set { _importData = value; OnPropertyChanged(); } }
		public string ImportTarget { get => _importTarget; set { _importTarget = value; OnPropertyChanged(); } }
		public string NumReplacements { get => _numReplacements; set { _numReplacements = value; OnPropertyChanged(); } }
		public bool ReadyToFinalize { get => _readyToFinalize; set { _readyToFinalize = value; OnPropertyChanged(); } }
		public bool ReadyToReplace { get => _readyToReplace; set { _readyToReplace = value; OnPropertyChanged(); } }
		public ObservableCollection<NoteRecord> RecentNotes { get => _recentNotes; set { _recentNotes = value; OnPropertyChanged(); } }
		public ObservableCollection<NoteRecord> SearchResults { get => _searchResults; set { _searchResults = value; OnPropertyChanged(); } }
		public double SearchTabHeight { get => _searchTabHeight; set { _searchTabHeight = value; OnPropertyChanged(); } }
		public string VersionString => _versionString;

		private static int GetBuildYear(Assembly assembly)
		{
			const string prefix = "+build";
			var attr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

			string? value = attr?.InformationalVersion;
			int index = value?.IndexOf(prefix) ?? 0;
			if (index > 0 && DateTime.TryParseExact(value?[(index + prefix.Length)..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
				return result.Year;

			return default;
		}

		protected void OnPropertyChanged([CallerMemberName] string? name = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		}
	}
}
