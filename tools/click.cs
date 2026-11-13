using System;
using System.Runtime.InteropServices;
using System.Threading;

// Moves the cursor to physical screen coordinates (this helper is DPI aware) and clicks.
internal static class Click
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private const uint LEFTDOWN = 0x0002;
    private const uint LEFTUP = 0x0004;
    private const uint RIGHTDOWN = 0x0008;
    private const uint RIGHTUP = 0x0010;

    private static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: click.exe <x> <y> [right] [double]");
            return;
        }
        int x = int.Parse(args[0]);
        int y = int.Parse(args[1]);
        bool right = args.Length > 2 && args[2] == "right";
        bool dbl = args.Length > 3 && args[3] == "double";

        SetCursorPos(x, y);
        Thread.Sleep(120);
        if (right)
        {
            mouse_event(RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        }
        else
        {
            mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);
            if (dbl)
            {
                Thread.Sleep(80);
                mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);
            }
        }
        Console.WriteLine(string.Format("clicked {0},{1} right={2} (screen {3}x{4})", x, y, right,
            GetSystemMetrics(0), GetSystemMetrics(1)));
    }
}
