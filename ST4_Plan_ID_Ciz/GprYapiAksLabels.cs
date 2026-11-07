using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>GPR sayfasındaki "YAPI AKS BİLGİLERİ" tablosundan X/Y aks isimlerini okur.</summary>
    public static class GprYapiAksLabels
    {
        /// <summary>Aynı dizindeki .GPR dosyasından aks etiketlerini modele yazar; GPR veya tablosu yoksa .AKS dosyasından.</summary>
        public static void TryMergeFromGprBesideSt4(string st4Path, St4Model model)
        {
            if (model == null || string.IsNullOrEmpty(st4Path)) return;
            model.GprAxisXLabelByRow.Clear();
            model.GprAxisYLabelByRow.Clear();
            string dir = Path.GetDirectoryName(st4Path);
            string baseName = Path.GetFileNameWithoutExtension(st4Path);
            if (string.IsNullOrEmpty(dir)) return;
            string gpr = Path.Combine(dir, baseName + ".GPR");
            if (!File.Exists(gpr)) gpr = Path.Combine(dir, baseName + ".gpr");
            if (File.Exists(gpr))
            {
                try
                {
                    Parse(gpr, model);
                }
                catch { /* GPR okunamazsa .AKS veya varsayılan etiketler */ }
            }
            if (model.GprAxisXLabelByRow.Count == 0 && model.GprAxisYLabelByRow.Count == 0)
                TryFillFromAksBesideSt4(st4Path, model);
        }

        /// <summary>
        /// STA4CAD .AKS: satır başına bir isim (4 karakter, boş olabilir), ST4 /Axis data/ sırasıyla
        /// önce X akslarının tamamı, ardından Y aksları. Boş isimler varsayılan etikette kalır.
        /// </summary>
        private static void TryFillFromAksBesideSt4(string st4Path, St4Model model)
        {
            int nx = model.AxisX.Count, ny = model.AxisY.Count;
            if (nx == 0 && ny == 0) return;
            string dir = Path.GetDirectoryName(st4Path);
            string baseName = Path.GetFileNameWithoutExtension(st4Path);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(baseName)) return;
            string aks = Path.Combine(dir, baseName + ".AKS");
            if (!File.Exists(aks)) aks = Path.Combine(dir, baseName + ".aks");
            if (!File.Exists(aks)) return;
            string[] lines;
            try
            {
                lines = WindowsAnsiEncodings.TryGet(1254, out Encoding enc1254)
                    ? File.ReadAllLines(aks, enc1254)
                    : File.ReadAllLines(aks);
            }
            catch { return; }
            for (int i = 0; i < nx && i < lines.Length; i++)
            {
                string t = (lines[i] ?? string.Empty).Trim();
                if (t.Length > 0) model.GprAxisXLabelByRow[i + 1] = t;
            }
            for (int j = 0; j < ny && nx + j < lines.Length; j++)
            {
                string t = (lines[nx + j] ?? string.Empty).Trim();
                if (t.Length > 0) model.GprAxisYLabelByRow[j + 1] = t;
            }
        }

        private static void Parse(string gprPath, St4Model model)
        {
            string text = ReadAllTextRobust(gprPath);
            if (string.IsNullOrEmpty(text)) return;
            int idx = text.IndexOf("YAPI AKS", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return;
            int end = text.IndexOf("KAT KOLONLARI", idx, StringComparison.OrdinalIgnoreCase);
            if (end < 0) end = text.IndexOf("1. KAT KOLON", idx, StringComparison.OrdinalIgnoreCase);
            if (end < 0) end = Math.Min(idx + 8000, text.Length);
            string block = text.Substring(idx, end - idx);
            var lines = block.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            // Blok: | no | isim | Ax | Bx | ... | no | isim | Ay | By |
            var rowRx = new Regex(
                @"\|\s*(\d+)\s*\|\s*([^|]*?)\s*\|\s*[-\d.,]+\s*\|\s*[-\d.,]+\s*",
                RegexOptions.CultureInvariant);
            foreach (var rawLine in lines)
            {
                if (rawLine.IndexOf("no", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    rawLine.IndexOf("isim", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (rawLine.IndexOf("X y", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    rawLine.IndexOf("Y y", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                string line = NormalizeTableSeparators(rawLine);
                if (!line.Contains("|")) continue;
                var matches = rowRx.Matches(line);
                if (matches.Count >= 2)
                {
                    int xNo = int.Parse(matches[0].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    string xName = matches[0].Groups[2].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(xName))
                        model.GprAxisXLabelByRow[xNo] = xName;

                    int yNo = int.Parse(matches[1].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    string yName = matches[1].Groups[2].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(yName))
                        model.GprAxisYLabelByRow[yNo] = yName;
                }
                else if (matches.Count == 1)
                {
                    int firstPipe = line.IndexOf('|');
                    int no = int.Parse(matches[0].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    string name = matches[0].Groups[2].Value.Trim();
                    if (firstPipe > 30)
                    {
                        if (!string.IsNullOrWhiteSpace(name))
                            model.GprAxisYLabelByRow[no] = name;
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(name))
                            model.GprAxisXLabelByRow[no] = name;
                    }
                }
            }
        }

        private static string ReadAllTextRobust(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            // OEM 857/437/850'de 0xB3 = │ (box-drawing vertical).
            // Hangi encoding kullanılırsa kullanılsın sorun çıkmaması için
            // byte seviyesinde doğrudan ASCII pipe'a dönüştür.
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == 0xB3) bytes[i] = 0x7C; // │ → |
                if (bytes[i] == 0xED) bytes[i] = 0x7C; // í/� → |
            }
            if (WindowsAnsiEncodings.TryGet(1254, out Encoding enc1254))
                return enc1254.GetString(bytes);
            return WindowsAnsiEncodings.DecodeWindows1254Bytes(bytes);
        }

        private static string NormalizeTableSeparators(string rawLine)
        {
            if (string.IsNullOrEmpty(rawLine)) return rawLine ?? string.Empty;
            var sb = new StringBuilder(rawLine.Length);
            for (int i = 0; i < rawLine.Length; i++)
            {
                char ch = rawLine[i];
                bool keep =
                    char.IsLetterOrDigit(ch) ||
                    char.IsWhiteSpace(ch) ||
                    ch == '.' || ch == ',' || ch == '-' || ch == '\'';
                sb.Append(keep ? ch : '|');
            }

            // Birden fazla ardışık ayırıcıyı tek "|" yap (regex eşleşmesini stabil tutar).
            var collapsed = new StringBuilder(sb.Length);
            bool prevPipe = false;
            foreach (char ch in sb.ToString())
            {
                bool isPipe = ch == '|';
                if (!(isPipe && prevPipe))
                    collapsed.Append(ch);
                prevPipe = isPipe;
            }
            return collapsed.ToString();
        }
    }
}
