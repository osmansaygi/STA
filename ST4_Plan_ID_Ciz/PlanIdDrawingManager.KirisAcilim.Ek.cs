using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// KIRISACILIMLARI: 1200 cm'yi (stok boyu) aşan alt düz ve gövde donatısında bindirmeli ek.
    /// TS 500 9.2.5: bindirme boyu ℓo = α1·ℓb, α1 = 1 + 0.5r; tüm çubuklar aynı kesitte eklenirse 1.5·ℓb.
    /// TBDY 2018 7.4.3.2(a): kolon yüzünden 2h sarılma bölgesinde ve açıklık ortası gibi donatının akabileceği
    /// kritik bölgelerde bindirmeli ek yapılmaz; izinli yerde ek boyunca etriye aralığı ≤ h/4 ve ≤ 10 cm.
    /// TBDY 2018 7.4.3.2(b): manşonlu veya bindirmeli kaynaklı ekler bir kesitte birer donatı atlanarak yapılır,
    /// komşu iki ekin merkezleri arası ≥ 60 cm.
    /// Alt düz: ek önce sarılma bölgesinin hemen dışında (izinli bölge) aranır; iki parça da ≤ 1200 olmuyorsa
    /// ek açıklık ortasından en uzak yere konur ve bindirmeli kaynaklı ek olarak işaretlenir.
    /// Gövde: eşit parçalar (600–1200 cm), ℓo = 1.5·ℓb; eğilme donatısı olmadığından düz bindirme.
    /// </summary>
    public sealed partial class PlanIdDrawingManager
    {
        private struct KirisEk
        {
            public double A, B;
            public bool Kaynak;
        }

        /// <summary>Açılımda ek ölçüsü çubuk ekseninden bu kadar aşağıda (cm).</summary>
        private const double KirisAcilimEkOlcuAsagiCm = 13.0;

        /// <summary>ℓo = 1.5·ℓb (konum II), 5 cm'ye yukarı yuvarlanır.</summary>
        private double KirisAcilimEkBoyu(int capMm)
        {
            return KirisAcilimYuvarlaYukari(1.5 * _lbPhiKonumII * capMm / 10.0);
        }

        /// <summary>
        /// Çubuğu (gönyeler dahil) ≤ 1200 cm parçalara böler. Ek gerekmiyorsa veya bölünemiyorsa null.
        /// esit: eşit boylu parçalar (gövde). Değilse önce izinli pencereler [izinSol0, izinSol1] / [izinSag0, izinSag1]
        /// (çizim X) denenir; olmazsa sağdan 1200'lük parçalarla bölünür ve ek kaynaklı işaretlenir.
        /// </summary>
        private List<List<Point2d>> KirisAcilimEkBol(
            List<Point2d> pts, double l0, bool esit,
            double izinSol0, double izinSol1, double izinSag0, double izinSag1,
            out List<KirisEk> ekler)
        {
            ekler = new List<KirisEk>();
            if (pts == null || pts.Count < 2 || l0 < 5.0) return null;
            int ana = 0;
            double enGenis = -1;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                double g = pts[i + 1].X - pts[i].X;
                if (g > enGenis) { enGenis = g; ana = i; }
            }
            Point2d a = pts[ana], b = pts[ana + 1];
            double duz = b.X - a.X;
            if (duz < 2.0 * l0 + 10.0) return null;
            double boyL = 0, boyR = 0;
            for (int i = 0; i < ana; i++) boyL += pts[i].GetDistanceTo(pts[i + 1]);
            for (int i = ana + 1; i + 1 < pts.Count; i++) boyR += pts[i].GetDistanceTo(pts[i + 1]);
            double max = KirisAcilimMontajMaxBoyCm;
            double toplam = boyL + duz + boyR;
            if (toplam <= max + 0.49) return null;
            Point2d At(double x) => new Point2d(x, a.Y + (b.Y - a.Y) * (x - a.X) / duz);
            bool Izinli(double s, double e) =>
                (s >= izinSol0 - 0.5 && e <= izinSol1 + 0.5) || (s >= izinSag0 - 0.5 && e <= izinSag1 + 0.5);

            var kes = new List<KirisEk>();
            if (esit)
            {
                // Stok boyuna göre (fire az): soldan tam boy 1200 cm parçalar, son parça kalan.
                // Kalan 600'ün altına düşecekse o adımda yarım boy (600) alınır; böylece parçalar 600–1200 arasında kalır.
                double yari = 0.5 * max;
                double kalan = toplam, uSon = 0;
                for (int guard = 0; guard < 20 && kalan > max + 0.49; guard++)
                {
                    double parca = max;
                    if (kalan - max + l0 < yari - 0.49 && kalan - yari + l0 <= max + 0.49) parca = yari;
                    uSon += parca;
                    double s = a.X + (uSon - l0 - boyL);
                    kes.Add(new KirisEk { A = s, B = s + l0, Kaynak = false });
                    uSon -= l0;
                    kalan = toplam - uSon;
                }
            }
            else
            {
                bool Uygun(double s, double e) =>
                    s > a.X + 1.0 && e < b.X - 1.0
                    && boyL + (e - a.X) <= max + 0.49 && (b.X - s) + boyR <= max + 0.49;
                double sSol = izinSol0, eSol = izinSol0 + l0;
                double eSag = izinSag1, sSag = izinSag1 - l0;
                if (Izinli(sSol, eSol) && Uygun(sSol, eSol))
                    kes.Add(new KirisEk { A = sSol, B = eSol, Kaynak = false });
                else if (Izinli(sSag, eSag) && Uygun(sSag, eSag))
                    kes.Add(new KirisEk { A = sSag, B = eSag, Kaynak = false });
                else
                {
                    // Sağdan 1200'lük parçalar: ek açıklık ortasından en uzağa düşer.
                    double sonX = b.X, sonEk = boyR;
                    for (int guard = 0; guard < 20 && boyL + (sonX - a.X) + sonEk > max + 0.49; guard++)
                    {
                        double s = sonX + sonEk - max;
                        double e = s + l0;
                        if (e >= sonX - 1.0 || s <= a.X + 1.0) return null;
                        kes.Insert(0, new KirisEk { A = s, B = e, Kaynak = !Izinli(s, e) });
                        sonX = e;
                        sonEk = 0;
                    }
                }
            }
            if (kes.Count == 0) return null;
            foreach (var k in kes)
                if (k.A <= a.X + 1.0 || k.B >= b.X - 1.0) return null;

            var parcalar = new List<List<Point2d>>();
            for (int k = 0; k <= kes.Count; k++)
            {
                var p = new List<Point2d>();
                if (k == 0) for (int i = 0; i <= ana; i++) p.Add(pts[i]);
                else p.Add(At(kes[k - 1].A));
                if (k == kes.Count) for (int i = ana + 1; i < pts.Count; i++) p.Add(pts[i]);
                else p.Add(At(kes[k].B));
                // Bindirmede çubuklar üst üste görünsün: tek sıradaki parça 2.5 cm yukarıda çizilir.
                if (k % 2 == 1)
                    for (int i = 0; i < p.Count; i++) p[i] = new Point2d(p[i].X, p[i].Y + KirisAcilimCubukAraCm);
                parcalar.Add(p);
            }
            ekler = kes;
            return parcalar;
        }

        /// <summary>
        /// Açılım satırında ek boyu ölçüsü, kaynaklı ekte kaynak işareti ve yönetmelik notu.
        /// yBar: açılım satırının çubuk ekseni; notlar: ölçünün altına alt alta yazılır.
        /// </summary>
        private void KirisAcilimEkNotu(Transaction tr, BlockTableRecord ms, Database db,
            KirisEk ek, double asagiCm, string[] notlar)
        {
            if (tr == null || ms == null || db == null || _kirisAcilimKesimY > 1e8) return;
            double yBar = _kirisAcilimKesimY - asagiCm;
            double yDim = yBar - KirisAcilimMontajSasirtmaCm - KirisAcilimAcilimOlcuUzakCm;
            ObjectId stil = GetOrCreatePlanOlcuDimStyle(tr, db, 10.0, 1.0, BeykentOrtak.OlcuStilAdlari.Acilim50_25);
            AppendEntity(tr, ms, new RotatedDimension(0.0,
                new Point3d(ek.A, yBar, 0), new Point3d(ek.B, yBar, 0),
                new Point3d(0.5 * (ek.A + ek.B), yDim, 0), "", stil)
            { Layer = LayerOlcu, Dimexo = 0.0, Dimse1 = false, Dimse2 = false });
            double xm = 0.5 * (ek.A + ek.B);
            double yNot = yDim - 9.0;
            if (ek.Kaynak)
            {
                // Kaynak işareti: ekten çıkan ok, referans çizgisi ve köşe kaynağı üçgeni.
                double yRef = yNot + 1.0;
                double xRef = xm + 12.0;
                KirisAcilimCizgi(tr, ms, xm, yBar - KirisAcilimMontajSasirtmaCm * 0.5, xRef, yRef, LayerOlcu);
                KirisAcilimCizgi(tr, ms, xm, yBar - KirisAcilimMontajSasirtmaCm * 0.5, xm + 1.2, yBar - KirisAcilimMontajSasirtmaCm * 0.5 - 3.2, LayerOlcu);
                KirisAcilimCizgi(tr, ms, xm, yBar - KirisAcilimMontajSasirtmaCm * 0.5, xm + 3.0, yBar - KirisAcilimMontajSasirtmaCm * 0.5 - 1.6, LayerOlcu);
                KirisAcilimCizgi(tr, ms, xRef, yRef, xRef + 22.0, yRef, LayerOlcu);
                var ucgen = new Polyline();
                ucgen.AddVertexAt(0, new Point2d(xRef + 6.0, yRef), 0, 0, 0);
                ucgen.AddVertexAt(1, new Point2d(xRef + 6.0, yRef - 4.0), 0, 0, 0);
                ucgen.AddVertexAt(2, new Point2d(xRef + 10.0, yRef), 0, 0, 0);
                ucgen.Layer = LayerOlcu;
                AppendEntity(tr, ms, ucgen);
                for (int i = 0; i < notlar.Length; i++)
                {
                    DrawBeamLabel(tr, ms, db, new Point3d(xRef + 26.0, yRef - 2.0 - i * 7.0, 0), notlar[i], i == 0 ? 5.0 : 4.0, 0.0,
                        KirisAcilimDonatiYaziLayer, bottomLeftAligned: true, useMiddleCenter: false);
                }
                return;
            }
            for (int i = 0; i < notlar.Length; i++)
                DrawBeamLabel(tr, ms, db, new Point3d(xm, yNot - i * 7.0, 0), notlar[i], i == 0 ? 5.0 : 4.0, 0.0,
                    KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
        }

        /// <summary>Görünüşte ek boyu ölçüsü (çubuğun üstünde).</summary>
        private void KirisAcilimEkGorunusOlcu(Transaction tr, BlockTableRecord ms, Database db, KirisEk ek, double yBar)
        {
            if (tr == null || ms == null || db == null) return;
            ObjectId stil = GetOrCreatePlanOlcuDimStyle(tr, db, 10.0, 1.0, BeykentOrtak.OlcuStilAdlari.Acilim50_25);
            AppendEntity(tr, ms, new RotatedDimension(0.0,
                new Point3d(ek.A, yBar, 0), new Point3d(ek.B, yBar, 0),
                new Point3d(0.5 * (ek.A + ek.B), yBar + KirisAcilimAcilimOlcuUzakCm, 0), "", stil)
            { Layer = LayerOlcu, Dimexo = 0.0, Dimse1 = false, Dimse2 = false });
        }

        private static string[] KirisAcilimEkNotMetni(bool govde, bool kaynak, double l0)
        {
            string boy = Math.Round(l0).ToString("0", CultureInfo.InvariantCulture);
            if (govde)
                return new[] { "GÖVDE BİNDİRMELİ EK L=" + boy, "TS500 9.2.5: lo=1.5 lb" };
            if (kaynak)
                return new[]
                {
                    "BİNDİRMELİ KAYNAKLI EK L=" + boy,
                    "TBDY 7.4.3.2(b): ekler birer donatı atlanarak, komşu ek merkezleri ≥ 60 cm",
                    "TBDY 7.4.3.2(a): açıklık ortasında kaynaksız bindirme yapılmaz; TS500 9.2.5: lo=1.5 lb"
                };
            return new[]
            {
                "BİNDİRMELİ EK L=" + boy + " (TS500 9.2.5: lo=1.5 lb)",
                "TBDY 7.4.3.2(a): ek boyunca etriye aralığı ≤ h/4 ve ≤ 10 cm"
            };
        }
    }
}
