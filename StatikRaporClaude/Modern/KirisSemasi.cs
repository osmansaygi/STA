using System.Drawing;
using StatikRaporClaude.Cizim;

namespace StatikRaporClaude.Modern
{
    /// <summary>
    /// Kirişin statik şeması (uç tipi), BEA +36 (int16): 0 iki uç ankastre, 5 iki uç basit, 1 sol basit / sağ ankastre,
    /// 2 sol ankastre / sağ basit, 3 sol konsol, 4 sağ konsol (bkz. docs/sta4cad-statik-dosyalari.md "Uç tipi").
    /// Basit uç = kolona değil başka kirişe saplanan uç (mafsallı mesnet).
    /// </summary>
    public static class KirisSemasi
    {
        private enum Uc { Ankastre, Mafsal, Serbest }

        public static bool Gecerli(int kod) => kod >= 0 && kod <= 5;

        /// <summary>Sol/sağ ters gösterilen tablo (Y doğrultusundaki kirişin betonarme satırı) için kod.</summary>
        public static int Ters(int kod) => kod switch { 1 => 2, 2 => 1, 3 => 4, 4 => 3, _ => kod };

        private static (Uc Sol, Uc Sag) Uclar(int kod) => kod switch
        {
            5 => (Uc.Mafsal, Uc.Mafsal),
            1 => (Uc.Mafsal, Uc.Ankastre),
            2 => (Uc.Ankastre, Uc.Mafsal),
            3 => (Uc.Serbest, Uc.Ankastre),
            4 => (Uc.Ankastre, Uc.Serbest),
            _ => (Uc.Ankastre, Uc.Ankastre),
        };

        public static string Ad(int kod) => kod switch
        {
            5 => "basit kiriş (iki ucu kirişe saplı)",
            1 => "sol ucu kirişe saplı",
            2 => "sağ ucu kirişe saplı",
            3 => "konsol (sol uç serbest)",
            4 => "konsol (sağ uç serbest)",
            _ => "iki ucu kolona ankastre",
        };

        public static bool Konsol(int kod) => kod == 3 || kod == 4;
        public static bool Sapli(int kod) => kod == 1 || kod == 2 || kod == 5;

        /// <summary>Konsol turuncu, saplı kobalt, ankastre koyu gri: tablo taranırken tip renkten seçilir.</summary>
        public static Color Renk(int kod) => Konsol(kod) ? ModernSayfa.Turuncu : Sapli(kod) ? ModernSayfa.Kobalt : Color.FromArgb(90, 98, 110);

        /// <summary>Şema: (x, yOrta) solundan başlayan w genişliğinde, ~2.6 mm yüksekliğinde.</summary>
        public static void Ciz(ICizimYuzeyi g, float x, float yOrta, float w, int kod)
        {
            var renk = Renk(kod);
            var (sol, sag) = Uclar(kod);
            float x1 = x + 1.1f, x2 = x + w - 1.1f;
            g.Cizgi(renk, 0.45f, sol == Uc.Serbest ? x1 - 0.6f : x1, yOrta, sag == Uc.Serbest ? x2 + 0.6f : x2, yOrta);
            UcCiz(g, x1, yOrta, sol, -1, renk);
            UcCiz(g, x2, yOrta, sag, +1, renk);
        }

        private static void UcCiz(ICizimYuzeyi g, float x, float y, Uc uc, int yon, Color renk)
        {
            switch (uc)
            {
                case Uc.Ankastre:
                    // duvar çizgisi + dışa doğru tarama
                    g.Cizgi(renk, 0.35f, x, y - 1.3f, x, y + 1.3f);
                    for (float k = -1.1f; k <= 1.15f; k += 0.75f)
                        g.Cizgi(renk, 0.15f, x, y + k, x + yon * 0.7f, y + k + 0.55f);
                    break;
                case Uc.Mafsal:
                    // üçgen mafsal + zemin çizgisi
                    g.Poligon(new[] { new PointF(x, y), new PointF(x - 0.75f, y + 1.15f), new PointF(x + 0.75f, y + 1.15f) }, renk);
                    g.Cizgi(renk, 0.15f, x - 1f, y + 1.35f, x + 1f, y + 1.35f);
                    break;
                case Uc.Serbest:
                    // serbest uç: küçük açık daire
                    g.Daire(new PointF(x + yon * 0.6f, y), 0.35f, Color.White, renk, 0.15f);
                    break;
            }
        }

        /// <summary>Tablo üstü açıklama satırı: üç tip yan yana; yükseklik ~4.6 mm.</summary>
        public static void Aciklama(ModernSayfa s, float x, float y)
        {
            var f = ModernSayfa.F(6.6f);
            s.Metin("Kiriş statik şeması:", ModernSayfa.F(6.6f, true), ModernSayfa.Koyu, x, y);
            float cx = x + 24f;
            foreach (var (kod, ad) in new[] { (0, "iki ucu kolona ankastre"), (2, "ucu kirişe saplı (mafsallı)"), (5, "iki ucu saplı (basit)"), (4, "konsol (serbest uç)") })
            {
                Ciz(s.G, cx, y + 1.25f, 9f, kod);
                s.Metin(ad, f, ModernSayfa.Gri, cx + 10f, y);
                cx += 11f + s.Olc(ad, f).Width + 5f;
            }
        }
    }
}
