using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(KpProbe.Komutlar))]
namespace KpProbe
{
    public class Komutlar
    {
        static string Sonuc => Environment.GetEnvironmentVariable("KP_RESULT") ?? "kp.txt";
        static void Yaz(string s) => File.AppendAllText(Sonuc, s + Environment.NewLine);
        static Assembly Plan => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz_2027"));
        static Type T(string n) => Plan.GetType("ST4PlanIdCiz." + n, true);
        const BindingFlags SF = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        const BindingFlags IF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        static void Kutu(Transaction tr, BlockTableRecord ms, double x0, double y0, double x1, double y1, bool tara)
        {
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0); pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0); pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
            pl.Closed = true;
            var id = ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            if (!tara) return;
            var h = new Hatch();
            ms.AppendEntity(h); tr.AddNewlyCreatedDBObject(h, true);
            h.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31");
            h.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { id });
            h.EvaluateHatch(true);
        }
        static void Cizgi(Transaction tr, BlockTableRecord ms, double x0, double y0, double x1, double y1)
        { var l = new Line(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0)); ms.AppendEntity(l); tr.AddNewlyCreatedDBObject(l, true); }
        static void Metin(Transaction tr, BlockTableRecord ms, string s, double x, double y, double rot, bool mtext = false)
        {
            Entity e;
            if (mtext) e = new MText { Contents = s, Location = new Point3d(x, y, 0), TextHeight = 10, Rotation = rot };
            else e = new DBText { TextString = s, Position = new Point3d(x, y, 0), Height = 10, Rotation = rot };
            ms.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true);
        }

        [CommandMethod("KP_CIZ")]
        public void Ciz()
        {
            var db = AcApp.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                // A: ekran görüntüsü gibi — kolon 30, MC-01 Ln 425, perde/kolon 30, MC-02 Ln 225, kolon 30
                Kutu(tr, ms, 0, -60, 30, 0, true); Kutu(tr, ms, 455, -200, 485, 100, true); Kutu(tr, ms, 710, -25, 740, 30, true);
                Cizgi(tr, ms, 30, 0, 455, 0); Cizgi(tr, ms, 30, -25, 455, -25); Cizgi(tr, ms, 485, 0, 710, 0); Cizgi(tr, ms, 485, -25, 710, -25);
                Metin(tr, ms, "MC-01 (25/55)", 33, -15, 0); Metin(tr, ms, "MC-02 (25/55)", 488, -15, 0, true);
                Metin(tr, ms, "MC-03 (25/55)", 475, -190, Math.PI / 2); Metin(tr, ms, "DC-01 d=20cm", 150, -120, 0);
                // B: y=-1000 kolon 40, K101 Ln 400, saplama kiriş 25 (taramasız), K102 konsol 230
                Kutu(tr, ms, 0, -1040, 40, -1000, true);
                Cizgi(tr, ms, 440, -900, 440, -1200); Cizgi(tr, ms, 465, -900, 465, -1200);
                Metin(tr, ms, "K101 (30x60)", 50, -1020, 0); Metin(tr, ms, "K102 30/50", 480, -1020, 0);
                // C: A senaryosunun STA4CAD DWG gibi 1/100 pafta metre kopyası (×0.0001), (5000,5000)'e taşınmış
                var m = Matrix3d.Displacement(new Vector3d(5000, 5000, 0)) * Matrix3d.Scaling(0.0001, Point3d.Origin);
                var ids = new System.Collections.Generic.List<ObjectId>();
                foreach (ObjectId id in ms) ids.Add(id);
                foreach (var id in ids)
                {
                    var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    var b = e.Bounds; if (!b.HasValue || b.Value.MaxPoint.Y < -300) continue;
                    var k = e.GetTransformedCopy(m); ms.AppendEntity(k); tr.AddNewlyCreatedDBObject(k, true);
                }
                tr.Commit();
            }
        }

        [CommandMethod("KP_SEC")]
        public void Sec()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var o = T("KirisManuelPlanSecim").GetMethod("SecEditor", SF).Invoke(null, new object[] { doc });
            if (o == null) { Yaz("SEC null"); return; }
            Yaz("OZET " + o.GetType().GetMethod("Ozet", IF).Invoke(o, null));
            var v = T("KirisManuelVeri").GetMethod("Varsayilan", SF).Invoke(null, null);
            var u = T("KirisPlanOkuma").GetMethod("Uygula", SF).Invoke(null, new[] { v, o });
            Yaz("VERI " + u.GetType().GetMethod("Yaz", IF).Invoke(u, null));
            var args = new object[] { null };
            bool ok = (bool)u.GetType().GetMethod("Dogrula", IF).Invoke(u, args);
            Yaz("DOGRULA " + ok + " " + args[0]);
        }

        [CommandMethod("KP_DOK")]
        public void Dok()
        {
            var db = AcApp.DocumentManager.MdiActiveDocument.Database;
            var et = T("KirisPlanOkuma").GetMethod("TryEtiket", SF);
            int nT = 0, nE = 0, nH = 0, nHp = 0, nHc = 0, nP = 0, nI = 0, nBlokEt = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead);
                    string s = null; double rot = 0; Point3d p = Point3d.Origin;
                    if (e is DBText dt) { s = dt.TextString; rot = dt.Rotation; p = dt.Position; }
                    else if (e is MText mt) { s = mt.Text; rot = mt.Rotation; p = mt.Location; }
                    else if (e is Hatch h) { nH++; for (int i = 0; i < h.NumberOfLoops; i++) { if (h.GetLoopAt(i).IsPolyline) nHp++; else nHc++; } if (nH <= 3) Yaz("HATCH " + h.Layer + " " + h.PatternName + " loops=" + h.NumberOfLoops); }
                    else if (e is Polyline pl && pl.Closed) nP++;
                    else if (e is BlockReference br) { nI++; var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead); foreach (ObjectId c in btr) { var ce = tr.GetObject(c, OpenMode.ForRead); string cs = ce is DBText cdt ? cdt.TextString : ce is MText cmt ? cmt.Text : null; if (cs != null && (bool)et.Invoke(null, new object[] { cs, null, 0.0, 0.0 })) nBlokEt++; } }
                    if (s == null) continue;
                    nT++;
                    var a = new object[] { s, null, 0.0, 0.0 };
                    if ((bool)et.Invoke(null, a)) { nE++; if (nE <= 15) Yaz("ETIKET " + e.GetType().Name + " " + ((Entity)e).Layer + " '" + s + "' rot=" + rot.ToString("0.00") + " p=" + p); }
                    else if (s.Contains("/") && nT < 400 && s.Length < 30) Yaz("DIGER '" + s + "'");
                }
                tr.Commit();
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                var yazilar = new System.Collections.Generic.List<(string s, Point3d p, double r, string tip)>();
                foreach (ObjectId id in ms)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead);
                    if (e is DBText dt) yazilar.Add((dt.TextString, dt.Position, dt.Rotation, "T h=" + dt.Height.ToString("0.#") + " j=" + dt.Justify));
                    else if (e is MText mt) yazilar.Add((mt.Text, mt.Location, mt.Rotation, "M h=" + mt.TextHeight.ToString("0.#")));
                }
                foreach (var y in yazilar.Where(y => y.s.TrimStart().StartsWith("(") && y.s.Contains("/")).Take(4))
                {
                    Yaz("== '" + y.s + "' " + y.tip + " rot=" + y.r.ToString("0.00") + " p=" + y.p);
                    foreach (var z in yazilar.Where(z => z.p.DistanceTo(y.p) < 120 && z.s != y.s))
                        Yaz("   '" + z.s + "' " + z.tip + " rot=" + z.r.ToString("0.00") + " p=" + z.p);
                }
                tr.Commit();
            }
            Yaz($"YAZI {nT} ETIKET {nE} HATCH {nH} (poly loop {nHp}, egri loop {nHc}) KAPALIPL {nP} INSERT {nI} BLOKICI_ETIKET {nBlokEt}");
        }

        [CommandMethod("KP_SAF")]
        public void Saf()
        {
            var tp = T("KirisPlanOkuma");
            var et = tp.GetMethod("TryEtiket", SF);
            foreach (var s in new[] { "MC-01 (25/55)", "K101 (30x60)", "K-12 25/50", "K101(25*50)", "DC-01 d=20cm", "25/55", "S1-01", "K 1 (25/55)" })
            {
                var a = new object[] { s, null, 0.0, 0.0 };
                bool r = (bool)et.Invoke(null, a);
                Yaz("ETIKET '" + s + "' -> " + r + " " + a[1] + " " + a[2] + "/" + a[3]);
            }
            var ar = tp.GetMethod("Araliklardan", SF);
            foreach (var (t, bs) in new[] { (new double[] { 0, 30, 455, 485, 710, 740 }, false), (new double[] { 0, 200, 230 }, true), (new double[] { 0, 30, 300 }, false), (new double[] { 0, 30, 20 }, false), (new double[] { 0, 30 }, false) })
            {
                var a = new object[] { t, bs, null };
                var o = ar.Invoke(null, a);
                Yaz("ARALIK [" + string.Join(",", t) + "] bas=" + bs + " -> " + (o == null ? "HATA " + a[2] : o.GetType().GetMethod("Ozet", IF).Invoke(o, null)));
            }
        }
    }
}
