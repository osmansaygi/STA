using System;
using System.Collections.Generic;
using System.Linq;

namespace ST4Yardimci
{
    /// <summary>
    /// MIMARISTA hesap çekirdeği (AutoCAD'den bağımsız): katman sınıfı önerisi, çizim birimi tahmini,
    /// aks parçalarını balondan balona tek çizgiye toplama ve çizgi parçalarından kapalı kolon çevrimi bulma.
    /// Uzunluk eşikleri cm verilir; <c>b</c> = 1 cm'nin çizim birimi karşılığı (cm çizimde 1, mm 10, m 0.01).
    /// </summary>
    internal static class MimariStaAnaliz
    {
        internal enum Sinif { Diger, Aks, Kolon, Duvar, Merdiven, Olcu, Sil, Kapi, Pencere }

        private static readonly string[] AksAd = { "AKS", "AXIS", "AXES", "GRID" };
        private static readonly string[] MerdivenAd = { "MERD", "STAIR", "LADDER", "BASAMAK" };
        private static readonly string[] KolonAd = { "KOLON", "COLUMN", "PERDE", "BETONARME" };
        private static readonly string[] KolonRet = { "KIRIS", "BEAM", "DOSEME", "SLAB" };
        private static readonly string[] DuvarAd = { "DUVAR", "WALL", "TUGLA", "GAZBETON", "BIMS", "BRIKET", "YTONG" };
        private static readonly string[] OlcuAd = { "OLCU", "DIM" };
        // Mahal listesi, kaplama, asma tavan ve yazı katmanları adında DUVAR/MERDIVEN geçse de yapı elemanı değildir.
        private static readonly string[] YapiRet = { "MAHAL", "KAPLAMA", "ASMA", "YAZI", "SIVA" };
        // Kapı/pencere korunur: STA duvar boşluklarını ve sonra pencereleri bunlarla işler (STA örneğinde "kapi" katmanı).
        private static readonly string[] KapiAd = { "KAPI", "DOOR" };
        private static readonly string[] PencereAd = { "PENCERE", "DOGRAMA", "WINDOW" };
        private static readonly string[] SilAd =
        {
            "TEFR", "MOBILYA", "FURN", "PEYZAJ", "BITKI", "AGAC", "ARAC", "ARABA", "KAPLAMA", "ZEMIN KAP", "SERAMIK",
            "KORKULUK", "RAILING", "HANDRAIL", "KUPESTE"
        };

        /// <summary>Türkçe harfleri sadeleştirip büyütür (katman adı karşılaştırması).</summary>
        internal static string Sade(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var c = s.ToCharArray();
            for (int i = 0; i < c.Length; i++)
                switch (c[i])
                {
                    case 'ı': case 'İ': case 'i': c[i] = 'I'; break;
                    case 'ş': case 'Ş': c[i] = 'S'; break;
                    case 'ğ': case 'Ğ': c[i] = 'G'; break;
                    case 'ü': case 'Ü': c[i] = 'U'; break;
                    case 'ö': case 'Ö': c[i] = 'O'; break;
                    case 'ç': case 'Ç': c[i] = 'C'; break;
                    default: c[i] = char.ToUpperInvariant(c[i]); break;
                }
            return new string(c);
        }

        private static bool Var(string u, string[] a) => a.Any(u.Contains);

        /// <summary>Katman adından STA4 sınıfı önerisi; kullanıcı formda değiştirir.</summary>
        internal static Sinif Oner(string katman)
        {
            string u = Sade(katman);
            if (Var(u, AksAd)) return Sinif.Aks;
            if (Var(u, OlcuAd)) return Sinif.Olcu;
            bool yapi = !Var(u, YapiRet);
            if (yapi && Var(u, MerdivenAd)) return Sinif.Merdiven;
            if (yapi && Var(u, KolonAd) && !Var(u, KolonRet)) return Sinif.Kolon;
            if (yapi && Var(u, DuvarAd)) return Sinif.Duvar;
            if (Var(u, PencereAd)) return Sinif.Pencere;
            if (Var(u, KapiAd) && !Var(u, new[] { "YAZI" })) return Sinif.Kapi;
            if (Var(u, SilAd)) return Sinif.Sil;
            return Sinif.Diger;
        }

        /// <summary>
        /// Çizim birimi: kolon kısa kenarlarının ortancası (cm 15–150, mm 150–1500, m 0.15–1.5); kolon yoksa plan genişliği.
        /// Dönen değer b (1 cm = b çizim birimi).
        /// </summary>
        internal static double BirimTahmin(List<double> kolonKisaKenar, double planGenislik)
        {
            if (kolonKisaKenar != null && kolonKisaKenar.Count >= 3)
            {
                var s = kolonKisaKenar.OrderBy(v => v).ToList();
                double m = s[s.Count / 2];
                if (m >= 150 && m <= 1500) return 10;
                if (m >= 15 && m < 150) return 1;
                if (m >= 0.15 && m <= 1.5) return 0.01;
            }
            if (planGenislik > 0)
            {
                if (planGenislik < 300) return 0.01;
                if (planGenislik > 30000) return 10;
            }
            return 1;
        }

        internal static string BirimAdi(double b) => b >= 5 ? "mm" : b <= 0.05 ? "m" : "cm";

        // ───────────── Aks ─────────────

        internal sealed class AksParca
        {
            public double X0, Y0, X1, Y1;
            public object Kaynak;
            public double Boy => Math.Sqrt((X1 - X0) * (X1 - X0) + (Y1 - Y0) * (Y1 - Y0));
        }

        internal sealed class Balon
        {
            public double X, Y, R;
            public string Ad = "";
            public object Kaynak, YaziKaynak;
            public bool Kullanildi;
        }

        internal sealed class Aks
        {
            public double X0, Y0, X1, Y1;          // balon kenarından balon kenarına
            public double Ux, Uy;                  // doğrultu (X0→X1)
            public readonly List<AksParca> Parcalar = new List<AksParca>();
            public Balon Bas, Son;
            public bool BasYeni, SonYeni;          // bu uçta balon yoktu, eklenecek
            public string Ad = "";
            public bool AdUretildi;
            public bool Birlesti;                  // birden çok parçadan toplandı
            public bool Uzadi;                     // uç balona uzatıldı/kırpıldı
            public bool Eklendi;                   // kolondan geçmeyen aks tamamlaması (çizimde yoktu)
            public double Boy => Math.Sqrt((X1 - X0) * (X1 - X0) + (Y1 - Y0) * (Y1 - Y0));
            public bool Dusey => Math.Abs(Uy) > Math.Abs(Ux);
        }

        private sealed class Kosu
        {
            public double T0, T1;
            public readonly List<AksParca> P = new List<AksParca>();
            public Balon Bas, Son;
        }

        private sealed class Grup
        {
            public double Ux, Uy, D;               // doğrultu ve doğrunun orijine dik uzaklığı
            public readonly List<AksParca> P = new List<AksParca>();
        }

        private const double AksEnAz = 100;        // bundan kısa çizgi aks parçası sayılmaz (balon çentiği vb.)
        private const double AksTamBoy = 400;      // bundan kısa tek başına parça "uç parçası"dır; eksik balon eklenmez
        private const double KosuBosluk = 60;      // aynı doğru üzerindeki parçalar bu boşluğa kadar tek koşudur
        private const double BalonErim = 300;      // koşu ucundan bu kadar ötedeki balon o uca aittir
        private const double KarsiUcEnCok = 20000; // iki uç parçası arası en çok açıklık (200 m)

        /// <summary>
        /// Aks parçalarını aynı doğru üzerinde toplar; uçlarındaki balonları bulur, karşılıklı uç parçalarını
        /// (balon–parça … parça–balon) tek aksa birleştirir, çizgiyi balon kenarına uzatır/kırpar, eksik balonu işaretler.
        /// </summary>
        internal static List<Aks> AksKur(List<AksParca> parcalar, List<Balon> balonlar, double b)
        {
            var gruplar = new List<Grup>();
            double aciTol = 0.25 * Math.PI / 180, dTol = 1.5 * b;
            foreach (var p in parcalar.Where(q => q.Boy >= AksEnAz * b).OrderByDescending(q => q.Boy))
            {
                double ux = (p.X1 - p.X0) / p.Boy, uy = (p.Y1 - p.Y0) / p.Boy;
                if (ux < -1e-9 || (Math.Abs(ux) <= 1e-9 && uy < 0)) { ux = -ux; uy = -uy; }
                Grup g = null;
                foreach (var a in gruplar)
                {
                    double capraz = Math.Abs(a.Ux * uy - a.Uy * ux);
                    if (capraz > Math.Sin(aciTol)) continue;
                    double mx = (p.X0 + p.X1) / 2, my = (p.Y0 + p.Y1) / 2;
                    if (Math.Abs(-mx * a.Uy + my * a.Ux - a.D) > dTol) continue;
                    g = a; break;
                }
                if (g == null)
                {
                    g = new Grup { Ux = ux, Uy = uy, D = -p.X0 * uy + p.Y0 * ux };
                    gruplar.Add(g);
                }
                g.P.Add(p);
            }

            var sonuc = new List<Aks>();
            foreach (var g in gruplar)
            {
                Func<double, double, double> T = (x, y) => x * g.Ux + y * g.Uy;
                var kosular = new List<Kosu>();
                foreach (var p in g.P.OrderBy(q => Math.Min(T(q.X0, q.Y0), T(q.X1, q.Y1))))
                {
                    double t0 = Math.Min(T(p.X0, p.Y0), T(p.X1, p.Y1)), t1 = Math.Max(T(p.X0, p.Y0), T(p.X1, p.Y1));
                    var son = kosular.Count > 0 ? kosular[kosular.Count - 1] : null;
                    if (son != null && t0 <= son.T1 + KosuBosluk * b) { son.T1 = Math.Max(son.T1, t1); son.P.Add(p); }
                    else { var k = new Kosu { T0 = t0, T1 = t1 }; k.P.Add(p); kosular.Add(k); }
                }

                // Doğru üzerindeki balonlar: merkez doğruya yarıçapın üçte biri kadar yakın.
                var hat = balonlar.Where(q => Math.Abs(-q.X * g.Uy + q.Y * g.Ux - g.D) <= Math.Max(0.35 * q.R, 2 * b))
                    .Select(q => new { B = q, T = T(q.X, q.Y) }).OrderBy(q => q.T).ToList();
                foreach (var h in hat)
                {
                    Kosu en = null; bool bas = false; double enMesafe = double.MaxValue;
                    foreach (var k in kosular)
                    {
                        // Baş ucu: balon koşunun başından geride (erim içinde) ya da çizgi balonun içine kadar girmiş.
                        double mb = k.T0 - h.T, ms = h.T - k.T1;
                        if (mb >= -h.B.R * 1.2 && mb <= h.B.R + BalonErim * b && Math.Abs(mb) < enMesafe) { en = k; bas = true; enMesafe = Math.Abs(mb); }
                        if (ms >= -h.B.R * 1.2 && ms <= h.B.R + BalonErim * b && Math.Abs(ms) < enMesafe) { en = k; bas = false; enMesafe = Math.Abs(ms); }
                    }
                    if (en == null) continue;
                    if (bas) { if (en.Bas == null || Math.Abs(T(en.Bas.X, en.Bas.Y) - en.T0) > enMesafe) en.Bas = h.B; }
                    else { if (en.Son == null || Math.Abs(T(en.Son.X, en.Son.Y) - en.T1) > enMesafe) en.Son = h.B; }
                }

                // Karşılıklı uç parçaları: [balon–parça] … [parça–balon], adlar aynı ya da biri boş.
                for (int i = 0; i + 1 < kosular.Count; i++)
                {
                    Kosu a = kosular[i], c = kosular[i + 1];
                    if (a.Bas == null || a.Son != null || c.Bas != null || c.Son == null) continue;
                    if (c.T0 - a.T1 > KarsiUcEnCok * b) continue;
                    if (a.Bas.Ad != "" && c.Son.Ad != "" && !string.Equals(a.Bas.Ad, c.Son.Ad, StringComparison.OrdinalIgnoreCase)) continue;
                    a.T1 = c.T1; a.Son = c.Son; a.P.AddRange(c.P);
                    kosular.RemoveAt(i + 1);
                }

                foreach (var k in kosular)
                {
                    bool tam = k.T1 - k.T0 >= AksTamBoy * b;
                    if (k.Bas == null && k.Son == null && !tam) continue;      // sahipsiz kısa parça: dokunma
                    var aks = new Aks { Ux = g.Ux, Uy = g.Uy, Bas = k.Bas, Son = k.Son };
                    aks.Parcalar.AddRange(k.P);
                    double t0 = k.T0, t1 = k.T1;
                    if (k.Bas != null) t0 = T(k.Bas.X, k.Bas.Y) + k.Bas.R;
                    if (k.Son != null) t1 = T(k.Son.X, k.Son.Y) - k.Son.R;
                    if (t1 - t0 < AksEnAz * b) continue;
                    aks.Uzadi = Math.Abs(t0 - k.T0) > 0.5 * b || Math.Abs(t1 - k.T1) > 0.5 * b;
                    aks.Birlesti = k.P.Count > 1;
                    aks.BasYeni = k.Bas == null && tam;
                    aks.SonYeni = k.Son == null && tam;
                    aks.Ad = k.Bas != null && k.Bas.Ad != "" ? k.Bas.Ad : k.Son != null ? k.Son.Ad : "";
                    // Doğrunun taban noktası: orijinin doğruya izdüşümü = D * n, n = (-Uy, Ux)
                    double nx = -g.Uy, ny = g.Ux;
                    aks.X0 = g.D * nx + t0 * g.Ux; aks.Y0 = g.D * ny + t0 * g.Uy;
                    aks.X1 = g.D * nx + t1 * g.Ux; aks.Y1 = g.D * ny + t1 * g.Uy;
                    if (k.Bas != null) k.Bas.Kullanildi = true;
                    if (k.Son != null) k.Son.Kullanildi = true;
                    sonuc.Add(aks);
                }
            }
            return sonuc;
        }

        /// <summary>
        /// Adı olmayan akslara ad üretir (kullanıcı kuralı 14.11.2026): yatay doğrultuda sıralanan akslar (düşey çizgiler)
        /// soldan sağa 1, 2, 3…; düşey doğrultuda sıralananlar (yatay çizgiler) yukarıdan aşağıya A, B, C…
        /// Her kat planı (birbirini kesen akslar kümesi) ayrı adlandırılır; orijinal adlar korunur ve tekrar kullanılmaz.
        /// </summary>
        internal static int AdUret(List<Aks> akslar) => AdUret(akslar, null, 1, out _);

        internal static int AdUret(List<Aks> akslar, List<double[]> katSirasi, double tol, out int eslesen) => AdUret(akslar, katSirasi, tol, null, out eslesen);

        /// <summary>
        /// Kat sıralı adlandırma (kullanıcı kuralı 14.11.2026): planlar <paramref name="katSirasi"/> sırasıyla (alt kattan yukarı)
        /// ele alınır; her üst kat bir alttakiyle aks konumlarından çakıştırılır, aynı yere düşen aks alttakinin adını alır.
        /// Bina küçülse de (aks azalsa da) aynı yerdeki aks aynı adda kalır. Eşleşmeyen akslar sıradaki boş numara/harfi alır.
        /// </summary>
        /// <param name="katSirasi">Kat pencereleri (x0, y0, x1, y1), alt kattan yukarı; null ise planlar soldan sağa sıralanır.</param>
        /// <param name="tol">Aynı yer toleransı (çizim birimi).</param>
        /// <param name="eslesen">Alt kattan ad alan aks sayısı.</param>
        internal static int AdUret(List<Aks> akslar, List<double[]> katSirasi, double tol, List<double[]> kolonlar, out int eslesen)
            => AdUret(akslar, katSirasi, tol, kolonlar, false, out eslesen, out _);

        /// <param name="kolonKutulari">Kolon kapsamları (x0, y0, x1, y1) ya da merkezleri (x, y); çakıştırmada merkez, tamamlamada kapsam kullanılır.</param>
        /// <param name="tamamla">Kolondan geçmeyen akslar eklensin (ad: alttaki kattan ya da komşu adın asalı, 2 ile 3 arası 2').</param>
        internal static int AdUret(List<Aks> akslar, List<double[]> katSirasi, double tol, List<double[]> kolonKutulari, bool tamamla, out int eslesen, out int eklenen)
        {
            int uretilen = 0;
            eslesen = 0; eklenen = 0;
            var kolonlar = kolonKutulari?.Select(k => k.Length >= 4 ? new[] { (k[0] + k[2]) / 2, (k[1] + k[3]) / 2 } : k).ToList();
            var planlar = Planlar(akslar);
            Func<List<Aks>, double> sira = p =>
            {
                double cx = p.Average(a => (a.X0 + a.X1) / 2), cy = p.Average(a => (a.Y0 + a.Y1) / 2);
                if (katSirasi != null)
                    for (int i = 0; i < katSirasi.Count; i++)
                    {
                        var w = katSirasi[i];
                        if (cx >= w[0] && cx <= w[2] && cy >= w[1] && cy <= w[3]) return i * 1e12 + cx;
                    }
                return 1e15 + cx;
            };
            List<Aks> alt = null;
            var oncekiAdlar = new HashSet<string>();
            foreach (var plan in planlar.OrderBy(sira))
            {
                if (tamamla && kolonKutulari != null)
                {
                    var ek = AksTamamla(plan, kolonKutulari.Where(k => k.Length >= 4).ToList(), tol);
                    plan.AddRange(ek); akslar.AddRange(ek); eklenen += ek.Count;
                }
                if (alt != null) eslesen += AlttanAl(plan, alt, tol, kolonlar);
                var kullanilan = new HashSet<string>(plan.Where(a => a.Ad != "").Select(a => a.Ad.ToUpperInvariant()));
                kullanilan.UnionWith(oncekiAdlar);
                int sayi = 1, harf = 0;
                foreach (var a in plan.Where(q => q.Ad == "" && !q.Eklendi && q.Dusey).OrderBy(q => (q.X0 + q.X1) / 2))
                {
                    while (kullanilan.Contains(sayi.ToString())) sayi++;
                    a.Ad = sayi.ToString(); kullanilan.Add(a.Ad); a.AdUretildi = true; uretilen++;
                }
                foreach (var a in plan.Where(q => q.Ad == "" && !q.Eklendi && !q.Dusey).OrderByDescending(q => (q.Y0 + q.Y1) / 2))
                {
                    string ad;
                    do { ad = HarfAd(harf++); } while (kullanilan.Contains(ad));
                    a.Ad = ad; kullanilan.Add(ad); a.AdUretildi = true; uretilen++;
                }
                AsalAdVer(plan, kullanilan);
                alt = plan;
                foreach (var a in plan) oncekiAdlar.Add(a.Ad.ToUpperInvariant());
            }
            return uretilen;
        }

        private static double Konum(Aks a) => a.Dusey ? (a.X0 + a.X1) / 2 : (a.Y0 + a.Y1) / 2;

        /// <summary>
        /// Kolondan geçmeyen akslar (kullanıcı isteği 14.11.2026): kolonun ve perdenin içinden hem düşey hem yatay aks geçmelidir.
        /// Eksik olana kolon ekseninde aks eklenir (aynı hizadaki kolonlar tek aks),
        /// boyu plandaki aynı doğrultulu akslar kadardır; iki ucuna balon istenir.
        /// </summary>
        internal static List<Aks> AksTamamla(List<Aks> plan, List<double[]> kolonlar, double tol)
        {
            var ek = new List<Aks>();
            if (plan.Count == 0) return ek;
            double px0 = plan.Min(a => Math.Min(a.X0, a.X1)), px1 = plan.Max(a => Math.Max(a.X0, a.X1));
            double py0 = plan.Min(a => Math.Min(a.Y0, a.Y1)), py1 = plan.Max(a => Math.Max(a.Y0, a.Y1));
            var icerde = kolonlar.Where(k => (k[0] + k[2]) / 2 > px0 && (k[0] + k[2]) / 2 < px1 && (k[1] + k[3]) / 2 > py0 && (k[1] + k[3]) / 2 < py1).ToList();
            foreach (bool dusey in new[] { true, false })
            {
                var ayni = plan.Where(a => a.Dusey == dusey && Math.Abs(dusey ? a.Ux : a.Uy) < 0.01).ToList();
                var eksik = new List<double>();
                var eksikKolon = new List<double[]>();   // {orta, alt/sol yüz, üst/sağ yüz}
                foreach (var k in icerde)
                {
                    double u0 = dusey ? k[0] : k[1], u1 = dusey ? k[2] : k[3], c = dusey ? (k[1] + k[3]) / 2 : (k[0] + k[2]) / 2;
                    // Kullanıcı kuralı: aks kolonun İÇİNDEN geçmeli; yüzün dışından (birkaç cm bile) geçen aks sayılmaz.
                    bool gecer = plan.Any(a => a.Dusey == dusey && Konum(a) >= u0 - tol / 6 && Konum(a) <= u1 + tol / 6
                        && c >= (dusey ? Math.Min(a.Y0, a.Y1) : Math.Min(a.X0, a.X1)) - tol
                        && c <= (dusey ? Math.Max(a.Y0, a.Y1) : Math.Max(a.X0, a.X1)) + tol);
                    if (!gecer) { eksik.Add((u0 + u1) / 2); eksikKolon.Add(new[] { (u0 + u1) / 2, u0, u1 }); }
                }
                double orta = dusey ? (px0 + px1) / 2 : (py0 + py1) / 2;
                // Aynı hizadaki kolonlar (10 tol içinde) tek aks.
                foreach (var g in Kumele(eksik, 10 * tol))
                {
                    double v = YuzKonumu(g, eksikKolon, icerde, dusey, orta, tol);
                    if (ayni.Any(a => Math.Abs(Konum(a) - v) < tol / 6)) continue;   // aynı yerde aks zaten var
                    double t0 = ayni.Count > 0 ? ayni.Min(a => dusey ? Math.Min(a.Y0, a.Y1) : Math.Min(a.X0, a.X1)) : (dusey ? py0 : px0);
                    double t1 = ayni.Count > 0 ? ayni.Max(a => dusey ? Math.Max(a.Y0, a.Y1) : Math.Max(a.X0, a.X1)) : (dusey ? py1 : px1);
                    ek.Add(dusey
                        ? new Aks { X0 = v, Y0 = t0, X1 = v, Y1 = t1, Ux = 0, Uy = 1, BasYeni = true, SonYeni = true, Eklendi = true }
                        : new Aks { X0 = t0, Y0 = v, X1 = t1, Y1 = v, Ux = 1, Uy = 0, BasYeni = true, SonYeni = true, Eklendi = true });
                }
            }
            return ek;
        }

        /// <summary>
        /// Eklenen aks kolonun ortasından değil yüzünden geçer (kullanıcı isteği 15.11.2026): düşey aks sol/sağ, yatay aks alt/üst yüz.
        /// Adaylar gruptaki kolonların ortak aralığının iki ucudur (aks hepsinin içinden geçmeli). Seçim: plandaki başka kolonların
        /// aynı yüzüyle en çok hizalanan; eşitlikte planın dışına bakan yüz (sol yarıda sol, sağ yarıda sağ). Ortak aralık yoksa orta.
        /// </summary>
        internal static double YuzKonumu(List<double> grup, List<double[]> eksikKolon, List<double[]> kolonlar, bool dusey, double orta, double tol)
        {
            var uyeler = eksikKolon.Where(k => grup.Contains(k[0])).ToList();
            if (uyeler.Count == 0) return grup.Average();
            double alt = uyeler.Max(k => k[1]), ust = uyeler.Min(k => k[2]);
            if (ust - alt < -tol / 6) return grup.Average();
            Func<double, bool, int> hiza = (v, altYuz) => kolonlar.Count(k => Math.Abs((dusey ? (altYuz ? k[0] : k[2]) : (altYuz ? k[1] : k[3])) - v) < tol / 3);
            int hAlt = hiza(alt, true), hUst = hiza(ust, false);
            if (hAlt != hUst) return hAlt > hUst ? alt : ust;
            return (alt + ust) / 2 < orta ? alt : ust;
        }

        private static IEnumerable<List<double>> Kumele(List<double> v, double ara)
        {
            var s = v.OrderBy(x => x).ToList();
            var g = new List<double>();
            foreach (double x in s)
            {
                if (g.Count > 0 && x - g[g.Count - 1] > ara) { yield return g; g = new List<double>(); }
                g.Add(x);
            }
            if (g.Count > 0) yield return g;
        }

        /// <summary>
        /// Eklenen ve alt kattan ad almayan aksın adı (kullanıcı örneği: 2 ile 3 arası 2'): önce iki adlı aks arasındakiler önceki
        /// komşunun asalını alır (yatayda harfler yukarıdan aşağı olduğundan önceki = üstündeki); dolusa bir asal daha (2'').
        /// İlk adlı aksın önündekiler sıfırla adlanır: sayıda 0, 00; harfte A0, A00 (yakından uzağa).
        /// </summary>
        private static void AsalAdVer(List<Aks> plan, HashSet<string> kullanilan)
        {
            const char Asal = (char)39;
            var bos = plan.Where(q => q.Eklendi && q.Ad == "").ToList();
            if (bos.Count == 0) return;
            var adli = plan.Where(q => q.Ad != "" && !q.Eklendi && q.Ad.IndexOf(Asal) < 0).ToList();
            Aks Onceki(Aks a) => a.Dusey ? adli.Where(q => q.Dusey && Konum(q) < Konum(a)).OrderByDescending(Konum).FirstOrDefault()
                                         : adli.Where(q => !q.Dusey && Konum(q) > Konum(a)).OrderBy(Konum).FirstOrDefault();
            Aks Sonraki(Aks a) => a.Dusey ? adli.Where(q => q.Dusey && Konum(q) > Konum(a)).OrderBy(Konum).FirstOrDefault()
                                          : adli.Where(q => !q.Dusey && Konum(q) < Konum(a)).OrderByDescending(Konum).FirstOrDefault();
            foreach (var a in bos.Where(q => Onceki(q) != null).OrderBy(q => q.Dusey ? Konum(q) : -Konum(q)))
            {
                string ad = Onceki(a).Ad + Asal;
                while (kullanilan.Contains(ad.ToUpperInvariant())) ad += Asal;
                a.Ad = ad; kullanilan.Add(ad.ToUpperInvariant());
            }
            foreach (var a in bos.Where(q => Onceki(q) == null).OrderBy(q => q.Dusey ? -Konum(q) : Konum(q)))
            {
                string kok = Sonraki(a)?.Ad ?? (a.Dusey ? "1" : "A");
                bool sayi = kok.All(char.IsDigit);
                string ad = sayi ? "0" : kok + "0";
                while (kullanilan.Contains(ad.ToUpperInvariant())) ad += "0";
                a.Ad = ad; kullanilan.Add(ad.ToUpperInvariant());
            }
        }

        /// <summary>
        /// Üst katı alt kata çakıştırır: düşey akslar X'te, yataylar Y'de en çok eşleşme veren öteleme (en az 2 eş aks).
        /// Düzenli aralıklı akslarda birden çok öteleme aynı sayıda eşleşir; o zaman katlar boyunca devam eden kolonların
        /// en çok üst üste geldiği öteleme seçilir. Adı olmayan üst kat aksı, ötelenmiş konumda alttaki aksın adını alır.
        /// </summary>
        /// <param name="kolonlar">Tüm kolon merkezleri (x, y); null olabilir.</param>
        internal static int AlttanAl(List<Aks> ust, List<Aks> alt, double tol, List<double[]> kolonlar = null)
        {
            var aday = new List<double>[2];
            for (int yon = 0; yon < 2; yon++)
            {
                bool dusey = yon == 0;
                var u = ust.Where(a => a.Dusey == dusey).ToList();
                var l = alt.Where(a => a.Dusey == dusey && a.Ad != "").ToList();
                aday[yon] = new List<double>();
                if (u.Count == 0 || l.Count == 0) continue;
                var puan = new List<(double d, int n, double hata)>();
                foreach (var p in u)
                    foreach (var q in l)
                    {
                        double d = Konum(q) - Konum(p);
                        if (puan.Any(z => Math.Abs(z.d - d) <= tol)) continue;
                        int n = 0; double hata = 0;
                        foreach (var x in u)
                        {
                            double m = l.Min(y => Math.Abs(Konum(y) - Konum(x) - d));
                            if (m <= tol) { n++; hata += m; }
                        }
                        puan.Add((d, n, hata));
                    }
                int enCok = puan.Max(z => z.n);
                if (enCok < Math.Min(2, Math.Min(u.Count, l.Count))) continue;
                aday[yon] = puan.Where(z => z.n == enCok).OrderBy(z => z.hata).Select(z => z.d).ToList();
            }

            // Kolon ile seçim: her öteleme çiftinde üst kat kolonlarından kaçı alt kat kolonuna oturuyor.
            double dx = aday[0].Count > 0 ? aday[0][0] : double.NaN, dy = aday[1].Count > 0 ? aday[1][0] : double.NaN;
            if (kolonlar != null && kolonlar.Count > 0 && (aday[0].Count > 1 || aday[1].Count > 1))
            {
                List<double[]> Icinde(List<Aks> p)
                {
                    double x0 = p.Min(a => Math.Min(a.X0, a.X1)), x1 = p.Max(a => Math.Max(a.X0, a.X1));
                    double y0 = p.Min(a => Math.Min(a.Y0, a.Y1)), y1 = p.Max(a => Math.Max(a.Y0, a.Y1));
                    return kolonlar.Where(k => k[0] >= x0 && k[0] <= x1 && k[1] >= y0 && k[1] <= y1).ToList();
                }
                var ku = Icinde(ust); var kl = Icinde(alt);
                if (ku.Count > 0 && kl.Count > 0)
                {
                    var xs = aday[0].Count > 0 ? aday[0] : new List<double> { 0 };
                    var ys = aday[1].Count > 0 ? aday[1] : new List<double> { 0 };
                    int enIyi = -1; double ktol = 5 * tol;
                    foreach (double cx in xs)
                        foreach (double cy in ys)
                        {
                            int n = ku.Count(k => kl.Any(m => Math.Abs(m[0] - k[0] - cx) <= ktol && Math.Abs(m[1] - k[1] - cy) <= ktol));
                            if (n > enIyi) { enIyi = n; if (aday[0].Count > 0) dx = cx; if (aday[1].Count > 0) dy = cy; }
                        }
                }
            }

            int sayi = 0;
            for (int yon = 0; yon < 2; yon++)
            {
                double d = yon == 0 ? dx : dy;
                if (double.IsNaN(d)) continue;
                bool dusey = yon == 0;
                var l = alt.Where(a => a.Dusey == dusey && a.Ad != "").ToList();
                var alinan = new HashSet<string>(ust.Where(a => a.Ad != "").Select(a => a.Ad.ToUpperInvariant()));
                foreach (var x in ust.Where(a => a.Dusey == dusey && a.Ad == "").OrderBy(Konum))
                {
                    var q = l.OrderBy(y => Math.Abs(Konum(y) - Konum(x) - d)).First();
                    if (Math.Abs(Konum(q) - Konum(x) - d) > tol || alinan.Contains(q.Ad.ToUpperInvariant())) continue;
                    x.Ad = q.Ad; x.AdUretildi = q.AdUretildi; alinan.Add(q.Ad.ToUpperInvariant()); sayi++;
                }
            }
            return sayi;
        }

        /// <summary>Birbirini kesen akslar aynı kat planıdır (yan yana seçilen planlar ayrı kümeler olur).</summary>
        internal static List<List<Aks>> Planlar(List<Aks> akslar)
        {
            int n = akslar.Count;
            var ata = Enumerable.Range(0, n).ToArray();
            Func<int, int> kok = null;
            kok = i => ata[i] == i ? i : (ata[i] = kok(ata[i]));
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (Kesisir(akslar[i], akslar[j])) ata[kok(i)] = kok(j);
            return Enumerable.Range(0, n).GroupBy(kok).Select(g => g.Select(i => akslar[i]).ToList()).ToList();
        }

        private static bool Kesisir(Aks p, Aks q)
        {
            double dx = p.X1 - p.X0, dy = p.Y1 - p.Y0, ex = q.X1 - q.X0, ey = q.Y1 - q.Y0;
            double den = dx * ey - dy * ex;
            if (Math.Abs(den) < 1e-9 * p.Boy * q.Boy) return false;            // paralel
            double t = ((q.X0 - p.X0) * ey - (q.Y0 - p.Y0) * ex) / den, u = ((q.X0 - p.X0) * dy - (q.Y0 - p.Y0) * dx) / den;
            return t >= -0.02 && t <= 1.02 && u >= -0.02 && u <= 1.02;
        }

        private static string HarfAd(int i)
        {
            string s = "";
            do { s = (char)('A' + i % 26) + s; i = i / 26 - 1; } while (i >= 0);
            return s;
        }

        // ───────────── Kapalı çevrim ─────────────

        /// <summary>Uçtan uca yol: bir çizgi (2 nokta) ya da açık çoklu çizgi (köşeleri sırayla).</summary>
        internal sealed class Yol
        {
            public List<double[]> Noktalar;
            public object Kaynak;
        }

        internal sealed class Cevrim
        {
            public List<double[]> Noktalar = new List<double[]>();
            public List<Yol> Yollar = new List<Yol>();
        }

        /// <summary>
        /// Uçları birbirine değen yollardan kapalı çevrimleri bulur: bileşendeki her düğüm tam iki yola değiyorsa çevrimdir.
        /// Dallanan ya da açık kalan bileşenler döndürülmez.
        /// </summary>
        internal static List<Cevrim> CevrimBul(List<Yol> yollar, double tol)
        {
            var dugum = new Dictionary<long, List<int>>();
            Func<double[], long> anahtar = p => ((long)Math.Round(p[0] / tol) << 32) ^ ((long)Math.Round(p[1] / tol) & 0xffffffffL);
            var bas = new long[yollar.Count]; var son = new long[yollar.Count];
            for (int i = 0; i < yollar.Count; i++)
            {
                var n = yollar[i].Noktalar;
                bas[i] = anahtar(n[0]); son[i] = anahtar(n[n.Count - 1]);
                foreach (long k in new[] { bas[i], son[i] })
                {
                    if (!dugum.TryGetValue(k, out var l)) dugum[k] = l = new List<int>();
                    l.Add(i);
                }
            }
            var sonuc = new List<Cevrim>();
            var gezildi = new bool[yollar.Count];
            for (int i = 0; i < yollar.Count; i++)
            {
                if (gezildi[i] || bas[i] == son[i]) continue;
                var c = new Cevrim();
                int simdiki = i; long uc = son[i]; bool ters = false, gecerli = true;
                while (true)
                {
                    gezildi[simdiki] = true;
                    var n = yollar[simdiki].Noktalar;
                    c.Yollar.Add(yollar[simdiki]);
                    if (ters) for (int j = n.Count - 1; j >= 1; j--) c.Noktalar.Add(n[j]);
                    else for (int j = 0; j < n.Count - 1; j++) c.Noktalar.Add(n[j]);
                    var komsu = dugum[uc];
                    if (komsu.Count != 2) { gecerli = false; break; }
                    int diger = komsu[0] == simdiki ? komsu[1] : komsu[0];
                    if (diger == i) break;                                   // çevrim kapandı
                    if (gezildi[diger]) { gecerli = false; break; }
                    ters = son[diger] == uc;
                    uc = ters ? bas[diger] : son[diger];
                    simdiki = diger;
                }
                if (gecerli && dugum[bas[i]].Count == 2 && c.Noktalar.Count >= 3) sonuc.Add(c);
            }
            return sonuc;
        }
    }
}
