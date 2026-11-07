using System.Globalization;

namespace StatikRaporClaude.Uretec;

/// <summary>
/// STA4 keşif metrajı bölümleri (GPR'siz): temel donatı listesi (.MATBQF), kat bazında kiriş / kolon / döşeme kalıp-beton
/// (ST4 geometrisi + .FLO) ve yapı beton/kalıp özeti. Donatı boyu gerektiren tablolar (kiriş/kolon/döşeme donatı, duvar,
/// donatı kg özeti) STA4 çizim algoritmasına bağlı olduğundan üretilmez. Kurallar: scratchpad ara_metraj/RAPOR.md (A1–A4, A8).
/// </summary>
public static class Metraj
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;
    const string V = "¡";
    const double E = 0.005;
    static string Cizgi(char sol, char ara, char sag, params int[] w) => sol + string.Join(ara.ToString(), w.Select(x => new string('Ä', x))) + sag;
    /// <summary>float32 değerin kesin ikili değeri yuvarlanır (kolon/döşeme metrajı; 6.6949997 → 6.69).</summary>
    static string Fx(double v, int d) { double x = (float)v; var s = x.ToString("F" + d, ci); return s.TrimStart('-').Trim('0', '.').Length == 0 ? s.TrimStart('-') : s; }

    // ------------------------------------------------------------------ geometri
    sealed class Kol { public int No, Kat, Yerel, Bx, By, Sira; public double Ust, Alt, X1, X2, Y1, Y2; }
    sealed class Kir { public int No, Kat, B, H, Aks, S, E, Kod, I, J, Sira; }
    sealed class Dos { public int No, Kat, Dk, F5, Sira; public double X1, X2, Y1, Y2; }

    sealed class Geo
    {
        public List<double> Eks = new(); public int Nx;
        public List<(string Ad, double Kot, string Kod)> Katlar = new();
        public List<Kol> Kolonlar = new(); public List<Kir> Kirisler = new(); public List<Dos> Dosemeler = new();
        public double X(int a) => Eks[a - 1001];
        public double Y(int a) => Eks[Nx + a - 2001];
        public double A(int a) => a < 2000 ? X(a) : Y(a);
        public string KatKod(int k) => k >= 0 && k < Katlar.Count ? Katlar[k].Kod : k.ToString(ci);
        public int Taban(IEnumerable<int> nolar) => nolar.DefaultIfEmpty(0).Max() / 100 >= Math.Max(Katlar.Count, 1) ? 1000 : 100;
    }

    static double Merkez(double aks, double kod, double b) =>
        kod == 0 ? aks : kod == -1 ? aks + b / 2 : kod == 1 ? aks - b / 2 : kod < 0 ? aks + kod / 1000 + b / 2 : aks + kod / 1000 - b / 2;

    static Geo Oku(Proj p)
    {
        if (p.St4 == null) return null;
        var l = p.St4; var g = new Geo();
        int N(string s) => (int)Math.Round(Proj.D(s));
        g.Nx = N(Proj.F(l[4])[0]);
        int k = p.Sec("/Story/");
        if (k < 0) return null;
        for (int i = k + 1; i + 2 < l.Length && !l[i].StartsWith("/"); i += 3) g.Katlar.Add((l[i].Trim(), Proj.D(Proj.F(l[i + 2])[0]), l[i + 1].Trim().TrimEnd('-')));
        k = p.Sec("/Axis data/");
        for (int i = k + 1; i < l.Length && !l[i].StartsWith("/"); i++) g.Eks.Add(Proj.D(Proj.F(l[i])[1]));
        var rows = new List<double[]>();
        k = p.Sec("/Column axis data/");
        if (k >= 0) for (int i = k + 1; i < l.Length && !l[i].StartsWith("/"); i++) rows.Add(Proj.F(l[i]).Select(Proj.D).ToArray());
        var kol = new List<Kol>();
        k = p.Sec("/Columns Data/");
        if (k >= 0)
            for (int i = k + 1; i + 2 < l.Length && !l[i].StartsWith("/"); i += 3)
            {
                var f = Proj.F(l[i]); int no = N(f[0]); if (no == 0) break;
                var f2 = Proj.F(l[i + 1]);
                kol.Add(new Kol { No = no, Bx = N(f[1]), By = N(f[2]), Ust = Proj.D(f2.ElementAtOrDefault(6) ?? "0"), Alt = Proj.D(f2.ElementAtOrDefault(7) ?? "0"), Sira = kol.Count });
            }
        int tk = g.Taban(kol.Select(c => c.No));
        foreach (var c in kol)
        {
            c.Kat = c.No / tk; c.Yerel = c.No % tk;
            if (c.Yerel >= 1 && c.Yerel - 1 < rows.Count)
            {
                var r = rows[c.Yerel - 1];
                double cx = Merkez(g.X((int)r[1]), r[3], c.Bx / 100.0), cy = Merkez(g.Y((int)r[2]), r[4], c.By / 100.0);
                c.X1 = cx - c.Bx / 200.0; c.X2 = cx + c.Bx / 200.0; c.Y1 = cy - c.By / 200.0; c.Y2 = cy + c.By / 200.0;
            }
        }
        g.Kolonlar = kol;
        k = p.Sec("/Beams Data/");
        if (k >= 0)
            for (int i = k + 1; i + 2 < l.Length && !l[i].StartsWith("/"); i += 3)
            {
                var f = Proj.F(l[i]); int no = N(f[0]); if (no == 0) break;
                var f2 = Proj.F(l[i + 1]);
                g.Kirisler.Add(new Kir { No = no, B = N(f[1]), H = N(f[2]), Aks = N(f[4]), S = N(f[5]), E = N(f[6]), Kod = N(f[7]), I = N(f2[4]), J = N(f2[5]), Sira = g.Kirisler.Count });
            }
        int tb = g.Taban(g.Kirisler.Select(x => x.No));
        foreach (var x in g.Kirisler) x.Kat = x.No / tb;
        k = p.Sec("/Floors Data/");
        if (k >= 0)
            for (int i = k + 1; i + 1 < l.Length && !l[i].StartsWith("/"); i += 2)
            {
                var f = Proj.F(l[i]); int no = N(f[0]); if (no == 0) break;
                int a1 = N(f[8]), a2 = N(f[9]), b1 = N(f[10]), b2 = N(f[11]);
                g.Dosemeler.Add(new Dos
                {
                    No = no, Dk = N(f[1]), F5 = N(f[5]), Sira = g.Dosemeler.Count,
                    X1 = Math.Min(g.X(a1), g.X(a2)), X2 = Math.Max(g.X(a1), g.X(a2)), Y1 = Math.Min(g.Y(b1), g.Y(b2)), Y2 = Math.Max(g.Y(b1), g.Y(b2))
                });
            }
        int td = g.Taban(g.Dosemeler.Select(x => x.No));
        foreach (var x in g.Dosemeler) x.Kat = x.No / td;
        return g;
    }

    static string Ad(Geo g, char on, int no, int kat) => $"{on}{g.KatKod(kat)}-{(no % 100):00}";

    /// <summary>Kiriş bandı (dik doğrultuda: kod −1 aks..aks+B, +1 aks−B..aks, 0 ortalı) ve boyuna aralık.</summary>
    static (double b1, double b2, double s1, double s2) Bant(Geo g, Kir k)
    {
        double a = g.A(k.Aks), b = k.B / 100.0;
        double b1 = k.Kod == 1 ? a - b : k.Kod == -1 ? a : a - b / 2;
        return (b1, b1 + b, Math.Min(g.A(k.S), g.A(k.E)), Math.Max(g.A(k.S), g.A(k.E)));
    }

    static List<List<Kir>> Gruplar(Geo g, int kat)
    {
        var res = new List<List<Kir>>();
        foreach (var k in g.Kirisler.Where(x => x.Kat == kat))
            if (res.Count > 0 && res[^1][^1].No == k.No) res[^1].Add(k); else res.Add(new List<Kir> { k });
        return res;
    }

    static string KatBaslik(Geo g, int kat) => $" {kat} > {g.Katlar[kat].Ad} KAT";

    static IEnumerable<int> KolonluKatlar(Geo g) => g.Kolonlar.Select(c => c.Kat).Concat(g.Kirisler.Select(k => k.Kat)).Distinct().OrderBy(x => x);

    // ------------------------------------------------------------------ A2 kiriş
    static (float ta, float tb) UcTasma(Geo g, List<Kir> grp)
    {
        var k0 = grp[0]; var kn = grp[^1];
        bool xYon = k0.Aks >= 2000;
        var (b1, b2, _, _) = Bant(g, k0);
        float s1 = (float)Math.Min(g.A(k0.S), g.A(kn.E)), s2 = (float)Math.Max(g.A(k0.S), g.A(kn.E));
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

    static int YanDoseme(Geo g, Kir k, int yan, double s1, double s2)
    {
        int best = 0; bool xYon = k.Aks >= 2000; double c = g.A(k.Aks);
        foreach (var d in g.Dosemeler.Where(d => d.Kat == k.Kat))
        {
            double a1 = xYon ? d.X1 : d.Y1, a2 = xYon ? d.X2 : d.Y2, b1 = xYon ? d.Y1 : d.X1, b2 = xYon ? d.Y2 : d.X2;
            if (Math.Min(a2, s2) - Math.Max(a1, s1) < 0.01) continue;
            if (yan < 0 && Math.Abs(b2 - c) < E) best = Math.Max(best, d.Dk);
            if (yan > 0 && Math.Abs(b1 - c) < E) best = Math.Max(best, d.Dk);
        }
        return best;
    }

    /// <summary>Düğümsüz (I/J = 0) uç noktalarına değen dik kiriş kayıtları (her nokta bir kez).</summary>
    static IEnumerable<Kir> Dugumsuz(Geo g, List<Kir> grp)
    {
        var k0 = grp[0]; bool xYon = k0.Aks >= 2000;
        var pts = new HashSet<(int, int)>();
        foreach (var k in grp) { if (k.I == 0) pts.Add((k.Aks, k.S)); if (k.J == 0) pts.Add((k.Aks, k.E)); }
        foreach (var (aks, pt) in pts)
            foreach (var k in g.Kirisler.Where(x => x.Kat == k0.Kat && (x.Aks >= 2000) != xYon))
                if (k.Aks == pt && (k.S == aks || k.E == aks)) yield return k;
    }

    sealed class KatToplam { public double Kalip, Beton; }

    static List<string> KirisKat(Geo g, int kat, KatToplam top)
    {
        var gr = Gruplar(g, kat);
        if (gr.Count == 0) return null;
        int[] w = { 6, 5, 5, 5, 5, 6, 9, 9 };
        var o = new List<string> { KatBaslik(g, kat) + " KİRİŞ KALIP VE BETON KEŞİF METRAJI", Cizgi('Ú', 'Â', '¿', w),
            "¡Kiriş ¡  bw ¡  H  ¡  d1 ¡  d2 ¡  Ln  ¡  Kalıp  ¡  Beton  ¡", "¡  no  ¡  cm ¡  cm ¡  cm ¡  cm ¡   m  ¡    m²   ¡    m³   ¡", Cizgi('Ã', 'Å', '´', w) };
        float sk = 0, sb = 0;
        foreach (var grp in gr)
        {
            var k0 = grp[0]; var kn = grp[^1];
            float L = Math.Abs((float)g.A(kn.E) - (float)g.A(k0.S));
            var (ta, tb) = UcTasma(g, grp);
            float Ln = L - ta - tb;
            double s1 = Math.Min(g.A(k0.S), g.A(kn.E)), s2 = Math.Max(g.A(k0.S), g.A(kn.E));
            int d1 = k0.H - YanDoseme(g, k0, -1, s1, s2), d2 = k0.H - YanDoseme(g, k0, +1, s1, s2);
            float kal = Ln * (k0.B + d1 + d2) / 100f - Dugumsuz(g, grp).Sum(s => s.B * s.H / 10000f);
            float bet = k0.B / 100f * (k0.H / 100f) * Ln;
            sk += kal; sb += bet;
            o.Add(V + Ad(g, 'K', k0.No, kat).PadRight(6) + V + k0.B.ToString(ci).PadLeft(5) + V + k0.H.ToString(ci).PadLeft(5) + V + d1.ToString(ci).PadLeft(5) + V + d2.ToString(ci).PadLeft(5)
                + V + Fmt.N(Ln, 2).PadLeft(6) + V + Fmt.N(kal, 2).PadLeft(9) + V + Fmt.N(bet, 2).PadLeft(9) + V);
        }
        o.Add(Cizgi('À', 'Á', 'Ù', w));
        o.Add(Fmt.N(sk, 2).PadLeft(48) + Fmt.N(sb, 2).PadLeft(10));
        top.Kalip = sk; top.Beton = sb;
        return o;
    }

    /// <summary>"n > KAT İSMİ KAT KİRİŞ KALIP VE BETON KEŞİF METRAJI" tabloları (kat kat). N_ULK_02 512/512 hücre, alt toplam 3/4.</summary>
    public static List<string> KirisKalipBeton(Proj p)
    {
        var g = Oku(p); if (g == null || g.Kirisler.Count == 0) return null;
        var o = new List<string>();
        foreach (int kat in KolonluKatlar(g)) { var t = KirisKat(g, kat, new KatToplam()); if (t != null) o.AddRange(t); }
        return o.Count == 0 ? null : o;
    }

    // ------------------------------------------------------------------ A3 kolon
    static List<Kir> KolonaDegen(Geo g, Kol c)
    {
        var r = new List<Kir>();
        foreach (var k in g.Kirisler.Where(x => x.Kat == c.Kat))
        {
            if (k.Sira > 0 && g.Kirisler[k.Sira - 1].No == k.No) continue;   // birleşik kirişin 2.+ parçası
            var (b1, b2, s1, s2) = Bant(g, k);
            bool xYon = k.Aks >= 2000;
            double c1 = xYon ? c.X1 : c.Y1, c2 = xYon ? c.X2 : c.Y2, d1 = xYon ? c.Y1 : c.X1, d2 = xYon ? c.Y2 : c.X2;
            if (Math.Min(b2, d2) - Math.Max(b1, d1) < E) continue;
            if (Math.Min(s2, c2) - Math.Max(s1, c1) < -E) continue;
            r.Add(k);
        }
        return r;
    }

    /// <summary>STA4 sırası: ST4 sırasında gelen kolon, listenin sonundakinden küçük numaralıysa başa, değilse sona eklenir.</summary>
    static List<Kol> KolonSirasi(IEnumerable<Kol> ks)
    {
        var l = new List<Kol>();
        foreach (var c in ks) if (l.Count > 0 && c.No < l[^1].No) l.Insert(0, c); else l.Add(c);
        return l;
    }

    static List<string> KolonKat(Geo g, int kat, KatToplam top)
    {
        var ks = KolonSirasi(g.Kolonlar.Where(c => c.Kat == kat));
        if (ks.Count == 0) return null;
        int[] w = { 6, 12, 5, 5, 5, 6, 9, 9 };
        var o = new List<string> { KatBaslik(g, kat) + " KOLON KALIP VE BETON KEŞİF METRAJI", Cizgi('Ú', 'Â', '¿', w),
            "¡Kolon ¡     tip    ¡  bx ¡  by ¡  Uk ¡  Hk  ¡   Kalıp ¡  Beton  ¡", "¡  no  ¡            ¡  cm ¡  cm ¡  cm ¡   m  ¡     m²  ¡    m³   ¡", Cizgi('Ã', 'Å', '´', w) };
        double sk = 0, sb = 0;
        foreach (var c in ks)
        {
            float hk = (float)c.Ust - (float)c.Alt;
            if (hk <= 0 && kat > 0) hk = (float)g.Katlar[kat].Kot - (float)g.Katlar[kat - 1].Kot;
            int uk = 2 * (c.Bx + c.By);
            float kal = (float)uk / 100f * hk - KolonaDegen(g, c).Sum(x => x.B / 100f * (x.H / 100f));
            float bet = c.Bx / 100f * (c.By / 100f) * hk;
            sk += kal; sb += bet;
            o.Add(V + Ad(g, 'S', c.No, kat).PadRight(6) + V + " Dikdortgen " + V + c.Bx.ToString(ci).PadLeft(5) + V + c.By.ToString(ci).PadLeft(5) + V + uk.ToString(ci).PadLeft(5)
                + V + Fx(hk, 2).PadLeft(6) + V + Fx(kal, 2).PadLeft(9) + V + Fx(bet, 2).PadLeft(9) + V);
        }
        o.Add(Cizgi('À', 'Á', 'Ù', w));
        o.Add(Fx(sk, 2).PadLeft(55) + Fx(sb, 3).PadLeft(10));
        top.Kalip = sk; top.Beton = sb;
        return o;
    }

    /// <summary>"n > KAT İSMİ KAT KOLON KALIP VE BETON KEŞİF METRAJI" tabloları. N_ULK_02 352/352 hücre, alt toplam 4/4 (yalnız dikdörtgen kolon).</summary>
    public static List<string> KolonKalipBeton(Proj p)
    {
        var g = Oku(p); if (g == null || g.Kolonlar.Count == 0) return null;
        var o = new List<string>();
        foreach (int kat in KolonluKatlar(g)) { var t = KolonKat(g, kat, new KatToplam()); if (t != null) o.AddRange(t); }
        return o.Count == 0 ? null : o;
    }

    // ------------------------------------------------------------------ A4 döşeme
    static (float l, float r, float b, float t) KenarTasma(Geo g, Dos d)
    {
        float l = 0, r = 0, b = 0, t = 0;
        foreach (var k in g.Kirisler.Where(k => k.Kat == d.Kat))
        {
            var (b1, b2, s1, s2) = Bant(g, k);
            double c = g.A(k.Aks);
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

    /// <summary>FLO (200 B, ST4 sırası) +60+4k kenar kirişi, +76+2k komşu döşeme; ikisi 0 ise serbest kenar: k=0,1 → Lx, k=2,3 → Ly.</summary>
    static float Serbest(byte[] flo, Dos d, float lx, float ly)
    {
        int o = 200 * d.Sira; float s = 0;
        if (flo == null || o + 84 > flo.Length) return 0;
        for (int k = 0; k < 4; k++)
            if (BitConverter.ToInt16(flo, o + 60 + 4 * k) == 0 && BitConverter.ToInt16(flo, o + 76 + 2 * k) == 0) s += k < 2 ? lx : ly;
        return s;
    }

    static List<string> DosemeKat(Geo g, byte[] flo, int kat, int altKat, KatToplam top)
    {
        var ds = g.Dosemeler.Where(d => d.Kat <= kat && d.Kat >= altKat).OrderBy(d => d.Kat).ThenBy(d => d.No).ToList();
        if (ds.Count == 0) return null;
        int[] w = { 6, 5, 6, 6, 6, 6, 9, 9, 10 };
        var o = new List<string> { KatBaslik(g, kat) + " DÖŞEME KALIP VE BETON KEŞİF METRAJI", Cizgi('Ú', 'Â', '¿', w),
            "¡Döşeme¡  d  ¡  Lx1 ¡  Lx2 ¡  Ly1 ¡  Ly2 ¡  Kalıp  ¡  Beton  ¡Plak Dolgu¡", "¡  no  ¡ cm  ¡   m  ¡   m  ¡   m  ¡   m  ¡    m²   ¡    m³   ¡     m³   ¡", Cizgi('Ã', 'Å', '´', w) };
        double sk = 0, sbTum = 0, sbKat = 0;
        foreach (var d in ds)
        {
            var (tl, tr, tb, tt) = KenarTasma(g, d);
            if (Math.Abs(d.F5) > d.Dk * 10) { tl = tr = tb = tt = 0; }
            float lx = (float)d.X2 - (float)d.X1 - tl - tr, ly = (float)d.Y2 - (float)d.Y1 - tb - tt;
            float alan = lx * ly;
            float kal = alan + d.Dk / 100f * Serbest(flo, d, lx, ly), bet = alan * (d.Dk / 100f);
            if (d.Kat == kat) { sk += kal; sbKat += bet; }
            sbTum += bet;
            string ls = Fx(lx, 2).PadLeft(6), lys = Fx(ly, 2).PadLeft(6);
            o.Add(V + Ad(g, 'D', d.No, d.Kat).PadRight(6) + V + d.Dk.ToString(ci).PadLeft(5) + V + ls + V + ls + V + lys + V + lys
                + V + Fx(kal, 2).PadLeft(9) + V + Fx(bet, 2).PadLeft(9) + V + "0.00".PadLeft(10) + V);
        }
        o.Add(Cizgi('À', 'Á', 'Ù', w));
        o.Add(Fx(sk, 2).PadLeft(51) + Fx(sbTum, 2).PadLeft(10) + "0.00".PadLeft(11));
        top.Kalip = sk; top.Beton = sbKat;   // özet: yalnız katın kendi döşemeleri
        return o;
    }

    static int AltKat(Geo g, int kat, List<int> katlar) => kat == katlar[0] ? 0 : kat;

    /// <summary>"n > KAT İSMİ KAT DÖŞEME KALIP VE BETON KEŞİF METRAJI" tabloları (ilk katta alt kotlu S katı döşemeleri de listelenir). N_ULK_02 508/513, alt toplam 6/6.</summary>
    public static List<string> DosemeKalipBeton(Proj p)
    {
        var g = Oku(p); if (g == null || g.Dosemeler.Count == 0) return null;
        var flo = p.Bytes(".FLO");
        var katlar = KolonluKatlar(g).ToList();
        var o = new List<string>();
        foreach (int kat in katlar) { var t = DosemeKat(g, flo, kat, AltKat(g, kat, katlar), new KatToplam()); if (t != null) o.AddRange(t); }
        return o.Count == 0 ? null : o;
    }

    // ------------------------------------------------------------------ A1 temel donatı
    /// <summary>TEMEL DONATI KEŞİF METRAJI: .MATBQF (8 B kayıt: adet, çap, k, boy int16) aynen; ad ilk radye plağı. N_ULK_02 420/420.</summary>
    public static List<string> TemelDonatiListesi(Proj p)
    {
        var b = p.Bytes(".MATBQF");
        if (b == null || b.Length < 8) return null;
        string ad = "";
        int s = p.St4 == null ? -1 : p.Sec("/Slab foundations/");
        if (s >= 0 && s + 1 < p.St4.Length) ad = p.St4[s + 1].Trim();
        int[] w = { 6, 9, 4, 4, 5, 9, 4, 4, 5, 9, 4, 4, 5 };
        var o = new List<string> { "TEMEL DONATI KEŞİF METRAJI", Cizgi('Ú', 'Â', '¿', w),
            "¡Temel ¡  donatı ¡adet¡ çap¡ boy ¡  donatı ¡adet¡ çap¡ boy ¡  donatı ¡adet¡ çap¡ boy ¡", Cizgi('Ã', 'Å', '´', w) };
        int n = b.Length / 8;
        for (int i = 0; i < n; i += 3)
        {
            string sat = V + (i == 0 ? ad : "").PadRight(6);
            for (int j = i; j < i + 3; j++)
            {
                if (j < n)
                {
                    short a = BitConverter.ToInt16(b, 8 * j), d = BitConverter.ToInt16(b, 8 * j + 2), L = BitConverter.ToInt16(b, 8 * j + 6);
                    sat += V + "Mat Duz  " + V + a.ToString(ci).PadLeft(4) + V + (d.ToString(ci).PadLeft(3) + " ") + V + L.ToString(ci).PadLeft(5);
                }
                else sat += V + new string(' ', 9) + V + "    " + V + "    " + V + "     ";
            }
            o.Add(sat + V);
        }
        o.Add(Cizgi('À', 'Á', 'Ù', w));
        return o;
    }

    // ------------------------------------------------------------------ A8 özet
    /// <summary>
    /// "&lt;PROJE&gt;.ST4 YAPISI BETON/KALIP KEŞİF METRAJI": kat satırları döşeme/kiriş/kolon tablolarının alt toplamları
    /// (döşeme betonu S katı hariç). TEMEL satırı üretilemediği için yok; bu yüzden TOPLAM satırı GPR'dekinden temel kadar küçüktür.
    /// </summary>
    public static List<string> BetonKalipOzeti(Proj p)
    {
        var g = Oku(p); if (g == null) return null;
        var flo = p.Bytes(".FLO");
        var katlar = KolonluKatlar(g).ToList();
        if (katlar.Count == 0) return null;
        int[] w = { 22, 9, 9, 10 };
        var o = new List<string> { $"{p.Base}.ST4 YAPISI BETON/KALIP KEŞİF METRAJI", Cizgi('Ú', 'Â', '¿', w),
            "¡Kat > Kat ismi        ¡   Beton ¡  Kalıp  ¡Plak Dolgu¡", "¡                      ¡     m³  ¡    m²   ¡     m³   ¡", Cizgi('Ã', 'Å', '´', w) };
        string Sat(string a, double bet, double kal, bool dolgu) => V + a.PadRight(22) + V + Fx(bet, 2).PadLeft(9) + V + Fx(kal, 2).PadLeft(9) + V + (dolgu ? "0.00".PadLeft(10) : new string(' ', 10)) + V;
        double tb = 0, tk = 0;
        for (int i = 0; i < katlar.Count; i++)
        {
            int kat = katlar[i];
            KatToplam d = new(), k = new(), c = new();
            DosemeKat(g, flo, kat, AltKat(g, kat, katlar), d); KirisKat(g, kat, k); KolonKat(g, kat, c);
            string on = $" {kat}> " + g.Katlar[kat].Ad.PadRight(11);
            o.Add(Sat(on + "Döşeme ", d.Beton, d.Kalip, true));
            o.Add(V + (on + "Kiriş  ").PadRight(22) + V + Fx(k.Beton, 2).PadLeft(9) + V + Fmt.N(k.Kalip, 2).PadLeft(9) + V + new string(' ', 10) + V);   // kiriş: G7 yarım-yukarı
            o.Add(Sat(on + "Kolon  ", c.Beton, c.Kalip, false));
            o.Add(Cizgi('Ã', 'Å', '´', w));
            double sb = d.Beton + k.Beton + c.Beton, sk = d.Kalip + k.Kalip + c.Kalip;
            o.Add(Sat(on + "Toplam ", sb, sk, true));
            o.Add(Cizgi('Ã', 'Å', '´', w));
            tb += sb; tk += sk;
        }
        o.Add(Sat(" TOPLAM", tb, tk, true));
        o.Add(Cizgi('À', 'Á', 'Ù', w));
        return o;
    }
}
