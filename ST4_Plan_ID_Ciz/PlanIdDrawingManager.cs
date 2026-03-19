using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite;
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
    public sealed partial class PlanIdDrawingManager
    {
        private readonly St4Model _model;
        private readonly AxisGeometryService _axisService;
        /// <summary>Son çizilen kattaki kiriş geometrileri (birleştirilmiş parçalar); döşeme kesimi için DrawSlabs kullanır.</summary>
        private List<Geometry> _drawnBeamGeometriesForSlabCut;
        /// <summary>Son çizilen kattaki perde geometrileri (kolon diff sonrası); döşeme kesimi için DrawSlabs kullanır.</summary>
        private List<Geometry> _drawnWallGeometriesForSlabCut;
        /// <summary>Son çizilen kattaki döşeme geometrileri (kesim + en büyük parça); birleşik katman için.</summary>
        private List<Geometry> _drawnSlabGeometriesForUnion;
        /// <summary>ST4PLANID Draw süresince tek fabrika (binlerce new önlenir).</summary>
        private GeometryFactory _ntsDrawFactory;
        /// <summary>TEMEL50ST4 için özel 1:50 başlık/yazı modu.</summary>
        private bool _isTemel50Mode;
        /// <summary>KOLON50ST4 için kolon aplikasyon ölçü modu.</summary>
        private bool _isKolon50Mode;
        /// <summary>KOLON50ST4 perde+kolon kopyalarının dünya sınırları (GPR tablosu yerleşimi için).</summary>
        private Envelope _kolon50PerdeCopyExtent;
        private const double Temel50BaslikAltAksBalonBoslukCm = 50.0;
        /// <summary>antet_02 SHEETVIEW sol-alt (cizim birimi); bire bir yerlesimde bu köse layout sol-alt ile hizalanir.</summary>
        private const double AntetDxfSheetViewXmin = -2854.616393644967;
        private const double AntetDxfSheetViewYmin = 1658.94236737828;
        // Ileride: antet sablon degiskenleri — klon sonrasi DBText/ATTRIBUTE (sablon: antet_02.dwg + projede antet_02.dxf referans)
        private const string TemelAntetEmbeddedResourceName = "ST4PlanIdCiz.TemelAntet.antet_02.dwg";
        /// <summary>Statik kesit örnekleme / şerit buffer için tek örnek.</summary>
        private static readonly GeometryFactory StaticGeomFactory = new GeometryFactory();

        public PlanIdDrawingManager(St4Model model)
        {
            _model = model;
            _axisService = new AxisGeometryService(model);
        }

        /// <summary>Kolon donatı tablosu için: her kolon numarasında temel planında üzerine geldiği temelin yüksekliği (cm) ve o kolona değen en yüksek temel hatılı yüksekliği (cm). Temel planı ilk kat (floor) ile hesaplanır.</summary>
        public Dictionary<int, (double? temelCm, double? hatilCm)> GetColumnFoundationHeights(FloorInfo firstFloor)
        {
            var result = new Dictionary<int, (double?, double?)>();
            if (firstFloor == null) return result;
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory();
            const double offsetX = 0.0, offsetY = 0.0;

            foreach (var col in _model.Columns)
            {
                int colNo = col.ColumnNo;
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode))
                    continue;
                int sectionId = ResolveColumnSectionId(firstFloor.FloorNo, colNo);
                double hw = 20.0, hh = 20.0;
                if (sectionId > 0 && _model.ColumnDimsBySectionId.TryGetValue(sectionId, out var dim)) { hw = dim.W / 2.0; hh = dim.H / 2.0; }
                var offsetLocal = col.ColumnType == 2 ? ComputeColumnOffsetCircle(col.OffsetXRaw, col.OffsetYRaw) : ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                var center = new Point2d(axisNode.X + offsetGlobal.X + offsetX, axisNode.Y + offsetGlobal.Y + offsetY);
                var pt = factory.CreatePoint(new Coordinate(center.X, center.Y));

                int polygonSectionId = ResolvePolygonPositionSectionId(firstFloor.FloorNo, colNo);
                Geometry colPoly = null;
                if (col.ColumnType == 2)
                {
                    var coords = BuildCircleRing(center, Math.Max(hw, hh), col.AngleDeg, 64);
                    colPoly = factory.CreatePolygon(factory.CreateLinearRing(coords));
                }
                else if (col.ColumnType == 3 && TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out var polyPoints))
                {
                    var coords = new Coordinate[polyPoints.Length + 1];
                    for (int i = 0; i < polyPoints.Length; i++) coords[i] = new Coordinate(polyPoints[i].X, polyPoints[i].Y);
                    coords[polyPoints.Length] = coords[0];
                    colPoly = factory.CreatePolygon(factory.CreateLinearRing(coords));
                }
                else
                {
                    var rect = BuildRect(center, hw, hh, col.AngleDeg);
                    var coords = new Coordinate[5];
                    for (int i = 0; i < 4; i++) coords[i] = new Coordinate(rect[i].X, rect[i].Y);
                    coords[4] = coords[0];
                    colPoly = factory.CreatePolygon(factory.CreateLinearRing(coords));
                }

                double? temelCm = null;
                var singleFooting = _model.SingleFootings.FirstOrDefault(sf => sf.ColumnRef == 100 + colNo);
                if (singleFooting != null)
                    temelCm = singleFooting.HeightCm;
                if (!temelCm.HasValue)
                {
                    foreach (var cf in _model.ContinuousFoundations)
                    {
                        if (!_axisService.TryIntersect(cf.FixedAxisId, cf.StartAxisId, out Point2d p1) || !_axisService.TryIntersect(cf.FixedAxisId, cf.EndAxisId, out Point2d p2)) continue;
                        Vector2d along = (p2 - p1).GetNormal();
                        if (p1.GetDistanceTo(p2) <= 1e-9) continue;
                        Point2d p1Eff = p1 - along.MultiplyBy(cf.StartExtensionCm);
                        Point2d p2Eff = p2 + along.MultiplyBy(cf.EndExtensionCm);
                        int offsetForBeam = (cf.FixedAxisId >= 1001 && cf.FixedAxisId <= 1999) ? -cf.OffsetRaw : cf.OffsetRaw;
                        ComputeBeamEdgeOffsets(offsetForBeam, cf.WidthCm / 2.0, out double upperEdge, out double lowerEdge);
                        Vector2d perp = new Vector2d(-along.Y, along.X);
                        Point2d[] r = new[] { p1Eff + perp.MultiplyBy(upperEdge), p2Eff + perp.MultiplyBy(upperEdge), p2Eff + perp.MultiplyBy(lowerEdge), p1Eff + perp.MultiplyBy(lowerEdge) };
                        var coords = new Coordinate[5];
                        for (int i = 0; i < 4; i++) coords[i] = new Coordinate(r[i].X + offsetX, r[i].Y + offsetY);
                        coords[4] = coords[0];
                        var poly = factory.CreatePolygon(factory.CreateLinearRing(coords));
                        if (poly.Contains(pt)) { temelCm = cf.HeightCm; break; }
                    }
                }
                if (!temelCm.HasValue)
                {
                    foreach (var sf in _model.SlabFoundations)
                    {
                        if (!_axisService.TryIntersect(sf.AxisX1, sf.AxisY1, out Point2d p11) || !_axisService.TryIntersect(sf.AxisX1, sf.AxisY2, out Point2d p12) ||
                            !_axisService.TryIntersect(sf.AxisX2, sf.AxisY1, out Point2d p21) || !_axisService.TryIntersect(sf.AxisX2, sf.AxisY2, out Point2d p22)) continue;
                        var coords = new[] { new Coordinate(p11.X + offsetX, p11.Y + offsetY), new Coordinate(p21.X + offsetX, p21.Y + offsetY), new Coordinate(p22.X + offsetX, p22.Y + offsetY), new Coordinate(p12.X + offsetX, p12.Y + offsetY), new Coordinate(p11.X + offsetX, p11.Y + offsetY) };
                        var poly = factory.CreatePolygon(factory.CreateLinearRing(coords));
                        if (poly.Contains(pt)) { temelCm = sf.ThicknessCm; break; }
                    }
                }

                double? hatilCm = null;
                if (colPoly != null && !colPoly.IsEmpty)
                {
                    foreach (var cf in _model.ContinuousFoundations)
                    {
                        if (cf.TieBeamWidthCm <= 0) continue;
                        if (!_axisService.TryIntersect(cf.FixedAxisId, cf.StartAxisId, out Point2d p1) || !_axisService.TryIntersect(cf.FixedAxisId, cf.EndAxisId, out Point2d p2)) continue;
                        Vector2d along = (p2 - p1).GetNormal();
                        if (p1.GetDistanceTo(p2) <= 1e-9) continue;
                        ComputeTieBeamEdgeOffsets(cf.FixedAxisId, cf.TieBeamOffsetRaw, cf.TieBeamWidthCm / 2.0, out double hu, out double hl);
                        Vector2d perp = new Vector2d(-along.Y, along.X);
                        Point2d[] hatilRect = new[] { p1 + perp.MultiplyBy(hu), p2 + perp.MultiplyBy(hu), p2 + perp.MultiplyBy(hl), p1 + perp.MultiplyBy(hl) };
                        var hcoords = new Coordinate[5];
                        for (int i = 0; i < 4; i++) hcoords[i] = new Coordinate(hatilRect[i].X + offsetX, hatilRect[i].Y + offsetY);
                        hcoords[4] = hcoords[0];
                        var hatilPoly = factory.CreatePolygon(factory.CreateLinearRing(hcoords));
                        if (hatilPoly.Intersects(colPoly) && cf.HatilLabelHeightCm > 0)
                        {
                            if (!hatilCm.HasValue || cf.HatilLabelHeightCm > hatilCm.Value) hatilCm = cf.HatilLabelHeightCm;
                        }
                    }
                    foreach (var tb in _model.TieBeams)
                    {
                        if (!_axisService.TryIntersect(tb.FixedAxisId, tb.StartAxisId, out Point2d p1) || !_axisService.TryIntersect(tb.FixedAxisId, tb.EndAxisId, out Point2d p2)) continue;
                        Vector2d along = (p2 - p1).GetNormal();
                        if (p1.GetDistanceTo(p2) <= 1e-9) continue;
                        int offsetForBeam = (tb.FixedAxisId >= 1001 && tb.FixedAxisId <= 1999) ? -tb.OffsetRaw : tb.OffsetRaw;
                        ComputeBeamEdgeOffsets(offsetForBeam, tb.WidthCm / 2.0, out double upperEdge, out double lowerEdge);
                        Vector2d perp = new Vector2d(-along.Y, along.X);
                        Point2d[] rect = new[] { p1 + perp.MultiplyBy(upperEdge), p2 + perp.MultiplyBy(upperEdge), p2 + perp.MultiplyBy(lowerEdge), p1 + perp.MultiplyBy(lowerEdge) };
                        var tcoords = new Coordinate[5];
                        for (int i = 0; i < 4; i++) tcoords[i] = new Coordinate(rect[i].X + offsetX, rect[i].Y + offsetY);
                        tcoords[4] = tcoords[0];
                        var tbPoly = factory.CreatePolygon(factory.CreateLinearRing(tcoords));
                        if (tbPoly.Intersects(colPoly) && tb.HeightCm > 0)
                        {
                            if (!hatilCm.HasValue || tb.HeightCm > hatilCm.Value) hatilCm = tb.HeightCm;
                        }
                    }
                }
                result[colNo] = (temelCm, hatilCm);
            }
            return result;
        }

        /// <summary>Kolon donatı tablosu için: verilen kattaki her kolon numarası → (ColumnType, W, H). ColumnType: 1=dikdörtgen, 2=daire (W=çap), 3=poligon.</summary>
        public Dictionary<int, (int columnType, double W, double H)> GetColumnDimensionsForFloor(FloorInfo floor)
        {
            var result = new Dictionary<int, (int, double, double)>();
            if (floor == null) return result;
            foreach (var col in _model.Columns)
            {
                if (col.ColumnType == 3) { result[col.ColumnNo] = (3, 0, 0); continue; }
                int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
                if (sectionId <= 0 || !_model.ColumnDimsBySectionId.TryGetValue(sectionId, out var dim))
                {
                    result[col.ColumnNo] = (col.ColumnType, 0, 0);
                    continue;
                }
                result[col.ColumnNo] = (col.ColumnType, dim.W, dim.H);
            }
            return result;
        }

        /// <summary>Bu katta bu kolon numarası için ST4'te kesit/poligon tanımı var mı (tablo hücresi dolu mu).</summary>
        public bool HasColumnOnFloor(FloorInfo floor, ColumnAxisInfo col)
        {
            if (floor == null || col == null) return false;
            if (col.ColumnType == 3)
            {
                int ps = ResolvePolygonPositionSectionId(floor.FloorNo, col.ColumnNo);
                return ps > 0 && _model.PolygonColumnSectionByPositionSectionId.ContainsKey(ps);
            }
            int sid = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
            return sid > 0 && _model.ColumnDimsBySectionId.ContainsKey(sid);
        }

        /// <summary>Kolon donatı tablosu: alt kot cm = (ST4 kolon alt kotu m + bina taban kotu m)×100 (genel kota). ST4’te alt 0 = model zemin kotu; taban -5.18 ise -518 cm.</summary>
        public Dictionary<int, (double altKotCm, double yukseklikCm, double? kirisUstAltFarkCm)> GetColumnTableExtraData(FloorInfo floor)
        {
            var result = new Dictionary<int, (double, double, double?)>();
            if (floor == null) return result;
            double floorElevM = floor.ElevationM;
            double baseKotuM = _model.BuildingBaseKotu;
            double defaultAltKotCm = (baseKotuM + floorElevM) * 100.0;
            int floorIdx = _model.Floors.IndexOf(floor);
            double defaultYukseklikCm = 0;
            if (floorIdx >= 0 && floorIdx < _model.Floors.Count - 1)
                defaultYukseklikCm = (_model.Floors[floorIdx + 1].ElevationM - floorElevM) * 100.0;

            var factory = NtsGeometryServices.Instance.CreateGeometryFactory();
            const double ox = 0.0, oy = 0.0;

            foreach (var col in _model.Columns)
            {
                int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
                if (col.ColumnType != 3 && (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId)) && col.ColumnId > 0 && _model.ColumnDimsBySectionId.ContainsKey(col.ColumnId))
                    sectionId = col.ColumnId;
                double altKotCm = defaultAltKotCm;
                double yukseklikCm = defaultYukseklikCm;
                if (col.ColumnType == 3)
                {
                    int posId = ResolvePolygonPositionSectionId(floor.FloorNo, col.ColumnNo);
                    if (posId > 0 && _model.PolygonColumnKotMFromBinaTabaniByPositionId.TryGetValue(posId, out var pk))
                    {
                        altKotCm = (pk.altM + baseKotuM) * 100.0;
                        yukseklikCm = (pk.ustM - pk.altM) * 100.0;
                    }
                }
                else if (sectionId > 0 && _model.ColumnKotMFromBinaTabaniBySectionId.TryGetValue(sectionId, out var kotM))
                {
                    altKotCm = (kotM.altM + baseKotuM) * 100.0;
                    yukseklikCm = (kotM.ustM - kotM.altM) * 100.0;
                }

                Geometry colPoly = GetColumnPolygonForTable(floor, col, ox, oy, factory);
                double? kirisFark = null;
                if (colPoly != null && !colPoly.IsEmpty)
                {
                    var beamsOnFloor = _model.Beams.Where(b => GetBeamFloorNo(b.BeamId) == floor.FloorNo && b.IsWallFlag != 1).ToList();
                    double maxUst = double.MinValue;
                    double minAlt = double.MaxValue;
                    double floorLevelCm = (baseKotuM + floorElevM) * 100.0;
                    foreach (var beam in beamsOnFloor)
                    {
                        if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1) ||
                            !_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2))
                            continue;
                        var line = factory.CreateLineString(new[] { new Coordinate(p1.X, p1.Y), new Coordinate(p2.X, p2.Y) });
                        if (!colPoly.Intersects(line)) continue;
                        double kotUstCm = floorLevelCm + Math.Max(beam.Point1KotCm, beam.Point2KotCm);
                        double hCm = beam.HeightCm > 0 ? beam.HeightCm : 30.0;
                        double kotAltCm = kotUstCm - hCm;
                        if (kotUstCm > maxUst) maxUst = kotUstCm;
                        if (kotAltCm < minAlt) minAlt = kotAltCm;
                    }
                    if (maxUst > double.MinValue && minAlt < double.MaxValue)
                        kirisFark = maxUst - minAlt;
                }
                result[col.ColumnNo] = (altKotCm, yukseklikCm, kirisFark);
            }
            return result;
        }

        /// <summary>Kolon poligonu (tip 3 dahil), offset ile. Tablo/kiriş kesişimi için.</summary>
        private Geometry GetColumnPolygonForTable(FloorInfo floor, ColumnAxisInfo col, double offsetX, double offsetY, GeometryFactory factory)
        {
            if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) return null;
            int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
            int polygonSectionId = ResolvePolygonPositionSectionId(floor.FloorNo, col.ColumnNo);
            if (col.ColumnType != 3 && (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId)) && col.ColumnId > 0 && _model.ColumnDimsBySectionId.ContainsKey(col.ColumnId))
                sectionId = col.ColumnId;
            double hw = 0, hh = 0;
            if (sectionId > 0 && _model.ColumnDimsBySectionId.TryGetValue(sectionId, out var dim)) { hw = dim.W / 2.0; hh = dim.H / 2.0; }
            var offsetLocal = col.ColumnType == 2 ? ComputeColumnOffsetCircle(col.OffsetXRaw, col.OffsetYRaw) : ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
            var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
            var center = new Point2d(axisNode.X + offsetGlobal.X + offsetX, axisNode.Y + offsetGlobal.Y + offsetY);
            Coordinate[] coords;
            if (col.ColumnType == 3)
            {
                if (polygonSectionId <= 0 || !TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out var polyPoints))
                    return null;
                coords = new Coordinate[polyPoints.Length + 1];
                for (int i = 0; i < polyPoints.Length; i++)
                    coords[i] = new Coordinate(polyPoints[i].X, polyPoints[i].Y);
                coords[polyPoints.Length] = coords[0];
            }
            else if (col.ColumnType == 2)
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

        public void Draw(Database db, Editor ed)
        {
            _ntsDrawFactory = NtsGeometryServices.Instance.CreateGeometryFactory();
            try
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
                    var firstFloorAxisExt = GetAksSiniriEnvelope(firstFloorUnion);
                    DrawAxes(tr, btr, offsetX, offsetY, firstFloorAxisExt);
                    DrawColumns(tr, btr, firstFloor, offsetX, offsetY);
                    DrawWallsForFloor(tr, btr, firstFloor, offsetX, offsetY);
                    Geometry temelUnion = BuildTemelUnion(offsetX, offsetY, firstFloor);
                    Geometry kolonPerdeUnion = BuildKolonPerdeUnion(firstFloor, offsetX, offsetY);
                    Geometry slabUnionForLabels = BuildSlabFoundationsUnion(offsetX, offsetY);
                    DrawTemelMerged(tr, btr, offsetX, offsetY, firstFloor, temelUnion);
                    var temelHatiliRaws = new List<(Geometry geom, double widthCm, double heightDisplayCm, double kot, bool isRadyeTemelHatili)>();
                    DrawContinuousFoundations(tr, btr, offsetX, offsetY, firstFloor, drawTemelOutline: false, temelUnion, kolonPerdeUnion, temelHatiliRaws, slabUnionForLabels);
                    DrawSlabFoundations(tr, btr, offsetX, offsetY, drawTemelOutline: false);
                    DrawTieBeams(tr, btr, firstFloor, offsetX, offsetY, kolonPerdeUnion, temelHatiliRaws);
                    DrawSingleFootings(tr, btr, firstFloor, offsetX, offsetY, drawTemelOutline: false);
                    DrawPerdeLabelsForFloor(tr, btr, firstFloor, offsetX, offsetY, kolonPerdeUnion);
                    DrawFloorTitle(tr, btr, firstFloor, offsetX, offsetY, firstFloorAxisExt, isFoundationPlan: true);
                    DrawPlanSections(tr, btr, db, firstFloor, offsetX, offsetY, firstFloorAxisExt, isFoundationPlan: true, firstFloorUnion,
                        out _, out _, out _, out _);
                }

                // Benzer kat birleştirme şimdilik kapalı; açmak için GetFormworkFloorGroups() ile döngüyü gruplar üzerinden çalıştır, labelFloor = en alt kat, DrawSimilarFloorsNote ile not yaz.
                for (int floorIdx = 0; floorIdx < _model.Floors.Count; floorIdx++)
                {
                    var floor = _model.Floors[floorIdx];
                    double offsetX = (floorIdx + planStartIndex) * (floorWidth + floorGap);
                    double offsetY = 0.0;

                    Geometry elemUnion = BuildFloorElementUnion(floor);
                    var floorAxisExt = GetAksSiniriEnvelope(elemUnion);
                    DrawAxes(tr, btr, offsetX, offsetY, floorAxisExt);
                    DrawColumns(tr, btr, floor, offsetX, offsetY);
                    DrawBeamsAndWalls(tr, btr, floor, offsetX, offsetY);
                    DrawSlabs(tr, btr, floor, offsetX, offsetY);
                    DrawSlabVoids(tr, btr, elemUnion, offsetX, offsetY);
                    DrawUnifiedLayer(tr, btr, floor, offsetX, offsetY, elemUnion);
                    DrawFloorTitle(tr, btr, floor, offsetX, offsetY, floorAxisExt, isFoundationPlan: false);
                    DrawPlanSections(tr, btr, db, floor, offsetX, offsetY, floorAxisExt, isFoundationPlan: false, elemUnion,
                        out _, out _, out _, out _);
                }

                tr.Commit();

                ed.WriteMessage(
                    "\nST4PLANID: {0} kat, akslar, kolonlar (poligon dahil), kirişler, perdeler ve döşemeler{1} ID'leriyle cizildi. (cm)",
                    _model.Floors.Count,
                    hasFoundations ? string.Format(", temel plani (surekli: {0}, radye: {1}, bag kirisi: {2}, tekil: {3})", _model.ContinuousFoundations.Count, _model.SlabFoundations.Count, _model.TieBeams.Count, _model.SingleFootings.Count) : "");
            }
            }
            finally { _ntsDrawFactory = null; }
        }

        /// <summary>
        /// 1/50 kolon aplikasyon planı için tüm katlarda sadece
        /// akslar (+aks ölçüleri), kolonlar (poligon dahil) ve perdeleri çizer.
        /// </summary>
        public void DrawColumnApplicationPlan50(Database db, Editor ed, Point3d baseInsertPoint, string st4SourcePath = null)
        {
            _ntsDrawFactory = NtsGeometryServices.Instance.CreateGeometryFactory();
            _isKolon50Mode = true;
            _kolon50PerdeCopyExtent = null;
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayers(tr, db);
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    var ext = CalculateBaseExtents();
                    double floorWidth = (ext.Xmax - ext.Xmin) + 80.0;
                    double floorGap = 1000.0;
                    Geometry firstFloorUnion = _model.Floors.Count > 0 ? BuildFloorElementUnion(_model.Floors[0]) : null;
                    var firstFloorAxisExt = GetAksSiniriEnvelope(firstFloorUnion);
                    double baseDx = baseInsertPoint.X - firstFloorAxisExt.Xmin;
                    double baseDy = baseInsertPoint.Y - firstFloorAxisExt.Ymin;

                    var copyLayouts = new List<(FloorInfo floor, double offsetX, double offsetY, (double Xmin, double Xmax, double Ymin, double Ymax) floorAxisExt)>();
                    for (int floorIdx = 0; floorIdx < _model.Floors.Count; floorIdx++)
                    {
                        var floor = _model.Floors[floorIdx];
                        double offsetX = baseDx + (floorIdx * (floorWidth + floorGap));
                        double offsetY = baseDy;

                        Geometry kolonPerdeUnion = BuildKolonPerdeUnion(floor, 0.0, 0.0);
                        Geometry axisExtGeom = (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty)
                            ? kolonPerdeUnion
                            : BuildFloorElementUnion(floor);
                        var floorAxisExt = GetAksSiniriEnvelope(axisExtGeom);

                        DrawAxes(tr, btr, offsetX, offsetY, floorAxisExt);
                        DrawColumns(tr, btr, floor, offsetX, offsetY);
                        DrawWallsForFloor(tr, btr, floor, offsetX, offsetY);
                        DrawPerdeLabelsForFloor(tr, btr, floor, offsetX, offsetY, kolonPerdeUnion);
                        DrawColumnPlanDimensionsForFloor(tr, btr, db, floor, offsetX, offsetY);
                        copyLayouts.Add((floor, offsetX, offsetY, floorAxisExt));
                    }

                    DrawSimplePerdeKolonCopiesByFloorId(tr, btr, copyLayouts);

                    TryDrawKolonDonatiTableAboveKolon50PerdeCopies(tr, btr, db, ed, baseInsertPoint, st4SourcePath);

                    tr.Commit();
                    ed.WriteMessage(
                        "\nKOLON50ST4: {0} kat icin 1/50 kolon aplikasyon plani cizildi (aks, aks olculeri, kolonlar/poligon kolonlar, perdeler).",
                        _model.Floors.Count);
                }
            }
            finally
            {
                _isKolon50Mode = false;
                _kolon50PerdeCopyExtent = null;
                _ntsDrawFactory = null;
            }
        }

        private void Kolon50AccumulatePerdeCopyExtent(Geometry g)
        {
            if (!_isKolon50Mode || g == null || g.IsEmpty) return;
            Kolon50AccumulatePerdeCopyExtent(g.EnvelopeInternal);
        }

        private void Kolon50AccumulatePerdeCopyExtent(Envelope env)
        {
            if (!_isKolon50Mode || env == null) return;
            _kolon50PerdeCopyExtent = _kolon50PerdeCopyExtent == null
                ? new Envelope(env)
                : EnvelopeUtil.ExpandToInclude(_kolon50PerdeCopyExtent, env);
        }

        /// <summary>ST4 yanında .GPR varsa KOLONDATA ile aynı kolon donatı tablosunu kopya perdelerin üstüne çizer.</summary>
        private void TryDrawKolonDonatiTableAboveKolon50PerdeCopies(
            Transaction tr,
            BlockTableRecord btr,
            Database db,
            Editor ed,
            Point3d baseInsertPoint,
            string st4SourcePath)
        {
            if (!_isKolon50Mode || string.IsNullOrWhiteSpace(st4SourcePath) || _kolon50PerdeCopyExtent == null)
                return;
            string dir = Path.GetDirectoryName(st4SourcePath);
            string baseName = Path.GetFileNameWithoutExtension(st4SourcePath);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(baseName)) return;
            string gprPath = Path.Combine(dir, baseName + ".GPR");
            if (!File.Exists(gprPath)) return;

            var columnData = KolonDonatiTableDrawer.ParseKolonBetonarmeFromFile(gprPath, out string parseError);
            if (parseError != null)
            {
                ed.WriteMessage("\nKOLON50ST4: GPR kolon tablosu atlandi — {0}", parseError);
                return;
            }

            var firstFloor = _model.Floors.Count > 0 ? _model.Floors[0] : null;
            var columnFoundationHeights = firstFloor != null ? GetColumnFoundationHeights(firstFloor) : null;
            var columnDimsByFloor = new List<Dictionary<int, (int columnType, double W, double H)>>();
            var columnTableExtraByFloor = new List<Dictionary<int, (double altKotCm, double yukseklikCm, double? kirisUstAltFarkCm)>>();
            for (int i = 0; i < _model.Floors.Count; i++)
            {
                var f = _model.Floors[i];
                columnDimsByFloor.Add(GetColumnDimensionsForFloor(f));
                columnTableExtraByFloor.Add(GetColumnTableExtraData(f));
            }

            var columnActiveCells = new HashSet<(int floorIndex, int columnNo)>();
            for (int fi = 0; fi < _model.Floors.Count; fi++)
            {
                var fl = _model.Floors[fi];
                foreach (var col in _model.Columns)
                {
                    if (HasColumnOnFloor(fl, col))
                        columnActiveCells.Add((fi, col.ColumnNo));
                }
            }

            const double tableGapAboveCopiesCm = 80.0;
            double tableX = baseInsertPoint.X;
            double tableY = _kolon50PerdeCopyExtent.MaxY + tableGapAboveCopiesCm;
            var insertPoint = new Point3d(tableX, tableY, 0);

            bool ok = KolonDonatiTableDrawer.Draw(
                _model,
                columnData,
                insertPoint,
                db,
                ed,
                tr,
                btr,
                columnFoundationHeights,
                columnDimsByFloor,
                columnTableExtraByFloor,
                columnActiveCells,
                echoCompletionMessage: false);
            if (ok)
                ed.WriteMessage("\nKOLON50ST4: GPR kolon donati tablosu kopya perdelerin ustune cizildi ({0}).", Path.GetFileName(gprPath));
        }

        private void DrawSimplePerdeKolonCopiesByFloorId(
            Transaction tr,
            BlockTableRecord btr,
            List<(FloorInfo floor, double offsetX, double offsetY, (double Xmin, double Xmax, double Ymin, double Ymax) floorAxisExt)> layouts)
        {
            if (!_isKolon50Mode || layouts == null || layouts.Count == 0) return;
            var ordered = layouts.OrderBy(x => x.floor.FloorNo).ToList();
            var first = ordered[0];
            const double verticalGapCm = 1000.0;
            double firstRowTopY = first.floorAxisExt.Ymax + first.offsetY + verticalGapCm + 3000.0;
            var wallNoToX = new Dictionary<int, double>();

            int altRowIndex = 0;
            foreach (var l in ordered)
            {
                var wallItems = BuildPerdeWallItemsForCopy(l.floor, l.offsetX, l.offsetY, onlyXAxisWalls: false);
                if (wallItems == null || wallItems.Count == 0) continue;

                bool isFirstFloor = l.floor.FloorNo == first.floor.FloorNo;
                double rowTopY;
                if (isFirstFloor)
                {
                    rowTopY = firstRowTopY;
                }
                else
                {
                    altRowIndex++;
                    rowTopY = firstRowTopY - (500.0 * altRowIndex);
                }

                DrawAlignedWallGroupsAsSeparateCopiesWithAnchors(tr, btr, l.floor, wallItems, rowTopY, wallNoToX, isFirstFloor);
            }
        }

        /// <summary>
        /// Basit kopya: mevcut plandaki perdeleri ve onlara bitişik kolonları,
        /// geometriyi bozmadan (döndürmeden/gruplamadan) planın üstüne taşır.
        /// </summary>
        private void DrawSimplePerdeKolonCopyAbovePlan(
            Transaction tr,
            BlockTableRecord btr,
            FloorInfo floor,
            double offsetX,
            double offsetY,
            (double Xmin, double Xmax, double Ymin, double Ymax) floorAxisExt)
        {
            if (!_isKolon50Mode) return;
            var wallItems = BuildPerdeWallItemsForCopy(floor, offsetX, offsetY, onlyXAxisWalls: false);
            if (wallItems == null || wallItems.Count == 0) return;

            double minY = double.MaxValue;
            foreach (var w in wallItems)
            {
                if (w.wall != null && !w.wall.IsEmpty)
                    minY = Math.Min(minY, w.wall.EnvelopeInternal.MinY);
                foreach (var c in w.columns)
                {
                    if (c.geom != null && !c.geom.IsEmpty)
                        minY = Math.Min(minY, c.geom.EnvelopeInternal.MinY);
                }
            }
            if (double.IsInfinity(minY) || minY == double.MaxValue) return;

            const double verticalGapCm = 1000.0;
            double targetMinY = floorAxisExt.Ymax + offsetY + verticalGapCm;
            // Sadece son (gruplu/ayri) kopyalar cizilsin.
            DrawAlignedWallGroupsAsSeparateCopies(tr, btr, floor, wallItems, targetMinY + 3000.0);
        }

        private void DrawAlignedWallGroupsAsSeparateCopies(
            Transaction tr,
            BlockTableRecord btr,
            FloorInfo floor,
            List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> wallItems,
            double baseY)
        {
            if (wallItems == null || wallItems.Count == 0) return;
            var groups = BuildAlignedWallGroups(wallItems, 0.1); // 1 mm = 0.1 cm
            if (groups.Count == 0) return;

            int wallPad = GetLabelPadWidth(_model.Beams.Where(x => x.IsWallFlag == 1).Select(x => GetBeamNumero(x.BeamId)).DefaultIfEmpty(0).Max());
            string katEtiketi = !string.IsNullOrWhiteSpace(floor.ShortName) ? floor.ShortName : floor.FloorNo.ToString(CultureInfo.InvariantCulture);
            const double exactGapX = 340.0;
            double topLineY = baseY; // Tum bloklarin ust perde cizgisi bu hatta oturur.
            double anchorX = groups
                .SelectMany(g => g)
                .Where(i => i.wall != null && !i.wall.IsEmpty)
                .Select(i => i.wall.EnvelopeInternal.MinX)
                .DefaultIfEmpty(0.0)
                .Min();

            // Soldan saga: en kucuk perde numarasi solda.
            groups = groups
                .OrderBy(g => g.Where(i => i.beam != null).Select(i => GetBeamNumero(i.beam.BeamId)).DefaultIfEmpty(int.MaxValue).Min())
                .ToList();
            bool isFirstPlaced = false;
            double prevRightEndpointPlacedX = 0.0;

            foreach (var group in groups)
            {
                var geoms = new List<Geometry>();
                foreach (var it in group)
                {
                    if (it.wall != null && !it.wall.IsEmpty) geoms.Add(it.wall);
                    foreach (var c in it.columns)
                        if (c.geom != null && !c.geom.IsEmpty) geoms.Add(c.geom);
                }
                if (geoms.Count == 0) continue;

                Envelope rawEnv = null;
                foreach (var g in geoms) rawEnv = rawEnv == null ? new Envelope(g.EnvelopeInternal) : EnvelopeUtil.ExpandToInclude(rawEnv, g.EnvelopeInternal);
                if (rawEnv == null) continue;

                // Grup bir blok gibi dusunulup, bagli oldugu aks acisi sifira getirilsin.
                double axisAngle = GetAxisLineAngleRad(group[0].fixedAxisId);
                double cx = (rawEnv.MinX + rawEnv.MaxX) * 0.5;
                double cy = (rawEnv.MinY + rawEnv.MaxY) * 0.5;
                var rot = NetTopologySuite.Geometries.Utilities.AffineTransformation.RotationInstance(-axisAngle, cx, cy);

                var rotatedGroup = new List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)>();
                Envelope env = null;
                Envelope wallEnv = null;
                foreach (var it in group)
                {
                    Geometry rw = (it.wall != null && !it.wall.IsEmpty) ? rot.Transform(it.wall) : null;
                    var rcols = new List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)>();
                    foreach (var c in it.columns)
                    {
                        if (c.geom == null || c.geom.IsEmpty) continue;
                        var rcg = rot.Transform(c.geom);
                        if (rcg == null || rcg.IsEmpty) continue;
                        rcols.Add((rcg, c.col, c.dim, c.center, c.polygonSectionId));
                        env = env == null ? new Envelope(rcg.EnvelopeInternal) : EnvelopeUtil.ExpandToInclude(env, rcg.EnvelopeInternal);
                    }
                    if (rw != null && !rw.IsEmpty)
                    {
                        env = env == null ? new Envelope(rw.EnvelopeInternal) : EnvelopeUtil.ExpandToInclude(env, rw.EnvelopeInternal);
                        wallEnv = wallEnv == null ? new Envelope(rw.EnvelopeInternal) : EnvelopeUtil.ExpandToInclude(wallEnv, rw.EnvelopeInternal);
                    }
                    rotatedGroup.Add((rw, it.fixedAxisId, it.beam, rcols));
                }
                if (env == null || wallEnv == null) continue;

                double blockTopWallY = rotatedGroup
                    .Where(x => x.wall != null && !x.wall.IsEmpty)
                    .Select(x => x.wall.EnvelopeInternal.MaxY)
                    .DefaultIfEmpty(env.MaxY)
                    .Max();

                // Mesafe hesabı birleşmiş mevcut çizim bloğunun zarfına göre yapılır.
                double srcLeftEndpointX = env.MinX;
                double srcRightEndpointX = env.MaxX;

                // Y: ust perde cizgisi tek dogruya hizali.
                // X: birleşmiş blok sınırları arası net 340 cm.
                double targetLeftEndpointX = isFirstPlaced ? (prevRightEndpointPlacedX + exactGapX) : anchorX;
                double dx = targetLeftEndpointX - srcLeftEndpointX;
                double dy = topLineY - blockTopWallY;
                var trf = NetTopologySuite.Geometries.Utilities.AffineTransformation.TranslationInstance(dx, dy);

                foreach (var it in rotatedGroup)
                {
                    var w = trf.Transform(it.wall);
                    if (w != null && !w.IsEmpty)
                    {
                        DrawGeometryRingsAsPolylines(tr, btr, w, LayerPerde, addHatch: true, hatchAngleRad: 0.0, applySmallTriangleTrim: false);
                        int wallNumero = GetBeamNumero(it.beam.BeamId);
                        string wallNo = wallNumero.ToString("D" + wallPad, CultureInfo.InvariantCulture);
                        string wallText = string.Format(CultureInfo.InvariantCulture, "P{0}{1}", katEtiketi, wallNo);
                        var wEnv = w.EnvelopeInternal;
                        DrawBeamLabel(
                            tr,
                            btr,
                            btr.Database,
                            new Point3d((wEnv.MinX + wEnv.MaxX) * 0.5, (wEnv.MinY + wEnv.MaxY) * 0.5, 0),
                            wallText,
                            12.0,
                            0.0,
                            LayerPerdeYazisi,
                            useMiddleCenter: true);
                    }
                    foreach (var c in it.columns)
                    {
                        var cg = trf.Transform(c.geom);
                        if (cg == null || cg.IsEmpty) continue;
                        DrawGeometryRingsAsPolylines(tr, btr, cg, LayerKolon, addHatch: true, hatchAngleRad: 0.0, applySmallTriangleTrim: false);
                        var cEnv = cg.EnvelopeInternal;
                        var cCenter = new Point2d((cEnv.MinX + cEnv.MaxX) * 0.5, (cEnv.MinY + cEnv.MaxY) * 0.5);
                        Point2d labelRef = GetColumnLabelReferencePoint(cCenter, 0.0, c.col.ColumnType, c.dim.W / 2.0, c.dim.H / 2.0, c.polygonSectionId);
                        AppendColumnLabel(tr, btr, labelRef, 0.0, c.col.ColumnNo, c.col.ColumnType, c.dim, floor);
                    }
                }

                prevRightEndpointPlacedX = srcRightEndpointX + dx;
                isFirstPlaced = true;
            }
        }

        private void DrawAlignedWallGroupsAsSeparateCopiesWithAnchors(
            Transaction tr,
            BlockTableRecord btr,
            FloorInfo floor,
            List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> wallItems,
            double topLineY,
            Dictionary<int, double> wallNoToX,
            bool recordAnchors)
        {
            if (wallItems == null || wallItems.Count == 0) return;
            var groups = BuildAlignedWallGroups(wallItems, 0.1);
            if (groups.Count == 0) return;

            int wallPad = GetLabelPadWidth(_model.Beams.Where(x => x.IsWallFlag == 1).Select(x => GetBeamNumero(x.BeamId)).DefaultIfEmpty(0).Max());
            string katEtiketi = !string.IsNullOrWhiteSpace(floor.ShortName) ? floor.ShortName : floor.FloorNo.ToString(CultureInfo.InvariantCulture);
            const double exactGapX = 340.0;
            double anchorX = groups
                .SelectMany(g => g)
                .Where(i => i.wall != null && !i.wall.IsEmpty)
                .Select(i => i.wall.EnvelopeInternal.MinX)
                .DefaultIfEmpty(0.0)
                .Min();

            groups = groups
                .OrderBy(g => g.Where(i => i.beam != null).Select(i => GetBeamNumero(i.beam.BeamId)).DefaultIfEmpty(int.MaxValue).Min())
                .ToList();

            bool isFirstPlaced = false;
            double prevRightEndpointPlacedX = 0.0;
            foreach (var group in groups)
            {
                var geoms = new List<Geometry>();
                foreach (var it in group)
                {
                    if (it.wall != null && !it.wall.IsEmpty) geoms.Add(it.wall);
                    foreach (var c in it.columns)
                        if (c.geom != null && !c.geom.IsEmpty) geoms.Add(c.geom);
                }
                if (geoms.Count == 0) continue;

                Envelope rawEnv = null;
                foreach (var g in geoms) rawEnv = rawEnv == null ? new Envelope(g.EnvelopeInternal) : EnvelopeUtil.ExpandToInclude(rawEnv, g.EnvelopeInternal);
                if (rawEnv == null) continue;

                double axisAngle = GetAxisLineAngleRad(group[0].fixedAxisId);
                double cx = (rawEnv.MinX + rawEnv.MaxX) * 0.5;
                double cy = (rawEnv.MinY + rawEnv.MaxY) * 0.5;
                var rot = NetTopologySuite.Geometries.Utilities.AffineTransformation.RotationInstance(-axisAngle, cx, cy);

                var rotatedGroup = new List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)>();
                Envelope env = null;
                foreach (var it in group)
                {
                    Geometry rw = (it.wall != null && !it.wall.IsEmpty) ? rot.Transform(it.wall) : null;
                    var rcols = new List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)>();
                    foreach (var c in it.columns)
                    {
                        if (c.geom == null || c.geom.IsEmpty) continue;
                        var rcg = rot.Transform(c.geom);
                        if (rcg == null || rcg.IsEmpty) continue;
                        rcols.Add((rcg, c.col, c.dim, c.center, c.polygonSectionId));
                        env = env == null ? new Envelope(rcg.EnvelopeInternal) : EnvelopeUtil.ExpandToInclude(env, rcg.EnvelopeInternal);
                    }
                    if (rw != null && !rw.IsEmpty)
                        env = env == null ? new Envelope(rw.EnvelopeInternal) : EnvelopeUtil.ExpandToInclude(env, rw.EnvelopeInternal);
                    rotatedGroup.Add((rw, it.fixedAxisId, it.beam, rcols));
                }
                if (env == null) continue;

                double blockTopWallY = rotatedGroup
                    .Where(x => x.wall != null && !x.wall.IsEmpty)
                    .Select(x => x.wall.EnvelopeInternal.MaxY)
                    .DefaultIfEmpty(env.MaxY)
                    .Max();
                double blockBottomWallY = rotatedGroup
                    .Where(x => x.wall != null && !x.wall.IsEmpty)
                    .Select(x => x.wall.EnvelopeInternal.MinY)
                    .DefaultIfEmpty(env.MinY)
                    .Min();

                double srcLeftX = env.MinX;
                double srcRightX = env.MaxX;

                int minNo = group.Where(x => x.beam != null).Select(x => GetBeamNumero(x.beam.BeamId)).DefaultIfEmpty(0).Min();
                double targetLeftX = isFirstPlaced ? (prevRightEndpointPlacedX + exactGapX) : anchorX;
                bool foundAnchor = false;
                // Ayni perde numarasi farkli katta her zaman ayni X'te olsun.
                foreach (var it in rotatedGroup)
                {
                    if (it.beam == null || it.wall == null || it.wall.IsEmpty) continue;
                    int no = GetBeamNumero(it.beam.BeamId);
                    if (!wallNoToX.TryGetValue(no, out double mapX)) continue;
                    double thisLeft = it.wall.EnvelopeInternal.MinX;
                    targetLeftX = mapX - (thisLeft - srcLeftX);
                    foundAnchor = true;
                    break;
                }
                if (!foundAnchor && wallNoToX.TryGetValue(minNo, out double mappedLeftX))
                    targetLeftX = mappedLeftX;
                double dx = targetLeftX - srcLeftX;
                double dy = topLineY - blockTopWallY;
                var trf = NetTopologySuite.Geometries.Utilities.AffineTransformation.TranslationInstance(dx, dy);
                double topCutY = blockTopWallY + dy + 40.0;
                double bottomCutY = blockBottomWallY + dy - 40.0;
                double placedWallBottomY = blockBottomWallY + dy;
                var clippedColumnGeoms = new List<Geometry>();

                foreach (var it in rotatedGroup)
                {
                    var w = trf.Transform(it.wall);
                    if (w != null && !w.IsEmpty)
                    {
                        DrawGeometryRingsAsPolylines(tr, btr, w, LayerPerde, addHatch: true, hatchAngleRad: 0.0, applySmallTriangleTrim: false);
                        Kolon50AccumulatePerdeCopyExtent(w);
                        int wallNumero = GetBeamNumero(it.beam.BeamId);
                        string wallNo = wallNumero.ToString("D" + wallPad, CultureInfo.InvariantCulture);
                        string wallText = string.Format(CultureInfo.InvariantCulture, "P{0}{1}", katEtiketi, wallNo);
                        var wEnv = w.EnvelopeInternal;
                        DrawBeamLabel(tr, btr, btr.Database, new Point3d((wEnv.MinX + wEnv.MaxX) * 0.5, (wEnv.MinY + wEnv.MaxY) * 0.5, 0), wallText, 12.0, 0.0, LayerPerdeYazisi, useMiddleCenter: true);
                    }
                    foreach (var c in it.columns)
                    {
                        var cg = trf.Transform(c.geom);
                        if (cg == null || cg.IsEmpty) continue;
                        var clipped = ClipGeometryOutsideYBand(cg, bottomCutY, topCutY);
                        if (clipped == null || clipped.IsEmpty) continue;
                        clippedColumnGeoms.Add(clipped);
                        DrawGeometryRingsAsPolylines(tr, btr, clipped, LayerKolon, addHatch: true, hatchAngleRad: 0.0, applySmallTriangleTrim: false);
                        Kolon50AccumulatePerdeCopyExtent(clipped);
                        var cEnv = clipped.EnvelopeInternal;
                        AppendColumnLabelCenteredBelowWallBottom(tr, btr, cEnv.MaxX, placedWallBottomY, c.col.ColumnNo, c.col.ColumnType, c.dim, floor);
                    }
                }

                Geometry colUnion = null;
                if (clippedColumnGeoms.Count == 1) colUnion = clippedColumnGeoms[0];
                else if (clippedColumnGeoms.Count > 1)
                {
                    try { colUnion = CascadedPolygonUnion.Union(clippedColumnGeoms); } catch { colUnion = clippedColumnGeoms[0]; }
                }
                if (colUnion != null && !colUnion.IsEmpty)
                {
                    DrawHorizontalSectionBoundaryOnColumns(tr, btr, topCutY, colUnion, srcLeftX + dx - 50.0, srcRightX + dx + 50.0);
                    DrawHorizontalSectionBoundaryOnColumns(tr, btr, bottomCutY, colUnion, srcLeftX + dx - 50.0, srcRightX + dx + 50.0);
                    if (_isKolon50Mode)
                    {
                        var u = colUnion.EnvelopeInternal;
                        Kolon50AccumulatePerdeCopyExtent(new Envelope(u.MinX, u.MaxX, topCutY, topCutY));
                        Kolon50AccumulatePerdeCopyExtent(new Envelope(u.MinX, u.MaxX, bottomCutY, bottomCutY));
                    }
                }

                if (recordAnchors)
                {
                    foreach (var it in rotatedGroup)
                    {
                        if (it.beam == null || it.wall == null || it.wall.IsEmpty) continue;
                        int no = GetBeamNumero(it.beam.BeamId);
                        if (wallNoToX.ContainsKey(no)) continue;
                        wallNoToX[no] = it.wall.EnvelopeInternal.MinX + dx;
                    }
                }

                prevRightEndpointPlacedX = srcRightX + dx;
                isFirstPlaced = true;
            }
        }

        private Geometry ClipGeometryOutsideYBand(Geometry geom, double minY, double maxY)
        {
            if (geom == null || geom.IsEmpty) return geom;
            try
            {
                var env = geom.EnvelopeInternal;
                double minX = env.MinX - 10000.0;
                double maxX = env.MaxX + 10000.0;
                double extMaxY = env.MaxY + 10000.0;
                double extMinY = env.MinY - 10000.0;
                if (maxY <= minY + 1e-6) return geom;

                var upperHalf = _ntsDrawFactory.CreatePolygon(_ntsDrawFactory.CreateLinearRing(new[]
                {
                    new Coordinate(minX, maxY),
                    new Coordinate(maxX, maxY),
                    new Coordinate(maxX, extMaxY),
                    new Coordinate(minX, extMaxY),
                    new Coordinate(minX, maxY)
                }));

                var lowerHalf = _ntsDrawFactory.CreatePolygon(_ntsDrawFactory.CreateLinearRing(new[]
                {
                    new Coordinate(minX, extMinY),
                    new Coordinate(maxX, extMinY),
                    new Coordinate(maxX, minY),
                    new Coordinate(minX, minY),
                    new Coordinate(minX, extMinY)
                }));

                var diffTop = geom.Difference(upperHalf);
                if (diffTop == null || diffTop.IsEmpty) return diffTop;
                var diffBand = diffTop.Difference(lowerHalf);
                var kept = (diffBand != null && !diffBand.IsEmpty) ? diffBand : diffTop;

                // Kesit siniri hattiyla birebir cakisan kolon kenarlarini dusur.
                const double edgeEps = 0.01; // 0.1 mm
                var topStrip = _ntsDrawFactory.CreatePolygon(_ntsDrawFactory.CreateLinearRing(new[]
                {
                    new Coordinate(minX, maxY - edgeEps),
                    new Coordinate(maxX, maxY - edgeEps),
                    new Coordinate(maxX, maxY + edgeEps),
                    new Coordinate(minX, maxY + edgeEps),
                    new Coordinate(minX, maxY - edgeEps)
                }));
                var botStrip = _ntsDrawFactory.CreatePolygon(_ntsDrawFactory.CreateLinearRing(new[]
                {
                    new Coordinate(minX, minY - edgeEps),
                    new Coordinate(maxX, minY - edgeEps),
                    new Coordinate(maxX, minY + edgeEps),
                    new Coordinate(minX, minY + edgeEps),
                    new Coordinate(minX, minY - edgeEps)
                }));
                var noTopEdge = kept.Difference(topStrip);
                if (noTopEdge == null || noTopEdge.IsEmpty) return noTopEdge;
                var noBothEdges = noTopEdge.Difference(botStrip);
                return (noBothEdges != null && !noBothEdges.IsEmpty) ? noBothEdges : noTopEdge;
            }
            catch
            {
                return geom;
            }
        }

        private void DrawHorizontalSectionBoundaryOnColumns(Transaction tr, BlockTableRecord btr, double y, Geometry columnUnion, double xMin, double xMax)
        {
            if (columnUnion == null || columnUnion.IsEmpty || xMax <= xMin) return;
            try
            {
                var line = _ntsDrawFactory.CreateLineString(new[] { new Coordinate(xMin, y), new Coordinate(xMax, y) });
                var inter = line.Intersection(columnUnion);
                if (inter == null || inter.IsEmpty) return;

                if (inter is LineString ls)
                {
                    DrawLineStringSegment(tr, btr, ls);
                    return;
                }
                if (inter is MultiLineString mls)
                {
                    for (int i = 0; i < mls.NumGeometries; i++)
                        DrawLineStringSegment(tr, btr, mls.GetGeometryN(i) as LineString);
                    return;
                }
                if (inter is GeometryCollection gc)
                {
                    for (int i = 0; i < gc.NumGeometries; i++)
                    {
                        var gi = gc.GetGeometryN(i);
                        if (gi is LineString gls) DrawLineStringSegment(tr, btr, gls);
                        else if (gi is MultiLineString gmls)
                        {
                            for (int j = 0; j < gmls.NumGeometries; j++)
                                DrawLineStringSegment(tr, btr, gmls.GetGeometryN(j) as LineString);
                        }
                    }
                }
            }
            catch { }
        }

        private void DrawLineStringSegment(Transaction tr, BlockTableRecord btr, LineString ls)
        {
            if (ls == null || ls.IsEmpty || ls.NumPoints < 2) return;
            var s = ls.GetCoordinateN(0);
            var e = ls.GetCoordinateN(ls.NumPoints - 1);
            if (s == null || e == null) return;
            if (Math.Abs(s.X - e.X) < 1e-6 && Math.Abs(s.Y - e.Y) < 1e-6) return;
            AppendEntity(tr, btr, new Line(new Point3d(s.X, s.Y, 0), new Point3d(e.X, e.Y, 0)) { Layer = LayerKesitSiniri });
        }

        private List<List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)>> BuildAlignedWallGroups(
            List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> items,
            double tolCm)
        {
            var result = new List<List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)>>();
            foreach (var axisBucket in items.GroupBy(i => i.fixedAxisId))
            {
                var list = axisBucket.ToList();
                var bands = new List<(double key, List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> members)>();
                foreach (var it in list)
                {
                    double coord = GetWallAlignmentCoordinate(it);
                    bool added = false;
                    for (int i = 0; i < bands.Count; i++)
                    {
                        if (Math.Abs(bands[i].key - coord) <= tolCm)
                        {
                            bands[i].members.Add(it);
                            added = true;
                            break;
                        }
                    }
                    if (!added)
                        bands.Add((coord, new List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> { it }));
                }
                foreach (var b in bands)
                {
                    foreach (var comp in SplitAlignedBandByContiguity(b.members))
                        result.Add(comp);
                }
            }
            return result;
        }

        private List<List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)>> SplitAlignedBandByContiguity(
            List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> members)
        {
            var res = new List<List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)>>();
            if (members == null || members.Count == 0) return res;
            var visited = new bool[members.Count];

            for (int i = 0; i < members.Count; i++)
            {
                if (visited[i]) continue;
                visited[i] = true;
                var q = new Queue<int>();
                q.Enqueue(i);
                var comp = new List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)>();

                while (q.Count > 0)
                {
                    int k = q.Dequeue();
                    comp.Add(members[k]);
                    for (int j = 0; j < members.Count; j++)
                    {
                        if (visited[j]) continue;
                        bool connected =
                            AreItemsContiguousOnPlan(members[k], members[j]) ||
                            AreWallsConnectedForCopyGrouping(members[k].wall, members[j].wall) ||
                            AreWallsBeamConnectedOnAxis(members[k].beam, members[j].beam) ||
                            ShareEndpointColumnByAxis(members[k].beam, members[j].beam);
                        if (!connected) continue;
                        visited[j] = true;
                        q.Enqueue(j);
                    }
                }
                res.Add(comp);
            }

            return res;
        }

        private double GetWallAlignmentCoordinate(
            (Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns) item)
        {
            if (item.wall == null || item.wall.IsEmpty) return 0.0;
            var env = item.wall.EnvelopeInternal;
            var c = new Point2d((env.MinX + env.MaxX) * 0.5, (env.MinY + env.MaxY) * 0.5);
            var axis = _model.AxisX.Concat(_model.AxisY).FirstOrDefault(a => a.Id == item.fixedAxisId);
            if (axis == null) return 0.0;
            Vector2d d = axis.Kind == AxisKind.X ? new Vector2d(axis.Slope, 1.0) : new Vector2d(-1.0, axis.Slope);
            if (d.Length <= 1e-9) d = Vector2d.XAxis;
            d = d.GetNormal();
            Vector2d n = new Vector2d(-d.Y, d.X).GetNormal();
            return (c.X * n.X) + (c.Y * n.Y);
        }

        private void DrawPerdeKolonCopiesStackedByFloors(
            Transaction tr,
            BlockTableRecord btr,
            List<(FloorInfo floor, double offsetX, double offsetY, (double Xmin, double Xmax, double Ymin, double Ymax) axisExt)> layouts)
        {
            if (!_isKolon50Mode || layouts == null || layouts.Count == 0) return;
            const double groupGapX = 340.0;
            const double rowGapY = 340.0;

            double topY = layouts.Max(x => x.axisExt.Ymax + x.offsetY) + 1000.0;
            double baseX = layouts.Min(x => x.axisExt.Xmin + x.offsetX);
            var wallNoToX = new Dictionary<int, double>();
            var seenWallBeamIds = new HashSet<int>();
            var groupedEntries = new List<(int floorIndexNo, FloorInfo labelFloor, string katEtiketi, int wallPad,
                List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> group)>();

            // Ayni kat indisi birden fazla FloorInfo satiriyla geliyorsa kopya uretimini tek temsille yap.
            foreach (var l in layouts.GroupBy(x => x.floor.FloorNo).Select(g => g.First()))
            {
                var wallItems = BuildPerdeWallItemsForCopy(l.floor, l.offsetX, l.offsetY);
                if (wallItems.Count == 0) continue;
                var groups = BuildPerdeCopyGroups(wallItems);
                if (groups.Count == 0) continue;
                int wallPad = GetLabelPadWidth(wallItems.Max(w => GetBeamNumero(w.beam.BeamId)));
                string katEtiketi = !string.IsNullOrWhiteSpace(l.floor.ShortName) ? l.floor.ShortName : l.floor.FloorNo.ToString(CultureInfo.InvariantCulture);
                foreach (var g in groups)
                {
                    if (g == null || g.Count == 0) continue;
                    var filteredGroup = g.Where(x => x.beam != null && seenWallBeamIds.Add(x.beam.BeamId)).ToList();
                    if (filteredGroup.Count == 0) continue;
                    int floorIndexNo = l.floor.FloorNo > 0 ? l.floor.FloorNo : 1;
                    groupedEntries.Add((floorIndexNo, l.floor, katEtiketi, wallPad, filteredGroup));
                }
            }

            var rowBuckets = groupedEntries
                .GroupBy(e => e.floorIndexNo)
                .OrderBy(g => g.Key)
                .Select(g => g.ToList())
                .ToList();

            for (int row = 0; row < rowBuckets.Count; row++)
            {
                var bucket = rowBuckets[row];
                if (bucket.Count == 0) continue;
                var mergedBucket = MergeRowGroupEntries(bucket);
                var groups = mergedBucket.Select(x => x.group).ToList();

                double rowTopY = topY - row * (rowGapY + groups.Max(g => g.Where(i => i.wall != null && !i.wall.IsEmpty).Select(i => i.wall.EnvelopeInternal.Height).DefaultIfEmpty(0.0).Max()));
                double cursorX = baseX;

                foreach (var entry in mergedBucket)
                {
                    var group = entry.group;
                    if (group.Count == 0) continue;
                    int fixedAxisId = group[0].fixedAxisId;
                    double axisAngle = GetAxisLineAngleRad(fixedAxisId);
                    var rotatedWalls = new List<(Geometry geom, BeamInfo beam)>();
                    var rotatedCols = new List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, int polygonSectionId)>();
                    var wallNos = new HashSet<int>();
                    Envelope groupEnv = null;
                    Envelope wallOnlyEnv = null;

                    foreach (var item in group)
                    {
                        if (item.wall == null || item.wall.IsEmpty) continue;
                        wallNos.Add(GetBeamNumero(item.beam.BeamId));
                        var env0 = item.wall.EnvelopeInternal;
                        double cx = (env0.MinX + env0.MaxX) * 0.5;
                        double cy = (env0.MinY + env0.MaxY) * 0.5;
                        var rot = NetTopologySuite.Geometries.Utilities.AffineTransformation.RotationInstance(-axisAngle, cx, cy);
                        var wallRot = rot.Transform(item.wall);
                        if (wallRot != null && !wallRot.IsEmpty)
                        {
                            rotatedWalls.Add((wallRot, item.beam));
                            groupEnv = groupEnv == null ? new Envelope(wallRot.EnvelopeInternal) : EnvelopeUtil.ExpandToInclude(groupEnv, wallRot.EnvelopeInternal);
                            wallOnlyEnv = wallOnlyEnv == null ? new Envelope(wallRot.EnvelopeInternal) : EnvelopeUtil.ExpandToInclude(wallOnlyEnv, wallRot.EnvelopeInternal);
                        }
                        foreach (var col in item.columns)
                        {
                            var colRot = rot.Transform(col.geom);
                            if (colRot == null || colRot.IsEmpty) continue;
                            rotatedCols.Add((colRot, col.col, col.dim, col.polygonSectionId));
                            groupEnv = groupEnv == null ? new Envelope(colRot.EnvelopeInternal) : EnvelopeUtil.ExpandToInclude(groupEnv, colRot.EnvelopeInternal);
                        }
                    }
                    if (groupEnv == null) continue;

                    double targetX = cursorX;
                    if (row == 0)
                    {
                        foreach (int no in wallNos) if (!wallNoToX.ContainsKey(no)) wallNoToX[no] = targetX;
                    }
                    else
                    {
                        var mapped = wallNos.Where(no => wallNoToX.ContainsKey(no)).Select(no => wallNoToX[no]).DefaultIfEmpty(double.NaN).First();
                        if (!double.IsNaN(mapped)) targetX = mapped;
                    }

                    double dx = targetX - groupEnv.MinX;
                    // Ayni kattaki tum perde gruplari, perde ust cizgisine gore tek hatta hizalansin.
                    double alignTopY = wallOnlyEnv != null ? wallOnlyEnv.MaxY : groupEnv.MaxY;
                    double dy = rowTopY - alignTopY;
                    var trf = NetTopologySuite.Geometries.Utilities.AffineTransformation.TranslationInstance(dx, dy);

                    foreach (var rw in rotatedWalls)
                    {
                        var wallMoved = trf.Transform(rw.geom);
                        if (wallMoved == null || wallMoved.IsEmpty) continue;
                        DrawGeometryRingsAsPolylines(tr, btr, wallMoved, LayerPerde, addHatch: true, hatchAngleRad: 0.0, applySmallTriangleTrim: false);
                        int wallNumero = GetBeamNumero(rw.beam.BeamId);
                        string wallNo = wallNumero.ToString("D" + entry.wallPad, CultureInfo.InvariantCulture);
                        string wallText = string.Format(CultureInfo.InvariantCulture, "P{0}{1}", entry.katEtiketi, wallNo);
                        var movedEnv = wallMoved.EnvelopeInternal;
                        double tx = (movedEnv.MinX + movedEnv.MaxX) * 0.5;
                        double ty = (movedEnv.MinY + movedEnv.MaxY) * 0.5;
                        DrawBeamLabel(tr, btr, btr.Database, new Point3d(tx, ty, 0), wallText, 12.0, 0.0, LayerPerdeYazisi, useMiddleCenter: true);
                    }
                    foreach (var rc in rotatedCols)
                    {
                        var colMoved = trf.Transform(rc.geom);
                        if (colMoved == null || colMoved.IsEmpty) continue;
                        DrawGeometryRingsAsPolylines(tr, btr, colMoved, LayerKolon, addHatch: true, hatchAngleRad: 0.0, applySmallTriangleTrim: false);
                        var cEnv = colMoved.EnvelopeInternal;
                        var cCenter = new Point2d((cEnv.MinX + cEnv.MaxX) * 0.5, (cEnv.MinY + cEnv.MaxY) * 0.5);
                        Point2d labelRef = GetColumnLabelReferencePoint(cCenter, 0.0, rc.col.ColumnType, rc.dim.W / 2.0, rc.dim.H / 2.0, rc.polygonSectionId);
                        AppendColumnLabel(tr, btr, labelRef, 0.0, rc.col.ColumnNo, rc.col.ColumnType, rc.dim, entry.labelFloor);
                    }

                    cursorX = Math.Max(cursorX, targetX + groupEnv.Width + groupGapX);
                }
            }
        }

        private List<(FloorInfo labelFloor, string katEtiketi, int wallPad,
            List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> group)>
            MergeRowGroupEntries(List<(int floorIndexNo, FloorInfo labelFloor, string katEtiketi, int wallPad,
                List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> group)> bucket)
        {
            var result = new List<(FloorInfo, string, int, List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)>)>();
            if (bucket == null || bucket.Count == 0) return result;

            var visited = new bool[bucket.Count];
            for (int i = 0; i < bucket.Count; i++)
            {
                if (visited[i]) continue;
                visited[i] = true;
                var q = new Queue<int>();
                q.Enqueue(i);

                var mergedGroup = new List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)>();
                FloorInfo labelFloor = bucket[i].labelFloor;
                string katEtiketi = bucket[i].katEtiketi;
                int wallPad = bucket[i].wallPad;

                while (q.Count > 0)
                {
                    int k = q.Dequeue();
                    mergedGroup.AddRange(bucket[k].group);
                    if (bucket[k].wallPad > wallPad) wallPad = bucket[k].wallPad;

                    for (int j = 0; j < bucket.Count; j++)
                    {
                        if (visited[j]) continue;
                        if (!ArePerdeGroupsConnected(bucket[k].group, bucket[j].group)) continue;
                        visited[j] = true;
                        q.Enqueue(j);
                    }
                }

                result.Add((labelFloor, katEtiketi, wallPad, mergedGroup));
            }

            return result;
        }

        private bool ArePerdeGroupsConnected(
            List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> a,
            List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> b)
        {
            if (a == null || b == null || a.Count == 0 || b.Count == 0) return false;
            foreach (var wa in a)
            {
                var colsA = new HashSet<int>(wa.columns.Select(c => c.col.ColumnNo));
                foreach (var wb in b)
                {
                    if (wa.fixedAxisId != wb.fixedAxisId) continue;
                    // Kullanici kurali: X aksina fixli ayni fixed aks perdeleri birlikte ciz.
                    if (wa.fixedAxisId >= 1001 && wa.fixedAxisId <= 1999) return true;
                    bool sharedColumn = wb.columns.Any(c => colsA.Contains(c.col.ColumnNo));
                    bool sharedEndpointColumn = ShareEndpointColumnByAxis(wa.beam, wb.beam);
                    bool wallConnected = AreWallsConnectedForCopyGrouping(wa.wall, wb.wall);
                    bool beamNodeConnected = AreWallsBeamConnectedOnAxis(wa.beam, wb.beam);
                    bool beamSpanConnected = AreWallsBeamSpanConnectedOnPlan(wa.beam, wb.beam);
                    bool planContiguous = AreItemsContiguousOnPlan(wa, wb);
                    if (sharedColumn || sharedEndpointColumn || wallConnected || beamNodeConnected || beamSpanConnected || planContiguous) return true;
                }
            }
            return false;
        }

        private List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> BuildPerdeWallItemsForCopy(
            FloorInfo floor, double offsetX, double offsetY, bool onlyXAxisWalls = false)
        {
            var factory = _ntsDrawFactory;
            var beams = MergeSameIdBeamsOnFloor(floor.FloorNo);
            var wallItems = new List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)>();
            if (beams == null || beams.Count == 0) return wallItems;
            Geometry kolonUnion = BuildKolonUnionSameFloorOnly(floor, offsetX, offsetY);
            foreach (var beam in beams)
            {
                if (beam.IsWallFlag != 1) continue;
                if (onlyXAxisWalls && !(beam.FixedAxisId >= 1001 && beam.FixedAxisId <= 1999)) continue;
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1) || !_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2)) continue;
                var a = new Point2d(p1.X + offsetX, p1.Y + offsetY);
                var b = new Point2d(p2.X + offsetX, p2.Y + offsetY);
                NormalizeBeamDirection(beam.FixedAxisId, ref a, ref b);
                Vector2d dir = b - a; if (dir.Length <= 1e-9) continue;
                Vector2d u = dir.GetNormal(); Vector2d perp = new Vector2d(-u.Y, u.X);
                double hw = beam.WidthCm / 2.0;
                ComputeBeamEdgeOffsets(beam.OffsetRaw, hw, out double upperEdge, out double lowerEdge);
                Point2d q1 = a + perp.MultiplyBy(upperEdge), q2 = b + perp.MultiplyBy(upperEdge), q3 = b + perp.MultiplyBy(lowerEdge), q4 = a + perp.MultiplyBy(lowerEdge);
                var wallPoly = factory.CreatePolygon(factory.CreateLinearRing(new[] { new Coordinate(q1.X, q1.Y), new Coordinate(q2.X, q2.Y), new Coordinate(q3.X, q3.Y), new Coordinate(q4.X, q4.Y), new Coordinate(q1.X, q1.Y) }));
                if (wallPoly == null || wallPoly.IsEmpty) continue;
                Geometry wallToDraw = wallPoly;
                if (kolonUnion != null && !kolonUnion.IsEmpty)
                {
                    var diff = wallPoly.Difference(kolonUnion);
                    if (diff != null && !diff.IsEmpty) { wallToDraw = ReducePrecisionSafe(diff, 100); if (wallToDraw == null || wallToDraw.IsEmpty) wallToDraw = diff; }
                }
                var cols = new List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)>();
                foreach (var col in _model.Columns)
                {
                    if (!HasColumnOnFloor(floor, col)) continue;
                    var colGeom = GetColumnPolygonForTable(floor, col, offsetX, offsetY, factory);
                    if (colGeom == null || colGeom.IsEmpty) continue;
                    bool includeCol = false;
                    try
                    {
                        includeCol = wallPoly.Intersects(colGeom) || wallPoly.Distance(colGeom) <= 0.1; // 1 mm tolerans (cm biriminde 0.1)
                    }
                    catch
                    {
                        includeCol = wallPoly.Intersects(colGeom);
                    }
                    if (!includeCol) continue;
                    int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
                    int polygonSectionId = ResolvePolygonPositionSectionId(floor.FloorNo, col.ColumnNo);
                    var dim = sectionId > 0 && _model.ColumnDimsBySectionId.ContainsKey(sectionId) ? _model.ColumnDimsBySectionId[sectionId] : (W: 40.0, H: 40.0);
                    Point2d centerPt = new Point2d((colGeom.EnvelopeInternal.MinX + colGeom.EnvelopeInternal.MaxX) * 0.5, (colGeom.EnvelopeInternal.MinY + colGeom.EnvelopeInternal.MaxY) * 0.5);
                    cols.Add((colGeom, col, dim, centerPt, polygonSectionId));
                }
                wallItems.Add((wallToDraw, beam.FixedAxisId, beam, cols));
            }
            return wallItems;
        }

        /// <summary>
        /// KOLON50ST4: Aynı aksa tanımlı perde parçalarını, kesişen kolonlarıyla birlikte
        /// planın üstüne yan yana kopya olarak yerleştirir.
        /// </summary>
        private void DrawPerdeKolonCopiesAbovePlan(
            Transaction tr,
            BlockTableRecord btr,
            FloorInfo floor,
            double offsetX,
            double offsetY,
            (double Xmin, double Xmax, double Ymin, double Ymax) floorAxisExt)
        {
            if (!_isKolon50Mode) return;
            var factory = _ntsDrawFactory;
            var beams = MergeSameIdBeamsOnFloor(floor.FloorNo);
            if (beams == null || beams.Count == 0) return;
            Geometry kolonUnion = BuildKolonUnionSameFloorOnly(floor, offsetX, offsetY);
            var wallItems = new List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)>();
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
                var wallCoords = new[]
                {
                    new Coordinate(q1.X, q1.Y),
                    new Coordinate(q2.X, q2.Y),
                    new Coordinate(q3.X, q3.Y),
                    new Coordinate(q4.X, q4.Y),
                    new Coordinate(q1.X, q1.Y)
                };
                var wallPoly = factory.CreatePolygon(factory.CreateLinearRing(wallCoords));
                if (wallPoly == null || wallPoly.IsEmpty) continue;
                Geometry wallToDraw = wallPoly;
                if (kolonUnion != null && !kolonUnion.IsEmpty)
                {
                    var diff = wallPoly.Difference(kolonUnion);
                    if (diff != null && !diff.IsEmpty)
                    {
                        wallToDraw = ReducePrecisionSafe(diff, 100);
                        if (wallToDraw == null || wallToDraw.IsEmpty) wallToDraw = diff;
                    }
                }

                var cols = new List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)>();
                foreach (var col in _model.Columns)
                {
                    if (!HasColumnOnFloor(floor, col)) continue;
                    var colGeom = GetColumnPolygonForTable(floor, col, offsetX, offsetY, factory);
                    if (colGeom == null || colGeom.IsEmpty) continue;
                    if (!wallPoly.Intersects(colGeom)) continue;

                    int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
                    int polygonSectionId = ResolvePolygonPositionSectionId(floor.FloorNo, col.ColumnNo);
                    var dim = sectionId > 0 && _model.ColumnDimsBySectionId.ContainsKey(sectionId)
                        ? _model.ColumnDimsBySectionId[sectionId]
                        : (W: 40.0, H: 40.0);
                    Point2d centerPt = new Point2d((colGeom.EnvelopeInternal.MinX + colGeom.EnvelopeInternal.MaxX) * 0.5, (colGeom.EnvelopeInternal.MinY + colGeom.EnvelopeInternal.MaxY) * 0.5);
                    cols.Add((colGeom, col, dim, centerPt, polygonSectionId));
                }

                wallItems.Add((wallToDraw, beam.FixedAxisId, beam, cols));
            }

            if (wallItems.Count == 0) return;

            double placeTopY = floorAxisExt.Ymax + offsetY + 1000.0;
            double cursorX = floorAxisExt.Xmin + offsetX;
            const double copyGapCm = 340.0;
            int wallPad = GetLabelPadWidth(wallItems.Max(w => GetBeamNumero(w.beam.BeamId)));
            string katEtiketi = !string.IsNullOrWhiteSpace(floor.ShortName) ? floor.ShortName : floor.FloorNo.ToString(CultureInfo.InvariantCulture);

            foreach (var group in BuildPerdeCopyGroups(wallItems))
            {
                if (group.Count == 0) continue;
                int fixedAxisId = group[0].fixedAxisId;
                double axisAngle = GetAxisLineAngleRad(fixedAxisId);
                var rotatedWalls = new List<(Geometry geom, BeamInfo beam)>();
                var rotatedCols = new List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, int polygonSectionId)>();
                Envelope groupEnv = null;

                foreach (var item in group)
                {
                    if (item.wall == null || item.wall.IsEmpty) continue;
                    var env0 = item.wall.EnvelopeInternal;
                    double cx = (env0.MinX + env0.MaxX) * 0.5;
                    double cy = (env0.MinY + env0.MaxY) * 0.5;
                    var rot = NetTopologySuite.Geometries.Utilities.AffineTransformation.RotationInstance(-axisAngle, cx, cy);
                    var wallRot = rot.Transform(item.wall);
                    if (wallRot != null && !wallRot.IsEmpty)
                    {
                        rotatedWalls.Add((wallRot, item.beam));
                        groupEnv = groupEnv == null ? new Envelope(wallRot.EnvelopeInternal) : EnvelopeUtil.ExpandToInclude(groupEnv, wallRot.EnvelopeInternal);
                    }

                    foreach (var col in item.columns)
                    {
                        var colRot = rot.Transform(col.geom);
                        if (colRot == null || colRot.IsEmpty) continue;
                        rotatedCols.Add((colRot, col.col, col.dim, col.polygonSectionId));
                        groupEnv = groupEnv == null ? new Envelope(colRot.EnvelopeInternal) : EnvelopeUtil.ExpandToInclude(groupEnv, colRot.EnvelopeInternal);
                    }
                }

                if (groupEnv == null) continue;
                double dx = cursorX - groupEnv.MinX;
                double dy = placeTopY - groupEnv.MaxY;
                var trf = NetTopologySuite.Geometries.Utilities.AffineTransformation.TranslationInstance(dx, dy);

                foreach (var rw in rotatedWalls)
                {
                    var wallMoved = trf.Transform(rw.geom);
                    if (wallMoved == null || wallMoved.IsEmpty) continue;
                    DrawGeometryRingsAsPolylines(tr, btr, wallMoved, LayerPerde, addHatch: true, hatchAngleRad: 0.0, applySmallTriangleTrim: false);
                    int wallNumero = GetBeamNumero(rw.beam.BeamId);
                    string wallNo = wallNumero.ToString("D" + wallPad, CultureInfo.InvariantCulture);
                    string wallText = string.Format(CultureInfo.InvariantCulture, "P{0}{1}", katEtiketi, wallNo);
                    var movedEnv = wallMoved.EnvelopeInternal;
                    double tx = movedEnv.MaxX + 8.0;
                    double ty = movedEnv.MinY - 8.0;
                    DrawBeamLabel(tr, btr, btr.Database, new Point3d(tx, ty, 0), wallText, 12.0, 0.0, LayerPerdeYazisi, bottomLeftAligned: true);
                }

                foreach (var rc in rotatedCols)
                {
                    var colMoved = trf.Transform(rc.geom);
                    if (colMoved == null || colMoved.IsEmpty) continue;
                    DrawGeometryRingsAsPolylines(tr, btr, colMoved, LayerKolon, addHatch: true, hatchAngleRad: 0.0, applySmallTriangleTrim: false);

                    var cEnv = colMoved.EnvelopeInternal;
                    var cCenter = new Point2d((cEnv.MinX + cEnv.MaxX) * 0.5, (cEnv.MinY + cEnv.MaxY) * 0.5);
                    Point2d labelRef = GetColumnLabelReferencePoint(cCenter, 0.0, rc.col.ColumnType, rc.dim.W / 2.0, rc.dim.H / 2.0, rc.polygonSectionId);
                    AppendColumnLabel(tr, btr, labelRef, 0.0, rc.col.ColumnNo, rc.col.ColumnType, rc.dim, floor);
                }

                cursorX += groupEnv.Width + copyGapCm;
            }
        }

        private static class EnvelopeUtil
        {
            public static Envelope ExpandToInclude(Envelope current, Envelope add)
            {
                if (current == null) return add == null ? null : new Envelope(add);
                if (add == null) return current;
                current.ExpandToInclude(add);
                return current;
            }
        }

        private List<List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)>> BuildPerdeCopyGroups(
            List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> items)
        {
            var groups = new List<List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)>>();
            if (items == null || items.Count == 0) return groups;

            foreach (var axisBucket in items.GroupBy(i => i.fixedAxisId))
            {
                var list = axisBucket.ToList();
                var visited = new bool[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    if (visited[i]) continue;
                    var g = new List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)>();
                    var q = new Queue<int>();
                    q.Enqueue(i);
                    visited[i] = true;
                    while (q.Count > 0)
                    {
                        int k = q.Dequeue();
                        g.Add(list[k]);
                        var colsK = new HashSet<int>(list[k].columns.Select(c => c.col.ColumnNo));
                        for (int j = 0; j < list.Count; j++)
                        {
                            if (visited[j]) continue;
                            bool sharedColumn = list[j].columns.Any(c => colsK.Contains(c.col.ColumnNo));
                            bool sharedEndpointColumn = ShareEndpointColumnByAxis(list[k].beam, list[j].beam);
                            bool wallConnected = AreWallsConnectedForCopyGrouping(list[k].wall, list[j].wall);
                            bool beamNodeConnected = AreWallsBeamConnectedOnAxis(list[k].beam, list[j].beam);
                            bool beamSpanConnected = AreWallsBeamSpanConnectedOnPlan(list[k].beam, list[j].beam);
                            bool planContiguous = AreItemsContiguousOnPlan(list[k], list[j]);
                            if (!sharedColumn && !sharedEndpointColumn && !wallConnected && !beamNodeConnected && !beamSpanConnected && !planContiguous) continue;
                            visited[j] = true;
                            q.Enqueue(j);
                        }
                    }
                    groups.Add(g);
                }
            }

            return groups;
        }

        private static bool AreWallsConnectedForCopyGrouping(Geometry a, Geometry b)
        {
            if (a == null || b == null || a.IsEmpty || b.IsEmpty) return false;
            try
            {
                if (a.Intersects(b) || a.Touches(b) || b.Touches(a))
                    return true;
                // Aynı aks üzerinde çok küçük boşlukla kopuk gelen perdeleri tek grup kabul et.
                return a.Distance(b) <= 2.0; // cm
            }
            catch
            {
                return false;
            }
        }

        private static bool AreWallsBeamConnectedOnAxis(BeamInfo a, BeamInfo b)
        {
            if (a == null || b == null) return false;
            if (a.FixedAxisId != b.FixedAxisId) return false;
            return a.StartAxisId == b.StartAxisId
                || a.StartAxisId == b.EndAxisId
                || a.EndAxisId == b.StartAxisId
                || a.EndAxisId == b.EndAxisId;
        }

        private bool AreWallsBeamSpanConnectedOnPlan(BeamInfo a, BeamInfo b)
        {
            if (a == null || b == null) return false;
            if (a.FixedAxisId != b.FixedAxisId) return false;
            if (!_axisService.TryIntersect(a.FixedAxisId, a.StartAxisId, out Point2d a1) ||
                !_axisService.TryIntersect(a.FixedAxisId, a.EndAxisId, out Point2d a2) ||
                !_axisService.TryIntersect(b.FixedAxisId, b.StartAxisId, out Point2d b1) ||
                !_axisService.TryIntersect(b.FixedAxisId, b.EndAxisId, out Point2d b2))
                return false;

            Vector2d u = (a2 - a1);
            if (u.Length <= 1e-9) return false;
            u = u.GetNormal();
            double aMin = Math.Min(a1.X * u.X + a1.Y * u.Y, a2.X * u.X + a2.Y * u.Y);
            double aMax = Math.Max(a1.X * u.X + a1.Y * u.Y, a2.X * u.X + a2.Y * u.Y);
            double bMin = Math.Min(b1.X * u.X + b1.Y * u.Y, b2.X * u.X + b2.Y * u.Y);
            double bMax = Math.Max(b1.X * u.X + b1.Y * u.Y, b2.X * u.X + b2.Y * u.Y);

            // Ayni aks uzerinde araliklar temas/ortusuyorsa (kolonla bitis dahil) bagli say.
            const double tol = 1e-3;
            double gap = Math.Max(bMin - aMax, aMin - bMax);
            return gap <= tol;
        }

        private bool ShareEndpointColumnByAxis(BeamInfo a, BeamInfo b)
        {
            if (a == null || b == null) return false;
            if (a.FixedAxisId != b.FixedAxisId) return false;
            var ca = GetEndpointColumnNosForBeam(a);
            var cb = GetEndpointColumnNosForBeam(b);
            if (ca.Count == 0 || cb.Count == 0) return false;
            return ca.Overlaps(cb);
        }

        private HashSet<int> GetEndpointColumnNosForBeam(BeamInfo beam)
        {
            var set = new HashSet<int>();
            if (beam == null) return set;
            bool fixedIsX = beam.FixedAxisId >= 1001 && beam.FixedAxisId <= 1999;
            bool fixedIsY = beam.FixedAxisId >= 2001 && beam.FixedAxisId <= 2999;
            if (!fixedIsX && !fixedIsY) return set;

            foreach (var c in _model.Columns)
            {
                if (fixedIsX)
                {
                    if (c.AxisXId != beam.FixedAxisId) continue;
                    if (c.AxisYId == beam.StartAxisId || c.AxisYId == beam.EndAxisId)
                        set.Add(c.ColumnNo);
                }
                else
                {
                    if (c.AxisYId != beam.FixedAxisId) continue;
                    if (c.AxisXId == beam.StartAxisId || c.AxisXId == beam.EndAxisId)
                        set.Add(c.ColumnNo);
                }
            }
            return set;
        }

        private bool AreItemsContiguousOnPlan(
            (Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns) a,
            (Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns) b)
        {
            if (a.beam == null || b.beam == null) return false;
            if (a.fixedAxisId != b.fixedAxisId) return false;

            // Aynı doğrultu: mevcut plan görünümünde açı farkı çok küçük olmalı.
            double angA = GetAxisLineAngleRad(a.fixedAxisId);
            double angB = GetAxisLineAngleRad(b.fixedAxisId);
            if (Math.Abs(NormalizeAngleRad(angA - angB)) > (Math.PI / 180.0)) return false; // 1 deg

            Geometry ga = BuildGroupingCompositeGeometry(a);
            Geometry gb = BuildGroupingCompositeGeometry(b);
            if (ga == null || gb == null || ga.IsEmpty || gb.IsEmpty) return false;
            try
            {
                if (ga.Intersects(gb) || ga.Touches(gb) || gb.Touches(ga)) return true;
                return ga.Distance(gb) <= 1.0; // 1 cm ve alti bosluk "bosluksuz" kabul edilir
            }
            catch
            {
                return false;
            }
        }

        private static double NormalizeAngleRad(double a)
        {
            while (a > Math.PI) a -= (2.0 * Math.PI);
            while (a <= -Math.PI) a += (2.0 * Math.PI);
            return a;
        }

        private static Geometry BuildGroupingCompositeGeometry(
            (Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns) item)
        {
            if (item.wall == null || item.wall.IsEmpty) return null;
            var geoms = new List<Geometry> { item.wall };
            if (item.columns != null)
            {
                foreach (var c in item.columns)
                {
                    if (c.geom != null && !c.geom.IsEmpty) geoms.Add(c.geom);
                }
            }
            if (geoms.Count == 1) return geoms[0];
            try { return CascadedPolygonUnion.Union(geoms); } catch { return geoms[0]; }
        }

        /// <summary>
        /// Sadece temel planını (ilk kat) ve ona ait plan kesitlerini çizer.
        /// 1/50 projeler için TEMEL50ST4 komutundan çağrılır.
        /// </summary>
        public void DrawFoundationPlanWithSections(Database db, Editor ed, Point3d baseInsertPoint)
        {
            _ntsDrawFactory = NtsGeometryServices.Instance.CreateGeometryFactory();
            _isTemel50Mode = true;
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayers(tr, db);
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    bool hasFoundations = _model.ContinuousFoundations.Count > 0 || _model.SlabFoundations.Count > 0 || _model.TieBeams.Count > 0 || _model.SingleFootings.Count > 0;
                    if (!hasFoundations || _model.Floors.Count == 0)
                    {
                        ed.WriteMessage("\nTEMEL50ST4: Cizilecek temel verisi bulunamadi.");
                        return;
                    }

                    var firstFloor = _model.Floors[0];
                    Geometry firstFloorUnion = BuildFloorElementUnion(firstFloor);
                    var firstFloorAxisExt = GetAksSiniriEnvelope(firstFloorUnion);
                    double offsetX = baseInsertPoint.X - firstFloorAxisExt.Xmin;
                    double offsetY = baseInsertPoint.Y - firstFloorAxisExt.Ymin;
                    DrawAxes(tr, btr, offsetX, offsetY, firstFloorAxisExt);
                    DrawColumns(tr, btr, firstFloor, offsetX, offsetY);
                    DrawWallsForFloor(tr, btr, firstFloor, offsetX, offsetY);
                    Geometry temelUnion = BuildTemelUnion(offsetX, offsetY, firstFloor);
                    Geometry kolonPerdeUnion = BuildKolonPerdeUnion(firstFloor, offsetX, offsetY);
                    Geometry slabUnionForLabels = BuildSlabFoundationsUnion(offsetX, offsetY);
                    DrawTemelMerged(tr, btr, offsetX, offsetY, firstFloor, temelUnion);
                    var temelHatiliRaws = new List<(Geometry geom, double widthCm, double heightDisplayCm, double kot, bool isRadyeTemelHatili)>();
                    DrawContinuousFoundations(tr, btr, offsetX, offsetY, firstFloor, drawTemelOutline: false, temelUnion, kolonPerdeUnion, temelHatiliRaws, slabUnionForLabels);
                    DrawSlabFoundations(tr, btr, offsetX, offsetY, drawTemelOutline: false);
                    DrawTieBeams(tr, btr, firstFloor, offsetX, offsetY, kolonPerdeUnion, temelHatiliRaws);
                    DrawSingleFootings(tr, btr, firstFloor, offsetX, offsetY, drawTemelOutline: false);
                    DrawPerdeLabelsForFloor(tr, btr, firstFloor, offsetX, offsetY, kolonPerdeUnion);
                    DrawFloorTitle(tr, btr, firstFloor, offsetX, offsetY, firstFloorAxisExt, isFoundationPlan: true);
                    DrawPlanSections(tr, btr, db, firstFloor, offsetX, offsetY, firstFloorAxisExt, isFoundationPlan: true, firstFloorUnion,
                        out double antetLayMinX, out double antetLayMaxX, out double antetLayMinY, out double antetLayMaxY);
                    GetSectionCutBalloonExtents(offsetX, offsetY, firstFloorAxisExt,
                        out _, out _, out double yBottomBalloonTemel, out _,
                        out _, out _, out _, out _);
                    double temelBaslikYTop = yBottomBalloonTemel - Temel50BaslikAltAksBalonBoslukCm;
                    antetLayMinY = Math.Min(antetLayMinY, temelBaslikYTop - 30.0 - 100.0);
                    TryDrawTemelAntetFromDxf(tr, btr, antetLayMinX, antetLayMinY, antetLayMaxX, antetLayMaxY, ed);

                    tr.Commit();

                    ed.WriteMessage(
                        "\nTEMEL50ST4: Temel plani cizildi (surekli: {0}, radye: {1}, bag kirisi: {2}, tekil: {3}) ve kesitler olusturuldu.",
                        _model.ContinuousFoundations.Count,
                        _model.SlabFoundations.Count,
                        _model.TieBeams.Count,
                        _model.SingleFootings.Count);
                }
            }
            finally
            {
                _isTemel50Mode = false;
                _ntsDrawFactory = null;
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
        private const string LayerBirlesikKatman = "BIRLESIK KATMAN (BEYKENT)";
        private const string LayerOlcu = "OLCU (BEYKENT)";
        private const string LayerAksOlcu = "AKS OLCU (BEYKENT)";
        private const string LayerKirisYazisi = "KIRIS ISMI (BEYKENT)";
        private const string LayerPerdeYazisi = "PERDE ISMI (BEYKENT)";
        private const string LayerKolonIsmi = "KOLON ISMI (BEYKENT)";
        private const string LayerDosemeIsmi = "DOSEME ISMI (BEYKENT)";
        private const string LayerDosemePafta = "DOSEME PAFTA (BEYKENT)";
        private const string LayerYukYazisi = "YUK YAZISI (BEYKENT)";
        private const string LayerKotYazi = "KOT YAZI (BEYKENT)";
        private const string LayerKotCizgisi = "KOT CIZGISI (BEYKENT)";
        private const string LayerTemelHatiliIsmi = "TEMEL HATILI ISMI (BEYKENT)";
        private const string LayerTemelIsmi = "TEMEL ISMI (BEYKENT)";
        private const string LayerKesit = "KESIT (BEYKENT)";
        /// <summary>Kesit şemasında birleşik gövde; KESİT SINIRI dikdörtgeni ile kırpılır.</summary>
        private const string LayerKesitCizgisi = "KESIT CIZGISI (BEYKENT)";
        private const string LayerKesitIsmi = "KESIT ISMI (BEYKENT)";
        private const string LayerKesitSiniri = "KESIT SINIRI (BEYKENT)";
        /// <summary>Temel kesitlerinde zemin altı grobeton şeridi (kapalı polyline).</summary>
        private const string LayerGrobeton = "GROBETON (BEYKENT)";
        /// <summary>Grobeton kesit taraması: AutoCAD önceden tanımlı desen adı.</summary>
        private const string GrobetonHatchPatternName = "AR-CONC";
        private const double GrobetonHatchPatternScale = 0.05;
        /// <summary>Temel parçaları arası BLOKAJ şeridi AR-CONC ölçeği (alt grobeton 0,05; blokaj 0,1).</summary>
        private const double BlokajGapStripArConcHatchScale = 0.1;
        /// <summary>Blokaj altı zemin dolgusu: önceden tanımlı desen.</summary>
        private const string BlokajEarthHatchPatternName = "EARTH";
        private const double BlokajEarthHatchScale = 2.0;
        private const double BlokajEarthHatchAngleDeg = 45.0;
        /// <summary>Temel kesitinde ara boşluk altı blokaj dikdörtgeni.</summary>
        private const string LayerBlokaj = "BLOKAJ (BEYKENT)";
        /// <summary>BLOKAJ katmanı şeffaflık (AutoCAD 0=opak … 90=en şeffaf).</summary>
        private const int LayerBlokajTransparencyPercent = 90;
        /// <summary>Temel kesitlerinde grobeton altı zemin temas çizgisi (A-A: alt kenar; B-B: dış yüzey).</summary>
        private const string LayerZemin = "ZEMIN (BEYKENT)";
        /// <summary>DASHED katmanlarda (AKS, KESIT SINIRI) entity çizgi tipi ölçeği; LTSCALE=1 iken kesikli okunaklı olsun.</summary>
        private const double DashedLayerEntityLinetypeScale = 25.0;
        private const string YaziBeykentTextStyleName = "YAZI (BEYKENT)";
        /// <summary>Kiriş etiket çizim boyutları (resimdeki gibi): 70cm x 14cm referans (13 karakter). Genişlik = RefWidth * metin uzunluğu / RefCharCount.</summary>
        private const double BeamLabelRefWidthCm = 70.0;
        private const double BeamLabelRefHeightCm = 12.0;
        private const int BeamLabelRefCharCount = 13;
        private const string AksOlcuDimStyleName = "AKS_OLCU";
        private const string PlanOlcuDimStyleName = "PLAN_OLCU";
        /// <summary>Symbols and Arrows → Arrow size = 3 (cm). <see cref="DimStyleTableRecord.Dimasz"/>; First/Second tik için <see cref="DimStyleTableRecord.Dimtsz"/>.</summary>
        private const double OlcuDimArrowTickSizeCm = 3.0;

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
            EnsurePlanLayer(tr, db, LayerTemelHatiliIsmi, 243, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, LayerTemelIsmi, 40, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, LayerKatSiniri, 41, LineWeight.LineWeight025, useDashed: false);
            EnsurePlanLayer(tr, db, LayerKalipBosluk, 30, LineWeight.LineWeight025, useDashed: false);
            SetPlanLayerLinetypeContinuous(tr, db, LayerKalipBosluk);
            EnsurePlanLayer(tr, db, LayerBirlesikKatman, 8, LineWeight.LineWeight025, useDashed: false);
            EnsurePlanLayer(tr, db, LayerOlcu, 14, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, LayerAksOlcu, 6, LineWeight.LineWeight018, useDashed: false);
            EnsurePlanLayer(tr, db, LayerKirisYazisi, 40, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, LayerPerdeYazisi, 240, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, LayerKolonIsmi, 91, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, LayerDosemeIsmi, 9, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, LayerDosemePafta, 3, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, LayerYukYazisi, 140, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, LayerKotYazi, 7, LineWeight.LineWeight020, useDashed: false);
            // Kesit/plan kot işaretleri: cyan (klasik kot çizgisi).
            EnsurePlanLayer(tr, db, LayerKotCizgisi, 7, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, "KIRIS UZATMA ISARET (BEYKENT)", 1, LineWeight.LineWeight025, useDashed: false);
            EnsurePlanLayer(tr, db, "KIRIS UZATMA ISARET MAVI (BEYKENT)", 5, LineWeight.LineWeight025, useDashed: false);
            EnsurePlanLayer(tr, db, LayerKesit, 151, LineWeight.LineWeight060, useDashed: false);
            EnsurePlanLayer(tr, db, LayerKesitCizgisi, 3, LineWeight.LineWeight050, useDashed: false);
            EnsurePlanLayer(tr, db, LayerKesitIsmi, 6, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, LayerKesitSiniri, 241, LineWeight.LineWeight020, useDashed: true);
            EnsurePlanLayer(tr, db, LayerGrobeton, 70, LineWeight.LineWeight050, useDashed: false);
            EnsurePlanLayer(tr, db, LayerZemin, 43, LineWeight.LineWeight050, useDashed: false);
            EnsurePlanLayer(tr, db, LayerBlokaj, 8, LineWeight.LineWeight015, useDashed: false, layerTransparencyPercent: LayerBlokajTransparencyPercent);
        }

        private static void SetPlanLayerLinetypeContinuous(Transaction tr, Database db, string layerName)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName)) return;
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (!ltt.Has("Continuous")) return;
            var rec = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
            rec.LinetypeObjectId = ltt["Continuous"];
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

        private static void EnsurePlanLayer(Transaction tr, Database db, string layerName, int colorIndex, LineWeight lineWeight, bool useDashed = false, int? layerTransparencyPercent = null)
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
            {
                rec.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorIndex);
                rec.LineWeight = lineWeight;
            }
            if (layerTransparencyPercent.HasValue)
            {
                int tp = layerTransparencyPercent.Value;
                if (tp < 0) tp = 0;
                if (tp > 90) tp = 90;
                rec.Transparency = new Transparency((byte)tp);
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

        /// <summary>Kalıp planına çizilen kat sınırı (eleman birleşimi) zarfını model koordinatlarında döndürür. Boşsa CalculateBaseExtents fallback.</summary>
        private (double Xmin, double Xmax, double Ymin, double Ymax) GetKatSiniriEnvelope(Geometry elementUnion)
        {
            if (elementUnion == null || elementUnion.IsEmpty) return CalculateBaseExtents();
            var env = elementUnion.EnvelopeInternal;
            return (env.MinX, env.MaxX, env.MinY, env.MaxY);
        }

        /// <summary>Eğimsiz kolon akslarının en sol/sağ/alt/üst kesişim noktalarıyla oluşan aks sınırı dikdörtgeninin zarfı (model koordinatları). Kat sınırı varsa stretch uygulanır. Aks çizgileri bu zarfın 200 cm dışında biter. Hesaplanamazsa kat sınırı zarfı veya fallback döner.</summary>
        private (double Xmin, double Xmax, double Ymin, double Ymax) GetAksSiniriEnvelope(Geometry elementUnion)
        {
            var xKolonAks = BuildColumnAxisIds(c => c.AxisXId, _model.AxisX.Select(a => a.Id));
            var yKolonAks = BuildColumnAxisIds(c => c.AxisYId, _model.AxisY.Select(a => a.Id));
            int? leftXId = null, rightXId = null;
            double leftX = double.MaxValue, rightX = double.MinValue;
            foreach (var ax in _model.AxisX)
            {
                if (!xKolonAks.Contains(ax.Id) || Math.Abs(ax.Slope) > 1e-9) continue;
                if (ax.ValueCm < leftX) { leftX = ax.ValueCm; leftXId = ax.Id; }
                if (ax.ValueCm > rightX) { rightX = ax.ValueCm; rightXId = ax.Id; }
            }
            int? bottomYId = null, topYId = null;
            double bottomY = double.MinValue, topY = double.MaxValue;
            foreach (var ay in _model.AxisY)
            {
                if (!yKolonAks.Contains(ay.Id) || Math.Abs(ay.Slope) > 1e-9) continue;
                if (ay.ValueCm > bottomY) { bottomY = ay.ValueCm; bottomYId = ay.Id; }
                if (ay.ValueCm < topY) { topY = ay.ValueCm; topYId = ay.Id; }
            }
            if (!leftXId.HasValue || !rightXId.HasValue || !bottomYId.HasValue || !topYId.HasValue)
                return elementUnion != null && !elementUnion.IsEmpty ? GetKatSiniriEnvelope(elementUnion) : CalculateBaseExtents();
            if (!_axisService.TryIntersect(leftXId.Value, bottomYId.Value, out Point2d pLB) ||
                !_axisService.TryIntersect(rightXId.Value, bottomYId.Value, out Point2d pRB) ||
                !_axisService.TryIntersect(rightXId.Value, topYId.Value, out Point2d pRT) ||
                !_axisService.TryIntersect(leftXId.Value, topYId.Value, out Point2d pLT))
                return elementUnion != null && !elementUnion.IsEmpty ? GetKatSiniriEnvelope(elementUnion) : CalculateBaseExtents();
            double xLB = pLB.X, yLB = pLB.Y;
            double xRB = pRB.X, yRB = pRB.Y;
            double xRT = pRT.X, yRT = pRT.Y;
            double xLT = pLT.X, yLT = pLT.Y;
            if (elementUnion != null && !elementUnion.IsEmpty)
            {
                try
                {
                    var env = elementUnion.EnvelopeInternal;
                    double kMinX = env.MinX, kMaxX = env.MaxX, kMinY = env.MinY, kMaxY = env.MaxY;
                    double aLeft = Math.Min(xLB, xLT);
                    double aRight = Math.Max(xRB, xRT);
                    double aBottom = Math.Min(yLB, yRB);
                    double aTop = Math.Max(yLT, yRT);
                    if (aTop < kMaxY) { yLT = kMaxY; yRT = kMaxY; }
                    if (aBottom > kMinY) { yLB = kMinY; yRB = kMinY; }
                    if (aLeft > kMinX) { xLB = kMinX; xLT = kMinX; }
                    if (aRight < kMaxX) { xRB = kMaxX; xRT = kMaxX; }
                }
                catch { }
            }
            double xmin = Math.Min(Math.Min(xLB, xRB), Math.Min(xRT, xLT));
            double xmax = Math.Max(Math.Max(xLB, xRB), Math.Max(xRT, xLT));
            double ymin = Math.Min(Math.Min(yLB, yRB), Math.Min(yRT, yLT));
            double ymax = Math.Max(Math.Max(yLB, yRB), Math.Max(yRT, yLT));
            return (xmin, xmax, ymin, ymax);
        }

        /// <summary>Koordinatlar datadaki şekliyle kullanılır; aks yuvarlaması yok.</summary>
        private static Coordinate C1(double x, double y) => new Coordinate(x, y);

        /// <summary>Katta çizilen elemanların (kolon, kiriş, perde, döşeme) birleşimi; model koordinatları (offset 0). Resimdeki gibi yapıyı takip eden dış sınır için kullanılır. Koordinatlar 1 cm yuvarlanarak non-noded intersection önlenir.</summary>
        private Geometry BuildFloorElementUnion(FloorInfo floor)
        {
            var factory = _ntsDrawFactory;
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

        /// <summary>Çizimde görüldüğü şekilde tüm poligonları (kolon, kiriş, perde, döşeme, kalıp boşluk; aks ve kat sınırı hariç) birleştirip BIRLESIK KATMAN (BEYKENT) katmanında ilave çizer.</summary>
        private void DrawUnifiedLayer(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY, Geometry elementUnion)
        {
            var factory = _ntsDrawFactory;
            var allPolygons = new List<Geometry>();

            Geometry kolonUnion = BuildKolonUnionSameFloorOnly(floor, offsetX, offsetY);
            if (kolonUnion != null && !kolonUnion.IsEmpty)
                AddPolygonsToList(kolonUnion, allPolygons);

            if (_drawnBeamGeometriesForSlabCut != null)
                foreach (var g in _drawnBeamGeometriesForSlabCut)
                    if (g != null && !g.IsEmpty) AddPolygonsToList(g, allPolygons);

            if (_drawnWallGeometriesForSlabCut != null)
                foreach (var g in _drawnWallGeometriesForSlabCut)
                    if (g != null && !g.IsEmpty) AddPolygonsToList(g, allPolygons);

            if (_drawnSlabGeometriesForUnion != null)
                foreach (var g in _drawnSlabGeometriesForUnion)
                    if (g != null && !g.IsEmpty) AddPolygonsToList(g, allPolygons);

            if (elementUnion != null && !elementUnion.IsEmpty)
            {
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
                        if (mp.GetGeometryN(i) is Polygon p)
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
                    if (coords == null || coords.Length < 3) continue;
                    var ringCoords = new Coordinate[coords.Length];
                    for (int i = 0; i < coords.Length; i++)
                        ringCoords[i] = new Coordinate(coords[i].X + offsetX, coords[i].Y + offsetY);
                    if (ringCoords.Length > 1 && (ringCoords[0].X != ringCoords[ringCoords.Length - 1].X || ringCoords[0].Y != ringCoords[ringCoords.Length - 1].Y))
                    {
                        var closed = new Coordinate[ringCoords.Length + 1];
                        Array.Copy(ringCoords, closed, ringCoords.Length);
                        closed[ringCoords.Length] = closed[0];
                        ringCoords = closed;
                    }
                    try
                    {
                        var ring = factory.CreateLinearRing(ringCoords);
                        if (ring != null && ring.IsValid)
                            allPolygons.Add(factory.CreatePolygon(ring));
                    }
                    catch { }
                }
            }

            if (allPolygons.Count == 0) return;
            Geometry unionResult = allPolygons.Count == 1 ? allPolygons[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(allPolygons);
            if (unionResult != null && !unionResult.IsEmpty)
                DrawGeometryRingsAsPolylines(tr, btr, unionResult, LayerKatSiniri, addHatch: false, applySmallTriangleTrim: false, vertexAngleTolDeg: 0.3, minVertexDistCm: 0.1, collinearTolCm: 0.05);
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

        /// <summary>Kat sınırı (çizim sınırı) dışına aksların taşabileceği mesafe (cm) — aks çizgisi bu kadar uzakta biter.</summary>
        private const double AxisExtensionBeyondBoundaryCm = 200.0;

        private void DrawAxes(Transaction tr, BlockTableRecord btr, double offsetX, double offsetY,
            (double Xmin, double Xmax, double Ymin, double Ymax) ext)
        {
            Database db = btr.Database;
            const double axisBalonRadiusCm = 20.0;   // çap 40 cm
            const double axisLabelHeightCm = 20.0;

            const double dimOffsetFromBalonCm = 55.0;  // ölçü çizgisi aks balonundan 55 cm (içeri doğru)
            const double dimRowGapCm = 20.0;           // iki aks ölçü sırası arasında 20 cm
            const double dimTextHeightCm = 12.0;

            double extCm = AxisExtensionBeyondBoundaryCm;
            double xLo = ext.Xmin + offsetX - extCm;
            double xHi = ext.Xmax + offsetX + extCm;
            double yLo = ext.Ymin + offsetY - extCm;
            double yHi = ext.Ymax + offsetY + extCm;

            var xKolonAks = BuildColumnAxisIds(c => c.AxisXId, _model.AxisX.Select(a => a.Id));
            var yKolonAks = BuildColumnAxisIds(c => c.AxisYId, _model.AxisY.Select(a => a.Id));

            var xAxisTopPositions = new List<Point3d>();
            var xAxisBottomPositions = new List<Point3d>();
            var yAxisLeftPositions = new List<Point3d>();
            var yAxisRightPositions = new List<Point3d>();

            // Bütün akslar her zaman gösterilir (kolon filtresi yok)
            for (int i = 0; i < _model.AxisX.Count; i++)
            {
                var ax = _model.AxisX[i];
                if (!xKolonAks.Contains(ax.Id)) continue;
                double yBot = ext.Ymin + offsetY - extCm;
                double yTop = ext.Ymax + offsetY + extCm;
                double xBot = offsetX + ax.ValueCm + ax.Slope * (ext.Ymin - extCm);
                double xTop = offsetX + ax.ValueCm + ax.Slope * (ext.Ymax + extCm);
                var p1 = new Point3d(xBot, yBot, 0);
                var p2 = new Point3d(xTop, yTop, 0);
                if (!ClipSegmentToRectangle(p1, p2, xLo, xHi, yLo, yHi, out Point3d q1, out Point3d q2)) continue;
                AppendEntity(tr, btr, new Line(q1, q2) { Layer = LayerAks });
                bool inclinedX = Math.Abs(ax.Slope) > 1e-9;
                if (!inclinedX)
                {
                    string xLabel = (_model.GprAxisXLabelByRow.TryGetValue(i + 1, out var gx) && !string.IsNullOrWhiteSpace(gx))
                        ? gx.Trim()
                        : (i + 1).ToString(CultureInfo.InvariantCulture);
                    Point3d centerTop = AxisBalonCenterAtEnd(q2, q1, axisBalonRadiusCm);
                    Point3d centerBot = AxisBalonCenterAtEnd(q1, q2, axisBalonRadiusCm);
                    xAxisTopPositions.Add(centerTop);
                    xAxisBottomPositions.Add(centerBot);
                    var circleTop = new Circle(centerTop, Vector3d.ZAxis, axisBalonRadiusCm) { Layer = LayerAksBalonu };
                    AppendEntity(tr, btr, circleTop);
                    AppendEntity(tr, btr, MakeCenteredAxisLabelText(tr, db, axisLabelHeightCm, xLabel, centerTop));
                    var circleBot = new Circle(centerBot, Vector3d.ZAxis, axisBalonRadiusCm) { Layer = LayerAksBalonu };
                    AppendEntity(tr, btr, circleBot);
                    AppendEntity(tr, btr, MakeCenteredAxisLabelText(tr, db, axisLabelHeightCm, xLabel, centerBot));
                }
            }

            for (int j = 0; j < _model.AxisY.Count; j++)
            {
                var ay = _model.AxisY[j];
                if (!yKolonAks.Contains(ay.Id)) continue;
                double xLeft = ext.Xmin + offsetX - extCm;
                double xRight = ext.Xmax + offsetX + extCm;
                double yLeft = -(ay.ValueCm + ay.Slope * (ext.Xmin - extCm)) + offsetY;
                double yRight = -(ay.ValueCm + ay.Slope * (ext.Xmax + extCm)) + offsetY;
                var p1 = new Point3d(xLeft, yLeft, 0);
                var p2 = new Point3d(xRight, yRight, 0);
                if (!ClipSegmentToRectangle(p1, p2, xLo, xHi, yLo, yHi, out Point3d q1, out Point3d q2)) continue;
                AppendEntity(tr, btr, new Line(q1, q2) { Layer = LayerAks });
                bool inclinedY = Math.Abs(ay.Slope) > 1e-9;
                if (!inclinedY)
                {
                    string yLabel = (_model.GprAxisYLabelByRow.TryGetValue(j + 1, out var gy) && !string.IsNullOrWhiteSpace(gy))
                        ? gy.Trim()
                        : (j < 26 ? ((char)('A' + j)).ToString() : "A" + (j - 25).ToString(CultureInfo.InvariantCulture));
                    Point3d centerRight = AxisBalonCenterAtEnd(q2, q1, axisBalonRadiusCm);
                    Point3d centerLeft = AxisBalonCenterAtEnd(q1, q2, axisBalonRadiusCm);
                    yAxisLeftPositions.Add(centerLeft);
                    yAxisRightPositions.Add(centerRight);
                    var circleRight = new Circle(centerRight, Vector3d.ZAxis, axisBalonRadiusCm) { Layer = LayerAksBalonu };
                    AppendEntity(tr, btr, circleRight);
                    AppendEntity(tr, btr, MakeCenteredAxisLabelText(tr, db, axisLabelHeightCm, yLabel, centerRight));
                    var circleLeft = new Circle(centerLeft, Vector3d.ZAxis, axisBalonRadiusCm) { Layer = LayerAksBalonu };
                    AppendEntity(tr, btr, circleLeft);
                    AppendEntity(tr, btr, MakeCenteredAxisLabelText(tr, db, axisLabelHeightCm, yLabel, centerLeft));
                }
            }

            ObjectId aksOlcuDimStyleId = GetOrCreateAksOlcuDimStyle(tr, db, dimTextHeightCm);
            if (xAxisTopPositions.Count >= 2)
                DrawAxisDimensionsXFourSides(tr, btr, xAxisTopPositions, xAxisBottomPositions, dimOffsetFromBalonCm, dimRowGapCm, aksOlcuDimStyleId);
            if (yAxisLeftPositions.Count >= 2)
                DrawAxisDimensionsYFourSides(tr, btr, yAxisLeftPositions, yAxisRightPositions, dimOffsetFromBalonCm, dimRowGapCm, aksOlcuDimStyleId);
        }

        /// <summary>Kesit çizgisini aks balon merkezlerine uzatmak için sınır X/Y (world cm) + balon merkez listeleri.</summary>
        private void GetSectionCutBalloonExtents(double offsetX, double offsetY, (double Xmin, double Xmax, double Ymin, double Ymax) ext,
            out double xLeftBalloon, out double xRightBalloon, out double yBottomBalloon, out double yTopBalloon,
            out List<Point3d> xBotBalloons, out List<Point3d> xTopBalloons, out List<Point3d> yLeftBalloons, out List<Point3d> yRightBalloons)
        {
            const double axisBalonRadiusCm = 20.0;
            double extCm = AxisExtensionBeyondBoundaryCm;
            double xLo = ext.Xmin + offsetX - extCm;
            double xHi = ext.Xmax + offsetX + extCm;
            double yLo = ext.Ymin + offsetY - extCm;
            double yHi = ext.Ymax + offsetY + extCm;
            var xKolonAks = BuildColumnAxisIds(c => c.AxisXId, _model.AxisX.Select(a => a.Id));
            var yKolonAks = BuildColumnAxisIds(c => c.AxisYId, _model.AxisY.Select(a => a.Id));
            var xTopCenters = new List<Point3d>();
            var xBotCenters = new List<Point3d>();
            var yL = new List<Point3d>();
            var yR = new List<Point3d>();
            for (int i = 0; i < _model.AxisX.Count; i++)
            {
                var ax = _model.AxisX[i];
                if (!xKolonAks.Contains(ax.Id)) continue;
                double yBotE = ext.Ymin + offsetY - extCm;
                double yTopE = ext.Ymax + offsetY + extCm;
                double xBotE = offsetX + ax.ValueCm + ax.Slope * (ext.Ymin - extCm);
                double xTopE = offsetX + ax.ValueCm + ax.Slope * (ext.Ymax + extCm);
                var p1 = new Point3d(xBotE, yBotE, 0);
                var p2 = new Point3d(xTopE, yTopE, 0);
                if (!ClipSegmentToRectangle(p1, p2, xLo, xHi, yLo, yHi, out Point3d q1, out Point3d q2)) continue;
                if (Math.Abs(ax.Slope) > 1e-9) continue;
                xTopCenters.Add(AxisBalonCenterAtEnd(q2, q1, axisBalonRadiusCm));
                xBotCenters.Add(AxisBalonCenterAtEnd(q1, q2, axisBalonRadiusCm));
            }
            for (int j = 0; j < _model.AxisY.Count; j++)
            {
                var ay = _model.AxisY[j];
                if (!yKolonAks.Contains(ay.Id)) continue;
                double xLeft = ext.Xmin + offsetX - extCm;
                double xRight = ext.Xmax + offsetX + extCm;
                double yLeft = -(ay.ValueCm + ay.Slope * (ext.Xmin - extCm)) + offsetY;
                double yRight = -(ay.ValueCm + ay.Slope * (ext.Xmax + extCm)) + offsetY;
                var p1 = new Point3d(xLeft, yLeft, 0);
                var p2 = new Point3d(xRight, yRight, 0);
                if (!ClipSegmentToRectangle(p1, p2, xLo, xHi, yLo, yHi, out Point3d q1, out Point3d q2)) continue;
                if (Math.Abs(ay.Slope) > 1e-9) continue;
                yL.Add(AxisBalonCenterAtEnd(q1, q2, axisBalonRadiusCm));
                yR.Add(AxisBalonCenterAtEnd(q2, q1, axisBalonRadiusCm));
            }
            double pad = extCm + axisBalonRadiusCm + 40;
            xLeftBalloon = yL.Count > 0 ? yL.Min(p => p.X) : offsetX + ext.Xmin - pad;
            xRightBalloon = yR.Count > 0 ? yR.Max(p => p.X) : offsetX + ext.Xmax + pad;
            yBottomBalloon = xBotCenters.Count > 0 ? xBotCenters.Min(p => p.Y) : offsetY + ext.Ymin - pad;
            yTopBalloon = xTopCenters.Count > 0 ? xTopCenters.Max(p => p.Y) : offsetY + ext.Ymax + pad;
            xBotBalloons = xBotCenters;
            xTopBalloons = xTopCenters;
            yLeftBalloons = yL;
            yRightBalloons = yR;
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

        /// <summary>
        /// AKS_OLCU / PLAN_OLCU: Arrow size = <see cref="OlcuDimArrowTickSizeCm"/> (<see cref="DimStyleTableRecord.Dimasz"/>, <see cref="DimStyleTableRecord.Dimtsz"/>).
        /// First/Second — Oblique tik (Dimtsz + boş ok blokları).
        /// Leader — diyalogda &quot;Oblique&quot; görünsün diye özel blok atanmaz: <see cref="DimStyleTableRecord.Dimldrblk"/> = Null, First/Second ile aynı Dimtsz tik mantığı.
        /// </summary>
        private static void ApplyAksPlanOlcuDimStyleArrowsAndSize(DimStyleTableRecord rec, double dimTextHeightCm)
        {
            const double arrowSize = OlcuDimArrowTickSizeCm;
            try { rec.Dimscale = 1.0; } catch { }
            try { rec.Dimlfac = 1.0; } catch { }
            try { rec.Dimtxt = dimTextHeightCm; } catch { }
            try { rec.Dimblk = ObjectId.Null; } catch { }
            try { rec.Dimblk1 = ObjectId.Null; } catch { }
            try { rec.Dimblk2 = ObjectId.Null; } catch { }
            try { rec.Dimldrblk = ObjectId.Null; } catch { }
            try { rec.Dimasz = arrowSize; } catch { }
            try { rec.Dimtsz = arrowSize; } catch { }
            try { rec.Dimasz = arrowSize; } catch { }
            try { rec.Dimtsz = arrowSize; } catch { }
            try { rec.Dimtix = true; } catch { }
        }

        /// <summary>Aks ölçüleri için özel dim style "AKS_OLCU": metin stili YAZI (BEYKENT), yükseklik çağrıda; oklar vb. AKS_OLCU ayarları.</summary>
        private static ObjectId GetOrCreateAksOlcuDimStyle(Transaction tr, Database db, double dimTextHeightCm)
        {
            ObjectId yaziId = GetOrCreateYaziBeykentTextStyle(tr, db);
            var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (dst.Has(AksOlcuDimStyleName))
            {
                ObjectId id = dst[AksOlcuDimStyleName];
                try
                {
                    var existing = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                    if (!yaziId.IsNull) existing.Dimtxsty = yaziId;
                    ApplyAksPlanOlcuDimStyleArrowsAndSize(existing, dimTextHeightCm);
                }
                catch { }
                return id;
            }

            var newRec = new DimStyleTableRecord();
            newRec.Name = AksOlcuDimStyleName;
            try { if (!yaziId.IsNull) newRec.Dimtxsty = yaziId; } catch { }
            try { newRec.Dimclrt = Color.FromColorIndex(ColorMethod.ByAci, 7); } catch { }
            try { newRec.Dimgap = 2.0; } catch { }
            try { newRec.Dimtad = 1; } catch { }
            try { newRec.Dimtih = false; } catch { }
            try { newRec.Dimtoh = false; } catch { }

            try { newRec.Dimdec = 0; } catch { }
            try { newRec.Dimrnd = 0.5; } catch { }
            try { newRec.Dimlfac = 1.0; } catch { }
            try { newRec.Dimzin = 12; } catch { }
            try { newRec.Dimaunit = 0; } catch { }
            try { newRec.Dimadec = 0; } catch { }

            try { newRec.Dimtofl = true; } catch { }
            try { newRec.Dimscale = 1.0; } catch { }
            ApplyAksPlanOlcuDimStyleArrowsAndSize(newRec, dimTextHeightCm);

            dst.UpgradeOpen();
            ObjectId newDimId = dst.Add(newRec);
            tr.AddNewlyCreatedDBObject(newRec, true);
            dst.DowngradeOpen();
            return newDimId;
        }

        /// <summary>Kesit/plan eleman ölçüleri: AKS_OLCU ile aynı özellikler, metin YAZI (BEYKENT), isim PLAN_OLCU.</summary>
        private static ObjectId GetOrCreatePlanOlcuDimStyle(Transaction tr, Database db, double dimTextHeightCm)
        {
            ObjectId yaziId = GetOrCreateYaziBeykentTextStyle(tr, db);
            var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (dst.Has(PlanOlcuDimStyleName))
            {
                ObjectId id = dst[PlanOlcuDimStyleName];
                try
                {
                    var existing = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                    if (!yaziId.IsNull) existing.Dimtxsty = yaziId;
                    ApplyAksPlanOlcuDimStyleArrowsAndSize(existing, dimTextHeightCm);
                }
                catch { }
                return id;
            }

            var newRec = new DimStyleTableRecord();
            newRec.Name = PlanOlcuDimStyleName;
            try { if (!yaziId.IsNull) newRec.Dimtxsty = yaziId; } catch { }
            try { newRec.Dimclrt = Color.FromColorIndex(ColorMethod.ByAci, 7); } catch { }
            try { newRec.Dimgap = 2.0; } catch { }
            try { newRec.Dimtad = 1; } catch { }
            try { newRec.Dimtih = false; } catch { }
            try { newRec.Dimtoh = false; } catch { }
            try { newRec.Dimdec = 0; } catch { }
            try { newRec.Dimrnd = 0.5; } catch { }
            try { newRec.Dimlfac = 1.0; } catch { }
            try { newRec.Dimzin = 12; } catch { }
            try { newRec.Dimaunit = 0; } catch { }
            try { newRec.Dimadec = 0; } catch { }
            try { newRec.Dimtofl = true; } catch { }
            try { newRec.Dimscale = 1.0; } catch { }
            ApplyAksPlanOlcuDimStyleArrowsAndSize(newRec, dimTextHeightCm);
            dst.UpgradeOpen();
            ObjectId newPlanDimId = dst.Add(newRec);
            tr.AddNewlyCreatedDBObject(newRec, true);
            dst.DowngradeOpen();
            return newPlanDimId;
        }

        /// <summary>Çizimdeki tüm metin ve ölçü yazıları için ortak stil: "YAZI (BEYKENT)".</summary>
        private static ObjectId GetOrCreateYaziBeykentTextStyle(Transaction tr, Database db)
        {
            var txtTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (txtTable.Has(YaziBeykentTextStyleName)) return txtTable[YaziBeykentTextStyleName];

            var rec = new TextStyleTableRecord { Name = YaziBeykentTextStyleName };
            try
            {
                rec.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Bahnschrift Light Condensed", false, false, 0, 0);
            }
            catch
            {
                try { rec.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Arial", false, false, 0, 0); } catch { }
            }
            try { rec.TextSize = 0.0; } catch { }
            try { rec.XScale = 1.0; } catch { }
            txtTable.UpgradeOpen();
            ObjectId id = txtTable.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            txtTable.DowngradeOpen();
            return id;
        }

        private DBText MakeCenteredAxisLabelText(Transaction tr, Database db, double height, string value, Point3d p)
        {
            return new DBText
            {
                Layer = LayerAksYazisi,
                Height = height,
                TextStyleId = GetOrCreateYaziBeykentTextStyle(tr, db),
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(value ?? string.Empty),
                Position = p,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                AlignmentPoint = p
            };
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
            int maxColNo = _model.Columns.Count > 0 ? _model.Columns.Max(c => c.ColumnNo) : 0;
            int colPad = GetLabelPadWidth(maxColNo);
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

                Point2d labelRef = GetColumnLabelReferencePoint(center, col.AngleDeg, col.ColumnType, hw, hh, polygonSectionId);
                AppendColumnLabel(tr, btr, labelRef, col.AngleDeg, col.ColumnNo, col.ColumnType, dim, floor, colPad);
            }
        }

        private void DrawColumnPlanDimensionsForFloor(Transaction tr, BlockTableRecord btr, Database db, FloorInfo floor, double offsetX, double offsetY)
        {
            if (!_isKolon50Mode) return;
            const double dimTextHeightCm = 12.0;
            ObjectId dimStyleId = GetOrCreatePlanOlcuDimStyle(tr, db, dimTextHeightCm);
            const double rowGapCm = 20.0;
            const double firstOffsetCm = 20.0;

            foreach (var col in _model.Columns)
            {
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
                int polygonSectionId = ResolvePolygonPositionSectionId(floor.FloorNo, col.ColumnNo);
                if (col.ColumnType == 2) continue; // sadece dikdörtgen + poligon
                if (col.ColumnType == 3)
                {
                    if (polygonSectionId <= 0 || !_model.PolygonColumnSectionByPositionSectionId.ContainsKey(polygonSectionId)) continue;
                }
                else if (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId))
                {
                    continue;
                }

                var dim = sectionId > 0 && _model.ColumnDimsBySectionId.ContainsKey(sectionId)
                    ? _model.ColumnDimsBySectionId[sectionId]
                    : (W: 40.0, H: 40.0);
                double hw = dim.W / 2.0;
                double hh = dim.H / 2.0;
                var offsetLocal = ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                var center = new Point2d(axisNode.X + offsetGlobal.X + offsetX, axisNode.Y + offsetGlobal.Y + offsetY);
                var axisAtColumn = new Point2d(axisNode.X + offsetX, axisNode.Y + offsetY);

                if (col.ColumnType == 3 && TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out var polyPoints))
                {
                    DrawPolygonColumnDimension2Tier(tr, btr, dimStyleId, polyPoints, axisAtColumn, col.AngleDeg, firstOffsetCm, rowGapCm);
                }
                else
                {
                    DrawRectColumnDimensionWithAxisSplit(tr, btr, dimStyleId, center, hw, hh, col.AngleDeg, axisAtColumn, firstOffsetCm, rowGapCm);
                }
            }
        }

        private void DrawRectColumnDimensionWithAxisSplit(
            Transaction tr, BlockTableRecord btr, ObjectId dimStyleId,
            Point2d center, double hw, double hh, double angleDeg, Point2d axisPointWorld,
            double firstOffsetCm, double rowGapCm)
        {
            var rect = BuildRect(center, hw, hh, angleDeg);
            // rect order: LB, RB, RT, LT in local system
            DrawOneRectSideDimension(tr, btr, dimStyleId, rect[0], rect[1], new Vector2d(0, -1), axisPointWorld, angleDeg, center, firstOffsetCm, rowGapCm, useLocalX: true);
            DrawOneRectSideDimension(tr, btr, dimStyleId, rect[1], rect[2], new Vector2d(1, 0), axisPointWorld, angleDeg, center, firstOffsetCm, rowGapCm, useLocalX: false);
        }

        private void DrawOneRectSideDimension(
            Transaction tr, BlockTableRecord btr, ObjectId dimStyleId,
            Point2d p1, Point2d p2, Vector2d localOutwardNormal, Point2d axisPointWorld, double angleDeg, Point2d center,
            double firstOffsetCm, double rowGapCm, bool useLocalX)
        {
            Vector2d worldOut = Rotate(localOutwardNormal, angleDeg).GetNormal();
            Point2d mid = new Point2d((p1.X + p2.X) * 0.5, (p1.Y + p2.Y) * 0.5);

            // Aks kolonu kesiyorsa ikinci ölçü çizgisi: (parça ölçüler)
            Point2d axisLocal = ToLocal(axisPointWorld, center, angleDeg);
            Point2d a = ToLocal(p1, center, angleDeg);
            Point2d b = ToLocal(p2, center, angleDeg);
            double tSplit = useLocalX
                ? (Math.Abs(b.X - a.X) > 1e-9 ? (axisLocal.X - a.X) / (b.X - a.X) : -1.0)
                : (Math.Abs(b.Y - a.Y) > 1e-9 ? (axisLocal.Y - a.Y) / (b.Y - a.Y) : -1.0);
            bool hasSplit = tSplit > 1e-6 && tSplit < 1.0 - 1e-6;
            Point2d dimLinePtTotal = mid + worldOut.MultiplyBy(hasSplit ? (firstOffsetCm + rowGapCm) : firstOffsetCm);
            if (tSplit > 1e-6 && tSplit < 1.0 - 1e-6)
            {
                Point2d split = new Point2d(
                    p1.X + (p2.X - p1.X) * tSplit,
                    p1.Y + (p2.Y - p1.Y) * tSplit);
                Point2d dimLinePtSplit = mid + worldOut.MultiplyBy(firstOffsetCm);
                AppendEntity(tr, btr, new AlignedDimension(
                    new Point3d(p1.X, p1.Y, 0),
                    new Point3d(split.X, split.Y, 0),
                    new Point3d(dimLinePtSplit.X, dimLinePtSplit.Y, 0),
                    "",
                    dimStyleId) { Layer = LayerOlcu });
                AppendEntity(tr, btr, new AlignedDimension(
                    new Point3d(split.X, split.Y, 0),
                    new Point3d(p2.X, p2.Y, 0),
                    new Point3d(dimLinePtSplit.X, dimLinePtSplit.Y, 0),
                    "",
                    dimStyleId) { Layer = LayerOlcu });
            }

            AppendEntity(tr, btr, new AlignedDimension(
                new Point3d(p1.X, p1.Y, 0),
                new Point3d(p2.X, p2.Y, 0),
                new Point3d(dimLinePtTotal.X, dimLinePtTotal.Y, 0),
                "",
                dimStyleId) { Layer = LayerOlcu });
        }

        private void DrawPolygonColumnDimension2Tier(
            Transaction tr, BlockTableRecord btr, ObjectId dimStyleId, Point2d[] polyPoints, Point2d axisPointWorld, double angleDeg,
            double firstOffsetCm, double rowGapCm)
        {
            if (polyPoints == null || polyPoints.Length < 3) return;
            Point2d center = GetPolygonCenter(polyPoints);
            var local = polyPoints.Select(p => ToLocal(p, center, angleDeg)).ToList();
            double minX = local.Min(p => p.X), maxX = local.Max(p => p.X), minY = local.Min(p => p.Y), maxY = local.Max(p => p.Y);
            Point2d axisLocal = ToLocal(axisPointWorld, center, angleDeg);
            const double tol = 1e-3;

            DrawPolygonFace2Tier(tr, btr, dimStyleId, local, center, angleDeg, isHorizontal: true, faceValue: minY, outwardLocal: new Vector2d(0, -1), axisCoordOnFace: axisLocal.X, firstOffsetCm, rowGapCm, tol);
            DrawPolygonFace2Tier(tr, btr, dimStyleId, local, center, angleDeg, isHorizontal: false, faceValue: maxX, outwardLocal: new Vector2d(1, 0), axisCoordOnFace: axisLocal.Y, firstOffsetCm, rowGapCm, tol);
            DrawPolygonFace2Tier(tr, btr, dimStyleId, local, center, angleDeg, isHorizontal: true, faceValue: maxY, outwardLocal: new Vector2d(0, 1), axisCoordOnFace: axisLocal.X, firstOffsetCm, rowGapCm, tol);
            DrawPolygonFace2Tier(tr, btr, dimStyleId, local, center, angleDeg, isHorizontal: false, faceValue: minX, outwardLocal: new Vector2d(-1, 0), axisCoordOnFace: axisLocal.Y, firstOffsetCm, rowGapCm, tol);
        }

        private void DrawPolygonFace2Tier(
            Transaction tr, BlockTableRecord btr, ObjectId dimStyleId, List<Point2d> localPoints, Point2d center, double angleDeg,
            bool isHorizontal, double faceValue, Vector2d outwardLocal, double axisCoordOnFace,
            double firstOffsetCm, double rowGapCm, double tol)
        {
            if (localPoints == null || localPoints.Count < 2) return;
            double minT = isHorizontal ? localPoints.Min(p => p.X) : localPoints.Min(p => p.Y);
            double maxT = isHorizontal ? localPoints.Max(p => p.X) : localPoints.Max(p => p.Y);
            if (maxT - minT <= 1e-6) return;

            Point2d startLocal = isHorizontal ? new Point2d(minT, faceValue) : new Point2d(faceValue, minT);
            Point2d endLocal = isHorizontal ? new Point2d(maxT, faceValue) : new Point2d(faceValue, maxT);
            Point2d startWorld = ToWorld(startLocal, center, angleDeg);
            Point2d endWorld = ToWorld(endLocal, center, angleDeg);

            Vector2d worldOut = Rotate(outwardLocal, angleDeg).GetNormal();
            Point2d midWorld = new Point2d((startWorld.X + endWorld.X) * 0.5, (startWorld.Y + endWorld.Y) * 0.5);

            // 2. katman: toplam
            Point2d totalLinePt = midWorld + worldOut.MultiplyBy(firstOffsetCm + rowGapCm);
            AppendEntity(tr, btr, new AlignedDimension(
                new Point3d(startWorld.X, startWorld.Y, 0),
                new Point3d(endWorld.X, endWorld.Y, 0),
                new Point3d(totalLinePt.X, totalLinePt.Y, 0),
                "",
                dimStyleId) { Layer = LayerOlcu });

            // 1. katman: verteks segmentleri + kolon aksi ile bolumler
            var cutCoords = new List<double>();
            foreach (var pt in localPoints)
            {
                cutCoords.Add(isHorizontal ? pt.X : pt.Y);
            }
            cutCoords.Add(axisCoordOnFace);
            var orderedCuts = cutCoords
                .Where(v => v >= minT - 1e-6 && v <= maxT + 1e-6)
                .OrderBy(v => v)
                .ToList();
            var uniq = new List<double>();
            foreach (double v in orderedCuts)
            {
                if (uniq.Count == 0 || Math.Abs(uniq[uniq.Count - 1] - v) > 1e-3) uniq.Add(v);
            }
            if (uniq.Count < 2) return;

            Point2d detailedLinePt = midWorld + worldOut.MultiplyBy(firstOffsetCm);
            for (int i = 0; i < uniq.Count - 1; i++)
            {
                Point2d aL = isHorizontal ? new Point2d(uniq[i], faceValue) : new Point2d(faceValue, uniq[i]);
                Point2d bL = isHorizontal ? new Point2d(uniq[i + 1], faceValue) : new Point2d(faceValue, uniq[i + 1]);
                Point2d aW = ToWorld(aL, center, angleDeg);
                Point2d bW = ToWorld(bL, center, angleDeg);
                if (aW.GetDistanceTo(bW) <= 1e-6) continue;
                AppendEntity(tr, btr, new AlignedDimension(
                    new Point3d(aW.X, aW.Y, 0),
                    new Point3d(bW.X, bW.Y, 0),
                    new Point3d(detailedLinePt.X, detailedLinePt.Y, 0),
                    "",
                    dimStyleId) { Layer = LayerOlcu });
            }
        }

        private static Point2d GetPolygonCenter(Point2d[] pts)
        {
            double sx = 0.0, sy = 0.0;
            for (int i = 0; i < pts.Length; i++) { sx += pts[i].X; sy += pts[i].Y; }
            return new Point2d(sx / pts.Length, sy / pts.Length);
        }

        private static Point2d ToLocal(Point2d world, Point2d origin, double angleDeg)
        {
            double a = -angleDeg * (Math.PI / 180.0);
            double c = Math.Cos(a), s = Math.Sin(a);
            double dx = world.X - origin.X;
            double dy = world.Y - origin.Y;
            return new Point2d(dx * c - dy * s, dx * s + dy * c);
        }

        private static Point2d ToWorld(Point2d local, Point2d origin, double angleDeg)
        {
            var r = Rotate(new Vector2d(local.X, local.Y), angleDeg);
            return new Point2d(origin.X + r.X, origin.Y + r.Y);
        }

        /// <summary>Kolonun sağ alt köşesi: kolon açısına göre (dünya Y değil). Dikdörtgen: rect[1]; daire: merkez + r*(sağ-alt yönü); poligon: kolon yerelinde sağ-alt köşe.</summary>
        private Point2d GetColumnLabelReferencePoint(Point2d center, double angleDeg, int columnType, double hw, double hh, int polygonSectionId)
        {
            const double tol = 1e-6;
            double angleRad = angleDeg * (Math.PI / 180.0);
            bool angled = Math.Abs(angleDeg) > tol;
            if (columnType == 2)
            {
                double r = Math.Max(hw, hh);
                if (!angled)
                    return new Point2d(center.X + r, center.Y - r);
                double cos = Math.Cos(angleRad), sin = Math.Sin(angleRad);
                double dx = (cos + sin) / Math.Sqrt(2), dy = (sin - cos) / Math.Sqrt(2);
                return new Point2d(center.X + r * dx, center.Y + r * dy);
            }
            if (columnType == 3 && TryGetPolygonColumn(polygonSectionId, center, angleDeg, out var polyPoints))
            {
                double cos = Math.Cos(angleRad), sin = Math.Sin(angleRad);
                int idx = 0;
                double best = double.NegativeInfinity;
                for (int i = 0; i < polyPoints.Length; i++)
                {
                    double lx = (polyPoints[i].X - center.X) * cos + (polyPoints[i].Y - center.Y) * sin;
                    double lyDown = (polyPoints[i].X - center.X) * sin - (polyPoints[i].Y - center.Y) * cos;
                    double score = lx + lyDown;
                    if (score > best) { best = score; idx = i; }
                }
                return polyPoints[idx];
            }
            var rect = BuildRect(center, hw, hh, angleDeg);
            return rect[1];
        }

        /// <summary>Kolon etiketi: KOLON ISMI (BEYKENT), format "S" + Story ID + kolon no (haneli, örn. SB01 S15). Isim üstte boyut altta; boyut satırı kolon açısı yönünde altında.</summary>
        private void AppendColumnLabel(Transaction tr, BlockTableRecord btr, Point2d refPoint, double angleDeg, int columnNo, int columnType, (double W, double H) dim, FloorInfo floor, int columnNoPadWidth = 2)
        {
            const double labelHeightCm = 12.0;
            const double gapNameToDimCm = 18.0;
            const double offsetRightCm = 10.0;
            const double offsetNameDownCm = 22.0;
            double angleRad = angleDeg * (Math.PI / 180.0);
            double cos = Math.Cos(angleRad), sin = Math.Sin(angleRad);
            Point2d namePos = new Point2d(
                refPoint.X + offsetRightCm * cos + offsetNameDownCm * sin,
                refPoint.Y + offsetRightCm * sin - offsetNameDownCm * cos);
            // Boyut satırı: ismin "altında" kolon açısı yönünde
            double dx = gapNameToDimCm * Math.Sin(angleRad);
            double dy = -gapNameToDimCm * Math.Cos(angleRad);
            Point2d dimPos = new Point2d(namePos.X + dx, namePos.Y + dy);
            double rotationRad = angleRad;
            Database db = btr.Database;
            ObjectId styleId = GetOrCreateYaziBeykentTextStyle(tr, db);
            string storyId = floor != null && !string.IsNullOrEmpty(floor.ShortName)
                ? floor.ShortName
                : (floor != null ? floor.FloorNo.ToString(CultureInfo.InvariantCulture) : "B");
            int pad = columnNoPadWidth < 1 ? 1 : columnNoPadWidth;
            string nameLine = "S" + storyId + columnNo.ToString("D" + pad, CultureInfo.InvariantCulture);
            string dimLine = columnType == 2
                ? "R= " + dim.W.ToString("F0", CultureInfo.InvariantCulture)
                : string.Format(CultureInfo.InvariantCulture, "({0:F0}/{1:F0})", dim.W, dim.H);
            var vertMode = TextVerticalMode.TextBottom;
            if (columnType == 3)
            {
                var txt = new DBText
                {
                    Layer = LayerKolonIsmi,
                    Height = labelHeightCm,
                    TextStyleId = styleId,
                    TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(nameLine),
                    Position = new Point3d(namePos.X, namePos.Y, 0),
                    HorizontalMode = TextHorizontalMode.TextLeft,
                    VerticalMode = vertMode,
                    AlignmentPoint = new Point3d(namePos.X, namePos.Y, 0),
                    Rotation = rotationRad
                };
                AppendEntity(tr, btr, txt);
                return;
            }
            var txt1 = new DBText
            {
                Layer = LayerKolonIsmi,
                Height = labelHeightCm,
                TextStyleId = styleId,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(nameLine),
                Position = new Point3d(namePos.X, namePos.Y, 0),
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = vertMode,
                AlignmentPoint = new Point3d(namePos.X, namePos.Y, 0),
                Rotation = rotationRad
            };
            AppendEntity(tr, btr, txt1);
            var txt2 = new DBText
            {
                Layer = LayerKolonIsmi,
                Height = labelHeightCm,
                TextStyleId = styleId,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(dimLine),
                Position = new Point3d(dimPos.X, dimPos.Y, 0),
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = vertMode,
                AlignmentPoint = new Point3d(dimPos.X, dimPos.Y, 0),
                Rotation = rotationRad
            };
            AppendEntity(tr, btr, txt2);
        }

        /// <summary>
        /// KOLON50 perde kopyalarında: etiket solundan hizalı — kolonun en sağ noktasının 10 cm sağında (TextLeft);
        /// düşeyde önceki hizaya göre 10 cm aşağı; üst satır tepesi hâlâ perde alt çizgisine göre hesaplanır.
        /// </summary>
        private void AppendColumnLabelCenteredBelowWallBottom(
            Transaction tr,
            BlockTableRecord btr,
            double columnRightEdgeX,
            double wallBottomY,
            int columnNo,
            int columnType,
            (double W, double H) dim,
            FloorInfo floor,
            int columnNoPadWidth = 2)
        {
            const double labelHeightCm = 12.0;
            const double gapNameToDimCm = 18.0;
            // Perde alt çizgisi (Y) ile üst yazı satırının üst kenarı arası (cm) — hemen alt.
            const double gapBelowWallTopOfTextCm = 3.0;
            const double extraDownCm = 10.0;
            const double offsetRightFromColumnCm = 10.0;
            double anchorX = columnRightEdgeX + offsetRightFromColumnCm;
            Database db = btr.Database;
            ObjectId styleId = GetOrCreateYaziBeykentTextStyle(tr, db);
            string storyId = floor != null && !string.IsNullOrEmpty(floor.ShortName)
                ? floor.ShortName
                : (floor != null ? floor.FloorNo.ToString(CultureInfo.InvariantCulture) : "B");
            int pad = columnNoPadWidth < 1 ? 1 : columnNoPadWidth;
            string nameLine = "S" + storyId + columnNo.ToString("D" + pad, CultureInfo.InvariantCulture);
            string dimLine = columnType == 2
                ? "R= " + dim.W.ToString("F0", CultureInfo.InvariantCulture)
                : string.Format(CultureInfo.InvariantCulture, "({0:F0}/{1:F0})", dim.W, dim.H);
            var vertMode = TextVerticalMode.TextBottom;
            // TextBottom: hizalama noktası satırın tabanı; üst kenar = taban + yükseklik.
            // Üst satırın üstü = wallBottomY - gap - extraDownCm (bir miktar daha aşağı).
            double nameBaselineY = wallBottomY - gapBelowWallTopOfTextCm - labelHeightCm - extraDownCm;

            if (columnType == 3)
            {
                var txt = new DBText
                {
                    Layer = LayerKolonIsmi,
                    Height = labelHeightCm,
                    TextStyleId = styleId,
                    TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(nameLine),
                    HorizontalMode = TextHorizontalMode.TextLeft,
                    VerticalMode = vertMode,
                    AlignmentPoint = new Point3d(anchorX, nameBaselineY, 0),
                    Position = new Point3d(anchorX, nameBaselineY, 0),
                    Rotation = 0.0
                };
                AppendEntity(tr, btr, txt);
                return;
            }

            var txt1 = new DBText
            {
                Layer = LayerKolonIsmi,
                Height = labelHeightCm,
                TextStyleId = styleId,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(nameLine),
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = vertMode,
                AlignmentPoint = new Point3d(anchorX, nameBaselineY, 0),
                Position = new Point3d(anchorX, nameBaselineY, 0),
                Rotation = 0.0
            };
            AppendEntity(tr, btr, txt1);
            double dimBaselineY = nameBaselineY - gapNameToDimCm;
            var txt2 = new DBText
            {
                Layer = LayerKolonIsmi,
                Height = labelHeightCm,
                TextStyleId = styleId,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(dimLine),
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = vertMode,
                AlignmentPoint = new Point3d(anchorX, dimBaselineY, 0),
                Position = new Point3d(anchorX, dimBaselineY, 0),
                Rotation = 0.0
            };
            AppendEntity(tr, btr, txt2);
        }

        /// <summary>Verilen kattaki perdeleri (IsWallFlag==1) çizer; kolon alanları çıkarılır, saç teli temizliği uygulanır. Temel planında bodrum perdeleri için kullanılır.</summary>
        private void DrawWallsForFloor(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY)
        {
            var factory = _ntsDrawFactory;
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

        /// <summary>
        /// Verilen aks ID'sine ait eksen çizgisinin plan açısını (radyan) döndürür. Etiketin aksa paralel çizilmesi için kullanılır.
        /// X: doğru yönü (Slope,1) → Atan2(1, Slope). Y: doğru yönü (-1, Slope) → Atan2(Slope, -1).
        /// Açı (-π/2, π/2] aralığına normalize edilir; böylece etiket çizimde ters (baş aşağı) görünmez.
        /// </summary>
        private double GetAxisLineAngleRad(int axisId)
        {
            var axis = _model.AxisX.Concat(_model.AxisY).FirstOrDefault(a => a.Id == axisId);
            if (axis == null) return 0.0;
            double angleRad;
            if (axis.Kind == AxisKind.X)
                angleRad = Math.Atan2(1.0, axis.Slope);
            else
                angleRad = Math.Atan2(axis.Slope, -1.0);
            // Eksen doğrusunun iki yönü var (θ ve θ+π); metnin okunaklı olması için (-π/2, π/2] seç
            while (angleRad > Math.PI / 2.0) angleRad -= Math.PI;
            while (angleRad <= -Math.PI / 2.0) angleRad += Math.PI;
            return angleRad;
        }

        /// <summary>Point2d[] dikdörtgenini NTS Polygon'a çevirir (kapalı halka).</summary>
        private static Polygon RectToPolygon(GeometryFactory factory, Point2d[] rect)
        {
            if (rect == null || rect.Length < 4) return null;
            var coords = new Coordinate[5];
            for (int i = 0; i < 4; i++) coords[i] = new Coordinate(rect[i].X, rect[i].Y);
            coords[4] = coords[0];
            return factory.CreatePolygon(factory.CreateLinearRing(coords));
        }

        /// <summary>Sürekli temel etiketi: etiket kutusu diğer sürekli temeller veya radye alanı ile kesişiyorsa yazı açısı doğrultusunda sadece sağa/sola kaydırarak temiz alan arar. Sürekli/radye alanı içine taşımaz. Temiz alan yoksa taşımaz.</summary>
        private static void TryFindClearLabelPositionForContinuous(GeometryFactory factory, ref double labelCx, ref double labelCy, double axisAngleRad, string labelText, List<Geometry> obstaclePolygons, int currentObstacleIndex = -1, Geometry slabUnion = null)
        {
            const double labelHeightCm = 12.0;
            const double toleranceCm = 5.0;
            const double extraShiftCm = 4.0;
            double labelWidthCm = Math.Max(55.0, (labelText?.Length ?? 0) * 7.0);
            double cosA = Math.Cos(axisAngleRad);
            double sinA = Math.Sin(axisAngleRad);
            Polygon LabelBoxAt(double cx, double cy)
            {
                double brX = cx; double brY = cy;
                double blX = cx - labelWidthCm * cosA; double blY = cy - labelWidthCm * sinA;
                double tlX = blX - labelHeightCm * sinA; double tlY = blY + labelHeightCm * cosA;
                double trX = brX - labelHeightCm * sinA; double trY = brY + labelHeightCm * cosA;
                var coords = new[] { new Coordinate(brX, brY), new Coordinate(blX, blY), new Coordinate(tlX, tlY), new Coordinate(trX, trY), new Coordinate(brX, brY) };
                return factory.CreatePolygon(factory.CreateLinearRing(coords));
            }
            bool IntersectsObstaclesWithTolerance(Geometry box)
            {
                if (box == null || box.IsEmpty) return false;
                Geometry checkArea = null;
                try { checkArea = box.Buffer(toleranceCm); } catch { checkArea = box; }
                if (checkArea == null || checkArea.IsEmpty) return false;
                for (int i = 0; i < (obstaclePolygons?.Count ?? 0); i++)
                {
                    if (i == currentObstacleIndex) continue;
                    var obs = obstaclePolygons[i];
                    if (obs != null && !obs.IsEmpty && checkArea.Intersects(obs)) return true;
                }
                if (slabUnion != null && !slabUnion.IsEmpty && checkArea.Intersects(slabUnion)) return true;
                return false;
            }
            if (!IntersectsObstaclesWithTolerance(LabelBoxAt(labelCx, labelCy))) return;
            double step = 10.0;
            for (int n = 1; n <= 25; n++)
            {
                foreach (double sign in new[] { 1.0, -1.0 })
                {
                    double dx = sign * n * step * cosA;
                    double dy = sign * n * step * sinA;
                    double cx = labelCx + dx;
                    double cy = labelCy + dy;
                    if (!IntersectsObstaclesWithTolerance(LabelBoxAt(cx, cy)))
                    {
                        labelCx = cx + sign * extraShiftCm * cosA;
                        labelCy = cy + sign * extraShiftCm * sinA;
                        return;
                    }
                }
            }
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

        /// <summary>Geometrinin sınırındaki köşe noktalarını döndürür (Polygon/MultiPolygon dış halkaları, kapalı halkanın son noktası hariç).</summary>
        private static List<Coordinate> GetBoundaryVertices(Geometry geom)
        {
            var list = new List<Coordinate>();
            if (geom == null || geom.IsEmpty) return list;
            if (geom is Polygon pg && pg.ExteriorRing != null)
            {
                var coords = pg.ExteriorRing.Coordinates;
                for (int i = 0; i < coords.Length - 1; i++) list.Add(coords[i]);
                return list;
            }
            if (geom is MultiPolygon mp)
            {
                for (int k = 0; k < mp.NumGeometries; k++)
                    if (mp.GetGeometryN(k) is Polygon p && p.ExteriorRing != null)
                    {
                        var coords = p.ExteriorRing.Coordinates;
                        for (int i = 0; i < coords.Length - 1; i++) list.Add(coords[i]);
                    }
                return list;
            }
            return list;
        }

        /// <summary>İki geometri en az iki ortak köşe noktasına (tolerans dahilinde) sahipse true.</summary>
        private static bool ShareAtLeastTwoVertices(Geometry a, Geometry b, double toleranceCm = 0.2)
        {
            var va = GetBoundaryVertices(a);
            var vb = GetBoundaryVertices(b);
            if (va.Count == 0 || vb.Count == 0) return false;
            int shared = 0;
            foreach (var ca in va)
            {
                foreach (var cb in vb)
                {
                    double dx = ca.X - cb.X, dy = ca.Y - cb.Y;
                    if (dx * dx + dy * dy <= toleranceCm * toleranceCm) { shared++; break; }
                }
                if (shared >= 2) return true;
            }
            return false;
        }

        /// <summary>İki geometri kenar teması (segment) veya gerçek alan örtüşmesi ile birleştirilir. Tek nokta teması ve kolon/perde kesim kenarındaki temas birleştirme sayılmaz.</summary>
        private static bool ProperlyTouches(Geometry a, Geometry b, double minTouchLengthCm = 0.2, Geometry kolonPerdeBoundary = null)
        {
            if (a == null || b == null || a.IsEmpty || b.IsEmpty) return false;
            // Örtüşme: sadece gerçek alan örtüşmesi (köşe noktası/çizgi = alan 0 birleştirilmez)
            if (a.Intersects(b) && !a.Touches(b))
            {
                try
                {
                    var inter = a.Intersection(b);
                    if (inter == null || inter.IsEmpty) return false;
                    if (inter.Area < 0.5) return false;
                    return true;
                }
                catch { return false; }
            }
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
                // Görselde birbirine değmiyor: temas kolon/perde hattına çok yakınsa (0.3 cm) birleştirme yapılmaz
                if (kolonPerdeBoundary != null && !kolonPerdeBoundary.IsEmpty && inter.Distance(kolonPerdeBoundary) <= 0.3)
                    return false;
                return true;
            }
            catch { return false; }
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

        /// <summary>Noktadan verilen yönde ilerleyen ışının poligon kenarıyla ilk kesişimine olan mesafeyi döndürür. Kesişim yoksa double.MaxValue.</summary>
        private static double DistanceFromPointToPolygonBoundaryInDirection(Point2d point, Vector2d dirNormalized, Polygon poly)
        {
            if (poly == null || poly.IsEmpty || dirNormalized.Length < 1e-9) return double.MaxValue;
            var ring = poly.ExteriorRing;
            if (ring == null) return double.MaxValue;
            var coords = ring.Coordinates;
            if (coords == null || coords.Length < 3) return double.MaxValue;
            double px = point.X, py = point.Y;
            double dx = dirNormalized.X, dy = dirNormalized.Y;
            double minT = double.MaxValue;
            for (int i = 0; i < coords.Length - 1; i++)
            {
                double ax = coords[i].X, ay = coords[i].Y;
                double bx = coords[i + 1].X, by = coords[i + 1].Y;
                double vx = bx - ax, vy = by - ay;
                double wx = px - ax, wy = py - ay;
                double denom = dx * vy - dy * vx;
                if (Math.Abs(denom) < 1e-12) continue;
                double t = (wx * vy - wy * vx) / denom;
                double v2 = vx * vx + vy * vy;
                if (v2 < 1e-12) continue;
                double s = (wx * vx + wy * vy + t * (dx * vx + dy * vy)) / v2;
                if (t > 1e-9 && s >= -1e-9 && s <= 1.0 + 1e-9 && t < minT)
                    minT = t;
            }
            return minT;
        }

        /// <summary>Köşe noktasından diğer uca giden kiriş segmenti için NTS dikdörtgen (kiriş kesiti) üretir; uzatma kuralında iki kirişin sağ/sol çizgilerinin kesişmesi kontrolü için kullanılır.</summary>
        private static Geometry BuildBeamStubPolygon(Point2d corner, Point2d otherEnd, BeamInfo beam, GeometryFactory factory)
        {
            Vector2d axisDir = otherEnd - corner;
            if (axisDir.Length < 1e-9) return null;
            Vector2d u = axisDir.GetNormal();
            Vector2d perp = new Vector2d(-u.Y, u.X);
            double hw = beam.WidthCm / 2.0;
            ComputeBeamEdgeOffsets(beam.OffsetRaw, hw, out double upperEdge, out double lowerEdge);
            var coords = new[]
            {
                new Coordinate((corner + perp.MultiplyBy(upperEdge)).X, (corner + perp.MultiplyBy(upperEdge)).Y),
                new Coordinate((otherEnd + perp.MultiplyBy(upperEdge)).X, (otherEnd + perp.MultiplyBy(upperEdge)).Y),
                new Coordinate((otherEnd + perp.MultiplyBy(lowerEdge)).X, (otherEnd + perp.MultiplyBy(lowerEdge)).Y),
                new Coordinate((corner + perp.MultiplyBy(lowerEdge)).X, (corner + perp.MultiplyBy(lowerEdge)).Y),
                new Coordinate((corner + perp.MultiplyBy(upperEdge)).X, (corner + perp.MultiplyBy(upperEdge)).Y)
            };
            return factory.CreatePolygon(factory.CreateLinearRing(coords));
        }

        /// <summary>İki konsol kiriş köşesi: sadece X ve Y aksına sabit iki kirişin birleştiği noktalar. Aynı ID'li kirişlerin iç kenarları dahil edilmez.
        /// Uzatma kuralı (resimdeki kırmızı mesafeler):
        /// - KZ-50 sola: Kesişim noktasından (işaret) KZ-56'nın uzatma yönünde kalan kenarına (KZ-56'nın sol dış çizgisi) kadar = 1. resimdeki kırmızı mesafe.
        /// - KZ-56 aşağı: Kesişim noktasından KZ-50'nin uzatma yönünde kalan kenarına (KZ-50'nin alt dış çizgisi) kadar = 2. resimdeki kırmızı mesafe.
        /// Yani uzatma = köşeden referans kirişin (diğer kirişin) dış kenar çizgisine kadar olan mesafe; ışın–poligon kenar kesişimi ile hesaplanır.
        /// Döner: (rounded point -> (beamId, point1Kot, point2Kot) -> extend cm). Farklı kottaki kirişler ayrı anahtarla tutulur.</summary>
        private Dictionary<(int x, int y), Dictionary<(int beamId, double p1, double p2), double>> BuildTwoBeamCornerExtensionMap(List<BeamInfo> beamsForDrawing, int floorNo, double offsetX, double offsetY, Geometry kolonPerdeUnion, GeometryFactory factory)
        {
            if (beamsForDrawing == null || beamsForDrawing.Count == 0) return null;
            const int axisXMin = 1001, axisXMax = 1999;
            const int axisYMin = 2001, axisYMax = 2999;

            var pointToBeams = new Dictionary<(int x, int y), List<(BeamInfo beam, Point2d otherEnd)>>();
            var endpointCountByBeam = new Dictionary<(int beamId, int x, int y), int>();
            foreach (var beam in beamsForDrawing)
            {
                if (GetBeamFloorNo(beam.BeamId) != floorNo) continue;
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1)) continue;
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2)) continue;
                var a = new Point2d(p1.X + offsetX, p1.Y + offsetY);
                var b = new Point2d(p2.X + offsetX, p2.Y + offsetY);
                var keyA = ((int)Math.Round(a.X), (int)Math.Round(a.Y));
                var keyB = ((int)Math.Round(b.X), (int)Math.Round(b.Y));
                var kA = (beam.BeamId, keyA.Item1, keyA.Item2);
                var kB = (beam.BeamId, keyB.Item1, keyB.Item2);
                if (!endpointCountByBeam.ContainsKey(kA)) endpointCountByBeam[kA] = 0;
                endpointCountByBeam[kA]++;
                if (!endpointCountByBeam.ContainsKey(kB)) endpointCountByBeam[kB] = 0;
                endpointCountByBeam[kB]++;
                if (!pointToBeams.ContainsKey(keyA)) pointToBeams[keyA] = new List<(BeamInfo, Point2d)>();
                pointToBeams[keyA].Add((beam, b));
                if (!pointToBeams.ContainsKey(keyB)) pointToBeams[keyB] = new List<(BeamInfo, Point2d)>();
                pointToBeams[keyB].Add((beam, a));
            }
            var result = new Dictionary<(int x, int y), Dictionary<(int beamId, double p1, double p2), double>>();
            foreach (var kv in pointToBeams)
            {
                var list = kv.Value.GroupBy(x => (x.beam.BeamId, x.beam.Point1KotCm, x.beam.Point2KotCm)).Select(g => g.First()).ToList();
                if (list.Count != 2) continue;
                var (beam1, other1) = list[0];
                var (beam2, other2) = list[1];
                var keyBeam1 = (beam1.BeamId, beam1.Point1KotCm, beam1.Point2KotCm);
                var keyBeam2 = (beam2.BeamId, beam2.Point1KotCm, beam2.Point2KotCm);
                int keyX = kv.Key.Item1, keyY = kv.Key.Item2;
                int c1, c2;
                if (!endpointCountByBeam.TryGetValue((beam1.BeamId, keyX, keyY), out c1) || c1 != 1) continue;
                if (!endpointCountByBeam.TryGetValue((beam2.BeamId, keyX, keyY), out c2) || c2 != 1) continue;
                int fix1 = beam1.FixedAxisId, fix2 = beam2.FixedAxisId;
                bool oneX = (fix1 >= axisXMin && fix1 <= axisXMax) || (fix2 >= axisXMin && fix2 <= axisXMax);
                bool oneY = (fix1 >= axisYMin && fix1 <= axisYMax) || (fix2 >= axisYMin && fix2 <= axisYMax);
                if (!oneX || !oneY) continue;
                var pt = factory.CreatePoint(new Coordinate(kv.Key.Item1, kv.Key.Item2));
                if (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty && kolonPerdeUnion.Contains(pt)) continue;

                Point2d corner = new Point2d(kv.Key.Item1, kv.Key.Item2);
                Geometry poly1 = BuildBeamStubPolygon(corner, other1, beam1, factory);
                Geometry poly2 = BuildBeamStubPolygon(corner, other2, beam2, factory);
                if (poly1 == null || poly2 == null || poly1.IsEmpty || poly2.IsEmpty || !poly1.Intersects(poly2))
                    continue; // Sağ/sol çizgileri kesişmiyorsa (uç uca gelmeyen kirişler) uzatma yapma

                // Uzatma = köşeden referans kirişin İÇ kenarına (uzatma yönünde en yakın yüz = işaret hizasındaki 20cm) kadar mesafe; dış kenara (60cm) göre değil.
                Vector2d dir1 = (corner - other1).GetNormal();
                Vector2d dir2 = (corner - other2).GetNormal();
                double d1Out = DistanceFromPointToPolygonBoundaryInDirection(corner, dir1, poly2 as Polygon);
                double d1In = DistanceFromPointToPolygonBoundaryInDirection(corner, new Vector2d(-dir1.X, -dir1.Y), poly2 as Polygon);
                double d2Out = DistanceFromPointToPolygonBoundaryInDirection(corner, dir2, poly1 as Polygon);
                double d2In = DistanceFromPointToPolygonBoundaryInDirection(corner, new Vector2d(-dir2.X, -dir2.Y), poly1 as Polygon);
                double ext1 = (d1Out > 0 && d1Out < 1e6) && (d1In > 0 && d1In < 1e6) ? Math.Min(d1Out, d1In) : (d1Out > 0 && d1Out < 1e6 ? d1Out : (d1In > 0 && d1In < 1e6 ? d1In : beam2.WidthCm / 2.0));
                double ext2 = (d2Out > 0 && d2Out < 1e6) && (d2In > 0 && d2In < 1e6) ? Math.Min(d2Out, d2In) : (d2Out > 0 && d2Out < 1e6 ? d2Out : (d2In > 0 && d2In < 1e6 ? d2In : beam1.WidthCm / 2.0));
                if (ext1 <= 0 || ext1 >= 1e6) ext1 = beam2.WidthCm / 2.0;
                if (ext2 <= 0 || ext2 >= 1e6) ext2 = beam1.WidthCm / 2.0;
                result[kv.Key] = new Dictionary<(int, double, double), double> { { keyBeam1, ext1 }, { keyBeam2, ext2 } };
            }
            return result.Count == 0 ? null : result;
        }

        /// <summary>İki doğru parçasının kesişim noktasını döndürür; kesişim parça içinde değilse null.</summary>
        private static Point2d? SegmentSegmentIntersection(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy, double tol = 1e-9)
        {
            double vx = bx - ax, vy = by - ay;
            double wx = dx - cx, wy = dy - cy;
            double denom = vx * wy - vy * wx;
            if (Math.Abs(denom) < tol) return null;
            double rx = cx - ax, ry = cy - ay;
            double t = (rx * wy - ry * wx) / denom;
            double s = (rx * vy - ry * vx) / denom;
            if (t >= -tol && t <= 1.0 + tol && s >= -tol && s <= 1.0 + tol)
                return new Point2d(ax + t * vx, ay + t * vy);
            return null;
        }

        /// <summary>Geometry'nin dış halka kenarlarını doğru parçaları olarak döndürür (Polygon/MultiPolygon/GeometryCollection).</summary>
        private static void GetBoundarySegments(Geometry geom, List<(double ax, double ay, double bx, double by)> segments)
        {
            if (geom == null || geom.IsEmpty) return;
            if (geom is Polygon poly && poly.ExteriorRing != null)
            {
                var coords = poly.ExteriorRing.Coordinates;
                for (int i = 0; i < coords.Length - 1; i++)
                    segments.Add((coords[i].X, coords[i].Y, coords[i + 1].X, coords[i + 1].Y));
                return;
            }
            if (geom is MultiPolygon mp)
            {
                for (int n = 0; n < mp.NumGeometries; n++)
                    GetBoundarySegments(mp.GetGeometryN(n), segments);
                return;
            }
            if (geom is NetTopologySuite.Geometries.GeometryCollection gc)
            {
                for (int n = 0; n < gc.NumGeometries; n++)
                    GetBoundarySegments(gc.GetGeometryN(n), segments);
            }
        }

        /// <summary>Kirişin sadece iki yan kenarını (eksene dik uç yüzleri) döndürür; eksene paralel uzun kenarlar dahil edilmez. fixedAxisId 1001-1999 = X aksı (plan görünüşte uç yüzler yatay), 2001-2999 = Y aksı (uç yüzler düşey).</summary>
        private static void GetLateralBoundarySegments(Geometry geom, int fixedAxisId, List<(double ax, double ay, double bx, double by)> segments, int axisXMin = 1001, int axisXMax = 1999)
        {
            var all = new List<(double ax, double ay, double bx, double by)>();
            GetBoundarySegments(geom, all);
            bool isXAxis = fixedAxisId >= axisXMin && fixedAxisId <= axisXMax;
            foreach (var s in all)
            {
                double dx = s.bx - s.ax, dy = s.by - s.ay;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;
                dx /= len; dy /= len;
                if (isXAxis) { if (Math.Abs(dx) >= 0.9) segments.Add(s); }
                else { if (Math.Abs(dy) >= 0.9) segments.Add(s); }
            }
        }

        /// <summary>Noktanın doğru parçasına en kısa mesafesini döndürür.</summary>
        private static double DistancePointToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double vx = bx - ax, vy = by - ay;
            double len2 = vx * vx + vy * vy;
            if (len2 < 1e-18) return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
            double t = Math.Max(0, Math.Min(1, ((px - ax) * vx + (py - ay) * vy) / len2));
            double qx = ax + t * vx, qy = ay + t * vy;
            return Math.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
        }

        /// <summary>Kiriş geometrisinde kırmızı işaretin olduğu uçtaki yan kenarı (köşe noktasına en yakın lateral segment) bulur; bu kenarın başlangıç ve bitiş vertex noktalarını döndürür (mavi işaret bu vertex'lere konur).</summary>
        private static List<Point2d> GetCornerEndVerticesOfBeamFromCorner(Geometry beamGeom, int fixedAxisId, Point2d corner, int axisXMin = 1001, int axisXMax = 1999)
        {
            var result = new List<Point2d>();
            var laterals = new List<(double ax, double ay, double bx, double by)>();
            GetLateralBoundarySegments(beamGeom, fixedAxisId, laterals, axisXMin, axisXMax);
            if (laterals.Count == 0) return result;
            double cx = corner.X, cy = corner.Y;
            int nearIdx = 0;
            double nearDist = double.MaxValue;
            for (int i = 0; i < laterals.Count; i++)
            {
                var s = laterals[i];
                double d = DistancePointToSegment(cx, cy, s.ax, s.ay, s.bx, s.by);
                if (d < nearDist) { nearDist = d; nearIdx = i; }
            }
            var near = laterals[nearIdx];
            result.Add(new Point2d(near.ax, near.ay));
            result.Add(new Point2d(near.bx, near.by));
            return result;
        }

        /// <summary>Tek (a,b) segmentinden kiriş dikdörtgeni poligonu üretir.</summary>
        private static Polygon BuildBeamSegmentPolygon(Point2d a, Point2d b, BeamInfo beam, GeometryFactory factory)
        {
            Vector2d dir = b - a;
            if (dir.Length <= 1e-9) return null;
            Vector2d u = dir.GetNormal();
            Vector2d perp = new Vector2d(-u.Y, u.X);
            double hw = beam.WidthCm / 2.0;
            ComputeBeamEdgeOffsets(beam.OffsetRaw, hw, out double upperEdge, out double lowerEdge);
            var coords = new[]
            {
                new Coordinate((a + perp.MultiplyBy(upperEdge)).X, (a + perp.MultiplyBy(upperEdge)).Y),
                new Coordinate((b + perp.MultiplyBy(upperEdge)).X, (b + perp.MultiplyBy(upperEdge)).Y),
                new Coordinate((b + perp.MultiplyBy(lowerEdge)).X, (b + perp.MultiplyBy(lowerEdge)).Y),
                new Coordinate((a + perp.MultiplyBy(lowerEdge)).X, (a + perp.MultiplyBy(lowerEdge)).Y),
                new Coordinate((a + perp.MultiplyBy(upperEdge)).X, (a + perp.MultiplyBy(upperEdge)).Y)
            };
            return factory.CreatePolygon(factory.CreateLinearRing(coords));
        }

        /// <summary>Segment listesinden kiriş birleşim geometrisi (Union); kolon/perde Difference ve küçük artık filtre uygular.</summary>
        private static Geometry BuildBeamGeometryFromSegments(List<(Point2d a, Point2d b)> segments, BeamInfo beam, GeometryFactory factory, Geometry kolonPerdeUnion)
        {
            if (segments == null || segments.Count == 0) return null;
            var polygons = new List<Geometry>();
            foreach (var (a, b) in segments)
            {
                var poly = BuildBeamSegmentPolygon(a, b, beam, factory);
                if (poly != null && !poly.IsEmpty) polygons.Add(poly);
            }
            if (polygons.Count == 0) return null;
            Geometry toDraw = polygons.Count == 1 ? polygons[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(polygons);
            if (toDraw != null && !toDraw.IsEmpty && kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty)
            {
                try
                {
                    var diff = toDraw.Difference(kolonPerdeUnion);
                    if (diff != null && !diff.IsEmpty)
                    {
                        toDraw = ReducePrecisionSafe(diff, 100);
                        if (toDraw == null || toDraw.IsEmpty) toDraw = diff;
                    }
                }
                catch { }
            }
            if (toDraw != null && !toDraw.IsEmpty)
            {
                const double minBeamRemnantAreaCm2 = 1200.0;
                toDraw = FilterSmallPolygons(toDraw, minBeamRemnantAreaCm2);
            }
            return toDraw;
        }

        /// <summary>Kolon/perde kesişiminden sonraki kiriş geometrilerinin (son hal) kesişim noktalarını ve o noktada kesişen iki kirişin ID'lerini döndürür. Sadece kirişin iki yan kenarı (eksene dik uç yüzler) kullanılır. X+Y kiriş çiftleri; nokta kolon/perde dışında; uç noktalar hariç 1 mm toleransla kenar içinde kalan kesişimler.</summary>
        private static List<(Point2d redPoint, int beamId1, int beamId2)> GetMarkerPointsFromFinalBeamGeometries(List<(int beamId, Geometry toDraw, int fixedAxisId)> finalGeometries, Geometry kolonPerdeUnion, GeometryFactory factory, int axisXMin = 1001, int axisXMax = 1999, int axisYMin = 2001, int axisYMax = 2999)
        {
            var pointByKey = new Dictionary<(int x, int y), (Point2d pt, int id1, int id2)>();
            for (int i = 0; i < finalGeometries.Count; i++)
            {
                int fixI = finalGeometries[i].fixedAxisId;
                bool iX = fixI >= axisXMin && fixI <= axisXMax;
                bool iY = fixI >= axisYMin && fixI <= axisYMax;
                var segsI = new List<(double ax, double ay, double bx, double by)>();
                GetLateralBoundarySegments(finalGeometries[i].toDraw, finalGeometries[i].fixedAxisId, segsI, axisXMin, axisXMax);
                for (int j = i + 1; j < finalGeometries.Count; j++)
                {
                    int fixJ = finalGeometries[j].fixedAxisId;
                    bool jX = fixJ >= axisXMin && fixJ <= axisXMax;
                    bool jY = fixJ >= axisYMin && fixJ <= axisYMax;
                    if (!((iX && jY) || (iY && jX))) continue;
                    var segsJ = new List<(double ax, double ay, double bx, double by)>();
                    GetLateralBoundarySegments(finalGeometries[j].toDraw, finalGeometries[j].fixedAxisId, segsJ, axisXMin, axisXMax);
                    foreach (var s1 in segsI)
                    foreach (var s2 in segsJ)
                    {
                        var pt = SegmentSegmentIntersection(s1.ax, s1.ay, s1.bx, s1.by, s2.ax, s2.ay, s2.bx, s2.by);
                        if (!pt.HasValue) continue;
                        const double endpointToleranceCm = 0.1; // 1 mm: uç noktalar hariç, sadece kenar içinde kalan kesişimler
                        double px = pt.Value.X, py = pt.Value.Y;
                        if (Math.Sqrt((px - s1.ax) * (px - s1.ax) + (py - s1.ay) * (py - s1.ay)) < endpointToleranceCm) continue;
                        if (Math.Sqrt((px - s1.bx) * (px - s1.bx) + (py - s1.by) * (py - s1.by)) < endpointToleranceCm) continue;
                        if (Math.Sqrt((px - s2.ax) * (px - s2.ax) + (py - s2.ay) * (py - s2.ay)) < endpointToleranceCm) continue;
                        if (Math.Sqrt((px - s2.bx) * (px - s2.bx) + (py - s2.by) * (py - s2.by)) < endpointToleranceCm) continue;
                        var key = ((int)Math.Round(px), (int)Math.Round(py));
                        if (!pointByKey.ContainsKey(key)) pointByKey[key] = (pt.Value, finalGeometries[i].beamId, finalGeometries[j].beamId);
                    }
                }
            }
            var result = new List<(Point2d redPoint, int beamId1, int beamId2)>();
            foreach (var kv in pointByKey)
            {
                var pt = factory.CreatePoint(new Coordinate(kv.Value.pt.X, kv.Value.pt.Y));
                if (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty && kolonPerdeUnion.Contains(pt)) continue;
                result.Add((kv.Value.pt, kv.Value.id1, kv.Value.id2));
            }
            return result;
        }

        /// <summary>X aksı kirişi ile Y aksı kirişinin kenar çizgileri (kesit dikdörtgenleri) kesişen ve bu noktada kolon/perde/3. kiriş olmayan noktaları döndürür. Aynı ID'li kirişlerin içinde kalan kenarlar dahil edilmez; sadece her kirişin başlangıç/bitiş uçları sayılır. (Kolon/perde öncesi geometri; işaretler artık son geometriye göre çiziliyor.)</summary>
        private List<Point2d> GetTwoBeamCornerPointsForMarker(List<BeamInfo> beamsForDrawing, int floorNo, double offsetX, double offsetY, Geometry kolonPerdeUnion, GeometryFactory factory)
        {
            if (beamsForDrawing == null || beamsForDrawing.Count == 0) return new List<Point2d>();
            const int axisXMin = 1001, axisXMax = 1999;
            const int axisYMin = 2001, axisYMax = 2999;
            var pointToBeams = new Dictionary<(int x, int y), List<(BeamInfo beam, Point2d otherEnd)>>();
            var endpointCountByBeam = new Dictionary<(int beamId, int x, int y), int>();
            foreach (var beam in beamsForDrawing)
            {
                if (GetBeamFloorNo(beam.BeamId) != floorNo) continue;
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1)) continue;
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2)) continue;
                var a = new Point2d(p1.X + offsetX, p1.Y + offsetY);
                var b = new Point2d(p2.X + offsetX, p2.Y + offsetY);
                var keyA = ((int)Math.Round(a.X), (int)Math.Round(a.Y));
                var keyB = ((int)Math.Round(b.X), (int)Math.Round(b.Y));
                var kA = (beam.BeamId, keyA.Item1, keyA.Item2);
                var kB = (beam.BeamId, keyB.Item1, keyB.Item2);
                if (!endpointCountByBeam.ContainsKey(kA)) endpointCountByBeam[kA] = 0;
                endpointCountByBeam[kA]++;
                if (!endpointCountByBeam.ContainsKey(kB)) endpointCountByBeam[kB] = 0;
                endpointCountByBeam[kB]++;
                if (!pointToBeams.ContainsKey(keyA)) pointToBeams[keyA] = new List<(BeamInfo, Point2d)>();
                pointToBeams[keyA].Add((beam, b));
                if (!pointToBeams.ContainsKey(keyB)) pointToBeams[keyB] = new List<(BeamInfo, Point2d)>();
                pointToBeams[keyB].Add((beam, a));
            }
            var points = new List<Point2d>();
            foreach (var kv in pointToBeams)
            {
                var list = kv.Value.GroupBy(x => x.beam.BeamId).Select(g => g.First()).ToList();
                if (list.Count != 2) continue;
                var (beam1, other1) = list[0];
                var (beam2, other2) = list[1];
                int keyX = kv.Key.Item1, keyY = kv.Key.Item2;
                int c1, c2;
                if (!endpointCountByBeam.TryGetValue((beam1.BeamId, keyX, keyY), out c1) || c1 != 1) continue;
                if (!endpointCountByBeam.TryGetValue((beam2.BeamId, keyX, keyY), out c2) || c2 != 1) continue;
                int fix1 = beam1.FixedAxisId, fix2 = beam2.FixedAxisId;
                bool oneX = (fix1 >= axisXMin && fix1 <= axisXMax) || (fix2 >= axisXMin && fix2 <= axisXMax);
                bool oneY = (fix1 >= axisYMin && fix1 <= axisYMax) || (fix2 >= axisYMin && fix2 <= axisYMax);
                if (!oneX || !oneY) continue;
                var pt = factory.CreatePoint(new Coordinate(kv.Key.x, kv.Key.y));
                if (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty && kolonPerdeUnion.Contains(pt)) continue;
                Point2d corner = new Point2d(kv.Key.x, kv.Key.y);
                Geometry poly1 = BuildBeamStubPolygon(corner, other1, beam1, factory);
                Geometry poly2 = BuildBeamStubPolygon(corner, other2, beam2, factory);
                if (poly1 == null || poly2 == null || poly1.IsEmpty || poly2.IsEmpty || !poly1.Intersects(poly2))
                    continue;
                points.Add(corner);
            }
            return points;
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

        /// <summary>Difference sonucu 2 veya daha fazla poligona ayrıldıysa sadece en büyük alanlı poligonu döndürür; tek poligon ise aynen döner.</summary>
        private static Geometry KeepLargestPolygon(Geometry geom)
        {
            if (geom == null || geom.IsEmpty) return null;
            if (geom is Polygon p)
                return p;
            var polygons = new List<Polygon>();
            if (geom is MultiPolygon mp)
            {
                for (int i = 0; i < mp.NumGeometries; i++)
                    if (mp.GetGeometryN(i) is Polygon pg) polygons.Add(pg);
            }
            else if (geom is NetTopologySuite.Geometries.GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                    if (gc.GetGeometryN(i) is Polygon pg) polygons.Add(pg);
            }
            if (polygons.Count == 0) return null;
            if (polygons.Count == 1) return polygons[0];
            Polygon largest = polygons[0];
            double maxArea = largest.Area;
            for (int i = 1; i < polygons.Count; i++)
            {
                double a = polygons[i].Area;
                if (a > maxArea) { maxArea = a; largest = polygons[i]; }
            }
            return largest;
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
            var factory = _ntsDrawFactory;
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
            var factory = _ntsDrawFactory;
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
            var factory = _ntsDrawFactory;
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
            var factory = _ntsDrawFactory;
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
            var factory = _ntsDrawFactory;
            var wallList = new List<(Geometry poly, int fixedAxisId, BeamInfo beam, Point2d a, Point2d b)>();
            Geometry kolonPerdeUnion = BuildKolonPerdeUnion(floor, offsetX, offsetY);
            var kolonPerdeSafe = (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty) ? EnsureBoundarySafe(kolonPerdeUnion, _ntsDrawFactory) : null;
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

            // Uzatma kapalı.
            Dictionary<(int x, int y), Dictionary<(int beamId, double p1, double p2), double>> twoBeamCornerExtendCm = null;
            var finalBeamGeometries = new List<(int beamId, Geometry toDraw, int fixedAxisId)>();
            var beamSegmentData = new Dictionary<int, (List<(Point2d a, Point2d b)> segments, BeamInfo firstBeam)>();

            // Kirişler: aynı (BeamId, kot) grubundaki segmentler tek birleşik geometri olarak çizilir. Farklı kottakiler ayrı çizilir. Önce geometri üretilir ve segment verisi hafızaya alınır.
            var beamLabelInfos = new List<(int beamId, Geometry drawnGeometry, BeamInfo firstBeam, Point2d firstA, Point2d firstB, double minSegmentLengthCm)>();
            var beamsByKey = beamsForDrawing.GroupBy(b => (b.BeamId, b.Point1KotCm, b.Point2KotCm)).ToList();
            int nextBeamId = 0;
            foreach (var group in beamsByKey)
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
                    Point2d a0Axis = a;
                    Point2d b0Axis = b;
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
                    // İki konsol kiriş köşesi: uçta sadece bu iki kiriş (kolon/perde/3. kiriş yok), kiriş ucunu diğer kirişin İÇ kenarına kadar uzat (işaret hizası 20cm). Her iki kiriş de uzatılır (kolon çekmesi bu ucu bozsa bile).
                    if (twoBeamCornerExtendCm != null)
                    {
                        var keyA = ((int)Math.Round(a0Axis.X), (int)Math.Round(a0Axis.Y));
                        var keyB = ((int)Math.Round(b0Axis.X), (int)Math.Round(b0Axis.Y));
                        double len = dir.Length;
                        var beamKey = (group.First().BeamId, group.First().Point1KotCm, group.First().Point2KotCm);
                        if (twoBeamCornerExtendCm.TryGetValue(keyA, out var mapA) && mapA.TryGetValue(beamKey, out double extA) && extA > 0)
                        {
                            extA = Math.Min(extA, len * 0.5);
                            a = a0Axis - u.MultiplyBy(extA);
                        }
                        if (twoBeamCornerExtendCm.TryGetValue(keyB, out var mapB) && mapB.TryGetValue(beamKey, out double extB) && extB > 0)
                        {
                            extB = Math.Min(extB, len * 0.5);
                            b = b0Axis + u.MultiplyBy(extB);
                        }
                        dir = b - a;
                        if (dir.Length <= 1e-9) continue;
                        u = dir.GetNormal();
                    }
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
                // Kirişleri kolon ve perde sınırlarında kes (Difference)
                if (toDraw != null && !toDraw.IsEmpty && kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty)
                {
                    try
                    {
                        var diff = toDraw.Difference(kolonPerdeUnion);
                        if (diff != null && !diff.IsEmpty)
                        {
                            toDraw = ReducePrecisionSafe(diff, 100);
                            if (toDraw == null || toDraw.IsEmpty) toDraw = diff;
                        }
                    }
                    catch { }
                }
                // Üçgen ve 1200 cm² altı kiriş artıklarını temizle
                if (toDraw != null && !toDraw.IsEmpty)
                {
                    const double minBeamRemnantAreaCm2 = 1200.0;
                    toDraw = FilterSmallPolygons(toDraw, minBeamRemnantAreaCm2);
                }
                if (toDraw != null && !toDraw.IsEmpty)
                {
                    finalBeamGeometries.Add((nextBeamId, toDraw, group.First().FixedAxisId));
                    beamSegmentData[nextBeamId] = (new List<(Point2d a, Point2d b)>(segmentEndpoints), group.First());
                    if (firstAlignedA.HasValue && firstAlignedB.HasValue)
                    {
                        double minSeg = segmentEndpoints.Count > 0 ? segmentEndpoints.Min(s => (s.b - s.a).Length) : (firstAlignedB.Value - firstAlignedA.Value).Length;
                        beamLabelInfos.Add((nextBeamId, toDraw, group.First(), firstAlignedA.Value, firstAlignedB.Value, minSeg));
                    }
                    nextBeamId++;
                }
            }
            // Kiriş birleştirme: kolon-perde kesimi işleminden hemen sonra; uzatma öncesi geometrilerle yapılır.
            var kirisKolonPerdeKesimSonrasi = finalBeamGeometries.Select(x => (x.beamId, x.toDraw, x.fixedAxisId)).ToList();
            // İşaret noktalarını hafızaya al; sadece kırmızı işaretin kestiği kirişleri diğer kirişin mavi uçlarına (izdüşümüne) kadar uzat, sonra çiz.
            const int axisXMin = 1001, axisXMax = 1999, axisYMin = 2001, axisYMax = 2999;
            var twoBeamCornerData = GetMarkerPointsFromFinalBeamGeometries(finalBeamGeometries, kolonPerdeUnion, factory, axisXMin, axisXMax, axisYMin, axisYMax);
            bool isInsideOtherBeam(Point2d pt, int excludeBeamId, List<(int beamId, Geometry toDraw, int fixedAxisId)> geoms)
            {
                var ptNts = factory.CreatePoint(new Coordinate(pt.X, pt.Y));
                foreach (var g in geoms)
                    if (g.beamId != excludeBeamId && g.toDraw != null && !g.toDraw.IsEmpty && g.toDraw.Contains(ptNts))
                        return true;
                return false;
            }
            var redCornerWithBlue = new List<(Point2d redPoint, int beamIdX, int beamIdY, List<Point2d> blueX, List<Point2d> blueY)>();
            var beamGeomById = finalBeamGeometries.ToDictionary(g => g.beamId, g => (g.toDraw, g.fixedAxisId));
            foreach (var (redPoint, beamId1, beamId2) in twoBeamCornerData)
            {
                int fix1 = beamGeomById.TryGetValue(beamId1, out var g1) ? g1.fixedAxisId : 0;
                int fix2 = beamGeomById.TryGetValue(beamId2, out var g2) ? g2.fixedAxisId : 0;
                bool fix1X = fix1 >= axisXMin && fix1 <= axisXMax;
                bool fix2Y = fix2 >= axisYMin && fix2 <= axisYMax;
                int beamIdX = fix1X ? beamId1 : beamId2;
                int beamIdY = fix1X ? beamId2 : beamId1;
                var blueVerticesX = new List<Point2d>();
                var blueVerticesY = new List<Point2d>();
                if (beamGeomById.TryGetValue(beamIdX, out var tx))
                {
                    foreach (var pt in GetCornerEndVerticesOfBeamFromCorner(tx.toDraw, tx.fixedAxisId, redPoint, axisXMin, axisXMax))
                        if (!isInsideOtherBeam(pt, beamIdX, finalBeamGeometries))
                            blueVerticesX.Add(pt);
                }
                if (beamGeomById.TryGetValue(beamIdY, out var ty))
                {
                    foreach (var pt in GetCornerEndVerticesOfBeamFromCorner(ty.toDraw, ty.fixedAxisId, redPoint, axisXMin, axisXMax))
                        if (!isInsideOtherBeam(pt, beamIdY, finalBeamGeometries))
                            blueVerticesY.Add(pt);
                }
                redCornerWithBlue.Add((redPoint, beamIdX, beamIdY, blueVerticesX, blueVerticesY));
            }
            const double redPointToleranceCm = 2.0;
            var extendedSegments = new Dictionary<int, List<(Point2d a, Point2d b)>>();
            foreach (var kv in beamSegmentData)
                extendedSegments[kv.Key] = kv.Value.segments.Select(s => (s.a, s.b)).ToList();
            // X aksına bağlı kiriş (plan görünüşte genelde dikey): diğer kiriş (Y aksı) üzerindeki mavi noktaya kadar Y yönünde uzatılır → targetY = blueY.Y, endpoint (sameX, targetY).
            // Y aksına bağlı kiriş (plan görünüşte genelde yatay): diğer kiriş (X aksı) üzerindeki mavi noktaya kadar X yönünde uzatılır → targetX = blueX.X, endpoint (targetX, sameY).
            foreach (var (redPoint, beamIdX, beamIdY, blueX, blueY) in redCornerWithBlue)
            {
                double rx = redPoint.X, ry = redPoint.Y;
                if (blueY.Count > 0 && extendedSegments.TryGetValue(beamIdX, out var segsX))
                {
                    double targetY = (blueY.Max(p => p.Y) + blueY.Min(p => p.Y)) / 2.0;
                    if (blueY.Count == 1) targetY = blueY[0].Y;
                    else
                    {
                        bool xBeamPositiveY = false;
                        foreach (var (a, b) in segsX)
                        {
                            if (Math.Sqrt((a.X - rx) * (a.X - rx) + (a.Y - ry) * (a.Y - ry)) < redPointToleranceCm)
                                xBeamPositiveY = b.Y > a.Y;
                            else if (Math.Sqrt((b.X - rx) * (b.X - rx) + (b.Y - ry) * (b.Y - ry)) < redPointToleranceCm)
                                xBeamPositiveY = a.Y > b.Y;
                            else continue;
                            break;
                        }
                        targetY = xBeamPositiveY ? blueY.Max(p => p.Y) : blueY.Min(p => p.Y);
                    }
                    for (int i = 0; i < segsX.Count; i++)
                    {
                        var (a, b) = segsX[i];
                        bool aAtRed = Math.Sqrt((a.X - rx) * (a.X - rx) + (a.Y - ry) * (a.Y - ry)) < redPointToleranceCm;
                        bool bAtRed = Math.Sqrt((b.X - rx) * (b.X - rx) + (b.Y - ry) * (b.Y - ry)) < redPointToleranceCm;
                        Point2d na = a, nb = b;
                        if (aAtRed) na = new Point2d(a.X, targetY);
                        if (bAtRed) nb = new Point2d(b.X, targetY);
                        if (aAtRed || bAtRed) segsX[i] = (na, nb);
                    }
                }
                if (blueX.Count > 0 && extendedSegments.TryGetValue(beamIdY, out var segsY))
                {
                    double targetX = (blueX.Max(p => p.X) + blueX.Min(p => p.X)) / 2.0;
                    if (blueX.Count == 1) targetX = blueX[0].X;
                    else
                    {
                        bool yBeamPositiveX = false;
                        foreach (var (a, b) in segsY)
                        {
                            if (Math.Sqrt((a.X - rx) * (a.X - rx) + (a.Y - ry) * (a.Y - ry)) < redPointToleranceCm)
                                yBeamPositiveX = b.X > a.X;
                            else if (Math.Sqrt((b.X - rx) * (b.X - rx) + (b.Y - ry) * (b.Y - ry)) < redPointToleranceCm)
                                yBeamPositiveX = a.X > b.X;
                            else continue;
                            break;
                        }
                        targetX = yBeamPositiveX ? blueX.Max(p => p.X) : blueX.Min(p => p.X);
                    }
                    for (int i = 0; i < segsY.Count; i++)
                    {
                        var (a, b) = segsY[i];
                        bool aAtRed = Math.Sqrt((a.X - rx) * (a.X - rx) + (a.Y - ry) * (a.Y - ry)) < redPointToleranceCm;
                        bool bAtRed = Math.Sqrt((b.X - rx) * (b.X - rx) + (b.Y - ry) * (b.Y - ry)) < redPointToleranceCm;
                        Point2d na = a, nb = b;
                        if (aAtRed) na = new Point2d(targetX, a.Y);
                        if (bAtRed) nb = new Point2d(targetX, b.Y);
                        if (aAtRed || bAtRed) segsY[i] = (na, nb);
                    }
                }
            }
            var beamsExtended = new HashSet<int>();
            foreach (var (redPoint, beamIdX, beamIdY, _, _) in redCornerWithBlue)
            {
                beamsExtended.Add(beamIdX);
                beamsExtended.Add(beamIdY);
            }
            for (int i = 0; i < finalBeamGeometries.Count; i++)
            {
                var (beamId, _, fixedAxisId) = finalBeamGeometries[i];
                if (beamsExtended.Contains(beamId) && beamSegmentData.TryGetValue(beamId, out var segData) && extendedSegments.TryGetValue(beamId, out var segs))
                {
                    Geometry toDrawExt = BuildBeamGeometryFromSegments(segs, segData.firstBeam, factory, kolonPerdeUnion);
                    if (toDrawExt != null && !toDrawExt.IsEmpty)
                        finalBeamGeometries[i] = (beamId, toDrawExt, fixedAxisId);
                }
            }
            var finalGeomByBeamId = finalBeamGeometries.ToDictionary(g => g.beamId, g => g.toDraw);
            for (int i = 0; i < beamLabelInfos.Count; i++)
            {
                var (beamId, _, firstBeam, firstA, firstB, minSeg) = beamLabelInfos[i];
                if (finalGeomByBeamId.TryGetValue(beamId, out Geometry drawnGeom) && drawnGeom != null)
                    beamLabelInfos[i] = (beamId, drawnGeom, firstBeam, firstA, firstB, minSeg);
            }
            // Kirişleri çizimde görüldüğü hali (kesim + uzatma sonrası) ile birleştir: temas eden parçaları Union ile tek geometri yapıp çiz.
            var beamPieces = new List<Geometry>();
            foreach (var (_, toDraw, _) in finalBeamGeometries)
            {
                if (toDraw == null || toDraw.IsEmpty) continue;
                AddPolygonsToList(toDraw, beamPieces);
            }
            if (beamPieces.Count > 0)
            {
                const double touchToleranceCm = 0.2;
                var beamMergeBoundarySafe = (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty) ? EnsureBoundarySafe(kolonPerdeUnion, factory) : null;
                Geometry beamMergeBoundary = (beamMergeBoundarySafe != null && !beamMergeBoundarySafe.IsEmpty) ? beamMergeBoundarySafe.Boundary : null;
                int n = beamPieces.Count;
                var parent = new int[n];
                for (int i = 0; i < n; i++) parent[i] = i;
                int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
                void Merge(int x, int y) { parent[Find(x)] = Find(y); }
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                        if (ProperlyTouches(beamPieces[i], beamPieces[j], touchToleranceCm, beamMergeBoundary)
                            || ShareAtLeastTwoVertices(beamPieces[i], beamPieces[j], touchToleranceCm))
                            Merge(i, j);
                var componentGroups = new Dictionary<int, List<Geometry>>();
                for (int i = 0; i < n; i++)
                {
                    int root = Find(i);
                    if (!componentGroups.ContainsKey(root)) componentGroups[root] = new List<Geometry>();
                    componentGroups[root].Add(beamPieces[i]);
                }
                _drawnBeamGeometriesForSlabCut = new List<Geometry>();
                foreach (var list in componentGroups.Values)
                {
                    Geometry part = list.Count == 1 ? list[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(list);
                    if (part != null && !part.IsEmpty)
                    {
                        _drawnBeamGeometriesForSlabCut.Add(part);
                        DrawGeometryRingsAsPolylines(tr, btr, part, LayerKiris, addHatch: false, exteriorRingsOnly: false, applySmallTriangleTrim: false);
                    }
                }
            }
            else
                _drawnBeamGeometriesForSlabCut = null;
            // İşaretler (kırmızı/mavi daire) çizilmiyor; uzatma mantığı aynen uygulanıyor.
            var wallLabelInfos = new List<(int beamId, Geometry drawnGeometry, BeamInfo beam, Point2d firstA, Point2d firstB)>();
            _drawnWallGeometriesForSlabCut = new List<Geometry>();
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
                        _drawnWallGeometriesForSlabCut.Add(toDraw);
                        DrawGeometryRingsAsPolylines(tr, btr, toDraw, LayerPerde, addHatch: true, hatchAngleRad: GetAxisAngleRad(fixedAxisId), applySmallTriangleTrim: false);
                        wallLabelInfos.Add((beam.BeamId, toDraw, beam, a, b));
                    }
                }
            }

            // Perde etiketleri (isim) çizilir; perde ID yazıları çizilmez (wallLabelInfos üzerinden DrawBeamLabel ile isim yazılıyor).

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
            int maxBeamNumero = beamLabelInfos.Count > 0 ? beamLabelInfos.Max(x => GetBeamNumero(x.firstBeam.BeamId)) : 0;
            int beamPad = GetLabelPadWidth(maxBeamNumero);
            foreach (var (beamId, drawnGeometry, firstBeam, firstA, firstB, _) in beamLabelInfos)
            {
                Vector2d dir = firstB - firstA;
                if (dir.Length <= 1e-9) continue;
                Vector2d u = dir.GetNormal();
                Vector2d perp = new Vector2d(-u.Y, u.X);

                if (!GetBeamDrawnCorners(drawnGeometry, firstA, u, perp, out Point2d rectBottomLeft, out Point2d rectUpperRight, out Point2d rectBottomRight))
                    continue;

                int beamFloor = GetBeamFloorNo(firstBeam.BeamId);
                var floorInfo = _model.Floors.FirstOrDefault(f => f.FloorNo == beamFloor);
                string katEtiketi = floorInfo?.ShortName ?? beamFloor.ToString(CultureInfo.InvariantCulture);
                int beamNumero = GetBeamNumero(firstBeam.BeamId);
                string beamNumeroStr = beamNumero.ToString("D" + beamPad, CultureInfo.InvariantCulture);
                string labelText = string.Format(CultureInfo.InvariantCulture, "K{0}{1} ({2}/{3})",
                    katEtiketi, beamNumeroStr, (int)Math.Round(firstBeam.WidthCm), (int)Math.Round(firstBeam.HeightCm));

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

                // Kot takımı: sadece kiriş üst kotu kat kotundan farklıysa; birleştirilmeden önceki poligon (drawnGeometry) merkezine ortalı; kirişin fixlendiği aksa göre döndürülür
                if (firstBeam.Point1KotCm != 0 || firstBeam.Point2KotCm != 0)
                {
                    double floorElevM = floorInfo != null ? floorInfo.ElevationM : 0.0;
                    double topElevM = _model.BuildingBaseKotu + floorElevM + (Math.Max(firstBeam.Point1KotCm, firstBeam.Point2KotCm) / 100.0);
                    double heightCm = firstBeam.HeightCm > 0 ? firstBeam.HeightCm : 30.0;
                    double bottomElevM = topElevM - heightCm / 100.0;
                    Point2d kotCenter = drawnGeometry.Centroid != null && !drawnGeometry.Centroid.IsEmpty
                        ? new Point2d(drawnGeometry.Centroid.X, drawnGeometry.Centroid.Y)
                        : new Point2d((firstA.X + firstB.X) * 0.5, (firstA.Y + firstB.Y) * 0.5);
                    double axisAngleRad = GetAxisAngleRad(firstBeam.FixedAxisId);
                    DrawKotBlockAtCenter(tr, btr, db, kotCenter.X, kotCenter.Y, topElevM, bottomElevM, axisAngleRad);
                }
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
            int maxWallNumero = wallLabelInfos.Count > 0 ? wallLabelInfos.Max(x => GetBeamNumero(x.beamId)) : 0;
            int wallPad = GetLabelPadWidth(maxWallNumero);
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
                string beamNumeroStr = beamNumero.ToString("D" + wallPad, CultureInfo.InvariantCulture);
                Point2d wallCenter = drawnGeometry.Centroid != null && !drawnGeometry.Centroid.IsEmpty
                    ? new Point2d(drawnGeometry.Centroid.X, drawnGeometry.Centroid.Y)
                    : new Point2d((firstA.X + firstB.X) * 0.5, (firstA.Y + firstB.Y) * 0.5);
                double perdeLengthCm = GetPerdeLengthCm(wallCenter, u, kolonUnionForWalls, factory, firstA.GetDistanceTo(firstB));
                const double perdeLabelMinLengthForDimensionsCm = 160.0;
                string labelText = perdeLengthCm < perdeLabelMinLengthForDimensionsCm
                    ? string.Format(CultureInfo.InvariantCulture, "P{0}{1}", katEtiketi, beamNumeroStr)
                    : string.Format(CultureInfo.InvariantCulture, "P{0}{1} ({2}/{3})",
                        katEtiketi, beamNumeroStr, (int)Math.Round(beam.WidthCm), (int)Math.Round(perdeLengthCm));
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

                // Kot takımı: sadece perde üst kotu kat kotundan farklıysa; poligon merkezine ortalı; perdenin fixlendiği aksa göre döndürülür
                if (beam.Point1KotCm != 0 || beam.Point2KotCm != 0)
                {
                    var floorInfoPerde = _model.Floors.FirstOrDefault(f => f.FloorNo == beamFloor);
                    double floorElevM = floorInfoPerde != null ? floorInfoPerde.ElevationM : 0.0;
                    double topElevM = _model.BuildingBaseKotu + floorElevM + (Math.Max(beam.Point1KotCm, beam.Point2KotCm) / 100.0);
                    double heightCm = beam.HeightCm > 0 ? beam.HeightCm : 30.0;
                    double bottomElevM = topElevM - heightCm / 100.0;
                    Point2d kotCenter = drawnGeometry.Centroid != null && !drawnGeometry.Centroid.IsEmpty
                        ? new Point2d(drawnGeometry.Centroid.X, drawnGeometry.Centroid.Y)
                        : new Point2d((firstA.X + firstB.X) * 0.5, (firstA.Y + firstB.Y) * 0.5);
                    double axisAngleRad = GetAxisAngleRad(beam.FixedAxisId);
                    DrawKotBlockAtCenter(tr, btr, db, kotCenter.X, kotCenter.Y, topElevM, bottomElevM, axisAngleRad);
                }
            }
        }

        /// <summary>Verilen kattaki perde isimlerini (P{kat}{no} eni/uzunluk) çizer. Temel planında ilk kat perdeleri için kullanılır.</summary>
        /// <param name="kolonPerdeUnionForObstacles">Temel planında engel alanı için (kiriş yok); null ise BuildKolonPerdeUnion ile hesaplanır.</param>
        private void DrawPerdeLabelsForFloor(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY, Geometry kolonPerdeUnionForObstacles = null)
        {
            var factory = _ntsDrawFactory;
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
            int maxWallNumeroFloor = wallLabelInfos.Count > 0 ? wallLabelInfos.Max(x => GetBeamNumero(x.beamId)) : 0;
            int wallPadFloor = GetLabelPadWidth(maxWallNumeroFloor);
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
                string beamNumeroStr = beamNumero.ToString("D" + wallPadFloor, CultureInfo.InvariantCulture);
                Point2d wallCenter = drawnGeometry.Centroid != null && !drawnGeometry.Centroid.IsEmpty
                    ? new Point2d(drawnGeometry.Centroid.X, drawnGeometry.Centroid.Y)
                    : new Point2d((firstA.X + firstB.X) * 0.5, (firstA.Y + firstB.Y) * 0.5);
                double perdeLengthCm = GetPerdeLengthCm(wallCenter, u, kolonUnionForWalls, factory, firstA.GetDistanceTo(firstB));
                const double perdeLabelMinLengthForDimensionsCm = 160.0;
                string labelText = perdeLengthCm < perdeLabelMinLengthForDimensionsCm
                    ? string.Format(CultureInfo.InvariantCulture, "P{0}{1}", katEtiketi, beamNumeroStr)
                    : string.Format(CultureInfo.InvariantCulture, "P{0}{1} ({2}/{3})",
                        katEtiketi, beamNumeroStr, (int)Math.Round(beam.WidthCm), (int)Math.Round(perdeLengthCm));
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

                // Kot takımı: sadece perde üst kotu kat kotundan farklıysa; poligon merkezine ortalı; perdenin fixlendiği aksa göre döndürülür
                if (beam.Point1KotCm != 0 || beam.Point2KotCm != 0)
                {
                    double floorElevM = floor != null ? floor.ElevationM : 0.0;
                    double topElevM = _model.BuildingBaseKotu + floorElevM + (Math.Max(beam.Point1KotCm, beam.Point2KotCm) / 100.0);
                    double heightCm = beam.HeightCm > 0 ? beam.HeightCm : 30.0;
                    double bottomElevM = topElevM - heightCm / 100.0;
                    Point2d kotCenter = drawnGeometry.Centroid != null && !drawnGeometry.Centroid.IsEmpty
                        ? new Point2d(drawnGeometry.Centroid.X, drawnGeometry.Centroid.Y)
                        : new Point2d((firstA.X + firstB.X) * 0.5, (firstA.Y + firstB.Y) * 0.5);
                    double axisAngleRad = GetAxisAngleRad(beam.FixedAxisId);
                    DrawKotBlockAtCenter(tr, btr, db, kotCenter.X, kotCenter.Y, topElevM, bottomElevM, axisAngleRad);
                }
            }
        }

        /// <summary>
        /// Bu kata ait döşemeler: kolon, perde ve kirişlerin çizimde görüldüğü geometrilerden kesilir; kesim sonucu 2+ poligon olursa sadece en büyük parça çizilir. Merdiven ayrıca MERDIVEN katmanında da çizilir.
        /// Köşeler sırayla: (axis1,axis3), (axis1,axis4), (axis2,axis4), (axis2,axis3).
        /// </summary>
        private void DrawSlabs(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY)
        {
            int floorNo = floor.FloorNo;
            var factory = _ntsDrawFactory;
            _drawnSlabGeometriesForUnion = new List<Geometry>();
            // Çizimde görüldüğü haliyle kolon + perde + kiriş birleşimi (DrawBeamsAndWalls tarafından doldurulur)
            Geometry drawnKolonPerdeKirisUnion = BuildKolonUnionSameFloorOnly(floor, offsetX, offsetY);
            if (_drawnWallGeometriesForSlabCut != null && _drawnWallGeometriesForSlabCut.Count > 0)
            {
                Geometry wallsUnion = _drawnWallGeometriesForSlabCut.Count == 1
                    ? _drawnWallGeometriesForSlabCut[0]
                    : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(_drawnWallGeometriesForSlabCut);
                if (wallsUnion != null && !wallsUnion.IsEmpty)
                    drawnKolonPerdeKirisUnion = drawnKolonPerdeKirisUnion != null && !drawnKolonPerdeKirisUnion.IsEmpty
                        ? drawnKolonPerdeKirisUnion.Union(wallsUnion)
                        : wallsUnion;
            }
            if (_drawnBeamGeometriesForSlabCut != null && _drawnBeamGeometriesForSlabCut.Count > 0)
            {
                Geometry beamsUnion = _drawnBeamGeometriesForSlabCut.Count == 1
                    ? _drawnBeamGeometriesForSlabCut[0]
                    : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(_drawnBeamGeometriesForSlabCut);
                if (beamsUnion != null && !beamsUnion.IsEmpty)
                    drawnKolonPerdeKirisUnion = drawnKolonPerdeKirisUnion != null && !drawnKolonPerdeKirisUnion.IsEmpty
                        ? drawnKolonPerdeKirisUnion.Union(beamsUnion)
                        : beamsUnion;
            }

            var slabsOnFloor = _model.Slabs.Where(s => GetSlabFloorNo(s.SlabId) == floorNo).ToList();
            int maxSlabNumero = slabsOnFloor.Count > 0 ? slabsOnFloor.Max(s => GetSlabNumero(s.SlabId)) : 0;
            int slabPad = GetLabelPadWidth(maxSlabNumero);
            string storyId = floor != null && !string.IsNullOrEmpty(floor.ShortName) ? floor.ShortName : (floor?.FloorNo.ToString(CultureInfo.InvariantCulture) ?? "B");
            Database db = btr.Database;
            ObjectId slabLabelStyleId = GetOrCreateYaziBeykentTextStyle(tr, db);

            var slabRecords = new List<(SlabInfo slab, Geometry toDraw, Point2d center)>();
            var labelSlabsNoGeometry = new HashSet<int>();
            foreach (var slab in _model.Slabs)
            {
                if (GetSlabFloorNo(slab.SlabId) != floorNo) continue;
                Geometry toDraw;
                Point2d center;
                if (!TryGetSlabGeometry(slab, floorNo, offsetX, offsetY, factory, drawnKolonPerdeKirisUnion, out toDraw, out center))
                    continue;
                if (toDraw == null || toDraw.IsEmpty)
                {
                    if (!_model.StairSlabIds.Contains(slab.SlabId))
                        labelSlabsNoGeometry.Add(slab.SlabId);
                    continue;
                }
                slabRecords.Add((slab, toDraw, center));
            }
            // Kat sınırı: kolon+perde+kiriş birleşimi ile tüm döşeme geometrilerinin birleşiminin dış sınırı (bitişik döşeme etiket kuralı için)
            Geometry floorBoundary = null;
            try
            {
                Geometry floorUnion = drawnKolonPerdeKirisUnion;
                foreach (var (_, toDraw, _) in slabRecords)
                {
                    if (toDraw == null || toDraw.IsEmpty) continue;
                    floorUnion = (floorUnion != null && !floorUnion.IsEmpty) ? floorUnion.Union(toDraw) : toDraw;
                }
                floorBoundary = floorUnion?.Boundary;
            }
            catch { }
            HashSet<int> slabIdsToLabel = ComputeSlabIdsToLabel(slabRecords, floor, floorBoundary);
            foreach (int id in labelSlabsNoGeometry)
                slabIdsToLabel.Add(id);

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

                var coords = new Coordinate[pts.Length + 1];
                for (int i = 0; i < pts.Length; i++)
                    coords[i] = new Coordinate(pts[i].X, pts[i].Y);
                coords[pts.Length] = coords[0];
                var slabPoly = factory.CreatePolygon(factory.CreateLinearRing(coords));
                if (slabPoly == null || slabPoly.IsEmpty)
                {
                    if (labelSlabsNoGeometry.Contains(slab.SlabId))
                    {
                        double cx = 0, cy = 0;
                        for (int i = 0; i < pts.Length; i++) { cx += pts[i].X; cy += pts[i].Y; }
                        AppendSlabLabel(tr, btr, slab, floor, storyId, slabPad, new Point2d(cx / pts.Length, cy / pts.Length), slabLabelStyleId);
                    }
                    continue;
                }

                Geometry toDraw = slabPoly;
                if (drawnKolonPerdeKirisUnion != null && !drawnKolonPerdeKirisUnion.IsEmpty)
                {
                    try
                    {
                        var diff = slabPoly.Difference(drawnKolonPerdeKirisUnion);
                        if (diff != null && !diff.IsEmpty)
                        {
                            toDraw = KeepLargestPolygon(diff);
                            if (toDraw == null) toDraw = diff;
                        }
                    }
                    catch { }
                }

                if (toDraw != null && !toDraw.IsEmpty)
                {
                    _drawnSlabGeometriesForUnion.Add(toDraw);
                    if (isStair)
                        DrawGeometryRingsAsPolylines(tr, btr, toDraw, LayerMerdiven, addHatch: false, applySmallTriangleTrim: false);
                }

                double cx2 = 0, cy2 = 0;
                for (int i = 0; i < pts.Length; i++) { cx2 += pts[i].X; cy2 += pts[i].Y; }
                var center = new Point2d(cx2 / pts.Length, cy2 / pts.Length);
                if (!isStair && slabIdsToLabel.Contains(slab.SlabId))
                    AppendSlabLabel(tr, btr, slab, floor, storyId, slabPad, center, slabLabelStyleId);
            }
        }

        /// <summary>Döşeme dikdörtgeni ve kolon/perde/kiriş farkı; toDraw ve merkez döner. Geçersizse false.</summary>
        private bool TryGetSlabGeometry(SlabInfo slab, int floorNo, double offsetX, double offsetY, GeometryFactory factory, Geometry drawnKolonPerdeKirisUnion, out Geometry toDraw, out Point2d center)
        {
            toDraw = null;
            center = default;
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
            if (pts == null || pts.Length < 3) return false;
            double cx = 0, cy = 0;
            for (int i = 0; i < pts.Length; i++) { cx += pts[i].X; cy += pts[i].Y; }
            center = new Point2d(cx / pts.Length, cy / pts.Length);
            var coords = new Coordinate[pts.Length + 1];
            for (int i = 0; i < pts.Length; i++)
                coords[i] = new Coordinate(pts[i].X, pts[i].Y);
            coords[pts.Length] = coords[0];
            var slabPoly = factory.CreatePolygon(factory.CreateLinearRing(coords));
            if (slabPoly == null || slabPoly.IsEmpty) return true;
            toDraw = slabPoly;
            if (drawnKolonPerdeKirisUnion != null && !drawnKolonPerdeKirisUnion.IsEmpty)
            {
                try
                {
                    var diff = slabPoly.Difference(drawnKolonPerdeKirisUnion);
                    if (diff != null && !diff.IsEmpty)
                    {
                        toDraw = KeepLargestPolygon(diff);
                        if (toDraw == null) toDraw = diff;
                    }
                }
                catch { }
            }
            return true;
        }

        /// <summary>Bitişik ve aynı kalınlık/kot/hareketli yük olan döşemelerden sadece en büyük alanlı (eşitse en sol üst) etiketlenir. İstisna: grup aynı zamanda kat sınırına bitişikse gruptaki tüm döşemelere etiket yazılır.</summary>
        private HashSet<int> ComputeSlabIdsToLabel(List<(SlabInfo slab, Geometry toDraw, Point2d center)> records, FloorInfo floor, Geometry floorBoundary = null)
        {
            var result = new HashSet<int>();
            if (records.Count == 0) return result;
            double floorElev = floor?.ElevationM ?? 0;
            double baseKotu = _model.BuildingBaseKotu;
            var keyToIndices = new Dictionary<(int t, int te, int be, int l), List<int>>();
            for (int i = 0; i < records.Count; i++)
            {
                var r = records[i];
                if (_model.StairSlabIds.Contains(r.slab.SlabId)) continue;
                double th = r.slab.ThicknessCm > 0 ? r.slab.ThicknessCm : 15.0;
                double topElev = baseKotu + floorElev + r.slab.OffsetFromFloorCm / 100.0;
                double bottomElev = topElev - th / 100.0;
                double load = r.slab.LiveLoadKNm2;
                var key = ((int)Math.Round(th), (int)Math.Round(topElev * 100), (int)Math.Round(bottomElev * 100), (int)Math.Round(load * 10));
                if (!keyToIndices.TryGetValue(key, out var list)) { list = new List<int>(); keyToIndices[key] = list; }
                list.Add(i);
            }
            bool hasFloorBoundary = floorBoundary != null && !floorBoundary.IsEmpty;
            foreach (var kv in keyToIndices)
            {
                var indices = kv.Value;
                if (indices.Count == 0) continue;
                if (indices.Count == 1) { result.Add(records[indices[0]].slab.SlabId); continue; }
                int n = indices.Count;
                var parent = new int[n];
                for (int i = 0; i < n; i++) parent[i] = i;
                int Root(int j) { while (parent[j] != j) j = parent[j]; return j; }
                void Union(int a, int b) { parent[Root(a)] = Root(b); }
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                        if (records[indices[i]].toDraw.Touches(records[indices[j]].toDraw))
                            Union(i, j);
                var byRoot = new Dictionary<int, List<int>>();
                for (int i = 0; i < n; i++)
                {
                    int r = Root(i);
                    if (!byRoot.TryGetValue(r, out var list)) { list = new List<int>(); byRoot[r] = list; }
                    list.Add(i);
                }
                foreach (var comp in byRoot.Values)
                {
                    bool groupTouchesFloorBoundary = false;
                    if (hasFloorBoundary)
                    {
                        try
                        {
                            foreach (int i in comp)
                            {
                                var geom = records[indices[i]].toDraw;
                                if (geom != null && !geom.IsEmpty && geom.Touches(floorBoundary))
                                { groupTouchesFloorBoundary = true; break; }
                            }
                        }
                        catch { }
                    }
                    if (groupTouchesFloorBoundary)
                    {
                        foreach (int i in comp)
                            result.Add(records[indices[i]].slab.SlabId);
                    }
                    else
                    {
                        int chosen = comp[0];
                        double areaChosen = records[indices[chosen]].toDraw.Area;
                        double xChosen = records[indices[chosen]].center.X;
                        double yChosen = records[indices[chosen]].center.Y;
                        for (int k = 1; k < comp.Count; k++)
                        {
                            int idx = comp[k];
                            double area = records[indices[idx]].toDraw.Area;
                            double x = records[indices[idx]].center.X;
                            double y = records[indices[idx]].center.Y;
                            if (area > areaChosen || (Math.Abs(area - areaChosen) < 1e-6 && (x < xChosen || (Math.Abs(x - xChosen) < 1e-6 && y > yChosen))))
                            { chosen = idx; areaChosen = area; xChosen = x; yChosen = y; }
                        }
                        result.Add(records[indices[chosen]].slab.SlabId);
                    }
                }
            }
            return result;
        }

        /// <summary>Döşeme etiketi: D+KatId+-+no d=Xcm (12 cm), çerçeve sol/sağ yarım daire (2 yay + 2 düz), Q, üst/alt kot, aralarında doğrudan çizilen kot işareti. Üst kot = bina tabanı + kat kotu + 16. sütun (cm)/100; alt kot = üst kot - kalınlık.</summary>
        private void AppendSlabLabel(Transaction tr, BlockTableRecord btr, SlabInfo slab, FloorInfo floor, string storyId, int slabPad, Point2d center, ObjectId textStyleId)
        {
            const double labelHeightCm = 12.0;
            const double framePaddingCm = 2.0;
            const double mainToQCm = 15.0;
            const double qToUstKotCm = 11.0;
            const double kotArasiMesafeCm = 10.0;
            const double subTextHeightCm = 10.0;
            const double kotTextHeightCm = 8.0;

            int slabNumero = GetSlabNumero(slab.SlabId);
            string slabNoStr = slabNumero.ToString("D" + slabPad, CultureInfo.InvariantCulture);
            double thickness = slab.ThicknessCm > 0 ? slab.ThicknessCm : 15.0;
            string mainLabel = string.Format(CultureInfo.InvariantCulture, "D{0}{1} d={2:F0}cm", storyId, slabNoStr, thickness);
            double mainWidth = EstimateTextWidthCm(mainLabel, labelHeightCm);
            double blockWidth = mainWidth;
            double blockHeight = labelHeightCm;
            string qLine = null;
            if (slab.LiveLoadKNm2 > 0)
            {
                qLine = string.Format(CultureInfo.InvariantCulture, "Q={0:F1}kN/m²", slab.LiveLoadKNm2);
                blockWidth = Math.Max(blockWidth, EstimateTextWidthCm(qLine, subTextHeightCm));
                blockHeight += mainToQCm + subTextHeightCm + qToUstKotCm;
            }
            else
                blockHeight += mainToQCm + qToUstKotCm;
            double topElevM = _model.BuildingBaseKotu + (floor?.ElevationM ?? 0) + slab.OffsetFromFloorCm / 100.0;
            double bottomElevM = topElevM - thickness / 100.0;
            string topElevStr = string.Format(CultureInfo.InvariantCulture, "{0:+0.00;-0.00;0.00}", topElevM);
            string bottomElevStr = string.Format(CultureInfo.InvariantCulture, "{0:+0.00;-0.00;0.00}", bottomElevM);
            if (topElevM == 0) topElevStr = "±" + topElevStr;
            if (bottomElevM == 0) bottomElevStr = "±" + bottomElevStr;
            blockWidth = Math.Max(blockWidth, Math.Max(EstimateTextWidthCm(topElevStr, kotTextHeightCm), EstimateTextWidthCm(bottomElevStr, kotTextHeightCm)));
            blockHeight += kotTextHeightCm + kotArasiMesafeCm + kotTextHeightCm;
            double leftX = center.X - blockWidth / 2.0;
            double mainY = center.Y + blockHeight / 2.0 - labelHeightCm;

            var txtMain = new DBText
            {
                Layer = LayerDosemeIsmi,
                Height = labelHeightCm,
                TextStyleId = textStyleId,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(mainLabel),
                Position = new Point3d(leftX, mainY, 0),
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = TextVerticalMode.TextBottom,
                AlignmentPoint = new Point3d(leftX, mainY, 0),
                Rotation = 0
            };
            AppendEntity(tr, btr, txtMain);
            double textXmin, textYmin, textXmax, textYmax;
            try
            {
                Extents3d ext = txtMain.GeometricExtents;
                textXmin = ext.MinPoint.X;
                textYmin = ext.MinPoint.Y;
                textXmax = ext.MaxPoint.X;
                textYmax = ext.MaxPoint.Y;
            }
            catch
            {
                textXmin = leftX;
                textYmin = mainY;
                textXmax = leftX + mainWidth;
                textYmax = mainY + labelHeightCm;
            }
            double textCx = (textXmin + textXmax) * 0.5;
            double textW = textXmax - textXmin;
            if (textW < 0.1) textW = mainWidth;
            double textCy = mainY + labelHeightCm * 0.5;
            double textH = labelHeightCm;
            double H = textH + 2.0 * framePaddingCm;
            double R = H * 0.5;
            double x0 = textCx - textW * 0.5 - framePaddingCm - R;
            double x1 = textCx + textW * 0.5 + framePaddingCm + R;
            const double frameVerticalOffsetCm = 3.4828;
            double y0 = textCy - textH * 0.5 - framePaddingCm + frameVerticalOffsetCm;
            double y1 = textCy + textH * 0.5 + framePaddingCm + frameVerticalOffsetCm;
            var pline = new Polyline();
            pline.SetDatabaseDefaults();
            pline.Layer = LayerDosemePafta;
            pline.AddVertexAt(0, new Point2d(x0 + R, y1), 0, 0, 0);
            pline.AddVertexAt(1, new Point2d(x1 - R, y1), -1, 0, 0);
            pline.AddVertexAt(2, new Point2d(x1 - R, y0), 0, 0, 0);
            pline.AddVertexAt(3, new Point2d(x0 + R, y0), -1, 0, 0);
            pline.Closed = true;
            AppendEntity(tr, btr, pline);

            double nextY = mainY - mainToQCm;
            if (!string.IsNullOrEmpty(qLine))
            {
                var txtQ = new DBText
                {
                    Layer = LayerYukYazisi,
                    Height = subTextHeightCm,
                    TextStyleId = textStyleId,
                    TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(qLine),
                    Position = new Point3d(leftX, nextY, 0),
                    HorizontalMode = TextHorizontalMode.TextLeft,
                    VerticalMode = TextVerticalMode.TextBottom,
                    AlignmentPoint = new Point3d(leftX, nextY, 0),
                    Rotation = 0
                };
                AppendEntity(tr, btr, txtQ);
                nextY -= qToUstKotCm;
            }
            else
                nextY -= qToUstKotCm;
            const double kotBlokOffsetXcm = 19.5;
            const double kotSolaKaydirCm = 5.1739;
            double leftXKot = leftX + kotBlokOffsetXcm - kotSolaKaydirCm;
            var txtTop = new DBText
            {
                Layer = LayerKotYazi,
                Height = kotTextHeightCm,
                TextStyleId = textStyleId,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(topElevStr),
                Position = new Point3d(leftXKot, nextY, 0),
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = TextVerticalMode.TextBottom,
                AlignmentPoint = new Point3d(leftXKot, nextY, 0),
                Rotation = 0
            };
            AppendEntity(tr, btr, txtTop);
            nextY -= kotArasiMesafeCm;
            const double kotSymbolOffsetXcm = 3.5;
            const double kotSymbolOffsetYcm = 6.322;
            double kotSymbolY = nextY + kotArasiMesafeCm * 0.5;
            DrawKotSymbol(tr, btr, leftXKot + kotSymbolOffsetXcm, kotSymbolY + kotSymbolOffsetYcm);
            var txtBottom = new DBText
            {
                Layer = LayerKotYazi,
                Height = kotTextHeightCm,
                TextStyleId = textStyleId,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(bottomElevStr),
                Position = new Point3d(leftXKot, nextY, 0),
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = TextVerticalMode.TextBottom,
                AlignmentPoint = new Point3d(leftXKot, nextY, 0),
                Rotation = 0
            };
            AppendEntity(tr, btr, txtBottom);
        }

        /// <summary>Bitişik ve aynı kalınlık/kot/hareketli yük radyelerden sadece en sol üstteki etiketlenir.</summary>
        private static HashSet<int> ComputeRadyeIndicesToLabel(List<(SlabFoundationInfo sf, Geometry poly, Point2d center)> records, double buildingBaseKotu)
        {
            var result = new HashSet<int>();
            if (records.Count == 0) return result;
            double baseKotu = buildingBaseKotu;
            var keyToIndices = new Dictionary<(int t, int te, int be, int l), List<int>>();
            for (int i = 0; i < records.Count; i++)
            {
                var r = records[i];
                double th = r.sf.ThicknessCm > 0 ? r.sf.ThicknessCm : 80.0;
                double topElev = baseKotu;
                double bottomElev = topElev - th / 100.0;
                double load = r.sf.LiveLoadKNm2;
                var key = ((int)Math.Round(th), (int)Math.Round(topElev * 100), (int)Math.Round(bottomElev * 100), (int)Math.Round(load * 10));
                if (!keyToIndices.TryGetValue(key, out var list)) { list = new List<int>(); keyToIndices[key] = list; }
                list.Add(i);
            }
            foreach (var kv in keyToIndices)
            {
                var indices = kv.Value;
                if (indices.Count == 0) continue;
                if (indices.Count == 1) { result.Add(indices[0]); continue; }
                int n = indices.Count;
                var parent = new int[n];
                for (int i = 0; i < n; i++) parent[i] = i;
                int Root(int j) { while (parent[j] != j) j = parent[j]; return j; }
                void Union(int a, int b) { parent[Root(a)] = Root(b); }
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                        if (records[indices[i]].poly.Touches(records[indices[j]].poly))
                            Union(i, j);
                var byRoot = new Dictionary<int, List<int>>();
                for (int i = 0; i < n; i++)
                {
                    int r = Root(i);
                    if (!byRoot.TryGetValue(r, out var list)) { list = new List<int>(); byRoot[r] = list; }
                    list.Add(i);
                }
                foreach (var comp in byRoot.Values)
                {
                    int chosen = comp[0];
                    double xChosen = records[indices[chosen]].center.X;
                    double yChosen = records[indices[chosen]].center.Y;
                    for (int k = 1; k < comp.Count; k++)
                    {
                        int idx = comp[k];
                        double x = records[indices[idx]].center.X;
                        double y = records[indices[idx]].center.Y;
                        if (x < xChosen || (Math.Abs(x - xChosen) < 1e-6 && y > yChosen))
                        { chosen = idx; xChosen = x; yChosen = y; }
                    }
                    result.Add(indices[chosen]);
                }
            }
            return result;
        }

        /// <summary>Radye temel etiketi: RD-+no d=Xcm, çerçeve, Q (7. sütun/10 kN/m²), üst/alt kot, kot işareti. Üst kot = bina taban kotu; alt kot = üst kot - kalınlık.</summary>
        private void AppendRadyeLabel(Transaction tr, BlockTableRecord btr, SlabFoundationInfo sf, int radyeNo, int radyePad, Point2d center, ObjectId textStyleId)
        {
            const double labelHeightCm = 12.0;
            const double framePaddingCm = 2.0;
            const double mainToQCm = 15.0;
            const double qToUstKotCm = 11.0;
            const double kotArasiMesafeCm = 10.0;
            const double subTextHeightCm = 10.0;
            const double kotTextHeightCm = 8.0;

            string radyeNoStr = radyeNo.ToString("D" + radyePad, CultureInfo.InvariantCulture);
            double thickness = sf.ThicknessCm > 0 ? sf.ThicknessCm : 80.0;
            string mainLabel = string.Format(CultureInfo.InvariantCulture, "RD-{0} d={1:F0}cm", radyeNoStr, thickness);
            double mainWidth = EstimateTextWidthCm(mainLabel, labelHeightCm);
            double blockWidth = mainWidth;
            double blockHeight = labelHeightCm;
            string qLine = null;
            if (sf.LiveLoadKNm2 > 0)
            {
                qLine = string.Format(CultureInfo.InvariantCulture, "Q={0:F1}kN/m²", sf.LiveLoadKNm2);
                blockWidth = Math.Max(blockWidth, EstimateTextWidthCm(qLine, subTextHeightCm));
                blockHeight += mainToQCm + subTextHeightCm + qToUstKotCm;
            }
            else
                blockHeight += mainToQCm + qToUstKotCm;
            blockHeight += kotTextHeightCm + kotArasiMesafeCm + kotTextHeightCm;

            double topElevM = _model.BuildingBaseKotu;
            double bottomElevM = topElevM - thickness / 100.0;
            string topElevStr = string.Format(CultureInfo.InvariantCulture, "{0:+0.00;-0.00;0.00}", topElevM);
            string bottomElevStr = string.Format(CultureInfo.InvariantCulture, "{0:+0.00;-0.00;0.00}", bottomElevM);
            if (topElevM == 0) topElevStr = "±" + topElevStr;
            if (bottomElevM == 0) bottomElevStr = "±" + bottomElevStr;
            blockWidth = Math.Max(blockWidth, Math.Max(EstimateTextWidthCm(topElevStr, kotTextHeightCm), EstimateTextWidthCm(bottomElevStr, kotTextHeightCm)));
            double leftX = center.X - blockWidth / 2.0;
            double mainY = center.Y + blockHeight / 2.0 - labelHeightCm;

            var txtMain = new DBText
            {
                Layer = LayerDosemeIsmi,
                Height = labelHeightCm,
                TextStyleId = textStyleId,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(mainLabel),
                Position = new Point3d(leftX, mainY, 0),
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = TextVerticalMode.TextBottom,
                AlignmentPoint = new Point3d(leftX, mainY, 0),
                Rotation = 0
            };
            AppendEntity(tr, btr, txtMain);
            double textXmin = leftX, textYmin = mainY, textXmax = leftX + mainWidth, textYmax = mainY + labelHeightCm;
            try
            {
                Extents3d ext = txtMain.GeometricExtents;
                textXmin = ext.MinPoint.X; textYmin = ext.MinPoint.Y; textXmax = ext.MaxPoint.X; textYmax = ext.MaxPoint.Y;
            }
            catch { }
            double textCx = (textXmin + textXmax) * 0.5;
            double textW = Math.Max(0.1, textXmax - textXmin);
            double textCy = mainY + labelHeightCm * 0.5;
            double textH = labelHeightCm;
            double H = textH + 2.0 * framePaddingCm;
            double R = H * 0.5;
            double x0 = textCx - textW * 0.5 - framePaddingCm - R;
            double x1 = textCx + textW * 0.5 + framePaddingCm + R;
            const double frameVerticalOffsetCm = 3.4828;
            double y0 = textCy - textH * 0.5 - framePaddingCm + frameVerticalOffsetCm;
            double y1 = textCy + textH * 0.5 + framePaddingCm + frameVerticalOffsetCm;
            var pline = new Polyline();
            pline.SetDatabaseDefaults();
            pline.Layer = LayerDosemePafta;
            pline.AddVertexAt(0, new Point2d(x0 + R, y1), 0, 0, 0);
            pline.AddVertexAt(1, new Point2d(x1 - R, y1), -1, 0, 0);
            pline.AddVertexAt(2, new Point2d(x1 - R, y0), 0, 0, 0);
            pline.AddVertexAt(3, new Point2d(x0 + R, y0), -1, 0, 0);
            pline.Closed = true;
            AppendEntity(tr, btr, pline);

            double nextY = mainY - mainToQCm;
            if (!string.IsNullOrEmpty(qLine))
            {
                var txtQ = new DBText
                {
                    Layer = LayerYukYazisi,
                    Height = subTextHeightCm,
                    TextStyleId = textStyleId,
                    TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(qLine),
                    Position = new Point3d(leftX, nextY, 0),
                    HorizontalMode = TextHorizontalMode.TextLeft,
                    VerticalMode = TextVerticalMode.TextBottom,
                    AlignmentPoint = new Point3d(leftX, nextY, 0),
                    Rotation = 0
                };
                AppendEntity(tr, btr, txtQ);
                nextY -= qToUstKotCm;
            }
            else
                nextY -= qToUstKotCm;
            const double kotBlokOffsetXcm = 19.5;
            const double kotSolaKaydirCm = 5.1739;
            double leftXKot = leftX + kotBlokOffsetXcm - kotSolaKaydirCm;
            var txtTop = new DBText
            {
                Layer = LayerKotYazi,
                Height = kotTextHeightCm,
                TextStyleId = textStyleId,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(topElevStr),
                Position = new Point3d(leftXKot, nextY, 0),
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = TextVerticalMode.TextBottom,
                AlignmentPoint = new Point3d(leftXKot, nextY, 0),
                Rotation = 0
            };
            AppendEntity(tr, btr, txtTop);
            nextY -= kotArasiMesafeCm;
            const double kotSymbolOffsetXcm = 3.5;
            const double kotSymbolOffsetYcm = 6.322;
            double kotSymbolY = nextY + kotArasiMesafeCm * 0.5;
            DrawKotSymbol(tr, btr, leftXKot + kotSymbolOffsetXcm, kotSymbolY + kotSymbolOffsetYcm);
            var txtBottom = new DBText
            {
                Layer = LayerKotYazi,
                Height = kotTextHeightCm,
                TextStyleId = textStyleId,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(bottomElevStr),
                Position = new Point3d(leftXKot, nextY, 0),
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = TextVerticalMode.TextBottom,
                AlignmentPoint = new Point3d(leftXKot, nextY, 0),
                Rotation = 0
            };
            AppendEntity(tr, btr, txtBottom);
        }

        /// <summary>Kot işareti dosya yolu: önce YATAY_KOT.dxf, yoksa YATAY_KOT.dwg (DLL ile aynı klasör).</summary>
        private static string GetKotSymbolFilePath()
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(dir)) return null;
                string dxf = Path.Combine(dir, "YATAY_KOT.dxf");
                if (System.IO.File.Exists(dxf)) return dxf;
                string dwg = Path.Combine(dir, "YATAY_KOT.dwg");
                if (System.IO.File.Exists(dwg)) return dwg;
                return null;
            }
            catch { return null; }
        }

        /// <summary>Noktayı (cx, cy) etrafında angleRad radyan döndürür.</summary>
        private static void RotatePointAround(double x, double y, double cx, double cy, double angleRad, out double outX, out double outY)
        {
            double dx = x - cx, dy = y - cy;
            double c = Math.Cos(angleRad), s = Math.Sin(angleRad);
            outX = cx + dx * c - dy * s;
            outY = cy + dx * s + dy * c;
        }

        /// <summary>Üst ve alt kot yazıları arasına çizilen kot işareti. YATAY_KOT.dxf/dwg varsa dosyadan entity kopyalanır (KOT CIZGISI katmanında), yoksa T şekli çizilir. rotationRad != 0 ise aksa göre döndürülür (rotCenterX, rotCenterY) etrafında.</summary>
        private static void DrawKotSymbol(Transaction tr, BlockTableRecord btr, double leftX, double centerY, double rotCenterX = double.NaN, double rotCenterY = double.NaN, double rotationRad = 0)
        {
            bool rotate = rotationRad != 0 && !double.IsNaN(rotCenterX) && !double.IsNaN(rotCenterY);
            string path = GetKotSymbolFilePath();
            if (!string.IsNullOrEmpty(path) && TryLoadKotSymbolFromFile(tr, btr, path, leftX, centerY, rotCenterX, rotCenterY, rotationRad))
                return;
            const double kotSymbolWidthCm = 40.0; // Yatay çizgi sağ taraftan 40 cm
            const double kotSymbolTickHeightCm = 0.5;
            double xMid = leftX + kotSymbolWidthCm * 0.5;
            double x1 = leftX, y1 = centerY, x2 = leftX + kotSymbolWidthCm, y2 = centerY;
            double x3 = xMid, y3 = centerY, x4 = xMid, y4 = centerY + kotSymbolTickHeightCm;
            if (rotate)
            {
                RotatePointAround(x1, y1, rotCenterX, rotCenterY, rotationRad, out x1, out y1);
                RotatePointAround(x2, y2, rotCenterX, rotCenterY, rotationRad, out x2, out y2);
                RotatePointAround(x3, y3, rotCenterX, rotCenterY, rotationRad, out x3, out y3);
                RotatePointAround(x4, y4, rotCenterX, rotCenterY, rotationRad, out x4, out y4);
            }
            var lineHorz = new Line(new Point3d(x1, y1, 0), new Point3d(x2, y2, 0));
            lineHorz.SetDatabaseDefaults();
            lineHorz.Layer = LayerKotCizgisi;
            AppendEntity(tr, btr, lineHorz);
            var lineTick = new Line(new Point3d(x3, y3, 0), new Point3d(x4, y4, 0));
            lineTick.SetDatabaseDefaults();
            lineTick.Layer = LayerKotCizgisi;
            AppendEntity(tr, btr, lineTick);
        }

        /// <summary>Üst kot, yatay kot işareti ve alt kot takımını verilen merkeze ortalı çizer. Kiriş/perde kot etiketi için kullanılır. rotationRad: kiriş/perdenin fixlendiği aksa göre dönüş (radyan); 0 ise yatay.</summary>
        private void DrawKotBlockAtCenter(Transaction tr, BlockTableRecord btr, Database db, double centerX, double centerY, double topElevM, double bottomElevM, double rotationRad = 0)
        {
            const double kotArasiMesafeCm = 10.0;
            const double kotTextHeightCm = 8.0;
            const double blockHalfWidthCm = 21.75;
            const double kotBlockSagaKaydirCm = 21.75;
            const double ustKotAsagiKaydirCm = 10.322;
            const double altKotAsagiKaydirCm = 2.322;
            double leftX = centerX - blockHalfWidthCm + kotBlockSagaKaydirCm;
            string topElevStr = string.Format(CultureInfo.InvariantCulture, "{0:+0.00;-0.00;0.00}", topElevM);
            string bottomElevStr = string.Format(CultureInfo.InvariantCulture, "{0:+0.00;-0.00;0.00}", bottomElevM);
            if (topElevM == 0) topElevStr = "±" + topElevStr;
            if (bottomElevM == 0) bottomElevStr = "±" + bottomElevStr;
            ObjectId textStyleId = GetOrCreateYaziBeykentTextStyle(tr, db);
            double topY = centerY + kotArasiMesafeCm * 0.5 + kotTextHeightCm * 0.5 - ustKotAsagiKaydirCm;
            double bottomY = centerY - kotArasiMesafeCm * 0.5 - kotTextHeightCm * 0.5 - altKotAsagiKaydirCm;
            const double kotSymbolOffsetFromLeftCm = 3.5;
            double leftXSym = leftX + kotSymbolOffsetFromLeftCm;
            bool rotate = rotationRad != 0;
            double topX = leftX, topYout = topY, bottomX = leftX, bottomYout = bottomY;
            if (rotate)
            {
                RotatePointAround(leftX, topY, centerX, centerY, rotationRad, out topX, out topYout);
                RotatePointAround(leftX, bottomY, centerX, centerY, rotationRad, out bottomX, out bottomYout);
            }
            var txtTop = new DBText
            {
                Layer = LayerKotYazi,
                Height = kotTextHeightCm,
                TextStyleId = textStyleId,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(topElevStr),
                Position = new Point3d(topX, topYout, 0),
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = TextVerticalMode.TextBottom,
                AlignmentPoint = new Point3d(topX, topYout, 0),
                Rotation = rotationRad
            };
            AppendEntity(tr, btr, txtTop);
            DrawKotSymbol(tr, btr, leftXSym, centerY, centerX, centerY, rotationRad);
            var txtBottom = new DBText
            {
                Layer = LayerKotYazi,
                Height = kotTextHeightCm,
                TextStyleId = textStyleId,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(bottomElevStr),
                Position = new Point3d(bottomX, bottomYout, 0),
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = TextVerticalMode.TextBottom,
                AlignmentPoint = new Point3d(bottomX, bottomYout, 0),
                Rotation = rotationRad
            };
            AppendEntity(tr, btr, txtBottom);
        }

        /// <summary>YATAY_KOT.dxf veya .dwg dosyasındaki Model Space entity'lerini btr'ye kopyalar; merkezi (leftX, centerY) olacak şekilde öteler, isteğe bağlı döndürür ve KOT CIZGISI katmanına alır.</summary>
        private static bool TryLoadKotSymbolFromFile(Transaction tr, BlockTableRecord btr, string filePath, double leftX, double centerY, double rotCenterX = double.NaN, double rotCenterY = double.NaN, double rotationRad = 0)
        {
            Database sourceDb = null;
            try
            {
                sourceDb = new Database(false, true);
                sourceDb.ReadDwgFile(filePath, FileOpenMode.OpenForReadAndAllShare, true, null);
                ObjectIdCollection ids = new ObjectIdCollection();
                using (Transaction trSrc = sourceDb.TransactionManager.StartTransaction())
                {
                    BlockTableRecord ms = trSrc.GetObject(sourceDb.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                    if (ms == null) return false;
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsValid && !id.IsErased) ids.Add(id);
                    }
                    trSrc.Commit();
                }
                if (ids.Count == 0) return false;
                IdMapping mapping = new IdMapping();
                btr.Database.WblockCloneObjects(ids, btr.ObjectId, mapping, DuplicateRecordCloning.Ignore, false);
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (IdPair pair in mapping)
                {
                    if (!pair.Value.IsValid) continue;
                    Entity ent = tr.GetObject(pair.Value, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    try
                    {
                        Extents3d ext = ent.GeometricExtents;
                        minX = Math.Min(minX, ext.MinPoint.X);
                        minY = Math.Min(minY, ext.MinPoint.Y);
                        maxX = Math.Max(maxX, ext.MaxPoint.X);
                        maxY = Math.Max(maxY, ext.MaxPoint.Y);
                    }
                    catch { }
                }
                if (minX > maxX || minY > maxY) return true;
                double srcCx = (minX + maxX) * 0.5;
                double srcCy = (minY + maxY) * 0.5;
                Matrix3d disp = Matrix3d.Displacement(new Vector3d(leftX - srcCx, centerY - srcCy, 0));
                const double kotSymbolHorzLengthCm = 40.0; // Yatay çizgi sağ taraftan 40 cm
                double scaleX = kotSymbolHorzLengthCm / (maxX - minX);
                Matrix3d scaleAt = Matrix3d.Scaling(scaleX, new Point3d(leftX, centerY, 0));
                bool applyRotation = rotationRad != 0 && !double.IsNaN(rotCenterX) && !double.IsNaN(rotCenterY);
                Matrix3d rot = applyRotation ? Matrix3d.Rotation(rotationRad, Vector3d.ZAxis, new Point3d(rotCenterX, rotCenterY, 0)) : Matrix3d.Identity;
                foreach (IdPair pair in mapping)
                {
                    if (!pair.Value.IsValid) continue;
                    Entity ent = tr.GetObject(pair.Value, OpenMode.ForWrite) as Entity;
                    if (ent == null) continue;
                    ent.TransformBy(disp);
                    ent.TransformBy(scaleAt);
                    if (applyRotation) ent.TransformBy(rot);
                    ent.Layer = LayerKotCizgisi;
                }
                return true;
            }
            catch { return false; }
            finally
            {
                sourceDb?.Dispose();
            }
        }

        /// <summary>TEMEL50ST4: DLL içine gömülü antet_02.dwg → geçici dosyaya yazilir; ReadDwgFile (DXF DxfIn sembol tablosu hatalarindan kacinir).</summary>
        private static bool TryPopulateDatabaseFromEmbeddedTemelAntet(Database db, Editor ed)
        {
            string tmpDwg = null;
            try
            {
                Assembly asm = typeof(PlanIdDrawingManager).Assembly;
                using (Stream stream = asm.GetManifestResourceStream(TemelAntetEmbeddedResourceName))
                {
                    if (stream == null)
                    {
                        ed?.WriteMessage("\nTEMEL50ST4: Gömülü antet kaynagi yok (derlemede antet_02.dwg gomulu olmali).");
                        return false;
                    }
                    tmpDwg = Path.Combine(Path.GetTempPath(), "ST4PlanIdCiz_antet_" + Guid.NewGuid().ToString("N") + ".dwg");
                    using (var fs = new FileStream(tmpDwg, FileMode.Create, FileAccess.Write, FileShare.Read))
                        stream.CopyTo(fs);
                }
                db.ReadDwgFile(tmpDwg, FileOpenMode.OpenForReadAndAllShare, true, null);
                return true;
            }
            catch (Exception ex)
            {
                ed?.WriteMessage("\nTEMEL50ST4: Gömülü antet acilamadi: {0}", ex.Message);
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(tmpDwg))
                {
                    try { File.Delete(tmpDwg); } catch { }
                }
            }
        }

        /// <summary>Yalnizca TEMEL50ST4: gömülü antet_02.dwg bire bir (ölçeksiz öteleme). SHEETVIEW sol-alt, plan+kesit layout sol-altina oturur. TransformBy yalnizca Model Space kök Entity.</summary>
        private void TryDrawTemelAntetFromDxf(Transaction tr, BlockTableRecord btr, double layoutMinX, double layoutMinY, double layoutMaxX, double layoutMaxY, Editor ed)
        {
            double contentW = layoutMaxX - layoutMinX;
            double contentH = layoutMaxY - layoutMinY;
            if (contentW < 10.0 || contentH < 10.0)
            {
                ed?.WriteMessage("\nTEMEL50ST4: Antet icin plan sinirlari gecersiz.");
                return;
            }

            var srcLL = new Point3d(AntetDxfSheetViewXmin, AntetDxfSheetViewYmin, 0);
            var xf = Matrix3d.Displacement(new Vector3d(layoutMinX - srcLL.X, layoutMinY - srcLL.Y, 0));

            Database sourceDb = null;
            try
            {
                sourceDb = new Database(false, true);
                if (!TryPopulateDatabaseFromEmbeddedTemelAntet(sourceDb, ed))
                    return;
                var ids = new ObjectIdCollection();
                ObjectId sourceMsId;
                using (Transaction trSrc = sourceDb.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)trSrc.GetObject(sourceDb.BlockTableId, OpenMode.ForRead);
                    sourceMsId = bt[BlockTableRecord.ModelSpace];
                    var ms = (BlockTableRecord)trSrc.GetObject(sourceMsId, OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsValid && !id.IsErased) ids.Add(id);
                    }
                    trSrc.Commit();
                }
                if (ids.Count == 0)
                {
                    ed?.WriteMessage("\nTEMEL50ST4: antet dosyasinda Model Space bos.");
                    return;
                }

                var mapping = new IdMapping();
                btr.Database.WblockCloneObjects(ids, btr.ObjectId, mapping, DuplicateRecordCloning.Ignore, false);
                using (Transaction trSrc = sourceDb.TransactionManager.StartTransaction())
                {
                    var sourceMs = (BlockTableRecord)trSrc.GetObject(sourceMsId, OpenMode.ForRead);
                    foreach (IdPair pair in mapping)
                    {
                        if (!pair.Key.IsValid || !pair.Value.IsValid) continue;
                        var entSrc = trSrc.GetObject(pair.Key, OpenMode.ForRead) as Entity;
                        if (entSrc == null) continue;
                        if (!entSrc.BlockId.Equals(sourceMs.ObjectId)) continue;
                        var entDst = tr.GetObject(pair.Value, OpenMode.ForWrite) as Entity;
                        if (entDst == null) continue;
                        entDst.TransformBy(xf);
                    }
                    trSrc.Commit();
                }
            }
            catch (Exception ex)
            {
                ed?.WriteMessage("\nTEMEL50ST4: Antet yuklenemedi: {0}", ex.Message);
            }
            finally
            {
                sourceDb?.Dispose();
            }
        }

        /// <summary>Sürekli temellerin tüm dikdörtgenlerinin birleşimi (offset uygulanmış); radye temel temel hatılı / bağ kirişi içinde mi kontrolü için.</summary>
        private Geometry BuildContinuousFoundationsUnion(double offsetX, double offsetY)
        {
            var factory = _ntsDrawFactory;
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

        /// <summary>Radye temellerin (slab foundations) birleşik alanı (offset uygulanmış); radye temel temel hatılı / bağ kirişi içinde mi kontrolü için.</summary>
        private Geometry BuildSlabFoundationsUnion(double offsetX, double offsetY)
        {
            var factory = _ntsDrawFactory;
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
            var factory = _ntsDrawFactory;
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

            // Radye temel temel hatılı ve bağ kirişi poligonları da birleşime dahil edilir.
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

            // Bağ kirişleri (TieBeams) — TEMEL (BEYKENT) katmanına çizilen bağ kirişleri birleşime dahil; TEMEL HATILI katmanındakiler "radye temel temel hatılı"dır, DrawTieBeams'ta işlenir.
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
            DrawTemelIcBoslukTarama(tr, btr, BuildTemelCizimGeometrisi(unionResult));
        }

        /// <summary>Temel planı içinde kalan iç boşlukları TARAMA katmanında AR-SAND olarak tarar.</summary>
        private void DrawTemelIcBoslukTarama(Transaction tr, BlockTableRecord btr, Geometry temelUnion)
        {
            if (temelUnion == null || temelUnion.IsEmpty) return;
            var holes = new List<LinearRing>();
            CollectInteriorRings(temelUnion, holes);
            if (holes.Count == 0) return;

            foreach (var hole in holes)
            {
                if (hole == null || hole.IsEmpty || hole.NumPoints < 4) continue;
                try
                {
                    AppendPredefinedHatchWithoutBoundaryPolyline(tr, btr, hole.Coordinates, "AR-SAND", 1.0, 0.0, LayerTarama);
                }
                catch
                {
                    // Geçersiz halka varsa atla; çizim devam etsin.
                }
            }
        }

        /// <summary>Temel çizimi ile aynı ring cleanup kurallarını uygulayarak tarama için temiz geometri üretir.</summary>
        private Geometry BuildTemelCizimGeometrisi(Geometry raw)
        {
            if (raw == null || raw.IsEmpty) return null;
            var polys = new List<Polygon>();

            void AddFromPolygon(Polygon p)
            {
                if (p == null || p.IsEmpty) return;
                var extPts = ApplyRingCleanup(p.ExteriorRing?.Coordinates, applySmallTriangleTrim: true);
                if (extPts == null || extPts.Count < 3) return;

                var extCoords = new Coordinate[extPts.Count + 1];
                for (int i = 0; i < extPts.Count; i++) extCoords[i] = new Coordinate(extPts[i].X, extPts[i].Y);
                extCoords[extPts.Count] = extCoords[0];
                var shell = _ntsDrawFactory.CreateLinearRing(extCoords);

                var holeRings = new List<LinearRing>();
                for (int h = 0; h < p.NumInteriorRings; h++)
                {
                    var holePts = ApplyRingCleanup(p.GetInteriorRingN(h)?.Coordinates, applySmallTriangleTrim: true);
                    if (holePts == null || holePts.Count < 3) continue;
                    var holeCoords = new Coordinate[holePts.Count + 1];
                    for (int i = 0; i < holePts.Count; i++) holeCoords[i] = new Coordinate(holePts[i].X, holePts[i].Y);
                    holeCoords[holePts.Count] = holeCoords[0];
                    holeRings.Add(_ntsDrawFactory.CreateLinearRing(holeCoords));
                }

                try { polys.Add(_ntsDrawFactory.CreatePolygon(shell, holeRings.ToArray())); }
                catch { /* Geçersiz halka kombinasyonu varsa atla */ }
            }

            if (raw is Polygon rp)
            {
                AddFromPolygon(rp);
            }
            else if (raw is MultiPolygon rmp)
            {
                for (int i = 0; i < rmp.NumGeometries; i++)
                    AddFromPolygon(rmp.GetGeometryN(i) as Polygon);
            }
            else if (raw is GeometryCollection rgc)
            {
                for (int i = 0; i < rgc.NumGeometries; i++)
                {
                    if (rgc.GetGeometryN(i) is Polygon gcp) AddFromPolygon(gcp);
                    else if (rgc.GetGeometryN(i) is MultiPolygon gcmp)
                    {
                        for (int j = 0; j < gcmp.NumGeometries; j++)
                            AddFromPolygon(gcmp.GetGeometryN(j) as Polygon);
                    }
                }
            }

            if (polys.Count == 0) return null;
            if (polys.Count == 1) return polys[0];
            return _ntsDrawFactory.CreateMultiPolygon(polys.ToArray());
        }

        /// <summary>Kapalı halka için hatch çizer; sınır polyline'ını sonrasında siler (görünür çevre çizgisi kalmaz).</summary>
        private static void AppendPredefinedHatchWithoutBoundaryPolyline(
            Transaction tr,
            BlockTableRecord btr,
            Coordinate[] ringCoords,
            string patternName,
            double patternScale,
            double patternAngleRad,
            string hatchLayer)
        {
            if (ringCoords == null || ringCoords.Length < 4) return;

            var pl = new Polyline();
            int idx = 0;
            for (int i = 0; i < ringCoords.Length; i++)
            {
                var c = ringCoords[i];
                if (idx > 0)
                {
                    var prev = ringCoords[i - 1];
                    if (Math.Abs(c.X - prev.X) < 1e-9 && Math.Abs(c.Y - prev.Y) < 1e-9) continue;
                }
                pl.AddVertexAt(idx++, new Point2d(c.X, c.Y), 0, 0, 0);
            }
            if (pl.NumberOfVertices < 3)
            {
                pl.Dispose();
                return;
            }
            pl.Closed = true;
            pl.Layer = hatchLayer;

            ObjectId plId = AppendEntityReturnId(tr, btr, pl);

            var hatch = new Hatch();
            btr.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);
            hatch.SetHatchPattern(HatchPatternType.PreDefined, patternName);
            hatch.PatternScale = patternScale;
            hatch.PatternAngle = patternAngleRad;
            hatch.Layer = hatchLayer;
            hatch.Associative = false;
            hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { plId });
            try { hatch.EvaluateHatch(true); }
            catch { try { hatch.EvaluateHatch(false); } catch { } }

            try { pl.Erase(); } catch { }
        }

        private static void CollectInteriorRings(Geometry geom, List<LinearRing> holes)
        {
            if (geom == null || geom.IsEmpty || holes == null) return;
            if (geom is Polygon p)
            {
                for (int i = 0; i < p.NumInteriorRings; i++)
                {
                    if (p.GetInteriorRingN(i) is LinearRing ring)
                        holes.Add(ring);
                }
                return;
            }
            if (geom is MultiPolygon mp)
            {
                for (int i = 0; i < mp.NumGeometries; i++)
                    CollectInteriorRings(mp.GetGeometryN(i), holes);
                return;
            }
            if (geom is GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                    CollectInteriorRings(gc.GetGeometryN(i), holes);
            }
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

        /// <summary>NTS Geometry (Polygon/MultiPolygon) dış ve iç halkalarını verilen katmanda polyline olarak çizer; 4 mm'den kısa segmentleri atlar. addHatch true ise her halka için tarama eklenir: <paramref name="hatchPatternName"/> doluysa o desen + ölçek + <paramref name="hatchLayerOverride"/> (varsayılan <see cref="LayerTarama"/>), değilse ANSI33. hatchAngleRad verilirse tarama açısı olarak kullanılır (perde: aks eğimi). exteriorRingsOnly true ise sadece dış halkalar çizilir (iç halkalar/delik sınırları çizilmez; kolona yapışık çizgi olmaz).</summary>
        private static void DrawGeometryRingsAsPolylines(Transaction tr, BlockTableRecord btr, Geometry geom, string layer, bool addHatch = false, double? hatchAngleRad = null, bool exteriorRingsOnly = false, bool applySmallTriangleTrim = false, double vertexAngleTolDeg = 1.0, double minVertexDistCm = 0.4, double collinearTolCm = 0, string hatchPatternName = null, double hatchPatternScale = 1.0, string hatchLayerOverride = null)
        {
            if (geom == null || geom.IsEmpty) return;
            double minSegmentLen = minVertexDistCm; // ardışık vertex arası min mesafe (varsayılan 4 mm)
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

                // 1c) Nihai kural: Bir vertex'e bağlı iki segment açı farkı vertexAngleTolDeg dereceden küçükse bu vertex gereksizdir (neredeyse doğrusal), kaldır.
                double vertexAngleTolRad = vertexAngleTolDeg * Math.PI / 180.0;
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
                // 1c2) Aynı doğrultuda devam eden segmentler: B vertex'i A-C doğru parçası üzerinde (collinearTolCm içinde) ise B'yi kaldır.
                if (collinearTolCm > 0 && pts.Count >= 4)
                {
                    int guardCol = 0;
                    while (guardCol++ < 50)
                    {
                        bool removedCol = false;
                        int m = pts.Count;
                        for (int i = 0; i < m; i++)
                        {
                            var a = pts[(i - 1 + m) % m];
                            var b = pts[i];
                            var c = pts[(i + 1) % m];
                            if (PointToSegmentDistance(b, a, c) < collinearTolCm)
                            {
                                pts.RemoveAt(i);
                                removedCol = true;
                                break;
                            }
                        }
                        if (!removedCol) break;
                    }
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
                if (layer == LayerGrobeton)
                    pl.LineWeight = LineWeight.LineWeight050;
                if (addHatch)
                {
                    ObjectId plId = AppendEntityReturnId(tr, btr, pl);
                    double angleRad = hatchAngleRad ?? Math.Atan2(filtered[1].Y - filtered[0].Y, filtered[1].X - filtered[0].X);
                    if (!string.IsNullOrEmpty(hatchPatternName))
                        AppendHatchPredefined(tr, btr, plId, hatchPatternName, hatchPatternScale, hatchAngleRad ?? 0.0, string.IsNullOrEmpty(hatchLayerOverride) ? LayerTarama : hatchLayerOverride);
                    else
                        AppendHatchAnsi33(tr, btr, plId, angleRad);
                }
                else
                    AppendEntity(tr, btr, pl);
            }
        }

        /// <param name="temelHatiliRaws">Dolu verilirse sürekli temel hatılı (geom, widthCm, heightDisplayCm, kot, isRadyeTemelHatili=false).</param>
        /// <param name="slabUnion">Radye temel alanı birleşiği; etiket taşınırken bu alanın içine girmemesi için kullanılır.</param>
        private void DrawContinuousFoundations(Transaction tr, BlockTableRecord btr, double offsetX, double offsetY, FloorInfo floor, bool drawTemelOutline = true, Geometry temelUnion = null, Geometry kolonPerdeUnion = null, List<(Geometry geom, double widthCm, double heightDisplayCm, double kot, bool isRadyeTemelHatili)> temelHatiliRaws = null, Geometry slabUnion = null)
        {
            const string layer = "TEMEL (BEYKENT)";
            const string layerAmpatman = "TEMEL AMPATMAN (BEYKENT)";
            var factory = _ntsDrawFactory;
            var ampatmanPolygons = new List<Geometry>();
            var continuousRectsForLabelCheck = new List<Geometry>();
            int cfIndex = 0;
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
                var rectPoly = RectToPolygon(factory, rect);
                continuousRectsForLabelCheck.Add(rectPoly);
                if (drawTemelOutline)
                {
                    var pl = ToPolyline(rect, true);
                    pl.Layer = layer;
                    AppendEntity(tr, btr, pl);
                }

                {
                    // 5. sütun 1 yönü (X ekseni 1001-1999): etiket temel sol çizgisinin 3 cm soluna. Diğerleri: üst kenarın 3 cm üstüne.
                    Vector2d perpUnit = perp.GetNormal();
                    double labelCx, labelCy;
                    if (cf.FixedAxisId >= 1001 && cf.FixedAxisId <= 1999)
                    {
                        // Sol çizgi = rect[2]-rect[3] (alt kenar, -perp yönü); 3 cm soluna = -perp yönünde 3 cm
                        double leftMidX = (rect[2].X + rect[3].X) / 2.0;
                        double leftMidY = (rect[2].Y + rect[3].Y) / 2.0;
                        labelCx = leftMidX - perpUnit.X * 3.0;
                        labelCy = leftMidY - perpUnit.Y * 3.0;
                    }
                    else
                    {
                        double upperMidX = (rect[0].X + rect[1].X) / 2.0;
                        double upperMidY = (rect[0].Y + rect[1].Y) / 2.0;
                        labelCx = upperMidX + perpUnit.X * 3.0;
                        labelCy = upperMidY + perpUnit.Y * 3.0;
                    }
                    int temelNo = cfIndex + 1;
                    int eni = (int)Math.Round(cf.WidthCm);
                    int yukseklik = (int)Math.Round(cf.HeightCm);
                    string eniStr = eni.ToString(CultureInfo.InvariantCulture);
                    if (cf.AmpatmanWidthCm > 0 && Math.Abs(cf.AmpatmanWidthCm - cf.WidthCm) > 1e-6)
                        eniStr = eniStr + "-" + ((int)Math.Round(cf.AmpatmanWidthCm)).ToString(CultureInfo.InvariantCulture);
                    string labelText = string.Format(CultureInfo.InvariantCulture, "T-{0} ({1}/{2})", temelNo, eniStr, yukseklik);
                    int labelAxisId = cf.LabelAxisId != 0 ? cf.LabelAxisId : cf.FixedAxisId;
                    double axisAngleRad = GetAxisLineAngleRad(labelAxisId);
                    TryFindClearLabelPositionForContinuous(factory, ref labelCx, ref labelCy, axisAngleRad, labelText, continuousRectsForLabelCheck, cfIndex, slabUnion);
                    DrawTemelIsmiLabel(tr, btr, btr.Database, labelCx, labelCy, labelText, axisAngleRad, bottomRightAligned: true);
                }
                cfIndex++;

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
                        double temelUstKotu = _model.BuildingBaseKotu + (cf.BottomKotBinaGoreCm + cf.HeightCm) / 100.0;
                        temelHatiliRaws.Add((hatilPoly, cf.TieBeamWidthCm, cf.HatilLabelHeightCm, temelUstKotu, false));
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
            int sfIndex = 0;
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
                sfIndex++;
                int xBoyu = (int)Math.Round(sf.SizeXCm);
                int yBoyu = (int)Math.Round(sf.SizeYCm);
                string labelText = string.Format(CultureInfo.InvariantCulture, "TT-{0} ({1}/{2})", sfIndex, xBoyu, yBoyu);
                double footingAngleRad = sf.AngleDeg * Math.PI / 180.0;
                // Temel eksenine göre sol üst köşenin üstüne yaz: rect[3] = sol üst, etiket = köşe + (0, 3) cm (14 - 11 cm aşağı taşındı, temel yerel Y)
                Point2d solUstKose = new Point2d(rect[3].X, rect[3].Y);
                Vector2d upLocal = new Vector2d(0, 3.0);
                Vector2d upWorld = Rotate(upLocal, sf.AngleDeg);
                double labelCx = solUstKose.X + upWorld.X;
                double labelCy = solUstKose.Y + upWorld.Y;
                DrawTemelIsmiLabel(tr, btr, btr.Database, labelCx, labelCy, labelText, footingAngleRad, bottomLeftAligned: true);
            }
        }

        private void DrawTieBeams(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY, Geometry kolonPerdeUnion = null, List<(Geometry geom, double widthCm, double heightDisplayCm, double kot, bool isRadyeTemelHatili)> temelHatiliRaws = null)
        {
            const string layerTemel = "TEMEL (BEYKENT)";
            const string layerHatili = "TEMEL HATILI (BEYKENT)";
            var factory = _ntsDrawFactory;
            Geometry cfUnion = BuildContinuousFoundationsUnion(offsetX, offsetY);
            Geometry slabUnion = BuildSlabFoundationsUnion(offsetX, offsetY);
            var hatiliRaws = new List<(Geometry geom, double widthCm, double heightDisplayCm, double kot, bool isRadyeTemelHatili)>();

            // Bağ kirişi etiketi: aynı (eni, yükseklik) = (WidthCm, HeightCm) olanlar aynı numara (01); farklı kesitler 02, 03... (BK-xx (eni/yükseklik)).
            var tieBeamLabelInfos = new Dictionary<int, (int numero, double lengthCm)>();
            var passIndexToKey = new List<(int passIndex, int w, int h)>();
            int passIndex = 0;
            foreach (var tb in _model.TieBeams)
            {
                passIndex++;
                if (!_axisService.TryIntersect(tb.FixedAxisId, tb.StartAxisId, out Point2d p1) ||
                    !_axisService.TryIntersect(tb.FixedAxisId, tb.EndAxisId, out Point2d p2))
                    continue;
                if (p1.GetDistanceTo(p2) <= 1e-9) continue;
                int offsetForBeam = (tb.FixedAxisId >= 1001 && tb.FixedAxisId <= 1999) ? -tb.OffsetRaw : tb.OffsetRaw;
                ComputeBeamEdgeOffsets(offsetForBeam, tb.WidthCm / 2.0, out double upperEdge, out double lowerEdge);
                Vector2d perp = new Vector2d(-(p2.Y - p1.Y), (p2.X - p1.X));
                Point2d[] rectLocal = new[]
                {
                    p1 + perp.MultiplyBy(upperEdge),
                    p2 + perp.MultiplyBy(upperEdge),
                    p2 + perp.MultiplyBy(lowerEdge),
                    p1 + perp.MultiplyBy(lowerEdge)
                };
                var coordsPass = new Coordinate[5];
                for (int i = 0; i < 4; i++) coordsPass[i] = new Coordinate(rectLocal[i].X + offsetX, rectLocal[i].Y + offsetY);
                coordsPass[4] = coordsPass[0];
                var tbPolyPass = factory.CreatePolygon(factory.CreateLinearRing(coordsPass));
                bool insideContinuous = cfUnion != null && !cfUnion.IsEmpty && cfUnion.Contains(tbPolyPass);
                bool insideSlab = slabUnion != null && !slabUnion.IsEmpty && slabUnion.Contains(tbPolyPass);
                if (insideContinuous || insideSlab) continue;
                double lengthCm = p1.GetDistanceTo(p2);
                int w = (int)Math.Round(tb.WidthCm);
                int h = (int)Math.Round(tb.HeightCm);
                passIndexToKey.Add((passIndex, w, h));
                tieBeamLabelInfos[passIndex] = (0, lengthCm);
            }
            // Kesit (eni,yükseklik) gruplarına göre numara: her farklı (w,h) grubu 1, 2, 3...
            var keyToNumero = new Dictionary<(int w, int h), int>();
            int nextNumero = 1;
            foreach (var (pi, w, h) in passIndexToKey)
            {
                var key = (w, h);
                if (!keyToNumero.TryGetValue(key, out int num))
                    keyToNumero[key] = num = nextNumero++;
                tieBeamLabelInfos[pi] = (num, tieBeamLabelInfos[pi].lengthCm);
            }

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
                var coords = new Coordinate[5];
                for (int i = 0; i < 4; i++)
                    coords[i] = new Coordinate(rect[i].X, rect[i].Y);
                coords[4] = coords[0];
                var tbPoly = factory.CreatePolygon(factory.CreateLinearRing(coords));

                // Tamamen sürekli temel veya radye alanı içinde → TEMEL HATILI (radye temel temel hatılı); dışında → TEMEL (bağ kirişi).
                bool insideContinuous = cfUnion != null && !cfUnion.IsEmpty && cfUnion.Contains(tbPoly);
                bool insideSlab = slabUnion != null && !slabUnion.IsEmpty && slabUnion.Contains(tbPoly);
                string layer = (insideContinuous || insideSlab) ? layerHatili : layerTemel;

                if (layer == layerTemel)
                {
                    if (tieBeamLabelInfos.TryGetValue(tbIndex, out var labelInfo))
                    {
                        int eni = (int)Math.Round(tb.WidthCm);
                        int yukseklik = (int)Math.Round(tb.HeightCm);
                        string labelText = string.Format(CultureInfo.InvariantCulture, "BK-{0:D2} ({1}/{2})", labelInfo.numero, eni, yukseklik);
                        int labelAxisId = tb.LabelAxisId != 0 ? tb.LabelAxisId : tb.FixedAxisId;
                        double axisAngleRad = GetAxisLineAngleRad(labelAxisId);
                        Vector2d perpUnit = perp.GetNormal();
                        double labelCx, labelCy;
                        // 3. sütun 1 yönü (X 1001-1999): etiket sol çizginin 3 cm soluna. Diğerleri: üst kenar + 3 cm.
                        if (tb.LabelAxisId >= 1001 && tb.LabelAxisId <= 1999)
                        {
                            double leftMidX = (rect[2].X + rect[3].X) / 2.0;
                            double leftMidY = (rect[2].Y + rect[3].Y) / 2.0;
                            labelCx = leftMidX - perpUnit.X * 3.0;
                            labelCy = leftMidY - perpUnit.Y * 3.0;
                        }
                        else
                        {
                            double upperMidX = (rect[0].X + rect[1].X) / 2.0;
                            double upperMidY = (rect[0].Y + rect[1].Y) / 2.0;
                            labelCx = upperMidX + perpUnit.X * 3.0;
                            labelCy = upperMidY + perpUnit.Y * 3.0;
                        }
                        DrawTemelIsmiLabel(tr, btr, btr.Database, labelCx, labelCy, labelText, axisAngleRad, bottomLeftAligned: true);
                    }
                }
                else
                {
                    // Radye temel temel hatılı: yazılacak yükseklik = 2. sütun (HeightCm) - konumlandığı radye temelin yüksekliği
                    double radyeYukseklikCm = TryGetSlabThicknessAtPoint(offsetX, offsetY, tbPoly, factory);
                    double heightDisplayCm = Math.Max(0, tb.HeightCm - radyeYukseklikCm);
                    hatiliRaws.Add((tbPoly, tb.WidthCm, heightDisplayCm, _model.BuildingBaseKotu, true));
                }
            }

            var allHatilRaws = new List<(Geometry geom, double widthCm, double heightDisplayCm, double kot, bool isRadyeTemelHatili)>();
            if (temelHatiliRaws != null) allHatilRaws.AddRange(temelHatiliRaws);
            allHatilRaws.AddRange(hatiliRaws);
            if (allHatilRaws.Count > 0)
            {
                var hatilPieces = new List<(Polygon poly, double widthCm, double heightDisplayCm, double kot, bool isRadyeTemelHatili)>();
                foreach (var (geom, w, h, kot, isRadye) in allHatilRaws)
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
                                hatilPieces.Add((pg, w, h, kot, isRadye));
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
                            if (ProperlyTouches(hatilPieces[i].poly, hatilPieces[j].poly, touchToleranceCm, kolonPerdeBoundary))
                                Union(i, j);
                    var componentGroups = new Dictionary<int, List<int>>();
                    for (int i = 0; i < n; i++)
                    {
                        int root = Find(i);
                        if (!componentGroups.ContainsKey(root)) componentGroups[root] = new List<int>();
                        componentGroups[root].Add(i);
                    }
                    const double minHatilAreaCm2 = 1000.0;
                    var components = new List<(Geometry part, double widthCm, double heightDisplayCm, double kot, bool isRadyeTemelHatili)>();
                    foreach (var list in componentGroups.Values)
                    {
                        var polys = list.Select(i => hatilPieces[i].poly).ToList();
                        var polysGeom = polys.Cast<Geometry>().ToList();
                        Geometry part = polys.Count == 1 ? polys[0] : CascadedPolygonUnion.Union(polysGeom);
                        if (part != null && !part.IsEmpty)
                        {
                            part = FilterSmallPolygons(part, minHatilAreaCm2);
                            if (part != null && !part.IsEmpty)
                            {
                                DrawGeometryRingsAsPolylines(tr, btr, part, layerHatili, addHatch: false, exteriorRingsOnly: false, applySmallTriangleTrim: false);
                                var first = hatilPieces[list[0]];
                                components.Add((part, first.widthCm, first.heightDisplayCm, first.kot, first.isRadyeTemelHatili));
                            }
                        }
                    }
                    // Etiketler: kolon/perde kesiminden sonra, birleştirmeden önceki poligonlar üzerinden; konum sağ üst (eksen yatay kabul)
                    DrawRadyeTemelTemelHatiliLabels(tr, btr, btr.Database, hatilPieces);
                }
            }
        }

        /// <summary>Radye temel temel hatılı: TH-01 (en/yükseklik). Sürekli temel altı hatılı: TH (en/yükseklik), numara ve - yok. Yazı yüksekliği 12 cm.</summary>
        private void DrawRadyeTemelTemelHatiliLabels(Transaction tr, BlockTableRecord btr, Database db, List<(Polygon poly, double widthCm, double heightDisplayCm, double kot, bool isRadyeTemelHatili)> pieces)
        {
            if (pieces == null || pieces.Count == 0) return;
            const double labelHeightCm = 12.0;
            var radyeIndices = pieces.Select((p, i) => (p, i)).Where(x => x.p.isRadyeTemelHatili).Select(x => x.i).ToList();
            var keyToIndices = new Dictionary<(int w, int h, int k), List<int>>();
            foreach (int i in radyeIndices)
            {
                var c = pieces[i];
                int w = (int)Math.Round(c.widthCm);
                int h = (int)Math.Round(c.heightDisplayCm);
                int k = (int)Math.Round(c.kot * 100);
                var key = (w, h, k);
                if (!keyToIndices.TryGetValue(key, out var list)) { list = new List<int>(); keyToIndices[key] = list; }
                list.Add(i);
            }
            var keyOrder = keyToIndices.Keys.OrderBy(x => x.w).ThenBy(x => x.h).ThenBy(x => x.k).ToList();
            var indexToNo = new Dictionary<int, int>();
            for (int no = 1; no <= keyOrder.Count; no++)
                foreach (int i in keyToIndices[keyOrder[no - 1]])
                    indexToNo[i] = no;
            int pad = GetLabelPadWidth(keyOrder.Count);

            for (int i = 0; i < pieces.Count; i++)
            {
                var (poly, widthCm, heightDisplayCm, _, isRadyeTemelHatili) = pieces[i];
                if (poly == null || poly.ExteriorRing == null) continue;
                string labelText = isRadyeTemelHatili && indexToNo.TryGetValue(i, out int no)
                    ? string.Format(CultureInfo.InvariantCulture, "TH-{0} ({1}/{2})", no.ToString("D" + pad, CultureInfo.InvariantCulture), (int)Math.Round(widthCm), (int)Math.Round(heightDisplayCm))
                    : string.Format(CultureInfo.InvariantCulture, "TH ({0}/{1})", (int)Math.Round(widthCm), (int)Math.Round(heightDisplayCm));
                if (!GetPolygonPrincipalDirection(poly, out Point2d center, out Vector2d u, out Vector2d perp))
                    continue;
                double angleRad = Math.Atan2(u.Y, u.X);
                bool isFixedX = Math.Abs(u.X) >= Math.Abs(u.Y);
                if (isFixedX)
                    angleRad += Math.PI;
                DrawBeamLabel(tr, btr, db, new Point3d(center.X, center.Y, 0), labelText, labelHeightCm, angleRad, LayerTemelHatiliIsmi, useMiddleCenter: true);
            }
        }

        /// <summary>Poligon merkezi ve en uzun kenar yönü (u) ile dik (perp) döndürür. MultiPolygon/Polygon desteklenir.</summary>
        private static bool GetPolygonPrincipalDirection(Geometry geom, out Point2d center, out Vector2d u, out Vector2d perp)
        {
            center = default;
            u = default;
            perp = default;
            if (geom == null || geom.IsEmpty) return false;
            var ring = geom is Polygon p && p.ExteriorRing != null ? p.ExteriorRing
                : (geom is MultiPolygon mp && mp.NumGeometries > 0 && mp.GetGeometryN(0) is Polygon p0 && p0.ExteriorRing != null ? p0.ExteriorRing : null);
            if (ring == null || ring.NumPoints < 2) return false;
            var cen = geom.Centroid;
            if (cen == null || cen.IsEmpty) return false;
            center = new Point2d(cen.X, cen.Y);
            double maxLen = 0;
            Coordinate prev = ring.Coordinates[0];
            for (int i = 1; i < ring.NumPoints; i++)
            {
                var cur = ring.Coordinates[i];
                double dx = cur.X - prev.X, dy = cur.Y - prev.Y;
                double len = dx * dx + dy * dy;
                if (len > maxLen) { maxLen = len; u = new Vector2d(dx, dy); }
                prev = cur;
            }
            if (maxLen <= 1e-18) return false;
            u = u.GetNormal();
            perp = new Vector2d(-u.Y, u.X);
            return true;
        }

        /// <summary>Poligon merkezi radye içindeyse o radyenin kalınlığını (cm) döndürür.</summary>
        private double TryGetSlabThicknessAtPoint(double offsetX, double offsetY, Geometry poly, GeometryFactory factory)
        {
            if (poly == null || poly.IsEmpty || _model.SlabFoundations == null) return 0;
            var cen = poly.Centroid;
            if (cen == null || cen.IsEmpty) return 0;
            double x = cen.X, y = cen.Y;
            foreach (var sf in _model.SlabFoundations)
            {
                if (!_axisService.TryIntersect(sf.AxisX1, sf.AxisY1, out Point2d p11) ||
                    !_axisService.TryIntersect(sf.AxisX1, sf.AxisY2, out Point2d p12) ||
                    !_axisService.TryIntersect(sf.AxisX2, sf.AxisY1, out Point2d p21) ||
                    !_axisService.TryIntersect(sf.AxisX2, sf.AxisY2, out Point2d p22))
                    continue;
                var coords = new Coordinate[] {
                    new Coordinate(p11.X + offsetX, p11.Y + offsetY),
                    new Coordinate(p12.X + offsetX, p12.Y + offsetY),
                    new Coordinate(p22.X + offsetX, p22.Y + offsetY),
                    new Coordinate(p21.X + offsetX, p21.Y + offsetY),
                    new Coordinate(p11.X + offsetX, p11.Y + offsetY)
                };
                var slabPoly = factory.CreatePolygon(factory.CreateLinearRing(coords));
                if (slabPoly != null && !slabPoly.IsEmpty && slabPoly.Contains(factory.CreatePoint(new Coordinate(x, y))))
                    return sf.ThicknessCm > 0 ? sf.ThicknessCm : 80.0;
            }
            return 0;
        }

        private void DrawSlabFoundations(Transaction tr, BlockTableRecord btr, double offsetX, double offsetY, bool drawTemelOutline = true)
        {
            const string layer = "TEMEL (BEYKENT)";
            var factory = _ntsDrawFactory;
            var polygons = new List<Geometry>();
            var radyeRecords = new List<(SlabFoundationInfo sf, Geometry poly, Point2d center)>();
            foreach (var sf in _model.SlabFoundations)
            {
                if (!_axisService.TryIntersect(sf.AxisX1, sf.AxisY1, out Point2d p11) ||
                    !_axisService.TryIntersect(sf.AxisX1, sf.AxisY2, out Point2d p12) ||
                    !_axisService.TryIntersect(sf.AxisX2, sf.AxisY1, out Point2d p21) ||
                    !_axisService.TryIntersect(sf.AxisX2, sf.AxisY2, out Point2d p22))
                    continue;
                double cx = (p11.X + p12.X + p21.X + p22.X) / 4.0 + offsetX;
                double cy = (p11.Y + p12.Y + p21.Y + p22.Y) / 4.0 + offsetY;
                var coords = new[]
                {
                    new Coordinate(p11.X + offsetX, p11.Y + offsetY),
                    new Coordinate(p21.X + offsetX, p21.Y + offsetY),
                    new Coordinate(p22.X + offsetX, p22.Y + offsetY),
                    new Coordinate(p12.X + offsetX, p12.Y + offsetY),
                    new Coordinate(p11.X + offsetX, p11.Y + offsetY)
                };
                var ring = factory.CreateLinearRing(coords);
                var poly = factory.CreatePolygon(ring);
                if (poly != null && !poly.IsEmpty)
                    radyeRecords.Add((sf, poly, new Point2d(cx, cy)));
                polygons.Add(factory.CreatePolygon(factory.CreateLinearRing(new[]
                {
                    new Coordinate(p11.X, p11.Y),
                    new Coordinate(p21.X, p21.Y),
                    new Coordinate(p22.X, p22.Y),
                    new Coordinate(p12.X, p12.Y),
                    new Coordinate(p11.X, p11.Y)
                })));
            }
            int radyeCount = radyeRecords.Count;
            int radyePad = GetLabelPadWidth(radyeCount);
            Database db = btr.Database;
            ObjectId radyeLabelStyleId = GetOrCreateYaziBeykentTextStyle(tr, db);
            HashSet<int> radyeIndicesToLabel = ComputeRadyeIndicesToLabel(radyeRecords, _model.BuildingBaseKotu);
            for (int i = 0; i < radyeRecords.Count; i++)
            {
                if (!radyeIndicesToLabel.Contains(i)) continue;
                var rec = radyeRecords[i];
                AppendRadyeLabel(tr, btr, rec.sf, i + 1, radyePad, rec.center, radyeLabelStyleId);
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

        /// <summary>Döşeme numarasını kat bilgisi olmadan döndürür (etiket için). SlabFloorKeyStep varsa slabId % step, yoksa % 1000 veya % 100.</summary>
        private int GetSlabNumero(int slabId)
        {
            if (_model.SlabFloorKeyStep > 0) return slabId % _model.SlabFloorKeyStep;
            return slabId >= 1000 ? (slabId % 1000) : (slabId % 100);
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

        /// <summary>Etiket numara hane sayısı: max 1–99 ise 2 hane (01), 100–999 ise 3 hane (001).</summary>
        private static int GetLabelPadWidth(int maxNumber)
        {
            if (maxNumber < 1) return 1;
            return maxNumber < 100 ? 2 : 3;
        }

        /// <summary>Kotlar hariç kalıp içeriğine göre kat imzası: döşeme (ebat, hareketli yük), kiriş/perde (ebat, kot yok), kolon (boyut, yükseklik yok), perde yerleşim ve boyut. Aynı imzaya sahip katlar tek plan olarak çizilir.</summary>
        private string GetFloorFormworkSignature(FloorInfo floor)
        {
            int floorNo = floor.FloorNo;
            var ci = CultureInfo.InvariantCulture;
            var parts = new List<string>();

            // Döşemeler: eksenler, kalınlık, hareketli yük (kot yok)
            var slabLines = new List<string>();
            foreach (var s in _model.Slabs)
            {
                if (GetSlabFloorNo(s.SlabId) != floorNo) continue;
                slabLines.Add(string.Format(ci, "S,{0},{1},{2},{3},{4},{5}", s.Axis1, s.Axis2, s.Axis3, s.Axis4, s.ThicknessCm, s.LiveLoadKNm2));
            }
            slabLines.Sort(StringComparer.Ordinal);
            parts.Add(string.Join("|", slabLines));

            // Kirişler ve perdeler: sabit/başlangıç/bitiş eksen, en, yükseklik, kaçıklık (kot yok); IsWallFlag ile perde ayrımı
            var beamLines = new List<string>();
            foreach (var b in _model.Beams)
            {
                if (GetBeamFloorNo(b.BeamId) != floorNo) continue;
                beamLines.Add(string.Format(ci, "B,{0},{1},{2},{3},{4},{5},{6}", b.FixedAxisId, b.StartAxisId, b.EndAxisId, b.WidthCm, b.HeightCm, b.OffsetRaw, b.IsWallFlag));
            }
            beamLines.Sort(StringComparer.Ordinal);
            parts.Add(string.Join("|", beamLines));

            // Kolonlar: konum (eksen, kaçıklık, açı), boyut (en x boy; yükseklik yok); poligon için kesit kimliği
            var colLines = new List<string>();
            foreach (var col in _model.Columns)
            {
                int sectionId = ResolveColumnSectionId(floorNo, col.ColumnNo);
                int polygonSectionId = ResolvePolygonPositionSectionId(floorNo, col.ColumnNo);
                if (col.ColumnType == 3)
                {
                    if (polygonSectionId <= 0 || !_model.PolygonColumnSectionByPositionSectionId.TryGetValue(polygonSectionId, out int polyId)) continue;
                    colLines.Add(string.Format(ci, "C,{0},{1},{2},{3},{4},{5},P,{6}", col.ColumnNo, col.AxisXId, col.AxisYId, col.OffsetXRaw, col.OffsetYRaw, col.AngleDeg.ToString(ci), polyId));
                }
                else
                {
                    if (sectionId <= 0 || !_model.ColumnDimsBySectionId.TryGetValue(sectionId, out var dim)) continue;
                    colLines.Add(string.Format(ci, "C,{0},{1},{2},{3},{4},{5},R,{6},{7}", col.ColumnNo, col.AxisXId, col.AxisYId, col.OffsetXRaw, col.OffsetYRaw, col.AngleDeg.ToString(ci), dim.W, dim.H));
                }
            }
            colLines.Sort(StringComparer.Ordinal);
            parts.Add(string.Join("|", colLines));

            return string.Join("\n", parts);
        }

        /// <summary>Kalıp imzasına göre kat grupları: her grup aynı planı paylaşır; çizimde gruptan tek plan çizilir.</summary>
        private List<List<int>> GetFormworkFloorGroups()
        {
            var sigToIndices = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < _model.Floors.Count; i++)
            {
                string sig = GetFloorFormworkSignature(_model.Floors[i]);
                if (!sigToIndices.TryGetValue(sig, out var list)) { list = new List<int>(); sigToIndices[sig] = list; }
                list.Add(i);
            }
            return sigToIndices.Values.ToList();
        }

        private void DrawFloorTitle(Transaction tr, BlockTableRecord btr, FloorInfo floor, double offsetX, double offsetY,
            (double Xmin, double Xmax, double Ymin, double Ymax) ext, bool isFoundationPlan = false)
        {
            if (isFoundationPlan && _isTemel50Mode)
            {
                GetSectionCutBalloonExtents(offsetX, offsetY, ext,
                    out _, out _, out double yBottomBalloon, out _,
                    out _, out _, out _, out _);
                double xCenter = offsetX + (ext.Xmin + ext.Xmax) / 2.0;
                double yTopAnchor = yBottomBalloon - Temel50BaslikAltAksBalonBoslukCm;
                var txt = new DBText
                {
                    Layer = LayerBaslik,
                    Height = 30.0,
                    TextStyleId = GetOrCreateYaziBeykentTextStyle(tr, btr.Database),
                    TextString = "%%uTEMEL APLIKASYON PLANI (1:50)%%u",
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextTop,
                    Position = new Point3d(xCenter, yTopAnchor, 0),
                    AlignmentPoint = new Point3d(xCenter, yTopAnchor, 0),
                    LineWeight = LineWeight.LineWeight020
                };
                AppendEntity(tr, btr, txt);
                return;
            }

            var titlePos = new Point3d(offsetX + (ext.Xmin + ext.Xmax) / 2.0, offsetY + ext.Ymax + 45, 0);
            string title = isFoundationPlan
                ? "TEMEL PLANI (aks + 1. kat kolon + surekli/radye temel)"
                : string.Format(CultureInfo.InvariantCulture, "{0} ({1}m)", floor.Name, floor.ElevationM.ToString("0", CultureInfo.InvariantCulture));
            AppendEntity(tr, btr, MakeCenteredText(tr, btr.Database, LayerBaslik, 12, title, titlePos));
        }

        /// <summary>Çizilmeyen benzer katları planın üstüne not olarak yazar (kalıp planı gruplama).</summary>
        private void DrawSimilarFloorsNote(Transaction tr, BlockTableRecord btr, double offsetX, double offsetY,
            (double Xmin, double Xmax, double Ymin, double Ymax) ext, List<FloorInfo> otherFloors)
        {
            if (otherFloors == null || otherFloors.Count == 0) return;
            string names = string.Join(", ", otherFloors.Select(f => string.IsNullOrEmpty(f.ShortName) ? f.Name : f.ShortName));
            string note = names + " bu planla aynıdır (çizilmedi).";
            var notePos = new Point3d(offsetX + (ext.Xmin + ext.Xmax) / 2.0, offsetY + ext.Ymax + 28, 0);
            AppendEntity(tr, btr, MakeCenteredText(tr, btr.Database, LayerBaslik, 8, note, notePos));
        }

        /// <summary>Kiriş/perde etiketi: bottomLeftAligned false ise Right, true ise Left; topAligned true ise üst; useMiddleCenter true ise orta merkez. layer verilmezse KIRIS ISMI.</summary>
        private void DrawBeamLabel(Transaction tr, BlockTableRecord btr, Database db, Point3d insertionPoint, string labelText, double textHeightCm, double rotationRad, string layer = null, bool bottomLeftAligned = true, bool topAligned = false, bool useMiddleCenter = false)
        {
            if (string.IsNullOrEmpty(layer)) layer = LayerKirisYazisi;
            ObjectId textStyleId = GetOrCreateYaziBeykentTextStyle(tr, db);
            var txt = new DBText
            {
                Layer = layer,
                TextStyleId = textStyleId,
                Height = textHeightCm,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(labelText ?? string.Empty),
                Position = insertionPoint,
                HorizontalMode = useMiddleCenter ? TextHorizontalMode.TextCenter : (bottomLeftAligned ? TextHorizontalMode.TextLeft : TextHorizontalMode.TextRight),
                VerticalMode = useMiddleCenter ? TextVerticalMode.TextVerticalMid : (topAligned ? TextVerticalMode.TextTop : TextVerticalMode.TextBottom),
                AlignmentPoint = insertionPoint,
                Rotation = rotationRad
            };
            AppendEntity(tr, btr, txt);
        }

        /// <summary>Sürekli/tekil temel ve bağ kirişi etiketi: TEMEL ISMI (BEYKENT) katmanı, 12 cm yükseklik, YAZI (BEYKENT) stili, 0.2 mm kalınlık. bottomRightAligned: sağ alt (metin sola ve yukarı). bottomLeftAligned: sol alt (metin sağa ve yukarı).</summary>
        private void DrawTemelIsmiLabel(Transaction tr, BlockTableRecord btr, Database db, double centerX, double centerY, string labelText, double rotationRad, bool bottomRightAligned = false, bool bottomLeftAligned = false)
        {
            const double labelHeightCm = 12.0;
            ObjectId textStyleId = GetOrCreateYaziBeykentTextStyle(tr, db);
            var txt = new DBText
            {
                Layer = LayerTemelIsmi,
                TextStyleId = textStyleId,
                Height = labelHeightCm,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(labelText ?? string.Empty),
                Position = new Point3d(centerX, centerY, 0),
                HorizontalMode = bottomRightAligned ? TextHorizontalMode.TextRight : (bottomLeftAligned ? TextHorizontalMode.TextLeft : TextHorizontalMode.TextCenter),
                VerticalMode = (bottomRightAligned || bottomLeftAligned) ? TextVerticalMode.TextBottom : TextVerticalMode.TextVerticalMid,
                AlignmentPoint = new Point3d(centerX, centerY, 0),
                Rotation = rotationRad,
                LineWeight = LineWeight.LineWeight020
            };
            AppendEntity(tr, btr, txt);
        }

        private static DBText MakeCenteredText(Transaction tr, Database db, string layer, double height, string value, Point3d p)
        {
            return new DBText
            {
                Layer = layer,
                Height = height,
                TextStyleId = GetOrCreateYaziBeykentTextStyle(tr, db),
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(value ?? string.Empty),
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

        /// <summary>Radye temel temel hatılı 13. sütun: X/Y aksına göre 0=ortada, ±1=kenar aks üzerinde, &gt;1/&lt;-1=mm mesafe.</summary>
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

        private static void ApplyDashedLinetypeScaleToEntity(Entity e)
        {
            if (e == null || string.IsNullOrEmpty(e.Layer)) return;
            if (e.Layer != LayerAks && e.Layer != LayerKesitSiniri) return;
            try { e.LinetypeScale = DashedLayerEntityLinetypeScale; } catch { /* eski API */ }
        }

        private static void AppendEntity(Transaction tr, BlockTableRecord btr, Entity e)
        {
            btr.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
            ApplyDashedLinetypeScaleToEntity(e);
        }

        /// <summary>Entity ekler ve ObjectId döndürür (tarama boundary için).</summary>
        private static ObjectId AppendEntityReturnId(Transaction tr, BlockTableRecord btr, Entity e)
        {
            btr.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
            ApplyDashedLinetypeScaleToEntity(e);
            return e.ObjectId;
        }

        /// <summary>Kiriş uzatma işareti: daire sınırı içine kırmızı solid dolgu (resimdeki kırmızı nokta gibi).</summary>
        private static void AppendHatchSolidRed(Transaction tr, BlockTableRecord btr, ObjectId boundaryId)
        {
            var hatch = new Hatch();
            btr.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            hatch.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
            hatch.Layer = "KIRIS UZATMA ISARET (BEYKENT)";
            hatch.Associative = true;
            var ids = new ObjectIdCollection { boundaryId };
            hatch.AppendLoop(HatchLoopTypes.Outermost, ids);
            hatch.EvaluateHatch(true);
        }

        /// <summary>Mavi işaret: daire sınırı içine mavi solid dolgu (kırmızı işarete bağlı kirişin karşı ucu; başka kiriş içinde değilse).</summary>
        private static void AppendHatchSolidBlue(Transaction tr, BlockTableRecord btr, ObjectId boundaryId)
        {
            var hatch = new Hatch();
            btr.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            hatch.Color = Color.FromColorIndex(ColorMethod.ByAci, 5);
            hatch.Layer = "KIRIS UZATMA ISARET MAVI (BEYKENT)";
            hatch.Associative = true;
            var ids = new ObjectIdCollection { boundaryId };
            hatch.AppendLoop(HatchLoopTypes.Outermost, ids);
            hatch.EvaluateHatch(true);
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

        /// <summary>Önceden tanımlı desen (ör. grobeton AR-CONC); tarama ayrı katmanda (genelde <see cref="LayerTarama"/>).</summary>
        private static void AppendHatchPredefined(Transaction tr, BlockTableRecord btr, ObjectId boundaryId, string patternName, double patternScale, double patternAngleRad, string hatchLayer)
        {
            var hatch = new Hatch();
            btr.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);
            hatch.SetHatchPattern(HatchPatternType.PreDefined, patternName);
            hatch.PatternScale = patternScale;
            hatch.PatternAngle = patternAngleRad;
            hatch.Layer = hatchLayer;
            hatch.Associative = true;
            var ids = new ObjectIdCollection { boundaryId };
            hatch.AppendLoop(HatchLoopTypes.Outermost, ids);
            try { hatch.EvaluateHatch(true); }
            catch { try { hatch.EvaluateHatch(false); } catch { } }
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
                // Aynı BeamId, aynı aksta, aynı boyutta (kesit/kaçıklık), aynı kot ve aralarında 1 m'den büyük boşluk olmayan kirişleri birleştir. Farklı BeamId veya farklı kottakiler asla birleştirilmez.
                string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}", beam.BeamId, beam.FixedAxisId, beam.WidthCm, beam.HeightCm, beam.OffsetRaw, beam.IsWallFlag, beam.Point1KotCm, beam.Point2KotCm);
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
                            Point1KotCm = s.Beam.Point1KotCm,
                            Point2KotCm = s.Beam.Point2KotCm,
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
                        // Farklı kottaki segmentleri asla birleştirme (aynı BeamId olsa bile)
                        if (next.Beam.Point1KotCm != currentStart.Beam.Point1KotCm || next.Beam.Point2KotCm != currentStart.Beam.Point2KotCm)
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
                        Point1KotCm = b0.Point1KotCm,
                        Point2KotCm = b0.Point2KotCm,
                        IsWallFlag = b0.IsWallFlag
                    });
                }
            }

            merged.AddRange(passthrough);
            return merged;
        }
    }
}
