using System.Globalization;
using StatikRaporClaude.Dizgi;
using StatikRaporClaude.Modern;

namespace StatikRaporClaude.Veri
{
    /// <summary>
    /// GPR'siz tablo üretimi: STA4 sonuç dosyalarından, STA4 GPR tablolarıyla aynı satır/hücre düzeninde tablolar.
    /// Sayı biçimi STA4 ile aynı (F2, kolonda N F3, ρ F4). Dosyada bulunmayan hücreler "—" yazılır.
    /// </summary>
    public static class DosyaTablolari
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const string Yok = "—";

        private static readonly string[] Duzenler = { "GGGGGG", "QQQQQQ", "Q_Q_Q_", "_Q_Q_Q", "QQ_QQ_", "_QQ_QQ", "Q_QQ_Q", "Zemin" };
        private static readonly string[] Depremler = { "Deprem+X", "Deprem-X", "Deprem+Y", "Deprem-Y", "Rüzgar X", "Rüzgar Y" };

        /// <summary>Deprem satır başlığı: 6 yük + (ısı yükü varsa "ISI") + "Deprem Z", 10 hücre.</summary>
        private static string[] DepremBaslik(bool isi) =>
            new[] { "" }.Concat(Depremler).Concat(isi ? new[] { "ISI", "Deprem Z" } : new[] { "Deprem Z", "" }).Append("").ToArray();

        /// <summary>Deprem satırı değerleri (8 hücre): indis 8..13, ısı yükü (14) varsa, Deprem Z = 2/3·SDS·G.</summary>
        private static IEnumerable<string> DepremDegerleri(float[] a, double kz, bool isi) =>
            Enumerable.Range(8, 6).Select(j => S(a[j])).Concat(isi ? new[] { S(a[14]), S(kz * a[0]) } : new[] { S(kz * a[0]), "" });

        /// <summary>
        /// STA4 sayı biçimi: sabit ondalık, yarım yukarı, "-0.00" yazılmaz. float32 değerler STA4 gibi kısa ondalık
        /// gösterimleri üzerinden yuvarlanır: 7 anlamlı basamak (5.4249997f → "5.425" → 5.43).
        /// </summary>
        public static string S(double v, int ondalik = 2) => Bicim((decimal)v, ondalik);

        public static string S(float v, int ondalik = 2) =>
            Bicim(decimal.Parse(v.ToString("G7", Inv), NumberStyles.Float, Inv), ondalik);

        private static string Bicim(decimal v, int ondalik)
        {
            string s = Math.Round(v, ondalik, MidpointRounding.AwayFromZero).ToString("F" + ondalik, Inv);
            return s.TrimStart('-').Trim('0', '.').Length == 0 ? s.TrimStart('-') : s;
        }

        /// <summary>TBDY: uzun kenar ≥ 6 × kısa kenar ise perde.</summary>
        public static bool Perde(St4Kolon k) => k.Poligon ? k.PerdeX || k.PerdeY : Math.Max(k.Bx, k.By) >= 6 * Math.Min(k.Bx, k.By);

        private static TabloSatir Satir(bool grupBasi, params string[] hucreler)
        {
            var r = new TabloSatir { GrupBasi = grupBasi };
            for (int i = 0; i < hucreler.Length; i++) r.Hucreler.Add(new TabloHucre { Ilk = i, Son = i, Metin = hucreler[i] ?? "" });
            return r;
        }

        private static TabloSatir AraBaslik(TabloSatir r) { r.AraBaslik = true; return r; }

        private static TabloOgesi Tablo(int[] sutunlar, params string[][] baslik)
        {
            var t = new TabloOgesi { Sutunlar = sutunlar.ToList() };
            foreach (var b in baslik) t.Baslik.Add(Satir(false, b));
            t.Imza = "dosya";
            return t;
        }

        private static double Kot(StaVeri v, int kat) => kat >= 0 && kat < v.Model.Katlar.Count ? v.Model.Katlar[kat].Kot : 0;

        // ---------------- kiriş statik (BEA) ----------------

        public static TabloOgesi KirisStatik(StaVeri v, ISet<string> katlar = null)
        {
            var t = Tablo(new[] { 11, 9, 9, 9, 9, 9, 9, 9, 9, 13 });
            double kz = 2.0 / 3.0 * v.Model.Sds;
            foreach (var k in v.Model.Kirisler)
            {
                if (!Rapor.KatFiltresi.Dahil(k.Ad, katlar) || !v.KirisKaydi.TryGetValue(k, out int r)) continue;
                var b = v.Bea[r];
                var a = new[] { b.Dizi(38), b.Dizi(126), b.Dizi(390), b.Dizi(478) };
                string[] ad = { "SolM", "SagM", "SolV", "SagV" };
                var adSatiri = AraBaslik(Satir(t.Govde.Count > 0, new[] { k.Ad }.Concat(Duzenler).Append("Maçıklık").ToArray()));
                short uc = b.S(36);
                if (Modern.KirisSemasi.Gecerli(uc)) adSatiri.UcTipi = uc;   // statik tablo BEA sırasıyla (sol = başlangıç)
                t.Govde.Add(adSatiri);
                string[] sag = { S(b.F(660)) + " (tm)", "", "Xaç (m)", S(b.F(664)) };
                for (int i = 0; i < 4; i++)
                    t.Govde.Add(Satir(false, new[] { ad[i] }.Concat(Enumerable.Range(0, 8).Select(j => S(a[i][j]))).Append(sag[i]).ToArray()));
                t.Govde.Add(AraBaslik(Satir(false, DepremBaslik(v.IsiVar))));
                double z = Kot(v, k.Kat);
                string[] sag2 = { "", "", $"Z1= {(z + k.Kot1).ToString("0.00", Inv)}m", $"Z2= {(z + k.Kot2).ToString("0.00", Inv)}m" };
                for (int i = 0; i < 4; i++)
                    t.Govde.Add(Satir(false, new[] { ad[i] }.Concat(DepremDegerleri(a[i], kz, v.IsiVar)).Append(sag2[i]).ToArray()));
            }
            return t;
        }

        // ---------------- kolon statik (COL) ----------------

        private static readonly (string Ad, int Ofset)[] KolonKuvvet =
            { ("Üst Mx", 114), ("Alt Mx", 202), ("Üst My", 290), ("Alt My", 378), ("Tx", 466), ("Ty", 554), ("Nz", 26) };

        /// <summary>Kolon boyu (m): ST4 üst/alt kotu varsa ondan (GPR "H ="), yoksa COL +662.</summary>
        private static float KolonBoyu(St4Kolon k, IkiliKayit c) => k.KotVar ? (float)Math.Round(k.UstKot - k.AltKot, 4) : c.F(662);

        public static IEnumerable<St4Kolon> KolonSirasi(StaVeri v) =>
            v.Model.Kolonlar.OrderBy(k => k.Yerel).ThenByDescending(k => k.Kat);

        public static TabloOgesi KolonStatik(StaVeri v, ISet<string> katlar = null)
        {
            var t = Tablo(new[] { 11, 9, 9, 9, 9, 9, 9, 9, 9, 13 });
            double kz = 2.0 / 3.0 * v.Model.Sds;
            var perdeM = TasarimHesaplari.PerdeMomentleri(v);
            foreach (var k in KolonSirasi(v))
            {
                if (!Rapor.KatFiltresi.Dahil(k.Ad, katlar)) continue;
                var c = v.Col[v.KolonKaydi(k)];
                string[] sag = { "", $"I = {c.S(0)}", $"J = {c.S(2)}", "", $"Bx= {k.Bx} cm", $"By= {k.By} cm", $"H = {S(KolonBoyu(k, c))} m" };
                if (k.Poligon) { sag[4] = "POLİGON"; sag[5] = "KOLON"; }
                t.Govde.Add(AraBaslik(Satir(t.Govde.Count > 0, new[] { k.Ad }.Concat(Duzenler).Append("Material:E1").ToArray())));
                for (int i = 0; i < KolonKuvvet.Length; i++)
                {
                    var a = c.Dizi(KolonKuvvet[i].Ofset);
                    t.Govde.Add(Satir(false, new[] { KolonKuvvet[i].Ad }.Concat(Enumerable.Range(0, 8).Select(j => S(a[j]))).Append(sag[i]).ToArray()));
                }
                t.Govde.Add(AraBaslik(Satir(false, DepremBaslik(v.IsiVar))));
                // perdelerde sağ sütunda tasarım moment zarfı (Myu kat üstü, Mya kat altı) basılır
                string[] perde = perdeM.TryGetValue(k, out var pm)
                    ? new[] { "M perde" }.Concat(pm.SelectMany(x => new[] { $"{x.Eksen}u: {S(x.Ust, 1)}", $"{x.Eksen}a: {S(x.Alt, 1)}" })).ToArray() : new string[0];
                for (int i = 0; i < KolonKuvvet.Length; i++)
                {
                    var a = c.Dizi(KolonKuvvet[i].Ofset);
                    t.Govde.Add(Satir(false, new[] { KolonKuvvet[i].Ad }.Concat(DepremDegerleri(a, kz, v.IsiVar))
                        .Append(i < perde.Length ? perde[i] : "").ToArray()));
                }
            }
            return t;
        }

        // ---------------- döşeme statik (FLO) ----------------

        public static IEnumerable<St4Doseme> RaporDosemeleri(StaVeri v) => v.Model.Dosemeler.Where(d => d.No >= 100);

        public static TabloOgesi DosemeStatik(StaVeri v, ISet<string> katlar = null)
        {
            var t = Tablo(new[] { 7, 6, 8, 9, 9, 9, 9, 9, 9, 9, 18, 5 },
                new[] { "Döşeme", "yön", "L", "sol gGg", "sol qGq", "sol gQg", "açıklık", "sağ gGg", "sağ qGq", "sağ gQg", "sehim < fmax", "Wk" },
                new[] { "no", "", "m", "(tm)", "(tm)", "(tm)", "(tm)", "(tm)", "(tm)", "(tm)", "mm", "mm" });
            foreach (var d in RaporDosemeleri(v).OrderBy(d => d.Kat).ThenBy(d => d.Yerel))
            {
                if (!Rapor.KatFiltresi.Dahil(d.Ad, katlar)) continue;
                var f = v.Flo[v.DosemeKaydi(d)];
                float sehim = f.F(136) * 1000, fmax = f.F(150) * 1000;
                // X satırında ani sehim ve Wk, Y satırında zamana bağlı toplam sehim (δt) basılır
                t.Govde.Add(Satir(t.Govde.Count > 0, d.Ad, "X", S(f.F(116)), S(f.F(12)), S(f.F(16)), S(f.F(20)), S(f.F(4)), S(f.F(24)), S(f.F(28)), S(f.F(32)),
                    $"{S(sehim)} < {S(fmax)}" + (sehim < fmax ? " ✓" : " ✗"), S(TasarimHesaplari.DosemeWk(v.Dmx[v.DosemeKaydi(d)]))));
                t.Govde.Add(Satir(false, "E1", "Y", S(f.F(120)), S(f.F(36)), S(f.F(40)), S(f.F(44)), S(f.F(8)), S(f.F(48)), S(f.F(52)), S(f.F(56)),
                    S(TasarimHesaplari.DosemeToplamSehim(sehim, d)), ""));
            }
            return t;
        }

        // ---------------- döşeme betonarme (DMX) ----------------

        /// <param name="yYonu">Y doğrultusunda DMX sol/sağ ilaveleri ters tutar.</param>
        public static string DosemeDonati(IkiliKayit d, int ofset, bool yYonu)
        {
            short a(int i) => d.S(ofset + 2 * i);
            var p = new List<string> { $"ø{a(0)}/{a(1)}(düz)" };
            bool pilye = a(2) == 1 && a(3) > 0;
            if (pilye) p.Add($"ø{a(3)}/{a(4)}(pil)");
            (int c, int s) sol = yYonu ? (a(7), a(8)) : (a(5), a(6)), sag = yYonu ? (a(5), a(6)) : (a(7), a(8));
            bool iki = pilye && sol.c > 0 && sag.c > 0;   // pilyeli ve iki tarafta ilaveli döşemede STA kısa etiket yazar: "(sol)" "(sağ)"
            if (sol.c > 0) p.Add($"ø{sol.c}/{sol.s}({(iki ? "sol" : "sol ila")})");
            if (sag.c > 0) p.Add($"ø{sag.c}/{sag.s}({(iki ? "sağ" : "sağ ila")})");
            return string.Join("+", p);
        }

        public static TabloOgesi DosemeBetonarme(StaVeri v, ISet<string> katlar = null)
        {
            var t = Tablo(new[] { 10, 9, 8, 9, 8, 9, 8, 46 },
                new[] { "Döşeme", "Msol", "As", "Maç", "As", "Msağ", "As", "Donatı" },
                new[] { "no", "(tm)", "cm²", "(tm)", "cm²", "(tm)", "cm²", "" });
            foreach (var d in RaporDosemeleri(v).OrderBy(d => d.Kat).ThenBy(d => d.Yerel))
            {
                if (!Rapor.KatFiltresi.Dahil(d.Ad, katlar)) continue;
                var m = v.Dmx[v.DosemeKaydi(d)];
                t.Govde.Add(Satir(t.Govde.Count > 0, d.Ad + "  X", S(m.F(0)), S(m.F(24)), S(m.F(8)), S(m.F(32)), S(m.F(4)), S(m.F(28)), DosemeDonati(m, 96, false)));
                t.Govde.Add(Satir(false, $"d={d.D}cm Y", S(m.F(16)), S(m.F(40)), S(m.F(20)), S(m.F(44)), S(m.F(12)), S(m.F(36)), DosemeDonati(m, 114, true)));
            }
            return t;
        }

        // ---------------- yükler (LOA) ----------------

        private static string ElemanAdi(StaVeri v, int no1)
        {
            var d = v.Model.Dosemeler.ElementAtOrDefault(no1 - 1);
            return d?.Ad ?? $"#{no1}";
        }

        public static TabloOgesi KirisYuk(StaVeri v, ISet<string> katlar = null)
        {
            var t = Tablo(new[] { 9, 8, 8, 9, 9, 10, 10, 12 },
                new[] { "Kiriş", "A1", "A2", "G1", "G2", "Q1", "Q2", "Yük elemanı" },
                new[] { "no", "m", "m", "t/m", "t/m", "t/m", "t/m", "no" });
            string onceki = null;
            for (int i = 0; i < v.Loa.Adet; i++)
            {
                var r = v.Loa[i];
                if (r.S(26) != 0) continue;                     // kiriş satırı
                var k = v.Model.Kirisler.ElementAtOrDefault(r.S(24) - 1);
                if (k == null || !Rapor.KatFiltresi.Dahil(k.Ad, katlar)) continue;
                bool duvar = r.F(16) == 0 && r.F(20) == 0;
                t.Govde.Add(Satir(onceki != null && onceki != k.Ad, k.Ad, S(r.F(0)), S(r.F(4)), S(r.F(8)), S(r.F(12)), S(r.F(16)), S(r.F(20)),
                    ElemanAdi(v, r.S(28)) + (duvar ? " Duvar" : "")));
                onceki = k.Ad;
            }
            return t;
        }

        /// <summary>STA4 kolon yük satırını Pz, Mx, My'den biri |≥ 0.01| ise basar (K_BLG_03 SB-18 ile doğrulandı).</summary>
        private static bool Basilir(params float[] d) => d.Any(x => Math.Abs(x) >= 0.01);

        public static TabloOgesi KolonYuk(StaVeri v, ISet<string> katlar = null)
        {
            var t = Tablo(new[] { 9, 15, 9, 9, 9, 9, 9, 12 },
                new[] { "Kolon", "Kombinasyon", "Px", "Py", "Pz", "Mx", "My", "Eleman" },
                new[] { "no", "", "t", "t", "t", "tm", "tm", "no" });
            string onceki = null;
            for (int i = 0; i < v.Loa.Adet; i++)
            {
                var r = v.Loa[i];
                if (r.S(26) != 1) continue;                     // kolon satırı
                var k = v.Model.Kolonlar.ElementAtOrDefault(r.S(24) + 31072 - 1);
                if (k == null || !Rapor.KatFiltresi.Dahil(k.Ad, katlar)) continue;
                bool duvar = r.F(16) == 0 && r.F(4) == 0 && r.F(20) == 0;
                string el = ElemanAdi(v, r.S(28)) + (duvar ? " Duvar" : "");
                bool ilk = true;
                if (Basilir(r.F(8), r.F(12), r.F(0)))
                {
                    t.Govde.Add(Satir(onceki != null && onceki != k.Ad, k.Ad, "Olu yuk", "0.00", "0.00", S(r.F(8)), S(r.F(12)), S(r.F(0)), el));
                    ilk = false; onceki = k.Ad;
                }
                if (Basilir(r.F(16), r.F(20), r.F(4)))
                {
                    t.Govde.Add(Satir(ilk && onceki != null && onceki != k.Ad, k.Ad, "Hareketli yuk", "0.00", "0.00", S(r.F(16)), S(r.F(20)), S(r.F(4)), el));
                    onceki = k.Ad;
                }
            }
            return t;
        }

        // ---------------- betonarme (modern tablolar için) ----------------

        private static readonly string[] KirisDonatiAd = { "mon.", "düz", "pil", "pil", "göv.", "sol üst ila.", "sol alt ila.", "sağ üst ila.", "sağ alt ila." };

        public static List<KirisSonucu> KirisBetonarme(StaVeri v, ISet<string> katlar = null)
        {
            var liste = new List<KirisSonucu>();
            foreach (var k in v.Model.Kirisler)
            {
                if (!Rapor.KatFiltresi.Dahil(k.Ad, katlar) || !v.KirisKaydi.TryGetValue(k, out int r)) continue;
                var m = v.Kmx[r]; var b = v.Bea[r];
                bool ters = k.YYonu;   // Y doğrultusundaki kirişlerde KMX sol/sağ ters tutulur
                int sol = ters ? 16 : 0, sag = ters ? 0 : 16;          // M: sol üst +8/+12, sağ +24/+28
                int asSol = ters ? 8 : 0, asSag = ters ? 0 : 8;        // As: sol +60/+64, sağ +68/+72
                var s = new KirisSonucu { Ad = k.Ad, D = k.H.ToString(), B = k.B.ToString(), Sinif = "A4" };
                s.MaxM = new[] { S(m.F(8 + sol)), S(m.F(12 + sol)), S(b.F(660)), S(m.F(8 + sag)), S(m.F(12 + sag)) };
                s.Mduz = new[] { "0.00", "0.00", $"( {S(b.F(664))}m)", "0.00", "0.00" };
                s.As = new[] { S(m.F(60 + asSol)), S(m.F(64 + asSol)), S(m.F(76)), S(m.F(60 + asSag)), S(m.F(64 + asSag)) };
                s.Asw = new[] { S(m.F(ters ? 104 : 100)), "", "", S(m.F(ters ? 100 : 104)), "" };
                var adlar = (string[])KirisDonatiAd.Clone();
                if (ters) for (int i = 5; i < 9; i++) adlar[i] = adlar[i].StartsWith("sol") ? "sağ" + adlar[i].Substring(3) : "sol" + adlar[i].Substring(3);
                foreach (int i in new[] { 0, 4, 1, 2, 3, 5, 6, 7, 8 })
                {
                    short n = m.S(108 + 4 * i), cap = m.S(110 + 4 * i);
                    if (n > 0) s.Boyuna.Add($"{n}ø{cap}({adlar[i]})");
                }
                short ec = m.S(146), ea = m.S(148), es = m.S(150);
                short ekol = m.S(144);   // etriye takımı sayısı: 2 → "2×ø10/20/9" (K_BLG_03)
                s.Etriye = (ekol >= 2 ? $"{ekol}×" : "") + (es > 0 ? $"ø{ec}/{ea}/{es}" : $"ø{ec}/{ea}");
                double wk = Math.Round(TasarimHesaplari.KirisWk(m, k), 2, MidpointRounding.AwayFromZero);
                s.WkUygun = wk < 0.4;
                s.Wk = $"Wk={S(wk)}{(s.WkUygun ? "<" : ">")}0.4";
                short uc = b.S(36);
                if (Modern.KirisSemasi.Gecerli(uc)) s.UcTipi = ters ? Modern.KirisSemasi.Ters(uc) : uc;
                liste.Add(s);
            }
            return liste;
        }

        private static readonly (string Ad, int N, int Maj, int Min, int As)[] KolonKombinasyon =
        {
            ("X-(G+Q)", 36, 48, 60, 84), ("X-(G+Q+E)", 40, 52, 64, 88), ("Y-(G+Q)", 36, 108, 120, 144),
            ("Y-(G+Q+E)", 100, 112, 124, 148), ("X-(G-E)", 44, 56, 68, 88), ("Y-(G-E)", 104, 116, 128, 148)
        };

        private static double Alan(int cap) => Math.PI * cap * cap / 400.0;

        /// <summary>Kolon etriye kol öneki: SMX bayrağı yön başına ondalık basamak (1 → "2ø", 11 → "3ø"; N_ULK_02, VKF_01).</summary>
        private static string EtriyeKol(short bayrak) => bayrak > 0 ? (bayrak.ToString().Count(c => c != '0') + 1).ToString() : "";

        public static List<KolonSonucu> KolonBetonarme(StaVeri v, ISet<string> katlar = null)
        {
            var liste = new List<KolonSonucu>();
            foreach (var k in KolonSirasi(v))
            {
                if (!Rapor.KatFiltresi.Dahil(k.Ad, katlar)) continue;
                var s = v.Smx[v.KolonKaydi(k)]; var c = v.Col[v.KolonKaydi(k)];
                short a(int i) => s.S(161 + 2 * i);
                double ac = k.Ac;
                var sonuc = new KolonSonucu
                {
                    Ad = k.Ad, Bx = k.Poligon ? "Polygon" : k.Bx.ToString(), By = k.Poligon ? "kolon" : k.By.ToString(), I = c.S(0).ToString(), J = c.S(2).ToString(),
                    Hk = KolonBoyu(k, c).ToString("0.0##", Inv), BetaX = S(s.F(0), 3), BetaY = S(s.F(4), 3), Sinif = "A4"
                };
                foreach (var (ad, n, maj, min, asOf) in KolonKombinasyon)
                    sonuc.Kombinasyonlar.Add(new[] { ad, S(s.F(n), 3), S(s.F(min)), S(s.F(maj)), S(s.F(72), 1), S((float)(s.F(asOf) / (float)ac), 4), S(s.F(asOf)) });
                sonuc.Boyuna = $"2×{a(5)}ø{a(6)}+2×{a(7)}ø{a(8)}(govde)";
                sonuc.Etriye = EtriyeKol(a(3)) + $"ø{a(0)}/" + (k.Poligon || Perde(k) ? $"[{a(1)}]" : $"{a(1)}") + (a(2) > 0 ? $"/{a(2)}" : "");   // perdede orta aralık köşeli
                double asToplam = 2 * a(5) * Alan(a(6)) + 2 * a(7) * Alan(a(8));
                sonuc.AsAc = S(asToplam / ac, 4);
                if (k.Poligon) { sonuc.Boyuna = Yok; sonuc.AsAc = Yok; }   // poligon donatısı PMX çubuk dizilerinde; adetler çözülemedi
                liste.Add(sonuc);
            }
            return liste;
        }
    }
}
