using System.Globalization;
using StatikRaporClaude.Dizgi;
using StatikRaporClaude.Gpr;
using StatikRaporClaude.Modern;
using StatikRaporClaude.Uretec;

namespace StatikRaporClaude.Rapor
{
    /// <summary>
    /// Kapaktan sonra gelen "Yapı teknik raporu": proje, yapı, malzeme, yük, deprem, zemin ve analiz bilgileri ile
    /// STA4 yapı yorumu (A statik, B ekonomi, C temel ve zemin + kolon/perde alan oranı). Değerler ST4, OPS, kapak
    /// bilgileri ve deprem raporundan (GPR'de varsa oradan, yoksa ST4 + FeaMod) alınır; hesap yapılmaz.
    /// </summary>
    public static class TeknikRapor
    {
        public const string Grup = "Teknik rapor";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static GprBolum Bolum() => new GprBolum { No = 980000, Anahtar = "TEKNIK RAPOR", Baslik = "Yapı teknik raporu", Grup = Grup };

        public static GprBolum YorumBolumu() => new GprBolum { No = 980001, Anahtar = RaporKatalogu.YorumAnahtari, Baslik = "Yapı yorumu", Grup = Grup };

        public static List<Blok> Bloklar(RaporSecenekleri o, GprBolum bolum)
        {
            var pk = o.Proje;
            var k = o.Kapak ?? new KapakBilgisi();
            var p = new Proj(pk.St4Yolu);
            var m = o.Veri?.Model ?? Veri.St4Model.Oku(pk.St4Yolu);
            var gprDeprem = o.Gpr?.Bolumler.FirstOrDefault(b => b.Anahtar == "DEPREM RAPORU" && !b.Grafik);
            var d = gprDeprem != null ? DepremRaporu.Oku(gprDeprem) : o.Veri != null ? DepremRaporu.Dosyadan(o.Veri) : null;
            string Dp(string e) => d?.Deger(e)?.Trim() ?? "—";
            string Ilk(string e) => Dp(e).Split(new[] { "   " }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "—";
            string Dolu(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s.Trim();
            string N(double v, string f) => v.ToString(f, Inv);

            var bl = new List<Blok>
            {
                new BolumBasiBlok { Bolum = bolum, Baslik = "Yapı teknik raporu", Aciklama = "Statik proje teknik raporu · değerler STA4-CAD proje ve analiz dosyalarından" },
            };
            // tüm alt bölümlerin değerleri tek sütunda hizalı
            var ortak = new List<(string Anahtar, string Deger)>();
            void Bolum(string baslik, IEnumerable<(string, string)> satirlar)
            {
                var s = satirlar.Where(x => !string.IsNullOrWhiteSpace(x.Item2) && x.Item2 != "—").ToList();
                if (s.Count == 0) return;
                ortak.AddRange(s);
                bl.Add(new BaslikBlok { Metin = baslik, Seviye = 1 });
                bl.Add(new AnahtarDegerBlok { Satirlar = s, Grup = ortak });
            }

            // 1. proje
            Bolum("1. Proje bilgileri", new[]
            {
                ("Proje", Dolu(pk.ProjeAdi)),
                ("Yapı sahibi", Dolu(k.YapiSahibi)),
                ("Yeri", Dolu(k.Yeri)),
                ("Pafta / ada / parsel", Dolu(k.PaftaAdaParsel)),
                ("Kullanım amacı", Dolu(k.KullanimAmaci)),
                ("Yapı sınıfı", Dolu(k.YapiSinifi)),
                ("İnşaat alanı", Dolu(k.InsaatAlani)),
                ("Statik proje müellifi", Dolu(string.IsNullOrWhiteSpace(k.MuellifAdi) ? pk.Firma : k.MuellifAdi)),
            });

            // 2. yapı
            var katlar = pk.Katlar.Select(x => $"{ModernBolumler.Tr(x.Ad)} ({N(x.Kot, "0.00")} m)").ToList();
            int perde = m.Kolonlar.Count(x => x.Poligon ? (x.PerdeX || x.PerdeY) : Math.Max(x.Bx, x.By) >= 6 * Math.Min(x.Bx, x.By));
            string davranis = Dp("YAPI DAVRANIŞ");
            string sistem = davranis.Contains("   ") ? davranis.Substring(davranis.IndexOf("   ", StringComparison.Ordinal)).Trim() : "";
            // "SÜNEKLİLİK DÜZEYİ SINIRLI (SS) Çerçeveli yapılar" → cümle düzeni, kısaltma büyük harf kalır
            if (sistem.Length > 0) sistem = System.Text.RegularExpressions.Regex.Replace(ModernBolumler.Tr(sistem), @"\((\w{2})\)", x => "(" + x.Groups[1].Value.ToUpper(new CultureInfo("tr-TR")) + ")");
            string hn = System.Text.RegularExpressions.Regex.Match(Dp("BİNA YÜKSEKLİK"), @"Hn\s*=\s*([\d.]+)").Groups[1].Value;
            string temel = pk.St4ElemanVar("/Slab foundations/") ? "Radye (plak) temel"
                : pk.St4ElemanVar("/Continuous foundations/") ? "Sürekli temel" : pk.St4ElemanVar("/Single footings/") ? "Tekil temel" : "";
            Bolum("2. Yapının genel tanımı", new[]
            {
                ("Kat adedi", Dolu(Sayfa7.S(p, 4, 1))),
                ("Katlar ve kotları", string.Join(" · ", katlar)),
                ("Bina yüksekliği (Hn)", hn.Length > 0 ? $"{hn} m" : ""),
                ("Taşıyıcı sistem", Dolu(sistem.Length > 0 ? sistem : "Betonarme çerçeve ve perde")),
                ("Eleman sayıları", $"{m.Kolonlar.Count - perde} kolon · {perde} perde · {m.Kirisler.Count} kiriş · {m.Dosemeler.Count} döşeme"),
                ("Temel sistemi", temel),
            });

            // 3. malzeme
            double fck = Sayfa7.Fck(p, (int)Sayfa7.SD(p, 6, 2)), fckT = Sayfa7.Fck(p, (int)Sayfa7.SD(p, 6, 3));
            double fy = Sayfa7.Fy(p, (int)Sayfa7.SD(p, 6, 4));
            string Celik(double f) => f >= 4900 ? "B500C" : f >= 4100 ? "B420C" : f >= 2100 && f < 2300 ? "S220" : $"fyk = {N(f, "0")} kg/cm²";
            int mi = p.Sec("/Materials/");
            Bolum("3. Malzeme", new[]
            {
                ("Beton (kiriş, kolon, perde, döşeme)", $"C{N(fck / 10, "0")}   (fck = {N(fck, "0")} kg/cm²)"),
                ("Beton (temel)", $"C{N(fckT / 10, "0")}   (fck = {N(fckT, "0")} kg/cm²)"),
                ("Donatı çeliği", $"{Celik(fy)}   (fyk = {N(fy, "0")} kg/cm²)"),
                ("Betonarme birim hacim ağırlığı", mi >= 0 ? $"{Proj.D(p.St4[mi + 1].Split(',')[3]).ToString("0.0#", Inv)} t/m³" : ""),
            });

            // 4. yükler
            Bolum("4. Yükler", new[]
            {
                ("Yük standardı", "TS 498 — yapı elemanlarının boyutlandırılmasında alınacak yüklerin hesap değerleri"),
                ("Hareketli yük katılım katsayısı (n)", N(Sayfa7.SD(p, 4, 9), "0.0#")),
                ("Döşeme ve duvar yükleri", "Döşeme ve kiriş yük analizi tablolarında"),
                ("Hareketli yük azaltma katsayısı (Cz)", N(Sayfa7.SD(p, 7, 6), "0.0#")),
            });

            // 5. deprem
            string per = d?.Tx is double tx && d.Ty is double ty ? $"Tx = {N(tx, "0.000")} s,  Ty = {N(ty, "0.000")} s" : "";
            Bolum("5. Deprem", new[]
            {
                ("Yönetmelik", "Türkiye Bina Deprem Yönetmeliği (TBDY 2018)"),
                ("Deprem yer hareketi düzeyi", Dp("DEPREM YER HAREKETİ")),
                ("Yerel zemin sınıfı", Ilk("ZEMİN SINIFI")),
                ("Ss / S1", Ilk("YEREL SPECTRAL")),
                ("SDS / SD1", Ilk("TASARIM SPECTRAL")),
                ("Bina kullanım sınıfı / önem katsayısı", Dp("BİNA KULLANIM")),
                ("Deprem tasarım sınıfı (DTS)", Ilk("DEPREM TASARIM SINIFI")),
                ("Bina yükseklik sınıfı (BYS)", Ilk("BİNA YÜKSEKLİK")),
                ("R / D", $"{Ilk("YAPI DAVRANIŞ")} / {Ilk("SİSTEM DAYANIM")}"),
                ("Hesap yöntemi", Dp("DEPREM ANALIZI") != "—" ? ModernBolumler.Tr(Dp("DEPREM ANALIZI")) : "Mod birleştirme yöntemi"),
                ("Hakim titreşim periyotları", per),
            });

            // 6. zemin ve temel
            int tm = (int)Sayfa7.SD(p, 8, 3);
            Bolum("6. Zemin ve temel", new[]
            {
                ("Zemin taşıma gücü tasarım gerilmesi", $"{N(Sayfa7.SD(p, 7, 3), "0.0")} t/m²"),
                ("Zemin yatak katsayısı", $"{N(Sayfa7.SD(p, 7, 2), "0")} t/m³"),
                ("Temel analizi", tm == 0 ? "Temeller dikkate alınmadan yapı analizi" : tm == 1 ? "Yapı ile birlikte, temel dönmeleri dikkate alınarak" : "Yapı ile birlikte, tüm temel deplasmanları dikkate alınarak"),
            });

            // 7. analiz ve tasarım
            Bolum("7. Analiz ve betonarme tasarım", new[]
            {
                ("Yazılım", Dolu(pk.Surum)),
                ("Statik analiz", Sayfa7.SD(p, 13, 4) == 1 ? $"FEA3D doğrusal sonlu eleman analizi (mesh {Sayfa7.MeshS(p)} m)" : "FRAME3D doğrusal çerçeve analizi"),
                ("Kesit rijitlikleri", "Çatlamış kesit etkin rijitlik çarpanları (TBDY 2018 Tablo 4.2)"),
                ("Betonarme hesap", "Taşıma gücü yöntemi, TS 500-2000"),
                ("Uygulanan yönetmelikler", "TBDY 2018 · TS 500-2000 · TS 498 · TS 708"),
            });
            return bl;
        }
    }
}
