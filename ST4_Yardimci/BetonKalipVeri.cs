using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NetTopologySuite.Geometries;

namespace ST4Yardimci
{
    /// <summary>Bir kalem: beton m³, kalıp m².</summary>
    internal sealed class BetonKalip
    {
        public double Beton, Kalip;
        public void Ekle(BetonKalip b) { Beton += b.Beton; Kalip += b.Kalip; }
        public void Ekle(double beton, double kalip) { Beton += beton; Kalip += kalip; }
    }

    internal sealed class BetonKalipKat
    {
        public int Indeks;
        public string Ad, Kod;
        public readonly BetonKalip Doseme = new BetonKalip(), Kiris = new BetonKalip(), Kolon = new BetonKalip();
        public BetonKalip Toplam
        {
            get { var t = new BetonKalip(); t.Ekle(Doseme); t.Ekle(Kiris); t.Ekle(Kolon); return t; }
        }
    }

    /// <summary>
    /// STA4CAD projesinden beton/kalıp metrajı (BETONKALIP). Kat satırları STA4 keşif (KSF / &lt;proje&gt;@.TXT) kurallarıyla, beton
    /// brüt (KSF betonundan donatı hacmi düşer; burada düşülmez): döşeme kalıp = Lx·Ly − kolon izdüşümü, beton = Lx·Ly·d;
    /// kiriş kalıp = Ln·(bw+d1+d2), beton = bw·H·Ln; kolon kalıp = çevre·Hk − değen kiriş kesitleri, beton = alan·Hk;
    /// bodrum perde panelleri (kiriş 15. alan = 1) kolon satırında, kalıp Ln·2H. Lx/Ly/Ln kenar kirişi ve mesnet taşmaları
    /// düşülmüş net açıklıklardır (dik akslarda float32, StatikRaporClaude Uretec/Metraj.cs ile aynı). Eğik akslarda BetonKalipEgik.
    /// Doğrulama 26.10.2026: ~110 projenin KSF'si ile (donatı geri eklenerek) kolon betonu 403 satırın 328'inde %0.3 içinde.
    /// </summary>
    internal sealed partial class BetonKalipProjesi
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const double E = 0.005;
        /// <summary>Doğrulama izi (yalnız test aracı açar): eleman başına satır.</summary>
        internal static List<string> Iz { get; set; }

        public string St4Yolu;
        public readonly List<BetonKalipKat> Katlar = new List<BetonKalipKat>();
        /// <summary>Subasman (S) katı döşemeleri: STA4 kat özetine girmez; SUBASMAN satırına eklenir.</summary>
        public readonly BetonKalip SubasmanDoseme = new BetonKalip();

        // ------------------------------------------------------------------ geometri
        private sealed class Kol { public int No, Kat, Yerel, Bx, By; public double Ust, Alt, X1, X2, Y1, Y2, Alan, Cevre; public bool Poligon; public Polygon Pol; }
        /// <summary>Kiriş; Ps/Pe başlangıç/bitiş noktasının kiriş doğrultusundaki, C aksın dik doğrultudaki koordinatı (aks kesişimlerinden).</summary>
        private sealed class Kir { public int No, Kat, B, H, Aks, S, E, Kod, I, J, Sira; public bool Panel; public double Ps, Pe, C, X1, Y1, X2, Y2; }
        /// <summary>Döşeme; X/Y köşe kesişimlerinin sınır kutusu, Oran = gerçek dörtgen alanı / kutu alanı (eğik akslarda &lt; 1).</summary>
        private sealed class Dos { public int No, Kat, Dk, F5, Sira; public double X1, X2, Y1, Y2, Oran = 1; public (double x, double y)[] Q; }

        private sealed class Geo
        {
            public readonly List<double> Eks = new List<double>();
            public readonly List<double> Egim = new List<double>();
            public bool Egik => Egim.Any(s => Math.Abs(s) > 1e-6);
            public int Nx;
            public readonly List<(string Ad, double Kot, string Kod)> Katlar = new List<(string, double, string)>();
            public List<Kol> Kolonlar = new List<Kol>();
            public readonly List<Kir> Kirisler = new List<Kir>();
            public readonly List<Dos> Dosemeler = new List<Dos>();
            public double X(int a) => a - 1001 >= 0 && a - 1001 < Eks.Count ? Eks[a - 1001] : 0;
            public double Y(int a) => Nx + a - 2001 >= 0 && Nx + a - 2001 < Eks.Count ? Eks[Nx + a - 2001] : 0;
            public double A(int a) => a < 2000 ? X(a) : Y(a);
            private double S(int a) { int i = a < 2000 ? a - 1001 : Nx + a - 2001; return i >= 0 && i < Egim.Count ? Egim[i] : 0; }
            /// <summary>X aksı x = v + s·y ile Y aksı y = v + s·x kesişimi (eğik akslar; eğimsizde (X, Y)).</summary>
            public (double x, double y) P(int xa, int ya)
            {
                double sx = S(xa), sy = S(ya), d = 1 - sx * sy;
                if (Math.Abs(d) < 1e-9) d = 1;
                double x = (X(xa) + sx * Y(ya)) / d;
                return (x, Y(ya) + sy * x);
            }
            /// <summary>İki aksın kesişimi; eğik planda aynı türden iki aks da kesişebilir (X aksı x − s·y = v, Y aksı −s·x + y = v).</summary>
            public (double x, double y) Kes(int a, int b)
            {
                if ((a < 2000) != (b < 2000)) return a < 2000 ? P(a, b) : P(b, a);
                double a1 = a < 2000 ? 1 : -S(a), b1 = a < 2000 ? -S(a) : 1, c1 = A(a);
                double a2 = b < 2000 ? 1 : -S(b), b2 = b < 2000 ? -S(b) : 1, c2 = A(b);
                double d = a1 * b2 - a2 * b1;
                if (Math.Abs(d) < 1e-12) return a < 2000 ? P(a, 2001) : P(1001, a);
                return ((c1 * b2 - c2 * b1) / d, (a1 * c2 - a2 * c1) / d);
            }
            public int Taban(IEnumerable<int> nolar) => nolar.DefaultIfEmpty(0).Max() / 100 >= Math.Max(Katlar.Count, 1) ? 1000 : 100;
        }

        private static double D(string s) =>
            double.TryParse((s ?? "").Trim(), NumberStyles.Float, Inv, out double d) ? d : 0;
        private static int N(string s) => (int)Math.Round(D(s));
        private static string[] F(string s) => (s ?? "").Split(',');
        private static string Al(string[] f, int i) => i < f.Length ? f[i] : "0";

        /// <summary>Kolon kaçıklık kodu: 0 ortalı, −1 aks..aks+b, +1 aks−b..aks, diğer: aks + kod/1000 (m) kaydırma.</summary>
        private static double Merkez(double aks, double kod, double b) =>
            kod == 0 ? aks : kod == -1 ? aks + b / 2 : kod == 1 ? aks - b / 2 : kod < 0 ? aks + kod / 1000 + b / 2 : aks + kod / 1000 - b / 2;

        private static Geo Oku(string[] l)
        {
            var g = new Geo();
            int Sec(string ad) => Array.FindIndex(l, s => s.StartsWith(ad, StringComparison.Ordinal));
            if (l.Length < 5) return null;
            g.Nx = N(F(l[4])[0]);
            int k = Sec("/Story/");
            if (k < 0) return null;
            for (int i = k + 1; i + 2 < l.Length && !l[i].StartsWith("/"); i += 3)
                g.Katlar.Add((l[i].Trim(), D(F(l[i + 2])[0]), l[i + 1].Trim().TrimEnd('-')));
            k = Sec("/Axis data/");
            if (k >= 0)
                for (int i = k + 1; i < l.Length && !l[i].StartsWith("/"); i++) { var f = F(l[i]); g.Eks.Add(D(Al(f, 1))); g.Egim.Add(D(f[0])); }
            var rows = new List<double[]>();
            k = Sec("/Column axis data/");
            if (k >= 0)
                for (int i = k + 1; i < l.Length && !l[i].StartsWith("/"); i++) rows.Add(F(l[i]).Select(D).ToArray());

            var kol = new List<Kol>();
            k = Sec("/Columns Data/");
            if (k >= 0)
                for (int i = k + 1; i + 2 < l.Length && !l[i].StartsWith("/"); i += 3)
                {
                    var f = F(l[i]); int no = N(f[0]); if (no == 0) break;
                    var f2 = F(l[i + 1]);
                    int bx = N(Al(f, 1)), by = N(Al(f, 2));
                    kol.Add(new Kol { No = no, Bx = bx, By = by, Ust = D(Al(f2, 6)), Alt = D(Al(f2, 7)), Alan = bx * by / 10000.0, Cevre = 2 * (bx + by) / 100.0 });
                }

            // Poligon kolon / perde: 1. satır no, kesit kimliği; 2. satır 3./4. alan üst/alt kot. Köşeler /Polygon section/ x, y (cm), kimlik.
            var kesit = new Dictionary<int, List<(double x, double y)>>();
            k = Sec("/Polygon section/");
            if (k >= 0)
                for (int i = k + 1; i < l.Length && !l[i].StartsWith("/"); i++)
                {
                    var f = F(l[i]); int id = N(Al(f, 2));
                    if (id == 0) continue;
                    if (!kesit.TryGetValue(id, out var p)) kesit[id] = p = new List<(double, double)>();
                    p.Add((D(f[0]), D(Al(f, 1))));
                }
            k = Sec("/Polygon columns/");
            var poligon = new List<(Kol c, List<(double x, double y)> p)>();
            if (k >= 0)
                for (int i = k + 1; i + 1 < l.Length && !l[i].StartsWith("/"); i += 2)
                {
                    var f = F(l[i]); int no = N(f[0]); if (no == 0) break;
                    var g2 = F(l[i + 1]);
                    kesit.TryGetValue(N(Al(f, 1)), out var p);
                    p = p ?? new List<(double, double)>();
                    double alan = 0, cevre = 0;
                    for (int j = 0; j < p.Count; j++)
                    {
                        var a = p[j]; var b = p[(j + 1) % p.Count];
                        alan += a.x * b.y - b.x * a.y;
                        cevre += Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
                    }
                    var c = new Kol { No = no, Poligon = true, Ust = D(Al(g2, 2)), Alt = D(Al(g2, 3)), Alan = Math.Abs(alan) / 2 / 10000.0, Cevre = cevre / 100.0 };
                    kol.Add(c);
                    poligon.Add((c, p));
                }

            int tk = g.Taban(kol.Select(c => c.No));
            foreach (var c in kol)
            {
                c.Kat = c.No / tk; c.Yerel = c.No % tk;
                if (c.Yerel < 1 || c.Yerel - 1 >= rows.Count) continue;
                var r = rows[c.Yerel - 1];
                if (r.Length < 5) continue;
                if (!c.Poligon)
                {
                    var o = g.P((int)r[1], (int)r[2]);
                    double cx = Merkez(o.x, r[3], c.Bx / 100.0), cy = Merkez(o.y, r[4], c.By / 100.0);
                    c.X1 = cx - c.Bx / 200.0; c.X2 = cx + c.Bx / 200.0; c.Y1 = cy - c.By / 200.0; c.Y2 = cy + c.By / 200.0;
                    double aci = r.Length > 6 ? r[6] : 0;
                    if ((int)r[0] == 2)   // daire kolon: çap = büyük boyut
                    {
                        double rr = Math.Max(c.Bx, c.By) / 200.0;
                        c.Alan = Math.PI * rr * rr; c.Cevre = 2 * Math.PI * rr;
                        c.Pol = (Polygon)Gf.CreatePoint(new Coordinate(cx, cy)).Buffer(rr, 16);
                    }
                    else c.Pol = Dondur(new[] { (c.X1, c.Y1), (c.X2, c.Y1), (c.X2, c.Y2), (c.X1, c.Y2) }, cx, cy, -aci);
                }
            }
            // poligon: aks noktası + köşe (x, y) cm (Plan ID TryGetPolygonColumn çizim düzleminde (x, −y)); sınır kutusu
            foreach (var (c, p) in poligon)
            {
                if (p.Count == 0 || c.Yerel < 1 || c.Yerel - 1 >= rows.Count) continue;
                var r = rows[c.Yerel - 1];
                if (r.Length < 3) continue;
                var (ax, ay) = g.P((int)r[1], (int)r[2]);
                c.X1 = ax + p.Min(q => q.x) / 100.0; c.X2 = ax + p.Max(q => q.x) / 100.0;
                c.Y1 = ay + p.Min(q => q.y) / 100.0; c.Y2 = ay + p.Max(q => q.y) / 100.0;   // çizim düzlemi y ters: gerçek +y
                if (p.Count >= 3) c.Pol = Dondur(p.Select(q => (ax + q.x / 100.0, ay + q.y / 100.0)).ToArray(), ax, ay, -(r.Length > 6 ? r[6] : 0));
            }
            g.Kolonlar = kol;

            k = Sec("/Beams Data/");
            if (k >= 0)
                for (int i = k + 1; i + 2 < l.Length && !l[i].StartsWith("/"); i += 3)
                {
                    var f = F(l[i]); int no = N(f[0]); if (no == 0) break;
                    var f2 = F(l[i + 1]);
                    g.Kirisler.Add(new Kir
                    {
                        No = no, B = N(Al(f, 1)), H = N(Al(f, 2)), Aks = N(Al(f, 4)), S = N(Al(f, 5)), E = N(Al(f, 6)), Kod = N(Al(f, 7)),
                        I = N(Al(f2, 4)), J = N(Al(f2, 5)), Sira = g.Kirisler.Count, Panel = Al(f, 14).Trim() == "1"
                    });
                }
            int tb = g.Taban(g.Kirisler.Select(x => x.No));
            foreach (var x in g.Kirisler) x.Kat = x.No / tb;
            foreach (var x in g.Kirisler)
            {
                var p1 = g.Kes(x.Aks, x.S); var p2 = g.Kes(x.Aks, x.E);
                x.X1 = p1.x; x.Y1 = p1.y; x.X2 = p2.x; x.Y2 = p2.y;
                bool xYon = x.Aks >= 2000;   // Y aksı üzerindeki kiriş X doğrultusunda ilerler
                x.Ps = xYon ? p1.x : p1.y; x.Pe = xYon ? p2.x : p2.y; x.C = xYon ? (p1.y + p2.y) / 2 : (p1.x + p2.x) / 2;
            }

            k = Sec("/Floors Data/");
            if (k >= 0)
                for (int i = k + 1; i + 1 < l.Length && !l[i].StartsWith("/"); i += 2)
                {
                    var f = F(l[i]); int no = N(f[0]); if (no == 0) break;
                    int a1 = N(Al(f, 8)), a2 = N(Al(f, 9)), b1 = N(Al(f, 10)), b2 = N(Al(f, 11));
                    var q = new[] { g.P(a1, b1), g.P(a2, b1), g.P(a2, b2), g.P(a1, b2) };
                    double qa = 0;
                    for (int j = 0; j < 4; j++) qa += q[j].x * q[(j + 1) % 4].y - q[(j + 1) % 4].x * q[j].y;
                    var dd = new Dos
                    {
                        No = no, Dk = N(Al(f, 1)), F5 = N(Al(f, 5)), Sira = g.Dosemeler.Count,
                        X1 = q.Min(z => z.x), X2 = q.Max(z => z.x), Y1 = q.Min(z => z.y), Y2 = q.Max(z => z.y), Q = q
                    };
                    double kutu = (dd.X2 - dd.X1) * (dd.Y2 - dd.Y1);
                    if (kutu > 1e-9) dd.Oran = Math.Min(1, Math.Abs(qa) / 2 / kutu);
                    g.Dosemeler.Add(dd);
                }
            int td = g.Taban(g.Dosemeler.Select(x => x.No));
            foreach (var x in g.Dosemeler) x.Kat = x.No / td;
            return g;
        }

        /// <summary>Kiriş bandı (dik doğrultuda: kod −1 aks..aks+B, +1 aks−B..aks, 0 ortalı) ve boyuna aralık.</summary>
        private static (double b1, double b2, double s1, double s2) Bant(Geo g, Kir k)
        {
            double a = k.C, b = k.B / 100.0;
            double b1 = k.Kod == 1 ? a - b : k.Kod == -1 ? a : a - b / 2;
            return (b1, b1 + b, Math.Min(k.Ps, k.Pe), Math.Max(k.Ps, k.Pe));
        }

        /// <summary>Köşeleri (cx, cy) etrafında derece cinsinden döndürülmüş çokgen.</summary>
        private static Polygon Dondur((double x, double y)[] k, double cx, double cy, double aciDerece)
        {
            double a = aciDerece * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
            return Dikdortgen(k.Select(q => (cx + (q.x - cx) * ca - (q.y - cy) * sa, cy + (q.x - cx) * sa + (q.y - cy) * ca)).ToArray());
        }

        /// <summary>Uç noktaları arası gerçek boy / doğrultu boyunca izdüşüm (eğik kirişte &gt; 1).</summary>
        private static double Egiklik(Kir k0, Kir kn)
        {
            double dx = kn.X2 - k0.X1, dy = kn.Y2 - k0.Y1, iz = Math.Abs(kn.Pe - k0.Ps);
            return iz < 1e-9 ? 1 : Math.Sqrt(dx * dx + dy * dy) / iz;
        }

        /// <summary>Aynı numaralı ardışık kiriş kayıtları (sürekli kiriş) tek grup.</summary>
        private static List<List<Kir>> Gruplar(Geo g, int kat)
        {
            var res = new List<List<Kir>>();
            foreach (var k in g.Kirisler.Where(x => x.Kat == kat))
                if (res.Count > 0 && res[res.Count - 1][res[res.Count - 1].Count - 1].No == k.No) res[res.Count - 1].Add(k);
                else res.Add(new List<Kir> { k });
            return res;
        }

        // ------------------------------------------------------------------ kiriş
        private static (float ta, float tb) UcTasma(Geo g, List<Kir> grp)
        {
            var k0 = grp[0]; var kn = grp[grp.Count - 1];
            bool xYon = k0.Aks >= 2000;
            var (b1, b2, _, _) = Bant(g, k0);
            float s1 = (float)Math.Min(k0.Ps, kn.Pe), s2 = (float)Math.Max(k0.Ps, kn.Pe);
            float ta = 0, tb = 0;
            var rects = new List<(double c1, double c2, double d1, double d2)>();
            foreach (var c in g.Kolonlar.Where(c => c.Kat == k0.Kat)) rects.Add(xYon ? (c.X1, c.X2, c.Y1, c.Y2) : (c.Y1, c.Y2, c.X1, c.X2));
            foreach (var k in g.Kirisler.Where(x => x.Kat == k0.Kat && (x.Aks >= 2000) != xYon)) { var q = Bant(g, k); rects.Add((q.b1, q.b2, q.s1, q.s2)); }
            foreach (var (c1, c2, d1, d2) in rects)
            {
                if (Math.Min(b2, d2) - Math.Max(b1, d1) < -E) continue;
                if (c1 <= s1 + E && c2 > s1 + E) ta = Math.Max(ta, (float)c2 - s1);
                if (c2 >= s2 - E && c1 < s2 - E) tb = Math.Max(tb, s2 - (float)c1);
            }
            return (ta, tb);
        }

        private static int YanDoseme(Geo g, Kir k, int yan, double s1, double s2)
        {
            int best = 0; bool xYon = k.Aks >= 2000; double c = k.C;
            foreach (var d in g.Dosemeler.Where(d => d.Kat == k.Kat))
            {
                double a1 = xYon ? d.X1 : d.Y1, a2 = xYon ? d.X2 : d.Y2, b1 = xYon ? d.Y1 : d.X1, b2 = xYon ? d.Y2 : d.X2;
                if (Math.Min(a2, s2) - Math.Max(a1, s1) < 0.01) continue;
                if (yan < 0 && Math.Abs(b2 - c) < E) best = Math.Max(best, d.Dk);
                if (yan > 0 && Math.Abs(b1 - c) < E) best = Math.Max(best, d.Dk);
            }
            return best;
        }

        /// <summary>
        /// Kiriş grupları; <paramref name="panel"/> ise yalnız bodrum perde panelleri (STA4 KSF bunları Kolon satırına sayar;
        /// kalıp iki yüz Ln·2H). Kiriş kalıbı Ln·(bw + d1 + d2): KSF, GPR'deki düğümsüz uç düşümünü yapmaz.
        /// </summary>
        private static BetonKalip KirisKat(Geo g, int kat, bool panel = false)
        {
            float sk = 0, sb = 0;
            foreach (var grp in Gruplar(g, kat))
            {
                if (grp[0].Panel != panel) continue;
                var k0 = grp[0]; var kn = grp[grp.Count - 1];
                // gerçek boy (eğik aks): uç kesişimleri arası; eğimsizde aks farkı
                float L = Math.Abs((float)kn.Pe - (float)k0.Ps) * (float)Egiklik(k0, kn);
                var (ta, tb) = UcTasma(g, grp);
                float Ln = L - ta - tb;
                double s1 = Math.Min(k0.Ps, kn.Pe), s2 = Math.Max(k0.Ps, kn.Pe);
                int d1 = k0.H - YanDoseme(g, k0, -1, s1, s2), d2 = k0.H - YanDoseme(g, k0, +1, s1, s2);
                Iz?.Add(string.Format(Inv, "K|{0}|{1}|B{2} H{3} L{4:F3} Ln{5:F3} d1 {6} d2 {7}", kat, k0.No, k0.B, k0.H, L, Ln, d1, d2));
                sk += panel ? Ln * 2 * k0.H / 100f : Ln * (k0.B + d1 + d2) / 100f;
                sb += k0.B / 100f * (k0.H / 100f) * Ln;
            }
            return new BetonKalip { Beton = sb, Kalip = sk };
        }

        // ------------------------------------------------------------------ kolon
        private static IEnumerable<Kir> KolonaDegen(Geo g, Kol c)
        {
            foreach (var k in g.Kirisler.Where(x => x.Kat == c.Kat))
            {
                if (k.Sira > 0 && g.Kirisler[k.Sira - 1].No == k.No) continue;   // birleşik kirişin 2.+ parçası
                var (b1, b2, s1, s2) = Bant(g, k);
                bool xYon = k.Aks >= 2000;
                double c1 = xYon ? c.X1 : c.Y1, c2 = xYon ? c.X2 : c.Y2, d1 = xYon ? c.Y1 : c.X1, d2 = xYon ? c.Y2 : c.X2;
                if (Math.Min(b2, d2) - Math.Max(b1, d1) < E) continue;
                if (Math.Min(s2, c2) - Math.Max(s1, c1) < -E) continue;
                yield return k;
            }
        }

        private static BetonKalip KolonKat(Geo g, int kat)
        {
            double sk = 0, sb = 0;
            foreach (var c in g.Kolonlar.Where(c => c.Kat == kat))
            {
                float hk = (float)c.Ust - (float)c.Alt;
                if (hk <= 0 && kat > 0) hk = (float)g.Katlar[kat].Kot - (float)g.Katlar[kat - 1].Kot;
                Iz?.Add(string.Format(Inv, "C|{0}|{1}|bx{2} by{3} hk{4:F3} pol{5} degen {6}", kat, c.No, c.Bx, c.By, hk, c.Poligon, string.Join(",", KolonaDegen(g, c).Select(x => x.No + ":" + x.B + "x" + x.H))));
                sk += (float)c.Cevre * hk - KolonaDegen(g, c).Sum(x => x.B / 100f * (x.H / 100f));
                sb += (float)c.Alan * hk;
            }
            return new BetonKalip { Beton = sb, Kalip = sk };
        }

        // ------------------------------------------------------------------ döşeme
        private static (float l, float r, float b, float t) KenarTasma(Geo g, Dos d)
        {
            float l = 0, r = 0, b = 0, t = 0;
            foreach (var k in g.Kirisler.Where(k => k.Kat == d.Kat))
            {
                var (b1, b2, s1, s2) = Bant(g, k);
                double c = k.C;
                if (k.Aks >= 2000)
                {
                    if (Math.Min(s2, d.X2) - Math.Max(s1, d.X1) < E) continue;
                    if (Math.Abs(c - d.Y1) < E) b = Math.Max(b, (float)(Math.Min(b2, d.Y2) - d.Y1));
                    if (Math.Abs(c - d.Y2) < E) t = Math.Max(t, (float)(d.Y2 - Math.Max(b1, d.Y1)));
                }
                else
                {
                    if (Math.Min(s2, d.Y2) - Math.Max(s1, d.Y1) < E) continue;
                    if (Math.Abs(c - d.X1) < E) l = Math.Max(l, (float)(Math.Min(b2, d.X2) - d.X1));
                    if (Math.Abs(c - d.X2) < E) r = Math.Max(r, (float)(d.X2 - Math.Max(b1, d.X1)));
                }
            }
            return (Math.Max(l, 0), Math.Max(r, 0), Math.Max(b, 0), Math.Max(t, 0));
        }

        /// <summary>
        /// Döşeme: net alan = Lx·Ly (kenar kirişi taşmaları düşülmüş). STA4 KSF: kalıp = net − kolon izdüşümü (serbest kenar
        /// yüzü eklenmez), beton = net·d (kolon izdüşümü düşülmez).
        /// </summary>
        private static BetonKalip DosemeKat(Geo g, int kat)
        {
            float sk = 0, sb = 0;
            foreach (var d in g.Dosemeler.Where(d => d.Kat == kat))
            {
                var (tl, tr, tb, tt) = KenarTasma(g, d);
                if (Math.Abs(d.F5) > d.Dk * 10) { tl = tr = tb = tt = 0; }   // merdiven sahanlığı
                float lx = (float)d.X2 - (float)d.X1 - tl - tr, ly = (float)d.Y2 - (float)d.Y1 - tb - tt;
                float alan = lx * ly * (float)d.Oran;
                double x1 = d.X1 + tl, x2 = d.X2 - tr, y1 = d.Y1 + tb, y2 = d.Y2 - tt, delik = 0;
                foreach (var c in g.Kolonlar.Where(c => c.Kat == kat))
                {
                    double ox = Math.Min(x2, c.X2) - Math.Max(x1, c.X1), oy = Math.Min(y2, c.Y2) - Math.Max(y1, c.Y1);
                    if (ox > E && oy > E) delik += ox * oy;
                }
                Iz?.Add(string.Format(Inv, "D|{0}|{1}|d{2} lx{3:F3} ly{4:F3} delik{5:F3}", kat, d.No, d.Dk, lx, ly, delik));
                sk += (float)(alan - delik);
                sb += alan * (d.Dk / 100f);
            }
            return new BetonKalip { Beton = sb, Kalip = sk };
        }

        // ------------------------------------------------------------------ proje
        public static BetonKalipProjesi Oku(string st4)
        {
            string[] l = RadyeOtomatikSource.ReadRadText(st4).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var p = new BetonKalipProjesi { St4Yolu = st4 };
            var g = Oku(l);
            if (g == null) return p;

            var katlar = g.Kolonlar.Select(c => c.Kat).Concat(g.Kirisler.Select(k => k.Kat)).Distinct().OrderBy(x => x).ToList();
            foreach (int kat in katlar)
            {
                if (kat < 0 || kat >= g.Katlar.Count) continue;
                var k = new BetonKalipKat { Indeks = kat, Ad = g.Katlar[kat].Ad, Kod = g.Katlar[kat].Kod };
                if (g.Egik)
                {
                    k.Doseme.Ekle(DosemeKatEgik(g, kat));
                    k.Kiris.Ekle(KirisKatEgik(g, kat, false));
                    k.Kolon.Ekle(KolonKatEgik(g, kat));
                    k.Kolon.Ekle(KirisKatEgik(g, kat, true));
                }
                else
                {
                    k.Doseme.Ekle(DosemeKat(g, kat));
                    k.Kiris.Ekle(KirisKat(g, kat));
                    k.Kolon.Ekle(KolonKat(g, kat));
                    k.Kolon.Ekle(KirisKat(g, kat, true));
                }
                p.Katlar.Add(k);
            }
            // kolon/kirişi olmayan alt katların döşemeleri (subasman S katı): STA4 özetinde yok, SUBASMAN'a
            foreach (int kat in g.Dosemeler.Select(d => d.Kat).Distinct().Where(x => !katlar.Contains(x)))
                p.SubasmanDoseme.Ekle(DosemeKat(g, kat));

            // temel / subasman: eğik akslar dahil gerçek aks doğrularıyla
            var ag = new AksAg { Nx = g.Nx };
            int ka = Array.FindIndex(l, s => s.StartsWith("/Axis data/", StringComparison.Ordinal));
            if (ka >= 0)
                for (int i = ka + 1; i < l.Length && !l[i].StartsWith("/"); i++) { var f = F(l[i]); ag.Satir.Add((D(f[0]), D(Al(f, 1)))); }
            var kolonAksi = new List<double[]>();
            int kc = Array.FindIndex(l, s => s.StartsWith("/Column axis data/", StringComparison.Ordinal));
            if (kc >= 0)
                for (int i = kc + 1; i < l.Length && !l[i].StartsWith("/"); i++) kolonAksi.Add(F(l[i]).Select(D).ToArray());
            // taban kolonları (en alt kolonlu kat): bağ kirişleri bunlarla kesilir
            Geometry tabanKolon = null;
            if (g.Kolonlar.Count > 0)
            {
                int kmin = g.Kolonlar.Min(c => c.Kat);
                var kp = g.Kolonlar.Where(c => c.Kat == kmin && c.X2 > c.X1 && c.Y2 > c.Y1)
                    .Select(c => (Geometry)Dikdortgen(new[] { (c.X1, c.Y1), (c.X2, c.Y1), (c.X2, c.Y2), (c.X1, c.Y2) })).ToList();
                if (kp.Count > 0) tabanKolon = NtsEk.Birlesim(kp);
            }
            p.TemelHesapla(l, ag, kolonAksi, tabanKolon);

            // subasman döşemesi: hatıl / bağ kirişi / kolon izdüşümleri arasında (zemine oturur: kalıp yok, STA4 KSF de yazmaz)
            var sKatlar = g.Dosemeler.Select(d => d.Kat).Distinct().Where(x => !katlar.Contains(x)).ToList();
            if (sKatlar.Count > 0)
            {
                Geometry kes = p.DestekU;
                if (tabanKolon != null) kes = kes == null ? tabanKolon : kes.Birles(tabanKolon);
                double sb = 0;
                foreach (var d in g.Dosemeler.Where(d => sKatlar.Contains(d.Kat) && d.X2 > d.X1 && d.Y2 > d.Y1))
                {
                    Geometry pol = Dikdortgen(new[] { (d.X1, d.Y1), (d.X2, d.Y1), (d.X2, d.Y2), (d.X1, d.Y2) });
                    if (kes != null && !kes.IsEmpty) pol = pol.Fark(kes);
                    sb += pol.Area * d.Dk / 100.0;
                }
                p.SubasmanDoseme.Beton = sb; p.SubasmanDoseme.Kalip = 0;
            }
            if (p.SubasmanDoseme.Beton > 0) Kalem(p.Subasman, "Subasman döşemesi").Ekle(p.SubasmanDoseme);
            return p;
        }
    }
}
