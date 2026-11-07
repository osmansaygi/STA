using System.Text.RegularExpressions;
using StatikRaporClaude.Gpr;

namespace StatikRaporClaude.Modern
{
    /// <summary>DEPREM RAPORU bölümünün özet verisi. Değerler GPR metninden aynen alınır.</summary>
    public sealed class DepremRaporu
    {
        public readonly List<(string Etiket, string Sembol, string Deger)> Parametreler = new List<(string, string, string)>();
        public readonly List<(double T, double Sa)> Spektrum = new List<(double, double)>();
        public readonly List<string> SpektrumNotlari = new List<string>();
        public string Ra;
        /// <summary>Yapının hakim periyotları (spektrum grafiğinde işaretlenir).</summary>
        public double? Tx, Ty;
        public bool DosyaKaynakli;   // ST4 + FeaMod kaynaklı (GPR yok)
        public readonly List<string[]> Modal = new List<string[]>(); // satır adı + 6 mod (+ toplam)
        public readonly List<string> Kontroller = new List<string>();

        public string Deger(string etiketParcasi) =>
            Parametreler.FirstOrDefault(p => GprBolumleri.Anahtar(p.Etiket).Contains(GprBolumleri.Anahtar(etiketParcasi))).Deger;

        // ---------------- GPR'siz: ST4 + FeaMod ----------------

        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>TBDY 2018 Tablo 2.1 Fs (Ss = 0.25, 0.50, 0.75, 1.00, 1.25, 1.50).</summary>
        private static readonly (string Sinif, double[] Fs)[] FsTablosu =
        {
            ("ZA", new[] { 0.8, 0.8, 0.8, 0.8, 0.8, 0.8 }), ("ZB", new[] { 0.9, 0.9, 0.9, 0.9, 0.9, 0.9 }),
            ("ZC", new[] { 1.3, 1.3, 1.2, 1.2, 1.2, 1.2 }), ("ZD", new[] { 1.6, 1.4, 1.2, 1.1, 1.0, 1.0 }),
            ("ZE", new[] { 2.4, 1.7, 1.3, 1.1, 0.9, 0.8 }),
        };

        private static double Ara(double[] y, double ss)
        {
            double t = Math.Clamp((ss - 0.25) / 0.25, 0, 5);
            int i = Math.Min((int)t, 4);
            return y[i] + (y[i + 1] - y[i]) * (t - i);
        }

        /// <summary>
        /// Deprem raporunu GPR olmadan kurar: parametreler ST4'ten (zemin sınıfı Sds/Ss = Fs'den, DTS/BYS TBDY tablolarından),
        /// spektrum TBDY 2.3.4 bağıntılarından, periyot/ω/kütle katılımı FeaMod'dan (N_ULK_02, VKF_01, K_BLG_03 ile doğrulandı).
        /// </summary>
        public static DepremRaporu Dosyadan(Veri.StaVeri v)
        {
            var m = v.Model;
            var r = new DepremRaporu { DosyaKaynakli = true };
            string F(double x, string f) => x.ToString(f, Inv);
            string zemin = m.Ss > 0 ? FsTablosu.OrderBy(z => Math.Abs(m.Ss * Ara(z.Fs, m.Ss) - m.Sds)).First().Sinif : "—";
            int bks = m.Onem >= 1.45 ? 1 : m.Onem >= 1.15 ? 2 : 3;
            string dts = (m.Sds < 0.33 ? "4" : m.Sds < 0.5 ? "3" : m.Sds < 0.75 ? "2" : "1") + (bks == 1 ? "a" : "");
            var kat = m.Katlar.Where(k => !Regex.IsMatch(GprBolumleri.Anahtar(k.Ad), "CATI|ASANSOR")).ToList();
            double hn = (kat.Count > 0 ? kat.Max(k => k.Kot) : 0) - m.Hbodrum;
            double[] sinir = dts.StartsWith("3") || dts.StartsWith("4") ? new[] { 91, 70, 56, 42, 28, 17.5, 10.5 } : new[] { 70, 56, 42, 28, 17.5, 10.5, 7 };
            int bys = Array.FindIndex(sinir, s => hn > s); bys = bys < 0 ? 8 : bys + 1;

            void P(string e, string s, string d) => r.Parametreler.Add((e, s, d));
            P("DEPREM STANDARDI", "", "TBDY2018");
            P("DEPREM ANALIZI", "", "MOD SÜPERPOZİSYONU İLE MODAL ANALİZ");
            P("DEPREM YER HAREKETİ DÜZEYİ", "", "DD2   50 yılda aşılma olasılığı %10");
            P("ZEMİN SINIFI", "", zemin);
            P("BİNA KOORDİNATI", "(ENLEM/BOYLAM)", $"{F(m.Enlem, "0.#####")}° / {F(m.Boylam, "0.#####")}°");
            P("YEREL SPECTRAL İVME KATSAYISI", "Ss/S1", $"{F(m.Ss, "0.000")} / {F(m.S1, "0.000")}");
            P("TASARIM SPECTRAL İVME KATSAYISI", "Sds/Sd1", $"{F(m.Sds, "0.000")} / {F(m.Sd1, "0.000")}   DD2,   {F(m.Sds3, "0.000")} / {F(m.Sd13, "0.000")}   DD3");
            P("YAPI DAVRANIŞ KATSAYISI", "R", F(m.R, "0.00"));
            P("SİSTEM DAYANIM FAZLALIĞI KATSAYISI", "D", F(m.Dfazla, "0.0"));
            P("DEPREM TASARIM SINIFI", "DTS", dts);
            P("BİNA YÜKSEKLİK SINIFI", "BYS", $"{bys}   Hn={F(hn, "0.##")}m");
            P("BİNA KULLANIM SINIFI", "BKS", $"{bks}   I = {F(m.Onem, "0.0")}");
            P("Modal Analiz min. deprem yükü oranı", "β", F(m.Beta, "0.0#"));
            P("Deprem yükü eksantirisitesi", "", F(m.Eksantrisite, "0.000"));
            P("Deprem modal analiz CQC sönüm oranı", "", "%5");

            // spektrum: Sae(T), TL = 6 s
            if (m.Sds > 0 && m.Sd1 > 0)
            {
                double ta = 0.2 * m.Sd1 / m.Sds, tb = m.Sd1 / m.Sds;
                double Sae(double t) => t < ta ? (0.4 + 0.6 * t / ta) * m.Sds : t <= tb ? m.Sds : t <= 6 ? m.Sd1 / t : m.Sd1 * 6 / (t * t);
                var tler = new List<double> { 0, ta, tb };
                for (double t = tb + 0.05; t < 6; t += t < tb + 0.1 ? 0.05 : t < 2 ? 0.1 : 0.2) tler.Add(t);
                foreach (var t in tler) r.Spektrum.Add((Math.Round(t, 2), Math.Round(Sae(t), 3)));
                r.SpektrumNotlari.Add("T < Ta   Sae=(0.4 + 0.6·T/Ta)·Sds");
                r.SpektrumNotlari.Add("Ta < T < Tb   Sae=Sds");
                r.SpektrumNotlari.Add("Tb < T < Tl=6   Sae=Sd1/T");
                r.SpektrumNotlari.Add("T > Tl   Sae=Sd1·Tl/T²");
                r.SpektrumNotlari.Add($"Ta=0.2·Sd1/Sds={F(ta, "0.000")}, Tb=Sd1/Sds={F(tb, "0.000")}");
            }

            // modal: FeaMod int[1] mod sayısı; T double 20+8i, ω double 20+8(n+1)+8i; Mxr/Myr float çiftleri dosya sonunda (8n bayt)
            string yol = Path.ChangeExtension(v.Bea.Yol, ".FeaMod");
            if (File.Exists(yol))
            {
                var b = PaylasimliDosya.Bytes(yol);
                int n = b.Length >= 8 ? BitConverter.ToInt32(b, 4) : 0;
                if (n > 0 && 20 + 8 * (2 * n + 1) <= b.Length && 8 * n <= b.Length)
                {
                    int g = Math.Min(n, 6), kb = b.Length - 8 * n;
                    double T(int i) => BitConverter.ToDouble(b, 20 + 8 * i);
                    double W(int i) => BitConverter.ToDouble(b, 20 + 8 * (n + 1) + 8 * i);
                    double Mx(int i) => BitConverter.ToSingle(b, kb + 8 * i) * 100.0;
                    double My(int i) => BitConverter.ToSingle(b, kb + 8 * i + 4) * 100.0;
                    double sx = Enumerable.Range(0, n).Sum(Mx), sy = Enumerable.Range(0, n).Sum(My);
                    string[] Satir(string ad, Func<int, string> d, string toplam) =>
                        new[] { ad }.Concat(Enumerable.Range(0, 6).Select(i => i < g ? d(i) : "")).Append(toplam).ToArray();
                    r.Modal.Add(Satir("ω", i => F(W(i), "0.00"), ""));
                    r.Modal.Add(Satir("T", i => F(T(i), "0.0000"), ""));
                    r.Modal.Add(Satir("Mxr%", i => F(Mx(i), "0.000"), F(sx, "0.00")));
                    r.Modal.Add(Satir("Myr%", i => F(My(i), "0.000"), F(sy, "0.00")));
                    string Sonuc(string yon, double s) => $"Dinamik kütle oranı {yon} yönü ({n} mod): %{F(s, "0.00")} {(s >= 95 ? "≥ %95 yeterli" : "< %95 yetersiz")}";
                    r.Kontroller.Add(Sonuc("X", sx));
                    r.Kontroller.Add(Sonuc("Y", sy));
                    int ix = Enumerable.Range(0, n).OrderByDescending(Mx).First(), iy = Enumerable.Range(0, n).OrderByDescending(My).First();
                    r.Tx = T(ix); r.Ty = T(iy);
                }
            }
            return r;
        }

        public static DepremRaporu Oku(GprBolum bolum)
        {
            var r = new DepremRaporu();
            bool spektrum = false, spektrumBitti = false;
            foreach (var s in bolum.Sayfalar)
                foreach (var y in s.Yazilar.Where(t => t.Font == 1 && (t.Tip == 8 || t.Tip == -8) && bolum.Icinde(s, t.Y)).OrderBy(t => t.Y))
                {
                    string m = y.Metin.TrimEnd();
                    string t = m.Trim();
                    if (t.Length == 0) continue;

                    if (!spektrumBitti && t.StartsWith("│") && Regex.IsMatch(t, @"^│\s*[\d.]+\s*│\s*[\d.]+\s*│"))
                    {
                        var h = t.Split('│', StringSplitOptions.RemoveEmptyEntries);
                        r.Spektrum.Add((BetonarmeSonuclari.Sayi(h[0]), BetonarmeSonuclari.Sayi(h[1])));
                        spektrum = true;
                        string not = h.Length > 2 ? string.Join(" ", h.Skip(2)).Trim() : "";
                        if (not.Length > 0) r.SpektrumNotlari.Add(Regex.Replace(not, @"\s{2,}", "   "));
                        continue;
                    }
                    if (t.StartsWith("│") && Regex.IsMatch(t, @"^│\s*(Mod|ω|T|M[xy]r%)\s*│\s*([\d.]+|1\.mod)\s*│"))
                    {
                        var h = t.Split('│').Select(x => x.Trim()).ToList();
                        h.RemoveAt(0);
                        string toplam = h.Count > 0 ? h[^1].Replace("─", "").Replace("Σ", "").Replace("=", "").Trim() : "";
                        var hucre = h.Take(7).ToList();
                        if (toplam.Length > 0 && hucre.Count == 7) hucre.Add(toplam);
                        r.Modal.Add(hucre.ToArray());
                        continue;
                    }
                    if (spektrum) spektrumBitti = true;
                    if (t.StartsWith("Ra(T)")) { r.Ra = Regex.Replace(t, @"\s{2,}", "   "); continue; }
                    if (t.Contains("Dinamik kütle oranı") || t.Contains(">>")) { string k = Regex.Replace(t, @"\s{2,}", "  "); if (!r.Kontroller.Contains(k)) r.Kontroller.Add(k); continue; }

                    // "ETİKET    SEMBOL : DEĞER" — yalnız spektrumdan önceki parametre listesi
                    if (!spektrum && s == bolum.Sayfalar[0])
                    {
                        var mm = Regex.Match(m, @"^\s{2,}(?<e>[^:]+?)\s{2,}(?<s>\S+(?:/\S+)?\s*(?:\([^)]*\))?)?\s*:\s*(?<d>.*)$");
                        if (!mm.Success) mm = Regex.Match(m, @"^\s+(?<e>[^:]+?)\s*:\s*(?<d>.*)$");
                        if (mm.Success && mm.Groups["d"].Value.Trim().Length > 0)
                            r.Parametreler.Add((mm.Groups["e"].Value.Trim().TrimStart('├', '└', '─', '-', ' '),mm.Groups["s"].Value.Trim(), Regex.Replace(mm.Groups["d"].Value.Trim(), @"\s{2,}", "   ")));
                    }
                }
            // hakim periyotlar: "Yapı peryodları  Tx=0.528  Ty=0.347" / "Tx=  0.528 s."
            foreach (var s in bolum.Sayfalar)
                foreach (var y in s.Yazilar.Where(t => bolum.Icinde(s, t.Y)))
                {
                    if (r.Tx == null && Regex.Match(y.Metin, @"(?<![\w])Tx=\s*([\d.]+)") is { Success: true } mx) r.Tx = BetonarmeSonuclari.Sayi(mx.Groups[1].Value);
                    if (r.Ty == null && Regex.Match(y.Metin, @"(?<![\w])Ty=\s*([\d.]+)") is { Success: true } my) r.Ty = BetonarmeSonuclari.Sayi(my.Groups[1].Value);
                }
            return r;
        }
    }
}
