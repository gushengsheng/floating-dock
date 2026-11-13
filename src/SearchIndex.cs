using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace FloatingDock
{
    /// <summary>Lazy file index over a set of target folders, queried by name substring.</summary>
    internal sealed class SearchIndex
    {
        internal sealed class Item
        {
            public string Path;
            public string Name;
            public string Dir;
            public string Lower;
        }

        private readonly object _lock = new object();
        private List<Item> _items = new List<Item>();
        private bool _building;
        private bool _capped;
        private DateTime _builtAt = DateTime.MinValue;
        private int _maxFiles = 120000;

        public bool Capped
        {
            get { lock (_lock) return _capped; }
        }

        public int Count
        {
            get { lock (_lock) return _items.Count; }
        }

        public bool Building
        {
            get { lock (_lock) return _building; }
        }

        public DateTime BuiltAt
        {
            get { lock (_lock) return _builtAt; }
        }

        public void Invalidate()
        {
            lock (_lock)
            {
                _builtAt = DateTime.MinValue;
                _items = new List<Item>();
            }
        }

        public void BuildAsync(IList<string> roots, Action<int> done)
        {
            List<string> copy = new List<string>();
            if (roots != null)
            {
                for (int i = 0; i < roots.Count; i++)
                    if (!string.IsNullOrEmpty(roots[i])) copy.Add(roots[i]);
            }

            lock (_lock)
            {
                if (_building) return;
                _building = true;
                _items = new List<Item>();
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Item> found = new List<Item>();
                for (int i = 0; i < copy.Count; i++)
                {
                    try { Scan(copy[i], found, 0, 12); }
                    catch { }
                }
                lock (_lock)
                {
                    _items = found;
                    _building = false;
                    _capped = found.Count >= _maxFiles;
                    _builtAt = DateTime.Now;
                }
                if (done != null) done(found.Count);
            });
        }

        private void Scan(string dir, List<Item> outItems, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            if (outItems.Count >= _maxFiles) return;

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { return; }

            for (int i = 0; i < files.Length; i++)
            {
                if (outItems.Count >= _maxFiles) return;
                string f = files[i];
                try
                {
                    Item it = new Item();
                    it.Path = f;
                    it.Name = System.IO.Path.GetFileName(f);
                    it.Dir = dir;
                    it.Lower = it.Name.ToLowerInvariant();
                    outItems.Add(it);
                }
                catch { }
            }

            string[] dirs;
            try { dirs = Directory.GetDirectories(dir); }
            catch { return; }

            for (int i = 0; i < dirs.Length; i++)
            {
                if (outItems.Count >= _maxFiles) return;
                try
                {
                    DirectoryInfo di = new DirectoryInfo(dirs[i]);
                    FileAttributes a = di.Attributes;
                    if ((a & FileAttributes.Hidden) != 0) continue;
                    if ((a & FileAttributes.System) != 0) continue;
                    if ((a & FileAttributes.ReparsePoint) != 0) continue;
                    Scan(dirs[i], outItems, depth + 1, maxDepth);
                }
                catch { }
            }
        }

        /// <summary>Name substring search; prefix matches first, then shorter names.</summary>
        public List<Item> Query(string text, int max)
        {
            List<Item> result = new List<Item>();
            string q = (text ?? "").Trim().ToLowerInvariant();
            if (q.Length == 0) return result;

            List<Item> prefix = new List<Item>();
            List<Item> contains = new List<Item>();

            lock (_lock)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    Item it = _items[i];
                    int at = it.Lower.IndexOf(q, StringComparison.Ordinal);
                    if (at < 0) continue;
                    if (at == 0) prefix.Add(it);
                    else contains.Add(it);
                    if (prefix.Count >= max * 4 && contains.Count >= max * 4) break;
                }
            }

            Comparison<Item> cmp = delegate(Item a, Item b)
            {
                int c = a.Name.Length.CompareTo(b.Name.Length);
                if (c != 0) return c;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            };
            prefix.Sort(cmp);
            contains.Sort(cmp);

            for (int i = 0; i < prefix.Count && result.Count < max; i++) result.Add(prefix[i]);
            for (int i = 0; i < contains.Count && result.Count < max; i++) result.Add(contains[i]);
            return result;
        }
    }
}
