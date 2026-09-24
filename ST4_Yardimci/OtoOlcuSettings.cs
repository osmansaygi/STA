using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ST4Yardimci
{
    internal enum OtoOlcuSablon
    {
        TekOlcu = 0,
        CiftOlcu = 1
    }

    internal enum OtoOlcuLayerList
    {
        Birinci = 1,
        Ikinci = 2
    }

    internal sealed class OtoOlcuPreset
    {
        public string Name { get; set; } = "";
        public string DimStyleName { get; set; } = "OLCU (BEYKENT)";
        public string DimLayerName { get; set; } = "OLCU (BEYKENT)";
        public OtoOlcuSablon Sablon { get; set; } = OtoOlcuSablon.TekOlcu;
        public bool IkinciOlcuToplam { get; set; }
        public List<string> Layers1 { get; } = new List<string>();
        public List<string> Layers2 { get; } = new List<string>();
    }

    /// <summary>
    /// Oto Ölçü — hazır şablonlar ayrı kaydedilir; aktif şablon değişince son ayarları yüklenir.
    /// </summary>
    internal static class OtoOlcuSettings
    {
        private static readonly object Sync = new object();
        private static readonly List<OtoOlcuPreset> Presets = new List<OtoOlcuPreset>();
        private static readonly string[] DefaultNames =
        {
            "Kalıp ölçüsü",
            "Radye temel",
            "Sürekli temel",
            AksOlcuPresetName
        };

        /// <summary>Silinemez hazır şablon; aks çizgisi + balon ile çift ölçü.</summary>
        public const string AksOlcuPresetName = "AKS OLCU";
        public const string AksOlcuDimStyleName = "AKS_OLCU";
        public const string AksOlcuDimLayerName = "AKS OLCU (BEYKENT)";
        /// <summary>Balondan içeri toplam ölçü mesafesi (cm).</summary>
        public const double AksOlcuTotalOffsetCm = 35.0;
        /// <summary>Toplam ölçüden içeri ara ölçü mesafesi (cm).</summary>
        public const double AksOlcuIntervalGapCm = 20.0;

        private static bool _loaded;
        private static string _activePreset = "Kalıp ölçüsü";

        public static OtoOlcuLayerList LayerPickTarget { get; set; } = OtoOlcuLayerList.Birinci;

        public static bool IsAksOlcuPreset
        {
            get
            {
                EnsureLoaded();
                lock (Sync)
                    return string.Equals(_activePreset, AksOlcuPresetName, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static bool IsLockedPreset(string name) =>
            string.Equals(name, AksOlcuPresetName, StringComparison.OrdinalIgnoreCase);

        private static string StorePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ST4Yardimci");
                return Path.Combine(dir, "otoolcu.ini");
            }
        }

        private static OtoOlcuPreset ActiveUnlocked()
        {
            OtoOlcuPreset p = FindUnlocked(_activePreset);
            if (p != null) return p;
            EnsureDefaultsUnlocked();
            p = Presets[0];
            _activePreset = p.Name;
            return p;
        }

        private static OtoOlcuPreset FindUnlocked(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (OtoOlcuPreset p in Presets)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
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
                    OtoOlcuPreset p = ActiveUnlocked();
                    if (IsLockedPreset(p.Name)) return AksOlcuDimStyleName;
                    return p.DimStyleName;
                }
            }
            set
            {
                EnsureLoaded();
                lock (Sync)
                {
                    OtoOlcuPreset p = ActiveUnlocked();
                    if (IsLockedPreset(p.Name))
                    {
                        EnforceAksOlcuUnlocked(p);
                        return;
                    }
                    p.DimStyleName = value ?? "";
                }
            }
        }

        public static string DimLayerName
        {
            get
            {
                EnsureLoaded();
                lock (Sync)
                {
                    OtoOlcuPreset p = ActiveUnlocked();
                    if (IsLockedPreset(p.Name)) return AksOlcuDimLayerName;
                    return p.DimLayerName;
                }
            }
            set
            {
                EnsureLoaded();
                lock (Sync)
                {
                    OtoOlcuPreset p = ActiveUnlocked();
                    if (IsLockedPreset(p.Name))
                    {
                        EnforceAksOlcuUnlocked(p);
                        return;
                    }
                    p.DimLayerName = string.IsNullOrWhiteSpace(value) ? "OLCU (BEYKENT)" : value;
                }
            }
        }

        public static OtoOlcuSablon Sablon
        {
            get { EnsureLoaded(); lock (Sync) return ActiveUnlocked().Sablon; }
            set { EnsureLoaded(); lock (Sync) ActiveUnlocked().Sablon = value; }
        }

        public static bool IkinciOlcuToplam
        {
            get { EnsureLoaded(); lock (Sync) return ActiveUnlocked().IkinciOlcuToplam; }
            set { EnsureLoaded(); lock (Sync) ActiveUnlocked().IkinciOlcuToplam = value; }
        }

        public static void EnsureLoaded()
        {
            lock (Sync)
            {
                if (_loaded) return;
                _loaded = true;
                TryLoadUnlocked();
                EnsureDefaultsUnlocked();
                if (FindUnlocked(_activePreset) == null)
                    _activePreset = Presets[0].Name;
            }
        }

        public static IReadOnlyList<string> GetPresetNames()
        {
            EnsureLoaded();
            lock (Sync)
                return Presets.Select(p => p.Name).ToArray();
        }

        /// <summary>Tüm hazır şablonlardaki katman ve ölçü stili adları (çizimde oluşturmak için).</summary>
        public static void CollectAllReferencedNames(out List<string> layers, out List<string> dimStyles)
        {
            EnsureLoaded();
            layers = new List<string>();
            dimStyles = new List<string>();
            var seenL = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenD = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (Sync)
            {
                foreach (OtoOlcuPreset p in Presets)
                {
                    if (IsLockedPreset(p.Name))
                        EnforceAksOlcuUnlocked(p);
                    if (!string.IsNullOrWhiteSpace(p.DimStyleName) && seenD.Add(p.DimStyleName.Trim()))
                        dimStyles.Add(p.DimStyleName.Trim());
                    if (!string.IsNullOrWhiteSpace(p.DimLayerName) && seenL.Add(p.DimLayerName.Trim()))
                        layers.Add(p.DimLayerName.Trim());
                    foreach (string n in p.Layers1)
                    {
                        if (!string.IsNullOrWhiteSpace(n) && seenL.Add(n.Trim()))
                            layers.Add(n.Trim());
                    }
                    foreach (string n in p.Layers2)
                    {
                        if (!string.IsNullOrWhiteSpace(n) && seenL.Add(n.Trim()))
                            layers.Add(n.Trim());
                    }
                }
                if (seenD.Add(AksOlcuDimStyleName))
                    dimStyles.Add(AksOlcuDimStyleName);
                if (seenL.Add(AksOlcuDimLayerName))
                    layers.Add(AksOlcuDimLayerName);
            }
        }

        public static bool SelectPreset(string name)
        {
            EnsureLoaded();
            lock (Sync)
            {
                OtoOlcuPreset p = FindUnlocked(name);
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
            if (IsLockedPreset(t)) return false;
            lock (Sync)
            {
                if (FindUnlocked(t) != null) return false;
                SaveUnlocked();
                Presets.Add(new OtoOlcuPreset { Name = t });
                _activePreset = t;
                SaveUnlocked();
                return true;
            }
        }

        public static bool RemovePreset(string name)
        {
            EnsureLoaded();
            if (IsLockedPreset(name)) return false;
            lock (Sync)
            {
                if (Presets.Count <= 1) return false;
                OtoOlcuPreset p = FindUnlocked(name);
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
                    TryLoadUnlocked();
                    EnsureDefaultsUnlocked();
                }
                SaveUnlocked();
            }
        }

        private static void EnsureDefaultsUnlocked()
        {
            foreach (string n in DefaultNames)
            {
                if (FindUnlocked(n) == null)
                {
                    var p = new OtoOlcuPreset { Name = n };
                    if (IsLockedPreset(n))
                        EnforceAksOlcuUnlocked(p);
                    Presets.Add(p);
                }
            }
            OtoOlcuPreset aks = FindUnlocked(AksOlcuPresetName);
            if (aks != null)
                EnforceAksOlcuUnlocked(aks);
            if (Presets.Count == 0)
                Presets.Add(new OtoOlcuPreset { Name = "Kalıp ölçüsü" });
        }

        private static void EnforceAksOlcuUnlocked(OtoOlcuPreset p)
        {
            if (p == null) return;
            p.Name = AksOlcuPresetName;
            p.DimStyleName = AksOlcuDimStyleName;
            p.DimLayerName = AksOlcuDimLayerName;
            p.Sablon = OtoOlcuSablon.CiftOlcu;
            p.IkinciOlcuToplam = true;
            p.Layers1.Clear();
            p.Layers2.Clear();
        }

        private static void SaveUnlocked()
        {
            try
            {
                string dir = Path.GetDirectoryName(StorePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                OtoOlcuPreset aks = FindUnlocked(AksOlcuPresetName);
                if (aks != null)
                    EnforceAksOlcuUnlocked(aks);

                var sb = new StringBuilder();
                sb.AppendLine("ActivePreset=" + (_activePreset ?? ""));
                foreach (OtoOlcuPreset p in Presets)
                {
                    sb.AppendLine("---");
                    sb.AppendLine("Name=" + (p.Name ?? ""));
                    sb.AppendLine("DimStyle=" + (p.DimStyleName ?? ""));
                    sb.AppendLine("DimLayer=" + (p.DimLayerName ?? ""));
                    sb.AppendLine("Sablon=" + ((int)p.Sablon).ToString());
                    sb.AppendLine("IkinciToplam=" + (p.IkinciOlcuToplam ? "1" : "0"));
                    sb.AppendLine("Layers1=" + string.Join("|", p.Layers1));
                    sb.AppendLine("Layers2=" + string.Join("|", p.Layers2));
                }
                File.WriteAllText(StorePath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        private static void TryLoadUnlocked()
        {
            try
            {
                if (!File.Exists(StorePath)) return;
                string[] lines = File.ReadAllLines(StorePath, Encoding.UTF8);

                // Eski tek-blok format mı?
                bool hasSection = lines.Any(l => (l ?? "").Trim() == "---");
                if (!hasSection)
                {
                    LoadLegacyFlatUnlocked(lines);
                    return;
                }

                OtoOlcuPreset cur = null;
                foreach (string raw in lines)
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    if (line == "---")
                    {
                        cur = new OtoOlcuPreset();
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
                        cur.DimStyleName = val;
                    else if (string.Equals(key, "DimLayer", StringComparison.OrdinalIgnoreCase))
                        cur.DimLayerName = string.IsNullOrWhiteSpace(val) ? "OLCU (BEYKENT)" : val;
                    else if (string.Equals(key, "Sablon", StringComparison.OrdinalIgnoreCase))
                    {
                        int s;
                        if (int.TryParse(val, out s))
                            cur.Sablon = s == 1 ? OtoOlcuSablon.CiftOlcu : OtoOlcuSablon.TekOlcu;
                    }
                    else if (string.Equals(key, "IkinciToplam", StringComparison.OrdinalIgnoreCase))
                        cur.IkinciOlcuToplam = val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                    else if (string.Equals(key, "Layers1", StringComparison.OrdinalIgnoreCase))
                        ReplaceList(cur.Layers1, SplitPipe(val));
                    else if (string.Equals(key, "Layers2", StringComparison.OrdinalIgnoreCase))
                        ReplaceList(cur.Layers2, SplitPipe(val));
                }

                // İsimsiz bölümleri at
                for (int i = Presets.Count - 1; i >= 0; i--)
                {
                    if (string.IsNullOrWhiteSpace(Presets[i].Name))
                        Presets.RemoveAt(i);
                }
            }
            catch { }
        }

        private static void LoadLegacyFlatUnlocked(string[] lines)
        {
            var p = new OtoOlcuPreset { Name = "Kalıp ölçüsü" };
            foreach (string raw in lines)
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if (string.Equals(key, "DimStyle", StringComparison.OrdinalIgnoreCase))
                    p.DimStyleName = val;
                else if (string.Equals(key, "DimLayer", StringComparison.OrdinalIgnoreCase))
                    p.DimLayerName = string.IsNullOrWhiteSpace(val) ? "OLCU (BEYKENT)" : val;
                else if (string.Equals(key, "Sablon", StringComparison.OrdinalIgnoreCase))
                {
                    int s;
                    if (int.TryParse(val, out s))
                        p.Sablon = s == 1 ? OtoOlcuSablon.CiftOlcu : OtoOlcuSablon.TekOlcu;
                }
                else if (string.Equals(key, "IkinciToplam", StringComparison.OrdinalIgnoreCase))
                    p.IkinciOlcuToplam = val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                else if (string.Equals(key, "Layers1", StringComparison.OrdinalIgnoreCase))
                    ReplaceList(p.Layers1, SplitPipe(val));
                else if (string.Equals(key, "Layers2", StringComparison.OrdinalIgnoreCase))
                    ReplaceList(p.Layers2, SplitPipe(val));
            }
            Presets.Add(p);
            _activePreset = p.Name;
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

        public static IReadOnlyList<string> GetLayers() => GetLayers(OtoOlcuLayerList.Birinci);

        public static IReadOnlyList<string> GetLayers(OtoOlcuLayerList which)
        {
            EnsureLoaded();
            lock (Sync)
            {
                OtoOlcuPreset p = ActiveUnlocked();
                return which == OtoOlcuLayerList.Ikinci ? p.Layers2.ToArray() : p.Layers1.ToArray();
            }
        }

        public static void SetLayers(IEnumerable<string> names) => SetLayers(OtoOlcuLayerList.Birinci, names);

        public static void SetLayers(OtoOlcuLayerList which, IEnumerable<string> names)
        {
            EnsureLoaded();
            lock (Sync)
            {
                OtoOlcuPreset p = ActiveUnlocked();
                ReplaceList(which == OtoOlcuLayerList.Ikinci ? p.Layers2 : p.Layers1, names);
                SaveUnlocked();
            }
        }

        public static bool AddLayer(string name) => AddLayer(LayerPickTarget, name);

        public static bool AddLayer(OtoOlcuLayerList which, string name)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(name)) return false;
            string t = name.Trim();
            lock (Sync)
            {
                List<string> list = which == OtoOlcuLayerList.Ikinci
                    ? ActiveUnlocked().Layers2
                    : ActiveUnlocked().Layers1;
                foreach (string e in list)
                    if (string.Equals(e, t, StringComparison.OrdinalIgnoreCase))
                        return false;
                list.Add(t);
                SaveUnlocked();
            }
            return true;
        }

        public static bool RemoveLayer(string name) => RemoveLayer(LayerPickTarget, name);

        public static bool RemoveLayer(OtoOlcuLayerList which, string name)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(name)) return false;
            bool removed = false;
            lock (Sync)
            {
                List<string> list = which == OtoOlcuLayerList.Ikinci
                    ? ActiveUnlocked().Layers2
                    : ActiveUnlocked().Layers1;
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
    }
}
