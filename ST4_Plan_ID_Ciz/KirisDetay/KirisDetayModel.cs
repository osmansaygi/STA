using System.Collections.Generic;

namespace ST4PlanIdCiz.KirisDetay
{
    /// <summary>
    /// Kiriş donatı detay motorunun girdi, ayar ve sonuç tipleri.
    /// Kurallar: docs/kiris_detay_kurallari.md ve docs/kiris_kurallari.json.
    /// Birimler: mm ve MPa. Eksenel kuvvet Newton.
    /// </summary>

    public enum SuneklikDuzeyi
    {
        Yuksek,
        Sinirli
    }

    public enum DepremTasarimSinifi
    {
        Dts1,
        Dts1a,
        Dts2,
        Dts2a,
        Dts3,
        Dts3a,
        Dts4,
        Dts4a
    }

    public enum BetonSinifi
    {
        C20,
        C25,
        C30,
        C35,
        C40,
        C45,
        C50
    }

    public enum CelikSinifi
    {
        B420C,
        B500C
    }

    public enum FctdKaynagi
    {
        Tablo,
        Formul
    }

    /// <summary>TS 500 Çizelge 9.3 çevre durumu. Minimum net örtü buna göre zorlanır.</summary>
    public enum KirisCevre
    {
        /// <summary>Yapı içi, dış etkiye kapalı: cc ≥ 20 mm (TS500 9.5.1).</summary>
        Ic,
        /// <summary>Hava koşullarına açık: cc ≥ 25 mm.</summary>
        Dis,
        /// <summary>Zeminle doğrudan temas: cc ≥ 50 mm.</summary>
        ZeminleTemas
    }

    public enum MesnetTuru
    {
        Kenar,
        Ara,
        Perde
    }

    public enum KesitYeri
    {
        SolMesnet,
        Aciklik,
        SagMesnet
    }

    public enum DonatiRolu
    {
        Montaj,
        AltDuz,
        UstSolIlave,
        UstSagIlave,
        AltSolIlave,
        AltSagIlave,
        Govde,
        UstIlaveSurekli,
        AltIlaveSurekli
    }

    public enum KontrolSeviyesi
    {
        Bilgi,
        Uyari,
        Hata
    }

    public static class KirisKuralKodu
    {
        public const string Mat = "R-MAT";
        public const string Cc = "R-CC-01";
        public const string Sp = "R-SP-02";
        public const string RhoMin = "R-RHO-01";
        public const string RhoMax = "R-RHO-02";
        public const string Cap = "R-RHO-03";
        public const string SurekliAdet = "R-RHO-03-ADET";
        public const string SurekliOran = "R-RHO-03-14";
        public const string AltUcTeBir = "R-RHO-03-13";
        public const string MesnetOran = "R-RHO-04";
        public const string KenarA = "R-LB-04";
        public const string G01 = "R-G-01";
        public const string G02 = "R-G-02";
        public const string G03 = "R-G-03";
        public const string G04 = "R-G-04";
        public const string G05 = "R-G-05";
        public const string G06 = "R-G-06";
        public const string G12 = "R-G-12";
        public const string StCap = "R-ST-01";
        public const string Web = "R-WEB";
        public const string Lap = "R-LAP-03";
        public const string Lap30 = "R-LAP-02";
        public const string Girdi = "R-GIRDI";
    }

    /// <summary>
    /// Proje ve detaylandırma ayarları. Yönetmelik sayıları varsayılandır;
    /// açık konular (belge bölüm 13) ayar olarak durur, koda gömülü değildir.
    /// Süneklik ve DTS boş bırakılırsa hesap hata verir: bunlar proje girdisidir.
    /// </summary>
    public sealed class KirisDetayAyarlari
    {
        public SuneklikDuzeyi? Suneklik { get; set; }
        public DepremTasarimSinifi? Dts { get; set; }

        public BetonSinifi Beton { get; set; }
        public CelikSinifi Celik { get; set; }
        public CelikSinifi EtriyeCelik { get; set; }
        public FctdKaynagi FctdKaynagi { get; set; }

        /// <summary>TS500 6.2.5. Yerinde dökme 1.5; öndöküm 1.4; denetimsiz 1.7.</summary>
        public double GammaMc { get; set; }

        /// <summary>TS500 6.2.5. Tüm donatı sınıfları 1.15.</summary>
        public double GammaMs { get; set; }

        public double CcMm { get; set; }
        public KirisCevre Cevre { get; set; }

        /// <summary>En büyük agrega tane çapı. Aralık hesabında 4/3·Dmax kullanılır (TS500 9.5.2).</summary>
        public double DmaxMm { get; set; }

        /// <summary>TBDY kapsamı: C25 altı beton hata sayılır (TBDY 7.2.5.1).</summary>
        public bool TbdyKapsaminda { get; set; }

        /// <summary>
        /// Aynı sıradaki minimum net aralık tabanı, mm.
        /// TS500 7.3 kirişte 20 mm, 9.5.2 genel 25 mm der. Güvenli taraf 25 mm (bölüm 13.3).
        /// </summary>
        public double NetAralikMutlakMinMm { get; set; }

        /// <summary>Üst çubuklara Konum I katsayısı 1.4 uygulansın (TS500 9.1.1). Güvenli taraf: evet.</summary>
        public bool KonumKatsayisiUygula { get; set; }

        /// <summary>As,gerekli/As,mevcut azaltması. Önerilen: kapalı (bölüm 13.7).</summary>
        public bool KAsAzaltmaUygula { get; set; }
        public double KAs { get; set; }

        public bool Nervurlu { get; set; }

        /// <summary>TS500 9.3.1 metin 4φ, Şekil 9.1 6φ. Güvenli taraf 6φ ve 60 mm.</summary>
        public double Kanca180CarpanPhi { get; set; }
        public double Kanca180MinMm { get; set; }

        /// <summary>Boyuna 90° kanca düz ucu, TS500 9.3.1 / TBDY 7.4.3.1(b): 12φ.</summary>
        public double Kanca90CarpanPhi { get; set; }

        /// <summary>Moment diyagramı varken kesim aşımı φ çarpanı, nervürlü (TS500 7.3).</summary>
        public double KesimPhiCarpanNervurlu { get; set; }
        public double KesimPhiCarpanDuz { get; set; }

        /// <summary>
        /// Kuramsal kesimden sonraki aşım max(d, çarpan·φ, ve istenirse ℓb).
        /// Bölüm 13.8 güvenli seçenek: ℓb dahil.
        /// </summary>
        public bool KesimdeLbDahil { get; set; }

        /// <summary>Moment diyagramı yokken üst ilave: kolon yüzünden k_ln·ℓn (belge varsayılanı 0.25).</summary>
        public double KLn { get; set; }

        /// <summary>Üst ilave için kullanıcı alt sınırı, mm. 0 ise yok.</summary>
        public double LMinKullaniciMm { get; set; }

        /// <summary>Alt ilave açıklığa uzama: max(ℓb, bu çarpan·φ). TBDY 7.4.3.1(c) ile uyumlu varsayılan 50.</summary>
        public double AltIlavePhiCarpan { get; set; }

        /// <summary>Kesim boylarını bu adıma yukarı yuvarla. 0 kapatır. Varsayılan 50 mm.</summary>
        public double KesimBoyuYuvarlamaMm { get; set; }

        /// <summary>Kanca düşey boyunu bu adıma yukarı yuvarla. Varsayılan 10 mm (örnek: 197→200, 418→420).</summary>
        public double KancaBoyuYuvarlamaMm { get; set; }

        /// <summary>İkinci sıra üst ilave için ayrı ℓn katsayısı. Türk yönetmeliğinde yok; varsayılan kapalı.</summary>
        public bool IkinciSiraAyriLn { get; set; }
        public double BirinciSiraKLn { get; set; }
        public double IkinciSiraKLn { get; set; }

        /// <summary>Sol+sağ ilave arasındaki boşluk bundan kısaysa sürekli çubuğa dönülür.</summary>
        public double KisaAciklikBoslukMinMm { get; set; }

        /// <summary>
        /// 0 ise kapalı. Pozitifse boşluk &lt; çarpan·ℓ0 iken de birleştirilir.
        /// Güvenli varsayılan 2 (R-CUT-05).
        /// </summary>
        public double KisaAciklikL0Carpani { get; set; }

        public double StokBoyMm { get; set; }

        /// <summary>Alt donatı ekinin yasak olduğu açıklık ortası bölgesinin ℓn'ye oranı. TBDY sayı vermez; varsayılan 1/3.</summary>
        public double AciklikOrtasiAltEkOrani { get; set; }

        /// <summary>φ&gt;30 mm bindirme yerine manşon (TS500 9.2.6.1, güvenli taraf çekmeye de uygulanır).</summary>
        public bool Phi30UstuBindirmeYasak { get; set; }

        /// <summary>Tüm kesit çekmede ise α1=1.8 (TS500 9.2.5.1). Kirişte varsayılan kapalı.</summary>
        public bool TamKesitCekme { get; set; }

        /// <summary>
        /// DY yüksek sarılma aralığında metin d/4 (TBDY 7.4.4). Kapalıysa şeklin hk/4 değeri kullanılır.
        /// </summary>
        public bool YuksekSarilmaMetinDBolum4 { get; set; }
        public double SarilmaUstSinirYuksekMm { get; set; }
        public double SarilmaUstSinirSinirliMm { get; set; }

        /// <summary>8φ minimumunda gövde çapı dahil edilsin mi? Önerilen: hayır (bölüm 13.11).</summary>
        public bool PhiMinGovdeDahil { get; set; }

        public double IlkEtriyeMm { get; set; }

        /// <summary>Etriye aralığı bu adımın katına aşağı yuvarlanır. Varsayılan 25 mm (114→100).</summary>
        public double EtriyeAralikYuvarlamaMm { get; set; }

        /// <summary>Orta bölge pratik üst sınır (Topçu). 0 ise yalnız TS500 d/2 geçerlidir.</summary>
        public double OrtaBolgePratikUstSinirMm { get; set; }

        public double SarilmaPratikAltSinirMm { get; set; }
        public double OrtaPratikAltSinirMm { get; set; }

        /// <summary>Vd &gt; 3·Vcr ise orta bölgede s ≤ d/4 (TS500 8.1.6). Kesme hesabı programa ait değildir.</summary>
        public bool VdUcVcrdenBuyuk { get; set; }

        /// <summary>TS500 Denk. 8.1 içindeki γ. Vcr bilgisi için; aralığı doğrudan VdUcVcrdenBuyuk belirler.</summary>
        public double VcrGamma { get; set; }

        public double? KullaniciSarilmaAraligiMm { get; set; }
        public double? KullaniciOrtaAralikMm { get; set; }

        public double EtriyeKancaUcCarpan { get; set; }
        public double EtriyeKancaUcMinMm { get; set; }
        public double EtriyeKancaIcCapCarpan { get; set; }
        public double KolAralikMaxMm { get; set; }

        /// <summary>TS500 gövde yüksekliği eşiği için h kullan (güvenli). Kapalıysa h−t.</summary>
        public bool GovdeYuksekligiHKullan { get; set; }
        public double GovdeTs500EsikMm { get; set; }
        public double GovdeAralikMaxMm { get; set; }
        public double GovdeCirozDuseyMaxMm { get; set; }
        public double GovdeCirozEksenMaxMm { get; set; }

        /// <summary>Etriye köşe kıvrımına göre köşe çubuğunu içeri al (isteğe bağlı, varsayılan kapalı).</summary>
        public bool KoseKivrimDuzeltmesi { get; set; }

        public bool MafsalliKiris { get; set; }
        public bool BagKirisi { get; set; }
        public bool IkincilKiris { get; set; }
        public bool BurulmaVar { get; set; }
        public bool DolayliMesnet { get; set; }

        public KirisDetayAyarlari()
        {
            Beton = BetonSinifi.C30;
            Celik = CelikSinifi.B420C;
            EtriyeCelik = CelikSinifi.B420C;
            FctdKaynagi = FctdKaynagi.Tablo;
            GammaMc = 1.5;
            GammaMs = 1.15;
            CcMm = 25;
            Cevre = KirisCevre.Dis;
            DmaxMm = 22;
            TbdyKapsaminda = true;
            NetAralikMutlakMinMm = 25;
            KonumKatsayisiUygula = true;
            KAsAzaltmaUygula = false;
            KAs = 1.0;
            Nervurlu = true;
            Kanca180CarpanPhi = 6;
            Kanca180MinMm = 60;
            Kanca90CarpanPhi = 12;
            KesimPhiCarpanNervurlu = 20;
            KesimPhiCarpanDuz = 40;
            KesimdeLbDahil = true;
            KLn = 0.25;
            LMinKullaniciMm = 0;
            AltIlavePhiCarpan = 50;
            KesimBoyuYuvarlamaMm = 50;
            KancaBoyuYuvarlamaMm = 10;
            IkinciSiraAyriLn = false;
            BirinciSiraKLn = 1.0 / 3.0;
            IkinciSiraKLn = 0.25;
            KisaAciklikBoslukMinMm = 500;
            KisaAciklikL0Carpani = 2;
            StokBoyMm = 12000;
            AciklikOrtasiAltEkOrani = 1.0 / 3.0;
            Phi30UstuBindirmeYasak = true;
            TamKesitCekme = false;
            YuksekSarilmaMetinDBolum4 = true;
            SarilmaUstSinirYuksekMm = 150;
            SarilmaUstSinirSinirliMm = 200;
            PhiMinGovdeDahil = false;
            IlkEtriyeMm = 50;
            EtriyeAralikYuvarlamaMm = 25;
            OrtaBolgePratikUstSinirMm = 200;
            SarilmaPratikAltSinirMm = 50;
            OrtaPratikAltSinirMm = 100;
            VdUcVcrdenBuyuk = false;
            VcrGamma = 0.07;
            EtriyeKancaUcCarpan = 6;
            EtriyeKancaUcMinMm = 80;
            EtriyeKancaIcCapCarpan = 5;
            KolAralikMaxMm = 350;
            GovdeYuksekligiHKullan = true;
            GovdeTs500EsikMm = 600;
            GovdeAralikMaxMm = 300;
            GovdeCirozDuseyMaxMm = 600;
            GovdeCirozEksenMaxMm = 400;
            KoseKivrimDuzeltmesi = false;
        }
    }

    public sealed class DonatiGrubu
    {
        public int Adet { get; set; }
        public double CapMm { get; set; }

        /// <summary>Demetteki çubuk sayısı. 1 = demet yok. TS500 9.5.3 en çok 3.</summary>
        public int DemetAdet { get; set; }

        public DonatiGrubu()
        {
            DemetAdet = 1;
        }

        public DonatiGrubu(int adet, double capMm)
        {
            Adet = adet;
            CapMm = capMm;
            DemetAdet = 1;
        }

        public bool Var
        {
            get { return Adet > 0 && CapMm > 0; }
        }

        public int Demet
        {
            get { return DemetAdet < 1 ? 1 : DemetAdet; }
        }

        /// <summary>Yerleşim ve kenetlenmede kullanılan çap. Demette φe = 1.2·φ·√n (TS500 Denk. 9.3).</summary>
        public double GeometrikCapMm
        {
            get
            {
                if (Demet <= 1) return CapMm;
                return KirisFormuller.EsdegerCap(CapMm, Demet);
            }
        }

        /// <summary>Gerçek çelik alanı (eşdeğer daire alanı değil).</summary>
        public double AlanMm2
        {
            get
            {
                if (!Var) return 0;
                return Adet * Demet * KirisFormuller.TekCubukAlani(CapMm);
            }
        }
    }

    public sealed class KirisMesnet
    {
        public MesnetTuru Tur { get; set; }
        /// <summary>Kolonun (veya perdenin) kiriş doğrultusundaki boyutu, mm.</summary>
        public double HcMm { get; set; }
        /// <summary>Kirişe dik kolon/perde genişliği, mm. R-G-02.</summary>
        public double BDikMm { get; set; }
        public double CcMm { get; set; }
        public double PhiWMm { get; set; }
        public double PhiBoyunaMm { get; set; }
        /// <summary>a hesabından kolon boyuna çapı da düşülsün (isteğe bağlı).</summary>
        public bool BoyunaCapiDus { get; set; }

        public KirisMesnet()
        {
            CcMm = 25;
            PhiWMm = 10;
        }
    }

    public sealed class KirisDonatiGirdisi
    {
        public DonatiGrubu Montaj { get; set; }
        public DonatiGrubu AltDuz { get; set; }
        public DonatiGrubu UstSolIlave { get; set; }
        public DonatiGrubu UstSagIlave { get; set; }
        public DonatiGrubu AltSolIlave { get; set; }
        public DonatiGrubu AltSagIlave { get; set; }

        /// <summary>Kullanıcının verdiği gövde: yüz başına adet ve çap. Adet 0 ise motor gerekirse önerir.</summary>
        public DonatiGrubu Govde { get; set; }

        public double EtriyeCapMm { get; set; }

        public KirisDonatiGirdisi()
        {
            Montaj = new DonatiGrubu();
            AltDuz = new DonatiGrubu();
            UstSolIlave = new DonatiGrubu();
            UstSagIlave = new DonatiGrubu();
            AltSolIlave = new DonatiGrubu();
            AltSagIlave = new DonatiGrubu();
            Govde = new DonatiGrubu();
            EtriyeCapMm = 8;
        }
    }

    /// <summary>Tek kiriş açıklığının detay girdisi. Çizim kodu bu nesneyi doldurup <see cref="KirisDetayMotoru.Hesapla"/> çağırır.</summary>
    public sealed class KirisDetayGirdi
    {
        public double BwMm { get; set; }
        public double HMm { get; set; }
        /// <summary>Tabla / döşeme kalınlığı, mm. 0 ise tabla yok.</summary>
        public double TMm { get; set; }
        /// <summary>Net açıklık, kolon yüzünden kolon yüzüne, mm.</summary>
        public double LnMm { get; set; }
        public double? LnKomsuSolMm { get; set; }
        public double? LnKomsuSagMm { get; set; }

        public KirisMesnet Sol { get; set; }
        public KirisMesnet Sag { get; set; }
        public KirisDonatiGirdisi Donati { get; set; }
        public KirisDetayAyarlari Ayarlar { get; set; }

        /// <summary>Moment diyagramından kuramsal kesim noktası, ilgili kolon yüzünden açıklığa, mm.</summary>
        public double? KuramsalUstSolMm { get; set; }
        public double? KuramsalUstSagMm { get; set; }
        public double? KuramsalAltSolMm { get; set; }
        public double? KuramsalAltSagMm { get; set; }

        /// <summary>Tasarım eksenel kuvveti, N. Boşsa R-G-06 atlanır.</summary>
        public double? EksenelKuvvetN { get; set; }

        public KirisDetayGirdi()
        {
            Sol = new KirisMesnet();
            Sag = new KirisMesnet();
            Donati = new KirisDonatiGirdisi();
            Ayarlar = new KirisDetayAyarlari();
        }

        /// <summary>Belge bölüm 11 örneği: C30, B420C, φ16, φ10 etriye, 300×500, cc 25.</summary>
        public static KirisDetayGirdi OrnekC30B420C(double dmaxMm, double lnMm, bool kenarMesnet)
        {
            var g = new KirisDetayGirdi();
            g.BwMm = 300;
            g.HMm = 500;
            g.TMm = 120;
            g.LnMm = lnMm;
            g.Ayarlar.Suneklik = SuneklikDuzeyi.Yuksek;
            g.Ayarlar.Dts = DepremTasarimSinifi.Dts1;
            g.Ayarlar.Beton = BetonSinifi.C30;
            g.Ayarlar.Celik = CelikSinifi.B420C;
            g.Ayarlar.EtriyeCelik = CelikSinifi.B420C;
            g.Ayarlar.CcMm = 25;
            g.Ayarlar.Cevre = KirisCevre.Dis;
            g.Ayarlar.DmaxMm = dmaxMm;
            g.Donati.Montaj = new DonatiGrubu(2, 16);
            g.Donati.AltDuz = new DonatiGrubu(3, 16);
            g.Donati.EtriyeCapMm = 10;
            MesnetTuru tur = kenarMesnet ? MesnetTuru.Kenar : MesnetTuru.Ara;
            g.Sol = MesnetOrnek(tur);
            g.Sag = MesnetOrnek(tur);
            return g;
        }

        public static KirisMesnet MesnetOrnek(MesnetTuru tur)
        {
            return new KirisMesnet
            {
                Tur = tur,
                HcMm = 400,
                BDikMm = 400,
                CcMm = 25,
                PhiWMm = 10
            };
        }
    }

    public sealed class KuralKontrol
    {
        public string Kod { get; set; }
        public string Madde { get; set; }
        public KontrolSeviyesi Seviye { get; set; }
        public string Mesaj { get; set; }
    }

    public sealed class MalzemeSonuc
    {
        public BetonSinifi Beton { get; set; }
        public CelikSinifi Celik { get; set; }
        public double Fck { get; set; }
        public double Fcd { get; set; }
        public double Fctk { get; set; }
        public double Fctd { get; set; }
        public double FctdFormul { get; set; }
        public double Ec { get; set; }
        public double K1 { get; set; }
        public double Fyk { get; set; }
        public double Fyd { get; set; }
        public double Fywd { get; set; }
        public double RhoMin { get; set; }
        public double RhoB { get; set; }
        public string CevreSinifiAdi { get; set; }
    }

    public sealed class YerlesenCubuk
    {
        public DonatiRolu Rol { get; set; }
        public int Sira { get; set; }
        public double CapMm { get; set; }
        public double XMm { get; set; }
        public double YYuzdenMm { get; set; }
        public double YAlttanMm { get; set; }
        public bool Kose { get; set; }
        public bool Surekli { get; set; }
        public double BirimAlanMm2 { get; set; }
    }

    public sealed class YuzYerlesim
    {
        public double BavMm { get; set; }
        public double SMinMm { get; set; }
        public int NMax { get; set; }
        public double SNetMm { get; set; }
        public bool SNetVar { get; set; }
        public double SvMm { get; set; }
        public double YsMm { get; set; }
        public double DMm { get; set; }
        public int SiraSayisi { get; set; }
        public bool Sigmadi { get; set; }
        public List<YerlesenCubuk> Cubuklar { get; set; }

        public YuzYerlesim()
        {
            Cubuklar = new List<YerlesenCubuk>();
        }
    }

    public sealed class KesitSonuc
    {
        public KesitYeri Yer { get; set; }
        public YuzYerlesim Ust { get; set; }
        public YuzYerlesim Alt { get; set; }
        public List<YerlesenCubuk> Govde { get; set; }
        /// <summary>Bu kesitte çekme yüzüne göre faydalı yükseklik (mesnette üst, açıklıkta alt).</summary>
        public double DCekmeMm { get; set; }

        public KesitSonuc()
        {
            Govde = new List<YerlesenCubuk>();
        }
    }

    public sealed class MesnetKenetlenme
    {
        public MesnetTuru Tur { get; set; }
        public bool KancaVar { get; set; }
        /// <summary>Kolon içindeki yatay gömme, mm.</summary>
        public double AMm { get; set; }
        public double AMinMm { get; set; }
        /// <summary>90° kanca düşey boyu, mm. Üst çubuk aşağı, alt çubuk yukarı kıvrılır.</summary>
        public double BMm { get; set; }
        public double BMinMm { get; set; }
        public double LbMm { get; set; }
        public bool DuzKenetlenme { get; set; }
        public bool AsagiKivrilir { get; set; }
    }

    public sealed class GrupKenetlenme
    {
        public DonatiRolu Rol { get; set; }
        public double CapMm { get; set; }
        public double Lb0HamMm { get; set; }
        public double LbHamMm { get; set; }
        public double LbMm { get; set; }
        public bool KonumI { get; set; }
        public bool KOrtu { get; set; }
    }

    public sealed class KenetlenmeSonuc
    {
        public List<GrupKenetlenme> Gruplar { get; set; }
        public double LbAltMm { get; set; }
        public double LbUstMm { get; set; }
        public double LbkAltMm { get; set; }
        public double L0AltR05Mm { get; set; }
        public double L0AltR1Mm { get; set; }
        public double L0UstR1Mm { get; set; }
        public double AraSolAltUzatmaMm { get; set; }
        public double AraSagAltUzatmaMm { get; set; }
        public MesnetKenetlenme SolUst { get; set; }
        public MesnetKenetlenme SolAlt { get; set; }
        public MesnetKenetlenme SagUst { get; set; }
        public MesnetKenetlenme SagAlt { get; set; }

        public KenetlenmeSonuc()
        {
            Gruplar = new List<GrupKenetlenme>();
        }
    }

    public sealed class IlaveBoySonuc
    {
        public double UstSolAciklikMm { get; set; }
        public double UstSagAciklikMm { get; set; }
        public double AltSolAciklikMm { get; set; }
        public double AltSagAciklikMm { get; set; }
        public double LnRefSolMm { get; set; }
        public double LnRefSagMm { get; set; }
        public bool UstBirlesti { get; set; }
        public bool AltBirlesti { get; set; }
        public double UstSira2SolMm { get; set; }
        public double UstSira2SagMm { get; set; }
    }

    public sealed class EtriyeSonuc
    {
        public double LSarMm { get; set; }
        public double SSarMaxHamMm { get; set; }
        public double SSarMm { get; set; }
        public double SOrtaMaxHamMm { get; set; }
        public double SOrtaMm { get; set; }
        public int NSarBirUc { get; set; }
        public int NOrta { get; set; }
        public int ToplamAdet { get; set; }
        public bool TumKirisSarilma { get; set; }
        public int NKol { get; set; }
        public double EksenAraligiMm { get; set; }
        public double KancaAci { get; set; }
        public double KancaUcMm { get; set; }
        public double KancaIcCapMm { get; set; }
        public double PhiWMm { get; set; }
        public double PhiBoyunaMinMm { get; set; }
        public double AswBolumSGerekli { get; set; }
        public double AswMm2 { get; set; }
        public List<double> KonumlarMm { get; set; }
        public List<double> KolXMm { get; set; }

        public EtriyeSonuc()
        {
            KonumlarMm = new List<double>();
            KolXMm = new List<double>();
            KancaAci = 135;
        }
    }

    public sealed class GovdeSonuc
    {
        public bool Gerekli { get; set; }
        public bool Ts500Tetik { get; set; }
        public bool TbdyTetik { get; set; }
        public double AsGerekliMm2 { get; set; }
        public double PhiMinMm { get; set; }
        public int OnerilenAdetYuz { get; set; }
        public double OnerilenCapMm { get; set; }
        public double HSerbestMm { get; set; }
        public int KullanilanAdetYuz { get; set; }
        public double KullanilanCapMm { get; set; }
        public double CirozDuseyMaxMm { get; set; }
        public double CirozEksenMaxMm { get; set; }
        public int CirozAdetYukseklik { get; set; }
    }

    /// <summary>
    /// Boyuna açılım. X = 0 sol kolon yüzü, X = ℓn sağ kolon yüzü.
    /// Negatif X sol kolona / komşu açıklığa girer.
    /// </summary>
    public sealed class BoyunaCubukCizim
    {
        public DonatiRolu Rol { get; set; }
        public double CapMm { get; set; }
        public int Adet { get; set; }
        public double BaslangicMm { get; set; }
        public double BitisMm { get; set; }
        public double BoyMm { get; set; }
        public bool SolKanca { get; set; }
        public bool SagKanca { get; set; }
        public double SolKancaBMm { get; set; }
        public double SagKancaBMm { get; set; }
        public bool SolKancaAsagi { get; set; }
        public bool SagKancaAsagi { get; set; }
        public bool Surekli { get; set; }
    }

    public sealed class EkYeri
    {
        public DonatiRolu Rol { get; set; }
        public double CapMm { get; set; }
        public double BaslangicMm { get; set; }
        public double BitisMm { get; set; }
        public double L0Mm { get; set; }
        public double R { get; set; }
        public bool Manson { get; set; }
        public bool OzelEtriyeGerekli { get; set; }
        public double OzelEtriyeAralikMm { get; set; }
        public double SasirtmaMinMm { get; set; }
        public string Aciklama { get; set; }
    }

    public sealed class KirisDetaySonuc
    {
        public bool Gecerli { get; set; }
        public double CcKullanilanMm { get; set; }
        public double DMm { get; set; }
        public MalzemeSonuc Malzeme { get; set; }
        public List<KesitSonuc> Kesitler { get; set; }
        public KenetlenmeSonuc Kenetlenme { get; set; }
        public IlaveBoySonuc Ilave { get; set; }
        public EtriyeSonuc Etriye { get; set; }
        public GovdeSonuc Govde { get; set; }
        public List<BoyunaCubukCizim> BoyunaCubuklar { get; set; }
        public List<EkYeri> Ekler { get; set; }
        public List<KuralKontrol> Kontroller { get; set; }

        public KirisDetaySonuc()
        {
            Kesitler = new List<KesitSonuc>();
            BoyunaCubuklar = new List<BoyunaCubukCizim>();
            Ekler = new List<EkYeri>();
            Kontroller = new List<KuralKontrol>();
            Kenetlenme = new KenetlenmeSonuc();
            Ilave = new IlaveBoySonuc();
            Etriye = new EtriyeSonuc();
            Govde = new GovdeSonuc();
        }

        public KesitSonuc Kesit(KesitYeri yer)
        {
            for (int i = 0; i < Kesitler.Count; i++)
            {
                if (Kesitler[i].Yer == yer) return Kesitler[i];
            }
            return null;
        }
    }
}
