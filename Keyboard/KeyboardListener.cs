using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace SylverInk.Keyboard;

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
