using System.Drawing;
using StatikRaporClaude.Cizim;

namespace StatikRaporClaude.Gpr
{
    /// <summary>GPR sayfasını vektör olarak çizer. Sayfa birimi: 2030 x 2870 (A4 oranı), y aşağı.</summary>
    public static class GprCizici
    {
        public const float SayfaGenislik = 2030f;
        public const float SayfaYukseklik = 2870f;

        /// <summary>STA4 sayfa gövdesi (çerçeve içi, başlık altı).</summary>
        public static readonly RectangleF Govde = RectangleF.FromLTRB(100, 130, 1990, 2860);

        // Çizgi stil alanı DOS/EGA renk numarası gibi davranıyor (spektrum eğrisi 1 = mavi).
        private static readonly Color[] Ega =
        {
            Color.Black, Color.FromArgb(0, 0, 170), Color.FromArgb(0, 140, 0), Color.FromArgb(0, 150, 150),
            Color.FromArgb(190, 0, 0), Color.FromArgb(160, 0, 160), Color.FromArgb(150, 90, 0), Color.FromArgb(120, 120, 120),
            Color.FromArgb(85, 85, 85), Color.FromArgb(60, 60, 255), Color.FromArgb(40, 180, 40), Color.FromArgb(40, 170, 200),
            Color.FromArgb(230, 40, 40), Color.FromArgb(200, 60, 200), Color.FromArgb(255, 235, 0), Color.Black
        };

        private static Color Renk(int stil) => stil >= 0 && stil < Ega.Length ? Ega[stil] : Color.Black;

        private static YaziTipi Font(GprYazi y)
        {
            bool sabit = y.Font != 0;
            float boy = y.Tip switch { 8 => 28, 7 => 24, -8 => 28, -9 => 30, -10 => 30, -11 => 36, -12 => 40, 5 => 20, _ => 26 };
            return new YaziTipi(sabit ? YaziTipi.Sta : YaziTipi.Oransal, boy, y.Tip < 0);
        }

        /// <summary>Önizleme: sayfanın tamamını hedefe sığdırır (STA4 başlığı dahil).</summary>
        public static void Ciz(ICizimYuzeyi g, GprSayfa s, RectangleF hedef)
        {
            float olcek = Math.Min(hedef.Width / SayfaGenislik, hedef.Height / SayfaYukseklik);
            g.Kaydet();
            g.Tasi(hedef.X + (hedef.Width - SayfaGenislik * olcek) / 2, hedef.Y + (hedef.Height - SayfaYukseklik * olcek) / 2);
            g.Olcekle(olcek);
            g.Dikdortgen(new RectangleF(0, 0, SayfaGenislik, SayfaYukseklik), Color.White);
            Icerik(g, s, true, float.MinValue, float.MaxValue);
            g.GeriYukle();
        }

        /// <summary>
        /// Rapor: STA4 çerçevesi ve başlığı olmadan gövdenin [yUst, yAlt] aralığını hedefin üstüne yerleştirir.
        /// Döndürülen değer hedefte kullanılan yüksekliktir.
        /// </summary>
        public static float CizGovde(ICizimYuzeyi g, GprSayfa s, RectangleF hedef, float yUst, float yAlt)
        {
            float ust = Math.Max(Govde.Top, yUst), alt = Math.Min(Govde.Bottom, yAlt);
            float olcek = Math.Min(hedef.Width / Govde.Width, hedef.Height / (Govde.Bottom - Govde.Top));
            float x0 = hedef.X + (hedef.Width - Govde.Width * olcek) / 2;
            g.Kaydet();
            g.Tasi(x0, hedef.Y);
            g.Olcekle(olcek);
            g.Tasi(-Govde.Left, -ust);
            g.Kirp(RectangleF.FromLTRB(Govde.Left + 4, ust + (ust > Govde.Top ? 0 : 4), Govde.Right - 4, alt - (alt < Govde.Bottom ? 0 : 4)));
            Icerik(g, s, false, ust - 40, alt + 40);
            g.GeriYukle();
            return (alt - ust) * olcek;
        }

        /// <summary>STA4 sayfa çerçevesi ve başlık çizgileri (yeniden dizgide atlanır).</summary>
        public static bool CerceveCizgisi(GprCizgi c)
        {
            float y1 = Math.Min(c.Y1, c.Y2), y2 = Math.Max(c.Y1, c.Y2), x1 = Math.Min(c.X1, c.X2), x2 = Math.Max(c.X1, c.X2);
            return y2 <= 135 || x2 <= 95 || (x1 == x2 && (x1 <= 100 || x1 >= 1985) && y2 - y1 > 500) || y1 >= 2855;
        }

        /// <summary>
        /// Yeniden dizgi: sayfanın 'alan' bölgesini (STA çerçevesi hariç) 'konum'a 'olcek' ile çizer.
        /// Yazılar STA yazı tipi yerine Arial ile basılır (okunur Unicode metin).
        /// </summary>
        public static void CizBolge(ICizimYuzeyi g, GprSayfa s, RectangleF alan, PointF konum, float olcek, ICollection<GprYazi> yazilar)
        {
            g.Kaydet();
            g.Tasi(konum.X, konum.Y);
            g.Olcekle(olcek);
            g.Tasi(-alan.Left, -alan.Top);
            g.Kirp(alan);
            foreach (var k in s.Kutular)
                if (k.Alan.IntersectsWith(alan)) KutuCiz(g, k);
            foreach (var c in s.Cizgiler)
            {
                if (CerceveCizgisi(c)) continue;
                if (Math.Max(c.Y1, c.Y2) < alan.Top || Math.Min(c.Y1, c.Y2) > alan.Bottom) continue;
                g.Cizgi(Renk(c.Stil), Math.Max(1.5f, c.Kalinlik / 5f), c.X1, c.Y1, c.X2, c.Y2);
            }
            foreach (var y in yazilar)
            {
                var f = Font(y);
                g.Yazi(y.Metin, new YaziTipi(YaziTipi.Oransal, y.Font == 0 ? f.Boy : f.Boy * 0.92f, f.Kalin), Color.Black, y.X, y.Y);
            }
            g.GeriYukle();
        }

        private static void KutuCiz(ICizimYuzeyi g, GprKutu k)
        {
            if (k.Mod == 3) g.Daire(new PointF(k.X1, k.Y1), k.X2, null, Renk(k.Cerceve), 2f);
            else g.Dikdortgen(k.Alan, k.Mod == 2 ? Renk(k.Dolgu) : null, Renk(k.Cerceve), 2f);
        }

        private static void Icerik(ICizimYuzeyi g, GprSayfa s, bool yerTutucu, float yMin, float yMax)
        {
            foreach (var k in s.Kutular)
                if (k.Alan.Bottom >= yMin && k.Alan.Top <= yMax) KutuCiz(g, k);
            foreach (var c in s.Cizgiler)
            {
                if (Math.Max(c.Y1, c.Y2) < yMin || Math.Min(c.Y1, c.Y2) > yMax) continue;
                g.Cizgi(Renk(c.Stil), Math.Max(1.5f, c.Kalinlik / 5f), c.X1, c.Y1, c.X2, c.Y2);
            }
            if (yerTutucu)
            {
                var f = new YaziTipi(YaziTipi.Arayuz, 26);
                foreach (var r in s.Resimler)
                {
                    if (r.Y > 2500) continue; // STA4CAD yan logo/lisans deseni (101, 108, 110, 124…)
                    var rect = new RectangleF(r.X, r.Y, 520, 300);
                    g.Dikdortgen(rect, Color.FromArgb(18, 70, 130, 180), Color.FromArgb(160, 120, 160, 200), 3f);
                    g.Yazi($"STA4CAD resmi #{r.No}", f, Color.SteelBlue, rect.X + 16, rect.Y + 16);
                    g.Yazi("(henüz çözülmedi)", f, Color.SteelBlue, rect.X + 16, rect.Y + 50);
                }
            }
            bool sta = FontCozucu.StaDosyalari().Count > 0;
            foreach (var y in s.Yazilar)
            {
                if (y.Y < yMin || y.Y > yMax) continue;
                // Coursta.ttf STA baytlarını (1254 konumları) doğrudan çizer; yoksa okunur Unicode metin
                string metin = y.Font != 0 && sta ? y.Ham : y.Metin;
                g.Yazi(metin, Font(y), Color.Black, y.X, y.Y);
            }
        }
    }
}
