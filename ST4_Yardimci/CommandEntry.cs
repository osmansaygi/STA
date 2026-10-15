using System;
using System.Collections.Generic;
using System.IO;
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
                AppDomain.CurrentDomain.AssemblyResolve -= ResolvePluginDependency;
                AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginDependency;
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
                    ed.WriteMessage("\nBETONARME TOOL yuklendi. BAPANEL otomatik aciliyor…  |  OTOOLCU  PLANOLCU  MERDIVEN  DOSEMEDONATI  OTODONATI  SEHPADONATI  HASIRDONATI  RADYEILAVE  TEMELHATILI  TEMELCEKURU  DONATIBOYU  DONATIMETRAJ  DONATI ADET  DONATI OKU  KOTDEGISTIR  KATMANYUKLE");

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
                if (!MachineLicense.IsAllowed()) return;
                // Workspace oturmadan PaletteSet bazı sürümlerde göçürür.
                if (_idleSkip < 2)
                {
                    _idleSkip++;
                    AcApp.Idle += OnIdleShowBaPanel;
                    return;
                }
                // Plan_ID Idle'da STA paneli açıyorsa aynı anda 3 PaletteSet yaratmayı engelle.
                bool gotGate = false;
                try
                {
                    bool created;
                    _paletteGate = new Mutex(false, @"Local\ST4_AcadPaletteInit", out created);
                    gotGate = _paletteGate.WaitOne(1200);
                }
                catch { }
                try
                {
                    // Belge yokken PaletteSet bazı sürümlerde düşer — bir Idle daha bekle.
                    if (AcApp.DocumentManager.MdiActiveDocument == null)
                    {
                        AcApp.Idle += OnIdleShowBaPanel;
                        return;
                    }
                    St4LispPaletteManager.Show();
                    // BAUST paleti / COM toolbar silme ofiste süreç düşürür — açılışta yok.
                }
                finally
                {
                    if (gotGate)
                    {
                        try { _paletteGate?.ReleaseMutex(); } catch { }
                    }
                    try { _paletteGate?.Dispose(); } catch { }
                    _paletteGate = null;
                }
            }
            catch { }
        }

        private static Mutex _paletteGate;
        private static int _idleSkip;

        /// <summary>Bundle klasöründeki NTS / System.* — çalışma dizini AutoCAD olunca FileNotFound süreç düşürebilir.</summary>
        private static Assembly ResolvePluginDependency(object sender, ResolveEventArgs args)
        {
            try
            {
                if (args == null || string.IsNullOrEmpty(args.Name)) return null;
                string simple = new AssemblyName(args.Name).Name;
                if (string.IsNullOrEmpty(simple) || simple.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                    return null;
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(dir)) return null;
                string path = Path.Combine(dir, simple + ".dll");
                if (File.Exists(path))
                    return Assembly.LoadFrom(path);
            }
            catch { }
            return null;
        }

        public void Terminate()
        {
            try { AcApp.Idle -= OnIdleShowBaPanel; } catch { }
            try { AppDomain.CurrentDomain.AssemblyResolve -= ResolvePluginDependency; } catch { }
            try { OtoOlcuSettings.Save(); } catch { }
            try { PlanOlcuSettings.Save(); } catch { }
            try { OtoDonatiSettings.Save(); } catch { }
            try { SehpaDonatiSettings.Save(); } catch { }
            try { HasirDonatiSettings.Save(); } catch { }
            try { OtoOlcuForm.Shutdown(); } catch { }
            try { PlanOlcuForm.Shutdown(); } catch { }
            try { MerdivenForm.Shutdown(); } catch { }
            try { DosemeDonatiForm.Shutdown(); } catch { }
            try { OtoDonatiForm.Shutdown(); } catch { }
            try { SehpaDonatiForm.Shutdown(); } catch { }
            try { HasirDonatiForm.Shutdown(); } catch { }
            try { RadyeTemelDonatiForm.Shutdown(); } catch { }
            try { TemelHatiliForm.Shutdown(); } catch { }
            try { KuranglezForm.Shutdown(); } catch { }
            try { DonatiBoyuForm.Shutdown(); } catch { }
            try { DonatiMetrajForm.Shutdown(); } catch { }
            try { KotDegistirForm.Shutdown(); } catch { }
            try { BaFormSession.Clear(); } catch { }
            try { St4LispPaletteManager.Shutdown(); } catch { }
            try { BaTopToolbar.Shutdown(); } catch { }
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
        private static bool Gate(string family)
        {
            if (!MachineLicense.EnsureOrWarn()) return false;
            return BaFormSession.EnsureCanEnter(family);
        }

        private static bool GateIdle()
        {
            if (!MachineLicense.EnsureOrWarn()) return false;
            return BaFormSession.EnsureCanEnter(null);
        }

        /// <summary>BETONARME TOOL paletini açar (STAPANEL gibi kısayol).</summary>
        [CommandMethod("BAPANEL")]
        public void BaPanel()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            St4LispPaletteManager.Show();
        }

        /// <summary>Eski yazısız simge şeridini kapatır (artık kullanılmaz).</summary>
        [CommandMethod("BAUST")]
        public void BaUst()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            BaTopToolbar.Hide();
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
            if (!Gate("OTOOLCU")) return;
            OtoOlcuSettings.EnsureLoaded();
            OtoOlcuDrawingBootstrap.EnsureForActiveDocument();
            OtoOlcuForm.ShowOrActivate();
        }

        /// <summary>Plan ölçü şablon formu. Ölçülendirme komutu sonra bağlanacak.</summary>
        [CommandMethod("PLANOLCU")]
        public void PlanOlcu()
        {
            if (!Gate("PLANOLCU")) return;
            PlanOlcuSettings.EnsureLoaded();
            PlanOlcuForm.ShowOrActivate();
        }

        [CommandMethod("PLANOLCUKATMANEKLE")]
        public void PlanOlcuKatmanEkle()
        {
            if (!Gate("PLANOLCU")) return;
            PlanOlcuLayerPick.RunAdd();
        }

        [CommandMethod("PLANOLCUKATMANKALDIR")]
        public void PlanOlcuKatmanKaldir()
        {
            if (!Gate("PLANOLCU")) return;
            PlanOlcuLayerPick.RunRemove();
        }

        [CommandMethod("PLANOLCUCIFTKATMANEKLE")]
        public void PlanOlcuCiftKatmanEkle()
        {
            if (!Gate("PLANOLCU")) return;
            PlanOlcuLayerPick.RunAddCift();
        }

        [CommandMethod("PLANOLCUCIFTKATMANKALDIR")]
        public void PlanOlcuCiftKatmanKaldir()
        {
            if (!Gate("PLANOLCU")) return;
            PlanOlcuLayerPick.RunRemoveCift();
        }

        [CommandMethod("PLANOLCUCIZ")]
        public void PlanOlcuCiz()
        {
            if (!Gate("PLANOLCU")) return;
            PlanOlcuRunner.RunInteractive();
        }

        [CommandMethod("OTOOLCUCIZ")]
        public void OtoOlcuCiz()
        {
            if (!Gate("OTOOLCU")) return;
            OtoOlcuRunner.RunInteractive();
        }

        [CommandMethod("MERDIVEN", CommandFlags.Modal)]
        public void Merdiven()
        {
            if (!Gate("MERDIVEN")) return;
            if (!MerdivenForm.ShowModalAndAcceptDraw()) return;
            MerdivenRunner.RunInteractive();
        }

        /// <summary>Doğrudan plandan çiz (form yok). Enter ile tekrarlanmaz — MERDIVEN kullanın.</summary>
        [CommandMethod("MERDIVENCIZ", CommandFlags.Modal)]
        public void MerdivenCiz()
        {
            if (!Gate("MERDIVEN")) return;
            MerdivenRunner.RunInteractive();
        }

        [CommandMethod("DOSEMEDONATI")]
        public void DosemeDonati()
        {
            if (!Gate("DOSEMEDONATI")) return;
            DosemeDonatiForm.ShowOrActivate();
        }

        [CommandMethod("DOSEMEDONATICIZ", CommandFlags.Modal)]
        public void DosemeDonatiCiz()
        {
            if (!Gate("DOSEMEDONATI")) return;
            DosemeDonatiRunner.RunInteractive();
        }

        /// <summary>Çizgisel donatı: serbest yön; sınır/mesnet katman listeleri.</summary>
        [CommandMethod("DOSEMEDONATICIZGISEL", CommandFlags.Modal)]
        public void DosemeDonatiCizgisel()
        {
            if (!Gate("DOSEMEDONATI")) return;
            DosemeDonatiRunner.RunCizgisel();
        }

        [CommandMethod("DOSEMEDONATIKATMANEKLE", CommandFlags.Modal)]
        public void DosemeDonatiKatmanEkle()
        {
            if (!Gate("DOSEMEDONATI")) return;
            DosemeDonatiLayerPick.RunAdd();
        }

        [CommandMethod("DOSEMEDONATIKATMANKALDIR", CommandFlags.Modal)]
        public void DosemeDonatiKatmanKaldir()
        {
            if (!Gate("DOSEMEDONATI")) return;
            DosemeDonatiLayerPick.RunRemove();
        }

        /// <summary>Radye temel / mantar döşeme ana donatı (alt+üst). İlave/pilye yok.</summary>
        [CommandMethod("OTODONATI")]
        public void OtoDonati()
        {
            if (!Gate("OTODONATI")) return;
            OtoDonatiSettings.EnsureLoaded();
            OtoDonatiForm.ShowOrActivate();
        }

        [CommandMethod("OTODONATICIZ", CommandFlags.Modal)]
        public void OtoDonatiCiz()
        {
            if (!Gate("OTODONATI")) return;
            OtoDonatiRunner.RunInteractive();
        }

        [CommandMethod("OTODONATIMANUEL", CommandFlags.Modal)]
        public void OtoDonatiManuel()
        {
            if (!Gate("OTODONATI")) return;
            OtoDonatiRunner.RunManual();
        }

        /// <summary>Radye temel / mantar döşeme sehpa donatısı kesiti.</summary>
        [CommandMethod("SEHPADONATI")]
        [CommandMethod("SEHPADONATISI")]
        public void SehpaDonati()
        {
            if (!Gate("SEHPADONATI")) return;
            SehpaDonatiSettings.EnsureLoaded();
            SehpaDonatiForm.ShowOrActivate();
        }

        [CommandMethod("SEHPADONATICIZ", CommandFlags.Modal)]
        public void SehpaDonatiCiz()
        {
            if (!Gate("SEHPADONATI")) return;
            SehpaDonatiRunner.RunInteractive();
        }

        /// <summary>Hasır donatı: tip/ebat/bindirme/paspayı formu → alana dizilim.</summary>
        [CommandMethod("HASIRDONATI")]
        [CommandMethod("HASIR DONATI")]
        public void HasirDonati()
        {
            if (!Gate("HASIRDONATI")) return;
            HasirDonatiSettings.EnsureLoaded();
            HasirDonatiForm.ShowOrActivate();
        }

        [CommandMethod("HASIRDONATICIZ", CommandFlags.Modal)]
        public void HasirDonatiCiz()
        {
            if (!Gate("HASIRDONATI")) return;
            HasirDonatiRunner.RunInteractive();
        }

        /// <summary>Radye ilave donatı formu (TEMEL ILAVE X ısı konturu → X ilave).</summary>
        [CommandMethod("RADYEILAVE")]
        [CommandMethod("RADYETEMEL")] // eski alias
        public void RadyeIlaveDonati()
        {
            if (!Gate("RADYEILAVE")) return;
            RadyeTemelDonatiForm.ShowOrActivate();
        }

        [CommandMethod("RADYEILAVECIZ", CommandFlags.Modal)]
        public void RadyeIlaveDonatiCiz()
        {
            if (!Gate("RADYEILAVE")) return;
            RadyeIlaveRunner.RunInteractive();
        }

        /// <summary>Temel üstü hatıl detayı.</summary>
        [CommandMethod("TEMELHATILI")]
        public void TemelHatili()
        {
            if (!Gate("TEMELHATILI")) return;
            TemelHatiliForm.ShowOrActivate();
        }

        [CommandMethod("TEMELHATILICIZ", CommandFlags.Modal)]
        public void TemelHatiliCiz()
        {
            if (!Gate("TEMELHATILI")) return;
            TemelHatiliRunner.RunManual();
        }

        [CommandMethod("TEMELHATILIPLAN", CommandFlags.Modal)]
        public void TemelHatiliPlan()
        {
            if (!Gate("TEMELHATILI")) return;
            TemelHatiliRunner.RunFromPlan();
        }

        /// <summary>Temel çukuru kesiti — gömülü şablon, CIZGI katmanı form değişkeni.</summary>
        [CommandMethod("TEMELCEKURU")]
        [CommandMethod("TEMEL CEKURU")]
        public void TemelCekuru()
        {
            if (!Gate("TEMELCEKURU")) return;
            TemelCekuruForm.ShowOrActivate();
        }

        [CommandMethod("TEMELCEKURUCIZ", CommandFlags.Modal)]
        public void TemelCekuruCiz()
        {
            if (!Gate("TEMELCEKURU")) return;
            TemelCekuruRunner.RunInteractive();
        }

        [CommandMethod("TEMELCEKURUPLAN", CommandFlags.Modal)]
        public void TemelCekuruPlan()
        {
            if (!Gate("TEMELCEKURU")) return;
            TemelCekuruRunner.RunFromPlan();
        }

        /// <summary>Kuranglez donatı detayı — plan + A-A kesit, ölçek 1/25.</summary>
        [CommandMethod("KURANGLEZ")]
        public void Kuranglez()
        {
            if (!Gate("KURANGLEZ")) return;
            KuranglezForm.ShowOrActivate();
        }

        [CommandMethod("KURANGLEZCIZ", CommandFlags.Modal)]
        public void KuranglezCiz()
        {
            if (!Gate("KURANGLEZ")) return;
            KuranglezRunner.RunInteractive();
        }

        [CommandMethod("DONATIBOYU")]
        public void DonatiBoyu()
        {
            if (!Gate("DONATIBOYU")) return;
            DonatiBoyuForm.ShowOrActivate();
        }

        [CommandMethod("DONATIBOYUCIZ")]
        public void DonatiBoyuCiz()
        {
            if (!Gate("DONATIBOYU")) return;
            DonatiBoyuRunner.RunInteractive();
        }

        [CommandMethod("DONATIBOYUKATMANEKLE")]
        public void DonatiBoyuKatmanEkle()
        {
            if (!Gate("DONATIBOYU")) return;
            DonatiBoyuLayerPick.RunAdd();
        }

        [CommandMethod("DONATIBOYUKATMANKALDIR")]
        public void DonatiBoyuKatmanKaldir()
        {
            if (!Gate("DONATIBOYU")) return;
            DonatiBoyuLayerPick.RunRemove();
        }

        /// <summary>Mesafe / aralık → üst tamsayı adet; etiket güncellenir. Form yok.</summary>
        [CommandMethod("DONATI ADET", CommandFlags.Modal)]
        [CommandMethod("DONATIADET", CommandFlags.Modal)]
        public void DonatiAdet()
        {
            if (!GateIdle()) return;
            DonatiAdetRunner.RunInteractive();
        }

        /// <summary>Mesafe + donatı oku ölçüsü + halka, sonra aynı adet güncellemesi.</summary>
        [CommandMethod("DONATI OKU", CommandFlags.Modal)]
        [CommandMethod("DONATIOKU", CommandFlags.Modal)]
        public void DonatiOku()
        {
            if (!GateIdle()) return;
            DonatiOkuRunner.RunInteractive();
        }

        [CommandMethod("DONATIMETRAJ")]
        public void DonatiMetraj()
        {
            if (!Gate("DONATIMETRAJ")) return;
            DonatiMetrajForm.ShowOrActivate();
        }

        [CommandMethod("DONATIMETRAJCIZ")]
        public void DonatiMetrajCiz()
        {
            if (!Gate("DONATIMETRAJ")) return;
            DonatiMetrajRunner.RunInteractive();
        }

        [CommandMethod("DONATIMETRAJANTET")]
        public void DonatiMetrajAntet()
        {
            if (!Gate("DONATIMETRAJ")) return;
            DonatiMetrajRunner.RunByAntet();
        }

        [CommandMethod("DONATIMETRAJTOPLAM")]
        public void DonatiMetrajToplam()
        {
            if (!Gate("DONATIMETRAJ")) return;
            DonatiMetrajRunner.RunToplam();
        }

        [CommandMethod("DONATIMETRAJKATMANEKLE")]
        public void DonatiMetrajKatmanEkle()
        {
            if (!Gate("DONATIMETRAJ")) return;
            DonatiMetrajLayerPick.RunAdd();
        }

        [CommandMethod("DONATIMETRAJKATMANKALDIR")]
        public void DonatiMetrajKatmanKaldir()
        {
            if (!Gate("DONATIMETRAJ")) return;
            DonatiMetrajLayerPick.RunRemove();
        }

        [CommandMethod("KOTDEGISTIR")]
        public void KotDegistir()
        {
            if (!Gate("KOTDEGISTIR")) return;
            KotDegistirForm.ShowOrActivate();
        }

        [CommandMethod("KOTDEGISTIRUYGULA", CommandFlags.Modal)]
        public void KotDegistirUygula()
        {
            if (!Gate("KOTDEGISTIR")) return;
            KotDegistirRunner.Run();
        }

        [CommandMethod("KOTDEGISTIRKATMANEKLE")]
        public void KotDegistirKatmanEkle()
        {
            if (!Gate("KOTDEGISTIR")) return;
            KotDegistirLayerPick.RunAdd();
        }

        [CommandMethod("KOTDEGISTIRKATMANKALDIR")]
        public void KotDegistirKatmanKaldir()
        {
            if (!Gate("KOTDEGISTIR")) return;
            KotDegistirLayerPick.RunRemove();
        }

        [CommandMethod("OTOOLCUKATMANEKLE")]
        public void OtoOlcuKatmanEkle()
        {
            if (!Gate("OTOOLCU")) return;
            OtoOlcuLayerPick.RunAdd();
        }

        [CommandMethod("OTOOLCUKATMANKALDIR")]
        public void OtoOlcuKatmanKaldir()
        {
            if (!Gate("OTOOLCU")) return;
            OtoOlcuLayerPick.RunRemove();
        }

        /// <summary>Eksik BEYKENT katmanlarını açar. Var olanın renk ve kalınlığına dokunmaz.</summary>
        [CommandMethod("KATMANYUKLE", CommandFlags.Modal)]
        public void KatmanYukle()
        {
            BeykentKatmanYukle.Run();
        }
    }
}
