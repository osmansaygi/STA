using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// Birbirini kesen zarf dikdörtgenleri önce tek zarf olur.
    /// O dikdörtgen, donatı doğrultusunda lejant kademesine göre dilimlenir.
    /// Dilimler aynı boyda kalır; kutular birbirini kesmez.
    /// </summary>
    public static class TemelIlaveKutuBol
    {
        private const double MinKutuCm = 8.0;

        /// <summary>Aynı doğrultuda zarf dikdörtgenleri kesişen kümeler.</summary>
        public static List<List<int>> IcIceKumeler(IReadOnlyList<Polygon> sekiller, IReadOnlyList<bool> yatay)
        {
            var sonuc = new List<List<int>>();
            int n = sekiller == null ? 0 : sekiller.Count;
            if (n < 2 || yatay == null || yatay.Count != n) return sonuc;

            var parent = new int[n];
            var zarf = new Envelope[n];
            for (int i = 0; i < n; i++)
            {
                parent[i] = i;
                if (sekiller[i] == null || sekiller[i].IsEmpty) continue;
                zarf[i] = sekiller[i].EnvelopeInternal;
            }

            for (int i = 0; i < n; i++)
            {
                if (zarf[i] == null) continue;
                for (int j = i + 1; j < n; j++)
                {
                    if (yatay[i] != yatay[j] || zarf[j] == null) continue;
                    if (!ZarfKesisiyor(zarf[i], zarf[j])) continue;
                    parent[Kok(parent, j)] = Kok(parent, i);
                }
            }

            var map = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (zarf[i] == null) continue;
                int kok = Kok(parent, i);
                List<int> liste;
                if (!map.TryGetValue(kok, out liste))
                {
                    liste = new List<int>();
                    map[kok] = liste;
                }
                liste.Add(i);
            }
            foreach (List<int> liste in map.Values)
            {
                if (liste.Count >= 2) sonuc.Add(liste);
            }
            return sonuc;
        }

        /// <summary>
        /// Grupların nihai kutuları (x0,y0,x1,y1) aynı doğrultuda birbirine giriyorsa birleşecek kümeler.
        /// Yalnız kenar teması birleştirmez. Dönüşte yalnız 2+ gruplu kümeler, küçük indeks önce.
        /// </summary>
        public static List<List<int>> KutuCakisanKumeler(IReadOnlyList<IReadOnlyList<double[]>> kutular, IReadOnlyList<bool> yatay)
        {
            var sonuc = new List<List<int>>();
            int n = kutular == null ? 0 : kutular.Count;
            if (n < 2 || yatay == null || yatay.Count != n) return sonuc;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (yatay[i] != yatay[j] || !KutularCakisir(kutular[i], kutular[j])) continue;
                    int ki = Kok(parent, i);
                    int kj = Kok(parent, j);
                    if (ki == kj) continue;
                    if (ki < kj) parent[kj] = ki;
                    else parent[ki] = kj;
                }
            }
            var map = new SortedDictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int kok = Kok(parent, i);
                if (!map.TryGetValue(kok, out List<int> liste))
                {
                    liste = new List<int>();
                    map[kok] = liste;
                }
                liste.Add(i);
            }
            foreach (List<int> liste in map.Values)
                if (liste.Count >= 2) sonuc.Add(liste);
            return sonuc;
        }

        private static bool KutularCakisir(IReadOnlyList<double[]> a, IReadOnlyList<double[]> b)
        {
            if (a == null || b == null) return false;
            foreach (double[] p in a)
            {
                if (p == null || p.Length < 4) continue;
                foreach (double[] q in b)
                {
                    if (q == null || q.Length < 4) continue;
                    double ix = Math.Min(p[2], q[2]) - Math.Max(p[0], q[0]);
                    double iy = Math.Min(p[3], q[3]) - Math.Max(p[1], q[1]);
                    if (ix > 1.0 && iy > 1.0) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Kesişen zarfların tek dikdörtgenini lejant kademesine göre dilimler.
        /// Dönüş: x0,y0,x1,y1. Aynı kademe tek dikdörtgen kalır.
        /// kademeOut verilirse her kutunun lejant kademesi aynı sırayla eklenir (bilinmiyorsa int.MinValue).
        /// </summary>
        public static List<double[]> ZarfDilimle(
            IReadOnlyList<Polygon> sekiller,
            IReadOnlyList<int> kademeler,
            bool yataySerit,
            IReadOnlyList<double> alanlar = null,
            IReadOnlyList<int> araliklar = null,
            List<int> kademeOut = null)
        {
            var sonuc = new List<double[]>();
            if (sekiller == null || sekiller.Count == 0) return sonuc;

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            var gecerli = new List<int>();
            for (int i = 0; i < sekiller.Count; i++)
            {
                if (sekiller[i] == null || sekiller[i].IsEmpty) continue;
                Envelope e = sekiller[i].EnvelopeInternal;
                if (e.MinX < minX) minX = e.MinX;
                if (e.MinY < minY) minY = e.MinY;
                if (e.MaxX > maxX) maxX = e.MaxX;
                if (e.MaxY > maxY) maxY = e.MaxY;
                gecerli.Add(i);
            }
            if (gecerli.Count == 0 || maxX - minX < MinKutuCm || maxY - minY < MinKutuCm)
                return sonuc;

            double minA = yataySerit ? minY : minX;
            double maxA = yataySerit ? maxY : maxX;
            double minB = yataySerit ? minX : minY;
            double maxB = yataySerit ? maxX : maxY;

            var ham = new List<double> { minA, maxA };
            foreach (int ix in gecerli)
            {
                foreach (Coordinate c in sekiller[ix].ExteriorRing.Coordinates)
                    ham.Add(yataySerit ? c.Y : c.X);
            }
            ham.Sort();
            var sinir = new List<double>();
            foreach (double deger in ham)
            {
                if (deger < minA - 1.0 || deger > maxA + 1.0) continue;
                double a = deger < minA ? minA : (deger > maxA ? maxA : deger);
                if (sinir.Count == 0 || a - sinir[sinir.Count - 1] > 5.0)
                    sinir.Add(a);
            }
            if (sinir.Count == 0 || sinir[0] > minA + 1.0) sinir.Insert(0, minA);
            else sinir[0] = minA;
            if (sinir[sinir.Count - 1] < maxA - 1.0) sinir.Add(maxA);
            else sinir[sinir.Count - 1] = maxA;

            GeometryFactory gf = sekiller[gecerli[0]].Factory;
            var serit = new List<LejantDilim>();
            for (int s = 0; s < sinir.Count - 1; s++)
            {
                double a0 = sinir[s];
                double a1 = sinir[s + 1];
                if (a1 - a0 < 5.0) continue;
                double orta = (a0 + a1) * 0.5;
                LineString cizgi = yataySerit
                    ? gf.CreateLineString(new[] { new Coordinate(minB - 20.0, orta), new Coordinate(maxB + 20.0, orta) })
                    : gf.CreateLineString(new[] { new Coordinate(orta, minB - 20.0), new Coordinate(orta, maxB + 20.0) });
                int kademe = int.MinValue;
                double alan = 0;
                int aralik = 0;
                bool degdi = false;
                foreach (int ix in gecerli)
                {
                    try
                    {
                        if (!sekiller[ix].Intersects(cizgi)) continue;
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    degdi = true;
                    int k = kademeler != null && ix < kademeler.Count ? kademeler[ix] : 0;
                    if (k <= kademe) continue;
                    kademe = k;
                    alan = alanlar != null && ix < alanlar.Count ? alanlar[ix] : k;
                    aralik = araliklar != null && ix < araliklar.Count ? araliklar[ix] : 0;
                }
                if (!degdi) continue;
                if (serit.Count > 0 && serit[serit.Count - 1].Kademe == kademe)
                    serit[serit.Count - 1].A1 = a1;
                else
                    serit.Add(new LejantDilim { A0 = a0, A1 = a1, Kademe = kademe, Alan = alan, AralikCm = aralik });
            }

            if (serit.Count == 0)
                serit.Add(new LejantDilim { A0 = minA, A1 = maxA, Kademe = int.MinValue });
            else
            {
                serit[0].A0 = minA;
                serit[serit.Count - 1].A1 = maxA;
                for (int i = 1; i < serit.Count; i++)
                    serit[i - 1].A1 = serit[i].A0;
            }
            LejantGenislet(serit);

            foreach (LejantDilim r in serit)
            {
                if (r.A1 - r.A0 < MinKutuCm) continue;
                if (yataySerit)
                    sonuc.Add(new[] { minB, r.A0, maxB, r.A1 });
                else
                    sonuc.Add(new[] { r.A0, minB, r.A1, maxB });
                kademeOut?.Add(r.Kademe);
            }
            if (sonuc.Count == 0)
            {
                sonuc.Add(new[] { minX, minY, maxX, maxY });
                kademeOut?.Add(int.MinValue);
            }
            return sonuc;
        }

        public sealed class LejantDilim
        {
            public double A0;
            public double A1;
            public double Alan;
            public int Kademe;
            public int AralikCm;
        }

        /// <summary>Dilim genişliği en az bu kadar cm (sonra aralığın tam katına tamamlanır). Komut formu yazar.</summary>
        public static double MinDilimGenislikCm = 120.0;

        /// <summary>
        /// En az formdaki genişlik, ısı aralığının içinde komşu yutarak sağlanır; dışarı taşmaz.
        /// Genişlik her zaman aralığın tam katıdır. En büyük As iki yana eşit büyür.
        /// </summary>
        public static void LejantGenislet(List<LejantDilim> dilimler)
        {
            if (dilimler == null || dilimler.Count == 0) return;
            if (dilimler.Count >= 2)
            {
                List<int> sira = KademeSirasi(dilimler);
                int enBuyuk = sira.Count > 0 ? sira[0] : int.MinValue;
                foreach (int kademe in sira)
                {
                    int tur = 0;
                    bool oldu = true;
                    while (oldu && tur++ < 40)
                    {
                        oldu = false;
                        for (int i = 0; i < dilimler.Count; i++)
                        {
                            if (dilimler[i].Kademe != kademe) continue;
                            double hedef = MinAralik(dilimler[i]);
                            if (hedef < 1.0 || dilimler[i].A1 - dilimler[i].A0 >= hedef - 0.5) continue;
                            if (!BirAdimBuyut(dilimler, i, hedef, kademe == enBuyuk)) continue;
                            AyniKademeyiBirlestir(dilimler);
                            oldu = true;
                            break;
                        }
                    }
                }

                int em = 0;
                bool yut = true;
                while (yut && em++ < dilimler.Count + 2)
                {
                    yut = false;
                    for (int i = 0; i < dilimler.Count; i++)
                    {
                        double hedef = MinAralik(dilimler[i]);
                        if (hedef < 1.0 || dilimler[i].A1 - dilimler[i].A0 >= hedef - 0.5) continue;
                        int sol = i - 1;
                        int sag = i + 1;
                        bool solBuyuk = sol >= 0 && Kiyas(dilimler[sol], dilimler[i]) > 0;
                        bool sagBuyuk = sag < dilimler.Count && Kiyas(dilimler[sag], dilimler[i]) > 0;
                        if (!solBuyuk && !sagBuyuk) continue;
                        int alici = solBuyuk && sagBuyuk
                            ? (Kiyas(dilimler[sol], dilimler[sag]) >= 0 ? sol : sag)
                            : (solBuyuk ? sol : sag);
                        Yut(dilimler, alici, i);
                        AyniKademeyiBirlestir(dilimler);
                        yut = true;
                        break;
                    }
                }
            }
            AralikKatinaTamamla(dilimler);
        }

        private static double MinAralik(LejantDilim dilim)
        {
            return dilim.AralikCm > 0 ? KatGenislik(MinDilimGenislikCm, dilim.AralikCm) : 0;
        }

        /// <summary>Mevcut boyun aralık katı. 179.9985 ve 20 cm → 180. Sekiz adet bu hedefe eklenmez.</summary>
        private static double HedefKat(LejantDilim dilim)
        {
            return KatGenislik(dilim.A1 - dilim.A0, dilim.AralikCm);
        }

        private static double KatGenislik(double boy, int adim)
        {
            if (adim <= 0) return 0;
            if (boy < 0) boy = 0;
            int kat = (int)Math.Ceiling((boy - 0.5) / adim);
            if (kat < 1) kat = 1;
            return kat * (double)adim;
        }

        /// <summary>Eksik kat dış uçtan tamamlanır. İç dilim komşuya eşit bölünür; komşu kendi katını yine dıştan alır.</summary>
        private static void AralikKatinaTamamla(List<LejantDilim> dilimler)
        {
            var hedef = new double[dilimler.Count];
            for (int i = 0; i < dilimler.Count; i++)
                hedef[i] = HedefKat(dilimler[i]);
            for (int tur = 0; tur < 24; tur++)
            {
                bool oldu = false;
                for (int i = 0; i < dilimler.Count && i < hedef.Length; i++)
                {
                    double boy = dilimler[i].A1 - dilimler[i].A0;
                    double fark = hedef[i] - boy;
                    if (hedef[i] < 1.0 || Math.Abs(fark) < 0.0001) continue;
                    int n = dilimler.Count;
                    if (n == 1)
                    {
                        double orta = (dilimler[i].A0 + dilimler[i].A1) * 0.5;
                        dilimler[i].A0 = orta - hedef[i] * 0.5;
                        dilimler[i].A1 = dilimler[i].A0 + hedef[i];
                        oldu = true;
                        continue;
                    }
                    if (i == 0)
                    {
                        dilimler[i].A0 = dilimler[i].A1 - hedef[i];
                        oldu = true;
                        continue;
                    }
                    if (i == n - 1)
                    {
                        dilimler[i].A1 = dilimler[i].A0 + hedef[i];
                        oldu = true;
                        continue;
                    }
                    double sol = fark * 0.5;
                    double sag = fark - sol;
                    if (fark > 0)
                    {
                        double solGen = dilimler[i - 1].A1 - dilimler[i - 1].A0;
                        double sagGen = dilimler[i + 1].A1 - dilimler[i + 1].A0;
                        if (sol > solGen - 1.0) sol = Math.Max(0, solGen - 1.0);
                        if (sag > sagGen - 1.0) sag = Math.Max(0, sagGen - 1.0);
                    }
                    if (Math.Abs(sol) + Math.Abs(sag) < 0.0001) continue;
                    dilimler[i].A0 -= sol;
                    dilimler[i - 1].A1 = dilimler[i].A0;
                    dilimler[i].A1 += sag;
                    dilimler[i + 1].A0 = dilimler[i].A1;
                    oldu = true;
                }
                if (!oldu) break;
            }
        }

        private static List<int> KademeSirasi(List<LejantDilim> dilimler)
        {
            var enBuyuk = new Dictionary<int, LejantDilim>();
            foreach (LejantDilim dilim in dilimler)
            {
                LejantDilim eski;
                if (!enBuyuk.TryGetValue(dilim.Kademe, out eski) || Kiyas(dilim, eski) > 0)
                    enBuyuk[dilim.Kademe] = dilim;
            }
            var liste = new List<LejantDilim>(enBuyuk.Values);
            liste.Sort(delegate (LejantDilim a, LejantDilim b) { return Kiyas(b, a); });
            var sira = new List<int>(liste.Count);
            foreach (LejantDilim dilim in liste)
                sira.Add(dilim.Kademe);
            return sira;
        }

        private static int Kiyas(LejantDilim a, LejantDilim b)
        {
            int alan = a.Alan.CompareTo(b.Alan);
            if (alan != 0) return alan;
            return a.Kademe.CompareTo(b.Kademe);
        }

        private static bool BirAdimBuyut(List<LejantDilim> dilimler, int i, double hedef, bool esitIkiYan)
        {
            double ihtiyac = hedef - (dilimler[i].A1 - dilimler[i].A0);
            if (ihtiyac < 0.5) return false;
            int sol = i - 1;
            int sag = i + 1;
            bool solKucuk = sol >= 0 && Kiyas(dilimler[sol], dilimler[i]) < 0;
            bool sagKucuk = sag < dilimler.Count && Kiyas(dilimler[sag], dilimler[i]) < 0;
            if (!solKucuk && !sagKucuk) return false;
            double solPay = 0;
            double sagPay = 0;
            if (solKucuk && sagKucuk && esitIkiYan)
            {
                solPay = ihtiyac * 0.5;
                sagPay = ihtiyac - solPay;
            }
            else if (solKucuk && sagKucuk)
            {
                int k = Kiyas(dilimler[sol], dilimler[sag]);
                if (k < 0) solPay = ihtiyac;
                else if (k > 0) sagPay = ihtiyac;
                else
                {
                    solPay = ihtiyac * 0.5;
                    sagPay = ihtiyac - solPay;
                }
            }
            else if (solKucuk)
                solPay = ihtiyac;
            else
                sagPay = ihtiyac;

            bool oldu = false;
            if (sagPay > 0.5)
            {
                double gen = dilimler[sag].A1 - dilimler[sag].A0;
                double al = Math.Min(sagPay, gen);
                LejantDilim sahip = dilimler[i];
                LejantDilim kaynak = dilimler[sag];
                kaynak.A0 += al;
                sahip.A1 += al;
                dilimler[i] = sahip;
                dilimler[sag] = kaynak;
                if (kaynak.A1 - kaynak.A0 < 0.5) dilimler.RemoveAt(sag);
                oldu = true;
            }
            if (solPay > 0.5 && sol >= 0 && sol < dilimler.Count && Kiyas(dilimler[sol], dilimler[i]) < 0)
            {
                double gen = dilimler[sol].A1 - dilimler[sol].A0;
                double al = Math.Min(solPay, gen);
                LejantDilim sahip = dilimler[i];
                LejantDilim kaynak = dilimler[sol];
                kaynak.A1 -= al;
                sahip.A0 -= al;
                dilimler[i] = sahip;
                dilimler[sol] = kaynak;
                if (kaynak.A1 - kaynak.A0 < 0.5) dilimler.RemoveAt(sol);
                oldu = true;
            }
            return oldu;
        }

        private static void Yut(List<LejantDilim> dilimler, int buyuk, int kucuk)
        {
            LejantDilim alan = dilimler[buyuk];
            LejantDilim parca = dilimler[kucuk];
            if (parca.A0 < alan.A0) alan.A0 = parca.A0;
            if (parca.A1 > alan.A1) alan.A1 = parca.A1;
            dilimler[buyuk] = alan;
            dilimler.RemoveAt(kucuk);
        }

        private static void AyniKademeyiBirlestir(List<LejantDilim> dilimler)
        {
            for (int i = 0; i < dilimler.Count - 1;)
            {
                if (dilimler[i].Kademe == dilimler[i + 1].Kademe && dilimler[i + 1].A0 - dilimler[i].A1 < 1.0)
                {
                    dilimler[i].A1 = dilimler[i + 1].A1;
                    dilimler.RemoveAt(i + 1);
                    continue;
                }
                i++;
            }
        }

        /// <summary>Donatı boyu yönü. Isıyı örten en küçük boy; iki yana eşit pay.</summary>
        public static readonly double[] StandartBoyCm = { 200, 240, 300, 350, 400, 450, 500, 600, 700, 800, 900, 1000, 1200 };

        /// <summary>
        /// Isı aralığını standart boya büyütür veya mevcut zarfı bu boya indirir.
        /// Pay iki yana eşittir; paspay sınırına dayanan yanın payı karşı yana geçer.
        /// Paspayı aşan ısı kısmı sınırda kesilir. Boy paspay penceresine sığmazsa uydurulmaz.
        /// </summary>
        public static bool StandartBoyUydur(
            double isi0,
            double isi1,
            double sinir0,
            double sinir1,
            out double b0,
            out double b1)
        {
            b0 = isi0;
            b1 = isi1;
            double i0 = Math.Max(isi0, sinir0);
            double i1 = Math.Min(isi1, sinir1);
            double boy = i1 - i0;
            if (boy < 1.0) return false;
            double pencere = sinir1 - sinir0;
            foreach (double s in StandartBoyCm)
            {
                if (boy > s + 0.5) continue;
                if (s > pencere + 0.05) break;
                double pad = (s - boy) * 0.5;
                double u0 = i0 - pad;
                double u1 = i1 + pad;
                if (u0 < sinir0)
                {
                    u1 += sinir0 - u0;
                    u0 = sinir0;
                }
                else if (u1 > sinir1)
                {
                    u0 -= u1 - sinir1;
                    u1 = sinir1;
                }
                b0 = u0;
                b1 = u1;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Isı kenarı payı. StandartBoyUydur ısıdan 0.5 cm'e kadar kısa boyu kabul eder (yana 0.25);
        /// hiza aynı payı tanımazsa ısısı boyu tam dolduran kutu kendi yerine bile hizalanamaz.
        /// </summary>
        public const double IsiPayCm = 0.3;

        /// <summary>Hiza aralığı dilimin ısısını örtüyorsa uygulanabilir. Örtmezse kutu büyük ve hizasız kalır.</summary>
        public static bool HizaIsiyiOrter(double hiza0, double hiza1, double isi0, double isi1)
        {
            if (isi1 - isi0 < 1.0) return true;
            double pay = IsiPayCm + 0.02;
            return hiza0 <= isi0 + pay && hiza1 >= isi1 - pay;
        }

        /// <summary>Düşey boylar bu kadar yakınsa en büyük As zarfının düşeyine çekilir. Form yazar.</summary>
        public static bool DuseyBoyYakin(double boy, double referans, double farkCm)
        {
            if (farkCm < 0 || referans < 1.0 || boy < 1.0) return false;
            return Math.Abs(boy - referans) <= farkCm + 0.01;
        }

        /// <summary>Bir dilimin kendi standart kutusu, paspayda kırpılmış ısısı ve paspay sınırları.</summary>
        public struct DuseyAday
        {
            public double Y0;
            public double Y1;
            public bool IsiVar;
            public double Isi0;
            public double Isi1;
            public double Sinir0;
            public double Sinir1;
        }

        /// <summary>Kutu bu hizaya çekilebilir mi: boy yakın, ısıyı örter, paspaya girmez.</summary>
        public static bool DuseyHizaUyar(DuseyAday kutu, double h0, double h1, double farkCm)
        {
            if (h1 - h0 < 1.0) return false;
            if (!DuseyBoyYakin(kutu.Y1 - kutu.Y0, h1 - h0, farkCm)) return false;
            if (kutu.IsiVar && !HizaIsiyiOrter(h0, h1, kutu.Isi0, kutu.Isi1)) return false;
            return h0 >= kutu.Sinir0 - 0.05 && h1 <= kutu.Sinir1 + 0.05;
        }

        /// <summary>
        /// Düşey hiza: her kutu boyu aday boydur. O boyda kutunun ısısını örtüp paspaya girmeyen
        /// başlangıç aralığı [ısı1-L, ısı0] ∩ [sınır0, sınır1-L]'dir. En çok kutunun ortak noktası seçilir;
        /// bu nokta hiçbir kutunun kendi konumu olmayabilir. Eşitlikte büyük donatılı referansın boyu ve
        /// ona en yakın konum kalır.
        /// </summary>
        public static bool DuseyHizaBul(IReadOnlyList<DuseyAday> adaylar, int referans, double farkCm, out double h0, out double h1)
        {
            h0 = 0;
            h1 = 0;
            if (adaylar == null || adaylar.Count == 0) return false;
            if (referans < 0 || referans >= adaylar.Count) referans = 0;
            DuseyAday refKutu = adaylar[referans];
            double refBoy = refKutu.Y1 - refKutu.Y0;
            int enIyiSayi = -1;
            double enIyiFark = double.MaxValue;
            bool enIyiRefBoy = false;
            bool bulundu = false;
            var lo = new List<double>();
            var hi = new List<double>();
            foreach (DuseyAday boyAday in adaylar)
            {
                double boy = boyAday.Y1 - boyAday.Y0;
                if (boy < 1.0) continue;
                bool refBoyu = Math.Abs(boy - refBoy) < 0.05;
                lo.Clear();
                hi.Clear();
                foreach (DuseyAday kutu in adaylar)
                {
                    if (!DuseyBoyYakin(kutu.Y1 - kutu.Y0, boy, farkCm)) continue;
                    double a = kutu.Sinir0;
                    double b = kutu.Sinir1 - boy;
                    if (kutu.IsiVar)
                    {
                        a = Math.Max(a, kutu.Isi1 - boy - IsiPayCm);
                        b = Math.Min(b, kutu.Isi0 + IsiPayCm);
                    }
                    if (a > b + 0.04) continue;
                    lo.Add(a);
                    hi.Add(Math.Max(a, b));
                }
                if (lo.Count == 0) continue;
                // Tercih: referans kutunun kendi başlangıcı (aynı boyda), değilse ilk kutunun.
                double tercih = refBoyu ? refKutu.Y0 : boyAday.Y0;
                var noktalar = new List<double> { tercih };
                for (int i = 0; i < lo.Count; i++)
                {
                    if (!double.IsInfinity(lo[i])) noktalar.Add(lo[i]);
                    if (!double.IsInfinity(hi[i])) noktalar.Add(hi[i]);
                }
                foreach (double p in noktalar)
                {
                    int sayi = 0;
                    double bolge0 = double.NegativeInfinity;
                    double bolge1 = double.PositiveInfinity;
                    for (int i = 0; i < lo.Count; i++)
                    {
                        if (p < lo[i] - 0.04 || p > hi[i] + 0.04) continue;
                        sayi++;
                        if (lo[i] > bolge0) bolge0 = lo[i];
                        if (hi[i] < bolge1) bolge1 = hi[i];
                    }
                    if (sayi == 0) continue;
                    if (bolge1 < bolge0) bolge1 = bolge0;
                    double nokta = tercih < bolge0 ? bolge0 : (tercih > bolge1 ? bolge1 : tercih);
                    double fark = Math.Abs(nokta - tercih);
                    bool daha = sayi > enIyiSayi
                        || (sayi == enIyiSayi && refBoyu && !enIyiRefBoy)
                        || (sayi == enIyiSayi && refBoyu == enIyiRefBoy && fark < enIyiFark - 0.01);
                    if (!daha) continue;
                    enIyiSayi = sayi;
                    enIyiRefBoy = refBoyu;
                    enIyiFark = fark;
                    h0 = nokta;
                    h1 = nokta + boy;
                    bulundu = true;
                }
            }
            return bulundu;
        }

        private static bool ZarfKesisiyor(Envelope a, Envelope b)
        {
            double ix = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
            double iy = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY);
            return ix > 1.0 && iy > 1.0;
        }

        private static int Kok(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }
    }
}
