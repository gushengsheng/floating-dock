using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace FloatingDock
{
    [DataContract]
    public sealed class PaneState
    {
        [DataMember(Order = 1)]
        public string Title;

        [DataMember(Order = 2)]
        public string Path;

        [DataMember(Order = 3, EmitDefaultValue = false)]
        public int Selected;
    }

    [DataContract]
    public sealed class FolderSettings
    {
        [DataMember(Order = 1)]
        public List<PaneState> Panes;

        [DataMember(Order = 2, EmitDefaultValue = false)]
        public int ActiveIndex;
    }

    internal static class FolderStore
    {
        public static string SettingsPath
        {
            get { return System.IO.Path.Combine(ConfigStore.Dir, "folders.json"); }
        }

        public static FolderSettings Load()
        {
            FolderSettings s = null;
            try
            {
                if (File.Exists(SettingsPath))
                {
                    using (FileStream fs = File.OpenRead(SettingsPath))
                    {
                        DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(FolderSettings));
                        s = (FolderSettings)ser.ReadObject(fs);
                    }
                }
            }
            catch (Exception ex)
            {
                ConfigStore.Log("folders load failed: " + ex.Message);
            }

            if (s == null || s.Panes == null || s.Panes.Count == 0)
            {
                s = Seed();
                Save(s);
            }

            for (int i = 0; i < s.Panes.Count; i++)
            {
                PaneState p = s.Panes[i];
                if (p == null) continue;
                if (string.IsNullOrEmpty(p.Path)) p.Path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (string.IsNullOrEmpty(p.Title)) p.Title = SafeName(p.Path);
                if (p.Selected < 0) p.Selected = 0;
            }
            if (s.ActiveIndex < 0 || s.ActiveIndex >= s.Panes.Count) s.ActiveIndex = 0;
            return s;
        }

        public static void Save(FolderSettings s)
        {
            try
            {
                if (s == null) return;
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(FolderSettings));
                using (MemoryStream ms = new MemoryStream())
                {
                    ser.WriteObject(ms, s);
                    File.WriteAllText(SettingsPath, Encoding.UTF8.GetString(ms.ToArray()), new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                ConfigStore.Log("folders save failed: " + ex.Message);
            }
        }

        private static string SafeName(string path)
        {
            try
            {
                string n = new DirectoryInfo(path).Name;
                if (!string.IsNullOrEmpty(n)) return n;
                return path;
            }
            catch { return path; }
        }

        private static FolderSettings Seed()
        {
            FolderSettings s = new FolderSettings();
            s.Panes = new List<PaneState>();

            string[][] wanted = new string[][]
            {
                new string[] { "桌面", Environment.GetFolderPath(Environment.SpecialFolder.Desktop) },
                new string[] { "下载", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") },
                new string[] { "文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) }
            };
            for (int i = 0; i < wanted.Length; i++)
            {
                try
                {
                    if (!Directory.Exists(wanted[i][1])) continue;
                    PaneState p = new PaneState();
                    p.Title = wanted[i][0];
                    p.Path = wanted[i][1];
                    s.Panes.Add(p);
                }
                catch { }
            }
            if (s.Panes.Count == 0)
            {
                PaneState p = new PaneState();
                p.Title = "桌面";
                p.Path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                s.Panes.Add(p);
            }
            return s;
        }
    }
}
