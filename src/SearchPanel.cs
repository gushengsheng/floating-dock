using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace FloatingDock
{
    /// <summary>Search over files inside user-picked target folders.</summary>
    public sealed class SearchPanel : Window
    {
        private static readonly Brush PanelBg = Frozen(Color.FromArgb(0xF6, 0x15, 0x16, 0x1B));
        private static readonly Brush PaneBorder = Frozen(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));
        private static readonly Brush RowHover = Frozen(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
        private static readonly Brush RowSelected = Frozen(Color.FromArgb(0x44, 0x6E, 0x9C, 0xEF));
        private static readonly Brush TextMain = Brushes.White;
        private static readonly Brush TextDim = Frozen(Color.FromArgb(0xFF, 0x92, 0x98, 0xA3));
        private static readonly Brush TextFaint = Frozen(Color.FromArgb(0xFF, 0x6B, 0x70, 0x7A));
        private static readonly Brush ChipBg = Frozen(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));

        private static SolidColorBrush Frozen(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private sealed class Row
        {
            public Border Border;
            public SearchIndex.Item Item;
        }

        private readonly DockConfig _cfg;
        private readonly SearchIndex _index = new SearchIndex();
        private readonly List<Row> _rows = new List<Row>();

        private TextBox _box;
        private StackPanel _list;
        private TextBlock _status;
        private DispatcherTimer _debounce;
        private int _selected;
        private bool _suppressClose;
        private int _lastBuiltFolderCount = -1;

        public SearchPanel(DockConfig cfg)
        {
            _cfg = cfg;

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

            _debounce = new DispatcherTimer();
            _debounce.Interval = TimeSpan.FromMilliseconds(140);
            _debounce.Tick += delegate
            {
                _debounce.Stop();
                RefreshResults();
            };

            Deactivated += delegate
            {
                if (!_suppressClose) ClosePanel();
            };
        }

        // ------------------------------------------------------------------ ui

        private void BuildUi()
        {
            Border panel = new Border();
            panel.Margin = new Thickness(18);
            panel.CornerRadius = new CornerRadius(16);
            panel.Background = PanelBg;
            panel.BorderBrush = PaneBorder;
            panel.BorderThickness = new Thickness(1);

            DropShadowEffect sh = new DropShadowEffect();
            sh.BlurRadius = 28;
            sh.ShadowDepth = 6;
            sh.Opacity = 0.6;
            sh.Color = Colors.Black;
            panel.Effect = sh;

            Grid inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

            inner.Children.Add(BuildSearchRow());
            Grid.SetRow(inner.Children[0], 0);

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.CanContentScroll = false;
            scroll.Margin = new Thickness(6, 4, 6, 4);
            scroll.Background = Brushes.Transparent;

            _list = new StackPanel();
            scroll.Content = _list;
            Grid.SetRow(scroll, 1);
            inner.Children.Add(scroll);

            FrameworkElement footer = BuildFooter();
            Grid.SetRow(footer, 2);
            inner.Children.Add(footer);

            panel.Child = inner;

            Grid root = new Grid();
            root.Children.Add(panel);
            Content = root;
        }

        private FrameworkElement BuildSearchRow()
        {
            Border b = new Border();
            b.BorderThickness = new Thickness(0, 0, 0, 1);
            b.BorderBrush = PaneBorder;
            b.Padding = new Thickness(18, 0, 14, 0);

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            System.Windows.Shapes.Path glass = new System.Windows.Shapes.Path();
            glass.Data = Geometry.Parse("M10,3.5 A6.5,6.5 0 1 1 9.99,3.5 Z M14.6,14.6 L19,19");
            glass.Stroke = Frozen(Color.FromArgb(0xFF, 0x9A, 0x9F, 0xAA));
            glass.StrokeThickness = 1.7;
            glass.StrokeStartLineCap = PenLineCap.Round;
            glass.StrokeEndLineCap = PenLineCap.Round;
            glass.Width = 22;
            glass.Height = 22;
            glass.Margin = new Thickness(0, 0, 12, 0);
            glass.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(glass, 0);
            g.Children.Add(glass);

            _box = new TextBox();
            _box.FontSize = 15;
            _box.Background = Brushes.Transparent;
            _box.Foreground = Brushes.White;
            _box.CaretBrush = Brushes.White;
            _box.BorderThickness = new Thickness(0);
            _box.VerticalContentAlignment = VerticalAlignment.Center;
            _box.TextChanged += delegate { _debounce.Stop(); _debounce.Start(); };
            Grid.SetColumn(_box, 1);
            g.Children.Add(_box);

            TextBlock hint = new TextBlock();
            hint.Text = "Esc 关闭";
            hint.FontSize = 10.5;
            hint.Foreground = TextFaint;
            hint.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(hint, 2);
            g.Children.Add(hint);

            b.Child = g;
            return b;
        }

        private FrameworkElement BuildFooter()
        {
            Border b = new Border();
            b.BorderThickness = new Thickness(0, 1, 0, 0);
            b.BorderBrush = PaneBorder;
            b.Padding = new Thickness(18, 0, 12, 0);

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _status = new TextBlock();
            _status.Text = "";
            _status.FontSize = 10.5;
            _status.Foreground = TextFaint;
            _status.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_status, 0);
            g.Children.Add(_status);

            Border folders = new Border();
            folders.CornerRadius = new CornerRadius(7);
            folders.Background = ChipBg;
            folders.Padding = new Thickness(10, 4, 10, 4);
            folders.Cursor = Cursors.Hand;
            folders.VerticalAlignment = VerticalAlignment.Center;
            TextBlock ft = new TextBlock();
            ft.Text = "目标文件夹";
            ft.FontSize = 11;
            ft.Foreground = Frozen(Color.FromArgb(0xFF, 0xDC, 0xDF, 0xE5));
            folders.Child = ft;
            folders.MouseEnter += delegate { folders.Background = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)); };
            folders.MouseLeave += delegate { folders.Background = ChipBg; };
            folders.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                OpenFolderMenu(folders);
            };
            Grid.SetColumn(folders, 1);
            g.Children.Add(folders);

            b.Child = g;
            return b;
        }

        // ------------------------------------------------------------------ show / close

        public void ShowSearch()
        {
            try
            {
                Rect wa = SystemParameters.WorkArea;
                double w = Math.Min(820, wa.Width - 40);
                double h = Math.Min(480, wa.Height - 160);
                Left = wa.Left + (wa.Width - w) / 2.0;
                Top = wa.Bottom - h - 96;
                Width = w;
                Height = h;

                EnsureIndex();
                if (!IsVisible) Show();
                // a hotkey-opened window cannot take the foreground by itself (Windows foreground lock)
                N.ActivateWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                Activate();
                _box.Focus();
                _box.SelectAll();
                RefreshResults();
                UpdateStatus();
            }
            catch (Exception ex)
            {
                ConfigStore.Log("search show failed: " + ex);
            }
        }

        public void ClosePanel()
        {
            if (!IsVisible) return;
            Hide();
            _box.Text = "";
            _list.Children.Clear();
            _rows.Clear();
            _selected = 0;
        }

        private void EnsureIndex()
        {
            if (_cfg.SearchFolders == null || _cfg.SearchFolders.Count == 0) return;
            bool stale = _index.Count == 0 || _index.BuiltAt == DateTime.MinValue ||
                         (DateTime.Now - _index.BuiltAt).TotalMinutes > 5 ||
                         _lastBuiltFolderCount != _cfg.SearchFolders.Count;
            if (_index.Building || !stale) return;

            _lastBuiltFolderCount = _cfg.SearchFolders.Count;
            UpdateStatus();
            _index.BuildAsync(_cfg.SearchFolders, delegate(int count)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    UpdateStatus();
                    RefreshResults();
                }));
            });
        }

        private void RebuildIndex()
        {
            _index.Invalidate();
            _lastBuiltFolderCount = -1;
            EnsureIndex();
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (_cfg.SearchFolders == null || _cfg.SearchFolders.Count == 0)
            {
                _status.Text = "还没有目标文件夹 —— 点右下角「目标文件夹」添加";
                return;
            }
            if (_index.Building)
            {
                _status.Text = "正在扫描 " + _cfg.SearchFolders.Count + " 个文件夹…";
                return;
            }
            _status.Text = _cfg.SearchFolders.Count + " 个文件夹 · " + _index.Count + " 个文件" +
                           (_index.Capped ? "（已达上限）" : "");
        }

        // ------------------------------------------------------------------ results

        private void RefreshResults()
        {
            if (_list == null) return;
            _list.Children.Clear();
            _rows.Clear();
            _selected = 0;

            string q = _box.Text == null ? "" : _box.Text.Trim();
            if (q.Length == 0)
            {
                if (_cfg.SearchFolders != null && _cfg.SearchFolders.Count > 0 && !_index.Building && _index.Count == 0)
                {
                    _list.Children.Add(Message("索引是空的，可能这些文件夹里没有文件"));
                }
                else
                {
                    _list.Children.Add(Message("输入文件名开始搜索（" + (_cfg.SearchFolders == null ? 0 : _cfg.SearchFolders.Count) + " 个目标文件夹）"));
                }
                return;
            }

            if (_cfg.SearchFolders == null || _cfg.SearchFolders.Count == 0)
            {
                _list.Children.Add(Message("先添加目标文件夹：点右下角「目标文件夹」"));
                return;
            }

            List<SearchIndex.Item> found = _index.Query(q, 40);
            if (found.Count == 0)
            {
                _list.Children.Add(Message(_index.Building ? "扫描中…" : "没有匹配的文件"));
                return;
            }

            for (int i = 0; i < found.Count; i++)
            {
                Border row = CreateRow(found[i]);
                SearchIndex.Item it = found[i];
                Row view = new Row();
                view.Border = row;
                view.Item = it;
                _rows.Add(view);
                _list.Children.Add(row);
            }
            UpdateRows();
        }

        private TextBlock Message(string text)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 12;
            t.Foreground = TextDim;
            t.Margin = new Thickness(14, 14, 14, 14);
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        private Border CreateRow(SearchIndex.Item item)
        {
            Border row = new Border();
            row.Height = 40;
            row.CornerRadius = new CornerRadius(7);
            row.Margin = new Thickness(2, 1, 2, 1);
            row.Padding = new Thickness(10, 0, 10, 0);
            row.Background = Brushes.Transparent;
            row.Cursor = Cursors.Hand;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Image icon = new Image();
            icon.Source = Icons.ForExtension(System.IO.Path.GetExtension(item.Path));
            icon.Width = 18;
            icon.Height = 18;
            icon.Margin = new Thickness(0, 0, 10, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(icon, 0);
            g.Children.Add(icon);

            StackPanel sp = new StackPanel();
            sp.VerticalAlignment = VerticalAlignment.Center;

            TextBlock name = new TextBlock();
            name.Text = item.Name;
            name.FontSize = 12.3;
            name.Foreground = TextMain;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            sp.Children.Add(name);

            TextBlock dir = new TextBlock();
            dir.Text = item.Dir;
            dir.FontSize = 10.2;
            dir.Foreground = TextFaint;
            dir.TextTrimming = TextTrimming.CharacterEllipsis;
            sp.Children.Add(dir);

            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);

            TextBlock open = new TextBlock();
            open.Text = "Enter 打开";
            open.FontSize = 10.2;
            open.Foreground = TextFaint;
            open.VerticalAlignment = VerticalAlignment.Center;
            open.Visibility = Visibility.Collapsed;
            open.Tag = "hint";
            Grid.SetColumn(open, 2);
            g.Children.Add(open);

            row.Child = g;

            Row view = new Row();
            view.Border = row;
            view.Item = item;

            row.MouseEnter += delegate { view.Border.Background = RowHover; open.Visibility = Visibility.Visible; };
            row.MouseLeave += delegate
            {
                int idx = _rows.IndexOf(view);
                view.Border.Background = idx == _selected ? RowSelected : Brushes.Transparent;
                open.Visibility = Visibility.Collapsed;
            };
            row.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                OpenItem(item);
            };

            return row;
        }

        private void UpdateRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                Border b = _rows[i].Border;
                b.Background = i == _selected ? RowSelected : Brushes.Transparent;
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

        private void OpenSelected()
        {
            if (_selected < 0 || _selected >= _rows.Count) return;
            OpenItem(_rows[_selected].Item);
        }

        private void OpenItem(SearchIndex.Item item)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = item.Path;
                psi.UseShellExecute = true;
                Process.Start(psi);
                ClosePanel();
            }
            catch (Exception ex)
            {
                ConfigStore.Log("search open failed: " + ex.Message);
                _status.Text = "打不开：" + ex.Message;
            }
        }

        // ------------------------------------------------------------------ folders

        private void OpenFolderMenu(Border anchor)
        {
            ContextMenu menu = new ContextMenu();
            menu.FontSize = 12;

            if (_cfg.SearchFolders != null && _cfg.SearchFolders.Count > 0)
            {
                for (int i = 0; i < _cfg.SearchFolders.Count; i++)
                {
                    string folder = _cfg.SearchFolders[i];
                    MenuItem mi = new MenuItem();
                    mi.Header = "移除  " + folder;
                    mi.Click += delegate
                    {
                        _cfg.SearchFolders.Remove(folder);
                        ConfigStore.Save(_cfg);
                        RebuildIndex();
                        RefreshResults();
                    };
                    menu.Items.Add(mi);
                }
                menu.Items.Add(new Separator());
            }

            MenuItem add = new MenuItem();
            add.Header = "＋ 添加文件夹…";
            add.Click += delegate { AddFolder(); };
            menu.Items.Add(add);

            menu.PlacementTarget = anchor;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            menu.IsOpen = true;
        }

        private void AddFolder()
        {
            _suppressClose = true;
            bool wasTopmost = Topmost;
            Topmost = false;
            string picked = null;
            try
            {
                System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog();
                dlg.Description = "选择要被搜索的文件夹";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) picked = dlg.SelectedPath;
            }
            catch (Exception ex)
            {
                ConfigStore.Log("search folder picker failed: " + ex.Message);
            }
            Topmost = wasTopmost;
            _suppressClose = false;
            Activate();
            _box.Focus();

            if (string.IsNullOrEmpty(picked)) return;
            if (_cfg.SearchFolders == null) _cfg.SearchFolders = new List<string>();
            for (int i = 0; i < _cfg.SearchFolders.Count; i++)
            {
                if (string.Equals(_cfg.SearchFolders[i], picked, StringComparison.OrdinalIgnoreCase)) return;
            }
            _cfg.SearchFolders.Add(picked);
            ConfigStore.Save(_cfg);
            RebuildIndex();
            RefreshResults();
        }

        // ------------------------------------------------------------------ input

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if ((_box.Text == null || _box.Text.Length == 0)) ClosePanel();
                else { _box.Text = ""; }
                return;
            }
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                OpenSelected();
                return;
            }
            if (e.Key == Key.Up)
            {
                e.Handled = true;
                Move(-1);
                return;
            }
            if (e.Key == Key.Down)
            {
                e.Handled = true;
                Move(1);
                return;
            }
            if (ctrl && e.Key == Key.A)
            {
                e.Handled = true;
                _box.SelectAll();
                return;
            }

            base.OnPreviewKeyDown(e);
        }
    }
}
