using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ST4PlanIdCiz
{
    public sealed class GprPerdePanelDonati
    {
        public int FloorNo;
        public int WallNo;
        public int LayerCount = 2;
        public int BarCount;
        public int DiaMm = 12;
        public int YatayDiaMm;
        public int YataySpacingCm;
        public double BxCm;
        public double ByCm;
        public double HkCm;
    }

    /// <summary>
    /// GPR: ilk "PANEL BETONARME HESAP SONUÇLARI" ile
    /// "PANEL MOMENT ve KESME KAPASİTE KONTROLU" arası düşey/yatay perde donatısı.
    /// </summary>
    public static class GprPerdePanelDonatiParser
    {
        // AA_01: PB2-42 (kat-perde). MS_A_01: PB-071 (yalniz perde no).
        private static readonly Regex RxPanelIdFloor = new Regex(@"PB\s*(\d+)\s*[-]\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxPanelIdOnly = new Regex(@"PB\s*[-]\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // "2×17ø12 (düşey)" — "11.02│ø8/15 (yatay)" içindeki 2+8/15 eşleşmesin.
        private static readonly Regex RxDusey = new Regex(@"(?:^|[^0-9.])2[^0-9]{1,4}(\d{1,3})[^0-9]{1,4}(\d{2})\s*\(\s*d", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxYatay = new Regex(@"(\d{1,2})\s*/\s*(\d{1,3})\s*\(\s*yatay", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxBx = new Regex(@"Bx\s*=\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxBy = new Regex(@"By\s*=\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxHk = new Regex(@"Hk\s*=\s*(\d+(?:\.\d+)?)\s*m", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxCClass = new Regex(@"\bC\s*(2[05]|30|35|40|45|50)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxFykKgcm = new Regex(@"\b(4200|5000)\b", RegexOptions.Compiled);

        public static void TryReadMaterials(string gprFilePath, out double fckMPa, out double fykMPa)
        {
            fckMPa = 30.0;
            fykMPa = 420.0;
            if (string.IsNullOrWhiteSpace(gprFilePath) || !File.Exists(gprFilePath)) return;
            try
            {
                string[] lines = WindowsAnsiEncodings.ReadAllLines(gprFilePath);
                int n = Math.Min(lines.Length, 1200);
                for (int i = 0; i < n; i++)
                {
                    string ascii = ToAscii(lines[i]);
                    var cM = RxCClass.Match(ascii);
                    if (cM.Success)
                        fckMPa = int.Parse(cM.Groups[1].Value);
                    var sM = RxFykKgcm.Match(ascii);
                    if (sM.Success)
                        fykMPa = int.Parse(sM.Groups[1].Value) / 10.0;
                    if (ascii.IndexOf("PANEL BETONARME", StringComparison.OrdinalIgnoreCase) >= 0)
                        break;
                }
            }
            catch { }
            if (fckMPa < 16.0) fckMPa = 30.0;
            if (fykMPa < 200.0) fykMPa = 420.0;
        }

        public static bool TryParse(string gprFilePath, out Dictionary<int, GprPerdePanelDonati> map, out string error)
        {
            map = null;
            error = null;
            if (string.IsNullOrWhiteSpace(gprFilePath) || !File.Exists(gprFilePath))
            {
                error = "GPR yok.";
                return false;
            }

            string[] lines;
            try { lines = WindowsAnsiEncodings.ReadAllLines(gprFilePath); }
            catch (Exception ex)
            {
                error = "GPR okunamadi: " + ex.Message;
                return false;
            }
            var dict = ParseLines(lines);
            if (dict == null || dict.Count == 0)
            {
                error = "PANEL BETONARME donati bulunamadi.";
                return false;
            }
            map = dict;
            return true;
        }

        public static int Key(int floorNo, int wallNo) => floorNo * 100000 + wallNo;

        private static Dictionary<int, GprPerdePanelDonati> ParseLines(string[] lines)
        {
            var dict = new Dictionary<int, GprPerdePanelDonati>();
            bool inSection = false;
            GprPerdePanelDonati cur = null;
            foreach (var raw in lines)
            {
                string ascii = ToAscii(raw);
                if (!inSection)
                {
                    if (ascii.IndexOf("PANEL BETONARME HESAP", StringComparison.OrdinalIgnoreCase) >= 0)
                        inSection = true;
                    continue;
                }
                if (ascii.IndexOf("PANEL MOMENT", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (dict.Count > 0) break;
                    inSection = false;
                    cur = null;
                    continue;
                }

                int floorNo = 0, wallNo = 0;
                var idFloor = RxPanelIdFloor.Match(ascii);
                if (idFloor.Success)
                {
                    floorNo = int.Parse(idFloor.Groups[1].Value);
                    wallNo = int.Parse(idFloor.Groups[2].Value);
                }
                else
                {
                    var idOnly = RxPanelIdOnly.Match(ascii);
                    if (idOnly.Success)
                        wallNo = int.Parse(idOnly.Groups[1].Value);
                }
                if (wallNo > 0)
                {
                    int key = Key(floorNo, wallNo);
                    if (!dict.TryGetValue(key, out cur) || cur == null)
                    {
                        cur = new GprPerdePanelDonati { FloorNo = floorNo, WallNo = wallNo };
                        dict[key] = cur;
                    }
                }
                if (cur == null) continue;

                var bxM = RxBx.Match(ascii);
                if (bxM.Success)
                    cur.BxCm = double.Parse(bxM.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var byM = RxBy.Match(ascii);
                if (byM.Success)
                    cur.ByCm = double.Parse(byM.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var hkM = RxHk.Match(ascii);
                if (hkM.Success)
                    cur.HkCm = double.Parse(hkM.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 100.0;

                foreach (Match dM in RxDusey.Matches(ascii))
                {
                    int n = int.Parse(dM.Groups[1].Value);
                    int dia = int.Parse(dM.Groups[2].Value);
                    if (n < 2 || n > 400) continue;
                    if (dia < 8 || dia > 32) continue;
                    cur.BarCount = n;
                    cur.DiaMm = dia;
                    cur.LayerCount = 2;
                }
                var yM = RxYatay.Match(ascii);
                if (yM.Success)
                {
                    cur.YatayDiaMm = int.Parse(yM.Groups[1].Value);
                    cur.YataySpacingCm = int.Parse(yM.Groups[2].Value);
                }
            }
            var prune = new List<int>();
            foreach (var kv in dict)
            {
                if (kv.Value.BarCount <= 0 || kv.Value.DiaMm <= 0)
                    prune.Add(kv.Key);
            }
            foreach (var k in prune) dict.Remove(k);
            return dict;
        }

        private static string ToAscii(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c >= 32 && c <= 126) sb.Append(c);
                else sb.Append(' ');
            }
            return sb.ToString();
        }
    }
}
