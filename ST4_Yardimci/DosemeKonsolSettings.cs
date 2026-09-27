using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ST4Yardimci
{
    /// <summary>
    /// Konsol boyuna göre çap/aralık — %LocalAppData%\ST4Yardimci\doseme_konsol.ini
    /// </summary>
    internal static class DosemeKonsolSettings
    {
        public const int BandCount = 4;

        /// <summary>Azami konsol boyu (cm, dahil), çap mm, aralık cm.</summary>
        public struct Band
        {
            public double MaxBoyCm;
            public int CapMm;
            public int AralikCm;
        }

        private static readonly object Sync = new object();
        private static bool _loaded;
        private static readonly Band[] Bands = CreateDefaults();

        private static Band[] CreateDefaults() => new[]
        {
            new Band { MaxBoyCm = 50, CapMm = 8, AralikCm = 20 },
            new Band { MaxBoyCm = 75, CapMm = 10, AralikCm = 20 },
            new Band { MaxBoyCm = 99.99, CapMm = 12, AralikCm = 20 }, // &lt;100
            new Band { MaxBoyCm = 10000, CapMm = 14, AralikCm = 20 } // ≥100
        };

        private static string StorePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ST4Yardimci");
                return Path.Combine(dir, "doseme_konsol.ini");
            }
        }

        public static void EnsureLoaded()
        {
            lock (Sync)
            {
                if (_loaded) return;
                _loaded = true;
                try { LoadUnlocked(); }
                catch { ApplyDefaultsUnlocked(); }
            }
        }

        public static Band[] GetBandsCopy()
        {
            EnsureLoaded();
            lock (Sync)
            {
                var copy = new Band[BandCount];
                Array.Copy(Bands, copy, BandCount);
                return copy;
            }
        }

        public static void SetBands(Band[] bands)
        {
            if (bands == null || bands.Length != BandCount) return;
            EnsureLoaded();
            lock (Sync)
            {
                for (int i = 0; i < BandCount; i++)
                {
                    Band b = bands[i];
                    if (b.MaxBoyCm < 1) b.MaxBoyCm = 1;
                    if (b.CapMm < 6) b.CapMm = 6;
                    if (b.AralikCm < 5) b.AralikCm = 5;
                    Bands[i] = b;
                }
                SortBandsUnlocked();
            }
            Save();
        }

        public static Band Resolve(double konsolBoyCm)
        {
            EnsureLoaded();
            double L = Math.Max(0, konsolBoyCm);
            lock (Sync)
            {
                for (int i = 0; i < BandCount; i++)
                {
                    if (L <= Bands[i].MaxBoyCm + 1e-6)
                        return Bands[i];
                }
                return Bands[BandCount - 1];
            }
        }

        /// <summary>Konsol üst ilave/firkete: çap/aralık ayardan; adet açıklığa göre. Alt düz bu tabloyu kullanmaz.</summary>
        public static void ApplySteel(double konsolBoyCm, double adetRefLenCm, ref int adet, ref int cap, ref int aralik)
        {
            Band b = Resolve(konsolBoyCm);
            cap = b.CapMm;
            aralik = b.AralikCm;
            double len = adetRefLenCm > 1 ? adetRefLenCm : konsolBoyCm;
            if (aralik <= 0) aralik = 20;
            adet = Math.Max(1, (int)Math.Round(len / aralik) + 1);
        }

        public static void Save()
        {
            EnsureLoaded();
            lock (Sync)
            {
                try
                {
                    string path = StorePath;
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    var sb = new StringBuilder();
                    sb.AppendLine("; ST4 Yardimci — konsol donati boy tablosu");
                    for (int i = 0; i < BandCount; i++)
                    {
                        sb.Append("Band").Append(i + 1).Append('=');
                        sb.Append(Bands[i].MaxBoyCm.ToString("0.##", CultureInfo.InvariantCulture)).Append(',');
                        sb.Append(Bands[i].CapMm).Append(',');
                        sb.Append(Bands[i].AralikCm);
                        sb.AppendLine();
                    }
                    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                }
                catch { }
            }
        }

        private static void ApplyDefaultsUnlocked()
        {
            Band[] d = CreateDefaults();
            for (int i = 0; i < BandCount; i++)
                Bands[i] = d[i];
        }

        private static void SortBandsUnlocked()
        {
            Array.Sort(Bands, (a, b) => a.MaxBoyCm.CompareTo(b.MaxBoyCm));
        }

        private static void LoadUnlocked()
        {
            ApplyDefaultsUnlocked();
            string path = StorePath;
            if (!File.Exists(path)) return;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line[0] == ';' || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if (!key.StartsWith("Band", StringComparison.OrdinalIgnoreCase)) continue;
                if (!int.TryParse(key.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                    continue;
                idx--;
                if (idx < 0 || idx >= BandCount) continue;
                string[] p = val.Split(',');
                if (p.Length < 3) continue;
                if (!double.TryParse(p[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double maxBoy))
                    continue;
                if (!int.TryParse(p[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cap))
                    continue;
                if (!int.TryParse(p[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ar))
                    continue;
                Bands[idx] = new Band { MaxBoyCm = maxBoy, CapMm = cap, AralikCm = ar };
            }
            SortBandsUnlocked();
        }
    }
}
