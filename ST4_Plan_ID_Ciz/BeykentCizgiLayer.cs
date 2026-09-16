using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace ST4PlanIdCiz
{
    /// <summary>CIZGI (BEYKENT): ACI 1 kırmızı, Continuous, 0.20 mm. Boş olsa da katman listesinde kalır.</summary>
    internal static class BeykentCizgiLayer
    {
        public const string Name = "CIZGI (BEYKENT)";

        public static void Ensure(Database db)
        {
            if (db == null) return;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Ensure(tr, db);
                tr.Commit();
            }
        }

        public static void Ensure(Transaction tr, Database db)
        {
            if (tr == null || db == null) return;
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            LayerTableRecord rec;
            if (lt.Has(Name))
            {
                rec = (LayerTableRecord)tr.GetObject(lt[Name], OpenMode.ForWrite);
            }
            else
            {
                lt.UpgradeOpen();
                rec = new LayerTableRecord { Name = Name };
                lt.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }
            rec.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
            rec.LineWeight = LineWeight.LineWeight020;
            try { rec.IsOff = false; } catch { }
            try
            {
                var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (ltt.Has("Continuous"))
                    rec.LinetypeObjectId = ltt["Continuous"];
            }
            catch { }
        }
    }
}
