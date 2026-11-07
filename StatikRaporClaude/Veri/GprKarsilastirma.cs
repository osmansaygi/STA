using System.Text.RegularExpressions;
using StatikRaporClaude.Dizgi;
using StatikRaporClaude.Gpr;
using StatikRaporClaude.Modern;
using StatikRaporClaude.Rapor;

namespace StatikRaporClaude.Veri
{
    /// <summary>
    /// Dosyadan üretilen tabloları GPR'deki karşılığıyla hücre hücre karşılaştırır.
    /// Satırlar eleman adına (ve aynı adın kaçıncı tekrarı olduğuna) göre eşlenir.
    /// </summary>
    public static class GprKarsilastirma
    {
        private static readonly Regex ElemanRx = new Regex(@"^((K|S|D)[A-Z0-9]{1,2}-\d+|\d+( (Pz|Mx|My))?$|PL\d+$)");   // eleman adı veya radye düğüm no

        public sealed class Sonuc
        {
            public string Tablo;
            public int Eleman, ElemanTam, Hucre, Esit, DosyadaYok, Farkli, EksikSatir, FazlaSatir;
            public readonly List<string> Ornekler = new List<string>();
            public override string ToString() =>
                $"{Tablo}: eleman {ElemanTam}/{Eleman} tam; hücre {Esit}/{Hucre - DosyadaYok} eşit" +
                (DosyadaYok > 0 ? $", dosyada olmayan {DosyadaYok}" : "") + (Farkli > 0 ? $", FARKLI {Farkli}" : "") +
                (EksikSatir + FazlaSatir > 0 ? $", satır farkı −{EksikSatir}/+{FazlaSatir}" : "");
        }

        // kutu çizgisinden ibaret hücreler (STA deprem başlık satırının ilk hücresi "├───┤") boş sayılır
        private static string N(string s) => Regex.Replace(Regex.Replace(s ?? "", "[─│├┤┌┐└┘┬┴┼]", ""), @"\s+", " ").Trim();

        private static List<string[]> Satirlar(IEnumerable<TabloOgesi> tablolar) =>
            tablolar.SelectMany(t => t.Govde).Select(r => r.Hucreler.OrderBy(h => h.Ilk).Select(h => N(h.Metin)).ToArray()).ToList();

        /// <summary>Satırları eleman bloklarına böler: anahtar = ad#tekrar.</summary>
        private static Dictionary<string, List<string[]>> Bloklar(List<string[]> satirlar, bool herSatirAyriEleman = false)
        {
            var d = new Dictionary<string, List<string[]>>();
            var sayac = new Dictionary<string, int>();
            string aktif = null, oncekiAd = null;
            foreach (var r in satirlar)
            {
                var m = r.Length > 0 ? ElemanRx.Match(r[0]) : Match.Empty;
                if (m.Success && (herSatirAyriEleman ? true : true))
                {
                    string ad = m.Value;
                    bool yeniBlok = !herSatirAyriEleman || ad != oncekiAd;
                    if (yeniBlok)
                    {
                        sayac[ad] = sayac.GetValueOrDefault(ad) + 1;
                        aktif = $"{ad}#{sayac[ad]}";
                        d[aktif] = new List<string[]>();
                    }
                    oncekiAd = ad;
                }
                if (aktif != null) d[aktif].Add(r);
            }
            return d;
        }

        public static Sonuc TabloKarsilastir(string ad, TabloOgesi dosya, IEnumerable<TabloOgesi> gpr, bool satirBazli = false)
        {
            var s = new Sonuc { Tablo = ad };
            var a = Bloklar(Satirlar(new[] { dosya }), satirBazli);
            var b = Bloklar(Satirlar(gpr), satirBazli);
            foreach (var (anahtar, gr) in b)
            {
                s.Eleman++;
                if (!a.TryGetValue(anahtar, out var dr)) { s.EksikSatir += gr.Count; if (s.Ornekler.Count < 12) s.Ornekler.Add($"  {anahtar}: dosyada yok"); continue; }
                bool tam = dr.Count == gr.Count;
                if (dr.Count < gr.Count) s.EksikSatir += gr.Count - dr.Count;
                if (dr.Count > gr.Count) s.FazlaSatir += dr.Count - gr.Count;
                for (int i = 0; i < Math.Min(dr.Count, gr.Count); i++)
                    for (int j = 0; j < gr[i].Length; j++)
                    {
                        string g = gr[i][j], f = j < dr[i].Length ? dr[i][j] : "";
                        if (g.Length == 0 && f.Length == 0) continue;
                        s.Hucre++;
                        if (f.Contains(DosyaTablolari.Yok)) { s.DosyadaYok++; continue; }
                        // STA4 sütuna sığmayan uzun metni keser ("…+ø10/24(s"): GPR hücresi dosyanın önekiyse eşit sayılır
                        if (f == g || (g.Length >= 40 && f.StartsWith(g))) s.Esit++;
                        else
                        {
                            s.Farkli++; tam = false;
                            if (s.Ornekler.Count < 12) s.Ornekler.Add($"  {anahtar} satır {i + 1} hücre {j + 1}: GPR '{g}' dosya '{f}'");
                        }
                    }
                if (tam) s.ElemanTam++;
            }
            foreach (var anahtar in a.Keys.Where(k => !b.ContainsKey(k))) { s.FazlaSatir += a[anahtar].Count; if (s.Ornekler.Count < 12) s.Ornekler.Add($"  {anahtar}: GPR'de yok"); }
            return s;
        }

        public static Sonuc KirisBetonarme(List<KirisSonucu> dosya, List<KirisSonucu> gpr)
        {
            var s = new Sonuc { Tablo = "Kiriş betonarme (KMX)" };
            var sayac = new Dictionary<string, int>();
            string Anahtar(KirisSonucu k, Dictionary<string, int> c) { c[k.Ad] = c.GetValueOrDefault(k.Ad) + 1; return $"{k.Ad}#{c[k.Ad]}"; }
            var d = dosya.ToDictionary(k => Anahtar(k, sayac), k => k);
            sayac.Clear();
            foreach (var g in gpr)
            {
                string an = Anahtar(g, sayac);
                s.Eleman++;
                if (!d.TryGetValue(an, out var f)) { s.Ornekler.Add($"  {an}: dosyada yok"); continue; }
                bool tam = true;
                void K(string alan, string gv, string fv)
                {
                    if (string.IsNullOrEmpty(gv) && string.IsNullOrEmpty(fv)) return;
                    s.Hucre++;
                    if (fv == DosyaTablolari.Yok) { s.DosyadaYok++; return; }
                    if (N(gv) == N(fv)) s.Esit++;
                    else { s.Farkli++; tam = false; if (s.Ornekler.Count < 15) s.Ornekler.Add($"  {an} {alan}: GPR '{gv}' dosya '{fv}'"); }
                }
                for (int i = 0; i < 5; i++) { K($"maxM{i}", g.MaxM[i], f.MaxM[i]); K($"As{i}", g.As[i], f.As[i]); }
                K("Asw sol", g.Asw[0], f.Asw[0]); K("Asw sağ", g.Asw[3], f.Asw[3]);
                K("boyuna", string.Join("+", g.Boyuna.OrderBy(x => x)), string.Join("+", f.Boyuna.OrderBy(x => x)));
                K("etriye", g.Etriye, f.Etriye);
                K("D/B", g.D + "/" + g.B, f.D + "/" + f.B);
                K("Wk", g.Wk, f.Wk);
                if (tam) s.ElemanTam++;
            }
            return s;
        }

        public static Sonuc KolonBetonarme(List<KolonSonucu> dosya, List<KolonSonucu> gpr)
        {
            var s = new Sonuc { Tablo = "Kolon betonarme (SMX)" };
            var d = dosya.GroupBy(k => k.Ad).ToDictionary(x => x.Key, x => x.First());
            foreach (var g in gpr)
            {
                s.Eleman++;
                if (!d.TryGetValue(g.Ad, out var f)) { s.Ornekler.Add($"  {g.Ad}: dosyada yok"); continue; }
                bool tam = true;
                void K(string alan, string gv, string fv)
                {
                    s.Hucre++;
                    if (fv == DosyaTablolari.Yok) { s.DosyadaYok++; return; }
                    if (N(gv) == N(fv)) s.Esit++;
                    else { s.Farkli++; tam = false; if (s.Ornekler.Count < 15) s.Ornekler.Add($"  {g.Ad} {alan}: GPR '{gv}' dosya '{fv}'"); }
                }
                for (int r = 0; r < Math.Min(6, g.Kombinasyonlar.Count); r++)
                    for (int c = 0; c < 7; c++) K($"komb{r + 1}.{c}", g.Kombinasyonlar[r][c], f.Kombinasyonlar[r][c]);
                K("bx/by", g.Bx + "/" + g.By, f.Bx + "/" + f.By);
                K("I/J", g.I + "/" + g.J, f.I + "/" + f.J);
                K("Hk", g.Hk, f.Hk);
                K("βx/βy", g.BetaX + "/" + g.BetaY, f.BetaX + "/" + f.BetaY);
                K("boyuna", g.Boyuna, f.Boyuna);
                K("etriye", g.Etriye, f.Etriye);
                K("As/Ac", g.AsAc, f.AsAc);
                if (tam) s.ElemanTam++;
            }
            return s;
        }

        /// <summary>Tüm dosya tablolarını GPR ile karşılaştırır.</summary>
        public static List<string> Hepsi(StaVeri v, GprBelgesi g)
        {
            var kalemler = RaporKatalogu.Dagit(g);
            List<TabloOgesi> Gpr(string kalem) => kalemler.First(k => k.Ad == kalem).Bolumler
                .SelectMany(b => GprAyristirici.Ayristir(b)).OfType<TabloOgesi>().ToList();
            GprBolum Bolum(string kalem, string anahtar) => kalemler.First(k => k.Ad == kalem).Bolumler.FirstOrDefault(b => b.Anahtar.Contains(anahtar));

            var sonuclar = new List<Sonuc>
            {
                TabloKarsilastir("Kiriş statik (BEA)", DosyaTablolari.KirisStatik(v), Gpr("Kiriş statik sonuçları")),
                TabloKarsilastir("Kolon statik (COL)", DosyaTablolari.KolonStatik(v), Gpr("Kolon statik sonuçları")),
                TabloKarsilastir("Döşeme statik (FLO)", DosyaTablolari.DosemeStatik(v), Gpr("Döşeme statik sonuçları")),
                TabloKarsilastir("Döşeme betonarme (DMX)", DosyaTablolari.DosemeBetonarme(v), Gpr("Döşeme betonarme sonuçları")),
            };
            var yuk = kalemler.First(k => k.Ad == "Eleman yük bilgileri").Bolumler;
            sonuclar.Add(TabloKarsilastir("Kiriş düşey yük (LOA)", DosyaTablolari.KirisYuk(v),
                yuk.Where(b => b.Anahtar.Contains("KIRIS DUSEY")).SelectMany(b => GprAyristirici.Ayristir(b)).OfType<TabloOgesi>(), true));
            sonuclar.Add(TabloKarsilastir("Kolon yük (LOA)", DosyaTablolari.KolonYuk(v),
                yuk.Where(b => b.Anahtar.Contains("KOLON YUK")).SelectMany(b => GprAyristirici.Ayristir(b)).OfType<TabloOgesi>(), true));
            var kb = Bolum("Kiriş betonarme sonuçları", "KIRIS BETONARME");
            if (kb != null) sonuclar.Add(KirisBetonarme(DosyaTablolari.KirisBetonarme(v), BetonarmeSonuclari.Kirisler(kb)));
            var kob = Bolum("Kolon betonarme sonuçları", "KOLON BETONARME");
            if (kob != null) sonuclar.Add(KolonBetonarme(DosyaTablolari.KolonBetonarme(v), BetonarmeSonuclari.Kolonlar(kob)));
            if (RadyeVerisi.Var(v.St4Yolu))
            {
                List<TabloOgesi> Radye(string kalem) => kalemler.Where(k => k.Sekme == "Radye" && k.Ad == kalem).SelectMany(k => k.Bolumler)
                    .SelectMany(b => GprAyristirici.Ayristir(b)).OfType<TabloOgesi>().ToList();
                sonuclar.Add(TabloKarsilastir("Radye nokta deplasmanları (RAD+DE1)", RadyeTablolari.Deplasmanlar(v), Radye("Deplasmanlar"), true));
                sonuclar.Add(TabloKarsilastir("Radye zemin gerilmesi (RAD+DE1)", RadyeTablolari.ZeminGerilmesi(v), Radye("Zemin gerilmesi"), true));
                var mesh = kalemler.Where(k => k.Sekme == "Radye" && k.Ad == "Radye mesh bilgileri").SelectMany(k => k.Bolumler).ToList();
                sonuclar.Add(TabloKarsilastir("Radye plak bilgileri (RAD)", RadyeTablolari.Plaklar(v),
                    mesh.Where(b => b.Anahtar.Contains("PLAK")).SelectMany(b => GprAyristirici.Ayristir(b)).OfType<TabloOgesi>(), true));
                sonuclar.Add(TabloKarsilastir("Radye mesh bilgileri (RAD)", RadyeTablolari.Meshler(v),
                    mesh.Where(b => b.Anahtar.Contains("MESH")).SelectMany(b => GprAyristirici.Ayristir(b)).OfType<TabloOgesi>(), true));
                sonuclar.Add(TabloKarsilastir("Radye nokta yükleri (RAD)", RadyeTablolari.NoktaYukleri(v),
                    kalemler.Where(k => k.Sekme == "Radye" && k.Ad == "Radye nokta yük bilgileri").SelectMany(k => k.Bolumler).Where(b => b.Anahtar.Contains("NOKTA"))
                        .SelectMany(b => GprAyristirici.Ayristir(b)).OfType<TabloOgesi>(), true));
                sonuclar.Add(TabloKarsilastir("Radye Winkler (RAD+DE1)", RadyeTablolari.Winkler(v), Radye("Winkler yay yükleri"), true));
                sonuclar.Add(TabloKarsilastir("Radye zemin kontrolü (RAD+DE1)", RadyeTablolari.ZeminKontrolu(v), Radye("Zemin gerilmesi kontrolü"), true));
            }

            var satirlar = new List<string>();
            foreach (var s in sonuclar) { satirlar.Add(s.ToString()); satirlar.AddRange(s.Ornekler); }
            return satirlar;
        }
    }
}
