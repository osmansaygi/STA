using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(ST4AksCizCSharp.PluginLifecycle))]
[assembly: CommandClass(typeof(ST4AksCizCSharp.CommandEntry))]

namespace ST4AksCizCSharp
{
    public class PluginLifecycle : IExtensionApplication
    {
        public void Initialize()
        {
            // Startup notification intentionally disabled.
        }

        public void Terminate()
        {
            // No cleanup required.
        }
    }

    public class CommandEntry
    {
        [CommandMethod("ST4AKS")]
        public void Run()
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
                var manager = new St4DrawingManager(model);
                manager.Draw(db, ed);
                doc.SendStringToExecute("_.ZOOM _E ", true, false, false);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nST4AKS hata: {ex.Message}");
            }
        }
    }
}
