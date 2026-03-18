using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        /// <summary>Kesit yerleşiminde merdiven/paralel ceza şeridi (cm). Kesit profili çizimi şeritsiz düz hat.</summary>
        private const double SectionStripHalfWidthCm = 50.0;
        private const double SectionLineExtendCm = 120.0;
        /// <summary>Plan üst kenarı ile üst kesit kutusu arası + ek 100 cm.</summary>
        private const double SectionAbovePlanGapCm = 340.0;
        private const double SectionLeftGapFromPlanCm = 320.0;
        /// <summary>Üst kesit kutusunun, üst X aks balon/etiket bandının en az bu kadar üstünde kalması (cm).</summary>
        private const double SectionMinAboveAxisTopLabelsCm = 100.0;
        /// <summary>Çap 40 cm aks balonu ile aynı; ok √2·R oranında.</summary>
        private const double KesitEtiketRadiusCm = 20.0;
        /// <summary>Kesit çizgisi ucu ile kat sınırı dikdörtgeni arasındaki boşluk (cm).</summary>
        private const double SectionCutGapFromFloorBoundaryCm = 30.0;
        private const double KesitEtiketTextHeightCm = 20.0;
        /// <summary>Sol kesit kutusunun sağ kenarının, sol Y aks etiketlerinin en az bu kadar solunda kalması (cm).</summary>
        private const double SectionMinLeftOfAxisLeftLabelsCm = 100.0;
        private const double SectionCutLineWidthCm = 3.0;
        /// <summary>Kesit şemasında minimum kat yüksekliği (cm).</summary>
        private const double SectionMinStoryHeightCm = 280.0;
        private const double SectionStairAvoidBufferCm = 90.0;
        private const double SectionCutOptimizeStepCm = 55.0;
        /// <summary>Kesit sınırı: kiriş alt/üst kotundan taşma (cm).</summary>
        private const double KesitSiniriKotTasmasiCm = 50.0;
        /// <summary>Kiriş yokken döşeme üst kot + bu kadar (cm).</summary>
        private const double KesitSiniriDosemeUstTasmasiCm = 50.0;
        /// <summary>Kiriş yokken döşeme alt kot − bu kadar (cm).</summary>
        private const double KesitSiniriDosemeAltTasmasiCm = 100.0;
        /// <summary>Temel kesiti: kolon/perde hariç eleman alt/üst kot taşması (cm).</summary>
        private const double KesitSiniriTemelKotTasmasiCm = 50.0;
        /// <summary>Kesit sınırı: kesit boyunca sol/sağ taşma (cm).</summary>
        private const double KesitSiniriBoyunaTasmasiCm = 100.0;
        /// <summary>Kesit dilim sırası: sürekli temel (TEMEL etiketi için).</summary>
        private const int SectionOrderContinuousFoundation = 10;
        /// <summary>Temel üst kesitte kolon/perde: kesit sınırı üst çizgisinin üstünde, metin alt kenarına bu kadar (cm); TextBottom ile tamamen sınır dışında.</summary>
        private const double KesitTemelKolonPerdeUstBoslukCm = 20.0;
        /// <summary>Sürekli temel T- etiketi: A-A üstte boşluk (cm).</summary>
        private const double KesitSurekliTemelEtiketGapCm = 5.0;
        /// <summary>Temel sol kesitte sürekli temel T- etiketi: kesit bloğunun sol kenarından bu kadar sola (cm).</summary>
        private const double KesitSurekliTemelEtiketSolBbCm = 10.0;
        private const int SectionOrderSingleFooting = 11;
        private const int SectionOrderSlabFoundation = 12;
        private const int SectionOrderTieBeam = 14;
        private const int SectionOrderHatilStrip = 15;
        private const string LayerTemelHatiliKesit = "TEMEL HATILI (BEYKENT)";
        /// <summary>A-A: ölçü çizgisi eleman sağ kenarından bu kadar sağda (cm).</summary>
        private const double KesitOlcuAaDimLineOffsetCm = 20.0;
        /// <summary>B-B: ölçü çizgisi referans kenarının bu kadar altında (cm).</summary>
        private const double KesitOlcuBbDimLineBelowRefCm = 20.0;
        /// <summary>Toplam + kesişim çift ölçü çizgileri arası (cm).</summary>
        private const double KesitOlcuCiftOlcuAraligiCm = 20.0;
        private const double KesitOlcuKesisimMinCm = 5.0;
        /// <summary>Hatıl–temel Z birleşiminde alt/üst hizaya cm tolerans.</summary>
        private const double KesitOlcuZFlushEpsCm = 4.0;
        private const double KesitOlcuRadyeAralikCm = 2000.0;
        private const double KesitOlcuStaggerCm = 16.0;

        private sealed class SectionSlice
        {
            public double A0, A1, Z0, Z1;
            public string Layer;
            public int Order;
            /// <summary>Kesit şemasında kiriş/kolon/perde etiketi; boşsa yazılmaz.</summary>
            public string Etiket;
        }

        private List<Geometry> BuildStairAvoidZonesForFloor(FloorInfo floor)
        {
            var list = new List<Geometry>();
            var factory = new GeometryFactory();
            int fn = floor.FloorNo;
            foreach (var slab in _model.Slabs)
            {
                if (!_model.StairSlabIds.Contains(slab.SlabId)) continue;
                if (GetSlabFloorNo(slab.SlabId) != fn) continue;
                var p = SlabFootprintPoly(slab);
                if (p == null || p.IsEmpty) continue;
                try { list.Add(p.Buffer(SectionStairAvoidBufferCm)); }
                catch { list.Add(p); }
            }
            return list;
        }

        private List<Geometry> BuildColumnFootprintsForCutScore(FloorInfo floor)
        {
            var list = new List<Geometry>();
            var factory = new GeometryFactory();
            int floorNo = floor.FloorNo;
            foreach (var col in _model.Columns)
            {
                if (!HasColumnOnFloor(floor, col)) continue;
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                int sectionId = ResolveColumnSectionId(floorNo, col.ColumnNo);
                int polygonSectionId = ResolvePolygonPositionSectionId(floorNo, col.ColumnNo);
                if (col.ColumnType == 3 && (polygonSectionId <= 0 || !_model.PolygonColumnSectionByPositionSectionId.ContainsKey(polygonSectionId))) continue;
                if (col.ColumnType != 3 && (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId))) continue;
                var dim = _model.ColumnDimsBySectionId.TryGetValue(sectionId, out var d) ? d : (W: 40.0, H: 40.0);
                double hw = dim.W / 2.0, hh = dim.H / 2.0;
                var offsetLocal = col.ColumnType == 2 ? ComputeColumnOffsetCircle(col.OffsetXRaw, col.OffsetYRaw) : ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                var center = new Point2d(axisNode.X + offsetGlobal.X, axisNode.Y + offsetGlobal.Y);
                Geometry colPoly;
                if (col.ColumnType == 2)
                {
                    var raw = BuildCircleRing(center, Math.Max(hw, hh), col.AngleDeg, 24);
                    var coords = new Coordinate[raw.Length + 1];
                    for (int i = 0; i < raw.Length; i++) coords[i] = new Coordinate(raw[i].X, raw[i].Y);
                    coords[raw.Length] = coords[0];
                    colPoly = factory.CreatePolygon(factory.CreateLinearRing(coords));
                }
                else if (col.ColumnType == 3 && TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out var polyPts))
                {
                    var coords = new Coordinate[polyPts.Length + 1];
                    for (int i = 0; i < polyPts.Length; i++) coords[i] = new Coordinate(polyPts[i].X, polyPts[i].Y);
                    coords[polyPts.Length] = coords[0];
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
                list.Add(colPoly);
            }
            return list;
        }

        /// <summary>Kesit hattının mutlaka üzerinden geçmesi gereken dolu alan (döşeme/kiriş/perde/kolon + temelde temeller).</summary>
        private Geometry BuildCutOccupancyUnion(FloorInfo floor, bool isFoundationPlan)
        {
            var geoms = new List<Geometry>();
            var factory = new GeometryFactory();
            try
            {
                var u = BuildFloorElementUnion(floor);
                if (u != null && !u.IsEmpty) geoms.Add(ReducePrecisionSafe(u, 1) ?? u);
            }
            catch { }
            if (isFoundationPlan)
            {
                foreach (var cf in _model.ContinuousFoundations)
                {
                    var p = ContinuousFootprintPoly(cf);
                    if (p != null && !p.IsEmpty) geoms.Add(p);
                }
                foreach (var sfd in _model.SlabFoundations)
                {
                    var p = SlabFoundationFootprintPoly(sfd);
                    if (p != null && !p.IsEmpty) geoms.Add(p);
                }
                foreach (var tb in _model.TieBeams)
                {
                    var p = TieBeamFootprintPoly(tb);
                    if (p != null && !p.IsEmpty) geoms.Add(p);
                }
                foreach (var sf in _model.SingleFootings)
                {
                    var p = SingleFootingModelPoly(sf, floor);
                    if (p != null && !p.IsEmpty) geoms.Add(p);
                }
            }
            if (geoms.Count == 0) return null;
            try { return geoms.Count == 1 ? geoms[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(geoms); }
            catch
            {
                try
                {
                    var reduced = geoms.Select(g => ReducePrecisionSafe(g, 1)).Where(g => g != null && !g.IsEmpty).ToList();
                    return reduced.Count == 0 ? null : (reduced.Count == 1 ? reduced[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(reduced));
                }
                catch { return geoms[0]; }
            }
        }

        /// <summary>Dikey hat (x sabit): boyunca örnekleme; eleman üzerinde kalma oranı ve en uzun ardışık segment.</summary>
        private static (double frac, double longestRun) SampleVerticalOnOccupancy(Geometry occ, double x, double ymin, double ymax)
        {
            if (occ == null || occ.IsEmpty) return (0, 0);
            var factory = new GeometryFactory();
            int n = Math.Max(24, (int)((ymax - ymin) / 65.0));
            int hit = 0, run = 0, bestRun = 0;
            for (int i = 0; i <= n; i++)
            {
                double yy = ymin + (ymax - ymin) * i / n;
                var pt = factory.CreatePoint(new Coordinate(x, yy));
                bool inside = occ.Contains(pt) || occ.Intersects(pt) || occ.Distance(pt) < 2.0;
                if (inside) { hit++; run++; if (run > bestRun) bestRun = run; }
                else run = 0;
            }
            return ((double)hit / (n + 1), (double)bestRun / (n + 1));
        }

        private static (double frac, double longestRun) SampleHorizontalOnOccupancy(Geometry occ, double y, double xmin, double xmax)
        {
            if (occ == null || occ.IsEmpty) return (0, 0);
            var factory = new GeometryFactory();
            int n = Math.Max(24, (int)((xmax - xmin) / 65.0));
            int hit = 0, run = 0, bestRun = 0;
            for (int i = 0; i <= n; i++)
            {
                double xx = xmin + (xmax - xmin) * i / n;
                var pt = factory.CreatePoint(new Coordinate(xx, y));
                bool inside = occ.Contains(pt) || occ.Intersects(pt) || occ.Distance(pt) < 2.0;
                if (inside) { hit++; run++; if (run > bestRun) bestRun = run; }
                else run = 0;
            }
            return ((double)hit / (n + 1), (double)bestRun / (n + 1));
        }

        private static double StairPenaltyVertical(double x, double ymin, double ymax, double e, List<Geometry> stairZones)
        {
            var factory = new GeometryFactory();
            Geometry strip;
            try
            {
                strip = factory.CreateLineString(new[] { new Coordinate(x, ymin - e), new Coordinate(x, ymax + e) })
                    .Buffer(SectionStripHalfWidthCm + 6.0);
            }
            catch { return 0; }
            double s = 0;
            foreach (var sz in stairZones)
            {
                if (sz != null && strip.Intersects(sz))
                {
                    try { s += strip.Intersection(sz).Area * 0.12; }
                    catch { s += 5000; }
                }
            }
            return s;
        }

        private static double StairPenaltyHorizontal(double y, double xmin, double xmax, double e, List<Geometry> stairZones)
        {
            var factory = new GeometryFactory();
            Geometry strip;
            try
            {
                strip = factory.CreateLineString(new[] { new Coordinate(xmin - e, y), new Coordinate(xmax + e, y) })
                    .Buffer(SectionStripHalfWidthCm + 6.0);
            }
            catch { return 0; }
            double s = 0;
            foreach (var sz in stairZones)
            {
                if (sz != null && strip.Intersects(sz))
                {
                    try { s += strip.Intersection(sz).Area * 0.12; }
                    catch { s += 5000; }
                }
            }
            return s;
        }

        private static Geometry VerticalCutStripPoly(GeometryFactory gf, double x, double ymin, double ymax, double e, double halfW)
        {
            var c = new[]
            {
                new Coordinate(x - halfW, ymin - e),
                new Coordinate(x + halfW, ymin - e),
                new Coordinate(x + halfW, ymax + e),
                new Coordinate(x - halfW, ymax + e),
                new Coordinate(x - halfW, ymin - e)
            };
            return gf.CreatePolygon(c);
        }

        private static Geometry HorizontalCutStripPoly(GeometryFactory gf, double y, double xmin, double xmax, double e, double halfW)
        {
            var c = new[]
            {
                new Coordinate(xmin - e, y - halfW),
                new Coordinate(xmax + e, y - halfW),
                new Coordinate(xmax + e, y + halfW),
                new Coordinate(xmin - e, y + halfW),
                new Coordinate(xmin - e, y - halfW)
            };
            return gf.CreatePolygon(c);
        }

        /// <summary>Kesit şeridi kiriş/perde/temel bağ şeridi **uzanım aksı** ile paralelse (boyuna kesmek) ağır ceza.</summary>
        private double ParallelSpanAxisPenalty(Geometry strip, Vector2d cutLineTangent, FloorInfo floor, bool isFoundationPlan)
        {
            const double parallelDotMin = 0.85;
            double pen = 0;
            int fn = floor.FloorNo;
            void addIfParallel(Geometry poly, Vector2d elemAlong)
            {
                if (poly == null || poly.IsEmpty || strip == null) return;
                try
                {
                    if (!strip.Intersects(poly)) return;
                    var u = elemAlong.GetNormal();
                    double d = Math.Abs(u.X * cutLineTangent.X + u.Y * cutLineTangent.Y);
                    if (d >= parallelDotMin) pen += 26000.0;
                }
                catch { }
            }

            foreach (var beam in MergeSameIdBeamsOnFloor(fn))
            {
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1) ||
                    !_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2)) continue;
                var a = new Point2d(p1.X, p1.Y);
                var b = new Point2d(p2.X, p2.Y);
                NormalizeBeamDirection(beam.FixedAxisId, ref a, ref b);
                Vector2d dirB = b - a;
                if (dirB.Length <= 1e-9) continue;
                addIfParallel(BeamFootprintPoly(beam), dirB);
            }

            if (!isFoundationPlan) return pen;

            foreach (var cf in _model.ContinuousFoundations)
            {
                if (!_axisService.TryIntersect(cf.FixedAxisId, cf.StartAxisId, out Point2d p1) ||
                    !_axisService.TryIntersect(cf.FixedAxisId, cf.EndAxisId, out Point2d p2)) continue;
                Vector2d along = (p2 - p1).GetNormal();
                if (along.Length <= 1e-9) continue;
                addIfParallel(ContinuousFootprintPoly(cf), along);
                if (cf.TieBeamWidthCm > 0)
                    addIfParallel(HatilStripOnContinuousPoly(cf), along);
            }
            foreach (var tb in _model.TieBeams)
            {
                if (!_axisService.TryIntersect(tb.FixedAxisId, tb.StartAxisId, out Point2d p1) ||
                    !_axisService.TryIntersect(tb.FixedAxisId, tb.EndAxisId, out Point2d p2)) continue;
                Vector2d along = (p2 - p1).GetNormal();
                if (along.Length <= 1e-9) continue;
                addIfParallel(TieBeamFootprintPoly(tb), along);
            }
            return pen;
        }

        /// <summary>Planda uzun kenar &gt; 4×kısa kenar ise kesit, kolonun uzun kenarı boyunca içinden geçmesin.</summary>
        private double SlenderColumnLongAxisCutPenaltyVertical(double x, double ymin, double ymax, double e, FloorInfo floor, bool isFoundationPlan)
        {
            var colExtra = GetColumnTableExtraData(floor);
            if (colExtra == null || colExtra.Count == 0) return 0;
            int floorNo = floor.FloorNo;
            var gf = new GeometryFactory();
            double yLo = ymin - e - 5e4, yHi = ymax + e + 5e4;
            var vLine = gf.CreateLineString(new[] { new Coordinate(x, yLo), new Coordinate(x, yHi) });
            const double pen = 42000.0;
            double s = 0;
            foreach (var col in _model.Columns)
            {
                if (col.ColumnType == 2) continue;
                int sectionId = ResolveColumnSectionId(floorNo, col.ColumnNo);
                int polygonSectionId = ResolvePolygonPositionSectionId(floorNo, col.ColumnNo);
                if (col.ColumnType == 3 && (polygonSectionId <= 0 || !_model.PolygonColumnSectionByPositionSectionId.ContainsKey(polygonSectionId))) continue;
                if (col.ColumnType != 3 && (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId))) continue;
                if (!colExtra.ContainsKey(col.ColumnNo)) continue;
                var dim = _model.ColumnDimsBySectionId.TryGetValue(sectionId, out var d) ? d : (W: 40.0, H: 40.0);
                double hw = dim.W / 2.0, hh = dim.H / 2.0;
                double minS = Math.Min(dim.W, dim.H), maxS = Math.Max(dim.W, dim.H);
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                var offsetLocal = ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                var center = new Point2d(axisNode.X + offsetGlobal.X, axisNode.Y + offsetGlobal.Y);
                Geometry colPoly;
                if (col.ColumnType == 3 && TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out var polyPts))
                {
                    var envP = new double[] { double.MaxValue, double.MaxValue, double.MinValue, double.MinValue };
                    foreach (var q in polyPts) { envP[0] = Math.Min(envP[0], q.X); envP[1] = Math.Min(envP[1], q.Y); envP[2] = Math.Max(envP[2], q.X); envP[3] = Math.Max(envP[3], q.Y); }
                    minS = Math.Min(envP[2] - envP[0], envP[3] - envP[1]);
                    maxS = Math.Max(envP[2] - envP[0], envP[3] - envP[1]);
                    var coords = new Coordinate[polyPts.Length + 1];
                    for (int i = 0; i < polyPts.Length; i++) coords[i] = new Coordinate(polyPts[i].X, polyPts[i].Y);
                    coords[polyPts.Length] = coords[0];
                    colPoly = gf.CreatePolygon(gf.CreateLinearRing(coords));
                }
                else
                {
                    var rect = BuildRect(center, hw, hh, col.AngleDeg);
                    var coords = new Coordinate[5];
                    for (int i = 0; i < 4; i++) coords[i] = new Coordinate(rect[i].X, rect[i].Y);
                    coords[4] = coords[0];
                    colPoly = gf.CreatePolygon(gf.CreateLinearRing(coords));
                }
                if (maxS <= 4.0 * minS + 1e-6) continue;
                try
                {
                    if (!vLine.Intersects(colPoly)) continue;
                    var inter = vLine.Intersection(colPoly);
                    if (inter == null || inter.IsEmpty) continue;
                    var env = inter.EnvelopeInternal;
                    double vSpan = env.MaxY - env.MinY;
                    if (vSpan >= maxS * 0.76) s += pen;
                }
                catch { }
            }
            return s;
        }

        private double SlenderColumnLongAxisCutPenaltyHorizontal(double y, double xmin, double xmax, double e, FloorInfo floor, bool isFoundationPlan)
        {
            var colExtra = GetColumnTableExtraData(floor);
            if (colExtra == null || colExtra.Count == 0) return 0;
            int floorNo = floor.FloorNo;
            var gf = new GeometryFactory();
            double xLo = xmin - e - 5e4, xHi = xmax + e + 5e4;
            var hLine = gf.CreateLineString(new[] { new Coordinate(xLo, y), new Coordinate(xHi, y) });
            const double pen = 42000.0;
            double s = 0;
            foreach (var col in _model.Columns)
            {
                if (col.ColumnType == 2) continue;
                int sectionId = ResolveColumnSectionId(floorNo, col.ColumnNo);
                int polygonSectionId = ResolvePolygonPositionSectionId(floorNo, col.ColumnNo);
                if (col.ColumnType == 3 && (polygonSectionId <= 0 || !_model.PolygonColumnSectionByPositionSectionId.ContainsKey(polygonSectionId))) continue;
                if (col.ColumnType != 3 && (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId))) continue;
                if (!colExtra.ContainsKey(col.ColumnNo)) continue;
                var dim = _model.ColumnDimsBySectionId.TryGetValue(sectionId, out var d) ? d : (W: 40.0, H: 40.0);
                double hw = dim.W / 2.0, hh = dim.H / 2.0;
                double minS = Math.Min(dim.W, dim.H), maxS = Math.Max(dim.W, dim.H);
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                var offsetLocal = ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                var center = new Point2d(axisNode.X + offsetGlobal.X, axisNode.Y + offsetGlobal.Y);
                Geometry colPoly;
                if (col.ColumnType == 3 && TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out var polyPts))
                {
                    var envP = new double[] { double.MaxValue, double.MaxValue, double.MinValue, double.MinValue };
                    foreach (var q in polyPts) { envP[0] = Math.Min(envP[0], q.X); envP[1] = Math.Min(envP[1], q.Y); envP[2] = Math.Max(envP[2], q.X); envP[3] = Math.Max(envP[3], q.Y); }
                    minS = Math.Min(envP[2] - envP[0], envP[3] - envP[1]);
                    maxS = Math.Max(envP[2] - envP[0], envP[3] - envP[1]);
                    var coords = new Coordinate[polyPts.Length + 1];
                    for (int i = 0; i < polyPts.Length; i++) coords[i] = new Coordinate(polyPts[i].X, polyPts[i].Y);
                    coords[polyPts.Length] = coords[0];
                    colPoly = gf.CreatePolygon(gf.CreateLinearRing(coords));
                }
                else
                {
                    var rect = BuildRect(center, hw, hh, col.AngleDeg);
                    var coords = new Coordinate[5];
                    for (int i = 0; i < 4; i++) coords[i] = new Coordinate(rect[i].X, rect[i].Y);
                    coords[4] = coords[0];
                    colPoly = gf.CreatePolygon(gf.CreateLinearRing(coords));
                }
                if (maxS <= 4.0 * minS + 1e-6) continue;
                try
                {
                    if (!hLine.Intersects(colPoly)) continue;
                    var inter = hLine.Intersection(colPoly);
                    if (inter == null || inter.IsEmpty) continue;
                    var env = inter.EnvelopeInternal;
                    double hSpan = env.MaxX - env.MinX;
                    if (hSpan >= maxS * 0.76) s += pen;
                }
                catch { }
            }
            return s;
        }

        private double FindBestVerticalCutX(FloorInfo floor, double xmin, double xmax, double ymin, double ymax, double e, Geometry occ, double cx, bool isFoundationPlan)
        {
            var stairs = BuildStairAvoidZonesForFloor(floor);
            var gf = new GeometryFactory();
            double halfW = Math.Max(60.0, SectionStripHalfWidthCm * 1.2);
            double xLo = xmin + 100, xHi = xmax - 100;
            if (xHi <= xLo) return cx;
            double step = Math.Max(40.0, (xHi - xLo) / 48.0);
            double bestX = cx;
            double bestKey = double.NegativeInfinity;
            var tanV = new Vector2d(0, 1);

            void consider(double x)
            {
                var (frac, lng) = SampleVerticalOnOccupancy(occ, x, ymin, ymax);
                Geometry strip = VerticalCutStripPoly(gf, x, ymin, ymax, e, halfW);
                double par = ParallelSpanAxisPenalty(strip, tanV, floor, isFoundationPlan);
                double stair = StairPenaltyVertical(x, ymin, ymax, e, stairs);
                double colLong = SlenderColumnLongAxisCutPenaltyVertical(x, ymin, ymax, e, floor, isFoundationPlan);
                double key = frac * 900.0 + lng * 320.0 - stair - par - colLong - Math.Abs(x - cx) * 0.02;
                if (key > bestKey + 1e-6 || (Math.Abs(key - bestKey) < 1e-6 && Math.Abs(x - cx) < Math.Abs(bestX - cx)))
                {
                    bestKey = key;
                    bestX = x;
                }
            }

            for (double x = xLo; x <= xHi + 1e-6; x += step) consider(x);
            return bestX;
        }

        private double FindBestHorizontalCutY(FloorInfo floor, double xmin, double xmax, double ymin, double ymax, double e, Geometry occ, double cy, bool isFoundationPlan)
        {
            var stairs = BuildStairAvoidZonesForFloor(floor);
            var gf = new GeometryFactory();
            double halfW = Math.Max(60.0, SectionStripHalfWidthCm * 1.2);
            double yLo = ymin + 100, yHi = ymax - 100;
            if (yHi <= yLo) return cy;
            double step = Math.Max(40.0, (yHi - yLo) / 48.0);
            double bestY = cy;
            double bestKey = double.NegativeInfinity;
            var tanH = new Vector2d(1, 0);

            void consider(double y)
            {
                var (frac, lng) = SampleHorizontalOnOccupancy(occ, y, xmin, xmax);
                Geometry strip = HorizontalCutStripPoly(gf, y, xmin, xmax, e, halfW);
                double par = ParallelSpanAxisPenalty(strip, tanH, floor, isFoundationPlan);
                double stair = StairPenaltyHorizontal(y, xmin, xmax, e, stairs);
                double colLong = SlenderColumnLongAxisCutPenaltyHorizontal(y, xmin, xmax, e, floor, isFoundationPlan);
                double key = frac * 900.0 + lng * 320.0 - stair - par - colLong - Math.Abs(y - cy) * 0.02;
                if (key > bestKey + 1e-6 || (Math.Abs(key - bestKey) < 1e-6 && Math.Abs(y - cy) < Math.Abs(bestY - cy)))
                {
                    bestKey = key;
                    bestY = y;
                }
            }

            for (double y = yLo; y <= yHi + 1e-6; y += step) consider(y);
            return bestY;
        }

        private void DrawPlanSections(Transaction tr, BlockTableRecord btr, Database db, FloorInfo floor,
            double offsetX, double offsetY, (double Xmin, double Xmax, double Ymin, double Ymax) ext,
            bool isFoundationPlan)
        {
            double xmin = ext.Xmin, xmax = ext.Xmax, ymin = ext.Ymin, ymax = ext.Ymax;
            double cx = (xmin + xmax) * 0.5;
            double cy = (ymin + ymax) * 0.5;
            double e = SectionLineExtendCm;
            Geometry occ = BuildCutOccupancyUnion(floor, isFoundationPlan);

            // Her zaman: yatay kesit çizgisi → KESİT 1-1 üstte; dikey kesit → KESİT 2-2 solda
            double xvCut = FindBestVerticalCutX(floor, xmin, xmax, ymin, ymax, e, occ, cx, isFoundationPlan);
            double yhCut = FindBestHorizontalCutY(floor, xmin, xmax, ymin, ymax, e, occ, cy, isFoundationPlan);

            Point2d topA = new Point2d(xmin - e, yhCut);
            Point2d topB = new Point2d(xmax + e, yhCut);
            Point2d alongOrigTop = new Point2d(xmin, yhCut);
            Vector2d dirTop = new Vector2d(1, 0);

            Point2d leftA = new Point2d(xvCut, ymin - e);
            Point2d leftB = new Point2d(xvCut, ymax + e);
            Point2d alongOrigLeft = new Point2d(xvCut, ymin);
            Vector2d dirLeft = new Vector2d(0, 1);

            var colExtra = GetColumnTableExtraData(floor);
            DrawSectionCutsRevitStyleOnPlan(tr, btr, db, offsetX, offsetY, ext, xvCut, yhCut, "A", "B");

            var slicesTop = CollectAllSectionSlices(floor, topA, topB, alongOrigTop, dirTop, isFoundationPlan, colExtra);
            var slicesLeft = CollectAllSectionSlices(floor, leftA, leftB, alongOrigLeft, dirLeft, isFoundationPlan, colExtra);

            double planTop = offsetY + ymax;
            double GetAmin(List<SectionSlice> sl) => sl == null || sl.Count == 0 ? 0 : sl.Min(s => s.A0) - 40;
            double GetAmax(List<SectionSlice> sl) => sl == null || sl.Count == 0 ? 200 : sl.Max(s => s.A1) + 40;
            double GetZmin(List<SectionSlice> sl) => sl == null || sl.Count == 0 ? 0 : sl.Min(s => s.Z0) - 25;
            double GetZmax(List<SectionSlice> sl) => sl == null || sl.Count == 0 ? SectionMinStoryHeightCm : sl.Max(s => s.Z1) + 25;

            double aminT = GetAmin(slicesTop), amaxT = GetAmax(slicesTop), minZT = GetZmin(slicesTop), maxZT = GetZmax(slicesTop);
            double spanAT = Math.Max(180.0, amaxT - aminT);
            double spanZT = Math.Max(SectionMinStoryHeightCm * 0.5, maxZT - minZT);

            // Üst kutu: planda yatay kesit hattına göre X hizası (boyunca zincir ortası)
            double alignXTop = xmin + (aminT + amaxT) * 0.5;
            double contentTopX = offsetX + alignXTop - (amaxT - aminT) * 0.5;
            double axisTopBandY = offsetY + ymax + AxisExtensionBeyondBoundaryCm + 55.0;
            double contentTopY = Math.Max(
                planTop + SectionAbovePlanGapCm + 28.0,
                axisTopBandY + SectionMinAboveAxisTopLabelsCm + 12.0);
            ObjectId planOlcuDimId = GetOrCreatePlanOlcuDimStyle(tr, db, 12.0);
            DrawSchematicFromSlicesOneToOne(tr, btr, slicesTop, contentTopX, contentTopY, aminT, minZT, spanAT, spanZT, horizontalAlongX: true, mirrorElevationX: false, drawReferenceAxis: false);
            DrawKesitSiniriFromBeams(tr, btr, slicesTop, contentTopX, contentTopY, aminT, minZT, spanZT, horizontalAlongX: true, mirrorElevationX: false, isFoundationPlan);
            DrawKesitSchematicDimensions(tr, btr, slicesTop, contentTopX, contentTopY, aminT, minZT, spanZT, horizontalAlongX: true, mirrorElevationX: false, isFoundationPlan, planOlcuDimId);
            DrawKesitSchematicElementLabels(tr, btr, db, slicesTop, floor, contentTopX, contentTopY, aminT, minZT, spanZT, true, false, isFoundationPlan);
            DrawKesitTitleBelowSchematic(tr, btr, db, "A-A KESİTİ", contentTopX + spanAT * 0.5, contentTopY - 14.0);

            double aminL = GetAmin(slicesLeft), amaxL = GetAmax(slicesLeft), minZL = GetZmin(slicesLeft), maxZL = GetZmax(slicesLeft);
            double spanAL = Math.Max(180.0, amaxL - aminL);
            double spanZL = Math.Max(SectionMinStoryHeightCm * 0.5, maxZL - minZL);
            // Sol kutu: planda dikey kesit X = xvCut → boyunca Y zinciri; Y hizası
            double alignYLeft = ymin + (aminL + amaxL) * 0.5;
            double contentLeftY = offsetY + alignYLeft - spanAL * 0.5;
            double labelLeftEdgeX = offsetX + xmin - AxisExtensionBeyondBoundaryCm - 30.0;
            double schematicRightMaxX = labelLeftEdgeX - SectionMinLeftOfAxisLeftLabelsCm;
            double contentLeftX = Math.Min(
                offsetX + xmin - SectionLeftGapFromPlanCm - spanZL - 28.0,
                schematicRightMaxX - spanZL - 28.0);
            DrawSchematicFromSlicesOneToOne(tr, btr, slicesLeft, contentLeftX, contentLeftY, aminL, minZL, spanAL, spanZL, horizontalAlongX: false, mirrorElevationX: true, drawReferenceAxis: false);
            DrawKesitSiniriFromBeams(tr, btr, slicesLeft, contentLeftX, contentLeftY, aminL, minZL, spanZL, horizontalAlongX: false, mirrorElevationX: true, isFoundationPlan);
            DrawKesitSchematicDimensions(tr, btr, slicesLeft, contentLeftX, contentLeftY, aminL, minZL, spanZL, horizontalAlongX: false, mirrorElevationX: true, isFoundationPlan, planOlcuDimId);
            DrawKesitSchematicElementLabels(tr, btr, db, slicesLeft, floor, contentLeftX, contentLeftY, aminL, minZL, spanZL, false, true, isFoundationPlan);
            DrawKesitTitleVerticalRightOfSection(tr, btr, db, "B-B KESİTİ", contentLeftX + spanZL + 48.0, contentLeftY + spanAL * 0.5);
        }

        /// <summary>Üst kesit başlığı şemanın altında (Y aşağı doğru).</summary>
        private void DrawKesitTitleBelowSchematic(Transaction tr, BlockTableRecord btr, Database db, string title, double cx, double yTopAnchor)
        {
            var txt = new DBText
            {
                Layer = LayerKesitIsmi,
                Height = 34,
                TextStyleId = GetOrCreateYaziBeykentTextStyle(tr, db),
                TextString = title,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextTop,
                Position = new Point3d(cx, yTopAnchor, 0),
                AlignmentPoint = new Point3d(cx, yTopAnchor, 0),
                LineWeight = LineWeight.LineWeight020
            };
            AppendEntity(tr, btr, txt);
        }

        private void DrawKesitTitleVerticalRightOfSection(Transaction tr, BlockTableRecord btr, Database db, string title, double x, double cy)
        {
            var txt = new DBText
            {
                Layer = LayerKesitIsmi,
                Height = 32,
                TextStyleId = GetOrCreateYaziBeykentTextStyle(tr, db),
                TextString = title,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                Position = new Point3d(x, cy, 0),
                AlignmentPoint = new Point3d(x, cy, 0),
                Rotation = Math.PI / 2.0,
                LineWeight = LineWeight.LineWeight020
            };
            AppendEntity(tr, btr, txt);
        }

        /// <summary>Kesit çizgisi daireden kat sınırına 30 cm kala biter; dışa uzamaz.</summary>
        private void DrawSectionCutsRevitStyleOnPlan(Transaction tr, BlockTableRecord btr, Database db,
            double offsetX, double offsetY, (double Xmin, double Xmax, double Ymin, double Ymax) ext,
            double xvCut, double yhCut, string letterH, string letterV)
        {
            GetSectionCutBalloonExtents(offsetX, offsetY, ext, out double xL, out double xR, out double yB, out double yT,
                out List<Point3d> xBot, out List<Point3d> xTop, out List<Point3d> yL, out List<Point3d> yR);
            double Xmi = offsetX + ext.Xmin, Xma = offsetX + ext.Xmax;
            double Ymi = offsetY + ext.Ymin, Yma = offsetY + ext.Ymax;
            double yw = offsetY + yhCut;
            double xw = offsetX + xvCut;
            var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            ObjectId dashId = lt.Has("DASHED") ? lt["DASHED"] : (lt.Has("Dashed") ? lt["Dashed"] : ObjectId.Null);

            string hL = string.IsNullOrEmpty(letterH) ? "?" : letterH.ToUpperInvariant();
            string vL = string.IsNullOrEmpty(letterV) ? "?" : letterV.ToUpperInvariant();
            double Rlab = KesitEtiketRadiusCm;
            double g = SectionCutGapFromFloorBoundaryCm;
            const double tol = 0.02;

            Point3d cHL = new Point3d(xL, yw, 0);
            Point3d cHR = new Point3d(xR, yw, 0);

            // Yatay: sol — daireden Xmi−g’ye; sağ — Xma+g’den daireye (kat sınırına g cm yaklaşmadan durur)
            double xEndL = Xmi - g;
            if (xL + Rlab < xEndL - tol)
                AppendDashedCutSegment(tr, btr, dashId, xL + Rlab, yw, xEndL, yw);
            double xStartR = Xma + g;
            if (xR - Rlab > xStartR + tol)
                AppendDashedCutSegment(tr, btr, dashId, xStartR, yw, xR - Rlab, yw);
            if (yL.Count + yR.Count > 0)
            {
                DrawKesitEtiketDxfStyle(tr, btr, db, cHL, new Vector2d(1, 0), new Vector2d(0, 1), false, hL);
                DrawKesitEtiketDxfStyle(tr, btr, db, cHR, new Vector2d(1, 0), new Vector2d(0, 1), true, hL);
            }

            Point3d cVB = new Point3d(xw, yB, 0);
            Point3d cVT = new Point3d(xw, yT, 0);

            double yEndB = Ymi - g;
            if (yB + Rlab < yEndB - tol)
                AppendDashedCutSegment(tr, btr, dashId, xw, yB + Rlab, xw, yEndB);
            double yStartT = Yma + g;
            if (yT - Rlab > yStartT + tol)
                AppendDashedCutSegment(tr, btr, dashId, xw, yStartT, xw, yT - Rlab);
            if (xBot.Count + xTop.Count > 0)
            {
                DrawKesitEtiketDxfStyle(tr, btr, db, cVB, new Vector2d(0, 1), new Vector2d(-1, 0), false, vL);
                DrawKesitEtiketDxfStyle(tr, btr, db, cVT, new Vector2d(0, 1), new Vector2d(-1, 0), true, vL);
            }
        }

        private static void AppendDashedCutSegment(Transaction tr, BlockTableRecord btr, ObjectId dashId, double x0, double y0, double x1, double y1)
        {
            if (Math.Abs(x1 - x0) + Math.Abs(y1 - y0) < 0.02) return;
            var pl = new Polyline(2);
            pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x1, y1), 0, 0, 0);
            pl.Layer = LayerKesit;
            pl.ConstantWidth = 0;
            if (!dashId.IsNull) pl.LinetypeId = dashId;
            AppendEntity(tr, btr, pl);
        }

        private static List<(Point3d a, Point3d b)> KesitOutlineOutsideCircle(Point3d pA, Point3d pB, Point3d o, double R)
        {
            var res = new List<(Point3d, Point3d)>();
            double dx = pB.X - pA.X, dy = pB.Y - pA.Y;
            double fx = pA.X - o.X, fy = pA.Y - o.Y;
            double A = dx * dx + dy * dy;
            if (A < 1e-12) return res;
            double B = 2 * (fx * dx + fy * dy);
            double C = fx * fx + fy * fy - R * R;
            double fbx = pB.X - o.X, fby = pB.Y - o.Y;
            const double eps = 1e-3;
            bool outA = fx * fx + fy * fy >= R * R - 0.01;
            bool outB = fbx * fbx + fby * fby >= R * R - 0.01;
            double disc = B * B - 4 * A * C;
            if (disc < 1e-8)
            {
                if (outA && outB) res.Add((pA, pB));
                return res;
            }
            double sd = Math.Sqrt(disc);
            double t1 = (-B - sd) / (2 * A);
            double t2 = (-B + sd) / (2 * A);
            if (t1 > t2) { double z = t1; t1 = t2; t2 = z; }
            t1 = Math.Max(0, Math.Min(1, t1));
            t2 = Math.Max(0, Math.Min(1, t2));
            if (t2 - t1 < 1e-4)
            {
                if (outA && outB) res.Add((pA, pB));
                return res;
            }
            if (t1 > eps)
                res.Add((pA, new Point3d(pA.X + t1 * dx, pA.Y + t1 * dy, 0)));
            if (t2 < 1 - eps)
                res.Add((new Point3d(pA.X + t2 * dx, pA.Y + t2 * dy, 0), pB));
            return res;
        }

        private void DrawKesitEtiketDxfStyle(Transaction tr, BlockTableRecord btr, Database db, Point3d center,
            Vector2d unitAlongCut, Vector2d unitArrow, bool mirrorAlong, string letter)
        {
            double R = KesitEtiketRadiusCm;
            var t = unitAlongCut.GetNormal();
            var n = unitArrow.GetNormal();
            double w = R * Math.Sqrt(2.0);
            double sx = mirrorAlong ? -1.0 : 1.0;

            Point3d W(double lx, double ly) =>
                new Point3d(center.X + t.X * lx * sx + n.X * ly, center.Y + t.Y * lx * sx + n.Y * ly, 0);

            var p0 = W(R, 0);
            var p1 = W(w, 0);
            var apex = W(0, w);
            var p3 = W(-w, 0);
            var p4 = W(-R, 0);

            var circ = new Circle(center, Vector3d.ZAxis, R) { Layer = LayerKesit, LineWeight = LineWeight.LineWeight060 };
            AppendEntity(tr, btr, circ);

            string ch = letter.Length > 0 ? letter.Substring(0, 1) : "?";
            var dt = new DBText
            {
                Layer = LayerKesitIsmi,
                Height = KesitEtiketTextHeightCm,
                TextStyleId = GetOrCreateYaziBeykentTextStyle(tr, db),
                TextString = ch,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                Position = center,
                AlignmentPoint = center,
                LineWeight = LineWeight.LineWeight020
            };
            AppendEntity(tr, btr, dt);

            void drawSeg(Point3d a, Point3d b)
            {
                foreach (var seg in KesitOutlineOutsideCircle(a, b, center, R))
                {
                    var pln = new Polyline(2);
                    pln.AddVertexAt(0, new Point2d(seg.a.X, seg.a.Y), 0, 0, 0);
                    pln.AddVertexAt(1, new Point2d(seg.b.X, seg.b.Y), 0, 0, 0);
                    pln.Layer = LayerKesit;
                    pln.LineWeight = LineWeight.LineWeight060;
                    pln.ConstantWidth = 0;
                    AppendEntity(tr, btr, pln);
                }
            }
            drawSeg(p0, p1);
            drawSeg(p1, apex);
            drawSeg(apex, p3);
            drawSeg(p3, p4);
        }

        /// <summary>Çizgi–poligon kesişiminde her ardışık iç segment ayrı (çökük poligon / iç bölmeler).</summary>
        private static List<(double a0, double a1)> AlongSpansFromLinePolygonIntersect(Geometry inter, Point2d alongOrigin, Vector2d dirUnit)
        {
            var du = dirUnit.GetNormal();
            double T(NetTopologySuite.Geometries.Coordinate c) =>
                (c.X - alongOrigin.X) * du.X + (c.Y - alongOrigin.Y) * du.Y;
            var spans = new List<(double, double)>();
            void fromLs(LineString ls)
            {
                if (ls == null || ls.IsEmpty) return;
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (var c in ls.Coordinates)
                {
                    double t = T(c);
                    lo = Math.Min(lo, t);
                    hi = Math.Max(hi, t);
                }
                if (lo > hi) return;
                if (hi - lo < 2.0) { double m = (lo + hi) * 0.5; lo = m - 4.0; hi = m + 4.0; }
                spans.Add((lo, hi));
            }
            void walk(Geometry g)
            {
                if (g == null || g.IsEmpty) return;
                if (g is LineString ls) { fromLs(ls); return; }
                if (g is MultiLineString ml)
                {
                    for (int i = 0; i < ml.NumGeometries; i++)
                        fromLs((LineString)ml.GetGeometryN(i));
                    return;
                }
                if (g is NetTopologySuite.Geometries.Point pt)
                {
                    double t = T(pt.Coordinate);
                    spans.Add((t - 5.0, t + 5.0));
                    return;
                }
                int n = g.NumGeometries;
                for (int i = 0; i < n; i++)
                    walk(g.GetGeometryN(i));
            }
            walk(inter);
            return spans;
        }

        /// <summary>Kesit düzlemi = çizgi; poligon kolonda birden fazla parça ayrı dilim.</summary>
        private void TryAddSliceCutLine(Geometry cutLine, Geometry footprint, Point2d alongOrigin, Vector2d dirUnit,
            double z0, double z1, string layer, int order, List<SectionSlice> list, string etiket = null)
        {
            if (footprint == null || footprint.IsEmpty || cutLine == null || cutLine.IsEmpty) return;
            try
            {
                if (!cutLine.Intersects(footprint)) return;
                var inter = cutLine.Intersection(footprint);
                if (inter == null || inter.IsEmpty) return;
                var du = dirUnit.GetNormal();
                foreach (var (a0, a1) in AlongSpansFromLinePolygonIntersect(inter, alongOrigin, du))
                    list.Add(new SectionSlice { A0 = a0, A1 = a1, Z0 = z0, Z1 = z1, Layer = layer, Order = order, Etiket = etiket });
            }
            catch { /* non-noded */ }
        }

        private string FormatSectionBeamSliceLabel(BeamInfo beam, FloorInfo floor)
        {
            int bf = GetBeamFloorNo(beam.BeamId);
            var fl = floor != null && floor.FloorNo == bf ? floor : _model.Floors.FirstOrDefault(f => f.FloorNo == bf);
            string kat = fl != null && !string.IsNullOrEmpty(fl.ShortName) ? fl.ShortName : bf.ToString(CultureInfo.InvariantCulture);
            int n = GetBeamNumero(beam.BeamId);
            int maxN = _model.Beams.Where(b => GetBeamFloorNo(b.BeamId) == bf && b.IsWallFlag != 1).Select(b => GetBeamNumero(b.BeamId)).DefaultIfEmpty(0).Max();
            int pad = GetLabelPadWidth(Math.Max(maxN, n));
            return string.Format(CultureInfo.InvariantCulture, "K{0}{1} ({2}/{3})", kat, n.ToString("D" + pad, CultureInfo.InvariantCulture),
                (int)Math.Round(beam.WidthCm), (int)Math.Round(beam.HeightCm));
        }

        private string FormatSectionPerdeSliceLabel(BeamInfo beam, FloorInfo floor)
        {
            int bf = GetBeamFloorNo(beam.BeamId);
            var fl = floor != null && floor.FloorNo == bf ? floor : _model.Floors.FirstOrDefault(f => f.FloorNo == bf);
            string kat = fl != null && !string.IsNullOrEmpty(fl.ShortName) ? fl.ShortName : bf.ToString(CultureInfo.InvariantCulture);
            int n = GetBeamNumero(beam.BeamId);
            int maxN = _model.Beams.Where(b => GetBeamFloorNo(b.BeamId) == bf && b.IsWallFlag == 1).Select(b => GetBeamNumero(b.BeamId)).DefaultIfEmpty(0).Max();
            int pad = GetLabelPadWidth(Math.Max(maxN, n));
            return string.Format(CultureInfo.InvariantCulture, "P{0}{1} ({2}/{3})", kat, n.ToString("D" + pad, CultureInfo.InvariantCulture),
                (int)Math.Round(beam.WidthCm), (int)Math.Round(beam.HeightCm));
        }

        private string FormatSectionKolonSliceLabel(ColumnAxisInfo col, FloorInfo floor, (double W, double H) dim)
        {
            int maxCol = _model.Columns.Count > 0 ? _model.Columns.Max(c => c.ColumnNo) : 99;
            int colPad = GetLabelPadWidth(maxCol);
            string storyId = floor != null && !string.IsNullOrEmpty(floor.ShortName)
                ? floor.ShortName
                : (floor != null ? floor.FloorNo.ToString(CultureInfo.InvariantCulture) : "B");
            string nameLine = "S" + storyId + col.ColumnNo.ToString("D" + colPad, CultureInfo.InvariantCulture);
            string dimLine = col.ColumnType == 2
                ? "R= " + dim.W.ToString("F0", CultureInfo.InvariantCulture)
                : string.Format(CultureInfo.InvariantCulture, "({0:F0}/{1:F0})", dim.W, dim.H);
            return nameLine + " " + dimLine;
        }

        private Geometry BeamFootprintPoly(BeamInfo beam)
        {
            var factory = new GeometryFactory();
            if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1) ||
                !_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2)) return null;
            var a = new Point2d(p1.X, p1.Y);
            var b = new Point2d(p2.X, p2.Y);
            NormalizeBeamDirection(beam.FixedAxisId, ref a, ref b);
            Vector2d dir = b - a;
            if (dir.Length <= 1e-9) return null;
            Vector2d perp = new Vector2d(-dir.Y, dir.X).GetNormal();
            double hw = beam.WidthCm / 2.0;
            ComputeBeamEdgeOffsets(beam.OffsetRaw, hw, out double ue, out double le);
            var coords = new[]
            {
                C1(a.X + perp.X * ue, a.Y + perp.Y * ue),
                C1(b.X + perp.X * ue, b.Y + perp.Y * ue),
                C1(b.X + perp.X * le, b.Y + perp.Y * le),
                C1(a.X + perp.X * le, a.Y + perp.Y * le),
                C1(a.X + perp.X * ue, a.Y + perp.Y * ue)
            };
            return factory.CreatePolygon(factory.CreateLinearRing(coords));
        }

        private Geometry ContinuousFootprintPoly(ContinuousFoundationInfo cf)
        {
            var factory = new GeometryFactory();
            if (!_axisService.TryIntersect(cf.FixedAxisId, cf.StartAxisId, out Point2d p1) ||
                !_axisService.TryIntersect(cf.FixedAxisId, cf.EndAxisId, out Point2d p2)) return null;
            Vector2d along = (p2 - p1).GetNormal();
            if (p1.GetDistanceTo(p2) <= 1e-9) return null;
            Point2d p1Eff = p1 - along.MultiplyBy(cf.StartExtensionCm);
            Point2d p2Eff = p2 + along.MultiplyBy(cf.EndExtensionCm);
            int offsetForBeam = (cf.FixedAxisId >= 1001 && cf.FixedAxisId <= 1999) ? -cf.OffsetRaw : cf.OffsetRaw;
            ComputeBeamEdgeOffsets(offsetForBeam, cf.WidthCm / 2.0, out double ue, out double le);
            Vector2d perp = new Vector2d(-along.Y, along.X);
            var coords = new[]
            {
                C1(p1Eff.X + perp.X * ue, p1Eff.Y + perp.Y * ue),
                C1(p2Eff.X + perp.X * ue, p2Eff.Y + perp.Y * ue),
                C1(p2Eff.X + perp.X * le, p2Eff.Y + perp.Y * le),
                C1(p1Eff.X + perp.X * le, p1Eff.Y + perp.Y * le),
                C1(p1Eff.X + perp.X * ue, p1Eff.Y + perp.Y * ue)
            };
            return factory.CreatePolygon(factory.CreateLinearRing(coords));
        }

        private Geometry TieBeamFootprintPoly(TieBeamInfo tb)
        {
            var factory = new GeometryFactory();
            if (!_axisService.TryIntersect(tb.FixedAxisId, tb.StartAxisId, out Point2d p1) ||
                !_axisService.TryIntersect(tb.FixedAxisId, tb.EndAxisId, out Point2d p2)) return null;
            Vector2d along = (p2 - p1).GetNormal();
            if (p1.GetDistanceTo(p2) <= 1e-9) return null;
            int offsetForBeam = (tb.FixedAxisId >= 1001 && tb.FixedAxisId <= 1999) ? -tb.OffsetRaw : tb.OffsetRaw;
            ComputeBeamEdgeOffsets(offsetForBeam, tb.WidthCm / 2.0, out double ue, out double le);
            Vector2d perp = new Vector2d(-along.Y, along.X);
            var coords = new[]
            {
                C1(p1.X + perp.X * ue, p1.Y + perp.Y * ue),
                C1(p2.X + perp.X * ue, p2.Y + perp.Y * ue),
                C1(p2.X + perp.X * le, p2.Y + perp.Y * le),
                C1(p1.X + perp.X * le, p1.Y + perp.Y * le),
                C1(p1.X + perp.X * ue, p1.Y + perp.Y * ue)
            };
            return factory.CreatePolygon(factory.CreateLinearRing(coords));
        }

        private Geometry SlabFoundationFootprintPoly(SlabFoundationInfo sf)
        {
            var factory = new GeometryFactory();
            if (!_axisService.TryIntersect(sf.AxisX1, sf.AxisY1, out Point2d p11) ||
                !_axisService.TryIntersect(sf.AxisX1, sf.AxisY2, out Point2d p12) ||
                !_axisService.TryIntersect(sf.AxisX2, sf.AxisY1, out Point2d p21) ||
                !_axisService.TryIntersect(sf.AxisX2, sf.AxisY2, out Point2d p22)) return null;
            var coords = new[] { C1(p11.X, p11.Y), C1(p21.X, p21.Y), C1(p22.X, p22.Y), C1(p12.X, p12.Y), C1(p11.X, p11.Y) };
            return factory.CreatePolygon(factory.CreateLinearRing(coords));
        }

        private Geometry HatilStripOnContinuousPoly(ContinuousFoundationInfo cf)
        {
            if (cf.TieBeamWidthCm <= 0) return null;
            var factory = new GeometryFactory();
            if (!_axisService.TryIntersect(cf.FixedAxisId, cf.StartAxisId, out Point2d p1) ||
                !_axisService.TryIntersect(cf.FixedAxisId, cf.EndAxisId, out Point2d p2)) return null;
            Vector2d along = (p2 - p1).GetNormal();
            if (p1.GetDistanceTo(p2) <= 1e-9) return null;
            ComputeTieBeamEdgeOffsets(cf.FixedAxisId, cf.TieBeamOffsetRaw, cf.TieBeamWidthCm / 2.0, out double hu, out double hl);
            Vector2d perp = new Vector2d(-along.Y, along.X);
            var coords = new[]
            {
                C1(p1.X + perp.X * hu, p1.Y + perp.Y * hu),
                C1(p2.X + perp.X * hu, p2.Y + perp.Y * hu),
                C1(p2.X + perp.X * hl, p2.Y + perp.Y * hl),
                C1(p1.X + perp.X * hl, p1.Y + perp.Y * hl),
                C1(p1.X + perp.X * hu, p1.Y + perp.Y * hu)
            };
            return factory.CreatePolygon(factory.CreateLinearRing(coords));
        }

        private Geometry SingleFootingModelPoly(SingleFootingInfo sf, FloorInfo floor)
        {
            int positionIndex = sf.ColumnRef - 100;
            if (positionIndex < 1 || positionIndex > _model.ColumnAxisPositions.Count) return null;
            var pos = _model.ColumnAxisPositions[positionIndex - 1];
            if (!_axisService.TryIntersect(pos.AxisXId, pos.AxisYId, out Point2d axisNode)) return null;
            int colNo = positionIndex;
            int sectionId = ResolveColumnSectionId(floor.FloorNo, colNo);
            double hw = 20.0, hh = 20.0;
            if (sectionId > 0 && _model.ColumnDimsBySectionId.TryGetValue(sectionId, out var dim)) { hw = dim.W / 2.0; hh = dim.H / 2.0; }
            var offsetLocal = ComputeColumnOffset(pos.OffsetXRaw, pos.OffsetYRaw, hw, hh);
            var offsetGlobal = Rotate(offsetLocal, pos.AngleDeg);
            var columnCenter = new Point2d(axisNode.X + offsetGlobal.X, axisNode.Y + offsetGlobal.Y);
            double halfX = sf.SizeXCm / 2.0;
            double halfY = sf.SizeYCm / 2.0;
            double cx = 0, cy = 0;
            if (sf.AlignX == 1) cx = 1.0;
            else if (sf.AlignX == 2) cx = -1.0;
            if (sf.AlignY == 1) cy = -1.0;
            else if (sf.AlignY == 2) cy = 1.0;
            Point2d footingCenter;
            if (Math.Abs(sf.AngleDeg) > 0.01 || Math.Abs(pos.AngleDeg) > 0.01)
            {
                double angleRad = sf.AngleDeg * Math.PI / 180.0;
                Vector2d uFootX = new Vector2d(Math.Cos(angleRad), Math.Sin(angleRad));
                Vector2d uFootY = new Vector2d(-Math.Sin(angleRad), Math.Cos(angleRad));
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
                double k1 = (sf.AlignX == 1) ? (maxUx - halfX) : (sf.AlignX == 2) ? (minUx + halfX) : (columnCenter.X * uFootX.X + columnCenter.Y * uFootX.Y);
                double k2 = (sf.AlignY == 1) ? (minUy + halfY) : (sf.AlignY == 2) ? (maxUy - halfY) : (columnCenter.X * uFootY.X + columnCenter.Y * uFootY.Y);
                footingCenter = new Point2d(k1 * uFootX.X + k2 * uFootY.X, k1 * uFootX.Y + k2 * uFootY.Y);
            }
            else
            {
                Vector2d columnVec = new Vector2d(cx * hw, cy * hh);
                Vector2d footingVec = new Vector2d(cx * halfX, cy * halfY);
                Vector2d alignGlobal = Rotate(columnVec, pos.AngleDeg) - Rotate(footingVec, sf.AngleDeg);
                footingCenter = new Point2d(columnCenter.X + alignGlobal.X, columnCenter.Y + alignGlobal.Y);
            }
            var rect = BuildRect(footingCenter, halfX, halfY, sf.AngleDeg);
            var factory = new GeometryFactory();
            var coords = new Coordinate[5];
            for (int i = 0; i < 4; i++) coords[i] = C1(rect[i].X, rect[i].Y);
            coords[4] = coords[0];
            return factory.CreatePolygon(factory.CreateLinearRing(coords));
        }

        private Geometry SlabFootprintPoly(SlabInfo slab)
        {
            var factory = new GeometryFactory();
            int a1 = slab.Axis1, a2 = slab.Axis2, a3 = slab.Axis3, a4 = slab.Axis4;
            if (a1 == 0 || a2 == 0 || a3 == 0 || a4 == 0) return null;
            if (!_axisService.TryIntersect(a1, a3, out Point2d p11) || !_axisService.TryIntersect(a1, a4, out Point2d p12) ||
                !_axisService.TryIntersect(a2, a3, out Point2d p21) || !_axisService.TryIntersect(a2, a4, out Point2d p22)) return null;
            var coords = new[] { C1(p11.X, p11.Y), C1(p12.X, p12.Y), C1(p22.X, p22.Y), C1(p21.X, p21.Y), C1(p11.X, p11.Y) };
            return factory.CreatePolygon(factory.CreateLinearRing(coords));
        }

        private List<SectionSlice> CollectAllSectionSlices(FloorInfo floor, Point2d pa, Point2d pb, Point2d alongOrigin, Vector2d dirUnit,
            bool isFoundationPlan, Dictionary<int, (double altKotCm, double yukseklikCm, double? k)> colExtra)
        {
            var list = new List<SectionSlice>();
            var factory = new GeometryFactory();
            Geometry cutLine;
            try
            {
                cutLine = factory.CreateLineString(new[]
                {
                    new Coordinate(pa.X, pa.Y),
                    new Coordinate(pb.X, pb.Y)
                });
            }
            catch { return list; }
            if (cutLine == null || cutLine.IsEmpty) return list;
            var dirN = dirUnit.GetNormal();
            double baseCm = _model.BuildingBaseKotu * 100.0;
            double floorLevelCm = (_model.BuildingBaseKotu + floor.ElevationM) * 100.0;
            int floorNo = floor.FloorNo;

            void addColPolys()
            {
                foreach (var col in _model.Columns)
                {
                    if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                    int sectionId = ResolveColumnSectionId(floorNo, col.ColumnNo);
                    int polygonSectionId = ResolvePolygonPositionSectionId(floorNo, col.ColumnNo);
                    if (col.ColumnType == 3 && (polygonSectionId <= 0 || !_model.PolygonColumnSectionByPositionSectionId.ContainsKey(polygonSectionId))) continue;
                    if (col.ColumnType != 3 && (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId))) continue;
                    var dim = _model.ColumnDimsBySectionId.TryGetValue(sectionId, out var d) ? d : (W: 40.0, H: 40.0);
                    double hw = dim.W / 2.0, hh = dim.H / 2.0;
                    var offsetLocal = col.ColumnType == 2 ? ComputeColumnOffsetCircle(col.OffsetXRaw, col.OffsetYRaw) : ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                    var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                    var center = new Point2d(axisNode.X + offsetGlobal.X, axisNode.Y + offsetGlobal.Y);
                    Geometry colPoly;
                    if (col.ColumnType == 2)
                    {
                        var raw = BuildCircleRing(center, Math.Max(hw, hh), col.AngleDeg, 32);
                        var coords = new Coordinate[raw.Length + 1];
                        for (int i = 0; i < raw.Length; i++) coords[i] = new Coordinate(raw[i].X, raw[i].Y);
                        coords[raw.Length] = coords[0];
                        colPoly = factory.CreatePolygon(factory.CreateLinearRing(coords));
                    }
                    else if (col.ColumnType == 3 && TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out var polyPts))
                    {
                        var coords = new Coordinate[polyPts.Length + 1];
                        for (int i = 0; i < polyPts.Length; i++) coords[i] = new Coordinate(polyPts[i].X, polyPts[i].Y);
                        coords[polyPts.Length] = coords[0];
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
                    if (!colExtra.TryGetValue(col.ColumnNo, out var ek)) continue;
                    double yuk = ek.yukseklikCm > 1.0 ? ek.yukseklikCm : SectionMinStoryHeightCm;
                    string kEt = FormatSectionKolonSliceLabel(col, floor, dim);
                    TryAddSliceCutLine(cutLine, colPoly, alongOrigin, dirN, ek.altKotCm, ek.altKotCm + yuk, LayerKolon, 50, list, kEt);
                }
            }

            if (isFoundationPlan)
            {
                int cfSliceIndex = 0;
                foreach (var cf in _model.ContinuousFoundations)
                {
                    var poly = ContinuousFootprintPoly(cf);
                    double z0 = baseCm + cf.BottomKotBinaGoreCm;
                    double z1 = z0 + cf.HeightCm;
                    int eni = (int)Math.Round(cf.WidthCm);
                    int yukCf = (int)Math.Round(cf.HeightCm);
                    string eniStr = eni.ToString(CultureInfo.InvariantCulture);
                    if (cf.AmpatmanWidthCm > 0 && Math.Abs(cf.AmpatmanWidthCm - cf.WidthCm) > 1e-6)
                        eniStr = eniStr + "-" + ((int)Math.Round(cf.AmpatmanWidthCm)).ToString(CultureInfo.InvariantCulture);
                    string cfEtik = string.Format(CultureInfo.InvariantCulture, "T-{0} ({1}/{2})", cfSliceIndex + 1, eniStr, yukCf);
                    TryAddSliceCutLine(cutLine, poly, alongOrigin, dirN, z0, z1, "TEMEL (BEYKENT)", SectionOrderContinuousFoundation, list, cfEtik);
                    cfSliceIndex++;
                    var hat = HatilStripOnContinuousPoly(cf);
                    if (hat != null && cf.HatilLabelHeightCm > 0)
                    {
                        double hz1 = z1 + Math.Max(15.0, cf.HatilLabelHeightCm * 0.35);
                        TryAddSliceCutLine(cutLine, hat, alongOrigin, dirN, z1, hz1, "TEMEL HATILI (BEYKENT)", 15, list);
                    }
                }
                foreach (var sfd in _model.SlabFoundations)
                {
                    var poly = SlabFoundationFootprintPoly(sfd);
                    double z1 = baseCm;
                    double z0 = z1 - Math.Max(sfd.ThicknessCm, 40.0);
                    TryAddSliceCutLine(cutLine, poly, alongOrigin, dirN, z0, z1, "TEMEL (BEYKENT)", 12, list);
                }
                foreach (var tb in _model.TieBeams)
                {
                    var poly = TieBeamFootprintPoly(tb);
                    double z0 = (_model.BuildingBaseKotu + tb.BottomKotM) * 100.0;
                    double z1 = z0 + Math.Max(tb.HeightCm, 25.0);
                    TryAddSliceCutLine(cutLine, poly, alongOrigin, dirN, z0, z1, "TEMEL HATILI (BEYKENT)", 14, list);
                }
                foreach (var sf in _model.SingleFootings)
                {
                    var poly = SingleFootingModelPoly(sf, floor);
                    double z0 = (_model.BuildingBaseKotu + sf.BottomLevelM) * 100.0;
                    double z1 = z0 + Math.Max(sf.HeightCm, 30.0);
                    TryAddSliceCutLine(cutLine, poly, alongOrigin, dirN, z0, z1, "TEMEL (BEYKENT)", 11, list);
                }
                addColPolys();
                foreach (var beam in MergeSameIdBeamsOnFloor(floorNo))
                {
                    if (beam.IsWallFlag != 1) continue;
                    var poly = BeamFootprintPoly(beam);
                    double zu = floorLevelCm + Math.Max(beam.Point1KotCm, beam.Point2KotCm);
                    double h = beam.HeightCm > 0 ? beam.HeightCm : 40.0;
                    TryAddSliceCutLine(cutLine, poly, alongOrigin, dirN, zu - h, zu, LayerPerde, 40, list, FormatSectionPerdeSliceLabel(beam, floor));
                }
            }
            else
            {
                foreach (var slab in _model.Slabs)
                {
                    if (GetSlabFloorNo(slab.SlabId) != floorNo) continue;
                    var poly = SlabFootprintPoly(slab);
                    double zt = floorLevelCm + slab.OffsetFromFloorCm;
                    double zb = zt - Math.Max(slab.ThicknessCm, 15.0);
                    TryAddSliceCutLine(cutLine, poly, alongOrigin, dirN, zb, zt, LayerDoseme, 5, list);
                }
                foreach (var beam in MergeSameIdBeamsOnFloor(floorNo))
                {
                    if (beam.IsWallFlag == 1) continue;
                    var poly = BeamFootprintPoly(beam);
                    double zu = floorLevelCm + Math.Max(beam.Point1KotCm, beam.Point2KotCm);
                    double h = beam.HeightCm > 0 ? beam.HeightCm : 30.0;
                    TryAddSliceCutLine(cutLine, poly, alongOrigin, dirN, zu - h, zu, LayerKiris, 30, list, FormatSectionBeamSliceLabel(beam, floor));
                }
                foreach (var beam in MergeSameIdBeamsOnFloor(floorNo))
                {
                    if (beam.IsWallFlag != 1) continue;
                    var poly = BeamFootprintPoly(beam);
                    double zu = floorLevelCm + Math.Max(beam.Point1KotCm, beam.Point2KotCm);
                    double h = beam.HeightCm > 0 ? beam.HeightCm : 40.0;
                    TryAddSliceCutLine(cutLine, poly, alongOrigin, dirN, zu - h, zu, LayerPerde, 40, list, FormatSectionPerdeSliceLabel(beam, floor));
                }
                addColPolys();
            }

            if (isFoundationPlan)
                ApplyKolonPerdeTemelHatiliFoundationPriority(list);
            else
                ApplyKolonPerdeKirisCutPriority(list);
            return list;
        }

        /// <summary>Temel kesiti: A ekseninde kolon &gt; perde &gt; temel hatılı (bağ kirişi + sürekli üst şerit).</summary>
        private static void ApplyKolonPerdeTemelHatiliFoundationPriority(List<SectionSlice> slices)
        {
            if (slices == null || slices.Count == 0) return;
            const double eps = 1e-6;
            const double minAlong = 8.0;
            var kolon = slices.Where(s => s.Layer == LayerKolon).ToList();
            var perde = slices.Where(s => s.Layer == LayerPerde).ToList();
            var hatili = slices.Where(s => s.Layer == LayerTemelHatiliKesit).ToList();
            var other = slices.Where(s => s.Layer != LayerKolon && s.Layer != LayerPerde && s.Layer != LayerTemelHatiliKesit).ToList();

            static List<(double lo, double hi)> MergeAlong(List<(double lo, double hi)> raw)
            {
                if (raw.Count == 0) return raw;
                var sorted = raw.OrderBy(x => x.lo).ToList();
                var merged = new List<(double lo, double hi)>();
                double cl = sorted[0].lo, ch = sorted[0].hi;
                for (int i = 1; i < sorted.Count; i++)
                {
                    var (l, h) = sorted[i];
                    if (l <= ch + eps) ch = Math.Max(ch, h);
                    else { merged.Add((cl, ch)); cl = l; ch = h; }
                }
                merged.Add((cl, ch));
                return merged;
            }

            static List<(double lo, double hi)> SubtractFrom(double a0, double a1, List<(double lo, double hi)> blocks)
            {
                var free = new List<(double lo, double hi)> { (a0, a1) };
                foreach (var (bl, bh) in blocks)
                {
                    var next = new List<(double lo, double hi)>();
                    foreach (var (fl, fh) in free)
                    {
                        if (bh <= fl + eps || bl >= fh - eps) { next.Add((fl, fh)); continue; }
                        if (bl > fl + eps) next.Add((fl, Math.Min(bl, fh)));
                        if (bh < fh - eps) next.Add((Math.Max(bh, fl), fh));
                    }
                    free = next;
                    if (free.Count == 0) break;
                }
                return free;
            }

            static SectionSlice CloneA(SectionSlice s, double a0, double a1) =>
                new SectionSlice { A0 = a0, A1 = a1, Z0 = s.Z0, Z1 = s.Z1, Layer = s.Layer, Order = s.Order, Etiket = s.Etiket };

            var kolonIv = MergeAlong(kolon.Select(s => { double u = Math.Min(s.A0, s.A1), v = Math.Max(s.A0, s.A1); return (u, v); }).ToList());
            var perdeIv = MergeAlong(perde.Select(s => { double u = Math.Min(s.A0, s.A1), v = Math.Max(s.A0, s.A1); return (u, v); }).ToList());
            var hatiliBlockIv = MergeAlong(kolonIv.Concat(perdeIv).ToList());

            var newPerde = new List<SectionSlice>();
            foreach (var s in perde)
            {
                double a0 = Math.Min(s.A0, s.A1), a1 = Math.Max(s.A0, s.A1);
                foreach (var (lo, hi) in SubtractFrom(a0, a1, kolonIv))
                    if (hi - lo >= minAlong - eps) newPerde.Add(CloneA(s, lo, hi));
            }
            var newHatili = new List<SectionSlice>();
            foreach (var s in hatili)
            {
                double a0 = Math.Min(s.A0, s.A1), a1 = Math.Max(s.A0, s.A1);
                foreach (var (lo, hi) in SubtractFrom(a0, a1, hatiliBlockIv))
                    if (hi - lo >= minAlong - eps) newHatili.Add(CloneA(s, lo, hi));
            }

            slices.Clear();
            slices.AddRange(other);
            slices.AddRange(kolon);
            slices.AddRange(newPerde);
            slices.AddRange(newHatili);
        }

        /// <summary>Kesit boyunca A ekseninde kolon–perde–kiriş üst üste biniyorsa yalnızca biri: 1 kolon 2 perde 3 kiriş.</summary>
        private static void ApplyKolonPerdeKirisCutPriority(List<SectionSlice> slices)
        {
            if (slices == null || slices.Count == 0) return;
            const double eps = 1e-6;
            const double minAlong = 8.0;
            var kolon = slices.Where(s => s.Layer == LayerKolon).ToList();
            var perde = slices.Where(s => s.Layer == LayerPerde).ToList();
            var kiris = slices.Where(s => s.Layer == LayerKiris).ToList();
            var other = slices.Where(s => s.Layer != LayerKolon && s.Layer != LayerPerde && s.Layer != LayerKiris).ToList();

            static List<(double lo, double hi)> MergeAlong(List<(double lo, double hi)> raw)
            {
                if (raw.Count == 0) return raw;
                var sorted = raw.OrderBy(x => x.lo).ToList();
                var merged = new List<(double lo, double hi)>();
                double cl = sorted[0].lo, ch = sorted[0].hi;
                for (int i = 1; i < sorted.Count; i++)
                {
                    var (l, h) = sorted[i];
                    if (l <= ch + eps) ch = Math.Max(ch, h);
                    else { merged.Add((cl, ch)); cl = l; ch = h; }
                }
                merged.Add((cl, ch));
                return merged;
            }

            static List<(double lo, double hi)> SubtractFrom(double a0, double a1, List<(double lo, double hi)> blocks)
            {
                var free = new List<(double lo, double hi)> { (a0, a1) };
                foreach (var (bl, bh) in blocks)
                {
                    var next = new List<(double lo, double hi)>();
                    foreach (var (fl, fh) in free)
                    {
                        if (bh <= fl + eps || bl >= fh - eps) { next.Add((fl, fh)); continue; }
                        if (bl > fl + eps) next.Add((fl, Math.Min(bl, fh)));
                        if (bh < fh - eps) next.Add((Math.Max(bh, fl), fh));
                    }
                    free = next;
                    if (free.Count == 0) break;
                }
                return free;
            }

            static SectionSlice CloneA(SectionSlice s, double a0, double a1) =>
                new SectionSlice { A0 = a0, A1 = a1, Z0 = s.Z0, Z1 = s.Z1, Layer = s.Layer, Order = s.Order, Etiket = s.Etiket };

            var kolonIv = MergeAlong(kolon.Select(s => { double u = Math.Min(s.A0, s.A1), v = Math.Max(s.A0, s.A1); return (u, v); }).ToList());
            var perdeIv = MergeAlong(perde.Select(s => { double u = Math.Min(s.A0, s.A1), v = Math.Max(s.A0, s.A1); return (u, v); }).ToList());
            var kirisBlockIv = MergeAlong(kolonIv.Concat(perdeIv).ToList());

            var newPerde = new List<SectionSlice>();
            foreach (var s in perde)
            {
                double a0 = Math.Min(s.A0, s.A1), a1 = Math.Max(s.A0, s.A1);
                foreach (var (lo, hi) in SubtractFrom(a0, a1, kolonIv))
                    if (hi - lo >= minAlong - eps) newPerde.Add(CloneA(s, lo, hi));
            }
            var newKiris = new List<SectionSlice>();
            foreach (var s in kiris)
            {
                double a0 = Math.Min(s.A0, s.A1), a1 = Math.Max(s.A0, s.A1);
                foreach (var (lo, hi) in SubtractFrom(a0, a1, kirisBlockIv))
                    if (hi - lo >= minAlong - eps) newKiris.Add(CloneA(s, lo, hi));
            }

            slices.Clear();
            slices.AddRange(other);
            slices.AddRange(kolon);
            slices.AddRange(newPerde);
            slices.AddRange(newKiris);
        }

        private bool TryGetKesitSiniriBounds(List<SectionSlice> slices, bool isFoundationPlan, out double aLo, out double aHi, out double zLo, out double zHi)
        {
            aLo = aHi = zLo = zHi = 0;
            if (slices == null || slices.Count == 0) return false;
            aLo = slices.Min(s => s.A0) - KesitSiniriBoyunaTasmasiCm;
            aHi = slices.Max(s => s.A1) + KesitSiniriBoyunaTasmasiCm;
            if (isFoundationPlan)
            {
                var temelOnly = slices.Where(s => s.Layer != LayerKolon && s.Layer != LayerPerde).ToList();
                if (temelOnly.Count == 0) return false;
                double g = KesitSiniriTemelKotTasmasiCm;
                zLo = temelOnly.Min(s => s.Z0) - g;
                zHi = temelOnly.Max(s => s.Z1) + g;
            }
            else
            {
                var beams = slices.Where(s => s.Layer == LayerKiris).ToList();
                if (beams.Count > 0)
                {
                    zLo = beams.Min(s => s.Z0) - KesitSiniriKotTasmasiCm;
                    zHi = beams.Max(s => s.Z1) + KesitSiniriKotTasmasiCm;
                }
                else
                {
                    var slabs = slices.Where(s => s.Layer == LayerDoseme).ToList();
                    if (slabs.Count == 0) return false;
                    zLo = slabs.Min(s => s.Z0) - KesitSiniriDosemeAltTasmasiCm;
                    zHi = slabs.Max(s => s.Z1) + KesitSiniriDosemeUstTasmasiCm;
                }
            }
            return true;
        }

        /// <summary>Temel: kolon/perde hariç kot ±50 cm, boyuna tüm eleman ±100. Normal kat: kiriş/döşeme kuralı + boy ±100.</summary>
        private void DrawKesitSiniriFromBeams(Transaction tr, BlockTableRecord btr, List<SectionSlice> slices,
            double originX, double originY, double amin, double minZ, double spanZ, bool horizontalAlongX, bool mirrorElevationX,
            bool isFoundationPlan)
        {
            if (!TryGetKesitSiniriBounds(slices, isFoundationPlan, out double aLo, out double aHi, out double zLo, out double zHi))
                return;
            var pl = new Polyline(4);
            if (horizontalAlongX)
            {
                double x0 = originX + (aLo - amin);
                double x1 = originX + (aHi - amin);
                double y0 = originY + (zLo - minZ);
                double y1 = originY + (zHi - minZ);
                pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
            }
            else
            {
                double y0 = originY + (aLo - amin);
                double y1 = originY + (aHi - amin);
                double x0, x1;
                if (mirrorElevationX)
                {
                    x0 = originX + spanZ - (zHi - minZ);
                    x1 = originX + spanZ - (zLo - minZ);
                }
                else
                {
                    x0 = originX + (zLo - minZ);
                    x1 = originX + (zHi - minZ);
                }
                pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
            }
            pl.Closed = true;
            pl.Layer = LayerKesitSiniri;
            pl.ConstantWidth = 0;
            AppendEntity(tr, btr, pl);
        }

        /// <summary>A-A: kiriş alt; kolon/perde temel üstü / kalıp altı. B-B: kolon-perde temelde sınır solu, kalıpta sınır sağı, kesit boyu ortalı; kalıp kiriş sağda ortalı.</summary>
        private void DrawKesitSchematicElementLabels(Transaction tr, BlockTableRecord btr, Database db, List<SectionSlice> slices,
            FloorInfo _floor, double originX, double originY, double amin, double minZ, double spanZ,
            bool horizontalAlongX, bool mirrorElevationX, bool isFoundationPlan)
        {
            if (slices == null || slices.Count == 0) return;
            const double beamUnderGap = 5.0;
            const double siniriGap = 12.0;
            const double labelH = 11.0;
            const double rotDikKesit = Math.PI / 2.0;
            const double kirisSagGap = 8.0;
            bool hasSiniri = TryGetKesitSiniriBounds(slices, isFoundationPlan, out _, out _, out double zLoS, out double zHiS);
            double zHiEff = hasSiniri ? zHiS : slices.Max(s => s.Z1);
            double zLoEff = hasSiniri ? zLoS : slices.Min(s => s.Z0);
            bool kolonPerdeUstunde = isFoundationPlan;
            double yKolonPerdeBaseAa = 0;
            if (horizontalAlongX && !kolonPerdeUstunde)
                yKolonPerdeBaseAa = originY + (zLoEff - minZ) - siniriGap;
            void kesitSiniriXSolSag(out double xSol, out double xSag)
            {
                if (mirrorElevationX)
                {
                    xSol = originX + spanZ - (zHiEff - minZ);
                    xSag = originX + spanZ - (zLoEff - minZ);
                }
                else
                {
                    xSol = originX + (zLoEff - minZ);
                    xSag = originX + (zHiEff - minZ);
                }
            }
            ObjectId styleId = GetOrCreateElemanEtiketTextStyle(tr, db);
            void putLabel(double x, double y, string txt, string layer, double rotationRad)
            {
                if (string.IsNullOrEmpty(txt)) return;
                bool horiz = Math.Abs(rotationRad) < 1e-6;
                AppendEntity(tr, btr, new DBText
                {
                    Layer = layer,
                    Height = labelH,
                    TextStyleId = styleId,
                    TextString = txt,
                    Rotation = rotationRad,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = horiz ? TextVerticalMode.TextTop : TextVerticalMode.TextVerticalMid,
                    Position = new Point3d(x, y, 0),
                    AlignmentPoint = new Point3d(x, y, 0)
                });
            }
            foreach (var s in slices.Where(x => x.Layer == LayerKiris && !string.IsNullOrEmpty(x.Etiket)))
            {
                if (horizontalAlongX)
                {
                    double cx = originX + ((s.A0 + s.A1) * 0.5 - amin);
                    double yUnder = originY + (s.Z0 - minZ) - beamUnderGap;
                    putLabel(cx, yUnder, s.Etiket, LayerKirisYazisi, 0);
                }
                else if (!isFoundationPlan)
                {
                    double cy = originY + ((s.A0 + s.A1) * 0.5 - amin);
                    double xKirisSag = mirrorElevationX
                        ? originX + spanZ - (s.Z0 - minZ) + kirisSagGap
                        : originX + (s.Z1 - minZ) + kirisSagGap;
                    putLabel(xKirisSag, cy, s.Etiket, LayerKirisYazisi, rotDikKesit);
                }
            }
            const double temelEtiketH = 12.0;
            foreach (var s in slices.Where(x => x.Layer == "TEMEL (BEYKENT)" && x.Order == SectionOrderContinuousFoundation && !string.IsNullOrEmpty(x.Etiket)))
            {
                if (horizontalAlongX)
                {
                    double cx = originX + ((s.A0 + s.A1) * 0.5 - amin);
                    double yAlt = originY + (s.Z1 - minZ) + KesitSurekliTemelEtiketGapCm;
                    AppendEntity(tr, btr, new DBText
                    {
                        Layer = LayerTemelIsmi,
                        Height = temelEtiketH,
                        TextStyleId = styleId,
                        TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(s.Etiket),
                        Rotation = 0,
                        HorizontalMode = TextHorizontalMode.TextCenter,
                        VerticalMode = TextVerticalMode.TextBottom,
                        Position = new Point3d(cx, yAlt, 0),
                        AlignmentPoint = new Point3d(cx, yAlt, 0),
                        LineWeight = LineWeight.LineWeight020
                    });
                }
                else if (isFoundationPlan)
                {
                    double cy = originY + ((s.A0 + s.A1) * 0.5 - amin);
                    double xSolKenar = mirrorElevationX
                        ? originX + spanZ - (s.Z1 - minZ)
                        : originX + (s.Z0 - minZ);
                    double xTxt = xSolKenar - KesitSurekliTemelEtiketSolBbCm;
                    AppendEntity(tr, btr, new DBText
                    {
                        Layer = LayerTemelIsmi,
                        Height = temelEtiketH,
                        TextStyleId = styleId,
                        TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(s.Etiket),
                        Rotation = rotDikKesit,
                        HorizontalMode = TextHorizontalMode.TextCenter,
                        VerticalMode = TextVerticalMode.TextVerticalMid,
                        Position = new Point3d(xTxt, cy, 0),
                        AlignmentPoint = new Point3d(xTxt, cy, 0),
                        LineWeight = LineWeight.LineWeight020
                    });
                }
            }
            kesitSiniriXSolSag(out double xSiniriSol, out double xSiniriSag);
            double kolPerXOffset = siniriGap + labelH * 0.55;
            int rowKp = 0;
            foreach (var g in slices.Where(x => (x.Layer == LayerKolon || x.Layer == LayerPerde) && !string.IsNullOrEmpty(x.Etiket)).GroupBy(x => x.Etiket))
            {
                string layer = g.First().Layer == LayerKolon ? LayerKolonIsmi : LayerPerdeYazisi;
                double aMid = g.Average(s => (s.A0 + s.A1) * 0.5);
                rowKp++;
                if (horizontalAlongX)
                {
                    double xKp = originX + (aMid - amin);
                    if (kolonPerdeUstunde)
                    {
                        double yAltKenar = originY + (zHiEff - minZ) + KesitTemelKolonPerdeUstBoslukCm + (rowKp - 1) * (labelH + 3);
                        AppendEntity(tr, btr, new DBText
                        {
                            Layer = layer,
                            Height = labelH,
                            TextStyleId = styleId,
                            TextString = g.Key,
                            Rotation = 0,
                            HorizontalMode = TextHorizontalMode.TextCenter,
                            VerticalMode = TextVerticalMode.TextBottom,
                            Position = new Point3d(xKp, yAltKenar, 0),
                            AlignmentPoint = new Point3d(xKp, yAltKenar, 0)
                        });
                    }
                    else
                    {
                        double yRow = yKolonPerdeBaseAa - (rowKp - 1) * (labelH + 3);
                        putLabel(xKp, yRow, g.Key, layer, 0);
                    }
                }
                else
                {
                    double cy = originY + (aMid - amin) + (rowKp - 1) * 3.0;
                    double xTxt = isFoundationPlan ? xSiniriSol - kolPerXOffset : xSiniriSag + kolPerXOffset;
                    putLabel(xTxt, cy, g.Key, layer, rotDikKesit);
                }
            }
        }

        private static List<double> KesitRadyeOlcuIstasyonlari(double a0, double a1, double adimCm)
        {
            if (a1 < a0) { double t = a0; a0 = a1; a1 = t; }
            var list = new List<double>();
            double L = a1 - a0;
            if (L <= adimCm + 1e-6)
                list.Add((a0 + a1) * 0.5);
            else
            {
                for (double u = a0 + adimCm * 0.5; u < a1 - 1e-6; u += adimCm)
                    list.Add(u);
            }
            return list;
        }

        private static bool KesitSliceAOverlap(SectionSlice u, SectionSlice v)
        {
            double u0 = Math.Min(u.A0, u.A1), u1 = Math.Max(u.A0, u.A1);
            double v0 = Math.Min(v.A0, v.A1), v1 = Math.Max(v.A0, v.A1);
            return Math.Max(u0, v0) < Math.Min(u1, v1) - 1e-6;
        }

        /// <summary>Hedef dilimin Z bandında, A örtüşen diğer dilimlerle en uzun kesişim aralığı.</summary>
        private static (bool ok, double z0, double z1) KesitEnBuyukZKesisim(SectionSlice hedef, IEnumerable<SectionSlice> digerleri)
        {
            double h0 = Math.Min(hedef.Z0, hedef.Z1), h1 = Math.Max(hedef.Z0, hedef.Z1);
            var list = new List<(double lo, double hi)>();
            foreach (var t in digerleri)
            {
                if (!KesitSliceAOverlap(hedef, t)) continue;
                double t0 = Math.Min(t.Z0, t.Z1), t1 = Math.Max(t.Z0, t.Z1);
                double lo = Math.Max(h0, t0), hi = Math.Min(h1, t1);
                if (hi - lo >= KesitOlcuKesisimMinCm - 1e-6) list.Add((lo, hi));
            }
            if (list.Count == 0) return (false, 0, 0);
            list.Sort((a, b) => a.lo.CompareTo(b.lo));
            double bestLo = 0, bestHi = 0, bestLen = 0;
            double cl = list[0].lo, ch = list[0].hi;
            for (int i = 1; i <= list.Count; i++)
            {
                if (i == list.Count || list[i].lo > ch + 1e-6)
                {
                    if (ch - cl > bestLen) { bestLen = ch - cl; bestLo = cl; bestHi = ch; }
                    if (i < list.Count) { cl = list[i].lo; ch = list[i].hi; }
                }
                else ch = Math.Max(ch, list[i].hi);
            }
            return (true, bestLo, bestHi);
        }

        /// <summary>Kesit şemasında kiriş/döşeme/temel kalınlık ölçüleri; OLCU (BEYKENT), PLAN_OLCU stili.</summary>
        private void DrawKesitSchematicDimensions(Transaction tr, BlockTableRecord btr, List<SectionSlice> slices,
            double originX, double originY, double amin, double minZ, double spanZ,
            bool horizontalAlongX, bool mirrorElevationX, bool isFoundationPlan, ObjectId dimStyleId)
        {
            if (slices == null || slices.Count == 0 || dimStyleId.IsNull) return;

            void AddAligned(Point3d p1, Point3d p2, Point3d dimLinePt)
            {
                AppendEntity(tr, btr, new AlignedDimension(p1, p2, dimLinePt, "", dimStyleId)
                {
                    Layer = LayerOlcu,
                    LineWeight = LineWeight.LineWeight020
                });
            }

            var staggerAaRight = new Dictionary<long, int>();
            int NextStaggerAa(double aKey)
            {
                long k = (long)Math.Round(aKey * 0.05);
                int n = staggerAaRight.TryGetValue(k, out int v) ? v + 1 : 0;
                staggerAaRight[k] = n;
                return n;
            }

            if (horizontalAlongX)
            {
                if (isFoundationPlan)
                {
                    foreach (var s in slices.Where(x =>
                                 x.Order == SectionOrderContinuousFoundation ||
                                 x.Order == SectionOrderSingleFooting))
                    {
                        double aMax = Math.Max(s.A0, s.A1);
                        double xR = originX + (aMax - amin);
                        double y0 = originY + (s.Z0 - minZ);
                        double y1 = originY + (s.Z1 - minZ);
                        if (y1 - y0 < 2.0) y1 = y0 + 2.0;
                        int st = NextStaggerAa(aMax);
                        double dimX = xR + KesitOlcuAaDimLineOffsetCm + st * KesitOlcuStaggerCm;
                        AddAligned(new Point3d(xR, y0, 0), new Point3d(xR, y1, 0), new Point3d(dimX, (y0 + y1) * 0.5, 0));
                    }
                    var temelGovdeKesisim = slices.Where(x => x.Layer == "TEMEL (BEYKENT)" &&
                        (x.Order == SectionOrderContinuousFoundation || x.Order == SectionOrderSingleFooting || x.Order == SectionOrderSlabFoundation)).ToList();
                    foreach (var s in slices.Where(x => x.Order == SectionOrderTieBeam || x.Order == SectionOrderHatilStrip))
                    {
                        double aMax = Math.Max(s.A0, s.A1);
                        double xR = originX + (aMax - amin);
                        double h0 = Math.Min(s.Z0, s.Z1), h1 = Math.Max(s.Z0, s.Z1);
                        double yBot = originY + (h0 - minZ), yTop = originY + (h1 - minZ);
                        if (Math.Abs(yTop - yBot) < 2.0) { double m = (yTop + yBot) * 0.5; yBot = m - 1.0; yTop = m + 1.0; }
                        int st = NextStaggerAa(aMax);
                        double dimInner = xR + KesitOlcuAaDimLineOffsetCm + st * KesitOlcuStaggerCm;
                        var (okZ, ol, oh) = KesitEnBuyukZKesisim(s, temelGovdeKesisim);
                        bool stacked = false;
                        if (okZ && oh - ol >= KesitOlcuKesisimMinCm - 1e-6)
                        {
                            if (ol <= h0 + KesitOlcuZFlushEpsCm && oh - h0 >= KesitOlcuKesisimMinCm - 1e-6 && h1 - oh >= KesitOlcuKesisimMinCm - 1e-6)
                            {
                                double ySp = originY + (oh - minZ);
                                AddAligned(new Point3d(xR, yBot, 0), new Point3d(xR, ySp, 0), new Point3d(dimInner, (yBot + ySp) * 0.5, 0));
                                AddAligned(new Point3d(xR, ySp, 0), new Point3d(xR, yTop, 0), new Point3d(dimInner, (ySp + yTop) * 0.5, 0));
                                AddAligned(new Point3d(xR, yBot, 0), new Point3d(xR, yTop, 0), new Point3d(dimInner + KesitOlcuCiftOlcuAraligiCm, (yBot + yTop) * 0.5, 0));
                                stacked = true;
                            }
                            else if (oh >= h1 - KesitOlcuZFlushEpsCm && ol - h0 >= KesitOlcuKesisimMinCm - 1e-6 && h1 - ol >= KesitOlcuKesisimMinCm - 1e-6)
                            {
                                double ySp = originY + (ol - minZ);
                                AddAligned(new Point3d(xR, yBot, 0), new Point3d(xR, ySp, 0), new Point3d(dimInner, (yBot + ySp) * 0.5, 0));
                                AddAligned(new Point3d(xR, ySp, 0), new Point3d(xR, yTop, 0), new Point3d(dimInner, (ySp + yTop) * 0.5, 0));
                                AddAligned(new Point3d(xR, yBot, 0), new Point3d(xR, yTop, 0), new Point3d(dimInner + KesitOlcuCiftOlcuAraligiCm, (yBot + yTop) * 0.5, 0));
                                stacked = true;
                            }
                        }
                        if (!stacked)
                            AddAligned(new Point3d(xR, yBot, 0), new Point3d(xR, yTop, 0), new Point3d(dimInner, (yBot + yTop) * 0.5, 0));
                    }
                    int radyeIx = 0;
                    foreach (var s in slices.Where(x => x.Order == SectionOrderSlabFoundation))
                    {
                        double y0 = originY + (s.Z0 - minZ);
                        double y1 = originY + (s.Z1 - minZ);
                        if (y1 - y0 < 2.0) y1 = y0 + 2.0;
                        foreach (double aSta in KesitRadyeOlcuIstasyonlari(s.A0, s.A1, KesitOlcuRadyeAralikCm))
                        {
                            double xm = originX + (aSta - amin);
                            double dimX = xm + KesitOlcuAaDimLineOffsetCm + (radyeIx++ % 5) * (KesitOlcuStaggerCm * 0.45);
                            AddAligned(new Point3d(xm, y0, 0), new Point3d(xm, y1, 0), new Point3d(dimX, (y0 + y1) * 0.5, 0));
                        }
                    }
                }
                else
                {
                    var dosemeKirisKesisim = slices.Where(x => x.Layer == LayerDoseme).ToList();
                    foreach (var s in slices.Where(x => x.Layer == LayerKiris))
                    {
                        double aMax = Math.Max(s.A0, s.A1);
                        double xR = originX + (aMax - amin);
                        double Zb = Math.Min(s.Z0, s.Z1), Zt = Math.Max(s.Z0, s.Z1);
                        double yBot = originY + (Zb - minZ), yTop = originY + (Zt - minZ);
                        if (Math.Abs(yTop - yBot) < 2.0) { double m = (yTop + yBot) * 0.5; yBot = m - 1.0; yTop = m + 1.0; }
                        int st = NextStaggerAa(aMax);
                        double dimInner = xR + KesitOlcuAaDimLineOffsetCm + st * KesitOlcuStaggerCm;
                        SectionSlice bestD = null;
                        double bestA = 0;
                        double s0 = Math.Min(s.A0, s.A1), s1 = Math.Max(s.A0, s.A1);
                        foreach (var d in dosemeKirisKesisim)
                        {
                            if (!KesitSliceAOverlap(s, d)) continue;
                            double d0 = Math.Min(d.A0, d.A1), d1a = Math.Max(d.A0, d.A1);
                            double aLen = Math.Min(s1, d1a) - Math.Max(s0, d0);
                            if (aLen > bestA) { bestA = aLen; bestD = d; }
                        }
                        bool stacked = false;
                        if (bestD != null)
                        {
                            double zb = Math.Min(bestD.Z0, bestD.Z1), ztop = Math.Max(bestD.Z0, bestD.Z1);
                            double slabT = ztop - zb;
                            double d1 = Math.Min(slabT, Zt - Math.Max(Zb, zb));
                            if (d1 >= KesitOlcuKesisimMinCm - 1e-6 && Zt - d1 - Zb >= KesitOlcuKesisimMinCm - 1e-6)
                            {
                                double zSp = Zt - d1;
                                double ySp = originY + (zSp - minZ);
                                AddAligned(new Point3d(xR, yTop, 0), new Point3d(xR, ySp, 0), new Point3d(dimInner, (yTop + ySp) * 0.5, 0));
                                AddAligned(new Point3d(xR, ySp, 0), new Point3d(xR, yBot, 0), new Point3d(dimInner, (ySp + yBot) * 0.5, 0));
                                AddAligned(new Point3d(xR, yTop, 0), new Point3d(xR, yBot, 0), new Point3d(dimInner + KesitOlcuCiftOlcuAraligiCm, (yTop + yBot) * 0.5, 0));
                                stacked = true;
                            }
                        }
                        if (!stacked)
                            AddAligned(new Point3d(xR, yBot, 0), new Point3d(xR, yTop, 0), new Point3d(dimInner, (yBot + yTop) * 0.5, 0));
                    }
                    foreach (var s in slices.Where(x => x.Layer == LayerDoseme))
                    {
                        double xm = originX + ((s.A0 + s.A1) * 0.5 - amin);
                        double y0 = originY + (s.Z0 - minZ);
                        double y1 = originY + (s.Z1 - minZ);
                        if (y1 - y0 < 2.0) y1 = y0 + 2.0;
                        int st = NextStaggerAa((s.A0 + s.A1) * 0.5);
                        double dimX = xm + KesitOlcuAaDimLineOffsetCm + st * KesitOlcuStaggerCm;
                        AddAligned(new Point3d(xm, y0, 0), new Point3d(xm, y1, 0), new Point3d(dimX, (y0 + y1) * 0.5, 0));
                    }
                }
            }
            else
            {
                var staggerBb = new Dictionary<long, int>();
                int NextStaggerBb(double key)
                {
                    long k = (long)Math.Round(key * 0.05);
                    int n = staggerBb.TryGetValue(k, out int v) ? v + 1 : 0;
                    staggerBb[k] = n;
                    return n;
                }
                void KotXR(SectionSlice s, out double xL, out double xR)
                {
                    double xa = mirrorElevationX ? originX + spanZ - (s.Z1 - minZ) : originX + (s.Z0 - minZ);
                    double xb = mirrorElevationX ? originX + spanZ - (s.Z0 - minZ) : originX + (s.Z1 - minZ);
                    xL = Math.Min(xa, xb);
                    xR = Math.Max(xa, xb);
                    if (xR - xL < 2.0) xR = xL + 2.0;
                }

                if (isFoundationPlan)
                {
                    foreach (var s in slices.Where(x =>
                                 x.Order == SectionOrderContinuousFoundation ||
                                 x.Order == SectionOrderSingleFooting))
                    {
                        KotXR(s, out double xL, out double xR);
                        double yRef = originY + Math.Min(s.A0, s.A1) - amin;
                        int st = NextStaggerBb(yRef);
                        double yDimLine = yRef - KesitOlcuBbDimLineBelowRefCm - st * KesitOlcuStaggerCm;
                        AddAligned(new Point3d(xL, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xL + xR) * 0.5, yDimLine, 0));
                    }
                    var temelGovdeBb = slices.Where(x => x.Layer == "TEMEL (BEYKENT)" &&
                        (x.Order == SectionOrderContinuousFoundation || x.Order == SectionOrderSingleFooting || x.Order == SectionOrderSlabFoundation)).ToList();
                    foreach (var s in slices.Where(x => x.Order == SectionOrderTieBeam || x.Order == SectionOrderHatilStrip))
                    {
                        KotXR(s, out double xL, out double xR);
                        double yRef = originY + Math.Min(s.A0, s.A1) - amin;
                        int st = NextStaggerBb(yRef);
                        double yDimInner = yRef - KesitOlcuBbDimLineBelowRefCm - st * KesitOlcuStaggerCm;
                        double bestLen = 0, bestIl = xL, bestIr = xR;
                        foreach (var t in temelGovdeBb)
                        {
                            if (!KesitSliceAOverlap(s, t)) continue;
                            KotXR(t, out double xtL, out double xtR);
                            double il = Math.Max(xL, xtL), ir = Math.Min(xR, xtR);
                            if (ir - il >= KesitOlcuKesisimMinCm && ir - il > bestLen) { bestLen = ir - il; bestIl = il; bestIr = ir; }
                        }
                        bool stacked = false;
                        if (bestLen >= KesitOlcuKesisimMinCm - 1e-6)
                        {
                            double eps = KesitOlcuZFlushEpsCm;
                            if (bestIl <= xL + eps && bestIr - xL >= KesitOlcuKesisimMinCm - 1e-6 && xR - bestIr >= KesitOlcuKesisimMinCm - 1e-6)
                            {
                                AddAligned(new Point3d(xL, yRef, 0), new Point3d(bestIr, yRef, 0), new Point3d((xL + bestIr) * 0.5, yDimInner, 0));
                                AddAligned(new Point3d(bestIr, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((bestIr + xR) * 0.5, yDimInner, 0));
                                AddAligned(new Point3d(xL, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xL + xR) * 0.5, yDimInner - KesitOlcuCiftOlcuAraligiCm, 0));
                                stacked = true;
                            }
                            else if (bestIr >= xR - eps && bestIl - xL >= KesitOlcuKesisimMinCm - 1e-6 && xR - bestIl >= KesitOlcuKesisimMinCm - 1e-6)
                            {
                                AddAligned(new Point3d(bestIl, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((bestIl + xR) * 0.5, yDimInner, 0));
                                AddAligned(new Point3d(xL, yRef, 0), new Point3d(bestIl, yRef, 0), new Point3d((xL + bestIl) * 0.5, yDimInner, 0));
                                AddAligned(new Point3d(xL, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xL + xR) * 0.5, yDimInner - KesitOlcuCiftOlcuAraligiCm, 0));
                                stacked = true;
                            }
                        }
                        if (!stacked)
                            AddAligned(new Point3d(xL, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xL + xR) * 0.5, yDimInner, 0));
                    }
                    foreach (var s in slices.Where(x => x.Order == SectionOrderSlabFoundation))
                    {
                        KotXR(s, out double xL, out double xR);
                        int ri = 0;
                        foreach (double aSta in KesitRadyeOlcuIstasyonlari(s.A0, s.A1, KesitOlcuRadyeAralikCm))
                        {
                            double yRef = originY + (aSta - amin);
                            double yDimLine = yRef - KesitOlcuBbDimLineBelowRefCm - (ri++ % 4) * 8.0;
                            AddAligned(new Point3d(xL, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xL + xR) * 0.5, yDimLine, 0));
                        }
                    }
                }
                else
                {
                    foreach (var s in slices.Where(x => x.Layer == LayerKiris))
                    {
                        KotXR(s, out double xL, out double xR);
                        double yRef = originY + Math.Min(s.A0, s.A1) - amin;
                        int st = NextStaggerBb(yRef);
                        double yDimInner = yRef - KesitOlcuBbDimLineBelowRefCm - st * KesitOlcuStaggerCm;
                        SectionSlice bestD = null;
                        double bestA = 0;
                        double s0 = Math.Min(s.A0, s.A1), s1 = Math.Max(s.A0, s.A1);
                        foreach (var d in slices.Where(x => x.Layer == LayerDoseme))
                        {
                            if (!KesitSliceAOverlap(s, d)) continue;
                            double d0 = Math.Min(d.A0, d.A1), d1a = Math.Max(d.A0, d.A1);
                            double aLen = Math.Min(s1, d1a) - Math.Max(s0, d0);
                            if (aLen > bestA) { bestA = aLen; bestD = d; }
                        }
                        bool stacked = false;
                        if (bestD != null)
                        {
                            KotXR(bestD, out double xdL, out double xdR);
                            double slabW = Math.Abs(xdR - xdL);
                            double d1r = Math.Min(slabW, xR - Math.Max(xL, xdL));
                            double d1l = Math.Min(slabW, Math.Min(xR, xdR) - xL);
                            if (d1r >= KesitOlcuKesisimMinCm - 1e-6 && xR - d1r - xL >= KesitOlcuKesisimMinCm - 1e-6)
                            {
                                double xSp = xR - d1r;
                                AddAligned(new Point3d(xSp, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xSp + xR) * 0.5, yDimInner, 0));
                                AddAligned(new Point3d(xL, yRef, 0), new Point3d(xSp, yRef, 0), new Point3d((xL + xSp) * 0.5, yDimInner, 0));
                                AddAligned(new Point3d(xL, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xL + xR) * 0.5, yDimInner - KesitOlcuCiftOlcuAraligiCm, 0));
                                stacked = true;
                            }
                            else if (!stacked && d1l >= KesitOlcuKesisimMinCm - 1e-6 && xL + d1l < xR - KesitOlcuKesisimMinCm + 1e-6)
                            {
                                double xSp = xL + d1l;
                                AddAligned(new Point3d(xL, yRef, 0), new Point3d(xSp, yRef, 0), new Point3d((xL + xSp) * 0.5, yDimInner, 0));
                                AddAligned(new Point3d(xSp, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xSp + xR) * 0.5, yDimInner, 0));
                                AddAligned(new Point3d(xL, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xL + xR) * 0.5, yDimInner - KesitOlcuCiftOlcuAraligiCm, 0));
                                stacked = true;
                            }
                        }
                        if (!stacked)
                            AddAligned(new Point3d(xL, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xL + xR) * 0.5, yDimInner, 0));
                    }
                    foreach (var s in slices.Where(x => x.Layer == LayerDoseme))
                    {
                        KotXR(s, out double xL, out double xR);
                        double yRef = originY + ((s.A0 + s.A1) * 0.5 - amin);
                        int st = NextStaggerBb(yRef);
                        double yDimLine = yRef - KesitOlcuBbDimLineBelowRefCm - st * KesitOlcuStaggerCm;
                        AddAligned(new Point3d(xL, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xL + xR) * 0.5, yDimLine, 0));
                    }
                }
            }
        }

        /// <summary>Model cm birebir: yatay eksen = kesit boyunca mesafe, dikey = kot (genel cm).</summary>
        /// <param name="mirrorElevationX">Sol kesit kutusunda kot ekseninde (X) simetri; zincir (Y) aynalanmaz.</param>
        private void DrawSchematicFromSlicesOneToOne(Transaction tr, BlockTableRecord btr, List<SectionSlice> slices,
            double originX, double originY, double amin, double minZ, double spanA, double spanZ, bool horizontalAlongX, bool mirrorElevationX, bool drawReferenceAxis = true)
        {
            if (slices == null || slices.Count == 0)
            {
                var note = new MText
                {
                    Layer = LayerKesit,
                    Contents = "Kesit şeridinde eleman yok",
                    Location = new Point3d(originX + spanA * 0.5, originY + spanZ * 0.5, 0),
                    TextHeight = 26,
                    Width = Math.Max(200.0, spanA * 0.7),
                    Attachment = AttachmentPoint.MiddleCenter,
                    Color = Color.FromColorIndex(ColorMethod.ByAci, 8)
                };
                AppendEntity(tr, btr, note);
                return;
            }

            if (horizontalAlongX)
            {
                if (drawReferenceAxis)
                {
                    var gr = new Polyline(2);
                    gr.AddVertexAt(0, new Point2d(originX, originY), 0, 0, 0);
                    gr.AddVertexAt(1, new Point2d(originX + spanA, originY), 0, 0, 0);
                    gr.Layer = LayerKesit;
                    gr.ConstantWidth = 0;
                    gr.Color = Color.FromColorIndex(ColorMethod.ByAci, 252);
                    AppendEntity(tr, btr, gr);
                }

                foreach (var s in slices.OrderBy(x => x.Order).ThenBy(x => x.Z0))
                {
                    double x0 = originX + (s.A0 - amin);
                    double x1 = originX + (s.A1 - amin);
                    if (x1 - x0 < 6.0) { double m = (x0 + x1) * 0.5; x0 = m - 4.0; x1 = m + 4.0; }
                    double y0 = originY + (s.Z0 - minZ);
                    double y1 = originY + (s.Z1 - minZ);
                    if (y1 - y0 < 3.0) y1 = y0 + 3.0;
                    var rect = new Polyline(4);
                    rect.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
                    rect.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
                    rect.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
                    rect.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
                    rect.Closed = true;
                    rect.Layer = string.IsNullOrEmpty(s.Layer) ? LayerKesit : s.Layer;
                    AppendEntity(tr, btr, rect);
                }
            }
            else
            {
                if (drawReferenceAxis)
                {
                    var gr = new Polyline(2);
                    gr.AddVertexAt(0, new Point2d(originX, originY), 0, 0, 0);
                    gr.AddVertexAt(1, new Point2d(originX, originY + spanA), 0, 0, 0);
                    gr.Layer = LayerKesit;
                    gr.ConstantWidth = 0;
                    gr.Color = Color.FromColorIndex(ColorMethod.ByAci, 252);
                    AppendEntity(tr, btr, gr);
                }

                foreach (var s in slices.OrderBy(x => x.Order).ThenBy(x => x.Z0))
                {
                    double y0 = originY + (s.A0 - amin);
                    double y1 = originY + (s.A1 - amin);
                    if (y1 - y0 < 6.0) { double m = (y0 + y1) * 0.5; y0 = m - 4.0; y1 = m + 4.0; }
                    double x0, x1;
                    if (mirrorElevationX)
                    {
                        x0 = originX + spanZ - (s.Z1 - minZ);
                        x1 = originX + spanZ - (s.Z0 - minZ);
                    }
                    else
                    {
                        x0 = originX + (s.Z0 - minZ);
                        x1 = originX + (s.Z1 - minZ);
                    }
                    if (x1 - x0 < 3.0) x1 = x0 + 3.0;
                    var rect = new Polyline(4);
                    rect.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
                    rect.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
                    rect.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
                    rect.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
                    rect.Closed = true;
                    rect.Layer = string.IsNullOrEmpty(s.Layer) ? LayerKesit : s.Layer;
                    AppendEntity(tr, btr, rect);
                }
            }
        }
    }
}
