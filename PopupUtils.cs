using SylverInk.Notes;
using System;
using System.Windows;
using System.Windows.Input;
using static SylverInk.Common;

namespace SylverInk
{
	public static class PopupUtils
	{
		public static void Popup_AddressKeyDown(this MainWindow window, object? sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
				window.ConnectAddress.IsOpen = false;
		}

		public static void Popup_CodeClosed(this MainWindow window, object? sender, EventArgs e) => Clipboard.SetText(window.CodeBox.Text);

		public static void Popup_CopyCode(this MainWindow window, object? sender, RoutedEventArgs e) => window.CodePopup.IsOpen = false;

		public static void Popup_RenameClosed(this MainWindow window, object? sender, EventArgs e)
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
				if (window.DatabaseNameBox.Text.Contains(pc))
				{
					MessageBox.Show($"Provided name contains invalid character: {pc}", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
					return;
				}
			}

			CurrentDatabase.Rename(window.DatabaseNameBox.Text);
		}

		public static void Popup_RenameKeyDown(this MainWindow window, object? sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
				window.RenameDatabase.IsOpen = false;
		}

		public static async void Popup_SaveAddress(this MainWindow window, object? sender, RoutedEventArgs e)
		{
			window.ConnectAddress.IsOpen = false;

			Database newDB = new();
			AddDatabase(newDB);

			var addr = window.AddressBox.Text;
			await newDB.Client.Connect(addr);
		}
	}
}
