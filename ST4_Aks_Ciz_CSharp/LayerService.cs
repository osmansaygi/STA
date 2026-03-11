using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace ST4AksCizCSharp
{
    public static class LayerService
    {
        public static void EnsureLayer(Transaction tr, Database db, string layerName, short colorIndex)
        {
            EnsureLayer(tr, db, layerName, colorIndex, LineWeight.LineWeight000);
        }

        public static void EnsureLayer(Transaction tr, Database db, string layerName, short colorIndex, LineWeight lineWeight)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName))
            {
                var rec = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
                rec.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
                if (lineWeight != LineWeight.LineWeight000)
                    rec.LineWeight = lineWeight;
                return;
            }
            lt.UpgradeOpen();
            var newRec = new LayerTableRecord
            {
                Name = layerName,
                Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex),
                LineWeight = lineWeight
            };
            lt.Add(newRec);
            tr.AddNewlyCreatedDBObject(newRec, true);
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
