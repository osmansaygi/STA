using System.Globalization;
using StatikRaporClaude.Veri;

namespace StatikRaporClaude.Uretec;

/// <summary>
/// STA4 "özel tablolar" bölümleri (GPR'siz): kolon süneklilik, temele gelen sünek deprem tesirleri, kiriş dengeli donatı,
/// imar kuşatılmış kolon (sabit), yapı yorumu (.YORUM), kolon/perde alan oranı. Kurallar ve doğrulama:
/// scratchpad ara_metraj (B/RAPOR_B.md, RAPOR.md). N_ULK_02 GPR'si ile hücre hücre karşılaştırıldı.
/// </summary>
public static class OzelTablolar
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;
    const string V = "¡";
    static string N(double v, int d) => Fmt.N(v, d);
    static string ND(double v, int d) => Fmt.ND(v, d);
    static string Cizgi(char sol, char ara, char sag, params int[] w) => sol + string.Join(ara.ToString(), w.Select(x => new string('Ä', x))) + sag;

    static StaVeri Veri(Proj p)
    {
        var st4 = p.FilePath(".ST4");
        return st4 == null ? null : StaVeri.Ac(st4);
    }

    static bool Perde(int a, int b) => Math.Max(a, b) >= 6 * Math.Min(a, b);

    // ------------------------------------------------------------------ B1
    /// <summary>KOLON SUNEKLILIK ALAN KONTROLU (COL Nz dizisi, SMX fcd, ST4 Sds). N_ULK_02 352/352.</summary>
    public static List<string> KolonSuneklik(Proj p)
    {
        var v = Veri(p);
        if (v == null || !v.Col.Var || !v.Smx.Var) return null;
        var m = v.Model;
        var o = new List<string>
        {
            " KOLON SUNEKLILIK ALAN KONTROLU",
            "  1.4·Ng + 1.6·Nq / (Ac·fck) < 0.6  TS500,    Ng + Nq + Ne / (Ac·fck) < 0.4 TBDY2018,   Nq= Cqa·NQ",
            "  Ne = Ned + 0.30·2/3·Sds·Ng   TBDY2018 - 4.4.4",
        };
        int[] w = { 6, 11, 8, 8, 8, 20, 20, 20 };
        o.Add(Cizgi('Ú', 'Â', '¿', w));
        o.Add("¡Kolon ¡  Boyut    ¡   Ng   ¡   Nq   ¡   Ne   ¡1,4·Ng+1,6·Nq       ¡Ng + Nq + Ne        ¡Ng + Nq             ¡");
        o.Add("¡      ¡           ¡        ¡        ¡        ¡ÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄ<0.60¡ÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄ<0.40¡ÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄ<0.35¡");
        o.Add("¡      ¡           ¡   (t)  ¡   (t)  ¡   (t)  ¡  (Ac·fck)          ¡ (Ac·fck)           ¡ (Ac·fck)           ¡");
        o.Add(Cizgi('Ã', 'Å', '´', w));
        foreach (var k in m.Kolonlar.Where(k => k.Sira < v.Col.Adet).OrderBy(k => k.Yerel).ThenByDescending(k => k.Kat))
        {
            var r = v.Col[k.Sira].Dizi(26);
            double Ng = r[0], Nq = r[1];
            double Ned = new[] { r[8], r[9], r[10], r[11] }.Max(x => Math.Abs((double)x));
            double Ne = Ned + 0.3 * 2.0 / 3.0 * m.Sds * Ng;
            double fck = v.Smx[k.Sira].F(72) * 1.5 * 10;   // kg/cm² → t/m²
            double Ac = k.Ac / 1e4;
            bool pr = Perde(k.Bx, k.By);
            double r1 = (1.4 * Ng + 1.6 * Nq) / (Ac * fck), r2 = (Ng + Nq + Ne) / (Ac * fck), r3 = (Ng + Nq) / (Ac * fck);
            string s2 = pr ? "0.35" : "0.40", s3 = pr ? "0.25" : "0.35";
            string Kont(double x, string sinir) => x < double.Parse(sinir, ci) ? $"{ND(x, 2)} < {sinir} ò" : $"{ND(x, 2)} > {sinir} û";
            string boyut = k.Poligon ? "Poligon" : $"{k.Bx}×{k.By}";
            o.Add(V + k.Ad.PadRight(6) + V + boyut.PadRight(11) + V + N(Ng, 2).PadLeft(8) + V + N(Nq, 2).PadLeft(8) + V + N(Ne, 2).PadLeft(8)
                + V + ("     " + Kont(r1, "0.60")).PadRight(20) + V + ("     " + Kont(r2, s2)).PadRight(20)
                + V + ((pr ? "Perde: " : "Kolon: ") + Kont(r3, s3)).PadRight(20) + V);
        }
        o.Add(Cizgi('À', 'Á', 'Ù', w));
        return o;
    }

    // ------------------------------------------------------------------ B2
    static readonly (string Sinif, double[] Fs)[] FsTablosu =
    {
        ("ZA", new[] { 0.8, 0.8, 0.8, 0.8, 0.8, 0.8 }), ("ZB", new[] { 0.9, 0.9, 0.9, 0.9, 0.9, 0.9 }),
        ("ZC", new[] { 1.3, 1.3, 1.2, 1.2, 1.2, 1.2 }), ("ZD", new[] { 1.6, 1.4, 1.2, 1.1, 1.0, 1.0 }),
        ("ZE", new[] { 2.4, 1.7, 1.3, 1.1, 0.9, 0.8 }),
    };
    static double FsAra(double[] y, double ss)
    {
        double t = Math.Clamp((ss - 0.25) / 0.25, 0, 5); int i = Math.Min((int)t, 4);
        return y[i] + (y[i + 1] - y[i]) * (t - i);
    }

    /// <summary>ST4 "/External loads//V14,1-Update:d.m.yyyy" sürüm tarihi (yoksa en eski).</summary>
    static DateTime St4Surum(Proj p)
    {
        var s = p.St4?.FirstOrDefault(l => l.Contains("Update:"));
        if (s == null) return DateTime.MinValue;
        return DateTime.TryParseExact(s.Substring(s.IndexOf("Update:") + 7).Trim(), new[] { "d.M.yyyy", "dd.MM.yyyy" }, ci, DateTimeStyles.None, out var d) ? d : DateTime.MinValue;
    }

    /// <summary>Kolon yerleşimi: Column axis data satır no = kolon yerel no; kod −1 → aks+b/2, +1 → aks−b/2, 0 → aks, diğer: aks+kod/1000±b/2.</summary>
    static (double x, double y) KolonMerkez(Proj p, St4Kolon k)
    {
        int a = p.Sec("/Axis data/"), c = p.Sec("/Column axis data/");
        if (a < 0 || c < 0) return (double.NaN, double.NaN);
        var eks = new List<double>();
        for (int i = a + 1; i < p.St4.Length && !p.St4[i].StartsWith("/"); i++) eks.Add(Proj.D(Proj.F(p.St4[i])[1]));
        int nx = (int)Proj.D(Proj.F(p.St4[4])[0]);
        int satir = c + k.Yerel;
        if (satir >= p.St4.Length || p.St4[satir].StartsWith("/")) return (double.NaN, double.NaN);
        var f = Proj.F(p.St4[satir]).Select(Proj.D).ToArray();
        double M(double aks, double kod, double b) => kod == 0 ? aks : kod == -1 ? aks + b / 2 : kod == 1 ? aks - b / 2 : kod < 0 ? aks + kod / 1000 + b / 2 : aks + kod / 1000 - b / 2;
        int ix = (int)f[1] - 1001, iy = nx + (int)f[2] - 2001;
        if (ix < 0 || ix >= eks.Count || iy < 0 || iy >= eks.Count) return (double.NaN, double.NaN);
        return (M(eks[ix], f[3], k.Bx / 100.0), M(eks[iy], f[4], k.By / 100.0));
    }

    /// <summary>Radye temel plakları (ST4 /Slab foundations/ aks dikdörtgenleri): alan ve sınır kutusu.</summary>
    static (double alan, double x1, double x2, double y1, double y2) Radye(Proj p)
    {
        int s = p.Sec("/Slab foundations/"), a = p.Sec("/Axis data/");
        if (s < 0 || a < 0) return (0, 0, 0, 0, 0);
        var eks = new List<double>();
        for (int i = a + 1; i < p.St4.Length && !p.St4[i].StartsWith("/"); i++) eks.Add(Proj.D(Proj.F(p.St4[i])[1]));
        int nx = (int)Proj.D(Proj.F(p.St4[4])[0]);
        double alan = 0, x1 = double.MaxValue, x2 = double.MinValue, y1 = double.MaxValue, y2 = double.MinValue;
        for (int i = s + 1; i + 2 < p.St4.Length && !p.St4[i].StartsWith("/"); i += 3)
        {
            var f = Proj.F(p.St4[i + 1]).Select(Proj.D).ToArray();
            if (f.Length < 5 || f[1] < 1001 || f[3] < 2001) continue;
            double X(double ax) => eks[(int)ax - 1001]; double Y(double ax) => eks[nx + (int)ax - 2001];
            double a1 = Math.Min(X(f[1]), X(f[2])), a2 = Math.Max(X(f[1]), X(f[2])), b1 = Math.Min(Y(f[3]), Y(f[4])), b2 = Math.Max(Y(f[3]), Y(f[4]));
            alan += (a2 - a1) * (b2 - b1);
            x1 = Math.Min(x1, a1); x2 = Math.Max(x2, a2); y1 = Math.Min(y1, b1); y2 = Math.Max(y2, b2);
        }
        return alan == 0 ? (0, 0, 0, 0, 0) : (alan, x1, x2, y1, y2);
    }

    static double RadyeKalinlik(Proj p)
    {
        int s = p.Sec("/Slab foundations/");
        return s >= 0 && s + 2 < p.St4.Length ? Proj.D(Proj.F(p.St4[s + 2])[0]) : 0;
    }

    static bool BosBolum(Proj p, string ad)
    {
        int s = p.Sec(ad); if (s < 0) return true;
        for (int i = s + 1; i < p.St4.Length && !p.St4[i].StartsWith("/"); i++)
            if (Proj.F(p.St4[i]).Any(x => Proj.D(x) != 0)) return false;
        return true;
    }

    /// <summary>
    /// TEMELLERE KOLONLARDAN GELEN, SÜNEK TASARIM DEPREM TESİRLERİ (Dust=…) — başlık bilgileri ve kolon satırları.
    /// Kolon satırları (en alt kat kolonları, yerel no sırasıyla) N_ULK_02 792/792. Çözülemeyen başlık değerleri
    /// (düşey deprem yükü, toplam temel taban alanı, davranış katsayısı sistem metni) boş; "TEMEL Gf+Qf" satırı ve
    /// TOPLAM TEMEL YÜKÜ tablosu üretilmez.
    /// </summary>
    public static List<string> TemelSunekTesirler(Proj p)
    {
        var v = Veri(p);
        if (v == null || !v.Col.Var) return null;
        var m = v.Model;
        int altKat = m.Kolonlar.Count == 0 ? 0 : m.Kolonlar.Min(k => k.Kat);
        var kolonlar = m.Kolonlar.Where(k => k.Kat == altKat && k.Sira < v.Col.Adet).OrderBy(k => k.Yerel).ToList();
        if (kolonlar.Count == 0) return null;
        double sds = m.Sds, ez = 0.3 * 2.0 / 3.0 * sds;
        // Kombinasyon 3 ölü yük katsayısı: STA4 Update ≥ 09.2026 sürümleri 1.2 (N_ULK_02), daha eskiler 1.0 (A_02, F_TIPI, 112_ACIL).
        double g3 = St4Surum(p) >= new DateTime(2026, 9, 1) ? 1.2 : 1.0;
        string F(double x, string f) => x.ToString(f, ci);
        string Satir(string etiket, string deger) => etiket.PadRight(46) + ": " + deger;

        int katAdedi = m.Kolonlar.Select(k => k.Kat).Distinct().Count();
        double hTepe = m.Katlar.Count > 0 ? m.Katlar.Max(k => k.Kot) : 0;
        string zemin = m.Ss > 0 ? FsTablosu.OrderBy(z => Math.Abs(m.Ss * FsAra(z.Fs, m.Ss) - m.Sds)).First().Sinif : "";
        int bks = m.Onem >= 1.45 ? 1 : m.Onem >= 1.15 ? 2 : 3;
        string dts = (sds < 0.33 ? "4" : sds < 0.5 ? "3" : sds < 0.75 ? "2" : "1") + (bks == 1 ? "a" : "");
        double hn = hTepe - m.Hbodrum;
        double[] sinir = dts.StartsWith("3") || dts.StartsWith("4") ? new[] { 91, 70, 56, 42, 28, 17.5, 10.5 } : new[] { 70, 56, 42, 28, 17.5, 10.5, 7 };
        int bys = Array.FindIndex(sinir, s => hn > s); bys = bys < 0 ? 8 : bys + 1;
        double ko = p.St4D(7, 2), qt = p.St4D(7, 3);
        var rad = Radye(p);
        bool surekliYok = BosBolum(p, "/Continuous foundations/"), tekilYok = BosBolum(p, "/Single footings/");
        var mass = p.Lines(".MASS");
        double insaat = mass == null ? double.NaN : mass.Where(l => l.Trim().Length > 0).Sum(l => Proj.D(Proj.F(l)[0]));
        // temel üst kotu: ST4 10. satır 1. alan; alt = üst − ilk radye plağı kalınlığı (cm)
        double kotUst = p.St4D(10, 1), kotAlt = kotUst - RadyeKalinlik(p) / 100.0;

        var o = new List<string>
        {
            $"TEMELLERE KOLONLARDAN GELEN, SÜNEK TASARIM DEPREM TESİRLERİ (Dust={F(m.Dfazla, "0.0#")}) TBDY2018 4.10.3",
            Satir("YAPI KAT ADEDİ", katAdedi.ToString(ci)),
            Satir("YAPI YÜKSEKLİĞİ", F(hTepe, "0.##") + " m"),
            Satir("DEPREM YER HAREKETİ DÜZEYİ", "DD2   50 yılda aşılma olasılığı %10"),
            Satir("ZEMİN SINIFI", zemin),
            Satir("ZEMİN TAŞIMA GÜCÜ TASARIM GERİLMESİ", F(qt, "0.0") + " (t/m²)"),
            Satir("ZEMİN YATAK KATSAYISI", F(ko, "0.0") + " (t/m³)"),
            Satir("BİNA KOORDİNATI               (ENLEM/BOYLAM)", $"{F(m.Enlem, "0.#####")}° / {F(m.Boylam, "0.#####")}°"),
            Satir("YEREL SPECTRAL İVME KATSAYISI          Ss/Sı", $"{F(m.Ss, "0.000")} / {F(m.S1, "0.000")}"),
            Satir("TASARIM SPECTRAL İVME KATSAYISI       Sds/Sdı", $"{F(m.Sds, "0.000")} / {F(m.Sd1, "0.000")}   DD2"),
            Satir("YAPI DAVRANIŞ KATSAYISI                  R", F(m.R, "0.00")),
            Satir("SİSTEM DAYANIM FAZLALIĞI KATSAYISI       D", F(m.Dfazla, "0.#").Replace('.', ',')),
            Satir("DEPREM TASARIM SINIFI                   DTS", dts),
            Satir("BİNA YÜKSEKLİK SINIFI                   BYS", $"{bys}   Hn={F(hn, "0.##")}m"),
            Satir("BİNA KULLANIM  SINIFI                   BKS", $"{bks}   I = {F(m.Onem, "0.0")}"),
            Satir("DÜŞEY DEPREM YÜKÜ", ""),
            Satir("SÜREKLİ TEMEL TABAN ALANI", surekliYok ? "0.0 m²" : ""),
            Satir("TEKİL TEMEL TABAN ALANI", tekilYok ? "0.0 m²" : ""),
            Satir("RADYE TEMEL TABAN ALANI", F(rad.alan, "0.0##") + " m²"),
            Satir("TOPLAM TEMEL TABAN ALANI", ""),
            Satir("TOPLAM BİNA OTURUM ALANI", surekliYok && tekilYok ? F(rad.alan, "0.0##") + " m²" : ""),
            Satir("TOPLAM İNŞAAT ALANI", double.IsNaN(insaat) ? "" : Fmt.N(insaat, 3).TrimEnd('0').TrimEnd('.') + " m²"),
            Satir("TEMEL BOYUTLARI X/Y", rad.alan > 0 ? $"{F(rad.x2 - rad.x1, "0.##")}/{F(rad.y2 - rad.y1, "0.##")} m" : ""),
            Satir("TEMEL KOTLARI ALT/UST", rad.alan > 0 ? $"{F(kotAlt, "0.0#")}/{F(kotUst, "0.0#")} m" : ""),
            "Kolonlarda; Me'=0.6×Dust×Me, Perdelerde;  Me'=Dust×Me Ë Mp düzeltmesi yapılarak kombinasyon düzenlenmiştir.",
            "Yük bileşenleri :",
            "1- G + Q,   2- 1.4 G + 1.6 Q",
            $"3- {(g3 == 1.2 ? "1.2 G" : "G")} + Q + 0.2 S + Edx + 0.3 Edy + 0.3 Edz",
            "4- 0.9 G + Edx + 0.3 Edy - 0.3 Edz",
        };
        int[] w = { 10, 12, 10, 10, 10, 10, 10 };
        o.Add(Cizgi('Ú', 'Â', '¿', w));
        o.Add("¡   Kolon  ¡Yük bileşeni¡    Fx    ¡    Fy    ¡    Fz    ¡    Mx    ¡    My    ¡   (kN,m)");
        o.Add(Cizgi('Ã', 'Å', '´', w));
        int[] of = { 466, 554, 26, 202, 378 };   // COL alt uç: Tx, Ty, Nz, AltMx, AltMy
        string[] bil = { "     1      ", "     2      ", "     3 X    ", "     3 Y    ", "     4 X    ", "     4 Y    " };
        for (int ki = 0; ki < kolonlar.Count; ki++)
        {
            var k = kolonlar[ki];
            var r = v.Col[k.Sira];
            var vals = new double[6, 5];
            for (int c = 0; c < 5; c++)
            {
                var d = r.Dizi(of[c]); double G = d[0], Q = d[1];
                vals[0, c] = G + Q; vals[1, c] = 1.4 * G + 1.6 * Q;
                for (int y = 0; y < 2; y++)
                {
                    double e1 = d[8 + 2 * y], e2 = d[10 - 2 * y];
                    if (c == 2) { vals[2 + y, c] = g3 * G + Q + ez * G + Math.Abs(e1); vals[4 + y, c] = 0.9 * (G - ez * G) - Math.Abs(e1); }
                    else if (c < 2) { double E = e1 + 0.3 * e2; vals[2 + y, c] = g3 * G + Q + ez * G + E; vals[4 + y, c] = 0.9 * (G + ez * G) + E; }
                    else { double E = Math.Abs(e1) + 0.3 * Math.Abs(e2); vals[2 + y, c] = G + Q + ez * Math.Abs(G) + E; vals[4 + y, c] = 0.9 * (G + ez * Math.Abs(G)) + E; }
                }
            }
            var xy = KolonMerkez(p, k);
            string[] sol = { k.Ad, k.Poligon ? "Poligon" : $"{k.Bx}×{k.By}", double.IsNaN(xy.x) ? "" : $"Xg={ND(xy.x, 2),7}", double.IsNaN(xy.y) ? "" : $"Yg={ND(xy.y, 2),7}", "", "" };
            for (int j = 0; j < 6; j++)
                o.Add(V + sol[j].PadRight(10) + V + bil[j] + string.Concat(Enumerable.Range(0, 5).Select(c => V + N(vals[j, c] * 10, 2).PadLeft(10))) + V);
            o.Add(ki < kolonlar.Count - 1 ? Cizgi('Ã', 'Å', '´', w) : Cizgi('À', 'Á', 'Ù', w));
        }
        return o;
    }

    // ------------------------------------------------------------------ B3
    sealed class KGiris { public List<St4Kiris> P = new(); public St4Kiris F => P[0]; public St4Kiris L => P[^1]; }

    /// <summary>Sürekli kiriş zinciri sırası: kat kat, kayıt sırasıyla; zincir başı aynı aksta önceli olmayan giriş, sonra I = önceki J.</summary>
    static List<KGiris> KirisZinciri(StaVeri v)
    {
        var res = new List<KGiris>();
        var recs = v.Model.Kirisler.Where(k => v.KirisKaydi.ContainsKey(k)).OrderBy(k => v.KirisKaydi[k]).ToList();
        foreach (var kat in recs.GroupBy(k => k.Kat))
        {
            var ent = new List<KGiris>();
            foreach (var k in kat)
                if (ent.Count > 0 && ent[^1].F.Ad == k.Ad) ent[^1].P.Add(k); else ent.Add(new KGiris { P = { k } });
            var yer = new HashSet<KGiris>();
            KGiris Sonraki(KGiris e) => e.L.J == 0 ? null : ent.FirstOrDefault(x => !yer.Contains(x) && x.F.Aks == e.F.Aks && x.F.I == e.L.J);
            bool Onceli(KGiris e) => e.F.I != 0 && ent.Any(x => x != e && x.F.Aks == e.F.Aks && x.L.J == e.F.I);
            foreach (var e in ent)
            {
                if (yer.Contains(e) || Onceli(e)) continue;
                for (var c = e; c != null; c = Sonraki(c)) { yer.Add(c); res.Add(c); }
            }
            foreach (var e in ent) if (!yer.Contains(e)) { yer.Add(e); res.Add(e); }
        }
        return res;
    }

    static double Alan(int n, int cap) => n * 3.1422 * cap * cap / 400.0;   // STA4 montaj alanı (π≈3.1422; GPR ρ`` ile birebir)

    /// <summary>KİRİŞLERDE DENGELİ DONATI ORANI ve SÜNEK TASARIM KONTROLU (KMX As, d = H − OPS paspayı). N_ULK_02 ~98 %.</summary>
    public static List<string> DengeliDonati(Proj p)
    {
        var v = Veri(p);
        if (v == null || !v.Kmx.Var) return null;
        var sira = KirisZinciri(v);
        if (sira.Count == 0) return null;
        double fcd = v.Kmx[0].F(40), fck = fcd * 1.5;
        var mat = p.Sec("/Materials/") >= 0 ? Proj.F(p.St4[p.Sec("/Materials/") + 1]).Select(Proj.D).ToArray() : new double[] { 250, 4200 };
        double fyk = mat.Length > 1 && mat[1] > 0 ? mat[1] : 4200, fyd = fyk / 1.15;
        double k1 = fck <= 250 ? 0.85 : Math.Max(0.70, 0.85 - 0.006 * (fck / 10 - 25));
        double rb = 0.85 * k1 * fcd / fyd * 600 / (600 + fyd / 10), rmax = 0.6 * rb;
        double pas = double.IsNaN(p.OpsD(10, 1)) ? 5.5 : p.OpsD(10, 1);
        string R7(double x) => N(x, 7);

        var o = new List<string>
        {
            "KİRİŞLERDE DENGELİ DONATI OPSIYONU   : 0.6 × ïb  deprem ve yeniden dağılım kontrolu",
            $"0.6 × ïb  = {rmax.ToString("0.000000", ci)}  <   0.85 × ïb = {(0.85 * rb).ToString("0.000000", ci)}  dengeli donatı deprem koşulu   TS500-7.4",
            "ï-ï` < 0.4 ïb  ÄÄÄ>  %15,   ï-ï` < 0.6 ïb  ÄÄÄ>  %10    yeniden dağılım koşulu   TS500-6.14",
            "TBDY2018-7.4.2-Yüksek sünek tasarım koşulu:Sds < 0.5 ÄÄÄ> min(As`/As)È0.3,   Sds È 0.5 ÄÄÄ> min(As`/As)È0.5",
            "TBDY2018-7.4.2.3  mesnetlerde As`/As => 0.30 ,     TBDY2018-7.4.3.1 açıklıkta As`/As => 0.25",
            "TBDY2018 süneklilik kontrolu deprem momenti oluşan kirişlerde yapılmaktadır.",
            "TS500    dengeli donatı kontrolu kirişin moment oluşan noktalarında yapılmaktadır.",
            "KİRİŞLERDE DENGELİ DONATI ORANI ve SÜNEK TASARIM KONTROLU",
        };
        int[] w = { 12, 6, 6, 6, 6, 9, 9, 9, 22, 12 };
        o.Add(Cizgi('Ú', 'Â', '¿', w));
        o.Add("¡Kiriş       ¡  Bw  ¡   H  ¡  As  ¡  As` ¡    ï    ¡    ï`   ¡    ïb   ¡ ï-ï` < ïmax=0.6 ïb   ¡   As`/As   ¡");
        o.Add(Cizgi('Ã', 'Å', '´', w));
        string Tire = V + "  -   " + V + "  -   " + V + "    -    " + V + "    -    " + V + "    -    " + V + "           -          " + V + "      -     " + V;
        for (int i = 0; i < sira.Count; i++)
        {
            var e = sira[i];
            var m1 = v.Kmx[v.KirisKaydi[e.F]]; var m2 = v.Kmx[v.KirisKaydi[e.L]];
            double bw = e.F.B, h = e.F.H, d = h - pas;
            double asMon = Alan(m1.S(108), m1.S(110));
            double maxU = Math.Max(m1.F(60), m1.F(68)); if (e.P.Count > 1) maxU = Math.Max(maxU, Math.Max(m2.F(60), m2.F(68)));
            bool solYok = e.F.I == 0, sagYok = e.L.J == 0;
            string[] etiket = { e.F.Ad.PadRight(9) + "sol", "         ac.", (solYok ? "ÚÄÄÄÄÄ" : "ÃÄÄÄÄÄ") + (sagYok ? "¿" : "´") + "  sag" };   // köşe: sol ucu düğümsüz Ú, sağ ucu düğümsüz ¿
            for (int j = 0; j < 3; j++)
            {
                double As, Asp; bool yok; string oran;
                if (j == 0) { As = m1.F(60); Asp = m1.F(64); yok = solYok; oran = yok ? "-" : $"{ND(Asp / As, 2)}>0.30 ò"; }
                else if (j == 1)
                {
                    As = m1.F(76); Asp = asMon; yok = false;
                    double rr = Math.Max(maxU, asMon) / (m1.S(112) * 3.14 * m1.S(114) * m1.S(114) / 400.0);
                    oran = $"{ND(rr, 2)}>0.25 ò";
                }
                else
                {
                    As = m2.F(68); Asp = m2.F(72); yok = sagYok;
                    oran = yok || m1.F(68) == 0 || e.P.Count > 1 ? "-" : $"{ND(Asp / As, 2)}>0.30 ò";
                }
                string bas = V + etiket[j] + V + bw.ToString(ci).PadLeft(5) + " " + V + h.ToString(ci).PadLeft(5) + " ";
                if (yok) { o.Add(bas + Tire); continue; }
                float rho = (float)As / ((float)bw * (float)d), rhop = (float)Asp / ((float)bw * (float)d);
                string fark = $"{R7(Math.Abs(rho - rhop))}<{R7(rmax)} ò";
                string or = oran == "-" ? "      -     " : oran.PadRight(12);
                o.Add(bas + V + N(As, 2).PadLeft(6) + V + N(Asp, 2).PadLeft(6) + V + R7(rho) + V + R7(rhop) + V + R7(rb) + V + fark.PadRight(22) + V + or + V);
            }
            o.Add(i < sira.Count - 1 ? Cizgi('Ã', 'Å', '´', w) : Cizgi('À', 'Á', 'Ù', w));
        }
        return o;
    }

    // ------------------------------------------------------------------ B4
    /// <summary>İMAR YÖNETMELİĞİ, KAPALI ÇIKMALI YAPIDA KUŞATILMIŞ KOLON KONTROLU — sabit not ve boş tablo (satırlı örnek yok).</summary>
    public static List<string> ImarKusatilmis(Proj p)
    {
        if (p.St4 == null) return null;
        int[] w = { 8, 14, 12, 5, 5, 17, 30 };
        return new List<string>
        {
            "11.3.2025 tarihli Planlı Alanlar Yönetmeliğindeki değişiklikler nedeniyle kuşatılmışlık kontrolu yapılmasına",
            "gerek yoktur. A12, A13 ve A32 tipi taşıyıcı sistemlerin kullanıldığı binalar hariç,",
            "çerçeve kirişleriyle bağlanması zorunludur.",
            " İMAR YÖNETMELİĞİ, KAPALI ÇIKMALI YAPIDA KUŞATILMIŞ KOLON KONTROLU",
            Cizgi('Ú', 'Â', '¿', w),
            "¡Kolon   ¡Kiriş konumu  ¡   Kiriş    ¡  bw ¡  bc ¡  bw / bc        ¡           AÇIKLAMA           ¡",
            Cizgi('Ã', 'Å', '´', w),
            Cizgi('À', 'Á', 'Ù', w),
        };
    }

    // ------------------------------------------------------------------ B5
    /// <summary>STA4-CAD YAPI YORUMU: .YORUM metin dosyası aynen (analiz anında yazılır; 8 projede GPR ile birebir).</summary>
    public static List<string> YapiYorumu(Proj p)
    {
        var y = p.Lines(".YORUM");
        if (y == null || y.Length == 0) return null;
        var o = new List<string> { "STA4-CAD YAPI YORUMU" };
        o.AddRange(y);
        while (o.Count > 1 && o[^1].Trim().Length == 0) o.RemoveAt(o.Count - 1);
        return o;
    }

    /// <summary>
    /// Rapordaki "Yapı yorumu": .YORUM'dan yalnız A (statik), D (ekonomi), E (temel ve zemin) maddeleri ve kolon/perde
    /// alan oranı tablosu. B (süneklik) ve C (stabilite) kullanıcı isteğiyle dışarıda.
    /// </summary>
    public static List<string> YapiYorumuOzet(Proj p)
    {
        var y = p.Lines(".YORUM");
        if (y == null || y.Length == 0) return null;
        var o = new List<string>();
        bool al = false;
        foreach (var satir in y)
        {
            string t = satir.Trim();
            // madde başı "A-STATİK YORUM", "D-EKONOMİ YORUMU" …; tablo başı "KOLON VE PERDE ALAN ORANI"
            var madde = System.Text.RegularExpressions.Regex.Match(t, @"^([A-Z])-\S");
            if (madde.Success)
            {
                al = "ADE".Contains(madde.Groups[1].Value);
                // rapordaki sıra harfleri: A statik, B ekonomi (STA4 D), C temel ve zemin (STA4 E)
                if (al) { o.Add(madde.Groups[1].Value switch { "D" => "B", "E" => "C", _ => "A" } + t.Substring(1)); continue; }
            }
            else if (t.StartsWith("KOLON VE PERDE ALAN ORANI")) { al = true; o.Add("KOLON VE PERDE KESİT ALANI ORANI  (M²)"); continue; }
            if (!al) continue;
            if (t.Length == 0 && (o.Count == 0 || o[^1].Trim().Length == 0)) continue;
            o.Add(satir);
        }
        while (o.Count > 0 && o[^1].Trim().Length == 0) o.RemoveAt(o.Count - 1);
        return o.Count == 0 ? null : o;
    }

    // ------------------------------------------------------------------ A11
    /// <summary>
    /// KOLON VE PERDE ALAN ORANI (metraj sayfasındaki 4 sütunlu biçim). Ak = .MASS satır 1. alan; perde = uzun ≥ 6 × kısa;
    /// % = A/Ak·100 (3 hane). N_ULK_02 12/12.
    /// </summary>
    public static List<string> KolonPerdeAlanOrani(Proj p)
    {
        var mass = p.Lines(".MASS");
        var st4 = p.FilePath(".ST4");
        if (mass == null || st4 == null) return null;
        var m = St4Model.Oku(st4);
        var ak = mass.Where(l => l.Trim().Length > 0).Select(l => Proj.D(Proj.F(l)[0])).ToList();
        if (ak.Count == 0) return null;
        int ilkKat = m.Kolonlar.Count == 0 ? 1 : m.Kolonlar.Min(k => k.Kat);
        int[] w = { 17, 15, 21, 23 };
        var o = new List<string> { "KOLON VE PERDE ALAN ORANI  (m²)", Cizgi('Ú', 'Â', '¿', w),
            "¡ Kat  (Diyafram) ¡  Kat alanı Ak ¡ Kolon Alanı, %Ac/Ak ¡ Perde alanı,  %Ap/Ak  ¡", Cizgi('Ã', 'Å', '´', w) };
        double tAk = 0, tAc = 0, tAp = 0;
        string Sat(string kat, double a, double ac, double ap) =>
            V + kat.PadRight(17) + V + ("  " + N(a, 2)).PadRight(15) + V + (ND(ac, 2).PadLeft(8) + "      %" + ND(a > 0 ? ac / a * 100 : 0, 3)).PadRight(21)
            + V + (ND(ap, 2).PadLeft(8) + "      %" + ND(a > 0 ? ap / a * 100 : 0, 3)).PadRight(23) + V;
        for (int i = 0; i < ak.Count; i++)
        {
            int kat = ilkKat + i;
            var ks = m.Kolonlar.Where(k => k.Kat == kat).ToList();
            double ac = ks.Where(k => !(!k.Poligon && Perde(k.Bx, k.By))).Sum(k => k.Ac) / 1e4;
            double ap = ks.Where(k => !k.Poligon && Perde(k.Bx, k.By)).Sum(k => k.Ac) / 1e4;
            o.Add(Sat($"   {kat}     ({kat})", ak[i], ac, ap));
            tAk += ak[i]; tAc += ac; tAp += ap;
        }
        o.Add(Cizgi('Ã', 'Å', '´', w));
        o.Add(Sat("   Toplam", tAk, tAc, tAp));
        o.Add(Cizgi('À', 'Á', 'Ù', w));
        return o;
    }
}
