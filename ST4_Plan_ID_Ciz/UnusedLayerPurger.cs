using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// Hiçbir nesnenin kullanmadığı katmanları siler (0 / Defpoints / güncel / xref hariç).
    /// STA komutları tüm katman tablosunu önceden açtığı ve antet/şablon ekstra katman getirdiği için katman listesi şişer.
    /// Çizim sonunda FinishStaDrawing çağırır. CIZGI (BEYKENT) boş olsa da silinmez.
    /// </summary>
    internal static class UnusedLayerPurger
    {
        public static int PurgeUnusedLayers(Database db, Editor ed = null)
        {
            if (db == null) return 0;
            int total = 0;
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    int n = PurgeUnusedLayersOnce(db);
                    total += n;
                    if (n == 0) break;
                }
            }
            catch (System.Exception ex)
            {
                try { ed?.WriteMessage("\nKatman temizleme atlandi: {0}", ex.Message); } catch { }
                return total;
            }
            if (total > 0)
            {
                try { ed?.WriteMessage("\nKullanilmayan katman silindi: {0}", total); } catch { }
            }
            return total;
        }

        private static int PurgeUnusedLayersOnce(Database db)
        {
            int erased = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                var ids = new ObjectIdCollection();
                ObjectId current = db.Clayer;
                foreach (ObjectId lid in lt)
                {
                    if (!lid.IsValid || lid.IsErased) continue;
                    if (lid == current) continue;
                    var rec = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
                    if (rec == null) continue;
                    string name = rec.Name ?? "";
                    if (string.Equals(name, "0", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(name, "Defpoints", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(name, BeykentCizgiLayer.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (rec.IsDependent) continue;
                    ids.Add(lid);
                }
                if (ids.Count == 0)
                {
                    tr.Commit();
                    return 0;
                }
                db.Purge(ids);
                foreach (ObjectId id in ids)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    try
                    {
                        var rec = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                        rec.Erase();
                        erased++;
                    }
                    catch
                    {
                    }
                }
                tr.Commit();
            }
            return erased;
        }
    }
}
