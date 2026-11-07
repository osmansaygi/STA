using System.Globalization;

namespace StatikRaporClaude.Uretec;
public static class Opsiyon
{
    public static string O(Proj p, int l, int f) { if (p.Ops == null || l > p.Ops.Length) return ""; var s = p.Ops[l - 1].Split(','); return f <= s.Length ? s[f - 1].Trim() : ""; }
    public static double OD(Proj p, int l, int f) => Proj.D(O(p, l, f));
    public static string Tr(double v) => v.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', ',');   // VB CStr, Türkçe yerel
    static string Pas(Proj p, int l) { double b = OD(p, l, 1); return Tr(b - 1.5) + " / " + Tr(b); }
    public static (double fck, double fy) Malzeme(Proj p)
    {
        int m = p.Sec("/Materials/"); var f = p.St4[m + 1].Split(',');
        return (Proj.D(f[0]), Proj.D(f[1]));
    }
    /// sira: ST4 6. satır beton sınıfı alanı (1 plak, 2 kiriş/kolon, 3 temel); çelik 4 (genel) / 6 (temel)
    public static double AsMin(Proj p, int sira = 2)
    {
        double fck = Sayfa7.Fck(p, (int)Sayfa7.SD(p, 6, sira)), fy = Sayfa7.Fy(p, (int)Sayfa7.SD(p, 6, sira == 3 ? 6 : 4));
        double gc = OD(p, 2, 2), gs = OD(p, 2, 3); if (gc == 0) gc = 1.5; if (gs == 0) gs = 1.15;
        return 0.8 * (0.35 * Math.Sqrt(fck / 10) / gc) / (fy / 10 / gs);
    }
    static string AsS(Proj p, int sira = 2) => Math.Round(AsMin(p, sira), 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture);

    public static List<string> Uret(Proj p)
    {
        var o = new List<string>();
        void L(string s) => o.Add(s);
        L("GENEL BETONARME CIZIM OPSIYONLARI");
        L("Maximum demir boyu...............................cm.= " + O(p, 17, 1));
        L("Minimum demir bindirme boyu oranı...................= ø x " + O(p, 17, 2));
        L("min. Lp.............................................= Lnet1 /" + O(p, 17, 3));
        L("Lpa.................................................= Lnet1 /" + O(p, 17, 6));
        L("min. Lpu.........................................cm.= " + O(p, 17, 4));
        L("min. Lpu ...........................................= d /" + O(p, 17, 5));
        L("min. Lk ............................................= Lnet2 /" + O(p, 17, 7));
        L("Pilye kayma donatısı katılım oranı..................= " + O(p, 17, 10));
        L("Genel kanca boyu ...................................= ø x 10");
        L("Kiriş donatısının, kolon içindeki aderans boyu......= ø x " + O(p, 17, 8));
        L("Kirişlerde sık etriye opsiyonu......................= " + (O(p, 17, 11) == "1" ? "zorunlu" : "?"));
        L("Kirişlerde Pilye opsiyonu...........................= " + (O(p, 17, 12) == "1" ? "pilyesiz" : "?"));
        L("Minimum pilye açıklık oranı.........................= Lnet/2");
        L("Tek donatılarda, pilye ve düz donatı tercihi........= düz");
        L("Kirişlerde minimum iki demir aralığı.............cm.= 2.5");
        L("DOSEME BETONARME OPSIYONLARI");
        L("Plaklarda paspayı.................................cm.= " + Tr(OD(p, 9, 1)));
        L("Maksimum  demir araligi...........................cm.= " + O(p, 9, 6) + ", d x " + Tr(OD(p, 9, 7)));
        L("Iki  yonlu  plak-minimum  cekme  bolgesi  pursantaji = " + OD(p, 9, 2).ToString("0.0###", CultureInfo.InvariantCulture));
        L("Tek  yonlu  plak-minimum  cekme  bolgesi  pursantaji = " + OD(p, 9, 3).ToString("0.0###", CultureInfo.InvariantCulture));
        L("Nervur Max. Etriye araligi........................cm.= 20, d/2");
        L("Lk :  ust donati uzatma  boyu.....................cm.= 50Ø, Ln/4");
        L("KIRIS BETONARME OPSIYONLARI");
        L("Etriye paspayı / Boyuna donatı paspayı ..........cm.= " + Pas(p, 10));
        L("Maksimum sehim sınırı  (bölme duvarsız) ........= L / " + O(p, 10, 2));
        L("Maksimum sehim sınırı  (bölme duvarlı) .........= L / " + O(p, 10, 3));
        L("Min. çekme bölgesi  TS500-2000 'e göre .............= " + AsS(p));
        L("     As min= 0.8 x fctd / fyd    alınacaktır.");
        L("Minimum düz ve pilye donatı çapı .................ø.= " + O(p, 10, 7));
        L("Minimum montaj donatı çapı .......................ø.= " + O(p, 10, 8));
        L("Minimum gövde donatı çapı ........................ø.= " + O(p, 10, 9));
        L("Minimum etriye donatı çapı .......................ø.= " + O(p, 10, 10));
        L("Pilye açısı.......................................°.= " + O(p, 11, 6));
        L("Minimum gövde demirsiz kiriş yüksekliği..........cm.= " + O(p, 11, 7));
        L("Minimum düz ve montaj demir aralığı .............cm.= " + O(p, 11, 8));
        L("Kayma donatısı beton katılım oranı..................= " + O(p, 11, 9));
        L("Süreklilik icin max. kolon genişliği.............cm.= " + O(p, 10, 5));
        L("Minimum montaj donatı oranı  .............(% maxAs).= " + O(p, 10, 6));
        L("Maximum etriye aralığı..S........................cm.= " + O(p, 11, 1));
        L("Minimum etriye aralığı..S........................cm.= " + O(p, 11, 2));
        L("Maximum etriye aralığı. Sk.(1)...................cm.= " + O(p, 11, 3));
        L("Maximum etriye aralığı. Sk.(2)......................= d/" + O(p, 11, 4));
        L("Maximum etriye aralığı. Sk.(3)......................= ø x " + O(p, 11, 5));
        L("Maksimum tek etriye genişliği ...................cm.= " + O(p, 10, 11));
        L("min.(alt As/üst As) ................................= " + O(p, 11, 11));
        if (OD(p, 36, 7) != 0) L("min.üst As= ........................................= 0,8 x fctd / fyd");
        else L("min.üst As=Fctd/Fyd ................................= Hayır");
        L("min Lb =............................................= ø x " + O(p, 17, 8));
        L("Alt ilaveye, düz donatıları L/4 uzatarak katılması..= Hayır");
        L("Üst ilaveye, montaj donatı. L/4 uzatarak katılması..= Hayır");
        L("KOLON-PERDE BETONARME OPSIYONLARI");
        L("govde");
        L("D >= " + O(p, 12, 11) + " × B (Perde)");
        L("D < " + O(p, 12, 11) + " × B");
        L("(Kolon)");
        L("KOLON ve PERDELERIN betonarme opsiyonlari           :");
        L("Etriye paspayı / Boyuna donatı paspayı ..........cm.= " + Pas(p, 12));
        L("Min.kolon çekme bölgesi.............................= " + O(p, 12, 2));
        L("Min.kolon toplam kesit .............................= " + O(p, 12, 3));
        L("Kolon eksenel yük eksantirisite etkisinin alınması..= evet");
        L("Minimum etriye aralığı...........................cm.= " + O(p, 12, 5));
        L("Maximum etriye aralığı.(1).......................cm.= " + O(p, 12, 6));
        L("Maximum etriye aralığı (2)......................min.= ø x " + O(p, 12, 7));
        L("Minimum çiroz aralığı...........................min.= ø x " + O(p, 12, 8));
        L("Minimum donatı çapı .......................ø........= " + O(p, 12, 9));
        L("Minimum etriye çapı .......................ø........= " + O(p, 12, 10));
        L("Perde/Kolon oranı  (D/B)............................= " + O(p, 12, 11));
        L("Perde uzun etriyelerinde gönye......................= " + (O(p, 6, 6) == "0" ? "Gönyesiz" : "Gönyeli"));
        L("Nervürlü etriye kanca acısı.............. (90ø,135ø)= 135");
        L("min.Hcr yuksekliği ................................<  D x " + O(p, 13, 1));
        L("max.Hcr yuksekliği ................................>= D x " + O(p, 13, 2));
        L("max.Hcr yuksekliği ................................>= Hw/" + O(p, 13, 3));
        L("Min.başlık bölgesi.(Hcr)............................= " + O(p, 13, 4));
        L("Min.başlık bölgesi..................................= " + O(p, 13, 5));
        L("Min.gövde bölgesi...................................= " + O(p, 13, 6));
        L("Min.başlık bölgesi................................Lu= " + O(p, 13, 7) + " cm");
        L("Min.başlık bölgesi.(Hcr)..........................Lu=B x " + O(p, 13, 8));
        L("Min.başlık bölgesi.(Hcr)..........................Lu=D x " + O(p, 13, 9));
        L("Min.başlık bölgesi................................Lu=B x " + O(p, 13, 10));
        L("Min.başlık bölgesi................................Lu=D x " + O(p, 13, 11));
        L("Başlık bölgesi min. donatı çapı .................ø..= " + O(p, 12, 14));
        L("Gövde  bölgesi min. donatı çapı .................ø..= " + O(p, 12, 15));
        L("Perdelerde tasarım eğilme momenti...................= " + (O(p, 13, 12) == "1" ? "Evet" : "Hayır"));
        L("Kolonlarda minimum iki demir aralığı.............cm.= 4.0");
        L("TEMEL BETONARME OPSIYONLARI");
        L("Etriye paspayı / Boyuna donatı paspayı ..............cm.= " + Pas(p, 14));
        L("Min. çekme bölgesi  TS500-2000 (As min=0,8.fctd/fyd).= " + AsS(p, 3));
        L("Min. toplam kesit ...................................= " + O(p, 14, 3));
        L("Minimum basınç bölgesi donatı oranı .................= " + O(p, 6, 3));
        L("Pilye açısı..........................................= " + O(p, 14, 4));
        L("Minimum etriye aralığı............................cm.= " + O(p, 14, 5));
        L("Maximum etriye aralığı............................cm.= " + O(p, 14, 6));
        L("Maximum etriye genislığı..........................cm.= " + O(p, 14, 10));
        L("Minimum düz ve montaj demir aralığı ..............cm.= " + O(p, 14, 7));
        L("Temelde, Kolon donatı filiz boyu..................cm.= " + O(p, 14, 8));
        L("Müt. temel min. etriye çapı.......................ø..= " + O(p, 15, 3));
        L("Müt. temel min. düz ve pilye çapı.................ø..= " + O(p, 15, 4));
        L("Müt. temel min. montaj çapı.......................ø..= " + O(p, 15, 5));
        L("Müt. temel min. gövde çapı........................ø..= " + O(p, 15, 6));
        L("Temel min. ampatman çapı..........................ø..= " + O(p, 15, 7));
        L("Ampatman kenar yüksekliği.(Ha)....................cm.= " + (OD(p, 14, 9) == 0 ? "Düz ampatman" : O(p, 14, 9)));
        return o;
    }
}
