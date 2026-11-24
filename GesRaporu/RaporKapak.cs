using System.Globalization;
using PdfSharp.Drawing;

namespace GesRaporu
{
    /// <summary>Rapor girişi: kapak (StatikRaporClaude kapak düzeni), içindekiler ve üretim akışı.</summary>
    public static partial class Rapor
    {
        public static void Uret(Girdi g, DwgBilgi.PlanCizimi plan, string yol)
        {
            // Raporda yetersiz eleman yer almaz: gerekirse kademeli kurtarılır
            g = Hesap.Kurtar(g, out _);
            if (!new Hesap(g).HepsiYeterli)
                throw new InvalidOperationException("Bazı tahkikler otomatik düzeltmeyle sağlanamadı; yetersiz eleman içeren rapor oluşturulmaz.");
            var h = new Hesap(g);
            string blok = DwgBilgi.Etiketle(g.Blok);
            var p = new PdfYazici
            {
                ProjeAdi = ProjeAdi(g),
                AltBaslik = "GES çatı taşıyıcı sistemi   ·   statik hesap raporu",
                Firma = Bos(g.Hazirlayan, ""),
                Tarih = DateTime.Now.ToString("dd.MM.yyyy", Tr),
                BlokAdi = blok,
            };
            p.Belge.Info.Title = $"GES Çatı Statik Hesap Raporu {blok}".Trim();
            p.Belge.Info.Author = g.Hazirlayan;

            int toplam = Kapak(p, g, h, blok);
            p.YeniSayfa();
            Icindekiler(p);
            IsinKonusu(p, g, h);
            Kriterler(p, g, h);
            Model(p, g, h, plan);
            Yukler(p, g, h);
            KaplamaTahkiki(p, g, h);
            BulonTahkiki(p, g, h);
            VidaTahkiki(p, g, h);
            ProfilTahkiki(p, g, h);
            Sonuc(p, g, h);
            Ekler(p, g);
            p.Kaydet(yol);
        }

        private static string ProjeAdi(Girdi g)
        {
            var s = new List<string>();
            if (!string.IsNullOrWhiteSpace(g.Ada)) s.Add($"{g.Ada} ADA {Bos(g.Parsel, "-")} PARSEL");
            if (!string.IsNullOrWhiteSpace(g.Mahalle)) s.Add($"{Up(g.Mahalle)} MAH.");
            if (!string.IsNullOrWhiteSpace(g.Blok)) s.Add(DwgBilgi.Etiketle(g.Blok));
            return s.Count > 0 ? string.Join("  ·  ", s) : "GES ÇATI HESABI";
        }

        // ------------------------------------------------------------------ kapak
        private static int Kapak(PdfYazici p, Girdi g, Hesap h, string blok)
        {
            p.YeniSayfa(false);
            var G = p.Gfx;
            double En = p.Sayfa.Width.Point, Boy = p.Sayfa.Height.Point, K = PdfYazici.Sol, gen = En - 2 * K, mm = PdfYazici.Mm;

            // üst şerit
            G.DrawRectangle(Renk.F(Renk.Lacivert), 0, 0, En, 6 * mm);
            G.DrawRectangle(Renk.F(Renk.LogoMavi), 0, 6 * mm, En * 0.62, 1.2 * mm);
            G.DrawRectangle(Renk.F(Renk.LogoYesil), En * 0.62, 6 * mm, En * 0.38, 1.2 * mm);

            var logo = Logo.Al();
            if (logo != null)
            {
                double lh = 15 * mm, lw = lh * logo.PixelWidth / logo.PixelHeight;
                G.DrawImage(logo, K, 18 * mm, lw, lh);
                p.Metin("MÜHENDİSLİK HİZMETLERİ", PdfYazici.P(10), XColor.FromArgb(70, 70, 70), K + 1, 34 * mm);
            }

            // blok rozeti
            if (!string.IsNullOrEmpty(blok))
            {
                var f = PdfYazici.P(blok.Length > 8 ? 13 : 20, true);
                double bw = Math.Max(G.MeasureString(blok, f).Width + 28, 80), bh = 15 * mm;
                var r = new XRect(En - K - bw, 18 * mm, bw, bh);
                G.DrawRoundedRectangle(Renk.F(Renk.Kobalt), r, new XSize(10, 10));
                G.DrawString(blok, f, XBrushes.White, new XRect(r.X, r.Y - 4, r.Width, r.Height), XStringFormats.Center);
                G.DrawString("RAPORU HAZIRLANAN BLOK", PdfYazici.P(6.5), Renk.F(Renk.Gri), new XRect(r.X, r.Bottom + 2, r.Width, 9), XStringFormats.TopCenter);
            }

            double y = 58 * mm;
            G.DrawRectangle(Renk.F(Renk.Turuncu), K, y + 1.5 * mm, 1.8 * mm, 23 * mm);
            p.Metin("GES ÇATI TAŞIYICI SİSTEMİ", PdfYazici.P(24, true), Renk.Lacivert, K + 5 * mm, y);
            y += 11 * mm;
            p.Metin("STATİK HESAP RAPORU", PdfYazici.P(24, true), Renk.Lacivert, K + 5 * mm, y);
            y += 13 * mm;
            p.Metin(ProjeAdi(g), PdfYazici.P(13, true), Renk.Kobalt, K + 5 * mm, y, gen - 5 * mm);
            y += 7 * mm;
            p.Metin($"Güneş enerji santrali (GES) çatı uygulaması   ·   TS 498:2021   ·   {DateTime.Now.ToString("dd.MM.yyyy", Tr)}", PdfYazici.P(8.5), Renk.Gri, K + 5 * mm, y);
            y += 4.6 * mm;
            y += 11 * mm;

            // arsa / bina
            double sw = (gen - 6 * mm) / 2;
            double h1 = Kart(p, "ARSA", K, y, sw, new[]
            {
                ("İl / ilçe", $"{Bos(Up(g.Il))} / {Bos(Up(g.Ilce))}"), ("Mahalle", Bos(Up(g.Mahalle))),
                ("Pafta - ada - parsel", $"{Bos(g.Pafta, "-")} / {Bos(g.Ada)} / {Bos(g.Parsel)}"), ("Rakım", $"{N(g.Rakim, 0)} m"),
            });
            double h2 = Kart(p, "BİNA", K + sw + 6 * mm, y, sw, new[]
            {
                ("Blok", Bos(blok)), ("Cinsi", Bos(Up(g.YapiCinsi))), ("Kat adedi", Bos(Up(g.KatAdedi))),
                ("Kullanım amacı", Bos(Up(g.KullanimAmaci))), ("Bina yüksekliği", $"{N(g.BinaYuksekligi, 2)} m"),
            });
            y += Math.Max(h1, h2) + 7 * mm;

            // GES sistemi + sonuç
            double mw = gen * 0.6;
            double h3 = Kart(p, "GES SİSTEMİ", K, y, mw, new[]
            {
                ("Panel", g.PanelModeli), ("Ebat / ağırlık", $"{N(g.PanelA, 3)} x {N(g.PanelB, 3)} m  ·  {N(g.PanelAgirlik, 1)} kg"),
                ("Adet / kurulu güç", g.PanelAdedi > 0 ? $"{g.PanelAdedi} adet  ·  {N(h.KuruluGucKwp, 2)} kWp" : "—"),
                ("Çatı kaplaması", $"{g.KaplamaTipi}  t = {N(g.SacKalinligi, 2)} mm"), ("Taşıyıcı profil", Aluminyum.Profil),
            });
            var sr = new XRect(K + mw + 6 * mm, y, gen - mw - 6 * mm, h3);
            G.DrawRectangle(Renk.F(XColor.FromArgb(236, 246, 239)), sr);
            G.DrawRectangle(Renk.F(Renk.Uygun), sr.X, sr.Y, sr.Width, 0.8 * mm);
            p.Metin("SONUÇ", PdfYazici.P(8, true), Renk.Uygun, sr.X + 3 * mm, sr.Y + 2.6 * mm);
            p.Metin("YETERLİ", PdfYazici.P(20, true), Renk.Uygun, sr.X, sr.Y + 9 * mm, sr.Width, XStringFormats.TopCenter);
            double ty = sr.Y + 20 * mm;
            foreach (var m in new[] { "Çatı kaplaması", "Bağlantı bulonu", "Bağlantı vidası", "Alüminyum profil" })
            {
                var pen = new XPen(Renk.Uygun, 1.4) { LineCap = XLineCap.Round };
                G.DrawLines(pen, new[] { new XPoint(sr.X + 4 * mm, ty + 4.5), new XPoint(sr.X + 4 * mm + 3, ty + 7.5), new XPoint(sr.X + 4 * mm + 8, ty + 1.5) });
                p.Metin(m, PdfYazici.P(8), Renk.Koyu, sr.X + 4 * mm + 13, ty - 0.5);
                ty += 5.4 * mm;
            }
            y += h3 + 7 * mm;

            // hazırlayan (tam genişlik)
            double h4 = Kart(p, "HAZIRLAYAN", K, y, gen, new[]
            {
                ("Firma", Bos(g.Hazirlayan)), ("Mühendis", Bos(g.HazirlayanMuhendis)), ("Tarih", DateTime.Now.ToString("dd.MM.yyyy", Tr)),
            });
            y += h4 + 7 * mm;

            Kart(p, "YAPI SAHİBİ", K, y, gen, new[] { ("", Bos(Up(g.YapiSahibi))) });

            // alt şerit
            G.DrawRectangle(Renk.F(Renk.Lacivert), 0, Boy - 8 * mm, En, 8 * mm);
            p.Metin("BEYKENT PROJE  ·  Mühendislik Hizmetleri", PdfYazici.P(7.5, true), XColors.White, K, Boy - 5.8 * mm);
            p.Metin("GES çatı statik hesap raporu", PdfYazici.P(7.5), XColor.FromArgb(185, 200, 225), K, Boy - 5.8 * mm, gen, XStringFormats.TopRight);
            return 0;
        }

        /// <summary>Başlıklı bilgi kartı (StatikRapor KapakSayfasi.Kart); yüksekliği döndürür.</summary>
        private static double Kart(PdfYazici p, string baslik, double x, double y, double w, (string Etiket, string Deger)[] satirlar)
        {
            var G = p.Gfx;
            double mm = PdfYazici.Mm, sh = 6.4 * mm;
            double h = 8 * mm + satirlar.Length * sh + 2 * mm;
            G.DrawRectangle(Renk.F(Renk.Serit), x, y, w, h);
            G.DrawRectangle(Renk.F(Renk.Kobalt), x, y, w, 0.8 * mm);
            p.Metin(baslik, PdfYazici.P(8, true), Renk.Kobalt, x + 3 * mm, y + 2.4 * mm);
            double yy = y + 8.5 * mm;
            foreach (var (e, d) in satirlar)
            {
                if (e.Length > 0)
                {
                    p.Metin(e, PdfYazici.P(7.4), Renk.Gri, x + 3 * mm, yy + 0.4);
                    var f = PdfYazici.P(8.6, true);
                    while (f.Size > 6.5 && G.MeasureString(d, f).Width > w * 0.6 - 3 * mm) f = PdfYazici.P(f.Size - 0.3, true);
                    p.Metin(d, f, Renk.Koyu, x + w * 0.38, yy, w * 0.6 - 3 * mm);
                }
                else p.Metin(d, PdfYazici.P(10, true), Renk.Koyu, x + 3 * mm, yy - 0.5, w - 6 * mm);
                yy += sh;
                if (e.Length > 0) G.DrawLine(new XPen(Renk.Cizgi, 0.5), x + 3 * mm, yy - 1.4 * mm, x + w - 3 * mm, yy - 1.4 * mm);
            }
            return h;
        }

        private static void Icindekiler(PdfYazici p)
        {
            p.Baslik("İÇİNDEKİLER", 12);
            p.IcindekilerYeri(215);
        }
    }
}
