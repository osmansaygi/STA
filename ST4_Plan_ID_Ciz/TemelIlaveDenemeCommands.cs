using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
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

[assembly: Autodesk.AutoCAD.Runtime.CommandClass(typeof(ST4PlanIdCiz.TemelIlaveDenemeCommands))]

namespace ST4PlanIdCiz
{
    /// <summary>TEMELILAVEDENEME: dört temel planında mesh düğümlerinden sabit donatıyı aşan ilave yerleri.</summary>
    public sealed class TemelIlaveDenemeCommands
    {
        private const double OfsetM = 0.40;
        internal static double TemelPaspayCm = 5.0;
        internal static double DuseyHizaFarkCm = 100.0;
        private const double KopyaYukariCm = 6000.0;
        private const short YetmezRenk = 1;
        private static readonly short[] BantRenk = { 4, 3, 2, 30, 40, 6, 5, 140, 150, 200, 8, 9 };

        [Autodesk.AutoCAD.Runtime.CommandMethod("TEMELILAVEDENEME")]
        public void TemelIlaveDeneme()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            if (!TemelIlaveDenemeForm.TryShow()) return;
            ed.WriteMessage(
                "\nTEMELILAVEDENEME: en az dilim {0} cm, paspay {1} cm, dusey hiza {2} cm.",
                TemelIlaveKutuBol.MinDilimGenislikCm.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture),
                TemelPaspayCm.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture),
                DuseyHizaFarkCm.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture));

            if (!RememberedFilePrompt.TryPrompt(ed, "\nTEMELILAVEDENEME: ST4 dosyasi secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            if (!GprOku(st4Path, ed, "TEMELILAVEDENEME", out List<GprRadyeDugumAs> nodes, out GprRadyeGenelDonati genel))
                return;
            Face[] faces = Yuzler(genel);
            var captions = new string[faces.Length];
            for (int i = 0; i < faces.Length; i++)
                captions[i] = (i + 1) + " - " + faces[i].Ad + "  sabit " + faces[i].Cubuk + "  +40 cm";

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
                var manager = new PlanIdDrawingManager(model);
                var insRes = ed.GetPoint(new PromptPointOptions("\nTEMELILAVEDENEME yerlesim noktasi (SheetViewOut sol-altin 50 cm solu): ") { AllowNone = false });
                if (insRes.Status != PromptStatus.OK) return;

                var once = new HashSet<ObjectId>(ModelKimlikleri(db));
                var copies = new List<PlanIdDrawingManager.TemelPlanCopyLayout>();
                manager.DrawFoundationPlanWithSections(db, ed, insRes.Value, st4Path, TemelFoundationPlanScale.Fifty, 4, copies, captions);
                if (copies.Count < 4)
                {
                    ed.WriteMessage("\nTEMELILAVEDENEME: 4 temel kopyasi olusmadi.");
                    return;
                }

                string rad = Path.ChangeExtension(st4Path, ".RAD");
                if (!File.Exists(rad)) rad = Path.ChangeExtension(st4Path, ".rad");
                bool radVar = RadyeEsikPoligon.TryRadElemanlari(rad, nodes, out List<int[]> elemanlar, out string radHata);
                if (!radVar)
                    ed.WriteMessage("\nTEMELILAVEDENEME: RAD agi kullanilmadi ({0}). Dugum ucgenlemesi.", radHata);

                IList<int[]> ag = radVar ? elemanlar : null;
                List<DonatiAdim> adimlar = Merdiven(st4Path);
                var isaretler = new List<Isaret>();
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

                ed.WriteMessage("\nTEMELILAVEDENEME: ilave kontur +40 cm. Kopya {0:0} cm yukarida, yon kutulari orada. Merdiven:", KopyaYukariCm);
                foreach (DonatiAdim adim in adimlar)
                    ed.WriteMessage(" {0}", adim.Yazi);
                ed.WriteMessage("\nTEMELILAVEDENEME: hiza tanilama {0}", HizaLogYolu);
            }
            catch (System.Exception ex)
            {
                if (ex is AcRxException aex)
                    ed.WriteMessage("\nTEMELILAVEDENEME hata: {0} ({1})", aex.Message, aex.ErrorStatus);
                else
                    ed.WriteMessage("\nTEMELILAVEDENEME hata: {0}", ex.Message);
            }
        }

        private static bool GprOku(string st4Path, Editor ed, string komut, out List<GprRadyeDugumAs> nodes, out GprRadyeGenelDonati genel)
        {
            nodes = null;
            genel = null;
            string gpr = Path.ChangeExtension(st4Path, ".GPR");
            if (!File.Exists(gpr)) gpr = Path.ChangeExtension(st4Path, ".gpr");
            if (!File.Exists(gpr))
            {
                ed.WriteMessage("\n{0}: ST4 yaninda GPR yok. Radye sonuclari GPR'ye yazilmis olmali.", komut);
                return false;
            }
            try
            {
                WindowsAnsiEncodings.EnsureRegistered();
                string[] lines = WindowsAnsiEncodings.ReadAllLines(gpr);
                if (!GprRadyeAyir.TryReadMesh(lines, out nodes, out string meshError))
                {
                    ed.WriteMessage("\n{0}: {1}", komut, meshError);
                    return false;
                }
                if (!GprRadyeAyir.TryReadGenel(lines, out genel, out string genelError))
                {
                    ed.WriteMessage("\n{0}: {1}", komut, genelError);
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n{0}: GPR okunamadi: {1}", komut, ex.Message);
                return false;
            }
            return true;
        }

        private static Face[] Yuzler(GprRadyeGenelDonati genel)
        {
            return new[]
            {
                new Face("ALT X", "TEMEL ILAVE X (BEYKENT)", 1, genel.Yazi(1), genel.AsXAlt, n => n.AsXAlt),
                new Face("ALT Y", "TEMEL ILAVE Y (BEYKENT)", 5, genel.Yazi(3), genel.AsYAlt, n => n.AsYAlt),
                new Face("UST X", "TEMEL ILAVE X (BEYKENT)", 1, genel.Yazi(0), genel.AsXUst, n => n.AsXUst),
                new Face("UST Y", "TEMEL ILAVE Y (BEYKENT)", 5, genel.Yazi(2), genel.AsYUst, n => n.AsYUst)
            };
        }

        /// <summary>
        /// TEMELDONATI (Rd. Temel Planı): PDF yerine GPR ısısından ilave zarfları ve lejant yazıları.
        /// ALT X/Y 3. plana, ÜST X/Y 4. plana; ısı haritası ve lejant çizilmez. Ayarlar son TEMELILAVEDENEME formundan.
        /// </summary>
        internal static void RdPlanZarflari(Database db, Editor ed, string st4Path, IList<PlanIdDrawingManager.TemelPlanCopyLayout> copies)
        {
            if (copies == null || copies.Count < 4) return;
            if (!GprOku(st4Path, ed, "TEMELDONATI", out List<GprRadyeDugumAs> nodes, out GprRadyeGenelDonati genel))
                return;
            Face[] faces = Yuzler(genel);
            int[] kopya = { 2, 2, 3, 3 };
            string rad = Path.ChangeExtension(st4Path, ".RAD");
            if (!File.Exists(rad)) rad = Path.ChangeExtension(st4Path, ".rad");
            bool radVar = RadyeEsikPoligon.TryRadElemanlari(rad, nodes, out List<int[]> elemanlar, out string radHata);
            if (!radVar)
                ed.WriteMessage("\nTEMELDONATI: RAD agi kullanilmadi ({0}). Dugum ucgenlemesi.", radHata);
            IList<int[]> ag = radVar ? elemanlar : null;
            List<DonatiAdim> adimlar = Merdiven(st4Path);
            var isaretler = new List<Isaret>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                AnaDonatiYazisi(tr, db, btr, copies[1], genel);
                for (int i = 0; i < faces.Length; i++)
                {
                    PlanIdDrawingManager.TemelPlanCopyLayout copy = copies[kopya[i]];
                    if (copy.TemelGeom == null)
                    {
                        ed.WriteMessage("\nTEMELDONATI: {0}. plan temel zarfi yok, {1} ilave atlandi.", kopya[i] + 1, faces[i].Ad);
                        continue;
                    }
                    CizBantlar(tr, btr, copy, ag, nodes, faces[i], adimlar, ed, isaretler, false);
                }
                tr.Commit();
            }
            CizYonKutulari(db, isaretler, 0.0);
            ed.WriteMessage("\nTEMELDONATI: GPR ilave zarflari 3. (alt) ve 4. (ust) plana cizildi. Hiza tanilama {0}", HizaLogYolu);
        }

        /// <summary>2. plan (ANA DONATI) altına GPR genel donatısı: X alt, Y alt, X üst, Y üst.</summary>
        private static void AnaDonatiYazisi(Transaction tr, Database db, BlockTableRecord btr, PlanIdDrawingManager.TemelPlanCopyLayout copy, GprRadyeGenelDonati genel)
        {
            const string katman = "DONATI YAZISI (BEYKENT)";
            EnsureLayer(tr, db, katman, 3);
            string[] satirlar =
            {
                "TEMEL ANA DONATILARI",
                "X ALT : " + genel.Yazi(1),
                "Y ALT : " + genel.Yazi(3),
                "X UST : " + genel.Yazi(0),
                "Y UST : " + genel.Yazi(2)
            };
            double x = copy.AltYaziX - 150.0;
            for (int i = 0; i < satirlar.Length; i++)
            {
                var t = new DBText
                {
                    TextString = satirlar[i],
                    Height = 20.0,
                    Position = new Point3d(x, copy.AltYaziY - 20.0 - i * 40.0, 0),
                    Layer = katman
                };
                btr.AppendEntity(t);
                tr.AddNewlyCreatedDBObject(t, true);
            }
        }

        private static void CizBantlar(
            Transaction tr,
            BlockTableRecord btr,
            PlanIdDrawingManager.TemelPlanCopyLayout copy,
            IList<int[]> ag,
            List<GprRadyeDugumAs> nodes,
            Face face,
            List<DonatiAdim> adimlar,
            Editor ed,
            List<Isaret> isaretler,
            bool ciz = true)
        {
            bool yatay = face.Ad.EndsWith("X", StringComparison.Ordinal);
            var gorunen = new List<DonatiAdim>();
            for (int k = 0; k < adimlar.Count; k++)
            {
                double taban = face.Sinir + (k == 0 ? 0 : adimlar[k - 1].Alan);
                double ofset = k == 0 ? OfsetM : 0;
                List<Polygon> alan = RadyeEsikPoligon.Hesapla(nodes, ag, face.As, taban, 0, ofset);
                var cizilen = new List<Polygon>();
                var sarkan = new List<Polygon>();
                int n = CizYuz(tr, btr, copy, alan, face.Katman, adimlar[k].Renk, null, cizilen, sarkan, ciz);
                if (n > 0) gorunen.Add(adimlar[k]);
                IsaretEkle(isaretler, cizilen, adimlar[k].Alan, yatay, false, copy.TemelGeom, k, adimlar[k].AralikCm, adimlar[k].Yazi);
                IsaretEkle(isaretler, sarkan, adimlar[k].Alan, yatay, true, copy.TemelGeom, k, adimlar[k].AralikCm, adimlar[k].Yazi);
            }

            DonatiAdim son = adimlar[adimlar.Count - 1];
            string yetmezYazi = son.Yazi + " yetmez";
            List<Polygon> asan = RadyeEsikPoligon.Hesapla(nodes, ag, face.As, face.Sinir + son.Alan);
            var yetmezCizilen = new List<Polygon>();
            var yetmezSarkan = new List<Polygon>();
            int yetmez = CizYuz(tr, btr, copy, asan, face.Katman, YetmezRenk, yetmezYazi, yetmezCizilen, yetmezSarkan, ciz);
            IsaretEkle(isaretler, yetmezCizilen, son.Alan, yatay, false, copy.TemelGeom, adimlar.Count, son.AralikCm, yetmezYazi);
            IsaretEkle(isaretler, yetmezSarkan, son.Alan, yatay, true, copy.TemelGeom, adimlar.Count, son.AralikCm, yetmezYazi);
            if (gorunen.Count == 0 && yetmez == 0)
            {
                ed.WriteMessage("\n  {0}: ilave yok.", face.Ad);
                return;
            }
            if (ciz)
                Lejant(tr, btr, copy, face.Katman, gorunen, yetmez > 0 ? yetmezYazi : null);
            ed.WriteMessage("\n  {0}:", face.Ad);
            foreach (DonatiAdim adim in gorunen)
                ed.WriteMessage(" {0}", adim.Yazi);
            if (yetmez > 0) ed.WriteMessage(" {0}", yetmezYazi);
        }

        private static void IsaretEkle(List<Isaret> isaretler, List<Polygon> poligonlar, double alan, bool yatay, bool sarkan, Geometry temel, int kademe, int aralikCm, string yazi)
        {
            if (poligonlar == null) return;
            foreach (Polygon p in poligonlar)
                isaretler.Add(new Isaret { Sekil = p, Alan = alan, Yatay = yatay, Sarkan = sarkan, Temel = temel, Kademe = kademe, AralikCm = aralikCm, Yazi = yazi });
        }

        private static int CizYuz(
            Transaction tr,
            BlockTableRecord btr,
            PlanIdDrawingManager.TemelPlanCopyLayout copy,
            List<Polygon> alanlar,
            string katman,
            short renk,
            string not,
            List<Polygon> cizilen,
            List<Polygon> sarkan,
            bool ciz = true)
        {
            int n = 0;
            if (alanlar == null) return 0;
            var gf = NtsGeometryServices.Instance.CreateGeometryFactory();
            foreach (Polygon metre in alanlar)
            {
                Polygon cm = MetredenCm(gf, metre, copy.OffsetX, copy.OffsetY);
                if (cm == null || cm.IsEmpty) continue;
                // Temel poligonu dışına ısı ve zarf çizilmez: yalnız temelle kesişen parça kalır (sarkan boş kalır).
                Geometry sekil = TemelIci(cm, copy.TemelGeom);
                if (sekil == null || sekil.IsEmpty) continue;
                int once = cizilen.Count;
                EkleCizilen(sekil, cizilen);
                // ciz=false: yalnız zarf için poligon toplanır, ısı haritası çizilmez.
                n += ciz ? CizGeometri(tr, btr, sekil, katman, renk, not) : cizilen.Count - once;
            }
            return n;
        }

        /// <summary>Konturun temel içindeki parçası. Temel yoksa kontur aynen; kesişim yok/hesaplanamazsa null.</summary>
        private static Geometry TemelIci(Geometry cm, Geometry temel)
        {
            if (cm == null || cm.IsEmpty) return null;
            if (temel == null || temel.IsEmpty) return cm;
            try
            {
                Geometry a = NtsOverlaySafe.PrepareForOverlay(cm);
                Geometry b = NtsOverlaySafe.PrepareForOverlay(temel);
                if (a == null || a.IsEmpty || b == null || b.IsEmpty) return null;
                Geometry kesisim = b.Intersection(a);
                return kesisim == null || kesisim.IsEmpty ? null : kesisim;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static void EkleCizilen(Geometry geom, List<Polygon> hedef)
        {
            if (geom == null || geom.IsEmpty || hedef == null) return;
            if (geom is Polygon poly)
            {
                if (poly.Area >= 200.0) hedef.Add(poly);
                return;
            }
            for (int i = 0; i < geom.NumGeometries; i++)
                EkleCizilen(geom.GetGeometryN(i), hedef);
        }

        private static int CizGeometri(
            Transaction tr,
            BlockTableRecord btr,
            Geometry sekil,
            string katman,
            short renk,
            string not)
        {
            int n = 0;
            if (sekil == null || sekil.IsEmpty) return 0;
            if (sekil is Polygon poly)
            {
                if (CizPoligon(tr, btr, poly, katman, renk, not)) n++;
                return n;
            }
            for (int i = 0; i < sekil.NumGeometries; i++)
                n += CizGeometri(tr, btr, sekil.GetGeometryN(i), katman, renk, not);
            return n;
        }

        private static bool CizPoligon(
            Transaction tr,
            BlockTableRecord btr,
            Polygon poly,
            string katman,
            short renk,
            string not)
        {
            if (poly == null || poly.IsEmpty || poly.Area < 200.0) return false;
            ObjectId dis = HalkaCiz(tr, btr, poly.ExteriorRing.Coordinates, katman, renk);
            if (dis.IsNull) return false;
            var delikler = new List<ObjectId>();
            for (int i = 0; i < poly.NumInteriorRings; i++)
            {
                ObjectId ic = HalkaCiz(tr, btr, poly.GetInteriorRingN(i).Coordinates, katman, renk);
                if (!ic.IsNull) delikler.Add(ic);
            }

            try
            {
                var hatch = new Hatch();
                btr.AppendEntity(hatch);
                tr.AddNewlyCreatedDBObject(hatch, true);
                hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                hatch.Layer = katman;
                hatch.Color = Color.FromColorIndex(ColorMethod.ByAci, renk);
                hatch.Associative = false;
                hatch.Transparency = new Transparency(160);
                hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { dis });
                foreach (ObjectId delik in delikler)
                    hatch.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { delik });
                hatch.EvaluateHatch(true);
            }
            catch (System.Exception)
            {
            }

            if (!string.IsNullOrEmpty(not))
            {
                Point c = poly.Centroid;
                var yazi = new DBText
                {
                    TextString = not,
                    Height = 22.0,
                    Position = new Point3d(c.X, c.Y, 0),
                    Layer = katman,
                    Color = Color.FromColorIndex(ColorMethod.ByAci, renk)
                };
                btr.AppendEntity(yazi);
                tr.AddNewlyCreatedDBObject(yazi, true);
            }
            return true;
        }

        private static void Lejant(
            Transaction tr,
            BlockTableRecord btr,
            PlanIdDrawingManager.TemelPlanCopyLayout copy,
            string katman,
            List<DonatiAdim> adimlar,
            string yetmezYazi)
        {
            double x = copy.Envelope != null ? copy.Envelope.MinX + 80.0 : copy.OffsetX + 80.0;
            double y = copy.Envelope != null ? copy.Envelope.MinY + 80.0 : copy.OffsetY + 80.0;
            var baslik = new DBText
            {
                TextString = "ILAVE",
                Height = 22.0,
                Position = new Point3d(x, y + Math.Max(1, adimlar.Count) * 56.0 + (yetmezYazi == null ? 0 : 56.0), 0),
                Layer = katman,
                Color = Color.FromColorIndex(ColorMethod.ByAci, 7)
            };
            btr.AppendEntity(baslik);
            tr.AddNewlyCreatedDBObject(baslik, true);
            for (int i = 0; i < adimlar.Count; i++)
                LejantSatir(tr, btr, katman, x, y + i * 56.0, adimlar[i].Renk, adimlar[i].Yazi);
            if (yetmezYazi != null)
                LejantSatir(tr, btr, katman, x, y + adimlar.Count * 56.0, YetmezRenk, yetmezYazi);
        }

        private static void LejantSatir(Transaction tr, BlockTableRecord btr, string katman, double x, double y, short renk, string yazi)
        {
            var kutu = new Polyline();
            kutu.AddVertexAt(0, new Point2d(x, y), 0, 0, 0);
            kutu.AddVertexAt(1, new Point2d(x + 70.0, y), 0, 0, 0);
            kutu.AddVertexAt(2, new Point2d(x + 70.0, y + 36.0), 0, 0, 0);
            kutu.AddVertexAt(3, new Point2d(x, y + 36.0), 0, 0, 0);
            kutu.Closed = true;
            kutu.Layer = katman;
            kutu.Color = Color.FromColorIndex(ColorMethod.ByAci, renk);
            btr.AppendEntity(kutu);
            tr.AddNewlyCreatedDBObject(kutu, true);
            try
            {
                var hatch = new Hatch();
                btr.AppendEntity(hatch);
                tr.AddNewlyCreatedDBObject(hatch, true);
                hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                hatch.Layer = katman;
                hatch.Color = Color.FromColorIndex(ColorMethod.ByAci, renk);
                hatch.Associative = false;
                hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { kutu.ObjectId });
                hatch.EvaluateHatch(true);
            }
            catch (System.Exception)
            {
            }
            var t = new DBText
            {
                TextString = yazi,
                Height = 22.0,
                Position = new Point3d(x + 86.0, y + 6.0, 0),
                Layer = katman,
                Color = Color.FromColorIndex(ColorMethod.ByAci, renk)
            };
            btr.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
        }

        private static List<DonatiAdim> Merdiven(string st4Path)
        {
            var caps = new SortedSet<int>();
            string path = Path.ChangeExtension(st4Path, ".BAR");
            if (!File.Exists(path)) path = Path.ChangeExtension(st4Path, ".bar");
            if (File.Exists(path))
            {
                try
                {
                    foreach (string line in File.ReadLines(path).Take(4))
                    {
                        foreach (string tok in line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cap)
                                && cap >= 12 && cap <= 40)
                                caps.Add(cap);
                        }
                    }
                }
                catch (System.Exception)
                {
                }
            }
            if (!caps.Contains(12)) caps.Add(12);
            int max = 12;
            foreach (int cap in caps)
                if (cap > max) max = cap;
            int alt = 0;
            foreach (int cap in caps)
                if (cap < max && cap > alt) alt = cap;
            var ham = new List<int[]>();
            ham.Add(new[] { 12, 30 });
            if (alt > 0 && GprRadyeAyir.AlanCm2Pm(max, 30) >= GprRadyeAyir.AlanCm2Pm(alt, 20))
                ham.Add(new[] { max, 30 });
            foreach (int cap in caps)
                ham.Add(new[] { cap, 20 });
            ham.Add(new[] { max, 15 });
            ham.Add(new[] { max, 10 });
            ham.Sort(delegate (int[] a, int[] b)
            {
                return GprRadyeAyir.AlanCm2Pm(a[0], a[1]).CompareTo(GprRadyeAyir.AlanCm2Pm(b[0], b[1]));
            });
            var liste = new List<DonatiAdim>();
            foreach (int[] p in ham)
            {
                double alan = GprRadyeAyir.AlanCm2Pm(p[0], p[1]);
                if (liste.Count > 0 && alan <= liste[liste.Count - 1].Alan + 0.01) continue;
                liste.Add(new DonatiAdim
                {
                    Alan = alan,
                    AralikCm = p[1],
                    Renk = BantRenk[Math.Min(liste.Count, BantRenk.Length - 1)],
                    Yazi = "ø" + p[0] + "/" + p[1]
                });
            }
            return liste;
        }

        private sealed class DonatiAdim
        {
            public double Alan;
            public int AralikCm;
            public short Renk;
            public string Yazi;
        }

        private sealed class Isaret
        {
            public Polygon Sekil;
            public double Alan;
            public bool Yatay;
            public bool Sarkan;
            public Geometry Temel;
            public int Kademe;
            public int AralikCm;
            /// <summary>Lejant satırı (ör. ø12/20); kutunun sol üstüne yazılır.</summary>
            public string Yazi;
        }

        private static List<ObjectId> ModelKimlikleri(Database db)
        {
            var ids = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in btr)
                    if (id.IsValid) ids.Add(id);
                tr.Commit();
            }
            return ids;
        }

        private static void KopyalaYukari(Database db, ObjectIdCollection kaynak, double dy)
        {
            if (kaynak == null || kaynak.Count == 0) return;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                ObjectId ms = bt[BlockTableRecord.ModelSpace];
                var mapping = new IdMapping();
                db.DeepCloneObjects(kaynak, ms, mapping, false);
                var tasima = Matrix3d.Displacement(new Vector3d(0, dy, 0));
                foreach (IdPair pair in mapping)
                {
                    if (!pair.IsCloned || !pair.IsPrimary) continue;
                    var ent = tr.GetObject(pair.Value, OpenMode.ForWrite, false) as Entity;
                    if (ent == null || ent.IsErased) continue;
                    ent.TransformBy(tasima);
                }
                tr.Commit();
            }
        }

        private static void CizYonKutulari(Database db, List<Isaret> isaretler, double dy)
        {
            if (isaretler == null || isaretler.Count == 0) return;
            var gf = NtsGeometryServices.Instance.CreateGeometryFactory();
            var kaynak = new List<Isaret>();
            var sekiller = new List<Polygon>();
            var yon = new List<bool>();
            foreach (Isaret aday in isaretler)
            {
                if (aday.Sekil == null || aday.Sekil.IsEmpty) continue;
                kaynak.Add(aday);
                sekiller.Add(aday.Sekil);
                yon.Add(aday.Yatay);
            }
            List<List<int>> icIce = TemelIlaveKutuBol.IcIceKumeler(sekiller, yon);
            var haric = new HashSet<Isaret>();
            var gruplar = new List<List<Isaret>>();
            var birlesik = new List<bool>();
            foreach (List<int> kume in icIce)
            {
                var grup = new List<Isaret>();
                foreach (int ix in kume)
                {
                    haric.Add(kaynak[ix]);
                    grup.Add(kaynak[ix]);
                }
                gruplar.Add(grup);
                birlesik.Add(true);
            }
            var kalan = new List<Isaret>();
            foreach (Isaret aday in isaretler)
            {
                if (!haric.Contains(aday)) kalan.Add(aday);
            }
            foreach (List<Isaret> grup in Grupla(kalan))
            {
                gruplar.Add(grup);
                birlesik.Add(false);
            }
            _hizaLog = new System.Text.StringBuilder();
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    KutuKatmanHazirla(tr, db, true);
                    KutuKatmanHazirla(tr, db, false);
                    CakisanGruplariBirlestir(tr, btr, gf, gruplar, birlesik, dy);
                    for (int i = 0; i < gruplar.Count; i++)
                    {
                        Envelope ge = new Envelope();
                        foreach (Isaret uye in gruplar[i]) ge.ExpandToInclude(uye.Sekil.EnvelopeInternal);
                        HizaLog("GRUP {0}: uye={1} yatay={2} birlesik={3} zarf x {4}..{5} y {6}..{7}",
                            i, gruplar[i].Count, gruplar[i][0].Yatay, birlesik[i], ge.MinX, ge.MaxX, ge.MinY, ge.MaxY);
                        GrupCiz(tr, btr, gf, gruplar[i], birlesik[i], dy);
                    }
                    tr.Commit();
                }
            }
            finally
            {
                try
                {
                    File.WriteAllText(HizaLogYolu, _hizaLog.ToString());
                }
                catch (System.Exception)
                {
                }
                _hizaLog = null;
            }
        }

        /// <summary>Tanılama: düşey hiza kararları. Komut her çalışmada üzerine yazar.</summary>
        internal static readonly string HizaLogYolu = Path.Combine(Path.GetTempPath(), "TEMELILAVEDENEME_hiza.log");
        private static System.Text.StringBuilder _hizaLog;

        private static void HizaLog(string bicim, params object[] arg)
        {
            if (_hizaLog == null || _kutuTopla != null) return;
            for (int i = 0; i < arg.Length; i++)
                if (arg[i] is double d) arg[i] = double.IsInfinity(d) ? (d > 0 ? "+inf" : "-inf") : d.ToString("0.0", CultureInfo.InvariantCulture);
            _hizaLog.AppendLine(string.Format(CultureInfo.InvariantCulture, bicim, arg));
        }

        /// <summary>Çizim yerine KutuCiz dikdörtgenleri buraya toplar (dy eklenmeden).</summary>
        private static List<double[]> _kutuTopla;

        private static void GrupCiz(Transaction tr, BlockTableRecord btr, GeometryFactory gf, List<Isaret> grup, bool birlesik, double dy)
        {
            Geometry eski = _kutuSinir;
            List<KutuKayit> eskiBekle = _kutuBekle;
            _kutuSinir = KutuSiniri(grup);
            _kutuBekle = new List<KutuKayit>();
            try
            {
                if (birlesik)
                    CizBirlestirBol(tr, btr, gf, grup, dy);
                else
                    CizSeritler(tr, btr, gf, grup, dy);
                // Grubun kutuları birlikte netleştirilip çizilir: ortak kenar, aralık katı, küçük kaçıklık yok.
                Netlestir(_kutuBekle);
                foreach (KutuKayit k in _kutuBekle)
                    KutuYaz(tr, btr, k, dy);
            }
            finally
            {
                _kutuSinir = eski;
                _kutuBekle = eskiBekle;
            }
        }

        /// <summary>Kutuların son sınırı: temel poligonunun paspay içi (PaspayCek ile aynı çizgi). Kutu bunun dışına çizilmez.</summary>
        private static Geometry _kutuSinir;
        private static Geometry _kutuSinirTemel;
        private static Geometry _kutuSinirOnbellek;

        private static Geometry KutuSiniri(List<Isaret> grup)
        {
            Geometry temel = null;
            foreach (Isaret uye in grup)
                if (uye.Temel != null && !uye.Temel.IsEmpty) { temel = uye.Temel; break; }
            if (temel == null) return null;
            if (ReferenceEquals(temel, _kutuSinirTemel)) return _kutuSinirOnbellek;
            Geometry sinir;
            try
            {
                sinir = NtsOverlaySafe.PrepareForOverlay(temel)?.Buffer(-TemelPaspayCm);
                if (sinir != null && sinir.IsEmpty) sinir = null;
            }
            catch (System.Exception)
            {
                sinir = null;
            }
            _kutuSinirTemel = temel;
            _kutuSinirOnbellek = sinir;
            return sinir;
        }

        /// <summary>
        /// Büyüyen (standart boy, en az genişlik) kutular başka grubun kutusuna girerse gruplar birleşir.
        /// Birleşen grup kesişen zarf gibi tek zarftan dilimlenir; yeni çakışma kalmayana dek tekrarlanır.
        /// </summary>
        private static void CakisanGruplariBirlestir(
            Transaction tr,
            BlockTableRecord btr,
            GeometryFactory gf,
            List<List<Isaret>> gruplar,
            List<bool> birlesik,
            double dy)
        {
            for (int tur = 0; tur < 50 && gruplar.Count > 1; tur++)
            {
                var kutular = new List<List<double[]>>(gruplar.Count);
                var yonler = new List<bool>(gruplar.Count);
                for (int i = 0; i < gruplar.Count; i++)
                {
                    _kutuTopla = new List<double[]>();
                    try
                    {
                        GrupCiz(tr, btr, gf, gruplar[i], birlesik[i], dy);
                        kutular.Add(_kutuTopla);
                    }
                    finally
                    {
                        _kutuTopla = null;
                    }
                    yonler.Add(gruplar[i][0].Yatay);
                }
                List<List<int>> kumeler = TemelIlaveKutuBol.KutuCakisanKumeler(kutular, yonler);
                if (kumeler.Count == 0) return;
                var silinecek = new List<int>();
                foreach (List<int> kume in kumeler)
                {
                    int hedef = kume[0];
                    for (int k = 1; k < kume.Count; k++)
                    {
                        gruplar[hedef].AddRange(gruplar[kume[k]]);
                        silinecek.Add(kume[k]);
                    }
                    birlesik[hedef] = true;
                }
                silinecek.Sort();
                for (int k = silinecek.Count - 1; k >= 0; k--)
                {
                    gruplar.RemoveAt(silinecek[k]);
                    birlesik.RemoveAt(silinecek[k]);
                }
            }
        }

        /// <summary>Kesişen zarfları tek dikdörtgen yapar, lejant kademesine göre doğrultusunda dilimler.</summary>
        private static void CizBirlestirBol(Transaction tr, BlockTableRecord btr, GeometryFactory gf, List<Isaret> grup, double dy)
        {
            var sekiller = new List<Polygon>();
            var kademeler = new List<int>();
            var alanlar = new List<double>();
            var araliklar = new List<int>();
            foreach (Isaret uye in grup)
            {
                if (uye.Sekil == null || uye.Sekil.IsEmpty) continue;
                sekiller.Add(uye.Sekil);
                kademeler.Add(uye.Kademe);
                alanlar.Add(uye.Alan);
                araliklar.Add(uye.AralikCm);
            }
            bool yataySerit = grup[0].Yatay;
            var kutuKademe = new List<int>();
            List<double[]> kutular = TemelIlaveKutuBol.ZarfDilimle(sekiller, kademeler, yataySerit, alanlar, araliklar, kutuKademe);
            if (kutular == null || kutular.Count == 0)
            {
                CizSeritler(tr, btr, gf, grup, dy);
                return;
            }

            Geometry temel = null;
            foreach (Isaret uye in grup)
            {
                if (uye.Temel != null && !uye.Temel.IsEmpty) { temel = uye.Temel; break; }
            }
            var govde = new List<Coordinate[]>();
            var delik = new List<Coordinate[]>();
            List<SinirKenar> kenarlar = null;
            List<SinirKenar> paspay = PaspayKenarlari(temel);
            if (temel != null)
            {
                TemelHalkalari(temel, govde, delik);
                var ham = new List<SinirKenar>();
                foreach (Coordinate[] halka in govde)
                    KenarlariEkle(halka, govde, delik, yataySerit, ham);
                kenarlar = BirlestirKenar(ham);
            }

            var dilimler = new List<double[]>();
            foreach (double[] k in kutular)
                dilimler.Add(yataySerit ? new[] { k[1], k[3] } : new[] { k[0], k[2] });
            double grupB0 = yataySerit ? kutular[0][0] : kutular[0][1];
            double grupB1 = yataySerit ? kutular[0][2] : kutular[0][3];
            DuseyHizaKur(grup, yataySerit, dilimler, grupB0, grupB1, paspay);

            for (int ki = 0; ki < kutular.Count; ki++)
            {
                double[] k = kutular[ki];
                _kutuYazi = KademeYazisi(grup, ki < kutuKademe.Count ? kutuKademe[ki] : int.MinValue);
                double minB;
                double maxB;
                double a0;
                double a1;
                if (yataySerit)
                {
                    minB = k[0];
                    maxB = k[2];
                    a0 = k[1];
                    a1 = k[3];
                }
                else
                {
                    a0 = k[0];
                    a1 = k[2];
                    minB = k[1];
                    maxB = k[3];
                }
                if (kenarlar == null || kenarlar.Count == 0 ||
                    !CizKirpilmis(tr, btr, gf, grup, govde, delik, kenarlar, yataySerit, a0, a1, minB, maxB, dy, paspay))
                    KutuCizBoy(tr, btr, grup, yataySerit, minB, maxB, a0, a1, dy, paspay);
            }
            _kutuYazi = null;
        }

        /// <summary>Çizilen kutunun sol üstüne yazılacak lejant satırı; dilim döngüsü ayarlar.</summary>
        private static string _kutuYazi;

        private static string KademeYazisi(List<Isaret> grup, int kademe)
        {
            if (grup == null || kademe == int.MinValue) return null;
            foreach (Isaret uye in grup)
                if (uye.Kademe == kademe && !string.IsNullOrEmpty(uye.Yazi)) return uye.Yazi;
            return null;
        }

        private static List<List<Isaret>> Grupla(List<Isaret> isaretler)
        {
            var gruplar = new List<List<Isaret>>();
            foreach (Isaret aday in isaretler)
            {
                if (aday.Sekil == null || aday.Sekil.IsEmpty) continue;
                List<Isaret> bulunan = null;
                foreach (List<Isaret> grup in gruplar)
                {
                    foreach (Isaret uye in grup)
                    {
                        if (uye.Yatay != aday.Yatay || uye.Sarkan != aday.Sarkan) continue;
                        try
                        {
                            if (uye.Sekil.Intersects(aday.Sekil) || uye.Sekil.Distance(aday.Sekil) < 5.0)
                            {
                                bulunan = grup;
                                break;
                            }
                        }
                        catch (System.Exception)
                        {
                        }
                    }
                    if (bulunan != null) break;
                }
                if (bulunan == null)
                {
                    bulunan = new List<Isaret>();
                    gruplar.Add(bulunan);
                }
                bulunan.Add(aday);
            }
            return gruplar;
        }

        /// <summary>
        /// Y donatısında düşey şerit: X boyunca, o düşeydeki en yüksek adıma göre.
        /// X donatısında aynı kural yatay şerit olarak.
        /// </summary>
        private static void CizSeritler(Transaction tr, BlockTableRecord btr, GeometryFactory gf, List<Isaret> grup, double dy)
        {
            if (grup.Count == 0) return;
            bool yataySerit = grup[0].Yatay;
            double minA = double.MaxValue, maxA = double.MinValue, minB = double.MaxValue, maxB = double.MinValue;
            var kesimler = new List<double>();
            foreach (Isaret uye in grup)
            {
                Envelope env = uye.Sekil.EnvelopeInternal;
                double a0 = yataySerit ? env.MinY : env.MinX;
                double a1 = yataySerit ? env.MaxY : env.MaxX;
                double b0 = yataySerit ? env.MinX : env.MinY;
                double b1 = yataySerit ? env.MaxX : env.MaxY;
                if (a0 < minA) minA = a0;
                if (a1 > maxA) maxA = a1;
                if (b0 < minB) minB = b0;
                if (b1 > maxB) maxB = b1;
                kesimler.Add(a0);
                kesimler.Add(a1);
                foreach (Coordinate c in uye.Sekil.ExteriorRing.Coordinates)
                    kesimler.Add(yataySerit ? c.Y : c.X);
            }
            if (maxA - minA < 20.0 || maxB - minB < 20.0) return;
            kesimler.Sort();
            var sinir = new List<double>();
            foreach (double ham in kesimler)
            {
                if (ham < minA - 1.0 || ham > maxA + 1.0) continue;
                double deger = ham < minA ? minA : (ham > maxA ? maxA : ham);
                if (sinir.Count == 0 || deger - sinir[sinir.Count - 1] > 5.0)
                    sinir.Add(deger);
            }
            if (sinir.Count == 0 || sinir[0] > minA + 5.0) sinir.Insert(0, minA);
            else sinir[0] = minA;
            if (sinir[sinir.Count - 1] < maxA - 5.0) sinir.Add(maxA);
            else sinir[sinir.Count - 1] = maxA;

            var hamDilim = new List<TemelIlaveKutuBol.LejantDilim>();
            for (int i = 0; i < sinir.Count - 1; i++)
            {
                double a0 = sinir[i];
                double a1 = sinir[i + 1];
                if (a1 - a0 < 5.0) continue;
                double orta = (a0 + a1) * 0.5;
                int kademe = int.MinValue;
                double alan = -1;
                int aralik = 0;
                bool degdi = false;
                LineString cizgi = yataySerit
                    ? gf.CreateLineString(new[] { new Coordinate(minB - 20.0, orta), new Coordinate(maxB + 20.0, orta) })
                    : gf.CreateLineString(new[] { new Coordinate(orta, minB - 20.0), new Coordinate(orta, maxB + 20.0) });
                foreach (Isaret uye in grup)
                {
                    try
                    {
                        if (!uye.Sekil.Intersects(cizgi)) continue;
                    }
                    catch (System.Exception)
                    {
                        continue;
                    }
                    degdi = true;
                    if (uye.Kademe > kademe || (uye.Kademe == kademe && uye.Alan > alan))
                    {
                        kademe = uye.Kademe;
                        alan = uye.Alan;
                        aralik = uye.AralikCm;
                    }
                }
                if (!degdi) continue;
                if (hamDilim.Count > 0 && hamDilim[hamDilim.Count - 1].Kademe == kademe)
                    hamDilim[hamDilim.Count - 1].A1 = a1;
                else
                    hamDilim.Add(new TemelIlaveKutuBol.LejantDilim { A0 = a0, A1 = a1, Kademe = kademe, Alan = alan, AralikCm = aralik });
            }
            TemelIlaveKutuBol.LejantGenislet(hamDilim);
            var seritler = new List<double[]>();
            foreach (TemelIlaveKutuBol.LejantDilim dilim in hamDilim)
                seritler.Add(new[] { dilim.A0, dilim.A1, dilim.Alan, dilim.Kademe });

            Geometry temel = null;
            foreach (Isaret uye in grup)
            {
                if (uye.Temel != null && !uye.Temel.IsEmpty) { temel = uye.Temel; break; }
            }
            var govde = new List<Coordinate[]>();
            var delik = new List<Coordinate[]>();
            List<SinirKenar> kenarlar = null;
            List<SinirKenar> paspay = PaspayKenarlari(temel);
            if (temel != null)
            {
                TemelHalkalari(temel, govde, delik);
                var ham = new List<SinirKenar>();
                foreach (Coordinate[] halka in govde)
                    KenarlariEkle(halka, govde, delik, yataySerit, ham);
                kenarlar = BirlestirKenar(ham);
            }

            var seritDilim = new List<double[]>();
            foreach (double[] serit in seritler)
                seritDilim.Add(new[] { serit[0], serit[1] });
            DuseyHizaKur(grup, yataySerit, seritDilim, minB, maxB, paspay);

            foreach (double[] serit in seritler)
            {
                _kutuYazi = KademeYazisi(grup, (int)serit[3]);
                if (kenarlar == null || kenarlar.Count == 0 ||
                    !CizKirpilmis(tr, btr, gf, grup, govde, delik, kenarlar, yataySerit, serit[0], serit[1], minB, maxB, dy, paspay))
                    KutuCizBoy(tr, btr, grup, yataySerit, minB, maxB, serit[0], serit[1], dy, paspay);
            }
            _kutuYazi = null;
        }

        /// <summary>
        /// Şerit temel sınırını aşıyorsa donatı yönünde kesilir.
        /// Sınırın dışında kalan boş dikdörtgen atılır; kontur kendi aralığında kalır.
        /// </summary>
        private static bool CizKirpilmis(
            Transaction tr,
            BlockTableRecord btr,
            GeometryFactory gf,
            List<Isaret> grup,
            List<Coordinate[]> govde,
            List<Coordinate[]> delik,
            List<SinirKenar> kenarlar,
            bool yataySerit,
            double a0,
            double a1,
            double minB,
            double maxB,
            double dy,
            List<SinirKenar> paspay)
        {
            SinirKenar sec = null;
            double enDerin = 0;
            foreach (SinirKenar k in kenarlar)
            {
                double ov0 = Math.Max(k.A0, minB);
                double ov1 = Math.Min(k.A1, maxB);
                if (ov1 - ov0 < 40.0) continue;
                double derin = k.DisNegatif ? k.B - a0 : a1 - k.B;
                if (derin < 15.0) continue;
                double aDis = k.DisNegatif ? Math.Min(a1 - 5.0, k.B - 15.0) : Math.Max(a0 + 5.0, k.B + 15.0);
                if (aDis < a0) aDis = a0 + 5.0;
                if (aDis > a1) aDis = a1 - 5.0;
                if (aDis <= a0 || aDis >= a1) continue;
                if (!TarafDisarida(govde, delik, yataySerit, minB, maxB, aDis)) continue;
                if (derin <= enDerin) continue;
                enDerin = derin;
                sec = k;
            }
            if (sec == null) return false;

            double kes = sec.B;
            if (kes < a0) kes = a0;
            if (kes > a1) kes = a1;
            double ic0 = sec.DisNegatif ? kes : a0;
            double ic1 = sec.DisNegatif ? a1 : kes;
            double birles = kes;
            double icB0 = minB;
            double icB1 = maxB;
            if (ic1 - ic0 >= 5.0)
            {
                double x0, y0, x1, y1;
                if (yataySerit)
                {
                    x0 = minB;
                    x1 = maxB;
                    y0 = ic0;
                    y1 = ic1;
                }
                else
                {
                    x0 = ic0;
                    x1 = ic1;
                    y0 = minB;
                    y1 = maxB;
                }
                PaspayCek(paspay, ref x0, ref y0, ref x1, ref y1);
                if (yataySerit)
                {
                    birles = sec.DisNegatif ? y0 : y1;
                    ic0 = y0;
                    ic1 = y1;
                    icB0 = x0;
                    icB1 = x1;
                }
                else
                {
                    birles = sec.DisNegatif ? x0 : x1;
                    ic0 = x0;
                    ic1 = x1;
                    icB0 = y0;
                    icB1 = y1;
                }
                double cizB0 = icB0;
                double cizB1 = icB1;
                BYonUydur(grup, yataySerit, ic0, ic1, paspay, ref cizB0, ref cizB1);
                if (ic1 - ic0 >= 5.0 && cizB1 - cizB0 >= 5.0)
                    KutuCiz(tr, btr, yataySerit, cizB0, cizB1, ic0, ic1, dy, paspay);
            }
            if (sec.DisNegatif)
            {
                if (birles < kes) birles = kes;
                if (birles > kes + TemelPaspayCm) birles = kes + TemelPaspayCm;
            }
            else
            {
                if (birles > kes) birles = kes;
                if (birles < kes - TemelPaspayCm) birles = kes - TemelPaspayCm;
            }
            if (sec.DisNegatif)
                CizDisAralik(tr, btr, gf, grup, yataySerit, a0, kes, minB, maxB, dy, paspay, kes, birles);
            else
                CizDisAralik(tr, btr, gf, grup, yataySerit, kes, a1, minB, maxB, dy, paspay, kes, birles);
            return true;
        }

        private static bool TarafDisarida(
            List<Coordinate[]> govde,
            List<Coordinate[]> delik,
            bool yataySerit,
            double minB,
            double maxB,
            double aDis)
        {
            int n = 0;
            int dis = 0;
            for (double t = minB + 15.0; t < maxB - 5.0; t += 40.0)
            {
                n++;
                double x = yataySerit ? t : aDis;
                double y = yataySerit ? aDis : t;
                if (!GovdeIcinde(govde, delik, x, y)) dis++;
            }
            return n > 0 && dis >= 2 && dis * 5 >= n;
        }

        /// <summary>
        /// Temel dışında kalan aralıkta kutu yalnız konturun kendi boyuna çizilir.
        /// Kenara dayanan parça, içerideki kutunun paspay çizgisine kadar uzar.
        /// </summary>
        private static void CizDisAralik(
            Transaction tr,
            BlockTableRecord btr,
            GeometryFactory gf,
            List<Isaret> grup,
            bool yataySerit,
            double a0,
            double a1,
            double minB,
            double maxB,
            double dy,
            List<SinirKenar> paspay,
            double temas = double.NaN,
            double birles = double.NaN)
        {
            double ek = !double.IsNaN(temas) && !double.IsNaN(birles) ? Math.Abs(birles - temas) : 0.0;
            if (a1 - a0 + ek < 8.0) return;
            const double adim = 20.0;
            var kosular = new List<double[]>();
            for (double t = a0 + adim * 0.5; t < a1; t += adim)
            {
                LineString cizgi = yataySerit
                    ? gf.CreateLineString(new[] { new Coordinate(minB - 5.0, t), new Coordinate(maxB + 5.0, t) })
                    : gf.CreateLineString(new[] { new Coordinate(t, minB - 5.0), new Coordinate(t, maxB + 5.0) });
                foreach (Isaret uye in grup)
                {
                    Geometry hit;
                    try { hit = uye.Sekil.Intersection(cizgi); }
                    catch (System.Exception) { continue; }
                    if (hit == null || hit.IsEmpty) continue;
                    for (int i = 0; i < hit.NumGeometries; i++)
                    {
                        Envelope he = hit.GetGeometryN(i).EnvelopeInternal;
                        double b0 = yataySerit ? he.MinX : he.MinY;
                        double b1 = yataySerit ? he.MaxX : he.MaxY;
                        if (b1 - b0 < 5.0) continue;
                        double bas = t - adim * 0.5;
                        if (kosular.Count == 0 || bas < a0 + adim) bas = a0;
                        double bit = Math.Min(a1, t + adim * 0.5);
                        int bulunan = -1;
                        for (int k = 0; k < kosular.Count; k++)
                        {
                            double[] r = kosular[k];
                            if (bit < r[0] - 1.0 || bas > r[1] + adim) continue;
                            if (Math.Abs(r[2] - b0) > 25.0 || Math.Abs(r[3] - b1) > 25.0) continue;
                            bulunan = k;
                            break;
                        }
                        if (bulunan < 0)
                            kosular.Add(new[] { bas, bit, b0, b1 });
                        else
                        {
                            double[] r = kosular[bulunan];
                            if (bas < r[0]) r[0] = bas;
                            if (bit > r[1]) r[1] = bit;
                            if (b0 < r[2]) r[2] = b0;
                            if (b1 > r[3]) r[3] = b1;
                        }
                    }
                }
            }
            foreach (double[] r in kosular)
            {
                if (!double.IsNaN(temas) && !double.IsNaN(birles))
                {
                    if (birles > temas + 0.1 && r[1] >= temas - 21.0 && r[1] <= temas + 1.0)
                        r[1] = birles;
                    else if (birles < temas - 0.1 && r[0] <= temas + 21.0 && r[0] >= temas - 1.0)
                        r[0] = birles;
                }
                if (r[1] - r[0] < 8.0 || r[3] - r[2] < 8.0) continue;
                KutuCizBoy(tr, btr, grup, yataySerit, r[2], r[3], r[0], r[1], dy, paspay);
            }
        }

        /// <summary>
        /// Temel sınırı dışında kalan köşeleri donatı yönünde ayırır.
        /// X: yatay temel kenarı. Y: düşey temel kenarı. Ana şerit tam ölçüde kalır.
        /// </summary>
        private static bool TryKesTemelDisi(GeometryFactory gf, List<Isaret> grup, out List<Isaret> ic, out List<Isaret> dis)
        {
            ic = null;
            dis = null;
            Geometry temel = null;
            foreach (Isaret uye in grup)
            {
                if (uye.Temel != null && !uye.Temel.IsEmpty) { temel = uye.Temel; break; }
            }
            if (temel == null || grup.Count == 0) return false;

            var govde = new List<Coordinate[]>();
            var delik = new List<Coordinate[]>();
            TemelHalkalari(temel, govde, delik);
            if (govde.Count == 0) return false;

            bool yatay = grup[0].Yatay;
            var hamKenar = new List<SinirKenar>();
            foreach (Coordinate[] halka in govde)
                KenarlariEkle(halka, govde, delik, yatay, hamKenar);
            List<SinirKenar> kenarlar = BirlestirKenar(hamKenar);
            if (kenarlar.Count == 0) return false;

            var kutular = new List<Polygon>();
            foreach (Isaret uye in grup)
            {
                if (uye.Sekil == null || uye.Sekil.IsEmpty) continue;
                Coordinate[] kontur = uye.Sekil.ExteriorRing.Coordinates;
                int disNokta = 0;
                var yakin = new Dictionary<int, List<Coordinate>>();
                for (int i = 0; i < kontur.Length; i++)
                {
                    Coordinate c = kontur[i];
                    if (GovdeIcinde(govde, delik, c.X, c.Y)) continue;
                    disNokta++;
                    int kenar = EnYakinKenar(kenarlar, yatay, c);
                    if (kenar < 0) continue;
                    if (!yakin.TryGetValue(kenar, out List<Coordinate> liste))
                    {
                        liste = new List<Coordinate>();
                        yakin[kenar] = liste;
                    }
                    liste.Add(c);
                }
                if (disNokta == 0 || disNokta > kontur.Length * 0.65) continue;
                foreach (KeyValuePair<int, List<Coordinate>> cift in yakin)
                {
                    Polygon kutu = DisKutu(gf, kenarlar[cift.Key], cift.Value, yatay);
                    if (kutu != null) kutular.Add(kutu);
                }
            }
            if (kutular.Count == 0) return false;

            ic = new List<Isaret>();
            dis = new List<Isaret>();
            bool kesildi = false;
            foreach (Isaret uye in grup)
            {
                Geometry kalan = uye.Sekil;
                bool bu = false;
                foreach (Polygon kutu in kutular)
                {
                    Geometry parca;
                    Geometry sonra;
                    try
                    {
                        parca = kalan.Intersection(kutu);
                        sonra = kalan.Difference(kutu);
                    }
                    catch (System.Exception)
                    {
                        continue;
                    }
                    if (parca == null || parca.IsEmpty || parca.Area < 200.0) continue;
                    if (sonra == null || sonra.IsEmpty || sonra.Area < 200.0) continue;
                    if (parca.Area > uye.Sekil.Area * 0.65) continue;
                    IsaretEkleGeom(dis, parca, uye);
                    kalan = sonra;
                    bu = true;
                }
                if (!bu)
                    ic.Add(uye);
                else
                {
                    kesildi = true;
                    IsaretEkleGeom(ic, kalan, uye);
                }
            }
            return kesildi && dis.Count > 0;
        }

        /// <summary>Dış parçayı donatı yönünde, yalnız kendi genişliğinde kutular.</summary>
        private static void CizDisYerel(Transaction tr, BlockTableRecord btr, GeometryFactory gf, List<Isaret> dis, double dy)
        {
            bool yataySerit = dis[0].Yatay;
            const double adim = 20.0;
            foreach (Isaret uye in dis)
            {
                if (uye.Sekil == null || uye.Sekil.IsEmpty) continue;
                Envelope env = uye.Sekil.EnvelopeInternal;
                double minA = yataySerit ? env.MinY : env.MinX;
                double maxA = yataySerit ? env.MaxY : env.MaxX;
                double minD = yataySerit ? env.MinX : env.MinY;
                double maxD = yataySerit ? env.MaxX : env.MaxY;
                if (maxA - minA < 5.0 || maxD - minD < 5.0) continue;

                var kosular = new List<double[]>();
                for (double t = minA + adim * 0.5; t < maxA; t += adim)
                {
                    LineString cizgi = yataySerit
                        ? gf.CreateLineString(new[] { new Coordinate(minD - 5.0, t), new Coordinate(maxD + 5.0, t) })
                        : gf.CreateLineString(new[] { new Coordinate(t, minD - 5.0), new Coordinate(t, maxD + 5.0) });
                    Geometry hit;
                    try { hit = uye.Sekil.Intersection(cizgi); }
                    catch (System.Exception) { break; }
                    if (hit == null || hit.IsEmpty) continue;
                    for (int i = 0; i < hit.NumGeometries; i++)
                    {
                        Envelope he = hit.GetGeometryN(i).EnvelopeInternal;
                        double b0 = yataySerit ? he.MinX : he.MinY;
                        double b1 = yataySerit ? he.MaxX : he.MaxY;
                        if (b1 - b0 < 5.0) continue;
                        double a0 = t - adim * 0.5;
                        double a1 = Math.Min(maxA, t + adim * 0.5);
                        int bulunan = -1;
                        for (int k = 0; k < kosular.Count; k++)
                        {
                            double[] r = kosular[k];
                            if (Math.Abs(r[1] - a0) > adim + 1.0) continue;
                            if (Math.Abs(r[2] - b0) > 25.0 || Math.Abs(r[3] - b1) > 25.0) continue;
                            bulunan = k;
                            break;
                        }
                        if (bulunan < 0)
                            kosular.Add(new[] { a0, a1, b0, b1 });
                        else
                        {
                            double[] r = kosular[bulunan];
                            r[1] = a1;
                            if (b0 < r[2]) r[2] = b0;
                            if (b1 > r[3]) r[3] = b1;
                        }
                    }
                }
                foreach (double[] r in kosular)
                {
                    if (r[1] - r[0] < 5.0 || r[3] - r[2] < 5.0) continue;
                    KutuCiz(tr, btr, yataySerit, r[2], r[3], r[0], r[1], dy);
                }
            }
        }

        private sealed class SinirKenar
        {
            public double A0;
            public double A1;
            public double B;
            public bool DisNegatif;
            public bool Yatay;
        }

        private static void TemelHalkalari(Geometry g, List<Coordinate[]> govde, List<Coordinate[]> delik)
        {
            if (g == null || g.IsEmpty) return;
            if (g is Polygon p)
            {
                govde.Add(p.ExteriorRing.Coordinates);
                for (int i = 0; i < p.NumInteriorRings; i++)
                    delik.Add(p.GetInteriorRingN(i).Coordinates);
                return;
            }
            for (int i = 0; i < g.NumGeometries; i++)
                TemelHalkalari(g.GetGeometryN(i), govde, delik);
        }

        private static void KenarlariEkle(Coordinate[] halka, List<Coordinate[]> govde, List<Coordinate[]> delik, bool yatay, List<SinirKenar> hedef)
        {
            if (halka == null || halka.Length < 2) return;
            for (int i = 0; i < halka.Length - 1; i++)
            {
                double x0 = halka[i].X, y0 = halka[i].Y, x1 = halka[i + 1].X, y1 = halka[i + 1].Y;
                double da = yatay ? x1 - x0 : y1 - y0;
                double db = yatay ? y1 - y0 : x1 - x0;
                if (Math.Abs(da) < 5.0 || Math.Abs(db) > 2.0) continue;
                double a0 = yatay ? Math.Min(x0, x1) : Math.Min(y0, y1);
                double a1 = yatay ? Math.Max(x0, x1) : Math.Max(y0, y1);
                double b = yatay ? (y0 + y1) * 0.5 : (x0 + x1) * 0.5;
                double orta = (a0 + a1) * 0.5;
                double px = yatay ? orta : b;
                double py = yatay ? b : orta;
                bool arti = yatay
                    ? GovdeIcinde(govde, delik, px, py + 12.0)
                    : GovdeIcinde(govde, delik, px + 12.0, py);
                bool eksi = yatay
                    ? GovdeIcinde(govde, delik, px, py - 12.0)
                    : GovdeIcinde(govde, delik, px - 12.0, py);
                if (arti == eksi) continue;
                hedef.Add(new SinirKenar { A0 = a0, A1 = a1, B = b, DisNegatif = arti && !eksi, Yatay = yatay });
            }
        }

        private static List<SinirKenar> PaspayKenarlari(Geometry temel)
        {
            if (temel == null || temel.IsEmpty) return null;
            var govde = new List<Coordinate[]>();
            var delik = new List<Coordinate[]>();
            TemelHalkalari(temel, govde, delik);
            var ham = new List<SinirKenar>();
            foreach (Coordinate[] halka in govde)
            {
                KenarlariEkle(halka, govde, delik, true, ham);
                KenarlariEkle(halka, govde, delik, false, ham);
            }
            foreach (Coordinate[] halka in delik)
            {
                KenarlariEkle(halka, govde, delik, true, ham);
                KenarlariEkle(halka, govde, delik, false, ham);
            }
            return BirlestirKenar(ham, 15.0);
        }

        /// <summary>Zarf kenarını temel sınırının 5 cm içine çeker.</summary>
        private static void PaspayCek(List<SinirKenar> kenarlar, ref double x0, ref double y0, ref double x1, ref double y1)
        {
            if (kenarlar == null) return;
            foreach (SinirKenar k in kenarlar)
            {
                double ic = k.B + (k.DisNegatif ? TemelPaspayCm : -TemelPaspayCm);
                if (k.Yatay)
                {
                    if (Math.Min(k.A1, x1) - Math.Max(k.A0, x0) < 15.0) continue;
                    if (k.DisNegatif)
                    {
                        if (y0 < ic && y1 > ic + TemelPaspayCm) y0 = ic;
                    }
                    else if (y1 > ic && y0 < ic - TemelPaspayCm)
                        y1 = ic;
                }
                else
                {
                    if (Math.Min(k.A1, y1) - Math.Max(k.A0, y0) < 15.0) continue;
                    if (k.DisNegatif)
                    {
                        if (x0 < ic && x1 > ic + TemelPaspayCm) x0 = ic;
                    }
                    else if (x1 > ic && x0 < ic - TemelPaspayCm)
                        x1 = ic;
                }
            }
        }

        private static List<SinirKenar> BirlestirKenar(List<SinirKenar> ham, double minBoy = 30.0)
        {
            var sonuc = new List<SinirKenar>();
            if (ham == null || ham.Count == 0) return sonuc;
            ham.Sort(delegate (SinirKenar a, SinirKenar b)
            {
                int yon = a.Yatay.CompareTo(b.Yatay);
                if (yon != 0) return yon;
                int y = a.B.CompareTo(b.B);
                if (y != 0) return y;
                if (a.DisNegatif != b.DisNegatif) return a.DisNegatif ? -1 : 1;
                return a.A0.CompareTo(b.A0);
            });
            foreach (SinirKenar k in ham)
            {
                SinirKenar son = sonuc.Count > 0 ? sonuc[sonuc.Count - 1] : null;
                if (son != null && son.Yatay == k.Yatay && son.DisNegatif == k.DisNegatif && Math.Abs(son.B - k.B) < 3.0 && k.A0 <= son.A1 + 8.0)
                {
                    if (k.A1 > son.A1) son.A1 = k.A1;
                    continue;
                }
                sonuc.Add(new SinirKenar { A0 = k.A0, A1 = k.A1, B = k.B, DisNegatif = k.DisNegatif, Yatay = k.Yatay });
            }
            sonuc.RemoveAll(delegate (SinirKenar k) { return k.A1 - k.A0 < minBoy; });
            return sonuc;
        }

        private static int EnYakinKenar(List<SinirKenar> kenarlar, bool yatay, Coordinate c)
        {
            double paralel = yatay ? c.X : c.Y;
            double dik = yatay ? c.Y : c.X;
            int bulunan = -1;
            double enIyi = double.MaxValue;
            for (int i = 0; i < kenarlar.Count; i++)
            {
                SinirKenar k = kenarlar[i];
                if (paralel < k.A0 - 50.0 || paralel > k.A1 + 50.0) continue;
                double derin = k.DisNegatif ? k.B - dik : dik - k.B;
                if (derin < 12.0 || derin >= enIyi) continue;
                bulunan = i;
                enIyi = derin;
            }
            return bulunan;
        }

        private static Polygon DisKutu(GeometryFactory gf, SinirKenar kenar, List<Coordinate> noktalar, bool yatay)
        {
            double a0 = double.MaxValue, a1 = double.MinValue, disUcu = kenar.B;
            foreach (Coordinate c in noktalar)
            {
                double paralel = yatay ? c.X : c.Y;
                double dik = yatay ? c.Y : c.X;
                if (paralel < a0) a0 = paralel;
                if (paralel > a1) a1 = paralel;
                if (kenar.DisNegatif)
                {
                    if (dik < disUcu) disUcu = dik;
                }
                else if (dik > disUcu)
                    disUcu = dik;
            }
            if (a1 - a0 < 15.0 || Math.Abs(disUcu - kenar.B) < 12.0) return null;
            double b0 = Math.Min(kenar.B, disUcu);
            double b1 = Math.Max(kenar.B, disUcu);
            try
            {
                return yatay
                    ? KutuPoligon(gf, a0 - 2.0, b0, a1 + 2.0, b1)
                    : KutuPoligon(gf, b0, a0 - 2.0, b1, a1 + 2.0);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static bool GovdeIcinde(List<Coordinate[]> govde, List<Coordinate[]> delik, double x, double y)
        {
            bool ic = false;
            foreach (Coordinate[] h in govde)
            {
                if (!NoktaHalkada(h, x, y)) continue;
                ic = true;
                break;
            }
            if (!ic) return false;
            foreach (Coordinate[] h in delik)
                if (NoktaHalkada(h, x, y)) return false;
            return true;
        }

        private static bool NoktaHalkada(Coordinate[] h, double x, double y)
        {
            if (h == null || h.Length < 4) return false;
            bool ic = false;
            for (int i = 0, j = h.Length - 1; i < h.Length; j = i++)
            {
                double yi = h[i].Y, yj = h[j].Y;
                if ((yi > y) == (yj > y)) continue;
                double xk = (h[j].X - h[i].X) * (y - yi) / (yj - yi) + h[i].X;
                if (x < xk) ic = !ic;
            }
            return ic;
        }

        private static void IsaretEkleGeom(List<Isaret> hedef, Geometry geom, Isaret kaynak)
        {
            if (geom == null || geom.IsEmpty) return;
            if (geom is Polygon p)
            {
                if (p.Area >= 200.0)
                    hedef.Add(new Isaret { Sekil = p, Alan = kaynak.Alan, Yatay = kaynak.Yatay, Sarkan = kaynak.Sarkan, Temel = kaynak.Temel, Kademe = kaynak.Kademe, AralikCm = kaynak.AralikCm, Yazi = kaynak.Yazi });
                return;
            }
            for (int i = 0; i < geom.NumGeometries; i++)
                IsaretEkleGeom(hedef, geom.GetGeometryN(i), kaynak);
        }

        private static Polygon KutuPoligon(GeometryFactory gf, double x0, double y0, double x1, double y1)
        {
            if (x1 < x0) { double t = x0; x0 = x1; x1 = t; }
            if (y1 < y0) { double t = y0; y0 = y1; y1 = t; }
            return gf.CreatePolygon(new[]
            {
                new Coordinate(x0, y0),
                new Coordinate(x1, y0),
                new Coordinate(x1, y1),
                new Coordinate(x0, y1),
                new Coordinate(x0, y0)
            });
        }

        /// <summary>
        /// Donatı boyunu standart ölçüye çeker. Isı konturunun içine ve paspay şeridine girmez.
        /// Uymayan boy olduğu gibi kalır.
        /// </summary>
        private static void BYonUydur(
            List<Isaret> grup,
            bool yataySerit,
            double a0,
            double a1,
            List<SinirKenar> paspay,
            ref double b0,
            ref double b1)
        {
            if (!IsiBAraligi(grup, yataySerit, a0, a1, out double isi0, out double isi1))
            {
                DuseyHizaUygula(ref b0, ref b1, false, 0, 0, double.NegativeInfinity, double.PositiveInfinity);
                return;
            }
            PaspayBSiniri(paspay, yataySerit, a0, a1, isi0, isi1, out double sinir0, out double sinir1);
            // Paspayın ötesindeki ısı kutuya girmez; hiza kontrolü kırpılmış ısıyla yapılır.
            double kIsi0 = Math.Max(isi0, sinir0);
            double kIsi1 = Math.Min(isi1, sinir1);
            if (!TemelIlaveKutuBol.StandartBoyUydur(isi0, isi1, sinir0, sinir1, out double u0, out double u1))
            {
                if (_duseyHiza.Var)
                    HizaLog("    STANDART YOK a {0}..{1} isi {2}..{3} sinir {4}..{5} kutu {6}..{7}", a0, a1, isi0, isi1, sinir0, sinir1, b0, b1);
                DuseyHizaUygula(ref b0, ref b1, true, kIsi0, kIsi1, sinir0, sinir1);
                return;
            }
            b0 = u0;
            b1 = u1;
            DuseyHizaUygula(ref b0, ref b1, true, kIsi0, kIsi1, sinir0, sinir1);
        }

        private struct DuseyHiza
        {
            public bool Var;
            public double Y0;
            public double Y1;
        }

        private static DuseyHiza _duseyHiza;

        private static void DuseyHizaKur(
            List<Isaret> grup,
            bool yataySerit,
            List<double[]> dilimler,
            double minB,
            double maxB,
            List<SinirKenar> paspay)
        {
            _duseyHiza = default;
            if (grup == null || dilimler == null || dilimler.Count == 0) return;
            Isaret en = null;
            foreach (Isaret uye in grup)
            {
                if (uye == null || uye.Sekil == null || uye.Sekil.IsEmpty) continue;
                if (en == null || uye.Alan > en.Alan + 1e-6 || (Math.Abs(uye.Alan - en.Alan) <= 1e-6 && uye.Kademe > en.Kademe))
                    en = uye;
            }
            if (en == null) return;
            Envelope env = en.Sekil.EnvelopeInternal;
            double merkez = yataySerit ? (env.MinY + env.MaxY) * 0.5 : (env.MinX + env.MaxX) * 0.5;
            // Her dilimin kendi standart kutusu hiza boyu/tercih konumu verir; _duseyHiza boşken BYonUydur hizasız hesaplar.
            var adaylar = new List<TemelIlaveKutuBol.DuseyAday>();
            int referans = -1;
            double enYakin = double.MaxValue;
            bool icinde = false;
            foreach (double[] dilim in dilimler)
            {
                if (dilim == null || dilim.Length < 2 || dilim[1] - dilim[0] < 1.0) continue;
                TemelIlaveKutuBol.DuseyAday aday = DuseyAdayHesap(grup, yataySerit, dilim[0], dilim[1], minB, maxB, paspay);
                if (aday.Y1 - aday.Y0 < 1.0) continue;
                adaylar.Add(aday);
                if (icinde) continue;
                if (merkez >= dilim[0] && merkez <= dilim[1])
                {
                    referans = adaylar.Count - 1;
                    icinde = true;
                    continue;
                }
                double mesafe = merkez < dilim[0] ? dilim[0] - merkez : merkez - dilim[1];
                if (mesafe < enYakin)
                {
                    enYakin = mesafe;
                    referans = adaylar.Count - 1;
                }
            }
            HizaLog("  HIZA KUR: yatay={0} dilim={1} aday={2} referans={3} fark={4}", yataySerit, dilimler.Count, adaylar.Count, referans, DuseyHizaFarkCm);
            for (int i = 0; i < adaylar.Count; i++)
            {
                TemelIlaveKutuBol.DuseyAday a = adaylar[i];
                HizaLog("    aday {0}: kutu {1}..{2} (boy {3}) isiVar={4} isi {5}..{6} sinir {7}..{8}",
                    i, a.Y0, a.Y1, a.Y1 - a.Y0, a.IsiVar, a.Isi0, a.Isi1, a.Sinir0, a.Sinir1);
            }
            if (!TemelIlaveKutuBol.DuseyHizaBul(adaylar, referans, DuseyHizaFarkCm, out double h0, out double h1))
            {
                HizaLog("    SONUC: hiza bulunamadi");
                return;
            }
            HizaLog("    SONUC: hiza {0}..{1}", h0, h1);
            _duseyHiza.Var = true;
            _duseyHiza.Y0 = h0;
            _duseyHiza.Y1 = h1;
        }

        private static TemelIlaveKutuBol.DuseyAday DuseyAdayHesap(
            List<Isaret> grup,
            bool yataySerit,
            double a0,
            double a1,
            double minB,
            double maxB,
            List<SinirKenar> paspay)
        {
            var aday = new TemelIlaveKutuBol.DuseyAday
            {
                Y0 = minB,
                Y1 = maxB,
                Sinir0 = double.NegativeInfinity,
                Sinir1 = double.PositiveInfinity
            };
            BYonUydur(grup, yataySerit, a0, a1, paspay, ref aday.Y0, ref aday.Y1);
            if (!IsiBAraligi(grup, yataySerit, a0, a1, out double isi0, out double isi1)) return aday;
            PaspayBSiniri(paspay, yataySerit, a0, a1, isi0, isi1, out aday.Sinir0, out aday.Sinir1);
            aday.IsiVar = true;
            aday.Isi0 = Math.Max(isi0, aday.Sinir0);
            aday.Isi1 = Math.Min(isi1, aday.Sinir1);
            return aday;
        }

        private static void DuseyHizaUygula(ref double b0, ref double b1, bool isiVar, double isi0, double isi1, double sinir0, double sinir1)
        {
            if (!_duseyHiza.Var) return;
            var kutu = new TemelIlaveKutuBol.DuseyAday
            {
                Y0 = b0,
                Y1 = b1,
                IsiVar = isiVar,
                Isi0 = isi0,
                Isi1 = isi1,
                Sinir0 = sinir0,
                Sinir1 = sinir1
            };
            // Hiza ısıyı açarsa veya paspay şeridine girerse uygulanmaz.
            bool uyar = TemelIlaveKutuBol.DuseyHizaUyar(kutu, _duseyHiza.Y0, _duseyHiza.Y1, DuseyHizaFarkCm);
            HizaLog("    UYGULA kutu {0}..{1} isiVar={2} isi {3}..{4} sinir {5}..{6} -> {7}",
                b0, b1, isiVar, isi0, isi1, sinir0, sinir1, uyar ? "HIZALANDI" : "HIZASIZ");
            if (!uyar) return;
            b0 = _duseyHiza.Y0;
            b1 = _duseyHiza.Y1;
        }

        private static void KutuCizBoy(
            Transaction tr,
            BlockTableRecord btr,
            List<Isaret> grup,
            bool yataySerit,
            double b0,
            double b1,
            double a0,
            double a1,
            double dy,
            List<SinirKenar> paspay)
        {
            BYonUydur(grup, yataySerit, a0, a1, paspay, ref b0, ref b1);
            KutuCiz(tr, btr, yataySerit, b0, b1, a0, a1, dy, paspay);
        }

        private static bool IsiBAraligi(List<Isaret> grup, bool yataySerit, double a0, double a1, out double b0, out double b1)
        {
            b0 = double.MaxValue;
            b1 = double.MinValue;
            if (grup == null || a1 - a0 < 1.0) return false;
            foreach (Isaret uye in grup)
            {
                if (uye.Sekil == null || uye.Sekil.IsEmpty) continue;
                Coordinate[] h = uye.Sekil.ExteriorRing.Coordinates;
                for (int i = 0; i < h.Length - 1; i++)
                {
                    double aS = yataySerit ? h[i].Y : h[i].X;
                    double bS = yataySerit ? h[i].X : h[i].Y;
                    double aE = yataySerit ? h[i + 1].Y : h[i + 1].X;
                    double bE = yataySerit ? h[i + 1].X : h[i + 1].Y;
                    IsiKenarB(a0, a1, aS, bS, aE, bE, ref b0, ref b1);
                }
            }
            return b1 > b0;
        }

        private static void IsiKenarB(double a0, double a1, double aS, double bS, double aE, double bE, ref double minB, ref double maxB)
        {
            double da = aE - aS;
            double t0 = 0;
            double t1 = 1;
            if (Math.Abs(da) < 1e-8)
            {
                if (aS < a0 - 0.01 || aS > a1 + 0.01) return;
            }
            else
            {
                double c0 = (a0 - aS) / da;
                double c1 = (a1 - aS) / da;
                if (c0 > c1)
                {
                    double t = c0;
                    c0 = c1;
                    c1 = t;
                }
                if (c0 > t0) t0 = c0;
                if (c1 < t1) t1 = c1;
                if (t1 < t0 - 1e-9) return;
            }
            double p0 = bS + (bE - bS) * t0;
            double p1 = bS + (bE - bS) * t1;
            if (p0 < minB) minB = p0;
            if (p1 < minB) minB = p1;
            if (p0 > maxB) maxB = p0;
            if (p1 > maxB) maxB = p1;
        }

        private static void PaspayBSiniri(
            List<SinirKenar> kenarlar,
            bool yataySerit,
            double a0,
            double a1,
            double isi0,
            double isi1,
            out double sinir0,
            out double sinir1)
        {
            sinir0 = double.NegativeInfinity;
            sinir1 = double.PositiveInfinity;
            if (kenarlar == null) return;
            // Isı paspay çizgisini aşsa da kenar sınır sayılır; ısının ortasına göre taraf seçilir.
            double orta = (isi0 + isi1) * 0.5;
            foreach (SinirKenar k in kenarlar)
            {
                if (k.Yatay == yataySerit) continue;
                if (Math.Min(k.A1, a1) - Math.Max(k.A0, a0) < 5.0) continue;
                double ic = k.B + (k.DisNegatif ? TemelPaspayCm : -TemelPaspayCm);
                if (k.DisNegatif)
                {
                    if (ic < orta && ic > sinir0) sinir0 = ic;
                }
                else if (ic > orta && ic < sinir1)
                    sinir1 = ic;
            }
        }

        /// <summary>Netleştirme için bekleyen kutu (dy eklenmeden).</summary>
        private sealed class KutuKayit
        {
            public double X0, Y0, X1, Y1;
            public bool Yatay;
            public string Yazi;
            /// <summary>Kırpılmış (dikdörtgen olmayan) kutu halkaları; null ise dikdörtgen.</summary>
            public List<Coordinate[]> Halkalar;
            public Coordinate YaziNokta;
            /// <summary>Aralık yönünde paspay/temel kırpımıyla içeri çekilen kenarlar.</summary>
            public bool Kesik0, Kesik1;
            public double IstenenB0, IstenenB1;

            public double A0 { get => Yatay ? Y0 : X0; set { if (Yatay) Y0 = value; else X0 = value; } }
            public double A1 { get => Yatay ? Y1 : X1; set { if (Yatay) Y1 = value; else X1 = value; } }
            public double B0 { get => Yatay ? X0 : Y0; set { if (Yatay) X0 = value; else Y0 = value; } }
            public double B1 { get => Yatay ? X1 : Y1; set { if (Yatay) X1 = value; else Y1 = value; } }
        }

        /// <summary>GrupCiz süresince KutuCiz kutuları buraya toplar; grup sonunda netleştirilip çizilir.</summary>
        private static List<KutuKayit> _kutuBekle;

        private static void KutuCiz(
            Transaction tr,
            BlockTableRecord btr,
            bool yataySerit,
            double b0,
            double b1,
            double a0,
            double a1,
            double dy,
            List<SinirKenar> paspay = null)
        {
            double x0, y0, x1, y1;
            if (yataySerit)
            {
                x0 = b0;
                x1 = b1;
                y0 = a0;
                y1 = a1;
            }
            else
            {
                x0 = a0;
                x1 = a1;
                y0 = b0;
                y1 = b1;
            }
            PaspayCek(paspay, ref x0, ref y0, ref x1, ref y1);
            if (x1 - x0 < 5.0 || y1 - y0 < 5.0) return;
            // Son kapı: kutu temel poligonunun paspay içinden taşamaz. Taşan kutu kırpılır;
            // kırpılan dikdörtgen kalırsa dikdörtgen, çentikli temelde kırpılmış poligon çizilir.
            if (!KutuSinirdaKirp(ref x0, ref y0, ref x1, ref y1, out List<Coordinate[]> halkalar, out Coordinate yaziNokta)) return;
            var kayit = new KutuKayit
            {
                X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Yatay = yataySerit, Yazi = _kutuYazi,
                Halkalar = halkalar, YaziNokta = yaziNokta, IstenenB0 = b0, IstenenB1 = b1
            };
            kayit.Kesik0 = kayit.A0 > a0 + 0.5;
            kayit.Kesik1 = kayit.A1 < a1 - 0.5;
            if (_kutuBekle != null)
            {
                _kutuBekle.Add(kayit);
                return;
            }
            KutuYaz(tr, btr, kayit, dy);
        }

        /// <summary>Netleşmiş kutuyu çizer ya da çakışma taraması için toplar.</summary>
        private static void KutuYaz(Transaction tr, BlockTableRecord btr, KutuKayit k, double dy)
        {
            if (k.X1 - k.X0 < 5.0 || k.Y1 - k.Y0 < 5.0) return;
            if (_kutuTopla != null)
            {
                _kutuTopla.Add(new[] { k.X0, k.Y0, k.X1, k.Y1 });
                return;
            }
            HizaLog("    KUTU x {0}..{1} y {2}..{3} (istenen b {4}..{5}){6}", k.X0, k.X1, k.Y0, k.Y1, k.IstenenB0, k.IstenenB1, k.Halkalar != null ? " kirpik" : "");
            string katman = KutuKatman(k.Yatay);
            var orta = new Point3d((k.X0 + k.X1) * 0.5, (k.Y0 + k.Y1) * 0.5 + dy, 0);
            List<Coordinate[]> halkalar = k.Halkalar;
            if (halkalar == null)
            {
                halkalar = new List<Coordinate[]>
                {
                    new[] { new Coordinate(k.X0, k.Y0), new Coordinate(k.X1, k.Y0), new Coordinate(k.X1, k.Y1), new Coordinate(k.X0, k.Y1), new Coordinate(k.X0, k.Y0) }
                };
            }
            else if (k.YaziNokta != null)
                orta = new Point3d(k.YaziNokta.X, k.YaziNokta.Y + dy, 0);
            foreach (Coordinate[] halka in halkalar)
            {
                var kutu = new Polyline();
                int n = halka.Length;
                if (n > 1 && halka[0].Equals2D(halka[n - 1])) n--;
                for (int i = 0; i < n; i++)
                    kutu.AddVertexAt(i, new Point2d(halka[i].X, halka[i].Y + dy), 0, 0, 0);
                kutu.Closed = true;
                // Rd. Temel Planı ile aynı: X donatısı KUTU X, Y donatısı KUTU Y katmanı; renk katmandan.
                kutu.Layer = katman;
                kutu.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                btr.AppendEntity(kutu);
                tr.AddNewlyCreatedDBObject(kutu, true);
            }
            if (string.IsNullOrEmpty(k.Yazi)) return;
            // Kutunun ortası (sol üstte komşu kutu yazılarıyla karışıyordu), aynı katman.
            var yazi = new DBText
            {
                Layer = katman,
                Color = Color.FromColorIndex(ColorMethod.ByLayer, 256),
                Height = 20.0,
                WidthFactor = 0.85,
                TextString = k.Yazi,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                Position = orta,
                AlignmentPoint = orta
            };
            btr.AppendEntity(yazi);
            tr.AddNewlyCreatedDBObject(yazi, true);
            yazi.AdjustAlignment(btr.Database);
        }

        /// <summary>Komşu sayılan kutularda boy kenarı farkı bundan küçükse aynı hizaya alınır (cm).</summary>
        private const double NetBoyHizaCm = 2.0;
        /// <summary>Aralık yönünde iki kutu arası bundan küçük boşluk/bindirme ortak kenar sayılır (cm).</summary>
        private const double NetKomsuCm = 15.0;

        /// <summary>
        /// Grubun dikdörtgen kutularını netleştirir:
        /// 1) aralık yönünde komşu şeritler ortak kenarı paylaşır (küçük bindirme/boşluk kalmaz);
        /// 2) her şerit genişliği kendi lejant aralığının katıdır: paspaya dayanan uçtan başlanır,
        ///    pay zincir boyunca serbest uca aktarılır; serbest uç temel paspayına sığmazsa bir alt kata daralır;
        /// 3) komşu şeritlerin boy kenarları 2 cm'den yakınsa aynı hizaya alınır.
        /// </summary>
        private static void Netlestir(List<KutuKayit> kutular)
        {
            if (kutular == null || kutular.Count == 0) return;
            var dik = kutular.Where(k => k.Halkalar == null).ToList();
            if (dik.Count == 0) return;

            bool Komsu(KutuKayit alt, KutuKayit ust)
            {
                if (alt.Yatay != ust.Yatay || ReferenceEquals(alt, ust)) return false;
                if (Math.Min(alt.B1, ust.B1) - Math.Max(alt.B0, ust.B0) < 10.0) return false;
                double ara = ust.A0 - alt.A1;
                double minEn = Math.Min(alt.A1 - alt.A0, ust.A1 - ust.A0);
                return ara <= NetKomsuCm && -ara < minEn * 0.5 && alt.A0 < ust.A0;
            }

            // 3) Boy kenarları: komşu şeritlerde küçük kaçıklık içe doğru eşitlenir.
            foreach (KutuKayit p in dik)
                foreach (KutuKayit q in dik)
                {
                    if (!Komsu(p, q)) continue;
                    if (Math.Abs(p.B0 - q.B0) > 1e-6 && Math.Abs(p.B0 - q.B0) <= NetBoyHizaCm) p.B0 = q.B0 = Math.Max(p.B0, q.B0);
                    if (Math.Abs(p.B1 - q.B1) > 1e-6 && Math.Abs(p.B1 - q.B1) <= NetBoyHizaCm) p.B1 = q.B1 = Math.Min(p.B1, q.B1);
                }

            // Komşuluk bileşenleri (aralık yönünde zincirler).
            var kalan = new HashSet<KutuKayit>(dik);
            while (kalan.Count > 0)
            {
                var bilesen = new List<KutuKayit>();
                var kuyruk = new Queue<KutuKayit>();
                KutuKayit ilk = kalan.First();
                kalan.Remove(ilk);
                kuyruk.Enqueue(ilk);
                while (kuyruk.Count > 0)
                {
                    KutuKayit k = kuyruk.Dequeue();
                    bilesen.Add(k);
                    foreach (KutuKayit d in kalan.ToList())
                    {
                        if (!Komsu(k, d) && !Komsu(d, k)) continue;
                        kalan.Remove(d);
                        kuyruk.Enqueue(d);
                    }
                }
                ZincirNetlestir(bilesen, Komsu);
            }
        }

        private static void ZincirNetlestir(List<KutuKayit> zincir, Func<KutuKayit, KutuKayit, bool> komsu)
        {
            // Yalnız üst uçta paspaya dayanan zincir yukarıdan aşağı; diğerleri aşağıdan yukarı kurulur.
            bool asagidan = !(zincir.Any(k => k.Kesik1) && !zincir.Any(k => k.Kesik0));
            var orijinal = zincir.ToDictionary(k => k, k => new[] { k.A0, k.A1 });
            var sirali = asagidan ? zincir.OrderBy(k => k.A0).ToList() : zincir.OrderByDescending(k => k.A1).ToList();
            var bitti = new List<KutuKayit>();
            foreach (KutuKayit k in sirali)
            {
                double[] o = orijinal[k];
                double s = KutuAraligi(k.Yazi);
                if (asagidan)
                {
                    var altlar = bitti.Where(b => komsu(OrijinalKutu(b, orijinal), OrijinalKutu(k, orijinal))).ToList();
                    double cap = altlar.Count > 0 ? altlar.Max(b => b.A1) : o[0];
                    double uc = KatUc(k, cap, o[1] - cap, s, +1);
                    k.A0 = cap;
                    k.A1 = uc;
                }
                else
                {
                    var ustler = bitti.Where(b => komsu(OrijinalKutu(k, orijinal), OrijinalKutu(b, orijinal))).ToList();
                    double cap = ustler.Count > 0 ? ustler.Min(b => b.A0) : o[1];
                    double uc = KatUc(k, cap, cap - o[0], s, -1);
                    k.A1 = cap;
                    k.A0 = uc;
                }
                bitti.Add(k);
            }
        }

        /// <summary>Komşuluk orijinal (netleşme öncesi) konumlarla değerlendirilir.</summary>
        private static KutuKayit OrijinalKutu(KutuKayit k, Dictionary<KutuKayit, double[]> orijinal)
        {
            var c = new KutuKayit { X0 = k.X0, Y0 = k.Y0, X1 = k.X1, Y1 = k.Y1, Yatay = k.Yatay };
            c.A0 = orijinal[k][0];
            c.A1 = orijinal[k][1];
            return c;
        }

        /// <summary>
        /// Çapadan (sabit kenar) yon yönünde aralığın katı genişlikte serbest uç: önce bir üst kat,
        /// temel paspayına sığmazsa bir alt kat; aralık yoksa ya da bir kat bile sığmazsa istenen genişlik.
        /// </summary>
        private static double KatUc(KutuKayit k, double cap, double w, double s, int yon)
        {
            if (w < 5.0) w = 5.0;
            if (s < 1.0) return cap + yon * w;
            double oran = w / s;
            double yukari = Math.Max(1.0, Math.Ceiling(oran - 1e-3)) * s;
            double asagi = Math.Floor(oran + 1e-3) * s;
            if (SinirIcinde(k, cap, cap + yon * yukari)) return cap + yon * yukari;
            if (asagi >= s && SinirIcinde(k, cap, cap + yon * asagi)) return cap + yon * asagi;
            return cap + yon * w;
        }

        private static bool SinirIcinde(KutuKayit k, double n0, double n1)
        {
            if (_kutuSinir == null) return true;
            var c = new KutuKayit { X0 = k.X0, Y0 = k.Y0, X1 = k.X1, Y1 = k.Y1, Yatay = k.Yatay };
            c.A0 = Math.Min(n0, n1);
            c.A1 = Math.Max(n0, n1);
            return SinirKapsar(c.X0, c.Y0, c.X1, c.Y1);
        }

        /// <summary>Paspay çizgisine oturan kenar içeride sayılsın diye 0.5 cm içeriden bakılır.</summary>
        private static bool SinirKapsar(double x0, double y0, double x1, double y1)
        {
            if (_kutuSinir == null) return true;
            try
            {
                var gf = NtsGeometryServices.Instance.CreateGeometryFactory();
                return _kutuSinir.Covers(KutuPoligon(gf, x0 + 0.5, y0 + 0.5, x1 - 0.5, y1 - 0.5));
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>Kutu lejant yazısındaki aralık: "ø12/30" → 30.</summary>
        private static double KutuAraligi(string yazi)
        {
            if (string.IsNullOrEmpty(yazi)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(yazi, @"/\s*(\d+)");
            return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }

        /// <summary>
        /// Kutuyu <see cref="_kutuSinir"/> (temel paspay içi) ile kırpar. Tamamen içerideyse dokunmaz (halkalar null).
        /// Kırpım dikdörtgen kalırsa sınırlar güncellenir (halkalar null); değilse kırpılmış halkalar döner.
        /// Temel içinde 5×5 cm'den büyük parça kalmazsa false (kutu çizilmez).
        /// </summary>
        private static bool KutuSinirdaKirp(ref double x0, ref double y0, ref double x1, ref double y1,
            out List<Coordinate[]> halkalar, out Coordinate yaziNokta)
        {
            halkalar = null;
            yaziNokta = null;
            Geometry sinir = _kutuSinir;
            if (sinir == null || SinirKapsar(x0, y0, x1, y1)) return true;
            var gf = NtsGeometryServices.Instance.CreateGeometryFactory();
            Polygon kutu = KutuPoligon(gf, x0, y0, x1, y1);
            Geometry kesisim;
            try
            {
                kesisim = sinir.Intersection(kutu);
            }
            catch (System.Exception)
            {
                return true;
            }
            var parcalar = new List<Polygon>();
            for (int i = 0; kesisim != null && i < kesisim.NumGeometries; i++)
                if (kesisim.GetGeometryN(i) is Polygon p && p.Area >= 25.0) parcalar.Add(p);
            if (parcalar.Count == 0) return false;

            var env = new Envelope();
            double alan = 0;
            Polygon buyuk = parcalar[0];
            foreach (Polygon p in parcalar)
            {
                env.ExpandToInclude(p.EnvelopeInternal);
                alan += p.Area;
                if (p.Area > buyuk.Area) buyuk = p;
            }
            if (env.Width < 5.0 || env.Height < 5.0) return false;
            x0 = env.MinX;
            y0 = env.MinY;
            x1 = env.MaxX;
            y1 = env.MaxY;
            if (parcalar.Count == 1 && alan >= env.Area * 0.995) return true;

            halkalar = new List<Coordinate[]>();
            foreach (Polygon p in parcalar)
                halkalar.Add(p.ExteriorRing.Coordinates);
            yaziNokta = buyuk.InteriorPoint.Coordinate;
            return true;
        }

        private static ObjectId HalkaCiz(Transaction tr, BlockTableRecord btr, Coordinate[] coords, string layer, short renk)
        {
            List<Point2d> pts = TemizHalka(coords);
            if (pts == null || pts.Count < 3) return ObjectId.Null;
            var pl = new Polyline();
            for (int i = 0; i < pts.Count; i++)
                pl.AddVertexAt(i, pts[i], 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layer;
            pl.Color = Color.FromColorIndex(ColorMethod.ByAci, renk);
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            return pl.ObjectId;
        }

        /// <summary>4 mm'den kısa kenarı ve 1°'den dar kırığı halkadan çıkarır.</summary>
        private static List<Point2d> TemizHalka(Coordinate[] coords)
        {
            if (coords == null || coords.Length < 4) return null;
            int n = coords.Length;
            if (Math.Abs(coords[0].X - coords[n - 1].X) < 1e-6 && Math.Abs(coords[0].Y - coords[n - 1].Y) < 1e-6)
                n--;
            var pts = new List<Point2d>(n);
            for (int i = 0; i < n; i++)
            {
                var p = new Point2d(coords[i].X, coords[i].Y);
                if (pts.Count == 0 || pts[pts.Count - 1].GetDistanceTo(p) > 0.05)
                    pts.Add(p);
            }
            if (pts.Count >= 2 && pts[0].GetDistanceTo(pts[pts.Count - 1]) <= 0.05)
                pts.RemoveAt(pts.Count - 1);
            bool silindi = true;
            int tur = 0;
            while (silindi && pts.Count >= 4 && tur < 8)
            {
                silindi = false;
                tur++;
                for (int i = 0; i < pts.Count && pts.Count >= 4; i++)
                {
                    Point2d a = pts[(i + pts.Count - 1) % pts.Count];
                    Point2d b = pts[i];
                    Point2d c = pts[(i + 1) % pts.Count];
                    double ab = a.GetDistanceTo(b);
                    double bc = b.GetDistanceTo(c);
                    if (ab < 0.4 || bc < 0.4 || UcNeredeyseParalel(a, b, c))
                    {
                        pts.RemoveAt(i);
                        silindi = true;
                        break;
                    }
                }
            }
            return pts.Count >= 3 ? pts : null;
        }

        private static bool UcNeredeyseParalel(Point2d a, Point2d b, Point2d c)
        {
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double bcx = c.X - b.X, bcy = c.Y - b.Y;
            double len1 = Math.Sqrt(abx * abx + aby * aby);
            double len2 = Math.Sqrt(bcx * bcx + bcy * bcy);
            if (len1 < 0.4 || len2 < 0.4) return false;
            double dot = (abx * bcx + aby * bcy) / (len1 * len2);
            if (dot > 1) dot = 1;
            if (dot < -1) dot = -1;
            double aci = Math.Acos(dot) * 180.0 / Math.PI;
            return aci < 1.0 || aci > 179.0;
        }

        private static Polygon MetredenCm(GeometryFactory gf, Polygon metre, double offsetX, double offsetY)
        {
            try
            {
                var shell = CevirHalka(gf, metre.ExteriorRing.Coordinates, offsetX, offsetY);
                var holes = new LinearRing[metre.NumInteriorRings];
                for (int i = 0; i < holes.Length; i++)
                    holes[i] = CevirHalka(gf, metre.GetInteriorRingN(i).Coordinates, offsetX, offsetY);
                return gf.CreatePolygon(shell, holes);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static LinearRing CevirHalka(GeometryFactory gf, Coordinate[] kaynak, double offsetX, double offsetY)
        {
            var pts = new Coordinate[kaynak.Length];
            for (int i = 0; i < kaynak.Length; i++)
                pts[i] = new Coordinate(offsetX + kaynak[i].X * 100.0, offsetY - kaynak[i].Y * 100.0);
            return gf.CreateLinearRing(pts);
        }

        private static string KutuKatman(bool yatay) =>
            TemelIlaveDonatiFromPdf.BoxLayer(yatay ? TemelIlaveDonatiFromPdf.Yon.XAlt : TemelIlaveDonatiFromPdf.Yon.YAlt);

        /// <summary>KUTU X/Y katmanı yoksa Rd. Temel Planı rengiyle açılır; varsa kullanıcının ayarı korunur.</summary>
        private static void KutuKatmanHazirla(Transaction tr, Database db, bool yatay)
        {
            string name = KutuKatman(yatay);
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            {
                Name = name,
                Color = TemelIlaveDonatiFromPdf.BoxColor(yatay ? TemelIlaveDonatiFromPdf.Yon.XAlt : TemelIlaveDonatiFromPdf.Yon.YAlt),
                LineWeight = LineWeight.LineWeight020
            };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        private static void EnsureLayer(Transaction tr, Database db, string name, short aci)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, aci),
                LineWeight = LineWeight.LineWeight020
            };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        private sealed class Face
        {
            public readonly string Ad;
            public readonly string Katman;
            public readonly short Renk;
            public readonly string Cubuk;
            public readonly double Sinir;
            public readonly Func<GprRadyeDugumAs, double> As;

            public Face(string ad, string katman, short renk, string cubuk, double sinir, Func<GprRadyeDugumAs, double> asDeger)
            {
                Ad = ad;
                Katman = katman;
                Renk = renk;
                Cubuk = cubuk;
                Sinir = sinir;
                As = asDeger;
            }
        }
    }
}
