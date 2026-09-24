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
    /// AKS OLCU şablonu: aks çizgilerini seç → balon tarafını seç →
    /// balona 35 cm toplam + 20 cm içeride ara ölçüler (akslara dik).
    /// </summary>
    internal static class OtoOlcuAksRunner
    {
        private const double MinDimLength = 0.2;

        public static void RunInteractive()
        {
            OtoOlcuSettings.EnsureLoaded();
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
                OtoOlcuDrawingBootstrap.EnsureForActiveDocument();
                ed.WriteMessage(
                    "\nAKS OLCU: Aks çizgilerini seçin, sonra ölçü tarafı için aks balonunu seçin. Esc = bitir.");

                while (true)
                {
                    var axisIds = PickAxisCurves(ed);
                    if (axisIds == null || axisIds.Count < 2)
                    {
                        if (axisIds != null && axisIds.Count == 1)
                            ed.WriteMessage("\nAKS OLCU: En az 2 aks çizgisi seçin.");
                        break;
                    }

                    BalloonPick? balloon = PickBalloon(ed, doc);
                    if (balloon == null)
                        break;

                    int placed = PlacePairDims(doc, db, ed, axisIds, balloon.Value);
                    ed.WriteMessage("\nAKS OLCU: {0} ölçü yazıldı. Devam için aks seçin (Esc = bitir).", placed);
                }
            }
            finally
            {
                OtoOlcuForm.ShowAfterPick();
            }
        }

        private static List<ObjectId> PickAxisCurves(Editor ed)
        {
            var opts = new PromptSelectionOptions
            {
                MessageForAdding = "\nÖlçü atılacak aks çizgilerini seçin: ",
                AllowDuplicates = false
            };
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, "LINE,LWPOLYLINE,POLYLINE,ARC")
            });
            PromptSelectionResult res = ed.GetSelection(opts, filter);
            if (res.Status != PromptStatus.OK || res.Value == null)
                return null;

            var ids = new List<ObjectId>();
            foreach (SelectedObject so in res.Value)
            {
                if (so != null && !so.ObjectId.IsNull)
                    ids.Add(so.ObjectId);
            }
            return ids;
        }

        private struct BalloonPick
        {
            public Point3d Center;
            /// <summary>Balon dış kenarına kadar yarıçap / yarı-extent (cm).</summary>
            public double Radius;
        }

        private static BalloonPick? PickBalloon(Editor ed, Document doc)
        {
            var opts = new PromptEntityOptions(
                "\nÖlçü tarafı için aks balonunu seçin: ")
            {
                AllowNone = false
            };
            opts.SetRejectMessage("\nBalon (daire/blok/yazı) seçin.");
            opts.AddAllowedClass(typeof(Circle), exactMatch: false);
            opts.AddAllowedClass(typeof(BlockReference), exactMatch: false);
            opts.AddAllowedClass(typeof(DBText), exactMatch: false);
            opts.AddAllowedClass(typeof(MText), exactMatch: false);
            opts.AddAllowedClass(typeof(Ellipse), exactMatch: false);
            opts.AddAllowedClass(typeof(Polyline), exactMatch: false);

            PromptEntityResult res = ed.GetEntity(opts);
            if (res.Status != PromptStatus.OK)
                return null;

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ent = tr.GetObject(res.ObjectId, OpenMode.ForRead) as Entity;
                if (ent == null)
                {
                    tr.Commit();
                    return null;
                }
                Point3d c = GetEntityCenter(ent);
                double r = GetBalloonRadius(ent, c);
                tr.Commit();
                return new BalloonPick { Center = c, Radius = r };
            }
        }

        private static Point3d GetEntityCenter(Entity ent)
        {
            if (ent is Circle cir)
                return cir.Center;
            if (ent is BlockReference br)
                return br.Position;
            if (ent is DBText txt)
                return txt.AlignmentPoint.DistanceTo(Point3d.Origin) > 1e-9
                    ? txt.AlignmentPoint
                    : txt.Position;
            if (ent is MText mt)
                return mt.Location;
            try
            {
                Extents3d ext = ent.GeometricExtents;
                return new Point3d(
                    (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                    0);
            }
            catch
            {
                return Point3d.Origin;
            }
        }

        /// <summary>35 cm mesafe balonun dış kenarından (merkezden değil) ölçülür.</summary>
        private static double GetBalloonRadius(Entity ent, Point3d center)
        {
            if (ent is Circle cir)
                return Math.Max(0, cir.Radius);
            if (ent is Ellipse el)
                return Math.Max(el.MajorRadius, el.MinorRadius);
            try
            {
                Extents3d ext = ent.GeometricExtents;
                double hx = Math.Abs(ext.MaxPoint.X - center.X);
                double hy = Math.Abs(ext.MaxPoint.Y - center.Y);
                double hx2 = Math.Abs(center.X - ext.MinPoint.X);
                double hy2 = Math.Abs(center.Y - ext.MinPoint.Y);
                return Math.Max(Math.Max(hx, hx2), Math.Max(hy, hy2));
            }
            catch
            {
                return 0;
            }
        }

        private static int PlacePairDims(
            Document doc,
            Database db,
            Editor ed,
            List<ObjectId> axisIds,
            BalloonPick balloon)
        {
            Point3d balloonCenter = balloon.Center;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var curves = new List<Curve>();
                foreach (ObjectId id in axisIds)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Curve c)
                        curves.Add(c);
                }
                if (curves.Count < 2)
                {
                    ed.WriteMessage("\nAKS OLCU: Geçerli aks eğrisi yok.");
                    tr.Commit();
                    return 0;
                }

                Vector3d axisDir;
                if (!TryGetAverageAxisDir(curves, out axisDir))
                {
                    ed.WriteMessage("\nAKS OLCU: Aks yönü belirlenemedi.");
                    tr.Commit();
                    return 0;
                }

                Vector3d u = new Vector3d(axisDir.X, axisDir.Y, 0);
                if (u.Length < 1e-9)
                {
                    ed.WriteMessage("\nAKS OLCU: Aks yönü geçersiz.");
                    tr.Commit();
                    return 0;
                }
                u = u.GetNormal();
                Vector3d n = new Vector3d(-u.Y, u.X, 0);
                if (n.Length < 1e-9)
                    n = Vector3d.XAxis;
                else
                    n = n.GetNormal();

                // Her aks: balona en yakın nokta → N boyunca sıra
                var samples = new List<(double t, Point3d near)>();
                Point3d centroid = Point3d.Origin;
                int cnt = 0;
                foreach (Curve c in curves)
                {
                    Point3d near;
                    try { near = c.GetClosestPointTo(balloonCenter, false); }
                    catch { continue; }
                    double t = (near - balloonCenter).DotProduct(n);
                    samples.Add((t, near));
                    centroid = new Point3d(centroid.X + near.X, centroid.Y + near.Y, 0);
                    cnt++;
                }
                if (samples.Count < 2)
                {
                    ed.WriteMessage("\nAKS OLCU: Yeterli aks noktası yok.");
                    tr.Commit();
                    return 0;
                }
                centroid = new Point3d(centroid.X / cnt, centroid.Y / cnt, 0);
                samples.Sort((a, b) => a.t.CompareTo(b.t));

                // Aynı hizadaki tekrarları birleştir
                var sortedT = new List<double>();
                foreach (var s in samples)
                {
                    if (sortedT.Count == 0 || Math.Abs(sortedT[sortedT.Count - 1] - s.t) > MinDimLength)
                        sortedT.Add(s.t);
                }
                if (sortedT.Count < 2)
                {
                    ed.WriteMessage("\nAKS OLCU: Akslar örtüşüyor / mesafe yetersiz.");
                    tr.Commit();
                    return 0;
                }

                // Balondan içeri (aks gövdesine doğru) U yönü
                Vector3d toMid = centroid - balloonCenter;
                Vector3d inward = toMid.DotProduct(u) >= 0 ? u : -u;

                // 35 cm = balon dış kenarından (yarıçap + 35)
                double offTotal = balloon.Radius + OtoOlcuSettings.AksOlcuTotalOffsetCm;
                double offInd = offTotal + OtoOlcuSettings.AksOlcuIntervalGapCm;

                var ptsTotal = new List<Point3d>();
                var ptsInd = new List<Point3d>();
                foreach (double t in sortedT)
                {
                    Point3d baseOnN = balloonCenter + n * t;
                    ptsTotal.Add(baseOnN + inward * offTotal);
                    ptsInd.Add(baseOnN + inward * offInd);
                }

                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                ObjectId dimStyleId = ResolveDimStyleId(tr, db, OtoOlcuSettings.AksOlcuDimStyleName);
                string layer = EnsureLayer(tr, db, OtoOlcuSettings.AksOlcuDimLayerName);

                int placed = 0;

                // Toplam (balona yakın sıra)
                Point3d aT = ptsTotal[0];
                Point3d bT = ptsTotal[ptsTotal.Count - 1];
                if (aT.DistanceTo(bT) >= MinDimLength)
                {
                    PlaceAligned(tr, btr, aT, bT, Mid(aT, bT), dimStyleId, layer);
                    placed++;
                }

                // Ara ölçüler
                for (int i = 0; i < ptsInd.Count - 1; i++)
                {
                    Point3d a = ptsInd[i];
                    Point3d b = ptsInd[i + 1];
                    if (a.DistanceTo(b) < MinDimLength)
                        continue;
                    PlaceAligned(tr, btr, a, b, Mid(a, b), dimStyleId, layer);
                    placed++;
                }

                tr.Commit();
                return placed;
            }
        }

        private static bool TryGetAverageAxisDir(List<Curve> curves, out Vector3d dir)
        {
            dir = Vector3d.XAxis;
            Vector3d sum = new Vector3d(0, 0, 0);
            int n = 0;
            foreach (Curve c in curves)
            {
                try
                {
                    double mid = (c.StartParam + c.EndParam) * 0.5;
                    Vector3d d = c.GetFirstDerivative(mid);
                    d = new Vector3d(d.X, d.Y, 0);
                    if (d.Length < 1e-9) continue;
                    d = d.GetNormal();
                    // İlk geçerli yönle aynı yarımküreye çevir
                    if (n > 0 && sum.DotProduct(d) < 0)
                        d = -d;
                    sum += d;
                    n++;
                }
                catch { }
            }
            if (n == 0) return false;
            if (sum.Length < 1e-9) return false;
            dir = sum.GetNormal();
            return true;
        }

        private static Point3d Mid(Point3d a, Point3d b) =>
            new Point3d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, 0);

        private static void PlaceAligned(
            Transaction tr,
            BlockTableRecord btr,
            Point3d x1,
            Point3d x2,
            Point3d dimLinePoint,
            ObjectId dimStyleId,
            string layer)
        {
            var dim = new AlignedDimension(x1, x2, dimLinePoint, "", dimStyleId);
            dim.SetDatabaseDefaults();
            if (!dimStyleId.IsNull)
                dim.DimensionStyle = dimStyleId;
            dim.Layer = layer;
            try { dim.Dimrnd = 0.5; } catch { }
            try { dim.Dimdec = 1; } catch { }
            try { dim.Dimtdec = 1; } catch { }
            try { dim.Dimzin = 12; } catch { }
            btr.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
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
                name = OtoOlcuSettings.AksOlcuDimLayerName;
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name))
                return name;
            lt.UpgradeOpen();
            var lyr = new LayerTableRecord
            {
                Name = name,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 6),
                LineWeight = LineWeight.LineWeight020
            };
            lt.Add(lyr);
            tr.AddNewlyCreatedDBObject(lyr, true);
            return name;
        }
    }
}
