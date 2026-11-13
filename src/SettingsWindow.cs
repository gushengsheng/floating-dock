using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FloatingDock
{
    /// <summary>Settings dialog: hotkeys, icon size and the little on/off switches.</summary>
    public sealed class SettingsWindow : Window
    {
        private static readonly Brush TextMain = Brushes.White;
        private static readonly Brush TextDim = Frozen(Color.FromArgb(0xFF, 0x9A, 0x9F, 0xAA));
        private static readonly Brush FieldBg = Frozen(Color.FromArgb(0xFF, 0x11, 0x12, 0x16));
        private static readonly Brush FieldBorder = Frozen(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
        private static readonly Brush Accent = Frozen(Color.FromArgb(0xFF, 0x2F, 0x5B, 0xA8));
        private static readonly Brush ChipBg = Frozen(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
        private static readonly Brush ChipHover = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        private static readonly Brush ErrorColor = Frozen(Color.FromArgb(0xFF, 0xF2, 0x8B, 0x82));

        private static SolidColorBrush Frozen(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private sealed class Toggle
        {
            public Border Border;
            public TextBlock Label;
            public string Text;
            public bool Value;
        }

        private readonly DockConfig _cfg;
        private readonly Action _onApply;

        private TextBox _folderBox;
        private TextBox _switcherBox;
        private TextBox _dockBox;
        private TextBox _taskbarBox;
        private TextBox _searchBox;
        private readonly List<Toggle> _toggles = new List<Toggle>();
        private readonly List<Border> _sizeChips = new List<Border>();
        private TextBlock _error;
        private int _iconSize;
        private Grid _grid;
        private int _row;

        public SettingsWindow(DockConfig cfg, Action onApply)
        {
            _cfg = cfg;
            _onApply = onApply;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, sans-serif");
            UseLayoutRounding = true;

            Border panel = new Border();
            panel.Width = 600;
            panel.CornerRadius = new CornerRadius(16);
            panel.Background = Frozen(Color.FromArgb(0xFF, 0x18, 0x19, 0x1F));
            panel.BorderBrush = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            panel.BorderThickness = new Thickness(1);
            panel.Padding = new Thickness(24, 20, 24, 18);
            DropShadowEffect sh = new DropShadowEffect();
            sh.BlurRadius = 30;
            sh.ShadowDepth = 8;
            sh.Opacity = 0.6;
            sh.Color = Colors.Black;
            panel.Effect = sh;

            StackPanel root = new StackPanel();

            TextBlock title = new TextBlock();
            title.Text = "设置";
            title.FontSize = 16;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = TextMain;
            root.Children.Add(title);

            TextBlock subtitle = new TextBlock();
            subtitle.Text = "快捷键可以自己改：Ctrl / Alt / Shift / Win 组合，例如 Ctrl+Alt+F、Win+`、Alt+Shift+Q";
            subtitle.FontSize = 11;
            subtitle.Foreground = TextDim;
            subtitle.Margin = new Thickness(0, 4, 0, 14);
            subtitle.TextWrapping = TextWrapping.Wrap;
            root.Children.Add(subtitle);

            _grid = new Grid();
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(_grid);

            _folderBox = AddHotkeyRow("快捷文件夹", cfg.FolderHotkey);
            _switcherBox = AddHotkeyRow("窗口切换器", cfg.SwitcherHotkey);
            _searchBox = AddHotkeyRow("目标文件夹搜索", cfg.SearchHotkey);
            _dockBox = AddHotkeyRow("悬浮栏显示/隐藏", cfg.DockHotkey);
            _taskbarBox = AddHotkeyRow("系统任务栏切换", cfg.TaskbarHotkey);

            AddSeparator();

            AddToggleRow("开机自动启动", cfg.AutoStart);
            AddToggleRow("全屏应用时自动隐藏悬浮栏", cfg.HideInFullscreen);
            AddToggleRow("显示左上角文件夹按钮", !cfg.HideCornerButton);
            AddToggleRow("显示左侧窗口切换点", !cfg.HideSwitcherDots);
            AddToggleRow("简洁模式（底部只留搜索）", cfg.CompactDock);

            AddSeparator();

            AddSizeRow();

            _error = new TextBlock();
            _error.FontSize = 11.5;
            _error.Foreground = ErrorColor;
            _error.TextWrapping = TextWrapping.Wrap;
            _error.Margin = new Thickness(0, 12, 0, 0);
            _error.Visibility = Visibility.Collapsed;
            root.Children.Add(_error);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 16, 0, 0);

            Border cancel = Chip("取消", ChipBg);
            cancel.Margin = new Thickness(0, 0, 8, 0);
            cancel.MouseLeftButtonDown += delegate { Close(); };
            buttons.Children.Add(cancel);

            Border save = Chip("保存并应用", Accent);
            save.MouseLeftButtonDown += delegate { Save(); };
            buttons.Children.Add(save);

            root.Children.Add(buttons);

            panel.Child = root;
            Content = panel;

            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Close();
                }
            };
        }

        // ------------------------------------------------------------------ rows

        private TextBox AddHotkeyRow(string label, string value)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock l = new TextBlock();
            l.Text = label;
            l.FontSize = 12;
            l.Foreground = TextDim;
            l.VerticalAlignment = VerticalAlignment.Center;
            l.Margin = new Thickness(0, 6, 0, 6);
            Grid.SetRow(l, _row);
            Grid.SetColumn(l, 0);
            _grid.Children.Add(l);

            TextBox box = new TextBox();
            box.Text = value ?? "";
            box.FontSize = 12.5;
            box.Padding = new Thickness(9, 5, 9, 5);
            box.Background = FieldBg;
            box.Foreground = Brushes.White;
            box.CaretBrush = Brushes.White;
            box.BorderBrush = FieldBorder;
            box.BorderThickness = new Thickness(1);
            box.Margin = new Thickness(0, 4, 0, 4);
            box.MaxWidth = 260;
            box.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(box, _row);
            Grid.SetColumn(box, 1);
            _grid.Children.Add(box);

            _row++;
            return box;
        }

        private void AddSeparator()
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border line = new Border();
            line.Height = 1;
            line.Background = Frozen(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
            line.Margin = new Thickness(0, 12, 0, 12);
            Grid.SetRow(line, _row);
            Grid.SetColumnSpan(line, 2);
            _grid.Children.Add(line);

            _row++;
        }

        private void AddToggleRow(string text, bool value)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Toggle t = new Toggle();
            t.Text = text;
            t.Value = value;

            Border chip = new Border();
            chip.CornerRadius = new CornerRadius(8);
            chip.Padding = new Thickness(11, 6, 11, 6);
            chip.Cursor = Cursors.Hand;
            chip.Margin = new Thickness(0, 3, 0, 3);
            chip.HorizontalAlignment = HorizontalAlignment.Left;
            chip.Background = value ? Accent : ChipBg;

            TextBlock label = new TextBlock();
            label.FontSize = 12;
            label.Foreground = TextMain;
            label.Text = (value ? "✓  " : "○  ") + text;
            chip.Child = label;

            chip.MouseLeftButtonDown += delegate
            {
                t.Value = !t.Value;
                label.Text = (t.Value ? "✓  " : "○  ") + t.Text;
                chip.Background = t.Value ? Accent : ChipBg;
            };

            t.Border = chip;
            t.Label = label;
            _toggles.Add(t);

            Grid.SetRow(chip, _row);
            Grid.SetColumn(chip, 1);
            _grid.Children.Add(chip);

            _row++;
        }

        private void AddSizeRow()
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock l = new TextBlock();
            l.Text = "图标大小";
            l.FontSize = 12;
            l.Foreground = TextDim;
            l.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(l, _row);
            Grid.SetColumn(l, 0);
            _grid.Children.Add(l);

            StackPanel sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            sp.Margin = new Thickness(0, 4, 0, 4);

            int[] sizes = new int[] { 24, 28, 32, 36, 40 };
            _iconSize = _cfg.IconSize;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                Border chip = Chip(s + " px", ChipBg);
                chip.Margin = new Thickness(0, 0, 6, 0);
                chip.Tag = s;
                chip.MouseLeftButtonDown += delegate
                {
                    _iconSize = s;
                    UpdateSizeChips();
                };
                _sizeChips.Add(chip);
                sp.Children.Add(chip);
            }

            Grid.SetRow(sp, _row);
            Grid.SetColumn(sp, 1);
            _grid.Children.Add(sp);

            _row++;
            UpdateSizeChips();
        }

        private void UpdateSizeChips()
        {
            for (int i = 0; i < _sizeChips.Count; i++)
            {
                Border chip = _sizeChips[i];
                int size = (int)chip.Tag;
                chip.Background = size == _iconSize ? Accent : ChipBg;
            }
        }

        private Border Chip(string text, Brush background)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(14, 6, 14, 6);
            b.Background = background;
            b.Cursor = Cursors.Hand;

            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 12;
            t.Foreground = TextMain;
            b.Child = t;

            b.MouseEnter += delegate { if (b.Background != Accent) b.Background = ChipHover; };
            b.MouseLeave += delegate { if (b.Background != Accent) b.Background = ChipBg; };
            return b;
        }

        // ------------------------------------------------------------------ save

        private void Save()
        {
            string folder = (_folderBox.Text ?? "").Trim();
            string switcher = (_switcherBox.Text ?? "").Trim();
            string search = (_searchBox.Text ?? "").Trim();
            string dock = (_dockBox.Text ?? "").Trim();
            string taskbar = (_taskbarBox.Text ?? "").Trim();

            string problem = CheckHotkey("快捷文件夹", folder);
            if (problem == null) problem = CheckHotkey("窗口切换器", switcher);
            if (problem == null) problem = CheckHotkey("目标文件夹搜索", search);
            if (problem == null) problem = CheckHotkey("悬浮栏显示/隐藏", dock);
            if (problem == null) problem = CheckHotkey("系统任务栏切换", taskbar);

            if (problem == null)
            {
                string[] names = new string[] { "快捷文件夹", "窗口切换器", "目标文件夹搜索", "悬浮栏显示/隐藏", "系统任务栏切换" };
                string[] values = new string[] { folder, switcher, search, dock, taskbar };
                for (int i = 0; i < values.Length && problem == null; i++)
                {
                    for (int j = i + 1; j < values.Length && problem == null; j++)
                    {
                        if (string.Equals(values[i], values[j], StringComparison.OrdinalIgnoreCase))
                            problem = "「" + names[i] + "」和「" + names[j] + "」用了同一个快捷键";
                    }
                }
            }

            if (problem != null)
            {
                _error.Text = problem;
                _error.Visibility = Visibility.Visible;
                return;
            }

            _cfg.FolderHotkey = folder;
            _cfg.SwitcherHotkey = switcher;
            _cfg.SearchHotkey = search;
            _cfg.DockHotkey = dock;
            _cfg.TaskbarHotkey = taskbar;
            _cfg.IconSize = _iconSize;
            foreach (Toggle t in _toggles)
            {
                if (t.Text == "开机自动启动") _cfg.AutoStart = t.Value;
                else if (t.Text.StartsWith("全屏应用")) _cfg.HideInFullscreen = t.Value;
                else if (t.Text.StartsWith("显示左上角文件夹按钮")) _cfg.HideCornerButton = !t.Value;
                else if (t.Text.StartsWith("显示左侧窗口切换点")) _cfg.HideSwitcherDots = !t.Value;
                else if (t.Text.StartsWith("简洁模式")) _cfg.CompactDock = t.Value;
            }
            ConfigStore.Save(_cfg);

            try { DialogResult = true; }
            catch { Close(); }

            if (_onApply != null) _onApply();
        }

        private string CheckHotkey(string label, string value)
        {
            if (string.IsNullOrEmpty(value)) return "请填写「" + label + "」的快捷键";
            int mods, vk;
            if (!HotkeyUtil.TryParse(value, out mods, out vk))
                return "「" + value + "」不是有效的快捷键。示例：Ctrl+Alt+F、Win+`、Alt+Shift+Q";
            if (mods == 0)
                return "「" + label + "」需要至少一个修饰键（Ctrl / Alt / Shift / Win）";
            return null;
        }
    }
}
