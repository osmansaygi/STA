using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        /// <summary>
        /// Temel kesiti zemin çizgisi grobetonun alt (B-B'de dış) kenarını izleyen kırık çizgi. Grobeton altı her yerde aynı
        /// kottaysa false döner (çağıran eski tek düz çizgiyi çizer — düz projeler değişmez). Temeller arası boşlukta çizgi,
        /// iki komşu ucun derin olanının kotunda sürer; uçlarda <see cref="KesitZeminCizgiTemelHatUzantisiCm"/> uzatılır.
        /// </summary>
        private static bool TryDrawZeminProfilUnderGrobeton(Transaction tr, BlockTableRecord btr, Geometry merged, bool horizontalAlongX, bool mirrorElevationX)
        {
            // Yerel eksen: u = kesit boyunca, v = "aşağı" ölçüsü küçüldükçe dışa. A-A: (x, y); B-B aynalı: (y, -x); B-B: (y, x).
            Func<Coordinate, (double u, double v)> toUV = horizontalAlongX
                ? (c => (c.X, c.Y))
                : mirrorElevationX ? (c => (c.Y, -c.X)) : (Func<Coordinate, (double u, double v)>)(c => (c.Y, c.X));
            Func<double, double, Point2d> fromUV = horizontalAlongX
                ? ((u, v) => new Point2d(u, v))
                : mirrorElevationX ? ((u, v) => new Point2d(-v, u)) : (Func<double, double, Point2d>)((u, v) => new Point2d(v, u));

            // Alt kenar parçaları: dış halkaların "aşağı bakan" kenarları (u boyunca ilerleyen, iç taraf yukarıda).
            var segs = new List<(double u0, double v0, double u1, double v1)>();
            for (int gi = 0; gi < merged.NumGeometries; gi++)
            {
                if (!(merged.GetGeometryN(gi) is Polygon p)) continue;
                var c = p.ExteriorRing.Coordinates;
                for (int i = 0; i + 1 < c.Length; i++)
                {
                    var a = toUV(c[i]); var b = toUV(c[i + 1]);
                    if (Math.Abs(b.u - a.u) < 1e-6) continue;
                    double um = 0.5 * (a.u + b.u), vm = 0.5 * (a.v + b.v);
                    // Kenarın hemen altı poligon dışı, hemen üstü içiyse alt kenardır.
                    var below = fromUV(um, vm - 0.05); var above = fromUV(um, vm + 0.05);
                    bool icAlt = p.Contains(new Point(below.X, below.Y));
                    bool icUst = p.Contains(new Point(above.X, above.Y));
                    if (icAlt || !icUst) continue;
                    if (a.u <= b.u) segs.Add((a.u, a.v, b.u, b.v)); else segs.Add((b.u, b.v, a.u, a.v));
                }
            }
            if (segs.Count == 0) return false;
            double vMin = segs.Min(s => Math.Min(s.v0, s.v1)), vMax = segs.Max(s => Math.Max(s.v0, s.v1));
            if (vMax - vMin < 0.01) return false;

            // u'ya göre sırala; aynı u aralığında üst üste binen alt kenarlardan en alttakini al (iç kenarlar zaten elendi).
            segs.Sort((x, y) => x.u0.CompareTo(y.u0));
            var pts = new List<(double u, double v)>();
            void Ekle(double u, double v)
            {
                if (pts.Count > 0)
                {
                    var l = pts[pts.Count - 1];
                    if (Math.Abs(l.u - u) < 1e-6 && Math.Abs(l.v - v) < 1e-6) return;
                }
                pts.Add((u, v));
            }
            double ext = KesitZeminCizgiTemelHatUzantisiCm;
            Ekle(segs[0].u0 - ext, segs[0].v0);
            double uEnd = double.NegativeInfinity, vEnd = 0;
            foreach (var s in segs)
            {
                if (s.u1 <= uEnd + 1e-6) continue; // tamamen önceki kenarın altında kalan/çakışan
                double u0 = s.u0, v0 = s.v0;
                if (u0 < uEnd)
                {
                    double t = (uEnd - s.u0) / (s.u1 - s.u0);
                    u0 = uEnd; v0 = s.v0 + (s.v1 - s.v0) * t;
                }
                if (pts.Count > 1 && u0 > uEnd + 1e-6)
                {
                    // Temeller arası boşluk: derin ucun kotunda köprü.
                    double vb = Math.Min(vEnd, v0);
                    Ekle(uEnd, vb);
                    Ekle(u0, vb);
                }
                Ekle(u0, v0);
                Ekle(s.u1, s.v1);
                uEnd = s.u1; vEnd = s.v1;
            }
            Ekle(uEnd + ext, vEnd);
            if (pts.Count < 2) return false;

            var pl = new Polyline(pts.Count);
            for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, fromUV(pts[i].u, pts[i].v), 0, 0, 0);
            pl.Layer = LayerZemin;
            pl.LineWeight = LineWeight.LineWeight050;
            pl.ConstantWidth = 0;
            AppendEntity(tr, btr, pl);
            return true;
        }
    }
}
