using System.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using StatikRaporClaude.Cizim;
using StatikRaporClaude.Dizgi;
using StatikRaporClaude.Gpr;
using StatikRaporClaude.Modern;
using StatikRaporClaude.Proje;

namespace StatikRaporClaude.Rapor
{
    public sealed class RaporSecenekleri
    {
        public ProjeKlasoru Proje;
        /// <summary>GPR; null olabilir (yalnız sonuç dosyalarından üretilen bölümler).</summary>
        public GprBelgesi Gpr;
        public HashSet<int> SeciliBolumler;
        /// <summary>Aday bölümler rapor sırasıyla (GPR + sanal); null ise GPR bölümleri.</summary>
        public List<GprBolum> Bolumler;
        public KapakBilgisi Kapak;
        public List<string> EkPdfler = new List<string>();
        public bool ModernTablolar = true;
        /// <summary>Basılacak kat kodları (Z, 1, S…); null = tümü.</summary>
        public ISet<string> Katlar;
        /// <summary>Doluysa desteklenen tablolar GPR yerine sonuç dosyalarından üretilir.</summary>
        public Veri.StaVeri Veri;
        /// <summary>Kapaktan sonra yapı teknik raporu ve yapı yorumu (A statik, B ekonomi, C temel ve zemin).</summary>
        public bool TeknikRapor = true;
        /// <summary>Yalnız jeoteknik bilgi raporu (zemin etüdü için): yapı/temel bilgileri, yapı yorumu, temel yükleri, zemin gerilmeleri.</summary>
        public bool Geoteknik;
        public string CiktiYolu;
        /// <summary>Doğrulama: doluysa her rapor sayfası bu klasöre PNG olarak da çizilir (sayfa NNN.png).</summary>
        public string PngKlasoru;
    }

    public sealed class RaporSonucu
    {
        public int ToplamSayfa, KapakVeIcindekiler, ModernSayfa, AkisSayfa, Sta4Sayfa, EkSayfa, Bolum;
        public string Yol;
    }

    /// <summary>
    /// Statik hesap raporunu PDF olarak derler: kapak, tıklanabilir içindekiler ve yer imleri, seçili bölümler
    /// (desteklenenler modern tablo, diğerleri STA4 sayfa gövdesi bizim çerçevemizde), sona eklenen PDF'ler.
    /// </summary>
    public static class RaporDerleyici
    {

        private sealed class GprIsi
        {
            public GprSayfa Sayfa;
            public float Ust, Alt;
            public string Baslik;
        }

        private sealed class Is
        {
            public SayfaIsi Ciz;
            public GprIsi Gpr;
        }

        private sealed class Girdi
        {
            public string Grup, Baslik;
            public int Sayfa;   // 1 tabanlı
        }

        public static RaporSonucu Olustur(RaporSecenekleri o, IProgress<string> ilerleme = null)
        {
            FontCozucu.Kur();
            var p = o.Proje;
            var g = o.Gpr;
            var bilgi = new RaporBilgisi
            {
                Firma = string.IsNullOrWhiteSpace(o.Kapak?.MuellifAdi) ? g?.Firma ?? p.Firma : o.Kapak.MuellifAdi,
                ProjeAdi = p.ProjeAdi, St4Adi = p.TabanAd + ".ST4",
                Tarih = g?.RaporTarihi?.Replace('-', '.') ?? DateTime.Today.ToString("dd.MM.yyyy"),
                RaporTuru = o.Geoteknik ? "Geoteknik bilgi raporu" : "Statik hesap raporu"
            };
            // jeoteknik raporda kapak başlığı (kullanıcı kendi başlığını yazmadıysa)
            if (o.Geoteknik && o.Kapak != null && (string.IsNullOrWhiteSpace(o.Kapak.RaporBasligi) || o.Kapak.RaporBasligi == "STATİK HESAPLAR"))
                o.Kapak.RaporBasligi = "GEOTEKNİK BİLGİ RAPORU";

            // ölçüm yüzeyi: modern tabloların sayfalaması PDF ile aynı ölçümle yapılır
            var olcumBelge = new PdfDocument();
            var olcumYuzey = new PdfYuzey(olcumBelge.AddPage());
            Func<ModernSayfa> olcum = () => new ModernSayfa(olcumYuzey);

            var isler = new List<Is>();
            var girdiler = new List<Girdi>();
            var bolumBasi = new List<(GprBolum Bolum, int IsNo)>();
            var sonuc = new RaporSonucu();

            // Modern dizgi: ardışık bölümler tek akışta (bölüm başlarında sayfa kesmesi yok); modern tablolu bölüm akışı keser
            var akis = new List<Blok>();
            var akisBolumleri = new List<GprBolum>();
            void AkisiBosalt()
            {
                if (akis.Count == 0) return;
                ilerleme?.Report("Sayfalanıyor…");
                var harita = new Dictionary<GprBolum, int>();
                var sayfalar = Akis.Sayfala(akis, olcum, harita);
                int bas = isler.Count;
                foreach (var sayfa in sayfalar)
                {
                    var sp = sayfa;
                    isler.Add(new Is
                    {
                        Ciz = (s, r, no) =>
                        {
                            s.Cerceve(r, sp.UstBaslik, no);
                            s.Y = Akis.Ust;
                            foreach (var parca in sp.Parcalar) parca.Ciz(s);
                        }
                    });
                }
                foreach (var b in akisBolumleri)
                    bolumBasi.Add((b, bas + (harita.TryGetValue(b, out var i) ? i : 0)));
                sonuc.AkisSayfa += sayfalar.Count;
                akis.Clear();
                akisBolumleri.Clear();
            }

            // jeoteknik bilgi raporu: yapı/temel bilgileri ve yapı yorumu başta, ardından seçilen temel bölümleri
            if (o.Geoteknik)
            {
                var gb = GeoteknikRapor.Bolum();
                akis.AddRange(GeoteknikRapor.Bloklar(o, gb));
                akisBolumleri.Add(gb);
                var yb = GeoteknikRapor.YorumBolumu();
                if (p.DosyaVar(".YORUM") && Uretec.SanalBolum.Doldur(yb, p.St4Yolu))
                {
                    akis.AddRange(BolumDizgi.Bloklar(yb, o.Katlar));
                    akisBolumleri.Add(yb);
                }
            }
            // yapı teknik raporu ve yapı yorumu: içindekilerden hemen sonra, raporun ilk bölümü
            else if (o.TeknikRapor)
            {
                var tr = TeknikRapor.Bolum();
                akis.AddRange(TeknikRapor.Bloklar(o, tr));
                akisBolumleri.Add(tr);
                var yb = TeknikRapor.YorumBolumu();
                if (p.DosyaVar(".YORUM") && Uretec.SanalBolum.Doldur(yb, p.St4Yolu))
                {
                    akis.AddRange(BolumDizgi.Bloklar(yb, o.Katlar));
                    akisBolumleri.Add(yb);
                }
            }

            foreach (var b in (o.Bolumler ?? g?.Bolumler ?? new List<GprBolum>()).Where(b => o.SeciliBolumler.Contains(b.No)))
            {
                // STA4'ün tam yapı yorumu teknik rapordaki kısaltılmış yorumla yer değiştirir
                if ((o.TeknikRapor || o.Geoteknik) && (b.Anahtar ?? "").Contains("YAPI YORUMU")) continue;
                if ((o.TeknikRapor || o.Geoteknik) && b.Anahtar == RaporKatalogu.YorumAnahtari) continue;
                // sanal bölüm (GPR'de yok): her zaman dosyadan, modern dizgiyle
                // GPR'siz metin bölümü: üreteç satırlarından sanal sayfa kurulur, yeniden dizgiyle basılır
                if (b.Sayfalar.Count == 0 && Uretec.SanalBolum.Destekler(b.Anahtar))
                {
                    if (!Uretec.SanalBolum.Doldur(b, p.St4Yolu)) continue;
                    akis.AddRange(BolumDizgi.Bloklar(b, o.Katlar));
                    akisBolumleri.Add(b);
                    continue;
                }
                if (b.Sayfalar.Count == 0)
                {
                    if (o.Veri == null) continue;
                    if (ModernBolumler.AkisBloklari(b, p, o.Katlar, o.Veri) is { } sb)
                    {
                        akis.AddRange(sb);
                        akisBolumleri.Add(b);
                        continue;
                    }
                    if (ModernBolumler.Destekler(b))
                    {
                        var ms = ModernBolumler.Sayfalar(b, p, olcum, o.Katlar, o.Veri);
                        AkisiBosalt();
                        bolumBasi.Add((b, isler.Count));
                        isler.AddRange(ms.Select(s => new Is { Ciz = s }));
                        sonuc.ModernSayfa += ms.Count;
                    }
                    else if (Veri.DosyaBolumleri.Destekler(b))
                    {
                        akis.AddRange(Veri.DosyaBolumleri.Bloklar(b, o.Veri, o.Katlar));
                        akisBolumleri.Add(b);
                    }
                    continue;
                }
                ilerleme?.Report($"Hazırlanıyor: {ModernBolumler.Tr(b.Baslik)}");
                // kat seçimi: seçilmeyen katın kalıp aplikasyon planı basılmaz
                if (o.Katlar != null && b.Anahtar.Contains("KALIP APLIKASYON"))
                {
                    var kat = p.Katlar.FirstOrDefault(k => b.Anahtar.StartsWith(GprBolumleri.Anahtar(k.Ad) + " "));
                    if (kat != null && !o.Katlar.Contains(kat.Kod.TrimEnd('-'))) continue;
                }
                // kiriş / kolon betonarme sonuçları: akışta, önceki bölümün bıraktığı yerden
                if (o.ModernTablolar && ModernBolumler.AkisBloklari(b, p, o.Katlar, o.Veri) is { } mb)
                {
                    akis.AddRange(mb);
                    akisBolumleri.Add(b);
                    continue;
                }
                if (o.ModernTablolar && ModernBolumler.Destekler(b))
                {
                    var sayfalar = ModernBolumler.Sayfalar(b, p, olcum, o.Katlar, o.Veri);
                    if (sayfalar.Count > 0)
                    {
                        AkisiBosalt();
                        bolumBasi.Add((b, isler.Count));
                        isler.AddRange(sayfalar.Select(s => new Is { Ciz = s }));
                        sonuc.ModernSayfa += sayfalar.Count;
                        continue;
                    }
                }
                if (o.ModernTablolar)
                {
                    akis.AddRange(o.Veri != null && Veri.DosyaBolumleri.Destekler(b)
                        ? Veri.DosyaBolumleri.Bloklar(b, o.Veri, o.Katlar)
                        : BolumDizgi.Bloklar(b, o.Katlar));
                    akisBolumleri.Add(b);
                    continue;
                }
                // STA4 görünümü: ardışık seçili bölümler aynı GPR sayfasını paylaşıyorsa sayfa bir kez basılır
                bool ilk = true;
                foreach (var s in b.Sayfalar)
                {
                    float ust = b.UstSinir(s), alt = b.AltSinir(s);
                    var son = isler.Count > 0 ? isler[^1].Gpr : null;
                    if (son != null && son.Sayfa == s && son.Alt >= ust - 1)
                    {
                        son.Alt = Math.Max(son.Alt, alt);
                        if (ilk) bolumBasi.Add((b, isler.Count - 1));
                    }
                    else
                    {
                        if (ilk) bolumBasi.Add((b, isler.Count));
                        var gi = new GprIsi { Sayfa = s, Ust = ust, Alt = alt, Baslik = b.Baslik };
                        isler.Add(new Is { Gpr = gi });
                        sonuc.Sta4Sayfa++;
                    }
                    ilk = false;
                }
            }
            AkisiBosalt();

            // ekler
            var ekler = new List<(string Yol, PdfDocument Belge)>();
            foreach (var yol in o.EkPdfler.Where(File.Exists))
            {
                try { ekler.Add((yol, PdfReader.Open(yol, PdfDocumentOpenMode.Import))); }
                catch (Exception ex) { ilerleme?.Report($"Ek açılamadı: {Path.GetFileName(yol)} ({ex.Message})"); }
            }

            // içindekiler girdileri: önce göreli sayfa (iş sırası), içindekiler sayfa sayısı bulununca kaydırılır
            foreach (var (b, no) in bolumBasi)
                girdiler.Add(new Girdi { Grup = b.Grup, Baslik = ModernBolumler.Tr(b.Baslik) + (b.Grafik ? " (grafik)" : ""), Sayfa = no });
            int ekGoreli = isler.Count;
            foreach (var (yol, belge) in ekler)
            {
                girdiler.Add(new Girdi { Grup = "Ekler", Baslik = Path.GetFileNameWithoutExtension(yol), Sayfa = ekGoreli });
                ekGoreli += belge.PageCount;
            }
            var tocSayfalari = IcindekilerSayfala(Satirlar(girdiler));
            int icindekilerSayfa = tocSayfalari.Count;
            int ilkIs = 2 + icindekilerSayfa;
            foreach (var gi in girdiler) gi.Sayfa += ilkIs;
            int toplam = ilkIs + ekGoreli - 1;
            bilgi.ToplamSayfa = toplam;

            // çizim
            var doc = new PdfDocument();
            doc.Info.Title = $"{p.ProjeAdi} — {bilgi.RaporTuru}";
            doc.Info.Author = bilgi.Firma;
            doc.Info.Creator = "Statik Rapor (Claude) — BEYKENT PROJE";

            using (var y = new PdfYuzey(doc.AddPage())) KapakSayfasi.Ciz(new ModernSayfa(y), bilgi, o.Kapak ?? new KapakBilgisi(), p, g);
            var tocSatirlari = Satirlar(girdiler);
            for (int i = 0; i < icindekilerSayfa; i++)
                using (var y = new PdfYuzey(doc.AddPage()))
                    Icindekiler(new ModernSayfa(y), bilgi, girdiler.Count, tocSatirlari.Skip(tocSayfalari[i].Bas).Take(tocSayfalari[i].Adet).ToList(), i, 2 + i);
            for (int i = 0; i < isler.Count; i++)
            {
                if (i % 10 == 0) ilerleme?.Report($"Sayfa {ilkIs + i} / {toplam}");
                using var y = new PdfYuzey(doc.AddPage());
                var s = new ModernSayfa(y);
                int no = ilkIs + i;
                if (isler[i].Ciz != null) isler[i].Ciz(s, bilgi, no);
                else Sta4Sayfasi(s, bilgi, isler[i].Gpr, no);
                if (o.PngKlasoru != null) Png(o.PngKlasoru, no, ms => { if (isler[i].Ciz != null) isler[i].Ciz(ms, bilgi, no); else Sta4Sayfasi(ms, bilgi, isler[i].Gpr, no); });
            }
            foreach (var (_, belge) in ekler)
                for (int i = 0; i < belge.PageCount; i++) { doc.AddPage(belge.Pages[i]); sonuc.EkSayfa++; }

            // yer imleri
            foreach (var (ad, liste) in Ardisik(girdiler))
            {
                var ust = doc.Outlines.Add(ad, doc.Pages[liste[0].Sayfa - 1], true);
                foreach (var gi in liste) ust.Outlines.Add(gi.Baslik, doc.Pages[gi.Sayfa - 1]);
            }

            ilerleme?.Report("Kaydediliyor…");
            sonuc.ToplamSayfa = doc.PageCount;
            doc.Save(o.CiktiYolu);
            sonuc.Yol = o.CiktiYolu;
            sonuc.KapakVeIcindekiler = 1 + icindekilerSayfa;
            sonuc.Bolum = bolumBasi.Count;
            return sonuc;
        }

        private static void Png(string klasor, int no, Action<ModernSayfa> ciz)
        {
            Directory.CreateDirectory(klasor);
            const float Px = 1240f / ModernSayfa.En;   // 150 dpi
            using var bmp = new Bitmap(1240, 1754);
            using (var gr = Graphics.FromImage(bmp))
            {
                gr.Clear(Color.White);
                gr.ScaleTransform(Px, Px);
                ciz(new ModernSayfa(new GdiYuzey(gr)));
            }
            bmp.Save(Path.Combine(klasor, $"sayfa{no:D3}.png"), System.Drawing.Imaging.ImageFormat.Png);
        }

        /// <summary>Rapor sırasını bozmadan ardışık aynı gruptaki girdiler.</summary>
        private static List<(string Ad, List<Girdi> Liste)> Ardisik(List<Girdi> girdiler)
        {
            var sonuc = new List<(string, List<Girdi>)>();
            foreach (var gi in girdiler)
            {
                if (sonuc.Count == 0 || sonuc[^1].Item1 != gi.Grup) sonuc.Add((gi.Grup, new List<Girdi>()));
                sonuc[^1].Item2.Add(gi);
            }
            return sonuc;
        }

        private static void Sta4Sayfasi(ModernSayfa s, RaporBilgisi r, GprIsi gi, int no)
        {
            s.Cerceve(r, ModernBolumler.Tr(gi.Baslik), no);
            var alan = RectangleF.FromLTRB(ModernSayfa.Kenar, 30.5f, ModernSayfa.En - ModernSayfa.Kenar, ModernSayfa.Boy - 15.5f);
            GprCizici.CizGovde(s.G, gi.Sayfa, alan, gi.Ust, gi.Alt);
        }

        private const float GrupYuksekligi = 8.2f, GirdiYuksekligi = 5.4f;
        private const float TocIlkUst = 33f + 8.6f + 5.5f + 2f, TocUst = 33f + 8.6f + 2f;

        private static List<(bool Grup, Girdi Girdi, string Metin)> Satirlar(List<Girdi> girdiler)
        {
            var satirlar = new List<(bool, Girdi, string)>();
            foreach (var (ad, liste) in Ardisik(girdiler))
            {
                satirlar.Add((true, liste[0], ad));
                satirlar.AddRange(liste.Select(x => (false, x, x.Baslik)));
            }
            return satirlar;
        }

        /// <summary>Satırları sayfa yüksekliğine göre böler; grup başlığı sayfa sonunda yalnız kalmaz.</summary>
        private static List<(int Bas, int Adet)> IcindekilerSayfala(List<(bool Grup, Girdi Girdi, string Metin)> satirlar)
        {
            var sayfalar = new List<(int, int)>();
            int bas = 0;
            while (bas < satirlar.Count || sayfalar.Count == 0)
            {
                float y = sayfalar.Count == 0 ? TocIlkUst : TocUst, alt = ModernSayfa.Boy - 18f;
                int i = bas;
                while (i < satirlar.Count)
                {
                    float h = satirlar[i].Grup ? GrupYuksekligi : GirdiYuksekligi;
                    bool yalnizBaslik = satirlar[i].Grup && y + h + GirdiYuksekligi > alt;
                    if (y + h > alt || yalnizBaslik) break;
                    y += h; i++;
                }
                if (i == bas) i = Math.Min(satirlar.Count, bas + 1);
                sayfalar.Add((bas, i - bas));
                bas = i;
                if (satirlar.Count == 0) break;
            }
            return sayfalar;
        }

        private static void Icindekiler(ModernSayfa s, RaporBilgisi r, int girdiSayisi, List<(bool Grup, Girdi Girdi, string Metin)> satirlar, int parca, int no)
        {
            s.Cerceve(r, "İçindekiler", no);
            s.BolumBasligi(parca == 0 ? "İçindekiler" : "İçindekiler (devam)",
                parca == 0 ? $"{girdiSayisi} bölüm  ·  {r.ToplamSayfa} sayfa  ·  başlıklara tıklanarak ilgili sayfaya gidilir" : null);

            var f = ModernSayfa.F(8.6f);
            var fg = ModernSayfa.F(9.2f, true);
            float x0 = ModernSayfa.Kenar, x1 = ModernSayfa.En - ModernSayfa.Kenar;
            foreach (var (grup, girdi, metin) in satirlar)
            {
                int sayfa = girdi.Sayfa;
                float h = grup ? GrupYuksekligi : GirdiYuksekligi;
                if (grup)
                {
                    float yy = s.Y + 1.6f;
                    s.G.Dikdortgen(new RectangleF(x0, yy, 1f, 4.6f), Logo.Yesil);
                    s.Metin(metin, fg, ModernSayfa.Kobalt, x0 + 3f, yy + 0.2f, x1 - x0 - 20);
                    s.Metin(sayfa.ToString(), fg, ModernSayfa.Kobalt, x1, yy + 0.2f, 0, Hiza.Sag);
                    s.G.Baglanti(new RectangleF(x0, yy - 0.5f, x1 - x0, 5.8f), sayfa);
                }
                else
                {
                    float gen = Math.Min(s.Olc(metin, f).Width, x1 - x0 - 26);
                    s.Metin(metin, f, ModernSayfa.Koyu, x0 + 6f, s.Y, x1 - x0 - 26);
                    for (float k = x0 + 6f + gen + 2f; k < x1 - 12; k += 1.6f)
                        s.G.Dikdortgen(new RectangleF(k, s.Y + 2.6f, 0.25f, 0.25f), ModernSayfa.Cizgi);
                    s.Metin(sayfa.ToString(), f, ModernSayfa.Lacivert, x1, s.Y, 0, Hiza.Sag);
                    s.G.Baglanti(new RectangleF(x0, s.Y - 0.5f, x1 - x0, h - 0.6f), sayfa);
                }
                s.Y += h;
            }
        }
    }
}
