using System;
using System.Runtime.InteropServices;
using System.Threading;

// Sends Win+` (the dock hotkey) so we can verify the hotkey path end to end.
internal static class Keys
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_LWIN = 0x5B;
    private const byte VK_OEM_3 = 0xC0; // `~

    private static void Main(string[] args)
    {
        bool down = args.Length == 0 || args[0] != "up";
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_OEM_3, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        keybd_event(VK_OEM_3, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Console.WriteLine("hotkey sent");
    }
}
