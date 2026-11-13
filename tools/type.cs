using System;
using System.Windows.Forms;

// Types literal text into the focused window via SendKeys.
internal static class TypeText
{
    [STAThread]
    private static void Main(string[] a)
    {
        if (a.Length == 0) { Console.WriteLine("usage: type <text>"); return; }
        SendKeys.SendWait(a[0]);
        Console.WriteLine("typed " + a[0]);
    }
}
