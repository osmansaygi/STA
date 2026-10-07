using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        private bool _kapamaDonatiFromSwx;

        /// <summary>
        /// GPR panel adı kesikse (P2B-078 → P2B-07) donatıyı .SWX'ten alır.
        /// Perde, GPR köşe düğümü (.SEE) + Bx/By ile ST4 perde kirişine bağlanır.
        /// </summary>
        private void EnsureKapamaPerdeDonatiFromSwx(string st4SourcePath)
        {
            _kapamaDonatiFromSwx = false;
            if (_gprPerdePanelDonati != null && _gprPerdePanelDonati.Count > 0) return;
            if (TryLoadKapamaDonatiFromSwx(st4SourcePath, out Dictionary<int, GprPerdePanelDonati> map)
                && map != null && map.Count > 0)
            {
                _gprPerdePanelDonati = map;
                _kapamaDonatiFromSwx = true;
            }
        }

        private bool TryLoadKapamaDonatiFromSwx(string st4SourcePath, out Dictionary<int, GprPerdePanelDonati> map)
        {
            map = null;
            if (_model == null || _model.Beams == null || _model.Floors == null) return false;
            string swxPath = SwxKapamaDonati.ResolvePath(st4SourcePath);
            string gprPath = ResolveGprPathNextToSt4(st4SourcePath);
            string seePath = ResolveSiblingPath(st4SourcePath, ".SEE");
            if (swxPath == null || gprPath == null || seePath == null) return false;

            List<SwxKapamaDonati.Rec> swx = SwxKapamaDonati.Read(swxPath);
            if (swx.Count == 0) return false;
            List<PanelKayit> panels = ReadGprPanelKayitlari(gprPath);
            if (panels.Count == 0) return false;
            Dictionary<int, (double xCm, double yCm)> nodes = ReadSeeNodesCm(seePath);
            if (nodes.Count == 0) return false;

            var swxUsed = new bool[swx.Count];
            foreach (PanelKayit panel in panels)
                TryAttachSwx(panel, swx, swxUsed);

            var axisById = new Dictionary<int, AxisLine>();
            if (_model.AxisX != null)
            {
                foreach (AxisLine ax in _model.AxisX)
                    if (ax != null) axisById[ax.Id] = ax;
            }
            if (_model.AxisY != null)
            {
                foreach (AxisLine ay in _model.AxisY)
                    if (ay != null) axisById[ay.Id] = ay;
            }

            var usedWalls = new HashSet<int>();
            var dict = new Dictionary<int, GprPerdePanelDonati>();
            foreach (PanelKayit panel in panels)
            {
                if (panel.BarCount < 2 || panel.DiaMm < 8) continue;
                if (!nodes.TryGetValue(panel.NodeI, out var pt)) continue;
                FloorInfo floor = FloorForPanelTag(panel.FloorTag);
                if (floor == null) continue;
                if (!TryPickWall(floor, panel, pt.xCm, pt.yCm, axisById, usedWalls, out int wallNo))
                    continue;
                int key = GprPerdePanelDonatiParser.Key(floor.FloorNo, wallNo);
                if (dict.ContainsKey(key)) continue;
                usedWalls.Add(key);
                dict[key] = new GprPerdePanelDonati
                {
                    FloorNo = floor.FloorNo,
                    WallNo = wallNo,
                    LayerCount = 2,
                    BarCount = panel.BarCount,
                    DiaMm = panel.DiaMm,
                    YatayDiaMm = panel.YatayDiaMm,
                    YataySpacingCm = panel.YataySpacingCm,
                    BxCm = panel.BxCm,
                    ByCm = panel.ByCm
                };
            }
            if (dict.Count == 0) return false;
            map = dict;
            return true;
        }

        private static void TryAttachSwx(PanelKayit panel, List<SwxKapamaDonati.Rec> swx, bool[] used)
        {
            int hit = -1;
            for (int i = 0; i < swx.Count; i++)
            {
                if (used[i]) continue;
                SwxKapamaDonati.Rec rec = swx[i];
                if (rec.BarCount != panel.BarCount || rec.DiaMm != panel.DiaMm) continue;
                if (Math.Abs(rec.AsCm2 - panel.AsCm2) > 0.08) continue;
                hit = i;
                break;
            }
            if (hit < 0) return;
            used[hit] = true;
            SwxKapamaDonati.Rec src = swx[hit];
            panel.BarCount = src.BarCount;
            panel.DiaMm = src.DiaMm;
            if (src.YatayDiaMm > 0) panel.YatayDiaMm = src.YatayDiaMm;
            if (src.YataySpacingCm > 0) panel.YataySpacingCm = src.YataySpacingCm;
        }

        private bool TryPickWall(
            FloorInfo floor,
            PanelKayit panel,
            double xCm,
            double yCm,
            Dictionary<int, AxisLine> axisById,
            HashSet<int> usedWalls,
            out int wallNo)
        {
            wallNo = 0;
            const double axisTolCm = 35.0;
            const double spanPadCm = 20.0;
            const double lenTolCm = 130.0;
            const double lenMarginCm = 15.0;
            const double thickTolCm = 1.5;
            double thick = Math.Min(panel.BxCm, panel.ByCm);
            double longCm = Math.Max(panel.BxCm, panel.ByCm);
            if (longCm < 20.0) return false;

            int bestNo = 0;
            double bestD = double.MaxValue;
            double secondD = double.MaxValue;
            foreach (BeamInfo beam in _model.Beams)
            {
                if (beam == null || beam.IsWallFlag != 1) continue;
                if (GetBeamFloorNo(beam.BeamId) != floor.FloorNo) continue;
                if (Math.Abs(beam.WidthCm - thick) > thickTolCm) continue;
                int no = GetBeamNumero(beam.BeamId);
                int key = GprPerdePanelDonatiParser.Key(floor.FloorNo, no);
                if (usedWalls.Contains(key)) continue;
                if (!WallSpanContains(beam, xCm, yCm, axisById, axisTolCm, spanPadCm, out double spanCm))
                    continue;
                double d = Math.Abs(spanCm - longCm);
                if (d > lenTolCm) continue;
                if (d < bestD)
                {
                    secondD = bestD;
                    bestD = d;
                    bestNo = no;
                }
                else if (d < secondD)
                {
                    secondD = d;
                }
            }
            if (bestNo <= 0) return false;
            if (secondD < double.MaxValue && secondD - bestD <= lenMarginCm) return false;
            wallNo = bestNo;
            return true;
        }

        private static bool WallSpanContains(
            BeamInfo beam,
            double xCm,
            double yCm,
            Dictionary<int, AxisLine> axisById,
            double axisTolCm,
            double spanPadCm,
            out double spanCm)
        {
            spanCm = 0;
            if (!axisById.TryGetValue(beam.FixedAxisId, out AxisLine fix)) return false;
            if (!axisById.TryGetValue(beam.StartAxisId, out AxisLine a)) return false;
            if (!axisById.TryGetValue(beam.EndAxisId, out AxisLine b)) return false;
            bool alongX = fix.Kind == AxisKind.Y || beam.FixedAxisId >= 2001;
            if (alongX)
            {
                if (Math.Abs(yCm - (fix.ValueCm + fix.Slope * xCm)) > axisTolCm) return false;
                double c0 = a.ValueCm + a.Slope * yCm;
                double c1 = b.ValueCm + b.Slope * yCm;
                double lo = Math.Min(c0, c1) - spanPadCm;
                double hi = Math.Max(c0, c1) + spanPadCm;
                if (xCm < lo || xCm > hi) return false;
                spanCm = Math.Abs(c1 - c0);
                return spanCm > 20.0;
            }
            if (Math.Abs(xCm - (fix.ValueCm + fix.Slope * yCm)) > axisTolCm) return false;
            double d0 = a.ValueCm + a.Slope * xCm;
            double d1 = b.ValueCm + b.Slope * xCm;
            double yLo = Math.Min(d0, d1) - spanPadCm;
            double yHi = Math.Max(d0, d1) + spanPadCm;
            if (yCm < yLo || yCm > yHi) return false;
            spanCm = Math.Abs(d1 - d0);
            return spanCm > 20.0;
        }

        private FloorInfo FloorForPanelTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || _model.Floors == null) return null;
            FloorInfo hit = null;
            int bestLen = -1;
            foreach (FloorInfo floor in _model.Floors)
            {
                if (floor == null) continue;
                string raw = (floor.ShortName ?? string.Empty).Trim();
                if (raw.Length == 0) continue;
                string bare = raw.TrimEnd('-');
                if (!string.Equals(bare, tag, StringComparison.OrdinalIgnoreCase)) continue;
                if (bare.Length > bestLen)
                {
                    bestLen = bare.Length;
                    hit = floor;
                }
            }
            return hit;
        }

        private static string ResolveSiblingPath(string st4SourcePath, string extensionWithDot)
        {
            if (string.IsNullOrEmpty(st4SourcePath)) return null;
            string dir = Path.GetDirectoryName(st4SourcePath);
            if (string.IsNullOrEmpty(dir)) return null;
            string baseName = Path.GetFileNameWithoutExtension(st4SourcePath);
            string p1 = Path.Combine(dir, baseName + extensionWithDot);
            if (File.Exists(p1)) return p1;
            string p2 = Path.Combine(dir, baseName + extensionWithDot.ToLowerInvariant());
            if (File.Exists(p2)) return p2;
            return null;
        }

        private sealed class PanelKayit
        {
            public string FloorTag;
            public int NodeI;
            public double BxCm;
            public double ByCm;
            public int BarCount;
            public int DiaMm;
            public int YatayDiaMm;
            public int YataySpacingCm;
            public double AsCm2;
        }

        private static readonly Regex RxPanelStory = new Regex(@"P(\d+B)-", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxDuseySwx = new Regex(@"(?:^|[^\d.])2[^\d]{1,4}(\d{1,3})[^\d]{1,4}(\d{2})\s*\(\s*d", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxYataySwx = new Regex(@"(\d{1,2})\s*/\s*(\d{1,3})\s*\(\s*yatay", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxBxSwx = new Regex(@"Bx\s*=\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxBySwx = new Regex(@"By\s*=\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxIj = new Regex(@"I/J\s*:\s*(\d+)\s*/\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxAs = new Regex(@"\d+\.\d+", RegexOptions.Compiled);
        private static readonly Regex RxSeeNode = new Regex(@"^\s*(\d+)\s+(-?\d+\.\d+)\s+(-?\d+\.\d+)\s+(-?\d+\.\d+)", RegexOptions.Compiled);

        private static List<PanelKayit> ReadGprPanelKayitlari(string gprPath)
        {
            var list = new List<PanelKayit>();
            string[] lines;
            try { lines = WindowsAnsiEncodings.ReadAllLines(gprPath); }
            catch { return list; }
            bool inSection = false;
            PanelKayit cur = null;
            foreach (string raw in lines)
            {
                string ascii = ToAsciiKapama(raw);
                if (!inSection)
                {
                    if (ascii.IndexOf("PANEL BETONARME HESAP", StringComparison.OrdinalIgnoreCase) >= 0)
                        inSection = true;
                    continue;
                }
                if (ascii.IndexOf("PANEL MOMENT", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (list.Count > 0) break;
                    inSection = false;
                    cur = null;
                    continue;
                }
                Match id = RxPanelStory.Match(ascii);
                Match bx = RxBxSwx.Match(ascii);
                if (id.Success && bx.Success)
                {
                    cur = new PanelKayit { FloorTag = id.Groups[1].Value.ToUpperInvariant() };
                    cur.BxCm = double.Parse(bx.Groups[1].Value, CultureInfo.InvariantCulture);
                    Match by = RxBySwx.Match(ascii);
                    if (by.Success)
                        cur.ByCm = double.Parse(by.Groups[1].Value, CultureInfo.InvariantCulture);
                    Match dus = RxDuseySwx.Match(ascii);
                    if (dus.Success)
                    {
                        cur.BarCount = int.Parse(dus.Groups[1].Value, CultureInfo.InvariantCulture);
                        cur.DiaMm = int.Parse(dus.Groups[2].Value, CultureInfo.InvariantCulture);
                    }
                    MatchCollection nums = RxAs.Matches(ascii);
                    if (nums.Count > 0)
                        cur.AsCm2 = double.Parse(nums[nums.Count - 1].Value, CultureInfo.InvariantCulture);
                    list.Add(cur);
                    continue;
                }
                if (cur == null) continue;
                Match yat = RxYataySwx.Match(ascii);
                if (yat.Success && cur.YatayDiaMm <= 0)
                {
                    cur.YatayDiaMm = int.Parse(yat.Groups[1].Value, CultureInfo.InvariantCulture);
                    cur.YataySpacingCm = int.Parse(yat.Groups[2].Value, CultureInfo.InvariantCulture);
                }
                Match ij = RxIj.Match(ascii);
                if (ij.Success && cur.NodeI <= 0)
                    cur.NodeI = int.Parse(ij.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            return list;
        }

        private static Dictionary<int, (double xCm, double yCm)> ReadSeeNodesCm(string seePath)
        {
            var map = new Dictionary<int, (double xCm, double yCm)>();
            string[] lines;
            try { lines = WindowsAnsiEncodings.ReadAllLines(seePath); }
            catch { return map; }
            foreach (string raw in lines)
            {
                Match m = RxSeeNode.Match(raw);
                if (!m.Success) continue;
                int no = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                double x = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 100.0;
                double y = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) * 100.0;
                map[no] = (x, y);
            }
            return map;
        }

        private static string ToAsciiKapama(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(c >= 32 && c <= 126 ? c : ' ');
            return sb.ToString();
        }
    }
}
