using System.Drawing;
using System.Text.RegularExpressions;
using StatikRaporClaude.Cizim;
using StatikRaporClaude.Gpr;
using StatikRaporClaude.Modern;

namespace StatikRaporClaude.Dizgi
{
    /// <summary>Akışa yerleşen içerik bloğu. Bol: verilen yüksekliğe sığan parçayı ve kalanı döndürür; Ciz: parçayı s.Y'ye çizer.</summary>
    public abstract class Blok
    {
        public abstract (Blok Parca, Blok Kalan, float Yukseklik) Bol(ModernSayfa s, float alan);
        public abstract void Ciz(ModernSayfa s);
        /// <summary>Bloktan sonra aynı sayfada en az bu kadar yer kalmalı (başlıklar).</summary>
        public virtual float SonrakiIcinPay => 0;
    }

    public sealed class BolumBasiBlok : Blok
    {
        public GprBolum Bolum;
        public string Baslik, Aciklama;
        private float H => string.IsNullOrEmpty(Aciklama) ? 10.5f : 15f;
        public override float SonrakiIcinPay => 24f;
        public override (Blok, Blok, float) Bol(ModernSayfa s, float alan) => alan >= H ? (this, null, H) : (null, this, 0);
        public override void Ciz(ModernSayfa s)
        {
            float y0 = s.Y;
            s.Y += 2f;
            s.BolumBasligi(Baslik, Aciklama);
            s.Y = y0 + H;
        }
    }

    public sealed class BaslikBlok : Blok
    {
        public string Metin;
        public int Seviye;
        private float H => Seviye == 1 ? 6.4f : 5.2f;
        public override float SonrakiIcinPay => 16f;
        public override (Blok, Blok, float) Bol(ModernSayfa s, float alan) => alan >= H ? (this, null, H) : (null, this, 0);
        public override void Ciz(ModernSayfa s)
        {
            if (Seviye == 1)
            {
                s.G.Dikdortgen(new RectangleF(ModernSayfa.Kenar, s.Y + 1.2f, 0.9f, 3.8f), Logo.Yesil);
                // STA madde başlığı "A-STATİK YORUM" → "A · Statik yorum"
                var madde = Regex.Match(Metin, @"^([A-Z])-(\S.*)$");
                string m = madde.Success ? madde.Groups[1].Value + " · " + ModernBolumler.Tr(madde.Groups[2].Value) : ModernBolumler.Tr(Metin);
                s.Metin(m, ModernSayfa.F(9f, true), ModernSayfa.Lacivert, ModernSayfa.Kenar + 2.8f, s.Y + 0.9f, ModernSayfa.En - 2 * ModernSayfa.Kenar - 3);
            }
            else
                s.Metin(Metin, ModernSayfa.F(8f, true), ModernSayfa.Kobalt, ModernSayfa.Kenar, s.Y + 0.8f, ModernSayfa.En - 2 * ModernSayfa.Kenar);
            s.Y += H;
        }
    }

    /// <summary>
    /// Düz metin. Sütunlu satırlarda hiza karakter ızgarasıyla korunur; STA'nın 95 karakterde kırdığı düz yazı
    /// paragraflara birleştirilip sayfa genişliğinde yeniden sarılır.
    /// </summary>
    public sealed class MetinBlok : Blok
    {
        public List<MetinSatiri> Satirlar;
        /// <summary>false: STA satırları olduğu gibi kalır (her satır ayrı cümle; yapı yorumu).</summary>
        public bool YenidenSar = true;
        public const float SatirH = 3.55f, Pay = 1.2f;
        private const float Izgara = 1.62f;
        private List<Gorsel> _g;

        private sealed class Gorsel
        {
            public MetinSatiri Satir;   // ızgaralı satır
            public string Akan;         // sarılmış paragraf satırı
            public float X;
            public bool Kalin;
        }

        private static string Duz(MetinSatiri r) => r.Parcalar.Count == 1 ? r.Parcalar[0].Metin : null;
        private static bool Madde(string m) => Regex.IsMatch(m, @"^([-•*]|\d+\s*[-.)]\s|[a-zA-Z]\)\s)");

        private List<Gorsel> Hazirla(ModernSayfa s)
        {
            if (_g != null) return _g;
            _g = new List<Gorsel>();
            int girinti = Satirlar.SelectMany(x => x.Parcalar).Select(p => p.Sutun).DefaultIfEmpty(0).Min();
            int enUzun = Satirlar.Select(r => Duz(r) is string d ? r.Parcalar[0].Sutun - girinti + d.Length : 0).DefaultIfEmpty(0).Max();
            float x0 = ModernSayfa.Kenar, x1 = ModernSayfa.En - ModernSayfa.Kenar;
            for (int i = 0; i < Satirlar.Count; i++)
            {
                var r = Satirlar[i];
                string d = Duz(r);
                // paragraf: uzun (STA'nın kırdığı) düz satırlar, madde imiyle başlamayan devam satırlarıyla birleşir
                int j = i;
                if (d != null && enUzun >= 60 && YenidenSar)
                    while (j + 1 < Satirlar.Count && Duz(Satirlar[j]) is string a && Duz(Satirlar[j + 1]) is string b
                           && Satirlar[j].Parcalar[0].Sutun - girinti + a.Length >= enUzun * 0.72 && !Madde(b) && Satirlar[j + 1].Kalin == r.Kalin
                           && !Regex.IsMatch(a.TrimEnd(), "[:✓✗]$"))
                        j++;
                if (d == null || !YenidenSar || j == i && d.Length < 60)
                {
                    _g.Add(new Gorsel { Satir = r });
                    continue;
                }
                string metin = string.Join(" ", Satirlar.Skip(i).Take(j - i + 1).Select(x => Duz(x).Trim()));
                float ilkX = x0 + (r.Parcalar[0].Sutun - girinti) * Izgara;
                float devX = j > i ? x0 + (Satirlar[i + 1].Parcalar[0].Sutun - girinti) * Izgara : ilkX;
                var f = ModernSayfa.F(7.6f, r.Kalin);
                // ilk satır kendi girintisinde, devam satırları ikinci satırın girintisinde
                var kelimeler = metin.Split(' ');
                string aktif = "";
                float x = ilkX;
                foreach (var k in kelimeler)
                {
                    string aday = aktif.Length == 0 ? k : aktif + " " + k;
                    if (aktif.Length > 0 && s.Olc(aday, f).Width > x1 - x)
                    {
                        _g.Add(new Gorsel { Akan = aktif, X = x, Kalin = r.Kalin });
                        aktif = k; x = devX;
                    }
                    else aktif = aday;
                }
                if (aktif.Length > 0) _g.Add(new Gorsel { Akan = aktif, X = x, Kalin = r.Kalin });
                i = j;
            }
            return _g;
        }

        public override (Blok, Blok, float) Bol(ModernSayfa s, float alan)
        {
            var g = Hazirla(s);
            int n = Math.Min(g.Count, (int)((alan - Pay) / SatirH));
            if (n <= 0) return (null, this, 0);
            if (n >= g.Count) return (this, null, g.Count * SatirH + Pay);
            int girinti = Satirlar.SelectMany(x => x.Parcalar).Select(p => p.Sutun).DefaultIfEmpty(0).Min();
            return (new MetinBlok { Satirlar = Satirlar, _g = g.Take(n).ToList(), _girinti = girinti },
                    new MetinBlok { Satirlar = Satirlar, _g = g.Skip(n).ToList(), _girinti = girinti }, n * SatirH + Pay);
        }

        private int? _girinti;

        public override void Ciz(ModernSayfa s)
        {
            var g = Hazirla(s);
            int girinti = _girinti ?? Satirlar.SelectMany(x => x.Parcalar).Select(p => p.Sutun).DefaultIfEmpty(0).Min();
            foreach (var v in g)
            {
                if (v.Akan != null)
                    s.G.Yazi(v.Akan, ModernSayfa.F(7.6f, v.Kalin), v.Kalin ? ModernSayfa.Lacivert : ModernSayfa.Koyu, v.X, s.Y);
                else
                {
                    var f = ModernSayfa.F(7.6f, v.Satir.Kalin);
                    float sonX = 0;
                    foreach (var (sutun, metin) in v.Satir.Parcalar)
                    {
                        float x = Math.Max(ModernSayfa.Kenar + (sutun - girinti) * Izgara, sonX + 1.5f);
                        s.G.Yazi(metin, f, v.Satir.Kalin ? ModernSayfa.Lacivert : ModernSayfa.Koyu, x, s.Y);
                        sonX = x + s.Olc(metin, f).Width;
                    }
                }
                s.Y += SatirH;
            }
            s.Y += Pay;
        }
    }

    /// <summary>Anahtar : değer listesi. Değerler tek sütunda hizalı, düz metinle aynı yazı tipi ve satır aralığı.</summary>
    public sealed class AnahtarDegerBlok : Blok
    {
        public List<(string Anahtar, string Deger)> Satirlar;
        /// <summary>Aynı bölümdeki tüm anahtar-değer listeleri: değer sütunu hepsinde aynı hizada.</summary>
        public List<(string Anahtar, string Deger)> Grup;
        private const float SatirH = MetinBlok.SatirH;
        /// <summary>Değer sütunu konumu (bölünen parçalar aynı hizayı korur).</summary>
        private float? _xd;

        private float Xd(ModernSayfa s) => _xd ??= ModernSayfa.Kenar + Math.Min(100f, (Grup ?? Satirlar).Concat(Satirlar).Max(r => s.Olc(r.Anahtar, ModernSayfa.F(7.6f)).Width) + 5f);

        public override (Blok, Blok, float) Bol(ModernSayfa s, float alan)
        {
            float xd = Xd(s);
            int n = Math.Min(Satirlar.Count, (int)((alan - MetinBlok.Pay) / SatirH));
            if (n <= 0) return (null, this, 0);
            if (n >= Satirlar.Count) return (this, null, Satirlar.Count * SatirH + MetinBlok.Pay);
            return (new AnahtarDegerBlok { Satirlar = Satirlar.Take(n).ToList(), _xd = xd },
                    new AnahtarDegerBlok { Satirlar = Satirlar.Skip(n).ToList(), _xd = xd }, n * SatirH + MetinBlok.Pay);
        }

        public override void Ciz(ModernSayfa s)
        {
            float x0 = ModernSayfa.Kenar, x1 = ModernSayfa.En - ModernSayfa.Kenar, xd = Xd(s);
            var f = ModernSayfa.F(7.6f);
            foreach (var (a, d) in Satirlar)
            {
                s.Metin(a, f, ModernSayfa.Koyu, x0, s.Y, xd - x0 - 3f);
                s.Metin(":", f, ModernSayfa.Gri, xd - 2.2f, s.Y);
                s.Metin(d, f, ModernSayfa.Koyu, xd, s.Y, x1 - xd);
                s.Y += SatirH;
            }
            s.Y += MetinBlok.Pay;
        }
    }

    /// <summary>Sabit yükseklikte serbest çizim (deprem özet kutuları, spektrum grafiği).</summary>
    public sealed class CizimBlok : Blok
    {
        public float H;
        public Action<ModernSayfa> Cizici;
        public override (Blok, Blok, float) Bol(ModernSayfa s, float alan) => alan >= H ? (this, null, H) : (null, this, 0);
        public override void Ciz(ModernSayfa s)
        {
            float y0 = s.Y;
            Cizici(s);
            s.Y = y0 + H;
        }
    }

    /// <summary>Modern sonuç tablosu (kiriş / kolon betonarme) akış içinde: sığan satırlar, başlık her parçada tekrarlanır.</summary>
    public sealed class ModernTabloBlok : Blok
    {
        public IList<Sutun> Sutunlar;
        public IList<Satir> Satirlar;
        public int Bas;
        public int Son = -1;
        private IList<Satir> Parca => Son < 0 ? Satirlar : Satirlar.Take(Son).ToList();

        public override (Blok, Blok, float) Bol(ModernSayfa s, float alan)
        {
            // ölçüm yüzeyinde deneme çizimi: kaç satır sığdığını ve kullanılan yüksekliği verir
            var p = Parca;
            float y0 = s.Y;
            int n = s.Tablo(Sutunlar, p, Bas, y0 + alan);
            float h = s.Y - y0;
            s.Y = y0;
            if (n <= 0) return (null, this, 0);
            if (Bas + n >= p.Count) return (this, null, h);
            return (new ModernTabloBlok { Sutunlar = Sutunlar, Satirlar = Satirlar, Bas = Bas, Son = Bas + n },
                    new ModernTabloBlok { Sutunlar = Sutunlar, Satirlar = Satirlar, Bas = Bas + n, Son = Son }, h);
        }

        public override void Ciz(ModernSayfa s) => s.Tablo(Sutunlar, Parca, Bas, 100000f);
    }

    /// <summary>STA4 kütüphane resminin BEYKENT çizimi (başlık ve açıklamasıyla), sayfa genişliğinde.</summary>
    public sealed class ResimBlok : Blok
    {
        public int No;
        /// <summary>Küçültme oranı: resim ilgili yazıyla aynı sayfada kalsın diye kalan yere sığdırılır.</summary>
        public float Olcek = 1f;
        /// <summary>En küçük 6.2 pt yazı ≥ 4.8 pt kalır (okunur sınır).</summary>
        public const float EnKucukOlcek = 0.78f;
        private static float W => ModernSayfa.En - 2 * ModernSayfa.Kenar;
        public override (Blok, Blok, float) Bol(ModernSayfa s, float alan)
        {
            float h = StaResimleri.Yukseklik(s, No, W) + 1.5f;
            if (alan >= h) return (this, null, h);
            if (alan >= h * EnKucukOlcek) return (new ResimBlok { No = No, Olcek = alan / h }, null, alan);
            return (null, this, 0);
        }
        public override void Ciz(ModernSayfa s)
        {
            float h = StaResimleri.Yukseklik(s, No, W) + 1.5f;
            if (Olcek >= 0.999f) StaResimleri.Ciz(s, No, ModernSayfa.Kenar, s.Y, W);
            else
            {
                // kart ortalanarak oranla küçültülür
                s.G.Kaydet();
                s.G.Tasi(ModernSayfa.Kenar + W * (1 - Olcek) / 2, s.Y);
                s.G.Olcekle(Olcek);
                StaResimleri.Ciz(s, No, 0, 0, W);
                s.G.GeriYukle();
            }
            s.Y += h * Olcek;
        }
    }

    /// <summary>Kat diyaframları: kat kesiti, her kat bir döşeme şeridi, diyafram numarası dairede.</summary>
    public sealed class DiyaframBlok : Blok
    {
        public DiyaframOgesi Oge;
        private const float KatH = 5.2f, Gen = 70f, Etiket = 20f;
        private float H => Oge.Katlar.Count * KatH + 6f;
        public override (Blok, Blok, float) Bol(ModernSayfa s, float alan) => alan >= H || alan > 200 ? (this, null, H) : (null, this, 0);
        public override void Ciz(ModernSayfa s)
        {
            var g = s.G;
            float x0 = ModernSayfa.Kenar + Etiket, y = s.Y + 1f;
            var f = ModernSayfa.F(7.4f);
            var fn = ModernSayfa.F(6.8f, true);
            foreach (var (kat, diyaframlar) in Oge.Katlar)
            {
                g.Dikdortgen(new RectangleF(x0, y, Gen, KatH), Color.FromArgb(241, 244, 249));
                g.Cizgi(ModernSayfa.Kobalt, 0.5f, x0, y, x0 + Gen, y);                                    // kat döşemesi
                g.Cizgi(ModernSayfa.Gri, 0.3f, x0 + 1f, y, x0 + 1f, y + KatH);                            // kolonlar
                g.Cizgi(ModernSayfa.Gri, 0.3f, x0 + Gen - 1f, y, x0 + Gen - 1f, y + KatH);
                s.Metin($"Kat {kat}", f, ModernSayfa.Koyu, x0 - 3f, y + KatH / 2f - 1.4f, 0, Hiza.Sag);
                float aralik = Gen / (diyaframlar.Count + 1);
                for (int i = 0; i < diyaframlar.Count; i++)
                {
                    var m = new PointF(x0 + aralik * (i + 1), y + KatH / 2f);
                    g.Daire(m, 1.9f, ModernSayfa.Kobalt, null, 0);
                    s.Metin(diyaframlar[i], fn, Color.White, m.X - 3f, m.Y - 1.25f, 6f, Hiza.Orta);
                }
                y += KatH;
            }
            // zemin
            g.Cizgi(ModernSayfa.Koyu, 0.4f, x0 - 4f, y, x0 + Gen + 4f, y);
            for (float k = x0 - 3f; k < x0 + Gen + 4f; k += 2.2f) g.Cizgi(ModernSayfa.Gri, 0.2f, k, y + 1.6f, k + 1.6f, y);
            s.Metin("Dairedeki sayı: diyafram numarası", ModernSayfa.F(6.6f), ModernSayfa.Gri, x0 + Gen + 6f, s.Y + 1.6f);
            s.Y += H;
        }
    }

    /// <summary>STA kutu tablosu → modern tablo. Başlık her sayfada tekrarlanır.</summary>
    public sealed class TabloBlok : Blok
    {
        public TabloOgesi Tablo;
        public int Bas;
        public int Son = -1;
        private Duzen _d;

        private sealed class Duzen
        {
            public float[] X;          // sütun sol kenarları, mm (n+1)
            public YaziTipi F, Fb;
            public float SatirH, BaslikH;
            public Hiza[] Hiza;        // sütun içeriği çoğunlukla sayıysa sağ, değilse sol (başlık da buna uyar)
        }

        private static readonly Regex Sayi = new Regex(@"^[-+]?(\d+[.,]?\d*|\.\d+)([eE][-+]?\d+)?\s*%?$|^[-+]?\d+[.,]?\d*\s*(cm|m|t|tm|mm|kN)?$");

        /// <summary>STA4 eleman adı: kat kodu + harf + tire + numara (KZ-03, K3B-185, SZ-01, DZ-01 X).</summary>
        private static readonly Regex ElemanAdi = new Regex(@"^[A-ZÇĞİÖŞÜ][A-ZÇĞİÖŞÜ0-9]{0,3}-\d{1,4}\b");

        private Duzen Hesapla(ModernSayfa s)
        {
            if (_d != null) return _d;
            // tablo her zaman sayfa genişliğinde; sütunlar STA karakter genişlikleri oranında
            int toplam = Math.Max(1, Tablo.Sutunlar.Sum());
            float gen = ModernSayfa.En - 2 * ModernSayfa.Kenar;
            var x = new float[Tablo.Sutunlar.Count + 1];
            x[0] = ModernSayfa.Kenar;
            var sg = Tablo.Sutunlar.Select(c => gen * c / toplam).ToArray();
            // eleman adı sütunları (K3B-185, SZ-01…) en az 8 karakterlik yer alır; fark diğer sütunlardan oranla alınır.
            // STA4 adı dar sütunda keser ("K3B-18"), burada tam okunur.
            float adGen = s.Olc("K3B-1858", ModernSayfa.F(7.2f, true)).Width + 1.8f;
            for (int c = 0; c < sg.Length; c++)
            {
                var dolu = Tablo.Govde.SelectMany(r => r.Hucreler).Where(h => h.Ilk == c && h.Son == c && h.Metin.Length > 0).ToList();
                if (dolu.Count == 0 || dolu.Count(h => ElemanAdi.IsMatch(h.Metin)) * 2 < dolu.Count || sg[c] >= adGen) continue;
                float ek = adGen - sg[c], digeri = sg.Where((_, i) => i != c).Sum();
                if (digeri <= ek * 2) continue;
                for (int i = 0; i < sg.Length; i++) sg[i] = i == c ? adGen : sg[i] * (1 - ek / digeri);
            }
            for (int i = 0; i < sg.Length; i++) x[i + 1] = x[i] + sg[i];
            // yazı boyu: en uzun hücre sütununa sığacak kadar (7.2 → 5.2 pt)
            float pt = 7.2f;
            var hucreler = Tablo.Baslik.Concat(Tablo.Govde).SelectMany(r => r.Hucreler).Where(h => h.Metin.Length > 0).ToList();
            var orneklem = hucreler.GroupBy(h => (h.Ilk, h.Son)).Select(g => g.OrderByDescending(h => h.Metin.Length).First());
            foreach (var h in orneklem)
            {
                float yer = x[h.Son + 1] - x[h.Ilk] - 1.6f;
                float w = s.Olc(h.Metin, ModernSayfa.F(7.2f)).Width;
                if (w > yer && w > 0) pt = Math.Min(pt, 7.2f * yer / w);
            }
            pt = Math.Max(5.2f, pt);
            var f = ModernSayfa.F(pt);
            float satir = f.Boy * 1.18f + 0.55f;
            var hiza = new Hiza[Tablo.Sutunlar.Count];
            for (int c = 0; c < hiza.Length; c++)
            {
                var dolu = Tablo.Govde.Where(r => !r.AraBaslik).SelectMany(r => r.Hucreler).Where(h => h.Ilk == c && h.Son == c && h.Metin.Length > 0).ToList();
                bool sayisal = dolu.Count > 0 && dolu.Count(h => Sayi.IsMatch(h.Metin)) * 10 >= dolu.Count * 6;
                hiza[c] = sayisal && Tablo.SolSutunlar?.Contains(c) != true ? Hiza.Sag : Hiza.Sol;
            }
            return _d = new Duzen { X = x, F = f, Fb = ModernSayfa.F(pt, true), SatirH = satir, BaslikH = satir + 0.35f, Hiza = hiza };
        }

        /// <summary>Sütun sınırlarında ince dikey çizgi; birden çok sütuna yayılan hücrenin içinden geçmez.</summary>
        private static void DikeyAyirici(ICizimYuzeyi g, Duzen d, TabloSatir r, float y, float h)
        {
            for (int i = 1; i < d.X.Length - 1; i++)
                if (!r.Hucreler.Any(c => c.Ilk < i && i <= c.Son))
                    g.Cizgi(ModernSayfa.Ince, 0.1f, d.X[i], y + 0.35f, d.X[i], y + h - 0.35f);
        }

        private int SonSatir => Son < 0 ? Tablo.Govde.Count : Son;
        private float NotH => Son < 0 || Son >= Tablo.Govde.Count ? Tablo.Notlar.Count * 3.4f : 0;

        public override (Blok, Blok, float) Bol(ModernSayfa s, float alan)
        {
            var d = Hesapla(s);
            float h = Tablo.Baslik.Count * d.BaslikH + 0.5f;
            int i = Bas;
            for (; i < SonSatir; i++)
            {
                if (h + d.SatirH > alan) break;
                h += d.SatirH;
            }
            if (i == Bas) return (null, this, 0);
            if (i >= SonSatir)
            {
                float notlar = Tablo.Notlar.Count * 3.2f;
                if (h + notlar + 2f > alan && Tablo.Notlar.Count > 0 && i - Bas > 1) i--; // notlar sığmazsa son satırı taşı
                else return (new TabloBlok { Tablo = Tablo, Bas = Bas, Son = -1, _d = d }, null, h + notlar + 2f);
            }
            var parca = new TabloBlok { Tablo = Tablo, Bas = Bas, Son = i, _d = d };
            var kalan = new TabloBlok { Tablo = Tablo, Bas = i, Son = -1, _d = d };
            return (parca, kalan, h + 2f);
        }

        public override void Ciz(ModernSayfa s)
        {
            var d = Hesapla(s);
            var g = s.G;
            float x0 = d.X[0], x1 = d.X[^1];
            float ust = s.Y, ty = 0.28f - 0.09f * d.F.Boy;   // büyük harf ortası satır ortasında (Segoe UI)
            if (Tablo.Baslik.Count > 0)
            {
                // başlık: açık gri zemin, koyu yazı; yazıcıda düz koyu blok basılmaz
                g.Dikdortgen(new RectangleF(x0, s.Y, x1 - x0, Tablo.Baslik.Count * d.BaslikH), ModernSayfa.BaslikZemin);
                foreach (var r in Tablo.Baslik)
                {
                    DikeyAyirici(g, d, r, s.Y, d.BaslikH);
                    foreach (var h in r.Hucreler)
                        s.Metin(h.Metin, d.Fb, ModernSayfa.Koyu, d.X[h.Ilk] + 0.8f, s.Y + ty, d.X[h.Son + 1] - d.X[h.Ilk] - 1.6f, h.Ilk == h.Son ? d.Hiza[h.Ilk] : Hiza.Orta);
                    s.Y += d.BaslikH;
                }
                g.Cizgi(ModernSayfa.Lacivert, 0.3f, x0, s.Y, x1, s.Y);
            }
            g.Cizgi(ModernSayfa.Lacivert, 0.3f, x0, ust, x1, ust);
            for (int i = Bas; i < SonSatir; i++)
            {
                var r = Tablo.Govde[i];
                // grup başı biraz daha koyu çizgi, diğer satırlar ince çizgiyle ayrılır
                if (i > Bas) g.Cizgi(r.GrupBasi ? ModernSayfa.Gri : ModernSayfa.Ince, r.GrupBasi ? 0.25f : 0.12f, x0, s.Y, x1, s.Y);
                if (r.AraBaslik) g.Dikdortgen(new RectangleF(x0, s.Y + 0.1f, x1 - x0, d.SatirH - 0.2f), Color.FromArgb(243, 245, 248));
                else DikeyAyirici(g, d, r, s.Y, d.SatirH);
                foreach (var h in r.Hucreler)
                {
                    if (h.Metin.Length == 0) continue;
                    if (r.AraBaslik)
                    {
                        s.Metin(h.Metin, d.Fb, ModernSayfa.Kobalt, d.X[h.Ilk] + 0.8f, s.Y + ty, d.X[h.Son + 1] - d.X[h.Ilk] - 1.6f, h.Ilk == 0 ? Hiza.Sol : Hiza.Sag);
                        // kiriş adının yanında statik şema simgesi (STA4'teki ├──┤ kutu sembolünün karşılığı)
                        if (h.Ilk == 0 && r.UcTipi is int uc)
                        {
                            float sx = d.X[0] + 0.8f + s.Olc(h.Metin, d.Fb).Width + 1.5f;
                            float sw = Math.Min(9f, d.X[h.Son + 1] - sx - 0.8f);
                            if (sw > 4f) Modern.KirisSemasi.Ciz(g, sx, s.Y + d.SatirH / 2f, sw, uc);
                        }
                        continue;
                    }
                    var hiza = h.Ilk == h.Son ? (d.Hiza[h.Ilk] == Hiza.Sag && Sayi.IsMatch(h.Metin) ? Hiza.Sag : Hiza.Sol)
                        : Sayi.IsMatch(h.Metin) && Tablo.SolSutunlar?.Contains(h.Ilk) != true ? Hiza.Sag : Hiza.Sol;
                    var renk = h.Metin.Contains('✗') ? ModernSayfa.Uygunsuz : h.Metin.Contains('✓') ? ModernSayfa.Uygun : ModernSayfa.Koyu;
                    s.Metin(h.Metin, r.Vurgu ? d.Fb : d.F, renk, d.X[h.Ilk] + 0.8f, s.Y + ty, d.X[h.Son + 1] - d.X[h.Ilk] - 1.6f, hiza);
                }
                s.Y += d.SatirH;
            }
            g.Cizgi(ModernSayfa.Lacivert, 0.3f, x0, s.Y, x1, s.Y);
            s.Y += 0.8f;
            if (Son < 0 || Son >= Tablo.Govde.Count)
                foreach (var n in Tablo.Notlar)
                {
                    s.Metin(n, ModernSayfa.F(6.6f), ModernSayfa.Gri, x0, s.Y, ModernSayfa.En - ModernSayfa.Kenar - x0);
                    s.Y += 3.2f;
                }
            s.Y += 1.2f;
        }
    }

    /// <summary>Şekil: kaynak dikdörtgeni STA ölçeğinde (sığmazsa küçültülerek), bizim yazı tiplerimizle.</summary>
    public sealed class SekilBlok : Blok
    {
        public SekilOgesi Sekil;
        public float TamSayfa = 240f;
        private const float StaOlcek = 0.0963f; // mm / GPR birimi (STA sayfa genişliği 1890 → 182 mm)

        private float Olcek()
        {
            float w = ModernSayfa.En - 2 * ModernSayfa.Kenar;
            float o = Math.Min(StaOlcek, w / Sekil.Alan.Width);
            return Math.Min(o, TamSayfa / Sekil.Alan.Height);
        }

        public override (Blok, Blok, float) Bol(ModernSayfa s, float alan)
        {
            float h = Sekil.Alan.Height * Olcek() + 3f;
            return h <= alan ? (this, null, h) : (null, this, 0);
        }

        public override void Ciz(ModernSayfa s)
        {
            float o = Olcek();
            float w = Sekil.Alan.Width * o;
            float x = ModernSayfa.Kenar + Math.Max(0, (ModernSayfa.En - 2 * ModernSayfa.Kenar - w) / 2);
            GprCizici.CizBolge(s.G, Sekil.Sayfa, Sekil.Alan, new PointF(x, s.Y + 1f), o, Sekil.Yazilar);
            s.Y += Sekil.Alan.Height * o + 3f;
        }
    }

    /// <summary>Blokları sayfalara böler. Ölçüm, PDF ile aynı ölçüm yüzeyinde yapılır.</summary>
    public static class Akis
    {
        public const float Ust = 33f;
        public static float Alt => ModernSayfa.Boy - 18f;

        public sealed class Sayfa
        {
            public readonly List<Blok> Parcalar = new List<Blok>();
            public string UstBaslik;
        }

        public static List<Sayfa> Sayfala(List<Blok> bloklar, Func<ModernSayfa> olcum, Dictionary<GprBolum, int> bolumSayfasi)
        {
            var sayfalar = new List<Sayfa>();
            Sayfa aktif = null;
            float y = 0;
            string baslik = null;
            void YeniSayfa() { aktif = new Sayfa { UstBaslik = baslik }; sayfalar.Add(aktif); y = Ust; }

            int sira = 0;
            Blok bekleyen = null;
            while (bekleyen != null || sira < bloklar.Count)
            {
                var b = bekleyen ?? bloklar[sira++];
                bekleyen = null;
                if (b is BolumBasiBlok bb) baslik = ModernBolumler.Tr(bb.Bolum.Baslik);
                if (aktif == null) YeniSayfa();
                var s = olcum(); s.Y = y;
                float alan = Alt - y;
                // başlık bloğu sayfa sonunda yalnız kalmasın: ya alan azsa ya da arkasından gelen blok (büyük şekil) buraya hiç sığmıyorsa
                if (b.SonrakiIcinPay > 0 && aktif.Parcalar.Count > 0)
                {
                    bool tasi = alan < b.SonrakiIcinPay + 12f;
                    if (!tasi && sira < bloklar.Count && bloklar[sira] is not BolumBasiBlok and not BaslikBlok)
                    {
                        var (_, _, hb) = b.Bol(s, alan);
                        // tam sayfalık şekil başlığıyla aynı sayfaya sığacak kadar küçülür
                        if (bloklar[sira] is SekilBlok sk) sk.TamSayfa = Math.Min(sk.TamSayfa, Alt - Ust - hb - 4f);
                        var sonraki = olcum(); sonraki.Y = y + hb;
                        tasi = bloklar[sira].Bol(sonraki, alan - hb).Parca == null;
                    }
                    if (tasi) { YeniSayfa(); s.Y = y; alan = Alt - y; }
                }
                var (parca, kalan, h) = b.Bol(s, alan);
                // sayfada yalnız başlık varsa şekil yeni sayfaya kaçmaz: kalan yere küçültülür (başlık tek başına kalmasın)
                if (parca == null && b is SekilBlok sb0 && aktif.Parcalar.Count > 0 && aktif.Parcalar.All(p => p is BolumBasiBlok || p is BaslikBlok) && alan > 60f)
                {
                    sb0.TamSayfa = alan - 4f;
                    (parca, kalan, h) = b.Bol(s, alan);
                }
                if (parca == null)
                {
                    if (aktif.Parcalar.Count == 0)
                    {
                        // boş sayfaya da sığmıyor: şekil ise küçült, değilse zorla yerleştir
                        if (b is SekilBlok sk) { sk.TamSayfa = Alt - Ust - 4f; (parca, kalan, h) = b.Bol(s, alan); }
                        if (parca == null) { parca = b; kalan = null; h = alan; }
                    }
                    else { YeniSayfa(); bekleyen = b; continue; }
                }
                if (parca is BolumBasiBlok pb && bolumSayfasi != null) { bolumSayfasi[pb.Bolum] = sayfalar.Count - 1; aktif.UstBaslik ??= baslik; }
                aktif.Parcalar.Add(parca);
                y += h;
                if (kalan != null) { YeniSayfa(); bekleyen = kalan; }
            }
            return sayfalar;
        }
    }
}
