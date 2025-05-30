using SylverInk.Notes;
using System;
using System.Windows;
using System.Windows.Input;
using static SylverInk.Common;

namespace SylverInk;

public static class PopupUtils
{
	public static void PopupAddressKeyDown(this MainWindow window, object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
			window.ConnectAddress.IsOpen = false;
	}

	public static void PopupCodeClosed(this MainWindow window, object? sender, EventArgs e)
	{
		Clipboard.SetText(CurrentDatabase.Server?.AddressCode);
		window.CodePopup.IsOpen = false;
	}

	public static void PopupRenameClosed(this MainWindow window, object? sender, EventArgs e)
	{
		if (!window.RenameDatabase.IsOpen)
			return;

		window.RenameDatabase.IsOpen = false;

		if (string.IsNullOrWhiteSpace(window.DatabaseNameBox.Text))
			return;

		if (window.DatabaseNameBox.Text.Equals(CurrentDatabase.Name))
			return;

		foreach (Database db in Databases)
		{
			if (!window.DatabaseNameBox.Text.Equals(db.Name))
				continue;

			MessageBox.Show("A database already exists with the provided name.", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
			return;
		}

		foreach (char pc in InvalidPathChars)
		{
			if (!window.DatabaseNameBox.Text.Contains(pc))
				continue;

			MessageBox.Show($"Provided name contains invalid character: {pc}", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
			return;
		}

		CurrentDatabase.Rename(window.DatabaseNameBox.Text);
	}

	public static void PopupRenameKeyDown(this MainWindow window, object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
			window.RenameDatabase.IsOpen = false;
	}

	public static async void PopupSaveAddress(this MainWindow window, object? sender, RoutedEventArgs e)
	{
		window.ConnectAddress.IsOpen = false;

		Database newDB = new();
		AddDatabase(newDB);

		var addr = window.AddressBox.Text;
		await newDB.Client.Connect(addr);
	}
}
