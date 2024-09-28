using System.ComponentModel;
using System.IO;

namespace SylverInk
{
	public partial class Database
	{
		public NoteController Controller = new();
		public string DBFile = string.Empty;
		public bool Loaded = false;

		public bool Changed => Controller.Changed;
		public string? Name { get => Controller.Name; set => Controller.Name = value; }

		public Database()
		{
			Controller = new();
			Loaded = Controller.Loaded;
		}

		public static void Create(string dbFile, bool threaded = false)
		{
			Database db = new();
			if (threaded)
			{
				BackgroundWorker worker = new();
				worker.DoWork += (_, _) => db.Load(dbFile);
				worker.RunWorkerCompleted += (_, _) => Common.AddDatabase(db);
				worker.RunWorkerAsync();

				return;
			}

			db.Load(dbFile);
			Common.AddDatabase(db);
		}

		public void Load(string dbFile)
		{
			Controller = new(DBFile = dbFile);
			Loaded = Controller.Loaded;

			if ((Name ?? string.Empty).Equals(string.Empty))
				Name = Path.GetFileNameWithoutExtension(DBFile);
		}

		public void MakeBackup(bool auto = false)
		{
			var DBPath = Common.GetDatabasePath(this);
			var BKPath = Common.GetBackupPath(this);

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

		public void Save()
		{
			if (!Changed)
				return;

			if (DBFile.Equals(string.Empty))
				DBFile = Common.GetDatabasePath(this);

			MakeBackup(true);
			
			if (!Directory.Exists(Path.GetDirectoryName(DBFile)))
				Directory.CreateDirectory(Path.GetDirectoryName(DBFile) ?? DBFile);

			if (!Controller.Open($"{DBFile}", true))
				return;

			Controller.SerializeRecords();
		}
	}
}
