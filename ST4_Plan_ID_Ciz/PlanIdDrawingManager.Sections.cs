using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        /// <summary>Kesit hattı, kesite <b>paralel</b> düz kolon akslarından (X aksı ↔ dikey kesit, Y aksı ↔ yatay kesit) en az bu kadar uzak olmalı (cm).</summary>
        private const double SectionCutParallelAxisMinClearanceCm = 51.0;
        /// <summary>Aks sınırı kutusundan kesit arama iç boşluğu (cm).</summary>
        private const double SectionCutSearchInsetFromAxisBoxCm = 80.0;
        /// <summary>Kolon/kiriş/döşeme birleşim zarfından ek iç boşluk (cm); kesit hattı yapı dışına çıkmasın.</summary>
        private const double SectionCutSearchInsetFromStructureCm = 40.0;
        /// <summary>Paralel aks altında her cm için ek ceza (kesit konumunu itmek için).</summary>
        private const double SectionCutParallelAxisPenaltyPerCm = 850.0;
        /// <summary>Temel planında kesit hattı poligon kolon alanından geçmesin diye uygulanan ceza (kat planlarında 0).</summary>
        private const double SectionCutFoundationPolygonColumnPenalty = 48000.0;
        /// <summary>Kesit yerleşiminde merdiven/paralel ceza şeridi (cm). Kesit profili çizimi şeritsiz düz hat.</summary>
        private const double SectionStripHalfWidthCm = 50.0;
        private const double SectionLineExtendCm = 120.0;
        /// <summary>Plan üst kenarı ile üst kesit kutusu arası + ek 100 cm.</summary>
        private const double SectionAbovePlanGapCm = 340.0;
        private const double SectionLeftGapFromPlanCm = 320.0;
        /// <summary>Üst X aks balonunun kesite bakan dış yüzeyi ile KESİT SINIRI alt çizgisi arası (cm). Balon: çerçeve köşesi + 2R (R=KesitEtiketRadiusCm).</summary>
        private const double SectionMinAboveAxisTopLabelsCm = 100.0;
        /// <summary>Çap 40 cm aks balonu ile aynı; ok √2·R oranında.</summary>
        private const double KesitEtiketRadiusCm = 20.0;
        /// <summary>A-A / B-B kesit başlığı metin yüksekliği (cm).</summary>
        private const double KesitBaslikMetinYukseklikCm = 20.0;
        private const double KesitBaslikMetinYukseklikTemel50Cm = 30.0;
        /// <summary>Üst X aks balonunun üst dış yüzeyi ile &quot;A-A KESİTİ&quot; metninin <b>alt</b> kenarı arası (cm).</summary>
        private const double KesitIsmiUstAksBalonUstuBoslukCm = 60.0;
        /// <summary>Sol Y aks balonunun kesite bakan sol dış yüzeyi ile &quot;B-B KESİTİ&quot; metninin <b>sağ</b> kenarı arası (cm).</summary>
        private const double KesitIsmiSolAksBalonSolBoslukCm = 60.0;
        /// <summary>Kesit çizgisi ucu ile kat sınırı dikdörtgeni arasındaki boşluk (cm).</summary>
        private const double SectionCutGapFromFloorBoundaryCm = 30.0;
        private const double KesitEtiketTextHeightCm = 20.0;
        /// <summary>Sol Y aks balonunun kesite bakan dış yüzeyi ile KESİT SINIRI sağ kenarı arası (cm). Balon: çerçeve köşesi − 2R.</summary>
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
        /// <summary>Temel kesiti altında grobeton bandı yüksekliği (cm).</summary>
        private const double GrobetonUnderFoundationKesitHeightCm = 10.0;
        /// <summary>Temel parçaları arası (kesitte) boşlukta üst kottan aşağı grobeton şeridi kalınlığı (cm).</summary>
        private const double GrobetonBetweenFoundationsGapStripHeightCm = 15.0;
        /// <summary>Ara boşluğa grobeton çizmek için minimum boşluk genişliği (cm).</summary>
        private const double GrobetonBetweenFoundationsMinGapWidthCm = 3.0;
        /// <summary>Temel A dilimleri birleştirmede bitişik kabul eşiği (cm).</summary>
        private const double GrobetonFoundationAIntervalJoinEpsCm = 2.0;
        /// <summary>Zemin çizgisi, temel/grobeton hattı boyunca her uçtan uzatma (cm).</summary>
        private const double KesitZeminCizgiTemelHatUzantisiCm = 50.0;
        /// <summary>Kesit kotları: yakın kotları tek satırda birleştir (cm).</summary>
        private const double KesitKotMergeTolCm = 1.5;
        /// <summary>A-A kesitte kot üçgeni tepe noktası — yapı sağ kenarından (cm).</summary>
        private const double KesitKotDatumGapFromSectionCm = 22.0;
        /// <summary>B-B kesitte kot üçgeni tepe noktası — kesit üst (boyuna) kenarından (cm).</summary>
        private const double KesitKotBbDatumGapFromSectionCm = 22.0;
        /// <summary>Klasik kot üçgeni: yükseklik (cm) — referans çizim.</summary>
        private const double KesitKotTriHeightCm = 12.0;
        /// <summary>Klasik kot üçgeni: üst taban yarım genişlik (cm); tam taban 24.</summary>
        private const double KesitKotTriHalfWidthCm = 12.0;
        /// <summary>Klasik kot: tepe noktasından kesite doğru uzantı (cm).</summary>
        private const double KesitKotExtTowardSectionCm = 20.0;
        /// <summary>Klasik kot: sağ üst köşeden yatay uzantı (cm).</summary>
        private const double KesitKotExtTopRightCm = 26.0;
        /// <summary>26 cm kot uzantı çizgisi ile DBText tabanı arası (cm); 0 = tam çizgi hizası.</summary>
        private const double KesitKotTextAboveExtensionCm = 0.0;
        /// <summary>İki kot kot farkı bundan küçükse alttaki (düşük Z) işaret + yazı kayar.</summary>
        private const double KesitKotCrowdedSeparationMinCm = 13.0;
        /// <summary>Kotlar çok yakınsa alttaki: A-A’da +X (sağa), sol kesitte +Y (üste) (cm).</summary>
        private const double KesitKotCrowdedShiftLowerCm = 30.0;
        /// <summary>Kesit kot metin yüksekliği (cm).</summary>
        private const double KesitKotTextHeightCm = 10.0;
        /// <summary>Yalnızca kalıp planı kesiti (temel planı değil): A-A kot işareti + yazı −X (cm).</summary>
        private const double KesitKotKalipPlanAaShiftLeftCm = 82.0;
        /// <summary>Yalnızca kalıp planı sol kesiti: B-B kot işareti + yazı −Y (cm) — aşağı (sola kaydırma yok).</summary>
        private const double KesitKotKalipPlanBbShiftDownCm = 82.0;
        /// <summary>Kesit kot işareti SOLID tarama rengi (ACI); çizgiler <see cref="LayerKotCizgisi"/> katman rengini kullanır.</summary>
        private const short KesitKotWedgeSolidHatchColorIndex = 250;
        /// <summary>A-A: ölçü çizgisi eleman sağ kenarından bu kadar sağda (cm).</summary>
        private const double KesitOlcuAaDimLineOffsetCm = 20.0;
        /// <summary>B-B: ölçü çizgisi referans kenarının bu kadar altında (cm).</summary>
        private const double KesitOlcuBbDimLineBelowRefCm = 20.0;
        /// <summary>B-B: en alttaki kiriş/hatıl ölçüsü kesit dışına taşmasın diye referansın üstünde (cm).</summary>
        private const double KesitOlcuBbDimLineAboveRefCm = 20.0;
        /// <summary>Toplam + kesişim çift ölçü çizgileri arası (cm).</summary>
        private const double KesitOlcuCiftOlcuAraligiCm = 20.0;
        private const double KesitOlcuKesisimMinCm = 5.0;
        /// <summary>Hatıl–temel Z birleşiminde alt/üst hizaya cm tolerans.</summary>
        private const double KesitOlcuZFlushEpsCm = 4.0;
        private const double KesitOlcuRadyeAralikCm = 2000.0;
        /// <summary>Radye ölçü istasyonu, kesitte dar kalan temel hatılı A bandının dışında kalmasın diye ± genişletme (cm).</summary>
        private const double KesitRadyeOlcuHatilSkipMarginCm = 650.0;
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
            var factory = _ntsDrawFactory;
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
            var factory = _ntsDrawFactory;
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
            var factory = _ntsDrawFactory;
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
            var factory = StaticGeomFactory;
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
            var factory = StaticGeomFactory;
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
            var factory = StaticGeomFactory;
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
            var factory = StaticGeomFactory;
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
            var gf = _ntsDrawFactory;
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

        /// <summary>Dikey kesit (x sabit): düz (eğimsiz) X akslarına paralel — mesafe |x − aksX|.</summary>
        private double ParallelColumnAxisClearancePenaltyVertical(double x)
        {
            double pen = 0;
            foreach (var ax in _model.AxisX)
            {
                if (Math.Abs(ax.Slope) > 1e-6) continue;
                double d = Math.Abs(x - ax.ValueCm);
                if (d < SectionCutParallelAxisMinClearanceCm)
                    pen += (SectionCutParallelAxisMinClearanceCm - d) * SectionCutParallelAxisPenaltyPerCm;
            }
            return pen;
        }

        /// <summary>Yatay kesit (y sabit): düz (eğimsiz) Y akslarına paralel — mesafe |y − (−Value)|.</summary>
        private double ParallelColumnAxisClearancePenaltyHorizontal(double y)
        {
            double pen = 0;
            foreach (var ay in _model.AxisY)
            {
                if (Math.Abs(ay.Slope) > 1e-6) continue;
                double yLine = -ay.ValueCm;
                double d = Math.Abs(y - yLine);
                if (d < SectionCutParallelAxisMinClearanceCm)
                    pen += (SectionCutParallelAxisMinClearanceCm - d) * SectionCutParallelAxisPenaltyPerCm;
            }
            return pen;
        }

        /// <summary>Sadece temel planı: kesit çizgisi poligon kolon poligonunun içinden geçmesin (dikey kesit).</summary>
        private double FoundationPolygonColumnCutPenaltyVertical(double x, double ymin, double ymax, double e, FloorInfo floor)
        {
            var colExtra = GetColumnTableExtraData(floor);
            if (colExtra == null || colExtra.Count == 0) return 0;
            int floorNo = floor.FloorNo;
            var gf = _ntsDrawFactory;
            double yLo = ymin - e - 5e4, yHi = ymax + e + 5e4;
            var vLine = gf.CreateLineString(new[] { new Coordinate(x, yLo), new Coordinate(x, yHi) });
            double s = 0;
            foreach (var col in _model.Columns)
            {
                if (col.ColumnType != 3) continue;
                int polygonSectionId = ResolvePolygonPositionSectionId(floorNo, col.ColumnNo);
                if (polygonSectionId <= 0 || !_model.PolygonColumnSectionByPositionSectionId.ContainsKey(polygonSectionId)) continue;
                if (!colExtra.ContainsKey(col.ColumnNo)) continue;
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                var dim = _model.ColumnDimsBySectionId.TryGetValue(ResolveColumnSectionId(floorNo, col.ColumnNo), out var d) ? d : (W: 40.0, H: 40.0);
                double hw = dim.W / 2.0, hh = dim.H / 2.0;
                var offsetLocal = ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                var center = new Point2d(axisNode.X + offsetGlobal.X, axisNode.Y + offsetGlobal.Y);
                if (!TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out var polyPts)) continue;
                var coords = new Coordinate[polyPts.Length + 1];
                for (int i = 0; i < polyPts.Length; i++) coords[i] = new Coordinate(polyPts[i].X, polyPts[i].Y);
                coords[polyPts.Length] = coords[0];
                var colPoly = gf.CreatePolygon(gf.CreateLinearRing(coords));
                try
                {
                    if (!vLine.Intersects(colPoly)) continue;
                    var inter = vLine.Intersection(colPoly);
                    if (inter == null || inter.IsEmpty) continue;
                    var env = inter.EnvelopeInternal;
                    if (env.MaxY - env.MinY > 2.0)
                        s += SectionCutFoundationPolygonColumnPenalty;
                }
                catch { }
            }
            return s;
        }

        /// <summary>Sadece temel planı: yatay kesit — poligon kolon.</summary>
        private double FoundationPolygonColumnCutPenaltyHorizontal(double y, double xmin, double xmax, double e, FloorInfo floor)
        {
            var colExtra = GetColumnTableExtraData(floor);
            if (colExtra == null || colExtra.Count == 0) return 0;
            int floorNo = floor.FloorNo;
            var gf = _ntsDrawFactory;
            double xLo = xmin - e - 5e4, xHi = xmax + e + 5e4;
            var hLine = gf.CreateLineString(new[] { new Coordinate(xLo, y), new Coordinate(xHi, y) });
            double s = 0;
            foreach (var col in _model.Columns)
            {
                if (col.ColumnType != 3) continue;
                int polygonSectionId = ResolvePolygonPositionSectionId(floorNo, col.ColumnNo);
                if (polygonSectionId <= 0 || !_model.PolygonColumnSectionByPositionSectionId.ContainsKey(polygonSectionId)) continue;
                if (!colExtra.ContainsKey(col.ColumnNo)) continue;
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                var dim = _model.ColumnDimsBySectionId.TryGetValue(ResolveColumnSectionId(floorNo, col.ColumnNo), out var d) ? d : (W: 40.0, H: 40.0);
                double hw = dim.W / 2.0, hh = dim.H / 2.0;
                var offsetLocal = ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                var center = new Point2d(axisNode.X + offsetGlobal.X, axisNode.Y + offsetGlobal.Y);
                if (!TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out var polyPts)) continue;
                var coords = new Coordinate[polyPts.Length + 1];
                for (int i = 0; i < polyPts.Length; i++) coords[i] = new Coordinate(polyPts[i].X, polyPts[i].Y);
                coords[polyPts.Length] = coords[0];
                var colPoly = gf.CreatePolygon(gf.CreateLinearRing(coords));
                try
                {
                    if (!hLine.Intersects(colPoly)) continue;
                    var inter = hLine.Intersection(colPoly);
                    if (inter == null || inter.IsEmpty) continue;
                    var env = inter.EnvelopeInternal;
                    if (env.MaxX - env.MinX > 2.0)
                        s += SectionCutFoundationPolygonColumnPenalty;
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
            var gf = _ntsDrawFactory;
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

        /// <summary>
        /// Kesit X/Y aramasını aks kutusu ile <see cref="BuildFloorElementUnion"/> zarfının kesişimine indirger.
        /// Aks kutusu çatı/son katta yapıdan büyük olduğunda merkez boşta kalıp kesit çizgisi kat sınırını kesmezdi; öncelik yapı içinde kalmak.
        /// </summary>
        private static void GetCutLineSearchBoundsFromStructure(
            double xmin, double xmax, double ymin, double ymax,
            Geometry structuralUnion,
            out double xLo, out double xHi, out double yLo, out double yHi,
            out double cxPrefer, out double cyPrefer)
        {
            double axInset = SectionCutSearchInsetFromAxisBoxCm;
            double sInset = SectionCutSearchInsetFromStructureCm;
            cxPrefer = (xmin + xmax) * 0.5;
            cyPrefer = (ymin + ymax) * 0.5;
            if (structuralUnion != null && !structuralUnion.IsEmpty)
            {
                var se = structuralUnion.EnvelopeInternal;
                cxPrefer = (se.MinX + se.MaxX) * 0.5;
                cyPrefer = (se.MinY + se.MaxY) * 0.5;
                xLo = Math.Max(xmin + axInset, se.MinX + sInset);
                xHi = Math.Min(xmax - axInset, se.MaxX - sInset);
                yLo = Math.Max(ymin + axInset, se.MinY + sInset);
                yHi = Math.Min(ymax - axInset, se.MaxY - sInset);
            }
            else
            {
                xLo = xmin + 100;
                xHi = xmax - 100;
                yLo = ymin + 100;
                yHi = ymax - 100;
            }

            void WidenIfEmpty(ref double lo, ref double hi, double c, double spanMin, double boxMin, double boxMax)
            {
                if (hi > lo) return;
                double half = Math.Max(spanMin * 0.5, (boxMax - boxMin) * 0.2);
                lo = Math.Max(boxMin + 15, c - half);
                hi = Math.Min(boxMax - 15, c + half);
                if (hi <= lo)
                {
                    double m = Math.Max(20, (boxMax - boxMin) * 0.05);
                    lo = boxMin + m;
                    hi = boxMax - m;
                }
            }

            double sx = structuralUnion != null && !structuralUnion.IsEmpty ? structuralUnion.EnvelopeInternal.MaxX - structuralUnion.EnvelopeInternal.MinX : xmax - xmin;
            double sy = structuralUnion != null && !structuralUnion.IsEmpty ? structuralUnion.EnvelopeInternal.MaxY - structuralUnion.EnvelopeInternal.MinY : ymax - ymin;
            WidenIfEmpty(ref xLo, ref xHi, cxPrefer, Math.Min(100, sx), xmin, xmax);
            WidenIfEmpty(ref yLo, ref yHi, cyPrefer, Math.Min(100, sy), ymin, ymax);
        }

        private double FindBestVerticalCutX(FloorInfo floor, double xmin, double xmax, double ymin, double ymax, double e, Geometry occ, bool isFoundationPlan, Geometry structuralUnion)
        {
            var stairs = BuildStairAvoidZonesForFloor(floor);
            var gf = _ntsDrawFactory;
            double halfW = Math.Max(60.0, SectionStripHalfWidthCm * 1.2);
            GetCutLineSearchBoundsFromStructure(xmin, xmax, ymin, ymax, structuralUnion, out double xLo, out double xHi, out _, out _, out double cxPrefer, out _);
            if (xHi <= xLo) return (xmin + xmax) * 0.5;
            double step = Math.Max(40.0, (xHi - xLo) / 48.0);
            double bestX = cxPrefer;
            double bestKey = double.NegativeInfinity;
            var tanV = new Vector2d(0, 1);

            void consider(double x)
            {
                var (frac, lng) = SampleVerticalOnOccupancy(occ, x, ymin, ymax);
                Geometry strip = VerticalCutStripPoly(gf, x, ymin, ymax, e, halfW);
                double par = ParallelSpanAxisPenalty(strip, tanV, floor, isFoundationPlan);
                double stair = StairPenaltyVertical(x, ymin, ymax, e, stairs);
                double colLong = SlenderColumnLongAxisCutPenaltyVertical(x, ymin, ymax, e, floor, isFoundationPlan);
                double axisClr = ParallelColumnAxisClearancePenaltyVertical(x);
                double polyCol = isFoundationPlan ? FoundationPolygonColumnCutPenaltyVertical(x, ymin, ymax, e, floor) : 0;
                double key = frac * 900.0 + lng * 320.0 - stair - par - colLong - axisClr - polyCol - Math.Abs(x - cxPrefer) * 0.02;
                if (key > bestKey + 1e-6 || (Math.Abs(key - bestKey) < 1e-6 && Math.Abs(x - cxPrefer) < Math.Abs(bestX - cxPrefer)))
                {
                    bestKey = key;
                    bestX = x;
                }
            }

            for (double x = xLo; x <= xHi + 1e-6; x += step) consider(x);
            return bestX;
        }

        private double FindBestHorizontalCutY(FloorInfo floor, double xmin, double xmax, double ymin, double ymax, double e, Geometry occ, bool isFoundationPlan, Geometry structuralUnion)
        {
            var stairs = BuildStairAvoidZonesForFloor(floor);
            var gf = _ntsDrawFactory;
            double halfW = Math.Max(60.0, SectionStripHalfWidthCm * 1.2);
            GetCutLineSearchBoundsFromStructure(xmin, xmax, ymin, ymax, structuralUnion, out _, out _, out double yLo, out double yHi, out _, out double cyPrefer);
            if (yHi <= yLo) return (ymin + ymax) * 0.5;
            double step = Math.Max(40.0, (yHi - yLo) / 48.0);
            double bestY = cyPrefer;
            double bestKey = double.NegativeInfinity;
            var tanH = new Vector2d(1, 0);

            void consider(double y)
            {
                var (frac, lng) = SampleHorizontalOnOccupancy(occ, y, xmin, xmax);
                Geometry strip = HorizontalCutStripPoly(gf, y, xmin, xmax, e, halfW);
                double par = ParallelSpanAxisPenalty(strip, tanH, floor, isFoundationPlan);
                double stair = StairPenaltyHorizontal(y, xmin, xmax, e, stairs);
                double colLong = SlenderColumnLongAxisCutPenaltyHorizontal(y, xmin, xmax, e, floor, isFoundationPlan);
                double axisClr = ParallelColumnAxisClearancePenaltyHorizontal(y);
                double polyCol = isFoundationPlan ? FoundationPolygonColumnCutPenaltyHorizontal(y, xmin, xmax, e, floor) : 0;
                double key = frac * 900.0 + lng * 320.0 - stair - par - colLong - axisClr - polyCol - Math.Abs(y - cyPrefer) * 0.02;
                if (key > bestKey + 1e-6 || (Math.Abs(key - bestKey) < 1e-6 && Math.Abs(y - cyPrefer) < Math.Abs(bestY - cyPrefer)))
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
            bool isFoundationPlan, Geometry floorStructuralUnion,
            out double layoutMinX, out double layoutMaxX, out double layoutMinY, out double layoutMaxY,
            out double leftSectionMinX,
            List<FloorInfo> similarFloorsForKot = null)
        {
            double xmin = ext.Xmin, xmax = ext.Xmax, ymin = ext.Ymin, ymax = ext.Ymax;
            double e = SectionLineExtendCm;
            Geometry occ = BuildCutOccupancyUnion(floor, isFoundationPlan);

            // Her zaman: yatay kesit çizgisi → KESİT 1-1 üstte; dikey kesit → KESİT 2-2 solda
            double xvCut = FindBestVerticalCutX(floor, xmin, xmax, ymin, ymax, e, occ, isFoundationPlan, floorStructuralUnion);
            double yhCut = FindBestHorizontalCutY(floor, xmin, xmax, ymin, ymax, e, occ, isFoundationPlan, floorStructuralUnion);

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
            double extC = AxisExtensionBeyondBoundaryCm;
            double Rbal = KesitEtiketRadiusCm;
            const double sectionLayoutPadCm = 280.0;
            layoutMinX = offsetX + xmin - extC - 2.0 * Rbal - sectionLayoutPadCm;
            layoutMaxX = offsetX + xmax + extC + 2.0 * Rbal + sectionLayoutPadCm;
            layoutMinY = offsetY + ymin - extC - 2.0 * Rbal - sectionLayoutPadCm;
            layoutMaxY = offsetY + ymax + extC + 2.0 * Rbal + sectionLayoutPadCm;
            // Aks balonu AxisBalonCenterAtEnd ile çerçeve köşesinden R dışarıda; kesite bakan dış yüzey çerçeveden 2R
            double yAksBalonUstDisYuzey = offsetY + ymax + extC + 2.0 * Rbal;
            double xAksBalonSolDisYuzey = offsetX + xmin - extC - 2.0 * Rbal;
            leftSectionMinX = offsetX + xmin;

            var slicesTop = CollectAllSectionSlices(floor, topA, topB, alongOrigTop, dirTop, isFoundationPlan, colExtra);
            var slicesLeft = CollectAllSectionSlices(floor, leftA, leftB, alongOrigLeft, dirLeft, isFoundationPlan, colExtra);

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
            double contentTopY;
            // Üst kesit: aks balonunun kesite bakan üst dış yüzeyinden tam 100 cm yukarı (plan/340 cm zorlaması yok)
            if (TryGetKesitSiniriPlacementZBounds(slicesTop, isFoundationPlan, out double zLoKesitT))
            {
                double yKesitSiniriAltOfset = zLoKesitT - minZT;
                double yKesitSiniriAltHedef = yAksBalonUstDisYuzey + SectionMinAboveAxisTopLabelsCm;
                contentTopY = yKesitSiniriAltHedef - yKesitSiniriAltOfset;
            }
            else
            {
                contentTopY = yAksBalonUstDisYuzey + SectionMinAboveAxisTopLabelsCm + 12.0;
            }
            ObjectId planOlcuDimId = GetOrCreatePlanOlcuDimStyle(tr, db, 12.0);
            DrawSchematicFromSlicesOneToOne(tr, btr, slicesTop, contentTopX, contentTopY, aminT, minZT, spanAT, spanZT, horizontalAlongX: true, mirrorElevationX: false, isFoundationPlan, drawReferenceAxis: false);
            DrawKesitSiniriFromBeams(tr, btr, slicesTop, contentTopX, contentTopY, aminT, minZT, spanZT, horizontalAlongX: true, mirrorElevationX: false, isFoundationPlan);
            DrawKesitSchematicDimensions(tr, btr, slicesTop, contentTopX, contentTopY, aminT, minZT, spanZT, horizontalAlongX: true, mirrorElevationX: false, isFoundationPlan, planOlcuDimId);
            DrawKesitSchematicElementLabels(tr, btr, db, slicesTop, floor, contentTopX, contentTopY, aminT, minZT, spanZT, true, false, isFoundationPlan);
            if (isFoundationPlan)
                DrawGrobetonUnderFoundationKesit(tr, btr, slicesTop, contentTopX, contentTopY, aminT, minZT, spanZT, horizontalAlongX: true, mirrorElevationX: false);
            string similarKotSuffix = BuildSimilarFloorKotSuffix(similarFloorsForKot);
            try { DrawKesitSchematicElevationKots(tr, btr, db, slicesTop, contentTopX, contentTopY, aminT, minZT, spanZT, horizontalAlongX: true, mirrorElevationX: false, isFoundationPlan, similarKotSuffix); }
            catch { /* kesit kotları — planın geri kalanı çizilsin */ }
            // A-A başlık: üst aks balon üst yüzeyinden 60 cm yukarıda (metin alt kenarı); TextTop için üst hizası = alt + yükseklik
            double yAaBaslikUstHizasi = yAksBalonUstDisYuzey + KesitIsmiUstAksBalonUstuBoslukCm + KesitBaslikMetinYukseklikCm;
            bool useScaledKesitTitle = (isFoundationPlan && _isTemel50Mode) || (!isFoundationPlan && _isKalip50Mode);
            string aaTitle = useScaledKesitTitle ? "A-A KESİTİ (1:50)" : "A-A KESİTİ";
            DrawKesitTitleBelowSchematic(tr, btr, db, aaTitle, contentTopX + spanAT * 0.5, yAaBaslikUstHizasi);
            layoutMinX = Math.Min(layoutMinX, contentTopX - sectionLayoutPadCm);
            layoutMaxX = Math.Max(layoutMaxX, contentTopX + spanAT + sectionLayoutPadCm);
            layoutMinY = Math.Min(layoutMinY, contentTopY - sectionLayoutPadCm);
            layoutMaxY = Math.Max(layoutMaxY, contentTopY + spanZT + sectionLayoutPadCm);
            double aaTitleHeight = _isTemel50Mode ? KesitBaslikMetinYukseklikTemel50Cm : KesitBaslikMetinYukseklikCm;
            layoutMinY = Math.Min(layoutMinY, yAaBaslikUstHizasi - aaTitleHeight - sectionLayoutPadCm);
            layoutMaxY = Math.Max(layoutMaxY, yAaBaslikUstHizasi + sectionLayoutPadCm);

            double aminL = GetAmin(slicesLeft), amaxL = GetAmax(slicesLeft), minZL = GetZmin(slicesLeft), maxZL = GetZmax(slicesLeft);
            double spanAL = Math.Max(180.0, amaxL - aminL);
            double spanZL = Math.Max(SectionMinStoryHeightCm * 0.5, maxZL - minZL);
            // Sol kutu: planda dikey kesit X = xvCut → boyunca Y zinciri; Y hizası
            double alignYLeft = ymin + (aminL + amaxL) * 0.5;
            double contentLeftY = offsetY + alignYLeft - spanAL * 0.5;
            double contentLeftX;
            // Sol kesit: sol aks balonunun kesite bakan yüzeyinden tam 100 cm sola (320 cm plan zorlaması yok)
            if (TryGetKesitSiniriPlacementZBounds(slicesLeft, isFoundationPlan, out double zLoKesitL))
            {
                double xKesitSiniriSagOfset = spanZL - (zLoKesitL - minZL);
                double xKesitSiniriSagHedef = xAksBalonSolDisYuzey - SectionMinLeftOfAxisLeftLabelsCm;
                contentLeftX = xKesitSiniriSagHedef - xKesitSiniriSagOfset;
            }
            else
            {
                contentLeftX = xAksBalonSolDisYuzey - SectionMinLeftOfAxisLeftLabelsCm - spanZL - 28.0;
            }
            DrawSchematicFromSlicesOneToOne(tr, btr, slicesLeft, contentLeftX, contentLeftY, aminL, minZL, spanAL, spanZL, horizontalAlongX: false, mirrorElevationX: true, isFoundationPlan, drawReferenceAxis: false);
            DrawKesitSiniriFromBeams(tr, btr, slicesLeft, contentLeftX, contentLeftY, aminL, minZL, spanZL, horizontalAlongX: false, mirrorElevationX: true, isFoundationPlan);
            DrawKesitSchematicDimensions(tr, btr, slicesLeft, contentLeftX, contentLeftY, aminL, minZL, spanZL, horizontalAlongX: false, mirrorElevationX: true, isFoundationPlan, planOlcuDimId);
            DrawKesitSchematicElementLabels(tr, btr, db, slicesLeft, floor, contentLeftX, contentLeftY, aminL, minZL, spanZL, false, true, isFoundationPlan);
            if (isFoundationPlan)
                DrawGrobetonUnderFoundationKesit(tr, btr, slicesLeft, contentLeftX, contentLeftY, aminL, minZL, spanZL, horizontalAlongX: false, mirrorElevationX: true);
            try { DrawKesitSchematicElevationKots(tr, btr, db, slicesLeft, contentLeftX, contentLeftY, aminL, minZL, spanZL, horizontalAlongX: false, mirrorElevationX: true, isFoundationPlan, similarKotSuffix); }
            catch { /* kesit kotları */ }
            // B-B başlık: sol aks balon sol dış yüzeyinden 60 cm sola; dikey metnin plana bakan sağ kenarı ≈ merkez + yükseklik/2
            double bbTitleHeight = _isTemel50Mode ? KesitBaslikMetinYukseklikTemel50Cm : KesitBaslikMetinYukseklikCm;
            double xBbBaslikMerkez = xAksBalonSolDisYuzey - KesitIsmiSolAksBalonSolBoslukCm - bbTitleHeight * 0.5;
            string bbTitle = useScaledKesitTitle ? "B-B KESİTİ (1:50)" : "B-B KESİTİ";
            DrawKesitTitleVerticalRightOfSection(tr, btr, db, bbTitle, xBbBaslikMerkez, contentLeftY + spanAL * 0.5);
            // Antet sol hizasi icin referans: soldaki kesitin kirpilmis NET geometri sinirinin en solu.
            // (kolon/perde/temel/temel hatili cizimi; baslik/metinler dahil degil)
            if (TryGetKesitSiniriBounds(slicesLeft, isFoundationPlan, out _, out _, out double zLoLeftNet, out double zHiLeftNet))
            {
                leftSectionMinX = contentLeftX + spanZL - (zHiLeftNet - minZL);
            }
            else
            {
                // Fallback: sol kesit kutusunun solu
                leftSectionMinX = contentLeftX;
            }
            layoutMinX = Math.Min(layoutMinX, contentLeftX - sectionLayoutPadCm);
            layoutMaxX = Math.Max(layoutMaxX, contentLeftX + spanZL + sectionLayoutPadCm);
            layoutMinY = Math.Min(layoutMinY, contentLeftY - sectionLayoutPadCm);
            layoutMaxY = Math.Max(layoutMaxY, contentLeftY + spanAL + sectionLayoutPadCm);
            layoutMinX = Math.Min(layoutMinX, xBbBaslikMerkez - bbTitleHeight * 0.5 - sectionLayoutPadCm);
            layoutMaxX = Math.Max(layoutMaxX, xBbBaslikMerkez + bbTitleHeight * 0.5 + sectionLayoutPadCm);
        }

        public bool DrawSectionFromUserCut(Database db, Editor ed, Point3d worldA, Point3d worldB, Point3d sectionInsertBase, string sectionLetterRaw)
        {
            if (db == null || ed == null) return false;
            if (_model == null || _model.Floors == null || _model.Floors.Count == 0)
            {
                ed.WriteMessage("\nST4KESIT: Modelde kat bulunamadi.");
                return false;
            }

            string letter = string.IsNullOrWhiteSpace(sectionLetterRaw) ? "C" : sectionLetterRaw.Trim();
            letter = letter.Length > 0 ? letter.Substring(0, 1).ToUpperInvariant() : "C";

            _ntsDrawFactory = NtsGeometryServices.Instance.CreateGeometryFactory();
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayers(tr, db);
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    var extBase = CalculateBaseExtents();
                    double floorWidth = (extBase.Xmax - extBase.Xmin) + 80.0;
                    double floorGap = 1000.0;
                    bool hasFoundations = _model.ContinuousFoundations.Count > 0 || _model.SlabFoundations.Count > 0 || _model.TieBeams.Count > 0 || _model.SingleFootings.Count > 0;
                    int planStartIndex = hasFoundations ? 1 : 0;

                    var candidates = new List<(FloorInfo floor, bool isFoundationPlan, double offsetX, double offsetY, (double Xmin, double Xmax, double Ymin, double Ymax) ext, Geometry structuralUnion)>();
                    if (hasFoundations && _model.Floors.Count > 0)
                    {
                        var firstFloor = _model.Floors[0];
                        Geometry firstFloorUnion = BuildFloorElementUnion(firstFloor);
                        var firstFloorAxisExt = GetAksSiniriEnvelope(firstFloorUnion);
                        candidates.Add((firstFloor, true, 0.0, 0.0, firstFloorAxisExt, firstFloorUnion));
                    }
                    for (int floorIdx = 0; floorIdx < _model.Floors.Count; floorIdx++)
                    {
                        var floor = _model.Floors[floorIdx];
                        double offsetX = (floorIdx + planStartIndex) * (floorWidth + floorGap);
                        double offsetY = 0.0;
                        Geometry elemUnion = BuildFloorElementUnion(floor);
                        var floorAxisExt = GetAksSiniriEnvelope(elemUnion);
                        candidates.Add((floor, false, offsetX, offsetY, floorAxisExt, elemUnion));
                    }
                    if (candidates.Count == 0)
                    {
                        ed.WriteMessage("\nST4KESIT: Kesit alinacak plan adayi bulunamadi.");
                        return false;
                    }

                    Point2d midWorld = new Point2d((worldA.X + worldB.X) * 0.5, (worldA.Y + worldB.Y) * 0.5);
                    const double pickTol = 120.0;
                    bool ContainsPick((double Xmin, double Xmax, double Ymin, double Ymax) ex, double ox, double oy, Point2d p)
                    {
                        return p.X >= ox + ex.Xmin - pickTol && p.X <= ox + ex.Xmax + pickTol &&
                               p.Y >= oy + ex.Ymin - pickTol && p.Y <= oy + ex.Ymax + pickTol;
                    }

                    int bestIdx = -1;
                    double bestDist2 = double.PositiveInfinity;
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var c = candidates[i];
                        if (!ContainsPick(c.ext, c.offsetX, c.offsetY, midWorld)) continue;
                        double cx = c.offsetX + (c.ext.Xmin + c.ext.Xmax) * 0.5;
                        double cy = c.offsetY + (c.ext.Ymin + c.ext.Ymax) * 0.5;
                        double dx = midWorld.X - cx;
                        double dy = midWorld.Y - cy;
                        double d2 = dx * dx + dy * dy;
                        if (d2 < bestDist2)
                        {
                            bestDist2 = d2;
                            bestIdx = i;
                        }
                    }

                    if (bestIdx < 0)
                    {
                        for (int i = 0; i < candidates.Count; i++)
                        {
                            var c = candidates[i];
                            double cx = c.offsetX + (c.ext.Xmin + c.ext.Xmax) * 0.5;
                            double cy = c.offsetY + (c.ext.Ymin + c.ext.Ymax) * 0.5;
                            double dx = midWorld.X - cx;
                            double dy = midWorld.Y - cy;
                            double d2 = dx * dx + dy * dy;
                            if (d2 < bestDist2)
                            {
                                bestDist2 = d2;
                                bestIdx = i;
                            }
                        }
                    }
                    if (bestIdx < 0)
                    {
                        ed.WriteMessage("\nST4KESIT: Kesit icin uygun plan secilemedi.");
                        return false;
                    }

                    var pick = candidates[bestIdx];
                    Point2d aModel = new Point2d(worldA.X - pick.offsetX, worldA.Y - pick.offsetY);
                    Point2d bModel = new Point2d(worldB.X - pick.offsetX, worldB.Y - pick.offsetY);
                    Vector2d cutDir = bModel - aModel;
                    if (cutDir.Length < 1e-6)
                    {
                        ed.WriteMessage("\nST4KESIT: Kesit hatti icin iki farkli nokta secin.");
                        return false;
                    }
                    Vector2d dirUnit = cutDir.GetNormal();
                    Point2d pa = aModel - dirUnit.MultiplyBy(SectionLineExtendCm);
                    Point2d pb = bModel + dirUnit.MultiplyBy(SectionLineExtendCm);
                    var colExtra = GetColumnTableExtraData(pick.floor);
                    var slices = CollectAllSectionSlices(pick.floor, pa, pb, aModel, dirUnit, pick.isFoundationPlan, colExtra);
                    if (slices == null || slices.Count == 0)
                    {
                        ed.WriteMessage("\nST4KESIT: Secilen hattan kesit dilimi bulunamadi.");
                        return false;
                    }

                    DrawUserDefinedSectionCutOnPlan(tr, btr, db, worldA, worldB, letter, pick.offsetX, pick.offsetY, pick.ext);

                    double amin = slices.Min(s => s.A0) - 40.0;
                    double amax = slices.Max(s => s.A1) + 40.0;
                    double minZ = slices.Min(s => s.Z0) - 25.0;
                    double maxZ = slices.Max(s => s.Z1) + 25.0;
                    double spanA = Math.Max(180.0, amax - amin);
                    double spanZ = Math.Max(SectionMinStoryHeightCm * 0.5, maxZ - minZ);

                    ObjectId planOlcuDimId = GetOrCreatePlanOlcuDimStyle(tr, db, 12.0);
                    double contentX = sectionInsertBase.X;
                    double contentY = sectionInsertBase.Y;

                    DrawSchematicFromSlicesOneToOne(tr, btr, slices, contentX, contentY, amin, minZ, spanA, spanZ, horizontalAlongX: true, mirrorElevationX: false, pick.isFoundationPlan, drawReferenceAxis: false);
                    DrawKesitSiniriFromBeams(tr, btr, slices, contentX, contentY, amin, minZ, spanZ, horizontalAlongX: true, mirrorElevationX: false, pick.isFoundationPlan);
                    DrawKesitSchematicDimensions(tr, btr, slices, contentX, contentY, amin, minZ, spanZ, horizontalAlongX: true, mirrorElevationX: false, pick.isFoundationPlan, planOlcuDimId);
                    DrawKesitSchematicElementLabels(tr, btr, db, slices, pick.floor, contentX, contentY, amin, minZ, spanZ, true, false, pick.isFoundationPlan);
                    if (pick.isFoundationPlan)
                        DrawGrobetonUnderFoundationKesit(tr, btr, slices, contentX, contentY, amin, minZ, spanZ, horizontalAlongX: true, mirrorElevationX: false);
                    try { DrawKesitSchematicElevationKots(tr, btr, db, slices, contentX, contentY, amin, minZ, spanZ, horizontalAlongX: true, mirrorElevationX: false, pick.isFoundationPlan); }
                    catch { }

                    double titleTopY = contentY - 24.0;
                    DrawKesitTitleBelowSchematic(tr, btr, db, letter + "-" + letter + " KESİTİ", contentX + spanA * 0.5, titleTopY);

                    tr.Commit();
                    ed.WriteMessage("\nST4KESIT: {0}-{0} kesiti cizildi. Kat: {1} ({2}).",
                        letter,
                        pick.floor.FloorNo,
                        pick.isFoundationPlan ? "temel" : "kalip");
                    return true;
                }
            }
            finally { _ntsDrawFactory = null; }
        }

        private void DrawUserDefinedSectionCutOnPlan(Transaction tr, BlockTableRecord btr, Database db, Point3d worldA, Point3d worldB, string letter,
            double offsetX, double offsetY, (double Xmin, double Xmax, double Ymin, double Ymax) ext)
        {
            Vector2d dir = new Vector2d(worldB.X - worldA.X, worldB.Y - worldA.Y);
            if (dir.Length < 1e-6) return;
            Vector2d u = dir.GetNormal();
            Vector2d n = new Vector2d(-u.Y, u.X);
            double midX = (worldA.X + worldB.X) * 0.5;
            double midY = (worldA.Y + worldB.Y) * 0.5;
            double rx0 = offsetX + ext.Xmin + SectionCutGapFromFloorBoundaryCm;
            double rx1 = offsetX + ext.Xmax - SectionCutGapFromFloorBoundaryCm;
            double ry0 = offsetY + ext.Ymin + SectionCutGapFromFloorBoundaryCm;
            double ry1 = offsetY + ext.Ymax - SectionCutGapFromFloorBoundaryCm;
            if (rx1 <= rx0 || ry1 <= ry0) return;

            var ts = new List<double>();
            void addIfInsideY(double xEdge)
            {
                if (Math.Abs(u.X) < 1e-9) return;
                double t = (xEdge - midX) / u.X;
                double y = midY + t * u.Y;
                if (y >= ry0 - 1e-6 && y <= ry1 + 1e-6) ts.Add(t);
            }
            void addIfInsideX(double yEdge)
            {
                if (Math.Abs(u.Y) < 1e-9) return;
                double t = (yEdge - midY) / u.Y;
                double x = midX + t * u.X;
                if (x >= rx0 - 1e-6 && x <= rx1 + 1e-6) ts.Add(t);
            }
            addIfInsideY(rx0);
            addIfInsideY(rx1);
            addIfInsideX(ry0);
            addIfInsideX(ry1);
            if (ts.Count < 2) return;
            ts.Sort();
            double tA = ts.First();
            double tB = ts.Last();
            if (tB - tA < 1e-3) return;

            Point3d pA = new Point3d(midX + tA * u.X, midY + tA * u.Y, 0);
            Point3d pB = new Point3d(midX + tB * u.X, midY + tB * u.Y, 0);

            var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            ObjectId dashId = lt.Has("DASHED") ? lt["DASHED"] : (lt.Has("Dashed") ? lt["Dashed"] : ObjectId.Null);
            double r = KesitEtiketRadiusCm;
            double segX0 = pA.X + u.X * r;
            double segY0 = pA.Y + u.Y * r;
            double segX1 = pB.X - u.X * r;
            double segY1 = pB.Y - u.Y * r;
            AppendDashedCutSegment(tr, btr, dashId, segX0, segY0, segX1, segY1);

            DrawKesitEtiketDxfStyle(tr, btr, db, pA, u, n, false, letter);
            DrawKesitEtiketDxfStyle(tr, btr, db, pB, u, n, true, letter);
        }

        /// <summary>Temel planı kesitlerinde her TEMEL (BEYKENT) parçası (sürekli / tekil / döşeme temeli) altına 10 cm grobeton — bitişik parçalar NTS <c>Union</c> ile birleştirilerek tek kapalı polyline olarak çizilir (<see cref="LayerGrobeton"/>).</summary>
        private void DrawGrobetonUnderFoundationKesit(Transaction tr, BlockTableRecord btr, List<SectionSlice> slices,
            double originX, double originY, double amin, double minZ, double spanZ,
            bool horizontalAlongX, bool mirrorElevationX)
        {
            if (slices == null || slices.Count == 0) return;
            const string layerTemel = "TEMEL (BEYKENT)";
            double h = GrobetonUnderFoundationKesitHeightCm;
            var grobetonRects = new List<Geometry>();
            var factory = StaticGeomFactory;
            foreach (var s in slices)
            {
                if (s.Layer != layerTemel) continue;
                if (s.Order != SectionOrderContinuousFoundation && s.Order != SectionOrderSingleFooting && s.Order != SectionOrderSlabFoundation)
                    continue;
                double aLo = Math.Min(s.A0, s.A1);
                double aHi = Math.Max(s.A0, s.A1);
                if (aHi - aLo < 1e-3) continue;
                double zLo = s.Z0;
                double xl, xr, yb, yt;
                if (horizontalAlongX)
                {
                    xl = originX + (aLo - amin);
                    xr = originX + (aHi - amin);
                    yt = originY + (zLo - minZ);
                    yb = yt - h;
                }
                else
                {
                    yb = originY + (aLo - amin);
                    yt = originY + (aHi - amin);
                    if (mirrorElevationX)
                    {
                        double xFace = originX + spanZ - (zLo - minZ);
                        double xOut = xFace + h;
                        xl = Math.Min(xFace, xOut);
                        xr = Math.Max(xFace, xOut);
                    }
                    else
                    {
                        double xFace = originX + (zLo - minZ);
                        double xOut = xFace - h;
                        xl = Math.Min(xFace, xOut);
                        xr = Math.Max(xFace, xOut);
                    }
                }
                if (xr - xl < 1e-6 || yt - yb < 1e-6) continue;
                var ring = factory.CreateLinearRing(new[]
                {
                    new Coordinate(xl, yb),
                    new Coordinate(xr, yb),
                    new Coordinate(xr, yt),
                    new Coordinate(xl, yt),
                    new Coordinate(xl, yb)
                });
                grobetonRects.Add(factory.CreatePolygon(ring));
            }
            if (grobetonRects.Count == 0) return;
            Geometry merged;
            try
            {
                merged = grobetonRects.Count == 1 ? grobetonRects[0] : CascadedPolygonUnion.Union(grobetonRects);
            }
            catch
            {
                foreach (var g in grobetonRects)
                {
                    if (g is Polygon p)
                    {
                        var c = p.ExteriorRing.Coordinates;
                        if (c.Length >= 4)
                            AppendClosedRectanglePolyline(tr, btr, c[0].X, c[0].Y, c[2].X, c[2].Y, LayerGrobeton, addGrobetonArConcHatch: true);
                    }
                }
                if (TryCombineGrobetonRectEnvelopes(grobetonRects, out double zx0, out double zx1, out double zy0, out double zy1))
                    DrawZeminSingleLineAlongFoundationSection(tr, btr, zx0, zx1, zy0, zy1, horizontalAlongX, mirrorElevationX);
                DrawGrobetonGapStripsBetweenFoundationPartsKesit(tr, btr, slices, originX, originY, amin, minZ, spanZ, horizontalAlongX, mirrorElevationX);
                return;
            }
            if (merged == null || merged.IsEmpty) return;
            DrawGeometryRingsAsPolylines(tr, btr, merged, LayerGrobeton, addHatch: true, hatchAngleRad: 0, exteriorRingsOnly: false, applySmallTriangleTrim: true, hatchPatternName: GrobetonHatchPatternName, hatchPatternScale: GrobetonHatchPatternScale, hatchLayerOverride: LayerTarama);
            DrawZeminLinesUnderGrobetonMerged(tr, btr, merged, horizontalAlongX, mirrorElevationX);
            DrawGrobetonGapStripsBetweenFoundationPartsKesit(tr, btr, slices, originX, originY, amin, minZ, spanZ, horizontalAlongX, mirrorElevationX);
        }

        /// <summary>Temel kesitinde ara boşluk şeritleri: temel hatılı <b>yoksa</b> sürekli/tekil/radye temeller arası; <b>varsa</b> temel hatılı + perde + kolon birleşim boşlukları (hatıl–hatıl / hatıl–perde / hatıl–kolon). Üst kot, komşu taraflardaki temel hatılların üst kotunun <b>en düşüğü</b> ile sınırlı (hiçbiri üstünde kalmaz).</summary>
        private static void DrawGrobetonGapStripsBetweenFoundationPartsKesit(Transaction tr, BlockTableRecord btr, List<SectionSlice> slices,
            double originX, double originY, double amin, double minZ, double spanZ,
            bool horizontalAlongX, bool mirrorElevationX)
        {
            if (slices == null || slices.Count == 0) return;
            const string layerTemel = "TEMEL (BEYKENT)";
            double zZeminGro = double.PositiveInfinity;
            foreach (var s in slices)
            {
                if (s.Layer != layerTemel) continue;
                if (s.Order != SectionOrderContinuousFoundation && s.Order != SectionOrderSingleFooting && s.Order != SectionOrderSlabFoundation)
                    continue;
                zZeminGro = Math.Min(zZeminGro, s.Z0 - GrobetonUnderFoundationKesitHeightCm);
            }

            bool sectionHasTemelHatili = slices.Any(s => s.Layer == LayerTemelHatiliKesit);
            double joinE = GrobetonFoundationAIntervalJoinEpsCm;
            double minGap = GrobetonBetweenFoundationsMinGapWidthCm;
            double hStrip = GrobetonBetweenFoundationsGapStripHeightCm;

            if (sectionHasTemelHatili)
            {
                double zTemelUst = double.NegativeInfinity;
                foreach (var s in slices)
                {
                    if (s.Layer != layerTemel) continue;
                    if (s.Order != SectionOrderContinuousFoundation && s.Order != SectionOrderSingleFooting && s.Order != SectionOrderSlabFoundation)
                        continue;
                    zTemelUst = Math.Max(zTemelUst, s.Z1);
                }
                double? zBlokajAltTemelUstu = double.IsNegativeInfinity(zTemelUst) ? (double?)null : zTemelUst;

                var rawHp = new List<(double lo, double hi, double zHat, bool isHat)>();
                foreach (var s in slices)
                {
                    double aLo = Math.Min(s.A0, s.A1);
                    double aHi = Math.Max(s.A0, s.A1);
                    if (aHi - aLo < 1e-3) continue;
                    if (s.Layer == LayerTemelHatiliKesit)
                        rawHp.Add((aLo, aHi, s.Z1, true));
                    else if (s.Layer == LayerPerde || s.Layer == LayerKolon)
                        rawHp.Add((aLo, aHi, double.PositiveInfinity, false));
                }
                if (rawHp.Count == 0) return;
                rawHp.Sort((a, b) => a.lo.CompareTo(b.lo));
                var mergedHp = new List<(double lo, double hi, double minHatZ1, bool anyHat)>();
                foreach (var seg in rawHp)
                {
                    if (mergedHp.Count == 0)
                    {
                        mergedHp.Add((seg.lo, seg.hi, seg.isHat ? seg.zHat : double.PositiveInfinity, seg.isHat));
                        continue;
                    }
                    var last = mergedHp[mergedHp.Count - 1];
                    if (seg.lo <= last.hi + joinE)
                    {
                        double mhz = last.minHatZ1;
                        if (seg.isHat) mhz = Math.Min(mhz, seg.zHat);
                        mergedHp[mergedHp.Count - 1] = (last.lo, Math.Max(last.hi, seg.hi), mhz, last.anyHat || seg.isHat);
                    }
                    else
                        mergedHp.Add((seg.lo, seg.hi, seg.isHat ? seg.zHat : double.PositiveInfinity, seg.isHat));
                }
                if (mergedHp.Count < 2) return;
                for (int i = 0; i < mergedHp.Count - 1; i++)
                {
                    if (!mergedHp[i].anyHat && !mergedHp[i + 1].anyHat) continue;
                    double gapLo = mergedHp[i].hi;
                    double gapHi = mergedHp[i + 1].lo;
                    if (gapHi - gapLo < minGap) continue;
                    double zTop = double.PositiveInfinity;
                    if (mergedHp[i].anyHat) zTop = Math.Min(zTop, mergedHp[i].minHatZ1);
                    if (mergedHp[i + 1].anyHat) zTop = Math.Min(zTop, mergedHp[i + 1].minHatZ1);
                    if (double.IsInfinity(zTop)) continue;
                    DrawGrobetonBlokajEarthGapStripsAt(tr, btr, gapLo, gapHi, zTop, zZeminGro, hStrip, minGap,
                        originX, originY, amin, minZ, spanZ, horizontalAlongX, mirrorElevationX,
                        drawEarthFillBelowBlokaj: false, zBlokBottomWorldOverride: zBlokajAltTemelUstu);
                }
                return;
            }

            var raw = new List<(double lo, double hi, double z1)>();
            foreach (var s in slices)
            {
                if (s.Layer != layerTemel) continue;
                if (s.Order != SectionOrderContinuousFoundation && s.Order != SectionOrderSingleFooting && s.Order != SectionOrderSlabFoundation)
                    continue;
                double aLo = Math.Min(s.A0, s.A1);
                double aHi = Math.Max(s.A0, s.A1);
                if (aHi - aLo < 1e-3) continue;
                raw.Add((aLo, aHi, s.Z1));
            }
            if (raw.Count == 0) return;
            raw.Sort((a, b) => a.lo.CompareTo(b.lo));
            var merged = new List<(double lo, double hi, double z1)>();
            foreach (var seg in raw)
            {
                if (merged.Count == 0)
                {
                    merged.Add(seg);
                    continue;
                }
                var last = merged[merged.Count - 1];
                if (seg.lo <= last.hi + joinE)
                {
                    last.hi = Math.Max(last.hi, seg.hi);
                    last.z1 = Math.Max(last.z1, seg.z1);
                    merged[merged.Count - 1] = last;
                }
                else
                    merged.Add(seg);
            }
            if (merged.Count < 2) return;
            for (int i = 0; i < merged.Count - 1; i++)
            {
                double gapLo = merged[i].hi;
                double gapHi = merged[i + 1].lo;
                if (gapHi - gapLo < minGap) continue;
                double zGroTop = Math.Max(merged[i].z1, merged[i + 1].z1);
                DrawGrobetonBlokajEarthGapStripsAt(tr, btr, gapLo, gapHi, zGroTop, zZeminGro, hStrip, minGap,
                    originX, originY, amin, minZ, spanZ, horizontalAlongX, mirrorElevationX,
                    drawEarthFillBelowBlokaj: true, zBlokBottomWorldOverride: null);
            }
        }

        /// <param name="drawEarthFillBelowBlokaj">Temel hatılı kesitte false: EARTH dikdörtgeni çizilmez.</param>
        /// <param name="zBlokBottomWorldOverride">Doluysa blokaj alt kotu (dünya Z, cm) = temel üst; null ise sabit 15 cm şerit.</param>
        private static void DrawGrobetonBlokajEarthGapStripsAt(Transaction tr, BlockTableRecord btr,
            double gapLo, double gapHi, double zGroTop, double zZeminGro, double hStrip, double minGap,
            double originX, double originY, double amin, double minZ, double spanZ,
            bool horizontalAlongX, bool mirrorElevationX,
            bool drawEarthFillBelowBlokaj, double? zBlokBottomWorldOverride)
        {
            if (gapHi - gapLo < minGap) return;
            double zGroBot = zGroTop - hStrip;
            double zBlokBot = zBlokBottomWorldOverride ?? (zGroBot - hStrip);
            if (zBlokBottomWorldOverride.HasValue && zBlokBot >= zGroBot - 1e-3)
                zBlokBot = zGroBot - hStrip;
            if (horizontalAlongX)
            {
                double xl = originX + (gapLo - amin);
                double xr = originX + (gapHi - amin);
                double ytGro = originY + (zGroTop - minZ);
                double ybGro = originY + (zGroBot - minZ);
                AppendGrobetonGapStripRectangleAnsi33Hatch(tr, btr, xl, ybGro, xr, ytGro);
                if (zGroBot - zBlokBot > 1e-3)
                {
                    double ytBl = originY + (zGroBot - minZ);
                    double ybBl = originY + (zBlokBot - minZ);
                    AppendBlokajGapStripRectangleArConcHatch(tr, btr, xl, ybBl, xr, ytBl);
                }
                if (drawEarthFillBelowBlokaj && zGroBot - zBlokBot > 1e-3 && !double.IsInfinity(zZeminGro) && zBlokBot - zZeminGro > 1e-3)
                {
                    double ytEz = originY + (zBlokBot - minZ);
                    double ybEz = originY + (zZeminGro - minZ);
                    AppendBlokajBelowStripRectangleEarthHatch(tr, btr, xl, ybEz, xr, ytEz);
                }
            }
            else
            {
                double yb = originY + (gapLo - amin);
                double yt = originY + (gapHi - amin);
                double xlGro, xrGro;
                if (mirrorElevationX)
                {
                    double xGroHi = originX + spanZ - (zGroTop - minZ);
                    double xGroLo = originX + spanZ - (zGroBot - minZ);
                    xlGro = Math.Min(xGroHi, xGroLo);
                    xrGro = Math.Max(xGroHi, xGroLo);
                }
                else
                {
                    double xGroHi = originX + (zGroTop - minZ);
                    double xGroLo = originX + (zGroBot - minZ);
                    xlGro = Math.Min(xGroHi, xGroLo);
                    xrGro = Math.Max(xGroHi, xGroLo);
                }
                AppendGrobetonGapStripRectangleAnsi33Hatch(tr, btr, xlGro, yb, xrGro, yt);
                if (zGroBot - zBlokBot > 1e-3)
                {
                    double xlBl, xrBl;
                    if (mirrorElevationX)
                    {
                        double xBlHi = originX + spanZ - (zGroBot - minZ);
                        double xBlLo = originX + spanZ - (zBlokBot - minZ);
                        xlBl = Math.Min(xBlHi, xBlLo);
                        xrBl = Math.Max(xBlHi, xBlLo);
                    }
                    else
                    {
                        double xBlHi = originX + (zGroBot - minZ);
                        double xBlLo = originX + (zBlokBot - minZ);
                        xlBl = Math.Min(xBlHi, xBlLo);
                        xrBl = Math.Max(xBlHi, xBlLo);
                    }
                    AppendBlokajGapStripRectangleArConcHatch(tr, btr, xlBl, yb, xrBl, yt);
                }
                if (drawEarthFillBelowBlokaj && zGroBot - zBlokBot > 1e-3 && !double.IsInfinity(zZeminGro) && zBlokBot - zZeminGro > 1e-3)
                {
                    double xlEz, xrEz;
                    if (mirrorElevationX)
                    {
                        double xTop = originX + spanZ - (zBlokBot - minZ);
                        double xBot = originX + spanZ - (zZeminGro - minZ);
                        xlEz = Math.Min(xTop, xBot);
                        xrEz = Math.Max(xTop, xBot);
                    }
                    else
                    {
                        double xTop = originX + (zBlokBot - minZ);
                        double xBot = originX + (zZeminGro - minZ);
                        xlEz = Math.Min(xTop, xBot);
                        xrEz = Math.Max(xTop, xBot);
                    }
                    AppendBlokajBelowStripRectangleEarthHatch(tr, btr, xlEz, yb, xrEz, yt);
                }
            }
        }

        /// <summary>Ara boşluk üst grobeton: kesitte ANSI33 tarama (<see cref="LayerTarama"/>), çizgi <see cref="LayerGrobeton"/>.</summary>
        private static void AppendGrobetonGapStripRectangleAnsi33Hatch(Transaction tr, BlockTableRecord btr, double xLo, double yLo, double xHi, double yHi)
        {
            double xl = Math.Min(xLo, xHi), xr = Math.Max(xLo, xHi);
            double yb = Math.Min(yLo, yHi), yt = Math.Max(yLo, yHi);
            if (xr - xl < 1e-6 || yt - yb < 1e-6) return;
            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(xl, yb), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(xr, yb), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(xr, yt), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(xl, yt), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = LayerGrobeton;
            pl.LineWeight = LineWeight.LineWeight050;
            pl.ConstantWidth = 0;
            ObjectId plId = AppendEntityReturnId(tr, btr, pl);
            AppendHatchAnsi33(tr, btr, plId, 0);
        }

        /// <summary>Ara boşluk alt blokaj: <see cref="LayerBlokaj"/> çizgi ve tarama (AR-CONC ölçek <see cref="BlokajGapStripArConcHatchScale"/>); katman şeffaflığı <see cref="LayerBlokajTransparencyPercent"/>.</summary>
        private static void AppendBlokajGapStripRectangleArConcHatch(Transaction tr, BlockTableRecord btr, double xLo, double yLo, double xHi, double yHi)
        {
            double xl = Math.Min(xLo, xHi), xr = Math.Max(xLo, xHi);
            double yb = Math.Min(yLo, yHi), yt = Math.Max(yLo, yHi);
            if (xr - xl < 1e-6 || yt - yb < 1e-6) return;
            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(xl, yb), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(xr, yb), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(xr, yt), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(xl, yt), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = LayerBlokaj;
            pl.LineWeight = LineWeight.LineWeight015;
            pl.ConstantWidth = 0;
            ObjectId plId = AppendEntityReturnId(tr, btr, pl);
            AppendHatchPredefined(tr, btr, plId, GrobetonHatchPatternName, BlokajGapStripArConcHatchScale, 0, LayerBlokaj);
        }

        /// <summary>Blokaj şeridinin altından kesitte grobeton altı (zemin çizgisi kotu) seviyesine: <see cref="LayerBlokaj"/>, EARTH ölçek <see cref="BlokajEarthHatchScale"/>, açı <see cref="BlokajEarthHatchAngleDeg"/>°.</summary>
        private static void AppendBlokajBelowStripRectangleEarthHatch(Transaction tr, BlockTableRecord btr, double xLo, double yLo, double xHi, double yHi)
        {
            double xl = Math.Min(xLo, xHi), xr = Math.Max(xLo, xHi);
            double yb = Math.Min(yLo, yHi), yt = Math.Max(yLo, yHi);
            if (xr - xl < 1e-6 || yt - yb < 1e-6) return;
            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(xl, yb), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(xr, yb), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(xr, yt), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(xl, yt), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = LayerBlokaj;
            pl.LineWeight = LineWeight.LineWeight015;
            pl.ConstantWidth = 0;
            ObjectId plId = AppendEntityReturnId(tr, btr, pl);
            double angleRad = BlokajEarthHatchAngleDeg * Math.PI / 180.0;
            AppendHatchPredefined(tr, btr, plId, BlokajEarthHatchPatternName, BlokajEarthHatchScale, angleRad, LayerBlokaj);
        }

        /// <summary>Tüm grobeton birleşiminin kesit zarfı boyunca <b>tek</b> zemin çizgisi: A-A alt yatay; B-B dış dikey (<paramref name="mirrorElevationX"/> true → max X). Uçlarda <see cref="KesitZeminCizgiTemelHatUzantisiCm"/>.</summary>
        private static void DrawZeminLinesUnderGrobetonMerged(Transaction tr, BlockTableRecord btr, Geometry merged, bool horizontalAlongX, bool mirrorElevationX)
        {
            if (merged == null || merged.IsEmpty) return;
            var env = merged.EnvelopeInternal;
            DrawZeminSingleLineAlongFoundationSection(tr, btr, env.MinX, env.MaxX, env.MinY, env.MaxY, horizontalAlongX, mirrorElevationX);
        }

        /// <summary>Grobeton dikdörtgen listesinin birleşik zarfı (union hata yolunda tek zemin için).</summary>
        private static bool TryCombineGrobetonRectEnvelopes(List<Geometry> rects, out double minX, out double maxX, out double minY, out double maxY)
        {
            minX = double.PositiveInfinity;
            maxX = double.NegativeInfinity;
            minY = double.PositiveInfinity;
            maxY = double.NegativeInfinity;
            foreach (var g in rects)
            {
                if (g == null || g.IsEmpty) continue;
                var e = g.EnvelopeInternal;
                if (e.IsNull) continue;
                minX = Math.Min(minX, e.MinX);
                maxX = Math.Max(maxX, e.MaxX);
                minY = Math.Min(minY, e.MinY);
                maxY = Math.Max(maxY, e.MaxY);
            }
            return minX <= maxX && minY <= maxY && maxX - minX >= 1e-9 && maxY - minY >= 1e-9;
        }

        private static void DrawZeminSingleLineAlongFoundationSection(Transaction tr, BlockTableRecord btr, double minX, double maxX, double minY, double maxY, bool horizontalAlongX, bool mirrorElevationX)
        {
            if (maxX - minX < 1e-9 || maxY - minY < 1e-9) return;
            if (horizontalAlongX)
                AppendZeminSegment(tr, btr, new Point2d(minX, minY), new Point2d(maxX, minY));
            else
            {
                double xLine = mirrorElevationX ? maxX : minX;
                AppendZeminSegment(tr, btr, new Point2d(xLine, minY), new Point2d(xLine, maxY));
            }
        }

        private static void AppendZeminSegment(Transaction tr, BlockTableRecord btr, Point2d p0, Point2d p1)
        {
            double ext = KesitZeminCizgiTemelHatUzantisiCm;
            if (Math.Abs(p1.Y - p0.Y) < 1e-6)
            {
                double y = p0.Y;
                double xa = Math.Min(p0.X, p1.X) - ext;
                double xb = Math.Max(p0.X, p1.X) + ext;
                p0 = new Point2d(xa, y);
                p1 = new Point2d(xb, y);
            }
            else if (Math.Abs(p1.X - p0.X) < 1e-6)
            {
                double x = p0.X;
                double ya = Math.Min(p0.Y, p1.Y) - ext;
                double yb = Math.Max(p0.Y, p1.Y) + ext;
                p0 = new Point2d(x, ya);
                p1 = new Point2d(x, yb);
            }
            if (Math.Abs(p0.X - p1.X) < 1e-9 && Math.Abs(p0.Y - p1.Y) < 1e-9) return;
            var pl = new Polyline(2);
            pl.AddVertexAt(0, p0, 0, 0, 0);
            pl.AddVertexAt(1, p1, 0, 0, 0);
            pl.Layer = LayerZemin;
            pl.LineWeight = LineWeight.LineWeight050;
            pl.ConstantWidth = 0;
            AppendEntity(tr, btr, pl);
        }

        /// <summary>
        /// Kalıp planı kesiti: kesitte görünen <b>perde, döşeme, kiriş</b> dilimlerinin yalnızca <b>üst</b> kotu (<see cref="SectionSlice.Z1"/>);
        /// <see cref="MergeKesitElevationZsAscending"/> ile yakın kotlar birleşir.
        /// Yedek: bu katmanlarda dilim yoksa önceki mantık (kiriş üstü veya döşeme üstü).
        /// Temel planı kesiti: temel üst/alt, grobeton alt, temel hatılı üst.
        /// </summary>
        private static List<double> CollectKesitKotElevationZs(List<SectionSlice> slices, bool isFoundationPlan)
        {
            var raw = new List<double>();
            if (slices == null || slices.Count == 0) return raw;
            const string layerTemel = "TEMEL (BEYKENT)";
            if (isFoundationPlan)
            {
                foreach (var s in slices)
                {
                    if (s.Layer != layerTemel) continue;
                    if (s.Order != SectionOrderContinuousFoundation && s.Order != SectionOrderSingleFooting && s.Order != SectionOrderSlabFoundation)
                        continue;
                    raw.Add(s.Z1);
                    raw.Add(s.Z0);
                    raw.Add(s.Z0 - GrobetonUnderFoundationKesitHeightCm);
                }
                foreach (var s in slices.Where(x => x.Layer == LayerTemelHatiliKesit))
                    raw.Add(s.Z1);
                return raw;
            }
            foreach (var s in slices.Where(s => s.Layer == LayerKiris || s.Layer == LayerDoseme || s.Layer == LayerPerde))
                raw.Add(s.Z1);
            if (raw.Count > 0) return raw;
            var beams = slices.Where(s => s.Layer == LayerKiris).ToList();
            if (beams.Count > 0)
            {
                foreach (var s in beams)
                    raw.Add(s.Z1);
            }
            else
            {
                foreach (var s in slices.Where(s => s.Layer == LayerDoseme))
                    raw.Add(s.Z1);
            }
            return raw;
        }

        private static void AppendKesitKotLineEntity(Transaction tr, BlockTableRecord btr, double x1, double y1, double x2, double y2)
        {
            var ln = new Line(new Point3d(x1, y1, 0), new Point3d(x2, y2, 0));
            ln.SetDatabaseDefaults();
            ln.Layer = LayerKotCizgisi;
            AppendEntity(tr, btr, ln);
        }

        /// <summary>Sol yarı üçgen: kapalı polyline sınır + SOLID tarama (entity rengi <see cref="KesitKotWedgeSolidHatchColorIndex"/>).</summary>
        private static void AppendKesitKotLeftWedgeSolidHatch(Transaction tr, BlockTableRecord btr, Point3d pA, Point3d pTL, Point3d pTM)
        {
            var pl = new Polyline(3);
            pl.AddVertexAt(0, new Point2d(pA.X, pA.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(pTL.X, pTL.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(pTM.X, pTM.Y), 0, 0, 0);
            pl.Closed = true;
            pl.SetDatabaseDefaults();
            pl.Layer = LayerKotCizgisi;
            AppendEntity(tr, btr, pl);

            var hatch = new Hatch();
            hatch.SetDatabaseDefaults();
            hatch.Layer = LayerKotCizgisi;
            hatch.Color = Color.FromColorIndex(ColorMethod.ByAci, KesitKotWedgeSolidHatchColorIndex);
            btr.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            hatch.Associative = true;
            hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { pl.ObjectId });
            try { hatch.EvaluateHatch(true); }
            catch { try { hatch.EvaluateHatch(false); } catch { } }
        }

        /// <summary>
        /// Yerel kot geometrisi (üst kesit A-A): tepe apex (0,0), kesite uzantı −X, üçgen +Y, 26 cm uzantı +X üst sağda.
        /// <paramref name="rotationRad"/> ile dünya koordinatına döndürülür; B-B için <c>π/2</c> (üst kesit ile aynı şekil, 90° dönmüş).
        /// </summary>
        private static void DrawKesitKotClassicSymbol(Transaction tr, BlockTableRecord btr, double apexX, double apexY, double rotationRad)
        {
            double h = KesitKotTriHeightCm;
            double w = KesitKotTriHalfWidthCm;
            double eSec = KesitKotExtTowardSectionCm;
            double eR = KesitKotExtTopRightCm;
            double c = Math.Cos(rotationRad);
            double s = Math.Sin(rotationRad);
            void Lw(double lx, double ly, out double wx, out double wy)
            {
                wx = apexX + c * lx - s * ly;
                wy = apexY + s * lx + c * ly;
            }
            Point3d P(double lx, double ly)
            {
                Lw(lx, ly, out double wx, out double wy);
                return new Point3d(wx, wy, 0);
            }
            Lw(-eSec, 0, out double x1, out double y1);
            Lw(0, 0, out double x2, out double y2);
            AppendKesitKotLineEntity(tr, btr, x1, y1, x2, y2);
            var pA = P(0, 0);
            var pTL = P(-w, h);
            var pTM = P(0, h);
            var pTR = P(w, h);
            AppendKesitKotLeftWedgeSolidHatch(tr, btr, pA, pTL, pTM);
            AppendKesitKotLineEntity(tr, btr, pTL.X, pTL.Y, pTR.X, pTR.Y);
            AppendKesitKotLineEntity(tr, btr, pA.X, pA.Y, pTR.X, pTR.Y);
            AppendKesitKotLineEntity(tr, btr, pTR.X, pTR.Y, pTM.X, pTM.Y);
            var pExt = P(w + eR, h);
            AppendKesitKotLineEntity(tr, btr, pTR.X, pTR.Y, pExt.X, pExt.Y);
        }

        /// <summary>Kesit kotu: <see cref="DBText"/> (MText değil); plan kotlarıyla aynı LEFT + BOTTOM + <c>AdjustAlignment(db)</c>.</summary>
        private static void AppendKesitKotElevationDbText(Transaction tr, BlockTableRecord btr, Database db, ObjectId textStyleId, string text, double x, double y, double rotationRad)
        {
            var txt = new DBText();
            txt.SetDatabaseDefaults();
            txt.Layer = LayerKotYazi;
            txt.Height = KesitKotTextHeightCm;
            txt.TextStyleId = textStyleId;
            txt.TextString = text ?? string.Empty;
            txt.HorizontalMode = TextHorizontalMode.TextLeft;
            txt.VerticalMode = TextVerticalMode.TextBottom;
            txt.Position = new Point3d(x, y, 0);
            txt.AlignmentPoint = new Point3d(x, y, 0);
            txt.Rotation = rotationRad;
            try { txt.AdjustAlignment(db); } catch { /* sürüm farkı */ }
            AppendEntity(tr, btr, txt);
        }

        /// <summary>Kesit şemasında kotlar: referans üçgen + metin (<see cref="LayerKotCizgisi"/>, <see cref="LayerKotYazi"/>).</summary>
        private void DrawKesitSchematicElevationKots(Transaction tr, BlockTableRecord btr, Database db, List<SectionSlice> slices,
            double originX, double originY, double amin, double minZ, double spanZ,
            bool horizontalAlongX, bool mirrorElevationX, bool isFoundationPlan,
            string similarKotSuffix = null)
        {
            if (slices == null || slices.Count == 0) return;
            if (!TryGetKesitSiniriBounds(slices, isFoundationPlan, out double aLo, out double aHi, out _, out _))
                return;
            var rawZ = CollectKesitKotElevationZs(slices, isFoundationPlan);
            var zsAsc = MergeKesitElevationZsAscending(rawZ, KesitKotMergeTolCm);
            if (zsAsc.Count == 0) return;
            double[] crowdedShiftLower = BuildKesitKotCrowdedShiftForLowerElevation(zsAsc, KesitKotCrowdedSeparationMinCm, KesitKotCrowdedShiftLowerCm);
            ObjectId styleId = GetOrCreateYaziBeykentTextStyle(tr, db);
            const double rotAa = 0.0;
            const double rotBb = Math.PI / 2.0;

            if (horizontalAlongX)
            {
                double xRight = originX + (aHi - amin);
                double xDatum = xRight + KesitKotDatumGapFromSectionCm;
                foreach (double zElev in zsAsc.OrderByDescending(v => v))
                {
                    int zi = IndexOfKesitMergedZ(zsAsc, zElev);
                    double extra = zi >= 0 && zi < crowdedShiftLower.Length ? crowdedShiftLower[zi] : 0.0;
                    double apexX = xDatum + extra;
                    double apexY = originY + (zElev - minZ);
                    if (!isFoundationPlan)
                        apexX -= KesitKotKalipPlanAaShiftLeftCm;
                    DrawKesitKotClassicSymbol(tr, btr, apexX, apexY, rotAa);
                    double lxText = KesitKotTriHalfWidthCm;
                    double lyText = KesitKotTriHeightCm + KesitKotTextAboveExtensionCm;
                    double c = Math.Cos(rotAa), si = Math.Sin(rotAa);
                    double textX = apexX + c * lxText - si * lyText;
                    double textY = apexY + si * lxText + c * lyText;
                    string kotText = FormatKesitKotElevationString(zElev);
                    if (!string.IsNullOrWhiteSpace(similarKotSuffix)) kotText += similarKotSuffix;
                    AppendKesitKotElevationDbText(tr, btr, db, styleId, kotText, textX, textY, rotAa);
                }
            }
            else
            {
                double yTop = originY + (aHi - amin);
                double apexYRow = yTop + KesitKotBbDatumGapFromSectionCm;
                foreach (double zElev in zsAsc.OrderBy(v => v))
                {
                    int zi = IndexOfKesitMergedZ(zsAsc, zElev);
                    double extraUp = zi >= 0 && zi < crowdedShiftLower.Length ? crowdedShiftLower[zi] : 0.0;
                    double apexX = mirrorElevationX ? originX + spanZ - (zElev - minZ) : originX + (zElev - minZ);
                    double apexY = apexYRow + extraUp;
                    if (!isFoundationPlan)
                        apexY -= KesitKotKalipPlanBbShiftDownCm;
                    DrawKesitKotClassicSymbol(tr, btr, apexX, apexY, rotBb);
                    double lxText = KesitKotTriHalfWidthCm;
                    double lyText = KesitKotTriHeightCm + KesitKotTextAboveExtensionCm;
                    double c = Math.Cos(rotBb), si = Math.Sin(rotBb);
                    double textX = apexX + c * lxText - si * lyText;
                    double textY = apexY + si * lxText + c * lyText;
                    string kotText = FormatKesitKotElevationString(zElev);
                    if (!string.IsNullOrWhiteSpace(similarKotSuffix)) kotText += similarKotSuffix;
                    AppendKesitKotElevationDbText(tr, btr, db, styleId, kotText, textX, textY, rotBb);
                }
            }
        }

        private static string BuildSimilarFloorKotSuffix(List<FloorInfo> similarFloors)
        {
            if (similarFloors == null || similarFloors.Count == 0) return null;
            var parts = similarFloors
                .Select(f => FormatKesitKotElevationString((f?.ElevationM ?? 0.0) * 100.0))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (parts.Count == 0) return null;
            return " (" + string.Join(", ", parts) + ")";
        }

        private static List<double> MergeKesitElevationZsAscending(List<double> raw, double tolCm)
        {
            var sorted = raw.Where(z => !double.IsNaN(z) && !double.IsInfinity(z)).OrderBy(z => z).ToList();
            if (sorted.Count == 0) return sorted;
            var merged = new List<double> { sorted[0] };
            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i] - merged[merged.Count - 1] > tolCm)
                    merged.Add(sorted[i]);
            }
            return merged;
        }

        /// <summary>
        /// <paramref name="zsAsc"/> artan Z; komşu kot farkı &lt; <paramref name="minSepCm"/> ise <b>düşük</b> kot (indeks i) için ofset <paramref name="shiftCm"/> —
        /// A-A’da +X, sol kesitte +Y uygulanır.
        /// </summary>
        private static double[] BuildKesitKotCrowdedShiftForLowerElevation(List<double> zsAsc, double minSepCm, double shiftCm)
        {
            var extra = new double[zsAsc != null ? zsAsc.Count : 0];
            if (zsAsc == null || zsAsc.Count < 2) return extra;
            for (int i = 0; i < zsAsc.Count - 1; i++)
            {
                if (zsAsc[i + 1] - zsAsc[i] < minSepCm - 1e-6)
                    extra[i] = shiftCm;
            }
            return extra;
        }

        private static int IndexOfKesitMergedZ(List<double> zsAsc, double zElev)
        {
            if (zsAsc == null) return -1;
            for (int i = 0; i < zsAsc.Count; i++)
            {
                if (Math.Abs(zsAsc[i] - zElev) < 1e-3)
                    return i;
            }
            return -1;
        }

        private static string FormatKesitKotElevationString(double zCm)
        {
            double m = zCm / 100.0;
            string s = string.Format(CultureInfo.InvariantCulture, "{0:+0.00;-0.00;0.00}", m);
            if (Math.Abs(m) < 1e-9)
                s = "±" + s.TrimStart('+');
            return KolonDonatiTableDrawer.NormalizeDiameterSymbol(s);
        }

        private static void AppendClosedRectanglePolyline(Transaction tr, BlockTableRecord btr, double xLo, double yLo, double xHi, double yHi, string layer, bool addGrobetonArConcHatch = false)
        {
            double xl = Math.Min(xLo, xHi), xr = Math.Max(xLo, xHi);
            double yb = Math.Min(yLo, yHi), yt = Math.Max(yLo, yHi);
            if (xr - xl < 1e-6 || yt - yb < 1e-6) return;
            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(xl, yb), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(xr, yb), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(xr, yt), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(xl, yt), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layer;
            pl.ConstantWidth = 0;
            if (layer == LayerGrobeton)
                pl.LineWeight = LineWeight.LineWeight050;
            if (addGrobetonArConcHatch && layer == LayerGrobeton)
            {
                ObjectId plId = AppendEntityReturnId(tr, btr, pl);
                AppendHatchPredefined(tr, btr, plId, GrobetonHatchPatternName, GrobetonHatchPatternScale, 0, LayerTarama);
            }
            else
                AppendEntity(tr, btr, pl);
        }

        /// <summary>A-A yatay kesit başlığı; <see cref="TextVerticalMode.TextTop"/> — <paramref name="yTopAnchor"/> metin üst kenarı.</summary>
        private void DrawKesitTitleBelowSchematic(Transaction tr, BlockTableRecord btr, Database db, string title, double cx, double yTopAnchor)
        {
            double titleHeight = _isTemel50Mode ? KesitBaslikMetinYukseklikTemel50Cm : KesitBaslikMetinYukseklikCm;
            var txt = new DBText
            {
                Layer = LayerKesitIsmi,
                Height = titleHeight,
                TextStyleId = GetOrCreateYaziBeykentTextStyle(tr, db),
                TextString = _isTemel50Mode ? ("%%u" + title + "%%u") : title,
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
            double titleHeight = _isTemel50Mode ? KesitBaslikMetinYukseklikTemel50Cm : KesitBaslikMetinYukseklikCm;
            var txt = new DBText
            {
                Layer = LayerKesitIsmi,
                Height = titleHeight,
                TextStyleId = GetOrCreateYaziBeykentTextStyle(tr, db),
                TextString = _isTemel50Mode ? ("%%u" + title + "%%u") : title,
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
            var factory = _ntsDrawFactory;
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
            var factory = _ntsDrawFactory;
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
            var factory = _ntsDrawFactory;
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
            var factory = _ntsDrawFactory;
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
            var factory = _ntsDrawFactory;
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
            var factory = _ntsDrawFactory;
            var coords = new Coordinate[5];
            for (int i = 0; i < 4; i++) coords[i] = C1(rect[i].X, rect[i].Y);
            coords[4] = coords[0];
            return factory.CreatePolygon(factory.CreateLinearRing(coords));
        }

        private Geometry SlabFootprintPoly(SlabInfo slab)
        {
            var factory = _ntsDrawFactory;
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
            var factory = _ntsDrawFactory;
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

        /// <summary>Plan üst/sol kesit ile aks balonu arası uzaklık: KESİT SINIRI kotu kolon/perde dilimlerine göre değil, kiriş-döşeme-temel gövdesine göre.</summary>
        private static bool TryGetKesitSiniriPlacementZBounds(List<SectionSlice> slices, bool isFoundationPlan, out double zLo)
        {
            zLo = 0;
            if (slices == null || slices.Count == 0) return false;
            var sl = slices.Where(s => s.Layer != LayerKolon && s.Layer != LayerPerde).ToList();
            if (sl.Count == 0) return false;
            if (isFoundationPlan)
            {
                double g = KesitSiniriTemelKotTasmasiCm;
                zLo = sl.Min(s => s.Z0) - g;
                return true;
            }
            var beams = sl.Where(s => s.Layer == LayerKiris).ToList();
            if (beams.Count > 0)
            {
                zLo = beams.Min(s => s.Z0) - KesitSiniriKotTasmasiCm;
                return true;
            }
            var slabs = sl.Where(s => s.Layer == LayerDoseme).ToList();
            if (slabs.Count == 0) return false;
            zLo = slabs.Min(s => s.Z0) - KesitSiniriDosemeAltTasmasiCm;
            return true;
        }

        /// <summary>KESİT SINIRI: kolon+perde dilimlerinin KESİT SINIRI dikdörtgeni kenarıyla örtüştüğü parçalar; kolon/perde yoksa veya hiç örtüşme yoksa çizilmez.</summary>
        private void DrawKesitSiniriFromBeams(Transaction tr, BlockTableRecord btr, List<SectionSlice> slices,
            double originX, double originY, double amin, double minZ, double spanZ, bool horizontalAlongX, bool mirrorElevationX,
            bool isFoundationPlan)
        {
            if (!TryGetKesitSiniriBounds(slices, isFoundationPlan, out double aLo, out double aHi, out double zLo, out double zHi))
                return;
            if (!slices.Any(s => s.Layer == LayerKolon || s.Layer == LayerPerde))
                return;
            double x0, x1, y0, y1;
            if (horizontalAlongX)
            {
                x0 = originX + (aLo - amin);
                x1 = originX + (aHi - amin);
                y0 = originY + (zLo - minZ);
                y1 = originY + (zHi - minZ);
            }
            else
            {
                y0 = originY + (aLo - amin);
                y1 = originY + (aHi - amin);
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
            }
            double xl = Math.Min(x0, x1), xr = Math.Max(x0, x1), yb = Math.Min(y0, y1), yt = Math.Max(y0, y1);
            const double zTol = 0.25;
            var rects = new List<(double x0, double x1, double y0, double y1)>();
            foreach (var s in slices.Where(s => s.Layer == LayerKolon || s.Layer == LayerPerde))
            {
                var poly = CreateSectionSliceSchematicPolygon(_ntsDrawFactory, s, originX, originY, amin, minZ, spanZ, horizontalAlongX, mirrorElevationX);
                var e = poly.EnvelopeInternal;
                rects.Add((e.MinX, e.MaxX, e.MinY, e.MaxY));
            }
            foreach (var seg in KesitSiniriSegmentsOnKolonPerdeEdges(xl, xr, yb, yt, rects, zTol))
            {
                var pl = new Polyline();
                pl.AddVertexAt(0, new Point2d(seg.ax, seg.ay), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(seg.bx, seg.by), 0, 0, 0);
                pl.Closed = false;
                pl.Layer = LayerKesitSiniri;
                pl.ConstantWidth = 0;
                AppendEntity(tr, btr, pl);
            }
        }

        private static List<(double ax, double ay, double bx, double by)> KesitSiniriSegmentsOnKolonPerdeEdges(
            double xl, double xr, double yb, double yt, List<(double x0, double x1, double y0, double y1)> rects, double tol)
        {
            var outSegs = new List<(double, double, double, double)>();
            List<(double lo, double hi)> Merge1D(List<(double lo, double hi)> iv, double mergeTol)
            {
                if (iv.Count == 0) return iv;
                iv.Sort((a, b) => a.lo.CompareTo(b.lo));
                var m = new List<(double lo, double hi)> { iv[0] };
                for (int i = 1; i < iv.Count; i++)
                {
                    var last = m[m.Count - 1];
                    if (iv[i].lo <= last.hi + mergeTol) m[m.Count - 1] = (last.lo, Math.Max(last.hi, iv[i].hi));
                    else m.Add(iv[i]);
                }
                return m;
            }
            var horizBottom = new List<(double lo, double hi)>();
            var horizTop = new List<(double lo, double hi)>();
            var vertLeft = new List<(double lo, double hi)>();
            var vertRight = new List<(double lo, double hi)>();
            foreach (var r in rects)
            {
                if (yb >= r.y0 - tol && yb <= r.y1 + tol)
                {
                    double a = Math.Max(xl, r.x0), b = Math.Min(xr, r.x1);
                    if (b > a + tol) horizBottom.Add((a, b));
                }
                if (yt >= r.y0 - tol && yt <= r.y1 + tol)
                {
                    double a = Math.Max(xl, r.x0), b = Math.Min(xr, r.x1);
                    if (b > a + tol) horizTop.Add((a, b));
                }
                if (xl >= r.x0 - tol && xl <= r.x1 + tol)
                {
                    double a = Math.Max(yb, r.y0), b = Math.Min(yt, r.y1);
                    if (b > a + tol) vertLeft.Add((a, b));
                }
                if (xr >= r.x0 - tol && xr <= r.x1 + tol)
                {
                    double a = Math.Max(yb, r.y0), b = Math.Min(yt, r.y1);
                    if (b > a + tol) vertRight.Add((a, b));
                }
            }
            foreach (var h in Merge1D(horizBottom, tol))
                outSegs.Add((h.lo, yb, h.hi, yb));
            foreach (var h in Merge1D(horizTop, tol))
                outSegs.Add((h.lo, yt, h.hi, yt));
            foreach (var v in Merge1D(vertLeft, tol))
                outSegs.Add((xl, v.lo, xl, v.hi));
            foreach (var v in Merge1D(vertRight, tol))
                outSegs.Add((xr, v.lo, xr, v.hi));
            return outSegs;
        }

        private static Polygon CreateSectionSliceSchematicPolygon(GeometryFactory gf, SectionSlice s,
            double originX, double originY, double amin, double minZ, double spanZ, bool horizontalAlongX, bool mirrorElevationX)
        {
            LinearRing ring;
            if (horizontalAlongX)
            {
                double x0 = originX + (s.A0 - amin);
                double x1 = originX + (s.A1 - amin);
                if (x1 - x0 < 6.0) { double m = (x0 + x1) * 0.5; x0 = m - 4.0; x1 = m + 4.0; }
                double y0 = originY + (s.Z0 - minZ);
                double y1 = originY + (s.Z1 - minZ);
                if (y1 - y0 < 3.0) y1 = y0 + 3.0;
                ring = gf.CreateLinearRing(new[]
                {
                    new Coordinate(x0, y0), new Coordinate(x1, y0), new Coordinate(x1, y1), new Coordinate(x0, y1), new Coordinate(x0, y0)
                });
            }
            else
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
                ring = gf.CreateLinearRing(new[]
                {
                    new Coordinate(x0, y0), new Coordinate(x1, y0), new Coordinate(x1, y1), new Coordinate(x0, y1), new Coordinate(x0, y0)
                });
            }
            return gf.CreatePolygon(ring);
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
            ObjectId styleId = GetOrCreateYaziBeykentTextStyle(tr, db);
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

        private static bool KesitSliceZOverlap(SectionSlice u, SectionSlice v)
        {
            double u0 = Math.Min(u.Z0, u.Z1), u1 = Math.Max(u.Z0, u.Z1);
            double v0 = Math.Min(v.Z0, v.Z1), v1 = Math.Max(v.Z0, v.Z1);
            return Math.Max(u0, v0) < Math.Min(u1, v1) - 1e-6;
        }

        /// <summary>Radye bu A istasyonunda temel hatılı (TEMEL HATILI) sütunu altında mı — ölçü atılmaz. Kesitte dar A + geniş margin; Z de örtüşmeli.</summary>
        private static bool KesitRadyeIstasyonuTemelHatiliAltinda(double aSta, SectionSlice radyeSlice, List<SectionSlice> temelHatiliSlices)
        {
            if (temelHatiliSlices == null || temelHatiliSlices.Count == 0 || radyeSlice == null) return false;
            const double eps = 1e-3;
            foreach (var h in temelHatiliSlices)
            {
                if (!KesitSliceZOverlap(radyeSlice, h)) continue;
                double h0 = Math.Min(h.A0, h.A1), h1 = Math.Max(h.A0, h.A1);
                double span = h1 - h0;
                double m = Math.Max(KesitRadyeOlcuHatilSkipMarginCm, span * 2.5);
                double cap = KesitOlcuRadyeAralikCm * 0.48;
                if (m > cap) m = cap;
                if (aSta >= h0 - m - eps && aSta <= h1 + m + eps)
                    return true;
            }
            return false;
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

        /// <summary>B-B en alt kiriş/hatıl: dayanak üst zincir kenarı; iç ölçü çizgisi mümkünse döşeme/radye bandında (kesitten geçsin).</summary>
        private static void KesitBbEnAltOlcuYerleri(SectionSlice s, double originY, double amin,
            IEnumerable<SectionSlice> ustOrtusenler, out double yRefUstKenar, out double yDimIcBant)
        {
            double b0 = Math.Min(s.A0, s.A1), b1 = Math.Max(s.A0, s.A1);
            yRefUstKenar = originY + b1 - amin;
            yDimIcBant = yRefUstKenar + 20.0;
            foreach (var d in ustOrtusenler)
            {
                if (d == null || !KesitSliceAOverlap(s, d)) continue;
                double d0 = Math.Min(d.A0, d.A1), d1 = Math.Max(d.A0, d.A1);
                double lo = Math.Max(b0, d0), hi = Math.Min(b1, d1);
                if (hi - lo >= 3.0)
                {
                    double yUstKisim = originY + lo + (hi - lo) * 0.78 - amin;
                    yDimIcBant = Math.Max(yDimIcBant, yUstKisim);
                }
                if (d0 >= b1 - 6.0 && d1 > d0 + KesitOlcuKesisimMinCm)
                {
                    double yDosemeIc = originY + d0 + (d1 - d0) * 0.42 - amin;
                    if (yDosemeIc >= yRefUstKenar - 1e-3)
                        yDimIcBant = Math.Max(yDimIcBant, yDosemeIc);
                }
            }
            if (yDimIcBant < yRefUstKenar + 20.0 - 1e-3)
                yDimIcBant = yRefUstKenar + 20.0;
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
                    var temelHatiliRadyeAtlama = slices.Where(x => x.Layer == LayerTemelHatiliKesit).ToList();
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
                        // Grobeton 10 cm: temel kalınlık ölçüsü ile aynı dikey ölçü çizgisi (dimX).
                        double yGro = y0 - GrobetonUnderFoundationKesitHeightCm;
                        AddAligned(new Point3d(xR, yGro, 0), new Point3d(xR, y0, 0), new Point3d(dimX, (yGro + y0) * 0.5, 0));
                    }
                    var temelGovdeKesisim = slices.Where(x => x.Layer == "TEMEL (BEYKENT)" &&
                        (x.Order == SectionOrderContinuousFoundation || x.Order == SectionOrderSingleFooting || x.Order == SectionOrderSlabFoundation)).ToList();
                    var hatilAaList = slices.Where(x => x.Order == SectionOrderTieBeam || x.Order == SectionOrderHatilStrip).ToList();
                    double hatilAaEnSag = hatilAaList.Count == 0 ? double.NegativeInfinity : hatilAaList.Max(x => Math.Max(x.A0, x.A1));
                    foreach (var s in hatilAaList)
                    {
                        double aMax = Math.Max(s.A0, s.A1);
                        double aMin = Math.Min(s.A0, s.A1);
                        double xR = originX + (aMax - amin);
                        double xLbeam = originX + (aMin - amin);
                        bool olcuSol = aMax >= hatilAaEnSag - 1e-3;
                        double xW = olcuSol ? xLbeam : xR;
                        int st = NextStaggerAa(olcuSol ? aMin - 1e7 : aMax);
                        double dimInner = olcuSol
                            ? xLbeam - KesitOlcuAaDimLineOffsetCm - st * KesitOlcuStaggerCm
                            : xR + KesitOlcuAaDimLineOffsetCm + st * KesitOlcuStaggerCm;
                        double dimOuterOfset = olcuSol ? -KesitOlcuCiftOlcuAraligiCm : KesitOlcuCiftOlcuAraligiCm;
                        double h0 = Math.Min(s.Z0, s.Z1), h1 = Math.Max(s.Z0, s.Z1);
                        double yBot = originY + (h0 - minZ), yTop = originY + (h1 - minZ);
                        if (Math.Abs(yTop - yBot) < 2.0) { double m = (yTop + yBot) * 0.5; yBot = m - 1.0; yTop = m + 1.0; }
                        var (okZ, ol, oh) = KesitEnBuyukZKesisim(s, temelGovdeKesisim);
                        bool stacked = false;
                        if (okZ && oh - ol >= KesitOlcuKesisimMinCm - 1e-6)
                        {
                            if (ol <= h0 + KesitOlcuZFlushEpsCm && oh - h0 >= KesitOlcuKesisimMinCm - 1e-6 && h1 - oh >= KesitOlcuKesisimMinCm - 1e-6)
                            {
                                double ySp = originY + (oh - minZ);
                                AddAligned(new Point3d(xW, yBot, 0), new Point3d(xW, ySp, 0), new Point3d(dimInner, (yBot + ySp) * 0.5, 0));
                                AddAligned(new Point3d(xW, ySp, 0), new Point3d(xW, yTop, 0), new Point3d(dimInner, (ySp + yTop) * 0.5, 0));
                                AddAligned(new Point3d(xW, yBot, 0), new Point3d(xW, yTop, 0), new Point3d(dimInner + dimOuterOfset, (yBot + yTop) * 0.5, 0));
                                stacked = true;
                            }
                            else if (oh >= h1 - KesitOlcuZFlushEpsCm && ol - h0 >= KesitOlcuKesisimMinCm - 1e-6 && h1 - ol >= KesitOlcuKesisimMinCm - 1e-6)
                            {
                                double ySp = originY + (ol - minZ);
                                AddAligned(new Point3d(xW, yBot, 0), new Point3d(xW, ySp, 0), new Point3d(dimInner, (yBot + ySp) * 0.5, 0));
                                AddAligned(new Point3d(xW, ySp, 0), new Point3d(xW, yTop, 0), new Point3d(dimInner, (ySp + yTop) * 0.5, 0));
                                AddAligned(new Point3d(xW, yBot, 0), new Point3d(xW, yTop, 0), new Point3d(dimInner + dimOuterOfset, (yBot + yTop) * 0.5, 0));
                                stacked = true;
                            }
                        }
                        if (!stacked)
                            AddAligned(new Point3d(xW, yBot, 0), new Point3d(xW, yTop, 0), new Point3d(dimInner, (yBot + yTop) * 0.5, 0));
                        // Grobeton: iç zincir (108/70/10); dış toplam (178) hattında da — 2. resim.
                        double yGroHat = yBot - GrobetonUnderFoundationKesitHeightCm;
                        AddAligned(new Point3d(xW, yGroHat, 0), new Point3d(xW, yBot, 0), new Point3d(dimInner, (yGroHat + yBot) * 0.5, 0));
                        if (stacked)
                        {
                            double dimOuterX = dimInner + dimOuterOfset;
                            AddAligned(new Point3d(xW, yGroHat, 0), new Point3d(xW, yBot, 0), new Point3d(dimOuterX, (yGroHat + yBot) * 0.5, 0));
                        }
                    }
                    int radyeIx = 0;
                    foreach (var s in slices.Where(x => x.Order == SectionOrderSlabFoundation))
                    {
                        double y0 = originY + (s.Z0 - minZ);
                        double y1 = originY + (s.Z1 - minZ);
                        if (y1 - y0 < 2.0) y1 = y0 + 2.0;
                        foreach (double aSta in KesitRadyeOlcuIstasyonlari(s.A0, s.A1, KesitOlcuRadyeAralikCm))
                        {
                            if (KesitRadyeIstasyonuTemelHatiliAltinda(aSta, s, temelHatiliRadyeAtlama))
                                continue;
                            double xm = originX + (aSta - amin);
                            double dimX = xm + KesitOlcuAaDimLineOffsetCm + (radyeIx++ % 5) * (KesitOlcuStaggerCm * 0.45);
                            AddAligned(new Point3d(xm, y0, 0), new Point3d(xm, y1, 0), new Point3d(dimX, (y0 + y1) * 0.5, 0));
                            double yGro = y0 - GrobetonUnderFoundationKesitHeightCm;
                            AddAligned(new Point3d(xm, yGro, 0), new Point3d(xm, y0, 0), new Point3d(dimX, (yGro + y0) * 0.5, 0));
                        }
                    }
                }
                else
                {
                    var dosemeKirisKesisim = slices.Where(x => x.Layer == LayerDoseme).ToList();
                    var kirisAaList = slices.Where(x => x.Layer == LayerKiris).ToList();
                    double kirisAaEnSag = kirisAaList.Count == 0 ? double.NegativeInfinity : kirisAaList.Max(x => Math.Max(x.A0, x.A1));
                    foreach (var s in kirisAaList)
                    {
                        double aMax = Math.Max(s.A0, s.A1);
                        double aMin = Math.Min(s.A0, s.A1);
                        double xR = originX + (aMax - amin);
                        double xLbeam = originX + (aMin - amin);
                        bool olcuSol = aMax >= kirisAaEnSag - 1e-3;
                        double xW = olcuSol ? xLbeam : xR;
                        int st = NextStaggerAa(olcuSol ? aMin - 1e7 : aMax);
                        double dimInner = olcuSol
                            ? xLbeam - KesitOlcuAaDimLineOffsetCm - st * KesitOlcuStaggerCm
                            : xR + KesitOlcuAaDimLineOffsetCm + st * KesitOlcuStaggerCm;
                        double dimOuterOfset = olcuSol ? -KesitOlcuCiftOlcuAraligiCm : KesitOlcuCiftOlcuAraligiCm;
                        double Zb = Math.Min(s.Z0, s.Z1), Zt = Math.Max(s.Z0, s.Z1);
                        double yBot = originY + (Zb - minZ), yTop = originY + (Zt - minZ);
                        if (Math.Abs(yTop - yBot) < 2.0) { double m = (yTop + yBot) * 0.5; yBot = m - 1.0; yTop = m + 1.0; }
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
                                AddAligned(new Point3d(xW, yTop, 0), new Point3d(xW, ySp, 0), new Point3d(dimInner, (yTop + ySp) * 0.5, 0));
                                AddAligned(new Point3d(xW, ySp, 0), new Point3d(xW, yBot, 0), new Point3d(dimInner, (ySp + yBot) * 0.5, 0));
                                AddAligned(new Point3d(xW, yTop, 0), new Point3d(xW, yBot, 0), new Point3d(dimInner + dimOuterOfset, (yTop + yBot) * 0.5, 0));
                                stacked = true;
                            }
                        }
                        if (!stacked)
                            AddAligned(new Point3d(xW, yBot, 0), new Point3d(xW, yTop, 0), new Point3d(dimInner, (yBot + yTop) * 0.5, 0));
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
                    var temelHatiliRadyeAtlama = slices.Where(x => x.Layer == LayerTemelHatiliKesit).ToList();
                    foreach (var s in slices.Where(x =>
                                 x.Order == SectionOrderContinuousFoundation ||
                                 x.Order == SectionOrderSingleFooting))
                    {
                        KotXR(s, out double xL, out double xR);
                        double yRef = originY + Math.Min(s.A0, s.A1) - amin;
                        int st = NextStaggerBb(yRef);
                        double yDimLine = yRef - KesitOlcuBbDimLineBelowRefCm - st * KesitOlcuStaggerCm;
                        AddAligned(new Point3d(xL, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xL + xR) * 0.5, yDimLine, 0));
                        // Grobeton 10 cm: temel genişlik ölçüsü ile aynı yatay ölçü çizgisi (yDimLine).
                        double hG = GrobetonUnderFoundationKesitHeightCm;
                        double xGroA = mirrorElevationX ? xR : xL - hG;
                        double xGroB = mirrorElevationX ? xR + hG : xL;
                        AddAligned(new Point3d(xGroA, yRef, 0), new Point3d(xGroB, yRef, 0), new Point3d((xGroA + xGroB) * 0.5, yDimLine, 0));
                    }
                    var temelGovdeBb = slices.Where(x => x.Layer == "TEMEL (BEYKENT)" &&
                        (x.Order == SectionOrderContinuousFoundation || x.Order == SectionOrderSingleFooting || x.Order == SectionOrderSlabFoundation)).ToList();
                    var hatilBbList = slices.Where(x => x.Order == SectionOrderTieBeam || x.Order == SectionOrderHatilStrip).ToList();
                    double hatilBbEnAltZincir = hatilBbList.Count == 0 ? double.PositiveInfinity : hatilBbList.Min(x => Math.Min(x.A0, x.A1));
                    foreach (var s in hatilBbList)
                    {
                        KotXR(s, out double xL, out double xR);
                        bool olcuUst = Math.Min(s.A0, s.A1) <= hatilBbEnAltZincir + 1e-3;
                        double yRef, yDimInner, yDimDis;
                        if (olcuUst)
                        {
                            KesitBbEnAltOlcuYerleri(s, originY, amin, temelGovdeBb.Where(x => x.Order == SectionOrderSlabFoundation), out yRef, out double yIc);
                            int st = NextStaggerBb(yRef + 1e7);
                            yDimInner = yIc + st * (KesitOlcuStaggerCm * 0.35);
                            yDimDis = yDimInner + KesitOlcuCiftOlcuAraligiCm;
                        }
                        else
                        {
                            yRef = originY + Math.Min(s.A0, s.A1) - amin;
                            int st = NextStaggerBb(yRef);
                            yDimInner = yRef - KesitOlcuBbDimLineBelowRefCm - st * KesitOlcuStaggerCm;
                            yDimDis = yDimInner - KesitOlcuCiftOlcuAraligiCm;
                        }
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
                                AddAligned(new Point3d(xL, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xL + xR) * 0.5, yDimDis, 0));
                                stacked = true;
                            }
                            else if (bestIr >= xR - eps && bestIl - xL >= KesitOlcuKesisimMinCm - 1e-6 && xR - bestIl >= KesitOlcuKesisimMinCm - 1e-6)
                            {
                                if (xR - bestIl <= bestIl - xL + 1e-3)
                                {
                                    AddAligned(new Point3d(bestIl, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((bestIl + xR) * 0.5, yDimInner, 0));
                                    AddAligned(new Point3d(xL, yRef, 0), new Point3d(bestIl, yRef, 0), new Point3d((xL + bestIl) * 0.5, yDimInner, 0));
                                }
                                else
                                {
                                    AddAligned(new Point3d(xL, yRef, 0), new Point3d(bestIl, yRef, 0), new Point3d((xL + bestIl) * 0.5, yDimInner, 0));
                                    AddAligned(new Point3d(bestIl, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((bestIl + xR) * 0.5, yDimInner, 0));
                                }
                                AddAligned(new Point3d(xL, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xL + xR) * 0.5, yDimDis, 0));
                                stacked = true;
                            }
                        }
                        if (!stacked)
                            AddAligned(new Point3d(xL, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xL + xR) * 0.5, yDimInner, 0));
                        // Grobeton: iç (yDimInner); dış toplam hattı (yDimDis) — A-A ile aynı mantık.
                        double hGh = GrobetonUnderFoundationKesitHeightCm;
                        double xGroHa = mirrorElevationX ? xR : xL - hGh;
                        double xGroHb = mirrorElevationX ? xR + hGh : xL;
                        double xGroMid = (xGroHa + xGroHb) * 0.5;
                        AddAligned(new Point3d(xGroHa, yRef, 0), new Point3d(xGroHb, yRef, 0), new Point3d(xGroMid, yDimInner, 0));
                        if (stacked)
                            AddAligned(new Point3d(xGroHa, yRef, 0), new Point3d(xGroHb, yRef, 0), new Point3d(xGroMid, yDimDis, 0));
                    }
                    foreach (var s in slices.Where(x => x.Order == SectionOrderSlabFoundation))
                    {
                        KotXR(s, out double xL, out double xR);
                        int ri = 0;
                        foreach (double aSta in KesitRadyeOlcuIstasyonlari(s.A0, s.A1, KesitOlcuRadyeAralikCm))
                        {
                            if (KesitRadyeIstasyonuTemelHatiliAltinda(aSta, s, temelHatiliRadyeAtlama))
                                continue;
                            double yRef = originY + (aSta - amin);
                            double yDimLine = yRef - KesitOlcuBbDimLineBelowRefCm - (ri++ % 4) * 8.0;
                            AddAligned(new Point3d(xL, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xL + xR) * 0.5, yDimLine, 0));
                            double hGr = GrobetonUnderFoundationKesitHeightCm;
                            double xGroA = mirrorElevationX ? xR : xL - hGr;
                            double xGroB = mirrorElevationX ? xR + hGr : xL;
                            AddAligned(new Point3d(xGroA, yRef, 0), new Point3d(xGroB, yRef, 0), new Point3d((xGroA + xGroB) * 0.5, yDimLine, 0));
                        }
                    }
                }
                else
                {
                    var kirisBbList = slices.Where(x => x.Layer == LayerKiris).ToList();
                    var dosemeBbAll = slices.Where(x => x.Layer == LayerDoseme).ToList();
                    double kirisBbEnAltZincir = kirisBbList.Count == 0 ? double.PositiveInfinity : kirisBbList.Min(x => Math.Min(x.A0, x.A1));
                    foreach (var s in kirisBbList)
                    {
                        KotXR(s, out double xL, out double xR);
                        bool olcuUst = Math.Min(s.A0, s.A1) <= kirisBbEnAltZincir + 1e-3;
                        double yRef, yDimInner, yDimDis;
                        if (olcuUst)
                        {
                            KesitBbEnAltOlcuYerleri(s, originY, amin, dosemeBbAll, out yRef, out double yIc);
                            int st = NextStaggerBb(yRef + 1e7);
                            yDimInner = yIc + st * (KesitOlcuStaggerCm * 0.35);
                            yDimDis = yDimInner + KesitOlcuCiftOlcuAraligiCm;
                        }
                        else
                        {
                            yRef = originY + Math.Min(s.A0, s.A1) - amin;
                            int st = NextStaggerBb(yRef);
                            yDimInner = yRef - KesitOlcuBbDimLineBelowRefCm - st * KesitOlcuStaggerCm;
                            yDimDis = yDimInner - KesitOlcuCiftOlcuAraligiCm;
                        }
                        SectionSlice bestD = null;
                        double bestA = 0;
                        double s0 = Math.Min(s.A0, s.A1), s1 = Math.Max(s.A0, s.A1);
                        foreach (var d in dosemeBbAll)
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
                            double sxa = Math.Min(xdL, xdR), sxb = Math.Max(xdL, xdR);
                            double il = Math.Max(xL, sxa), ir = Math.Min(xR, sxb);
                            double ov = ir - il;
                            double slabW = sxb - sxa;
                            double d1r = Math.Min(slabW, xR - Math.Max(xL, sxa));
                            double d1l = Math.Min(slabW, Math.Min(xR, sxb) - xL);
                            void IcDisIkiParca(double xSp)
                            {
                                AddAligned(new Point3d(xL, yRef, 0), new Point3d(xSp, yRef, 0), new Point3d((xL + xSp) * 0.5, yDimInner, 0));
                                AddAligned(new Point3d(xSp, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xSp + xR) * 0.5, yDimInner, 0));
                                AddAligned(new Point3d(xL, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xL + xR) * 0.5, yDimDis, 0));
                            }
                            // Döşeme solda: kısa solda (20) + uzun sağda (40), döşeme sağ kenarı = ir
                            if (ov >= KesitOlcuKesisimMinCm - 1e-6 && il <= xL + KesitOlcuZFlushEpsCm && xR - ir >= KesitOlcuKesisimMinCm - 1e-6)
                            {
                                IcDisIkiParca(ir);
                                stacked = true;
                            }
                            else if (d1l >= KesitOlcuKesisimMinCm - 1e-6 && xL + d1l < xR - KesitOlcuKesisimMinCm + 1e-6)
                            {
                                IcDisIkiParca(xL + d1l);
                                stacked = true;
                            }
                            else if (d1r >= KesitOlcuKesisimMinCm - 1e-6 && xR - d1r - xL >= KesitOlcuKesisimMinCm - 1e-6)
                            {
                                double xSp = xR - d1r;
                                if (ov >= KesitOlcuKesisimMinCm - 1e-6 && ir >= xR - KesitOlcuZFlushEpsCm && il - xL >= KesitOlcuKesisimMinCm - 1e-6)
                                {
                                    AddAligned(new Point3d(il, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((il + xR) * 0.5, yDimInner, 0));
                                    AddAligned(new Point3d(xL, yRef, 0), new Point3d(il, yRef, 0), new Point3d((xL + il) * 0.5, yDimInner, 0));
                                }
                                else
                                {
                                    AddAligned(new Point3d(xSp, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xSp + xR) * 0.5, yDimInner, 0));
                                    AddAligned(new Point3d(xL, yRef, 0), new Point3d(xSp, yRef, 0), new Point3d((xL + xSp) * 0.5, yDimInner, 0));
                                }
                                AddAligned(new Point3d(xL, yRef, 0), new Point3d(xR, yRef, 0), new Point3d((xL + xR) * 0.5, yDimDis, 0));
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
            double originX, double originY, double amin, double minZ, double spanA, double spanZ, bool horizontalAlongX, bool mirrorElevationX, bool isFoundationPlan, bool drawReferenceAxis = true)
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
                    Color = Color.FromColorIndex(ColorMethod.ByAci, 8),
                    TextStyleId = GetOrCreateYaziBeykentTextStyle(tr, btr.Database)
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

                var gf = _ntsDrawFactory;
                var slicePolys = new List<Geometry>();
                foreach (var s in slices.OrderBy(x => x.Order).ThenBy(x => x.Z0))
                {
                    double x0 = originX + (s.A0 - amin);
                    double x1 = originX + (s.A1 - amin);
                    if (x1 - x0 < 6.0) { double m = (x0 + x1) * 0.5; x0 = m - 4.0; x1 = m + 4.0; }
                    double y0 = originY + (s.Z0 - minZ);
                    double y1 = originY + (s.Z1 - minZ);
                    if (y1 - y0 < 3.0) y1 = y0 + 3.0;
                    var ring = gf.CreateLinearRing(new[]
                    {
                        new Coordinate(x0, y0), new Coordinate(x1, y0), new Coordinate(x1, y1), new Coordinate(x0, y1), new Coordinate(x0, y0)
                    });
                    slicePolys.Add(gf.CreatePolygon(ring));
                }
                TryDrawKesitSchematicMergedOutline(tr, btr, slicePolys, slices, isFoundationPlan, originX, originY, amin, minZ, spanZ, horizontalAlongX, mirrorElevationX);
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

                var gf2 = _ntsDrawFactory;
                var slicePolys2 = new List<Geometry>();
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
                    var ring = gf2.CreateLinearRing(new[]
                    {
                        new Coordinate(x0, y0), new Coordinate(x1, y0), new Coordinate(x1, y1), new Coordinate(x0, y1), new Coordinate(x0, y0)
                    });
                    slicePolys2.Add(gf2.CreatePolygon(ring));
                }
                TryDrawKesitSchematicMergedOutline(tr, btr, slicePolys2, slices, isFoundationPlan, originX, originY, amin, minZ, spanZ, horizontalAlongX, mirrorElevationX);
            }
        }

        /// <summary>KESİT SINIRI çizgisi ile aynı dikdörtgen (şema koordinatlarında).</summary>
        private Geometry BuildKesitSiniriClipPolygon(List<SectionSlice> slices, bool isFoundationPlan,
            double originX, double originY, double amin, double minZ, double spanZ, bool horizontalAlongX, bool mirrorElevationX)
        {
            if (!TryGetKesitSiniriBounds(slices, isFoundationPlan, out double aLo, out double aHi, out double zLo, out double zHi))
                return null;
            var gf = _ntsDrawFactory;
            double x0, x1, y0, y1;
            if (horizontalAlongX)
            {
                x0 = originX + (aLo - amin);
                x1 = originX + (aHi - amin);
                y0 = originY + (zLo - minZ);
                y1 = originY + (zHi - minZ);
            }
            else
            {
                y0 = originY + (aLo - amin);
                y1 = originY + (aHi - amin);
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
            }
            var ring = gf.CreateLinearRing(new[]
            {
                new Coordinate(x0, y0), new Coordinate(x1, y0), new Coordinate(x1, y1), new Coordinate(x0, y1), new Coordinate(x0, y0)
            });
            return gf.CreatePolygon(ring);
        }

        /// <summary>Dilimleri union eder; KESİT SINIRI dışını keser; <see cref="LayerKesitCizgisi"/> üzerinde çizer.</summary>
        private void TryDrawKesitSchematicMergedOutline(Transaction tr, BlockTableRecord btr, List<Geometry> slicePolys,
            List<SectionSlice> slices, bool isFoundationPlan, double originX, double originY, double amin, double minZ, double spanZ,
            bool horizontalAlongX, bool mirrorElevationX)
        {
            if (slicePolys == null || slicePolys.Count == 0) return;
            Geometry merged;
            try
            {
                var reduced = slicePolys.Select(g => ReducePrecisionSafe(g, 2) ?? g).Where(g => g != null && !g.IsEmpty).ToList();
                merged = reduced.Count == 1 ? reduced[0] : CascadedPolygonUnion.Union(reduced);
            }
            catch
            {
                try
                {
                    merged = slicePolys.Count == 1 ? slicePolys[0] : CascadedPolygonUnion.Union(slicePolys);
                }
                catch
                {
                    merged = slicePolys[0];
                }
            }
            if (merged == null || merged.IsEmpty) return;
            Geometry clip = BuildKesitSiniriClipPolygon(slices, isFoundationPlan, originX, originY, amin, minZ, spanZ, horizontalAlongX, mirrorElevationX);
            if (clip != null && !clip.IsEmpty)
            {
                try
                {
                    var m2 = ReducePrecisionSafe(merged, 2) ?? merged;
                    var c2 = ReducePrecisionSafe(clip, 2) ?? clip;
                    var inter = m2.Intersection(c2);
                    if (inter != null && !inter.IsEmpty)
                        merged = inter;
                }
                catch { /* tam gövde */ }
            }
            if (merged == null || merged.IsEmpty) return;
            // Kolon/perde ile aynı: ANSI33 tarama (TARAMA), sınır çizgisi TARAMA üzerinde; üstte KESIT CIZGISI
            DrawGeometryRingsAsPolylines(tr, btr, merged, LayerTarama, addHatch: true, hatchAngleRad: null,
                exteriorRingsOnly: true, applySmallTriangleTrim: false);
            if (clip != null && !clip.IsEmpty)
                DrawKesitCizgisiExcludingSiniriEdges(tr, btr, merged, clip);
            else
                DrawGeometryRingsAsPolylines(tr, btr, merged, LayerKesitCizgisi, addHatch: false, exteriorRingsOnly: false, applySmallTriangleTrim: false);
        }

        /// <summary>KESİT SINIRI dikdörtgeni üzerindeki kenar segmentlerini çizmez (sadece iç gövde hatları).</summary>
        private static bool KesitCizgisiSegmentOnSiniriKenari(double x1, double y1, double x2, double y2,
            double xl, double xr, double yb, double yt, double tol)
        {
            if (Math.Abs(y1 - y2) < tol && Math.Abs(y1 - yb) < tol)
            {
                double xa = Math.Min(x1, x2), xb = Math.Max(x1, x2);
                if (xa >= xl - tol && xb <= xr + tol) return true;
            }
            if (Math.Abs(y1 - y2) < tol && Math.Abs(y1 - yt) < tol)
            {
                double xa = Math.Min(x1, x2), xb = Math.Max(x1, x2);
                if (xa >= xl - tol && xb <= xr + tol) return true;
            }
            if (Math.Abs(x1 - x2) < tol && Math.Abs(x1 - xl) < tol)
            {
                double ya = Math.Min(y1, y2), yb2 = Math.Max(y1, y2);
                if (ya >= yb - tol && yb2 <= yt + tol) return true;
            }
            if (Math.Abs(x1 - x2) < tol && Math.Abs(x1 - xr) < tol)
            {
                double ya = Math.Min(y1, y2), yb2 = Math.Max(y1, y2);
                if (ya >= yb - tol && yb2 <= yt + tol) return true;
            }
            return false;
        }

        private void DrawKesitCizgisiExcludingSiniriEdges(Transaction tr, BlockTableRecord btr, Geometry merged, Geometry clipPoly)
        {
            if (merged == null || merged.IsEmpty) return;
            var env = clipPoly.EnvelopeInternal;
            double xl = env.MinX, xr = env.MaxX, yb = env.MinY, yt = env.MaxY;
            const double edgeTol = 0.45;
            const double minSeg = 0.4;

            var rings = new List<Coordinate[]>();
            if (merged is Polygon poly)
            {
                rings.Add(poly.ExteriorRing.Coordinates);
                for (int h = 0; h < poly.NumInteriorRings; h++)
                    rings.Add(poly.InteriorRings[h].Coordinates);
            }
            else if (merged is MultiPolygon mp)
            {
                for (int i = 0; i < mp.NumGeometries; i++)
                {
                    var p = (Polygon)mp.GetGeometryN(i);
                    rings.Add(p.ExteriorRing.Coordinates);
                    for (int h = 0; h < p.NumInteriorRings; h++)
                        rings.Add(p.InteriorRings[h].Coordinates);
                }
            }
            else if (merged is GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                {
                    if (gc.GetGeometryN(i) is Polygon p2)
                    {
                        rings.Add(p2.ExteriorRing.Coordinates);
                        for (int h = 0; h < p2.NumInteriorRings; h++)
                            rings.Add(p2.InteriorRings[h].Coordinates);
                    }
                }
            }

            var allChains = new List<List<Point2d>>();
            foreach (var coords in rings)
            {
                if (coords == null || coords.Length < 2) continue;
                int n = coords.Length;
                if (n > 1 && coords[0].Equals2D(coords[n - 1])) n--;
                if (n < 2) continue;

                var chain = new List<Point2d>();
                void FlushChain()
                {
                    if (chain.Count < 2) { chain.Clear(); return; }
                    var dedup = new List<Point2d> { chain[0] };
                    for (int k = 1; k < chain.Count; k++)
                    {
                        if (dedup[dedup.Count - 1].GetDistanceTo(chain[k]) >= minSeg)
                            dedup.Add(chain[k]);
                    }
                    if (dedup.Count < 2) { chain.Clear(); return; }
                    dedup = KesitCizgisiRemoveCollinearVertices(dedup);
                    if (dedup.Count < 2) { chain.Clear(); return; }
                    allChains.Add(dedup);
                    chain.Clear();
                }

                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    double ax = coords[i].X, ay = coords[i].Y;
                    double bx = coords[j].X, by = coords[j].Y;
                    if (KesitCizgisiSegmentOnSiniriKenari(ax, ay, bx, by, xl, xr, yb, yt, edgeTol))
                        FlushChain();
                    else
                    {
                        if (chain.Count == 0)
                            chain.Add(new Point2d(ax, ay));
                        chain.Add(new Point2d(bx, by));
                    }
                }
                FlushChain();
            }

            const double joinTol = 1.2;
            foreach (var joined in KesitCizgisiJoinTouchingOpenChains(allChains, joinTol))
            {
                if (joined == null || joined.Count < 2) continue;
                var pl = new Polyline();
                for (int k = 0; k < joined.Count; k++)
                    pl.AddVertexAt(k, joined[k], 0, 0, 0);
                pl.Layer = LayerKesitCizgisi;
                pl.ConstantWidth = 0;
                AppendEntity(tr, btr, pl);
            }
        }

        /// <summary>Uçları birbirine değen açık polyline zincirlerini tek polyline yapar (JOIN benzeri).</summary>
        private static List<List<Point2d>> KesitCizgisiJoinTouchingOpenChains(List<List<Point2d>> chains, double tol)
        {
            if (chains == null || chains.Count <= 1) return chains ?? new List<List<Point2d>>();
            var list = chains.Where(c => c != null && c.Count >= 2).Select(c => new List<Point2d>(c)).ToList();
            if (list.Count <= 1) return list;
            bool changed;
            int guard = 0;
            do
            {
                changed = false;
                if (++guard > list.Count * list.Count + 100) break;
                for (int i = 0; i < list.Count && !changed; i++)
                {
                    for (int j = i + 1; j < list.Count && !changed; j++)
                    {
                        var merged = KesitCizgisiTryMergeTwoOpenPolylines(list[i], list[j], tol);
                        if (merged != null)
                        {
                            list[i] = merged;
                            list.RemoveAt(j);
                            changed = true;
                        }
                    }
                }
            } while (changed);
            return list;
        }

        private static List<Point2d> KesitCizgisiTryMergeTwoOpenPolylines(List<Point2d> a, List<Point2d> b, double tol)
        {
            if (a == null || b == null || a.Count < 2 || b.Count < 2) return null;
            var a0 = a[0];
            var ae = a[a.Count - 1];
            var b0 = b[0];
            var be = b[b.Count - 1];
            if (ae.GetDistanceTo(b0) <= tol)
            {
                var r = new List<Point2d>(a);
                for (int k = 1; k < b.Count; k++) r.Add(b[k]);
                return KesitCizgisiRemoveCollinearVertices(r);
            }
            if (ae.GetDistanceTo(be) <= tol)
            {
                var r = new List<Point2d>(a);
                for (int k = b.Count - 2; k >= 0; k--) r.Add(b[k]);
                return KesitCizgisiRemoveCollinearVertices(r);
            }
            if (a0.GetDistanceTo(b0) <= tol)
            {
                var r = new List<Point2d>();
                for (int k = a.Count - 1; k >= 0; k--) r.Add(a[k]);
                for (int k = 1; k < b.Count; k++) r.Add(b[k]);
                return KesitCizgisiRemoveCollinearVertices(r);
            }
            if (a0.GetDistanceTo(be) <= tol)
            {
                var r = new List<Point2d>(b);
                for (int k = 1; k < a.Count; k++) r.Add(a[k]);
                return KesitCizgisiRemoveCollinearVertices(r);
            }
            return null;
        }

        /// <summary>Açı değiştirmeyen düz hat üzerindeki ara vertex'leri siler (KESIT CIZGISI).</summary>
        private static List<Point2d> KesitCizgisiRemoveCollinearVertices(List<Point2d> pts)
        {
            if (pts == null || pts.Count < 3) return pts;
            const double lineTol = 0.08;
            var list = new List<Point2d>(pts);
            bool changed;
            int guard = 0;
            do
            {
                changed = false;
                if (list.Count < 3 || guard++ > list.Count + 8) break;
                for (int i = 1; i < list.Count - 1; i++)
                {
                    var a = list[i - 1];
                    var b = list[i];
                    var c = list[i + 1];
                    if (KesitCizgisiPointToSegmentDist(b, a, c) <= lineTol)
                    {
                        list.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            } while (changed);
            return list;
        }

        private static double KesitCizgisiPointToSegmentDist(Point2d p, Point2d a, Point2d segB)
        {
            double vx = segB.X - a.X, vy = segB.Y - a.Y;
            double len = Math.Sqrt(vx * vx + vy * vy);
            if (len < 1e-9) return p.GetDistanceTo(a);
            double t = ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / (len * len);
            if (t <= 0) return p.GetDistanceTo(a);
            if (t >= 1) return p.GetDistanceTo(segB);
            double qx = a.X + t * vx, qy = a.Y + t * vy;
            double dx = p.X - qx, dy = p.Y - qy;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
