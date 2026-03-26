using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Geometry;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// Plan: ISKELE YATAY cift paralel cizgi (±2.5 cm) → orta hat; kesit ile kesisim = dikme aksı.
    /// </summary>
    internal static class IskeleKesitSectionGeometry
    {
        public readonly struct Seg2
        {
            public Seg2(Point3d a, Point3d b)
            {
                A = a;
                B = b;
            }
            public Point3d A { get; }
            public Point3d B { get; }
        }

        private const double YatayCiftGenislikCm = 5.0;
        private const double DistTolCm = 1.2;
        private const double ParallelDotTol = 0.015;
        private const double MinOverlapCm = 15.0;

        /// <summary>
        /// Paralel ve ~5 cm aralıklı çiftleri eşleştirir; kesit ile orta hattın kesişimini döndürür.
        /// Eşleşmeyen tek çizgiler (nadir) doğrudan kesit ile kesilir.
        /// </summary>
        public static List<Point3d> ComputeStationsFromYatayMidlines(IReadOnlyList<Seg2> yataySegs, List<(Point3d a, Point3d b)> cutSegs)
        {
            var lines = yataySegs.Select(s => (a: s.A, b: s.B)).ToList();
            int n = lines.Count;
            var used = new bool[n];
            var pairs = new List<(int i, int j)>();

            for (int i = 0; i < n; i++)
            {
                if (used[i]) continue;
                var bestJ = -1;
                double bestScore = double.MaxValue;
                for (int j = i + 1; j < n; j++)
                {
                    if (used[j]) continue;
                    if (!TryPairAsYatayDouble(lines[i], lines[j], out double dist, out double overlap))
                        continue;
                    if (overlap < MinOverlapCm) continue;
                    double score = Math.Abs(dist - YatayCiftGenislikCm) * 1000 - overlap;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestJ = j;
                    }
                }
                if (bestJ >= 0)
                {
                    pairs.Add((i, bestJ));
                    used[i] = used[bestJ] = true;
                }
            }

            var stations = new List<Point3d>();
            foreach (var (i, j) in pairs)
            {
                if (TryMidlineIntersectCut(lines[i], lines[j], cutSegs, out Point3d p))
                    stations.Add(p);
            }

            for (int i = 0; i < n; i++)
            {
                if (used[i]) continue;
                if (TrySegIntersectCut(lines[i].a, lines[i].b, cutSegs, out Point3d q))
                    stations.Add(q);
            }

            return DedupeStations(stations, 0.5);
        }

        private static List<Point3d> DedupeStations(List<Point3d> pts, double tolCm)
        {
            if (pts.Count == 0) return pts;
            var sorted = pts.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            var o = new List<Point3d> { sorted[0] };
            for (int k = 1; k < sorted.Count; k++)
            {
                if (sorted[k].DistanceTo(o[o.Count - 1]) > tolCm)
                    o.Add(sorted[k]);
            }
            return o;
        }

        private static bool TryPairAsYatayDouble((Point3d a, Point3d b) L1, (Point3d a, Point3d b) L2, out double distCm, out double overlapCm)
        {
            distCm = 0;
            overlapCm = 0;
            var u1 = IskelePlanGeometry.VUnit(IskelePlanGeometry.VSub(L1.b, L1.a));
            var u2 = IskelePlanGeometry.VUnit(IskelePlanGeometry.VSub(L2.b, L2.a));
            double dot = Math.Abs(u1.X * u2.X + u1.Y * u2.Y);
            if (dot < 1.0 - ParallelDotTol) return false;

            var w = IskelePlanGeometry.VSub(L2.a, L1.a);
            distCm = Math.Abs(w.X * u1.Y - w.Y * u1.X);
            if (Math.Abs(distCm - YatayCiftGenislikCm) > DistTolCm) return false;

            overlapCm = ProjectedOverlap(L1, L2, u1);
            return true;
        }

        private static double ProjectedOverlap((Point3d a, Point3d b) L1, (Point3d a, Point3d b) L2, Point3d u1)
        {
            double t1a = 0;
            double t1b = L1.a.DistanceTo(L1.b);
            double t2a = Dot2(L2.a - L1.a, u1);
            double t2b = Dot2(L2.b - L1.a, u1);
            double lo1 = Math.Min(t1a, t1b);
            double hi1 = Math.Max(t1a, t1b);
            double lo2 = Math.Min(t2a, t2b);
            double hi2 = Math.Max(t2a, t2b);
            double lo = Math.Max(lo1, lo2);
            double hi = Math.Min(hi1, hi2);
            return Math.Max(0, hi - lo);
        }

        private static double Dot2(Vector3d v, Point3d u) => v.X * u.X + v.Y * u.Y;

        private static bool TryMidlineIntersectCut((Point3d a, Point3d b) L1, (Point3d a, Point3d b) L2, List<(Point3d a, Point3d b)> cutSegs, out Point3d hit)
        {
            hit = Point3d.Origin;
            var u = IskelePlanGeometry.VUnit(IskelePlanGeometry.VSub(L1.b, L1.a));
            var w = L2.a - L1.a;
            double s = w.X * u.Y - w.Y * u.X;
            var n = new Point3d(-u.Y, u.X, 0);
            var omid = IskelePlanGeometry.VAdd(L1.a, IskelePlanGeometry.VScale(n, s * 0.5));
            var far = IskelePlanGeometry.VAdd(omid, IskelePlanGeometry.VScale(u, 1e7));
            var near = IskelePlanGeometry.VAdd(omid, IskelePlanGeometry.VScale(u, -1e7));
            return TrySegIntersectCut(near, far, cutSegs, out hit);
        }

        private static bool TrySegIntersectCut(Point3d a1, Point3d a2, List<(Point3d a, Point3d b)> cutSegs, out Point3d hit)
        {
            foreach (var seg in cutSegs)
            {
                if (TryIntersectSeg2d(a1, a2, seg.a, seg.b, out hit))
                    return true;
            }
            hit = Point3d.Origin;
            return false;
        }

        private static bool TryIntersectSeg2d(Point3d a1, Point3d a2, Point3d b1, Point3d b2, out Point3d hit)
        {
            hit = Point3d.Origin;
            double x1 = a1.X, y1 = a1.Y, x2 = a2.X, y2 = a2.Y;
            double x3 = b1.X, y3 = b1.Y, x4 = b2.X, y4 = b2.Y;
            double d = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(d) < 1e-12) return false;
            double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / d;
            double u = -((x1 - x2) * (y1 - y3) - (y1 - y2) * (x1 - x3)) / d;
            if (t < -1e-9 || t > 1 + 1e-9 || u < -1e-9 || u > 1 + 1e-9) return false;
            hit = new Point3d(x1 + t * (x2 - x1), y1 + t * (y2 - y1), 0);
            return true;
        }
    }
}
