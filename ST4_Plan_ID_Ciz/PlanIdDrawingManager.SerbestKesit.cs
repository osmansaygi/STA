using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// SERBESTKESIT: kullanıcının plan üzerinde gösterdiği hattan 1/200 şematik bina kesiti.
    /// BINAKESITI'nin dilim/arka plan/çizim düzenini kullanır; hat ve bakış yönü kullanıcıdan gelir.
    /// </summary>
    public sealed partial class PlanIdDrawingManager
    {
        private const double SerbestKesitAksEslesmeTolCm = 1.0;
        /// <summary>Kesit hattının ortası planın aks sınırından en çok bu kadar dışarıda olabilir.</summary>
        private const double SerbestKesitPlanPayiCm = 600.0;

        /// <summary>
        /// Çizimdeki AKS CIZGISI (BEYKENT) çizgilerini modeldeki dik akslarla eşleyip plan ötelemesini bulur
        /// (dünya = model + öteleme). Plan hangi komutla, nereye çizilmiş olursa olsun hat ortasının bulunduğu plan seçilir.
        /// </summary>
        public bool TryResolveSerbestKesitPlanOffset(Database db, Point3d worldA, Point3d worldB,
            out Vector2d offset, out int eslesenX, out int eslesenY)
        {
            offset = new Vector2d(0, 0);
            eslesenX = eslesenY = 0;
            if (db == null || _model == null) return false;

            var xKolon = BuildColumnAxisIds(c => c.AxisXId, _model.AxisX.Select(a => a.Id));
            var yKolon = BuildColumnAxisIds(c => c.AxisYId, _model.AxisY.Select(a => a.Id));
            var modelX = _model.AxisX.Where(a => Math.Abs(a.Slope) < 1e-9 && (xKolon.Count == 0 || xKolon.Contains(a.Id)))
                .Select(a => a.ValueCm).Distinct().OrderBy(v => v).ToList();
            var modelY = _model.AxisY.Where(a => Math.Abs(a.Slope) < 1e-9 && (yKolon.Count == 0 || yKolon.Contains(a.Id)))
                .Select(a => -a.ValueCm).Distinct().OrderBy(v => v).ToList();
            if (modelX.Count == 0 || modelY.Count == 0) return false;

            double midX = (worldA.X + worldB.X) * 0.5;
            double midY = (worldA.Y + worldB.Y) * 0.5;
            var dikeyler = new List<(double V, double Lo, double Hi)>();
            var yataylar = new List<(double V, double Lo, double Hi)>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(RXObjectLine)) continue;
                    var ln = tr.GetObject(id, OpenMode.ForRead) as Line;
                    if (ln == null || !string.Equals(ln.Layer, LayerAks, StringComparison.OrdinalIgnoreCase)) continue;
                    Point3d p = ln.StartPoint, q = ln.EndPoint;
                    double dx = q.X - p.X, dy = q.Y - p.Y;
                    if (Math.Abs(dx) < 0.01 && Math.Abs(dy) > 1.0)
                        dikeyler.Add(((p.X + q.X) * 0.5, Math.Min(p.Y, q.Y), Math.Max(p.Y, q.Y)));
                    else if (Math.Abs(dy) < 0.01 && Math.Abs(dx) > 1.0)
                        yataylar.Add(((p.Y + q.Y) * 0.5, Math.Min(p.X, q.X), Math.Max(p.X, q.X)));
                }
                tr.Commit();
            }

            // Önce hat ortasını kapsayan aks çizgileri (aynı satırdaki/sütundaki planlar); yoksa hepsi.
            List<double> Sec(List<(double V, double Lo, double Hi)> list, double c)
            {
                var yakin = list.Where(l => c >= l.Lo - 1.0 && c <= l.Hi + 1.0).Select(l => l.V).ToList();
                return (yakin.Count > 0 ? yakin : list.Select(l => l.V)).OrderBy(v => v).ToList();
            }

            if (!SerbestKesitEsle1D(modelX, Sec(dikeyler, midY), midX, out double ox, out eslesenX)) return false;
            if (!SerbestKesitEsle1D(modelY, Sec(yataylar, midX), midY, out double oy, out eslesenY)) return false;
            offset = new Vector2d(ox, oy);
            return true;
        }

        private static readonly Autodesk.AutoCAD.Runtime.RXClass RXObjectLine = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Line));

        /// <summary>Öteleme adayı = dünya − model; en çok aksı tutturan, eşitlikte hat ortasını planın içine alan aday.</summary>
        private static bool SerbestKesitEsle1D(List<double> model, List<double> world, double pick, out double off, out int score)
        {
            off = 0;
            score = 0;
            if (model.Count == 0 || world.Count == 0) return false;
            double lo = model[0], hi = model[model.Count - 1];
            var w = world.ToArray();

            bool Var(double v)
            {
                int i = Array.BinarySearch(w, v);
                if (i >= 0) return true;
                i = ~i;
                return (i < w.Length && Math.Abs(w[i] - v) <= SerbestKesitAksEslesmeTolCm)
                    || (i > 0 && Math.Abs(w[i - 1] - v) <= SerbestKesitAksEslesmeTolCm);
            }

            var denenen = new HashSet<long>();
            int bestScore = -1;
            double bestOut = double.MaxValue, bestOff = 0;
            foreach (double wv in w)
            {
                foreach (double mv in model)
                {
                    double c = wv - mv;
                    if (!denenen.Add((long)Math.Round(c * 2.0))) continue;
                    double p = pick - c;
                    double disari = p < lo ? lo - p : (p > hi ? p - hi : 0.0);
                    if (disari > SerbestKesitPlanPayiCm) continue;
                    int s = 0;
                    foreach (double m in model) if (Var(m + c)) s++;
                    if (s > bestScore || (s == bestScore && disari < bestOut))
                    {
                        bestScore = s;
                        bestOut = disari;
                        bestOff = c;
                    }
                }
            }
            if (bestScore < Math.Min(2, model.Count)) return false;
            off = bestOff;
            score = bestScore;
            return true;
        }

        /// <summary>Elle referans için ilk kolonlu X ve Y aksının kesişimi (model plan koordinatı) ve aks adları.</summary>
        public bool TryGetSerbestKesitReferansAks(out string xAd, out string yAd, out Point2d modelPt)
        {
            xAd = yAd = null;
            modelPt = Point2d.Origin;
            if (_model == null || _model.AxisX.Count == 0 || _model.AxisY.Count == 0) return false;
            var xKolon = BuildColumnAxisIds(c => c.AxisXId, _model.AxisX.Select(a => a.Id));
            var yKolon = BuildColumnAxisIds(c => c.AxisYId, _model.AxisY.Select(a => a.Id));
            int i = Enumerable.Range(0, _model.AxisX.Count).FirstOrDefault(k => xKolon.Contains(_model.AxisX[k].Id));
            int j = Enumerable.Range(0, _model.AxisY.Count).FirstOrDefault(k => yKolon.Contains(_model.AxisY[k].Id));
            var ax = _model.AxisX[i];
            var ay = _model.AxisY[j];
            // X aksı: x = V + s·y; Y aksı: y = −(V' + s'·x)  (DrawAxes ile aynı).
            double den = 1.0 + ax.Slope * ay.Slope;
            if (Math.Abs(den) < 1e-9) return false;
            double x = (ax.ValueCm - ax.Slope * ay.ValueCm) / den;
            double y = -(ay.ValueCm + ay.Slope * x);
            modelPt = new Point2d(x, y);
            xAd = _model.GprAxisXLabelByRow.TryGetValue(i + 1, out var gx) && !string.IsNullOrWhiteSpace(gx)
                ? gx.Trim() : (i + 1).ToString(CultureInfo.InvariantCulture);
            yAd = _model.GprAxisYLabelByRow.TryGetValue(j + 1, out var gy) && !string.IsNullOrWhiteSpace(gy)
                ? gy.Trim() : (j < 26 ? ((char)('A' + j)).ToString() : "A" + (j - 25).ToString(CultureInfo.InvariantCulture));
            return true;
        }

        /// <summary>
        /// A→B hattından kesit; bakış yönü hattın solu (A solda görünür). Kesit 1/200 blok olarak
        /// <paramref name="insert"/> noktasına, kesit işareti plana (dünya A/B) çizilir.
        /// </summary>
        public bool DrawSerbestKesit(Database db, Editor ed, Point3d worldA, Point3d worldB, Vector2d planOffset,
            Point3d insert, string harf)
        {
            const string tag = "SERBESTKESIT";
            if (db == null || ed == null || _model?.Floors == null || _model.Floors.Count == 0)
            {
                ed?.WriteMessage("\n{0}: Modelde kat bulunamadi.", tag);
                return false;
            }
            var aModel = new Point2d(worldA.X - planOffset.X, worldA.Y - planOffset.Y);
            var bModel = new Point2d(worldB.X - planOffset.X, worldB.Y - planOffset.Y);
            Vector2d d = bModel - aModel;
            double uzunluk = d.Length;
            if (uzunluk < 50.0)
            {
                ed.WriteMessage("\n{0}: Kesit hatti cok kisa.", tag);
                return false;
            }
            Vector2d u = d.GetNormal();
            Vector2d n = new Vector2d(-u.Y, u.X);
            harf = string.IsNullOrWhiteSpace(harf) ? "A" : harf.Trim().ToUpperInvariant();

            _ntsDrawFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
            int dilimSayisi = 0;
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayers(tr, db);
                    EnsureBinaGorunusLayers(tr, db);

                    var slices = new List<SectionSlice>();
                    CollectBinaKesitSlices(aModel, bModel, aModel, u, slices);
                    var bgFaces = new List<BinaGorunusFace>();
                    CollectSerbestKesitBackgroundFaces(aModel, u, n, uzunluk, bgFaces);
                    dilimSayisi = slices.Count;
                    if (slices.Count == 0)
                    {
                        ed.WriteMessage("\n{0}: Hat hicbir yapi elemanini kesmiyor (plan eslesmesini ve hatti kontrol edin).", tag);
                        return false;
                    }

                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    string blockName = "SERBESTKESIT_" + Guid.NewGuid().ToString("N");
                    var btrDef = new BlockTableRecord { Name = blockName, Origin = Point3d.Origin };
                    ObjectId blockId = bt.Add(btrDef);
                    tr.AddNewlyCreatedDBObject(btrDef, true);

                    if (!TryDrawSerbestKesitIntoBlock(tr, btrDef, slices, bgFaces, out double amin, out double zmin, out double spanU, ed, tag))
                        return false;

                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    AppendEntity(tr, ms, new BlockReference(new Point3d(insert.X, insert.Y, 0), blockId)
                    {
                        ScaleFactors = new Scale3d(BinaKesitScale),
                        Layer = LayerKesit
                    });
                    double cx = insert.X + spanU * BinaKesitScale * 0.5;
                    DrawKesitTitleBelowSchematic(tr, ms, db, harf + "-" + harf + " KESİTİ (1:200)", cx, insert.Y - BinaKesitTitleGapCm);
                    DrawBinaKesitFloorKots(tr, ms, db, insert.X, insert.Y, amin, zmin, spanU);
                    DrawBinaKesitFloorNameLabels(tr, ms, db, insert.X, insert.Y, zmin);

                    DrawSerbestKesitPlanIsareti(tr, ms, db, worldA, worldB, u, n, harf);
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                ed.WriteMessage("\n{0}: Cizilemedi: {1}", tag, ex.Message);
                return false;
            }
            finally { _ntsDrawFactory = null; }

            ed.WriteMessage("\n{0}: {1}-{1} kesiti cizildi (boy {2:0} cm, {3} kesilen dilim; 1/200).", tag, harf, uzunluk, dilimSayisi);
            return true;
        }

        /// <summary>Plan üzerinde kesik hat + iki uçta harfli bakış oku (ST4KESIT işaretiyle aynı sembol).</summary>
        private void DrawSerbestKesitPlanIsareti(Transaction tr, BlockTableRecord btr, Database db,
            Point3d worldA, Point3d worldB, Vector2d u, Vector2d n, string harf)
        {
            var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            ObjectId dashId = lt.Has("DASHED") ? lt["DASHED"] : (lt.Has("Dashed") ? lt["Dashed"] : ObjectId.Null);
            double r = KesitEtiketRadiusCm;
            AppendDashedCutSegment(tr, btr, dashId,
                worldA.X + u.X * r, worldA.Y + u.Y * r,
                worldB.X - u.X * r, worldB.Y - u.Y * r);
            var pA = new Point3d(worldA.X, worldA.Y, 0);
            var pB = new Point3d(worldB.X, worldB.Y, 0);
            DrawKesitEtiketDxfStyle(tr, btr, db, pA, u, n, false, harf);
            DrawKesitEtiketDxfStyle(tr, btr, db, pB, u, n, true, harf);
        }

        /// <summary>BINAKESITI blok çizimiyle aynı: kesilenler taralı kesit, arkadakiler derinlik tonunda görünüş.</summary>
        private bool TryDrawSerbestKesitIntoBlock(
            Transaction tr, BlockTableRecord btrDef,
            List<SectionSlice> slices, List<BinaGorunusFace> bgFaces,
            out double amin, out double zmin, out double spanU,
            Editor ed, string tag)
        {
            var cutPolys = new List<Geometry>();
            amin = slices.Min(s => Math.Min(s.A0, s.A1));
            double amax = slices.Max(s => Math.Max(s.A0, s.A1));
            zmin = slices.Min(s => s.Z0);
            double zmax = slices.Max(s => s.Z1);
            spanU = 0;
            var gf = _ntsDrawFactory;
            foreach (var s in slices)
            {
                double a0 = Math.Min(s.A0, s.A1);
                double a1 = Math.Max(s.A0, s.A1);
                if (a1 - a0 < 6.0)
                {
                    double m = (a0 + a1) * 0.5;
                    a0 = m - 4.0;
                    a1 = m + 4.0;
                }
                double z0 = s.Z0;
                double z1 = s.Z1;
                if (z1 - z0 < 3.0) z1 = z0 + 3.0;
                try
                {
                    var ring = gf.CreateLinearRing(new[]
                    {
                        new Coordinate(a0, z0), new Coordinate(a1, z0),
                        new Coordinate(a1, z1), new Coordinate(a0, z1), new Coordinate(a0, z0)
                    });
                    cutPolys.Add(gf.CreatePolygon(ring));
                }
                catch { /* bozuk dilim */ }
            }

            Geometry cutUnion = null;
            if (cutPolys.Count > 0)
            {
                if (cutPolys.Count > 60)
                {
                    var snapped = new List<Geometry>(cutPolys.Count);
                    foreach (var p in cutPolys)
                    {
                        var r = ReducePrecisionSafe(p, 10.0) ?? p;
                        if (r != null && !r.IsEmpty) snapped.Add(r);
                    }
                    cutPolys = snapped;
                }
                try
                {
                    cutUnion = cutPolys.Count == 1
                        ? BinaGorunusPolygonal(cutPolys[0])
                        : BinaGorunusPolygonal(cutPolys.Count > 60
                            ? TryCascadedPolygonUnionSafe(cutPolys)
                            : CascadedPolygonUnion.Union(cutPolys));
                }
                catch
                {
                    cutUnion = BinaGorunusPolygonal(TryCascadedPolygonUnionSafe(cutPolys));
                }
            }

            var visibleBg = new List<(Geometry Geom, double Depth)>();
            if (bgFaces.Count > 0)
            {
                bgFaces.Sort((a, b) => a.Depth.CompareTo(b.Depth));
                var bands = new List<List<BinaGorunusFace>>();
                foreach (var face in bgFaces)
                {
                    if (bands.Count == 0 || face.Depth > bands[bands.Count - 1][0].Depth + BinaKesitFlushDepthCm)
                        bands.Add(new List<BinaGorunusFace>());
                    bands[bands.Count - 1].Add(face);
                }

                Geometry occluder = cutUnion;
                foreach (var band in bands)
                {
                    var polys = new List<Geometry>(band.Count);
                    double bandDepth = double.MaxValue;
                    foreach (var face in band)
                    {
                        if (face.Poly == null || face.Poly.IsEmpty) continue;
                        polys.Add(face.Poly);
                        if (face.Depth < bandDepth) bandDepth = face.Depth;
                    }
                    Geometry uni = BinaGorunusPolygonal(TryCascadedPolygonUnionSafe(polys));
                    if (uni == null || uni.IsEmpty) continue;

                    Geometry visible = occluder == null ? uni : BinaGorunusDifference(uni, occluder);
                    visible = BinaGorunusPolygonal(visible);
                    if (visible != null && !visible.IsEmpty && visible.Area > 1.0)
                        visibleBg.Add((visible, bandDepth));

                    occluder = occluder == null ? uni : BinaGorunusPolygonal(BinaGorunusUnion(occluder, uni));
                    if (occluder == null) occluder = uni;
                }

                foreach (var part in visibleBg)
                {
                    var e = part.Geom.EnvelopeInternal;
                    if (e.MinX < amin) amin = e.MinX;
                    if (e.MaxX > amax) amax = e.MaxX;
                    if (e.MinY < zmin) zmin = e.MinY;
                    if (e.MaxY > zmax) zmax = e.MaxY;
                }
            }

            if (double.IsInfinity(amin) || double.IsInfinity(zmin) || amax - amin < 1.0 || zmax - zmin < 1.0)
            {
                ed?.WriteMessage("\n{0}: Kesit olcusu gecersiz.", tag);
                return false;
            }

            amin -= 20.0;
            zmin -= 15.0;
            spanU = amax - amin + 20.0;
            double spanZ = zmax - zmin + 30.0;

            double depthMinBg = bgFaces.Count > 0 ? bgFaces.Min(f => f.Depth) : 0.0;
            double depthMaxBg = bgFaces.Count > 0 ? bgFaces.Max(f => f.Depth) : 1.0;
            double depthSpanBg = Math.Max(depthMaxBg - depthMinBg, 1.0);

            var shift = new BinaGorunusShiftFilter(-amin, -zmin);
            var orderedBg = visibleBg
                .Select(p => (p.Geom, p.Depth, Tier: BinaGorunusDepthTier(p.Depth, depthMinBg, depthSpanBg)))
                .OrderByDescending(p => p.Tier)
                .ThenByDescending(p => p.Depth)
                .ToList();
            foreach (var part in orderedBg)
            {
                var drawG = part.Geom.Copy();
                drawG.Apply(shift);
                DrawGeometryRingsAsPolylines(
                    tr, btrDef, drawG, BinaGorunusLayerForTier(part.Tier),
                    addHatch: false,
                    exteriorRingsOnly: false,
                    applySmallTriangleTrim: true);
            }

            if (cutPolys.Count > 0)
            {
                var shiftedCuts = new List<Geometry>(cutPolys.Count);
                foreach (var g in cutPolys)
                {
                    var c = g.Copy();
                    c.Apply(shift);
                    shiftedCuts.Add(c);
                }
                TryDrawKesitSchematicMergedOutline(
                    tr, btrDef, shiftedCuts, slices,
                    isFoundationPlan: false,
                    originX: 0, originY: 0,
                    amin: amin, minZ: zmin, spanZ: spanZ,
                    horizontalAlongX: true, mirrorElevationX: false,
                    kalip50BuildingStackSiniriClip: true,
                    perSliceHatchOnly: true,
                    ansi33HatchPatternScale: 1.0);
            }
            return true;
        }

        /// <summary>
        /// Hattın arkasındaki (sol normal tarafı) elemanlar, BINAKESITI arka planıyla aynı eleman listesi;
        /// izdüşüm eğik hatta U = (p−A)·u, derinlik = (p−A)·n. U, hattın boyuyla [0, L] kırpılır.
        /// </summary>
        private void CollectSerbestKesitBackgroundFaces(Point2d origin, Vector2d u, Vector2d n, double uzunluk, List<BinaGorunusFace> faces)
        {
            double baseCm = _model.BuildingBaseKotu * 100.0;

            void addPlan(Geometry plan, double z0, double z1)
            {
                if (plan == null || plan.IsEmpty) return;
                if (z1 < z0) { double t = z0; z0 = z1; z1 = t; }
                if (z1 - z0 < 0.5) return;
                Geometry solid = PrepareForOverlay(plan);
                if (solid == null || solid.IsEmpty) return;
                for (int i = 0; i < solid.NumGeometries; i++)
                {
                    if (!(solid.GetGeometryN(i) is Polygon part)) continue;
                    AddSerbestKesitBackgroundRect(faces, origin, u, n, uzunluk, part, z0, z1);
                }
            }

            {
                int step = _model.SlabFloorKeyStep > 0 ? _model.SlabFloorKeyStep : 100;
                double refTopCm = _model.HasSubasmanStory
                    ? (_model.BuildingBaseKotu + _model.SubasmanElevationM) * 100.0
                    : _model.BuildingBaseKotu * 100.0;
                foreach (var slab in _model.Slabs)
                {
                    if (slab == null || slab.SlabId <= 0 || slab.SlabId >= step) continue;
                    if (GetSlabFloorNo(slab.SlabId) != 0) continue;
                    if (ShouldOmitSlabFromBinaView(slab)) continue;
                    double th = slab.ThicknessCm > 0 ? slab.ThicknessCm : 15.0;
                    double zt = refTopCm + slab.OffsetFromFloorCm;
                    if (Math.Abs(zt - refTopCm) > 5.0) continue;
                    addPlan(SlabFootprintPoly(slab), zt - th, zt);
                }
            }

            foreach (var floor in _model.Floors)
            {
                int floorNo = floor.FloorNo;
                double floorLevelCm = (_model.BuildingBaseKotu + floor.ElevationM) * 100.0;

                foreach (var slab in _model.Slabs)
                {
                    if (GetSlabFloorNo(slab.SlabId) != floorNo) continue;
                    if (ShouldOmitSlabFromBinaView(slab)) continue;
                    double zt = floorLevelCm + slab.OffsetFromFloorCm;
                    double th = slab.ThicknessCm > 0 ? slab.ThicknessCm : 15.0;
                    addPlan(SlabFootprintPoly(slab), zt - th, zt);
                }

                foreach (var beam in MergeSameIdBeamsOnFloor(floorNo))
                {
                    double zu = floorLevelCm + Math.Max(beam.Point1KotCm, beam.Point2KotCm);
                    double h = beam.HeightCm > 0 ? beam.HeightCm : (beam.IsWallFlag == 1 ? 40.0 : 30.0);
                    addPlan(BeamFootprintPoly(beam), zu - h, zu);
                }

                var extra = GetColumnTableExtraData(floor);
                foreach (var col in _model.Columns)
                {
                    if (!HasColumnOnFloor(floor, col)) continue;
                    if (!extra.TryGetValue(col.ColumnNo, out var ek)) continue;
                    double yuk = ek.yukseklikCm > 1.0 ? ek.yukseklikCm : SectionMinStoryHeightCm;
                    addPlan(GetColumnPolygonForTable(floor, col, 0.0, 0.0, _ntsDrawFactory), ek.altKotCm, ek.altKotCm + yuk);
                }
            }

            FloorInfo footingFloor = _model.Floors[0];
            foreach (var cf in _model.ContinuousFoundations)
            {
                double z0 = baseCm + cf.BottomKotBinaGoreCm;
                double z1 = z0 + (cf.HeightCm > 0 ? cf.HeightCm : 40.0);
                if (cf.AmpatmanWidthCm > 0 && Math.Abs(cf.AmpatmanWidthCm - cf.WidthCm) > 1e-6 &&
                    cf.AmpatmanHeightCm > 0 && cf.AmpatmanHeightCm < (z1 - z0) - 1e-6)
                {
                    var amp = AmpatmanFootprintPoly(cf);
                    if (amp != null && !amp.IsEmpty) addPlan(amp, z0, z0 + cf.AmpatmanHeightCm);
                }
                addPlan(ContinuousFootprintPoly(cf), z0, z1);
                var hat = HatilStripOnContinuousPoly(cf);
                if (hat != null && cf.HatilLabelHeightCm > 0)
                    addPlan(hat, z1, z1 + cf.HatilLabelHeightCm);
            }
            foreach (var sfd in _model.SlabFoundations)
            {
                double z0 = (_model.BuildingBaseKotu + sfd.BottomLevelM) * 100.0;
                addPlan(SlabFoundationFootprintPoly(sfd), z0, z0 + (sfd.ThicknessCm > 0 ? sfd.ThicknessCm : 40.0));
            }
            foreach (var tb in _model.TieBeams)
            {
                double z0 = (_model.BuildingBaseKotu + tb.BottomKotM) * 100.0;
                addPlan(TieBeamFootprintPoly(tb), z0, z0 + (tb.HeightCm > 0 ? tb.HeightCm : 25.0));
            }
            foreach (var sf in _model.SingleFootings)
            {
                double z0 = (_model.BuildingBaseKotu + sf.BottomLevelM) * 100.0;
                addPlan(SingleFootingModelPoly(sf, footingFloor), z0, z0 + (sf.HeightCm > 0 ? sf.HeightCm : 30.0));
            }
        }

        private void AddSerbestKesitBackgroundRect(
            List<BinaGorunusFace> faces, Point2d origin, Vector2d u, Vector2d n, double uzunluk,
            Geometry planPoly, double z0, double z1)
        {
            var coords = planPoly.Coordinates;
            if (coords == null || coords.Length == 0) return;
            double u0 = double.MaxValue, u1 = double.MinValue;
            double dMin = double.MaxValue;
            for (int i = 0; i < coords.Length; i++)
            {
                double px = coords[i].X - origin.X;
                double py = coords[i].Y - origin.Y;
                double pu = px * u.X + py * u.Y;
                double pd = px * n.X + py * n.Y;
                if (pu < u0) u0 = pu;
                if (pu > u1) u1 = pu;
                if (pd < dMin) dMin = pd;
            }
            // Kesit şeridine giren veya hattın önünde kalan eleman arka planda yok.
            if (dMin < BinaKesitCutHalfWidthCm) return;
            u0 = Math.Max(u0, 0.0);
            u1 = Math.Min(u1, uzunluk);
            if (u1 - u0 < 0.5) return;

            Coordinate[] ring =
            {
                new Coordinate(u0, z0), new Coordinate(u1, z0),
                new Coordinate(u1, z1), new Coordinate(u0, z1), new Coordinate(u0, z0)
            };
            try
            {
                var rect = _ntsDrawFactory.CreatePolygon(_ntsDrawFactory.CreateLinearRing(ring));
                if (rect == null || rect.IsEmpty || rect.Area < 1.0) return;
                faces.Add(new BinaGorunusFace { Poly = rect, Depth = dMin });
            }
            catch { /* bozuk halka */ }
        }
    }
}
