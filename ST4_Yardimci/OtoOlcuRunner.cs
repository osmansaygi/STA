using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Sanal doğru; 1. / 2. katman listelerine göre ölçü.
    /// Ext çizgi ≥ yazı yüksekliği / 2. Küçük ölçüler (&lt; 0.2) yazılmaz.
    /// </summary>
    internal static class OtoOlcuRunner
    {
        private const double MinDimLength = 0.2;

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

                var set1 = new HashSet<string>(layers1, StringComparer.OrdinalIgnoreCase);
                var set2 = (cift && layers2.Count > 0)
                    ? new HashSet<string>(layers2, StringComparer.OrdinalIgnoreCase)
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
                double textH = GetDimTextHeight(tr, dimStyleId);
                double minExe = textH * 0.5;
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

                int placed = 0;
                int skipped = 0;

                // 1. ölçü: ara ölçüler (sanal çizgide)
                if (hits1.Count >= 2)
                {
                    for (int i = 0; i < hits1.Count - 1; i++)
                    {
                        if (TryPlaceDim(tr, btr, hits1[i], hits1[i + 1], dimOnVirtual, dimStyleId, dimLayer, minExe))
                            placed++;
                        else
                            skipped++;
                    }
                }
                else
                {
                    ed.WriteMessage("\nOTOOLCUCIZ: 1. ölçü — yetersiz kesişim ({0}).", hits1.Count);
                }

                // İkinci ölçü toplam: 1. listenin dıştan dışa toplamı (2. listeden bağımsız)
                if (ikinciToplam && hits1.Count >= 2)
                {
                    Point3d dimTotal = dimOnVirtual + perp * gap;
                    if (TryPlaceDim(tr, btr, hits1[0], hits1[hits1.Count - 1], dimTotal, dimStyleId, dimLayer, minExe))
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

                    Point3d dimRow2 = dimOnVirtual + perp * gap;

                    if (hits2.Count >= 2)
                    {
                        for (int i = 0; i < hits2.Count - 1; i++)
                        {
                            if (TryPlaceDim(tr, btr, hits2[i], hits2[i + 1], dimRow2, dimStyleId, dimLayer, minExe))
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
            string layer,
            double minExe)
        {
            double len = x1.DistanceTo(x2);
            if (len < MinDimLength || len <= 1e-9)
                return false;
            PlaceRotatedDim(tr, btr, x1, x2, dimLinePoint, dimStyleId, layer, minExe);
            return true;
        }

        private static double GetDimTextHeight(Transaction tr, ObjectId dimStyleId)
        {
            double txt = 2.5;
            double scale = 1.0;
            try
            {
                if (!dimStyleId.IsNull)
                {
                    var rec = tr.GetObject(dimStyleId, OpenMode.ForRead) as DimStyleTableRecord;
                    if (rec != null)
                    {
                        txt = rec.Dimtxt;
                        scale = rec.Dimscale;
                        if (scale <= 1e-9) scale = 1.0;
                    }
                }
            }
            catch { }
            if (txt <= 1e-9) txt = 2.5;
            return txt * scale;
        }

        private static void PlaceRotatedDim(
            Transaction tr,
            BlockTableRecord btr,
            Point3d x1,
            Point3d x2,
            Point3d dimLinePoint,
            ObjectId dimStyleId,
            string layer,
            double minExe)
        {
            var dim = new RotatedDimension();
            dim.SetDatabaseDefaults();
            Vector3d v = x2 - x1;
            dim.Rotation = Math.Atan2(v.Y, v.X);
            dim.XLine1Point = x1;
            dim.XLine2Point = x2;
            dim.DimLinePoint = dimLinePoint;
            if (!dimStyleId.IsNull)
                dim.DimensionStyle = dimStyleId;
            dim.Layer = layer;

            // Ext çizgi: 0 olmasın, en az yazı yüksekliğinin yarısı
            try
            {
                if (dim.Dimexe < minExe) dim.Dimexe = minExe;
                if (dim.Dimexo < minExe) dim.Dimexo = minExe;
            }
            catch { }

            btr.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
        }

        private static void CollectIntersections(
            Transaction tr,
            BlockTableRecord btr,
            Point3d a,
            Point3d b,
            HashSet<string> layers,
            List<Point3d> hits)
        {
            using (var probe = new Line(a, b))
            {
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent))
                        continue;
                    if (ent is Dimension || ent is DBText || ent is MText || ent is Hatch)
                        continue;
                    if (!layers.Contains(ent.Layer))
                        continue;

                    try
                    {
                        var pts = new Point3dCollection();
                        ent.IntersectWith(probe, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                        foreach (Point3d p in pts)
                            hits.Add(p);
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
