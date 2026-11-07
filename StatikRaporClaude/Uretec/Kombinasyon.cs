using System.Globalization;

namespace StatikRaporClaude.Uretec;
public static class Kombinasyon
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;
    const char V = '\u00A1';
    static string N(double v, int d) => Fmt.ND(v, d);

    public static string CodYol(Proj p) => p.FilePath(".COD") ?? @"C:\Sta4V14\STA4.COD";
    public static List<double[]> Cod(Proj p)
    {
        var l = PaylasimliDosya.Lines(CodYol(p), GprKod.Enc);
        int n = (int)Proj.D(l[0]);
        return l.Skip(1).Take(n).Select(x => x.Split(',').Select(Proj.D).ToArray()).ToList();
    }
    public static string CodAd(Proj p) { var l = PaylasimliDosya.Lines(CodYol(p), GprKod.Enc); int n = (int)Proj.D(l[0]); return l[n + 1].Trim(); }

    static IEnumerable<string> Tablo(List<double[]> c, bool zemin, bool isi)
    {
        string s9 = new string('Ä', 9);
        yield return "Ú" + string.Join("Â", Enumerable.Repeat(s9, 6)) + "¿";
        yield return "¡ Ölü yük ¡Hareketli¡" + (zemin && isi ? "   Isı   " : "  Zemin  ") + "¡ Deprem  ¡ Rüzgar  ¡   Isı   ¡";
        yield return "¡   Cg    ¡ yük Cq  ¡" + (isi ? "   T c°  " : "   Cs    ") + "¡  ± Ce   ¡  ± Cw   ¡   Ct    ¡";
        yield return "Ã" + string.Join("Å", Enumerable.Repeat(s9, 6)) + "´";
        foreach (var r in c)
            yield return V + string.Join(V.ToString(), r.Take(6).Select(x => zemin ? N(x, 2).PadLeft(8) + " " : "   " + N(x, 2).PadRight(6))) + V;
        yield return "À" + string.Join("Á", Enumerable.Repeat(s9, 6)) + "Ù";
    }

    public static List<string> Uret(Proj p)
    {
        var o = new List<string>();
        var c = Cod(p);
        o.Add(" BETONARME HESAP YÜK KOMBİNASYONU");
        bool isi = Sayfa7.SD(p, 10, 2) != 0;
        o.AddRange(Tablo(c, false, isi));
        if (Opsiyon.OD(p, 7, 1) == 5) o.Add("TBDY2018  Düşey Deprem Kombinasyonu : G + Q + 0.2 S + Edh + 0.3 Edz,  0.9 G + H + Edh - 0.3 Edz");
        o.Add("CODE:" + CodAd(p));
        o.Add(" ZEMİN GERİLMESİ YÜK KOMBİNASYONU    qo < qt");
        o.Add(" ZEMİN GERİLMESİ OPSİYONU:ZEMİN TAŞIMA GÜCÜ TASARIM GERİLMESİ");
        o.AddRange(Tablo(c, true, isi));
        // hareketli yük azaltma
        string s5 = new string('Ä', 5), s11 = new string('Ä', 11);
        if (Sayfa7.SD(p, 7, 6) != 0) {
        o.Add(" ZEMİN GERİLMESİ HAREKETLİ YÜK AZALTMA DEĞERLERİ");
        o.Add("Ú" + s11 + string.Concat(Enumerable.Repeat("Â" + s5, 10)) + "¿");
        o.Add(V + "Kat        " + string.Concat(Enumerable.Range(1, 10).Select(i => V + i.ToString().PadLeft(4) + " ")) + V);
        o.Add("Ã" + s11 + string.Concat(Enumerable.Repeat("Å" + s5, 10)) + "´");
        var e = Sayfa7.Azaltma(p);
        o.Add(V + "Eksiltme % " + string.Concat(Enumerable.Range(0, 10).Select(i => V + (i < e.Length && e[i] != 0 ? e[i].ToString("0", ci) : "").PadLeft(4) + " ")) + V);
        o.Add("À" + s11 + string.Concat(Enumerable.Repeat("Á" + s5, 10)) + "Ù");
        }
        // duvar malzeme
        string s10 = new string('Ä', 10);
        if (Sayfa7.SD(p, 11, 10) == 1) {
        o.Add(" DUVAR MALZEME BİLGİLERİ");
        o.Add("Ú" + s11 + "Â" + s10 + "Â" + s10 + "Â" + s10 + "¿");
        o.Add("¡  Malzeme  ¡     E    ¡    fbd   ¡   fbtd   ¡");
        o.Add("¡     no    ¡ (kg/cm²) ¡ (kg/cm²) ¡ (kg/cm²) ¡");
        o.Add("Ã" + s11 + "Å" + s10 + "Å" + s10 + "Å" + s10 + "´");
        int m = p.Sec("/Materials/");
        for (int i = 0; i < 5; i++)
        {
            var f = p.St4[m + 2 + i].Split(',').Select(Proj.D).ToArray();
            o.Add(V + ("    B" + (i + 1)).PadRight(11) + V + f[0].ToString("0", ci).PadLeft(9) + " " + V + N(f[1], 2).PadLeft(8) + "  " + V + N(f[2], 2).PadLeft(8) + "  " + V);
        }
        o.Add("À" + s11 + "Á" + s10 + "Á" + s10 + "Á" + s10 + "Ù");
        }
        // rüzgar
        int w = p.Sec("/Wind parameters/"); var wf = p.St4[w + 1].Split(',').Select(Proj.D).ToArray();
        o.Add(" RÜZGAR YÜKÜ VE KATSAYILARI");
        o.Add(" RÜZGAR YÜKÜ BASINÇ KATSAYISI     : " + wf[1].ToString("0.###", ci));
        o.Add(" RÜZGAR YÜKÜ  EMME  KATSAYISI     : " + wf[0].ToString("0.###", ci));
        string s19 = new string('Ä', 19), s9 = new string('Ä', 9);
        o.Add("Ú" + s19 + "Â" + s9 + "Â" + s10 + "¿");
        o.Add("¡ Yükseklik bölgesi ¡    H    ¡    Qw    ¡");
        o.Add("Ã" + s19 + "Å" + s9 + "Å" + s10 + "´");
        for (int i = 0, b = 1; 2 + 2 * i + 1 < wf.Length; i++)
        {
            double h = wf[2 + 2 * i], q = wf[3 + 2 * i];
            if (h == 0 && q == 0) continue;
            o.Add(V + ("     " + b++ + ". bölge").PadRight(19) + V + N(h, 2).PadLeft(8) + " " + V + N(q, 2).PadLeft(8) + "  " + V);
        }
        o.Add("À" + s19 + "Á" + s9 + "Á" + s10 + "Ù");
        return o;
    }
}
