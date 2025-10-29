using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SylverInk.Keyboard;

/// <summary>
/// Logic for handling keyboard input interception.
/// </summary>
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
