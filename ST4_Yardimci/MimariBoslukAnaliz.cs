using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ST4Yardimci
{
    /// <summary>
    /// MIMARIBOSLUK hesap çekirdeği (AutoCAD'den bağımsız).
    /// 1) Hizalama: aynı ölçülü kolon/perde dikdörtgenlerinin öteleme oylaması (birim ölçeği 1/10/0.1/100/0.01 denenir).
    /// 2) Aday: mimaride kapalı kontur + (köşegen X / şaft-baca-asansör yazısı / katman adı); kolon üstü ve mevcut boşluk elenir.
    /// 3) Fark: kalıptaki boşluğun mimaride konturu yoksa "mimaride yok", ölçü farklıysa "ölçü farkı".
    /// Tüm ölçüler kalıp çizim birimindedir (cm); mimari noktalar Hizala sonucu ile kalıba taşınır.
    /// </summary>
    internal static class MimariBoslukAnaliz
    {
        internal struct Kutu
        {
            public double X0, Y0, X1, Y1;
            public Kutu(double x0, double y0, double x1, double y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
            public double W => X1 - X0;
            public double H => Y1 - Y0;
            public double Cx => (X0 + X1) / 2;
            public double Cy => (Y0 + Y1) / 2;
            public double Alan => Math.Max(0, W) * Math.Max(0, H);
            public bool Icerir(double x, double y, double tol = 0) => x >= X0 - tol && x <= X1 + tol && y >= Y0 - tol && y <= Y1 + tol;
            public bool IcindeMi(Kutu d, double tol) => X0 >= d.X0 - tol && Y0 >= d.Y0 - tol && X1 <= d.X1 + tol && Y1 <= d.Y1 + tol;
            public double Kesisim(Kutu o)
            {
                double w = Math.Min(X1, o.X1) - Math.Max(X0, o.X0), h = Math.Min(Y1, o.Y1) - Math.Max(Y0, o.Y0);
                return w > 0 && h > 0 ? w * h : 0;
            }
            public double IoU(Kutu o) { double k = Kesisim(o); double b = Alan + o.Alan - k; return b > 0 ? k / b : 0; }
            public double KenarFarki(Kutu o) => Math.Max(Math.Max(Math.Abs(X0 - o.X0), Math.Abs(X1 - o.X1)), Math.Max(Math.Abs(Y0 - o.Y0), Math.Abs(Y1 - o.Y1)));
            public static Kutu Of(IList<double[]> p)
            {
                double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                foreach (var q in p) { x0 = Math.Min(x0, q[0]); y0 = Math.Min(y0, q[1]); x1 = Math.Max(x1, q[0]); y1 = Math.Max(y1, q[1]); }
                return new Kutu(x0, y0, x1, y1);
            }
        }

        /// <summary>Kapalı çokgen (köşeler sırayla).</summary>
        internal sealed class Kontur { public string Katman; public List<double[]> Noktalar; public Kutu K; }
        internal sealed class Parca { public double X0, Y0, X1, Y1; public string Katman; }
        internal sealed class Yazi { public string Metin; public double X, Y; }

        internal sealed class Kaynak
        {
            public readonly List<Kontur> Konturlar = new List<Kontur>();
            public readonly List<Parca> Parcalar = new List<Parca>();
            public readonly List<Yazi> Yazilar = new List<Yazi>();
            /// <summary>Merdiven kipi: tüm düz parçalar ve 1–80 arası sayı yazıları (basamak numarası).</summary>
            public readonly List<Parca> Cizgiler = new List<Parca>();
            public readonly List<Yazi> Sayilar = new List<Yazi>();
            /// <summary>"PLAN"/"KAT" geçen yazılar: ayrı mimari dosyada kat planı seçimi için başlık.</summary>
            public readonly List<Yazi> Basliklar = new List<Yazi>();
            /// <summary>Üst düzey adlı bloklar ve kapsamları (kat planı blok olarak çizilmişse etiket).</summary>
            public readonly List<KeyValuePair<string, Kutu>> Bloklar = new List<KeyValuePair<string, Kutu>>();
        }

        internal sealed class Hizalama
        {
            public double Olcek = 1, Dx, Dy;
            public int Eslesen, KalipKolon, MimariKolon, EsitAday;
            /// <summary>Kolonları kalıba uyan ayrı kat planları (en iyisi başta); ayrı mimari dosyada kat seçimi için.</summary>
            public List<Hizalama> Adaylar = new List<Hizalama>();
            public double Uygula(double v, bool y) => v * Olcek + (y ? Dy : Dx);
            public double[] Uygula(double[] p) => new[] { p[0] * Olcek + Dx, p[1] * Olcek + Dy };
            public string Ozet => string.Format(CultureInfo.InvariantCulture,
                "{0}/{1} kalıp kolonu eşleşti (mimari {2}), ölçek {3}, öteleme ({4:0.0}, {5:0.0})",
                Eslesen, KalipKolon, MimariKolon, Olcek, Dx, Dy);
        }

        internal enum Tur { Yeni, Mevcut, OlcuFarki, MimarideYok }

        internal sealed class Sonuc
        {
            public Tur Tur;
            public Kutu K;                // kalıp koordinatı
            public List<double[]> Noktalar; // yeni boşlukta çizilecek çokgen (kalıp koordinatı)
            public Kutu KalipK;           // mevcut/ölçü farkında kalıptaki boşluk
            public string Neden = "";
            public string Katman = "";
            internal List<int> YaziNo = new List<int>();
            internal bool SadeceYazi;
            public bool Zayif;
        }

        private const double KolonMin = 20, KolonMax = 400, OlcuTol = 1.0;

        private static readonly string[] YaziAnahtar =
        {
            "ŞAFT", "SAFT", "SHAFT", "BACA", "ASANSÖR", "ASANSOR", "BOŞLUK", "BOSLUK", "GALERİ", "GALERI",
            "TESİSAT", "TESISAT", "MEKANİK", "MEKANIK", "MEK.", "ELEKTRİK", "ELEKTRIK", "ELK", "HAVALANDIRMA", "LIFT", "VOID"
        };
        // Tek başına yeterli yazı; diğer anahtarlar (mekanik, elektrik, tesisat…) yalnız zayıf aday üretir.
        private static readonly string[] GucluYazi =
        {
            "ŞAFT", "SAFT", "SHAFT", "BACA", "ASANSÖR", "ASANSOR", "BOŞLUK", "BOSLUK", "GALERİ", "GALERI", "LIFT", "VOID"
        };
        private static readonly string[] YaziRet = { "ODA", "OD.", "PANO", "DOLAP", "KAPI" };
        private static readonly string[] KatmanAnahtar =
        {
            "SAFT", "ŞAFT", "SHAFT", "BACA", "ASANS", "BOSLUK", "BOŞLUK", "GALERI", "GALERİ", "TESISAT", "TESİSAT", "LIFT", "ELEVATOR", "VOID"
        };
        private static readonly string[] KolonKatman = { "KOLON", "PERDE", "COLUMN", "WALL-STR", "BETONARME" };
        // Tefriş, bitki, ölçü, antet, kapı/pencere ve merdivendeki çarpı/kutu boşluk sayılmaz.
        private static readonly string[] RetKatman =
        {
            "TEFR", "MOBİLYA", "MOBILYA", "FURN", "BİTKİ", "BITKI", "AĞAÇ", "AGAC", "ARBOL", "ÖLÇÜ", "OLCU", "DIM",
            "ANTET", "PENCERE", "KAPI", "DOĞRAMA", "DOGRAMA", "MERDİVEN", "MERDIVEN", "KORKULUK", "OTOPARK", "ARAÇ", "ARAC"
        };

        private static string Ust(string s) => (s ?? "").ToUpper(new CultureInfo("tr-TR"));
        private static bool Iceriyor(string s, string[] a) { string u = Ust(s); return a.Any(k => u.Contains(k)); }

        internal static bool AnahtarYazi(string metin) => (Iceriyor(metin, YaziAnahtar) || HavaBacasi(metin)) && !Iceriyor(metin, YaziRet);

        /// <summary>"H.B." / "H.B" / "HB" (hava bacası) kısaltması; kelime içinde geçen HB sayılmaz.</summary>
        internal static bool HavaBacasi(string metin) =>
            System.Text.RegularExpressions.Regex.IsMatch(Ust(metin), @"(^|[^A-ZÇĞİÖŞÜ0-9])H\.?\s?B\.?($|[^A-ZÇĞİÖŞÜ0-9])");
        internal static bool KolonKatmani(string katman) => Iceriyor(katman, KolonKatman);
        /// <summary>Kontur ve köşegen parçası toplanmayacak mimari katman.</summary>
        internal static bool RetKatmani(string katman) => Iceriyor(katman, RetKatman);

        // Merdiven kipinde merdiven/korkuluk katmanları okunur; tefriş, ölçü, kapı/pencere, tarama yine dışarıda.
        private static readonly string[] MerdivenRetKatman =
        {
            "TEFR", "MOBİLYA", "MOBILYA", "FURN", "BİTKİ", "BITKI", "AĞAÇ", "AGAC", "ARBOL", "ÖLÇÜ", "OLCU", "DIM",
            "ANTET", "PENCERE", "KAPI", "DOĞRAMA", "DOGRAMA", "OTOPARK", "ARAÇ", "ARAC", "TARAMA", "HATCH", "KAPLAMA"
        };
        internal static bool MerdivenRetKatmani(string katman) => Iceriyor(katman, MerdivenRetKatman);

        private static bool Kolonluk(Kutu k) => k.W >= KolonMin && k.H >= KolonMin && k.W <= KolonMax && k.H <= KolonMax;

        /// <summary>Kolon dikdörtgenleri: kalıpta KOLON/PERDE katmanı; mimaride önce kolon katmanları, azsa tüm 4 köşeli konturlar.</summary>
        internal static List<Kutu> MimariKolonlar(Kaynak m)
        {
            var k = m.Konturlar.Where(c => c.Noktalar.Count <= 5 && KolonKatmani(c.Katman) && Kolonluk(c.K)).Select(c => c.K).ToList();
            if (k.Count >= 6) return k;
            return m.Konturlar.Where(c => c.Noktalar.Count <= 5 && Kolonluk(c.K)).Select(c => c.K).ToList();
        }

        /// <summary>Öteleme oylaması; en çok oy alan ölçek/öteleme, eşleşen çiftlerin ortalamasıyla inceltilir.</summary>
        internal static Hizalama Hizala(List<Kutu> kalipKolon, List<Kutu> mimariKolon)
        {
            var en = new Hizalama { KalipKolon = kalipKolon.Count, MimariKolon = mimariKolon.Count };
            var kalipKoval = new Dictionary<long, List<Kutu>>();
            foreach (var k in kalipKolon)
            {
                if (!Kolonluk(k)) continue;
                long key = BoyutAnahtar(k.W, k.H);
                if (!kalipKoval.TryGetValue(key, out var l)) kalipKoval[key] = l = new List<Kutu>();
                l.Add(k);
            }
            foreach (double s in new[] { 1.0, 0.1, 10.0, 0.01, 100.0 })
            {
                var oy = new Dictionary<long, int>();
                foreach (var m in mimariKolon)
                {
                    double w = m.W * s, h = m.H * s;
                    if (w < KolonMin || h < KolonMin || w > KolonMax || h > KolonMax) continue;
                    for (int dw = -1; dw <= 1; dw++)
                        for (int dh = -1; dh <= 1; dh++)
                        {
                            if (!kalipKoval.TryGetValue(BoyutAnahtar(w + dw, h + dh), out var l)) continue;
                            foreach (var k in l)
                            {
                                if (Math.Abs(k.W - w) > OlcuTol || Math.Abs(k.H - h) > OlcuTol) continue;
                                long t = OtelemeAnahtar(k.Cx - m.Cx * s, k.Cy - m.Cy * s);
                                oy.TryGetValue(t, out int v); oy[t] = v + 1;
                            }
                        }
                }
                if (oy.Count == 0) continue;
                var sirali = oy.OrderByDescending(kv => kv.Value).ToList();
                int max = sirali[0].Value;
                if (max <= en.Eslesen) continue;
                OtelemeCoz(sirali[0].Key, out double dx, out double dy);
                // Komşu kovalara bölünen oylar ve ince ayar: eşleşen çiftlerin ortalama farkı.
                var aday = new Hizalama { Olcek = s, Dx = dx, Dy = dy, KalipKolon = kalipKolon.Count, MimariKolon = mimariKolon.Count };
                Incelt(aday, kalipKolon, mimariKolon);
                if (aday.Eslesen <= en.Eslesen) continue;
                // Aynı kolon düzenine sahip diğer katlar (ayrı mimari dosyada tüm katlar bulunur): kullanıcı seçer.
                var adaylar = new List<Hizalama> { aday };
                foreach (var kv in sirali.Skip(1).TakeWhile(kv => kv.Value >= max * 0.6).Take(60))
                {
                    OtelemeCoz(kv.Key, out double x, out double y);
                    if (adaylar.Any(a => Math.Abs(a.Dx - x) < 100 && Math.Abs(a.Dy - y) < 100)) continue;
                    var b = new Hizalama { Olcek = s, Dx = x, Dy = y, KalipKolon = kalipKolon.Count, MimariKolon = mimariKolon.Count };
                    Incelt(b, kalipKolon, mimariKolon);
                    if (b.Eslesen >= 4 && b.Eslesen >= 0.6 * aday.Eslesen
                        && !adaylar.Any(a => Math.Abs(a.Dx - b.Dx) < 100 && Math.Abs(a.Dy - b.Dy) < 100))
                        adaylar.Add(b);
                }
                aday.Adaylar = adaylar.OrderByDescending(a => a.Eslesen).Take(12).ToList();
                aday.EsitAday = aday.Adaylar.Count - 1;
                en = aday;
            }
            return en;
        }

        private static void Incelt(Hizalama h, List<Kutu> kalipKolon, List<Kutu> mimariKolon)
        {
            const double Tol = 4;
            double sx = 0, sy = 0; int n = 0;
            var izgara = Izgara(kalipKolon, 200);
            var eslesen = new HashSet<KeyValuePair<double, double>>();
            foreach (var m in mimariKolon)
            {
                double cx = m.Cx * h.Olcek + h.Dx, cy = m.Cy * h.Olcek + h.Dy, w = m.W * h.Olcek, hh = m.H * h.Olcek;
                foreach (var k in Komsular(izgara, cx, cy, 200))
                {
                    if (Math.Abs(k.Cx - cx) > Tol || Math.Abs(k.Cy - cy) > Tol) continue;
                    if (Math.Abs(k.W - w) > OlcuTol || Math.Abs(k.H - hh) > OlcuTol) continue;
                    sx += k.Cx - cx; sy += k.Cy - cy; n++;
                    eslesen.Add(new KeyValuePair<double, double>(k.Cx, k.Cy));
                    break;
                }
            }
            // Mimaride üst üste çizilmiş kolonlar bir kalıp kolonunu iki kez saymasın.
            h.Eslesen = eslesen.Count;
            if (n > 0) { h.Dx += sx / n; h.Dy += sy / n; }
        }

        private static long BoyutAnahtar(double w, double h) => (long)Math.Round(w) * 100003L + (long)Math.Round(h);
        private static long OtelemeAnahtar(double dx, double dy) => (long)Math.Round(dx / 2) * 10000019L + (long)Math.Round(dy / 2);
        private static void OtelemeCoz(long key, out double dx, out double dy)
        {
            long a = (long)Math.Round(key / 10000019.0);
            long b = key - a * 10000019L;
            dx = a * 2; dy = b * 2;
        }

        private static Dictionary<long, List<Kutu>> Izgara(IEnumerable<Kutu> kutular, double g)
        {
            var d = new Dictionary<long, List<Kutu>>();
            foreach (var k in kutular)
            {
                long key = Hucre(k.Cx, k.Cy, g);
                if (!d.TryGetValue(key, out var l)) d[key] = l = new List<Kutu>();
                l.Add(k);
            }
            return d;
        }
        private static long Hucre(double x, double y, double g) => (long)Math.Floor(x / g) * 1000003L + (long)Math.Floor(y / g);
        private static IEnumerable<Kutu> Komsular(Dictionary<long, List<Kutu>> d, double x, double y, double g)
        {
            long gx = (long)Math.Floor(x / g), gy = (long)Math.Floor(y / g);
            for (long i = gx - 1; i <= gx + 1; i++)
                for (long j = gy - 1; j <= gy + 1; j++)
                    if (d.TryGetValue(i * 1000003L + j, out var l))
                        foreach (var k in l) yield return k;
        }

        /// <summary>Hizalanmış mimariden aday + kalıp karşılaştırması.</summary>
        /// <param name="kalipBosluk">Kalıptaki mevcut KALIP BOSLUK konturları.</param>
        /// <param name="kalipKolon">Kalıp kolon/perde kutuları (kolon üstü aday elemek ve kat zarfı için).</param>
        /// <param name="katSiniri">Kalıptaki KAT SINIRI çokgenleri; aday merkezi bunların içinde olmalı (yoksa kolon zarfı).</param>
        internal static List<Sonuc> Karsilastir(Kaynak mimari, Hizalama h, List<Kutu> kalipBosluk, List<Kutu> kalipKolon, List<List<double[]>> katSiniri)
        {
            var sonuc = new List<Sonuc>();
            // Kolon yoksa (kolonsuz mimari pencere) zarf sınırsız: pencere zaten okumayı sınırlar.
            bool kolonVar = kalipKolon.Count > 0;
            var zarf = kolonVar
                ? new Kutu(kalipKolon.Min(k => k.X0) - 150, kalipKolon.Min(k => k.Y0) - 150, kalipKolon.Max(k => k.X1) + 150, kalipKolon.Max(k => k.Y1) + 150)
                : new Kutu(-1e15, -1e15, 1e15, 1e15);

            // Mimari konturlar kalıp koordinatında.
            var konturlar = new List<Kontur>(mimari.Konturlar.Count);
            foreach (var c in mimari.Konturlar)
            {
                var p = c.Noktalar.Select(h.Uygula).ToList();
                var k = Kutu.Of(p);
                if (!zarf.Icerir(k.Cx, k.Cy)) continue;
                konturlar.Add(new Kontur { Katman = c.Katman, Noktalar = p, K = k });
            }
            var parcalar = mimari.Parcalar.Select(s => new Parca
            {
                X0 = h.Uygula(s.X0, false), Y0 = h.Uygula(s.Y0, true), X1 = h.Uygula(s.X1, false), Y1 = h.Uygula(s.Y1, true)
            }).ToList();
            var yazilar = mimari.Yazilar.Select(t => new Yazi { Metin = t.Metin, X = h.Uygula(t.X, false), Y = h.Uygula(t.Y, true) }).ToList();

            const double G = 100;
            var parcaIzgara = new Dictionary<long, List<Parca>>();
            foreach (var s in parcalar)
            {
                long key = Hucre((s.X0 + s.X1) / 2, (s.Y0 + s.Y1) / 2, G);
                if (!parcaIzgara.TryGetValue(key, out var l)) parcaIzgara[key] = l = new List<Parca>();
                l.Add(s);
            }
            var kolonIzgara = Izgara(kalipKolon, 400);
            var icZarf = kolonVar ? new Kutu(kalipKolon.Min(k => k.X0), kalipKolon.Min(k => k.Y0), kalipKolon.Max(k => k.X1), kalipKolon.Max(k => k.Y1)) : zarf;
            bool Icerde(double px, double py) => katSiniri.Count > 0 ? katSiniri.Any(s => CokgenIcinde(s, px, py)) : icZarf.Icerir(px, py);

            // 1) Aday konturlar.
            var adaylar = new List<Sonuc>();
            foreach (var c in konturlar)
            {
                var k = c.K;
                if (k.W < 15 || k.H < 15 || k.W > 1500 || k.H > 1500) continue;
                // Cephe dışındaki çarpı/kutu (korkuluk, yağmur borusu, peyzaj) aday değildir.
                if (!Icerde(k.Cx, k.Cy)) continue;
                double tol = Math.Max(5, 0.06 * Math.Min(k.W, k.H));
                bool x = XVar(k, parcaIzgara, G, tol);
                bool katman = Iceriyor(c.Katman, KatmanAnahtar);
                var yi = Enumerable.Range(0, yazilar.Count).Where(i => k.Icerir(yazilar[i].X, yazilar[i].Y)).ToList();
                var y = yi.Select(i => yazilar[i].Metin).ToList();
                bool yazi = y.Count > 0;
                bool kucuk = k.W <= 350 && k.H <= 350 && k.Alan <= 90000;
                bool kabul = (x && k.W <= 800 && k.H <= 800) || (katman && !KolonKatmani(c.Katman)) || (yazi && kucuk);
                if (!kabul) continue;
                // Kolon/perde üstündeki taralı/çarpılı kontur boşluk değildir.
                double kolonOrt = Komsular(kolonIzgara, k.Cx, k.Cy, 400).Sum(kk => kk.Kesisim(k));
                if (kolonOrt > 0.4 * k.Alan) continue;
                if (KolonKatmani(c.Katman) && !x && !yazi) continue;
                var neden = new List<string>();
                if (x) neden.Add("X");
                if (katman) neden.Add("katman");
                if (yazi) neden.Add("\"" + Kisalt(y[0]) + "\"");
                adaylar.Add(new Sonuc
                {
                    Tur = Tur.Yeni, K = k, Noktalar = c.Noktalar, Katman = c.Katman, Neden = string.Join(", ", neden),
                    YaziNo = yi, SadeceYazi = !x && !katman,
                    // Küçük çarpılı kutu (cephe dikmesi, dolap) veya zayıf yazı: onaysız çizilmez.
                    Zayif = !(katman || (x && (yazi || Math.Min(k.W, k.H) >= 30)) || y.Any(t => Iceriyor(t, GucluYazi) || HavaBacasi(t)))
                });
            }

            // 1b) Etiket merkezli sınır: şaft çoğu zaman kapalı kontur değil, duvar/sıva çizgileriyle çizilir ve yazı
            // daha büyük bir oda konturunun içinde kalır. Yazının etrafındaki en küçük kapalı alan çizgilerden kurulur.
            var duvar = mimari.Cizgiler.Where(c => !RetKatmani(c.Katman)).Select(s => new Parca
            {
                X0 = h.Uygula(s.X0, false), Y0 = h.Uygula(s.Y0, true), X1 = h.Uygula(s.X1, false), Y1 = h.Uygula(s.Y1, true), Katman = s.Katman
            }).ToList();
            var duvarIzgara = new Dictionary<long, List<Parca>>();
            foreach (var s in duvar)
                for (long gx = (long)Math.Floor(Math.Min(s.X0, s.X1) / 200); gx <= (long)Math.Floor(Math.Max(s.X0, s.X1) / 200); gx++)
                    for (long gy = (long)Math.Floor(Math.Min(s.Y0, s.Y1) / 200); gy <= (long)Math.Floor(Math.Max(s.Y0, s.Y1) / 200); gy++)
                    {
                        long key = gx * 1000003L + gy;
                        if (!duvarIzgara.TryGetValue(key, out var l)) duvarIzgara[key] = l = new List<Parca>();
                        l.Add(s);
                    }
            for (int i = 0; i < yazilar.Count; i++)
            {
                var t = yazilar[i];
                if (!Icerde(t.X, t.Y)) continue;
                // Asansör/galeri/boşluk kuyusu büyük olabilir; şaft ve baca 3 m²yi geçmez (geçerse yazının bulunduğu odadır).
                bool genis = Iceriyor(t.Metin, new[] { "ASANS", "LIFT", "GALER", "BOŞLUK", "BOSLUK", "VOID" });
                // Yazıyı içeren şaft ölçüsünde kapalı kontur zaten varsa o esastır; çizgi hücresi (kabin, bölme, tesisat)
                // onu küçültmesin. Sınır yalnız kontur yoksa ya da kontur oda büyüklüğündeyse kurulur.
                double sinir = genis ? 120000 : 30000;
                if (adaylar.Any(a => a.K.Icerir(t.X, t.Y) && a.K.Alan <= sinir)) continue;
                // "Şönt baca" gibi yazı yanındaki küçük baca kutusunu tarif eder (kutunun dışında durur): 60 cm içinde
                // küçük baca adayı varsa yazının bulunduğu dolap/oda hücresi şaft sayılmaz.
                bool bacaYazisi = Iceriyor(t.Metin, new[] { "BACA", "ŞÖNT", "SÖNT", "SONT" }) && !HavaBacasi(t.Metin);
                if (bacaYazisi && adaylar.Any(a => a.K.Icerir(t.X, t.Y, 60) && Math.Max(a.K.W, a.K.H) <= 45)) continue;
                var p = EtiketSiniri(t.X, t.Y, duvarIzgara, sinir);
                if (p == null) continue;
                var k = Kutu.Of(p);
                if (adaylar.Any(a => a.K.IoU(k) > 0.9)) continue;
                double kolonOrt = Komsular(kolonIzgara, k.Cx, k.Cy, 400).Sum(kk => kk.Kesisim(k));
                if (kolonOrt > 0.4 * k.Alan) continue;
                bool guclu = Iceriyor(t.Metin, GucluYazi) || HavaBacasi(t.Metin);
                var yi = Enumerable.Range(0, yazilar.Count).Where(j => k.Icerir(yazilar[j].X, yazilar[j].Y)).ToList();
                adaylar.Add(new Sonuc
                {
                    Tur = Tur.Yeni, K = k, Noktalar = p, Katman = "yazı sınırı", Neden = "\"" + Kisalt(t.Metin) + "\" sınırı",
                    YaziNo = yi, SadeceYazi = true, Zayif = !guclu
                });
            }

            // 2) Aynı yerdeki tekrarlar (duvar dış/iç çizgisi, kopya kontur): en içteki kalır.
            adaylar = adaylar.OrderBy(a => a.K.Alan).ToList();
            // Yazı en küçük kapsayan konturun kanıtıdır: aynı yazıyla gelen büyük oda/sıva konturu düşer.
            var tekil = new List<Sonuc>();
            var sahipli = new HashSet<int>();
            foreach (var a in adaylar)
            {
                // Baca: içteki baca kanalı (14x14) değil dış gövde (20x20) döşemeden geçer; küçük iç içe kutuda dıştaki kalır.
                int ic = tekil.FindIndex(b => b.K.IcindeMi(a.K, 2) && Math.Max(a.K.W, a.K.H) <= 40 && b.K.Alan < a.K.Alan);
                if (ic >= 0) { tekil[ic] = a; foreach (int i in a.YaziNo) sahipli.Add(i); continue; }
                bool tekrar = tekil.Any(b => b.K.IoU(a.K) > 0.85 || b.K.IcindeMi(a.K, 2));
                if (!tekrar && a.SadeceYazi && a.YaziNo.All(sahipli.Contains)) tekrar = true;
                foreach (int i in a.YaziNo) sahipli.Add(i);
                if (!tekrar) tekil.Add(a);
            }
            tekil = BacaGrupla(tekil);

            // 3) Kalıptaki boşluklarla eşleştir.
            var kullanilan = new HashSet<int>();
            foreach (var a in tekil)
            {
                int en = -1; double enIou = 0;
                for (int i = 0; i < kalipBosluk.Count; i++)
                {
                    double iou = kalipBosluk[i].IoU(a.K);
                    if (iou > enIou) { enIou = iou; en = i; }
                }
                if (en >= 0 && enIou >= 0.4)
                {
                    kullanilan.Add(en);
                    var kb = kalipBosluk[en];
                    a.KalipK = kb;
                    a.Tur = kb.KenarFarki(a.K) > 5 ? Tur.OlcuFarki : Tur.Mevcut;
                }
                else if (kalipBosluk.Any(kb => kb.IcindeMi(a.K, 3) || a.K.IcindeMi(kb, 3)))
                    a.Tur = Tur.Mevcut; // boşluk içinde/dışında kısmi çizim (ör. asansör kuyusu bölmesi)
                sonuc.Add(a);
            }

            // 4) Kalıpta olup mimaride konturu olmayan boşluklar (aday olmasa da herhangi bir mimari kontur yeterli).
            for (int i = 0; i < kalipBosluk.Count; i++)
            {
                if (kullanilan.Contains(i)) continue;
                var kb = kalipBosluk[i];
                double tol = Math.Max(6, 0.08 * Math.Min(kb.W, kb.H));
                bool var = konturlar.Any(c => c.K.KenarFarki(kb) <= tol)
                        || XVar(kb, parcaIzgara, G, tol);
                if (!var) sonuc.Add(new Sonuc { Tur = Tur.MimarideYok, K = kb, KalipK = kb });
            }
            return sonuc;
        }

        /// <summary>Kutunun iki köşegeni boyunca (uçlar köşelere tol içinde) çizgi parçası var mı.</summary>
        private static bool XVar(Kutu k, Dictionary<long, List<Parca>> izgara, double g, double tol)
        {
            bool a = false, b = false;
            long gx = (long)Math.Floor(k.Cx / g), gy = (long)Math.Floor(k.Cy / g);
            for (long i = gx - 1; i <= gx + 1; i++)
                for (long j = gy - 1; j <= gy + 1; j++)
                {
                    if (!izgara.TryGetValue(i * 1000003L + j, out var l)) continue;
                    foreach (var s in l)
                    {
                        if (UclarKosede(s, k.X0, k.Y0, k.X1, k.Y1, tol)) a = true;
                        else if (UclarKosede(s, k.X0, k.Y1, k.X1, k.Y0, tol)) b = true;
                        if (a && b) return true;
                    }
                }
            return false;
        }

        private static bool UclarKosede(Parca s, double ax, double ay, double bx, double by, double tol)
        {
            bool Yakin(double x, double y, double px, double py) => Math.Abs(x - px) <= tol && Math.Abs(y - py) <= tol;
            return (Yakin(s.X0, s.Y0, ax, ay) && Yakin(s.X1, s.Y1, bx, by)) || (Yakin(s.X0, s.Y0, bx, by) && Yakin(s.X1, s.Y1, ax, ay));
        }

        /// <summary>
        /// Noktayı çevreleyen en küçük kapalı alan (AutoCAD BOUNDARY benzeri): 350 cm çevredeki duvar/sıva/baca çizgileri
        /// 1.5 cm uzatılıp (küçük açıklıklar kapansın) düğümlenir ve poligonlaştırılır. Şaft ölçüsü dışı sonuç null.
        /// </summary>
        private static List<double[]> EtiketSiniri(double x, double y, Dictionary<long, List<Parca>> izgara, double enCokAlan)
        {
            const double R = 350, Uzat = 1.5;
            var secili = new HashSet<Parca>();
            for (long gx = (long)Math.Floor((x - R) / 200); gx <= (long)Math.Floor((x + R) / 200); gx++)
                for (long gy = (long)Math.Floor((y - R) / 200); gy <= (long)Math.Floor((y + R) / 200); gy++)
                    if (izgara.TryGetValue(gx * 1000003L + gy, out var l))
                        foreach (var s in l) secili.Add(s);
            if (secili.Count < 3 || secili.Count > 4000) return null;
            var gf = new NetTopologySuite.Geometries.GeometryFactory(new NetTopologySuite.Geometries.PrecisionModel(10));
            var cizgiler = new List<NetTopologySuite.Geometries.LineString>();
            foreach (var s in secili)
            {
                double dx = s.X1 - s.X0, dy = s.Y1 - s.Y0, L = Math.Sqrt(dx * dx + dy * dy);
                if (L < 0.5) continue;
                dx /= L; dy /= L;
                var a = new NetTopologySuite.Geometries.Coordinate(Math.Round((s.X0 - dx * Uzat) * 10) / 10, Math.Round((s.Y0 - dy * Uzat) * 10) / 10);
                var b = new NetTopologySuite.Geometries.Coordinate(Math.Round((s.X1 + dx * Uzat) * 10) / 10, Math.Round((s.Y1 + dy * Uzat) * 10) / 10);
                cizgiler.Add(gf.CreateLineString(new[] { a, b }));
            }
            NetTopologySuite.Geometries.Geometry dugum;
            try { dugum = gf.CreateMultiLineString(cizgiler.ToArray()).Union(); }
            catch (Exception ex) when (ex is NetTopologySuite.Geometries.TopologyException || ex is ArgumentException) { return null; }
            var pz = new NetTopologySuite.Operation.Polygonize.Polygonizer();
            pz.Add(dugum);
            var nokta = gf.CreatePoint(new NetTopologySuite.Geometries.Coordinate(x, y));
            NetTopologySuite.Geometries.Geometry en = null;
            foreach (var g in pz.GetPolygons())
                if (g.Area > 100 && g.Contains(nokta) && (en == null || g.Area < en.Area)) en = g;
            if (en == null) return null;
            var env = en.EnvelopeInternal;
            // Şaft/baca/asansör ölçüsü: kısa kenar ≥ 12, uzun kenar ≤ 600, alan sınırı; aksi oda/hol/koridordur.
            if (Math.Min(env.Width, env.Height) < 12 || Math.Max(env.Width, env.Height) > 600 || en.Area > enCokAlan) return null;
            // Dikdörtgene yakınsa temiz dikdörtgen; değilse çokgen (sadeleştirilmiş).
            if (en.Area >= 0.92 * env.Width * env.Height)
                return new List<double[]> { new[] { env.MinX, env.MinY }, new[] { env.MaxX, env.MinY }, new[] { env.MaxX, env.MaxY }, new[] { env.MinX, env.MaxY } };
            var sade = NetTopologySuite.Simplify.DouglasPeuckerSimplifier.Simplify(en, 0.5) as NetTopologySuite.Geometries.Polygon;
            var c = (sade ?? (NetTopologySuite.Geometries.Polygon)en).ExteriorRing.Coordinates;
            return c.Take(c.Length - 1).Select(q => new[] { q.X, q.Y }).ToList();
        }

        /// <summary>
        /// Yan yana küçük bacalar kalıpta tek dikdörtgen boşluk olur (kullanıcı projeleri: 3–4 baca → 80x20, 2 baca → 20x40):
        /// kısa kenarı ≤ 45 aday kutular arası açıklık ≤ 15 cm ve diğer eksende ≥ %70 örtüşme varsa birleşir.
        /// </summary>
        private static List<Sonuc> BacaGrupla(List<Sonuc> liste)
        {
            bool Kucuk(Sonuc s) => Math.Max(s.K.W, s.K.H) <= 45;
            int n = liste.Count;
            var ust = Enumerable.Range(0, n).ToArray();
            int Kok(int v) { while (ust[v] != v) v = ust[v] = ust[ust[v]]; return v; }
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (!Kucuk(liste[i]) || !Kucuk(liste[j])) continue;
                    Kutu a = liste[i].K, b = liste[j].K;
                    double bx = Math.Max(a.X0, b.X0) - Math.Min(a.X1, b.X1), by = Math.Max(a.Y0, b.Y0) - Math.Min(a.Y1, b.Y1);
                    double ox = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0), oy = Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0);
                    bool yatay = bx >= -0.5 && bx <= 15 && oy >= 0.7 * Math.Min(a.H, b.H);
                    bool dusey = by >= -0.5 && by <= 15 && ox >= 0.7 * Math.Min(a.W, b.W);
                    if (yatay || dusey) ust[Kok(i)] = Kok(j);
                }
            var sonuc = new List<Sonuc>();
            foreach (var g in Enumerable.Range(0, n).GroupBy(Kok))
            {
                var uye = g.Select(i => liste[i]).ToList();
                if (uye.Count == 1) { sonuc.Add(uye[0]); continue; }
                var k = new Kutu(uye.Min(s => s.K.X0), uye.Min(s => s.K.Y0), uye.Max(s => s.K.X1), uye.Max(s => s.K.Y1));
                sonuc.Add(new Sonuc
                {
                    Tur = Tur.Yeni, K = k, Katman = uye[0].Katman,
                    Noktalar = new List<double[]> { new[] { k.X0, k.Y0 }, new[] { k.X1, k.Y0 }, new[] { k.X1, k.Y1 }, new[] { k.X0, k.Y1 } },
                    Neden = uye.Count + " baca birleşik (" + uye[0].Neden + ")",
                    YaziNo = uye.SelectMany(s => s.YaziNo).Distinct().ToList(),
                    Zayif = uye.All(s => s.Zayif)
                });
            }
            return sonuc;
        }

        internal static bool CokgenIcinde(List<double[]> p, double x, double y)
        {
            bool ic = false;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
                if ((p[i][1] > y) != (p[j][1] > y) && x < (p[j][0] - p[i][0]) * (y - p[i][1]) / (p[j][1] - p[i][1]) + p[i][0])
                    ic = !ic;
            return ic;
        }

        private static string Kisalt(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 24 ? s.Substring(0, 24) + "…" : s;
        }
    }
}
