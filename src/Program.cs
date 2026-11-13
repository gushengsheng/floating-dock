using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace FloatingDock
{
    internal static class Program
    {
        public static bool OpenSettingsOnStart;

        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--settings", StringComparison.OrdinalIgnoreCase))
                        OpenSettingsOnStart = true;
                }
            }

            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\FloatingDock.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("悬浮栏已经在运行中了。\n\n用 Win+` 显示/隐藏它，或右键它打开菜单。",
                        "Floating Dock", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
                {
                    ConfigStore.Log("unhandled: " + e.Exception);
                    e.Handled = true;
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    ConfigStore.Log("fatal: " + e.ExceptionObject);
                };

                DockWindow window = new DockWindow();
                app.Run(window);
            }
        }
    }
}
