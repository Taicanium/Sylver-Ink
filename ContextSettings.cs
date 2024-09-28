using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace SylverInk
{
	public class ContextSettings : INotifyPropertyChanged
	{
		private Brush? _accentBackgound = Brushes.Khaki;
		private Brush? _accentForegound = Brushes.Blue;
		private string _importData = string.Empty;
		private string _importTarget = string.Empty;
		private int _lineTolerance = 2;
		private Brush? _listBackgound = Brushes.White;
		private Brush? _listForegound = Brushes.Black;
		private FontFamily? _mainFontFamily = new("Arial");
		private double _mainFontSize = 11.0;
		private Typeface? _mainTypeFace;
		private Brush? _menuBackgound = Brushes.Beige;
		private Brush? _menuForegound = Brushes.DimGray;
		private string _numReplacements = string.Empty;
		public event PropertyChangedEventHandler? PropertyChanged;
		private bool _readyToFinalize = false;
		private bool _readyToReplace = false;
		private readonly ObservableCollection<NoteRecord> _recentNotes = [];
		private readonly ObservableCollection<NoteRecord> _searchResults = [];
		private bool _searchResultsOnTop = false;
		private bool _snapSearchResults = true;
		private readonly string _versionString = $"v. {Assembly.GetExecutingAssembly().GetName().Version} © Taica, {GetBuildYear(Assembly.GetExecutingAssembly())}";

		public Brush? AccentBackground { get => _accentBackgound; set { _accentBackgound = value; OnPropertyChanged(); } }
		public Brush? AccentForeground { get => _accentForegound; set { _accentForegound = value; OnPropertyChanged(); } }
		public string ImportData { get => _importData; set { _importData = value; OnPropertyChanged(); } }
		public string ImportTarget { get => _importTarget; set { _importTarget = value; OnPropertyChanged(); } }
		public int LineTolerance { get => _lineTolerance; set { _lineTolerance = Math.Min(36, Math.Max(0, value)); OnPropertyChanged(); } }
		public Brush? ListBackground { get => _listBackgound; set { _listBackgound = value; OnPropertyChanged(); } }
		public Brush? ListForeground { get => _listForegound; set { _listForegound = value; OnPropertyChanged(); } }
		public FontFamily? MainFontFamily { get => _mainFontFamily; set { _mainFontFamily = value; _mainTypeFace = new(value, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal); OnPropertyChanged(); } }
		public double MainFontSize { get => _mainFontSize; set { _mainFontSize = Math.Min(24.0, Math.Max(10.0, value)); OnPropertyChanged(); } }
		public Typeface? MainTypeFace { get => _mainTypeFace; set { _mainTypeFace = value; OnPropertyChanged(); } }
		public Brush? MenuBackground { get => _menuBackgound; set { _menuBackgound = value; OnPropertyChanged(); } }
		public Brush? MenuForeground { get => _menuForegound; set { _menuForegound = value; OnPropertyChanged(); } }
		public string NumReplacements { get => _numReplacements; set { _numReplacements = value; OnPropertyChanged(); } }
		public bool ReadyToFinalize { get => _readyToFinalize; set { _readyToFinalize = value; OnPropertyChanged(); } }
		public bool ReadyToReplace { get => _readyToReplace; set { _readyToReplace = value; OnPropertyChanged(); } }
		public ObservableCollection<NoteRecord> RecentNotes => _recentNotes;
		public ObservableCollection<NoteRecord> SearchResults => _searchResults;
		public bool SearchResultsOnTop { get => _searchResultsOnTop; set { _searchResultsOnTop = value; OnPropertyChanged(); } }
		public bool SnapSearchResults { get => _snapSearchResults; set { _snapSearchResults = value; OnPropertyChanged(); } }
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

		protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
