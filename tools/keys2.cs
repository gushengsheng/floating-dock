using System;
using System.Runtime.InteropServices;
using System.Threading;

// Sends Ctrl+Alt+B (the taskbar toggle) as real keystrokes.
internal static class Keys2
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_MENU = 0x12;
    private const byte VK_B = 0x42;

    private static void Main()
    {
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        keybd_event(VK_B, 0, 0, UIntPtr.Zero);
        Thread.Sleep(70);
        keybd_event(VK_B, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Console.WriteLine("ctrl+alt+b sent");
    }
}
