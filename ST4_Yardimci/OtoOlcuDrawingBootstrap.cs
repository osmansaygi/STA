using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Şablonlarda kayıtlı katman / ölçü stilleri çizimde yoksa oluşturur.
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

            // Ölçü çizim katmanı her zaman
            if (!layers.Exists(n => string.Equals(n, "OLCU (BEYKENT)", StringComparison.OrdinalIgnoreCase)))
                layers.Add("OLCU (BEYKENT)");
            if (!dimStyles.Exists(n => string.Equals(n, "OLCU (BEYKENT)", StringComparison.OrdinalIgnoreCase)))
                dimStyles.Add("OLCU (BEYKENT)");
            if (!layers.Exists(n => string.Equals(n, OtoOlcuSettings.AksOlcuDimLayerName, StringComparison.OrdinalIgnoreCase)))
                layers.Add(OtoOlcuSettings.AksOlcuDimLayerName);
            if (!dimStyles.Exists(n => string.Equals(n, OtoOlcuSettings.AksOlcuDimStyleName, StringComparison.OrdinalIgnoreCase)))
                dimStyles.Add(OtoOlcuSettings.AksOlcuDimStyleName);

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
            if (lt.Has(name)) return false;

            lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = name };

            if (string.Equals(name, OtoOlcuSettings.AksOlcuDimLayerName, StringComparison.OrdinalIgnoreCase))
            {
                // AKS OLCU (BEYKENT) — magenta ACI 6, Continuous 0.20
                try { rec.Color = Color.FromColorIndex(ColorMethod.ByAci, 6); } catch { }
                try { rec.LineWeight = LineWeight.LineWeight020; } catch { }
            }
            else if (name.IndexOf("OLCU", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // OLCU (BEYKENT) → yeşil ACI 3, Continuous 0.30
                try { rec.Color = Color.FromColorIndex(ColorMethod.ByAci, 3); } catch { }
                try { rec.LineWeight = LineWeight.LineWeight030; } catch { }
            }
            else
            {
                try { rec.Color = Color.FromColorIndex(ColorMethod.ByAci, 7); } catch { }
                try { rec.LineWeight = LineWeight.LineWeight020; } catch { }
            }
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return true;
        }

        /// <summary>Yoksa oluşturur; true = yeni oluşturuldu.</summary>
        private static bool EnsureDimStyle(Transaction tr, Database db, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            name = name.Trim();
            var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (dst.Has(name)) return false;

            var rec = new DimStyleTableRecord { Name = name };

            // Standard'dan kopyala (varsa)
            try
            {
                if (dst.Has("Standard"))
                {
                    var std = tr.GetObject(dst["Standard"], OpenMode.ForRead) as DimStyleTableRecord;
                    if (std != null)
                    {
                        rec.CopyFrom(std);
                        rec.Name = name;
                    }
                }
            }
            catch { }

            if (string.Equals(name, OtoOlcuSettings.AksOlcuDimStyleName, StringComparison.OrdinalIgnoreCase))
                ApplyAksOlcuDimStyle(rec);
            else
            {
                // Makul varsayılanlar (cm çizim)
                try { rec.Dimtxt = 2.5; } catch { }
                try { rec.Dimscale = 1.0; } catch { }
                try { rec.Dimexe = 1.25; } catch { }
                try { rec.Dimexo = 1.25; } catch { }
                try { rec.Dimasz = 2.5; } catch { }
                try { rec.Dimtad = 1; } catch { }
                try { rec.Dimtih = false; } catch { }
                try { rec.Dimtoh = false; } catch { }
                try { rec.Dimtofl = true; } catch { }
                try { rec.Dimlfac = 1.0; } catch { }
                try { rec.Dimdec = 0; } catch { }
                try { rec.Dimclrt = Color.FromColorIndex(ColorMethod.ByAci, 3); } catch { }
            }

            dst.UpgradeOpen();
            dst.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return true;
        }

        /// <summary>Plan_ID AKS_OLCU ile aynı: yazı 12, ok/tik 3, exo 3, gap 2, 1 ondalık.</summary>
        private static void ApplyAksOlcuDimStyle(DimStyleTableRecord rec)
        {
            try { rec.Dimscale = 1.0; } catch { }
            try { rec.Dimlfac = 1.0; } catch { }
            try { rec.Dimtxt = 12.0; } catch { }
            try { rec.Dimblk = ObjectId.Null; } catch { }
            try { rec.Dimblk1 = ObjectId.Null; } catch { }
            try { rec.Dimblk2 = ObjectId.Null; } catch { }
            try { rec.Dimldrblk = ObjectId.Null; } catch { }
            try { rec.Dimasz = 3.0; } catch { }
            try { rec.Dimtsz = 3.0; } catch { }
            try { rec.Dimexo = 3.0; } catch { }
            try { rec.Dimgap = 2.0; } catch { }
            try { rec.Dimtix = true; } catch { }
            try { rec.Dimtad = 1; } catch { }
            try { rec.Dimtih = false; } catch { }
            try { rec.Dimtoh = false; } catch { }
            try { rec.Dimtofl = true; } catch { }
            try { rec.Dimaunit = 0; } catch { }
            try { rec.Dimadec = 0; } catch { }
            try { rec.Dimrnd = 0.5; } catch { }
            try { rec.Dimdec = 1; } catch { }
            try { rec.Dimtdec = 1; } catch { }
            try { rec.Dimzin = 12; } catch { }
            try { rec.Dimlunit = 2; } catch { }
            try { rec.Dimclrt = Color.FromColorIndex(ColorMethod.ByAci, 7); } catch { }
        }
    }
}
