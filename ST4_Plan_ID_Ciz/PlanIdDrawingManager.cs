using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Precision;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// Akslar, kolonlar (dikdörtgen + daire + poligon), kirişler, perdeler ve döşemeleri
    /// tüm eleman ID'leriyle çizer; katlar yan yana dizilir.
    /// </summary>
    public sealed class PlanIdDrawingManager
    {
        private readonly St4Model _model;
        private readonly AxisGeometryService _axisService;

        public PlanIdDrawingManager(St4Model model)
        {
            _model = model;
            _axisService = new AxisGeometryService(model);
        }

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

                bool hasFoundations = _model.ContinuousFoundations.Count > 0 || _model.SlabFoundations.Count > 0 || _model.TieBeams.Count > 0 || _model.SingleFootings.Count > 0;
                int planStartIndex = hasFoundations ? 1 : 0;

                if (hasFoundations && _model.Floors.Count > 0)
                {
                    double offsetX = 0.0;
                    double offsetY = 0.0;
                    var firstFloor = _model.Floors[0];
                    DrawAxes(tr, btr, offsetX, offsetY, ext);
                    DrawColumns(tr, btr, firstFloor, offsetX, offsetY);
                    DrawWallsForFloor(tr, btr, firstFloor, offsetX, offsetY);
                    Geometry temelUnion = BuildTemelUnion(offsetX, offsetY, firstFloor);
                    Geometry kolonPerdeUnion = BuildKolonPerdeUnion(firstFloor, offsetX, offsetY);
                    DrawTemelMerged(tr, btr, offsetX, offsetY, firstFloor, temelUnion);
                    DrawContinuousFoundations(tr, btr, offsetX, offsetY, firstFloor, drawTemelOutline: false, temelUnion, kolonPerdeUnion);
                    DrawSlabFoundations(tr, btr, offsetX, offsetY, drawTemelOutline: false);
                    DrawTieBeams(tr, btr, firstFloor, offsetX, offsetY, kolonPerdeUnion);
                    DrawSingleFootings(tr, btr, firstFloor, offsetX, offsetY, drawTemelOutline: false);
                    DrawFloorTitle(tr, btr, firstFloor, offsetX, offsetY, ext, isFoundationPlan: true);
                }

                for (int floorIdx = 0; floorIdx < _model.Floors.Count; floorIdx++)
                {
                    var floor = _model.Floors[floorIdx];
                    double offsetX = (floorIdx + planStartIndex) * (floorWidth + floorGap);
                    double offsetY = 0.0;

                    DrawAxes(tr, btr, offsetX, offsetY, ext);
                    DrawColumns(tr, btr, floor, offsetX, offsetY);
                    DrawBeamsAndWalls(tr, btr, floor, offsetX, offsetY);
                    DrawSlabs(tr, btr, floor, offsetX, offsetY);
                    DrawFloorTitle(tr, btr, floor, offsetX, offsetY, ext, isFoundationPlan: false);
                }

                tr.Commit();

                ed.WriteMessage(
                    "\nST4PLANID: {0} kat, akslar, kolonlar (poligon dahil), kirişler, perdeler ve döşemeler{1} ID'leriyle cizildi. (cm)",
                    _model.Floors.Count,
                    hasFoundations ? string.Format(", temel plani (surekli: {0}, radye: {1}, bag kirisi: {2}, tekil: {3})", _model.ContinuousFoundations.Count, _model.SlabFoundations.Count, _model.TieBeams.Count, _model.SingleFootings.Count) : "");
            }
        }

        private const string LayerAks = "AKS CIZGISI (BEYKENT)";
        private const string LayerKiris = "KIRIS (BEYKENT)";
        private const string LayerKolon = "KOLON (BEYKENT)";
        private const string LayerPerde = "PERDE (BEYKENT)";
        private const string LayerTarama = "TARAMA (BEYKENT)";
        private const string LayerDoseme = "DOSEME SINIRI (BEYKENT)";
        private const string LayerMerdiven = "MERDIVEN (BEYKENT)";
        private const string LayerYazi = "YAZI (BEYKENT)";
        private const string LayerBaslik = "YAZI (BEYKENT)";

        private static void EnsureLayers(Transaction tr, Database db)
        {
            EnsureDashedLinetype(tr, db);
            EnsurePlanLayer(tr, db, LayerAks, 252, LineWeight.LineWeight020, useDashed: true);
            EnsurePlanLayer(tr, db, LayerKiris, 2, LineWeight.LineWeight030, useDashed: false);
            EnsurePlanLayer(tr, db, LayerKolon, 3, LineWeight.LineWeight040, useDashed: false);
            EnsurePlanLayer(tr, db, LayerPerde, 6, LineWeight.LineWeight040, useDashed: false);
            EnsurePlanLayer(tr, db, LayerTarama, 8, LineWeight.LineWeight015, useDashed: false);
            EnsurePlanLayer(tr, db, LayerDoseme, 71, LineWeight.LineWeight030, useDashed: false);
            EnsurePlanLayer(tr, db, LayerMerdiven, 5, LineWeight.LineWeight030, useDashed: false);
            EnsurePlanLayer(tr, db, LayerYazi, 4, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, LayerBaslik, 4, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, "TEMEL (BEYKENT)", 2, LineWeight.LineWeight040, useDashed: false);
            EnsurePlanLayer(tr, db, "TEMEL AMPATMAN (BEYKENT)", 21, LineWeight.LineWeight040, useDashed: false);
            EnsurePlanLayer(tr, db, "TEMEL HATILI (BEYKENT)", 6, LineWeight.LineWeight030, useDashed: false);
        }

        private static void EnsureDashedLinetype(Transaction tr, Database db)
        {
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has("DASHED") || ltt.Has("Dashed")) return;
            try
            {
                db.LoadLineTypeFile("DASHED", "acad.lin");
            }
            catch
            {
                try { db.LoadLineTypeFile("Dashed", "acad.lin"); }
                catch
                {
                    try { db.LoadLineTypeFile("DASHED", "acadiso.lin"); }
                    catch { try { db.LoadLineTypeFile("Dashed", "acadiso.lin"); } catch { } }
                }
            }
        }

        private static void EnsurePlanLayer(Transaction tr, Database db, string layerName, int colorIndex, LineWeight lineWeight, bool useDashed = false)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            var rec = lt.Has(layerName)
                ? (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite)
                : null;
            if (rec == null)
            {
                lt.UpgradeOpen();
                rec = new LayerTableRecord
                {
                    Name = layerName,
                    Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorIndex),
                    LineWeight = lineWeight
                };
                lt.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }
            if (useDashed)
            {
                var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (ltt.Has("DASHED"))
                    rec.LinetypeObjectId = ltt["DASHED"];
                else if (ltt.Has("Dashed"))
                    rec.LinetypeObjectId = ltt["Dashed"];
            }
        }

        private (double Xmin, double Xmax, double Ymin, double Ymax) CalculateBaseExtents()
        {
            const double margin = 50.0;
            double xmin = _model.AxisX.Count > 0 ? _model.AxisX.Min(x => x.ValueCm) - margin : 0.0;
            double xmax = _model.AxisX.Count > 0 ? _model.AxisX.Max(x => x.ValueCm) + margin : 1000.0;
            double ymin = _model.AxisY.Count > 0 ? -_model.AxisY.Max(y => y.ValueCm) - margin : -1000.0;
            double ymax = _model.AxisY.Count > 0 ? -_model.AxisY.Min(y => y.ValueCm) + margin : 1000.0;

            foreach (var y in _model.AxisY)
            {
                if (Math.Abs(y.Slope) <= 1e-9) continue;
                ymin = Math.Min(ymin, -(y.ValueCm + y.Slope * xmin));
                ymin = Math.Min(ymin, -(y.ValueCm + y.Slope * xmax));
                ymax = Math.Max(ymax, -(y.ValueCm + y.Slope * xmin));
                ymax = Math.Max(ymax, -(y.ValueCm + y.Slope * xmax));
            }
            foreach (var x in _model.AxisX)
            {
                if (Math.Abs(x.Slope) <= 1e-9) continue;
                xmin = Math.Min(xmin, x.ValueCm + x.Slope * ymin);
                xmin = Math.Min(xmin, x.ValueCm + x.Slope * ymax);
                xmax = Math.Max(xmax, x.ValueCm + x.Slope * ymin);
                xmax = Math.Max(xmax, x.ValueCm + x.Slope * ymax);
            }

            return (xmin - margin, xmax + margin, ymin - margin, ymax + margin);
        }

        private void DrawAxes(Transaction tr, BlockTableRecord btr, double offsetX, double offsetY,
            (double Xmin, double Xmax, double Ymin, double Ymax) ext)
        {
            var xKolonAks = BuildColumnAxisIds(c => c.AxisXId, _model.AxisX.Select(a => a.Id));
            var yKolonAks = BuildColumnAxisIds(c => c.AxisYId, _model.AxisY.Select(a => a.Id));

            foreach (var ax in _model.AxisX)
            {
                if (!xKolonAks.Contains(ax.Id)) continue;
                var p1 = Math.Abs(ax.Slope) <= 1e-9
                    ? new Point3d(ax.ValueCm + offsetX, ext.Ymin + offsetY, 0)
                    : new Point3d(offsetX + ax.ValueCm + ax.Slope * ext.Ymin, ext.Ymin + offsetY, 0);
                var p2 = Math.Abs(ax.Slope) <= 1e-9
                    ? new Point3d(ax.ValueCm + offsetX, ext.Ymax + offsetY, 0)
                    : new Point3d(offsetX + ax.ValueCm + ax.Slope * ext.Ymax, ext.Ymax + offsetY, 0);
                AppendEntity(tr, btr, new Line(p1, p2) { Layer = LayerAks });
                var labelPt = new Point3d(ax.ValueCm + offsetX, ext.Ymax + 15 + offsetY, 0);
                AppendEntity(tr, btr, MakeCenteredText(LayerYazi, 6, ax.Id.ToString(CultureInfo.InvariantCulture), labelPt));
            }

            foreach (var ay in _model.AxisY)
            {
                if (!yKolonAks.Contains(ay.Id)) continue;
                var p1 = Math.Abs(ay.Slope) <= 1e-9
                    ? new Point3d(ext.Xmin + offsetX, -ay.ValueCm + offsetY, 0)
                    : new Point3d(ext.Xmin + offsetX, -(ay.ValueCm + ay.Slope * ext.Xmin) + offsetY, 0);
                var p2 = Math.Abs(ay.Slope) <= 1e-9
                    ? new Point3d(ext.Xmax + offsetX, -ay.ValueCm + offsetY, 0)
                    : new Point3d(ext.Xmax + offsetX, -(ay.ValueCm + ay.Slope * ext.Xmax) + offsetY, 0);
                AppendEntity(tr, btr, new Line(p1, p2) { Layer = LayerAks });
                var labelPt = new Point3d(ext.Xmax + 15 + offsetX, -ay.ValueCm + offsetY, 0);
                AppendEntity(tr, btr, MakeCenteredText(LayerYazi, 6, ay.Id.ToString(CultureInfo.InvariantCulture), labelPt));
            }
        }

        /// <summary>Kolon tanımlarında kullanılan aks ID'lerini döndürür (sadece kolon aksları çizilsin diye).</summary>
        private HashSet<int> BuildColumnAxisIds(Func<ColumnAxisInfo, int> selector, IEnumerable<int> axisIds)
        {
            var set = new HashSet<int>(axisIds);
            var used = new HashSet<int>();
            foreach (var c in _model.Columns)
            {
                int id = selector(c);
                if (set.Contains(id)) used.Add(id);
            }
            return used;
        }

        /// <summary>Kolon kesit: floorNo*100+colNo, floorNo*1000+colNo (TZN 1001,2001,8001), 1000+colNo. 2xx-9xx, 14xx, 2xxx-9xxx varsa 1000+colNo yok.</summary>
        private int ResolveColumnSectionId(int floorNo, int colNo)
        {
            int sid = floorNo * 100 + colNo;
            if (_model.ColumnDimsBySectionId.ContainsKey(sid)) return sid;
            sid = floorNo * 1000 + colNo;
            if (_model.ColumnDimsBySectionId.ContainsKey(sid)) return sid;
            bool hasFloorSpecific = _model.ColumnDimsBySectionId.Keys.Any(id => (id >= 200 && id < 1000) || (id >= 1400 && id < 1500) || (id >= 2000 && id < 10000));
            if (hasFloorSpecific) return 0;
            sid = 1000 + colNo;
            return _model.ColumnDimsBySectionId.ContainsKey(sid) ? sid : 0;
        }

        /// <summary>Poligon: floorNo*100+colNo, floorNo*1000+colNo. 2xx-9xx, 14xx, 2xxx-9xxx varsa 1000+colNo yok.</summary>
        private int ResolvePolygonPositionSectionId(int floorNo, int colNo)
        {
            int sid = floorNo * 100 + colNo;
            if (_model.PolygonColumnSectionByPositionSectionId.ContainsKey(sid)) return sid;
            sid = floorNo * 1000 + colNo;
            if (_model.PolygonColumnSectionByPositionSectionId.ContainsKey(sid)) return sid;
            bool hasFloorSpecific = _model.PolygonColumnSectionByPositionSectionId.Keys.Any(id => (id >= 200 && id < 1000) || (id >= 1400 && id < 1500) || (id >= 2000 && id < 10000));
            if (hasFloorSpecific) return 0;
            sid = 1000 + colNo;
            return _model.PolygonColumnSectionByPositionSectionId.ContainsKey(sid) ? sid : 0;
        }

        private void DrawColumns(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY)
        {
            foreach (var col in _model.Columns)
            {
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
                int polygonSectionId = ResolvePolygonPositionSectionId(floor.FloorNo, col.ColumnNo);
                if (col.ColumnType == 3)
                {
                    if (polygonSectionId <= 0 || !_model.PolygonColumnSectionByPositionSectionId.ContainsKey(polygonSectionId)) continue;
                }
                else
                {
                    if (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId)) continue;
                }

                var dim = sectionId > 0 && _model.ColumnDimsBySectionId.ContainsKey(sectionId)
                    ? _model.ColumnDimsBySectionId[sectionId]
                    : (W: 40.0, H: 40.0);
                double hw = dim.W / 2.0;
                double hh = dim.H / 2.0;
                var offsetLocal = col.ColumnType == 2
                    ? ComputeColumnOffsetCircle(col.OffsetXRaw, col.OffsetYRaw)
                    : ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                var center = new Point2d(axisNode.X + offsetGlobal.X + offsetX, axisNode.Y + offsetGlobal.Y + offsetY);

                double colAngleRad = col.AngleDeg * (Math.PI / 180.0);
                if (col.ColumnType == 2)
                {
                    var circle = new Circle(new Point3d(center.X, center.Y, 0), Vector3d.ZAxis, Math.Max(hw, hh)) { Layer = LayerKolon };
                    ObjectId circleId = AppendEntityReturnId(tr, btr, circle);
                    AppendHatchAnsi33(tr, btr, circleId, colAngleRad);
                }
                else if (col.ColumnType == 3 && TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out var polyPoints))
                {
                    var pl = ToPolyline(polyPoints, true);
                    pl.Layer = LayerKolon;
                    ObjectId plId = AppendEntityReturnId(tr, btr, pl);
                    AppendHatchAnsi33(tr, btr, plId, colAngleRad);
                }
                else
                {
                    var rect = BuildRect(center, hw, hh, col.AngleDeg);
                    var pl = ToPolyline(rect, true);
                    pl.Layer = LayerKolon;
                    ObjectId plId = AppendEntityReturnId(tr, btr, pl);
                    AppendHatchAnsi33(tr, btr, plId, colAngleRad);
                }

                AppendEntity(tr, btr, MakeCenteredText(LayerYazi, 6, col.ColumnNo.ToString(CultureInfo.InvariantCulture), new Point3d(center.X, center.Y, 0)));
            }
        }

        /// <summary>Verilen kattaki perdeleri (IsWallFlag==1) çizer; kolon alanları çıkarılır, saç teli temizliği uygulanır. Temel planında bodrum perdeleri için kullanılır.</summary>
        private void DrawWallsForFloor(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY)
        {
            var factory = new GeometryFactory();
            var wallList = new List<(Geometry poly, int fixedAxisId)>();
            var beams = MergeSameIdBeamsOnFloor(floor.FloorNo);
            foreach (var beam in beams)
            {
                if (beam.IsWallFlag != 1) continue;
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1) ||
                    !_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2))
                    continue;
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
                double cx = (q1.X + q2.X + q3.X + q4.X) / 4.0;
                double cy = (q1.Y + q2.Y + q3.Y + q4.Y) / 4.0;
                AppendEntity(tr, btr, MakeCenteredText(LayerYazi, 5, beam.BeamId.ToString(CultureInfo.InvariantCulture), new Point3d(cx, cy, 0)));
                var coordsWall = new[]
                {
                    new Coordinate(q1.X, q1.Y),
                    new Coordinate(q2.X, q2.Y),
                    new Coordinate(q3.X, q3.Y),
                    new Coordinate(q4.X, q4.Y),
                    new Coordinate(q1.X, q1.Y)
                };
                wallList.Add((factory.CreatePolygon(factory.CreateLinearRing(coordsWall)), beam.FixedAxisId));
            }
            if (wallList.Count == 0) return;
            Geometry kolonUnion = BuildKolonUnion(floor, offsetX, offsetY);
            foreach (var (wallPoly, fixedAxisId) in wallList)
            {
                if (wallPoly == null || wallPoly.IsEmpty) continue;
                Geometry toDraw = wallPoly;
                if (kolonUnion != null && !kolonUnion.IsEmpty)
                {
                    var diff = wallPoly.Difference(kolonUnion);
                    if (diff != null && !diff.IsEmpty)
                    {
                        toDraw = ReducePrecisionSafe(diff, 100);
                        if (toDraw == null || toDraw.IsEmpty) toDraw = diff;
                    }
                }
                if (toDraw != null && !toDraw.IsEmpty)
                    DrawGeometryRingsAsPolylines(tr, btr, toDraw, LayerPerde, addHatch: true, hatchAngleRad: GetAxisAngleRad(fixedAxisId));
            }
        }

        /// <summary>Verilen aks ID'sine ait eksenin eğimine göre yön açısını (radyan) döndürür. Perde tarama açısı için kullanılır. Y aksına bağlı perdelerde tarama eğimi ters alınır.</summary>
        private double GetAxisAngleRad(int axisId)
        {
            var axis = _model.AxisX.Concat(_model.AxisY).FirstOrDefault(a => a.Id == axisId);
            if (axis == null) return 0.0;
            // X ekseni: x - Slope*y = const → yön (Slope, 1). Y ekseni: Slope*x + y = const → yön (-1, Slope); Y için tarama eğimi ters.
            if (axis.Kind == AxisKind.X)
                return Math.Atan2(1.0, axis.Slope);
            return Math.Atan2(axis.Slope, -1.0) + Math.PI;
        }

        /// <summary>Geometriyi verilen ölçeğe (örn. 100 = 0.01 cm) indirger; ince sliver'lar birleşir. Hata olursa null döner.</summary>
        private static Geometry ReducePrecisionSafe(Geometry geom, double scale)
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

        /// <summary>Verilen kattaki kolon ve perdelerin birleşik alanını (NTS Geometry) döndürür.</summary>
        private Geometry BuildKolonPerdeUnion(FloorInfo floor, double offsetX, double offsetY)
        {
            var factory = new GeometryFactory();
            var geoms = new List<Geometry>();

            // Kolonlar
            foreach (var col in _model.Columns)
            {
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
                int polygonSectionId = ResolvePolygonPositionSectionId(floor.FloorNo, col.ColumnNo);
                if (col.ColumnType == 3)
                {
                    if (polygonSectionId <= 0 || !_model.PolygonColumnSectionByPositionSectionId.ContainsKey(polygonSectionId)) continue;
                }
                else
                {
                    if (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId)) continue;
                }

                var dim = sectionId > 0 && _model.ColumnDimsBySectionId.ContainsKey(sectionId)
                    ? _model.ColumnDimsBySectionId[sectionId]
                    : (W: 40.0, H: 40.0);
                double hw = dim.W / 2.0;
                double hh = dim.H / 2.0;
                var offsetLocal = col.ColumnType == 2
                    ? ComputeColumnOffsetCircle(col.OffsetXRaw, col.OffsetYRaw)
                    : ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                var center = new Point2d(axisNode.X + offsetGlobal.X + offsetX, axisNode.Y + offsetGlobal.Y + offsetY);

                Coordinate[] coords;
                if (col.ColumnType == 2)
                {
                    // Daire kolonu: bağ kirişi kesmek için kare yaklaşık.
                    var rect = BuildRect(center, hw, hh, col.AngleDeg);
                    coords = new Coordinate[5];
                    for (int i = 0; i < 4; i++) coords[i] = new Coordinate(rect[i].X, rect[i].Y);
                    coords[4] = coords[0];
                }
                else if (col.ColumnType == 3 && TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out var polyPoints))
                {
                    coords = new Coordinate[polyPoints.Length + 1];
                    for (int i = 0; i < polyPoints.Length; i++)
                        coords[i] = new Coordinate(polyPoints[i].X, polyPoints[i].Y);
                    coords[polyPoints.Length] = coords[0];
                }
                else
                {
                    var rect = BuildRect(center, hw, hh, col.AngleDeg);
                    coords = new Coordinate[5];
                    for (int i = 0; i < 4; i++) coords[i] = new Coordinate(rect[i].X, rect[i].Y);
                    coords[4] = coords[0];
                }
                geoms.Add(factory.CreatePolygon(factory.CreateLinearRing(coords)));
            }

            // Perdeler (duvarlar)
            var beamsWall = MergeSameIdBeamsOnFloor(floor.FloorNo);
            foreach (var beam in beamsWall)
            {
                if (beam.IsWallFlag != 1) continue;
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1) ||
                    !_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2))
                    continue;
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

                var coordsWall = new[]
                {
                    new Coordinate(q1.X, q1.Y),
                    new Coordinate(q2.X, q2.Y),
                    new Coordinate(q3.X, q3.Y),
                    new Coordinate(q4.X, q4.Y),
                    new Coordinate(q1.X, q1.Y)
                };
                geoms.Add(factory.CreatePolygon(factory.CreateLinearRing(coordsWall)));
            }

            if (geoms.Count == 0) return null;
            return geoms.Count == 1
                ? geoms[0]
                : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(geoms);
        }

        /// <summary>Verilen kattaki sadece kolonların birleşik alanını (NTS Geometry) döndürür. Perdeleri kolondan çıkartmak için kullanılır.</summary>
        private Geometry BuildKolonUnion(FloorInfo floor, double offsetX, double offsetY)
        {
            var factory = new GeometryFactory();
            var geoms = new List<Geometry>();

            foreach (var col in _model.Columns)
            {
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
                int polygonSectionId = ResolvePolygonPositionSectionId(floor.FloorNo, col.ColumnNo);
                // Perde kesimi için: formülle kesit bulunamazsa Columns Data sırasındaki ColumnId dene (örn. 37. kolon → 138)
                if (col.ColumnType != 3 && (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId)) &&
                    col.ColumnId > 0 && _model.ColumnDimsBySectionId.ContainsKey(col.ColumnId))
                    sectionId = col.ColumnId;
                if (col.ColumnType == 3)
                {
                    if (polygonSectionId <= 0 || !_model.PolygonColumnSectionByPositionSectionId.ContainsKey(polygonSectionId)) continue;
                }
                else
                {
                    if (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId)) continue;
                }

                var dim = sectionId > 0 && _model.ColumnDimsBySectionId.ContainsKey(sectionId)
                    ? _model.ColumnDimsBySectionId[sectionId]
                    : (W: 40.0, H: 40.0);
                double hw = dim.W / 2.0;
                double hh = dim.H / 2.0;
                var offsetLocal = col.ColumnType == 2
                    ? ComputeColumnOffsetCircle(col.OffsetXRaw, col.OffsetYRaw)
                    : ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                var center = new Point2d(axisNode.X + offsetGlobal.X + offsetX, axisNode.Y + offsetGlobal.Y + offsetY);

                Coordinate[] coords;
                if (col.ColumnType == 2)
                {
                    var rect = BuildRect(center, hw, hh, col.AngleDeg);
                    coords = new Coordinate[5];
                    for (int i = 0; i < 4; i++) coords[i] = new Coordinate(rect[i].X, rect[i].Y);
                    coords[4] = coords[0];
                }
                else if (col.ColumnType == 3 && TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out var polyPoints))
                {
                    coords = new Coordinate[polyPoints.Length + 1];
                    for (int i = 0; i < polyPoints.Length; i++)
                        coords[i] = new Coordinate(polyPoints[i].X, polyPoints[i].Y);
                    coords[polyPoints.Length] = coords[0];
                }
                else
                {
                    var rect = BuildRect(center, hw, hh, col.AngleDeg);
                    coords = new Coordinate[5];
                    for (int i = 0; i < 4; i++) coords[i] = new Coordinate(rect[i].X, rect[i].Y);
                    coords[4] = coords[0];
                }
                geoms.Add(factory.CreatePolygon(factory.CreateLinearRing(coords)));
            }

            if (geoms.Count == 0) return null;
            return geoms.Count == 1
                ? geoms[0]
                : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(geoms);
        }

        private void DrawBeamsAndWalls(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY)
        {
            var factory = new GeometryFactory();
            var wallList = new List<(Geometry poly, int fixedAxisId)>();
            var beams = MergeSameIdBeamsOnFloor(floor.FloorNo);
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

                if (beam.IsWallFlag == 1)
                {
                    var coordsWall = new[]
                    {
                        new Coordinate(q1.X, q1.Y),
                        new Coordinate(q2.X, q2.Y),
                        new Coordinate(q3.X, q3.Y),
                        new Coordinate(q4.X, q4.Y),
                        new Coordinate(q1.X, q1.Y)
                    };
                    wallList.Add((factory.CreatePolygon(factory.CreateLinearRing(coordsWall)), beam.FixedAxisId));
                    AppendEntity(tr, btr, MakeCenteredText(LayerYazi, 6, beam.BeamId.ToString(CultureInfo.InvariantCulture), center));
                }
                else
                {
                    AppendEntity(tr, btr, new Line(new Point3d(q1.X, q1.Y, 0), new Point3d(q2.X, q2.Y, 0)) { Layer = LayerKiris });
                    AppendEntity(tr, btr, new Line(new Point3d(q4.X, q4.Y, 0), new Point3d(q3.X, q3.Y, 0)) { Layer = LayerKiris });
                    AppendEntity(tr, btr, MakeCenteredText(LayerYazi, 6, beam.BeamId.ToString(CultureInfo.InvariantCulture), center));
                }
            }
            if (wallList.Count > 0)
            {
                Geometry kolonUnion = BuildKolonUnion(floor, offsetX, offsetY);
                foreach (var (wallPoly, fixedAxisId) in wallList)
                {
                    if (wallPoly == null || wallPoly.IsEmpty) continue;
                    Geometry toDraw = wallPoly;
                    if (kolonUnion != null && !kolonUnion.IsEmpty)
                    {
                        var diff = wallPoly.Difference(kolonUnion);
                        if (diff != null && !diff.IsEmpty)
                        {
                            toDraw = ReducePrecisionSafe(diff, 100);
                            if (toDraw == null || toDraw.IsEmpty) toDraw = diff;
                        }
                    }
                    if (toDraw != null && !toDraw.IsEmpty)
                        DrawGeometryRingsAsPolylines(tr, btr, toDraw, LayerPerde, addHatch: true, hatchAngleRad: GetAxisAngleRad(fixedAxisId));
                }
            }
        }

        /// <summary>
        /// Bu kata ait döşemeleri çizer. Köşeler sırayla: (axis1,axis3), (axis1,axis4), (axis2,axis4), (axis2,axis3).
        /// </summary>
        private void DrawSlabs(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY)
        {
            int floorNo = floor.FloorNo;
            foreach (var slab in _model.Slabs)
            {
                if (GetSlabFloorNo(slab.SlabId) != floorNo) continue;
                int a1 = slab.Axis1, a2 = slab.Axis2, a3 = slab.Axis3, a4 = slab.Axis4;
                Point2d[] pts = null;
                if (a1 != 0 && a2 != 0 && a3 != 0 && a4 != 0 &&
                    _axisService.TryIntersect(a1, a3, out Point2d p11) &&
                    _axisService.TryIntersect(a1, a4, out Point2d p12) &&
                    _axisService.TryIntersect(a2, a3, out Point2d p21) &&
                    _axisService.TryIntersect(a2, a4, out Point2d p22))
                {
                    pts = new[]
                    {
                        new Point2d(p11.X + offsetX, p11.Y + offsetY),
                        new Point2d(p12.X + offsetX, p12.Y + offsetY),
                        new Point2d(p22.X + offsetX, p22.Y + offsetY),
                        new Point2d(p21.X + offsetX, p21.Y + offsetY)
                    };
                }
                if (pts == null || pts.Length < 3) continue;
                var pl = ToPolyline(pts, true);
                pl.Layer = _model.StairSlabIds.Contains(slab.SlabId) ? LayerMerdiven : LayerDoseme;
                AppendEntity(tr, btr, pl);
                double cx = 0, cy = 0;
                for (int i = 0; i < pts.Length; i++) { cx += pts[i].X; cy += pts[i].Y; }
                var center = new Point3d(cx / pts.Length, cy / pts.Length, 0);
                AppendEntity(tr, btr, MakeCenteredText(LayerYazi, 5, slab.SlabId.ToString(CultureInfo.InvariantCulture), center));
            }
        }

        /// <summary>Sürekli temellerin tüm dikdörtgenlerinin birleşimi (offset uygulanmış); bağ kirişi içinde mi kontrolü için.</summary>
        private Geometry BuildContinuousFoundationsUnion(double offsetX, double offsetY)
        {
            var factory = new GeometryFactory();
            var polygons = new List<Geometry>();
            foreach (var cf in _model.ContinuousFoundations)
            {
                if (!_axisService.TryIntersect(cf.FixedAxisId, cf.StartAxisId, out Point2d p1) ||
                    !_axisService.TryIntersect(cf.FixedAxisId, cf.EndAxisId, out Point2d p2))
                    continue;
                Vector2d along = (p2 - p1).GetNormal();
                if (p1.GetDistanceTo(p2) <= 1e-9) continue;
                Point2d p1Eff = p1 - along.MultiplyBy(cf.StartExtensionCm);
                Point2d p2Eff = p2 + along.MultiplyBy(cf.EndExtensionCm);
                int offsetForBeam = (cf.FixedAxisId >= 1001 && cf.FixedAxisId <= 1999) ? -cf.OffsetRaw : cf.OffsetRaw;
                ComputeBeamEdgeOffsets(offsetForBeam, cf.WidthCm / 2.0, out double upperEdge, out double lowerEdge);
                Vector2d perp = new Vector2d(-along.Y, along.X);
                Point2d[] r = new[]
                {
                    p1Eff + perp.MultiplyBy(upperEdge),
                    p2Eff + perp.MultiplyBy(upperEdge),
                    p2Eff + perp.MultiplyBy(lowerEdge),
                    p1Eff + perp.MultiplyBy(lowerEdge)
                };
                var coords = new Coordinate[5];
                for (int i = 0; i < 4; i++)
                    coords[i] = new Coordinate(r[i].X + offsetX, r[i].Y + offsetY);
                coords[4] = coords[0];
                var ring = factory.CreateLinearRing(coords);
                polygons.Add(factory.CreatePolygon(ring));
            }
            if (polygons.Count == 0) return null;
            return polygons.Count == 1 ? polygons[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(polygons);
        }

        /// <summary>Radye temellerin (slab foundations) birleşik alanı (offset uygulanmış); bağ kirişi içinde mi kontrolü için.</summary>
        private Geometry BuildSlabFoundationsUnion(double offsetX, double offsetY)
        {
            var factory = new GeometryFactory();
            var polygons = new List<Geometry>();
            foreach (var sf in _model.SlabFoundations)
            {
                if (!_axisService.TryIntersect(sf.AxisX1, sf.AxisY1, out Point2d p11) ||
                    !_axisService.TryIntersect(sf.AxisX1, sf.AxisY2, out Point2d p12) ||
                    !_axisService.TryIntersect(sf.AxisX2, sf.AxisY1, out Point2d p21) ||
                    !_axisService.TryIntersect(sf.AxisX2, sf.AxisY2, out Point2d p22))
                    continue;
                var coords = new[]
                {
                    new Coordinate(p11.X + offsetX, p11.Y + offsetY),
                    new Coordinate(p21.X + offsetX, p21.Y + offsetY),
                    new Coordinate(p22.X + offsetX, p22.Y + offsetY),
                    new Coordinate(p12.X + offsetX, p12.Y + offsetY),
                    new Coordinate(p11.X + offsetX, p11.Y + offsetY)
                };
                var ring = factory.CreateLinearRing(coords);
                polygons.Add(factory.CreatePolygon(ring));
            }
            if (polygons.Count == 0) return null;
            return polygons.Count == 1 ? polygons[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(polygons);
        }

        /// <summary>Sürekli + radye + tekil temeller ve temel hatıllarının birleşimi (iç boşluklar korunur).</summary>
        private Geometry BuildTemelUnion(double offsetX, double offsetY, FloorInfo floorForSingleFootings)
        {
            var factory = new GeometryFactory();
            Geometry result = null;

            Geometry cf = BuildContinuousFoundationsUnion(offsetX, offsetY);
            if (cf != null && !cf.IsEmpty) result = cf;

            Geometry slab = BuildSlabFoundationsUnion(offsetX, offsetY);
            if (slab != null && !slab.IsEmpty) result = result == null ? slab : result.Union(slab);

            foreach (var sf in _model.SingleFootings)
            {
                if (!TryGetSingleFootingRect(sf, floorForSingleFootings, offsetX, offsetY, out Point2d[] rect)) continue;
                var coords = new Coordinate[5];
                for (int i = 0; i < 4; i++) coords[i] = new Coordinate(rect[i].X, rect[i].Y);
                coords[4] = coords[0];
                var poly = factory.CreatePolygon(factory.CreateLinearRing(coords));
                result = result == null ? poly : result.Union(poly);
            }

            // Bağ kirişleri (temel hatılları) de birleşime dahil edilir.
            foreach (var cfInfo in _model.ContinuousFoundations)
            {
                if (cfInfo.TieBeamWidthCm <= 0) continue;
                if (!_axisService.TryIntersect(cfInfo.FixedAxisId, cfInfo.StartAxisId, out Point2d p1) ||
                    !_axisService.TryIntersect(cfInfo.FixedAxisId, cfInfo.EndAxisId, out Point2d p2))
                    continue;
                Vector2d along = (p2 - p1).GetNormal();
                double len = p1.GetDistanceTo(p2);
                if (len <= 1e-9) continue;
                Vector2d perp = new Vector2d(-along.Y, along.X);
                ComputeTieBeamEdgeOffsets(cfInfo.FixedAxisId, cfInfo.TieBeamOffsetRaw, cfInfo.TieBeamWidthCm / 2.0, out double hu, out double hl);
                Point2d[] hatilRect = new[]
                {
                    p1 + perp.MultiplyBy(hu),
                    p2 + perp.MultiplyBy(hu),
                    p2 + perp.MultiplyBy(hl),
                    p1 + perp.MultiplyBy(hl)
                };
                var hatilCoords = new Coordinate[5];
                for (int i = 0; i < 4; i++)
                    hatilCoords[i] = new Coordinate(hatilRect[i].X + offsetX, hatilRect[i].Y + offsetY);
                hatilCoords[4] = hatilCoords[0];
                var hatilPoly = factory.CreatePolygon(factory.CreateLinearRing(hatilCoords));
                result = result == null ? hatilPoly : result.Union(hatilPoly);
            }

            // Bağımsız bağ kirişleri (TieBeams) — TEMEL (BEYKENT) katmanındakiler birleşime dahil.
            foreach (var tb in _model.TieBeams)
            {
                if (!_axisService.TryIntersect(tb.FixedAxisId, tb.StartAxisId, out Point2d p1) ||
                    !_axisService.TryIntersect(tb.FixedAxisId, tb.EndAxisId, out Point2d p2))
                    continue;
                Vector2d along = (p2 - p1).GetNormal();
                if (p1.GetDistanceTo(p2) <= 1e-9) continue;
                int offsetForBeam = (tb.FixedAxisId >= 1001 && tb.FixedAxisId <= 1999) ? -tb.OffsetRaw : tb.OffsetRaw;
                ComputeBeamEdgeOffsets(offsetForBeam, tb.WidthCm / 2.0, out double upperEdge, out double lowerEdge);
                Vector2d perp = new Vector2d(-along.Y, along.X);
                Point2d[] rect = new[]
                {
                    p1 + perp.MultiplyBy(upperEdge),
                    p2 + perp.MultiplyBy(upperEdge),
                    p2 + perp.MultiplyBy(lowerEdge),
                    p1 + perp.MultiplyBy(lowerEdge)
                };
                var coords = new Coordinate[5];
                for (int i = 0; i < 4; i++)
                    coords[i] = new Coordinate(rect[i].X + offsetX, rect[i].Y + offsetY);
                coords[4] = coords[0];
                var tbPoly = factory.CreatePolygon(factory.CreateLinearRing(coords));
                result = result == null ? tbPoly : result.Union(tbPoly);
            }

            return result;
        }

        private bool TryGetSingleFootingRect(SingleFootingInfo sf, FloorInfo floor, double offsetX, double offsetY, out Point2d[] rect)
        {
            rect = null;
            const double defaultHalfCm = 20.0;
            int positionIndex = sf.ColumnRef - 100;
            if (positionIndex < 1 || positionIndex > _model.ColumnAxisPositions.Count) return false;
            var pos = _model.ColumnAxisPositions[positionIndex - 1];
            if (!_axisService.TryIntersect(pos.AxisXId, pos.AxisYId, out Point2d axisNode)) return false;
            int colNo = positionIndex;
            int sectionId = ResolveColumnSectionId(floor.FloorNo, colNo);
            double hw = defaultHalfCm, hh = defaultHalfCm;
            if (sectionId > 0 && _model.ColumnDimsBySectionId.TryGetValue(sectionId, out var dim)) { hw = dim.W / 2.0; hh = dim.H / 2.0; }
            var offsetLocal = ComputeColumnOffset(pos.OffsetXRaw, pos.OffsetYRaw, hw, hh);
            var offsetGlobal = Rotate(offsetLocal, pos.AngleDeg);
            var columnCenter = new Point2d(axisNode.X + offsetGlobal.X, axisNode.Y + offsetGlobal.Y);
            double halfX = sf.SizeXCm / 2.0, halfY = sf.SizeYCm / 2.0;
            double cx = (sf.AlignX == 1) ? 1.0 : (sf.AlignX == 2) ? -1.0 : 0.0;
            double cy = (sf.AlignY == 1) ? -1.0 : (sf.AlignY == 2) ? 1.0 : 0.0;
            Point2d footingCenter;
            bool angledFooting = Math.Abs(sf.AngleDeg) > 0.01 || Math.Abs(pos.AngleDeg) > 0.01;
            if (angledFooting)
            {
                double angleRad = sf.AngleDeg * Math.PI / 180.0;
                Vector2d uFootX = new Vector2d(Math.Cos(angleRad), Math.Sin(angleRad));
                Vector2d uFootY = new Vector2d(-Math.Sin(angleRad), Math.Cos(angleRad));
                double[] corners_x = { -hw, hw, hw, -hw }, corners_y = { -hh, -hh, hh, hh };
                double minUx = double.MaxValue, maxUx = double.MinValue, minUy = double.MaxValue, maxUy = double.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    Vector2d v = Rotate(new Vector2d(corners_x[i], corners_y[i]), pos.AngleDeg);
                    double px = columnCenter.X + v.X, py = columnCenter.Y + v.Y;
                    double dux = px * uFootX.X + py * uFootX.Y, duy = px * uFootY.X + py * uFootY.Y;
                    if (dux < minUx) minUx = dux; if (dux > maxUx) maxUx = dux;
                    if (duy < minUy) minUy = duy; if (duy > maxUy) maxUy = duy;
                }
                double k1 = (sf.AlignX == 1) ? (maxUx - halfX) : (sf.AlignX == 2) ? (minUx + halfX) : (columnCenter.X * uFootX.X + columnCenter.Y * uFootX.Y);
                double k2 = (sf.AlignY == 1) ? (minUy + halfY) : (sf.AlignY == 2) ? (maxUy - halfY) : (columnCenter.X * uFootY.X + columnCenter.Y * uFootY.Y);
                footingCenter = new Point2d(k1 * uFootX.X + k2 * uFootY.X + offsetX, k1 * uFootX.Y + k2 * uFootY.Y + offsetY);
            }
            else
            {
                Vector2d columnVec = new Vector2d(cx * hw, cy * hh);
                Vector2d footingVec = new Vector2d(cx * halfX, cy * halfY);
                Vector2d alignGlobal = Rotate(columnVec, pos.AngleDeg) - Rotate(footingVec, sf.AngleDeg);
                footingCenter = new Point2d(columnCenter.X + alignGlobal.X + offsetX, columnCenter.Y + alignGlobal.Y + offsetY);
            }
            rect = BuildRect(footingCenter, halfX, halfY, sf.AngleDeg);
            return true;
        }

        private void DrawTemelMerged(Transaction tr, BlockTableRecord btr, double offsetX, double offsetY, FloorInfo floor, Geometry temelUnion = null)
        {
            const string layer = "TEMEL (BEYKENT)";
            Geometry unionResult = temelUnion ?? BuildTemelUnion(offsetX, offsetY, floor);
            if (unionResult == null || unionResult.IsEmpty) return;
            DrawGeometryRingsAsPolylines(tr, btr, unionResult, layer);
        }

        /// <summary>Kapalı bir halkanın (Coordinate dizisi) alanını cm² cinsinden döndürür (signed area, mutlak değer için Math.Abs kullan).</summary>
        private static double RingAreaCm2(Coordinate[] coords)
        {
            if (coords == null || coords.Length < 3) return 0.0;
            int n = coords.Length;
            if (n > 1 && coords[0].Equals2D(coords[n - 1])) n--;
            if (n < 3) return 0.0;
            double area = 0.0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += coords[i].X * coords[j].Y - coords[j].X * coords[i].Y;
            }
            return Math.Abs(area) * 0.5;
        }

        /// <summary>P noktasının AB doğru parçasına dik uzaklığını (cm) döndürür.</summary>
        private static double PointToSegmentDistance(Point2d p, Point2d a, Point2d b)
        {
            Vector2d ab = b - a;
            double len = ab.Length;
            if (len <= 1e-9) return p.GetDistanceTo(a);
            Vector2d ap = p - a;
            double t = (ap.X * ab.X + ap.Y * ab.Y) / (len * len);
            if (t <= 0.0) return p.GetDistanceTo(a);
            if (t >= 1.0) return p.GetDistanceTo(b);
            Point2d proj = new Point2d(a.X + t * ab.X, a.Y + t * ab.Y);
            return p.GetDistanceTo(proj);
        }

        /// <summary>NTS Geometry (Polygon/MultiPolygon) dış ve iç halkalarını verilen katmanda polyline olarak çizer; 4 mm'den kısa segmentleri atlar. addHatch true ise her halka için ANSI33 tarama eklenir. hatchAngleRad verilirse tarama açısı olarak kullanılır (perde: aks eğimi).</summary>
        private static void DrawGeometryRingsAsPolylines(Transaction tr, BlockTableRecord btr, Geometry geom, string layer, bool addHatch = false, double? hatchAngleRad = null)
        {
            if (geom == null || geom.IsEmpty) return;
            const double minSegmentLen = 0.4; // 4 mm = 0.4 cm (çizim birimi cm)
            const double parallelGapTol = 0.2; // 2 mm: neredeyse paralel iki kenar arasındaki mesafe
            var ringsToDraw = new List<Coordinate[]>();
            if (geom is Polygon poly)
            {
                ringsToDraw.Add(poly.ExteriorRing.Coordinates);
                for (int h = 0; h < poly.NumInteriorRings; h++)
                    ringsToDraw.Add(poly.InteriorRings[h].Coordinates);
            }
            else if (geom is MultiPolygon mp)
            {
                for (int i = 0; i < mp.NumGeometries; i++)
                {
                    var p = (Polygon)mp.GetGeometryN(i);
                    ringsToDraw.Add(p.ExteriorRing.Coordinates);
                    for (int h = 0; h < p.NumInteriorRings; h++)
                        ringsToDraw.Add(p.InteriorRings[h].Coordinates);
                }
            }
            else if (geom is GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                {
                    if (gc.GetGeometryN(i) is Polygon p2)
                    {
                        ringsToDraw.Add(p2.ExteriorRing.Coordinates);
                        for (int h = 0; h < p2.NumInteriorRings; h++)
                            ringsToDraw.Add(p2.InteriorRings[h].Coordinates);
                    }
                }
            }
            foreach (var coords in ringsToDraw)
            {
                if (coords == null || coords.Length < 3) continue;
                int n = coords.Length;
                if (n > 1 && coords[0].Equals2D(coords[n - 1])) n--;

                var pts = new List<Point2d>(n);
                for (int i = 0; i < n; i++)
                    pts.Add(new Point2d(coords[i].X, coords[i].Y));

                // 0) Üçgen (ABC) halkalarında: iki kenar neredeyse paralel (1°) ve arası < 2 mm ise saç kılı sayılır, çizilmez.
                if (pts.Count == 3)
                {
                    var a = pts[0];
                    var b = pts[1];
                    var c = pts[2];
                    double distBtoAC = PointToSegmentDistance(b, a, c);
                    double distCtoBA = PointToSegmentDistance(c, b, a);
                    double distAtoCB = PointToSegmentDistance(a, c, b);
                    if (distBtoAC < parallelGapTol || distCtoBA < parallelGapTol || distAtoCB < parallelGapTol)
                        continue;
                }

                // 1) Neredeyse paralel iki segmentin arasındaki çok ince kapalı alanı oluşturan
                // köşe noktalarını temizle: A-B-C-D dizisinde AB ve CD neredeyse paralel
                // ve aralarındaki mesafe 2 mm'den küçükse B ve C noktalarını sil.
                bool removed;
                int guard = 0;
                do
                {
                    removed = false;
                    if (pts.Count < 4) break;

                    // 1a) Doğrusal tarama (liste sonunu sarmadan).
                    for (int i = 1; i < pts.Count - 2; i++)
                    {
                        var a = pts[i - 1];
                        var b = pts[i];
                        var c = pts[i + 1];
                        var d = pts[i + 2];

                        Vector2d v1 = b - a;
                        Vector2d v2 = d - c;
                        double len1 = v1.Length;
                        double len2 = v2.Length;
                        if (len1 < 1e-6 || len2 < 1e-6) continue;

                        double dot = (v1.X * v2.X + v1.Y * v2.Y) / (len1 * len2);
                        if (dot > 1.0) dot = 1.0;
                        if (dot < -1.0) dot = -1.0;
                        double angle = Math.Acos(dot); // radyan
                        // Neredeyse paralel: açı ~0 veya ~pi (1° tolerans)
                        double tolRad = 1.0 * Math.PI / 180.0;
                        if (angle > tolRad && Math.Abs(Math.PI - angle) > tolRad)
                            continue;

                        // AB doğrusu ile BC orta noktasının arasındaki dik mesafe: iki paralel kenar aralığı için iyi bir yaklaşım.
                        var midBC = new Point2d((b.X + c.X) * 0.5, (b.Y + c.Y) * 0.5);
                        double num = Math.Abs((midBC.X - a.X) * (b.Y - a.Y) - (midBC.Y - a.Y) * (b.X - a.X));
                        double gap = num / len1;
                        if (gap < parallelGapTol)
                        {
                            pts.RemoveAt(i + 1); // C
                            pts.RemoveAt(i);     // B
                            removed = true;
                            break;
                        }
                    }

                    // 1b) Eğer hâlâ silme olmadıysa, ring sonu-başı arasında saran ABCD dörtlülerini kontrol et.
                    if (!removed && pts.Count >= 4)
                    {
                        int m = pts.Count;
                        for (int i = 0; i < m; i++)
                        {
                            int ia = (i - 1 + m) % m;
                            int ib = i;
                            int ic = (i + 1) % m;
                            int id = (i + 2) % m;

                            var a = pts[ia];
                            var b = pts[ib];
                            var c = pts[ic];
                            var d = pts[id];

                            Vector2d v1 = b - a;
                            Vector2d v2 = d - c;
                            double len1 = v1.Length;
                            double len2 = v2.Length;
                            if (len1 < 1e-6 || len2 < 1e-6) continue;

                            double dot = (v1.X * v2.X + v1.Y * v2.Y) / (len1 * len2);
                            if (dot > 1.0) dot = 1.0;
                            if (dot < -1.0) dot = -1.0;
                            double angle = Math.Acos(dot);
                            double tolRad = 1.0 * Math.PI / 180.0;
                            if (angle > tolRad && Math.Abs(Math.PI - angle) > tolRad)
                                continue;

                            var midBC = new Point2d((b.X + c.X) * 0.5, (b.Y + c.Y) * 0.5);
                            double num = Math.Abs((midBC.X - a.X) * (b.Y - a.Y) - (midBC.Y - a.Y) * (b.X - a.X));
                            double gap = num / len1;
                            if (gap < parallelGapTol)
                            {
                                // Dikkat: dairesel listede indeksleri küçükten büyüğe sil.
                                int first = Math.Min(ib, ic);
                                int second = Math.Max(ib, ic);
                                pts.RemoveAt(second);
                                pts.RemoveAt(first);
                                removed = true;
                                break;
                            }
                        }
                    }
                } while (removed && ++guard < 10);

                // 1c) Nihai kural: Bir vertex'e bağlı iki segment neredeyse paralel (<1°) ise bu vertex gereksizdir (neredeyse doğrusal), kaldır.
                const double vertexAngleTolRad = 1.0 * Math.PI / 180.0; // 1°
                int guardAngle = 0;
                while (guardAngle++ < 20 && pts.Count >= 4)
                {
                    bool removedAngle = false;
                    int m = pts.Count;
                    for (int i = 0; i < m; i++)
                    {
                        var a = pts[(i - 1 + m) % m];
                        var b = pts[i];
                        var c = pts[(i + 1) % m];
                        Vector2d v1 = b - a;
                        Vector2d v2 = c - b;
                        double len1 = v1.Length;
                        double len2 = v2.Length;
                        if (len1 < 1e-6 || len2 < 1e-6) continue;
                        double dot = (v1.X * v2.X + v1.Y * v2.Y) / (len1 * len2);
                        if (dot > 1.0) dot = 1.0;
                        if (dot < -1.0) dot = -1.0;
                        double angle = Math.Acos(dot);
                        // Açı ~0 (aynı yön) veya ~pi (zıt yön) => neredeyse doğrusal, B'yi kaldır
                        if (angle < vertexAngleTolRad || angle > Math.PI - vertexAngleTolRad)
                        {
                            pts.RemoveAt(i);
                            removedAngle = true;
                            break;
                        }
                    }
                    if (!removedAngle) break;
                }

                // 2) Çok kısa segmentleri filtrele.
                var filtered = new List<Point2d>(pts.Count);
                for (int i = 0; i < pts.Count; i++)
                {
                    var p = pts[i];
                    if (filtered.Count == 0 ||
                        filtered[filtered.Count - 1].GetDistanceTo(p) >= minSegmentLen)
                    {
                        filtered.Add(p);
                    }
                }
                if (filtered.Count < 3) continue; // Kapalı halka için en az 3 nokta; 2'ye inen saç kılı çizilmez

                var pl = ToPolyline(filtered.ToArray(), true);
                pl.Layer = layer;
                if (addHatch)
                {
                    ObjectId plId = AppendEntityReturnId(tr, btr, pl);
                    double angleRad = hatchAngleRad ?? Math.Atan2(filtered[1].Y - filtered[0].Y, filtered[1].X - filtered[0].X);
                    AppendHatchAnsi33(tr, btr, plId, angleRad);
                }
                else
                    AppendEntity(tr, btr, pl);
            }
        }

        private void DrawContinuousFoundations(Transaction tr, BlockTableRecord btr, double offsetX, double offsetY, FloorInfo floor, bool drawTemelOutline = true, Geometry temelUnion = null, Geometry kolonPerdeUnion = null)
        {
            const string layer = "TEMEL (BEYKENT)";
            const string layerAmpatman = "TEMEL AMPATMAN (BEYKENT)";
            var factory = new GeometryFactory();
            var ampatmanPolygons = new List<Geometry>();
            foreach (var cf in _model.ContinuousFoundations)
            {
                if (!_axisService.TryIntersect(cf.FixedAxisId, cf.StartAxisId, out Point2d p1) ||
                    !_axisService.TryIntersect(cf.FixedAxisId, cf.EndAxisId, out Point2d p2))
                    continue;
                Vector2d along = (p2 - p1).GetNormal();
                double len = p1.GetDistanceTo(p2);
                if (len <= 1e-9) continue;
                Point2d p1Eff = p1 - along.MultiplyBy(cf.StartExtensionCm);
                Point2d p2Eff = p2 + along.MultiplyBy(cf.EndExtensionCm);
                // 1 yönü aksı (X: 1001-1999) üzerindeki sürekli temellerde kaçıklık ters; Y ekseninde normal.
                int offsetForBeam = (cf.FixedAxisId >= 1001 && cf.FixedAxisId <= 1999) ? -cf.OffsetRaw : cf.OffsetRaw;
                ComputeBeamEdgeOffsets(offsetForBeam, cf.WidthCm / 2.0, out double upperEdge, out double lowerEdge);
                Vector2d perp = new Vector2d(-along.Y, along.X);
                Point2d[] rect = new[]
                {
                    p1Eff + perp.MultiplyBy(upperEdge),
                    p2Eff + perp.MultiplyBy(upperEdge),
                    p2Eff + perp.MultiplyBy(lowerEdge),
                    p1Eff + perp.MultiplyBy(lowerEdge)
                };
                for (int i = 0; i < rect.Length; i++)
                    rect[i] = new Point2d(rect[i].X + offsetX, rect[i].Y + offsetY);
                if (drawTemelOutline)
                {
                    var pl = ToPolyline(rect, true);
                    pl.Layer = layer;
                    AppendEntity(tr, btr, pl);
                }

                if (!string.IsNullOrEmpty(cf.Name))
                {
                    double cx = (rect[0].X + rect[1].X + rect[2].X + rect[3].X) / 4.0;
                    double cy = (rect[0].Y + rect[1].Y + rect[2].Y + rect[3].Y) / 4.0;
                    AppendEntity(tr, btr, MakeCenteredText(LayerYazi, 5, cf.Name.Trim(), new Point3d(cx, cy, 0)));
                }

                if (cf.AmpatmanWidthCm > 0 && Math.Abs(cf.AmpatmanWidthCm - cf.WidthCm) > 1e-6)
                {
                    double ampW = cf.AmpatmanWidthCm;
                    int align = cf.AmpatmanAlign;
                    if (cf.FixedAxisId >= 1001 && cf.FixedAxisId <= 1999 && align != 0)
                        align = align == 1 ? 2 : 1;
                    Point2d[] ampRect;
                    if (align == 0)
                    {
                        double hwAmp = ampW / 2.0;
                        ampRect = new[]
                        {
                            p1Eff + perp.MultiplyBy(hwAmp),
                            p2Eff + perp.MultiplyBy(hwAmp),
                            p2Eff - perp.MultiplyBy(hwAmp),
                            p1Eff - perp.MultiplyBy(hwAmp)
                        };
                    }
                    else if (align == 1)
                    {
                        ampRect = new[]
                        {
                            p1Eff + perp.MultiplyBy(lowerEdge),
                            p2Eff + perp.MultiplyBy(lowerEdge),
                            p2Eff + perp.MultiplyBy(lowerEdge + ampW),
                            p1Eff + perp.MultiplyBy(lowerEdge + ampW)
                        };
                    }
                    else
                    {
                        ampRect = new[]
                        {
                            p1Eff + perp.MultiplyBy(upperEdge),
                            p2Eff + perp.MultiplyBy(upperEdge),
                            p2Eff + perp.MultiplyBy(upperEdge - ampW),
                            p1Eff + perp.MultiplyBy(upperEdge - ampW)
                        };
                    }
                    for (int i = 0; i < ampRect.Length; i++)
                        ampRect[i] = new Point2d(ampRect[i].X + offsetX, ampRect[i].Y + offsetY);
                    var ampCoords = new Coordinate[5];
                    for (int i = 0; i < 4; i++) ampCoords[i] = new Coordinate(ampRect[i].X, ampRect[i].Y);
                    ampCoords[4] = ampCoords[0];
                    var ampPoly = factory.CreatePolygon(factory.CreateLinearRing(ampCoords));
                    ampatmanPolygons.Add(ampPoly);
                }

                if (cf.TieBeamWidthCm > 0)
                {
                    ComputeTieBeamEdgeOffsets(cf.FixedAxisId, cf.TieBeamOffsetRaw, cf.TieBeamWidthCm / 2.0, out double hu, out double hl);
                    Point2d[] hatilRect = new[]
                    {
                        p1 + perp.MultiplyBy(hu),
                        p2 + perp.MultiplyBy(hu),
                        p2 + perp.MultiplyBy(hl),
                        p1 + perp.MultiplyBy(hl)
                    };
                    for (int i = 0; i < hatilRect.Length; i++)
                        hatilRect[i] = new Point2d(hatilRect[i].X + offsetX, hatilRect[i].Y + offsetY);
                    var hatilCoords = new Coordinate[5];
                    for (int i = 0; i < 4; i++)
                        hatilCoords[i] = new Coordinate(hatilRect[i].X, hatilRect[i].Y);
                    hatilCoords[4] = hatilCoords[0];
                    var hatilPoly = factory.CreatePolygon(factory.CreateLinearRing(hatilCoords));

                    Geometry toDrawHatil = hatilPoly;
                    if (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty)
                    {
                        var diffH = hatilPoly.Difference(kolonPerdeUnion);
                        if (diffH == null || diffH.IsEmpty) continue;
                        toDrawHatil = diffH;
                    }
                    DrawGeometryRingsAsPolylines(tr, btr, toDrawHatil, "TEMEL HATILI (BEYKENT)");
                }
            }

            // Tüm ampatmanları birleştir, birleşik temel içinde kalan kısımları çıkar, tek çizim olarak çiz.
            if (ampatmanPolygons.Count > 0)
            {
                Geometry ampatmanUnion = ampatmanPolygons.Count == 1
                    ? ampatmanPolygons[0]
                    : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(ampatmanPolygons);
                if (ampatmanUnion != null && !ampatmanUnion.IsEmpty)
                {
                    Geometry toDraw = (temelUnion != null && !temelUnion.IsEmpty)
                        ? ampatmanUnion.Difference(temelUnion)
                        : ampatmanUnion;
                    if (toDraw != null && !toDraw.IsEmpty)
                        DrawGeometryRingsAsPolylines(tr, btr, toDraw, layerAmpatman);
                }
            }
        }

        /// <summary>Tekil temelleri kolon listesiyle ilişkilendirmeden çizer: konum Column axis data satır indeksi (ColumnRef-100), boyutlar Single footings'ten. Kolon boyutu aynı pozisyondaki kolon kesitinden (ResolveColumnSectionId) alınır; yoksa 20 cm.</summary>
        private void DrawSingleFootings(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY, bool drawTemelOutline = true)
        {
            const string layer = "TEMEL (BEYKENT)";
            const double defaultHalfCm = 20.0;
            foreach (var sf in _model.SingleFootings)
            {
                int positionIndex = sf.ColumnRef - 100;
                if (positionIndex < 1 || positionIndex > _model.ColumnAxisPositions.Count) continue;
                var pos = _model.ColumnAxisPositions[positionIndex - 1];
                if (!_axisService.TryIntersect(pos.AxisXId, pos.AxisYId, out Point2d axisNode)) continue;

                int colNo = positionIndex;
                int sectionId = ResolveColumnSectionId(floor.FloorNo, colNo);
                double hw = defaultHalfCm, hh = defaultHalfCm;
                if (sectionId > 0 && _model.ColumnDimsBySectionId.TryGetValue(sectionId, out var dim))
                {
                    hw = dim.W / 2.0;
                    hh = dim.H / 2.0;
                }
                var offsetLocal = ComputeColumnOffset(pos.OffsetXRaw, pos.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, pos.AngleDeg);
                var columnCenter = new Point2d(axisNode.X + offsetGlobal.X, axisNode.Y + offsetGlobal.Y);

                double halfX = sf.SizeXCm / 2.0;
                double halfY = sf.SizeYCm / 2.0;
                // Açısız: mevcut köşe hizalaması (değiştirme). Açılı: tek köşe çakıştırma — AlignX 1=sol, 2=sağ; AlignY 1=alt, 2=üst; seçilen köşe aynı anda hem X hem Y doğru olur.
                double cx = 0.0, cy = 0.0;
                if (sf.AlignX == 1) cx = 1.0;
                else if (sf.AlignX == 2) cx = -1.0;
                if (sf.AlignY == 1) cy = -1.0;
                else if (sf.AlignY == 2) cy = 1.0;

                Point2d footingCenter;
                bool angledFooting = Math.Abs(sf.AngleDeg) > 0.01 || Math.Abs(pos.AngleDeg) > 0.01;
                if (angledFooting)
                {
                    // Açılı: temel kenarları kolonun en uç noktalarından geçer (dört köşe temel içinde). X yönü kaçıklığı açılıda ters: 1=sağ kenar, 2=sol kenar.
                    double angleRad = sf.AngleDeg * Math.PI / 180.0;
                    Vector2d uFootX = new Vector2d(Math.Cos(angleRad), Math.Sin(angleRad));
                    Vector2d uFootY = new Vector2d(-Math.Sin(angleRad), Math.Cos(angleRad));

                    // Kolonun dört köşesini dünya koordinatında hesapla; temel yerel X/Y'de min/max bul.
                    double[] corners_x = { -hw, hw, hw, -hw };
                    double[] corners_y = { -hh, -hh, hh, hh };
                    double minUx = double.MaxValue, maxUx = double.MinValue, minUy = double.MaxValue, maxUy = double.MinValue;
                    for (int i = 0; i < 4; i++)
                    {
                        Vector2d v = Rotate(new Vector2d(corners_x[i], corners_y[i]), pos.AngleDeg);
                        double px = columnCenter.X + v.X, py = columnCenter.Y + v.Y;
                        double dux = px * uFootX.X + py * uFootX.Y;
                        double duy = px * uFootY.X + py * uFootY.Y;
                        if (dux < minUx) minUx = dux;
                        if (dux > maxUx) maxUx = dux;
                        if (duy < minUy) minUy = duy;
                        if (duy > maxUy) maxUy = duy;
                    }
                    // Açılıda X kaçıklığı ters: AlignX=1 → sağ kenar (max), AlignX=2 → sol kenar (min).
                    double k1 = (sf.AlignX == 1) ? (maxUx - halfX) : (sf.AlignX == 2) ? (minUx + halfX) : (columnCenter.X * uFootX.X + columnCenter.Y * uFootX.Y);
                    double k2 = (sf.AlignY == 1) ? (minUy + halfY) : (sf.AlignY == 2) ? (maxUy - halfY) : (columnCenter.X * uFootY.X + columnCenter.Y * uFootY.Y);

                    footingCenter = new Point2d(k1 * uFootX.X + k2 * uFootY.X + offsetX, k1 * uFootX.Y + k2 * uFootY.Y + offsetY);
                }
                else
                {
                    Vector2d columnVec = new Vector2d(cx * hw, cy * hh);
                    Vector2d footingVec = new Vector2d(cx * halfX, cy * halfY);
                    Vector2d alignGlobal = Rotate(columnVec, pos.AngleDeg) - Rotate(footingVec, sf.AngleDeg);
                    footingCenter = new Point2d(columnCenter.X + alignGlobal.X + offsetX, columnCenter.Y + alignGlobal.Y + offsetY);
                }

                var rect = BuildRect(footingCenter, halfX, halfY, sf.AngleDeg);
                if (drawTemelOutline)
                {
                    var pl = ToPolyline(rect, true);
                    pl.Layer = layer;
                    AppendEntity(tr, btr, pl);
                }
                if (!string.IsNullOrEmpty(sf.Name))
                    AppendEntity(tr, btr, MakeCenteredText(LayerYazi, 5, sf.Name.Trim(), new Point3d(footingCenter.X, footingCenter.Y, 0)));
            }
        }

        private void DrawTieBeams(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY, Geometry kolonPerdeUnion = null)
        {
            const string layerTemel = "TEMEL (BEYKENT)";
            const string layerHatili = "TEMEL HATILI (BEYKENT)";
            var factory = new GeometryFactory();
            Geometry cfUnion = BuildContinuousFoundationsUnion(offsetX, offsetY);
            Geometry slabUnion = BuildSlabFoundationsUnion(offsetX, offsetY);
            var hatiliPolygons = new List<Geometry>();

            int tbIndex = 0;
            foreach (var tb in _model.TieBeams)
            {
                tbIndex++;
                if (!_axisService.TryIntersect(tb.FixedAxisId, tb.StartAxisId, out Point2d p1) ||
                    !_axisService.TryIntersect(tb.FixedAxisId, tb.EndAxisId, out Point2d p2))
                    continue;
                Vector2d along = (p2 - p1).GetNormal();
                if (p1.GetDistanceTo(p2) <= 1e-9) continue;
                int offsetForBeam = (tb.FixedAxisId >= 1001 && tb.FixedAxisId <= 1999) ? -tb.OffsetRaw : tb.OffsetRaw;
                ComputeBeamEdgeOffsets(offsetForBeam, tb.WidthCm / 2.0, out double upperEdge, out double lowerEdge);
                Vector2d perp = new Vector2d(-along.Y, along.X);
                Point2d[] rect = new[]
                {
                    p1 + perp.MultiplyBy(upperEdge),
                    p2 + perp.MultiplyBy(upperEdge),
                    p2 + perp.MultiplyBy(lowerEdge),
                    p1 + perp.MultiplyBy(lowerEdge)
                };
                for (int i = 0; i < rect.Length; i++)
                    rect[i] = new Point2d(rect[i].X + offsetX, rect[i].Y + offsetY);

                double cx = (rect[0].X + rect[1].X + rect[2].X + rect[3].X) / 4.0;
                double cy = (rect[0].Y + rect[1].Y + rect[2].Y + rect[3].Y) / 4.0;
                string label = !string.IsNullOrWhiteSpace(tb.Name) ? tb.Name.Trim() : ("TB" + tbIndex);
                AppendEntity(tr, btr, MakeCenteredText(LayerYazi, 5, label, new Point3d(cx, cy, 0)));

                var coords = new Coordinate[5];
                for (int i = 0; i < 4; i++)
                    coords[i] = new Coordinate(rect[i].X, rect[i].Y);
                coords[4] = coords[0];
                var tbPoly = factory.CreatePolygon(factory.CreateLinearRing(coords));

                // Tamamen sürekli temel veya radye alanı içinde → TEMEL HATILI; ne sürekli ne radye içinde → TEMEL (BEYKENT).
                bool insideContinuous = cfUnion != null && !cfUnion.IsEmpty && cfUnion.Contains(tbPoly);
                bool insideSlab = slabUnion != null && !slabUnion.IsEmpty && slabUnion.Contains(tbPoly);
                string layer = (insideContinuous || insideSlab) ? layerHatili : layerTemel;

                // TEMEL (BEYKENT) katmanındaki bağ kirişleri birleşik çizimde zaten var; TEMEL HATILI olanları topla, sonra tek Union ile çiz.
                if (layer == layerHatili)
                    hatiliPolygons.Add(tbPoly);
            }

            // Bitişik TEMEL HATILI bağ kirişlerini Union ile birleştirip tek geometri olarak çiz (BK18-BK47 gibi).
            if (hatiliPolygons.Count > 0)
            {
                Geometry unionHatili = hatiliPolygons.Count == 1 ? hatiliPolygons[0] : CascadedPolygonUnion.Union(hatiliPolygons);
                if (unionHatili != null && !unionHatili.IsEmpty)
                {
                    Geometry toDraw = unionHatili;
                    if (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty)
                    {
                        var diff = unionHatili.Difference(kolonPerdeUnion);
                        if (diff != null && !diff.IsEmpty)
                            toDraw = diff;
                    }
                    if (toDraw != null && !toDraw.IsEmpty)
                        DrawGeometryRingsAsPolylines(tr, btr, toDraw, layerHatili);
                }
            }
        }

        private void DrawSlabFoundations(Transaction tr, BlockTableRecord btr, double offsetX, double offsetY, bool drawTemelOutline = true)
        {
            const string layer = "TEMEL (BEYKENT)";
            var factory = new GeometryFactory();
            var polygons = new List<Geometry>();
            foreach (var sf in _model.SlabFoundations)
            {
                if (!_axisService.TryIntersect(sf.AxisX1, sf.AxisY1, out Point2d p11) ||
                    !_axisService.TryIntersect(sf.AxisX1, sf.AxisY2, out Point2d p12) ||
                    !_axisService.TryIntersect(sf.AxisX2, sf.AxisY1, out Point2d p21) ||
                    !_axisService.TryIntersect(sf.AxisX2, sf.AxisY2, out Point2d p22))
                    continue;
                double cx = (p11.X + p12.X + p21.X + p22.X) / 4.0 + offsetX;
                double cy = (p11.Y + p12.Y + p21.Y + p22.Y) / 4.0 + offsetY;
                if (!string.IsNullOrEmpty(sf.Name))
                    AppendEntity(tr, btr, MakeCenteredText(LayerYazi, 5, sf.Name.Trim(), new Point3d(cx, cy, 0)));
                var coords = new[]
                {
                    new Coordinate(p11.X, p11.Y),
                    new Coordinate(p21.X, p21.Y),
                    new Coordinate(p22.X, p22.Y),
                    new Coordinate(p12.X, p12.Y),
                    new Coordinate(p11.X, p11.Y)
                };
                var ring = factory.CreateLinearRing(coords);
                polygons.Add(factory.CreatePolygon(ring));
            }
            if (polygons.Count == 0) return;
            if (!drawTemelOutline) return;
            Geometry unionResult = polygons.Count == 1 ? polygons[0] : CascadedPolygonUnion.Union(polygons);
            if (unionResult == null || unionResult.IsEmpty) return;
            var toDraw = new List<Coordinate[]>();
            if (unionResult is Polygon p)
            {
                toDraw.Add(p.ExteriorRing.Coordinates);
            }
            else if (unionResult is MultiPolygon mp)
            {
                for (int i = 0; i < mp.NumGeometries; i++)
                {
                    var poly = (Polygon)mp.GetGeometryN(i);
                    toDraw.Add(poly.ExteriorRing.Coordinates);
                }
            }
            else if (unionResult is GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                {
                    var g = gc.GetGeometryN(i);
                    if (g is Polygon p2)
                        toDraw.Add(p2.ExteriorRing.Coordinates);
                }
            }
            foreach (var coords in toDraw)
            {
                if (coords == null || coords.Length < 3) continue;
                var pts = new Point2d[coords.Length - 1];
                for (int i = 0; i < pts.Length; i++)
                    pts[i] = new Point2d(coords[i].X + offsetX, coords[i].Y + offsetY);
                var pl = ToPolyline(pts, true);
                pl.Layer = layer;
                AppendEntity(tr, btr, pl);
            }
        }

        /// <summary>Dosya 5. satır 3. sütun (SlabFloorKeyStep 100/1000) varsa slabId/step, yoksa slabId/1000 veya slabId/100.</summary>
        private int GetSlabFloorNo(int slabId)
        {
            if (_model.SlabFloorKeyStep > 0) return slabId / _model.SlabFloorKeyStep;
            return slabId >= 1000 ? (slabId / 1000) : (slabId / 100);
        }

        /// <summary>5. satır 4. sütun (BeamFloorKeyStep) varsa beamId/step; yoksa beamId/1000 veya beamId/100.</summary>
        private int GetBeamFloorNo(int beamId)
        {
            if (_model.BeamFloorKeyStep > 0) return beamId / _model.BeamFloorKeyStep;
            return beamId >= 1000 ? (beamId / 1000) : (beamId / 100);
        }

        private void DrawFloorTitle(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY,
            (double Xmin, double Xmax, double Ymin, double Ymax) ext, bool isFoundationPlan = false)
        {
            var titlePos = new Point3d(offsetX + (ext.Xmin + ext.Xmax) / 2.0, offsetY + ext.Ymax + 45, 0);
            string title = isFoundationPlan
                ? "TEMEL PLANI (aks + 1. kat kolon + surekli/radye temel)"
                : string.Format(CultureInfo.InvariantCulture, "{0} ({1}m)", floor.Name, floor.ElevationM.ToString("0", CultureInfo.InvariantCulture));
            AppendEntity(tr, btr, MakeCenteredText(LayerBaslik, 12, title, titlePos));
        }

        private static DBText MakeCenteredText(string layer, double height, string value, Point3d p)
        {
            return new DBText
            {
                Layer = layer,
                Height = height,
                TextString = value ?? string.Empty,
                Position = p,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                AlignmentPoint = p
            };
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
            if (!_model.PolygonColumnSectionByPositionSectionId.TryGetValue(sectionId, out int polygonSectionId)) return false;
            if (!_model.PolygonSections.TryGetValue(polygonSectionId, out List<Point2d> localPoints) || localPoints.Count < 3) return false;

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

        /// <summary>Daire kolon (ColumnType=2): kaçıklık eksenden merkeze doğrudan mesafe (mm → cm). Y yönü ST4 ile ters.</summary>
        private static Vector2d ComputeColumnOffsetCircle(int off1, int off2)
        {
            return new Vector2d(off1 / 10.0, -off2 / 10.0);
        }

        private static double ComputeColumnAxisOffsetX(int off, double halfSize)
        {
            if (off == -1) return halfSize;
            if (off == 1) return -halfSize;
            if (off == 0) return 0.0;
            double offCm = Math.Abs(off) / 10.0;
            return off > 1 ? offCm - halfSize : halfSize - offCm;
        }

        private static double ComputeColumnAxisOffsetY(int off, double halfSize)
        {
            if (off == -1) return -halfSize;
            if (off == 1) return halfSize;
            if (off == 0) return 0.0;
            double offCm = Math.Abs(off) / 10.0;
            return off > 1 ? halfSize - offCm : -halfSize + offCm;
        }

        private static void ComputeBeamEdgeOffsets(int off, double hw, out double upperEdge, out double lowerEdge)
        {
            if (off == -1) { upperEdge = 0.0; lowerEdge = -2.0 * hw; return; }
            if (off == 1) { upperEdge = 2.0 * hw; lowerEdge = 0.0; return; }
            if (off == 0) { upperEdge = hw; lowerEdge = -hw; return; }
            double offCm = off / 10.0;
            if (off > 1) { lowerEdge = -offCm; upperEdge = lowerEdge + (2.0 * hw); return; }
            upperEdge = -offCm; lowerEdge = upperEdge - (2.0 * hw);
        }

        /// <summary>Temel hatılı 13. sütun: X/Y aksına göre 0=ortada, ±1=kenar aks üzerinde, &gt;1/&lt;-1=mm mesafe.</summary>
        private static void ComputeTieBeamEdgeOffsets(int fixedAxisId, int offsetRaw, double hw, out double upperEdge, out double lowerEdge)
        {
            bool isX = fixedAxisId >= 1001 && fixedAxisId <= 1999;
            if (offsetRaw == 0) { upperEdge = hw; lowerEdge = -hw; return; }
            double offCm = offsetRaw / 10.0;
            if (isX)
            {
                if (offsetRaw == -1) { lowerEdge = 0; upperEdge = 2.0 * hw; return; }
                if (offsetRaw == 1) { upperEdge = 0; lowerEdge = -2.0 * hw; return; }
                if (offsetRaw > 1) { lowerEdge = -offCm; upperEdge = lowerEdge + 2.0 * hw; return; }
                upperEdge = -offCm; lowerEdge = upperEdge - 2.0 * hw; return;
            }
            if (offsetRaw == -1) { upperEdge = 0; lowerEdge = -2.0 * hw; return; }
            if (offsetRaw == 1) { lowerEdge = 0; upperEdge = 2.0 * hw; return; }
            if (offsetRaw > 1) { upperEdge = offCm; lowerEdge = upperEdge - 2.0 * hw; return; }
            lowerEdge = offCm; upperEdge = lowerEdge + 2.0 * hw;
        }

        private static void NormalizeBeamDirection(int fixedAxisId, ref Point2d a, ref Point2d b)
        {
            bool isFixedY = fixedAxisId >= 2001 && fixedAxisId <= 2999;
            bool isFixedX = fixedAxisId >= 1001 && fixedAxisId <= 1999;
            if (isFixedY && a.X > b.X) { (a, b) = (b, a); }
            else if (isFixedX && a.Y > b.Y) { (a, b) = (b, a); }
        }

        private static Vector2d Rotate(Vector2d v, double angleDeg)
        {
            double a = angleDeg * Math.PI / 180.0;
            return new Vector2d(v.X * Math.Cos(a) - v.Y * Math.Sin(a), v.X * Math.Sin(a) + v.Y * Math.Cos(a));
        }

        /// <summary>Merkezi (0,0) olan, yarı genişlik hx, yarı yükseklik hy ve angleDeg açılı dikdörtgenin dünya koordinatında min/max X ve Y değerlerini verir.</summary>
        private static void GetRotatedRectBounds(double hx, double hy, double angleDeg, out double minX, out double maxX, out double minY, out double maxY)
        {
            var c1 = Rotate(new Vector2d(hx, hy), angleDeg);
            var c2 = Rotate(new Vector2d(hx, -hy), angleDeg);
            var c3 = Rotate(new Vector2d(-hx, hy), angleDeg);
            var c4 = Rotate(new Vector2d(-hx, -hy), angleDeg);
            minX = Math.Min(Math.Min(c1.X, c2.X), Math.Min(c3.X, c4.X));
            maxX = Math.Max(Math.Max(c1.X, c2.X), Math.Max(c3.X, c4.X));
            minY = Math.Min(Math.Min(c1.Y, c2.Y), Math.Min(c3.Y, c4.Y));
            maxY = Math.Max(Math.Max(c1.Y, c2.Y), Math.Max(c3.Y, c4.Y));
        }

        private static Polyline ToPolyline(IReadOnlyList<Point2d> points, bool closed)
        {
            var pl = new Polyline();
            for (int i = 0; i < points.Count; i++)
                pl.AddVertexAt(i, points[i], 0, 0, 0);
            pl.Closed = closed;
            return pl;
        }

        private static void AppendEntity(Transaction tr, BlockTableRecord btr, Entity e)
        {
            btr.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
        }

        /// <summary>Entity ekler ve ObjectId döndürür (tarama boundary için).</summary>
        private static ObjectId AppendEntityReturnId(Transaction tr, BlockTableRecord btr, Entity e)
        {
            btr.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
            return e.ObjectId;
        }

        /// <summary>Kolon ve perde taraması: ANSI33, katman TARAMA (BEYKENT). patternAngleRad: taranan elemanın açısı (radyan).</summary>
        private static void AppendHatchAnsi33(Transaction tr, BlockTableRecord btr, ObjectId boundaryId, double patternAngleRad = 0)
        {
            var hatch = new Hatch();
            btr.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "ANSI33");
            hatch.PatternAngle = patternAngleRad;
            hatch.Layer = LayerTarama;
            hatch.Associative = true;
            var ids = new ObjectIdCollection { boundaryId };
            hatch.AppendLoop(HatchLoopTypes.Outermost, ids);
            hatch.EvaluateHatch(true);
        }

        private List<BeamInfo> MergeSameIdBeamsOnFloor(int floorNo)
        {
            var grouped = new Dictionary<string, List<(double S1, double S2, int StartAxis, int EndAxis, BeamInfo Beam)>>();
            var passthrough = new List<BeamInfo>();

            foreach (var beam in _model.Beams)
            {
                int beamFloor = GetBeamFloorNo(beam.BeamId);
                if (beamFloor != floorNo) continue;
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1) ||
                    !_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2))
                {
                    passthrough.Add(beam);
                    continue;
                }
                Vector2d v = p2 - p1;
                if (v.Length <= 1e-9) { passthrough.Add(beam); continue; }
                var u = v.GetNormal();
                double s1 = p1.X * u.X + p1.Y * u.Y;
                double s2 = p2.X * u.X + p2.Y * u.Y;
                int aStart = beam.StartAxisId;
                int aEnd = beam.EndAxisId;
                if (s1 > s2) { (s1, s2) = (s2, s1); (aStart, aEnd) = (aEnd, aStart); }
                string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}|{5}", beam.BeamId, beam.FixedAxisId, beam.WidthCm, beam.HeightCm, beam.OffsetRaw, beam.IsWallFlag);
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
