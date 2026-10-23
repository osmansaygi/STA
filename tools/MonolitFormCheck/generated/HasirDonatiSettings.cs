using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ST4Yardimci
{
    /// <summary>
    /// Hasır donatı — %LocalAppData%\ST4Yardimci\hasir_donati.ini
    /// Q tipi standartlar (TS 4559): ebat varsayılan 500×215 cm.
    /// </summary>
    internal static class HasirDonatiSettings
    {
        private static readonly object Sync = new object();
        private static bool _loaded;

        /// <summary>Açılır listedeki Q tipi hasır çeşitleri (boy/en çubuk As kodu).</summary>
        public static readonly string[] TipListesi =
        {
            "Q106/106",
            "Q131/131",
            "Q158/158",
            "Q188/188",
            "Q188/131",
            "Q221/221",
            "Q221/188",
            "Q257/257",
            "Q257/188",
            "Q295/295",
            "Q377/377",
            "Q377/188",
            "Q443/443",
            "Q503/503",
            "Q589/589",
            "Q785/785"
        };

        public static string Tip { get; set; } = "Q188/188";
        /// <summary>Standart boy (cm) — genelde 500.</summary>
        public static double BoyCm { get; set; } = 500;
        /// <summary>Standart en (cm) — genelde 215.</summary>
        public static double EnCm { get; set; } = 215;
        /// <summary>Bindirme boyu (cm). Pratik: çalışan yönde ≈3 göz (45 cm).</summary>
        public static double BindirmeCm { get; set; } = 45;
        /// <summary>Forma paspayı (cm); alandan kenarlardan düşülür.</summary>
        public static double PaspayiCm { get; set; } = 3;
        /// <summary>Çift hasır: etiketin başına 2x yazılır.</summary>
        public static bool CiftHasir { get; set; }

        private static string StorePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ST4Yardimci");
                return Path.Combine(dir, "hasir_donati.ini");
            }
        }

        public static void EnsureLoaded()
        {
            lock (Sync)
            {
                if (_loaded) return;
                _loaded = true;
                try { LoadUnlocked(); }
                catch { }
            }
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
                        TestStore.CreateDirectory(dir);

                    var sb = new StringBuilder();
                    sb.AppendLine("; ST4 Yardimci — Hasir donati");
                    Write(sb, "Tip", Tip ?? "Q188/188");
                    Write(sb, "BoyCm", BoyCm);
                    Write(sb, "EnCm", EnCm);
                    Write(sb, "BindirmeCm", BindirmeCm);
                    Write(sb, "PaspayiCm", PaspayiCm);
                    Write(sb, "CiftHasir", CiftHasir ? "1" : "0");
                    TestStore.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                }
                catch { }
            }
        }

        private static void Write(StringBuilder sb, string key, double v)
        {
            sb.Append(key).Append('=')
                .Append(v.ToString("0.####", CultureInfo.InvariantCulture))
                .AppendLine();
        }

        private static void Write(StringBuilder sb, string key, string v)
        {
            sb.Append(key).Append('=').Append(v ?? "").AppendLine();
        }

        private static void LoadUnlocked()
        {
            string path = StorePath;
            if (!TestStore.Exists(path)) return;

            foreach (string raw in TestStore.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line[0] == ';' || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                if (key.Equals("Tip", StringComparison.OrdinalIgnoreCase))
                {
                    Tip = string.IsNullOrWhiteSpace(val) ? "Q188/188" : val.Trim();
                }
                else if (key.Equals("BoyCm", StringComparison.OrdinalIgnoreCase) &&
                         TryParse(val, out double boy))
                    BoyCm = Clamp(boy, 50, 2000);
                else if (key.Equals("EnCm", StringComparison.OrdinalIgnoreCase) &&
                         TryParse(val, out double en))
                    EnCm = Clamp(en, 50, 2000);
                else if (key.Equals("BindirmeCm", StringComparison.OrdinalIgnoreCase) &&
                         TryParse(val, out double bin))
                    BindirmeCm = Clamp(bin, 5, 200);
                else if (key.Equals("PaspayiCm", StringComparison.OrdinalIgnoreCase) &&
                         TryParse(val, out double pas))
                    PaspayiCm = Clamp(pas, 0, 20);
                else if (key.Equals("CiftHasir", StringComparison.OrdinalIgnoreCase))
                    CiftHasir = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool TryParse(string s, out double v)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                   || double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v);
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
