using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FloatingDock
{
    internal sealed class DockItem
    {
        public string Path;
        public string MatchPath;
        public string Title;
        public WindowGroup Group;
        public bool Pinned;
        public ImageSource Icon;
    }

    public sealed class DockWindow : Window
    {
        private readonly DockConfig _cfg;
        private readonly WindowTracker _tracker = new WindowTracker();

        private Border _pill;
        private StackPanel _items;
        private ScrollViewer _scroll;

        private DispatcherTimer _timer;
        private HwndSource _hwndSource;
        private IntPtr _hwnd;

        private readonly List<IconView> _views = new List<IconView>();
        private string _builtSignature = "";

        private bool _userVisible = true;
        private bool _hiddenByFullscreen;
        private DateTime _suppressFullscreenUntil = DateTime.MinValue;
        private bool _centered = true;
        private bool _ready;
        private string _dockHotkeyText = "";
        private string _taskbarHotkeyText = "";
        private string _folderHotkeyText = "";
        private string _hotkeyNotice = "";
        private Rect _lastWorkArea;

        private CornerButton _corner;
        private FolderOverlay _overlay;
        private WindowSwitcher _switcher;
        private SwitcherDots _dots;
        private SearchPanel _search;
        private bool _foldersVisible;
        private bool _switcherVisible;
        private string _switcherHotkeyText = "";
        private string _searchHotkeyText = "Ctrl+Alt+S";

        private sealed class IconView
        {
            public Border Border;
            public DockItem Item;
            public bool Hover;
        }

        private static readonly Brush PillBackground = Frozen(Color.FromArgb(0xE8, 0x1B, 0x1D, 0x22));
        private static readonly Brush PillBorder = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        private static readonly Brush HoverBrush = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
        private static readonly Brush ActiveBrush = Frozen(Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF));
        private static readonly Brush DividerBrush = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
        private static readonly Brush AccentBrush = Frozen(Color.FromArgb(0xFF, 0x8A, 0xB4, 0xF8));

        private static Brush Frozen(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public DockWindow()
        {
            _cfg = ConfigStore.Load();
            BuildUi();

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            SizeChanged += OnSizeChanged;
            Closed += OnClosed;
        }

        // ------------------------------------------------------------------ UI

        private void BuildUi()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, sans-serif");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            Grid root = new Grid();
            root.Margin = new Thickness(20);   // room for the drop shadow

            _pill = new Border();
            _pill.CornerRadius = new CornerRadius(18);
            _pill.Background = PillBackground;
            _pill.BorderBrush = PillBorder;
            _pill.BorderThickness = new Thickness(1);
            _pill.Padding = new Thickness(8, 6, 8, 6);
            _pill.Cursor = Cursors.SizeAll;
            _pill.MouseLeftButtonDown += OnPillMouseDown;
            _pill.MouseRightButtonUp += OnPillRightClick;

            DropShadowEffect shadow = new DropShadowEffect();
            shadow.BlurRadius = 22;
            shadow.ShadowDepth = 3;
            shadow.Opacity = 0.55;
            shadow.Color = Colors.Black;
            _pill.Effect = shadow;

            _items = new StackPanel();
            _items.Orientation = Orientation.Horizontal;
            _items.VerticalAlignment = VerticalAlignment.Center;

            _scroll = new ScrollViewer();
            _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _scroll.CanContentScroll = false;
            _scroll.Content = _items;
            _scroll.MaxWidth = Math.Max(320, SystemParameters.WorkArea.Width - 80);
            _scroll.PreviewMouseWheel += OnWheel;

            _pill.Child = _scroll;
            root.Children.Add(_pill);
            Content = root;

            Rebuild();
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(_hwnd);
            if (_hwndSource != null) _hwndSource.AddHook(WndProc);

            long ex = N.GetWindowLongPtr(_hwnd, N.GWL_EXSTYLE).ToInt64();
            ex |= N.WS_EX_TOOLWINDOW;
            ex &= ~N.WS_EX_APPWINDOW;
            N.SetWindowLongPtr(_hwnd, N.GWL_EXSTYLE, new IntPtr(ex));

            _tracker.ExcludeHwnd = _hwnd;
            RegisterHotkeys();
        }

        private void OnLoaded(object sender, EventArgs e)
        {
            PlaceInitial();
            _ready = true;

            if (!File.Exists(ConfigStore.ConfigPath)) ConfigStore.Save(_cfg);
            if (_cfg.AutoStart) AutoStart.Set(true, Process.GetCurrentProcess().MainModule.FileName);

            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Interval = TimeSpan.FromMilliseconds(650);
            _timer.Tick += delegate { Tick(); };
            _timer.Start();
            Tick();

            EnsureCornerButton();
            EnsureSwitcherDots();

            if (Program.OpenSettingsOnStart)
            {
                Program.OpenSettingsOnStart = false;
                Dispatcher.BeginInvoke(new Action(delegate { OpenSettings(); }), DispatcherPriority.ApplicationIdle);
            }

            if (!string.IsNullOrEmpty(_hotkeyNotice))
                Dispatcher.BeginInvoke(new Action(delegate { Toast(_hotkeyNotice); }), DispatcherPriority.ApplicationIdle);
        }

        private void OnClosed(object sender, EventArgs e)
        {
            if (_timer != null) _timer.Stop();
            UnregisterHotkeys();
            if (_hwndSource != null) _hwndSource.RemoveHook(WndProc);
            try { if (_corner != null) _corner.Close(); } catch { }
            try { if (_dots != null) _dots.Close(); } catch { }
            try { if (_overlay != null) _overlay.Close(); } catch { }
            try { if (_switcher != null) _switcher.Close(); } catch { }
            try { if (_search != null) _search.Close(); } catch { }
        }

        // ------------------------------------------------------------------ quick folders

        private void EnsureCornerButton()
        {
            if (_cfg.HideCornerButton)
            {
                if (_corner != null) { _corner.Close(); _corner = null; }
                RefreshAuxWindows();
                return;
            }
            if (_corner != null) { RefreshAuxWindows(); return; }
            _corner = new CornerButton();
            _corner.OpenRequested += delegate { OpenFolders(); };
            _corner.HideRequested += delegate
            {
                _cfg.HideCornerButton = true;
                ConfigStore.Save(_cfg);
                EnsureCornerButton();
                Toast("已隐藏左上角按钮，可在设置里重新打开");
            };
            _corner.Show();
            RefreshAuxWindows();
        }

        private void EnsureSwitcherDots()
        {
            if (_cfg.HideSwitcherDots)
            {
                if (_dots != null) { _dots.Close(); _dots = null; }
                RefreshAuxWindows();
                return;
            }
            if (_dots != null)
            {
                _dots.SetHotkeyText(_switcherHotkeyText);
                RefreshAuxWindows();
                return;
            }
            _dots = new SwitcherDots(delegate { return _tracker.TotalWindows(); }, _switcherHotkeyText);
            _dots.OpenRequested += delegate { OpenSwitcher(); };
            _dots.SettingsRequested += delegate { OpenSettings(); };
            _dots.HideRequested += delegate
            {
                _cfg.HideSwitcherDots = true;
                ConfigStore.Save(_cfg);
                EnsureSwitcherDots();
                Toast("已隐藏窗口切换点，可在设置里重新打开");
            };
            _dots.Show();
            RefreshAuxWindows();
        }

        /// <summary>Corner button and dots hide while a full screen mode is open, or when everything is hidden.</summary>
        private void RefreshAuxWindows()
        {
            bool hideAll = _foldersVisible || _switcherVisible || !_userVisible;
            if (_corner != null)
            {
                if (hideAll) _corner.Hide();
                else if (!_cfg.HideCornerButton) _corner.Show();
            }
            if (_dots != null)
            {
                if (hideAll) _dots.Hide();
                else if (!_cfg.HideSwitcherDots) _dots.Show();
            }
        }

        public void OpenFolders()
        {
            if (_foldersVisible) return;
            if (_overlay == null)
            {
                _overlay = new FolderOverlay();
                _overlay.Hidden += delegate { CloseFoldersVisuals(); };
            }
            _foldersVisible = true;
            UpdateVisibility();
            RefreshAuxWindows();
            _overlay.ShowOverlay();
        }

        private void CloseFoldersVisuals()
        {
            _foldersVisible = false;
            RefreshAuxWindows();
            UpdateVisibility();
        }

        public void OpenSwitcher()
        {
            if (_switcherVisible)
            {
                if (_switcher != null) _switcher.CloseSwitcher();
                return;
            }
            if (_switcher == null)
            {
                _switcher = new WindowSwitcher();
                _switcher.Hidden += delegate
                {
                    _switcherVisible = false;
                    RefreshAuxWindows();
                    UpdateVisibility();
                };
            }
            _switcherVisible = true;
            UpdateVisibility();
            RefreshAuxWindows();
            _switcher.ShowSwitcher();
        }

        public void OpenSearch()
        {
            if (_search == null) _search = new SearchPanel(_cfg);
            _search.ShowSearch();
        }

        public void OpenSettings()
        {            SettingsWindow w = new SettingsWindow(_cfg, delegate { ApplySettings(); });
            w.Owner = this;
            w.ShowDialog();
            Activate();
        }

        private void ApplySettings()
        {
            ConfigStore.Save(_cfg);
            RegisterHotkeys();
            Rebuild();
            UpdateAppearance();
            EnsureCornerButton();
            EnsureSwitcherDots();
            try
            {
                AutoStart.Set(_cfg.AutoStart, Process.GetCurrentProcess().MainModule.FileName);
            }
            catch { }
            Toast("设置已保存");
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_ready && _centered) PlaceAuto();
        }

        private Rect WorkArea { get { return SystemParameters.WorkArea; } }

        private void PlaceAuto()
        {
            Rect wa = WorkArea;
            if (double.IsNaN(ActualWidth) || ActualWidth <= 0) return;

            double left, top;
            if (_cfg.CompactDock)
            {
                // 简洁模式：左上角一列 —— 文件夹按钮、搜索胶囊、竖点（点在最下面）
                double pillLeft = wa.Left + 10;
                double pillTop = _cfg.HideCornerButton ? wa.Top + 10 : wa.Top + 58;
                left = pillLeft - 20;
                top = pillTop - 20;
            }
            else
            {
                left = wa.Left + (wa.Width - ActualWidth) / 2.0;
                top = wa.Top + wa.Height - ActualHeight - 4;
            }

            if (Math.Abs(Left - left) > 0.5) Left = left;
            if (Math.Abs(Top - top) > 0.5) Top = top;

            PositionDots();
        }

        /// <summary>In compact mode the dots column sits right under the search capsule.</summary>
        private void PositionDots()
        {
            if (_dots == null) return;
            Rect wa = WorkArea;
            double top;
            if (_cfg.CompactDock)
            {
                double pillTop = _cfg.HideCornerButton ? wa.Top + 10 : wa.Top + 58;
                double pillHeight = Math.Max(40, ActualHeight - 40);
                top = pillTop + pillHeight + 8;
            }
            else
            {
                top = wa.Top + 56;
            }
            if (Math.Abs(_dots.Top - top) > 0.5) _dots.Top = top;
        }

        private void PlaceInitial()
        {
            if (!_cfg.Left.HasValue && !_cfg.Top.HasValue)
            {
                _centered = true;
                PlaceAuto();
                return;
            }
            double l = _cfg.Left.HasValue ? _cfg.Left.Value : 0;
            double t = _cfg.Top.HasValue ? _cfg.Top.Value : 0;
            Left = l;
            Top = t;
            _centered = false;
            if (!IsOnAnyScreen(l, t, ActualWidth, ActualHeight))
            {
                _centered = true;
                PlaceAuto();
            }
        }

        private static bool IsOnAnyScreen(double left, double top, double w, double h)
        {
            Rect wa = SystemParameters.WorkArea;
            Rect v = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            Rect r = new Rect(left, top, Math.Max(w, 40), Math.Max(h, 40));
            if (!v.IntersectsWith(r)) return false;
            return left >= v.Left - 20 && top >= v.Top - 20 &&
                   left + w <= v.Right + 20 && top + h <= v.Bottom + 20;
        }

        // ------------------------------------------------------------------ refresh

        private void Tick()
        {
            try
            {
                if (_tracker.ExcludeHwnd != _hwnd) _tracker.ExcludeHwnd = _hwnd;
                _tracker.Refresh();

                // keep a bottom-centred dock glued to the bottom edge when the work area changes
                Rect wa = WorkArea;
                if (_ready && _centered &&
                    (Math.Abs(wa.Height - _lastWorkArea.Height) > 0.5 || Math.Abs(wa.Width - _lastWorkArea.Width) > 0.5 ||
                     Math.Abs(wa.Top - _lastWorkArea.Top) > 0.5))
                {
                    PlaceAuto();
                }
                else if (_ready && _centered && _cfg.CompactDock)
                {
                    // the dots column changes height with the window count
                    PlaceAuto();
                }
                _lastWorkArea = wa;

                bool fs = _cfg.HideInFullscreen && IsFullscreenActive();
                _hiddenByFullscreen = fs;

                if (_tracker.Signature != _builtSignature) Rebuild();
                UpdateAppearance();
                UpdateVisibility();
            }
            catch (Exception ex)
            {
                ConfigStore.Log("tick: " + ex.Message);
            }
        }

        private bool IsFullscreenActive()
        {
            IntPtr fg = N.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == _hwnd) return false;
            if (fg == N.GetShellWindow()) return false;
            if (N.IsZoomed(fg)) return false;          // maximized is not fullscreen
            if (N.GetWindowTextLength(fg) <= 0) return false;

            RECT r;
            if (!N.GetWindowRect(fg, out r)) return false;
            RECT mon = N.GetMonitorRect(fg);
            if (mon.Width <= 0 || mon.Height <= 0) return false;
            return r.Left <= mon.Left + 1 && r.Top <= mon.Top + 1 &&
                   r.Right >= mon.Right - 1 && r.Bottom >= mon.Bottom - 1;
        }

        private void UpdateVisibility()
        {
            bool shown = _userVisible && !_foldersVisible && !_switcherVisible &&
                         !(_hiddenByFullscreen && DateTime.Now > _suppressFullscreenUntil);
            double want = shown ? 1.0 : 0.0;
            if (Math.Abs(Opacity - want) > 0.001)
            {
                Opacity = want;
                IsHitTestVisible = shown;
            }
        }

        private void UpdateAppearance()
        {
            for (int i = 0; i < _views.Count; i++)
            {
                IconView v = _views[i];
                bool active = v.Item.Group != null && _tracker.IsForeground(v.Item.Group);
                Brush b;
                if (v.Hover) b = HoverBrush;
                else if (active) b = ActiveBrush;
                else b = Brushes.Transparent;
                v.Border.Background = b;
            }
        }

        private void Rebuild()
        {
            if (_items == null) return;
            _items.Children.Clear();
            _views.Clear();

            if (_cfg.CompactDock)
            {
                // 简洁模式：底部只留一个搜索入口
                _items.Children.Add(CreateSearchButton());
                _builtSignature = _tracker.Signature;
                UpdateAppearance();
                return;
            }

            List<DockItem> list = new List<DockItem>();
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _cfg.Pinned.Count; i++)
            {
                string raw = _cfg.Pinned[i];
                if (string.IsNullOrEmpty(raw)) continue;
                string resolved = PathUtil.Resolve(raw);
                string match = resolved.ToLowerInvariant();
                WindowGroup g = FindGroup(match);

                DockItem it = new DockItem();
                it.Path = raw;
                it.MatchPath = match;
                it.Pinned = true;
                it.Group = g;
                it.Title = (g != null && !string.IsNullOrEmpty(g.Title)) ? g.Title : PathUtil.DisplayName(raw);
                ImageSource icon = g != null ? Icons.ForApp(g.ExePath) : Icons.ForApp(resolved);
                it.Icon = icon != null ? icon : Icons.Placeholder();
                list.Add(it);
                if (g != null) used.Add(g.Key);
            }

            bool anyPinned = list.Count > 0;
            int runningUnpinned = 0;
            for (int i = 0; i < _tracker.Groups.Count; i++)
            {
                WindowGroup g = _tracker.Groups[i];
                if (used.Contains(g.Key)) continue;
                DockItem it = new DockItem();
                it.Path = g.ExePath;
                it.MatchPath = (g.ExePath ?? "").ToLowerInvariant();
                it.Pinned = false;
                it.Group = g;
                it.Title = string.IsNullOrEmpty(g.Title) ? g.Key : g.Title;
                ImageSource icon = Icons.ForApp(g.ExePath);
                if (icon == null) icon = Icons.ForWindow(g.ActiveWindow);
                it.Icon = icon != null ? icon : Icons.Placeholder();
                list.Add(it);
                runningUnpinned++;
            }

            bool needDivider = anyPinned && runningUnpinned > 0;
            int dividerBefore = anyPinned ? _cfg.Pinned.Count : 0;

            for (int i = 0; i < list.Count; i++)
            {
                if (needDivider && i == dividerBefore)
                {
                    Border div = new Border();
                    div.Width = 1;
                    div.Height = 22;
                    div.Margin = new Thickness(4, 0, 4, 0);
                    div.Background = DividerBrush;
                    div.VerticalAlignment = VerticalAlignment.Center;
                    _items.Children.Add(div);
                }
                IconView view = CreateIconView(list[i]);
                _views.Add(view);
                _items.Children.Add(view.Border);
            }

            Border searchDivider = new Border();
            searchDivider.Width = 1;
            searchDivider.Height = 22;
            searchDivider.Margin = new Thickness(4, 0, 4, 0);
            searchDivider.Background = DividerBrush;
            searchDivider.VerticalAlignment = VerticalAlignment.Center;
            _items.Children.Add(searchDivider);
            _items.Children.Add(CreateSearchButton());

            _builtSignature = _tracker.Signature;
            UpdateAppearance();
        }

        /// <summary>Magnifier at the right end of the pill: opens the target-folder search.</summary>
        private Border CreateSearchButton()
        {
            Border b = new Border();
            b.Width = _cfg.IconSize + 16;
            b.Height = _cfg.IconSize + 16;
            b.CornerRadius = new CornerRadius(12);
            b.Background = Brushes.Transparent;
            b.Cursor = Cursors.Hand;
            b.Margin = new Thickness(2, 0, 2, 0);

            Grid g = new Grid();
            g.Width = _cfg.IconSize;
            g.Height = _cfg.IconSize;

            Brush stroke = Frozen(Color.FromArgb(0xFF, 0xC2, 0xCA, 0xD8));

            System.Windows.Shapes.Ellipse circle = new System.Windows.Shapes.Ellipse();
            circle.Width = _cfg.IconSize * 0.62;
            circle.Height = _cfg.IconSize * 0.62;
            circle.Stroke = stroke;
            circle.StrokeThickness = 1.7;
            circle.HorizontalAlignment = HorizontalAlignment.Left;
            circle.VerticalAlignment = VerticalAlignment.Top;
            circle.Margin = new Thickness(_cfg.IconSize * 0.1, _cfg.IconSize * 0.06, 0, 0);
            g.Children.Add(circle);

            System.Windows.Shapes.Line handle = new System.Windows.Shapes.Line();
            handle.Stroke = stroke;
            handle.StrokeThickness = 1.7;
            handle.StrokeStartLineCap = PenLineCap.Round;
            handle.StrokeEndLineCap = PenLineCap.Round;
            handle.X1 = _cfg.IconSize * 0.66;
            handle.Y1 = _cfg.IconSize * 0.66;
            handle.X2 = _cfg.IconSize * 0.94;
            handle.Y2 = _cfg.IconSize * 0.94;
            g.Children.Add(handle);

            b.Child = g;

            ToolTip tip = new ToolTip();
            tip.Content = "搜索目标文件夹  (" + _searchHotkeyText + ")";
            tip.Background = Frozen(Color.FromArgb(0xF2, 0x20, 0x21, 0x26));
            tip.Foreground = Brushes.White;
            tip.BorderBrush = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            tip.BorderThickness = new Thickness(1);
            tip.Padding = new Thickness(9, 5, 9, 5);
            tip.FontSize = 12;
            b.ToolTip = tip;
            ToolTipService.SetInitialShowDelay(b, 350);
            ToolTipService.SetPlacement(b, PlacementMode.Top);

            b.MouseEnter += delegate { b.Background = HoverBrush; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                OpenSearch();
            };
            return b;
        }

        private WindowGroup FindGroup(string exeLowerCase)
        {
            if (string.IsNullOrEmpty(exeLowerCase)) return null;
            for (int i = 0; i < _tracker.Groups.Count; i++)
            {
                WindowGroup g = _tracker.Groups[i];
                if (string.IsNullOrEmpty(g.ExePath)) continue;
                if (string.Equals(g.ExePath.ToLowerInvariant(), exeLowerCase, StringComparison.OrdinalIgnoreCase))
                    return g;
            }
            return null;
        }

        private IconView CreateIconView(DockItem item)
        {
            IconView view = new IconView();
            view.Item = item;

            Image img = new Image();
            img.Source = item.Icon;
            img.Width = _cfg.IconSize;
            img.Height = _cfg.IconSize;
            img.Stretch = Stretch.Uniform;
            img.SnapsToDevicePixels = true;
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            System.Windows.Shapes.Ellipse dot = new System.Windows.Shapes.Ellipse();
            dot.Width = 4;
            dot.Height = 4;
            dot.Fill = AccentBrush;
            dot.HorizontalAlignment = HorizontalAlignment.Center;
            dot.VerticalAlignment = VerticalAlignment.Bottom;
            dot.Margin = new Thickness(0, 0, 0, 3);
            dot.Visibility = item.Group != null ? Visibility.Visible : Visibility.Collapsed;

            Grid inner = new Grid();
            inner.Children.Add(img);
            inner.Children.Add(dot);

            Border b = new Border();
            b.Width = _cfg.IconSize + 16;
            b.Height = _cfg.IconSize + 16;
            b.CornerRadius = new CornerRadius(12);
            b.Background = Brushes.Transparent;
            b.Child = inner;
            b.Cursor = Cursors.Hand;
            b.Margin = new Thickness(2, 0, 2, 0);

            string title = item.Title;
            if (!string.IsNullOrEmpty(title))
            {
                ToolTip tt = new ToolTip();
                tt.Content = title.Length > 70 ? title.Substring(0, 70) + "…" : title;
                tt.Background = Frozen(Color.FromArgb(0xF2, 0x20, 0x21, 0x26));
                tt.Foreground = Brushes.White;
                tt.BorderBrush = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
                tt.BorderThickness = new Thickness(1);
                tt.Padding = new Thickness(9, 5, 9, 5);
                tt.FontSize = 12;
                tt.HasDropShadow = true;
                b.ToolTip = tt;
                ToolTipService.SetInitialShowDelay(b, 350);
                ToolTipService.SetPlacement(b, PlacementMode.Top);
            }

            b.MouseEnter += delegate { view.Hover = true; UpdateAppearance(); };
            b.MouseLeave += delegate { view.Hover = false; UpdateAppearance(); };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                OnIconClick(item);
            };
            b.MouseRightButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                ContextMenu menu = BuildIconMenu(item);
                menu.PlacementTarget = b;
                menu.IsOpen = true;
            };

            view.Border = b;
            return view;
        }

        // ------------------------------------------------------------------ actions

        private void OnIconClick(DockItem item)
        {
            if (item.Group == null)
            {
                Launch(item.Path);
                return;
            }
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                Launch(item.Path);
                return;
            }
            _tracker.NoteForeground(N.GetForegroundWindow());

            bool wasActive = _tracker.LastForeground != IntPtr.Zero && item.Group.Contains(_tracker.LastForeground);
            if (wasActive && !N.IsIconic(item.Group.ActiveWindow))
            {
                MinimizeGroup(item.Group);
            }
            else
            {
                IntPtr target = item.Group.ActiveWindow;
                ActivateWithRetry(target);
                _tracker.LastForeground = target;
            }
        }

        /// <summary>Bring a window forward, retrying shortly after in case our own window
        /// re-activates while the click is still being processed.</summary>
        private void ActivateWithRetry(IntPtr target)
        {
            N.ActivateWindow(target);
            DispatcherTimer t = new DispatcherTimer(DispatcherPriority.Normal);
            t.Interval = TimeSpan.FromMilliseconds(140);
            t.Tick += delegate(object s, EventArgs e)
            {
                t.Stop();
                if (N.IsWindow(target) && N.GetForegroundWindow() != target)
                    N.ActivateWindow(target);
            };
            t.Start();
        }

        private void MinimizeGroup(WindowGroup g)
        {
            for (int i = 0; i < g.Windows.Count; i++)
            {
                if (!N.IsIconic(g.Windows[i])) N.MinimizeWindow(g.Windows[i]);
            }
        }

        private void Launch(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = path;
                psi.UseShellExecute = true;
                string dir = null;
                try { dir = Path.GetDirectoryName(PathUtil.Resolve(path)); } catch { }
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) psi.WorkingDirectory = dir;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                ConfigStore.Log("launch failed (" + path + "): " + ex.Message);
                Toast("无法启动：" + Path.GetFileName(path));
            }
        }

        // ------------------------------------------------------------------ menus

        private ContextMenu BuildIconMenu(DockItem item)
        {
            ContextMenu menu = new ContextMenu();
            menu.FontSize = 12;

            if (item.Group != null)
            {
                MenuItem act = new MenuItem();
                act.Header = "切换到";
                IntPtr target = item.Group.ActiveWindow;
                act.Click += delegate { N.ActivateWindow(target); };
                menu.Items.Add(act);

                MenuItem min = new MenuItem();
                min.Header = "最小化";
                min.Click += delegate { MinimizeGroup(item.Group); };
                menu.Items.Add(min);

                if (item.Group.Windows.Count > 1)
                {
                    MenuItem sub = new MenuItem();
                    sub.Header = "窗口 (" + item.Group.Windows.Count + ")";
                    for (int i = 0; i < item.Group.Windows.Count; i++)
                    {
                        IntPtr h = item.Group.Windows[i];
                        string t = N.GetWindowTitle(h);
                        if (string.IsNullOrEmpty(t)) t = "窗口 " + (i + 1);
                        MenuItem wi = new MenuItem();
                        wi.Header = t.Length > 48 ? t.Substring(0, 48) + "…" : t;
                        wi.Click += delegate { N.ActivateWindow(h); };
                        sub.Items.Add(wi);
                    }
                    menu.Items.Add(sub);
                }

                if (!string.IsNullOrEmpty(item.Path))
                {
                    MenuItem again = new MenuItem();
                    again.Header = "新开一个";
                    string p = item.Path;
                    again.Click += delegate { Launch(p); };
                    menu.Items.Add(again);
                }

                MenuItem close = new MenuItem();
                close.Header = "关闭";
                IntPtr ch = item.Group.ActiveWindow;
                close.Click += delegate { N.PostMessage(ch, 0x0010, IntPtr.Zero, IntPtr.Zero); };
                menu.Items.Add(close);
                menu.Items.Add(new Separator());
            }

            if (item.Pinned)
            {
                MenuItem unpin = new MenuItem();
                unpin.Header = "取消固定";
                string p = item.Path;
                unpin.Click += delegate
                {
                    _cfg.Pinned.Remove(p);
                    ConfigStore.Save(_cfg);
                    Rebuild();
                    UpdateAppearance();
                };
                menu.Items.Add(unpin);
            }
            else if (!string.IsNullOrEmpty(item.Path))
            {
                MenuItem pin = new MenuItem();
                pin.Header = "固定到悬浮栏";
                string p = item.Path;
                pin.Click += delegate
                {
                    if (!_cfg.Pinned.Contains(p)) _cfg.Pinned.Add(p);
                    ConfigStore.Save(_cfg);
                    Rebuild();
                    UpdateAppearance();
                };
                menu.Items.Add(pin);
            }

            return menu;
        }

        private ContextMenu BuildMainMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.FontSize = 12;

            MenuItem folders = new MenuItem();
            folders.Header = "快捷文件夹  (" + _folderHotkeyText + ")";
            folders.Click += delegate { OpenFolders(); };
            menu.Items.Add(folders);

            MenuItem switcher = new MenuItem();
            switcher.Header = "窗口切换器  (" + _switcherHotkeyText + ")";
            switcher.Click += delegate { OpenSwitcher(); };
            menu.Items.Add(switcher);

            MenuItem search = new MenuItem();
            search.Header = "目标文件夹搜索  (" + _searchHotkeyText + ")";
            search.Click += delegate { OpenSearch(); };
            menu.Items.Add(search);

            MenuItem settings = new MenuItem();
            settings.Header = "设置…";
            settings.Click += delegate { OpenSettings(); };
            menu.Items.Add(settings);

            menu.Items.Add(new Separator());

            MenuItem hide = new MenuItem();
            hide.Header = "隐藏悬浮栏  (" + _dockHotkeyText + ")";
            hide.Click += delegate { ToggleDock(false); };
            menu.Items.Add(hide);

            MenuItem tb = new MenuItem();
            tb.Header = (GetTaskbarAutoHidden() ? "恢复系统任务栏常驻" : "自动隐藏系统任务栏") + "  (" + _taskbarHotkeyText + ")";
            tb.Click += delegate { ToggleMsTaskbar(); };
            menu.Items.Add(tb);

            menu.Items.Add(new Separator());

            MenuItem add = new MenuItem();
            add.Header = "添加应用…";
            add.Click += delegate { AddApp(); };
            menu.Items.Add(add);

            MenuItem auto = new MenuItem();
            auto.Header = "开机自动启动";
            auto.IsCheckable = true;
            auto.IsChecked = _cfg.AutoStart;
            auto.Click += delegate
            {
                _cfg.AutoStart = !_cfg.AutoStart;
                ConfigStore.Save(_cfg);
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                AutoStart.Set(_cfg.AutoStart, exe);
                RebuildMainMenuState();
            };
            menu.Items.Add(auto);

            MenuItem fs = new MenuItem();
            fs.Header = "全屏应用时自动隐藏";
            fs.IsCheckable = true;
            fs.IsChecked = _cfg.HideInFullscreen;
            fs.Click += delegate
            {
                _cfg.HideInFullscreen = !_cfg.HideInFullscreen;
                ConfigStore.Save(_cfg);
            };
            menu.Items.Add(fs);

            MenuItem corner = new MenuItem();
            corner.Header = "显示左上角按钮";
            corner.IsCheckable = true;
            corner.IsChecked = !_cfg.HideCornerButton;
            corner.Click += delegate
            {
                _cfg.HideCornerButton = !_cfg.HideCornerButton;
                ConfigStore.Save(_cfg);
                EnsureCornerButton();
                if (_foldersVisible && _corner != null) _corner.Hide();
            };
            menu.Items.Add(corner);

            MenuItem compact = new MenuItem();
            compact.Header = "简洁模式（底部只留搜索）";
            compact.IsCheckable = true;
            compact.IsChecked = _cfg.CompactDock;
            compact.Click += delegate
            {
                _cfg.CompactDock = !_cfg.CompactDock;
                ConfigStore.Save(_cfg);
                _centered = true;
                Rebuild();
                UpdateAppearance();
                PlaceAuto();
                PositionDots();
            };
            menu.Items.Add(compact);

            MenuItem dotToggle = new MenuItem();
            dotToggle.Header = "显示左侧窗口切换点";
            dotToggle.IsCheckable = true;
            dotToggle.IsChecked = !_cfg.HideSwitcherDots;
            dotToggle.Click += delegate
            {
                _cfg.HideSwitcherDots = !_cfg.HideSwitcherDots;
                ConfigStore.Save(_cfg);
                EnsureSwitcherDots();
            };
            menu.Items.Add(dotToggle);

            MenuItem size = new MenuItem();
            size.Header = "图标大小";
            int[] sizes = new int[] { 24, 28, 32, 36, 40 };
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                MenuItem si = new MenuItem();
                si.Header = s.ToString() + " px";
                si.IsCheckable = true;
                si.IsChecked = _cfg.IconSize == s;
                si.Click += delegate
                {
                    _cfg.IconSize = s;
                    ConfigStore.Save(_cfg);
                    Rebuild();
                    UpdateAppearance();
                };
                size.Items.Add(si);
            }
            menu.Items.Add(size);

            menu.Items.Add(new Separator());

            MenuItem reset = new MenuItem();
            reset.Header = "恢复默认位置";
            reset.Click += delegate
            {
                _centered = true;
                _cfg.Left = null;
                _cfg.Top = null;
                ConfigStore.Save(_cfg);
                PlaceAuto();
            };
            menu.Items.Add(reset);

            MenuItem open = new MenuItem();
            open.Header = "打开配置文件";
            open.Click += delegate
            {
                try { Process.Start("notepad.exe", "\"" + ConfigStore.ConfigPath + "\""); } catch { }
            };
            menu.Items.Add(open);

            MenuItem reload = new MenuItem();
            reload.Header = "重新加载配置";
            reload.Click += delegate { ReloadConfig(); };
            menu.Items.Add(reload);

            menu.Items.Add(new Separator());

            MenuItem quit = new MenuItem();
            quit.Header = "退出悬浮栏";
            quit.Click += delegate { Application.Current.Shutdown(); };
            menu.Items.Add(quit);

            return menu;
        }

        private void RebuildMainMenuState()
        {
            // main menu is rebuilt on every open; nothing to do here
        }

        private void OnPillRightClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ContextMenu menu = BuildMainMenu();
            menu.PlacementTarget = _pill;
            menu.IsOpen = true;
        }

        private void AddApp()
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "选择要固定到悬浮栏的应用";
            dlg.Filter = "应用 (*.exe;*.lnk)|*.exe;*.lnk|所有文件 (*.*)|*.*";
            dlg.CheckFileExists = true;
            bool? ok = dlg.ShowDialog(this);
            if (ok != true) return;
            string p = dlg.FileName;
            bool exists = false;
            for (int i = 0; i < _cfg.Pinned.Count; i++)
                if (string.Equals(_cfg.Pinned[i], p, StringComparison.OrdinalIgnoreCase)) exists = true;
            if (!exists)
            {
                _cfg.Pinned.Add(p);
                ConfigStore.Save(_cfg);
                Rebuild();
                UpdateAppearance();
            }
        }

        private void ReloadConfig()
        {
            DockConfig fresh = ConfigStore.Load();
            _cfg.Pinned.Clear();
            for (int i = 0; i < fresh.Pinned.Count; i++) _cfg.Pinned.Add(fresh.Pinned[i]);
            _cfg.DockHotkey = fresh.DockHotkey;
            _cfg.TaskbarHotkey = fresh.TaskbarHotkey;
            _cfg.FolderHotkey = fresh.FolderHotkey;
            _cfg.SwitcherHotkey = fresh.SwitcherHotkey;
            _cfg.SearchHotkey = fresh.SearchHotkey;
            _cfg.SearchFolders.Clear();
            for (int i = 0; i < fresh.SearchFolders.Count; i++) _cfg.SearchFolders.Add(fresh.SearchFolders[i]);
            _cfg.HideInFullscreen = fresh.HideInFullscreen;
            _cfg.IconSize = fresh.IconSize;
            _cfg.AutoStart = fresh.AutoStart;
            _cfg.HideCornerButton = fresh.HideCornerButton;
            _cfg.HideSwitcherDots = fresh.HideSwitcherDots;
            _cfg.CompactDock = fresh.CompactDock;
            RegisterHotkeys();
            EnsureCornerButton();
            EnsureSwitcherDots();
            Rebuild();
            UpdateAppearance();
            Toast("配置已重新加载");
        }

        // ------------------------------------------------------------------ dragging

        private void OnPillMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            e.Handled = true;
            try
            {
                DragMove();
            }
            catch { }
            SnapAndSave();
        }

        private void SnapAndSave()
        {
            Rect wa = WorkArea;

            if (_cfg.CompactDock)
            {
                double px = wa.Left + 10 - 20;
                double py = (_cfg.HideCornerButton ? wa.Top + 10 : wa.Top + 58) - 20;
                if (Math.Abs(Left - px) < 70) { Left = px; _centered = true; } else _centered = false;
                if (Math.Abs(Top - py) < 70) Top = py;
            }
            else
            {
                double centerX = wa.Left + (wa.Width - ActualWidth) / 2.0;
                double bottomY = wa.Top + wa.Height - ActualHeight - 4;
                if (Math.Abs(Left - centerX) < 60) { Left = centerX; _centered = true; }
                else _centered = false;
                if (Math.Abs(Top - bottomY) < 48) Top = bottomY;
            }

            if (_centered)
            {
                _cfg.Left = null;
                _cfg.Top = null;
            }
            else
            {
                _cfg.Left = (int)Math.Round(Left);
                _cfg.Top = (int)Math.Round(Top);
            }
            ConfigStore.Save(_cfg);
        }

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (_scroll == null) return;
            if (_scroll.ScrollableWidth <= 0) return;
            _scroll.ScrollToHorizontalOffset(_scroll.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        // ------------------------------------------------------------------ hotkeys & taskbar

        private void RegisterHotkeys()
        {
            UnregisterHotkeys();
            _dockHotkeyText = RegisterOne(1, _cfg.DockHotkey, new string[] { "Ctrl+Alt+Space", "Alt+`", "Ctrl+Alt+L" }, "悬浮栏");
            _taskbarHotkeyText = RegisterOne(2, _cfg.TaskbarHotkey, new string[] { "Ctrl+Alt+B", "Ctrl+Alt+Shift+T", "Ctrl+Alt+H" }, "系统任务栏");
            _folderHotkeyText = RegisterOne(3, _cfg.FolderHotkey, new string[] { "Ctrl+Alt+F", "Ctrl+Alt+J", "Ctrl+Shift+F" }, "快捷文件夹");
            _switcherHotkeyText = RegisterOne(4, _cfg.SwitcherHotkey, new string[] { "Ctrl+Alt+W", "Ctrl+Alt+Q", "Ctrl+Alt+Shift+W" }, "窗口切换器");
            _searchHotkeyText = RegisterOne(5, _cfg.SearchHotkey, new string[] { "Ctrl+Alt+S", "Ctrl+Alt+G", "Ctrl+Shift+S" }, "目标文件夹搜索");
            Rebuild();
        }

        private string RegisterOne(int id, string configured, string[] fallbacks, string label)
        {
            int mods, vk;
            if (HotkeyUtil.TryParse(configured, out mods, out vk) &&
                N.RegisterHotKey(_hwnd, id, mods | N.MOD_NOREPEAT, vk))
                return configured;

            ConfigStore.Log("hotkey " + id + " unavailable: " + configured);
            for (int i = 0; i < fallbacks.Length; i++)
            {
                string f = fallbacks[i];
                if (HotkeyUtil.TryParse(f, out mods, out vk) &&
                    N.RegisterHotKey(_hwnd, id, mods | N.MOD_NOREPEAT, vk))
                {
                    _hotkeyNotice = label + "热键 " + configured + " 已被占用，改用 " + f;
                    return f;
                }
            }
            _hotkeyNotice = label + "热键 " + configured + " 注册失败";
            return configured;
        }

        private void UnregisterHotkeys()
        {
            if (_hwnd == IntPtr.Zero) return;
            N.UnregisterHotKey(_hwnd, 1);
            N.UnregisterHotKey(_hwnd, 2);
            N.UnregisterHotKey(_hwnd, 3);
            N.UnregisterHotKey(_hwnd, 4);
            N.UnregisterHotKey(_hwnd, 5);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == N.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == 1) ToggleDock(true);
                else if (id == 2) ToggleMsTaskbar();
                else if (id == 3) OpenFolders();
                else if (id == 4) OpenSwitcher();
                else if (id == 5) OpenSearch();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ToggleDock(bool fromHotkey)
        {
            _userVisible = !_userVisible;
            if (_userVisible)
            {
                _suppressFullscreenUntil = DateTime.Now.AddSeconds(6);
                _hiddenByFullscreen = false;
            }
            UpdateVisibility();
            RefreshAuxWindows();
            if (fromHotkey) Toast(_userVisible ? "已显示所有图标" : "已隐藏所有图标  (" + _dockHotkeyText + " 唤回)");
        }

        private static bool GetTaskbarAutoHidden()
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            long state = (long)N.SHAppBarMessage(N.ABM_GETSTATE, ref abd);
            return (state & N.ABS_AUTOHIDE) != 0;
        }

        private void ToggleMsTaskbar()
        {
            IntPtr tray = N.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero)
            {
                Toast("未找到系统任务栏");
                return;
            }
            bool autoHidden = GetTaskbarAutoHidden();
            SetTaskbarAutoHide(!autoHidden);
            Toast(autoHidden ? "系统任务栏：常驻显示" : "系统任务栏：自动隐藏");
        }

        private static void SetTaskbarAutoHide(bool autoHide)
        {
            List<IntPtr> bars = new List<IntPtr>();
            IntPtr main = N.FindWindow("Shell_TrayWnd", null);
            if (main != IntPtr.Zero) bars.Add(main);
            N.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                string cls = N.GetWindowClass(h);
                if (string.Equals(cls, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase)) bars.Add(h);
                return true;
            }, IntPtr.Zero);

            for (int i = 0; i < bars.Count; i++)
            {
                APPBARDATA abd = new APPBARDATA();
                abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
                abd.hWnd = bars[i];
                abd.lParam = (IntPtr)(autoHide ? N.ABS_AUTOHIDE : N.ABS_ALWAYSONTOP);
                N.SHAppBarMessage(N.ABM_SETSTATE, ref abd);
                if (!autoHide) N.ShowWindow(bars[i], N.SW_SHOW);
            }
        }

        // ------------------------------------------------------------------ toast

        public void Toast(string text)
        {
            try
            {
                ToastWindow t = new ToastWindow(text, this);
                t.Show();
            }
            catch { }
        }

        private sealed class ToastWindow : Window
        {
            public ToastWindow(string text, DockWindow owner)
            {
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = Brushes.Transparent;
                ShowInTaskbar = false;
                Topmost = true;
                ShowActivated = false;
                IsHitTestVisible = false;
                SizeToContent = SizeToContent.WidthAndHeight;
                WindowStartupLocation = WindowStartupLocation.Manual;
                FontFamily = owner.FontFamily;

                Border b = new Border();
                b.CornerRadius = new CornerRadius(10);
                b.Background = Frozen(Color.FromArgb(0xF2, 0x21, 0x22, 0x28));
                b.BorderBrush = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
                b.BorderThickness = new Thickness(1);
                b.Padding = new Thickness(16, 9, 16, 9);

                TextBlock tb = new TextBlock();
                tb.Text = text;
                tb.Foreground = Brushes.White;
                tb.FontSize = 12.5;
                b.Child = tb;

                Content = b;
                Opacity = 0;

                Loaded += delegate
                {
                    UpdateLayout();
                    Rect wa = SystemParameters.WorkArea;
                    double x = owner.Left + (owner.ActualWidth - ActualWidth) / 2.0;
                    double y = owner.Top - ActualHeight - 12;
                    if (double.IsNaN(y) || y < wa.Top + 8) y = owner.Top + owner.ActualHeight + 12;
                    if (double.IsNaN(x)) x = wa.Left + 8;
                    if (x < wa.Left + 8) x = wa.Left + 8;
                    if (x + ActualWidth > wa.Right - 8) x = wa.Right - ActualWidth - 8;
                    if (y + ActualHeight > wa.Bottom - 8) y = wa.Top + 8;
                    Left = x;
                    Top = y;

                    DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120));
                    BeginAnimation(OpacityProperty, fadeIn);

                    DispatcherTimer timer = new DispatcherTimer();
                    timer.Interval = TimeSpan.FromMilliseconds(1500);
                    timer.Tick += delegate(object s, EventArgs e)
                    {
                        timer.Stop();
                        DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                        fadeOut.Completed += delegate { Close(); };
                        BeginAnimation(OpacityProperty, fadeOut);
                    };
                    timer.Start();
                };
            }
        }
    }
}
