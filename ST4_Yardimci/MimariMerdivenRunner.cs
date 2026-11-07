using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using static ST4Yardimci.MimariBoslukAnaliz;
using static ST4Yardimci.MimariMerdivenAnaliz;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// MIMARIMERDIVEN: mimari kat planındaki merdiven kollarını (MIMARIBOSLUK hizalamasıyla) kalıp planına aktarır.
    /// Çıktı MERDIVEN kesit komutunun girdisidir: MERDIVEN (BEYKENT) rıht çizgileri; MERDIVEN YAZI (BEYKENT)
    /// çıkış hattı, başlangıç dairesi (alt kot) ve bitiş üçgeni (üst kot). Rıhtları kalıpta zaten olan kollar atlanır.
    /// </summary>
    internal static class MimariMerdivenRunner
    {
        private const string LayerMerdiven = "MERDIVEN (BEYKENT)";
        private const string LayerMerdivenYazi = "MERDIVEN YAZI (BEYKENT)";
        private const string LayerDosemeSiniri = "DOSEME SINIRI (BEYKENT)";
        private const string LayerKontrol = "MIMARI MERDIVEN (BEYKENT)";
        private const string Etiket = "\nMIMARIMERDIVEN: ";
        private static readonly string[] YapiKatman =
        {
            "KIRIS (BEYKENT)", "PERDE (BEYKENT)", "KOLON (BEYKENT)", "DOSEME SINIRI (BEYKENT)", "KAT SINIRI (BEYKENT)", LayerMerdiven
        };

        public static void Run(bool komutSatiri) => MimariBoslukRunner.KatKat(komutSatiri, false, true);

        /// <summary>Bağlamdaki merdivenler: kalıp/mimari çizgileri okunur, bulunur; "mevcut" olanlar ayrılır.</summary>
        private static List<Merdiven> Hesapla(Database db, MimariBoslukRunner.Baglam b)
        {
            var kalipMerdiven = new List<Parca>();
            var kalipYapi = new List<Parca>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                KalipCizgileri(tr, db, b.KalipPencere, kalipMerdiven, kalipYapi);
                tr.Commit();
            }
            var icZarf = b.KalipKolon.Count > 0
                ? new Kutu(b.KalipKolon.Min(k => k.X0), b.KalipKolon.Min(k => k.Y0), b.KalipKolon.Max(k => k.X1), b.KalipKolon.Max(k => k.Y1))
                : b.KalipPencere;
            // Merdiven döşeme deliğindedir; KAT SINIRI çokgeni onu dışarıda bırakabilir. Bina zarfı (kolonlar) yeterli.
            bool Icerde(double x, double y) => icZarf.Icerir(x, y, 50);
            return Bul(b.Mimari, b.H, kalipMerdiven, kalipYapi, Icerde, b.MimariModu);
        }

        /// <summary>Mimari modunda bir katın merdivenlerini onay sormadan çizer (güçlüler); yönü belirsizleri işaretler.</summary>
        internal static void MerdivenCiz(Editor ed, Database db, MimariBoslukRunner.Baglam b, string etiket)
        {
            var hepsi = Hesapla(db, b);
            var yeni = hepsi.Where(m => !m.Mevcut).ToList();
            var guclu = yeni.Where(m => m.Guclu).ToList();
            var zayif = yeni.Where(m => !m.Guclu).ToList();
            foreach (var m in guclu) Ciz(db, m);
            Isaretle(db, b.KalipPencere, zayif);
            ed.WriteMessage(etiket + "{0} merdiven çizildi ({1} + {2}), {3} zaten çizili, {4} yönü belirsiz {5} katmanında işaretli.",
                guclu.Count, LayerMerdiven, LayerMerdivenYazi, hepsi.Count - yeni.Count, zayif.Count, LayerKontrol);
            foreach (var m in yeni)
                ed.WriteMessage("\n  {0} @({1:0},{2:0}): {3}", m.Guclu ? "çizildi" : "zayıf", m.K.Cx, m.K.Cy, m.Neden);
        }

        /// <summary>Eski akış: açık çizim statik kalıp; mimari dosyadan/çizimden hizalanır, onaylı çizilir.</summary>
        internal static void KalipAkisi(Editor ed, Database db, bool komutSatiri)
        {
            if (!MimariBoslukRunner.HizaliOku(ed, db, Etiket, true, out var b)) return;
            List<Merdiven> hepsi = Hesapla(db, b);
            var yeni = hepsi.Where(m => !m.Mevcut).OrderBy(m => !m.Guclu).ToList();
            ed.WriteMessage(Etiket + "{0} merdiven bulundu: {1} kalıpta zaten var, {2} yeni ({3} güçlü, {4} yönü belirsiz/zayıf).",
                hepsi.Count, hepsi.Count - yeni.Count, yeni.Count, yeni.Count(m => m.Guclu), yeni.Count(m => !m.Guclu));
            foreach (var m in yeni)
                ed.WriteMessage("\n  {0} @({1:0},{2:0}): {3}", m.Guclu ? "güçlü" : "zayıf", m.K.Cx, m.K.Cy, m.Neden);

            int cizilen = 0;
            var bekleyen = new List<Merdiven>(yeni);
            if (yeni.Count > 0)
            {
                string secim = MimariBoslukRunner.Secim(ed, Etiket, komutSatiri
                    ? new[] { "Tumu", "Hicbiri" }
                    : new[] { "Tektek", "Tumu", "Hicbiri" }, "Yeni merdivenler (Tumu: yalnız güçlüler)");
                if (secim == null) return;
                if (secim == "Tumu")
                {
                    var guclu = yeni.Where(m => m.Guclu).ToList();
                    foreach (var m in guclu) Ciz(db, m);
                    cizilen = guclu.Count;
                    bekleyen = yeni.Where(m => !m.Guclu).ToList();
                }
                else if (secim == "Tektek") cizilen = TekTek(ed, db, yeni, out bekleyen);
            }
            Isaretle(db, b.KalipPencere, bekleyen);
            ed.WriteMessage(Etiket + "{0} merdiven çizildi ({1} + {2}); {3} onaysız merdiven {4} katmanında işaretlendi. Kesit için MERDIVEN komutunu kullanın.",
                cizilen, LayerMerdiven, LayerMerdivenYazi, bekleyen.Count, LayerKontrol);
        }

        private static void KalipCizgileri(Transaction tr, Database db, Kutu pencere, List<Parca> merdiven, List<Parca> yapi)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (e == null) continue;
                bool mrd = MimariBoslukRunner.Ayni(e.Layer, LayerMerdiven);
                bool yp = YapiKatman.Any(l => MimariBoslukRunner.Ayni(e.Layer, l));
                if (!mrd && !yp) continue;
                var parcalar = new List<Parca>();
                if (e is Line ln)
                    parcalar.Add(new Parca { X0 = ln.StartPoint.X, Y0 = ln.StartPoint.Y, X1 = ln.EndPoint.X, Y1 = ln.EndPoint.Y, Katman = e.Layer });
                else if (e is Polyline pl && pl.NumberOfVertices >= 2)
                {
                    var p = MimariBoslukRunner.Noktalar(pl, Matrix3d.Identity);
                    int n = pl.Closed ? p.Count : p.Count - 1;
                    for (int i = 0; i < n; i++)
                    {
                        var q = p[(i + 1) % p.Count];
                        parcalar.Add(new Parca { X0 = p[i][0], Y0 = p[i][1], X1 = q[0], Y1 = q[1], Katman = e.Layer });
                    }
                }
                foreach (var s in parcalar)
                {
                    if (!pencere.Icerir((s.X0 + s.X1) / 2, (s.Y0 + s.Y1) / 2)) continue;
                    if (mrd) merdiven.Add(s);
                    if (yp) yapi.Add(s);
                }
            }
        }

        private static int TekTek(Editor ed, Database db, List<Merdiven> yeni, out List<Merdiven> sorulmayan)
        {
            int n = 0;
            sorulmayan = new List<Merdiven>();
            for (int i = 0; i < yeni.Count; i++)
            {
                var m = yeni[i];
                while (true)
                {
                    MimariBoslukRunner.Yakinlas(ed, m.K);
                    var gecici = new List<ObjectId>();
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        BeykentKatmanYukle.Ensure(tr, db, LayerKontrol, 6, LineWeight.LineWeight030);
                        gecici.Add(MimariBoslukRunner.Ekle(tr, db, MimariBoslukRunner.Cerceve(m.K, 15), LayerKontrol, 1));
                        var yol = YolCizgisi(db, m.Yol, LayerKontrol);
                        yol.ColorIndex = 1;
                        gecici.Add(MimariBoslukRunner.Ekle(tr, db, yol));
                        gecici.Add(MimariBoslukRunner.Ekle(tr, db, Ucgen(db, m, LayerKontrol)));
                        tr.Commit();
                    }
                    ed.UpdateScreen();
                    string c = MimariBoslukRunner.Secim(ed, Etiket, new[] { "Evet", "Ters", "Hayir", "Tumu", "Cik" },
                        string.Format("{0}/{1} {2} ({3}) çizilsin mi (Ters: çıkış yönünü çevir)", i + 1, yeni.Count, m.Guclu ? "güçlü" : "zayıf", m.Neden));
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (var id in gecici)
                            if (tr.GetObject(id, OpenMode.ForWrite, false) is Entity e) e.Erase();
                        tr.Commit();
                    }
                    if (c == "Ters") { Cevir(m); continue; }
                    if (c == "Evet") { Ciz(db, m); n++; }
                    else if (c == "Tumu") { foreach (var k in yeni.Skip(i)) Ciz(db, k); return n + yeni.Count - i; }
                    else if (c == null || c == "Cik") { sorulmayan = yeni.Skip(i).ToList(); return n; }
                    break;
                }
            }
            return n;
        }

        /// <summary>Çıkış yönünü ters çevirir: kol sırası ve her kolun yönü.</summary>
        private static void Cevir(Merdiven m)
        {
            m.Kollar.Reverse();
            foreach (var k in m.Kollar) k.Cevir();
            m.Yol = Yol(m.Kollar);
            if (m.Kenarlar.Count > 0) SinirKur(m);
        }

        private static void Ciz(Database db, Merdiven m)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                BeykentKatmanYukle.Ensure(tr, db, LayerMerdiven, 5, LineWeight.LineWeight030);
                BeykentKatmanYukle.Ensure(tr, db, LayerMerdivenYazi, 2, LineWeight.LineWeight020);
                foreach (var k in m.Kollar)
                    foreach (double s in k.S)
                    {
                        double[] a = k.P(k.A0, s), c = k.P(k.A1, s);
                        var ln = new Line(new Point3d(a[0], a[1], 0), new Point3d(c[0], c[1], 0));
                        ln.SetDatabaseDefaults(db);
                        ln.Layer = LayerMerdiven;
                        MimariBoslukRunner.Ekle(tr, db, ln);
                    }
                MimariBoslukRunner.Ekle(tr, db, YolCizgisi(db, m.Yol, LayerMerdivenYazi));
                var cir = new Circle(new Point3d(m.Yol[0][0], m.Yol[0][1], 0), Vector3d.ZAxis, 5);
                cir.SetDatabaseDefaults(db);
                cir.Layer = LayerMerdivenYazi;
                MimariBoslukRunner.Ekle(tr, db, cir);
                MimariBoslukRunner.Ekle(tr, db, Ucgen(db, m, LayerMerdivenYazi));
                // Mimari modu: kol uç (sahanlık/kat) çizgileri ve göz, kullanıcının kalıplarındaki gibi DOSEME SINIRI katmanında.
                if (m.Kenarlar.Count > 0)
                {
                    BeykentKatmanYukle.Ensure(tr, db, LayerDosemeSiniri, 71, LineWeight.LineWeight030);
                    foreach (var c in m.Kenarlar)
                    {
                        var ln = new Line(new Point3d(c[0][0], c[0][1], 0), new Point3d(c[1][0], c[1][1], 0));
                        ln.SetDatabaseDefaults(db);
                        ln.Layer = LayerDosemeSiniri;
                        MimariBoslukRunner.Ekle(tr, db, ln);
                    }
                    foreach (var g in m.Gozler) MimariBoslukRunner.Ekle(tr, db, g, LayerDosemeSiniri, -1);
                }
                tr.Commit();
            }
        }

        private static Polyline YolCizgisi(Database db, List<double[]> yol, string katman)
        {
            var pl = new Polyline(yol.Count);
            for (int i = 0; i < yol.Count; i++) pl.AddVertexAt(i, new Point2d(yol[i][0], yol[i][1]), 0, 0, 0);
            pl.SetDatabaseDefaults(db);
            pl.Layer = katman;
            return pl;
        }

        /// <summary>Bitiş üçgeni (üst kot): ucu yol sonundan 8 ileride, tabanı 4 geride, 14 geniş (elle çizilen kalıplarla aynı).</summary>
        private static Polyline Ucgen(Database db, Merdiven m, string katman)
        {
            double[] p = m.Yol[m.Yol.Count - 1], q = m.Yol[m.Yol.Count - 2];
            double dx = p[0] - q[0], dy = p[1] - q[1], L = Math.Sqrt(dx * dx + dy * dy);
            if (L < 1e-6) { dx = 1; dy = 0; L = 1; }
            dx /= L; dy /= L;
            var pl = new Polyline(3);
            pl.AddVertexAt(0, new Point2d(p[0] + dx * 8, p[1] + dy * 8), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(p[0] - dx * 4 - dy * 7, p[1] - dy * 4 + dx * 7), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(p[0] - dx * 4 + dy * 7, p[1] - dy * 4 - dx * 7), 0, 0, 0);
            pl.Closed = true;
            pl.SetDatabaseDefaults(db);
            pl.Layer = katman;
            return pl;
        }

        /// <summary>Kalıp penceresindeki eski işaretleri siler, onaysız merdivenleri çerçeve + yol + yazıyla işaretler.</summary>
        private static void Isaretle(Database db, Kutu pencere, List<Merdiven> bekleyen)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null || !MimariBoslukRunner.Ayni(e.Layer, LayerKontrol)) continue;
                    Extents3d ex;
                    try { ex = e.GeometricExtents; } catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                    if (!pencere.Icerir((ex.MinPoint.X + ex.MaxPoint.X) / 2, (ex.MinPoint.Y + ex.MaxPoint.Y) / 2)) continue;
                    e.UpgradeOpen(); e.Erase();
                }
                if (bekleyen.Count > 0)
                {
                    BeykentKatmanYukle.Ensure(tr, db, LayerKontrol, 6, LineWeight.LineWeight030);
                    foreach (var m in bekleyen)
                    {
                        MimariBoslukRunner.Ekle(tr, db, MimariBoslukRunner.Cerceve(m.K, 15), LayerKontrol, -1);
                        MimariBoslukRunner.Ekle(tr, db, YolCizgisi(db, m.Yol, LayerKontrol));
                        MimariBoslukRunner.Ekle(tr, db, Ucgen(db, m, LayerKontrol));
                        var t = new DBText();
                        t.SetDatabaseDefaults(db);
                        t.TextString = "MERDİVEN? " + m.Neden;
                        t.Height = 10; t.Position = new Point3d(m.K.X0 - 15, m.K.Y1 + 22, 0);
                        t.Layer = LayerKontrol;
                        MimariBoslukRunner.Ekle(tr, db, t);
                    }
                }
                tr.Commit();
            }
        }
    }
}
