using System.Drawing;
using StatikRaporClaude.Cizim;
using StatikRaporClaude.Gpr;
using StatikRaporClaude.Modern;
using StatikRaporClaude.Proje;

namespace StatikRaporClaude.Rapor
{
    /// <summary>Modern kapak: logo, başlık, arsa/bina bilgileri, müellif ve imza alanı, taraflar, onay kutuları.</summary>
    public static class KapakSayfasi
    {
        public static void Ciz(ModernSayfa s, RaporBilgisi r, KapakBilgisi k, ProjeKlasoru p, GprBelgesi g)
        {
            const float K = ModernSayfa.Kenar, En = ModernSayfa.En;
            float gen = En - 2 * K;
            var G = s.G;

            // üst şerit
            G.Dikdortgen(new RectangleF(0, 0, En, 6f), ModernSayfa.Lacivert);
            G.Dikdortgen(new RectangleF(0, 6f, En * 0.62f, 1.2f), Logo.Mavi);
            G.Dikdortgen(new RectangleF(En * 0.62f, 6f, En * 0.38f, 1.2f), Logo.Yesil);

            var logo = Logo.Renkli();
            if (logo != null)
            {
                float h = 15f, w = h * logo.Width / logo.Height;
                G.Resim(logo, new RectangleF(K, 18f, w, h));
                s.Metin("MÜHENDİSLİK HİZMETLERİ", ModernSayfa.F(10f), Color.FromArgb(70, 70, 70), K + 0.4f, 35f);
            }

            float y = 58f;
            G.Dikdortgen(new RectangleF(K, y + 1.5f, 1.8f, 13f), ModernSayfa.Turuncu);
            s.Metin(string.IsNullOrWhiteSpace(k.RaporBasligi) ? "STATİK HESAPLAR" : k.RaporBasligi, ModernSayfa.F(30f, true), ModernSayfa.Lacivert, K + 5f, y, gen - 5);
            y += 15f;
            s.Metin(p.ProjeAdi, ModernSayfa.F(14f, true), ModernSayfa.Kobalt, K + 5f, y, gen - 5);
            y += 7.5f;
            string katlar = string.Join("  ·  ", p.Katlar.Select(x => $"{ModernBolumler.Tr(x.Ad)} {x.Kot.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} m"));
            s.Metin($"{p.TabanAd}.ST4   ·   {p.Surum}   ·   hesap tarihi {(g?.RaporTarihi ?? DateTime.Today.ToString("dd-MM-yyyy"))?.Replace('-', '.')}", ModernSayfa.F(8.5f), ModernSayfa.Gri, K + 5f, y, gen - 5);
            y += 4.6f;
            s.Metin($"Katlar: {katlar}", ModernSayfa.F(8.5f), ModernSayfa.Gri, K + 5f, y, gen - 5);
            y += 13f;

            // arsa / bina
            float sw = (gen - 6f) / 2;
            float h1 = Kart(s, "ARSA", K, y, sw, new[]
            {
                ("Yeri", k.Yeri), ("Pafta - ada - parsel", k.PaftaAdaParsel), ("Yüzölçümü", k.Yuzolcumu), ("İhtiva ettiği yapı", k.IhtivaEttigiYapi)
            });
            float h2 = Kart(s, "BİNA", K + sw + 6f, y, sw, new[]
            {
                ("Cinsi", k.Cinsi), ("Kat adedi / yüksekliği", k.KatAdedi), ("Bina inşaat alanı", k.InsaatAlani),
                ("Kullanım amacı", k.KullanimAmaci), ("Yapı sınıfı / grubu", k.YapiSinifi)
            });
            y += Math.Max(h1, h2) + 7f;

            // müellif + imza
            float mw = gen * 0.6f;
            float h3 = Kart(s, "STATİK PROJE MÜELLİFİ", K, y, mw, new[]
            {
                ("Adı soyadı", k.MuellifAdi), ("Ünvanı", k.Unvani), ("Oda sicil no", k.OdaSicilNo), ("Büro tescil no", k.BuroTescilNo)
            });
            var imza = new RectangleF(K + mw + 6f, y, gen - mw - 6f, h3);
            G.Dikdortgen(imza, ModernSayfa.Serit, ModernSayfa.Cizgi, 0.3f);
            s.Metin("İMZA / KAŞE", ModernSayfa.F(7f, true), ModernSayfa.Gri, imza.X + 3f, imza.Y + 2.5f);
            s.Metin(k.MuellifAdi, ModernSayfa.F(10f, true), ModernSayfa.Lacivert, imza.X, imza.Bottom - 15f, imza.Width, Hiza.Orta);
            s.Metin(k.Unvani, ModernSayfa.F(7.5f), ModernSayfa.Gri, imza.X, imza.Bottom - 9.5f, imza.Width, Hiza.Orta);
            if (!string.IsNullOrWhiteSpace(k.OdaSicilNo))
                s.Metin($"Oda sicil no {k.OdaSicilNo}", ModernSayfa.F(7f), ModernSayfa.Gri, imza.X, imza.Bottom - 5.5f, imza.Width, Hiza.Orta);
            y += h3 + 7f;

            float h4 = Kart(s, "YAPI SAHİBİ", K, y, sw, new[] { ("", k.YapiSahibi) });
            float h5 = Kart(s, "YAPI DENETİM KURULUŞU", K + sw + 6f, y, sw, new[] { ("", k.YapiDenetim) });
            y += Math.Max(h4, h5) + 7f;

            // onay
            s.Metin("ONAY", ModernSayfa.F(8f, true), ModernSayfa.Kobalt, K, y);
            y += 5.5f;
            float oy = ModernSayfa.Boy - 30f - y;
            foreach (var (ad, x) in new[] { ("TMMOB onayı", K), ("Belediye onayı", K + sw + 6f) })
            {
                G.Dikdortgen(new RectangleF(x, y, sw, Math.Max(18f, oy)), null, ModernSayfa.Cizgi, 0.3f);
                s.Metin(ad, ModernSayfa.F(7f), ModernSayfa.Gri, x + 3f, y + 2.5f);
            }

            // yasal not ve alt şerit
            s.Paragraf("5846 sayılı Fikir ve Sanat Eserleri Yasası, 3458 sayılı Mühendis ve Mimarlar hk. Kanun ve 6235 (7303) sayılı TMMOB Yasaları uyarınca; " +
                       "proje müelliflerinden izin almadan projelerde hiçbir değişiklik yapılamaz.",
                ModernSayfa.F(6.6f), ModernSayfa.Gri, K, ModernSayfa.Boy - 22f, gen);
            G.Dikdortgen(new RectangleF(0, ModernSayfa.Boy - 8f, En, 8f), ModernSayfa.Lacivert);
            s.Metin("BEYKENT PROJE  ·  Mühendislik Hizmetleri", ModernSayfa.F(7.5f, true), Color.White, K, ModernSayfa.Boy - 5.8f);
            s.Metin($"{r.ToplamSayfa} sayfa", ModernSayfa.F(7.5f), Color.FromArgb(185, 200, 225), En - K, ModernSayfa.Boy - 5.8f, 0, Hiza.Sag);
        }

        /// <summary>Başlıklı bilgi kartı; yüksekliği döndürür.</summary>
        private static float Kart(ModernSayfa s, string baslik, float x, float y, float w, (string Etiket, string Deger)[] satirlar)
        {
            var G = s.G;
            float h = 8f + satirlar.Length * 6.4f + 2f;
            G.Dikdortgen(new RectangleF(x, y, w, h), ModernSayfa.Serit);
            G.Dikdortgen(new RectangleF(x, y, w, 0.8f), ModernSayfa.Kobalt);
            s.Metin(baslik, ModernSayfa.F(8f, true), ModernSayfa.Kobalt, x + 3f, y + 2.6f, w - 6);
            float yy = y + 8.5f;
            foreach (var (e, d) in satirlar)
            {
                if (e.Length > 0)
                {
                    s.Metin(e, ModernSayfa.F(7.4f), ModernSayfa.Gri, x + 3f, yy + 0.3f, w * 0.42f);
                    s.Metin(string.IsNullOrWhiteSpace(d) ? "—" : d, ModernSayfa.F(8.6f, true), ModernSayfa.Koyu, x + w * 0.44f, yy, w * 0.54f);
                }
                else s.Metin(string.IsNullOrWhiteSpace(d) ? "—" : d, ModernSayfa.F(9.5f, true), ModernSayfa.Koyu, x + 3f, yy, w - 6);
                yy += 6.4f;
                if (e.Length > 0) G.Cizgi(ModernSayfa.Cizgi, 0.2f, x + 3f, yy - 1.3f, x + w - 3f, yy - 1.3f);
            }
            return h;
        }
    }
}
