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
    /// Plan Ölçü: çoklu nesne seçimi ile katman ekle/kaldır.
    /// HATCH / TEXT / MTEXT seçilmez.
    /// </summary>
    internal static class PlanOlcuLayerPick
    {
        public static void RunAdd() => Run(add: true, cift: false);
        public static void RunRemove() => Run(add: false, cift: false);
        public static void RunAddCift() => Run(add: true, cift: true);
        public static void RunRemoveCift() => Run(add: false, cift: true);

        private static void Run(bool add, bool cift)
        {
            PlanOlcuSettings.EnsureLoaded();
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                PlanOlcuForm.ShowAfterPick();
                return;
            }
            Editor ed = doc.Editor;

            try
            {
                string kind = cift ? "çift ölçü" : "ölçü";
                string prompt = add
                    ? "\n" + kind + " katmanına eklenecek nesneleri seçin (tarama/yazı hariç) [Enter/sağ tık=bitir]: "
                    : "\n" + kind + " listesinden kaldırılacak katmanın nesnelerini seçin [Enter/sağ tık=bitir]: ";

                var peo = new PromptSelectionOptions
                {
                    MessageForAdding = prompt,
                    AllowDuplicates = false,
                    RejectObjectsOnLockedLayers = false
                };

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
                        ed.WriteMessage("\nPlan Ölçü: Geçerli katman bulunamadı (tarama/yazı yok sayılır).");
                    }
                    else if (add)
                    {
                        int n = 0;
                        var sb = new StringBuilder();
                        foreach (string lyr in layers)
                        {
                            bool ok = cift
                                ? PlanOlcuSettings.AddCiftLayer(lyr)
                                : PlanOlcuSettings.AddLayer(lyr);
                            if (ok)
                            {
                                n++;
                                if (sb.Length > 0) sb.Append(", ");
                                sb.Append(lyr);
                                if (cift) PlanOlcuForm.NotifyCiftLayerAdded(lyr);
                                else PlanOlcuForm.NotifyLayerAdded(lyr);
                            }
                        }
                        if (n > 0)
                            ed.WriteMessage("\nPlan Ölçü: {0} katman eklendi → {1}", n, sb);
                        else
                            ed.WriteMessage("\nPlan Ölçü: Seçilen katmanlar zaten listede.");
                    }
                    else
                    {
                        int n = 0;
                        var sb = new StringBuilder();
                        foreach (string lyr in layers)
                        {
                            bool ok = cift
                                ? PlanOlcuSettings.RemoveCiftLayer(lyr)
                                : PlanOlcuSettings.RemoveLayer(lyr);
                            if (ok)
                            {
                                n++;
                                if (sb.Length > 0) sb.Append(", ");
                                sb.Append(lyr);
                            }
                        }
                        if (cift) PlanOlcuForm.NotifyCiftLayersChanged();
                        else PlanOlcuForm.NotifyLayersChanged();
                        if (n > 0)
                            ed.WriteMessage("\nPlan Ölçü: {0} katman kaldırıldı → {1}", n, sb);
                        else
                            ed.WriteMessage("\nPlan Ölçü: Seçilen katmanlar listede yoktu.");
                    }
                }
                else if (psr.Status == PromptStatus.Cancel)
                {
                    ed.WriteMessage("\nPlan Ölçü: Seçim iptal.");
                }
            }
            finally
            {
                PlanOlcuForm.ShowAfterPick();
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
