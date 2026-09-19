using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Union;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        private const double Kolon50GorunusGapAboveCopyCm = 80.0;
        private const double Kolon50GorunusColStubCm = 40.0;
        private const double Kolon50GorunusTemelDisTasimaCm = 20.0;
        private const double Kolon50GorunusKotSagaKaydirCm = 30.0;
        private const double Kolon50GorunusSlabTouchTolCm = 8.0;
        private const double Kolon50GorunusWallArtikMaxCm = 15.0;
        private const double Kolon50GorunusOlcuUstBoslukCm = 30.0;
        private const double Kolon50GorunusDikeyOlcuSagaCm = 50.0;
        private const double Kolon50GorunusCiftOlcuAraCm = 18.0;
        private const double KolonDuseyEtriyeOlcuAraCm = 80.0;
        private const double KolonDuseyEtriyeOlcuKolondanCm = 20.0;
        private const double KolonDuseyAcilimGapFromOlcuCm = 45.0;
        private const double KolonDuseyAcilimColGapCm = 50.0;
        private const double KolonDuseyAcilimWidthCm = 200.0;
        private const double KolonDuseyAcilimSasirCm = 5.0;
        /// <summary>Kesit değişimi gönye/firkete ve extra filiz açılımları ana şaşırtmanın sağına bu aralıkla dizilir.</summary>
        private const double KolonDuseyAcilimKesitExtraGapCm = 65.0;
        private const string LayerSheetSummary50 = "SHEETSUMMARY-50";
        private const string LayerSheetView = "SheetView";
        private const double PerdeGorunusAntetGapCm = 20.0;
        private const double PerdeGorunusAntetPadCm = 50.0;
        private const double PerdeGorunusAntetAraCm = 50.0;
        private const double PerdeGorunusAntetSagKisaltCm = 34.0;
        private double PerdeGorunusSagTasimCm =>
            Kolon50GorunusDikeyOlcuSagaCm
            + KesitKotDatumGapFromSectionCm
            + Kolon50GorunusKotSagaKaydirCm
            + KesitKotTriHalfWidthCm
            + 60.0;
        /// <summary>İlk kopya yerleşiminde antetlerin üst üste binmemesi için pay; son 50 cm aralık SpacePerdeGorunusSheetsToAntetGap ile verilir.</summary>
        private double PerdeGorunusCopyGapXCm =>
            PerdeGorunusSagTasimCm
            + Kolon50GorunusTemelDisTasimaCm
            + PerdeGorunusAntetPadCm
            + (PerdeGorunusAntetPadCm - PerdeGorunusAntetSagKisaltCm)
            + PerdeGorunusAntetGapCm * 2.0
            + PerdeGorunusAntetAraCm;
        private const string LayerTemelBeykent = "TEMEL (BEYKENT)";
        private const string LayerDosemeGovde = "DOSEME (BEYKENT)";
        private const string LayerDonatiGovde = "DONATI (BEYKENT)";
        private const string LayerDonatiYazisiPerde = "DONATI YAZISI (BEYKENT)";
        private const string LayerCirozBeykent = "CIROZ (BEYKENT)";

        private sealed class Kolon50GorunusPending
        {
            public FloorInfo Floor;
            public List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> PlanGroup;
            public List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> RotatedGroup;
            public AffineTransformation Rot;
            public AffineTransformation Trf;
            public double PlanOffsetX;
            public double PlanOffsetY;
            public int MinWallNo;
        }

        private void FlushKolon50PerdeGorunus(Transaction tr, BlockTableRecord btr, double firstCopyRowTopY)
        {
            if (_kolon50GorunusPending == null || _kolon50GorunusPending.Count == 0) return;
            EnsurePlanLayer(tr, btr.Database, LayerDosemeGovde, 2, LineWeight.LineWeight030, useDashed: false);
            EnsurePlanLayer(tr, btr.Database, LayerDonatiGovde, 4, LineWeight.LineWeight040, useDashed: false);
            EnsurePlanLayer(tr, btr.Database, LayerDonatiYazisiPerde, 3, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, btr.Database, LayerCirozBeykent, 140, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, btr.Database, LayerYazi, 4, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, btr.Database, LayerSheetSummary50, 152, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, btr.Database, LayerSheetView, 40, LineWeight.LineWeight020, useDashed: false);
            double y0 = firstCopyRowTopY + Kolon50GorunusGapAboveCopyCm;
            var sheets = new List<(int wallNo, Envelope env)>();
            foreach (var stack in _kolon50GorunusPending
                .GroupBy(p => p.MinWallNo)
                .OrderBy(g => g.Key))
            {
                var list = stack.OrderBy(p => p.Floor.ElevationM).ToList();
                var before = SnapshotKolon50BtrIds(btr);
                var env = DrawKolon50StackedPerdeGorunus(tr, btr, list, y0);
                RememberKolon50SheetEntities(btr, stack.Key, before);
                if (env != null) sheets.Add((stack.Key, env));
            }
            var spaced = SpacePerdeGorunusSheetsToAntetGap(tr, btr, sheets);
            DrawPerdeGorunusAntetFrames(tr, btr, spaced);
        }

        private Envelope DrawKolon50StackedPerdeGorunus(
            Transaction tr,
            BlockTableRecord btr,
            List<Kolon50GorunusPending> stack,
            double y0)
        {
            if (stack == null || stack.Count == 0) return null;
            var gf = _ntsDrawFactory;
            if (gf == null) return null;

            var stories = new List<(Kolon50GorunusPending p, double zBot, double zTop, Geometry planUnion, Geometry wallUnion)>();
            foreach (var p in stack)
            {
                if (p.Floor == null || p.PlanGroup == null || p.RotatedGroup == null || p.Rot == null || p.Trf == null)
                    continue;
                double floorLevelCm = (_model.BuildingBaseKotu + p.Floor.ElevationM) * 100.0;
                double zTop = double.NaN, zBot = double.NaN;
                foreach (var it in p.PlanGroup)
                {
                    if (it.beam == null) continue;
                    double zu = floorLevelCm + Math.Max(it.beam.Point1KotCm, it.beam.Point2KotCm);
                    double h = it.beam.HeightCm > 0 ? it.beam.HeightCm : 40.0;
                    zTop = double.IsNaN(zTop) ? zu : Math.Max(zTop, zu);
                    zBot = double.IsNaN(zBot) ? zu - h : Math.Min(zBot, zu - h);
                }
                if (double.IsNaN(zTop) || double.IsNaN(zBot)) continue;
                stories.Add((p, zBot, zTop, UnionPlanGroup(p.PlanGroup), UnionWallsOnly(p.PlanGroup)));
            }
            if (stories.Count == 0) return null;

            int wallPad = GetLabelPadWidth(_model.Beams
                .Where(x => x.IsWallFlag == 1)
                .Select(x => GetBeamNumero(x.BeamId))
                .DefaultIfEmpty(0)
                .Max());

            var lowest = stories[0];
            double sectionY = double.NaN;
            foreach (var it in lowest.p.RotatedGroup)
            {
                if (it.wall == null || it.wall.IsEmpty) continue;
                var w0 = lowest.p.Trf.Transform(it.wall);
                if (w0 == null || w0.IsEmpty) continue;
                var we = w0.EnvelopeInternal;
                sectionY = (we.MinY + we.MaxY) * 0.5;
                break;
            }
            var temelSpans = new List<(double x0, double x1, double z0, double z1)>();
            if (lowest.planUnion != null && !lowest.planUnion.IsEmpty)
                CollectTemelSpansAlongWall(lowest.planUnion, lowest.p.Rot, lowest.p.Trf, lowest.p.PlanOffsetX, lowest.p.PlanOffsetY, lowest.p.Floor, temelSpans, sectionY);

            double zTemelBot = temelSpans.Count > 0 ? temelSpans.Min(s => s.z0) : lowest.zBot;
            double zTemelTop = temelSpans.Count > 0 ? temelSpans.Max(s => s.z1) : lowest.zBot;
            if (zTemelTop < lowest.zBot - 1.0) zTemelTop = lowest.zBot;

            var topStory = stories[stories.Count - 1];
            FloorInfo nextFloor = _model.Floors
                .Where(f => f != null && f.ElevationM > topStory.p.Floor.ElevationM + 1e-6)
                .OrderBy(f => f.ElevationM)
                .FirstOrDefault();

            double zMin = Math.Min(zTemelBot, stories.Min(s => s.zBot));
            double Y(double z) => y0 + (z - zMin);

            var colXs = new List<double>();
            var wallTopZs = new List<double>();
            var allColHoles = new List<(double lo, double hi)>();
            var segs = new GorunusLineBag();
            var wallRuns = new List<(double x0, double x1, double zBot, double zTop, bool lowest)>();
            var colRuns = new List<(double x0, double x1, double zTop, bool stub, ColumnAxisInfo col)>();
            var slabRuns = new List<(double x0, double x1, double zb, double zt)>();

            for (int si = 0; si < stories.Count; si++)
            {
                var st = stories[si];
                double floorLevelCm = (_model.BuildingBaseKotu + st.p.Floor.ElevationM) * 100.0;
                bool isLowest = st.p.Floor.FloorNo == lowest.p.Floor.FloorNo;
                bool isTopStory = st.p.Floor.FloorNo == topStory.p.Floor.FloorNo;
                int n = Math.Min(st.p.PlanGroup.Count, st.p.RotatedGroup.Count);
                double maxZuStory = double.NaN;
                for (int i = 0; i < n; i++)
                {
                    var beam = st.p.PlanGroup[i].beam;
                    if (beam == null) continue;
                    double zu0 = floorLevelCm + Math.Max(beam.Point1KotCm, beam.Point2KotCm);
                    maxZuStory = double.IsNaN(maxZuStory) ? zu0 : Math.Max(maxZuStory, zu0);
                }
                if (double.IsNaN(maxZuStory)) continue;
                double storySectionY = sectionY;
                double wallHalf = 8.0;
                double envLo = double.MaxValue, envHi = double.MinValue;
                for (int i = 0; i < n; i++)
                {
                    var rw = st.p.RotatedGroup[i].wall;
                    if (rw == null || rw.IsEmpty) continue;
                    var wt = st.p.Trf.Transform(rw);
                    if (wt == null || wt.IsEmpty) continue;
                    var we = wt.EnvelopeInternal;
                    envLo = Math.Min(envLo, we.MinX);
                    envHi = Math.Max(envHi, we.MaxX);
                    wallHalf = Math.Max(wallHalf, (we.MaxY - we.MinY) * 0.5);
                    storySectionY = (we.MinY + we.MaxY) * 0.5;
                }
                int colMark = colRuns.Count;
                if (envLo <= envHi)
                    CollectGorunusKolonCuts(st.p, storySectionY, wallHalf, envLo, envHi, maxZuStory,
                        topStory.p.Floor, nextFloor, colRuns, colXs, allColHoles);
                var storyColHoles = new List<(double lo, double hi)>();
                for (int k = 0; k < colRuns.Count; k++)
                {
                    var cr = colRuns[k];
                    if (k < colMark && (cr.col == null || !HasColumnOnFloor(st.p.Floor, cr.col))) continue;
                    storyColHoles.Add((Math.Min(cr.x0, cr.x1), Math.Max(cr.x0, cr.x1)));
                }

                var pendingDonati = new List<(BeamInfo beam, double x0, double x1, double wallBot, double zu, bool continuesAbove, double slZb)>();
                for (int i = 0; i < n; i++)
                {
                    var planIt = st.p.PlanGroup[i];
                    var rotIt = st.p.RotatedGroup[i];
                    if (planIt.beam == null) continue;
                    double zu = floorLevelCm + Math.Max(planIt.beam.Point1KotCm, planIt.beam.Point2KotCm);
                    double h = planIt.beam.HeightCm > 0 ? planIt.beam.HeightCm : 40.0;
                    double zbWall = zu - h;
                    double wallBot = isLowest ? zTemelTop : zbWall;
                    wallTopZs.Add(zu);

                    if (rotIt.wall == null || rotIt.wall.IsEmpty) continue;
                    var w = st.p.Trf.Transform(rotIt.wall);
                    var spans = SnapWallToKolonIfNear(
                        TrimWallArtikIntervals(CollectSectionXIntervals(w, storySectionY), storyColHoles),
                        storyColHoles);
                    foreach (var sp in spans)
                    {
                        wallRuns.Add((sp.x0, sp.x1, wallBot, zu, isLowest));
                        bool hasSlab = TryPickGorunusSlab(st.p.Floor, planIt.wall, st.p.PlanOffsetX, st.p.PlanOffsetY,
                            floorLevelCm, zTemelBot, zTemelTop, zu, out double slZb, out double slZt);
                        if (sp.x1 - sp.x0 > 1.0 && hasSlab)
                            slabRuns.Add((sp.x0, sp.x1, slZb, slZt));
                        bool continuesAbove = !isTopStory
                            || (nextFloor != null && HasWallNumeroOnFloor(nextFloor, GetBeamNumero(planIt.beam.BeamId)));
                        pendingDonati.Add((planIt.beam, sp.x0, sp.x1, wallBot, zu, continuesAbove, hasSlab ? slZb : double.NaN));
                    }
                }
                var storySpanXs = pendingDonati.Select(p => (p.x0, p.x1)).ToList();
                for (int pi = 0; pi < pendingDonati.Count; pi++)
                {
                    var p = pendingDonati[pi];
                    DrawGorunusPerdeEtiketSolAlt(tr, btr, st.p.Floor, p.beam, p.x0, p.x1, p.wallBot, wallPad, storyColHoles, Y);
                    AddGorunusPerdeDonati(tr, btr, st.p.Floor, p.beam, p.x0, p.x1, p.wallBot, p.zu,
                        isLowest, zTemelBot, p.continuesAbove, p.slZb, si, storyColHoles, storySpanXs, pi, Y);
                }
            }

            colRuns = MergeGorunusColRuns(colRuns);
            ApplyGorunusColumnStoryTops(colRuns, stories, nextFloor);
            allColHoles = colRuns.Select(c => (Math.Min(c.x0, c.x1), Math.Max(c.x0, c.x1))).ToList();
            colXs = colRuns.SelectMany(c => new[] { c.x0, c.x1 }).ToList();

            double xMin = double.MaxValue, xMax = double.MinValue;
            foreach (var w in wallRuns)
            {
                xMin = Math.Min(xMin, w.x0);
                xMax = Math.Max(xMax, w.x1);
            }
            foreach (var c in colRuns)
            {
                xMin = Math.Min(xMin, c.x0);
                xMax = Math.Max(xMax, c.x1);
            }
            if (xMin > xMax) return null;

            var slabDraw = new List<(double x0, double x1, double zb, double zt)>(slabRuns);

            foreach (var sl in slabDraw)
            {
                segs.AddH(sl.x0, sl.x1, Y(sl.zt), LayerDosemeGovde, allColHoles);
                segs.AddH(sl.x0, sl.x1, Y(sl.zb), LayerDosemeGovde, allColHoles);
            }

            foreach (var w in wallRuns)
            {
                bool NearCol(double x) => colRuns.Any(c => Math.Abs(c.x0 - x) < 1.2 || Math.Abs(c.x1 - x) < 1.2);
                if (!NearCol(w.x0))
                    segs.AddV(w.x0, Y(w.zBot), Y(w.zTop), LayerPerde, SlabHolesAtX(w.x0, slabDraw, Y));
                if (!NearCol(w.x1))
                    segs.AddV(w.x1, Y(w.zBot), Y(w.zTop), LayerPerde, SlabHolesAtX(w.x1, slabDraw, Y));
                bool slabAtTop = slabDraw.Any(s => Math.Abs(s.zt - w.zTop) < 2.0 && s.x1 > w.x0 && s.x0 < w.x1);
                if (!slabAtTop)
                    segs.AddH(w.x0, w.x1, Y(w.zTop), LayerPerde, allColHoles);
            }

            foreach (var c in colRuns)
            {
                double yTop = Y(c.zTop);
                if (c.stub)
                {
                    segs.AddV(c.x0, Y(zTemelTop), yTop, LayerPerde, SlabHolesAtX(c.x0, slabDraw, Y));
                    segs.AddV(c.x1, Y(zTemelTop), yTop, LayerPerde, SlabHolesAtX(c.x1, slabDraw, Y));
                    double yStub = yTop + Kolon50GorunusColStubCm;
                    segs.AddV(c.x0, yTop, yStub, LayerPerde, null);
                    segs.AddV(c.x1, yTop, yStub, LayerPerde, null);
                    segs.AddH(c.x0, c.x1, yStub, LayerKesitSiniri, null);
                }
                else
                {
                    segs.AddV(c.x0, Y(zTemelTop), yTop, LayerPerde, SlabHolesAtX(c.x0, slabDraw, Y));
                    segs.AddV(c.x1, Y(zTemelTop), yTop, LayerPerde, SlabHolesAtX(c.x1, slabDraw, Y));
                    segs.AddH(c.x0, c.x1, yTop, LayerPerde, null);
                }
            }

            if (temelSpans.Count > 0)
            {
                double fx0 = xMin - Kolon50GorunusTemelDisTasimaCm;
                double fx1 = xMax + Kolon50GorunusTemelDisTasimaCm;
                double yTb = Y(zTemelBot), yTt = Y(zTemelTop);
                segs.AddH(fx0, fx1, yTb, LayerTemelBeykent, null);
                segs.AddH(fx0, fx1, yTt, LayerTemelBeykent, allColHoles);
                segs.AddV(fx0, yTb, yTt, LayerKesitSiniri, null);
                segs.AddV(fx1, yTb, yTt, LayerKesitSiniri, null);
            }

            segs.Flush(tr, btr);
            Kolon50AccumulatePerdeCopyExtent(new Envelope(xMin, xMax, y0, y0));

            double yMax = Y(stories.Max(s => s.zTop));
            if (wallTopZs.Count > 0) yMax = Math.Max(yMax, Y(wallTopZs.Max()));
            foreach (var sl in slabDraw)
                yMax = Math.Max(yMax, Y(sl.zt));
            foreach (var c in colRuns)
            {
                yMax = Math.Max(yMax, Y(c.zTop));
                if (c.stub) yMax = Math.Max(yMax, Y(c.zTop) + Kolon50GorunusColStubCm);
            }
            Kolon50AccumulatePerdeCopyExtent(new Envelope(
                xMin - 80.0,
                xMax + Kolon50GorunusDikeyOlcuSagaCm + 50.0,
                y0,
                yMax + Kolon50GorunusOlcuUstBoslukCm));

            var kotZs = new List<double>();
            if (temelSpans.Count > 0)
            {
                kotZs.Add(zTemelBot);
                kotZs.Add(zTemelTop);
            }
            kotZs.AddRange(wallTopZs);
            DrawPerdeGorunusKots(tr, btr, xMax + Kolon50GorunusDikeyOlcuSagaCm, Y, kotZs);
            DrawPerdeGorunusOlculer(tr, btr, xMin, xMax, Y, zTemelBot, zTemelTop, wallTopZs, yMax, colXs, temelSpans.Count > 0);

            var sheet = new Envelope(
                xMin - Kolon50GorunusTemelDisTasimaCm,
                xMax + PerdeGorunusSagTasimCm,
                y0,
                yMax + Kolon50GorunusOlcuUstBoslukCm);
            int wallKey = stack[0].MinWallNo;
            if (_kolon50CopyExtentByWallNo != null)
            {
                var wallNos = new HashSet<int> { wallKey };
                foreach (var p in stack)
                {
                    if (p.PlanGroup == null) continue;
                    foreach (var it in p.PlanGroup)
                    {
                        if (it.beam != null) wallNos.Add(GetBeamNumero(it.beam.BeamId));
                    }
                }
                foreach (int wn in wallNos)
                {
                    if (_kolon50CopyExtentByWallNo.TryGetValue(wn, out var ce) && ce != null)
                        sheet = EnvelopeUtil.ExpandToInclude(sheet, ce);
                }
            }
            return sheet;
        }

        private static void PerdeGorunusAntetX(Envelope e, out double innerL, out double innerR, out double outerL, out double outerR)
        {
            double pad = PerdeGorunusAntetPadCm;
            double g = PerdeGorunusAntetGapCm;
            innerL = e.MinX - pad;
            innerR = e.MaxX + pad - PerdeGorunusAntetSagKisaltCm;
            outerL = innerL - g;
            outerR = innerR + g;
        }

        private List<Envelope> SpacePerdeGorunusSheetsToAntetGap(Transaction tr, BlockTableRecord btr, List<(int wallNo, Envelope env)> contents)
        {
            if (tr == null || btr == null || contents == null || contents.Count == 0)
                return new List<Envelope>();
            if (contents.Count < 2)
                return contents.Select(c => c.env).ToList();
            var ordered = contents.OrderBy(c => c.env.MinX).ToList();
            PerdeGorunusAntetX(ordered[0].env, out _, out _, out _, out double prevOuterR);
            var result = new List<Envelope> { ordered[0].env };
            for (int i = 1; i < ordered.Count; i++)
            {
                PerdeGorunusAntetX(ordered[i].env, out _, out _, out double naturalOuterL, out _);
                double dx = (prevOuterR + PerdeGorunusAntetAraCm) - naturalOuterL;
                if (Math.Abs(dx) >= 0.05
                    && _kolon50SheetEntityIds != null
                    && _kolon50SheetEntityIds.TryGetValue(ordered[i].wallNo, out var ids)
                    && ids != null)
                {
                    var disp = Matrix3d.Displacement(new Vector3d(dx, 0, 0));
                    foreach (ObjectId id in ids)
                    {
                        if (id.IsNull || id.IsErased) continue;
                        var ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                        if (ent == null) continue;
                        try { ent.TransformBy(disp); } catch { }
                    }
                }
                var e = ordered[i].env;
                var moved = new Envelope(e.MinX + dx, e.MaxX + dx, e.MinY, e.MaxY);
                result.Add(moved);
                PerdeGorunusAntetX(moved, out _, out _, out _, out prevOuterR);
            }
            return result;
        }

        private void DrawPerdeGorunusAntetFrames(Transaction tr, BlockTableRecord btr, List<Envelope> contents)
        {
            if (tr == null || btr == null || contents == null || contents.Count == 0) return;
            const double txtH = 20.0;
            const double scaleBoxH = 32.0;
            const double scaleBoxW = 240.0;
            double pad = PerdeGorunusAntetPadCm;
            double g = PerdeGorunusAntetGapCm;
            double iy0 = contents.Min(e => e.MinY) - pad - scaleBoxH;
            double iy1 = contents.Max(e => e.MaxY) + pad;
            foreach (var e in contents.OrderBy(c => c.MinX))
            {
                double ix0 = e.MinX - pad;
                double ix1 = e.MaxX + pad - PerdeGorunusAntetSagKisaltCm;
                AppendClosedRect(tr, btr, ix0 - g, iy0 - g, ix1 + g, iy1 + g, LayerSheetSummary50);
                AppendClosedRect(tr, btr, ix0, iy0, ix1, iy1, LayerSheetView);
                double sbx0 = ix1 - scaleBoxW;
                if (sbx0 < ix0 + pad) sbx0 = ix0 + pad;
                double sby1 = iy0 + scaleBoxH;
                AppendClosedRect(tr, btr, sbx0, iy0, ix1, sby1, LayerSheetView);
                DrawBeamLabel(tr, btr, btr.Database,
                    new Point3d((sbx0 + ix1) * 0.5, (iy0 + sby1) * 0.5, 0),
                    "ÖLÇEK: 1/50",
                    txtH, 0.0, LayerYazi, useMiddleCenter: true);
            }
        }

        private static void AppendClosedRect(Transaction tr, BlockTableRecord btr, double x0, double y0, double x1, double y1, string layer)
        {
            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            pl.Layer = layer;
            pl.Closed = true;
            pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
            AppendEntity(tr, btr, pl);
        }

        private Geometry UnionWallsOnly(
            List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> group)
        {
            var parts = new List<Geometry>();
            foreach (var it in group)
            {
                if (it.wall != null && !it.wall.IsEmpty) parts.Add(it.wall);
            }
            if (parts.Count == 0) return null;
            try { return parts.Count == 1 ? parts[0] : CascadedPolygonUnion.Union(parts); }
            catch { return parts[0]; }
        }

        private Geometry UnionPlanGroup(
            List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> group)
        {
            var parts = new List<Geometry>();
            foreach (var it in group)
            {
                if (it.wall != null && !it.wall.IsEmpty) parts.Add(it.wall);
                foreach (var c in it.columns)
                    if (c.geom != null && !c.geom.IsEmpty) parts.Add(c.geom);
            }
            if (parts.Count == 0) return null;
            try
            {
                return parts.Count == 1 ? parts[0] : CascadedPolygonUnion.Union(parts);
            }
            catch
            {
                return parts[0];
            }
        }

        private Geometry RectPoly(double x0, double y0, double x1, double y1, bool keepThin = false)
        {
            var gf = _ntsDrawFactory;
            double xa = Math.Min(x0, x1), xb = Math.Max(x0, x1);
            double ya = Math.Min(y0, y1), yb = Math.Max(y0, y1);
            if (!keepThin)
            {
                if (xb - xa < 1.0) { double m = (xa + xb) * 0.5; xa = m - 2; xb = m + 2; }
                if (yb - ya < 1.0) { double m = (ya + yb) * 0.5; ya = m - 2; yb = m + 2; }
            }
            else
            {
                if (xb - xa < 1e-3) { xa -= 0.03; xb += 0.03; }
                if (yb - ya < 1e-3) { ya -= 0.03; yb += 0.03; }
            }
            return gf.CreatePolygon(gf.CreateLinearRing(new[]
            {
                new Coordinate(xa, ya), new Coordinate(xb, ya), new Coordinate(xb, yb), new Coordinate(xa, yb), new Coordinate(xa, ya)
            }));
        }

        private void CollectTemelSpansAlongWall(
            Geometry planUnion,
            AffineTransformation rot,
            AffineTransformation trf,
            double ox,
            double oy,
            FloorInfo floor,
            List<(double x0, double x1, double z0, double z1)> dest,
            double sectionY = double.NaN)
        {
            var shift = AffineTransformation.TranslationInstance(ox, oy);
            void AddIfHit(Geometry modelPoly, double z0, double z1)
            {
                if (modelPoly == null || modelPoly.IsEmpty) return;
                Geometry g;
                try { g = shift.Transform(modelPoly); }
                catch { return; }
                if (g == null || g.IsEmpty) return;
                Geometry hit;
                try
                {
                    if (!g.Intersects(planUnion)) return;
                    hit = g.Intersection(planUnion);
                }
                catch { hit = g; }
                if (hit == null || hit.IsEmpty) return;
                try
                {
                    hit = rot.Transform(hit);
                    hit = trf.Transform(hit);
                }
                catch { return; }
                if (hit == null || hit.IsEmpty) return;
                if (TrySectionSpanX(hit, sectionY, out double sx0, out double sx1))
                    dest.Add((sx0, sx1, z0, z1));
                else
                {
                    var e = hit.EnvelopeInternal;
                    dest.Add((e.MinX, e.MaxX, z0, z1));
                }
            }

            double baseCm = _model.BuildingBaseKotu * 100.0;
            foreach (var cf in _model.ContinuousFoundations)
            {
                double z0 = baseCm + cf.BottomKotBinaGoreCm;
                double z1 = z0 + (cf.HeightCm > 0 ? cf.HeightCm : 80.0);
                AddIfHit(ContinuousFootprintPoly(cf), z0, z1);
            }
            foreach (var sf in _model.SlabFoundations)
            {
                double z0 = (_model.BuildingBaseKotu + sf.BottomLevelM) * 100.0;
                double h = sf.ThicknessCm > 0 ? sf.ThicknessCm : 40.0;
                AddIfHit(SlabFoundationFootprintPoly(sf), z0, z0 + h);
            }
            foreach (var sf in _model.SingleFootings)
            {
                double z0 = (_model.BuildingBaseKotu + sf.BottomLevelM) * 100.0;
                double h = sf.HeightCm > 0 ? sf.HeightCm : 30.0;
                AddIfHit(SingleFootingModelPoly(sf, floor), z0, z0 + h);
            }
        }

        private void CollectGorunusKolonCuts(
            Kolon50GorunusPending p,
            double storySectionY,
            double wallHalf,
            double wallX0,
            double wallX1,
            double zTop,
            FloorInfo topFloor,
            FloorInfo nextFloor,
            List<(double x0, double x1, double zTop, bool stub, ColumnAxisInfo col)> colRuns,
            List<double> colXs,
            List<(double lo, double hi)> allColHoles)
        {
            var gf = _ntsDrawFactory;
            if (p == null || p.Floor == null || p.Rot == null || p.Trf == null || gf == null) return;
            const double xPadCm = 80.0;
            double xMin = Math.Min(wallX0, wallX1) - xPadCm;
            double xMax = Math.Max(wallX0, wallX1) + xPadCm;
            bool isTopStory = topFloor != null && p.Floor.FloorNo == topFloor.FloorNo;
            var seen = new HashSet<int>();

            void AddCut(Geometry cg, ColumnAxisInfo col)
            {
                if (cg == null || cg.IsEmpty) return;
                if (!TrySectionCutKolonX(cg, storySectionY, wallHalf, out double cx0, out double cx1)) return;
                if (cx1 < xMin || cx0 > xMax) return;
                if (col != null && !seen.Add(col.ColumnNo)) return;
                colXs.Add(cx0);
                colXs.Add(cx1);
                allColHoles.Add((Math.Min(cx0, cx1), Math.Max(cx0, cx1)));
                bool cont = isTopStory && nextFloor != null && col != null && HasColumnOnFloor(nextFloor, col);
                colRuns.Add((cx0, cx1, zTop, cont, col));
            }

            if (_model.Columns != null)
            {
                foreach (var col in _model.Columns)
                {
                    if (col == null || !HasColumnOnFloor(p.Floor, col)) continue;
                    var colGeom = GetColumnPolygonForTable(p.Floor, col, p.PlanOffsetX, p.PlanOffsetY, gf);
                    if (colGeom == null || colGeom.IsEmpty) continue;
                    Geometry cg;
                    try
                    {
                        cg = p.Rot.Transform(colGeom);
                        if (cg == null || cg.IsEmpty) continue;
                        cg = p.Trf.Transform(cg);
                    }
                    catch { continue; }
                    AddCut(cg, col);
                }
            }

            if (p.RotatedGroup == null) return;
            foreach (var it in p.RotatedGroup)
            {
                if (it.columns == null) continue;
                foreach (var c in it.columns)
                {
                    if (c.geom == null || c.geom.IsEmpty) continue;
                    Geometry cg;
                    try { cg = p.Trf.Transform(c.geom); }
                    catch { continue; }
                    AddCut(cg, c.col);
                }
            }
        }

        /// <summary>
        /// Kolon X aralığı: önce perde orta çizgisi, olmazsa perde kalınlığı bandı.
        /// Perde geometrisi kullanılmaz.
        /// </summary>
        private bool TrySectionCutKolonX(Geometry geom, double sectionY, double wallHalf, out double x0, out double x1)
        {
            x0 = x1 = 0;
            if (geom == null || geom.IsEmpty || double.IsNaN(sectionY)) return false;
            var e = geom.EnvelopeInternal;
            double half = Math.Max(wallHalf, 2.0);
            if (sectionY < e.MinY - half - 2.0 || sectionY > e.MaxY + half + 2.0) return false;
            if (TryIntersectX(geom, e, sectionY, 2.0, out x0, out x1)) return true;
            return TryIntersectX(geom, e, sectionY, half, out x0, out x1);
        }

        private bool TryIntersectX(Geometry geom, Envelope e, double sectionY, double halfY, out double x0, out double x1)
        {
            x0 = x1 = 0;
            try
            {
                var band = RectPoly(e.MinX - 2.0, sectionY - halfY, e.MaxX + 2.0, sectionY + halfY, keepThin: halfY < 0.6);
                var h = geom.Intersection(band);
                if (h == null || h.IsEmpty) return false;
                var he = h.EnvelopeInternal;
                x0 = he.MinX;
                x1 = he.MaxX;
                return x1 - x0 > 0.5;
            }
            catch
            {
                return false;
            }
        }

        private List<(double x0, double x1)> CollectSectionXIntervals(Geometry geom, double sectionY)
        {
            var dest = new List<(double x0, double x1)>();
            if (geom == null || geom.IsEmpty) return dest;
            var e = geom.EnvelopeInternal;
            double midY = double.IsNaN(sectionY) ? (e.MinY + e.MaxY) * 0.5 : sectionY;
            Geometry hit = geom;
            try
            {
                var band = RectPoly(e.MinX - 80.0, midY - 0.5, e.MaxX + 80.0, midY + 0.5, keepThin: true);
                var h = geom.Intersection(band);
                if (h != null && !h.IsEmpty) hit = h;
            }
            catch { /* ham geometri */ }
            CollectXParts(hit, dest);
            dest.Sort((a, b) => a.x0.CompareTo(b.x0));
            return dest;
        }

        private static void CollectXParts(Geometry g, List<(double x0, double x1)> dest)
        {
            if (g == null || g.IsEmpty) return;
            int n = g.NumGeometries;
            if (n > 1)
            {
                for (int i = 0; i < n; i++)
                    CollectXParts(g.GetGeometryN(i), dest);
                return;
            }
            var e = g.EnvelopeInternal;
            if (e.MaxX - e.MinX > 0.5)
                dest.Add((e.MinX, e.MaxX));
        }

        private static List<(double x0, double x1)> TrimWallArtikIntervals(
            List<(double x0, double x1)> spans,
            List<(double lo, double hi)> colHoles)
        {
            var remain = new List<(double a, double b)>();
            if (spans == null) return new List<(double x0, double x1)>();
            foreach (var s in spans)
                remain.Add((Math.Min(s.x0, s.x1), Math.Max(s.x0, s.x1)));
            remain = SubtractIntervals(remain, colHoles);
            remain.RemoveAll(p => p.b - p.a <= Kolon50GorunusWallArtikMaxCm);
            return remain.Select(p => (p.a, p.b)).ToList();
        }

        /// <summary>Kolona 15 cm'ye kadar ulaşmayan perde uçlarını kolon yüzüne birleşmiş say.</summary>
        private static List<(double x0, double x1)> SnapWallToKolonIfNear(
            List<(double x0, double x1)> spans,
            List<(double lo, double hi)> colHoles)
        {
            var res = new List<(double x0, double x1)>();
            if (spans == null) return res;
            if (colHoles == null || colHoles.Count == 0) return spans;
            foreach (var s in spans)
            {
                double a = Math.Min(s.x0, s.x1), b = Math.Max(s.x0, s.x1);
                foreach (var h in colHoles)
                {
                    double clo = Math.Min(h.lo, h.hi), chi = Math.Max(h.lo, h.hi);
                    double gapR = clo - b;
                    if (gapR > 0.05 && gapR <= Kolon50GorunusWallArtikMaxCm)
                        b = clo;
                    double gapL = a - chi;
                    if (gapL > 0.05 && gapL <= Kolon50GorunusWallArtikMaxCm)
                        a = chi;
                }
                res.Add((a, b));
            }
            return res;
        }

        /// <summary>
        /// Yalnızca KOLON50 kopya kesit çizimi: döner kolon yüzüne kadar perdeyi uzat (üçgen boşluk dahil),
        /// sonra kolon içini Difference ile kes; kolon ötesindeki ince artıkları at.
        /// Görünüş geometrisine uygulanmaz.
        /// </summary>
        private Geometry FitKolon50CopyWallToColumns(Geometry wall, List<Geometry> columns)
        {
            if (wall == null || wall.IsEmpty) return wall;
            var gf = _ntsDrawFactory;
            if (gf == null) return wall;
            double maxArtik = Kolon50GorunusWallArtikMaxCm;
            Geometry w = wall;
            var we = w.EnvelopeInternal;
            double wy0 = we.MinY, wy1 = we.MaxY;
            double wx0 = we.MinX, wx1 = we.MaxX;
            double wallH = wy1 - wy0;
            if (columns != null)
            {
                foreach (var c in columns)
                {
                    if (c == null || c.IsEmpty) continue;
                    var ce = c.EnvelopeInternal;
                    double yOv = Math.Min(wy1, ce.MaxY) - Math.Max(wy0, ce.MinY);
                    if (wallH > 1.0 && yOv < wallH * 0.35) continue;
                    Geometry colInBand = null;
                    try
                    {
                        var band = RectPoly(ce.MinX - 80.0, wy0, ce.MaxX + 80.0, wy1, keepThin: true);
                        colInBand = c.Intersection(band);
                    }
                    catch { continue; }
                    if (colInBand == null || colInBand.IsEmpty) continue;
                    var be = colInBand.EnvelopeInternal;
                    double cLo = be.MinX, cHi = be.MaxX;
                    if (wx0 < cLo - 0.5 && wx1 > cHi + 0.5) continue;
                    double d = maxArtik + 1.0;
                    try { d = w.Distance(c); } catch { }
                    double gapL = wx0 - cHi;
                    double gapR = cLo - wx1;
                    bool nearLeft = (d <= maxArtik) || (gapL > 0.05 && gapL <= maxArtik) || (gapL <= 0.05 && gapL >= -maxArtik);
                    bool nearRight = (d <= maxArtik) || (gapR > 0.05 && gapR <= maxArtik) || (gapR <= 0.05 && gapR >= -maxArtik);
                    double wallCx = (wx0 + wx1) * 0.5;
                    double colCx = (cLo + cHi) * 0.5;
                    try
                    {
                        if (colCx <= wallCx && nearLeft && wx0 > cLo + 0.05)
                        {
                            w = w.Union(RectPoly(cLo, wy0, wx0, wy1, keepThin: true));
                            wx0 = cLo;
                        }
                        else if (colCx > wallCx && nearRight && cHi > wx1 + 0.05)
                        {
                            w = w.Union(RectPoly(wx1, wy0, cHi, wy1, keepThin: true));
                            wx1 = cHi;
                        }
                    }
                    catch { }
                }
            }

            Geometry colU = null;
            if (columns != null)
            {
                var parts = new List<Geometry>();
                foreach (var c in columns)
                    if (c != null && !c.IsEmpty) parts.Add(c);
                if (parts.Count == 1) colU = parts[0];
                else if (parts.Count > 1)
                {
                    try { colU = CascadedPolygonUnion.Union(parts); } catch { colU = parts[0]; }
                }
            }
            if (colU != null && !colU.IsEmpty)
            {
                try
                {
                    var diff = w.Difference(colU);
                    if (diff != null && !diff.IsEmpty) w = diff;
                }
                catch { }
            }

            var kept = new List<Geometry>();
            CollectCopyPolys(w, kept);
            kept.RemoveAll(p =>
            {
                var e = p.EnvelopeInternal;
                return (e.MaxX - e.MinX) <= maxArtik + 1e-6;
            });
            if (kept.Count == 0) return w;
            if (kept.Count == 1) return kept[0];
            try { return CascadedPolygonUnion.Union(kept); } catch { return kept[0]; }
        }

        private static void CollectCopyPolys(Geometry g, List<Geometry> dest)
        {
            if (g == null || g.IsEmpty || dest == null) return;
            if (g is Polygon) { dest.Add(g); return; }
            var gc = g as GeometryCollection;
            if (gc == null) return;
            for (int i = 0; i < gc.NumGeometries; i++)
                CollectCopyPolys(gc.GetGeometryN(i), dest);
        }

        private void ApplyGorunusColumnStoryTops(
            List<(double x0, double x1, double zTop, bool stub, ColumnAxisInfo col)> colRuns,
            List<(Kolon50GorunusPending p, double zBot, double zTop, Geometry planUnion, Geometry wallUnion)> stories,
            FloorInfo nextFloor)
        {
            if (colRuns == null || stories == null) return;
            for (int i = 0; i < colRuns.Count; i++)
            {
                var c = colRuns[i];
                if (c.col == null) continue;
                double zt = double.NaN;
                foreach (var st in stories)
                {
                    if (st.p?.Floor == null || st.p.PlanGroup == null) continue;
                    if (!HasColumnOnFloor(st.p.Floor, c.col)) continue;
                    if (TryGetGorunusColumnTopCm(st.p.Floor, c.col, out double colTop))
                        zt = double.IsNaN(zt) ? colTop : Math.Max(zt, colTop);
                    else
                    {
                        double wallZu = double.NaN;
                        foreach (var it in st.p.PlanGroup)
                        {
                            if (it.beam == null || it.columns == null) continue;
                            if (!it.columns.Any(x => x.col != null && x.col.ColumnNo == c.col.ColumnNo))
                                continue;
                            double floorLevelCm = (_model.BuildingBaseKotu + st.p.Floor.ElevationM) * 100.0;
                            double zu = floorLevelCm + Math.Max(it.beam.Point1KotCm, it.beam.Point2KotCm);
                            wallZu = double.IsNaN(wallZu) ? zu : Math.Max(wallZu, zu);
                        }
                        if (!double.IsNaN(wallZu))
                            zt = double.IsNaN(zt) ? wallZu : Math.Max(zt, wallZu);
                    }
                }
                if (double.IsNaN(zt)) zt = c.zTop;
                bool stub = false;
                if (nextFloor != null && HasColumnOnFloor(nextFloor, c.col))
                {
                    if (TryGetGorunusColumnTopCm(nextFloor, c.col, out double nextTop))
                        stub = nextTop > zt + 2.0;
                    else
                        stub = true;
                }
                colRuns[i] = (c.x0, c.x1, zt, stub, c.col);
            }
        }

        private bool TryGetGorunusColumnTopCm(FloorInfo floor, ColumnAxisInfo col, out double topCm)
        {
            topCm = 0;
            if (floor == null || col == null) return false;
            double baseKotuM = _model.BuildingBaseKotu;
            if (col.ColumnType == 3)
            {
                int posId = ResolvePolygonPositionSectionId(floor.FloorNo, col.ColumnNo);
                if (posId > 0 && _model.PolygonColumnKotMFromBinaTabaniByPositionId.TryGetValue(posId, out var pk))
                {
                    topCm = (pk.ustM + baseKotuM) * 100.0;
                    return true;
                }
                return false;
            }
            int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
            if (sectionId > 0 && _model.ColumnKotMFromBinaTabaniBySectionId.TryGetValue(sectionId, out var kotM))
            {
                topCm = (kotM.ustM + baseKotuM) * 100.0;
                return true;
            }
            return false;
        }

        private static List<(double x0, double x1, double zTop, bool stub, ColumnAxisInfo col)> MergeGorunusColRuns(
            List<(double x0, double x1, double zTop, bool stub, ColumnAxisInfo col)> runs)
        {
            var merged = new List<(double x0, double x1, double zTop, bool stub, ColumnAxisInfo col)>();
            if (runs == null) return merged;
            foreach (var r in runs.OrderBy(x => Math.Min(x.x0, x.x1)))
            {
                double a = Math.Min(r.x0, r.x1), b = Math.Max(r.x0, r.x1);
                int idx = merged.FindIndex(m =>
                    (m.col != null && r.col != null && m.col.ColumnNo == r.col.ColumnNo)
                    || (Math.Abs(m.x0 - a) < 1.5 && Math.Abs(m.x1 - b) < 1.5));
                if (idx < 0)
                    merged.Add((a, b, r.zTop, r.stub, r.col));
                else
                {
                    var m = merged[idx];
                    merged[idx] = (m.x0, m.x1, Math.Max(m.zTop, r.zTop), m.stub || r.stub, m.col ?? r.col);
                }
            }
            return merged;
        }

        private static List<(double a, double b)> SubtractIntervals(
            List<(double a, double b)> remain,
            List<(double lo, double hi)> holes)
        {
            if (remain == null) return new List<(double a, double b)>();
            if (holes == null || holes.Count == 0) return remain;
            foreach (var h in holes)
            {
                double lo = Math.Min(h.lo, h.hi), hi = Math.Max(h.lo, h.hi);
                var next = new List<(double a, double b)>();
                foreach (var r in remain)
                {
                    if (hi <= r.a || lo >= r.b) { next.Add(r); continue; }
                    if (lo > r.a + 0.05) next.Add((r.a, Math.Min(lo, r.b)));
                    if (hi < r.b - 0.05) next.Add((Math.Max(hi, r.a), r.b));
                }
                remain = next;
            }
            remain.RemoveAll(p => p.b - p.a < 0.2);
            return remain;
        }

        private bool TrySectionSpanX(Geometry geom, double sectionY, out double x0, out double x1)
        {
            x0 = x1 = 0;
            if (geom == null || geom.IsEmpty) return false;
            var e = geom.EnvelopeInternal;
            double midY = double.IsNaN(sectionY) ? (e.MinY + e.MaxY) * 0.5 : sectionY;
            Geometry hit = geom;
            try
            {
                var band = RectPoly(e.MinX - 80.0, midY - 0.04, e.MaxX + 80.0, midY + 0.04, keepThin: true);
                var h = geom.Intersection(band);
                if (h != null && !h.IsEmpty) hit = h;
                else
                {
                    x0 = e.MinX;
                    x1 = e.MaxX;
                    return x1 - x0 > 0.5;
                }
            }
            catch
            {
                x0 = e.MinX;
                x1 = e.MaxX;
                return x1 - x0 > 0.5;
            }
            var he = hit.EnvelopeInternal;
            x0 = he.MinX;
            x1 = he.MaxX;
            return x1 - x0 > 0.5;
        }

        private static List<(double lo, double hi)> SlabHolesAtX(
            double x,
            List<(double x0, double x1, double zb, double zt)> slabs,
            Func<double, double> Y)
        {
            var holes = new List<(double lo, double hi)>();
            if (slabs == null) return holes;
            foreach (var s in slabs)
            {
                double lo = Math.Min(s.x0, s.x1), hi = Math.Max(s.x0, s.x1);
                if (x < lo - 2.0 || x > hi + 2.0) continue;
                holes.Add((Y(s.zb), Y(s.zt)));
            }
            return holes;
        }

        private bool TryPickGorunusSlab(
            FloorInfo floor,
            Geometry wallGeom,
            double ox,
            double oy,
            double floorLevelCm,
            double zTemelBot,
            double zTemelTop,
            double zWallTop,
            out double zb,
            out double zt)
        {
            zb = zt = 0;
            if (wallGeom == null || wallGeom.IsEmpty) return false;
            Geometry wallTouch = wallGeom;
            try
            {
                var buf = wallGeom.Buffer(Kolon50GorunusSlabTouchTolCm);
                if (buf != null && !buf.IsEmpty) wallTouch = buf;
            }
            catch { /* ham perde */ }

            var bands = new List<(double zb, double zt)>();
            var shift = AffineTransformation.TranslationInstance(ox, oy);
            foreach (var slab in _model.Slabs)
            {
                if (_model.StairSlabIds != null && _model.StairSlabIds.Contains(slab.SlabId)) continue;
                if (GetSlabFloorNo(slab.SlabId) != floor.FloorNo) continue;
                var poly = SlabFootprintPoly(slab);
                if (poly == null || poly.IsEmpty) continue;
                Geometry g;
                try { g = shift.Transform(poly); }
                catch { continue; }
                if (g == null || g.IsEmpty) continue;
                bool touches = false;
                try { touches = g.Intersects(wallTouch) || g.Distance(wallGeom) <= Kolon50GorunusSlabTouchTolCm; }
                catch { continue; }
                if (!touches) continue;
                double top = floorLevelCm + slab.OffsetFromFloorCm;
                double th = slab.ThicknessCm > 0 ? slab.ThicknessCm : 15.0;
                double bot = top - th;
                if (top <= zTemelTop + 1.0) continue;
                if (bot < zTemelTop && top > zTemelBot) continue;
                if (Math.Abs(top - zWallTop) > 2.0) continue;
                bands.Add((bot, top));
            }
            if (bands.Count == 0) return false;
            double bestTh = -1.0;
            foreach (var b in bands)
            {
                double th = b.zt - b.zb;
                if (th > bestTh)
                {
                    bestTh = th;
                    zb = b.zb;
                    zt = b.zt;
                }
            }
            return bestTh > 0;
        }

        private sealed class GorunusLineBag
        {
            private readonly HashSet<string> _keys = new HashSet<string>(StringComparer.Ordinal);
            private readonly List<(double ax, double ay, double bx, double by, string layer)> _segs =
                new List<(double, double, double, double, string)>();

            public void AddH(double x0, double x1, double y, string layer, List<(double lo, double hi)> xHoles)
            {
                foreach (var (a, b) in Subtract(Math.Min(x0, x1), Math.Max(x0, x1), xHoles))
                    AddRaw(a, y, b, y, layer);
            }

            public void AddV(double x, double y0, double y1, string layer, List<(double lo, double hi)> yHoles)
            {
                foreach (var (a, b) in Subtract(Math.Min(y0, y1), Math.Max(y0, y1), yHoles))
                    AddRaw(x, a, x, b, layer);
            }

            public void Flush(Transaction tr, BlockTableRecord btr)
            {
                foreach (var s in MergeCollinear(_segs))
                {
                    AppendEntity(tr, btr, new Line(new Point3d(s.ax, s.ay, 0), new Point3d(s.bx, s.by, 0))
                    {
                        Layer = s.layer
                    });
                }
            }

            private static List<(double ax, double ay, double bx, double by, string layer)> MergeCollinear(
                List<(double ax, double ay, double bx, double by, string layer)> segs)
            {
                var h = new Dictionary<string, List<(double a, double b)>>(StringComparer.Ordinal);
                var v = new Dictionary<string, List<(double a, double b)>>(StringComparer.Ordinal);
                foreach (var s in segs)
                {
                    string layer = s.layer ?? "";
                    if (Math.Abs(s.ay - s.by) < 0.4)
                    {
                        string key = layer + "|" + Math.Round((s.ay + s.by) * 0.5, 1).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                        if (!h.TryGetValue(key, out var list)) { list = new List<(double, double)>(); h[key] = list; }
                        list.Add((Math.Min(s.ax, s.bx), Math.Max(s.ax, s.bx)));
                    }
                    else if (Math.Abs(s.ax - s.bx) < 0.4)
                    {
                        string key = layer + "|" + Math.Round((s.ax + s.bx) * 0.5, 1).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                        if (!v.TryGetValue(key, out var list)) { list = new List<(double, double)>(); v[key] = list; }
                        list.Add((Math.Min(s.ay, s.by), Math.Max(s.ay, s.by)));
                    }
                }

                var outSegs = new List<(double ax, double ay, double bx, double by, string layer)>();
                foreach (var kv in h)
                {
                    int bar = kv.Key.LastIndexOf('|');
                    string layer = bar >= 0 ? kv.Key.Substring(0, bar) : kv.Key;
                    double y = double.Parse(kv.Key.Substring(bar + 1), System.Globalization.CultureInfo.InvariantCulture);
                    foreach (var iv in UnionIntervals(kv.Value))
                        outSegs.Add((iv.a, y, iv.b, y, layer));
                }
                foreach (var kv in v)
                {
                    int bar = kv.Key.LastIndexOf('|');
                    string layer = bar >= 0 ? kv.Key.Substring(0, bar) : kv.Key;
                    double x = double.Parse(kv.Key.Substring(bar + 1), System.Globalization.CultureInfo.InvariantCulture);
                    foreach (var iv in UnionIntervals(kv.Value))
                        outSegs.Add((x, iv.a, x, iv.b, layer));
                }
                return outSegs;
            }

            private static List<(double a, double b)> UnionIntervals(List<(double a, double b)> src)
            {
                var list = src.Select(p => (Math.Min(p.a, p.b), Math.Max(p.a, p.b))).OrderBy(p => p.Item1).ToList();
                var res = new List<(double a, double b)>();
                foreach (var p in list)
                {
                    if (res.Count == 0 || p.Item1 > res[res.Count - 1].b + 0.5)
                        res.Add((p.Item1, p.Item2));
                    else
                    {
                        var last = res[res.Count - 1];
                        res[res.Count - 1] = (last.a, Math.Max(last.b, p.Item2));
                    }
                }
                return res;
            }

            private void AddRaw(double ax, double ay, double bx, double by, string layer)
            {
                if (Math.Abs(ax - bx) < 0.15 && Math.Abs(ay - by) < 0.15) return;
                if (ay > by + 1e-9 || (Math.Abs(ay - by) < 1e-9 && ax > bx))
                {
                    double tx = ax, ty = ay;
                    ax = bx; ay = by; bx = tx; by = ty;
                }
                string key = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:F1}|{1:F1}|{2:F1}|{3:F1}|{4}", ax, ay, bx, by, layer ?? "");
                if (!_keys.Add(key)) return;
                _segs.Add((ax, ay, bx, by, layer));
            }

            private static List<(double a, double b)> Subtract(double a, double b, List<(double lo, double hi)> holes)
            {
                return SubtractIntervals(new List<(double a, double b)> { (a, b) }, holes);
            }
        }

        private void DrawPerdeGorunusKots(
            Transaction tr,
            BlockTableRecord btr,
            double xRight,
            Func<double, double> Y,
            List<double> zs)
        {
            if (zs == null || zs.Count == 0) return;
            Database db = btr.Database;
            ObjectId styleId = GetOrCreateYaziBeykentTextStyle(tr, db);
            double apexX = xRight + KesitKotDatumGapFromSectionCm + Kolon50GorunusKotSagaKaydirCm;
            const double rot = 0.0;
            var uniq = new List<double>();
            foreach (double z in zs.OrderBy(v => v))
            {
                if (uniq.Count == 0 || Math.Abs(z - uniq[uniq.Count - 1]) > 2.0)
                    uniq.Add(z);
            }
            foreach (double z in uniq)
            {
                double y = Y(z);
                DrawKesitKotClassicSymbol(tr, btr, apexX, y, rot, 1.0);
                double lxText = KesitKotTriHalfWidthCm;
                double lyText = KesitKotTriHeightCm + KesitKotTextAboveExtensionCm;
                double textX = apexX + lxText;
                double textY = y + lyText;
                AppendKesitKotElevationDbText(tr, btr, db, styleId, FormatKesitKotElevationString(z), textX, textY, rot, KesitKotTextHeightCm);
            }
        }

        private void DrawPerdeGorunusOlculer(
            Transaction tr,
            BlockTableRecord btr,
            double xMin,
            double xMax,
            Func<double, double> Y,
            double zTemelBot,
            double zTemelTop,
            List<double> storyTops,
            double yMax,
            List<double> colXs,
            bool hasTemel,
            IEnumerable<double> extraZsRight = null,
            IEnumerable<double> extraZsLeft = null,
            double? xVertLeft = null,
            double? xVertRight = null,
            IEnumerable<double> extraZsEtriye = null,
            IEnumerable<(double zLo, double zHi, int sCm, int diaMm)> etriyeBolgeler = null)
        {
            ObjectId dimId = GetOrCreatePlanOlcuDimStyle(tr, btr.Database, 10.0, 1.0, PlanOlcuDonatiDimStyleName);
            void Dim(Point3d a, Point3d b, Point3d linePt)
            {
                var dim = new AlignedDimension(a, b, linePt, "", dimId)
                {
                    Layer = LayerOlcu,
                    LineWeight = LineWeight.LineWeight020
                };
                try { dim.DimfxlenOn = true; } catch { }
                try { dim.Dimfxlen = Kolon50GorunusCiftOlcuAraCm; } catch { }
                AppendEntity(tr, btr, dim);
            }

            double yTop = yMax;
            var xs = new List<double> { xMin, xMax };
            if (colXs != null) xs.AddRange(colXs);
            xs.Sort();
            var uniq = new List<double>();
            foreach (double x in xs)
            {
                if (uniq.Count == 0 || Math.Abs(x - uniq[uniq.Count - 1]) > 8.0)
                    uniq.Add(x);
            }
            double yDimTop = yTop + Kolon50GorunusOlcuUstBoslukCm;
            for (int i = 0; i < uniq.Count - 1; i++)
            {
                double a = uniq[i], b = uniq[i + 1];
                if (b - a < 8.0) continue;
                Dim(new Point3d(a, yTop, 0), new Point3d(b, yTop, 0), new Point3d((a + b) * 0.5, yDimTop, 0));
            }

            var zChain = new List<double>();
            if (hasTemel) { zChain.Add(zTemelBot); zChain.Add(zTemelTop); }
            if (storyTops != null)
            {
                foreach (double z in storyTops.OrderBy(v => v))
                {
                    if (zChain.Count == 0 || Math.Abs(z - zChain[zChain.Count - 1]) > 2.0)
                        zChain.Add(z);
                }
            }
            void DrawZDims(List<double> zs, double xFace, double xLine)
            {
                if (zs == null) return;
                for (int i = 0; i < zs.Count - 1; i++)
                {
                    double za = zs[i], zb = zs[i + 1];
                    if (zb - za < 2.0) continue;
                    double ya = Y(za), yb = Y(zb);
                    Dim(new Point3d(xFace, ya, 0), new Point3d(xFace, yb, 0), new Point3d(xLine, (ya + yb) * 0.5, 0));
                }
            }

            List<double> MergeKenarZs(IEnumerable<double> extra)
            {
                var merged = new List<double>(zChain);
                if (extra == null) return merged;
                foreach (double z in extra.OrderBy(v => v))
                {
                    bool seen = false;
                    foreach (double v in merged)
                    {
                        if (Math.Abs(v - z) < 2.0) { seen = true; break; }
                    }
                    if (!seen) merged.Add(z);
                }
                merged.Sort();
                return merged;
            }

            double xVR = xVertRight ?? xMax;
            double xVL = xVertLeft ?? xMin;
            double xDim = xVR + Kolon50GorunusDikeyOlcuSagaCm;

            var zEt = new List<double>();
            if (extraZsEtriye != null)
            {
                foreach (double z in extraZsEtriye.OrderBy(v => v))
                {
                    if (zEt.Count == 0 || Math.Abs(z - zEt[zEt.Count - 1]) > 1.5)
                        zEt.Add(z);
                }
            }

            var zRight = MergeKenarZs(extraZsRight);
            bool hasKenarRight = zRight.Count > zChain.Count;

            if (zEt.Count >= 2)
            {
                double xEt = xVR + KolonDuseyEtriyeOlcuKolondanCm;
                DrawZDims(zEt, xVR, xEt);
                if (etriyeBolgeler != null)
                {
                    var bol = etriyeBolgeler as IList<(double zLo, double zHi, int sCm, int diaMm)>
                              ?? etriyeBolgeler.ToList();
                    for (int i = 0; i < zEt.Count - 1; i++)
                    {
                        double za = zEt[i], zb = zEt[i + 1];
                        if (zb - za < 2.0) continue;
                        int sBest = int.MaxValue;
                        int diaBest = 8;
                        foreach (var b in bol)
                        {
                            double lo = Math.Max(za, b.zLo);
                            double hi = Math.Min(zb, b.zHi);
                            if (hi - lo < 1.0) continue;
                            if (b.sCm < sBest)
                            {
                                sBest = b.sCm;
                                diaBest = b.diaMm > 0 ? b.diaMm : diaBest;
                            }
                        }
                        if (sBest >= 100) continue;
                        if (!TryKolonEtriyeAdetAralik(zb - za, sBest, out int adet, out _))
                            continue;
                        string lab = FormatKolonEtriyeOlcuEtiket(adet, diaBest, sBest);
                        DrawBeamLabel(tr, btr, btr.Database,
                            new Point3d(xEt + 16.0, Y((za + zb) * 0.5), 0),
                            lab, 10.0, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                    }
                }
                double x2 = xEt + KolonDuseyEtriyeOlcuAraCm;
                if (hasKenarRight)
                {
                    DrawZDims(zRight, xVR, x2);
                    DrawZDims(zChain, xVR, x2 + Kolon50GorunusCiftOlcuAraCm);
                }
                else
                    DrawZDims(zChain, xVR, x2);
            }
            else
            {
                DrawZDims(zChain, xVR, xDim);
                if (hasKenarRight)
                    DrawZDims(zRight, xVR, xDim - Kolon50GorunusCiftOlcuAraCm);
            }

            var zLeft = MergeKenarZs(extraZsLeft);
            if (zLeft.Count > zChain.Count)
                DrawZDims(zLeft, xVL, xVL - Kolon50GorunusDikeyOlcuSagaCm);
        }

        private static void ResolvePerdeBxBy(
            GprPerdePanelDonati don,
            BeamInfo beam,
            double drawnSpanCm,
            out double lengthCm,
            out double thickCm)
        {
            double bx = don != null && don.BxCm > 1.0 ? don.BxCm : 0;
            double by = don != null && don.ByCm > 1.0 ? don.ByCm : 0;
            double beamW = beam != null && beam.WidthCm > 1.0 ? beam.WidthCm : 0;
            if (bx > 1.0 && by > 1.0)
            {
                thickCm = Math.Min(bx, by);
                lengthCm = Math.Max(bx, by);
            }
            else
            {
                lengthCm = drawnSpanCm > 1.0 ? drawnSpanCm : Math.Max(bx, by);
                thickCm = beamW > 1.0 ? beamW : (bx > 1.0 && bx < 80.0 ? bx : 25.0);
            }
            if (thickCm > 80.0 && beamW > 1.0 && beamW <= 80.0)
                thickCm = beamW;
            if (thickCm < 10.0) thickCm = 25.0;
            if (lengthCm < 20.0) lengthCm = drawnSpanCm;
        }

        /// <summary>
        /// TS 500 9.1.2.1 nervürlü düz kenetlenme (Konum II):
        /// ℓb = 0,12·φ·fyd/fctd ≥ 20φ; fyd = fyk/1,15; fctk = 0,35√fck; fctd = fctk/1,50.
        /// Konum I (üzerinde &gt;30 cm taze beton): 1,40× Konum II. Düşey perde/kolon donatısı Konum II.
        /// Örn. B420C C30 φ16 → 549 mm ≈ 55 cm (eski 40φ tablosu değil).
        /// </summary>
        private static double Ts500KenetlenmeLbCm(int diaMm, double fck = 30.0, double fyk = 420.0, bool konumI = false)
        {
            if (diaMm < 6) diaMm = 8;
            if (fck < 16.0) fck = 30.0;
            if (fyk < 200.0) fyk = 420.0;
            double fyd = fyk / 1.15;
            double fctd = (0.35 * Math.Sqrt(fck)) / 1.50;
            if (fctd < 0.20) fctd = 0.20;
            double lbMm = 0.12 * diaMm * fyd / fctd;
            if (konumI) lbMm *= 1.40;
            double minMm = 20.0 * diaMm;
            if (lbMm < minMm) lbMm = minMm;
            return Math.Max(1.0, Math.Round(lbMm / 10.0));
        }

        private static double CeilTo5Cm(double cm)
        {
            if (cm <= 0) return 5.0;
            return Math.Ceiling(cm / 5.0 - 1e-9) * 5.0;
        }

        private static double FloorTo5Cm(double cm)
        {
            if (cm <= 0) return 0.0;
            return Math.Floor(cm / 5.0 + 1e-9) * 5.0;
        }

        private bool TryFindGprPerdePanel(FloorInfo floor, int wallNo, out GprPerdePanelDonati don)
        {
            don = null;
            if (_gprPerdePanelDonati == null || _gprPerdePanelDonati.Count == 0) return false;
            var floors = new List<int>();
            if (floor != null) floors.Add(floor.FloorNo);
            floors.Add(0);
            int idx = floor != null && _model.Floors != null ? _model.Floors.IndexOf(floor) : -1;
            if (idx >= 0) { floors.Add(idx + 1); floors.Add(idx); }
            foreach (int f in floors)
            {
                if (f < 0) continue;
                if (_gprPerdePanelDonati.TryGetValue(GprPerdePanelDonatiParser.Key(f, wallNo), out don) && don != null && don.BarCount > 0)
                    return true;
            }
            foreach (var kv in _gprPerdePanelDonati)
            {
                if (kv.Value != null && kv.Value.WallNo == wallNo && kv.Value.BarCount > 0)
                {
                    don = kv.Value;
                    return true;
                }
            }
            return false;
        }

        private bool HasWallNumeroOnFloor(FloorInfo floor, int wallNo)
        {
            if (floor == null || _model.Beams == null || wallNo <= 0) return false;
            foreach (var b in _model.Beams)
            {
                if (b == null || b.IsWallFlag != 1) continue;
                if (GetBeamFloorNo(b.BeamId) != floor.FloorNo) continue;
                if (GetBeamNumero(b.BeamId) == wallNo) return true;
            }
            return false;
        }

        private void AppendDonatiPline(Transaction tr, BlockTableRecord btr, Point2d[] pts, double[] bulgeAt, string layer = null)
        {
            if (pts == null || pts.Length < 2) return;
            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            if (!string.IsNullOrEmpty(_kolonKesitLayerOverride))
                layer = _kolonKesitLayerOverride;
            pl.Layer = string.IsNullOrEmpty(layer) ? LayerDonatiGovde : layer;
            pl.Closed = false;
            for (int i = 0; i < pts.Length; i++)
            {
                double bul = bulgeAt != null && i < bulgeAt.Length ? bulgeAt[i] : 0.0;
                pl.AddVertexAt(i, pts[i], bul, 0, 0);
            }
            AppendEntity(tr, btr, pl);
        }

        private static bool TryColAtWallEnd(
            double wallEnd,
            bool leftEnd,
            List<(double lo, double hi)> holes,
            out double colLo,
            out double colHi)
        {
            colLo = colHi = 0;
            if (holes == null) return false;
            foreach (var h in holes)
            {
                double lo = Math.Min(h.lo, h.hi);
                double hi = Math.Max(h.lo, h.hi);
                if (leftEnd && Math.Abs(hi - wallEnd) < 5.0)
                {
                    colLo = lo;
                    colHi = hi;
                    return hi - lo > 2.0;
                }
                if (!leftEnd && Math.Abs(lo - wallEnd) < 5.0)
                {
                    colLo = lo;
                    colHi = hi;
                    return hi - lo > 2.0;
                }
            }
            return false;
        }

        private static bool YatayContinues(
            double wallEnd,
            bool lookingRight,
            List<(double x0, double x1)> spans,
            List<(double lo, double hi)> holes)
        {
            if (spans == null) return false;
            if (TryColAtWallEnd(wallEnd, leftEnd: !lookingRight, holes, out double colLo, out double colHi))
            {
                double otherFace = lookingRight ? colHi : colLo;
                foreach (var s in spans)
                {
                    if (lookingRight && Math.Abs(s.x0 - otherFace) < 6.0 && s.x1 > otherFace + 15.0)
                        return true;
                    if (!lookingRight && Math.Abs(s.x1 - otherFace) < 6.0 && s.x0 < otherFace - 15.0)
                        return true;
                }
            }
            foreach (var s in spans)
            {
                if (lookingRight && s.x0 > wallEnd - 1.0 && s.x0 < wallEnd + 10.0 && s.x1 > s.x0 + 15.0)
                    return true;
                if (!lookingRight && s.x1 < wallEnd + 1.0 && s.x1 > wallEnd - 10.0 && s.x0 < s.x1 - 15.0)
                    return true;
            }
            return false;
        }

        private void DrawGorunusPerdeEtiketSolAlt(
            Transaction tr,
            BlockTableRecord btr,
            FloorInfo floor,
            BeamInfo beam,
            double x0,
            double x1,
            double zBot,
            int wallPad,
            List<(double lo, double hi)> storyColHoles,
            Func<double, double> Y)
        {
            if (tr == null || btr == null || beam == null || Y == null) return;
            if (x1 - x0 < 20.0) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            int wallNumero = GetBeamNumero(beam.BeamId);
            string wallNo = wallNumero.ToString("D" + Math.Max(1, wallPad), ci);
            string katEtiketi = floor != null && !string.IsNullOrWhiteSpace(floor.ShortName)
                ? floor.ShortName
                : (floor != null ? floor.FloorNo.ToString(ci) : "");
            string wallText = string.Format(ci, "P{0}{1}", katEtiketi, wallNo);
            const double inset = 8.0;
            double xLab = x0 + inset;
            if (storyColHoles != null)
            {
                foreach (var h in storyColHoles)
                {
                    if (h.hi > x0 - 1.0 && h.lo < x0 + 25.0)
                        xLab = Math.Max(xLab, h.hi + inset);
                }
            }
            if (xLab > x1 - 30.0)
                xLab = x0 + inset;
            DrawBeamLabel(tr, btr, btr.Database, new Point3d(xLab, Y(zBot) + inset, 0),
                wallText, 12.0, 0.0, LayerPerdeYazisi, bottomLeftAligned: true);
        }

        private void AddGorunusPerdeDonati(
            Transaction tr,
            BlockTableRecord btr,
            FloorInfo floor,
            BeamInfo beam,
            double x0,
            double x1,
            double zBot,
            double zTop,
            bool isLowest,
            double zTemelBot,
            bool continuesAbove,
            double slabBotZ,
            int storyIndex,
            List<(double lo, double hi)> storyColHoles,
            List<(double x0, double x1)> storySpans,
            int spanIndex,
            Func<double, double> Y)
        {
            if (beam == null || tr == null || btr == null) return;
            if (_gprPerdePanelDonati == null || _gprPerdePanelDonati.Count == 0) return;
            int wallNo = GetBeamNumero(beam.BeamId);
            int floorNo = floor != null ? floor.FloorNo : GetBeamFloorNo(beam.BeamId);
            int floorFromBeam = GetBeamFloorNo(beam.BeamId);
            GprPerdePanelDonati don;
            if (!_gprPerdePanelDonati.TryGetValue(GprPerdePanelDonatiParser.Key(floorNo, wallNo), out don) || don == null || don.BarCount <= 0)
            {
                if (!_gprPerdePanelDonati.TryGetValue(GprPerdePanelDonatiParser.Key(floorFromBeam, wallNo), out don) || don == null || don.BarCount <= 0)
                {
                    if (!_gprPerdePanelDonati.TryGetValue(GprPerdePanelDonatiParser.Key(0, wallNo), out don) || don == null || don.BarCount <= 0)
                    {
                        if (!TryFindGprPerdePanel(floor, wallNo, out don) || don == null) return;
                    }
                }
            }

            double span = x1 - x0;
            if (span < 20.0) return;
            int nBar = Math.Max(don.BarCount, 2);
            ResolvePerdeBxBy(don, beam, span, out double wallLen, out double wallTh);
            double spacing = Math.Max(5.0, Math.Ceiling(Math.Max(span, wallLen) / (nBar + 1.0) - 1e-9));
            if (spacing > 20.0) spacing = 20.0;
            int dia = don.DiaMm > 0 ? don.DiaMm : 12;
            int layers = don.LayerCount > 0 ? don.LayerCount : 2;
            double fck = _rebarFckMPa > 16.0 ? _rebarFckMPa : 30.0;
            double fyk = _rebarFykMPa > 200.0 ? _rebarFykMPa : 420.0;
            double lb = Ts500KenetlenmeLbCm(dia, fck, fyk);
            // TBDY 2018 7.3.3.1: aynı kesitte >%50 ek → ℓ0 ≥ 1,50 ℓb ve ≥ 300 mm
            double lap = CeilTo5Cm(Math.Max(1.50 * lb, 30.0));
            const double pas = 4.0;
            const double rBend = 2.0;
            const double temelPas = 5.0;
            const double bulge90 = 0.41421356237;
            const double txtH = 10.0;
            const double txtUzak = 5.0;
            const double sasir = 5.0;
            double duseyYaziDy = (zTop - zBot) <= 200.0 + 1e-6 ? 0.0 : 30.0;

            double xBase = x0 + span * 0.75;
            if (xBase < x0 + 5.0 || xBase > x1 - 5.0)
                xBase = (x0 + x1) * 0.5;
            double xWall = xBase + ((storyIndex % 2 == 0) ? sasir : 0.0);
            double xFiliz = xBase + ((storyIndex % 2 == 0) ? 0.0 : sasir);
            double hookDir = 1.0;
            double bulge = -bulge90;
            double hookIn = Math.Max(2.0 * rBend + 1.0, wallTh - 2.0 * pas);
            ObjectId dimId = GetOrCreatePlanOlcuDimStyle(tr, btr.Database, 10.0, 1.0, PlanOlcuDonatiDimStyleName);
            double xLeftBar = Math.Min(xWall, xFiliz);
            double xRightBar = Math.Max(xWall, xFiliz);
            double xDimFiliz = xRightBar + 18.0;
            double xYaziParca = xLeftBar - txtUzak - txtH * 0.5 + 20.0;
            // Şaşırtmalı düşey (xWall = xBase + sasir): gövde boy yazısı 5 cm sağa.
            if (Math.Abs(xWall - xBase) > 1e-6)
                xYaziParca += 5.0;
            double xYaziFiliz = xFiliz - txtUzak - 5.0;
            double xYaziWall = xWall - txtUzak - 5.0;
            bool drawFiliz = isLowest && zBot - zTemelBot > temelPas + 2.0;
            bool combineFiliz = drawFiliz && (zTop - zBot) <= 200.0 + 1e-6;

            double zFilizBot = zTemelBot + temelPas;
            double b = 0.0;
            if (drawFiliz)
            {
                double a = zBot - zFilizBot;
                double bMin = CeilTo5Cm(12.0 * dia / 10.0);
                double lbk = 0.75 * lb;
                b = bMin;
                if (a + b < lbk - 1e-6)
                    b = Math.Max(bMin, CeilTo5Cm(lbk - a));
                b = CeilTo5Cm(b);
            }

            if (drawFiliz && !combineFiliz)
            {
                // TS 500 9.3.1.b: 90° kanca serbest uç ≥ 12φ. 9.1.2.2: kancalı kenetlenme ℓbk = 0,75 ℓb.
                double zFilizTop = zBot + lap;
                double yFilizBot = Y(zFilizBot);
                double yFilizR = Y(zFilizBot + rBend);
                const double k90 = 0.41421356237;
                AppendDonatiPline(tr, btr, new[]
                {
                    new Point2d(xFiliz + hookDir * b, yFilizBot),
                    new Point2d(xFiliz + hookDir * rBend, yFilizBot),
                    new Point2d(xFiliz, yFilizR),
                    new Point2d(xFiliz, Y(zFilizTop))
                }, new[] { 0.0, -k90, 0.0, 0.0 });
                double Lfiliz = CeilTo5Cm((zFilizTop - zFilizBot) + b);
                DrawBeamLabel(tr, btr, btr.Database, new Point3d(xYaziFiliz, Y((zFilizBot + zFilizTop) * 0.5) + 5.0 + duseyYaziDy, 0),
                    string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0}x{1}\u00F8{2}/{3:0} L={4:0}", layers, nBar, dia, spacing, Lfiliz),
                    txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                DrawBeamLabel(tr, btr, btr.Database, new Point3d(xFiliz + hookDir * b * 0.5, Y(zFilizBot) + txtUzak + 5.0, 0),
                    b.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                    txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                AppendEntity(tr, btr, new AlignedDimension(
                    new Point3d(xFiliz, Y(zBot), 0), new Point3d(xFiliz, Y(zFilizTop), 0),
                    new Point3d(xDimFiliz, Y(zBot + lap * 0.5), 0), "", dimId)
                { Layer = LayerOlcu, LineWeight = LineWeight.LineWeight020 });
            }

            if (combineFiliz)
            {
                double xBar = xBase;
                double xYaziBar = xBar - txtUzak - 5.0;
                const double k90 = 0.41421356237;
                if (continuesAbove)
                {
                    double zEnd = zTop + lap;
                    AppendDonatiPline(tr, btr, new[]
                    {
                        new Point2d(xBar + hookDir * b, Y(zFilizBot)),
                        new Point2d(xBar + hookDir * rBend, Y(zFilizBot)),
                        new Point2d(xBar, Y(zFilizBot + rBend)),
                        new Point2d(xBar, Y(zEnd))
                    }, new[] { 0.0, -k90, 0.0, 0.0 });
                    double Lbar = CeilTo5Cm((zEnd - zFilizBot) + b);
                    DrawBeamLabel(tr, btr, btr.Database, new Point3d(xYaziBar, Y((zBot + zTop) * 0.5) + 5.0 + duseyYaziDy, 0),
                        string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0}x{1}\u00F8{2}/{3:0} L={4:0}", layers, nBar, dia, spacing, Lbar),
                        txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                    DrawBeamLabel(tr, btr, btr.Database, new Point3d(xBar + hookDir * b * 0.5, Y(zFilizBot) + txtUzak + 5.0, 0),
                        b.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                        txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                }
                else
                {
                    double zHoriz = zTop - pas;
                    double zDown = !double.IsNaN(slabBotZ)
                        ? slabBotZ - 10.0
                        : zHoriz - CeilTo5Cm(12.0 * dia / 10.0);
                    AppendDonatiPline(tr, btr, new[]
                    {
                        new Point2d(xBar + hookDir * b, Y(zFilizBot)),
                        new Point2d(xBar + hookDir * rBend, Y(zFilizBot)),
                        new Point2d(xBar, Y(zFilizBot + rBend)),
                        new Point2d(xBar, Y(zHoriz - rBend)),
                        new Point2d(xBar + hookDir * rBend, Y(zHoriz)),
                        new Point2d(xBar + hookDir * (hookIn - rBend), Y(zHoriz)),
                        new Point2d(xBar + hookDir * hookIn, Y(zHoriz - rBend)),
                        new Point2d(xBar + hookDir * hookIn, Y(zDown))
                    }, new[] { 0.0, -k90, 0.0, bulge, 0, bulge, 0, 0 });
                    double down = zHoriz - zDown;
                    double Lbar = CeilTo5Cm((zHoriz - zFilizBot) + b + hookIn + down);
                    DrawBeamLabel(tr, btr, btr.Database, new Point3d(xYaziBar, Y((zBot + zHoriz) * 0.5) + 5.0 + duseyYaziDy, 0),
                        string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0}x{1}\u00F8{2}/{3:0} L={4:0}", layers, nBar, dia, spacing, Lbar),
                        txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                    DrawBeamLabel(tr, btr, btr.Database, new Point3d(xBar + hookDir * b * 0.5, Y(zFilizBot) + txtUzak + 5.0, 0),
                        b.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                        txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                    DrawBeamLabel(tr, btr, btr.Database, new Point3d(xBar + hookDir * hookIn * 0.5, Y(zHoriz) + txtUzak + txtH * 0.5, 0),
                        hookIn.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                        txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                    DrawBeamLabel(tr, btr, btr.Database, new Point3d(xBar + hookDir * hookIn + txtUzak + txtH * 0.5, Y((zHoriz + zDown) * 0.5), 0),
                        down.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                        txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                }
            }
            else if (continuesAbove)
            {
                double zEnd = zTop + lap;
                AppendDonatiPline(tr, btr, new[]
                {
                    new Point2d(xWall, Y(zBot)),
                    new Point2d(xWall, Y(zEnd))
                }, null);
                double Lwall = CeilTo5Cm(zEnd - zBot);
                DrawBeamLabel(tr, btr, btr.Database, new Point3d(xYaziWall, Y((zBot + zTop) * 0.5) + 5.0 + duseyYaziDy, 0),
                    string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0}x{1}\u00F8{2}/{3:0} L={4:0}", layers, nBar, dia, spacing, Lwall),
                    txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                AppendEntity(tr, btr, new AlignedDimension(
                    new Point3d(xWall, Y(zTop), 0), new Point3d(xWall, Y(zEnd), 0),
                    new Point3d(xDimFiliz, Y(zTop + lap * 0.5), 0), "", dimId)
                { Layer = LayerOlcu, LineWeight = LineWeight.LineWeight020 });
            }
            else
            {
                double zHoriz = zTop - pas;
                double zDown = !double.IsNaN(slabBotZ)
                    ? slabBotZ - 10.0
                    : zHoriz - CeilTo5Cm(12.0 * dia / 10.0);
                AppendDonatiPline(tr, btr, new[]
                {
                    new Point2d(xWall, Y(zBot)),
                    new Point2d(xWall, Y(zHoriz - rBend)),
                    new Point2d(xWall + hookDir * rBend, Y(zHoriz)),
                    new Point2d(xWall + hookDir * (hookIn - rBend), Y(zHoriz)),
                    new Point2d(xWall + hookDir * hookIn, Y(zHoriz - rBend)),
                    new Point2d(xWall + hookDir * hookIn, Y(zDown))
                }, new[] { 0, bulge, 0, bulge, 0, 0 });
                double stem = zHoriz - zBot;
                double down = zHoriz - zDown;
                double Lwall = CeilTo5Cm(stem + hookIn + down);
                DrawBeamLabel(tr, btr, btr.Database, new Point3d(xYaziWall, Y((zBot + zHoriz) * 0.5) + 5.0 + duseyYaziDy, 0),
                    string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0}x{1}\u00F8{2}/{3:0} L={4:0}", layers, nBar, dia, spacing, Lwall),
                    txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                DrawBeamLabel(tr, btr, btr.Database, new Point3d(xYaziParca, Y(zBot + stem * 0.35) + 50.0 + duseyYaziDy, 0),
                    stem.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                    txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                DrawBeamLabel(tr, btr, btr.Database, new Point3d(xWall + hookDir * hookIn * 0.5, Y(zHoriz) + txtUzak + txtH * 0.5, 0),
                    hookIn.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                    txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                DrawBeamLabel(tr, btr, btr.Database, new Point3d(xWall + hookDir * hookIn + txtUzak + txtH * 0.5, Y((zHoriz + zDown) * 0.5), 0),
                    down.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                    txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
            }

            if (don.YatayDiaMm > 0 && don.YataySpacingCm > 0)
            {
                const double yatayAralikCm = 25.0;
                const double yatayAltTabandanCm = 70.0;
                double hWall = Math.Max(zTop - zBot, 1.0);
                int nYatay = Math.Max(1, (int)Math.Round(hWall / don.YataySpacingCm));
                int xOrd = 0;
                if (storySpans != null && storySpans.Count > 0)
                {
                    var ordered = new List<(double x0, double x1)>(storySpans);
                    ordered.Sort((a, b) =>
                    {
                        int c = a.x0.CompareTo(b.x0);
                        return c != 0 ? c : a.x1.CompareTo(b.x1);
                    });
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        if (Math.Abs(ordered[i].x0 - x0) < 1.0 && Math.Abs(ordered[i].x1 - x1) < 1.0)
                        {
                            xOrd = i;
                            break;
                        }
                    }
                }
                double zOff = (xOrd % 2) * sasir;
                double zYatayAlt = zBot + yatayAltTabandanCm + zOff;
                double zYatayUst = zYatayAlt + yatayAralikCm;
                if (zYatayUst < zTop - 5.0 && zYatayAlt > zBot + 2.0)
                {
                    double lbY = Ts500KenetlenmeLbCm(don.YatayDiaMm > 0 ? don.YatayDiaMm : 8, fck, fyk);
                    double embedY = CeilTo5Cm(1.40 * lbY);
                    bool contL = YatayContinues(x0, lookingRight: false, storySpans, storyColHoles);
                    bool contR = YatayContinues(x1, lookingRight: true, storySpans, storyColHoles);
                    double xL = x0 + 8.0;
                    double xR = x1 - 8.0;
                    bool gonyeL = false, gonyeR = false;
                    double embedL = 0, embedR = 0;
                    double xColInnerL = x0, xColInnerR = x1;
                    if (TryColAtWallEnd(x0, leftEnd: true, storyColHoles, out double lLo, out double lHi))
                    {
                        xColInnerL = lHi;
                        double maxIn = xColInnerL - (lLo + pas);
                        double use = Math.Min(embedY, Math.Max(0.0, maxIn));
                        xL = xColInnerL - use;
                        if (xL < lLo + pas) xL = lLo + pas;
                        embedL = xColInnerL - xL;
                        if (!contL && embedL + 0.5 < lbY)
                            gonyeL = true;
                    }
                    if (TryColAtWallEnd(x1, leftEnd: false, storyColHoles, out double rLo, out double rHi))
                    {
                        xColInnerR = rLo;
                        double maxIn = (rHi - pas) - xColInnerR;
                        double use = Math.Min(embedY, Math.Max(0.0, maxIn));
                        xR = xColInnerR + use;
                        if (xR > rHi - pas) xR = rHi - pas;
                        embedR = xR - xColInnerR;
                        if (!contR && embedR + 0.5 < lbY)
                            gonyeR = true;
                    }
                    if (xR - xL > 20.0)
                    {
                        double yA = Y(zYatayAlt);
                        double yB = Y(zYatayUst);
                        double gLen = hookIn;
                        AppendYatayDonatiPline(tr, btr, xL, xR, yA, gonyeL, gonyeR, gLen, hookUp: true, rBend);
                        AppendYatayDonatiPline(tr, btr, xL, xR, yB, gonyeL, gonyeR, gLen, hookUp: false, rBend);
                        double Lh = Math.Round(xR - xL + (gonyeL ? gLen : 0) + (gonyeR ? gLen : 0));
                        string yt = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0}\u00F8{1}/{2} L={3:0}", nYatay, don.YatayDiaMm, don.YataySpacingCm, Lh);
                        double xm = (xL + xR) * 0.5;
                        double yLabA = yA + txtUzak + txtH * 0.5;
                        double yLabB = yB + txtUzak + txtH * 0.5;
                        DrawBeamLabel(tr, btr, btr.Database, new Point3d(xm, yLabA, 0),
                            yt, txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                        DrawBeamLabel(tr, btr, btr.Database, new Point3d(xm, yLabB, 0),
                            yt, txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                        double xColInner = x0;
                        if (TryColAtWallEnd(x0, leftEnd: true, storyColHoles, out double dimLo, out double dimHi))
                            xColInner = dimHi;
                        double xDim15 = xColInner + 50.0;
                        if (xDim15 < xL + 8.0 || xDim15 > xR - 8.0)
                            xDim15 = xL + Math.Min(50.0, Math.Max(20.0, (xR - xL) * 0.25));
                        var dimAralik = new AlignedDimension(
                            new Point3d(xDim15, yA, 0), new Point3d(xDim15, yB, 0),
                            new Point3d(xDim15 - 12.0, (yA + yB) * 0.5, 0), "", dimId)
                        {
                            Layer = LayerOlcu,
                            LineWeight = LineWeight.LineWeight020,
                            DimensionText = don.YataySpacingCm.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                        };
                        AppendEntity(tr, btr, dimAralik);
                        const double gommeOlcuUzakCm = 18.0;
                        bool ustSasir = zOff > 0.5;
                        double yGommeBar = ustSasir ? yB : yA;
                        double yGommeLine = ustSasir ? yB + gommeOlcuUzakCm : yA - gommeOlcuUzakCm;
                        if (embedL > 2.0 && !gonyeL)
                        {
                            AppendEntity(tr, btr, new AlignedDimension(
                                new Point3d(xL, yGommeBar, 0), new Point3d(xColInnerL, yGommeBar, 0),
                                new Point3d((xL + xColInnerL) * 0.5, yGommeLine, 0), "", dimId)
                            { Layer = LayerOlcu, LineWeight = LineWeight.LineWeight020 });
                        }
                        if (embedR > 2.0 && !gonyeR)
                        {
                            AppendEntity(tr, btr, new AlignedDimension(
                                new Point3d(xColInnerR, yGommeBar, 0), new Point3d(xR, yGommeBar, 0),
                                new Point3d((xColInnerR + xR) * 0.5, yGommeLine, 0), "", dimId)
                            { Layer = LayerOlcu, LineWeight = LineWeight.LineWeight020 });
                        }
                    }
                }
            }

            AddGorunusPerdeCiroz(tr, btr, don, x0, x1, zBot, zTop, wallLen, wallTh, Y);
        }

        private void AppendYatayDonatiPline(
            Transaction tr,
            BlockTableRecord btr,
            double xL,
            double xR,
            double y,
            bool gonyeL,
            bool gonyeR,
            double gLen,
            bool hookUp,
            double radius)
        {
            const double k = 0.41421356237;
            double R = Math.Min(Math.Max(radius, 0.5), Math.Max(0.5, gLen * 0.45));
            var pts = new List<Point2d>();
            var bul = new List<double>();
            if (gonyeL)
            {
                if (hookUp)
                {
                    pts.Add(new Point2d(xL, y + gLen));
                    bul.Add(0);
                    pts.Add(new Point2d(xL, y + R));
                    bul.Add(k);
                    pts.Add(new Point2d(xL + R, y));
                    bul.Add(0);
                }
                else
                {
                    pts.Add(new Point2d(xL, y - gLen));
                    bul.Add(0);
                    pts.Add(new Point2d(xL, y - R));
                    bul.Add(-k);
                    pts.Add(new Point2d(xL + R, y));
                    bul.Add(0);
                }
            }
            else
            {
                pts.Add(new Point2d(xL, y));
                bul.Add(0);
            }

            if (gonyeR)
            {
                if (hookUp)
                {
                    pts.Add(new Point2d(xR - R, y));
                    bul.Add(k);
                    pts.Add(new Point2d(xR, y + R));
                    bul.Add(0);
                    pts.Add(new Point2d(xR, y + gLen));
                    bul.Add(0);
                }
                else
                {
                    pts.Add(new Point2d(xR - R, y));
                    bul.Add(-k);
                    pts.Add(new Point2d(xR, y - R));
                    bul.Add(0);
                    pts.Add(new Point2d(xR, y - gLen));
                    bul.Add(0);
                }
            }
            else
            {
                pts.Add(new Point2d(xR, y));
                bul.Add(0);
            }
            AppendDonatiPline(tr, btr, pts.ToArray(), bul.ToArray());
        }

        /// <summary>
        /// TBDY 2018 7.6.4.2 / 7.2.8: gövde çirozu çap = yatay donatı; bir uç 90° diğer uç 135° içe.
        /// TS 500 9.3.1: 90° serbest uç ≥ 12φ. TBDY 7.2.8: 135° ≥ 6φ ve ≥ 80 mm. Pas 3 cm (dıştan sarma).
        /// </summary>
        private void AddGorunusPerdeCiroz(
            Transaction tr,
            BlockTableRecord btr,
            GprPerdePanelDonati don,
            double x0,
            double x1,
            double zBot,
            double zTop,
            double wallLen,
            double wallTh,
            Func<double, double> Y)
        {
            if (tr == null || btr == null || Y == null) return;
            int cirozDia = don != null && don.YatayDiaMm > 0 ? don.YatayDiaMm : 8;
            const double pas = 3.0;
            const double rBend = 2.0;
            const double txtH = 10.0;
            const double txtUzak = 5.0;
            const double asagiCm = 55.0;
            const double k90 = 0.41421356237;
            const double k135 = 0.6681786379192989;
            const double invSqrt2 = 0.7071067811865476;
            // TS 500 9.3.1.b: 90° ≥ 12φ. TBDY 7.2.8: 135° nervürlü ≥ 6φ ve ≥ 80 mm.
            double hook90 = Math.Ceiling(12.0 * cirozDia / 10.0 - 1e-9);
            if (hook90 < 8.0) hook90 = 8.0;
            double hook135 = Math.Max(8.0, Math.Ceiling(6.0 * cirozDia / 10.0 - 1e-9));
            double govde = wallTh - 2.0 * pas;
            double stem = Math.Max(6.0, govde);
            int L = Math.Max(1, (int)Math.Round(govde + hook90 + hook135));
            double hWall = Math.Max(zTop - zBot, 1.0);
            double len = Math.Max(Math.Max(wallLen, 1.0), x1 - x0);
            int nCiroz = Math.Max(1, (int)Math.Round(4.0 * (len / 100.0) * (hWall / 100.0)));

            double xL = x0 + 25.0;
            double xArc = xL + stem - rBend;
            if (xArc + rBend * invSqrt2 > x1 - 8.0)
            {
                xL = Math.Max(x0 + 4.0, x1 - 8.0 - stem);
                xArc = xL + stem - rBend;
            }
            double zUTop = zTop - pas - 10.0 - asagiCm;
            if (hWall <= 200.0 + 1e-6)
                zUTop += 90.0;
            if (zUTop - hook90 < zBot + 4.0) return;

            double yTop = Y(zUTop);
            double yL = Y(zUTop - hook90);
            double yArcEnd = yTop - rBend * (1.0 + invSqrt2);
            double xArcEnd = xArc + rBend * invSqrt2;
            double x135 = xArcEnd - hook135 * invSqrt2;
            double y135 = yArcEnd - hook135 * invSqrt2;
            AppendDonatiPline(tr, btr, new[]
            {
                new Point2d(xL, yL),
                new Point2d(xL, yTop - rBend),
                new Point2d(xL + rBend, yTop),
                new Point2d(xArc, yTop),
                new Point2d(xArcEnd, yArcEnd),
                new Point2d(x135, y135)
            }, new[] { 0.0, -k90, 0.0, -k135, 0.0, 0.0 }, LayerCirozBeykent);

            double xm = (xL + xArcEnd) * 0.5;
            DrawBeamLabel(tr, btr, btr.Database, new Point3d(xm, yTop + txtUzak + txtH * 0.5, 0),
                string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0}\u00F8{1} L={2}", nCiroz, cirozDia, L),
                txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
            DrawBeamLabel(tr, btr, btr.Database, new Point3d(xm, yTop + txtUzak + txtH * 1.5 + 4.0, 0),
                "4 adet/m\u00B2",
                txtH, 0.0, LayerYazi, useMiddleCenter: true);
            DrawBeamLabel(tr, btr, btr.Database, new Point3d(xL - txtUzak - txtH * 0.5, (yTop + yL) * 0.5, 0),
                hook90.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
            double xLab135 = (xArcEnd + x135) * 0.5 + txtUzak + 5.0;
            double yLab135 = (yArcEnd + y135) * 0.5;
            DrawBeamLabel(tr, btr, btr.Database, new Point3d(xLab135, yLab135, 0),
                hook135.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
        }
    }
}
