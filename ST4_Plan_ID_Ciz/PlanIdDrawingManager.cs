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
                    Geometry firstFloorUnion = BuildFloorElementUnion(firstFloor);
                    var firstFloorAxisExt = GetKatSiniriEnvelope(firstFloorUnion);
                    DrawAxes(tr, btr, offsetX, offsetY, firstFloorAxisExt);
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
                    DrawPerdeLabelsForFloor(tr, btr, firstFloor, offsetX, offsetY, kolonPerdeUnion);
                    DrawFloorTitle(tr, btr, firstFloor, offsetX, offsetY, firstFloorAxisExt, isFoundationPlan: true);
                }

                for (int floorIdx = 0; floorIdx < _model.Floors.Count; floorIdx++)
                {
                    var floor = _model.Floors[floorIdx];
                    double offsetX = (floorIdx + planStartIndex) * (floorWidth + floorGap);
                    double offsetY = 0.0;

                    Geometry elemUnion = BuildFloorElementUnion(floor);
                    var floorAxisExt = GetKatSiniriEnvelope(elemUnion);
                    DrawAxes(tr, btr, offsetX, offsetY, floorAxisExt);
                    DrawColumns(tr, btr, floor, offsetX, offsetY);
                    DrawBeamsAndWalls(tr, btr, floor, offsetX, offsetY);
                    DrawSlabs(tr, btr, floor, offsetX, offsetY);
                    DrawFloorBoundary(tr, btr, elemUnion, offsetX, offsetY);
                    DrawSlabVoids(tr, btr, elemUnion, offsetX, offsetY);
                    DrawFloorTitle(tr, btr, floor, offsetX, offsetY, floorAxisExt, isFoundationPlan: false);
                }

                tr.Commit();

                ed.WriteMessage(
                    "\nST4PLANID: {0} kat, akslar, kolonlar (poligon dahil), kirişler, perdeler ve döşemeler{1} ID'leriyle cizildi. (cm)",
                    _model.Floors.Count,
                    hasFoundations ? string.Format(", temel plani (surekli: {0}, radye: {1}, bag kirisi: {2}, tekil: {3})", _model.ContinuousFoundations.Count, _model.SlabFoundations.Count, _model.TieBeams.Count, _model.SingleFootings.Count) : "");
            }
        }

        private const string LayerAks = "AKS CIZGISI (BEYKENT)";
        private const string LayerAksBalonu = "AKS BALONU (BEYKENT)";
        private const string LayerAksYazisi = "AKS YAZISI (BEYKENT)";
        private const string LayerKiris = "KIRIS (BEYKENT)";
        private const string LayerKolon = "KOLON (BEYKENT)";
        private const string LayerPerde = "PERDE (BEYKENT)";
        private const string LayerTarama = "TARAMA (BEYKENT)";
        private const string LayerDoseme = "DOSEME SINIRI (BEYKENT)";
        private const string LayerMerdiven = "MERDIVEN (BEYKENT)";
        private const string LayerYazi = "YAZI (BEYKENT)";
        private const string LayerBaslik = "YAZI (BEYKENT)";
        private const string LayerKatSiniri = "KAT SINIRI (BEYKENT)";
        private const string LayerKalipBosluk = "KALIP BOSLUK (BEYKENT)";
        private const string LayerOlcu = "OLCU (BEYKENT)";
        private const string LayerAksOlcu = "AKS OLCU (BEYKENT)";
        private const string LayerKirisYazisi = "KIRIS ISMI (BEYKENT)";
        private const string LayerPerdeYazisi = "PERDE ISMI (BEYKENT)";
        /// <summary>Kiriş etiket çizim boyutları (resimdeki gibi): 70cm x 14cm referans (13 karakter). Genişlik = RefWidth * metin uzunluğu / RefCharCount.</summary>
        private const double BeamLabelRefWidthCm = 70.0;
        private const double BeamLabelRefHeightCm = 12.0;
        private const int BeamLabelRefCharCount = 13;
        private const string AksOlcuDimStyleName = "AKS_OLCU";
        private const string AksOlcuTextStyleName = "AKS_OLCU";
        private const string ElemanEtiketTextStyleName = "ETIKET";

        private static void EnsureLayers(Transaction tr, Database db)
        {
            EnsureDashedLinetype(tr, db);
            EnsurePlanLayer(tr, db, LayerAks, 252, LineWeight.LineWeight020, useDashed: true);
            EnsurePlanLayer(tr, db, LayerAksBalonu, 7, LineWeight.LineWeight030, useDashed: false);
            EnsurePlanLayer(tr, db, LayerAksYazisi, 3, LineWeight.LineWeight020, useDashed: false);
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
            EnsurePlanLayer(tr, db, LayerKatSiniri, 41, LineWeight.LineWeight025, useDashed: false);
            EnsurePlanLayer(tr, db, LayerKalipBosluk, 30, LineWeight.LineWeight025, useDashed: true);
            EnsurePlanLayer(tr, db, LayerOlcu, 4, LineWeight.LineWeight018, useDashed: false);
            EnsurePlanLayer(tr, db, LayerAksOlcu, 6, LineWeight.LineWeight018, useDashed: false);
            EnsurePlanLayer(tr, db, LayerKirisYazisi, 40, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, LayerPerdeYazisi, 240, LineWeight.LineWeight020, useDashed: false);
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
            else
                rec.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorIndex);
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

        /// <summary>Kalıp planına çizilen kat sınırı (eleman birleşimi) zarfını model koordinatlarında döndürür. Aks sınırları bu zarfın 220 cm dışına çıkacak şekilde DrawAxes içinde kullanılır. Boşsa CalculateBaseExtents fallback.</summary>
        private (double Xmin, double Xmax, double Ymin, double Ymax) GetKatSiniriEnvelope(Geometry elementUnion)
        {
            if (elementUnion == null || elementUnion.IsEmpty) return CalculateBaseExtents();
            var env = elementUnion.EnvelopeInternal;
            return (env.MinX, env.MaxX, env.MinY, env.MaxY);
        }

        /// <summary>Koordinatlar datadaki şekliyle kullanılır; aks yuvarlaması yok.</summary>
        private static Coordinate C1(double x, double y) => new Coordinate(x, y);

        /// <summary>Katta çizilen elemanların (kolon, kiriş, perde, döşeme) birleşimi; model koordinatları (offset 0). Resimdeki gibi yapıyı takip eden dış sınır için kullanılır. Koordinatlar 1 cm yuvarlanarak non-noded intersection önlenir.</summary>
        private Geometry BuildFloorElementUnion(FloorInfo floor)
        {
            var factory = new GeometryFactory();
            var geoms = new List<Geometry>();
            int floorNo = floor.FloorNo;

            foreach (var col in _model.Columns)
            {
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                int sectionId = ResolveColumnSectionId(floorNo, col.ColumnNo);
                int polygonSectionId = ResolvePolygonPositionSectionId(floorNo, col.ColumnNo);
                if (col.ColumnType == 3 && (polygonSectionId <= 0 || !_model.PolygonColumnSectionByPositionSectionId.ContainsKey(polygonSectionId))) continue;
                if (col.ColumnType != 3 && (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId))) continue;
                var dim = sectionId > 0 && _model.ColumnDimsBySectionId.ContainsKey(sectionId) ? _model.ColumnDimsBySectionId[sectionId] : (W: 40.0, H: 40.0);
                double hw = dim.W / 2.0, hh = dim.H / 2.0;
                var offsetLocal = col.ColumnType == 2 ? ComputeColumnOffsetCircle(col.OffsetXRaw, col.OffsetYRaw) : ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                var center = new Point2d(axisNode.X + offsetGlobal.X, axisNode.Y + offsetGlobal.Y);
                Coordinate[] coords;
                if (col.ColumnType == 2)
                {
                    var raw = BuildCircleRing(center, Math.Max(hw, hh), col.AngleDeg, 64);
                    coords = new Coordinate[raw.Length];
                    for (int i = 0; i < raw.Length; i++) coords[i] = C1(raw[i].X, raw[i].Y);
                }
                else if (col.ColumnType == 3 && TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out var polyPoints))
                {
                    coords = new Coordinate[polyPoints.Length + 1];
                    for (int i = 0; i < polyPoints.Length; i++) coords[i] = C1(polyPoints[i].X, polyPoints[i].Y);
                    coords[polyPoints.Length] = coords[0];
                }
                else
                {
                    var rect = BuildRect(center, hw, hh, col.AngleDeg);
                    coords = new Coordinate[5];
                    for (int i = 0; i < 4; i++) coords[i] = C1(rect[i].X, rect[i].Y);
                    coords[4] = coords[0];
                }
                geoms.Add(factory.CreatePolygon(factory.CreateLinearRing(coords)));
            }

            var beams = MergeSameIdBeamsOnFloor(floorNo);
            foreach (var beam in beams)
            {
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1) ||
                    !_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2)) continue;
                var a = new Point2d(p1.X, p1.Y);
                var b = new Point2d(p2.X, p2.Y);
                NormalizeBeamDirection(beam.FixedAxisId, ref a, ref b);
                Vector2d dir = b - a;
                if (dir.Length <= 1e-9) continue;
                Vector2d perp = new Vector2d(-dir.Y, dir.X).GetNormal();
                double hw = beam.WidthCm / 2.0;
                ComputeBeamEdgeOffsets(beam.OffsetRaw, hw, out double upperEdge, out double lowerEdge);
                var coords = new[]
                {
                    C1(a.X + perp.X * upperEdge, a.Y + perp.Y * upperEdge),
                    C1(b.X + perp.X * upperEdge, b.Y + perp.Y * upperEdge),
                    C1(b.X + perp.X * lowerEdge, b.Y + perp.Y * lowerEdge),
                    C1(a.X + perp.X * lowerEdge, a.Y + perp.Y * lowerEdge),
                    C1(a.X + perp.X * upperEdge, a.Y + perp.Y * upperEdge)
                };
                geoms.Add(factory.CreatePolygon(factory.CreateLinearRing(coords)));
            }

            foreach (var slab in _model.Slabs)
            {
                if (GetSlabFloorNo(slab.SlabId) != floorNo) continue;
                int a1 = slab.Axis1, a2 = slab.Axis2, a3 = slab.Axis3, a4 = slab.Axis4;
                if (a1 == 0 || a2 == 0 || a3 == 0 || a4 == 0) continue;
                if (!_axisService.TryIntersect(a1, a3, out Point2d p11) || !_axisService.TryIntersect(a1, a4, out Point2d p12) ||
                    !_axisService.TryIntersect(a2, a3, out Point2d p21) || !_axisService.TryIntersect(a2, a4, out Point2d p22)) continue;
                var coords = new[]
                {
                    C1(p11.X, p11.Y), C1(p12.X, p12.Y),
                    C1(p22.X, p22.Y), C1(p21.X, p21.Y),
                    C1(p11.X, p11.Y)
                };
                geoms.Add(factory.CreatePolygon(factory.CreateLinearRing(coords)));
            }

            if (geoms.Count == 0) return null;
            try
            {
                return geoms.Count == 1 ? geoms[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(geoms);
            }
            catch (Exception)
            {
                var reduced = new List<Geometry>();
                foreach (var g in geoms)
                {
                    var r = ReducePrecisionSafe(g, 1);
                    if (r != null && !r.IsEmpty) reduced.Add(r);
                }
                if (reduced.Count == 0) return null;
                return reduced.Count == 1 ? reduced[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(reduced);
            }
        }

        /// <summary>Kalıp planında: kat sınırı içinde kalan ve hiçbir eleman (kolon, kiriş, perde, döşeme) tanımlanmayan boşlukları çizer (element union iç halkaları / delikler).</summary>
        private void DrawSlabVoids(Transaction tr, BlockTableRecord btr, Geometry elementUnion, double offsetX, double offsetY)
        {
            if (elementUnion == null || elementUnion.IsEmpty) return;
            var interiorRings = new List<Coordinate[]>();
            if (elementUnion is Polygon poly)
            {
                for (int h = 0; h < poly.NumInteriorRings; h++)
                {
                    var ring = poly.GetInteriorRingN(h);
                    if (ring != null && ring.NumPoints >= 3)
                        interiorRings.Add(ring.Coordinates);
                }
            }
            else if (elementUnion is MultiPolygon mp)
            {
                for (int i = 0; i < mp.NumGeometries; i++)
                {
                    var p = (Polygon)mp.GetGeometryN(i);
                    if (p == null) continue;
                    for (int h = 0; h < p.NumInteriorRings; h++)
                    {
                        var ring = p.GetInteriorRingN(h);
                        if (ring != null && ring.NumPoints >= 3)
                            interiorRings.Add(ring.Coordinates);
                    }
                }
            }
            foreach (var coords in interiorRings)
            {
                var cleaned = ApplyRingCleanup(coords, applySmallTriangleTrim: false);
                if (cleaned != null && cleaned.Count >= 3)
                {
                    var pts = new List<Point2d>(cleaned.Count);
                    for (int i = 0; i < cleaned.Count; i++)
                        pts.Add(new Point2d(cleaned[i].X + offsetX, cleaned[i].Y + offsetY));
                    pts = RemoveDuplicateVertices(pts);
                    pts = RemoveCollinearVertices(pts, 0.1);
                    pts = RemoveShortSegmentVertices(pts, 1.0);
                    if (pts.Count >= 3)
                    {
                        var pl = ToPolyline(pts, true);
                        pl.Layer = LayerKalipBosluk;
                        AppendEntity(tr, btr, pl);
                    }
                }
            }
        }

        /// <summary>Kat sınırı poligonu çizer: eleman birleşiminin tüm dış halkaları (birden fazla kapalı alan varsa hepsi). Union yoksa bbox dikdörtgeni.</summary>
        private void DrawFloorBoundary(Transaction tr, BlockTableRecord btr, Geometry elementUnion, double offsetX, double offsetY)
        {
            var exteriorRings = new List<Coordinate[]>();
            if (elementUnion != null && !elementUnion.IsEmpty)
            {
                if (elementUnion is Polygon poly && poly.ExteriorRing != null && poly.ExteriorRing.NumPoints >= 3)
                    exteriorRings.Add(poly.ExteriorRing.Coordinates);
                else if (elementUnion is MultiPolygon mp)
                {
                    for (int i = 0; i < mp.NumGeometries; i++)
                    {
                        var p = (Polygon)mp.GetGeometryN(i);
                        if (p != null && p.ExteriorRing != null && p.ExteriorRing.NumPoints >= 3)
                            exteriorRings.Add(p.ExteriorRing.Coordinates);
                    }
                }
            }
            if (exteriorRings.Count > 0)
            {
                foreach (var coords in exteriorRings)
                {
                    var cleaned = ApplyRingCleanup(coords, applySmallTriangleTrim: false);
                    if (cleaned != null && cleaned.Count >= 3)
                    {
                        var pts = new List<Point2d>(cleaned.Count);
                        for (int i = 0; i < cleaned.Count; i++)
                            pts.Add(new Point2d(cleaned[i].X + offsetX, cleaned[i].Y + offsetY));
                        pts = RemoveDuplicateVertices(pts);
                        pts = RemoveCollinearVertices(pts, 0.1);
                        pts = RemoveShortSegmentVertices(pts, 1.0);
                        if (pts.Count >= 3)
                        {
                            var pl = ToPolyline(pts, true);
                            pl.Layer = LayerKatSiniri;
                            AppendEntity(tr, btr, pl);
                        }
                    }
                }
                return;
            }
            var ext = CalculateBaseExtents();
            var fallback = new Point2d[]
            {
                new Point2d(ext.Xmin + offsetX, ext.Ymin + offsetY),
                new Point2d(ext.Xmax + offsetX, ext.Ymin + offsetY),
                new Point2d(ext.Xmax + offsetX, ext.Ymax + offsetY),
                new Point2d(ext.Xmin + offsetX, ext.Ymax + offsetY)
            };
            var pl2 = ToPolyline(fallback, true);
            pl2.Layer = LayerKatSiniri;
            AppendEntity(tr, btr, pl2);
        }

        /// <summary>Kapalı poligon vertex listesinden üst üste binen (aynı konumdaki) vertexleri siler; aynı yerde birden fazla varsa sadece biri kalır.</summary>
        private static List<Point2d> RemoveDuplicateVertices(List<Point2d> pts)
        {
            if (pts == null || pts.Count < 4) return pts;
            const double eps = 1e-9;
            var outList = new List<Point2d>(pts.Count);
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                var curr = pts[i];
                var prev = pts[(i + n - 1) % n];
                double dx = curr.X - prev.X, dy = curr.Y - prev.Y;
                if (dx * dx + dy * dy > eps * eps)
                    outList.Add(curr);
            }
            // Kapalı halkada son vertex ilk ile aynıysa sonuncuyu sil (üst üste binenlerden sadece biri kalsın)
            if (outList.Count >= 3)
            {
                var first = outList[0];
                var last = outList[outList.Count - 1];
                double d = (last.X - first.X) * (last.X - first.X) + (last.Y - first.Y) * (last.Y - first.Y);
                if (d <= eps * eps)
                    outList.RemoveAt(outList.Count - 1);
            }
            return outList.Count >= 3 ? outList : pts;
        }

        /// <summary>Kapalı poligon vertex listesinden açı değişimi &lt; minAngleDeg derece olan vertexleri siler; en az 3 nokta kalır.</summary>
        private static List<Point2d> RemoveCollinearVertices(List<Point2d> pts, double minAngleDeg = 0.1)
        {
            if (pts == null || pts.Count < 4) return pts;
            double minSin = Math.Sin(minAngleDeg * Math.PI / 180.0);
            var outList = new List<Point2d>(pts.Count);
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                var prev = pts[(i + n - 1) % n];
                var curr = pts[i];
                var next = pts[(i + 1) % n];
                double vx = curr.X - prev.X, vy = curr.Y - prev.Y;
                double wx = next.X - curr.X, wy = next.Y - curr.Y;
                double cross = vx * wy - vy * wx;
                double lenPrev = Math.Sqrt(vx * vx + vy * vy);
                double lenNext = Math.Sqrt(wx * wx + wy * wy);
                if (lenPrev < 1e-12 || lenNext < 1e-12)
                    outList.Add(curr);
                else if (Math.Abs(cross) >= lenPrev * lenNext * minSin)
                    outList.Add(curr);
            }
            return outList.Count >= 3 ? outList : pts;
        }

        /// <summary>Kapalı poligon vertex listesinden 1mm'den kısa segment oluşturan vertexleri siler (minLenMm: mm cinsinden min segment uzunluğu).</summary>
        private static List<Point2d> RemoveShortSegmentVertices(List<Point2d> pts, double minLenMm)
        {
            if (pts == null || pts.Count < 4 || minLenMm <= 0) return pts;
            const double mmToDrawing = 1.0;
            double minLen = minLenMm * mmToDrawing;
            var list = new List<Point2d>(pts);
            bool changed = true;
            while (changed && list.Count >= 4)
            {
                changed = false;
                int n = list.Count;
                var nextList = new List<Point2d>(n);
                for (int i = 0; i < n; i++)
                {
                    var prev = list[(i + n - 1) % n];
                    var curr = list[i];
                    var next = list[(i + 1) % n];
                    double dPrev = Math.Sqrt((curr.X - prev.X) * (curr.X - prev.X) + (curr.Y - prev.Y) * (curr.Y - prev.Y));
                    double dNext = Math.Sqrt((next.X - curr.X) * (next.X - curr.X) + (next.Y - curr.Y) * (next.Y - curr.Y));
                    if (dPrev < minLen || dNext < minLen)
                        changed = true;
                    else
                        nextList.Add(curr);
                }
                if (nextList.Count >= 3)
                    list = nextList;
                else
                    break;
            }
            return list.Count >= 3 ? list : pts;
        }

        /// <summary>Kat sınırı: bbox (dikdörtgen) — temel planı gibi ext ile çizilen yerler için.</summary>
        private void DrawFloorBoundaryFromExt(Transaction tr, BlockTableRecord btr, (double Xmin, double Xmax, double Ymin, double Ymax) ext, double offsetX, double offsetY)
        {
            var pts = new Point2d[]
            {
                new Point2d(ext.Xmin + offsetX, ext.Ymin + offsetY),
                new Point2d(ext.Xmax + offsetX, ext.Ymin + offsetY),
                new Point2d(ext.Xmax + offsetX, ext.Ymax + offsetY),
                new Point2d(ext.Xmin + offsetX, ext.Ymax + offsetY)
            };
            var pl = ToPolyline(pts, true);
            pl.Layer = LayerKatSiniri;
            AppendEntity(tr, btr, pl);
        }

        /// <summary>Kat sınırı (çizim sınırı) dışına aksların taşabileceği minimum mesafe (cm) — en az bu kadar uzatılır.</summary>
        private const double MinAxisExtensionBeyondBoundaryCm = 220.0;
        /// <summary>Kat sınırı (çizim sınırı) dışına aksların taşabileceği maksimum mesafe (cm).</summary>
        private const double MaxAxisExtensionBeyondBoundaryCm = 240.0;

        private void DrawAxes(Transaction tr, BlockTableRecord btr, double offsetX, double offsetY,
            (double Xmin, double Xmax, double Ymin, double Ymax) ext)
        {
            var xKolonAks = BuildColumnAxisIds(c => c.AxisXId, _model.AxisX.Select(a => a.Id));
            var yKolonAks = BuildColumnAxisIds(c => c.AxisYId, _model.AxisY.Select(a => a.Id));

            const double axisBalonRadiusCm = 25.0;   // çap 50 cm
            const double axisLabelHeightCm = 25.0;

            const double dimOffsetFromBalonCm = 55.0;  // ölçü çizgisi aks balonundan 55 cm (içeri doğru)
            const double dimRowGapCm = 20.0;           // iki aks ölçü sırası arasında 20 cm
            const double dimTextHeightCm = 12.0;

            double xLo = ext.Xmin + offsetX - MaxAxisExtensionBeyondBoundaryCm;
            double xHi = ext.Xmax + offsetX + MaxAxisExtensionBeyondBoundaryCm;
            double yLo = ext.Ymin + offsetY - MaxAxisExtensionBeyondBoundaryCm;
            double yHi = ext.Ymax + offsetY + MaxAxisExtensionBeyondBoundaryCm;

            var xAxisTopPositions = new List<Point3d>();
            var xAxisBottomPositions = new List<Point3d>();
            var yAxisLeftPositions = new List<Point3d>();
            var yAxisRightPositions = new List<Point3d>();

            for (int i = 0; i < _model.AxisX.Count; i++)
            {
                var ax = _model.AxisX[i];
                if (!xKolonAks.Contains(ax.Id)) continue;
                if (Math.Abs(ax.Slope) > 1e-9) continue; // sadece eğimi olmayan akslar için ölçü
                double yBot = ext.Ymin + offsetY - MinAxisExtensionBeyondBoundaryCm;
                double yTop = ext.Ymax + offsetY + MinAxisExtensionBeyondBoundaryCm;
                double xBot = offsetX + ax.ValueCm + ax.Slope * (ext.Ymin - MinAxisExtensionBeyondBoundaryCm);
                double xTop = offsetX + ax.ValueCm + ax.Slope * (ext.Ymax + MinAxisExtensionBeyondBoundaryCm);
                var p1 = new Point3d(xBot, yBot, 0);
                var p2 = new Point3d(xTop, yTop, 0);
                if (!ClipSegmentToRectangle(p1, p2, xLo, xHi, yLo, yHi, out Point3d q1, out Point3d q2)) continue;
                AppendEntity(tr, btr, new Line(q1, q2) { Layer = LayerAks });
                string xLabel = (i + 1).ToString(CultureInfo.InvariantCulture);
                Point3d centerTop = AxisBalonCenterAtEnd(q2, q1, axisBalonRadiusCm);
                Point3d centerBot = AxisBalonCenterAtEnd(q1, q2, axisBalonRadiusCm);
                xAxisTopPositions.Add(centerTop);
                xAxisBottomPositions.Add(centerBot);
                var circleTop = new Circle(centerTop, Vector3d.ZAxis, axisBalonRadiusCm) { Layer = LayerAksBalonu };
                AppendEntity(tr, btr, circleTop);
                AppendEntity(tr, btr, MakeCenteredText(LayerAksYazisi, axisLabelHeightCm, xLabel, centerTop));
                var circleBot = new Circle(centerBot, Vector3d.ZAxis, axisBalonRadiusCm) { Layer = LayerAksBalonu };
                AppendEntity(tr, btr, circleBot);
                AppendEntity(tr, btr, MakeCenteredText(LayerAksYazisi, axisLabelHeightCm, xLabel, centerBot));
            }

            for (int j = 0; j < _model.AxisY.Count; j++)
            {
                var ay = _model.AxisY[j];
                if (!yKolonAks.Contains(ay.Id)) continue;
                if (Math.Abs(ay.Slope) > 1e-9) continue; // sadece eğimi olmayan akslar için ölçü
                double xLeft = ext.Xmin + offsetX - MinAxisExtensionBeyondBoundaryCm;
                double xRight = ext.Xmax + offsetX + MinAxisExtensionBeyondBoundaryCm;
                double yLeft = -(ay.ValueCm + ay.Slope * (ext.Xmin - MinAxisExtensionBeyondBoundaryCm)) + offsetY;
                double yRight = -(ay.ValueCm + ay.Slope * (ext.Xmax + MinAxisExtensionBeyondBoundaryCm)) + offsetY;
                var p1 = new Point3d(xLeft, yLeft, 0);
                var p2 = new Point3d(xRight, yRight, 0);
                if (!ClipSegmentToRectangle(p1, p2, xLo, xHi, yLo, yHi, out Point3d q1, out Point3d q2)) continue;
                AppendEntity(tr, btr, new Line(q1, q2) { Layer = LayerAks });
                string yLabel = j < 26 ? ((char)('A' + j)).ToString() : "A" + (j - 25).ToString(CultureInfo.InvariantCulture);
                Point3d centerRight = AxisBalonCenterAtEnd(q2, q1, axisBalonRadiusCm);
                Point3d centerLeft = AxisBalonCenterAtEnd(q1, q2, axisBalonRadiusCm);
                yAxisLeftPositions.Add(centerLeft);
                yAxisRightPositions.Add(centerRight);
                var circleRight = new Circle(centerRight, Vector3d.ZAxis, axisBalonRadiusCm) { Layer = LayerAksBalonu };
                AppendEntity(tr, btr, circleRight);
                AppendEntity(tr, btr, MakeCenteredText(LayerAksYazisi, axisLabelHeightCm, yLabel, centerRight));
                var circleLeft = new Circle(centerLeft, Vector3d.ZAxis, axisBalonRadiusCm) { Layer = LayerAksBalonu };
                AppendEntity(tr, btr, circleLeft);
                AppendEntity(tr, btr, MakeCenteredText(LayerAksYazisi, axisLabelHeightCm, yLabel, centerLeft));
            }

            Database db = btr.Database;
            ObjectId aksOlcuDimStyleId = GetOrCreateAksOlcuDimStyle(tr, db, dimTextHeightCm);
            if (xAxisTopPositions.Count >= 2)
                DrawAxisDimensionsXFourSides(tr, btr, xAxisTopPositions, xAxisBottomPositions, dimOffsetFromBalonCm, dimRowGapCm, aksOlcuDimStyleId);
            if (yAxisLeftPositions.Count >= 2)
                DrawAxisDimensionsYFourSides(tr, btr, yAxisLeftPositions, yAxisRightPositions, dimOffsetFromBalonCm, dimRowGapCm, aksOlcuDimStyleId);
        }

        /// <summary>X aksları için 4 tarafta (üst + alt) çift sıra ölçü; balondan içeri doğru 55 cm ve 75 cm.</summary>
        private void DrawAxisDimensionsXFourSides(Transaction tr, BlockTableRecord btr, List<Point3d> topPositions, List<Point3d> bottomPositions, double offsetFromBalonCm, double rowGapCm, ObjectId dimStyleId)
        {
            var sortedTop = topPositions.OrderBy(p => p.X).ToList();
            var sortedBot = bottomPositions.OrderBy(p => p.X).ToList();
            if (sortedTop.Count < 2 || sortedBot.Count < 2) return;

            double xFirst = sortedTop[0].X, xLast = sortedTop[sortedTop.Count - 1].X;

            // Üst taraf: balondan içeri (aşağı) = refY - offset
            double refYTop = sortedTop.Max(p => p.Y);
            double yTotalTop = refYTop - offsetFromBalonCm;
            double yIndTop = refYTop - offsetFromBalonCm - rowGapCm;

            AppendEntity(tr, btr, new AlignedDimension(new Point3d(xFirst, yTotalTop, 0), new Point3d(xLast, yTotalTop, 0), new Point3d((xFirst + xLast) * 0.5, yTotalTop, 0), "", dimStyleId) { Layer = LayerAksOlcu });
            for (int i = 0; i < sortedTop.Count - 1; i++)
            {
                double x1 = sortedTop[i].X, x2 = sortedTop[i + 1].X;
                AppendEntity(tr, btr, new AlignedDimension(new Point3d(x1, yIndTop, 0), new Point3d(x2, yIndTop, 0), new Point3d((x1 + x2) * 0.5, yIndTop, 0), "", dimStyleId) { Layer = LayerAksOlcu });
            }

            // Alt taraf: balondan içeri (yukarı) = refYBottom + offset
            double refYBot = sortedBot.Min(p => p.Y);
            double yTotalBot = refYBot + offsetFromBalonCm;
            double yIndBot = refYBot + offsetFromBalonCm + rowGapCm;

            AppendEntity(tr, btr, new AlignedDimension(new Point3d(xFirst, yTotalBot, 0), new Point3d(xLast, yTotalBot, 0), new Point3d((xFirst + xLast) * 0.5, yTotalBot, 0), "", dimStyleId) { Layer = LayerAksOlcu });
            for (int i = 0; i < sortedBot.Count - 1; i++)
            {
                double x1 = sortedBot[i].X, x2 = sortedBot[i + 1].X;
                AppendEntity(tr, btr, new AlignedDimension(new Point3d(x1, yIndBot, 0), new Point3d(x2, yIndBot, 0), new Point3d((x1 + x2) * 0.5, yIndBot, 0), "", dimStyleId) { Layer = LayerAksOlcu });
            }
        }

        /// <summary>Y aksları için 4 tarafta (sol + sağ) çift sıra ölçü; balondan içeri doğru 55 cm ve 75 cm.</summary>
        private void DrawAxisDimensionsYFourSides(Transaction tr, BlockTableRecord btr, List<Point3d> leftPositions, List<Point3d> rightPositions, double offsetFromBalonCm, double rowGapCm, ObjectId dimStyleId)
        {
            var sortedLeft = leftPositions.OrderBy(p => p.Y).ToList();
            var sortedRight = rightPositions.OrderBy(p => p.Y).ToList();
            if (sortedLeft.Count < 2 || sortedRight.Count < 2) return;

            double yFirst = sortedLeft[0].Y, yLast = sortedLeft[sortedLeft.Count - 1].Y;

            // Sol taraf: balondan içeri (sağa) = refXLeft + offset
            double refXLeft = sortedLeft.Min(p => p.X);
            double xTotalLeft = refXLeft + offsetFromBalonCm;
            double xIndLeft = refXLeft + offsetFromBalonCm + rowGapCm;

            AppendEntity(tr, btr, new AlignedDimension(new Point3d(xTotalLeft, yFirst, 0), new Point3d(xTotalLeft, yLast, 0), new Point3d(xTotalLeft, (yFirst + yLast) * 0.5, 0), "", dimStyleId) { Layer = LayerAksOlcu });
            for (int i = 0; i < sortedLeft.Count - 1; i++)
            {
                double y1 = sortedLeft[i].Y, y2 = sortedLeft[i + 1].Y;
                AppendEntity(tr, btr, new AlignedDimension(new Point3d(xIndLeft, y1, 0), new Point3d(xIndLeft, y2, 0), new Point3d(xIndLeft, (y1 + y2) * 0.5, 0), "", dimStyleId) { Layer = LayerAksOlcu });
            }

            // Sağ taraf: balondan içeri (sola) = refXRight - offset
            double refXRight = sortedRight.Max(p => p.X);
            double xTotalRight = refXRight - offsetFromBalonCm;
            double xIndRight = refXRight - offsetFromBalonCm - rowGapCm;

            AppendEntity(tr, btr, new AlignedDimension(new Point3d(xTotalRight, yFirst, 0), new Point3d(xTotalRight, yLast, 0), new Point3d(xTotalRight, (yFirst + yLast) * 0.5, 0), "", dimStyleId) { Layer = LayerAksOlcu });
            for (int i = 0; i < sortedRight.Count - 1; i++)
            {
                double y1 = sortedRight[i].Y, y2 = sortedRight[i + 1].Y;
                AppendEntity(tr, btr, new AlignedDimension(new Point3d(xIndRight, y1, 0), new Point3d(xIndRight, y2, 0), new Point3d(xIndRight, (y1 + y2) * 0.5, 0), "", dimStyleId) { Layer = LayerAksOlcu });
            }
        }

        /// <summary>Aks ölçüleri için özel dim style "AKS_OLCU": resimlerdeki ayarlar (metin beyaz 12 cm, oklar Oblique 5, birim Decimal 0.0, Fit/Text/Units/Tolerances).</summary>
        private static ObjectId GetOrCreateAksOlcuDimStyle(Transaction tr, Database db, double dimTextHeightCm)
        {
            var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (dst.Has(AksOlcuDimStyleName)) return dst[AksOlcuDimStyleName];

            ObjectId textStyleId = GetOrCreateAksOlcuTextStyle(tr, db);

            var newRec = new DimStyleTableRecord();
            newRec.Name = AksOlcuDimStyleName;
            try { if (!textStyleId.IsNull) newRec.Dimtxsty = textStyleId; } catch { }

            try { newRec.Dimtxt = dimTextHeightCm; } catch { }
            try { newRec.Dimclrt = Color.FromColorIndex(ColorMethod.ByAci, 7); } catch { }
            try { newRec.Dimgap = 2.0; } catch { }
            try { newRec.Dimtad = 1; } catch { }
            try { newRec.Dimtih = false; } catch { }
            try { newRec.Dimtoh = false; } catch { }

            try { newRec.Dimasz = 5.0; } catch { }

            try { newRec.Dimdec = 0; } catch { }
            try { newRec.Dimrnd = 0.5; } catch { }
            try { newRec.Dimlfac = 1.0; } catch { }
            try { newRec.Dimzin = 12; } catch { }
            try { newRec.Dimaunit = 0; } catch { }
            try { newRec.Dimadec = 0; } catch { }

            try { newRec.Dimtofl = true; } catch { }
            try { newRec.Dimscale = 1.0; } catch { }


            dst.UpgradeOpen();
            ObjectId id = dst.Add(newRec);
            tr.AddNewlyCreatedDBObject(newRec, true);
            dst.DowngradeOpen();
            return id;
        }

        /// <summary>Ölçü yazıları için text style: Bahnschrift Light Condensed, yükseklik 0 (ölçü stili belirler), genişlik 1, eğik 0.</summary>
        private static ObjectId GetOrCreateAksOlcuTextStyle(Transaction tr, Database db)
        {
            var txtTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (txtTable.Has(AksOlcuTextStyleName)) return txtTable[AksOlcuTextStyleName];

            var rec = new TextStyleTableRecord();
            rec.Name = AksOlcuTextStyleName;
            try
            {
                rec.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Bahnschrift Light Condensed", false, false, 0, 0);
            }
            catch
            {
                try { rec.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Bahnschrift", false, false, 0, 0); } catch { }
            }
            try { rec.TextSize = 0.0; } catch { }
            try { rec.XScale = 1.0; } catch { }
            try { rec.ObliquingAngle = 0.0; } catch { }

            txtTable.UpgradeOpen();
            ObjectId id = txtTable.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            txtTable.DowngradeOpen();
            return id;
        }

        /// <summary>Ölçü ve aks hariç diğer eleman etiketleri (kiriş, perde, döşeme vb.) için text style: Bahnschrift Light Condensed.</summary>
        private static ObjectId GetOrCreateElemanEtiketTextStyle(Transaction tr, Database db)
        {
            var txtTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (txtTable.Has(ElemanEtiketTextStyleName)) return txtTable[ElemanEtiketTextStyleName];

            var rec = new TextStyleTableRecord();
            rec.Name = ElemanEtiketTextStyleName;
            try
            {
                rec.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Bahnschrift Light Condensed", false, false, 0, 0);
            }
            catch
            {
                try { rec.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Bahnschrift", false, false, 0, 0); } catch { }
            }
            try { rec.TextSize = 0.0; } catch { }
            try { rec.XScale = 1.0; } catch { }
            try { rec.ObliquingAngle = 0.0; } catch { }

            txtTable.UpgradeOpen();
            ObjectId id = txtTable.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            txtTable.DowngradeOpen();
            return id;
        }

        /// <summary>Doğru parçasını dikdörtgene kırpar. Kırpılmış uçları q1, q2 olarak döndürür. Parça dikdörtgenle kesişmiyorsa false.</summary>
        private static bool ClipSegmentToRectangle(Point3d p1, Point3d p2, double xLo, double xHi, double yLo, double yHi,
            out Point3d q1, out Point3d q2)
        {
            double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
            double tMin = 0.0, tMax = 1.0;
            const double tol = 1e-9;

            if (Math.Abs(dx) < tol) { if (p1.X < xLo || p1.X > xHi) { q1 = default; q2 = default; return false; } }
            else
            {
                double txLo = (xLo - p1.X) / dx, txHi = (xHi - p1.X) / dx;
                if (dx > 0) { tMax = Math.Min(tMax, txHi); tMin = Math.Max(tMin, txLo); }
                else { tMax = Math.Min(tMax, txLo); tMin = Math.Max(tMin, txHi); }
            }
            if (Math.Abs(dy) < tol) { if (p1.Y < yLo || p1.Y > yHi) { q1 = default; q2 = default; return false; } }
            else
            {
                double tyLo = (yLo - p1.Y) / dy, tyHi = (yHi - p1.Y) / dy;
                if (dy > 0) { tMax = Math.Min(tMax, tyHi); tMin = Math.Max(tMin, tyLo); }
                else { tMax = Math.Min(tMax, tyLo); tMin = Math.Max(tMin, tyHi); }
            }
            if (tMin > tMax) { q1 = default; q2 = default; return false; }
            q1 = new Point3d(p1.X + tMin * dx, p1.Y + tMin * dy, 0);
            q2 = new Point3d(p1.X + tMax * dx, p1.Y + tMax * dy, 0);
            return true;
        }

        /// <summary>Aks çizgisi ucunda balon merkezini döndürür: çizgi dairenin kenarında biter, daire çizgi yönünde dışarıda.</summary>
        private static Point3d AxisBalonCenterAtEnd(Point3d lineEnd, Point3d otherEnd, double radiusCm)
        {
            Vector3d v = lineEnd - otherEnd;
            double len = v.Length;
            if (len < 1e-6) return lineEnd;
            return lineEnd + v * (radiusCm / len);
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
            Geometry kolonUnion = BuildKolonUnionSameFloorOnly(floor, offsetX, offsetY);
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
                var safeA = EnsureBoundarySafe(a, a.Factory);
                var safeB = EnsureBoundarySafe(b, b.Factory);
                var boundaryA = safeA?.Boundary;
                var boundaryB = safeB?.Boundary;
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

        /// <summary>GeometryCollection veya Boundary desteklemeyen geometriyi poligon listesine çevirip unionlar; Boundary çağrısı hata vermez.</summary>
        private static Geometry EnsureBoundarySafe(Geometry geom, GeometryFactory factory)
        {
            if (geom == null || geom.IsEmpty) return geom;
            if (geom is Polygon || geom is MultiPolygon) return geom;
            if (geom is NetTopologySuite.Geometries.GeometryCollection gc)
            {
                var list = new List<Geometry>();
                AddPolygonsToList(geom, list);
                if (list.Count == 0) return null;
                return list.Count == 1 ? list[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(list);
            }
            return geom;
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

        /// <summary>Verilen aks kesişiminde (axisId1 x axisId2) bu katta kolon varsa kolon merkezini (offset dahil) döndürür. Kiriş uçlarını kolon aksına uzatmak/kısaltmak için kullanılır. Kesit bulunamazsa aks kesişim noktası döner (uzatma/kısaltma yine uygulanır).</summary>
        private bool TryGetColumnCenterAtIntersection(FloorInfo floor, int axisId1, int axisId2, double offsetX, double offsetY, out Point2d center)
        {
            center = default;
            int axisX = (axisId1 >= 1001 && axisId1 <= 1999) ? axisId1 : axisId2;
            int axisY = (axisId1 >= 2001 && axisId1 <= 2999) ? axisId1 : axisId2;
            if (axisX == axisY || (axisX < 1001 || axisX > 1999) || (axisY < 2001 || axisY > 2999)) return false;
            var col = _model.Columns.Find(c => c.AxisXId == axisX && c.AxisYId == axisY);
            if (col == null) return false;
            if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) return false;
            int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
            int polygonSectionId = ResolvePolygonPositionSectionId(floor.FloorNo, col.ColumnNo);
            if (col.ColumnType == 3 && (polygonSectionId <= 0 || !_model.PolygonColumnSectionByPositionSectionId.ContainsKey(polygonSectionId)))
            {
                center = new Point2d(axisNode.X + offsetX, axisNode.Y + offsetY);
                return true;
            }
            if (col.ColumnType != 3 && (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId)))
            {
                if (col.ColumnId > 0 && _model.ColumnDimsBySectionId.ContainsKey(col.ColumnId))
                    sectionId = col.ColumnId;
                else
                {
                    center = new Point2d(axisNode.X + offsetX, axisNode.Y + offsetY);
                    return true;
                }
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
            center = new Point2d(axisNode.X + offsetGlobal.X + offsetX, axisNode.Y + offsetGlobal.Y + offsetY);
            return true;
        }

        /// <summary>Tek bir kolonun bu kattaki poligonunu (alanını) döndürür. Poligon kolon (3) ve uzun boyutu kısa boyutunun 6 katına eşit veya büyük kolonlar kullanılmaz; geometriyi bozarlar. Kiriş–kolon alan kesişimi için kullanılır.</summary>
        private Polygon GetColumnPolygon(FloorInfo floor, ColumnAxisInfo col, double offsetX, double offsetY, GeometryFactory factory)
        {
            if (col.ColumnType == 3) return null;
            if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) return null;
            int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
            if (col.ColumnType != 3 && (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId)) && col.ColumnId > 0 && _model.ColumnDimsBySectionId.ContainsKey(col.ColumnId))
                sectionId = col.ColumnId;
            if (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId))
                sectionId = 0;
            var dim = sectionId > 0 && _model.ColumnDimsBySectionId.ContainsKey(sectionId) ? _model.ColumnDimsBySectionId[sectionId] : (W: 40.0, H: 40.0);
            double longSide = Math.Max(dim.W, dim.H);
            double shortSide = Math.Min(dim.W, dim.H);
            if (shortSide <= 1e-9 || longSide >= 6.0 * shortSide) return null;
            double hw = dim.W / 2.0, hh = dim.H / 2.0;
            var offsetLocal = col.ColumnType == 2 ? ComputeColumnOffsetCircle(col.OffsetXRaw, col.OffsetYRaw) : ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
            var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
            var center = new Point2d(axisNode.X + offsetGlobal.X + offsetX, axisNode.Y + offsetGlobal.Y + offsetY);
            Coordinate[] coords;
            if (col.ColumnType == 2)
            {
                double radius = Math.Max(hw, hh);
                coords = BuildCircleRing(center, radius, col.AngleDeg, 64);
            }
            else
            {
                var rect = BuildRect(center, hw, hh, col.AngleDeg);
                coords = new Coordinate[5];
                for (int i = 0; i < 4; i++) coords[i] = new Coordinate(rect[i].X, rect[i].Y);
                coords[4] = coords[0];
            }
            return factory.CreatePolygon(factory.CreateLinearRing(coords));
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

        /// <summary>Verilen kattaki sadece bu katta kesiti tanımlı olan kolonların birleşik alanı. Perde kesiminde kullanılır; ColumnId fallback kullanılmaz.</summary>
        private Geometry BuildKolonUnionSameFloorOnly(FloorInfo floor, double offsetX, double offsetY)
        {
            var factory = new GeometryFactory();
            var geoms = new List<Geometry>();

            foreach (var col in _model.Columns)
            {
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
                int polygonSectionId = ResolvePolygonPositionSectionId(floor.FloorNo, col.ColumnNo);
                // Aynı kat kuralı: ColumnId fallback yok; sadece bu katta çözülen kesit
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

        /// <summary>Kolon + perde + kiriş (IsWallFlag!=1) birleşimi; merdiveni bunlardan çıkararak çizmek için.</summary>
        private Geometry BuildKolonPerdeKirisUnion(FloorInfo floor, double offsetX, double offsetY)
        {
            Geometry kolonPerde = BuildKolonPerdeUnion(floor, offsetX, offsetY);
            var factory = new GeometryFactory();
            var geoms = new List<Geometry>();
            if (kolonPerde != null && !kolonPerde.IsEmpty)
                AddPolygonsToList(kolonPerde, geoms);

            var beams = MergeSameIdBeamsOnFloor(floor.FloorNo);
            foreach (var beam in beams)
            {
                if (beam.IsWallFlag == 1) continue;
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
                var coords = new[]
                {
                    new Coordinate(a.X + perp.X * upperEdge, a.Y + perp.Y * upperEdge),
                    new Coordinate(b.X + perp.X * upperEdge, b.Y + perp.Y * upperEdge),
                    new Coordinate(b.X + perp.X * lowerEdge, b.Y + perp.Y * lowerEdge),
                    new Coordinate(a.X + perp.X * lowerEdge, a.Y + perp.Y * lowerEdge),
                    new Coordinate(a.X + perp.X * upperEdge, a.Y + perp.Y * upperEdge)
                };
                geoms.Add(factory.CreatePolygon(factory.CreateLinearRing(coords)));
            }
            if (geoms.Count == 0) return kolonPerde;
            return geoms.Count == 1 ? geoms[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(geoms);
        }

        private void DrawBeamsAndWalls(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY)
        {
            var factory = new GeometryFactory();
            var wallList = new List<(Geometry poly, int fixedAxisId, BeamInfo beam, Point2d a, Point2d b)>();
            Geometry kolonPerdeUnion = BuildKolonPerdeUnion(floor, offsetX, offsetY);
            var kolonPerdeSafe = (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty) ? EnsureBoundarySafe(kolonPerdeUnion, new GeometryFactory()) : null;
            Geometry kolonPerdeBoundary = (kolonPerdeSafe != null && !kolonPerdeSafe.IsEmpty) ? kolonPerdeSafe.Boundary : null;
            const double beamEndExtensionCm = 22.0;   // Perde ucu kolona değiyorsa 22 cm uzatılır
            const double touchEpsilonCm = 0.2;        // Uç kolon sınırında kabul

            // Perdeler: aynı akstaki birleştirme listesi kullanılır. Kirişler: birleştirme iptal, modeldeki ham kayıtlar kullanılır.
            var beamsForWalls = MergeSameIdBeamsOnFloor(floor.FloorNo);
            var beamsForDrawing = _model.Beams.Where(b => GetBeamFloorNo(b.BeamId) == floor.FloorNo && b.IsWallFlag != 1).ToList();

            foreach (var beam in beamsForWalls)
            {
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1)) continue;
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2)) continue;

                var a = new Point2d(p1.X + offsetX, p1.Y + offsetY);
                var b = new Point2d(p2.X + offsetX, p2.Y + offsetY);
                NormalizeBeamDirection(beam.FixedAxisId, ref a, ref b);
                Vector2d dir = b - a;
                if (dir.Length <= 1e-9) continue;
                Vector2d u = dir.GetNormal();
                // Kiriş birleştirme/kesim iptal: uzatma sadece perde (duvar) için uygulanır; kirişler ham aks aralığıyla çizilir.
                if (beam.IsWallFlag == 1 && kolonPerdeBoundary != null && !kolonPerdeBoundary.IsEmpty)
                {
                    var ptA = factory.CreatePoint(new Coordinate(a.X, a.Y));
                    var ptB = factory.CreatePoint(new Coordinate(b.X, b.Y));
                    var mid = factory.CreatePoint(new Coordinate((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5));
                    double distA = ptA.Distance(kolonPerdeBoundary);
                    double distB = ptB.Distance(kolonPerdeBoundary);
                    bool aOnCol = distA <= touchEpsilonCm;
                    bool bOnCol = distB <= touchEpsilonCm;
                    bool midInside = kolonPerdeUnion.Contains(mid);
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
                    wallList.Add((factory.CreatePolygon(factory.CreateLinearRing(coordsWall)), beam.FixedAxisId, beam, a, b));
                }
            }

            // Kirişler: aynı BeamId'ye sahip segmentler tek birleşik geometri olarak çizilir (ID başına bir çizim).
            // Etiket konumu için çizilen geometri, ilk segment yönü ve gruptaki en kısa parça uzunluğu (cm) kaydedilir.
            var beamLabelInfos = new List<(int beamId, Geometry drawnGeometry, BeamInfo firstBeam, Point2d firstA, Point2d firstB, double minSegmentLengthCm)>();
            var beamsById = beamsForDrawing.GroupBy(b => b.BeamId).ToList();
            foreach (var group in beamsById)
            {
                var polygons = new List<Geometry>();
                Point2d? firstAlignedA = null;
                Point2d? firstAlignedB = null;
                var segmentEndpoints = new List<(Point2d a, Point2d b)>();
                foreach (var beam in group)
                {
                    if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1)) continue;
                    if (!_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2)) continue;
                    var a = new Point2d(p1.X + offsetX, p1.Y + offsetY);
                    var b = new Point2d(p2.X + offsetX, p2.Y + offsetY);
                    // Adım 2: Kirişin kapsadığı alan ile kolonun kapsadığı alan kesişiyorsa, kesişen taraftaki ucu o kolonun merkezine (aks üzerinde izdüşüm) çek.
                    Vector2d axisDir = b - a;
                    if (axisDir.Length > 1e-9)
                    {
                        Vector2d axisU = axisDir.GetNormal();
                        double len = axisDir.Length;
                        Point2d a0 = a;
                        Vector2d perp0 = new Vector2d(-axisU.Y, axisU.X);
                        double hw0 = beam.WidthCm / 2.0;
                        ComputeBeamEdgeOffsets(beam.OffsetRaw, hw0, out double upperEdge0, out double lowerEdge0);
                        var beamCoords = new[]
                        {
                            new Coordinate((a0 + perp0.MultiplyBy(upperEdge0)).X, (a0 + perp0.MultiplyBy(upperEdge0)).Y),
                            new Coordinate((b + perp0.MultiplyBy(upperEdge0)).X, (b + perp0.MultiplyBy(upperEdge0)).Y),
                            new Coordinate((b + perp0.MultiplyBy(lowerEdge0)).X, (b + perp0.MultiplyBy(lowerEdge0)).Y),
                            new Coordinate((a0 + perp0.MultiplyBy(lowerEdge0)).X, (a0 + perp0.MultiplyBy(lowerEdge0)).Y),
                            new Coordinate((a0 + perp0.MultiplyBy(upperEdge0)).X, (a0 + perp0.MultiplyBy(upperEdge0)).Y)
                        };
                        Geometry beamPoly = factory.CreatePolygon(factory.CreateLinearRing(beamCoords));
                        const double intersectionToleranceCm = 0.2;
                        Geometry beamPolyTol = beamPoly.Buffer(intersectionToleranceCm);
                        double tMin = len;
                        double tMax = 0;
                        // Kiriş hangi katta ise o kattaki kolonları baz al (sadece bu katta kesiti çözülen kolonlar)
                        foreach (var col in _model.Columns)
                        {
                            int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
                            int polygonSectionId = ResolvePolygonPositionSectionId(floor.FloorNo, col.ColumnNo);
                            if (col.ColumnType == 3) { if (polygonSectionId <= 0 || !_model.PolygonColumnSectionByPositionSectionId.ContainsKey(polygonSectionId)) continue; }
                            else if (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId)) continue;
                            Polygon colPoly = GetColumnPolygon(floor, col, offsetX, offsetY, factory);
                            if (colPoly == null || colPoly.IsEmpty || !beamPolyTol.Intersects(colPoly)) continue;
                            if (!TryGetColumnCenterAtIntersection(floor, col.AxisXId, col.AxisYId, offsetX, offsetY, out Point2d colCenter)) continue;
                            double t = (colCenter - a0).DotProduct(axisU);
                            if (t <= len * 0.5)
                                tMin = Math.Min(tMin, t);
                            else
                                tMax = Math.Max(tMax, t);
                        }
                        if (tMin <= tMax + 1e-9)
                        {
                            if (tMin > len - 1e-9) tMin = 0;
                            if (tMax < 1e-9) tMax = len;
                            if (tMax > tMin + 1e-9)
                            {
                                a = a0 + axisU.MultiplyBy(tMin);
                                b = a0 + axisU.MultiplyBy(tMax);
                            }
                        }
                    }
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
                    var coordsBeam = new[]
                    {
                        new Coordinate(q1.X, q1.Y),
                        new Coordinate(q2.X, q2.Y),
                        new Coordinate(q3.X, q3.Y),
                        new Coordinate(q4.X, q4.Y),
                        new Coordinate(q1.X, q1.Y)
                    };
                    if (!firstAlignedA.HasValue) { firstAlignedA = a; firstAlignedB = b; }
                    segmentEndpoints.Add((a, b));
                    polygons.Add(factory.CreatePolygon(factory.CreateLinearRing(coordsBeam)));
                }
                if (polygons.Count == 0) continue;
                // Aynı ID'li birden fazla parça varsa: kiriş boyu tüm parçanın uçlarına göre (bölünme noktaları uzunluğu etkilemez)
                if (segmentEndpoints.Count >= 2 && firstAlignedA.HasValue && firstAlignedB.HasValue)
                {
                    Vector2d u = (firstAlignedB.Value - firstAlignedA.Value).GetNormal();
                    Point2d origin = firstAlignedA.Value;
                    double tMin = 0;
                    double tMax = (firstAlignedB.Value - firstAlignedA.Value).Length;
                    foreach (var (a, b) in segmentEndpoints)
                    {
                        double ta = (a - origin).DotProduct(u);
                        double tb = (b - origin).DotProduct(u);
                        tMin = Math.Min(tMin, Math.Min(ta, tb));
                        tMax = Math.Max(tMax, Math.Max(ta, tb));
                    }
                    firstAlignedA = origin + u.MultiplyBy(tMin);
                    firstAlignedB = origin + u.MultiplyBy(tMax);
                }
                Geometry toDraw = polygons.Count == 1
                    ? polygons[0]
                    : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(polygons);
                if (toDraw != null && !toDraw.IsEmpty)
                {
                    DrawGeometryRingsAsPolylines(tr, btr, toDraw, LayerKiris, addHatch: false, exteriorRingsOnly: false, applySmallTriangleTrim: false);
                    if (firstAlignedA.HasValue && firstAlignedB.HasValue)
                    {
                        double minSeg = segmentEndpoints.Count > 0 ? segmentEndpoints.Min(s => (s.b - s.a).Length) : (firstAlignedB.Value - firstAlignedA.Value).Length;
                        beamLabelInfos.Add((group.Key, toDraw, group.First(), firstAlignedA.Value, firstAlignedB.Value, minSeg));
                    }
                }
            }
            var wallLabelInfos = new List<(int beamId, Geometry drawnGeometry, BeamInfo beam, Point2d firstA, Point2d firstB)>();
            if (wallList.Count > 0)
            {
                Geometry kolonUnion = BuildKolonUnionSameFloorOnly(floor, offsetX, offsetY);
                foreach (var (wallPoly, fixedAxisId, beam, a, b) in wallList)
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
                    {
                        DrawGeometryRingsAsPolylines(tr, btr, toDraw, LayerPerde, addHatch: true, hatchAngleRad: GetAxisAngleRad(fixedAxisId), applySmallTriangleTrim: false);
                        wallLabelInfos.Add((beam.BeamId, toDraw, beam, a, b));
                    }
                }
            }

            // Perde ID yazıları: birleştirilmiş liste kullanılmaz; modeldeki her perde kaydı (Start–End aks çifti) için ayrı ID yazılır (40–41 ve 41–42 ayrı görünsün).
            foreach (var beam in _model.Beams)
            {
                if (GetBeamFloorNo(beam.BeamId) != floor.FloorNo) continue;
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
                var center = new Point3d((q1.X + q2.X + q3.X + q4.X) / 4.0, (q1.Y + q2.Y + q3.Y + q4.Y) / 4.0, 0);
                AppendEntity(tr, btr, MakeCenteredText(LayerYazi, 6, beam.BeamId.ToString(CultureInfo.InvariantCulture), center));
            }

            // Kiriş etiketleri: çizimde 70x14 cm referans (resimdeki gibi). Boyutlar ve 4 köşe koordinatı hafızada; 2 cm kuralı sabit.
            Database db = btr.Database;
            Geometry baseObstaclesBeams = null;
            try
            {
                if (beamLabelInfos.Count > 0)
                {
                    var allBeamGeoms = beamLabelInfos.Select(x => x.drawnGeometry).ToList();
                    Geometry allBeamsUnion = allBeamGeoms.Count == 1 ? allBeamGeoms[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(allBeamGeoms);
                    baseObstaclesBeams = (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty)
                        ? kolonPerdeUnion.Union(allBeamsUnion)
                        : allBeamsUnion;
                }
                else if (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty)
                    baseObstaclesBeams = kolonPerdeUnion;
            }
            catch { }

            // Y kiriş etiketini uzunluk çizgisinin başına hizalamak için önce her kirişin (tStart, tEnd) segmentini hesapla
            const double halfSpanCm = 500.0;
            const double beamLengthLineShortenCm = 20.0;
            const double beamLengthShortThresholdCm = 160.0; // Bu uzunluktan kısa kirişlerde engel araması yapılmaz, tam boy kullanılır
            var beamLengthSegmentByBeamId = new Dictionary<int, (double tStart, double tEnd)>();
            Geometry kolonUnionForBeamLength = BuildKolonUnionSameFloorOnly(floor, offsetX, offsetY);
            Geometry allWallsUnionForBeamLength = null;
            try
            {
                if (wallLabelInfos.Count > 0)
                {
                    var wallGeoms = wallLabelInfos.Select(x => x.drawnGeometry).ToList();
                    allWallsUnionForBeamLength = wallGeoms.Count == 1 ? wallGeoms[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(wallGeoms);
                }
            }
            catch { }
            foreach (var (beamId, drawnGeometry, firstBeam, firstA, firstB, minSegmentLengthCm) in beamLabelInfos)
            {
                Vector2d dir = firstB - firstA;
                if (dir.Length <= 1e-9) continue;
                Vector2d u = dir.GetNormal();
                Vector2d perp = new Vector2d(-u.Y, u.X);
                double L = dir.Length;
                double halfL = L * 0.5;
                // 160 cm ve daha kısa kirişler (veya gruptaki en kısa parça 160 cm ve kısaysa): engel araması yapılmaz, tam kiriş boyu (uçlardan 20 cm kısaltılmış) kullanılır
                if (L <= beamLengthShortThresholdCm || minSegmentLengthCm <= beamLengthShortThresholdCm)
                {
                    beamLengthSegmentByBeamId[beamId] = (-halfL + beamLengthLineShortenCm, halfL - beamLengthLineShortenCm);
                    continue;
                }
                Point2d center = drawnGeometry.Centroid != null && !drawnGeometry.Centroid.IsEmpty
                    ? new Point2d(drawnGeometry.Centroid.X, drawnGeometry.Centroid.Y)
                    : new Point2d((firstA.X + firstB.X) * 0.5, (firstA.Y + firstB.Y) * 0.5);
                // Kiriş uzunluğu: perde gibi — kolon tam (şerit kırpması yok), diğer kiriş ve perde sadece %25 uç şeritlerinde. Uzunluk = kolon/diğer kiriş/perde sınırları arası, çizgi 20 cm kısaltılarak.
                Geometry obstaclesForLength = null;
                try
                {
                    const double perpExtendCm = 60.0;
                    double tEnd1 = 0.25 * L;
                    double tStart2 = 0.75 * L;
                    var zoneEnd1 = CreateAxisStripPolygon(firstA, u, perp, 0, tEnd1, perpExtendCm, factory);
                    var zoneEnd2 = CreateAxisStripPolygon(firstA, u, perp, tStart2, L, perpExtendCm, factory);
                    // Kolon: perde mantığıyla tam kullan (kırpma yok), böylece kolon yüzü doğru kesilir
                    Geometry kolonFull = (kolonUnionForBeamLength != null && !kolonUnionForBeamLength.IsEmpty) ? EnsureBoundarySafe(kolonUnionForBeamLength, factory) : null;
                    // Diğer kiriş ve perdeleri sadece %25 uç şeritleri içinde kalan kısımlarıyla ekle
                    var otherBeamGeoms = new List<Geometry>();
                    foreach (var x in beamLabelInfos)
                    {
                        if (x.beamId == beamId) continue;
                        if (x.drawnGeometry == null || x.drawnGeometry.IsEmpty) continue;
                        if (!x.drawnGeometry.Intersects(zoneEnd1) && !x.drawnGeometry.Intersects(zoneEnd2)) continue;
                        var inter1 = EnsureBoundarySafe(x.drawnGeometry.Intersection(zoneEnd1), factory);
                        var inter2 = EnsureBoundarySafe(x.drawnGeometry.Intersection(zoneEnd2), factory);
                        if (inter1 != null && !inter1.IsEmpty) otherBeamGeoms.Add(inter1);
                        if (inter2 != null && !inter2.IsEmpty) otherBeamGeoms.Add(inter2);
                    }
                    Geometry otherBeamsUnion = null;
                    if (otherBeamGeoms.Count == 1) otherBeamsUnion = otherBeamGeoms[0];
                    else if (otherBeamGeoms.Count > 1) otherBeamsUnion = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(otherBeamGeoms);
                    var wallGeomsInEnds = new List<Geometry>();
                    foreach (var x in wallLabelInfos)
                    {
                        if (x.drawnGeometry == null || x.drawnGeometry.IsEmpty) continue;
                        if (!x.drawnGeometry.Intersects(zoneEnd1) && !x.drawnGeometry.Intersects(zoneEnd2)) continue;
                        var inter1 = EnsureBoundarySafe(x.drawnGeometry.Intersection(zoneEnd1), factory);
                        var inter2 = EnsureBoundarySafe(x.drawnGeometry.Intersection(zoneEnd2), factory);
                        if (inter1 != null && !inter1.IsEmpty) wallGeomsInEnds.Add(inter1);
                        if (inter2 != null && !inter2.IsEmpty) wallGeomsInEnds.Add(inter2);
                    }
                    Geometry wallsUnion = null;
                    if (wallGeomsInEnds.Count == 1) wallsUnion = wallGeomsInEnds[0];
                    else if (wallGeomsInEnds.Count > 1) wallsUnion = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(wallGeomsInEnds);
                    Geometry baseUnion = kolonFull;
                    if (otherBeamsUnion != null && !otherBeamsUnion.IsEmpty)
                        baseUnion = baseUnion != null && !baseUnion.IsEmpty ? EnsureBoundarySafe(baseUnion.Union(otherBeamsUnion), factory) : otherBeamsUnion;
                    if (wallsUnion != null && !wallsUnion.IsEmpty)
                        obstaclesForLength = baseUnion != null && !baseUnion.IsEmpty ? EnsureBoundarySafe(baseUnion.Union(wallsUnion), factory) : wallsUnion;
                    else
                        obstaclesForLength = baseUnion;
                }
                catch { }
                obstaclesForLength = EnsureBoundarySafe(obstaclesForLength, factory);
                double tStart, tEnd;
                // Merkez doğrusu kirişin tamamını kapsasın; sadece kirişle örtüşen açıklık seçilsin ve kiriş boyunu aşmasın
                double halfSpanForBeam = Math.Max(halfSpanCm, L * 0.5 + 50.0);
                if (obstaclesForLength != null && !obstaclesForLength.IsEmpty &&
                    GetCenterLineClearSegment(center, u, obstaclesForLength, factory, halfSpanForBeam, out tStart, out tEnd, beamHalfLength: halfL))
                {
                    // Bulunan segment kiriş boyuna göre çok kısaysa (yanlış açıklık seçimi) tam kiriş boyuna düş
                    double segLen = tEnd - tStart;
                    if (segLen >= L * 0.35)
                        beamLengthSegmentByBeamId[beamId] = (tStart, tEnd);
                    else
                        beamLengthSegmentByBeamId[beamId] = (-halfL + beamLengthLineShortenCm, halfL - beamLengthLineShortenCm);
                }
                else
                {
                    // Engel yoksa veya net boş segment bulunamadıysa tüm kiriş boyunca çiz (uçlardan 20 cm kısaltılmış). t merkeze göre: -L/2 .. +L/2
                    beamLengthSegmentByBeamId[beamId] = (-halfL + beamLengthLineShortenCm, halfL - beamLengthLineShortenCm);
                }
            }

            const double minSegmentAfterShortenCm = 1.0;
            foreach (var (beamId, drawnGeometry, firstBeam, firstA, firstB, _) in beamLabelInfos)
            {
                Vector2d dir = firstB - firstA;
                if (dir.Length <= 1e-9) continue;
                Vector2d u = dir.GetNormal();
                Vector2d perp = new Vector2d(-u.Y, u.X);

                if (!GetBeamDrawnCorners(drawnGeometry, firstA, u, perp, out Point2d rectBottomLeft, out Point2d rectUpperRight, out Point2d rectBottomRight))
                    continue;

                int beamFloor = GetBeamFloorNo(beamId);
                var floorInfo = _model.Floors.FirstOrDefault(f => f.FloorNo == beamFloor);
                string katEtiketi = floorInfo?.ShortName ?? beamFloor.ToString(CultureInfo.InvariantCulture);
                int beamNumero = GetBeamNumero(beamId);
                string labelText = string.Format(CultureInfo.InvariantCulture, "K{0}{1} ({2}/{3})",
                    katEtiketi, beamNumero, (int)Math.Round(firstBeam.WidthCm), (int)Math.Round(firstBeam.HeightCm));

                // Etiket boyutları (resimdeki gibi): 70cm x 14cm referans, genişlik = 70 * karakter sayısı / 13
                double labelHeightCm = BeamLabelRefHeightCm;
                int charCount = Math.Max(1, labelText?.Length ?? 0);
                double labelWidthCm = BeamLabelRefWidthCm * charCount / BeamLabelRefCharCount;

                double tMin = (rectBottomLeft - firstA).DotProduct(u);
                double tMax = (rectBottomRight - firstA).DotProduct(u);
                double pMin = (rectBottomLeft - firstA).DotProduct(perp);
                const double labelOffsetFromAxisCm = 2.0;

                bool isFixedX = firstBeam.FixedAxisId >= 1001 && firstBeam.FixedAxisId <= 1999;
                double beamAngleRad = Math.Atan2(u.Y, u.X);
                double tIns;
                Point2d center = drawnGeometry.Centroid != null && !drawnGeometry.Centroid.IsEmpty
                    ? new Point2d(drawnGeometry.Centroid.X, drawnGeometry.Centroid.Y)
                    : new Point2d((firstA.X + firstB.X) * 0.5, (firstA.Y + firstB.Y) * 0.5);
                double tCenter = (center - firstA).DotProduct(u);
                if (isFixedX)
                {
                    // X aksı: etiketin sağ kenarı (referans noktası) = kiriş uzunluk çizgisinin 2. noktası; kısa segmentte kısaltma uyarlanır
                    if (beamLengthSegmentByBeamId.TryGetValue(beamId, out var seg))
                    {
                        double segLen = seg.tEnd - seg.tStart;
                        double shortenEach = Math.Min(beamLengthLineShortenCm, Math.Max(0, (segLen - minSegmentAfterShortenCm) * 0.5));
                        double tLineEnd = tCenter + seg.tEnd - shortenEach;
                        tIns = Math.Max(tMin, tLineEnd - labelWidthCm);
                    }
                    else
                        tIns = Math.Max(tMin, tMax - labelWidthCm);
                }
                else
                {
                    // Y aksı: yazıyı KIRIS UZUNLUK çizgisinin ilk noktasının eksene izdüşümüne taşı; kısa segmentte kısaltma uyarlanır
                    if (beamLengthSegmentByBeamId.TryGetValue(beamId, out var seg))
                    {
                        double segLen = seg.tEnd - seg.tStart;
                        double shortenEach = Math.Min(beamLengthLineShortenCm, Math.Max(0, (segLen - minSegmentAfterShortenCm) * 0.5));
                        double tFirstPoint = tCenter + seg.tStart + shortenEach;
                        tIns = Math.Max(tMin, Math.Min(tFirstPoint, tMax - labelWidthCm));
                    }
                    else
                        tIns = tMin;
                }
                Point2d insertion = firstA + u.MultiplyBy(tIns) + perp.MultiplyBy(pMin + labelOffsetFromAxisCm);

                GetLabelBoxCorners(insertion, labelWidthCm, labelHeightCm, beamAngleRad, out _, out Point2d br, out _, out _);
                bool useBottomRight = isFixedX;
                Point3d labelInsert = useBottomRight ? new Point3d(br.X, br.Y, 0) : new Point3d(insertion.X, insertion.Y, 0);
                DrawBeamLabel(tr, btr, db, labelInsert, labelText, labelHeightCm, beamAngleRad, bottomLeftAligned: !useBottomRight);
            }

            // Kiriş uzunluk çizgileri artık çizilmiyor; segment değerleri (beamLengthSegmentByBeamId) sadece etiket yerleşimi için hafızada kullanılıyor.

            // Perde etiketleri: kiriş ile aynı mantık — çizilen perde geometrisine göre sol alt/alt sağ + 2 cm, 15 cm adımlarla merkeze kaydırma. Ölçü: eni/uzunluk (uzunluk = merkez doğrusunun kolonları kestiği noktalar arası).
            const double wallLabelHeightCm = 12.0;
            const double wallLabelOffsetCm = 2.0;
            const double wallLabelStepCm = 15.0;
            Geometry kolonUnionForWalls = wallLabelInfos.Count > 0 ? BuildKolonUnionSameFloorOnly(floor, offsetX, offsetY) : null;
            Geometry baseObstaclesWalls = null;
            try
            {
                if (wallLabelInfos.Count > 0)
                {
                    var allWallGeoms = wallLabelInfos.Select(x => x.drawnGeometry).ToList();
                    Geometry allWallsUnion = allWallGeoms.Count == 1 ? allWallGeoms[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(allWallGeoms);
                    baseObstaclesWalls = (baseObstaclesBeams != null && !baseObstaclesBeams.IsEmpty)
                        ? baseObstaclesBeams.Union(allWallsUnion)
                        : (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty ? kolonPerdeUnion.Union(allWallsUnion) : allWallsUnion);
                }
                else
                    baseObstaclesWalls = baseObstaclesBeams;
            }
            catch { }
            foreach (var (wallBeamId, drawnGeometry, beam, firstA, firstB) in wallLabelInfos)
            {
                Vector2d dir = firstB - firstA;
                if (dir.Length <= 1e-9) continue;
                Vector2d u = dir.GetNormal();
                Vector2d perp = new Vector2d(-u.Y, u.X);
                if (!GetBeamDrawnCorners(drawnGeometry, firstA, u, perp, out Point2d rectBottomLeft, out Point2d rectUpperRight, out Point2d rectBottomRight))
                    continue;
                int beamFloor = GetBeamFloorNo(wallBeamId);
                var floorInfo = _model.Floors.FirstOrDefault(f => f.FloorNo == beamFloor);
                string katEtiketi = floorInfo?.ShortName ?? beamFloor.ToString(CultureInfo.InvariantCulture);
                int beamNumero = GetBeamNumero(wallBeamId);
                Point2d wallCenter = drawnGeometry.Centroid != null && !drawnGeometry.Centroid.IsEmpty
                    ? new Point2d(drawnGeometry.Centroid.X, drawnGeometry.Centroid.Y)
                    : new Point2d((firstA.X + firstB.X) * 0.5, (firstA.Y + firstB.Y) * 0.5);
                double perdeLengthCm = GetPerdeLengthCm(wallCenter, u, kolonUnionForWalls, factory, firstA.GetDistanceTo(firstB));
                const double perdeLabelMinLengthForDimensionsCm = 160.0;
                string labelText = perdeLengthCm < perdeLabelMinLengthForDimensionsCm
                    ? string.Format(CultureInfo.InvariantCulture, "P{0}{1}", katEtiketi, beamNumero)
                    : string.Format(CultureInfo.InvariantCulture, "P{0}{1} ({2}/{3})",
                        katEtiketi, beamNumero, (int)Math.Round(beam.WidthCm), (int)Math.Round(perdeLengthCm));
                double textWidthCm = EstimateTextWidthCm(labelText, wallLabelHeightCm);
                double tMin = (rectBottomLeft - firstA).DotProduct(u);
                double tMax = (rectBottomRight - firstA).DotProduct(u);
                double pMin = (rectBottomLeft - firstA).DotProduct(perp);
                double tCenter = (wallCenter - firstA).DotProduct(u);
                const double perdeLengthShortenCmShort = 20.0;
                const double perdeLengthShortenCmLong = 30.0;
                const double perdeLabelLongThresholdCm = 160.0;
                const double minPerdeSegmentAfterShortenCm = 1.0;
                double tLineStart = tMin;
                double tLineEnd = tMax;
                if (TryGetPerdeLengthSegment(wallCenter, u, kolonUnionForWalls, factory, out double tSegStart, out double tSegEnd))
                {
                    double perdeSegLen = tSegEnd - tSegStart;
                    double maxShortenCm = perdeLengthCm >= perdeLabelLongThresholdCm ? perdeLengthShortenCmLong : perdeLengthShortenCmShort;
                    double shortenEach = Math.Min(maxShortenCm, Math.Max(0, (perdeSegLen - minPerdeSegmentAfterShortenCm) * 0.5));
                    tLineStart = tCenter + tSegStart + shortenEach;
                    tLineEnd = tCenter + tSegEnd - shortenEach;
                }
                Geometry obstacles = null;
                try
                {
                    if (baseObstaclesWalls != null && !baseObstaclesWalls.IsEmpty && drawnGeometry != null && !drawnGeometry.IsEmpty)
                        obstacles = baseObstaclesWalls.Difference(drawnGeometry);
                }
                catch { }
                bool isFixedX = beam.FixedAxisId >= 1001 && beam.FixedAxisId <= 1999;
                double angleRad = Math.Atan2(u.Y, u.X);
                Point2d insertion;
                if (isFixedX)
                {
                    double tIns = Math.Max(tMin, tLineEnd - textWidthCm);
                    insertion = firstA + u.MultiplyBy(tIns) + perp.MultiplyBy(pMin + wallLabelOffsetCm);
                    while (obstacles != null && TextBoxIntersectsObstacles(insertion, textWidthCm, wallLabelHeightCm, angleRad, 0, obstacles, factory) && tIns > tMin + 1e-6)
                    {
                        tIns -= wallLabelStepCm;
                        if (tIns < tMin) { tIns = tMin; break; }
                        insertion = firstA + u.MultiplyBy(tIns) + perp.MultiplyBy(pMin + wallLabelOffsetCm);
                    }
                    insertion = firstA + u.MultiplyBy(tIns) + perp.MultiplyBy(pMin + wallLabelOffsetCm);
                }
                else
                {
                    double tIns = Math.Max(tMin, Math.Min(tMax - textWidthCm, tLineStart));
                    insertion = firstA + u.MultiplyBy(tIns) + perp.MultiplyBy(pMin + wallLabelOffsetCm);
                    while (obstacles != null && TextBoxIntersectsObstacles(insertion, textWidthCm, wallLabelHeightCm, angleRad, 0, obstacles, factory) && tIns + textWidthCm <= tMax - 1e-6)
                    {
                        tIns += wallLabelStepCm;
                        if (tIns + textWidthCm > tMax) { tIns = Math.Max(tMin, tMax - textWidthCm); break; }
                        insertion = firstA + u.MultiplyBy(tIns) + perp.MultiplyBy(pMin + wallLabelOffsetCm);
                    }
                    insertion = firstA + u.MultiplyBy(tIns) + perp.MultiplyBy(pMin + wallLabelOffsetCm);
                }
                GetLabelBoxCorners(insertion, textWidthCm, wallLabelHeightCm, angleRad, out _, out Point2d br, out _, out _);
                Point3d labelInsert = isFixedX ? new Point3d(br.X, br.Y, 0) : new Point3d(insertion.X, insertion.Y, 0);
                DrawBeamLabel(tr, btr, db, labelInsert, labelText, wallLabelHeightCm, angleRad, LayerPerdeYazisi, bottomLeftAligned: !isFixedX);
            }
        }

        /// <summary>Verilen kattaki perde isimlerini (P{kat}{no} eni/uzunluk) çizer. Temel planında ilk kat perdeleri için kullanılır.</summary>
        /// <param name="kolonPerdeUnionForObstacles">Temel planında engel alanı için (kiriş yok); null ise BuildKolonPerdeUnion ile hesaplanır.</param>
        private void DrawPerdeLabelsForFloor(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY, Geometry kolonPerdeUnionForObstacles = null)
        {
            var factory = new GeometryFactory();
            Geometry kolonPerdeUnion = kolonPerdeUnionForObstacles ?? BuildKolonPerdeUnion(floor, offsetX, offsetY);
            var kolonPerdeSafe = (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty) ? EnsureBoundarySafe(kolonPerdeUnion, factory) : null;
            Geometry kolonPerdeBoundary = (kolonPerdeSafe != null && !kolonPerdeSafe.IsEmpty) ? kolonPerdeSafe.Boundary : null;
            const double beamEndExtensionCm = 22.0;
            const double touchEpsilonCm = 0.2;

            var wallList = new List<(Geometry poly, int fixedAxisId, BeamInfo beam, Point2d a, Point2d b)>();
            var beamsForWalls = MergeSameIdBeamsOnFloor(floor.FloorNo);
            foreach (var beam in beamsForWalls)
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
                if (kolonPerdeBoundary != null && !kolonPerdeBoundary.IsEmpty)
                {
                    var ptA = factory.CreatePoint(new Coordinate(a.X, a.Y));
                    var ptB = factory.CreatePoint(new Coordinate(b.X, b.Y));
                    var mid = factory.CreatePoint(new Coordinate((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5));
                    bool aOnCol = ptA.Distance(kolonPerdeBoundary) <= touchEpsilonCm;
                    bool bOnCol = ptB.Distance(kolonPerdeBoundary) <= touchEpsilonCm;
                    bool midInside = kolonPerdeUnion.Contains(mid);
                    var extA = factory.CreatePoint(new Coordinate(a.X - beamEndExtensionCm * u.X, a.Y - beamEndExtensionCm * u.Y));
                    var extB = factory.CreatePoint(new Coordinate(b.X + beamEndExtensionCm * u.X, b.Y + beamEndExtensionCm * u.Y));
                    if (aOnCol && !midInside && kolonPerdeUnion.Contains(extA)) a = a - u.MultiplyBy(beamEndExtensionCm);
                    if (bOnCol && !midInside && kolonPerdeUnion.Contains(extB)) b = b + u.MultiplyBy(beamEndExtensionCm);
                }
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
                wallList.Add((factory.CreatePolygon(factory.CreateLinearRing(coordsWall)), beam.FixedAxisId, beam, a, b));
            }
            if (wallList.Count == 0) return;

            Geometry kolonUnion = BuildKolonUnionSameFloorOnly(floor, offsetX, offsetY);
            var wallLabelInfos = new List<(int beamId, Geometry drawnGeometry, BeamInfo beam, Point2d firstA, Point2d firstB)>();
            foreach (var (wallPoly, fixedAxisId, beam, a, b) in wallList)
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
                    wallLabelInfos.Add((beam.BeamId, toDraw, beam, a, b));
            }
            if (wallLabelInfos.Count == 0) return;

            Geometry kolonUnionForWalls = kolonUnion;
            Geometry baseObstaclesWalls = null;
            try
            {
                var allWallGeoms = wallLabelInfos.Select(x => x.drawnGeometry).ToList();
                Geometry allWallsUnion = allWallGeoms.Count == 1 ? allWallGeoms[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(allWallGeoms);
                baseObstaclesWalls = (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty) ? kolonPerdeUnion.Union(allWallsUnion) : allWallsUnion;
            }
            catch { }

            Database db = btr.Database;
            const double wallLabelHeightCm = 12.0;
            const double wallLabelOffsetCm = 2.0;
            const double wallLabelStepCm = 15.0;
            foreach (var (wallBeamId, drawnGeometry, beam, firstA, firstB) in wallLabelInfos)
            {
                Vector2d dir = firstB - firstA;
                if (dir.Length <= 1e-9) continue;
                Vector2d u = dir.GetNormal();
                Vector2d perp = new Vector2d(-u.Y, u.X);
                if (!GetBeamDrawnCorners(drawnGeometry, firstA, u, perp, out Point2d rectBottomLeft, out Point2d rectUpperRight, out Point2d rectBottomRight))
                    continue;
                int beamFloor = GetBeamFloorNo(wallBeamId);
                var floorInfo = _model.Floors.FirstOrDefault(f => f.FloorNo == beamFloor);
                string katEtiketi = floorInfo?.ShortName ?? beamFloor.ToString(CultureInfo.InvariantCulture);
                int beamNumero = GetBeamNumero(wallBeamId);
                Point2d wallCenter = drawnGeometry.Centroid != null && !drawnGeometry.Centroid.IsEmpty
                    ? new Point2d(drawnGeometry.Centroid.X, drawnGeometry.Centroid.Y)
                    : new Point2d((firstA.X + firstB.X) * 0.5, (firstA.Y + firstB.Y) * 0.5);
                double perdeLengthCm = GetPerdeLengthCm(wallCenter, u, kolonUnionForWalls, factory, firstA.GetDistanceTo(firstB));
                const double perdeLabelMinLengthForDimensionsCm = 160.0;
                string labelText = perdeLengthCm < perdeLabelMinLengthForDimensionsCm
                    ? string.Format(CultureInfo.InvariantCulture, "P{0}{1}", katEtiketi, beamNumero)
                    : string.Format(CultureInfo.InvariantCulture, "P{0}{1} ({2}/{3})",
                        katEtiketi, beamNumero, (int)Math.Round(beam.WidthCm), (int)Math.Round(perdeLengthCm));
                double textWidthCm = EstimateTextWidthCm(labelText, wallLabelHeightCm);
                double tMin = (rectBottomLeft - firstA).DotProduct(u);
                double tMax = (rectBottomRight - firstA).DotProduct(u);
                double pMin = (rectBottomLeft - firstA).DotProduct(perp);
                double tCenter = (wallCenter - firstA).DotProduct(u);
                const double perdeLengthShortenCmShort = 20.0;
                const double perdeLengthShortenCmLong = 30.0;
                const double perdeLabelLongThresholdCm = 160.0;
                const double minPerdeSegmentAfterShortenCm = 1.0;
                double tLineStart = tMin;
                double tLineEnd = tMax;
                if (TryGetPerdeLengthSegment(wallCenter, u, kolonUnionForWalls, factory, out double tSegStart, out double tSegEnd))
                {
                    double perdeSegLen = tSegEnd - tSegStart;
                    double maxShortenCm = perdeLengthCm >= perdeLabelLongThresholdCm ? perdeLengthShortenCmLong : perdeLengthShortenCmShort;
                    double shortenEach = Math.Min(maxShortenCm, Math.Max(0, (perdeSegLen - minPerdeSegmentAfterShortenCm) * 0.5));
                    tLineStart = tCenter + tSegStart + shortenEach;
                    tLineEnd = tCenter + tSegEnd - shortenEach;
                }
                Geometry obstacles = null;
                try
                {
                    if (baseObstaclesWalls != null && !baseObstaclesWalls.IsEmpty && drawnGeometry != null && !drawnGeometry.IsEmpty)
                        obstacles = baseObstaclesWalls.Difference(drawnGeometry);
                }
                catch { }
                bool isFixedX = beam.FixedAxisId >= 1001 && beam.FixedAxisId <= 1999;
                double angleRad = Math.Atan2(u.Y, u.X);
                Point2d insertion;
                if (isFixedX)
                {
                    double tIns = Math.Max(tMin, tLineEnd - textWidthCm);
                    insertion = firstA + u.MultiplyBy(tIns) + perp.MultiplyBy(pMin + wallLabelOffsetCm);
                    while (obstacles != null && TextBoxIntersectsObstacles(insertion, textWidthCm, wallLabelHeightCm, angleRad, 0, obstacles, factory) && tIns > tMin + 1e-6)
                    {
                        tIns -= wallLabelStepCm;
                        if (tIns < tMin) { tIns = tMin; break; }
                        insertion = firstA + u.MultiplyBy(tIns) + perp.MultiplyBy(pMin + wallLabelOffsetCm);
                    }
                    insertion = firstA + u.MultiplyBy(tIns) + perp.MultiplyBy(pMin + wallLabelOffsetCm);
                }
                else
                {
                    double tIns = Math.Max(tMin, Math.Min(tMax - textWidthCm, tLineStart));
                    insertion = firstA + u.MultiplyBy(tIns) + perp.MultiplyBy(pMin + wallLabelOffsetCm);
                    while (obstacles != null && TextBoxIntersectsObstacles(insertion, textWidthCm, wallLabelHeightCm, angleRad, 0, obstacles, factory) && tIns + textWidthCm <= tMax - 1e-6)
                    {
                        tIns += wallLabelStepCm;
                        if (tIns + textWidthCm > tMax) { tIns = Math.Max(tMin, tMax - textWidthCm); break; }
                        insertion = firstA + u.MultiplyBy(tIns) + perp.MultiplyBy(pMin + wallLabelOffsetCm);
                    }
                    insertion = firstA + u.MultiplyBy(tIns) + perp.MultiplyBy(pMin + wallLabelOffsetCm);
                }
                GetLabelBoxCorners(insertion, textWidthCm, wallLabelHeightCm, angleRad, out _, out Point2d br, out _, out _);
                Point3d labelInsert = isFixedX ? new Point3d(br.X, br.Y, 0) : new Point3d(insertion.X, insertion.Y, 0);
                DrawBeamLabel(tr, btr, db, labelInsert, labelText, wallLabelHeightCm, angleRad, LayerPerdeYazisi, bottomLeftAligned: !isFixedX);
            }
        }

        /// <summary>
        /// Bu kata ait döşemeler: merdiven döşemeleri kolon/kiriş/perde alanlarından çıkarılarak sınır çizilir + ID; normal döşemelerde sadece ID.
        /// Köşeler sırayla: (axis1,axis3), (axis1,axis4), (axis2,axis4), (axis2,axis3).
        /// </summary>
        private void DrawSlabs(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY)
        {
            int floorNo = floor.FloorNo;
            Geometry kolonPerdeKirisUnion = null;
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
                bool isStair = _model.StairSlabIds.Contains(slab.SlabId);
                if (isStair)
                {
                    var factory = new GeometryFactory();
                    var coords = new Coordinate[pts.Length + 1];
                    for (int i = 0; i < pts.Length; i++)
                        coords[i] = new Coordinate(pts[i].X, pts[i].Y);
                    coords[pts.Length] = coords[0];
                    var stairPoly = factory.CreatePolygon(factory.CreateLinearRing(coords));
                    if (stairPoly != null && !stairPoly.IsEmpty)
                    {
                        if (kolonPerdeKirisUnion == null)
                            kolonPerdeKirisUnion = BuildKolonPerdeKirisUnion(floor, offsetX, offsetY);
                        Geometry toDraw = stairPoly;
                        if (kolonPerdeKirisUnion != null && !kolonPerdeKirisUnion.IsEmpty)
                        {
                            try
                            {
                                var diff = stairPoly.Difference(kolonPerdeKirisUnion);
                                if (diff != null && !diff.IsEmpty) toDraw = diff;
                            }
                            catch { }
                        }
                        if (toDraw != null && !toDraw.IsEmpty)
                            DrawGeometryRingsAsPolylines(tr, btr, toDraw, LayerMerdiven, addHatch: false, applySmallTriangleTrim: false);
                    }
                }
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
                var kolonPerdeSafe = (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty) ? EnsureBoundarySafe(kolonPerdeUnion, factory) : null;
                Geometry kolonPerdeBoundary = (kolonPerdeSafe != null && !kolonPerdeSafe.IsEmpty) ? kolonPerdeSafe.Boundary : null;
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

        /// <summary>Kiriş numarasını kat bilgisi olmadan döndürür: BeamFloorKeyStep varsa beamId % step, yoksa % 1000 veya % 100.</summary>
        private int GetBeamNumero(int beamId)
        {
            if (_model.BeamFloorKeyStep > 0) return beamId % _model.BeamFloorKeyStep;
            return beamId >= 1000 ? (beamId % 1000) : (beamId % 100);
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

        /// <summary>Kiriş/perde etiketi: bottomLeftAligned false ise Bottom Right, true ise Bottom Left justify; açıya göre döndürülmüş, ETIKET style. layer verilmezse KIRIS ISMI.</summary>
        private void DrawBeamLabel(Transaction tr, BlockTableRecord btr, Database db, Point3d insertionPoint, string labelText, double textHeightCm, double rotationRad, string layer = null, bool bottomLeftAligned = true)
        {
            if (string.IsNullOrEmpty(layer)) layer = LayerKirisYazisi;
            ObjectId textStyleId = GetOrCreateElemanEtiketTextStyle(tr, db);
            var txt = new DBText
            {
                Layer = layer,
                TextStyleId = textStyleId,
                Height = textHeightCm,
                TextString = labelText ?? string.Empty,
                Position = insertionPoint,
                HorizontalMode = bottomLeftAligned ? TextHorizontalMode.TextLeft : TextHorizontalMode.TextRight,
                VerticalMode = TextVerticalMode.TextBottom,
                AlignmentPoint = insertionPoint,
                Rotation = rotationRad
            };
            AppendEntity(tr, btr, txt);
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

        /// <summary>X aksında sağa yapışık (t=0.75), Y aksında sola yapışık (t=0.25).</summary>
        private static double GetBeamLabelAlongParameter(int fixedAxisId, Vector2d dir)
        {
            bool isFixedY = fixedAxisId >= 2001 && fixedAxisId <= 2999;
            bool isFixedX = fixedAxisId >= 1001 && fixedAxisId <= 1999;
            if (isFixedX) return dir.X >= 0 ? 0.75 : 0.25; // X kirişi: sağa
            if (isFixedY) return dir.Y >= 0 ? 0.25 : 0.75; // Y kirişi: sola
            return 0.5;
        }

        /// <summary>Yazı kutusu (bottom-left, genişlik, yükseklik, dönüş) + clearance buffer ile engelleri kesişiyorsa true.</summary>
        private static bool TextBoxIntersectsObstacles(Point2d insertionBottomLeft, double widthCm, double heightCm, double rotationRad, double clearanceCm, Geometry obstacles, GeometryFactory factory)
        {
            if (obstacles == null || obstacles.IsEmpty) return false;
            double c = Math.Cos(rotationRad);
            double s = Math.Sin(rotationRad);
            var p0 = insertionBottomLeft;
            var p1 = new Point2d(p0.X + widthCm * c, p0.Y + widthCm * s);
            var p2 = new Point2d(p1.X - heightCm * s, p1.Y + heightCm * c);
            var p3 = new Point2d(p0.X - heightCm * s, p0.Y + heightCm * c);
            var ring = new[]
            {
                new Coordinate(p0.X, p0.Y),
                new Coordinate(p1.X, p1.Y),
                new Coordinate(p2.X, p2.Y),
                new Coordinate(p3.X, p3.Y),
                new Coordinate(p0.X, p0.Y)
            };
            try
            {
                var box = factory.CreatePolygon(factory.CreateLinearRing(ring));
                var buffered = box.Buffer(clearanceCm);
                return buffered != null && !buffered.IsEmpty && buffered.Intersects(obstacles);
            }
            catch { return true; }
        }

        /// <summary>Etiket metni için yaklaşık genişlik (cm); yükseklik * karakter sayısı * oran.</summary>
        private static double EstimateTextWidthCm(string labelText, double heightCm)
        {
            if (string.IsNullOrEmpty(labelText)) return 0;
            return labelText.Length * heightCm * 0.65;
        }

        /// <summary>Yazı kutusunun sol alt köşesi (insertion), genişlik, yükseklik ve dönüş açısına göre 4 köşe koordinatını döndürür. Sıra: sol alt, sağ alt, sağ üst, sol üst.</summary>
        private static void GetLabelBoxCorners(Point2d insertionBottomLeft, double widthCm, double heightCm, double angleRad,
            out Point2d bottomLeft, out Point2d bottomRight, out Point2d topRight, out Point2d topLeft)
        {
            double c = Math.Cos(angleRad);
            double s = Math.Sin(angleRad);
            bottomLeft = insertionBottomLeft;
            bottomRight = new Point2d(insertionBottomLeft.X + widthCm * c, insertionBottomLeft.Y + widthCm * s);
            topRight = new Point2d(bottomRight.X - heightCm * s, bottomRight.Y + heightCm * c);
            topLeft = new Point2d(insertionBottomLeft.X - heightCm * s, insertionBottomLeft.Y + heightCm * c);
        }

        /// <summary>Çizilen kiriş geometrisinin dış halka koordinatlarına göre, eksen yönü (u, perp) ile hizalı bounding köşelerini döndürür. origin: firstA. Sol alt, üst sağ, alt sağ.</summary>
        private static bool GetBeamDrawnCorners(Geometry drawnGeometry, Point2d origin, Vector2d u, Vector2d perp, out Point2d bottomLeft, out Point2d upperRight, out Point2d bottomRight)
        {
            bottomLeft = default;
            upperRight = default;
            bottomRight = default;
            if (drawnGeometry == null || drawnGeometry.IsEmpty) return false;
            var pts = new List<Point2d>();
            if (drawnGeometry is Polygon poly && poly.ExteriorRing != null)
            {
                foreach (var c in poly.ExteriorRing.Coordinates)
                    pts.Add(new Point2d(c.X, c.Y));
            }
            else if (drawnGeometry is MultiPolygon mp)
            {
                for (int i = 0; i < mp.NumGeometries; i++)
                    if (mp.GetGeometryN(i) is Polygon p && p.ExteriorRing != null)
                        foreach (var c in p.ExteriorRing.Coordinates)
                            pts.Add(new Point2d(c.X, c.Y));
            }
            else if (drawnGeometry is GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                    if (gc.GetGeometryN(i) is Polygon p2 && p2.ExteriorRing != null)
                        foreach (var c in p2.ExteriorRing.Coordinates)
                            pts.Add(new Point2d(c.X, c.Y));
            }
            if (pts.Count < 3) return false;
            double tMin = double.MaxValue, tMax = double.MinValue, pMin = double.MaxValue, pMax = double.MinValue;
            foreach (var p in pts)
            {
                Vector2d v = p - origin;
                double t = v.X * u.X + v.Y * u.Y;
                double pVal = v.X * perp.X + v.Y * perp.Y;
                if (t < tMin) tMin = t;
                if (t > tMax) tMax = t;
                if (pVal < pMin) pMin = pVal;
                if (pVal > pMax) pMax = pVal;
            }
            bottomLeft = origin + u.MultiplyBy(tMin) + perp.MultiplyBy(pMin);
            upperRight = origin + u.MultiplyBy(tMax) + perp.MultiplyBy(pMax);
            bottomRight = origin + u.MultiplyBy(tMax) + perp.MultiplyBy(pMin);
            return true;
        }

        /// <summary>Perde uzunluğu: iki kolon arası net açıklık (cm). Merkezden geçen doğrunun kolon boundary kesişim noktaları sıralanır; kolon dışında kalan segment (iç yüzler arası) uzunluğu döner.</summary>
        private static double GetPerdeLengthCm(Point2d center, Vector2d u, Geometry kolonUnion, GeometryFactory factory, double fallbackLength)
        {
            if (kolonUnion == null || kolonUnion.IsEmpty) return fallbackLength;
            var safe = EnsureBoundarySafe(kolonUnion, factory);
            if (safe == null || safe.IsEmpty) return fallbackLength;
            var boundary = safe.Boundary;
            if (boundary == null || boundary.IsEmpty) return fallbackLength;
            const double halfSpan = 500.0;
            var lineCoords = new[]
            {
                new Coordinate(center.X - halfSpan * u.X, center.Y - halfSpan * u.Y),
                new Coordinate(center.X + halfSpan * u.X, center.Y + halfSpan * u.Y)
            };
            try
            {
                var line = factory.CreateLineString(lineCoords);
                var inter = line.Intersection(boundary);
                if (inter == null || inter.IsEmpty) return fallbackLength;
                var pts = new List<Coordinate>();
                if (inter is NetTopologySuite.Geometries.Point pt)
                    pts.Add(pt.Coordinate);
                else if (inter is NetTopologySuite.Geometries.MultiPoint mp)
                    for (int i = 0; i < mp.NumGeometries; i++)
                        pts.Add(((NetTopologySuite.Geometries.Point)mp.GetGeometryN(i)).Coordinate);
                else if (inter is NetTopologySuite.Geometries.LineString ls)
                    pts.AddRange(ls.Coordinates);
                else if (inter is NetTopologySuite.Geometries.GeometryCollection gc)
                    for (int i = 0; i < gc.NumGeometries; i++)
                    {
                        var g = gc.GetGeometryN(i);
                        if (g is NetTopologySuite.Geometries.Point gp) pts.Add(gp.Coordinate);
                        else if (g is NetTopologySuite.Geometries.LineString gls) foreach (var c in gls.Coordinates) pts.Add(c);
                    }
                if (pts.Count < 2) return fallbackLength;
                var tList = new List<double>();
                foreach (var c in pts)
                    tList.Add((c.X - center.X) * u.X + (c.Y - center.Y) * u.Y);
                tList.Sort();
                // Önce perde merkezini (t=0) içeren kolon-dışı segmenti seç; yoksa en uzun segment (yanlış 240 cm yerine doğru 65 cm için).
                double bestGap = 0;
                double gapContainingCenter = -1;
                for (int i = 0; i < tList.Count - 1; i++)
                {
                    double tA = tList[i];
                    double tB = tList[i + 1];
                    double tMid = (tA + tB) * 0.5;
                    var midCoord = new Coordinate(center.X + tMid * u.X, center.Y + tMid * u.Y);
                    var midPoint = factory.CreatePoint(midCoord);
                    if (!kolonUnion.Contains(midPoint))
                    {
                        double gap = tB - tA;
                        if (gap > bestGap) bestGap = gap;
                        if (tA <= 0 && 0 <= tB)
                            gapContainingCenter = gap;
                    }
                }
                if (gapContainingCenter > 1e-6)
                    return gapContainingCenter;
                return bestGap > 1e-6 ? bestGap : fallbackLength;
            }
            catch { return fallbackLength; }
        }

        /// <summary>Perde uzunluk segmenti: merkezden geçen u doğrusunun kolon dışında kalan (eni/boyu) aralığı; merkeze göre t. Başarılı ise true ve tStart, tEnd döner.</summary>
        private static bool TryGetPerdeLengthSegment(Point2d center, Vector2d u, Geometry kolonUnion, GeometryFactory factory, out double tStart, out double tEnd)
        {
            tStart = 0;
            tEnd = 0;
            if (kolonUnion == null || kolonUnion.IsEmpty) return false;
            var safe = EnsureBoundarySafe(kolonUnion, factory);
            if (safe == null || safe.IsEmpty) return false;
            var boundary = safe.Boundary;
            if (boundary == null || boundary.IsEmpty) return false;
            const double halfSpan = 500.0;
            var lineCoords = new[]
            {
                new Coordinate(center.X - halfSpan * u.X, center.Y - halfSpan * u.Y),
                new Coordinate(center.X + halfSpan * u.X, center.Y + halfSpan * u.Y)
            };
            try
            {
                var line = factory.CreateLineString(lineCoords);
                var inter = line.Intersection(boundary);
                if (inter == null || inter.IsEmpty) return false;
                var pts = new List<Coordinate>();
                if (inter is NetTopologySuite.Geometries.Point pt)
                    pts.Add(pt.Coordinate);
                else if (inter is NetTopologySuite.Geometries.MultiPoint mp)
                    for (int i = 0; i < mp.NumGeometries; i++)
                        pts.Add(((NetTopologySuite.Geometries.Point)mp.GetGeometryN(i)).Coordinate);
                else if (inter is NetTopologySuite.Geometries.LineString ls)
                    pts.AddRange(ls.Coordinates);
                else if (inter is NetTopologySuite.Geometries.GeometryCollection gc)
                    for (int i = 0; i < gc.NumGeometries; i++)
                    {
                        var g = gc.GetGeometryN(i);
                        if (g is NetTopologySuite.Geometries.Point gp) pts.Add(gp.Coordinate);
                        else if (g is NetTopologySuite.Geometries.LineString gls) foreach (var c in gls.Coordinates) pts.Add(c);
                    }
                if (pts.Count < 2) return false;
                var tList = new List<double>();
                foreach (var c in pts)
                    tList.Add((c.X - center.X) * u.X + (c.Y - center.Y) * u.Y);
                tList.Sort();
                double bestGap = 0;
                double bestTA = 0, bestTB = 0;
                bool foundContainingCenter = false;
                double segTA = 0, segTB = 0;
                for (int i = 0; i < tList.Count - 1; i++)
                {
                    double tA = tList[i];
                    double tB = tList[i + 1];
                    double tMid = (tA + tB) * 0.5;
                    var midCoord = new Coordinate(center.X + tMid * u.X, center.Y + tMid * u.Y);
                    var midPoint = factory.CreatePoint(midCoord);
                    if (!kolonUnion.Contains(midPoint))
                    {
                        double gap = tB - tA;
                        if (gap > bestGap) { bestGap = gap; bestTA = tA; bestTB = tB; }
                        if (tA <= 0 && 0 <= tB) { foundContainingCenter = true; segTA = tA; segTB = tB; }
                    }
                }
                if (foundContainingCenter)
                {
                    tStart = segTA;
                    tEnd = segTB;
                    return true;
                }
                if (bestGap <= 1e-6) return false;
                tStart = bestTA;
                tEnd = bestTB;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Geometrinin firstA + t*u eksenine izdüşümündeki t aralığını döndürür (envelope köşelerinden).</summary>
        private static bool GetGeometryTRangeOnAxis(Geometry geom, Point2d firstA, Vector2d u, out double tMin, out double tMax)
        {
            tMin = double.MaxValue;
            tMax = double.MinValue;
            if (geom == null || geom.IsEmpty) return false;
            var env = geom.EnvelopeInternal;
            double[] xs = { env.MinX, env.MaxX };
            double[] ys = { env.MinY, env.MaxY };
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                {
                    double t = (xs[i] - firstA.X) * u.X + (ys[j] - firstA.Y) * u.Y;
                    if (t < tMin) tMin = t;
                    if (t > tMax) tMax = t;
                }
            return tMin <= tMax;
        }

        /// <summary>Eksen boyunca [t0, t1] ve dik yönde ±perpExtend cm şerit poligonu (firstA, u, perp ile).</summary>
        private static Geometry CreateAxisStripPolygon(Point2d firstA, Vector2d u, Vector2d perp, double t0, double t1, double perpExtend, GeometryFactory factory)
        {
            Point2d a0 = firstA + u.MultiplyBy(t0);
            Point2d a1 = firstA + u.MultiplyBy(t1);
            var coords = new[]
            {
                new Coordinate(a0.X - perpExtend * perp.X, a0.Y - perpExtend * perp.Y),
                new Coordinate(a0.X + perpExtend * perp.X, a0.Y + perpExtend * perp.Y),
                new Coordinate(a1.X + perpExtend * perp.X, a1.Y + perpExtend * perp.Y),
                new Coordinate(a1.X - perpExtend * perp.X, a1.Y - perpExtend * perp.Y),
                new Coordinate(a0.X - perpExtend * perp.X, a0.Y - perpExtend * perp.Y)
            };
            return factory.CreatePolygon(factory.CreateLinearRing(coords));
        }

        /// <summary>Perde uzunluğu gibi: merkez doğrusunun engel sınırıyla kesişimlerinden en uzun engel-dışı segmenti döndürür. beamHalfLength>0 ise sadece kirişle örtüşen açıklık seçilir ve sonuç [-beamHalfLength, beamHalfLength] ile kırpılır.</summary>
        private static bool GetCenterLineClearSegment(Point2d center, Vector2d u, Geometry obstaclesUnion, GeometryFactory factory, double halfSpan, out double tStart, out double tEnd, double beamHalfLength = 0)
        {
            tStart = 0;
            tEnd = 0;
            if (obstaclesUnion == null || obstaclesUnion.IsEmpty) return false;
            var safe = EnsureBoundarySafe(obstaclesUnion, factory);
            if (safe == null || safe.IsEmpty) return false;
            var boundary = safe.Boundary;
            if (boundary == null || boundary.IsEmpty) return false;
            var lineCoords = new[]
            {
                new Coordinate(center.X - halfSpan * u.X, center.Y - halfSpan * u.Y),
                new Coordinate(center.X + halfSpan * u.X, center.Y + halfSpan * u.Y)
            };
            try
            {
                var line = factory.CreateLineString(lineCoords);
                var inter = line.Intersection(boundary);
                if (inter == null || inter.IsEmpty) return false;
                var pts = new List<Coordinate>();
                if (inter is NetTopologySuite.Geometries.Point pt)
                    pts.Add(pt.Coordinate);
                else if (inter is NetTopologySuite.Geometries.MultiPoint mp)
                    for (int i = 0; i < mp.NumGeometries; i++)
                        pts.Add(((NetTopologySuite.Geometries.Point)mp.GetGeometryN(i)).Coordinate);
                else if (inter is NetTopologySuite.Geometries.LineString ls)
                    pts.AddRange(ls.Coordinates);
                else if (inter is NetTopologySuite.Geometries.GeometryCollection gc)
                    for (int i = 0; i < gc.NumGeometries; i++)
                    {
                        var g = gc.GetGeometryN(i);
                        if (g is NetTopologySuite.Geometries.Point gp) pts.Add(gp.Coordinate);
                        else if (g is NetTopologySuite.Geometries.LineString gls) foreach (var c in gls.Coordinates) pts.Add(c);
                    }
                if (pts.Count < 2) return false;
                var tList = new List<double>();
                foreach (var c in pts)
                    tList.Add((c.X - center.X) * u.X + (c.Y - center.Y) * u.Y);
                tList.Sort();
                double tMin = beamHalfLength > 0 ? -beamHalfLength : double.NegativeInfinity;
                double tMax = beamHalfLength > 0 ? beamHalfLength : double.PositiveInfinity;
                // Kiriş boyu: kiriş aralığıyla en çok örtüşen engel-dışı açıklığı seç (ana açıklık); merkez veya en uzun yerine bu daha güvenilir
                double bestOverlap = 0;
                for (int i = 0; i < tList.Count - 1; i++)
                {
                    double tA = tList[i];
                    double tB = tList[i + 1];
                    if (tB <= tMin || tA >= tMax) continue;
                    double tMid = (tA + tB) * 0.5;
                    var midPoint = factory.CreatePoint(new Coordinate(center.X + tMid * u.X, center.Y + tMid * u.Y));
                    if (!obstaclesUnion.Contains(midPoint))
                    {
                        double overlapStart = Math.Max(tA, tMin);
                        double overlapEnd = Math.Min(tB, tMax);
                        double overlap = overlapEnd - overlapStart;
                        if (overlap > bestOverlap)
                        {
                            bestOverlap = overlap;
                            tStart = tA;
                            tEnd = tB;
                        }
                    }
                }
                if (bestOverlap > 1e-6)
                {
                    if (beamHalfLength > 0) { tStart = Math.Max(tStart, tMin); tEnd = Math.Min(tEnd, tMax); }
                    return tEnd > tStart + 1e-6;
                }
                return false;
            }
            catch { return false; }
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

                if (b0.IsWallFlag == 1)
                {
                    // Perdelerde birleştirme yapılmaz; her segment ayrı kalır (100 cm kuralı sadece kirişler için).
                    foreach (var s in segs)
                    {
                        merged.Add(new BeamInfo
                        {
                            BeamId = s.Beam.BeamId,
                            FixedAxisId = s.Beam.FixedAxisId,
                            StartAxisId = s.StartAxis,
                            EndAxisId = s.EndAxis,
                            WidthCm = b0.WidthCm,
                            HeightCm = b0.HeightCm,
                            OffsetRaw = b0.OffsetRaw,
                            IsWallFlag = 1
                        });
                    }
                    continue;
                }

                // Kirişlerde: aralarında 1 m'den fazla boşluk olmayanları birleştir.
                int idx = 0;
                while (idx < segs.Count)
                {
                    var currentStart = segs[idx];
                    var currentEnd = segs[idx];
                    double currentEndPos = currentEnd.S2;
                    idx++;

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
