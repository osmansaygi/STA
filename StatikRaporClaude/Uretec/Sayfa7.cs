using System.Globalization;
using System.Text.RegularExpressions;

namespace StatikRaporClaude.Uretec;
public static class Sayfa7
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;
    const char V = '¡';
    public static string S(Proj p, int l, int f) { var s = p.St4[l - 1].Split(','); return f <= s.Length ? s[f - 1].Trim() : ""; }
    public static double SD(Proj p, int l, int f) => Proj.D(S(p, l, f));
    static string N(double v, int d) => Fmt.ND(v, d);
    static readonly string[] Zemin = { "ZA", "ZB", "ZC", "ZD", "ZE", "ZF" };

    /// Kat adları ve kotları (/Story/: ad, önek, "kot,diyafram,n,?")
    public static List<(string Ad, string On, double Kot, int Dyf)> Katlar(Proj p)
    {
        int s = p.Sec("/Story/"); var o = new List<(string, string, double, int)>();
        for (int i = s + 1; i + 2 < p.St4.Length && !p.St4[i].StartsWith("/"); i += 3)
        { var f = p.St4[i + 2].Split(','); o.Add((p.St4[i].Trim(), p.St4[i + 1].Trim(), Proj.D(f[0]), (int)Proj.D(f[1]))); }
        return o;
    }
    public static double[] Azaltma(Proj p) => p.Use[1].Split(',').Select(Proj.D).ToArray();
    public static double ZeminAzaltma(Proj p)
    {
        int n = (int)SD(p, 4, 1); var e = Azaltma(p); double t = 0;
        for (int i = 0; i < n; i++) t += 1 - (i < e.Length ? e[i] : e[^1]) / 100.0;
        return n > 0 ? t / n : 1;
    }
    public static double Fck(Proj p, int idx) { if (idx == 0) { int m = p.Sec("/Materials/"); return Proj.D(p.St4[m + 1].Split(',')[0]); } return Opsiyon.OD(p, 4, idx); }   // OPS 4. satır sınıf listesi (kg/cm²)
    public static double Fy(Proj p, int idx) { if (idx == 0) { int m = p.Sec("/Materials/"); return Proj.D(p.St4[m + 1].Split(',')[1]); } return Opsiyon.OD(p, 5, idx); }
    public static double E(double fck) => Math.Floor((3250 * Math.Sqrt(fck / 10) + 14000) / 50) * 500;

    public static List<string> Uret(Proj p, string rev)
    {
        var o = new List<string>();
        void L(string lab, string v) => o.Add(lab + ": " + v);
        o.Add("STA4-CAD  PROGRAMI");
        o.Add("ÇOK KATLI BETONARME YAPILARIN STATİK ve BETONARME ANALİZ PROGRAMI  Ver.14.1  Rev.(" + rev + ")");
        L("PROJE İSMİ...................................", p.St4[2].Trim());
        L("KAT ADEDI....................................", S(p, 4, 1));
        L("Bir kattaki KOLON SAYISI.....................", S(p, 4, 2));
        L("X yönü aks sayısı............................", S(p, 5, 1));
        L("Y yönü aks sayısı............................", S(p, 5, 2));
        L("DEPREM YER HAREKETİ DÜZEYİ...................", "DD2   50 yılda aşılma olasılığı %10");
        int z = (int)SD(p, 14, 6) - 21;
        L("ZEMİN SINIFI.................................", z >= 0 && z < Zemin.Length ? Zemin[z] : "?");
        L("BİNA KOORDİNATI.............. (ENLEM/BOYLAM) ", S(p, 14, 7) + "° / " + S(p, 14, 8) + "°");
        L("YEREL SPECTRAL İVME KATSAYISI......... Ss/Sı ", N(SD(p, 14, 4), 3) + " / " + N(SD(p, 14, 5), 3));
        L("YAPI DAVRANIŞ KATSAYISI                  R   ", N(SD(p, 4, 5), 2));
        L("SİSTEM DAYANIM FAZLALIĞI KATSAYISI...... D ..", SD(p, 14, 1).ToString("0.###", ci));
        double sds = SD(p, 14, 2), sd1 = SD(p, 14, 3);
        L("SPEKTRUM KAREKTERİSTİK PERYODU...... (Ta/Tb) ", N(0.2 * sd1 / sds, 3) + " / " + N(sd1 / sds, 3));
        L("HAREKETLİ YÜK KATSAYISI.................(n)..", N(SD(p, 4, 9), 1));
        L("SIFIR RÖLATİF HAREKET YÜKSEKLİĞİ........ (m).", N(SD(p, 7, 1), 2));
        L("HAREKETLİ YÜK AZALTMA KATSAYISI........(Cz)..", N(SD(p, 7, 6), 1));
        L("ZEMİN TAŞIMA GÜCÜ TASARIM GERİLMESİ. (t/m²)..", N(SD(p, 7, 3), 1));
        L("ZEMİN YATAK KATSAYISI............... (t/m³)..", N(SD(p, 7, 2), 1));
        int m = p.Sec("/Materials/"); var mat = p.St4[m + 1].Split(',');
        L("BETON YOĞUNLUĞU......................(t/m³)..", Proj.D(mat[3]).ToString("0.0##", ci));
        L("GENLEŞME ISI FARKI.....................(°C)..", N(SD(p, 10, 2), 1));
        string mesh = SD(p, 16, 2) > 0 ? "" : "";
        L("STATİK ANALİZ YÖNTEMİ .......................", SD(p, 13, 4) == 1 ? "FEA3D LINEER ANALİZ / Birim Mesh Genisligi " + MeshS(p) + "m" : "FRAME3D LINEER ANALİZ");
        L("DEPREM STANDARDI ............................", "TBDY2018 CODE");
        L("BETONARME HESAP YÖNTEMİ .....................", "TAŞIMA GÜCÜ YÖNTEMİ TS500-2000");
        L("BETONARME KESİT DONATI HESAP YÖNTEMİ ........", "BRÜT KESİTE GÖRE");
        L("DEPREM HESABI YÖNTEMİ .......................", "MOD SÜPERPOZİSYONU İLE MODAL ANALİZ");
        int tm = (int)SD(p, 8, 3);
        L("TEMEL ANALİZ OPSİYONU........................", tm == 0 ? "TEMELLER DİKKATE ALINMADAN, YAPI ANALİZİ" : tm == 1 ? "SADECE TEMEL DÖNMELERİ DİKKATE ALINMASI" : "TÜM TEMEL DEPLASMANLARI DİKKATE ALINMASI");
        L("Zemin gerilmesi hareketli yük azaltma degeri.", N(ZeminAzaltma(p), 2));
        L("Kolonun oturduğu kiriş tesir çarpanı.........", Opsiyon.OD(p, 7, 1) == 5 ? "Düşey deprem analizi yapılmıştır." : N(Opsiyon.OD(p, 7, 3) * 0 + 1.5, 2));
        L("Kiriş & Kolon rijitlik bölgesi opsiyonu......", "Yarı Sonsuz Rijit davranış");
        o.Add("Kiriş uçlarında elastik ankastrelik opsiyonu : Elastik ankastre");
        // çatlamış kesit (TBDY 2018 Tablo 4.2 STA4 sabitleri)
        o.Add(" ÇATLAMIŞ KESİT ETKİN KESİT RİJİTLİĞİ BİLGİLERİ");
        string sep = new string('Ä', 16);
        o.Add("Ú" + new string('Ä', 26) + string.Concat(Enumerable.Repeat("Â" + sep, 4)) + "¿");
        o.Add(V + "    Elemanlar             " + V + "    Eğilme      " + V + "    Eksenel     " + V + " Lokal X kesme  " + V + " Lokal Y kesme  " + V);
        o.Add("Ã" + new string('Ä', 26) + string.Concat(Enumerable.Repeat("Å" + sep, 4)) + "´");
        (string, double, double, double, double)[] ck = {
            ("Perde", .25, .5, .5, 1), ("Bodrum perdesi", .5, .8, .5, 1), ("Döşeme", .25, .25, .25, 1), ("Çerçeve kirişi", .35, 1, 1, 1),
            ("Çerçeve kolonu", .7, 1, 1, 1), ("Bağ kirişi", .15, 1, 1, 1), ("Perde çubuk", .5, 1, .5, .5) };
        foreach (var (ad, a, b, c, d) in ck)
            o.Add(V + ("    " + ad).PadRight(26) + V + ("      " + N(a, 2)).PadRight(16) + V + ("     " + N(b, 2)).PadRight(16) + V + ("     " + N(c, 2)).PadRight(16) + V + ("     " + N(d, 2)).PadRight(16) + V);
        o.Add("À" + new string('Ä', 26) + string.Concat(Enumerable.Repeat("Á" + sep, 4)) + "Ù");
        // malzeme
        o.Add(" BETON ve ÇELİK MALZEME BİLGİLERİ           (kg/cm²)");
        o.Add("ÚÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÂÄÄÄÄÄÄÄÄÂÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÂÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÂÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÂÄÄÄÄÄÄÄÄÄÄÄÄÄ¿");
        o.Add("¡ Yapı Elemanı       ¡ Malzeme¡  Elastisite Modülü  ¡ Beton dayanım  ¡Çelik  akma   gerilmesi¡Birim Ağırlık¡");
        o.Add("¡                    ¡        ¡      E          G   ¡    gerilmesi   ¡   (Genel)    (Etriye) ¡     t/m³    ¡");
        o.Add("ÃÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÅÄÄÄÄÄÄÄÄÅÄÄÄÄÄÄÄÄÄÄÂÄÄÄÄÄÄÄÄÄÄÅÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÅÄÄÄÄÄÄÄÄÄÄÄÂÄÄÄÄÄÄÄÄÄÄÄÅÄÄÄÄÄÄÄÄÄÄÄÄÄ´");
        double gam = Proj.D(mat[3]);
        foreach (var (ad, ci6, si6, si7) in MalzemeSatirlari())
        {
            double fck = Fck(p, ci6 < 0 ? 0 : (int)SD(p, 6, ci6)); double e = E(fck);
            double fyg = Fy(p, si6 < 0 ? 0 : (int)SD(p, 6, si6)), fye = Fy(p, si7 < 0 ? 0 : (int)SD(p, 6, si7));
            o.Add(V + ad.PadRight(20) + V + ("   C" + (fck / 10).ToString("0", ci)).PadRight(8) + V + e.ToString("0", ci).PadLeft(9) + " " + V + (e * 0.4).ToString("0", ci).PadLeft(9) + " " + V
                + ("      " + fck.ToString("0", ci)).PadRight(16) + V + ("  " + fyg.ToString("0", ci)).PadRight(11) + V + ("  " + fye.ToString("0", ci)).PadRight(11) + V + ("     " + N(gam, 2)).PadRight(13) + V);
        }
        o.Add("ÀÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÁÄÄÄÄÄÄÄÄÁÄÄÄÄÄÄÄÄÄÄÁÄÄÄÄÄÄÄÄÄÄÁÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÁÄÄÄÄÄÄÄÄÄÄÄÁÄÄÄÄÄÄÄÄÄÄÄÁÄÄÄÄÄÄÄÄÄÄÄÄÄÙ");
        o.Add(" HNP : Hazır Nervürlü Plak");
        string w45 = new string('Ä', 45), w17 = new string('Ä', 17), w16 = new string('Ä', 16);
        o.Add("Ú" + w45 + "Â" + w17 + "Â" + w16 + "¿");
        o.Add(V + "TAŞIMA GÜCÜ MALZEME KATSAYILARI".PadRight(45) + V + "      BETON      " + V + "     ÇELİK      " + V);
        o.Add(V + new string(' ', 45) + V + ("      " + N(Opsiyon.OD(p, 2, 2), 2)).PadRight(17) + V + ("     " + N(Opsiyon.OD(p, 2, 3), 2)).PadRight(16) + V);
        o.Add("Ã" + w45 + "Å" + w17 + "Å" + w16 + "´");
        o.Add(V + "TAŞIMA GÜCÜ YÜK KATSAYILARI".PadRight(45) + V + "    SABİT YÜK    " + V + " HAREKETLİ YÜK  " + V);
        var cod = Kombinasyon.Cod(p);
        o.Add(V + new string(' ', 45) + V + ("      " + N(cod[0][0], 2)).PadRight(17) + V + ("     " + N(cod[0][1], 2)).PadRight(16) + V);
        o.Add("À" + w45 + "Á" + w17 + "Á" + w16 + "Ù");
        return o;
    }
    public static string MeshS(Proj p) { var s = SD(p, 13, 5).ToString("#.###", ci); return s == "" ? "0" : s; }
    public static (string, int, int, int)[] MalzemeSatirlari()
    {
        var e = Environment.GetEnvironmentVariable("MATIDX") ?? "1,4,5;1,4,5;3,6,7;2,4,5";
        var q = e.Split(';').Select(x => x.Split(',').Select(int.Parse).ToArray()).ToArray();
        return new[] { ("Plak/Nervür      E1", q[0][0], q[0][1], q[0][2]), ("HNP", q[1][0], q[1][1], q[1][2]), ("Temel            E1", q[2][0], q[2][1], q[2][2]), ("Kiriş\\Kolon      E1", q[3][0], q[3][1], q[3][2]) };
    }
}
