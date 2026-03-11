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
                var createdBeamLineRefs = new List<(ObjectId Id, int OwnerKey, double OffsetX, double OffsetY)>();
                var createdBeamBodies = new List<(int OwnerKey, Point2d[] Poly, double OffsetX, double OffsetY)>();
                var allBlockersPerFloor = new List<(double Ox, double Oy, List<Point2d[]> Polys)>();
                var allCirclesPerFloor = new List<(double Ox, double Oy, List<(Point2d Center, double Radius)> Circles)>();

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
                            wallCount += DrawBeamsAndWalls(tr, btr, floor, offsetX, offsetY, createdBeamLineRefs, createdBeamBodies, allBlockersPerFloor, allCirclesPerFloor);
                        }
                        DrawLabels(tr, btr, floor, drawBeams, offsetX, offsetY, ext);
                    }
                }

                // Remove beam lines that run inside another beam body.
                createdBeamLineRefs = CleanupBeamLinesInsideBeamBodies(tr, btr, createdBeamLineRefs, createdBeamBodies);

                // Extend beam lines that have one free end (no column/wall/beam within 5mm) – SNAP’ten ÖNCE:
                // Orijinal uç noktalarına göre karar verilir; snap sonrası uç başka çizgiye taşınırsa 0 mm sayılıp yanlış “dolu” çıkmasın.
                ExtendFreeBeamEnds(tr, btr, createdBeamLineRefs, createdBeamBodies, allBlockersPerFloor, allCirclesPerFloor);

                // Zero-radius fillet style cleanup: snap near beam endpoints to true intersections.
                SnapBeamLineEndpointsToIntersections(tr, createdBeamLineRefs);

                // Final safety passes: after endpoint snapping, temizlemesi kaçan kiriş içi parçaları da buda.
                createdBeamLineRefs = CleanupBeamLinesInsideBeamBodies(tr, btr, createdBeamLineRefs, createdBeamBodies);
                createdBeamLineRefs = CleanupBeamEndpointStubsInsideBodies(tr, createdBeamLineRefs, createdBeamBodies);

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
            List<(ObjectId Id, int OwnerKey, double OffsetX, double OffsetY)> createdBeamLineRefs,
            List<(int OwnerKey, Point2d[] Poly, double OffsetX, double OffsetY)> createdBeamBodies,
            List<(double Ox, double Oy, List<Point2d[]> Polys)> allBlockersPerFloor,
            List<(double Ox, double Oy, List<(Point2d Center, double Radius)> Circles)> allCirclesPerFloor)
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
                createdBeamBodies.Add((pr.OwnerKey, new[] { pr.Q1, pr.Q2, pr.Q3, pr.Q4 }, offsetX, offsetY));
            }

            var columnAndWallBlockers = BuildBlockerPolygonsForFloor(floor.FloorNo, offsetX, offsetY);
            foreach (var pr in beamProfiles.Where(x => x.Beam.IsWallFlag == 1))
            {
                var wallPoly = new[] { pr.Q1, pr.Q2, pr.Q3, pr.Q4 };
                columnAndWallBlockers.Add(wallPoly);
                var pl = ToPolyline(wallPoly, true);
                pl.Layer = "ST4-PERDELER";
                AppendEntity(tr, btr, pl);
                wallCount++;
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
                if (t12.HasValue) DrawTrimmedLine(tr, btr, t12.Value.A, t12.Value.B, "ST4-KIRISLAR", outwardBlockers, inwardBeamBlockers, pr.OwnerKey, offsetX, offsetY, createdBeamLineRefs);
                if (t43.HasValue) DrawTrimmedLine(tr, btr, t43.Value.A, t43.Value.B, "ST4-KIRISLAR", outwardBlockers, inwardBeamBlockers, pr.OwnerKey, offsetX, offsetY, createdBeamLineRefs);
            }

            allBlockersPerFloor.Add((offsetX, offsetY, new List<Point2d[]>(columnAndWallBlockers)));
            allCirclesPerFloor.Add((offsetX, offsetY, BuildCirclesForFloor(floor.FloorNo, offsetX, offsetY)));

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
                int sectionId = ResolveColumnSectionId(floorNo, col.ColumnNo);
                int polygonSectionId = ResolvePolygonPositionSectionId(floorNo, col.ColumnNo);
                if (col.ColumnType == 3)
                {
                    if (polygonSectionId <= 0 || !Model.PolygonColumnSectionByPositionSectionId.ContainsKey(polygonSectionId)) continue;
                }
                else
                {
                    if (sectionId <= 0 || !Model.ColumnDimsBySectionId.ContainsKey(sectionId)) continue;
                }

                var dim = sectionId > 0 && Model.ColumnDimsBySectionId.ContainsKey(sectionId)
                    ? Model.ColumnDimsBySectionId[sectionId]
                    : (W: 40.0, H: 40.0);
                double hw = dim.W / 2.0;
                double hh = dim.H / 2.0;
                var offsetLocal = ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                var center = new Point2d(axisNode.X + offsetGlobal.X + offsetX, axisNode.Y + offsetGlobal.Y + offsetY);

                if (col.ColumnType == 2)
                {
                    blockers.Add(ApproximateCircle(center, Math.Max(hw, hh), 24));
                }
                else if (col.ColumnType == 3 && TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out var poly))
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

        /// <summary>Kolon/perde çizimindeki daireler (ColumnType=2) – boş uç kontrolü için.</summary>
        private List<(Point2d Center, double Radius)> BuildCirclesForFloor(int floorNo, double offsetX, double offsetY)
        {
            var circles = new List<(Point2d Center, double Radius)>();
            foreach (var col in Model.Columns)
            {
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                int sectionId = ResolveColumnSectionId(floorNo, col.ColumnNo);
                if (col.ColumnType != 2 || sectionId <= 0 || !Model.ColumnDimsBySectionId.ContainsKey(sectionId)) continue;

                var dim = Model.ColumnDimsBySectionId[sectionId];
                double hw = dim.W / 2.0;
                double hh = dim.H / 2.0;
                var offsetLocal = ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                var center = new Point2d(axisNode.X + offsetGlobal.X + offsetX, axisNode.Y + offsetGlobal.Y + offsetY);
                double radius = Math.Max(hw, hh);
                circles.Add((center, radius));
            }
            return circles;
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
            double offsetX,
            double offsetY,
            List<(ObjectId Id, int OwnerKey, double OffsetX, double OffsetY)> createdBeamLineRefs)
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
                createdBeamLineRefs.Add((ln.ObjectId, ownerKey, offsetX, offsetY));
            }
        }

        private static void SnapBeamLineEndpointsToIntersections(Transaction tr, List<(ObjectId Id, int OwnerKey, double OffsetX, double OffsetY)> lineRefs)
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

        /// <summary>
        /// Boş kiriş uç tanımı ve kurallar:
        /// 1) Bir kiriş çizgisinin iki uç noktasından herhangi biri ele alınır.
        /// 2) O noktanın etrafında 5mm (0,5 cm) uzaklığında şunlardan hiçbiri yoksa o uç BOŞ kabul edilir:
        ///    - Başka kirişe/kolona/perdeye ait çizgi, poligon kenarı, daire veya yay.
        /// 3) Referans, DATA üzerinden değil FİNAL ÇİZİM üzerindeki entity'lerden alınır; uç koordinatları da çizimdeki Line koordinatlarıdır.
        /// 4) Boş uçlar en yakın referansa kadar (en fazla 200 cm) extend edilir.
        /// </summary>
        private static void ExtendFreeBeamEnds(
            Transaction tr,
            BlockTableRecord btr,
            List<(ObjectId Id, int OwnerKey, double OffsetX, double OffsetY)> lineRefs,
            List<(int OwnerKey, Point2d[] Poly, double OffsetX, double OffsetY)> beamBodies,
            List<(double Ox, double Oy, List<Point2d[]> Polys)> allBlockersPerFloor,
            List<(double Ox, double Oy, List<(Point2d Center, double Radius)> Circles)> allCirclesPerFloor)
        {
            const double freeTolCm = 0.5;   // 5 mm – uç nokta etrafında bu mesafede referans yoksa boş
            const double maxExtendCm = 200.0; // do not extend if nearest ref is beyond 200 cm
            const double searchRadiusCm = maxExtendCm + 50.0; // referans toplarken bu yarıçap

            int lineIndex = 0;
            foreach (var lineRef in lineRefs)
            {
                var id = lineRef.Id;
                if (id.IsNull || id.IsErased) { lineIndex++; continue; }
                if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Line ln)) { lineIndex++; continue; }
                var a = new Point2d(ln.StartPoint.X, ln.StartPoint.Y);
                var b = new Point2d(ln.EndPoint.X, ln.EndPoint.Y);
                double len = a.GetDistanceTo(b);
                if (len <= 1e-9) { lineIndex++; continue; }

                // Referansı DATA yerine FİNAL ÇİZİMden al: nokta a ve b civarında (searchRadiusCm) kalan tüm kolon/perde/kiriş çizgileri
                GetReferenceFromDrawing(tr, btr, a, b, searchRadiusCm, id, out var allRef, out var refCircles);

                bool startFree = IsEndpointFree(a, allRef, refCircles, freeTolCm);
                bool endFree = IsEndpointFree(b, allRef, refCircles, freeTolCm);

                if (startFree)
                {
                    Vector2d dirOut = (a - b).GetNormal();
                    if (TryFindNearestIntersection(a, dirOut, maxExtendCm, allRef, out double distStart))
                    {
                        Point2d newStart = a + dirOut.MultiplyBy(distStart);
                        ln.StartPoint = new Point3d(newStart.X, newStart.Y, ln.StartPoint.Z);
                    }
                }

                if (endFree)
                {
                    Vector2d dirOut = (b - a).GetNormal();
                    if (TryFindNearestIntersection(b, dirOut, maxExtendCm, allRef, out double distEnd))
                    {
                        Point2d newEnd = b + dirOut.MultiplyBy(distEnd);
                        ln.EndPoint = new Point3d(newEnd.X, newEnd.Y, ln.EndPoint.Z);
                    }
                }

                lineIndex++;
            }
        }

        /// <summary>Çizimdeki (btr) ST4-KOLONLAR, ST4-PERDELER, ST4-KIRISLAR entity'lerinden, verilen noktaların searchRadiusCm yakınındaki segment ve daireleri toplar. excludeLineId hariç; aynı kirişin diğer kenarı (her iki ucu da currentA/B'ye 5mm içinde) hariç.</summary>
        private static void GetReferenceFromDrawing(
            Transaction tr,
            BlockTableRecord btr,
            Point2d currentA,
            Point2d currentB,
            double searchRadiusCm,
            ObjectId excludeLineId,
            out List<(Point2d A, Point2d B)> segments,
            out List<(Point2d Center, double Radius)> circles)
        {
            segments = new List<(Point2d A, Point2d B)>();
            circles = new List<(Point2d Center, double Radius)>();
            const double sameBeamTol = 0.5; // 5mm – aynı kirişin diğer kenarı sayılmaz

            foreach (ObjectId eid in btr)
            {
                if (eid.IsNull || eid.IsErased) continue;
                if (!(tr.GetObject(eid, OpenMode.ForRead, false) is Entity ent)) continue;
                string layer = ent.Layer;
                if (layer != "ST4-KOLONLAR" && layer != "ST4-PERDELER" && layer != "ST4-KIRISLAR") continue;

                if (ent is Line line)
                {
                    var p1 = new Point2d(line.StartPoint.X, line.StartPoint.Y);
                    var p2 = new Point2d(line.EndPoint.X, line.EndPoint.Y);
                    if (p1.GetDistanceTo(p2) <= 1e-9) continue;
                    double d1 = p1.GetDistanceTo(currentA);
                    double d2 = p1.GetDistanceTo(currentB);
                    double d3 = p2.GetDistanceTo(currentA);
                    double d4 = p2.GetDistanceTo(currentB);
                    double minDist = Math.Min(Math.Min(d1, d2), Math.Min(d3, d4));
                    if (minDist > searchRadiusCm) continue;
                    if (layer == "ST4-KIRISLAR")
                    {
                        if (eid == excludeLineId) continue;
                        // Aynı kirişin diğer kenarı: her iki uç da currentA/currentB'ye 5mm içinde
                        if (Math.Min(d1, d3) <= sameBeamTol && Math.Min(d2, d4) <= sameBeamTol)
                            continue;
                    }
                    segments.Add((p1, p2));
                    continue;
                }

                if (ent is Circle circle)
                {
                    var c = new Point2d(circle.Center.X, circle.Center.Y);
                    double r = circle.Radius;
                    double distToA = Math.Abs(currentA.GetDistanceTo(c) - r);
                    double distToB = Math.Abs(currentB.GetDistanceTo(c) - r);
                    if (distToA <= searchRadiusCm || distToB <= searchRadiusCm)
                        circles.Add((c, r));
                    continue;
                }

                if (ent is Polyline pl)
                {
                    int n = pl.NumberOfVertices;
                    if (n < 2) continue;
                    for (int i = 0; i < n; i++)
                    {
                        Point2d p1 = pl.GetPoint2dAt(i);
                        Point2d p2 = pl.GetPoint2dAt((i + 1) % n);
                        if (p1.GetDistanceTo(p2) <= 1e-9) continue;
                        double d1 = p1.GetDistanceTo(currentA);
                        double d2 = p1.GetDistanceTo(currentB);
                        double d3 = p2.GetDistanceTo(currentA);
                        double d4 = p2.GetDistanceTo(currentB);
                        double minDist = Math.Min(Math.Min(d1, d2), Math.Min(d3, d4));
                        if (minDist <= searchRadiusCm)
                            segments.Add((p1, p2));
                    }
                }
            }
        }

        /// <summary>Uç boş mu: 5mm içinde kiriş/kolon/perde çizimine ait çizgi, poligon kenarı veya daire yoksa true.</summary>
        private static bool IsEndpointFree(Point2d p, List<(Point2d A, Point2d B)> refSegments,
            List<(Point2d Center, double Radius)> refCircles, double tolCm)
        {
            foreach (var seg in refSegments)
            {
                if (PointSegmentDistance(p, seg.A, seg.B) <= tolCm)
                    return false;
            }
            foreach (var c in refCircles)
            {
                if (PointToCircleDistance(p, c.Center, c.Radius) <= tolCm)
                    return false;
            }
            return true;
        }

        /// <summary>Noktanın daire sınırına uzaklığı (cm).</summary>
        private static double PointToCircleDistance(Point2d p, Point2d center, double radius)
        {
            double d = p.GetDistanceTo(center);
            return Math.Abs(d - radius);
        }

        /// <summary>Ray from P in direction D (unit). Find smallest distance in [0, maxDist] to any segment. Excludes hits at P (t &gt; small).</summary>
        private static bool TryFindNearestIntersection(Point2d p, Vector2d d, double maxDist, List<(Point2d A, Point2d B)> segments, out double distance)
        {
            distance = double.MaxValue;
            const double skipTol = 1e-4; // skip param very near 0 (self)
            Point2d rayEnd = p + d.MultiplyBy(maxDist);
            foreach (var seg in segments)
            {
                if (!TrySegmentSegmentT(p, rayEnd, seg.A, seg.B, out double t)) continue;
                if (t <= skipTol) continue;
                double dist = t * maxDist;
                if (dist < distance) distance = dist;
            }
            if (distance == double.MaxValue)
            {
                distance = 0.0;
                return false;
            }
            return true;
        }

        private static List<(ObjectId Id, int OwnerKey, double OffsetX, double OffsetY)> CleanupBeamLinesInsideBeamBodies(
            Transaction tr,
            BlockTableRecord btr,
            List<(ObjectId Id, int OwnerKey, double OffsetX, double OffsetY)> lineRefs,
            List<(int OwnerKey, Point2d[] Poly, double OffsetX, double OffsetY)> beamBodies)
        {
            const double minKeep = 2.0;
            const double insideTol = -0.1; // 1 mm inward check (cm units)
            var result = new List<(ObjectId Id, int OwnerKey, double OffsetX, double OffsetY)>();

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
                    result.Add((newLine.ObjectId, lineRef.OwnerKey, lineRef.OffsetX, lineRef.OffsetY));
                }
            }

            return result;
        }

        private static List<(ObjectId Id, int OwnerKey, double OffsetX, double OffsetY)> CleanupBeamEndpointStubsInsideBodies(
            Transaction tr,
            List<(ObjectId Id, int OwnerKey, double OffsetX, double OffsetY)> lineRefs,
            List<(int OwnerKey, Point2d[] Poly, double OffsetX, double OffsetY)> beamBodies)
        {
            const double stubMax = 40.0; // cm, max stub length to trim at ends
            const double edgeTol = 0.5;  // cm, consider intervals touching 0 / len within this

            var result = new List<(ObjectId Id, int OwnerKey, double OffsetX, double OffsetY)>();

            foreach (var lineRef in lineRefs)
            {
                var id = lineRef.Id;
                if (id.IsNull || id.IsErased) continue;
                if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Line ln)) continue;

                var a = new Point2d(ln.StartPoint.X, ln.StartPoint.Y);
                var b = new Point2d(ln.EndPoint.X, ln.EndPoint.Y);
                double len = a.GetDistanceTo(b);
                if (len <= 1e-9)
                {
                    ln.Erase();
                    continue;
                }
                var dir = (b - a).GetNormal();

                var intervals = new List<(double S, double E)>();
                foreach (var body in beamBodies)
                {
                    if (body.OwnerKey == lineRef.OwnerKey) continue;
                    intervals.AddRange(SegmentInsideIntervalsInPolygon(a, b, body.Poly, 0.0));
                }

                if (intervals.Count == 0)
                {
                    result.Add(lineRef);
                    continue;
                }

                var merged = MergeIntervals(intervals, 0.5);
                if (merged.Count == 0)
                {
                    result.Add(lineRef);
                    continue;
                }

                double start = 0.0;
                double end = len;

                var first = merged[0];
                if (first.S <= edgeTol && (first.E - first.S) <= stubMax)
                {
                    start = Math.Max(start, first.E);
                }

                var last = merged[merged.Count - 1];
                if (len - last.E <= edgeTol && (last.E - last.S) <= stubMax)
                {
                    end = Math.Min(end, last.S);
                }

                if (end - start <= 1e-3)
                {
                    ln.Erase();
                    continue;
                }

                var na = a + dir.MultiplyBy(start);
                var nb = a + dir.MultiplyBy(end);
                ln.StartPoint = new Point3d(na.X, na.Y, ln.StartPoint.Z);
                ln.EndPoint = new Point3d(nb.X, nb.Y, ln.EndPoint.Z);

                result.Add(lineRef);
            }

            return result;
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

        /// <summary>Bazı ST4 dosyalarında kolon kesit ID'si floorNo*100+colNo (101,102), bazılarında 1000+colNo (1001,1002). Her iki şemayı dene.</summary>
        private int ResolveColumnSectionId(int floorNo, int colNo)
        {
            int sid = floorNo * 100 + colNo;
            if (Model.ColumnDimsBySectionId.ContainsKey(sid)) return sid;
            sid = 1000 + colNo;
            return Model.ColumnDimsBySectionId.ContainsKey(sid) ? sid : 0;
        }

        private int ResolvePolygonPositionSectionId(int floorNo, int colNo)
        {
            int sid = floorNo * 100 + colNo;
            if (Model.PolygonColumnSectionByPositionSectionId.ContainsKey(sid)) return sid;
            sid = 1000 + colNo;
            if (Model.PolygonColumnSectionByPositionSectionId.ContainsKey(sid)) return sid;
            sid = floorNo * 1000 + colNo;
            return Model.PolygonColumnSectionByPositionSectionId.ContainsKey(sid) ? sid : 0;
        }

        private void DrawColumns(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY)
        {
            foreach (var col in Model.Columns)
            {
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
                int polygonSectionId = ResolvePolygonPositionSectionId(floor.FloorNo, col.ColumnNo);
                if (col.ColumnType == 3)
                {
                    if (polygonSectionId <= 0 || !Model.PolygonColumnSectionByPositionSectionId.ContainsKey(polygonSectionId)) continue;
                }
                else
                {
                    if (sectionId <= 0 || !Model.ColumnDimsBySectionId.ContainsKey(sectionId)) continue;
                }

                var dim = sectionId > 0 && Model.ColumnDimsBySectionId.ContainsKey(sectionId)
                    ? Model.ColumnDimsBySectionId[sectionId]
                    : (W: 40.0, H: 40.0);
                double hw = dim.W / 2.0;
                double hh = dim.H / 2.0;

                var offsetLocal = ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                var center = new Point2d(axisNode.X + offsetGlobal.X + offsetX, axisNode.Y + offsetGlobal.Y + offsetY);

                if (col.ColumnType == 2)
                {
                    AppendEntity(tr, btr, new Circle(new Point3d(center.X, center.Y, 0), Vector3d.ZAxis, Math.Max(hw, hh)) { Layer = "ST4-KOLONLAR" });
                }
                else if (col.ColumnType == 3 && TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out var polyPoints))
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
