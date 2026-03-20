using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ST4PlanIdCiz
{
    /// <summary>GPR döşeme satırından okunan X ve Y donatı hücre metinleri (ayrı renklerle çizim için).</summary>
    public readonly struct GprDosemeDonatiXy
    {
        public GprDosemeDonatiXy(string x, string y)
        {
            X = x ?? string.Empty;
            Y = y ?? string.Empty;
        }
        public string X { get; }
        public string Y { get; }
    }

    /// <summary>
    /// GPR içinde ilk "DÖŞEME BETONARME HESAP SONUÇLARI" ile ilk "KİRİŞ VE PANEL BİLGİLERİ" arasındaki
    /// döşeme satırlarından (X / Y çiftleri) donatı özetini okur. Kabuk implementasyon; kurallar sonra sıkılaştırılacak.
    /// </summary>
    public static class GprDosemeDonatiParser
    {
        private static readonly Regex RxSlabIdX = new Regex(
            @"([A-Z]{1,2}\d?-\d+)\s+X\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>\b bazı GPR satırlarında X sonrası özel karakterde başarısız olabiliyor.</summary>
        private static readonly Regex RxSlabIdXLoose = new Regex(
            @"([A-Z]{1,2}\d?-\d+)\s+X",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex RxSlabY = new Regex(
            @"d\s*=\s*\d+\s*cm\s+Y\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>cm ile Y arasında ¡ / fazla boşluk.</summary>
        private static readonly Regex RxSlabYLoose = new Regex(
            @"d\s*=\s*\d+\s*cm[\s\u00A1\uFFFD]{0,12}Y\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Birden fazla kodlama dener; <b>en çok döşeme anahtarı</b> üreten sonucu seçer (ilk geçerli değil).
        /// UTF-8 BOM varsa UTF-8 denemesi öne alınır.
        /// </summary>
        public static bool TryParse(string gprFilePath, out Dictionary<string, GprDosemeDonatiXy> slabIdToDonatiXy, out string error)
        {
            slabIdToDonatiXy = null;
            error = null;
            if (string.IsNullOrWhiteSpace(gprFilePath) || !File.Exists(gprFilePath))
            {
                error = "GPR dosyasi yok.";
                return false;
            }

            bool utf8Bom = false;
            try
            {
                var bom = new byte[3];
                using (var fs = File.OpenRead(gprFilePath))
                {
                    int n = fs.Read(bom, 0, 3);
                    utf8Bom = n == 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF;
                }
            }
            catch
            {
                /* yoksay */
            }

            var encodings = new List<Encoding>(5);
            if (utf8Bom)
                encodings.Add(Encoding.UTF8);
            encodings.Add(Encoding.GetEncoding(1254));
            if (!utf8Bom)
                encodings.Add(Encoding.UTF8);
            try
            {
                encodings.Add(Encoding.GetEncoding("iso-8859-9"));
            }
            catch
            {
                /* bazı ortamlarda yok */
            }

            Dictionary<string, GprDosemeDonatiXy> best = null;
            int bestCount = 0;
            foreach (Encoding enc in encodings)
            {
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(gprFilePath, enc);
                }
                catch
                {
                    continue;
                }

                if (!TryParseLines(lines, out Dictionary<string, GprDosemeDonatiXy> dict) || dict == null || dict.Count == 0)
                    continue;
                if (dict.Count > bestCount)
                {
                    bestCount = dict.Count;
                    best = dict;
                }
            }

            if (best != null)
            {
                slabIdToDonatiXy = best;
                return true;
            }

            error = "DÖŞEME BETONARME bolumu okunamadi veya kodlama uyusmazligi (1254 / UTF-8 / ISO-8859-9 ile denendi).";
            return false;
        }

        private static bool TryParseLines(string[] lines, out Dictionary<string, GprDosemeDonatiXy> dict)
        {
            dict = null;
            int start = FindDosemeBetonarmeStartLine(lines);
            if (start < 0)
                return false;

            int end = FindKirisVePanelLine(lines, start + 1);
            if (end < 0)
                return false;

            var d = new Dictionary<string, GprDosemeDonatiXy>(StringComparer.OrdinalIgnoreCase);
            string pendingKey = null;
            string pendingXDon = null;

            for (int i = start; i < end; i++)
            {
                string raw = lines[i];
                if (string.IsNullOrWhiteSpace(raw)) continue;

                if (raw.Length > 4 && char.IsDigit(raw[0]) && raw.StartsWith("130,", StringComparison.Ordinal))
                {
                    if (i + 1 < end)
                    {
                        ProcessContentLine(lines[i + 1], d, ref pendingKey, ref pendingXDon);
                        i++;
                    }
                    continue;
                }

                ProcessContentLine(raw, d, ref pendingKey, ref pendingXDon);
            }

            if (pendingKey != null)
                d[pendingKey] = ToXy(pendingXDon, null);

            if (d.Count == 0)
                return false;

            dict = d;
            return true;
        }

        private static GprDosemeDonatiXy ToXy(string x, string y)
        {
            string xs = string.IsNullOrWhiteSpace(x) ? string.Empty : KolonDonatiTableDrawer.NormalizeDiameterSymbol(x.Trim());
            string ys = string.IsNullOrWhiteSpace(y) ? string.Empty : KolonDonatiTableDrawer.NormalizeDiameterSymbol(y.Trim());
            return new GprDosemeDonatiXy(xs, ys);
        }

        /// <summary>GPR tablo satırı ¡ ile başlar; X/Y regex'leri harf/d ile başlamalıdır.</summary>
        private static string StripLeadingGprTableMarkers(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            line = line.Replace("\u00C2\u00A1", "\u00A1");
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                if (c == '\u00A1' || c == '\uFFFD' || c == '\uFEFF' || char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }
                // Bozuk UTF-8 tek bayt okumasında başta '?' kalabilir (DB-01 öncesi)
                if (c == '?' && i + 1 < line.Length && line[i + 1] == 'D')
                {
                    i++;
                    continue;
                }
                break;
            }
            return i > 0 ? line.Substring(i) : line;
        }

        /// <summary>GPR tablodaki döşeme anahtarı (DB-12); Unicode tire → '-', büyük harf — ST4 ile eşleşme için.</summary>
        private static string NormalizeDosemeGprSlabKeyFromMatch(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            var sb = new StringBuilder(key.Trim().Length);
            foreach (char ch in key.Trim())
            {
                if (ch == '\u2013' || ch == '\u2014' || ch == '\u2212') sb.Append('-');
                else sb.Append(char.ToUpperInvariant(ch));
            }
            return sb.ToString();
        }

        private static void ProcessContentLine(
            string raw,
            Dictionary<string, GprDosemeDonatiXy> dict,
            ref string pendingKey,
            ref string pendingXDon)
        {
            string line = raw ?? string.Empty;
            line = line.Replace("\u00C2\u00A1", "\u00A1");
            if (string.IsNullOrWhiteSpace(line)) return;

            // Donatı hücresi ¡ sütunlarından okunur; eşleştirme baştaki ¡ sonrası metinde yapılır.
            string lineForMatch = StripLeadingGprTableMarkers(line);

            var mX = RxSlabIdX.Match(lineForMatch);
            if (!mX.Success)
                mX = RxSlabIdXLoose.Match(lineForMatch);
            if (mX.Success)
            {
                string key = NormalizeDosemeGprSlabKeyFromMatch(mX.Groups[1].Value);
                string don = KolonDonatiTableDrawer.ExtractGprDonatiCellFromLine(line);
                if (string.IsNullOrWhiteSpace(don))
                    don = ExtractDonatiCellFromGprLineFallback(line);
                if (pendingKey != null)
                    dict[pendingKey] = ToXy(pendingXDon, null);
                pendingKey = key;
                pendingXDon = string.IsNullOrWhiteSpace(don) ? string.Empty : don.Trim();
                return;
            }

            bool yRow = RxSlabY.IsMatch(lineForMatch)
                || RxSlabYLoose.IsMatch(lineForMatch)
                || (pendingKey != null && LooksLikeGprDosemeYRow(lineForMatch));
            if (yRow && pendingKey != null)
            {
                string yDon = KolonDonatiTableDrawer.ExtractGprDonatiCellFromLine(line);
                if (string.IsNullOrWhiteSpace(yDon))
                    yDon = ExtractDonatiCellFromGprLineFallback(line);
                dict[pendingKey] = ToXy(pendingXDon, yDon);
                pendingKey = null;
                pendingXDon = null;
                return;
            }
        }

        /// <summary>d=..cm ... Y satırı; regex kaçırırsa son çare (pending X varken).</summary>
        private static bool LooksLikeGprDosemeYRow(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int d = s.IndexOf("d=", StringComparison.OrdinalIgnoreCase);
            if (d < 0) return false;
            if (s.IndexOf("cm", StringComparison.OrdinalIgnoreCase) < d) return false;
            return Regex.IsMatch(s, @"\bY\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>¡ ayracı veya KolonDonatiTableDrawer sütun sayısı uyuşmazsa son sütundan donatı metnini alır.</summary>
        private static string ExtractDonatiCellFromGprLineFallback(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            line = line.Replace("\u00C2\u00A1", "\u00A1");
            string[] parts = line.Split('\u00A1');
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                string t = parts[i].Trim();
                if (t.Length == 0) continue;
                if (Regex.IsMatch(t, @"\d+\s*/\s*\d+")) return t;
                if (t.IndexOf('\u00F8') >= 0 || t.IndexOf('\u00D8') >= 0) return t;
                if (t.IndexOf("ø", StringComparison.OrdinalIgnoreCase) >= 0) return t;
                if (t.IndexOf("(govde)", StringComparison.OrdinalIgnoreCase) >= 0) return t;
                if (t.IndexOf("(etriye)", StringComparison.OrdinalIgnoreCase) >= 0) return t;
                if (t.IndexOf("(d", StringComparison.OrdinalIgnoreCase) >= 0 && t.IndexOf(')') >= 0) return t;
            }
            return null;
        }

        private static int FindDosemeBetonarmeStartLine(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.IndexOf("BETONARME", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (l.IndexOf("HESAP", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (l.IndexOf("SONU", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (l.IndexOf("KOLON", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                bool hasDoseme = l.IndexOf("DÖŞEME", StringComparison.OrdinalIgnoreCase) >= 0
                    || l.IndexOf("DŞEME", StringComparison.OrdinalIgnoreCase) >= 0
                    || l.IndexOf("EME BETONARME", StringComparison.OrdinalIgnoreCase) >= 0
                    || (l.IndexOf("DEME", StringComparison.OrdinalIgnoreCase) >= 0
                        && l.IndexOf("DÖŞEME", StringComparison.OrdinalIgnoreCase) < 0
                        && l.IndexOf("KİRİŞ", StringComparison.OrdinalIgnoreCase) < 0
                        && l.IndexOf("KIRIS", StringComparison.OrdinalIgnoreCase) < 0);
                if (hasDoseme)
                    return i;
            }
            return -1;
        }

        private static int FindKirisVePanelLine(string[] lines, int searchFrom)
        {
            for (int i = searchFrom; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.IndexOf("PANEL", StringComparison.OrdinalIgnoreCase) < 0) continue;
                // "KİRİŞ/PANEL DUVAR ..." bitiş değil; "KİRİŞ VE PANEL BİLGİLERİ" olmalı ( VE zorunlu).
                if (l.IndexOf(" VE ", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (l.IndexOf("DUVAR", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                bool hasBilgi = l.IndexOf("BILG", StringComparison.OrdinalIgnoreCase) >= 0
                    || l.IndexOf("BİLG", StringComparison.OrdinalIgnoreCase) >= 0
                    || l.IndexOf("BÝLG", StringComparison.OrdinalIgnoreCase) >= 0
                    || (l.IndexOf("LG", StringComparison.OrdinalIgnoreCase) >= 0
                        && l.IndexOf("PANEL", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!hasBilgi) continue;

                int veIdx = l.IndexOf(" VE ", StringComparison.OrdinalIgnoreCase);
                string beforeVe = veIdx > 0 ? l.Substring(0, veIdx) : string.Empty;
                bool hasKiris = beforeVe.IndexOf("KIR", StringComparison.OrdinalIgnoreCase) >= 0
                    || beforeVe.IndexOf("KİR", StringComparison.OrdinalIgnoreCase) >= 0
                    || beforeVe.IndexOf("KÝR", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hasKiris && !Regex.IsMatch(beforeVe, @"(?i)k[^\s]{0,16}r"))
                    continue;
                return i;
            }
            return -1;
        }
    }
}
