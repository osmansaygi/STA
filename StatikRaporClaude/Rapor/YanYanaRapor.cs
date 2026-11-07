using System.Drawing;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using StatikRaporClaude.Cizim;
using StatikRaporClaude.Dizgi;
using StatikRaporClaude.Gpr;
using StatikRaporClaude.Modern;

namespace StatikRaporClaude.Rapor
{
    /// <summary>
    /// Seçili bölümleri A3 yatay sayfada yan yana basar: sol STA4 GPR sayfası (Coursta, özgün),
    /// sağ bu programın aynı bölümü (BEYKENT dizgi veya sonuç dosyası tablosu).
    /// Sayfa sayıları eşit olmayabilir; kısa kalan tarafta açıklama yazılır.
    /// </summary>
    public static class YanYanaRapor
    {
        private const float SayfaEn = 420f, SayfaBoy = 297f, Bant = 8f, Yari = 210f;

        public static int Olustur(RaporSecenekleri o, IProgress<string> ilerleme = null)
        {
            if (o.Gpr == null) throw new InvalidOperationException("Yan yana karşılaştırma için projenin GPR dosyası gerekir.");
            FontCozucu.Kur();
            var p = o.Proje;
            var gpr = o.Gpr;
            var bilgi = new RaporBilgisi
            {
                Firma = string.IsNullOrWhiteSpace(o.Kapak?.MuellifAdi) ? gpr.Firma ?? p.Firma : o.Kapak.MuellifAdi,
                ProjeAdi = p.ProjeAdi, St4Adi = p.TabanAd + ".ST4",
                Tarih = gpr.RaporTarihi?.Replace('-', '.') ?? DateTime.Today.ToString("dd.MM.yyyy")
            };

            var olcumBelge = new PdfDocument();
            var olcumYuzey = new PdfYuzey(olcumBelge.AddPage());
            Func<ModernSayfa> olcum = () => new ModernSayfa(olcumYuzey);

            var yapraklar = new List<(GprBolum Bolum, GprSayfa Sol, SayfaIsi Sag)>();
            foreach (var b in (o.Bolumler ?? gpr.Bolumler).Where(b => o.SeciliBolumler.Contains(b.No)))
            {
                ilerleme?.Report("Hazırlanıyor: " + ModernBolumler.Tr(b.Baslik));
                var sol = b.Sayfalar.Where(s => s.Sira >= 0).ToList();
                if (o.Katlar != null && b.Anahtar != null && b.Anahtar.Contains("KALIP APLIKASYON"))
                {
                    var kat = p.Katlar.FirstOrDefault(k => b.Anahtar.StartsWith(GprBolumleri.Anahtar(k.Ad) + " "));
                    if (kat != null && !o.Katlar.Contains(kat.Kod.TrimEnd('-'))) continue;
                }
                var sag = SagSayfalar(b, o, olcum);
                int n = Math.Max(sol.Count, sag.Count);
                for (int i = 0; i < n; i++)
                    yapraklar.Add((b, i < sol.Count ? sol[i] : null, i < sag.Count ? sag[i] : null));
            }
            if (yapraklar.Count == 0)
                throw new InvalidOperationException("Karşılaştırılacak bölüm yok. GPR'li bir kalem seçin.");

            var doc = new PdfDocument();
            doc.Info.Title = $"{p.ProjeAdi} — GPR ile yan yana";
            doc.Info.Author = bilgi.Firma;
            doc.Info.Creator = "Statik Rapor (Claude) — BEYKENT PROJE";
            bilgi.ToplamSayfa = yapraklar.Count;

            string grup = null;
            PdfOutline grupIsaret = null;
            int bolumBasi = -1;
            for (int i = 0; i < yapraklar.Count; i++)
            {
                if (i % 8 == 0) ilerleme?.Report($"Sayfa {i + 1} / {yapraklar.Count}");
                var (b, sol, sag) = yapraklar[i];
                var sayfa = doc.AddPage();
                sayfa.Width = XUnit.FromMillimeter(SayfaEn);
                sayfa.Height = XUnit.FromMillimeter(SayfaBoy);
                if (grup != b.Grup)
                {
                    grup = b.Grup;
                    grupIsaret = doc.Outlines.Add(string.IsNullOrEmpty(grup) ? "Bölümler" : grup, sayfa, true);
                    bolumBasi = -1;
                }
                if (bolumBasi != b.No)
                {
                    bolumBasi = b.No;
                    grupIsaret.Outlines.Add(ModernBolumler.Tr(b.Baslik), sayfa);
                }
                using var y = new PdfYuzey(sayfa);
                Ciz(y, bilgi, b, sol, sag, i + 1);
            }

            ilerleme?.Report("Kaydediliyor…");
            doc.Save(o.CiktiYolu);
            return yapraklar.Count;
        }

        private static List<SayfaIsi> SagSayfalar(GprBolum b, RaporSecenekleri o, Func<ModernSayfa> olcum)
        {
            bool gprVardi = b.Sayfalar.Any(s => s.Sira >= 0);
            if (!gprVardi && Uretec.SanalBolum.Destekler(b.Anahtar))
            {
                if (!Uretec.SanalBolum.Doldur(b, o.Proje.St4Yolu)) return new List<SayfaIsi>();
                return Akistan(BolumDizgi.Bloklar(b, o.Katlar), olcum);
            }
            if (!gprVardi)
            {
                if (o.Veri == null) return new List<SayfaIsi>();
                if (o.ModernTablolar && ModernBolumler.Destekler(b))
                {
                    var ms = ModernBolumler.Sayfalar(b, o.Proje, olcum, o.Katlar, o.Veri);
                    if (ms.Count > 0) return ms;
                }
                return Veri.DosyaBolumleri.Destekler(b)
                    ? Akistan(Veri.DosyaBolumleri.Bloklar(b, o.Veri, o.Katlar), olcum)
                    : new List<SayfaIsi>();
            }
            if (o.ModernTablolar && ModernBolumler.Destekler(b))
            {
                var ms = ModernBolumler.Sayfalar(b, o.Proje, olcum, o.Katlar, o.Veri);
                if (ms.Count > 0) return ms;
            }
            if (o.ModernTablolar)
            {
                var blok = o.Veri != null && Veri.DosyaBolumleri.Destekler(b)
                    ? Veri.DosyaBolumleri.Bloklar(b, o.Veri, o.Katlar)
                    : BolumDizgi.Bloklar(b, o.Katlar);
                return Akistan(blok, olcum);
            }
            return b.Sayfalar.Where(s => s.Sira >= 0).Select(s =>
            {
                var gi = (Sayfa: s, Ust: b.UstSinir(s), Alt: b.AltSinir(s), Baslik: b.Baslik);
                SayfaIsi isi = (sf, r, no) =>
                {
                    sf.Cerceve(r, ModernBolumler.Tr(gi.Baslik), no);
                    var alan = RectangleF.FromLTRB(ModernSayfa.Kenar, 30.5f, ModernSayfa.En - ModernSayfa.Kenar, ModernSayfa.Boy - 15.5f);
                    GprCizici.CizGovde(sf.G, gi.Sayfa, alan, gi.Ust, gi.Alt);
                };
                return isi;
            }).ToList();
        }

        private static List<SayfaIsi> Akistan(List<Blok> bloklar, Func<ModernSayfa> olcum)
        {
            var sayfalar = Akis.Sayfala(bloklar, olcum, new Dictionary<GprBolum, int>());
            return sayfalar.Select(sp =>
            {
                SayfaIsi isi = (s, r, no) =>
                {
                    s.Cerceve(r, sp.UstBaslik, no);
                    s.Y = Akis.Ust;
                    foreach (var parca in sp.Parcalar) parca.Ciz(s);
                };
                return isi;
            }).ToList();
        }

        private static void Ciz(PdfYuzey g, RaporBilgisi r, GprBolum b, GprSayfa sol, SayfaIsi sag, int no)
        {
            g.Dikdortgen(new RectangleF(0, 0, SayfaEn, SayfaBoy), Color.White);
            g.Dikdortgen(new RectangleF(0, 0, SayfaEn, Bant), ModernSayfa.Lacivert);
            var fb = new YaziTipi(YaziTipi.Arayuz, 3.3f, true);
            string ad = ModernBolumler.Tr(b.Baslik);
            g.Yazi("SOL   STA4 GPR", fb, Color.White, 4f, 2.1f);
            g.Yazi("SAĞ   bu program", fb, Color.White, Yari + 4f, 2.1f);
            g.Yazi(ad, new YaziTipi(YaziTipi.Arayuz, 3.1f), Color.FromArgb(210, 220, 235), 52f, 2.2f);
            g.Cizgi(Color.FromArgb(180, 186, 196), 0.3f, Yari, Bant, Yari, SayfaBoy);

            float alanY = Bant + 1.5f, alanH = SayfaBoy - Bant - 3f;
            var solAlan = new RectangleF(2f, alanY, Yari - 4f, alanH);
            if (sol != null)
            {
                g.Kaydet();
                g.Kirp(solAlan);
                GprCizici.Ciz(g, sol, solAlan);
                g.GeriYukle();
            }
            else
                Not(g, 10f, 28f, Yari - 20f, "Bu bölüm GPR dosyasında yok. Sağdaki sayfa analiz dosyalarından üretildi.");

            g.Kaydet();
            float olcek = alanH / ModernSayfa.Boy;
            float sagW = ModernSayfa.En * olcek;
            g.Tasi(Yari + (Yari - sagW) / 2f, alanY);
            g.Olcekle(olcek);
            if (sag != null) sag(new ModernSayfa(g), r, no);
            else Not(g, ModernSayfa.Kenar, 36f, ModernSayfa.En - 2 * ModernSayfa.Kenar,
                "Bu bölümün GPR sayfaları sürüyor. Bu programın dizgisi burada bitti.");
            g.GeriYukle();
        }

        private static void Not(ICizimYuzeyi g, float x, float y, float w, string m)
        {
            var f = new YaziTipi(YaziTipi.Arayuz, 3.6f);
            foreach (var satir in g.Sar(m, f, w))
            {
                g.Yazi(satir, f, ModernSayfa.Gri, x, y);
                y += CizimUzanti.SatirAraligi(f);
            }
        }
    }
}
