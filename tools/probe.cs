using System;
using System.Text;
using System.Runtime.InteropServices;

// DPI-aware probe: what window is under a physical point?
internal static class Probe
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT p);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr h, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr h, StringBuilder s, int n);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    private static void Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: probe <x> <y>"); return; }
        int x = int.Parse(args[0]);
        int y = int.Parse(args[1]);
        SetCursorPos(x, y);
        POINT p = new POINT();
        p.x = x;
        p.y = y;
        IntPtr h = WindowFromPoint(p);
        IntPtr root = GetAncestor(h, 2);
        Console.WriteLine(Describe("window at point", h));
        Console.WriteLine(Describe("root ancestor", root));
        Console.WriteLine(Describe("foreground", GetForegroundWindow()));
        Console.WriteLine("cursor set to " + x + "," + y);
    }

    private static string Describe(string label, IntPtr h)
    {
        if (h == IntPtr.Zero) return label + ": <none>";
        StringBuilder c = new StringBuilder(256);
        GetClassName(h, c, 256);
        StringBuilder t = new StringBuilder(256);
        GetWindowText(h, t, 256);
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        string name = "";
        try { name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { }
        return string.Format("{0}: hwnd={1} class={2} pid={3} proc={4} title={5}", label, h, c, pid, name, t);
    }
}
