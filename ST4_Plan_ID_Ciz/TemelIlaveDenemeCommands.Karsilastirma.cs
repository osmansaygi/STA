using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        private const double TakimAraligiCm = 2000.0;

        /// <summary>
        /// RADYELEGANTKARSILASTIRMA: TEMELILAVEDENEME bant/lejant çizimini iki kez üretir — altta GPR düğüm As'ı,
        /// üstte GPR'siz hesap (RadyeAsHesap, mod 4). TEMELILAVEDENEME ve TEMELDONATI değişmez.
        /// </summary>
        [Autodesk.AutoCAD.Runtime.CommandMethod("RADYELEGANTKARSILASTIRMA")]
        public void RadyeLegantKarsilastirma()
        {
            if (!PlanLisans.EnsureOrWarn()) return;
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            const string komut = "RADYELEGANTKARSILASTIRMA";

            if (!TemelIlaveDenemeForm.TryShow()) return;
            if (!RememberedFilePrompt.TryPrompt(ed, "\n" + komut + ": ST4 dosyasi secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            string gpr = Path.ChangeExtension(st4Path, ".GPR");
            bool gprVar = File.Exists(gpr) || File.Exists(Path.ChangeExtension(st4Path, ".gpr"));
            List<GprRadyeDugumAs> gprDugum = null;
            GprRadyeGenelDonati gprGenel = null;
            if (gprVar && !GprOku(st4Path, ed, komut, out gprDugum, out gprGenel))
                gprVar = false;
            if (!gprVar)
                ed.WriteMessage("\n{0}: GPR kullanilamadi; yalniz hesap cizilecek.", komut);

            if (!RadyeAsHesap.TryHesapla(st4Path, out List<GprRadyeDugumAs> hesapDugum, out GprRadyeGenelDonati hesapGenel, out string hata))
            {
                ed.WriteMessage("\n{0}: GPR'siz hesap yapilamadi: {1}", komut, hata);
                if (!gprVar) return;
                hesapDugum = null;
            }

            try
            {
                NtsGeometryServices.Instance = new NtsGeometryServices(
                    CoordinateArraySequenceFactory.Instance,
                    new PrecisionModel(),
                    0,
                    GeometryOverlay.NG,
                    new CoordinateEqualityComparer());
                var model = new St4Parser().Parse(st4Path);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(st4Path, model);
                var insRes = ed.GetPoint(new PromptPointOptions("\n" + komut + " yerlesim noktasi (alt takim GPR, ust takim hesap): ") { AllowNone = false });
                if (insRes.Status != PromptStatus.OK) return;

                Point3d ins = insRes.Value;
                if (gprVar)
                {
                    if (!CizTakim(db, ed, model, st4Path, ins, gprDugum, gprGenel, "GPR (STA)", komut, out Extents3d alt))
                        return;
                    ins = new Point3d(ins.X, ins.Y + (alt.MaxPoint.Y - alt.MinPoint.Y) + TakimAraligiCm, 0);
                }
                if (hesapDugum != null)
                {
                    if (gprVar) Fark(ed, gprDugum, hesapDugum, gprGenel);
                    CizTakim(db, ed, model, st4Path, ins, hesapDugum, hesapGenel, "HESAP (GPR'SIZ, MOD 4)", komut, out _);
                }
            }
            catch (System.Exception ex)
            {
                if (ex is AcRxException aex)
                    ed.WriteMessage("\n{0} hata: {1} ({2})", komut, aex.Message, aex.ErrorStatus);
                else
                    ed.WriteMessage("\n{0} hata: {1}", komut, ex.Message);
            }
        }

        /// <summary>TEMELILAVEDENEME ile aynı dört plan + bant/lejant + yukarı kopya ve yön kutuları; başlık eklenir.</summary>
        private static bool CizTakim(Database db, Editor ed, St4Model model, string st4Path, Point3d ins,
            List<GprRadyeDugumAs> nodes, GprRadyeGenelDonati genel, string baslik, string komut, out Extents3d kapsam)
        {
            kapsam = new Extents3d();
            Face[] faces = Yuzler(genel);
            var captions = new string[faces.Length];
            for (int i = 0; i < faces.Length; i++)
                captions[i] = baslik + " - " + (i + 1) + " - " + faces[i].Ad + "  sabit " + faces[i].Cubuk + "  +40 cm";

            var manager = new PlanIdDrawingManager(model);
            var once = new HashSet<ObjectId>(ModelKimlikleri(db));
            var copies = new List<PlanIdDrawingManager.TemelPlanCopyLayout>();
            manager.DrawFoundationPlanWithSections(db, ed, ins, st4Path, TemelFoundationPlanScale.Fifty, 4, copies, captions);
            if (copies.Count < 4)
            {
                ed.WriteMessage("\n{0}: {1} icin 4 temel kopyasi olusmadi.", komut, baslik);
                return false;
            }

            string rad = Path.ChangeExtension(st4Path, ".RAD");
            if (!File.Exists(rad)) rad = Path.ChangeExtension(st4Path, ".rad");
            bool radVar = RadyeEsikPoligon.TryRadElemanlari(rad, nodes, out List<int[]> elemanlar, out string radHata);
            if (!radVar)
                ed.WriteMessage("\n{0}: RAD agi kullanilmadi ({1}). Dugum ucgenlemesi.", komut, radHata);
            IList<int[]> ag = radVar ? elemanlar : null;
            List<DonatiAdim> adimlar = Merdiven(st4Path);
            var isaretler = new List<Isaret>();
            ed.WriteMessage("\n{0} — {1}:", komut, baslik);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                for (int i = 0; i < 4; i++)
                {
                    EnsureLayer(tr, db, faces[i].Katman, faces[i].Renk);
                    CizBantlar(tr, btr, copies[i], ag, nodes, faces[i], adimlar, ed, isaretler);
                }
                tr.Commit();
            }

            var yeni = new ObjectIdCollection();
            foreach (ObjectId id in ModelKimlikleri(db))
                if (!once.Contains(id)) yeni.Add(id);
            KopyalaYukari(db, yeni, KopyaYukariCm);
            CizYonKutulari(db, isaretler, KopyaYukariCm);

            bool dolu = false;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (ObjectId id in ModelKimlikleri(db))
                {
                    if (once.Contains(id) || !(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    try
                    {
                        Extents3d e = ent.GeometricExtents;
                        if (dolu) kapsam.AddExtents(e); else { kapsam = e; dolu = true; }
                    }
                    catch (AcRxException) { /* Kapsamı olmayan nesne (boş metin vb.) atlanır. */ }
                }
                tr.Commit();
            }
            if (!dolu) return false;
            Baslik(db, kapsam, baslik);
            return true;
        }

        private static void Baslik(Database db, Extents3d kapsam, string baslik)
        {
            const string katman = "DONATI YAZISI (BEYKENT)";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(tr, db, katman, 3);
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                var t = new DBText
                {
                    TextString = "RADYE LEGANT KARSILASTIRMA — " + baslik,
                    Height = 120.0,
                    Position = new Point3d(kapsam.MinPoint.X, kapsam.MaxPoint.Y + 200.0, 0),
                    Layer = katman
                };
                btr.AppendEntity(t);
                tr.AddNewlyCreatedDBObject(t, true);
                tr.Commit();
            }
        }

        /// <summary>Komut satırına yön bazında ilave düğüm sayıları: GPR, hesap, yalnız GPR, yalnız hesap.</summary>
        private static void Fark(Editor ed, List<GprRadyeDugumAs> gpr, List<GprRadyeDugumAs> hesap, GprRadyeGenelDonati genel)
        {
            var byNo = new Dictionary<int, GprRadyeDugumAs>();
            foreach (var d in hesap) byNo[d.No] = d;
            string[] ad = { "X alt", "X ust", "Y alt", "Y ust" };
            double[] sinir = { genel.AsXAlt, genel.AsXUst, genel.AsYAlt, genel.AsYUst };
            for (int i = 0; i < 4; i++)
            {
                int g = 0, h = 0, yalnizG = 0, yalnizH = 0;
                foreach (var d in gpr)
                {
                    if (!byNo.TryGetValue(d.No, out var c)) continue;
                    bool a = Deger(d, i) > sinir[i] + 1e-6, b = Deger(c, i) > sinir[i] + 1e-6;
                    if (a) g++; if (b) h++; if (a && !b) yalnizG++; if (b && !a) yalnizH++;
                }
                ed.WriteMessage("\n  {0}: ilave dugum GPR {1}, hesap {2}, yalniz GPR {3}, yalniz hesap {4}",
                    ad[i], g, h, yalnizG, yalnizH);
            }
        }

        private static double Deger(GprRadyeDugumAs d, int i) =>
            i == 0 ? d.AsXAlt : i == 1 ? d.AsXUst : i == 2 ? d.AsYAlt : d.AsYUst;
    }
}
