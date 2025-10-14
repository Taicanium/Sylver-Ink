using SylverInk.Net;
using SylverInk.Notes;
using SylverInk.XAML;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using static SylverInk.CommonUtils;
using static SylverInk.FileIO.FileUtils;
using static SylverInk.Notes.DatabaseUtils;
using static SylverInk.XAMLUtils.DataUtils;

namespace SylverInk;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, IDisposable
{
	[DllImport("User32.dll")]
	private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("User32.dll")]
	private static extern bool UnregisterHotKey(nint hWnd, int id);

	private KeyboardListener? hotkeyListener;
	private readonly WindowInteropHelper hWndHelper;
	private Mutex? mutex;
	private readonly CancellationTokenSource mutexTokenSource = new();
	private readonly string MutexName = $"SylverInk/{typeof(MainWindow).GUID}";
	private const int NewNoteHotKeyID = 5911;
	private const int PreviousNoteHotKeyID = 37193;
	private bool ShellVerbsPassed;
	private HwndSource? WindowSource;

	public MainWindow()
	{
		InitializeComponent();
		DataContext = CommonUtils.Settings;

		hWndHelper = new WindowInteropHelper(this);
		mutex = new Mutex(true, MutexName, out bool mutexCreated);

		HandleMutex(mutexCreated);
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

	public void Dispose()
	{
		WindowSource?.Dispose();
		mutex?.Dispose();
		GC.SuppressFinalize(this);
	}

	private void Drag(object? sender, MouseButtonEventArgs e) => DragMove();

	private async void HandleCheckInit()
	{
		using var tokenSource = new CancellationTokenSource();
		var token = tokenSource.Token;

		var initTask = Task.Run(() =>
		{
			do
			{
				InitComplete = Databases.Count > 0
					&& SettingsLoaded
					&& UpdatesChecked;

				if (Concurrent(() => Application.Current.MainWindow.FindName("DatabasesPanel")) is null)
					InitComplete = false;
			} while (!InitComplete && !token.IsCancellationRequested);
		}, token);

		await initTask;

		if (string.IsNullOrEmpty(ShellDB))
			SwitchDatabase($"~N:{CommonUtils.Settings.LastActiveDatabase}");
		else
			SwitchDatabase($"~F:{ShellDB}");

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

			if (!target.HasRecord(iNote))
				continue;

			if (OpenQuery(target.GetRecord(iNote)) is not SearchResult result)
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

		CanResize = true;
		LastActiveNotes.Clear();
		LastActiveNotesHeight.Clear();
		LastActiveNotesLeft.Clear();
		LastActiveNotesTop.Clear();
		LastActiveNotesWidth.Clear();
		ResizeMode = ResizeMode.CanResize;
		CommonUtils.Settings.MainTypeFace = new(CommonUtils.Settings.MainFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
		DeferUpdateRecentNotes();
	}

	/// <summary>
	/// Mutex management in Sylver Ink allows passing shell verbs through a named pipe to an existing open instance.
	/// </summary>
	private void HandleMutex(bool mutexCreated)
	{
		if (!mutexCreated)
		{
			mutex = null;
			var args = Environment.GetCommandLineArgs();

			var client = new NamedPipeClientStream(MutexName);
			client.Connect();

			using (StreamWriter writer = new(client))
				writer.Write(string.Join("\t", args));

			if (args.Length > 1)
				ShellVerbsPassed = true;

			return;
		}

		Thread mutexPipeThread = new(() => HandleMutexPipe(mutexTokenSource.Token));
		mutexPipeThread.Start();
	}

	private async void HandleMutexPipe(CancellationToken token)
	{
		while (mutex != null)
		{
			using var server = new NamedPipeServerStream(MutexName);

			try
			{
				await server.WaitForConnectionAsync(token);
			}
			catch
			{
				return;
			}

			if (token.IsCancellationRequested)
				return;

			using StreamReader reader = new(server);
			string[] args = [.. reader.ReadToEnd().Split("\t", StringSplitOptions.RemoveEmptyEntries)];
			bool activated;
			var now = DateTime.UtcNow;

			do
			{
				activated = Concurrent(Activate);
				Concurrent(Focus);
			} while (!activated && !IsFocused && (DateTime.UtcNow - now).Seconds < 2);

			Concurrent(() => HandleShellVerbs(args));
		}
	}

	private static void HandleShellVerbs(string[]? args = null)
	{
		if ((args ??= Environment.GetCommandLineArgs()).Length < 2)
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

	private async static void HandleVerbOpen(string filename)
	{
		if (string.IsNullOrWhiteSpace(filename))
			return;

		var wideBreak = string.Empty;

		foreach (string dbFile in InitComplete ? Databases.Select(db => db.DBFile) : CommonUtils.Settings.LastDatabases)
			if (Path.GetFullPath(dbFile).Equals(Path.GetFullPath(filename)))
				wideBreak = Path.GetFullPath(dbFile);

		if (string.IsNullOrWhiteSpace(wideBreak))
		{
			ShellDB = Path.GetFullPath(filename);
			await Database.Create(filename);
			return;
		}

		ShellDB = wideBreak;
		SwitchDatabase($"~F:{wideBreak}");
	}

	private nint HwndHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		switch (msg)
		{
			case 0x0312: // WM_HOTKEY
				switch (wParam.ToInt32())
				{
					case NewNoteHotKeyID:
						OnNewNoteHotkey();
						break;
					case PreviousNoteHotKeyID:
						OnPreviousNoteHotkey();
						break;
				}
				break;
			default:
				return default;
		}

		handled = true;
		return default;
	}

	private static bool InstanceRunning() => Process.GetProcessesByName("Sylver Ink").Length > 1 && !File.Exists(UpdateHandler.UpdateLockUri);

	private static bool IsShuttingDown()
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

	private async void MainWindow_Closing(object? sender, CancelEventArgs e)
	{
		if (IsShuttingDown()) // Prevent redundant event-handling.
			return;

		if (AbortRun)
		{
			Application.Current.Shutdown();
			return;
		}

		CommonUtils.Settings.Save();

		if (!DatabaseChanged)
		{
			switch (MessageBox.Show("Are you sure you wish to exit Sylver Ink?", "Sykver Ink: Notification", MessageBoxButton.YesNo, MessageBoxImage.Question))
			{
				case MessageBoxResult.No:
					e.Cancel = true;
					return;
				case MessageBoxResult.Yes:
					Application.Current.Shutdown();
					return;
			}
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

				await SaveDatabases();

				DatabaseChanged = false;
				CommonUtils.Settings.Save();
				Application.Current.Shutdown();
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

		if (sender is not TextBox box)
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
		mutexTokenSource.Cancel();
		base.OnClosed(e);
	}

	public void OnHotkey(object sender, RawKeyEventArgs e)
	{
		if (e.Ctrl == 0)
			return;

		switch (e.Key)
		{
			case Key.F:
				Concurrent(OnNoteSearchHotkey);
				break;
		}
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

	private static void OnNoteSearchHotkey()
	{
		var tabPanel = GetChildPanel("DatabasesPanel");
		var openTab = (TabItem)tabPanel.SelectedItem;

		if (openTab.Content is not NoteTab noteTab)
			return;

		noteTab.InternalSearchPopup.IsOpen = true;
		noteTab.ISPText.Focus();
	}

	private static void OnPreviousNoteHotkey() => OpenQuery(PreviousOpenNote ?? CurrentDatabase.GetRecord(CurrentDatabase.CreateRecord(string.Empty)));

	protected override async void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);

		WindowSource = HwndSource.FromHwnd(hWndHelper.Handle);
		WindowSource.AddHook(HwndHook);
		RegisterHotKeys();

		HandleCheckInit();
		HandleShellVerbs();

		if (InstanceRunning())
		{
			if (!ShellVerbsPassed)
				MessageBox.Show("Another instance of Sylver Ink is already running.", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);

			// Otherwise, close the program silently before a head is established.
			AbortRun = true;
			Close();
			return;
		}

		Erase(UpdateHandler.UpdateLockUri);
		Erase(UpdateHandler.TempUri);

		await CommonUtils.Settings.Load();
		SettingsLoaded = true;

		foreach (var folder in Subfolders)
			if (!Directory.Exists(folder.Value))
				Directory.CreateDirectory(folder.Value);

		await UpdateHandler.CheckForUpdates();

		if (!IsShuttingDown())
			UpdatesChecked = true;

		if (!FirstRun)
			return;

		await Database.Create(Path.Join(Subfolders["Databases"], DefaultDatabase, $"{DefaultDatabase}.sidb"));
	}

	private void RegisterHotKeys()
	{
		hotkeyListener = new();
		hotkeyListener.KeyDown += new RawKeyEventHandler(OnHotkey);

		RegisterHotKey(hWndHelper.Handle, NewNoteHotKeyID, 2, (uint)KeyInterop.VirtualKeyFromKey(Key.N));
		RegisterHotKey(hWndHelper.Handle, PreviousNoteHotKeyID, 2, (uint)KeyInterop.VirtualKeyFromKey(Key.L));
	}

	private void UnregisterHotKeys()
	{
		UnregisterHotKey(hWndHelper.Handle, NewNoteHotKeyID);
		UnregisterHotKey(hWndHelper.Handle, PreviousNoteHotKeyID);

		hotkeyListener?.Dispose();
	}
}
