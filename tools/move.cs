using System;
using System.Runtime.InteropServices;

// DPI-aware cursor mover: move.exe <x> <y>
internal static class Move
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    private static void Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: move.exe <x> <y>"); return; }
        int x = int.Parse(args[0]);
        int y = int.Parse(args[1]);
        bool ok = SetCursorPos(x, y);
        Console.WriteLine(string.Format("move {0},{1} ok={2}", x, y, ok));
    }
}
