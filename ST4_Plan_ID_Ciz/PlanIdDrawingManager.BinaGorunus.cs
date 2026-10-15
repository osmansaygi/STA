using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>Plan bakışına göre bina görünüşü. Derinlik küçük olan izleyiciye yakındır.</summary>
    public enum BinaGorunusBakis
    {
        /// <summary>Planın alt kenarından yukarı (+Y).</summary>
        AsagidanYukariya = 0,
        /// <summary>Planın üst kenarından aşağı (−Y).</summary>
        YukaridanAsagiya = 1,
        /// <summary>Planın sol kenarından sağa (+X).</summary>
        SoldanSaga = 2,
        /// <summary>Planın sağ kenarından sola (−X).</summary>
        SagdanSola = 3
    }

    public sealed partial class PlanIdDrawingManager
    {
        /// <summary>Model cm tam boy; kâğıtta 1/200 (1/50 çizimin 1/4'ü — şematik kesit ile aynı).</summary>
        private const double BinaGorunusScale = 0.25;
        /// <summary>Bu derinlik farkının içindeki yüzler aynı hizada sayılır; kolon–kiriş birleşim çizgisi kalkar.</summary>
        private const double BinaGorunusFlushDepthCm = 15.0;
        private const double BinaGorunusTitleGapCm = 40.0;
        private const string BinaGorunusTitle = "GORUNUS (1:200)";

        /// <summary>Ön 1/3 — ACI 150.</summary>
        private const string LayerGorunusOn = "GORUNUS ON (BEYKENT)";
        /// <summary>Orta 1/3 — ACI 116.</summary>
        private const string LayerGorunusOrta = "GORUNUS ORTA (BEYKENT)";
        /// <summary>Arka 1/3 — ACI 251.</summary>
        private const string LayerGorunusArka = "GORUNUS ARKA (BEYKENT)";
        private const short GorunusOnAci = 150;
        private const short GorunusOrtaAci = 116;
        private const short GorunusArkaAci = 251;

        /// <summary>
        /// Bina görünüş/kesit: merdiven döşmesi ve ona komşu ara sahanlık (OffsetFromFloorCm &lt; 0) çizilmez.
        /// </summary>
        private bool ShouldOmitSlabFromBinaView(SlabInfo slab)
        {
            if (slab == null || slab.SlabId <= 0) return true;
            if (_model?.StairSlabIds != null && _model.StairSlabIds.Contains(slab.SlabId))
                return true;
            if (slab.OffsetFromFloorCm >= 0)
                return false;

            int floorNo = GetSlabFloorNo(slab.SlabId);
            Geometry stairUnion = GetCachedStairFootprintUnionForFloorNo(floorNo);
            if (stairUnion == null || stairUnion.IsEmpty)
                return false;

            Geometry slabPoly = null;
            try { slabPoly = SlabFootprintPoly(slab); }
            catch { return false; }
            if (slabPoly == null || slabPoly.IsEmpty) return false;

            const double adjTolCm = 8.0;
            return AreSlabGeometriesAdjacent(slabPoly, stairUnion, adjTolCm)
                || SafeGeometryIntersects(slabPoly, stairUnion);
        }

        private Dictionary<int, Geometry> _stairFootprintUnionByFloorNo;

        private Geometry GetCachedStairFootprintUnionForFloorNo(int floorNo)
        {
            if (_stairFootprintUnionByFloorNo == null)
                _stairFootprintUnionByFloorNo = new Dictionary<int, Geometry>();
            if (_stairFootprintUnionByFloorNo.TryGetValue(floorNo, out Geometry cached))
                return cached;

            Geometry union = null;
            if (_ntsDrawFactory == null)
                _ntsDrawFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
            var parts = new List<Geometry>();
            if (_model?.Slabs != null && _model.StairSlabIds != null)
            {
                foreach (var s in _model.Slabs)
                {
                    if (s == null || !_model.StairSlabIds.Contains(s.SlabId)) continue;
                    if (GetSlabFloorNo(s.SlabId) != floorNo) continue;
                    try
                    {
                        var p = SlabFootprintPoly(s);
                        if (p != null && !p.IsEmpty) parts.Add(p);
                    }
                    catch { /* atla */ }
                }
            }
            if (parts.Count > 0)
                union = TryCascadedPolygonUnionSafe(parts);
            _stairFootprintUnionByFloorNo[floorNo] = union;
            return union;
        }

        private sealed class BinaGorunusFace
        {
            public Geometry Poly;
            public double Depth;
        }

        private sealed class BinaGorunusShiftFilter : ICoordinateSequenceFilter
        {
            private readonly double _dx;
            private readonly double _dy;
            public BinaGorunusShiftFilter(double dx, double dy) { _dx = dx; _dy = dy; }
            public bool Done => false;
            public bool GeometryChanged => true;
            public void Filter(CoordinateSequence seq, int i)
            {
                seq.SetX(i, seq.GetX(i) + _dx);
                seq.SetY(i, seq.GetY(i) + _dy);
            }
        }

        /// <summary>
        /// ST4 modelinin seçilen bakıştaki görünüşü. 1/200, altta GORUNUS (1:200), sağda kat kotları.
        /// Derinlik 3 dilim: ön ACI150 / orta ACI116 / arka ACI251. Kılavuz, zemin ve tarama yok.
        /// </summary>
        public bool DrawBinaGorunus(Database db, Editor ed, Point3d insert, BinaGorunusBakis bakis)
        {
            if (db == null || ed == null || _model?.Floors == null || _model.Floors.Count == 0)
            {
                ed?.WriteMessage("\nBINAGORUNUS: Modelde kat bulunamadi.");
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
                    string blockName = "BINAGORUNUS_" + Guid.NewGuid().ToString("N");
                    while (bt.Has(blockName))
                        blockName = "BINAGORUNUS_" + Guid.NewGuid().ToString("N");
                    var btrDef = new BlockTableRecord { Name = blockName, Origin = Point3d.Origin };
                    ObjectId blockId = bt.Add(btrDef);
                    tr.AddNewlyCreatedDBObject(btrDef, true);

                    if (!TryDrawBinaGorunusIntoBlock(tr, btrDef, bakis,
                            out double uMin, out double zMin, out double spanU, out _,
                            ed, "BINAGORUNUS"))
                        return false;

                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    var br = new BlockReference(new Point3d(insert.X, insert.Y, 0), blockId)
                    {
                        ScaleFactors = new Scale3d(BinaGorunusScale),
                        Layer = LayerGorunusOn
                    };
                    AppendEntity(tr, ms, br);

                    double cx = insert.X + spanU * BinaGorunusScale * 0.5;
                    DrawKesitTitleBelowSchematic(tr, ms, db, BinaGorunusTitle, cx, insert.Y - BinaGorunusTitleGapCm);
                    DrawBinaGorunusFloorKots(tr, ms, db, insert.X, insert.Y, uMin, zMin, spanU, BinaGorunusScale);
                    DrawBinaGorunusFloorNameLabels(tr, ms, db, insert.X, insert.Y, zMin, BinaGorunusScale);

                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                ed.WriteMessage("\nBINAGORUNUS: Cizilemedi: {0}", ex.Message);
                return false;
            }

            ed.WriteMessage("\nBINAGORUNUS: 1/200 gorunus yerlestirildi ({0}; on/orta/arka ton).", BinaGorunusBakisLabel(bakis));
            return true;
        }

        /// <summary>
        /// Hepsi: X/Y kesit + 4 görünüş (3×2 ızgara), 1/200 içerik, 1/50 antet içinde.
        /// <paramref name="insert"/> = SheetViewOut sol-altının 50 cm solu.
        /// </summary>
        public bool DrawBinaGorunusHepsi(Database db, Editor ed, Point3d insert, string st4SourcePath)
        {
            if (db == null || ed == null || _model?.Floors == null || _model.Floors.Count == 0)
            {
                ed?.WriteMessage("\nBINAGORUNUS: Modelde kat bulunamadi.");
                return false;
            }

            _ntsDrawFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
            const double sc = BinaGorunusScale; // 1:200
            const double gapCol = 220.0;
            const double gapRow = 100.0;
            const double titleBand = 55.0;
            const double leftLabelPad = 40.0;
            const double kotPad = 130.0;
            const double contentLeftPad = 160.0;
            const double contentBottomPad = 140.0;
            const double contentTopPad = 80.0;
            const double contentRightPad = 160.0;

            string gprPath = ResolveGprPathNextToSt4(st4SourcePath);
            if (!string.IsNullOrEmpty(gprPath))
                ed.WriteMessage("\nBINAGORUNUS Hepsi: GPR bulundu ({0}).", Path.GetFileName(gprPath));
            else
                ed.WriteMessage("\nBINAGORUNUS Hepsi: ST4 yaninda GPR yok (antet ST4 ile doldurulur).");

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayers(tr, db);
                    EnsureBinaGorunusLayers(tr, db);
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    ObjectId MakeGorunus(BinaGorunusBakis bakis, out double uMin, out double zMin, out double spanU, out double spanZ)
                    {
                        uMin = zMin = spanU = spanZ = 0;
                        string name = "BGOR_" + bakis + "_" + Guid.NewGuid().ToString("N");
                        while (bt.Has(name)) name = "BGOR_" + Guid.NewGuid().ToString("N");
                        var def = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
                        ObjectId id = bt.Add(def);
                        tr.AddNewlyCreatedDBObject(def, true);
                        if (!TryDrawBinaGorunusIntoBlock(tr, def, bakis, out uMin, out zMin, out spanU, out spanZ, ed, "BINAGORUNUS"))
                            return ObjectId.Null;
                        return id;
                    }

                    ObjectId MakeKesit(BinaKesitYon yon, out double amin, out double zmin, out double spanU, out double spanZ)
                    {
                        amin = zmin = spanU = spanZ = 0;
                        string name = "BKES_" + yon + "_" + Guid.NewGuid().ToString("N");
                        while (bt.Has(name)) name = "BKES_" + Guid.NewGuid().ToString("N");
                        var def = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
                        ObjectId id = bt.Add(def);
                        tr.AddNewlyCreatedDBObject(def, true);
                        if (!TryDrawBinaKesitIntoBlock(tr, def, yon, out amin, out zmin, out spanU, out spanZ, out _, ed, "BINAGORUNUS"))
                            return ObjectId.Null;
                        return id;
                    }

                    var idX = MakeKesit(BinaKesitYon.X, out double aminX, out double zminX, out double spanUX, out double spanZX);
                    var idY = MakeKesit(BinaKesitYon.Y, out double aminY, out double zminY, out double spanUY, out double spanZY);
                    var idAsa = MakeGorunus(BinaGorunusBakis.AsagidanYukariya, out double uAsa, out double zAsa, out double spanUAsa, out double spanZAsa);
                    var idSag = MakeGorunus(BinaGorunusBakis.SagdanSola, out double uSag, out double zSag, out double spanUSag, out double spanZSag);
                    var idYuk = MakeGorunus(BinaGorunusBakis.YukaridanAsagiya, out double uYuk, out double zYuk, out double spanUYuk, out double spanZYuk);
                    var idSol = MakeGorunus(BinaGorunusBakis.SoldanSaga, out double uSol, out double zSol, out double spanUSol, out double spanZSol);

                    if (!idX.IsValid || !idY.IsValid || !idAsa.IsValid || !idSag.IsValid || !idYuk.IsValid || !idSol.IsValid)
                        return false;

                    double wX = spanUX * sc, hX = spanZX * sc;
                    double wY = spanUY * sc, hY = spanZY * sc;
                    double wAsa = spanUAsa * sc, hAsa = spanZAsa * sc;
                    double wSag = spanUSag * sc, hSag = spanZSag * sc;
                    double wYuk = spanUYuk * sc, hYuk = spanZYuk * sc;
                    double wSol = spanUSol * sc, hSol = spanZSol * sc;

                    double colW0 = Math.Max(wX, Math.Max(wAsa, wYuk)) + kotPad;
                    double colW1 = Math.Max(wY, Math.Max(wSag, wSol)) + kotPad;
                    double rowH0 = Math.Max(hX, hY) + titleBand;
                    double rowH1 = Math.Max(hAsa, hSag) + titleBand;
                    double rowH2 = Math.Max(hYuk, hSol) + titleBand;

                    if (!TryGetEmbeddedAntetSheetViewOutOffsets(out double outDx, out double outDy, ed))
                    {
                        outDx = 0;
                        outDy = AntetDxfSheetViewOutYmin - AntetDxfSheetViewYmin;
                    }
                    double svLeft = insert.X + AntetYerlesimSheetViewOutSolPayCm - outDx;
                    double svBottom = insert.Y - outDy;

                    double xL = svLeft + contentLeftPad + leftLabelPad;
                    double xR = xL + colW0 + gapCol;
                    double y2 = svBottom + contentBottomPad;
                    double y1 = y2 + rowH2 + gapRow;
                    double y0 = y1 + rowH1 + gapRow;

                    void Place(ObjectId blockId, double ix, double iyTop, double panelH, double spanU, double zMin, double uMin,
                        string title, bool kesitFloorIds)
                    {
                        double iy = iyTop - panelH;
                        var br = new BlockReference(new Point3d(ix, iy, 0), blockId)
                        {
                            ScaleFactors = new Scale3d(sc),
                            Layer = LayerKesit
                        };
                        AppendEntity(tr, ms, br);
                        double cx = ix + spanU * sc * 0.5;
                        DrawKesitTitleBelowSchematic(tr, ms, db, title, cx, iy - BinaGorunusTitleGapCm);
                        if (kesitFloorIds)
                        {
                            DrawBinaKesitFloorKots(tr, ms, db, ix, iy, uMin, zMin, spanU);
                            // DrawBinaKesitFloorKots uses BinaKesitScale internally — OK same 0.25
                            DrawBinaKesitFloorNameLabels(tr, ms, db, ix, iy, zMin, sc);
                        }
                        else
                        {
                            DrawBinaGorunusFloorKots(tr, ms, db, ix, iy, uMin, zMin, spanU, sc);
                            DrawBinaGorunusFloorNameLabels(tr, ms, db, ix, iy, zMin, sc);
                        }
                    }

                    double top0 = y0 + rowH0 - titleBand;
                    double top1 = y1 + rowH1 - titleBand;
                    double top2 = y2 + rowH2 - titleBand;

                    Place(idX, xL, top0, hX, spanUX, zminX, aminX, "KESIT X (1:200)", true);
                    Place(idY, xR, top0, hY, spanUY, zminY, aminY, "KESIT Y (1:200)", true);
                    Place(idAsa, xL, top1, hAsa, spanUAsa, zAsa, uAsa, "ON GORUNUS (1:200)", false);
                    Place(idSag, xR, top1, hSag, spanUSag, zSag, uSag, "SAG GORUNUS (1:200)", false);
                    Place(idYuk, xL, top2, hYuk, spanUYuk, zYuk, uYuk, "ARKA GORUNUS (1:200)", false);
                    Place(idSol, xR, top2, hSol, spanUSol, zSol, uSol, "SOL GORUNUS (1:200)", false);

                    double layoutMaxX = xR + colW1 + contentRightPad;
                    // Genişlik: çizime göre (şablon varsayılanından daraltılabilir).
                    double targetRight = layoutMaxX;
                    // Min. SheetViewOut = 4500 cm. fitSheetViewHeightToContent, layoutMaxY'yi SheetView
                    // iç üstü sayar; Out şablonda üstte ve altta 50 cm taşar. Bu pay düşülmezse Out 4600 olur.
                    const double antetSheetViewSrcTop = 5758.942367378282;
                    double outAboveSv = AntetDxfSheetViewOutYmax - antetSheetViewSrcTop;
                    double outBelowSv = AntetDxfSheetViewYmin - AntetDxfSheetViewOutYmin;
                    double minSheetViewH = TemelAntetBaslangicYukseklikCm - outAboveSv - outBelowSv;
                    double contentTop = y0 + rowH0 + contentTopPad;
                    double layoutMaxY = Math.Max(contentTop, svBottom + minSheetViewH);

                    if (!TryDrawAntetFromEmbeddedTemplate(
                            tr, ms,
                            insert.X, insert.Y, layoutMaxY,
                            svLeft, svBottom, targetRight,
                            st4SourcePath, ed,
                            "BINA GORUNUS / KESIT",
                            null,
                            out _, out _, out _,
                            scaleTwoTimes: false,
                            useKolonAntetScaleOneHundred: false,
                            useKalipAntetScaleOneHundred: false,
                            fitSheetViewHeightToContent: true,
                            antetScaleTextOverride: "1:200"))
                    {
                        ed.WriteMessage("\nBINAGORUNUS Hepsi: Antet yerlestirilemedi (icerik cizildi).");
                    }

                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                ed.WriteMessage("\nBINAGORUNUS Hepsi: Cizilemedi: {0}", ex.Message);
                return false;
            }

            ed.WriteMessage("\nBINAGORUNUS Hepsi: X/Y kesit + 4 gorunus (1:200), 1/50 antet icinde yerlestirildi.");
            return true;
        }

        /// <summary>Görünüş geometrisini mevcut blok tanımına çizer (orijin = sol-alt).</summary>
        private bool TryDrawBinaGorunusIntoBlock(
            Transaction tr,
            BlockTableRecord btrDef,
            BinaGorunusBakis bakis,
            out double uMin,
            out double zMin,
            out double spanU,
            out double spanZ,
            Editor ed = null,
            string errPrefix = null)
        {
            uMin = zMin = spanU = spanZ = 0;
            string tag = string.IsNullOrWhiteSpace(errPrefix) ? "BINAGORUNUS" : errPrefix.Trim();
            if (tr == null || btrDef == null || _model?.Floors == null || _model.Floors.Count == 0)
                return false;
            if (_ntsDrawFactory == null)
                _ntsDrawFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();

            var faces = new List<BinaGorunusFace>();
            try
            {
                CollectBinaGorunusFaces(bakis, faces);
            }
            catch (Exception ex)
            {
                ed?.WriteMessage("\n{0}: Geometri toplanamadi: {1}", tag, ex.Message);
                return false;
            }

            if (faces.Count == 0)
            {
                ed?.WriteMessage("\n{0}: Cizilecek eleman bulunamadi.", tag);
                return false;
            }

            double depthMin = faces.Min(f => f.Depth);
            double depthMax = faces.Max(f => f.Depth);
            double depthSpan = Math.Max(depthMax - depthMin, 1.0);

            faces.Sort((a, b) => a.Depth.CompareTo(b.Depth));
            var bands = new List<List<BinaGorunusFace>>();
            foreach (var face in faces)
            {
                if (bands.Count == 0 || face.Depth > bands[bands.Count - 1][0].Depth + BinaGorunusFlushDepthCm)
                    bands.Add(new List<BinaGorunusFace>());
                bands[bands.Count - 1].Add(face);
            }

            var visibleParts = new List<(Geometry Geom, double Depth)>();
            Geometry occluder = null;
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
                    visibleParts.Add((visible, bandDepth));

                occluder = occluder == null ? uni : BinaGorunusPolygonal(BinaGorunusUnion(occluder, uni));
                if (occluder == null) occluder = uni;
            }

            if (visibleParts.Count == 0)
            {
                ed?.WriteMessage("\n{0}: Gorunur kontur olusmadi.", tag);
                return false;
            }

            var env = new Envelope();
            foreach (var part in visibleParts)
                env.ExpandToInclude(part.Geom.EnvelopeInternal);
            if (env.Width < 1.0 || env.Height < 1.0)
            {
                ed?.WriteMessage("\n{0}: Gorunus olcusu gecersiz.", tag);
                return false;
            }

            uMin = env.MinX;
            zMin = env.MinY;
            spanU = env.Width;
            spanZ = env.Height;

            var shift = new BinaGorunusShiftFilter(-uMin, -zMin);
            var ordered = visibleParts
                .Select(p => (p.Geom, p.Depth, Tier: BinaGorunusDepthTier(p.Depth, depthMin, depthSpan)))
                .OrderByDescending(p => p.Tier)
                .ThenByDescending(p => p.Depth)
                .ToList();

            foreach (var part in ordered)
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

            Geometry allVisible = null;
            foreach (var part in visibleParts)
            {
                var g = part.Geom.Copy();
                g.Apply(shift);
                allVisible = allVisible == null ? g : BinaGorunusPolygonal(BinaGorunusUnion(allVisible, g));
            }
            if (allVisible != null && !allVisible.IsEmpty)
            {
                DrawGeometryRingsAsPolylines(
                    tr, btrDef, allVisible, LayerGorunusOn,
                    addHatch: false,
                    exteriorRingsOnly: true,
                    applySmallTriangleTrim: true);
            }

            return true;
        }

        /// <summary>0=ön (yakın), 1=orta, 2=arka (uzak). Derinlik aralığı üç eşit dilim.</summary>
        private static int BinaGorunusDepthTier(double depth, double depthMin, double depthSpan)
        {
            double t = (depth - depthMin) / depthSpan;
            if (t < 1.0 / 3.0) return 0;
            if (t < 2.0 / 3.0) return 1;
            return 2;
        }

        private static string BinaGorunusLayerForTier(int tier)
        {
            switch (tier)
            {
                case 0: return LayerGorunusOn;
                case 1: return LayerGorunusOrta;
                default: return LayerGorunusArka;
            }
        }

        /// <summary>Görünüş derinlik katmanları: ön 150 / orta 116 / arka 251 (mevcutsa renk güncellenir).</summary>
        private static void EnsureBinaGorunusLayers(Transaction tr, Database db)
        {
            EnsurePlanLayer(tr, db, LayerGorunusOn, GorunusOnAci, LineWeight.LineWeight025, useDashed: false);
            EnsurePlanLayer(tr, db, LayerGorunusOrta, GorunusOrtaAci, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, LayerGorunusArka, GorunusArkaAci, LineWeight.LineWeight018, useDashed: false);
            ForceBinaGorunusLayerColor(tr, db, LayerGorunusOn, GorunusOnAci);
            ForceBinaGorunusLayerColor(tr, db, LayerGorunusOrta, GorunusOrtaAci);
            ForceBinaGorunusLayerColor(tr, db, LayerGorunusArka, GorunusArkaAci);
        }

        private static void ForceBinaGorunusLayerColor(Transaction tr, Database db, string layerName, short aci)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName)) return;
            var rec = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
            rec.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
            try { rec.IsOff = false; } catch { /* eski sürüm */ }
        }

        /// <summary>Görünüşün solunda kat kısa adları (şematik kesitteki gibi, sol kenara).</summary>
        private void DrawBinaGorunusFloorNameLabels(
            Transaction tr, BlockTableRecord btr, Database db,
            double insertX, double insertY, double zMin, double scale = BinaGorunusScale)
        {
            if (_model?.Floors == null || _model.Floors.Count == 0) return;
            if (scale <= 1e-9) scale = BinaGorunusScale;
            ObjectId styleId = GetOrCreateYaziBeykentTextStyle(tr, db);
            double xLabel = insertX - 18.0;
            double textH = 14.0;
            var trTr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
            foreach (var floor in _model.Floors.OrderBy(f => f.ElevationM))
            {
                string name = !string.IsNullOrWhiteSpace(floor.ShortName)
                    ? floor.ShortName
                    : (!string.IsNullOrWhiteSpace(floor.Name) ? floor.Name : floor.FloorNo.ToString());
                if (string.IsNullOrWhiteSpace(name)) continue;
                double zFloor = (_model.BuildingBaseKotu + floor.ElevationM) * 100.0;
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
        }

        private static string BinaGorunusBakisLabel(BinaGorunusBakis bakis)
        {
            switch (bakis)
            {
                case BinaGorunusBakis.AsagidanYukariya: return "alttan yukariya";
                case BinaGorunusBakis.YukaridanAsagiya: return "ustten asagiya";
                case BinaGorunusBakis.SoldanSaga: return "soldan saga";
                default: return "sagdan sola";
            }
        }

        private void DrawBinaGorunusFloorKots(
            Transaction tr, BlockTableRecord btr, Database db,
            double insertX, double insertY, double uMin, double zMin, double spanU, double scale = BinaGorunusScale)
        {
            if (scale <= 1e-9) scale = BinaGorunusScale;
            var raw = new List<double> { _model.BuildingBaseKotu * 100.0 };
            foreach (var floor in _model.Floors)
                raw.Add((_model.BuildingBaseKotu + floor.ElevationM) * 100.0);
            var zsAsc = MergeKesitElevationZsAscending(raw, KesitKotMergeTolCm);
            if (zsAsc == null || zsAsc.Count == 0) return;

            double minSepModel = KesitKotCrowdedSeparationMinCm / scale;
            double[] crowded = BuildKesitKotCrowdedShiftForLowerElevation(
                zsAsc, minSepModel, KesitKotCrowdedShiftLowerCm);
            ObjectId styleId = GetOrCreateYaziBeykentTextStyle(tr, db);
            double xRight = insertX + spanU * scale;
            double xDatumBase = xRight + KesitKotDatumGapFromSectionCm + 35.0;
            const double rot = 0.0;

            for (int zi = zsAsc.Count - 1; zi >= 0; zi--)
            {
                double zElev = zsAsc[zi];
                double extra = zi < crowded.Length ? crowded[zi] : 0.0;
                double apexX = xDatumBase + extra;
                double apexY = insertY + (zElev - zMin) * scale;
                DrawKesitKotClassicSymbol(tr, btr, apexX, apexY, rot, 1.0);
                double lxText = KesitKotTriHalfWidthCm;
                double lyText = KesitKotTriHeightCm + KesitKotTextAboveExtensionCm;
                double textX = apexX + lxText;
                double textY = apexY + lyText;
                AppendKesitKotElevationDbText(
                    tr, btr, db, styleId, FormatKesitKotElevationString(zElev), textX, textY, rot, KesitKotTextHeightCm);
            }
        }

        private void CollectBinaGorunusFaces(BinaGorunusBakis bakis, List<BinaGorunusFace> faces)
        {
            double baseCm = _model.BuildingBaseKotu * 100.0;
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
                    AddBinaGorunusPlan(faces, bakis, SlabFootprintPoly(slab), zt - th, zt);
                }

                foreach (var beam in MergeSameIdBeamsOnFloor(floorNo))
                {
                    double zu = floorLevelCm + Math.Max(beam.Point1KotCm, beam.Point2KotCm);
                    double h = beam.HeightCm > 0 ? beam.HeightCm : (beam.IsWallFlag == 1 ? 40.0 : 30.0);
                    AddBinaGorunusPlan(faces, bakis, BeamFootprintPoly(beam), zu - h, zu);
                }

                var extra = GetColumnTableExtraData(floor);
                foreach (var col in _model.Columns)
                {
                    if (!HasColumnOnFloor(floor, col)) continue;
                    if (!extra.TryGetValue(col.ColumnNo, out var ek)) continue;
                    double yuk = ek.yukseklikCm > 1.0 ? ek.yukseklikCm : SectionMinStoryHeightCm;
                    var poly = GetColumnPolygonForTable(floor, col, 0.0, 0.0, _ntsDrawFactory);
                    AddBinaGorunusPlan(faces, bakis, poly, ek.altKotCm, ek.altKotCm + yuk);
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
                    if (amp != null && !amp.IsEmpty) AddBinaGorunusPlan(faces, bakis, amp, z0, z0 + cf.AmpatmanHeightCm);
                }
                AddBinaGorunusPlan(faces, bakis, ContinuousFootprintPoly(cf), z0, z1);
                var hat = HatilStripOnContinuousPoly(cf);
                if (hat != null && cf.HatilLabelHeightCm > 0)
                    AddBinaGorunusPlan(faces, bakis, hat, z1, z1 + cf.HatilLabelHeightCm);
            }
            foreach (var sfd in _model.SlabFoundations)
            {
                double z0 = (_model.BuildingBaseKotu + sfd.BottomLevelM) * 100.0;
                double h = sfd.ThicknessCm > 0 ? sfd.ThicknessCm : 40.0;
                AddBinaGorunusPlan(faces, bakis, SlabFoundationFootprintPoly(sfd), z0, z0 + h);
            }
            foreach (var tb in _model.TieBeams)
            {
                double z0 = (_model.BuildingBaseKotu + tb.BottomKotM) * 100.0;
                double h = tb.HeightCm > 0 ? tb.HeightCm : 25.0;
                AddBinaGorunusPlan(faces, bakis, TieBeamFootprintPoly(tb), z0, z0 + h);
            }
            foreach (var sf in _model.SingleFootings)
            {
                double z0 = (_model.BuildingBaseKotu + sf.BottomLevelM) * 100.0;
                double h = sf.HeightCm > 0 ? sf.HeightCm : 30.0;
                AddBinaGorunusPlan(faces, bakis, SingleFootingModelPoly(sf, footingFloor), z0, z0 + h);
            }
        }

        private void AddBinaGorunusPlan(List<BinaGorunusFace> faces, BinaGorunusBakis bakis, Geometry plan, double z0, double z1)
        {
            if (plan == null || plan.IsEmpty) return;
            if (z1 < z0)
            {
                double t = z0;
                z0 = z1;
                z1 = t;
            }
            if (z1 - z0 < 0.5) return;

            Geometry solid = PrepareForOverlay(plan);
            if (solid == null || solid.IsEmpty) return;

            for (int i = 0; i < solid.NumGeometries; i++)
            {
                var part = solid.GetGeometryN(i);
                if (!(part is Polygon)) continue;
                AddBinaGorunusProjectedRect(faces, bakis, part, z0, z1);
            }
        }

        private void AddBinaGorunusProjectedRect(List<BinaGorunusFace> faces, BinaGorunusBakis bakis, Geometry planPoly, double z0, double z1)
        {
            double u0 = double.MaxValue, u1 = double.MinValue, depth = double.MaxValue;
            var coords = planPoly.Coordinates;
            if (coords == null || coords.Length == 0) return;
            for (int i = 0; i < coords.Length; i++)
            {
                double x = coords[i].X;
                double y = coords[i].Y;
                double u = BinaGorunusU(bakis, x, y);
                double d = BinaGorunusDepth(bakis, x, y);
                if (u < u0) u0 = u;
                if (u > u1) u1 = u;
                if (d < depth) depth = d;
            }
            if (u1 - u0 < 0.5 || double.IsInfinity(depth)) return;

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
                // bozuk halka atlanır
            }
        }

        private static double BinaGorunusU(BinaGorunusBakis bakis, double x, double y)
        {
            switch (bakis)
            {
                case BinaGorunusBakis.AsagidanYukariya: return x;
                case BinaGorunusBakis.YukaridanAsagiya: return -x;
                case BinaGorunusBakis.SoldanSaga: return -y;
                default: return y;
            }
        }

        private static double BinaGorunusDepth(BinaGorunusBakis bakis, double x, double y)
        {
            switch (bakis)
            {
                case BinaGorunusBakis.AsagidanYukariya: return y;
                case BinaGorunusBakis.YukaridanAsagiya: return -y;
                case BinaGorunusBakis.SoldanSaga: return x;
                default: return -x;
            }
        }

        /// <summary>
        /// Geçersiz poligonda Buffer(0) OverlayNG yığınını taşırıp AutoCAD'i kapatır.
        /// Önce 0,01 cm ızgara, o da yetmezse GeometryFixer. Geçerli girdi aynen kalır.
        /// </summary>
        private static Geometry PrepareForOverlay(Geometry g)
        {
            if (g == null || g.IsEmpty) return g;
            try
            {
                if (g.IsValid) return g;
            }
            catch { }
            var reduced = ReducePrecisionSafe(g, 100.0);
            if (reduced != null && !reduced.IsEmpty)
            {
                try { if (reduced.IsValid) return reduced; } catch { }
                g = reduced;
            }
            try
            {
                var fixedG = NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(g);
                if (fixedG != null && !fixedG.IsEmpty) return fixedG;
            }
            catch { }
            return g;
        }

        private static Geometry BinaGorunusPolygonal(Geometry g)
        {
            if (g == null || g.IsEmpty) return null;
            g = PrepareForOverlay(g);
            if (g == null || g.IsEmpty) return null;
            if (g is Polygon || g is MultiPolygon) return g;

            var polys = new List<Polygon>();
            for (int i = 0; i < g.NumGeometries; i++)
            {
                if (g.GetGeometryN(i) is Polygon p && !p.IsEmpty && p.Area > 1.0)
                    polys.Add(p);
            }
            if (polys.Count == 0) return null;
            if (polys.Count == 1) return polys[0];
            return g.Factory.CreateMultiPolygon(polys.ToArray());
        }

        private static Geometry BinaGorunusUnion(Geometry a, Geometry b)
        {
            if (a == null || a.IsEmpty) return b;
            if (b == null || b.IsEmpty) return a;
            a = PrepareForOverlay(a);
            b = PrepareForOverlay(b);
            // Büyük siluetlerde ham Union yığın taşırır. 0,1 cm ızgara çizimde görünmez.
            if (a != null && b != null && a.NumPoints + b.NumPoints > 80)
            {
                a = ReducePrecisionSafe(a, 10.0) ?? a;
                b = ReducePrecisionSafe(b, 10.0) ?? b;
            }
            try { return a.Union(b); }
            catch
            {
                var aa = ReducePrecisionSafe(a, 1.0);
                var bb = ReducePrecisionSafe(b, 1.0);
                try { return (aa ?? a).Union(bb ?? b); }
                catch { return a; }
            }
        }

        private static Geometry BinaGorunusDifference(Geometry a, Geometry b)
        {
            if (a == null || a.IsEmpty) return null;
            if (b == null || b.IsEmpty) return a;
            a = PrepareForOverlay(a);
            b = PrepareForOverlay(b);
            if (a != null && b != null && a.NumPoints + b.NumPoints > 80)
            {
                a = ReducePrecisionSafe(a, 10.0) ?? a;
                b = ReducePrecisionSafe(b, 10.0) ?? b;
            }
            try { return a.Difference(b); }
            catch
            {
                var aa = ReducePrecisionSafe(a, 1.0);
                var bb = ReducePrecisionSafe(b, 1.0);
                try { return (aa ?? a).Difference(bb ?? b); }
                catch { return a; }
            }
        }
    }
}
