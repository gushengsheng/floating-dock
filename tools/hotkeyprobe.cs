using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Tries to register a list of hotkey combinations and reports which ones are free.
internal static class HotkeyProbe
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int mods, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;

    private static void Main()
    {
        object[][] tests = new object[][]
        {
            new object[] { "Win+`", MOD_WIN, 0xC0 },
            new object[] { "Ctrl+Alt+T", MOD_CONTROL | MOD_ALT, 0x54 },
            new object[] { "Ctrl+Alt+D", MOD_CONTROL | MOD_ALT, 0x44 },
            new object[] { "Ctrl+Alt+W", MOD_CONTROL | MOD_ALT, 0x57 },
            new object[] { "Ctrl+Alt+Q", MOD_CONTROL | MOD_ALT, 0x51 },
            new object[] { "Ctrl+Alt+Z", MOD_CONTROL | MOD_ALT, 0x5A },
            new object[] { "Ctrl+Alt+Space", MOD_CONTROL | MOD_ALT, 0x20 },
            new object[] { "Ctrl+Shift+`", MOD_CONTROL | MOD_SHIFT, 0xC0 },
            new object[] { "Alt+`", MOD_ALT, 0xC0 },
            new object[] { "Win+Space", MOD_WIN, 0x20 },
            new object[] { "Win+T", MOD_WIN, 0x54 },
            new object[] { "Ctrl+Alt+H", MOD_CONTROL | MOD_ALT, 0x48 },
            new object[] { "Ctrl+Alt+B", MOD_CONTROL | MOD_ALT, 0x42 },
            new object[] { "Ctrl+Alt+Shift+T", MOD_CONTROL | MOD_ALT | MOD_SHIFT, 0x54 }
        };

        for (int i = 0; i < tests.Length; i++)
        {
            string name = (string)tests[i][0];
            int mods = (int)tests[i][1];
            int vk = (int)tests[i][2];
            bool ok = RegisterHotKey(IntPtr.Zero, 100 + i, mods, vk);
            Console.WriteLine((ok ? "FREE      " : "TAKEN/OFF ") + "  " + name);
            if (ok) UnregisterHotKey(IntPtr.Zero, 100 + i);
        }
    }
}
