using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>Kolon donatı tablosu: GPR/PRN'den veri okur, ST4 kat isimleriyle tabloyu çizer. Birim: cm.</summary>
    public sealed class KolonDonatiTableDrawer
    {
        private const string LayerGrid = "donatiplus.com";
        private const string LayerPenc4 = "PENC4";
        private const string LayerPenc2 = "PENC2";
        private const string LayerHeader = "PENC4";
        private const string LayerYazi = "YAZI (BEYKENT)";
        private const string LayerDonatiYazisi = "DONATI YAZISI (BEYKENT)";
        private const string LayerKolonIsmi = "KOLON ISMI (BEYKENT)";
        private const string LayerSubasman = "SUBASMAN (BEYKENT)";
        private const string LayerCizgi = "CIZGI (BEYKENT)";
        private const string LayerKot = "KOT (BEYKENT)";
        private const string LayerKirisIsmi = "KIRIS ISMI (BEYKENT)";
        private const short AciThinLine = 251;
        private const short AciLabelGray = 252;
        private const double ColWidthKatNo = 50;
        private const double ColWidthSubHeader = 100;
        private const double ColWidthFloor = 150;
        private const double RowHeightHeader = 50;
        private const double RowHeightDataBlock = 75;  // 3 x 25
        private const double TextHeightMain = 11.5;
        private const double TextHeightSmall = 9.0;
        private const double TextWidthFactor = 0.7;
        private const double Penc4TextShiftRightCm = 5.0;
        private const string TextStyleYazi = "YAZI (BEYKENT)";
        private const string FontName = "Bahnschrift Light Condensed";
        /// <summary>Bx satırı 9. sütun boyuna donatı — boyutun 25 cm altı, DONATI YAZISI.</summary>
        private const double DonatiBoyunaBoyutAltinaCm = 25.0;
        /// <summary>By satırı 9. sütun etriye — boyutun 50 cm altı, DONATI YAZISI.</summary>
        private const double EtriyeBoyutAltinaCm = 50.0;

        /// <summary>Donatı notasyonunda fi / φ → ø (U+00F8, Alt+0248).</summary>
        public static string NormalizeDiameterSymbol(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("\uFB01", "\u00F8");
            s = s.Replace("\u03C6", "\u00F8").Replace("\u03A6", "\u00F8");
            s = Regex.Replace(s, @"(?<=\d)\s*fi\s*(?=\d)", "\u00F8", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"(?<=\d)fi(?=\d)", "\u00F8", RegexOptions.IgnoreCase);
            return s;
        }

        private static string StripGovdeSuffix(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return Regex.Replace(s.Trim(), @"\s*\(govde\)\s*$", "", RegexOptions.IgnoreCase).Trim();
        }

        private static readonly Regex GprColumnIdInDonatiCell = new Regex(@"^(?:SB|SZ|SA|SC)\d+$|^(?:SB|SZ|SA|SC)-\d+$|^(?:S\d+)-\d+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>UTF-8 ¡ (C2 A1) yanlış okunduysa tek ¡ yap.</summary>
        private static string NormalizeGprTableLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            return line.Replace("\u00C2\u00A1", "\u00A1");
        }

        /// <summary>GPR: ¡ sütun ayırıcı; 1-tabanlı sütun no (9 = donatı metni).</summary>
        private static string GetGprColumnByInvertedExclamation(string line, int oneBasedColumn)
        {
            if (string.IsNullOrEmpty(line) || oneBasedColumn < 1) return null;
            var parts = line.Split('\u00A1');
            if (parts.Length <= oneBasedColumn) return null;
            string p = parts[oneBasedColumn].Trim();
            return string.IsNullOrEmpty(p) ? null : p;
        }

        private static bool LooksLikeGprDonatiOrEtriyeText(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return false;
            string s = t.Trim();
            if (GprColumnIdInDonatiCell.IsMatch(s)) return false;
            if (s.IndexOf("(govde)", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.IndexOf("(etriye)", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.IndexOf('\u00F8') >= 0 || s.IndexOf('\u00D8') >= 0) return true;
            if (s.IndexOf("ø", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (Regex.IsMatch(s, @"\d+\s*[x×\u00D7]\s*\d+")) return true;
            if (Regex.IsMatch(s, @"\d+\s*/\s*\d+")) return true;
            if (Regex.IsMatch(s, @"\d+\s*fi\s*\d+", RegexOptions.IgnoreCase)) return true;
            return false;
        }

        private static string GetGprDonatiCell(string line, bool isGpr)
        {
            if (!isGpr)
                return StripGovdeSuffix(GetLastTableColumn(line).Trim());
            line = NormalizeGprTableLine(line ?? string.Empty);
            string c9 = GetGprColumnByInvertedExclamation(line, 9);
            c9 = string.IsNullOrEmpty(c9) ? null : StripGovdeSuffix(c9.Trim());
            if (!string.IsNullOrEmpty(c9) && LooksLikeGprDonatiOrEtriyeText(c9))
                return c9;
            var parts = line.Split('\u00A1');
            for (int i = parts.Length - 1; i >= 1; i--)
            {
                string t = StripGovdeSuffix(parts[i].Trim());
                if (string.IsNullOrEmpty(t)) continue;
                if (LooksLikeGprDonatiOrEtriyeText(t))
                    return t;
            }
            if (!string.IsNullOrEmpty(c9) && !GprColumnIdInDonatiCell.IsMatch(c9))
                return c9;
            return StripGovdeSuffix(GetLastTableColumn(line).Trim());
        }

        /// <summary>GPR döşeme / kolon tablo satırından donatı hücresi (¡ sütunları, kolon tablosu ile aynı mantık).</summary>
        public static string ExtractGprDonatiCellFromLine(string line)
        {
            return GetGprDonatiCell(line ?? string.Empty, true);
        }

        /// <summary>GPR tablo satırı: ¡ hem UTF-8 (C2 A1) hem tek bayt A1 olabilir; string bölme güvenilmez. Ham bayıttan hücreler ayrılır.</summary>
        private static int IndexAfterGprCoordinatePrefix(ReadOnlySpan<byte> line)
        {
            if (line.Length < 11) return 0;
            if (line[0] < (byte)'0' || line[0] > (byte)'9') return 0;
            int i = 0;
            for (int part = 0; part < 6; part++)
            {
                if (part == 4 && i < line.Length && line[i] == (byte)'-') i++;
                if (i >= line.Length || line[i] < (byte)'0' || line[i] > (byte)'9') return 0;
                while (i < line.Length && line[i] >= (byte)'0' && line[i] <= (byte)'9') i++;
                if (part < 5)
                {
                    if (i >= line.Length || line[i] != (byte)',') return 0;
                    i++;
                }
            }
            while (i < line.Length && (line[i] == (byte)' ' || line[i] == (byte)'\t')) i++;
            return i;
        }

        private static List<byte[]> SplitGprRowCellsRaw(ReadOnlySpan<byte> content)
        {
            var cells = new List<byte[]>();
            int segStart = 0;
            int i = 0;
            while (i < content.Length)
            {
                if (i + 1 < content.Length && content[i] == 0xC2 && content[i + 1] == 0xA1)
                {
                    cells.Add(content.Slice(segStart, i - segStart).ToArray());
                    i += 2;
                    segStart = i;
                    continue;
                }
                if (content[i] == 0xA1 && (i == 0 || content[i - 1] != 0xC2))
                {
                    cells.Add(content.Slice(segStart, i - segStart).ToArray());
                    i += 1;
                    segStart = i;
                    continue;
                }
                i++;
            }
            cells.Add(content.Slice(segStart).ToArray());
            return cells;
        }

        private static int ScoreGprDonatiCellCandidate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            string t = s.Trim();
            if (GprColumnIdInDonatiCell.IsMatch(t)) return -100;
            int sc = 0;
            if (t.IndexOf("(govde)", StringComparison.OrdinalIgnoreCase) >= 0) sc += 25;
            if (t.IndexOf("(etriye)", StringComparison.OrdinalIgnoreCase) >= 0) sc += 25;
            if (t.IndexOf('\uFFFD') >= 0) sc -= 30;
            if (Regex.IsMatch(t, @"[øØ\u00F8].*\d|\d.*[øØ\u00F8]")) sc += 10;
            if (Regex.IsMatch(t, @"\d+\s*[x×\u00D7]\s*\d+")) sc += 8;
            if (Regex.IsMatch(t, @"/\s*\d+")) sc += 4;
            return sc;
        }

        private static string DecodeGprDonatiCellBytes(byte[] seg)
        {
            if (seg == null || seg.Length == 0) return string.Empty;
            var utf8 = new UTF8Encoding(false, false);
            var w1252 = Encoding.GetEncoding(1252);
            Encoding w1254 = null;
            try { w1254 = Encoding.GetEncoding(1254); } catch { w1254 = w1252; }
            string best = utf8.GetString(seg);
            int bestSc = ScoreGprDonatiCellCandidate(best);
            foreach (var enc in new[] { w1252, w1254 })
            {
                string c = enc.GetString(seg);
                int sc = ScoreGprDonatiCellCandidate(c);
                if (sc > bestSc) { bestSc = sc; best = c; }
            }
            return best;
        }

        private static string GprDonatiRegexFallback(ReadOnlySpan<byte> content)
        {
            byte[] arr = content.Length == 0 ? Array.Empty<byte>() : content.ToArray();
            var utf8 = new UTF8Encoding(false, false);
            foreach (var enc in new[] { utf8, Encoding.GetEncoding(1252), Encoding.GetEncoding(1254) })
            {
                string s = enc.GetString(arr);
                var m = Regex.Match(s, @"([\d\s+x×\u00D7\u00F8Ø/\.\(\)]+)\(govde\)", RegexOptions.IgnoreCase);
                if (m.Success && m.Groups[1].Value.Trim().Length >= 3)
                    return m.Groups[1].Value.Trim() + "(govde)";
                m = Regex.Match(s, @"([\d\s+x×\u00D7\u00F8Ø/\.\(\)]+)\(etriye\)", RegexOptions.IgnoreCase);
                if (m.Success && m.Groups[1].Value.Trim().Length >= 2)
                    return m.Groups[1].Value.Trim() + "(etriye)";
            }
            return null;
        }

        private static string ExtractGprDonatiCellFromRawLine(byte[] lineBytes)
        {
            if (lineBytes == null || lineBytes.Length == 0) return null;
            int off = IndexAfterGprCoordinatePrefix(lineBytes);
            ReadOnlySpan<byte> content = lineBytes.AsSpan(off);
            while (content.Length > 0 && (content[0] == (byte)' ' || content[0] == (byte)'\t'))
                content = content.Slice(1);
            if (content.Length == 0) return null;
            var cells = SplitGprRowCellsRaw(content);
            if (cells.Count < 4)
                return GprDonatiRegexFallback(content);
            int[] tryIdx = { 9, 10, 8, 11, 7, 6 };
            string best = null;
            int bestSc = int.MinValue;
            foreach (int idx in tryIdx)
            {
                if (idx >= cells.Count) continue;
                string dec = DecodeGprDonatiCellBytes(cells[idx]).Trim();
                int sc = ScoreGprDonatiCellCandidate(dec);
                if (sc > bestSc) { bestSc = sc; best = dec; }
            }
            if (!string.IsNullOrEmpty(best) && (bestSc >= 8 || LooksLikeGprDonatiOrEtriyeText(best)))
                return best;
            string fb = GprDonatiRegexFallback(content);
            if (!string.IsNullOrEmpty(fb)) return fb;
            return string.IsNullOrEmpty(best) ? null : best;
        }

        private static string ReplaceTimesWithAsciiX(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\u00D7", "x");
        }

        private static string FormatDonatiDisplay(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return ReplaceTimesWithAsciiX(NormalizeDiameterSymbol(StripGovdeSuffix(raw)));
        }

        /// <summary>
        /// STA4CAD çoklu bodrum GPR: S4B-01 (kısaltma 4B-). Eski/alternatif: SB4-01. Tek bodrum: SB-01 ↔ S1B/SB1.
        /// </summary>
        private static List<string> GprDataKeysForFloorColumn(string storyPrefix, bool hyphenBeforeColNo, int colNo)
        {
            var keys = new List<string>();
            if (string.IsNullOrEmpty(storyPrefix)) return keys;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string k) { if (seen.Add(k)) keys.Add(k); }
            void AddForPrefix(string p)
            {
                if (string.IsNullOrEmpty(p)) return;
                string n = colNo.ToString(CultureInfo.InvariantCulture);
                string d2 = colNo.ToString("D2", CultureInfo.InvariantCulture);
                string d3 = colNo.ToString("D3", CultureInfo.InvariantCulture);
                if (hyphenBeforeColNo)
                {
                    Add(p + "-" + n);
                    Add(p + "-" + d2);
                    if (colNo < 1000) Add(p + "-" + d3);
                }
                else
                {
                    Add(p + n);
                    Add(p + d2);
                    Add(p + d3);
                }
            }
            AddForPrefix(storyPrefix);
            var mSnb = Regex.Match(storyPrefix, @"^S(\d+)B$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (mSnb.Success)
                AddForPrefix("SB" + mSnb.Groups[1].Value);
            var mSbn = Regex.Match(storyPrefix, @"^SB(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (mSbn.Success)
                AddForPrefix("S" + mSbn.Groups[1].Value + "B");
            if (storyPrefix.Equals("S1B", StringComparison.OrdinalIgnoreCase) ||
                storyPrefix.Equals("SB1", StringComparison.OrdinalIgnoreCase))
                AddForPrefix("SB");
            return keys;
        }

        private static bool TryGetDonatiForFloorColumn(Dictionary<string, (string ebat, string donati, string etriye)> data, string storyPrefix, bool hyphenBeforeColNo, int colNo, out string donati)
        {
            donati = null;
            if (data == null || data.Count == 0) return false;
            foreach (var key in GprDataKeysForFloorColumn(storyPrefix, hyphenBeforeColNo, colNo))
            {
                if (data.TryGetValue(key, out var t) && !string.IsNullOrWhiteSpace(t.donati))
                {
                    donati = t.donati;
                    return true;
                }
            }
            return false;
        }

        private static bool TryGetEtriyeForFloorColumn(Dictionary<string, (string ebat, string donati, string etriye)> data, string storyPrefix, bool hyphenBeforeColNo, int colNo, out string etriye)
        {
            etriye = null;
            if (data == null || data.Count == 0) return false;
            foreach (var key in GprDataKeysForFloorColumn(storyPrefix, hyphenBeforeColNo, colNo))
            {
                if (data.TryGetValue(key, out var t) && !string.IsNullOrWhiteSpace(t.etriye))
                {
                    etriye = t.etriye;
                    return true;
                }
            }
            return false;
        }

        /// <summary>GPR anahtarından kat öneği ve kolon no (S4B-01 STA4CAD, SB01, SB2-01, SB-21, S1-02).</summary>
        private static bool TryParseGprStoryKey(string key, out string storyPrefix, out int columnNo)
        {
            storyPrefix = null;
            columnNo = 0;
            if (string.IsNullOrWhiteSpace(key)) return false;
            key = key.Trim().ToUpperInvariant();
            var m = Regex.Match(key, @"^(S\d+)B-(\d+)$");
            if (m.Success)
            {
                storyPrefix = m.Groups[1].Value + "B";
                return int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out columnNo);
            }
            // Çoklu bodrum / kat: SB2-01 → önek SB2 (önce tireli çok haneli biçim)
            m = Regex.Match(key, @"^(SB|SZ|SA|SC)(\d+)-(\d+)$");
            if (m.Success)
            {
                storyPrefix = m.Groups[1].Value + m.Groups[2].Value;
                return int.TryParse(m.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out columnNo);
            }
            m = Regex.Match(key, @"^(SB|SZ|SA|SC)(\d+)$");
            if (m.Success)
            {
                storyPrefix = m.Groups[1].Value;
                return int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out columnNo);
            }
            m = Regex.Match(key, @"^(SB|SZ|SA|SC)-(\d+)$");
            if (m.Success)
            {
                storyPrefix = m.Groups[1].Value;
                return int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out columnNo);
            }
            m = Regex.Match(key, @"^(S\d+)-(\d+)$");
            if (m.Success)
            {
                storyPrefix = m.Groups[1].Value;
                return int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out columnNo);
            }
            return false;
        }

        /// <summary>GPR 9. sütun etriye: (etriye) ve [ ] kaldırılır; başta rakam+ø ise araya x → 3xø8/15/8.</summary>
        private static string FormatEtriyeForTableDisplay(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string s = raw.Trim();
            s = Regex.Replace(s, @"\s*\(etriye\)\s*$", "", RegexOptions.IgnoreCase).Trim();
            s = s.Replace("[", string.Empty).Replace("]", string.Empty);
            s = NormalizeDiameterSymbol(s.Trim());
            s = Regex.Replace(s, @"^(\d+)(\u00F8|\u03C6|\u03A6)", "$1x$2");
            s = ReplaceTimesWithAsciiX(s);
            return string.IsNullOrEmpty(s) ? null : s;
        }

        private static int CountGprInvertedExclamationColumns(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return s.Split('\u00A1').Length;
        }

        private static byte[][] SplitFileIntoRawLines(byte[] b)
        {
            var lines = new List<byte[]>(Math.Max(256, b.Length / 64));
            int start = 0;
            for (int i = 0; i <= b.Length; i++)
            {
                if (i < b.Length && b[i] != (byte)'\n' && b[i] != (byte)'\r')
                    continue;
                int len = i - start;
                ReadOnlySpan<byte> span = len <= 0 ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>(b, start, len);
                while (span.Length > 0 && span[span.Length - 1] == (byte)'\r')
                    span = span.Slice(0, span.Length - 1);
                lines.Add(span.Length == 0 ? Array.Empty<byte>() : span.ToArray());
                if (i < b.Length && b[i] == (byte)'\r' && i + 1 < b.Length && b[i + 1] == (byte)'\n')
                    i++;
                start = i + 1;
            }
            return lines.ToArray();
        }

        /// <summary>GPR: satır metni (başlık, Panel); donatı hücresi ayrıca ham bayıttan okunur.</summary>
        private static void LoadGprFileLines(string filePath, out string[] textLines, out byte[][] rawLines)
        {
            byte[] b = File.ReadAllBytes(filePath);
            rawLines = SplitFileIntoRawLines(b);
            var utf8 = new UTF8Encoding(false, false);
            var win1252 = Encoding.GetEncoding(1252);
            textLines = new string[rawLines.Length];
            for (int k = 0; k < rawLines.Length; k++)
            {
                byte[] lb = rawLines[k];
                string u = utf8.GetString(lb);
                string w = win1252.GetString(lb);
                int cu = CountGprInvertedExclamationColumns(u);
                int cw = CountGprInvertedExclamationColumns(w);
                bool uBad = u.IndexOf('\uFFFD') >= 0;
                textLines[k] = (uBad || cu < 8) && cw >= 10 ? w : u;
            }
        }

        private static string[] ReadGprPrnLines(string filePath)
        {
            byte[] b = File.ReadAllBytes(filePath);
            int n = Math.Min(b.Length, 600000);
            int utf8Delim = 0;
            int winDelim = 0;
            for (int i = 0; i < n - 1; i++)
            {
                if (b[i] == 0xC2 && b[i + 1] == 0xA1)
                {
                    utf8Delim++;
                    i++;
                }
                else if (b[i] == 0xA1 && (i == 0 || b[i - 1] != 0xC2))
                    winDelim++;
            }
            Encoding enc;
            if (utf8Delim >= 25 && utf8Delim >= winDelim)
                enc = new UTF8Encoding(false, false);
            else if (winDelim >= 25)
                enc = Encoding.GetEncoding(1252);
            else if (winDelim > utf8Delim * 2)
                enc = Encoding.GetEncoding(1252);
            else
                enc = new UTF8Encoding(false, false);
            return enc.GetString(b).Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        }

        /// <summary>GPR: "… Kolon Moment büyütme katsayısı" dipnotu — bundan sonrası (temel vb.) kolon donatısı değildir.</summary>
        private static bool IsGprKolonMomentBuyutmeFootnoteLine(string rawLine, byte[] rawBytes)
        {
            if (string.IsNullOrEmpty(rawLine)) return false;
            string s = rawLine.Trim();
            if (s.IndexOf("BETONARME HESAP", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            int ik = s.IndexOf("Kolon", StringComparison.OrdinalIgnoreCase);
            int im = s.IndexOf("Moment", StringComparison.OrdinalIgnoreCase);
            if (ik < 0 || im < 0 || im < ik)
            {
                if (rawBytes == null || rawBytes.Length < 20) return false;
                if (!GprRawLineContainsAscii(rawBytes, "MOMENT")) return false;
                if (GprRawLineContainsAscii(rawBytes, "BETONARME HESAP")) return false;
                s = Encoding.GetEncoding(1252).GetString(rawBytes);
                ik = s.IndexOf("Kolon", StringComparison.OrdinalIgnoreCase);
                im = s.IndexOf("Moment", StringComparison.OrdinalIgnoreCase);
                if (ik < 0 || im < 0 || im < ik) return false;
            }
            int p = im + 6;
            while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
            if (p < s.Length && (s[p] == 'b' || s[p] == 'B')) return true;
            if (s.IndexOf("katsay", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (rawBytes != null && GprRawLineContainsAscii(rawBytes, "KATSAY")) return true;
            return false;
        }

        /// <summary>
        /// Ardışık GPR kolon tablolarında hep SB-01 kullanılıyorsa bir önceki bloğun SB-* anahtarlarını SB{n}-* olarak saklar (üzerine yazılmadan önce).
        /// </summary>
        private static void GprPromoteGenericSbKeysToIndexedBasement(
            Dictionary<string, (string ebat, string donati, string etriye)> result,
            int previousSectionIndex)
        {
            if (result == null || result.Count == 0 || previousSectionIndex < 1) return;
            string idx = previousSectionIndex.ToString(CultureInfo.InvariantCulture);
            foreach (var kv in result.ToList())
            {
                var m = Regex.Match(kv.Key ?? string.Empty, @"^SB-(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!m.Success) continue;
                string nk = "SB" + idx + "-" + m.Groups[1].Value;
                if (result.ContainsKey(nk)) continue;
                result[nk] = kv.Value;
            }
        }

        /// <summary>Kolon id (örn. SB-01) -> (ebat, donati, etriye). GPR: birden fazla bodrumda ardışık KOLON BETONARME blokları; her blok ayrı okunur (.prn ile aynı mantık).</summary>
        public static Dictionary<string, (string ebat, string donati, string etriye)> ParseKolonBetonarmeFromFile(string filePath, out string error)
        {
            error = null;
            var result = new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase);
            bool isGpr = filePath.EndsWith(".gpr", StringComparison.OrdinalIgnoreCase);
            string[] lines;
            byte[][] gprRawLines = null;
            try
            {
                if (isGpr)
                    LoadGprFileLines(filePath, out lines, out gprRawLines);
                else
                    lines = ReadGprPrnLines(filePath);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return result;
            }

            if (isGpr && gprRawLines != null)
            {
                int gprSearchFrom = 0;
                int gprSectionCount = 0;
                while (gprSearchFrom < lines.Length)
                {
                    int iKolon = -1;
                    for (int j = gprSearchFrom; j < lines.Length; j++)
                    {
                        string c = StripGprLinePrefix(lines[j] ?? string.Empty);
                        if (c.IndexOf("KOLON BETONARME HESAP", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            iKolon = j;
                            break;
                        }
                        if (j < gprRawLines.Length && GprRawLineContainsKolonHeader(gprRawLines[j]))
                        {
                            iKolon = j;
                            break;
                        }
                    }
                    if (iKolon < 0)
                    {
                        if (gprSectionCount == 0)
                            error = "KOLON BETONARME HESAP SONUCLARI bolumu bulunamadi.";
                        break;
                    }
                    gprSectionCount++;
                    if (gprSectionCount >= 2)
                        GprPromoteGenericSbKeysToIndexedBasement(result, gprSectionCount - 1);
                    int iEnd = lines.Length;
                    bool foundEnd = false;
                    for (int j = iKolon + 1; j < lines.Length; j++)
                    {
                        byte[] rb = j < gprRawLines.Length ? gprRawLines[j] : null;
                        string raw = lines[j] ?? string.Empty;
                        string c = StripGprLinePrefix(raw);
                        if (IsGprKolonMomentBuyutmeFootnoteLine(raw, rb))
                        {
                            iEnd = j;
                            foundEnd = true;
                            break;
                        }
                        // İkinci ve sonraki bodrum tabloları: yeni başlık (dipnottan önce de gelebilir)
                        if (j > iKolon + 1 &&
                            (c.IndexOf("KOLON BETONARME HESAP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             (rb != null && GprRawLineContainsKolonHeader(rb))))
                        {
                            iEnd = j;
                            foundEnd = true;
                            break;
                        }
                    }
                    if (!foundEnd)
                    {
                        for (int j = iKolon + 1; j < lines.Length; j++)
                        {
                            string c = StripGprLinePrefix(lines[j] ?? string.Empty);
                            byte[] rb = j < gprRawLines.Length ? gprRawLines[j] : null;
                            if (IsGprTemelBetonarmeSectionHeader(c, rb))
                            {
                                iEnd = j;
                                break;
                            }
                        }
                    }
                    AppendKolonBetonarmeSection(lines, iKolon + 1, isGpr, result, gprRawLines, iEnd, true);
                    int nextFrom = iEnd;
                    if (nextFrom <= iKolon)
                        nextFrom = iKolon + 1;
                    gprSearchFrom = nextFrom;
                }
                if (gprSectionCount > 0)
                    GprPromoteGenericSbKeysToIndexedBasement(result, gprSectionCount);
                return result;
            }

            int searchFrom = 0;
            int sectionCount = 0;
            while (searchFrom < lines.Length)
            {
                int headerIdx = -1;
                for (int j = searchFrom; j < lines.Length; j++)
                {
                    string c = StripGprLinePrefix(lines[j] ?? string.Empty);
                    if (c.IndexOf("KOLON BETONARME HESAP", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        headerIdx = j;
                        break;
                    }
                }
                if (headerIdx < 0)
                    break;
                sectionCount++;
                searchFrom = AppendKolonBetonarmeSection(lines, headerIdx + 1, isGpr, result, null, int.MaxValue, false);
            }

            if (sectionCount == 0)
            {
                error = "KOLON BETONARME HESAP SONUCLARI bolumu bulunamadi.";
                return result;
            }

            return result;
        }

        /// <summary>GPR satırında ASCII alt dize (kodlama bozuk olsa bile bayt düzeyinde).</summary>
        private static bool GprRawLineContainsAscii(byte[] line, string asciiNeedle)
        {
            if (line == null || string.IsNullOrEmpty(asciiNeedle)) return false;
            byte[] n = Encoding.ASCII.GetBytes(asciiNeedle);
            if (line.Length < n.Length) return false;
            for (int i = 0; i <= line.Length - n.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < n.Length && ok; j++)
                {
                    byte a = line[i + j];
                    byte b = n[j];
                    if (a >= (byte)'a' && a <= (byte)'z') a = (byte)(a - 32);
                    if (b >= (byte)'a' && b <= (byte)'z') b = (byte)(b - 32);
                    if (a != b) ok = false;
                }
                if (ok) return true;
            }
            return false;
        }

        private static bool GprRawLineContainsKolonHeader(byte[] line)
        {
            return line != null && line.Length >= 22 && GprRawLineContainsAscii(line, "KOLON BETONARME HESAP");
        }

        /// <summary>
        /// GPR sayfa çerçevesi: "38,10,84" / "54,10,94" / "3,10,67" — KOLON tablosu bu satırdan önce biter;
        /// sonraki sayfada tekrar KOLON BETONARME ile devam eder (a_klc, ia_c_blk_td, SL_09 ortak desen).
        /// </summary>
        private static bool IsGprKolonTablePageBreakLine(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) return false;
            string s = rawLine.Trim();
            var m = Regex.Match(s, @"^(\d{1,3}),(10),(\d{1,4})\s*$", RegexOptions.CultureInvariant);
            if (!m.Success) return false;
            if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int a))
                return false;
            if (!int.TryParse(m.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int c))
                return false;
            if (a >= 130) return false;
            if (a < 1 || a > 250) return false;
            if (c < 8 || c > 500) return false;
            return true;
        }

        /// <summary>KOLON BETONARME tablosu biter (TEMEL BETONARME = bağ kirişi vb.; kolon donatısı buradan okunmaz).</summary>
        private static bool IsGprTemelBetonarmeSectionHeader(string content, byte[] rawLine)
        {
            if (!string.IsNullOrEmpty(content) &&
                content.IndexOf("TEMEL BETONARME", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return rawLine != null && GprRawLineContainsAscii(rawLine, "TEMEL BETONARME");
        }

        private static string GetGprDonatiCellForRow(string content, bool isGpr, byte[] rawLine)
        {
            if (!isGpr || rawLine == null || rawLine.Length == 0)
                return GetGprDonatiCell(content, isGpr);
            string fromBytes = ExtractGprDonatiCellFromRawLine(rawLine);
            if (!string.IsNullOrWhiteSpace(fromBytes) && LooksLikeGprDonatiOrEtriyeText(fromBytes))
                return fromBytes.Trim();
            string fallback = GetGprDonatiCell(content, isGpr);
            if (!string.IsNullOrWhiteSpace(fallback) && LooksLikeGprDonatiOrEtriyeText(fallback))
                return fallback.Trim();
            return string.IsNullOrWhiteSpace(fromBytes) ? fallback : fromBytes.Trim();
        }

        /// <summary>Panel / POLIGON KOLON veya dosya sonuna kadar. GPR penceresi: [startIdx, lineEndExclusive).</summary>
        private static int AppendKolonBetonarmeSection(string[] lines, int startIdx, bool isGpr, Dictionary<string, (string ebat, string donati, string etriye)> result, byte[][] gprRawLines = null, int lineEndExclusive = int.MaxValue, bool gprKolonWindowOnly = false)
        {
            // Çoklu bodrum: SB2-01 önce yakalanmalı; aksi halde S[BZAC]\d{1,4} yalnızca SB2 (kolon 2 sanılır).
            // S2B-01: STA4CAD çoklu bodrum; S2-01 normal kat — S\d+B-\d+ önce olmalı
            var colIdRegex = new Regex(@"\b(S\d+B-\d+|S[BZAC]\d+-\d+|S\d+-\d+|S[BZAC]-\d+|S[BZAC]\d{1,4})\b", RegexOptions.IgnoreCase);
            var bxRegex = new Regex(@"Bx[=\s]*(\d+)", RegexOptions.IgnoreCase);
            var byRegex = new Regex(@"By[=\s]*(\d+)", RegexOptions.IgnoreCase);
            var polygonRegex = new Regex(@"Polygon", RegexOptions.IgnoreCase);

            int end = lineEndExclusive >= lines.Length ? lines.Length : lineEndExclusive;
            if (end < startIdx) end = startIdx;

            string currentId = null;
            string currentBx = null, currentBy = null;
            string currentDonati = null, currentEtriye = null;
            bool inBlock = false;

            void FlushCurrent()
            {
                if (currentId != null && (currentDonati != null || currentEtriye != null))
                {
                    string ebat = GetEbatString(currentBx, currentBy, currentBx != null && currentBx.Equals("Polygon", StringComparison.OrdinalIgnoreCase));
                    result[currentId] = (NormalizeDiameterSymbol(ebat ?? ""), NormalizeDiameterSymbol(currentDonati ?? ""), NormalizeDiameterSymbol(currentEtriye ?? ""));
                }
            }

            for (int i = startIdx; i < end; i++)
            {
                string raw = lines[i] ?? string.Empty;
                string content = StripGprLinePrefix(raw).Trim();
                if (content.Length == 0) continue;
                byte[] rawForHeader = (gprRawLines != null && i < gprRawLines.Length) ? gprRawLines[i] : null;
                if (isGpr && IsGprKolonTablePageBreakLine(raw))
                {
                    FlushCurrent();
                    currentId = null;
                    currentBx = null;
                    currentBy = null;
                    currentDonati = null;
                    currentEtriye = null;
                    inBlock = false;
                    continue;
                }
                if (IsGprTemelBetonarmeSectionHeader(content, rawForHeader))
                {
                    FlushCurrent();
                    if (gprKolonWindowOnly) break;
                    return i + 1;
                }
                if (content.IndexOf("KOLON BETONARME HESAP", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    FlushCurrent();
                    currentId = null;
                    currentBx = null;
                    currentBy = null;
                    currentDonati = null;
                    currentEtriye = null;
                    inBlock = false;
                    continue;
                }
                if (GprRawLineContainsKolonHeader(rawForHeader) && rawForHeader != null)
                {
                    FlushCurrent();
                    currentId = null;
                    currentBx = null;
                    currentBy = null;
                    currentDonati = null;
                    currentEtriye = null;
                    inBlock = false;
                    continue;
                }
                if (content.StartsWith("Panel ", StringComparison.OrdinalIgnoreCase) ||
                    content.IndexOf("POLIGON KOLON", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    FlushCurrent();
                    if (gprKolonWindowOnly)
                    {
                        currentId = null;
                        inBlock = false;
                        continue;
                    }
                    return i + 1;
                }

                var idMatch = colIdRegex.Match(content);
                if (idMatch.Success && idMatch.Index < 20)
                {
                    FlushCurrent();
                    currentId = idMatch.Groups[1].Value.Trim().ToUpperInvariant();
                    currentBx = null; currentBy = null; currentDonati = null; currentEtriye = null;
                    inBlock = true;

                    var bx = bxRegex.Match(content);
                    if (bx.Success) currentBx = bx.Groups[1].Value;
                    var by = byRegex.Match(content);
                    if (by.Success) currentBy = by.Groups[1].Value;
                    if (polygonRegex.IsMatch(content)) { currentBx = "Polygon"; currentBy = null; }

                    byte[] rawBytes = (gprRawLines != null && i < gprRawLines.Length) ? gprRawLines[i] : null;
                    string donatiCell = GetGprDonatiCellForRow(content, isGpr, rawBytes);
                    SplitDonatiEtriye(donatiCell, out string don, out string et);
                    if (!string.IsNullOrEmpty(don) && !GprColumnIdInDonatiCell.IsMatch(don.Trim()))
                        currentDonati = don;
                    if (!string.IsNullOrEmpty(et)) currentEtriye = et;
                }
                else if (inBlock && currentId != null)
                {
                    var by = byRegex.Match(content);
                    if (by.Success) currentBy = by.Groups[1].Value;
                    if (polygonRegex.IsMatch(content)) { currentBx = "Polygon"; currentBy = null; }
                    byte[] rawBytes2 = (gprRawLines != null && i < gprRawLines.Length) ? gprRawLines[i] : null;
                    string donatiCell = GetGprDonatiCellForRow(content, isGpr, rawBytes2);
                    SplitDonatiEtriye(donatiCell, out string don, out string et);
                    if (!string.IsNullOrEmpty(et)) currentEtriye = et;
                    if (!string.IsNullOrEmpty(don) && !GprColumnIdInDonatiCell.IsMatch(don.Trim()) && currentDonati == null)
                        currentDonati = don;
                }
            }
            FlushCurrent();
            return lines.Length;
        }

        private static string StripGprLinePrefix(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            var m = Regex.Match(line.Trim(), @"^\d+,\d+,\d+,\d+,-?\d+,\d+\s*");
            return m.Success ? line.Trim().Substring(m.Length) : line.Trim();
        }

        private static string GetLastTableColumn(string line)
        {
            char[] sep = { '\u00a7', '\u00c0', '\u00c1', '\u00c2', '\u00c3', '\u00c4', '\u00c5', '\u00c6', '\u00c7', '\u00d9', '\u00da', '\u00db', '\u00dc', '\u00dd', '\u00de', '\u00df', '\u00b5', '\u00b6', '\u00b7', '\u00af', '\u00b0', '\u00b1', '\u00b2', '\u00b3', '\u00b4', '¡', '¦', '|', '\t' };
            string s = line.Trim();
            int maxLast = -1;
            foreach (char c in sep)
            {
                int last = s.LastIndexOf(c);
                if (last > maxLast) maxLast = last;
            }
            if (maxLast >= 0 && maxLast < s.Length - 1)
                s = s.Substring(maxLast + 1).Trim();
            return s.Trim();
        }

        private static void SplitDonatiEtriye(string cell, out string donati, out string etriye)
        {
            donati = null; etriye = null;
            if (string.IsNullOrWhiteSpace(cell)) return;
            int idx = cell.IndexOf("(etriye)", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                etriye = cell.Substring(0, idx).Trim();
                if (etriye.StartsWith("ø", StringComparison.Ordinal) || etriye.StartsWith("Ã¸", StringComparison.Ordinal))
                    etriye = "ø" + etriye.TrimStart('ø', 'Ã', '¸');
                string rest = cell.Substring(idx + 8).Trim();
                if (rest.Length > 0) donati = rest;
            }
            else
                donati = cell;
        }

        private static string GetEbatString(string bx, string by, bool isPolygon)
        {
            if (isPolygon || (bx != null && bx.Equals("Polygon", StringComparison.OrdinalIgnoreCase)))
                return "Polygon";
            if (string.IsNullOrEmpty(bx) && string.IsNullOrEmpty(by)) return "";
            int bxi = 0, byi = 0;
            int.TryParse(bx, NumberStyles.Integer, CultureInfo.InvariantCulture, out bxi);
            int.TryParse(by, NumberStyles.Integer, CultureInfo.InvariantCulture, out byi);
            if (bxi == byi && bxi > 0) return "R= " + bxi.ToString(CultureInfo.InvariantCulture);
            if (bxi > 0 && byi > 0) return bxi.ToString(CultureInfo.InvariantCulture) + "/" + byi.ToString(CultureInfo.InvariantCulture);
            if (bxi > 0) return bxi.ToString(CultureInfo.InvariantCulture);
            if (byi > 0) return byi.ToString(CultureInfo.InvariantCulture);
            return bx ?? by ?? "";
        }

        /// <summary>B→SB + SB01 veya B-→SB-21. 1-→S1-02. Rakamsal katlarda kolondan önce daima tire.</summary>
        private struct GprFloorKeyFmt
        {
            public string StoryPrefix;
            public bool HyphenBeforeColNo;
        }

        private static GprFloorKeyFmt[] BuildGprFloorKeyFormats(IReadOnlyList<FloorInfo> floors)
        {
            if (floors == null || floors.Count == 0) return Array.Empty<GprFloorKeyFmt>();
            var arr = new GprFloorKeyFmt[floors.Count];
            for (int i = 0; i < floors.Count; i++)
                arr[i] = GetGprFloorKeyFormat(floors[i].ShortName, floors[i].Name);
            return arr;
        }

        private static GprFloorKeyFmt GetGprFloorKeyFormat(string shortName, string floorName)
        {
            string sn = (shortName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(sn))
                return new GprFloorKeyFmt { StoryPrefix = GprPrefixFromFloorNameFallback(floorName), HyphenBeforeColNo = true };
            bool endsWithDash = sn.EndsWith("-", StringComparison.Ordinal);
            string token = endsWithDash ? sn.Substring(0, sn.Length - 1).Trim() : sn;
            if (token.Length == 0)
                return new GprFloorKeyFmt { StoryPrefix = GprPrefixFromFloorNameFallback(floorName), HyphenBeforeColNo = true };
            if (token.Length == 1 && char.IsLetter(token[0]))
            {
                char c = char.ToUpperInvariant(token[0]);
                string p = c == 'B' ? "SB" : c == 'Z' ? "SZ" : c == 'A' ? "SA" : c == 'C' ? "SC" : "S1";
                return new GprFloorKeyFmt { StoryPrefix = p, HyphenBeforeColNo = endsWithDash };
            }
            var mBodIdx = Regex.Match(token, @"^B(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (mBodIdx.Success && int.TryParse(mBodIdx.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bLev) && bLev > 0)
                return new GprFloorKeyFmt { StoryPrefix = "S" + bLev.ToString(CultureInfo.InvariantCulture) + "B", HyphenBeforeColNo = true };
            mBodIdx = Regex.Match(token, @"^(\d+)B$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (mBodIdx.Success && int.TryParse(mBodIdx.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bLev2) && bLev2 > 0)
                return new GprFloorKeyFmt { StoryPrefix = "S" + bLev2.ToString(CultureInfo.InvariantCulture) + "B", HyphenBeforeColNo = true };
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0)
                return new GprFloorKeyFmt { StoryPrefix = "S" + n.ToString(CultureInfo.InvariantCulture), HyphenBeforeColNo = true };
            return new GprFloorKeyFmt { StoryPrefix = GprPrefixFromFloorNameFallback(floorName), HyphenBeforeColNo = true };
        }

        private static string GprPrefixFromFloorNameFallback(string floorName)
        {
            string nu = (floorName ?? string.Empty).Trim().ToUpperInvariant();
            var mBod = Regex.Match(nu, @"(\d+)\s*\.\s*BODRUM", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (mBod.Success && int.TryParse(mBod.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bodNo) && bodNo > 0)
                return "S" + bodNo.ToString(CultureInfo.InvariantCulture) + "B";
            mBod = Regex.Match(nu, @"(\d+)\s*\.\s*NORMAL\s*BODRUM", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (mBod.Success && int.TryParse(mBod.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bn2) && bn2 > 0)
                return "S" + bn2.ToString(CultureInfo.InvariantCulture) + "B";
            mBod = Regex.Match(nu, @"(\d+)\s*\.\s*N\s*BODRUM", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (mBod.Success && int.TryParse(mBod.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bn3) && bn3 > 0)
                return "S" + bn3.ToString(CultureInfo.InvariantCulture) + "B";
            mBod = Regex.Match(nu, @"BODRUM\D*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (mBod.Success && int.TryParse(mBod.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bn4) && bn4 > 0)
                return "S" + bn4.ToString(CultureInfo.InvariantCulture) + "B";
            if (nu.IndexOf("BODRUM", StringComparison.Ordinal) >= 0) return "SB";
            if (nu.IndexOf("ZEMIN", StringComparison.Ordinal) >= 0 || nu.IndexOf("ZEMİN", StringComparison.Ordinal) >= 0) return "SZ";
            if (nu.Equals("ASMA", StringComparison.OrdinalIgnoreCase) || nu.StartsWith("ASMA ", StringComparison.OrdinalIgnoreCase)) return "SA";
            if (nu.IndexOf("CATI", StringComparison.Ordinal) >= 0 || nu.IndexOf("ÇATI", StringComparison.Ordinal) >= 0) return "SC";
            var m = Regex.Match(nu, @"^(\d+)\s*\.\s*N");
            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int storyNum) && storyNum > 0)
                return "S" + storyNum.ToString(CultureInfo.InvariantCulture);
            return "S1";
        }

        /// <summary>GPR kat önek dikey sırası: SB &lt; SZ &lt; SA &lt; S1 &lt; S2 &lt; … &lt; SC.</summary>
        private static int GprStoryRank(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return 50;
            // Tüm bodrum önekleri (SB, SB1…SB4) aynı bant: aksi halde GPR'de yalnızca SB-01 varken SB2 katı minGprRank ile yanlış atlanırdı.
            if (prefix.Equals("SB", StringComparison.OrdinalIgnoreCase)) return 0;
            if (prefix.Length > 2 && prefix.StartsWith("SB", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(prefix.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sbN) && sbN > 0)
                return 0;
            if (Regex.IsMatch(prefix, @"^S\d+B$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return 0;
            if (prefix.Equals("SZ", StringComparison.OrdinalIgnoreCase)) return 1;
            if (prefix.Equals("SA", StringComparison.OrdinalIgnoreCase)) return 2;
            if (prefix.Equals("SC", StringComparison.OrdinalIgnoreCase)) return 200;
            if (prefix.Length >= 2 && prefix[0] == 'S' && char.IsDigit(prefix[1]))
            {
                if (int.TryParse(prefix.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0)
                    return 2 + n;
            }
            return 50;
        }

        /// <summary>Bu kolon no için GPR'de geçen en alt kat kodu. SB01 ile SB-01 aynı hiyerarşi.</summary>
        private static int GetMinGprStoryRankForColumn(Dictionary<string, (string ebat, string donati, string etriye)> data, int colNo)
        {
            if (data == null || data.Count == 0) return int.MaxValue;
            int min = int.MaxValue;
            foreach (var k in data.Keys)
            {
                if (!TryParseGprStoryKey(k, out string sp, out int num) || num != colNo) continue;
                int r = GprStoryRank(sp);
                if (r < min) min = r;
            }
            return min;
        }

        public static bool Draw(St4Model model, Dictionary<string, (string ebat, string donati, string etriye)> columnData,
            Point3d insertPoint, Database db, Editor ed, Transaction tr, BlockTableRecord btr,
            Dictionary<int, (double? temelCm, double? hatilCm)> columnFoundationHeights = null,
            List<Dictionary<int, (int columnType, double W, double H)>> columnDimsByFloor = null,
            List<Dictionary<int, (double altKotCm, double yukseklikCm, double? kirisUstAltFarkCm)>> columnTableExtraByFloor = null,
            HashSet<(int floorIndex, int columnNo)> columnActiveCells = null,
            bool echoCompletionMessage = true)
        {
            if (model == null || model.Floors == null || model.Floors.Count == 0)
            {
                ed.WriteMessage("\nKOLONDATA: ST4'te kat bulunamadi.");
                return false;
            }
            int numFloors = model.Floors.Count;
            var floorNames = model.Floors.Select(f => f.Name ?? "").ToList();

            var floorKeyFmt = BuildGprFloorKeyFormats(model.Floors);

            var columnIds = new HashSet<string>(columnData?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var rowColumnNumbers = new List<int>();
            foreach (var k in columnData?.Keys ?? Enumerable.Empty<string>())
            {
                if (TryParseGprStoryKey(k, out _, out int colNo) && !rowColumnNumbers.Contains(colNo))
                    rowColumnNumbers.Add(colNo);
            }
            rowColumnNumbers.Sort();
            if (rowColumnNumbers.Count == 0) rowColumnNumbers.Add(1);

            double totalWidth = ColWidthKatNo + ColWidthSubHeader + numFloors * ColWidthFloor;
            double totalHeight = RowHeightHeader + rowColumnNumbers.Count * RowHeightDataBlock;

            EnsureLayers(tr, db);
            EnsureTextStyle(tr, db, TextStyleYazi);

            double x0 = insertPoint.X;
            double y0 = insertPoint.Y;
            double yTop = y0 + totalHeight;

            DrawGrid(tr, btr, x0, yTop, numFloors, rowColumnNumbers.Count);
            DrawHeaderRow(tr, btr, db, x0, yTop, floorNames);
            DrawDataRows(tr, btr, db, x0, yTop - RowHeightHeader, numFloors, floorNames, rowColumnNumbers, columnData ?? new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase), columnFoundationHeights, columnDimsByFloor, columnTableExtraByFloor, columnActiveCells, floorKeyFmt);
            int gprKeys = columnData?.Count ?? 0;
            if (echoCompletionMessage)
                ed.WriteMessage("\nKOLONDATA tamam: {0} kat sutunu, {1} kolon satiri, veri dosyasinda {2} kolon kodu (SB-01, S1-02, SC-39, ...).",
                    numFloors, rowColumnNumbers.Count, gprKeys);
            return true;
        }

        private static void EnsureLayers(Transaction tr, Database db)
        {
            LayerService.EnsureLayer(tr, db, LayerGrid, 7);
            LayerService.EnsureLayer(tr, db, LayerPenc4, 7);
            LayerService.EnsureLayer(tr, db, LayerPenc2, 7);
            LayerService.EnsureLayer(tr, db, LayerHeader, 7);
            LayerService.EnsureLayer(tr, db, LayerYazi, 140);
            LayerService.EnsureLayer(tr, db, LayerDonatiYazisi, 3, LineWeight.LineWeight020);
            LayerService.EnsureLayer(tr, db, LayerKolonIsmi, 91);
            LayerService.EnsureLayer(tr, db, LayerSubasman, 197);
            LayerService.EnsureLayer(tr, db, LayerCizgi, 1, LineWeight.LineWeight020);
            LayerService.EnsureLayer(tr, db, LayerKot, 7);
            LayerService.EnsureLayer(tr, db, LayerKirisIsmi, 40);
        }

        private static void EnsureTextStyle(Transaction tr, Database db, string styleName)
        {
            var ts = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (ts.Has(styleName)) return;
            ts.UpgradeOpen();
            var str = new TextStyleTableRecord { Name = styleName };
            try { str.Font = new FontDescriptor(FontName, false, false, 0, 0); }
            catch { try { str.Font = new FontDescriptor("Bahnschrift", false, false, 0, 0); } catch { } }
            ts.Add(str);
            tr.AddNewlyCreatedDBObject(str, true);
        }

        /// <summary>Sol ust (0,0), Y asagi negatif. User (ux,uy) -> cizim (x0+ux, yTop+uy).</summary>
        private static Point3d U(double x0, double yTop, double ux, double uy)
        {
            return new Point3d(x0 + ux, yTop + uy, 0);
        }

        private static void DrawGrid(Transaction tr, BlockTableRecord btr, double x0, double yTop, int numFloors, int numRows)
        {
            const double wLeft = 150; // 50 Kat no + 100 ebat/donati/etriye
            double totalWidth = wLeft + numFloors * ColWidthFloor; // 150 + numFloors*150
            double bottomY = -RowHeightHeader - numRows * RowHeightDataBlock; // -50 - numRows*75

            // --- PENC4: Ana tablo çizgileri ---
            // Kat no alanı: LINE 0,0/150,0 - 0,0/0,-50 - 150,0/150,-50 - 0,-50/150,-50
            AppendLine(tr, btr, U(x0, yTop, 0, 0), U(x0, yTop, totalWidth, 0), LayerPenc4);
            AppendLine(tr, btr, U(x0, yTop, 0, 0), U(x0, yTop, 0, -50), LayerPenc4);
            AppendLine(tr, btr, U(x0, yTop, 150, 0), U(x0, yTop, 150, -50), LayerPenc4);
            AppendLine(tr, btr, U(x0, yTop, 0, -50), U(x0, yTop, totalWidth, -50), LayerPenc4);

            // İlk kat alanı: yatay çizgiler tek parça, dikeyler ayrı
            for (int f = 0; f < numFloors; f++)
            {
                double xL = 150 + f * 150;
                double xR = 150 + (f + 1) * 150;
                AppendLine(tr, btr, U(x0, yTop, xR, 0), U(x0, yTop, xR, -50), LayerPenc4);
            }

            // İlk kolon alanı (her kolon için 75 aşağı): LINE 0,-50/0,-125 - 50,-50/50,-125 - 0,-125/150,-125 - 150,-50/150,-125
            for (int r = 0; r < numRows; r++)
            {
                double yT = -50 - r * 75;
                double yB = -125 - r * 75;
                AppendLine(tr, btr, U(x0, yTop, 0, yT), U(x0, yTop, 0, yB), LayerPenc4);
                AppendLine(tr, btr, U(x0, yTop, 50, yT), U(x0, yTop, 50, yB), LayerPenc4);
                AppendLine(tr, btr, U(x0, yTop, 150, yT), U(x0, yTop, 150, yB), LayerPenc4);
                AppendLine(tr, btr, U(x0, yTop, 0, yB), U(x0, yTop, totalWidth, yB), LayerPenc4);
            }

            // İlk kolon ile kat kesişimi: LINE 150,-125/300,-125 - 300,-50/300,-125 (kata göre 150 sağa, kolon sayısına 75 aşağı)
            for (int r = 0; r < numRows; r++)
            {
                double yT = -50 - r * 75;
                double yB = -125 - r * 75;
                for (int f = 0; f < numFloors; f++)
                {
                    double xL = 150 + f * 150;
                    double xR = 150 + (f + 1) * 150;
                    AppendLine(tr, btr, U(x0, yTop, xR, yT), U(x0, yTop, xR, yB), LayerPenc4);
                }
            }

            // Tablo alt ve sağ dış çizgi
            AppendLine(tr, btr, U(x0, yTop, 0, bottomY), U(x0, yTop, totalWidth, bottomY), LayerPenc4);
            AppendLine(tr, btr, U(x0, yTop, totalWidth, 0), U(x0, yTop, totalWidth, bottomY), LayerPenc4);

            // --- PENC2: Kolon alanı içi ince çizgiler (ebat-donati, donati-etriye) ---
            // İlk kolon alanı: LINE 50,-75/150,-75 - 50,-100/150,-100 (kolon sayısına 75 aşağı)
            for (int r = 0; r < numRows; r++)
            {
                double y75 = -75 - r * 75;
                double y100 = -100 - r * 75;
                AppendLine(tr, btr, U(x0, yTop, 50, y75), U(x0, yTop, totalWidth, y75), LayerPenc2);
                AppendLine(tr, btr, U(x0, yTop, 50, y100), U(x0, yTop, totalWidth, y100), LayerPenc2);
            }
        }

        private static void AppendLine(Transaction tr, BlockTableRecord btr, Point3d from, Point3d to, string layer, short? colorIndex = null)
        {
            var line = new Line(from, to) { Layer = layer };
            if (colorIndex.HasValue)
                line.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex.Value);
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        private static void DrawHeaderRow(Transaction tr, BlockTableRecord btr, Database db, double x0, double yTop, List<string> floorNames)
        {
            DrawTextLeftBottom(tr, btr, db, x0 + 15 + Penc4TextShiftRightCm, yTop - 20, "Kat no", TextHeightMain, LayerHeader, TextStyleYazi, null);
            double cellLeft = x0 + ColWidthKatNo + ColWidthSubHeader;
            for (int i = 0; i < floorNames.Count; i++)
            {
                string name = string.IsNullOrEmpty(floorNames[i]) ? (i + 1).ToString(CultureInfo.InvariantCulture) + ". NORMAL" : floorNames[i];
                DrawTextLeftBottom(tr, btr, db, cellLeft + 40 + Penc4TextShiftRightCm, yTop - 25, name, TextHeightMain, LayerHeader, TextStyleYazi, null);
                cellLeft += ColWidthFloor;
            }
        }

        private static double EstimateTextWidthCm(string text, double heightCm)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return text.Length * heightCm * TextWidthFactor * 0.65;
        }

        private static void DrawDataRows(Transaction tr, BlockTableRecord btr, Database db, double x0, double yRowTop, int numFloors, List<string> floorNames, List<int> rowColumnNumbers, Dictionary<string, (string ebat, string donati, string etriye)> columnData, Dictionary<int, (double? temelCm, double? hatilCm)> columnFoundationHeights, List<Dictionary<int, (int columnType, double W, double H)>> columnDimsByFloor, List<Dictionary<int, (double altKotCm, double yukseklikCm, double? kirisUstAltFarkCm)>> columnTableExtraByFloor, HashSet<(int floorIndex, int columnNo)> columnActiveCells, GprFloorKeyFmt[] floorKeyFmt)
        {
            const double labelX = 5;
            double ebadDonatiX = x0 + ColWidthKatNo + 10;
            double temelTextX = ebadDonatiX + 40;
            double hatilTextX = ebadDonatiX + 70;
            const double cizgiLengthCm = 15.0;
            const double cizgiYOffsetCm = 7.5;
            const double cellBoyutOffset = 10;
            const double cellKotOffset = 50;
            const double cellYukseklikOffset = 85;
            const double cellKirisOffset = 120;

            for (int r = 0; r < rowColumnNumbers.Count; r++)
            {
                int colNo = rowColumnNumbers[r];
                double rowTop = yRowTop - r * RowHeightDataBlock;

                DrawTextLeftBottom(tr, btr, db, ebadDonatiX, rowTop - 20, "ebad", TextHeightSmall, LayerGrid, TextStyleYazi, AciLabelGray);
                DrawTextLeftBottom(tr, btr, db, ebadDonatiX, rowTop - 45, "donati", TextHeightSmall, LayerGrid, TextStyleYazi, AciLabelGray);
                DrawTextLeftBottom(tr, btr, db, ebadDonatiX, rowTop - 70, "etriye", TextHeightSmall, LayerGrid, TextStyleYazi, AciLabelGray);

                if (columnFoundationHeights != null && columnFoundationHeights.TryGetValue(colNo, out var heights))
                {
                    if (heights.temelCm.HasValue)
                        DrawTextLeftBottom(tr, btr, db, temelTextX, rowTop - 20, ((int)Math.Round(heights.temelCm.Value)).ToString(CultureInfo.InvariantCulture), TextHeightMain, LayerYazi, TextStyleYazi, null);
                    if (heights.temelCm.HasValue && heights.hatilCm.HasValue)
                    {
                        double hatilFark = heights.hatilCm.Value - heights.temelCm.Value;
                        DrawTextLeftBottom(tr, btr, db, hatilTextX, rowTop - 20, ((int)Math.Round(hatilFark)).ToString(CultureInfo.InvariantCulture), TextHeightMain, LayerSubasman, TextStyleYazi, null);
                    }
                }

                int minGprRank = GetMinGprStoryRankForColumn(columnData, colNo);

                for (int f = 0; f < numFloors; f++)
                {
                    if (columnActiveCells != null && !columnActiveCells.Contains((f, colNo)))
                        continue;
                    var fk = f < floorKeyFmt.Length ? floorKeyFmt[f] : new GprFloorKeyFmt { StoryPrefix = "S1", HyphenBeforeColNo = true };
                    string fp = fk.StoryPrefix;
                    if (minGprRank < int.MaxValue && GprStoryRank(fp) < minGprRank)
                        continue;

                    double cellLeft = x0 + ColWidthKatNo + ColWidthSubHeader + f * ColWidthFloor + 5;
                    var dimsForFloor = columnDimsByFloor != null && f < columnDimsByFloor.Count ? columnDimsByFloor[f] : null;
                    var extraForFloor = columnTableExtraByFloor != null && f < columnTableExtraByFloor.Count ? columnTableExtraByFloor[f] : null;

                    double boyutX = cellLeft + cellBoyutOffset;
                    double kotX = cellLeft + cellKotOffset;
                    double yukX = cellLeft + cellYukseklikOffset;
                    double kirisX = cellLeft + cellKirisOffset;

                    if (dimsForFloor != null && dimsForFloor.TryGetValue(colNo, out var dims))
                    {
                        if (dims.columnType == 3)
                        {
                            if (TryGetDonatiForFloorColumn(columnData, fp, fk.HyphenBeforeColNo, colNo, out string donRawP))
                            {
                                string donDisp = FormatDonatiDisplay(donRawP);
                                if (!string.IsNullOrEmpty(donDisp))
                                    DrawDonatiTextUnderBoyut(tr, btr, db, boyutX, rowTop - 20 - DonatiBoyunaBoyutAltinaCm, donDisp);
                            }
                            if (TryGetEtriyeForFloorColumn(columnData, fp, fk.HyphenBeforeColNo, colNo, out string etRawP))
                            {
                                string etDisp = FormatEtriyeForTableDisplay(etRawP);
                                if (!string.IsNullOrEmpty(etDisp))
                                    DrawDonatiTextUnderBoyut(tr, btr, db, boyutX, rowTop - 20 - EtriyeBoyutAltinaCm, etDisp, 30);
                            }
                        }
                        else
                        {
                            string dimStr;
                            bool drawCizgi = false;
                            if (dims.columnType == 2 && dims.W > 0)
                            {
                                dimStr = "R= " + ((int)Math.Round(dims.W)).ToString(CultureInfo.InvariantCulture);
                                drawCizgi = dims.W < 30;
                            }
                            else if (dims.W > 0 && dims.H > 0)
                            {
                                int wi = (int)Math.Round(dims.W);
                                int hi = (int)Math.Round(dims.H);
                                dimStr = wi.ToString(CultureInfo.InvariantCulture) + "/" + hi.ToString(CultureInfo.InvariantCulture);
                                bool kenar25 = wi == 25 || hi == 25;
                                if (kenar25)
                                {
                                    int diger = wi == 25 ? hi : wi;
                                    drawCizgi = diger <= 149;
                                }
                                else
                                    drawCizgi = wi < 30 || hi < 30;
                            }
                            else
                                dimStr = null;
                            if (dimStr != null)
                            {
                                DrawTextLeftBottom(tr, btr, db, boyutX, rowTop - 20, dimStr, TextHeightMain, LayerKolonIsmi, TextStyleYazi, null);
                                if (drawCizgi)
                                {
                                    double textW = EstimateTextWidthCm(dimStr, TextHeightMain);
                                    double lineX0 = boyutX + textW;
                                    double lineY = rowTop - 20 + cizgiYOffsetCm;
                                    AppendLine(tr, btr, new Point3d(lineX0, lineY, 0), new Point3d(lineX0 + cizgiLengthCm, lineY, 0), LayerCizgi);
                                }
                                if (TryGetDonatiForFloorColumn(columnData, fp, fk.HyphenBeforeColNo, colNo, out string donRaw))
                                {
                                    string donDisp = FormatDonatiDisplay(donRaw);
                                    if (!string.IsNullOrEmpty(donDisp))
                                        DrawDonatiTextUnderBoyut(tr, btr, db, boyutX, rowTop - 20 - DonatiBoyunaBoyutAltinaCm, donDisp);
                                }
                                if (TryGetEtriyeForFloorColumn(columnData, fp, fk.HyphenBeforeColNo, colNo, out string etRaw))
                                {
                                    string etDisp = FormatEtriyeForTableDisplay(etRaw);
                                    if (!string.IsNullOrEmpty(etDisp))
                                        DrawDonatiTextUnderBoyut(tr, btr, db, boyutX, rowTop - 20 - EtriyeBoyutAltinaCm, etDisp, 30);
                                }
                            }
                        }
                    }

                    if (extraForFloor != null && extraForFloor.TryGetValue(colNo, out var extra))
                    {
                        DrawTextLeftBottom(tr, btr, db, kotX, rowTop - 20, ((int)Math.Round(extra.altKotCm)).ToString(CultureInfo.InvariantCulture), TextHeightMain, LayerKot, TextStyleYazi, null);
                        double yukGosterCm = extra.yukseklikCm;
                        if (f == 0 && columnFoundationHeights != null && columnFoundationHeights.TryGetValue(colNo, out var fh))
                        {
                            if (fh.temelCm.HasValue && fh.hatilCm.HasValue)
                                yukGosterCm -= fh.hatilCm.Value - fh.temelCm.Value;
                            else if (fh.hatilCm.HasValue)
                                yukGosterCm -= fh.hatilCm.Value;
                        }
                        if (yukGosterCm < 0) yukGosterCm = 0;
                        DrawTextLeftBottom(tr, btr, db, yukX, rowTop - 20, ((int)Math.Round(yukGosterCm)).ToString(CultureInfo.InvariantCulture), TextHeightMain, LayerYazi, TextStyleYazi, null);
                        if (extra.kirisUstAltFarkCm.HasValue)
                            DrawTextLeftBottom(tr, btr, db, kirisX, rowTop - 20, ((int)Math.Round(extra.kirisUstAltFarkCm.Value)).ToString(CultureInfo.InvariantCulture), TextHeightMain, LayerKirisIsmi, TextStyleYazi, null);
                    }
                }

                DrawTextLeftBottom(tr, btr, db, x0 + labelX + Penc4TextShiftRightCm, rowTop - 45, "S" + (r + 1).ToString(CultureInfo.InvariantCulture), TextHeightMain, LayerHeader, TextStyleYazi, null);
            }
        }

        private static void DrawDonatiTextUnderBoyut(Transaction tr, BlockTableRecord btr, Database db, double x, double y, string text, short? colorIndex = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            var ts = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            ObjectId styleId = ts.Has(TextStyleYazi) ? ts[TextStyleYazi] : db.Textstyle;
            var txt = new DBText
            {
                Position = new Point3d(x - 5, y, 0),
                Height = TextHeightMain,
                TextString = text,
                Layer = LayerDonatiYazisi,
                TextStyleId = styleId,
                HorizontalMode = TextHorizontalMode.TextLeft,
                WidthFactor = TextWidthFactor,
                LineWeight = LineWeight.LineWeight020
            };
            if (colorIndex.HasValue)
                txt.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex.Value);
            btr.AppendEntity(txt);
            tr.AddNewlyCreatedDBObject(txt, true);
        }

        /// <summary>Referans DXF ile uyumlu: sol-alt (baseline) yerleşim, genişlik 0.7, isteğe bağlı ACI renk.</summary>
        private static void DrawTextLeftBottom(Transaction tr, BlockTableRecord btr, Database db, double x, double y, string text, double height, string layer, string styleName, short? colorIndex)
        {
            if (string.IsNullOrEmpty(text)) return;
            var ts = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            ObjectId styleId = ts.Has(styleName) ? ts[styleName] : db.Textstyle;
            var txt = new DBText
            {
                Position = new Point3d(x - 5, y, 0),
                Height = height,
                TextString = text,
                Layer = layer,
                TextStyleId = styleId,
                HorizontalMode = TextHorizontalMode.TextLeft,
                WidthFactor = TextWidthFactor
            };
            if (colorIndex.HasValue)
                txt.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex.Value);
            btr.AppendEntity(txt);
            tr.AddNewlyCreatedDBObject(txt, true);
        }
    }
}
