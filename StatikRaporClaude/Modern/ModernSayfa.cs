using System.Drawing;
using StatikRaporClaude.Cizim;

namespace StatikRaporClaude.Modern
{
    public sealed class RaporBilgisi
    {
        public string Firma, ProjeAdi, St4Adi, Tarih;
        public string RaporTuru = "Statik hesap raporu";
        public int ToplamSayfa;
    }

    public sealed class Sutun
    {
        public string Baslik, Birim;
        public float Genislik;
        public Hiza Hiza = Hiza.Sag;
        public Sutun(string baslik, string birim, float genislik, Hiza hiza = Hiza.Sag) { Baslik = baslik; Birim = birim; Genislik = genislik; Hiza = hiza; }
    }

    public sealed class Satir
    {
        public string[] Hucreler;
        public string Alt;            // gri ikinci satır
        public bool? Durum;           // son sütunda yeşil/kırmızı işaret
        public string GrupBasligi;    // dolu ise ara başlık satırı
        public bool Vurgu;
        /// <summary>Kiriş statik şeması: alt satırın başına simge.</summary>
        public int? UcTipi;
    }

    /// <summary>
    /// Modern rapor sayfası: A4, milimetre birimi. Başlık bandı (logo + proje), bölüm başlığı, tablo, alt bilgi.
    /// Değerler kaynaktan metin olarak gelir; burada yalnız yerleşim yapılır. Ekran ve PDF aynı kodla çizilir.
    /// </summary>
    public sealed class ModernSayfa
    {
        public const float En = 210f, Boy = 297f, Kenar = 14f;
        public static readonly Color Lacivert = Color.FromArgb(22, 40, 72);
        public static readonly Color Kobalt = Color.FromArgb(38, 99, 184);
        public static readonly Color Cizgi = Color.FromArgb(214, 220, 228);
        public static readonly Color Serit = Color.FromArgb(246, 248, 251);
        public static readonly Color Gri = Color.FromArgb(105, 112, 122);
        public static readonly Color Uygun = Color.FromArgb(30, 140, 70);
        public static readonly Color Uygunsuz = Color.FromArgb(200, 40, 40);
        public static readonly Color Turuncu = Color.FromArgb(226, 122, 36);
        public static readonly Color Koyu = Color.FromArgb(25, 30, 38);
        /// <summary>Tablo başlık zemini: yazıcıda düz koyu blok yerine açık gri.</summary>
        public static readonly Color BaslikZemin = Color.FromArgb(234, 237, 241);
        /// <summary>Tablo satırlarını bölen ince çizgi.</summary>
        public static readonly Color Ince = Color.FromArgb(205, 210, 217);

        public readonly ICizimYuzeyi G;
        public float Y;                 // içerik imleci (mm)
        public float Alt => Boy - 18f;  // içerik alt sınırı

        public ModernSayfa(ICizimYuzeyi g)
        {
            G = g;
            g.Dikdortgen(new RectangleF(0, 0, En, Boy), Color.White);
        }

        /// <summary>Punto → mm yazı tipi.</summary>
        public static YaziTipi F(float pt, bool kalin = false) => new YaziTipi(YaziTipi.Arayuz, pt * 25.4f / 72f, kalin);

        public SizeF Olc(string m, YaziTipi f) => G.Olc(m, f);

        public void Metin(string m, YaziTipi f, Color c, float x, float y, float w = 0, Hiza h = Hiza.Sol) => G.YaziHizali(m, f, c, x, y, w, h);

        /// <summary>Sarılı metin; kullanılan yüksekliği döndürür.</summary>
        public float Paragraf(string m, YaziTipi f, Color c, float x, float y, float w)
        {
            float a = CizimUzanti.SatirAraligi(f);
            var satirlar = G.Sar(m, f, w);
            for (int i = 0; i < satirlar.Count; i++) G.Yazi(satirlar[i], f, c, x, y + i * a);
            return satirlar.Count * a;
        }

        public float ParagrafYukseklik(string m, YaziTipi f, float w) => G.Sar(m, f, w).Count * CizimUzanti.SatirAraligi(f);

        // ---------------- çerçeve ----------------

        public void Cerceve(RaporBilgisi r, string ustBaslik, int sayfaNo)
        {
            var logo = Logo.Renkli();
            if (logo != null)
            {
                float h = 7.2f, w = h * logo.Width / logo.Height;
                G.Resim(logo, new RectangleF(Kenar, 10f, w, h));
                Metin("MÜHENDİSLİK HİZMETLERİ", F(6.6f), Color.FromArgb(70, 70, 70), Kenar + 0.2f, 18.2f);
            }
            Metin(r.ProjeAdi, F(9.5f, true), Lacivert, En - Kenar, 10.4f, 0, Hiza.Sag);
            Metin($"{r.St4Adi}   ·   {r.RaporTuru}", F(7f), Gri, En - Kenar, 15.3f, 0, Hiza.Sag);
            G.Cizgi(Kobalt, 0.5f, Kenar, 23.5f, En - Kenar, 23.5f);
            G.Cizgi(Logo.Yesil, 0.5f, Kenar, 24.1f, Kenar + 30f, 24.1f);
            if (!string.IsNullOrEmpty(ustBaslik))
                Metin(ustBaslik.ToUpper(new System.Globalization.CultureInfo("tr-TR")), F(6.8f, true), Kobalt, Kenar, 26.2f, En - 2 * Kenar);

            // alt bilgi
            G.Cizgi(Cizgi, 0.25f, Kenar, Boy - 13.5f, En - Kenar, Boy - 13.5f);
            Metin($"Statik proje müellifi: {r.Firma}", F(6.8f), Gri, Kenar, Boy - 11.6f);
            Metin(r.Tarih, F(6.8f), Gri, En / 2, Boy - 11.6f, 0, Hiza.Orta);
            string no = r.ToplamSayfa > 0 ? $"{sayfaNo} / {r.ToplamSayfa}" : $"{sayfaNo}";
            Metin(no, F(7.5f, true), Lacivert, En - Kenar, Boy - 11.8f, 0, Hiza.Sag);
            Y = 33f;
        }

        public void BolumBasligi(string baslik, string aciklama)
        {
            G.Dikdortgen(new RectangleF(Kenar, Y + 0.7f, 1.1f, 5.4f), Turuncu);
            Metin(baslik, F(13f, true), Lacivert, Kenar + 3.2f, Y, En - 2 * Kenar - 4);
            Y += 7.4f;
            if (!string.IsNullOrEmpty(aciklama))
            {
                Metin(aciklama, F(7.4f), Gri, Kenar + 3.2f, Y, En - 2 * Kenar - 4);
                Y += 4.6f;
            }
            Y += 1.4f;
        }

        /// <summary>Özet kutucukları: büyük değer + küçük etiket.</summary>
        public void Kutucuklar(IList<(string Deger, string Etiket, Color? Renk)> k)
        {
            float w = (En - 2 * Kenar - (k.Count - 1) * 3f) / k.Count, h = 13f, x = Kenar;
            foreach (var (deger, etiket, renk) in k)
            {
                G.Dikdortgen(new RectangleF(x, Y, w, h), Serit);
                G.Dikdortgen(new RectangleF(x, Y, 0.9f, h), renk ?? Kobalt);
                float pt = 11.5f;
                while (pt > 7.5f && Olc(deger, F(pt, true)).Width > w - 5f) pt -= 0.5f;
                Metin(deger, F(pt, true), renk ?? Lacivert, x + 3.5f, Y + 1.6f + (11.5f - pt) * 0.18f, w - 5f);
                Metin(etiket, F(6.4f), Gri, x + 3.5f, Y + 8.3f, w - 5f);
                x += w + 3f;
            }
            Y += h + 4f;
        }

        public void AraBaslik(string m)
        {
            Metin(m, F(9f, true), Lacivert, Kenar, Y, En - 2 * Kenar);
            Y += 6f;
        }

        public void Not(string m, Color? renk = null, float pt = 7.2f)
        {
            Y += Paragraf(m, F(pt), renk ?? Gri, Kenar, Y, En - 2 * Kenar) + 1.2f;
        }

        // ---------------- tablo ----------------

        /// <summary>Satırları sığdığı kadar çizer; çizilen satır sayısını döndürür.</summary>
        public int Tablo(IList<Sutun> sutunlar, IList<Satir> satirlar, int bas, float altSinir = -1)
        {
            if (altSinir < 0) altSinir = Alt;
            float x0 = Kenar, gen = sutunlar.Sum(s => s.Genislik);
            var fBas = F(6.8f, true); var fBirim = F(5.8f); var fHucre = F(7.4f); var fIlk = F(7.4f, true);
            var fAlt = F(6.1f); var fGrup = F(7.6f, true);
            float a = CizimUzanti.SatirAraligi(fHucre);

            bool birimVar = sutunlar.Any(s => !string.IsNullOrEmpty(s.Birim));
            float hb = birimVar ? 7.4f : 4.6f;
            G.Dikdortgen(new RectangleF(x0, Y, gen, hb), BaslikZemin);
            G.Cizgi(Lacivert, 0.3f, x0, Y, x0 + gen, Y);
            float x = x0;
            foreach (var s in sutunlar)
            {
                Metin(s.Baslik, fBas, Koyu, x + 1.6f, Y + 0.9f, s.Genislik - 3.2f, s.Hiza);
                if (!string.IsNullOrEmpty(s.Birim)) Metin(s.Birim, fBirim, Gri, x + 1.6f, Y + 4.1f, s.Genislik - 3.2f, s.Hiza);
                x += s.Genislik;
            }
            Y += hb;
            G.Cizgi(Lacivert, 0.3f, x0, Y, x0 + gen, Y);

            int i = bas, cizilen = 0;
            for (; i < satirlar.Count; i++)
            {
                var r = satirlar[i];
                if (r.GrupBasligi != null)
                {
                    if (Y + 5.2f + 9f > altSinir) break;
                    G.Dikdortgen(new RectangleF(x0, Y, gen, 4.8f), Color.FromArgb(243, 245, 248));
                    Metin(r.GrupBasligi, fGrup, Kobalt, x0 + 1.6f, Y + 0.8f, gen - 3.2f);
                    Y += 4.8f; cizilen++;
                    G.Cizgi(Ince, 0.15f, x0, Y, x0 + gen, Y);
                    continue;
                }
                // sarılan sütunlar (ilk hariç sola hizalı) satır yüksekliğini belirler
                var sarili = new List<string>[sutunlar.Count];
                float hs = 4.0f;
                for (int c = 0; c < sutunlar.Count; c++)
                    if (sutunlar[c].Hiza == Hiza.Sol && c > 0)
                    {
                        sarili[c] = G.Sar(Hucre(r, c), fHucre, sutunlar[c].Genislik - 3.2f);
                        hs = Math.Max(hs, sarili[c].Count * a + 0.9f);
                    }
                float ha = string.IsNullOrEmpty(r.Alt) ? 0 : 3.2f;
                if (Y + hs + ha > altSinir) break;

                if (r.Vurgu)
                    G.Dikdortgen(new RectangleF(x0, Y, gen, hs + ha), Color.FromArgb(255, 244, 230));
                x = x0;
                for (int c = 0; c < sutunlar.Count; c++)
                {
                    var s = sutunlar[c];
                    bool son = c == sutunlar.Count - 1 && r.Durum.HasValue;
                    var renk = son ? (r.Durum.Value ? Uygun : Uygunsuz) : Koyu;
                    if (sarili[c] != null)
                        for (int k = 0; k < sarili[c].Count; k++) G.Yazi(sarili[c][k], fHucre, renk, x + 1.6f, Y + 0.3f + k * a);
                    else
                        Metin(Hucre(r, c), c == 0 ? fIlk : fHucre, renk, x + 1.6f, Y + 0.3f, s.Genislik - 3.2f, s.Hiza);
                    x += s.Genislik;
                }
                if (ha > 0)
                {
                    float ax = x0 + 1.6f;
                    if (r.UcTipi is int uc)
                    {
                        // statik şema simgesi + adı, ardından ayrıntılar
                        // sıradan durum (iki uç ankastre) yalnız gri simge; saplı ve konsol renkli ve adıyla öne çıkar
                        KirisSemasi.Ciz(G, ax - 0.4f, Y + hs + 0.65f, 9f, uc);
                        ax += 10.4f;
                        if (uc != 0)
                        {
                            string ad = KirisSemasi.Ad(uc);
                            var fk = F(6.1f, true);
                            Metin(ad, fk, KirisSemasi.Renk(uc), ax - 0.8f, Y + hs - 0.7f);
                            ax += Olc(ad, fk).Width + 1.7f;
                        }
                    }
                    Metin(r.Alt, fAlt, Gri, ax, Y + hs - 0.7f, x0 + gen - 1.6f - ax);
                }
                Y += hs + ha;
                G.Cizgi(Ince, 0.15f, x0, Y, x0 + gen, Y);
                cizilen++;
            }
            G.Cizgi(Lacivert, 0.3f, x0, Y, x0 + gen, Y);
            Y += 2.5f;
            return i - bas;
        }

        private static string Hucre(Satir r, int c) => c < r.Hucreler.Length ? r.Hucreler[c] : "";
    }
}
