using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FloatingDock
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    internal static class Icons
    {
        private static readonly Dictionary<string, ImageSource> _cache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, ImageSource> _extCache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        private static ImageSource _folder;
        private static ImageSource _placeholder;

        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

        /// <summary>Generic folder icon (used for folder mode panes and rows).</summary>
        public static ImageSource ForFolder()
        {
            if (_folder != null) return _folder;
            _folder = FromAttributes(Environment.GetFolderPath(Environment.SpecialFolder.Windows), FILE_ATTRIBUTE_DIRECTORY);
            if (_folder == null) _folder = Placeholder();
            return _folder;
        }

        /// <summary>Icon for a file extension, resolved without touching the disk.</summary>
        public static ImageSource ForExtension(string ext)
        {
            string key = string.IsNullOrEmpty(ext) ? "" : ext.ToLowerInvariant();
            ImageSource hit;
            if (_extCache.TryGetValue(key, out hit)) return hit;
            ImageSource img = FromAttributes("file" + key, FILE_ATTRIBUTE_NORMAL);
            if (img == null) img = Placeholder();
            _extCache[key] = img;
            return img;
        }

        public static ImageSource ForApp(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string key = path;
            ImageSource hit;
            if (_cache.TryGetValue(key, out hit)) return hit;

            ImageSource img = FromShellItem(path);
            if (img == null) img = FromFileInfo(path);
            if (img != null) _cache[key] = img;
            return img;
        }

        public static ImageSource ForWindow(IntPtr hwnd)
        {
            try
            {
                IntPtr h = N.SendMessage(hwnd, N.WM_GETICON, (IntPtr)N.ICON_BIG, IntPtr.Zero);
                if (h == IntPtr.Zero) h = N.SendMessage(hwnd, N.WM_GETICON, (IntPtr)N.ICON_SMALL2, IntPtr.Zero);
                if (h == IntPtr.Zero) h = N.GetClassLongPtr(hwnd, N.GCLP_HICON);
                if (h == IntPtr.Zero) return null;
                BitmapSource src = Imaging.CreateBitmapSourceFromHIcon(
                    h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch
            {
                return null;
            }
        }

        public static ImageSource Placeholder()
        {
            if (_placeholder != null) return _placeholder;
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromArgb(255, 90, 96, 110)), null,
                    new Rect(0, 0, 48, 48), 12, 12);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(255, 210, 215, 225)), null,
                    new Point(24, 24), 9, 9);
            }
            RenderTargetBitmap rtb = new RenderTargetBitmap(48, 48, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            _placeholder = rtb;
            return _placeholder;
        }

        private static ImageSource FromShellItem(string path)
        {
            try
            {
                object item;
                Guid iid = typeof(IShellItemImageFactory).GUID;
                N.SHCreateItemFromParsingName(path, IntPtr.Zero, iid, out item);
                if (item == null) return null;
                IShellItemImageFactory factory = item as IShellItemImageFactory;
                if (factory == null) return null;
                IntPtr hbm;
                int hr = factory.GetImage(new SIZE(64, 64), N.SIIGBF_ICONONLY | N.SIIGBF_BIGGERSIZEOK, out hbm);
                if (hr != 0 || hbm == IntPtr.Zero) return null;
                try
                {
                    BitmapSource src = Imaging.CreateBitmapSourceFromHBitmap(
                        hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally
                {
                    N.DeleteObject(hbm);
                }
            }
            catch
            {
                return null;
            }
        }

        private static ImageSource FromFileInfo(string path)
        {
            try
            {
                SHFILEINFO info = new SHFILEINFO();
                uint flags = N.SHGFI_ICON | N.SHGFI_LARGEICON;
                IntPtr res = N.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), flags);
                if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
                try
                {
                    BitmapSource src = Imaging.CreateBitmapSourceFromHIcon(
                        info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally
                {
                    N.DestroyIcon(info.hIcon);
                }
            }
            catch
            {
                return null;
            }
        }

        private static ImageSource FromAttributes(string name, uint attributes)
        {
            try
            {
                SHFILEINFO info = new SHFILEINFO();
                uint flags = N.SHGFI_ICON | N.SHGFI_LARGEICON | N.SHGFI_USEFILEATTRIBUTES;
                IntPtr res = N.SHGetFileInfo(name, attributes, ref info,
                    (uint)Marshal.SizeOf(typeof(SHFILEINFO)), flags);
                if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
                try
                {
                    BitmapSource src = Imaging.CreateBitmapSourceFromHIcon(
                        info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally
                {
                    N.DestroyIcon(info.hIcon);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
