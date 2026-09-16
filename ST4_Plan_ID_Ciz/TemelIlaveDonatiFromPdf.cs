using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// STA TEMEL_PDF sayfa 1: ILAVE DONATILAR tablosu (çap/aralık, X/Y üst-alt, metre kutu, adet).
    /// CAD: Xcm = offsetX + Xm*100, Ycm = offsetY − Ym*100 (plan Y aksı ters).
    /// </summary>
    internal static class TemelIlaveDonatiFromPdf
    {
        public const string LayerYazi = "TEMEL ILAVE YAZI (BEYKENT)";

        public static string HeatLayer(Yon yon)
        {
            switch (yon)
            {
                case Yon.XAlt: return "TEMEL ILAVE X ALT (BEYKENT)";
                case Yon.YAlt: return "TEMEL ILAVE Y ALT (BEYKENT)";
                case Yon.XUst: return "TEMEL ILAVE X UST (BEYKENT)";
                case Yon.YUst: return "TEMEL ILAVE Y UST (BEYKENT)";
                default: return "TEMEL ILAVE DONATI (BEYKENT)";
            }
        }

        public static string BoxLayer(Yon yon)
        {
            switch (yon)
            {
                case Yon.XAlt: return "TEMEL ILAVE KUTU X ALT (BEYKENT)";
                case Yon.YAlt: return "TEMEL ILAVE KUTU Y ALT (BEYKENT)";
                case Yon.XUst: return "TEMEL ILAVE KUTU X UST (BEYKENT)";
                case Yon.YUst: return "TEMEL ILAVE KUTU Y UST (BEYKENT)";
                default: return "TEMEL ILAVE KUTU (BEYKENT)";
            }
        }

        public static short YonAci(Yon yon)
        {
            switch (yon)
            {
                case Yon.XAlt: return 1;
                case Yon.YAlt: return 5;
                case Yon.XUst: return 6;
                case Yon.YUst: return 4;
                default: return 1;
            }
        }

        public enum Yon
        {
            XAlt,
            XUst,
            YAlt,
            YUst
        }

        private static readonly Regex RowRx = new Regex(
            @"(\d{2})\s*/\s*(\d{2,3})\s*([XY])\s*(ust|alt).*?X=\(\s*(-?[\d.]+)\D+(-?[\d.]+)\s*\).*?Y=\(\s*(-?[\d.]+)\D+(-?[\d.]+)\s*\).*?(\d{1,3})\s+\S*?\s*([\d.]+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        public static int Draw(
            string pdfPath,
            Yon yon,
            double offsetX,
            double offsetY,
            Envelope temelEnv,
            Database db,
            Editor ed,
            Transaction tr,
            BlockTableRecord btr,
            int titleIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                ed?.WriteMessage("\nTEMELDONATI: PDF bulunamadi.");
                return 0;
            }

            List<IlaveRow> all = Parse(pdfPath);
            if (all.Count == 0)
            {
                ed?.WriteMessage("\nTEMELDONATI: PDF'de ILAVE DONATILAR satiri okunamadi.");
                return 0;
            }

            var rows = new List<IlaveRow>();
            foreach (var r in all)
            {
                if (Matches(r, yon)) rows.Add(r);
            }

            ed?.WriteMessage("\nTEMELDONATI PDF: {0} satir, {1} = {2} kutu ({3}).",
                all.Count, YonAd(yon), rows.Count, Path.GetFileName(pdfPath));

            EnsureLayer(tr, db, BoxLayer(yon), YonAci(yon), LineWeight.LineWeight020);
            EnsureLayer(tr, db, LayerYazi, 3, LineWeight.LineWeight020);

            if (temelEnv != null)
            {
                var baslik = new DBText
                {
                    Layer = LayerYazi,
                    Height = 20.0,
                    WidthFactor = 0.85,
                    TextString = YonAd(yon) + " ilave donati (PDF)",
                    Position = new Point3d(temelEnv.MinX, temelEnv.MaxY + 40.0, 0)
                };
                btr.AppendEntity(baslik);
                tr.AddNewlyCreatedDBObject(baslik, true);
            }

            int n = 0;
            foreach (var r in rows)
            {
                StaMToCad(r.X0, r.Y0, offsetX, offsetY, out double x0, out double y0);
                StaMToCad(r.X1, r.Y1, offsetX, offsetY, out double x1, out double y1);
                double xa = Math.Min(x0, x1), xb = Math.Max(x0, x1);
                double ya = Math.Min(y0, y1), yb = Math.Max(y0, y1);
                if (xb - xa < 8 || yb - ya < 8) continue;

                var pl = new Polyline { Layer = BoxLayer(yon), Closed = true };
                pl.AddVertexAt(0, new Point2d(xa, ya), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(xb, ya), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(xb, yb), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(xa, yb), 0, 0, 0);
                btr.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);

                string label = r.Adet + "Ø" + r.Cap + "/" + r.Aralik;
                var txt = new DBText
                {
                    Layer = LayerYazi,
                    Height = 20.0,
                    WidthFactor = 0.85,
                    TextString = label,
                    Position = new Point3d(xa, yb + 4.0, 0)
                };
                btr.AppendEntity(txt);
                tr.AddNewlyCreatedDBObject(txt, true);
                n++;
                ed?.WriteMessage("\nTEMELDONATI PDF: {0}  X=({1:0.00}-{2:0.00}) Y=({3:0.00}-{4:0.00}) m",
                    label, r.X0, r.X1, r.Y0, r.Y1);
            }

            ed?.WriteMessage("\nTEMELDONATI: {0} kutu+yazi cizildi ({1} / {2}).",
                n, BoxLayer(yon), LayerYazi);
            return n;
        }

        public static List<TemelIlaveDonatiFromPng.StaCadBox> CollectCadBoxes(
            string pdfPath, Yon yon, double offsetX, double offsetY)
        {
            var list = new List<TemelIlaveDonatiFromPng.StaCadBox>();
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath)) return list;
            foreach (var r in Parse(pdfPath))
            {
                if (!Matches(r, yon)) continue;
                StaMToCad(r.X0, r.Y0, offsetX, offsetY, out double x0, out double y0);
                StaMToCad(r.X1, r.Y1, offsetX, offsetY, out double x1, out double y1);
                double xa = Math.Min(x0, x1), xb = Math.Max(x0, x1);
                double ya = Math.Min(y0, y1), yb = Math.Max(y0, y1);
                if (xb - xa < 8 || yb - ya < 8) continue;
                list.Add(new TemelIlaveDonatiFromPng.StaCadBox { MinX = xa, MaxX = xb, MinY = ya, MaxY = yb });
            }
            return list;
        }

        private static bool Matches(IlaveRow r, Yon yon)
        {
            switch (yon)
            {
                case Yon.XAlt: return r.Eksen == 'X' && !r.Ust;
                case Yon.XUst: return r.Eksen == 'X' && r.Ust;
                case Yon.YAlt: return r.Eksen == 'Y' && !r.Ust;
                case Yon.YUst: return r.Eksen == 'Y' && r.Ust;
                default: return false;
            }
        }

        private static string YonAd(Yon yon)
        {
            switch (yon)
            {
                case Yon.XAlt: return "X yonu Alt";
                case Yon.XUst: return "X yonu Ust";
                case Yon.YAlt: return "Y yonu Alt";
                case Yon.YUst: return "Y yonu Ust";
                default: return yon.ToString();
            }
        }

        public static Bitmap TryLoadHeatBitmap(string pdfPath, Yon yon, Editor ed)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath)) return null;
            try
            {
                using (var doc = PdfDocument.Open(pdfPath))
                {
                    IPdfImage best = null;
                    int pageNo = 0;
                    double bestGap = double.MaxValue;
                    for (int pi = 1; pi <= doc.NumberOfPages; pi++)
                    {
                        Page page = doc.GetPage(pi);
                        foreach (double titleY in FindTitleYs(page, yon))
                        {
                            foreach (var img in page.GetImages())
                            {
                                if (img == null || img.WidthInSamples < 400 || img.HeightInSamples < 400) continue;
                                double top = img.Bounds.Top;
                                if (top > titleY + 12) continue;
                                if (img.Bounds.Bottom > titleY + 4) continue;
                                double gap = titleY - top;
                                if (gap < -8) continue;
                                if (gap < bestGap)
                                {
                                    bestGap = gap;
                                    best = img;
                                    pageNo = pi;
                                }
                            }
                        }
                    }
                    if (best == null)
                    {
                        ed?.WriteMessage("\nTEMELDONATI: '{0} Ek donati alani grafigi' basligi altinda gomulu resim yok.", YonAd(yon));
                        return null;
                    }
                    byte[] bytes = null;
                    if (!best.TryGetPng(out bytes) || bytes == null || bytes.Length < 32)
                        bytes = best.RawMemory.ToArray();
                    if (bytes == null || bytes.Length < 32) return null;
                    using (var ms = new MemoryStream(bytes, writable: false))
                    using (var tmp = new Bitmap(ms))
                    {
                        ed?.WriteMessage("\nTEMELDONATI isi izi PDF: sayfa {0} baslik alti {1} ({2}x{3}).",
                            pageNo, YonAd(yon), tmp.Width, tmp.Height);
                        return new Bitmap(tmp);
                    }
                }
            }
            catch (Exception ex)
            {
                ed?.WriteMessage("\nTEMELDONATI: PDF isi haritasi okunamadi ({0}).", ex.Message);
                return null;
            }
        }

        private static List<double> FindTitleYs(Page page, Yon yon)
        {
            var ys = new List<double>();
            if (page?.Letters == null) return ys;
            var buckets = new Dictionary<int, List<Letter>>();
            foreach (var L in page.Letters)
            {
                int key = (int)Math.Round(L.GlyphRectangle.Bottom * 2.0);
                List<Letter> list;
                if (!buckets.TryGetValue(key, out list))
                {
                    list = new List<Letter>();
                    buckets[key] = list;
                }
                list.Add(L);
            }
            foreach (var kv in buckets)
            {
                kv.Value.Sort((a, b) => a.GlyphRectangle.Left.CompareTo(b.GlyphRectangle.Left));
                var sb = new StringBuilder();
                foreach (var L in kv.Value) sb.Append(L.Value);
                if (!LineMatchesYon(sb.ToString(), yon)) continue;
                double y = 0;
                foreach (var L in kv.Value) y += L.GlyphRectangle.Bottom;
                ys.Add(y / kv.Value.Count);
            }
            return ys;
        }

        private static bool LineMatchesYon(string line, Yon yon)
        {
            if (string.IsNullOrEmpty(line)) return false;
            string n = NormTitle(line);
            if (n.IndexOf("donat", StringComparison.Ordinal) < 0) return false;
            bool alt = n.IndexOf("alt", StringComparison.Ordinal) >= 0;
            bool ust = n.IndexOf("ust", StringComparison.Ordinal) >= 0;
            bool x = n.StartsWith("x", StringComparison.Ordinal) || n.IndexOf("x onu", StringComparison.Ordinal) >= 0
                     || n.IndexOf("xyon", StringComparison.Ordinal) >= 0 || n.IndexOf("x yon", StringComparison.Ordinal) >= 0;
            bool y = n.StartsWith("y", StringComparison.Ordinal) || n.IndexOf("y onu", StringComparison.Ordinal) >= 0
                     || n.IndexOf("yyon", StringComparison.Ordinal) >= 0 || n.IndexOf("y yon", StringComparison.Ordinal) >= 0;
            if (x && y)
            {
                int ix = n.IndexOf('x');
                int iy = n.IndexOf('y');
                x = ix >= 0 && (iy < 0 || ix <= iy);
                y = !x;
            }
            switch (yon)
            {
                case Yon.XAlt: return x && alt && !ust;
                case Yon.XUst: return x && ust;
                case Yon.YAlt: return y && alt && !ust;
                case Yon.YUst: return y && ust;
                default: return false;
            }
        }

        private static string NormTitle(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s.ToLowerInvariant())
            {
                if (ch == 'ı' || ch == 'i' || ch == 'İ') sb.Append('i');
                else if (ch == 'ğ' || ch == 'g') sb.Append('g');
                else if (ch == 'ü' || ch == 'u') sb.Append('u');
                else if (ch == 'ö' || ch == 'o') sb.Append('o');
                else if (ch == 'ş' || ch == 's') sb.Append('s');
                else if (ch == 'ç' || ch == 'c') sb.Append('c');
                else if (char.IsLetterOrDigit(ch) || ch == ' ') sb.Append(ch);
            }
            return sb.ToString();
        }

        public static string FindHeatPng(string pdfPath, Yon yon)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath)) return null;
            string dir = Path.GetDirectoryName(pdfPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            string[] names;
            switch (yon)
            {
                case Yon.XAlt: names = new[] { "x_alt.png", "X_ALT.png", "X_alt.png" }; break;
                case Yon.XUst: names = new[] { "X_UST.png", "x_ust.png", "X_ust.png" }; break;
                case Yon.YAlt: names = new[] { "Y_ALT.png", "y_alt.png", "Y_alt.png" }; break;
                case Yon.YUst: names = new[] { "Y_UST.png", "y_ust.png", "Y_ust.png" }; break;
                default: names = Array.Empty<string>(); break;
            }
            foreach (string n in names)
            {
                string p = Path.Combine(dir, n);
                if (File.Exists(p)) return p;
            }
            try
            {
                foreach (string p in Directory.GetFiles(dir, "*.png"))
                {
                    string fn = Path.GetFileNameWithoutExtension(p).Replace(" ", "").Replace("_", "").ToUpperInvariant();
                    if (yon == Yon.XAlt && fn.Contains("XALT")) return p;
                    if (yon == Yon.XUst && fn.Contains("XUST")) return p;
                    if (yon == Yon.YAlt && fn.Contains("YALT")) return p;
                    if (yon == Yon.YUst && fn.Contains("YUST")) return p;
                }
            }
            catch { }
            return null;
        }

        private static void StaMToCad(double xm, double ym, double offsetX, double offsetY, out double x, out double y)
        {
            x = offsetX + xm * 100.0;
            y = offsetY - ym * 100.0;
        }

        private static List<IlaveRow> Parse(string pdfPath)
        {
            var list = new List<IlaveRow>();
            using (var doc = PdfDocument.Open(pdfPath))
            {
                var sb = new StringBuilder();
                foreach (Page page in doc.GetPages())
                {
                    foreach (var letter in page.Letters)
                        sb.Append(letter.Value);
                    sb.Append('\n');
                }
                string text = sb.ToString();
                foreach (Match m in RowRx.Matches(text))
                {
                    list.Add(new IlaveRow
                    {
                        Cap = m.Groups[1].Value,
                        Aralik = m.Groups[2].Value,
                        Eksen = char.ToUpperInvariant(m.Groups[3].Value[0]),
                        Ust = m.Groups[4].Value.StartsWith("u", StringComparison.OrdinalIgnoreCase),
                        X0 = ParseD(m.Groups[5].Value),
                        X1 = ParseD(m.Groups[6].Value),
                        Y0 = ParseD(m.Groups[7].Value),
                        Y1 = ParseD(m.Groups[8].Value),
                        Adet = int.Parse(m.Groups[9].Value)
                    });
                }
            }
            return list;
        }

        private static double ParseD(string s)
        {
            double v;
            double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
            return v;
        }

        private struct IlaveRow
        {
            public string Cap, Aralik;
            public char Eksen;
            public bool Ust;
            public double X0, X1, Y0, Y1;
            public int Adet;
        }

        private static void EnsureLayer(Transaction tr, Database db, string name, short aci, LineWeight lw)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            {
                Name = name,
                Color = AcColor.FromColorIndex(ColorMethod.ByAci, aci),
                LineWeight = lw
            };
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has("Continuous"))
                rec.LinetypeObjectId = ltt["Continuous"];
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }
    }
}
