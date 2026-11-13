using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FloatingDock
{
    /// <summary>Small dark yes/no dialog (used before deleting something).</summary>
    public sealed class ConfirmDialog : Window
    {
        private bool _confirmed;

        private ConfirmDialog(string title, string message, string okText)
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
            panel.Width = 420;
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

            TextBlock m = new TextBlock();
            m.Text = message;
            m.FontSize = 12.5;
            m.Margin = new Thickness(0, 8, 0, 0);
            m.Foreground = Brush(Color.FromArgb(0xFF, 0xC8, 0xCC, 0xD4));
            m.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(m);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 16, 0, 0);

            Border cancel = Chip("取消", false, Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
            cancel.Margin = new Thickness(0, 0, 8, 0);
            cancel.MouseLeftButtonDown += delegate { Close(); };
            buttons.Children.Add(cancel);

            Border ok = Chip(okText, true, Color.FromArgb(0xFF, 0x8A, 0x2F, 0x2F));
            ok.MouseLeftButtonDown += delegate { Accept(); };
            buttons.Children.Add(ok);

            sp.Children.Add(buttons);
            panel.Child = sp;
            Content = panel;

            Loaded += delegate { Activate(); cancel.Focus(); };
            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Close();
                }
                else if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    Accept();
                }
            };
        }

        private void Accept()
        {
            _confirmed = true;
            try { DialogResult = true; }
            catch { Close(); }
        }

        private static SolidColorBrush Brush(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static Border Chip(string text, bool primary, Color bg)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(16, 7, 16, 7);
            b.Cursor = Cursors.Hand;
            b.Background = Brush(bg);
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 12.5;
            t.Foreground = Brushes.White;
            b.Child = t;
            return b;
        }

        public static bool Ask(Window owner, string title, string message, string okText)
        {
            ConfirmDialog d = new ConfirmDialog(title, message, okText);
            if (owner != null && owner.IsVisible) d.Owner = owner;
            bool? r = d.ShowDialog();
            return r == true && d._confirmed;
        }
    }
}
