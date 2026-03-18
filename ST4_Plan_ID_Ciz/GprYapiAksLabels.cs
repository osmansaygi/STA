using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>GPR sayfasındaki "YAPI AKS BİLGİLERİ" tablosundan X/Y aks isimlerini okur.</summary>
    public static class GprYapiAksLabels
    {
        /// <summary>Aynı dizindeki .GPR dosyasından aks etiketlerini modele yazar.</summary>
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
            if (!File.Exists(gpr)) return;
            try
            {
                Parse(gpr, model);
            }
            catch { /* GPR okunamazsa varsayılan etiketler */ }
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
                string line = rawLine.Replace('\u2502', '|').Replace('│', '|');
                if (!line.Contains("|")) continue;
                var matches = rowRx.Matches(line);
                if (matches.Count >= 1)
                {
                    int xNo = int.Parse(matches[0].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    string xName = matches[0].Groups[2].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(xName))
                        model.GprAxisXLabelByRow[xNo] = xName;
                }
                if (matches.Count >= 2)
                {
                    int yNo = int.Parse(matches[1].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    string yName = matches[1].Groups[2].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(yName))
                        model.GprAxisYLabelByRow[yNo] = yName;
                }
                else if (matches.Count == 1 && line.TrimStart().Length > 0)
                {
                    int firstPipe = line.IndexOf('|');
                    if (firstPipe > 42)
                    {
                        int yNo = int.Parse(matches[0].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                        string yName = matches[0].Groups[2].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(yName))
                            model.GprAxisYLabelByRow[yNo] = yName;
                    }
                }
            }
        }

        private static string ReadAllTextRobust(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            try { return Encoding.GetEncoding(1254).GetString(bytes); }
            catch { }
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
