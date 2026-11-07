using StatikRaporClaude.Dizgi;
using StatikRaporClaude.Gpr;
using StatikRaporClaude.Modern;

namespace StatikRaporClaude.Veri
{
    /// <summary>
    /// Sonuç dosyalarından üretilebilen bölümler için akış blokları. Tablo GPR yerine dosyadan gelir;
    /// GPR varsa aynı bölümün tablosuyla hücre hücre karşılaştırılır ve sonuç tablonun altına not olarak yazılır.
    /// </summary>
    public static class DosyaBolumleri
    {
        private sealed class Tanim
        {
            public string Anahtar;
            public Func<StaVeri, ISet<string>, TabloOgesi> Uret;
            public string Kaynak, Aciklama;
            public bool SatirBazli;
        }

        private static readonly Tanim[] Tanimlar =
        {
            new Tanim { Anahtar = "KIRIS STATIK HESAP SONUCLARI", Uret = DosyaTablolari.KirisStatik, Kaynak = "BEA",
                Aciklama = "Analizlerde çatlamış kesit etkin kesit rijitlik çarpanı dikkate alınmıştır (TBDY 2018 4.5.8). Deprem Z = 2/3 · SDS · G." },
            new Tanim { Anahtar = "KOLON STATIK HESAP SONUCLARI", Uret = DosyaTablolari.KolonStatik, Kaynak = "COL",
                Aciklama = "Analizlerde çatlamış kesit etkin kesit rijitlik çarpanı dikkate alınmıştır (TBDY 2018 4.5.8). Deprem Z = 2/3 · SDS · G. " +
                           "Perdelerde Mu/Ma: tasarım eğilme momenti zarfı (TBDY 7.6.2.2), Hcr boyunca taban momenti, üstünde tepe momentine doğrusal." },
            new Tanim { Anahtar = "DOSEME STATIK HESAP SONUCLARI", Uret = DosyaTablolari.DosemeStatik, Kaynak = "FLO",
                Aciklama = "Zamana bağlı sehim: δt = δi + δig · λ, δig = δi · G/(G+Q), λ = 2 (5 yıl üzeri). " +
                           "Wk: açıklık alt donatısına göre çatlak genişliği (ACI 318 Gergely–Lutz, fs = 0.6 fy)." },
            new Tanim { Anahtar = "DOSEME BETONARME HESAP SONUCLARI", Uret = DosyaTablolari.DosemeBetonarme, Kaynak = "DMX" },
            new Tanim { Anahtar = "KIRIS DUSEY YUK BILGILERI", Uret = DosyaTablolari.KirisYuk, Kaynak = "LOA", SatirBazli = true },
            new Tanim { Anahtar = "KOLON YUK BILGILERI", Uret = DosyaTablolari.KolonYuk, Kaynak = "LOA", SatirBazli = true },
            new Tanim { Anahtar = "RADYE NOKTA DEPLASMANLARI", Uret = RadyeTablolari.Deplasmanlar, Kaynak = "RAD + DE1",
                Aciklama = "Radye düğümlerinin düşey deplasmanları (mm), yalnız zemin yayı olan düğümler." },
            new Tanim { Anahtar = "RADYE ZEMIN GERILMESI", Uret = RadyeTablolari.ZeminGerilmesi, Kaynak = "RAD + DE1",
                Aciklama = "Zemin gerilmesi σ = Ko · w (t/m²); max σ = 1.4·g + 1.6·max(q)." },
            new Tanim { Anahtar = "RADYE PLAK BILGILERI", Uret = RadyeTablolari.Plaklar, Kaynak = "RAD" },
            new Tanim { Anahtar = "RADYE MESH BILGILERI", Uret = RadyeTablolari.Meshler, Kaynak = "RAD" },
            new Tanim { Anahtar = "RADYE NOKTA YUKLERI", Uret = RadyeTablolari.NoktaYukleri, Kaynak = "RAD", SatirBazli = true },
            new Tanim { Anahtar = "RADYE WINKLER YAY YUKLERI", Uret = RadyeTablolari.Winkler, Kaynak = "RAD + DE1",
                Aciklama = "Winkler yay yükü R = yay · w (t), tüm radye düğümleri." },
            new Tanim { Anahtar = "RADYE ZEMIN GERILMESI KONTROLU", Uret = RadyeTablolari.ZeminKontrolu, Kaynak = "RAD + DE1",
                Aciklama = "G = Ko·w_g, Q = max(q), E = (max|σ_E| + 0.3|σ_Ez|)/2, W = max|σ_W|; kombinasyonlar zemin taşıma gücü tasarım gerilmesi qt ile karşılaştırılır." },
        };

        public static bool Destekler(GprBolum b) => !b.Grafik && Tanimlar.Any(t => t.Anahtar == b.Anahtar);

        public static List<Blok> Bloklar(GprBolum b, StaVeri v, ISet<string> katlar)
        {
            var t = Tanimlar.First(x => x.Anahtar == b.Anahtar);
            var tablo = t.Uret(v, katlar);
            var bloklar = new List<Blok>
            {
                new BolumBasiBlok { Bolum = b, Baslik = ModernBolumler.Tr(b.Baslik), Aciklama = null }
            };
            if (t.Aciklama != null) bloklar.Add(new MetinBlok { Satirlar = new List<MetinSatiri> { new MetinSatiri { Parcalar = { (0, t.Aciklama) } } } });
            bloklar.AddRange(BolumDizgi.ResimBloklari(b));
            if (t.Anahtar == "KIRIS STATIK HESAP SONUCLARI")
                bloklar.Add(new CizimBlok { H = 5f, Cizici = s => KirisSemasi.Aciklama(s, ModernSayfa.Kenar, s.Y) });

            // GPR doğrulaması (yalnız kat filtresi yokken anlamlı)
            string not = $"Veri kaynağı: {Path.GetFileNameWithoutExtension(v.Bea.Yol)}.{t.Kaynak} sonuç dosyası.";
            if (katlar == null && b.Sayfalar.Count > 0)
            {
                var gpr = GprAyristirici.Ayristir(b).OfType<TabloOgesi>().ToList();
                if (gpr.Count > 0)
                {
                    var s = GprKarsilastirma.TabloKarsilastir(b.Baslik, tablo, gpr, t.SatirBazli);
                    not += $"  STA4 GPR karşılaştırması: {s.Esit}/{s.Hucre - s.DosyadaYok} hücre aynı" + (s.Farkli > 0 ? $", {s.Farkli} farklı" : "") + ".";
                }
            }
            if (tablo.Govde.Any(r => r.Hucreler.Any(h => h.Metin.Contains(DosyaTablolari.Yok))))
                not += "  \"—\": STA4'ün yazdırma anında hesapladığı, sonuç dosyalarında bulunmayan değer.";
            tablo.Notlar.Add(not);
            bloklar.Add(new TabloBlok { Tablo = tablo });
            return bloklar;
        }
    }
}
