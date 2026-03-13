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
                    var temelHatiliRaws = new List<Geometry>();
                    DrawContinuousFoundations(tr, btr, offsetX, offsetY, firstFloor, drawTemelOutline: false, temelUnion, kolonPerdeUnion, temelHatiliRaws);
                    DrawSlabFoundations(tr, btr, offsetX, offsetY, drawTemelOutline: false);
                    DrawTieBeams(tr, btr, firstFloor, offsetX, offsetY, kolonPerdeUnion, temelHatiliRaws);
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
            EnsurePlanLayer(tr, db, "TEMEL HATILI (BEYKENT)", 230, LineWeight.LineWeight030, useDashed: false);
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
                    DrawGeometryRingsAsPolylines(tr, btr, toDraw, LayerPerde, addHatch: true, hatchAngleRad: GetAxisAngleRad(fixedAxisId), applySmallTriangleTrim: false);
            }
        }

        /// <summary>
        /// Verilen aks ID'sine ait eksenin eğimine göre yön açısını (radyan) döndürür. Perde tarama açısı için kullanılır.
        /// Y aksına bağlı ve eğimli (Slope != 0) akslardaki perdelerde tarama açısı aksa göre +90° döndürülür.
        /// </summary>
        private double GetAxisAngleRad(int axisId)
        {
            var axis = _model.AxisX.Concat(_model.AxisY).FirstOrDefault(a => a.Id == axisId);
            if (axis == null) return 0.0;
            // X ekseni: x - Slope*y = const → yön (Slope, 1). Y ekseni: Slope*x + y = const → yön (-1, Slope); Y için tarama eğimi ters.
            if (axis.Kind == AxisKind.X)
                return Math.Atan2(1.0, axis.Slope);
            double angleY = Math.Atan2(axis.Slope, -1.0) + Math.PI;
            // Sadece Y aksında ve açılı (Slope != 0) perdelerde taramayı 90° çevir.
            if (Math.Abs(axis.Slope) > 1e-9)
                angleY += Math.PI / 2.0;
            return angleY;
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

        /// <summary>İki geometri kenar veya alan paylaşıyorsa true; sadece tek noktada (minTouchLengthCm toleransına kadar) değiyorsa false. Temas kolon/perde sınırına çok yakınsa (kesim kenarı) birleştirme sayılmaz.</summary>
        private static bool ProperlyTouches(Geometry a, Geometry b, double minTouchLengthCm = 0.2, Geometry kolonPerdeBoundary = null)
        {
            if (a == null || b == null || a.IsEmpty || b.IsEmpty) return false;
            // Örtüşme veya içerme: birleştir
            if (a.Intersects(b) && !a.Touches(b)) return true;
            if (!a.Touches(b)) return false;
            // Sadece sınırlar temas ediyor; temas uzunluğu >= minTouchLengthCm ise kenar teması say
            try
            {
                var boundaryA = a.Boundary;
                var boundaryB = b.Boundary;
                if (boundaryA == null || boundaryB == null) return false;
                var inter = boundaryA.Intersection(boundaryB);
                if (inter == null || inter.IsEmpty) return false;
                if (inter.Length < minTouchLengthCm) return false;
                // Temas kolon/perde kesim kenarına çok yakınsa (0.3 cm) birleştirme; kesim sonrası sadece gerçek kiriş-kiriş temasında birleş
                if (kolonPerdeBoundary != null && !kolonPerdeBoundary.IsEmpty && inter.Distance(kolonPerdeBoundary) <= 0.3)
                    return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Kolon kenarı boyunca oluşan saç kılı: (1) Bir segment kolon sınırıyla tam üst üste, (2) Diğer segment ona paralel ve arası &lt; 1mm, (3) İki segmentin ortak vertexi yok. Bu üç koşul sağlanıyorsa true.</summary>
        private static bool IsColumnEdgeHairline(Polygon pg, Geometry kolonPerdeBoundary)
        {
            if (pg == null || pg.IsEmpty || kolonPerdeBoundary == null || kolonPerdeBoundary.IsEmpty) return false;
            var ring = pg.ExteriorRing;
            if (ring == null || ring.NumPoints < 4) return false;
            var coords = ring.Coordinates;
            int n = coords.Length - 1; // kapalı halka: son = ilk, segment sayısı n
            const double onColumnEpsilonCm = 0.15;  // segment kolon üzerinde kabul (daire kolon / precision sonrası için 1.5mm)
            const double parallelEpsilon = 1e-6;   // paralellik
            const double maxGapCm = 0.1;            // 1 mm

            for (int i = 0; i < n; i++)
            {
                var a = coords[i];
                var b = coords[i + 1];
                double sx = b.X - a.X, sy = b.Y - a.Y;
                double lenS = Math.Sqrt(sx * sx + sy * sy);
                if (lenS < 1e-6) continue;

                Geometry segGeom = pg.Factory.CreateLineString(new[] { a, b });
                if (segGeom.Distance(kolonPerdeBoundary) > onColumnEpsilonCm) continue; // kural 1: bu segment kolon üzerinde değil

                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    int prev = (i + n - 1) % n, next = (i + 1) % n;
                    if (j == prev || j == next) continue; // kural 3: ortak vertex yok
                    var c = coords[j];
                    var d = coords[j + 1];
                    double tx = d.X - c.X, ty = d.Y - c.Y;
                    double lenT = Math.Sqrt(tx * tx + ty * ty);
                    if (lenT < 1e-6) continue;

                    double cross = sx * ty - sy * tx;
                    if (Math.Abs(cross) > parallelEpsilon * (lenS * lenT + 1)) continue; // kural 2: paralel değil

                    double dist = Math.Abs((c.X - a.X) * sy - (c.Y - a.Y) * sx) / lenS;
                    if (dist >= maxGapCm) continue; // kural 2: arası 1mm'den az olmalı

                    return true; // üç kural da sağlandı → kolon kenarı saç kılı
                }
            }
            return false;
        }

        /// <summary>Alanı minAreaCm2'den küçük poligonları çıkarır; kiriş artığı (üçgen vb.) temizliği için. Koordinat birimi cm, alan cm².</summary>
        private static Geometry FilterSmallPolygons(Geometry geom, double minAreaCm2 = 1000.0)
        {
            if (geom == null || geom.IsEmpty) return null;
            var keep = new List<Geometry>();
            if (geom is Polygon p)
            {
                if (p.Area >= minAreaCm2) keep.Add(p);
            }
            else if (geom is MultiPolygon mp)
            {
                for (int i = 0; i < mp.NumGeometries; i++)
                {
                    var poly = (Polygon)mp.GetGeometryN(i);
                    if (poly.Area >= minAreaCm2) keep.Add(poly);
                }
            }
            else if (geom is GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                    if (gc.GetGeometryN(i) is Polygon p2 && p2.Area >= minAreaCm2)
                        keep.Add(p2);
            }
            if (keep.Count == 0) return null;
            if (keep.Count == 1) return keep[0];
            return keep[0].Factory.CreateMultiPolygon(keep.OfType<Polygon>().ToArray());
        }

        /// <summary>Geometry içindeki tüm poligonları (Polygon/MultiPolygon/GeometryCollection) listeye ekler; birleştirme öncesi parça toplamak için.</summary>
        private static void AddPolygonsToList(Geometry geom, List<Geometry> list)
        {
            if (geom == null || geom.IsEmpty) return;
            if (geom is Polygon p)
            {
                list.Add(p);
                return;
            }
            if (geom is MultiPolygon mp)
            {
                for (int i = 0; i < mp.NumGeometries; i++)
                    list.Add((Polygon)mp.GetGeometryN(i));
                return;
            }
            if (geom is GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                {
                    var g = gc.GetGeometryN(i);
                    if (g is Polygon p2)
                        list.Add(p2);
                }
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
                    // Daire kolonu: kiriş/perde kesiminde net daire çıkması için çokgen daire halkası (64 segment).
                    double radius = Math.Max(hw, hh);
                    coords = BuildCircleRing(center, radius, col.AngleDeg, 64);
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
                    double radius = Math.Max(hw, hh);
                    coords = BuildCircleRing(center, radius, col.AngleDeg, 64);
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
            var beamList = new List<Geometry>();
            Geometry kolonPerdeUnion = BuildKolonPerdeUnion(floor, offsetX, offsetY);
            Geometry kolonPerdeBoundary = (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty) ? kolonPerdeUnion.Boundary : null;
            const double beamEndExtensionCm = 22.0;   // Kolona tek noktada değen kiriş ucu 22 cm uzatılır
            const double touchEpsilonCm = 0.2;        // Uç kolon sınırında kabul

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
                if (kolonPerdeBoundary != null && !kolonPerdeBoundary.IsEmpty)
                {
                    var ptA = factory.CreatePoint(new Coordinate(a.X, a.Y));
                    var ptB = factory.CreatePoint(new Coordinate(b.X, b.Y));
                    var mid = factory.CreatePoint(new Coordinate((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5));
                    double distA = ptA.Distance(kolonPerdeBoundary);
                    double distB = ptB.Distance(kolonPerdeBoundary);
                    bool aOnCol = distA <= touchEpsilonCm;
                    bool bOnCol = distB <= touchEpsilonCm;
                    bool midInside = kolonPerdeUnion.Contains(mid);
                    // Referans nokta = kirişin kolona değdiği nokta (a veya b). Uzatma sadece bu noktadan 22 cm gidilen nokta kolon kesiti içinde kalıyorsa yapılır; dışına çıkıyorsa yapılmaz.
                    var extA = factory.CreatePoint(new Coordinate(a.X - beamEndExtensionCm * u.X, a.Y - beamEndExtensionCm * u.Y));
                    var extB = factory.CreatePoint(new Coordinate(b.X + beamEndExtensionCm * u.X, b.Y + beamEndExtensionCm * u.Y));
                    bool extendAtA = aOnCol && !midInside && kolonPerdeUnion.Contains(extA);
                    bool extendAtB = bOnCol && !midInside && kolonPerdeUnion.Contains(extB);
                    if (extendAtA) a = a - u.MultiplyBy(beamEndExtensionCm);
                    if (extendAtB) b = b + u.MultiplyBy(beamEndExtensionCm);
                }
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
                    var coordsBeam = new[]
                    {
                        new Coordinate(q1.X, q1.Y),
                        new Coordinate(q2.X, q2.Y),
                        new Coordinate(q3.X, q3.Y),
                        new Coordinate(q4.X, q4.Y),
                        new Coordinate(q1.X, q1.Y)
                    };
                    beamList.Add(factory.CreatePolygon(factory.CreateLinearRing(coordsBeam)));
                }
            }
            // Kesim: tüm kirişlerden kolon/perde çıkar; parçalara saç kılı eksiltmesi uygula (daralan boyutlar), sonra tek listede topla.
            var beamPieces = new List<Geometry>();
            foreach (var beamPoly in beamList)
            {
                if (beamPoly == null || beamPoly.IsEmpty) continue;
                Geometry toDraw = beamPoly;
                if (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty)
                {
                    var diff = beamPoly.Difference(kolonPerdeUnion);
                    if (diff != null && !diff.IsEmpty)
                    {
                        toDraw = ReducePrecisionSafe(diff, 100);
                        if (toDraw == null || toDraw.IsEmpty) toDraw = diff;
                    }
                }
                if (toDraw != null && !toDraw.IsEmpty)
                {
                    foreach (var poly in CleanGeometryToPolygons(toDraw, factory, applySmallTriangleTrim: false))
                    {
                        if (poly is Polygon pg && pg.Area >= 1000.0)
                        {
                            if (!IsColumnEdgeHairline(pg, kolonPerdeBoundary))
                                beamPieces.Add(pg);
                            else
                            {
                                // Saç kılı büyük kirişe bitişik: kolonu 1mm şişirip tekrar keserek kırp
                                var trimmed = pg.Difference(kolonPerdeUnion.Buffer(0.1));
                                if (trimmed != null && !trimmed.IsEmpty)
                                    foreach (var t in CleanGeometryToPolygons(trimmed, factory, applySmallTriangleTrim: false))
                                        if (t is Polygon tp && tp.Area > 100.0)
                                            beamPieces.Add(tp);
                            }
                        }
                    }
                }
            }
            // Kesim sonrası daralan (saç kılı eksiltmeli) parçalar üzerinden: birbirine değmeyen veya sadece 1 noktada (2mm) değenler hariç, temas edenleri birleştir.
            const double touchToleranceCm = 0.2; // 2 mm
            if (beamPieces.Count > 0)
            {
                int n = beamPieces.Count;
                var parent = new int[n];
                for (int i = 0; i < n; i++) parent[i] = i;
                int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
                void Union(int x, int y) { parent[Find(x)] = Find(y); }
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                        if (ProperlyTouches(beamPieces[i], beamPieces[j], touchToleranceCm, kolonPerdeBoundary))
                            Union(i, j);
                var componentGroups = new Dictionary<int, List<Geometry>>();
                for (int i = 0; i < n; i++)
                {
                    int root = Find(i);
                    if (!componentGroups.ContainsKey(root)) componentGroups[root] = new List<Geometry>();
                    componentGroups[root].Add(beamPieces[i]);
                }
                const double minBeamAreaCm2 = 1000.0; // Kiriş artığı (üçgen vb.) temizliği
                foreach (var list in componentGroups.Values)
                {
                    Geometry part = list.Count == 1 ? list[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(list);
                    if (part != null && !part.IsEmpty)
                    {
                        part = FilterSmallPolygons(part, minBeamAreaCm2);
                        if (part != null && !part.IsEmpty)
                            DrawGeometryRingsAsPolylines(tr, btr, part, LayerKiris, addHatch: false, exteriorRingsOnly: false, applySmallTriangleTrim: false);
                    }
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
                        DrawGeometryRingsAsPolylines(tr, btr, toDraw, LayerPerde, addHatch: true, hatchAngleRad: GetAxisAngleRad(fixedAxisId), applySmallTriangleTrim: false);
                }
            }

            // Kiriş ID yazıları: birleşme olsa bile, her BeamId için yalnızca tek yazı yaz.
            var writtenBeamIds = new HashSet<int>();
            foreach (var beam in _model.Beams)
            {
                int beamFloor = GetBeamFloorNo(beam.BeamId);
                if (beamFloor != floor.FloorNo) continue;
                if (beam.IsWallFlag == 1) continue; // Perdelerin yazıları yukarıda eklendi.
                if (!writtenBeamIds.Add(beam.BeamId)) continue; // Aynı ID için ikinci kez yazma.
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
                var center = new Point3d((q1.X + q2.X + q3.X + q4.X) / 4.0, (q1.Y + q2.Y + q3.Y + q4.Y) / 4.0, 0);

                AppendEntity(tr, btr, MakeCenteredText(LayerYazi, 6, beam.BeamId.ToString(CultureInfo.InvariantCulture), center));
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
            DrawGeometryRingsAsPolylines(tr, btr, unionResult, layer, applySmallTriangleTrim: true);
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

        /// <summary>Üç noktanın oluşturduğu üçgenin alanını cm² cinsinden döndürür (mutlak değer).</summary>
        private static double TriangleAreaCm2(Point2d a, Point2d b, Point2d c)
        {
            double signed = 0.5 * ((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y));
            return Math.Abs(signed);
        }

        /// <summary>X-A segmenti ile C-Y segmenti birbirine paralel mü (≈1° tolerans)? Paralel ise true. Kısa/dejenere segmentte güvenli tarafta kal: false dön (kırpma yapma).</summary>
        private static bool SegmentsParallel(Point2d x, Point2d a, Point2d c, Point2d y)
        {
            Vector2d v1 = a - x, v2 = y - c;
            double len1 = v1.Length, len2 = v2.Length;
            const double minSegmentLenCm = 0.4; // 4 mm: daha kısa segment anlamlı paralellik vermez, kırpma yapma
            if (len1 < minSegmentLenCm || len2 < minSegmentLenCm) return false;
            double dot = (v1.X * v2.X + v1.Y * v2.Y) / (len1 * len2);
            if (dot > 1.0) dot = 1.0; if (dot < -1.0) dot = -1.0;
            double angleRad = Math.Acos(dot);
            const double tolRad = 1.0 * Math.PI / 180.0;
            return angleRad <= tolRad || Math.Abs(Math.PI - angleRad) <= tolRad;
        }

        /// <summary>P noktasının L1-L2 doğrusuna (sonsuz doğru) dik uzaklığını (cm) döndürür.</summary>
        private static double PointToLineDistance(Point2d p, Point2d l1, Point2d l2)
        {
            Vector2d v = l2 - l1;
            double len = v.Length;
            if (len < 1e-9) return p.GetDistanceTo(l1);
            double cross = Math.Abs((l2.X - l1.X) * (p.Y - l1.Y) - (l2.Y - l1.Y) * (p.X - l1.X));
            return cross / len;
        }

        /// <summary>X-A doğrultusu ile C-Y ışınlandığında üst üste: C ve Y, X-A doğrusu üzerinde (tol cm içinde) mi?</summary>
        private static bool SegmentCYOnLineXA(Point2d x, Point2d a, Point2d c, Point2d y, double tolCm = 0.2)
        {
            return PointToLineDistance(c, x, a) <= tolCm && PointToLineDistance(y, x, a) <= tolCm;
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

        /// <summary>Saç kılı eksiltmelerini uygular; çizimdekiyle aynı kurallar (paralel-gap 2mm, vertex açı 1°, 4mm segment). applySmallTriangleTrim false ise küçük üçgen kırpma (1d) uygulanmaz (kirişler için).</summary>
        private static List<Point2d> ApplyRingCleanup(Coordinate[] coords, bool applySmallTriangleTrim = true)
        {
            const double minSegmentLen = 0.4;
            const double parallelGapTol = 0.2;
            if (coords == null || coords.Length < 3) return null;
            int n = coords.Length;
            if (n > 1 && coords[0].Equals2D(coords[n - 1])) n--;
            var pts = new List<Point2d>(n);
            for (int i = 0; i < n; i++) pts.Add(new Point2d(coords[i].X, coords[i].Y));
            if (pts.Count == 3)
            {
                var a = pts[0]; var b = pts[1]; var c = pts[2];
                if (PointToSegmentDistance(b, a, c) < parallelGapTol || PointToSegmentDistance(c, b, a) < parallelGapTol || PointToSegmentDistance(a, c, b) < parallelGapTol)
                    return null;
            }
            bool removed; int guard = 0;
            do
            {
                removed = false;
                if (pts.Count < 4) break;
                for (int i = 1; i < pts.Count - 2; i++)
                {
                    var a = pts[i - 1]; var b = pts[i]; var c = pts[i + 1]; var d = pts[i + 2];
                    Vector2d v1 = b - a, v2 = d - c;
                    double len1 = v1.Length, len2 = v2.Length;
                    if (len1 < 1e-6 || len2 < 1e-6) continue;
                    double dot = (v1.X * v2.X + v1.Y * v2.Y) / (len1 * len2);
                    if (dot > 1.0) dot = 1.0; if (dot < -1.0) dot = -1.0;
                    double angle = Math.Acos(dot);
                    double tolRad = 1.0 * Math.PI / 180.0;
                    if (angle > tolRad && Math.Abs(Math.PI - angle) > tolRad) continue;
                    var midBC = new Point2d((b.X + c.X) * 0.5, (b.Y + c.Y) * 0.5);
                    double num = Math.Abs((midBC.X - a.X) * (b.Y - a.Y) - (midBC.Y - a.Y) * (b.X - a.X));
                    if (num / len1 < parallelGapTol) { pts.RemoveAt(i + 1); pts.RemoveAt(i); removed = true; break; }
                }
                if (!removed && pts.Count >= 4)
                {
                    int m = pts.Count;
                    for (int i = 0; i < m; i++)
                    {
                        int ia = (i - 1 + m) % m, ib = i, ic = (i + 1) % m, id = (i + 2) % m;
                        var a = pts[ia]; var b = pts[ib]; var c = pts[ic]; var d = pts[id];
                        Vector2d v1 = b - a, v2 = d - c;
                        double len1 = v1.Length, len2 = v2.Length;
                        if (len1 < 1e-6 || len2 < 1e-6) continue;
                        double dot = (v1.X * v2.X + v1.Y * v2.Y) / (len1 * len2);
                        if (dot > 1.0) dot = 1.0; if (dot < -1.0) dot = -1.0;
                        double angle = Math.Acos(dot);
                        double tolRad = 1.0 * Math.PI / 180.0;
                        if (angle > tolRad && Math.Abs(Math.PI - angle) > tolRad) continue;
                        var midBC = new Point2d((b.X + c.X) * 0.5, (b.Y + c.Y) * 0.5);
                        double num = Math.Abs((midBC.X - a.X) * (b.Y - a.Y) - (midBC.Y - a.Y) * (b.X - a.X));
                        if (num / len1 < parallelGapTol) { int first = Math.Min(ib, ic), second = Math.Max(ib, ic); pts.RemoveAt(second); pts.RemoveAt(first); removed = true; break; }
                    }
                }
            } while (removed && ++guard < 10);
            const double vertexAngleTolRad = 1.0 * Math.PI / 180.0;
            int guardAngle = 0;
            while (guardAngle++ < 20 && pts.Count >= 4)
            {
                bool removedAngle = false;
                int m = pts.Count;
                for (int i = 0; i < m; i++)
                {
                    var a = pts[(i - 1 + m) % m]; var b = pts[i]; var c = pts[(i + 1) % m];
                    Vector2d v1 = b - a, v2 = c - b;
                    double len1 = v1.Length, len2 = v2.Length;
                    if (len1 < 1e-6 || len2 < 1e-6) continue;
                    double dot = (v1.X * v2.X + v1.Y * v2.Y) / (len1 * len2);
                    if (dot > 1.0) dot = 1.0; if (dot < -1.0) dot = -1.0;
                    double angle = Math.Acos(dot);
                    if (angle < vertexAngleTolRad || angle > Math.PI - vertexAngleTolRad) { pts.RemoveAt(i); removedAngle = true; break; }
                }
                if (!removedAngle) break;
            }
            // 1d) Düz hattaki ufak üçgen artığı: segment1 (X-A) ile segment4 (C-Y) aynı doğrultudaysa ve A-B-C alanı <500 cm² ise üçgeni oluşturan vertex1(A), vertex2(B), vertex3(C) üçünü sil; X-Y doğrudan birleşir.
            if (applySmallTriangleTrim)
            {
                const double minTriangleAreaCm2 = 1.0;
                const double maxTriangleAreaCm2 = 1000.0;
                int guardTri = 0;
                while (guardTri++ < 50 && pts.Count >= 6)
                {
                    bool removedTri = false;
                    int m = pts.Count;
                    for (int i = 0; i < m; i++)
                    {
                        int ia = (i - 1 + m) % m, ib = i, ic = (i + 1) % m;
                        var a = pts[ia]; var b = pts[ib]; var c = pts[ic];
                        var x = pts[(i - 2 + m) % m];
                        var y = pts[(i + 2) % m];
                        double area = TriangleAreaCm2(a, b, c);
                        if (area >= minTriangleAreaCm2 && area < maxTriangleAreaCm2 && SegmentsParallel(x, a, c, y) && SegmentCYOnLineXA(x, a, c, y))
                        {
                            // Üç vertex'i indeks sırasına göre büyükten küçüğe sil (kaydırma bozulmasın)
                            int r1 = Math.Max(Math.Max(ia, ib), ic);
                            int r3 = Math.Min(Math.Min(ia, ib), ic);
                            int r2 = ia + ib + ic - r1 - r3;
                            pts.RemoveAt(r1); pts.RemoveAt(r2); pts.RemoveAt(r3);
                            removedTri = true; break;
                        }
                    }
                    if (!removedTri) break;
                }
            }
            var filtered = new List<Point2d>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                if (filtered.Count == 0 || filtered[filtered.Count - 1].GetDistanceTo(p) >= minSegmentLen) filtered.Add(p);
            }
            if (filtered.Count < 3) return null;
            return filtered;
        }

        /// <summary>Geometriye saç kılı temizliği uygulayıp daralan (kesim sonrası) poligonları döndürür; birleştirme bu sonuç üzerinden yapılır. applySmallTriangleTrim false ise küçük üçgen kırpma (1d) uygulanmaz (kirişler için).</summary>
        private static List<Geometry> CleanGeometryToPolygons(Geometry geom, GeometryFactory factory, bool applySmallTriangleTrim = true)
        {
            var result = new List<Geometry>();
            if (geom == null || geom.IsEmpty) return result;
            var rings = new List<Coordinate[]>();
            if (geom is Polygon poly)
            {
                rings.Add(poly.ExteriorRing.Coordinates);
                for (int h = 0; h < poly.NumInteriorRings; h++) rings.Add(poly.InteriorRings[h].Coordinates);
            }
            else if (geom is MultiPolygon mp)
            {
                for (int i = 0; i < mp.NumGeometries; i++)
                {
                    var p = (Polygon)mp.GetGeometryN(i);
                    rings.Add(p.ExteriorRing.Coordinates);
                    for (int h = 0; h < p.NumInteriorRings; h++) rings.Add(p.InteriorRings[h].Coordinates);
                }
            }
            else if (geom is GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                    if (gc.GetGeometryN(i) is Polygon p2)
                    {
                        rings.Add(p2.ExteriorRing.Coordinates);
                        for (int h = 0; h < p2.NumInteriorRings; h++) rings.Add(p2.InteriorRings[h].Coordinates);
                    }
            }
            foreach (var coords in rings)
            {
                var cleaned = ApplyRingCleanup(coords, applySmallTriangleTrim);
                if (cleaned == null || cleaned.Count < 3) continue;
                var ringCoords = new Coordinate[cleaned.Count + 1];
                for (int i = 0; i < cleaned.Count; i++) ringCoords[i] = new Coordinate(cleaned[i].X, cleaned[i].Y);
                ringCoords[cleaned.Count] = ringCoords[0];
                result.Add(factory.CreatePolygon(factory.CreateLinearRing(ringCoords)));
            }
            return result;
        }

        /// <summary>NTS Geometry (Polygon/MultiPolygon) dış ve iç halkalarını verilen katmanda polyline olarak çizer; 4 mm'den kısa segmentleri atlar. addHatch true ise her halka için ANSI33 tarama eklenir. hatchAngleRad verilirse tarama açısı olarak kullanılır (perde: aks eğimi). exteriorRingsOnly true ise sadece dış halkalar çizilir (iç halkalar/delik sınırları çizilmez; kolona yapışık çizgi olmaz).</summary>
        private static void DrawGeometryRingsAsPolylines(Transaction tr, BlockTableRecord btr, Geometry geom, string layer, bool addHatch = false, double? hatchAngleRad = null, bool exteriorRingsOnly = false, bool applySmallTriangleTrim = false)
        {
            if (geom == null || geom.IsEmpty) return;
            const double minSegmentLen = 0.4; // 4 mm = 0.4 cm (çizim birimi cm)
            const double parallelGapTol = 0.2; // 2 mm: neredeyse paralel iki kenar arasındaki mesafe
            var ringsToDraw = new List<Coordinate[]>();
            if (geom is Polygon poly)
            {
                ringsToDraw.Add(poly.ExteriorRing.Coordinates);
                if (!exteriorRingsOnly)
                    for (int h = 0; h < poly.NumInteriorRings; h++)
                        ringsToDraw.Add(poly.InteriorRings[h].Coordinates);
            }
            else if (geom is MultiPolygon mp)
            {
                for (int i = 0; i < mp.NumGeometries; i++)
                {
                    var p = (Polygon)mp.GetGeometryN(i);
                    ringsToDraw.Add(p.ExteriorRing.Coordinates);
                    if (!exteriorRingsOnly)
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
                        if (!exteriorRingsOnly)
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

                // 1d) Düz hattaki ufak üçgen artığı: segment1 (X-A) ile segment4 (C-Y) aynı doğrultudaysa ve A-B-C alanı <500 cm² ise vertex1(A), vertex2(B), vertex3(C) üçünü sil; X-Y doğrudan birleşir.
                if (applySmallTriangleTrim)
                {
                    const double minTriangleAreaCm2 = 1.0;
                    const double maxTriangleAreaCm2 = 1000.0;
                    int guardTri = 0;
                    while (guardTri++ < 50 && pts.Count >= 6)
                    {
                        bool removedTri = false;
                        int m = pts.Count;
                        for (int i = 0; i < m; i++)
                        {
                            int ia = (i - 1 + m) % m, ib = i, ic = (i + 1) % m;
                            var a = pts[ia]; var b = pts[ib]; var c = pts[ic];
                            var x = pts[(i - 2 + m) % m];
                            var y = pts[(i + 2) % m];
                            double area = TriangleAreaCm2(a, b, c);
                            if (area >= minTriangleAreaCm2 && area < maxTriangleAreaCm2 && SegmentsParallel(x, a, c, y) && SegmentCYOnLineXA(x, a, c, y))
                            {
                                int r1 = Math.Max(Math.Max(ia, ib), ic);
                                int r3 = Math.Min(Math.Min(ia, ib), ic);
                                int r2 = ia + ib + ic - r1 - r3;
                                pts.RemoveAt(r1); pts.RemoveAt(r2); pts.RemoveAt(r3);
                                removedTri = true; break;
                            }
                        }
                        if (!removedTri) break;
                    }
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

                // Kiriş, perde ve temel hatılında yuvarlak kolon kesimini yay yap (LS daire uydurması + bulge)
                bool useCircleArcs = (layer == LayerKiris || layer == LayerPerde || layer == "TEMEL HATILI (BEYKENT)");
                var pl = useCircleArcs ? ToPolylineCircleArcsOnly(filtered, true) : ToPolyline(filtered.ToArray(), true);
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

        /// <param name="temelHatiliRaws">Dolu verilirse sürekli temel hatılı poligonları (diff öncesi) bu listeye eklenir ve hatıl burada çizilmez; DrawTieBeams'ta kiriş birleştirme mantığıyla birleştirilip çizilir.</param>
        private void DrawContinuousFoundations(Transaction tr, BlockTableRecord btr, double offsetX, double offsetY, FloorInfo floor, bool drawTemelOutline = true, Geometry temelUnion = null, Geometry kolonPerdeUnion = null, List<Geometry> temelHatiliRaws = null)
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

                    if (temelHatiliRaws != null)
                    {
                        temelHatiliRaws.Add(hatilPoly);
                    }
                    else
                    {
                        Geometry toDrawHatil = hatilPoly;
                        if (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty)
                        {
                            var diffH = hatilPoly.Difference(kolonPerdeUnion);
                            if (diffH == null || diffH.IsEmpty) continue;
                            toDrawHatil = diffH;
                        }
                        DrawGeometryRingsAsPolylines(tr, btr, toDrawHatil, "TEMEL HATILI (BEYKENT)", applySmallTriangleTrim: false);
                    }
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
                        DrawGeometryRingsAsPolylines(tr, btr, toDraw, layerAmpatman, applySmallTriangleTrim: false);
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

        private void DrawTieBeams(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY, Geometry kolonPerdeUnion = null, List<Geometry> temelHatiliRaws = null)
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

                if (layer == layerHatili)
                    hatiliPolygons.Add(tbPoly);
            }

            // Tüm TEMEL HATILI kaynaklarını topla (sürekli temel hatılları + bu katmandaki bağ kirişleri), kiriş birleştirme mantığıyla: diff → clean → temas edenleri birleştir → filtrele → çiz (aks gruplaması yok).
            var allHatilRaws = new List<Geometry>();
            if (temelHatiliRaws != null) allHatilRaws.AddRange(temelHatiliRaws);
            allHatilRaws.AddRange(hatiliPolygons);
            if (allHatilRaws.Count > 0)
            {
                var hatilPieces = new List<Geometry>();
                foreach (var geom in allHatilRaws)
                {
                    if (geom == null || geom.IsEmpty) continue;
                    Geometry toDraw = geom;
                    if (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty)
                    {
                        var diff = geom.Difference(kolonPerdeUnion);
                        if (diff != null && !diff.IsEmpty)
                        {
                            toDraw = ReducePrecisionSafe(diff, 100);
                            if (toDraw == null || toDraw.IsEmpty) toDraw = diff;
                        }
                    }
                    if (toDraw != null && !toDraw.IsEmpty)
                    {
                        foreach (var poly in CleanGeometryToPolygons(toDraw, factory, applySmallTriangleTrim: false))
                            if (poly is Polygon pg && pg.Area >= 1000.0)
                                hatilPieces.Add(poly);
                    }
                }
                const double touchToleranceCm = 0.2;
                Geometry kolonPerdeBoundary = (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty) ? kolonPerdeUnion.Boundary : null;
                if (hatilPieces.Count > 0)
                {
                    int n = hatilPieces.Count;
                    var parent = new int[n];
                    for (int i = 0; i < n; i++) parent[i] = i;
                    int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
                    void Union(int x, int y) { parent[Find(x)] = Find(y); }
                    for (int i = 0; i < n; i++)
                        for (int j = i + 1; j < n; j++)
                            if (ProperlyTouches(hatilPieces[i], hatilPieces[j], touchToleranceCm, kolonPerdeBoundary))
                                Union(i, j);
                    var componentGroups = new Dictionary<int, List<Geometry>>();
                    for (int i = 0; i < n; i++)
                    {
                        int root = Find(i);
                        if (!componentGroups.ContainsKey(root)) componentGroups[root] = new List<Geometry>();
                        componentGroups[root].Add(hatilPieces[i]);
                    }
                    const double minHatilAreaCm2 = 1000.0;
                    foreach (var list in componentGroups.Values)
                    {
                        Geometry part = list.Count == 1 ? list[0] : CascadedPolygonUnion.Union(list);
                        if (part != null && !part.IsEmpty)
                        {
                            part = FilterSmallPolygons(part, minHatilAreaCm2);
                            if (part != null && !part.IsEmpty)
                                DrawGeometryRingsAsPolylines(tr, btr, part, layerHatili, addHatch: false, exteriorRingsOnly: false, applySmallTriangleTrim: false);
                        }
                    }
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

        /// <summary>Daire kolon için NTS kesim geometrisi: merkez ve yarıçap ile çokgen daire halkası (net kesim için yeterli segment).</summary>
        private static Coordinate[] BuildCircleRing(Point2d center, double radiusCm, double angleDeg, int numSegments = 64)
        {
            if (numSegments < 8) numSegments = 8;
            var coords = new Coordinate[numSegments + 1];
            for (int i = 0; i < numSegments; i++)
            {
                double angleRad = (i * 2.0 * Math.PI / numSegments) + (angleDeg * Math.PI / 180.0);
                double x = center.X + radiusCm * Math.Cos(angleRad);
                double y = center.Y + radiusCm * Math.Sin(angleRad);
                coords[i] = new Coordinate(x, y);
            }
            coords[numSegments] = coords[0];
            return coords;
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

        /// <summary>Üç noktadan geçen dairenin merkez ve yarıçapı (collinear ise false).</summary>
        private static bool TryCircumcircle(Point2d a, Point2d b, Point2d c, out Point2d center, out double radius)
        {
            center = default;
            radius = 0;
            double d = 2.0 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
            if (Math.Abs(d) < 1e-12) return false;
            double cx = ((a.X * a.X + a.Y * a.Y) * (b.Y - c.Y) + (b.X * b.X + b.Y * b.Y) * (c.Y - a.Y) + (c.X * c.X + c.Y * c.Y) * (a.Y - b.Y)) / d;
            double cy = ((a.X * a.X + a.Y * a.Y) * (c.X - b.X) + (b.X * b.X + b.Y * b.Y) * (a.X - c.X) + (c.X * c.X + c.Y * c.Y) * (b.X - a.X)) / d;
            center = new Point2d(cx, cy);
            radius = Math.Sqrt((a.X - cx) * (a.X - cx) + (a.Y - cy) * (a.Y - cy));
            return radius >= 1e-9;
        }

        private static double PointToCircleDistance(Point2d p, Point2d center, double radius)
        {
            return Math.Abs(Math.Sqrt((p.X - center.X) * (p.X - center.X) + (p.Y - center.Y) * (p.Y - center.Y)) - radius);
        }

        /// <summary>Kása cebirsel daire uydurması: z = x²+y² = 2cx·x + 2cy·y + (R²−cx²−cy²). LS çözümü merkezi kolon dairesi ile uyumlu yapar (iki kiriş birleşince de).</summary>
        private static bool TryFitCircleLS(IReadOnlyList<Point2d> pts, out Point2d center, out double radius)
        {
            center = default;
            radius = 0;
            if (pts == null || pts.Count < 3) return false;
            int n = pts.Count;
            double sx = 0, sy = 0, sz = 0, sxx = 0, syy = 0, sxy = 0, sxz = 0, syz = 0;
            for (int k = 0; k < n; k++)
            {
                double x = pts[k].X, y = pts[k].Y, z = x * x + y * y;
                sx += x; sy += y; sz += z;
                sxx += x * x; syy += y * y; sxy += x * y;
                sxz += x * z; syz += y * z;
            }
            double det = sxx * (syy * n - sy * sy) - sxy * (sxy * n - sy * sx) + sx * (sxy * sy - syy * sx);
            if (Math.Abs(det) < 1e-15) return false;
            double u = (sxz * (syy * n - sy * sy) - sxy * (syz * n - sy * sz) + sx * (syz * sy - syy * sz)) / det;
            double v = (sxx * (syz * n - sy * sz) - sxz * (sxy * n - sy * sx) + sx * (sxy * sz - syz * sx)) / det;
            double w = (sxx * (syy * sz - syz * sy) - sxy * (sxy * sz - syz * sx) + sxz * (sxy * sy - syy * sx)) / det;
            double cx = u / 2.0, cy = v / 2.0;
            double R2 = w + cx * cx + cy * cy;
            if (R2 < 1e-12) return false;
            radius = Math.Sqrt(R2);
            center = new Point2d(cx, cy);
            return radius >= 1e-9;
        }

        /// <summary>Sadece daire kolon kesimini yay yapar. Merkez ve yarıçap LS daire uydurması ile (tüm run noktaları); iki kiriş birleşince de kolon dairesiyle uyumlu.</summary>
        private static Polyline ToPolylineCircleArcsOnly(IReadOnlyList<Point2d> points, bool closed)
        {
            if (points == null || points.Count < 2) return ToPolyline(points ?? new List<Point2d>(), closed);
            int n = points.Count;
            const double arcTol = 0.15;   // daire uydurma toleransı (cm)
            const int minCircleRun = 8;
            const int maxRun = 64;

            var vertices = new List<(Point2d pt, double bulge)>();

            for (int i = 0; i < n; )
            {
                int bestLen = 0;
                double bestBulge = 0;
                int maxLen = Math.Min(maxRun, n - 1 - i);
                if (maxLen < minCircleRun) { vertices.Add((points[i], 0)); i++; continue; }

                for (int len = minCircleRun; len <= maxLen; len++)
                {
                    int j = i + len;
                    var runPts = new List<Point2d>();
                    for (int k = 0; k <= len; k++) runPts.Add(points[i + k]);
                    if (!TryFitCircleLS(runPts, out Point2d center, out double radius))
                        continue;
                    bool ok = true;
                    for (int k = 0; k <= len && ok; k++)
                        if (PointToCircleDistance(points[i + k], center, radius) > arcTol) ok = false;
                    if (!ok) continue;

                    double a0 = Math.Atan2(points[i].Y - center.Y, points[i].X - center.X);
                    double a1 = Math.Atan2(points[j].Y - center.Y, points[j].X - center.X);
                    int mid = i + (len / 2);
                    double aMid = Math.Atan2(points[mid].Y - center.Y, points[mid].X - center.X);
                    double sweep = a1 - a0;
                    if (sweep > Math.PI) sweep -= 2.0 * Math.PI;
                    if (sweep < -Math.PI) sweep += 2.0 * Math.PI;
                    double sweepAlt = (sweep >= 0) ? sweep - 2.0 * Math.PI : sweep + 2.0 * Math.PI;
                    bool useAlt = false;
                    if (Math.Abs(sweep) > 1e-6)
                    {
                        double aMidNorm = aMid - a0;
                        while (aMidNorm > Math.PI) aMidNorm -= 2.0 * Math.PI;
                        while (aMidNorm < -Math.PI) aMidNorm += 2.0 * Math.PI;
                        if (sweep > 0 && aMidNorm < 0) useAlt = true;
                        if (sweep < 0 && aMidNorm > 0) useAlt = true;
                    }
                    if (useAlt) sweep = sweepAlt;
                    if (Math.Abs(sweep) > Math.PI - 0.01) continue;
                    double bulge = Math.Tan(sweep / 4.0);
                    if (double.IsNaN(bulge) || double.IsInfinity(bulge) || Math.Abs(bulge) > 5.0) continue;
                    if (len > bestLen) { bestLen = len; bestBulge = bulge; }
                }

                if (bestLen >= minCircleRun)
                {
                    vertices.Add((points[i], bestBulge));
                    i += bestLen;
                }
                else
                {
                    vertices.Add((points[i], 0));
                    i++;
                }
            }

            if (vertices.Count == 0) return ToPolyline(points, closed);
            var pl = new Polyline();
            for (int v = 0; v < vertices.Count; v++)
                pl.AddVertexAt(v, vertices[v].pt, vertices[v].bulge, 0, 0);
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

        /// <summary>
        /// Kolon ve perde taraması: ANSI33, katman TARAMA (BEYKENT).
        /// patternAngleRad parametresi şu an kullanılmıyor; tüm taramalar sabit açı ile çizilir.
        /// </summary>
        private static void AppendHatchAnsi33(Transaction tr, BlockTableRecord btr, ObjectId boundaryId, double patternAngleRad = 0)
        {
            var hatch = new Hatch();
            btr.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "ANSI33");
            hatch.PatternAngle = 0; // Tüm taramalarda sabit açı
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
                // Aynı aksta, aynı boyutta (kesit/kaçıklık) ve aralarında 1 m'den büyük boşluk olmayan kirişleri tek kiriş gibi birleştir (125+128 gibi). BeamId anahtar dışında; ID yazısı zaten BeamId başına tek yazılıyor.
                string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}", beam.FixedAxisId, beam.WidthCm, beam.HeightCm, beam.OffsetRaw, beam.IsWallFlag);
                if (!grouped.TryGetValue(key, out var list))
                {
                    list = new List<(double, double, int, int, BeamInfo)>();
                    grouped[key] = list;
                }
                list.Add((s1, s2, aStart, aEnd, beam));
            }

            var merged = new List<BeamInfo>();
            const double maxGapCm = 100.0; // 1 m = 100 cm: bu aralıktan büyük boşluk varsa ayır

            foreach (var kv in grouped)
            {
                var segs = kv.Value.OrderBy(x => x.S1).ToList();
                if (segs.Count == 0) continue;
                var b0 = segs[0].Beam;

                int idx = 0;
                while (idx < segs.Count)
                {
                    var currentStart = segs[idx];
                    var currentEnd = segs[idx];
                    double currentEndPos = currentEnd.S2;
                    idx++;

                    // Aynı aks üzerindeki ve aralarında 1 m'den fazla boşluk olmayan kirişleri küme halinde birleştir.
                    while (idx < segs.Count)
                    {
                        var next = segs[idx];
                        double gap = next.S1 - currentEndPos;
                        if (gap > maxGapCm)
                            break;
                        currentEnd = next;
                        currentEndPos = next.S2;
                        idx++;
                    }

                    merged.Add(new BeamInfo
                    {
                        BeamId = b0.BeamId,
                        FixedAxisId = b0.FixedAxisId,
                        StartAxisId = currentStart.StartAxis,
                        EndAxisId = currentEnd.EndAxis,
                        WidthCm = b0.WidthCm,
                        HeightCm = b0.HeightCm,
                        OffsetRaw = b0.OffsetRaw,
                        IsWallFlag = b0.IsWallFlag
                    });
                }
            }

            merged.AddRange(passthrough);
            return merged;
        }
    }
}
