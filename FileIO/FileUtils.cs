using Microsoft.Win32;
using SylverInk.Notes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using static SylverInk.CommonUtils;
using static SylverInk.Notes.DatabaseUtils;

namespace SylverInk.FileIO;

/// <summary>
/// Static functions serving specific needs in regards to file access.
/// </summary>
public static class FileUtils
{
	public static string DocumentsFolder { get; } = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sylver Ink");
	public static string SettingsFile { get; } = Path.Join(DocumentsFolder, "settings.sis");
	public static int HighestSIDBFormat { get; } = 12;
	public static char[] InvalidPathChars { get; } = ['/', '\\', ':', '*', '"', '?', '<', '>', '|'];
	public static Dictionary<string, string> Subfolders { get; } = new([
		new("Databases", Path.Join(DocumentsFolder, "Databases"))
		]);

	public static string DialogFileSelect(bool outgoing = false, int filterIndex = 3, string? defaultName = null)
	{
		FileDialog dialog = outgoing ? new SaveFileDialog()
		{
			FileName = defaultName ?? DefaultDatabase,
			Filter = "Sylver Ink backup files (*.sibk)|*.sibk|Sylver Ink database files (*.sidb)|*.sidb|All files (*.*)|*.*",
		} : new OpenFileDialog()
		{
			CheckFileExists = true,
			Filter = "Sylver Ink backup files (*.sibk)|*.sibk|Sylver Ink database files (*.sidb)|*.sidb|Text files (*.txt)|*.txt|All files (*.*)|*.*",
			InitialDirectory = Subfolders["Databases"],
		};

		dialog.FilterIndex = filterIndex;
		dialog.ValidateNames = true;

		return dialog.ShowDialog() is true ? dialog.FileName : string.Empty;
	}

	/// <summary>
	/// Deletes a file if it exists.
	/// </summary>
	/// <param name="filename">The file to be deleted.</param>
	/// <returns><c>true</c> if the file existed and was deleted; else, <c>false</c>.</returns>
	public static bool Erase(string filename)
	{
		if (!File.Exists(filename))
			return false;

		File.Delete(filename);
		return true;
	}

	public static string GetBackupPath(Database db) => Path.Join(Subfolders["Databases"], db.Name, db.Name);

	public static string GetDatabasePath(Database db)
	{
		var index = 0;
		Match match;
		if ((match = IndexDigits().Match(db.Name ?? string.Empty)).Success)
			index = int.Parse(match.Groups[1].Value, NumberFormatInfo.InvariantInfo);

		var path = Path.Join(Subfolders["Databases"], db.Name);
		var dbFile = Path.Join(path, $"{db.Name}.sidb");
		var uuidFile = Path.Join(path, "uuid.dat");

		while (File.Exists(dbFile))
		{
			if (File.Exists(uuidFile) && File.ReadAllText(uuidFile).Equals(db.UUID))
				return dbFile;

			if (!File.Exists(uuidFile))
			{
				Database tmpDB = new();
				try
				{
					tmpDB.Load(dbFile);
					if (tmpDB.UUID?.Equals(db.UUID) is true)
						return dbFile;
				}
				catch
				{
					return string.Empty;
				}
			}

			index++;
			db.Name = $"{db.Name} ({index})";
			dbFile = Path.Join(path, $"{db.Name}.sidb");
			uuidFile = Path.Join(path, "uuid.dat");
		}

		return dbFile;
	}

	public static string GetLockFile(string? dbFile = null) => Path.Join(Path.GetDirectoryName(dbFile ?? CurrentDatabase.DBFile) ?? ".", "~lock.sidb");
}
