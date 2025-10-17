using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;

namespace SylverInk;

/// <summary>
/// Logic for handling non-global hotkey registration.
/// </summary>
public class KeyboardListener : IDisposable
{
	[StructLayout(LayoutKind.Sequential)]
	public struct MSG
	{
		public nint hwnd;
		public uint message;
		public nint wParam;
		public nint lParam;
		public uint time;
		public System.Drawing.Point pt;
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PeekMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

	private readonly CancellationTokenSource hookTokenSource = new();
	private static nint hookId = nint.Zero;
	private readonly Thread? hookThread;
	public event RawKeyEventHandler? KeyDown;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private nint HookCallback(int nCode, nint wParam, nint lParam)
	{
		try
		{
			if (nCode >= 0 && wParam == InterceptKeys.WM_KEYDOWN)
			{
				int vkCode = Marshal.ReadInt32(lParam);
				KeyDown?.Invoke(this, new RawKeyEventArgs(vkCode));
			}
		}
		catch { }

		return InterceptKeys.CallNextHookEx(hookId, nCode, wParam, lParam);
	}

	public KeyboardListener()
	{
		hookThread = new(() => InstallHook(hookTokenSource.Token));
		hookThread.SetApartmentState(ApartmentState.STA);
		hookThread.Start();
	}

	public void Dispose()
	{
		hookTokenSource.Cancel();
		InterceptKeys.UnhookWindowsHookEx(hookId);
		GC.SuppressFinalize(this);
	}

	private void InstallHook(CancellationToken token)
	{
		hookId = InterceptKeys.SetHook(HookCallback);

		while (!token.IsCancellationRequested)
		{
			PeekMessage(out MSG msg, nint.Zero, 0, 0, 0x1);

			//GetMessage(out msg, nint.Zero, 0, 0);
			Thread.Sleep(15);
		}
	}
}

internal static class InterceptKeys
{
	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

	[DllImport("user32.dll")]
	public static extern short GetKeyState(int nVirtKey);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	public static extern nint GetModuleHandle(string lpModuleName);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	public static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool UnhookWindowsHookEx(nint hhk);

	public delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);
	private readonly static int WH_KEYBOARD_LL = 13;
	public readonly static int WM_KEYDOWN = 0x0100;

	public static nint SetHook(LowLevelKeyboardProc proc)
	{
		using Process thisProcess = Process.GetCurrentProcess();
		using ProcessModule? thisModule = thisProcess.MainModule;

		if (thisModule?.ModuleName is null)
			return nint.Zero;

		return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(thisModule.ModuleName), 0);
	}
}

public class RawKeyEventArgs(int VKCode) : EventArgs
{
	public int Ctrl { get; private set; } = InterceptKeys.GetKeyState(0x11) & 0x8000;
	public Key Key { get; private set; } = KeyInterop.KeyFromVirtualKey(VKCode);
}

public delegate void RawKeyEventHandler(object sender, RawKeyEventArgs args);