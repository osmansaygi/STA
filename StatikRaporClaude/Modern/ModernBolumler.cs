using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using StatikRaporClaude.Cizim;
using StatikRaporClaude.Gpr;
using StatikRaporClaude.Proje;

namespace StatikRaporClaude.Modern
{
    /// <summary>Tek rapor sayfası çizen iş: (sayfa, rapor bilgisi, sayfa no).</summary>
    public delegate void SayfaIsi(ModernSayfa s, RaporBilgisi r, int sayfaNo);

    /// <summary>
    /// Modern tasarımla basılan bölümler: deprem raporu, kiriş ve kolon betonarme sonuçları.
    /// Değerler GPR metninden aynen alınır, yeniden hesap yapılmaz.
    /// </summary>
    public static class ModernBolumler
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // GPR'li deprem raporu akışta basılır (BolumDizgi): özet + spektrum + GPR içeriğinin tamamı
        public static bool Destekler(GprBolum b) => !b.Grafik &&
            ((b.Anahtar == "DEPREM RAPORU" && b.Sayfalar.Count == 0) || b.Anahtar == "KIRIS BETONARME HESAP SONUCLARI" || b.Anahtar == "KOLON BETONARME HESAP SONUCLARI");

        /// <summary>Bölümün sayfa işleri. Sayfalama ölçüm yüzeyinde yapılır (PDF ile aynı ölçüm).</summary>
        public static List<SayfaIsi> Sayfalar(GprBolum b, ProjeKlasoru p, Func<ModernSayfa> olcum, ISet<string> katlar = null, Veri.StaVeri veri = null)
        {
            switch (b.Anahtar)
            {
                case "DEPREM RAPORU":
                {
                    // sanal bölüm (GPR'de yok) veya dosyadan seçeneği: ST4 + FeaMod
                    var d = b.Sayfalar.Count == 0 && veri != null ? DepremRaporu.Dosyadan(veri) : DepremRaporu.Oku(b);
                    var liste = new List<SayfaIsi> { (s, r, n) => DepremSayfasi(s, r, d, n, 1) };
                    if (d.Modal.Count > 0 || d.Kontroller.Count > 0) liste.Add((s, r, n) => DepremSayfasi(s, r, d, n, 2));
                    return liste;
                }
                case "KIRIS BETONARME HESAP SONUCLARI":
                {
                    var k = veri != null ? Veri.DosyaTablolari.KirisBetonarme(veri, katlar) : BetonarmeSonuclari.Kirisler(b).Where(x => Rapor.KatFiltresi.Dahil(x.Ad, katlar)).ToList();
                    var satirlar = KirisSatirlari(k, p);
                    return Sayfala(satirlar, olcum, (s, r, n, bas, ilk) => KirisSayfasi(s, r, k, satirlar, bas, n, ilk));
                }
                case "KOLON BETONARME HESAP SONUCLARI":
                {
                    var k = veri != null ? Veri.DosyaTablolari.KolonBetonarme(veri, katlar) : BetonarmeSonuclari.Kolonlar(b).Where(x => Rapor.KatFiltresi.Dahil(x.Ad, katlar)).ToList();
                    var satirlar = KolonSatirlari(k, p);
                    return Sayfala(satirlar, olcum, (s, r, n, bas, ilk) => KolonSayfasi(s, r, k, satirlar, bas, n, ilk));
                }
            }
            return new List<SayfaIsi>();
        }

        private delegate int TabloSayfasi(ModernSayfa s, RaporBilgisi r, int no, int bas, bool ilk);

        private static List<SayfaIsi> Sayfala(List<Satir> satirlar, Func<ModernSayfa> olcum, TabloSayfasi ciz)
        {
            var isler = new List<SayfaIsi>();
            var bos = new RaporBilgisi { Firma = "", ProjeAdi = "", St4Adi = "", Tarih = "" };
            int bas = 0;
            while (isler.Count == 0 || bas < satirlar.Count)
            {
                int b0 = bas;
                bool ilk = isler.Count == 0;
                int n = ciz(olcum(), bos, 0, b0, ilk);
                isler.Add((s, r, no) => ciz(s, r, no, b0, ilk));
                if (n <= 0) break;
                bas += n;
                if (isler.Count > 400) break;
            }
            return isler;
        }

        // ---------------- yardımcılar ----------------

        private static int KatSirasi(ProjeKlasoru p, string ad)
        {
            int tire = ad.IndexOf('-');
            if (tire <= 1) return int.MaxValue;
            int i = p.Katlar.FindIndex(k => k.Kod == ad.Substring(1, tire - 1) + "-");
            return i < 0 ? int.MaxValue : i;
        }

        private static string KatAdi(ProjeKlasoru p, string ad)
        {
            int tire = ad.IndexOf('-');
            if (tire <= 1) return "Diğer";
            string kod = ad.Substring(1, tire - 1) + "-";
            var kat = p.Katlar.FirstOrDefault(k => k.Kod == kod);
            return kat == null ? kod : $"{Tr(kat.Ad)} kat  ·  kot {kat.Kot.ToString("0.00", Inv)} m";
        }

        /// <summary>
        /// STA4 büyük harf başlığını cümle düzenine çevirir. Kelime bazında: Türkçe harf içeren kelimede I → ı,
        /// ASCII yazılmış kelimede ("ZEMIN") I → i; rakam/alt çizgi içeren kelimeler (STA4-CAD, N_ULK_02.ST4) aynen kalır.
        /// </summary>
        public static string Tr(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var tr = new CultureInfo("tr-TR");
            // Başlıkta noktalı İ geçiyorsa yazar Türkçe yazmıştır: tüm I'lar ı
            bool turkceBaslik = s.Contains('İ');
            var kelimeler = s.Split(' ');
            for (int i = 0; i < kelimeler.Length; i++)
            {
                string w = kelimeler[i];
                if (w.Any(char.IsDigit) || w.Contains('_') || w.Any(char.IsLower)) continue;
                kelimeler[i] = turkceBaslik || w.IndexOfAny("İŞĞÜÖÇ".ToCharArray()) >= 0 ? w.ToLower(tr) : w.ToLowerInvariant();
            }
            string k = string.Join(" ", kelimeler);
            int ilk = k.ToList().FindIndex(char.IsLetter);
            return ilk < 0 ? k : k.Substring(0, ilk) + char.ToUpper(k[ilk], tr) + k.Substring(ilk + 1);
        }

        private static string EnSik(IEnumerable<string> x) => x.GroupBy(e => e).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "—";

        // ---------------- deprem ----------------

        private static void DepremSayfasi(ModernSayfa s, RaporBilgisi r, DepremRaporu d, int no, int parca)
        {
            s.Cerceve(r, "Deprem hesabı", no);
            if (parca == 2)
            {
                s.BolumBasligi("Deprem raporu (devam)", null);
                ModalBolumu(s, d);
                return;
            }
            s.BolumBasligi("Deprem raporu", "TBDY 2018 · Mod birleştirme yöntemi ile modal analiz · " + (d.DosyaKaynakli ? "Parametreler ST4, modal değerler FeaMod dosyasından" : "Değerler STA4CAD hesabından aynen aktarılmıştır"));
            DepremKutucuklari(s, d);

            var sutun = new List<Sutun> { new Sutun("Parametre", null, 74, Hiza.Sol), new Sutun("Sembol", null, 28, Hiza.Sol), new Sutun("Değer", null, 80, Hiza.Sol) };
            var satir = d.Parametreler.Select(x => new Satir { Hucreler = new[] { Tr(x.Etiket), x.Sembol, x.Deger } }).ToList();
            s.AraBaslik("Deprem parametreleri");
            s.Tablo(sutun, satir, 0);

            if (d.Spektrum.Count > 1)
            {
                s.AraBaslik("Yatay elastik tasarım spektrumu");
                Spektrum(s, d, ModernSayfa.Kenar, s.Y, ModernSayfa.En - 2 * ModernSayfa.Kenar, 48f);
                s.Y += 52f;
            }
            if (d.Ra != null) s.Not(d.Ra, ModernSayfa.Lacivert);
        }

        /// <summary>Deprem raporu özet kutuları (zemin sınıfı, SDS/SD1, R, D, DTS, I).</summary>
        public static void DepremKutucuklari(ModernSayfa s, DepremRaporu d)
        {
            string ilk(string e) => (d.Deger(e) ?? "").Split(new[] { "   " }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "—";
            string bks = d.Deger("BİNA KULLANIM") ?? "";
            s.Kutucuklar(new List<(string, string, Color?)>
            {
                (ilk("ZEMİN SINIFI"), "Zemin sınıfı", null),
                (ilk("TASARIM SPECTRAL"), "SDS / SD1", null),
                (ilk("YAPI DAVRANIŞ"), "R  (taşıyıcı sistem davranış)", null),
                (ilk("SİSTEM DAYANIM"), "D  (dayanım fazlalığı)", null),
                (ilk("DEPREM TASARIM SINIFI"), "DTS", null),
                (Regex.Match(bks, @"I\s*=\s*\S+").Value.Replace(" ", ""), $"BKS {bks.Split(' ')[0]}  ·  bina önem katsayısı", null),
            });
        }

        private static void ModalBolumu(ModernSayfa s, DepremRaporu d)
        {
            if (d.Modal.Count > 0)
            {
                s.AraBaslik("Modal analiz — periyotlar ve kütle katılım oranları");
                var ms = new List<Sutun> { new Sutun("", null, 20, Hiza.Sol) };
                for (int i = 1; i <= 6; i++) ms.Add(new Sutun($"{i}. mod", null, 22));
                ms.Add(new Sutun("Toplam", null, 30));
                var mr = d.Modal.Where(m => m.Length > 0 && !m[0].StartsWith("Mod")).Select(m =>
                    new Satir { Hucreler = m.Select(x => x.Replace("Mxr%", "Mx  %").Replace("Myr%", "My  %")).ToArray(), Vurgu = m[0] == "T" }).ToList();
                s.Tablo(ms, mr, 0);
            }
            if (d.Kontroller.Count > 0)
            {
                s.AraBaslik("Kontroller");
                // ✓ Segoe UI'da yok (PDF'te kutu çıkar): uygun satırlar yeşil "UYGUN" etiketiyle
                foreach (var k in d.Kontroller)
                    s.Not((k.Contains("yeterli") ? "UYGUN  ·  " : "•  ") + k, k.Contains("yeterli") ? ModernSayfa.Uygun : ModernSayfa.Lacivert, 7.4f);
            }
        }

        /// <summary>
        /// Elastik tasarım spektrumu: yatay Sae(T) (dolgulu), düşey SaeD(T) (TBDY 2.5: TAD = TA/3, TBD = TB/3, TLD = TL/2),
        /// TA/TB köşe periyotları ve yapının hakim periyotları Tx/Ty ile o periyottaki Sae değeri.
        /// </summary>
        public static void Spektrum(ModernSayfa s, DepremRaporu d, float x, float y, float w, float h)
        {
            var g = s.G;
            float sol = x + 10, alt = y + h - 6, gen = w - 13, yuk = h - 9;
            double tMax = Math.Max(3, d.Spektrum.Max(p => p.T)), saMax = d.Spektrum.Max(p => p.Sa) * 1.18;
            PointF P(double t, double sa) => new PointF(sol + (float)(t / tMax) * gen, alt - (float)(Math.Min(sa, saMax) / saMax) * yuk);
            var f = ModernSayfa.F(6.2f);
            var fk = ModernSayfa.F(6.2f, true);
            for (double t = 0; t <= tMax + 1e-6; t += 0.5)
            {
                bool tam = Math.Abs(t - Math.Round(t)) < 1e-6;
                g.Cizgi(tam ? ModernSayfa.Cizgi : Color.FromArgb(236, 239, 243), 0.15f, P(t, 0).X, P(t, 0).Y, P(t, saMax).X, P(t, saMax).Y);
                if (tam) s.Metin(t.ToString("0", Inv), f, ModernSayfa.Gri, P(t, 0).X, alt + 0.8f, 0, Hiza.Orta);
            }
            for (double sa = 0; sa <= saMax; sa += 0.1)
            {
                g.Cizgi(ModernSayfa.Cizgi, 0.15f, P(0, sa).X, P(0, sa).Y, P(tMax, sa).X, P(tMax, sa).Y);
                s.Metin(sa.ToString("0.0", Inv), f, ModernSayfa.Gri, sol - 1.5f, P(0, sa).Y - 1.2f, 0, Hiza.Sag);
            }
            g.Cizgi(ModernSayfa.Koyu, 0.3f, sol, alt, sol + gen, alt);
            g.Cizgi(ModernSayfa.Koyu, 0.3f, sol, alt, sol, alt - yuk);

            // köşe periyotları
            var notlar = string.Join(" ", d.SpektrumNotlari);
            double Bul(string desen) { var m = Regex.Match(notlar, desen); return m.Success ? BetonarmeSonuclari.Sayi(m.Groups[1].Value) : double.NaN; }
            double ta = Bul(@"Ta=[^=]*=([\d.]+)"), tb = Bul(@"Tb=[^=]*=([\d.]+)");
            double sds = d.Spektrum.Max(p => p.Sa);
            const double tl = 6;

            // yatay spektrum: GPR noktaları
            var noktalar = d.Spektrum.Where(p => p.T <= tMax).Select(p => P(p.T, p.Sa)).ToArray();
            g.Poligon(noktalar.Append(P(d.Spektrum.Where(p => p.T <= tMax).Last().T, 0)).Append(P(0, 0)).ToArray(), Color.FromArgb(36, ModernSayfa.Kobalt));
            g.CokluCizgi(noktalar, ModernSayfa.Kobalt, 0.6f);

            // düşey spektrum (TBDY 2018 Denk. 2.5)
            if (!double.IsNaN(ta) && !double.IsNaN(tb) && tb > 0)
            {
                double tad = ta / 3, tbd = tb / 3, tld = tl / 2;
                double SaeD(double t) => t <= tad ? (0.32 + 0.48 * t / tad) * sds : t <= tbd ? 0.8 * sds : t <= tld ? 0.8 * sds * tbd / t : 0.8 * sds * tbd * tld / (t * t);
                var dus = Enumerable.Range(0, 301).Select(i => tMax * i / 300.0).Select(t => P(t, SaeD(t))).ToArray();
                for (int i = 0; i + 1 < dus.Length; i += 2) g.Cizgi(ModernSayfa.Turuncu, 0.45f, dus[i].X, dus[i].Y, dus[i + 1].X, dus[i + 1].Y);
            }

            // TA, TB: kesikli çizgi, kısa etiket platonun üstünde (değerleri bağıntılarda)
            foreach (var (t, ad) in new[] { (ta, "TA"), (tb, "TB") })
                if (!double.IsNaN(t))
                {
                    g.Cizgi(ModernSayfa.Gri, 0.25f, P(t, 0).X, P(t, 0).Y, P(t, sds).X, P(t, sds).Y - 1f, true);
                    s.Metin(ad, f, ModernSayfa.Gri, P(t, 0).X - 3f, P(t, sds).Y - 3.6f, 6f, Hiza.Orta);
                }

            // yapı periyotları: eğri üzerinde nokta, değerler açıklamada
            double Sae(double t) => double.IsNaN(ta) || double.IsNaN(tb) ? double.NaN
                : t < ta ? (0.4 + 0.6 * t / ta) * sds : t <= tb ? sds : t <= tl ? sds * tb / t : sds * tb * tl / (t * t);
            var periyotlar = new List<(string Metin, Color Renk)>();
            foreach (var (t, ad, renk) in new[] { (d.Tx, "Tx", Color.FromArgb(200, 40, 40)), (d.Ty, "Ty", Color.FromArgb(30, 140, 70)) })
                if (t is double tt && tt > 0 && tt <= tMax && !double.IsNaN(Sae(tt)))
                {
                    var p = P(tt, Sae(tt));
                    g.Cizgi(renk, 0.35f, p.X, alt, p.X, p.Y);
                    g.Daire(p, 0.8f, renk, null, 0);
                    s.Metin(ad, fk, renk, p.X + 1.2f, ad == "Ty" ? alt - 7f : alt - 3.6f);
                    periyotlar.Add(($"{ad} = {tt.ToString("0.000", Inv)} s   →   Sae({ad}) = {Sae(tt).ToString("0.000", Inv)} g", renk));
                }

            s.Metin("Sae (g)", f, ModernSayfa.Gri, sol + 1f, y - 1.2f);
            s.Metin("T (s)", f, ModernSayfa.Gri, sol + gen, alt + 0.8f, 0, Hiza.Sag);
            // açıklama ve bağıntılar sağ üstte
            float lx = sol + gen * 0.47f, ly = y + 1f;
            g.Dikdortgen(new RectangleF(lx, ly + 1f, 6f, 2.2f), Color.FromArgb(70, ModernSayfa.Kobalt));
            g.Cizgi(ModernSayfa.Kobalt, 0.6f, lx, ly + 1f, lx + 6f, ly + 1f);
            s.Metin("Yatay elastik tasarım spektrumu  Sae(T)", f, ModernSayfa.Koyu, lx + 7.5f, ly);
            ly += 3.4f;
            g.Cizgi(ModernSayfa.Turuncu, 0.45f, lx, ly + 1.4f, lx + 2.5f, ly + 1.4f);
            g.Cizgi(ModernSayfa.Turuncu, 0.45f, lx + 3.5f, ly + 1.4f, lx + 6f, ly + 1.4f);
            s.Metin("Düşey elastik tasarım spektrumu  SaeD(T)", f, ModernSayfa.Koyu, lx + 7.5f, ly);
            ly += 3.6f;
            foreach (var (m, renk) in periyotlar)
            {
                g.Daire(new PointF(lx + 3f, ly + 1.4f), 0.8f, renk, null, 0);
                s.Metin("Yapı periyodu  " + m, fk, renk, lx + 7.5f, ly);
                ly += 3.4f;
            }
            ly += 0.8f;
            foreach (var n in d.SpektrumNotlari) { s.Metin(n, ModernSayfa.F(6.2f), ModernSayfa.Lacivert, lx, ly); ly += 3f; }
            if (!double.IsNaN(ta))
                s.Metin($"TAD=TA/3={(ta / 3).ToString("0.000", Inv)}, TBD=TB/3={(tb / 3).ToString("0.000", Inv)}, TLD=TL/2=3   SaeD=0.8·Sds (TAD<T<TBD)", ModernSayfa.F(6.2f), ModernSayfa.Lacivert, lx, ly);
        }

        // ---------------- kiriş ----------------

        private static readonly List<Sutun> KirisSutun = new List<Sutun>
        {
            // boyuna donatı metni uzun: geniş sütun, satır sarması az (sayfa sayısı)
            new Sutun("Kiriş", null, 17, Hiza.Sol), new Sutun("b / h", "cm", 11, Hiza.Orta),
            new Sutun("M sol", "tm", 12), new Sutun("M açık", "tm", 12), new Sutun("M sağ", "tm", 12),
            new Sutun("As sol", "cm²", 13), new Sutun("As açık", "cm²", 13), new Sutun("As sağ", "cm²", 13),
            new Sutun("Boyuna donatı", null, 50, Hiza.Sol), new Sutun("Etriye", null, 14, Hiza.Sol), new Sutun("Wk", "mm", 15)
        };

        private static List<Satir> KirisSatirlari(List<KirisSonucu> k, ProjeKlasoru p)
        {
            var liste = new List<Satir>();
            string kat = null;
            foreach (var x in k.OrderBy(x => KatSirasi(p, x.Ad)))
            {
                string yeni = KatAdi(p, x.Ad);
                if (yeni != kat) { liste.Add(new Satir { GrupBasligi = yeni }); kat = yeni; }
                string konum = x.Mduz[2].Trim('(', ')', ' ');
                liste.Add(new Satir
                {
                    Hucreler = new[] { x.Ad, $"{x.B}/{x.D}", x.MaxM[0], x.MaxM[2], x.MaxM[3], x.As[0], x.As[2], x.As[3],
                        string.Join(" + ", x.Boyuna), x.Etriye, x.Wk.Replace("Wk=", "") },
                    UcTipi = x.UcTipi,
                    Alt = $"alt M: sol {x.MaxM[1]}, sağ {x.MaxM[4]} tm   ·   alt As: sol {x.As[1]}, sağ {x.As[4]} cm²   ·   Asw: {x.Asw[0]} / {x.Asw[3]} cm²   ·   açıklık momenti konumu {konum}",
                    Durum = x.Wk == Veri.DosyaTablolari.Yok ? null : x.WkUygun
                });
            }
            return liste;
        }

        private const string KirisBaslik = "Kiriş betonarme hesap sonuçları", KolonBaslik = "Kolon betonarme hesap sonuçları";
        private const string KirisAciklama = "TS500 · TBDY 2018 · Momentler tm, donatı alanları cm² · Wk: ACI 318 Gergely–Lutz (fs = 0.6 fy)";
        private const string KolonAciklama = "TS500 · TBDY 2018 · Her kolon için 6 kombinasyondan belirleyici satır · Değerler STA4CAD hesabından aynen aktarılmıştır";

        private static void KirisKutucuklari(ModernSayfa s, List<KirisSonucu> k)
        {
            double AsMax(KirisSonucu x) => x.As.Select(BetonarmeSonuclari.Sayi).Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Max();
            var enBuyuk = k.OrderByDescending(AsMax).FirstOrDefault();
            int uygun = k.Count(x => x.WkUygun);
            var kutular = new List<(string, string, Color?)>
            {
                (k.Count.ToString(), "kiriş açıklığı", null),
                k.All(x => x.Wk == Veri.DosyaTablolari.Yok) ? ("—", "çatlak genişliği Wk (dosyalarda yok)", (Color?)ModernSayfa.Gri) : ($"{uygun} / {k.Count}", "çatlak genişliği Wk < 0.4 mm", uygun == k.Count ? ModernSayfa.Uygun : ModernSayfa.Uygunsuz),
                (enBuyuk == null ? "—" : AsMax(enBuyuk).ToString("0.00", Inv) + " cm²", $"en büyük As  ·  {enBuyuk?.Ad}", null),
                (EnSik(k.Select(x => x.Etriye)), "en sık etriye", null),
            };
            if (k.Any(x => x.UcTipi != null))
            {
                int konsol = k.Count(x => x.UcTipi is int u && KirisSemasi.Konsol(u)), sapli = k.Count(x => x.UcTipi is int u && KirisSemasi.Sapli(u));
                kutular.Add(($"{konsol} / {sapli}", "konsol / kirişe saplı", konsol > 0 ? ModernSayfa.Turuncu : null));
            }
            s.Kutucuklar(kutular);
        }

        private static void KolonKutucuklari(ModernSayfa s, List<KolonSonucu> k)
        {
            var oran = k.Select(x => BetonarmeSonuclari.Sayi(x.AsAc)).Where(v => !double.IsNaN(v)).ToList();
            var nMax = k.OrderByDescending(x => x.Kombinasyonlar.Max(c => BetonarmeSonuclari.Sayi(c[1]))).FirstOrDefault();
            s.Kutucuklar(new List<(string, string, Color?)>
            {
                (k.Count.ToString(), "kolon / perde", null),
                (nMax == null ? "—" : nMax.Kombinasyonlar.OrderByDescending(c => BetonarmeSonuclari.Sayi(c[1])).First()[1] + " t", $"en büyük eksenel yük  ·  {nMax?.Ad}", null),
                (oran.Count > 0 ? oran.Min().ToString("0.0000", Inv) + " – " + oran.Max().ToString("0.0000", Inv) : "—", "boyuna donatı oranı As/Ac", null),
                (EnSik(k.Select(x => x.Etriye)), "en sık etriye", null),
            });
        }

        /// <summary>
        /// Kiriş / kolon betonarme sonuçları akış blokları: bölüm başlığı, özet kutuları ve tablo önceki bölümün
        /// bıraktığı yerden devam eder (yarım boş sayfa kalmaz). Desteklenmeyen bölümde null.
        /// </summary>
        public static List<Dizgi.Blok> AkisBloklari(GprBolum b, ProjeKlasoru p, ISet<string> katlar, Veri.StaVeri veri)
        {
            if (b.Grafik) return null;
            switch (b.Anahtar)
            {
                case "KIRIS BETONARME HESAP SONUCLARI":
                {
                    var k = veri != null ? Veri.DosyaTablolari.KirisBetonarme(veri, katlar) : BetonarmeSonuclari.Kirisler(b).Where(x => Rapor.KatFiltresi.Dahil(x.Ad, katlar)).ToList();
                    return new List<Dizgi.Blok>
                    {
                        new Dizgi.BolumBasiBlok { Bolum = b, Baslik = KirisBaslik, Aciklama = KirisAciklama },
                    }.Concat(Dizgi.BolumDizgi.ResimBloklari(b)).Concat(new List<Dizgi.Blok>
                    {
                        new Dizgi.CizimBlok { H = 17f, Cizici = s => KirisKutucuklari(s, k) },
                        new Dizgi.CizimBlok { H = k.Any(x => x.UcTipi != null) ? 5f : 0f, Cizici = s => { if (k.Any(x => x.UcTipi != null)) KirisSemasi.Aciklama(s, ModernSayfa.Kenar, s.Y); } },
                        new Dizgi.ModernTabloBlok { Sutunlar = KirisSutun, Satirlar = KirisSatirlari(k, p) },
                    }).ToList();
                }
                case "KOLON BETONARME HESAP SONUCLARI":
                {
                    var k = veri != null ? Veri.DosyaTablolari.KolonBetonarme(veri, katlar) : BetonarmeSonuclari.Kolonlar(b).Where(x => Rapor.KatFiltresi.Dahil(x.Ad, katlar)).ToList();
                    return new List<Dizgi.Blok>
                    {
                        new Dizgi.BolumBasiBlok { Bolum = b, Baslik = KolonBaslik, Aciklama = KolonAciklama },
                    }.Concat(Dizgi.BolumDizgi.ResimBloklari(b)).Concat(new List<Dizgi.Blok>
                    {
                        new Dizgi.CizimBlok { H = 17f, Cizici = s => KolonKutucuklari(s, k) },
                        new Dizgi.ModernTabloBlok { Sutunlar = KolonSutun, Satirlar = KolonSatirlari(k, p) },
                    }).ToList();
                }
            }
            return null;
        }

        private static int KirisSayfasi(ModernSayfa s, RaporBilgisi r, List<KirisSonucu> k, List<Satir> satirlar, int bas, int no, bool ilk)
        {
            s.Cerceve(r, "Betonarme · Kirişler", no);
            if (ilk)
            {
                s.BolumBasligi(KirisBaslik, KirisAciklama);
                KirisKutucuklari(s, k);
            }
            else s.BolumBasligi(KirisBaslik + " (devam)", null);
            return s.Tablo(KirisSutun, satirlar, bas);
        }

        // ---------------- kolon ----------------

        private static readonly List<Sutun> KolonSutun = new List<Sutun>
        {
            new Sutun("Kolon", null, 17, Hiza.Sol), new Sutun("bx / by", "cm", 15, Hiza.Orta), new Sutun("Hk", "m", 10),
            new Sutun("N maks", "t", 14), new Sutun("Belirleyici", "kombinasyon", 21, Hiza.Orta), new Sutun("M major", "tm", 14),
            new Sutun("M minor", "tm", 14), new Sutun("ρ", null, 13), new Sutun("As gerekli", "cm²", 15),
            new Sutun("Boyuna donatı", null, 29, Hiza.Sol), new Sutun("Etriye", null, 20, Hiza.Sol)
        };

        private static List<Satir> KolonSatirlari(List<KolonSonucu> k, ProjeKlasoru p)
        {
            var liste = new List<Satir>();
            string kat = null;
            // GPR kolonları katlar arasında karışık sıralar (S1-01, SZ-01, S1-02…): ST4 kat sırasına göre grupla
            foreach (var x in k.OrderBy(x => KatSirasi(p, x.Ad)))
            {
                string yeni = KatAdi(p, x.Ad);
                if (yeni != kat) { liste.Add(new Satir { GrupBasligi = yeni }); kat = yeni; }
                var nMax = x.Kombinasyonlar.OrderByDescending(c => BetonarmeSonuclari.Sayi(c[1])).First();
                var belirleyici = x.Kombinasyonlar.OrderByDescending(c => Math.Abs(BetonarmeSonuclari.Sayi(c[3]))).First();
                liste.Add(new Satir
                {
                    Hucreler = new[] { x.Ad, $"{x.Bx}/{x.By}", x.Hk, nMax[1], belirleyici[0], belirleyici[3], belirleyici[2],
                        x.Kombinasyonlar.Select(c => c[5]).Max(), x.Kombinasyonlar.Select(c => c[6]).Max(), x.Boyuna, x.Etriye },
                    Alt = $"I/J düğüm {x.I}/{x.J}   ·   βx {x.BetaX}   βy {x.BetaY}   ·   As/Ac {x.AsAc}   ·   belirleyici: en büyük |M major| veren kombinasyon"
                });
            }
            return liste;
        }

        private static int KolonSayfasi(ModernSayfa s, RaporBilgisi r, List<KolonSonucu> k, List<Satir> satirlar, int bas, int no, bool ilk)
        {
            s.Cerceve(r, "Betonarme · Kolonlar", no);
            if (ilk)
            {
                s.BolumBasligi(KolonBaslik, KolonAciklama);
                KolonKutucuklari(s, k);
            }
            else s.BolumBasligi(KolonBaslik + " (devam)", null);
            return s.Tablo(KolonSutun, satirlar, bas);
        }
    }
}
