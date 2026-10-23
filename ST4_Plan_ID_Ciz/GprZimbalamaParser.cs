using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace ST4PlanIdCiz
{
    /// <summary>GPR "RADYE TEMEL KOLON ZIMBALAMA HESABI": zımbalama donatısı gereken kolon kaydı.</summary>
    public sealed class GprZimbalamaKolon
    {
        /// <summary>GPR kolon adı (örn. S126).</summary>
        public string Ad;
        /// <summary>Ad içindeki sayı (126): kat × adım + kolon no.</summary>
        public int Numara;
        /// <summary>Faydalı yükseklik d (cm).</summary>
        public double DCm;
        /// <summary>STA önerisi: toplam etriye adedi, çap (mm), aralık (cm).</summary>
        public int Adet;
        public int CapMm;
        public int AralikCm;
        /// <summary>Gerekli toplam etriye kesit alanı Asb (cm²).</summary>
        public double AsbCm2;
        /// <summary>Zımbalama çevresi: sağlanan / gerekli (cm).</summary>
        public double UpSaglanan;
        public double UpGerekli;
    }

    /// <summary>
    /// STA4CAD GPR radye zımbalama tablosu. Kolon başlığı (S###) ve ardından "d= ..." satırı gelir;
    /// "ZIMBALAMA DONATISI GEREKLI" görülen kolonda "Zimbalama donatisi:28ø8/15=28.2> Asb= 27.03 cm²" okunur.
    /// </summary>
    public static class GprZimbalamaParser
    {
        private static readonly Regex KolonRx = new Regex(@"^\s*S(\d+)\s*$", RegexOptions.Compiled);
        private static readonly Regex DRx = new Regex(@"^\s*d\s*=\s*([\d.]+)\s*cm", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DonatiRx = new Regex(@"Zimbalama donatisi\s*:\s*(\d+)\D+(\d+)\s*/\s*(\d+)\s*=\s*[\d.]+\s*>\s*Asb\s*=\s*([\d.]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CevreRx = new Regex(@"Zimbalama cevresi\s*:\s*Up\s*=\s*([\d.]+)\s*>\s*([\d.]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>ST4 yanındaki GPR'den zımbalama donatısı gereken kolonlar. GPR yoksa veya tablo yoksa boş liste.</summary>
        public static List<GprZimbalamaKolon> TryRead(string st4Path, out string not)
        {
            not = null;
            var sonuc = new List<GprZimbalamaKolon>();
            string gpr = Path.ChangeExtension(st4Path ?? string.Empty, ".GPR");
            if (!File.Exists(gpr)) gpr = Path.ChangeExtension(st4Path ?? string.Empty, ".gpr");
            if (!File.Exists(gpr))
            {
                not = "GPR yok";
                return sonuc;
            }
            try
            {
                WindowsAnsiEncodings.EnsureRegistered();
                return Parse(WindowsAnsiEncodings.ReadAllLines(gpr));
            }
            catch (Exception ex)
            {
                not = ex.Message;
                return sonuc;
            }
        }

        public static List<GprZimbalamaKolon> Parse(IList<string> lines)
        {
            var byAd = new Dictionary<string, GprZimbalamaKolon>(StringComparer.OrdinalIgnoreCase);
            var sira = new List<string>();
            if (lines == null) return new List<GprZimbalamaKolon>();
            int bas = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if ((lines[i] ?? string.Empty).IndexOf("KOLON ZIMBALAMA HESABI", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bas = i;
                    break;
                }
            }
            if (bas < 0) return new List<GprZimbalamaKolon>();

            string ad = null;
            int numara = 0;
            double d = 0;
            bool gerekli = false;
            GprZimbalamaKolon acik = null;
            for (int i = bas + 1; i < lines.Count; i++)
            {
                string line = lines[i] ?? string.Empty;
                Match km = KolonRx.Match(line);
                if (km.Success && i + 4 < lines.Count && SonrakiD(lines, i, out double dYeni))
                {
                    ad = "S" + km.Groups[1].Value;
                    int.TryParse(km.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out numara);
                    d = dYeni;
                    gerekli = false;
                    acik = null;
                    continue;
                }
                if (ad == null) continue;
                if (line.IndexOf("ZIMBALAMA DONATISI GEREKLI", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    gerekli = true;
                    continue;
                }
                Match dm = DonatiRx.Match(line);
                if (gerekli && dm.Success)
                {
                    acik = new GprZimbalamaKolon
                    {
                        Ad = ad,
                        Numara = numara,
                        DCm = d,
                        Adet = Int(dm.Groups[1].Value),
                        CapMm = Int(dm.Groups[2].Value),
                        AralikCm = Int(dm.Groups[3].Value),
                        AsbCm2 = Dbl(dm.Groups[4].Value)
                    };
                    if (!byAd.ContainsKey(ad)) sira.Add(ad);
                    byAd[ad] = acik;
                    continue;
                }
                Match cm = CevreRx.Match(line);
                if (acik != null && cm.Success)
                {
                    acik.UpSaglanan = Dbl(cm.Groups[1].Value);
                    acik.UpGerekli = Dbl(cm.Groups[2].Value);
                }
            }
            var list = new List<GprZimbalamaKolon>();
            foreach (string k in sira)
            {
                GprZimbalamaKolon z = byAd[k];
                if (z.CapMm > 0 && z.AralikCm > 0) list.Add(z);
            }
            return list;
        }

        /// <summary>Kolon başlığından sonraki birkaç satırda "d= 43 cm" var mı (koordinat satırları atlanır).</summary>
        private static bool SonrakiD(IList<string> lines, int i, out double d)
        {
            d = 0;
            for (int k = i + 1; k < lines.Count && k <= i + 3; k++)
            {
                Match m = DRx.Match(lines[k] ?? string.Empty);
                if (m.Success)
                {
                    d = Dbl(m.Groups[1].Value);
                    return true;
                }
            }
            return false;
        }

        private static int Int(string s)
        {
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v);
            return v;
        }

        private static double Dbl(string s)
        {
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
            return v;
        }
    }
}
