using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FloatingDock
{
    /// <summary>Small always-on-top launcher pinned to the top-left corner of the screen.</summary>
    public sealed class CornerButton : Window
    {
        public event EventHandler OpenRequested;
        public event EventHandler HideRequested;

        public CornerButton()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, sans-serif");
            Width = 40;
            Height = 40;
            UseLayoutRounding = true;

            Border pill = new Border();
            pill.Width = 38;
            pill.Height = 38;
            pill.CornerRadius = new CornerRadius(11);
            pill.Background = Frozen(Color.FromArgb(0xCC, 0x1A, 0x1C, 0x22));
            pill.BorderBrush = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            pill.BorderThickness = new Thickness(1);
            pill.Cursor = Cursors.Hand;
            pill.Opacity = 0.72;

            Image icon = new Image();
            icon.Source = Icons.ForFolder();
            icon.Width = 20;
            icon.Height = 20;
            icon.Stretch = Stretch.Uniform;
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            pill.Child = icon;

            DropShadowEffect sh = new DropShadowEffect();
            sh.BlurRadius = 12;
            sh.ShadowDepth = 2;
            sh.Opacity = 0.45;
            sh.Color = Colors.Black;
            pill.Effect = sh;

            pill.MouseEnter += delegate { pill.Opacity = 1; };
            pill.MouseLeave += delegate { pill.Opacity = 0.72; };
            pill.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (OpenRequested != null) OpenRequested(this, EventArgs.Empty);
            };
            pill.MouseRightButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                ContextMenu menu = new ContextMenu();
                menu.FontSize = 12;

                MenuItem open = new MenuItem();
                open.Header = "打开快捷文件夹";
                open.Click += delegate { if (OpenRequested != null) OpenRequested(this, EventArgs.Empty); };
                menu.Items.Add(open);

                menu.Items.Add(new Separator());

                MenuItem hide = new MenuItem();
                hide.Header = "隐藏这个按钮";
                hide.Click += delegate { if (HideRequested != null) HideRequested(this, EventArgs.Empty); };
                menu.Items.Add(hide);

                menu.PlacementTarget = pill;
                menu.IsOpen = true;
            };

            ToolTip tip = new ToolTip();
            tip.Content = "快捷文件夹";
            tip.Background = Frozen(Color.FromArgb(0xF2, 0x20, 0x21, 0x26));
            tip.Foreground = Brushes.White;
            tip.BorderBrush = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            tip.BorderThickness = new Thickness(1);
            tip.Padding = new Thickness(9, 4, 9, 4);
            tip.FontSize = 12;
            pill.ToolTip = tip;

            Content = pill;
            Loaded += OnLoaded;
        }

        private static SolidColorBrush Frozen(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private void OnLoaded(object sender, EventArgs e)
        {
            Rect wa = SystemParameters.WorkArea;
            Left = wa.Left + 10;
            Top = wa.Top + 10;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            long ex = N.GetWindowLongPtr(hwnd, N.GWL_EXSTYLE).ToInt64();
            ex |= N.WS_EX_TOOLWINDOW;
            ex &= ~N.WS_EX_APPWINDOW;
            N.SetWindowLongPtr(hwnd, N.GWL_EXSTYLE, new IntPtr(ex));
        }
    }
}
