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

                bool hasFoundations = _model.ContinuousFoundations.Count > 0 || _model.SlabFoundations.Count > 0;
                int planStartIndex = hasFoundations ? 1 : 0;

                if (hasFoundations && _model.Floors.Count > 0)
                {
                    double offsetX = 0.0;
                    double offsetY = 0.0;
                    var firstFloor = _model.Floors[0];
                    DrawAxes(tr, btr, offsetX, offsetY, ext);
                    DrawColumns(tr, btr, firstFloor, offsetX, offsetY);
                    DrawWallsForFloor(tr, btr, firstFloor, offsetX, offsetY);
                    DrawContinuousFoundations(tr, btr, offsetX, offsetY);
                    DrawSlabFoundations(tr, btr, offsetX, offsetY);
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
                    hasFoundations ? string.Format(", temel plani (surekli: {0}, radye: {1})", _model.ContinuousFoundations.Count, _model.SlabFoundations.Count) : "");
            }
        }

        private const string LayerAks = "AKS CIZGISI (BEYKENT)";
        private const string LayerKiris = "KIRIS (BEYKENT)";
        private const string LayerKolon = "KOLON (BEYKENT)";
        private const string LayerPerde = "PERDE (BEYKENT)";
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
            EnsurePlanLayer(tr, db, LayerDoseme, 71, LineWeight.LineWeight030, useDashed: false);
            EnsurePlanLayer(tr, db, LayerMerdiven, 5, LineWeight.LineWeight030, useDashed: false);
            EnsurePlanLayer(tr, db, LayerYazi, 4, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, LayerBaslik, 4, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, "ST4-SUREKLI-TEMEL", 4, LineWeight.LineWeight030, useDashed: false);
            EnsurePlanLayer(tr, db, "ST4-RADYE-TEMEL", 5, LineWeight.LineWeight030, useDashed: false);
            EnsurePlanLayer(tr, db, "ST4-AMPATMAN", 6, LineWeight.LineWeight030, useDashed: false);
            EnsurePlanLayer(tr, db, "ST4-TEMEL-HATILI", 7, LineWeight.LineWeight030, useDashed: false);
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

                if (col.ColumnType == 2)
                {
                    AppendEntity(tr, btr, new Circle(new Point3d(center.X, center.Y, 0), Vector3d.ZAxis, Math.Max(hw, hh)) { Layer = LayerKolon });
                }
                else if (col.ColumnType == 3 && TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out var polyPoints))
                {
                    var pl = ToPolyline(polyPoints, true);
                    pl.Layer = LayerKolon;
                    AppendEntity(tr, btr, pl);
                }
                else
                {
                    var rect = BuildRect(center, hw, hh, col.AngleDeg);
                    var pl = ToPolyline(rect, true);
                    pl.Layer = LayerKolon;
                    AppendEntity(tr, btr, pl);
                }

                AppendEntity(tr, btr, MakeCenteredText(LayerYazi, 6, col.ColumnNo.ToString(CultureInfo.InvariantCulture), new Point3d(center.X, center.Y, 0)));
            }
        }

        /// <summary>Verilen kattaki perdeleri (IsWallFlag==1) çizer; temel planında bodrum perdeleri için kullanılır.</summary>
        private void DrawWallsForFloor(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY)
        {
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
                var poly = ToPolyline(new[] { q1, q2, q3, q4 }, true);
                poly.Layer = LayerPerde;
                AppendEntity(tr, btr, poly);
            }
        }

        private void DrawBeamsAndWalls(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY)
        {
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

                string layer = beam.IsWallFlag == 1 ? LayerPerde : LayerKiris;
                if (beam.IsWallFlag == 1)
                {
                    var poly = ToPolyline(new[] { q1, q2, q3, q4 }, true);
                    poly.Layer = layer;
                    AppendEntity(tr, btr, poly);
                }
                else
                {
                    AppendEntity(tr, btr, new Line(new Point3d(q1.X, q1.Y, 0), new Point3d(q2.X, q2.Y, 0)) { Layer = layer });
                    AppendEntity(tr, btr, new Line(new Point3d(q4.X, q4.Y, 0), new Point3d(q3.X, q3.Y, 0)) { Layer = layer });
                }
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

        private void DrawContinuousFoundations(Transaction tr, BlockTableRecord btr, double offsetX, double offsetY)
        {
            const string layer = "ST4-SUREKLI-TEMEL";
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
                var pl = ToPolyline(rect, true);
                pl.Layer = layer;
                AppendEntity(tr, btr, pl);

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
                    var plAmp = ToPolyline(ampRect, true);
                    plAmp.Layer = "ST4-AMPATMAN";
                    AppendEntity(tr, btr, plAmp);
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
                    var plHatil = ToPolyline(hatilRect, true);
                    plHatil.Layer = "ST4-TEMEL-HATILI";
                    AppendEntity(tr, btr, plHatil);
                }
            }
        }

        private void DrawSlabFoundations(Transaction tr, BlockTableRecord btr, double offsetX, double offsetY)
        {
            const string layer = "ST4-RADYE-TEMEL";
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
