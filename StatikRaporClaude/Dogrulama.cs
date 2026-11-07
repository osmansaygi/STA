using System.Drawing;
using System.Drawing.Imaging;
using StatikRaporClaude.Cizim;
using StatikRaporClaude.Gpr;
using StatikRaporClaude.Proje;
using StatikRaporClaude.Rapor;

namespace StatikRaporClaude
{
    /// <summary>Komut satırı doğrulaması: bölüm listesi, sayfa PNG'leri ve rapor PDF'i.</summary>
    internal static class Dogrulama
    {
        public static void PngYaz(string st4, string klasor, int[] siralar)
        {
            Directory.CreateDirectory(klasor);
            var p = ProjeKlasoru.Ac(st4);
            var g = GprBelgesi.Oku(p.GprYolu);
            var rapor = new List<string>
            {
                $"GPR {g.Sayfalar.Count} sayfa, firma={g.Firma}, proje={g.Proje}, tarih={g.RaporTarihi}, okunamayan satır={g.OkunamayanSatir}"
            };
            foreach (var d in p.Dosyalar) rapor.Add($"DOSYA {d.Ad} {d.Tarih:dd.MM.yyyy HH:mm} {d.Uyari}");
            foreach (var b in g.Bolumler)
                rapor.Add($"[{b.Grup}]{(b.Grafik ? " (grafik)" : "")} {b.Baslik} : {b.SayfaAraligi} ({b.Sayfalar.Count}) y {b.IlkY:0}..{(b.SonY > 1e6 ? "son" : b.SonY.ToString("0"))}");
            File.WriteAllLines(Path.Combine(klasor, "bolumler.txt"), rapor);
            File.WriteAllLines(Path.Combine(klasor, "icindekiler.txt"),
                g.Sayfalar.Where(s => s.Baslik != null).Select(s => $"{s.No}\t{GprBolumleri.Anahtar(s.Baslik)}"));

            foreach (int sira in siralar.Where(i => i >= 0 && i < g.Sayfalar.Count))
            {
                using var bmp = new Bitmap(1100, 1555);
                using (var gr = Graphics.FromImage(bmp))
                {
                    gr.Clear(Color.White);
                    GprCizici.Ciz(new GdiYuzey(gr), g.Sayfalar[sira], new RectangleF(0, 0, bmp.Width, bmp.Height));
                }
                bmp.Save(Path.Combine(klasor, $"gpr{sira:D3}.png"), ImageFormat.Png);
            }
        }

        /// <summary>Varsayılan seçimle A3 yatay karşılaştırma: sol GPR sayfası, sağ bu programın aynı bölümü.</summary>
        public static void YanYanaYaz(string st4, string cikti)
        {
            var p = ProjeKlasoru.Ac(st4);
            var g = GprBelgesi.Oku(p.GprYolu);
            int n = Rapor.YanYanaRapor.Olustur(new RaporSecenekleri
            {
                Proje = p, Gpr = g, SeciliBolumler = VarsayilanSecim(g), Kapak = KapakBilgisi.Yukle(st4),
                CiktiYolu = cikti, ModernTablolar = true, Veri = Veri.StaVeri.Ac(st4),
                Bolumler = RaporKatalogu.Sirala(RaporKatalogu.Dagit(g).SelectMany(k => k.Bolumler))
            });
            File.WriteAllText(Path.ChangeExtension(cikti, ".txt"), $"Yan yana {n} sayfa (A3 yatay). Sol: STA4 GPR. Sağ: bu program.\n");
        }

        public static void PdfYaz(string st4, string cikti, string katlar = null)
        {
            Modern.StaResimleri.Tema = Modern.StaResimTemasi.Bul(Environment.GetEnvironmentVariable("STATIK_TEMA"));
            var p = ProjeKlasoru.Ac(st4);
            var g = GprBelgesi.Oku(p.GprYolu);
            var icerik = RaporKatalogu.GprIcerigi(g, p);   // arayüzle aynı: GPR içeriği + yapı yorumu
            bool geo = Environment.GetEnvironmentVariable("STATIK_GEO") == "1";
            var bolumler = geo ? RaporKatalogu.Geoteknik(g, p) : RaporKatalogu.Sirala(icerik.SelectMany(k => k.Bolumler));
            var secim = bolumler.Select(b => b.No).ToHashSet();
            var gunluk = new List<string>();
            var s = RaporDerleyici.Olustur(new RaporSecenekleri
            {
                Proje = p, Gpr = g, SeciliBolumler = secim, Kapak = KapakBilgisi.Yukle(st4), CiktiYolu = cikti,
                Katlar = katlar == null ? null : katlar.Split(',').ToHashSet(),
                PngKlasoru = Environment.GetEnvironmentVariable("STATIK_PNG"),
                TeknikRapor = Environment.GetEnvironmentVariable("STATIK_TEKNIK") != "0",
                Geoteknik = geo,
                Bolumler = bolumler,
                Veri = Veri.StaVeri.Ac(st4)
            }, new Progress<string>(m => gunluk.Add(m)));
            var kontrol = SayiKontrolu(g, g.Bolumler.Where(b => secim.Contains(b.No) && !Modern.ModernBolumler.Destekler(b)));
            File.WriteAllText(Path.ChangeExtension(cikti, ".txt"),
                $"Toplam {s.ToplamSayfa} sayfa: kapak+içindekiler {s.KapakVeIcindekiler}, modern {s.ModernSayfa}, akış {s.AkisSayfa}, STA4 {s.Sta4Sayfa}, ek {s.EkSayfa}; {s.Bolum} bölüm\n"
                + string.Join("\n", kontrol) + "\n\nKatalog:\n" + string.Join("\n", KatalogDokumu(g)) + "\n");
        }

        /// <summary>GPR'yi hiç okumadan rapor: yalnız sonuç dosyalarından üretilebilen varsayılan kalemler.</summary>
        /// <summary>
        /// GPR'siz metin üreteçleri ↔ GPR: kalemin GPR bölümlerindeki tablo veri satırları (rakam içeren ¡ satırları, hücreler
        /// kırpılmış) ile üreteç satırları çoklu küme olarak karşılaştırılır. Sayfa başı tekrarları sayılmaz.
        /// </summary>
        public static void UretecKarsilastir(string st4, string cikti, string suzgec = null)
        {
            var p = ProjeKlasoru.Ac(st4);
            var g = GprBelgesi.Oku(p.GprYolu);
            var kalemler = RaporKatalogu.Dagit(g);
            string Satir(string ham) => string.Join("|", ham.Trim().Trim('¡').Split('¡').Select(x => x.Trim()));
            bool Veri(string ham) => ham.TrimStart().StartsWith("¡") && ham.Any(char.IsDigit);
            var rapor = new List<string>();
            foreach (var k in kalemler)
            {
                var anahtarlar = RaporKatalogu.UretimAnahtarlari(k).Where(Uretec.SanalBolum.Destekler).ToList();
                if (anahtarlar.Count == 0 || (suzgec != null && !k.Ad.Contains(suzgec))) continue;
                var gpr = k.Bolumler.SelectMany(b => b.Sayfalar.SelectMany(s => s.Yazilar.Where(y => b.Icinde(s, y.Y)).OrderBy(y => y.Y).Select(y => y.Ham)))
                    .Where(h => h != null && Veri(h)).Select(Satir).ToList();
                var uret = anahtarlar.SelectMany(a => Uretec.SanalBolum.Satirlar(a, st4) ?? new List<string>()).Where(Veri).Select(Satir).ToList();
                var kume = gpr.GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
                int tutan = 0; var farklar = new List<string>();
                foreach (var u in uret)
                {
                    if (kume.TryGetValue(u, out int c) && c > 0) { kume[u] = c - 1; tutan++; }
                    else if (farklar.Count < 8) farklar.Add("  üretilen: " + u);
                }
                var kalan = kume.Where(x => x.Value > 0).SelectMany(x => Enumerable.Repeat(x.Key, x.Value)).ToList();
                rapor.Add($"[{k.Sekme}] {k.Ad}: üretilen {uret.Count}, GPR {gpr.Count}, birebir {tutan}, GPR'de kalan {kalan.Count}");
                rapor.AddRange(farklar);
                rapor.AddRange(kalan.Take(8).Select(x => "  GPR: " + x));
            }
            File.WriteAllLines(cikti, rapor);
        }

        public static void PdfGprsiz(string st4, string cikti, bool hepsi = false)
        {
            var p = ProjeKlasoru.Ac(st4);
            var kalemler = RaporKatalogu.Olustur();
            RaporKatalogu.Zorla(kalemler, p);
            var secili = kalemler.Where(k => k.Dosyadan && (hepsi || k.Varsayilan)).ToList();
            var s = RaporDerleyici.Olustur(new RaporSecenekleri
            {
                Proje = p, Gpr = null, Kapak = KapakBilgisi.Yukle(st4), CiktiYolu = cikti, Veri = Veri.StaVeri.Ac(st4),
                SeciliBolumler = secili.SelectMany(k => k.Bolumler).Select(b => b.No).ToHashSet(),
                Bolumler = RaporKatalogu.Sirala(kalemler.SelectMany(k => k.Bolumler))
            });
            File.WriteAllText(Path.ChangeExtension(cikti, ".txt"),
                $"GPR'siz: {s.ToplamSayfa} sayfa (modern {s.ModernSayfa}, akış {s.AkisSayfa}, STA4 {s.Sta4Sayfa})\n" +
                string.Join("\n", kalemler.Select(k => $"[{k.Sekme}] {(k.Dosyadan ? (k.Varsayilan ? "X" : "o") : " ")} {k.Ad}: " +
                    (k.Dosyadan ? string.Join(", ", k.Bolumler.Select(b => b.Baslik)) : RaporKatalogu.PasifNedeni(k, p, false)))) + "\n");
        }

        /// <summary>
        /// Yeniden dizgide sayı kaybı kontrolü: her bölümde kaynak yazılardaki sayısal belirteçler (çoklu küme)
        /// ayrıştırılmış öğelerde aynen bulunmalı. Eksik ve fazla belirteçler raporlanır.
        /// </summary>
        public static List<string> SayiKontrolu(GprBelgesi g, IEnumerable<GprBolum> bolumler)
        {
            var rx = new System.Text.RegularExpressions.Regex(@"-?\d+(?:[.,]\d+)?");
            IEnumerable<string> Sayilar(string m) => rx.Matches(m ?? "").Select(x => x.Value);
            var satirlar = new List<string>();
            long toplamKaynak = 0, toplamEksik = 0, toplamAtilan = 0;
            foreach (var b in bolumler)
            {
                var kaynak = new List<string>();
                foreach (var s in b.Sayfalar)
                {
                    float ust = Math.Max(135f, b.UstSinir(s)), alt = b.AltSinir(s);
                    foreach (var y in s.Yazilar.Where(y => y.Y >= ust && y.Y < alt)) kaynak.AddRange(Sayilar(y.Metin));
                }
                var cikti = new List<string>();
                var atilan = new List<string>();
                var ogeler = Dizgi.GprAyristirici.Ayristir(b, atilan);
                cikti.AddRange(atilan.SelectMany(Sayilar)); // bilinçli atılan tekrarlar ayrıca sayılır
                int atilanSayi = atilan.SelectMany(Sayilar).Count();
                toplamAtilan += atilanSayi;
                foreach (var o in ogeler)
                    switch (o)
                    {
                        case Dizgi.BaslikOgesi h: cikti.AddRange(Sayilar(h.Metin)); break;
                        case Dizgi.MetinOgesi m: foreach (var st in m.Satirlar) foreach (var p in st.Parcalar) cikti.AddRange(Sayilar(p.Metin)); break;
                        case Dizgi.AnahtarDegerOgesi a: foreach (var (k, d) in a.Satirlar) { cikti.AddRange(Sayilar(k)); cikti.AddRange(Sayilar(d)); } break;
                        case Dizgi.TabloOgesi t:
                            foreach (var r in t.Baslik.Concat(t.Govde)) foreach (var h in r.Hucreler) cikti.AddRange(Sayilar(h.Metin));
                            foreach (var n in t.Notlar) cikti.AddRange(Sayilar(n));
                            break;
                        case Dizgi.SekilOgesi sk: foreach (var y in sk.Yazilar) cikti.AddRange(Sayilar(y.Metin)); break;
                        case Dizgi.DiyaframOgesi dy: foreach (var (kat, no) in dy.Katlar) { cikti.AddRange(Sayilar(kat)); foreach (var n in no) cikti.AddRange(Sayilar(n)); } break;
                    }
                // tekrarlanan sayfa başlıkları (aynı bölüm başlığı) çıktıda bir kez: kaynaktan farkı yalnız eksik yönde say
                var kalan = cikti.GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
                int eksik = 0;
                foreach (var x in kaynak) { if (kalan.TryGetValue(x, out int n) && n > 0) kalan[x] = n - 1; else eksik++; }
                toplamKaynak += kaynak.Count; toplamEksik += eksik;
                if (eksik > 0) satirlar.Add($"  {b.Baslik}: kaynak {kaynak.Count}, eksik {eksik}");
            }
            satirlar.Insert(0, $"Sayı kontrolü: kaynak {toplamKaynak} sayısal belirteç, tekrar olduğu için bilerek atılan {toplamAtilan}, kayıp {toplamEksik}");
            return satirlar;
        }

        /// <summary>Arayüzle aynı varsayılan: her başlığın ilk sürümü, FEA3D hariç.</summary>
        public static HashSet<int> VarsayilanSecim(GprBelgesi g) =>
            RaporKatalogu.Dagit(g).Where(k => k.Varsayilan).SelectMany(k => k.Bolumler).Select(b => b.No).ToHashSet();

        /// <summary>Arayüzle aynı: GPR'deki tüm bölümler (tekrar baskılar ayıklanmış).</summary>
        public static HashSet<int> GprIcerigi(GprBelgesi g) =>
            RaporKatalogu.Dagit(g).SelectMany(k => k.Bolumler).Select(b => b.No).ToHashSet();

        /// <summary>Doğrulama: STA4 kütüphane resimlerinin BEYKENT çizimleri, her biri ayrı PNG (8 px/mm).</summary>
        public static void ResimleriYaz(string klasor)
        {
            Directory.CreateDirectory(klasor);
            const float px = 8f, w = 182f;
            var onceki = Modern.StaResimleri.Tema;
            foreach (var tema in Modern.StaResimTemasi.Hepsi)
            {
                Modern.StaResimleri.Tema = tema;
                string tk = Path.Combine(klasor, tema.Kod);
                Directory.CreateDirectory(tk);
                foreach (int no in Modern.StaResimleri.Numaralar)
                {
                    float h;
                    using (var olc = new Bitmap(10, 10)) using (var go = Graphics.FromImage(olc))
                        h = Modern.StaResimleri.Yukseklik(new Modern.ModernSayfa(new GdiYuzey(go)), no, w);
                    using var bmp = new Bitmap((int)((w + 4) * px), (int)((h + 4) * px));
                    using (var gr = Graphics.FromImage(bmp))
                    {
                        gr.Clear(Color.White);
                        gr.ScaleTransform(px, px);
                        var s = new Modern.ModernSayfa(new GdiYuzey(gr));
                        Modern.StaResimleri.Ciz(s, no, 2, 2, w);
                    }
                    bmp.Save(Path.Combine(tk, $"resim{no:D2}.png"), ImageFormat.Png);
                }
            }
            Modern.StaResimleri.Tema = onceki;
        }

    
    /// <summary>Doğrulama: bölüm bölüm ayrıştırılan öğeler (tür + metin).</summary>
        public static void OgeDokumu(string st4, string cikti)
        {
            var g = GprBelgesi.Oku(ProjeKlasoru.Ac(st4).GprYolu);
            var l = new List<string>();
            foreach (var b in RaporKatalogu.Sirala(RaporKatalogu.Dagit(g).SelectMany(k => k.Bolumler)))
            {
                l.Add($"#### {b.No} {b.Baslik}");
                foreach (var o in Dizgi.GprAyristirici.Ayristir(b))
                    switch (o)
                    {
                        case Dizgi.BaslikOgesi h: l.Add($"BASLIK{h.Seviye} {h.Metin}"); break;
                        case Dizgi.MetinOgesi m: l.Add("METIN"); l.AddRange(m.Satirlar.Select(r => "  " + (r.Kalin ? "*" : "") + string.Join(" |", r.Parcalar.Select(p => $"{p.Sutun}:{p.Metin}")))); break;
                        case Dizgi.AnahtarDegerOgesi a: l.Add("ANAHTAR"); l.AddRange(a.Satirlar.Select(r => $"  {r.Anahtar} => {r.Deger}")); break;
                        case Dizgi.TabloOgesi t: l.Add($"TABLO [{string.Join(",", t.Sutunlar)}] {t.Baslik.Count}+{t.Govde.Count}"); l.AddRange(t.Baslik.Concat(t.Govde).Take(8).Select(r => "  | " + string.Join(" | ", r.Hucreler.Select(x => x.Metin)))); break;
                        case Dizgi.SekilOgesi s: l.Add($"SEKIL {s.Alan} yazı {s.Yazilar.Count}"); break;
                        case Dizgi.ResimOgesi r: l.Add($"RESIM #{r.No} y {r.Y}"); break;
                    }
            }
            File.WriteAllLines(cikti, l);
        }

        /// <summary>Katalog dağılımı: sekme / kalem / varsayılan / bölümler.</summary>
        public static List<string> KatalogDokumu(GprBelgesi g) =>
            RaporKatalogu.Dagit(g).Select(k => $"[{k.Sekme}] {(k.Varsayilan ? "X" : " ")} {k.Ad}: " +
                string.Join("; ", k.Bolumler.Select(b => $"{b.Baslik}{(b.Grafik ? " (grafik)" : "")} {b.SayfaAraligi}"))).ToList();
    }
}
