using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(ST4Yardimci.PluginLifecycle))]
[assembly: CommandClass(typeof(ST4Yardimci.CommandEntry))]

namespace ST4Yardimci
{
    public class PluginLifecycle : IExtensionApplication
    {
        public void Initialize()
        {
            // Açılışta paleti otomatik açma — Idle/Show titreşim yapıyordu.
            // Kullanıcı BAPANEL ile açar. STA paneli ayrı DLL'de.
            try
            {
                Application.DocumentManager.MdiActiveDocument?.Editor
                    .WriteMessage("\nBETONARME TOOL yuklendi. Acmak icin: BAPANEL  |  OTOOLCU  MERDIVEN  DOSEMEDONATI  RADYETEMEL  DONATIBOYU  DONATIMETRAJ");
            }
            catch { }
        }

        public void Terminate()
        {
            try { OtoOlcuSettings.Save(); } catch { }
            OtoOlcuForm.Shutdown();
            MerdivenForm.Shutdown();
            DosemeDonatiForm.Shutdown();
            RadyeTemelDonatiForm.Shutdown();
            DonatiBoyuForm.Shutdown();
            DonatiMetrajForm.Shutdown();
            St4LispPaletteManager.Shutdown();
        }
    }

    public class CommandEntry
    {
        /// <summary>BETONARME TOOL paletini açar (STAPANEL gibi kısayol).</summary>
        [CommandMethod("BAPANEL")]
        public void BaPanel()
        {
            St4LispPaletteManager.Show();
        }

        [CommandMethod("BETONARMETOOL")]
        public void BetonarmeToolAlias()
        {
            St4LispPaletteManager.Show();
        }

        [CommandMethod("ST4LISP")]
        public void St4LispPanelAlias()
        {
            St4LispPaletteManager.Show();
        }

        [CommandMethod("OTOOLCU")]
        public void OtoOlcu()
        {
            OtoOlcuSettings.EnsureLoaded();
            OtoOlcuDrawingBootstrap.EnsureForActiveDocument();
            St4LispPaletteManager.Show();
            OtoOlcuForm.ShowOrActivate();
        }

        [CommandMethod("OTOOLCUCIZ")]
        public void OtoOlcuCiz()
        {
            OtoOlcuRunner.RunInteractive();
        }

        [CommandMethod("MERDIVEN")]
        public void Merdiven()
        {
            St4LispPaletteManager.Show();
            MerdivenForm.ShowOrActivate();
        }

        [CommandMethod("DOSEMEDONATI")]
        public void DosemeDonati()
        {
            St4LispPaletteManager.Show();
            DosemeDonatiForm.ShowOrActivate();
        }

        [CommandMethod("RADYETEMEL")]
        public void RadyeTemelDonati()
        {
            St4LispPaletteManager.Show();
            RadyeTemelDonatiForm.ShowOrActivate();
        }

        [CommandMethod("DONATIBOYU")]
        public void DonatiBoyu()
        {
            St4LispPaletteManager.Show();
            DonatiBoyuForm.ShowOrActivate();
        }

        [CommandMethod("DONATIBOYUCIZ")]
        public void DonatiBoyuCiz()
        {
            DonatiBoyuRunner.RunInteractive();
        }

        /// <summary>Donatı yazılarından metraj tablosu. KSF METRAJ komutundan ayrıdır.</summary>
        [CommandMethod("DONATIMETRAJ")]
        public void DonatiMetraj()
        {
            St4LispPaletteManager.Show();
            DonatiMetrajForm.ShowOrActivate();
        }

        [CommandMethod("DONATIMETRAJCIZ")]
        public void DonatiMetrajCiz()
        {
            DonatiMetrajRunner.RunInteractive();
        }

        [CommandMethod("DOSEMEKATMANEKLE")]
        public void DosemeKatmanEkle()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            var opt = new PromptEntityOptions("\nKatmanı eklenecek nesneyi seçin: ");
            opt.SetRejectMessage("\nNesne seçin.");
            opt.AllowNone = false;
            PromptEntityResult res = ed.GetEntity(opt);
            if (res.Status != PromptStatus.OK) return;

            string layerName = null;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ent = tr.GetObject(res.ObjectId, OpenMode.ForRead) as Entity;
                if (ent != null)
                    layerName = ent.Layer;
                tr.Commit();
            }
            if (string.IsNullOrWhiteSpace(layerName)) return;
            if (DosemeDonatiSettings.AddLayer(layerName))
            {
                DosemeDonatiForm.NotifyLayerAdded(layerName);
                ed.WriteMessage("\nDöşeme Donatı: katman eklendi → {0}", layerName);
            }
            else
                ed.WriteMessage("\nDöşeme Donatı: '{0}' zaten listede.", layerName);
        }

        [CommandMethod("OTOOLCUKATMANEKLE")]
        public void OtoOlcuKatmanEkle()
        {
            OtoOlcuLayerPick.RunAdd();
        }

        [CommandMethod("OTOOLCUKATMANKALDIR")]
        public void OtoOlcuKatmanKaldir()
        {
            OtoOlcuLayerPick.RunRemove();
        }
    }
}
