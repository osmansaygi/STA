using System.Drawing;
using System.Text.RegularExpressions;
using StatikRaporClaude.Gpr;

namespace StatikRaporClaude.Dizgi
{
    /// <summary>
    /// STA4 GPR bölümünü içerik öğelerine ayırır: başlık, kutu çizgili tablo, anahtar-değer listesi, düz metin, şekil.
    /// Değerler metin olarak taşınır; sayılar değiştirilmez.
    /// </summary>
    public static class GprAyristirici
    {
        private const string KutuKose = "┌┬┐├┼┤└┴┘";
        private const float GovdeUst = 135f, CizgiPayi = 50f;
        /// <summary>Başlığın yanında (biraz üstünde) duran resim de o başlığın bölümüne sayılır.</summary>
        private const float ResimPayi = 30f;

        /// <param name="atilan">Bilerek atılan tekrarlar (sayfa başı tablo başlıkları, tekrar eden bölüm başlığı) buraya yazılır.</param>
        public static List<Oge> Ayristir(GprBolum bolum, List<string> atilan = null)
        {
            var sonuc = new List<Oge>();
            string sonBaslik = GprBolumleri.Anahtar(bolum.Baslik);
            var resimler = new HashSet<int>();   // STA4 aynı resmi her sayfada tekrar basar (yük işaret kuralları): bir kez
            foreach (var s in bolum.Sayfalar)
            {
                float ust = Math.Max(GovdeUst, bolum.UstSinir(s)), alt = bolum.AltSinir(s);
                var sayfaOgeleri = Sayfa(s, ust, alt);
                // STA4 kütüphane resimleri (yan logo/lisans deseni y > 2500 hariç)
                foreach (var r in s.Resimler.Where(r => r.Y < 2500 && Modern.StaResimleri.Var(r.No) && ResimSahibi(s, r) == bolum))
                    sayfaOgeleri.Add(new ResimOgesi { Sayfa = s, Y = Math.Max(r.Y, ust), No = r.No });
                sayfaOgeleri = sayfaOgeleri.OrderBy(o => o.Y).ToList();
                foreach (var o in sayfaOgeleri)
                {
                    if (o is ResimOgesi ro && !resimler.Add(ro.No)) { continue; }
                    // STA her sayfada bölüm başlığını tekrarlar: aynı başlık bir kez
                    if (o is BaslikOgesi b && b.Seviye == 1)
                    {
                        string a = GprBolumleri.Anahtar(b.Metin);
                        if (a == sonBaslik) { atilan?.Add(b.Metin); continue; }
                        sonBaslik = a;
                    }
                    // sayfa başında düz yazıyla tekrarlanan bölüm başlığı
                    if (o is MetinOgesi mo && mo.Satirlar.Count == 1 &&
                        GprBolumleri.Anahtar(string.Join(" ", mo.Satirlar[0].Parcalar.Select(p => p.Metin))) == sonBaslik)
                    {
                        atilan?.AddRange(mo.Satirlar[0].Parcalar.Select(p => p.Metin));
                        continue;
                    }
                    // sayfa geçişinde bölünmüş tablo: aynı sütun yapısındaysa satırlar öncekine eklenir
                    if (o is TabloOgesi t && sonuc.LastOrDefault() is TabloOgesi onceki && onceki.Sayfa != t.Sayfa
                        && (onceki.Imza == t.Imza || (t.Baslik.Count == 0 && onceki.Sutunlar.SequenceEqual(t.Sutunlar))))
                    {
                        if (t.Govde.Count > 0) t.Govde[0].GrupBasi = true;
                        atilan?.AddRange(t.Baslik.SelectMany(r => r.Hucreler).Select(h => h.Metin));
                        onceki.Govde.AddRange(t.Govde);
                        onceki.Notlar.AddRange(t.Notlar);
                        onceki.Sayfa = t.Sayfa;
                        continue;
                    }
                    sonuc.Add(o);
                }
            }
            return sonuc;
        }

        /// <summary>
        /// Resmin ait olduğu bölüm: STA4 açıklama resmini bölüm başlığının üstüne ya da yanına basar. Aynı sayfada resimden
        /// sonra başlayan ilk bölüm; yoksa resmin içinde durduğu bölüm.
        /// </summary>
        private static GprBolum ResimSahibi(GprSayfa s, GprResim r)
        {
            var icinde = s.Bolumler.Where(b => b.UstSinir(s) <= r.Y).OrderByDescending(b => b.UstSinir(s)).FirstOrDefault();
            // önceki sayfada da aynı resim varsa sayfa başında tekrarlanan lejanttır (yük işaret kuralı): içinde durduğu bölümün
            int i = icinde?.Sayfalar.IndexOf(s) ?? -1;
            if (i > 0 && icinde.Sayfalar[i - 1].Resimler.Any(x => x.No == r.No)) return icinde;
            return s.Bolumler.Where(b => b.UstSinir(s) >= r.Y - ResimPayi).OrderBy(b => b.UstSinir(s)).FirstOrDefault() ?? icinde;
        }

        // ---------------- sayfa ----------------

        private static bool TabloSatiriMi(GprYazi y)
        {
            if (y.Font == 0) return false;
            string t = y.Metin.TrimStart();
            return t.Length > 0 && (t[0] == '│' || KutuKose.IndexOf(t[0]) >= 0);
        }

        public static List<Oge> Sayfa(GprSayfa s, float ust, float alt)
        {
            var ogeler = new List<Oge>();
            var yazilar = s.Yazilar.Where(y => y.Y >= ust && y.Y < alt && !string.IsNullOrWhiteSpace(y.Metin))
                .OrderBy(y => y.Y).ThenBy(y => y.X).ToList();

            // 0) kat diyaframları: kutu karakterleriyle çizilmiş kat kesiti → şema öğesi
            var diyBas = yazilar.FirstOrDefault(y => y.Tip is -10 or -11 or -12 && GprBolumleri.Anahtar(y.Metin).Contains("KAT DIYAFRAM"));
            if (diyBas != null)
            {
                var d = new DiyaframOgesi { Sayfa = s, Y = diyBas.Y + 1 };
                var bekleyen = new List<string>();
                var kullanilan = new HashSet<GprYazi>();
                float son = diyBas.Y;
                foreach (var y in yazilar.Where(y => y.Y > diyBas.Y && y.Font != 0))
                {
                    if (y.Tip is -10 or -11 or -12 || y.Y - son > 60) break;
                    son = y.Y;
                    kullanilan.Add(y);
                    var km = Regex.Match(y.Metin, @"Kat\s*:\s*([^\s─│┌┬┐├┼┤└┴┘]+)");
                    if (km.Success) { d.Katlar.Add((km.Groups[1].Value, bekleyen.ToList())); bekleyen.Clear(); }
                    else bekleyen.AddRange(Regex.Matches(y.Metin, @"[^\s─│┌┬┐├┼┤└┴┘]+").Select(m => m.Value));
                }
                if (d.Katlar.Count > 0) { ogeler.Add(d); yazilar = yazilar.Where(y => !kullanilan.Contains(y)).ToList(); }
            }

            // 1) tablolar: ardışık tablo satırları
            var tablolar = new List<(List<GprYazi> Satirlar, float Ust, float Alt)>();
            List<GprYazi> aktif = null;
            foreach (var y in yazilar.Where(y => y.Font != 0))
            {
                if (TabloSatiriMi(y))
                {
                    // "┌──── X YÖNÜ ────┬──── Y YÖNÜ ────┐" üst başlığı altındaki tabloya katılır
                    bool ustBaslik = aktif != null && aktif.Count == 1 && UstBaslikSatiri(aktif[0].Metin);
                    bool yeni = aktif == null || y.Y - aktif[^1].Y > 45 || (y.Metin.TrimStart()[0] == '┌' && !ustBaslik);
                    if (yeni) { aktif = new List<GprYazi>(); tablolar.Add((aktif, 0, 0)); }
                    aktif.Add(y);
                }
                else if (aktif != null && y.Y - aktif[^1].Y > 5) aktif = null;
            }
            var tabloSatirlari = new HashSet<GprYazi>();
            var tabloAlanlari = new List<(float Ust, float Alt)>();
            foreach (var (satirlar, _, _) in tablolar)
            {
                var t = TabloCoz(satirlar.Select(x => x.Metin).ToList());
                if (t == null) continue;
                t = YukAnalizi(t) ?? t;
                t.Sayfa = s; t.Y = satirlar[0].Y;
                ogeler.Add(t);
                foreach (var x in satirlar) tabloSatirlari.Add(x);
                tabloAlanlari.Add((satirlar[0].Y - 15, satirlar[^1].Y + 35));
            }
            bool TabloAlaninda(float y1, float y2) => tabloAlanlari.Any(a => y1 >= a.Ust - 5 && y2 <= a.Alt + 5);

            // 2) şekiller: çizgi/kutu/orantılı yazı kümeleri
            var parcalar = new List<RectangleF>();
            foreach (var c in s.Cizgiler)
            {
                float y1 = Math.Min(c.Y1, c.Y2), y2 = Math.Max(c.Y1, c.Y2), x1 = Math.Min(c.X1, c.X2), x2 = Math.Max(c.X1, c.X2);
                if (y2 < ust || y1 >= alt) continue;
                if (y2 <= GovdeUst || x2 <= 95 || (x1 == x2 && (x1 <= 100 || x1 >= 1985)) || y1 >= 2855) continue; // STA çerçevesi
                // çerçeve ölçüsü projeye göre değişir (SPN_04: sağ 1970, alt 2840): sayfa boyu dikey ve gövde dışı tam genişlik yatay çizgi
                if ((x1 == x2 && y2 - y1 > 2400) || (y1 == y2 && x2 - x1 > 1700 && y1 >= 2780)) continue;
                if (TabloAlaninda(y1, y2)) continue;                                                          // tablo üstü renkli sütun çizgileri
                parcalar.Add(RectangleF.FromLTRB(x1, y1, Math.Max(x2, x1 + 1), Math.Max(y2, y1 + 1)));
            }
            foreach (var k in s.Kutular)
            {
                var r = k.Alan;
                if (r.Bottom < ust || r.Top >= alt || TabloAlaninda(r.Top, r.Bottom)) continue;
                parcalar.Add(r);
            }
            foreach (var y in yazilar.Where(y => y.Font == 0))
                parcalar.Add(new RectangleF(y.X, y.Y, Math.Max(20, y.Metin.Length * 11), 22));

            var govde = RectangleF.FromLTRB(100, Math.Max(GovdeUst, ust), 1990, Math.Min(2860, alt));
            foreach (var kume0 in Kumele(parcalar))
            {
                // perspektif çizimler sayfa dışına taşabilir: STA sayfa gövdesiyle sınırla
                var kume = RectangleF.Intersect(kume0, govde);
                if (kume.Width <= 0 || kume.Height <= 0) continue;
                // yalnız ince bir çizgi (başlık altı çizgisi vb.) şekil sayılmaz
                if (kume.Height < 12 && kume.Width < 400) continue;
                var alan = RectangleF.Inflate(kume, 15, 15);
                var sekil = new SekilOgesi { Sayfa = s, Y = kume.Top, Alan = alan };
                foreach (var y in yazilar.Where(y => !tabloSatirlari.Contains(y) && alan.Contains(y.X + 2, y.Y + 10)))
                    sekil.Yazilar.Add(y);
                // yazısız tek yatay çizgi (tablo grupları arası ayraç) şekil değil
                if (kume.Height < 12 && sekil.Yazilar.Count == 0) continue;
                ogeler.Add(sekil);
            }
            var sekilYazilari = new HashSet<GprYazi>(ogeler.OfType<SekilOgesi>().SelectMany(x => x.Yazilar));

            // 3) başlık ve metin
            MetinOgesi metin = null;
            AnahtarDegerOgesi ad = null;
            float sonY = -1000;
            foreach (var y in yazilar.Where(y => y.Font != 0 && !tabloSatirlari.Contains(y) && !sekilYazilari.Contains(y)))
            {
                string m = y.Metin.TrimEnd();
                bool kopuk = y.Y - sonY > 45;
                sonY = y.Y;
                if (y.Tip == -10 || y.Tip == -11 || y.Tip == -12)
                {
                    ogeler.Add(new BaslikOgesi { Sayfa = s, Y = y.Y, Metin = Sade(m), Seviye = 1 });
                    metin = null; ad = null; continue;
                }
                if ((y.Tip == -8 || y.Tip == -9) && !Regex.IsMatch(m.Trim(), @"\s{3,}") && m.Trim().Length < 70)
                {
                    ogeler.Add(new BaslikOgesi { Sayfa = s, Y = y.Y, Metin = Sade(m), Seviye = 2 });
                    metin = null; ad = null; continue;
                }
                var kv = AnahtarDeger(m);
                if (kv.HasValue)
                {
                    if (ad == null || kopuk) { ad = new AnahtarDegerOgesi { Sayfa = s, Y = y.Y }; ogeler.Add(ad); }
                    ad.Satirlar.Add(kv.Value);
                    metin = null;
                    continue;
                }
                if (metin == null || kopuk) { metin = new MetinOgesi { Sayfa = s, Y = y.Y }; ogeler.Add(metin); }
                metin.Satirlar.Add(Satir(m, y.Tip < 0));
                ad = null;
            }
            return ogeler.OrderBy(o => o.Y).ToList();
        }

        private static string Sade(string m) => Regex.Replace(m.Trim(), @"\s{2,}", "  ");

        private static (string, string)? AnahtarDeger(string m)
        {
            // opsiyon listeleri: "Anahtar ..........birim.= değer" (işleç = < > <= >=); noktasız "çok kelimeli anahtar = değer"
            var e = Regex.Match(m, @"^\s*(?<k>[^<>]*?[^\s.<>])\s*\.{2,}[\s.]*(?<s>[^=<>:]*?)[\s.]*(?<o>>=|<=|=|<|>)\s*(?<v>\S.*)$");
            // "Min. çekme bölgesi TS500-2000 (As min=0,8.fctd/fyd).= 0.0026": parantezden sonra ".="
            if (!e.Success) e = Regex.Match(m, @"^\s*(?<k>[^:=]*\([^)]*\))\.(?<s>)(?<o>=)\s*(?<v>\S.*)$");
            // noktasız biçim yalnız tek "=" ve ":" içermeyen satırda (formül satırları "Ix = … Iy = …" metin kalır)
            if (!e.Success && m.Count(ch => ch == '=') == 1 && !m.Contains(':') && m.IndexOfAny(new[] { '<', '>' }) < 0)
                e = Regex.Match(m, @"^\s*(?<k>[^\s=\-][^=]*?\S\s+\S+\s+[^=]*?\S)\s+(?<s>)(?<o>=)\s+(?<v>\S.*)$");
            if (e.Success)
            {
                string k = e.Groups["k"].Value.Trim().TrimEnd('=').Trim();
                // "aralığı..S.......cm" → anahtar "aralığı S", birim "cm"
                var sp = Regex.Split(e.Groups["s"].Value.Trim('.', ' '), @"\.{2,}").Select(x => x.Trim('.', ' ')).Where(x => x.Length > 0).ToList();
                if (sp.Count > 1) { k += " " + string.Join(" ", sp.Take(sp.Count - 1)); sp.RemoveRange(0, sp.Count - 1); }
                string o = e.Groups["o"].Value, v = Regex.Replace(e.Groups["v"].Value.Trim(), @"\s{2,}", "   ");
                return Cift(k, sp.FirstOrDefault() ?? "", o == "=" ? v : o + " " + v);
            }

            var c = Regex.Match(m, @"^\s*(?<k>[^:]+?)(\s*\.{2,}\s*|\s{2,})(?<s>[^\s:]{0,14})\s*:\s*(?<v>.*)$");
            // "ANAHTAR (birim):  değer", "anahtar .: değer" ve "anahtar : değer" biçimleri
            if (!c.Success) c = Regex.Match(m, @"^\s*(?<k>[^:\s\-][^:]*?)\s*\.?:\s*(?<s>)(?<v>\S.*)$");
            if (!c.Success) return null;
            // değeri çok sütunlu satırlar (grafik sayfalarındaki "BETONARME : üstMsol altMsol …") hizalı metin kalır
            if (Regex.Matches(c.Groups["v"].Value.Trim(), @"\S\s{3,}(?=\S)").Count > 3) return null;
            // "1.  G+G+G+G+G   : Genel ölü yük" — liste numarası tek başına anahtar değil
            if (Regex.IsMatch(c.Groups["k"].Value.Trim(), @"^\d+\.?$"))
                return Cift(c.Groups["k"].Value.Trim().TrimEnd('.') + ". " + c.Groups["s"].Value, "", Regex.Replace(c.Groups["v"].Value.Trim(), @"\s{2,}", "   "));
            return Cift(c.Groups["k"].Value.Trim().TrimEnd('.'), c.Groups["s"].Value.Trim('.', ' '), Regex.Replace(c.Groups["v"].Value.Trim(), @"\s{2,}", "   "));
        }

        private static (string, string)? Cift(string anahtar, string sembol, string deger)
        {
            // "KATSAYISI...... D" gibi anahtar içi nokta dizileri
            anahtar = Regex.Replace(Regex.Replace(anahtar, @"\s*\.{3,}\s*", " "), @"\s{2,}", " ").Trim();
            if (anahtar.Length < 1 || anahtar.Length > 70) return null;
            return (sembol.Length > 0 ? $"{anahtar}  ({sembol.Trim('(', ')')})" : anahtar, deger);
        }

        // ---------------- yük analizi ----------------

        private static readonly Regex YukSatiri = new Regex(@"^(?<ad>.+?)\s+(?<a>-?[\d.]+)\s*(?<ab>t/m[²³])\s*[×x]\s*(?<k>-?[\d.]+)\s*m\s*:\s*(?<y>-?[\d.]+)$");
        private static readonly Regex ToplamSatiri = new Regex(@"^TOPLAM[.\s]*:\s*(?<y>-?[\d.]+)$", RegexOptions.IgnoreCase);

        /// <summary>
        /// Döşeme / kiriş yük analizi: STA tek sütunlu kutuya "Kaplama (MARLEY) 0.050 t/m³ × 0.003 m : 0.000" yazar.
        /// Sütunlara ayrılır (malzeme, birim ağırlık, kalınlık, yük); grup adı ara başlık, TOPLAM kalın satır olur.
        /// </summary>
        private static TabloOgesi YukAnalizi(TabloOgesi t)
        {
            if (t.Sutunlar.Count != 1 || t.Baslik.Count > 0) return null;
            var metin = t.Govde.Select(r => r.Hucreler.Count > 0 ? r.Hucreler[0].Metin.Trim() : "").ToList();
            var es = metin.Select(x => YukSatiri.Match(x)).ToList();
            var ilk = es.FirstOrDefault(x => x.Success);
            if (es.Count(x => x.Success) < 2) return null;
            bool doseme = ilk.Groups["ab"].Value == "t/m³";
            var y = new TabloOgesi { Sayfa = t.Sayfa, Y = t.Y, Sutunlar = new List<int> { 40, 14, 14, 14 }, Imza = "YUK ANALIZI|" + doseme };
            TabloSatir Satir(bool grupBasi, params string[] h)
            {
                var r = new TabloSatir { GrupBasi = grupBasi };
                for (int i = 0; i < h.Length; i++) r.Hucreler.Add(new TabloHucre { Ilk = i, Son = h.Length == 1 ? 3 : i, Metin = h[i] });
                return r;
            }
            y.Baslik.Add(Satir(false, "Malzeme", doseme ? "Birim ağırlık  t/m³" : "Birim yük  t/m²", doseme ? "Kalınlık  m" : "Yükseklik  m", doseme ? "Yük  t/m²" : "Yük  t/m"));
            for (int i = 0; i < metin.Count; i++)
            {
                if (es[i].Success)
                {
                    string ad = Regex.Replace(es[i].Groups["ad"].Value, @"\s+", " ").Replace("( ", "(").Replace(" )", ")");
                    y.Govde.Add(Satir(false, ad, es[i].Groups["a"].Value, es[i].Groups["k"].Value, es[i].Groups["y"].Value));
                }
                else if (ToplamSatiri.Match(metin[i]) is { Success: true } tm)
                {
                    var r = Satir(false, "Toplam", "", "", tm.Groups["y"].Value);
                    r.Vurgu = true;
                    y.Govde.Add(r);
                }
                else if (metin[i].Length > 0)
                {
                    var r = Satir(y.Govde.Count > 0, metin[i]);
                    r.AraBaslik = true;
                    y.Govde.Add(r);
                }
            }
            y.Notlar.AddRange(t.Notlar);
            return y;
        }

        private static MetinSatiri Satir(string m, bool kalin)
        {
            var satir = new MetinSatiri { Kalin = kalin };
            foreach (Match p in Regex.Matches(m, @"\S+(?:\s{1,2}\S+)*"))
                satir.Parcalar.Add((p.Index, p.Value));
            return satir;
        }

        /// <summary>Dikdörtgenleri dikey çakışma/yakınlıkla kümeler.</summary>
        private static List<RectangleF> Kumele(List<RectangleF> parcalar)
        {
            var kumeler = new List<RectangleF>();
            foreach (var p in parcalar.OrderBy(r => r.Top))
            {
                if (kumeler.Count > 0 && p.Top <= kumeler[^1].Bottom + CizgiPayi)
                    kumeler[^1] = RectangleF.Union(kumeler[^1], p);
                else kumeler.Add(p);
            }
            return kumeler;
        }

        // ---------------- tablo ----------------

        /// <summary>Üst kenar çizgisine yazılmış başlık: "┌──── X YÖNÜ ────┬──── Y YÖNÜ ────┐".</summary>
        private static bool UstBaslikSatiri(string s) => s.TrimStart().StartsWith("┌") && s.Any(char.IsLetter);

        public static TabloOgesi TabloCoz(List<string> satirlar)
        {
            if (satirlar.Count == 0) return null;
            bool Dikey(char c) => c == '│' || KutuKose.IndexOf(c) >= 0;
            var sinirlar = new SortedSet<int>();
            foreach (var s in satirlar.Where(s => KutuKose.IndexOf(s.TrimStart()[0]) >= 0))
                for (int i = 0; i < s.Length; i++) if (KutuKose.IndexOf(s[i]) >= 0) sinirlar.Add(i);
            foreach (var s in satirlar.Where(s => s.TrimStart()[0] == '│'))
                for (int i = 0; i < s.Length; i++) if (s[i] == '│') sinirlar.Add(i);
            if (sinirlar.Count < 2) return null;
            var b = sinirlar.ToList();
            var t = new TabloOgesi { Sutunlar = b.Zip(b.Skip(1), (x, y) => y - x).ToList() };

            var bekleyen = new List<TabloSatir>();
            bool baslikBitti = false, grupBasi = false;
            foreach (var s in satirlar)
            {
                char c = s.TrimStart()[0];
                if (UstBaslikSatiri(s))
                {
                    // birleşik üst başlık: köşe/ayraç konumları arasındaki yazı, kapsadığı sütunlara yayılır
                    var r = new TabloSatir();
                    var kose = Enumerable.Range(0, s.Length).Where(i => "┌┬┐".IndexOf(s[i]) >= 0).ToList();
                    for (int k = 0; k + 1 < kose.Count; k++)
                    {
                        int ilk = b.IndexOf(kose[k]), son = b.IndexOf(kose[k + 1]) - 1;
                        if (ilk < 0 || son < ilk) continue;
                        r.Hucreler.Add(new TabloHucre { Ilk = ilk, Son = son, Metin = s.Substring(kose[k] + 1, kose[k + 1] - kose[k] - 1).Trim('─', ' ') });
                    }
                    if (r.Hucreler.Count > 0) bekleyen.Add(r);
                    continue;
                }
                if (c == '├')
                {
                    if (!baslikBitti)
                    {
                        // ilk ayırıcıdan önceki satırlar başlıktır; ancak sayı ağırlıklıysa (ör. döşeme yük analizi grupları) veri grubudur
                        var h = bekleyen.SelectMany(r => r.Hucreler).Where(x => x.Metin.Length > 0).ToList();
                        int sayisal = h.Count(x => Regex.IsMatch(x.Metin, @"\d[.,]\d"));
                        bool veri = bekleyen.Count > 3 || (h.Count > 0 && sayisal * 3 >= h.Count);
                        if (veri) { t.Govde.AddRange(bekleyen); grupBasi = true; }
                        else t.Baslik.AddRange(bekleyen);
                        bekleyen.Clear();
                        baslikBitti = true;
                    }
                    else grupBasi = true;
                    continue;
                }
                if (c != '│') continue;
                var satir = new TabloSatir { GrupBasi = grupBasi && (t.Govde.Count > 0 || bekleyen.Count > 0) };
                var konum = new List<int>();
                for (int i = 0; i < s.Length; i++) if (s[i] == '│') konum.Add(i);
                for (int k = 0; k + 1 < konum.Count; k++)
                {
                    int ilk = b.IndexOf(konum[k]), son = b.IndexOf(konum[k + 1]) - 1;
                    if (ilk < 0 || son < ilk) continue;
                    satir.Hucreler.Add(new TabloHucre { Ilk = ilk, Son = son, Metin = Regex.Replace(s.Substring(konum[k] + 1, konum[k + 1] - konum[k] - 1).Trim(), @"\s{3,}", "  ") });
                }
                if (konum.Count > 0 && konum[^1] + 1 < s.Length)
                {
                    string not = s.Substring(konum[^1] + 1).Trim().Trim('─', '=').Trim();
                    if (not.Length > 0)
                    {
                        // tablo dışında sağa yazılmış toplam (Σ= 99.21) son hücreye değil nota
                        t.Notlar.Add(not);
                    }
                }
                if (satir.Hucreler.Count == 0 || satir.Hucreler.All(h => h.Metin.Length == 0)) { grupBasi = true; continue; }
                grupBasi = false;
                if (baslikBitti) t.Govde.Add(satir); else bekleyen.Add(satir);
            }
            // ├ hiç yoksa (başlıksız parça) tüm satırlar gövde
            t.Govde.InsertRange(0, bekleyen);
            if (t.Baslik.Count == 0 && t.Govde.Count == 0) return null;
            t.Imza = string.Join(",", t.Sutunlar) + "|" + string.Join("|", t.Baslik.SelectMany(r => r.Hucreler).Select(h => h.Metin));
            return t;
        }
    }
}
