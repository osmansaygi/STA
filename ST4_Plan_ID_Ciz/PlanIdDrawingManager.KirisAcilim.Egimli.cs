using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// KIRISACILIMLARI eğimli kiriş zinciri (ST4 Beams Data p[11]/p[12] uç kotları; çatı mahya/dere kirişi).
    /// Zincirin tüm açıklıkları eğimliyse (|tan α| ≥ 0,10) donatı ve etriye bu dosyadan çizilir:
    /// kiriş yüksekliği eksene dik (düşeyde h/cos α), etriye eksene dik ve aralığı eksen boyunca,
    /// dışbükey yüzdeki çubuk kırıktan bükülerek sürekli geçer, içbükey yüzdeki çubuk bükülmez:
    /// düz devam edip karşı kolun öbür yüzüne kenetlenir (mahyada alt, derede üst donatı çaprazlanır).
    /// Eğimli açıklığı olmayan zincirde hiçbir çizim değişmez.
    /// </summary>
    public sealed partial class PlanIdDrawingManager
    {
        /// <summary>Bu eğimin altındaki açıklık düz kiriş gibi çizilir (tan α).</summary>
        private const double KirisEgimliMinTan = 0.10;
        /// <summary>Eğimli çubuk açılımları arası düşey adım (cm, 2× öncesi).</summary>
        private const double KirisEgimliAcilimAdimCm = 24.0;
        /// <summary>İlk açılım satırının kolon kesiminden aşağı uzaklığı (cm, 2× öncesi).</summary>
        private const double KirisEgimliAcilimIlkCm = 50.0;
        private bool _kirisEgimliZincir;

        private sealed class KirisEgimliCubuk
        {
            public List<Point2d> Yol;
            public int Adet, Cap;
            public double Boy;
            public string Tur;
            /// <summary>Açılımda çubuğu üst yüz hizasına getiren düşey ofset (d·sec).</summary>
            public double Ofs;
            /// <summary>Bindirmede satır içi ek kaydırma (cm).</summary>
            public double Kay;
            /// <summary>Bu parçanın başındaki bindirme: parça yolu üzerinde boy (0..Lo) ve bindirme boyu; yoksa 0.</summary>
            public double EkBoy;
            public bool EkKaynak;
        }
        private readonly List<KirisEgimliCubuk> _kirisEgimliCubuklar = new List<KirisEgimliCubuk>();

        private static double KirisEgimTan(KirisAcilimSpan s)
        {
            double L = s.X1 - s.X0;
            return L < 1.0 ? 0.0 : (s.Kot1 - s.Kot0) / L;
        }

        /// <summary>1/cos α; eşik altındaki eğimde 1.</summary>
        private static double KirisEgimCarpan(KirisAcilimSpan s)
        {
            double t = KirisEgimTan(s);
            return Math.Abs(t) < KirisEgimliMinTan ? 1.0 : Math.Sqrt(1.0 + t * t);
        }

        /// <summary>Görünüşte düşey kiriş derinliği: eksene dik h, eğimli açıklıkta h/cos α.</summary>
        private static double KirisAcilimDusH(KirisAcilimSpan s) => s.Beam.HeightCm * KirisEgimCarpan(s);

        private bool KirisEgimliZincirMi(List<KirisAcilimSpan> chain)
        {
            // En az bir açıklık eğimliyse zincir (düz kollarıyla birlikte) eğimli düzenle çizilir; hiç eğimli açıklık yoksa eski yol.
            if (_surekliTemelAcilim || chain == null || chain.Count == 0) return false;
            bool egimli = false;
            for (int i = 0; i < chain.Count; i++)
            {
                var s = chain[i];
                if (s?.Beam == null) return false;
                if (Math.Abs(KirisEgimTan(s)) >= KirisEgimliMinTan) egimli = true;
                if (s.GuseHg > 0 || Math.Abs(s.Beam.HeightCm - chain[0].Beam.HeightCm) > 0.5) return false;
                if (i == 0) continue;
                if (Math.Abs(s.X0 - chain[i - 1].X1) > 2.0 || Math.Abs(s.Kot0 - chain[i - 1].Kot1) > 1.0) return false;
            }
            return egimli;
        }

        private static bool KirisEgimliDuzMu(KirisAcilimSpan s) => Math.Abs(KirisEgimTan(s)) < KirisEgimliMinTan;

        /// <summary>
        /// Mesnet yüzü: kolon iç yüzünün kirişle birleştiği köşeden kiriş eksenine dik çizilen hattın d derinliğindeki x'i.
        /// Alt ve üst köşeden çizilen iki dik hattan kolonun içine düşeni alınır (spanSagda: kiriş mesnedin sağında).
        /// Düz kirişte iç yüzün kendisidir.
        /// </summary>
        private static double KirisEgimliYuzX(KirisAcilimSpan s, double ic, double d, bool spanSagda)
        {
            double t = KirisEgimTan(s), sec = Math.Sqrt(1.0 + t * t), h = s.Beam.HeightCm;
            double alt = ic - t / sec * (h - d), ust = ic + t / sec * d;
            return spanSagda ? Math.Min(alt, ust) : Math.Max(alt, ust);
        }

        /// <summary>Üst yüzden eksene dik d uzaklıktaki paralel doğrunun x'teki kotu (zincir yerel).</summary>
        private static double KirisEgimliY(KirisAcilimSpan s, double d, double x)
        {
            double t = KirisEgimTan(s);
            return s.Kot0 + t * (x - s.X0) - d * Math.Sqrt(1.0 + t * t);
        }

        /// <summary>chain[k] ile chain[k+1] birleşiminde d ofsetli doğruların kesişimi (zincir yerel).</summary>
        private static Point2d KirisEgimliKose(List<KirisAcilimSpan> chain, int k, double d)
        {
            var a = chain[k];
            var b = chain[k + 1];
            double ta = KirisEgimTan(a), tb = KirisEgimTan(b);
            double xj = a.X1;
            double ya = KirisEgimliY(a, d, xj), yb = KirisEgimliY(b, d, xj);
            if (Math.Abs(ta - tb) < 1e-6) return new Point2d(xj, ya);
            double dx = (yb - ya) / (ta - tb);
            return new Point2d(xj + dx, ya + ta * dx);
        }

        private static bool KirisEgimliMesnet(KirisAcilimSupport[] sup, int j)
            => sup != null && j >= 0 && j < sup.Length && sup[j].X1 - sup[j].X0 > 2.0;

        private static double KirisEgimliIstasyon(List<KirisAcilimSpan> chain, int j)
            => j < chain.Count ? chain[j].X0 : chain[chain.Count - 1].X1;

        /// <summary>
        /// Çubuk ucu; noktalar uçtan gövdeye doğru eklenir. tip 0: serbest (x verilir), 1: mesnet (dış yüz − paspayı,
        /// kanca istenirse 12φ/ℓb gönye), 2: kırıkta çapraz (düz devam, karşı kolun öbür yüzüne kenetlenir).
        /// </summary>
        private void KirisEgimliUc(List<KirisAcilimSpan> chain, KirisAcilimSupport[] sup, int i, bool sol,
            double d, double h, double phiCm, double lb, int tip, double xSerbest, bool kanca, double ekPas, List<Point2d> o)
        {
            var s = chain[i];
            double t = KirisEgimTan(s), sec = Math.Sqrt(1.0 + t * t);
            double pas = KirisAcilimMontajPaspayiCm;
            double yon = sol ? -1.0 : 1.0;
            if (tip == 0)
            {
                o.Add(new Point2d(xSerbest, KirisEgimliY(s, d, xSerbest)));
                return;
            }
            if (tip == 1)
            {
                int j = sol ? i : i + 1;
                double station = KirisEgimliIstasyon(chain, j);
                double dis = station + (sol ? sup[j].X0 : sup[j].X1);
                // Kenetlenme, kolon–kiriş birleşim köşesinden eksene dik çizilen mesnet hattından ölçülür.
                double ic = KirisEgimliYuzX(s, station + (sol ? sup[j].X1 : sup[j].X0), d, sol);
                double x = dis - yon * (pas + ekPas);
                double gomme = Math.Abs(ic - x) * sec;
                if (!kanca)
                {
                    // Gövde: mesnede düz, en çok ℓb girer.
                    if (gomme > lb) x = ic + yon * lb / sec;
                    o.Add(new Point2d(x, KirisEgimliY(s, d, x)));
                    return;
                }
                double y = KirisEgimliY(s, d, x);
                bool asagi = d < h * 0.5;
                double boy = KirisAcilimYuvarlaYukari(Math.Max(KirisAcilimKenarDuseyPhi * phiCm, lb - gomme));
                double oda = asagi ? (sup[j].ExtendDown ? double.MaxValue : (h - d) * sec - pas) : d * sec - pas;
                if (boy > oda) boy = oda;
                if (boy >= 5.0) o.Add(new Point2d(x, y + (asagi ? -boy : boy)));
                o.Add(new Point2d(x, y));
                return;
            }
            var diger = chain[sol ? i - 1 : i + 1];
            Point2d kose = KirisEgimliKose(chain, sol ? i - 1 : i, d);
            double to = KirisEgimTan(diger), seco = Math.Sqrt(1.0 + to * to);
            // Kendi doğrusu kırıktan sonra karşı kolun üst yüzüne mi (mahya) alt yüzüne mi (dere) gider.
            bool yukari = sol ? to > t : to < t;
            double dT = yukari ? pas : h - pas;
            double reach = lb;
            double xk = kose.X + yon * lb / sec;
            if (Math.Abs(to - t) > 0.05)
            {
                double aOwn = s.Kot0 - t * s.X0 - d * sec;
                double aOth = diger.Kot0 - to * diger.X0 - dT * seco;
                double xs = (aOwn - aOth) / (to - t);
                double r = (xs - kose.X) * yon * sec;
                if (r > 0) { reach = r; xk = xs; }
            }
            if (!kanca)
            {
                double ext = Math.Min(lb, reach);
                double x = kose.X + yon * ext / sec;
                o.Add(new Point2d(x, KirisEgimliY(s, d, x)));
                return;
            }
            if (reach >= lb)
            {
                // Karşı yüz çok uzaksa (küçük kırılma açısı) ℓb kadar düz uzanır.
                double ext = reach <= 1.5 * lb ? reach : lb;
                double x = kose.X + yon * ext / sec;
                o.Add(new Point2d(x, KirisEgimliY(s, d, x)));
                return;
            }
            // Karşı yüze ℓb'den önce varır: kalan boy karşı kolun yüzü boyunca, kırıktan uzağa.
            double ek = KirisAcilimYuvarlaYukari(Math.Max(KirisAcilimKenarDuseyPhi * phiCm, lb - reach));
            double xq = xk + yon * ek / seco;
            o.Add(new Point2d(xq, KirisEgimliY(diger, dT, xq)));
            o.Add(new Point2d(xk, KirisEgimliY(s, d, xk)));
        }

        private List<Point2d> KirisEgimliYol(List<KirisAcilimSpan> chain, KirisAcilimSupport[] sup, int ia, int ib,
            double d, double h, double phiCm, double lb, int solTip, double solX, int sagTip, double sagX, bool kanca, double ekPas = 0)
        {
            var pts = new List<Point2d>();
            KirisEgimliUc(chain, sup, ia, true, d, h, phiCm, lb, solTip, solX, kanca, ekPas, pts);
            for (int k = ia; k < ib; k++) pts.Add(KirisEgimliKose(chain, k, d));
            var son = new List<Point2d>();
            KirisEgimliUc(chain, sup, ib, false, d, h, phiCm, lb, sagTip, sagX, kanca, ekPas, son);
            son.Reverse();
            pts.AddRange(son);
            return pts;
        }

        private static List<Point2d> KirisEgimliDunya(List<Point2d> yol, double x0, double yShift)
        {
            var w = new List<Point2d>(yol.Count);
            foreach (var p in yol) w.Add(new Point2d(x0 + p.X, yShift + p.Y));
            return w;
        }

        /// <summary>Çubuğu görünüşe çizer, açılım satırı için kaydeder.</summary>
        private void KirisEgimliCubukCiz(Transaction tr, BlockTableRecord ms, List<Point2d> yol, double x0, double yShift,
            short renk, int adet, int cap, string tur, double ofs = 0)
        {
            if (yol == null || yol.Count < 2) return;
            var w = KirisEgimliDunya(yol, x0, yShift);
            double boy = KirisAcilimYuvarlaYukari(KirisAcilimDonatiPl(tr, ms, renk, w, egikGonye: true) - 0.01);
            if (boy < 1.0) return;
            _kirisEgimliCubuklar.Add(new KirisEgimliCubuk { Yol = w, Adet = adet, Cap = cap, Boy = boy, Tur = tur, Ofs = ofs });
        }

        private static double KirisEgimliYolBoy(List<Point2d> p)
        {
            double L = 0;
            for (int i = 1; i < p.Count; i++) L += p[i - 1].GetDistanceTo(p[i]);
            return L;
        }

        /// <summary>Yolun s..e boyları arasındaki parçası.</summary>
        private static List<Point2d> KirisEgimliAltYol(List<Point2d> p, double s, double e)
        {
            var o = new List<Point2d>();
            double u = 0;
            for (int i = 1; i < p.Count; i++)
            {
                double L = p[i - 1].GetDistanceTo(p[i]);
                double u1 = u + L;
                if (u1 >= s - 1e-6 && u <= e + 1e-6 && L > 1e-9)
                {
                    double a = Math.Max(0, (s - u) / L), b = Math.Min(1, (e - u) / L);
                    var pa = p[i - 1] + (p[i] - p[i - 1]) * a;
                    var pb = p[i - 1] + (p[i] - p[i - 1]) * b;
                    if (o.Count == 0 || o[o.Count - 1].GetDistanceTo(pa) > 0.01) o.Add(pa);
                    if (pb.GetDistanceTo(o[o.Count - 1]) > 0.01) o.Add(pb);
                }
                u = u1;
            }
            return o;
        }

        /// <summary>
        /// 1200 cm'yi aşan eğimli çubuğu (yerel yol) bindirmeli parçalara böler; gerekmiyorsa null.
        /// Ek, mesnet yüzünden 2h sarılma bölgesine ve kırığın ±h çevresine konmaz; alt donatıda açıklığın orta
        /// üçte biri de yasaktır (TBDY 7.4.3.2a). Uygun yer yoksa alt donatıda ek kaynaklı işaretlenir.
        /// Parçalar soldan stok boyuna (1200) yakın; ℓo = 1.5·ℓb (TS500 9.2.5).
        /// </summary>
        private List<(List<Point2d> Yol, double EkBoy, bool Kaynak)> KirisEgimliEkBol(
            List<KirisAcilimSpan> chain, KirisAcilimSupport[] sup, List<Point2d> yol, double lo, bool alt)
        {
            double limit = KirisAcilimMontajMaxBoyCm - KirisAcilimYuvarlamaCm;
            double toplam = KirisEgimliYolBoy(yol);
            if (toplam <= limit + 0.01 || lo < 5.0) return null;
            int m = chain.Count;
            double h = chain[0].Beam.HeightCm;
            var yasak = new List<(double Lo, double Hi)>();
            var mesnetler = new List<double>();
            for (int j = 0; j <= m; j++)
            {
                double st = KirisEgimliIstasyon(chain, j);
                if (KirisEgimliMesnet(sup, j))
                {
                    yasak.Add((st + sup[j].X0 - 2.0 * h, st + sup[j].X1 + 2.0 * h));
                    mesnetler.Add(st);
                }
                else if (j > 0 && j < m && Math.Abs(KirisEgimTan(chain[j - 1]) - KirisEgimTan(chain[j])) > 1e-3)
                    yasak.Add((st - h, st + h));
            }
            if (alt)
            {
                var sinir = new List<double> { chain[0].X0 };
                sinir.AddRange(mesnetler);
                sinir.Add(chain[m - 1].X1);
                sinir.Sort();
                for (int i = 1; i < sinir.Count; i++)
                {
                    double L = sinir[i] - sinir[i - 1];
                    if (L > 30.0) yasak.Add((sinir[i - 1] + L / 3.0, sinir[i] - L / 3.0));
                }
            }
            bool Serbest(double s, double e)
            {
                var p = KirisEgimliAltYol(yol, s, e);
                if (p.Count < 2) return false;
                double xl = p.Min(q => q.X), xh = p.Max(q => q.X);
                foreach (var y in yasak) if (xh > y.Lo && xl < y.Hi) return false;
                return true;
            }
            var ekler = new List<(double S, double E, bool K)>();
            double u0 = 0;
            for (int guard = 0; guard < 20 && toplam - u0 > limit + 0.01; guard++)
            {
                double eMax = u0 + limit, e = eMax;
                bool bulundu = false;
                for (double c = eMax; c >= u0 + lo + 50.0; c -= 5.0)
                    if (Serbest(c - lo, c)) { e = c; bulundu = true; break; }
                ekler.Add((e - lo, e, !bulundu && alt));
                u0 = e - lo;
            }
            var sonuc = new List<(List<Point2d> Yol, double EkBoy, bool Kaynak)>();
            for (int k = 0; k <= ekler.Count; k++)
            {
                double s = k == 0 ? 0 : ekler[k - 1].S;
                double e = k == ekler.Count ? toplam : ekler[k].E;
                sonuc.Add((KirisEgimliAltYol(yol, s, e), k == 0 ? 0 : lo, k > 0 && ekler[k - 1].K));
            }
            return sonuc;
        }

        /// <summary>Çubuğu (gerekirse bindirmeli parçalarla) çizer; kaydet ise açılıma ve kesit sayımına ekler.</summary>
        private void KirisEgimliCubukCizEkli(Transaction tr, BlockTableRecord ms, List<KirisAcilimSpan> chain, KirisAcilimSupport[] sup,
            List<Point2d> yol, double x0, double yShift, short renk, int adet, int cap, string tur, double ofs, bool alt,
            string kesitTur, bool kaydet = true)
        {
            if (yol == null || yol.Count < 2) return;
            var parcalar = KirisEgimliEkBol(chain, sup, yol, KirisAcilimEkBoyu(cap), alt);
            if (parcalar == null)
            {
                if (kaydet) KirisEgimliCubukCiz(tr, ms, yol, x0, yShift, renk, adet, cap, tur, ofs);
                else KirisAcilimDonatiPl(tr, ms, renk, KirisEgimliDunya(yol, x0, yShift), egikGonye: true);
                return;
            }
            for (int k = 0; k < parcalar.Count; k++)
            {
                var p = parcalar[k].Yol;
                if (p.Count < 2) continue;
                // Bindirmede çubuklar üst üste görünsün: tek sıradaki parça 2.5 cm yukarıda.
                var w = KirisEgimliDunya(p, x0, yShift + (k % 2 == 1 ? KirisAcilimCubukAraCm : 0.0));
                double boy = KirisAcilimYuvarlaYukari(KirisAcilimDonatiPl(tr, ms, renk, w, egikGonye: true) - 0.01);
                if (!kaydet || boy < 1.0) continue;
                _kirisEgimliCubuklar.Add(new KirisEgimliCubuk
                {
                    Yol = w, Adet = adet, Cap = cap, Boy = boy, Tur = tur, Ofs = ofs,
                    Kay = k % 2 == 1 ? 6.0 : 0.0, EkBoy = parcalar[k].EkBoy, EkKaynak = parcalar[k].Kaynak
                });
                // Kesit sayımı: bindirme bölgesinde ikinci çubuk (kesit istasyonu buradan kaçar).
                if (kesitTur != null && parcalar[k].EkBoy > 0)
                    KirisAcilimKesitDonatiKaydet(KirisEgimliAltYol(w, 0, parcalar[k].EkBoy), adet, cap, kesitTur);
            }
        }

        /// <summary>Çubuk etiketi: açıklık üzerinde x'te, d ofsetli doğrunun dikine kaydırılmış ve eğime döndürülmüş.</summary>
        private void KirisEgimliEtiket(Transaction tr, BlockTableRecord ms, Database db, KirisAcilimSpan s,
            double x, double d, double dikKay, string text, double x0, double yShift, double yaziH = 5.0, string layer = null)
        {
            double t = KirisEgimTan(s), sec = Math.Sqrt(1.0 + t * t);
            double y = KirisEgimliY(s, d, x);
            DrawBeamLabel(tr, ms, db,
                new Point3d(x0 + x - t / sec * dikKay, yShift + y + dikKay / sec, 0),
                text, yaziH, Math.Atan(t), layer ?? KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
        }

        /// <summary>
        /// Eğime paralel, sol-alt hizalı yazı: önce yatay çizilip genişliği ölçülür, sonra yerel x'teki üst yüz noktasının
        /// "dik" kadar dışına taşınıp döndürülür. bitisX verilirse yazı o x'te biter. Dönüş: yazının eksen boyunca genişliği.
        /// </summary>
        private double KirisEgimliYuzYazi(Transaction tr, BlockTableRecord ms, Database db, KirisAcilimSpan s,
            double x, double? bitisX, double dik, string text, double yaziH, string layer, double x0, double yShift, bool ciz = true)
        {
            double t = KirisEgimTan(s), sec = Math.Sqrt(1.0 + t * t);
            var gecici = new Point3d(x0 + x, yShift + KirisEgimliY(s, 0, x), 0);
            DrawBeamLabel(tr, ms, db, gecici, text, yaziH, 0.0, layer, bottomLeftAligned: true, useMiddleCenter: false);
            if (_lastBeamLabelId.IsNull) return 0;
            var txt = tr.GetObject(_lastBeamLabelId, OpenMode.ForWrite) as DBText;
            if (txt == null) return 0;
            double w;
            try { var ext = txt.GeometricExtents; w = ext.MaxPoint.X - ext.MinPoint.X; }
            catch { w = text.Length * yaziH * 0.75; }
            if (!ciz) { txt.Erase(); return w; }
            double xBas = bitisX.HasValue ? bitisX.Value - w / sec : x;
            var hedef = new Point3d(x0 + xBas - t / sec * dik, yShift + KirisEgimliY(s, 0, xBas) + dik / sec, 0);
            txt.TransformBy(Matrix3d.Displacement(hedef - gecici));
            txt.TransformBy(Matrix3d.Rotation(Math.Atan(t), Vector3d.ZAxis, hedef));
            return w;
        }

        /// <summary>
        /// Kiriş başlığı alçak uca yakın (Ln/10), eğim yüzdesi onun yüksek uç tarafında; sığmazsa kiriş altında.
        /// Mahya kotunun yazısıyla çakışmasın diye kırığa 20 cm'den fazla yaklaşmaz.
        /// </summary>
        private void KirisEgimliBaslik(Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, int i0, int i1, double net0, double net1, double x0, double yShift, string ad)
        {
            double ln = net1 - net0;
            bool yukselen = KirisEgimTan(chain[i0]) >= -1e-6;
            var s = yukselen ? chain[i0] : chain[i1];
            double t = KirisEgimTan(s), sec = Math.Sqrt(1.0 + t * t);
            const double dik = 3.0, ara = 8.0, egimH = 6.0;
            if (KirisEgimliDuzMu(s))
            {
                // Karışık zincirin düz kolu: başlık düz kirişteki yerinde (Ln/10), eğim yazılmaz.
                KirisEgimliYuzYazi(tr, ms, db, s, net0 + 0.1 * ln, null, dik, ad, KirisAcilimBaslikYukseklikCm, LayerKirisYazisi, x0, yShift);
                return;
            }
            string egim = "%" + (Math.Abs(t) * 100.0).ToString("0.#", CultureInfo.GetCultureInfo("tr-TR"));
            double wE = KirisEgimliYuzYazi(tr, ms, db, s, net0, null, dik, egim, egimH, LayerKirisYazisi, x0, yShift, ciz: false);
            double wB;
            bool sigdi;
            if (yukselen)
            {
                double xB = net0 + 0.1 * ln;
                wB = KirisEgimliYuzYazi(tr, ms, db, s, xB, null, dik, ad, KirisAcilimBaslikYukseklikCm, LayerKirisYazisi, x0, yShift);
                double xE = xB + (wB + ara) / sec;
                sigdi = xE + wE / sec <= net1 - 20.0;
                if (sigdi) KirisEgimliYuzYazi(tr, ms, db, s, xE, null, dik, egim, egimH, LayerKirisYazisi, x0, yShift);
            }
            else
            {
                double xSon = net1 - 0.1 * ln;
                wB = KirisEgimliYuzYazi(tr, ms, db, s, xSon, xSon, dik, ad, KirisAcilimBaslikYukseklikCm, LayerKirisYazisi, x0, yShift);
                double xESon = xSon - (wB + ara) / sec;
                sigdi = xESon - wE / sec >= net0 + 20.0;
                if (sigdi) KirisEgimliYuzYazi(tr, ms, db, s, xESon, xESon, dik, egim, egimH, LayerKirisYazisi, x0, yShift);
            }
            if (!sigdi)
                KirisEgimliEtiket(tr, ms, db, s, Math.Max(s.X0, net0) + 0.5 * (Math.Min(s.X1, net1) - Math.Max(s.X0, net0)),
                    s.Beam.HeightCm, -17.0, egim, x0, yShift, egimH, LayerKirisYazisi);
        }

        /// <summary>Mesnetler arası net eksen boyu (i açıklığını içeren); ilave boyu ln/4 için.</summary>
        private static double KirisEgimliLn(List<KirisAcilimSpan> chain, KirisAcilimSupport[] sup, int i)
        {
            int a = i, b = i + 1;
            while (a > 0 && !KirisEgimliMesnet(sup, a)) a--;
            while (b < chain.Count && !KirisEgimliMesnet(sup, b)) b++;
            double xa = KirisEgimliIstasyon(chain, a), xb = KirisEgimliIstasyon(chain, b);
            // Net boy, eksene dik mesnet hatları arasında (eksen derinliğinde).
            if (KirisEgimliMesnet(sup, a)) xa = KirisEgimliYuzX(chain[a], xa + sup[a].X1, 0.5 * chain[a].Beam.HeightCm, true);
            if (KirisEgimliMesnet(sup, b)) xb = KirisEgimliYuzX(chain[b - 1], xb + sup[b].X0, 0.5 * chain[b - 1].Beam.HeightCm, false);
            double ln = 0;
            for (int k = a; k < b && k < chain.Count; k++)
            {
                double lo = Math.Max(xa, chain[k].X0), hi = Math.Min(xb, chain[k].X1);
                if (hi > lo) ln += (hi - lo) * KirisEgimCarpan(chain[k]);
            }
            return ln;
        }

        private static void KirisEgimliIlaveUc(KirisAcilimSpan s, KirisAcilimMontajBilgi m, bool solUc, bool ust, out int adet, out int cap)
        {
            bool bas = solUc ? s.Cross0 == s.Beam.StartAxisId : s.Cross1 == s.Beam.StartAxisId;
            if (bas) { adet = ust ? m.SolUstAdet : m.SolAltAdet; cap = ust ? m.SolUstCap : m.SolAltCap; }
            else { adet = ust ? m.SagUstAdet : m.SagAltAdet; cap = ust ? m.SagUstCap : m.SagAltCap; }
        }

        /// <summary>Açılım satırları için kolon kesiminin altında ayrılacak yükseklik (cm).</summary>
        private static double KirisEgimliAcilimYer(List<KirisAcilimSpan> chain)
        {
            double minK = chain.Min(s => Math.Min(s.Kot0, s.Kot1)), maxK = chain.Max(s => Math.Max(s.Kot0, s.Kot1));
            double sec = chain.Max(s => KirisEgimCarpan(s));
            // 5 tür satırı; çubuklar kirişin düşey yayılımı + kanca kadar yer kaplar.
            return KirisEgimliAcilimIlkCm + 4 * KirisEgimliAcilimAdimCm + (maxK - minK) + chain[0].Beam.HeightCm * sec + 30.0;
        }

        private static int KirisEgimliAcilimSatir(string tur)
        {
            switch (tur)
            {
                case "ÜST İLAVE": return 0;
                case "MONTAJ": return 1;
                case "GÖVDE": return 2;
                case "ALT": return 3;
                default: return 4;
            }
        }

        /// <summary>Zincirin tüm boyuna donatısı, kırık etriyeleri ve açılım satırları.</summary>
        private void KirisEgimliDonati(Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, double x0, double yShift, KirisAcilimSupport[] sup,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj)
        {
            _kirisEgimliCubuklar.Clear();
            if (montaj == null) return;
            int m = chain.Count;
            double h = chain[0].Beam.HeightCm;
            double pas = KirisAcilimMontajPaspayiCm;
            var mahya = new bool[m + 1];
            var dere = new bool[m + 1];
            for (int j = 1; j < m; j++)
            {
                double ta = KirisEgimTan(chain[j - 1]), tb = KirisEgimTan(chain[j]);
                mahya[j] = ta > tb + 1e-3;
                dere[j] = ta < tb - 1e-3;
            }
            double solSerbest = chain[0].X0 + pas, sagSerbest = chain[m - 1].X1 - pas;
            int solTip = KirisEgimliMesnet(sup, 0) ? 1 : 0, sagTip = KirisEgimliMesnet(sup, m) ? 1 : 0;

            void CaprazKaydet(int i, bool sol, double d, double phi, double lb, int adet, int cap)
            {
                var uc = new List<Point2d>();
                KirisEgimliUc(chain, sup, i, sol, d, h, phi, lb, 2, 0, true, 0, uc);
                Point2d kose = KirisEgimliKose(chain, sol ? i - 1 : i, d);
                uc.Add(kose);
                KirisAcilimKesitDonatiKaydet(KirisEgimliDunya(uc, x0, yShift), adet, cap, uc[0].Y > kose.Y ? "ilaU" : "ilaA");
            }

            var montajEtiketleri = new List<(KirisAcilimSpan S, string Lbl, double D)>();
            var ustIlaveX = new List<(double Lo, double Hi)>();
            // Montaj (üst) ve düz (alt): içbükey kaldığı kırıkta kesilir ve çaprazlanır.
            foreach (bool ust in new[] { true, false })
            {
                int a = 0;
                while (a < m)
                {
                    int b = a;
                    while (b + 1 < m && !(ust ? dere[b + 1] : mahya[b + 1])) b++;
                    int adet = 0, cap = 0;
                    for (int i = a; i <= b; i++)
                    {
                        if (!montaj.TryGetValue(chain[i].Beam, out var mm)) continue;
                        int n = ust ? mm.Adet : mm.DuzAdet, c = ust ? mm.Cap : mm.DuzCap;
                        if (n > 0 && c > 0 && n * c * c > adet * cap * cap) { adet = n; cap = c; }
                    }
                    if (adet > 0)
                    {
                        double phi = cap / 10.0;
                        double lb = KirisAcilimYuvarlaYukari((ust ? _lbPhiKonumI : _lbPhiKonumII) * phi);
                        double d = ust ? pas : h - pas;
                        int tL = a == 0 ? solTip : 2, tR = b == m - 1 ? sagTip : 2;
                        var yol = KirisEgimliYol(chain, sup, a, b, d, h, phi, lb, tL, solSerbest, tR, sagSerbest, true);
                        KirisEgimliCubukCizEkli(tr, ms, chain, sup, yol, x0, yShift, ust ? KirisAcilimRenkMontaj : KirisAcilimRenkDuz,
                            adet, cap, ust ? "MONTAJ" : "ALT", d * Math.Sqrt(1.0 + KirisEgimTan(chain[a]) * KirisEgimTan(chain[a])), !ust, ust ? "mon" : "duz");
                        // Kesit: çubuğun kendi kollarındaki kısmı; çapraz uzantı ayrı (kesit istasyonu ondan kaçar).
                        var ana = KirisEgimliYol(chain, sup, a, b, d, h, phi, lb,
                            tL == 2 ? 0 : tL, tL == 2 ? KirisEgimliKose(chain, a - 1, d).X : solSerbest,
                            tR == 2 ? 0 : tR, tR == 2 ? KirisEgimliKose(chain, b, d).X : sagSerbest, true);
                        KirisAcilimKesitDonatiKaydet(KirisEgimliDunya(ana, x0, yShift), adet, cap, ust ? "mon" : "duz");
                        if (tL == 2) CaprazKaydet(a, true, d, phi, lb, adet, cap);
                        if (tR == 2) CaprazKaydet(b, false, d, phi, lb, adet, cap);
                        string lbl = adet.ToString(CultureInfo.InvariantCulture) + "ø" + cap.ToString(CultureInfo.InvariantCulture) + (ust ? " (mon)" : " (duz)");
                        for (int i = a; i <= b; i++)
                        {
                            if (i > a && chain[i].Beam.BeamId == chain[i - 1].Beam.BeamId) continue;
                            var s = chain[i];
                            // Montaj etiketi düz kirişteki gibi çubuğun altında (kiriş içinde); yeri üst ilavelerden sonra seçilir.
                            if (ust) { montajEtiketleri.Add((s, lbl, d)); continue; }
                            KirisEgimliEtiket(tr, ms, db, s, s.X0 + (s.X1 - s.X0) * 0.38, d, 4.5, lbl, x0, yShift);
                        }
                    }
                    a = b + 1;
                }
            }

            // Gövde: konstrüktif yüz donatısı; kırıklardan bükülerek tek çubuk geçer (iki kolun gövdeleri kesişmez), 1200'ü aşarsa eklenir.
            {
                int a = 0;
                while (a < m)
                {
                    int b = m - 1;
                    int adet = 0, cap = 0;
                    for (int i = a; i <= b; i++)
                    {
                        if (!montaj.TryGetValue(chain[i].Beam, out var mm)) continue;
                        if (mm.GovdeAdet > 0 && mm.GovdeCap > 0 && mm.GovdeAdet * mm.GovdeCap * mm.GovdeCap > adet * cap * cap)
                        { adet = mm.GovdeAdet; cap = mm.GovdeCap; }
                    }
                    if (adet > 0)
                    {
                        double phi = cap / 10.0;
                        double lb = KirisAcilimYuvarlaYukari(_lbPhiKonumII * phi);
                        int gorunen = Math.Max(1, (adet + 1) / 2);
                        int tL = a == 0 ? solTip : 2, tR = b == m - 1 ? sagTip : 2;
                        for (int k = 1; k <= gorunen; k++)
                        {
                            double d = pas + (h - 2.0 * pas) * k / (gorunen + 1.0);
                            var yol = KirisEgimliYol(chain, sup, a, b, d, h, phi, lb, tL, solSerbest, tR, sagSerbest, false);
                            KirisEgimliCubukCizEkli(tr, ms, chain, sup, yol, x0, yShift, KirisAcilimRenkGovde, adet, cap, "GÖVDE",
                                d * Math.Sqrt(1.0 + KirisEgimTan(chain[a]) * KirisEgimTan(chain[a])), false, "gov", kaydet: k == 1);
                            if (k == 1) KirisAcilimKesitDonatiKaydet(KirisEgimliDunya(yol, x0, yShift), adet, cap, "gov");
                        }
                        string lbl = adet.ToString(CultureInfo.InvariantCulture) + "ø" + cap.ToString(CultureInfo.InvariantCulture) + " (gov)";
                        int orta = Math.Max(1, (gorunen + 1) / 2);
                        double dOrta = pas + (h - 2.0 * pas) * orta / (gorunen + 1.0);
                        for (int i = a; i <= b; i++)
                        {
                            if (i > a && chain[i].Beam.BeamId == chain[i - 1].Beam.BeamId) continue;
                            var s = chain[i];
                            KirisEgimliEtiket(tr, ms, db, s, 0.5 * (s.X0 + s.X1), dOrta, 4.5, lbl, x0, yShift);
                        }
                    }
                    a = b + 1;
                }
            }

            // İlaveler: mesnet ve kırık düğümlerinde, iki yandaki kirişin o uçtaki ilavesinden As'si büyük olan.
            double ayir = KirisAcilimIlaveAyirCm;
            for (int j = 0; j <= m; j++)
            {
                var L = j > 0 ? chain[j - 1] : null;
                var R = j < m ? chain[j] : null;
                if (j > 0 && j < m && !KirisEgimliMesnet(sup, j) && !mahya[j] && !dere[j]) continue;
                if ((j == 0 || j == m) && !KirisEgimliMesnet(sup, j)) continue;
                foreach (bool ust in new[] { true, false })
                {
                    int adet = 0, cap = 0;
                    if (L != null && montaj.TryGetValue(L.Beam, out var mL))
                    {
                        KirisEgimliIlaveUc(L, mL, false, ust, out int n, out int c);
                        if (n > 0 && c > 0) { adet = n; cap = c; }
                    }
                    if (R != null && montaj.TryGetValue(R.Beam, out var mR))
                    {
                        KirisEgimliIlaveUc(R, mR, true, ust, out int n, out int c);
                        if (n > 0 && c > 0 && n * c * c > adet * cap * cap) { adet = n; cap = c; }
                    }
                    if (adet <= 0) continue;
                    double phi = cap / 10.0;
                    double lb = KirisAcilimYuvarlaYukari((ust ? _lbPhiKonumI : _lbPhiKonumII) * phi);
                    double d = ust ? pas + ayir : h - pas - ayir;
                    double station = KirisEgimliIstasyon(chain, j);
                    // Mesnet yüzü: birleşim köşesinden eksene dik hat (bu çubuğun derinliğinde).
                    double yuzSol = station, yuzSag = station;
                    if (KirisEgimliMesnet(sup, j))
                    {
                        if (L != null) yuzSol = KirisEgimliYuzX(L, station + sup[j].X0, d, false);
                        if (R != null) yuzSag = KirisEgimliYuzX(R, station + sup[j].X1, d, true);
                    }
                    // Yüzden açıklığa ln/4 (TBDY 7.4.3.1a), en az ℓb; eksen boyunca ölçülür, açıklığın dışına çıkmaz.
                    double xa = 0, xb = 0;
                    if (L != null)
                    {
                        double c = KirisAcilimYuvarlaYukari(Math.Max(KirisEgimliLn(chain, sup, j - 1) * KirisAcilimUstSurekliOran, lb));
                        xa = Math.Max(L.X0 + 5.0, yuzSol - c / KirisEgimCarpan(L));
                    }
                    if (R != null)
                    {
                        double c = KirisAcilimYuvarlaYukari(Math.Max(KirisEgimliLn(chain, sup, j) * KirisAcilimUstSurekliOran, lb));
                        xb = Math.Min(R.X1 - 5.0, yuzSag + c / KirisEgimCarpan(R));
                    }
                    short renk = ust ? KirisAcilimRenkIlaveUst : KirisAcilimRenkIlaveAlt;
                    string tur = ust ? "ÜST İLAVE" : "ALT İLAVE";
                    string kesitTur = ust ? "ilaU" : "ilaA";
                    string lbl = adet.ToString(CultureInfo.InvariantCulture) + "ø" + cap.ToString(CultureInfo.InvariantCulture) + " (ila.)";
                    var yollar = new List<(List<Point2d> Yol, KirisAcilimSpan S, double X)>();
                    if (j == 0)
                        yollar.Add((KirisEgimliYol(chain, sup, 0, 0, d, h, phi, lb, 1, 0, 0, xb, true, ayir), R, 0.5 * (yuzSag + xb)));
                    else if (j == m)
                        yollar.Add((KirisEgimliYol(chain, sup, m - 1, m - 1, d, h, phi, lb, 0, xa, 1, 0, true, ayir), L, 0.5 * (xa + yuzSol)));
                    else if (ust ? dere[j] : mahya[j])
                    {
                        yollar.Add((KirisEgimliYol(chain, sup, j - 1, j - 1, d, h, phi, lb, 0, xa, 2, 0, true), L, 0.5 * (xa + yuzSol)));
                        yollar.Add((KirisEgimliYol(chain, sup, j, j, d, h, phi, lb, 2, 0, 0, xb, true), R, 0.5 * (yuzSag + xb)));
                    }
                    else
                        yollar.Add((KirisEgimliYol(chain, sup, j - 1, j, d, h, phi, lb, 0, xa, 0, xb, true), L, 0.5 * (xa + yuzSol)));
                    foreach (var y in yollar)
                    {
                        double tY = KirisEgimTan(y.S);
                        KirisEgimliCubukCiz(tr, ms, y.Yol, x0, yShift, renk, adet, cap, tur, d * Math.Sqrt(1.0 + tY * tY));
                        KirisAcilimKesitDonatiKaydet(KirisEgimliDunya(y.Yol, x0, yShift), adet, cap, kesitTur);
                        KirisEgimliEtiket(tr, ms, db, y.S, y.X, d, ust ? -4.5 : 4.5, lbl, x0, yShift);
                        if (ust) ustIlaveX.Add((y.Yol.Min(p => p.X), y.Yol.Max(p => p.X)));
                    }
                }
            }

            // Montaj etiketi: açıklıkta üst ilavesiz en uzun bölgenin ortası (ilave etiketine binmesin).
            foreach (var me in montajEtiketleri)
            {
                var s = me.S;
                double lo = s.X0 + 5.0, hi = s.X1 - 5.0;
                var dolu = ustIlaveX.Where(r => r.Hi > lo && r.Lo < hi).OrderBy(r => r.Lo).ToList();
                double enIyiA = lo, enIyiB = hi, enUzun = -1, cur = lo;
                foreach (var r in dolu)
                {
                    if (r.Lo - 3.0 - cur > enUzun) { enUzun = r.Lo - 3.0 - cur; enIyiA = cur; enIyiB = r.Lo - 3.0; }
                    cur = Math.Max(cur, r.Hi + 3.0);
                }
                if (hi - cur > enUzun) { enIyiA = cur; enIyiB = hi; }
                KirisEgimliEtiket(tr, ms, db, s, 0.5 * (enIyiA + enIyiB), me.D, -4.5, me.Lbl, x0, yShift);
            }

            // Mesnetsiz kırıkta açıortay etriyesi.
            double pe = KirisAcilimEtriyePaspayiCm;
            for (int j = 1; j < m; j++)
            {
                if (KirisEgimliMesnet(sup, j) || (!mahya[j] && !dere[j])) continue;
                Point2d u = KirisEgimliKose(chain, j - 1, pe), v = KirisEgimliKose(chain, j - 1, h - pe);
                KirisAcilimCizgi(tr, ms, x0 + u.X, yShift + u.Y, x0 + v.X, yShift + v.Y, LayerEtriye);
            }

            KirisEgimliAcilimlariCiz(tr, ms, db);
        }

        /// <summary>
        /// Açılım satırları: her çubuk görünüşteki X'inde ve kendi biçiminde, kolon kesiminin altına sırayla iner.
        /// En uzun kolun üstünde "nøφ L=… TÜR", her kolun altında kol boyu yazılır.
        /// </summary>
        private void KirisEgimliAcilimlariCiz(Transaction tr, BlockTableRecord ms, Database db)
        {
            if (_kirisAcilimKesimY > 1e8 || db == null) return;
            if (_kirisEgimliCubuklar.Count == 0) return;
            double yUst = _kirisAcilimKesimY - KirisEgimliAcilimIlkCm;
            // Tür satırları (yukarıdan): üst ilave, montaj, gövde, alt, alt ilave. Her çubuk kendi derinliği kadar yukarı
            // alınıp (Ofs) üst yüz hizasına getirilir, sonra satırı kadar iner: satırlar arası sabit ve sık, sol ve sağ
            // koldaki eşler aynı satırda, aynı koldakiler paralel. Kancalar düz kirişteki gibi 30° dışa yatırılır.
            double ustRef = _kirisEgimliCubuklar.Max(c => c.Yol.Max(p => p.Y) + c.Ofs);
            ObjectId ekStil = GetOrCreatePlanOlcuDimStyle(tr, db, 10.0, 1.0, "PLAN_OLCU_25");
            for (int k = 0; k < _kirisEgimliCubuklar.Count; k++)
            {
                var c = _kirisEgimliCubuklar[k];
                double dy = yUst - KirisEgimliAcilimSatir(c.Tur) * KirisEgimliAcilimAdimCm - c.Kay - ustRef + c.Ofs;
                var ac = c.Yol.Select(p => new Point2d(p.X, p.Y + dy)).ToList();
                int ana = 0;
                double enUzun = -1;
                for (int i = 0; i + 1 < ac.Count; i++)
                {
                    double len = ac[i].GetDistanceTo(ac[i + 1]);
                    if (len > enUzun) { enUzun = len; ana = i; }
                }
                // Bindirme ölçüsü: parçanın başındaki ℓo, kancalar yatırılmadan önceki yol üzerinde.
                if (c.EkBoy > 0)
                {
                    var bind = KirisEgimliAltYol(ac, 0, c.EkBoy);
                    if (bind.Count >= 2)
                    {
                        Point2d pA = bind[0], pB = bind[bind.Count - 1];
                        Vector2d u = (pB - pA).GetNormal(), n = new Vector2d(u.Y, -u.X);
                        if (n.Y > 0) n = n.Negate();
                        var dimP = pA + (pB - pA) * 0.5 + n * 9.0;
                        AppendEntity(tr, ms, new AlignedDimension(new Point3d(pA.X, pA.Y, 0), new Point3d(pB.X, pB.Y, 0),
                            new Point3d(dimP.X, dimP.Y, 0), "", ekStil) { Layer = LayerOlcu, Dimexo = 0.0, Dimse1 = false, Dimse2 = false });
                    }
                }
                KirisAcilimGonyeOtuzDerece(ac, ana);
                KirisAcilimDonatiPl(tr, ms, 0, ac, egikGonye: true);
                for (int i = 0; i + 1 < ac.Count; i++)
                {
                    Point2d a = ac[i], b = ac[i + 1];
                    double len = a.GetDistanceTo(b);
                    if (len < 8.0) continue;
                    if (b.X < a.X - 0.01 || (Math.Abs(b.X - a.X) <= 0.01 && b.Y < a.Y)) { var tmp = a; a = b; b = tmp; }
                    double ang = Math.Atan2(b.Y - a.Y, b.X - a.X);
                    double nx = -Math.Sin(ang), ny = Math.Cos(ang);
                    double mx = 0.5 * (a.X + b.X), my = 0.5 * (a.Y + b.Y);
                    if (ac.Count > 2)
                        DrawBeamLabel(tr, ms, db, new Point3d(mx - nx * 4.5, my - ny * 4.5, 0),
                            Math.Round(len).ToString("0", CultureInfo.InvariantCulture), 5.0, ang, KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
                    if (i != ana) continue;
                    string lbl = c.Adet.ToString(CultureInfo.InvariantCulture) + "ø" + c.Cap.ToString(CultureInfo.InvariantCulture)
                        + " L=" + c.Boy.ToString("0", CultureInfo.InvariantCulture) + " " + c.Tur
                        + (c.EkBoy > 0 ? " (EK lo=" + c.EkBoy.ToString("0", CultureInfo.InvariantCulture) + (c.EkKaynak ? " KAYNAKLI" : "") + ")" : "");
                    DrawBeamLabel(tr, ms, db, new Point3d(mx + nx * 4.5, my + ny * 4.5, 0), lbl, 5.0, ang, KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
                }
            }
        }

        /// <summary>
        /// Eğimli açıklıkta temsili etriyeler: eksene dik, aralık eksen boyunca (sarılma 2h). Etiket kiriş altında, eğime paralel.
        /// </summary>
        private void KirisEgimliEtriye(Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, int i0, int i1, double x0, double yShift, double net0, double net1,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> donati, KirisAcilimSupport[] sup)
        {
            double h = chain[i0].Beam.HeightCm;
            if (h <= 8.0) return;
            double phiMin = double.MaxValue, raporSik = double.MaxValue, raporOrta = double.MaxValue;
            int etriyeCap = 0;
            void Cap(int adet, int cap) { if (adet > 0 && cap > 0) phiMin = Math.Min(phiMin, cap / 10.0); }
            for (int i = i0; i <= i1; i++)
            {
                if (donati == null || !donati.TryGetValue(chain[i].Beam, out var m)) continue;
                if (m.EtriyeCap > 0 && m.EtriyeOrtaCm > 0)
                {
                    etriyeCap = m.EtriyeCap;
                    raporOrta = Math.Min(raporOrta, m.EtriyeOrtaCm);
                    raporSik = Math.Min(raporSik, m.EtriyeSikCm > 0 ? m.EtriyeSikCm : m.EtriyeOrtaCm);
                }
                Cap(m.Adet, m.Cap); Cap(m.DuzAdet, m.DuzCap); Cap(m.GovdeAdet, m.GovdeCap);
                Cap(m.SolUstAdet, m.SolUstCap); Cap(m.SagUstAdet, m.SagUstCap);
                Cap(m.SolAltAdet, m.SolAltCap); Cap(m.SagAltAdet, m.SagAltCap);
            }
            if (phiMin == double.MaxValue) phiMin = 1.2;
            double dFay = h - KirisAcilimDuzPaspayiCm;
            double sik = raporSik < double.MaxValue ? raporSik : Math.Floor(Math.Min(dFay / 4.0, Math.Min(8.0 * phiMin, 15.0)));
            double orta = raporOrta < double.MaxValue ? raporOrta : Math.Floor(Math.Min(dFay / 2.0, 20.0));
            // Eğimli kirişte sıklaştırma kiriş boyunca: orta bölge de sık aralıkla.
            orta = sik;
            if (sik <= 0 || orta <= 0) return;
            double pe = KirisAcilimEtriyePaspayiCm;
            for (int i = i0; i <= i1; i++)
            {
                var s = chain[i];
                double a = Math.Max(net0, s.X0), b = Math.Min(net1, s.X1);
                if (b - a <= 10.0) continue;
                double t = KirisEgimTan(s), sec = Math.Sqrt(1.0 + t * t);
                // Mesnetsiz kırık ucunda dik etriye açıortayı geçmesin: eksen boyu (h/2)·tan(θ/2) kısalır.
                double Kisalt(int j)
                {
                    if (j <= 0 || j >= chain.Count || KirisEgimliMesnet(sup, j)) return 0.0;
                    double teta = Math.Atan(KirisEgimTan(chain[j - 1])) - Math.Atan(KirisEgimTan(chain[j]));
                    return 0.5 * h * Math.Abs(Math.Tan(0.5 * teta));
                }
                double offBas = a <= s.X0 + 0.5 ? Kisalt(i) : 0.0, offSon = b >= s.X1 - 0.5 ? Kisalt(i + 1) : 0.0;
                a += offBas / sec;
                double boyEksen = (b - a) * sec - offSon;
                if (boyEksen <= 10.0) continue;
                var xs = KirisAcilimEtriyeKonumlari(0, boyEksen, sik, orta, 2.0 * h, 2.0 * h, false, false,
                    new List<(double Lo, double Hi)>(), out var sikBolgeleri);
                sikBolgeleri.Clear();
                double yari = 0.5 * h - pe;
                foreach (double xe in xs)
                {
                    double x = a + xe / sec;
                    double y = KirisEgimliY(s, 0.5 * h, x);
                    KirisAcilimCizgi(tr, ms,
                        x0 + x - t / sec * yari, yShift + y + yari / sec,
                        x0 + x + t / sec * yari, yShift + y - yari / sec, LayerEtriye);
                }
                if (etriyeCap <= 0 || db == null) continue;
                var kutular = KirisKesitEtriyeKutulari(s.Beam.WidthCm);
                if (kutular.Count == 0) continue;
                double boy = EtriyeAcilimKesimBoyCm(kutular[0].Hi - kutular[0].Lo, h - 8.0, Math.Max(10.0, EtriyeAcilimKancaCm(etriyeCap)));
                foreach (var bolge in KirisAcilimEtriyeEtiketBolgeleri(5.0, boyEksen - 5.0, sik, orta, sikBolgeleri))
                {
                    string label = KirisKesitEtriyeCarpan(kutular.Count)
                        + string.Format(CultureInfo.InvariantCulture, "{0}ø{1}/{2:0.##} L={3:0}", bolge.Adet, etriyeCap, bolge.Aralik, boy);
                    KirisEgimliEtiket(tr, ms, db, s, a + 0.5 * (bolge.Lo + bolge.Hi) / sec, h, -7.0, label, x0, yShift);
                }
            }
        }
    }
}
