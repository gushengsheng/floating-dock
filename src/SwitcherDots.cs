using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace FloatingDock
{
    /// <summary>Vertical column of dots under the folder button. Click (or hotkey) opens the window switcher.</summary>
    public sealed class SwitcherDots : Window
    {
        private static readonly Brush DotBrush = Frozen(Color.FromArgb(0xFF, 0x8A, 0xB4, 0xF8));

        private static SolidColorBrush Frozen(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private readonly Func<int> _countProvider;
        private string _hotkeyText;
        private StackPanel _panel;
        private DispatcherTimer _timer;
        private int _builtCount = -1;

        public event EventHandler OpenRequested;
        public event EventHandler SettingsRequested;
        public event EventHandler HideRequested;

        public SwitcherDots(Func<int> countProvider, string hotkeyText)
        {
            _countProvider = countProvider;
            _hotkeyText = hotkeyText;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = 22;
            Top = 56;
            Width = 16;
            UseLayoutRounding = true;

            _panel = new StackPanel();
            _panel.VerticalAlignment = VerticalAlignment.Center;
            _panel.HorizontalAlignment = HorizontalAlignment.Center;
            _panel.Cursor = Cursors.Hand;
            _panel.Opacity = 0.55;

            _panel.MouseEnter += delegate { _panel.Opacity = 1.0; };
            _panel.MouseLeave += delegate { _panel.Opacity = 0.55; };
            _panel.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (OpenRequested != null) OpenRequested(this, EventArgs.Empty);
            };
            _panel.MouseRightButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                BuildMenu().IsOpen = true;
            };

            Content = _panel;

            RefreshDots();

            Loaded += delegate
            {
                _timer = new DispatcherTimer();
                _timer.Interval = TimeSpan.FromMilliseconds(1200);
                _timer.Tick += delegate { RefreshDots(); };
                _timer.Start();
            };
            Closed += delegate { if (_timer != null) _timer.Stop(); };
        }

        public void SetHotkeyText(string text)
        {
            _hotkeyText = text;
            UpdateTooltip();
        }

        public void RefreshDots()
        {
            int wanted = 1;
            try
            {
                if (_countProvider != null) wanted = _countProvider();
            }
            catch { }
            if (wanted < 1) wanted = 1;
            if (wanted > 8) wanted = 8;
            if (wanted == _builtCount) return;
            _builtCount = wanted;

            _panel.Children.Clear();
            for (int i = 0; i < wanted; i++)
            {
                System.Windows.Shapes.Ellipse dot = new System.Windows.Shapes.Ellipse();
                dot.Width = 6;
                dot.Height = 6;
                dot.Fill = DotBrush;
                dot.Opacity = 0.85;
                dot.Margin = new Thickness(0, 4, 0, 4);
                _panel.Children.Add(dot);
            }

            Height = wanted * 14 + 6;
            UpdateTooltip();
        }

        private void UpdateTooltip()
        {
            ToolTip tip = new ToolTip();
            tip.Content = string.IsNullOrEmpty(_hotkeyText)
                ? "窗口切换"
                : "窗口切换  (" + _hotkeyText + ")";
            tip.Background = Frozen(Color.FromArgb(0xF2, 0x20, 0x21, 0x26));
            tip.Foreground = Brushes.White;
            tip.BorderBrush = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            tip.BorderThickness = new Thickness(1);
            tip.Padding = new Thickness(9, 4, 9, 4);
            tip.FontSize = 12;
            _panel.ToolTip = tip;
        }

        private ContextMenu BuildMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.FontSize = 12;

            MenuItem open = new MenuItem();
            open.Header = "打开窗口切换器";
            open.Click += delegate { if (OpenRequested != null) OpenRequested(this, EventArgs.Empty); };
            menu.Items.Add(open);

            MenuItem settings = new MenuItem();
            settings.Header = "设置…";
            settings.Click += delegate { if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty); };
            menu.Items.Add(settings);

            menu.Items.Add(new Separator());

            MenuItem hide = new MenuItem();
            hide.Header = "隐藏这列点";
            hide.Click += delegate { if (HideRequested != null) HideRequested(this, EventArgs.Empty); };
            menu.Items.Add(hide);

            menu.PlacementTarget = _panel;
            return menu;
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
