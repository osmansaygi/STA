using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ST4Yardimci
{
    internal static partial class TemelCekuruRunner
    {
        private sealed partial class Fit
        {
            private void FinishSingleDetail(Transaction tr, BlockTableRecord ms)
            {
                if (!SingleDetail) return;
                double cut = DetailCutX();
                var ids = new List<ObjectId>();
                foreach (ObjectId id in ms) ids.Add(id);
                foreach (ObjectId id in ids)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Entity ent) || ent.IsErased) continue;
                    if (ent is LineAngularDimension2 angle)
                    {
                        if (!KeepDetailX(angle.XLine1Start.X)) angle.Erase();
                        continue;
                    }
                    if (ent is Dimension || ent.Layer.IndexOf("KOT", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (TryDotCenter(ent, out Point3d dot))
                    {
                        double inner = DetailLeft ? XInnerL : XInnerR + DW;
                        bool inFlat = DetailLeft ? dot.X > inner + 1 : dot.X < inner - 1;
                        double upper = PhysicalDot(6).Y, lower = PhysicalDot(8).Y;
                        if (!KeepDetailX(dot.X) || (inFlat && (Math.Abs(dot.Y - upper) < 1 || Math.Abs(dot.Y - lower) < 1))) ent.Erase();
                        continue;
                    }
                    if (ent is DBText text)
                    {
                        if (!KeepDetailX(text.Position.X)) text.Erase();
                        continue;
                    }
                    if (ent is Hatch hatch) { ClipDetailHatch(hatch, cut, DetailLeft); continue; }
                    if (ent is Line line)
                    {
                        bool a = KeepDetailX(line.StartPoint.X), b = KeepDetailX(line.EndPoint.X);
                        if (!a && !b) { line.Erase(); continue; }
                        if (a == b) continue;
                        Point3d p = line.StartPoint, q = line.EndPoint;
                        double t = (cut - p.X) / (q.X - p.X);
                        Point3d edge = p + (q - p) * t;
                        if (a) line.EndPoint = edge; else line.StartPoint = edge;
                        continue;
                    }
                    if (ent is Polyline polyline) ClipDetailPolyline(tr, ms, polyline, cut);
                }
                double floor = Y3 + Dy3;
                double gro = MapPt(new Point3d(XKotCenter, YGro, 0)).Y;
                double mid = (floor + gro) / 2;
                var cutMark = new Polyline();
                Point2d[] points = {
                    new Point2d(cut, floor + 10), new Point2d(cut, mid + 12),
                    new Point2d(cut - 7, mid + 4), new Point2d(cut + 7, mid - 4),
                    new Point2d(cut, mid - 12), new Point2d(cut, gro - 10)
                };
                for (int i = 0; i < points.Length; i++) cutMark.AddVertexAt(i, points[i], 0, 0, 0);
                cutMark.Layer = LayerZemin;
                cutMark.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                ms.AppendEntity(cutMark);
                tr.AddNewlyCreatedDBObject(cutMark, true);
            }

            private void ClipDetailPolyline(Transaction tr, BlockTableRecord ms, Polyline original, double cut)
            {
                int n = original.NumberOfVertices;
                if (n < 2) return;
                bool inside = false, outside = false;
                for (int i = 0; i < n; i++)
                    if (KeepDetailX(original.GetPoint2dAt(i).X)) inside = true; else outside = true;
                if (!outside) return;
                if (!inside) { original.Erase(); return; }
                var chains = new List<List<Point2d>>();
                List<Point2d> current = null;
                int segments = original.Closed ? n : n - 1;
                for (int i = 0; i < segments; i++)
                {
                    Point2d a = original.GetPoint2dAt(i), b = original.GetPoint2dAt((i + 1) % n);
                    bool keepA = KeepDetailX(a.X), keepB = KeepDetailX(b.X);
                    if (!keepA && !keepB) { current = null; continue; }
                    if (keepA != keepB)
                    {
                        Point2d intersection = DetailIntersection(a, b, cut);
                        if (keepA) b = intersection; else a = intersection;
                    }
                    if (current == null || current[current.Count - 1].GetDistanceTo(a) > NamedDotTolerance)
                    {
                        current = new List<Point2d> { a };
                        chains.Add(current);
                    }
                    current.Add(b);
                    if (!keepB) current = null;
                }
                foreach (List<Point2d> chain in chains)
                {
                    var clipped = new Polyline(chain.Count);
                    clipped.SetPropertiesFrom(original);
                    for (int i = 0; i < chain.Count; i++) clipped.AddVertexAt(i, chain[i], 0, 0, 0);
                    ms.AppendEntity(clipped);
                    tr.AddNewlyCreatedDBObject(clipped, true);
                }
                original.Erase();
            }

            private static Point2d DetailIntersection(Point2d a, Point2d b, double cut)
            {
                double t = (cut - a.X) / (b.X - a.X);
                return new Point2d(cut, a.Y + (b.Y - a.Y) * t);
            }

            private static List<Point2d> ClipDetailPolygon(List<Point2d> points, double cut, bool keepLeft)
            {
                var result = new List<Point2d>();
                if (points.Count == 0) return result;
                Point2d previous = points[points.Count - 1];
                bool previousInside = keepLeft ? previous.X <= cut : previous.X >= cut;
                foreach (Point2d point in points)
                {
                    bool inside = keepLeft ? point.X <= cut : point.X >= cut;
                    if (inside != previousInside) result.Add(DetailIntersection(previous, point, cut));
                    if (inside) result.Add(point);
                    previous = point;
                    previousInside = inside;
                }
                return result;
            }

            private static void ClipDetailHatch(Hatch hatch, double cut, bool keepLeft)
            {
                var loops = new List<KeyValuePair<HatchLoopTypes, List<Point2d>>>();
                int count = hatch.NumberOfLoops;
                for (int i = 0; i < count; i++)
                {
                    HatchLoop loop = hatch.GetLoopAt(i);
                    var points = new List<Point2d>();
                    if (loop.IsPolyline)
                        foreach (BulgeVertex vertex in loop.Polyline) points.Add(vertex.Vertex);
                    else if (loop.Curves != null)
                        foreach (Curve2d curve in loop.Curves)
                            if (curve is LineSegment2d line) points.Add(line.StartPoint);
                    points = ClipDetailPolygon(points, cut, keepLeft);
                    if (points.Count >= 3) loops.Add(new KeyValuePair<HatchLoopTypes, List<Point2d>>(loop.LoopType, points));
                }
                if (loops.Count == 0) { hatch.Erase(); return; }
                hatch.Associative = false;
                for (int i = count - 1; i >= 0; i--) hatch.RemoveLoopAt(i);
                foreach (var loop in loops)
                {
                    var points = new Point2dCollection();
                    var bulges = new DoubleCollection();
                    foreach (Point2d point in loop.Value) { points.Add(point); bulges.Add(0); }
                    hatch.AppendLoop(loop.Key, points, bulges);
                }
                hatch.EvaluateHatch(true);
            }
        }
    }
}
