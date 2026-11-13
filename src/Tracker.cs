using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace FloatingDock
{
    internal sealed class WindowEntry
    {
        public IntPtr Hwnd;
        public string Title;
        public string ExePath;
        public string AppName;
    }

    internal sealed class WindowGroup
    {
        public string Key;
        public string ExePath;
        public string Title;
        public List<IntPtr> Windows = new List<IntPtr>();
        public IntPtr ActiveWindow;

        public bool Contains(IntPtr hwnd)
        {
            for (int i = 0; i < Windows.Count; i++)
                if (Windows[i] == hwnd) return true;
            return false;
        }
    }

    internal sealed class WindowTracker
    {
        private static readonly HashSet<string> SkipClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
            "Windows.UI.Core.CoreWindow", "ApplicationManager_DesktopShellWindow",
            "TaskListThumbnailWnd", "ForegroundStaging", "Shell_InputSwitchTopLevelWindow",
            "MultitaskingViewFrame", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
            "IgnoreBlurShow", "SysShadow", "DummyDWMListenerWindow", "EdgeUiInputTopWndClass"
        };

        public IntPtr ExcludeHwnd;
        public IntPtr LastForeground;

        private readonly Dictionary<string, IntPtr> _lastActive = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, string> _pidPath = new Dictionary<uint, string>();
        private readonly Dictionary<string, string> _appNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<WindowGroup> _groups = new List<WindowGroup>();

        /// <summary>How many switchable windows are open right now.</summary>
        public int TotalWindows()
        {
            int n = 0;
            for (int i = 0; i < _groups.Count; i++) n += _groups[i].Windows.Count;
            return n;
        }

        /// <summary>Flat list of open windows in z-order, foreground first. Used by the switcher.</summary>
        public List<WindowEntry> ListWindows()
        {
            List<WindowEntry> list = new List<WindowEntry>();
            List<IntPtr> hwnds = new List<IntPtr>();
            N.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (h == ExcludeHwnd) return true;
                if (!IsCandidate(h)) return true;
                hwnds.Add(h);
                return true;
            }, IntPtr.Zero);

            IntPtr fg = N.GetForegroundWindow();
            for (int i = 0; i < hwnds.Count; i++)
            {
                IntPtr h = hwnds[i];
                uint pid = N.GetPid(h);
                if (pid == 0) continue;
                if (pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) continue;

                string path = GetPathCached(pid);
                string cls = N.GetWindowClass(h);
                if (string.Equals(cls, "ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase))
                {
                    string hosted = GetHostedAppPath(h);
                    if (!string.IsNullOrEmpty(hosted)) path = hosted;
                }

                WindowEntry e = new WindowEntry();
                e.Hwnd = h;
                e.Title = N.GetWindowTitle(h);
                e.ExePath = path;
                e.AppName = AppNameOf(path);
                list.Add(e);
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Hwnd == fg && i > 0)
                {
                    WindowEntry front = list[i];
                    list.RemoveAt(i);
                    list.Insert(0, front);
                    break;
                }
            }
            return list;
        }

        private string AppNameOf(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return "应用";
            string name;
            if (_appNames.TryGetValue(exePath, out name)) return name;
            name = PathUtil.DisplayName(exePath);
            _appNames[exePath] = name;
            return name;
        }

        public string Signature { get; private set; }

        public List<WindowGroup> Groups { get { return _groups; } }

        public bool IsForeground(WindowGroup g)
        {
            return LastForeground != IntPtr.Zero && g.Contains(LastForeground);
        }

        /// <summary>Record the foreground window as "last used app" when it is a real app window.</summary>
        public void NoteForeground(IntPtr fg)
        {
            if (fg != IntPtr.Zero && fg != ExcludeHwnd && IsCandidate(fg))
                LastForeground = fg;
        }

        public void Refresh()
        {
            IntPtr fg = N.GetForegroundWindow();
            NoteForeground(fg);

            List<IntPtr> hwnds = new List<IntPtr>();
            N.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (h == ExcludeHwnd) return true;
                if (!IsCandidate(h)) return true;
                hwnds.Add(h);
                return true;
            }, IntPtr.Zero);

            _groups.Clear();
            Dictionary<string, WindowGroup> map = new Dictionary<string, WindowGroup>(StringComparer.OrdinalIgnoreCase);
            StringBuilder sig = new StringBuilder();

            for (int i = 0; i < hwnds.Count; i++)
            {
                IntPtr h = hwnds[i];
                uint pid = N.GetPid(h);
                if (pid == 0) continue;
                if (pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) continue;

                string cls = N.GetWindowClass(h);
                string path = GetPathCached(pid);
                if (string.Equals(cls, "ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase))
                {
                    string hosted = GetHostedAppPath(h);
                    if (!string.IsNullOrEmpty(hosted)) path = hosted;
                }

                string key = !string.IsNullOrEmpty(path) ? path.ToLowerInvariant() : "pid:" + pid;

                WindowGroup g;
                if (!map.TryGetValue(key, out g))
                {
                    g = new WindowGroup();
                    g.Key = key;
                    g.ExePath = path;
                    map[key] = g;
                    _groups.Add(g);
                }
                g.Windows.Add(h);
            }

            for (int i = 0; i < _groups.Count; i++)
            {
                WindowGroup g = _groups[i];
                IntPtr active = IntPtr.Zero;
                if (fg != IntPtr.Zero && g.Contains(fg)) active = fg;
                if (active == IntPtr.Zero)
                {
                    IntPtr prev;
                    if (_lastActive.TryGetValue(g.Key, out prev) && g.Contains(prev) && PrevWasUsable(prev)) active = prev;
                }
                if (active == IntPtr.Zero)
                {
                    for (int j = 0; j < g.Windows.Count; j++)
                    {
                        if (!N.IsIconic(g.Windows[j])) { active = g.Windows[j]; break; }
                    }
                }
                if (active == IntPtr.Zero) active = g.Windows[0];
                g.ActiveWindow = active;
                if (fg != IntPtr.Zero && g.Contains(fg)) _lastActive[g.Key] = fg;

                g.Title = N.GetWindowTitle(active);
                sig.Append(g.Key).Append('\u0002').Append(g.Title).Append('\u0002')
                   .Append(N.IsIconic(active) ? "1" : "0").Append('\u0001');
            }

            Signature = sig.ToString();
        }

        private static bool PrevWasUsable(IntPtr h)
        {
            return N.IsWindow(h) && IsCandidateWindow(h);
        }

        private bool IsCandidate(IntPtr h)
        {
            return IsCandidateWindow(h) && h != ExcludeHwnd;
        }

        private static bool IsCandidateWindow(IntPtr h)
        {
            if (h == IntPtr.Zero || !N.IsWindowVisible(h)) return false;
            if (h == N.GetShellWindow()) return false;
            if (N.GetWindowTextLength(h) <= 0) return false;

            long ex = N.GetWindowLongPtr(h, N.GWL_EXSTYLE).ToInt64();
            if ((ex & N.WS_EX_TOOLWINDOW) != 0) return false;
            if (N.GetWindow(h, N.GW_OWNER) != IntPtr.Zero && (ex & N.WS_EX_APPWINDOW) == 0) return false;

            string cls = N.GetWindowClass(h);
            if (SkipClasses.Contains(cls)) return false;
            if (N.IsCloaked(h)) return false;

            RECT r;
            if (!N.GetWindowRect(h, out r)) return false;
            if (r.Width <= 0 || r.Height <= 0) return false;
            return true;
        }

        private string GetPathCached(uint pid)
        {
            string p;
            if (_pidPath.TryGetValue(pid, out p)) return p;
            p = N.GetProcessPath(pid);
            _pidPath[pid] = p;
            return p;
        }

        /// <summary>For UWP / packaged apps the top-level window belongs to ApplicationFrameHost.exe;
        /// the real app runs in a child CoreWindow.</summary>
        private static string GetHostedAppPath(IntPtr frame)
        {
            IntPtr found = IntPtr.Zero;
            N.EnumChildWindows(frame, delegate(IntPtr h, IntPtr l)
            {
                string cls = N.GetWindowClass(h);
                if (string.Equals(cls, "Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase))
                {
                    found = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (found == IntPtr.Zero) return null;
            uint pid = N.GetPid(found);
            return N.GetProcessPath(pid);
        }
    }
}
