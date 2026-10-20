using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        private Extents3d? _kolonDuseyLastMainAntetBounds;

        private double DrawApplicationVerticalDetails(
            Database db, Editor ed, string st4SourcePath,
            List<int> floorGroup, double left, double bottom)
        {
            var ids = new List<ObjectId>();
            _kolonDuseyLastMainAntetBounds = null;
            ed?.WriteMessage("\nAPLIKASYON: {0} — antetli 1/25 kolon dusey acilimi basliyor.",
                _model.Floors[floorGroup[0]].Name);
            // Aplikasyon nesneleri kapatılmıştır; standart düşey komutuyla aynı işlem sınırı.
            using (var drawTr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)drawTr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)drawTr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                var before = SnapshotKolon50BtrIds(btr);
                bool ok = DrawKolonDuseyFromSt4(Point3d.Origin, db, ed, drawTr, btr, st4SourcePath,
                    olcek25: true, katKatCiz: true, benzerKatBirlestir: true,
                    drawingFloorIndexes: new HashSet<int>(floorGroup));
                if (!ok || !_kolonDuseyLastMainAntetBounds.HasValue)
                    throw new InvalidOperationException("Antetli 1/25 kolon düşey açılımı tamamlanamadı.");
                foreach (ObjectId id in btr)
                    if (!id.IsNull && !id.IsErased && !before.Contains(id)) ids.Add(id);
                drawTr.Commit();
            }

            // Ölçülerin GeometricExtents'ini zorlamadan, hesaplanan dış antet kutusuyla yerleştir.
            var bounds = _kolonDuseyLastMainAntetBounds.Value;
            var move = Matrix3d.Displacement(new Vector3d(
                left - bounds.MinPoint.X, bottom - bounds.MinPoint.Y, 0));
            using (var moveTr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                    if (!id.IsErased)
                        ((Entity)moveTr.GetObject(id, OpenMode.ForWrite)).TransformBy(move);
                moveTr.Commit();
            }
            ed?.WriteMessage("\nAPLIKASYON: antetli 1/25 acilim saga yerlestirildi.");
            return left + bounds.MaxPoint.X - bounds.MinPoint.X;
        }
    }
}