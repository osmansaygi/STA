using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Oto Ölçü: çoklu nesne seçimi ile katman ekle/kaldır.
    /// HATCH / TEXT / MTEXT seçilmez.
    /// </summary>
    internal static class OtoOlcuLayerPick
    {
        public static void RunAdd() => Run(add: true);
        public static void RunRemove() => Run(add: false);

        private static void Run(bool add)
        {
            OtoOlcuSettings.EnsureLoaded();
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                OtoOlcuForm.ShowAfterPick();
                return;
            }
            Editor ed = doc.Editor;
            OtoOlcuLayerList target = OtoOlcuSettings.LayerPickTarget;

            try
            {
                string which = target == OtoOlcuLayerList.Ikinci ? "2. ölçü" : "1. ölçü";
                string prompt = add
                    ? string.Format("\n{0} katmanı eklenecek nesneleri seçin (tarama/yazı hariç) [Enter/sağ tık=bitir]: ", which)
                    : string.Format("\n{0} katmanı kaldırılacak nesneleri seçin (tarama/yazı hariç) [Enter/sağ tık=bitir]: ", which);

                var peo = new PromptSelectionOptions
                {
                    MessageForAdding = prompt,
                    AllowDuplicates = false,
                    RejectObjectsOnLockedLayers = false
                };

                // HATCH + TEXT + MTEXT seçilemesin
                var filter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Operator, "<AND"),
                    new TypedValue((int)DxfCode.Operator, "<NOT"),
                    new TypedValue((int)DxfCode.Start, "HATCH"),
                    new TypedValue((int)DxfCode.Operator, "NOT>"),
                    new TypedValue((int)DxfCode.Operator, "<NOT"),
                    new TypedValue((int)DxfCode.Start, "TEXT"),
                    new TypedValue((int)DxfCode.Operator, "NOT>"),
                    new TypedValue((int)DxfCode.Operator, "<NOT"),
                    new TypedValue((int)DxfCode.Start, "MTEXT"),
                    new TypedValue((int)DxfCode.Operator, "NOT>"),
                    new TypedValue((int)DxfCode.Operator, "AND>")
                });

                PromptSelectionResult psr = ed.GetSelection(peo, filter);

                if (psr.Status == PromptStatus.OK && psr.Value != null && psr.Value.Count > 0)
                {
                    var layers = CollectLayers(doc, psr.Value);
                    if (layers.Count == 0)
                    {
                        ed.WriteMessage("\nOto Ölçü: Geçerli katman bulunamadı (tarama/yazı yok sayılır).");
                    }
                    else if (add)
                    {
                        int n = 0;
                        var sb = new StringBuilder();
                        foreach (string lyr in layers)
                        {
                            if (OtoOlcuSettings.AddLayer(target, lyr))
                            {
                                n++;
                                if (sb.Length > 0) sb.Append(", ");
                                sb.Append(lyr);
                                OtoOlcuForm.NotifyLayerAdded(lyr);
                            }
                        }
                        if (n > 0)
                            ed.WriteMessage("\nOto Ölçü ({0}): {1} katman eklendi → {2}", which, n, sb);
                        else
                            ed.WriteMessage("\nOto Ölçü: Seçilen katmanlar zaten listede.");
                    }
                    else
                    {
                        int n = 0;
                        var sb = new StringBuilder();
                        foreach (string lyr in layers)
                        {
                            if (OtoOlcuSettings.RemoveLayer(target, lyr))
                            {
                                n++;
                                if (sb.Length > 0) sb.Append(", ");
                                sb.Append(lyr);
                            }
                        }
                        OtoOlcuForm.NotifyLayersChanged();
                        if (n > 0)
                            ed.WriteMessage("\nOto Ölçü ({0}): {1} katman kaldırıldı → {2}", which, n, sb);
                        else
                            ed.WriteMessage("\nOto Ölçü: Seçilen katmanlar listede yoktu.");
                    }
                }
                else if (psr.Status == PromptStatus.Cancel)
                {
                    ed.WriteMessage("\nOto Ölçü: Seçim iptal.");
                }
            }
            finally
            {
                OtoOlcuForm.ShowAfterPick();
            }
        }

        private static List<string> CollectLayers(Document doc, SelectionSet ss)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Database db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in ss)
                {
                    if (so == null) continue;
                    var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null || ent.IsErased) continue;
                    if (ent is Hatch || ent is DBText || ent is MText || ent is Dimension)
                        continue;
                    string lyr = ent.Layer;
                    if (string.IsNullOrWhiteSpace(lyr)) continue;
                    if (seen.Add(lyr))
                        result.Add(lyr);
                }
                tr.Commit();
            }
            return result;
        }
    }
}
