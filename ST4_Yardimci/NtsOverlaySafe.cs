using System;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Precision;

namespace ST4Yardimci
{
    /// <summary>
    /// OverlayNG / Buffer(0) yığın taşması AutoCAD sürecini düşürür (catch yakalamaz).
    /// Geçerli geometri aynen döner — çizim görünümü değişmez.
    /// </summary>
    internal static class NtsOverlaySafe
    {
        public static Geometry PrepareForOverlay(Geometry g)
        {
            if (g == null || g.IsEmpty) return g;
            try
            {
                if (g.IsValid) return g;
            }
            catch { }

            var reduced = ReducePrecision(g, 100.0);
            if (reduced != null && !reduced.IsEmpty)
            {
                try { if (reduced.IsValid) return reduced; } catch { }
                g = reduced;
            }

            try
            {
                var fixedG = GeometryFixer.Fix(g);
                if (fixedG != null && !fixedG.IsEmpty) return fixedG;
            }
            catch { }

            return g;
        }

        public static Geometry FixIfInvalid(Geometry g) => PrepareForOverlay(g);

        public static Polygon LargestPolygon(Geometry g)
        {
            if (g == null || g.IsEmpty) return null;
            if (g is Polygon p) return p;
            Polygon best = null;
            for (int i = 0; i < g.NumGeometries; i++)
            {
                if (g.GetGeometryN(i) is Polygon pi && (best == null || pi.Area > best.Area))
                    best = pi;
            }
            return best;
        }

        public static Polygon FixToPolygon(Geometry g)
        {
            if (g == null || g.IsEmpty) return null;
            return LargestPolygon(PrepareForOverlay(g));
        }

        public static Geometry ReducePrecision(Geometry geom, double scale)
        {
            if (geom == null || geom.IsEmpty) return geom;
            try
            {
                var pm = new PrecisionModel(scale);
                return GeometryPrecisionReducer.Reduce(geom, pm);
            }
            catch
            {
                return null;
            }
        }
    }
}
