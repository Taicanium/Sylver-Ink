using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using static SylverInk.Common;

namespace SylverInk
{
	public partial class Database
	{
		private NoteController Controller = new();
		public string DBFile = string.Empty;
		public bool Loaded = false;

		public bool Changed { get => Controller.Changed; set => Controller.Changed = value; }
		public NetClient? Client;
		public long? Created;
		public int Format { get => Controller.Format; set => Controller.Format = value; }
		private StackPanel? HeaderPanel;
		public string? Name { get => Controller.Name; set => Controller.Name = value; }
		public int RecordCount => Controller.RecordCount;
		public NetServer? Server;
		public string? UUID { get => Controller.UUID; set => Controller.UUID = value; }
		public Dictionary<string, double> WordPercentages => Controller.WordPercentages;

		public Database()
		{
			Client = new(this);
			Controller = new();
			Loaded = Controller.Loaded;
			Server = new(this);
		}

		public static void Create(string dbFile, bool threaded = false)
		{
			Database db = new();
			if (threaded)
			{
				BackgroundWorker worker = new();
				worker.DoWork += (_, _) => db.Load(dbFile);
				worker.RunWorkerCompleted += (_, _) => AddDatabase(db);
				worker.RunWorkerAsync();

				return;
			}

			db.Load(dbFile);
			AddDatabase(db);
		}

		public int CreateRecord(string entry, bool local = true)
		{
			int index = Controller.CreateRecord(entry);

			if (local)
			{
				var outBuffer = new List<byte>([0, 0, 0, 0, .. IntToBytes(entry.Length)]);

				if (entry.Length > 0)
					outBuffer.AddRange(Encoding.UTF8.GetBytes(entry));

				Transmit(Network.MessageType.RecordAdd, [.. outBuffer]);
			}

			DeferUpdateRecentNotes();

			return index;
		}

		public void CreateRevision(int index, string newVersion, bool local = true)
		{
			Controller.CreateRevision(index, newVersion);

			if (!local)
				return;

			var outBuffer = new List<byte>([
				.. IntToBytes(index),
					.. IntToBytes(newVersion.Length)
			]);

			if (newVersion.Length > 0)
				outBuffer.AddRange(Encoding.UTF8.GetBytes(newVersion));

			Transmit(Network.MessageType.TextInsert, [.. outBuffer]);
		}

		public void CreateRevision(NoteRecord record, string newVersion, bool local = true)
		{
			Controller.CreateRevision(record, newVersion);

			if (!local)
				return;

			var outBuffer = new List<byte>([
				.. IntToBytes(record.Index),
					.. IntToBytes(newVersion.Length)
			]);

			if (newVersion.Length > 0)
				outBuffer.AddRange(Encoding.UTF8.GetBytes(newVersion));

			Transmit(Network.MessageType.TextInsert, [.. outBuffer]);
		}

		public void DeleteRecord(int index, bool local = true)
		{
			Controller.DeleteRecord(index);

			if (local)
				Transmit(Network.MessageType.RecordRemove, IntToBytes(index));
		}

		public void DeleteRecord(NoteRecord record, bool local = true)
		{
			for (int index = RecordCount - 1; index > -1; index--)
			{
				if (!Controller.GetRecord(index).Equals(record))
					continue;

				Controller.DeleteRecord(index);

				if (local)
					Transmit(Network.MessageType.RecordRemove, IntToBytes(index));
			}
		}

		public void DeserializeRecords(List<byte>? inMemory = null) => Controller.DeserializeRecords(inMemory);

		public void Erase()
		{
			if (Client?.Connected is true || Server?.Serving is true)
				return;

			Controller.EraseDatabase();
		}

		public string GetCreated()
		{
			if (Created is not null)
				return DateTime.FromBinary((long)Created).ToString(DateFormat);

			var CreatedObject = DateTime.UtcNow;
			for (int i = 0; i < RecordCount; i++)
			{
				var other = Controller.GetRecord(i).GetCreatedObject();
				if (CreatedObject.CompareTo(other) > 0)
					CreatedObject = other;
			}

			Created = CreatedObject.ToBinary();
			return CreatedObject.ToString(DateFormat);
		}

		public object GetHeader()
		{
			string? headerContent;
			Label label;
			if (HeaderPanel is not null)
			{
				HeaderPanel.Dispatcher.Invoke(() =>
				{
					label = (Label)HeaderPanel.Children[0];

					headerContent = Name;
					if (headerContent?.Length > 12)
						headerContent = $"{headerContent[..10]}...";

					label.Content = headerContent;

					HeaderPanel.Children.RemoveAt(1);
					HeaderPanel.Children.Add((Client?.Active is true ? Client?.Indicator : Server?.Indicator) ?? new System.Windows.Shapes.Ellipse());
					HeaderPanel.ToolTip = Name;
				});

				return HeaderPanel;
			}

			headerContent = Name;
			if (headerContent?.Length > 12)
				headerContent = $"{headerContent[..10]}...";

			label = new Label()
			{
				Content = headerContent,
				Margin = new(0),
			};

			HeaderPanel = new StackPanel()
			{
				ContextMenu = (ContextMenu)Application.Current.MainWindow.TryFindResource("DatabaseContextMenu"),
				Margin = new(0),
				Orientation = Orientation.Horizontal,
				ToolTip = Name,
			};

			HeaderPanel.Children.Add(label);
			HeaderPanel.Children.Add((Client?.Active is true ? Client?.Indicator : Server?.Indicator) ?? new System.Windows.Shapes.Ellipse());

			return HeaderPanel;
		}

		public NoteRecord GetRecord(int index) => Controller.GetRecord(index);

		public void Initialize(bool newDatabase = true) => Controller.InitializeRecords(newDatabase);

		public void Load(string dbFile)
		{
			Controller = new(DBFile = dbFile);
			Loaded = Controller.Loaded;

			if ((Name ?? string.Empty).Equals(string.Empty))
				Name = Path.GetFileNameWithoutExtension(DBFile);
		}

		public void Lock(int index) => Controller.GetRecord(index).Lock();

		public void MakeBackup(bool auto = false)
		{
			var DBPath = GetDatabasePath(this);
			var BKPath = GetBackupPath(this);

			if (auto)
			{
				for (int i = 2; i > 0; i--)
				{
					if (File.Exists($"{BKPath}_{i}.sibk"))
						File.Copy($"{BKPath}_{i}.sibk", $"{BKPath}_{i + 1}.sibk", true);
				}

				if (File.Exists($"{DBPath}"))
					File.Copy($"{DBPath}", $"{BKPath}_1.sibk", true);

				return;
			}

			Controller.MakeBackup();
		}

		public bool Open(string path, bool writing = false) => Controller.Open(path, writing);

		public void Rename(string newName)
		{
			var overwrite = false;
			var oldFile = DBFile;
			var oldName = Name;
			var oldPath = Path.GetDirectoryName(oldFile);

			Changed = true;
			Name = newName;

			DBFile = GetDatabasePath(CurrentDatabase);
			var newFile = DBFile;
			var newPath = Path.GetDirectoryName(newFile);

			var panel = (TabControl)Application.Current.MainWindow.FindName("DatabasesPanel");
			var currentTab = (TabItem)panel.SelectedItem;
			currentTab.Header = GetHeader();

			if (!File.Exists(oldFile))
				return;

			if (!Directory.Exists(oldPath))
				return;

			var directorySearch = Directory.GetDirectories(Subfolders["Databases"], "*", new EnumerationOptions() { IgnoreInaccessible = true, RecurseSubdirectories = true, MaxRecursionDepth = 3 });
			if (oldPath is not null && newPath is not null && directorySearch.Contains(oldPath))
			{
				if (Directory.Exists(newPath))
				{
					if (MessageBox.Show($"A database with that name already exists in {newPath}.\n\nDo you want to overwrite it?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
					{
						DBFile = oldFile;
						Name = oldName;
						currentTab.Header = GetHeader();
						return;
					}
					Directory.Delete(newPath, true);
					overwrite = true;
				}
				else
					Directory.Move(oldPath, newPath);
			}

			var adjustedPath = Path.Join(Path.GetDirectoryName(newFile), Path.GetFileName(oldFile));

			if (!File.Exists(adjustedPath))
				return;

			if (File.Exists(newFile) && !overwrite)
			{
				if (MessageBox.Show($"A database with that name already exists at {newFile}.\n\nDo you want to overwrite it?", "Sylver Ink: Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
				{
					DBFile = oldFile;
					Name = oldName;
					currentTab.Header = GetHeader();
					return;
				}
				overwrite = true;
			}

			if (File.Exists(newFile) && overwrite)
				File.Delete(newFile);

			File.Move(adjustedPath, newFile);
		}

		public (int, int) Replace(string oldText, string newText, bool local = true)
		{
			if (local)
			{
				var oldLength = oldText.Length;
				var newLength = newText.Length;

				List<byte> outBuffer = [
					0, 0, 0, 0,
					.. IntToBytes(oldLength),
					.. IntToBytes(newLength),
				];

				outBuffer.InsertRange(8, Encoding.UTF8.GetBytes(oldText));
				outBuffer.AddRange(Encoding.UTF8.GetBytes(newText));

				Transmit(Network.MessageType.RecordReplace, [.. outBuffer]);
			}

			return Controller.Replace(oldText, newText);
		}

		public void Revert(DateTime targetDate) => Controller.Revert(targetDate);

		public void Save()
		{
			if (!Changed)
				return;

			if (DBFile.Equals(string.Empty))
				DBFile = GetDatabasePath(this);

			if (UUID is null || UUID.Equals(string.Empty))
				UUID = MakeUUID(UUIDType.Database);

			MakeBackup(true);
			
			if (!Directory.Exists(Path.GetDirectoryName(DBFile)))
				Directory.CreateDirectory(Path.GetDirectoryName(DBFile) ?? DBFile);

			if (!Controller.Open($"{DBFile}", true))
				return;

			Controller.SerializeRecords();

			if (DBFile.Contains(Subfolders["Databases"]))
				File.WriteAllText(Path.Join(Path.GetDirectoryName(DBFile), "uuid.dat"), UUID);
		}

		public List<byte>? SerializeRecords(bool inMemory = false) => Controller.SerializeRecords(inMemory);

		public void Sort(SortType type = SortType.ByIndex)
		{
			if (type == SortType.ByIndex)
				Controller.PropagateIndices();
			Controller.Sort(type);
		}

		public void Transmit(Network.MessageType type, byte[] data)
		{
			if (Client?.Connected is true)
				Client?.Send(type, data);

			if (Server?.Serving is true)
				Server?.Broadcast(type, data);
		}

		public void Unlock(int index) => Controller.GetRecord(index).Unlock();

		public void UpdateWordPercentages() => Controller.UpdateWordPercentages();
	}
}
