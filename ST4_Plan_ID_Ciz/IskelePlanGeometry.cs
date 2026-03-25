using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// iskele_plan.lsp geometri yardımcıları (ISKELEPLAN ile uyumlu).
    /// </summary>
    internal static class IskelePlanGeometry
    {
        public static List<Point3d> LwPolyVertices(Polyline pl)
        {
            var pts = new List<Point3d>();
            int n = pl.NumberOfVertices;
            for (int i = 0; i < n; i++)
            {
                var p2 = pl.GetPoint2dAt(i);
                pts.Add(new Point3d(p2.X, p2.Y, 0));
            }
            return pts;
        }

        public static Point3d CentroidAvg(IReadOnlyList<Point3d> pts)
        {
            if (pts == null || pts.Count == 0) return Point3d.Origin;
            double sx = 0, sy = 0;
            foreach (var p in pts)
            {
                sx += p.X;
                sy += p.Y;
            }
            double n = pts.Count;
            return new Point3d(sx / n, sy / n, 0);
        }

        public static Point3d VAdd(Point3d a, Point3d b) => new Point3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Point3d VSub(Point3d a, Point3d b) => new Point3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Point3d VScale(Point3d v, double s) => new Point3d(v.X * s, v.Y * s, v.Z * s);

        public static Point3d VUnit(Point3d v)
        {
            double l = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (l < 1e-12) return new Point3d(1, 0, 0);
            return new Point3d(v.X / l, v.Y / l, v.Z / l);
        }

        public static double SignedArea(IReadOnlyList<Point3d> pts)
        {
            int n = pts.Count;
            double sa = 0;
            for (int i = 0; i < n; i++)
            {
                var p1 = pts[i];
                var p2 = pts[(i + 1) % n];
                sa += p1.X * p2.Y - p2.X * p1.Y;
            }
            return sa * 0.5;
        }

        public static bool TooCloseToAny(Point3d pt, IReadOnlyList<Point3d> ptList, double minDist)
        {
            foreach (var v in ptList)
                if (pt.DistanceTo(v) < minDist) return true;
            return false;
        }

        public static List<Point3d> RemoveCollinear(IReadOnlyList<Point3d> pts)
        {
            int n = pts.Count;
            if (n <= 3) return pts.ToList();
            var result = new List<Point3d>();
            for (int i = 0; i < n; i++)
            {
                var p1 = pts[(i + n - 1) % n];
                var p2 = pts[i];
                var p3 = pts[(i + 1) % n];
                double d1x = p2.X - p1.X, d1y = p2.Y - p1.Y;
                double d2x = p3.X - p2.X, d2y = p3.Y - p2.Y;
                double len1 = Math.Sqrt(d1x * d1x + d1y * d1y);
                double len2 = Math.Sqrt(d2x * d2x + d2y * d2y);
                double cross, dot;
                if (len1 > 0.001 && len2 > 0.001)
                {
                    cross = Math.Abs((d1x / len1) * (d2y / len2) - (d1y / len1) * (d2x / len2));
                    dot = (d1x / len1) * (d2x / len2) + (d1y / len1) * (d2y / len2);
                }
                else
                {
                    cross = 999;
                    dot = 0;
                }
                if (!(cross < 0.05 && dot > 0))
                    result.Add(p2);
            }
            return result.Count < 3 ? pts.ToList() : result;
        }

        public static Point3d? PerpFootOnSeg(Point3d pt, Point3d p1, Point3d p2)
        {
            double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-12) return null;
            double t1 = ((pt.X - p1.X) * dx + (pt.Y - p1.Y) * dy) / len2;
            if (t1 < 0 || t1 > 1) return null;
            return new Point3d(p1.X + t1 * dx, p1.Y + t1 * dy, 0);
        }

        public static Point3d? ClosestPerpOnPoly(Point3d pt, IReadOnlyList<Point3d> polyPts)
        {
            int n = polyPts.Count;
            double bestDist = 1e30;
            Point3d? bestFoot = null;
            for (int i = 0; i < n; i++)
            {
                var p1 = polyPts[i];
                var p2 = polyPts[(i + 1) % n];
                var foot = PerpFootOnSeg(pt, p1, p2);
                if (foot.HasValue)
                {
                    double d = pt.DistanceTo(foot.Value);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestFoot = foot;
                    }
                }
            }
            return bestFoot;
        }

        public static int FindSegIdx(Point3d pt, IReadOnlyList<Point3d> polyPts)
        {
            int n = polyPts.Count;
            for (int i = 0; i < n; i++)
            {
                var p1 = polyPts[i];
                var p2 = polyPts[(i + 1) % n];
                if (PtOnSeg(pt, p1, p2, 1.0)) return i;
            }
            return 0;
        }

        public static bool PtOnSeg(Point3d pt, Point3d p1, Point3d p2, double tol)
        {
            double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
            double t1 = dx * dx + dy * dy;
            if (t1 < 1e-12) return false;
            double proj = ((pt.X - p1.X) * dx + (pt.Y - p1.Y) * dy) / t1;
            if (proj < -0.01 || proj > 1.01) return false;
            var q = new Point3d(p1.X + proj * dx, p1.Y + proj * dy, 0);
            return pt.DistanceTo(q) < tol;
        }

        public static double DistAlongPoly(Point3d pt, IReadOnlyList<Point3d> polyPts)
        {
            int n = polyPts.Count;
            double cumul = 0;
            for (int i = 0; i < n; i++)
            {
                var p1 = polyPts[i];
                var p2 = polyPts[(i + 1) % n];
                if (PtOnSeg(pt, p1, p2, 2.0))
                {
                    cumul += p1.DistanceTo(pt);
                    return cumul;
                }
                cumul += p1.DistanceTo(p2);
            }
            return cumul;
        }

        public static double TotalPolyLen(IReadOnlyList<Point3d> polyPts)
        {
            int n = polyPts.Count;
            double len = 0;
            for (int i = 0; i < n; i++)
                len += polyPts[i].DistanceTo(polyPts[(i + 1) % n]);
            return len;
        }

        public static Point3d PtAtDistOnPoly(double dist, IReadOnlyList<Point3d> polyPts)
        {
            int n = polyPts.Count;
            double cumul = 0;
            for (int i = 0; i < n; i++)
            {
                var p1 = polyPts[i];
                var p2 = polyPts[(i + 1) % n];
                double segL = p1.DistanceTo(p2);
                if (dist <= cumul + segL + 0.01)
                {
                    double rem2 = dist - cumul;
                    if (rem2 < 0) rem2 = 0;
                    if (rem2 > segL) rem2 = segL;
                    var dir = VUnit(VSub(p2, p1));
                    return VAdd(p1, VScale(dir, rem2));
                }
                cumul += segL;
            }
            return polyPts[0];
        }

        public static Point3d OutwardNormalAt(Point3d pt, IReadOnlyList<Point3d> polyPts, Point3d cen)
        {
            double sa = SignedArea(polyPts);
            int n = polyPts.Count;
            Point3d? nrm = null;
            for (int i = 0; i < n; i++)
            {
                var p1 = polyPts[i];
                var p2 = polyPts[(i + 1) % n];
                if (PtOnSeg(pt, p1, p2, 2.0))
                {
                    // Sol normal (-dy, dx)
                    nrm = VUnit(new Point3d(p1.Y - p2.Y, p2.X - p1.X, 0));
                    if (sa > 0) nrm = VScale(nrm.Value, -1);
                    break;
                }
            }
            if (!nrm.HasValue)
                nrm = VUnit(VSub(pt, cen));
            return nrm.Value;
        }

        public static List<Point3d> CircleSegTangent(Point3d center, double radius, Point3d p1, Point3d p2)
        {
            var pts = new List<Point3d>();
            double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
            double fx = p1.X - center.X, fy = p1.Y - center.Y;
            double a = dx * dx + dy * dy;
            if (a < 1e-12) a = 1e-12;
            double b = 2 * (fx * dx + fy * dy);
            double c = fx * fx + fy * fy - radius * radius;
            double disc = b * b - 4 * a * c;
            if (disc >= -100 * a && disc <= 100 * a)
            {
                double t1 = -b / (2 * a);
                if (t1 >= -0.01 && t1 <= 1.01)
                {
                    t1 = Math.Max(0, Math.Min(1, t1));
                    pts.Add(new Point3d(p1.X + t1 * dx, p1.Y + t1 * dy, 0));
                }
            }
            return pts;
        }

        public static List<Point3d> CirclePolyTangents(Point3d center, double radius, IReadOnlyList<Point3d> polyPts)
        {
            var all = new List<Point3d>();
            int n = polyPts.Count;
            for (int i = 0; i < n; i++)
            {
                var p1 = polyPts[i];
                var p2 = polyPts[(i + 1) % n];
                all.AddRange(CircleSegTangent(center, radius, p1, p2));
            }
            return all;
        }

        public static bool CanSkipTangent(Point3d pOut, IReadOnlyList<Point3d> polyPts, IReadOnlyList<Point3d> allVertexPts)
        {
            if (!TooCloseToAny(pOut, allVertexPts, 20.0)) return false;
            double dPt = DistAlongPoly(pOut, polyPts);
            double totalLen = TotalPolyLen(polyPts);
            var vDists = polyPts.Select(v => DistAlongPoly(v, polyPts)).OrderBy(d => d).ToList();
            int n = vDists.Count;
            double? prevD = null;
            foreach (var d in vDists)
            {
                if (d < dPt - 0.5) prevD = d;
            }
            if (prevD == null) prevD = vDists[n - 1];
            double? nextD = null;
            foreach (var d in vDists)
            {
                if (d > dPt + 0.5)
                {
                    nextD = d;
                    break;
                }
            }
            if (nextD == null) nextD = vDists[0];
            double gapWithout = nextD.Value > prevD.Value
                ? nextD.Value - prevD.Value
                : (totalLen - prevD.Value) + nextD.Value;
            return gapWithout <= 250.0;
        }

        public static Point3d Rot2d(double px, double py, double ang)
        {
            double c = Math.Cos(ang), s = Math.Sin(ang);
            return new Point3d(px * c - py * s, px * s + py * c, 0);
        }

        /// <summary>
        /// Kapalı polyline için offset: dış poligonu dış noktanın yakınlığından seç.
        /// Lisp'teki OFFSET komutunun "outsidePt" seçim mantığına yaklaştırılır.
        /// </summary>
        public static Polyline OffsetOutside(Polyline inner, double dFar, Point3d outPt)
        {
            var candidatePlines = new List<Polyline>();

            void AddCandidates(double dist)
            {
                DBObjectCollection offs = null;
                try { offs = inner.GetOffsetCurves(dist); } catch { offs = null; }
                if (offs == null || offs.Count == 0) return;
                foreach (DBObject o in offs)
                {
                    if (o is Polyline pl && pl.NumberOfVertices >= 3)
                        candidatePlines.Add(pl);
                }
            }

            // OFFSET outsidePt ile taraf seçer; GetOffsetCurves'ta işaret de tarafı etkiler.
            // Bu yüzden her iki işareti de aday olarak al.
            AddCandidates(dFar);
            AddCandidates(-dFar);
            if (candidatePlines.Count == 0) return null;

            Polyline best = null;
            double bestMinDist = double.PositiveInfinity;
            double bestAbsArea = 0;
            double bestDotMax = double.NegativeInfinity;

            var innerVerts = LwPolyVertices(inner);
            if (innerVerts.Count < 3) return null;
            var pOn = innerVerts[0];
            var dirUnit = VUnit(VSub(outPt, pOn));

            foreach (var pl in candidatePlines)
            {
                var verts = LwPolyVertices(pl);
                if (verts.Count < 3) continue;

                // Aday offset poligonunun outPt'ye en yakın noktası (segmentlere göre).
                double minDist = double.PositiveInfinity;
                int n = verts.Count;
                for (int i = 0; i < n; i++)
                {
                    var p1 = verts[i];
                    var p2 = verts[(i + 1) % n];
                    // Nokta-segment mesafesi (2D)
                    var foot = PerpFootOnSeg(outPt, p1, p2);
                    if (foot.HasValue)
                    {
                        double d = outPt.DistanceTo(foot.Value);
                        if (d < minDist) minDist = d;
                    }
                    else
                    {
                        double d = outPt.DistanceTo(p1);
                        if (d < minDist) minDist = d;
                    }
                }

                double absArea = Math.Abs(SignedArea(verts));
                // outPt'nin tarif ettiği yön (lisp: pOn + nrm*1000) boyunca pozitif olan taraf.
                double dotMax = double.NegativeInfinity;
                foreach (var v in verts)
                {
                    var dx = v.X - pOn.X;
                    var dy = v.Y - pOn.Y;
                    double dot = dx * dirUnit.X + dy * dirUnit.Y; // 2D dot
                    if (dot > dotMax) dotMax = dot;
                }

                // Öncelik minDist; eşitse daha büyük alanı tercih et.
                if (dotMax > bestDotMax + 1e-6 ||
                    (Math.Abs(dotMax - bestDotMax) <= 1e-6 && (minDist < bestMinDist - 1e-6 ||
                                                                     (Math.Abs(minDist - bestMinDist) <= 1e-6 && absArea > bestAbsArea))))
                {
                    best = pl;
                    bestMinDist = minDist;
                    bestAbsArea = absArea;
                    bestDotMax = dotMax;
                }
            }

            return best;
        }
    }
}
