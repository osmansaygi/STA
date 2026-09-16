using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// Keşif özeti birim fiyatları: ÇŞİDB Yüksek Fen Kurulu Eylül 2026 İnşaat Birim Fiyat Listesi
    /// (1 Eylül 2026'den geçerli). Tutar ve yüzde satırları miktar üzerinden yeniden hesaplanır.
    /// </summary>
    internal static class KesifOzetiPricer
    {
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

        private static readonly Regex PozNo = new Regex(@"^\s*\d+\.\d+\.\d+", RegexOptions.Compiled);
        private static readonly Regex QtyUnit = new Regex(@"([\d\.]+)\s*(m³|m2|m²|tn|ton)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Pct = new Regex(@"%\s*(\d+)", RegexOptions.Compiled);
        private static readonly Regex PctTimes = new Regex(@"%\s*(\d+)\s*[×xX\u00D7]\s*(\d+)", RegexOptions.Compiled);

        public static List<string> CollectPozNumbers(KsfDocument ksf)
        {
            var found = new List<string>();
            if (ksf?.Tables == null) return found;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (KsfTableBlock block in ksf.Tables)
            {
                if (block?.Rows == null || !IsKesifOzeti(block.Title)) continue;
                foreach (KsfTableRow row in block.Rows)
                {
                    if (row?.Cells == null || row.Cells.Length == 0) continue;
                    string poz = PozKey(Cell(row, 0));
                    if (poz.Length == 0 || !seen.Add(poz)) continue;
                    found.Add(poz);
                }
            }
            return found;
        }

        public static void ApplyIfKesifOzeti(KsfTableBlock block)
        {
            if (block?.Rows == null || block.Rows.Count == 0) return;
            if (!IsKesifOzeti(block.Title)) return;

            double nakPct = 10;
            const double kdvPct = 20;
            double artisPct = 9;
            double pozSum = 0;
            bool anyPoz = false;

            foreach (KsfTableRow row in block.Rows)
            {
                if (row?.Cells == null || row.Cells.Length == 0) continue;
                string key = Cell(row, 0);
                if (PozNo.IsMatch(key) && CsbBirimFiyatCatalog.TryGetPrice(PozKey(key), out double price))
                {
                    string qtyCell = Cell(row, 3);
                    if (!TryParseQuantity(qtyCell, out double qty, out string unit))
                        continue;
                    double tutar = Math.Round(price * qty, 2, MidpointRounding.AwayFromZero);
                    pozSum += tutar;
                    anyPoz = true;
                    SetCell(row, 2, FormatMoney(price));
                    SetCell(row, 3, FormatQuantity(qty, unit));
                    SetCell(row, 4, FormatMoney(tutar));
                    continue;
                }
                if (ContainsNorm(key, "NAKL"))
                    nakPct = ReadPercent(key, nakPct);
                else if (ContainsNorm(key, "AYLIK") || ContainsNorm(key, "ARTIS") || ContainsNorm(key, "ARTI"))
                    artisPct = ReadPercentTimes(key, artisPct);
            }

            if (!anyPoz) return;

            double nak = Math.Round(pozSum * nakPct / 100.0, 2, MidpointRounding.AwayFromZero);
            double ara = Math.Round(pozSum + nak, 2, MidpointRounding.AwayFromZero);
            double kdv = Math.Round(ara * kdvPct / 100.0, 2, MidpointRounding.AwayFromZero);
            double kdvToplam = Math.Round(ara + kdv, 2, MidpointRounding.AwayFromZero);
            double artis = Math.Round(kdvToplam * artisPct / 100.0, 2, MidpointRounding.AwayFromZero);
            double genel = Math.Round(kdvToplam + artis, 2, MidpointRounding.AwayFromZero);

            int toplamIdx = 0;
            foreach (KsfTableRow row in block.Rows)
            {
                if (row?.Cells == null || row.Cells.Length == 0) continue;
                string key = Cell(row, 0);
                if (ContainsNorm(key, "NAKL"))
                    SetAmountCell(row, FormatMoney(nak));
                else if (ContainsNorm(key, "KDV"))
                {
                    SetCell(row, 0, ReplacePercent(key, 20));
                    SetAmountCell(row, FormatMoney(kdv));
                }
                else if (ContainsNorm(key, "AYLIK") || ContainsNorm(key, "ARTIS") || ContainsNorm(key, "ARTI"))
                    SetAmountCell(row, FormatMoney(artis));
                else if (ContainsNorm(key, "TOPLAM"))
                {
                    if (toplamIdx == 0) SetAmountCell(row, FormatMoney(ara));
                    else if (toplamIdx == 1) SetAmountCell(row, FormatMoney(kdvToplam));
                    else SetAmountCell(row, FormatMoney(genel));
                    toplamIdx++;
                }
            }
        }

        private static bool IsKesifOzeti(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            string n = Norm(title);
            return n.IndexOf("KESIF OZET", StringComparison.Ordinal) >= 0;
        }

        private static string PozKey(string cell)
        {
            var m = PozNo.Match(cell ?? "");
            return m.Success ? m.Value.Trim() : "";
        }

        private static string Cell(KsfTableRow row, int i)
        {
            if (row.Cells == null || i < 0 || i >= row.Cells.Length) return "";
            return row.Cells[i] ?? "";
        }

        private static void SetCell(KsfTableRow row, int i, string value)
        {
            if (row.Cells == null || i < 0 || i >= row.Cells.Length) return;
            row.Cells[i] = value;
        }

        private static void SetAmountCell(KsfTableRow row, string value)
        {
            if (row.Cells == null || row.Cells.Length == 0) return;
            int last = row.Cells.Length - 1;
            for (int i = row.Cells.Length - 1; i >= 0; i--)
            {
                if (row.ColSpans != null && i < row.ColSpans.Length && row.ColSpans[i] <= 0)
                    continue;
                last = i;
                break;
            }
            row.Cells[last] = value;
        }

        private static bool TryParseQuantity(string cell, out double qty, out string unit)
        {
            qty = 0;
            unit = "";
            if (string.IsNullOrWhiteSpace(cell)) return false;
            var m = QtyUnit.Match(cell.Replace('\u00B3', '³'));
            if (!m.Success) return false;
            if (!TryParseStaNumber(m.Groups[1].Value, out qty)) return false;
            unit = m.Groups[2].Value.Trim();
            if (unit.Equals("m2", StringComparison.OrdinalIgnoreCase)) unit = "m²";
            if (unit.Equals("ton", StringComparison.OrdinalIgnoreCase)) unit = "tn";
            return true;
        }

        private static bool TryParseStaNumber(string raw, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim().Replace(" ", "").Replace(',', '.');
            var parts = s.Split('.');
            if (parts.Length == 1)
                return double.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
            string last = parts[parts.Length - 1];
            string intPart = string.Join("", parts, 0, parts.Length - 1);
            if (!double.TryParse(intPart + "." + last, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
                return false;
            return true;
        }

        private static string FormatMoney(double v)
        {
            string tr = v.ToString("N2", Tr);
            return tr.Replace(",", ".");
        }

        private static string FormatQuantity(double qty, string unit)
        {
            string n = qty.ToString("0.#", CultureInfo.InvariantCulture);
            if (unit == "m³") return n + " m³";
            if (unit == "m²") return n + " m²";
            return n + " " + unit;
        }

        private static string ReplacePercent(string text, int percent)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return Pct.Replace(text, "% " + percent.ToString(CultureInfo.InvariantCulture), 1);
        }

        private static double ReadPercent(string text, double fallback)
        {
            var m = Pct.Match(text ?? "");
            if (!m.Success) return fallback;
            if (int.TryParse(m.Groups[1].Value, out int p)) return p;
            return fallback;
        }

        private static double ReadPercentTimes(string text, double fallback)
        {
            var m = PctTimes.Match(text ?? "");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int a) && int.TryParse(m.Groups[2].Value, out int b))
                return a * b;
            return ReadPercent(text, fallback);
        }

        private static bool ContainsNorm(string text, string ascii)
        {
            return Norm(text).IndexOf(ascii, StringComparison.Ordinal) >= 0;
        }

        private static string Norm(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            return title.ToUpperInvariant()
                .Replace('\u0131', 'I').Replace('\u0130', 'I')
                .Replace('İ', 'I').Replace('ı', 'I')
                .Replace('\u015E', 'S').Replace('\u015F', 'S')
                .Replace('\u00D6', 'O').Replace('\u00F6', 'O');
        }
    }
}
