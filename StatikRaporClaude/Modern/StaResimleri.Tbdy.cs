using System.Drawing;
using StatikRaporClaude.Cizim;

namespace StatikRaporClaude.Modern
{
    /// <summary>
    /// Deprem tasarımı resimleri, TBDY 2018 Bölüm 7 metnine göre (C:\Sta4V14\tbdy2018.pdf): 7.3.5 güçlü kolon (Denk. 7.3),
    /// 7.3.7 kolon kesmesi (7.5, 7.6), 7.4.4 kiriş sarılma bölgesi, 7.4.5 kiriş kesmesi (7.9), 7.5.2 birleşim kesmesi (7.11–7.13),
    /// 7.6.2 kritik perde yüksekliği (7.15), 7.6.6 perde kesmesi (7.16).
    /// </summary>
    public static partial class StaResimleri
    {
        /// <summary>Plastik mafsal: dolu daire.</summary>
        private static void Mafsal(ICizimYuzeyi g, float x, float y) => g.Daire(P(x, y), 1.1f, Vurgu, Tema.KartZemin, 0.3f);

        private static void DepremOku(ICizimYuzeyi g, ModernSayfa s, float x, float y, bool sagaDogru)
        {
            Ok(g, Yuk, K(0.5f), sagaDogru ? x : x + 14, y, sagaDogru ? x + 14 : x, y, 2f);
            Yaz(s, "deprem", x + 7, y - 4.2f, 6f, false, Tema.Aciklama, Hiza.Orta);
        }

        // ---------------- #13 kiriş sarılma bölgesi (TBDY 7.4.4) ----------------

        private static void KirisEtriye(ICizimYuzeyi g, ModernSayfa s)
        {
            float[] k = { 10, 86, 158 };
            const float y0 = 16, y1 = 26, h = y1 - y0, kb = 3;
            IkiAcklikKiris(g, k, y0, y1);
            float u = y0 + 1.6f, a = y1 - 1.6f;
            // sarılma bölgeleri: kolon yüzünden 2h
            // (kolon yüzü, yön): yüzden açıklığa doğru 2h
            var bolgeler = new List<(float Yuz, int Yon)>();
            foreach (var x in k)
            {
                if (x > k[0]) bolgeler.Add((x - kb, -1));
                if (x < k[^1]) bolgeler.Add((x + kb, 1));
            }
            foreach (var (yuz, yon) in bolgeler)
                g.Dikdortgen(new RectangleF(Math.Min(yuz, yuz + yon * 2 * h), y0 + 0.2f, 2 * h, h - 0.4f), Color.FromArgb(55, Vurgu));
            g.Cizgi(Donati, K(0.45f), 8.5f, u, 159.5f, u);
            g.Cizgi(Eksen, K(0.45f), 8.5f, a, 159.5f, a);
            g.CokluCizgi(new[] { P(159.5f, u), P(159.5f, y1 + 8) }, Donati, K(0.45f));
            // etriyeler: bölgede sık (ilk etriye yüzden ≤ 50 mm), dışında TS500 aralığı
            foreach (var (yuz, yon) in bolgeler)
                for (float d = 1; d <= 2 * h; d += 1.6f) g.Cizgi(Vurgu, K(0.3f), yuz + yon * d, u, yuz + yon * d, a);
            for (int i = 0; i < 2; i++)
            {
                float b0 = k[i] + kb + 2 * h, b1 = k[i + 1] - kb - 2 * h;
                for (float x = b0 + 4; x < b1 - 1; x += 4.5f) g.Cizgi(Kenar, K(0.3f), x, u, x, a);
            }
            Olcu(g, s, k[0] + kb, k[0] + kb + 2 * h, y0 - 5, "2h  sarılma bölgesi", Vurgu);
            Olcu(g, s, k[1] + kb + 2 * h + 4, k[1] + kb + 2 * h + 8.5f, y0 - 5, "s (TS500)", Kenar);
            Olcu(g, s, k[1] - kb - 1, k[1] - kb, y1 + 5, "", Vurgu);
            Yaz(s, "s0 ≤ 50 mm", k[1] - kb - 1.5f, y1 + 2.4f, 6f, true, Vurgu, Hiza.Sag);
            DikeyOlcu(g, s, 164, y0, y1, "h", Yuk);
            DikeyOlcu(g, s, 168, y1, y1 + 8, "Lb", Donati);
            Olcu(g, s, 10, 86, y1 + 20, "L1", Eksen);
            Olcu(g, s, 86, 158, y1 + 20, "L2", Eksen);
            Yaz(s, "Sarılma bölgesinde etriye aralığı:  s ≤ min( d/4 ;  8·ømin ;  150 mm ),  ø ≥ 8 mm, özel deprem etriyesi", 10, y1 + 25.5f, 6.4f, true, Yuk);
        }

        // ---------------- #5 kolon kesmesi (TBDY 7.3.7) ----------------

        private static void KolonKesme(ICizimYuzeyi g, ModernSayfa s)
        {
            const float cx = 22, ust = 12, alt = 54;
            Kolon(g, cx, 3, 63, 4);
            foreach (var y in new[] { ust, alt }) g.Dikdortgen(new RectangleF(cx - 18, y - 2.2f, 36, 4.4f), Beton, Kenar, K(0.25f));
            // üst düğüm: kiriş uçlarında plastik mafsallar ve ΣMp
            Mafsal(g, cx - 4.5f, ust); Mafsal(g, cx + 4.5f, ust);
            Moment(g, cx - 11, ust, 3, true, Vurgu); Moment(g, cx + 11, ust, 3, true, Vurgu);
            Yaz(s, "Mpi", cx - 17, ust - 8, 6.4f, true, Vurgu); Yaz(s, "Mpj", cx + 9, ust - 8, 6.4f, true, Vurgu);
            Mafsal(g, cx - 4.5f, alt); Mafsal(g, cx + 4.5f, alt);
            // kolon uç momentleri ve kesme
            Moment(g, cx, ust + 7, 3, false, Yuk); Yaz(s, "Mü", cx + 3.6f, ust + 4.6f, 6.6f, true, Yuk);
            Moment(g, cx, alt - 7, 3, false, Yuk); Yaz(s, "Ma", cx + 3.6f, alt - 9.4f, 6.6f, true, Yuk);
            Ok(g, Yuk, K(0.5f), cx - 2.4f, ust + 13, cx - 9, ust + 13, 1.8f); Yaz(s, "Ve", cx - 10, ust + 13.6f, 6.4f, true, Yuk, Hiza.Sag);
            Ok(g, Yuk, K(0.5f), cx + 2.4f, alt - 13, cx + 9, alt - 13, 1.8f); Yaz(s, "Ve", cx + 10, alt - 16.4f, 6.4f, true, Yuk);
            DikeyOlcu(g, s, cx - 15, ust + 2.2f, alt - 2.2f, "ln", Eksen, true);
            // dağıtım formülleri
            float fx = 46;
            Yaz(s, "ΣMp = Mpi + Mpj    (Mp ≈ 1.4·Mr)", fx, 6, 6.6f, true, Vurgu);
            Formul(g, s, fx, 19, "Mü =", "Mhu", "Mhu + Mha", "· ΣMp");
            Formul(g, s, fx, 31, "Ma =", "Mha", "Mhu + Mha", "· ΣMp");
            Formul(g, s, fx, 45, "Ve =", "Ma + Mü", "ln", "      TBDY Denk. 7.5");
            Yaz(s, "ΣMp, kolon uçlarına Bölüm 4", fx, 51, 6f, false, Tema.Aciklama);
            Yaz(s, "momentleri oranında dağıtılır.", fx, 54.4f, 6f, false, Tema.Aciklama);
            // kesit ve dayanım
            const float kx = 104, ky = 6, B = 16, D = 28;
            g.Dikdortgen(new RectangleF(kx, ky, D, B), Beton, Kenar, K(0.3f));
            g.Dikdortgen(new RectangleF(kx + 1.4f, ky + 1.4f, D - 2.8f, B - 2.8f), null, Eksen, K(0.3f));
            g.Cizgi(Eksen, K(0.25f), kx + 1.4f, ky + B / 2, kx + D - 1.4f, ky + B / 2);
            g.Cizgi(Eksen, K(0.25f), kx + D / 2, ky + 1.4f, kx + D / 2, ky + B - 1.4f);
            foreach (var x in new[] { 0f, 0.5f, 1f }) { Cubuk(g, kx + 2.4f + x * (D - 4.8f), ky + 2.4f); Cubuk(g, kx + 2.4f + x * (D - 4.8f), ky + B - 2.4f); }
            Olcu(g, s, kx, kx + D, ky + B + 4.5f, "D", Yuk); DikeyOlcu(g, s, kx - 2.5f, ky, ky + B, "B", Yuk, true);
            Formul(g, s, 136, 12, "ρ =", "Fk·Letr", "Ac·Hk", " ≥ 0.0025");
            Yaz(s, "Vr = 0.65·Ac·fctd + ρ·Ac·fyd > Ve", 104, 33, 6.6f, true, Vurgu);
            Yaz(s, "Ln serbest boy · Hk kat yüksekliği · Fk etriye alanı", 104, 39, 5.8f, false, Tema.Aciklama);
            Yaz(s, "Letr hesap doğrultusundaki etriye boyları toplamı", 104, 42.2f, 5.8f, false, Tema.Aciklama);
            Yaz(s, "Dikdörtgen kolonda Letr ≅ D, Ac = B·D", 104, 45.4f, 5.8f, false, Tema.Aciklama);
        }

        // ---------------- #21 perde kesmesi (TBDY 7.6.2, 7.6.6) ----------------

        private static void PerdeKesme(ICizimYuzeyi g, ModernSayfa s)
        {
            const float x0 = 6, y0 = 6, L = 84, bw = 8, lu = 16;
            g.Dikdortgen(new RectangleF(x0, y0, L, bw), Beton, Kenar, K(0.3f));
            foreach (var bx in new[] { x0 + 1.2f, x0 + L - lu - 1.2f }) g.Dikdortgen(new RectangleF(bx, y0 + 1.2f, lu, bw - 2.4f), Color.FromArgb(60, Vurgu), Vurgu, K(0.3f));
            for (float x = x0 + 3; x <= x0 + L - 2.9f; x += 4.6f) { Cubuk(g, x, y0 + 2.4f, 0.45f); Cubuk(g, x, y0 + bw - 2.4f, 0.45f); }
            DikeyOlcu(g, s, x0 + L + 3, y0, y0 + bw, "bw", Yuk);
            Olcu(g, s, x0 + 1.2f, x0 + 1.2f + lu, y0 + bw + 5, "uç bölgesi", Vurgu);
            Olcu(g, s, x0, x0 + L, y0 + bw + 12, "lw", Yuk);
            // yatay gövde donatısı (Asws)
            float ey = 38;
            g.CokluCizgi(new[] { P(x0 + 2, ey + 4), P(x0 + 2, ey), P(x0 + L - 2, ey), P(x0 + L - 2, ey + 7), P(x0 + 2, ey + 7), P(x0 + 2, ey + 4.5f) }, Eksen, K(0.4f));
            Formul(g, s, x0, 56, "ρsh =", "2·Asws", "Ach", "· lw / s       Vr = 0.65·Ach·fctd + ρsh·Ach·fyd");
            Yaz(s, "Asws  yatay gövde donatısı (iki yüz)", x0 + 30, ey + 8.2f, 6f, false, Tema.Aciklama);
            // görünüş: kritik perde yüksekliği
            const float gx = 102, gw = 12, gt = 4, gb = 62;
            g.Dikdortgen(new RectangleF(gx, gt, gw, gb - gt), Beton, Kenar, K(0.3f));
            for (float y = gt + 11; y < gb; y += 11) g.Cizgi(Tema.Ikincil, 0.2f, gx - 2, y, gx + gw + 2, y);
            g.Dikdortgen(new RectangleF(gx, gb - 20, gw, 20), Color.FromArgb(70, Vurgu), Vurgu, K(0.3f));
            Tarama(g, gx - 4, gb, gw + 8);
            DikeyOlcu(g, s, gx - 3, gb - 20, gb, "Hcr", Vurgu, true);
            DikeyOlcu(g, s, gx + gw + 3, gt, gb, "Hw", Yuk);
            // formüller
            float fx = 126;
            Yaz(s, "Kritik perde yüksekliği", fx, 2, 6.4f, true, Vurgu);
            Yaz(s, "Hcr ≥ max( lw ; Hw/6 ),  Hcr ≤ 2·lw", fx, 5.6f, 6.2f);
            Yaz(s, "Tasarım kesmesi (Hw/lw > 2)", fx, 13, 6.4f, true, Yuk);
            Formul(g, s, fx, 23, "Ve = βv ·", "(Mp)t", "(Md)t", "· Vd");
            Yaz(s, "βv = 1.5  (yalnız perdeli: 1.0)", fx, 29, 6.2f);
            Yaz(s, "(Mp)t ≤ 1.25·(Mr)t", fx, 32.6f, 6.2f);
            Yaz(s, "TBDY 7.6.2.2 · 7.6.6.3", fx, 38, 5.8f, false, Tema.Aciklama);
        }

        // ---------------- #8 güçlü kolon (TBDY 7.3.5, Şekil 7.4) ----------------

        private static void GucluKolon(ICizimYuzeyi g, ModernSayfa s)
        {
            void Dugum(float cx, float cy, bool sagaDeprem)
            {
                g.Dikdortgen(new RectangleF(cx - 24, cy - 2.6f, 48, 5.2f), Beton, Kenar, K(0.25f));
                g.Dikdortgen(new RectangleF(cx - 2.8f, cy - 19, 5.6f, 38), BetonYan, Kenar, K(0.25f));
                // kiriş uç momentleri düğüme aynı yönde, kolon momentleri ters yönde etkir
                bool kir = sagaDeprem;
                Mafsal(g, cx - 4.4f, cy); Mafsal(g, cx + 4.4f, cy);
                Moment(g, cx - 13, cy, 3.4f, kir, Vurgu); Yaz(s, "Mri", cx - 20, cy + 4, 6.6f, true, Vurgu);
                Moment(g, cx + 13, cy, 3.4f, kir, Vurgu); Yaz(s, "Mrj", cx + 14, cy + 4, 6.6f, true, Vurgu);
                Moment(g, cx, cy - 10, 3.4f, !kir, Yuk); Yaz(s, "Mra", cx + 4, cy - 14, 6.6f, true, Yuk);
                Moment(g, cx, cy + 10, 3.4f, !kir, Yuk); Yaz(s, "Mrü", cx + 4, cy + 9, 6.6f, true, Yuk);
                DepremOku(g, s, cx - 22, cy - 15, sagaDeprem);
            }
            Dugum(36, 24, true); Dugum(114, 24, false);
            Yaz(s, "(Mra + Mrü) ≥ 1.2 · (Mri + Mrj)", 75, 46, 7.8f, true, Vurgu, Hiza.Orta);
            Yaz(s, "TBDY Denk. 7.3 · her iki deprem yönü için ayrı · Mr: depremin yönüne göre en küçük Nd ile", 75, 50.5f, 6f, false, Tema.Aciklama, Hiza.Orta);
        }

        // ---------------- #6 kolon-kiriş birleşimi (TBDY 7.5.2) ----------------

        private static void Birlesim(ICizimYuzeyi g, ModernSayfa s)
        {
            const float cx = 44, cy = 26, hb = 7, bc = 6;
            g.Dikdortgen(new RectangleF(cx - 36, cy - hb, 72, 2 * hb), Beton, Kenar, K(0.25f));
            g.Dikdortgen(new RectangleF(cx - bc, cy - 22, 2 * bc, 44), BetonYan, Kenar, K(0.25f));
            g.Dikdortgen(new RectangleF(cx - bc, cy - hb, 2 * bc, 2 * hb), Color.FromArgb(80, Vurgu));
            // donatılar
            g.Cizgi(Donati, K(0.6f), cx - 34, cy - hb + 1.8f, cx + 34, cy - hb + 1.8f);
            g.Cizgi(Donati, K(0.6f), cx - 34, cy + hb - 1.8f, cx + 34, cy + hb - 1.8f);
            // deprem soldan sağa: sol kirişte üst donatı çekme, sağ kirişte alt donatı çekme
            Ok(g, Donati, K(0.6f), cx - bc, cy - hb + 1.8f, cx - 26, cy - hb + 1.8f, 2.2f);
            Yaz(s, "1.25·fyk·As1", cx - 27, cy - hb - 2.6f, 6.2f, true, Donati, Hiza.Sag);
            Ok(g, Donati, K(0.6f), cx + bc, cy + hb - 1.8f, cx + 26, cy + hb - 1.8f, 2.2f);
            Yaz(s, "1.25·fyk·As2", cx + 27, cy + hb - 0.6f, 6.2f, true, Donati);
            Ok(g, Yuk, K(0.45f), cx - 22, cy + hb - 2.4f, cx - bc - 0.5f, cy + hb - 2.4f, 1.8f); Yaz(s, "C1", cx - 22, cy + hb + 0.4f, 6.2f, true, Yuk);
            Ok(g, Yuk, K(0.45f), cx + 22, cy - hb + 2.4f, cx + bc + 0.5f, cy - hb + 2.4f, 1.8f); Yaz(s, "C2", cx + 18, cy - hb - 3.4f, 6.2f, true, Yuk);
            Ok(g, Yuk, K(0.45f), cx + 2, cy - 17, cx - 6, cy - 17, 1.6f); Yaz(s, "Vü", cx + 3, cy - 19, 6.2f, true, Yuk);
            Ok(g, Yuk, K(0.45f), cx - 2, cy + 17, cx + 6, cy + 17, 1.6f); Yaz(s, "Va", cx + 7, cy + 15, 6.2f, true, Yuk);
            Etiket(g, s, "birleşim bölgesi", cx + 10, cy + 13, cx + 2, cy + 3);
            DepremOku(g, s, cx - 36, cy - 17, true);
            float fx = 92;
            Yaz(s, "Ve = 1.25·fyk·(As1 + As2) − Vkol", fx, 6, 7f, true, Vurgu);
            Yaz(s, "Vkol = min( Va ; Vü )    TBDY Denk. 7.11", fx, 10.6f, 6.2f);
            Yaz(s, "Kiriş tek taraftan saplanıyorsa As2 = 0", fx, 14.2f, 6f, false, Tema.Aciklama);
            Yaz(s, "Kuşatılmış birleşim:", fx, 22, 6.4f, true, Yuk);
            Yaz(s, "Ve ≤ 1.7·bj·h·√fck", fx + 28, 22, 6.4f);
            Yaz(s, "Kuşatılmamış birleşim:", fx, 26.6f, 6.4f, true, Yuk);
            Yaz(s, "Ve ≤ 1.0·bj·h·√fck", fx + 28, 26.6f, 6.4f);
            Yaz(s, "Aşılırsa kesitler büyütülür (7.5.2.2).", fx, 31.2f, 6f, false, Tema.Aciklama);
            Yaz(s, "STA4 kontrolü: Ast > max( Asu1 + Asa2 ; Asa1 + Asu2 )", fx, 38, 6.2f, true);
        }

        // ---------------- #9 kiriş kesmesi (TBDY 7.4.5) ----------------

        private static void KirisKesme(ICizimYuzeyi g, ModernSayfa s)
        {
            const float x0 = 12, x1 = 86, y = 22, hb = 3;
            Kolon(g, x0 - 3, y - 12, y + 12, 6); Kolon(g, x1 + 3, y - 12, y + 12, 6);
            g.Dikdortgen(new RectangleF(x0, y - hb, x1 - x0, 2 * hb), Beton, Kenar, K(0.25f));
            // düşey yük
            g.Cizgi(Tema.Ikincil, K(0.3f), x0, y - 9, x1, y - 9);
            for (float x = x0 + 4; x < x1; x += 6) Ok(g, Tema.Ikincil, K(0.25f), x, y - 9, x, y - hb - 0.4f, 1.3f);
            Yaz(s, "G + Q", (x0 + x1) / 2, y - 13, 6.2f, false, Tema.Aciklama, Hiza.Orta);
            // plastik mafsallar ve kapasite momentleri (aynı dönme yönü)
            Mafsal(g, x0 + 2.5f, y); Mafsal(g, x1 - 2.5f, y);
            Moment(g, x0 + 8, y, 3.4f, true, Vurgu); Yaz(s, "Mpi", x0 + 3, y + 5, 6.6f, true, Vurgu);
            Moment(g, x1 - 8, y, 3.4f, true, Vurgu); Yaz(s, "Mpj", x1 - 13, y + 5, 6.6f, true, Vurgu);
            DepremOku(g, s, x0 - 2, y - 19, true);
            Olcu(g, s, x0, x1, y + 14, "ln", Eksen);
            // kesme diyagramı: Vdy (doğrusal) + (Mpi + Mpj)/ln (sabit)
            float dy = 50, vp = 3.2f, vd = 5.5f;
            g.Cizgi(Kenar, K(0.3f), x0, dy, x1, dy);
            var poli = new[] { P(x0, dy), P(x0, dy - vd - vp), P(x1, dy + vd - vp), P(x1, dy) };
            g.Poligon(poli, Color.FromArgb(60, Eksen)); g.CokluCizgi(poli, Eksen, K(0.35f));
            Yaz(s, "Ve", x0 - 1, dy - vd - vp - 1.6f, 6.4f, true, Eksen, Hiza.Sag);
            Yaz(s, "kesme diyagramı", x1 + 2, dy - 1.4f, 6f, false, Tema.Aciklama);
            float fx = 104;
            Formul(g, s, fx, 12, "Ve = Vdy ±", "Mpi + Mpj", "ln");
            Yaz(s, "TBDY Denk. 7.9 · iki deprem yönü ayrı", fx, 18, 6f, false, Tema.Aciklama);
            Yaz(s, "Vdy  G+Q yükünden basit kiriş kesmesi", fx, 25, 6.2f);
            Yaz(s, "Mp ≈ 1.4·Mr  (pekleşmeli kapasite)", fx, 28.8f, 6.2f);
            Yaz(s, "STA4: Ve > Vd(G+Q+D·E) ise", fx, 36, 6.2f, true, Yuk);
            Yaz(s, "Ve = Vd(G+Q+D·E) alınır", fx, 39.6f, 6.2f, true, Yuk);
        }
    }
}
