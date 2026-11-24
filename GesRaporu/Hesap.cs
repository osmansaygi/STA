namespace GesRaporu
{
    /// <summary>Örnek "ÇATI GES HESABI" raporundaki tahkiklerin hesap motoru. Birimler: kg, m, cm.</summary>
    public sealed class Hesap
    {
        public const double KN = 101.97; // 1 kN → kgf

        public Girdi G { get; }

        // Kar
        public int KarSatir, KarBolge;
        public double SkTablo, KarArtis, SkKn, KarKg;

        // Rüzgar
        public int RuzgarSatir;
        public double QTabloKn, VMs, QHizKn, QKn, QKg;
        public double SinA, CosA, W1, W2, WEmme; // kg/m² (W1 işaretli, WEmme > 0 emme büyüklüğü)

        // Kaplama
        public double GPanel, GBaglanti, GKaplama, GToplam, PKaplama, Ga;
        public bool KaplamaTablodan, KaplamaYeterli;

        // Bulon
        public double GDusey, AlanBulon, WTasarim, WRuz, WBulon, D, Tb, N1, N2, N3, NBulon;
        public bool KaldirmaEtkili, BulonYeterli;

        // Vida
        public double Dv, AVida, PVida, TauVida, SigmaZVida, TSac, EzilmeVida;
        public bool VidaYeterli;

        // Profil
        public double YukAlani, L, QKarT, QPanelT, QAlanT, QRuzT, QHat, QProfilT, QTop, Qx, Qy;
        public double MxAc, MyAc, SxAc, SyAc, SAc, MxMes, MyMes, SxMes, SyMes, SMes, Fx, Fy, FIzin;
        public bool ProfilYeterli, RuzgarProfilBelirleyici;

        public bool HepsiYeterli => KaplamaYeterli && BulonYeterli && VidaYeterli && ProfilYeterli;
        public double KuruluGucKwp => G.PanelAdedi * G.PanelGucWp / 1000.0;

        /// <summary>
        /// Yetersiz çıkan elemanları kademeli olarak büyütür / sıklaştırır; tüm tahkikler sağlanana kadar.
        /// Dönen girdi rapora esas alınır, yapılan değişiklikler listelenir. Çözüm yoksa son kademe kalır.
        /// </summary>
        public static Girdi Kurtar(Girdi kaynak, out List<string> degisiklik)
        {
            var g = kaynak.Kopya();
            degisiklik = new List<string>();
            var tr = new System.Globalization.CultureInfo("tr-TR");
            string N(double x, int d = 2) => x.ToString("0." + new string('#', d), tr);

            // 1) Trapez sac: önce aşık aralığı 50 cm adımlarla (en az 0,50 m), yetmezse sac kalınlığı
            if (!new Hesap(g).KaplamaYeterli && TrapezTablosu.Tablo.ContainsKey(g.KaplamaTipi) && g.KaplamaKapasitesi <= 0)
            {
                double t0 = g.SacKalinligi, a0 = g.AsikAraligi;
                var acikliklar = new List<double> { a0 };
                for (double a = Math.Round(a0 - 0.5, 2); a >= 0.5 - 1e-9; a = Math.Round(a - 0.5, 2)) acikliklar.Add(a);
                if (acikliklar[^1] > 0.5 + 1e-9) acikliklar.Add(0.5);
                var adaylar = TrapezTablosu.Kalinliklar.Where(t => t >= t0 - 1e-6).Prepend(t0).Distinct()
                    .SelectMany(t => acikliklar.Select(a => (t, a)));
                foreach (var (t, a) in adaylar)
                {
                    g.SacKalinligi = t; g.AsikAraligi = a;
                    if (new Hesap(g).KaplamaYeterli) break;
                }
                if (g.SacKalinligi != t0) degisiklik.Add($"Çatı kaplaması sac kalınlığı {N(t0)} → {N(g.SacKalinligi)} mm");
                if (g.AsikAraligi != a0) degisiklik.Add($"Aşık aralığı {N(a0)} → {N(g.AsikAraligi)} m");
            }

            // 2) Bulon: çap → adet → bağlantı profili kalınlığı
            if (!new Hesap(g).BulonYeterli)
            {
                double d0 = g.BulonCapi, t0 = g.BaglantiProfilKalinligi; int n0 = g.MesnetBulonAdedi;
                bool ok = false;
                foreach (var n in new[] { n0, Math.Max(n0, 2), Math.Max(n0, 3), Math.Max(n0, 4) }.Distinct())
                {
                    foreach (var t in new[] { t0, 2.0, 2.5, 3.0, 4.0 }.Where(x => x >= t0).Distinct())
                    {
                        foreach (var d in new[] { d0, 8, 10, 12, 14, 16 }.Where(x => x >= d0).Distinct())
                        {
                            g.MesnetBulonAdedi = n; g.BaglantiProfilKalinligi = t; g.BulonCapi = d;
                            if (new Hesap(g).BulonYeterli) { ok = true; break; }
                        }
                        if (ok) break;
                    }
                    if (ok) break;
                }
                if (g.BulonCapi != d0) degisiklik.Add($"Bağlantı bulonu M{N(d0, 0)} → M{N(g.BulonCapi, 0)}");
                if (g.MesnetBulonAdedi != n0) degisiklik.Add($"Mesnetteki bulon adedi {n0} → {g.MesnetBulonAdedi}");
                if (g.BaglantiProfilKalinligi != t0) degisiklik.Add($"Bağlantı profili kalınlığı {N(t0, 1)} → {N(g.BaglantiProfilKalinligi, 1)} mm");
            }

            // 3) Vida: adet → çap
            if (!new Hesap(g).VidaYeterli)
            {
                double d0 = g.VidaCapi; int n0 = g.ProfilVidaAdedi;
                bool ok = false;
                foreach (var d in new[] { d0, 5.5, 6.3, 8 }.Where(x => x >= d0).Distinct())
                {
                    foreach (var n in new[] { n0, 2, 3, 4, 6, 8 }.Where(x => x >= n0).Distinct())
                    {
                        g.VidaCapi = d; g.ProfilVidaAdedi = n;
                        if (new Hesap(g).VidaYeterli) { ok = true; break; }
                    }
                    if (ok) break;
                }
                if (g.ProfilVidaAdedi != n0) degisiklik.Add($"Mesnet başına vida adedi {n0} → {g.ProfilVidaAdedi}");
                if (g.VidaCapi != d0) degisiklik.Add($"Vida çapı Ø{N(d0, 1)} → Ø{N(g.VidaCapi, 1)} mm");
            }

            // 4) Alüminyum profil: mesnet aralığını sıklaştır
            if (!new Hesap(g).ProfilYeterli)
            {
                double l0 = g.ProfilMesnetAraligi;
                for (double l = Math.Round(l0 - 0.05, 2); l >= 0.10 - 1e-9; l = Math.Round(l - 0.05, 2))
                {
                    g.ProfilMesnetAraligi = l;
                    if (new Hesap(g).ProfilYeterli) break;
                }
                if (g.ProfilMesnetAraligi != l0) degisiklik.Add($"Profil mesnet aralığı {N(l0)} → {N(g.ProfilMesnetAraligi)} m");
            }
            return g;
        }

        public Hesap(Girdi g)
        {
            G = g;
            Kar(); Ruzgar(); Kaplama(); Bulon(); Vida(); Profil();
        }

        private void Kar()
        {
            KarBolge = Math.Clamp(G.KarBolgesi, 1, 9);
            KarSatir = KarTablosu.Satir(G.Rakim);
            SkTablo = KarTablosu.Sk[KarSatir, KarBolge - 1];
            KarArtis = KarTablosu.Artis(G.Rakim);
            SkKn = Math.Round(SkTablo * KarArtis, 3);
            KarKg = SkKn * KN;
        }

        private void Ruzgar()
        {
            RuzgarSatir = RuzgarTablosu.Satir(G.BinaYuksekligi);
            QTabloKn = RuzgarTablosu.Satirlar[RuzgarSatir].Q;
            VMs = G.RuzgarHiziKmSa * 1000 / 3600;
            QHizKn = VMs * VMs / 1600;
            QKn = Math.Max(QTabloKn, QHizKn);
            QKg = QKn * KN;
            double a = G.CatiEgimi * Math.PI / 180;
            SinA = Math.Sin(a); CosA = Math.Cos(a);
            W1 = (1.2 * SinA - 0.4) * QKg;
            W2 = -0.4 * QKg;
            WEmme = Math.Max(-W1, -W2);
        }

        private void Kaplama()
        {
            GPanel = G.PanelAgirlik / (G.PanelA * G.PanelB);
            GBaglanti = G.BaglantiAgirligi / G.PanelA;
            GKaplama = G.KaplamaAgirligi;
            GToplam = GPanel + GBaglanti + GKaplama;
            PKaplama = 1.4 * GToplam + 1.6 * KarKg;
            double t = TrapezTablosu.Kapasite(G.KaplamaTipi, G.SacKalinligi, G.AsikAraligi);
            KaplamaTablodan = G.KaplamaKapasitesi <= 0 && !double.IsNaN(t);
            Ga = G.KaplamaKapasitesi > 0 ? G.KaplamaKapasitesi : double.IsNaN(t) ? 0 : t;
            KaplamaYeterli = Ga >= PKaplama;
        }

        private void Bulon()
        {
            GDusey = GPanel + GBaglanti;                 // kg/m² (kaldırmaya karşı koyan zati yük)
            KaldirmaEtkili = WEmme > GDusey;
            AlanBulon = G.PanelA * G.PanelB;             // m²
            WTasarim = 1.15 * WEmme;                     // kg/m² (%15 artırılmış)
            WRuz = WTasarim * AlanBulon * 0.5;           // kg (vida yük alanı 0.5)
            WBulon = WRuz / Math.Max(1, G.MesnetBulonAdedi);
            D = G.BulonCapi / 10;                        // cm
            Tb = G.BaglantiProfilKalinligi / 10;         // cm
            double ad = Math.PI * D * D / 4;
            N1 = Baglanti.TauEm * ad;
            N2 = D * Tb * Baglanti.SigmaLEm;
            N3 = Baglanti.SigmaZEm * ad;
            NBulon = Math.Min(N1, Math.Min(N2, N3));
            BulonYeterli = NBulon >= WBulon;
        }

        private void Vida()
        {
            Dv = G.VidaCapi / 10;                         // cm
            AVida = Math.PI * Math.Pow(0.8 * Dv, 2) / 4;  // çekirdek alanı ≈ π(0.8d)²/4
            PVida = WRuz / Math.Max(1, G.ProfilVidaAdedi);
            TauVida = PVida / AVida;
            SigmaZVida = PVida / AVida;
            TSac = G.SacKalinligi / 10;                   // cm
            EzilmeVida = PVida / (TSac * Dv);
            VidaYeterli = TauVida <= Baglanti.VidaTauEm && SigmaZVida <= Baglanti.VidaSigmaZEm && EzilmeVida <= Baglanti.VidaEzilmeEm;
        }

        private void Profil()
        {
            YukAlani = G.PanelA / 2;                      // m (Lp/2)
            L = G.ProfilMesnetAraligi;                    // m
            QKarT = KarKg / 1000;                         // t/m²
            QPanelT = GPanel / 1000;
            QAlanT = QKarT + QPanelT;
            QRuzT = WTasarim / 1000;
            RuzgarProfilBelirleyici = QRuzT > QAlanT;
            QHat = Math.Max(QAlanT, QRuzT) * YukAlani;    // t/m
            QProfilT = Aluminyum.ProfilG / 1000;
            QTop = QHat + QProfilT;
            Qx = QTop * CosA; Qy = QTop * SinA;

            // t·m → kg·cm: ×1e5 ; σ (kg/cm²)
            MxAc = Qx * L * L / 16; MyAc = Qy * L * L / 16;
            SxAc = MxAc * 1e5 / Aluminyum.Wx; SyAc = MyAc * 1e5 / Aluminyum.Wy; SAc = SxAc + SyAc;
            MxMes = Qx * L * L / 11; MyMes = Qy * L * L / 11;
            SxMes = MxMes * 1e5 / Aluminyum.Wx; SyMes = MyMes * 1e5 / Aluminyum.Wy; SMes = SxMes + SyMes;

            // Sehim: δ = 5 q l⁴ / (384 E I), q kg/cm, l cm
            double lc = L * 100;
            Fx = 5 * (Qx * 10) * Math.Pow(lc, 4) / (384 * Aluminyum.E * Aluminyum.Jx);
            Fy = 5 * (Qy * 10) * Math.Pow(lc, 4) / (384 * Aluminyum.E * Aluminyum.Jy);
            FIzin = lc / 300;
            ProfilYeterli = Math.Max(SAc, SMes) <= Aluminyum.SigmaEm && Fx <= FIzin && Fy <= FIzin;
        }
    }
}
