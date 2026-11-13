using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace FloatingDock
{
    /// <summary>Full-screen quick folder mode: several folder panes, keyboard driven.</summary>
    public sealed class FolderOverlay : Window
    {
        // ------------------------------------------------------------------ colors

        private static readonly Brush Backdrop = Frozen(Color.FromArgb(0xC6, 0x07, 0x08, 0x0A));
        private static readonly Brush PanelBg = Frozen(Color.FromArgb(0xF4, 0x15, 0x16, 0x1B));
        private static readonly Brush PaneBg = Frozen(Color.FromArgb(0xFF, 0x10, 0x11, 0x15));
        private static readonly Brush PaneBorderBrush = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        private static readonly Brush ActiveBorderBrush = Frozen(Color.FromArgb(0xFF, 0x6E, 0x9C, 0xEF));
        private static readonly Brush RowHover = Frozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        private static readonly Brush SelActive = Frozen(Color.FromArgb(0x44, 0x6E, 0x9C, 0xEF));
        private static readonly Brush SelInactive = Frozen(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
        private static readonly Brush TextMain = Brushes.White;
        private static readonly Brush TextDim = Frozen(Color.FromArgb(0xFF, 0x8E, 0x94, 0x9F));
        private static readonly Brush TextFaint = Frozen(Color.FromArgb(0xFF, 0x6B, 0x70, 0x7A));

        private static SolidColorBrush Frozen(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // ------------------------------------------------------------------ state

        private sealed class Entry
        {
            public string Name;
            public string FullPath;
            public bool IsDir;
            public bool IsParent;
            public long Size;
        }

        private sealed class PaneView
        {
            public Border Frame;
            public StackPanel List;
            public ScrollViewer Scroll;
            public TextBlock TitleText;
            public TextBlock PathText;
            public PaneState State;
            public List<Entry> Entries = new List<Entry>();
            public List<Border> Rows = new List<Border>();
            public int Selected;
            public int HoverIndex = -1;
        }

        private FolderSettings _settings;
        private UniformGrid _paneHost;
        private readonly List<PaneView> _panes = new List<PaneView>();
        private int _active;

        private Border _toast;
        private TextBlock _toastText;
        private DispatcherTimer _toastTimer;

        public event EventHandler Hidden;

        public FolderOverlay()
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

        // ------------------------------------------------------------------ ui

        private void BuildUi()
        {
            Grid root = new Grid();
            root.Background = Backdrop;
            root.MouseLeftButtonDown += delegate { Focus(); };

            Border panel = new Border();
            panel.Margin = new Thickness(26, 26, 26, 26);
            panel.CornerRadius = new CornerRadius(16);
            panel.Background = PanelBg;
            panel.BorderBrush = Frozen(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));
            panel.BorderThickness = new Thickness(1);
            DropShadowEffect sh = new DropShadowEffect();
            sh.BlurRadius = 34;
            sh.ShadowDepth = 6;
            sh.Opacity = 0.65;
            sh.Color = Colors.Black;
            panel.Effect = sh;

            Grid inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

            FrameworkElement header = BuildHeader();
            Grid.SetRow(header, 0);
            inner.Children.Add(header);

            _paneHost = new UniformGrid();
            _paneHost.Margin = new Thickness(8, 4, 8, 4);
            Grid.SetRow(_paneHost, 1);
            inner.Children.Add(_paneHost);

            FrameworkElement footer = BuildFooter();
            Grid.SetRow(footer, 2);
            inner.Children.Add(footer);

            panel.Child = inner;
            root.Children.Add(panel);

            _toast = new Border();
            _toast.CornerRadius = new CornerRadius(10);
            _toast.Background = Frozen(Color.FromArgb(0xF2, 0x24, 0x26, 0x2D));
            _toast.BorderBrush = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            _toast.BorderThickness = new Thickness(1);
            _toast.Padding = new Thickness(16, 8, 16, 8);
            _toast.HorizontalAlignment = HorizontalAlignment.Center;
            _toast.VerticalAlignment = VerticalAlignment.Bottom;
            _toast.Margin = new Thickness(0, 0, 0, 46);
            _toast.IsHitTestVisible = false;
            _toast.Visibility = Visibility.Collapsed;
            _toastText = new TextBlock();
            _toastText.FontSize = 12.5;
            _toastText.Foreground = Brushes.White;
            _toast.Child = _toastText;
            root.Children.Add(_toast);

            _toastTimer = new DispatcherTimer();
            _toastTimer.Interval = TimeSpan.FromMilliseconds(1800);
            _toastTimer.Tick += delegate
            {
                _toastTimer.Stop();
                _toast.Visibility = Visibility.Collapsed;
            };

            Content = root;
        }

        private FrameworkElement BuildHeader()
        {
            Border b = new Border();
            b.Height = 52;
            b.Padding = new Thickness(18, 0, 14, 0);
            b.BorderThickness = new Thickness(0, 0, 0, 1);
            b.BorderBrush = PaneBorderBrush;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;

            Image icon = new Image();
            icon.Source = Icons.ForFolder();
            icon.Width = 19;
            icon.Height = 19;
            icon.Margin = new Thickness(0, 0, 10, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(icon);

            TextBlock title = new TextBlock();
            title.Text = "快捷文件夹";
            title.FontSize = 14.5;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = TextMain;
            title.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(title);

            TextBlock hint = new TextBlock();
            hint.Text = "双击窗口进入 · Tab 切换 · Esc 退出";
            hint.FontSize = 11;
            hint.Foreground = TextFaint;
            hint.Margin = new Thickness(14, 0, 0, 0);
            hint.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(hint);

            Grid.SetColumn(left, 0);
            g.Children.Add(left);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(Chip("＋ 添加窗口", delegate { AddPane(); }));
            Grid.SetColumn(right, 2);
            g.Children.Add(right);

            b.Child = g;
            return b;
        }

        private FrameworkElement BuildFooter()
        {
            Border b = new Border();
            b.Height = 34;
            b.Padding = new Thickness(18, 0, 18, 0);
            b.BorderThickness = new Thickness(0, 1, 0, 0);
            b.BorderBrush = PaneBorderBrush;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock hint = new TextBlock();
            hint.Text = "↑↓ / W S 移动　　←→ / A D 切换窗口　　Enter 打开　　Backspace 上一级　　Ctrl+N 新建（带后缀=文件，不带=文件夹）　　F2 重命名　　Delete 删除";
            hint.FontSize = 10.8;
            hint.Foreground = TextFaint;
            hint.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(hint, 0);
            g.Children.Add(hint);

            TextBlock esc = new TextBlock();
            esc.Text = "Esc 退出";
            esc.FontSize = 10.8;
            esc.Foreground = TextFaint;
            esc.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(esc, 1);
            g.Children.Add(esc);

            b.Child = g;
            return b;
        }

        private Border Chip(string text, Action onClick)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(11, 6, 11, 6);
            b.Background = Frozen(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
            b.Cursor = Cursors.Hand;
            b.Margin = new Thickness(6, 0, 0, 0);
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 11.5;
            t.Foreground = Frozen(Color.FromArgb(0xFF, 0xDC, 0xDF, 0xE5));
            b.Child = t;
            b.MouseEnter += delegate { b.Background = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)); };
            b.MouseLeave += delegate { b.Background = Frozen(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)); };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                onClick();
            };
            return b;
        }

        // ------------------------------------------------------------------ show / hide

        public void ShowOverlay()
        {
            try
            {
                _settings = FolderStore.Load();

                Rect wa = SystemParameters.WorkArea;
                Left = wa.Left;
                Top = wa.Top;
                Width = wa.Width;
                Height = wa.Height;

                RebuildPanes();

                if (!IsVisible) Show();
                N.ActivateWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                Activate();
                Focus();
            }
            catch (Exception ex)
            {
                ConfigStore.Log("overlay show failed: " + ex);
            }
        }

        public void CloseOverlay()
        {
            SaveAll();
            Hide();
            // drop the row visuals so a big folder does not stay in memory while closed
            if (_paneHost != null) _paneHost.Children.Clear();
            _panes.Clear();
            if (Hidden != null) Hidden(this, EventArgs.Empty);
        }

        private void SaveAll()
        {
            if (_settings == null) return;
            for (int i = 0; i < _panes.Count && i < _settings.Panes.Count; i++)
            {
                _settings.Panes[i].Path = _panes[i].State.Path;
                _settings.Panes[i].Selected = _panes[i].Selected;
            }
            FolderStore.Save(_settings);
        }

        // ------------------------------------------------------------------ panes

        private PaneView Active
        {
            get
            {
                if (_panes.Count == 0) return null;
                if (_active < 0 || _active >= _panes.Count) _active = 0;
                return _panes[_active];
            }
        }

        private void RebuildPanes()
        {
            _paneHost.Children.Clear();
            _panes.Clear();

            if (_settings == null || _settings.Panes.Count == 0)
            {
                _settings = FolderStore.Load();
                if (_settings.Panes.Count == 0) return;
            }

            int n = _settings.Panes.Count;
            int cols = n <= 3 ? n : (n <= 4 ? 2 : 3);
            _paneHost.Columns = cols;
            _paneHost.Rows = (int)Math.Ceiling((double)n / cols);

            for (int i = 0; i < n; i++)
            {
                PaneState st = _settings.Panes[i];
                PaneView v = CreatePane(i, st);
                _panes.Add(v);
                _paneHost.Children.Add(v.Frame);
                LoadDirectory(v, st.Path, st.Selected);
            }

            _active = _settings.ActiveIndex;
            if (_active < 0 || _active >= _panes.Count) _active = 0;
            UpdateActiveVisuals();
        }

        private PaneView CreatePane(int index, PaneState st)
        {
            PaneView v = new PaneView();
            v.State = st;

            Border frame = new Border();
            frame.CornerRadius = new CornerRadius(12);
            frame.Background = PaneBg;
            frame.BorderThickness = new Thickness(1.5);
            frame.BorderBrush = PaneBorderBrush;
            frame.Margin = new Thickness(7);
            frame.ClipToBounds = true;
            frame.MouseLeftButtonDown += delegate { SetActive(index); };

            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Border head = new Border();
            head.Padding = new Thickness(10, 0, 6, 0);
            head.BorderThickness = new Thickness(0, 0, 0, 1);
            head.BorderBrush = PaneBorderBrush;
            head.MouseLeftButtonDown += delegate { SetActive(index); };

            Grid hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel titleSp = new StackPanel();
            titleSp.Orientation = Orientation.Horizontal;
            titleSp.VerticalAlignment = VerticalAlignment.Center;

            Image fi = new Image();
            fi.Source = Icons.ForFolder();
            fi.Width = 15;
            fi.Height = 15;
            fi.Margin = new Thickness(0, 0, 6, 0);
            fi.VerticalAlignment = VerticalAlignment.Center;
            titleSp.Children.Add(fi);

            TextBlock title = new TextBlock();
            title.Text = st.Title;
            title.FontSize = 12;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = TextMain;
            title.VerticalAlignment = VerticalAlignment.Center;
            v.TitleText = title;
            titleSp.Children.Add(title);

            Grid.SetColumn(titleSp, 0);
            hg.Children.Add(titleSp);

            TextBlock pathText = new TextBlock();
            pathText.FontSize = 10.5;
            pathText.Foreground = TextFaint;
            pathText.Margin = new Thickness(9, 0, 9, 0);
            pathText.VerticalAlignment = VerticalAlignment.Center;
            pathText.TextTrimming = TextTrimming.CharacterEllipsis;
            v.PathText = pathText;
            Grid.SetColumn(pathText, 1);
            hg.Children.Add(pathText);

            Border menuChip = Chip("…", delegate { OpenPaneMenu(index); });
            menuChip.Padding = new Thickness(8, 2, 8, 2);
            Grid.SetColumn(menuChip, 2);
            hg.Children.Add(menuChip);

            head.Child = hg;
            Grid.SetRow(head, 0);
            g.Children.Add(head);

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.CanContentScroll = false;
            scroll.Background = Brushes.Transparent;
            scroll.Margin = new Thickness(6, 4, 4, 6);
            v.Scroll = scroll;

            StackPanel list = new StackPanel();
            list.Margin = new Thickness(0);
            v.List = list;
            scroll.Content = list;

            Grid.SetRow(scroll, 1);
            g.Children.Add(scroll);

            frame.Child = g;
            v.Frame = frame;
            return v;
        }

        // ------------------------------------------------------------------ directory loading

        private void LoadDirectory(PaneView v, string path, int desired)
        {
            if (v == null) return;

            string dir = path;
            string note = null;

            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                string walk = dir;
                for (int guard = 0; guard < 40 && !string.IsNullOrEmpty(walk) && !Directory.Exists(walk); guard++)
                {
                    try
                    {
                        DirectoryInfo up = Directory.GetParent(walk);
                        walk = up == null ? null : up.FullName;
                    }
                    catch { walk = null; }
                }
                if (!string.IsNullOrEmpty(walk) && Directory.Exists(walk))
                {
                    dir = walk;
                    note = "原文件夹已不存在，已回到上一级";
                }
                else
                {
                    dir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    note = "原文件夹已不存在";
                }
            }

            DirectoryInfo di;
            try { di = new DirectoryInfo(dir); }
            catch (Exception ex)
            {
                ConfigStore.Log("open dir failed: " + ex.Message);
                return;
            }
            v.State.Path = di.FullName;

            v.Entries.Clear();
            v.Rows.Clear();
            v.List.Children.Clear();
            v.HoverIndex = -1;

            DirectoryInfo parent = null;
            try { parent = di.Parent; } catch { }
            if (parent != null)
            {
                Entry pe = new Entry();
                pe.IsParent = true;
                pe.Name = "..";
                pe.FullPath = parent.FullName;
                v.Entries.Add(pe);
            }

            List<Entry> dirs = new List<Entry>();
            List<Entry> files = new List<Entry>();
            try
            {
                FileSystemInfo[] items = di.GetFileSystemInfos();
                for (int i = 0; i < items.Length; i++)
                {
                    FileSystemInfo fsi = items[i];
                    try
                    {
                        FileAttributes a = fsi.Attributes;
                        if ((a & FileAttributes.Hidden) != 0) continue;
                        if ((a & FileAttributes.System) != 0) continue;
                    }
                    catch { continue; }

                    Entry e = new Entry();
                    e.Name = fsi.Name;
                    e.FullPath = fsi.FullName;
                    e.IsDir = fsi is DirectoryInfo;
                    if (fsi is FileInfo)
                    {
                        try { e.Size = ((FileInfo)fsi).Length; } catch { }
                        files.Add(e);
                    }
                    else
                    {
                        dirs.Add(e);
                    }
                }
            }
            catch (Exception ex)
            {
                note = "无法读取此文件夹（" + ex.GetType().Name + "）";
                ConfigStore.Log("read dir failed: " + ex.Message);
            }

            Comparison<Entry> cmp = delegate(Entry x, Entry y)
            {
                return string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase);
            };
            dirs.Sort(cmp);
            files.Sort(cmp);
            v.Entries.AddRange(dirs);
            v.Entries.AddRange(files);

            v.Selected = 0;
            for (int i = 0; i < v.Entries.Count; i++)
            {
                Border row = CreateRow(v, i);
                v.Rows.Add(row);
                v.List.Children.Add(row);
            }

            if (desired > 0 && desired < v.Entries.Count) v.Selected = desired;

            v.PathText.Text = CompactPath(di.FullName);
            v.TitleText.Text = string.IsNullOrEmpty(v.State.Title) ? di.Name : v.State.Title;

            UpdateSelection(v, true);
            if (note != null) Toast(note);
        }

        private static string CompactPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (path.Length <= 58) return path;
            return path.Substring(0, 3) + "…" + path.Substring(path.Length - 52);
        }

        private Border CreateRow(PaneView v, int index)
        {
            Entry en = v.Entries[index];

            Border row = new Border();
            row.Height = 26;
            row.CornerRadius = new CornerRadius(6);
            row.Padding = new Thickness(8, 0, 8, 0);
            row.Margin = new Thickness(2, 1, 2, 1);
            row.Background = Brushes.Transparent;
            row.Cursor = Cursors.Hand;
            row.Tag = index;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement iconEl;
            if (en.IsParent)
            {
                TextBlock up = new TextBlock();
                up.Text = "↑";
                up.FontSize = 12;
                up.Foreground = TextDim;
                iconEl = up;
            }
            else
            {
                Image im = new Image();
                im.Source = en.IsDir ? Icons.ForFolder() : Icons.ForExtension(Path.GetExtension(en.FullPath));
                im.Width = 16;
                im.Height = 16;
                iconEl = im;
            }
            iconEl.Margin = new Thickness(0, 0, 8, 0);
            iconEl.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(iconEl, 0);
            g.Children.Add(iconEl);

            TextBlock name = new TextBlock();
            name.Text = en.IsParent ? "上一级" : en.Name;
            name.FontSize = 12.3;
            name.Foreground = en.IsParent ? TextDim : TextMain;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(name, 1);
            g.Children.Add(name);

            if (!en.IsParent && !en.IsDir)
            {
                TextBlock meta = new TextBlock();
                meta.Text = FormatSize(en.Size);
                meta.FontSize = 10.5;
                meta.Foreground = TextFaint;
                meta.VerticalAlignment = VerticalAlignment.Center;
                meta.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(meta, 2);
                g.Children.Add(meta);
            }

            row.Child = g;

            row.MouseEnter += delegate
            {
                v.HoverIndex = index;
                UpdateRowBackground(v, index);
            };
            row.MouseLeave += delegate
            {
                if (v.HoverIndex == index) v.HoverIndex = -1;
                UpdateRowBackground(v, index);
            };
            row.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                int paneIndex = _panes.IndexOf(v);
                if (paneIndex >= 0) SetActive(paneIndex);
                v.Selected = index;
                UpdateSelection(v, false);
                if (e.ClickCount >= 2) ActivateEntry(v);
                else SaveAll();
            };

            return row;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.#") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.#") + " MB";
            return (mb / 1024.0).ToString("0.##") + " GB";
        }

        // ------------------------------------------------------------------ visuals

        private void UpdateSelection(PaneView v, bool bring)
        {
            for (int i = 0; i < v.Rows.Count; i++) UpdateRowBackground(v, i);
            if (bring && v.Selected >= 0 && v.Selected < v.Rows.Count)
                v.Rows[v.Selected].BringIntoView();
        }

        private void UpdateRowBackground(PaneView v, int i)
        {
            if (i < 0 || i >= v.Rows.Count) return;
            Border row = v.Rows[i];
            bool selected = i == v.Selected;
            bool activePane = _panes.Count > 0 && _active >= 0 && _active < _panes.Count && _panes[_active] == v;
            if (selected) row.Background = activePane ? SelActive : SelInactive;
            else if (i == v.HoverIndex) row.Background = RowHover;
            else row.Background = Brushes.Transparent;
        }

        private void UpdateActiveVisuals()
        {
            for (int i = 0; i < _panes.Count; i++)
            {
                _panes[i].Frame.BorderBrush = (i == _active) ? ActiveBorderBrush : PaneBorderBrush;
                UpdateSelection(_panes[i], false);
            }
        }

        private void SetActive(int index)
        {
            if (index < 0 || index >= _panes.Count) return;
            if (index == _active) return;
            _active = index;
            _settings.ActiveIndex = index;
            UpdateActiveVisuals();
            FolderStore.Save(_settings);
        }

        // ------------------------------------------------------------------ navigation

        private Entry SelectedEntry
        {
            get
            {
                PaneView v = Active;
                if (v == null) return null;
                if (v.Selected < 0 || v.Selected >= v.Entries.Count) return null;
                return v.Entries[v.Selected];
            }
        }

        private void MoveSelection(int delta)
        {
            PaneView v = Active;
            if (v == null || v.Entries.Count == 0) return;
            int next = v.Selected + delta;
            if (next < 0) next = 0;
            if (next >= v.Entries.Count) next = v.Entries.Count - 1;
            if (next == v.Selected) return;
            v.Selected = next;
            UpdateSelection(v, true);
            _settings.Panes[_panes.IndexOf(v)].Selected = next;
            FolderStore.Save(_settings);
        }

        private void ActivateEntry(PaneView v)
        {
            if (v == null || v.Selected < 0 || v.Selected >= v.Entries.Count) return;
            Entry e = v.Entries[v.Selected];
            if (e.IsDir || e.IsParent)
            {
                string child = e.IsParent ? null : e.Name;
                LoadDirectory(v, e.FullPath, 0);
                if (child != null) SelectByName(v, child);
                SaveAll();
            }
            else
            {
                OpenFile(e.FullPath);
            }
        }

        private void GoParent()
        {
            PaneView v = Active;
            if (v == null) return;
            DirectoryInfo di;
            try { di = new DirectoryInfo(v.State.Path); } catch { return; }
            DirectoryInfo p = null;
            try { p = di.Parent; } catch { }
            if (p == null)
            {
                Toast("已经在最上层了");
                return;
            }
            string from = di.Name;
            LoadDirectory(v, p.FullName, 0);
            SelectByName(v, from);
            SaveAll();
        }

        private void EnterFolder()
        {
            Entry e = SelectedEntry;
            if (e == null) return;
            if (e.IsDir || e.IsParent) ActivateEntry(Active);
        }

        private void SelectByName(PaneView v, string name)
        {
            for (int i = 0; i < v.Entries.Count; i++)
            {
                if (string.Equals(v.Entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    v.Selected = i;
                    UpdateSelection(v, true);
                    return;
                }
            }
        }

        // ------------------------------------------------------------------ file actions

        private void OpenFile(string path)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = path;
                psi.UseShellExecute = true;
                Process.Start(psi);
                Toast("已打开 " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                ConfigStore.Log("open file failed: " + ex.Message);
                Toast("无法打开：" + ex.Message);
            }
        }

        /// <summary>Create something: a name with an extension makes a file, a bare name makes a folder.</summary>
        private void NewItem()
        {
            PaneView v = Active;
            if (v == null) return;
            string err = null;
            while (true)
            {
                string name = TextPrompt.Ask(this, "新建",
                    "输入名称。带后缀就是文件（例如 a.txt）；不带后缀就建文件夹（例如 新建文件夹）。", null, err);
                Refocus();
                if (name == null) return;
                name = name.Trim();
                string problem = ValidateName(v, name);
                if (problem != null) { err = problem; continue; }

                bool asFile = LooksLikeFileName(name);
                try
                {
                    string full = Path.Combine(v.State.Path, name);
                    if (asFile)
                    {
                        using (FileStream fs = File.Create(full)) { }
                    }
                    else
                    {
                        Directory.CreateDirectory(full);
                    }
                    LoadDirectory(v, v.State.Path, 0);
                    SelectByName(v, name);
                    SaveAll();
                    Toast((asFile ? "已创建文件 " : "已创建文件夹 ") + name);
                }
                catch (Exception ex)
                {
                    ConfigStore.Log("new item failed: " + ex.Message);
                    err = "创建失败：" + ex.Message;
                    continue;
                }
                return;
            }
        }

        /// <summary>True when the typed name looks like a file name with a real suffix
        /// (a.txt, archive.tar.gz). Bare names like "报告" or "v1.2 草稿" become folders.</summary>
        private static bool LooksLikeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            int dot = name.LastIndexOf('.');
            if (dot <= 0 || dot == name.Length - 1) return false;
            string ext = name.Substring(dot + 1);
            if (ext.Length > 10) return false;
            for (int i = 0; i < ext.Length; i++)
            {
                char c = ext[i];
                if (!char.IsLetterOrDigit(c)) return false;
            }
            return true;
        }

        private void DeleteSelected()        {
            PaneView v = Active;
            if (v == null) return;
            Entry e = SelectedEntry;
            if (e == null) return;
            if (e.IsParent)
            {
                Toast("这一行代表上一级，不能删除");
                return;
            }

            string kind = e.IsDir ? "文件夹" : "文件";
            string message = "确定把" + kind + "「" + e.Name + "」移到回收站吗？\n\n" + e.FullPath;
            bool ok = ConfirmDialog.Ask(this, "删除" + kind, message, "移到回收站");
            Refocus();
            if (!ok) return;

            int index = v.Selected;
            string problem = N.DeleteToRecycleBin(new System.Windows.Interop.WindowInteropHelper(this).Handle, e.FullPath);
            if (problem != null)
            {
                ConfigStore.Log("delete failed: " + problem);
                Toast("删除失败：" + problem);
                return;
            }

            LoadDirectory(v, v.State.Path, 0);
            if (v.Entries.Count > 0)
            {
                if (index >= v.Entries.Count) index = v.Entries.Count - 1;
                v.Selected = index;
                UpdateSelection(v, true);
            }
            SaveAll();
            Toast("已移到回收站：" + e.Name);
        }

        private void NewFolder()
        {
            PaneView v = Active;
            if (v == null) return;
            string err = null;
            while (true)
            {
                string name = TextPrompt.Ask(this, "新建文件夹", "输入文件夹名称。", null, err);
                Refocus();
                if (name == null) return;
                name = name.Trim();
                string problem = ValidateName(v, name);
                if (problem != null) { err = problem; continue; }
                try
                {
                    string full = Path.Combine(v.State.Path, name);
                    Directory.CreateDirectory(full);
                    LoadDirectory(v, v.State.Path, 0);
                    SelectByName(v, name);
                    SaveAll();
                    Toast("已创建文件夹 " + name);
                }
                catch (Exception ex)
                {
                    err = "创建失败：" + ex.Message;
                    continue;
                }
                return;
            }
        }

        private void RenameSelected()
        {
            PaneView v = Active;
            if (v == null) return;
            Entry e = SelectedEntry;
            if (e == null) return;
            if (e.IsParent)
            {
                Toast("这一行代表上一级，不能重命名");
                return;
            }

            string initial = e.Name;
            string err = null;
            while (true)
            {
                string name = TextPrompt.Ask(this, "重命名", "输入新的名称（可带后缀）。", initial, err);
                Refocus();
                if (name == null) return;
                name = name.Trim();
                if (name == e.Name) return;
                string problem = ValidateName(v, name);
                if (problem != null) { err = problem; initial = name; continue; }
                try
                {
                    string target = Path.Combine(v.State.Path, name);
                    if (e.IsDir) Directory.Move(e.FullPath, target);
                    else File.Move(e.FullPath, target);
                    LoadDirectory(v, v.State.Path, 0);
                    SelectByName(v, name);
                    SaveAll();
                    Toast("已重命名为 " + name);
                }
                catch (Exception ex)
                {
                    err = "重命名失败：" + ex.Message;
                    initial = name;
                    continue;
                }
                return;
            }
        }

        private string ValidateName(PaneView v, string name)
        {
            if (string.IsNullOrEmpty(name)) return "名称不能为空";
            if (name == "." || name == "..") return "这个名称不可用";
            char[] bad = Path.GetInvalidFileNameChars();
            for (int i = 0; i < bad.Length; i++)
                if (name.IndexOf(bad[i]) >= 0) return "名称里不能包含这些字符：\\ / : * ? \" < > |";
            if (name.EndsWith(" ") || name.EndsWith(".")) return "名称不能以空格或句点结尾";
            string full;
            try { full = Path.Combine(v.State.Path, name); }
            catch { return "名称不合法"; }
            if (File.Exists(full) || Directory.Exists(full)) return "同名的文件或文件夹已经存在";
            return null;
        }

        // ------------------------------------------------------------------ pane management

        private void OpenPaneMenu(int index)
        {
            SetActive(index);
            ContextMenu menu = new ContextMenu();
            menu.FontSize = 12;

            MenuItem change = new MenuItem();
            change.Header = "更换文件夹…";
            change.Click += delegate { ChangePaneFolder(index); };
            menu.Items.Add(change);

            MenuItem retitle = new MenuItem();
            retitle.Header = "编辑标题…";
            retitle.Click += delegate { EditPaneTitle(index); };
            menu.Items.Add(retitle);

            menu.Items.Add(new Separator());

            MenuItem add = new MenuItem();
            add.Header = "添加窗口…";
            add.Click += delegate { AddPane(); };
            menu.Items.Add(add);

            MenuItem remove = new MenuItem();
            remove.Header = "移除此窗口";
            remove.Click += delegate { RemovePane(index); };
            menu.Items.Add(remove);

            menu.PlacementTarget = _panes[index].Frame;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void AddPane()
        {
            string path = PickFolder(null);
            if (path == null) return;
            PaneState p = new PaneState();
            p.Path = path;
            p.Title = new DirectoryInfo(path).Name;
            if (string.IsNullOrEmpty(p.Title)) p.Title = path;
            _settings.Panes.Add(p);
            FolderStore.Save(_settings);
            RebuildPanes();
            SetActive(_settings.Panes.Count - 1);
            Toast("已添加窗口：" + p.Title);
        }

        private void RemovePane(int index)
        {
            if (_settings.Panes.Count <= 1)
            {
                Toast("至少保留一个窗口");
                return;
            }
            _settings.Panes.RemoveAt(index);
            if (_settings.ActiveIndex >= _settings.Panes.Count) _settings.ActiveIndex = _settings.Panes.Count - 1;
            FolderStore.Save(_settings);
            RebuildPanes();
            Toast("已移除这个窗口");
        }

        private void ChangePaneFolder(int index)
        {
            if (index < 0 || index >= _panes.Count) return;
            string path = PickFolder(_panes[index].State.Path);
            if (path == null) return;
            _panes[index].State.Path = path;
            if (_panes[index].State.Title == new DirectoryInfo(_panes[index].State.Path).Name)
                _panes[index].State.Title = new DirectoryInfo(path).Name;
            _settings.Panes[index].Path = path;
            LoadDirectory(_panes[index], path, 0);
            SaveAll();
            Toast("已切换文件夹");
        }

        private void EditPaneTitle(int index)
        {
            if (index < 0 || index >= _panes.Count) return;
            string title = TextPrompt.Ask(this, "编辑标题", "给这个窗口起个名字。", _panes[index].State.Title, null);
            Refocus();
            if (title == null) return;
            title = title.Trim();
            if (title.Length == 0) return;
            _panes[index].State.Title = title;
            _panes[index].TitleText.Text = title;
            _settings.Panes[index].Title = title;
            FolderStore.Save(_settings);
            Activate();
        }

        private string PickFolder(string initial)
        {
            try
            {
                bool wasTopmost = Topmost;
                Topmost = false;
                System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog();
                dlg.Description = "选择一个文件夹";
                dlg.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial)) dlg.SelectedPath = initial;
                System.Windows.Forms.DialogResult r = dlg.ShowDialog();
                Topmost = wasTopmost;
                Activate();
                Focus();
                if (r == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
                    return dlg.SelectedPath;
                return null;
            }
            catch (Exception ex)
            {
                Topmost = true;
                ConfigStore.Log("folder picker failed: " + ex.Message);
                Toast("打开文件夹选择器失败");
                return null;
            }
        }

        // ------------------------------------------------------------------ toast

        public void Toast(string text)
        {
            if (_toast == null) return;
            _toastText.Text = text;
            _toast.Visibility = Visibility.Visible;
            _toast.Opacity = 1;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void Refocus()
        {
            if (!IsActive) Activate();
            Focus();
        }

        // ------------------------------------------------------------------ keyboard

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseOverlay();
                return;
            }

            if (ctrl && e.Key == Key.N)
            {
                e.Handled = true;
                if (shift) NewFolder(); else NewItem();
                return;
            }
            if (e.Key == Key.Delete)
            {
                e.Handled = true;
                DeleteSelected();
                return;
            }
            if (e.Key == Key.F2)
            {
                e.Handled = true;
                RenameSelected();
                return;
            }
            if (e.Key == Key.O && ctrl)
            {
                e.Handled = true;
                Entry sel = SelectedEntry;
                if (sel != null && !sel.IsDir && !sel.IsParent) OpenFile(sel.FullPath);
                else EnterFolder();
                return;
            }
            if (e.Key == Key.Tab)
            {
                e.Handled = true;
                if (_panes.Count > 1)
                    SetActive((_active + (shift ? _panes.Count - 1 : 1)) % _panes.Count);
                return;
            }

            switch (e.Key)
            {
                case Key.Up:
                case Key.W:
                    e.Handled = true;
                    MoveSelection(-1);
                    break;

                case Key.Down:
                case Key.S:
                    e.Handled = true;
                    MoveSelection(1);
                    break;

                case Key.Left:
                case Key.A:
                    e.Handled = true;
                    if (_panes.Count > 1) SetActive((_active + _panes.Count - 1) % _panes.Count);
                    else GoParent();
                    break;

                case Key.Right:
                case Key.D:
                    e.Handled = true;
                    if (_panes.Count > 1) SetActive((_active + 1) % _panes.Count);
                    else EnterFolder();
                    break;

                case Key.Enter:
                    e.Handled = true;
                    Entry cur = SelectedEntry;
                    if (cur != null && !cur.IsDir && !cur.IsParent) OpenFile(cur.FullPath);
                    else EnterFolder();
                    break;

                case Key.Back:
                    e.Handled = true;
                    GoParent();
                    break;

                case Key.Home:
                    e.Handled = true;
                    if (Active != null && Active.Entries.Count > 0)
                    {
                        Active.Selected = 0;
                        UpdateSelection(Active, true);
                    }
                    break;

                case Key.End:
                    e.Handled = true;
                    if (Active != null && Active.Entries.Count > 0)
                    {
                        Active.Selected = Active.Entries.Count - 1;
                        UpdateSelection(Active, true);
                    }
                    break;
            }

            base.OnPreviewKeyDown(e);
        }

        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            base.OnPreviewKeyUp(e);
        }
    }
}
