using System;
using System.Runtime.InteropServices;
using System.Threading;

// Sends one virtual key with optional modifiers: vkey <name> [ctrl] [shift] [alt]
internal static class VKey
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint UP = 0x0002;

    private static void Main(string[] a)
    {
        if (a.Length == 0)
        {
            Console.WriteLine("usage: vkey <up|down|left|right|enter|esc|tab|f2|home|end|backspace|space|a-z|0-9> [ctrl] [shift] [alt]");
            return;
        }
        int vk = Map(a[0]);
        if (vk == 0) { Console.WriteLine("unknown key: " + a[0]); return; }

        bool ctrl = false, shift = false, alt = false;
        for (int i = 1; i < a.Length; i++)
        {
            string m = a[i].ToLowerInvariant();
            if (m == "ctrl") ctrl = true;
            else if (m == "shift") shift = true;
            else if (m == "alt") alt = true;
        }

        if (ctrl) keybd_event(0x11, 0, 0, UIntPtr.Zero);
        if (shift) keybd_event(0x10, 0, 0, UIntPtr.Zero);
        if (alt) keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        keybd_event((byte)vk, 0, UP, UIntPtr.Zero);
        if (alt) keybd_event(0x12, 0, UP, UIntPtr.Zero);
        if (shift) keybd_event(0x10, 0, UP, UIntPtr.Zero);
        if (ctrl) keybd_event(0x11, 0, UP, UIntPtr.Zero);

        Console.WriteLine("sent " + a[0]);
    }

    private static int Map(string s)
    {
        string k = s.ToLowerInvariant();
        if (k.Length == 1)
        {
            char c = char.ToUpperInvariant(k[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return c;
        }
        switch (k)
        {
            case "up": return 0x26;
            case "down": return 0x28;
            case "left": return 0x25;
            case "right": return 0x27;
            case "enter": return 0x0D;
            case "esc": return 0x1B;
            case "tab": return 0x09;
            case "backspace": return 0x08;
            case "space": return 0x20;
            case "home": return 0x24;
            case "end": return 0x23;
            case "delete": return 0x2E;
            case "insert": return 0x2D;
            case "f2": return 0x71;
            case "f3": return 0x72;
        }
        return 0;
    }
}
