using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcRxException = Autodesk.AutoCAD.Runtime.Exception;
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
        public void Initialize()
        {
            WindowsAnsiEncodings.EnsureRegistered();
            int loaded = CountLoadedPlanIdAssemblies();
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (loaded > 1)
            {
                try
                {
                    doc?.Editor.WriteMessage(
                        "\nST4_Plan_ID_Ciz: Ayni AutoCAD oturumunda birden fazla DLL yuklu ({0}). MOVE/COPY cokmesi icin AutoCAD'i kapatip yalnizca son DLL ile NETLOAD yapin.",
                        loaded);
                }
                catch { }
                return;
            }
            // NETLOAD sırasında PaletteSet açmak (özellikle Yardımcı ile birlikte) AutoCAD'i göçertebilir.
            Application.Idle -= OnIdleShowStaPanel;
            Application.Idle += OnIdleShowStaPanel;
        }

        private static void OnIdleShowStaPanel(object sender, EventArgs e)
        {
            Application.Idle -= OnIdleShowStaPanel;
            try
            {
                // Yardımcı Idle'da BAPANEL açıyorsa aynı tick'te çakışmayı azalt.
                if (TryEnterPaletteInitGate(1200))
                {
                    try { CommandPaletteManager.Show(); }
                    finally { ExitPaletteInitGate(); }
                }
                else
                    CommandPaletteManager.Show();
            }
            catch { }
        }

        /// <summary>Yardımcı ile ortak named mutex — iki eklenti aynı anda PaletteSet yaratmasın.</summary>
        internal static bool TryEnterPaletteInitGate(int timeoutMs)
        {
            try
            {
                bool created;
                _paletteGate = new System.Threading.Mutex(false, @"Local\ST4_AcadPaletteInit", out created);
                return _paletteGate.WaitOne(timeoutMs < 0 ? 0 : timeoutMs);
            }
            catch { return false; }
        }

        internal static void ExitPaletteInitGate()
        {
            try { _paletteGate?.ReleaseMutex(); } catch { }
            try { _paletteGate?.Dispose(); } catch { }
            _paletteGate = null;
        }

        private static System.Threading.Mutex _paletteGate;

        public void Terminate()
        {
            try { Application.Idle -= OnIdleShowStaPanel; } catch { }
            CommandPaletteManager.Shutdown();
        }

        private static int CountLoadedPlanIdAssemblies()
        {
            int n = 0;
            foreach (System.Reflection.Assembly a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = a.GetName().Name ?? "";
                // ST4_Aks kaynak olarak Plan_ID içinde; ayrı DLL sayma (yanlış "çift yükleme" uyarısı).
                if (name.StartsWith("ST4_Plan_ID_Ciz", System.StringComparison.OrdinalIgnoreCase))
                    n++;
            }
            return n;
        }
    }

    public class CommandEntry
    {
        /// <summary>
        /// STA komutları çalışırken aktif çizimde ızgara kapalı ve genel çizgi tipi ölçeği sabitlensin.
        /// </summary>
        private static void ApplyStaDefaultDrawingDisplaySettings(Document doc)
        {
            if (doc == null) return;
            try
            {
                Application.SetSystemVariable("GRIDMODE", (short)0);
            }
            catch { /* sürüm / bağlam */ }
            try
            {
                Application.SetSystemVariable("LTSCALE", 10.0);
            }
            catch { /* sürüm / bağlam */ }
            try
            {
                BeykentCizgiLayer.Ensure(doc.Database);
            }
            catch { /* katman acilamazsa cizim devam */ }
        }

        private static void FinishStaDrawing(Document doc)
        {
            if (doc == null) return;
            // Katman purge komut bitiminde AutoCAD fatal error üretebiliyor; ST4KATMANTEMIZLE ile elle.
            AcadDocumentViewUtil.ZoomExtentsWithoutNestedCommand(doc);
        }

        private static void PurgeStaUnusedLayers(Document doc)
        {
            if (doc == null) return;
            try { UnusedLayerPurger.PurgeUnusedLayers(doc.Database, doc.Editor); }
            catch { }
        }

        private static void ConfigureNtsNextGenOverlay()
        {
            NtsGeometryServices.Instance = new NtsGeometryServices(
                CoordinateArraySequenceFactory.Instance,
                new PrecisionModel(),
                0,
                GeometryOverlay.NG,
                new CoordinateEqualityComparer());
        }

        private static void WriteGprAxisSummary(Editor ed, St4Model model)
        {
            if (ed == null || model == null) return;
            int nx = model.GprAxisXLabelByRow?.Count ?? 0;
            int ny = model.GprAxisYLabelByRow?.Count ?? 0;
            ed.WriteMessage("\n[GPR aks etiketi] X={0} satir, Y={1} satir.", nx, ny);
        }

        private static void WriteStaCommandError(Editor ed, string commandTag, System.Exception ex)
        {
            if (ed == null || ex == null) return;
            if (ex is AcRxException aex)
            {
                ed.WriteMessage("\n{0} hata: {1} ({2})", commandTag, aex.Message, aex.ErrorStatus);
                return;
            }
            var e = ex;
            if (e is System.AggregateException agg && agg.InnerExceptions.Count > 0)
                e = agg.Flatten().InnerExceptions[0];
            ed.WriteMessage("\n{0} hata: {1}", commandTag, e.Message);
            if (e.InnerException != null)
                ed.WriteMessage("  Inner: {0}", e.InnerException.Message);
        }

        /// <summary>STA komut paletini yeniden açar.</summary>
        [CommandMethod("STAPANEL")]
        public void StaPanel()
        {
            CommandPaletteManager.Show();
        }

        /// <summary>Aktif çizimde hiç nesne kullanmayan katmanları siler (0 / Defpoints / güncel katman / xref hariç). Herhangi bir DWG.</summary>
        [CommandMethod("ST4KATMANTEMIZLE")]
        public void St4KatmanTemizle()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            int n;
            using (doc.LockDocument())
                n = UnusedLayerPurger.PurgeUnusedLayers(doc.Database, doc.Editor);
            if (n == 0)
                doc.Editor.WriteMessage("\nST4KATMANTEMIZLE: Silinecek kullanilmayan katman yok.");
        }

        /// <summary>
        /// ST4 dosyasından akslar, kolonlar (poligon dahil), kirişler ve perdeleri
        /// tüm eleman ID'leriyle çizer. Her plan kendi antetindedir; yerleşim noktası
        /// en soldaki antetin SheetViewOut sol-altının 50 cm soludur. Antetler arası 50 cm.
        /// </summary>
        [CommandMethod("ST4PLANID")]
        public void Run()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nSTA4CAD ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            var insRes = ed.GetPoint(new PromptPointOptions(
                "\nST4PLANID yerlesim noktasi (SheetViewOut sol-altin 50 cm solu): ") { AllowNone = false });
            if (insRes.Status != PromptStatus.OK) return;

            try
            {
                ConfigureNtsNextGenOverlay();
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(st4Path, model);
                WriteGprAxisSummary(ed, model);
                var manager = new PlanIdDrawingManager(model);
                manager.Draw(db, ed, insRes.Value, st4Path);
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "ST4PLANID", ex);
            }
        }

        /// <summary>
        /// Kalıp iskele planı: kapalı poligon etrafında iskele çizimi (iskele_plan.lsp ISKELEPLAN ile aynı mantık).
        /// </summary>
        [CommandMethod("ISKELECIZ")]
        public void IskeleCiz()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);
            IskelePlanRunner.Execute(doc);
            PurgeStaUnusedLayers(doc);
        }

        /// <summary>
        /// Iskele kesiti: bu oturumda ISKELECIZ sonrasi kesit cizgisi + ISKELE YATAY kesisimleri + ST4 kotlari; ISKELE_KESIT.dxf sablonu yerlestirilir.
        /// </summary>
        [CommandMethod("ISKELEKESIT")]
        public void IskeleKesit()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);
            IskeleKesitRunner.Execute(doc);
            PurgeStaUnusedLayers(doc);
        }

        /// <summary>
        /// Kiriş/kesit: ST4 + düzeltilmemiş DWG seçilir, KIRISDUZ mantığıyla düzeltilir,
        /// ana antete yerleştirilir (nokta = SheetViewOut sol-altın 50 cm solu).
        /// Antet başlığı kiriş isminden kat koduna göre (ör. KB-01 → BODRUM KAT KIRIS ACILIMLARI).
        /// </summary>
        [CommandMethod("KIRISDUZELT")]
        public void KirisDuzelt()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);
            var ed = doc.Editor;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nKIRISDUZELT icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            string st4Dir = Path.GetDirectoryName(st4Path);
            if (!RememberedFilePrompt.TryPrompt(ed,
                "\nKiris acilimi DWG secin (henuz duzeltilmemis STA ciktisi)",
                "DWG Dosyalari (*.dwg)|*.dwg|Tum Dosyalar (*.*)|*.*",
                RememberedFilePrompt.KindDwg, out string dwgPath, fallbackInitialDirectory: st4Dir))
                return;

            var insRes = ed.GetPoint(new PromptPointOptions(
                "\nKIRISDUZELT yerlesim noktasi (SheetViewOut sol-altin 50 cm solu): ")
            { AllowNone = false });
            if (insRes.Status != PromptStatus.OK) return;

            try
            {
                KirisDuzeltRunner.ExecuteFromFiles(doc, st4Path, dwgPath, insRes.Value);
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "KIRISDUZELT", ex);
            }
        }

        /// <summary>
        /// Temel kiriş/kesit: ST4 + düzeltilmemiş DWG seçilir, TKIRIS mantığıyla düzeltilir,
        /// ana antete yerleştirilir (nokta = SheetViewOut sol-altın 50 cm solu).
        /// </summary>
        [CommandMethod("TEMELKIRISDUZELT")]
        public void TemelKirisDuzelt()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);
            var ed = doc.Editor;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nTEMELKIRISDUZELT icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            string st4Dir = Path.GetDirectoryName(st4Path);
            if (!RememberedFilePrompt.TryPrompt(ed,
                "\nTemel kiris acilimi DWG secin (henuz duzeltilmemis STA ciktisi)",
                "DWG Dosyalari (*.dwg)|*.dwg|Tum Dosyalar (*.*)|*.*",
                RememberedFilePrompt.KindDwg, out string dwgPath, fallbackInitialDirectory: st4Dir))
                return;

            var insRes = ed.GetPoint(new PromptPointOptions(
                "\nTEMELKIRISDUZELT yerlesim noktasi (SheetViewOut sol-altin 50 cm solu): ")
            { AllowNone = false });
            if (insRes.Status != PromptStatus.OK) return;

            try
            {
                TemelKirisDuzeltRunner.ExecuteFromFiles(doc, st4Path, dwgPath, insRes.Value);
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "TEMELKIRISDUZELT", ex);
            }
        }

        /// <summary>
        /// Kolon donatı tablosu: ST4 seçilir, aynı dizinde GPR (yoksa PRN) bulunur;
        /// tablo yerleşim noktası alınır ve tablo çizilir.
        /// </summary>
        [CommandMethod("KOLONDATA")]
        public void KolonData()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nKolon donati tablosu icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;
            string dir = Path.GetDirectoryName(st4Path);
            string baseName = Path.GetFileNameWithoutExtension(st4Path);
            string gprPath = Path.Combine(dir, baseName + ".GPR");
            string prnPath = Path.Combine(dir, baseName + ".PRN");

            string dataPath = null;
            if (File.Exists(gprPath))
                dataPath = gprPath;
            else if (File.Exists(prnPath))
                dataPath = prnPath;
            else
            {
                if (!RememberedFilePrompt.TryPrompt(ed,
                    "\nGPR/PRN bulunamadi. Kolon donati verisi (GPR veya PRN) dosyasini secin",
                    "GPR/PRN (*.gpr;*.prn)|*.gpr;*.prn|Tum Dosyalar (*.*)|*.*",
                    RememberedFilePrompt.KindGpr, out dataPath, fallbackInitialDirectory: dir))
                    return;
            }

            St4Model model;
            try
            {
                var parser = new St4Parser();
                model = parser.Parse(st4Path);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nKOLONDATA ST4 okuma hatasi: {0}", ex.Message);
                return;
            }

            var columnData = KolonDonatiTableDrawer.ParseKolonBetonarmeFromFile(dataPath, out string parseError);
            if (parseError != null)
            {
                ed.WriteMessage("\nKOLONDATA: {0}", parseError);
                return;
            }

            var firstFloor = model.Floors.Count > 0 ? model.Floors[0] : null;
            var planMgr = new PlanIdDrawingManager(model);
            var columnFoundationHeights = firstFloor != null ? planMgr.GetColumnFoundationHeights(firstFloor) : null;
            var columnDimsByFloor = new List<Dictionary<int, (int columnType, double W, double H)>>();
            var columnTableExtraByFloor = new List<Dictionary<int, (double altKotCm, double yukseklikCm, double? kirisUstAltFarkCm)>>();
            for (int i = 0; i < model.Floors.Count; i++)
            {
                var floor = model.Floors[i];
                columnDimsByFloor.Add(planMgr.GetColumnDimensionsForFloor(floor));
                columnTableExtraByFloor.Add(planMgr.GetColumnTableExtraData(floor));
            }

            var columnActiveCells = new HashSet<(int floorIndex, int columnNo)>();
            for (int fi = 0; fi < model.Floors.Count; fi++)
            {
                var fl = model.Floors[fi];
                foreach (var col in model.Columns)
                {
                    if (planMgr.HasColumnOnFloor(fl, col))
                        columnActiveCells.Add((fi, col.ColumnNo));
                }
            }

            var ptOpts = new PromptPointOptions("\nTablo yerlestirme noktasi: ") { AllowNone = false };
            var ptRes = ed.GetPoint(ptOpts);
            if (ptRes.Status != PromptStatus.OK) return;

            Point3d insertPoint = ptRes.Value;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    bool ok = KolonDonatiTableDrawer.Draw(model, columnData, insertPoint, db, ed, tr, btr, columnFoundationHeights, columnDimsByFloor, columnTableExtraByFloor, columnActiveCells);
                    if (ok)
                        tr.Commit();
                }
                PurgeStaUnusedLayers(doc);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nKOLONDATA cizim hatasi: {0}", ex.Message);
            }
        }

        /// <summary>
        /// STA4CAD *.KSF keşif dosyasını AutoCAD tablosu olarak çizer. Katman şimdilik 0.
        /// </summary>
        [CommandMethod("METRAJ")]
        public void Metraj()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nMETRAJ: KSF keşif dosyasi secin",
                "KSF Dosyalari (*.ksf)|*.ksf|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindKsf, out string ksfPath))
                return;

            KsfDocument ksf;
            try
            {
                byte[] bytes = File.ReadAllBytes(ksfPath);
                ksf = KsfParser.Parse(bytes, Path.GetFileName(ksfPath));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nMETRAJ okuma hatasi: {0}", ex.Message);
                return;
            }

            try
            {
                var pozlar = KesifOzetiPricer.CollectPozNumbers(ksf);
                ed.WriteMessage("\nMETRAJ: Guncel birim fiyat kontrol ediliyor (CŞIDB YFK, {0} poz)...", pozlar.Count);
                string fiyatDurum = CsbBirimFiyatCatalog.RefreshFromWeb(pozlar);
                ed.WriteMessage("\nMETRAJ: {0}", fiyatDurum);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nMETRAJ: Fiyat kontrolu atlandi ({0}).", ex.Message);
            }

            var ptOpts = new PromptPointOptions("\nTablo yerlestirme noktasi (sol-ust): ") { AllowNone = false };
            var ptRes = ed.GetPoint(ptOpts);
            if (ptRes.Status != PromptStatus.OK) return;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    bool ok = KsfTableDrawer.Draw(ksf, ptRes.Value, db, ed, tr, btr);
                    if (ok)
                        tr.Commit();
                }
                PurgeStaUnusedLayers(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "METRAJ", ex);
            }
        }

        [CommandMethod("ST4KESIT")]
        public void St4Kesit()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nST4KESIT icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            try
            {
                ConfigureNtsNextGenOverlay();
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(st4Path, model);
                WriteGprAxisSummary(ed, model);
                var manager = new PlanIdDrawingManager(model);

                var p1Res = ed.GetPoint(new PromptPointOptions("\nKesit hatti 1. nokta: ") { AllowNone = false });
                if (p1Res.Status != PromptStatus.OK) return;
                var p2Opts = new PromptPointOptions("\nKesit hatti 2. nokta: ") { AllowNone = false, UseBasePoint = true, BasePoint = p1Res.Value };
                var p2Res = ed.GetPoint(p2Opts);
                if (p2Res.Status != PromptStatus.OK) return;

                var insRes = ed.GetPoint(new PromptPointOptions("\nKesitin cizilecegi sol-alt yerlestirme noktasi: ") { AllowNone = false });
                if (insRes.Status != PromptStatus.OK) return;

                var letterOpts = new PromptStringOptions("\nKesit harfi [varsayilan C]: ")
                {
                    AllowSpaces = false,
                    DefaultValue = "C",
                    UseDefaultValue = true
                };
                var letterRes = ed.GetString(letterOpts);
                if (letterRes.Status != PromptStatus.OK && letterRes.Status != PromptStatus.None) return;
                string letter = string.IsNullOrWhiteSpace(letterRes.StringResult) ? "C" : letterRes.StringResult.Trim();

                bool ok = manager.DrawSectionFromUserCut(db, ed, p1Res.Value, p2Res.Value, insRes.Value, letter);
                if (ok)
                    FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "ST4KESIT", ex);
            }
        }

        [CommandMethod("TEMEL50ST4")]
        public void Temel50St4()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nTEMEL50ST4 icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            try
            {
                ConfigureNtsNextGenOverlay();
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(st4Path, model);
                WriteGprAxisSummary(ed, model);
                var manager = new PlanIdDrawingManager(model);
                var insRes = ed.GetPoint(new PromptPointOptions("\nTEMEL50ST4 yerlesim noktasi (SheetViewOut sol-altin 50 cm solu): ") { AllowNone = false });
                if (insRes.Status != PromptStatus.OK) return;
                manager.DrawFoundationPlanWithSections(db, ed, insRes.Value, st4Path);
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "TEMEL50ST4", ex);
            }
        }

        [CommandMethod("TEMEL100ST4")]
        public void Temel100St4()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nTEMEL100ST4 icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            try
            {
                ConfigureNtsNextGenOverlay();
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(st4Path, model);
                WriteGprAxisSummary(ed, model);
                var manager = new PlanIdDrawingManager(model);
                var insRes = ed.GetPoint(new PromptPointOptions("\nTEMEL100ST4 yerlesim noktasi (SheetViewOut sol-altin 50 cm solu): ") { AllowNone = false });
                if (insRes.Status != PromptStatus.OK) return;
                manager.DrawFoundationPlanWithSections(db, ed, insRes.Value, st4Path, TemelFoundationPlanScale.Hundred);
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "TEMEL100ST4", ex);
            }
        }

        [CommandMethod("KOLON50ST4")]
        public void Kolon50St4()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nKOLON50ST4 icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            try
            {
                ConfigureNtsNextGenOverlay();
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(st4Path, model);
                WriteGprAxisSummary(ed, model);
                var manager = new PlanIdDrawingManager(model);
                var insRes = ed.GetPoint(new PromptPointOptions("\nKOLON50ST4 yerlesim noktasi (SheetViewOut sol-altin 50 cm solu): ") { AllowNone = false });
                if (insRes.Status != PromptStatus.OK) return;
                manager.DrawColumnApplicationPlan(db, ed, insRes.Value, st4Path, KolonApplicationPlanScale.Fifty, drawPerdeGorunus: true);
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "KOLON50ST4", ex);
            }
        }

        [CommandMethod("KOLON100ST4")]
        public void Kolon100St4()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nKOLON100ST4 icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            try
            {
                ConfigureNtsNextGenOverlay();
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(st4Path, model);
                WriteGprAxisSummary(ed, model);
                var manager = new PlanIdDrawingManager(model);
                var insRes = ed.GetPoint(new PromptPointOptions("\nKOLON100ST4 yerlesim noktasi (SheetViewOut sol-altin 50 cm solu): ") { AllowNone = false });
                if (insRes.Status != PromptStatus.OK) return;
                manager.DrawColumnApplicationPlan(db, ed, insRes.Value, st4Path, KolonApplicationPlanScale.Hundred);
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "KOLON100ST4", ex);
            }
        }

        [CommandMethod("KALIP50ST4")]
        public void Kalip50St4()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nKALIP50ST4 icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            try
            {
                ConfigureNtsNextGenOverlay();
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(st4Path, model);
                WriteGprAxisSummary(ed, model);
                var manager = new PlanIdDrawingManager(model);
                var insRes = ed.GetPoint(new PromptPointOptions("\nKALIP50ST4: En soldaki antet SheetViewOut sol-altinin 50 cm solu: ") { AllowNone = false });
                if (insRes.Status != PromptStatus.OK) return;
                manager.DrawFormworkPlan50(db, ed, insRes.Value, st4Path, KalipPlanScale.Fifty);
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "KALIP50ST4", ex);
            }
        }

        /// <summary>İlk iki model katı için 1/50 sade kalıp planı; kat başına tek kopya; antet, aks, kesit, GPR döşeme yazısı yok; kiriş/perde/kolon/döşeme etiketleri yok. KALIP50ST4 davranışını değiştirmez.</summary>
        [CommandMethod("DENEME1")]
        public void Deneme1KalipIlkIkiKat()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nDENEME1 icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            try
            {
                ConfigureNtsNextGenOverlay();
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(st4Path, model);
                WriteGprAxisSummary(ed, model);
                SlabAxesDirections.PopulateCornerAxisDirections(model);
                var manager = new PlanIdDrawingManager(model);
                var insRes = ed.GetPoint(new PromptPointOptions("\nDENEME1: Ilk kat aks kutusu sol-alt kosesi (yerlesim): ") { AllowNone = false });
                if (insRes.Status != PromptStatus.OK) return;
                manager.DrawFormworkPlan50(
                    db,
                    ed,
                    insRes.Value,
                    st4Path,
                    KalipPlanScale.Fifty,
                    firstIndividualFloorCount: 2,
                    suppressKirisPerdeKolonDosemeLabels: true,
                    completionMessageCmdTag: "DENEME1",
                    layoutFlags: new Kalip50LayoutFlags(
                        plainInsertPoint: true,
                        singleCopyPerFloor: true,
                        noAntetAndBinaSema: true,
                        noAxes: true,
                        noSections: true,
                        drawSlabBoundaryPolylinesOnDosemeHattiLayer: true,
                        excludeStairAdjacentSlabsAndSlabsBelowFloorKot: true,
                        drawInteriorVoidsOnBosLayerNotKalipBosluk: true,
                        classifyDosemeHattiIntoTopologyLayers: true));
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "DENEME1", ex);
            }
        }

        /// <summary>1/50 kalıp planı + /Slabs Data/ mantar fiktif kirişleri (BANT). Kat başına 3 kopya (1–2 boş, 3. donatılı).</summary>
        [CommandMethod("MANTARDOSEME")]
        public void MantarDosemeKalip()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nMANTARDOSEME icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            try
            {
                ConfigureNtsNextGenOverlay();
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(st4Path, model);
                WriteGprAxisSummary(ed, model);
                ed.WriteMessage("\nMANTARDOSEME: Mantar fiktif kiris sayisi = {0}.", model.MushroomBeams?.Count ?? 0);
                var manager = new PlanIdDrawingManager(model);
                var insRes = ed.GetPoint(new PromptPointOptions("\nMANTARDOSEME: En soldaki antet SheetViewOut sol-altinin 50 cm solu: ") { AllowNone = false });
                if (insRes.Status != PromptStatus.OK) return;
                manager.DrawFormworkPlan50(
                    db,
                    ed,
                    insRes.Value,
                    st4Path,
                    KalipPlanScale.Fifty,
                    completionMessageCmdTag: "MANTARDOSEME",
                    layoutFlags: new Kalip50LayoutFlags(includeMushroomBeams: true, copiesPerFloor: 3));
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "MANTARDOSEME", ex);
            }
        }

        /// <summary>ST4 modelinden 1/200 bina görünüşü veya Hepsi (X/Y kesit + 4 görünüş, 1/50 antet).</summary>
        [CommandMethod("BINAGORUNUS")]
        public void BinaGorunus()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nBINAGORUNUS icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            ed.WriteMessage("\nBINAGORUNUS: Asagidan/Yukaridan/Soldan/Sagdan = tek gorunus; Hepsi = X/Y kesit + 4 gorunus (1/50 antet).");
            var kwo = new PromptKeywordOptions("\nBakis yonu")
            {
                AllowNone = false,
                AppendKeywordsToMessage = true
            };
            kwo.Keywords.Add("Asagidan");
            kwo.Keywords.Add("Yukaridan");
            kwo.Keywords.Add("Soldan");
            kwo.Keywords.Add("Sagdan");
            kwo.Keywords.Add("Hepsi");
            kwo.Keywords.Default = "Asagidan";
            var kres = ed.GetKeywords(kwo);
            if (kres.Status != PromptStatus.OK) return;

            string key = (kres.StringResult ?? string.Empty).ToUpperInvariant();
            if (key == "HEPSI")
            {
                var insHepsi = ed.GetPoint(new PromptPointOptions(
                    "\nBINAGORUNUS Hepsi yerlesim noktasi (SheetViewOut sol-altin 50 cm solu): ") { AllowNone = false });
                if (insHepsi.Status != PromptStatus.OK) return;

                try
                {
                    ConfigureNtsNextGenOverlay();
                    var parser = new St4Parser();
                    var model = parser.Parse(st4Path);
                    var manager = new PlanIdDrawingManager(model);
                    manager.DrawBinaGorunusHepsi(db, ed, insHepsi.Value, st4Path);
                    FinishStaDrawing(doc);
                }
                catch (System.Exception ex)
                {
                    WriteStaCommandError(ed, "BINAGORUNUS", ex);
                }
                return;
            }

            BinaGorunusBakis bakis;
            switch (key)
            {
                case "YUKARIDAN": bakis = BinaGorunusBakis.YukaridanAsagiya; break;
                case "SOLDAN": bakis = BinaGorunusBakis.SoldanSaga; break;
                case "SAGDAN": bakis = BinaGorunusBakis.SagdanSola; break;
                default: bakis = BinaGorunusBakis.AsagidanYukariya; break;
            }

            var insRes = ed.GetPoint(new PromptPointOptions("\nBINAGORUNUS yerlestirme noktasi (gorunus sol-alt): ") { AllowNone = false });
            if (insRes.Status != PromptStatus.OK) return;

            try
            {
                ConfigureNtsNextGenOverlay();
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);
                var manager = new PlanIdDrawingManager(model);
                manager.DrawBinaGorunus(db, ed, insRes.Value, bakis);
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "BINAGORUNUS", ex);
            }
        }

        /// <summary>ST4 modelinden 1/200 bina kesiti. X/Y yönü; merdiven döşemesini kesmeyen hat; arka plan görünüş tonu.</summary>
        [CommandMethod("BINAKESITI")]
        public void BinaKesiti()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nBINAKESITI icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            ed.WriteMessage("\nBINAKESITI: X = yatay kesit hatti (Y sabit, bina boyunca X); Y = dikey kesit hatti (X sabit). Merdiven dosemeleri kacinilir.");
            var kwo = new PromptKeywordOptions("\nKesit yonu")
            {
                AllowNone = false,
                AppendKeywordsToMessage = true
            };
            kwo.Keywords.Add("X");
            kwo.Keywords.Add("Y");
            kwo.Keywords.Default = "X";
            var kres = ed.GetKeywords(kwo);
            if (kres.Status != PromptStatus.OK) return;

            BinaKesitYon yon = string.Equals(kres.StringResult, "Y", StringComparison.OrdinalIgnoreCase)
                ? BinaKesitYon.Y
                : BinaKesitYon.X;

            var insRes = ed.GetPoint(new PromptPointOptions("\nBINAKESITI yerlestirme noktasi (kesit sol-alt): ") { AllowNone = false });
            if (insRes.Status != PromptStatus.OK) return;

            try
            {
                ConfigureNtsNextGenOverlay();
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);
                var manager = new PlanIdDrawingManager(model);
                manager.DrawBinaKesit(db, ed, insRes.Value, yon);
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "BINAKESITI", ex);
            }
        }

        /// <summary>Kiriş açılımı (DENEME): ana aks boyunca düşey kesit. 25°'ye kadar kırılan kirişler tek şeritte. Donatı yok.</summary>
        [CommandMethod("KIRISACILIMLARI")]
        public void KirisAcilimlari()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nKIRISACILIMLARI icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            var insRes = ed.GetPoint(new PromptPointOptions("\nKIRISACILIMLARI yerlestirme noktasi (ilk kiris alt-sol): ") { AllowNone = false });
            if (insRes.Status != PromptStatus.OK) return;

            try
            {
                ConfigureNtsNextGenOverlay();
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(st4Path, model);
                var manager = new PlanIdDrawingManager(model);
                manager.DrawKirisAcilimlari(db, ed, insRes.Value, st4Path);
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "KIRISACILIMLARI", ex);
            }
        }

        /// <summary>Kolon düşey açılımı: form (ölçek + kat alma + sınır) → ST4 → yerleştirme.</summary>
        [CommandMethod("KOLONDUSEY")]
        public void KolonDusey()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!KolonDuseyForm.ShowModalAndAccept())
                return;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nKOLONDUSEY icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            try
            {
                ConfigureNtsNextGenOverlay();
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);

                HashSet<int> allowedFloors = null;
                if (KolonDuseySettings.KatSinirla)
                {
                    if (!KolonDuseyForm.TryPickSonKat(model, out allowedFloors))
                    {
                        ed.WriteMessage("\nKOLONDUSEY: kat siniri iptal.");
                        return;
                    }
                }

                string yerPrompt = KolonDuseySettings.KatKatCiz
                    ? "\nKOLONDUSEY yerlesim noktasi (SheetViewOut sol-altin 50 cm solu): "
                    : "\nKOLONDUSEY yerlestirme noktasi (sol-alt): ";
                var insRes = ed.GetPoint(new PromptPointOptions(yerPrompt) { AllowNone = false });
                if (insRes.Status != PromptStatus.OK) return;

                var planMgr = new PlanIdDrawingManager(model);
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    bool ok = planMgr.DrawKolonDuseyFromSt4(
                        insRes.Value, db, ed, tr, btr, st4Path,
                        olcek25: KolonDuseySettings.Olcek25,
                        katKatCiz: KolonDuseySettings.KatKatCiz,
                        benzerKatBirlestir: KolonDuseySettings.BenzerKatBirlestir,
                        allowedFloorIndexes: allowedFloors);
                    if (ok)
                        tr.Commit();
                }
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "KOLONDUSEY", ex);
            }
        }

        /// <summary>Seçilen IC ANTET içindeki kolon düşey donatı çapını çizimden okuyup alan korunarak revize eder.</summary>
        [CommandMethod("KOLONREVIZE")]
        public void KolonRevize()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);
            try
            {
                KolonRevizeRunner.Run();
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(doc.Editor, "KOLONREVIZE", ex);
            }
        }

        /// <summary>Kapama perdesi (GPR panel) düşey açılım; KOLONDUSEY ile aynı antet.</summary>
        [CommandMethod("KAPAMADETAY")]
        public void KapamaDetay()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!KapamaPerdeForm.ShowModalAndAccept())
                return;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nKAPAMADETAY icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            var insRes = ed.GetPoint(new PromptPointOptions(
                "\nKAPAMADETAY yerlesim noktasi (SheetViewOut sol-altin 50 cm solu): ") { AllowNone = false });
            if (insRes.Status != PromptStatus.OK) return;

            try
            {
                ConfigureNtsNextGenOverlay();
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);
                var planMgr = new PlanIdDrawingManager(model);
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    bool ok = planMgr.DrawKapamaDetayFromSt4(insRes.Value, db, ed, tr, btr, st4Path,
                        temelHatili: KapamaPerdeSettings.TemelHatili);
                    if (ok)
                        tr.Commit();
                }
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "KAPAMADETAY", ex);
            }
        }

        /// <summary>Aynı antette 4 temel planı (ölçü / ana donatı / alt ilave / üst ilave). STA TEMEL_PDF X-Y alt ve üst.</summary>
        [CommandMethod("TEMELDONATI")]
        public void TemelDonati()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nTEMELDONATI: ST4 dosyasi secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nTEMELDONATI: TEMEL donati PDF (STA rapor) secin",
                "PDF (*.pdf)|*.pdf|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindPdf, out string pdfPath,
                fallbackInitialDirectory: Path.GetDirectoryName(st4Path)))
                return;

            try
            {
                ConfigureNtsNextGenOverlay();
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(st4Path, model);
                WriteGprAxisSummary(ed, model);
                var manager = new PlanIdDrawingManager(model);
                var insRes = ed.GetPoint(new PromptPointOptions("\nTEMELDONATI yerlesim noktasi (SheetViewOut sol-altin 50 cm solu): ") { AllowNone = false });
                if (insRes.Status != PromptStatus.OK) return;

                var copies = new List<PlanIdDrawingManager.TemelPlanCopyLayout>();
                manager.DrawFoundationPlanWithSections(db, ed, insRes.Value, st4Path, TemelFoundationPlanScale.Fifty, 4, copies);
                if (copies.Count < 4)
                {
                    ed.WriteMessage("\nTEMELDONATI: 4 temel kopyasi olusmadi.");
                    FinishStaDrawing(doc);
                    return;
                }

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    DrawTemelIlaveOnCopy(pdfPath, copies[2], new[] { TemelIlaveDonatiFromPdf.Yon.XAlt, TemelIlaveDonatiFromPdf.Yon.YAlt }, db, ed, tr, btr);
                    DrawTemelIlaveOnCopy(pdfPath, copies[3], new[] { TemelIlaveDonatiFromPdf.Yon.XUst, TemelIlaveDonatiFromPdf.Yon.YUst }, db, ed, tr, btr);
                    tr.Commit();
                }
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "TEMELDONATI", ex);
            }
        }

        private static void DrawTemelIlaveOnCopy(
            string pdfPath,
            PlanIdDrawingManager.TemelPlanCopyLayout copy,
            TemelIlaveDonatiFromPdf.Yon[] yonler,
            Database db,
            Editor ed,
            Transaction tr,
            BlockTableRecord btr)
        {
            if (copy.Envelope == null || copy.TemelGeom == null)
            {
                ed.WriteMessage("\nTEMELDONATI: kopya temel zarfi yok, ilave atlandi.");
                return;
            }
            foreach (var yon in yonler)
            {
                TemelIlaveDonatiFromPdf.Draw(pdfPath, yon, copy.OffsetX, copy.OffsetY, copy.Envelope, db, ed, tr, btr);
                var staBoxes = TemelIlaveDonatiFromPdf.CollectCadBoxes(pdfPath, yon, copy.OffsetX, copy.OffsetY);
                string heatLayer = TemelIlaveDonatiFromPdf.HeatLayer(yon);
                var heatColor = TemelIlaveDonatiFromPdf.HeatColor(yon);
                int cizilen = 0;
                using (Bitmap heat = TemelIlaveDonatiFromPdf.TryLoadHeatBitmap(pdfPath, yon, ed))
                {
                    if (heat != null)
                        cizilen = TemelIlaveDonatiFromPng.DrawBitmap(heat, copy.Envelope, copy.TemelGeom, db, ed, tr, btr, drawStaBoxes: false, staBoxes, heatLayer, heatColor);
                    else
                    {
                        string heatPng = TemelIlaveDonatiFromPdf.FindHeatPng(pdfPath, yon);
                        if (!string.IsNullOrEmpty(heatPng))
                            cizilen = TemelIlaveDonatiFromPng.Draw(heatPng, copy.Envelope, copy.TemelGeom, db, ed, tr, btr, drawStaBoxes: false, staBoxes, heatLayer, heatColor);
                        else
                            ed.WriteMessage("\nTEMELDONATI: {0} isi grafigi alinamadi.", yon);
                    }
                }
                // İlave donatı kutusu olan her bölgede ilave bölgesi de bulunmak zorundadır:
                // grafik hiç okunamadıysa bölge doğrudan STA kutularından çizilir.
                if (cizilen <= 0 && staBoxes.Count > 0)
                    TemelIlaveDonatiFromPng.DrawStaBoxesAsHeat(staBoxes, copy.TemelGeom, db, ed, tr, btr, heatLayer, heatColor);
            }
        }

        [CommandMethod("KALIP100ST4")]
        public void Kalip100St4()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyStaDefaultDrawingDisplaySettings(doc);

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nKALIP100ST4 icin ST4 Dosyasi Secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            try
            {
                ConfigureNtsNextGenOverlay();
                var parser = new St4Parser();
                var model = parser.Parse(st4Path);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(st4Path, model);
                WriteGprAxisSummary(ed, model);
                var manager = new PlanIdDrawingManager(model);
                var insRes = ed.GetPoint(new PromptPointOptions("\nKALIP100ST4: En soldaki antet SheetViewOut sol-altinin 50 cm solu: ") { AllowNone = false });
                if (insRes.Status != PromptStatus.OK) return;
                manager.DrawFormworkPlan50(db, ed, insRes.Value, st4Path, KalipPlanScale.Hundred);
                FinishStaDrawing(doc);
            }
            catch (System.Exception ex)
            {
                WriteStaCommandError(ed, "KALIP100ST4", ex);
            }
        }
    }
}
