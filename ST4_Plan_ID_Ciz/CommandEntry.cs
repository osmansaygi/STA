using System;
using System.Collections.Generic;
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
                GprYapiAksLabels.TryMergeFromGprBesideSt4(fileRes.StringResult, model);
                var manager = new PlanIdDrawingManager(model);
                manager.Draw(db, ed);
                doc.SendStringToExecute("_.ZOOM _E ", true, false, false);
            }
            catch (AcRxException aex)
            {
                ed.WriteMessage("\nST4PLANID hata: {0} ({1})", aex.Message, aex.ErrorStatus);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nST4PLANID hata: {0}", ex.Message);
                if (ex.InnerException != null)
                    ed.WriteMessage("  Inner: {0}", ex.InnerException.Message);
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

            var ed = doc.Editor;
            var db = doc.Database;

            var opts = new PromptOpenFileOptions("\nKolon donati tablosu icin ST4 Dosyasi Secin")
            {
                Filter = "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*"
            };

            var fileRes = ed.GetFileNameForOpen(opts);
            if (fileRes.Status != PromptStatus.OK) return;

            string st4Path = fileRes.StringResult;
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
                var dataOpts = new PromptOpenFileOptions("\nGPR/PRN bulunamadi. Kolon donati verisi (GPR veya PRN) dosyasini secin")
                {
                    Filter = "GPR/PRN (*.gpr;*.prn)|*.gpr;*.prn|Tum Dosyalar (*.*)|*.*"
                };
                var dataRes = ed.GetFileNameForOpen(dataOpts);
                if (dataRes.Status != PromptStatus.OK) return;
                dataPath = dataRes.StringResult;
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
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nKOLONDATA cizim hatasi: {0}", ex.Message);
            }
        }

        [CommandMethod("ST4KESIT")]
        public void St4Kesit()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;
            var db = doc.Database;

            var opts = new PromptOpenFileOptions("\nST4KESIT icin ST4 Dosyasi Secin")
            {
                Filter = "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*"
            };
            var fileRes = ed.GetFileNameForOpen(opts);
            if (fileRes.Status != PromptStatus.OK) return;

            try
            {
                NtsGeometryServices.Instance = new NtsGeometryServices(
                    CoordinateArraySequenceFactory.Instance,
                    new PrecisionModel(),
                    0,
                    GeometryOverlay.NG,
                    new CoordinateEqualityComparer());

                var parser = new St4Parser();
                var model = parser.Parse(fileRes.StringResult);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(fileRes.StringResult, model);
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
                    doc.SendStringToExecute("_.REGEN ", true, false, false);
            }
            catch (AcRxException aex)
            {
                ed.WriteMessage("\nST4KESIT hata: {0} ({1})", aex.Message, aex.ErrorStatus);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nST4KESIT hata: {0}", ex.Message);
                if (ex.InnerException != null)
                    ed.WriteMessage("  Inner: {0}", ex.InnerException.Message);
            }
        }
    }
}
