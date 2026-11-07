using System.Drawing;
using StatikRaporClaude.Cizim;

namespace StatikRaporClaude.Modern
{
    /// <summary>
    /// STA4CAD kütüphane resimlerinin (GPR "-3" resim kaydı, numara 1–30) BEYKENT çizimleri. Resmin kendisi GPR'de yok;
    /// STA4 bunları programın içinden basar. Burada aynı kavram vektör olarak, etiket ve açıklamayla yeniden çizilir.
    /// Birim mm; (x, y) çizim alanının sol üstü.
    /// </summary>
    public static partial class StaResimleri
    {
        /// <summary>Etkin tema (rapor ve önizleme bunu kullanır).</summary>
        public static StaResimTemasi Tema = StaResimTemasi.Hepsi[0];

        private static Color Beton => Tema.Beton;
        private static Color BetonYan => Tema.BetonYan;
        private static Color BetonUst => Tema.BetonUst;
        private static Color Kenar => Tema.Kenar;
        private static Color Eksen => Tema.Eksen;
        private static Color Vurgu => Tema.Vurgu;
        private static Color Yuk => Tema.Yuk;
        private static Color Donati => Tema.Donati;
        private static float K(float k) => k * Tema.Kalinlik;

        private sealed class Tanim
        {
            public float Gen, Yuk;
            public string Baslik, Aciklama;
            public Action<ICizimYuzeyi, ModernSayfa> Ciz;
        }

        private static readonly Dictionary<int, Tanim> Resimler = Ekler(new Dictionary<int, Tanim>
        {
            [16] = new Tanim { Gen = 132, Yuk = 66, Ciz = Eksantrisite,
                Baslik = "Perde ve kolonlarda eksenel yük kaçıklığı",
                Aciklama = "Üst kolonun yükü N, alttaki perdenin ağırlık merkezine e kadar kaçık iner; perde ek M = N·e momenti alır. Hesap geometrik aksla değil, elemanların gerçek ağırlık merkezleriyle yapılır." },
            [17] = new Tanim { Gen = 150, Yuk = 64, Ciz = RijitBolge,
                Baslik = "Kayma deformasyonu ve rijit bölgeler",
                Aciklama = "Kirişin perde içinde kalan kısmı sonsuz rijit kabul edilir (a); eğilme ve kayma deformasyonu yalnız serbest açıklıkta (Lnet) oluşur. Yatay ötelenmede kiriş çift eğrilikle şekil değiştirir." },
            [29] = new Tanim { Gen = 132, Yuk = 64, Ciz = DegiskenRijit,
                Baslik = "Değişken rijit bölge — perdeye zayıf yönde saplanan kiriş",
                Aciklama = "Kiriş ucu perdenin yerel eğilmesiyle θ kadar döner. Perde düşey plak gibi sonlu elemanlarla çözülür; kiriş ucu tam ankastre yerine bulunan rijitlikte elastik ankastre alınır." },
        });

        public static bool Var(int no) => Resimler.ContainsKey(no);
        public static IEnumerable<int> Numaralar => Resimler.Keys.OrderBy(n => n);

        /// <summary>Çizim + başlık + açıklama toplam yüksekliği (genişlik w'ye sarılmış açıklamayla).</summary>
        public static float Yukseklik(ModernSayfa s, int no, float w)
        {
            var t = Resimler[no];
            return t.Yuk + 5f + s.ParagrafYukseklik(t.Aciklama, ModernSayfa.F(7f), w - 10f) + 3f;
        }

        public static void Ciz(ModernSayfa s, int no, float x, float y, float w)
        {
            var t = Resimler[no];
            var g = s.G;
            // hafif zemin kartı
            float h = Yukseklik(s, no, w);
            g.Dikdortgen(new RectangleF(x, y, w, h - 1f), Tema.KartZemin, Tema.KartCerceve.IsEmpty ? null : Tema.KartCerceve, 0.25f);
            if (!Tema.KartSerit.IsEmpty) g.Dikdortgen(new RectangleF(x, y, 0.8f, h - 1f), Tema.KartSerit);
            g.Kaydet();
            g.Tasi(x + (w - t.Gen) / 2f, y + 1f);
            t.Ciz(g, s);
            g.GeriYukle();
            float ay = y + t.Yuk + 1.5f;
            s.Metin(t.Baslik, ModernSayfa.F(7.6f, true), Tema.Baslik, x + 5f, ay);
            s.Paragraf(t.Aciklama, ModernSayfa.F(7f), Tema.Aciklama, x + 5f, ay + 3.6f, w - 10f);
        }

        // ---------------- yardımcılar ----------------

        private static PointF P(float x, float y) => new PointF(x, y);

        private static void Kutu(ICizimYuzeyi g, PointF[] n, Color dolgu)
        {
            g.Poligon(n, dolgu);
            g.CokluCizgi(n.Append(n[0]).ToArray(), Kenar, K(0.3f));
        }

        /// <summary>Eğik izdüşümlü kutu: ön yüz (x0,y0)-(x1,y1), derinlik vektörü (dx,dy).</summary>
        private static void Prizma(ICizimYuzeyi g, float x0, float y0, float x1, float y1, float dx, float dy)
        {
            Kutu(g, new[] { P(x0, y0), P(x0 + dx, y0 + dy), P(x1 + dx, y0 + dy), P(x1, y0) }, BetonUst);
            Kutu(g, new[] { P(x1, y0), P(x1 + dx, y0 + dy), P(x1 + dx, y1 + dy), P(x1, y1) }, BetonYan);
            Kutu(g, new[] { P(x0, y0), P(x1, y0), P(x1, y1), P(x0, y1) }, Beton);
        }

        private static void EksenCizgisi(ICizimYuzeyi g, float x1, float y1, float x2, float y2)
        {
            // nokta-çizgi: uzun-kısa
            float L = (float)Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            float ux = (x2 - x1) / L, uy = (y2 - y1) / L;
            for (float t = 0; t < L; t += 4.2f)
            {
                float a = t, b = Math.Min(L, t + 2.6f);
                g.Cizgi(Eksen, K(0.3f), x1 + ux * a, y1 + uy * a, x1 + ux * b, y1 + uy * b);
                float c = t + 3.2f;
                if (c < L) g.Cizgi(Eksen, K(0.3f), x1 + ux * c, y1 + uy * c, x1 + ux * Math.Min(L, c + 0.3f), y1 + uy * Math.Min(L, c + 0.3f));
            }
        }

        private static void Ok(ICizimYuzeyi g, Color renk, float kalinlik, float x1, float y1, float x2, float y2, float uc = 2.2f)
        {
            float L = (float)Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            float ux = (x2 - x1) / L, uy = (y2 - y1) / L;
            g.Cizgi(renk, kalinlik, x1, y1, x2 - ux * uc * 0.8f, y2 - uy * uc * 0.8f);
            g.Poligon(new[] { P(x2, y2), P(x2 - ux * uc - uy * uc * 0.42f, y2 - uy * uc + ux * uc * 0.42f), P(x2 - ux * uc + uy * uc * 0.42f, y2 - uy * uc - ux * uc * 0.42f) }, renk);
        }

        private static void Olcu(ICizimYuzeyi g, ModernSayfa s, float x1, float x2, float y, string etiket, Color renk)
        {
            g.Cizgi(renk, 0.25f, x1, y, x2, y);
            g.Cizgi(renk, 0.25f, x1, y - 1.4f, x1, y + 1.4f);
            g.Cizgi(renk, 0.25f, x2, y - 1.4f, x2, y + 1.4f);
            g.Cizgi(renk, 0.4f, x1 - 0.8f, y + 0.8f, x1 + 0.8f, y - 0.8f);
            g.Cizgi(renk, 0.4f, x2 - 0.8f, y + 0.8f, x2 + 0.8f, y - 0.8f);
            s.Metin(etiket, ModernSayfa.F(7f, true), renk, (x1 + x2) / 2f - 15f, y - 3.6f, 30f, Hiza.Orta);
        }

        /// <summary>Etiket + ince kılavuz çizgisi + uçta nokta.</summary>
        private static void Etiket(ICizimYuzeyi g, ModernSayfa s, string m, float tx, float ty, float px, float py, Hiza hiza = Hiza.Sol)
        {
            var f = ModernSayfa.F(6.4f);
            float w = s.Olc(m, f).Width;
            float bx = hiza == Hiza.Sag ? tx - w : tx;
            // kılavuz yazının noktaya bakan ucundan başlar
            float lx = px >= bx + w ? bx + w + 0.8f : px <= bx ? bx - 0.8f : bx + w / 2f;
            float ly = py > ty + 2.6f ? ty + 2.9f : py < ty ? ty - 0.2f : ty + 1.3f;
            if (px > bx && px < bx + w) lx = px;
            g.Cizgi(Tema.Kilavuz, 0.15f, lx, ly, px, py);
            g.Daire(P(px, py), 0.45f, Tema.Kilavuz, null, 0);
            s.Metin(m, f, Tema.Yazi, bx, ty);
        }

        private static void Yay(ICizimYuzeyi g, Color renk, float cx, float cy, float r, float a0, float a1, bool okUcu = true)
        {
            int n = 24;
            var nok = Enumerable.Range(0, n + 1).Select(i => a0 + (a1 - a0) * i / n)
                .Select(a => P(cx + r * (float)Math.Cos(a), cy + r * (float)Math.Sin(a))).ToArray();
            g.CokluCizgi(nok, renk, 0.45f);
            if (okUcu) Ok(g, renk, 0.45f, nok[^3].X, nok[^3].Y, nok[^1].X, nok[^1].Y, 1.8f);
        }

        // ---------------- #16 perde–kolon eksantrisitesi ----------------

        private static void Eksantrisite(ICizimYuzeyi g, ModernSayfa s)
        {
            const float dx = 9f, dy = -5.5f;
            // alttaki perde (uzun) ve üstteki kolon (perdenin sol ucuna oturur)
            Prizma(g, 22, 34, 104, 60, dx, dy);
            Prizma(g, 26, 9, 40, 34, dx * 0.6f, dy * 0.6f);

            float xk = 33f + dx * 0.3f, xp = 63f + dx * 0.5f;   // kolon ve perde ağırlık merkezi (ön yüz + yarı derinlik)
            EksenCizgisi(g, xk, 2, xk, 63);
            EksenCizgisi(g, xp, 22, xp, 63);
            // eksenel yük ve kaçıklık
            Ok(g, Yuk, 0.7f, xk, -1, xk, 9.5f, 2.6f);
            s.Metin("N", ModernSayfa.F(8f, true), Yuk, xk + 1.6f, -1.6f);
            Olcu(g, s, xk, xp, 64.5f, "e", Vurgu);
            // perdede oluşan moment
            Yay(g, Vurgu, xp, 31f, 6.5f, (float)(Math.PI * 1.15), (float)(Math.PI * 1.85));
            s.Metin("M = N · e", ModernSayfa.F(7.4f, true), Vurgu, xp + 6f, 22f);

            Etiket(g, s, "Kolon ağırlık merkezi", 2, 4, xk - 1, 6);
            Etiket(g, s, "Perde ağırlık merkezi", 2, 56, xp - 0.6f, 56);
            Etiket(g, s, "Perde", 2, 46, 23, 47);
        }

        // ---------------- #17 rijit bölge + kayma deformasyonu ----------------

        private static void RijitBolge(ICizimYuzeyi g, ModernSayfa s)
        {
            const float sol0 = 8, sol1 = 34, sag0 = 116, sag1 = 142, ust = 6, alt = 60, h = 4.5f;
            // perdeler
            g.Dikdortgen(new RectangleF(sol0, ust, sol1 - sol0, alt - ust), Beton, Kenar, 0.3f);
            g.Dikdortgen(new RectangleF(sag0, ust, sag1 - sag0, alt - ust), Beton, Kenar, 0.3f);
            float ms = (sol0 + sol1) / 2, mg = (sag0 + sag1) / 2;
            EksenCizgisi(g, ms, ust - 3, ms, alt + 2);
            EksenCizgisi(g, mg, ust - 3, mg, alt + 2);

            // kiriş ekseni: sol uç y0, sağ uç y0+Δ, uçlarda eğim sıfır (çift eğrilik)
            const float y0 = 26f, delta = 9f;
            float Y(float x) { float t = (x - sol1) / (sag0 - sol1); return y0 + delta * (3 * t * t - 2 * t * t * t); }
            var ustK = new List<PointF>(); var altK = new List<PointF>();
            for (int i = 0; i <= 40; i++) { float x = sol1 + (sag0 - sol1) * i / 40f; ustK.Add(P(x, Y(x) - h)); altK.Add(P(x, Y(x) + h)); }
            // şekil değiştirmemiş kiriş (soluk)
            g.Dikdortgen(new RectangleF(sol1, y0 - h, sag0 - sol1, 2 * h), null, Tema.Ikincil, 0.2f);
            g.Poligon(ustK.Concat(Enumerable.Reverse(altK)).ToArray(), Color.FromArgb(70, Eksen));
            g.CokluCizgi(ustK.ToArray(), Eksen, 0.4f);
            g.CokluCizgi(altK.ToArray(), Eksen, 0.4f);
            // kayma deformasyonu: orta bölgede eğik kesit çizgileri
            for (int i = 1; i < 8; i++)
            {
                float x = sol1 + (sag0 - sol1) * i / 8f, yy = Y(x);
                g.Cizgi(Color.FromArgb(140, Eksen), 0.2f, x - 1.1f, yy - h, x + 1.1f, yy + h);
            }
            // rijit bölgeler (perde içi): eksenden yüze
            var rijit = Color.FromArgb(150, Vurgu);
            g.Dikdortgen(new RectangleF(ms, y0 - h, sol1 - ms, 2 * h), rijit, Vurgu, 0.3f);
            g.Dikdortgen(new RectangleF(sag0, y0 + delta - h, mg - sag0, 2 * h), rijit, Vurgu, 0.3f);
            // ölçüler
            Olcu(g, s, ms, sol1, ust - 1.2f + 0f, "a", Vurgu);
            Olcu(g, s, sag0, mg, ust - 1.2f, "a", Vurgu);
            Olcu(g, s, sol1, sag0, alt - 4f, "Lnet", Yuk);
            // öteleme
            s.Metin("Δ", ModernSayfa.F(7.6f, true), Yuk, sag1 + 1.5f, y0 + delta / 2 - 1.5f);
            g.Cizgi(Yuk, 0.25f, sag1 + 0.5f, y0, sag1 + 0.5f, y0 + delta);
            g.Cizgi(Yuk, 0.25f, sag1 - 0.7f, y0, sag1 + 1.7f, y0);
            g.Cizgi(Yuk, 0.25f, sag1 - 0.7f, y0 + delta, sag1 + 1.7f, y0 + delta);
            g.Cizgi(Tema.Ikincil, 0.2f, sag0, y0, sag1 - 0.7f, y0, true);

            Etiket(g, s, "Rijit bölge (sonsuz rijit)", 40, 12, sol1 - 4, y0 - 2);
            Etiket(g, s, "Eğilme + kayma deformasyonu", 50, 45, 66, Y(66) + h - 0.3f);
        }

        // ---------------- #29 değişken rijit bölge (perde düzlem dışı) ----------------

        private static void DegiskenRijit(ICizimYuzeyi g, ModernSayfa s)
        {
            const float x0 = 6, x1 = 74, y0 = 4, y1 = 62, bx = 74, by = 31;
            // perde sonlu eleman ağı
            g.Dikdortgen(new RectangleF(x0, y0, x1 - x0, y1 - y0), Tema.Panel, Kenar, 0.3f);
            // yerel eğilme bölgesi: bağlantının üst köşesinde basınç (mavi), alt köşesinde çekme (turuncu) gerilme lobları
            void Lob(float cy, Color ana)
            {
                for (int k = 5; k >= 1; k--)
                {
                    float rx = 4.4f * k, ry = 2.3f * k;
                    var elips = Enumerable.Range(0, 37).Select(i => i * Math.PI / 18).Where(a => Math.Cos(a) <= 0.001)
                        .Select(a => P(bx + rx * (float)Math.Cos(a), cy + ry * (float)Math.Sin(a))).ToArray();
                    g.Poligon(elips, Color.FromArgb(30 + 32 * (5 - k), ana));
                }
            }
            Lob(by - 4f, Eksen);
            Lob(by + 4f, Vurgu);
            for (float x = x0; x <= x1 + 0.01f; x += 4f) g.Cizgi(Tema.Ikincil, 0.12f, x, y0, x, y1);
            for (float y = y0; y <= y1 + 0.01f; y += 4f) g.Cizgi(Tema.Ikincil, 0.12f, x0, y, x1, y);
            g.Dikdortgen(new RectangleF(x0, y0, x1 - x0, y1 - y0), null, Kenar, 0.35f);
            EksenCizgisi(g, (x0 + x1) / 2, y0 - 2, (x0 + x1) / 2, y1 + 2);

            // kiriş: bağlantıda θ kadar dönmüş
            double teta = -10 * Math.PI / 180;
            float c = (float)Math.Cos(teta), sn = (float)Math.Sin(teta);
            PointF R(float lx, float ly) => P(bx + lx * c - ly * sn, by + lx * sn + ly * c);
            var kiris = new[] { R(0, -4f), R(48, -4f), R(48, 4f), R(0, 4f) };
            g.Poligon(kiris, Beton);
            g.CokluCizgi(kiris.Append(kiris[0]).ToArray(), Kenar, 0.3f);
            // dönme açısı
            g.Cizgi(Tema.Kilavuz, 0.2f, bx, by, bx + 40, by, false);
            EksenCizgisi(g, bx, by, R(44, 0).X, R(44, 0).Y);
            Yay(g, Vurgu, bx, by, 30f, 0f, (float)teta, true);
            s.Metin("θ", ModernSayfa.F(8.4f, true), Vurgu, bx + 31.5f, by - 6.8f);

            Etiket(g, s, "Perde: sonlu eleman ağı (düşey plak)", 2, -1.5f, 12, 8);
            Etiket(g, s, "Basınç", 46, 10, bx - 6, by - 7);
            Etiket(g, s, "Çekme — yerel eğilme", 26, 54, bx - 7, by + 8);
            Etiket(g, s, "Kiriş: elastik ankastre uç", 130, 44, bx + 30, R(30, 4).Y, Hiza.Sag);
        }
    }
}
