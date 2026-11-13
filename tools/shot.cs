using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

internal static class Shot
{
    [STAThread]
    private static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "shot.png";
        Rectangle b = Screen.PrimaryScreen.Bounds;
        using (Bitmap bmp = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(b.Location, Point.Empty, b.Size, CopyPixelOperation.SourceCopy);
            }
            bmp.Save(outPath, ImageFormat.Png);
        }
        Console.WriteLine(string.Format("{0} {1}x{2}", outPath, b.Width, b.Height));
    }
}
