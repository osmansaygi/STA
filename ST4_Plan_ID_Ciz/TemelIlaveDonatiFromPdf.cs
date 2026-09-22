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
        private static bool IsXYon(Yon yon) => yon == Yon.XAlt || yon == Yon.XUst;

        public static AcColor BoxColor(Yon yon) =>
            IsXYon(yon) ? AcColor.FromRgb(255, 176, 176) : AcColor.FromRgb(176, 204, 236);

        public static AcColor HeatColor(Yon yon) =>
            IsXYon(yon) ? AcColor.FromRgb(196, 72, 72) : AcColor.FromRgb(64, 112, 176);

        public static string HeatLayer(Yon yon) =>
            IsXYon(yon) ? "TEMEL ILAVE X (BEYKENT)" : "TEMEL ILAVE Y (BEYKENT)";

        public static string BoxLayer(Yon yon) =>
            IsXYon(yon) ? "TEMEL ILAVE KUTU X (BEYKENT)" : "TEMEL ILAVE KUTU Y (BEYKENT)";

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

            ed?.WriteMessage("\nTEMELDONATI PDF: {0} satir, {1} = {2} kutu.",
                all.Count, YonAd(yon), rows.Count);

            EnsureLayer(tr, db, BoxLayer(yon), BoxColor(yon), LineWeight.LineWeight020);

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
                    Layer = BoxLayer(yon),
                    Height = 20.0,
                    WidthFactor = 0.85,
                    TextString = label,
                    Position = new Point3d(xa, yb + 4.0, 0)
                };
                btr.AppendEntity(txt);
                tr.AddNewlyCreatedDBObject(txt, true);
                n++;
            }

            ed?.WriteMessage("\nTEMELDONATI: {0} kutu+yazi cizildi ({1}).", n, BoxLayer(yon));
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
                                if (img.Bounds.Bottom > titleY + 4) continue;
                                // Başlık ile grafiğin üst kenarı arasındaki boşluk sürücüye göre
                                // değişir (doPDF sayfayı küçültüp kaydırıyor); en yakın grafik seçilir.
                                double gap = Math.Abs(titleY - top);
                                if (titleY - top < -20) continue;
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

        /// <summary>
        /// Başlık satırları: harf taban çizgisi (baseline) + tolerans ile kümelenir.
        /// Glyph kutusunun altı kullanılamaz; "y/ğ" gibi alt uzantılı harfler ayrı düşer.
        /// Sabit ızgara da kullanılamaz: doPDF aynı satırın harflerini ~0,2 pt kaydırarak
        /// yazdığı için "X yönü Üst ... grafiği" başlığı ikiye bölünüp bulunamıyordu
        /// (Microsoft Print to PDF tek taban çizgisi yazdığı için sorun çıkmıyordu).
        /// </summary>
        private static List<double> FindTitleYs(Page page, Yon yon)
        {
            var ys = new List<double>();
            if (page?.Letters == null) return ys;
            var letters = new List<Letter>(page.Letters);
            letters.Sort((a, b) => LetterBaselineY(b).CompareTo(LetterBaselineY(a)));
            int i = 0;
            while (i < letters.Count)
            {
                double yTop = LetterBaselineY(letters[i]);
                var line = new List<Letter>();
                while (i < letters.Count && yTop - LetterBaselineY(letters[i]) <= TitleBaselineTolPt)
                {
                    line.Add(letters[i]);
                    i++;
                }
                line.Sort((a, b) => a.GlyphRectangle.Left.CompareTo(b.GlyphRectangle.Left));
                var sb = new StringBuilder();
                double y = 0;
                foreach (var L in line)
                {
                    sb.Append(L.Value);
                    y += LetterBaselineY(L);
                }
                if (!LineMatchesYon(sb.ToString(), yon)) continue;
                ys.Add(y / line.Count);
            }
            return ys;
        }

        private const double TitleBaselineTolPt = 1.2;

        private static double LetterBaselineY(Letter L)
        {
            double y = L.StartBaseLine.Y;
            return y > 0 ? y : L.GlyphRectangle.Bottom;
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

        private static string _parseCachePath;
        private static List<IlaveRow> _parseCache;

        private static List<IlaveRow> Parse(string pdfPath)
        {
            if (_parseCache != null && string.Equals(_parseCachePath, pdfPath, StringComparison.OrdinalIgnoreCase))
                return _parseCache;
            var list = ParseUncached(pdfPath);
            _parseCachePath = pdfPath;
            _parseCache = list;
            return list;
        }

        private static List<IlaveRow> ParseUncached(string pdfPath)
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

        private static void EnsureLayer(Transaction tr, Database db, string name, AcColor color, LineWeight lw)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name))
            {
                var existing = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
                if (color != null)
                    existing.Color = color;
                existing.LineWeight = lw;
                return;
            }
            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            {
                Name = name,
                Color = color,
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
