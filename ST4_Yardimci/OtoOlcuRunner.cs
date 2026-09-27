using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Sanal doğru; 1. / 2. katman listelerine göre ölçü.
    /// Her zaman RotatedDimension; uzatma (DIMEXE) ≥ yazı yüksekliği / 2. Küçük ölçüler (&lt; 0.2) yazılmaz.
    /// </summary>
    internal static class OtoOlcuRunner
    {
        private const double MinDimLength = 0.2;
        /// <summary>Ölçü çizgisi sanal hattan mesafe (cm): yatayda alt, dikeyde sağ.</summary>
        private const double DimBelowVirtualCm = 5.0;

        public static void RunInteractive()
        {
            OtoOlcuSettings.EnsureLoaded();
            if (OtoOlcuSettings.IsAksOlcuPreset)
            {
                OtoOlcuAksRunner.RunInteractive();
                return;
            }

            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                OtoOlcuForm.ShowAfterPick();
                return;
            }
            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                var layers1 = OtoOlcuSettings.GetLayers(OtoOlcuLayerList.Birinci);
                if (layers1.Count == 0)
                {
                    ed.WriteMessage("\nOTOOLCUCIZ: 1. ölçü için katman seçin.");
                    return;
                }

                bool cift = OtoOlcuSettings.Sablon == OtoOlcuSablon.CiftOlcu;
                bool ikinciToplam = OtoOlcuSettings.IkinciOlcuToplam;
                var layers2 = OtoOlcuSettings.GetLayers(OtoOlcuLayerList.Ikinci);
                if (cift && !ikinciToplam && layers2.Count == 0)
                {
                    ed.WriteMessage("\nOTOOLCUCIZ: Çift ölçüde 2. katman listesi veya «İkinci ölçü toplam» gerekli.");
                    return;
                }

                var set1 = new HashSet<string>(
                    layers1.Select(s => (s ?? "").Trim()).Where(s => s.Length > 0),
                    StringComparer.OrdinalIgnoreCase);
                var set2 = (cift && layers2.Count > 0)
                    ? new HashSet<string>(
                        layers2.Select(s => (s ?? "").Trim()).Where(s => s.Length > 0),
                        StringComparer.OrdinalIgnoreCase)
                    : null;

                ed.WriteMessage("\nOTOOLCUCIZ: Ölçü modu. Bitirmek için Esc veya sağ tık.");

                while (true)
                {
                    var opt1 = new PromptPointOptions("\nSanal ölçü çizgisi birinci nokta (bitir: Esc/sağ tık): ")
                    {
                        AllowNone = true
                    };
                    PromptPointResult p1 = ed.GetPoint(opt1);
                    if (p1.Status != PromptStatus.OK)
                        break;

                    var opt2 = new PromptPointOptions("\nSanal ölçü çizgisi ikinci nokta: ")
                    {
                        BasePoint = p1.Value,
                        UseBasePoint = true,
                        AllowNone = true
                    };
                    PromptPointResult p2 = ed.GetPoint(opt2);
                    if (p2.Status != PromptStatus.OK)
                        break;

                    Point3d a = p1.Value;
                    Point3d b = p2.Value;
                    Vector3d dir = b - a;
                    if (dir.Length < MinDimLength)
                    {
                        ed.WriteMessage("\nOTOOLCUCIZ: Çizgi çok kısa (< {0}).", MinDimLength);
                        continue;
                    }

                    PlaceDimsForSegment(doc, db, ed, a, b, dir, set1, set2, cift, ikinciToplam);
                }
            }
            finally
            {
                OtoOlcuForm.ShowAfterPick();
            }
        }

        private static void PlaceDimsForSegment(
            Document doc,
            Database db,
            Editor ed,
            Point3d a,
            Point3d b,
            Vector3d dir,
            HashSet<string> set1,
            HashSet<string> set2,
            bool cift,
            bool ikinciToplam)
        {
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                ObjectId dimStyleId = ResolveDimStyleId(tr, db, OtoOlcuSettings.DimStyleName);
                string dimLayer = EnsureLayer(tr, db, OtoOlcuSettings.DimLayerName);
                double textH = OtoOlcuDimFactory.GetDimTextHeight(tr, dimStyleId);
                double gap = 1.8 * textH;

                var hits1 = new List<Point3d>();
                CollectIntersections(tr, btr, a, b, set1, hits1);
                SortAlongLine(a, dir, hits1);
                DedupNear(hits1, 1e-4);

                Point3d dimOnVirtual = new Point3d(
                    (a.X + b.X) * 0.5,
                    (a.Y + b.Y) * 0.5,
                    (a.Z + b.Z) * 0.5);

                Vector3d unit = dir.GetNormal();
                Vector3d perp = unit.CrossProduct(Vector3d.ZAxis);
                if (perp.Length < 1e-9)
                    perp = Vector3d.YAxis;
                else
                    perp = perp.GetNormal();
                // Yatay sanal hat → 5 cm alt (−Y); dikey → 5 cm sağ (+X)
                Vector3d side = perp;
                bool vertical = Math.Abs(unit.Y) >= Math.Abs(unit.X);
                if (vertical)
                {
                    if (side.X < 0) side = -side;
                }
                else
                {
                    if (side.Y > 0) side = -side;
                }

                Point3d dimRow1 = dimOnVirtual + side * DimBelowVirtualCm;

                int placed = 0;
                int skipped = 0;

                // 1. ölçü: ara ölçüler (yatayda alt / dikeyde sağ, 5 cm)
                if (hits1.Count >= 2)
                {
                    for (int i = 0; i < hits1.Count - 1; i++)
                    {
                        if (TryPlaceDim(tr, btr, hits1[i], hits1[i + 1], dimRow1, dimStyleId, dimLayer))
                            placed++;
                        else
                            skipped++;
                    }
                }
                else
                {
                    ed.WriteMessage(
                        "\nOTOOLCUCIZ: 1. ölçü — yetersiz kesişim ({0}). Seçili katman: {1}",
                        hits1.Count,
                        string.Join(", ", set1));
                }

                // İkinci ölçü toplam: 1. listenin dıştan dışa toplamı (2. listeden bağımsız)
                if (ikinciToplam && hits1.Count >= 2)
                {
                    Point3d dimTotal = dimRow1 + side * gap;
                    if (TryPlaceDim(tr, btr, hits1[0], hits1[hits1.Count - 1], dimTotal, dimStyleId, dimLayer))
                        placed++;
                    else
                        skipped++;
                }

                // Çift ölçü + 2. katman listesi: yalnız toplam tiki kapalıysa atılır
                if (cift && !ikinciToplam && set2 != null && set2.Count > 0)
                {
                    var hits2 = new List<Point3d>();
                    CollectIntersections(tr, btr, a, b, set2, hits2);
                    SortAlongLine(a, dir, hits2);
                    DedupNear(hits2, 1e-4);

                    Point3d dimRow2 = dimRow1 + side * gap;

                    if (hits2.Count >= 2)
                    {
                        for (int i = 0; i < hits2.Count - 1; i++)
                        {
                            if (TryPlaceDim(tr, btr, hits2[i], hits2[i + 1], dimRow2, dimStyleId, dimLayer))
                                placed++;
                            else
                                skipped++;
                        }
                    }
                    else
                    {
                        ed.WriteMessage("\nOTOOLCUCIZ: 2. ölçü katmanları — yetersiz kesişim ({0}).", hits2.Count);
                    }
                }

                tr.Commit();
                ed.WriteMessage("\nOTOOLCUCIZ: {0} ölçü ({1} atlandı < {2}).", placed, skipped, MinDimLength);
            }
        }

        private static bool TryPlaceDim(
            Transaction tr,
            BlockTableRecord btr,
            Point3d x1,
            Point3d x2,
            Point3d dimLinePoint,
            ObjectId dimStyleId,
            string layer)
        {
            double len = x1.DistanceTo(x2);
            if (len < MinDimLength || len <= 1e-9)
                return false;
            OtoOlcuDimFactory.PlaceRotated(tr, btr, x1, x2, dimLinePoint, dimStyleId, layer);
            return true;
        }

        private static void CollectIntersections(
            Transaction tr,
            BlockTableRecord btr,
            Point3d a,
            Point3d b,
            HashSet<string> layers,
            List<Point3d> hits)
        {
            var probeSeg = new LineSegment2d(new Point2d(a.X, a.Y), new Point2d(b.X, b.Y));
            int onLayer = 0;
            foreach (ObjectId id in btr)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent))
                    continue;
                if (ent is Dimension || ent is DBText || ent is MText || ent is Hatch)
                    continue;
                if (!LayerInSet(ent.Layer, layers))
                    continue;
                onLayer++;
                try
                {
                    CollectEntityHits(tr, ent, probeSeg, layers, hits, depth: 0);
                }
                catch { }
            }

            // 0 kesişim + katmanda nesne var → teşhis (Z / tip / kapalı poligon)
            if (hits.Count == 0 && onLayer > 0)
            {
                // sessiz; PlaceDimsForSegment mesajı yeterli
            }
        }

        private static bool LayerInSet(string layer, HashSet<string> layers)
        {
            if (layers == null || layers.Count == 0) return false;
            if (string.IsNullOrWhiteSpace(layer)) return false;
            return layers.Contains(layer.Trim());
        }

        private static void CollectEntityHits(
            Transaction tr,
            Entity ent,
            LineSegment2d probeSeg,
            HashSet<string> layers,
            List<Point3d> hits,
            int depth)
        {
            if (ent == null || depth > 4) return;

            if (ent is BlockReference br)
            {
                CollectBlockReferenceHits(tr, br, probeSeg, layers, hits, depth);
                return;
            }

            if (ent is Polyline pl)
            {
                CollectPolylineSegmentHits(pl, probeSeg, hits);
                return;
            }

            if (ent is Polyline2d pl2)
            {
                CollectPolyline2dHits(tr, pl2, probeSeg, hits);
                return;
            }

            if (ent is Line line)
            {
                AddSegHits(
                    new Point2d(line.StartPoint.X, line.StartPoint.Y),
                    new Point2d(line.EndPoint.X, line.EndPoint.Y),
                    probeSeg, hits);
                return;
            }

            if (ent is Arc arc)
            {
                try
                {
                    var c2 = new CircularArc2d(
                        new Point2d(arc.Center.X, arc.Center.Y),
                        arc.Radius,
                        arc.StartAngle,
                        arc.EndAngle,
                        Vector2d.XAxis,
                        false);
                    Point2d[] pts = c2.IntersectWith(probeSeg);
                    if (pts != null)
                        foreach (Point2d p in pts)
                            hits.Add(new Point3d(p.X, p.Y, 0));
                }
                catch { }
                return;
            }

            if (ent is Circle cir)
            {
                try
                {
                    var c2 = new CircularArc2d(
                        new Point2d(cir.Center.X, cir.Center.Y),
                        cir.Radius);
                    Point2d[] pts = c2.IntersectWith(probeSeg);
                    if (pts != null)
                        foreach (Point2d p in pts)
                            hits.Add(new Point3d(p.X, p.Y, 0));
                }
                catch { }
                return;
            }

            // Diğer eğriler: Z farkı IntersectWith'i bozmasın diye XY projeksiyon + yedek
            if (ent is Curve curve)
            {
                try
                {
                    Point3d s = curve.StartPoint;
                    Point3d e = curve.EndPoint;
                    // Kısa düz yaklaşım (spline vb. için yetersiz olabilir) — önce gerçek IntersectWith (düzlem)
                    using (var probe = new Line(
                        new Point3d(probeSeg.StartPoint.X, probeSeg.StartPoint.Y, 0),
                        new Point3d(probeSeg.EndPoint.X, probeSeg.EndPoint.Y, 0)))
                    using (var flat = new Line(
                        new Point3d(s.X, s.Y, 0),
                        new Point3d(e.X, e.Y, 0)))
                    {
                        var pts = new Point3dCollection();
                        // Önce düzleştirilmiş kopya ile
                        flat.IntersectWith(probe, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                        if (pts.Count == 0)
                        {
                            // Plane XY üzerinden
                            var pln = new Plane(Point3d.Origin, Vector3d.ZAxis);
                            curve.IntersectWith(probe, Intersect.OnBothOperands, pln, pts, IntPtr.Zero, IntPtr.Zero);
                        }
                        foreach (Point3d p in pts)
                            hits.Add(new Point3d(p.X, p.Y, 0));
                    }
                }
                catch { }
            }
        }

        private static void CollectBlockReferenceHits(
            Transaction tr,
            BlockReference br,
            LineSegment2d probeSeg,
            HashSet<string> layers,
            List<Point3d> hits,
            int depth)
        {
            try
            {
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                Matrix3d xf = br.BlockTransform;
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity nested))
                        continue;
                    if (nested is Dimension || nested is DBText || nested is MText || nested is Hatch)
                        continue;

                    // Katman: iç entity veya insert
                    string lyr = nested.Layer;
                    if (string.Equals(lyr, "0", StringComparison.OrdinalIgnoreCase))
                        lyr = br.Layer;
                    if (!LayerInSet(lyr, layers) && !LayerInSet(br.Layer, layers))
                        continue;

                    Entity clone = null;
                    try
                    {
                        clone = (Entity)nested.Clone();
                        clone.TransformBy(xf);
                        CollectEntityHits(tr, clone, probeSeg, layers, hits, depth + 1);
                    }
                    catch { }
                    finally
                    {
                        if (clone != null) clone.Dispose();
                    }
                }
            }
            catch { }
        }

        private static void CollectPolyline2dHits(
            Transaction tr,
            Polyline2d pl2,
            LineSegment2d probeSeg,
            List<Point3d> hits)
        {
            var verts = new List<Point2d>();
            foreach (ObjectId vid in pl2)
            {
                if (tr.GetObject(vid, OpenMode.ForRead) is Vertex2d v)
                    verts.Add(new Point2d(v.Position.X, v.Position.Y));
            }
            int n = verts.Count;
            if (n < 2) return;
            int segs = pl2.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
                AddSegHits(verts[i], verts[(i + 1) % n], probeSeg, hits);
        }

        private static void AddSegHits(Point2d a, Point2d b, LineSegment2d probeSeg, List<Point3d> hits)
        {
            try
            {
                Point2d[] pts = new LineSegment2d(a, b).IntersectWith(probeSeg);
                if (pts == null) return;
                foreach (Point2d p in pts)
                    hits.Add(new Point3d(p.X, p.Y, 0));
            }
            catch { }
        }

        private static void CollectPolylineSegmentHits(Polyline pl, LineSegment2d probeSeg, List<Point3d> hits)
        {
            int n = pl.NumberOfVertices;
            if (n < 2) return;
            int segs = pl.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                try
                {
                    Point2d[] pts = null;
                    if (Math.Abs(pl.GetBulgeAt(i)) < 1e-9)
                    {
                        Point3d p0 = pl.GetPoint3dAt(i);
                        Point3d p1 = pl.GetPoint3dAt((i + 1) % n);
                        pts = new LineSegment2d(new Point2d(p0.X, p0.Y), new Point2d(p1.X, p1.Y))
                            .IntersectWith(probeSeg);
                    }
                    else
                        pts = pl.GetArcSegment2dAt(i).IntersectWith(probeSeg);

                    if (pts == null) continue;
                    foreach (Point2d p in pts)
                        hits.Add(new Point3d(p.X, p.Y, 0));
                }
                catch
                {
                    // GetArcSegment2dAt bazı LWPOLYLINE'larda patlar — düz kenar yedek
                    try
                    {
                        Point3d p0 = pl.GetPoint3dAt(i);
                        Point3d p1 = pl.GetPoint3dAt((i + 1) % n);
                        AddSegHits(new Point2d(p0.X, p0.Y), new Point2d(p1.X, p1.Y), probeSeg, hits);
                    }
                    catch { }
                }
            }
        }

        private static void SortAlongLine(Point3d origin, Vector3d dir, List<Point3d> pts)
        {
            Vector3d u = dir.GetNormal();
            pts.Sort((p, q) =>
            {
                double tp = (p - origin).DotProduct(u);
                double tq = (q - origin).DotProduct(u);
                return tp.CompareTo(tq);
            });
        }

        private static void DedupNear(List<Point3d> pts, double tol)
        {
            for (int i = pts.Count - 1; i > 0; i--)
            {
                if (pts[i].DistanceTo(pts[i - 1]) <= tol)
                    pts.RemoveAt(i);
            }
        }

        private static ObjectId ResolveDimStyleId(Transaction tr, Database db, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ObjectId.Null;
            var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (dst.Has(name))
                return dst[name];
            return ObjectId.Null;
        }

        private static string EnsureLayer(Transaction tr, Database db, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = "0";
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name))
                return name;
            lt.UpgradeOpen();
            var lyr = new LayerTableRecord { Name = name };
            lt.Add(lyr);
            tr.AddNewlyCreatedDBObject(lyr, true);
            return name;
        }
    }
}
