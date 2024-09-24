using System.IO;

namespace SylverInk
{
	public partial class Database
	{
		public bool Changed => Controller.Changed;
		public NoteController Controller { get; set; } = new();
		public string DBFile = string.Empty;
		public bool Loaded = false;
		public string? Name { get => Controller.Name; set => Controller.Name = value; }

		public Database()
		{
			Controller = new();
			Loaded = Controller.Loaded;
		}

		public Database(string dbFile)
		{
			DBFile = Path.GetFullPath(dbFile);
			Controller = new(DBFile);
			Loaded = Controller.Loaded;
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
