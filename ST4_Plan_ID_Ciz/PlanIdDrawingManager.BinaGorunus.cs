using System;
using System.Collections.Generic;
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
            var faces = new List<BinaGorunusFace>();
            try
            {
                CollectBinaGorunusFaces(bakis, faces);
            }
            catch (Exception ex)
            {
                ed.WriteMessage("\nBINAGORUNUS: Geometri toplanamadi: {0}", ex.Message);
                return false;
            }

            if (faces.Count == 0)
            {
                ed.WriteMessage("\nBINAGORUNUS: Cizilecek eleman bulunamadi.");
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
                ed.WriteMessage("\nBINAGORUNUS: Gorunur kontur olusmadi.");
                return false;
            }

            var env = new Envelope();
            foreach (var part in visibleParts)
                env.ExpandToInclude(part.Geom.EnvelopeInternal);
            if (env.Width < 1.0 || env.Height < 1.0)
            {
                ed.WriteMessage("\nBINAGORUNUS: Gorunus olcusu gecersiz.");
                return false;
            }

            double uMin = env.MinX;
            double zMin = env.MinY;
            double spanU = env.Width;

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

                    var shift = new BinaGorunusShiftFilter(-uMin, -zMin);

                    // Arka → orta → ön çiz (ön çizgiler üstte kalsın).
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

                    // Tüm görünürün dış silueti — ön katmanda.
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

                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    var br = new BlockReference(new Point3d(insert.X, insert.Y, 0), blockId)
                    {
                        ScaleFactors = new Scale3d(BinaGorunusScale),
                        Layer = LayerGorunusOn
                    };
                    AppendEntity(tr, ms, br);

                    double cx = insert.X + spanU * BinaGorunusScale * 0.5;
                    DrawKesitTitleBelowSchematic(tr, ms, db, BinaGorunusTitle, cx, insert.Y - BinaGorunusTitleGapCm);
                    DrawBinaGorunusFloorKots(tr, ms, db, insert.X, insert.Y, uMin, zMin, spanU);
                    DrawBinaGorunusFloorNameLabels(tr, ms, db, insert.X, insert.Y, zMin);

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
            double insertX, double insertY, double zMin)
        {
            if (_model?.Floors == null || _model.Floors.Count == 0) return;
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
                double y = insertY + (zFloor - zMin) * BinaGorunusScale - 4.0;
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
            double insertX, double insertY, double uMin, double zMin, double spanU)
        {
            var raw = new List<double> { _model.BuildingBaseKotu * 100.0 };
            foreach (var floor in _model.Floors)
                raw.Add((_model.BuildingBaseKotu + floor.ElevationM) * 100.0);
            var zsAsc = MergeKesitElevationZsAscending(raw, KesitKotMergeTolCm);
            if (zsAsc == null || zsAsc.Count == 0) return;

            double minSepModel = KesitKotCrowdedSeparationMinCm / BinaGorunusScale;
            double[] crowded = BuildKesitKotCrowdedShiftForLowerElevation(
                zsAsc, minSepModel, KesitKotCrowdedShiftLowerCm);
            ObjectId styleId = GetOrCreateYaziBeykentTextStyle(tr, db);
            double xRight = insertX + spanU * BinaGorunusScale;
            double xDatumBase = xRight + KesitKotDatumGapFromSectionCm + 35.0;
            const double rot = 0.0;

            for (int zi = zsAsc.Count - 1; zi >= 0; zi--)
            {
                double zElev = zsAsc[zi];
                double extra = zi < crowded.Length ? crowded[zi] : 0.0;
                double apexX = xDatumBase + extra;
                double apexY = insertY + (zElev - zMin) * BinaGorunusScale;
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

            Geometry solid = plan;
            if (!solid.IsValid)
            {
                try { solid = solid.Buffer(0); }
                catch { return; }
            }
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

        private static Geometry BinaGorunusPolygonal(Geometry g)
        {
            if (g == null || g.IsEmpty) return null;
            try
            {
                if (!g.IsValid)
                    g = g.Buffer(0);
            }
            catch
            {
                return null;
            }
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
