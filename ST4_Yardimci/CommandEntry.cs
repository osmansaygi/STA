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
        private bool _ownsInitialization;

        public void Initialize()
        {
            try
            {
                if (_loadMutex != null) return;
                var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                WarnIfDuplicateAssemblies(ed);
                // A process-specific named handle is shared by timestamped DLL copies.
                // Keep the first handle alive; a second copy must not create another palette.
                bool created;
                var registration = new Mutex(false, @"Local\ST4_Yardimci_ACAD_" +
                    System.Diagnostics.Process.GetCurrentProcess().Id, out created);
                if (!created)
                {
                    registration.Dispose();
                    ed?.WriteMessage("\nBETONARME TOOL: Bu oturumda Yardimci zaten yuklu. Ikinci kopyanin panel acilisi engellendi. Yeni surum icin AutoCAD'i yeniden baslatin.");
                    return;
                }
                _loadMutex = registration;
                _ownsInitialization = true;
                LogLoadStage("Initialize: " + Assembly.GetExecutingAssembly().Location);
                AppDomain.CurrentDomain.AssemblyResolve -= ResolvePluginDependency;
                AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginDependency;

                if (!MachineLicense.IsAllowed())
                {
                    if (ed != null)
                        ed.WriteMessage(
                            "\nBETONARME TOOL: Bu bilgisayar için lisans yok (yetkisiz makine).");
                    return;
                }
                if (ed != null)
                    ed.WriteMessage(BaHomeRibbonCatalog.LegacyPaletteEnabled
                        ? "\nBETONARME TOOL yuklendi. BAPANEL otomatik aciliyor…  |  OTOOLCU  PLANOLCU  MERDIVEN  DOSEMEDONATI  OTODONATI  SEHPADONATI  HASIRDONATI  RADYEILAVE  TEMELHATILI  TEMELCEKURU  TEMELDETAY  DONATIBOYU  DONATIMETRAJ  DONATI ADET  DONATI OKU  KOTDEGISTIR  KATMANYUKLE"
                        : "\nBETONARME TOOL yuklendi. Home: Olcu / Detay / Donati / Genel Duzenleme.");

                // NETLOAD sırasında palet hemen açılamayabilir; Idle'da bir kez aç.
                AcApp.Idle -= OnIdleShowBaPanel;
                if (!string.Equals(System.Diagnostics.Process.GetCurrentProcess().ProcessName,
                    "accoreconsole", StringComparison.OrdinalIgnoreCase))
                {
                    if (BaHomeRibbonCatalog.LegacyPaletteEnabled)
                        AcApp.Idle += OnIdleShowBaPanel;
#if BA_HOME_RIBBON
                    BaHomeRibbonManager.Initialize();
#endif
                }
            }
            catch { }
        }

        private static void OnIdleShowBaPanel(object sender, EventArgs e)
        {
            AcApp.Idle -= OnIdleShowBaPanel;
            try
            {
                if (!BaHomeRibbonCatalog.LegacyPaletteEnabled || !MachineLicense.IsAllowed()) return;
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null || !doc.Editor.IsQuiescent)
                {
                    AcApp.Idle += OnIdleShowBaPanel;
                    return;
                }
                // Queue a normal command after NETLOAD/startup completes. Never create
                // native palette windows inside an Idle callback or assembly initialization.
                LogLoadStage("BAPANEL queued");
                doc.SendStringToExecute("BAPANEL ", true, false, false);
            }
            catch (System.Exception ex) { LogLoadStage("Panel queue failed: " + ex); }
        }

        internal static void LogLoadStage(string stage)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "ST4_Yardimci-load.log"),
                    DateTime.UtcNow.ToString("o") + " PID=" + System.Diagnostics.Process.GetCurrentProcess().Id
                    + " " + stage + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>Bundle klasöründeki NTS / System.* — çalışma dizini AutoCAD olunca FileNotFound süreç düşürebilir.</summary>
        private static Assembly ResolvePluginDependency(object sender, ResolveEventArgs args)
        {
            try
            {
                if (args == null || string.IsNullOrEmpty(args.Name)) return null;
                var requested = new AssemblyName(args.Name);
                string simple = requested.Name;
                if (string.IsNullOrEmpty(simple) || simple.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                    return null;
                if (!string.Equals(Path.GetFileName(simple), simple, StringComparison.Ordinal)) return null;
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(dir)) return null;
                string path = Path.Combine(dir, simple + ".dll");
                if (!File.Exists(path)) return null;
                // Do not bind an unrelated/old DLL merely because its filename matches.
                // Returning null leaves host/framework resolution to AutoCAD.
                var candidate = AssemblyName.GetAssemblyName(path);
                if (!string.Equals(candidate.Name, requested.Name, StringComparison.OrdinalIgnoreCase)
                    || (requested.Version != null && candidate.Version != requested.Version)
                    || !string.Equals(candidate.CultureName ?? "", requested.CultureName ?? "", StringComparison.OrdinalIgnoreCase)
                    || Convert.ToBase64String(candidate.GetPublicKeyToken() ?? new byte[0])
                        != Convert.ToBase64String(requested.GetPublicKeyToken() ?? new byte[0])) return null;
                return Assembly.LoadFrom(path);
            }
            catch { }
            return null;
        }

        public void Terminate()
        {
            if (!_ownsInitialization) return;
            LogLoadStage("Terminate begin");
            try { AcApp.Idle -= OnIdleShowBaPanel; } catch { }
#if BA_HOME_RIBBON
            try { BaHomeRibbonManager.Shutdown(); } catch { }
#endif
            try { AppDomain.CurrentDomain.AssemblyResolve -= ResolvePluginDependency; } catch { }
            try { OtoOlcuSettings.Save(); } catch { }
            try { PlanOlcuSettings.Save(); } catch { }
            try { OtoDonatiSettings.Save(); } catch { }
            try { SehpaDonatiSettings.Save(); } catch { }
            try { HasirDonatiSettings.Save(); } catch { }
            // Native modeless windows belong to the host; closing them during Terminate can re-enter destroyed UI.
            try { St4LispPaletteManager.Shutdown(); } catch { }
            try { BaTopToolbar.Shutdown(); } catch { }
            try { _loadMutex?.Dispose(); _loadMutex = null; } catch { }
            LogLoadStage("Terminate completed");
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
#if BA_HOME_RIBBON
        [CommandMethod("BARIBBONAUTO", CommandFlags.NoHistory)]
        public void BaRibbonAuto()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            BaHomeRibbonManager.InstallPending();
        }

        [CommandMethod("BARIBBON")]
        public void BaRibbon()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            BaHomeRibbonManager.Show();
        }

        [CommandMethod("BARIBBONKAPAT")]
        public void BaRibbonKapat()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            BaHomeRibbonManager.Hide();
        }
#endif
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
            if (!BaHomeRibbonCatalog.LegacyPaletteEnabled) return;
#if !AUTOCAD2027
            PluginLifecycle.LogLoadStage("BAPANEL creating/showing palette");
            St4LispPaletteManager.Show();
            PluginLifecycle.LogLoadStage("BAPANEL completed");
#endif
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
            if (!BaHomeRibbonCatalog.LegacyPaletteEnabled) return;
#if !AUTOCAD2027
            St4LispPaletteManager.Show();
#endif
        }

        [CommandMethod("ST4LISP")]
        public void St4LispPanelAlias()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            if (!BaHomeRibbonCatalog.LegacyPaletteEnabled) return;
#if !AUTOCAD2027
            St4LispPaletteManager.Show();
#endif
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

        [CommandMethod("PARAPET")]
        [CommandMethod("PARAPETDETAY")]
        public void Parapet()
        {
            if (!Gate("PARAPET")) return;
            ParapetForm.ShowOrActivate();
        }

        [CommandMethod("PARAPETCIZ", CommandFlags.Modal)]
        public void ParapetCiz()
        {
            if (!Gate("PARAPET")) return;
            ParapetRunner.RunManual();
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

        /// <summary>Temel detayı — temel çukurunun tek kesiti; sol ve sağ yükseklik ayrı.</summary>
        [CommandMethod("TEMELDETAY")]
        public void TemelDetay()
        {
            if (!Gate("TEMELDETAY")) return;
            TemelDetayForm.ShowOrActivate();
        }

        [CommandMethod("TEMELDETAYCIZ", CommandFlags.Modal)]
        public void TemelDetayCiz()
        {
            if (!Gate("TEMELDETAY")) return;
            TemelCekuruRunner.RunDetay();
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

        [CommandMethod("PERDEBOSLUK", CommandFlags.Modal)]
        [CommandMethod("PERDE BOSLUK", CommandFlags.Modal)]
        public void PerdeBosluk()
        {
            if (!Gate("PERDEBOSLUK")) return;
            PerdeBoslukRunner.RunInteractive();
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

        [CommandMethod("DONATIBOYUCIZIMKATMANEKLE")]
        public void DonatiBoyuCizimKatmanEkle()
        {
            if (!Gate("DONATIBOYU")) return;
            DonatiBoyuDrawingLayerPick.RunAdd();
        }

        [CommandMethod("DONATIBOYUCIZIMKATMANKALDIR")]
        public void DonatiBoyuCizimKatmanKaldir()
        {
            if (!Gate("DONATIBOYU")) return;
            DonatiBoyuDrawingLayerPick.RunRemove();
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

        /// <summary>Katman / ölçü stili / yazı stili ayar formu; Yükle ile açık çizime uygular.</summary>
        [CommandMethod("KATMANYUKLE", CommandFlags.Modal)]
        public void KatmanYukle()
        {
            BeykentKatmanYukle.Run();
        }
    }
}
