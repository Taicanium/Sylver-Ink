using System;
using System.Windows.Input;

namespace SylverInk.Keyboard;

public class RawKeyEventArgs(int VKCode) : EventArgs
{
	public int Ctrl { get; private set; } = InterceptKeys.GetKeyState(0x11) & 0x8000;
	public Key Key { get; private set; } = KeyInterop.KeyFromVirtualKey(VKCode);
}

public delegate void RawKeyEventHandler(object sender, RawKeyEventArgs args);
