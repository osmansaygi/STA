using System;
using System.Collections.Generic;
using System.Linq;
using static ST4Yardimci.MimariBoslukAnaliz;

namespace ST4Yardimci
{
    /// <summary>
    /// MIMARIMERDIVEN hesap çekirdeği (AutoCAD'den bağımsız). Katman adına bakmaz:
    /// kol = aynı doğrultuda, aynı boyda, uçları hizalı ve eşit aralıklı (basamak 22–38 cm) en az 4 paralel çizgi.
    /// Çıkış yönü basamak numaralarından; kollar giriş/çıkış uçları en yakın olacak sırayla dizilir.
    /// Çıkış hattı kalıp standardındadır (MERDIVEN kesit komutunun girdisi): ilk rıhttan bir basamak önce başlar,
    /// dik kollarda eksen kesişiminden, ters kollarda sahanlık ortasından döner, son rıhttan bir basamak sonra biter.
    /// </summary>
    internal static class MimariMerdivenAnaliz
    {
        internal sealed class Kol
        {
            public double Ux, Uy, Nx, Ny;      // U: rıht doğrultusu, N: çıkış yönü (birim)
            public List<double> S;             // rıhtların N konumu, çıkış yönünde artan
            public List<double> Cift;          // rıhtın S+Cift'te ikinci çizgisi (basamak burnu çifti), yoksa 0
            public double A0, A1;              // rıhtların U aralığı (kol genişliği)
            public double Basamak;
            public string Katman;
            public int Yon;                    // +1/-1 numaralardan, 0 belirsiz
            public double OrtNo = double.NaN;
            public bool Mevcut;                // kalıpta rıhtları zaten var
            public double? BasSinir, SonSinir; // kalıp döşeme sınırına denk gelip çizilmeyen ilk/son rıhtın konumu
            public double W => A1 - A0;
            public double Am => (A0 + A1) / 2;
            public double[] P(double a, double s) => new[] { Ux * a + Nx * s, Uy * a + Ny * s };
            public void Cevir()
            {
                Nx = -Nx; Ny = -Ny;
                S = S.Select((v, i) => -(v + Cift[i])).Reverse().ToList();
                Cift = Enumerable.Reverse(Cift).ToList();
                double? bas = BasSinir;
                BasSinir = -SonSinir; SonSinir = -bas;
            }
            /// <summary>Yön kesinleşince çiftten çıkış yönünde ilerideki çizgi (asıl rıht; burun 3 cm aşağıda kalır) seçilir.</summary>
            public void CiftUygula()
            {
                S = S.Select((v, i) => v + Cift[i]).ToList();
                Cift = Cift.Select(_ => 0.0).ToList();
            }
            public Kutu K
            {
                get
                {
                    var p = new List<double[]> { P(A0, S[0]), P(A1, S[0]), P(A0, S[S.Count - 1]), P(A1, S[S.Count - 1]) };
                    return Kutu.Of(p);
                }
            }
        }

        internal sealed class Merdiven
        {
            public List<Kol> Kollar = new List<Kol>();
            public List<double[]> Yol = new List<double[]>();
            /// <summary>Mimari modu: kol uç (sahanlık/kat) çizgileri ve merdiven gözleri (kalıpta DOSEME SINIRI).</summary>
            public List<List<double[]>> Kenarlar = new List<List<double[]>>();
            public List<List<double[]>> Gozler = new List<List<double[]>>();
            public bool YonBelli, Guclu, Mevcut;
            public Kutu K;
            public string Neden = "";
            public int RihtSayisi => Kollar.Sum(k => k.S.Count);
        }

        private const double BasMin = 22, BasMax = 38, BoyMin = 60, BoyMax = 350;

        /// <summary>Mimari çizgilerden kalıp koordinatında merdivenler.</summary>
        /// <param name="kalipMerdiven">Kalıpta MERDIVEN (BEYKENT) çizgileri (mevcut rıht kontrolü).</param>
        /// <param name="kalipYapi">Kalıpta kiriş/perde/kolon/döşeme sınırı kenarları (rıht ucunu yüze uzatmak için).</param>
        internal static List<Merdiven> Bul(Kaynak mimari, Hizalama h, List<Parca> kalipMerdiven, List<Parca> kalipYapi,
            Func<double, double, bool> icerde, bool mimariModu = false, double ciftEnCok = 5)
        {
            var parcalar = new List<Parca>();
            var kenarlar = new List<Parca>(); // boy sınırı yok: döşeme kenarı uzun olabilir
            foreach (var c in mimari.Cizgiler)
            {
                var p = new Parca { X0 = h.Uygula(c.X0, false), Y0 = h.Uygula(c.Y0, true), X1 = h.Uygula(c.X1, false), Y1 = h.Uygula(c.Y1, true), Katman = c.Katman };
                double L = Math.Sqrt((p.X1 - p.X0) * (p.X1 - p.X0) + (p.Y1 - p.Y0) * (p.Y1 - p.Y0));
                if (L >= 15) kenarlar.Add(p);
                if (L >= 15 && L <= BoyMax) parcalar.Add(p); // kısa parçalar kesik rıht tamamlamada kullanılır; kol tohumu yine ≥ BoyMin
            }
            var sayilar = mimari.Sayilar.Select(t => new Yazi { Metin = t.Metin, X = h.Uygula(t.X, false), Y = h.Uygula(t.Y, true) }).ToList();

            var kollar = new List<Kol>();
            foreach (var grup in parcalar.GroupBy(AciKovasi))
                kollar.AddRange(KolBul(grup.ToList(), ciftEnCok));
            kollar = kollar.Where(k => icerde(k.K.Cx, k.K.Cy)).ToList();
            foreach (var k in kollar) Numarala(k, sayilar);
            foreach (var k in kollar) k.Mevcut = KalipteVar(k, kalipMerdiven);
            // Mimari modunda kalıp yüzü yok: rıht ucu mimarideki duvar/perde/kolon çizgisine uzatılır (korkuluk tarafı değişmez).
            var yuzler = mimariModu
                ? mimari.Cizgiler.Where(c => Iceriyor(c.Katman, DuvarKatman)).Select(c => new Parca
                {
                    X0 = h.Uygula(c.X0, false), Y0 = h.Uygula(c.Y0, true), X1 = h.Uygula(c.X1, false), Y1 = h.Uygula(c.Y1, true), Katman = c.Katman
                }).ToList()
                : kalipYapi;
            foreach (var k in kollar) UclariUzat(k, yuzler, mimariModu ? 6 : 12); // mimaride yalnız sıva payı

            var sonuc = new List<Merdiven>();
            foreach (var grup in Grupla(kollar).SelectMany(NumarasizAyir))
            {
                var m = Diz(grup);
                if (mimariModu) MimariSinir(m.Kollar, kenarlar);
                else foreach (var k in m.Kollar) SinirAyikla(k, kalipYapi);
                m.Yol = Yol(m.Kollar);
                if (mimariModu) SinirKur(m);
                m.K = Kutu.Of(m.Kollar.SelectMany(k => new[] { k.P(k.A0, k.S[0]), k.P(k.A1, k.S[k.S.Count - 1]) }).Concat(m.Yol).ToList());
                m.Mevcut = m.Kollar.All(k => k.Mevcut);
                bool katman = m.Kollar.Any(k => Ust(k.Katman).Contains("MERD") || Ust(k.Katman).Contains("STAIR") || Ust(k.Katman).Contains("BASAMAK"));
                m.Guclu = m.YonBelli || katman;
                var neden = new List<string> { m.Kollar.Count + " kol", m.RihtSayisi + " rıht", "basamak " + m.Kollar[0].Basamak.ToString("0.#") };
                neden.Add(m.YonBelli ? "yön numaralardan" : "yön belirsiz");
                if (katman) neden.Add("merdiven katmanı");
                m.Neden = string.Join(", ", neden);
                sonuc.Add(m);
            }
            return sonuc;
        }

        private static string Ust(string s) => (s ?? "").ToUpperInvariant().Replace('İ', 'I');
        private static readonly string[] DuvarKatman = { "DUVAR", "PERDE", "KOLON", "BETON", "WALL", "COLUMN" };
        private static bool Iceriyor(string s, string[] a) { string u = Ust(s); return a.Any(x => u.Contains(x)); }

        private static int AciKovasi(Parca p)
        {
            double a = Math.Atan2(p.Y1 - p.Y0, p.X1 - p.X0) * 180 / Math.PI;
            if (a < 0) a += 180;
            if (a >= 179.75) a -= 180;
            return (int)Math.Round(a * 2);
        }

        private sealed class Ara { public double S, A0, A1; public string Katman; }

        /// <summary>Tek doğrultudaki parçalardan kol dizileri.</summary>
        private static IEnumerable<Kol> KolBul(List<Parca> grup, double ciftEnCok = 5)
        {
            double ang = grup.Average(p => { double a = Math.Atan2(p.Y1 - p.Y0, p.X1 - p.X0); if (a < 0) a += Math.PI; if (a >= Math.PI - 0.005) a -= Math.PI; return a; });
            double ux = Math.Cos(ang), uy = Math.Sin(ang), nx = -uy, ny = ux;
            var aralar = grup.Select(p =>
            {
                double a0 = p.X0 * ux + p.Y0 * uy, a1 = p.X1 * ux + p.Y1 * uy;
                return new Ara { S = ((p.X0 + p.X1) * nx + (p.Y0 + p.Y1) * ny) / 2, A0 = Math.Min(a0, a1), A1 = Math.Max(a0, a1), Katman = p.Katman };
            }).OrderBy(r => r.S).ThenBy(r => r.A0).ToList();

            // Aynı hattaki parçalar (çıkış çizgisiyle bölünmüş rıht, kutu kenarı tekrarları) birleşir.
            var birlesik = new List<Ara>();
            foreach (var r in aralar)
            {
                Ara son = null;
                for (int x = birlesik.Count - 1; x >= 0 && birlesik[x].S > r.S - 0.6; x--)
                    if (r.A0 <= birlesik[x].A1 + 3 && r.A1 >= birlesik[x].A0 - 3) { son = birlesik[x]; break; }
                if (son != null) { son.A1 = Math.Max(son.A1, r.A1); son.A0 = Math.Min(son.A0, r.A0); }
                else birlesik.Add(new Ara { S = r.S, A0 = r.A0, A1 = r.A1, Katman = r.Katman });
            }

            // Uçları hizalı (±4) parçalar aynı kol adayıdır.
            var kullanildi = new bool[birlesik.Count];
            var siraA0 = Enumerable.Range(0, birlesik.Count).OrderBy(i => birlesik[i].A0).ToList();
            var a0Dizi = siraA0.Select(i => birlesik[i].A0).ToArray();
            foreach (int i in siraA0)
            {
                if (kullanildi[i]) continue;
                var b = birlesik[i];
                if (b.A1 - b.A0 < BoyMin) continue;
                int bas = Array.BinarySearch(a0Dizi, b.A0 - 4);
                if (bas < 0) bas = ~bas;
                var takim = new List<int>();
                for (int x = bas; x < a0Dizi.Length && a0Dizi[x] <= b.A0 + 4; x++)
                {
                    int j = siraA0[x];
                    if (!kullanildi[j] && Math.Abs(birlesik[j].A1 - b.A1) <= 4) takim.Add(j);
                }
                takim = takim.OrderBy(j => birlesik[j].S).ToList();
                // Rıht + basamak burnu (≤5 cm arayla iki çizgi) tek rıht sayılır; ikinci çizginin farkı Cift'te tutulur.
                var tekil = new List<int>();
                var cift = new List<double>();
                foreach (int j in takim)
                {
                    if (tekil.Count == 0) { tekil.Add(j); cift.Add(0); continue; }
                    double d = birlesik[j].S - birlesik[tekil[tekil.Count - 1]].S;
                    if (d < 1.0) continue;
                    if (d <= ciftEnCok && cift[cift.Count - 1] == 0) { cift[cift.Count - 1] = d; continue; }
                    tekil.Add(j); cift.Add(0);
                }
                foreach (var dizi in EsitAralikli(tekil.Select(j => birlesik[j].S).ToList()))
                {
                    var secilen = dizi.Select(x => tekil[x]).ToList();
                    foreach (int j in takim.Where(j => secilen.Any(s => birlesik[j].S - birlesik[s].S > -1.0 && birlesik[j].S - birlesik[s].S < Math.Max(6, ciftEnCok + 1)))) kullanildi[j] = true;
                    var ss = secilen.Select(j => birlesik[j].S).ToList();
                    var farklar = ss.Zip(ss.Skip(1), (p, q) => q - p).OrderBy(v => v).ToList();
                    var kol = new Kol
                    {
                        Ux = ux, Uy = uy, Nx = nx, Ny = ny, S = ss, Cift = dizi.Select(x => cift[x]).ToList(),
                        A0 = secilen.Average(j => birlesik[j].A0), A1 = secilen.Average(j => birlesik[j].A1),
                        Basamak = farklar[farklar.Count / 2],
                        Katman = birlesik[secilen[0]].Katman
                    };
                    KesikRihtEkle(kol, birlesik, kullanildi);
                    yield return kol;
                }
            }
        }

        /// <summary>
        /// Mimaride kesit (kırık) çizgisiyle kesilen ya da kısa çizilen rıhtlar: kolun iki ucundan basamak aralığıyla devam eden,
        /// kol genişliği içinde kalan ve toplam boyu genişliğin en az %35'i olan parça(lar) rıht sayılır; kalıba tam boy çizilir.
        /// </summary>
        private static void KesikRihtEkle(Kol k, List<Ara> parcalar, bool[] kullanildi)
        {
            double w = k.A1 - k.A0;
            double? Bul(double s)
            {
                double ort = 0, top = 0; var alinan = new List<int>();
                var araliklar = new List<(double a, double b)>();
                for (int j = 0; j < parcalar.Count; j++)
                {
                    var p = parcalar[j];
                    if (kullanildi[j] || Math.Abs(p.S - s) > 2.5) continue;
                    double a = Math.Max(p.A0, k.A0), b = Math.Min(p.A1, k.A1);
                    if (b - a < 5 || p.A0 < k.A0 - 5 || p.A1 > k.A1 + 5) continue;
                    araliklar.Add((a, b)); ort += p.S * (b - a); top += b - a; alinan.Add(j);
                }
                if (alinan.Count == 0) return null;
                // örtüşen parçaları bir kez say
                double kapsam = 0, son = double.MinValue;
                foreach (var (a, b) in araliklar.OrderBy(x => x.a)) { if (b <= son) continue; kapsam += b - Math.Max(a, son); son = b; }
                if (kapsam < 0.35 * w) return null;
                foreach (int j in alinan) kullanildi[j] = true;
                return ort / top;
            }
            for (int adim = 0; adim < 30; adim++)
            {
                var s = Bul(k.S[k.S.Count - 1] + k.Cift[k.Cift.Count - 1] + k.Basamak);
                if (s == null) s = Bul(k.S[k.S.Count - 1] + k.Basamak);
                if (s == null) break;
                k.S.Add(s.Value); k.Cift.Add(0);
            }
            for (int adim = 0; adim < 30; adim++)
            {
                var s = Bul(k.S[0] - k.Basamak);
                if (s == null) break;
                k.S.Insert(0, s.Value); k.Cift.Insert(0, 0);
            }
        }

        /// <summary>Artan konumlarda en az 4 elemanlı, aralığı sabit (±2) ve 22–38 cm olan ardışık diziler (indeksler).</summary>
        private static IEnumerable<List<int>> EsitAralikli(List<double> s)
        {
            int i = 0;
            while (i < s.Count - 1)
            {
                double d0 = s[i + 1] - s[i];
                if (d0 < BasMin || d0 > BasMax) { i++; continue; }
                int j = i + 1;
                while (j + 1 < s.Count && Math.Abs((s[j + 1] - s[j]) - d0) <= 2) j++;
                if (j - i + 1 >= 4) { yield return Enumerable.Range(i, j - i + 1).ToList(); i = j; }
                else i++;
            }
        }

        /// <summary>Kol içindeki basamak numaralarıyla çıkış yönü (numara artan yön).</summary>
        private static void Numarala(Kol k, List<Yazi> sayilar)
        {
            var noktalar = new List<(double s, double no)>();
            foreach (var t in sayilar)
            {
                double a = t.X * k.Ux + t.Y * k.Uy, s = t.X * k.Nx + t.Y * k.Ny;
                if (a < k.A0 - 30 || a > k.A1 + 30 || s < k.S[0] - k.Basamak || s > k.S[k.S.Count - 1] + k.Basamak) continue;
                noktalar.Add((s, double.Parse(t.Metin)));
            }
            if (noktalar.Select(p => p.no).Distinct().Count() < 2) return;
            double ms = noktalar.Average(p => p.s), mn = noktalar.Average(p => p.no);
            double kov = noktalar.Sum(p => (p.s - ms) * (p.no - mn));
            if (Math.Abs(kov) < 1e-6) return;
            k.Yon = kov > 0 ? 1 : -1;
            k.OrtNo = mn;
            if (k.Yon < 0) { k.Cevir(); k.Yon = 1; }
        }

        private static bool KalipteVar(Kol k, List<Parca> kalip)
        {
            // Yön henüz kesinleşmediğinden rıht çiftin iki çizgisinden biri olabilir (burun 3 cm): ikisi de denenir.
            int var = 0;
            for (int i = 0; i < k.S.Count; i++)
                if (kalip.Any(p =>
                {
                    double s0 = p.X0 * k.Nx + p.Y0 * k.Ny, s1 = p.X1 * k.Nx + p.Y1 * k.Ny;
                    bool yakin(double s) => Math.Abs(s0 - s) <= 3 && Math.Abs(s1 - s) <= 3;
                    if (!yakin(k.S[i]) && !yakin(k.S[i] + k.Cift[i])) return false;
                    double a0 = p.X0 * k.Ux + p.Y0 * k.Uy, a1 = p.X1 * k.Ux + p.Y1 * k.Uy;
                    return Math.Min(Math.Max(a0, a1), k.A1) - Math.Max(Math.Min(a0, a1), k.A0) > 0.5 * k.W;
                })) var++;
            return var >= 0.5 * k.S.Count;
        }

        /// <summary>Mimari rıht sıva yüzünde biter; kalıpta kiriş/perde yüzüne kadar (en çok 12 cm) dışa uzatılır.</summary>
        private static void UclariUzat(Kol k, List<Parca> yapi, double enCok)
        {
            double s0 = k.S[0], s1 = k.S[k.S.Count - 1];
            double? Yuz(double a, int yon)
            {
                double? en = null;
                foreach (var p in yapi)
                {
                    double pa0 = p.X0 * k.Ux + p.Y0 * k.Uy, pa1 = p.X1 * k.Ux + p.Y1 * k.Uy;
                    if (Math.Abs(pa0 - pa1) > 1) continue; // yalnız çıkış yönüne paralel kenar
                    double ps0 = p.X0 * k.Nx + p.Y0 * k.Ny, ps1 = p.X1 * k.Nx + p.Y1 * k.Ny;
                    if (Math.Min(Math.Max(ps0, ps1), s1) - Math.Max(Math.Min(ps0, ps1), s0) < 0.5 * (s1 - s0)) continue;
                    double d = (pa0 - a) * yon;
                    if (d < -0.5 || d > enCok) continue;
                    if (en == null || d < (en.Value - a) * yon) en = pa0;
                }
                return en;
            }
            var y0 = Yuz(k.A0, -1); if (y0.HasValue) k.A0 = y0.Value;
            var y1 = Yuz(k.A1, 1); if (y1.HasValue) k.A1 = y1.Value;
        }

        /// <summary>
        /// Kalıpta döşeme/kiriş kenarına (ör. DOSEME SINIRI) 3.5 cm içinde denk gelen rıht çizilmez: MERDIVEN kesit komutu
        /// o kenarı zaten döşeme sınırı olarak sayar. Kolun ilk/son rıhtıysa çıkış hattı o kenardan başlar/biter.
        /// </summary>
        private static void SinirAyikla(Kol k, List<Parca> yapi)
        {
            bool Sinirda(double s) => yapi.Any(p =>
            {
                double s0 = p.X0 * k.Nx + p.Y0 * k.Ny, s1 = p.X1 * k.Nx + p.Y1 * k.Ny;
                if (Math.Abs(s0 - s) > 3.5 || Math.Abs(s1 - s) > 3.5) return false;
                double a0 = p.X0 * k.Ux + p.Y0 * k.Uy, a1 = p.X1 * k.Ux + p.Y1 * k.Uy;
                return Math.Min(Math.Max(a0, a1), k.A1) - Math.Max(Math.Min(a0, a1), k.A0) > 0.5 * k.W;
            });
            var kalan = new List<double>();
            for (int i = 0; i < k.S.Count; i++)
            {
                if (!Sinirda(k.S[i])) { kalan.Add(k.S[i]); continue; }
                if (i == 0) k.BasSinir = k.S[i];
                else if (i == k.S.Count - 1) k.SonSinir = k.S[i];
            }
            if (kalan.Count < 2) { k.BasSinir = k.SonSinir = null; return; }
            k.S = kalan;
            k.Cift = kalan.Select(_ => 0.0).ToList();
        }

        /// <summary>
        /// Kalıp bilgisi yokken (mimari modu) kalıptaki döşeme sınırının yerini mimariden çıkarır; elle çizilen kalıpla aynı sonuç:
        /// (1) merdivenin ilk çizgisi kat kenarıdır: rıht çizilmez, çıkış hattı oradan başlar;
        /// (2) başka bir kolun yan kenarına (sahanlık birleşimi) ±8 cm denk gelen çizgi rıht değildir;
        /// (3) ters dönen kolda, önceki kolun son rıhtını sahanlık yönünde geçen ilk çizgi sahanlık kenarıdır.
        /// </summary>
        /// <summary>Kolun ucundan (yon: -1 baş, +1 son) yarım–bir buçuk basamak ötede, kol genişliğinin %60'ını örten paralel çizgi.</summary>
        private static double? Kenar(Kol k, List<Parca> cizgiler, int yon)
        {
            if (cizgiler == null || k.S.Count == 0) return null;
            double uc = yon < 0 ? k.S[0] : k.S[k.S.Count - 1];
            double? en = null;
            foreach (var c in cizgiler)
            {
                double dx = c.X1 - c.X0, dy = c.Y1 - c.Y0, L = Math.Sqrt(dx * dx + dy * dy);
                if (Math.Abs(dx * k.Nx + dy * k.Ny) > 0.02 * L) continue; // rıhta paralel değil
                if (L > 2.6 * k.W + 60 || Iceriyor(c.Katman, DuvarKatman)) continue; // duvar/kat kenarı değil, sahanlık kenarı
                double s = (c.X0 + c.X1) / 2 * k.Nx + (c.Y0 + c.Y1) / 2 * k.Ny, d = (s - uc) * yon;
                if (d < 0.5 * k.Basamak || d > 1.5 * k.Basamak) continue;
                double u0 = c.X0 * k.Ux + c.Y0 * k.Uy, u1 = c.X1 * k.Ux + c.Y1 * k.Uy;
                double ortu = Math.Min(Math.Max(u0, u1), k.A1) - Math.Max(Math.Min(u0, u1), k.A0);
                if (ortu < 0.6 * k.W) continue;
                if (en == null || Math.Abs(s - uc) < Math.Abs(en.Value - uc)) en = s;
            }
            return en;
        }

        internal static void MimariSinir(List<Kol> kollar, List<Parca> cizgiler = null)
        {
            for (int i = 0; i < kollar.Count; i++)
            {
                var k = kollar[i];
                var sil = new HashSet<int>();
                // Her kolun iki ucu sahanlık/kat kenarıdır (kullanıcı kalıplarında DOSEME SINIRI). Uçtan bir basamak ötede
                // paralel mimari çizgi varsa (kola katılmamış uzun kenar) sınır odur ve uç çizgi rıhttır; yoksa uç çizgi kenardır.
                double? basKenar = Kenar(k, cizgiler, -1);
                double? sonKenar = Kenar(k, cizgiler, +1);
                if (basKenar == null) sil.Add(0);
                if (sonKenar == null) sil.Add(k.S.Count - 1);
                for (int r = 0; r < k.S.Count; r++)
                {
                    double[] p = k.P(k.Am, k.S[r]);
                    foreach (var o in kollar)
                    {
                        if (o == k || Math.Abs(o.Nx * k.Nx + o.Ny * k.Ny) > 0.3) continue;
                        double u = p[0] * o.Ux + p[1] * o.Uy, s = p[0] * o.Nx + p[1] * o.Ny;
                        bool kenarda = Math.Abs(u - o.A0) <= 8 || Math.Abs(u - o.A1) <= 8; // mimari sıva yüzü kalıp yüzünden ~2.5 cm içeride
                        bool yaninda = s >= o.S[0] - k.W && s <= o.S[o.S.Count - 1] + k.W;
                        if (kenarda && yaninda) sil.Add(r);
                    }
                }
                if (i > 0)
                {
                    var a = kollar[i - 1];
                    if (a.Nx * k.Nx + a.Ny * k.Ny < -0.7)
                    {
                        double[] p = k.P(k.Am, k.S[0]);
                        if (p[0] * a.Nx + p[1] * a.Ny > a.S[a.S.Count - 1] + 1) sil.Add(0);
                    }
                }
                if (basKenar != null) k.BasSinir = basKenar;
                if (sonKenar != null) k.SonSinir = sonKenar;
                if (k.S.Count - sil.Count < 2) continue;
                if (sil.Contains(0)) k.BasSinir = k.S[0];
                if (sil.Contains(k.S.Count - 1)) k.SonSinir = k.S[k.S.Count - 1];
                k.S = k.S.Where((_, r) => !sil.Contains(r)).ToList();
                k.Cift = k.S.Select(_ => 0.0).ToList();
            }
        }

        /// <summary>
        /// Numaralı merdivenin yakınındaki numarasız kol (duvarın öbür yanındaki giriş/dış merdiveni) ancak ucu numaralı bir
        /// kolun ucuna 0.6 kol genişliğinden yakınsa aynı merdivendir; değilse ayrı merdiven olur.
        /// </summary>
        private static IEnumerable<List<Kol>> NumarasizAyir(List<Kol> grup)
        {
            var nolu = grup.Where(k => k.Yon != 0).ToList();
            if (nolu.Count == 0 || nolu.Count == grup.Count) { yield return grup; yield break; }
            double[][] Uclar(Kol k) => new[] { k.P(k.Am, k.S[0] - k.Basamak), k.P(k.Am, k.S[k.S.Count - 1] + k.Basamak), k.P(k.Am, k.S[0]), k.P(k.Am, k.S[k.S.Count - 1]) };
            var ana = new List<Kol>(nolu);
            var ayri = new List<Kol>();
            foreach (var k in grup.Where(k => k.Yon == 0))
            {
                double en = nolu.SelectMany(Uclar).Min(p => Uclar(k).Min(q => Mesafe(p, q)));
                if (en <= 0.6 * Math.Max(k.W, nolu.Max(x => x.W))) ana.Add(k); else ayri.Add(k);
            }
            yield return ana;
            foreach (var k in ayri) yield return new List<Kol> { k };
        }

        /// <summary>Birbirine kol genişliği kadar yakın kollar aynı merdivendir.</summary>
        private static List<List<Kol>> Grupla(List<Kol> kollar)
        {
            int n = kollar.Count;
            var ust = Enumerable.Range(0, n).ToArray();
            int Kok(int x) { while (ust[x] != x) x = ust[x] = ust[ust[x]]; return x; }
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    double pay = Math.Max(kollar[i].W, kollar[j].W) * 1.2 + 50;
                    Kutu a = kollar[i].K, b = kollar[j].K;
                    var gen = new Kutu(a.X0 - pay, a.Y0 - pay, a.X1 + pay, a.Y1 + pay);
                    if (gen.Kesisim(b) > 0) ust[Kok(i)] = Kok(j);
                }
            return Enumerable.Range(0, n).GroupBy(Kok).Select(g => g.Select(i => kollar[i]).ToList()).ToList();
        }

        private static double[] Giris(Kol k) => k.P(k.Am, k.BasSinir ?? k.S[0] - k.Basamak);
        private static double[] Cikis(Kol k) => k.P(k.Am, k.SonSinir ?? k.S[k.S.Count - 1] + k.Basamak);
        private static double Mesafe(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]));

        /// <summary>Kolları çıkış sırasına dizer; numarasız kolun yönü en kısa bağlantıya göre seçilir.</summary>
        private static Merdiven Diz(List<Kol> kollar)
        {
            var m = new Merdiven();
            if (kollar.Count > 5)
            {
                // Çok kollu (veya yan yana birden fazla merdiven): numara ya da konum sırası, yön korunur.
                m.Kollar = kollar.OrderBy(k => double.IsNaN(k.OrtNo) ? double.MaxValue : k.OrtNo).ToList();
                m.YonBelli = kollar.All(k => k.Yon != 0);
                foreach (var k in m.Kollar) k.CiftUygula();
                return m;
            }
            double enIyi = double.MaxValue; List<Kol> enSira = null; int[] enYon = null;
            foreach (var sira in Permutasyon(kollar))
                for (int mask = 0; mask < (1 << sira.Count); mask++)
                {
                    bool gecersiz = false;
                    var yon = new int[sira.Count];
                    for (int i = 0; i < sira.Count; i++)
                    {
                        yon[i] = (mask >> i & 1) == 1 ? -1 : 1;
                        if (sira[i].Yon != 0 && yon[i] < 0) gecersiz = true;
                    }
                    if (gecersiz) continue;
                    double maliyet = 0;
                    for (int i = 0; i + 1 < sira.Count; i++)
                        maliyet += Mesafe(Uc(sira[i], yon[i], true), Uc(sira[i + 1], yon[i + 1], false));
                    // Numaralı kollar artan ortalama numara sırasında olmalı.
                    var nolu = sira.Where(k => !double.IsNaN(k.OrtNo)).Select(k => k.OrtNo).ToList();
                    for (int i = 0; i + 1 < nolu.Count; i++) if (nolu[i + 1] < nolu[i]) maliyet += 1e6;
                    if (maliyet < enIyi - 1e-6) { enIyi = maliyet; enSira = sira; enYon = yon; }
                }
            for (int i = 0; i < enSira.Count; i++) if (enYon[i] < 0) enSira[i].Cevir();
            foreach (var k in enSira) k.CiftUygula();
            m.Kollar = enSira;
            m.YonBelli = enSira.Any(k => k.Yon != 0);
            return m;
        }

        private static double[] Uc(Kol k, int yon, bool cikis)
        {
            bool son = cikis == (yon > 0);
            double s = son ? k.S[k.S.Count - 1] + k.Basamak : k.S[0] - k.Basamak;
            return k.P(k.Am, s);
        }

        private static IEnumerable<List<Kol>> Permutasyon(List<Kol> l)
        {
            if (l.Count <= 1) { yield return new List<Kol>(l); yield break; }
            for (int i = 0; i < l.Count; i++)
            {
                var kalan = l.Where((_, j) => j != i).ToList();
                foreach (var p in Permutasyon(kalan)) { p.Insert(0, l[i]); yield return p; }
            }
        }

        /// <summary>
        /// Kullanıcı kalıplarındaki DOSEME SINIRI alışkanlığı: merdivenin tam çevresi çizilmez (duvar/kiriş boyu zaten kiriştir);
        /// yalnız kolun sahanlığa/kata bağlandığı uç çizgileri (kol genişliğince) ve ters dönen iki kol arasındaki göz çizilir.
        /// </summary>
        internal static void SinirKur(Merdiven m)
        {
            m.Kenarlar.Clear();
            m.Gozler.Clear();
            int n = m.Kollar.Count;
            for (int i = 0; i < n; i++)
            {
                var k = m.Kollar[i];
                double s0 = k.BasSinir ?? k.S[0] - k.Basamak, s1 = k.SonSinir ?? k.S[k.S.Count - 1] + k.Basamak;
                m.Kenarlar.Add(new List<double[]> { k.P(k.A0, s0), k.P(k.A1, s0) });
                m.Kenarlar.Add(new List<double[]> { k.P(k.A0, s1), k.P(k.A1, s1) });
            }
            // Göz: ters yönlü (U) kol çiftinin arasındaki 3–400 cm açıklık (dar göz ya da merdiven boşluğu), kolların ortak boyu boyunca.
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    Kol a = m.Kollar[i], b = m.Kollar[j];
                    if (a.Nx * b.Nx + a.Ny * b.Ny > -0.9) continue;
                    double As0 = a.BasSinir ?? a.S[0] - a.Basamak, As1 = a.SonSinir ?? a.S[a.S.Count - 1] + a.Basamak;
                    double Bs0 = b.BasSinir ?? b.S[0] - b.Basamak, Bs1 = b.SonSinir ?? b.S[b.S.Count - 1] + b.Basamak;
                    var bk = new[] { b.P(b.A0, Bs0), b.P(b.A1, Bs0), b.P(b.A0, Bs1), b.P(b.A1, Bs1) };
                    double bu0 = bk.Min(p => p[0] * a.Ux + p[1] * a.Uy), bu1 = bk.Max(p => p[0] * a.Ux + p[1] * a.Uy);
                    double bs0 = bk.Min(p => p[0] * a.Nx + p[1] * a.Ny), bs1 = bk.Max(p => p[0] * a.Nx + p[1] * a.Ny);
                    double g0, g1;
                    if (bu0 >= a.A1) { g0 = a.A1; g1 = bu0; }
                    else if (bu1 <= a.A0) { g0 = bu1; g1 = a.A0; }
                    else continue;
                    double t0 = Math.Max(Math.Min(As0, As1), bs0), t1 = Math.Min(Math.Max(As0, As1), bs1);
                    if (g1 - g0 < 3 || g1 - g0 > 400 || t1 - t0 < 50) continue;
                    m.Gozler.Add(new List<double[]> { a.P(g0, t0), a.P(g1, t0), a.P(g1, t1), a.P(g0, t1) });
                }
        }

        /// <summary>Çıkış hattı köşeleri (kalıp koordinatı).</summary>
        internal static List<double[]> Yol(List<Kol> kollar)
        {
            var y = new List<double[]> { Giris(kollar[0]) };
            for (int i = 0; i + 1 < kollar.Count; i++)
            {
                Kol a = kollar[i], b = kollar[i + 1];
                double dot = a.Nx * b.Nx + a.Ny * b.Ny;
                if (Math.Abs(dot) < 0.3)
                {
                    var p = Kesisim(a, b);
                    if (p != null) y.Add(p);
                }
                else if (dot < -0.7)
                {
                    // Ters kol: sahanlık ortası = iki kolun ileri ucundan yarım kol genişliği öte.
                    double ea = a.SonSinir ?? a.S[a.S.Count - 1];
                    double sb = Proj(b.P(b.Am, b.S[0]), a.Nx, a.Ny);
                    double land = Math.Max(ea, sb) + Math.Max(a.W, b.W) / 2;
                    y.Add(a.P(a.Am, land));
                    double bAm = Proj(b.P(b.Am, b.S[0]), a.Ux, a.Uy);
                    y.Add(new[] { a.Ux * bAm + a.Nx * land, a.Uy * bAm + a.Ny * land });
                }
                else
                {
                    // Aynı yönde, eksen kaymışsa sahanlıkta iki köşe.
                    double kay = Proj(b.P(b.Am, 0), a.Ux, a.Uy) - a.Am;
                    if (Math.Abs(kay) > 5)
                    {
                        double orta = (a.S[a.S.Count - 1] + Proj(b.P(b.Am, b.S[0]), a.Nx, a.Ny)) / 2;
                        y.Add(a.P(a.Am, orta));
                        y.Add(a.P(a.Am + kay, orta));
                    }
                }
            }
            y.Add(Cikis(kollar[kollar.Count - 1]));
            return y;
        }

        private static double Proj(double[] p, double x, double yy) => p[0] * x + p[1] * yy;

        private static double[] Kesisim(Kol a, Kol b)
        {
            double[] p = a.P(a.Am, 0), q = b.P(b.Am, 0);
            double det = a.Nx * (-b.Ny) - a.Ny * (-b.Nx);
            if (Math.Abs(det) < 1e-9) return null;
            double t = ((q[0] - p[0]) * (-b.Ny) - (q[1] - p[1]) * (-b.Nx)) / det;
            return new[] { p[0] + a.Nx * t, p[1] + a.Ny * t };
        }
    }
}
