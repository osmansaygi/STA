using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ST4Yardimci
{
    /// <summary>Oto Donatı hedef tipi — radye temel veya mantar döşeme.</summary>
    internal enum OtoDonatiTip
    {
        RadyeTemel = 0,
        MantarDoseme = 1
    }

    /// <summary>Bir tip (radye / mantar) için kalıcı ayar seti.</summary>
    internal sealed class OtoDonatiProfile
    {
        public double KalinlikCm;
        public double PaspayiCm;
        public double UstKotM;
        public double GonyeBoyuMaxCm;
        public double AnalizEsigiCm;
        public int AltDonatiCapMm;
        public int UstDonatiCapMm;
        public double AltAralikCm;
        public double UstAralikCm;
        public bool AltGonyeSol;
        public bool AltGonyeSag;
        public bool UstGonyeSol;
        public bool UstGonyeSag;
        public bool AltDonatiCiz;
        public bool UstDonatiCiz;
        public bool SehpaDonatisi;

        public static OtoDonatiProfile CreateRadyeDefault() => new OtoDonatiProfile
        {
            KalinlikCm = 50,
            PaspayiCm = 5,
            UstKotM = 0,
            GonyeBoyuMaxCm = 40,
            AnalizEsigiCm = 100,
            AltDonatiCapMm = 14,
            UstDonatiCapMm = 12,
            AltAralikCm = 15,
            UstAralikCm = 15,
            AltGonyeSol = true,
            AltGonyeSag = true,
            UstGonyeSol = true,
            UstGonyeSag = true,
            AltDonatiCiz = true,
            UstDonatiCiz = true,
            SehpaDonatisi = false
        };

        public static OtoDonatiProfile CreateMantarDefault() => new OtoDonatiProfile
        {
            KalinlikCm = 25,
            PaspayiCm = 3,
            UstKotM = 0,
            GonyeBoyuMaxCm = 40,
            AnalizEsigiCm = 100,
            AltDonatiCapMm = 12,
            UstDonatiCapMm = 10,
            AltAralikCm = 15,
            UstAralikCm = 15,
            AltGonyeSol = true,
            AltGonyeSag = true,
            UstGonyeSol = true,
            UstGonyeSag = true,
            AltDonatiCiz = true,
            UstDonatiCiz = true,
            SehpaDonatisi = false
        };

        public void CopyFrom(OtoDonatiProfile src)
        {
            if (src == null) return;
            KalinlikCm = src.KalinlikCm;
            PaspayiCm = src.PaspayiCm;
            UstKotM = src.UstKotM;
            GonyeBoyuMaxCm = src.GonyeBoyuMaxCm;
            AnalizEsigiCm = src.AnalizEsigiCm;
            AltDonatiCapMm = src.AltDonatiCapMm;
            UstDonatiCapMm = src.UstDonatiCapMm;
            AltAralikCm = src.AltAralikCm;
            UstAralikCm = src.UstAralikCm;
            AltGonyeSol = src.AltGonyeSol;
            AltGonyeSag = src.AltGonyeSag;
            UstGonyeSol = src.UstGonyeSol;
            UstGonyeSag = src.UstGonyeSag;
            AltDonatiCiz = src.AltDonatiCiz;
            UstDonatiCiz = src.UstDonatiCiz;
            SehpaDonatisi = src.SehpaDonatisi;
        }
    }

    /// <summary>
    /// Oto Donatı ayarları — %LocalAppData%\ST4Yardimci\oto_donati.ini
    /// Radye ve mantar profilleri ayrı saklanır; Tip hangi profilin aktif olduğunu seçer.
    /// Çizim kuralları aynı; fark (şimdilik) yalnız sehpa donatısı bayrağı.
    /// </summary>
    internal static class OtoDonatiSettings
    {
        private static readonly object Sync = new object();
        private static bool _loaded;
        private static OtoDonatiProfile _radye = OtoDonatiProfile.CreateRadyeDefault();
        private static OtoDonatiProfile _mantar = OtoDonatiProfile.CreateMantarDefault();

        public static OtoDonatiTip Tip { get; set; } = OtoDonatiTip.RadyeTemel;

        private static OtoDonatiProfile Active =>
            Tip == OtoDonatiTip.MantarDoseme ? _mantar : _radye;

        public static OtoDonatiProfile ProfileFor(OtoDonatiTip tip) =>
            tip == OtoDonatiTip.MantarDoseme ? _mantar : _radye;

        /// <summary>Temel veya döşeme kalınlığı (cm).</summary>
        public static double KalinlikCm { get => Active.KalinlikCm; set => Active.KalinlikCm = value; }
        public static double PaspayiCm { get => Active.PaspayiCm; set => Active.PaspayiCm = value; }
        /// <summary>Temel / döşeme üst kotu (m) — sehpa vb. ekstra çizimler için.</summary>
        public static double UstKotM { get => Active.UstKotM; set => Active.UstKotM = value; }
        public static double GonyeBoyuMaxCm { get => Active.GonyeBoyuMaxCm; set => Active.GonyeBoyuMaxCm = value; }
        public static double AnalizEsigiCm { get => Active.AnalizEsigiCm; set => Active.AnalizEsigiCm = value; }

        public static double EffectiveGonyeBoyuCm
        {
            get
            {
                double avail = RealGonyeBoyuCm;
                double max = GonyeBoyuMaxCm;
                if (max < 0) max = 0;
                return avail < max ? avail : max;
            }
        }

        public static double RealGonyeBoyuCm
        {
            get
            {
                double avail = KalinlikCm - 2.0 * PaspayiCm;
                return avail < 0 ? 0 : avail;
            }
        }

        public static int AltDonatiCapMm { get => Active.AltDonatiCapMm; set => Active.AltDonatiCapMm = value; }
        public static int UstDonatiCapMm { get => Active.UstDonatiCapMm; set => Active.UstDonatiCapMm = value; }
        public static double AltAralikCm { get => Active.AltAralikCm; set => Active.AltAralikCm = value; }
        public static double UstAralikCm { get => Active.UstAralikCm; set => Active.UstAralikCm = value; }

        public static bool AltGonyeSol { get => Active.AltGonyeSol; set => Active.AltGonyeSol = value; }
        public static bool AltGonyeSag { get => Active.AltGonyeSag; set => Active.AltGonyeSag = value; }
        public static bool UstGonyeSol { get => Active.UstGonyeSol; set => Active.UstGonyeSol = value; }
        public static bool UstGonyeSag { get => Active.UstGonyeSag; set => Active.UstGonyeSag = value; }

        /// <summary>Kapalıysa alt donatı çizilmez (konum şeması değişmez).</summary>
        public static bool AltDonatiCiz { get => Active.AltDonatiCiz; set => Active.AltDonatiCiz = value; }
        /// <summary>Kapalıysa üst donatı çizilmez (konum şeması değişmez).</summary>
        public static bool UstDonatiCiz { get => Active.UstDonatiCiz; set => Active.UstDonatiCiz = value; }

        /// <summary>Sehpa donatısı (mantar/radyede ayrı; çizim sonra).</summary>
        public static bool SehpaDonatisi { get => Active.SehpaDonatisi; set => Active.SehpaDonatisi = value; }

        private static string StorePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ST4Yardimci");
                return Path.Combine(dir, "oto_donati.ini");
            }
        }

        public static void EnsureLoaded()
        {
            lock (Sync)
            {
                if (_loaded) return;
                _loaded = true;
                try { LoadUnlocked(); }
                catch { /* varsayılanlar kalsın */ }
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
                        Directory.CreateDirectory(dir);

                    var sb = new StringBuilder();
                    sb.AppendLine("; ST4 Yardimci — Oto Donati (Radye + Mantar ayrı profiller)");
                    Write(sb, "Tip", Tip == OtoDonatiTip.MantarDoseme ? "Mantar" : "Radye");
                    WriteProfile(sb, "Radye", _radye);
                    WriteProfile(sb, "Mantar", _mantar);
                    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                }
                catch { }
            }
        }

        private static void WriteProfile(StringBuilder sb, string prefix, OtoDonatiProfile p)
        {
            if (p == null) return;
            Write(sb, prefix + "_KalinlikCm", p.KalinlikCm);
            Write(sb, prefix + "_PaspayiCm", p.PaspayiCm);
            Write(sb, prefix + "_UstKotM", p.UstKotM);
            Write(sb, prefix + "_GonyeBoyuMaxCm", p.GonyeBoyuMaxCm);
            Write(sb, prefix + "_AnalizEsigiCm", p.AnalizEsigiCm);
            Write(sb, prefix + "_AltDonatiCapMm", p.AltDonatiCapMm);
            Write(sb, prefix + "_UstDonatiCapMm", p.UstDonatiCapMm);
            Write(sb, prefix + "_AltAralikCm", p.AltAralikCm);
            Write(sb, prefix + "_UstAralikCm", p.UstAralikCm);
            Write(sb, prefix + "_AltGonyeSol", p.AltGonyeSol);
            Write(sb, prefix + "_AltGonyeSag", p.AltGonyeSag);
            Write(sb, prefix + "_UstGonyeSol", p.UstGonyeSol);
            Write(sb, prefix + "_UstGonyeSag", p.UstGonyeSag);
            Write(sb, prefix + "_AltDonatiCiz", p.AltDonatiCiz);
            Write(sb, prefix + "_UstDonatiCiz", p.UstDonatiCiz);
            Write(sb, prefix + "_SehpaDonatisi", p.SehpaDonatisi);
        }

        private static void Write(StringBuilder sb, string key, double v)
        {
            sb.Append(key).Append('=')
                .Append(v.ToString("0.####", CultureInfo.InvariantCulture))
                .AppendLine();
        }

        private static void Write(StringBuilder sb, string key, int v)
        {
            sb.Append(key).Append('=').Append(v).AppendLine();
        }

        private static void Write(StringBuilder sb, string key, bool v)
        {
            sb.Append(key).Append('=').Append(v ? "1" : "0").AppendLine();
        }

        private static void Write(StringBuilder sb, string key, string v)
        {
            sb.Append(key).Append('=').Append(v ?? "").AppendLine();
        }

        private static void LoadUnlocked()
        {
            string path = StorePath;
            if (!File.Exists(path)) return;

            // Eski tek-profilli anahtarlar (önek yok) → migrate.
            var legacy = OtoDonatiProfile.CreateRadyeDefault();
            bool hasLegacy = false;
            bool radyeTouched = false;
            bool mantarTouched = false;

            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line[0] == ';' || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                if (key.Equals("Tip", StringComparison.OrdinalIgnoreCase))
                {
                    Tip = val.StartsWith("M", StringComparison.OrdinalIgnoreCase)
                        ? OtoDonatiTip.MantarDoseme
                        : OtoDonatiTip.RadyeTemel;
                    continue;
                }

                if (TryApplyPrefixed(key, val, "Radye_", _radye))
                {
                    radyeTouched = true;
                    continue;
                }
                if (TryApplyPrefixed(key, val, "Mantar_", _mantar))
                {
                    mantarTouched = true;
                    continue;
                }

                if (TryApplyLegacy(key, val, legacy))
                    hasLegacy = true;
            }

            if (hasLegacy && !radyeTouched && !mantarTouched)
            {
                _radye.CopyFrom(legacy);
                _mantar.CopyFrom(legacy);
                // Eski radye kalınlığı mantara kopyalanmasın — mantar varsayılanına çek.
                if (_mantar.KalinlikCm >= 40)
                {
                    _mantar.KalinlikCm = 25;
                    _mantar.PaspayiCm = 3;
                }
            }
            else if (hasLegacy && !radyeTouched)
                _radye.CopyFrom(legacy);
            else if (hasLegacy && !mantarTouched)
                _mantar.CopyFrom(legacy);
        }

        private static bool TryApplyPrefixed(string key, string val, string prefix, OtoDonatiProfile p)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            string sub = key.Substring(prefix.Length);
            return TryApplyField(sub, val, p);
        }

        private static bool TryApplyLegacy(string key, string val, OtoDonatiProfile p) =>
            TryApplyField(key, val, p);

        private static bool TryApplyField(string key, string val, OtoDonatiProfile p)
        {
            if (key.Equals("KalinlikCm", StringComparison.OrdinalIgnoreCase) && TryD(val, out double kal))
            { p.KalinlikCm = kal; return true; }
            if (key.Equals("PaspayiCm", StringComparison.OrdinalIgnoreCase) && TryD(val, out double pas))
            { p.PaspayiCm = pas; return true; }
            if (key.Equals("UstKotM", StringComparison.OrdinalIgnoreCase) && TryD(val, out double kot))
            { p.UstKotM = kot; return true; }
            if (key.Equals("GonyeBoyuMaxCm", StringComparison.OrdinalIgnoreCase) && TryD(val, out double gmax))
            { p.GonyeBoyuMaxCm = gmax; return true; }
            if (key.Equals("AnalizEsigiCm", StringComparison.OrdinalIgnoreCase) && TryD(val, out double aes))
            { p.AnalizEsigiCm = aes; return true; }
            if (key.Equals("AltAralikCm", StringComparison.OrdinalIgnoreCase) && TryD(val, out double aa))
            { p.AltAralikCm = aa; return true; }
            if (key.Equals("UstAralikCm", StringComparison.OrdinalIgnoreCase) && TryD(val, out double ua))
            { p.UstAralikCm = ua; return true; }
            if (key.Equals("AltDonatiCapMm", StringComparison.OrdinalIgnoreCase) && TryI(val, out int ac))
            { p.AltDonatiCapMm = ac; return true; }
            if (key.Equals("UstDonatiCapMm", StringComparison.OrdinalIgnoreCase) && TryI(val, out int uc))
            { p.UstDonatiCapMm = uc; return true; }
            if (key.Equals("AltGonyeSol", StringComparison.OrdinalIgnoreCase))
            { p.AltGonyeSol = IsTrue(val); return true; }
            if (key.Equals("AltGonyeSag", StringComparison.OrdinalIgnoreCase))
            { p.AltGonyeSag = IsTrue(val); return true; }
            if (key.Equals("UstGonyeSol", StringComparison.OrdinalIgnoreCase))
            { p.UstGonyeSol = IsTrue(val); return true; }
            if (key.Equals("UstGonyeSag", StringComparison.OrdinalIgnoreCase))
            { p.UstGonyeSag = IsTrue(val); return true; }
            if (key.Equals("AltDonatiCiz", StringComparison.OrdinalIgnoreCase))
            { p.AltDonatiCiz = IsTrue(val); return true; }
            if (key.Equals("UstDonatiCiz", StringComparison.OrdinalIgnoreCase))
            { p.UstDonatiCiz = IsTrue(val); return true; }
            if (key.Equals("SehpaDonatisi", StringComparison.OrdinalIgnoreCase))
            { p.SehpaDonatisi = IsTrue(val); return true; }
            return false;
        }

        private static bool TryD(string s, out double v) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        private static bool TryI(string s, out int v) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

        private static bool IsTrue(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("evet", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }
    }
}
