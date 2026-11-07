using System.Globalization;
using StatikRaporClaude.Dizgi;

namespace StatikRaporClaude.Veri
{
    /// <summary>
    /// Radye temel tabloları GPR'siz, STA4 radye analiz dosyalarından:
    /// .RAD (ağ: 2. satır plak sayısı, düğüm sayısı, eleman sayısı, Ko; düğüm satırları "plak, x, y, z, bayrak, yay")
    /// ve .DE1 (düğüm kaydı 40 + 588·(n−1), yük durumu başına 28 bayt, ilk float düşey deplasman, m, işaret ters).
    /// Tabloda yalnız yayı olan (zemine oturan) düğümler basılır. N_ULK_02: 516 düğüm, deplasman ve zemin gerilmesi
    /// hücreleri GPR ile 7740/7740, max sütunu 516/516 aynı.
    /// </summary>
    public sealed class RadyeVerisi
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        /// <summary>GPR sütunları: yük durumu numarası, kısa ad, DE1 vaka indisi.</summary>
        public static readonly (string No, string Ad, int Vaka)[] Vakalar =
        {
            ("1", "g", 0), ("2", "q", 1), ("3", "q", 2), ("4", "q", 3), ("5", "q", 4), ("6", "q", 5), ("7", "q", 6), ("8", "s", 7),
            ("9", "e", 8), ("10", "e", 9), ("11", "e", 10), ("12", "e", 11), ("13", "w", 12), ("14", "w", 13), ("17", "Ez", 16),
        };

        public double Ko, Qt;
        public readonly List<int> Dugumler = new List<int>();   // yaylı düğümler (1 tabanlı)
        /// <summary>Tüm düğümler: plak, x, y, z, kolon no, yay (t/m).</summary>
        public readonly List<(int Plak, double X, double Y, double Z, int Kolon, double Yay)> Dugum = new List<(int, double, double, double, int, double)>();
        /// <summary>Plak satırı: no, mesh x/y, h (m), g, p, Ko + 4 köşe (x, y, z).</summary>
        public readonly List<(int No, int Mx, int My, double H, double G, double P, double Ko, double[] Kose)> Plaklar = new List<(int, int, int, double, double, double, double, double[])>();
        /// <summary>Mesh: plak, I, J, K, L (0 = üçgen), h (m).</summary>
        public readonly List<(int Plak, int[] N, double H)> Meshler = new List<(int, int[], double)>();
        /// <summary>Nokta yükü: düğüm, kolon, 17 durum × (Pz, Mx, My).</summary>
        public readonly List<(int Dugum, int Kolon, double[] V)> Yukler = new List<(int, int, double[])>();
        private byte[] _de1;

        public static string RadYolu(string st4) => Path.ChangeExtension(st4, ".RAD");
        public static string De1Yolu(string st4) => Path.ChangeExtension(st4, ".DE1");
        public static bool Var(string st4) => File.Exists(RadYolu(st4)) && File.Exists(De1Yolu(st4));

        private static double D(string s) => double.TryParse(s, NumberStyles.Float, Inv, out var d) ? d : 0;
        private static int I(string s) => (int)Math.Round(D(s));

        /// <summary>
        /// RAD bölüm sırası: başlık (2. satır plak, düğüm, mesh, Ko, durum, nokta yükü …; 5. satır 3. alan qt) → ST4 yolu satırı →
        /// plaklar (2 satır) → mesh satırları → düğüm satırları → nokta yükleri (6 alanlı başlık + 51 değerlik satır).
        /// </summary>
        public static RadyeVerisi Oku(string st4)
        {
            if (!Var(st4)) return null;
            var l = PaylasimliDosya.Lines(RadYolu(st4));
            var r = new RadyeVerisi { _de1 = PaylasimliDosya.Bytes(De1Yolu(st4)) };
            var h = l.Length > 1 ? l[1].Split(',') : new string[0];
            int np = I(h.ElementAtOrDefault(0)), nd = I(h.ElementAtOrDefault(1)), nm = I(h.ElementAtOrDefault(2));
            r.Ko = D(h.ElementAtOrDefault(3));
            if (l.Length > 4) r.Qt = D(l[4].Split(',').ElementAtOrDefault(2));
            int i = Array.FindIndex(l, s => s.Contains(":\\") || s.EndsWith(".ST4", StringComparison.OrdinalIgnoreCase)) + 1;
            if (i <= 0) return r;
            for (int p = 0; p < np && i + 1 < l.Length; p++, i += 2)
            {
                var f = l[i].Split(','); var k = l[i + 1].Split(',').Select(D).ToArray();
                r.Plaklar.Add((I(f[0]), I(f[1]), I(f[2]), D(f[3]), D(f[4]), D(f[5]), D(f[6]), k));
            }
            for (int m = 0; m < nm && i < l.Length; m++, i++)
            {
                var f = l[i].Split(',');
                r.Meshler.Add((I(f[0]), f.Skip(1).Take(4).Select(I).ToArray(), D(f.ElementAtOrDefault(6))));
            }
            for (int n = 0; n < nd && i < l.Length; n++, i++)
            {
                var f = l[i].Split(',');
                r.Dugum.Add((I(f[0]), D(f[1]), D(f[2]), D(f[3]), I(f[4]), D(f[5])));
                if (D(f[5]) != 0) r.Dugumler.Add(n + 1);
            }
            for (; i + 1 < l.Length; i += 2)
            {
                var f = l[i].Split(','); var v = l[i + 1].Split(',');
                if (f.Length != 6 || v.Length != 51) break;
                r.Yukler.Add((I(f[0]), I(f[1]), v.Select(D).ToArray()));
            }
            return r;
        }

        /// <summary>Düğümün yük durumundaki düşey deplasmanı (m, aşağı +).</summary>
        public double W(int dugum, int vaka)
        {
            int o = 40 + 588 * (dugum - 1) + 28 * vaka;
            return o + 4 <= _de1.Length ? -BitConverter.ToSingle(_de1, o) : 0;
        }
    }

    public static class RadyeTablolari
    {
        private static TabloSatir Satir(bool grupBasi, params string[] h)
        {
            var r = new TabloSatir { GrupBasi = grupBasi };
            for (int i = 0; i < h.Length; i++) r.Hucreler.Add(new TabloHucre { Ilk = i, Son = i, Metin = h[i] ?? "" });
            return r;
        }

        private static TabloOgesi Tablo(params string[] sonEk)
        {
            var v = RadyeVerisi.Vakalar;
            var t = new TabloOgesi { Sutunlar = new[] { 6 }.Concat(v.Select(_ => 6)).Concat(sonEk.Select(_ => 7)).ToList(), Imza = "dosya" };
            t.Baslik.Add(Satir(false, new[] { "Nokta" }.Concat(v.Select(x => x.No)).Concat(sonEk.Select(s => s.Split('|')[0])).ToArray()));
            t.Baslik.Add(Satir(false, new[] { "no" }.Concat(v.Select(x => x.Ad)).Concat(sonEk.Select(s => s.Split('|')[1])).ToArray()));
            return t;
        }

        private static string S(double x, int d) => DosyaTablolari.S(x, d);

        /// <summary>RADYE NOKTA DEPLASMANLARI (mm).</summary>
        public static TabloOgesi Deplasmanlar(StaVeri v, ISet<string> katlar = null)
        {
            var r = RadyeVerisi.Oku(v.St4Yolu);
            var t = Tablo();
            if (r == null) return t;
            foreach (int n in r.Dugumler)
                t.Govde.Add(Satir(false, new[] { n.ToString() }.Concat(RadyeVerisi.Vakalar.Select(x => S(r.W(n, x.Vaka) * 1000, 3))).ToArray()));
            return t;
        }

        /// <summary>PLAK BİLGİLERİ (RAD plak satırları).</summary>
        public static TabloOgesi Plaklar(StaVeri v, ISet<string> katlar = null)
        {
            var r = RadyeVerisi.Oku(v.St4Yolu);
            var t = new TabloOgesi { Sutunlar = new[] { 6, 4, 7, 7 }.Concat(Enumerable.Repeat(6, 14)).ToList(), Imza = "dosya" };
            var k = new[] { "X1", "Y1", "Z1", "X2", "Y2", "Z2", "X3", "Y3", "Z3", "X4", "Y4", "Z4" };
            t.Baslik.Add(Satir(false, new[] { "Plak", "D", "mesh", "Ko", "g", "p" }.Concat(k).ToArray()));
            t.Baslik.Add(Satir(false, new[] { "no", "cm", "x  y", "t/m³", "t/m²", "t/m²" }.Concat(k.Select(_ => "")).ToArray()));
            if (r == null) return t;
            foreach (var p in r.Plaklar)
                t.Govde.Add(Satir(false, new[] { $"PL{p.No}", S(p.H * 100, 0), $"{p.Mx}  {p.My}", S(p.Ko, 0), S(p.G, 3), S(p.P, 3) }
                    .Concat(p.Kose.Take(12).Select(x => S(x, 2))).ToArray()));
            return t;
        }

        /// <summary>MESH BİLGİLERİ: satırda üç mesh (no, d, I, J, K, L; L = 0 boş).</summary>
        public static TabloOgesi Meshler(StaVeri v, ISet<string> katlar = null)
        {
            var r = RadyeVerisi.Oku(v.St4Yolu);
            var t = new TabloOgesi { Sutunlar = Enumerable.Repeat(new[] { 5, 4, 5, 5, 5, 5 }, 3).SelectMany(x => x).ToList(), Imza = "dosya" };
            t.Baslik.Add(Satir(false, Enumerable.Repeat(new[] { "Mesh", "d", "I", "J", "K", "L" }, 3).SelectMany(x => x).ToArray()));
            t.Baslik.Add(Satir(false, Enumerable.Repeat(new[] { "no", "cm", "", "", "", "" }, 3).SelectMany(x => x).ToArray()));
            if (r == null) return t;
            for (int i = 0; i < r.Meshler.Count; i += 3)
                t.Govde.Add(Satir(false, Enumerable.Range(i, 3).SelectMany(j => j < r.Meshler.Count
                    ? new[] { (j + 1).ToString(), S(r.Meshler[j].H * 100, 0) }.Concat(r.Meshler[j].N.Select(n => n > 0 ? n.ToString() : ""))
                    : Enumerable.Repeat("", 6)).ToArray()));
            return t;
        }

        /// <summary>RADYE NOKTA YÜKLERİ (t)-(m): düğüm sırasıyla, her düğüm için Pz, Mx, My satırı (değer float32 üzerinden yuvarlanır).</summary>
        public static TabloOgesi NoktaYukleri(StaVeri v, ISet<string> katlar = null)
        {
            var r = RadyeVerisi.Oku(v.St4Yolu);
            var t = Tablo();
            t.Sutunlar[0] = 8;
            if (r == null) return t;
            string[] bil = { "Pz", "Mx", "My" };
            foreach (var y in r.Yukler.OrderBy(y => y.Dugum))
                for (int c = 0; c < 3; c++)
                    t.Govde.Add(Satir(c == 0 && t.Govde.Count > 0, new[] { $"{y.Dugum} {bil[c]}" }
                        .Concat(RadyeVerisi.Vakalar.Select(x => S((float)y.V[3 * x.Vaka + c], 2))).ToArray()));
            return t;
        }

        /// <summary>WINKLER YAY YÜKLERİ (t) = yay · w, tüm düğümler.</summary>
        public static TabloOgesi Winkler(StaVeri v, ISet<string> katlar = null)
        {
            var r = RadyeVerisi.Oku(v.St4Yolu);
            var t = Tablo();
            if (r == null) return t;
            for (int n = 1; n <= r.Dugum.Count; n++)
            {
                int d = n;
                t.Govde.Add(Satir(false, new[] { n.ToString() }.Concat(RadyeVerisi.Vakalar.Select(x => S(r.Dugum[d - 1].Yay * r.W(d, x.Vaka), 2))).ToArray()));
            }
            return t;
        }

        /// <summary>
        /// ZEMİN GERİLMESİNİN TAŞIMA GÜCÜNE GÖRE KONTROLÜ: G = Ko·w_g; Q = max(q1..q6); E = (max|σ deprem| + 0.3|σ Ez|)/2;
        /// W = max(|σ13|, |σ14|); 1.4G+1.6Q, G+Q+E, G+Q+W &lt; qt.
        /// </summary>
        public static TabloOgesi ZeminKontrolu(StaVeri v, ISet<string> katlar = null)
        {
            var r = RadyeVerisi.Oku(v.St4Yolu);
            var t = new TabloOgesi { Sutunlar = new[] { 6, 7, 7, 7, 7, 21, 21, 21 }.ToList(), Imza = "dosya" };
            t.Baslik.Add(Satir(false, "Nokta", "G", "Q", "E", "W", "1.4 G + 1.6 Q", "G + Q + E", "G + Q + W"));
            if (r == null) return t;
            string K(double c) => $"{S(c, 3)} < {S(r.Qt, 3)} " + (c < r.Qt ? "✓" : "✗");
            foreach (int n in r.Dugumler)
            {
                double s(int vaka) => r.Ko * r.W(n, vaka);
                double g = s(0), q = Enumerable.Range(1, 6).Max(s);
                double e = (new[] { 8, 9, 10, 11 }.Max(k => Math.Abs(s(k))) + 0.3 * Math.Abs(s(16))) / 2;
                double w = Math.Max(Math.Abs(s(12)), Math.Abs(s(13)));
                t.Govde.Add(Satir(false, n.ToString(), S(g, 3), S(q, 3), S(e, 3), S(w, 3), K(1.4 * g + 1.6 * q), K(g + q + e), K(g + q + w)));
            }
            return t;
        }

        /// <summary>ZEMİN GERİLMESİ (t/m²) = Ko·w; max σ = 1.4·g + 1.6·max(q).</summary>
        public static TabloOgesi ZeminGerilmesi(StaVeri v, ISet<string> katlar = null)
        {
            var r = RadyeVerisi.Oku(v.St4Yolu);
            var t = Tablo("max.|σ");
            if (r == null) return t;
            foreach (int n in r.Dugumler)
            {
                var s = RadyeVerisi.Vakalar.Select(x => r.Ko * r.W(n, x.Vaka)).ToArray();
                double mx = 1.4 * s[0] + 1.6 * s.Skip(1).Take(6).Max();
                t.Govde.Add(Satir(false, new[] { n.ToString() }.Concat(s.Select((x, i) => S(x, i < 7 ? 3 : 2))).Append(S(mx, 3)).ToArray()));
            }
            return t;
        }
    }
}
