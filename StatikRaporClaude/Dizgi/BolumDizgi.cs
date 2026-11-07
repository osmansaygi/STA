using System.Globalization;
using System.Text.RegularExpressions;
using StatikRaporClaude.Gpr;
using StatikRaporClaude.Modern;

namespace StatikRaporClaude.Dizgi
{
    /// <summary>GPR bölümünü modern akış bloklarına çevirir (başlık + tablo/metin/şekil).</summary>
    public static class BolumDizgi
    {
        public static List<Blok> Bloklar(GprBolum b, ISet<string> katlar = null)
        {
            if (b.Anahtar == "DEPREM RAPORU" && b.Sayfalar.Count > 0 && !b.Grafik) return DepremBloklari(b, katlar);
            var bloklar = new List<Blok>
            {
                new BolumBasiBlok { Bolum = b, Baslik = ModernBolumler.Tr(b.Baslik) + (b.Grafik ? " (grafik)" : ""), Aciklama = Aciklama(b) }
            };
            // yapı yorumunda her satır ayrı bir cümle: paragraf birleştirme yok
            bloklar.AddRange(Donustur(GprAyristirici.Ayristir(b), katlar, b.Anahtar != Rapor.RaporKatalogu.YorumAnahtari));
            return bloklar;
        }

        private static IEnumerable<Blok> Donustur(IEnumerable<Oge> ogeler0, ISet<string> katlar, bool sar = true)
        {
            var ogeler = ogeler0.ToList();
            // bölümdeki tüm anahtar-değer satırları ortak değer hizası için (çok uzun anahtarlar hizayı belirlemez)
            var grup = ogeler.OfType<AnahtarDegerOgesi>().SelectMany(a => a.Satirlar).Where(r => r.Anahtar.Length <= 60).ToList();
            foreach (var o in ogeler)
            {
                switch (o)
                {
                    case BaslikOgesi h: yield return new BaslikBlok { Metin = h.Metin, Seviye = h.Seviye }; break;
                    case TabloOgesi t:
                        Rapor.KatFiltresi.Uygula(t, katlar);
                        if (t.Govde.Count > 0 || t.Baslik.Count == 0) yield return new TabloBlok { Tablo = t };
                        break;
                    case AnahtarDegerOgesi a: yield return new AnahtarDegerBlok { Satirlar = a.Satirlar, Grup = grup }; break;
                    case MetinOgesi m: yield return new MetinBlok { Satirlar = m.Satirlar, YenidenSar = sar }; break;
                    case DiyaframOgesi d: yield return new DiyaframBlok { Oge = d }; break;
                    case ResimOgesi r: yield return new ResimBlok { No = r.No }; break;
                    case SekilOgesi s: yield return new SekilBlok { Sekil = s }; break;
                }
            }
        }

        /// <summary>Bölümün GPR sayfalarındaki STA4 kütüphane resimleri (tablosu sonuç dosyalarından üretilen bölümler için).</summary>
        public static List<Blok> ResimBloklari(GprBolum b) => b.Sayfalar.Count == 0 ? new List<Blok>()
            : GprAyristirici.Ayristir(b).OfType<ResimOgesi>().Select(r => (Blok)new ResimBlok { No = r.No }).ToList();

        /// <summary>GPR'siz (analiz dosyalarından) üretilen bölümün kaynak notu; yaklaşık değer içeriyorsa nedeni.</summary>
        private static string Aciklama(GprBolum b)
        {
            if (b.No < 900000) return null;
            if (b.Anahtar == Rapor.RaporKatalogu.YorumAnahtari)
                return "STA4-CAD analiz yorumu (.YORUM dosyası) · A statik, B ekonomi, C temel ve zemin maddeleri ile kolon/perde alan oranı";
            return Uretec.SanalBolum.Yaklasik.TryGetValue(b.Anahtar ?? "", out var y)
                ? "Analiz dosyalarından üretildi — " + y : "Analiz dosyalarından üretildi";
        }

        // ---------------- deprem raporu ----------------

        /// <summary>
        /// Deprem raporu: özet kutuları, parametre tablosu, yatay/düşey spektrum grafiği ve sıkıştırılmış T–Sa tablosu;
        /// ardından GPR bölümünün spektrumdan sonraki tüm içeriği (modal analiz, eşdeğer deprem, kat kuvvetleri, rüzgar…).
        /// </summary>
        private static List<Blok> DepremBloklari(GprBolum b, ISet<string> katlar)
        {
            var d = DepremRaporu.Oku(b);
            var bloklar = new List<Blok>
            {
                new BolumBasiBlok { Bolum = b, Baslik = ModernBolumler.Tr(b.Baslik), Aciklama = "TBDY 2018 · Mod birleştirme yöntemi ile modal analiz · Değerler STA4CAD hesabından aynen aktarılmıştır" },
                new CizimBlok { H = 20f, Cizici = s => ModernBolumler.DepremKutucuklari(s, d) },
            };
            if (d.Parametreler.Count > 0)
                bloklar.Add(new TabloBlok { Tablo = Sol(Tablo(new[] { 40, 16, 44 }, new[] { "Parametre", "Sembol", "Değer" },
                    d.Parametreler.Select(p => new[] { ModernBolumler.Tr(p.Etiket), p.Sembol, p.Deger }))) });

            var ogeler = GprAyristirici.Ayristir(b);
            if (d.Spektrum.Count > 1)
            {
                bloklar.Add(new BaslikBlok { Metin = "Elastik tasarım spektrumu", Seviye = 1 });
                bloklar.Add(new CizimBlok { H = 66f, Cizici = s => ModernBolumler.Spektrum(s, d, ModernSayfa.Kenar, s.Y + 2f, ModernSayfa.En - 2 * ModernSayfa.Kenar, 62f) });
                // T–Sa değerleri: 5 sütun çifti (STA4 tek uzun sütun basar)
                const int cift = 5;
                int satir = (d.Spektrum.Count + cift - 1) / cift;
                var inv = CultureInfo.InvariantCulture;
                var rows = Enumerable.Range(0, satir).Select(r => Enumerable.Range(0, cift).SelectMany(c =>
                {
                    int i = c * satir + r;
                    return i < d.Spektrum.Count ? new[] { d.Spektrum[i].T.ToString("0.00", inv), d.Spektrum[i].Sa.ToString("0.000", inv) } : new[] { "", "" };
                }).ToArray());
                bloklar.Add(new TabloBlok { Tablo = Tablo(Enumerable.Repeat(new[] { 8, 10 }, cift).SelectMany(x => x).ToArray(),
                    Enumerable.Repeat(new[] { "T (s)", "Sa (g)" }, cift).SelectMany(x => x).ToArray(), rows) });
            }

            // GPR'nin spektrumdan sonraki içeriği ("Ra(T)…" satırından itibaren); bulunamazsa son şekilden sonrası
            int bas = ogeler.FindIndex(o => o is MetinOgesi m && m.Satirlar.Count > 0 && m.Satirlar[0].Parcalar.Count > 0 && m.Satirlar[0].Parcalar[0].Metin.StartsWith("Ra(T)"));
            if (bas < 0) bas = ogeler.FindLastIndex(o => o is SekilOgesi) + 1;
            if (bas <= 0) bas = ogeler.FindIndex(o => o is BaslikOgesi h && GprBolumleri.Anahtar(h.Metin).Contains("DINAMIK ANALIZ"));
            bloklar.AddRange(Donustur(ogeler.Skip(Math.Max(0, bas)), katlar));
            return bloklar;
        }

        private static TabloOgesi Sol(TabloOgesi t) { t.SolSutunlar = Enumerable.Range(0, t.Sutunlar.Count).ToHashSet(); return t; }

        private static TabloOgesi Tablo(int[] sutunlar, string[] baslik, IEnumerable<string[]> satirlar)
        {
            TabloSatir Satir(string[] h)
            {
                var r = new TabloSatir();
                for (int i = 0; i < h.Length; i++) r.Hucreler.Add(new TabloHucre { Ilk = i, Son = i, Metin = h[i] ?? "" });
                return r;
            }
            var t = new TabloOgesi { Sutunlar = sutunlar.ToList(), Imza = string.Join(",", baslik) };
            t.Baslik.Add(Satir(baslik));
            foreach (var r in satirlar) t.Govde.Add(Satir(r));
            return t;
        }
    }
}
