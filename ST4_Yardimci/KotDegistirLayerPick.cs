using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>Kot değiştir: çoklu nesne seçimi ile katman ekle/kaldır.</summary>
    internal static class KotDegistirLayerPick
    {
        public static void RunAdd() => Run(add: true);
        public static void RunRemove() => Run(add: false);

        private static void Run(bool add)
        {
            KotDegistirSettings.EnsureLoaded();
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                KotDegistirForm.ShowAfterPick();
                return;
            }
            Editor ed = doc.Editor;

            try
            {
                string prompt = add
                    ? "\nKot yazısı katmanı eklenecek yazıları seçin [Enter/sağ tık=bitir]: "
                    : "\nKot yazısı katmanı kaldırılacak yazıları seçin [Enter/sağ tık=bitir]: ";

                var peo = new PromptSelectionOptions
                {
                    MessageForAdding = prompt,
                    AllowDuplicates = false,
                    RejectObjectsOnLockedLayers = false
                };
                var filter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Operator, "<OR"),
                    new TypedValue((int)DxfCode.Start, "TEXT"),
                    new TypedValue((int)DxfCode.Start, "MTEXT"),
                    new TypedValue((int)DxfCode.Operator, "OR>")
                });

                PromptSelectionResult psr = ed.GetSelection(peo, filter);
                if (psr.Status == PromptStatus.OK && psr.Value != null && psr.Value.Count > 0)
                {
                    var layers = CollectLayers(doc, psr.Value);
                    if (layers.Count == 0)
                    {
                        ed.WriteMessage("\nKOTDEGISTIR: Geçerli katman bulunamadı.");
                    }
                    else if (add)
                    {
                        int n = 0;
                        var sb = new StringBuilder();
                        foreach (string lyr in layers)
                        {
                            if (KotDegistirSettings.AddKatman(lyr))
                            {
                                n++;
                                if (sb.Length > 0) sb.Append(", ");
                                sb.Append(lyr);
                                KotDegistirForm.NotifyLayerAdded(lyr);
                            }
                        }
                        if (n > 0)
                            ed.WriteMessage("\nKOTDEGISTIR: {0} katman eklendi → {1}", n, sb);
                        else
                            ed.WriteMessage("\nKOTDEGISTIR: Seçilen katmanlar zaten listede.");
                    }
                    else
                    {
                        int n = 0;
                        var sb = new StringBuilder();
                        foreach (string lyr in layers)
                        {
                            if (KotDegistirSettings.RemoveKatman(lyr))
                            {
                                n++;
                                if (sb.Length > 0) sb.Append(", ");
                                sb.Append(lyr);
                            }
                        }
                        KotDegistirForm.NotifyLayersChanged();
                        if (n > 0)
                            ed.WriteMessage("\nKOTDEGISTIR: {0} katman kaldırıldı → {1}", n, sb);
                        else
                            ed.WriteMessage("\nKOTDEGISTIR: Seçilen katmanlar listede yoktu.");
                    }
                }
                else if (psr.Status == PromptStatus.Cancel)
                {
                    ed.WriteMessage("\nKOTDEGISTIR: Seçim iptal.");
                }
            }
            finally
            {
                KotDegistirForm.ShowAfterPick();
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
                    if (!(ent is DBText) && !(ent is MText)) continue;
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
