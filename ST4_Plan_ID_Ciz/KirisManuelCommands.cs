using System;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Implementation;
using AcRxException = Autodesk.AutoCAD.Runtime.Exception;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ST4PlanIdCiz.KirisManuelCommands))]

namespace ST4PlanIdCiz
{
    /// <summary>
    /// KIRISCIZ — ST4'süz manuel kiriş: formda mesnet/açıklık/donatı, KIRISACILIMLARI motoruyla 1/25 açılım.
    /// -KIRISCIZ — formsuz: .kiris dosyası + yerleşim noktası (betik / Core Console).
    /// </summary>
    public sealed class KirisManuelCommands
    {
        [CommandMethod("KIRISCIZ")]
        public void KirisCiz()
        {
            if (!PlanLisans.EnsureOrWarn()) return;
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            if (!KirisManuelForm.TryShow(out KirisManuelVeri veri, KirisManuelPlanSecim.Sec)) return;
            Ciz(doc, veri, KirisManuelForm.Pafta100);
        }

        [CommandMethod("-KIRISCIZ")]
        public void KirisCizBetik()
        {
            if (!PlanLisans.EnsureOrWarn()) return;
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var pr = ed.GetString(new PromptStringOptions("\nKIRISCIZ veri dosyasi (.kiris): ") { AllowSpaces = true });
            if (pr.Status != PromptStatus.OK) return;
            string yol = pr.StringResult.Trim().Trim('"');
            KirisManuelVeri veri = null;
            try { if (File.Exists(yol)) veri = KirisManuelVeri.Oku(File.ReadAllText(yol, Encoding.UTF8)); }
            catch (System.Exception ex) { ed.WriteMessage("\nKIRISCIZ: dosya okunamadi: {0}", ex.Message); return; }
            if (veri == null) { ed.WriteMessage("\nKIRISCIZ: gecerli veri yok: {0}", yol); return; }
            if (!veri.Dogrula(out string hata)) { ed.WriteMessage("\nKIRISCIZ: {0}", hata); return; }
            Ciz(doc, veri, false);
        }

        private static void Ciz(Document doc, KirisManuelVeri veri, bool pafta100)
        {
            var ed = doc.Editor;
            var db = doc.Database;
            try
            {
                Application.SetSystemVariable("GRIDMODE", (short)0);
                Application.SetSystemVariable("LTSCALE", 10.0);
                BeykentCizgiLayer.Ensure(db);
            }
            catch { /* sürüm / bağlam */ }

            var insRes = ed.GetPoint(new PromptPointOptions("\nKIRISCIZ yerlestirme noktasi (kiris alt-sol): ") { AllowNone = false });
            if (insRes.Status != PromptStatus.OK) return;
            try
            {
                NtsGeometryServices.Instance = new NtsGeometryServices(
                    CoordinateArraySequenceFactory.Instance,
                    new PrecisionModel(),
                    0,
                    GeometryOverlay.NG,
                    new CoordinateEqualityComparer());
                var model = PlanIdDrawingManager.KirisManuelModelKur(veri);
                var manager = new PlanIdDrawingManager(model);
                long ilkHandle = db.Handseed.Value;
                if (manager.DrawKirisManuel(db, ed, insRes.Value, veri))
                {
                    // 1/100 pafta: açılım (yazı/ölçü) ×2; görünüm büyütülmüş çizime alınır.
                    if (pafta100)
                        BeykentOrtak.PaftaOlcek.BuyutYeni(db, ilkHandle, insRes.Value, ed: ed);
                    else
                    {
                        try { manager.KirisAcilimGorunumeAl(ed); }
                        catch { /* görünüm başarısız olsa da çizim kalsın */ }
                    }
                }
            }
            catch (System.Exception ex)
            {
                if (ex is AcRxException aex)
                    ed.WriteMessage("\nKIRISCIZ hata: {0} ({1})", aex.Message, aex.ErrorStatus);
                else
                    ed.WriteMessage("\nKIRISCIZ hata: {0}", ex.Message);
            }
        }
    }
}
