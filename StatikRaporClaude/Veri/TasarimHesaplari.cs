namespace StatikRaporClaude.Veri
{
    /// <summary>
    /// STA4'ün sonuç dosyalarına yazmadığı, rapor anında hesapladığı değerler: çatlak genişliği (Wk), döşeme zamana bağlı
    /// sehimi ve perde moment zarfı (Myu/Mya). Bağıntılar 26 proje GPR'sindeki değerlerle geriye doğru kalibre edildi
    /// (bkz. docs/modul-durum/StatikRaporClaude.md "Hesaplanan değerler").
    /// </summary>
    public static class TasarimHesaplari
    {
        // ---------------- çatlak genişliği ----------------
        //
        // STA4 Wk'yı ACI 318 Gergely–Lutz bağıntısıyla hesaplıyor: w = 0.011·β·fs·∛(dc·A)·10⁻³ (mm, MPa),
        // A = 2·dc·b/n (çubuk başına etkili çekme alanı), β = 1.2, fs = 0.6·fy. Konan donatı gerekenin 1.2 katını aşarsa
        // fs bir kademe düşer (÷1.2). Wk; momentten, beton sınıfından ve kesit yüksekliğinden bağımsızdır.

        private const double FsKatsayi = 0.011 * 1.2 * 0.6 * 420 / 1000.0;   // 0.003326 mm/mm
        private const double DonatiFazlasi = 1.2;

        private static double AciWk(double dc, double cubukBasinaGenislik, double gerekli, double konan)
        {
            double kip = konan > 0 && gerekli / konan > 1 / DonatiFazlasi ? 1 : 1 / DonatiFazlasi;
            return FsKatsayi * kip * Math.Pow(2 * dc * dc * cubukBasinaGenislik, 1.0 / 3);
        }

        /// <summary>Kiriş açıklık alt donatısına göre Wk (mm). KMX: +112/+114 düz adet/çap, +116/+118 pilye, +76 gerekli açıklık As.</summary>
        public static double KirisWk(IkiliKayit kmx, St4Kiris k)
        {
            int n = kmx.S(112), f = kmx.S(114);
            int np = kmx.S(116) > 0 && kmx.S(118) > 0 ? kmx.S(116) : 0;
            if (n <= 0 || f <= 0) return 0;
            double konan = (n + np) * Math.PI * f * f / 400.0;
            return AciWk(KirisDc(k.B * 10.0, n + np, f), k.B * 10.0 / (n + np), kmx.F(76), konan);
        }

        /// <summary>
        /// Kirişte çekme donatısı ağırlık merkezinin dış yüze uzaklığı (mm). Tek sırada dc = 57 + 0.24·ø; sıraya sığmayan
        /// çubuklar (paspayı 25 mm, en az 12 mm net aralık) ikinci sıraya ø + 25 mm yukarı konur.
        /// Kalibrasyon: 141 (B, n, ø, kip) düzeninin 139'u, ~6300 kirişin 25'i dışında tümü birebir.
        /// </summary>
        private static double KirisDc(double b, int n, int f)
        {
            int sira = Math.Max(2, (int)Math.Floor((b - 2 * 25 + 12) / (f + 12.0)));
            double dc = 57 + 0.24 * f;
            return n <= sira ? dc : dc + (n - sira) / (double)n * (f + 25);
        }

        /// <summary>
        /// Döşeme Wk (mm): iki doğrultudan büyüğü. Açıklık alt donatısı düz + pilye birlikte etkin aralıkla alınır.
        /// STA4 bazı döşemelerde Wk'yı 0.00 basar; dosyalardan ayırt edilebilen tek koşul pilyesizliktir (yaklaşık).
        /// </summary>
        public static double DosemeWk(IkiliKayit dmx)
        {
            var x = DosemeYonu(dmx, 96, 32); var y = DosemeYonu(dmx, 114, 44);
            if (!x.Pilye && !y.Pilye) return 0;
            double W((int f, double s, double konan, double gerekli, bool Pilye) a) =>
                a.f <= 0 ? 0 : AciWk(DosemeDcSabit + 0.5 * a.f, a.s, a.gerekli, a.konan) * DosemeOran;
            return Math.Max(W(x), W(y));
        }

        // kalibrasyon: 887 döşeme düzeninin 777'si birebir; C = 0.0031 → FsKatsayi·DosemeOran
        private const double DosemeDcSabit = 33.7, DosemeOran = 0.0031 / FsKatsayi;

        private static (int f, double s, double konan, double gerekli, bool Pilye) DosemeYonu(IkiliKayit d, int ofset, int asOfset)
        {
            short a(int i) => d.S(ofset + 2 * i);
            int f = a(0), s = a(1);
            if (f <= 0 || s <= 0) return (0, 0, 0, 0, false);
            double konan = Math.PI * f * f / 400.0 * 100.0 / s, aralik = s * 10.0;
            bool pilye = a(2) == 1 && a(3) > 0 && a(4) > 0;
            if (pilye)
            {
                konan += Math.PI * a(3) * a(3) / 400.0 * 100.0 / a(4);
                aralik = 10.0 / (1.0 / s + 1.0 / a(4));
            }
            return (f, aralik, konan, d.F(asOfset), pilye);
        }

        // ---------------- döşeme sehimi ----------------

        /// <summary>Zamana bağlı toplam sehim (mm): δt = δi + δig·λ, δig = δi·G/(G+Q), λ = 2 (5 yıl üzeri).</summary>
        public static double DosemeToplamSehim(double aniMm, St4Doseme d) =>
            d.G + d.Q > 0 ? aniMm * (1 + 2 * d.G / (d.G + d.Q)) : aniMm;

        // ---------------- perde moment zarfı ----------------

        /// <summary>
        /// Perde tasarım eğilme momenti zarfı (TBDY 7.6.2.2): kat üstü (…u) ve kat altı (…a) değerleri (tm).
        /// Güçlü eksen uzun kenara göre: By ≥ Bx → My (deprem Y, etiket "My"), değilse Mx (deprem X, etiket "Mx").
        /// Mb: bodrum (Hbodrum) üstündeki ilk perde parçasının alt ucundaki deprem momenti; Mt: en üst parçanın üst ucundaki.
        /// Hcr = max(min(max(lw, Hw/6), 2·lw), taban parçasının kat yüksekliği); z ≤ Hbodrum + Hcr'de M = Mb, üstünde Mt'ye doğrusal.
        /// Poligon kolonda iki eksen birden (önce Mx, sonra My; lw = kesit sınır kutusunun o yöndeki boyu).
        /// Kalibrasyon: N_ULK_02, CP_03, VKF_01, FHM_GO_A, K_BLG_03 perdeleri.
        /// </summary>
        public static Dictionary<St4Kolon, List<(string Eksen, double Ust, double Alt)>> PerdeMomentleri(StaVeri v)
        {
            var sonuc = new Dictionary<St4Kolon, List<(string, double, double)>>();
            foreach (var aks in v.Model.Kolonlar.Where(DosyaTablolari.Perde).GroupBy(k => (k.Yerel, k.Poligon)))
            {
                var parca = aks.OrderBy(k => k.Kat).ToList();   // alttan üste
                var ilk = parca[0];
                var eksenler = ilk.Poligon ? new[] { false, true }.Where(y => y ? ilk.PerdeY : ilk.PerdeX).ToArray() : new[] { ilk.By >= ilk.Bx };
                foreach (bool yYonu in eksenler)
                    foreach (var (k, ust, alt) in Zarf(v, parca, yYonu))
                    {
                        if (!sonuc.TryGetValue(k, out var l)) sonuc[k] = l = new List<(string, double, double)>();
                        l.Add((yYonu ? "My" : "Mx", ust, alt));
                    }
            }
            return sonuc;
        }

        private static IEnumerable<(St4Kolon, double, double)> Zarf(StaVeri v, List<St4Kolon> parca, bool yYonu)
        {
            var kat = v.Model.Katlar;
            double KatKotu(int i) => i >= 0 && i < kat.Count ? kat[i].Kot : 0;
            var ilk = parca[0];
            {
                int ust = yYonu ? 290 : 114, alt = yYonu ? 378 : 202, e = yYonu ? 40 : 32;   // deprem ± indisleri 10/11 veya 8/9
                double Deprem(St4Kolon k, int of)
                {
                    var c = v.Col[v.KolonKaydi(k)];
                    return Math.Max(Math.Abs(c.F(of + e)), Math.Abs(c.F(of + e + 4)));
                }
                // STA4 zarfı kat kotlarında değerlendirir; kademeli perdede kolonun kendi kotu kullanılmaz (K_BLG_03 perde 18/19)
                double Ust(St4Kolon k) => KatKotu(k.Kat);
                double Alt(St4Kolon k) => KatKotu(k.Kat - 1);

                double hb = v.Model.Hbodrum;
                var taban = parca.FirstOrDefault(k => Ust(k) > hb + 0.01) ?? parca[0];
                double mb = Deprem(taban, alt), mt = Deprem(parca[^1], ust);
                double tepe = Ust(parca[^1]);
                double lw = (yYonu ? ilk.By : ilk.Bx) / 100.0, hw = tepe - hb;
                double tabanKat = Ust(taban) - KatKotu(taban.Kat - 1);
                double hcr = Math.Max(Math.Min(Math.Max(lw, hw / 6), 2 * lw), tabanKat);
                double z0 = hb + hcr;
                double M(double z) => z <= z0 || tepe <= z0 ? mb : mb - (mb - mt) * (z - z0) / (tepe - z0);

                foreach (var k in parca)
                    yield return (k, k == parca[^1] ? mt : M(Ust(k)), M(Alt(k)));
            }
        }
    }
}
