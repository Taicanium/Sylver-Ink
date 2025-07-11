﻿using SylverInk.Net;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static SylverInk.CommonUtils;
using static SylverInk.FileIO.FileUtils;
using static SylverInk.Notes.DatabaseUtils;
using static SylverInk.XAMLUtils.DataUtils;

namespace SylverInk.Notes;

public partial class Database : IDisposable
{
	private NoteController Controller = new();
	private StackPanel? HeaderPanel;

	public bool Changed { get => Controller.Changed; set => Controller.Changed = value; }
	public NetClient Client { get; private set; }
	public long? Created { get; private set; }
	public string DBFile { get; set; } = string.Empty;
	public int Format { get => Controller.Format; set => Controller.Format = value; }
	public bool Loaded { get; private set; }
	public string? Name { get => Controller.Name; set => Controller.Name = value; }
	public int RecordCount => Controller.RecordCount;
	public NetServer Server { get; private set; }
	public string UUID { get => Controller.UUID; set => Controller.UUID = value; }
	public Dictionary<string, double> WordPercentages => Controller.WordPercentages;

	public Database()
	{
		Client = new(this);
		Controller = new();
		Loaded = Controller.Loaded;
		Server = new(this);
	}

	public Database(string DBFile)
	{
		Client = new(this);
		Controller = new();
		this.DBFile = DBFile;
		Loaded = Controller.Loaded;
		Server = new(this);
	}

	public static async Task Create(string dbFile)
	{
		Database? db = null;
		try
		{
			db = new(dbFile);
			AddDatabase(db);
			await Task.Run(db.Load);
		}
		catch
		{
			if (db is not null)
				RemoveDatabase(db);

			MessageBox.Show($"Could not load database: {dbFile}", "Sylver Ink: Error", MessageBoxButton.OK);
		}
	}

	public int CreateRecord(string entry, bool local = true)
	{
		int index = Controller.CreateRecord(entry);

		if (local)
		{
			var outBuffer = new List<byte>([0, 0, 0, 0, .. IntToBytes(entry.Length)]);

			if (entry.Length > 0)
				outBuffer.AddRange(Encoding.UTF8.GetBytes(entry));

			Transmit(NetworkUtils.MessageType.RecordAdd, [.. outBuffer]);
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

		Transmit(NetworkUtils.MessageType.TextInsert, [.. outBuffer]);
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

		Transmit(NetworkUtils.MessageType.TextInsert, [.. outBuffer]);
	}

	public void DeleteRecord(int index, bool local = true)
	{
		Controller.DeleteRecord(index);

		if (local)
			Transmit(NetworkUtils.MessageType.RecordRemove, IntToBytes(index));
	}

	public void DeleteRecord(NoteRecord record, bool local = true)
	{
		for (int index = RecordCount - 1; index > -1; index--)
		{
			if (!Controller.GetRecord(index).Equals(record))
				continue;

			Controller.DeleteRecord(index);

			if (local)
				Transmit(NetworkUtils.MessageType.RecordRemove, IntToBytes(index));
		}
	}

	public void DeserializeRecords(List<byte>? inMemory = null) => Controller.DeserializeRecords(inMemory);

	public void Dispose()
	{
		Controller.Dispose();
		GC.SuppressFinalize(this);
	}

	public override bool Equals(object? obj)
	{
		if (obj is Database otherDB)
		{
			if (!otherDB.Name?.Equals(Name) is true)
				return false;

			if (!otherDB.UUID.Equals(UUID))
				return false;

			return true;
		}

		if (obj is NoteController otherController)
		{
			if (!otherController.Name?.Equals(Name) is true)
				return false;

			if (!otherController.UUID.Equals(UUID))
				return false;

			return true;
		}

		return false;
	}

	public void Erase()
	{
		if (Client.Connected || Server.Serving)
			return;

		Controller.EraseDatabase();
	}

	public string GetCreated()
	{
		if (Created is not null)
			return DateTime.FromBinary((long)Created).ToString(DateFormat, CultureInfo.InvariantCulture);

		var CreatedObject = DateTime.UtcNow;
		for (int i = 0; i < RecordCount; i++)
		{
			var other = Controller.GetRecord(i).GetCreatedObject();
			if (CreatedObject.CompareTo(other) > 0)
				CreatedObject = other;
		}

		Created = CreatedObject.ToBinary();
		return CreatedObject.ToString(DateFormat, CultureInfo.InvariantCulture);
	}

	public override int GetHashCode() => int.Parse(UUID.Replace("-", string.Empty)[^8..], NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo);

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

				HeaderPanel.ToolTip = Name;
				label.Content = headerContent;
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
		HeaderPanel.Children.Add((Client.Active ? Client.Indicator : Server.Indicator) ?? new System.Windows.Shapes.Ellipse());

		return HeaderPanel;
	}

	public NoteRecord GetRecord(int index) => Controller.GetRecord(index);

	public bool HasRecord(int index) => Controller.HasRecord(index);

	public void Initialize(bool newDatabase = true) => Controller.InitializeRecords(newDatabase);

	public bool Load()
	{
		if (string.IsNullOrWhiteSpace(DBFile))
			return false;

		Load(DBFile);
		Concurrent(GetHeader);
		return true;
	}

	public void Load(string dbFile)
	{
		var lockFile = GetLockFile(dbFile);
		if (File.Exists(lockFile) && MessageBox.Show($"{Path.GetFileName(dbFile)} - The database last closed unexpectedly. Do you want to load the most recent autosave?", "Sylver Ink: Notification", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
		{
			Controller.Open(lockFile);
			Initialize();

			if (Controller.EnforceNoForwardCompatibility)
			{
				Loaded = false;
				throw new NotSupportedException($"The program attempted to load a .sidb file with a newer format than it supports.");
			}

			Loaded = Controller.Loaded = true;
			Changed = true;

			if (string.IsNullOrWhiteSpace(Name))
				Name = Path.GetFileNameWithoutExtension(DBFile);

			DeferUpdateRecentNotes();

			return;
		}

		Controller = new(DBFile = dbFile);

		if (Controller.EnforceNoForwardCompatibility)
		{
			Loaded = false;
			throw new NotSupportedException($"The program attempted to load a .sidb file with a newer format than it supports.");
		}

		Loaded = Controller.Loaded;

		if (string.IsNullOrWhiteSpace(Name))
			Name = Path.GetFileNameWithoutExtension(DBFile);

		if (DBFile.EndsWith("sibk"))
			Name = $"Backup: {Name}";

		DeferUpdateRecentNotes();
	}

	public void Lock(int index, bool local = false)
	{
		var record = Controller.GetRecord(index);

		if (local)
		{
			Transmit(NetworkUtils.MessageType.RecordLock, [.. IntToBytes(index)]);
			return;
		}

		record.Lock();
	}

	public void MakeBackup(bool auto = false)
	{
		var DBPath = GetDatabasePath(this);
		var BKPath = GetBackupPath(this);

		if (!auto)
		{
			Controller.MakeBackup();
			return;
		}

		for (int i = 2; i > 0; i--)
		{
			if (File.Exists($"{BKPath}_{i}.sibk"))
				File.Copy($"{BKPath}_{i}.sibk", $"{BKPath}_{i + 1}.sibk", true);
		}

		if (File.Exists($"{DBPath}"))
			File.Copy($"{DBPath}", $"{BKPath}_1.sibk", true);
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

		DBFile = GetDatabasePath(this);
		var newFile = DBFile;
		var newPath = Path.GetDirectoryName(newFile);

		GetHeader();

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
					GetHeader();
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
				GetHeader();
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

			Transmit(NetworkUtils.MessageType.RecordReplace, [.. outBuffer]);
		}

		return Controller.Replace(oldText, newText);
	}

	public void Revert(DateTime targetDate) => Controller.Revert(targetDate);

	public void Save()
	{
		if (!Changed)
			return;

		if (string.IsNullOrWhiteSpace(DBFile))
			DBFile = GetDatabasePath(this);

		if (string.IsNullOrWhiteSpace(UUID))
			UUID = MakeUUID(UUIDType.Database);

		MakeBackup(true);

		if (!Directory.Exists(Path.GetDirectoryName(DBFile)))
			Directory.CreateDirectory(Path.GetDirectoryName(DBFile) ?? DBFile);

		if (!Controller.Open($"{DBFile}", true))
			return;

		Controller.SerializeRecords();

		if (DBFile.Contains(Subfolders["Databases"]))
			File.WriteAllText(Path.Join(Path.GetDirectoryName(DBFile), "uuid.dat"), UUID);

		SylverInk.FileIO.FileUtils.Erase(GetLockFile(DBFile));
	}

	public void Save(string targetFile)
	{
		if (string.IsNullOrWhiteSpace(targetFile))
			targetFile = DBFile;

		if (string.IsNullOrWhiteSpace(UUID))
			UUID = MakeUUID(UUIDType.Database);

		if (!Directory.Exists(Path.GetDirectoryName(targetFile)))
			Directory.CreateDirectory(Path.GetDirectoryName(targetFile) ?? targetFile);

		File.Create(targetFile, 1).Dispose();

		if (!Controller.Open($"{targetFile}", true))
			return;

		Controller.SerializeRecords();

		File.SetAttributes(targetFile, File.GetAttributes(targetFile) | FileAttributes.Hidden);
		Changed = true;
	}

	public byte[]? SerializeRecords(bool inMemory = false) => Controller.SerializeRecords(inMemory);

	public void Sort(SortType type = SortType.ByIndex)
	{
		if (type == SortType.ByIndex)
			Controller.PropagateIndices();
		Controller.Sort(type);
	}

	public void Transmit(NetworkUtils.MessageType type, byte[] data)
	{
		if (Client.Connected)
			Client.Send(type, data);

		if (Server.Serving)
			Server.Broadcast(type, data);
	}

	public void Unlock(int index, bool local = false)
	{
		if (index == -1)
			return;

		if (local)
		{
			Transmit(NetworkUtils.MessageType.RecordUnlock, [.. IntToBytes(index)]);
			return;
		}

		Controller.GetRecord(index).Unlock();
	}

	public void UpdateWordPercentages() => Controller.UpdateWordPercentages();
}
