using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace FloatingDock
{
    /// <summary>Vertical window switcher: dim the screen, list open windows, arrow keys or click to jump.</summary>
    public sealed class WindowSwitcher : Window
    {
        private static readonly Brush Backdrop = Frozen(Color.FromArgb(0xCC, 0x02, 0x02, 0x04));
        private static readonly Brush PanelBg = Frozen(Color.FromArgb(0xF6, 0x15, 0x16, 0x1B));
        private static readonly Brush PaneBorder = Frozen(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));
        private static readonly Brush RowHover = Frozen(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
        private static readonly Brush RowSelected = Frozen(Color.FromArgb(0x44, 0x6E, 0x9C, 0xEF));
        private static readonly Brush TextMain = Brushes.White;
        private static readonly Brush TextDim = Frozen(Color.FromArgb(0xFF, 0x92, 0x98, 0xA3));
        private static readonly Brush TextFaint = Frozen(Color.FromArgb(0xFF, 0x6B, 0x70, 0x7A));

        private static SolidColorBrush Frozen(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private sealed class Row
        {
            public Border Border;
            public WindowEntry Entry;
            public int Hover;
        }

        private readonly WindowTracker _tracker = new WindowTracker();
        private readonly List<Row> _rows = new List<Row>();
        private StackPanel _list;
        private TextBlock _countText;
        private int _selected;

        public event EventHandler Hidden;

        public WindowSwitcher()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, sans-serif");
            UseLayoutRounding = true;

            BuildUi();
        }

        private void BuildUi()
        {
            Grid root = new Grid();
            root.Background = Backdrop;
            root.MouseLeftButtonDown += delegate { CloseSwitcher(); };

            Border panel = new Border();
            panel.Width = 460;
            panel.HorizontalAlignment = HorizontalAlignment.Left;
            panel.VerticalAlignment = VerticalAlignment.Top;
            panel.Margin = new Thickness(66, 54, 0, 54);
            panel.MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 108);
            panel.CornerRadius = new CornerRadius(14);
            panel.Background = PanelBg;
            panel.BorderBrush = PaneBorder;
            panel.BorderThickness = new Thickness(1);
            panel.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };

            DropShadowEffect sh = new DropShadowEffect();
            sh.BlurRadius = 30;
            sh.ShadowDepth = 6;
            sh.Opacity = 0.65;
            sh.Color = Colors.Black;
            panel.Effect = sh;

            Grid inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Border head = new Border();
            head.Padding = new Thickness(16, 0, 14, 0);
            head.BorderThickness = new Thickness(0, 0, 0, 1);
            head.BorderBrush = PaneBorder;

            Grid hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel titleSp = new StackPanel();
            titleSp.Orientation = Orientation.Horizontal;
            titleSp.VerticalAlignment = VerticalAlignment.Center;

            TextBlock title = new TextBlock();
            title.Text = "窗口切换";
            title.FontSize = 13.5;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = TextMain;
            title.VerticalAlignment = VerticalAlignment.Center;
            titleSp.Children.Add(title);

            _countText = new TextBlock();
            _countText.FontSize = 10.5;
            _countText.Foreground = TextFaint;
            _countText.Margin = new Thickness(8, 0, 0, 0);
            _countText.VerticalAlignment = VerticalAlignment.Center;
            titleSp.Children.Add(_countText);

            Grid.SetColumn(titleSp, 0);
            hg.Children.Add(titleSp);

            TextBlock hint = new TextBlock();
            hint.Text = "↑↓ 选择 · Enter 切换 · Esc 关闭";
            hint.FontSize = 10.2;
            hint.Foreground = TextFaint;
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(hint, 2);
            hg.Children.Add(hint);

            head.Child = hg;
            Grid.SetRow(head, 0);
            inner.Children.Add(head);

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.CanContentScroll = false;
            scroll.Margin = new Thickness(6, 6, 6, 6);
            scroll.Background = Brushes.Transparent;

            _list = new StackPanel();
            scroll.Content = _list;

            Grid.SetRow(scroll, 1);
            inner.Children.Add(scroll);

            panel.Child = inner;
            root.Children.Add(panel);
            Content = root;
        }

        public void ShowSwitcher()
        {
            try
            {
                Rect wa = SystemParameters.WorkArea;
                Left = wa.Left;
                Top = wa.Top;
                Width = wa.Width;
                Height = wa.Height;

                BuildRows();
                if (!IsVisible) Show();
                N.ActivateWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                Activate();
                Focus();
                Dispatcher.BeginInvoke(new Action(delegate { Focus(); }), DispatcherPriority.Input);
            }
            catch (Exception ex)
            {
                ConfigStore.Log("switcher show failed: " + ex);
            }
        }

        public void CloseSwitcher()
        {
            if (!IsVisible) return;
            Hide();
            if (_list != null) _list.Children.Clear();
            _rows.Clear();
            if (Hidden != null) Hidden(this, EventArgs.Empty);
        }

        private void BuildRows()
        {
            _list.Children.Clear();
            _rows.Clear();

            List<WindowEntry> windows = _tracker.ListWindows();
            _countText.Text = windows.Count + " 个窗口";

            if (windows.Count == 0)
            {
                TextBlock empty = new TextBlock();
                empty.Text = "没有可切换的窗口";
                empty.FontSize = 12;
                empty.Foreground = TextDim;
                empty.Margin = new Thickness(12, 12, 12, 12);
                _list.Children.Add(empty);
                _selected = 0;
                return;
            }

            for (int i = 0; i < windows.Count; i++)
            {
                WindowEntry e = windows[i];
                Border row = new Border();
                row.Height = 44;
                row.CornerRadius = new CornerRadius(8);
                row.Margin = new Thickness(2, 1, 2, 1);
                row.Padding = new Thickness(10, 0, 10, 0);
                row.Background = Brushes.Transparent;
                row.Cursor = Cursors.Hand;

                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Image icon = new Image();
                ImageSource src = Icons.ForApp(e.ExePath);
                if (src == null) src = Icons.ForWindow(e.Hwnd);
                icon.Source = src != null ? src : Icons.Placeholder();
                icon.Width = 22;
                icon.Height = 22;
                icon.Margin = new Thickness(0, 0, 10, 0);
                icon.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(icon, 0);
                g.Children.Add(icon);

                StackPanel text = new StackPanel();
                text.VerticalAlignment = VerticalAlignment.Center;

                TextBlock t = new TextBlock();
                t.Text = string.IsNullOrEmpty(e.Title) ? e.AppName : e.Title;
                t.FontSize = 12.5;
                t.Foreground = TextMain;
                t.TextTrimming = TextTrimming.CharacterEllipsis;
                text.Children.Add(t);

                TextBlock app = new TextBlock();
                app.Text = e.AppName;
                app.FontSize = 10.3;
                app.Foreground = TextFaint;
                app.TextTrimming = TextTrimming.CharacterEllipsis;
                text.Children.Add(app);

                Grid.SetColumn(text, 1);
                g.Children.Add(text);

                row.Child = g;

                Row view = new Row();
                view.Border = row;
                view.Entry = e;
                view.Hover = -1;

                row.MouseEnter += delegate { view.Hover = 1; UpdateRows(); };
                row.MouseLeave += delegate { view.Hover = -1; UpdateRows(); };
                row.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs ev)
                {
                    ev.Handled = true;
                    int index = _rows.IndexOf(view);
                    _selected = index;
                    UpdateRows();
                    ActivateCurrent();
                };

                _rows.Add(view);
                _list.Children.Add(row);
            }

            _selected = windows.Count > 1 ? 1 : 0;
            UpdateRows();
        }

        private void UpdateRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (i == _selected) r.Border.Background = RowSelected;
                else if (r.Hover > 0) r.Border.Background = RowHover;
                else r.Border.Background = Brushes.Transparent;
            }
        }

        private void Move(int delta)
        {
            if (_rows.Count == 0) return;
            int next = _selected + delta;
            if (next < 0) next = 0;
            if (next >= _rows.Count) next = _rows.Count - 1;
            if (next == _selected) return;
            _selected = next;
            UpdateRows();
            _rows[_selected].Border.BringIntoView();
        }

        private void ActivateCurrent()
        {
            if (_selected < 0 || _selected >= _rows.Count) return;
            IntPtr hwnd = _rows[_selected].Entry.Hwnd;

            // Bring the target forward while this overlay still owns the foreground,
            // then drop the overlay so it cannot sit above the window we just switched to.
            N.ActivateWindow(hwnd);
            CloseSwitcher();
            N.BringToFront(hwnd);

            DispatcherTimer retry = new DispatcherTimer(DispatcherPriority.Normal);
            retry.Interval = TimeSpan.FromMilliseconds(120);
            retry.Tick += delegate(object s, EventArgs e)
            {
                retry.Stop();
                if (N.IsWindow(hwnd) && N.GetForegroundWindow() != hwnd) N.ActivateWindow(hwnd);
                N.BringToFront(hwnd);
            };
            retry.Start();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    e.Handled = true;
                    CloseSwitcher();
                    return;

                case Key.Up:
                case Key.W:
                    e.Handled = true;
                    Move(-1);
                    return;

                case Key.Down:
                case Key.S:
                    e.Handled = true;
                    Move(1);
                    return;

                case Key.Enter:
                    e.Handled = true;
                    ActivateCurrent();
                    return;
            }
            base.OnPreviewKeyDown(e);
        }
    }
}
