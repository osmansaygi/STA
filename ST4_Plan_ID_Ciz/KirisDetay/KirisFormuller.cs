using System;

namespace ST4PlanIdCiz.KirisDetay
{
    /// <summary>
    /// TS 500:2000 ve TBDY 2018 kiriş detay formülleri.
    /// Madde numaraları docs/kiris_detay_kurallari.md ile aynıdır.
    /// </summary>
    public static class KirisFormuller
    {
        public const double EsMpa = 200000;

        public static double TekCubukAlani(double phiMm)
        {
            return Math.PI * phiMm * phiMm / 4.0;
        }

        /// <summary>Metraj birim ağırlığı, kg/m. φ mm. [UYGULAMA] G = φ²/162.</summary>
        public static double BirimAgirlikKgM(double phiMm)
        {
            return phiMm * phiMm / 162.0;
        }

        /// <summary>TS500 9.5.3 Denk. 9.3. φe = 1.2·φ·√n</summary>
        public static double EsdegerCap(double phiMm, int demetAdet)
        {
            if (demetAdet <= 1) return phiMm;
            return 1.2 * phiMm * Math.Sqrt(demetAdet);
        }

        public static double Fyk(CelikSinifi celik)
        {
            return celik == CelikSinifi.B500C ? 500.0 : 420.0;
        }

        public static double Fyd(CelikSinifi celik, double gammaMs)
        {
            return Fyk(celik) / gammaMs;
        }

        public static void BetonSatiri(BetonSinifi sinif, out double fck, out double fctkTablo, out double ec, out double k1)
        {
            // TS500 Çizelge 3.2 (fck, fctk, Ec) ve Çizelge 7.1 (k1).
            switch (sinif)
            {
                case BetonSinifi.C20: fck = 20; fctkTablo = 1.6; ec = 28000; k1 = 0.85; return;
                case BetonSinifi.C25: fck = 25; fctkTablo = 1.8; ec = 30000; k1 = 0.85; return;
                case BetonSinifi.C30: fck = 30; fctkTablo = 1.9; ec = 32000; k1 = 0.82; return;
                case BetonSinifi.C35: fck = 35; fctkTablo = 2.1; ec = 33000; k1 = 0.79; return;
                case BetonSinifi.C40: fck = 40; fctkTablo = 2.2; ec = 34000; k1 = 0.76; return;
                case BetonSinifi.C45: fck = 45; fctkTablo = 2.3; ec = 36000; k1 = 0.73; return;
                default: fck = 50; fctkTablo = 2.5; ec = 37000; k1 = 0.70; return;
            }
        }

        /// <summary>TS500 Denk. 3.1: fctk = 0.35·√fck</summary>
        public static double FctkFormul(double fck)
        {
            return 0.35 * Math.Sqrt(fck);
        }

        public static double Fctd(BetonSinifi sinif, FctdKaynagi kaynak, double gammaMc, out double fctdFormul)
        {
            double fck, fctk, ec, k1;
            BetonSatiri(sinif, out fck, out fctk, out ec, out k1);
            fctdFormul = FctkFormul(fck) / gammaMc;
            if (kaynak == FctdKaynagi.Formul) return fctdFormul;
            return fctk / gammaMc;
        }

        /// <summary>TS500 Denk. 7.3 ve TBDY Denk. 7.8: ρmin = 0.8·fctd/fyd</summary>
        public static double RhoMin(double fctd, double fyd)
        {
            return 0.8 * fctd / fyd;
        }

        /// <summary>TS500 7.1 varsayımlarından: ρb = 0.85·k1·(fcd/fyd)·600/(600+fyd)</summary>
        public static double RhoB(double k1, double fcd, double fyd)
        {
            return 0.85 * k1 * (fcd / fyd) * 600.0 / (600.0 + fyd);
        }

        /// <summary>TS500 9.5.2 güvenli taraf: max(φ, 25 mm, 4/3·Dmax). 7.3'teki 20 mm kullanılmaz.</summary>
        public static double SMin(double phiMaxMm, double dmaxMm, double mutlakMinMm)
        {
            return Math.Max(phiMaxMm, Math.Max(mutlakMinMm, (4.0 / 3.0) * dmaxMm));
        }

        /// <summary>Etriye içi net genişlik: bw − 2·cc − 2·φw</summary>
        public static double Bav(double bwMm, double ccMm, double phiWMm)
        {
            return bwMm - 2.0 * ccMm - 2.0 * phiWMm;
        }

        /// <summary>Tek çaplı sıraya sığan adet: floor((b_av + smin)/(φ + smin))</summary>
        public static int NMax(double bavMm, double phiMm, double sMinMm)
        {
            if (phiMm <= 0 || sMinMm < 0) return 0;
            double payda = phiMm + sMinMm;
            if (payda <= 0) return 0;
            return (int)Math.Floor((bavMm + sMinMm) / payda + 1e-9);
        }

        public static double SNet(double bavMm, double toplamCapMm, int adet)
        {
            if (adet <= 1) return double.PositiveInfinity;
            return (bavMm - toplamCapMm) / (adet - 1);
        }

        /// <summary>
        /// Köşe çubuğu merkezi, etriye bacağı iç yüzünden.
        /// R = Db/2, x = R − (R − φ/2)/√2 , R &gt; φ/2 ise.
        /// </summary>
        public static double KoseEksenIcYuzden(double phiMm, double phiWMm, double icCapCarpan)
        {
            double yaricap = phiMm / 2.0;
            double r = icCapCarpan * phiWMm / 2.0;
            if (r <= yaricap) return yaricap;
            return r - (r - yaricap) / Math.Sqrt(2.0);
        }

        /// <summary>TS500 Denk. 9.1: ℓb0 = max(0.12·(fyd/fctd)·φ, 20φ). Düz çubukta 2 kat.</summary>
        public static double Lb0(double fyd, double fctd, double phiMm, bool nervurlu)
        {
            double v = Math.Max(0.12 * (fyd / fctd) * phiMm, 20.0 * phiMm);
            if (!nervurlu) v *= 2.0;
            return v;
        }

        /// <summary>
        /// Düz kenetlenme. 32 &lt; φ ≤ 40 için 100/(132−φ) (TS500 9.1.2.1).
        /// Örtü/aralık yetersizse ×1.2. Konum I ise ×1.4 (bir kez).
        /// </summary>
        public static double LbHam(
            double lb0,
            double phiMm,
            bool konumI,
            bool kOrtu,
            bool konumKatsayisi,
            bool kAsUygula,
            double kAs)
        {
            double v = lb0;
            if (phiMm > 32.0 && phiMm <= 40.0)
                v *= 100.0 / (132.0 - phiMm);
            if (kOrtu)
                v *= 1.2;
            if (konumI && konumKatsayisi)
                v *= 1.4;
            if (kAsUygula)
            {
                double alt = Math.Max(0.5 * lb0, 20.0 * phiMm);
                v = Math.Max(v * kAs, alt);
            }
            return v;
        }

        /// <summary>
        /// Çekme bindirmesi ℓ0 = α1·ℓb . Konum I çarpanı ℓb içinde bir kez vardır; burada tekrarlanmaz.
        /// α1 = 1+0.5·r (TS500 Denk. 9.2). Kancalı ekte ×0.75. Demette ×1.2 (TS500 9.2.2).
        /// </summary>
        public static double L0Ham(double lbHamKonumlu, double alpha1, bool kancali, bool demet)
        {
            double v = alpha1 * lbHamKonumlu;
            if (kancali) v *= 0.75;
            if (demet) v *= 1.2;
            return v;
        }

        public static double Alpha1(double r, bool tamKesitCekme)
        {
            if (tamKesitCekme) return 1.8;
            if (r < 0) r = 0;
            if (r > 1) r = 1;
            return 1.0 + 0.5 * r;
        }

        /// <summary>
        /// TBDY 7.4.4 metin: DY yüksek min(d/4, 8φmin, 150).
        /// TBDY 7.8.4: DY sınırlı min(h/4, 8φmin, 200).
        /// </summary>
        public static double SarilmaAralikMax(
            SuneklikDuzeyi dy,
            double dMm,
            double hMm,
            double phiMinMm,
            bool yuksekteDBolum4,
            double ustSinirYuksekMm,
            double ustSinirSinirliMm)
        {
            double sekizPhi = 8.0 * phiMinMm;
            if (dy == SuneklikDuzeyi.Yuksek)
            {
                double yukseklikPay = yuksekteDBolum4 ? dMm / 4.0 : hMm / 4.0;
                return Math.Min(yukseklikPay, Math.Min(sekizPhi, ustSinirYuksekMm));
            }
            return Math.Min(hMm / 4.0, Math.Min(sekizPhi, ustSinirSinirliMm));
        }

        /// <summary>TBDY 7.4.4: eksene dik kol aralığı ≤ 350 mm. n = max(2, ceil(e/350)+1)</summary>
        public static int EtriyeKolSayisi(double bwMm, double ccMm, double phiWMm, double kolMaxMm)
        {
            double e = bwMm - 2.0 * ccMm - phiWMm;
            if (kolMaxMm <= 0) return 2;
            int n = (int)Math.Ceiling(e / kolMaxMm - 1e-9) + 1;
            if (n < 2) n = 2;
            return n;
        }

        public static double EksenAraligi(double bwMm, double ccMm, double phiWMm)
        {
            return bwMm - 2.0 * ccMm - phiWMm;
        }

        /// <summary>TS500 Denk. 8.6: Asw/s ≥ 0.3·(fctd/fywd)·bw</summary>
        public static double MinEtriyeAswBolumS(double fctd, double fywd, double bwMm)
        {
            return 0.3 * (fctd / fywd) * bwMm;
        }

        /// <summary>TS500 Denk. 8.1: Vcr = 0.65·fctd·bw·d·(1+γ·Nd/Ac)</summary>
        public static double Vcr(double fctd, double bwMm, double dMm, double gamma, double ndN, double acMm2)
        {
            double carpan = 1.0;
            if (acMm2 > 0) carpan += gamma * ndN / acMm2;
            return 0.65 * fctd * bwMm * dMm * carpan;
        }

        public static double CcMin(KirisCevre cevre)
        {
            // TS500 9.5.1 Çizelge 9.3 ve 7.3.
            if (cevre == KirisCevre.ZeminleTemas) return 50;
            if (cevre == KirisCevre.Ic) return 20;
            return 25;
        }

        public static bool DtsYuksekOran(DepremTasarimSinifi dts)
        {
            // TBDY 7.4.2.3: DTS 1, 1a, 2, 2a → %50; diğer → %30.
            return dts == DepremTasarimSinifi.Dts1
                || dts == DepremTasarimSinifi.Dts1a
                || dts == DepremTasarimSinifi.Dts2
                || dts == DepremTasarimSinifi.Dts2a;
        }

        public static bool DySinirliIzinli(DepremTasarimSinifi dts)
        {
            // TBDY 4.3.4: sınırlı süneklik yalnız DTS 3 ve 4.
            return dts == DepremTasarimSinifi.Dts3 || dts == DepremTasarimSinifi.Dts4;
        }

        public static double YukariYuvarla(double degerMm, double adimMm)
        {
            if (adimMm <= 0) return degerMm;
            decimal adim = (decimal)adimMm;
            decimal oran = decimal.Round((decimal)degerMm / adim, 6, MidpointRounding.AwayFromZero);
            return (double)(Math.Ceiling(oran) * adim);
        }

        public static double AsagiYuvarla(double degerMm, double adimMm)
        {
            if (adimMm <= 0 || degerMm <= 0) return degerMm;
            decimal adim = (decimal)adimMm;
            decimal oran = decimal.Round((decimal)degerMm / adim, 6, MidpointRounding.AwayFromZero);
            decimal kat = Math.Floor(oran);
            if (kat < 1) return degerMm;
            return (double)(kat * adim);
        }

        public static double EnYakinMm(double degerMm)
        {
            return (double)Math.Round((decimal)degerMm, 0, MidpointRounding.AwayFromZero);
        }

        /// <summary>Kenar mesnette kullanılabilir yatay gömme: hc − cc − φw − φ/2.</summary>
        public static double AMevcut(double hcMm, double ccKolonMm, double phiWKolonMm, double phiMm, double phiKolonMm, bool kolonBoyunaDus)
        {
            double a = hcMm - ccKolonMm - phiWKolonMm - phiMm / 2.0;
            if (kolonBoyunaDus) a -= phiKolonMm;
            return a;
        }
    }
}
