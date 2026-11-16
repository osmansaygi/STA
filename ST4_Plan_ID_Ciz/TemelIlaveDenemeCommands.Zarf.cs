using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Implementation;
using ST4AksCizCSharp;
using AcRxException = Autodesk.AutoCAD.Runtime.Exception;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    public sealed partial class TemelIlaveDenemeCommands
    {
        /// <summary>
        /// RADYEILAVEZARF: dört temel planı (1 ALT X, 2 ALT Y, 3 UST X, 4 UST Y); her plana yalnız GPR düğüm As'ından
        /// ilave zarfları (kutu + ø yazısı), saydam dolgulu ısı haritası ve renkli lejant (TEMELILAVEDENEME gibi). Yukarı kopya yok.
        /// Zarf ayarları (en az genişlik, paspay, düşey hiza) ve ölçek TEMELILAVEDENEME/TEMELDONATI formundan.
        /// </summary>
        [Autodesk.AutoCAD.Runtime.CommandMethod("RADYEILAVEZARF")]
        public void RadyeIlaveZarf()
        {
            if (!PlanLisans.EnsureOrWarn()) return;
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            const string komut = "RADYEILAVEZARF";

            if (!TemelIlaveDenemeForm.TryShow(olcekSor: true)) return;
            bool yuz = TemelIlaveDenemeForm.Olcek100;
            if (!RememberedFilePrompt.TryPrompt(ed, "\n" + komut + ": ST4 dosyasi secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;
            if (!GprOku(st4Path, ed, komut, out _, out _))
                return;
            try
            {
                var insRes = ed.GetPoint(new PromptPointOptions("\n" + komut + " yerlesim noktasi (SheetViewOut sol-altin 50 cm solu): ") { AllowNone = false });
                if (insRes.Status != PromptStatus.OK) return;
                if (ZarfPlanlariCiz(db, ed, st4Path, insRes.Value, yuz, komut))
                    AcadDocumentViewUtil.ZoomExtentsWithoutNestedCommand(doc);
            }
            catch (System.Exception ex)
            {
                if (ex is AcRxException aex)
                    ed.WriteMessage("\n{0} hata: {1} ({2})", komut, aex.Message, aex.ErrorStatus);
                else
                    ed.WriteMessage("\n{0} hata: {1}", komut, ex.Message);
            }
        }

        /// <summary>RADYEILAVEZARF çizimi (form/istem yok; Core Console testi de bunu çağırır).</summary>
        internal static bool ZarfPlanlariCiz(Database db, Editor ed, string st4Path, Point3d ins, bool yuz, string komut)
        {
            if (!GprOku(st4Path, ed, komut, out List<GprRadyeDugumAs> nodes, out GprRadyeGenelDonati genel))
                return false;

            Face[] faces = Yuzler(genel);
            var captions = new string[faces.Length];
            for (int i = 0; i < faces.Length; i++)
                captions[i] = (i + 1) + " - " + faces[i].Ad + " ILAVE DONATI  (ana " + faces[i].Cubuk + ")";

            {
                NtsGeometryServices.Instance = new NtsGeometryServices(
                    CoordinateArraySequenceFactory.Instance,
                    new PrecisionModel(),
                    0,
                    GeometryOverlay.NG,
                    new CoordinateEqualityComparer());
                var model = new St4Parser().Parse(st4Path);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(st4Path, model);
                var manager = new PlanIdDrawingManager(model);

                var copies = new List<PlanIdDrawingManager.TemelPlanCopyLayout>();
                manager.DrawFoundationPlanWithSections(db, ed, ins, st4Path,
                    yuz ? TemelFoundationPlanScale.Hundred : TemelFoundationPlanScale.Fifty, 4, copies, captions);
                if (copies.Count < 4)
                {
                    ed.WriteMessage("\n{0}: 4 temel kopyasi olusmadi.", komut);
                    return false;
                }

                string rad = Path.ChangeExtension(st4Path, ".RAD");
                if (!File.Exists(rad)) rad = Path.ChangeExtension(st4Path, ".rad");
                bool radVar = RadyeEsikPoligon.TryRadElemanlari(rad, nodes, out List<int[]> elemanlar, out string radHata);
                if (!radVar)
                    ed.WriteMessage("\n{0}: RAD agi kullanilmadi ({1}). Dugum ucgenlemesi.", komut, radHata);
                IList<int[]> ag = radVar ? elemanlar : null;
                List<DonatiAdim> adimlar = KademeSec(db, st4Path, genel, ed, komut, Uretici(db, copies, faces, ag, nodes));

                YaziCarpani = yuz ? 1.5 : 1.0;
                try
                {
                    var isaretler = new List<Isaret>();
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        for (int i = 0; i < faces.Length; i++)
                        {
                            if (copies[i].TemelGeom == null)
                            {
                                ed.WriteMessage("\n{0}: {1}. plan temel zarfi yok, {2} ilave atlandi.", komut, i + 1, faces[i].Ad);
                                continue;
                            }
                            EnsureLayer(tr, db, faces[i].Katman, faces[i].Renk);
                            CizBantlar(tr, btr, copies[i], ag, nodes, faces[i], adimlar, ed, isaretler);
                        }
                        tr.Commit();
                    }
                    CizYonKutulari(db, isaretler, 0.0, ed, komut);
                }
                finally { YaziCarpani = 1.0; }

                ed.WriteMessage("\n{0}: GPR ilave zarflari 4 plana cizildi. Kademe:", komut);
                foreach (DonatiAdim adim in adimlar)
                    ed.WriteMessage(" {0}", adim.Yazi);
                return true;
            }
        }
    }
}
