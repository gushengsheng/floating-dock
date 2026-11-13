using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Win32;

namespace FloatingDock
{
    [DataContract]
    public sealed class DockConfig
    {
        [DataMember(Order = 1)]
        public List<string> Pinned;

        [DataMember(Order = 2, EmitDefaultValue = false)]
        public int? Left;

        [DataMember(Order = 3, EmitDefaultValue = false)]
        public int? Top;

        [DataMember(Order = 4)]
        public string DockHotkey;

        [DataMember(Order = 5)]
        public string TaskbarHotkey;

        [DataMember(Order = 6)]
        public bool HideInFullscreen;

        [DataMember(Order = 7)]
        public int IconSize;

        [DataMember(Order = 8)]
        public bool AutoStart;

        [DataMember(Order = 9)]
        public string FolderHotkey;

        [DataMember(Order = 10, EmitDefaultValue = false)]
        public bool HideCornerButton;

        [DataMember(Order = 11)]
        public string SwitcherHotkey;

        [DataMember(Order = 12, EmitDefaultValue = false)]
        public bool HideSwitcherDots;

        [DataMember(Order = 13)]
        public List<string> SearchFolders;

        [DataMember(Order = 14)]
        public string SearchHotkey;

        [DataMember(Order = 15, EmitDefaultValue = false)]
        public bool CompactDock;

        public DockConfig()
        {
            Pinned = new List<string>();
            SearchFolders = new List<string>();
            DockHotkey = "Win+`";
            TaskbarHotkey = "Ctrl+Alt+B";
            FolderHotkey = "Ctrl+Alt+F";
            SwitcherHotkey = "Ctrl+Alt+W";
            SearchHotkey = "Ctrl+Alt+S";
            HideInFullscreen = false;
            IconSize = 28;
            AutoStart = true;
        }
    }

    internal static class ConfigStore
    {
        public static string Dir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FloatingDock");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return d;
            }
        }

        public static string ConfigPath { get { return Path.Combine(Dir, "config.json"); } }
        public static string LogPath { get { return Path.Combine(Dir, "dock.log"); } }

        public static DockConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    using (FileStream fs = File.OpenRead(ConfigPath))
                    {
                        DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(DockConfig));
                        DockConfig cfg = (DockConfig)ser.ReadObject(fs);
                        if (cfg != null) return Normalize(cfg);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("config load failed: " + ex.Message);
                try
                {
                    File.Copy(ConfigPath, ConfigPath + ".bad", true);
                }
                catch { }
            }
            DockConfig fresh = new DockConfig();
            SeedPinned(fresh);
            return fresh;
        }

        private static DockConfig Normalize(DockConfig cfg)
        {
            if (cfg.Pinned == null) cfg.Pinned = new List<string>();
            if (cfg.SearchFolders == null) cfg.SearchFolders = new List<string>();
            if (string.IsNullOrEmpty(cfg.DockHotkey)) cfg.DockHotkey = "Win+`";
            if (string.IsNullOrEmpty(cfg.TaskbarHotkey)) cfg.TaskbarHotkey = "Ctrl+Alt+B";
            if (string.IsNullOrEmpty(cfg.FolderHotkey)) cfg.FolderHotkey = "Ctrl+Alt+F";
            if (string.IsNullOrEmpty(cfg.SwitcherHotkey)) cfg.SwitcherHotkey = "Ctrl+Alt+W";
            if (string.IsNullOrEmpty(cfg.SearchHotkey)) cfg.SearchHotkey = "Ctrl+Alt+S";
            if (cfg.IconSize < 16 || cfg.IconSize > 64) cfg.IconSize = 28;
            return cfg;
        }

        public static void Save(DockConfig cfg)
        {
            try
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(DockConfig));
                using (MemoryStream ms = new MemoryStream())
                {
                    ser.WriteObject(ms, cfg);
                    File.WriteAllText(ConfigPath, Encoding.UTF8.GetString(ms.ToArray()), new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                Log("config save failed: " + ex.Message);
            }
        }

        /// <summary>First run: pin a small set of apps that actually exist on this machine.</summary>
        private static void SeedPinned(DockConfig cfg)
        {
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            if (File.Exists(explorer)) cfg.Pinned.Add(explorer);

            string[] wanted = new string[] { "wt.exe", "msedge.exe", "chrome.exe", "Code.exe", "notepad.exe" };
            for (int i = 0; i < wanted.Length; i++)
            {
                string p = AppPaths.Find(wanted[i]);
                if (!string.IsNullOrEmpty(p) && !AlreadyPinned(cfg, p)) cfg.Pinned.Add(p);
            }
        }

        private static bool AlreadyPinned(DockConfig cfg, string path)
        {
            for (int i = 0; i < cfg.Pinned.Count; i++)
                if (string.Equals(PathUtil.Resolve(cfg.Pinned[i]), PathUtil.Resolve(path), StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public static void Log(string msg)
        {
            try
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }

    internal static class AppPaths
    {
        /// <summary>Look up an executable through the registry "App Paths" keys, then PATH.</summary>
        public static string Find(string exe)
        {
            string[] roots = new string[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\"
            };
            RegistryKey[] hives = new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser };
            for (int h = 0; h < hives.Length; h++)
            {
                for (int r = 0; r < roots.Length; r++)
                {
                    try
                    {
                        using (RegistryKey k = hives[h].OpenSubKey(roots[r] + exe))
                        {
                            if (k == null) continue;
                            string p = k.GetValue(null) as string;
                            if (!string.IsNullOrEmpty(p))
                            {
                                p = Environment.ExpandEnvironmentVariables(p.Trim('"'));
                                if (File.Exists(p)) return p;
                            }
                        }
                    }
                    catch { }
                }
            }
            return null;
        }
    }

    internal static class PathUtil
    {
        /// <summary>Resolve a .lnk shortcut to its target so icons and running matches work.</summary>
        public static string Resolve(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return path;
                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
                string target = (string)link.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, link, null);
                if (!string.IsNullOrEmpty(target)) return target;
            }
            catch { }
            return path;
        }

        public static string DisplayName(string path)
        {
            try
            {
                string resolved = Resolve(path);
                if (File.Exists(resolved))
                {
                    FileVersionInfo vi = FileVersionInfo.GetVersionInfo(resolved);
                    if (!string.IsNullOrEmpty(vi.FileDescription)) return vi.FileDescription.Trim();
                    if (!string.IsNullOrEmpty(vi.ProductName)) return vi.ProductName.Trim();
                }
                return Path.GetFileNameWithoutExtension(resolved);
            }
            catch
            {
                return Path.GetFileNameWithoutExtension(path);
            }
        }
    }

    internal static class HotkeyUtil
    {
        public static bool TryParse(string text, out int mods, out int vk)
        {
            mods = 0;
            vk = 0;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) return false;
                string low = p.ToLowerInvariant();
                if (low == "ctrl" || low == "control") { mods |= N.MOD_CONTROL; continue; }
                if (low == "alt") { mods |= N.MOD_ALT; continue; }
                if (low == "shift") { mods |= N.MOD_SHIFT; continue; }
                if (low == "win" || low == "windows") { mods |= N.MOD_WIN; continue; }
                int code = KeyCode(p);
                if (code == 0) return false;
                vk = code;
            }
            return vk != 0;
        }

        public static string Describe(string text)
        {
            return string.IsNullOrEmpty(text) ? "(未设置)" : text;
        }

        private static int KeyCode(string p)
        {
            if (p.Length == 1)
            {
                char c = char.ToUpperInvariant(p[0]);
                if (c >= 'A' && c <= 'Z') return c;
                if (c >= '0' && c <= '9') return c;
                switch (p[0])
                {
                    case '`': return 0xC0;
                    case '-': return 0xBD;
                    case '=': return 0xBB;
                    case '[': return 0xDB;
                    case ']': return 0xDD;
                    case '\\': return 0xDC;
                    case ';': return 0xBA;
                    case '\'': return 0xDE;
                    case ',': return 0xBC;
                    case '.': return 0xBE;
                    case '/': return 0xBF;
                    case ' ': return 0x20;
                }
            }
            string low = p.ToLowerInvariant();
            if (low.StartsWith("f") && low.Length <= 3)
            {
                int n;
                if (int.TryParse(low.Substring(1), out n) && n >= 1 && n <= 24) return 0x70 + (n - 1);
            }
            switch (low)
            {
                case "space": return 0x20;
                case "tab": return 0x09;
                case "enter": return 0x0D;
                case "esc":
                case "escape": return 0x1B;
                case "home": return 0x24;
                case "end": return 0x23;
                case "pageup": return 0x21;
                case "pagedown": return 0x22;
                case "insert": return 0x2D;
                case "delete": return 0x2E;
                case "left": return 0x25;
                case "up": return 0x26;
                case "right": return 0x27;
                case "down": return 0x28;
                case "backspace": return 0x08;
            }
            return 0;
        }
    }

    internal static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "FloatingDock";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (k == null) return false;
                    return k.GetValue(ValueName) != null;
                }
            }
            catch { return false; }
        }

        public static void Set(bool enabled, string exePath)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k == null) return;
                    if (enabled) k.SetValue(ValueName, "\"" + exePath + "\"");
                    else k.DeleteValue(ValueName, false);
                }
            }
            catch (Exception ex)
            {
                ConfigStore.Log("autostart failed: " + ex.Message);
            }
        }
    }
}
