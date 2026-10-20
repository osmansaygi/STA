using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Implementation;
using ST4AksCizCSharp;
using AcRxException = Autodesk.AutoCAD.Runtime.Exception;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ST4PlanIdCiz.SurekliTemelAcilimCommands))]

namespace ST4PlanIdCiz
{
    /// <summary>SUREKLITEMELACILIMLARI — kiriş açılımıyla aynı akış; montaj altta, düz üstte.</summary>
    public sealed class SurekliTemelAcilimCommands
    {
        [CommandMethod("SUREKLITEMELACILIMLARI")]
        public void SurekliTemelAcilimlari()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                Application.SetSystemVariable("GRIDMODE", (short)0);
                Application.SetSystemVariable("LTSCALE", 10.0);
                BeykentCizgiLayer.Ensure(doc.Database);
            }
            catch { /* sürüm / bağlam */ }

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nSUREKLITEMELACILIMLARI icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            try
            {
                NtsGeometryServices.Instance = new NtsGeometryServices(
                    CoordinateArraySequenceFactory.Instance,
                    new PrecisionModel(),
                    0,
                    GeometryOverlay.NG,
                    new CoordinateEqualityComparer());
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(st4Path, model);
                if (!SurekliTemelAcilimForm.TryShow(out var secenekler)) return;
                string prompt = secenekler.Antetli
                    ? "\nSUREKLITEMELACILIMLARI yerlesim noktasi (SheetViewOut sol-altin 50 cm solu): "
                    : "\nSUREKLITEMELACILIMLARI yerlestirme noktasi (ilk temel alt-sol): ";
                var insRes = ed.GetPoint(new PromptPointOptions(prompt) { AllowNone = false });
                if (insRes.Status != PromptStatus.OK) return;
                var manager = new PlanIdDrawingManager(model);
                if (manager.DrawSurekliTemelAcilimlariSecenekli(db, ed, insRes.Value, st4Path, secenekler))
                {
                    try { manager.KirisAcilimGorunumeAl(ed); }
                    catch { /* görünüm başarısız olsa da çizim kalsın */ }
                }
            }
            catch (System.Exception ex)
            {
                if (ex is AcRxException aex)
                    ed.WriteMessage("\nSUREKLITEMELACILIMLARI hata: {0} ({1})", aex.Message, aex.ErrorStatus);
                else
                    ed.WriteMessage("\nSUREKLITEMELACILIMLARI hata: {0}", ex.Message);
            }
        }
    }
}
