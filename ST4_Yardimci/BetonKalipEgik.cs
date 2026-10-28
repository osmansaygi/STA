using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;

namespace ST4Yardimci
{
    /// <summary>
    /// Eğik akslı projelerde kat elemanları: dik akslı yolun STA4 KSF kurallarıyla aynı hesap, ama aksa paralel aralıklar yerine
    /// gerçek çokgenler (kiriş bandı uç kesişimleri arasında, döşeme 4 aks kesişiminden dörtgen, kolon dönmüş dikdörtgen/poligon).
    /// Kiriş Ln = boy − uçtaki kolon / dik kiriş taşmaları; kalıp Ln·(bw + d1 + d2); döşeme net = dörtgen − kenar kirişi bantları,
    /// kalıp net − kolon izdüşümü, beton net·d; kolon kalıp çevre·Hk − değen kiriş kesitleri.
    /// </summary>
    internal sealed partial class BetonKalipProjesi
    {
        private sealed class KirisBandi
        {
            public Kir K;
            public double Ux, Uy, Nx, Ny, Ax, Ay, L;   // birim doğrultu, dik (+ pozitif taraf), başlangıç noktası, boy
            public double O1, O2;                       // dik doğrultuda yüzler (m)
            public Polygon Pol;
            public double T(double x, double y) => (x - Ax) * Ux + (y - Ay) * Uy;
            public double Dk(double x, double y) => (x - Ax) * Nx + (y - Ay) * Ny;
        }

        /// <summary>Kiriş (grup) bandı; X doğrultusundaki kiriş +x, Y doğrultusundaki +y yönüne çevrilir. Dik yön orth Bant ile aynı işaret.</summary>
        private static KirisBandi BantKur(Kir k0, Kir kn)
        {
            double x1 = k0.X1, y1 = k0.Y1, x2 = kn.X2, y2 = kn.Y2;
            bool xYon = k0.Aks >= 2000;
            if (xYon ? x2 < x1 : y2 < y1) { (x1, x2) = (x2, x1); (y1, y2) = (y2, y1); }
            double L = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            if (L < 1e-6) return null;
            double ux = (x2 - x1) / L, uy = (y2 - y1) / L;
            double nx = xYon ? -uy : uy, ny = xYon ? ux : -ux;   // X kirişinde +y, Y kirişinde +x tarafı
            double b = k0.B / 100.0;
            double o1 = k0.Kod == 1 ? -b : k0.Kod == -1 ? 0 : -b / 2, o2 = o1 + b;
            var p = Dikdortgen(new[] { (x1 + nx * o1, y1 + ny * o1), (x2 + nx * o1, y2 + ny * o1), (x2 + nx * o2, y2 + ny * o2), (x1 + nx * o2, y1 + ny * o2) });
            return new KirisBandi { K = k0, Ux = ux, Uy = uy, Nx = nx, Ny = ny, Ax = x1, Ay = y1, L = L, O1 = o1, O2 = o2, Pol = p };
        }

        /// <summary>Kesme sonucu GeometryCollection olabilir: yalnız çokgen parçaları.</summary>
        private static Geometry Cokgen(Geometry g)
        {
            if (g == null || g is Polygon || g is MultiPolygon) return g;
            var p = Enumerable.Range(0, g.NumGeometries).Select(i => g.GetGeometryN(i)).OfType<Polygon>().ToArray();
            return Gf.CreateMultiPolygon(p);
        }

        private static Polygon DosemePol(Dos d) => d.Q == null ? null : Dikdortgen(d.Q);

        private static BetonKalip KirisKatEgik(Geo g, int kat, bool panel)
        {
            double sk = 0, sb = 0;
            var gruplar = Gruplar(g, kat);
            var dosemeler = g.Dosemeler.Where(d => d.Kat == kat && d.Q != null).ToList();
            var kolonlar = g.Kolonlar.Where(c => c.Kat == kat && c.Pol != null).ToList();
            var bantlar = gruplar.Select(gr => BantKur(gr[0], gr[gr.Count - 1])).ToList();
            for (int gi = 0; gi < gruplar.Count; gi++)
            {
                var grp = gruplar[gi]; var bt = bantlar[gi];
                if (grp[0].Panel != panel || bt == null) continue;
                bool xYon = grp[0].Aks >= 2000;
                // uç taşmaları: başlangıcı / bitişi örten kolon ve dik kiriş bantları
                double ta = 0, tb = 0;
                var destek = kolonlar.Select(c => (Geometry)c.Pol)
                    .Concat(bantlar.Where((x, j) => x != null && j != gi && Math.Abs(x.Ux * bt.Ux + x.Uy * bt.Uy) < Math.Cos(10 * Math.PI / 180)).Select(x => (Geometry)x.Pol));
                foreach (var s in destek)
                {
                    var ort = Cokgen(bt.Pol.Kesis(s));
                    if (ort.IsEmpty || ort.Area < 1e-6) continue;
                    double tmin = ort.Coordinates.Min(c => bt.T(c.X, c.Y)), tmax = ort.Coordinates.Max(c => bt.T(c.X, c.Y));
                    if (tmin <= E && tmax > E) ta = Math.Max(ta, tmax);
                    if (tmax >= bt.L - E && tmin < bt.L - E) tb = Math.Max(tb, bt.L - tmin);
                }
                double Ln = bt.L - ta - tb;
                // yan döşemeler: kenarı kiriş aksı üzerinde, boyuna örtüşen; her yanda en kalını
                int d1 = 0, d2 = 0;
                foreach (var d in dosemeler)
                {
                    var q = d.Q;
                    for (int e = 0; e < 4; e++)
                    {
                        var a = q[e]; var b = q[(e + 1) % 4];
                        if (Math.Abs(bt.Dk(a.x, a.y)) > E || Math.Abs(bt.Dk(b.x, b.y)) > E) continue;
                        double t1 = Math.Min(bt.T(a.x, a.y), bt.T(b.x, b.y)), t2 = Math.Max(bt.T(a.x, a.y), bt.T(b.x, b.y));
                        if (Math.Min(t2, bt.L) - Math.Max(t1, 0) < 0.01) continue;
                        double mx = q.Average(z => z.x), my = q.Average(z => z.y);
                        if (bt.Dk(mx, my) < 0) d1 = Math.Max(d1, d.Dk); else d2 = Math.Max(d2, d.Dk);
                    }
                }
                var k0 = grp[0];
                sk += Ln * (k0.B + (k0.H - d1) + (k0.H - d2)) / 100.0;
                sb += k0.B / 100.0 * (k0.H / 100.0) * Ln;
            }
            return new BetonKalip { Beton = sb, Kalip = sk };
        }

        private static BetonKalip KolonKatEgik(Geo g, int kat)
        {
            double sk = 0, sb = 0;
            var bantlar = g.Kirisler.Where(x => x.Kat == kat && !(x.Sira > 0 && g.Kirisler[x.Sira - 1].No == x.No))
                .Select(x => BantKur(x, x)).Where(x => x != null).ToList();
            foreach (var c in g.Kolonlar.Where(c => c.Kat == kat))
            {
                double hk = c.Ust - c.Alt;
                if (hk <= 0 && kat > 0) hk = g.Katlar[kat].Kot - g.Katlar[kat - 1].Kot;
                double degen = 0;
                if (c.Pol != null)
                    foreach (var bt in bantlar)
                    {
                        // kolon kiriş bandının genişliğiyle örtüşmeli (dik doğrultuda > E) ve boyuna değmeli
                        var cs = c.Pol.Coordinates;
                        double dmin = cs.Min(z => bt.Dk(z.X, z.Y)), dmax = cs.Max(z => bt.Dk(z.X, z.Y));
                        if (Math.Min(bt.O2, dmax) - Math.Max(bt.O1, dmin) < E) continue;
                        double tmin = cs.Min(z => bt.T(z.X, z.Y)), tmax = cs.Max(z => bt.T(z.X, z.Y));
                        if (Math.Min(bt.L, tmax) - Math.Max(0, tmin) < -E) continue;
                        degen += bt.K.B / 100.0 * (bt.K.H / 100.0);
                    }
                sk += c.Cevre * hk - degen;
                sb += c.Alan * hk;
            }
            return new BetonKalip { Beton = sb, Kalip = sk };
        }

        private static BetonKalip DosemeKatEgik(Geo g, int kat)
        {
            double sk = 0, sb = 0;
            var bantlar = Gruplar(g, kat).Select(gr => BantKur(gr[0], gr[gr.Count - 1])).Where(x => x != null).ToList();
            var kolon = g.Kolonlar.Where(c => c.Kat == kat && c.Pol != null).Select(c => (Geometry)c.Pol).ToList();
            Geometry kolonU = kolon.Count == 0 ? null : NtsEk.Birlesim(kolon);
            foreach (var d in g.Dosemeler.Where(d => d.Kat == kat && d.Q != null))
            {
                Geometry net = DosemePol(d);
                if (Math.Abs(d.F5) <= d.Dk * 10)   // merdiven sahanlığında kırpma yok
                {
                    // kenar kirişleri: aksı döşeme kenarı üzerinde, boyuna örtüşen
                    var q = d.Q;
                    foreach (var bt in bantlar)
                        for (int e = 0; e < 4; e++)
                        {
                            var a = q[e]; var b = q[(e + 1) % 4];
                            if (Math.Abs(bt.Dk(a.x, a.y)) > E || Math.Abs(bt.Dk(b.x, b.y)) > E) continue;
                            double t1 = Math.Min(bt.T(a.x, a.y), bt.T(b.x, b.y)), t2 = Math.Max(bt.T(a.x, a.y), bt.T(b.x, b.y));
                            if (Math.Min(t2, bt.L) - Math.Max(t1, 0) < E) continue;
                            net = Cokgen(net.Fark(bt.Pol));
                            break;
                        }
                }
                double alan = net.Area;
                double delik = kolonU == null ? 0 : Cokgen(net.Kesis(kolonU)).Area;
                sk += alan - delik;
                sb += alan * d.Dk / 100.0;
            }
            return new BetonKalip { Beton = sb, Kalip = sk };
        }
    }
}
