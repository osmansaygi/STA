using System;
using System.Collections.Generic;
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
    /// <summary>Bina kesiti yönü: X = yatay kesit hattı (Y sabit), Y = dikey kesit hattı (X sabit).</summary>
    public enum BinaKesitYon
    {
        /// <summary>Kesit hattı X boyunca (Y = const); görünüş derinliği Y.</summary>
        X = 0,
        /// <summary>Kesit hattı Y boyunca (X = const); görünüş derinliği X.</summary>
        Y = 1
    }

    public sealed partial class PlanIdDrawingManager
    {
        private const double BinaKesitScale = 0.25;
        private const double BinaKesitFlushDepthCm = 15.0;
        private const double BinaKesitTitleGapCm = 40.0;
        private const string BinaKesitTitle = "KESIT (1:200)";
        /// <summary>Kesit şeridi: bu tolerans içinde kalan eleman kesilmiş sayılır.</summary>
        private const double BinaKesitCutHalfWidthCm = 8.0;
        /// <summary>Kat döşemesi kesilmiş sayılsın diye gereken en kısa dolu kesişme (cm).</summary>
        private const double BinaKesitStoryHitMinCm = 120.0;
        /// <summary>Kesitin arkasında sayılmak için minimum derinlik payı (cm).</summary>
        private const double BinaKesitBehindEpsCm = 12.0;

        /// <summary>
        /// ST4 modelinden 1/200 bina kesiti. X/Y yönünde merdiven döşemesini kesmeyen hat;
        /// kesilen elemanlar kesit, arkadakiler görünüş (ön/orta/arka ton) gibi.
        /// </summary>
        public bool DrawBinaKesit(Database db, Editor ed, Point3d insert, BinaKesitYon yon)
        {
            if (db == null || ed == null || _model?.Floors == null || _model.Floors.Count == 0)
            {
                ed?.WriteMessage("\nBINAKESITI: Modelde kat bulunamadi.");
                return false;
            }

            _ntsDrawFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayers(tr, db);
                    EnsureBinaGorunusLayers(tr, db);
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    string blockName = "BINAKESITI_" + Guid.NewGuid().ToString("N");
                    while (bt.Has(blockName))
                        blockName = "BINAKESITI_" + Guid.NewGuid().ToString("N");
                    var btrDef = new BlockTableRecord { Name = blockName, Origin = Point3d.Origin };
                    ObjectId blockId = bt.Add(btrDef);
                    tr.AddNewlyCreatedDBObject(btrDef, true);

                    if (!TryDrawBinaKesitIntoBlock(tr, btrDef, yon,
                            out double amin, out double zmin, out double spanU, out _, out _,
                            ed, "BINAKESITI"))
                        return false;

                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    var br = new BlockReference(new Point3d(insert.X, insert.Y, 0), blockId)
                    {
                        ScaleFactors = new Scale3d(BinaKesitScale),
                        Layer = LayerKesit
                    };
                    AppendEntity(tr, ms, br);

                    double cx = insert.X + spanU * BinaKesitScale * 0.5;
                    DrawKesitTitleBelowSchematic(tr, ms, db, BinaKesitTitle, cx, insert.Y - BinaKesitTitleGapCm);
                    DrawBinaKesitFloorKots(tr, ms, db, insert.X, insert.Y, amin, zmin, spanU);
                    DrawBinaKesitFloorNameLabels(tr, ms, db, insert.X, insert.Y, zmin);

                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                ed.WriteMessage("\nBINAKESITI: Cizilemedi: {0}", ex.Message);
                return false;
            }

            ed.WriteMessage(
                "\nBINAKESITI: 1/200 kesit yerlestirildi ({0}; merdiven kacinildi; arka plan gorunus tonu).",
                yon == BinaKesitYon.X ? "X yonu" : "Y yonu");
            return true;
        }

        /// <summary>
        /// Kesit + arka plan geometrisini mevcut blok tanımına çizer (orijin = sol-alt).
        /// KALIP antet üstü şema ve BINAKESITI komutu ortak kullanır.
        /// </summary>
        private bool TryDrawBinaKesitIntoBlock(
            Transaction tr,
            BlockTableRecord btrDef,
            BinaKesitYon yon,
            out double amin,
            out double zmin,
            out double spanU,
            out double spanZ,
            out double labelXBlock,
            Editor ed = null,
            string errPrefix = null)
        {
            amin = zmin = spanU = spanZ = labelXBlock = 0;
            string tag = string.IsNullOrWhiteSpace(errPrefix) ? "BINAKESITI" : errPrefix.Trim();

            if (tr == null || btrDef == null || _model?.Floors == null || _model.Floors.Count == 0)
                return false;
            if (_ntsDrawFactory == null)
                _ntsDrawFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();

            if (!TryFindBinaKesitCut(yon, out double cutPos, out Point2d cutA, out Point2d cutB, out Point2d alongOrig, out Vector2d dirUnit, out double aminHint, out double amaxHint))
            {
                ed?.WriteMessage("\n{0}: Kesit hatti bulunamadi.", tag);
                return false;
            }

            var slices = new List<SectionSlice>();
            try
            {
                CollectBinaKesitSlices(cutA, cutB, alongOrig, dirUnit, slices);
            }
            catch (Exception ex)
            {
                ed?.WriteMessage("\n{0}: Kesit dilimleri toplanamadi: {1}", tag, ex.Message);
                return false;
            }

            var bgFaces = new List<BinaGorunusFace>();
            try
            {
                CollectBinaKesitBackgroundFaces(yon, cutPos, bgFaces);
            }
            catch (Exception ex)
            {
                ed?.WriteMessage("\n{0}: Arka plan toplanamadi: {1}", tag, ex.Message);
                return false;
            }

            if (slices.Count == 0 && bgFaces.Count == 0)
            {
                ed?.WriteMessage("\n{0}: Cizilecek eleman bulunamadi.", tag);
                return false;
            }

            var cutPolys = new List<Geometry>();
            amin = slices.Count > 0 ? slices.Min(s => Math.Min(s.A0, s.A1)) : aminHint;
            double amax = slices.Count > 0 ? slices.Max(s => Math.Max(s.A0, s.A1)) : amaxHint;
            zmin = slices.Count > 0 ? slices.Min(s => s.Z0) : double.MaxValue;
            double zmax = slices.Count > 0 ? slices.Max(s => s.Z1) : double.MinValue;
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
                    Geometry uni = TryCascadedPolygonUnionSafe(polys);
                    uni = BinaGorunusPolygonal(uni);
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
            spanZ = zmax - zmin + 30.0;
            labelXBlock = spanU * 0.5;

            double depthMinBg = bgFaces.Count > 0 ? bgFaces.Min(f => f.Depth) : cutPos;
            double depthMaxBg = bgFaces.Count > 0 ? bgFaces.Max(f => f.Depth) : cutPos + 1.0;
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
                string layer = BinaGorunusLayerForTier(part.Tier);
                DrawGeometryRingsAsPolylines(
                    tr, btrDef, drawG, layer,
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

        private void DrawBinaKesitFloorNameLabels(
            Transaction tr, BlockTableRecord btr, Database db,
            double insertX, double insertY, double zMin, double scale = BinaKesitScale)
        {
            if (_model?.Floors == null || _model.Floors.Count == 0) return;
            if (scale <= 1e-9) scale = BinaKesitScale;
            ObjectId styleId = GetOrCreateYaziBeykentTextStyle(tr, db);
            double xLabel = insertX - 18.0;
            double textH = 14.0;
            var trTr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");

            void placeFloorLabel(string name, double zFloor)
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                double y = insertY + (zFloor - zMin) * scale - 4.0;
                var txt = new DBText
                {
                    Layer = LayerKesitIsmi,
                    Height = textH,
                    TextStyleId = styleId,
                    TextString = name.Trim().ToUpper(trTr),
                    HorizontalMode = TextHorizontalMode.TextRight,
                    VerticalMode = TextVerticalMode.TextBottom,
                    Position = new Point3d(xLabel, y, 0),
                    AlignmentPoint = new Point3d(xLabel, y, 0),
                    Color = Color.FromColorIndex(ColorMethod.ByAci, 8),
                    LineWeight = LineWeight.LineWeight015
                };
                try { txt.AdjustAlignment(db); } catch { /* sürüm */ }
                AppendEntity(tr, btr, txt);
            }

            if (_model.HasSubasmanStory)
            {
                double zSb = (_model.BuildingBaseKotu + _model.SubasmanElevationM) * 100.0;
                placeFloorLabel("S-", zSb);
            }

            foreach (var floor in _model.Floors.OrderBy(f => f.ElevationM))
            {
                string name = !string.IsNullOrWhiteSpace(floor.ShortName)
                    ? floor.ShortName
                    : (!string.IsNullOrWhiteSpace(floor.Name) ? floor.Name : floor.FloorNo.ToString());
                double zFloor = (_model.BuildingBaseKotu + floor.ElevationM) * 100.0;
                placeFloorLabel(name, zFloor);
            }
        }

        private void DrawBinaKesitFloorKots(
            Transaction tr, BlockTableRecord btr, Database db,
            double insertX, double insertY, double uMin, double zMin, double spanU)
        {
            var raw = new List<double> { _model.BuildingBaseKotu * 100.0 };
            if (_model.HasSubasmanStory)
                raw.Add((_model.BuildingBaseKotu + _model.SubasmanElevationM) * 100.0);
            foreach (var floor in _model.Floors)
                raw.Add((_model.BuildingBaseKotu + floor.ElevationM) * 100.0);
            var zsAsc = MergeKesitElevationZsAscending(raw, KesitKotMergeTolCm);
            if (zsAsc == null || zsAsc.Count == 0) return;

            double minSepModel = KesitKotCrowdedSeparationMinCm / BinaKesitScale;
            double[] crowded = BuildKesitKotCrowdedShiftForLowerElevation(
                zsAsc, minSepModel, KesitKotCrowdedShiftLowerCm);
            ObjectId styleId = GetOrCreateYaziBeykentTextStyle(tr, db);
            double xRight = insertX + spanU * BinaKesitScale;
            double xDatumBase = xRight + KesitKotDatumGapFromSectionCm + 35.0;
            const double rot = 0.0;

            for (int zi = zsAsc.Count - 1; zi >= 0; zi--)
            {
                double zElev = zsAsc[zi];
                double extra = zi < crowded.Length ? crowded[zi] : 0.0;
                double apexX = xDatumBase + extra;
                double apexY = insertY + (zElev - zMin) * BinaKesitScale;
                DrawKesitKotClassicSymbol(tr, btr, apexX, apexY, rot, 1.0);
                double lxText = KesitKotTriHalfWidthCm;
                double lyText = KesitKotTriHeightCm + KesitKotTextAboveExtensionCm;
                AppendKesitKotElevationDbText(
                    tr, btr, db, styleId, FormatKesitKotElevationString(zElev),
                    apexX + lxText, apexY + lyText, rot, KesitKotTextHeightCm);
            }
        }

        /// <summary>Tüm katlarda en çok döşemeyi kesen hat; merdiven, paralel kiriş ve kolon cezaları durur.</summary>
        private sealed class BinaKesitCutInputs
        {
            public Geometry CutSearchUnion, Occ;
            public List<Geometry> StairsAll, StorySlabs;
        }

        private BinaKesitCutInputs _binaKesitCutInputs;

        private bool TryFindBinaKesitCut(
            BinaKesitYon yon,
            out double cutPos,
            out Point2d cutA,
            out Point2d cutB,
            out Point2d alongOrig,
            out Vector2d dirUnit,
            out double aminHint,
            out double amaxHint)
        {
            cutPos = 0;
            cutA = cutB = alongOrig = Point2d.Origin;
            dirUnit = new Vector2d(1, 0);
            aminHint = 0;
            amaxHint = 100;

            // KALIP50: X ve Y kesiti aynı girdileri kullanır; oturumda bir kez kurulur.
            var girdi = _isKalip50Mode ? _binaKesitCutInputs : null;
            if (girdi == null)
            {
                Geometry csu = TryMergeKalip50BinaKesitCutSearchUnion();
                Geometry oc = TryMergeGeometryList(new List<Geometry>
                {
                    TryMergeFormworkCutOccupancyAllFloors(),
                    TryMergeFoundationOccupancyForKalip50Cut(),
                    csu
                }.Where(g => g != null && !g.IsEmpty).ToList()) ?? csu;
                girdi = new BinaKesitCutInputs { CutSearchUnion = csu, Occ = oc };
                if (csu != null && !csu.IsEmpty)
                {
                    girdi.StairsAll = BuildStairAvoidZonesAllFloors();
                    girdi.StorySlabs = BuildBinaKesitStorySlabFootprints();
                }
                if (_isKalip50Mode) _binaKesitCutInputs = girdi;
            }
            Geometry cutSearchUnion = girdi.CutSearchUnion;
            Geometry occ = girdi.Occ;

            if (cutSearchUnion == null || cutSearchUnion.IsEmpty)
                return false;

            var ext = GetAksSiniriEnvelope(cutSearchUnion);
            double xmin = ext.Xmin, xmax = ext.Xmax, ymin = ext.Ymin, ymax = ext.Ymax;
            double e = SectionLineExtendCm;
            var stairsAll = girdi.StairsAll;
            var refFloor = _model.Floors[0];
            var storySlabs = girdi.StorySlabs;

            // alongOrig (0,cut)/(cut,0): A = X veya Y — arka plan U ile aynı mutlak eksen.
            if (yon == BinaKesitYon.X)
            {
                cutPos = FindBestBinaKesitHorizontalY(refFloor, xmin, xmax, ymin, ymax, e, occ ?? cutSearchUnion, cutSearchUnion, stairsAll, storySlabs);
                cutA = new Point2d(xmin - e, cutPos);
                cutB = new Point2d(xmax + e, cutPos);
                alongOrig = new Point2d(0, cutPos);
                dirUnit = new Vector2d(1, 0);
                aminHint = xmin;
                amaxHint = xmax;
            }
            else
            {
                cutPos = FindBestBinaKesitVerticalX(refFloor, xmin, xmax, ymin, ymax, e, occ ?? cutSearchUnion, cutSearchUnion, stairsAll, storySlabs);
                cutA = new Point2d(cutPos, ymin - e);
                cutB = new Point2d(cutPos, ymax + e);
                alongOrig = new Point2d(cutPos, 0);
                dirUnit = new Vector2d(0, 1);
                aminHint = ymin;
                amaxHint = ymax;
            }
            return true;
        }

        private List<Geometry> BuildStairAvoidZonesAllFloors()
        {
            var list = new List<Geometry>();
            if (_model?.Floors == null) return list;
            foreach (var floor in _model.Floors)
            {
                var z = BuildStairAvoidZonesForFloor(floor);
                if (z != null) list.AddRange(z);
            }
            return list;
        }

        /// <summary>
        /// Her katın döşeme ayak izi (merdiven ve bitişik ara sahanlık hariç) ve varsa subasman.
        /// Kesit hattı bunların mümkün olduğunca çoğunu keser; bodrum / zemin çatısı gibi dar izler birleşik alana yenilmez.
        /// </summary>
        private List<Geometry> BuildBinaKesitStorySlabFootprints()
        {
            var stories = new List<Geometry>();
            if (_model?.Floors == null || _model.Slabs == null) return stories;

            bool hasFloorZero = false;
            foreach (var floor in _model.Floors)
            {
                if (floor != null && floor.FloorNo == 0) hasFloorZero = true;
                var parts = new List<Geometry>();
                int floorNo = floor?.FloorNo ?? int.MinValue;
                foreach (var slab in _model.Slabs)
                {
                    if (slab == null || GetSlabFloorNo(slab.SlabId) != floorNo) continue;
                    if (ShouldOmitSlabFromBinaView(slab)) continue;
                    try
                    {
                        var p = SlabFootprintPoly(slab);
                        if (p != null && !p.IsEmpty) parts.Add(p);
                    }
                    catch { }
                }
                var u = TryMergeGeometryList(parts);
                if (u != null && !u.IsEmpty) stories.Add(u);
            }

            if (!hasFloorZero)
            {
                int step = _model.SlabFloorKeyStep > 0 ? _model.SlabFloorKeyStep : 100;
                var sub = new List<Geometry>();
                foreach (var slab in _model.Slabs)
                {
                    if (slab == null || slab.SlabId <= 0 || slab.SlabId >= step) continue;
                    if (GetSlabFloorNo(slab.SlabId) != 0) continue;
                    if (ShouldOmitSlabFromBinaView(slab)) continue;
                    try
                    {
                        var p = SlabFootprintPoly(slab);
                        if (p != null && !p.IsEmpty) sub.Add(p);
                    }
                    catch { }
                }
                var su = TryMergeGeometryList(sub);
                if (su != null && !su.IsEmpty) stories.Add(su);
            }
            return stories;
        }

        private static int CountBinaKesitStoriesCut(IList<Geometry> stories, bool horizontal, double coord, double lo, double hi)
        {
            if (stories == null || stories.Count == 0) return 0;
            double span = hi - lo;
            if (span < 1.0) return 0;
            int n = 0;
            foreach (var g in stories)
            {
                if (g == null || g.IsEmpty) continue;
                var (frac, lng) = horizontal
                    ? SampleHorizontalOnOccupancy(g, coord, lo, hi)
                    : SampleVerticalOnOccupancy(g, coord, lo, hi);
                double longestCm = lng * span;
                double totalCm = frac * span;
                if (longestCm >= BinaKesitStoryHitMinCm || totalCm >= BinaKesitStoryHitMinCm * 1.5)
                    n++;
            }
            return n;
        }

        /// <summary>Önce daha çok kat döşemesi; aynı sayıda merdiven, paralel kiriş ve kolon cezası.</summary>
        private static bool BinaKesitCutCandidateWins(
            int hits, double key, double pos,
            int bestHits, double bestKey, double bestPos, double prefer)
        {
            if (hits > bestHits) return true;
            if (hits < bestHits) return false;
            if (key > bestKey + 1e-6) return true;
            if (Math.Abs(key - bestKey) < 1e-6 && Math.Abs(pos - prefer) < Math.Abs(bestPos - prefer)) return true;
            return false;
        }

        private static void AddBinaKesitStoryMidCandidates(List<double> coords, IList<Geometry> stories, bool horizontal, double lo, double hi)
        {
            if (stories == null || coords == null || hi <= lo) return;
            foreach (var g in stories)
            {
                if (g == null || g.IsEmpty) continue;
                var env = g.EnvelopeInternal;
                double mid = horizontal ? (env.MinY + env.MaxY) * 0.5 : (env.MinX + env.MaxX) * 0.5;
                if (mid < lo - 1e-6 || mid > hi + 1e-6) continue;
                bool near = false;
                for (int i = 0; i < coords.Count; i++)
                {
                    if (Math.Abs(coords[i] - mid) < 15.0) { near = true; break; }
                }
                if (!near) coords.Add(mid);
            }
        }

        private double FindBestBinaKesitHorizontalY(
            FloorInfo floor,
            double xmin, double xmax, double ymin, double ymax, double e,
            Geometry occ, Geometry structuralUnion, List<Geometry> stairsAll, List<Geometry> storySlabs)
        {
            var gf = _ntsDrawFactory;
            double halfW = Math.Max(60.0, SectionStripHalfWidthCm * 1.2);
            GetCutLineSearchBoundsFromStructure(xmin, xmax, ymin, ymax, structuralUnion,
                out _, out _, out double yLo, out double yHi, out _, out double cyPrefer);
            if (yHi <= yLo) return (ymin + ymax) * 0.5;

            double step = Math.Max(40.0, (yHi - yLo) / 48.0);
            var tanH = new Vector2d(1, 0);
            var candidates = new List<double>();
            for (double y = yLo; y <= yHi + 1e-6; y += step) candidates.Add(y);
            AddBinaKesitStoryMidCandidates(candidates, storySlabs, horizontal: true, yLo, yHi);
            double bestY = cyPrefer;
            int bestHits = -1;
            double bestKey = double.NegativeInfinity;

            foreach (double y in candidates)
            {
                var (frac, lng) = SampleHorizontalOnOccupancy(occ, y, xmin, xmax);
                Geometry strip = HorizontalCutStripPoly(gf, y, xmin, xmax, e, halfW);
                double par = ParallelSpanAxisPenalty(strip, tanH, floor, isFoundationPlan: false);
                double stair = StairPenaltyHorizontal(y, xmin, xmax, e, stairsAll);
                double colLong = 0;
                try { colLong = SlenderColumnLongAxisCutPenaltyHorizontal(y, xmin, xmax, e, floor, false); } catch { }
                double axisClr = 0;
                try { axisClr = ParallelColumnAxisClearancePenaltyHorizontal(y); } catch { }
                int hits = CountBinaKesitStoriesCut(storySlabs, horizontal: true, y, xmin, xmax);
                double key = frac * 900.0 + lng * 320.0 - stair - par - colLong - axisClr - Math.Abs(y - cyPrefer) * 0.02;
                if (BinaKesitCutCandidateWins(hits, key, y, bestHits, bestKey, bestY, cyPrefer))
                {
                    bestHits = hits;
                    bestKey = key;
                    bestY = y;
                }
            }
            return bestY;
        }

        private double FindBestBinaKesitVerticalX(
            FloorInfo floor,
            double xmin, double xmax, double ymin, double ymax, double e,
            Geometry occ, Geometry structuralUnion, List<Geometry> stairsAll, List<Geometry> storySlabs)
        {
            var gf = _ntsDrawFactory;
            double halfW = Math.Max(60.0, SectionStripHalfWidthCm * 1.2);
            GetCutLineSearchBoundsFromStructure(xmin, xmax, ymin, ymax, structuralUnion,
                out double xLo, out double xHi, out _, out _, out double cxPrefer, out _);
            if (xHi <= xLo) return (xmin + xmax) * 0.5;

            double step = Math.Max(40.0, (xHi - xLo) / 48.0);
            var tanV = new Vector2d(0, 1);
            var candidates = new List<double>();
            for (double x = xLo; x <= xHi + 1e-6; x += step) candidates.Add(x);
            AddBinaKesitStoryMidCandidates(candidates, storySlabs, horizontal: false, xLo, xHi);
            double bestX = cxPrefer;
            int bestHits = -1;
            double bestKey = double.NegativeInfinity;

            foreach (double x in candidates)
            {
                var (frac, lng) = SampleVerticalOnOccupancy(occ, x, ymin, ymax);
                Geometry strip = VerticalCutStripPoly(gf, x, ymin, ymax, e, halfW);
                double par = ParallelSpanAxisPenalty(strip, tanV, floor, isFoundationPlan: false);
                double stair = StairPenaltyVertical(x, ymin, ymax, e, stairsAll);
                double colLong = 0;
                try { colLong = SlenderColumnLongAxisCutPenaltyVertical(x, ymin, ymax, e, floor, false); } catch { }
                double axisClr = 0;
                try { axisClr = ParallelColumnAxisClearancePenaltyVertical(x); } catch { }
                int hits = CountBinaKesitStoriesCut(storySlabs, horizontal: false, x, ymin, ymax);
                double key = frac * 900.0 + lng * 320.0 - stair - par - colLong - axisClr - Math.Abs(x - cxPrefer) * 0.02;
                if (BinaKesitCutCandidateWins(hits, key, x, bestHits, bestKey, bestX, cxPrefer))
                {
                    bestHits = hits;
                    bestKey = key;
                    bestX = x;
                }
            }
            return bestX;
        }

        private void CollectBinaKesitSlices(
            Point2d cutA, Point2d cutB, Point2d alongOrig, Vector2d dirUnit,
            List<SectionSlice> slices)
        {
            for (int fi = 0; fi < _model.Floors.Count; fi++)
            {
                var floor = _model.Floors[fi];
                var colExtra = GetColumnTableExtraData(floor);
                var sl = CollectAllSectionSlices(floor, cutA, cutB, alongOrig, dirUnit, isFoundationPlan: false, colExtra,
                    omitStairAndAdjacentLandings: true);
                if (sl != null && sl.Count > 0)
                    slices.AddRange(sl);
            }

            // Temel + hatıl + bağ kirişi / radye / tekil (kolon-perde tekrarlanmaz).
            if (Kalip50ModelHasFoundationGeometry() && _model.Floors.Count > 0)
            {
                var ff = _model.Floors[0];
                var colExtraF = GetColumnTableExtraData(ff);
                var found = CollectAllSectionSlices(ff, cutA, cutB, alongOrig, dirUnit, isFoundationPlan: true, colExtraF,
                    omitStairAndAdjacentLandings: true);
                if (found != null)
                {
                    foreach (var s in found)
                    {
                        if (s.Layer == LayerKolon || s.Layer == LayerPerde) continue;
                        slices.Add(s);
                    }
                }
            }

            AppendBinaKesitSubasmanSlices(cutA, cutB, alongOrig, dirUnit, slices);
        }

        /// <summary>
        /// Subasman döşemesi Floors listesinde yok (FloorNo 0); slabId &lt; SlabFloorKeyStep.
        /// Kot: HasSubasmanStory → BuildingBase+SubasmanElevation; yoksa bina tabanı.
        /// </summary>
        private void AppendBinaKesitSubasmanSlices(
            Point2d cutA, Point2d cutB, Point2d alongOrig, Vector2d dirUnit,
            List<SectionSlice> slices)
        {
            if (_model?.Slabs == null || slices == null) return;
            int step = _model.SlabFloorKeyStep > 0 ? _model.SlabFloorKeyStep : 100;
            bool anySub = false;
            foreach (var slab in _model.Slabs)
            {
                if (slab == null || slab.SlabId <= 0 || slab.SlabId >= step) continue;
                if (GetSlabFloorNo(slab.SlabId) != 0) continue;
                anySub = true;
                break;
            }
            if (!anySub) return;

            double refTopCm = _model.HasSubasmanStory
                ? (_model.BuildingBaseKotu + _model.SubasmanElevationM) * 100.0
                : _model.BuildingBaseKotu * 100.0;

            Geometry cutLine;
            try
            {
                cutLine = _ntsDrawFactory.CreateLineString(new[]
                {
                    new Coordinate(cutA.X, cutA.Y),
                    new Coordinate(cutB.X, cutB.Y)
                });
            }
            catch { return; }
            if (cutLine == null || cutLine.IsEmpty) return;
            var dirN = dirUnit.GetNormal();

                foreach (var slab in _model.Slabs)
                {
                    if (slab == null || slab.SlabId <= 0 || slab.SlabId >= step) continue;
                    if (GetSlabFloorNo(slab.SlabId) != 0) continue;
                    if (ShouldOmitSlabFromBinaView(slab)) continue;

                    double th = slab.ThicknessCm > 0 ? slab.ThicknessCm : 15.0;
                double zt = refTopCm + slab.OffsetFromFloorCm;
                if (Math.Abs(zt - refTopCm) > 5.0) continue;
                double zb = zt - th;
                var poly = SlabFootprintPoly(slab);
                TryAddSliceCutLine(cutLine, poly, alongOrig, dirN, zb, zt, LayerDoseme, 5, slices);
            }
        }

        /// <summary>
        /// Kesit düzleminin arkasındaki elemanlar — görünüşteki gibi U×Z dikdörtgen projeksiyon.
        /// X yönü: U=X, Depth=Y (kesit Y=cut; arka Y &gt; cut). Y yönü: U=Y, Depth=X.
        /// </summary>
        private void CollectBinaKesitBackgroundFaces(BinaKesitYon yon, double cutPos, List<BinaGorunusFace> faces)
        {
            double baseCm = _model.BuildingBaseKotu * 100.0;
            double behindMin = cutPos + BinaKesitBehindEpsCm;

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
                    AddBinaKesitBackgroundRect(faces, yon, cutPos, behindMin, part, z0, z1);
                }
            }

            // Subasman döşemesi (FloorNo 0 — Floors listesinde yok).
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
                    var poly = GetColumnPolygonForTable(floor, col, 0.0, 0.0, _ntsDrawFactory);
                    addPlan(poly, ek.altKotCm, ek.altKotCm + yuk);
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
                double h = sfd.ThicknessCm > 0 ? sfd.ThicknessCm : 40.0;
                addPlan(SlabFoundationFootprintPoly(sfd), z0, z0 + h);
            }
            foreach (var tb in _model.TieBeams)
            {
                double z0 = (_model.BuildingBaseKotu + tb.BottomKotM) * 100.0;
                double h = tb.HeightCm > 0 ? tb.HeightCm : 25.0;
                addPlan(TieBeamFootprintPoly(tb), z0, z0 + h);
            }
            foreach (var sf in _model.SingleFootings)
            {
                double z0 = (_model.BuildingBaseKotu + sf.BottomLevelM) * 100.0;
                double h = sf.HeightCm > 0 ? sf.HeightCm : 30.0;
                addPlan(SingleFootingModelPoly(sf, footingFloor), z0, z0 + h);
            }
        }

        private void AddBinaKesitBackgroundRect(
            List<BinaGorunusFace> faces,
            BinaKesitYon yon,
            double cutPos,
            double behindMin,
            Geometry planPoly,
            double z0,
            double z1)
        {
            double u0 = double.MaxValue, u1 = double.MinValue;
            double dMin = double.MaxValue, dMax = double.MinValue;
            var coords = planPoly.Coordinates;
            if (coords == null || coords.Length == 0) return;
            for (int i = 0; i < coords.Length; i++)
            {
                double x = coords[i].X;
                double y = coords[i].Y;
                double u = yon == BinaKesitYon.X ? x : y;
                double d = yon == BinaKesitYon.X ? y : x;
                if (u < u0) u0 = u;
                if (u > u1) u1 = u;
                if (d < dMin) dMin = d;
                if (d > dMax) dMax = d;
            }
            if (u1 - u0 < 0.5 || double.IsInfinity(dMin)) return;

            // Kesit şeridine giren veya tamamen önde kalan eleman arka planda yok.
            if (dMin < cutPos + BinaKesitCutHalfWidthCm) return;
            if (dMax < behindMin) return;

            double depth = dMin;
            Coordinate[] ring =
            {
                new Coordinate(u0, z0),
                new Coordinate(u1, z0),
                new Coordinate(u1, z1),
                new Coordinate(u0, z1),
                new Coordinate(u0, z0)
            };
            try
            {
                var rect = _ntsDrawFactory.CreatePolygon(_ntsDrawFactory.CreateLinearRing(ring));
                if (rect == null || rect.IsEmpty || rect.Area < 1.0) return;
                faces.Add(new BinaGorunusFace { Poly = rect, Depth = depth });
            }
            catch
            {
                // bozuk halka
            }
        }
    }
}
