using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using ST4AksCizCSharp;

[assembly: ExtensionApplication(typeof(ST4PlanIdCiz.PluginLifecycle))]
[assembly: CommandClass(typeof(ST4PlanIdCiz.CommandEntry))]

namespace ST4PlanIdCiz
{
    public class PluginLifecycle : IExtensionApplication
    {
        public void Initialize() { }
        public void Terminate() { }
    }

    public class CommandEntry
    {
        /// <summary>
        /// ST4 dosyasından akslar, kolonlar (poligon dahil), kirişler ve perdeleri
        /// tüm eleman ID'leriyle çizer; katlar yan yana dizilir.
        /// </summary>
        [CommandMethod("ST4PLANID")]
        public void Run()
        {
            RunCore(usePlanId: true);
        }

        /// <summary>ST4AKS: Aks/kolon/kiris/perde/doseme (ID’siz). Tek DLL yuklendiginde cift komut cikmasin diye burada kayitli.</summary>
        [CommandMethod("ST4AKS")]
        public void RunSt4Aks()
        {
            RunCore(usePlanId: false);
        }

        private static void RunCore(bool usePlanId)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;
            var db = doc.Database;

            var opts = new PromptOpenFileOptions("\nSTA4CAD ST4 Dosyasi Secin")
            {
                Filter = "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*"
            };

            var fileRes = ed.GetFileNameForOpen(opts);
            if (fileRes.Status != PromptStatus.OK) return;

            try
            {
                var parser = new St4Parser();
                var model = parser.Parse(fileRes.StringResult);
                if (usePlanId)
                {
                    var manager = new PlanIdDrawingManager(model);
                    manager.Draw(db, ed);
                }
                else
                {
                    var manager = new St4DrawingManager(model);
                    manager.Draw(db, ed);
                }
                doc.SendStringToExecute("_.ZOOM _E ", true, false, false);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n{0} hata: {1}", usePlanId ? "ST4PLANID" : "ST4AKS", ex.Message);
            }
        }
    }
}
