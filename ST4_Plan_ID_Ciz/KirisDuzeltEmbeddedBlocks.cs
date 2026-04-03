using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// KIRISDUZELT: yalnızca DONATI_TAR gömülü DXF’ten blok; KOT/KES_DET/DONATI artık kodla çizilir (Runner).
    /// </summary>
    internal static class KirisDuzeltEmbeddedBlocks
    {
        public const string ResDonatiTar = "ST4PlanIdCiz.KirisDuzeltBlocks.DONATI_TAR.dxf";

        private static readonly (string ResourceName, string TargetBlockName, string[] SourceNameTryOrder)[] Specs =
        {
            (ResDonatiTar, "DONATI_TAR", new[] { "DONATI_TAR", "arrowblock", "_Open30" })
        };

        /// <summary>DXF ile aynı blok tanımı: mümkünse eski hedef silinir, tam BTR klon + gerekirse yeniden adlandırma.</summary>
        public static void TryEnsureAllBlocks(Transaction tr, Database db, Editor ed)
        {
            foreach (string legacyName in new[] { "KOT", "KES_DET", "DONATI" })
                TryForceEraseBlockDefinition(tr, db, legacyName, ed, out _);

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (var spec in Specs)
            {
                if (!TryForceEraseBlockDefinition(tr, db, spec.TargetBlockName, ed, out string eraseErr))
                {
                    ed?.WriteMessage("\nKIRISDUZELT: '{0}' DXF ile guncellenemedi: {1}", spec.TargetBlockName, eraseErr);
                    continue;
                }

                // arrowblock: donati isaretci INSERT’leri ReplaceDonatiCirclesWithBlock sonrasina birakilir; burada silinmez.

                bool ok = TryInstallBlockFromEmbeddedDxf(tr, db, spec.ResourceName, spec.TargetBlockName, spec.SourceNameTryOrder, out string err);

                if (ok)
                    ed?.WriteMessage("\nKIRISDUZELT: '{0}' DXF'ten tam blok olarak yuklendi.", spec.TargetBlockName);
                else if (!string.IsNullOrEmpty(err))
                    ed?.WriteMessage("\nKIRISDUZELT: '{0}' yuklenemedi: {1}", spec.TargetBlockName, err);

                bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            }
        }

        /// <summary>Donati yerlestirmeden sonra kullanilmayan <c>arrowblock</c> tanimini temizlemek icin (INSERT’ler once ReplaceDonati ile silinmeli).</summary>
        internal static void TryPurgeArrowBlockDefinition(Transaction tr, Database db, Editor ed)
        {
            TryForceEraseBlockDefinition(tr, db, "arrowblock", ed, out _);
        }

        /// <summary>
        /// Önce çizimdeki tüm INSERT’leri (model, layout, iç bloklar; dinamik blok) kaldırır, sonra blok tanımını siler.
        /// </summary>
        private static bool TryForceEraseBlockDefinition(Transaction tr, Database db, string blockName, Editor ed, out string error)
        {
            error = null;
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(blockName))
                return true;

            ObjectId targetBtrId = bt[blockName];
            int removed = EraseAllInsertsOfBlockDefinition(tr, db, targetBtrId);
            if (removed > 0)
                ed?.WriteMessage("\nKIRISDUZELT: '{0}' icin {1} adet INSERT kaldirildi (DXF ile yenileme).", blockName, removed);

            try
            {
                var btr = (BlockTableRecord)tr.GetObject(targetBtrId, OpenMode.ForWrite);
                btr.Erase();
                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool BlockReferenceUsesDefinition(BlockReference br, ObjectId targetBtrId)
        {
            if (br == null || targetBtrId.IsNull)
                return false;
            if (br.BlockTableRecord == targetBtrId)
                return true;
            try
            {
                if (br.IsDynamicBlock && br.DynamicBlockTableRecord == targetBtrId)
                    return true;
            }
            catch
            {
                // ignored
            }

            return false;
        }

        /// <summary>Tüm <see cref="BlockTableRecord"/> içinde bu tanıma işaret eden <see cref="BlockReference"/> öğelerini siler.</summary>
        private static int EraseAllInsertsOfBlockDefinition(Transaction tr, Database db, ObjectId targetBtrId)
        {
            int count = 0;
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId btrId in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                if (btr.IsFromExternalReference)
                    continue;

                var toErase = new List<ObjectId>();
                foreach (ObjectId entId in btr)
                {
                    if (entId.IsNull || entId.IsErased)
                        continue;
                    var ent = tr.GetObject(entId, OpenMode.ForRead, false) as Entity;
                    if (ent is BlockReference br && BlockReferenceUsesDefinition(br, targetBtrId))
                        toErase.Add(entId);
                }

                foreach (ObjectId eid in toErase)
                {
                    var e = (Entity)tr.GetObject(eid, OpenMode.ForWrite);
                    e.Erase();
                    count++;
                }
            }

            return count;
        }

        /// <summary>Kaynak DXF’teki <see cref="BlockTableRecord"/> nesnesinin tamamını hedef <see cref="BlockTable"/>a klonlar (WblockCloneObjects).</summary>
        private static bool TryInstallBlockFromEmbeddedDxf(
            Transaction trDest,
            Database destDb,
            string resourceName,
            string targetBlockName,
            string[] sourceNameTryOrder,
            out string error)
        {
            error = null;
            string tmpDxf = null;
            Database srcDb = null;
            try
            {
                tmpDxf = ExtractEmbeddedDxfToTemp(resourceName);
                if (string.IsNullOrEmpty(tmpDxf))
                {
                    error = "gomulu kaynak yok (DLL derlemesinde " + resourceName + ")";
                    return false;
                }

                srcDb = new Database(false, true);
                string log = Path.Combine(Path.GetTempPath(), "ST4PlanIdCiz_kiris_dxf_" + Guid.NewGuid().ToString("N") + ".log");
                srcDb.DxfIn(tmpDxf, log);

                ObjectId srcBtrId;
                string sourceBlockName;
                using (var trSrc = srcDb.TransactionManager.StartTransaction())
                {
                    var srcBt = (BlockTable)trSrc.GetObject(srcDb.BlockTableId, OpenMode.ForRead);
                    srcBtrId = ResolveSourceBlockId(srcBt, trSrc, sourceNameTryOrder);
                    if (srcBtrId.IsNull)
                    {
                        error = "DXF icinde uygun blok tanimi bulunamadi";
                        trSrc.Commit();
                        return false;
                    }
                    var srcBtr = (BlockTableRecord)trSrc.GetObject(srcBtrId, OpenMode.ForRead);
                    sourceBlockName = srcBtr.Name ?? "";
                    trSrc.Commit();
                }

                var destBt = (BlockTable)trDest.GetObject(destDb.BlockTableId, OpenMode.ForWrite);
                var ids = new ObjectIdCollection { srcBtrId };
                var mapping = new IdMapping();
                destDb.WblockCloneObjects(ids, destBt.ObjectId, mapping, DuplicateRecordCloning.Ignore, false);

                ObjectId clonedBtrId = ObjectId.Null;
                foreach (IdPair pair in mapping)
                {
                    if (pair.Key == srcBtrId && pair.Value.IsValid)
                    {
                        clonedBtrId = pair.Value;
                        break;
                    }
                }

                if (clonedBtrId.IsNull)
                {
                    error = "Blok klonu IdMapping'de bulunamadi";
                    return false;
                }

                if (!string.Equals(sourceBlockName, targetBlockName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryRenameBlockTableRecord(trDest, clonedBtrId, targetBlockName))
                    {
                        error = "Blok adi '" + sourceBlockName + "' -> '" + targetBlockName + "' olarak degistirilemedi";
                        return false;
                    }
                }

                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                srcDb?.Dispose();
                if (!string.IsNullOrEmpty(tmpDxf))
                {
                    try { File.Delete(tmpDxf); } catch { }
                }
            }
        }

        private static bool TryRenameBlockTableRecord(Transaction tr, ObjectId btrId, string newName)
        {
            try
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
                btr.Name = newName;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static ObjectId ResolveSourceBlockId(BlockTable srcBt, Transaction trSrc, string[] tryNames)
        {
            foreach (var n in tryNames)
            {
                if (!string.IsNullOrEmpty(n) && srcBt.Has(n))
                    return srcBt[n];
            }
            foreach (ObjectId bid in srcBt)
            {
                var btr = (BlockTableRecord)trSrc.GetObject(bid, OpenMode.ForRead);
                if (btr == null || btr.IsLayout) continue;
                var name = btr.Name ?? "";
                if (name.StartsWith("*", StringComparison.Ordinal) || name.StartsWith("|", StringComparison.Ordinal))
                    continue;
                if (string.Equals(name, BlockTableRecord.ModelSpace, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, BlockTableRecord.PaperSpace, StringComparison.OrdinalIgnoreCase))
                    continue;
                return bid;
            }
            return ObjectId.Null;
        }

        private static string ExtractEmbeddedDxfToTemp(string logicalName)
        {
            var asm = typeof(KirisDuzeltEmbeddedBlocks).Assembly;
            using (var stream = asm.GetManifestResourceStream(logicalName))
            {
                if (stream == null)
                    return null;
                var path = Path.Combine(Path.GetTempPath(), "ST4PlanIdCiz_kiris_" + Guid.NewGuid().ToString("N") + ".dxf");
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                    stream.CopyTo(fs);
                return path;
            }
        }
    }
}
