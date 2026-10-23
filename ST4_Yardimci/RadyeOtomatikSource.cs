using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ST4Yardimci
{
    // STA plan geometry and selected diameter/spacing. Actual nodal As is read
    // separately from GPR by RadyeRecordedHeatmap and checked against RAD/SO1.
    internal sealed class RadyeOtomatikSource : IDisposable
    {
        internal static readonly Regex BarLabel = new Regex(
            @"(?<n>\d+)\s*[ƒøØΦφ]\s*(?<d>\d{1,2})\s*/\s*(?<s>\d+)\s*[lL]\s*=\s*(?<len>\d+)\s*\((?<side>alt|ust|üst)\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        internal readonly Database Drawing = new Database(false, true);
        internal readonly ObjectIdCollection PlanIds = new ObjectIdCollection();
        internal readonly List<string> Bars = new List<string>();
        internal sealed class BarSelection
        {
            internal int Diameter, Spacing, Length;
            internal bool IsY, Upper;
            internal Point2d LabelPoint;
            internal string Label;
        }
        internal readonly List<BarSelection> Selections = new List<BarSelection>();
        internal readonly List<Point2d> Foundation = new List<Point2d>();
        internal readonly double[] BaseAs = new double[4], PeakAs = new double[4];
        internal string ProjectBase, DrawingPath;
        internal double Scale, MinX, MinY, MaxX, MaxY;
        internal int PlateCount, NodeCount, ElementCount;
        private readonly Dictionary<string, byte[]> inputHashes = new Dictionary<string, byte[]>();

        internal void EnsureUnchanged()
        {
            using (var hash = SHA256.Create())
                foreach (var input in inputHashes)
                    if (!File.Exists(input.Key) || !hash.ComputeHash(ReadShared(input.Key)).SequenceEqual(input.Value))
                        throw new InvalidDataException("Okuma sırasında hesap dosyası değişti; çizim iptal edildi: " + input.Key);
        }

        internal static RadyeOtomatikSource Open(string projectPath)
        {
            var source = new RadyeOtomatikSource();
            try { source.Read(projectPath); return source; }
            catch { source.Dispose(); throw; }
        }

        private static double Number(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        internal static byte[] ReadShared(string path)
        {
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var output = new MemoryStream()) { input.CopyTo(output); return output.ToArray(); }
        }
        internal static string ReadRadText(string path)
        {
            byte[] bytes = ReadShared(path);
            try { return Encoding.GetEncoding(1254).GetString(bytes); }
            catch (NotSupportedException)
            {
                // RAD sayıları ASCII; yol satırındaki Türkçe harfleri code-page
                // sağlayıcısı bulunmayan hostta da kaybetmeden oku.
                return Encoding.GetEncoding(28591).GetString(bytes)
                    .Replace('\u00d0', '\u011e').Replace('\u00dd', '\u0130').Replace('\u00de', '\u015e')
                    .Replace('\u00f0', '\u011f').Replace('\u00fd', '\u0131').Replace('\u00fe', '\u015f');
            }
        }
        private void Read(string projectPath)
        {
            ProjectBase = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath)), Path.GetFileNameWithoutExtension(projectPath));
            DrawingPath = ProjectBase + "_MatTem.dwg";
            foreach (string path in new[] { ProjectBase + ".ST4", ProjectBase + ".RAD", ProjectBase + ".SO1", ProjectBase + ".GPR", ProjectBase + ".MATBQF", DrawingPath })
            {
                if (!File.Exists(path)) throw new InvalidDataException("Gerekli proje dosyası yok: " + path);
                using (var hash = SHA256.Create()) inputHashes[path] = hash.ComputeHash(ReadShared(path));
            }
            string[] rad = ReadRadText(ProjectBase + ".RAD")
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (rad.Length < 9 || rad[0].Trim() != "V14.01") throw new InvalidDataException("RAD sürümü doğrulanamadı (beklenen V14.01).");
            string[] counts = rad[1].Split(',');
            PlateCount = int.Parse(counts[0], CultureInfo.InvariantCulture);
            NodeCount = int.Parse(counts[1], CultureInfo.InvariantCulture);
            ElementCount = int.Parse(counts[2], CultureInfo.InvariantCulture);
            if (PlateCount < 1 || PlateCount > 10000 || NodeCount < 3 || ElementCount < 1)
                throw new InvalidDataException("RAD ağ sayıları geçersiz.");
            string radProject = Path.GetFileNameWithoutExtension(rad[6].Trim());
            if (!string.Equals(radProject, Path.GetFileName(ProjectBase), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("RAD farklı bir projeye ait.");
            var corners = new List<Point2d>();
            for (int i = 0; i < PlateCount; i++)
            {
                double[] row = rad[8 + i * 2].Split(',').Select(Number).ToArray();
                if (row.Length != 12) throw new InvalidDataException("RAD plak köşeleri doğrulanamadı.");
                for (int j = 0; j < 4; j++) corners.Add(new Point2d(row[j * 3], row[j * 3 + 1]));
            }
            double x0 = corners.Min(p => p.X), x1 = corners.Max(p => p.X);
            double y0 = corners.Min(p => p.Y), y1 = corners.Max(p => p.Y);
            byte[] results = ReadShared(ProjectBase + ".SO1");
            if (results.Length != checked(ElementCount * 2772 + 350))
                throw new InvalidDataException("SO1 kayıt düzeni bu proje için doğrulanamadı.");
            for (int i = 0; i < 4; i++)
            {
                BaseAs[i] = BitConverter.ToSingle(results, 8 + i * 4);
                PeakAs[i] = BitConverter.ToSingle(results, 24 + i * 4);
                if (!(BaseAs[i] > 0) || double.IsInfinity(BaseAs[i]) || double.IsNaN(PeakAs[i]) || PeakAs[i] < 0 || double.IsInfinity(PeakAs[i]))
                    throw new InvalidDataException("SO1 genel/tepe As değerleri geçersiz.");
            }
            if (Math.Abs(BitConverter.ToSingle(results, 0)) > .00001)
                throw new InvalidDataException("Sıfırdan farklı asal donatı açısı bu karşılaştırma sürümünde doğrulanmadı.");
            Drawing.ReadDwgFile(DrawingPath, FileOpenMode.OpenForReadAndAllShare, false, null);
            Drawing.CloseInput(true);
            byte[] bqf = ReadShared(ProjectBase + ".MATBQF");
            using (var tr = Drawing.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(Drawing.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                var outline = new List<Point2d>(); var barKeys = new List<string>();
                bool projectTag = false;
                foreach (ObjectId id in ms)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e is DBText tag && (tag.TextString ?? "").StartsWith("2#RADYE/", StringComparison.OrdinalIgnoreCase))
                        projectTag = string.Equals(Path.GetFileName(tag.TextString.Substring(8)), Path.GetFileName(ProjectBase), StringComparison.OrdinalIgnoreCase);
                    if (e is Line line && e.Layer.Equals("PENC3", StringComparison.OrdinalIgnoreCase))
                    { outline.Add(new Point2d(line.StartPoint.X, line.StartPoint.Y)); outline.Add(new Point2d(line.EndPoint.X, line.EndPoint.Y)); }
                    if (!(e is DBText text) || !e.Layer.Equals("PENC3", StringComparison.OrdinalIgnoreCase)) continue;
                    Match m = BarLabel.Match(text.TextString ?? "");
                    if (!m.Success) continue;
                    double axis = text.Rotation;
                    bool isX = Math.Abs(Math.Sin(axis)) < .00001, isY = Math.Abs(Math.Cos(axis)) < .00001;
                    if (!isX && !isY) throw new InvalidDataException("İlave yazısının yönü doğrulanamadı.");
                    string side = m.Groups["side"].Value.Equals("alt", StringComparison.OrdinalIgnoreCase) ? "alt" : "üst";
                    string key = m.Groups["n"].Value + "|" + m.Groups["d"].Value + "|" + m.Groups["s"].Value + "|" + m.Groups["len"].Value;
                    barKeys.Add(key);
                    Bars.Add((isX ? "X " : "Y ") + side + ": " + m.Groups["n"].Value + "ø" + m.Groups["d"].Value + "/" + m.Groups["s"].Value + " L=" + m.Groups["len"].Value);
                    Selections.Add(new BarSelection { Diameter = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture),
                        Spacing = int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture),
                        Length = int.Parse(m.Groups["len"].Value, CultureInfo.InvariantCulture), IsY = isY, Upper = side == "üst",
                        LabelPoint = new Point2d(text.Position.X, text.Position.Y), Label = Bars[Bars.Count - 1] });
                }
                if (!projectTag || outline.Count < 8 || Bars.Count == 0)
                    throw new InvalidDataException("STA radye çiziminin proje/ilave/çevre kaydı doğrulanamadı. Kaynak çizimi STA'dan yeniden üretin.");
                if (bqf.Length < Bars.Count * 8) throw new InvalidDataException("MATBQF ilave kayıtları eksik.");
                var records = new List<string>();
                for (int i = 0; i < Bars.Count; i++) records.Add(string.Join("|", Enumerable.Range(0, 4)
                    .Select(j => BitConverter.ToInt16(bqf, i * 8 + j * 2).ToString(CultureInfo.InvariantCulture))));
                if (!barKeys.OrderBy(k => k).SequenceEqual(records.OrderBy(k => k)))
                    throw new InvalidDataException("MatTem ilave seçimi ile MATBQF uyuşmuyor; farklı hesap çıktıları karıştırılmadı.");
                MinX = outline.Min(p => p.X); MaxX = outline.Max(p => p.X);
                MinY = outline.Min(p => p.Y); MaxY = outline.Max(p => p.Y);
                double sx = (MaxX - MinX) / (x1 - x0), sy = (MaxY - MinY) / (y1 - y0);
                if (!(sx > 0) || Math.Abs(sx - sy) > sx * .0001)
                    throw new InvalidDataException("STA çizim ölçeği RAD geometrisiyle uyuşmuyor.");
                foreach (Point2d p in outline)
                {
                    var model = new Point2d(x0 + (p.X - MinX) / sx, y1 - (p.Y - MinY) / sx);
                    if (!corners.Any(c => c.GetDistanceTo(model) < .001))
                        throw new InvalidDataException("STA plan çevresi ile RAD köşeleri uyuşmuyor.");
                }
                Scale = 100.0 / sx; // RAD metre; proje çizimleri cm.
                var remaining = Enumerable.Range(0, outline.Count / 2).ToList();
                Point2d current = outline[0]; Point2d first = current;
                while (remaining.Count > 0)
                {
                    Foundation.Add(new Point2d(x0 + (current.X - MinX) / sx, y1 - (current.Y - MinY) / sx));
                    int edge = remaining.FindIndex(i => outline[i * 2].GetDistanceTo(current) < .000001 || outline[i * 2 + 1].GetDistanceTo(current) < .000001);
                    if (edge < 0) throw new InvalidDataException("STA temel çevresi kapalı ve tekil değil.");
                    int k = remaining[edge]; remaining.RemoveAt(edge);
                    current = outline[k * 2].GetDistanceTo(current) < .000001 ? outline[k * 2 + 1] : outline[k * 2];
                    if (current.GetDistanceTo(first) < .000001 && remaining.Count > 0) throw new InvalidDataException("Birden fazla STA çevre döngüsü var.");
                }
                if (current.GetDistanceTo(first) >= .000001) throw new InvalidDataException("STA temel çevresi açık.");
                foreach (var selection in Selections)
                    selection.LabelPoint = new Point2d(x0 + (selection.LabelPoint.X - MinX) / sx,
                        y1 - (selection.LabelPoint.Y - MinY) / sx);
                var layers = new HashSet<string>(new[] { "AXIS2", "AXIS3", "AXIS4", "DIM_LAY", "MAT_FND2", "MAT_FND3", "COLUMN3", "COLUMN4", "CON_FND3", "PENC2", "PENC3", "REBAR" }, StringComparer.OrdinalIgnoreCase);
                double margin = sx * 3.0;
                foreach (ObjectId id in ms)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null || (!layers.Contains(e.Layer) && !(e is Hatch && e.Layer == "0"))) continue;
                    if (e is DBText tag && (tag.TextString ?? "").StartsWith("2#RADYE/", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        Extents3d box = e.GeometricExtents;
                        double x = (box.MinPoint.X + box.MaxPoint.X) * .5, y = (box.MinPoint.Y + box.MaxPoint.Y) * .5;
                        if (x >= MinX - margin && x <= MaxX + margin && y >= MinY - margin && y <= MaxY + margin) PlanIds.Add(id);
                    }
                    catch { throw new InvalidDataException("STA plan nesnesinin sınırı okunamadı; eksik plan aktarılmadı."); }
                }
            }
            EnsureUnchanged();
        }
        public void Dispose() { Drawing.Dispose(); }
    }
}
