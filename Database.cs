using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows.Controls;
using static SylverInk.Common;

namespace SylverInk
{
	public partial class Database
	{
		public NoteController Controller = new();
		public string DBFile = string.Empty;
		public bool Loaded = false;

		public bool Changed { get => Controller.Changed; set => Controller.Changed = value; }
		public NetClient? Client;
		private StackPanel? HeaderPanel;
		public string? Name { get => Controller.Name; set => Controller.Name = value; }
		public int RecordCount => Controller.RecordCount;
		public NetServer? Server;
		public string UUID { get; set; } = MakeUUID(UUIDType.Database);
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

		public int CreateRecord(string entry)
		{
			int index = Controller.CreateRecord(entry);

			if (Client?.Connected is true || Server?.Serving is true)
			{
				var outBuffer = new List<byte>([
					(byte)((entry.Length >> 24) & 0xFF),
					(byte)((entry.Length >> 16) & 0xFF),
					(byte)((entry.Length >> 8) & 0xFF),
					(byte)(entry.Length & 0xFF),
				]);

				if (entry.Length > 0)
					outBuffer.AddRange(Encoding.UTF8.GetBytes(entry));

				Transmit(Network.MessageType.RecordAdd, [.. outBuffer]);
			}

			return index;
		}

		public void CreateRevision(int index, string newVersion, bool inhibitNetwork = false)
		{
			Controller.CreateRevision(index, newVersion);
			if (!inhibitNetwork)
			{
				var outBuffer = new List<byte>([
					(byte)((index >> 24) & 0xFF),
					(byte)((index >> 16) & 0xFF),
					(byte)((index >> 8) & 0xFF),
					(byte)(index & 0xFF),
					(byte)((newVersion.Length >> 24) & 0xFF),
					(byte)((newVersion.Length >> 16) & 0xFF),
					(byte)((newVersion.Length >> 8) & 0xFF),
					(byte)(newVersion.Length & 0xFF)
				]);

				if (newVersion.Length > 0)
					outBuffer.AddRange(Encoding.UTF8.GetBytes(newVersion));

				Transmit(Network.MessageType.TextInsert, [.. outBuffer]);
			}
			DeferUpdateRecentNotes(true);
		}

		public void DeleteRecord(int index)
		{
			Controller.DeleteRecord(index);

			if (Client?.Connected is true || Server?.Serving is true)
			{
				byte[] outBuffer = [
					(byte)((index >> 24) & 0xFF),
					(byte)((index >> 16) & 0xFF),
					(byte)((index >> 8) & 0xFF),
					(byte)(index & 0xFF),
				];

				Transmit(Network.MessageType.RecordRemove, outBuffer);
			}
		}

		public void Erase()
		{
			if (Client?.Connected is true || Server?.Serving is true)
				return;

			Controller.EraseDatabase();
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

		public void Lock(int index)
		{
			if (!Client?.Connected is true && !Server?.Serving is true)
				return;

			byte[] outBuffer = [
				(byte)((index >> 24) & 0xFF),
				(byte)((index >> 16) & 0xFF),
				(byte)((index >> 8) & 0xFF),
				(byte)(index & 0xFF),
			];

			Controller.GetRecord(index).Lock();
			Transmit(Network.MessageType.RecordLock, outBuffer);
		}

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

		public (int, int) Replace(string oldText, string newText) => Controller.Replace(oldText, newText);

		public void Revert(DateTime targetDate) => Controller.Revert(targetDate);

		public void Save()
		{
			if (!Changed)
				return;

			if (DBFile.Equals(string.Empty))
				DBFile = GetDatabasePath(this);

			MakeBackup(true);
			
			if (!Directory.Exists(Path.GetDirectoryName(DBFile)))
				Directory.CreateDirectory(Path.GetDirectoryName(DBFile) ?? DBFile);

			if (!Controller.Open($"{DBFile}", true))
				return;

			Controller.SerializeRecords();

			if (DBFile.Contains(Path.Join(DocumentsSubfolders["Databases"])))
				File.WriteAllText(Path.Join(Path.GetDirectoryName(DBFile), "uuid.dat"), UUID);
		}

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

		public void Unlock(int index)
		{
			if (!Client?.Connected is true && !Server?.Serving is true)
				return;

			byte[] outBuffer = [
				(byte)((index >> 24) & 0xFF),
				(byte)((index >> 16) & 0xFF),
				(byte)((index >> 8) & 0xFF),
				(byte)(index & 0xFF),
			];

			Controller.GetRecord(index).Unlock();
			Transmit(Network.MessageType.RecordUnlock, outBuffer);
		}

		public void UpdateWordPercentages() => Controller.UpdateWordPercentages();
	}
}
