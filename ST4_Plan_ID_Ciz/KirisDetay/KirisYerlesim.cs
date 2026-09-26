using System;
using System.Collections.Generic;

namespace ST4PlanIdCiz.KirisDetay
{
    /// <summary>
    /// Kesitte çubuk yerleşimi. R-SP-01…05.
    /// Köşeler sürekli çubuktur (üstte montaj, altta düz). İlave önce 1. sıradaki boşluğa, sığmazsa 2. sıraya aynı düşey eksene.
    /// </summary>
    public static class KirisYerlesim
    {
        sealed class Birim
        {
            public DonatiRolu Rol;
            public double Cap;
            public bool Surekli;
            public double Alan;
        }

        public static YuzYerlesim Yerlestir(
            IList<DonatiGrubu> gruplar,
            IList<DonatiRolu> roller,
            IList<bool> surekli,
            double bwMm,
            double hMm,
            double ccMm,
            double phiWMm,
            double dmaxMm,
            double sMinMutlakMm,
            bool koseDuzelt,
            double koseIcCapCarpan,
            bool ustYuz)
        {
            var yuz = new YuzYerlesim();
            var birimler = new List<Birim>();
            double phiMax = 0;
            for (int g = 0; g < gruplar.Count; g++)
            {
                DonatiGrubu gr = gruplar[g];
                if (gr == null || !gr.Var) continue;
                double cap = gr.GeometrikCapMm;
                if (cap > phiMax) phiMax = cap;
                double alan = gr.Demet * KirisFormuller.TekCubukAlani(gr.CapMm);
                for (int i = 0; i < gr.Adet; i++)
                {
                    birimler.Add(new Birim
                    {
                        Rol = roller[g],
                        Cap = cap,
                        Surekli = surekli[g],
                        Alan = alan
                    });
                }
            }

            double bavNominal = KirisFormuller.Bav(bwMm, ccMm, phiWMm);
            yuz.SMinMm = phiMax > 0
                ? KirisFormuller.SMin(phiMax, dmaxMm, sMinMutlakMm)
                : sMinMutlakMm;
            yuz.SvMm = yuz.SMinMm;

            double extra = 0;
            if (koseDuzelt && phiMax > 0)
            {
                double inset = KirisFormuller.KoseEksenIcYuzden(phiMax, phiWMm, koseIcCapCarpan);
                extra = Math.Max(0, inset - phiMax / 2.0);
            }
            double bav = Math.Max(0, bavNominal - 2.0 * extra);
            yuz.BavMm = bav;
            yuz.NMax = phiMax > 0 ? KirisFormuller.NMax(bav, phiMax, yuz.SMinMm) : 0;

            if (birimler.Count == 0)
            {
                yuz.DMm = hMm - (ccMm + phiWMm);
                yuz.YsMm = ccMm + phiWMm;
                return yuz;
            }

            birimler.Sort(Karsilastir);

            int kapasite = Math.Min(birimler.Count, Math.Max(yuz.NMax, 1));
            Birim[] satir1 = null;
            while (kapasite >= 1)
            {
                Birim[] deneme = Ata(birimler, kapasite);
                if (deneme != null && Sigar(deneme, bav, yuz.SMinMm))
                {
                    satir1 = deneme;
                    break;
                }
                kapasite--;
            }

            if (satir1 == null)
            {
                yuz.Sigmadi = true;
                yuz.SiraSayisi = 0;
                return yuz;
            }

            var kalan = new List<Birim>(birimler);
            for (int i = 0; i < satir1.Length; i++) kalan.Remove(satir1[i]);

            var satirlar = new List<Birim[]> { satir1 };
            while (kalan.Count > 0)
            {
                int n = Math.Min(kalan.Count, satir1.Length);
                var parca = new Birim[n];
                for (int i = 0; i < n; i++) parca[i] = kalan[i];
                if (!Sigar(parca, bav, yuz.SMinMm) && n > 1)
                {
                    // Aynı eksene geldikleri için aralık genelde rahattır; sığmıyorsa adedi düşür.
                    while (n > 1 && !Sigar(parca, bav, yuz.SMinMm))
                    {
                        n--;
                        parca = new Birim[n];
                        for (int i = 0; i < n; i++) parca[i] = kalan[i];
                    }
                }
                satirlar.Add(parca);
                for (int i = 0; i < n; i++) kalan.RemoveAt(0);
                if (satirlar.Count > 6) break;
            }
            if (kalan.Count > 0) yuz.Sigmadi = true;

            yuz.SiraSayisi = satirlar.Count;
            double[] x1 = XKonumlari(satir1, bwMm, ccMm, phiWMm, bav, extra);
            double toplamCap = 0;
            for (int i = 0; i < satir1.Length; i++) toplamCap += satir1[i].Cap;
            double sNet = KirisFormuller.SNet(bav, toplamCap, satir1.Length);
            yuz.SNetVar = satir1.Length > 1 && !double.IsInfinity(sNet);
            yuz.SNetMm = yuz.SNetVar ? sNet : 0;

            double alanToplam = 0;
            double moment = 0;
            for (int s = 0; s < satirlar.Count; s++)
            {
                Birim[] satir = satirlar[s];
                int[] slotlar = s == 0 ? null : IlkN(MerkezdenDisa(satir1.Length), satir.Length);
                for (int i = 0; i < satir.Length; i++)
                {
                    int slot = s == 0 ? i : slotlar[i];
                    double x = x1[slot];
                    double y = ccMm + phiWMm + satir[i].Cap / 2.0;
                    if (s > 0)
                    {
                        double yOnce = 0;
                        double capOnce = 0;
                        for (int k = 0; k < yuz.Cubuklar.Count; k++)
                        {
                            if (yuz.Cubuklar[k].Sira != s) continue;
                            if (Math.Abs(yuz.Cubuklar[k].XMm - x) > 0.05) continue;
                            yOnce = yuz.Cubuklar[k].YYuzdenMm;
                            capOnce = yuz.Cubuklar[k].CapMm;
                        }
                        // TS500 9.5.2: üst sıra, alttaki çubukla aynı düşey eksende; net açıklık sv.
                        y = yOnce + capOnce / 2.0 + yuz.SvMm + satir[i].Cap / 2.0;
                    }

                    var cubuk = new YerlesenCubuk
                    {
                        Rol = satir[i].Rol,
                        Sira = s + 1,
                        CapMm = satir[i].Cap,
                        XMm = x,
                        YYuzdenMm = y,
                        Kose = s == 0 && (i == 0 || i == satir.Length - 1),
                        Surekli = satir[i].Surekli,
                        BirimAlanMm2 = satir[i].Alan
                    };
                    cubuk.YAlttanMm = ustYuz ? hMm - y : y;
                    yuz.Cubuklar.Add(cubuk);
                    alanToplam += satir[i].Alan;
                    moment += satir[i].Alan * y;
                }
            }

            yuz.YsMm = alanToplam > 0 ? moment / alanToplam : ccMm + phiWMm + phiMax / 2.0;
            yuz.DMm = hMm - yuz.YsMm;
            return yuz;
        }

        static int Karsilastir(Birim a, Birim b)
        {
            if (a.Surekli != b.Surekli) return a.Surekli ? -1 : 1;
            return b.Cap.CompareTo(a.Cap);
        }

        static bool Sigar(Birim[] slots, double bav, double sMin)
        {
            if (slots == null || slots.Length == 0) return true;
            double sum = 0;
            for (int i = 0; i < slots.Length; i++) sum += slots[i].Cap;
            double ihtiyac = sum + (slots.Length - 1) * sMin;
            return ihtiyac <= bav + 1e-6;
        }

        static Birim[] Ata(IList<Birim> kaynak, int n)
        {
            var cont = new List<Birim>();
            var ilave = new List<Birim>();
            for (int i = 0; i < kaynak.Count; i++)
            {
                if (kaynak[i].Surekli) cont.Add(kaynak[i]);
                else ilave.Add(kaynak[i]);
            }
            var slots = new Birim[n];
            if (n >= 1 && cont.Count > 0)
            {
                slots[0] = cont[0];
                cont.RemoveAt(0);
            }
            if (n >= 2 && cont.Count > 0)
            {
                slots[n - 1] = cont[0];
                cont.RemoveAt(0);
            }
            int[] merkez = MerkezdenDisa(n);
            for (int k = 0; k < merkez.Length && cont.Count > 0; k++)
            {
                int idx = merkez[k];
                if (slots[idx] != null) continue;
                slots[idx] = cont[0];
                cont.RemoveAt(0);
            }
            int[] dis = DisaridanIce(n);
            for (int k = 0; k < dis.Length && ilave.Count > 0; k++)
            {
                int idx = dis[k];
                if (slots[idx] != null) continue;
                slots[idx] = ilave[0];
                ilave.RemoveAt(0);
            }
            for (int i = 0; i < n; i++)
            {
                if (slots[i] != null) continue;
                if (cont.Count > 0)
                {
                    slots[i] = cont[0];
                    cont.RemoveAt(0);
                }
                else if (ilave.Count > 0)
                {
                    slots[i] = ilave[0];
                    ilave.RemoveAt(0);
                }
                else return null;
            }
            return slots;
        }

        public static int[] MerkezdenDisa(int n)
        {
            var order = new List<int>();
            if (n <= 0) return order.ToArray();
            if (n % 2 == 1)
            {
                int m = n / 2;
                order.Add(m);
                for (int k = 1; k <= m; k++)
                {
                    order.Add(m - k);
                    order.Add(m + k);
                }
            }
            else
            {
                int r = n / 2;
                int l = r - 1;
                for (int k = 0; k < n; k++)
                {
                    int a = l - k;
                    int b = r + k;
                    if (a >= 0) order.Add(a);
                    if (b < n) order.Add(b);
                    if (a < 0 && b >= n) break;
                }
            }
            return order.ToArray();
        }

        public static int[] DisaridanIce(int n)
        {
            var order = new List<int>();
            int l = 0;
            int r = n - 1;
            while (l <= r)
            {
                order.Add(l);
                if (r != l) order.Add(r);
                l++;
                r--;
            }
            return order.ToArray();
        }

        static int[] IlkN(int[] kaynak, int n)
        {
            var d = new int[n];
            for (int i = 0; i < n; i++) d[i] = kaynak[i];
            return d;
        }

        static double[] XKonumlari(Birim[] slots, double bw, double cc, double phiW, double bav, double extra)
        {
            int n = slots.Length;
            var x = new double[n];
            if (n == 1)
            {
                x[0] = bw / 2.0;
                return x;
            }
            double sum = 0;
            for (int i = 0; i < n; i++) sum += slots[i].Cap;
            double sNet = (bav - sum) / (n - 1);
            double cursor = cc + phiW + extra + slots[0].Cap / 2.0;
            for (int i = 0; i < n; i++)
            {
                x[i] = cursor;
                if (i < n - 1)
                    cursor += slots[i].Cap / 2.0 + sNet + slots[i + 1].Cap / 2.0;
            }
            return x;
        }
    }
}
