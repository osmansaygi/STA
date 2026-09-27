using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(ST4Yardimci.PluginLifecycle))]
[assembly: CommandClass(typeof(ST4Yardimci.CommandEntry))]

namespace ST4Yardimci
{
    public class PluginLifecycle : IExtensionApplication
    {
        private static Mutex _loadMutex;

        public void Initialize()
        {
            try
            {
                var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                WarnIfDuplicateAssemblies(ed);
                try
                {
                    bool created;
                    _loadMutex = new Mutex(true, @"Local\ST4_Yardimci_ACAD", out created);
                    if (!created && ed != null)
                        ed.WriteMessage(
                            "\nBETONARME TOOL UYARI: Ayni AutoCAD oturumunda ikinci DLL (eski NETLOAD + yeni). " +
                            "Komutlar cift calisir. AutoCAD'i kapatip tek kopya yukleyin (bundle veya bir NETLOAD).");
                }
                catch (AbandonedMutexException)
                {
                    /* onceki oturum kirik kalmis olabilir */
                }
                catch { }

                if (!MachineLicense.IsAllowed())
                {
                    if (ed != null)
                        ed.WriteMessage(
                            "\nBETONARME TOOL: Bu bilgisayar için lisans yok (yetkisiz makine).");
                    return;
                }
                if (ed != null)
                    ed.WriteMessage("\nBETONARME TOOL yuklendi. BAPANEL otomatik aciliyor…  |  OTOOLCU  PLANOLCU  MERDIVEN  DOSEMEDONATI  OTODONATI  RADYEILAVE  TEMELHATILI  DONATIBOYU  DONATIMETRAJ");

                // NETLOAD sırasında palet hemen açılamayabilir; Idle'da bir kez aç.
                AcApp.Idle -= OnIdleShowBaPanel;
                AcApp.Idle += OnIdleShowBaPanel;
            }
            catch { }
        }

        private static void OnIdleShowBaPanel(object sender, EventArgs e)
        {
            AcApp.Idle -= OnIdleShowBaPanel;
            try
            {
                if (MachineLicense.IsAllowed())
                    St4LispPaletteManager.Show();
            }
            catch { }
        }

        public void Terminate()
        {
            try { AcApp.Idle -= OnIdleShowBaPanel; } catch { }
            try { OtoOlcuSettings.Save(); } catch { }
            try { PlanOlcuSettings.Save(); } catch { }
            try { OtoDonatiSettings.Save(); } catch { }
            OtoOlcuForm.Shutdown();
            PlanOlcuForm.Shutdown();
            MerdivenForm.Shutdown();
            DosemeDonatiForm.Shutdown();
            OtoDonatiForm.Shutdown();
            RadyeTemelDonatiForm.Shutdown();
            TemelHatiliForm.Shutdown();
            DonatiBoyuForm.Shutdown();
            DonatiMetrajForm.Shutdown();
            St4LispPaletteManager.Shutdown();
        }

        /// <summary>Aynı oturumda birden fazla ST4_Yardimci / ST4_Plan_ID_Ciz kopyası (eski NETLOAD kalır).</summary>
        private static void WarnIfDuplicateAssemblies(Editor ed)
        {
            if (ed == null) return;
            try
            {
                var yard = new List<string>();
                var plan = new List<string>();
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string n = a.GetName().Name ?? "";
                    string loc = "";
                    try { loc = a.IsDynamic ? "" : a.Location; } catch { }
                    if (n.StartsWith("ST4_Yardimci", StringComparison.OrdinalIgnoreCase))
                        yard.Add(string.IsNullOrEmpty(loc) ? n : loc);
                    else if (n.StartsWith("ST4_Plan_ID_Ciz", StringComparison.OrdinalIgnoreCase))
                        plan.Add(string.IsNullOrEmpty(loc) ? n : loc);
                }
                if (yard.Count > 1)
                {
                    ed.WriteMessage(
                        "\nBETONARME TOOL UYARI: {0} adet Yardimci DLL yuklu — komutlar cift/eski kod calisir. AutoCAD kapatilip tek NETLOAD yapilmali:",
                        yard.Count);
                    foreach (string p in yard)
                        ed.WriteMessage("\n  - {0}", p);
                }
                if (plan.Count > 1)
                {
                    ed.WriteMessage(
                        "\nST4PLANID UYARI: {0} adet Plan_ID DLL yuklu. AutoCAD kapatilip tek NETLOAD yapilmali:",
                        plan.Count);
                    foreach (string p in plan)
                        ed.WriteMessage("\n  - {0}", p);
                }
            }
            catch { }
        }
    }

    public class CommandEntry
    {
        /// <summary>BETONARME TOOL paletini açar (STAPANEL gibi kısayol).</summary>
        [CommandMethod("BAPANEL")]
        public void BaPanel()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            St4LispPaletteManager.Show();
        }

        [CommandMethod("BETONARMETOOL")]
        public void BetonarmeToolAlias()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            St4LispPaletteManager.Show();
        }

        [CommandMethod("ST4LISP")]
        public void St4LispPanelAlias()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            St4LispPaletteManager.Show();
        }

        [CommandMethod("OTOOLCU")]
        public void OtoOlcu()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            OtoOlcuSettings.EnsureLoaded();
            OtoOlcuDrawingBootstrap.EnsureForActiveDocument();
            St4LispPaletteManager.Show();
            OtoOlcuForm.ShowOrActivate();
        }

        /// <summary>Plan ölçü şablon formu. Ölçülendirme komutu sonra bağlanacak.</summary>
        [CommandMethod("PLANOLCU")]
        public void PlanOlcu()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            PlanOlcuSettings.EnsureLoaded();
            St4LispPaletteManager.Show();
            PlanOlcuForm.ShowOrActivate();
        }

        [CommandMethod("PLANOLCUKATMANEKLE")]
        public void PlanOlcuKatmanEkle()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            PlanOlcuLayerPick.RunAdd();
        }

        [CommandMethod("PLANOLCUKATMANKALDIR")]
        public void PlanOlcuKatmanKaldir()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            PlanOlcuLayerPick.RunRemove();
        }

        [CommandMethod("PLANOLCUCIFTKATMANEKLE")]
        public void PlanOlcuCiftKatmanEkle()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            PlanOlcuLayerPick.RunAddCift();
        }

        [CommandMethod("PLANOLCUCIFTKATMANKALDIR")]
        public void PlanOlcuCiftKatmanKaldir()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            PlanOlcuLayerPick.RunRemoveCift();
        }

        [CommandMethod("PLANOLCUCIZ")]
        public void PlanOlcuCiz()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            PlanOlcuRunner.RunInteractive();
        }

        [CommandMethod("OTOOLCUCIZ")]
        public void OtoOlcuCiz()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            OtoOlcuRunner.RunInteractive();
        }

        [CommandMethod("MERDIVEN")]
        public void Merdiven()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            St4LispPaletteManager.Show();
            MerdivenForm.ShowOrActivate();
        }

        [CommandMethod("DOSEMEDONATI")]
        public void DosemeDonati()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            St4LispPaletteManager.Show();
            DosemeDonatiForm.ShowOrActivate();
        }

        [CommandMethod("DOSEMEDONATICIZ", CommandFlags.Modal)]
        public void DosemeDonatiCiz()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            DosemeDonatiRunner.RunInteractive();
        }

        /// <summary>Radye temel / mantar döşeme ana donatı (alt+üst). İlave/pilye yok.</summary>
        [CommandMethod("OTODONATI")]
        public void OtoDonati()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            OtoDonatiSettings.EnsureLoaded();
            St4LispPaletteManager.Show();
            OtoDonatiForm.ShowOrActivate();
        }

        [CommandMethod("OTODONATICIZ", CommandFlags.Modal)]
        public void OtoDonatiCiz()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            OtoDonatiRunner.RunInteractive();
        }

        /// <summary>Radye ilave donatı formu (TEMEL ILAVE X ısı konturu → X ilave).</summary>
        [CommandMethod("RADYEILAVE")]
        [CommandMethod("RADYETEMEL")] // eski alias
        public void RadyeIlaveDonati()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            St4LispPaletteManager.Show();
            RadyeTemelDonatiForm.ShowOrActivate();
        }

        [CommandMethod("RADYEILAVECIZ", CommandFlags.Modal)]
        public void RadyeIlaveDonatiCiz()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            RadyeIlaveRunner.RunInteractive();
        }

        /// <summary>Temel üstü hatıl detayı.</summary>
        [CommandMethod("TEMELHATILI")]
        public void TemelHatili()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            St4LispPaletteManager.Show();
            TemelHatiliForm.ShowOrActivate();
        }

        [CommandMethod("TEMELHATILICIZ", CommandFlags.Modal)]
        public void TemelHatiliCiz()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            TemelHatiliRunner.RunManual();
        }

        [CommandMethod("TEMELHATILIPLAN", CommandFlags.Modal)]
        public void TemelHatiliPlan()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            TemelHatiliRunner.RunFromPlan();
        }

        [CommandMethod("DONATIBOYU")]
        public void DonatiBoyu()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            St4LispPaletteManager.Show();
            DonatiBoyuForm.ShowOrActivate();
        }

        [CommandMethod("DONATIBOYUCIZ")]
        public void DonatiBoyuCiz()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            DonatiBoyuRunner.RunInteractive();
        }

        /// <summary>Donatı yazılarından metraj tablosu. KSF METRAJ komutundan ayrıdır.</summary>
        [CommandMethod("DONATIMETRAJ")]
        public void DonatiMetraj()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            St4LispPaletteManager.Show();
            DonatiMetrajForm.ShowOrActivate();
        }

        [CommandMethod("DONATIMETRAJCIZ")]
        public void DonatiMetrajCiz()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            DonatiMetrajRunner.RunInteractive();
        }

        [CommandMethod("OTOOLCUKATMANEKLE")]
        public void OtoOlcuKatmanEkle()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            OtoOlcuLayerPick.RunAdd();
        }

        [CommandMethod("OTOOLCUKATMANKALDIR")]
        public void OtoOlcuKatmanKaldir()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            OtoOlcuLayerPick.RunRemove();
        }
    }
}
