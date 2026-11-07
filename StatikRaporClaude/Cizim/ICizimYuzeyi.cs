using System.Drawing;

namespace StatikRaporClaude.Cizim
{
    public enum Hiza { Sol, Orta, Sag }

    /// <summary>Yazı tipi tanımı. Boy, o anki dünya biriminde (mm veya GPR birimi) yazı yüksekliğidir.</summary>
    public readonly record struct YaziTipi(string Aile, float Boy, bool Kalin = false)
    {
        public const string Arayuz = "Segoe UI";
        public const string Sta = "CourSTA";      // C:\Sta4V14\Coursta.ttf — STA4 GPR sayfaları
        public const string Sabit = "Courier New";
        public const string Oransal = "Arial";
    }

    /// <summary>
    /// Ekran (GDI+) ve PDF (PDFsharp) için ortak çizim yüzeyi. Yazı konumu (x, y) yazının üst sol köşesidir.
    /// Sarma ve hizalama bu katmanın üstünde, iki yüzeyde aynı ölçümle yapılır.
    /// </summary>
    public interface ICizimYuzeyi
    {
        void Kaydet();
        void GeriYukle();
        void Tasi(float dx, float dy);
        void Olcekle(float s);
        void Kirp(RectangleF r);

        void Cizgi(Color renk, float kalinlik, float x1, float y1, float x2, float y2, bool kesik = false);
        void Dikdortgen(RectangleF r, Color? dolgu, Color? cerceve = null, float kalinlik = 0);
        void Poligon(PointF[] noktalar, Color dolgu);
        void Daire(PointF merkez, float yaricap, Color? dolgu, Color? cerceve, float kalinlik);
        void CokluCizgi(PointF[] noktalar, Color renk, float kalinlik);
        void Yazi(string metin, YaziTipi f, Color renk, float x, float y);
        SizeF Olc(string metin, YaziTipi f);
        void Resim(Image resim, RectangleF r);

        /// <summary>PDF içi bağlantı (içindekiler). Ekranda yok sayılır.</summary>
        void Baglanti(RectangleF r, int hedefSayfa);
    }

    public static class CizimUzanti
    {
        public const string SembolAilesi = "Segoe UI Symbol";

        /// <summary>Segoe UI ve Arial'de glifi olmayan işaret (kutu çizgisi, ✓ ✗). PDF'te boş kutu olarak çıkar.</summary>
        public static bool SembolKarakter(char c) =>
            c is '✓' or '✗' || (c >= '\u2500' && c <= '\u257F');

        /// <summary>Sembol karakterleri Segoe UI Symbol ile, kalanı istenen yazı tipiyle çizilsin diye metni parçalar.</summary>
        public static IEnumerable<(string Parca, bool Sembol)> Parcala(string m)
        {
            if (string.IsNullOrEmpty(m)) { yield return ("", false); yield break; }
            int bas = 0;
            bool sembol = SembolKarakter(m[0]);
            for (int i = 1; i <= m.Length; i++)
            {
                bool son = i == m.Length || SembolKarakter(m[i]) != sembol;
                if (!son) continue;
                yield return (m.Substring(bas, i - bas), sembol);
                if (i == m.Length) yield break;
                bas = i;
                sembol = !sembol;
            }
        }

        /// <summary>Tek satır, hizalı; genişlik verilirse taşan kısım "…" ile kısaltılır.</summary>
        public static void YaziHizali(this ICizimYuzeyi y, string m, YaziTipi f, Color c, float x, float ust, float w, Hiza h)
        {
            m ??= "";
            float gen = y.Olc(m, f).Width;
            if (w > 0 && gen > w && m.Length > 1)
            {
                while (m.Length > 1 && y.Olc(m + "…", f).Width > w) m = m.Substring(0, m.Length - 1);
                m += "…";
                gen = y.Olc(m, f).Width;
            }
            float px = h switch
            {
                Hiza.Sol => x,
                Hiza.Orta => w > 0 ? x + (w - gen) / 2 : x - gen / 2,
                _ => w > 0 ? x + w - gen : x - gen
            };
            y.Yazi(m, f, c, px, ust);
        }

        /// <summary>Kelime sarma: satırları döndürür.</summary>
        public static List<string> Sar(this ICizimYuzeyi y, string m, YaziTipi f, float w)
        {
            var satirlar = new List<string>();
            foreach (var paragraf in (m ?? "").Split('\n'))
            {
                string aktif = "";
                foreach (var kelime in paragraf.Split(' '))
                {
                    string aday = aktif.Length == 0 ? kelime : aktif + " " + kelime;
                    if (aktif.Length > 0 && y.Olc(aday, f).Width > w) { satirlar.Add(aktif); aktif = kelime; }
                    else aktif = aday;
                }
                satirlar.Add(aktif);
            }
            return satirlar;
        }

        public static float SatirAraligi(YaziTipi f) => f.Boy * 1.32f;
    }
}
