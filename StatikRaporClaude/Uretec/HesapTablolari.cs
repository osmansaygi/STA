using System.Globalization;
using StatikRaporClaude.Veri;

namespace StatikRaporClaude.Uretec;

/// <summary>
/// STA4 "Hesap çıktıları" ara hesap bölümleri (GPR satırı üreteçleri; GPR okumaz): duvar yükleri, döşeme şerit/bilgileri,
/// kiriş/panel bilgileri, kiriş sehim-çatlak, kolon/perde kesme, kiriş kesme-burulma ve askı, temel kolon yükleri ve sünek
/// momentler, burkulma, perde-diyafram. Her fonksiyon üretemezse null döner. Kaynak ofsetleri ve doğrulama oranları her
/// bölümün açıklamasında; kalibrasyon araçları ve raporlar scratchpad ara_hesap klasöründe.
/// </summary>
// ================= Ortak yardımcılar =================
public static partial class HesapTablolari
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const char H = '¡';   // hücre ayracı (1254 0xA1)

    // ---------- tablo çizimi ----------
    /// <summary>Çerçeve çizgisi: sol + (Ä × genişlik) ara ile + sağ.</summary>
    internal static string Cizgi(char sol, char ara, char sag, params int[] en) =>
        sol + string.Join(ara.ToString(), en.Select(w => new string('Ä', w))) + sag;
    internal static string Ust(params int[] en) => Cizgi('Ú', 'Â', '¿', en);
    internal static string Ayrac(params int[] en) => Cizgi('Ã', 'Å', '´', en);
    internal static string Alt(params int[] en) => Cizgi('À', 'Á', 'Ù', en);
    /// <summary>Satır: hücreler zaten genişliğe göre hazırlanmış.</summary>
    internal static string Satir(params string[] h) => H + string.Join(H.ToString(), h) + H;
    internal static string Sol(string s, int w) => (s ?? "").Length >= w ? (s ?? "").Substring(0, w) : (s ?? "").PadRight(w);
    internal static string Sag(string s, int w) => (s ?? "").Length >= w ? (s ?? "") : (s ?? "").PadLeft(w);
    /// <summary>STA4 sayı biçimi (float32 → G7 → yarım yukarı).</summary>
    internal static string N(double v, int dec) => Fmt.N(v, dec);

    /// <summary>Tam çerçeveli tablo: başlık satırları (hazır metin), Ã, gövde, À. on: satır öneki (STA4 bazı tablolarda " ").</summary>
    internal static void Tablo(List<string> o, int[] en, IEnumerable<string> baslik, IEnumerable<string> govde, string on = "")
    {
        o.Add(on + Ust(en));
        foreach (var b in baslik) o.Add(on + b);
        o.Add(on + Ayrac(en));
        foreach (var g in govde) o.Add(on + g);
        o.Add(on + Alt(en));
    }

    // ---------- veri erişimi ----------
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Proj, StaVeri> VeriOnbellek = new();
    internal static StaVeri Veri(Proj p)
    {
        if (VeriOnbellek.TryGetValue(p, out var v)) return v;
        var st4 = p.FilePath(".ST4"); if (st4 == null) return null;
        _ = GprKod.Enc;   // 1254 sağlayıcısı
        v = StaVeri.Ac(st4);
        VeriOnbellek.AddOrUpdate(p, v);
        return v;
    }
    internal static int Ii(string s) => (int)Math.Round(Proj.D(s));

    /// <summary>ST4 bölüm satırları (alanlara bölünmüş), bölüm adından sonraki "/" satırına kadar.</summary>
    internal static List<string[]> Bolum(Proj p, string ad)
    {
        var r = new List<string[]>(); int k = p.Sec(ad); if (k < 0) return r;
        for (int i = k + 1; i < p.St4.Length && !p.St4[i].StartsWith("/"); i++) r.Add(p.St4[i].Split(','));
        return r;
    }

    /// <summary>Aks koordinatları: ST4 5. satır 1./2. alan X/Y aks sayısı; /Axis data/ 2. alan; önce X sonra Y. Kod 1000+n → X, 2000+n → Y.</summary>
    internal sealed class Akslar
    {
        public double[] X, Y;
        public double this[int kod] => kod >= 2000 ? (kod - 2000 < Y.Length ? Y[kod - 2000] : 0) : (kod - 1000 >= 0 && kod - 1000 < X.Length ? X[kod - 1000] : 0);
        public static Akslar Oku(Proj p)
        {
            var b = p.St4[4].Split(','); int nx = Ii(b[0]), ny = Ii(b[1]);
            var a = Bolum(p, "/Axis data/");
            var r = new Akslar { X = new double[nx + 1], Y = new double[ny + 1] };
            for (int i = 0; i < nx && i < a.Count; i++) r.X[i + 1] = Proj.D(a[i].ElementAtOrDefault(1) ?? "0");
            for (int i = 0; i < ny && nx + i < a.Count; i++) r.Y[i + 1] = Proj.D(a[nx + i].ElementAtOrDefault(1) ?? "0");
            return r;
        }
    }
    internal static string AksAd(int kod, bool kucuk) => kod >= 2000 ? (kod - 2000) + (kucuk ? "y" : "Y") : (kod - 1000) + (kucuk ? "x" : "X");

    /// <summary>ST4 /Beams Data/ 1. satırı (alanlar) — kiriş sırasına göre.</summary>
    internal static string[] KirisSatiri(Proj p, St4Kiris k) => p.St4[p.Sec("/Beams Data/") + 1 + 3 * k.Sira].Split(',');
    /// <summary>ST4 kiriş 15. alan (indis 14) = 1 → panel (bodrum perde paneli).</summary>
    internal static bool Panel(Proj p, St4Kiris k) => (KirisSatiri(p, k).ElementAtOrDefault(14) ?? "0").Trim() == "1";
    /// <summary>Rapor adı: panel ise "P" öneki (PB-066).</summary>
    internal static string KirisAdi(Proj p, St4Kiris k) => Panel(p, k) ? "P" + k.Ad.Substring(1) : k.Ad;

    /// <summary>
    /// STA4 kiriş listesi sırası: sürekli kiriş zincirleri (aynı aks/kat; öncülün J düğümü = ardılın I düğümü, BEA +32/+34).
    /// ST4 sırasında gezilir; ardıl seçiminde aynı B/H/G önce; öncülü olan (panel olmayan, öncülü panel olmayan) kiriş bekletilir;
    /// yazılan her kirişin ardından ardıl zinciri yazılır. Panel bekletilmez, yalnız ardılını çeker.
    /// Doğrulama: N_ULK_02, K_BLG_03 (6 satır hariç), VKF_01 birebir; CP_03/FHM_GO_A'da istisnalar var.
    /// </summary>
    internal static List<St4Kiris> KirisSirasi(Proj p)
    {
        var v = Veri(p);
        var ks = v.Model.Kirisler.Where(k => v.KirisKaydi.ContainsKey(k)).ToList();
        var panel = ks.ToDictionary(k => k, k => Panel(p, k));
        var tip = ks.ToDictionary(k => k, k => { var f = KirisSatiri(p, k); return f[1] + "|" + f[2] + "|" + f[3]; });
        var I = ks.ToDictionary(k => k, k => (int)v.Bea[v.KirisKaydi[k]].S(32));
        var J = ks.ToDictionary(k => k, k => (int)v.Bea[v.KirisKaydi[k]].S(34));
        var oncul = new Dictionary<St4Kiris, St4Kiris>();
        var byJ = ks.GroupBy(k => (k.Aks, k.Kat, J[k])).ToDictionary(g => g.Key, g => g.First());
        foreach (var k in ks) if (byJ.TryGetValue((k.Aks, k.Kat, I[k]), out var o) && o != k) oncul[k] = o;
        var tekArd = new Dictionary<St4Kiris, St4Kiris>(); var alinan = new HashSet<St4Kiris>();
        var byI = ks.GroupBy(k => (k.Aks, k.Kat, I[k])).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var a in ks)
        {
            if (!byI.TryGetValue((a.Aks, a.Kat, J[a]), out var l0)) continue;
            var l = l0.Where(x => x != a && !alinan.Contains(x)).ToList();
            var b = l.FirstOrDefault(x => tip[x] == tip[a]) ?? l.FirstOrDefault();
            if (b != null) { tekArd[a] = b; alinan.Add(b); oncul[b] = a; }
        }
        var r = new List<St4Kiris>(); var yaz = new HashSet<St4Kiris>();
        foreach (var a in ks)
        {
            if (yaz.Contains(a) || (alinan.Contains(a) && !panel[a] && !panel[oncul[a]])) continue;
            for (var x = a; x != null && yaz.Add(x); x = tekArd.GetValueOrDefault(x)) r.Add(x);
        }
        foreach (var k in ks) if (yaz.Add(k)) r.Add(k);
        return r;
    }

    /// <summary>
    /// Analiz düğüm koordinatları. Kolon düğümü: COL +0 üst düğüm → (COL +18, +22). Kolon olmayan düğüm: 1) düğümü uç olarak
    /// paylaşan kirişin diğer ucu biliniyorsa kiriş doğrultusuna dik bileşen oradan (aks 1xxx: x, 2xxx: y); 2) yoksa kiriş çizgisi
    /// aks − f7·B/200 (ST4 kiriş 8. alan hizalama); 3) yoksa uç aksı.
    /// </summary>
    internal static Dictionary<int, (double x, double y)> Dugumler(Proj p)
    {
        var v = Veri(p); var ak = Akslar.Oku(p);
        var d = new Dictionary<int, (double x, double y)>();
        for (int r = 0; r < v.Col.Adet; r++) { var c = v.Col[r]; if (c.S(0) > 0) d[c.S(0)] = (c.F(18), c.F(22)); }
        var uc = new List<(int n, int diger, int aks, int aksUc, double kay)>();
        foreach (var k in v.Model.Kirisler)
        {
            if (!v.KirisKaydi.TryGetValue(k, out int r)) continue;
            var b = v.Bea[r];
            double kay = -Ii(KirisSatiri(p, k).ElementAtOrDefault(7) ?? "0") * k.B / 200.0;
            uc.Add((b.S(32), b.S(34), k.Aks, k.AksBas, kay)); uc.Add((b.S(34), b.S(32), k.Aks, k.AksSon, kay));
        }
        var bilinmeyen = uc.Where(u => !d.ContainsKey(u.n)).GroupBy(u => u.n).ToList();
        for (int tur = 0; tur < 6; tur++)
            foreach (var g in bilinmeyen.Where(g => !d.ContainsKey(g.Key)))
            {
                double? x = null, y = null;
                foreach (var u in g.Where(u => d.ContainsKey(u.diger)))
                    if (u.aks < 2000) x ??= d[u.diger].x; else y ??= d[u.diger].y;
                if (tur < 5 && (x == null || y == null) && g.All(u => !d.ContainsKey(u.diger))) continue;
                foreach (var u in g) { if (u.aks < 2000) x ??= ak[u.aks] + u.kay; else y ??= ak[u.aks] + u.kay; }
                foreach (var u in g) { if (u.aks < 2000) y ??= ak[u.aksUc]; else x ??= ak[u.aksUc]; }
                d[g.Key] = (x.Value, y.Value);
            }
        return d;
    }

    /// <summary>
    /// Kiriş Wk (mm; betonarme tablosundaki): ACI Gergely–Lutz w = 0.011·1.2·(0.6·420)·∛(dc·2dc·b/n)·10⁻³,
    /// n, ø KMX +112/+114 (+ pilye +116/+118); As_gerekli(+76)/As_konan ≤ 1/1.2 ise ÷1.2; dc = 57 + 0.24ø (+ ikinci sıra).
    /// (Veri/TasarimHesaplari.KirisWk ile aynı; bağımlılık zinciri olmasın diye burada.)
    /// </summary>
    internal static double KirisWk(IkiliKayit kmx, St4Kiris k)
    {
        int n = kmx.S(112), f = kmx.S(114);
        int np = kmx.S(116) > 0 && kmx.S(118) > 0 ? kmx.S(116) : 0;
        if (n <= 0 || f <= 0) return 0;
        double konan = (n + np) * Math.PI * f * f / 400.0, b = k.B * 10.0;
        int sira = Math.Max(2, (int)Math.Floor((b - 2 * 25 + 12) / (f + 12.0)));
        double dc = 57 + 0.24 * f; int nt = n + np;
        if (nt > sira) dc += (nt - sira) / (double)nt * (f + 25);
        double kip = konan > 0 && kmx.F(76) / konan > 1 / 1.2 ? 1 : 1 / 1.2;
        return 0.011 * 1.2 * 0.6 * 420 / 1000.0 * kip * Math.Pow(2 * dc * dc * (b / nt), 1.0 / 3);
    }
}

// ================= Burkulma (TS500-7.6) =================
public static partial class HesapTablolari
{
    /// <summary>
    /// "KAT BURKULMA HESABI (t)     (TS500-7.6)" + "KOLON BURKULMA HESAP SONUÇLARI" (N_ULK_02 s.85–91).
    /// Kaynak: ST4 (/Columns Data/ Bx, By, üst/alt kot; /Axis data/; /Materials/ 1. satır fck, Ec), COL (+0/+2 üst/alt düğüm,
    /// +26 Nz, +114/+202 Mx üst/alt, +290/+378 My üst/alt; dizi 0 = G, 1 = Q), BEA (+32/+34 düğüm, +20/+24 rijit uç), KMX (+0/+4 tabla
    /// kenar genişlikleri, +2/+6 tabla kalınlığı).
    /// Kolon blokları: Hk = (float)üst − (float)alt kot; I = b·h³/12; (I/L)c = I/Hk; (I/L)b = Σ 0.35·I_T/ln (T kesit); ψ = 0.7·Σ(I/L)c/Σ(I/L)b ≤ 20,
    /// temelde ψa = 0; k nomogram denklemi (önlenmiş); Lkr = k·(Hk − hb); Nd = 1.4G + 1.6Q; Rm = 1.4G/Nd; Cm = 0.6 + 0.4·M1/M2 ≥ 0.4;
    /// EI = 0.4·Ec·I/(1 + Rm); Nkr = π²EI/Lkr²; β = Cm/(1 − 1.3Nd/Nkr); λ = Lkr/i.
    /// Kat tablosu: ΣNd (başlık "äNg") ve ΣNkr ile ΣNd ≤ 0.45ΣNkr denetimi. Σ(φi·Ndi/L), Vfi, é ve βs boş: kolon göreli kat ötelemesi
    /// sonuç dosyalarında bulunamadı. é bilinmediğinden tüm katlar "yanal deplasman önlenmiş" kabul edilir (STA4 é > 0.05 olan
    /// kat/yönde ötelenen çerçeve k'sı, Rm = 0, Cm = 1 ve ">35 … ß=ßx.ßs" basar; N_ULK_02'de kat 1 X yönü).
    /// Doğrulama N_ULK_02: önlenmiş kat/yön blokları (STA4 k'sı tam nomogram çözümünden ≤ 0.0007 küçük → k, Lkr, Nkr son hanede ±1–3).
    /// </summary>
    public static List<string> Burkulma(Proj p)
    {
        var v = Veri(p); if (v == null || !v.Col.Var || !v.Bea.Var) return null;
        var bl = BurkulmaH.Hesapla(p, v);
        if (bl.Count == 0) return null;
        var o = new List<string>();
        o.Add("  KAT BURKULMA HESABI (t)     (TS500-7.6)");
        o.Add("  é=1.5 ä(ôi Ndi/L) /Vfi   éË0.05 Kat yanal deplasmanı önlenmiş, é>0.05 Kat yanal deplasmanı önlenmemiş");
        o.Add("  ßs=1/ (1 - 1.3 * äNg / äNd )È1 Kat burkulması");
        o.Add("  äNd Ë 0.45 äNkr  ò  , äNd > 0.45 äNkr  ñ  (Kat kolonları büyütülecektir.)");
        int[] en = { 10, 11, 11, 11, 11, 13, 8, 15 };
        var kat = new List<string>();
        foreach (var g in bl.GroupBy(b => b.Kat).OrderBy(g => g.Key))
            foreach (bool yy in new[] { false, true })
            {
                var l = g.Where(b => b.Y == yy).ToList();
                double nd = l.Sum(b => b.Nd), nkr = l.Sum(b => b.Nkr);
                kat.Add(Satir((g.Key.ToString().PadLeft(5) + "  " + (yy ? "Y" : "X")).PadRight(10), new string(' ', 11), new string(' ', 11), new string(' ', 11),
                    Sag(nd.ToString("F1", Inv), 11), Sag(nkr.ToString("F1", Inv), 13), new string(' ', 8), (nd <= 0.45 * nkr ? "       ò       " : "       ñ       ")));
            }
        Tablo(o, en, new[] { Satir("   Kat    ", "ä(ôi Ndi/L)", "    Vfi    ", "     é     ", "    äNg    ", "     äNkr    ", "   ßs   ", "äNd Ë 0.45 äNkr") }, kat, "  ");

        o.Add(" KOLON BURKULMA HESAP SONUÇLARI");
        o.Add(Ust(52, 52));
        o.Add(Satir("         X Yönü".PadRight(52), "         Y Yönü".PadRight(52)));
        var kolonlar = bl.Select(b => b.Kolon).Distinct().ToList();
        foreach (var ad in kolonlar)
        {
            o.Add(Ayrac(52, 52));
            var x = BurkulmaH.Yari(bl.First(b => b.Kolon == ad && !b.Y));
            var y = BurkulmaH.Yari(bl.First(b => b.Kolon == ad && b.Y));
            for (int i = 0; i < x.Length; i++) o.Add(H + x[i] + H + y[i] + H);
        }
        o.Add(Alt(52, 52));
        return o;
    }

    static class BurkulmaH
    {
        internal sealed class Sonuc
        {
            public string Kolon, UstAd, AltAd; public bool Y; public int Kat, KatA = -1;
            public double Hk, IbU, IbA, IcU, Ic, IcA, I, Ac, PsiU, PsiA, K, Lkr, Cm, Rm, EI, Nd, Nkr, Beta, Lam;
        }

        /// <summary>Sayı: (tek ise float32 G7 üzerinden) yarım yukarı dec haneye, sondaki sıfırlar atılır.</summary>
        static string T(double v, int dec, bool tek = true)
        {
            decimal d = tek ? decimal.Parse(((float)v).ToString("G7", Inv), NumberStyles.Float, Inv) : (decimal)v;
            d = Math.Round(d, dec, MidpointRounding.AwayFromZero);
            if (d == 0) d = 0;
            return d.ToString("0." + new string('#', dec), Inv);
        }
        static string Iki(string a, string b) => (a.Length >= 34 ? a : a.PadRight(34)) + b;
        static string P(string s) => s.PadRight(52);

        /// <summary>Yarım blok (11 satır, 52 karakter; 3. ve 5. satırda 10. sütunda iç ¡).</summary>
        internal static string[] Yari(Sonuc r)
        {
            string y = r.Y ? "y" : "x";
            return new[]
            {
                P(Iki(r.UstAd != null ? $"        {r.UstAd} (I/L)c={T(r.IcU, 8)}" : "", $"Hk= {r.Hk.ToString("F3", Inv)}")),
                P(Iki($"Kat :{r.Kat.ToString().PadRight(5)}{(r.UstAd != null ? 'Å' : 'Â')}  (I/L)b={T(r.IbU, 8)}", $"I{y}={T(r.I, 8)}")),
                ($"àu={T(r.PsiU, 3)}".PadRight(10) + H + new string(' ', 23) + $"Ac={T(r.Ac, 3)}").PadRight(52),
                P(Iki($"        {r.Kolon} (I/L)c={T(r.Ic, 8)}", $"k(àu,àa)={T(r.K, 3)}")),
                ($"àa={T(r.PsiA, 3)}".PadRight(10) + H + new string(' ', 23) + $"Lkr={T(r.Lkr, 3)}").PadRight(52),
                P(Iki($"Kat :{(r.AltAd != null ? r.KatA.ToString() : "").PadRight(5)}{(r.AltAd != null ? 'Å' : 'Á')}  (I/L)b={T(r.IbA, 8)}", $"Cm{y}={T(r.Cm, 3)}")),
                P(Iki(r.AltAd != null ? $"        {r.AltAd} (I/L)c={T(r.IcA, 8)}" : "", $"Rm=Ng/Nd={T(r.Rm, 3)}")),
                P("yanal deplasman onlenmis"),
                P($"EI=0.4 EcIc /(1 + Rm)={T(r.EI, 3, false)}  Nd={T(r.Nd, 3)}"),
                P($"Nkr=ã² EI/Lkr²={T(r.Nkr, 3)}  ß{y}=Cm/(1-1.3 Nd/Nkr)={T(r.Beta, 3)}"),
                P($"è{y}=Lkr/i{y}={T(r.Lam, 1)}{(r.Lam < 100 ? "<100 ò" : ">100 ñ")} (Lk/i)<35/û(Nd/fck Ac)>>ß=ß{y}"),
            };
        }

        static double IT(double bw, double h, double bf, double hf)
        {
            double a1 = bf * hf, a2 = bw * (h - hf), yc = (a1 * hf / 2 + a2 * (hf + (h - hf) / 2)) / (a1 + a2);
            return bf * hf * hf * hf / 12 + a1 * Math.Pow(yc - hf / 2, 2) + bw * Math.Pow(h - hf, 3) / 12 + a2 * Math.Pow(hf + (h - hf) / 2 - yc, 2);
        }

        /// <summary>
        /// Kirişin düğümdeki burkulma rijitliği 0.35·I_T/ln. ln = |aks(AksSon) − aks(AksBas)| − BEA(+20) − BEA(+24) (bölünmüş kirişte zincirin ilk
        /// ve son parçası). T kesit: hf = max(KMX +2, +6), kenar genişliği = 0.06·ln eğer round(0.06·ln·100) = KMX değeri, değilse KMX/100 (cm).
        /// </summary>
        static double KirisIL(StaVeri v, Akslar ax, St4Kiris b, out double h)
        {
            var m = v.Kmx[v.KirisKaydi[b]];
            h = b.H / 100.0;
            var par = v.Model.Kirisler.Where(z => z.Ad == b.Ad && v.KirisKaydi.ContainsKey(z) && z.Aks == b.Aks).ToList();
            var bas = par.FirstOrDefault(z => !par.Any(w => w.AksSon == z.AksBas)) ?? b;
            var son = par.FirstOrDefault(z => !par.Any(w => w.AksBas == z.AksSon)) ?? b;
            double lc = Math.Abs(ax[son.AksSon] - ax[bas.AksBas]) - v.Bea[v.KirisKaydi[bas]].F(20) - v.Bea[v.KirisKaydi[son]].F(24);
            double bw = b.B / 100.0, hf = Math.Max(m.S(2), m.S(6)) / 100.0;
            double Kenar(int s) { if (s <= 0) return 0; double t = 0.06 * lc; return t > 0 && Math.Round(t * 100) == s ? t : s / 100.0; }
            double f = Kenar(m.S(0)) + Kenar(m.S(4));
            double I = hf > 0 && f > 0 ? IT(bw, h, bw + f, hf) : bw * h * h * h / 12;
            return lc > 0 ? 0.35 * I / lc : 0;
        }

        static (double il, double h) Dugum(StaVeri v, Akslar ax, HashSet<St4Kiris> panel, int n, bool yy)
        {
            double s = 0, h = 0;
            foreach (var b in v.Model.Kirisler.Where(b => v.KirisKaydi.ContainsKey(b) && b.YYonu == yy && !panel.Contains(b)))   // paneller (bodrum perdesi) kiriş sayılmaz
            {
                var e = v.Bea[v.KirisKaydi[b]];
                if (e.S(32) != n && e.S(34) != n) continue;
                s += KirisIL(v, ax, b, out double hb); h = Math.Max(h, hb);
            }
            return (s, h);
        }

        /// <summary>Yanal ötelenmesi önlenmiş çerçeve nomogram denklemi, k ikiye bölmeyle.</summary>
        static double Kk(double ga, double gb)
        {
            double F(double k)
            {
                double x = Math.PI / k;
                return ga * gb / 4 * x * x + (ga + gb) / 2 * (1 - x / Math.Tan(x)) + 2 * Math.Tan(x / 2) / x - 1;
            }
            double lo = 0.5000001, hi = 1, flo = F(lo);
            for (int i = 0; i < 200; i++)
            {
                double mid = (lo + hi) / 2, fm = F(mid);
                if (Math.Sign(fm) == Math.Sign(flo)) { lo = mid; flo = fm; } else hi = mid;
            }
            return (lo + hi) / 2;
        }

        internal static List<Sonuc> Hesapla(Proj p, StaVeri v)
        {
            var res = new List<Sonuc>();
            var ax = Akslar.Oku(p);
            int mk = p.Sec("/Materials/");
            double ec = mk >= 0 ? Proj.D(p.St4[mk + 1].Split(',').ElementAtOrDefault(2) ?? "0") * 10 : 3025000;   // kg/cm² → t/m²
            if (ec <= 0) ec = 3025000;
            double KatKot(int i) => i >= 0 && i < v.Model.Katlar.Count ? v.Model.Katlar[i].Kot : 0;
            var panel = new HashSet<St4Kiris>(v.Model.Kirisler.Where(b => Panel(p, b)));
            var kols = v.Model.Kolonlar;
            int Ust(St4Kolon z) => v.Col[v.KolonKaydi(z)].S(0);
            int Alt(St4Kolon z) => v.Col[v.KolonKaydi(z)].S(2);
            foreach (var g in kols.GroupBy(k => k.Yerel).OrderBy(g => g.Key))
                foreach (var k in g.OrderByDescending(z => z.Kat))
                {
                    var c = v.Col[v.KolonKaydi(k)];
                    int nu = c.S(0), na = c.S(2);
                    var ust = kols.FirstOrDefault(z => z != k && Alt(z) == nu);
                    var alt = kols.FirstOrDefault(z => z != k && Ust(z) == na);
                    var n = c.Dizi(26);
                    double nd = 1.4 * n[0] + 1.6 * n[1], ng = 1.4 * n[0];
                    foreach (bool yy in new[] { false, true })
                    {
                        float Ix(St4Kolon z) { float bb = (yy ? z.Bx : z.By) / 100f, dd = (yy ? z.By : z.Bx) / 100f; return bb * dd * dd * dd / 12f; }
                        double Ix0(St4Kolon z) => yy ? z.Bx / 100.0 * Math.Pow(z.By / 100.0, 3) / 12 : z.By / 100.0 * Math.Pow(z.Bx / 100.0, 3) / 12;
                        float Hk(St4Kolon z) => z.KotVar ? (float)z.UstKot - (float)z.AltKot : (float)KatKot(z.Kat) - (float)KatKot(z.Kat - 1);   // kot yoksa kat kotları
                        var r = new Sonuc { Kolon = k.Ad, Y = yy, Nd = nd, Kat = k.Kat, I = Ix(k), Ac = k.Bx * k.By / 10000.0 };
                        r.Hk = Hk(k);
                        var tu = Dugum(v, ax, panel, nu, yy); var ta = Dugum(v, ax, panel, na, yy);
                        double hb = tu.h;
                        r.Ic = (float)r.I / (float)r.Hk; r.IbU = tu.il; r.IbA = alt != null ? ta.il : 0;
                        double su = r.Ic, sa = r.Ic;
                        if (ust != null) { r.UstAd = ust.Ad; r.IcU = Ix(ust) / Hk(ust); su += r.IcU; }
                        if (alt != null) { r.AltAd = alt.Ad; r.KatA = alt.Kat; r.IcA = Ix(alt) / Hk(alt); sa += r.IcA; }
                        r.PsiU = r.IbU > 0 ? Math.Min(20, 0.7 * su / r.IbU) : 20;
                        r.PsiA = alt == null ? 0 : r.IbA > 0 ? Math.Min(20, 0.7 * sa / r.IbA) : 20;
                        r.K = Kk(r.PsiU, r.PsiA);
                        r.Rm = nd != 0 ? ng / nd : 0;
                        // Cm: Md = 1.4G + 1.6Q; X yönü Mx (+114 üst, +202 alt), Y yönü My (+290/+378); m(0) = Malt, m(H) = −Müst,
                        // üst uç kiriş altı yüzünde (z = Hk − hb); M2 mutlak büyük olan
                        int ou = yy ? 290 : 114, oa = yy ? 378 : 202;
                        double mu = 1.4 * c.F(ou) + 1.6 * c.F(ou + 4), ma = 1.4 * c.F(oa) + 1.6 * c.F(oa + 4);
                        double hc = r.Hk - hb, mt0 = ma - (ma + mu) * hc / r.Hk;
                        double m2 = Math.Abs(mt0) >= Math.Abs(ma) ? mt0 : ma, m1 = Math.Abs(mt0) >= Math.Abs(ma) ? ma : mt0;
                        r.Cm = m2 != 0 ? Math.Max(0.4, 0.6 + 0.4 * m1 / m2) : 1;
                        r.Lkr = r.K * (r.Hk - hb > 0.1 ? r.Hk - hb : r.Hk);
                        r.EI = 0.4 * ec * Ix0(k) / (1 + r.Rm);
                        r.Nkr = Math.PI * Math.PI * r.EI / (r.Lkr * r.Lkr);
                        r.Beta = r.Cm / (1 - 1.3 * nd / r.Nkr);
                        r.Lam = r.Lkr / Math.Sqrt(r.I / r.Ac);
                        res.Add(r);
                    }
                }
            return res;
        }
    }
}

// ================= s.48 KİRİŞ/PANEL DUVAR ve DUVAR YÜK BİLGİLERİ; s.49–50 DÖŞEME; s.55 KİRİŞ VE PANEL =================
public static partial class HesapTablolari
{
    static readonly string[] DuvarTipAd = { "Tuğla", "Gazbeton", "Cam", "Panel", "Fabrika panel", "Taş" };

    /// <summary>
    /// Proje .USE duvar kütüphanesi: "ad" satırını izleyen 10 alanlı satır = tip, kalınlık (cm), g (t/m²), H, pencere g, pencere H, …
    /// </summary>
    static List<(int tip, double kal, double g)> DuvarKutuphanesi(Proj p)
    {
        var r = new List<(int, double, double)>();
        var l = p.Use; if (l == null) return r;
        for (int i = 1; i < l.Length; i++)
        {
            var f = l[i].Split(',');
            if (f.Length == 10 && f.All(z => double.TryParse(z, NumberStyles.Float, Inv, out _)) && !l[i - 1].Contains(','))
                r.Add((Ii(f[0]), Proj.D(f[1]), Proj.D(f[2])));
        }
        return r;
    }

    /// <summary>
    /// s.48 "KİRİŞ/PANEL DUVAR YÜK BİLGİLERİ" + "DUVAR YÜK BİLGİLERİ".
    /// Kiriş/panel: ST4 /Beams Data/ 17. alan B &gt; 0 ve 18. alan kod &gt; 0 olan kirişler (zincir sırası); H = kod % 10000,
    /// tip = kod/10000 − 1; g = .USE kalemi (kalınlık = B, tip); G = g·H/100; iki sütun yan yana.
    /// Duvar: ST4 /Walls/ [0] kat, [2] G×100, [3] B, [4] kod, [5] aks, [6]/[7] sol/sağ aks; L = |aks farkı| (float32);
    /// yüklenen eleman: aynı kattaki döşemelerden (no/100 = kat; kat 0'da önce radye plakları) duvarın 1/4 veya 3/4 noktasını
    /// (kenar dahil) içerenler, artan no, virgülle. STA4 70 satırda keser; burada tüm duvarlar basılır.
    /// Doğrulama: N_ULK_02 174/174 + 242/242; K_BLG_03 1878/1878; VKF_01 1710/1710; FHM_GO_A duvar 363/363.
    /// Gerekenler: .ST4, .USE, .BEA (kiriş sırası).
    /// </summary>
    public static List<string> DuvarYuk(Proj p)
    {
        var v = Veri(p); if (v == null) return null;
        var o = new List<string>();
        var lib = DuvarKutuphanesi(p);
        var kayit = new List<string[]>();
        foreach (var k in KirisSirasi(p))
        {
            var f = KirisSatiri(p, k);
            double B = Proj.D(f.ElementAtOrDefault(16) ?? "0"); int kod = Ii(f.ElementAtOrDefault(17) ?? "0");
            if (B <= 0 || kod <= 0) continue;
            int Hc = kod % 10000, tip = Math.Max(kod / 10000 - 1, 0);
            var e = lib.FirstOrDefault(x => x.kal == B && x.tip == tip);
            if (e == default) e = lib.FirstOrDefault(x => x.kal == B);
            kayit.Add(new[] { Sol(KirisAdi(p, k), 8), Sag(Hc.ToString(), 5), Sag(B.ToString(Inv), 5),
                Sol(DuvarTipAd.ElementAtOrDefault(tip) ?? tip.ToString(), 14), Sag(N(e.g, 3), 5), Sag(N(e.g * Hc / 100.0, 3), 7) });
        }
        if (kayit.Count > 0)
        {
            int[] en = { 8, 5, 5, 14, 5, 7 };
            string Iki(Func<int[], string> c) => c(en) + "     " + c(en);
            string bos = Satir(new string(' ', 8), new string(' ', 5), new string(' ', 5), new string(' ', 14), new string(' ', 5), new string(' ', 7));
            o.Add(" KİRİŞ/PANEL DUVAR YÜK BİLGİLERİ");
            o.Add(Iki(Ust));
            string b1 = Satir("Kiriş   ", "  H  ", "  B  ", "  Duvar tipi  ", "  g  ", "   G   ");
            string b2 = Satir("  no    ", "  cm ", "  cm ", "              ", " t/m²", " (t/m) ");
            o.Add(b1 + "     " + b1); o.Add(b2 + "     " + b2);
            o.Add(Iki(Ayrac));
            for (int i = 0; i < kayit.Count; i += 2)
                o.Add(Satir(kayit[i]) + "     " + (i + 1 < kayit.Count ? Satir(kayit[i + 1]) : bos));
            o.Add(Iki(Alt));
        }

        // ---- DUVAR YÜK BİLGİLERİ ----
        var ak = Akslar.Oku(p);
        var flo = Bolum(p, "/Floors Data/");
        var plk = Bolum(p, "/Slab foundations/");
        var gov = new List<string>(); int no = 0;
        foreach (var f in Bolum(p, "/Walls/"))
        {
            if (f.Length < 8 || Ii(f[4]) == 0) break;
            no++;
            int kat = Ii(f[0]), aks = Ii(f[5]), a1 = Ii(f[6]), a2 = Ii(f[7]), kod = Ii(f[4]);
            int Hc = kod % 10000, tip = Math.Max(kod / 10000 - 1, 0);
            float L = Math.Abs((float)ak[a2] - (float)ak[a1]);
            gov.Add(Satir(Sag(no.ToString(), 5), Sag(kat == 0 ? "Zemin" : kat.ToString(), 5), Sag(AksAd(aks, true), 5), Sag(AksAd(a1, true), 5),
                Sag(AksAd(a2, true), 5), Sag(Hc.ToString(), 5), Sag(f[3].Trim(), 5), Sol(DuvarTipAd.ElementAtOrDefault(tip) ?? tip.ToString(), 14),
                Sag(N(Proj.D(f[2]) / 100, 3), 7), Sag(N(L, 2), 6) + " ", Sol(YukluEleman(ak, flo, plk, kat, aks, a1, a2), 21)));
        }
        if (gov.Count > 0)
        {
            o.Add(" DUVAR YÜK BİLGİLERİ");
            Tablo(o, new[] { 5, 5, 5, 5, 5, 5, 5, 14, 7, 7, 21 },
                new[] { Satir("Duvar", " kat ", " aks ", " sol ", " sağ ", "  H  ", "  B  ", "  Duvar tipi  ", "   G   ", "   L   ", "  Yüklenen eleman    "),
                        Satir(" no  ", "     ", "     ", " aks ", " aks ", "  cm ", "  cm ", "              ", " (t/m) ", "   m   ", "                     ") }, gov);
        }
        return o.Count > 0 ? o : null;
    }

    static string YukluEleman(Akslar ak, List<string[]> flo, List<string[]> plk, int kat, int aks, int a1, int a2)
    {
        const double e = 1e-4;
        double c = ak[aks], s1 = Math.Min(ak[a1], ak[a2]), s2 = Math.Max(ak[a1], ak[a2]);
        bool Cakisir(int x1, int x2, int y1, int y2)
        {
            double ax = Math.Min(ak[x1], ak[x2]), bx = Math.Max(ak[x1], ak[x2]);
            double ay = Math.Min(ak[y1], ak[y2]), by = Math.Max(ak[y1], ak[y2]);
            var (pp, q, r1, r2) = aks < 2000 ? (ax, bx, ay, by) : (ay, by, ax, bx);
            bool Nokta(double m) => m >= r1 - e && m <= r2 + e;
            return c >= pp - e && c <= q + e && (Nokta(s1 + (s2 - s1) / 4) || Nokta(s1 + 3 * (s2 - s1) / 4));
        }
        var l = new List<(int no, string ad)>();
        if (kat == 0)
            for (int i = 0; i + 2 < plk.Count; i += 3)
            {
                var f = plk[i + 1]; if (f.Length < 5 || Ii(f[1]) == 0) continue;
                if (Cakisir(Ii(f[1]), Ii(f[2]), Ii(f[3]), Ii(f[4]))) l.Add((i, plk[i][0].Trim()));
            }
        if (l.Count == 0)
            for (int i = 0; i + 1 < flo.Count; i += 2)
            {
                var f = flo[i]; int no = Ii(f[0]); if (no == 0) break;
                if (no / 100 != kat) continue;
                if (Cakisir(Ii(f[8]), Ii(f[9]), Ii(f[10]), Ii(f[11]))) l.Add((no, "D" + no));
            }
        return string.Join(",", l.OrderBy(z => z.no).Select(z => z.ad));
    }

    /// <summary>
    /// s.49 "DÖŞEME ŞERİT BİLGİLERİ": ST4 /Floors Continuous/ satır çiftleri (kod 100 → X, 200 → Y, 0 → son), metin aynen.
    /// STA4 her sayfa sonunda (84 satır) bir kaydı atlar; burada tüm kayıtlar basılır. Doğrulama: 5 projede atlanan kayıtlar dışında birebir.
    /// Gereken: .ST4.
    /// </summary>
    public static List<string> DosemeSerit(Proj p)
    {
        if (p.St4 == null) return null;
        var fc = Bolum(p, "/Floors Continuous/");
        var gov = new List<string>();
        for (int i = 0; i + 1 < fc.Count; i += 2)
        {
            int kod = Ii(fc[i][0]); if (kod == 0) break;
            string m = string.Join(",", fc[i + 1]).TrimEnd();
            gov.Add(Satir(kod == 100 ? " X  " : kod == 200 ? " Y  " : Sol(kod.ToString(), 4), Sol(m, 51)));
        }
        if (gov.Count == 0) return null;
        var o = new List<string> { "  DÖŞEME ŞERİT BİLGİLERİ" };
        Tablo(o, new[] { 4, 51 }, new[] { Satir("YÖN ", "       DÖŞEME  ŞERİTİ                             ") }, gov, " ");
        return o;
    }

    /// <summary>
    /// s.50 "DÖŞEME BİLGİLERİ": ST4 /Floors Data/ (no ≥ taban; no'ya göre artan) + FLO (200 B, ST4 sırası).
    /// Akslar alan [8..11]; yön [4]: 0 PLAK, 1 MERDIVEN-X, 2 MERDIVEN-Y; d [1]; g, q [2], [3];
    /// gx = g·k, gy = g·(1−k), qx = q·k, qy = q·(1−k), k = FLO +0 (5 projede birebir).
    /// dmin (yaklaşık, TS500 11.2): net açıklık nx = FLO+116 − FLO+88 − FLO+92, ny = FLO+120 − FLO+104 − FLO+108;
    /// αs = sürekli kenar sayısı/4 (FLO +76..+82 komşu ≠ 0); m ≤ 2: h = ls/(15+20/m)·(1−αs/4), yarım yukarı, en az 8;
    /// merdiven/kotlu (alan [5] ≠ 0) ve tek doğrultu (m &gt; 2) 8. Doğrulama: N_ULK_02 806/810, K_BLG_03 4085/4110 (farklar dmin).
    /// Gerekenler: .ST4, .FLO.
    /// </summary>
    public static List<string> DosemeBilgileri(Proj p)
    {
        var v = Veri(p); if (v == null || !v.Flo.Var) return null;
        var flo = Bolum(p, "/Floors Data/");
        int taban = v.Model.Taban(v.Model.Dosemeler.Select(x => x.No));
        var sat = new List<(int no, string s)>();
        foreach (var d in v.Model.Dosemeler)
        {
            if (d.No < taban || 2 * d.Sira >= flo.Count || d.Sira >= v.Flo.Adet) continue;
            var f = flo[2 * d.Sira]; var r = v.Flo[d.Sira];
            int yon = Ii(f[4]);
            string yonAd = yon switch { 0 => "PLAK", 1 => "MERDIVEN-X", 2 => "MERDIVEN-Y", _ => yon.ToString() };
            double k = r.F(0), g = Proj.D(f[2]), q = Proj.D(f[3]);
            string A(int i) => " " + Sol(AksAd(Ii(f[i]), false), 4);
            sat.Add((d.No, Satir(Sol(d.Ad, 6), A(8), A(9), A(10), A(11), yonAd.Length >= 10 ? yonAd : " " + Sol(yonAd, 9),
                $"{Ii(f[1]),4}È{DosemeDmin(r, f),3}", "    ", "    ", Sag(N(g, 3), 7), Sag(N(q, 3), 7),
                Sag(N(g * k, 3), 7), Sag(N(g * (1 - k), 3), 7), Sag(N(q * k, 3), 7), Sag(N(q * (1 - k), 3), 7))));
        }
        if (sat.Count == 0) return null;
        var o = new List<string> { "  DÖŞEME BİLGİLERİ" };
        Tablo(o, new[] { 6, 5, 5, 5, 5, 10, 8, 4, 4, 7, 7, 7, 7, 7, 7 },
            new[] { Satir("Döşeme", " Sol ", " Sag ", " Üst ", " Alt ", "   yön    ", " d, dmin", "  bo", "  bt", "   g   ", "   q   ", "   gx  ", "   gy  ", "   qx  ", "   qy  "),
                    Satir(" no   ", " Aks ", " Aks ", " Aks ", " Aks ", "          ", "   cm   ", "  cm", "  cm", "  t/m² ", "  t/m² ", "  t/m² ", "  t/m² ", "  t/m² ", "  t/m² ") },
            sat.OrderBy(x => x.no).Select(x => x.s));
        o.Add("Dmin: TS500 minimum yükseklik");
        return o;
    }

    static int DosemeDmin(IkiliKayit r, string[] f)
    {
        if (Ii(f[4]) != 0 || Ii(f.ElementAtOrDefault(5) ?? "0") != 0) return 8;
        double nx = r.F(116) - r.F(88) - r.F(92), ny = r.F(120) - r.F(104) - r.F(108);
        double ls = Math.Min(nx, ny); if (ls <= 0) return 8;
        double m = Math.Max(nx, ny) / ls;
        double a = Enumerable.Range(0, 4).Count(i => r.S(76 + 2 * i) != 0) / 4.0;
        double h = m <= 2 ? ls / (15 + 20 / m) * (1 - a / 4) * 100 : 0;
        return Math.Max(8, (int)Math.Round(h, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// s.55 "KİRİŞ VE PANEL BİLGİLERİ": ST4 /Beams Data/ + BEA + KMX + COL. Ad (panel → "P"), aks [4], sol/sağ aks [5]/[6], D [2], B [1],
    /// G = [3]/100; I/J = BEA +32/+34; L = I/J düğüm koordinatlarının kiriş doğrultusundaki farkı (Dugumler; aynı adlı bölünmüş kirişin
    /// ara düğümünde boyuna koordinat bölünme aksından), float32; Rh 0; tabla = (KMX +0 + KMX +4)/max(KMX +2, +6); "0/0", "E1".
    /// Sıra: KirisSirasi. Doğrulama: N_ULK_02 924/924, K_BLG_03 6437/6468 (içerik 6468/6468), VKF_01 4347/4550 (L).
    /// Gerekenler: .ST4, .BEA, .KMX, .COL.
    /// </summary>
    public static List<string> KirisBilgileri(Proj p)
    {
        var v = Veri(p); if (v == null || !v.Bea.Var || !v.Kmx.Var) return null;
        var ak = Akslar.Oku(p); var dug = Dugumler(p);
        var kolonD = new HashSet<int>(Enumerable.Range(0, v.Col.Adet).Select(i => (int)v.Col[i].S(0)));
        var gov = new List<string>();
        foreach (var k in KirisSirasi(p))
        {
            var f = KirisSatiri(p, k); int rec = v.KirisKaydi[k];
            var b = v.Bea[rec]; var m = v.Kmx[rec];
            int ni = b.S(32), nj = b.S(34), aks = Ii(f[4]), a1 = Ii(f[5]), a2 = Ii(f[6]);
            (double x, double y) P(int n, int a) => dug.TryGetValue(n, out var q) ? q : (aks >= 2000 ? (ak[a], ak[aks]) : (ak[aks], ak[a]));
            bool Bolunme(int n) => !kolonD.Contains(n) && v.Model.Kirisler.Any(x => x != k && x.No == k.No && v.KirisKaydi.TryGetValue(x, out int rx) && (v.Bea[rx].S(32) == n || v.Bea[rx].S(34) == n));
            double Boy((double x, double y) q, int n, int a) => Bolunme(n) ? ak[a] : (aks >= 2000 ? q.x : q.y);
            float L = Math.Abs((float)Boy(P(nj, a2), nj, a2) - (float)Boy(P(ni, a1), ni, a1));
            gov.Add(Satir(Sol(KirisAdi(p, k), 6), Sag(AksAd(aks, false), 5), Sag(AksAd(a1, false), 5), Sag(AksAd(a2, false), 5),
                Sag(f[2].Trim(), 5), Sag(f[1].Trim(), 5), Sag(N(Proj.D(f[3]) / 100, 3), 6), " " + Sol($"{ni}-{nj}", 9),
                Sag(N(L, 2), 6) + " ", Sag(N(0, 2), 5) + " ", Sol($"{m.S(0) + m.S(4)}/{Math.Max(m.S(2), m.S(6))}", 9), Sol("0/0", 9), Sol("0/0", 9), " E1 "));
        }
        if (gov.Count == 0) return null;
        var o = new List<string> { " KİRİŞ VE PANEL BİLGİLERİ" };
        Tablo(o, new[] { 6, 5, 5, 5, 5, 5, 6, 10, 7, 6, 9, 9, 9, 4 },
            new[] { Satir("Kiriş ", " aks ", " sol ", " sağ ", "  D  ", "  B  ", "   G  ", "   I/J    ", "   L   ", "  Rh  ", " Tabla   ", "   sol   ", "   sağ   ", "Mal "),
                    Satir(" no   ", "     ", " aks ", " aks ", "  cm ", "  cm ", " (t/m)", "  Nokta   ", "   m   ", "   m  ", " b/d(cm) ", "Hg/Lg(cm)", "Hg/Lg(cm)", "zeme") }, gov);
        return o;
    }
}

// ================= Kiriş kesme/burulma ve askı (GPR s.101–102) =================
public static partial class HesapTablolari
{
    /// <summary>
    /// "KİRİŞLERİN KESME ve BURULMA KONTROLU (TS500-8.2)" ve "KİRİŞ ASKI DONATISI KESME KONTROLU (TS500-8.1.2)".
    /// Kaynak: BEA (900 B; +214/+302 sol/sağ burulma T, +390/+478 sol/sağ V — 22 float, 0 G, 1..6 Q, 8..11 deprem;
    /// +16/+20, +24/+28 uç mesnet bileşenleri; +684/+688 → +700/+704 yüz noktaları; +32/+34 düğüm), KMX (+146 ø, +148/+150 s,
    /// +144 kol, +0..+6 tabla), COL +0, ST4 /Materials/ fck.
    /// Sıra: ST4 sırası, aynı adlı ardışık parçalar tek kiriş (sol uç ilk, sağ uç son parçadan).
    /// Doğrulama N_ULK_02 ve K_BLG_03 için bkz. scratchpad ara_hesap\uret_kiris; Tcr 4. ondalıkta ±1–7, Tr bundan; askı Vd yaklaşık
    /// (yalnız G: |V_G,uç| − q_G·x0).
    /// </summary>
    public static List<string> KirisKesme(Proj p)
    {
        var v = Veri(p);
        if (v == null || !v.Bea.Var || !v.Kmx.Var) return null;
        var o = new List<string>();
        double fck = Proj.D(Bolum(p, "/Materials/").FirstOrDefault()?[0] ?? "0");
        if (fck <= 0) return null;
        double fctd = KirisKesmeH.Fctd(fck);
        string fs = N(fctd, 2);

        // ---- kesme ve burulma ----
        o.Add(" KİRİŞLERİN KESME ve BURULMA KONTROLU  (tm)     (TS500-8.2)");
        o.Add($"  Vcr=0.65·fctd·bw·d,  Vc=0.80·Vcr,  Vw=Asw/s·fywd·d,  Vr=Vc+Vw> Vd,  fctd={fs}(kg/cm²)");
        o.Add("  Tcr = 1.35·fctd·Sx, Tr=(Vd/Vcr)²+(Td/Tcr)² Ë 1 (Eğik çatlama sınırı),  n·Asw/s È 0.3·fctd/fywd·bw");
        o.Add("  Aov/s =(Vd-Vc)/(2·d·fywd),  Aot/s =Td/(2·Ae·fywd),  Ao/s=Aov/s+Aot/s Ë Asw/s,  Asl=Aot/s Ue fywd/fyd");
        o.Add("  Vd:V(min{a+D,a+Ln/3})");
        int[] en = { 7, 7, 7, 7, 7, 7, 7, 7, 20, 8, 5 };
        int[] en2 = { 7, 7, 7, 7, 7, 7, 7, 7, 6, 6, 6, 8, 5 };
        o.Add(Ust(en));
        o.Add(Satir("Kiriş  ", "   Vcr ", "   Vw  ", "   Vr  ", "   Vd  ", "   Td  ", "  Tcr  ", "   Tr  ", "Aov/s + Aot/s = Ao/s", " Asw/s  ", " ò,ñ "));
        o.Add("Ã" + string.Join("Å", en.Take(8).Select(w => new string('Ä', w))) + "Å" + new string('Ä', 6) + "Â" + new string('Ä', 6) + "Â" + new string('Ä', 6) + "Å" + new string('Ä', 8) + "Å" + new string('Ä', 5) + "´");
        foreach (var (k, rr) in KirisKesmeH.Gruplar(v).Where(g => !Panel(p, g.k)))   // paneller basılmaz (K_BLG_03)
        {
            var x = KirisKesmeH.Hesapla(v, k, rr, fctd);
            o.Add(Satir(Sol(KirisAdi(p, k), 7), Sag(N(x.Vcr, 2), 7), Sag(N(x.Vw, 2), 7), Sag(N(x.Vr, 2), 7), Sag(N(x.Vd, 2), 7),
                Sag(N(x.Td, 4), 7), Sag(N(x.Tcr, 4), 7), Sag(N(x.Tr, 4), 7), Sag(N(x.Aov, 4), 6), Sag(N(x.Aot, 4), 6), Sag(N(x.Ao, 4), 6),
                Sag(N(x.Asw, 4), 7) + " ", (x.Ok ? "ò" : "ñ").PadLeft(4) + " "));
        }
        o.Add(Alt(en2));

        // ---- askı ----
        var aski = KirisKesmeH.Aski(p, v, fctd);
        o.Add("  KİRİŞ ASKI DONATISI KESME KONTROLU    (tm)     (TS500-8.1.2)");
        o.Add($"  fctd={fs}(kg/cm²)");
        o.Add("  Vcr=0.65·fctd·bw·d,  Vc=0.80·Vcr");
        o.Add("  Vw=Asw/s·fywd·d,  Vr=Vc+Vw> Vd");
        int[] ea = { 8, 18, 19, 6, 9, 7, 7, 7, 7, 5 };
        o.Add(Ust(ea));
        o.Add(Satir("  Kiriş ", "  Tali Kiriş      ", "    Askı Donatısı  ", " Asw  ", "  Bw/H   ", "   Vc  ", "   Vw  ", "   Vr  ", "   Vd  ", " ò,ñ "));
        o.Add(Ayrac(ea));
        foreach (var a in aski)
            o.Add(Satir(Sol(a.Ana, 8), Sol(a.Tali, 18), Sol(a.Don, 19), Sag(N(a.Asw, 2), 6), Sol(" " + a.BwH, 9), Sag(N(a.Vc, 2), 7),
                Sag(N(a.Vw, 2), 7), Sag(N(a.Vr, 2), 7), Sag(N(a.Vd, 2), 7), (a.Vd <= a.Vr ? "ò" : "ñ").PadLeft(4) + " "));
        o.Add(Alt(ea));
        return o;
    }

    static class KirisKesmeH
    {
        const double Pi = 3.142;              // STA4 çubuk alanı π
        const double FywV = 3652.35;          // Vw için fywd (kg/cm²)
        const double FywA = 4200 / 1.15;      // Aov/Aot için fywd

        /// <summary>fctd (kg/cm²) = 0.35·√(fck/10)/1.5·10·k; k STA4 kalibrasyonu (C25 0.99927, C30+ 0.99932).</summary>
        public static double Fctd(double fck) => 0.35 * Math.Sqrt(fck / 10) / 1.5 * 10 * (fck >= 300 ? 0.99932 : 0.99927);

        public sealed class Uc { public float[] TL, TR, VL, VR; public double Ln, l1, l2, r1, r2; public IkiliKayit Kmx; }

        /// <summary>Aynı adlı ardışık parçalar tek kiriş: sol uç ilk parçadan, sağ uç son parçadan; Ln yüz noktaları arası.</summary>
        public static Uc Birlesik(StaVeri v, List<int> kayitlar)
        {
            var a = v.Bea[kayitlar[0]]; var z = v.Bea[kayitlar[^1]];
            double x1 = a.F(684), y1 = a.F(688), x2 = z.F(700), y2 = z.F(704);
            return new Uc
            {
                TL = a.Dizi(214), VL = a.Dizi(390), TR = z.Dizi(302), VR = z.Dizi(478),
                Ln = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1)),
                l1 = a.F(16), l2 = a.F(20), r1 = z.F(24), r2 = z.F(28), Kmx = v.Kmx[kayitlar[0]]
            };
        }

        /// <summary>Rapor sırası: ST4 sırası, ardışık aynı ad birleşir.</summary>
        public static List<(St4Kiris k, List<int> r)> Gruplar(StaVeri v)
        {
            var g = new List<(St4Kiris, List<int>)>();
            foreach (var k in v.Model.Kirisler)
            {
                if (!v.KirisKaydi.TryGetValue(k, out int r)) continue;
                if (g.Count > 0 && g[^1].Item1.Ad == k.Ad) g[^1].Item2.Add(r); else g.Add((k, new List<int> { r }));
            }
            return g;
        }

        /// <summary>Kombinasyonlar: 1.4G+1.6Qi (i=1..6), G+Q1±E (8..11), 0.9G±E (8..11).</summary>
        static readonly List<(bool deprem, double[] c)> Kombler = KomblerOlustur();
        static List<(bool, double[])> KomblerOlustur()
        {
            var l = new List<(bool, double[])>();
            for (int i = 1; i <= 6; i++) { var c = new double[22]; c[0] = 1.4; c[i] = 1.6; l.Add((false, c)); }
            for (int e = 8; e <= 11; e++) foreach (int sg in new[] { 1, -1 }) { var c = new double[22]; c[0] = 1; c[1] = 1; c[e] = sg; l.Add((true, c)); }
            for (int e = 8; e <= 11; e++) foreach (int sg in new[] { 1, -1 }) { var c = new double[22]; c[0] = 0.9; c[e] = sg; l.Add((true, c)); }
            return l;
        }
        static double K(float[] a, double[] c) { double s = 0; for (int i = 0; i < 22; i++) s += a[i] * c[i]; return s; }

        /// <summary>Kesme kesiti uzaklığı (yüzden, m): a = (p1+p2)/2; a > Ln/3 → 0; p1 veya p2 ≈ 0 → a; değilse a + min(d, Ln/3).</summary>
        public static double X0(double p1, double p2, double d, double L)
        {
            double a = (p1 + p2) / 2;
            if (a > L / 3) return 0;
            return p1 < 0.0005 || p2 < 0.0005 ? a : a + Math.Min(d, L / 3);
        }

        static List<(double V, double T, bool sol)> Adaylar(Uc u, double dE)
        {
            var l = new List<(double, double, bool)>();
            foreach (var (deprem, cf) in Kombler)
            {
                double vl = K(u.VL, cf), vr = K(u.VR, cf); var cg = (double[])cf.Clone(); for (int i = 7; i < 22; i++) cg[i] = 0;
                double q = (K(u.VL, cg) - K(u.VR, cg)) / u.Ln;
                foreach (bool sol in new[] { true, false })
                {
                    double x0 = X0(sol ? u.l1 : u.r1, sol ? u.l2 : u.r2, dE, u.Ln);
                    if (x0 == 0 && deprem) continue;   // rijit uçta depremli kombinasyon alınmaz
                    l.Add((Math.Abs(sol ? vl : vr) - Math.Abs(q) * x0, Math.Abs(K(sol ? u.TL : u.TR, cf)), sol));
                }
            }
            return l;
        }

        public record Sonuc(double Vcr, double Vw, double Vr, double Vd, double Td, double Tcr, double Tr, double Aov, double Aot, double Ao, double Asw, bool Ok);

        static double Kanat(IkiliKayit m, int i, double Ln)
        {
            int f = m.S(i); double t = 0.06 * Ln * 100;
            return f <= 0 ? 0 : (f < Math.Floor(t + 1e-6) ? f : t);
        }
        /// <summary>Sx = Σ x²y/3 (x kısa kenar): gövde bw×H + iki tabla hf×f (hf KMX +2/+6, f = 0.06·Ln; KMX +0/+4 daha küçükse o).</summary>
        static double Sx(St4Kiris k, IkiliKayit m, double Ln)
        {
            double R(double a, double b) { double x = Math.Min(a, b), y = Math.Max(a, b); return x * x * y / 3; }
            double s = R(k.B, k.H);
            foreach (var (fo, ho) in new[] { (0, 2), (4, 6) }) { double f = Kanat(m, fo, Ln); int hf = m.S(ho); if (f > 0 && hf > 0) s += R(f, hf); }
            return s;
        }

        public static Sonuc Hesapla(StaVeri v, St4Kiris k, List<int> rr, double fctd)
        {
            var u = Birlesik(v, rr); var m = u.Kmx;
            double d = k.H - 5.5;
            double Vcr = 0.65 * fctd * k.B * d / 1000;
            int fi = m.S(146); int s = m.S(150) > 0 ? m.S(150) : m.S(148); int n = m.S(144) >= 2 ? 4 : 2;
            double asw = s > 0 ? n * Pi * fi * fi / 400.0 / s : 0;
            double Vw = asw * FywV * d / 1000, Vr = 0.8 * Vcr + Vw;
            double Tcr = 1.35 * fctd * Sx(k, m, u.Ln) / 1e5;
            var ad = Adaylar(u, d / 100);
            if (ad.Count == 0) ad.Add((0, 0, true));
            double TrF((double V, double T, bool sol) x) => (x.V / Vcr) * (x.V / Vcr) + (x.T / Tcr) * (x.T / Tcr);
            var e = ad.OrderByDescending(TrF).First();
            double Ae = (k.B - 11.0) * (k.H - 11.0);
            double AoV(double V) => Math.Max(0, V - 0.8 * Vcr) * 1000 / (2 * d * FywA);
            double AoT(double T) => T * 1e5 / (2 * Ae * FywA);
            var us = new[] { true, false }.Where(b => ad.Any(x => x.sol == b))
                .Select(b => ad.Where(x => x.sol == b).OrderByDescending(TrF).First()).OrderByDescending(x => AoV(x.V) + AoT(x.T)).First();
            double Aov = AoV(us.V), Aot = AoT(us.T);
            return new Sonuc(Vcr, Vw, Vr, e.V, e.T, Tcr, TrF(e), Aov, Aot, Aov + Aot, asw, Aov + Aot <= asw && e.V <= Vr);
        }

        public record AskiSatir(string Ana, string Tali, string Don, double Asw, string BwH, double Vc, double Vw, double Vr, double Vd, int Sira = 0);

        /// <summary>
        /// Askı: kolon olmayan, ≥3 kiriş ucunun buluştuğu düğüm; içinden geçen aynı adlı parça çifti ana kiriş, diğerleri tali
        /// ("A+B"). Tali iki parçalı, aynı doğrultulu ve kesiti ana kirişten küçük değilse roller değişir. Askı "í{ø}/9" (ø ana
        /// kiriş KMX +146), Asw = 2·π·ø²/4, Vc = 0.8·0.65·fctd·bw·d, Vw = Asw/9·fywd·d. Vd (yaklaşık): Σ tali |V_G,uç| − |q_G|·x0.
        /// </summary>
        public static List<AskiSatir> Aski(Proj p, StaVeri v, double fctd)
        {
            var kol = new HashSet<int>(); for (int i = 0; i < v.Col.Adet; i++) kol.Add(v.Col[i].S(0));
            var uc = new SortedDictionary<int, List<(St4Kiris k, int r, bool sol)>>();
            foreach (var k in v.Model.Kirisler)
            {
                if (!v.KirisKaydi.TryGetValue(k, out int r)) continue;
                var b = v.Bea[r];
                foreach (var (n, s) in new[] { ((int)b.S(32), true), ((int)b.S(34), false) })
                { if (!uc.TryGetValue(n, out var l)) uc[n] = l = new(); l.Add((k, r, s)); }
            }
            var sonuc = new List<AskiSatir>();
            foreach (var (n, l) in uc)
            {
                if (kol.Contains(n) || n == 0 || l.Count < 3) continue;
                var cift = l.GroupBy(z => z.k.Ad).FirstOrDefault(g => g.Count() == 2 && g.Any(z => z.sol) && g.Any(z => !z.sol));
                if (cift == null) continue;
                var ana = cift.ToList(); var tali = l.Where(z => z.k.Ad != cift.Key).ToList();
                if (tali.Count == 2 && tali[0].k.YYonu == tali[1].k.YYonu && tali.Any(z => z.sol) && tali.Any(z => !z.sol) && tali[0].k.B * tali[0].k.H >= ana[0].k.B * ana[0].k.H)
                    (ana, tali) = (tali, ana);
                var m = ana.First(z => !z.sol);
                int fi = v.Kmx[m.r].S(146); const int s9 = 9;
                double asw = 2 * Pi * fi * fi / 400.0, d = m.k.H - 5.5;
                double Vc = 0.8 * 0.65 * fctd * m.k.B * d / 1000, Vw = asw / s9 * FywV * d / 1000;
                double vd = 0;
                foreach (var t in tali)
                {
                    var u = Birlesik(v, new List<int> { t.r });
                    var arr = t.sol ? u.VL : u.VR;
                    double x0 = X0(t.sol ? u.l1 : u.r1, t.sol ? u.l2 : u.r2, (t.k.H - 5.5) / 100, u.Ln);
                    double qG = (u.VL[0] - u.VR[0]) / u.Ln;
                    vd += Math.Abs(arr[0]) - Math.Abs(qG) * x0;
                }
                sonuc.Add(new AskiSatir(KirisAdi(p, m.k), string.Join("+", tali.Select(z => KirisAdi(p, z.k))), $"í{fi}/{s9}", asw, $"{m.k.B}/{m.k.H}", Vc, Vw, Vc + Vw, vd, tali.Min(z => z.k.Sira)));
            }
            return sonuc.OrderBy(a => a.Sira).ToList();   // sıra: ilk tali kirişin ST4 sırası
        }
    }
}

// ================= Kolon / perde kesme (GPR s.92–100) =================
public static partial class HesapTablolari
{
    /// <summary>
    /// "KOLONLARIN KESME DAYANIM KONTROLU", "KOLON SARILMA BÖLGESİ ENİNE DONATI KONTROLU   TBDY2018-7.3.4",
    /// "KOLONLARIN KESME GUVENLİK KONTROLU", "PERDELERİN KESME GÜVENLİK KONTROLU".
    /// Dosyalar: .ST4 (/Materials/ fck, fyk; kolonlar; R; Hbodrum; D), .COL (Tx +466, Ty +554, Nz +26, uç momentleri, düğümler),
    /// .SMX (donatı int16 +161.., etriye kol baytları 193/195, N +40/+100), .BEA/.KMX (kiriş uç donatısı → Mp).
    /// Kesin: Ac, As, Asw/s, Vw, Vcr, Vr, Vfr, x yönü Vd; sarılma metni; perde N, Vr, Vrh, Vfr, Bv.
    /// Yaklaşık: y yönü Vd (zarf kuralı projeler arası tutarsız), perde β ve üst kat Y Vd, kesme güvenlik tablosunun
    /// Mp/Mr/Mu/Ma/Ve hücreleri (kapasite ±%1, Ve tavanı V(G+Q+D·E) varsayımı).
    /// </summary>
    public static List<string> KolonKesme(Proj p)
    {
        var v = Veri(p); if (v == null || !v.Col.Var || !v.Smx.Var) return null;
        var h = new KolonKesmeH(p, v);
        var o = new List<string>();
        h.Dayanim(o); h.Sarilma(o); h.Guvenlik(o); h.Perde(o);
        return o;
    }

    sealed class KolonKesmeH
    {
        readonly Proj p; readonly StaVeri v;
        readonly double fck, fyk, fyd, fctd, D; readonly bool sinirli;
        public KolonKesmeH(Proj p, StaVeri v)
        {
            this.p = p; this.v = v;
            var m = Bolum(p, "/Materials/")[0];
            fck = Proj.D(m[0]); fyk = Proj.D(m[1]); fyd = fyk / 1.15;
            fctd = 0.35 * Math.Sqrt(fck) * 3.16 / 1.5;
            sinirli = v.Model.R <= 4;
            D = v.Model.Dfazla > 0 ? v.Model.Dfazla : 2.5;
        }

        static double Alan(int f) => 3.142185 * f * f / 400.0;
        static string K(double x) => Math.Round((decimal)x, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);
        static bool PerdeMi(St4Kolon k) => k.Poligon ? k.PerdeX || k.PerdeY : Math.Max(k.Bx, k.By) >= 6 * Math.Min(k.Bx, k.By);
        short A(St4Kolon k, int i) => v.Smx[k.Sira].S(161 + 2 * i);
        int Aralik(St4Kolon k) => A(k, 2) > 0 ? A(k, 2) : A(k, 1);
        double As(St4Kolon k) => 2 * A(k, 5) * Alan(A(k, 6)) + 2 * A(k, 7) * Alan(A(k, 8));

        (int lx, int ly) Kol(St4Kolon k)
        {
            var s = v.Smx[k.Sira];
            int B(int o) => (s.S(o - 1) >> 8) & 0xFF;
            int uzun = 2 + B(193) + ((A(k, 4) & 2) != 0 ? 2 : 0), kisa = 2 + B(195) + ((A(k, 4) & 1) != 0 ? 2 : 0);
            return k.By >= k.Bx ? (uzun, kisa) : (kisa, uzun);
        }
        bool Bodrum(St4Kolon k)
        {
            var kt = v.Model.Katlar;
            return v.Model.Hbodrum > 0 && k.Kat < kt.Count && kt[k.Kat].Kot <= kt[0].Kot + v.Model.Hbodrum + 0.01 && A(k, 2) == 0;
        }
        IEnumerable<St4Kolon> Sira() => v.Model.Kolonlar.Where(k => !PerdeMi(k)).OrderBy(k => k.Yerel).ThenByDescending(k => k.Kat);

        /// <summary>Vd (t): x: max(|G|+max|Qi|+max|EX±|, 1.4|G|+1.6max|Qi|); y: max(|G+Q1|+max|EY±|, |1.4G+1.6Q1|) (y yaklaşık).</summary>
        static double Vd(float[] t, bool x)
        {
            double g = t[0], qm = Enumerable.Range(1, 6).Max(i => Math.Abs(t[i]));
            double e = Enumerable.Range(x ? 8 : 10, 2).Max(i => Math.Abs(t[i]));
            double gq = x ? Math.Abs(g) + qm : Math.Abs(g + t[1]);
            double u = x ? 1.4 * Math.Abs(g) + 1.6 * qm : Math.Abs(1.4 * g + 1.6 * t[1]);
            return Math.Max(gq + e, u);
        }

        // ---------------- kesme dayanım ----------------
        public void Dayanim(List<string> o)
        {
            o.Add(" KOLONLARIN KESME DAYANIM KONTROLU");
            o.Add(" Vw=(Asw/s)·fywd·d ,  Vcr=0.65·fctd·Ac , Vr=0.8·Vcr+Vw, 0.85×Ac×ûfck È Vd  (t,cm²)   TBDY2018-7.3.7");
            o.Add(" Vfr = (µ×As×fyd) > Vd   İş Derzi Sürtünme Kesme Dayanımı    TS500-8.1.7");
            o.Add($" Vcr = 0.2 fcd Ac  > Vd     TS500-8.1.7  , µ = 1.0 (pürüzlendirilmiş yüzey È5mm)    fctd={N(fctd, 2)}(kg/cm²)");
            int[] en = { 10, 7, 7, 8, 12, 12, 12, 15, 15 };
            var g = new List<string>();
            foreach (var k in Sira())
            {
                var c = v.Col[k.Sira];
                double ac = k.Bx * k.By, vcr = 0.65 * fctd * ac, asl = As(k);
                var (lx, ly) = Kol(k); int sp = Aralik(k); int f = A(k, 0);
                bool bod = Bodrum(k);
                foreach (bool x in new[] { true, false })
                {
                    double asw = (x ? lx : ly) * (3.14 * f * f / 400.0) / sp;
                    double vw = asw * fyd * (x ? k.Bx : k.By);
                    double vd = Vd(c.Dizi(x ? 466 : 554), x) * 1000;
                    double vr = (bod ? 1.0 : 0.8) * vcr + vw, vfr = asl * fyd + (bod ? fctd * ac : 0);
                    g.Add(Satir(Sol(k.Ad, 8) + (x ? "x " : "y "), Sag(((int)ac).ToString(), 6) + " ", Sag(N(asl, 2), 6) + " ", Sag(N(asw, 4), 7) + " ",
                        Sag(N(vw / 1000, 3), 11) + " ", Sag(N(vcr / 1000, 3), 11) + " ", Sag(N(vd / 1000, 3), 11) + " ",
                        Sag(N(vr / 1000, 3), 13) + (vr >= vd ? " ò" : " ñ"), Sag(N(vfr / 1000, 3), 11) + (vfr >= vd ? " ò  " : " ñ  ")));
                }
            }
            Tablo(o, en, new[] { Satir("Kolon     ", "   Ac  ", "   As  ", "  Asw/s ", "     Vw     ", "     Vcr    ", "     Vd     ", "Vr=0.8 Vcr+ Vw ", "      Vfr      ") }, g);
        }

        // ---------------- sarılma ----------------
        public void Sarilma(List<string> o)
        {
            o.Add(" KOLON SARILMA BÖLGESİ ENİNE DONATI KONTROLU   TBDY2018-7.3.4");
            int[] en = { 8, 98 };
            o.Add(Ust(en)); o.Add(Satir("Kolon   ", Sol("                Kontrol", 98)));
            bool ilk = true;
            foreach (var k in Sira().Where(k => !Bodrum(k)))
            {
                o.Add(Ayrac(en));
                var s = v.Smx[k.Sira];
                int sp = Aralik(k), f = A(k, 0); var (lx, ly) = Kol(k);
                double ac = k.Bx * k.By, ack = (k.Bx - 5) * (k.By - 5);
                double nd = Math.Max(s.F(40), s.F(100)) * 1000, oran = nd / (0.2 * ac * fck); bool buyuk = oran > 1;
                double a1 = 0.785 * f * f / 100.0;
                o.Add(Satir(Sol(k.Ad, 8), Sol($"Malz.E1 C{fck / 10:0} B{fyk / 10:0}C, b/d= {k.Bx}×{k.By}, Etriye:í{f}/{sp}, X/Y kol:{lx}/{ly}, Nd/(0.2×Ac×fck)={K(oran)}, » Ash > {(buyuk ? "" : "2/3×")}Asw", 98)));
                foreach (bool x in new[] { true, false })
                {
                    int bk = x ? k.By - 5 : k.Bx - 5, n = x ? lx : ly;
                    double s2 = sinirli ? sp / 2.0 : sp;
                    double w1 = 0.3 * s2 * bk * (ac / ack - 1) * fck / fyk, w2 = 0.075 * s2 * bk * fck / fyk;
                    double asw = (buyuk ? 1 : 0.66) * Math.Max(w1, w2), ash = n * a1;
                    o.Add(Satir(x ? "X yonu  " : "Y yonu  ", Sol($"bk={bk} cm, Asw1={K(w1)}, Asw2={K(w2)} » Asw=max(Asw1,Asw2)={K(asw)}, Ash={n}×{K(a1)}={K(ash)}cm²> {(buyuk ? "" : "2/3×")}Asw {(ash >= asw ? "ò" : "ñ")}", 98)));
                }
                ilk = false;
            }
            if (ilk) o.Add(Ayrac(en));
            o.Add(Alt(en));
        }

        // ---------------- kesme güvenlik ----------------
        St4Kolon UstK(St4Kolon k) => v.Model.Kolonlar.FirstOrDefault(z => z.Yerel == k.Yerel && z.Poligon == k.Poligon && z.Kat == k.Kat + 1);
        St4Kolon AltK(St4Kolon k) => v.Model.Kolonlar.FirstOrDefault(z => z.Yerel == k.Yerel && z.Poligon == k.Poligon && z.Kat == k.Kat - 1);
        /// <summary>Kolon boyu: kat kotu farkı; en alt katta COL +662 (H).</summary>
        double KatH(St4Kolon k) { double hc = v.Col[k.Sira].F(662); if (k.Kat == 0 && hc > 0) return hc; var kt = v.Model.Katlar; return k.Kat < kt.Count ? kt[k.Kat].Kot - (k.Kat > 0 ? kt[k.Kat - 1].Kot : 0) : 3; }
        /// <summary>Düğüme bağlanan, verilen doğrultudaki kirişler (X: aks 2xxx).</summary>
        IEnumerable<(St4Kiris k, int r, bool jUcu)> Kirisler(int dugum, bool x)
        {
            foreach (var b in v.Model.Kirisler)
            {
                if (!v.KirisKaydi.TryGetValue(b, out int r) || b.YYonu == x) continue;
                var e = v.Bea[r];
                if (e.S(32) == dugum) yield return (b, r, false);
                else if (e.S(34) == dugum) yield return (b, r, true);
            }
        }
        double KirisHmax(St4Kolon k, bool x) => Kirisler(v.Col[k.Sira].S(0), x).Select(z => (double)z.k.H).DefaultIfEmpty(0).Max();

        /// <summary>Kolon Mr (tm): dikdörtgen gerilme bloğu (0.85fck, k1 0.85, εcu 0.003, Es 2e6), fyk, paspayı 4 cm; N = SMX +40 (x) / +100 (y).</summary>
        double Mr(St4Kolon k, bool x)
        {
            var s = v.Smx[k.Sira];
            double hh = x ? k.Bx : k.By, b = x ? k.By : k.Bx, pas = 4;
            double A1 = Alan(A(k, 6)), A2 = Alan(A(k, 8)); int n1 = A(k, 5), n2 = A(k, 7);
            var kat = new List<(double y, double a)>();
            bool hKisa = (x && k.By >= k.Bx) || (!x && k.By < k.Bx);
            if (hKisa)
            {
                kat.Add((pas, 2 * A1 + n2 * A2)); kat.Add((hh - pas, 2 * A1 + n2 * A2));
                for (int i = 1; i <= n1 - 2; i++) kat.Add((pas + (hh - 2 * pas) * i / (n1 - 1), 2 * A1));
            }
            else
            {
                kat.Add((pas, n1 * A1)); kat.Add((hh - pas, n1 * A1));
                for (int i = 1; i <= n2; i++) kat.Add((pas + (hh - 2 * pas) * i / (n2 + 1), 2 * A2));
            }
            return KesitMr(b, hh, kat, s.F(x ? 40 : 100) * 1000, fck, fyk);
        }
        static double KesitMr(double b, double h, List<(double y, double A)> kat, double N, double fc, double fy)
        {
            const double Es = 2e6, k1 = 0.85, ecu = 0.003;
            double Kuv(double c, out double M)
            {
                double a = Math.Min(k1 * c, h), C = 0.85 * fc * b * a; M = C * (h / 2 - a / 2);
                double S = 0;
                foreach (var (y, Ar) in kat)
                {
                    double sg = Math.Max(-fy, Math.Min(fy, ecu * (c - y) / c * Es)); if (y < a) sg -= 0.85 * fc;
                    S += sg * Ar; M += sg * Ar * (h / 2 - y);
                }
                return C + S - N;
            }
            double lo = 0.01, hi = 10 * h, m = 0;
            for (int i = 0; i < 200; i++) { double c = (lo + hi) / 2; if (Kuv(c, out _) > 0) hi = c; else lo = c; }
            Kuv((lo + hi) / 2, out m);
            return m / 1e5;
        }
        /// <summary>Kiriş uç kapasitesi (tm): As·fyk·(d − a/2), d = h − 3, a = As·fyk/(0.85·fck·bw). Üst: montaj + pilyeler + uç üst ilave; alt: düz + uç alt ilave.</summary>
        double KirisMp(St4Kiris b, int r, bool jUcu, bool ust)
        {
            var m = v.Kmx[r];
            double Ad(int i) => m.S(108 + 4 * i) * Math.PI * m.S(110 + 4 * i) * m.S(110 + 4 * i) / 400.0;
            bool sag = jUcu;
            double asl = ust ? Ad(0) + Ad(2) + Ad(3) + Ad(sag ? 7 : 5) : Ad(1) + Ad(sag ? 8 : 6);
            double d = b.H - 3, a = asl * fyk / (0.85 * fck * b.B);
            return asl * fyk * (d - a / 2) / 1e5;
        }
        /// <summary>Düğümde ±yön kiriş ΣMp: + sağa ötelenmede düğümün solundaki kirişin J ucu alt, sağındaki I ucu üst donatıyla.</summary>
        double DugumMp(int dugum, bool x, bool arti)
        {
            double t = 0;
            foreach (var (b, r, j) in Kirisler(dugum, x))
                t += KirisMp(b, r, j, arti ? j : !j);
            return arti ? t : -t;
        }
        /// <summary>Kısa kolon: kolon alt düğümündeki o doğrultudaki kirişte kat yüksekliğinden kısa (H &lt; Ln) duvar → Ln − H.</summary>
        double KisaDuvar(St4Kolon k, bool x, double ln)
        {
            double hmax = 0;
            foreach (var (b, r, j) in Kirisler(v.Col[k.Sira].S(2), x))
            {
                var f = KirisSatiri(p, b); double B = Proj.D(f.ElementAtOrDefault(16) ?? "0"); int kod = Ii(f.ElementAtOrDefault(17) ?? "0");
                if (B > 10 && kod > 0) { double hw = (kod % 10000) / 100.0; if (hw < ln - 0.01) hmax = Math.Max(hmax, hw); }
            }
            return hmax;
        }

        public void Guvenlik(List<string> o)
        {
            o.Add(" KOLONLARIN KESME GUVENLİK KONTROLU");
            o.Add(" Ve>Vd(G+Q+D.E,Bax+0.3×Bay) =>  Ve=Vd(G+Q+D.E,Bax+0.3×Bay)");
            if (v.Model.Hbodrum > 0) o.Add(" Bodrum katlar; sınırlı sünek kat olup, kesme güvenlik yeterliliği aranmaz.");
            o.Add(" 10cm'den geniş tuğla duvarlar, kesme güvenliğinde dikkate alınmıştır.");
            o.Add(" TBDY2018-7.3.7");
            int[] en = { 9, 22, 22, 22, 22 };
            o.Add(Ust(en));
            o.Add(Satir("         ", "          +X          ", "          -X          ", "          +Y          ", "          -Y          "));
            o.Add(Satir("Kolon    ", "    Mp     Mc    Mr   ", "    Mp     Mc    Mr   ", "    Mp     Mc    Mr   ", "    Mp     Mc     Mr  "));
            int kisaSay = 0;
            string Sy(double x) => Sag(N(x, 2), 7);
            foreach (var k in Sira().Where(k => !Bodrum(k)))
            {
                o.Add(Ayrac(en));
                var c = v.Col[k.Sira]; var ust = UstK(k); var alt = AltK(k);
                int du = c.S(0), da = c.S(2);
                var hucre = new string[6][];
                for (int i = 0; i < 6; i++) hucre[i] = new string[4];
                double[] lnD = new double[2]; bool kisaVar = false;
                double ac = k.Bx * k.By, nd = Math.Max(v.Smx[k.Sira].F(40), v.Smx[k.Sira].F(100)) * 1000;
                double vcr = 0.65 * fctd * ac; var (lx, ly) = Kol(k);
                for (int d = 0; d < 4; d++)
                {
                    bool x = d < 2, arti = d % 2 == 0;
                    double ln = KatH(k) - KirisHmax(k, x) / 100.0;
                    double kd = KisaDuvar(k, x, ln); bool kisa = kd > 0;
                    if (kisa) { ln -= kd; kisaVar = true; }
                    lnD[x ? 0 : 1] = ln;
                    double mrK = Mr(k, x), mrU = ust != null ? Mr(ust, x) : 0, mrA = alt != null ? Mr(alt, x) : 0;
                    double mpU = DugumMp(du, x, arti), mpA = alt != null ? DugumMp(da, x, arti) : 0;
                    double mu, ma;
                    if (kisa) { mu = ma = mrK; }
                    else
                    {
                        mu = ust == null || mpU == 0 ? mrK : Math.Min(mrK, Math.Abs(mpU) * Pay(k, ust, x, true, arti));
                        ma = alt == null || mpA == 0 ? mrK : Math.Min(mrK, Math.Abs(mpA) * Pay(k, alt, x, false, arti));   // alt düğümde o doğrultuda kiriş yoksa Ma = Mr
                    }
                    double vw = (x ? lx : ly) * (3.14 * A(k, 0) * A(k, 0) / 400.0) / Aralik(k) * fyd * (x ? k.Bx : k.By);
                    double vr = (vw + (nd > 0.05 * ac * fck ? 0.8 * vcr : 0)) / 1000;
                    double ve = (mu + ma) / ln, tavan = Tavan(c, x);
                    if (ve > tavan) ve = tavan;
                    hucre[0][d] = kisa ? Sol("    Kisa kolon", 22) : Sol(Sag(N(mrU, 2), 12), 22);
                    hucre[1][d] = (kisa ? "       " : Sy(mpU)) + "  Å  Mu=" + Sag(N(mu, 2), 6) + " ";
                    hucre[2][d] = Sol(Sag(N(mrK, 2), 12), 22);
                    hucre[3][d] = (kisa ? "       " : Sy(mpA)) + "  Å  Ma=" + Sag(N(ma, 2), 6) + " ";
                    hucre[4][d] = Sol(Sag(N(mrA, 2), 12), 22);
                    hucre[5][d] = "Ve=" + Sag(N(ve, 2), 6) + "  Vr=" + Sag(N(vr, 2), 6) + (vr >= ve ? " ò" : " ñ");
                }
                if (kisaVar) kisaSay++;
                string[] sol = { Sol(k.Ad, 9), "         ", "Lnx=" + Sag(N(lnD[0], 2), 5), "Lny=" + Sag(N(lnD[1], 2), 5), "         ", "         " };
                for (int i = 0; i < 6; i++) o.Add(Satir(new[] { sol[i] }.Concat(hucre[i]).ToArray()));
            }
            o.Add(Alt(en));
            if (kisaSay > 0)
            {
                o.Add($" {kisaSay} adet kısa kolon bulunmuştur.");
                o.Add(" Kısa kolonlar; kolon kenarındaki dolgu duvarlarında pencere boşluğu nedeni ile oluşabilirler.");
                o.Add(" Kısa kolon olması durumunda, kesme güvenlik hesabı kısa kolon yüksekliğine göre yapılmaktadır.");
            }
        }
        /// <summary>Düğüm ΣMp'nin bu kolona düşen payı: deprem analiz uç momentleri oranı (X: EX− indis 9, Y: EY+ indis 10).</summary>
        double Pay(St4Kolon k, St4Kolon diger, bool x, bool ustDugum, bool arti)
        {
            int e = x ? 9 : 10;
            double mk = Math.Abs(v.Col[k.Sira].F((ustDugum ? (x ? 114 : 290) : (x ? 202 : 378)) + 4 * e));
            double md = Math.Abs(v.Col[diger.Sira].F((ustDugum ? (x ? 202 : 378) : (x ? 114 : 290)) + 4 * e));
            return mk + md > 0 ? mk / (mk + md) : 0.5;
        }
        /// <summary>Ve tavanı: |V(G+Q1)| + D·(max|E| + 0.3·max|E dik|) (yaklaşık; STA4 kuralı çözülmedi).</summary>
        double Tavan(IkiliKayit c, bool x)
        {
            var t = c.Dizi(x ? 466 : 554); var dik = c.Dizi(x ? 554 : 466);
            double e = Enumerable.Range(x ? 8 : 10, 2).Max(i => Math.Abs(t[i])), e2 = Enumerable.Range(x ? 10 : 8, 2).Max(i => Math.Abs(t[i]));
            return Math.Abs(t[0] + t[1]) + 2 * D * (e + 0.3 * e2);
        }

        // ---------------- perde kesme güvenlik ----------------
        public void Perde(List<string> o)
        {
            var perdeler = v.Model.Kolonlar.Where(PerdeMi).OrderBy(k => k.Kat).ThenBy(k => k.Yerel).ToList();
            o.Add(" PERDELERİN KESME GÜVENLİK KONTROLU");
            o.Add(" Vr  = Ach (0.65  fctd + rsh fyd)  Perde Kesme Dayanımı");
            o.Add(" Vrh = 0.85×Ac×ûfck                Max. Beton Kesme Dayanımı");
            o.Add(" Vd  = 1.2×D×(Vex+0.3×Vey)         Max. kesme kuvveti");
            o.Add(" ïshx  = Asws.( ä Letrx/s )/Ach    ïshy  = Asws.( ä Letry/s )/Ach");
            o.Add(" Vfr = (fctd×Ac + µ×As×fyd) =      İş Derzi Sürtünme Kesme Dayanımı   TBDY2018-7.6.7.2");
            o.Add(" Bv                                Perde dinamik etki parametresi");
            o.Add(" âm=(Mp/Md) < è=(Vu/Ve)            Perde kesme kapasite büyütmesi");
            o.Add(" Ve=ßv â Vd,  Vd=V(G+Q+E,Bax+0.3×Bay)    Ve<V(G+Q+D.E,Bax+0.3×Bay)    TBDY2018-7.6.6.3");
            o.Add($" µ = 1 (pürüzlendirilmiş yüzey È5mm)    fctd={N(fctd, 2)}(kg/cm²)");
            int[] en = { 7, 4, 5, 5, 5, 10, 10, 10, 10, 10, 10, 4 };
            o.Add(Ust(en));
            o.Add(Satir(" Perde ", " Yön", "  Ln ", "  Bv ", "  â  ", "     N    ", "     Vd   ", "     Vr   ", "    Vrh   ", "    Vfd   ", "    Vfr   ", "    ") + " (t)");
            foreach (var k in perdeler)
            {
                o.Add(Ayrac(en));
                var c = v.Col[k.Sira]; var n = c.Dizi(26);
                double ac = k.Ac, asl = As(k), lw = Math.Max(k.Bx, k.By), bw = Math.Min(k.Bx, k.By);
                bool guclY = k.By >= k.Bx; double aet = 3.14 * A(k, 0) * A(k, 0) / 400.0;
                foreach (bool x in new[] { true, false })
                {
                    bool guclu = x != guclY;
                    var t = c.Dizi(x ? 466 : 554);
                    double gqa = Enumerable.Range(1, 6).Max(i => Math.Abs(t[0] + t[i]));
                    double e = Enumerable.Range(x ? 8 : 10, 2).Max(i => Math.Abs(t[i])), e2 = Enumerable.Range(x ? 10 : 8, 2).Max(i => Math.Abs(t[i]));
                    double vfd = gqa + e + 0.3 * e2, cap = gqa + 1.2 * D * (e + 0.3 * e2);
                    double bv = guclu ? 1.5 : 1.0, beta = guclu ? Beta(k, x) : 1.0;
                    double vd = guclu ? Math.Min(bv * beta * vfd, cap) : vfd;
                    double N0 = n[0] + n[1] - Enumerable.Range(x ? 8 : 10, 2).Max(i => Math.Abs(n[i]));
                    double ln = KatH(k) - KirisHmax(k, x) / 100.0;
                    double rho;
                    if (guclu) rho = 2 * aet / (bw * A(k, 1));
                    else
                    {
                        int ng = (int)Math.Ceiling((lw - 0.4 * lw) / 25.0 - 1e-9);
                        rho = aet * (bw - 4) * (4.0 / Aralik(k) + ng / (double)A(k, 1)) / ac;
                    }
                    double vr = ac * (0.65 * fctd + rho * fyd), vrh = 0.85 * ac * Math.Sqrt(fck / 10) * 10.197, vfr = fctd * ac + asl * fyd;
                    bool ok = vd * 1000 <= vr && vd * 1000 <= vrh && vfd * 1000 <= vfr;
                    o.Add(Satir(x ? Sol(k.Ad, 7) : "Hw/Lw>2", x ? "  X " : "  Y ", Sag(N(ln, 2), 5), Sag(N(bv, 2), 5), Sag(N(beta, 2), 5),
                        Sag(N(N0, 3), 10), Sag(N(vd, 3), 10), Sag(N(vr / 1000, 3), 10), Sag(N(vrh / 1000, 3), 10), Sag(N(vfd, 3), 10), Sag(N(vfr / 1000, 3), 10), ok ? "  ò " : "  ñ "));
                }
            }
            o.Add(Alt(en));
        }
        /// <summary>Perde β = Mp/Md (yaklaşık): taban parçasının güçlü eksen kapasitesi Mr(fck, fyk) / tasarım momenti (±%3) (G+Q+E alt uç).</summary>
        double Beta(St4Kolon k, bool x)
        {
            var taban = v.Model.Kolonlar.Where(z => z.Yerel == k.Yerel && z.Poligon == k.Poligon).OrderBy(z => z.Kat)
                .FirstOrDefault(z => z.Kat < v.Model.Katlar.Count && v.Model.Katlar[z.Kat].Kot > v.Model.Hbodrum + v.Model.Katlar[0].Kot * 0 + 0.01) ?? k;
            var c = v.Col[taban.Sira]; var m = c.Dizi(x ? 202 : 378);
            double md = Math.Abs(m[0] + m[1]) + Enumerable.Range(x ? 8 : 10, 2).Max(i => Math.Abs(m[i]));
            if (md <= 0) return 1;
            return Math.Max(1, Mr(taban, x) / md);
        }
    }
}

// ================= KİRİŞ SEHİM ve ÇATLAK KONTROLU (s.84) =================
public static partial class HesapTablolari
{
    /// <summary>
    /// "KİRİŞ SEHİM ve ÇATLAK KONTROLU (TS500-13.2.4)". Dosyalar: ST4, BEA, KMX, COL, LOA, OPS.
    /// Satırlar: kiriş zinciri sırası (KirisSirasi), aynı adlı ardışık parçalar tek satır (L toplanır, yükler kaydırılır).
    /// - δi+δig·λ: |BEA +760| ×1000 (parçaların en büyüğü), F3 — birebir.
    /// - Elastik = Ani δi = δt/(1+λ·r), λ = 2/(1+50ρ'), ρ' = KMX montaj (+108 adet, +110 ø; konsolda +112/+114) / (b·(h − pas)),
    ///   pas = OPS 10. satır 1. alan; r = ΣG/Σ(G+Q) LOA kiriş satırlarının uzunluk integrali.
    /// - Wk = Gergely–Lutz Wk × 0.97 (sehim tablosu betonarme tablosundan ~%3 küçük), F2.
    /// - fmax: " L/360=" (OPS 10/2) veya " L/240=" (OPS 10/3) + F2(Ln/sınır·1000) + "w"; w: ST4 kiriş 18. alan %10000 ≥ 200.
    ///   Ln: ST4 2. satır 7./8. alan rijit uç ≠ 0 → aks ± rijit uç, değilse BEA yüz koordinatı (+684/+688, +700/+704);
    ///   kiriş-kiriş ucu (BEA +0/+8 = 0) → düğüm L.
    /// - sol/sağ T, M, M ac: YAKLAŞIK — STA4 dosyaya yazmıyor; ankastre tek açıklık, LOA G+Q yükleri (öz ağırlık hariç),
    ///   sol/sağ uç momentine ters simetrik ampirik düzeltme ΔM = 0.00025·W·L. ±0.01–0.03 sapar.
    /// - Durum: δt ≤ fmax → "ò".
    /// </summary>
    public static List<string> KirisSehimCatlak(Proj p)
    {
        var v = Veri(p); if (v == null || !v.Bea.Var || !v.Kmx.Var || !v.Loa.Var) return null;
        double pas = p.OpsD(10, 1), s1 = p.OpsD(10, 2), s2 = p.OpsD(10, 3);
        if (double.IsNaN(pas)) pas = 5.5; if (double.IsNaN(s1) || s1 <= 0) s1 = 360; if (double.IsNaN(s2) || s2 <= 0) s2 = 240;
        var ak = Akslar.Oku(p);
        var kol = new Dictionary<int, (double x, double y)>();
        for (int i = 0; i < v.Col.Adet; i++) { var c = v.Col[i]; kol[c.S(0)] = (c.F(18), c.F(22)); }
        int kb = p.Sec("/Beams Data/");
        string[] Sat2(St4Kiris k) => p.St4[kb + 2 + 3 * k.Sira].Split(',');

        var gruplar = new List<List<St4Kiris>>();
        foreach (var k in KirisSirasi(p))
            if (gruplar.Count > 0 && gruplar[^1][0].Ad == k.Ad) gruplar[^1].Add(k); else gruplar.Add(new() { k });
        var ic = new HashSet<int>(gruplar.Where(g => g.Count > 1).SelectMany(g => g.Take(g.Count - 1).Select(k => (int)v.Bea[v.KirisKaydi[k]].S(34))));

        int[] en = { 8, 8, 8, 8, 8, 8, 7, 15, 8, 8, 8, 3 };
        var govde = new List<string>(); bool duvarVar = false;
        foreach (var grp in gruplar)
        {
            var k0 = grp[0]; int r0 = v.KirisKaydi[k0];
            if (Panel(p, k0)) continue;   // paneller sehim tablosunda yok (K_BLG_03)
            double L = 0, fx0 = 0, fy0 = 0, fx1 = 0, fy1 = 0;
            var y = new List<SehimH.Yuk>(); var yg = new List<SehimH.Yuk>();
            foreach (var k in grp)
            {
                var b = v.Bea[v.KirisKaydi[k]];
                double U(int n, int aks) => kol.TryGetValue(n, out var q) ? (k.YYonu ? q.y : q.x) : ak[aks];
                double l = Math.Abs(U(b.S(34), k.AksSon) - U(b.S(32), k.AksBas));
                if (k == k0) { fx0 = b.F(684); fy0 = b.F(688); }
                fx1 = b.F(700); fy1 = b.F(704);
                y.AddRange(SehimH.Yukler(v, k, 1).Select(q => q with { a = q.a + L, b = q.b + L }));
                yg.AddRange(SehimH.Yukler(v, k, 0).Select(q => q with { a = q.a + L, b = q.b + L }));
                L += l;
            }
            var bI = v.Bea[v.KirisKaydi[k0]]; var bJ = v.Bea[v.KirisKaydi[grp[^1]]];
            var f0 = KirisSatiri(p, k0);
            bool konsol = (f0.ElementAtOrDefault(14) ?? "").Trim() == "1";
            bool solM = !konsol && bI.S(0) == 0 && ic.Contains(bI.S(32)), sagM = !konsol && bJ.S(8) == 0 && ic.Contains(bJ.S(34));
            double o1 = Proj.D(Sat2(k0).ElementAtOrDefault(6) ?? "0"), o2 = Proj.D(Sat2(grp[^1]).ElementAtOrDefault(7) ?? "0");
            double g0 = k0.YYonu ? fy0 : fx0, g1 = k0.YYonu ? fy1 : fx1;
            double u0 = o1 != 0 ? (float)ak[k0.AksBas] + o1 : g0, u1 = o2 != 0 ? (float)ak[grp[^1].AksSon] - o2 : g1;
            double Ln = Math.Abs(u1 - u0);
            if (!konsol && (bI.S(0) == 0 || bJ.S(8) == 0)) Ln = L;
            if (grp.Count == 1 && (solM || sagM)) L = Math.Abs(ak[k0.AksSon] - ak[k0.AksBas]);

            var s = konsol ? SehimH.Konsol(y, L, bI.S(0) == 0) : SehimH.Coz(y, L, solM, sagM);
            double wg = yg.Sum(q => (q.w1 + q.w2) / 2 * (q.b - q.a)), wq = y.Sum(q => (q.w1 + q.w2) / 2 * (q.b - q.a));
            double rr = wq > 0 ? wg / wq : 0;
            var km = v.Kmx[r0];
            int nm = konsol ? km.S(112) : km.S(108), fm = konsol ? km.S(114) : km.S(110);
            double rho = nm * Math.PI * fm * fm / 400 / (k0.B * (k0.H - pas));
            double lam = 2 / (1 + 50 * rho);
            double dt = Math.Abs(grp.Max(k => v.Bea[v.KirisKaydi[k]].F(760)) * 1000);
            double di = dt / (1 + lam * rr);
            bool w = grp.Any(k => { var f = KirisSatiri(p, k); return f.Length > 17 && Ii(f[17]) % 10000 >= 200; });
            duvarVar |= w;
            double fmax = (float)Ln / (float)(w ? s2 : s1) * 1000f;
            double wk = KirisWk(km, k0) * 0.97;
            // ampirik ters simetrik uç düzeltmesi
            double dM = 0.00025 * wq * L;
            double Ma = solM ? 0 : s.Ma - dM, Mb = sagM ? 0 : s.Mb + dM;
            double Ra = s.Ra + (Ma - s.Ma - (Mb - s.Mb)) / L, Rb = wq - Ra;
            int nM = s.M.Length;
            double Mac = nM == 0 ? 0 : Enumerable.Range(0, nM).Max(i => s.M[i] - (Ma - s.Ma) * (1 - (i + .5) / nM) - (Mb - s.Mb) * (i + .5) / nM);
            string Sy(double x, int d, int wd) => Sag(N(x, d), wd) + " ";
            govde.Add(Satir(Sol(KirisAdi(p, k0), 8), Sy(Ra, 2, 7), Sy(-Rb, 2, 7), Sy(Ma, 2, 7), Sy(-Mb, 2, 7), Sy(Mac, 2, 7), Sy(wk, 2, 6),
                " L/" + (w ? s2 : s1).ToString("0", Inv) + "=" + Sag(N(fmax, 2), 7) + (w ? "w" : " "),
                Sy(di, 3, 7), Sy(di, 3, 7), Sy(dt, 3, 7), dt <= fmax ? " ò " : " ñ "));
        }
        var o = new List<string> { " KİRİŞ SEHİM ve ÇATLAK KONTROLU     (TS500-13.2.4)" };
        Tablo(o, en, new[]
        {
            "¡  Kiriş ¡  sol T ¡  sağ T ¡  sol M ¡  sağ M ¡  M ac. ¡   Wk  ¡     fmax      ¡ Elastik¡Ani (ëi)¡ëi+ëig·è¡ ò ¡",
            "¡   no   ¡    (t) ¡    (t) ¡   (tm) ¡   (tm) ¡   (tm) ¡   mm  ¡      mm       ¡Sehim mm¡Sehim mm¡Sehim mm¡ ñ ¡",
        }, govde);
        if (duvarVar) o.Add(" w : Kiriş üzerinde kat yüksekliğinde tuğla duvar");
        return o;
    }

    static class SehimH
    {
        public record Yuk(double a, double b, double w1, double w2);
        /// <summary>LOA kiriş satırları (+26 = 0, +24 = ST4 sırası + 1): +0 başlangıç, +4 uzunluk, G1/G2 +8/+12, Q1/Q2 +16/+20.</summary>
        public static List<Yuk> Yukler(StaVeri v, St4Kiris k, double qKat)
        {
            var y = new List<Yuk>();
            for (int i = 0; i < v.Loa.Adet; i++)
            {
                var r = v.Loa[i];
                if (r.S(26) != 0 || r.S(24) != k.Sira + 1) continue;
                double a = r.F(0), L = r.F(4);
                y.Add(new Yuk(a, a + L, r.F(8) + qKat * r.F(16), r.F(12) + qKat * r.F(20)));
            }
            return y;
        }
        static double W(List<Yuk> y, double x)
        {
            double w = 0;
            foreach (var q in y) if (x >= q.a && x <= q.b && q.b > q.a) w += q.w1 + (q.w2 - q.w1) * (x - q.a) / (q.b - q.a);
            return w;
        }
        /// <summary>Ankastre (ya da mafsallı uçlu) kiriş, sayısal integrasyon (2000 parça); momentler mesnette pozitif.</summary>
        public static (double Ma, double Mb, double Ra, double Rb, double[] M) Coz(List<Yuk> y, double L, bool solMafsal, bool sagMafsal)
        {
            const int n = 2000; double dx = L / n; var w = new double[n]; var xs = new double[n];
            double W0 = 0, Wm = 0;
            for (int i = 0; i < n; i++) { xs[i] = (i + 0.5) * dx; w[i] = W(y, xs[i]) * dx; W0 += w[i]; Wm += w[i] * xs[i]; }
            double R0 = L > 0 ? (W0 * L - Wm) / L : 0; var M0 = new double[n];
            double cum = 0, cumx = 0;
            for (int i = 0; i < n; i++) { M0[i] = R0 * xs[i] - (cum * xs[i] - cumx); cum += w[i]; cumx += w[i] * xs[i]; }
            double f11 = 0, f12 = 0, f22 = 0, d1 = 0, d2 = 0;
            for (int i = 0; i < n; i++)
            {
                double m1 = 1 - xs[i] / L, m2 = xs[i] / L;
                f11 += m1 * m1; f12 += m1 * m2; f22 += m2 * m2; d1 += M0[i] * m1; d2 += M0[i] * m2;
            }
            double det = f11 * f22 - f12 * f12;
            double Ma = (d1 * f22 - d2 * f12) / det, Mb = (d2 * f11 - d1 * f12) / det;
            if (solMafsal && sagMafsal) { Ma = 0; Mb = 0; } else if (solMafsal) { Ma = 0; Mb = d2 / f22; } else if (sagMafsal) { Mb = 0; Ma = d1 / f11; }
            var M = new double[n];
            for (int i = 0; i < n; i++) M[i] = M0[i] - Ma * (1 - xs[i] / L) - Mb * xs[i] / L;
            double Ra = R0 + (Ma - Mb) / L;
            return (Ma, Mb, Ra, W0 - Ra, M);
        }
        /// <summary>Konsol: ankastre uç düğümü olan uç (solBos: sol uç serbest).</summary>
        public static (double Ma, double Mb, double Ra, double Rb, double[] M) Konsol(List<Yuk> y, double L, bool solBos)
        {
            const int n = 2000; double dx = L / n, Wt = 0, Mx = 0;
            for (int i = 0; i < n; i++) { double x = (i + .5) * dx, w = W(y, x) * dx; Wt += w; Mx += w * x; }
            var M = new double[n];
            return solBos ? (0, Wt * L - Mx, 0, Wt, M) : (Mx, 0, Wt, 0, M);
        }
    }
}

// ================= Temel kolon yükleri, sünek momentler, perde-diyafram =================
public static partial class HesapTablolari
{
    /// <summary>
    /// "TEMELLERE GELEN KOLON YÜKLERİ" + "TOPLAM ΣNg…" satırı + "TEMELLERE KOLONLARDAN GELEN SÜNEK TASARIM DEPREM MOMENTLERİ   (tm)".
    /// Kaynak: COL (900 B): N +26, Alt Mx +202, Alt My +378 (22 float: 0..6 düşey düzenler, 7 zemin, 8..13 ±EX ±EY WX WY, 14 ısı).
    /// Kolonlar: aynı (yerel no, poligon) grubunun en alt parçası ve COL +2 alt düğümü ne bir kolon üst düğümü (COL +0) ne kiriş
    /// düğümü (BEA +32/+34); sıra kat, yerel no. Hareketli yük azaltması f (TS498): STA4 >3 katta kolona göre 0.86–0.98 basıyor,
    /// kat başına Q payı dosyada yok → f = 1.000 basılır (≤3 katta STA4 ile aynı; >3 katta Q_az %2–14 fazla). Panel uçları üretilmez.
    /// Sünek: Mex1/2 = Alt Mx 8/9, Mey1/2 = Alt My 10/11 × f (bodrumsuz: kolon 0.6·D, perde D; bodrumlu: 1.5); perde Mp kesit
    /// hesabı (dikdörtgen blok k1 0.85, εcu 0.003, Es 2e6, fcd SMX +72, N SMX +40/+100, donatı SMX +161; yaklaşık).
    /// "Temellerin kayma kontrolu" alt bölümü üretilmez (radye/temel geometrisi gerekir).
    /// Doğrulama: N_ULK_02 temel yükleri ve sünek momentler (Mp hariç) birebir.
    /// </summary>
    public static List<string> TemelYukleri(Proj p)
    {
        var v = Veri(p); if (v == null || !v.Col.Var) return null;
        var kol = TemelH.TemelKolonlari(v);
        if (kol.Count == 0) return null;
        var o = new List<string>();
        int[] en = { 10, 91 };
        bool isi = TemelH.IsiVar(v);
        // hareketli yük azaltma katsayısı f: STA4 TS498 kat eksiltmesi; >3 katta kolona göre 0.86–0.98, kat başına Q payı dosyada yok → 1.000 (≤3 katta STA4 ile aynı)
        const double fk = 1.0;
        o.Add("TEMELLERE GELEN KOLON YÜKLERİ");
        o.Add(Ust(en));
        double sg = 0, sq = 0, sqf = 0;
        for (int i = 0; i < kol.Count; i++)
        {
            var k = kol[i]; var c = v.Col[v.KolonKaydi(k)];
            float[] Nn = c.Dizi(26), Mx = c.Dizi(202), My = c.Dizi(378);
            double qaz = Nn[1] * fk;
            sg += Nn[0]; sq += Nn[1]; sqf += qaz;
            if (i > 0) o.Add(Ayrac(en));
            string Et(string s) => s.Length <= 6 ? "    " + s.PadRight(6) : s.PadLeft(10);
            string Sy(double x) => N(x, 2).PadLeft(10);
            o.Add(Satir(Sol(k.Ad, 10), Sol(string.Concat(new[] { "GGGGGG", "QQQQQQ", "Q_Q_Q_", "_Q_Q_Q", "QQ_QQ_", "_QQ_QQ", "Q_QQ_Q", "Zemin" }.Select(Et)), 91)));
            o.Add(Satir("      N   ", Sol(Sy(Nn[0]) + Sy(qaz) + " (azaltma   Nq=" + Sy(Nn[1]) + "×" + N(fk, 3) + " =" + N(qaz, 2).PadLeft(8) + ")" + N(Nn[7], 2).PadLeft(18), 91)));
            o.Add(Satir("    Alt Mx", Sol(string.Concat(Enumerable.Range(0, 8).Select(j => Sy(Mx[j]))), 91)));
            o.Add(Satir("    Alt My", Sol(string.Concat(Enumerable.Range(0, 8).Select(j => Sy(My[j]))), 91)));
            var dep = new List<string> { "Deprem+X", "Deprem-X", "Deprem+Y", "Deprem-Y", "Rüzgar X", "Rüzgar Y" };
            if (isi) dep.Add("ISI");
            o.Add(Satir("          ", Sol(string.Concat(dep.Select(Et)), 91)));
            var idx = Enumerable.Range(8, 7).Concat(isi ? new[] { 14 } : new int[0]).ToArray();
            o.Add(Satir("      N   ", Sol(string.Concat(idx.Select(j => Sy(Nn[j]))), 91)));
            o.Add(Satir("    Alt Mx", Sol(string.Concat(idx.Select(j => Sy(Mx[j]))), 91)));
            o.Add(Satir("    Alt My", Sol(string.Concat(idx.Select(j => Sy(My[j]))), 91)));
        }
        o.Add(Alt(en));
        o.Add($"TOPLAM  äNg={N(sg, 2),10}     äNq={N(sq, 2),11}    äNqf={N(sqf, 2),11}");

        // ---------- sünek tasarım deprem momentleri ----------
        bool bodrum = v.Model.Hbodrum > 0.001; double dust = v.Model.Dfazla;
        o.Add("TEMELLERE KOLONLARDAN GELEN SÜNEK TASARIM DEPREM MOMENTLERİ   (tm)");
        o.Add(bodrum ? "Kolon ve Perdelerde Nez=Dalt×Ne ,  (Dalt=1.5) TBDY2018-4.10.3.2 Bodrumlu yapı"
                     : $"Kolon ve Perdelerde Nez=0.6×Dust×Ne ,  (Dust={dust.ToString("0.###", Inv)}) TBDY2018-4.10.3.1 Bodrumsuz yapı");
        int[] es = { 9, 26, 10, 10, 10, 10, 10, 10 };
        var gov = new List<string>();
        foreach (var k in kol)
        {
            var c = v.Col[v.KolonKaydi(k)];
            bool perde = TemelH.Perde(k);
            double fs = bodrum ? 1.5 : perde ? dust : 0.6 * dust;
            float[] Mx = c.Dizi(202), My = c.Dizi(378);
            string kos = (perde ? "Perde" : "Kolon") + "  Me=" + (bodrum ? "Dalt" : perde ? "Dust" : "0.6×Dust") + "×Me";
            if (perde) kos = kos.PadRight(21) + "Ë Mp";
            string mpx = "", mpy = "";
            if (perde && !k.Poligon)
            {
                var (mx, my) = TemelH.Mp(k, v.Smx[v.KolonKaydi(k)]);
                mpx = N(mx, 2); mpy = N(my, 2);
            }
            gov.Add(Satir(Sol(k.Ad, 9), Sol(kos, 26), Sag(N(Mx[8] * fs, 2), 10), Sag(N(Mx[9] * fs, 2), 10), Sag(mpx, 10),
                Sag(N(My[10] * fs, 2), 10), Sag(N(My[11] * fs, 2), 10), Sag(mpy, 10)));
        }
        Tablo(o, es, new[] { Satir("  KOLON  ", " Kolon/Perde koşulu       ", " Mex1  (9)", " Mex2 (10)", "   Mpx    ", " Mey1 (11)", " Mey2 (12)", "   Mpy    ") }, gov);
        return o;
    }

    /// <summary>
    /// "PERDE-DIYAFRAM KESME YÜKÜ KONTROLU   TBDY2018-7.11.6, 17.9": her perde parçası (uzun ≥ 6×kısa, poligon hariç) için
    /// Vx = D·|Vxalt − Vxüst| (COL Tx +466 indis 8), Vy = D·|Vyalt − Vyüst| (COL Ty +554 indis 10); üst = aynı x,y'deki üst kat parçası.
    /// Çevre: perde dikdörtgeni (COL +18/+22 ± B/2) 0.01 m dışa büyütülür; kenarlar sol (ß=270, aşağıdan yukarı), üst (0, soldan sağa),
    /// sağ (90, yukarıdan aşağı), alt (180, sağdan sola). Kenarı kesen aynı kat kirişleri (bant: aks − f7·B/200 ekseni, B genişlik,
    /// AksBas–AksSon boyu) ve döşemeler (aks dikdörtgeni; kiriş bantları dışında kalan kısım) parçalar; L &lt; 0.02 m atılır.
    /// Asb: kiriş → perde düğümüne (COL +0) bağlanan uçta max(0, montaj KMX +108/+110 + uç üst ilave (I ucu +128/+130, J ucu
    /// +136/+138) − As_gerekli (+60 / +68)); döşeme → DMX düz donatısı (kenara dik: düşey kenarda +96/+98, yatayda +114/+116), cm²/m.
    /// Vxr = fctd·d·L·|cosß| + fyd·Asb′·|sinß| + fyd·Asa′·|cosß|, Vyr = fctd·d·L·|sinß| + fyd·Asb′·|cosß| + fyd·Asa′·|sinß|
    /// (kiriş Asb′ = Asb, Asa′ = 0; döşeme Asb′ = Asa′ = Asb·L); fctd = 0.35√fck/1.5 iki haneye kesik, fyd = fyk/1.15.
    /// Şekil numara satırları üretilmez.
    /// </summary>
    public static List<string> PerdeDiyafram(Proj p)
    {
        var v = Veri(p); if (v == null || !v.Col.Var) return null;
        var perdeler = v.Model.Kolonlar.Where(k => !k.Poligon && TemelH.Perde(k)).OrderBy(k => k.Yerel).ThenByDescending(k => k.Kat).ToList();
        if (perdeler.Count == 0) return null;
        var ak = Akslar.Oku(p);
        var mal = p.St4[p.Sec("/Materials/") + 1].Split(',');
        double fck = Proj.D(mal[0]), fyk = Proj.D(mal[1]);
        double fctd = Math.Floor(0.35 * Math.Sqrt(fck / 10) / 1.5 * 10 * 100) / 100, fyd = fyk / 1.15;
        double dd = v.Model.Dfazla;
        var o = new List<string>
        {
            " PERDE-DIYAFRAM KESME YÜKÜ KONTROLU   TBDY2018-7.11.6, 17.9",
            "Vx=D×(Vxalt-Vxust),    Vy=D×(Vxalt-Vxust)",
            "Vxr=äfctd×d×L×Cosß + äfyd×Asb×Sinß + äfyd×Asa×Cosß",
            "Vyr=äfctd×d×L×Sinß + äfyd×Asb×Cosß + äfyd×Asa×Sinß",
            "Asa, Asb : Kiriş ve plaklarda tasarım fazlalığı donatı alanı",
            "   Ä¿    : Plaklara ilave edilen perde aktarma donatısıdır",
        };
        int[] en = { 5, 8, 5, 5, 5, 5, 6, 6, 6, 6, 6, 8, 9, 9 };
        var bas = new[]
        {
            Satir("Yuzey", " Eleman ", "  d  ", "  L  ", "  Rx ", "  Ry ", "  X1  ", "  Y1  ", "  X2  ", "  Y2  ", "   ß  ", "   Asb  ", "    Vxr  ", "    Vyr  "),
            Satir("  no ", "   no   ", "  cm ", "  m  ", "   m ", "   m ", "   m  ", "   m  ", "   m  ", "   m  ", "   °  ", "   cm²  ", "         ", "         "),
        };
        string R2(double x) => Math.Round(x, 2, MidpointRounding.ToEven).ToString("F2", Inv);   // STA4 koordinat yazımı (çift yuvarlama)
        foreach (var k in perdeler)
        {
            var c = v.Col[v.KolonKaydi(k)];
            float x0 = c.F(18), y0 = c.F(22);
            var ust = v.Model.Kolonlar.FirstOrDefault(u => u.Kat == k.Kat + 1 && Math.Abs(v.Col[v.KolonKaydi(u)].F(18) - x0) < 0.01 && Math.Abs(v.Col[v.KolonKaydi(u)].F(22) - y0) < 0.01);
            double vxa = c.F(466 + 32), vya = c.F(554 + 40);
            double vxu = ust == null ? 0 : v.Col[v.KolonKaydi(ust)].F(466 + 32), vyu = ust == null ? 0 : v.Col[v.KolonKaydi(ust)].F(554 + 40);
            double Vx = Math.Abs(dd * (vxa - vxu)), Vy = Math.Abs(dd * (vya - vyu));
            string ds = dd.ToString("0.##", Inv).Replace('.', ',');
            o.Add($"{k.Ad} perde-diyafram deprem kesme yukleri:");
            o.Add("D=Dust alınmıştır.");
            o.Add($"Vx=D×(Vxalt-Vxust)={ds}×({TemelH.Vb(vxa)}-({TemelH.Vb(vxu, true)}))={TemelH.Vb(Vx)}   Vy=D×(Vyalt-Vyust)={ds}×({TemelH.Vb(vya)}-({TemelH.Vb(vyu, true)}))={TemelH.Vb(Vy)}");

            var parca = TemelH.Cevre(p, v, ak, k, x0, y0);
            var gov = new List<string>(); double sx = 0, sy = 0; int no = 0;
            foreach (var q in parca)
            {
                double b = q.Beta * Math.PI / 180, cs = Math.Abs(Math.Cos(b)), sn = Math.Abs(Math.Sin(b));
                double asb = Math.Round(q.Asb, 2, MidpointRounding.AwayFromZero);
                double ae = q.Plak ? q.Asb * q.L : q.Asb, aa = q.Plak ? q.Asb * q.L : 0;   // dayanımda yuvarlanmamış Asb
                double vxr = fctd * q.D * q.L * 100 * cs / 1000 + fyd / 1000 * ae * sn + fyd / 1000 * aa * cs;
                double vyr = fctd * q.D * q.L * 100 * sn / 1000 + fyd / 1000 * ae * cs + fyd / 1000 * aa * sn;
                sx += vxr; sy += vyr;
                gov.Add(Satir(Sag((++no).ToString(), 4) + " ", " " + Sol(q.Ad, 7), Sag(q.D.ToString("0", Inv), 4) + " ", Sag(R2(q.L), 5), Sag(R2(q.Rx), 5), Sag(R2(q.Ry), 5),
                    Sag(R2(q.X1), 6), Sag(R2(q.Y1), 6), Sag(R2(q.X2), 6), Sag(R2(q.Y2), 6), Sag(q.Beta.ToString("F1", Inv), 6), Sag(N(asb, 2), 8),
                    Sag(N(vxr, 1), 7) + "  ", Sag(N(vyr, 1), 7) + "  "));
            }
            Tablo(o, en, bas, gov);
            o.Add(new string(' ', 81) + "Vr=" + N(sx, 1).PadLeft(7) + (sx >= Vx ? " ò" : " ñ") + N(sy, 1).PadLeft(8) + (sy >= Vy ? " ò" : " ñ"));
        }
        return o;
    }

    static class TemelH
    {
        public static bool Perde(St4Kolon k) => k.Poligon ? k.PerdeX || k.PerdeY : Math.Max(k.Bx, k.By) >= 6 * Math.Min(k.Bx, k.By);

        public static bool IsiVar(StaVeri v) =>
            Enumerable.Range(0, v.Col.Adet).Any(r => new[] { 114, 202, 290, 378, 466, 554, 26 }.Any(o => v.Col[r].F(o + 56) != 0))
            || Enumerable.Range(0, v.Bea.Adet).Any(r => new[] { 38, 126, 390, 478 }.Any(o => v.Bea[r].F(o + 56) != 0));

        public static List<St4Kolon> TemelKolonlari(StaVeri v)
        {
            var m = v.Model;
            var ustDugum = new HashSet<int>(m.Kolonlar.Select(k => (int)v.Col[v.KolonKaydi(k)].S(0)));
            for (int r = 0; r < v.Bea.Adet; r++) { ustDugum.Add(v.Bea[r].S(32)); ustDugum.Add(v.Bea[r].S(34)); }
            var enAlt = new HashSet<St4Kolon>(m.Kolonlar.GroupBy(k => (k.Yerel, k.Poligon)).Select(x => x.OrderBy(k => k.Kat).First()));
            return m.Kolonlar.Where(k => v.KolonKaydi(k) < v.Col.Adet && enAlt.Contains(k) && !ustDugum.Contains(v.Col[v.KolonKaydi(k)].S(2)))
                .OrderBy(k => k.Kat).ThenBy(k => k.Yerel).ToList();
        }


        /// <summary>VB biçimi: iki basamak, sondaki sıfırlar ve (ust değilse) baştaki 0 atılır; sıfır "0.0".</summary>
        public static string Vb(double x, bool ust = false)
        {
            double r = Math.Round(x, 2, MidpointRounding.AwayFromZero);
            if (r == 0) return "0.0";
            var s = r.ToString("0.##", Inv);
            if (ust) return s;   // parantez içindeki üst kat değeri baştaki 0 ile
            if (s.StartsWith("0.")) s = s.Substring(1); else if (s.StartsWith("-0.")) s = "-" + s.Substring(2);
            return s;
        }

        static double A(double f) => Math.PI * f * f / 400;

        /// <summary>Perde Mp (tm): Mpx X depremi (kesit Bx doğrultusunda eğilir), Mpy Y depremi. Yaklaşık.</summary>
        public static (double mpx, double mpy) Mp(St4Kolon k, IkiliKayit s)
        {
            short a(int i) => s.S(161 + 2 * i);
            int nu = a(5), fu = a(6), ng = a(7), fg = a(8);
            double c = 4, sp = 17.5;
            bool uzunY = k.By >= k.Bx;
            double lw = uzunY ? k.By : k.Bx, bw = uzunY ? k.Bx : k.By;
            var bars = new List<(double t, double l, double a)>();
            int per = (nu + 1) / 2, per2 = nu / 2;
            foreach (var (t, n) in new[] { (c, per), (bw - c, per2) })
                for (int i = 0; i < n; i++) { bars.Add((t, c + i * sp, A(fu))); bars.Add((t, lw - c - i * sp, A(fu))); }
            double y0 = c + (per - 1) * sp, y1 = lw - y0;
            for (int i = 1; i <= ng; i++) foreach (double t in new[] { c, bw - c }) bars.Add((t, y0 + (y1 - y0) * i / (ng + 1), A(fg)));
            var d = bars.Select(q => uzunY ? (x: q.t, y: q.l, q.a) : (x: q.l, y: q.t, q.a)).ToList();
            double fcd = s.F(72), fyd = 4200 / 1.15;
            double mpx = Mr(k.By, k.Bx, d.Select(q => (q.x, q.a)).ToList(), s.F(40), fcd, fyd);
            double mpy = Mr(k.Bx, k.By, d.Select(q => (q.y, q.a)).ToList(), s.F(100), fcd, fyd);
            return (mpx, mpy);
        }

        static double Mr(double b, double h, List<(double y, double a)> don, double N, double fcd, double fyd)
        {
            const double k1 = 0.85, fakt = 0.85, ecu = 0.003, Es = 2e6;
            double lo = 0.01, hi = 5 * h;
            double F(double cc, out double M)
            {
                double aa = Math.Min(k1 * cc, h);
                double Cc = fakt * fcd * b * aa, sum = Cc; M = Cc * (h / 2 - aa / 2);
                foreach (var (y, As) in don)
                {
                    double sg = Math.Max(-fyd, Math.Min(fyd, ecu * (cc - y) / cc * Es));
                    if (y < aa) sg -= fakt * fcd;
                    sum += sg * As; M += sg * As * (h / 2 - y);
                }
                return sum - N * 1000;
            }
            for (int i = 0; i < 200; i++) { double m = (lo + hi) / 2; if (F(m, out _) > 0) hi = m; else lo = m; }
            F((lo + hi) / 2, out double Mm);
            return Mm / 1e5;
        }

        public sealed class Parca { public string Ad; public bool Plak; public double D, L, Rx, Ry, X1, Y1, X2, Y2, Beta, Asb; }

        /// <summary>Perde çevresinin kiriş/döşeme parçaları (sırayla sol, üst, sağ, alt kenar).</summary>
        public static List<Parca> Cevre(Proj p, StaVeri v, Akslar ak, St4Kolon k, double x0, double y0)
        {
            const double e = 0.01;
            // STA4: sol/alt köşe float32 (merkez − (B/2 + 0.01)), sağ/üst = sol + B + 0.02 (double) — yazımdaki ±0.01 farkları buna bağlı
            double xa = (float)x0 - (float)(k.Bx / 200.0 + e), ya = (float)y0 - (float)(k.By / 200.0 + e);
            double xb = xa + k.Bx / 100.0 + 2 * e, yb = ya + k.By / 100.0 + 2 * e;
            int pd = v.Col[v.KolonKaydi(k)].S(0);
            // kiriş bantları (aynı kat)
            var bant = new List<(St4Kiris k, double x1, double x2, double y1, double y2)>();
            foreach (var kr in v.Model.Kirisler.Where(x => x.Kat == k.Kat && v.KirisKaydi.ContainsKey(x)))
            {
                double kay = -Ii(KirisSatiri(p, kr).ElementAtOrDefault(7) ?? "0") * kr.B / 200.0, w = kr.B / 200.0;
                double c = ak[kr.Aks] + kay, s1 = Math.Min(ak[kr.AksBas], ak[kr.AksSon]), s2 = Math.Max(ak[kr.AksBas], ak[kr.AksSon]);
                if (kr.Aks < 2000) bant.Add((kr, c - w, c + w, s1, s2)); else bant.Add((kr, s1, s2, c - w, c + w));
            }
            var dos = new List<(St4Doseme d, double x1, double x2, double y1, double y2)>();
            var fl = Bolum(p, "/Floors Data/");
            foreach (var d in v.Model.Dosemeler.Where(x => x.Kat == k.Kat))
            {
                var f = fl[2 * d.Sira];
                if (Ii(f[4]) != 0 || Ii(f[5]) != 0) continue;   // merdiven ve kotlu (sahanlık) döşemeler diyafram aktarımına girmez
                double ax = ak[Ii(f[8])], bx = ak[Ii(f[9])], ay = ak[Ii(f[10])], by = ak[Ii(f[11])];
                dos.Add((d, Math.Min(ax, bx), Math.Max(ax, bx), Math.Min(ay, by), Math.Max(ay, by)));
            }
            var r = new List<Parca>();
            // kenar: başlangıç (sx,sy), yön (dx,dy), uzunluk, ß
            var kenar = new[]
            {
                (sx: xa, sy: ya, dx: 0.0, dy: 1.0, len: yb - ya, beta: 270.0),
                (sx: xa, sy: yb, dx: 1.0, dy: 0.0, len: xb - xa, beta: 0.0),
                (sx: xb, sy: yb, dx: 0.0, dy: -1.0, len: yb - ya, beta: 90.0),
                (sx: xb, sy: ya, dx: -1.0, dy: 0.0, len: xb - xa, beta: 180.0),
            };
            const double tol = 1e-6;
            foreach (var kn in kenar)
            {
                bool dusey = kn.dx == 0;
                double sabit = dusey ? kn.sx : kn.sy;
                (double a, double b)? Aralik(double x1, double x2, double y1, double y2)
                {
                    double p1 = dusey ? x1 : y1, p2 = dusey ? x2 : y2;   // kenara dik eksendeki kapsam
                    if (sabit < p1 - tol || sabit > p2 + tol) return null;
                    double q1 = dusey ? y1 : x1, q2 = dusey ? y2 : x2;   // kenar boyunca kapsam (mutlak)
                    double t1 = ((dusey ? q1 - kn.sy : q1 - kn.sx)) / (dusey ? kn.dy : kn.dx), t2 = ((dusey ? q2 - kn.sy : q2 - kn.sx)) / (dusey ? kn.dy : kn.dx);
                    double a = Math.Max(0, Math.Min(t1, t2)), b = Math.Min(kn.len, Math.Max(t1, t2));
                    return b - a > tol ? (a, b) : null;
                }
                var ps = new List<(double a, double b, Parca pr)>();
                var kirisAr = new List<(double a, double b)>();
                foreach (var bt in bant)
                {
                    var ar = Aralik(bt.x1, bt.x2, bt.y1, bt.y2); if (ar == null) continue;
                    kirisAr.Add(ar.Value);
                    ps.Add((ar.Value.a, ar.Value.b, new Parca { Ad = KirisAdi(p, bt.k), Plak = false, D = KirisSatiri(p, bt.k).Length > 2 ? Proj.D(KirisSatiri(p, bt.k)[2]) : 0, Asb = KirisAsb(v, bt.k, pd) }));
                }
                foreach (var ds in dos)
                {
                    var ar = Aralik(ds.x1, ds.x2, ds.y1, ds.y2); if (ar == null) continue;
                    // kiriş bantlarını çıkar
                    var kal = new List<(double a, double b)> { ar.Value };
                    foreach (var ka in kirisAr)
                        kal = kal.SelectMany(z => z.b <= ka.a || z.a >= ka.b ? new[] { z } : new[] { (z.a, Math.Min(z.b, ka.a)), (Math.Max(z.a, ka.b), z.b) }.Where(w => w.Item2 - w.Item1 > tol)).ToList();
                    var dm = v.Dmx[v.DosemeKaydi(ds.d)];
                    int of = dusey ? 96 : 114; int fi = dm.S(of), si = dm.S(of + 2);
                    double asb = si > 0 ? A(fi) * 100 / si : 0;
                    foreach (var z in kal) ps.Add((z.a, z.b, new Parca { Ad = ds.d.Ad, Plak = true, D = ds.d.D, Asb = asb }));
                }
                foreach (var (a, b, pr) in ps.Where(z => z.b - z.a >= 0.02).OrderBy(z => z.a))
                {
                    pr.Rx = a; pr.Ry = b; pr.L = b - a; pr.Beta = kn.beta;
                    pr.X1 = kn.sx + kn.dx * a; pr.Y1 = kn.sy + kn.dy * a; pr.X2 = kn.sx + kn.dx * b; pr.Y2 = kn.sy + kn.dy * b;
                    r.Add(pr);
                }
            }
            return r;
        }

        static double KirisAsb(StaVeri v, St4Kiris kr, int perdeDugum)
        {
            int rec = v.KirisKaydi[kr]; var b = v.Bea[rec]; var km = v.Kmx[rec];
            bool sol = b.S(32) == perdeDugum, sag = b.S(34) == perdeDugum;
            if (!sol && !sag) return 0;
            double Ay(double f) => Math.Round(A(f), 2, MidpointRounding.AwayFromZero);   // çubuk alanı iki haneye yuvarlı
            double kon = km.S(108) * Ay(km.S(110)) + (sol ? km.S(128) * Ay(km.S(130)) : km.S(136) * Ay(km.S(138)));
            return Math.Max(0, kon - km.F(sol ? 60 : 68));
        }
    }
}
