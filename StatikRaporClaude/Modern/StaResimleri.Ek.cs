using System.Drawing;
using StatikRaporClaude.Cizim;

namespace StatikRaporClaude.Modern
{
    /// <summary>STA4 kütüphane resimleri: yapı modeli, opsiyon şemaları, yük kuralları, kesme ve sehim hesap şemaları.</summary>
    public static partial class StaResimleri
    {
        private static Dictionary<int, Tanim> Ekler(Dictionary<int, Tanim> d)
        {
            void E(int no, float gen, float yuk, string baslik, string aciklama, Action<ICizimYuzeyi, ModernSayfa> ciz) =>
                d[no] = new Tanim { Gen = gen, Yuk = yuk, Baslik = baslik, Aciklama = aciklama, Ciz = ciz };

            E(30, 150, 62, "Yapı modeli ve serbestlik dereceleri",
                "Her kat döşemesi kendi düzleminde rijit diyafram: katta üç bilinmeyen (δx, δy, θz). Kolon-kiriş uçlarında her düğümde üç bilinmeyen (δz, θx, θy); bir noktada toplam altı serbestlik.", YapiModeli);
            E(1, 96, 62, "Duvar yükü: tuğla ve pencere",
                "Kiriş üstündeki duvar yükü, kat yüksekliğinde tuğla ve pencere bölümlerinin birim yükleriyle ayrı ayrı hesaplanıp toplanır.", DuvarKesiti);
            E(12, 168, 52, "Kiriş donatı boyları (Lpu, Lpa, Lp, Lk)",
                "Pilye ve ilave donatı boyları net açıklığa (Lnet) oranla verilir: Lpu üst pilye kolu, Lpa pilye başlangıcı, Lp alt düz bölüm, Lk mesnet ilavesinin komşu açıklığa uzaması.", KirisDonatiBoylari);
            E(13, 168, 60, "Kiriş sarılma bölgesi ve etriye aralığı (TBDY 7.4.4)",
                "Kolon yüzünden itibaren kiriş yüksekliğinin iki katı (2h) sarılma bölgesidir: özel deprem etriyesi, ilk etriye yüzden en çok 50 mm, aralık d/4, 8·ømin ve 150 mm’nin küçüğü. Bölge dışında TS500 aralığı; kenar mesnette boyuna donatı Lb ile kolona kenetlenir.", KirisEtriye);
            E(14, 165, 44, "Perde ve kolon kesiti (Lu, B, D)",
                "Perde uçlarında Lu uzunluğunda başlık bölgesi kolon gibi sarılır; gövdede yayılı donatı. D ≥ 6·B olan eleman perde, küçük olan kolon kabul edilir.", PerdeKolonKesiti);
            E(15, 100, 48, "Temel kesiti (Ha)",
                "Ters T temel: temel kirişi ve iki yana açılan ampatman. Ha ampatmanın kenar yüksekliği; kalınlık kirişe doğru artar.", TemelKesiti);
            E(2, 150, 36, "Kiriş yük tanımı (A1, A2, G1, G2)",
                "Tip 0: kiriş başından A1 uzaklığında tekil yük (A2 = 0). Yayılı yükte A1 başlangıç, A2 uzunluk; G1/Q1 ve G2/Q2 iki uçtaki sabit/hareketli yük şiddeti (trapez).", KirisYukTanimi);
            E(4, 92, 54, "Kolon yük yönleri (Px, Py, Pz, Mx, My)",
                "Kolon yükleri yerel eksenlerde verilir: Pz eksenel, Px ve Py yatay kuvvet, Mx ve My eğilme momentleri (çift oklu vektör: sağ el kuralı).", KolonYukYonleri);
            E(20, 172, 46, "Kiriş bilgileri (La, Lb, Hg, Rh, uç kodu)",
                "Guseli kirişte La/Lb guse boyu, Hg guse yüksekliği; tabla genişliği B, kiriş yüksekliği D ve kot farkı; eğrisel kirişte Rh yarıçapı. Uç kodu 0 ankastre, −1 mafsallı, −2 çatlamış kesit.", KirisBilgileri);
            E(19, 172, 66, "Ani sehim hesabı",
                "Kesit dengeli donatı oranına göre çekme veya basınç kırılması ile çözülür; çatlamış kesit atalet momentinden etkin atalet Ief bulunur. Zamana bağlı sehim δt = δi + δig·λ.", AniSehim);
            E(5, 172, 64, "Kolon kesme güvenliği (TBDY 7.3.7)",
                "Düğüme bağlanan kiriş uçlarında plastik mafsal oluştuğu kabul edilir; ΣMp = Mpi + Mpj (Mp ≈ 1.4·Mr) kolon uçlarına Bölüm 4 momentleri oranında dağıtılır. Kolon kesmesi Ve = (Ma + Mü)/ln, dayanım Vr = beton + etriye katkısı.", KolonKesme);
            E(21, 172, 66, "Perde kesme güvenliği (TBDY 7.6.2, 7.6.6)",
                "Perde uçlarında uç bölgeleri, tabanda Hcr kritik perde yüksekliği boyunca sıklaştırılmış donatı. Hw/lw > 2 perdelerde tasarım kesmesi Ve = βv·(Mp/Md)·Vd ile büyütülür (βv = 1.5); dayanım gövde donatısı oranı ρsh ile.", PerdeKesme);
            E(7, 112, 58, "Panel statik sonuçları: işaret kuralı",
                "Perde panelinin uç kesitlerinde (I–J üst, K–L alt) kesme T, moment M ve eksenel kuvvet Nz; sol ve sağ kenar kolonlarında Tsol, Msol, Tsağ, Msağ.", PanelIsaret);
            E(27, 172, 60, "Panel moment ve kesme kapasitesi",
                "Panel moment kapasitesi başlık kolonları (Mr1, Mr2) ve gövde (Mrp) katkılarının toplamıdır; deprem yönüne göre basınç tarafı beton katkısını alır. Md ≤ Mr ve Vd ≤ Vr aranır.", PanelKapasite);
            E(8, 150, 54, "Güçlü kolon koşulu (TBDY 7.3.5)",
                "Depremde kiriş uçlarında mafsal oluşur, kolon ise elastik kalmalıdır: düğüme bağlanan kolonların taşıma gücü momentleri toplamı, kirişlerinkinden en az %20 büyük olmalıdır. Kiriş momentleri düğüme aynı yönde, kolon momentleri ters yönde etkir; her iki deprem yönü ayrı kontrol edilir.", GucluKolon);
            E(6, 172, 50, "Kolon-kiriş birleşim kesme güvenliği (TBDY 7.5.2)",
                "Deprem yönünde bir kirişin üst, diğerinin alt donatısı akar; birleşim kesmesi bu çekme kuvvetlerinden kolon kesmesi çıkarılarak bulunur. Sınır, birleşimin kuşatılmış olup olmamasına göre 1.7 veya 1.0·bj·h·√fck.", Birlesim);
            E(9, 172, 54, "Kiriş kesme güvenliği (TBDY 7.4.5)",
                "Kiriş iki ucunda pekleşmeli moment kapasitelerine (Mp ≈ 1.4·Mr) ulaşır; bunların ln açıklığındaki kesmesi düşey yük kesmesine eklenir. Depremin iki yönü ayrı ayrı alınır.", KirisKesme);
            return d;
        }

        // ---------------- yardımcılar ----------------

        private static void Yaz(ModernSayfa s, string m, float x, float y, float pt = 6.6f, bool kalin = false, Color? renk = null, Hiza h = Hiza.Sol)
            => s.Metin(m, ModernSayfa.F(pt, kalin), renk ?? Tema.Yazi, x, y, 0, h);

        private static float Gen(ModernSayfa s, string m, float pt, bool kalin = false) => s.Olc(m, ModernSayfa.F(pt, kalin)).Width;

        /// <summary>Kesir: kesir çizgisi y'de; genişliği döndürür.</summary>
        private static float Kesir(ICizimYuzeyi g, ModernSayfa s, float x, float y, string pay, string payda, float pt = 6.6f)
        {
            float w = Math.Max(Gen(s, pay, pt), Gen(s, payda, pt)) + 1f;
            float a = ModernSayfa.F(pt).Boy;
            Yaz(s, pay, x + w / 2, y - 1.05f * a - 0.35f, pt, false, null, Hiza.Orta);
            g.Cizgi(Tema.Yazi, 0.2f, x, y, x + w, y);
            Yaz(s, payda, x + w / 2, y + 0.55f - 0.32f * a, pt, false, null, Hiza.Orta);
            return w;
        }

        /// <summary>"önce  pay/payda  sonra" biçiminde tek satır formül; kesir çizgisi y'de.</summary>
        private static void Formul(ICizimYuzeyi g, ModernSayfa s, float x, float y, string once, string pay, string payda, string sonra = "", float pt = 6.6f)
        {
            float a = ModernSayfa.F(pt).Boy;
            Yaz(s, once, x, y - 0.68f * a, pt);
            x += Gen(s, once, pt) + 0.6f;
            x += Kesir(g, s, x, y, pay, payda, pt) + 0.6f;
            if (sonra.Length > 0) Yaz(s, sonra, x, y - 0.68f * a, pt);
        }

        private static void Cubuk(ICizimYuzeyi g, float x, float y, float r = 0.55f) => g.Daire(P(x, y), r, Donati, null, 0);

        private static void Tarama(ICizimYuzeyi g, float x, float y, float w, float yon = 1)
        {
            g.Cizgi(Kenar, K(0.35f), x, y, x + w, y);
            for (float k = x; k < x + w; k += 1.6f) g.Cizgi(Kenar, 0.15f, k, y + 1.4f * yon, k + 1.2f, y);
        }

        private static void Kolon(ICizimYuzeyi g, float x, float y0, float y1, float b) =>
            g.Dikdortgen(new RectangleF(x - b / 2, y0, b, y1 - y0), BetonYan, Kenar, K(0.25f));

        /// <summary>Moment oku: saat yönünde ya da tersine ~3/4 daire.</summary>
        private static void Moment(ICizimYuzeyi g, float cx, float cy, float r, bool saat, Color renk)
        {
            Yay(g, renk, cx, cy, r, saat ? (float)(Math.PI * 1.1) : (float)(Math.PI * 1.9), saat ? (float)(Math.PI * 2.4) : (float)(Math.PI * 0.6));
        }

        private static void CiftOk(ICizimYuzeyi g, Color renk, float x1, float y1, float x2, float y2)
        {
            Ok(g, renk, K(0.45f), x1, y1, x2, y2, 2f);
            float L = (float)Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            float ux = (x2 - x1) / L, uy = (y2 - y1) / L;
            Ok(g, renk, K(0.45f), x1, y1, x2 - ux * 1.6f, y2 - uy * 1.6f, 2f);
        }

        private static void DikeyOlcu(ICizimYuzeyi g, ModernSayfa s, float x, float y1, float y2, string etiket, Color renk, bool solda = false)
        {
            g.Cizgi(renk, 0.25f, x, y1, x, y2);
            g.Cizgi(renk, 0.25f, x - 1.4f, y1, x + 1.4f, y1);
            g.Cizgi(renk, 0.25f, x - 1.4f, y2, x + 1.4f, y2);
            g.Cizgi(renk, 0.4f, x - 0.8f, y1 + 0.8f, x + 0.8f, y1 - 0.8f);
            g.Cizgi(renk, 0.4f, x - 0.8f, y2 + 0.8f, x + 0.8f, y2 - 0.8f);
            Yaz(s, etiket, solda ? x - 1.5f : x + 1.5f, (y1 + y2) / 2 - 1.4f, 7f, true, renk, solda ? Hiza.Sag : Hiza.Sol);
        }

        // ---------------- #30 yapı modeli ----------------

        private static void YapiModeli(ICizimYuzeyi g, ModernSayfa s)
        {
            const float x0 = 8, taban = 58, kat = 14, W = 72, dx = 20, dy = -11;
            float Y(int k) => taban - kat * k;
            // arka kolonlar
            for (int i = 0; i <= 3; i++) g.Cizgi(Tema.Ikincil, K(0.3f), x0 + dx + W * i / 3, taban + dy, x0 + dx + W * i / 3, Y(3) + dy);
            for (int k = 1; k <= 3; k++)
            {
                var p = new[] { P(x0, Y(k)), P(x0 + W, Y(k)), P(x0 + W + dx, Y(k) + dy), P(x0 + dx, Y(k) + dy) };
                g.Poligon(p, Color.FromArgb(k == 3 ? 255 : 200, BetonUst));
                g.CokluCizgi(p.Append(p[0]).ToArray(), Kenar, K(0.3f));
                for (int i = 1; i < 3; i++) g.Cizgi(Tema.Ikincil, 0.15f, x0 + W * i / 3, Y(k), x0 + dx + W * i / 3, Y(k) + dy);
            }
            // ön kolonlar
            for (int i = 0; i <= 3; i++)
            {
                float x = x0 + W * i / 3;
                g.Cizgi(Kenar, K(0.45f), x, taban + 3, x, Y(3));
                g.Cizgi(Kenar, K(0.35f), x - 1, taban + 3, x + 1, taban + 3);
                g.Cizgi(Kenar, K(0.3f), x + dx, Y(3) + dy, x + dx, Y(3) + dy);
            }
            for (int i = 0; i <= 3; i++) g.Cizgi(Kenar, K(0.45f), x0 + W, taban + 3, x0 + W, Y(3));
            g.Cizgi(Kenar, K(0.45f), x0 + W + dx, taban + dy + 3, x0 + W + dx, Y(3) + dy);
            // kat düzlemi serbestlikleri (üst kat merkezi)
            float cx = x0 + W / 2 + dx / 2, cy = Y(3) + dy / 2;
            Ok(g, Vurgu, K(0.5f), cx, cy, cx + 12, cy, 2f); Yaz(s, "δx", cx + 12.5f, cy - 2.4f, 7f, true, Vurgu);
            Ok(g, Vurgu, K(0.5f), cx, cy, cx - 6, cy + 4, 2f); Yaz(s, "δy", cx - 12f, cy + 2.6f, 7f, true, Vurgu);
            Yay(g, Vurgu, cx, cy, 3.4f, (float)(Math.PI * 0.2), (float)(Math.PI * 1.6)); Yaz(s, "θz", cx - 2.5f, cy - 8.5f, 7f, true, Vurgu);
            // düğüm serbestlikleri (ön sağ üst köşe)
            float nx = x0 + W, ny = Y(3);
            Ok(g, Yuk, K(0.5f), nx, ny, nx, ny - 11, 2f); Yaz(s, "δz", nx + 1, ny - 13, 7f, true, Yuk);
            CiftOk(g, Yuk, nx, ny, nx + 11, ny); Yaz(s, "θx", nx + 11.5f, ny - 2.2f, 7f, true, Yuk);
            CiftOk(g, Yuk, nx, ny, nx + 6, ny + 5); Yaz(s, "θy", nx + 6.5f, ny + 3.5f, 7f, true, Yuk);
            g.Daire(P(nx, ny), 0.8f, Yuk, null, 0);
            // açıklama
            float lx = 112;
            Yaz(s, "Kat düzlemi (rijit diyafram)", lx, 12, 6.8f, true, Vurgu);
            Yaz(s, "δx, δy öteleme, θz burulma", lx, 15.6f, 6.4f);
            Yaz(s, "her katta 3 bilinmeyen", lx, 18.8f, 6.4f, false, Tema.Aciklama);
            Yaz(s, "Düğüm noktası", lx, 27, 6.8f, true, Yuk);
            Yaz(s, "δz düşey yer değiştirme", lx, 30.6f, 6.4f);
            Yaz(s, "θx, θy dönmeler", lx, 33.8f, 6.4f);
            Yaz(s, "her düğümde 3 bilinmeyen", lx, 37, 6.4f, false, Tema.Aciklama);
        }

        // ---------------- #1 duvar kesiti ----------------

        private static void DuvarKesiti(ICizimYuzeyi g, ModernSayfa s)
        {
            const float x = 30, b = 5, ust = 6, alt = 54;
            // üst ve alt kiriş + döşeme
            foreach (var y in new[] { ust, alt })
            {
                g.Dikdortgen(new RectangleF(4, y - 2.5f, x + b - 4, 2.5f), Beton, Kenar, K(0.25f));
                g.Dikdortgen(new RectangleF(x - 1, y - 2.5f, b + 2, 7f), Beton, Kenar, K(0.25f));
            }
            float pencereAlt = 28;
            // pencere
            g.Dikdortgen(new RectangleF(x + 0.8f, ust + 4.5f, b - 1.6f, pencereAlt - ust - 4.5f), Tema.Panel, Kenar, K(0.2f));
            g.Cizgi(Eksen, K(0.25f), x + b / 2, ust + 4.5f, x + b / 2, pencereAlt);
            // tuğla
            g.Dikdortgen(new RectangleF(x, pencereAlt, b, alt - 2.5f - pencereAlt), Tema.Tugla, Kenar, K(0.25f));
            int sira = 0;
            for (float y = pencereAlt + 1.8f; y < alt - 2.5f; y += 1.8f, sira++)
            {
                g.Cizgi(Kenar, 0.12f, x, y, x + b, y);
                float ox = sira % 2 == 0 ? x + b / 2 : x + b / 4;
                g.Cizgi(Kenar, 0.12f, ox, y - 1.8f, ox, y);
            }
            DikeyOlcu(g, s, x + b + 6, ust + 4.5f, pencereAlt, "hp", Eksen);
            DikeyOlcu(g, s, x + b + 6, pencereAlt, alt - 2.5f, "hd", Eksen);
            Etiket(g, s, "Pencere (t/m²)", 52, 14, x + b - 1, 16);
            Etiket(g, s, "Tuğla duvar (t/m²)", 52, 38, x + b - 1, 40);
            Etiket(g, s, "Döşeme + kiriş", 2, 58, 12, alt - 1);
            Yaz(s, "q = γtuğla · hd + γpencere · hp", 50, 48, 6.6f, true, Vurgu);
        }

        // ---------------- #12 / #13 kiriş açılımı ----------------

        private static void IkiAcklikKiris(ICizimYuzeyi g, float[] kolon, float y0, float y1)
        {
            g.Dikdortgen(new RectangleF(kolon[0] - 3, y0, kolon[^1] - kolon[0] + 6, y1 - y0), Beton, Kenar, K(0.25f));
            foreach (var k in kolon)
            {
                g.Dikdortgen(new RectangleF(k - 3, y1, 6, 10), BetonYan, Kenar, K(0.25f));
                EksenCizgisi(g, k, y0 - 9, k, y1 + 22);
            }
        }

        private static void KirisDonatiBoylari(ICizimYuzeyi g, ModernSayfa s)
        {
            float[] k = { 10, 86, 158 };
            const float y0 = 13, y1 = 23;
            IkiAcklikKiris(g, k, y0, y1);
            float u = y0 + 1.6f, a = y1 - 1.6f;
            // montaj ve alt düz
            g.CokluCizgi(new[] { P(8.5f, u + 3), P(8.5f, u), P(159.5f, u), P(159.5f, u + 3) }, Donati, K(0.45f));
            g.CokluCizgi(new[] { P(8.5f, a - 3), P(8.5f, a), P(159.5f, a), P(159.5f, a - 3) }, Eksen, K(0.45f));
            // 1. açıklıkta pilye
            g.CokluCizgi(new[] { P(13, u + 0.8f), P(22, u + 0.8f), P(28, a - 0.8f), P(60, a - 0.8f), P(66, u + 0.8f), P(108, u + 0.8f) }, Vurgu, K(0.55f));
            // 2. mesnet ilavesi
            g.Cizgi(Donati, K(0.55f), 68, u + 1.8f, 112, u + 1.8f);
            Olcu(g, s, 13, 22, y0 - 4, "Lpu", Vurgu);
            Olcu(g, s, 86, 112, y0 - 4, "Lk", Donati);
            Olcu(g, s, 13, 28, y1 + 5, "Lpa", Vurgu);
            Olcu(g, s, 28, 60, y1 + 5, "Lp", Vurgu);
            Olcu(g, s, 13, 83, y1 + 12, "Lnet1", Yuk);
            Olcu(g, s, 89, 155, y1 + 12, "Lnet2", Yuk);
            Olcu(g, s, 10, 86, y1 + 20, "L1", Eksen);
            Olcu(g, s, 86, 158, y1 + 20, "L2", Eksen);
            DikeyOlcu(g, s, 132, y0, y1 - 1.6f, "d", Yuk);
            Etiket(g, s, "pilye", 36, 5, 25, u + 3.5f);
            Etiket(g, s, "mesnet ilavesi", 118, 5, 100, u + 1.8f);
        }


        // ---------------- #14 perde / kolon kesiti ----------------

        private static void PerdeKolonKesiti(ICizimYuzeyi g, ModernSayfa s)
        {
            const float x0 = 6, y0 = 12, L = 104, B = 12, lu = 18;
            g.Dikdortgen(new RectangleF(x0, y0, L, B), Beton, Kenar, K(0.3f));
            g.Dikdortgen(new RectangleF(x0 + 1.2f, y0 + 1.2f, L - 2.4f, B - 2.4f), null, Eksen, K(0.25f));
            foreach (var bx in new[] { x0 + 1.6f, x0 + L - lu - 1.6f })
            {
                g.Dikdortgen(new RectangleF(bx, y0 + 1.6f, lu, B - 3.2f), Color.FromArgb(60, Vurgu), Vurgu, K(0.3f));
                for (int i = 0; i < 3; i++) { Cubuk(g, bx + 2 + i * (lu - 4) / 2, y0 + 3.2f); Cubuk(g, bx + 2 + i * (lu - 4) / 2, y0 + B - 3.2f); }
            }
            for (float x = x0 + lu + 8; x < x0 + L - lu - 6; x += 9) { Cubuk(g, x, y0 + 3.2f, 0.45f); Cubuk(g, x, y0 + B - 3.2f, 0.45f); }
            Olcu(g, s, x0 + 1.6f, x0 + 1.6f + lu, y0 + B + 6, "Lu", Vurgu);
            Olcu(g, s, x0 + L - lu - 1.6f, x0 + L - 1.6f, y0 + B + 6, "Lu", Vurgu);
            Olcu(g, s, x0, x0 + L, y0 + B + 14, "D (perde boyu)", Yuk);
            DikeyOlcu(g, s, x0 + L + 4, y0, y0 + B, "B", Yuk);
            Yaz(s, "PERDE  D ≥ 6·B", x0, 4, 6.8f, true, Yuk);
            // kolon
            const float kx = 136, ky = 4, kb = 13, kd = 34;
            g.Dikdortgen(new RectangleF(kx, ky, kb, kd), Beton, Kenar, K(0.3f));
            g.Dikdortgen(new RectangleF(kx + 1.2f, ky + 1.2f, kb - 2.4f, kd - 2.4f), null, Eksen, K(0.25f));
            for (int i = 0; i < 5; i++) { Cubuk(g, kx + 3, ky + 3 + i * (kd - 6) / 4); Cubuk(g, kx + kb - 3, ky + 3 + i * (kd - 6) / 4); }
            Cubuk(g, kx + kb / 2, ky + 3); Cubuk(g, kx + kb / 2, ky + kd - 3);
            DikeyOlcu(g, s, kx + kb + 4, ky, ky + kd, "D", Yuk);
            Olcu(g, s, kx, kx + kb, ky + kd + 4, "B", Yuk);
            Yaz(s, "KOLON  D < 6·B", kx - 4, ky + kd + 6.5f, 6.8f, true, Yuk);
        }

        // ---------------- #15 temel kesiti ----------------

        private static void TemelKesiti(ICizimYuzeyi g, ModernSayfa s)
        {
            const float cx = 46, w = 24, ust = 4, gov = 30, ampY = 34, alt = 44, L = 80;
            var p = new[] { P(cx - w / 2, ust), P(cx + w / 2, ust), P(cx + w / 2, gov), P(cx + L / 2, ampY), P(cx + L / 2, alt), P(cx - L / 2, alt), P(cx - L / 2, ampY), P(cx - w / 2, gov) };
            g.Poligon(p, Beton);
            g.CokluCizgi(p.Append(p[0]).ToArray(), Kenar, K(0.35f));
            // etriye ve donatı
            g.Dikdortgen(new RectangleF(cx - w / 2 + 1.6f, ust + 1.6f, w - 3.2f, alt - ust - 3.2f), null, Eksen, K(0.3f));
            foreach (var x in new[] { -8f, 0f, 8f }) { Cubuk(g, cx + x, ust + 3.4f); Cubuk(g, cx + x, alt - 3.4f); }
            Cubuk(g, cx - 8, 20); Cubuk(g, cx + 8, 20);
            g.CokluCizgi(new[] { P(cx - L / 2 + 1.6f, ampY + 2), P(cx - L / 2 + 1.6f, alt - 1.6f), P(cx + L / 2 - 1.6f, alt - 1.6f), P(cx + L / 2 - 1.6f, ampY + 2) }, Donati, K(0.4f));
            foreach (var x in new[] { -34f, -27f, -20f, 20f, 27f, 34f }) Cubuk(g, cx + x, alt - 3.2f, 0.45f);
            Tarama(g, cx - L / 2 - 4, alt, L + 8);
            DikeyOlcu(g, s, cx + L / 2 + 4, ampY, alt, "Ha", Vurgu);
            Etiket(g, s, "Temel kirişi", 2, 6, cx - w / 2, 10);
            Etiket(g, s, "Ampatman", 2, 30, cx - L / 2 + 6, ampY + 2);
        }

        // ---------------- #2 kiriş yük tanımı ----------------

        private static void KirisYukTanimi(ICizimYuzeyi g, ModernSayfa s)
        {
            const float y = 22;
            // tekil
            g.Cizgi(Eksen, K(0.9f), 4, y, 64, y);
            Ok(g, Yuk, K(0.6f), 26, y - 13, 26, y - 0.6f, 2.4f);
            Yaz(s, "G1, Q1", 27, y - 14, 6.8f, true, Yuk);
            Olcu(g, s, 4, 26, y + 5, "A1", Eksen);
            Yaz(s, "A2 = 0", 40, y + 2, 6.6f, false, Tema.Aciklama);
            Yaz(s, "Tip 0 — tekil yük", 4, 1, 7f, true, Vurgu);
            // yayılı trapez
            const float x0 = 82, a1 = 96, a2 = 140;
            g.Cizgi(Eksen, K(0.9f), x0, y, 148, y);
            var trap = new[] { P(a1, y - 0.6f), P(a1, y - 6), P(a2, y - 13), P(a2, y - 0.6f) };
            g.Poligon(trap, Color.FromArgb(110, Vurgu));
            g.CokluCizgi(trap, Vurgu, K(0.35f));
            for (float x = a1 + 5; x < a2; x += 6) { float h = 6 + 7 * (x - a1) / (a2 - a1); Ok(g, Yuk, K(0.3f), x, y - h, x, y - 0.8f, 1.4f); }
            Yaz(s, "G1, Q1", a1 - 1, y - 11, 6.8f, true, Yuk, Hiza.Sag);
            Yaz(s, "G2, Q2", a2 + 1, y - 16, 6.8f, true, Yuk);
            Olcu(g, s, x0, a1, y + 5, "A1", Eksen);
            Olcu(g, s, a1, a2, y + 5, "A2", Eksen);
            Yaz(s, "Yayılı yük — trapez", x0, 1, 7f, true, Vurgu);
        }

        // ---------------- #4 kolon yük yönleri ----------------

        private static void KolonYukYonleri(ICizimYuzeyi g, ModernSayfa s)
        {
            Prizma(g, 34, 14, 46, 46, 8, -5);
            float cx = 44, cy = 11.5f;
            Ok(g, Yuk, K(0.6f), cx, cy - 11, cx, cy, 2.4f); Yaz(s, "Pz", cx + 1.2f, cy - 13, 7.4f, true, Yuk);
            Ok(g, Yuk, K(0.6f), cx, cy, cx + 18, cy, 2.4f); Yaz(s, "Px", cx + 18.5f, cy - 2.6f, 7.4f, true, Yuk);
            Ok(g, Yuk, K(0.6f), cx, cy, cx - 11, cy + 9, 2.4f); Yaz(s, "Py", cx - 17, cy + 7, 7.4f, true, Yuk);
            CiftOk(g, Vurgu, cx, cy + 6, cx + 16, cy + 6); Yaz(s, "My", cx + 16.5f, cy + 4, 7.4f, true, Vurgu);
            CiftOk(g, Vurgu, cx - 2, cy + 8, cx - 12, cy + 16); Yaz(s, "Mx", cx - 18, cy + 15, 7.4f, true, Vurgu);
            Yaz(s, "kuvvet", 66, 34, 6.4f, false, Yuk); g.Cizgi(Yuk, K(0.6f), 60, 35.4f, 65, 35.4f);
            Yaz(s, "moment (çift ok)", 66, 39, 6.4f, false, Vurgu); g.Cizgi(Vurgu, K(0.6f), 60, 40.4f, 65, 40.4f);
            Tarama(g, 30, 46, 30);
        }

        // ---------------- #20 kiriş bilgileri ----------------

        private static void KirisBilgileri(ICizimYuzeyi g, ModernSayfa s)
        {
            // guseli kiriş
            var p = new[] { P(6, 6), P(58, 6), P(58, 22), P(52, 22), P(46, 16), P(18, 16), P(12, 22), P(6, 22) };
            g.Poligon(p, Beton); g.CokluCizgi(p.Append(p[0]).ToArray(), Kenar, K(0.3f));
            Kolon(g, 6, 2, 26, 3); Kolon(g, 58, 2, 26, 3);
            Yaz(s, "I", 4, -1.5f, 6.8f, true); Yaz(s, "J", 57, -1.5f, 6.8f, true);
            Olcu(g, s, 7.5f, 18, 28, "La", Vurgu); Olcu(g, s, 46, 56.5f, 28, "Lb", Vurgu); Olcu(g, s, 6, 58, 36, "L", Eksen);
            DikeyOlcu(g, s, 20, 16, 22, "Hg", Vurgu);
            // tabla kesiti ve kot farkı
            var t = new[] { P(72, 10), P(92, 10), P(92, 13), P(86, 13), P(86, 24), P(78, 24), P(78, 13), P(72, 13) };
            g.Poligon(t, Beton); g.CokluCizgi(t.Append(t[0]).ToArray(), Kenar, K(0.3f));
            DikeyOlcu(g, s, 96, 10, 24, "D", Yuk); Olcu(g, s, 78, 86, 29, "B", Yuk);
            CiftOk(g, Vurgu, 68, 14, 68, 4); Yaz(s, "Kot ±", 68, -0.5f, 6.4f, true, Vurgu, Hiza.Orta);
            // eğrisel kiriş (planda)
            var yay = Enumerable.Range(0, 25).Select(i => (float)(Math.PI * (0.2 + 0.6 * i / 24))).Select(a => P(83 + 16 * (float)Math.Cos(a), 26 + 14 * (float)Math.Sin(a))).ToArray();
            g.CokluCizgi(yay, Kenar, K(0.9f));
            g.Cizgi(Tema.Ikincil, 0.2f, yay[0].X, yay[0].Y, yay[^1].X, yay[^1].Y, true);
            DikeyOlcu(g, s, 83, 34.7f, 40, "Rh", Vurgu);
            // uç kodları
            float x0 = 112, x1 = 168;
            void Uc(float y, string kod, string ad, Action<float, float> uc)
            {
                g.Cizgi(Eksen, K(0.9f), x0, y, x1, y); uc(x0, -1); uc(x1, 1);
                Yaz(s, $"La = Lb = {kod}  ·  {ad}", (x0 + x1) / 2, y - 4.4f, 6.4f, true, Yuk, Hiza.Orta);
            }
            Uc(9, "0", "ankastre", (x, yon) => { g.Cizgi(Kenar, K(0.4f), x, 6, x, 12); for (float k = 6; k < 12; k += 1.2f) g.Cizgi(Kenar, 0.15f, x, k, x + yon * 1, k + 0.8f); });
            Uc(23, "−1", "mafsallı", (x, yon) => g.Daire(P(x - yon * 0.9f, 23), 0.9f, Tema.KartZemin, Kenar, K(0.25f)));
            Uc(37, "−2", "çatlamış kesit", (x, yon) => g.Daire(P(x - yon * 1.4f, 37), 1.3f, Donati, null, 0));
        }

        // ---------------- #19 ani sehim ----------------

        private static void AniSehim(ICizimYuzeyi g, ModernSayfa s)
        {
            const float x0 = 6, y0 = 8, bw = 22, h = 44;
            g.Dikdortgen(new RectangleF(x0, y0, bw, h), Beton, Kenar, K(0.3f));
            g.Dikdortgen(new RectangleF(x0, y0, bw, 13), Color.FromArgb(90, Eksen));
            g.Dikdortgen(new RectangleF(x0 + 1.5f, y0 + 1.5f, bw - 3, h - 3), null, Eksen, K(0.25f));
            for (int i = 0; i < 5; i++) { Cubuk(g, x0 + 3.5f + i * 3.75f, y0 + 3.2f, 0.5f); Cubuk(g, x0 + 3.5f + i * 3.75f, y0 + h - 3.2f, 0.6f); }
            Yaz(s, "As", x0 + bw / 2, y0 + h - 8, 6.4f, true, Donati, Hiza.Orta);
            Olcu(g, s, x0, x0 + bw, y0 + h + 5, "bw", Yuk);
            DikeyOlcu(g, s, x0 + bw + 4, y0, y0 + h - 3.2f, "d", Yuk);
            // şekil değiştirme
            float sx = 40, xn = 13;
            g.Cizgi(Kenar, K(0.3f), sx, y0, sx, y0 + h - 3);
            var e = new[] { P(sx, y0), P(sx + 6, y0), P(sx - 9, y0 + h - 3), P(sx, y0 + h - 3) };
            g.Poligon(e, Color.FromArgb(60, Eksen)); g.CokluCizgi(e, Eksen, K(0.35f));
            Yaz(s, "εcu = 0.003", sx + 1, y0 - 4, 6.4f, true, Eksen);
            Yaz(s, "εs", sx - 11, y0 + h - 2, 6.4f, true, Eksen);
            DikeyOlcu(g, s, sx - 4, y0, y0 + xn, "X", Yuk, true);
            // gerilme bloğu
            float gx = 62;
            g.Cizgi(Kenar, K(0.3f), gx, y0, gx, y0 + h - 3);
            g.Dikdortgen(new RectangleF(gx, y0, 8, xn * 0.85f), Color.FromArgb(150, Vurgu), Vurgu, K(0.3f));
            Yaz(s, "0.85 fcd", gx + 1, y0 - 4, 6.4f, true, Vurgu);
            DikeyOlcu(g, s, gx - 3, y0, y0 + xn * 0.85f, "a", Vurgu, true);
            Ok(g, Donati, K(0.45f), gx, y0 + h - 3, gx - 9, y0 + h - 3, 1.8f);
            Yaz(s, "As·fyd", gx - 10, y0 + h - 1.5f, 6.2f, false, Donati, Hiza.Sag);
            // formüller
            float fx = 82, y = 8;
            Formul(g, s, fx, y + 2, "ρb =", "0.85·0.85·fcd", "fyd", "·  6000 / (6000 + fyd)");
            Formul(g, s, fx + 64, y + 2, "X =", "6000·d", "6000 + fyd");
            y += 10;
            Yaz(s, "ρ < ρb  çekme kırılması", fx, y, 6.4f, true, Vurgu);
            Formul(g, s, fx, y + 7, "a =", "As·fyd", "0.85·fcd·bw");
            Yaz(s, "ρ > ρb  basınç kırılması", fx + 44, y, 6.4f, true, Vurgu);
            Yaz(s, "(0.85·fcd·bw)·a² + 6000·As·a − 6000·As·0.85·d = 0", fx + 44, y + 4.2f, 6.2f);
            y += 15;
            Yaz(s, "Çatlamış kesit atalet momenti", fx, y, 6.4f, true, Yuk);
            Yaz(s, "Mcr = 2.5·fctd·Ic / y      Ic = bw·d³ / 12", fx, y + 3.6f, 6.4f);
            Yaz(s, "Icr = bw·X³ / 3 + (Es / Ec)·As·(d − X)²", fx, y + 7.2f, 6.4f);
            Yaz(s, "Ief = (Mcr/Mmax)³·Ic + [1 − (Mcr/Mmax)³]·Icr", fx, y + 10.8f, 6.4f);
            Yaz(s, "δi ani sehim · δt zamana bağlı sehim", fx, y + 16, 6.4f, true, Vurgu);
            Formul(g, s, fx, y + 23.5f, "λ =", "2", "1 + 50·ρ'", "     δt = δi + δig·λ");
        }


        // ---------------- #7 panel işaret kuralı ----------------

        private static void PanelIsaret(ICizimYuzeyi g, ModernSayfa s)
        {
            const float x0 = 14, x1 = 98, y0 = 8, y1 = 50;
            g.Dikdortgen(new RectangleF(x0, y0, x1 - x0, y1 - y0), Tema.Panel, Kenar, K(0.3f));
            Kolon(g, x0, y0 - 3, y1 + 3, 4); Kolon(g, x1, y0 - 3, y1 + 3, 4);
            Yaz(s, "I", x0 - 1, y0 - 8, 7f, true); Yaz(s, "J", x1 - 1, y0 - 8, 7f, true);
            Yaz(s, "K", x0 - 1, y1 + 3.5f, 7f, true); Yaz(s, "L", x1 - 1, y1 + 3.5f, 7f, true);
            float cx = (x0 + x1) / 2;
            Ok(g, Yuk, K(0.5f), cx - 9, y0 + 3, cx - 15, y0 + 3, 1.8f); Yaz(s, "Tü", cx - 21, y0 + 1, 6.6f, true, Yuk);
            Moment(g, cx + 3, y0 + 4, 3, true, Vurgu); Yaz(s, "Mü", cx + 7, y0 + 2, 6.6f, true, Vurgu);
            Ok(g, Yuk, K(0.5f), cx, 22, cx, 34, 2f); Yaz(s, "Nz", cx + 1.5f, 26, 6.6f, true, Yuk);
            Ok(g, Yuk, K(0.5f), x0 + 5, 34, x0 + 5, 22, 1.8f); Yaz(s, "Tsol", x0 + 6, 20, 6.4f, true, Yuk);
            Moment(g, x0 + 11, 32, 3, false, Vurgu); Yaz(s, "Msol", x0 + 9, 37, 6.4f, true, Vurgu);
            Ok(g, Yuk, K(0.5f), x1 - 5, 22, x1 - 5, 34, 1.8f); Yaz(s, "Tsağ", x1 - 6, 20, 6.4f, true, Yuk, Hiza.Sag);
            Moment(g, x1 - 11, 32, 3, true, Vurgu); Yaz(s, "Msağ", x1 - 9, 37, 6.4f, true, Vurgu, Hiza.Sag);
            Moment(g, cx - 4, y1 - 4, 3, false, Vurgu); Yaz(s, "Ma", cx - 12, y1 - 7, 6.6f, true, Vurgu);
            Ok(g, Yuk, K(0.5f), cx + 4, y1 - 3, cx + 10, y1 - 3, 1.8f); Yaz(s, "Ta", cx + 11, y1 - 5, 6.6f, true, Yuk);
        }

        // ---------------- #27 panel kapasite ----------------

        private static void PanelKapasite(ICizimYuzeyi g, ModernSayfa s)
        {
            const float x0 = 8, x1 = 64, y0 = 4, y1 = 36;
            g.Dikdortgen(new RectangleF(x0, y0, x1 - x0, y1 - y0), Tema.Panel, Kenar, K(0.3f));
            Kolon(g, x0 + 2, y0, y1, 4); Kolon(g, x1 - 2, y0, y1, 4);
            Ok(g, Yuk, K(0.5f), x0 + 2, 26, x0 + 2, 12, 1.8f); Yaz(s, "Nc1", x0 + 5, 8, 6.4f, true, Yuk);
            Ok(g, Yuk, K(0.5f), x1 - 2, 12, x1 - 2, 26, 1.8f); Yaz(s, "Nc2", x1 - 5, 8, 6.4f, true, Yuk, Hiza.Sag);
            Moment(g, (x0 + x1) / 2, 30, 3.4f, true, Vurgu); Yaz(s, "Mp", (x0 + x1) / 2 + 4.5f, 26, 6.6f, true, Vurgu);
            // plan
            const float py = 44;
            g.Dikdortgen(new RectangleF(x0, py, x1 - x0, 4), Beton, Kenar, K(0.25f));
            g.Dikdortgen(new RectangleF(x0, py - 1.5f, 6, 7), Color.FromArgb(70, Vurgu), Vurgu, K(0.3f));
            g.Dikdortgen(new RectangleF(x1 - 6, py - 1.5f, 6, 7), Color.FromArgb(70, Vurgu), Vurgu, K(0.3f));
            Yaz(s, "Mr1 (As1, Ac1)", x0, py - 5.5f, 6.2f, true, Vurgu);
            Yaz(s, "Mr2 (As2, Ac2)", x1, py - 5.5f, 6.2f, true, Vurgu, Hiza.Sag);
            Yaz(s, "Mrp (As, Ac)", (x0 + x1) / 2, py + 4.5f, 6.2f, true, Yuk, Hiza.Orta);
            Olcu(g, s, x0 + 3, (x0 + x1) / 2, py + 12, "e", Eksen); Olcu(g, s, (x0 + x1) / 2, x1 - 3, py + 12, "e", Eksen);
            float fx = 74;
            Ok(g, Yuk, K(0.45f), fx, 6, fx + 7, 6, 1.6f); Yaz(s, "Ve", fx, 1.6f, 6f, true, Yuk);
            Yaz(s, "Mr1 = As1·fyd·e,   Mr2 = 0.85·Ac2·fcd + As2·fyd·e", fx + 10, 4.2f, 6.4f);
            Ok(g, Yuk, K(0.45f), fx + 7, 13, fx, 13, 1.6f); Yaz(s, "Ve", fx, 8.6f, 6f, true, Yuk);
            Yaz(s, "Mr1 = 0.85·Ac1·fcd + As1·fyd·e,   Mr2 = As2·fyd·e", fx + 10, 11.2f, 6.4f);
            Yaz(s, "Md = (Mc1 + Nc1·e) + (Mc2 + Nc2·e) + Mp", fx, 20, 6.6f, true);
            Yaz(s, "Mr = Mrp + Mr1 + Mr2 > Md", fx, 25, 6.6f, true, Vurgu);
            Formul(g, s, fx, 35, "ρsh =", "2·Asws", "Ach", "· Lw / s          Vr = 0.22·Ach·fcd > Vd");
            Yaz(s, "Vr = 0.65·fctd·Ac + ρsh·Ach·fyd > Vd", fx, 42, 6.6f, true, Vurgu);
        }

    }
}
