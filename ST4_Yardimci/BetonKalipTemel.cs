using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NetTopologySuite.Geometries;

namespace ST4Yardimci
{
    /// <summary>
    /// BETONKALIP temel ve subasman kalemleri (ST4 /Continuous foundations/, /Single footings/, /Tie beams/, /Slab foundations/).
    /// Plan izdüşümleri Plan ID ile aynı kurallarla (aks kesişimi, kaçıklık, uç uzatma) NTS çokgeni olarak kurulur; aynı türdeki
    /// elemanların kesişimi bir kez sayılır (yeni eleman = çokgen − öncekilerin birleşimi). Kalıp = açıkta kalan kenar boyu × yükseklik.
    /// TEMEL: sürekli temel gövdesi, ampatman, sürekli temele gömülü bağ kirişi, radye, tekil temel.
    /// SUBASMAN: sürekli temel üstü hatıl, hatıl statüsündeki bağ kirişi, subasman döşemesi (ve kolonlu S katı).
    /// </summary>
    internal sealed partial class BetonKalipProjesi
    {
        /// <summary>Satır adı → miktar (ekleniş sırası korunur).</summary>
        public readonly List<KeyValuePair<string, BetonKalip>> Temel = new List<KeyValuePair<string, BetonKalip>>();
        public readonly List<KeyValuePair<string, BetonKalip>> Subasman = new List<KeyValuePair<string, BetonKalip>>();
        /// <summary>Subasman döşemesinin oturduğu hatıl ve bağ kirişi izdüşümlerinin birleşimi.</summary>
        internal Geometry DestekU;

        private static BetonKalip Kalem(List<KeyValuePair<string, BetonKalip>> l, string ad)
        {
            foreach (var kv in l) if (kv.Key == ad) return kv.Value;
            var b = new BetonKalip();
            l.Add(new KeyValuePair<string, BetonKalip>(ad, b));
            return b;
        }

        // ------------------------------------------------------------------ akslar (eğik dahil)
        /// <summary>X aksı: x = v + s·y; Y aksı: y = v + s·x (m). Plan ID AxisGeometryService ile aynı doğrular (y yukarı).</summary>
        private sealed class AksAg
        {
            public readonly List<(double s, double v)> Satir = new List<(double, double)>();
            public int Nx;
            public bool Kes(int a, int b, out double x, out double y)
            {
                x = y = 0;
                if (!Dogru(a, out double a1, out double b1, out double c1) || !Dogru(b, out double a2, out double b2, out double c2)) return false;
                double d = a1 * b2 - a2 * b1;
                if (Math.Abs(d) < 1e-12) return false;
                x = (c1 * b2 - c2 * b1) / d; y = (a1 * c2 - a2 * c1) / d;
                return true;
            }
            /// <summary>a·x + b·y = c</summary>
            private bool Dogru(int aks, out double a, out double b, out double c)
            {
                a = b = c = 0;
                int i = aks < 2000 ? aks - 1001 : Nx + aks - 2001;
                if (aks < 1001 || i < 0 || i >= Satir.Count || (aks < 2000 && aks - 1001 >= Nx)) return false;
                var (s, v) = Satir[i];
                if (aks < 2000) { a = 1; b = -s; c = v; } else { a = -s; b = 1; c = v; }
                return true;
            }
        }

        private static readonly GeometryFactory Gf = new GeometryFactory(new PrecisionModel(10000));

        /// <summary>Plan ID ComputeBeamEdgeOffsets: kaçıklık kodu (mm) ve yarı genişlik (cm) → perp yönünde üst/alt yüz (cm).</summary>
        private static void KenarKacik(int off, double hw, out double ust, out double alt)
        {
            if (off == -1) { ust = 0; alt = -2 * hw; return; }
            if (off == 1) { ust = 2 * hw; alt = 0; return; }
            if (off == 0) { ust = hw; alt = -hw; return; }
            double cm = off / 10.0;
            if (off > 1) { alt = -cm; ust = alt + 2 * hw; return; }
            ust = -cm; alt = ust - 2 * hw;
        }

        /// <summary>Plan ID ComputeTieBeamEdgeOffsets (sürekli temel üstü hatıl kaçıklığı).</summary>
        private static void HatilKacik(int sabit, int off, double hw, out double ust, out double alt)
        {
            bool x = sabit < 2000;
            if (off == 0) { ust = hw; alt = -hw; return; }
            double cm = off / 10.0;
            if (x)
            {
                if (off == -1) { alt = 0; ust = 2 * hw; return; }
                if (off == 1) { ust = 0; alt = -2 * hw; return; }
                if (off > 1) { alt = -cm; ust = alt + 2 * hw; return; }
                ust = -cm; alt = ust - 2 * hw; return;
            }
            if (off == -1) { ust = 0; alt = -2 * hw; return; }
            if (off == 1) { alt = 0; ust = 2 * hw; return; }
            if (off > 1) { ust = cm; alt = ust - 2 * hw; return; }
            alt = cm; ust = alt + 2 * hw;
        }

        /// <summary>Sabit aks boyunca baş–son aks kesişimleri arasında şerit; kenarlar perp yönünde cm, uzatmalar cm.</summary>
        private static Polygon Serit(AksAg ag, int sabit, int bas, int son, double ustCm, double altCm, double uz1Cm, double uz2Cm)
        {
            if (!ag.Kes(sabit, bas, out double x1, out double y1) || !ag.Kes(sabit, son, out double x2, out double y2)) return null;
            double L = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            if (L < 1e-6) return null;
            double ax = (x2 - x1) / L, ay = (y2 - y1) / L, px = ay, py = -ax;   // Plan ID perp (y ekseni ters çizim düzlemi)
            double sx = x1 - ax * uz1Cm / 100, sy = y1 - ay * uz1Cm / 100, ex = x2 + ax * uz2Cm / 100, ey = y2 + ay * uz2Cm / 100;
            double u = ustCm / 100, a = altCm / 100;
            return Dikdortgen(new[] { (sx + px * u, sy + py * u), (ex + px * u, ey + py * u), (ex + px * a, ey + py * a), (sx + px * a, sy + py * a) });
        }

        private static Polygon Dikdortgen((double x, double y)[] p)
        {
            var c = p.Select(q => new Coordinate(q.x, q.y)).ToList();
            c.Add(c[0]);
            var pol = Gf.CreatePolygon(c.ToArray());
            if (pol.IsValid) return pol;
            // köşe sırası çapraz (kendini kesen dörtgen): dışbükey zarf; dejenere ise boş
            var zarf = Gf.CreateMultiPointFromCoords(c.Take(c.Count - 1).ToArray()).ConvexHull();
            return zarf as Polygon ?? Gf.CreatePolygon();
        }

        /// <summary>Temel / subasman katı elemanı: plan çokgeni, alt–üst kot (m), kalem adı ve grubu.</summary>
        private sealed class Prizma
        {
            public Geometry P;
            public double Z0, Z1;
            public string Kalem;
            public bool Subasman;
        }

        /// <summary>
        /// Prizmaların birleşimini kot dilimleriyle kesişimi bir kez sayarak hesaplar: her dilimde kalemler öncelik sırasıyla
        /// (önce gelen kesişimi alır) beton = (kalem birleşimi − öncekiler) alanı × dilim yüksekliği; kalıp = dilimdeki tüm katı
        /// birleşiminin dış sınırı × yükseklik, sınırı içeren ilk kaleme yazılır.
        /// </summary>
        private void PrizmaHesapla(List<Prizma> ps, string[] sira)
        {
            var z = ps.SelectMany(p => new[] { p.Z0, p.Z1 }).Select(v => Math.Round(v, 4)).Distinct().OrderBy(v => v).ToList();
            var sonuc = sira.ToDictionary(k => k, k => new BetonKalip());
            for (int i = 0; i + 1 < z.Count; i++)
            {
                double za = z[i], zb = z[i + 1], dz = zb - za;
                if (dz < 1e-6) continue;
                var aktif = ps.Where(p => p.Z0 <= za + 1e-6 && p.Z1 >= zb - 1e-6).ToList();
                if (aktif.Count == 0) continue;
                Geometry onceki = Gf.CreatePolygon();
                var kalemU = new List<(string k, Geometry u)>();
                foreach (string k in sira)
                {
                    var l = aktif.Where(p => p.Kalem == k).Select(p => p.P).ToList();
                    if (l.Count == 0) continue;
                    var u = NtsEk.Birlesim(l);
                    sonuc[k].Beton += u.Fark(onceki).Area * dz;
                    onceki = onceki.Birles(u);
                    kalemU.Add((k, u));
                }
                Geometry kalan = onceki.Boundary;
                foreach (var (k, u) in kalemU)
                {
                    if (kalan.IsEmpty) break;
                    var tampon = u.Buffer(0.0005);   // sınır çizgisi ile çokgen kenarı sayısal olarak tam çakışmaz
                    sonuc[k].Kalip += kalan.Kesis(tampon).Length * dz;
                    kalan = kalan.Fark(tampon);
                }
            }
            foreach (string k in sira)
            {
                var s = sonuc[k];
                if (s.Beton < 1e-6 && s.Kalip < 1e-6) continue;
                Kalem(ps.First(p => p.Kalem == k).Subasman ? Subasman : Temel, k).Ekle(s);
            }
        }

        // ------------------------------------------------------------------ temel okuma ve hesap
        /// <summary>
        /// Kotlar bina alt kotuna göre (m): sürekli temel 11. alan alt kot, gövde H; ampatman aynı alttan Da; hatıl gövde üstünden;
        /// radye 6. alan alt kot, kalınlık; bağ kirişi 7. alan alt kot, H; tekil temel 7. alan alt kot, H.
        /// </summary>
        private void TemelHesapla(string[] l, AksAg ag, List<double[]> kolonAksi, Geometry tabanKolon)
        {
            double D(string s) => double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
            int I(string s) => (int)Math.Round(D(s));
            bool Aks(int a) => a >= 1001 && a <= 2999;
            IEnumerable<string[]> Bolum(string ad)
            {
                int k = Array.FindIndex(l, s => s.StartsWith(ad, StringComparison.Ordinal));
                if (k < 0) yield break;
                for (int i = k + 1; i < l.Length && !l[i].StartsWith("/"); i++) yield return l[i].Split(',');
            }
            const string KRadye = "Radye", KTekil = "Tekil temel", KGovde = "Sürekli temel", KAmp = "Ampatman",
                KBagT = "Bağ kirişi", KHatil = "Temel hatılı", KBagH = "Bağ kirişi (hatıl)";
            var ps = new List<Prizma>();
            void Ekle(Geometry p, double z0, double h, string k, bool sub)
            {
                if (p != null && !p.IsEmpty && p.Area > 1e-9 && h > 0) ps.Add(new Prizma { P = p, Z0 = z0, Z1 = z0 + h, Kalem = k, Subasman = sub });
            }

            // sürekli temel: veri satırı (≥10 alan, 5–7. alan aks) + ardından ampatman satırı (2. alan Da)
            var cs = Bolum("/Continuous foundations/").ToList();
            for (int i = 0; i < cs.Count; i++)
            {
                var p = cs[i];
                if (p.Length < 10 || !Aks(I(p[4])) || !Aks(I(p[5])) || !Aks(I(p[6]))) continue;
                int sabit = I(p[4]), bas = I(p[5]), son = I(p[6]);
                double h = D(p[1]) / 100, b = D(p[2]) > 0 ? D(p[2]) : 80, ampB = D(p[3]), alt = p.Length > 10 ? D(p[10]) : 0;
                double uz1 = p[7].Trim() == "1" ? 0 : D(p[7]), uz2 = p[8].Trim() == "1" ? 0 : D(p[8]);
                int off = I(p[9]); if (sabit < 2000) off = -off;
                double hatB = p.Length > 11 ? D(p[11]) : 0, hatH = p.Length > 12 ? D(p[12]) / 100 : 0; int hatOff = p.Length > 13 ? I(p[13]) : 0;
                double da = i + 1 < cs.Count && cs[i + 1].Length > 1 ? D(cs[i + 1][1]) / 100 : 0;
                KenarKacik(off, b / 2, out double ue, out double le);
                var govdeSerit = Serit(ag, sabit, bas, son, ue, le, uz1, uz2);
                Ekle(govdeSerit, alt, h, KGovde, false);
                Iz?.Add(string.Format(CultureInfo.InvariantCulture, "T|{0} {1}-{2} B{3} H{4} off{5} uz{6}/{7} alt{8} alan{9:F3}", sabit, bas, son, b, h, off, uz1, uz2, alt, govdeSerit?.Area ?? 0));
                if (ampB > 0 && Math.Abs(ampB - b) > 1e-6 && da > 0 && da < h)
                {
                    int hiza = I(p[0]); if (sabit < 2000 && hiza != 0) hiza = hiza == 1 ? 2 : 1;
                    double e0, e1;
                    if (hiza == 0) { double m = (ue + le) / 2; e0 = m - ampB / 2; e1 = m + ampB / 2; }
                    else if (hiza == 1) { e0 = le; e1 = le + ampB; } else { e0 = ue; e1 = ue - ampB; }
                    Ekle(Serit(ag, sabit, bas, son, Math.Max(e0, e1), Math.Min(e0, e1), uz1, uz2), alt, da, KAmp, false);
                }
                if (hatB > 0 && hatH > 0)
                {
                    HatilKacik(sabit, hatOff, hatB / 2, out double hu, out double hl);
                    Ekle(Serit(ag, sabit, bas, son, hu, hl, 0, 0), alt + h, hatH, KHatil, true);
                }
                i++;   // ampatman satırı
            }
            var govdeU = NtsEk.Birlesim(ps.Where(x => x.Kalem == KGovde).Select(x => x.P).ToList()) ?? Gf.CreatePolygon();

            // radye: kalınlık, 4 aks, alt kot (+ atlanan ikinci satır)
            var rs = Bolum("/Slab foundations/").ToList();
            for (int i = 0; i < rs.Count; i++)
            {
                var p = rs[i];
                if (p.Length < 5 || D(p[0]) <= 0) continue;
                var ax = p.Skip(1).Take(4).Select(I).ToList();
                var xs = ax.Where(a => a >= 1001 && a < 2000).ToList(); var ys = ax.Where(a => a >= 2001 && a <= 2999).ToList();
                if (xs.Count != 2 || ys.Count != 2) continue;
                var k = new List<(double, double)>();
                foreach (var (a, b) in new[] { (xs[0], ys[0]), (xs[1], ys[0]), (xs[1], ys[1]), (xs[0], ys[1]) })
                { if (!ag.Kes(a, b, out double x, out double y)) { k = null; break; } k.Add((x, y)); }
                if (k != null) Ekle(Dikdortgen(k.ToArray()), p.Length > 5 ? D(p[5]) : 0, D(p[0]) / 100, KRadye, false);
                i++;
            }

            // bağ kirişi: B, H, sabit, baş, son, kaçıklık, alt kot; kolonlar arasında (kolon izdüşümleri düşülür, STA4 Ln);
            // sürekli temele gömülü olan TEMEL, diğeri hatıl statüsünde (SUBASMAN)
            foreach (var p in Bolum("/Tie beams/"))
            {
                if (p.Length < 6 || !Aks(I(p[2])) || !Aks(I(p[3])) || !Aks(I(p[4]))) continue;
                int sabit = I(p[2]); double b = D(p[0]) > 0 ? D(p[0]) : 30, h = D(p[1]) / 100;
                int off = I(p[5]); if (sabit < 2000) off = -off;
                KenarKacik(off, b / 2, out double ue, out double le);
                var serit = Serit(ag, sabit, I(p[3]), I(p[4]), ue, le, 0, 0);
                if (serit == null) continue;
                Geometry pol = tabanKolon == null || tabanKolon.IsEmpty ? (Geometry)serit : serit.Fark(tabanKolon);
                bool gomulu = !govdeU.IsEmpty && pol.Kesis(govdeU).Area > 0.5 * pol.Area;
                Ekle(pol, p.Length > 6 ? D(p[6]) : 0, h, gomulu ? KBagT : KBagH, !gomulu);
            }

            // tekil temel: kolon konum no (100 + sıra), x/y boyu, hizalar, yükseklik, alt kot (+ atlanan ikinci satır)
            var ts = Bolum("/Single footings/").ToList();
            for (int i = 0; i < ts.Count; i++)
            {
                var p = ts[i];
                if (p.Length < 6 || I(p[0]) <= 100 || D(p[1]) <= 0 || D(p[2]) <= 0) continue;
                int sira = I(p[0]) - 100;
                if (sira >= 1 && sira <= kolonAksi.Count && kolonAksi[sira - 1].Length >= 3 && ag.Kes((int)kolonAksi[sira - 1][1], (int)kolonAksi[sira - 1][2], out double cx, out double cy))
                {
                    double hx = D(p[1]) / 200, hy = D(p[2]) / 200;
                    Ekle(Dikdortgen(new[] { (cx - hx, cy - hy), (cx + hx, cy - hy), (cx + hx, cy + hy), (cx - hx, cy + hy) }),
                        p.Length > 6 ? D(p[6]) : 0, D(p[5]) / 100, KTekil, false);
                }
                i++;
            }

            if (ps.Count > 0) PrizmaHesapla(ps, new[] { KRadye, KTekil, KGovde, KAmp, KBagT, KHatil, KBagH });
            var destek = ps.Where(x => x.Kalem == KHatil || x.Kalem == KBagH || x.Kalem == KBagT).Select(x => x.P).ToList();
            if (destek.Count > 0) DestekU = NtsEk.Birlesim(destek);
        }
    }
}

namespace ST4Yardimci
{
    /// <summary>
    /// BETONKALIP çokgen işlemleri: OverlayNG sabit hassasiyet (0.1 mm yakalama) ile; kayan noktada "non-noded intersection"
    /// hatası vermez. Girdiler basit dikdörtgen/dörtgenlerdir.
    /// </summary>
    internal static class NtsEk
    {
        private static readonly NetTopologySuite.Geometries.PrecisionModel Pm = new NetTopologySuite.Geometries.PrecisionModel(10000);
        private static NetTopologySuite.Geometries.Geometry Op(NetTopologySuite.Geometries.Geometry a, NetTopologySuite.Geometries.Geometry b, NetTopologySuite.Operation.Overlay.SpatialFunction f) =>
            NetTopologySuite.Operation.OverlayNG.OverlayNG.Overlay(Tek(a), Tek(b), f, Pm);

        /// <summary>Karışık GeometryCollection girdisinde en yüksek boyutlu parçalar (OverlayNG karışık boyut kabul etmez).</summary>
        private static NetTopologySuite.Geometries.Geometry Tek(NetTopologySuite.Geometries.Geometry g)
        {
            if (g == null || g.GetType() != typeof(NetTopologySuite.Geometries.GeometryCollection)) return g;
            int boyut = 0;
            for (int i = 0; i < g.NumGeometries; i++) boyut = System.Math.Max(boyut, (int)g.GetGeometryN(i).Dimension);
            var p = new System.Collections.Generic.List<NetTopologySuite.Geometries.Geometry>();
            for (int i = 0; i < g.NumGeometries; i++) if ((int)g.GetGeometryN(i).Dimension == boyut && !g.GetGeometryN(i).IsEmpty) p.Add(g.GetGeometryN(i));
            return g.Factory.BuildGeometry(p);
        }
        public static NetTopologySuite.Geometries.Geometry Fark(this NetTopologySuite.Geometries.Geometry a, NetTopologySuite.Geometries.Geometry b) =>
            Op(a, b, NetTopologySuite.Operation.Overlay.SpatialFunction.Difference);
        public static NetTopologySuite.Geometries.Geometry Kesis(this NetTopologySuite.Geometries.Geometry a, NetTopologySuite.Geometries.Geometry b) =>
            Op(a, b, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection);
        public static NetTopologySuite.Geometries.Geometry Birles(this NetTopologySuite.Geometries.Geometry a, NetTopologySuite.Geometries.Geometry b) =>
            Op(a, b, NetTopologySuite.Operation.Overlay.SpatialFunction.Union);
        public static NetTopologySuite.Geometries.Geometry Birlesim(System.Collections.Generic.IList<NetTopologySuite.Geometries.Geometry> l)
        {
            if (l == null || l.Count == 0) return null;
            // yalnız çokgen parçalar (kesme sonucu çizgi/nokta artıkları karışık boyut hatası verir)
            var p = new System.Collections.Generic.List<NetTopologySuite.Geometries.Geometry>();
            foreach (var g in l)
                if (g != null)
                    for (int i = 0; i < g.NumGeometries; i++)
                        if (g.GetGeometryN(i) is NetTopologySuite.Geometries.Polygon q && !q.IsEmpty) p.Add(q);
            if (p.Count == 0) return l[0].Factory.CreatePolygon();
            var gc = l[0].Factory.BuildGeometry(p);
            return NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Union(gc);
        }
    }
}
