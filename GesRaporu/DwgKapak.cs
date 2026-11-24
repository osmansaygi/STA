using System.Text.RegularExpressions;

namespace GesRaporu
{
    /// <summary>
    /// Mimari proje kapağı (antet/künye) okuyucu. Farklı mimarlık bürolarının kapakları incelenerek yazıldı:
    /// etiket yazımı (PAFTA / ADA / PARSEL, PAFTA-ADA-PARSEL, Pafta / Ada / Parsel, PAFTA,ADA,PARSEL …),
    /// değer yeri (etiketin sağında, başlık satırının altında, sahip kutusunda üstte/altta),
    /// birleşik değerler (AZİZİYE/ KARATAY, M28b05b2a-43704- 1, -/ 15644 / 7) ve
    /// aynı çizimde birden çok kapak (A/B/C BLOK) desteklenir.
    /// </summary>
    public sealed partial class DwgBilgi
    {
        // ------------------------------------------------------------------ etiket sözlüğü
        /// <summary>Karşılaştırma anahtarı: büyük harf, Türkçe harfler ASCII, yalnız harf ve rakam.</summary>
        public static string Anahtar(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var u = s.ToUpper(Tr);
            var sb = new System.Text.StringBuilder(u.Length);
            foreach (var c in u)
            {
                char d = c switch { 'İ' or 'I' or 'ı' => 'I', 'Ş' => 'S', 'Ğ' => 'G', 'Ü' => 'U', 'Ö' => 'O', 'Ç' => 'C', 'Â' => 'A', 'Î' => 'I', 'Û' => 'U', _ => c };
                if (char.IsLetterOrDigit(d)) sb.Append(d);
            }
            return sb.ToString();
        }

        private const string Pap = "PAFTA/ADA/PARSEL", MahIlce = "MAH/İLÇE", IlceMah = "İLÇE/MAH", Yeri = "YERİ", Sahip = "YAPI SAHİBİ";

        private static readonly Dictionary<string, string> EtiketAlani = Sozluk(
            ("İL", "IL ILI"),
            ("İLÇE", "ILCE ILCESI"),
            ("MAHALLE", "MAHALLE MAHALLESI MAHALLESOKAK MAHALLECADDESOKAK MAHALLEMEVKI MAHALLEKOY MAHALLEKOYU MAHALLEKOYMEVKI"),
            (MahIlce, "MAHALLESIILCESI MAHALLEILCE MAHALLEILCESI"),
            (IlceMah, "ILCEMAHALLE ILCESIMAHALLESI ILCEMAHALLESI"),
            (Yeri, "YERI YAPININYERI"),
            (Pap, "PAFTAADAPARSEL PAFTAADAPARSELNO ADAPARSEL ADAPARSELNO PAFTAADAPARSELNUMARASI"),
            ("PAFTA", "PAFTA PAFTANO"),
            ("ADA", "ADA ADANO"),
            ("PARSEL", "PARSEL PARSELNO"),
            ("YÜZÖLÇÜMÜ", "YUZOLCUMU PARSELALANI ARSAALANI"),
            ("CİNSİ", "CINSI YAPICINSI YAPININCINSI INSAATTURU YAPITURU TASIYICISISTEM"),
            ("KAT ADEDİ", "KATADEDI KATADETI KATSAYISI INSAATKATSAYISI"),
            ("KULLANIM AMACI", "KULLANIMAMACI"),
            ("BLOK", "BLOKADI BLOK BLOKNO"),
            (Sahip, "YAPISAHIBI PROJESAHIBI PROJENINSAHIBI SAHIBI MALSAHIBI ISSAHIBI ISVEREN MALIKI MALIK YAPISAHIBIVEKILI YAPISAHIPLERI " +
                    "PROJESAHIPLERI PROJESAHIBIADISOYADI PROJESAHIBININADISOYADI PROJESAHIBIUNVANI YAPISAHIBIADISOYADI YAPISAHIBININADISOYADI " +
                    "MALSAHIPLERI SAHIPLERI ARSASAHIBI ARSASAHIPLERI"),
            ("MÜELLİF", "MIMARIPROJEMUELLIFI PROJEMUELLIFI PROJENINMUELLIFI"));

        /// <summary>Alan değeri olmayan ama etiket olan kapak yazıları (değer aranırken etiket sayılır).</summary>
        private static readonly HashSet<string> DigerEtiketler = new(
            ("NITELIGI IHTIVAETTIGIYER IHTIVAETTIGIYAPI BINAINSAATALANI YAPIINSAATALANI INSAATALANI TOPLAMINSAATALANI " +
             "MMALIYETI M2MALIYETI MALIYETI TOPLAMMALIYETI TOPLAMMALIYET YAPISINIFI MIMARISINIFI MIMARIHIZMETSINIFI " +
             "ARSA BINA ARSABILGILERI BINABILGILERI PARSELBILGILERI ADISOYADI ADSOYAD ADI SOYADI ODASICIL ODASICILNO SICILNO " +
             "BUROTESCILI BUROTESNO BUROTESCILNO UNVANI YAPIMUTEAHHITI YAPIMUTEAHITI MUTEAHHIT DENETCI DENETCIMIMAR " +
             "YAPIDENETIMFIRMASI BELEDIYEONAYI TAKS KAKS HMAX SOKAK CADDE CADDESOKAK YAPININ OLCEK PAFTAISMI BOLUM CIZEN " +
             "TARIH IMZA BELGE SICIL PROJE ADRES MIMHIZSINORANI MIMHIZSNFORANI MIMHIZSINIFORANI MIMHIZSNFVEORANI " +
             "MIMHIZSINORTAL YASALUYARI").Split(' ', StringSplitOptions.RemoveEmptyEntries));

        /// <summary>Kişi kutusundaki alt etiketler (sahip değeri bunların sağında/altında olabilir).</summary>
        private static readonly HashSet<string> AltEtiketler = new("ADISOYADI ADSOYAD ADISOYADIUNVANI UNVANI ADI".Split(' '));

        private static readonly string[] EtiketKokleri =
            { "MALIYETI", "MALIYET", "SINIFI", "ORANI", "ONAYI", "SICIL", "TESCIL", "SOYADI", "MUELLIFI", "DENETCI", "DENETIM",
              "MUTEAHHITI", "MUTEAHITI", "ALANI", "IMZA", "UNVANI", "KURULUSU", "BILGILERI" };

        private static Dictionary<string, string> Sozluk(params (string Alan, string Etiketler)[] l)
        {
            var d = new Dictionary<string, string>();
            foreach (var (alan, et) in l)
                foreach (var e in et.Split(' ', StringSplitOptions.RemoveEmptyEntries)) d[e] = alan;
            return d;
        }

        private static string AlanOf(Yazi y) => EtiketAlani.GetValueOrDefault(Anahtar(y.Metin));

        /// <summary>Yazı bir kapak etiketi mi (bilinen etiket, ':' ile biten veya etiket kökü içeren kısa yazı)?</summary>
        private static bool EtiketMi(Yazi y)
        {
            var a = Anahtar(y.Metin);
            if (a.Length == 0) return false;
            if (EtiketAlani.ContainsKey(a) || DigerEtiketler.Contains(a)) return true;
            var m = y.Metin.Trim();
            if (m.EndsWith(':') && m.Length < 40) return true;
            var kelimeler = Regex.Split(m, @"[\s/.\-]+").Select(Anahtar).Where(k => k.Length > 0).ToList();
            return kelimeler.Count <= 5 && kelimeler.Any(k => EtiketKokleri.Contains(k));
        }

        private static bool BosMu(string m) => !Regex.IsMatch(m ?? "", @"[\p{L}\p{N}]");

        /// <summary>Kanun/uyarı metni, adres, iletişim satırı: değer olamaz.</summary>
        private static bool Kalip(string m)
        {
            var u = m.ToUpper(Tr);
            return m.Length > 90 || u.Contains("SAYILI") || u.Contains("KANUN") || u.Contains("YÖNETMELİ") || u.Contains('@')
                || Regex.IsMatch(u, @"\bTEL\s*[:.]|\bFAX\b|\bGSM\b|WWW\.");
        }

        // ------------------------------------------------------------------ geometrik arama
        private static double Gen(Yazi y) => Math.Max(y.X1 - y.X0, y.H);

        private static bool AyniSatir(Yazi a, Yazi b) => Math.Abs(a.Yc - b.Yc) <= 0.6 * Math.Max(a.H, b.H);

        /// <summary>Yazı yüksekliği etiketle uyumlu mu (süs harfleri, başlıklar ve dipnotlar elenir)?</summary>
        private static bool BoyUygun(Yazi lab, Yazi c) => c.H >= 0.4 * lab.H && c.H <= 1.6 * lab.H;

        private sealed class Bolge
        {
            public readonly List<Yazi> Liste; // yatay yazılar, Yc'ye göre sıralı
            public readonly List<Yazi> Dikey; // 90° yan etiketler
            private readonly double[] _yc;
            public Bolge(IEnumerable<Yazi> y)
            {
                var l = y.Where(x => x.Metin.Length > 0).ToList();
                Liste = l.Where(x => !x.Egik).OrderBy(x => x.Yc).ToList();
                Dikey = l.Where(x => x.Egik && DikeyAci(x.Aci)).ToList();
                _yc = Liste.Select(x => x.Yc).ToArray();
            }
            public IEnumerable<Yazi> Bant(double y0, double y1)
            {
                int i = Array.BinarySearch(_yc, y0);
                if (i < 0) i = ~i;
                while (i > 0 && _yc[i - 1] >= y0) i--;
                for (; i < _yc.Length && _yc[i] <= y1; i++) yield return Liste[i];
            }
        }

        /// <summary>Etiketin sağındaki ilk yazı (aynı satır). Etiket blok içindeyse önce aynı blok aranır.</summary>
        private static Yazi SagYazi(Yazi lab, Bolge b)
        {
            double h = lab.H, gw = Gen(lab);
            var adaylar = b.Bant(lab.Yc - 1.6 * h, lab.Yc + 1.6 * h)
                .Where(c => !ReferenceEquals(c, lab) && AyniSatir(lab, c) && c.X0 > lab.X0 + 0.5 * gw && c.X0 - lab.X1 < 35 * h
                            && !BosMu(c.Metin) && BoyUygun(lab, c))
                .ToList();
            if (lab.Grup != 0 && adaylar.Any(c => c.Grup == lab.Grup)) adaylar = adaylar.Where(c => c.Grup == lab.Grup).ToList();
            return adaylar.OrderBy(c => c.X0).FirstOrDefault();
        }

        /// <summary>Aynı satırdaki diğer etiketler (sütun başlıkları).</summary>
        private static List<Yazi> SatirEtiketleri(Yazi lab, Bolge b) =>
            b.Bant(lab.Yc - 1.6 * lab.H, lab.Yc + 1.6 * lab.H)
             .Where(c => !ReferenceEquals(c, lab) && AyniSatir(lab, c) && Math.Abs(c.Cx - lab.Cx) < 45 * lab.H && EtiketMi(c)
                         && (lab.Grup == 0 || c.Grup == lab.Grup))
             .ToList();

        /// <summary>
        /// Sütunda etiketin altındaki (yon=-1) veya üstündeki (yon=+1) ilk değer. Aday, satırdaki diğer sütun başlıklarına
        /// değil bu etikete en yakın olmalı. İlk rastlanan etiket aramayı keser (alt etiketler sahip aramasında atlanır).
        /// </summary>
        private static Yazi SutunYazisi(Yazi lab, Bolge b, int yon, double enFazla, bool kisi) =>
            lab.Grup != 0
                ? SutunYazisi(lab, b, yon, enFazla, kisi, true) ?? SutunYazisi(lab, b, yon, enFazla, kisi, false)
                : SutunYazisi(lab, b, yon, enFazla, kisi, false);

        private static Yazi SutunYazisi(Yazi lab, Bolge b, int yon, double enFazla, bool kisi, bool ayniGrup)
        {
            double h = lab.H;
            var komsu = SatirEtiketleri(lab, b);
            double y0 = yon < 0 ? lab.Yc - enFazla * h : lab.Yc + 0.3 * h;
            double y1 = yon < 0 ? lab.Yc - 0.3 * h : lab.Yc + enFazla * h;
            var adaylar = b.Bant(y0, y1)
                .Where(c => !ReferenceEquals(c, lab) && !AyniSatir(lab, c) && !BosMu(c.Metin) && BoyUygun(lab, c)
                            && (!ayniGrup || c.Grup == lab.Grup)
                            && c.X1 > lab.X0 - 3 * h && c.X0 < lab.X1 + 3 * h
                            && komsu.All(k => Math.Abs(c.Cx - lab.Cx) <= Math.Abs(c.Cx - k.Cx)))
                .OrderBy(c => Math.Abs(c.Yc - lab.Yc)).ThenBy(c => Math.Abs(c.Cx - lab.Cx));
            foreach (var c in adaylar)
            {
                var a = Anahtar(c.Metin);
                if (kisi && (AltEtiketler.Contains(a) || UnvanMi(c.Metin) || Regex.IsMatch(c.Metin, @"^\s*AD[Iİ]\s*[-/]?\s*SOYAD[Iİ]\s*:?\s*$", RegexOptions.IgnoreCase))) continue;
                if (EtiketMi(c)) return null;
                if (Kalip(c.Metin)) continue;
                return c;
            }
            return null;
        }

        /// <summary>Etiketin aynı satırdaki en yakın komşusu da etiketse satır bir sütun başlığı satırıdır.</summary>
        private static bool BaslikSatiriMi(Yazi lab, Bolge b)
        {
            var en = b.Bant(lab.Yc - 1.6 * lab.H, lab.Yc + 1.6 * lab.H)
                .Where(c => !ReferenceEquals(c, lab) && AyniSatir(lab, c) && !BosMu(c.Metin) && BoyUygun(lab, c)
                            && (lab.Grup == 0 || c.Grup == lab.Grup))
                .Select(c => (c, Bosluk: c.X0 >= lab.X1 ? c.X0 - lab.X1 : c.X1 <= lab.X0 ? lab.X0 - c.X1 : 0.0))
                .Where(x => x.Bosluk < 35 * lab.H)
                .OrderBy(x => x.Bosluk).Select(x => x.c).FirstOrDefault();
            return en != null && EtiketMi(en);
        }

        /// <summary>Dik yan etiketin sağındaki, etiket boyu hizasındaki ilk değer.</summary>
        private static Yazi DikeyDeger(Yazi lab, Bolge b)
        {
            double h = lab.H, y0 = lab.Yc - lab.Boy / 2 - 3 * h, y1 = lab.Yc + lab.Boy / 2 + 3 * h;
            var adaylar = b.Bant(y0, y1)
                .Where(c => c.X0 > lab.X0 && c.X0 - lab.X1 < 25 * h && !BosMu(c.Metin) && c.H >= 0.4 * h && c.H <= 2 * h && !Kalip(c.Metin))
                .ToList();
            if (lab.Grup != 0 && adaylar.Any(c => c.Grup == lab.Grup)) adaylar = adaylar.Where(c => c.Grup == lab.Grup).ToList();
            return adaylar.Where(c => !EtiketMi(c) && !UnvanMi(c.Metin) && !Regex.IsMatch(c.Metin.ToUpper(Tr), @"^\s*(BÜRO|ODA|S[İI]C[İI]L|TEL|ADRES|BELGE)"))
                          .OrderBy(c => c.X0 - lab.X1 + 2 * Math.Abs(c.Yc - lab.Yc)).FirstOrDefault();
        }

        private static bool UnvanMi(string m) =>
            Regex.IsMatch(Anahtar(m), @"^(Y?MIMAR|YUKSEKMIMAR|MUHENDIS|INSMUH|INSAATMUHENDISI|MIMARMUHENDIS|SEHIRPLANCISI|ICMIMAR)$");

        /// <summary>Kişi/kurum adı: ad-soyad etiketi, oda sicil eki ve büyük/küçük karışıklığı temizlenir.</summary>
        private static string KisiSade(string v)
        {
            v = Temizle(v);
            v = Regex.Replace(v, @"\s*\b(ODA\s+)?S[İI]C[İI]L\b.*$", "", RegexOptions.IgnoreCase);
            return v.Trim(' ', ',', '-');
        }

        private static string Temizle(string v)
        {
            v = (v ?? "").Trim().TrimStart(':', ';').Trim();
            v = Regex.Replace(v, @"^(AD[Iİ]\s*[-/]?\s*SOYAD[Iİ]|ÜNVANI)\s*:\s*", "", RegexOptions.IgnoreCase);
            return v.Trim();
        }

        // ------------------------------------------------------------------ kapak okuma
        /// <summary>Bir kapak bölgesindeki etiket/değer çiftlerini okur. Etiketler kapak çapasına yakınlık sırasıyla denenir.</summary>
        private void KapakOku(Bolge b, Yazi capa, Dictionary<string, string> kapak)
        {
            var ham = new Dictionary<string, string>();
            var etiketler = b.Liste.Concat(b.Dikey).Select(y => (y, Alan: AlanOf(y))).Where(x => x.Alan != null)
                .OrderBy(x => capa == null ? 0 : Math.Abs(x.y.Cx - capa.Cx) + Math.Abs(x.y.Yc - capa.Yc)).ToList();

            foreach (var (lab, alan) in etiketler)
            {
                if (ham.ContainsKey(alan)) continue;
                bool kisi = alan == Sahip || alan == "MÜELLİF";
                if (lab.Egik)
                {
                    // Kutunun yanına dik yazılmış etiket (PROJE SAHİBİ): değer kutunun içinde, sağda
                    if (kisi) { var d = DikeyDeger(lab, b); if (d != null) ham[alan] = KisiSade(d.Metin); }
                    continue;
                }
                if (alan == "MÜELLİF")
                {
                    var alt = SutunYazisi(lab, b, -1, 8, true);
                    if (alt != null) ham[alan] = KisiSade(alt.Metin);
                    continue;
                }
                var sag = SagYazi(lab, b);
                bool sagEtiket = sag != null && (EtiketMi(sag) && !(kisi && AltEtiketler.Contains(Anahtar(sag.Metin))));
                if (sag != null && !sagEtiket && !Kalip(sag.Metin)) { ham[alan] = kisi ? KisiSade(sag.Metin) : Temizle(sag.Metin); continue; }

                Yazi bulunan = null;
                if (kisi)
                {
                    // Sahip kutusu: önce altta, yoksa üstte (kutunun alt kenarına yazılmış etiket)
                    bulunan = SutunYazisi(lab, b, -1, 10, true) ?? SutunYazisi(lab, b, +1, 12, true);
                    if (bulunan != null)
                    {
                        var deger = bulunan.Metin.Trim();
                        // "… LTD. ŞTİ. VE" + alt satırda "HİSSEDARLARI"
                        if (Regex.IsMatch(deger, @"(\bVE|&|,)$", RegexOptions.IgnoreCase))
                        {
                            var devam = SutunYazisi(bulunan, b, -1, 2.5, true);
                            if (devam != null) deger += " " + devam.Metin.Trim();
                        }
                        ham[alan] = KisiSade(deger);
                        continue;
                    }
                }
                else if (BaslikSatiriMi(lab, b))
                {
                    // Sütun başlığı düzeni (MİMARİ SINIFI | CİNSİ | KAT ADEDİ … altında değerler)
                    bulunan = SutunYazisi(lab, b, -1, 3.5, false);
                }
                if (bulunan != null) ham[alan] = Temizle(bulunan.Metin);
            }

            // Tek yazıda "ETİKET: değer"
            foreach (var y in b.Liste)
            {
                int i = y.Metin.IndexOf(':');
                if (i <= 0 || i >= y.Metin.Length - 1) continue;
                var alan = EtiketAlani.GetValueOrDefault(Anahtar(y.Metin[..i]));
                var deger = y.Metin[(i + 1)..].Trim();
                if (alan == null || ham.ContainsKey(alan) || BosMu(deger) || deger.Length > 60 || Kalip(y.Metin)) continue;
                ham[alan] = deger;
            }

            Yerlestir(ham, kapak);
            CumleyiOku(b.Liste.Where(y => y.Metin.Length < 160), kapak);
        }

        /// <summary>Ham etiket değerlerini rapor alanlarına çevirir (birleşik alanları böler).</summary>
        private void Yerlestir(Dictionary<string, string> ham, Dictionary<string, string> kapak)
        {
            void Koy(string k, string v)
            {
                v = (v ?? "").Trim().Trim(',', ';').Trim();
                if (!BosMu(v) && !kapak.ContainsKey(k)) kapak[k] = v;
            }
            foreach (var k in new[] { "İL", "İLÇE", "CİNSİ", "KAT ADEDİ", "KULLANIM AMACI", "BLOK", Sahip, "MÜELLİF", "YÜZÖLÇÜMÜ" })
                if (ham.TryGetValue(k, out var v)) Koy(k, k is "İL" or "İLÇE" ? v.ToUpper(Tr) : v);
            if (ham.TryGetValue("MAHALLE", out var mh)) Koy("MAHALLE", MahalleSade(mh));

            if (ham.TryGetValue(MahIlce, out var mi)) { var p = Bol(mi); Koy("MAHALLE", MahalleSade(p[0])); if (p.Length > 1) Koy("İLÇE", p[^1].ToUpper(Tr)); }
            if (ham.TryGetValue(IlceMah, out var im)) { var p = Bol(im); if (p.Length > 1) { Koy("İLÇE", p[0].ToUpper(Tr)); Koy("MAHALLE", MahalleSade(p[^1])); } else Koy("MAHALLE", MahalleSade(p[0])); }
            if (ham.TryGetValue(Yeri, out var yer)) YerCoz(yer, ham.GetValueOrDefault("İL"), Koy);

            if (ham.TryGetValue(Pap, out var pap))
            {
                var (pf, ada, par) = PapCoz(pap);
                Koy("PAFTA", pf); Koy("ADA", ada); Koy("PARSEL", par);
            }
            foreach (var k in new[] { "PAFTA", "ADA", "PARSEL" })
                if (ham.TryGetValue(k, out var v)) Koy(k, v.Trim('.', ' ', '-'));
            if (kapak.ContainsKey("BLOK")) kapak["BLOK"] = BlokAdiSade(kapak["BLOK"]);
        }

        private static string[] Bol(string v) =>
            v.Split(new[] { '/', '\\', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim(' ', '-')).Where(x => x.Length > 0).DefaultIfEmpty("").ToArray();

        public static string MahalleSade(string m)
        {
            m = Regex.Replace((m ?? "").Trim(), @"\s*\b(MAHALLES[İI]|MAHALLE|MAH\.?|MH\.?)\s*$", "", RegexOptions.IgnoreCase);
            return m.Trim().ToUpper(Tr);
        }

        /// <summary>"YERİ" değeri: SELÇUKLU/YAZIR, ARDIÇLI MAH., FEVZİÇAKMAK → ilçe ve/veya mahalle.</summary>
        private void YerCoz(string yer, string il, Action<string, string> koy)
        {
            var parca = Bol(yer);
            var ilceler = new HashSet<string>(BolgeDeposu.Ilceler(il ?? Il ?? "KONYA").Concat(BolgeDeposu.KonyaIlceleri));
            string ilce = parca.Select(p => p.ToUpper(Tr)).FirstOrDefault(ilceler.Contains);
            var kalan = parca.Where(p => p.ToUpper(Tr) != ilce && !BolgeDeposu.Iller.Contains(p.ToUpper(Tr))).ToList();
            if (ilce != null) koy("İLÇE", ilce);
            var mah = kalan.FirstOrDefault(p => Regex.IsMatch(p, @"\bMAH", RegexOptions.IgnoreCase)) ?? kalan.FirstOrDefault();
            if (mah != null) koy("MAHALLE", MahalleSade(mah));
        }

        /// <summary>Pafta/ada/parsel: sondan iki sayı parsel ve ada, kalan pafta (M28b05b2a-43704- 1, -/ 15644 / 7, 26870/5).</summary>
        public static (string Pafta, string Ada, string Parsel) PapCoz(string v)
        {
            var s = Regex.Replace(v ?? "", @"\.{2,}|…", " ");
            var parca = Regex.Split(s, @"[/\\\-,;]").Select(p => p.Trim(' ', '.', ':')).Where(p => p.Length > 0).ToList();
            if (parca.Count == 1) parca = Regex.Split(parca[0], @"\s+").Where(p => p.Length > 0).ToList();
            bool Sayi(string p) => Regex.IsMatch(p, @"^\d+[A-Za-z]?$");
            string par = null, ada = null;
            if (parca.Count > 0 && Sayi(parca[^1])) { par = parca[^1]; parca.RemoveAt(parca.Count - 1); }
            if (parca.Count > 0 && Sayi(parca[^1])) { ada = parca[^1]; parca.RemoveAt(parca.Count - 1); }
            var pafta = string.Join("-", parca).Trim();
            if (par != null && ada == null && pafta.Length > 0 && !Regex.IsMatch(pafta, @"\p{L}")) { ada = pafta; pafta = ""; }
            return (pafta, ada ?? "", par ?? "");
        }

        /// <summary>Kapak tablosunda bulunmayan bilgiler için başlık cümlesi: "KONYA İLİ MERAM İLÇESİ MELİKŞAH MAHALLESİ 2645 ADA 103 PARSEL".</summary>
        private static void CumleyiOku(IEnumerable<Yazi> yazilar, Dictionary<string, string> kapak)
        {
            foreach (var y in yazilar)
            {
                var u = y.Metin.ToUpper(Tr);
                var ada = Regex.Match(u, @"(\d+)\s*ADA\b|\bADA\s*(?:NO)?\s*:?\s*(\d+)");
                var par = Regex.Match(u, @"(\d+)\s*PARSEL\b|\bPARSEL\s*(?:NO)?\s*:?\s*(\d+)");
                if (!ada.Success || !par.Success) continue;
                void Koy(string k, string v) { if (!string.IsNullOrWhiteSpace(v) && !kapak.ContainsKey(k)) kapak[k] = v.Trim(); }
                Koy("ADA", ada.Groups[1].Success ? ada.Groups[1].Value : ada.Groups[2].Value);
                Koy("PARSEL", par.Groups[1].Success ? par.Groups[1].Value : par.Groups[2].Value);
                var il = Regex.Match(u, @"([\p{Lu}]+)\s+İL[İI]\b"); if (il.Success) Koy("İL", il.Groups[1].Value);
                var ilce = Regex.Match(u, @"([\p{Lu}]+)\s+İLÇES[İI]\b"); if (ilce.Success) Koy("İLÇE", ilce.Groups[1].Value);
                var mah = Regex.Match(u, @"([\p{Lu}]+)\s+(?:MAHALLES[İI]|MAH\.)"); if (mah.Success) Koy("MAHALLE", mah.Groups[1].Value);
            }
        }

        private static readonly Regex BlokRx = new(@"(?:^|\(|\s)([A-ZÇĞİÖŞÜ0-9]{1,3})\s*-?\s*BLO[KĞ]U?\s*(?:\)|$)");

        public static string BlokAdiSade(string b)
        {
            var u = (b ?? "").Trim().ToUpper(Tr);
            var m = BlokRx.Match(u);
            return m.Success ? m.Groups[1].Value : u;
        }

        /// <summary>Kapakta "BLOK ADI" yoksa çapa çevresindeki "A BLOK" / "MİMARİ PROJE (A BLOK)" yazısı.</summary>
        private static string BlokAdiBul(Bolge b, Yazi capa, double r)
        {
            if (capa == null) return null;
            return b.Liste
                .Where(y => y.Metin.Length <= 40 && y.H >= 0.8 * capa.H && Math.Abs(y.Cx - capa.Cx) <= Math.Min(r, 60 * capa.H)
                            && y.Yc - capa.Yc <= Math.Min(r, 40 * capa.H) && capa.Yc - y.Yc <= Math.Min(r, 10 * capa.H))
                .Select(y => (y, M: BlokRx.Match(y.Metin.ToUpper(Tr))))
                .Where(x => x.M.Success)
                .OrderBy(x => Math.Abs(x.y.Cx - capa.Cx) + Math.Abs(x.y.Yc - capa.Yc))
                .Select(x => x.M.Groups[1].Value).FirstOrDefault();
        }

        // ------------------------------------------------------------------ kapakları ayırma
        /// <summary>Çizimdeki kapakları bulur: her PAFTA/ADA/PARSEL (yoksa MAHALLE, yoksa BLOK ADI) etiketi bir kapak çapasıdır.</summary>
        private void BloklariBul(List<Yazi> yazilar)
        {
            var tumu = new Bolge(yazilar);
            List<Yazi> Capalar(params string[] alanlar) => tumu.Liste.Where(y => alanlar.Contains(AlanOf(y))).ToList();
            var capalar = Capalar(Pap);
            if (capalar.Count == 0) capalar = Capalar("MAHALLE", MahIlce, IlceMah);
            if (capalar.Count == 0) capalar = Capalar("BLOK");

            var hamlar = new List<(Blok B, Yazi Capa)>();
            if (capalar.Count <= 1)
            {
                var capa = capalar.FirstOrDefault();
                var b = new Blok { Etiket = capa };
                KapakOku(tumu, capa, b.Kapak);
                if (!b.Kapak.ContainsKey("BLOK")) { var ad = BlokAdiBul(tumu, capa, double.MaxValue); if (ad != null) b.Kapak["BLOK"] = ad; }
                b.Ad = b.Kapak.GetValueOrDefault("BLOK", "");
                b.Planlar.AddRange(PlanBasliklari);
                Bloklar.Add(b);
                return;
            }

            // Kapak bölgesi: en yakın iki çapa arasındaki mesafenin %45'i
            double minD = double.MaxValue;
            for (int i = 0; i < capalar.Count; i++)
                for (int j = i + 1; j < capalar.Count; j++)
                    minD = Math.Min(minD, Math.Max(Math.Abs(capalar[i].Cx - capalar[j].Cx), Math.Abs(capalar[i].Yc - capalar[j].Yc)));
            double r = Math.Max(minD * 0.45, 1e-6);
            foreach (var c in capalar)
            {
                var bolge = new Bolge(tumu.Liste.Where(y => Math.Abs(y.Cx - c.Cx) <= r && Math.Abs(y.Yc - c.Yc) <= r));
                var b = new Blok { Etiket = c };
                KapakOku(bolge, c, b.Kapak);
                if (!b.Kapak.ContainsKey("BLOK")) { var ad = BlokAdiBul(bolge, c, r); if (ad != null) b.Kapak["BLOK"] = ad; }
                b.Ad = b.Kapak.GetValueOrDefault("BLOK", "");
                hamlar.Add((b, c));
            }
            // Plan başlıkları en yakın kapağa (önce düşey, sonra yatay uzaklık)
            foreach (var p in PlanBasliklari)
                hamlar.OrderBy(h => Math.Abs(h.Capa.Y - p.Y) + 0.1 * Math.Abs(h.Capa.X - p.X)).First().B.Planlar.Add(p);

            // Her paftada tekrarlanan aynı kapaklar birleştirilir (aynı içerik + aynı blok adı)
            string Imza(Blok b) => string.Join("|", b.Kapak.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + k.Value));
            foreach (var grup in hamlar.Select(h => h.B).GroupBy(Imza))
            {
                var ilk = grup.First();
                foreach (var diger in grup.Skip(1)) ilk.Planlar.AddRange(diger.Planlar);
                Bloklar.Add(ilk);
            }
            // Blok adı olmayan kapaklar (TRAFO, GÜVENLİK …) kullanım amacıyla, o da yoksa sırayla adlandırılır
            for (int i = 0; i < Bloklar.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(Bloklar[i].Ad) || Bloklar.Count == 1) continue;
                var amac = Bloklar[i].Kapak.GetValueOrDefault("KULLANIM AMACI", "").ToUpper(Tr);
                bool tek = amac.Length > 3 && Bloklar.Count(x => x.Kapak.GetValueOrDefault("KULLANIM AMACI", "").ToUpper(Tr) == amac) == 1;
                Bloklar[i].Ad = tek ? amac : $"Kapak {i + 1}";
            }
            // Çatı planı olan bloklar öne
            var sirali = Bloklar.OrderBy(b => b.Planlar.Any(p => Norm(p.Metin).Contains("ÇATI")) ? 0 : 1).ThenBy(b => b.Ad, StringComparer.Create(Tr, true)).ToList();
            Bloklar.Clear(); Bloklar.AddRange(sirali);
        }
    }
}
