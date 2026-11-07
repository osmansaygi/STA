using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// GPR'siz radye düğüm As hesabı (mod 4). Girdi: RAD (ağ, kalınlık, ρmin), SO1 (eleman köşe Mx/My, genel donatı,
    /// yön tavanı), ST4 /Materials/ (fck, fyk). Ayrıntı ve doğrulama: tools/RadyeAsGprsiz20261028/README.md.
    /// Düğüm momenti: eleman köşe ortalaması (üçgende K köşesi Mx, J köşesi My katkısı 0, bölen sabit) →
    /// TS500 zarfı → kendisi + kenar komşuları ortalaması. STA'nın sınır/üçgen kuralı belirsiz olduğundan
    /// olası komşu kümelerinin zarfı alınır (alt için en büyük, üst için en küçük).
    /// </summary>
    internal static class RadyeAsHesap
    {
        // TS500T.COD betonarme kombinasyonları: Cg, Cq, Cs (zemin itkisi), Ce (±deprem), Cw (±rüzgar).
        private static readonly double[][] Kombinasyon =
        {
            new[] { 1.4, 1.6, 0.0, 0.0, 0.0 }, new[] { 1.4, 1.6, 1.6, 0.0, 0.0 }, new[] { 1.0, 1.2, 0.0, 0.0, 0.0 },
            new[] { 1.0, 1.0, 0.0, 1.0, 0.0 }, new[] { 1.0, 1.0, 1.0, 1.0, 0.0 }, new[] { 0.9, 0.0, 0.0, 1.0, 0.0 },
            new[] { 1.0, 1.3, 0.0, 0.0, 1.3 }, new[] { 1.0, 1.3, 1.0, 0.0, 1.3 }, new[] { 0.9, 0.0, 0.0, 0.0, 1.3 },
            new[] { 0.9, 0.0, 0.9, 0.0, 1.3 }
        };

        // SO1 yük satırları (0 tabanlı): 1 G, 2–7 Q1..Q6, 8 zemin itkisi, 9–12 deprem, 13–14 rüzgar, 17 Z deprem.
        private static readonly int[] YukSatiri = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 16 };
        private const int Satir = 21, Sutun = 33;
        private const double SinirOrani = 0.0032858;

        internal static bool TryHesapla(string st4Path, out List<GprRadyeDugumAs> dugumler, out GprRadyeGenelDonati genel, out string hata)
        {
            dugumler = null;
            genel = null;
            hata = null;
            try
            {
                return Hesapla(st4Path, out dugumler, out genel, out hata);
            }
            catch (Exception ex)
            {
                hata = ex.Message;
                return false;
            }
        }

        private static bool Hesapla(string st4Path, out List<GprRadyeDugumAs> dugumler, out GprRadyeGenelDonati genel, out string hata)
        {
            dugumler = null;
            genel = null;
            hata = null;
            string rad = Yan(st4Path, ".RAD"), so1 = Yan(st4Path, ".SO1");
            if (rad == null) { hata = "RAD yok."; return false; }
            if (so1 == null) { hata = "SO1 yok."; return false; }
            if (!TryMalzeme(st4Path, out double fck, out double fyk)) { hata = "ST4 /Materials/ fck/fyk okunamadi."; return false; }

            WindowsAnsiEncodings.EnsureRegistered();
            string[] L = WindowsAnsiEncodings.ReadAllLines(rad);
            if (L.Length < 8) { hata = "RAD kisa."; return false; }
            string[] h = L[1].Split(',');
            int nPlak = int.Parse(h[0], CultureInfo.InvariantCulture);
            int nNode = int.Parse(h[1], CultureInfo.InvariantCulture);
            int nMesh = int.Parse(h[2], CultureInfo.InvariantCulture);
            double rhoMin = Sayi(L[3].Split(',')[0]);
            int li = 7 + 2 * nPlak;
            if (li + nMesh + nNode > L.Length) { hata = "RAD eleman/dugum tablosu kisa."; return false; }

            var mesh = new int[nMesh][];
            var kalinlikCm = new double[nMesh];
            for (int m = 0; m < nMesh; m++, li++)
            {
                string[] f = L[li].Split(',');
                mesh[m] = new[] { int.Parse(f[1], CultureInfo.InvariantCulture), int.Parse(f[2], CultureInfo.InvariantCulture), int.Parse(f[3], CultureInfo.InvariantCulture), int.Parse(f[4], CultureInfo.InvariantCulture) };
                kalinlikCm[m] = Sayi(f[6]) * 100.0;
            }
            var x = new double[nNode + 1]; var y = new double[nNode + 1]; var z = new double[nNode + 1];
            for (int n = 1; n <= nNode; n++, li++)
            {
                string[] f = L[li].Split(',');
                x[n] = Sayi(f[1]); y[n] = Sayi(f[2]); z[n] = Sayi(f[3]);
            }

            byte[] so = File.ReadAllBytes(so1);
            if (so.Length < (long)nMesh * Satir * Sutun * 4) { hata = "SO1 eleman sayisi RAD ile uyusmuyor."; return false; }
            Func<int, float> bas = i => BitConverter.ToSingle(so, 4 * i);
            // SO1 başlığı: genel As X üst, X alt, Y üst, Y alt; ardından aynı sırada yön tavanı (hesaplanmışsa).
            double[] genelAs = { bas(2), bas(3), bas(4), bas(5) };
            double[] tavan = { bas(6), bas(7), bas(8), bas(9) };
            if (!TryGenel(genelAs, out genel)) { hata = "SO1 genel donati cap/aralik cozulemedi."; return false; }

            var hesap = new Hesap(mesh, so, fck, fyk, nNode);
            dugumler = new List<GprRadyeDugumAs>(nNode);
            // Dizi sırası GPR ile aynı: X alt, X üst, Y alt, Y üst. Tavan/genel sırası X üst, X alt, Y üst, Y alt.
            int[] tavanSirasi = { 1, 0, 3, 2 };
            for (int n = 1; n <= nNode; n++)
            {
                var d = new GprRadyeDugumAs { No = n, X = x[n], Y = y[n], Z = z[n] };
                if (hesap.Bagli(n))
                {
                    double hCm = hesap.Kalinlik(n, kalinlikCm);
                    var a = new double[4];
                    for (int i = 0; i < 4; i++)
                    {
                        double As = hesap.As(hesap.Mod4(n, i), hCm, rhoMin);
                        int t = tavanSirasi[i];
                        if (tavan[t] > 0) As = Math.Min(As, Math.Max(tavan[t], genelAs[t]));
                        a[i] = Math.Round(As, 2, MidpointRounding.AwayFromZero);
                    }
                    d.AsXAlt = a[0]; d.AsXUst = a[1]; d.AsYAlt = a[2]; d.AsYUst = a[3];
                }
                dugumler.Add(d);
            }
            return true;
        }

        private sealed class Hesap
        {
            private readonly int[][] mesh;
            private readonly byte[] so;
            private readonly double fck, fyk;
            private readonly List<KeyValuePair<int, int>>[] bag;
            private readonly Dictionary<long, int> kenar = new Dictionary<long, int>();
            private readonly Dictionary<int, double[]> sifirli = new Dictionary<int, double[]>();
            private readonly Dictionary<int, double[]> ham = new Dictionary<int, double[]>();
            private readonly List<double[]> katsayilar;

            internal Hesap(int[][] mesh, byte[] so, double fck, double fyk, int nNode)
            {
                this.mesh = mesh; this.so = so; this.fck = fck; this.fyk = fyk;
                bag = new List<KeyValuePair<int, int>>[nNode + 1];
                for (int i = 0; i <= nNode; i++) bag[i] = new List<KeyValuePair<int, int>>();
                for (int e = 0; e < mesh.Length; e++)
                {
                    for (int k = 0; k < 4; k++) if (mesh[e][k] > 0 && mesh[e][k] <= nNode) bag[mesh[e][k]].Add(new KeyValuePair<int, int>(e, k));
                    int[] nn = Kose(e);
                    for (int i = 0; i < nn.Length; i++)
                    {
                        long key = Anahtar(nn[i], nn[(i + 1) % nn.Length]);
                        kenar.TryGetValue(key, out int c);
                        kenar[key] = c + 1;
                    }
                }
                katsayilar = Kombinasyonlar();
            }

            internal bool Bagli(int n) => bag[n].Count > 0;

            internal double Kalinlik(int n, double[] kalinlikCm) => bag[n].Average(t => kalinlikCm[t.Key]);

            private int[] Kose(int e) => mesh[e].Where(v => v > 0).ToArray();

            private bool Ucgen(int e) => mesh[e][3] == 0;

            private static long Anahtar(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

            private int Kenar(int a, int b) => kenar.TryGetValue(Anahtar(a, b), out int c) ? c : 0;

            private float Deger(int e, int yuk, int kose, int bilesen) =>
                BitConverter.ToSingle(so, 4 * (e * Satir * Sutun + (1 + yuk) * Sutun + 1 + kose * 8 + bilesen));

            /// <summary>Düğüm statik momenti (15 yük): bağlı eleman köşelerinin ortalaması. Bileşen 3 = Mx, 4 = My.</summary>
            private double[] Statik(int n, int bilesen, bool ucgenSifir)
            {
                var r = new double[YukSatiri.Length];
                foreach (var t in bag[n])
                {
                    if (ucgenSifir && Ucgen(t.Key) && ((t.Value == 2 && bilesen == 3) || (t.Value == 1 && bilesen == 4))) continue;
                    for (int i = 0; i < YukSatiri.Length; i++) r[i] += Deger(t.Key, YukSatiri[i], t.Value, bilesen);
                }
                for (int i = 0; i < r.Length; i++) r[i] /= bag[n].Count;
                return r;
            }

            /// <summary>Kendi tasarım momenti: X alt, X üst, Y alt, Y üst (alt ≥ 0, üst ≤ 0).</summary>
            private double[] Tasarim(int n, bool ucgenSifir)
            {
                var cache = ucgenSifir ? sifirli : ham;
                if (cache.TryGetValue(n, out double[] v)) return v;
                double[] mx = Statik(n, 3, ucgenSifir), my = Statik(n, 4, ucgenSifir);
                double xa = 0, xu = 0, ya = 0, yu = 0;
                foreach (double[] c in katsayilar)
                {
                    double a = 0, b = 0;
                    for (int i = 0; i < c.Length; i++) { a += c[i] * mx[i]; b += c[i] * my[i]; }
                    xa = Math.Max(xa, a); xu = Math.Min(xu, a); ya = Math.Max(ya, b); yu = Math.Min(yu, b);
                }
                v = new[] { xa, xu, ya, yu };
                cache[n] = v;
                return v;
            }

            /// <summary>Komşu kümesi: 3 tüm kenarlar; 6 üçgende I–J, J–K; 7 üçgende yalnız K–I. Kendisi dahil, tekil.</summary>
            private List<int> Kume(int n, int tur)
            {
                var r = new List<int>();
                foreach (var t in bag[n])
                {
                    int[] nn = Kose(t.Key);
                    int c = nn.Length, k = Array.IndexOf(nn, n);
                    if (c == 3 && tur == 6) { if (k == 0) r.Add(nn[1]); else if (k == 1) { r.Add(nn[0]); r.Add(nn[2]); } else r.Add(nn[1]); }
                    else if (c == 3 && tur == 7) { if (k == 0) r.Add(nn[2]); else if (k == 2) r.Add(nn[0]); }
                    else { r.Add(nn[(k + 1) % c]); r.Add(nn[(k + c - 1) % c]); }
                }
                r = r.Distinct().Where(v => v != n).ToList();
                r.Add(n);
                return r;
            }

            private double Ortalama(List<int> kume, int i, bool ucgenSifir) => kume.Average(v => Tasarim(v, ucgenSifir)[i]);

            /// <summary>Mod 4 tasarım momenti (i: 0 X alt, 1 X üst, 2 Y alt, 3 Y üst).</summary>
            internal double Mod4(int n, int i)
            {
                var degerler = new List<double>();
                foreach (int tur in new[] { 3, 6, 7 })
                {
                    List<int> kume = Kume(n, tur);
                    degerler.Add(Ortalama(kume, i, true));
                    degerler.Add(Ortalama(kume, i, false));
                }
                if (bag[n].All(t => Ucgen(t.Key)))
                    degerler.Add(0.5 * (Tasarim(n, true)[i] + Ortalama(Kume(n, 6), i, true)));
                List<int> tum = Kume(n, 3);
                bool ucgenSinir = bag[n].Any(t => Ucgen(t.Key)) && tum.Any(v => v != n && Kenar(n, v) == 1);
                if (ucgenSinir)
                {
                    List<int> ic = tum.Where(v => v == n || Kenar(n, v) != 1).ToList();
                    if (ic.Count >= 2)
                    {
                        degerler.Add(Ortalama(ic, i, true));
                        degerler.Add(Ortalama(ic, i, false));
                    }
                }
                return i % 2 == 0 ? degerler.Max() : degerler.Min();
            }

            /// <summary>
            /// As (cm²/m): dikdörtgen blok 0.85·fck/1.5, d = h − 6.8, fy = fyk/1.5. Sınır 0.0032858·b·(h−7) aşılırsa
            /// max(sınır, fyk/1.15 ile As). Minimum max(ρmin, 0.001)·b·(h−7).
            /// </summary>
            internal double As(double momentTm, double hCm, double rhoMin)
            {
                if (Math.Abs(momentTm) < 1e-6) return 0;
                double M = Math.Abs(momentTm) * 1e5, d = hCm - 6.8, sinir = SinirOrani * 100.0 * (hCm - 7.0);
                double as1 = Blok(M, d, fyk / 1.5);
                double As = as1 <= sinir ? as1 : Math.Max(sinir, Blok(M, d, fyk / 1.15));
                return Math.Max(As, Math.Max(rhoMin, 0.001) * 100.0 * (hCm - 7.0));
            }

            private double Blok(double M, double d, double fy)
            {
                double A = 0.85 * fck / 1.5 * 100.0;
                double disc = d * d - 2.0 * M / A;
                double a = d - Math.Sqrt(Math.Max(0.0, disc));
                return A * a / fy;
            }

            private static List<double[]> Kombinasyonlar()
            {
                var eq = new List<double[]>();
                for (int j = 8; j <= 11; j++) foreach (int s in new[] { 1, -1 }) { var v = new double[15]; v[j] = s; eq.Add(v); }
                var w = new List<double[]>();
                foreach (int j in new[] { 12, 13 }) foreach (int s in new[] { 1, -1 }) { var v = new double[15]; v[j] = s; w.Add(v); }
                var sifir = new List<double[]> { new double[15] };
                var r = new List<double[]>();
                foreach (double[] c in Kombinasyon)
                    for (int q = 1; q <= 6; q++)
                        foreach (double[] e in c[3] != 0 ? eq : sifir)
                            foreach (double[] r2 in c[4] != 0 ? w : sifir)
                            {
                                var v = new double[15];
                                v[0] = c[0]; v[q] = c[1]; v[7] = c[2];
                                for (int i = 0; i < 15; i++) v[i] += c[3] * e[i] + c[4] * r2[i];
                                r.Add(v);
                            }
                return r;
            }
        }

        /// <summary>SO1 genel As (X üst, X alt, Y üst, Y alt) → çap/aralık; önce 20 cm aralık denenir.</summary>
        private static bool TryGenel(double[] alan, out GprRadyeGenelDonati genel)
        {
            genel = null;
            var cap = new int[4]; var aralik = new int[4];
            int[] caplar = { 8, 10, 12, 14, 16, 18, 20, 22, 24, 25, 26, 28, 30, 32 };
            var araliklar = new List<int> { 20 };
            for (int s = 10; s <= 35; s++) if (s != 20) araliklar.Add(s);
            for (int i = 0; i < 4; i++)
            {
                bool bulundu = false;
                foreach (int s in araliklar)
                {
                    foreach (int c in caplar)
                        if (Math.Abs(GprRadyeAyir.AlanCm2Pm(c, s) - alan[i]) <= 0.01) { cap[i] = c; aralik[i] = s; bulundu = true; break; }
                    if (bulundu) break;
                }
                if (!bulundu) return false;
            }
            genel = new GprRadyeGenelDonati
            {
                CapXUstMm = cap[0], AralikXUstCm = aralik[0], AsXUst = alan[0],
                CapXAltMm = cap[1], AralikXAltCm = aralik[1], AsXAlt = alan[1],
                CapYUstMm = cap[2], AralikYUstCm = aralik[2], AsYUst = alan[2],
                CapYAltMm = cap[3], AralikYAltCm = aralik[3], AsYAlt = alan[3]
            };
            return true;
        }

        /// <summary>ST4 /Materials/ ilk satırı: fck, fyk (kg/cm²).</summary>
        private static bool TryMalzeme(string st4Path, out double fck, out double fyk)
        {
            fck = 0; fyk = 0;
            WindowsAnsiEncodings.EnsureRegistered();
            string[] lines = WindowsAnsiEncodings.ReadAllLines(st4Path);
            for (int i = 0; i + 1 < lines.Length; i++)
            {
                if ((lines[i] ?? string.Empty).IndexOf("/Materials/", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string[] f = lines[i + 1].Split(',');
                if (f.Length < 2) return false;
                fck = Sayi(f[0]); fyk = Sayi(f[1]);
                return fck > 100 && fck < 1000 && fyk > 1000 && fyk < 10000;
            }
            return false;
        }

        private static string Yan(string st4Path, string uzanti)
        {
            string p = Path.ChangeExtension(st4Path, uzanti);
            if (File.Exists(p)) return p;
            p = Path.ChangeExtension(st4Path, uzanti.ToLowerInvariant());
            return File.Exists(p) ? p : null;
        }

        private static double Sayi(string s) => double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
