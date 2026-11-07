using StatikRaporClaude.Gpr;

namespace StatikRaporClaude.Uretec;

/// <summary>
/// GPR'siz metin bölümleri: STA4'ün yazdırma anında ürettiği sayfaların metnini analiz dosyalarından (ST4, OPS, USE, COD,
/// EAR, DIS, CQC, FeaSeismic, RAD…) GPR biçiminde satır satır üretir ve bunları sanal bir GPR sayfasına koyar; böylece
/// mevcut yeniden dizgi motoru (BolumDizgi) bölümü BEYKENT tasarımıyla basar. Üreteçler ve doğrulamaları:
/// scratchpad ara_genel araştırması (≈1000 proje GPR derlemi), bkz. docs/modul-durum/StatikRaporClaude.md.
/// </summary>
public static class SanalBolum
{
    /// <summary>Sanal bölüm anahtarı → üreteç. Anahtarlar "URETEC " önekiyle DosyaBolumleri anahtarlarından ayrılır.</summary>
    public static readonly Dictionary<string, Func<Proj, List<string>>> Ureticiler = new()
    {
        ["URETEC GIRIS"] = p => File.Exists(@"C:\Sta4V14\STA4.TXT")
            ? File.ReadAllLines(@"C:\Sta4V14\STA4.TXT", GprKod.Enc).Where(x => x.Trim().Length > 0).ToList() : null,
        ["URETEC DOSEME YUK"] = p => p.Use == null ? null : Yuk.Doseme(p.Use),
        ["URETEC KIRIS YUK"] = p => p.Use == null ? null : Yuk.Kiris(p.Use),
        ["URETEC OPSIYON"] = p => p.Ops == null ? null : Opsiyon.Uret(p),
        ["URETEC PROGRAM"] = p => Sayfa7.Uret(p, Revizyon()),
        ["URETEC KOMBINASYON"] = p => Kombinasyon.Uret(p),
        ["URETEC AKS"] = p => Aks.Uret(p),
        ["URETEC KAT MOMENT"] = p =>
        {
            var l = new List<string> { "    DEPREM PERDELERİ TABAN MOMENT KONTROLU" };
            l.AddRange(Devrilme.KatMomenti(p, out _, out _, out _, out _));
            return l;
        },
        ["URETEC MOD"] = p => ModGraf.Uret(p, 4),
        ["URETEC NOKTA DEPLASMAN"] = p => NoktaUret.Uret(p),
        ["URETEC DEVRILME"] = p => Devrilme.Uret(p),
        ["URETEC FEA GENEL"] = Fea3D.Genel,
        ["URETEC FEA KOORDINAT"] = Fea3D.Koordinat,
        ["URETEC FEA MESH"] = Fea3D.Mesh,
        ["URETEC FEA MOD"] = Fea3D.ModVektorleri,
        ["URETEC FEA NOKTA STATIK"] = Fea3D.NoktaStatik,
        ["URETEC FEA KIRIS"] = p => Fea3D.Cubuk(p, 1),
        ["URETEC FEA BAG KIRIS"] = p => Fea3D.Cubuk(p, 5),
        ["URETEC FEA KOLON"] = p => Fea3D.Cubuk(p, 2),
        ["URETEC FEA DEPLASMAN"] = Fea3D.NoktaDeplasman,
        ["URETEC KOLON SUNEKLIK"] = OzelTablolar.KolonSuneklik,
        ["URETEC TEMEL SUNEK"] = OzelTablolar.TemelSunekTesirler,
        ["URETEC DENGELI DONATI"] = OzelTablolar.DengeliDonati,
        ["URETEC IMAR"] = OzelTablolar.ImarKusatilmis,
        ["URETEC YORUM"] = OzelTablolar.YapiYorumu,
        ["URETEC YORUM OZET"] = OzelTablolar.YapiYorumuOzet,
        ["URETEC METRAJ ALAN ORANI"] = OzelTablolar.KolonPerdeAlanOrani,
        ["URETEC METRAJ TEMEL DONATI"] = Metraj.TemelDonatiListesi,
        ["URETEC METRAJ KIRIS"] = Metraj.KirisKalipBeton,
        ["URETEC METRAJ KOLON"] = Metraj.KolonKalipBeton,
        ["URETEC METRAJ DOSEME"] = Metraj.DosemeKalipBeton,
        ["URETEC METRAJ OZET"] = Metraj.BetonKalipOzeti,
        ["URETEC DUVAR YUK"] = HesapTablolari.DuvarYuk,
        ["URETEC DOSEME SERIT"] = HesapTablolari.DosemeSerit,
        ["URETEC DOSEME BILGILERI"] = HesapTablolari.DosemeBilgileri,
        ["URETEC KIRIS BILGILERI"] = HesapTablolari.KirisBilgileri,
        ["URETEC KIRIS SEHIM"] = HesapTablolari.KirisSehimCatlak,
        ["URETEC BURKULMA"] = HesapTablolari.Burkulma,
        ["URETEC KOLON KESME"] = HesapTablolari.KolonKesme,
        ["URETEC KIRIS KESME"] = HesapTablolari.KirisKesme,
        ["URETEC PERDE DIYAFRAM"] = HesapTablolari.PerdeDiyafram,
        ["URETEC TEMEL YUK"] = HesapTablolari.TemelYukleri,
    };

    /// <summary>
    /// STA4'ün değerlerinin bir kısmı analiz dosyalarında bulunmayan bölümler: eksik değerler bizim hesabımızla basılır
    /// (N_ULK_02 / K_BLG_03 GPR'lerine göre hücrelerin bir kısmında ± son hane / ±%1–3 sapma). Raporda başlık altında belirtilir.
    /// </summary>
    public static readonly Dictionary<string, string> Yaklasik = new()
    {
        ["URETEC DOSEME BILGILERI"] = "dmin sütunu TS500 11.2'ye göre hesaplandı",
        ["URETEC KIRIS SEHIM"] = "uç kesme/momentleri ankastre kiriş kabulüyle hesaplandı (yaklaşık)",
        ["URETEC BURKULMA"] = "göreli kat ötelemesi dosyalarda yok: Σ(Δi·Nd/L), Vfi ve é sütunları boş; k, Lkr, Nkr yaklaşık",
        ["URETEC KOLON KESME"] = "kesme güvenliğinde Mr/Ve ve perde β kendi hesabımızla (yaklaşık)",
        ["URETEC KIRIS KESME"] = "Tcr/Tr ve askı Vd kendi hesabımızla (son hanede sapabilir)",
        ["URETEC PERDE DIYAFRAM"] = "Asb ve Vr son hanede sapabilir",
        ["URETEC TEMEL YUK"] = "perde Mp kesit hesabıyla (yaklaşık); 3 kattan fazla yapıda hareketli yük azaltması uygulanmadı",
    };

    /// <summary>Program revizyon tarihi (GPR'siz): kurulu STA4 yazdırma programının tarihi.</summary>
    public static string Revizyon()
    {
        var t = File.GetLastWriteTime(@"C:\Sta4V14\OUTPRN.EXE");
        return $"{t.Day}.{t.Month}.{t.Year}";
    }

    public static bool Destekler(string anahtar) => anahtar != null && Ureticiler.ContainsKey(anahtar);

    /// <summary>Üreteci çalıştırır (hata/eksik dosyada null).</summary>
    public static List<string> Satirlar(string anahtar, string st4)
    {
        try { return Ureticiler[anahtar](new Proj(st4)); }
        catch { return null; }
    }

    /// <summary>
    /// Sayfasız sanal bölüme üretilen satırlardan bir sanal GPR sayfası ekler (yazı: x 130, satır aralığı 30, STA yazı tipi;
    /// kutu çizgisiz kısa büyük harfli satırlar başlık). Satır üretilemezse false.
    /// </summary>
    public static bool Doldur(GprBolum b, string st4)
    {
        var satir = Satirlar(b.Anahtar, st4);
        if (satir == null || satir.Count == 0) return false;
        var s = new GprSayfa { Sira = -1, No = 0, Baslik = b.Baslik };
        float y = 200;
        foreach (var ham in satir)
        {
            string m = StaKodlama.Unicode(ham);
            bool baslik = !ham.Contains('¡') && !ham.Contains('Ä') && ham.Trim().Length > 3 && ham.Trim().Length < 80
                          && ham.Trim() == ham.Trim().ToUpper(new System.Globalization.CultureInfo("tr-TR")) && ham.Any(char.IsLetter)
                          && !System.Text.RegularExpressions.Regex.IsMatch(ham, @"\S\s+:\s+\S");   // "YAPI KAT ADEDİ   : 2" başlık değil, anahtar-değer
            s.Yazilar.Add(new GprYazi { X = 130, Y = y, Tip = baslik ? -10 : 8, Font = 1, Ham = ham, Metin = m });
            y += 30;
        }
        s.Bolumler.Add(b);
        b.Sayfalar.Add(s);
        b.IlkY = 0; b.SonY = float.MaxValue;
        return true;
    }
}
