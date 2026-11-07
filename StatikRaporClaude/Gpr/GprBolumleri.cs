namespace StatikRaporClaude.Gpr
{
    public sealed class GprBolum
    {
        public int No;
        public string Anahtar;    // başlığın sade karşılaştırma anahtarı (aynı başlıklı tekrarlar ortak)
        public string Baslik;
        public string Grup;
        public bool Grafik;       // STA4 "grafik output" sayfası (-11 başlık)
        public bool Radye;
        public readonly List<GprSayfa> Sayfalar = new List<GprSayfa>();
        /// <summary>İlk sayfada bölümün başladığı y (0 = sayfa başı); son sayfada bittiği y (∞ = sayfa sonu).</summary>
        public float IlkY;
        public float SonY = float.MaxValue;

        public float UstSinir(GprSayfa s) => s == Sayfalar[0] ? IlkY : 0f;
        public float AltSinir(GprSayfa s) => s == Sayfalar[^1] ? SonY : float.MaxValue;
        public bool Icinde(GprSayfa s, float y) => y >= UstSinir(s) && y < AltSinir(s);

        public string SayfaAraligi => Sayfalar.Count == 1 ? $"s.{Sayfalar[0].No}" : $"s.{Sayfalar[0].No}–{Sayfalar[^1].No}";
    }

    /// <summary>
    /// GPR sayfalarını STA4CAD içindekiler kuralıyla bölümlere ayırır:
    /// her sayfada, son içindekiler girdisinden farklı olan ilk başlık (tip -10/-11/-12) yeni girdi açar.
    /// Başlık sayfa ortasındaysa sayfanın üstü önceki bölüme de aittir.
    /// Kural N_ULK_02 tam raporunun (1518 sayfa) STA4 içindekiler sayfasıyla karşılaştırılarak çıkarıldı.
    /// </summary>
    public static class GprBolumleri
    {
        public static readonly string[] GrupSirasi =
        {
            "Genel bilgiler", "Deprem", "Yük bilgileri", "Döşeme", "Kiriş", "Kolon ve perde", "Temel",
            "Metraj", "Özel tablolar", "Planlar ve görünüşler", "FEA3D analizi", "Radye temel"
        };

        private const float UstSinir = 450f;

        public static void Ata(GprBelgesi b)
        {
            b.Bolumler.Clear();
            GprBolum son = null;
            bool radye = false, fea = false;
            foreach (var s in b.Sayfalar)
            {
                var basliklar = s.Yazilar
                    .Where(t => (t.Tip == -10 || t.Tip == -11 || t.Tip == -12) && t.Y > 130 && t.Metin.Trim().Length > 3)
                    .ToList();
                GprYazi yeni = basliklar.FirstOrDefault(t => son == null || Anahtar(t.Metin) != son.Anahtar);
                if (yeni != null)
                {
                    string baslik = Sadelestir(yeni.Metin);
                    string anahtar = Anahtar(baslik);
                    if (anahtar.Contains("RADYE PROGRAMI") || anahtar.Contains("RADYE SONLU")) radye = true;
                    if (anahtar.Contains("3D SONLU ELEMANLARLA")) fea = true;
                    else if (fea && !FeaBasligi(anahtar)) fea = false;

                    // Sayfanın üstünde önceki bölümün devamı varsa sayfa iki bölüme de girer; sınır başlığın hemen üstü
                    float sinir = 0f;
                    if (son != null && yeni.Y > UstSinir)
                    {
                        sinir = yeni.Y - 12f;
                        if (son.Sayfalar.LastOrDefault() != s) son.Sayfalar.Add(s);
                        son.SonY = sinir;
                    }

                    son = new GprBolum
                    {
                        No = b.Bolumler.Count, Anahtar = anahtar, Baslik = baslik,
                        Grafik = yeni.Tip == -11, Radye = radye, IlkY = sinir
                    };
                    son.Grup = radye ? "Radye temel" : fea ? "FEA3D analizi" : GrupBul(anahtar);
                    b.Bolumler.Add(son);
                    s.Baslik = baslik;
                }
                else if (son == null)
                {
                    // Başlıksız ilk sayfa (ör. radye düğüm planı)
                    son = new GprBolum { No = 0, Anahtar = "GIRIS", Baslik = "Giriş", Grup = "Genel bilgiler" };
                    b.Bolumler.Add(son);
                }
                else if (!radye && s.Yazilar.Any(t => Anahtar(t.Metin).Contains("RADYE ANALIZI")))
                {
                    radye = true;
                    son = new GprBolum
                    {
                        No = b.Bolumler.Count, Anahtar = "RADYE DUGUM PLANI", Baslik = "RADYE SONLU ELEMAN DÜĞÜM PLANI",
                        Grup = "Radye temel", Radye = true
                    };
                    b.Bolumler.Add(son);
                    s.Baslik = son.Baslik;
                }
                son.Sayfalar.Add(s);
            }
            foreach (var bolum in b.Bolumler)
                foreach (var s in bolum.Sayfalar) s.Bolumler.Add(bolum);
        }

        private static bool FeaBasligi(string a) =>
            a.Contains("KOORDINAT") || a.Contains("MESH") || a.Contains("MOD VEKTOR") || a.Contains("NOKTALARIN STATIK") ||
            a.Contains("STATIK SONUCLARI T TM") || a.Contains("NOKTA DEPLASMAN") || a.Contains("3D SONLU");

        private static string GrupBul(string a)
        {
            if (a.Contains("APLIKASYON") || a.Contains("3D") || a.Contains("GORUNUS")) return "Planlar ve görünüşler";
            if (a.Contains("METRAJ")) return "Metraj";
            if (a.Contains("YORUM") || a.Contains("SUNEKLILIK ALAN") || a.Contains("IMAR YONETMELIGI") || a.Contains("DENGELI DONATI")
                || a.Contains("TASARIM DEPREM TESIRLERI") || a.Contains("TASIMA GUCU") || a == "ZEMIN GERILMESI") return "Özel tablolar";
            if (a.Contains("DEPREM RAPORU") || a.Contains("MODAL") || a.Contains("DUZENSIZ") || a.Contains("DEVRILME")
                || a.Contains("TABAN MOMENT") || a.Contains("NOKTA DEPLASMAN") || a.Contains("BURKULMA") || a.Contains("DIYAFRAM")) return "Deprem";
            if (a.Contains("YUK BILGI") || a.Contains("YUK ANALIZI")) return "Yük bilgileri";
            if (a.Contains("TEMEL")) return "Temel";
            if (a.Contains("DOSEME")) return "Döşeme";
            if (a.Contains("KIRIS")) return "Kiriş";
            if (a.Contains("KOLON") || a.Contains("PERDE"))
                return a.Contains("OPSIYON") || a.Contains("EKSANTRISITE") ? "Genel bilgiler" : "Kolon ve perde";
            return "Genel bilgiler";
        }

        public static string Anahtar(string s) => StaKodlama.Anahtar(s);

        private static string Sadelestir(string s)
        {
            s = s.Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s;
        }
    }
}
