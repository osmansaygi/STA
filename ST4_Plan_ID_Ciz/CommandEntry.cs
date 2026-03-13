using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Implementation;
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
                // non-noded intersection hatalarını önlemek için (sb_emn.st4 vb.) overlay işlemlerinde NextGen kullan
                NtsGeometryServices.Instance = new NtsGeometryServices(
                    CoordinateArraySequenceFactory.Instance,
                    new PrecisionModel(),
                    0,
                    GeometryOverlay.NG,
                    new CoordinateEqualityComparer());

                var parser = new St4Parser();
                var model = parser.Parse(fileRes.StringResult);
                var manager = new PlanIdDrawingManager(model);
                manager.Draw(db, ed);
                doc.SendStringToExecute("_.ZOOM _E ", true, false, false);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nST4PLANID hata: {0}", ex.Message);
            }
        }
    }
}
