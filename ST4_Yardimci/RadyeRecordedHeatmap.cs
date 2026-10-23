using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.Geometry;

namespace ST4Yardimci
{
    // Recorded nodal design As; never reconstruct As by fitting raw SO1 forces.
    internal sealed class RadyeRecordedHeatmap
    {
        internal Point3d[] Nodes;
        internal int[][] Elements;
        internal double[,] As;
        internal double[] Base, Peak;
        internal bool[] Connected;
        internal double MinX, MaxY, Width, Height;
        internal readonly List<RadyeIlaveRunner.RecordedJob> Jobs = new List<RadyeIlaveRunner.RecordedJob>();
        private static double N(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        internal static RadyeRecordedHeatmap Read(RadyeOtomatikSource source)
        {
            var h = new RadyeRecordedHeatmap { Base = source.BaseAs, Peak = source.PeakAs };
            string[] rad = RadyeOtomatikSource.ReadRadText(source.ProjectBase + ".RAD").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            int start = 7 + 2 * source.PlateCount;
            h.Elements = new int[source.ElementCount][];
            h.Nodes = new Point3d[source.NodeCount]; h.As = new double[source.NodeCount, 4]; h.Connected = new bool[source.NodeCount];
            for (int j = 0; j < h.Elements.Length; j++)
            {
                double[] r = rad[start + j].Split(',').Select(N).ToArray();
                if (r.Length != 8 || Math.Abs(r[6] - .4) > .00001) throw new InvalidDataException("Bu karşılaştırma sürümü yalnız 40 cm radye için doğrulandı.");
                var ids = new List<int>();
                for (int k = 1; k <= 4; k++)
                {
                    if (r[k] != Math.Truncate(r[k]) || r[k] < 0 || r[k] > h.Nodes.Length) throw new InvalidDataException("RAD düğüm kimliği geçersiz.");
                    if (r[k] > 0) { int id = (int)r[k] - 1; ids.Add(id); h.Connected[id] = true; }
                }
                if (ids.Count < 3 || ids.Distinct().Count() != ids.Count) throw new InvalidDataException("RAD eleman bağlantısı geçersiz.");
                h.Elements[j] = ids.ToArray();
            }
            for (int j = 0; j < h.Nodes.Length; j++)
            {
                double[] r = rad[start + h.Elements.Length + j].Split(',').Select(N).ToArray();
                if (r.Length != 6 || r.Any(v => double.IsNaN(v) || double.IsInfinity(v))) throw new InvalidDataException("RAD koordinatı geçersiz.");
                h.Nodes[j] = new Point3d(r[1], r[2], r[3]);
            }
            h.MinX = h.Nodes.Min(p => p.X); h.MaxY = h.Nodes.Max(p => p.Y);
            h.Width = h.Nodes.Max(p => p.X) - h.MinX; h.Height = h.MaxY - h.Nodes.Min(p => p.Y);
            string gprPath = source.ProjectBase + ".GPR";
            if (!File.Exists(gprPath)) throw new InvalidDataException("Düğüm As tablosunu içeren mevcut GPR dosyası yok: " + gprPath);
            string gpr = RadyeOtomatikSource.ReadRadText(gprPath);
            if (gpr.IndexOf("(" + Path.GetFileName(source.ProjectBase) + ".ST4)", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidDataException("GPR farklı projeye ait.");
            bool inTable = false; int row = 0;
            foreach (string line in gpr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                if (new[] { "Nokta", "Mxalt", "Mxust", "Myalt", "Myust", "Asax", "Asux", "Asay", "Asuy" }.All(line.Contains)) { inTable = true; continue; }
                if (!inTable || !line.StartsWith("¡", StringComparison.Ordinal)) continue;
                string[] parts = line.Split('¡');
                if (parts.Length != 16) continue;
                double[] v = new double[14]; bool numeric = true;
                for (int i = 0; i < 14; i++) if (!double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) { numeric = false; break; }
                if (!numeric) continue;
                if (v.Any(x => double.IsNaN(x) || double.IsInfinity(x)) || v[0] != row + 1) throw new InvalidDataException("GPR düğüm tablosu eksik veya sırası farklı.");
                Point3d p = h.Nodes[row];
                if (Math.Abs(v[3] - p.X) > .000511 || Math.Abs(v[4] - p.Y) > .000511 || Math.Abs(v[5] - p.Z) > .000511)
                    throw new InvalidDataException("GPR düğüm koordinatları güncel RAD ile uyuşmuyor.");
                if (h.Connected[row] && (v[1] != 40 || v[2] != 40)) throw new InvalidDataException("GPR radye kalınlığı uyuşmuyor.");
                int[] columns = { 11, 10, 13, 12 };
                for (int i = 0; i < 4; i++) { h.As[row, i] = v[columns[i]]; if (h.As[row, i] < 0) throw new InvalidDataException("GPR As değeri negatif."); }
                if (++row == h.Nodes.Length) break;
            }
            if (row != h.Nodes.Length) throw new InvalidDataException("GPR düğüm As tablosu tam değil.");
            for (int ch = 0; ch < 4; ch++)
            {
                double peak = Enumerable.Range(0, row).Where(i => h.Connected[i]).Max(i => h.As[i, ch]);
                if (Math.Abs(peak - h.Peak[ch]) > .00511) throw new InvalidDataException("GPR ve SO1 tepe As değerleri uyuşmuyor.");
                if (Enumerable.Range(0, row).Any(i => h.Connected[i] && Math.Abs(h.As[i, ch] - h.Base[ch]) <= .005))
                    throw new InvalidDataException("GPR'nin 0.01 hassasiyeti nedeniyle ilave sınırında belirsiz düğüm var.");
            }
            h.Assign(source);
            return h;
        }

        private void Assign(RadyeOtomatikSource source)
        {
            // STA's recorded bar selections supply only diameter/spacing and region association.
            // Geometry is covered from the actual GPR As support, not copied REBAR lines.
            var assigned = source.Selections.Select(s => new List<int>()).ToArray();
            for (int ch = 0; ch < 4; ch++)
            {
                var candidates = Enumerable.Range(0, source.Selections.Count).Where(i =>
                    source.Selections[i].IsY == (ch >= 2) && source.Selections[i].Upper == (ch % 2 == 0)).ToArray();
                for (int e = 0; e < Elements.Length; e++)
                {
                    if (!Elements[e].Any(n => As[n, ch] > Base[ch])) continue;
                    if (candidates.Length == 0) throw new InvalidDataException("Pozitif ilave As için STA çap/aralık seçimi yok.");
                    var xs = Elements[e].Select(n => Nodes[n].X).ToArray(); var ys = Elements[e].Select(n => Nodes[n].Y).ToArray();
                    double x0 = xs.Min(), x1 = xs.Max(), y0 = ys.Min(), y1 = ys.Max();
                    Func<int, double> distance = i => {
                        Point2d p = source.Selections[i].LabelPoint;
                        double dx = Math.Max(0, Math.Max(x0 - p.X, p.X - x1)), dy = Math.Max(0, Math.Max(y0 - p.Y, p.Y - y1));
                        return Math.Sqrt(dx * dx + dy * dy);
                    };
                    var ordered = candidates.OrderBy(distance).ToArray(); int best = ordered[0];
                    if (distance(best) > Math.Max(3, source.Selections[best].Length / 100.0) ||
                        (ordered.Length > 1 && Math.Abs(distance(best) - distance(ordered[1])) < .01))
                        throw new InvalidDataException("Isı alanı ile STA çap/aralık seçimi tekil eşleşmedi.");
                    double required = Elements[e].Max(n => Math.Max(0, As[n, ch] - Base[ch])) + .005;
                    var selection = source.Selections[best];
                    double provided = Math.PI * selection.Diameter * selection.Diameter / (4.0 * selection.Spacing);
                    if (selection.Spacing < 1 || provided < required) throw new InvalidDataException("STA ilave çap/aralığı kayıtlı As ihtiyacını karşılamıyor.");
                    assigned[best].Add(e);
                }
            }
            for (int i = 0; i < assigned.Length; i++)
            {
                if (assigned[i].Count == 0) throw new InvalidDataException("Kayıtlı ilave seçimine karşılık pozitif As alanı yok.");
                var points = assigned[i].SelectMany(e => Elements[e]).Distinct().Select(n => Nodes[n]).ToArray();
                double x0 = points.Min(p => p.X), x1 = points.Max(p => p.X), y0 = points.Min(p => p.Y), y1 = points.Max(p => p.Y);
                var s = source.Selections[i];
                Jobs.Add(new RadyeIlaveRunner.RecordedJob { IsY = s.IsY, Upper = s.Upper, Diameter = s.Diameter, Spacing = s.Spacing,
                    Ring = new List<Point2d> { new Point2d(x0, y0), new Point2d(x1, y0), new Point2d(x1, y1), new Point2d(x0, y1) } });
            }
        }

        internal Point3d Place(Point3d origin, Point3d p) => new Point3d(origin.X + (p.X - MinX) * 100, origin.Y + (MaxY - p.Y) * 100, origin.Z);
    }
}
