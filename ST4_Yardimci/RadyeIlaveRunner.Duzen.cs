using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ST4Yardimci
{
    /// <summary>
    /// RADYEILAVE "Düzenle" son işlemi (12.11.2026): bu çalıştırmada çizilen ilave nesnelerine çarpan
    /// kolon/perde isimleri kendi elemanının yanında boş bir yere alınır; başka yazıya binen ölçü yazısı
    /// kendi ölçü çizgisi üzerinde kaydırılır (işaret halkası çizilmez; eski halkalar silinir).
    /// Yalnız Duzenle açıkken çağrılır.
    /// </summary>
    internal static partial class RadyeIlaveRunner
    {
        private const string LayerKontrol = "RADYEILAVE KONTROL (BEYKENT)";

        private struct Kutu
        {
            public double X0, Y0, X1, Y1;
            public Kutu(Extents3d e) { X0 = e.MinPoint.X; Y0 = e.MinPoint.Y; X1 = e.MaxPoint.X; Y1 = e.MaxPoint.Y; }
            public Kutu(double x0, double y0, double x1, double y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
            public double W => X1 - X0;
            public double H => Y1 - Y0;
            public Kutu Kaydir(double dx, double dy) => new Kutu(X0 + dx, Y0 + dy, X1 + dx, Y1 + dy);
            public Kutu Genislet(double d) => new Kutu(X0 - d, Y0 - d, X1 + d, Y1 + d);
            public double Ortusme(Kutu o, double tol = 0)
            {
                double w = Math.Min(X1, o.X1) - Math.Max(X0, o.X0) - tol;
                double h = Math.Min(Y1, o.Y1) - Math.Max(Y0, o.Y0) - tol;
                return w > 0 && h > 0 ? w * h : 0;
            }
        }

        /// <summary>Çalıştırma başında alınır: bundan büyük tutamaçlı nesneler bu çalıştırmanın çizimidir.</summary>
        private static long DuzenBaslangic(Database db)
        {
            try { return db.Handseed.Value; } catch { return long.MaxValue; }
        }

        private static void DuzenSonIslem(Document doc, Database db, Editor ed, long seed)
        {
            if (!Duzenle || seed == long.MaxValue) return;
            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // 1) Yeni nesneler (etiket/ölçü yazısı kutuları + çubuk şeritleri) ve çalışma alanı
                    var yeniYazi = new List<Kutu>();   // etiket (+ poz balonu payı), ölçü yazısı, gönye yazısı
                    var yeniCizgi = new List<Kutu>();  // donatı/halka şeritleri
                    var olculer = new List<ObjectId>(); // yazısı kaydırılabilecek yeni ölçüler
                    double ax0 = double.MaxValue, ay0 = double.MaxValue, ax1 = double.MinValue, ay1 = double.MinValue;
                    var eskiKontrol = new List<ObjectId>();
                    foreach (ObjectId id in btr)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        if (LayerEquals(ent.Layer, LayerKontrol)) { eskiKontrol.Add(id); continue; }
                        if (ent.Handle.Value < seed) continue;
                        if (LayerEquals(ent.Layer, LayerKutuX) || LayerEquals(ent.Layer, LayerKutuY)) continue;
                        if (ent is DBText || ent is MText)
                        {
                            try { yeniYazi.Add(EtiketKutusu(ent)); } catch { }
                        }
                        else if (ent is Dimension dim)
                        {
                            if (ent is RotatedDimension rd)
                            {
                                olculer.Add(id);
                                // Donatı oku çizgisi: aks ölçü yazısı bunun üstüne oturmasın
                                if (LayerEquals(rd.Layer, LayerDonatiOku))
                                {
                                    bool yatay = Math.Abs(Math.Cos(rd.Rotation)) > 0.5;
                                    double a0 = yatay ? Math.Min(rd.XLine1Point.X, rd.XLine2Point.X) : Math.Min(rd.XLine1Point.Y, rd.XLine2Point.Y);
                                    double a1 = yatay ? Math.Max(rd.XLine1Point.X, rd.XLine2Point.X) : Math.Max(rd.XLine1Point.Y, rd.XLine2Point.Y);
                                    yeniCizgi.Add(yatay
                                        ? new Kutu(a0, rd.DimLinePoint.Y - 2, a1, rd.DimLinePoint.Y + 2)
                                        : new Kutu(rd.DimLinePoint.X - 2, a0, rd.DimLinePoint.X + 2, a1));
                                }
                            }
                            else foreach (Kutu k in OlcuYaziKutulari(dim)) yeniYazi.Add(k);
                        }
                        else if (ent is Polyline pl && pl.NumberOfVertices >= 2)
                        {
                            for (int i = 0; i < pl.NumberOfVertices - 1; i++)
                            {
                                Point2d a = pl.GetPoint2dAt(i), b = pl.GetPoint2dAt(i + 1);
                                yeniCizgi.Add(new Kutu(Math.Min(a.X, b.X) - 3, Math.Min(a.Y, b.Y) - 3, Math.Max(a.X, b.X) + 3, Math.Max(a.Y, b.Y) + 3));
                            }
                        }
                        else continue;
                        try
                        {
                            Extents3d e = ent.GeometricExtents;
                            ax0 = Math.Min(ax0, e.MinPoint.X); ay0 = Math.Min(ay0, e.MinPoint.Y);
                            ax1 = Math.Max(ax1, e.MaxPoint.X); ay1 = Math.Max(ay1, e.MaxPoint.Y);
                        }
                        catch { }
                    }
                    if (ax0 > ax1) { tr.Commit(); return; }
                    var alan = new Kutu(ax0, ay0, ax1, ay1).Genislet(300);

                    // Ölçü yazısı kutuları (her ölçü için tek birleşik kutu)
                    var olcuKutu = new List<Kutu>();
                    var olcuVar = new List<bool>();
                    foreach (ObjectId id in olculer)
                    {
                        var d = (Dimension)tr.GetObject(id, OpenMode.ForRead);
                        bool var = TekKutu(OlcuYaziKutulari(d), out Kutu k);
                        olcuKutu.Add(k);
                        olcuVar.Add(var);
                    }
                    var tumYazi = new List<Kutu>(yeniYazi);
                    for (int i = 0; i < olcuKutu.Count; i++) if (olcuVar[i]) tumYazi.Add(olcuKutu[i]);

                    // Önceki çalıştırmanın kontrol halkaları (bu alanda) silinir
                    foreach (ObjectId id in eskiKontrol)
                    {
                        var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        try { if (new Kutu(ent.GeometricExtents).Ortusme(alan) > 0) { ent.UpgradeOpen(); ent.Erase(); } } catch { }
                    }

                    // 2) Alan içindeki kolon/perde poligonları ve isim grupları
                    var eleman = new List<Kutu>();
                    var isimler = new List<(ObjectId id, Kutu k, string lyr, double rot, double h)>();
                    foreach (ObjectId id in btr)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null || ent.IsErased) continue;
                        string lyr = ent.Layer ?? "";
                        if (ent is Polyline pl && pl.Closed && pl.NumberOfVertices >= 3
                            && (LayerEquals(lyr, LayerKolon) || LayerEquals(lyr, LayerPerde) || LayerEquals(lyr, LayerKapama)))
                        {
                            try { var k = new Kutu(pl.GeometricExtents); if (k.Ortusme(alan) > 0) eleman.Add(k); } catch { }
                        }
                        else if (ent is DBText t && ent.Handle.Value < seed
                            && (LayerEquals(lyr, LayerKolonIsmi) || LayerEquals(lyr, LayerPerdeIsmi) || LayerEquals(lyr, LayerKapamaIsmi)))
                        {
                            try { var k = new Kutu(t.GeometricExtents); if (k.Ortusme(alan) > 0) isimler.Add((id, k, lyr, t.Rotation, t.Height)); } catch { }
                        }
                    }
                    var gruplar = IsimGruplari(isimler);

                    // 3) Yeni çizime çarpan isim grubunu elemanının yanına al
                    var grupKutu = new List<Kutu>();
                    foreach (var g in gruplar) grupKutu.Add(GrupKutusu(isimler, g));
                    int tasinan = 0;
                    for (int gi = 0; gi < gruplar.Count; gi++)
                    {
                        Kutu gk = grupKutu[gi];
                        double simdi = YeniCarpma(gk, tumYazi, yeniCizgi);
                        if (simdi <= 1.0) continue;
                        if (!SahipEleman(gk, eleman, out Kutu sahip)) continue;
                        double h = isimler[gruplar[gi][0]].h;
                        double gap = Math.Max(5.0, 0.6 * h);
                        var adaylar = new List<(double dx, double dy)>();
                        void Aday(double x0, double y0) => adaylar.Add((x0 - gk.X0, y0 - gk.Y0));
                        double cy = 0.5 * (sahip.Y0 + sahip.Y1) - 0.5 * gk.H, cx = 0.5 * (sahip.X0 + sahip.X1) - 0.5 * gk.W;
                        Aday(sahip.X1 + gap, sahip.Y0 - gap - gk.H);   // sağ alt (ST4 alışkanlığı)
                        Aday(sahip.X0 - gap - gk.W, sahip.Y0 - gap - gk.H); // sol alt
                        Aday(sahip.X1 + gap, sahip.Y1 + gap);          // sağ üst
                        Aday(sahip.X0 - gap - gk.W, sahip.Y1 + gap);   // sol üst
                        Aday(sahip.X1 + gap, cy);                      // sağ
                        Aday(sahip.X0 - gap - gk.W, cy);               // sol
                        Aday(cx, sahip.Y1 + gap);                      // üst
                        Aday(cx, sahip.Y0 - gap - gk.H);               // alt
                        double enIyi = simdi * 10.0 + DigerCarpma(gk, gi, grupKutu, eleman, sahip);
                        (double dx, double dy) secim = (0, 0);
                        foreach (var a in adaylar)
                        {
                            Kutu k = gk.Kaydir(a.dx, a.dy);
                            double sc = YeniCarpma(k, tumYazi, yeniCizgi) * 10.0 + DigerCarpma(k, gi, grupKutu, eleman, sahip)
                                + 0.001 * Math.Sqrt(a.dx * a.dx + a.dy * a.dy);
                            if (sc + 1e-6 < enIyi) { enIyi = sc; secim = a; }
                        }
                        if (Math.Abs(secim.dx) < 1e-6 && Math.Abs(secim.dy) < 1e-6) continue;
                        var v = new Vector3d(secim.dx, secim.dy, 0);
                        foreach (int ii in gruplar[gi])
                        {
                            var t = (Entity)tr.GetObject(isimler[ii].id, OpenMode.ForWrite);
                            t.TransformBy(Matrix3d.Displacement(v));
                        }
                        grupKutu[gi] = gk.Kaydir(secim.dx, secim.dy);
                        tasinan++;
                    }

                    // 4) Ölçü yazısı başka yazıya (etiket + poz balonu payı, ölçü yazısı, isim) ya da — aks ölçüsünde —
                    //    donatı oku çizgisine/halkaya/çubuğa binerse kendi ölçü çizgisi üzerinde, oklar arasından
                    //    çıkmadan kaydırılır (DIMTMOVE 2). Önce aks ölçüleri; donatı oku yazısı yalnız mecbur kalırsa.
                    int kaydirilan = 0, kalan = 0;
                    var bos = new List<Kutu>();
                    double Carp(Kutu k, int self)
                    {
                        double s = 0;
                        foreach (Kutu o in yeniYazi) s += k.Ortusme(o, 0.5);
                        for (int j = 0; j < olcuKutu.Count; j++) if (j != self && olcuVar[j]) s += k.Ortusme(olcuKutu[j], 0.5);
                        foreach (Kutu o in grupKutu) s += k.Ortusme(o, 0.5);
                        return s;
                    }
                    for (int gec = 0; gec < 2; gec++)
                    {
                        for (int i = 0; i < olculer.Count; i++)
                        {
                            if (!olcuVar[i]) continue;
                            var d = (RotatedDimension)tr.GetObject(olculer[i], OpenMode.ForRead);
                            bool oku = LayerEquals(d.Layer, LayerDonatiOku);
                            if (oku != (gec == 1)) continue;
                            Kutu k0 = olcuKutu[i];
                            double Skor(Kutu k) => Carp(k, i) + (oku ? 0.0 : 2.0 * YeniCarpma(k, bos, yeniCizgi));
                            double c0 = Skor(k0);
                            if (c0 <= 0) continue;
                            double ux = Math.Cos(d.Rotation), uy = Math.Sin(d.Rotation);
                            bool yatay = Math.Abs(ux) > 0.5;
                            double w = yatay ? k0.W : k0.H;
                            // Oklar arası: XLine noktalarının çizgi yönündeki izdüşümü, ok boyu + 2 cm pay
                            double p1 = d.XLine1Point.X * ux + d.XLine1Point.Y * uy;
                            double p2 = d.XLine2Point.X * ux + d.XLine2Point.Y * uy;
                            double sc = d.Dimscale > 0 ? d.Dimscale : 1.0;
                            double pay = Math.Max(0.0, d.Dimasz) * sc + 2.0;
                            double cMer = (0.5 * (k0.X0 + k0.X1)) * ux + (0.5 * (k0.Y0 + k0.Y1)) * uy;
                            double sMin = Math.Min(p1, p2) + pay + 0.5 * w - cMer;
                            double sMax = Math.Max(p1, p2) - pay - 0.5 * w - cMer;
                            if (sMax < sMin) { kalan++; continue; }
                            double enIyi = c0, secS = 0;
                            double adim = Math.Max(2.0, w / 6.0);
                            for (double s = sMin; s <= sMax + 1e-6; s += adim)
                            {
                                Kutu k = k0.Kaydir(ux * s, uy * s);
                                double v = Skor(k) + 0.01 * Math.Abs(s);
                                if (v + 1e-6 < enIyi) { enIyi = v; secS = s; }
                            }
                            if (Math.Abs(secS) < 1e-6) { kalan++; continue; }
                            try
                            {
                                d.UpgradeOpen();
                                d.Dimtmove = 2;
                                d.TextPosition = new Point3d(0.5 * (k0.X0 + k0.X1) + ux * secS, 0.5 * (k0.Y0 + k0.Y1) + uy * secS, 0);
                                d.RecomputeDimensionBlock(true);
                                olcuKutu[i] = TekKutu(OlcuYaziKutulari(d), out Kutu yk) ? yk : k0.Kaydir(ux * secS, uy * secS);
                                kaydirilan++;
                                if (Skor(olcuKutu[i]) > 0) kalan++;
                            }
                            catch { kalan++; }
                        }
                    }
                    tr.Commit();
                    ed?.WriteMessage(
                        "\nRADYEILAVE Düzenle: {0} kolon/perde ismi taşındı, {1} ölçü yazısı ölçü üzerinde kaydırıldı{2}.",
                        tasinan, kaydirilan, kalan > 0 ? "; " + kalan + " ölçü yazısında çakışma kaldı" : "");
                }
            }
            catch (System.Exception ex)
            {
                try { ed?.WriteMessage("\nRADYEILAVE Düzenle son işlem atlandı: {0}", ex.Message); } catch { }
            }
        }

        private static IEnumerable<Kutu> OlcuYaziKutulari(Dimension dim)
        {
            var sonuc = new List<Kutu>();
            var col = new DBObjectCollection();
            try { dim.Explode(col); } catch { return sonuc; }
            foreach (DBObject o in col)
            {
                if (o is DBText || o is MText)
                {
                    try { sonuc.Add(new Kutu(((Entity)o).GeometricExtents)); } catch { }
                }
                o.Dispose();
            }
            return sonuc;
        }

        /// <summary>Aynı katman ve dönüklükte, kutuları 0,6·h içinde değen isim satırları tek grup (ör. "SB-39" + "(50/50)").</summary>
        private static List<List<int>> IsimGruplari(List<(ObjectId id, Kutu k, string lyr, double rot, double h)> isimler)
        {
            int n = isimler.Count;
            var p = new int[n];
            for (int i = 0; i < n; i++) p[i] = i;
            int Bul(int x) { while (p[x] != x) x = p[x] = p[p[x]]; return x; }
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (isimler[i].lyr != isimler[j].lyr || Math.Abs(isimler[i].rot - isimler[j].rot) > 1e-3) continue;
                    double d = 0.6 * Math.Max(isimler[i].h, isimler[j].h);
                    if (isimler[i].k.Genislet(d).Ortusme(isimler[j].k) > 0) p[Bul(i)] = Bul(j);
                }
            var map = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int r = Bul(i);
                if (!map.TryGetValue(r, out var l)) map[r] = l = new List<int>();
                l.Add(i);
            }
            return new List<List<int>>(map.Values);
        }

        private static Kutu GrupKutusu(List<(ObjectId id, Kutu k, string lyr, double rot, double h)> isimler, List<int> g)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (int i in g)
            {
                x0 = Math.Min(x0, isimler[i].k.X0); y0 = Math.Min(y0, isimler[i].k.Y0);
                x1 = Math.Max(x1, isimler[i].k.X1); y1 = Math.Max(y1, isimler[i].k.Y1);
            }
            return new Kutu(x0, y0, x1, y1);
        }

        private static double YeniCarpma(Kutu k, List<Kutu> yazi, List<Kutu> cizgi)
        {
            double s = 0;
            foreach (Kutu o in yazi) s += k.Ortusme(o);
            foreach (Kutu o in cizgi) s += 0.5 * k.Ortusme(o);
            return s;
        }

        private static double DigerCarpma(Kutu k, int self, List<Kutu> gruplar, List<Kutu> eleman, Kutu sahip)
        {
            double s = 0;
            for (int i = 0; i < gruplar.Count; i++) if (i != self) s += 2.0 * k.Ortusme(gruplar[i]);
            foreach (Kutu e in eleman) s += k.Ortusme(e);
            return s;
        }

        /// <summary>İsim grubuna en yakın kolon/perde (≤ 150 cm) sahip sayılır.</summary>
        private static bool SahipEleman(Kutu g, List<Kutu> eleman, out Kutu sahip)
        {
            sahip = default;
            double best = 150.0;
            bool ok = false;
            foreach (Kutu e in eleman)
            {
                double dx = Math.Max(0, Math.Max(e.X0 - g.X1, g.X0 - e.X1));
                double dy = Math.Max(0, Math.Max(e.Y0 - g.Y1, g.Y0 - e.Y1));
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < best) { best = d; sahip = e; ok = true; }
            }
            return ok;
        }

        private static bool TekKutu(IEnumerable<Kutu> kutular, out Kutu k)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            bool var = false;
            foreach (Kutu q in kutular)
            {
                x0 = Math.Min(x0, q.X0); y0 = Math.Min(y0, q.Y0); x1 = Math.Max(x1, q.X1); y1 = Math.Max(y1, q.Y1);
                var = true;
            }
            k = var ? new Kutu(x0, y0, x1, y1) : default;
            return var;
        }

        /// <summary>Yazı kutusu; ilave etiketinde (alt/üst) önündeki poz balonu payı dahil.</summary>
        private static Kutu EtiketKutusu(Entity ent)
        {
            var k = new Kutu(ent.GeometricExtents);
            if (ent is DBText t && LayerEquals(t.Layer, LayerDonatiYazi)
                && (t.TextString.Contains("(alt)") || t.TextString.Contains("(üst)")))
            {
                if (Math.Abs(Math.Sin(t.Rotation)) > 0.5) k.Y0 -= PozBalonPayCm;
                else k.X0 -= PozBalonPayCm;
            }
            return k;
        }
    }
}
