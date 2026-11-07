using StatikRaporClaude.Gpr;

namespace StatikRaporClaude.Rapor
{
    /// <summary>STA4CAD yazdırma penceresindeki bir seçeneğe karşılık gelen rapor kalemi.</summary>
    public sealed class RaporKalemi
    {
        public string Sekme, Ad;
        public bool Varsayilan;
        /// <summary>GPR'de yok; bölümleri sonuç dosyalarından üretilecek sanal bölümler.</summary>
        public bool Dosyadan;
        public Func<GprBolum, string, bool> Eslesir;   // (bölüm, anahtar)
        /// <summary>Bu kalemin rapora girecek bölümleri (aynı başlığın metin/grafik/tekrar sürümlerinden yalnız biri).</summary>
        public readonly List<GprBolum> Bolumler = new List<GprBolum>();
        public int SayfaSayisi => Bolumler.SelectMany(b => b.Sayfalar).Distinct().Count();
    }

    /// <summary>
    /// STA4CAD "Printer yazdırma seçimi" sekmelerine denk rapor kataloğu. Varsayılanlar, N_ULK_02 ilk raporunun
    /// (N_ULK_RPR.pdf, 20.10.2026) içindekileri ve kullanıcının gönderdiği seçim ekranlarıyla eşlendi.
    /// Aynı rapor STA4'te hem "text" hem "grafik" çıktı olarak varsa tablo (text) sürümü kullanılır.
    /// </summary>
    public static class RaporKatalogu
    {
        public static readonly string[] Sekmeler =
            { "Yapı genel", "Hesap çıktıları", "Aplikasyon", "Radye", "Özel tablolar", "FEA3D", "Diğer" };

        private static Func<GprBolum, string, bool> Iceren(params string[] parcalar) =>
            (b, a) => parcalar.Any(p => a.Contains(p));

        private static Func<GprBolum, string, bool> Esit(params string[] adlar) =>
            (b, a) => adlar.Contains(a);

        private static bool Radye(GprBolum b) => b.Radye;
        private static bool Fea(GprBolum b) => b.Grup == "FEA3D analizi";
        private static bool BagKirisi(GprBolum b) =>
            b.Sayfalar.Take(2).Any(s => s.Yazilar.Any(y => GprBolumleri.Anahtar(y.Metin).Contains("BAG KIRISI")));

        public static List<RaporKalemi> Olustur()
        {
            var k = new List<RaporKalemi>();
            void E(string sekme, string ad, bool v, Func<GprBolum, string, bool> f) => k.Add(new RaporKalemi { Sekme = sekme, Ad = ad, Varsayilan = v, Eslesir = f });

            // ---- Radye ve FEA önce: aynı başlıklar (MESH BİLGİLERİ, NOKTA DEPLASMANLARI) bağlama göre ayrılır
            const string R = "Radye";
            E(R, "Radye izometrik çizimi", true, (b, a) => (Radye(b) && a.Contains("DUGUM PLANI")) || a == "TEMEL 3D GORUNUSU");
            E(R, "Radye genel bilgileri", true, (b, a) => Radye(b) && a.Contains("RADYE PROGRAMI"));
            E(R, "Radye mesh bilgileri", false, (b, a) => Radye(b) && (a.Contains("PLAK BILGILERI") || a.Contains("MESH BILGILERI")));
            E(R, "Radye nokta yük bilgileri", false, (b, a) => Radye(b) && (a.Contains("NOKTA YUKLERI") || a.Contains("DUVAR YUK")));
            E(R, "X yönü statik sonuçları", false, (b, a) => Radye(b) && a.Contains("X YONU STATIK"));
            E(R, "Y yönü statik sonuçları", false, (b, a) => Radye(b) && a.Contains("Y YONU STATIK"));
            E(R, "Analiz sonuçları ve temel donatısı", true, (b, a) => Radye(b) && (a == "ANALIZ SONUCLARI" || a.Contains("TEMEL DONATI SONUCLARI")));
            E(R, "Ek donatı grafiği", false, (b, a) => Radye(b) && a.Contains("EK DONATI ALANI"));
            E(R, "Winkler yay yükleri", false, (b, a) => Radye(b) && a.Contains("WINKLER"));
            E(R, "Zemin gerilmesi", true, (b, a) => Radye(b) && a.StartsWith("ZEMIN GERILMESI T"));
            E(R, "Zemin gerilmesi kontrolü", false, (b, a) => Radye(b) && a.Contains("TASIMA GUCU"));
            E(R, "Deplasmanlar", true, (b, a) => Radye(b) && a.Contains("NOKTA DEPLASMAN"));
            E(R, "Temel kiriş statik sonuçları", false, (b, a) => Radye(b) && a.Contains("KIRIS STATIK"));
            E(R, "Radye zımbalama", true, (b, a) => Radye(b) && a.Contains("ZIMBALAMA"));

            const string F = "FEA3D";
            E(F, "Genel bilgiler", false, (b, a) => Fea(b) && a.Contains("3D SONLU"));
            E(F, "Koordinat bilgileri", false, (b, a) => Fea(b) && a.Contains("KOORDINAT"));
            E(F, "Mesh bilgileri", false, (b, a) => Fea(b) && a.Contains("MESH"));
            E(F, "Mod vektörleri", false, (b, a) => Fea(b) && a.Contains("MOD VEKTOR"));
            E(F, "Mesh analiz sonuçları", false, (b, a) => Fea(b) && a.Contains("NOKTALARIN STATIK"));
            E(F, "Kiriş analiz sonuçları", false, (b, a) => Fea(b) && a.Contains("KIRIS STATIK"));
            E(F, "Kolon analiz sonuçları", false, (b, a) => Fea(b) && a.Contains("KOLON STATIK"));
            E(F, "Deplasmanlar", false, (b, a) => Fea(b) && a.Contains("DEPLASMAN"));

            // ---- Yapı genel bilgisi
            const string G = "Yapı genel";
            E(G, "STA4 programı giriş", true, Esit("STA4 CAD", "PERDE VE KOLONLARDA EKSANTRISITE"));
            E(G, "STA4 yük analizi", true, Iceren("DOSEME YUK ANALIZI", "KIRIS YUK ANALIZI"));
            E(G, "Yapı genel opsiyonları", true, Iceren("CIZIM OPSIYONLARI", "BETONARME OPSIYONLARI"));
            E(G, "Yapı genel bilgileri", true, Iceren("STA4 CAD PROGRAMI", "YUK KOMBINASYONU", "AKS BILGILERI"));
            E(G, "Yapı deprem raporu", true, Iceren("DEPREM RAPORU", "TABAN MOMENT", "DUZENSIZLIK"));
            E(G, "Yapı deprem mod vektörleri", true, Iceren("MOD GRAFIGI"));
            E(G, "Yapı nokta deplasmanları", false, Esit("NOKTA DEPLASMANLARI"));
            E(G, "Yapı deprem devrilme kontrolü", false, Iceren("DEVRILME"));
            E(G, "Yapı elemanları metrajı", false, Iceren("METRAJ"));

            // ---- Hesap çıktıları (STA4 "Yapı text output")
            const string H = "Hesap çıktıları";
            E(H, "Eleman yük bilgileri", true, Iceren("KIRIS DUSEY YUK", "KOLON YUK BILGILERI", "PANEL DUVAR YUK", "DUVAR YUK BILGILERI"));
            E(H, "Döşeme şerit bilgileri", true, Iceren("DOSEME SERIT"));
            E(H, "Döşeme bilgileri", true, Esit("DOSEME BILGILERI"));
            E(H, "Döşeme statik sonuçları", true, Iceren("DOSEME STATIK"));
            E(H, "Döşeme betonarme sonuçları", true, Iceren("DOSEME BETONARME"));
            E(H, "Kiriş bilgileri", true, Iceren("KIRIS VE PANEL BILGILERI", "KIRIS BILGILERI"));
            E(H, "Kiriş statik sonuçları", true, Iceren("KIRIS STATIK HESAP"));
            E(H, "Kiriş betonarme sonuçları", true, Iceren("KIRIS BETONARME"));
            E(H, "Kolon statik sonuçları", true, Iceren("KOLON STATIK HESAP"));
            E(H, "Kolon betonarme sonuçları", true, Iceren("KOLON BETONARME"));
            E(H, "Kiriş sehim ve çatlak kontrolü", true, Iceren("SEHIM VE CATLAK"));
            E(H, "Kat ve kolon burkulma sonuçları", false, Iceren("BURKULMA"));
            E(H, "Kolon kesme tasarım kontrolü", true, Iceren("KOLONLARIN KESME", "SARILMA BOLGESI", "PERDELERIN KESME"));
            E(H, "Kiriş kesme tasarım kontrolü", true, Iceren("KIRISLERIN KESME", "ASKI DONATISI"));
            E(H, "Perde-plak kesme yükü transferi", false, Iceren("DIYAFRAM KESME"));
            E(H, "Temel yük bilgisi", true, (b, a) => a.Contains("TEMELLERE GELEN KOLON YUKLERI") || (a.Contains("TEMELLERE KOLONLARDAN") && a.Contains("MOMENTLERI")));
            E(H, "Temel statik sonuçları", true, Iceren("TEMEL STATIK"));
            E(H, "Bağ kiriş", false, (b, a) => a.Contains("TEMEL BETONARME") && BagKirisi(b));
            E(H, "Temel betonarme sonuçları", true, Iceren("TEMEL BETONARME"));
            E(H, "Temel zımbalama", false, Iceren("TEMEL ZIMBALAMA"));

            // ---- Kat aplikasyon
            const string P = "Aplikasyon";
            E(P, "Temel aplikasyon planı", true, Iceren("TEMEL APLIKASYON"));
            E(P, "Kat kalıp aplikasyon planları", true, Iceren("KALIP APLIKASYON"));
            E(P, "Yapı 3D görünüşü", false, Iceren("YAPI 3D"));

            // ---- Özel tablolar
            const string O = "Özel tablolar";
            E(O, "Kolon süneklik alan kontrolü", false, Iceren("SUNEKLILIK ALAN"));
            E(O, "Kolon-perde temel yükleri (geoteknik)", false, Iceren("TASARIM DEPREM TESIRLERI"));
            E(O, "Mat temel zemin gerilmesi", false, (b, a) => a.Contains("TASIMA GUCU") || a == "ZEMIN GERILMESI");
            E(O, "Kirişlerde dengeli donatı ve sünek tasarım", false, Iceren("DENGELI DONATI"));
            E(O, "İmar yönetmeliği kuşatılmış kolon kontrolü", false, Iceren("IMAR YONETMELIGI"));
            E(O, "Yapı yorumu", false, Iceren("YAPI YORUMU"));   // varsayılan olarak teknik raporla (seçenek) gelir
            return k;
        }

        /// <summary>
        /// GPR'siz üretilebilen kalemler: GPR bölüm anahtarı, başlık, grup. Üreticiler: Veri.DosyaBolumleri (tablolar),
        /// ModernBolumler (deprem raporu ST4 + FeaMod, kiriş/kolon betonarme KMX/SMX).
        /// </summary>
        private static readonly Dictionary<string, (string Anahtar, string Baslik, string Grup)[]> DosyaUreticileri = new()
        {
            ["Yapı deprem raporu"] = new[] { ("DEPREM RAPORU", "DEPREM RAPORU", "Deprem"), ("URETEC KAT MOMENT", "DEPREM PERDELERİ TABAN MOMENT KONTROLU", "Deprem") },
            ["Eleman yük bilgileri"] = new[] { ("KIRIS DUSEY YUK BILGILERI", "KİRİŞ DÜŞEY YÜK BİLGİLERİ", "Yük bilgileri"), ("KOLON YUK BILGILERI", "KOLON YÜK BİLGİLERİ", "Yük bilgileri"), ("URETEC DUVAR YUK", "DUVAR YÜK BİLGİLERİ", "Yük bilgileri") },
            ["Döşeme statik sonuçları"] = new[] { ("DOSEME STATIK HESAP SONUCLARI", "DÖŞEME STATİK HESAP SONUÇLARI", "Döşeme") },
            ["Döşeme betonarme sonuçları"] = new[] { ("DOSEME BETONARME HESAP SONUCLARI", "DÖŞEME BETONARME HESAP SONUÇLARI", "Döşeme") },
            ["Kiriş statik sonuçları"] = new[] { ("KIRIS STATIK HESAP SONUCLARI", "KİRİŞ STATİK HESAP SONUÇLARI", "Kiriş") },
            ["Kiriş betonarme sonuçları"] = new[] { ("KIRIS BETONARME HESAP SONUCLARI", "KİRİŞ BETONARME HESAP SONUÇLARI", "Kiriş") },
            ["Kolon statik sonuçları"] = new[] { ("KOLON STATIK HESAP SONUCLARI", "KOLON STATİK HESAP SONUÇLARI", "Kolon ve perde") },
            ["Kolon betonarme sonuçları"] = new[] { ("KOLON BETONARME HESAP SONUCLARI", "KOLON BETONARME HESAP SONUÇLARI", "Kolon ve perde") },
            ["Radye|Deplasmanlar"] = new[] { ("RADYE NOKTA DEPLASMANLARI", "RADYE NOKTA DEPLASMANLARI", "Radye temel") },
            ["STA4 programı giriş"] = new[] { ("URETEC GIRIS", "STA4-CAD", "Genel bilgiler") },
            ["STA4 yük analizi"] = new[] { ("URETEC DOSEME YUK", "DÖŞEME YÜK ANALİZİ", "Genel bilgiler"), ("URETEC KIRIS YUK", "KİRİŞ YÜK ANALİZİ", "Genel bilgiler") },
            ["Yapı genel opsiyonları"] = new[] { ("URETEC OPSIYON", "BETONARME OPSİYONLARI", "Genel bilgiler") },
            ["Yapı genel bilgileri"] = new[] { ("URETEC PROGRAM", "STA4-CAD PROGRAMI", "Genel bilgiler"), ("URETEC KOMBINASYON", "BETONARME HESAP YÜK KOMBİNASYONU", "Genel bilgiler"), ("URETEC AKS", "YAPI AKS BİLGİLERİ", "Genel bilgiler") },
            ["Yapı deprem mod vektörleri"] = new[] { ("URETEC MOD", "MODAL ANALİZ MOD GRAFİĞİ (1000 x Dep. vektörü)", "Deprem") },
            ["Yapı nokta deplasmanları"] = new[] { ("URETEC NOKTA DEPLASMAN", "NOKTA DEPLASMANLARI", "Deprem") },
            ["Yapı deprem devrilme kontrolü"] = new[] { ("URETEC DEVRILME", "DEPREM YAPI DEVRILME KONTROLU", "Deprem") },
            ["Radye|Zemin gerilmesi"] = new[] { ("RADYE ZEMIN GERILMESI", "ZEMİN GERİLMESİ", "Radye temel") },
            ["Radye|Radye mesh bilgileri"] = new[] { ("RADYE PLAK BILGILERI", "PLAK BİLGİLERİ", "Radye temel"), ("RADYE MESH BILGILERI", "MESH BİLGİLERİ", "Radye temel") },
            ["Radye|Radye nokta yük bilgileri"] = new[] { ("RADYE NOKTA YUKLERI", "RADYE NOKTA YÜKLERİ (t)-(m)", "Radye temel") },
            ["Radye|Winkler yay yükleri"] = new[] { ("RADYE WINKLER YAY YUKLERI", "WINKLER YAY YÜKLERİ (t)", "Radye temel") },
            ["Radye|Zemin gerilmesi kontrolü"] = new[] { ("RADYE ZEMIN GERILMESI KONTROLU", "ZEMİN GERİLMESİNİN, ZEMİN TAŞIMA GÜCÜ TASARIM GERİLMESİNE GÖRE KONTROLU t/m²", "Radye temel") },
            ["Döşeme şerit bilgileri"] = new[] { ("URETEC DOSEME SERIT", "DÖŞEME ŞERİT BİLGİLERİ", "Döşeme") },
            ["Döşeme bilgileri"] = new[] { ("URETEC DOSEME BILGILERI", "DÖŞEME BİLGİLERİ", "Döşeme") },
            ["Kiriş bilgileri"] = new[] { ("URETEC KIRIS BILGILERI", "KİRİŞ VE PANEL BİLGİLERİ", "Kiriş") },
            ["Kiriş sehim ve çatlak kontrolü"] = new[] { ("URETEC KIRIS SEHIM", "KİRİŞ SEHİM ve ÇATLAK KONTROLU     (TS500-13.2.4)", "Kiriş") },
            ["Kat ve kolon burkulma sonuçları"] = new[] { ("URETEC BURKULMA", "KAT BURKULMA HESABI (t)     (TS500-7.6)", "Kolon ve perde") },
            ["Kolon kesme tasarım kontrolü"] = new[] { ("URETEC KOLON KESME", "KOLONLARIN KESME DAYANIM KONTROLU", "Kolon ve perde") },
            ["Kiriş kesme tasarım kontrolü"] = new[] { ("URETEC KIRIS KESME", "KİRİŞLERİN KESME ve BURULMA KONTROLU  (tm)     (TS500-8.2)", "Kiriş") },
            ["Perde-plak kesme yükü transferi"] = new[] { ("URETEC PERDE DIYAFRAM", "PERDE-DIYAFRAM KESME YÜKÜ KONTROLU   TBDY2018-7.11.6, 17.9", "Kolon ve perde") },
            ["Temel yük bilgisi"] = new[] { ("URETEC TEMEL YUK", "TEMELLERE GELEN KOLON YÜKLERİ", "Temel") },
            ["Kolon süneklik alan kontrolü"] = new[] { ("URETEC KOLON SUNEKLIK", "KOLON SUNEKLILIK ALAN KONTROLU", "Özel tablolar") },
            ["Kolon-perde temel yükleri (geoteknik)"] = new[] { ("URETEC TEMEL SUNEK", "TEMELLERE KOLONLARDAN GELEN, SÜNEK TASARIM DEPREM TESİRLERİ", "Özel tablolar") },
            ["Kirişlerde dengeli donatı ve sünek tasarım"] = new[] { ("URETEC DENGELI DONATI", "KİRİŞLERDE DENGELİ DONATI ORANI ve SÜNEK TASARIM KONTROLU", "Özel tablolar") },
            ["İmar yönetmeliği kuşatılmış kolon kontrolü"] = new[] { ("URETEC IMAR", "İMAR YÖNETMELİĞİ, KAPALI ÇIKMALI YAPIDA KUŞATILMIŞ KOLON KONTROLU", "Özel tablolar") },
            ["Yapı yorumu"] = new[] { (YorumAnahtari, "Yapı yorumu", "Özel tablolar") },
            ["Yapı elemanları metrajı"] = new[]
            {
                ("URETEC METRAJ TEMEL DONATI", "TEMEL DONATI KEŞİF METRAJI", "Metraj"),
                ("URETEC METRAJ KIRIS", "KİRİŞ KALIP VE BETON KEŞİF METRAJI", "Metraj"),
                ("URETEC METRAJ KOLON", "KOLON KALIP VE BETON KEŞİF METRAJI", "Metraj"),
                ("URETEC METRAJ DOSEME", "DÖŞEME KALIP VE BETON KEŞİF METRAJI", "Metraj"),
                ("URETEC METRAJ OZET", "YAPI BETON/KALIP KEŞİF METRAJI", "Metraj"),
                ("URETEC METRAJ ALAN ORANI", "KOLON VE PERDE ALAN ORANI  (m²)", "Metraj"),
            },
            ["FEA3D|Genel bilgiler"] = new[] { ("URETEC FEA GENEL", "YAPININ 3D SONLU ELEMANLARLA ANALİZİ", "FEA3D analizi") },
            ["FEA3D|Koordinat bilgileri"] = new[] { ("URETEC FEA KOORDINAT", "KOORDİNAT BİLGİLERİ", "FEA3D analizi") },
            ["FEA3D|Mesh bilgileri"] = new[] { ("URETEC FEA MESH", "MESH BİLGİLERİ", "FEA3D analizi") },
            ["FEA3D|Mod vektörleri"] = new[] { ("URETEC FEA MOD", "MOD VEKTÖRLERİ", "FEA3D analizi") },
            ["FEA3D|Mesh analiz sonuçları"] = new[] { ("URETEC FEA NOKTA STATIK", "NOKTALARIN STATİK SONUÇLARI", "FEA3D analizi") },
            ["FEA3D|Kiriş analiz sonuçları"] = new[] { ("URETEC FEA KIRIS", "KİRİŞ STATİK SONUÇLARI   (t,tm)", "FEA3D analizi"), ("URETEC FEA BAG KIRIS", "BAĞ KIRIS STATİK SONUÇLARI   (t,tm)", "FEA3D analizi") },
            ["FEA3D|Kolon analiz sonuçları"] = new[] { ("URETEC FEA KOLON", "KOLON STATİK SONUÇLARI   (t,tm)", "FEA3D analizi") },
            ["FEA3D|Deplasmanlar"] = new[] { ("URETEC FEA DEPLASMAN", "NOKTA DEPLASMANLARI", "FEA3D analizi") },
        };

        /// <summary>Kalemin GPR'siz üretimi için gereken sonuç dosyaları (taban ad + uzantı).</summary>
        private static string[] GerekenDosyalar(string kalem) => kalem switch
        {
            "Yapı deprem raporu" => new[] { ".FeaMod" },
            "STA4 yük analizi" => new[] { ".USE" },
            "Yapı genel opsiyonları" => new[] { ".OPS" },
            "Yapı deprem mod vektörleri" => new[] { ".CQC" },
            "Yapı nokta deplasmanları" => new[] { ".DIS", ".EAR" },
            "Yapı deprem devrilme kontrolü" => new[] { ".EAR", ".FeaSeismic", ".RAD" },
            "Eleman yük bilgileri" => new[] { ".LOA" },
            "Döşeme statik sonuçları" => new[] { ".FLO", ".DMX" },
            "Döşeme betonarme sonuçları" => new[] { ".DMX" },
            "Kiriş statik sonuçları" => new[] { ".BEA" },
            "Kiriş betonarme sonuçları" => new[] { ".KMX", ".BEA" },
            "Kolon statik sonuçları" => new[] { ".COL", ".SMX" },
            "Kolon betonarme sonuçları" => new[] { ".SMX", ".COL" },
            _ when kalem.StartsWith("Radye|") => new[] { ".RAD", ".DE1" },
            "Döşeme bilgileri" => new[] { ".FLO" },
            "Kiriş bilgileri" => new[] { ".KMX", ".COL" },
            "Kiriş sehim ve çatlak kontrolü" => new[] { ".KMX", ".COL", ".LOA", ".OPS" },
            "Kat ve kolon burkulma sonuçları" => new[] { ".COL", ".KMX" },
            "Kolon kesme tasarım kontrolü" => new[] { ".COL", ".SMX", ".KMX" },
            "Kiriş kesme tasarım kontrolü" => new[] { ".KMX", ".COL" },
            "Perde-plak kesme yükü transferi" => new[] { ".COL", ".KMX", ".DMX" },
            "Temel yük bilgisi" => new[] { ".COL", ".SMX" },
            "Kolon süneklik alan kontrolü" => new[] { ".COL", ".SMX" },
            "Kolon-perde temel yükleri (geoteknik)" => new[] { ".COL", ".MASS" },
            "Kirişlerde dengeli donatı ve sünek tasarım" => new[] { ".KMX", ".OPS" },
            "Yapı yorumu" => new[] { ".YORUM" },
            "Yapı elemanları metrajı" => new[] { ".FLO", ".MASS" },
            "FEA3D|Mod vektörleri" => new[] { ".Fea3D", ".FeaMod", ".FeaVec" },
            _ when kalem.StartsWith("FEA3D|") => new[] { ".Fea3D", ".FeaMod" },
            _ => new string[0]
        };

        /// <summary>Üretici anahtarı: aynı ad birden çok sekmede varsa (Deplasmanlar) "Sekme|Ad".</summary>
        private static string Kod(RaporKalemi k) => DosyaUreticileri.ContainsKey(k.Sekme + "|" + k.Ad) ? k.Sekme + "|" + k.Ad : k.Ad;

        public static bool DosyadanUretilir(RaporKalemi k) => DosyaUreticileri.ContainsKey(Kod(k));

        /// <summary>Kalemin dosyadan üretim anahtarları (yoksa boş).</summary>
        public static IEnumerable<string> UretimAnahtarlari(RaporKalemi k) =>
            DosyaUreticileri.TryGetValue(Kod(k), out var l) ? l.Select(x => x.Anahtar) : Enumerable.Empty<string>();

        /// <summary>
        /// GPR'den bağımsızlık: bölümü GPR'de olmayan (veya GPR'si hiç olmayan) ama sonuç dosyalarından üretilebilen kalemlere
        /// sayfasız sanal bölüm ekler. Sanal bölüm numaraları 900000'den başlar; derleyici bunları dosyadan üretir.
        /// </summary>
        public static void Zorla(List<RaporKalemi> kalemler, Proje.ProjeKlasoru p)
        {
            if (p == null) return;
            int no = 900000;
            foreach (var k in kalemler)
            {
                if (k.Bolumler.Count > 0 || !DosyaUreticileri.TryGetValue(Kod(k), out var liste)) continue;
                if (!GerekenDosyalar(Kod(k)).All(p.DosyaVar) || !p.DosyaVar(".BEA")) continue;
                foreach (var (anahtar, baslik, grup) in liste)
                    k.Bolumler.Add(new GprBolum { No = no++, Anahtar = anahtar, Baslik = baslik, Grup = grup, Radye = grup == "Radye temel" });
                k.Dosyadan = true;
            }
        }

        /// <summary>
        /// Rapor sırası: GPR bölümleri GPR sırasıyla; sanal bölümler kendi grubunun son GPR bölümünden sonra,
        /// grubu GPR'de yoksa grup sırasında kendinden sonraki ilk grubun önüne.
        /// </summary>
        public static List<GprBolum> Sirala(IEnumerable<GprBolum> bolumler)
        {
            var hepsi = bolumler.Distinct().ToList();
            var sira = hepsi.Where(b => b.No < 900000).OrderBy(b => b.No).ToList();
            int Grup(GprBolum b) { int i = Array.IndexOf(GprBolumleri.GrupSirasi, b.Grup); return i < 0 ? 99 : i; }
            foreach (var s in hepsi.Where(b => b.No >= 900000).OrderBy(b => b.No))
            {
                int son = sira.FindLastIndex(b => Grup(b) <= Grup(s));
                sira.Insert(son + 1, s);
            }
            return sira;
        }

        /// <summary>
        /// Kalem neden seçilemiyor (bölümü GPR'de yok): proje klasörü ve ST4'e bakarak en olası neden. Bölümü varsa null.
        /// </summary>
        public static string PasifNedeni(RaporKalemi k, Proje.ProjeKlasoru p, bool gprVar)
        {
            if (k.Bolumler.Count > 0) return null;
            // radye sonuçları (RAD + DE1) bu projede yoksa: aynı klasörde radyesi çözülmüş başka proje var mı
            if (k.Sekme == "Radye" && p != null && !(p.DosyaVar(".RAD") && p.DosyaVar(".DE1")))
            {
                string baska = Directory.GetFiles(p.Klasor, "*.DE1").Select(Path.GetFileNameWithoutExtension)
                    .FirstOrDefault(ad => !string.Equals(ad, p.TabanAd, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(p.Klasor, ad + ".RAD")));
                return baska != null ? $"radye sonucu yok — {baska}.ST4 içinde var" : "radye analiz sonucu yok (.RAD/.DE1)";
            }
            if (DosyadanUretilir(k)) return "gerekli sonuç dosyaları yok: " + string.Join(", ", GerekenDosyalar(Kod(k)).Append(".BEA").Distinct().Where(u => p == null || !p.DosyaVar(u)));
            if (!gprVar) return "GPR yok ve dosyalardan üretilemiyor";
            const string secilmemis = "STA4 yazdırmada seçilmemiş";
            if (p == null) return secilmemis;
            if (k.Sekme == "Radye")
                return !p.DosyaVar(".RAD") ? "radye analizi yapılmamış" : !p.St4ElemanVar("/Slab foundations/") ? "projede radye yok" : secilmemis;
            if (k.Sekme == "FEA3D") return !p.DosyaVar(".Fea3D") ? "FEA3D analizi yapılmamış" : secilmemis;
            switch (k.Ad)
            {
                case "Bağ kiriş": return p.St4ElemanVar("/Tie beams/") ? secilmemis : "projede bağ kirişi yok";
                case "Temel zımbalama":
                    return p.St4ElemanVar("/Single footings/") || p.St4ElemanVar("/Slab foundations/") ? secilmemis : "projede tekil temel / radye yok";
                case "Temel statik sonuçları":
                case "Temel betonarme sonuçları":
                    return p.St4ElemanVar("/Continuous foundations/") || p.St4ElemanVar("/Single footings/") || p.St4ElemanVar("/Tie beams/")
                        ? secilmemis : "projede sürekli/tekil temel yok (radye ayrı sekmede)";
                case "Mat temel zemin gerilmesi": return p.St4ElemanVar("/Slab foundations/") ? secilmemis : "projede radye yok";
            }
            return secilmemis;
        }

        /// <summary>Yapı yorumu üreteci: .YORUM'dan A, D, E maddeleri ve kolon/perde alan oranı tablosu.</summary>
        public const string YorumAnahtari = "URETEC YORUM OZET";

        /// <summary>
        /// Raporun içeriği: GPR'deki bölümler (tekrar baskılar ayıklanmış). Yapı yorumu ayrı seçenekle
        /// (RaporSecenekleri.TeknikRapor) teknik raporla birlikte kapaktan sonra gelir.
        /// </summary>
        public static List<RaporKalemi> GprIcerigi(GprBelgesi g, Proje.ProjeKlasoru p) =>
            Dagit(g).Where(x => x.Bolumler.Count > 0).ToList();

        /// <summary>Jeoteknik bilgi raporuna giren kalemler, rapordaki sırasıyla.</summary>
        private static readonly string[] GeoteknikKalemleri =
        {
            "Kolon-perde temel yükleri (geoteknik)", "Zemin gerilmesi kontrolü", "Mat temel zemin gerilmesi", "Zemin gerilmesi", "Temel aplikasyon planı",
        };

        /// <summary>
        /// Jeoteknik bilgi raporu bölümleri: temellere gelen sünek tasarım deprem tesirleri, radye zemin gerilmesi kontrolü ve
        /// gerilmeleri, temel aplikasyon planı. GPR'de yoksa sonuç dosyalarından üretilir (Zorla).
        /// </summary>
        public static List<GprBolum> Geoteknik(GprBelgesi g, Proje.ProjeKlasoru p)
        {
            var kalemler = g != null ? Dagit(g) : Olustur();
            Zorla(kalemler, p);
            return kalemler.Where(k => k.Sekme != "Diğer" && Array.IndexOf(GeoteknikKalemleri, k.Ad) >= 0)
                .OrderBy(k => Array.IndexOf(GeoteknikKalemleri, k.Ad))
                .SelectMany(k => k.Bolumler.OrderBy(b => b.No)).ToList();
        }

        /// <summary>Bölümleri kalemlere dağıtır. Eşleşmeyenler "Diğer" sekmesine düşer (rapordan sessizce kaybolmasın).</summary>
        public static List<RaporKalemi> Dagit(GprBelgesi g)
        {
            var kalemler = Olustur();
            var diger = new Dictionary<string, RaporKalemi>();
            var eslesen = new Dictionary<RaporKalemi, List<GprBolum>>();
            foreach (var b in g.Bolumler)
            {
                string a = GprBolumleri.Anahtar(b.Baslik);
                var kalem = kalemler.FirstOrDefault(x => x.Eslesir(b, a));
                if (kalem == null)
                {
                    if (!diger.TryGetValue(a, out kalem))
                    {
                        kalem = new RaporKalemi { Sekme = "Diğer", Ad = Modern.ModernBolumler.Tr(b.Baslik), Varsayilan = false, Eslesir = (_, __) => false };
                        diger[a] = kalem;
                        kalemler.Add(kalem);
                    }
                }
                if (!eslesen.TryGetValue(kalem, out var liste)) eslesen[kalem] = liste = new List<GprBolum>();
                liste.Add(b);
            }
            // aynı başlığın sürümlerinden biri: tablo (text) sürümü varsa o, yoksa ilk sürüm
            foreach (var (kalem, liste) in eslesen)
                foreach (var grup in liste.GroupBy(b => b.Anahtar))
                {
                    var tablo = grup.Where(b => !b.Grafik).ToList();
                    var secilen = tablo.Count > 0 ? tablo : grup.Take(1).ToList();
                    // tekrar baskı (aynı başlık, ayrı yazdırma işi) varsa ilk ardışık dizi alınır
                    var ilk = new List<GprBolum> { secilen[0] };
                    foreach (var b in secilen.Skip(1))
                        if (b.No == ilk[^1].No + 1 || ilk[^1].Sayfalar[^1].No + 1 >= b.Sayfalar[0].No) ilk.Add(b); else break;
                    kalem.Bolumler.AddRange(ilk);
                }
            foreach (var kalem in kalemler) kalem.Bolumler.Sort((x, y) => x.No.CompareTo(y.No));
            return kalemler;
        }
    }
}
