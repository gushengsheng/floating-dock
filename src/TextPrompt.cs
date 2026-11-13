using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FloatingDock
{
    /// <summary>Small dark modal input box used for "new file / new folder / rename".</summary>
    public sealed class TextPrompt : Window
    {
        private TextBox _box;
        private bool _accepted;

        private TextPrompt(string title, string label, string initial, string error)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, sans-serif");
            UseLayoutRounding = true;

            Border panel = new Border();
            panel.Width = 400;
            panel.CornerRadius = new CornerRadius(14);
            panel.Background = Brush(Color.FromArgb(0xFF, 0x1A, 0x1B, 0x20));
            panel.BorderBrush = Brush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            panel.BorderThickness = new Thickness(1);
            panel.Padding = new Thickness(20, 16, 20, 16);
            DropShadowEffect sh = new DropShadowEffect();
            sh.BlurRadius = 26;
            sh.ShadowDepth = 5;
            sh.Opacity = 0.6;
            sh.Color = Colors.Black;
            panel.Effect = sh;

            StackPanel sp = new StackPanel();

            TextBlock t = new TextBlock();
            t.Text = title;
            t.FontSize = 15;
            t.FontWeight = FontWeights.SemiBold;
            t.Foreground = Brushes.White;
            sp.Children.Add(t);

            TextBlock l = new TextBlock();
            l.Text = label;
            l.FontSize = 11.5;
            l.Margin = new Thickness(0, 4, 0, 10);
            l.Foreground = Brush(Color.FromArgb(0xFF, 0x9A, 0x9F, 0xAA));
            l.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(l);

            _box = new TextBox();
            _box.FontSize = 13.5;
            _box.Padding = new Thickness(10, 7, 10, 7);
            _box.Background = Brush(Color.FromArgb(0xFF, 0x11, 0x12, 0x16));
            _box.Foreground = Brushes.White;
            _box.CaretBrush = Brushes.White;
            _box.BorderBrush = Brush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
            _box.BorderThickness = new Thickness(1);
            _box.Text = initial ?? "";
            _box.KeyDown += OnBoxKeyDown;
            sp.Children.Add(_box);

            if (!string.IsNullOrEmpty(error))
            {
                TextBlock err = new TextBlock();
                err.Text = error;
                err.FontSize = 11.5;
                err.Margin = new Thickness(0, 8, 0, 0);
                err.Foreground = Brush(Color.FromArgb(0xFF, 0xF2, 0x8B, 0x82));
                err.TextWrapping = TextWrapping.Wrap;
                sp.Children.Add(err);
            }

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 14, 0, 0);

            Border cancel = Chip("取消", false);
            cancel.Margin = new Thickness(0, 0, 8, 0);
            cancel.MouseLeftButtonDown += delegate { Close(); };
            buttons.Children.Add(cancel);

            Border ok = Chip("确定", true);
            ok.MouseLeftButtonDown += delegate { Accept(); };
            buttons.Children.Add(ok);

            sp.Children.Add(buttons);
            panel.Child = sp;
            Content = panel;

            Loaded += delegate
            {
                Activate();
                _box.Focus();
                if (string.IsNullOrEmpty(initial)) _box.CaretIndex = 0;
                else _box.SelectAll();
            };
            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Close();
                }
            };
        }

        private void Accept()
        {
            _accepted = true;
            try
            {
                DialogResult = true;
            }
            catch
            {
                Close();
            }
        }

        private void OnBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Accept();
            }
        }

        private static SolidColorBrush Brush(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static Border Chip(string text, bool primary)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(16, 7, 16, 7);
            b.Cursor = Cursors.Hand;
            b.Background = primary
                ? Brush(Color.FromArgb(0xFF, 0x2F, 0x5B, 0xA8))
                : Brush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 12.5;
            t.Foreground = Brushes.White;
            b.Child = t;
            return b;
        }

        public static string Ask(Window owner, string title, string label, string initial, string error)
        {
            TextPrompt p = new TextPrompt(title, label, initial, error);
            if (owner != null && owner.IsVisible) p.Owner = owner;
            bool? ok = p.ShowDialog();
            if (ok == true && p._accepted) return p._box.Text;
            return null;
        }
    }
}
