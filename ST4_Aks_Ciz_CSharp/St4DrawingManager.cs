using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ST4AksCizCSharp
{
    public sealed class St4DrawingManager
    {
        private readonly AxisGeometryService _axisService;

        public St4DrawingManager(St4Model model)
        {
            Model = model;
            _axisService = new AxisGeometryService(model);
        }

        public St4Model Model { get; }

        public void Draw(Database db, Editor ed)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayers(tr, db);
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                var ext = CalculateBaseExtents();
                double floorWidth = (ext.Xmax - ext.Xmin) + 80.0;
                double floorGap = 1000.0;
                double floorHeight = (ext.Ymax - ext.Ymin) + 100.0;
                double rowGap = 800.0;
                int wallCount = 0;
                var createdBeamLineRefs = new List<(ObjectId Id, int OwnerKey)>();
                var createdBeamBodies = new List<(int OwnerKey, Point2d[] Poly)>();

                for (int row = 0; row < 2; row++)
                {
                    bool drawBeams = row == 1;
                    double rowOffsetY = row == 0 ? 0.0 : -(floorHeight + rowGap);

                    for (int floorIdx = 0; floorIdx < Model.Floors.Count; floorIdx++)
                    {
                        var floor = Model.Floors[floorIdx];
                        double offsetX = floorIdx * (floorWidth + floorGap);
                        double offsetY = rowOffsetY;

                        DrawAxes(tr, btr, offsetX, offsetY, ext);
                        DrawColumns(tr, btr, floor, offsetX, offsetY);
                        if (drawBeams)
                        {
                            wallCount += DrawBeamsAndWalls(tr, btr, floor, offsetX, offsetY, createdBeamLineRefs, createdBeamBodies);
                        }
                        DrawLabels(tr, btr, floor, drawBeams, offsetX, offsetY, ext);
                    }
                }

                // Restore baseline behavior:
                // 1) trim line parts inside beam bodies,
                // 2) snap near beam endpoints to intersections,
                // 3) run a final inside-body trim pass.
                createdBeamLineRefs = CleanupBeamLinesInsideBeamBodies(tr, btr, createdBeamLineRefs, createdBeamBodies);
                SnapBeamLineEndpointsToIntersections(tr, createdBeamLineRefs);

                // BREAK-like split on intersecting beam lines (max once per line).
                createdBeamLineRefs = BreakIntersectingBeamLinesOnce(tr, btr, createdBeamLineRefs);

                // Fillet-like cleanup:
                // if a short arm penetrates at least 1 mm into another beam at intersection,
                // trim that short arm back to the intersection.
                FilletIntersectingBeamLinesTrimShortArms(tr, createdBeamLineRefs);

                // Remove beam lines shorter than 22 cm (threshold depends on db.Insunits).
                createdBeamLineRefs = RemoveShortBeamLines(tr, db, createdBeamLineRefs);

                createdBeamLineRefs = CleanupBeamLinesInsideBeamBodies(tr, btr, createdBeamLineRefs, createdBeamBodies);

                // Remove short segments created by CleanupBeamLinesInsideBeamBodies.
                createdBeamLineRefs = RemoveShortBeamLines(tr, db, createdBeamLineRefs);

                // Final pass: scan all beam lines in model space and remove any shorter than 220 mm.
                RemoveShortBeamLinesFromBlock(tr, db, ed);

                DrawCirclesAtFreeBeamEndpoints(tr, btr);

                // Extend sonrasi kesisen kiris cizgilerini tekrar kesisme noktasinda bol, 22 birimden kisa parcalari sil.
                var beamRefsAfterExtend = CollectBeamLineRefsFromBlock(tr, btr);
                BreakIntersectingBeamLinesOnce(tr, btr, beamRefsAfterExtend);
                RemoveShortBeamLinesFromBlock(tr, db, ed);

                // Kesisimde en az bir kol 1 birim tasiyorsa kisa kenarlari kes (fillet).
                var beamRefsForFillet = CollectBeamLineRefsFromBlock(tr, btr);
                FilletIntersectingBeamLinesWhenOneArmSticksOut(tr, beamRefsForFillet);

                LayerService.SetLayerOn(tr, db, "ST4-AKS-CIZGILER", false);
                LayerService.SetLayerOn(tr, db, "ST4-AKS-KOLONLU", false);
                LayerService.SetLayerOn(tr, db, "ST4-AKS-KOLONSUZ", false);
                LayerService.SetLayerOn(tr, db, "ST4-AKS-ETIKETLER", false);
                LayerService.SetLayerOn(tr, db, "ST4-KOLON-NUMARALARI", false);
                db.Clayer = ((LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead))["ST4-KIRISLAR"];

                tr.Commit();

                ed.WriteMessage(
                    $"\n{Model.Floors.Count} kat, {Model.AxisX.Count} X ekseni, {Model.AxisY.Count} Y ekseni, {Model.Columns.Count} kolon/kat" +
                    (Model.Beams.Count > 0 ? $", {Model.Beams.Count} kiris" : string.Empty) +
                    (wallCount > 0 ? $", {wallCount} perde" : string.Empty) +
                    " cizildi. (cm)");
            }
        }

        private static void EnsureLayers(Transaction tr, Database db)
        {
            LayerService.EnsureLayer(tr, db, "ST4-AKS-CIZGILER", 1);
            LayerService.EnsureLayer(tr, db, "ST4-AKS-KOLONLU", 1);
            LayerService.EnsureLayer(tr, db, "ST4-AKS-KOLONSUZ", 8);
            LayerService.EnsureLayer(tr, db, "ST4-AKS-ETIKETLER", 3);
            LayerService.EnsureLayer(tr, db, "ST4-KOLONLAR", 3);
            LayerService.EnsureLayer(tr, db, "ST4-KOLON-NUMARALARI", 2);
            LayerService.EnsureLayer(tr, db, "ST4-KIRISLAR", 2);
            LayerService.EnsureLayer(tr, db, "ST4-PERDELER", 6);
            LayerService.EnsureLayer(tr, db, "ST4-SERBEST-UCLAR", 1);
        }

        private static void DrawCirclesAtFreeBeamEndpoints(Transaction tr, BlockTableRecord btr)
        {
            const string beamLayer = "ST4-KIRISLAR";
            const string columnLayer = "ST4-KOLONLAR";
            const string wallLayer = "ST4-PERDELER";
            // Uca en yakin eleman (kiriş ucu, kolon veya perde) 3 birimden uzaktaysa uç boşta kabul edilir.
            const double connectionTol = 3.0;
            const double circleRadius = 5.0;
            const double extendMin = 1.0;
            const double extendMax = 100.0;

            var lines = new List<(ObjectId Id, Point2d Start, Point2d End)>();
            var columnCircles = new List<(Point2d Center, double Radius)>();
            var columnPolys = new List<Point2d[]>();
            var wallPolys = new List<Point2d[]>();

            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                if (ent.Layer == beamLayer && ent is Line ln)
                {
                    var sp = ln.StartPoint;
                    var ep = ln.EndPoint;
                    lines.Add((id, new Point2d(sp.X, sp.Y), new Point2d(ep.X, ep.Y)));
                }
                else if (ent.Layer == columnLayer)
                {
                    if (ent is Circle circ)
                    {
                        columnCircles.Add((new Point2d(circ.Center.X, circ.Center.Y), circ.Radius));
                    }
                    else if (ent is Polyline colPl)
                    {
                        var pts = new Point2d[colPl.NumberOfVertices];
                        for (int i = 0; i < pts.Length; i++) pts[i] = colPl.GetPoint2dAt(i);
                        columnPolys.Add(pts);
                    }
                }
                else if (ent.Layer == wallLayer && ent is Polyline wallPl)
                {
                    var pts = new Point2d[wallPl.NumberOfVertices];
                    for (int i = 0; i < pts.Length; i++) pts[i] = wallPl.GetPoint2dAt(i);
                    wallPolys.Add(pts);
                }
            }

            bool IsConnectedToColumnsOrWalls(Point2d pt)
            {
                foreach (var (center, radius) in columnCircles)
                    if (pt.GetDistanceTo(center) <= radius + connectionTol) return true;
                foreach (var vertices in columnPolys)
                    if (MinDistancePointToPolyline(pt, vertices, true) <= connectionTol) return true;
                foreach (var vertices in wallPolys)
                    if (MinDistancePointToPolyline(pt, vertices, true) <= connectionTol) return true;
                return false;
            }

            var freeEnds = new List<(int lineIdx, int end, Point2d pt)>();
            for (int i = 0; i < lines.Count; i++)
            {
                for (int end = 0; end < 2; end++)
                {
                    var pt = end == 0 ? lines[i].Start : lines[i].End;
                    bool connected = false;
                    for (int j = 0; j < lines.Count; j++)
                    {
                        if (i == j) continue;
                        if (pt.GetDistanceTo(lines[j].Start) <= connectionTol ||
                            pt.GetDistanceTo(lines[j].End) <= connectionTol)
                        {
                            connected = true;
                            break;
                        }
                    }
                    if (!connected && IsConnectedToColumnsOrWalls(pt))
                        connected = true;
                    if (!connected)
                        freeEnds.Add((i, end, pt));
                }
            }

            var drawn = new List<(int lineIdx, int end, Point2d pt)>();
            foreach (var fe in freeEnds)
            {
                if (drawn.Any(d => d.pt.GetDistanceTo(fe.pt) <= connectionTol)) continue;
                drawn.Add(fe);
                var circle = new Circle
                {
                    Center = new Point3d(fe.pt.X, fe.pt.Y, 0),
                    Radius = circleRadius,
                    Layer = "ST4-SERBEST-UCLAR"
                };
                btr.AppendEntity(circle);
                tr.AddNewlyCreatedDBObject(circle, true);
            }

            ExtendBeamLinesAtMarkedPoints(tr, btr, lines, columnCircles, columnPolys, wallPolys, drawn, connectionTol, extendMin, extendMax);
        }

        private static void ExtendBeamLinesAtMarkedPoints(
            Transaction tr,
            BlockTableRecord btr,
            List<(ObjectId Id, Point2d Start, Point2d End)> lines,
            List<(Point2d Center, double Radius)> columnCircles,
            List<Point2d[]> columnPolys,
            List<Point2d[]> wallPolys,
            List<(int lineIdx, int end, Point2d pt)> markedEnds,
            double connectionTol,
            double extendMin,
            double extendMax)
        {
            foreach (var (lineIdx, end, freePt) in markedEnds)
            {
                var (lineId, start, last) = lines[lineIdx];
                Point2d otherEnd = end == 0 ? last : start;
                Vector2d dir = (freePt - otherEnd).GetNormal();
                if (dir.Length <= 1e-9) continue;

                double? minDist = null;

                for (int j = 0; j < lines.Count; j++)
                {
                    if (j == lineIdx) continue;
                    var (_, a, b) = lines[j];
                    if (RaySegmentIntersection(freePt, dir, a, b, out double t) && t > 1e-6 && (!minDist.HasValue || t < minDist.Value))
                        minDist = t;
                }
                foreach (var (center, radius) in columnCircles)
                {
                    if (RayCircleIntersection(freePt, dir, center, radius, out double t) && t > 1e-6 && (!minDist.HasValue || t < minDist.Value))
                        minDist = t;
                }
                foreach (var vertices in columnPolys)
                {
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        int j = (i + 1) % vertices.Length;
                        if (RaySegmentIntersection(freePt, dir, vertices[i], vertices[j], out double t) && t > 1e-6 && (!minDist.HasValue || t < minDist.Value))
                            minDist = t;
                    }
                }
                foreach (var vertices in wallPolys)
                {
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        int j = (i + 1) % vertices.Length;
                        if (RaySegmentIntersection(freePt, dir, vertices[i], vertices[j], out double t) && t > 1e-6 && (!minDist.HasValue || t < minDist.Value))
                            minDist = t;
                    }
                }

                if (!minDist.HasValue || minDist.Value < extendMin) continue;
                double ext = Math.Min(minDist.Value, extendMax);

                Point2d newEnd = freePt + dir.MultiplyBy(ext);
                var lineEnt = tr.GetObject(lineId, OpenMode.ForWrite) as Line;
                if (lineEnt == null) continue;
                if (end == 0)
                    lineEnt.StartPoint = new Point3d(newEnd.X, newEnd.Y, 0);
                else
                    lineEnt.EndPoint = new Point3d(newEnd.X, newEnd.Y, 0);
            }
        }

        private static bool RaySegmentIntersection(Point2d origin, Vector2d dir, Point2d a, Point2d b, out double t)
        {
            t = 0;
            Vector2d v = b - a;
            double denom = dir.X * v.Y - dir.Y * v.X;
            if (Math.Abs(denom) < 1e-10) return false;
            double numT = (a.X - origin.X) * v.Y - (a.Y - origin.Y) * v.X;
            t = numT / denom;
            if (t < 0) return false;
            double numS = (a.X - origin.X) * dir.Y - (a.Y - origin.Y) * dir.X;
            double s = numS / denom;
            return s >= -1e-10 && s <= 1.0 + 1e-10;
        }

        private static bool RayCircleIntersection(Point2d origin, Vector2d dir, Point2d center, double radius, out double t)
        {
            t = 0;
            Vector2d oc = origin - center;
            double a = dir.DotProduct(dir);
            double b = 2.0 * oc.DotProduct(dir);
            double c = oc.DotProduct(oc) - radius * radius;
            double disc = b * b - 4 * a * c;
            if (disc < 0) return false;
            double sqrt = Math.Sqrt(disc);
            double t0 = (-b - sqrt) / (2.0 * a);
            double t1 = (-b + sqrt) / (2.0 * a);
            if (t0 >= 0) { t = t0; return true; }
            if (t1 >= 0) { t = t1; return true; }
            return false;
        }

        private static double MinDistancePointToSegment(Point2d p, Point2d a, Point2d b)
        {
            var v = b - a;
            var w = p - a;
            double c1 = w.DotProduct(v);
            if (c1 <= 0) return p.GetDistanceTo(a);
            double c2 = v.DotProduct(v);
            if (c2 <= c1) return p.GetDistanceTo(b);
            double t = c1 / c2;
            var proj = a + v.MultiplyBy(t);
            return p.GetDistanceTo(proj);
        }

        private static double MinDistancePointToPolyline(Point2d p, Point2d[] vertices, bool closed)
        {
            if (vertices == null || vertices.Length < 2) return double.MaxValue;
            double min = double.MaxValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                int j = closed ? (i + 1) % vertices.Length : i + 1;
                if (j >= vertices.Length) break;
                double d = MinDistancePointToSegment(p, vertices[i], vertices[j]);
                if (d < min) min = d;
            }
            return min;
        }

        private (double Xmin, double Xmax, double Ymin, double Ymax) CalculateBaseExtents()
        {
            const double margin = 50.0;
            double xmin = Model.AxisX.Count > 0 ? Model.AxisX.Min(x => x.ValueCm) - margin : 0.0;
            double xmax = Model.AxisX.Count > 0 ? Model.AxisX.Max(x => x.ValueCm) + margin : 1000.0;
            double ymin = Model.AxisY.Count > 0 ? -Model.AxisY.Max(y => y.ValueCm) - margin : -1000.0;
            double ymax = Model.AxisY.Count > 0 ? -Model.AxisY.Min(y => y.ValueCm) + margin : 1000.0;

            foreach (var y in Model.AxisY)
            {
                if (Math.Abs(y.Slope) <= 1e-9) continue;
                ymin = Math.Min(ymin, -(y.ValueCm + y.Slope * xmin));
                ymin = Math.Min(ymin, -(y.ValueCm + y.Slope * xmax));
                ymax = Math.Max(ymax, -(y.ValueCm + y.Slope * xmin));
                ymax = Math.Max(ymax, -(y.ValueCm + y.Slope * xmax));
            }
            foreach (var x in Model.AxisX)
            {
                if (Math.Abs(x.Slope) <= 1e-9) continue;
                xmin = Math.Min(xmin, x.ValueCm + x.Slope * ymin);
                xmin = Math.Min(xmin, x.ValueCm + x.Slope * ymax);
                xmax = Math.Max(xmax, x.ValueCm + x.Slope * ymin);
                xmax = Math.Max(xmax, x.ValueCm + x.Slope * ymax);
            }

            return (xmin - margin, xmax + margin, ymin - margin, ymax + margin);
        }

        private void DrawAxes(Transaction tr, BlockTableRecord btr, double offsetX, double offsetY, (double Xmin, double Xmax, double Ymin, double Ymax) ext)
        {
            var xHasCol = BuildAxisUsageMap(Model.AxisX.Select(a => a.Id), c => c.AxisXId);
            var yHasCol = BuildAxisUsageMap(Model.AxisY.Select(a => a.Id), c => c.AxisYId);

            foreach (var ax in Model.AxisX)
            {
                string layer = xHasCol.Contains(ax.Id) ? "ST4-AKS-KOLONLU" : "ST4-AKS-KOLONSUZ";
                var p1 = Math.Abs(ax.Slope) <= 1e-9
                    ? new Point3d(ax.ValueCm + offsetX, ext.Ymin + offsetY, 0)
                    : new Point3d(offsetX + ax.ValueCm + ax.Slope * ext.Ymin, ext.Ymin + offsetY, 0);
                var p2 = Math.Abs(ax.Slope) <= 1e-9
                    ? new Point3d(ax.ValueCm + offsetX, ext.Ymax + offsetY, 0)
                    : new Point3d(offsetX + ax.ValueCm + ax.Slope * ext.Ymax, ext.Ymax + offsetY, 0);
                AppendEntity(tr, btr, new Line(p1, p2) { Layer = layer });
            }

            foreach (var ay in Model.AxisY)
            {
                string layer = yHasCol.Contains(ay.Id) ? "ST4-AKS-KOLONLU" : "ST4-AKS-KOLONSUZ";
                var p1 = Math.Abs(ay.Slope) <= 1e-9
                    ? new Point3d(ext.Xmin + offsetX, -ay.ValueCm + offsetY, 0)
                    : new Point3d(ext.Xmin + offsetX, -(ay.ValueCm + ay.Slope * ext.Xmin) + offsetY, 0);
                var p2 = Math.Abs(ay.Slope) <= 1e-9
                    ? new Point3d(ext.Xmax + offsetX, -ay.ValueCm + offsetY, 0)
                    : new Point3d(ext.Xmax + offsetX, -(ay.ValueCm + ay.Slope * ext.Xmax) + offsetY, 0);
                AppendEntity(tr, btr, new Line(p1, p2) { Layer = layer });
            }
        }

        private int DrawBeamsAndWalls(
            Transaction tr,
            BlockTableRecord btr,
            FloorInfo floor,
            double offsetX,
            double offsetY,
            List<(ObjectId Id, int OwnerKey)> createdBeamLineRefs,
            List<(int OwnerKey, Point2d[] Poly)> createdBeamBodies)
        {
            int wallCount = 0;
            var beams = MergeSameIdBeamsOnFloor(floor.FloorNo);
            var beamProfiles = new List<(int OwnerKey, BeamInfo Beam, Point2d Q1, Point2d Q2, Point2d Q3, Point2d Q4, Point3d Center)>();
            foreach (var beam in beams)
            {
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1)) continue;
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2)) continue;

                var a = new Point2d(p1.X + offsetX, p1.Y + offsetY);
                var b = new Point2d(p2.X + offsetX, p2.Y + offsetY);
                NormalizeBeamDirection(beam.FixedAxisId, ref a, ref b);
                Vector2d dir = b - a;
                if (dir.Length <= 1e-9) continue;
                Vector2d u = dir.GetNormal();
                Vector2d perp = new Vector2d(-u.Y, u.X);
                double hw = beam.WidthCm / 2.0;
                ComputeBeamEdgeOffsets(beam.OffsetRaw, hw, out double upperEdge, out double lowerEdge);

                Point2d q1 = a + perp.MultiplyBy(upperEdge);
                Point2d q2 = b + perp.MultiplyBy(upperEdge);
                Point2d q3 = b + perp.MultiplyBy(lowerEdge);
                Point2d q4 = a + perp.MultiplyBy(lowerEdge);
                var center = new Point3d((q1.X + q2.X + q3.X + q4.X) / 4.0, (q1.Y + q2.Y + q3.Y + q4.Y) / 4.0, 0);
                int ownerKey = beamProfiles.Count + 1;
                beamProfiles.Add((ownerKey, beam, q1, q2, q3, q4, center));
            }

            foreach (var pr in beamProfiles)
            {
                createdBeamBodies.Add((pr.OwnerKey, new[] { pr.Q1, pr.Q2, pr.Q3, pr.Q4 }));
            }

            var columnOnlyBlockers = BuildBlockerPolygonsForFloor(floor.FloorNo, offsetX, offsetY);
            var columnAndWallBlockers = new List<Point2d[]>(columnOnlyBlockers);

            foreach (var pr in beamProfiles.Where(x => x.Beam.IsWallFlag == 1))
            {
                // Önce ham perde polyline çiz; sonra perde+kolon baz alarak boundary (Region subtract) ile net çizim elde et.
                var rawWallPoly = new[] { pr.Q1, pr.Q2, pr.Q3, pr.Q4 };
                if (!TryDrawWallAsBoundary(tr, btr, rawWallPoly, columnOnlyBlockers, out var resultPolyIds))
                {
                    // Region boundary başarısız olursa eski yöntem (kırpılmış poligon)
                    var t12 = PreTrimBeamEdgeEnds(pr.Q1, pr.Q2, columnOnlyBlockers, new List<Point2d[]>());
                    var t43 = PreTrimBeamEdgeEnds(pr.Q4, pr.Q3, columnOnlyBlockers, new List<Point2d[]>());
                    if (t12.HasValue && t43.HasValue)
                    {
                        var T1 = t12.Value.A; var T2 = t12.Value.B; var T3 = t43.Value.B; var T4 = t43.Value.A;
                        var wallPts = BuildWallPolygonOutsideColumns(T1, T2, T3, T4, columnOnlyBlockers);
                        columnAndWallBlockers.Add(wallPts.ToArray());
                        var pl = ToPolyline(wallPts, true);
                        pl.Layer = "ST4-PERDELER";
                        AppendEntity(tr, btr, pl);
                        wallCount++;
                    }
                    continue;
                }
                foreach (var oid in resultPolyIds)
                {
                    columnAndWallBlockers.Add(GetPolylinePoints(tr, oid));
                    wallCount++;
                }
            }

            foreach (var pr in beamProfiles.Where(x => x.Beam.IsWallFlag != 1))
            {
                var outwardBlockers = new List<Point2d[]>(columnAndWallBlockers);
                var inwardBeamBlockers = new List<Point2d[]>();

                // Beam-beam cleanup: trim this beam lines against other beam bodies too.
                foreach (var other in beamProfiles)
                {
                    if (other.OwnerKey == pr.OwnerKey) continue;
                    inwardBeamBlockers.Add(new[] { other.Q1, other.Q2, other.Q3, other.Q4 });
                }

                var t12 = PreTrimBeamEdgeEnds(pr.Q1, pr.Q2, outwardBlockers, inwardBeamBlockers);
                var t43 = PreTrimBeamEdgeEnds(pr.Q4, pr.Q3, outwardBlockers, inwardBeamBlockers);
                if (t12.HasValue) DrawTrimmedLine(tr, btr, t12.Value.A, t12.Value.B, "ST4-KIRISLAR", outwardBlockers, inwardBeamBlockers, pr.OwnerKey, createdBeamLineRefs);
                if (t43.HasValue) DrawTrimmedLine(tr, btr, t43.Value.A, t43.Value.B, "ST4-KIRISLAR", outwardBlockers, inwardBeamBlockers, pr.OwnerKey, createdBeamLineRefs);
            }

            foreach (var pr in beamProfiles)
            {
                AppendEntity(tr, btr, new DBText
                {
                    Layer = "ST4-AKS-ETIKETLER",
                    Height = 6,
                    TextString = pr.Beam.BeamId.ToString(CultureInfo.InvariantCulture),
                    Position = pr.Center,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextVerticalMid,
                    AlignmentPoint = pr.Center
                });
            }
            return wallCount;
        }

        private List<Point2d[]> BuildBlockerPolygonsForFloor(int floorNo, double offsetX, double offsetY)
        {
            var blockers = new List<Point2d[]>();
            foreach (var col in Model.Columns)
            {
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                int sectionId = floorNo * 100 + col.ColumnNo;
                if (col.ColumnType <= 2 && !Model.ColumnDimsBySectionId.ContainsKey(sectionId)) continue;
                if (col.ColumnType == 3 && !Model.PolygonColumnSectionByPositionSectionId.ContainsKey(sectionId)) continue;

                var dim = Model.ColumnDimsBySectionId.ContainsKey(sectionId) ? Model.ColumnDimsBySectionId[sectionId] : (W: 40.0, H: 40.0);
                double hw = dim.W / 2.0;
                double hh = dim.H / 2.0;
                var offsetLocal = ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                var center = new Point2d(axisNode.X + offsetGlobal.X + offsetX, axisNode.Y + offsetGlobal.Y + offsetY);

                if (col.ColumnType == 2)
                {
                    blockers.Add(ApproximateCircle(center, Math.Max(hw, hh), 24));
                }
                else if (col.ColumnType == 3 && TryGetPolygonColumn(sectionId, center, col.AngleDeg, out var poly))
                {
                    blockers.Add(poly);
                }
                else
                {
                    blockers.Add(BuildRect(center, hw, hh, col.AngleDeg));
                }
            }
            return blockers;
        }

        private static Point2d[] ApproximateCircle(Point2d c, double r, int segments)
        {
            var pts = new Point2d[segments];
            for (int i = 0; i < segments; i++)
            {
                double a = (2.0 * Math.PI * i) / segments;
                pts[i] = new Point2d(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
            }
            return pts;
        }

        private static void DrawTrimmedLine(
            Transaction tr,
            BlockTableRecord btr,
            Point2d a,
            Point2d b,
            string layer,
            List<Point2d[]> blockersOutward,
            List<Point2d[]> blockersInward,
            int ownerKey,
            List<(ObjectId Id, int OwnerKey)> createdBeamLineRefs)
        {
            double len = a.GetDistanceTo(b);
            if (len <= 1e-9) return;
            var dir = (b - a).GetNormal();
            const double minKeep = 2.0;
            const double endExtend = 0.2; // 0.2 cm (drawing unit is cm)
            const double outwardTol = 0.5; // cm
            const double inwardTol = -0.5; // cm
            var intervals = new List<(double S, double E)>();

            foreach (var poly in blockersOutward)
            {
                foreach (var iv in SegmentInsideIntervalsInPolygon(a, b, poly, outwardTol))
                {
                    if (iv.E > iv.S + minKeep)
                    {
                        intervals.Add(iv);
                    }
                }
            }
            foreach (var poly in blockersInward)
            {
                foreach (var iv in SegmentInsideIntervalsInPolygon(a, b, poly, inwardTol))
                {
                    if (iv.E > iv.S + minKeep)
                    {
                        intervals.Add(iv);
                    }
                }
            }

            var merged = MergeIntervals(intervals, 0.5);
            var keeps = InvertIntervals(merged, len, minKeep);
            foreach (var keep in keeps)
            {
                var p1 = a + dir.MultiplyBy(keep.S);
                var p2 = a + dir.MultiplyBy(keep.E);
                if (p1.GetDistanceTo(p2) < minKeep) continue;

                // Close tiny visual gaps caused by trim tolerance.
                var e1 = p1 - dir.MultiplyBy(endExtend);
                var e2 = p2 + dir.MultiplyBy(endExtend);
                var ln = new Line(new Point3d(e1.X, e1.Y, 0), new Point3d(e2.X, e2.Y, 0)) { Layer = layer };
                AppendEntity(tr, btr, ln);
                createdBeamLineRefs.Add((ln.ObjectId, ownerKey));
            }
        }

        private static void SnapBeamLineEndpointsToIntersections(Transaction tr, List<(ObjectId Id, int OwnerKey)> lineRefs)
        {
            const double endpointSnapTol = 1.5;   // cm
            const double endpointExtendTol = 2.0; // cm

            var lines = new List<Line>(lineRefs.Count);
            foreach (var lineRef in lineRefs)
            {
                var id = lineRef.Id;
                if (id.IsNull || id.IsErased) continue;
                if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Line ln)) continue;
                if (ln.Length <= 1e-6) continue;
                lines.Add(ln);
            }

            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    var l1 = lines[i];
                    var l2 = lines[j];
                    if (!TryIntersectInfinite2d(l1.StartPoint, l1.EndPoint, l2.StartPoint, l2.EndPoint, out Point3d ip)) continue;
                    if (!IntersectionNearSegment(l1.StartPoint, l1.EndPoint, ip, endpointExtendTol)) continue;
                    if (!IntersectionNearSegment(l2.StartPoint, l2.EndPoint, ip, endpointExtendTol)) continue;

                    double d1s = l1.StartPoint.DistanceTo(ip);
                    double d1e = l1.EndPoint.DistanceTo(ip);
                    double d2s = l2.StartPoint.DistanceTo(ip);
                    double d2e = l2.EndPoint.DistanceTo(ip);
                    double d1 = Math.Min(d1s, d1e);
                    double d2 = Math.Min(d2s, d2e);
                    if (d1 > endpointSnapTol || d2 > endpointSnapTol) continue;

                    if (d1s <= d1e) l1.StartPoint = ip; else l1.EndPoint = ip;
                    if (d2s <= d2e) l2.StartPoint = ip; else l2.EndPoint = ip;
                }
            }
        }

        private static List<(ObjectId Id, int OwnerKey)> CleanupBeamLinesInsideBeamBodies(
            Transaction tr,
            BlockTableRecord btr,
            List<(ObjectId Id, int OwnerKey)> lineRefs,
            List<(int OwnerKey, Point2d[] Poly)> beamBodies)
        {
            const double insideTol = -0.1; // 1 mm inward check (cm units)
            return CleanupBeamLinesInsideBeamBodiesWithTolerance(tr, btr, lineRefs, beamBodies, insideTol);
        }

        private static List<(ObjectId Id, int OwnerKey)> CleanupBeamLinesInsideBeamBodiesExact(
            Transaction tr,
            BlockTableRecord btr,
            List<(ObjectId Id, int OwnerKey)> lineRefs,
            List<(int OwnerKey, Point2d[] Poly)> beamBodies)
        {
            const double exactTol = 0.0;
            return CleanupBeamLinesInsideBeamBodiesWithTolerance(tr, btr, lineRefs, beamBodies, exactTol);
        }

        private static List<(ObjectId Id, int OwnerKey)> CleanupCrossingLinesInsideBeamBodiesExact(
            Transaction tr,
            BlockTableRecord btr,
            List<(ObjectId Id, int OwnerKey)> lineRefs,
            List<(int OwnerKey, Point2d[] Poly)> beamBodies)
        {
            const double minKeep = 2.0;
            const double exactTol = 0.0;
            const double crossingDotMax = 0.85; // trim only non-parallel crossings
            var result = new List<(ObjectId Id, int OwnerKey)>();

            foreach (var lineRef in lineRefs)
            {
                var id = lineRef.Id;
                if (id.IsNull || id.IsErased) continue;
                if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Line ln)) continue;

                var a = new Point2d(ln.StartPoint.X, ln.StartPoint.Y);
                var b = new Point2d(ln.EndPoint.X, ln.EndPoint.Y);
                double len = a.GetDistanceTo(b);
                if (len <= 1e-9) { ln.Erase(); continue; }
                var dir = (b - a).GetNormal();

                var intervals = new List<(double S, double E)>();
                foreach (var body in beamBodies)
                {
                    if (body.OwnerKey == lineRef.OwnerKey) continue;
                    if (body.Poly.Length < 2) continue;

                    var bd = (body.Poly[1] - body.Poly[0]).GetNormal();
                    if (Math.Abs(dir.DotProduct(bd)) > crossingDotMax) continue;

                    intervals.AddRange(SegmentInsideIntervalsInPolygon(a, b, body.Poly, exactTol));
                }

                var merged = MergeIntervals(intervals, 0.5);
                if (merged.Count == 0)
                {
                    result.Add(lineRef);
                    continue;
                }

                var keeps = InvertIntervals(merged, len, minKeep);
                ln.Erase();
                foreach (var keep in keeps)
                {
                    var p1 = a + dir.MultiplyBy(keep.S);
                    var p2 = a + dir.MultiplyBy(keep.E);
                    if (p1.GetDistanceTo(p2) < minKeep) continue;
                    var newLine = new Line(new Point3d(p1.X, p1.Y, 0), new Point3d(p2.X, p2.Y, 0))
                    {
                        Layer = "ST4-KIRISLAR"
                    };
                    AppendEntity(tr, btr, newLine);
                    result.Add((newLine.ObjectId, lineRef.OwnerKey));
                }
            }

            return result;
        }

        private static List<(ObjectId Id, int OwnerKey)> CleanupBeamLinesInsideBeamBodiesWithTolerance(
            Transaction tr,
            BlockTableRecord btr,
            List<(ObjectId Id, int OwnerKey)> lineRefs,
            List<(int OwnerKey, Point2d[] Poly)> beamBodies,
            double insideTol)
        {
            const double minKeep = 2.0;
            var result = new List<(ObjectId Id, int OwnerKey)>();

            foreach (var lineRef in lineRefs)
            {
                var id = lineRef.Id;
                if (id.IsNull || id.IsErased) continue;
                if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Line ln)) continue;

                var a = new Point2d(ln.StartPoint.X, ln.StartPoint.Y);
                var b = new Point2d(ln.EndPoint.X, ln.EndPoint.Y);
                double len = a.GetDistanceTo(b);
                if (len <= 1e-9) { ln.Erase(); continue; }
                var dir = (b - a).GetNormal();

                var intervals = new List<(double S, double E)>();
                foreach (var body in beamBodies)
                {
                    if (body.OwnerKey == lineRef.OwnerKey) continue;
                    intervals.AddRange(SegmentInsideIntervalsInPolygon(a, b, body.Poly, insideTol));
                }

                var merged = MergeIntervals(intervals, 0.5);
                if (merged.Count == 0)
                {
                    result.Add(lineRef);
                    continue;
                }

                var keeps = InvertIntervals(merged, len, minKeep);
                ln.Erase();
                foreach (var keep in keeps)
                {
                    var p1 = a + dir.MultiplyBy(keep.S);
                    var p2 = a + dir.MultiplyBy(keep.E);
                    if (p1.GetDistanceTo(p2) < minKeep) continue;
                    var newLine = new Line(new Point3d(p1.X, p1.Y, 0), new Point3d(p2.X, p2.Y, 0))
                    {
                        Layer = "ST4-KIRISLAR"
                    };
                    AppendEntity(tr, btr, newLine);
                    result.Add((newLine.ObjectId, lineRef.OwnerKey));
                }
            }

            return result;
        }

        private static List<(ObjectId Id, int OwnerKey)> CollectBeamLineRefsFromBlock(Transaction tr, BlockTableRecord btr)
        {
            const string beamLayer = "ST4-KIRISLAR";
            var list = new List<(ObjectId Id, int OwnerKey)>();
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                if (!(tr.GetObject(id, OpenMode.ForRead) is Line ln)) continue;
                if (ln.Layer != beamLayer) continue;
                list.Add((id, 0));
            }
            return list;
        }

        private static List<(ObjectId Id, int OwnerKey)> BreakIntersectingBeamLinesOnce(
            Transaction tr,
            BlockTableRecord btr,
            List<(ObjectId Id, int OwnerKey)> lineRefs)
        {
            const double eps = 1e-6;
            const double parallelDot = 0.995;
            const double minSegmentLength = 40.0; // cm; do not split if either part would be shorter

            var result = new List<(ObjectId Id, int OwnerKey)>();

            // Snapshot original lines so each original is broken at most once.
            var snapshot = new List<(ObjectId Id, int OwnerKey, Point3d A, Point3d B)>();
            foreach (var lineRef in lineRefs)
            {
                if (lineRef.Id.IsNull || lineRef.Id.IsErased) continue;
                if (!(tr.GetObject(lineRef.Id, OpenMode.ForWrite, false) is Line ln)) continue;
                if (ln.Length <= eps) continue;
                snapshot.Add((lineRef.Id, lineRef.OwnerKey, ln.StartPoint, ln.EndPoint));
            }

            for (int i = 0; i < snapshot.Count; i++)
            {
                var s = snapshot[i];
                if (!(tr.GetObject(s.Id, OpenMode.ForWrite, false) is Line ln) || ln.IsErased)
                {
                    continue;
                }

                var d1 = (s.B - s.A).GetNormal();
                Point3d? breakPoint = null;

                for (int j = 0; j < snapshot.Count; j++)
                {
                    if (i == j) continue;
                    var o = snapshot[j];
                    var d2 = (o.B - o.A).GetNormal();
                    if (Math.Abs(d1.DotProduct(d2)) >= parallelDot) continue;

                    if (!TryIntersectInfinite2d(s.A, s.B, o.A, o.B, out var ip)) continue;
                    if (!IsPointOnSegment2d(ip, s.A, s.B, eps)) continue;
                    if (!IsPointOnSegment2d(ip, o.A, o.B, eps)) continue;

                    // True BREAKPOINT behavior: split exactly at intersection point.
                    // Skip only degenerate endpoint splits on the line itself.
                    if (ip.DistanceTo(s.A) <= eps || ip.DistanceTo(s.B) <= eps) continue;

                    breakPoint = ip;
                    break;
                }

                if (!breakPoint.HasValue)
                {
                    result.Add((s.Id, s.OwnerKey));
                    continue;
                }

                var p = breakPoint.Value;
                double len1 = p.DistanceTo(ln.StartPoint);
                double len2 = p.DistanceTo(ln.EndPoint);
                if (len1 < minSegmentLength || len2 < minSegmentLength)
                {
                    result.Add((s.Id, s.OwnerKey));
                    continue;
                }

                var l1 = new Line(ln.StartPoint, p) { Layer = ln.Layer };
                var l2 = new Line(p, ln.EndPoint) { Layer = ln.Layer };
                ln.Erase();
                AppendEntity(tr, btr, l1);
                AppendEntity(tr, btr, l2);
                result.Add((l1.ObjectId, s.OwnerKey));
                result.Add((l2.ObjectId, s.OwnerKey));
            }

            return result;
        }

        private static void FilletIntersectingBeamLinesTrimShortArms(
            Transaction tr,
            List<(ObjectId Id, int OwnerKey)> lineRefs)
        {
            const double eps = 1e-6;
            const double minPenetration = 0.1; // 1 mm in cm units
            const double maxShortArm = 50.0;   // guardrail
            const double shortLongRatio = 0.35;
            const double parallelDot = 0.995;

            var lines = new List<Line>(lineRefs.Count);
            foreach (var lineRef in lineRefs)
            {
                if (lineRef.Id.IsNull || lineRef.Id.IsErased) continue;
                if (!(tr.GetObject(lineRef.Id, OpenMode.ForWrite, false) is Line ln)) continue;
                if (ln.Length <= eps) continue;
                lines.Add(ln);
            }

            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    var l1 = lines[i];
                    var l2 = lines[j];
                    var d1 = (l1.EndPoint - l1.StartPoint).GetNormal();
                    var d2 = (l2.EndPoint - l2.StartPoint).GetNormal();
                    if (Math.Abs(d1.DotProduct(d2)) >= parallelDot) continue;

                    if (!TryIntersectInfinite2d(l1.StartPoint, l1.EndPoint, l2.StartPoint, l2.EndPoint, out var ip)) continue;
                    if (!IsPointOnSegment2d(ip, l1.StartPoint, l1.EndPoint, eps)) continue;
                    if (!IsPointOnSegment2d(ip, l2.StartPoint, l2.EndPoint, eps)) continue;

                    TrimShortArmIfPenetrating(l1, ip, minPenetration, maxShortArm, shortLongRatio);
                    TrimShortArmIfPenetrating(l2, ip, minPenetration, maxShortArm, shortLongRatio);
                }
            }
        }

        /// <summary>
        /// Kesisen kiriş cizgilerinde en az bir kol 1 birim tasiyorsa kisa kenarlari kesisme noktasina ceker (fillet).
        /// </summary>
        private static void FilletIntersectingBeamLinesWhenOneArmSticksOut(
            Transaction tr,
            List<(ObjectId Id, int OwnerKey)> lineRefs)
        {
            const double eps = 1e-6;
            const double minArmToApply = 1.0;  // en az bir kol bu kadar tasimali
            const double minPenetration = 0.1;
            const double maxShortArm = 50.0;
            const double shortLongRatio = 0.35;
            const double parallelDot = 0.995;

            var lines = new List<Line>(lineRefs.Count);
            foreach (var lineRef in lineRefs)
            {
                if (lineRef.Id.IsNull || lineRef.Id.IsErased) continue;
                if (!(tr.GetObject(lineRef.Id, OpenMode.ForWrite, false) is Line ln)) continue;
                if (ln.Length <= eps) continue;
                lines.Add(ln);
            }

            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    var l1 = lines[i];
                    var l2 = lines[j];
                    var d1 = (l1.EndPoint - l1.StartPoint).GetNormal();
                    var d2 = (l2.EndPoint - l2.StartPoint).GetNormal();
                    if (Math.Abs(d1.DotProduct(d2)) >= parallelDot) continue;

                    if (!TryIntersectInfinite2d(l1.StartPoint, l1.EndPoint, l2.StartPoint, l2.EndPoint, out var ip)) continue;
                    if (!IsPointOnSegment2d(ip, l1.StartPoint, l1.EndPoint, eps)) continue;
                    if (!IsPointOnSegment2d(ip, l2.StartPoint, l2.EndPoint, eps)) continue;

                    double a1s = ip.DistanceTo(l1.StartPoint);
                    double a1e = ip.DistanceTo(l1.EndPoint);
                    double a2s = ip.DistanceTo(l2.StartPoint);
                    double a2e = ip.DistanceTo(l2.EndPoint);
                    double maxArm = Math.Max(Math.Max(a1s, a1e), Math.Max(a2s, a2e));
                    if (maxArm < minArmToApply) continue;

                    TrimShortArmIfPenetrating(l1, ip, minPenetration, maxShortArm, shortLongRatio);
                    TrimShortArmIfPenetrating(l2, ip, minPenetration, maxShortArm, shortLongRatio);
                }
            }
        }

        private static void TrimShortArmIfPenetrating(
            Line line,
            Point3d ip,
            double minPenetration,
            double maxShortArm,
            double shortLongRatio)
        {
            double ds = line.StartPoint.DistanceTo(ip);
            double de = line.EndPoint.DistanceTo(ip);
            if (ds <= 1e-9 || de <= 1e-9) return;

            bool trimStart = ds < de;
            double shortArm = trimStart ? ds : de;
            double longArm = trimStart ? de : ds;

            if (shortArm < minPenetration) return;
            if (shortArm > maxShortArm) return;
            if (shortArm > longArm * shortLongRatio) return;

            if (trimStart) line.StartPoint = ip;
            else line.EndPoint = ip;
        }

        private static List<(ObjectId Id, int OwnerKey)> RemoveShortBeamLines(
            Transaction tr,
            Database db,
            List<(ObjectId Id, int OwnerKey)> lineRefs)
        {
            // Rule 1: Under 22 units → delete only if parallel adjacent beam exists. Rule 2: Under 5 units → always delete.
            const double minLengthAlwaysDelete = 5.0;
            const double minLengthConditional = 22.0;
            const double parallelDot = 0.99;
            const double adjEps = 0.02;

            var beamLines = new List<(ObjectId Id, int OwnerKey, Line L)>();
            foreach (var lineRef in lineRefs)
            {
                if (lineRef.Id.IsNull || lineRef.Id.IsErased) continue;
                if (!(tr.GetObject(lineRef.Id, OpenMode.ForRead, false) is Line ln)) continue;
                beamLines.Add((lineRef.Id, lineRef.OwnerKey, ln));
            }

            var toErase = new List<ObjectId>();
            var beamLinesForAdj = beamLines.ConvertAll(x => (x.Id, x.L));
            for (int i = 0; i < beamLines.Count; i++)
            {
                var (id, ownerKey, ln) = beamLines[i];
                if (ln.Length < minLengthAlwaysDelete) { toErase.Add(id); continue; }
                if (ln.Length >= minLengthConditional) continue;
                if (HasParallelAdjacentBeamLine(ln, id, beamLinesForAdj, i, parallelDot, adjEps))
                    toErase.Add(id);
            }

            foreach (var id in toErase)
            {
                if (id.IsErased) continue;
                var ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                ent?.Erase();
            }

            var result = new List<(ObjectId Id, int OwnerKey)>();
            foreach (var (id, ownerKey, _) in beamLines)
                if (!toErase.Contains(id) && !id.IsErased) result.Add((id, ownerKey));
            return result;
        }

        private static void RemoveShortBeamLinesFromBlock(Transaction tr, Database db, Editor ed)
        {
            // Rule 1: Under 22 units → delete only if there is a parallel adjacent beam line.
            // Rule 2: Under 5 units → always delete.
            const double minLengthAlwaysDelete = 5.0;   // 5 mm equivalent
            const double minLengthConditional = 22.0;   // 220 mm equivalent
            const double parallelDot = 0.99;
            const double adjEps = 0.02;
            var layerNames = new[] { "ST4-KIRISLAR", "ST4-AKS-KOLONLU", "ST4-AKS-KOLONSUZ" };

            for (int pass = 0; pass < 2; pass++)
            {
                var beamLines = new List<(ObjectId Id, Line L)>();
                foreach (var layerName in layerNames)
                {
                    var filter = new SelectionFilter(new[]
                    {
                        new TypedValue((int)DxfCode.Start, "LINE"),
                        new TypedValue((int)DxfCode.LayerName, layerName)
                    });
                    var selRes = ed.SelectAll(filter);
                    if (selRes.Status != PromptStatus.OK) continue;
                    foreach (ObjectId id in selRes.Value.GetObjectIds())
                    {
                        if (id.IsNull || id.IsErased) continue;
                        if (!(tr.GetObject(id, OpenMode.ForRead, false) is Line ln)) continue;
                        beamLines.Add((id, ln));
                    }
                }

                var toErase = new List<ObjectId>();
                for (int i = 0; i < beamLines.Count; i++)
                {
                    var (id, ln) = beamLines[i];
                    if (ln.Length < minLengthAlwaysDelete)
                    {
                        toErase.Add(id);
                        continue;
                    }
                    if (ln.Length >= minLengthConditional) continue;
                    if (HasParallelAdjacentBeamLine(ln, id, beamLines, i, parallelDot, adjEps))
                        toErase.Add(id);
                }

                foreach (var id in toErase)
                {
                    if (id.IsErased) continue;
                    var ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                    ent?.Erase();
                }
            }
        }

        private static bool HasParallelAdjacentBeamLine(
            Line ln,
            ObjectId currentId,
            List<(ObjectId Id, Line L)> beamLines,
            int currentIndex,
            double parallelDot,
            double adjEps)
        {
            if (ln.Length <= 1e-9) return false;
            var dir = (ln.EndPoint - ln.StartPoint).GetNormal();
            var p1 = ln.StartPoint;
            var p2 = ln.EndPoint;

            for (int j = 0; j < beamLines.Count; j++)
            {
                if (j == currentIndex) continue;
                var (otherId, other) = beamLines[j];
                if (otherId == currentId || other.IsErased) continue;
                if (other.Length <= 1e-9) continue;

                var otherDir = (other.EndPoint - other.StartPoint).GetNormal();
                if (Math.Abs(dir.DotProduct(otherDir)) < parallelDot) continue;

                bool adjacent = IsPointAdjacentToSegment2d(p1, other.StartPoint, other.EndPoint, adjEps)
                             || IsPointAdjacentToSegment2d(p2, other.StartPoint, other.EndPoint, adjEps);
                if (adjacent) return true;
            }
            return false;
        }

        private static bool IsPointAdjacentToSegment2d(Point3d p, Point3d a, Point3d b, double eps)
        {
            if (p.DistanceTo(a) <= eps || p.DistanceTo(b) <= eps) return true;
            return IsPointOnSegment2d(p, a, b, eps);
        }

        private static List<(int OwnerKey, Point2d[] Poly)> BuildBeamBodiesFromDrawnLineBands(
            Transaction tr,
            List<(ObjectId Id, int OwnerKey)> lineRefs)
        {
            var byOwner = new Dictionary<int, List<Line>>();
            foreach (var lineRef in lineRefs)
            {
                if (lineRef.Id.IsNull || lineRef.Id.IsErased) continue;
                if (!(tr.GetObject(lineRef.Id, OpenMode.ForRead, false) is Line ln)) continue;
                if (ln.Length <= 1e-9) continue;

                if (!byOwner.TryGetValue(lineRef.OwnerKey, out var list))
                {
                    list = new List<Line>();
                    byOwner[lineRef.OwnerKey] = list;
                }
                list.Add(ln);
            }

            var bodies = new List<(int OwnerKey, Point2d[] Poly)>();
            foreach (var kv in byOwner)
            {
                var lines = kv.Value;
                if (lines.Count < 2) continue;

                // Dominant axis from longest currently drawn segment.
                var longest = lines.OrderByDescending(l => l.Length).First();
                var u3 = (longest.EndPoint - longest.StartPoint).GetNormal();
                var u = new Vector2d(u3.X, u3.Y);
                if (u.Length <= 1e-9) continue;
                var v = new Vector2d(-u.Y, u.X);

                // Keep only lines roughly parallel to beam axis.
                var parallel = new List<(double S0, double S1, double Tm)>();
                foreach (var ln in lines)
                {
                    var d3 = (ln.EndPoint - ln.StartPoint).GetNormal();
                    var d = new Vector2d(d3.X, d3.Y);
                    if (Math.Abs(d.DotProduct(u)) < 0.97) continue;

                    double s0 = ln.StartPoint.X * u.X + ln.StartPoint.Y * u.Y;
                    double s1 = ln.EndPoint.X * u.X + ln.EndPoint.Y * u.Y;
                    if (s1 < s0) (s0, s1) = (s1, s0);
                    double t0 = ln.StartPoint.X * v.X + ln.StartPoint.Y * v.Y;
                    double t1 = ln.EndPoint.X * v.X + ln.EndPoint.Y * v.Y;
                    double tm = (t0 + t1) * 0.5;
                    parallel.Add((s0, s1, tm));
                }
                if (parallel.Count < 2) continue;

                var tValues = parallel.Select(x => x.Tm).OrderBy(x => x).ToList();
                double median = tValues[tValues.Count / 2];
                var low = parallel.Where(x => x.Tm <= median).ToList();
                var high = parallel.Where(x => x.Tm > median).ToList();
                if (low.Count == 0 || high.Count == 0)
                {
                    int half = parallel.Count / 2;
                    low = parallel.OrderBy(x => x.Tm).Take(half).ToList();
                    high = parallel.OrderBy(x => x.Tm).Skip(half).ToList();
                }
                if (low.Count == 0 || high.Count == 0) continue;

                double tLow = low.Average(x => x.Tm);
                double tHigh = high.Average(x => x.Tm);
                if (Math.Abs(tHigh - tLow) <= 1e-6) continue;
                if (tLow > tHigh) (tLow, tHigh) = (tHigh, tLow);

                var lowMerged = MergeIntervals(low.Select(x => (x.S0, x.S1)).ToList(), 0.5);
                var highMerged = MergeIntervals(high.Select(x => (x.S0, x.S1)).ToList(), 0.5);

                Point2d P(double s, double t) => new Point2d(u.X * s + v.X * t, u.Y * s + v.Y * t);
                foreach (var a in lowMerged)
                {
                    foreach (var b in highMerged)
                    {
                        double s0 = Math.Max(a.S, b.S);
                        double s1 = Math.Min(a.E, b.E);
                        if (s1 - s0 <= 2.0) continue;
                        var poly = new[]
                        {
                            P(s0, tHigh),
                            P(s1, tHigh),
                            P(s1, tLow),
                            P(s0, tLow)
                        };
                        bodies.Add((kv.Key, poly));
                    }
                }
            }

            return bodies;
        }

        private static List<(ObjectId Id, int OwnerKey)> CleanupTinyBeamArtifacts(
            Transaction tr,
            List<(ObjectId Id, int OwnerKey)> lineRefs,
            List<(int OwnerKey, Point2d[] Poly)> beamBodies)
        {
            const double maxArtifactLen = 1.0; // cm
            const double nearBeamTol = 0.35;   // cm
            var result = new List<(ObjectId Id, int OwnerKey)>();

            foreach (var lineRef in lineRefs)
            {
                var id = lineRef.Id;
                if (id.IsNull || id.IsErased) continue;
                if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Line ln)) continue;

                if (ln.Length > maxArtifactLen)
                {
                    result.Add(lineRef);
                    continue;
                }

                var mid = new Point2d((ln.StartPoint.X + ln.EndPoint.X) * 0.5, (ln.StartPoint.Y + ln.EndPoint.Y) * 0.5);
                bool erase = false;
                foreach (var body in beamBodies)
                {
                    if (body.OwnerKey == lineRef.OwnerKey) continue;
                    if (PointInPolygonWithTol(mid, body.Poly, nearBeamTol))
                    {
                        erase = true;
                        break;
                    }
                }

                if (erase)
                {
                    ln.Erase();
                }
                else
                {
                    result.Add(lineRef);
                }
            }

            return result;
        }

        private static List<(ObjectId Id, int OwnerKey)> CleanupBeamLineCrossingSegments(
            Transaction tr,
            BlockTableRecord btr,
            List<(ObjectId Id, int OwnerKey)> lineRefs,
            List<(int OwnerKey, Point2d[] Poly)> beamBodies)
        {
            const double minKeep = 2.0;
            const double crossingTol = 0.5; // cm, same outward trim tolerance
            var result = new List<(ObjectId Id, int OwnerKey)>();

            foreach (var lineRef in lineRefs)
            {
                var id = lineRef.Id;
                if (id.IsNull || id.IsErased) continue;
                if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Line ln)) continue;

                var a = new Point2d(ln.StartPoint.X, ln.StartPoint.Y);
                var b = new Point2d(ln.EndPoint.X, ln.EndPoint.Y);
                double len = a.GetDistanceTo(b);
                if (len <= 1e-9) { ln.Erase(); continue; }
                var dir = (b - a).GetNormal();

                var intervals = new List<(double S, double E)>();
                foreach (var body in beamBodies)
                {
                    if (body.OwnerKey == lineRef.OwnerKey) continue;
                    intervals.AddRange(SegmentInsideIntervalsInPolygon(a, b, body.Poly, crossingTol));
                }

                var merged = MergeIntervals(intervals, 0.5);
                if (merged.Count == 0)
                {
                    result.Add(lineRef);
                    continue;
                }

                var keeps = InvertIntervals(merged, len, minKeep);
                ln.Erase();
                foreach (var keep in keeps)
                {
                    var p1 = a + dir.MultiplyBy(keep.S);
                    var p2 = a + dir.MultiplyBy(keep.E);
                    if (p1.GetDistanceTo(p2) < minKeep) continue;
                    var newLine = new Line(new Point3d(p1.X, p1.Y, 0), new Point3d(p2.X, p2.Y, 0))
                    {
                        Layer = "ST4-KIRISLAR"
                    };
                    AppendEntity(tr, btr, newLine);
                    result.Add((newLine.ObjectId, lineRef.OwnerKey));
                }
            }

            return result;
        }

        private static void BridgeSmallCollinearBeamGaps(Transaction tr, List<(ObjectId Id, int OwnerKey)> lineRefs)
        {
            const double gapTol = 1.0;         // cm
            const double collinearTol = 0.2;   // cm
            const double parallelDotMin = 0.999;

            var lines = new List<Line>(lineRefs.Count);
            foreach (var lineRef in lineRefs)
            {
                if (lineRef.Id.IsNull || lineRef.Id.IsErased) continue;
                if (!(tr.GetObject(lineRef.Id, OpenMode.ForWrite, false) is Line ln)) continue;
                if (ln.Length <= 1e-9) continue;
                lines.Add(ln);
            }

            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    var l1 = lines[i];
                    var l2 = lines[j];
                    var d1 = (l1.EndPoint - l1.StartPoint).GetNormal();
                    var d2 = (l2.EndPoint - l2.StartPoint).GetNormal();
                    if (Math.Abs(d1.DotProduct(d2)) < parallelDotMin) continue;

                    // Check if lines are on the same infinite line (approximately).
                    if (PointToInfiniteLineDistance(l1.StartPoint, l2.StartPoint, l2.EndPoint) > collinearTol) continue;
                    if (PointToInfiniteLineDistance(l1.EndPoint, l2.StartPoint, l2.EndPoint) > collinearTol) continue;

                    // Connect the nearest pair of endpoints if gap is tiny.
                    var p1 = l1.StartPoint;
                    var p2 = l1.EndPoint;
                    var q1 = l2.StartPoint;
                    var q2 = l2.EndPoint;
                    var candidates = new[]
                    {
                        (A: p1, B: q1, I: 0, J: 0, D: p1.DistanceTo(q1)),
                        (A: p1, B: q2, I: 0, J: 1, D: p1.DistanceTo(q2)),
                        (A: p2, B: q1, I: 1, J: 0, D: p2.DistanceTo(q1)),
                        (A: p2, B: q2, I: 1, J: 1, D: p2.DistanceTo(q2))
                    };
                    var best = candidates.OrderBy(x => x.D).First();
                    if (best.D > gapTol) continue;

                    var m = new Point3d((best.A.X + best.B.X) * 0.5, (best.A.Y + best.B.Y) * 0.5, 0);
                    if (best.I == 0) l1.StartPoint = m; else l1.EndPoint = m;
                    if (best.J == 0) l2.StartPoint = m; else l2.EndPoint = m;
                }
            }
        }

        private static void SnapBeamEndpointsToNearbySegments(Transaction tr, List<(ObjectId Id, int OwnerKey)> lineRefs)
        {
            const double joinTol = 5.0; // cm
            var lines = new List<Line>(lineRefs.Count);
            foreach (var lineRef in lineRefs)
            {
                if (lineRef.Id.IsNull || lineRef.Id.IsErased) continue;
                if (!(tr.GetObject(lineRef.Id, OpenMode.ForWrite, false) is Line ln)) continue;
                if (ln.Length <= 1e-9) continue;
                lines.Add(ln);
            }

            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                var pStart = l.StartPoint;
                var pEnd = l.EndPoint;
                if (TrySnapPointToNearbySegment(lines, i, pStart, joinTol, out var snappedStart))
                {
                    l.StartPoint = snappedStart;
                }
                if (TrySnapPointToNearbySegment(lines, i, pEnd, joinTol, out var snappedEnd))
                {
                    l.EndPoint = snappedEnd;
                }
            }
        }

        private static bool TrySnapPointToNearbySegment(List<Line> lines, int selfIndex, Point3d p, double tol, out Point3d snapped)
        {
            snapped = p;
            bool found = false;
            double best = tol;

            for (int j = 0; j < lines.Count; j++)
            {
                if (j == selfIndex) continue;
                var other = lines[j];
                if (TryClosestPointOnSegment(p, other.StartPoint, other.EndPoint, out var q))
                {
                    double d = p.DistanceTo(q);
                    if (d <= best)
                    {
                        best = d;
                        snapped = q;
                        found = true;
                    }
                }
            }

            return found;
        }

        private static bool TryClosestPointOnSegment(Point3d p, Point3d a, Point3d b, out Point3d q)
        {
            q = a;
            var ab = b - a;
            double len2 = ab.DotProduct(ab);
            if (len2 <= 1e-12) return false;
            double t = (p - a).DotProduct(ab) / len2;
            t = Math.Max(0.0, Math.Min(1.0, t));
            q = new Point3d(a.X + ab.X * t, a.Y + ab.Y * t, 0.0);
            return true;
        }

        private static void CutTJunctionEndpointOverhangs(Transaction tr, List<(ObjectId Id, int OwnerKey)> lineRefs)
        {
            const double eps = 1e-6;
            const double maxCut = 2.5;            // cm
            const double tJunctionDotMax = 0.35;  // near-perpendicular

            var lines = new List<Line>(lineRefs.Count);
            foreach (var lineRef in lineRefs)
            {
                if (lineRef.Id.IsNull || lineRef.Id.IsErased) continue;
                if (!(tr.GetObject(lineRef.Id, OpenMode.ForWrite, false) is Line ln)) continue;
                if (ln.Length <= eps) continue;
                lines.Add(ln);
            }

            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                TryCutOneEndpoint(lines, i, true, maxCut, tJunctionDotMax, eps);
                TryCutOneEndpoint(lines, i, false, maxCut, tJunctionDotMax, eps);
            }
        }

        private static void TryCutOneEndpoint(
            List<Line> lines,
            int lineIndex,
            bool isStart,
            double maxCut,
            double tJunctionDotMax,
            double eps)
        {
            var l = lines[lineIndex];
            Point3d endpoint = isStart ? l.StartPoint : l.EndPoint;
            Point3d otherEnd = isStart ? l.EndPoint : l.StartPoint;
            var inwardDir = (otherEnd - endpoint).GetNormal();

            Point3d? bestCutPoint = null;
            double bestCut = double.MaxValue;

            for (int j = 0; j < lines.Count; j++)
            {
                if (j == lineIndex) continue;
                var other = lines[j];
                var otherDir = (other.EndPoint - other.StartPoint).GetNormal();

                // Target T-junctions: mostly perpendicular interactions.
                if (Math.Abs(inwardDir.DotProduct(otherDir)) > tJunctionDotMax) continue;

                if (!TryIntersectInfinite2d(l.StartPoint, l.EndPoint, other.StartPoint, other.EndPoint, out Point3d ip)) continue;
                if (!IsPointOnSegment2d(ip, other.StartPoint, other.EndPoint, eps)) continue;

                var v = ip - endpoint;
                double cutLen = v.DotProduct(inwardDir);

                // Cut only inward, never extend.
                if (cutLen <= eps || cutLen > maxCut) continue;
                if (cutLen < bestCut)
                {
                    bestCut = cutLen;
                    bestCutPoint = ip;
                }
            }

            if (bestCutPoint.HasValue)
            {
                if (isStart) l.StartPoint = bestCutPoint.Value;
                else l.EndPoint = bestCutPoint.Value;
            }
        }

        private static void ExtendBeamEndpointsToNearestCrossings(Transaction tr, List<(ObjectId Id, int OwnerKey)> lineRefs)
        {
            const double eps = 1e-6;
            const double maxExtend = 2.5; // cm, local closure only
            const double maxParallelDot = 0.35; // prefer T-junctions, avoid near-parallel snaps
            var lines = new List<Line>(lineRefs.Count);
            foreach (var lineRef in lineRefs)
            {
                if (lineRef.Id.IsNull || lineRef.Id.IsErased) continue;
                if (!(tr.GetObject(lineRef.Id, OpenMode.ForWrite, false) is Line ln)) continue;
                if (ln.Length <= eps) continue;
                lines.Add(ln);
            }

            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                var d = (l.EndPoint - l.StartPoint).GetNormal();
                var newStart = FindNearestCrossingOnRay(l.StartPoint, -d, d, l, i, lines, eps, maxExtend, maxParallelDot);
                var newEnd = FindNearestCrossingOnRay(l.EndPoint, d, d, l, i, lines, eps, maxExtend, maxParallelDot);
                if (newStart.HasValue) l.StartPoint = newStart.Value;
                if (newEnd.HasValue) l.EndPoint = newEnd.Value;
            }
        }

        private static Point3d? FindNearestCrossingOnRay(
            Point3d origin,
            Vector3d rayDir,
            Vector3d baseDir,
            Line baseLine,
            int baseIndex,
            List<Line> lines,
            double eps,
            double maxExtend,
            double maxParallelDot)
        {
            Point3d? bestPoint = null;
            double bestDist = double.MaxValue;

            for (int j = 0; j < lines.Count; j++)
            {
                if (j == baseIndex) continue;
                var other = lines[j];
                var otherDir = (other.EndPoint - other.StartPoint).GetNormal();
                if (Math.Abs(baseDir.DotProduct(otherDir)) > maxParallelDot) continue;

                if (!TryIntersectInfinite2d(baseLine.StartPoint, baseLine.EndPoint, other.StartPoint, other.EndPoint, out var ip))
                    continue;
                if (!IsPointOnSegment2d(ip, other.StartPoint, other.EndPoint, eps))
                    continue;

                var v = ip - origin;
                double along = v.DotProduct(rayDir);
                if (along <= eps) continue;
                if (along > maxExtend) continue;
                if (along < bestDist)
                {
                    bestDist = along;
                    bestPoint = ip;
                }
            }

            return bestPoint;
        }

        private static bool IsPointOnSegment2d(Point3d p, Point3d a, Point3d b, double eps)
        {
            var ab = b - a;
            var ap = p - a;
            double len2 = ab.DotProduct(ab);
            if (len2 <= eps) return false;
            double cross = Math.Abs(ap.X * ab.Y - ap.Y * ab.X);
            if (cross > eps * Math.Sqrt(len2)) return false;
            double t = ap.DotProduct(ab) / len2;
            return t >= -eps && t <= 1.0 + eps;
        }

        private static double PointToInfiniteLineDistance(Point3d p, Point3d a, Point3d b)
        {
            var ab = b - a;
            double len = ab.Length;
            if (len <= 1e-12) return p.DistanceTo(a);
            var ap = p - a;
            double area2 = Math.Abs(ap.X * ab.Y - ap.Y * ab.X);
            return area2 / len;
        }

        private static bool IntersectionNearSegment(Point3d a, Point3d b, Point3d p, double extendTol)
        {
            var ab = b - a;
            double len2 = ab.DotProduct(ab);
            if (len2 <= 1e-12) return false;
            double len = Math.Sqrt(len2);
            double t = (p - a).DotProduct(ab) / len2;
            double tolT = extendTol / len;
            return t >= -tolT && t <= 1.0 + tolT;
        }

        private static bool TryIntersectInfinite2d(Point3d a1, Point3d a2, Point3d b1, Point3d b2, out Point3d ip)
        {
            ip = default;
            var r = new Vector2d(a2.X - a1.X, a2.Y - a1.Y);
            var s = new Vector2d(b2.X - b1.X, b2.Y - b1.Y);
            double den = Cross(r, s);
            if (Math.Abs(den) <= 1e-12) return false;

            var qmp = new Vector2d(b1.X - a1.X, b1.Y - a1.Y);
            double t = Cross(qmp, s) / den;
            ip = new Point3d(a1.X + r.X * t, a1.Y + r.Y * t, 0.0);
            return true;
        }

        private static (Point2d A, Point2d B)? PreTrimBeamEdgeEnds(Point2d a, Point2d b, List<Point2d[]> blockersOutward, List<Point2d[]> blockersInward)
        {
            double len = a.GetDistanceTo(b);
            if (len <= 1e-9) return null;

            var dir = (b - a).GetNormal();
            const double outwardTol = 0.5; // cm
            const double inwardTol = -0.5; // cm
            var intervals = new List<(double S, double E)>();
            foreach (var poly in blockersOutward)
            {
                intervals.AddRange(SegmentInsideIntervalsInPolygon(a, b, poly, outwardTol));
            }
            foreach (var poly in blockersInward)
            {
                intervals.AddRange(SegmentInsideIntervalsInPolygon(a, b, poly, inwardTol));
            }

            var merged = MergeIntervals(intervals, 0.5);
            if (merged.Count == 0) return (a, b);

            double start = 0.0;
            double end = len;
            const double trimGap = 0.2;
            const double endWindow = 40.0; // trim only if intersection is near endpoints

            // Trim once from the start if the first blocker intersection is near start.
            var first = merged[0];
            if (first.S <= endWindow)
            {
                start = Math.Min(len, first.E + trimGap);
            }

            // Trim once from the end if the last blocker intersection is near end.
            var last = merged[merged.Count - 1];
            if (len - last.E <= endWindow)
            {
                end = Math.Max(0.0, last.S - trimGap);
            }

            if (end - start <= 2.0) return null;

            var ta = a + dir.MultiplyBy(start);
            var tb = a + dir.MultiplyBy(end);
            return (ta, tb);
        }

        private static List<(double S, double E)> SegmentInsideIntervalsInPolygon(Point2d a, Point2d b, Point2d[] poly, double tol)
        {
            var tVals = new List<double> { 0.0, 1.0 };
            Vector2d d = b - a;
            double len2 = d.DotProduct(d);
            if (len2 <= 1e-12) return new List<(double, double)>();

            for (int i = 0; i < poly.Length; i++)
            {
                Point2d p = poly[i];
                Point2d q = poly[(i + 1) % poly.Length];
                if (TrySegmentSegmentT(a, b, p, q, out double t))
                {
                    tVals.Add(Math.Max(0.0, Math.Min(1.0, t)));
                }

                Vector2d w = p - a;
                double tProj = w.DotProduct(d) / len2;
                if (tProj >= -1e-6 && tProj <= 1.0 + 1e-6)
                {
                    tVals.Add(Math.Max(0.0, Math.Min(1.0, tProj)));
                }
            }

            var uniq = tVals.OrderBy(x => x).ToList();
            var dedup = new List<double>();
            foreach (var t in uniq)
            {
                if (dedup.Count == 0 || Math.Abs(t - dedup[dedup.Count - 1]) > 1e-6)
                {
                    dedup.Add(t);
                }
            }

            double len = Math.Sqrt(len2);
            var insideIntervals = new List<(double S, double E)>();
            for (int i = 0; i < dedup.Count - 1; i++)
            {
                double t0 = dedup[i];
                double t1 = dedup[i + 1];
                if (t1 - t0 <= 1e-8) continue;
                double tm = (t0 + t1) * 0.5;
                Point2d m = a + d.MultiplyBy(tm);
                if (PointInPolygonWithTol(m, poly, tol))
                {
                    insideIntervals.Add((len * t0, len * t1));
                }
            }
            return insideIntervals;
        }

        private static bool TrySegmentSegmentT(Point2d a, Point2d b, Point2d c, Point2d d, out double t)
        {
            t = 0.0;
            Vector2d r = b - a;
            Vector2d s = d - c;
            Vector2d qp = c - a;
            double den = Cross(r, s);
            if (Math.Abs(den) <= 1e-12) return false;
            double tt = Cross(qp, s) / den;
            double uu = Cross(qp, r) / den;
            if (tt >= -1e-9 && tt <= 1.0 + 1e-9 && uu >= -1e-9 && uu <= 1.0 + 1e-9)
            {
                t = tt;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Find closest intersection of ray (origin + t*dir, t>0) with polygon edges. dir should be unit length.
        /// Returns the hit point or null if none.
        /// </summary>
        private static Point2d? RayPolygonClosestHit(Point2d origin, Vector2d dir, List<Point2d[]> polygons, double maxDist = 1e4)
        {
            var end = origin + dir.MultiplyBy(maxDist);
            Point2d? bestPoint = null;
            double bestT = double.MaxValue;
            foreach (var poly in polygons)
            {
                for (int i = 0; i < poly.Length; i++)
                {
                    var c = poly[i];
                    var e = poly[(i + 1) % poly.Length];
                    if (TrySegmentSegmentT(origin, end, c, e, out double t) && t > 1e-8 && t < bestT)
                    {
                        bestT = t;
                        bestPoint = origin + (end - origin).MultiplyBy(t);
                    }
                }
            }
            return bestPoint;
        }

        /// <summary>
        /// Ham perde polyline çizilir; perde + kolon poligonları baz alınarak Region subtract ile boundary alınır;
        /// ilk perde kaldırılır, net boundary polyline(lar) kalır. Başarılı ise true ve oluşan polyline Id'leri döner.
        /// </summary>
        private static bool TryDrawWallAsBoundary(
            Transaction tr,
            BlockTableRecord btr,
            Point2d[] rawWallPoly,
            List<Point2d[]> columnOnlyBlockers,
            out List<ObjectId> resultPolyIds)
        {
            resultPolyIds = new List<ObjectId>();
            ObjectId wallPlId = ObjectId.Null;
            var toErase = new List<ObjectId>();
            try
            {
                var wallPl = ToPolyline(rawWallPoly, true);
                wallPl.Layer = "ST4-PERDELER";
                btr.AppendEntity(wallPl);
                tr.AddNewlyCreatedDBObject(wallPl, true);
                wallPlId = wallPl.ObjectId;

                var wallCurves = new DBObjectCollection();
                wallCurves.Add(tr.GetObject(wallPlId, OpenMode.ForRead));
                var wallRegionColl = Region.CreateFromCurves(wallCurves);
                Region wallRegion = null;
                if (wallRegionColl != null && wallRegionColl.Count > 0 && wallRegionColl[0] is Region r)
                    wallRegion = r;
                if (wallRegion == null) return false;
                wallRegion.Layer = "ST4-PERDELER";
                btr.AppendEntity(wallRegion);
                tr.AddNewlyCreatedDBObject(wallRegion, true);
                toErase.Add(wallPlId);

                using (var wallRegObj = tr.GetObject(wallRegion.ObjectId, OpenMode.ForWrite) as Region)
                {
                    if (wallRegObj == null) return false;
                    foreach (var colPoly in columnOnlyBlockers)
                    {
                        var colPl = ToPolyline(colPoly, true);
                        colPl.Layer = "0";
                        btr.AppendEntity(colPl);
                        tr.AddNewlyCreatedDBObject(colPl, true);
                        var colCurves = new DBObjectCollection();
                        colCurves.Add(tr.GetObject(colPl.ObjectId, OpenMode.ForRead));
                        var colRegionColl = Region.CreateFromCurves(colCurves);
                        Region colRegion = null;
                        if (colRegionColl != null && colRegionColl.Count > 0 && colRegionColl[0] is Region cr)
                            colRegion = cr;
                        if (colRegion == null) continue;
                        btr.AppendEntity(colRegion);
                        tr.AddNewlyCreatedDBObject(colRegion, true);
                        toErase.Add(colPl.ObjectId);
                        toErase.Add(colRegion.ObjectId);
                        wallRegObj.BooleanOperation(BooleanOperationType.BoolSubtract, colRegion);
                    }

                    var exploded = new DBObjectCollection();
                    wallRegObj.Explode(exploded);
                    toErase.Add(wallRegObj.ObjectId);

                    var explodedIds = new List<ObjectId>();
                    foreach (DBObject dbObj in exploded)
                    {
                        var ent = dbObj as Entity;
                        if (ent != null)
                        {
                            btr.AppendEntity(ent);
                            tr.AddNewlyCreatedDBObject(ent, true);
                            explodedIds.Add(ent.ObjectId);
                        }
                    }

                    var boundaryPolys = BuildPolylinesFromExplodedEdges(exploded);
                    foreach (var pts in boundaryPolys)
                    {
                        if (pts.Count < 3) continue;
                        var pl = ToPolyline(pts, true);
                        pl.Layer = "ST4-PERDELER";
                        btr.AppendEntity(pl);
                        tr.AddNewlyCreatedDBObject(pl, true);
                        resultPolyIds.Add(pl.ObjectId);
                    }

                    foreach (var id in explodedIds)
                    {
                        if (!id.IsErased)
                        {
                            (tr.GetObject(id, OpenMode.ForWrite, false) as Entity)?.Erase();
                        }
                    }
                }

                foreach (var id in toErase)
                {
                    if (!id.IsNull && !id.IsErased)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                        ent?.Erase();
                    }
                }

                return resultPolyIds.Count > 0;
            }
            catch
            {
                foreach (var id in toErase)
                {
                    if (!id.IsNull && !id.IsErased)
                    {
                        try { (tr.GetObject(id, OpenMode.ForWrite, false) as Entity)?.Erase(); } catch { }
                    }
                }
                return false;
            }
        }

        private static List<List<Point2d>> BuildPolylinesFromExplodedEdges(DBObjectCollection exploded)
        {
            var edges = new List<(Point2d Start, Point2d End, double Bulge)>();
            foreach (DBObject dbObj in exploded)
            {
                if (dbObj is Line ln)
                    edges.Add((new Point2d(ln.StartPoint.X, ln.StartPoint.Y), new Point2d(ln.EndPoint.X, ln.EndPoint.Y), 0));
                else if (dbObj is Arc arc)
                {
                    var start = new Point2d(arc.StartPoint.X, arc.StartPoint.Y);
                    var end = new Point2d(arc.EndPoint.X, arc.EndPoint.Y);
                    double bulge = Math.Tan((arc.EndAngle - arc.StartAngle) * 0.25);
                    if (arc.Normal.Z < 0) bulge = -bulge;
                    edges.Add((start, end, bulge));
                }
            }
            var loops = new List<List<Point2d>>();
            const double eps = 1e-6;
            while (edges.Count > 0)
            {
                var loop = new List<Point2d>();
                var first = edges[0];
                edges.RemoveAt(0);
                loop.Add(first.Start);
                var cur = first.End;
                loop.Add(cur);
                while (edges.Count > 0)
                {
                    int idx = -1;
                    for (int i = 0; i < edges.Count; i++)
                    {
                        if (cur.GetDistanceTo(edges[i].Start) <= eps) { cur = edges[i].End; idx = i; break; }
                        if (cur.GetDistanceTo(edges[i].End) <= eps) { cur = edges[i].Start; idx = i; break; }
                    }
                    if (idx < 0) break;
                    if (cur.GetDistanceTo(first.Start) <= eps) { edges.RemoveAt(idx); break; }
                    loop.Add(cur);
                    edges.RemoveAt(idx);
                }
                if (loop.Count >= 3) loops.Add(loop);
            }
            return loops;
        }

        private static Point2d[] GetPolylinePoints(Transaction tr, ObjectId plId)
        {
            if (plId.IsNull || plId.IsErased) return new Point2d[0];
            if (!(tr.GetObject(plId, OpenMode.ForRead, false) is Polyline pl)) return new Point2d[0];
            var pts = new Point2d[pl.NumberOfVertices];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = pl.GetPoint2dAt(i);
            return pts;
        }

        /// <summary>
        /// Segment (a,b) doğru parçasını kolonların dışında kalan kısımlara kırpar.
        /// Kolon kesiti/poligon kesiti içinde hiçbir perde çizgisi kalmaz.
        /// </summary>
        private static List<Point2d> ClipSegmentToOutsideColumns(Point2d a, Point2d b, List<Point2d[]> columnPolygons)
        {
            const double outwardTol = 0.5;
            const double minKeep = 0.5;
            double len = a.GetDistanceTo(b);
            if (len <= 1e-9) return new List<Point2d> { a };

            var intervals = new List<(double S, double E)>();
            foreach (var poly in columnPolygons)
                intervals.AddRange(SegmentInsideIntervalsInPolygon(a, b, poly, outwardTol));
            var merged = MergeIntervals(intervals, 0.5);
            var keeps = InvertIntervals(merged, len, minKeep);

            var dists = new List<double> { 0.0, len };
            foreach (var k in keeps)
            {
                if (k.S > 1e-6 && k.S < len - 1e-6) dists.Add(k.S);
                if (k.E > 1e-6 && k.E < len - 1e-6) dists.Add(k.E);
            }
            dists = dists.OrderBy(x => x).ToList();
            var dedup = new List<double>();
            foreach (var d in dists)
            {
                if (dedup.Count == 0 || Math.Abs(d - dedup[dedup.Count - 1]) > 1e-6)
                    dedup.Add(d);
            }

            var dir = (b - a);
            var pts = new List<Point2d>();
            for (int i = 0; i < dedup.Count; i++)
                pts.Add(a + dir.MultiplyBy(dedup[i] / len)); // dedup[i] = distance from a
            return pts;
        }

        /// <summary>
        /// Perde poligonunu oluşturur: uzun kenarlar zaten kırpılmış (T1-T2, T4-T3).
        /// Uç kenarlar (T1-T4, T2-T3) kolon dışında kalacak şekilde kırpılır; kolon içinde hiçbir perde çizgisi kalmaz.
        /// </summary>
        private static List<Point2d> BuildWallPolygonOutsideColumns(Point2d T1, Point2d T2, Point2d T3, Point2d T4, List<Point2d[]> columnOnlyBlockers)
        {
            var leftPts = ClipSegmentToOutsideColumns(T1, T4, columnOnlyBlockers);
            var rightPts = ClipSegmentToOutsideColumns(T2, T3, columnOnlyBlockers);
            if (leftPts.Count < 2 || rightPts.Count < 2) return new List<Point2d> { T1, T2, T3, T4 };

            var wallPts = new List<Point2d>(leftPts);
            wallPts.Add(T3);
            for (int i = rightPts.Count - 2; i >= 0; i--)
                wallPts.Add(rightPts[i]);
            return wallPts;
        }

        /// <summary>
        /// Returns intersection parameters t in (0,1) for segment a-b with polygon edges, sorted ascending.
        /// Used to add polygon points at column boundary for clean wall-column miter.
        /// </summary>
        private static List<(double t, Point2d p)> SegmentPolygonIntersections(Point2d a, Point2d b, List<Point2d[]> polygons)
        {
            const double eps = 1e-6;
            var list = new List<(double t, Point2d p)>();
            Vector2d d = b - a;
            foreach (var poly in polygons)
            {
                for (int i = 0; i < poly.Length; i++)
                {
                    var c = poly[i];
                    var e = poly[(i + 1) % poly.Length];
                    if (TrySegmentSegmentT(a, b, c, e, out double t) && t > eps && t < 1.0 - eps)
                    {
                        var p = a + d.MultiplyBy(t);
                        list.Add((t, p));
                    }
                }
            }
            var sorted = list.OrderBy(x => x.t).ToList();
            var dedup = new List<(double t, Point2d p)>();
            foreach (var x in sorted)
            {
                if (dedup.Count == 0 || Math.Abs(dedup[dedup.Count - 1].t - x.t) > eps)
                    dedup.Add(x);
            }
            return dedup;
        }

        private static double Cross(Vector2d a, Vector2d b) => a.X * b.Y - a.Y * b.X;

        private static bool PointInPolygonWithTol(Point2d p, Point2d[] poly, double tol)
        {
            if (tol < 0)
            {
                // Negative tolerance means "inside, but away from boundary" (inward shrink).
                double innerTol = -tol;
                if (!PointInPolygonOrBoundary(p, poly)) return false;
                for (int i = 0; i < poly.Length; i++)
                {
                    var a = poly[i];
                    var b = poly[(i + 1) % poly.Length];
                    if (PointSegmentDistance(p, a, b) <= innerTol) return false;
                }
                return true;
            }

            if (PointInPolygonOrBoundary(p, poly)) return true;
            for (int i = 0; i < poly.Length; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Length];
                if (PointSegmentDistance(p, a, b) <= tol) return true;
            }
            return false;
        }

        private static bool PointInPolygonOrBoundary(Point2d p, Point2d[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var pi = poly[i];
                var pj = poly[j];
                if (PointOnSegment(p, pi, pj, 1e-6)) return true;
                bool intersect = ((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                                 (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / ((pj.Y - pi.Y) + 1e-16) + pi.X);
                if (intersect) inside = !inside;
            }
            return inside;
        }

        private static bool PointOnSegment(Point2d p, Point2d a, Point2d b, double tol)
        {
            Vector2d ap = p - a;
            Vector2d ab = b - a;
            double cross = Math.Abs(Cross(ap, ab));
            if (cross > tol) return false;
            double dot = ap.DotProduct(ab);
            double len2 = ab.DotProduct(ab);
            return dot >= -tol && dot <= len2 + tol;
        }

        private static double PointSegmentDistance(Point2d p, Point2d a, Point2d b)
        {
            Vector2d ab = b - a;
            double len2 = ab.DotProduct(ab);
            if (len2 <= 1e-12) return p.GetDistanceTo(a);
            double t = (p - a).DotProduct(ab) / len2;
            t = Math.Max(0.0, Math.Min(1.0, t));
            Point2d proj = a + ab.MultiplyBy(t);
            return p.GetDistanceTo(proj);
        }

        private static List<(double S, double E)> MergeIntervals(List<(double S, double E)> intervals, double tol)
        {
            if (intervals.Count == 0) return new List<(double S, double E)>();
            var sorted = intervals.OrderBy(x => x.S).ToList();
            var merged = new List<(double S, double E)> { sorted[0] };
            for (int i = 1; i < sorted.Count; i++)
            {
                var last = merged[merged.Count - 1];
                var cur = sorted[i];
                if (cur.S <= last.E + tol)
                {
                    merged[merged.Count - 1] = (last.S, Math.Max(last.E, cur.E));
                }
                else
                {
                    merged.Add(cur);
                }
            }
            return merged;
        }

        private static List<(double S, double E)> InvertIntervals(List<(double S, double E)> merged, double len, double minSeg)
        {
            var keep = new List<(double S, double E)>();
            double cur = 0.0;
            foreach (var iv in merged)
            {
                double s = Math.Max(0.0, iv.S);
                double e = Math.Min(len, iv.E);
                if (s - cur > minSeg) keep.Add((cur, s));
                cur = Math.Max(cur, e);
            }
            if (len - cur > minSeg) keep.Add((cur, len));
            return keep;
        }

        private void DrawColumns(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY)
        {
            foreach (var col in Model.Columns)
            {
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                int sectionId = floor.FloorNo * 100 + col.ColumnNo;
                if (col.ColumnType <= 2 && !Model.ColumnDimsBySectionId.ContainsKey(sectionId)) continue;
                if (col.ColumnType == 3 && !Model.PolygonColumnSectionByPositionSectionId.ContainsKey(sectionId)) continue;

                var dim = Model.ColumnDimsBySectionId.ContainsKey(sectionId) ? Model.ColumnDimsBySectionId[sectionId] : (W: 40.0, H: 40.0);
                double hw = dim.W / 2.0;
                double hh = dim.H / 2.0;

                var offsetLocal = ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                var center = new Point2d(axisNode.X + offsetGlobal.X + offsetX, axisNode.Y + offsetGlobal.Y + offsetY);

                if (col.ColumnType == 2)
                {
                    AppendEntity(tr, btr, new Circle(new Point3d(center.X, center.Y, 0), Vector3d.ZAxis, Math.Max(hw, hh)) { Layer = "ST4-KOLONLAR" });
                }
                else if (col.ColumnType == 3 && TryGetPolygonColumn(sectionId, center, col.AngleDeg, out var polyPoints))
                {
                    var pl = ToPolyline(polyPoints, true);
                    pl.Layer = "ST4-KOLONLAR";
                    AppendEntity(tr, btr, pl);
                }
                else
                {
                    var rect = BuildRect(center, hw, hh, col.AngleDeg);
                    var pl = ToPolyline(rect, true);
                    pl.Layer = "ST4-KOLONLAR";
                    AppendEntity(tr, btr, pl);
                }

                var txt = new Point3d(center.X, center.Y, 0);
                AppendEntity(tr, btr, new DBText
                {
                    Layer = "ST4-KOLON-NUMARALARI",
                    Height = 8,
                    TextString = col.ColumnNo.ToString(CultureInfo.InvariantCulture),
                    Position = txt,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextVerticalMid,
                    AlignmentPoint = txt
                });
            }
        }

        private void DrawLabels(Transaction tr, BlockTableRecord btr, FloorInfo floor, bool drawBeams, double offsetX, double offsetY, (double Xmin, double Xmax, double Ymin, double Ymax) ext)
        {
            for (int i = 0; i < Model.AxisX.Count; i++)
            {
                var x = Model.AxisX[i];
                var p = Math.Abs(x.Slope) <= 1e-9
                    ? new Point3d(x.ValueCm + offsetX, ext.Ymax + 20 + offsetY, 0)
                    : new Point3d(offsetX + x.ValueCm + x.Slope * ext.Ymax, ext.Ymax + 20 + offsetY, 0);
                AppendEntity(tr, btr, MakeText("ST4-AKS-ETIKETLER", 15, (i + 1).ToString(CultureInfo.InvariantCulture), p, true));
            }

            for (int i = 0; i < Model.AxisY.Count; i++)
            {
                var y = Model.AxisY[i];
                var p = Math.Abs(y.Slope) <= 1e-9
                    ? new Point3d(ext.Xmax + 20 + offsetX, -y.ValueCm + offsetY, 0)
                    : new Point3d(ext.Xmax + 20 + offsetX, -(y.ValueCm + y.Slope * ext.Xmax) + offsetY, 0);
                AppendEntity(tr, btr, MakeText("ST4-AKS-ETIKETLER", 15, NumToAlpha(i + 1), p, false));
            }

            var titlePos = new Point3d(offsetX + (ext.Xmin + ext.Xmax) / 2.0, offsetY + ext.Ymax + 45, 0);
            string title = $"{floor.Name} - {floor.ShortName} ({floor.ElevationM.ToString("0", CultureInfo.InvariantCulture)}m){(drawBeams ? " [KIRISLI]" : string.Empty)}";
            AppendEntity(tr, btr, MakeText("ST4-AKS-ETIKETLER", 12, title, titlePos, true));
        }

        private static DBText MakeText(string layer, double height, string value, Point3d p, bool center)
        {
            var t = new DBText
            {
                Layer = layer,
                Height = height,
                TextString = value ?? string.Empty,
                Position = p
            };
            if (center)
            {
                t.HorizontalMode = TextHorizontalMode.TextCenter;
                t.VerticalMode = TextVerticalMode.TextVerticalMid;
                t.AlignmentPoint = p;
            }
            return t;
        }

        private static string NumToAlpha(int n)
        {
            string result = string.Empty;
            while (n > 0)
            {
                n--;
                result = (char)('A' + (n % 26)) + result;
                n /= 26;
            }
            return result;
        }

        private static Point2d[] BuildRect(Point2d center, double hw, double hh, double angleDeg)
        {
            var pts = new[]
            {
                new Point2d(center.X - hw, center.Y - hh),
                new Point2d(center.X + hw, center.Y - hh),
                new Point2d(center.X + hw, center.Y + hh),
                new Point2d(center.X - hw, center.Y + hh)
            };
            if (Math.Abs(angleDeg) <= 1e-9) return pts;
            for (int i = 0; i < pts.Length; i++)
            {
                var local = new Vector2d(pts[i].X - center.X, pts[i].Y - center.Y);
                var rotated = Rotate(local, angleDeg);
                pts[i] = new Point2d(center.X + rotated.X, center.Y + rotated.Y);
            }
            return pts;
        }

        private bool TryGetPolygonColumn(int sectionId, Point2d center, double angleDeg, out Point2d[] points)
        {
            points = Array.Empty<Point2d>();
            if (!Model.PolygonColumnSectionByPositionSectionId.TryGetValue(sectionId, out int polygonSectionId)) return false;
            if (!Model.PolygonSections.TryGetValue(polygonSectionId, out List<Point2d> localPoints) || localPoints.Count < 3) return false;

            var list = new List<Point2d>(localPoints.Count);
            foreach (var p in localPoints)
            {
                var g = new Point2d(center.X + p.X, center.Y - p.Y);
                if (Math.Abs(angleDeg) > 1e-9)
                {
                    var v = new Vector2d(g.X - center.X, g.Y - center.Y);
                    var r = Rotate(v, angleDeg);
                    g = new Point2d(center.X + r.X, center.Y + r.Y);
                }
                list.Add(g);
            }

            points = list.ToArray();
            return true;
        }

        private static Vector2d ComputeColumnOffset(int off1, int off2, double hw, double hh)
        {
            double locX = ComputeColumnAxisOffsetX(off1, hw);
            double locY = ComputeColumnAxisOffsetY(off2, hh);

            return new Vector2d(locX, locY);
        }

        private static double ComputeColumnAxisOffsetX(int off, double halfSize)
        {
            if (off == -1) return halfSize;   // axis on left edge
            if (off == 1) return -halfSize;   // axis on right edge
            if (off == 0) return 0.0;         // centered

            double offCm = Math.Abs(off) / 10.0;
            if (off > 1)
            {
                // axis is inside from right edge by off(mm)
                return offCm - halfSize;
            }

            // off < -1: axis is inside from left edge by |off|(mm)
            return halfSize - offCm;
        }

        private static double ComputeColumnAxisOffsetY(int off, double halfSize)
        {
            if (off == -1) return -halfSize;  // axis on top edge
            if (off == 1) return halfSize;    // axis on bottom edge
            if (off == 0) return 0.0;         // centered

            double offCm = Math.Abs(off) / 10.0;
            if (off > 1)
            {
                // axis is inside from bottom edge by off(mm)
                return halfSize - offCm;
            }

            // off < -1: axis is inside from top edge by |off|(mm)
            return -halfSize + offCm;
        }

        private static void ComputeBeamEdgeOffsets(int off, double hw, out double upperEdge, out double lowerEdge)
        {
            if (off == -1)
            {
                // Axis coincides with upper edge.
                upperEdge = 0.0;
                lowerEdge = -2.0 * hw;
                return;
            }
            if (off == 1)
            {
                // Axis coincides with lower edge.
                upperEdge = 2.0 * hw;
                lowerEdge = 0.0;
                return;
            }
            if (off == 0)
            {
                // Beam is centered on axis.
                upperEdge = hw;
                lowerEdge = -hw;
                return;
            }

            double offCm = off / 10.0;
            if (off > 1)
            {
                // Axis is inside beam, off(mm) from lower edge.
                lowerEdge = -offCm;
                upperEdge = lowerEdge + (2.0 * hw);
                return;
            }

            // off < -1: axis is inside beam, |off|(mm) from upper edge.
            upperEdge = -offCm;
            lowerEdge = upperEdge - (2.0 * hw);
        }

        private static void NormalizeBeamDirection(int fixedAxisId, ref Point2d a, ref Point2d b)
        {
            bool isFixedY = fixedAxisId >= 2001 && fixedAxisId <= 2999;
            bool isFixedX = fixedAxisId >= 1001 && fixedAxisId <= 1999;

            if (isFixedY && a.X > b.X)
            {
                (a, b) = (b, a);
            }
            else if (isFixedX && a.Y > b.Y)
            {
                (a, b) = (b, a);
            }
        }

        private HashSet<int> BuildAxisUsageMap(IEnumerable<int> axisIds, Func<ColumnAxisInfo, int> selector)
        {
            var set = new HashSet<int>(axisIds);
            var used = new HashSet<int>();
            foreach (var c in Model.Columns)
            {
                int id = selector(c);
                if (set.Contains(id)) used.Add(id);
            }
            return used;
        }

        private static Vector2d Rotate(Vector2d v, double angleDeg)
        {
            double a = angleDeg * Math.PI / 180.0;
            double c = Math.Cos(a);
            double s = Math.Sin(a);
            return new Vector2d(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }

        private static Polyline ToPolyline(IReadOnlyList<Point2d> points, bool closed)
        {
            var pl = new Polyline();
            for (int i = 0; i < points.Count; i++)
            {
                pl.AddVertexAt(i, points[i], 0, 0, 0);
            }
            pl.Closed = closed;
            return pl;
        }

        private static void AppendEntity(Transaction tr, BlockTableRecord btr, Entity e)
        {
            btr.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
        }

        private List<BeamInfo> MergeSameIdBeamsOnFloor(int floorNo)
        {
            var grouped = new Dictionary<string, List<(double S1, double S2, int StartAxis, int EndAxis, BeamInfo Beam)>>();
            var passthrough = new List<BeamInfo>();

            foreach (var beam in Model.Beams)
            {
                int beamFloor = beam.BeamId >= 1000 ? beam.BeamId / 1000 : beam.BeamId / 100;
                if (beamFloor != floorNo) continue;
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1) ||
                    !_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2))
                {
                    passthrough.Add(beam);
                    continue;
                }

                Vector2d v = p2 - p1;
                if (v.Length <= 1e-9)
                {
                    passthrough.Add(beam);
                    continue;
                }

                var u = v.GetNormal();
                double s1 = p1.X * u.X + p1.Y * u.Y;
                double s2 = p2.X * u.X + p2.Y * u.Y;
                int aStart = beam.StartAxisId;
                int aEnd = beam.EndAxisId;
                if (s1 > s2)
                {
                    (s1, s2) = (s2, s1);
                    (aStart, aEnd) = (aEnd, aStart);
                }

                string key = $"{beam.BeamId}|{beam.FixedAxisId}|{beam.WidthCm.ToString("0.###", CultureInfo.InvariantCulture)}|{beam.HeightCm.ToString("0.###", CultureInfo.InvariantCulture)}|{beam.OffsetRaw}|{beam.IsWallFlag}";
                if (!grouped.TryGetValue(key, out var list))
                {
                    list = new List<(double, double, int, int, BeamInfo)>();
                    grouped[key] = list;
                }
                list.Add((s1, s2, aStart, aEnd, beam));
            }

            var merged = new List<BeamInfo>();
            foreach (var kv in grouped)
            {
                var segs = kv.Value;
                var min = segs.OrderBy(x => x.S1).First();
                var max = segs.OrderByDescending(x => x.S2).First();
                var b0 = segs[0].Beam;
                merged.Add(new BeamInfo
                {
                    BeamId = b0.BeamId,
                    FixedAxisId = b0.FixedAxisId,
                    StartAxisId = min.StartAxis,
                    EndAxisId = max.EndAxis,
                    WidthCm = b0.WidthCm,
                    HeightCm = b0.HeightCm,
                    OffsetRaw = b0.OffsetRaw,
                    IsWallFlag = b0.IsWallFlag
                });
            }

            merged.AddRange(passthrough);
            return merged;
        }
    }
}
