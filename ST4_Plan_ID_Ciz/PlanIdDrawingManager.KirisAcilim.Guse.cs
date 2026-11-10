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
    /// KIRISACILIMLARI guseli kiriş: ST4 Beams Data p[8] Hg, p[9] La, p[10] Lb (cm; boy mesnet yüzünden).
    /// Kiriş altı mesnet yüzünde Hg kadar aşağıda başlar, guse boyunca doğrusal olarak normal kiriş altına çıkar.
    /// Ana alt donatı düz kalır; guse donatısı eğik yüze paralel, kolona gönyeyle girer ve kırılma noktasında
    /// bükülmeden (içbükey köşe) düz devam ederek kirişe kenetlenir (STA4CAD düzeni).
    /// Guse yoksa liste boştur ve hiçbir çizim değişmez.
    /// </summary>
    public sealed partial class PlanIdDrawingManager
    {
        private struct KirisGuseBolge
        {
            /// <summary>Zincirdeki açıklık indeksi.</summary>
            public int Span;
            /// <summary>true: guse açıklığın sol (X0) ucunda.</summary>
            public bool Sol;
            /// <summary>Mesnet yüzü (zincir X).</summary>
            public double XFace;
            /// <summary>Guse bitimi, kırılma noktası (zincir X).</summary>
            public double XEnd;
            /// <summary>Mesnedin kirişe uzak kenarı (zincir X); mesnet yoksa XFace.</summary>
            public double XKolonDis;
            public double Hg;
            /// <summary>Mesnet kolonu kiriş altından aşağı devam ediyor.</summary>
            public bool KolonAsagi;
        }

        private readonly List<KirisGuseBolge> _kirisGuse = new List<KirisGuseBolge>();
        /// <summary>Guse donatısı açılımı, en alttaki kolon kesiminin bu kadar altında (cm).</summary>
        private const double KirisAcilimGuseAcilimCm = 185.0;
        /// <summary>Guse açılım satırı için şeridin altında ayrılan ek yer (cm).</summary>
        private const double KirisAcilimGuseEkYerCm = 30.0;

        private static bool KirisAcilimGuseVar(List<KirisAcilimSpan> chain)
        {
            if (chain == null) return false;
            foreach (var s in chain)
                if (s.GuseHg > 0 && (s.GuseL0 > 0 || s.GuseL1 > 0)) return true;
            return false;
        }

        private void KirisAcilimGuseHazirla(List<KirisAcilimSpan> chain, KirisAcilimSupport[] sup)
        {
            _kirisGuse.Clear();
            if (_surekliTemelAcilim || chain == null || sup == null) return;
            int m = chain.Count;
            for (int k = 0; k < m; k++)
            {
                var s = chain[k];
                if (s.GuseHg <= 0) continue;
                double boy = s.X1 - s.X0;
                if (s.GuseL0 > 0)
                {
                    bool kolon = k < sup.Length && sup[k].X1 - sup[k].X0 > 2.0;
                    double face = kolon ? s.X0 + sup[k].X1 : s.X0;
                    double dis = kolon ? s.X0 + sup[k].X0 : face;
                    double end = Math.Min(face + s.GuseL0, s.X1);
                    if (end - face > 5.0 && boy > 5.0)
                        _kirisGuse.Add(new KirisGuseBolge { Span = k, Sol = true, XFace = face, XEnd = end, XKolonDis = dis, Hg = s.GuseHg, KolonAsagi = kolon && sup[k].ExtendDown });
                }
                if (s.GuseL1 > 0)
                {
                    bool kolon = k + 1 < sup.Length && sup[k + 1].X1 - sup[k + 1].X0 > 2.0;
                    double station = k + 1 < m ? chain[k + 1].X0 : s.X1;
                    double face = kolon ? station + sup[k + 1].X0 : s.X1;
                    double dis = kolon ? station + sup[k + 1].X1 : face;
                    double end = Math.Max(face - s.GuseL1, s.X0);
                    if (face - end > 5.0 && boy > 5.0)
                        _kirisGuse.Add(new KirisGuseBolge { Span = k, Sol = false, XFace = face, XEnd = end, XKolonDis = dis, Hg = s.GuseHg, KolonAsagi = kolon && sup[k + 1].ExtendDown });
                }
            }
        }

        /// <summary>Zincir X konumunda guseden gelen ek kiriş yüksekliği (cm); guse dışında 0.</summary>
        private double KirisAcilimGuseEk(double x)
        {
            double ek = 0;
            foreach (var g in _kirisGuse)
            {
                double v = 0;
                if (g.Sol)
                {
                    if (x >= g.XKolonDis - 0.5 && x <= g.XFace) v = g.Hg;
                    else if (x > g.XFace && x < g.XEnd) v = g.Hg * (g.XEnd - x) / (g.XEnd - g.XFace);
                }
                else
                {
                    if (x <= g.XKolonDis + 0.5 && x >= g.XFace) v = g.Hg;
                    else if (x < g.XFace && x > g.XEnd) v = g.Hg * (x - g.XEnd) / (g.XFace - g.XEnd);
                }
                if (v > ek) ek = v;
            }
            return ek;
        }

        /// <summary>[lo, hi] zincir aralığına değen guselerin en büyük Hg değeri.</summary>
        private double KirisAcilimGuseMaxHg(double lo, double hi)
        {
            double hg = 0;
            foreach (var g in _kirisGuse)
            {
                double a = Math.Min(g.XFace, g.XEnd), b = Math.Max(g.XFace, g.XEnd);
                if (b < lo - 0.5 || a > hi + 0.5) continue;
                if (g.Hg > hg) hg = g.Hg;
            }
            return hg;
        }

        /// <summary>
        /// Kiriş beton poligonunun alt kenarına (pts[0..nAlt), X artan) guse köşelerini ekler:
        /// mesnet yüzünde Hg basamağı (kolon dikdörtgeniyle birleşir), guse bitiminde kiriş altı.
        /// </summary>
        private void KirisAcilimGuseAltKenar(List<Point2d> pts, int nAlt, double x0)
        {
            if (_kirisGuse.Count == 0 || pts == null || nAlt < 2 || nAlt > pts.Count) return;
            var alt = pts.GetRange(0, nAlt);
            double xMin = alt[0].X, xMax = alt[nAlt - 1].X;
            double Yb(double x)
            {
                if (x <= alt[0].X) return alt[0].Y;
                for (int i = 1; i < nAlt; i++)
                {
                    if (x > alt[i].X) continue;
                    double dx = alt[i].X - alt[i - 1].X;
                    return dx < 1e-6 ? alt[i].Y : alt[i - 1].Y + (alt[i].Y - alt[i - 1].Y) * (x - alt[i - 1].X) / dx;
                }
                return alt[nAlt - 1].Y;
            }
            var yeni = new List<(double X, int Sira, Point2d P)>();
            var dusur = new double[nAlt];
            bool eklendi = false;
            foreach (var g in _kirisGuse)
            {
                double xf = x0 + g.XFace, xe = x0 + g.XEnd;
                if (xf < xMin - 0.5 || xf > xMax + 0.5) continue;
                xf = Math.Max(xMin, Math.Min(xMax, xf));
                xe = Math.Max(xMin, Math.Min(xMax, xe));
                double boy = Math.Abs(xe - xf);
                if (boy < 1.0) continue;
                eklendi = true;
                double lo = Math.Min(xf, xe), hi = Math.Max(xf, xe);
                for (int i = 0; i < nAlt; i++)
                    if (alt[i].X > lo + 0.2 && alt[i].X < hi - 0.2)
                        dusur[i] = Math.Max(dusur[i], g.Hg * Math.Abs(xe - alt[i].X) / boy);
                double yf = Yb(xf), ye = Yb(xe);
                // Sol guse: kolon tarafı (üst) → basamak altı → eğik; sağ guse: eğik → basamak altı → kolon tarafı (üst).
                yeni.Add((xf, 1, new Point2d(xf, yf)));
                yeni.Add((xf, g.Sol ? 2 : 0, new Point2d(xf, yf - g.Hg)));
                yeni.Add((xe, 1, new Point2d(xe, ye)));
            }
            if (!eklendi) return;
            for (int i = 0; i < nAlt; i++)
                yeni.Add((alt[i].X, 1, new Point2d(alt[i].X, alt[i].Y - dusur[i])));
            var sirali = yeni.OrderBy(t => Math.Round(t.X, 3)).ThenBy(t => t.Sira).Select(t => t.P).ToList();
            pts.RemoveRange(0, nAlt);
            pts.InsertRange(0, sirali);
        }

        /// <summary>
        /// Guse kalıp ölçüleri (STA4CAD düzeni): guse alt hizasında yatay Lg ölçüsü (mesnet yüzü → kırılma),
        /// kırılma noktasında kiriş altından guse altına düşey Hg ölçüsü.
        /// </summary>
        private void KirisAcilimGuseEtiket(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, int i0, int i1, double x0, double yShift)
        {
            if (_kirisGuse.Count == 0 || db == null) return;
            ObjectId stil = GetOrCreatePlanOlcuDimStyle(tr, db, 10.0, 1.0, "PLAN_OLCU_25");
            foreach (var g in _kirisGuse)
            {
                if (g.Span < i0 || g.Span > i1) continue;
                var span = chain[g.Span];
                double s = g.Sol ? 1.0 : -1.0;
                double xf = x0 + g.XFace, xe = x0 + g.XEnd;
                double yKirilma = yShift + KirisAcilimKotAt(chain, g.Span, g.Span, g.XEnd) - span.Beam.HeightCm;
                double yYuz = yShift + KirisAcilimKotAt(chain, g.Span, g.Span, g.XFace) - span.Beam.HeightCm - g.Hg;
                // Lg: ölçü çizgisi guse alt hizasında; yazı eğik yüzün altındaki boşlukta kalır.
                AppendEntity(tr, ms, new RotatedDimension(0.0,
                    new Point3d(xf, yYuz, 0), new Point3d(xe, yKirilma, 0),
                    new Point3d(0.5 * (xf + xe), yYuz, 0), "", stil)
                { Layer = LayerOlcu, Dimexo = 0.0, Dimse1 = true, Dimse2 = false });
                // Hg: kırılma noktasının 8 cm açığında düşey ölçü.
                AppendEntity(tr, ms, new RotatedDimension(Math.PI * 0.5,
                    new Point3d(xe, yKirilma, 0), new Point3d(xe, yYuz, 0),
                    new Point3d(xe + s * 8.0, 0.5 * (yKirilma + yYuz), 0), "", stil)
                { Layer = LayerOlcu, Dimexo = 0.0 });
            }
        }

        /// <summary>Guseli uçtaki alt mesnet ilavesi (KMX: Sol* = ST4 başlangıç ucu). Yoksa 0.</summary>
        private static void KirisAcilimGuseAltIlave(KirisAcilimSpan span, bool sol, KirisAcilimMontajBilgi m, out int adet, out int cap)
        {
            bool bas = (sol ? span.Cross0 : span.Cross1) == span.Beam.StartAxisId;
            adet = bas ? m.SolAltAdet : m.SagAltAdet;
            cap = bas ? m.SolAltCap : m.SagAltCap;
            if (adet <= 0 || cap <= 0) adet = cap = 0;
        }

        /// <summary>Açıklığın sol/sağ ucunda guse var mı (alt ilave guse boyunca çizilir, düz çizilmez).</summary>
        private bool KirisAcilimGuseUcu(int span, bool sol)
        {
            foreach (var g in _kirisGuse)
                if (g.Span == span && g.Sol == sol) return true;
            return false;
        }

        /// <summary>
        /// Guse donatısı = o uçtaki alt mesnet ilavesi (STA4CAD "ila."); raporda yoksa alt düz donatının adet/çapı.
        /// Eğik yüze paralel; mesnet yüzünden yatayda ilave boyu (Ln/4 kuralı) kadar, en az kırılmadan sonra ℓb,
        /// bükülmeden düz devam eder. Kolonda dış yüze kadar uzanıp yukarı gönye yapar.
        /// </summary>
        private void KirisAcilimGuseDonati(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, int i0, int i1, double x0, double yShift, double net0, double net1,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj, KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus)
        {
            if (_kirisGuse.Count == 0 || montaj == null) return;
            foreach (var g in _kirisGuse)
            {
                if (g.Span < i0 || g.Span > i1) continue;
                var span = chain[g.Span];
                if (span.Beam == null || !montaj.TryGetValue(span.Beam, out KirisAcilimMontajBilgi m)) continue;
                KirisAcilimGuseAltIlave(span, g.Sol, m, out int adet, out int cap);
                if (adet <= 0) { adet = m.DuzAdet; cap = m.DuzCap; }
                if (adet <= 0 || cap <= 0) continue;
                double phi = cap / 10.0;
                double s = g.Sol ? 1.0 : -1.0;
                double L = Math.Abs(g.XEnd - g.XFace);
                double len = Math.Sqrt(L * L + g.Hg * g.Hg);
                if (len < 5.0) continue;
                double h = span.Beam.HeightCm;
                double topF = yShift + KirisAcilimKotAt(chain, g.Span, g.Span, g.XFace);
                double off = KirisAcilimCelikY(topF, h, montajCubugu: false) - (topF - h);
                if (off < 2.0) off = KirisAcilimDuzPaspayiCm;
                double ux = s * L / len, uy = g.Hg / len;
                double nx = -s * g.Hg / len, ny = L / len;
                double ax = x0 + g.XFace + nx * off, ay = topF - h - g.Hg + ny * off;
                Point2d P(double t) => new Point2d(ax + ux * t, ay + uy * t);

                // Uç: yüzden yatayda alt ilave boyu (Ln/4 kuralı); kırılmadan sonra en az ℓb.
                double lnNet = net1 - net0;
                double hk = KirisAcilimMesnetHk(chain, i0, i1, sup, gorunus, g.Sol);
                double yuzden = lnNet > 1.0 ? KirisAcilimIlaveYuzdenCm(lnNet, hk, cap, false) : 0.0;
                double ext = KirisAcilimYuvarlaYukari(_lbPhiKonumII * phi);
                double tUst = Math.Max(len + ext, yuzden * len / L);
                // Üst donatı bölgesine girmesin.
                double tMax = (topF - 3.0 * off - ay) / uy;
                if (tUst > tMax) tUst = Math.Max(len, tMax);

                var pts = new List<Point2d>();
                double kolonEn = Math.Abs(g.XFace - g.XKolonDis);
                double duzKolon = 0;
                if (kolonEn > 2.0 * off + 6.0)
                {
                    double xc = x0 + g.XKolonDis + s * (off + 2.0);
                    Point2d p0;
                    if (g.KolonAsagi)
                    {
                        // Kolon aşağı devam ediyor: çubuk eğimini bozmadan kolonun içine uzanır.
                        p0 = P((xc - ax) / ux);
                        duzKolon = p0.GetDistanceTo(P(0));
                    }
                    else
                    {
                        // Kolon kiriş altında bitiyor: mesnet yüzünde yataya döner.
                        p0 = new Point2d(xc, ay);
                        duzKolon = Math.Abs(ax - xc);
                    }
                    double gonye = KirisAcilimYuvarlaYukari(Math.Max(Math.Max(KirisAcilimKenarDuseyPhi * phi, KirisAcilimGonyeMinCm),
                        _lbPhiKonumII * phi - duzKolon));
                    double gonyeMax = topF - 2.0 * off - p0.Y;
                    if (gonye > gonyeMax) gonye = Math.Max(0, gonyeMax);
                    if (gonye >= 5.0) pts.Add(new Point2d(p0.X, p0.Y + gonye));
                    pts.Add(p0);
                    if (!g.KolonAsagi) pts.Add(new Point2d(ax, ay));
                }
                else
                    pts.Add(P(0));
                pts.Add(P(tUst));

                double boy = KirisAcilimDonatiPl(tr, ms, KirisAcilimRenkIlaveAlt, pts, egikGonye: true);
                if (boy < 1.0) continue;
                KirisAcilimKesitDonatiKaydet(pts, adet, cap, "ilaA");

                // Görünüş etiketi: eğik çubuğa paralel, guse ortasında, çubuğun üstünde.
                if (db != null)
                {
                    double ang = Math.Atan2(uy, ux);
                    if (ang > Math.PI * 0.5) ang -= Math.PI;
                    if (ang < -Math.PI * 0.5) ang += Math.PI;
                    Point2d orta = P(len * 0.5);
                    string lbl = adet.ToString(CultureInfo.InvariantCulture) + "ø" + cap.ToString(CultureInfo.InvariantCulture) + " (guse)";
                    DrawBeamLabel(tr, ms, db, new Point3d(orta.X + nx * 5.5, orta.Y + ny * 5.5, 0), lbl, 5.0, ang,
                        KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
                }

                // Açılım: eğik kol yataya yatırılmış gerçek boy, kolon ucunda gönye.
                var ilk = pts[0];
                bool gonyeli = pts.Count >= 3 && Math.Abs(pts[0].X - pts[1].X) < 0.5;
                double gonyeBoy = gonyeli ? pts[0].GetDistanceTo(pts[1]) : 0;
                double anaBoy = 0;
                for (int i = gonyeli ? 2 : 1; i < pts.Count; i++) anaBoy += pts[i - 1].GetDistanceTo(pts[i]);
                double xk = gonyeli ? pts[1].X : ilk.X;
                double yk = topF - h;
                var ac = new List<Point2d>();
                if (g.Sol)
                {
                    if (gonyeli) ac.Add(new Point2d(xk, yk + gonyeBoy));
                    ac.Add(new Point2d(xk, yk));
                    ac.Add(new Point2d(xk + anaBoy, yk));
                }
                else
                {
                    ac.Add(new Point2d(xk - anaBoy, yk));
                    ac.Add(new Point2d(xk, yk));
                    if (gonyeli) ac.Add(new Point2d(xk, yk + gonyeBoy));
                }
                KirisAcilimAcilimCiz(tr, ms, db, ac, KirisAcilimRenkIlaveAlt, adet, cap, boy, KirisAcilimGuseAcilimCm, ref _kirisAcilimIlaveSira);
            }
        }

        /// <summary>
        /// Guse bölgesindeki etriyeler değişken yükseklikte. Etiket bölgesindeki etriyeleri yükseklik basamağına
        /// (5 cm'nin katı, en çok ~6 grup) göre gruplar; "adet L=boy" listesi döner. Bölge guseye değmiyorsa null.
        /// </summary>
        private string KirisAcilimGuseEtriyeListesi(double lo, double hi, double aralik, int adet,
            double genislik, double h, int etriyeCap, out double boyMin, out double boyMax, out double boyOrt)
        {
            boyMin = boyMax = boyOrt = 0;
            if (_kirisGuse.Count == 0 || adet <= 0 || aralik <= 0) return null;
            double hg = KirisAcilimGuseMaxHg(lo, hi);
            if (hg <= 0) return null;
            // 5 cm yükseklik basamağı, aşağı yuvarlanır: etriye kalıba sığar, alt örtü en çok 5 cm artar.
            const double adim = 5.0;
            double kanca = Math.Max(10.0, EtriyeAcilimKancaCm(etriyeCap));
            var gruplar = new SortedDictionary<double, int>();
            bool degisken = false;
            for (int k = 0; k < adet; k++)
            {
                double x = adet == 1 ? 0.5 * (lo + hi) : lo + (hi - lo) * k / (adet - 1);
                double ek = KirisAcilimGuseEk(x);
                if (ek > 0.5) degisken = true;
                double yuk = h + Math.Floor(ek / adim + 1e-6) * adim;
                gruplar[yuk] = gruplar.TryGetValue(yuk, out int n) ? n + 1 : 1;
            }
            if (!degisken || gruplar.Count < 2) return null;
            var parcalar = new List<string>();
            double toplamBoy = 0;
            foreach (var kv in gruplar.Reverse())
            {
                double boy = EtriyeAcilimKesimBoyCm(genislik, kv.Key - 8.0, kanca);
                if (boyMin == 0 || boy < boyMin) boyMin = boy;
                if (boy > boyMax) boyMax = boy;
                toplamBoy += boy * kv.Value;
                parcalar.Add(string.Format(CultureInfo.InvariantCulture, "{0} L={1:0}", kv.Value, boy));
            }
            // Metraj için adet ağırlıklı ortalama boy.
            boyOrt = Math.Round(toplamBoy / adet);
            return string.Join(" + ", parcalar);
        }
    }
}
