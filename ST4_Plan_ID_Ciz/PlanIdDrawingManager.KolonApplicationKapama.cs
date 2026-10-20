using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        private double DrawApplicationKapamaDetails(
            Database db, Editor ed, string st4SourcePath, double left, double bottom, bool temelHatili)
        {
            ed?.WriteMessage("\nAPLIKASYON: kapama perde acilimi ekleniyor.");
            // Mevcut KAPAMADETAY akışı kendi antetini ve tüm katların panel açılımını üretir.
            // Aplikasyon ve 1/25 kolon düşeyi bu işlem sırasında açık tutulmaz.
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                var before = SnapshotKolon50BtrIds(btr);
                var origin = new Point3d(left - AntetYerlesimSheetViewOutSolPayCm, bottom, 0);
                if (!DrawKapamaDetayFromSt4(origin, db, ed, tr, btr, st4SourcePath, temelHatili))
                    return left; // Panel yoksa kısmi çizimi kaydetme; diğer paftalar normal devam eder.
                var ids = new List<ObjectId>();
                foreach (ObjectId id in btr)
                    if (!id.IsNull && !id.IsErased && !before.Contains(id)) ids.Add(id);
                // Sadece dış antet katmanını oku; donatı ölçülerinin extents'ini hesaplatma.
                double right = GetMaxXOfEntitiesOnLayer(tr, ids, AntetSheetViewOutLayerName, left);
                if (right <= left + 0.01)
                    throw new InvalidOperationException("Kapama perde açılımının dış anteti oluşturulamadı.");
                tr.Commit();
                return right;
            }
        }
    }
}
