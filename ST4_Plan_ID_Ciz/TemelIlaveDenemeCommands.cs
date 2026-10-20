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
        private const double KopyaYukariCm = 6000.0;
        private const string KatmanIsaret = "CIZGI (BEYKENT)";
        private const short YetmezRenk = 1;
        private static readonly short[] BantRenk = { 4, 3, 2, 30, 40, 6, 5, 140, 150, 200, 8, 9 };

        [Autodesk.AutoCAD.Runtime.CommandMethod("TEMELILAVEDENEME")]
        public void TemelIlaveDeneme()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nTEMELILAVEDENEME: ST4 dosyasi secin",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            string gpr = Path.ChangeExtension(st4Path, ".GPR");
            if (!File.Exists(gpr)) gpr = Path.ChangeExtension(st4Path, ".gpr");
            if (!File.Exists(gpr))
            {
                ed.WriteMessage("\nTEMELILAVEDENEME: ST4 yaninda GPR yok. Radye sonuclari GPR'ye yazilmis olmali.");
                return;
            }

            List<GprRadyeDugumAs> nodes;
            GprRadyeGenelDonati genel;
            try
            {
                WindowsAnsiEncodings.EnsureRegistered();
                string[] lines = WindowsAnsiEncodings.ReadAllLines(gpr);
                if (!GprRadyeAyir.TryReadMesh(lines, out nodes, out string meshError))
                {
                    ed.WriteMessage("\nTEMELILAVEDENEME: {0}", meshError);
                    return;
                }
                if (!GprRadyeAyir.TryReadGenel(lines, out genel, out string genelError))
                {
                    ed.WriteMessage("\nTEMELILAVEDENEME: {0}", genelError);
                    return;
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nTEMELILAVEDENEME: GPR okunamadi: {0}", ex.Message);
                return;
            }

            var faces = new[]
            {
                new Face("ALT X", "TEMEL ILAVE X (BEYKENT)", 1, genel.Yazi(1), genel.AsXAlt, n => n.AsXAlt),
                new Face("ALT Y", "TEMEL ILAVE Y (BEYKENT)", 5, genel.Yazi(3), genel.AsYAlt, n => n.AsYAlt),
                new Face("UST X", "TEMEL ILAVE X (BEYKENT)", 1, genel.Yazi(0), genel.AsXUst, n => n.AsXUst),
                new Face("UST Y", "TEMEL ILAVE Y (BEYKENT)", 5, genel.Yazi(2), genel.AsYUst, n => n.AsYUst)
            };
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
            }
            catch (System.Exception ex)
            {
                if (ex is AcRxException aex)
                    ed.WriteMessage("\nTEMELILAVEDENEME hata: {0} ({1})", aex.Message, aex.ErrorStatus);
                else
                    ed.WriteMessage("\nTEMELILAVEDENEME hata: {0}", ex.Message);
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
            List<Isaret> isaretler)
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
                int n = CizYuz(tr, btr, copy, alan, face.Katman, adimlar[k].Renk, null, cizilen, sarkan);
                if (n > 0) gorunen.Add(adimlar[k]);
                IsaretEkle(isaretler, cizilen, adimlar[k].Alan, yatay, false, copy.TemelGeom);
                IsaretEkle(isaretler, sarkan, adimlar[k].Alan, yatay, true, copy.TemelGeom);
            }

            DonatiAdim son = adimlar[adimlar.Count - 1];
            string yetmezYazi = son.Yazi + " yetmez";
            List<Polygon> asan = RadyeEsikPoligon.Hesapla(nodes, ag, face.As, face.Sinir + son.Alan);
            var yetmezCizilen = new List<Polygon>();
            var yetmezSarkan = new List<Polygon>();
            int yetmez = CizYuz(tr, btr, copy, asan, face.Katman, YetmezRenk, yetmezYazi, yetmezCizilen, yetmezSarkan);
            IsaretEkle(isaretler, yetmezCizilen, son.Alan, yatay, false, copy.TemelGeom);
            IsaretEkle(isaretler, yetmezSarkan, son.Alan, yatay, true, copy.TemelGeom);
            if (gorunen.Count == 0 && yetmez == 0)
            {
                ed.WriteMessage("\n  {0}: ilave yok.", face.Ad);
                return;
            }
            Lejant(tr, btr, copy, face.Katman, gorunen, yetmez > 0 ? yetmezYazi : null);
            ed.WriteMessage("\n  {0}:", face.Ad);
            foreach (DonatiAdim adim in gorunen)
                ed.WriteMessage(" {0}", adim.Yazi);
            if (yetmez > 0) ed.WriteMessage(" {0}", yetmezYazi);
        }

        private static void IsaretEkle(List<Isaret> isaretler, List<Polygon> poligonlar, double alan, bool yatay, bool sarkan, Geometry temel)
        {
            if (poligonlar == null) return;
            foreach (Polygon p in poligonlar)
                isaretler.Add(new Isaret { Sekil = p, Alan = alan, Yatay = yatay, Sarkan = sarkan, Temel = temel });
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
            List<Polygon> sarkan)
        {
            int n = 0;
            if (alanlar == null) return 0;
            var gf = NtsGeometryServices.Instance.CreateGeometryFactory();
            foreach (Polygon metre in alanlar)
            {
                Polygon cm = MetredenCm(gf, metre, copy.OffsetX, copy.OffsetY);
                if (cm == null || cm.IsEmpty) continue;
                AyirTemel(cm, copy.TemelGeom, out Geometry ic, out Geometry lob);
                Geometry sekil = ic ?? cm;
                EkleCizilen(sekil, cizilen);
                n += CizGeometri(tr, btr, sekil, katman, renk, not);
                if (lob == null || lob.IsEmpty) continue;
                EkleCizilen(lob, sarkan);
                n += CizGeometri(tr, btr, lob, katman, renk, not);
            }
            return n;
        }

        /// <summary>Temel içinde kalan kontur ana şeritlere, sınır dışında kalan parça aynı yönde ayrı kutuya gider.</summary>
        private static void AyirTemel(Geometry cm, Geometry temel, out Geometry ic, out Geometry dis)
        {
            ic = cm;
            dis = null;
            if (cm == null || cm.IsEmpty || temel == null || temel.IsEmpty) return;
            try
            {
                Geometry a = NtsOverlaySafe.PrepareForOverlay(cm);
                Geometry b = NtsOverlaySafe.PrepareForOverlay(temel);
                if (a == null || a.IsEmpty || b == null || b.IsEmpty) return;
                Geometry kesisim = b.Intersection(a);
                if (kesisim != null && !kesisim.IsEmpty) ic = kesisim;
                Geometry fark = a.Difference(b);
                if (fark != null && !fark.IsEmpty && fark.Area >= 200.0) dis = fark;
            }
            catch (System.Exception)
            {
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
            var gruplar = Grupla(isaretler);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                EnsureLayer(tr, db, KatmanIsaret, 1);
                foreach (List<Isaret> grup in gruplar)
                    CizSeritler(tr, btr, gf, grup, dy);
                tr.Commit();
            }
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

            var seritler = new List<double[]>();
            for (int i = 0; i < sinir.Count - 1; i++)
            {
                double a0 = sinir[i];
                double a1 = sinir[i + 1];
                if (a1 - a0 < 5.0) continue;
                double orta = (a0 + a1) * 0.5;
                double enYuksek = -1;
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
                    if (uye.Alan > enYuksek) enYuksek = uye.Alan;
                }
                if (!degdi) continue;
                if (seritler.Count > 0 && Math.Abs(seritler[seritler.Count - 1][2] - enYuksek) < 0.01)
                    seritler[seritler.Count - 1][1] = a1;
                else
                    seritler.Add(new[] { a0, a1, enYuksek });
            }

            Geometry temel = null;
            foreach (Isaret uye in grup)
            {
                if (uye.Temel != null && !uye.Temel.IsEmpty) { temel = uye.Temel; break; }
            }
            var govde = new List<Coordinate[]>();
            var delik = new List<Coordinate[]>();
            List<SinirKenar> kenarlar = null;
            if (temel != null)
            {
                TemelHalkalari(temel, govde, delik);
                var ham = new List<SinirKenar>();
                foreach (Coordinate[] halka in govde)
                    KenarlariEkle(halka, govde, delik, yataySerit, ham);
                kenarlar = BirlestirKenar(ham);
            }

            foreach (double[] serit in seritler)
            {
                if (kenarlar == null || kenarlar.Count == 0 ||
                    !CizKirpilmis(tr, btr, gf, grup, govde, delik, kenarlar, yataySerit, serit[0], serit[1], minB, maxB, dy))
                    KutuCiz(tr, btr, yataySerit, minB, maxB, serit[0], serit[1], dy);
            }
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
            double dy)
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
            if (sec.DisNegatif)
            {
                if (a1 - kes >= 5.0)
                    KutuCiz(tr, btr, yataySerit, minB, maxB, kes, a1, dy);
                CizDisAralik(tr, btr, gf, grup, yataySerit, a0, kes, minB, maxB, dy);
            }
            else
            {
                if (kes - a0 >= 5.0)
                    KutuCiz(tr, btr, yataySerit, minB, maxB, a0, kes, dy);
                CizDisAralik(tr, btr, gf, grup, yataySerit, kes, a1, minB, maxB, dy);
            }
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

        /// <summary>Temel dışında kalan aralıkta kutu yalnız konturun kendi boyuna çizilir.</summary>
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
            double dy)
        {
            if (a1 - a0 < 8.0) return;
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
                if (r[1] - r[0] < 8.0 || r[3] - r[2] < 8.0) continue;
                KutuCiz(tr, btr, yataySerit, r[2], r[3], r[0], r[1], dy);
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
                hedef.Add(new SinirKenar { A0 = a0, A1 = a1, B = b, DisNegatif = arti && !eksi });
            }
        }

        private static List<SinirKenar> BirlestirKenar(List<SinirKenar> ham)
        {
            var sonuc = new List<SinirKenar>();
            if (ham == null || ham.Count == 0) return sonuc;
            ham.Sort(delegate (SinirKenar a, SinirKenar b)
            {
                int y = a.B.CompareTo(b.B);
                if (y != 0) return y;
                if (a.DisNegatif != b.DisNegatif) return a.DisNegatif ? -1 : 1;
                return a.A0.CompareTo(b.A0);
            });
            foreach (SinirKenar k in ham)
            {
                SinirKenar son = sonuc.Count > 0 ? sonuc[sonuc.Count - 1] : null;
                if (son != null && son.DisNegatif == k.DisNegatif && Math.Abs(son.B - k.B) < 3.0 && k.A0 <= son.A1 + 8.0)
                {
                    if (k.A1 > son.A1) son.A1 = k.A1;
                    continue;
                }
                sonuc.Add(new SinirKenar { A0 = k.A0, A1 = k.A1, B = k.B, DisNegatif = k.DisNegatif });
            }
            sonuc.RemoveAll(delegate (SinirKenar k) { return k.A1 - k.A0 < 30.0; });
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
                    hedef.Add(new Isaret { Sekil = p, Alan = kaynak.Alan, Yatay = kaynak.Yatay, Sarkan = kaynak.Sarkan, Temel = kaynak.Temel });
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

        private static void KutuCiz(
            Transaction tr,
            BlockTableRecord btr,
            bool yataySerit,
            double b0,
            double b1,
            double a0,
            double a1,
            double dy)
        {
            double x0, y0, x1, y1;
            if (yataySerit)
            {
                x0 = b0;
                x1 = b1;
                y0 = a0 + dy;
                y1 = a1 + dy;
            }
            else
            {
                x0 = a0;
                x1 = a1;
                y0 = b0 + dy;
                y1 = b1 + dy;
            }
            if (x1 - x0 < 5.0 || y1 - y0 < 5.0) return;
            var kutu = new Polyline();
            kutu.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
            kutu.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
            kutu.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
            kutu.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
            kutu.Closed = true;
            kutu.Layer = KatmanIsaret;
            kutu.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
            btr.AppendEntity(kutu);
            tr.AddNewlyCreatedDBObject(kutu, true);
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
