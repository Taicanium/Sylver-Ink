using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SylverInk
{
	internal class Common
	{
		private readonly static string[] _dummyNames = ["Taica", "Unit 731", "Unit 732", "Tai Ca", "Dumbass", "Dumbfuck", "Some Guy", "Notafurrylad", "Floof", "Ballztothewallz", "Blazor", "Rhian", "Simha", "Coomer", "Dude", "Not Dude", "That One Chick", "Sephiroth", "Buizy", "Superfurry", "Butch Hartman", "Mah boi", "Goku"];
		private readonly static string[] _dummyStrings = ["a", "about", "all", "also", "and", "as", "at", "be", "because", "but", "by", "can", "come", "could", "day", "do", "even", "find", "first", "for", "from", "get", "give", "go", "have", "he", "her", "here", "him", "his", "how", "I", "if", "in", "into", "it", "its", "just", "know", "like", "look", "make", "man", "many", "me", "more", "my", "new", "no", "not", "now", "of", "on", "one", "only", "or", "other", "our", "out", "people", "say", "see", "she", "so", "some", "take", "tell", "than", "that", "the", "their", "them", "then", "there", "these", "they", "thing", "think", "this", "those", "time", "to", "two", "up", "use", "very", "want", "way", "we", "well", "what", "when", "which", "who", "will", "with", "would", "year", "you", "your"];
		private static Import? _import;
		private static Replace? _replace;
		private static Search? _search;
		private static Settings? _settings;

		public static bool CanResize { get; set; } = false;
		public static Database CurrentDatabase { get; set; } = new();
		public static bool DatabaseChanged { get; set; } = false;
		public static List<string> DatabaseFiles => Settings.DatabaseFiles;
		public static ObservableCollection<Database> Databases { get => Settings.Databases; set => Settings.Databases = value; }
		public static string DefaultDatabase { get; } = "New 1";
		public static double PPD { get; set; } = 1.0;
		public static bool ForceClose { get; set; } = false;
		public static Import? ImportWindow { get => _import; set { _import?.Close(); _import = value; _import?.Show(); } }
		private static BackgroundWorker? MeasureTask { get; set; }
		public static List<SearchResult> OpenQueries { get; } = [];
		private static int RecentEntries { get; set; } = 10;
		public static bool RepeatUpdate { get; set; } = false;
		public static Replace? ReplaceWindow { get => _replace; set { _replace?.Close(); _replace = value; _replace?.Show(); } }
		public static string RibbonTabContent { get; set; } = "CONTENT";
		public static Search? SearchWindow { get => _search; set { _search?.Close(); _search = value; _search?.Show(); } }
		public static ContextSettings Settings { get; } = new();
		public static Settings? SettingsWindow { get => _settings; set { _settings?.Close(); _settings = value; _settings?.Show(); } }
		private static double TextHeight { get; set; } = 0.0;
		private static BackgroundWorker? UpdateTask { get; set; }
		public static double WindowHeight { get; set; } = 275.0;
		public static double WindowWidth { get; set; } = 330.0;

		public static void AddDatabase(Database db)
		{
			var template = (DataTemplate)Application.Current.MainWindow.TryFindResource("DatabaseContentTemplate");
			var menu = (ContextMenu)Application.Current.MainWindow.TryFindResource("DatabaseContextMenu");
			var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");

			if ((db.Name ?? string.Empty).Equals(string.Empty))
			{
				db.Name = $"New 1";
				if (control.Items.Count > 0)
				{
					var lastItem = (TabItem)control.Items[^1];
					var header = (string)lastItem.Header;
					if (lastItem is not null && int.TryParse(header.Replace("New ", string.Empty), out int newIndex))
						newIndex++;
					else
						newIndex = 1;
					db.Name = $"New {newIndex}";
				}
				db.DBFile = $"{db.Name}.sidb";
			}

			var _name = db.Name ?? string.Empty;
			var dbHeaderLength = _name.Length > 12 ? 9 : _name.Length;
			var dbHeader = _name[..dbHeaderLength];
			if (_name.Length > 12)
				dbHeader += "...";

			TabItem item = new()
			{
				Content = template.LoadContent(),
				ContextMenu = menu,
				Header = dbHeader,
				Tag = db,
				ToolTip = db.Name,
			};

			item.MouseRightButtonDown += (sender, e) => { control.SelectedItem = item; };
			control.Items.Add(item);
			Databases.Add(db);
			UpdateContextMenu();
		}

		public static SolidColorBrush? BrushFromBytes(string data)
		{
			var hex = NumberStyles.HexNumber;
			Color color;
			if (data.Length < 6)
				return Brushes.Transparent;

			try
			{
				if (data.Length == 8)
				{
					color = new Color()
					{
						A = byte.Parse(data[..2], hex),
						R = byte.Parse(data[2..4], hex),
						G = byte.Parse(data[4..6], hex),
						B = byte.Parse(data[6..8], hex)
					};
					return new(color);
				}

				color = new Color()
				{
					A = 0xFF,
					R = byte.Parse(data[..2], hex),
					G = byte.Parse(data[2..4], hex),
					B = byte.Parse(data[4..6], hex)
				};
				return new(color);
			}
			catch
			{
				return null;
			}
		}

		public static string BytesFromBrush(Brush? brush, int colors = 4)
		{
			var data = brush as SolidColorBrush;
			if (colors == 4)
				return string.Format("{0:X2}{1:X2}{2:X2}{3:X2}", data?.Color.A, data?.Color.R, data?.Color.G, data?.Color.B);
			return string.Format("{0:X2}{1:X2}{2:X2}", data?.Color.R, data?.Color.G, data?.Color.B);
		}

		public static void DeferUpdateRecentNotes(bool RepeatUpdate = false)
		{
			if (!CanResize)
				return;

			var RecentBox = (ListBox?)GetChildPanel("DatabasesPanel").FindName("RecentNotes");
			var ChangesBox = (ListBox?)GetChildPanel("DatabasesPanel").FindName("ShortChanges");

			if ((RecentBox ?? ChangesBox) is null)
				return;

			if (UpdateTask is null)
			{
				UpdateTask = new();
				UpdateTask.DoWork += (_, _) => UpdateRecentNotes();
				UpdateTask.RunWorkerCompleted += (_, _) =>
				{
					CurrentDatabase.Controller.Sort(NoteController.SortType.ByChange);
					Settings.RecentNotes.Clear();
					for (int i = 0; i < Math.Min(RecentEntries, CurrentDatabase.Controller.RecordCount); i++)
						Settings.RecentNotes.Add(CurrentDatabase.Controller.GetRecord(i));
					CurrentDatabase.Controller.Sort();
					RecentBox?.Items.Refresh();
					ChangesBox?.Items.Refresh();
					UpdateRibbonTabs(RibbonTabContent);
				};
				UpdateTask.WorkerSupportsCancellation = true;
			}

			if (MeasureTask is null)
			{
				MeasureTask = new();
				MeasureTask.DoWork += (_, _) =>
				{
					SpinWait.SpinUntil(() => RecentBox?.IsMeasureValid ?? true, 1000);
					WindowHeight = RecentBox?.ActualHeight ?? Application.Current.MainWindow.ActualHeight - 225;
					WindowWidth = RecentBox?.ActualWidth ?? Application.Current.MainWindow.ActualHeight - 100;

					SpinWait.SpinUntil(() => !UpdateTask?.IsBusy ?? true, 200);
					if (UpdateTask?.IsBusy ?? false)
						UpdateTask?.CancelAsync();

					SpinWait.SpinUntil(() => !UpdateTask?.IsBusy ?? true, 200);
					if (UpdateTask?.IsBusy ?? false)
						UpdateTask?.Dispose();
				};
				MeasureTask.RunWorkerCompleted += (_, _) => UpdateTask?.RunWorkerAsync();
			}

			if (!MeasureTask.IsBusy)
				MeasureTask.RunWorkerAsync();

			if (RepeatUpdate)
			{
				var RepeatTask = new BackgroundWorker();
				RepeatTask.DoWork += (_, _) => SpinWait.SpinUntil(() => !MeasureTask.IsBusy, 1500);
				RepeatTask.RunWorkerCompleted += (_, _) => DeferUpdateRecentNotes();
				RepeatTask.RunWorkerAsync();
			}
		}

		public static string DialogFileSelect(bool outgoing = false, int filterIndex = 3, string? defaultName = null)
		{
			FileDialog dialog;

			if (outgoing)
			{
				dialog = new SaveFileDialog()
				{
					FileName = defaultName ?? DefaultDatabase,
					Filter = "Sylver Ink backup files (*.sibk)|*.sibk|Sylver Ink database files (*.sidb)|*.sidb|All files (*.*)|*.*",
					FilterIndex = filterIndex,
					ValidateNames = true,
				};

				return dialog.ShowDialog() is true ? dialog.FileName : string.Empty;
			}

			dialog = new OpenFileDialog()
			{
				CheckFileExists = true,
				Filter = "Sylver Ink backup files (*.sibk)|*.sibk|Sylver Ink database files (*.sidb)|*.sidb|Text files (*.txt)|*.txt|All files (*.*)|*.*",
				FilterIndex = filterIndex,
				ValidateNames = true,
			};

			return dialog.ShowDialog() is true ? dialog.FileName : string.Empty;
		}

		public static string GetRibbonHeader(int recordIndex)
		{
			string content = string.Empty;
			var record = CurrentDatabase.Controller.GetRecord(recordIndex);
			switch (RibbonTabContent)
			{
				case "CREATED":
					content = record.GetCreated();
					break;
				case "CHANGED":
					content = record.ShortChange;
					break;
				case "CONTENT":
					content = record.Preview;
					break;
				case "INDEX":
					content = $"Note #{recordIndex + 1:N0}";
					break;
			}

			if (content.Length > 13)
				content = content[..10] + "...";

			return content;
		}

		public static string GetRibbonTooltip(int recordIndex)
		{
			string content = string.Empty;
			var record = CurrentDatabase.Controller.GetRecord(recordIndex);
			switch (RibbonTabContent)
			{
				case "CREATED":
					content = $"{record.GetCreated()} — {record.Preview}";
					break;
				case "CHANGED":
					content = $"{record.ShortChange} — {record.Preview}";
					break;
				case "CONTENT":
					content = record.Preview;
					break;
				case "INDEX":
					content = $"Note #{recordIndex + 1:N0} — {record.Preview}";
					break;
			}

			return content;
		}

		public static TabControl GetChildPanel(string basePanel)
		{
			var db = (TabControl)Application.Current.MainWindow.FindName(basePanel);
			var dbItem = (TabItem)db.SelectedItem;
			var tabPanel = (TabControl)dbItem.Content;
			return tabPanel;
		}

		public static void MakeBackups()
		{
			for (int db = 0; db < Databases.Count; db++)
				Databases[db].MakeBackup(true);
		}

		public static string MakeDummySearchResult()
		{
			Random r = new();
			string result = new DateTime(r.Next(2004, 2030), r.Next(1, 13), r.Next(1, 29), r.Next(0, 24), r.Next(0, 60), r.Next(0, 60)).ToString("[yyyy-MM-dd HH:mm:ss]");

			var starter = _dummyStrings[r.Next(0, _dummyStrings.Length)];
			result = result + " " + _dummyNames[r.Next(0, _dummyNames.Length)] + ": " + starter[0].ToString().ToUpper() + starter[1..];

			for (int i = 1; i < r.Next(4, 25); i++)
			{
				result += " " + _dummyStrings[r.Next(0, _dummyStrings.Length)];
				switch (r.Next(0, 28))
				{
					case 5:
					case 6:
					case 7:
					case 8:
						result += ",";
						break;
					case 9:
						result += ";";
						break;
					case 10:
					case 11:
						result += "?";
						break;
					case 12:
					case 13:
						result += ".";
						break;
					case 14:
					case 15:
						result += "!";
						break;
					case 16:
						result += " - ";
						break;
					case 17:
						result += "...";
						break;
					case 18:
						result += "~";
						break;
					default:
						break;
				}
			}

			return result;
		}

		public static double MeasureTextSize(string text)
		{
			FormattedText ft = new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Settings.MainTypeFace, Settings.MainFontSize, Brushes.Black, PPD);
			return ft.Width;
		}

		private static double MeasureTextHeight(string text)
		{
			FormattedText ft = new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Settings.MainTypeFace, Settings.MainFontSize, Brushes.Black, PPD);
			return ft.Height;
		}

		public static void RemoveDatabase(Database db)
		{
			var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");

			for (int i = Databases.Count; i > 0; i--)
			{
				if ((Databases[i - 1].Name ?? string.Empty).Equals(db.Name))
					Databases.RemoveAt(i - 1);
			}

			control.SelectedIndex = Math.Max(0, control.SelectedIndex - 1);
			control.Items.RemoveAt(control.SelectedIndex + 1);

			UpdateContextMenu();
		}

		public static void UpdateContextMenu()
		{
			var menu = (ContextMenu)Application.Current.MainWindow.TryFindResource("DatabaseContextMenu");
			var control = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");

			if (control.Items.Count == 1)
			{
				foreach (DependencyObject mItem in menu.Items)
				{
					if ((mItem.GetValue(FrameworkElement.TagProperty) ?? string.Empty).Equals("Always"))
						continue;

					mItem.SetValue(UIElement.IsEnabledProperty, false);
				}

				return;
			}

			foreach (DependencyObject mItem in menu.Items)
			{
				if ((mItem.GetValue(FrameworkElement.TagProperty) ?? string.Empty).Equals("Always"))
					continue;

				mItem.SetValue(UIElement.IsEnabledProperty, true);
			}
		}

		private static void UpdateRecentNotes()
		{
			Application.Current.Resources["MainFontFamily"] = Settings.MainFontFamily;
			Application.Current.Resources["MainFontSize"] = Settings.MainFontSize;

			RecentEntries = 0;
			TextHeight = 0.0;
			CurrentDatabase.Controller.Sort(NoteController.SortType.ByChange);

			while (RecentEntries < CurrentDatabase.Controller.RecordCount && TextHeight < WindowHeight - 25.0)
			{
				var record = CurrentDatabase.Controller.GetRecord(RecentEntries);
				record.Preview = $"{Math.Floor(WindowWidth - 50.0)}";

				RecentEntries++;
				TextHeight += MeasureTextHeight(record.Preview) * 1.3333;
			}

			CurrentDatabase.Controller.Sort();
		}

		public static void UpdateRibbonTabs(string protocol)
		{
			RibbonTabContent = protocol;
			var control = GetChildPanel("DatabasesPanel");
			if (control is null)
				return;

			foreach (TabItem item in control.Items)
			{
				var tag = item.Tag;
				if (tag is null)
					continue;

				item.Header = GetRibbonHeader((int)tag);
				item.ToolTip = GetRibbonTooltip((int)tag);
			}
		}
	}
}
