using System.Globalization;
using System.Text.RegularExpressions;
using StatikRaporClaude.Dizgi;
using StatikRaporClaude.Gpr;
using StatikRaporClaude.Modern;
using StatikRaporClaude.Uretec;

namespace StatikRaporClaude.Rapor
{
    /// <summary>
    /// Zemin etüdü raporunu hazırlayacak jeoloji/jeoteknik mühendisine verilen yapı bilgi raporu. İçerik TBDY 2018
    /// 16.2 (zemin araştırmaları, zemin ve temel etüt raporları) ve Ek 16A (sondaj sayısı/yeri/derinliği) esas alınarak
    /// seçildi: proje ve konum, yapı ve temel bilgileri, temele aktarılan yükler ve zemin gerilmeleri, projede kabul edilen
    /// zemin parametreleri, asgari zemin araştırması ve etüt raporundan beklenen değerler. Değerler ST4, OPS, .YORUM, RAD/DE1
    /// ve deprem raporundan alınır; Ek 16A sondaj sayısı ve 1.5·B derinliği dışında hesap yapılmaz.
    /// </summary>
    public static class GeoteknikRapor
    {
        public const string Grup = "Geoteknik";
        public const string Baslik = "Zemin etüdü için yapı bilgileri";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static GprBolum Bolum() => new GprBolum { No = 980010, Anahtar = "GEOTEKNIK YAPI BILGILERI", Baslik = Baslik, Grup = Grup };

        public static GprBolum YorumBolumu() => new GprBolum { No = 980011, Anahtar = RaporKatalogu.YorumAnahtari, Baslik = "Yapı yorumu", Grup = Grup };

        public static List<Blok> Bloklar(RaporSecenekleri o, GprBolum bolum)
        {
            var pk = o.Proje;
            var k = o.Kapak ?? new KapakBilgisi();
            var p = new Proj(pk.St4Yolu);
            var m = o.Veri?.Model ?? Veri.St4Model.Oku(pk.St4Yolu);
            var gprDeprem = o.Gpr?.Bolumler.FirstOrDefault(b => b.Anahtar == "DEPREM RAPORU" && !b.Grafik);
            var d = gprDeprem != null ? DepremRaporu.Oku(gprDeprem) : o.Veri != null ? DepremRaporu.Dosyadan(o.Veri) : null;
            string Dp(string e) => d?.Deger(e)?.Trim() ?? "";
            string Ilk(string e) => Dp(e).Split(new[] { "   " }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            string N(double v, string f) => v.ToString(f, Inv);
            string Dolu(string s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();

            // "TEMELLERE KOLONLARDAN GELEN, SÜNEK TASARIM DEPREM TESİRLERİ" üretecinin başlık satırları (temel alanı, boyut, kot, BYS…)
            var ust = new Dictionary<string, string>();
            foreach (var ham in SanalBolum.Satirlar("URETEC TEMEL SUNEK", pk.St4Yolu) ?? new List<string>())
            {
                string s = StaKodlama.Unicode(ham);
                if (s.StartsWith("Kolonlarda")) break;
                int i = s.IndexOf(": ", StringComparison.Ordinal);
                if (i > 0) ust[Regex.Replace(s.Substring(0, i), @"\s+", " ").Trim()] = s.Substring(i + 2).Trim();
            }
            string U(string anahtar) => ust.FirstOrDefault(x => x.Key.StartsWith(anahtar, StringComparison.Ordinal)).Value ?? "";

            // .YORUM: toplam yapı yükü, temele gelen yük, efektif zemin gerilmesi
            string yorum = string.Join("\n", SanalBolum.Satirlar(RaporKatalogu.YorumAnahtari, pk.St4Yolu) ?? new List<string>());
            string Y(string desen) { var x = Regex.Match(yorum, desen); return x.Success ? x.Groups[1].Value : ""; }
            string yapiYuku = Y(@"Toplam yapı yükü\s*=\s*([\d.]+)"), temelYuku = Y(@"Toplam temel gelen yük\s*=\s*([\d.]+)");
            string gzef = Y(@"Gzef\s*=[^=]*=\s*([\d.]+)"), yapiAlani = Y(@"Toplam yapı alanı\s*=\s*([\d.]+)");

            // radye: en büyük zemin gerilmeleri ve düşey yerdeğiştirme (Winkler modeli)
            Veri.RadyeVerisi r = null;
            try { if (Veri.RadyeVerisi.Var(pk.St4Yolu)) r = Veri.RadyeVerisi.Oku(pk.St4Yolu); } catch { r = null; }
            double sGQ = 0, sTasarim = 0, sDeprem = 0, wGQ = 0;
            if (r != null)
                foreach (int n in r.Dugumler)
                {
                    double s(int vaka) => r.Ko * r.W(n, vaka);
                    double g = s(0), q = Enumerable.Range(1, 6).Max(s);
                    double e = (new[] { 8, 9, 10, 11 }.Max(x => Math.Abs(s(x))) + 0.3 * Math.Abs(s(16))) / 2;
                    sGQ = Math.Max(sGQ, g + q); sTasarim = Math.Max(sTasarim, 1.4 * g + 1.6 * q); sDeprem = Math.Max(sDeprem, g + q + e);
                    wGQ = Math.Max(wGQ, r.W(n, 0) + Enumerable.Range(1, 6).Max(x => r.W(n, x)));
                }

            var bl = new List<Blok>
            {
                new BolumBasiBlok { Bolum = bolum, Baslik = Baslik,
                    Aciklama = "Zemin ve temel etüdü (TBDY 2018 16.2, Ek 16A) için statik projeden yapı, temel ve yük bilgileri · değerler STA4-CAD dosyalarından" },
            };
            var ortak = new List<(string Anahtar, string Deger)>();
            void Bolum(string baslik, IEnumerable<(string, string)> satirlar)
            {
                var s = satirlar.Where(x => !string.IsNullOrWhiteSpace(x.Item2) && x.Item2 != "—").ToList();
                if (s.Count == 0) return;
                ortak.AddRange(s);
                bl.Add(new BaslikBlok { Metin = baslik, Seviye = 1 });
                bl.Add(new AnahtarDegerBlok { Satirlar = s, Grup = ortak });
            }
            void Maddeler(IEnumerable<string> maddeler)
            {
                foreach (var t in maddeler)
                    bl.Add(new MetinBlok { Satirlar = new List<MetinSatiri> { new MetinSatiri { Parcalar = { (0, "•  " + t) } } } });
            }

            // 1. proje ve konum
            string koord = m.Enlem != 0 || m.Boylam != 0 ? $"{N(m.Enlem, "0.0000#")}° K  /  {N(m.Boylam, "0.0000#")}° D" : U("BİNA KOORDİNATI");
            Bolum("1. Proje ve konum", new[]
            {
                ("Proje", Dolu(pk.ProjeAdi)),
                ("Yapı sahibi", Dolu(k.YapiSahibi)),
                ("Yeri", Dolu(k.Yeri)),
                ("Pafta / ada / parsel", Dolu(k.PaftaAdaParsel)),
                ("Bina koordinatı (enlem / boylam)", koord),
                ("Statik proje müellifi", Dolu(string.IsNullOrWhiteSpace(k.MuellifAdi) ? pk.Firma : k.MuellifAdi)),
            });

            // 2. yapı
            // çok katlı yapıda satıra sığsın: ilk iki ve son iki kat
            var kl = pk.Katlar.Select(x => $"{ModernBolumler.Tr(x.Ad)} {N(x.Kot, "0.00")}").ToList();
            if (string.Join(" · ", kl).Length > 90 && kl.Count > 4) kl = kl.Take(2).Append("…").Concat(kl.Skip(kl.Count - 2)).ToList();
            var katlar = new List<string> { string.Join(" · ", kl) + " m" };
            string davranis = Dp("YAPI DAVRANIŞ");
            string sistem = davranis.Contains("   ") ? davranis.Substring(davranis.IndexOf("   ", StringComparison.Ordinal)).Trim() : "";
            if (sistem.Length > 0) sistem = Regex.Replace(ModernBolumler.Tr(sistem), @"\((\w{2})\)", x => "(" + x.Groups[1].Value.ToUpper(new CultureInfo("tr-TR")) + ")");
            string bys = U("BİNA YÜKSEKLİK SINIFI"), bysNo = Regex.Match(bys, @"^\d+").Value;
            string hn = Regex.Match(bys + " " + Dp("BİNA YÜKSEKLİK"), @"Hn\s*=\s*([\d.]+)").Groups[1].Value;
            Bolum("2. Yapı bilgileri", new[]
            {
                ("Kullanım amacı", Dolu(k.KullanimAmaci)),
                ("Yapı sınıfı", Dolu(k.YapiSinifi)),
                ("Kat adedi", U("YAPI KAT ADEDİ")),
                ("Katlar ve kotları", string.Join(" · ", katlar)),
                ("Bina yüksekliği (Hn)", hn.Length > 0 ? $"{hn} m" : ""),
                ("Bina yükseklik sınıfı (BYS)", bysNo),
                ("Bina kullanım sınıfı (BKS) / önem katsayısı", U("BİNA KULLANIM SINIFI").Replace("   ", "  ·  ")),
                ("Deprem tasarım sınıfı (DTS)", U("DEPREM TASARIM SINIFI")),
                ("Taşıyıcı sistem", sistem),
                ("Toplam inşaat alanı", U("TOPLAM İNŞAAT ALANI").Length > 0 ? U("TOPLAM İNŞAAT ALANI") : yapiAlani.Length > 0 ? yapiAlani + " m²" : ""),
                ("Bina oturum alanı", U("TOPLAM BİNA OTURUM ALANI")),
            });

            // 3. temel
            string temel = pk.St4ElemanVar("/Slab foundations/") ? "Radye (plak) temel"
                : pk.St4ElemanVar("/Continuous foundations/") ? "Sürekli temel" : pk.St4ElemanVar("/Single footings/") ? "Tekil temel" : "";
            var boyut = Regex.Match(U("TEMEL BOYUTLARI"), @"([\d.]+)\s*/\s*([\d.]+)");
            var kot = Regex.Match(U("TEMEL KOTLARI"), @"(-?[\d.]+)\s*/\s*(-?[\d.]+)");
            double bx = boyut.Success ? double.Parse(boyut.Groups[1].Value, Inv) : 0, by = boyut.Success ? double.Parse(boyut.Groups[2].Value, Inv) : 0;
            double kotAlt = kot.Success ? double.Parse(kot.Groups[1].Value, Inv) : double.NaN;
            int tm = (int)Sayfa7.SD(p, 8, 3);
            Bolum("3. Temel bilgileri", new[]
            {
                ("Temel sistemi", temel),
                ("Temel boyutları (X × Y)", boyut.Success ? $"{boyut.Groups[1].Value} m × {boyut.Groups[2].Value} m" : ""),
                ("Temel taban alanı", U("RADYE TEMEL TABAN ALANI") is var ra && !ra.StartsWith("0.0 ") ? ra : ""),
                ("Temel taban kotu / üst kotu (±0.00'a göre)", kot.Success ? $"{kot.Groups[1].Value} m  /  {kot.Groups[2].Value} m" : ""),
                ("Temel analizi", tm == 0 ? "Temeller dikkate alınmadan yapı analizi" : tm == 1 ? "Yapı ile birlikte, temel dönmeleri dikkate alınarak" : "Yapı ile birlikte, tüm temel deplasmanları dikkate alınarak"),
                ("Zemin modeli", r != null ? "Winkler yayları (yatak katsayısı Ko), sonlu eleman radye" : ""),
            });

            // 4. yükler ve gerilmeler
            Bolum("4. Temele aktarılan yükler ve zemin gerilmeleri", new[]
            {
                ("Toplam yapı yükü (G + n·Q)", yapiYuku.Length > 0 ? $"{yapiYuku} t" : ""),
                ("Temele gelen toplam yük", temelYuku.Length > 0 ? $"{temelYuku} t" : ""),
                ("Ortalama zemin gerilmesi (net temel basıncı)", gzef.Length > 0 ? $"{gzef} t/m²" : ""),
                ("En büyük zemin gerilmesi, G + Q (servis)", r != null ? $"{N(sGQ, "0.00")} t/m²" : ""),
                ("En büyük zemin gerilmesi, 1.4 G + 1.6 Q", r != null ? $"{N(sTasarim, "0.00")} t/m²" : ""),
                ("En büyük zemin gerilmesi, G + Q + E", r != null ? $"{N(sDeprem, "0.00")} t/m²" : ""),
                ("En büyük düşey yerdeğiştirme, G + Q (Winkler)", r != null ? $"{N(wGQ * 1000, "0.0")} mm" : ""),
            });
            if (yapiYuku.Length + gzef.Length > 0 || r != null)
                bl.Add(new MetinBlok { Satirlar = new List<MetinSatiri> { new MetinSatiri { Parcalar = { (0,
                    "Kolon ve perdelerden temele gelen yükler (TBDY 2018 4.10.3 sünek tasarım deprem tesirleri) ve radye zemin gerilmeleri düğüm düğüm ilerideki tablolardadır. Winkler yerdeğiştirmesi yatak katsayısı kabulüne bağlı model değeridir; oturma hesabı yerine geçmez.") } } } });

            // 5. kabuller
            string zs = Ilk("ZEMİN SINIFI");
            Bolum("5. Statik hesapta kabul edilen zemin ve deprem parametreleri", new[]
            {
                ("Zemin taşıma gücü tasarım gerilmesi (qt)", U("ZEMİN TAŞIMA GÜCÜ").Replace("(t/m²)", "t/m²")),
                ("Zemin yatak katsayısı (Ko)", U("ZEMİN YATAK KATSAYISI").Replace("(t/m³)", "t/m³")),
                ("Yerel zemin sınıfı", zs.Length > 0 ? zs : U("ZEMİN SINIFI")),
                ("Deprem yer hareketi düzeyi", Dp("DEPREM YER HAREKETİ").Length > 0 ? Dp("DEPREM YER HAREKETİ") : U("DEPREM YER HAREKETİ")),
                ("Ss / S1", U("YEREL SPECTRAL")),
                ("SDS / SD1", Regex.Replace(U("TASARIM SPECTRAL"), @"\s+DD\d$", "")),
                ("Hakim titreşim periyotları", d?.Tx is double tx && d.Ty is double ty ? $"Tx = {N(tx, "0.000")} s,  Ty = {N(ty, "0.000")} s" : ""),
            });
            bl.Add(new MetinBlok { Satirlar = new List<MetinSatiri> { new MetinSatiri { Parcalar = { (0,
                "Bu değerler statik hesapta kullanılan ön kabullerdir. Zemin ve temel etüt raporunda belirlenen değerler bunlardan elverişsiz çıkarsa statik hesap etüt değerleriyle yenilenecektir.") } } } });

            // 6. Ek 16A asgari zemin araştırması
            double alan = bx * by;
            var alanM = Regex.Match(U("RADYE TEMEL TABAN ALANI") + " " + U("TOPLAM BİNA OTURUM ALANI"), @"([\d.]+)\s*m²");
            if (alanM.Success && double.Parse(alanM.Groups[1].Value, Inv) > 0) alan = double.Parse(alanM.Groups[1].Value, Inv);
            if (alan > 0)
            {
                int sayi = (int)Math.Ceiling(alan / 300.0);
                var gerekce = new List<string> { $"{N(alan, "0.#")} m² / 300 m² → {sayi}" };
                if (int.TryParse(bysNo, out int b) && b <= 3 && sayi < 3) { sayi = 3; gerekce.Add("BYS ≤ 3 → en az 3"); }
                if (alan > 1000 && sayi < 5) { sayi = 5; gerekce.Add("taban alanı > 1000 m² → en az 5"); }
                double gen = Math.Min(bx, by), der = 1.5 * gen;
                Bolum("6. Asgari zemin araştırması (TBDY2018 Ek 16A)", new[]
                {
                    ("Sondaj sayısı (16A.1.2)", $"en az {sayi}   ({string.Join("; ", gerekce)})"),
                    ("Sondaj yerleri (16A.1.3)", "Yapı planının köşeleri ve ortası"),
                    ("Sondaj derinliği (16A.1.4)", gen > 0 ? $"temel tabanından en az 1.5 × B = 1.5 × {N(gen, "0.0#")} = {N(der, "0.0")} m"
                        + (double.IsNaN(kotAlt) ? "" : $"   (±0.00'dan ≈ {N(Math.Abs(kotAlt) + der, "0.0")} m)") : ""),
                });
                bl.Add(new MetinBlok { Satirlar = new List<MetinSatiri> { new MetinSatiri { Parcalar = { (0,
                    "B: yapı (temel) genişliği, kısa kenar. Derinlik, net temel basıncından doğan gerilme artışının zeminin efektif gerilmesinin %10'una indiği derinlikle (Δσ = 0.10·σ'vo) karşılaştırılıp daha elverişsizi alınır; sığda yüksek dayanımlı tabaka varsa bu tabakaya en az 3 m girilerek sonlandırılabilir. Kesin program etüt müellifince belirlenir.") } } } });
            }

            // 7. etüt raporundan beklenenler
            bl.Add(new BaslikBlok { Metin = "7. Zemin ve temel etüt raporunda istenen bilgiler (TBDY2018 16.2.2)", Seviye = 1 });
            Maddeler(new[]
            {
                "Arazi zemin modeli: zemin tabakaları, kalınlıkları ve geoteknik tasarım parametreleri; sondaj ve muayene çukuru logları, zemin kesitleri.",
                "Yerel zemin sınıfı (16.4, Tablo 16.1: VS30 / N60 / cu); ZF sınıfında sahaya özel zemin davranış analizi (16.5).",
                "Temel sistemi önerisi ve temel derinliği; bu yapı için seçilen " + (temel.Length > 0 ? temel.ToLower(new CultureInfo("tr-TR")) : "temel") + " ve temel taban kotuna göre değerlendirme.",
                "Zemin taşıma gücü tasarım gerilmesi (qt) — statik ve deprem durumu için ayrı ayrı.",
                "Düşey yatak katsayısı (Ko) önerisi.",
                "Kısa ve uzun süreli oturma ve farklı oturma değerleri (net temel basıncına göre).",
                "Yeraltı su seviyesi; kaldırma kuvveti ve su yalıtımı gereksinimi.",
                "Sıvılaşma potansiyeli değerlendirmesi (16.6); şişen / göçen zemin varsa önlemler.",
                "Kazı ve şev duraylılığı, gerekiyorsa iksa önerisi; komşu yapılar ve yol etkisi.",
                "Zemin iyileştirmesi veya kazıklı temel gerekiyorsa tasarım parametreleri.",
            });
            return bl;
        }
    }
}
