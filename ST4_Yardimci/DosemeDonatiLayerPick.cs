using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>Döşeme donatı: çizgi seçerek sınır / mesnet katmanı ekle veya kaldır.</summary>
    internal static class DosemeDonatiLayerPick
    {
        public static void RunAdd() => Run(add: true);
        public static void RunRemove() => Run(add: false);

        private static void Run(bool add)
        {
            DosemeDonatiSettings.EnsureLoaded();
            DosemeKatmanListe which = DosemeDonatiSettings.PickList;
            string liste = which == DosemeKatmanListe.Mesnet ? "mesnet"
                : which == DosemeKatmanListe.Bosluk ? "boşluk"
                : which == DosemeKatmanListe.KatSinir ? "kat sınırı"
                : "konsol sınır";

            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                DosemeDonatiForm.ShowAfterPick();
                return;
            }
            Editor ed = doc.Editor;

            try
            {
                string prompt = add
                    ? "\n" + liste + " katmanı eklenecek çizgileri seçin [Enter/sağ tık=bitir]: "
                    : "\n" + liste + " katmanı kaldırılacak çizgileri seçin [Enter/sağ tık=bitir]: ";

                var peo = new PromptSelectionOptions
                {
                    MessageForAdding = prompt,
                    AllowDuplicates = false,
                    RejectObjectsOnLockedLayers = false
                };

                PromptSelectionResult psr = ed.GetSelection(peo);
                if (psr.Status == PromptStatus.OK && psr.Value != null && psr.Value.Count > 0)
                {
                    var layers = CollectLayers(doc, psr.Value);
                    if (layers.Count == 0)
                    {
                        ed.WriteMessage("\nDOSEMEDONATI: Geçerli katman bulunamadı.");
                    }
                    else if (add)
                    {
                        int n = 0;
                        var sb = new StringBuilder();
                        foreach (string lyr in layers)
                        {
                            if (DosemeDonatiSettings.AddKatman(which, lyr))
                            {
                                n++;
                                if (sb.Length > 0) sb.Append(", ");
                                sb.Append(lyr);
                            }
                        }
                        if (n > 0)
                            ed.WriteMessage("\nDOSEMEDONATI: {0} {1} katmanı eklendi → {2}", n, liste, sb);
                        else
                            ed.WriteMessage("\nDOSEMEDONATI: Seçilen katmanlar zaten {0} listesinde.", liste);
                    }
                    else
                    {
                        int n = 0;
                        var sb = new StringBuilder();
                        foreach (string lyr in layers)
                        {
                            if (DosemeDonatiSettings.RemoveKatman(which, lyr))
                            {
                                n++;
                                if (sb.Length > 0) sb.Append(", ");
                                sb.Append(lyr);
                            }
                        }
                        if (n > 0)
                            ed.WriteMessage("\nDOSEMEDONATI: {0} {1} katmanı kaldırıldı → {2}", n, liste, sb);
                        else
                            ed.WriteMessage("\nDOSEMEDONATI: Seçilen katmanlar {0} listesinde yoktu.", liste);
                    }
                }
                else if (psr.Status == PromptStatus.Cancel)
                {
                    ed.WriteMessage("\nDOSEMEDONATI: Seçim iptal.");
                }
            }
            finally
            {
                DosemeDonatiForm.ShowAfterPick();
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
                    if (ent is Hatch || ent is Dimension || ent is DBText || ent is MText)
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
