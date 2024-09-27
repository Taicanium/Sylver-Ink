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
			var Extensionless = Path.GetFileNameWithoutExtension(DBFile);

			if (auto)
			{
				for (int i = 2; i > 0; i--)
				{
					if (File.Exists($"{Extensionless}_{i}.sibk"))
						File.Copy($"{Extensionless}_{i}.sibk", $"{Extensionless}_{i + 1}.sibk", true);
				}

				if (File.Exists($"{DBFile}"))
					File.Copy($"{DBFile}", $"{Extensionless}_1.sibk", true);

				return;
			}

			Controller.MakeBackup();
		}

		public void Save()
		{
			if (!Changed)
				return;

			MakeBackup(true);

			if (DBFile.Equals(string.Empty))
				DBFile = Common.DialogFileSelect(true, 2, Name);

			if (!Controller.Open($"{DBFile}", true))
				return;

			Controller.SerializeRecords();
		}
	}
}
