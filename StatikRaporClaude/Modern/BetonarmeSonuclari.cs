using System.Text.RegularExpressions;
using StatikRaporClaude.Gpr;

namespace StatikRaporClaude.Modern
{
    /// <summary>
    /// "KİRİŞ BETONARME HESAP SONUÇLARI" bloğu (6 satır). Değerler GPR'deki metinle aynı tutulur, yeniden hesap yok.
    /// Sütunlar: üstMsol, altMsol, Mac., üstMsağ, altMsağ, DONATI.
    /// </summary>
    public sealed class KirisSonucu
    {
        public string Ad, D, B, Sinif;
        public string[] Mduz = new string[5], MaxM = new string[5], Fcd = new string[5], AsBasinc = new string[5], As = new string[5], Asw = new string[5];
        public readonly List<string> Boyuna = new List<string>();
        public string Etriye, Wk;
        public bool WkUygun;
        /// <summary>Statik şema (BEA +36), tablodaki sol/sağ yönüne göre; dosya yoksa null.</summary>
        public int? UcTipi;
        public int Sayfa;
    }

    /// <summary>"KOLON BETONARME HESAP SONUÇLARI" bloğu (6 kombinasyon satırı).</summary>
    public sealed class KolonSonucu
    {
        public string Ad, Bx, By, I, J, Hk, BetaX, BetaY, Sinif;
        public readonly List<string[]> Kombinasyonlar = new List<string[]>(); // ad, N, minorM, majorM, fcd, ρ, As
        public string Boyuna, Etriye, AsAc;
        public int Sayfa;
    }

    public static class BetonarmeSonuclari
    {
        public static List<KirisSonucu> Kirisler(GprBolum bolum)
        {
            var sonuc = new List<KirisSonucu>();
            foreach (var blok in GprTablo.Bloklar(GprTablo.Satirlar(bolum)))
            {
                // 1. satır etiketi V14 "Mduz.", V15 "ΔM" (Kiriş mesnet düzeltme momenti)
                string c0 = GprTablo.Hucre(blok[0], 0);
                string etiket = c0.Contains("Mduz") ? "Mduz" : c0.Contains("ΔM") ? "ΔM" : null;
                if (blok.Count < 6 || etiket == null) continue;
                var k = new KirisSonucu { Sayfa = blok[0].Sayfa.No };
                // 3 haneli adlarda (KB-001) ad ile "Mduz." arasında boşluk kalmaz
                k.Ad = c0.Replace(etiket, " " + etiket).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                k.Sinif = GprTablo.Hucre(blok[1], 0).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                k.D = Regex.Match(GprTablo.Hucre(blok[2], 0), @"D=(\S+)").Groups[1].Value;
                k.B = Regex.Match(GprTablo.Hucre(blok[3], 0), @"B=(\S+)").Groups[1].Value;
                for (int i = 0; i < 5; i++)
                {
                    k.Mduz[i] = GprTablo.Hucre(blok[0], i + 1);
                    k.MaxM[i] = GprTablo.Hucre(blok[1], i + 1);
                    k.Fcd[i] = GprTablo.Hucre(blok[2], i + 1);
                    k.AsBasinc[i] = GprTablo.Hucre(blok[3], i + 1);
                    k.As[i] = GprTablo.Hucre(blok[4], i + 1);
                    k.Asw[i] = GprTablo.Hucre(blok[5], i + 1);
                }
                for (int r = 0; r < 4; r++)
                    foreach (var parca in GprTablo.Hucre(blok[r], 6).Split('+', StringSplitOptions.RemoveEmptyEntries))
                        if (parca.Trim().Length > 0) k.Boyuna.Add(parca.Trim());
                k.Etriye = GprTablo.Hucre(blok[4], 6).Replace("(etriye)", "").Trim();
                k.Wk = GprTablo.Hucre(blok[5], 6).Replace("✓", "").Trim();
                k.WkUygun = GprTablo.Hucre(blok[5], 6).Contains("✓");
                sonuc.Add(k);
            }
            return sonuc;
        }

        public static List<KolonSonucu> Kolonlar(GprBolum bolum)
        {
            var sonuc = new List<KolonSonucu>();
            foreach (var blok in GprTablo.Bloklar(GprTablo.Satirlar(bolum)))
            {
                bool poligon = GprTablo.Hucre(blok[0], 1).StartsWith("Polygon");   // poligon kolon: "Polygon / kolon"
                if (blok.Count < 6 || !(GprTablo.Hucre(blok[0], 1).StartsWith("Bx=") || poligon)) continue;
                var k = new KolonSonucu { Sayfa = blok[0].Sayfa.No, Ad = GprTablo.Hucre(blok[0], 0) };
                string sol(int r) => GprTablo.Hucre(blok[r], 1);
                k.Bx = poligon ? "Polygon" : Regex.Match(sol(0), @"Bx=(\S+)").Groups[1].Value;
                k.By = poligon ? "kolon" : Regex.Match(sol(1), @"By=(\S+)").Groups[1].Value;
                k.Hk = Regex.Match(sol(3), @"Hk=(\S+?)m").Groups[1].Value;
                k.BetaX = Regex.Match(sol(4), @"[ßβ]x\s*=\s*(\S+)").Groups[1].Value;
                k.BetaY = Regex.Match(sol(5), @"[ßβ]y\s*=\s*(\S+)").Groups[1].Value;
                k.I = GprTablo.Hucre(blok[2], 0).Replace("I:", "").Trim();
                k.J = GprTablo.Hucre(blok[3], 0).Replace("J:", "").Trim();
                k.Sinif = GprTablo.Hucre(blok[5], 0).Replace("✓", "").Trim();
                foreach (var r in blok.Take(6))
                {
                    string ad = Regex.Match(GprTablo.Hucre(r, 1), @"([XY]-\(.*\))").Groups[1].Value.Replace(" ", "");
                    k.Kombinasyonlar.Add(new[] { ad, GprTablo.Hucre(r, 2), GprTablo.Hucre(r, 3), GprTablo.Hucre(r, 4), GprTablo.Hucre(r, 5), GprTablo.Hucre(r, 6), GprTablo.Hucre(r, 7) });
                }
                k.Boyuna = GprTablo.Hucre(blok[0], 8);
                k.Etriye = GprTablo.Hucre(blok[1], 8).Replace("(etriye)", "").Trim();
                k.AsAc = Regex.Match(GprTablo.Hucre(blok[5], 8), @"As/Ac=\s*([\d.]+)").Groups[1].Value;
                sonuc.Add(k);
            }
            return sonuc;
        }

        /// <summary>Sayı metni → double (karşılaştırma için); okunamazsa NaN.</summary>
        public static double Sayi(string s) =>
            double.TryParse(s?.Trim().Trim('(', ')').Replace("m", ""), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
    }
}
