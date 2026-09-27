using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Dış köşe ölçüleri aynı hizada, en dış çizgiden 50 cm.
    /// SÜREKLİ: TEMEL katmanı poligon segmentleri, içeri 50 cm.
    /// </summary>
    internal static class PlanOlcuRunner
    {
        private const double MergeTolCm = 0.5;
        private const double OutGapCm = 50.0;
        private const double MinEdgeCm = 15.0;
        private const double MinSegCm = 0.4;
        private const double ColinearDeg = 1.0;
        private const double SectionScanCm = 2.0;
        private const double SectionTolCm = 3.0;
        private const double PairGapCm = 20.0;
        /// <summary>İç ölçü: poligon segmentinden içeri ofset (cm).</summary>
        private const double BeamDimGapCm = 50.0;
        /// <summary>Yedek; gerçek eşik PlanOlcuSettings.BandWidthCm.</summary>
        private const double MinZoneCmDefault = 15.0;
        /// <summary>Zon üretimi için sabit min; form bant genişliği yalnız normal bantlara uygulanır.</summary>
        private static double MinZoneCm => MinZoneCmDefault;

        private static double NormalBandMinCm()
        {
            double v = PlanOlcuSettings.BandWidthCm;
            return v > 0.5 ? v : MinZoneCmDefault;
        }

        public static void RunInteractive()
        {
            PlanOlcuSettings.EnsureLoaded();
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                PlanOlcuForm.ShowAfterPick();
                return;
            }
            Editor ed = doc.Editor;

            try
            {
                var layers = PlanOlcuSettings.GetLayers();
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string s in layers)
                {
                    string t = (s ?? "").Trim();
                    if (t.Length > 0) set.Add(t);
                }
                // İç ölçü (radye/mantar/sürekli) için katman şart.
                if ((IsSectionPreset() || IsSurekliPreset()) && set.Count == 0)
                {
                    ed.WriteMessage("\nPLANOLCU: Ölçülendirilecek katman seçin.");
                    return;
                }

                if (IsSurekliPreset())
                    ed.WriteMessage(
                        "\nPLANOLCU [SÜREKLİ TEMEL]: dış + TEMEL poligon segmentleri (içeri 50 cm). Bitirmek için Esc veya sağ tık.");
                else
                    ed.WriteMessage("\nPLANOLCU: Kat veya temel sınırı. Bitirmek için Esc veya sağ tık.");

                while (true)
                {
                    var peo = new PromptEntityOptions("\nKat veya temel sınırı poligonunu seçin: ")
                    {
                        AllowNone = true
                    };
                    peo.SetRejectMessage("\nKapalı poligon seçin.");
                    peo.AddAllowedClass(typeof(Polyline), true);
                    peo.AddAllowedClass(typeof(Polyline2d), true);

                    PromptEntityResult per = ed.GetEntity(peo);
                    if (per.Status != PromptStatus.OK)
                        break;

                    List<ObjectId> voidIds = null;
                    if (IsSectionPreset())
                        voidIds = PickVoidPolygons(ed, per.ObjectId);
                    PlaceForPolygon(doc, ed, per.ObjectId, set, voidIds);
                }
            }
            finally
            {
                PlanOlcuForm.ShowAfterPick();
            }
        }

        private static void PlaceForPolygon(
            Document doc,
            Editor ed,
            ObjectId polyId,
            HashSet<string> layers,
            List<ObjectId> voidIds)
        {
            Database db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (!(tr.GetObject(polyId, OpenMode.ForRead) is Entity polyEnt))
                {
                    ed.WriteMessage("\nPLANOLCU: Poligon okunamadı.");
                    tr.Commit();
                    return;
                }

                var ring = ReadRing(tr, polyEnt);
                ring = CleanRing(ring);
                if (ring.Count < 3)
                {
                    ed.WriteMessage("\nPLANOLCU: Kapalı poligon değil veya kenar sayısı yetersiz.");
                    tr.Commit();
                    return;
                }

                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                ObjectId dimStyleId = ResolveDimStyleId(tr, db, PlanOlcuSettings.DimStyleName);
                if (dimStyleId.IsNull)
                    ed.WriteMessage("\nPLANOLCU: '{0}' stili yok; aktif ölçü stili kullanılacak.", PlanOlcuSettings.DimStyleName);
                string dimLayer = EnsureOlcuLayer(tr, db);

                bool ccw = SignedArea(ring) > 0;
                var edges = BuildEdges(ring, ccw);
                int placedOut = 0;
                if (edges.Count >= 3)
                    placedOut = PlaceCornerDims(tr, btr, ring, edges, dimStyleId, dimLayer);

                if (IsSectionPreset())
                {
                    ed.WriteMessage(
                        "\nPLANOLCU [{0}]: dış + iç ölçü (X/Y). Bant çizilmez.",
                        PlanOlcuSettings.ActivePresetName);
                    var voids = new List<List<Point2d>>();
                    if (voidIds != null)
                    {
                        foreach (ObjectId vid in voidIds)
                        {
                            if (vid == polyId) continue;
                            if (!(tr.GetObject(vid, OpenMode.ForRead) is Entity ve)) continue;
                            var vr = CleanRing(ReadRing(tr, ve));
                            if (vr.Count >= 3) voids.Add(vr);
                        }
                    }
                    var segs = new List<Seg>();
                    var circles = new List<Circ>();
                    CollectSegments(tr, btr, layers, segs, circles, polyId);
                    int xDims, yDims;
                    DrawSectionBands(
                        tr, btr, ring, voids, segs, circles, dimStyleId, dimLayer, out xDims, out yDims);
                    ed.WriteMessage(
                        "\nPLANOLCU: {0} dış ölçü (50 cm), {1} yatay + {2} düşey iç ölçü.",
                        placedOut, xDims, yDims);
                }
                else if (IsSurekliPreset())
                {
                    int xDims, yDims;
                    int n = DrawSurekliInteriorDims(
                        tr, btr, ring, polyId, layers,
                        dimStyleId, dimLayer, out xDims, out yDims);
                    if (n == 0)
                        ed.WriteMessage(
                            "\nPLANOLCU [SÜREKLİ TEMEL]: {0} dış ölçü. İç ölçü yok — TEMEL katmanında sınır içi poligon yok.",
                            placedOut);
                    else
                        ed.WriteMessage(
                            "\nPLANOLCU [SÜREKLİ TEMEL]: {0} dış, {1} yatay + {2} düşey iç (çift: sol/alt).",
                            placedOut, xDims, yDims);
                }
                else
                    ed.WriteMessage("\nPLANOLCU: {0} dış ölçü (50 cm).", placedOut);

                tr.Commit();
            }
        }

        private static bool IsSectionPreset()
        {
            // RADYE TEMEL ve MANTAR KALIP: iç X/Y ölçü
            return IsRadyePreset() || IsMantarPreset();
        }

        private static bool IsSurekliPreset()
        {
            string n = PlanOlcuSettings.ActivePresetName ?? "";
            // Türkçe İ/I farkı: ASCII'ye indirgeme
            string ascii = n
                .Replace('İ', 'I').Replace('ı', 'i')
                .Replace('Ü', 'U').Replace('ü', 'u')
                .Replace('Ö', 'O').Replace('ö', 'o')
                .Replace('Ş', 'S').Replace('ş', 's')
                .Replace('Ç', 'C').Replace('ç', 'c')
                .Replace('Ğ', 'G').Replace('ğ', 'g');
            return ascii.IndexOf("SUREKLI", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsRadyePreset()
        {
            string n = PlanOlcuSettings.ActivePresetName ?? "";
            return n.IndexOf("RADYE", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsMantarPreset()
        {
            string n = PlanOlcuSettings.ActivePresetName ?? "";
            return n.IndexOf("MANTAR", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<ObjectId> PickVoidPolygons(Editor ed, ObjectId outerId)
        {
            var ids = new List<ObjectId>();
            if (IsMantarPreset())
                ed.WriteMessage(
                    "\nMantar döşeme: poligon içi boşluk/açıklıkları gösterin. Bitirmek için Esc.");
            else
                ed.WriteMessage(
                    "\nPoligon içi boşlukları gösterin (asansör çukuru vb.). Bitirmek için Esc.");
            while (true)
            {
                var peo = new PromptEntityOptions(
                    IsMantarPreset()
                        ? "\nBoşluk / açıklık poligonu (bitir: Esc): "
                        : "\nBoşluk poligonu (bitir: Esc): ")
                {
                    AllowNone = true
                };
                peo.SetRejectMessage("\nKapalı poligon seçin.");
                peo.AddAllowedClass(typeof(Polyline), true);
                peo.AddAllowedClass(typeof(Polyline2d), true);
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    break;
                if (per.ObjectId == outerId)
                {
                    ed.WriteMessage("\nDış sınır seçildi; boşluk olarak eklenmedi.");
                    continue;
                }
                if (ids.Contains(per.ObjectId))
                {
                    ed.WriteMessage("\nBu poligon zaten seçildi.");
                    continue;
                }
                ids.Add(per.ObjectId);
                ed.WriteMessage("\n  + boşluk eklendi ({0}).", ids.Count);
            }
            return ids;
        }

        /// <summary>
        /// Sürekli iç ölçü: sınır içi TEMEL bay poligonları.
        /// Aynı hiza/açıdaki segmentler birleştirilir (aradaki boşluk da zincire girer).
        /// </summary>
        private static int DrawSurekliInteriorDims(
            Transaction tr, BlockTableRecord btr,
            List<Point2d> ring, ObjectId skipId, HashSet<string> layers,
            ObjectId dimStyleId, string dimLayer,
            out int xDimCount, out int yDimCount)
        {
            xDimCount = 0;
            yDimCount = 0;
            if (ring == null || ring.Count < 3 || layers == null || layers.Count == 0)
                return 0;

            List<List<Point2d>> polys = CollectClosedPolygonsInside(tr, btr, layers, ring, skipId);
            if (polys.Count == 0) return 0;

            const double minPolyThicknessCm = BeamDimGapCm * 2.0 + 10.0; // ~110
            var cands = new List<InteriorDimCand>();

            foreach (List<Point2d> poly in polys)
            {
                if (poly == null || poly.Count < 3) continue;
                RingBounds(poly, out double minX, out double minY, out double maxX, out double maxY);
                if ((maxX - minX) < minPolyThicknessCm || (maxY - minY) < minPolyThicknessCm)
                    continue;

                bool ccw = SignedArea(poly) > 0;
                List<Edge> edges = BuildEdges(poly, ccw);
                foreach (Edge e in edges)
                {
                    if (e.Length < MinEdgeCm) continue;
                    Point2d mid = new Point2d((e.A.X + e.B.X) * 0.5, (e.A.Y + e.B.Y) * 0.5);
                    Vector2d inward = e.Inward;
                    if (!TryInwardDimPoint(poly, ring, mid, e.Dir, ref inward, out Point2d dim2))
                        continue;

                    Vector2d along = e.Dir;
                    Point2d a = e.A;
                    Point2d b = e.B;
                    if (along.X < -1e-9 || (Math.Abs(along.X) < 1e-9 && along.Y < 0))
                    {
                        along = -along;
                        a = e.B;
                        b = e.A;
                    }
                    cands.Add(new InteriorDimCand
                    {
                        A = a,
                        B = b,
                        DimAt = dim2,
                        Along = along,
                        Rotation = Math.Atan2(along.Y, along.X)
                    });
                }
            }

            if (cands.Count == 0) return 0;

            List<Point2d> snapVerts = CollectExtensionSnapVertices(
                tr, btr, layers, ring, skipId, strictLayerList: false);

            List<Point2d> snapVertsCift = null;
            if (PlanOlcuSettings.PlaceCiftOlcu)
            {
                var ciftSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string s in PlanOlcuSettings.GetCiftLayers())
                {
                    string t = (s ?? "").Trim();
                    if (t.Length > 0) ciftSet.Add(t);
                }
                if (ciftSet.Count > 0)
                    snapVertsCift = CollectExtensionSnapVertices(
                        tr, btr, ciftSet, ring, skipId, strictLayerList: true);
            }

            return PlaceMergedInteriorDims(
                tr, btr, ring, snapVerts, snapVertsCift, cands,
                dimStyleId, dimLayer, out xDimCount, out yDimCount);
        }

        private const double OnBoundaryTolCm = 2.0;

        private static bool IsPerdeLayerName(string lyr)
        {
            if (string.IsNullOrWhiteSpace(lyr)) return false;
            string u = lyr.Trim().ToUpperInvariant()
                .Replace('İ', 'I').Replace('ı', 'I');
            return u.IndexOf("PERDE", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Snap vertex'leri. strictLayerList=true: yalnız listedeki katmanlar (PERDE otomatik eklenmez).
        /// </summary>
        private static List<Point2d> CollectExtensionSnapVertices(
            Transaction tr, BlockTableRecord btr, HashSet<string> layers,
            List<Point2d> outer, ObjectId skipId, bool strictLayerList)
        {
            var verts = new List<Point2d>();
            if (outer == null || outer.Count < 3) return verts;

            // Sınır köşeleri her zaman (TEMEL SINIRI) — perde değil
            foreach (Point2d p in outer)
                TryAddUniqueVertex(verts, p);

            if (btr == null || layers == null || layers.Count == 0) return verts;

            var wantLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string s in layers)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                wantLayers.Add(s.Trim());
            }

            if (!strictLayerList)
            {
                // Birinci ölçü: kolon/perde varsayılan (sınır üzeri)
                wantLayers.Add("KOLON (BEYKENT)");
                wantLayers.Add("KOLON");
                wantLayers.Add("PERDE (BEYKENT)");
                wantLayers.Add("PERDE");
            }

            bool listHasPerde = LayerListHasPerde(layers);

            foreach (ObjectId id in btr)
            {
                if (id == skipId) continue;
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                if (ent is Dimension || ent is DBText || ent is MText || ent is Hatch)
                    continue;

                string lyr = (ent.Layer ?? "").Trim();
                if (!wantLayers.Contains(lyr)) continue;

                // strict: listede yoksa zaten continue; perde yalnız listede varsa
                bool isPerde = IsPerdeLayerName(lyr);
                if (strictLayerList && isPerde && !listHasPerde)
                    continue;

                bool requireOnBoundary = !isPerde || !listHasPerde;
                if (strictLayerList)
                {
                    // İkinci katman: listedeki her katman (sınır şartı yok; içerde kalsın)
                    requireOnBoundary = false;
                }

                AddEntityVertices(tr, ent, lyr, verts, outer, requireOnBoundary);
            }
            return verts;
        }

        private static bool LayerListHasPerde(HashSet<string> layers)
        {
            if (layers == null) return false;
            foreach (string s in layers)
                if (IsPerdeLayerName(s)) return true;
            return false;
        }

        private static void AddEntityVertices(
            Transaction tr, Entity ent, string lyr, List<Point2d> verts,
            List<Point2d> outer, bool requireOnBoundary)
        {
            if (ent is Polyline || ent is Polyline2d)
            {
                List<Point2d> raw = CleanRing(ReadRing(tr, ent));
                if (raw != null && raw.Count > 0)
                {
                    foreach (Point2d p in raw)
                        TryAddSnapVertex(verts, p, outer, requireOnBoundary);
                    return;
                }
                var segs = new List<Seg>();
                var circ = new List<Circ>();
                var one = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { lyr };
                try { CollectEntity(tr, ent, one, segs, circ, 0, lyr); }
                catch { }
                foreach (Seg s in segs)
                {
                    TryAddSnapVertex(verts, s.A, outer, requireOnBoundary);
                    TryAddSnapVertex(verts, s.B, outer, requireOnBoundary);
                }
            }
            else if (ent is Line ln)
            {
                TryAddSnapVertex(verts, new Point2d(ln.StartPoint.X, ln.StartPoint.Y), outer, requireOnBoundary);
                TryAddSnapVertex(verts, new Point2d(ln.EndPoint.X, ln.EndPoint.Y), outer, requireOnBoundary);
            }
        }

        private static void TryAddSnapVertex(
            List<Point2d> verts, Point2d p, List<Point2d> outer, bool requireOnBoundary)
        {
            if (outer == null || outer.Count < 3) return;
            // Dışarı taşma yok — yalnız sınır üzeri veya içi
            if (!PointInRing(p, outer) && DistanceToRing(p, outer) > OnBoundaryTolCm)
                return;
            if (requireOnBoundary && DistanceToRing(p, outer) > OnBoundaryTolCm)
                return;
            TryAddUniqueVertex(verts, p);
        }

        private static void TryAddUniqueVertex(List<Point2d> verts, Point2d p)
        {
            for (int i = 0; i < verts.Count; i++)
            {
                if (verts[i].GetDistanceTo(p) <= MergeTolCm)
                    return;
            }
            verts.Add(p);
        }

        private struct InteriorDimCand
        {
            public Point2d A;
            public Point2d B;
            public Point2d DimAt;
            public Vector2d Along;
            public double Rotation;
        }

        /// <summary>
        /// Aynı hiza birleşir. Çift ölçü: sola/alta ikinci hat (ikinci katman vertex'leri).
        /// </summary>
        private static int PlaceMergedInteriorDims(
            Transaction tr, BlockTableRecord btr,
            List<Point2d> outer,
            List<Point2d> snapVerts,
            List<Point2d> snapVertsCift,
            List<InteriorDimCand> cands,
            ObjectId dimStyleId, string dimLayer,
            out int xDimCount, out int yDimCount)
        {
            xDimCount = 0;
            yDimCount = 0;
            if (cands == null || cands.Count == 0) return 0;

            const double angTolDeg = 2.0;
            const double lineTolCm = 8.0;
            var used = new bool[cands.Count];
            int n = 0;
            Point2d centroid = RingCentroid(outer);
            bool wantCift = PlanOlcuSettings.PlaceCiftOlcu
                && snapVertsCift != null && snapVertsCift.Count > 0;

            for (int i = 0; i < cands.Count; i++)
            {
                if (used[i]) continue;
                InteriorDimCand seed = cands[i];
                Vector2d along = seed.Along;
                double alen = along.Length;
                if (alen < 1e-9) { used[i] = true; continue; }
                along = along / alen;
                Vector2d perp = new Vector2d(-along.Y, along.X);
                double seedLine = Dot(seed.DimAt, perp);

                var group = new List<int> { i };
                used[i] = true;
                for (int j = i + 1; j < cands.Count; j++)
                {
                    if (used[j]) continue;
                    InteriorDimCand o = cands[j];
                    double ang = AngleBetweenDirs(along, o.Along) * 180.0 / Math.PI;
                    if (ang > angTolDeg && ang < 180.0 - angTolDeg) continue;
                    double line = Dot(o.DimAt, perp);
                    if (Math.Abs(line - seedLine) > lineTolCm) continue;
                    used[j] = true;
                    group.Add(j);
                }

                double lineSum = 0;
                foreach (int gi in group)
                    lineSum += Dot(cands[gi].DimAt, perp);
                double linePos = lineSum / group.Count;

                var stations = BuildStationsForGroup(
                    cands, group, along, snapVerts, outer);
                EnsureEndsOnTemelSiniri(stations, outer, along);
                stations.Sort((p, q) => p.T.CompareTo(q.T));

                double rotation = Math.Atan2(along.Y, along.X);
                bool horiz = Math.Abs(along.X) >= Math.Abs(along.Y);

                int placed = PlaceStationDims(
                    tr, btr, stations, outer, along, perp, linePos, rotation, horiz,
                    dimStyleId, dimLayer, allowDimOutside: false,
                    ref xDimCount, ref yDimCount);
                n += placed;

                // Çift ölçü: yalnız sol (düşey hat) ve alt (yatay hat) — ikinci katman
                if (!wantCift || stations.Count < 2) continue;
                bool isLeft = !horiz && seed.DimAt.X < centroid.X;
                bool isBottom = horiz && seed.DimAt.Y < centroid.Y;
                if (!isLeft && !isBottom) continue;

                double linePos2 = OuterCiftLinePos(linePos, along, perp, centroid);
                var stationsCift = BuildStationsForGroup(
                    cands, group, along, snapVertsCift, outer);
                // Finiş birebir 1. ölçü uçları (TEMEL SINIRI) — farklı vertex olmasın
                ForceIdenticalEnds(stations, stationsCift, along);

                n += PlaceStationDims(
                    tr, btr, stationsCift, outer, along, perp, linePos2, rotation, horiz,
                    dimStyleId, dimLayer, allowDimOutside: true,
                    ref xDimCount, ref yDimCount);
            }
            return n;
        }

        private static List<CornerStation> BuildStationsForGroup(
            List<InteriorDimCand> cands, List<int> group, Vector2d along,
            List<Point2d> snapVerts, List<Point2d> outer)
        {
            var stations = new List<CornerStation>();
            foreach (int gi in group)
            {
                InteriorDimCand c = cands[gi];
                AddAlongStation(stations, c.A, along);
                AddAlongStation(stations, c.B, along);
            }
            InsertAlignedSnapVertices(stations, snapVerts, along, outer);
            AppendAdjacentVertexStations(stations, outer, snapVerts, along);
            ClampStationsToOuter(stations, outer, along);
            SnapNearOuterToVertex(stations, outer, along);
            MergeNearStations(stations, 12.0, outer, along);
            return stations;
        }

        /// <summary>
        /// 2. ölçü uçları = 1. ölçünün TEMEL SINIRI finiş vertex'leri (birebir aynı nokta).
        /// </summary>
        private static void ForceIdenticalEnds(
            List<CornerStation> primary, List<CornerStation> cift, Vector2d along)
        {
            if (primary == null || primary.Count < 2 || cift == null) return;
            primary.Sort((p, q) => p.T.CompareTo(q.T));
            Point2d left = primary[0].P;
            Point2d right = primary[primary.Count - 1].P;
            double tL = Dot(left, along);
            double tR = Dot(right, along);
            if (tL > tR)
            {
                Point2d tmpP = left; left = right; right = tmpP;
                double tmpT = tL; tL = tR; tR = tmpT;
            }

            const double endTol = 12.0;
            for (int i = cift.Count - 1; i >= 0; i--)
            {
                double t = cift[i].T;
                if (t < tL - MergeTolCm || t > tR + MergeTolCm
                    || Math.Abs(t - tL) <= endTol || Math.Abs(t - tR) <= endTol)
                    cift.RemoveAt(i);
            }

            cift.Add(new CornerStation { T = tL, P = left });
            cift.Add(new CornerStation { T = tR, P = right });
            cift.Sort((p, q) => p.T.CompareTo(q.T));
        }

        /// <summary>
        /// Zincir uçlarını TEMEL SINIRI üzerindeki en yakın vertex'e bağlar (finiş).
        /// </summary>
        private static void EnsureEndsOnTemelSiniri(
            List<CornerStation> stations, List<Point2d> outer, Vector2d along)
        {
            if (stations == null || stations.Count == 0 || outer == null || outer.Count < 3)
                return;

            stations.Sort((p, q) => p.T.CompareTo(q.T));
            Point2d leftEnd = stations[0].P;
            Point2d rightEnd = stations[stations.Count - 1].P;
            double tMin = stations[0].T;
            double tMax = stations[stations.Count - 1].T;
            const double maxGap = 200.0;

            if (TryNearestVertex(outer, outer, along, leftEnd, tMin, towardMin: true, maxGap, out Point2d vL))
                AddOuterStationPreferNear(stations, vL, along, leftEnd);
            else if (TryClosestOuterVertex(outer, leftEnd, along, preferMinT: true, tMin, maxGap, out vL))
                AddOuterStationPreferNear(stations, vL, along, leftEnd);

            if (TryNearestVertex(outer, outer, along, rightEnd, tMax, towardMin: false, maxGap, out Point2d vR))
                AddOuterStationPreferNear(stations, vR, along, rightEnd);
            else if (TryClosestOuterVertex(outer, rightEnd, along, preferMinT: false, tMax, maxGap, out vR))
                AddOuterStationPreferNear(stations, vR, along, rightEnd);

            MergeNearStations(stations, 12.0, outer, along);
        }

        private static bool TryClosestOuterVertex(
            List<Point2d> outer, Point2d fromEnd, Vector2d along,
            bool preferMinT, double tEnd, double maxGapCm, out Point2d best)
        {
            best = default(Point2d);
            double bestDist = double.MaxValue;
            bool found = false;
            foreach (Point2d v in outer)
            {
                double t = Dot(v, along);
                if (preferMinT)
                {
                    if (t > tEnd + MergeTolCm) continue;
                    if (tEnd - t > maxGapCm) continue;
                }
                else
                {
                    if (t < tEnd - MergeTolCm) continue;
                    if (t - tEnd > maxGapCm) continue;
                }
                double d = fromEnd.GetDistanceTo(v);
                if (d >= bestDist) continue;
                bestDist = d;
                best = v;
                found = true;
            }
            return found;
        }

        /// <summary>Çift hat: birinci ölçüden PairGapCm (20) dışarı; vertex ofseti (50) aynı kalır.</summary>
        private static double OuterCiftLinePos(
            double linePos, Vector2d along, Vector2d perp, Point2d centroid)
        {
            Point2d mid = new Point2d(
                along.X * Dot(centroid, along) + perp.X * linePos,
                along.Y * Dot(centroid, along) + perp.Y * linePos);
            Point2d plus = new Point2d(mid.X + perp.X * PairGapCm, mid.Y + perp.Y * PairGapCm);
            Point2d minus = new Point2d(mid.X - perp.X * PairGapCm, mid.Y - perp.Y * PairGapCm);
            double dPlus = plus.GetDistanceTo(centroid);
            double dMinus = minus.GetDistanceTo(centroid);
            return dPlus >= dMinus ? linePos + PairGapCm : linePos - PairGapCm;
        }

        private static int PlaceStationDims(
            Transaction tr, BlockTableRecord btr,
            List<CornerStation> stations, List<Point2d> outer,
            Vector2d along, Vector2d perp, double linePos, double rotation, bool horiz,
            ObjectId dimStyleId, string dimLayer, bool allowDimOutside,
            ref int xDimCount, ref int yDimCount)
        {
            int n = 0;
            if (stations == null || stations.Count < 2) return 0;
            for (int s = 0; s < stations.Count - 1; s++)
            {
                Point2d a = stations[s].P;
                Point2d b = stations[s + 1].P;
                if (a.GetDistanceTo(b) < MinEdgeCm) continue;
                if (!IsInsideOrOnOuter(a, outer) || !IsInsideOrOnOuter(b, outer))
                    continue;

                Point2d mid = new Point2d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
                double tMid = Dot(mid, along);
                Point2d dim2 = new Point2d(
                    along.X * tMid + perp.X * linePos,
                    along.Y * tMid + perp.Y * linePos);
                if (!allowDimOutside && !IsInsideOrOnOuter(dim2, outer))
                {
                    dim2 = new Point2d(mid.X, mid.Y);
                    if (!IsInsideOrOnOuter(dim2, outer))
                        continue;
                }
                if (!TryPlaceInterior(tr, btr, a, b, To3(dim2), rotation, dimStyleId, dimLayer))
                    continue;
                n++;
                if (horiz) xDimCount++;
                else yDimCount++;
            }
            return n;
        }

        /// <summary>
        /// Ölçü hizasındaki snap vertex'leri — TEMEL SINIRI dışındakiler alınmaz.
        /// </summary>
        private static void InsertAlignedSnapVertices(
            List<CornerStation> stations, List<Point2d> snapVerts, Vector2d along, List<Point2d> outer)
        {
            if (stations == null || stations.Count == 0 || snapVerts == null) return;

            bool horiz = Math.Abs(along.X) >= Math.Abs(along.Y);
            double sum = 0;
            foreach (CornerStation st in stations)
                sum += horiz ? st.P.Y : st.P.X;
            double row = sum / stations.Count;
            const double rowTolCm = 40.0;

            stations.Sort((p, q) => p.T.CompareTo(q.T));
            double tMin = stations[0].T - 200.0;
            double tMax = stations[stations.Count - 1].T + 200.0;
            GetOuterSpanOnRow(outer, along, horiz, row, out double outerT0, out double outerT1);

            foreach (Point2d v in snapVerts)
            {
                if (outer != null && outer.Count >= 3
                    && !PointInRing(v, outer) && DistanceToRing(v, outer) > OnBoundaryTolCm)
                    continue;
                double rowV = horiz ? v.Y : v.X;
                if (Math.Abs(rowV - row) > rowTolCm) continue;
                double t = Dot(v, along);
                if (t < tMin || t > tMax) continue;
                if (!double.IsNaN(outerT0) && t < outerT0 - MergeTolCm) continue;
                if (!double.IsNaN(outerT1) && t > outerT1 + MergeTolCm) continue;
                AddAlongStation(stations, v, along);
            }
        }

        /// <summary>Verilen sırada dış sınır kirişinin along aralığı [t0,t1].</summary>
        private static void GetOuterSpanOnRow(
            List<Point2d> outer, Vector2d along, bool horiz, double row,
            out double t0, out double t1)
        {
            t0 = double.NaN;
            t1 = double.NaN;
            if (outer == null || outer.Count < 3) return;
            List<double[]> chords = horiz
                ? HorizontalChords(outer, row)
                : VerticalChords(outer, row);
            if (chords == null || chords.Count == 0) return;
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (double[] ch in chords)
            {
                if (ch == null || ch.Length < 2) continue;
                Point2d p0 = horiz ? new Point2d(ch[0], row) : new Point2d(row, ch[0]);
                Point2d p1 = horiz ? new Point2d(ch[1], row) : new Point2d(row, ch[1]);
                double a = Dot(p0, along);
                double b = Dot(p1, along);
                if (a > b) { double tmp = a; a = b; b = tmp; }
                if (a < lo) lo = a;
                if (b > hi) hi = b;
            }
            if (lo <= hi) { t0 = lo; t1 = hi; }
        }

        /// <summary>İstasyonları TEMEL SINIRI dışına taşımaz.</summary>
        private static void ClampStationsToOuter(
            List<CornerStation> stations, List<Point2d> outer, Vector2d along)
        {
            if (stations == null || stations.Count == 0 || outer == null || outer.Count < 3)
                return;

            bool horiz = Math.Abs(along.X) >= Math.Abs(along.Y);
            double sum = 0;
            foreach (CornerStation st in stations)
                sum += horiz ? st.P.Y : st.P.X;
            double row = sum / Math.Max(stations.Count, 1);
            GetOuterSpanOnRow(outer, along, horiz, row, out double t0, out double t1);
            if (double.IsNaN(t0) || double.IsNaN(t1)) return;

            for (int i = stations.Count - 1; i >= 0; i--)
            {
                CornerStation st = stations[i];
                double t = st.T;
                bool outsideRing = !PointInRing(st.P, outer) && DistanceToRing(st.P, outer) > OnBoundaryTolCm;
                bool outsideSpan = t < t0 - MergeTolCm || t > t1 + MergeTolCm;
                if (!outsideRing && !outsideSpan) continue;

                double tc = t;
                if (t < t0) tc = t0;
                else if (t > t1) tc = t1;
                Point2d on = PointOnRow(along, horiz, row, tc);
                // Sınır köşesine daha yakın vertex varsa onu kullan
                Point2d best = on;
                double bestD = on.GetDistanceTo(st.P);
                foreach (Point2d ov in outer)
                {
                    double ot = Dot(ov, along);
                    if (ot < t0 - 1.0 || ot > t1 + 1.0) continue;
                    double d = ov.GetDistanceTo(st.P);
                    if (d < bestD) { bestD = d; best = ov; }
                }
                stations[i] = new CornerStation { T = Dot(best, along), P = best };
            }
        }

        private static Point2d PointOnRow(Vector2d along, bool horiz, double row, double t)
        {
            if (horiz)
            {
                double x = Math.Abs(along.X) > 0.5 ? t / along.X : t;
                return new Point2d(x, row);
            }
            double y = Math.Abs(along.Y) > 0.5 ? t / along.Y : t;
            return new Point2d(row, y);
        }

        private static bool IsInsideOrOnOuter(Point2d p, List<Point2d> outer)
        {
            if (outer == null || outer.Count < 3) return true;
            if (PointInRing(p, outer)) return true;
            return DistanceToRing(p, outer) <= OnBoundaryTolCm;
        }

        /// <summary>Sınıra yakın istasyon → en yakın dış vertex (2/7.5 artık boşluk yok).</summary>
        private static void SnapNearOuterToVertex(
            List<CornerStation> stations, List<Point2d> outer, Vector2d along)
        {
            if (stations == null || outer == null || outer.Count < 3) return;
            const double snapCm = 15.0;
            for (int i = 0; i < stations.Count; i++)
            {
                Point2d p = stations[i].P;
                if (DistanceToRing(p, outer) > snapCm) continue;
                Point2d best = p;
                double bestD = double.MaxValue;
                foreach (Point2d ov in outer)
                {
                    double d = ov.GetDistanceTo(p);
                    if (d < bestD) { bestD = d; best = ov; }
                }
                if (bestD <= snapCm)
                    stations[i] = new CornerStation { T = Dot(best, along), P = best };
            }
        }

        /// <summary>Yakın istasyonları birleştir; sınıra daha yakın vertex kalsın.</summary>
        private static void MergeNearStations(
            List<CornerStation> stations, double tolCm, List<Point2d> outer, Vector2d along)
        {
            if (stations == null || stations.Count < 2) return;
            stations.Sort((p, q) => p.T.CompareTo(q.T));
            for (int i = stations.Count - 1; i > 0; i--)
            {
                bool close = stations[i].P.GetDistanceTo(stations[i - 1].P) <= tolCm
                    || Math.Abs(stations[i].T - stations[i - 1].T) <= tolCm;
                if (!close) continue;

                Point2d a = stations[i - 1].P;
                Point2d b = stations[i].P;
                Point2d keep = a;
                if (outer != null && outer.Count >= 3)
                {
                    double da = DistanceToRing(a, outer);
                    double db = DistanceToRing(b, outer);
                    keep = da <= db ? a : b;
                }
                stations[i - 1] = new CornerStation { T = Dot(keep, along), P = keep };
                stations.RemoveAt(i);
            }
        }

        /// <summary>
        /// Dışa komşu uç → en yakın sınır/perde vertex (sınır dışına çıkmaz).
        /// </summary>
        private static void AppendAdjacentVertexStations(
            List<CornerStation> stations, List<Point2d> outer,
            List<Point2d> snapVerts, Vector2d along)
        {
            if (stations == null || stations.Count == 0) return;
            if (snapVerts == null || snapVerts.Count == 0) return;

            const double maxOuterGapCm = 200.0;

            stations.Sort((p, q) => p.T.CompareTo(q.T));
            Point2d leftEnd = stations[0].P;
            Point2d rightEnd = stations[stations.Count - 1].P;
            double tMin = stations[0].T;
            double tMax = stations[stations.Count - 1].T;

            bool leftNearOuter = outer == null || outer.Count < 3
                || DistanceToRing(leftEnd, outer) <= maxOuterGapCm;
            bool rightNearOuter = outer == null || outer.Count < 3
                || DistanceToRing(rightEnd, outer) <= maxOuterGapCm;

            if (leftNearOuter
                && TryNearestVertex(snapVerts, outer, along, leftEnd, tMin, towardMin: true, maxOuterGapCm, out Point2d vL))
                AddOuterStationPreferNear(stations, vL, along, leftEnd);
            if (rightNearOuter
                && TryNearestVertex(snapVerts, outer, along, rightEnd, tMax, towardMin: false, maxOuterGapCm, out Point2d vR))
                AddOuterStationPreferNear(stations, vR, along, rightEnd);
        }

        /// <summary>Aynı T'de birden fazla sınır noktası varsa uca en yakın olanı tut.</summary>
        private static void AddOuterStationPreferNear(
            List<CornerStation> stations, Point2d p, Vector2d along, Point2d fromEnd)
        {
            double t = Dot(p, along);
            for (int i = 0; i < stations.Count; i++)
            {
                if (Math.Abs(stations[i].T - t) > MergeTolCm) continue;
                // Aynı hizada: uca daha yakın vertex kalsın (en sol uç yerine kolon köşesi)
                if (p.GetDistanceTo(fromEnd) < stations[i].P.GetDistanceTo(fromEnd))
                    stations[i] = new CornerStation { T = t, P = p };
                return;
            }
            stations.Add(new CornerStation { T = t, P = p });
        }

        /// <summary>Ucun dışa bakan tarafında maxGap içindeki en yakın vertex.</summary>
        private static bool TryNearestVertex(
            List<Point2d> verts, List<Point2d> outer, Vector2d along, Point2d fromEnd,
            double tEnd, bool towardMin, double maxGapCm, out Point2d best)
        {
            best = default(Point2d);
            if (verts == null) return false;
            double bestDist = double.MaxValue;
            bool found = false;
            foreach (Point2d v in verts)
            {
                // TEMEL SINIRI dışına çıkma
                if (outer != null && outer.Count >= 3
                    && !PointInRing(v, outer) && DistanceToRing(v, outer) > OnBoundaryTolCm)
                    continue;
                double t = Dot(v, along);
                if (towardMin)
                {
                    if (t >= tEnd - MergeTolCm) continue;
                    if (tEnd - t > maxGapCm) continue;
                }
                else
                {
                    if (t <= tEnd + MergeTolCm) continue;
                    if (t - tEnd > maxGapCm) continue;
                }
                if (v.GetDistanceTo(fromEnd) <= MergeTolCm) continue;
                double d = fromEnd.GetDistanceTo(v);
                if (d >= bestDist) continue;
                bestDist = d;
                best = v;
                found = true;
            }
            return found;
        }

        private static double AngleBetweenDirs(Vector2d a, Vector2d b)
        {
            double la = a.Length;
            double lb = b.Length;
            if (la < 1e-12 || lb < 1e-12) return Math.PI;
            double c = Math.Abs(Dot(a, b) / (la * lb));
            if (c > 1.0) c = 1.0;
            return Math.Acos(c);
        }

        private static void AddAlongStation(List<CornerStation> stations, Point2d p, Vector2d along)
        {
            double t = Dot(p, along);
            for (int i = 0; i < stations.Count; i++)
            {
                if (Math.Abs(stations[i].T - t) <= MergeTolCm)
                {
                    Point2d q = stations[i].P;
                    stations[i] = new CornerStation
                    {
                        T = 0.5 * (stations[i].T + t),
                        P = new Point2d(0.5 * (q.X + p.X), 0.5 * (q.Y + p.Y))
                    };
                    return;
                }
            }
            stations.Add(new CornerStation { T = t, P = p });
        }

        /// <summary>
        /// Ölçü noktası: mid + inward·50; nokta hem poligon hem dış sınır içinde olmalı.
        /// </summary>
        private static bool TryInwardDimPoint(
            List<Point2d> poly, List<Point2d> outer,
            Point2d mid, Vector2d along, ref Vector2d inward, out Point2d dim2)
        {
            dim2 = default(Point2d);
            for (int flip = 0; flip < 2; flip++)
            {
                Vector2d dir = flip == 0 ? inward : -inward;
                Point2d cand = new Point2d(
                    mid.X + dir.X * BeamDimGapCm,
                    mid.Y + dir.Y * BeamDimGapCm);
                if (!PointStrictlyInside(cand, poly)) continue;
                if (!PointStrictlyInside(cand, outer)) continue;
                // Segment boyunca birkaç örnek — hepsi poligon içinde
                bool ok = true;
                for (int k = -2; k <= 2; k++)
                {
                    Point2d s = new Point2d(
                        cand.X + along.X * (k * 5.0),
                        cand.Y + along.Y * (k * 5.0));
                    if (!PointStrictlyInside(s, poly))
                    {
                        ok = false;
                        break;
                    }
                }
                if (!ok) continue;
                inward = dir;
                dim2 = cand;
                return true;
            }
            return false;
        }

        /// <summary>Sınır üstü sayılmaz; gerçekten içeride.</summary>
        private static bool PointStrictlyInside(Point2d p, List<Point2d> ring)
        {
            if (ring == null || ring.Count < 3) return false;
            if (DistanceToRing(p, ring) <= 0.5) return false;
            return PointInRingRaw(p, ring);
        }

        private static bool PointInRingRaw(Point2d p, List<Point2d> ring)
        {
            bool inside = false;
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Point2d pi = ring[i];
                Point2d pj = ring[j];
                if (((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                    (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / ((pj.Y - pi.Y) == 0 ? 1e-12 : (pj.Y - pi.Y)) + pi.X))
                    inside = !inside;
            }
            return inside;
        }

        private static void RingBounds(
            List<Point2d> ring, out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = maxX = ring[0].X;
            minY = maxY = ring[0].Y;
            for (int i = 1; i < ring.Count; i++)
            {
                Point2d p = ring[i];
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
        }

        /// <summary>
        /// TEMEL sınırı içinde (tüm köşeler içeride), seçili katman kapalı poligonları.
        /// Dış sınır ve sınır dışı elemanlar alınmaz.
        /// </summary>
        private static List<List<Point2d>> CollectClosedPolygonsInside(
            Transaction tr, BlockTableRecord btr, HashSet<string> layers,
            List<Point2d> outer, ObjectId skipId)
        {
            var result = new List<List<Point2d>>();
            if (btr == null || layers == null || layers.Count == 0) return result;
            double outerArea = Math.Abs(SignedArea(outer));

            foreach (ObjectId id in btr)
            {
                if (id == skipId) continue;
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                if (ent is Dimension || ent is DBText || ent is MText || ent is Hatch)
                    continue;
                if (!(ent is Polyline) && !(ent is Polyline2d))
                    continue;

                string lyr = (ent.Layer ?? "").Trim();
                if (!layers.Contains(lyr)) continue;

                // Kapalı olmalı
                if (ent is Polyline pl && !pl.Closed
                    && pl.StartPoint.DistanceTo(pl.EndPoint) > 1.0)
                    continue;
                if (ent is Polyline2d pl2 && !pl2.Closed)
                    continue;

                List<Point2d> raw = CleanRing(ReadRing(tr, ent));
                if (raw == null || raw.Count < 3) continue;

                double area = Math.Abs(SignedArea(raw));
                if (area < MinEdgeCm * MinEdgeCm) continue;
                if (outerArea > 1.0 && area > outerArea * 0.92) continue;

                // Tüm köşeler dış sınırın içinde / üzerinde — kısmi örtüşen dış eleman yok
                bool allIn = true;
                foreach (Point2d p in raw)
                {
                    if (PointInRing(p, outer) || DistanceToRing(p, outer) <= 1.0)
                        continue;
                    allIn = false;
                    break;
                }
                if (!allIn) continue;

                // Merkez de içeride
                Point2d c = RingCentroid(raw);
                if (!PointInRing(c, outer)) continue;

                result.Add(raw);
            }
            return result;
        }

        private static Point2d RingCentroid(List<Point2d> ring)
        {
            double sx = 0, sy = 0;
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                sx += ring[i].X;
                sy += ring[i].Y;
            }
            return new Point2d(sx / Math.Max(n, 1), sy / Math.Max(n, 1));
        }

        private static string SegInwardSignature(Point2d a, Point2d b, Vector2d inward)
        {
            long ax = (long)Math.Round(a.X);
            long ay = (long)Math.Round(a.Y);
            long bx = (long)Math.Round(b.X);
            long by = (long)Math.Round(b.Y);
            if (ax > bx || (ax == bx && ay > by))
            {
                long tx = ax; ax = bx; bx = tx;
                long ty = ay; ay = by; by = ty;
            }
            long ix = (long)Math.Round(inward.X * 10.0);
            long iy = (long)Math.Round(inward.Y * 10.0);
            return ax + "," + ay + "|" + bx + "," + by + "|" + ix + "," + iy;
        }

        private struct SpanClaim
        {
            public string AbsSig;
            public double Coord;
        }

        /// <summary>
        /// Yatay: alt kenara H/6'dan yakınsa çizme. Düşey: sağ kenara W/6'dan yakınsa çizme.
        /// </summary>
        private static bool IsSafeVoidOffset(List<HatchBox> boxes, double axis, bool horizontal)
        {
            if (boxes == null || boxes.Count == 0) return true;
            bool any = false;
            foreach (HatchBox b in boxes)
            {
                if (horizontal)
                {
                    if (axis < b.MinY - 0.5 || axis > b.MaxY + 0.5) continue;
                    any = true;
                    double minInset = b.H / 6.0;
                    if (axis - b.MinY < minInset - 0.5) return false;
                }
                else
                {
                    if (axis < b.MinX - 0.5 || axis > b.MaxX + 0.5) continue;
                    any = true;
                    double minInset = b.W / 6.0;
                    if (b.MaxX - axis < minInset - 0.5) return false;
                }
            }
            return any;
        }

        /// <summary>
        /// Yatay: 1/6 altına inme → NaN (çizilmez). Düşey: sağ 1/6 dışına → NaN.
        /// </summary>
        private static double EnforceVoidOffset(List<HatchBox> boxes, double axis, bool horizontal)
        {
            if (boxes == null || boxes.Count == 0) return axis;
            bool any = false;
            if (horizontal)
            {
                double floor = double.MinValue;
                double ceil = double.MaxValue;
                foreach (HatchBox b in boxes)
                {
                    if (axis < b.MinY - 1.0 || axis > b.MaxY + 1.0) continue;
                    any = true;
                    double lo = b.MinY + b.H / 6.0;
                    double hi = b.MaxY - 1.0;
                    if (lo > floor) floor = lo;
                    if (hi < ceil) ceil = hi;
                }
                if (!any) return SnapCm(axis);
                // 1/6'nın altındaysa yukarı çekme — çizme (NaN)
                if (axis < floor - 0.5) return double.NaN;
                double y = Math.Max(axis, floor);
                if (y > ceil + 0.5) return double.NaN;
                return SnapCm(y);
            }
            else
            {
                double rightLimit = double.MaxValue;
                double leftLimit = double.MinValue;
                foreach (HatchBox b in boxes)
                {
                    if (axis < b.MinX - 1.0 || axis > b.MaxX + 1.0) continue;
                    any = true;
                    double hi = b.MaxX - b.W / 6.0;
                    double lo = b.MinX + 1.0;
                    if (hi < rightLimit) rightLimit = hi;
                    if (lo > leftLimit) leftLimit = lo;
                }
                if (!any) return SnapCm(axis);
                // Sağ 1/6'dan dışarıdaysa (kenara daha yakın) çizme
                if (axis > rightLimit + 0.5) return double.NaN;
                double x = Math.Min(axis, rightLimit);
                if (x < leftLimit - 0.5) return double.NaN;
                return SnapCm(x);
            }
        }

        /// <summary>
        /// Eksenin kestiği taramaların min H (yatay tarama) veya min W (düşey tarama).
        /// </summary>
        private static double VoidSizeAtAxis(List<HatchBox> boxes, double axis, bool horizontal)
        {
            double best = 0;
            if (boxes == null) return best;
            foreach (HatchBox b in boxes)
            {
                if (horizontal)
                {
                    if (axis <= b.MinY + 0.5 || axis >= b.MaxY - 0.5) continue;
                    if (best <= 0 || b.H < best) best = b.H;
                }
                else
                {
                    if (axis <= b.MinX + 0.5 || axis >= b.MaxX - 0.5) continue;
                    if (best <= 0 || b.W < best) best = b.W;
                }
            }
            return best;
        }

        /// <summary>
        /// horizontal: alt hat Y = MinY + H/6.
        /// düşey: sağ hat X = MaxX − W/6.
        /// Yakın eksenler birleştirilir; her hücre en az bir eksenle kesilir.
        /// </summary>
        private static List<double> BuildVoidScanAxes(List<HatchBox> boxes, bool horizontal)
        {
            var axes = new List<double>();
            if (boxes == null || boxes.Count == 0) return axes;

            var raw = new List<double>(boxes.Count);
            foreach (HatchBox b in boxes)
            {
                if (horizontal)
                    raw.Add(SnapCm(b.MinY + b.H / 6.0));
                else
                    raw.Add(SnapCm(b.MaxX - b.W / 6.0));
            }
            raw.Sort();

            double mergeTol = Math.Max(10.0, NormalBandMinCm());
            foreach (double v in raw)
            {
                if (axes.Count == 0 || Math.Abs(axes[axes.Count - 1] - v) > mergeTol)
                    axes.Add(v);
                else if (horizontal)
                    // Yatay: alt 1/6 (küçük Y)
                    axes[axes.Count - 1] = SnapCm(Math.Min(axes[axes.Count - 1], v));
                else
                    // Düşey: sağ 1/6 (büyük X)
                    axes[axes.Count - 1] = SnapCm(Math.Max(axes[axes.Count - 1], v));
            }

            // Her tarama en az bir eksenle kesilsin
            const double margin = 1.0;
            foreach (HatchBox b in boxes)
            {
                bool covered = false;
                foreach (double a in axes)
                {
                    if (horizontal)
                    {
                        if (a > b.MinY + margin && a < b.MaxY - margin)
                        {
                            covered = true;
                            break;
                        }
                    }
                    else if (a > b.MinX + margin && a < b.MaxX - margin)
                    {
                        covered = true;
                        break;
                    }
                }
                if (covered) continue;
                double add = horizontal
                    ? SnapCm(b.MinY + b.H / 6.0)
                    : SnapCm(b.MaxX - b.W / 6.0);
                axes.Add(add);
            }

            axes.Sort();
            // Tekrar birleştir
            var compact = new List<double>();
            foreach (double v in axes)
            {
                if (compact.Count == 0 || Math.Abs(compact[compact.Count - 1] - v) > mergeTol)
                    compact.Add(v);
            }

            // Birleştirme sonrası coverage kaybı olursa eksik hücreyi ekle
            foreach (HatchBox b in boxes)
            {
                bool covered = false;
                foreach (double a in compact)
                {
                    if (horizontal)
                    {
                        if (a > b.MinY + margin && a < b.MaxY - margin)
                        {
                            covered = true;
                            break;
                        }
                    }
                    else if (a > b.MinX + margin && a < b.MaxX - margin)
                    {
                        covered = true;
                        break;
                    }
                }
                if (covered) continue;
                compact.Add(horizontal
                    ? SnapCm(b.MinY + b.H / 6.0)
                    : SnapCm(b.MaxX - b.W / 6.0));
            }
            compact.Sort();
            return compact;
        }

        private const double MinHatchCm = 5.0;
        /// <summary>Boşluk taraması min kenar (kolon tarama/hatch elemesi).</summary>
        private const double MinVoidHatchCm = 80.0;

        /// <summary>
        /// AR-SAND / büyük boşluk taramaları. Kolon içi küçük hatch elenir.
        /// </summary>
        private static List<HatchBox> CollectVoidHatchBoxes(
            Transaction tr, BlockTableRecord btr, List<Point2d> ring)
        {
            var all = new List<HatchCandidate>();
            CollectHatchCandidates(tr, btr, ring, Matrix3d.Identity, 0, all);

            var sand = new List<HatchBox>();
            var large = new List<HatchBox>();
            foreach (HatchCandidate c in all)
            {
                if (c.Box.W < MinVoidHatchCm || c.Box.H < MinVoidHatchCm) continue;
                if (c.IsSandLike) sand.Add(c.Box);
                else large.Add(c.Box);
            }
            // AR-SAND vb. varsa yalnız onlar; yoksa yeterince büyük hatch’ler
            return sand.Count > 0 ? sand : large;
        }

        private sealed class HatchCandidate
        {
            public HatchBox Box;
            public bool IsSandLike;
        }

        private static void CollectHatchCandidates(
            Transaction tr, BlockTableRecord btr, List<Point2d> ring,
            Matrix3d xform, int depth, List<HatchCandidate> list)
        {
            if (btr == null || list == null || depth > 4) return;
            foreach (ObjectId id in btr)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                if (ent is BlockReference br)
                {
                    try
                    {
                        var btrNested = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                        CollectHatchCandidates(tr, btrNested, ring, br.BlockTransform * xform, depth + 1, list);
                    }
                    catch { }
                    continue;
                }
                if (!(ent is Hatch h)) continue;
                if (!TryGetHatchBox(h, xform, out HatchBox box)) continue;
                if (box.W < MinHatchCm || box.H < MinHatchCm) continue;
                if (!HatchTouchesRing(box, ring)) continue;
                list.Add(new HatchCandidate
                {
                    Box = box,
                    IsSandLike = IsSandLikeHatch(h)
                });
            }
        }

        private static bool IsSandLikeHatch(Hatch h)
        {
            if (h == null) return false;
            string pat = (h.PatternName ?? "").Trim();
            if (pat.Length > 0)
            {
                if (pat.IndexOf("SAND", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (pat.IndexOf("AR-", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (pat.IndexOf("DOT", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (pat.IndexOf("GRAVEL", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            string lyr = (h.Layer ?? "").Trim();
            if (lyr.IndexOf("TARAMA", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (lyr.IndexOf("SAND", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static List<HatchBox> CollectHatchBoxes(
            Transaction tr, BlockTableRecord btr, List<Point2d> ring, HashSet<string> layerFilter)
        {
            var list = new List<HatchBox>();
            CollectHatchBoxesInSpace(tr, btr, ring, layerFilter, Matrix3d.Identity, 0, list);
            return list;
        }

        private static void CollectHatchBoxesInSpace(
            Transaction tr, BlockTableRecord btr, List<Point2d> ring,
            HashSet<string> layerFilter, Matrix3d xform, int depth, List<HatchBox> list)
        {
            if (btr == null || list == null || depth > 4) return;
            foreach (ObjectId id in btr)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;

                if (ent is BlockReference br)
                {
                    try
                    {
                        var btrNested = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                        CollectHatchBoxesInSpace(
                            tr, btrNested, ring, layerFilter, br.BlockTransform * xform, depth + 1, list);
                    }
                    catch { }
                    continue;
                }

                if (!(ent is Hatch h)) continue;
                if (layerFilter != null && layerFilter.Count > 0)
                {
                    string lyr = (h.Layer ?? "").Trim();
                    if (!layerFilter.Contains(lyr)) continue;
                }

                if (!TryGetHatchBox(h, xform, out HatchBox box)) continue;
                if (box.W < MinHatchCm || box.H < MinHatchCm) continue;
                if (!HatchTouchesRing(box, ring)) continue;
                list.Add(box);
            }
        }

        private static bool TryGetHatchBox(Hatch h, Matrix3d xform, out HatchBox box)
        {
            box = null;
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            bool any = false;

            try
            {
                Extents3d ext = h.GeometricExtents;
                AddXformed(ref minX, ref maxX, ref minY, ref maxY, ref any,
                    ext.MinPoint.X, ext.MinPoint.Y, xform);
                AddXformed(ref minX, ref maxX, ref minY, ref maxY, ref any,
                    ext.MaxPoint.X, ext.MaxPoint.Y, xform);
                AddXformed(ref minX, ref maxX, ref minY, ref maxY, ref any,
                    ext.MinPoint.X, ext.MaxPoint.Y, xform);
                AddXformed(ref minX, ref maxX, ref minY, ref maxY, ref any,
                    ext.MaxPoint.X, ext.MinPoint.Y, xform);
            }
            catch { }

            if (!any)
            {
                try
                {
                    int n = h.NumberOfLoops;
                    for (int i = 0; i < n; i++)
                    {
                        HatchLoop loop = h.GetLoopAt(i);
                        if (loop == null) continue;
                        if (loop.IsPolyline)
                        {
                            foreach (BulgeVertex bv in loop.Polyline)
                            {
                                AddXformed(ref minX, ref maxX, ref minY, ref maxY, ref any,
                                    bv.Vertex.X, bv.Vertex.Y, xform);
                            }
                        }
                        else
                        {
                            foreach (Curve2d c in loop.Curves)
                            {
                                if (c == null) continue;
                                try
                                {
                                    Interval iv = c.GetInterval();
                                    Point2d a = c.EvaluatePoint(iv.LowerBound);
                                    Point2d b = c.EvaluatePoint(iv.UpperBound);
                                    AddXformed(ref minX, ref maxX, ref minY, ref maxY, ref any, a.X, a.Y, xform);
                                    AddXformed(ref minX, ref maxX, ref minY, ref maxY, ref any, b.X, b.Y, xform);
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }
            }

            if (!any || maxX <= minX || maxY <= minY) return false;
            box = new HatchBox
            {
                MinX = SnapCm(minX),
                MaxX = SnapCm(maxX),
                MinY = SnapCm(minY),
                MaxY = SnapCm(maxY)
            };
            return true;
        }

        private static void AddXformed(
            ref double minX, ref double maxX, ref double minY, ref double maxY, ref bool any,
            double x, double y, Matrix3d xform)
        {
            Point3d p = new Point3d(x, y, 0);
            try { p = p.TransformBy(xform); } catch { }
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
            any = true;
        }

        /// <summary>Merkez veya herhangi bir köşe dış poligon içindeyse kabul.</summary>
        private static bool HatchTouchesRing(HatchBox box, List<Point2d> ring)
        {
            if (ring == null || ring.Count < 3 || box == null) return false;
            if (PointInRing(new Point2d(box.MidX, box.MidY), ring)) return true;
            if (PointInRing(new Point2d(box.MinX, box.MinY), ring)) return true;
            if (PointInRing(new Point2d(box.MaxX, box.MinY), ring)) return true;
            if (PointInRing(new Point2d(box.MaxX, box.MaxY), ring)) return true;
            if (PointInRing(new Point2d(box.MinX, box.MaxY), ring)) return true;
            // Dış poligon taramayı kapsıyor mu (bbox orta noktaları)
            if (PointInRing(new Point2d(box.MidX, box.MinY), ring)) return true;
            if (PointInRing(new Point2d(box.MidX, box.MaxY), ring)) return true;
            if (PointInRing(new Point2d(box.MinX, box.MidY), ring)) return true;
            if (PointInRing(new Point2d(box.MaxX, box.MidY), ring)) return true;
            return false;
        }

        /// <summary>
        /// horizontal=true: MidY'ye göre satır; false: MidX'e göre sütun.
        /// </summary>
        private static List<List<HatchBox>> ClusterByAxis(List<HatchBox> boxes, bool horizontal)
        {
            var result = new List<List<HatchBox>>();
            if (boxes == null || boxes.Count == 0) return result;

            var sorted = new List<HatchBox>(boxes);
            sorted.Sort((a, b) =>
            {
                double da = horizontal ? a.MidY : a.MidX;
                double db = horizontal ? b.MidY : b.MidX;
                return da.CompareTo(db);
            });

            double sumSize = 0;
            foreach (HatchBox b in sorted)
                sumSize += horizontal ? b.H : b.W;
            double avgSize = sumSize / sorted.Count;
            double tol = Math.Max(NormalBandMinCm(), avgSize * 0.35);

            List<HatchBox> cur = null;
            double sumAxis = 0;
            foreach (HatchBox b in sorted)
            {
                double axis = horizontal ? b.MidY : b.MidX;
                if (cur == null)
                {
                    cur = new List<HatchBox> { b };
                    sumAxis = axis;
                    continue;
                }
                double mean = sumAxis / cur.Count;
                if (Math.Abs(axis - mean) <= tol)
                {
                    cur.Add(b);
                    sumAxis += axis;
                }
                else
                {
                    result.Add(cur);
                    cur = new List<HatchBox> { b };
                    sumAxis = axis;
                }
            }
            if (cur != null && cur.Count > 0) result.Add(cur);
            return result;
        }

        /// <summary>
        /// Aynı yöne bakan kenarlar tek ölçü doğrusunda: en dıştan 50 cm.
        /// Atılma noktaları: aynı hizadaki köşelerden ölçüye (dışa) en yakın olan.
        /// </summary>
        private static int PlaceCornerDims(
            Transaction tr, BlockTableRecord btr, List<Point2d> ring, List<Edge> edges,
            ObjectId dimStyleId, string dimLayer)
        {
            var groups = new List<List<Edge>>();
            foreach (Edge e in edges)
            {
                if (e.Length < MinEdgeCm) continue;
                List<Edge> found = null;
                foreach (List<Edge> g in groups)
                {
                    Edge h = g[0];
                    if (Math.Abs(Dot(h.Dir, e.Dir)) < 0.996) continue;
                    if (Dot(h.Outward, e.Outward) < 0.5) continue;
                    found = g;
                    break;
                }
                if (found == null)
                {
                    found = new List<Edge>();
                    groups.Add(found);
                }
                found.Add(e);
            }

            int n = 0;
            foreach (List<Edge> g in groups)
            {
                Vector2d outward = g[0].Outward;
                Vector2d along = g[0].Dir;
                double extreme = double.MinValue;
                foreach (Point2d p in ring)
                {
                    double d = p.X * outward.X + p.Y * outward.Y;
                    if (d > extreme) extreme = d;
                }
                double linePos = extreme + OutGapCm;
                Edge e0 = g[0];
                Point2d mid0 = new Point2d((e0.A.X + e0.B.X) * 0.5, (e0.A.Y + e0.B.Y) * 0.5);
                double cur = mid0.X * outward.X + mid0.Y * outward.Y;
                double delta = linePos - cur;
                Point3d dimPt = To3(new Point2d(mid0.X + outward.X * delta, mid0.Y + outward.Y * delta));
                double rotation = Math.Atan2(along.Y, along.X);

                // Kenar uçlarını aynı hizada ölçüye en yakın köşeye çek; istasyon zinciri kur.
                var stations = new List<CornerStation>();
                foreach (Edge e in g)
                {
                    AddCornerStation(stations, ClosestOnAlignment(e.A, ring, along, outward), along, outward);
                    AddCornerStation(stations, ClosestOnAlignment(e.B, ring, along, outward), along, outward);
                }
                stations.Sort((p, q) => p.T.CompareTo(q.T));

                for (int i = 0; i < stations.Count - 1; i++)
                {
                    Point2d a = stations[i].P;
                    Point2d b = stations[i + 1].P;
                    if (a.GetDistanceTo(b) < MinEdgeCm) continue;
                    if (TryPlace(tr, btr, a, b, dimPt, rotation, dimStyleId, dimLayer))
                        n++;
                }
            }
            return n;
        }

        /// <summary>
        /// Aynı hiza (along istasyonu): ölçü tarafına (outward) en yakın halka köşesi.
        /// </summary>
        private static Point2d ClosestOnAlignment(
            Point2d raw, List<Point2d> ring, Vector2d along, Vector2d outward)
        {
            double station = raw.X * along.X + raw.Y * along.Y;
            Point2d best = raw;
            double bestOut = raw.X * outward.X + raw.Y * outward.Y;
            if (ring == null) return best;
            foreach (Point2d q in ring)
            {
                double s = q.X * along.X + q.Y * along.Y;
                if (Math.Abs(s - station) > MergeTolCm) continue;
                double o = q.X * outward.X + q.Y * outward.Y;
                if (o > bestOut + 1e-9)
                {
                    bestOut = o;
                    best = q;
                }
            }
            return best;
        }

        private static void AddCornerStation(
            List<CornerStation> stations, Point2d p, Vector2d along, Vector2d outward)
        {
            double t = p.X * along.X + p.Y * along.Y;
            double oNew = p.X * outward.X + p.Y * outward.Y;
            for (int i = 0; i < stations.Count; i++)
            {
                if (Math.Abs(stations[i].T - t) > MergeTolCm) continue;
                double oOld = stations[i].P.X * outward.X + stations[i].P.Y * outward.Y;
                if (oNew > oOld + 1e-9)
                    stations[i] = new CornerStation { T = t, P = p };
                return;
            }
            stations.Add(new CornerStation { T = t, P = p });
        }

        /// <summary>
        /// Mantar/radye: bant dikdörtgeni çizilmez; X/Y iç ölçü aynı kurallarla atılır.
        /// </summary>
        private static int DrawSectionBands(
            Transaction tr, BlockTableRecord btr, List<Point2d> ring, List<List<Point2d>> voids,
            List<Seg> segs, List<Circ> circles,
            ObjectId dimStyleId, string dimLayer, out int xDimCount, out int yDimCount)
        {
            xDimCount = 0;
            yDimCount = 0;
            if (ring.Count < 3) return 0;
            double minX = ring[0].X, maxX = ring[0].X, minY = ring[0].Y, maxY = ring[0].Y;
            foreach (Point2d p in ring)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            var voidSegs = new List<Seg>();
            if (voids != null)
            {
                foreach (List<Point2d> v in voids)
                {
                    if (v == null || v.Count < 3) continue;
                    for (int i = 0; i < v.Count; i++)
                        AddSeg(voidSegs, v[i], v[(i + 1) % v.Count]);
                }
            }

            // Bant dikdörtgeni çizilmez; yalnız ölçü (X/Y).
            int drawn = 0;
            var seenH = new HashSet<string>(StringComparer.Ordinal);
            var seenV = new HashSet<string>(StringComparer.Ordinal);
            bool wantTotal = PlanOlcuSettings.PlaceTotalDim;
            double pairLimit = PlanOlcuSettings.TekCiftSinirCm;
            double normalMin = NormalBandMinCm();

            // X (yatay) bantlar → yatay ölçü
            var yZones = BuildSectionZones(minY, maxY, y => EffectiveChords(ring, voids, y, horizontal: true));
            foreach (BandZone z in yZones)
            {
                bool merged = z.Chords != null && z.Chords.Count >= 2;
                if (!merged && (z.Hi - z.Lo) < normalMin) continue;
                double bandH = z.Hi - z.Lo;
                bool placeTotal = wantTotal && bandH >= pairLimit;
                double totalY = z.Lo + bandH * 0.25;
                double detailY = placeTotal ? totalY + PairGapCm : totalY;
                if (detailY > z.Hi - 1.0) detailY = Math.Min(z.Hi - 1.0, totalY + PairGapCm);
                double mid = 0.5 * (z.Lo + z.Hi);

                var chords = merged
                    ? HorizontalChords(ring, mid)
                    : (z.Chords ?? new List<double[]>());
                foreach (double[] ch in chords)
                {
                    if (ch == null || ch.Length < 2) continue;
                    drawn++;
                    xDimCount += PlaceChordPair(
                        tr, btr, ring, segs, voidSegs, circles,
                        ch[0], ch[1], detailY, horizontal: true,
                        dimStyleId, dimLayer, seenH, placeTotal, totalY);
                }
            }

            // Y (düşey) bantlar → düşey ölçü (toplam: sağdan W/4)
            var xZones = BuildSectionZones(minX, maxX, x => EffectiveChords(ring, voids, x, horizontal: false));
            foreach (BandZone z in xZones)
            {
                if (z.Chords == null || z.Chords.Count == 0) continue;
                bool merged = z.Chords.Count >= 2;
                if (!merged && (z.Hi - z.Lo) < normalMin) continue;
                double a = double.MaxValue, b = double.MinValue;
                bool any = false;
                foreach (double[] ch in z.Chords)
                {
                    if (ch == null || ch.Length < 2) continue;
                    if (ch[0] < a) a = ch[0];
                    if (ch[1] > b) b = ch[1];
                    any = true;
                }
                if (!any || b - a < MinEdgeCm) continue;
                drawn++;

                double bandW = z.Hi - z.Lo;
                bool placeTotal = wantTotal && bandW >= pairLimit;
                double totalX = z.Hi - bandW * 0.25;
                double detailX = placeTotal ? totalX - PairGapCm : totalX;
                if (detailX < z.Lo + 1.0) detailX = Math.Max(z.Lo + 1.0, totalX - PairGapCm);
                yDimCount += PlaceChordPair(
                    tr, btr, ring, segs, voidSegs, circles,
                    a, b, detailX, horizontal: false,
                    dimStyleId, dimLayer, seenV, placeTotal, totalX);
            }
            return drawn;
        }

        private static double SnapCm(double v) => Math.Round(v * 100.0) / 100.0;

        /// <returns>Yazılan ölçü adedi.</returns>
        private static int PlaceChordPair(
            Transaction tr, BlockTableRecord btr, List<Point2d> ring,
            List<Seg> segs, List<Seg> voidSegs, List<Circ> circles,
            double a, double b, double fixedCoord, bool horizontal,
            ObjectId dimStyleId, string dimLayer, HashSet<string> seen, bool placeTotal,
            double totalFixedCoord = double.NaN,
            bool placeSegments = true)
        {
            return PlaceChordPair(
                tr, btr, ring, segs, voidSegs, circles,
                a, b, fixedCoord, horizontal, dimStyleId, dimLayer, seen,
                claimed: null, nearGapCm: 0,
                placeTotal, totalFixedCoord, placeSegments);
        }

        private static int PlaceChordPair(
            Transaction tr, BlockTableRecord btr, List<Point2d> ring,
            List<Seg> segs, List<Seg> voidSegs, List<Circ> circles,
            double a, double b, double fixedCoord, bool horizontal,
            ObjectId dimStyleId, string dimLayer, HashSet<string> seen,
            List<SpanClaim> claimed, double nearGapCm,
            bool placeTotal,
            double totalFixedCoord = double.NaN,
            bool placeSegments = true)
        {
            if (b - a < MinEdgeCm) return 0;
            Point2d origin = horizontal
                ? new Point2d(a, fixedCoord)
                : new Point2d(fixedCoord, a);
            Vector2d dir = horizontal ? new Vector2d(1, 0) : new Vector2d(0, 1);
            Vector2d perp = horizontal ? new Vector2d(0, 1) : new Vector2d(1, 0);
            double len = b - a;

            var cutSegs = new List<Seg>(segs ?? new List<Seg>());
            if (voidSegs != null) cutSegs.AddRange(voidSegs);
            var hits = HitsOnSpan(origin, dir, 0, len, cutSegs, circles ?? new List<Circ>());
            hits = WithPolygonEnds(hits, origin, dir, 0, len);
            if (hits.Count < 2) return 0;

            // Mutlak kesişim imzası (kiriş başlangıcından bağımsız) — aynı açıklık = aynı imza
            string absSig = AbsoluteHitSignature(hits, horizontal);
            if (placeSegments && claimed != null && nearGapCm > 0.5)
            {
                if (!TryClaimSpan(claimed, absSig, fixedCoord, nearGapCm))
                    placeSegments = false;
            }
            if (!placeSegments && !placeTotal) return 0;

            string lineSig = (horizontal ? "H:" : "V:")
                + ((long)Math.Round(fixedCoord)).ToString()
                + ";"
                + absSig
                + (placeSegments ? ";S" : "")
                + (placeTotal ? ";T" : "");
            if (!seen.Add(lineSig)) return 0;

            double rotation = horizontal ? 0.0 : Math.PI * 0.5;
            Point2d mid = origin + dir * (len * 0.5);
            Point3d detailPt = To3(mid);
            int detail = 0;
            if (placeSegments)
            {
                for (int i = 0; i < hits.Count - 1; i++)
                {
                    if (TryPlace(tr, btr, hits[i].P, hits[i + 1].P, detailPt, rotation, dimStyleId, dimLayer))
                        detail++;
                }
            }

            if (!placeTotal) return detail;

            // Tek açıklık (yalnız 2 uç): toplam = detayın aynısı → ikinci ölçü yazma
            if (hits.Count <= 2) return detail;

            Point2d totalAt;
            if (!double.IsNaN(totalFixedCoord))
            {
                totalAt = horizontal
                    ? new Point2d(mid.X, totalFixedCoord)
                    : new Point2d(totalFixedCoord, mid.Y);
            }
            else
            {
                Point2d plus = mid + perp * PairGapCm;
                Point2d minus = mid - perp * PairGapCm;
                if (horizontal)
                    totalAt = minus;
                else if (SegmentInside(plus, dir, hits, ring))
                    totalAt = plus;
                else if (SegmentInside(minus, dir, hits, ring))
                    totalAt = minus;
                else
                    totalAt = plus;
            }
            bool total = TryPlace(
                tr, btr, hits[0].P, hits[hits.Count - 1].P, To3(totalAt),
                rotation, dimStyleId, dimLayer);
            return detail + (total ? 1 : 0);
        }

        /// <summary>Kesişimleri dünya koordinatında imzala (T değil).</summary>
        private static string AbsoluteHitSignature(List<Hit> hits, bool horizontal)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Hit h in hits)
            {
                if (sb.Length > 0) sb.Append('|');
                double v = horizontal ? h.P.X : h.P.Y;
                sb.Append(((long)Math.Round(v)).ToString());
            }
            return sb.ToString();
        }

        private static bool TryClaimSpan(
            List<SpanClaim> claimed, string absSig, double coord, double nearGapCm)
        {
            if (claimed == null || string.IsNullOrEmpty(absSig)) return true;
            for (int i = 0; i < claimed.Count; i++)
            {
                if (!string.Equals(claimed[i].AbsSig, absSig, StringComparison.Ordinal))
                    continue;
                if (Math.Abs(claimed[i].Coord - coord) <= nearGapCm)
                    return false;
            }
            claimed.Add(new SpanClaim { AbsSig = absSig, Coord = coord });
            return true;
        }

        /// <summary>Zincir poligon girişinden çıkışına kadar kesintisiz; uçlar sınırda.</summary>
        private static List<Hit> WithPolygonEnds(List<Hit> hits, Point2d origin, Vector2d dir, double t0, double t1)
        {
            var result = new List<Hit>
            {
                new Hit { T = t0, P = origin + dir * t0 }
            };
            foreach (Hit h in hits)
            {
                if (h.T - result[result.Count - 1].T <= MergeTolCm) continue;
                if (t1 - h.T <= MergeTolCm) continue;
                result.Add(h);
            }
            var end = new Hit { T = t1, P = origin + dir * t1 };
            if (end.T - result[result.Count - 1].T > MergeTolCm)
                result.Add(end);
            else
                result[result.Count - 1] = end;
            return result;
        }

        private static bool SegmentInside(Point2d dimAt, Vector2d dir, List<Hit> hits, List<Point2d> ring)
        {
            if (hits.Count < 2) return false;
            double t0 = hits[0].T;
            double t1 = hits[hits.Count - 1].T;
            double midT = (t0 + t1) * 0.5;
            for (int k = 0; k <= 6; k++)
            {
                double t = t0 + (t1 - t0) * k / 6.0;
                Point2d p = dimAt + dir * (t - midT);
                if (!PointInRing(p, ring)) return false;
            }
            return true;
        }

        /// <summary>
        /// Bantlar bitişik (ortak kenar); kiriş koordinatları zonun ilk örneğinden sabitlenir (kaçıklık yok).
        /// </summary>
        private static List<BandZone> BuildSectionZones(
            double from, double to, Func<double, List<double[]>> chordsAt)
        {
            var zones = new List<BandZone>();
            if (to - from < MinZoneCm || chordsAt == null) return zones;
            var samples = new List<double[]>();
            var chordLists = new List<List<double[]>>();
            var poss = new List<double>();
            for (double p = from; p <= to + 1e-6; p += SectionScanCm)
            {
                double pp = Math.Min(p, to);
                poss.Add(pp);
                var ch = chordsAt(pp) ?? new List<double[]>();
                chordLists.Add(ch);
                samples.Add(Flatten(ch));
                if (pp >= to - 1e-9) break;
            }
            if (poss.Count == 0) return zones;

            double zoneLo = poss[0];
            int start = 0;
            for (int i = 1; i < poss.Count; i++)
            {
                if (!SameChordSig(samples[i - 1], samples[i]))
                {
                    double zoneHi = 0.5 * (poss[i - 1] + poss[i]);
                    if (zoneHi - zoneLo >= MinZoneCm)
                        zones.Add(MakeBandZone(zoneLo, zoneHi, chordLists[start]));
                    zoneLo = zoneHi;
                    start = i;
                }
            }
            double lastHi = poss[poss.Count - 1];
            if (lastHi - zoneLo >= MinZoneCm)
                zones.Add(MakeBandZone(zoneLo, lastHi, chordLists[start]));
            return zones;
        }

        private static BandZone MakeBandZone(double lo, double hi, List<double[]> chords)
        {
            var copy = new List<double[]>();
            if (chords != null)
            {
                foreach (double[] c in chords)
                {
                    if (c == null || c.Length < 2) continue;
                    copy.Add(new[] { SnapCm(c[0]), SnapCm(c[1]) });
                }
            }
            return new BandZone
            {
                Lo = SnapCm(lo),
                Hi = SnapCm(hi),
                Chords = copy
            };
        }

        private static double[] Flatten(List<double[]> chords)
        {
            if (chords == null || chords.Count == 0) return new double[0];
            var list = new List<double>();
            foreach (double[] c in chords)
            {
                if (c == null || c.Length < 2) continue;
                list.Add(c[0]);
                list.Add(c[1]);
            }
            return list.ToArray();
        }

        private static bool SameChordSig(double[] a, double[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > SectionTolCm) return false;
            return true;
        }

        private static List<double[]> EffectiveChords(
            List<Point2d> ring, List<List<Point2d>> voids, double p, bool horizontal)
        {
            var solid = horizontal ? HorizontalChords(ring, p) : VerticalChords(ring, p);
            if (voids == null || voids.Count == 0) return solid;
            var holes = new List<double[]>();
            foreach (List<Point2d> v in voids)
            {
                if (v == null || v.Count < 3) continue;
                holes.AddRange(horizontal ? HorizontalChords(v, p) : VerticalChords(v, p));
            }
            return SubtractChords(solid, holes);
        }

        private static List<double[]> HorizontalChords(List<Point2d> ring, double y)
        {
            var xs = new List<double>();
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                Point2d p0 = ring[i];
                Point2d p1 = ring[(i + 1) % n];
                if (Math.Abs(p0.Y - p1.Y) < 1e-9)
                {
                    if (Math.Abs(p0.Y - y) < 1e-6)
                    {
                        xs.Add(p0.X);
                        xs.Add(p1.X);
                    }
                    continue;
                }
                if ((p0.Y > y) == (p1.Y > y)) continue;
                double t = (y - p0.Y) / (p1.Y - p0.Y);
                if (t < -1e-9 || t > 1 + 1e-9) continue;
                xs.Add(p0.X + t * (p1.X - p0.X));
            }
            return PairCoords(xs);
        }

        private static List<double[]> VerticalChords(List<Point2d> ring, double x)
        {
            var ys = new List<double>();
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                Point2d p0 = ring[i];
                Point2d p1 = ring[(i + 1) % n];
                if (Math.Abs(p0.X - p1.X) < 1e-9)
                {
                    if (Math.Abs(p0.X - x) < 1e-6)
                    {
                        ys.Add(p0.Y);
                        ys.Add(p1.Y);
                    }
                    continue;
                }
                if ((p0.X > x) == (p1.X > x)) continue;
                double t = (x - p0.X) / (p1.X - p0.X);
                if (t < -1e-9 || t > 1 + 1e-9) continue;
                ys.Add(p0.Y + t * (p1.Y - p0.Y));
            }
            return PairCoords(ys);
        }

        private static List<double[]> PairCoords(List<double> vals)
        {
            var res = new List<double[]>();
            if (vals.Count < 2) return res;
            vals.Sort();
            for (int i = vals.Count - 1; i > 0; i--)
            {
                if (Math.Abs(vals[i] - vals[i - 1]) < 0.05)
                    vals.RemoveAt(i);
            }
            for (int i = 0; i + 1 < vals.Count; i += 2)
            {
                if (vals[i + 1] - vals[i] >= MinEdgeCm)
                    res.Add(new[] { vals[i], vals[i + 1] });
            }
            return res;
        }

        private static List<double[]> SubtractChords(List<double[]> solid, List<double[]> holes)
        {
            if (solid == null) return new List<double[]>();
            if (holes == null || holes.Count == 0) return solid;
            var result = new List<double[]>();
            foreach (double[] s in solid)
            {
                var pieces = new List<double[]> { s };
                foreach (double[] h in holes)
                {
                    var next = new List<double[]>();
                    foreach (double[] p in pieces)
                    {
                        double a = p[0], b = p[1], c = h[0], d = h[1];
                        if (d <= a + 0.05 || c >= b - 0.05)
                        {
                            next.Add(p);
                            continue;
                        }
                        if (c > a + 0.05) next.Add(new[] { a, Math.Min(c, b) });
                        if (d < b - 0.05) next.Add(new[] { Math.Max(d, a), b });
                    }
                    pieces = next;
                }
                foreach (double[] p in pieces)
                    if (p[1] - p[0] >= MinEdgeCm) result.Add(p);
            }
            return result;
        }

        private static List<Hit> HitsOnSpan(
            Point2d origin, Vector2d dir, double t0, double t1,
            List<Seg> segs, List<Circ> circles)
        {
            var raw = new List<Hit>();
            Point2d a = origin + dir * t0;
            Point2d b = origin + dir * t1;
            foreach (Seg s in segs)
            {
                if (!SegIntersect(s.A, s.B, a, b, out Point2d hit)) continue;
                raw.Add(new Hit { T = Dot(hit - origin, dir), P = hit });
            }
            foreach (Circ c in circles)
            {
                Vector2d oc = c.C - origin;
                double tc = Dot(oc, dir);
                double d2 = oc.X * oc.X + oc.Y * oc.Y - tc * tc;
                double h2 = c.R * c.R - d2;
                if (h2 < 0) continue;
                double h = Math.Sqrt(h2);
                AddCircleHit(raw, origin, dir, tc - h, t0, t1);
                AddCircleHit(raw, origin, dir, tc + h, t0, t1);
            }
            raw.Sort((p, q) => p.T.CompareTo(q.T));
            var hits = new List<Hit>();
            foreach (Hit h in raw)
            {
                if (h.T < t0 - 0.5 || h.T > t1 + 0.5) continue;
                if (hits.Count == 0 || h.T - hits[hits.Count - 1].T > MergeTolCm)
                    hits.Add(h);
            }
            return hits;
        }

        private static void AddCircleHit(List<Hit> raw, Point2d origin, Vector2d dir, double t, double t0, double t1)
        {
            if (t < t0 - 0.5 || t > t1 + 0.5) return;
            raw.Add(new Hit { T = t, P = origin + dir * t });
        }

        private static string HitSignature(List<Hit> hits)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Hit h in hits)
            {
                if (sb.Length > 0) sb.Append('|');
                sb.Append(((long)Math.Round(h.T)).ToString());
            }
            return sb.ToString();
        }

        private static bool TryPlace(
            Transaction tr,
            BlockTableRecord btr,
            Point2d a,
            Point2d b,
            Point3d dimLinePoint,
            double rotation,
            ObjectId dimStyleId,
            string layer)
        {
            if (a.GetDistanceTo(b) < MergeTolCm) return false;
            OtoOlcuDimFactory.PlaceRotated(
                tr, btr, To3(a), To3(b), dimLinePoint, dimStyleId, layer,
                d =>
                {
                    if (d is RotatedDimension rd)
                        rd.Rotation = rotation;
                });
            return true;
        }

        /// <summary>İç ölçü: uzatma çizgisi vertex dışına taşmasın (DIMEXE küçük).</summary>
        private static bool TryPlaceInterior(
            Transaction tr,
            BlockTableRecord btr,
            Point2d a,
            Point2d b,
            Point3d dimLinePoint,
            double rotation,
            ObjectId dimStyleId,
            string layer)
        {
            if (a.GetDistanceTo(b) < MergeTolCm) return false;
            OtoOlcuDimFactory.PlaceRotated(
                tr, btr, To3(a), To3(b), dimLinePoint, dimStyleId, layer,
                d =>
                {
                    if (d is RotatedDimension rd)
                        rd.Rotation = rotation;
                    try { d.Dimexe = 0.25; } catch { }
                },
                enforceMinExtension: false);
            return true;
        }

        private static List<Edge> BuildEdges(List<Point2d> ring, bool ccw)
        {
            var edges = new List<Edge>();
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                Point2d a = ring[i];
                Point2d b = ring[(i + 1) % n];
                Vector2d d = b - a;
                double len = d.Length;
                if (len < 0.1) continue;
                Vector2d dir = d / len;
                Vector2d right = new Vector2d(dir.Y, -dir.X);
                Vector2d outward = ccw ? right : -right;
                edges.Add(new Edge
                {
                    A = a,
                    B = b,
                    Dir = dir,
                    Length = len,
                    Outward = outward,
                    Inward = -outward
                });
            }
            return edges;
        }

        private static List<Point2d> ReadRing(Transaction tr, Entity ent)
        {
            var pts = new List<Point2d>();
            if (ent is Polyline pl)
            {
                if (!pl.Closed && pl.StartPoint.DistanceTo(pl.EndPoint) > 1.0)
                    return pts;
                int n = pl.NumberOfVertices;
                for (int i = 0; i < n; i++)
                {
                    int steps = 1;
                    try
                    {
                        if (Math.Abs(pl.GetBulgeAt(i)) > 1e-6)
                            steps = 8;
                    }
                    catch { }
                    int next = (i + 1) % n;
                    if (!pl.Closed && next == 0) break;
                    for (int s = 0; s < steps; s++)
                    {
                        double param = i + s / (double)steps;
                        Point3d p;
                        try { p = pl.GetPointAtParameter(param); }
                        catch { p = pl.GetPoint3dAt(i); }
                        pts.Add(new Point2d(p.X, p.Y));
                    }
                }
                return pts;
            }

            if (ent is Polyline2d pl2)
            {
                if (!pl2.Closed) return pts;
                foreach (ObjectId vid in pl2)
                {
                    if (tr.GetObject(vid, OpenMode.ForRead) is Vertex2d v)
                        pts.Add(new Point2d(v.Position.X, v.Position.Y));
                }
            }
            return pts;
        }

        private static List<Point2d> CleanRing(List<Point2d> pts)
        {
            var r = new List<Point2d>();
            if (pts == null) return r;
            foreach (Point2d p in pts)
            {
                if (r.Count == 0 || r[r.Count - 1].GetDistanceTo(p) > 0.1)
                    r.Add(p);
            }
            if (r.Count > 1 && r[0].GetDistanceTo(r[r.Count - 1]) <= 0.1)
                r.RemoveAt(r.Count - 1);

            int guard = 0;
            bool changed = true;
            while (changed && r.Count >= 4 && guard++ < 64)
            {
                changed = false;
                for (int i = 0; i < r.Count; i++)
                {
                    Point2d prev = r[(i + r.Count - 1) % r.Count];
                    Point2d cur = r[i];
                    Point2d next = r[(i + 1) % r.Count];
                    Vector2d a = cur - prev;
                    Vector2d b = next - cur;
                    if (a.Length < 0.1 || b.Length < 0.1)
                    {
                        r.RemoveAt(i);
                        changed = true;
                        break;
                    }
                    double turn = Math.Abs(Math.Atan2(Cross(a, b), Dot(a, b))) * 180.0 / Math.PI;
                    if (turn < ColinearDeg)
                    {
                        r.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }
            return r;
        }

        private static void CollectSegments(
            Transaction tr, BlockTableRecord btr, HashSet<string> layers, List<Seg> segs, List<Circ> circles, ObjectId skipId)
        {
            foreach (ObjectId id in btr)
            {
                if (id == skipId) continue;
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent))
                    continue;
                if (ent is Dimension || ent is DBText || ent is MText || ent is Hatch)
                    continue;
                try { CollectEntity(tr, ent, layers, segs, circles, 0, ent.Layer); }
                catch { }
            }
        }

        private static void CollectEntity(
            Transaction tr, Entity ent, HashSet<string> layers, List<Seg> segs, List<Circ> circles, int depth, string inheritLayer)
        {
            if (ent == null || depth > 4) return;
            if (ent is Dimension || ent is DBText || ent is MText || ent is Hatch)
                return;

            if (ent is BlockReference br)
            {
                CollectBlock(tr, br, layers, segs, circles, depth);
                return;
            }

            string lyr = ent.Layer;
            if (string.Equals(lyr, "0", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(inheritLayer))
                lyr = inheritLayer;
            if (!layers.Contains((lyr ?? "").Trim()) && !layers.Contains((inheritLayer ?? "").Trim()))
                return;

            if (ent is Line ln)
            {
                AddSeg(segs, new Point2d(ln.StartPoint.X, ln.StartPoint.Y), new Point2d(ln.EndPoint.X, ln.EndPoint.Y));
                return;
            }
            if (ent is Polyline pl)
            {
                int n = pl.NumberOfVertices;
                if (n < 2) return;
                int se = pl.Closed ? n : n - 1;
                for (int i = 0; i < se; i++)
                {
                    int steps = 1;
                    try { if (Math.Abs(pl.GetBulgeAt(i)) > 1e-6) steps = 8; }
                    catch { }
                    Point2d prev = PointAt(pl, i);
                    for (int s = 1; s <= steps; s++)
                    {
                        Point2d q = PointAt(pl, i + s / (double)steps);
                        AddSeg(segs, prev, q);
                        prev = q;
                    }
                }
                return;
            }
            if (ent is Arc arc)
            {
                AddArc(segs, new Point2d(arc.Center.X, arc.Center.Y), arc.Radius, arc.StartAngle, arc.EndAngle, full: false);
                return;
            }
            if (ent is Circle cir)
            {
                if (cir.Radius >= 0.2)
                    circles.Add(new Circ { C = new Point2d(cir.Center.X, cir.Center.Y), R = cir.Radius });
                return;
            }
            if (ent is Polyline2d pl2)
            {
                var verts = new List<Point2d>();
                foreach (ObjectId vid in pl2)
                {
                    if (tr.GetObject(vid, OpenMode.ForRead) is Vertex2d v)
                        verts.Add(new Point2d(v.Position.X, v.Position.Y));
                }
                int n = verts.Count;
                if (n < 2) return;
                int se = pl2.Closed ? n : n - 1;
                for (int i = 0; i < se; i++)
                    AddSeg(segs, verts[i], verts[(i + 1) % n]);
            }
        }

        private static void CollectBlock(
            Transaction tr, BlockReference br, HashSet<string> layers, List<Seg> segs, List<Circ> circles, int depth)
        {
            try
            {
                var def = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                Matrix3d xf = br.BlockTransform;
                foreach (ObjectId id in def)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity nested))
                        continue;
                    Entity clone = null;
                    try
                    {
                        clone = (Entity)nested.Clone();
                        clone.TransformBy(xf);
                        CollectEntity(tr, clone, layers, segs, circles, depth + 1, br.Layer);
                    }
                    catch { }
                    finally { if (clone != null) clone.Dispose(); }
                }
            }
            catch { }
        }

        private static Point2d PointAt(Polyline pl, double param)
        {
            try
            {
                Point3d p = pl.GetPointAtParameter(param);
                return new Point2d(p.X, p.Y);
            }
            catch
            {
                int i = (int)Math.Floor(param);
                if (i < 0) i = 0;
                if (i >= pl.NumberOfVertices) i = pl.NumberOfVertices - 1;
                Point3d p = pl.GetPoint3dAt(i);
                return new Point2d(p.X, p.Y);
            }
        }

        private static void AddArc(List<Seg> segs, Point2d c, double r, double a0, double a1, bool full)
        {
            if (r < 0.2) return;
            double sweep = full ? Math.PI * 2 : a1 - a0;
            if (!full)
            {
                while (sweep <= 1e-6) sweep += Math.PI * 2;
                if (sweep > Math.PI * 2) sweep = Math.PI * 2;
            }
            int n = Math.Max(8, (int)Math.Ceiling(sweep / (Math.PI / 12.0)));
            Point2d prev = new Point2d(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0));
            for (int i = 1; i <= n; i++)
            {
                double a = a0 + sweep * i / n;
                var q = new Point2d(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
                AddSeg(segs, prev, q);
                prev = q;
            }
        }

        private static void AddSeg(List<Seg> segs, Point2d a, Point2d b)
        {
            if (a.GetDistanceTo(b) < MinSegCm) return;
            segs.Add(new Seg(a, b));
        }

        private static bool SegIntersect(Point2d a, Point2d b, Point2d c, Point2d d, out Point2d hit)
        {
            hit = new Point2d();
            Vector2d r = b - a;
            Vector2d s = d - c;
            double den = Cross(r, s);
            if (Math.Abs(den) < 1e-12) return false;
            Vector2d qp = c - a;
            double t = Cross(qp, s) / den;
            double u = Cross(qp, r) / den;
            if (t < -1e-8 || t > 1.0 + 1e-8 || u < -1e-8 || u > 1.0 + 1e-8)
                return false;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            hit = a + r * t;
            return true;
        }

        private static bool PointInRing(Point2d p, List<Point2d> ring)
        {
            if (DistanceToRing(p, ring) <= 0.2) return true;
            bool inside = false;
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Point2d pi = ring[i];
                Point2d pj = ring[j];
                if (((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                    (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / ((pj.Y - pi.Y) == 0 ? 1e-12 : (pj.Y - pi.Y)) + pi.X))
                    inside = !inside;
            }
            return inside;
        }

        private static double DistanceToRing(Point2d p, List<Point2d> ring)
        {
            double best = double.MaxValue;
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                double d = DistToSeg(p, ring[i], ring[(i + 1) % n]);
                if (d < best) best = d;
            }
            return best;
        }

        private static double DistToSeg(Point2d p, Point2d a, Point2d b)
        {
            Vector2d ab = b - a;
            double len2 = ab.X * ab.X + ab.Y * ab.Y;
            if (len2 < 1e-12) return p.GetDistanceTo(a);
            double t = Dot(p - a, ab) / len2;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            var q = new Point2d(a.X + ab.X * t, a.Y + ab.Y * t);
            return p.GetDistanceTo(q);
        }

        private static double SignedArea(List<Point2d> ring)
        {
            double a = 0;
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                Point2d p = ring[i];
                Point2d q = ring[(i + 1) % n];
                a += p.X * q.Y - q.X * p.Y;
            }
            return a * 0.5;
        }

        private static string EnsureOlcuLayer(Transaction tr, Database db)
        {
            string name = OtoOlcuSettings.DefaultDimLayerName;
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return name;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = name };
            try { rec.Color = Color.FromColorIndex(ColorMethod.ByAci, 3); } catch { }
            try { rec.LineWeight = LineWeight.LineWeight030; } catch { }
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return name;
        }

        private static ObjectId ResolveDimStyleId(Transaction tr, Database db, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ObjectId.Null;
            var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (dst.Has(name)) return dst[name];
            return ObjectId.Null;
        }

        private static Point3d To3(Point2d p) => new Point3d(p.X, p.Y, 0);

        private static double Dot(Vector2d a, Vector2d b) => a.X * b.X + a.Y * b.Y;
        private static double Dot(Point2d p, Vector2d v) => p.X * v.X + p.Y * v.Y;
        private static double Cross(Vector2d a, Vector2d b) => a.X * b.Y - a.Y * b.X;

        private struct Seg
        {
            public Point2d A;
            public Point2d B;
            public Seg(Point2d a, Point2d b) { A = a; B = b; }
        }

        private struct Circ
        {
            public Point2d C;
            public double R;
        }

        private sealed class CornerStation
        {
            public double T;
            public Point2d P;
        }

        private sealed class BandZone
        {
            public double Lo;
            public double Hi;
            public List<double[]> Chords;
        }

        private sealed class HatchBox
        {
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
            public double MidX => 0.5 * (MinX + MaxX);
            public double MidY => 0.5 * (MinY + MaxY);
            public double W => MaxX - MinX;
            public double H => MaxY - MinY;
        }

        private sealed class Edge
        {
            public Point2d A;
            public Point2d B;
            public Vector2d Dir;
            public Vector2d Outward;
            public Vector2d Inward;
            public double Length;
        }

        private sealed class Hit
        {
            public double T;
            public Point2d P;
        }
    }
}
