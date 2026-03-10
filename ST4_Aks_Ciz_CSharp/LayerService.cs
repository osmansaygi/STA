using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace ST4AksCizCSharp
{
    public static class LayerService
    {
        public static void EnsureLayer(Transaction tr, Database db, string layerName, short colorIndex)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName)) return;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            {
                Name = layerName,
                Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex)
            };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        public static void SetLayerOn(Transaction tr, Database db, string layerName, bool on)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName)) return;
            var rec = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
            rec.IsOff = !on;
        }
    }
}
