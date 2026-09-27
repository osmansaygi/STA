using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ST4Yardimci
{
    internal sealed class PlanOlcuPreset
    {
        public string Name { get; set; } = "";
        public string DimStyleName { get; set; } = OtoOlcuSettings.DefaultDimStyleName;
        public double BandWidthCm { get; set; } = 15.0;
        /// <summary>Bant genişliği bundan küçükse tek ölçü; ≥ ise (Toplam ölçü açıksa) çift.</summary>
        public double TekCiftSinirCm { get; set; } = 40.0;
        public bool PlaceTotalDim { get; set; } = true;
        /// <summary>Sürekli: ikinci (çift) detay ölçü hattı.</summary>
        public bool PlaceCiftOlcu { get; set; } = false;
        public List<string> Layers { get; } = new List<string>();
        /// <summary>Çift ölçü için kesilecek katmanlar.</summary>
        public List<string> CiftLayers { get; } = new List<string>();
    }

    /// <summary>
    /// Plan Ölçü — sabit şablonlar (KALIP, MANTAR KALIP, SÜREKLİ TEMEL, RADYE TEMEL).
    /// </summary>
    internal static class PlanOlcuSettings
    {
        private static readonly object Sync = new object();
        private static readonly List<PlanOlcuPreset> Presets = new List<PlanOlcuPreset>();
        private static readonly string[] DefaultNames =
        {
            "KALIP",
            "MANTAR KALIP",
            "SÜREKLİ TEMEL",
            "RADYE TEMEL"
        };

        private static bool _loaded;
        private static string _activePreset = "KALIP";

        private static string StorePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ST4Yardimci");
                return Path.Combine(dir, "planolcu.ini");
            }
        }

        public static string ActivePresetName
        {
            get { EnsureLoaded(); lock (Sync) return _activePreset; }
        }

        public static string DimStyleName
        {
            get
            {
                EnsureLoaded();
                lock (Sync)
                {
                    PlanOlcuPreset p = Active();
                    return string.IsNullOrWhiteSpace(p.DimStyleName)
                        ? OtoOlcuSettings.DefaultDimStyleName
                        : p.DimStyleName;
                }
            }
            set
            {
                EnsureLoaded();
                lock (Sync)
                {
                    Active().DimStyleName = string.IsNullOrWhiteSpace(value)
                        ? OtoOlcuSettings.DefaultDimStyleName
                        : value.Trim();
                }
            }
        }

        /// <summary>Kesit bandı min genişliği (cm). Bundan kısa bantlar atlanır.</summary>
        public static double BandWidthCm
        {
            get
            {
                EnsureLoaded();
                lock (Sync)
                {
                    double v = Active().BandWidthCm;
                    return v < 1.0 ? 15.0 : v;
                }
            }
            set
            {
                EnsureLoaded();
                lock (Sync)
                {
                    Active().BandWidthCm = value < 1.0 ? 1.0 : value;
                }
            }
        }

        /// <summary>
        /// Tek/çift ölçü sınırı (cm). Bant genişliği bundan küçükse yalnız tek ölçü;
        /// büyük/eşit ve PlaceTotalDim açıksa detay+toplam.
        /// </summary>
        public static double TekCiftSinirCm
        {
            get
            {
                EnsureLoaded();
                lock (Sync)
                {
                    double v = Active().TekCiftSinirCm;
                    return v < 1.0 ? 40.0 : v;
                }
            }
            set
            {
                EnsureLoaded();
                lock (Sync)
                {
                    Active().TekCiftSinirCm = value < 1.0 ? 1.0 : value;
                }
            }
        }

        /// <summary>İç ölçüde toplam ölçü atılsın mı (sınır üstü bantlarda).</summary>
        public static bool PlaceTotalDim
        {
            get
            {
                EnsureLoaded();
                lock (Sync) return Active().PlaceTotalDim;
            }
            set
            {
                EnsureLoaded();
                lock (Sync) Active().PlaceTotalDim = value;
            }
        }

        /// <summary>Sürekli: çift (ikinci) detay ölçü hattı.</summary>
        public static bool PlaceCiftOlcu
        {
            get
            {
                EnsureLoaded();
                lock (Sync) return Active().PlaceCiftOlcu;
            }
            set
            {
                EnsureLoaded();
                lock (Sync) Active().PlaceCiftOlcu = value;
            }
        }

        public static void EnsureLoaded()
        {
            lock (Sync)
            {
                if (_loaded) return;
                _loaded = true;
                TryLoad();
                EnsureDefaults();
                if (Find(_activePreset) == null && Presets.Count > 0)
                    _activePreset = Presets[0].Name;
            }
        }

        public static IReadOnlyList<string> GetPresetNames()
        {
            EnsureLoaded();
            lock (Sync)
                return Presets.Select(p => p.Name).ToArray();
        }

        public static bool SelectPreset(string name)
        {
            EnsureLoaded();
            lock (Sync)
            {
                PlanOlcuPreset p = Find(name);
                if (p == null) return false;
                SaveUnlocked();
                _activePreset = p.Name;
                SaveUnlocked();
                return true;
            }
        }

        public static bool AddPreset(string name)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(name)) return false;
            string t = name.Trim();
            lock (Sync)
            {
                if (Find(t) != null) return false;
                SaveUnlocked();
                Presets.Add(new PlanOlcuPreset { Name = t });
                _activePreset = t;
                SaveUnlocked();
                return true;
            }
        }

        public static bool RemovePreset(string name)
        {
            EnsureLoaded();
            lock (Sync)
            {
                if (Presets.Count <= 1) return false;
                PlanOlcuPreset p = Find(name);
                if (p == null) return false;
                Presets.Remove(p);
                if (string.Equals(_activePreset, p.Name, StringComparison.OrdinalIgnoreCase))
                    _activePreset = Presets[0].Name;
                SaveUnlocked();
                return true;
            }
        }

        public static void Save()
        {
            lock (Sync)
            {
                if (!_loaded)
                {
                    _loaded = true;
                    TryLoad();
                    EnsureDefaults();
                }
                SaveUnlocked();
            }
        }

        public static IReadOnlyList<string> GetLayers()
        {
            EnsureLoaded();
            lock (Sync)
                return Active().Layers.ToArray();
        }

        public static void SetLayers(IEnumerable<string> names)
        {
            EnsureLoaded();
            lock (Sync)
            {
                ReplaceList(Active().Layers, names);
                SaveUnlocked();
            }
        }

        public static bool AddLayer(string name)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(name)) return false;
            string t = name.Trim();
            lock (Sync)
            {
                List<string> list = Active().Layers;
                foreach (string e in list)
                    if (string.Equals(e, t, StringComparison.OrdinalIgnoreCase))
                        return false;
                list.Add(t);
                SaveUnlocked();
            }
            return true;
        }

        public static bool RemoveLayer(string name)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(name)) return false;
            bool removed = false;
            lock (Sync)
            {
                List<string> list = Active().Layers;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(list[i], name.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        list.RemoveAt(i);
                        removed = true;
                    }
                }
                if (removed) SaveUnlocked();
            }
            return removed;
        }

        public static IReadOnlyList<string> GetCiftLayers()
        {
            EnsureLoaded();
            lock (Sync)
                return Active().CiftLayers.ToArray();
        }

        public static void SetCiftLayers(IEnumerable<string> names)
        {
            EnsureLoaded();
            lock (Sync)
            {
                ReplaceList(Active().CiftLayers, names);
                SaveUnlocked();
            }
        }

        public static bool AddCiftLayer(string name)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(name)) return false;
            string t = name.Trim();
            lock (Sync)
            {
                List<string> list = Active().CiftLayers;
                foreach (string e in list)
                    if (string.Equals(e, t, StringComparison.OrdinalIgnoreCase))
                        return false;
                list.Add(t);
                SaveUnlocked();
            }
            return true;
        }

        public static bool RemoveCiftLayer(string name)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(name)) return false;
            bool removed = false;
            lock (Sync)
            {
                List<string> list = Active().CiftLayers;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(list[i], name.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        list.RemoveAt(i);
                        removed = true;
                    }
                }
                if (removed) SaveUnlocked();
            }
            return removed;
        }

        private static PlanOlcuPreset Active()
        {
            PlanOlcuPreset p = Find(_activePreset);
            if (p != null) return p;
            EnsureDefaults();
            p = Presets[0];
            _activePreset = p.Name;
            return p;
        }

        private static PlanOlcuPreset Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (PlanOlcuPreset p in Presets)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }

        private static void EnsureDefaults()
        {
            for (int i = Presets.Count - 1; i >= 0; i--)
            {
                if (!IsFixedName(Presets[i].Name))
                    Presets.RemoveAt(i);
            }
            foreach (string n in DefaultNames)
            {
                if (Find(n) == null)
                    Presets.Add(new PlanOlcuPreset { Name = n });
            }
            Presets.Sort((a, b) => FixedIndex(a.Name).CompareTo(FixedIndex(b.Name)));
            if (Find(_activePreset) == null)
                _activePreset = DefaultNames[0];
        }

        private static bool IsFixedName(string name)
        {
            return FixedIndex(name) >= 0;
        }

        private static int FixedIndex(string name)
        {
            for (int i = 0; i < DefaultNames.Length; i++)
            {
                if (string.Equals(DefaultNames[i], name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static void SaveUnlocked()
        {
            try
            {
                string dir = Path.GetDirectoryName(StorePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine("ActivePreset=" + (_activePreset ?? ""));
                foreach (PlanOlcuPreset p in Presets)
                {
                    sb.AppendLine("---");
                    sb.AppendLine("Name=" + (p.Name ?? ""));
                    sb.AppendLine("DimStyle=" + (p.DimStyleName ?? ""));
                    sb.AppendLine("BandWidthCm=" + p.BandWidthCm.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    sb.AppendLine("TekCiftSinirCm=" + p.TekCiftSinirCm.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    sb.AppendLine("PlaceTotalDim=" + (p.PlaceTotalDim ? "1" : "0"));
                    sb.AppendLine("PlaceCiftOlcu=" + (p.PlaceCiftOlcu ? "1" : "0"));
                    sb.AppendLine("Layers=" + string.Join("|", p.Layers));
                    sb.AppendLine("CiftLayers=" + string.Join("|", p.CiftLayers));
                }
                File.WriteAllText(StorePath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        private static void TryLoad()
        {
            try
            {
                if (!File.Exists(StorePath)) return;
                string[] lines = File.ReadAllLines(StorePath, Encoding.UTF8);
                PlanOlcuPreset cur = null;
                foreach (string raw in lines)
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    if (line == "---")
                    {
                        cur = new PlanOlcuPreset();
                        Presets.Add(cur);
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (string.Equals(key, "ActivePreset", StringComparison.OrdinalIgnoreCase))
                    {
                        _activePreset = val;
                        continue;
                    }
                    if (cur == null) continue;

                    if (string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase))
                        cur.Name = val;
                    else if (string.Equals(key, "DimStyle", StringComparison.OrdinalIgnoreCase))
                        cur.DimStyleName = string.IsNullOrWhiteSpace(val)
                            ? OtoOlcuSettings.DefaultDimStyleName
                            : val;
                    else if (string.Equals(key, "BandWidthCm", StringComparison.OrdinalIgnoreCase))
                    {
                        double bw;
                        if (double.TryParse(val, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out bw) && bw >= 1.0)
                            cur.BandWidthCm = bw;
                    }
                    else if (string.Equals(key, "TekCiftSinirCm", StringComparison.OrdinalIgnoreCase))
                    {
                        double lim;
                        if (double.TryParse(val, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out lim) && lim >= 1.0)
                            cur.TekCiftSinirCm = lim;
                    }
                    else if (string.Equals(key, "PlaceTotalDim", StringComparison.OrdinalIgnoreCase))
                        cur.PlaceTotalDim = val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                    else if (string.Equals(key, "PlaceCiftOlcu", StringComparison.OrdinalIgnoreCase))
                        cur.PlaceCiftOlcu = val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                    else if (string.Equals(key, "Layers", StringComparison.OrdinalIgnoreCase))
                        ReplaceList(cur.Layers, SplitPipe(val));
                    else if (string.Equals(key, "CiftLayers", StringComparison.OrdinalIgnoreCase))
                        ReplaceList(cur.CiftLayers, SplitPipe(val));
                }

                for (int i = Presets.Count - 1; i >= 0; i--)
                {
                    if (string.IsNullOrWhiteSpace(Presets[i].Name))
                        Presets.RemoveAt(i);
                }
            }
            catch { }
        }

        private static IEnumerable<string> SplitPipe(string val)
        {
            if (string.IsNullOrWhiteSpace(val)) yield break;
            foreach (string p in val.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = p.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        private static void ReplaceList(List<string> list, IEnumerable<string> names)
        {
            list.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (names == null) return;
            foreach (string n in names)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                string t = n.Trim();
                if (seen.Add(t)) list.Add(t);
            }
        }
    }
}
