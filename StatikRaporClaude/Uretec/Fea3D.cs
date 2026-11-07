using System.Globalization;
using System.Text;
using StatikRaporClaude.Veri;

namespace StatikRaporClaude.Uretec;

/// <summary>
/// FEA3D bölümleri (STA4 "YAPININ 3D SONLU ELEMANLARLA ANALİZİ"): .Fea3D + .FeaMod + .FeaVec + .ST4'ten GPR satırları.
/// Fea3D yerleşimi (little-endian): başlık int[0..5] = mesh, nokta, çubuk, link, eleman sonuç kaydı, kombinasyon (60 B);
/// mesh 40 B; nokta 28 B (x, y, z, int[3] ≠ 0 → constraint); çubuk 40 B; link 24 B; nokta sonucu 1512 B (18 blok × 21 float,
/// kombinasyon c → +4c); eleman sonucu 972 B (48 B başlık + 11 blok × 84 B). Doğrulama (scratchpad ara_metraj/C):
/// N_ULK_02, H_DVR_te_03, sd_01..03 GPR'leriyle tüm değer hücreleri birebir. Koordinat tablosunda STA4 X/Y/Z'yi kaydırarak
/// basıyor (X = x(n+c+1), Y = Z = x(n)); burada gerçek koordinat basılır.
/// </summary>
public static class Fea3D
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    sealed class Dosya
    {
        public byte[] B;
        public int Nm, Nn, Nc, Nl, Ne, Nk;
        public long N1, R1, E0;
        public Dosya(byte[] b)
        {
            B = b;
            Nm = I(0); Nn = I(4); Nc = I(8); Nl = I(12); Ne = I(16); Nk = I(20);
            long M = 60 + 40L * Nm;
            N1 = M + 32;
            long L0 = M + 4 + 28L * (Nn + 1) + 40L * (Nc + 1);
            R1 = L0 + 24L * (Nl + 1) + 1512 + 4;
            E0 = R1 + 1512L * Nn + 948;
        }
        public int I(long o) => BitConverter.ToInt32(B, (int)o);
        public float F(long o) => BitConverter.ToSingle(B, (int)o);
        public float X(int n, int k) => F(N1 + 28L * (n - 1) + 4 * k);
        public bool Constraint(int n) => I(N1 + 28L * (n - 1) + 12) != 0;
        /// <summary>Nokta sonucu: blok 0..5 ux..rz, 6 Q12, 7 N11, 8 N22, 9 M11, 10 M22, 11 M12; c kombinasyon (0 tabanlı).</summary>
        public float Nokta(int n, int blok, int c) => F(R1 + 1512L * (n - 1) + 84 * blok + 4 * c);
        public int H(int r, int q) => I(E0 + 972L * r + 4 * q);
        public float Eleman(int r, int blok, int c) => F(E0 + 972L * r + 48 + 84 * blok + 4 * c);
    }

    static Dosya Ac(Proj p) { var b = p.Bytes(".Fea3D"); return b == null || b.Length < 60 ? null : new Dosya(b); }

    /// <summary>STA4: float32 → 7 anlamlı haneye yarım-yukarı → istenen ondalığa yarım-yukarı; "-0" yok.</summary>
    public static string Sayi(float f, int dec)
    {
        if (f == 0 || float.IsNaN(f)) return 0m.ToString("F" + dec, ci);
        decimal d = decimal.Parse(((double)f).ToString("G17", ci), NumberStyles.Float, ci);
        int e = (int)Math.Floor(Math.Log10((double)Math.Abs(d)));
        int sig = 6 - e;
        if (sig >= 0 && sig <= 28) d = Math.Round(d, sig, MidpointRounding.AwayFromZero);
        d = Math.Round(d, dec, MidpointRounding.AwayFromZero);
        return d == 0 ? 0m.ToString("F" + dec, ci) : d.ToString("F" + dec, ci);
    }

    /// <summary>Sütuna sığmayan değer sağdan kesilir (STA4 yuvarlamaz).</summary>
    static string Sigdir(float f, int dec, int en)
    {
        var s = Sayi(f, dec);
        if (s.Length > en) s = s.Substring(0, en).TrimEnd('.');
        return s.PadLeft(en);
    }

    static string Cizgi(char sol, char ara, char sag, params int[] en) =>
        sol + string.Join(ara.ToString(), en.Select(w => new string('Ä', w))) + sag;

    static readonly string[] Kombinasyon =
    {
        "G+G+G+G", "Q+Q+Q+Q", "o+Q+o+Q", "Q+o+Q+o", "Q+Q+o+Q", "o+Q+Q+o", "o+o+Q+Q", "Zemin",
        "+X-Deprem", "-X-Deprem", "+Y-Deprem", "-Y-Deprem", "X-Rüzgar", "Y-Rüzgar", "Isi etkisi"
    };

    // ---------------- genel bilgiler ----------------

    public static List<string> Genel(Proj p)
    {
        var d = Ac(p); if (d == null) return null;
        int cons = Enumerable.Range(1, d.Nn).Count(d.Constraint);
        var fm = p.Bytes(".FeaMod");
        int nmod = fm != null && fm.Length >= 8 ? BitConverter.ToInt32(fm, 4) : 0;
        string Z(long v) => v == 0 ? "" : v.ToString();
        string L(string ad, string v) => " " + ad.PadRight(38) + ":" + v.PadLeft(9);
        return new List<string>
        {
            " YAPININ 3D SONLU ELEMANLARLA ANALİZİ",
            L("Yapı nokta sayısı", Z(d.Nn)),
            L("Yapı constraint sayısı", Z(cons)),
            L("Yapı mesh sayısı", Z(d.Nm)),
            L("Yapı çubuk sonlu eleman sayısı", Z(d.Nc)),
            L("Yapı link eleman sayısı", Z(d.Nl)),
            L("Yapı yük kombinasyon sayısı", Z(d.Nk)),
            L("Yapı denklem sayısı", Z(6L * d.Nn * d.Nk)),
            L("Yapı mod sayısı", Z(nmod)),
            L("Beton malzeme poisson oranı", ""),
            L("Plak Birim Mesh Genisligi           m", "1.00"),
        };
    }

    // ---------------- koordinat ----------------

    public static List<string> Koordinat(Proj p)
    {
        var d = Ac(p); if (d == null) return null;
        int[] en = { 7, 8, 8, 8, 7, 8, 8, 8, 7, 8, 8, 8 };
        var o = new List<string>
        {
            " KOORDİNAT BİLGİLERİ",
            Cizgi('Ú', 'Â', '¿', en),
            "¡" + string.Concat(Enumerable.Repeat(" Nokta ¡    X   ¡    Y   ¡    Z   ¡", 3)),
            Cizgi('Ã', 'Å', '´', en),
        };
        var sb = new StringBuilder("¡");
        int s = 0;
        for (int n = 1; n <= d.Nn; n++)
        {
            sb.Append(n.ToString().PadLeft(7)).Append('¡');
            for (int k = 0; k < 3; k++) sb.Append(Sigdir(d.X(n, k), 3, 8)).Append('¡');
            if (++s == 3) { o.Add(sb.ToString()); sb.Clear().Append('¡'); s = 0; }
        }
        if (s > 0)
        {
            for (; s < 3; s++) sb.Append("       ¡        ¡        ¡        ¡");
            o.Add(sb.ToString());
        }
        o.Add(Cizgi('À', 'Á', 'Ù', en));
        return o;
    }

    // ---------------- mesh ----------------

    public static List<string> Mesh(Proj p)
    {
        var d = Ac(p); if (d == null) return null;
        int[] en = { 5, 4, 5, 5, 5, 5, 5, 4, 5, 5, 5, 5, 5, 4, 5, 5, 5, 5 };
        var o = new List<string>
        {
            "   MESH BİLGİLERİ",
            Cizgi('Ú', 'Â', '¿', en),
            "¡ Mesh¡  d ¡  I1 ¡  I2 ¡  I3 ¡  I4 ¡Mesh ¡  d ¡  I1 ¡  I2 ¡  I3 ¡  I4 ¡Mesh ¡  d ¡  I1 ¡  I2 ¡  I3 ¡  I4 ¡",
            "¡  no ¡ cm ¡     ¡     ¡     ¡     ¡  no ¡ cm ¡     ¡     ¡     ¡     ¡  no ¡ cm ¡     ¡     ¡     ¡     ¡",
            Cizgi('Ã', 'Å', '´', en),
        };
        string Z(int v, int w) => (v == 0 ? "" : v.ToString()).PadLeft(w);
        var sb = new StringBuilder("¡");
        int s = 0;
        for (int m = 1; m <= d.Nm; m++)
        {
            long r = 60 + 40L * (m - 1);
            int dd = (int)Math.Round(d.F(r + 32) * 100);
            sb.Append(m.ToString().PadLeft(5)).Append('¡').Append(Z(dd, 4)).Append('¡');
            for (int k = 2; k <= 5; k++) sb.Append(Z(d.I(r + 4 * k), 5)).Append('¡');
            if (++s == 3) { o.Add(sb.ToString()); sb.Clear().Append('¡'); s = 0; }
        }
        if (s > 0)
        {
            for (; s < 3; s++) sb.Append("     ¡    ¡     ¡     ¡     ¡     ¡");
            o.Add(sb.ToString());
        }
        o.Add(Cizgi('À', 'Á', 'Ù', en));
        return o;
    }

    // ---------------- mod vektörleri ----------------

    public static List<string> ModVektorleri(Proj p)
    {
        var d = Ac(p); var fm = p.Bytes(".FeaMod"); var fv = p.Bytes(".FeaVec");
        if (d == null || fm == null || fv == null) return null;
        int nmod = BitConverter.ToInt32(fm, 4);
        if (nmod <= 0) return null;
        double T(int i) => BitConverter.ToDouble(fm, 20 + 8 * i);
        double W(int i) => BitConverter.ToDouble(fm, 20 + 8 * (nmod + 1) + 8 * i);
        float Kutle(int i) => Math.Max(BitConverter.ToSingle(fm, fm.Length - 8 * nmod + 8 * i), BitConverter.ToSingle(fm, fm.Length - 8 * nmod + 8 * i + 4));
        int nd = fv.Length / (24 * nmod);
        string[] bilesen = { "ëx", "ëy", "ëz", "éx", "éy", "éz" };
        var o = new List<string>();
        for (int g0 = 0; g0 < nmod; g0 += 9)
        {
            int g1 = Math.Min(nmod, g0 + 9), k = g1 - g0;
            var en = new[] { 9 }.Concat(Enumerable.Repeat(10, k)).ToArray();
            string Satir(string ad, Func<int, string> f) => "¡" + ad + "¡" + string.Concat(Enumerable.Range(g0, k).Select(i => f(i) + "¡"));
            o.Add($" {g0 + 1}-{g1} MOD VEKTÖRLERİ");
            o.Add(Cizgi('Ú', 'Â', '¿', en));
            o.Add(Satir("  Mod no ", i => ("     " + (i + 1)).PadRight(10)));
            o.Add(Satir("    T    ", i => Sayi((float)T(i), 3).PadLeft(8) + "  "));
            o.Add(Satir("    ì    ", i => Sayi((float)W(i), 3).PadLeft(8) + "  "));
            o.Add(Satir(" äM / äm ", i => Sayi(Kutle(i), 3).PadLeft(8) + "  "));
            for (int n = 1; n <= nd; n++)
            {
                o.Add(Cizgi('Ã', 'Å', '´', en));
                for (int b = 0; b < 6; b++)
                    o.Add(Satir(n.ToString().PadLeft(6) + " " + bilesen[b],
                        i => Sigdir(BitConverter.ToSingle(fv, (n - 1) * 24 * nmod + b * 4 * nmod + i * 4), 7, 10)));
            }
            o.Add(Cizgi('À', 'Á', 'Ù', en));
        }
        return o;
    }

    // ---------------- nokta sonuçları ----------------

    static List<string> NoktaTablosu(Dosya d, string baslik, string[] ust, string[] alt, int[] bloklar, double olcek, int dec, int en)
    {
        var w = new[] { 18 }.Concat(Enumerable.Repeat(en, 6)).ToArray();
        var o = new List<string>
        {
            baslik,
            Cizgi('Ú', 'Â', '¿', w),
            "¡ Nokta/Kombinasyon¡" + string.Concat(ust.Select(s => s.PadRight(en) + "¡")),
            "¡        no        ¡" + string.Concat(alt.Select(s => s.PadRight(en) + "¡")),
        };
        int nk = Math.Min(15, d.Nk);
        for (int n = 1; n <= d.Nn; n++)
        {
            o.Add(Cizgi('Ã', 'Å', '´', w));
            for (int c = 0; c < nk; c++)
            {
                var sb = new StringBuilder("¡").Append(n.ToString().PadLeft(6)).Append("  ").Append(Kombinasyon[c].PadRight(10)).Append('¡');
                foreach (int b in bloklar) sb.Append(Sigdir((float)(d.Nokta(n, b, c) * olcek), dec, en)).Append('¡');
                o.Add(sb.ToString());
            }
        }
        o.Add(Cizgi('À', 'Á', 'Ù', w));
        return o;
    }

    public static List<string> NoktaStatik(Proj p)
    {
        var d = Ac(p); if (d == null) return null;
        return NoktaTablosu(d, "  NOKTALARIN STATİK SONUÇLARI",
            new[] { "    M11", "    M22", "    M12", "    Q12", "    N11", "    N22" },
            new[] { "   tm/m", "   tm/m", "   tm/m", "    t/m", "    t/m", "    t/m" },
            new[] { 9, 10, 11, 6, 7, 8 }, 1, 3, 10);
    }

    public static List<string> NoktaDeplasman(Proj p)
    {
        var d = Ac(p); if (d == null) return null;
        return NoktaTablosu(d, "  NOKTA DEPLASMANLARI",
            new[] { "     ëx", "     ëy", "     ëz", "     éx", "     éy", "     éz" },
            // STA4 başlığı "radyan" yazar ama değerler ×1000'dir (mrad)
            new[] { "     mm", "     mm", "     mm", "     mrad", "     mrad", "     mrad" },
            new[] { 0, 1, 2, 3, 4, 5 }, 1000, 8, 12);
    }

    // ---------------- çubuk sonuçları ----------------

    /// <summary>tip 1 kiriş, 5 bağ kirişi, 2 kolon/perde. Sütunlar N, Mxi, Myi, Mbi, Txi, Tyi, Mxj, Myj, Mbj, Txj, Tyj.</summary>
    public static List<string> Cubuk(Proj p, int tip)
    {
        var d = Ac(p); if (d == null) return null;
        var kayit = Enumerable.Range(0, d.Ne).Where(r => (d.H(r, 5) >> 16) == tip).ToList();
        if (kayit.Count == 0) return null;
        var m = St4Model.Oku(p.FilePath(".ST4"));
        // ST4 /Beams Data/ 4. alan / 100 (G), /Tie beams/ ad + B, D
        var gK = new List<double>(); var bag = new List<(string Ad, int B, int D)>();
        int i0 = p.Sec("/Beams Data/");
        if (i0 >= 0)
            for (int i = i0 + 1; i + 2 < p.St4.Length && !p.St4[i].StartsWith("/"); i += 3)
            { var f = p.St4[i].Split(','); if (f[0] == "0") break; gK.Add(f.Length > 3 ? Proj.D(f[3]) / 100 : 0); }
        i0 = p.Sec("/Tie beams/");
        if (i0 >= 0)
            for (int i = i0 + 1; i + 1 < p.St4.Length && !p.St4[i].StartsWith("/") && !p.St4[i + 1].StartsWith("/"); i += 2)
            { var f = p.St4[i + 1].Split(','); bag.Add((p.St4[i].Trim(), (int)Proj.D(f[0]), f.Length > 1 ? (int)Proj.D(f[1]) : 0)); }

        int[] blok = { 10, 0, 2, 8, 4, 6, 1, 3, 9, 5, 7 };
        int[] en = { 8, 8, 8, 7, 7, 7, 8, 8, 7, 7, 7 };
        var w = new[] { 9, 4 }.Concat(en).ToArray();
        string baslik = tip == 1 ? "   KİRİŞ STATİK SONUÇLARI   (t,tm)" : tip == 5 ? "   BAĞ KIRIS STATİK SONUÇLARI   (t,tm)" : "   KOLON STATİK SONUÇLARI   (t,tm)";
        var o = new List<string>
        {
            baslik,
            Cizgi('Ú', 'Â', '¿', w),
            "¡" + (tip == 2 ? "  Kolon  " : "  Kiriş  ") + "¡Kom.¡    N   ¡   Mxi  ¡   Myi  ¡  Mbi  ¡  Txi  ¡  Tyi  ¡   Mxj  ¡   Myj  ¡  Mbj  ¡  Txj  ¡  Tyj  ¡",
        };
        string W7(string on, string v) => on + v.PadLeft(7);
        int nk = Math.Min(15, d.Nk);
        foreach (int r in kayit)
        {
            int no = d.H(r, 6);
            var et = Enumerable.Repeat("", 15).ToArray();
            string I = d.H(r, 8) == 0 ? "" : d.H(r, 8).ToString(), J = d.H(r, 9) == 0 ? "" : d.H(r, 9).ToString();
            et[3] = W7("I=", I); et[4] = W7("J=", J);
            if (tip == 1)
            {
                bool var = no >= 1 && no <= m.Kirisler.Count;
                et[0] = " " + (var ? m.Kirisler[no - 1].Ad : "K000");
                et[7] = var ? W7("B=", m.Kirisler[no - 1].B.ToString()) : "B=";
                et[8] = var ? W7("D=", m.Kirisler[no - 1].H.ToString()) : "D=";
                et[10] = W7("G=", (var && no <= gK.Count ? gK[no - 1] : 0).ToString("0.000", ci));
            }
            else if (tip == 5)
            {
                bool var = no >= 1 && no <= bag.Count;
                et[0] = " " + (var ? bag[no - 1].Ad : "");
                et[7] = var ? W7("B=", bag[no - 1].B.ToString()) : "B=";
                et[8] = var ? W7("D=", bag[no - 1].D.ToString()) : "D=";
            }
            else
            {
                bool var = no >= 1 && no <= m.Kolonlar.Count;
                et[0] = " " + (var ? m.Kolonlar[no - 1].Ad : "S000");
                et[7] = var ? W7("B=", m.Kolonlar[no - 1].Bx.ToString()) : "B=";
                et[8] = var ? W7("D=", m.Kolonlar[no - 1].By.ToString()) : "D=";
            }
            o.Add(Cizgi('Ã', 'Å', '´', w));
            for (int c = 0; c < nk; c++)
            {
                var sb = new StringBuilder("¡").Append(et[c].PadRight(9)).Append('¡').Append((c + 1).ToString().PadLeft(4)).Append('¡');
                for (int j = 0; j < 11; j++) sb.Append(Sigdir(d.Eleman(r, blok[j], c), 2, en[j])).Append('¡');
                o.Add(sb.ToString());
            }
        }
        o.Add(Cizgi('À', 'Á', 'Ù', w));
        return o;
    }
}
