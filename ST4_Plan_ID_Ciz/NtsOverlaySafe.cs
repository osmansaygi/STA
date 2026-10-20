using System;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Precision;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// OverlayNG / Buffer(0) yığın taşması AutoCAD sürecini düşürür (catch yakalamaz).
    /// Geçerli geometri aynen döner — çizim görünümü değişmez.
    /// </summary>
    internal static class NtsOverlaySafe
    {
        /// <summary>
        /// Geçersiz poligonda Buffer(0) yerine: 0,01 cm ızgara, sonra GeometryFixer.
        /// </summary>
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

        /// <summary>Yalnız geçersizse düzelt; geçerliyse aynı örnek.</summary>
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

        /// <summary>Geçersiz poligon → PrepareForOverlay + en büyük Polygon.</summary>
        public static Polygon FixToPolygon(Geometry g)
        {
            if (g == null || g.IsEmpty) return null;
            var fixedG = PrepareForOverlay(g);
            return LargestPolygon(fixedG);
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

        /// <summary>Union öncesi girdileri hazırla; geçerli parçalar değişmez.</summary>
        public static Geometry CascadedUnionSafe(System.Collections.Generic.IReadOnlyList<Geometry> geoms)
        {
            if (geoms == null || geoms.Count == 0) return null;
            var prep = new System.Collections.Generic.List<Geometry>(geoms.Count);
            foreach (var g in geoms)
            {
                if (g == null || g.IsEmpty) continue;
                var p = PrepareForOverlay(g);
                if (p != null && !p.IsEmpty) prep.Add(p);
            }
            if (prep.Count == 0) return null;
            if (prep.Count == 1) return prep[0];
            try
            {
                return NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(prep);
            }
            catch
            {
                foreach (double scale in new[] { 100.0, 10.0, 1.0 })
                {
                    try
                    {
                        var work = new System.Collections.Generic.List<Geometry>();
                        foreach (var g in prep)
                        {
                            var r = ReducePrecision(g, scale);
                            if (r != null && !r.IsEmpty) work.Add(r);
                        }
                        if (work.Count == 0) continue;
                        if (work.Count == 1) return work[0];
                        return NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(work);
                    }
                    catch { }
                }
                return prep[0];
            }
        }
    }
}
