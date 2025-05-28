using SylverInk.Net;
using SylverInk.Notes;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using static SylverInk.Common;

namespace SylverInk;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
	[DllImport("User32.dll")]
	private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("User32.dll")]
	private static extern bool UnregisterHotKey(nint hWnd, int id);

	private bool _ABORT;
	private const int NewNoteHotKeyID = 5911;
	private const int PreviousNoteHotKeyID = 37193;
	private HwndSource? WindowSource;
	private readonly WindowInteropHelper hWndHelper;

	public MainWindow()
	{
		InitializeComponent();
		DataContext = Common.Settings;
		hWndHelper = new WindowInteropHelper(this);
	}

	private void Button_Click(object? sender, RoutedEventArgs e)
	{
		var senderObject = (Button?)sender;

		switch (senderObject?.Content)
		{
			case "Import":
				ImportWindow = new();
				break;
			case "Replace":
				ReplaceWindow = new();
				break;
			case "Search":
				SearchWindow = new();
				break;
			case "Settings":
				SettingsWindow = new();
				break;
			case "Exit":
				Close();
				break;
		}
	}

	private void Drag(object? sender, MouseButtonEventArgs e) => DragMove();

	private void ExitComplete(object? sender, RunWorkerCompletedEventArgs e)
	{
		DatabaseChanged = false;
		MainGrid.IsEnabled = true;
		Common.Settings.Save();
		Application.Current.Shutdown();
	}

	private async void HandleCheckInit()
	{
		WindowSource = HwndSource.FromHwnd(hWndHelper.Handle);
		WindowSource.AddHook(HwndHook);
		RegisterHotKeys();

		HandleShellVerbs();

		await Task.Run(() =>
		{
			do
			{
				InitComplete = DatabaseCount > 0
					&& Databases.Count == DatabaseCount
					&& SettingsLoaded
					&& UpdatesChecked;

				if (Concurrent(() => Application.Current.MainWindow.FindName("DatabasesPanel")) is null)
					InitComplete = false;
			} while (!InitComplete);
		});

		SwitchDatabase($"~N:{LastActiveDatabase}");

		foreach (var openNote in LastActiveNotes)
		{
			var oSplit = openNote.Split(':');
			if (oSplit.Length < 2)
				continue;

			if (!int.TryParse(oSplit[1], out var iNote))
				continue;

			Database? target = null;
			foreach (Database db in Databases)
				if (oSplit[0].Equals(db.Name))
					target = db;

			if (target is null)
				continue;

			if (target.HasRecord(iNote))
			{
				var result = OpenQuery(target.GetRecord(iNote));
				if (result is null)
					continue;

				if (LastActiveNotesHeight.TryGetValue($"{target.Name}:{iNote}", out var openHeight))
					result.Height = openHeight;

				if (LastActiveNotesLeft.TryGetValue($"{target.Name}:{iNote}", out var openLeft))
					result.Left = openLeft;

				if (LastActiveNotesTop.TryGetValue($"{target.Name}:{iNote}", out var openTop))
					result.Top = openTop;

				if (LastActiveNotesWidth.TryGetValue($"{target.Name}:{iNote}", out var openWidth))
					result.Width = openWidth;
			}
		}

		CanResize = true;
		ResizeMode = ResizeMode.CanResize;
		Common.Settings.MainTypeFace = new(Common.Settings.MainFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
		DeferUpdateRecentNotes();
	}

	private void HandleShellVerbs()
	{
		var args = Environment.GetCommandLineArgs();
		if (args.Length < 2)
			return;

		switch (args[1])
		{
			case "open": // &Open
				HandleVerbOpen(args.Length > 2 ? args[2] : string.Empty);
				break;
			default: // &Open
				HandleVerbOpen(args[1]);
				break;
		}
	}

	private void HandleVerbOpen(string filename)
	{
		if (string.IsNullOrWhiteSpace(filename))
			return;

		var wideBreak = string.Empty;
		foreach (string dbFile in Common.Settings.LastDatabases)
			if (Path.GetFullPath(dbFile).Equals(Path.GetFullPath(filename)))
				wideBreak = Path.GetFullPath(dbFile);

		if (string.IsNullOrWhiteSpace(wideBreak))
		{
			Database.Create(filename);
			return;
		}

		SwitchDatabase($"~F:{wideBreak}");
	}

	private nint HwndHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		if (msg != 0x0312) // WM_HOTKEY
			return default;

		if (wParam.ToInt32() == NewNoteHotKeyID)
			OnNewNoteHotkey();

		if (wParam.ToInt32() == PreviousNoteHotKeyID)
			OnPreviousNoteHotkey();

		handled = true;
		return default;
	}

	public static bool IsShuttingDown()
	{
		try
		{
			Application.Current.ShutdownMode = Application.Current.ShutdownMode;
			return false;
		}
		catch
		{
			return true;
		}
	}

	private void MainWindow_Closing(object? sender, CancelEventArgs e)
	{
		if (!_ABORT)
			Common.Settings.Save();

		if (_ABORT || !DatabaseChanged)
		{
			Application.Current.Shutdown();
			return;
		}

		switch (MessageBox.Show("Do you want to save your work before exiting?", "Sylver Ink: Notification", MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
		{
			case MessageBoxResult.Cancel:
				e.Cancel = true;
				return;
			case MessageBoxResult.Yes:
				e.Cancel = true;
				MainGrid.IsEnabled = false;

				foreach (Database db in Databases)
					Erase(GetLockFile(db.DBFile));

				BackgroundWorker exitTask = new();
				exitTask.DoWork += (_, _) => SaveDatabases();
				exitTask.RunWorkerCompleted += ExitComplete;
				exitTask.RunWorkerAsync();
				return;
			case MessageBoxResult.No:
				foreach (Database db in Databases)
					Erase(GetLockFile(db.DBFile));

				Application.Current.Shutdown();
				return;
		}
	}

	private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e) => DeferUpdateRecentNotes();

	private void NewNote_Keydown(object? sender, KeyEventArgs e)
	{
		if (e.Key != Key.Enter)
			return;

		var box = (TextBox?)sender;
		if (box is null)
			return;

		CurrentDatabase.CreateRecord(box.Text);
		box.Text = string.Empty;
		DeferUpdateRecentNotes();
	}

	protected override void OnClosed(EventArgs e)
	{
		WindowSource?.RemoveHook(HwndHook);
		WindowSource = null;
		UnregisterHotKeys();
		base.OnClosed(e);
	}

	private static void OnNewNoteHotkey()
	{
		var firstRecord = CurrentDatabase.GetRecord(0);
		var lastRecord = CurrentDatabase.GetRecord(CurrentDatabase.RecordCount - 1);

		if (string.IsNullOrEmpty(firstRecord.ToString()))
		{
			OpenQuery(firstRecord);
			return;
		}

		if (string.IsNullOrEmpty(lastRecord.ToString()))
		{
			OpenQuery(lastRecord);
			return;
		}

		OpenQuery(CurrentDatabase.GetRecord(CurrentDatabase.CreateRecord(string.Empty)));
	}

	private static void OnPreviousNoteHotkey() => OpenQuery(PreviousOpenNote ?? CurrentDatabase.GetRecord(CurrentDatabase.CreateRecord(string.Empty)));

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);

		if (Process.GetProcessesByName("Sylver Ink").Length > 1 && !File.Exists(UpdateHandler.UpdateLockUri))
		{
			_ABORT = true;
			MessageBox.Show("Another instance of Sylver Ink is already running.", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
			Application.Current.Shutdown();
			return;
		}

		Erase(UpdateHandler.UpdateLockUri);
		Erase(UpdateHandler.TempUri);

		Common.Settings.Load();
		SettingsLoaded = true;

		HandleCheckInit();

		foreach (var folder in Subfolders)
			if (!Directory.Exists(folder.Value))
				Directory.CreateDirectory(folder.Value);

		UpdateHandler.CheckForUpdates();

		if (!IsShuttingDown())
			UpdatesChecked = true;

		if (FirstRun)
		{
			DatabaseCount = 1;
			Database.Create(Path.Join(Subfolders["Databases"], DefaultDatabase, $"{DefaultDatabase}.sidb"));
		}
	}

	private void RegisterHotKeys()
	{
		RegisterHotKey(hWndHelper.Handle, NewNoteHotKeyID, 2, (uint)KeyInterop.VirtualKeyFromKey(Key.N));
		RegisterHotKey(hWndHelper.Handle, PreviousNoteHotKeyID, 2, (uint)KeyInterop.VirtualKeyFromKey(Key.L));
	}

	private void UnregisterHotKeys()
	{
		UnregisterHotKey(hWndHelper.Handle, NewNoteHotKeyID);
		UnregisterHotKey(hWndHelper.Handle, PreviousNoteHotKeyID);
	}
}
