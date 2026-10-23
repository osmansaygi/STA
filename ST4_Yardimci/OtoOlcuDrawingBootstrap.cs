using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Katmanları oluşturur; standart ölçü stillerini tanımlı ayarlarıyla oluşturur/günceller.
    /// Ayarlar %LocalAppData%\ST4Yardimci\otoolcu.ini içinde AutoCAD kapansa da kalır.
    /// </summary>
    internal static class OtoOlcuDrawingBootstrap
    {
        /// <summary>OTOOLCU açılınca çağrılır.</summary>
        public static void EnsureForActiveDocument()
        {
            OtoOlcuSettings.EnsureLoaded();
            OtoOlcuSettings.Save(); // diske yazılmış olsun

            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;

            List<string> layers;
            List<string> dimStyles;
            OtoOlcuSettings.CollectAllReferencedNames(out layers, out dimStyles);

            // Ölçü katmanları ve standart stil aileleri çizimde hazır olsun.
            if (!layers.Exists(n => string.Equals(n, OtoOlcuSettings.DefaultDimLayerName, StringComparison.OrdinalIgnoreCase)))
                layers.Add(OtoOlcuSettings.DefaultDimLayerName);
            if (!layers.Exists(n => string.Equals(n, OtoOlcuSettings.AksOlcuDimLayerName, StringComparison.OrdinalIgnoreCase)))
                layers.Add(OtoOlcuSettings.AksOlcuDimLayerName);
            // Standart ölçü ailelerini eksiksiz ayarlarıyla oluştur/güncelle.
            dimStyles.Clear();
            dimStyles.Add(OtoOlcuSettings.AksOlcuDimStyleName);
            dimStyles.Add(OtoOlcuSettings.DefaultDimStyleName);
            dimStyles.Add("PLAN_OLCU_DONATI");
            dimStyles.Add("PLAN_OLCU 25");
            dimStyles.Add("PLAN_OLCU_DONATI 25");

            int addedL = 0, addedD = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (string lyr in layers)
                {
                    if (EnsureLayer(tr, db, lyr))
                        addedL++;
                }
                foreach (string st in dimStyles)
                {
                    if (EnsureDimStyle(tr, db, st))
                        addedD++;
                }
                tr.Commit();
            }

            try
            {
                if (addedL > 0 || addedD > 0)
                {
                    doc.Editor.WriteMessage(
                        "\nOto Ölçü: oluşturulan katman={0}, ölçü stili={1}.",
                        addedL, addedD);
                }
            }
            catch { }
        }

        /// <summary>Yoksa oluşturur; true = yeni oluşturuldu.</summary>
        private static bool EnsureLayer(Transaction tr, Database db, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            name = name.Trim();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            bool existed = lt.Has(name);
            short aci = 7;
            LineWeight lw = LineWeight.LineWeight020;
            if (string.Equals(name, OtoOlcuSettings.AksOlcuDimLayerName, StringComparison.OrdinalIgnoreCase))
                aci = 6;
            else if (name.IndexOf("OLCU", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                aci = 3;
                lw = LineWeight.LineWeight030;
            }
            BeykentKatmanYukle.Ensure(tr, db, name, aci, lw);
            return !existed && ((LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead)).Has(name);
        }

        /// <summary>Yoksa oluşturur; true = yeni oluşturuldu.</summary>
        private static bool EnsureDimStyle(Transaction tr, Database db, string name)
        {
            var table = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            bool existed = table.Has(name);
            ObjectId id = BaDimensionStyles.Ensure(tr, db, name);
            return !existed && !id.IsNull;
        }
        
    }
}
