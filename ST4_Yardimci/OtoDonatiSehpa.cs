using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ST4Yardimci
{
    /// <summary>
    /// OTODONATI «Sehpa» tiki: seçilen temel alanı (m², boşluklar düşülmez) ve radye etiketinin
    /// (RD-.. d=..) altındaki kot çiftinin üst değeri ana temel kotu olarak okunur; SEHPA.dwg detayı temel antetinin (SheetView) üstüne konur.
    /// Aynı antetteki alanlar toplanır, antet başına tek detay.
    /// </summary>
    internal static class OtoDonatiSehpa
    {
        private const string LayerKotYazi = "KOT YAZI (BEYKENT)";
        /// <summary>Antet üst kenarı ile sehpa detayı alt kenarı arası (cm).</summary>
        private const double AntetUstBoslukCm = 50.0;
        /// <summary>Antet bulunamazsa poligon üstünden boşluk (cm).</summary>
        private const double PoligonUstBoslukCm = 200.0;
        /// <summary>Alan içinde kot yoksa poligon kutusundan bu kadar dışarıdaki kotlar da sayılır (cm).</summary>
        private const double KotYakinPayCm = 100.0;

        /// <summary>Plan ID radye etiketi: «RD-01 d=70cm».</summary>
        private static readonly Regex RxRadyeEtiket = new Regex(
            @"^RD\s*-?\s*\w*\s+d\s*=\s*(\d+(?:[.,]\d+)?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex RxKot = new Regex(
            @"^[+\-]?\d+[.,]\d{1,3}$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        internal sealed class Alan
        {
            public double AlanM2;
            public double MinX, MinY, MaxX, MaxY;
            public double Cx, Cy;
            /// <summary>Okunan ana temel kotu (m); okunamadıysa null.</summary>
            public double? KotM;
            public string KotNot;
        }

        private sealed class Cerceve
        {
            public List<Point2d> Ring;
            public double MinX, MinY, MaxX, MaxY, Area;
            public bool IsOut;
        }

        /// <summary>Alan = dış poligonun tamamı; boşluklar sehpa adedinden düşülmez.</summary>
        public static Alan ReadAlan(Document doc, ObjectId polyId)
        {
            try
            {
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    if (!(tr.GetObject(polyId, OpenMode.ForRead) is Polyline pl) || pl.NumberOfVertices < 3)
                        return null;
                    List<Point2d> ring = Ring(pl);
                    double area = Math.Abs(SignedArea(ring));

                    var a = new Alan { AlanM2 = area / 10000.0 };
                    Bounds(ring, out a.MinX, out a.MinY, out a.MaxX, out a.MaxY);
                    a.Cx = 0.5 * (a.MinX + a.MaxX);
                    a.Cy = 0.5 * (a.MinY + a.MaxY);
                    a.KotM = ReadAnaKot(tr, doc.Database, ring, a, out a.KotNot);
                    tr.Commit();
                    return a;
                }
            }
            catch
            {
                return null;
            }
        }

        public static void Draw(Document doc, List<Alan> alanlar)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;
            SehpaDonatiSettings.EnsureLoaded();

            // Antet başına grupla (antet yoksa her alan kendi başına).
            List<Cerceve> frames;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                frames = ReadFrames(tr, db);
                tr.Commit();
            }

            var groups = new List<KeyValuePair<Cerceve, List<Alan>>>();
            foreach (Alan a in alanlar)
            {
                Cerceve host = SmallestContaining(frames, a.Cx, a.Cy, sheetViewOnly: true);
                KeyValuePair<Cerceve, List<Alan>> g = groups.Find(x => host != null && ReferenceEquals(x.Key, host));
                if (g.Value == null)
                {
                    g = new KeyValuePair<Cerceve, List<Alan>>(host, new List<Alan>());
                    groups.Add(g);
                }
                g.Value.Add(a);
            }

            SehpaDonatiTip prevTip = SehpaDonatiSettings.Tip;
            SehpaDonatiTip tip = OtoDonatiSettings.Tip == OtoDonatiTip.MantarDoseme
                ? SehpaDonatiTip.MantarDoseme
                : SehpaDonatiTip.RadyeTemel;
            SehpaDonatiProfile p = SehpaDonatiSettings.ProfileFor(tip);
            double sKal = p.KalinlikCm, sPas = p.PaspayiCm, sKot = p.UstKotM, sAlan = p.AlanM2;
            int sCap = p.CapMm;
            try
            {
                SehpaDonatiSettings.Tip = tip;
                p.KalinlikCm = OtoDonatiSettings.KalinlikCm;
                p.PaspayiCm = OtoDonatiSettings.PaspayiCm;
                p.CapMm = OtoDonatiSettings.SehpaCapMm;

                foreach (var g in groups)
                {
                    double alanM2 = 0;
                    double? kot = null;
                    string kotNot = null;
                    double minX = double.MaxValue, maxY = double.MinValue;
                    foreach (Alan a in g.Value)
                    {
                        alanM2 += a.AlanM2;
                        if (!kot.HasValue && a.KotM.HasValue) { kot = a.KotM; kotNot = a.KotNot; }
                        if (a.MinX < minX) minX = a.MinX;
                        if (a.MaxY > maxY) maxY = a.MaxY;
                    }
                    p.AlanM2 = alanM2;
                    p.UstKotM = kot ?? OtoDonatiSettings.UstKotM;

                    Point3d at;
                    if (g.Key != null)
                    {
                        // SheetViewOut dahil, alanı içeren en üst çerçevenin üstü.
                        double top = g.Key.MaxY;
                        foreach (Cerceve f in frames)
                            if (Contains(f, g.Key.MinX + 1, g.Key.MinY + 1) && f.MaxY > top) top = f.MaxY;
                        at = new Point3d(g.Key.MinX, top + AntetUstBoslukCm, 0);
                    }
                    else
                        at = new Point3d(minX, maxY + PoligonUstBoslukCm, 0);

                    int n;
                    string etiket;
                    using (doc.LockDocument())
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        n = SehpaDonatiRunner.InsertBottomLeft(tr, db, at, out etiket);
                        tr.Commit();
                    }

                    if (n < 0)
                    {
                        ed.WriteMessage("\nOTODONATICIZ sehpa: gömülü SEHPA.dwg bulunamadı.");
                        return;
                    }
                    ed.WriteMessage(
                        "\nOTODONATICIZ sehpa: alan {0:0.##} m², kot {1} ({2}), {3} — {4}.",
                        alanM2,
                        p.UstKotM.ToString("+0.00;-0.00;±0.00", CultureInfo.InvariantCulture),
                        kot.HasValue ? kotNot : "kot yazısı yok, formdaki üst kot",
                        etiket,
                        g.Key != null ? "temel antetinin üstüne" : "antet bulunamadı, alanın üstüne");
                }
            }
            catch (Exception ex)
            {
                try { ed.WriteMessage("\nOTODONATICIZ sehpa hata: {0}", ex.Message); } catch { }
            }
            finally
            {
                // SEHPADONATI formunun kendi değerleri korunur.
                p.KalinlikCm = sKal;
                p.PaspayiCm = sPas;
                p.UstKotM = sKot;
                p.AlanM2 = sAlan;
                p.CapMm = sCap;
                SehpaDonatiSettings.Tip = prevTip;
            }
        }

        private sealed class Yazi
        {
            public double MinX, MinY, MaxX, MaxY, Cx, Cy;
            public double Kot;
            public double DCm;
        }

        /// <summary>
        /// Radye etiketi (RD-.. d=..cm) altındaki kot çifti; üst değer = ana temel kotu.
        /// Temel çukuru kotları kullanılmaz. Etiket/kot yoksa null (formdaki üst kot).
        /// </summary>
        private static double? ReadAnaKot(Transaction tr, Database db, List<Point2d> ring, Alan a, out string not)
        {
            not = null;
            var etiketler = new List<Yazi>();
            var kotlar = new List<Yazi>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                Entity ent;
                try { ent = tr.GetObject(id, OpenMode.ForRead) as Entity; }
                catch { continue; }
                if (ent == null) continue;
                if (!(ent is DBText || ent is MText)) continue;

                string text = ent is DBText dt ? dt.TextString : ((MText)ent).Text;
                if (string.IsNullOrWhiteSpace(text)) continue;
                Extents3d e;
                try { e = ent.GeometricExtents; }
                catch { continue; }
                var y = new Yazi
                {
                    MinX = e.MinPoint.X, MinY = e.MinPoint.Y, MaxX = e.MaxPoint.X, MaxY = e.MaxPoint.Y,
                    Cx = 0.5 * (e.MinPoint.X + e.MaxPoint.X), Cy = 0.5 * (e.MinPoint.Y + e.MaxPoint.Y)
                };
                if (y.Cx < a.MinX - KotYakinPayCm || y.Cx > a.MaxX + KotYakinPayCm ||
                    y.Cy < a.MinY - KotYakinPayCm || y.Cy > a.MaxY + KotYakinPayCm) continue;

                if (string.Equals(ent.Layer, LayerKotYazi, StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseKotM(text, out double kot)) { y.Kot = kot; kotlar.Add(y); }
                    continue;
                }
                Match m = RxRadyeEtiket.Match(text.Trim());
                if (m.Success)
                {
                    double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y.DCm);
                    etiketler.Add(y);
                }
            }

            // Alan içindeki etiketler önce; birden çoksa alan merkezine en yakın.
            etiketler.Sort((p, q) =>
            {
                int ip = PointInRing(ring, p.Cx, p.Cy) ? 0 : 1;
                int iq = PointInRing(ring, q.Cx, q.Cy) ? 0 : 1;
                if (ip != iq) return ip.CompareTo(iq);
                return Dist2(p, a).CompareTo(Dist2(q, a));
            });

            foreach (Yazi lab in etiketler)
            {
                double h = Math.Max(1.0, lab.MaxY - lab.MinY);
                var alt = kotlar.FindAll(k =>
                    k.Cx >= lab.MinX - 3 * h && k.Cx <= lab.MaxX + h &&
                    k.Cy < lab.MinY && k.Cy >= lab.MinY - 8 * h);
                if (alt.Count == 0) continue;
                alt.Sort((p, q) => q.Cy.CompareTo(p.Cy));

                // Etiketin hemen altındaki çift: üst − alt ≈ d ise kesin; değilse en yakın iki kotun üstü.
                double ust = alt[0].Kot;
                if (alt.Count >= 2)
                {
                    ust = Math.Max(alt[0].Kot, alt[1].Kot);
                    for (int i = 0; i + 1 < alt.Count && lab.DCm > 0; i++)
                    {
                        double fark = Math.Abs(alt[i].Kot - alt[i + 1].Kot) * 100.0;
                        if (Math.Abs(fark - lab.DCm) < 1.0)
                        {
                            ust = Math.Max(alt[i].Kot, alt[i + 1].Kot);
                            break;
                        }
                    }
                }
                not = "radye etiketi altındaki kot";
                return ust;
            }
            return null;
        }

        private static double Dist2(Yazi y, Alan a)
        {
            double dx = y.Cx - a.Cx, dy = y.Cy - a.Cy;
            return dx * dx + dy * dy;
        }

        private static bool TryParseKotM(string raw, out double m)
        {
            m = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim().Replace(',', '.').Replace("±", "+").Replace("%%p", "+").Replace("%%P", "+");
            if (!RxKot.IsMatch(t)) return false;
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out m)) return false;
            return m >= -50.0 && m <= 50.0;
        }

        private static List<Cerceve> ReadFrames(Transaction tr, Database db)
        {
            var list = new List<Cerceve>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                Polyline pl;
                try { pl = tr.GetObject(id, OpenMode.ForRead) as Polyline; }
                catch { continue; }
                if (pl == null || pl.NumberOfVertices < 3) continue;
                if (string.IsNullOrEmpty(pl.Layer) ||
                    pl.Layer.IndexOf("SheetView", StringComparison.OrdinalIgnoreCase) < 0) continue;
                List<Point2d> ring = Ring(pl);
                Bounds(ring, out double x0, out double y0, out double x1, out double y1);
                if (x1 - x0 < 20 || y1 - y0 < 20) continue;
                list.Add(new Cerceve
                {
                    Ring = ring,
                    MinX = x0, MinY = y0, MaxX = x1, MaxY = y1,
                    Area = Math.Abs(SignedArea(ring)),
                    IsOut = pl.Layer.IndexOf("SheetViewOut", StringComparison.OrdinalIgnoreCase) >= 0
                });
            }
            return list;
        }

        private static Cerceve SmallestContaining(List<Cerceve> frames, double x, double y, bool sheetViewOnly)
        {
            Cerceve best = null;
            foreach (Cerceve f in frames)
            {
                if (sheetViewOnly && f.IsOut) continue;
                if (!Contains(f, x, y)) continue;
                if (best == null || f.Area < best.Area) best = f;
            }
            return best;
        }

        private static bool Contains(Cerceve f, double x, double y) =>
            x >= f.MinX && x <= f.MaxX && y >= f.MinY && y <= f.MaxY && PointInRing(f.Ring, x, y);

        private static List<Point2d> Ring(Polyline pl)
        {
            var ring = new List<Point2d>(pl.NumberOfVertices);
            for (int i = 0; i < pl.NumberOfVertices; i++)
                ring.Add(pl.GetPoint2dAt(i));
            return ring;
        }

        private static double SignedArea(List<Point2d> ring)
        {
            double s = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                Point2d a = ring[i];
                Point2d b = ring[(i + 1) % ring.Count];
                s += a.X * b.Y - b.X * a.Y;
            }
            return 0.5 * s;
        }

        private static void Bounds(List<Point2d> ring, out double x0, out double y0, out double x1, out double y1)
        {
            x0 = y0 = double.MaxValue;
            x1 = y1 = double.MinValue;
            foreach (Point2d p in ring)
            {
                if (p.X < x0) x0 = p.X;
                if (p.Y < y0) y0 = p.Y;
                if (p.X > x1) x1 = p.X;
                if (p.Y > y1) y1 = p.Y;
            }
        }

        private static bool PointInRing(List<Point2d> ring, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                Point2d a = ring[i], b = ring[j];
                if ((a.Y > y) != (b.Y > y) &&
                    x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }
    }
}
