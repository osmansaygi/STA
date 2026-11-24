using System.Globalization;
using System.Text.RegularExpressions;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;

namespace GesRaporu
{
    public readonly record struct Cizgi(double X1, double Y1, double X2, double Y2, bool Kalin);

    /// <summary>Çizim yazısı. X/Y ekleme noktası; X0–X1 tahmini yatay kapsam, Yc ilk satırın düşey ortası, Grup blok örneği (0 = model).</summary>
    public sealed record Yazi(string Metin, double X, double Y, double H, string Katman)
    {
        public int Grup { get; init; }
        public double X0 { get; init; }
        public double X1 { get; init; }
        public double Yc { get; init; }
        public bool Egik { get; init; }
        public double Aci { get; init; }
        public double Boy { get; init; }
        /// <summary>Öznitelik yazısıysa sahibi blok örneğinin sınırları (x0, y0, x1, y1); pafta başlığı blokları için.</summary>
        public (double X0, double Y0, double X1, double Y1)? Kutu { get; init; }
        public double Cx => (X0 + X1) / 2;
    }

    /// <summary>Mimari DWG'den rapor bilgilerini çıkarır (AutoCAD gerekmez, dosya açıkken de okunur).</summary>
    public sealed partial class DwgBilgi
    {
        private static readonly CultureInfo Tr = new("tr-TR");
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public List<string> Notlar { get; } = new();
        public List<Yazi> Yazilar { get; } = new();
        public List<Yazi> PlanBasliklari { get; } = new();
        public Dictionary<string, string> Kapak { get; } = new();
        public string Il { get; private set; }
        public string Ilce { get; private set; }
        public double? SifirKotu { get; private set; }
        public double? EnUstKot { get; private set; }
        public double? MahyaKotu { get; private set; }
        public bool GesNotuVar { get; private set; }

        private CadDocument _doc;
        private readonly List<(double Rel, double X, double Y)> _bagilKotlar = new();
        private readonly List<double> _kesitKotlari = new(); // KESİT bloklarındaki kotlar (son çare)

        public static DwgBilgi Oku(string yol)
        {
            using var fs = new FileStream(yol, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var ms = new MemoryStream();
            fs.CopyTo(ms);
            ms.Position = 0;
            var b = new DwgBilgi { _doc = DwgReader.Read(ms, (s, e) => { }) };
            b.Coz();
            return b;
        }

        // ------------------------------------------------------------------ yazılar
        public static string Duz(string m)
        {
            if (string.IsNullOrEmpty(m)) return "";
            var s = m.Replace("\\P", " ").Replace("\\~", " ");
            s = Regex.Replace(s, @"\\S([^;^/#]*)[\^/#]([^;]*);", "$1/$2");
            s = Regex.Replace(s, @"\\[fFHhCcTQWApq][^;]*;", "");
            s = Regex.Replace(s, @"\\[LlOoKk]", "");
            s = s.Replace("{", "").Replace("}", "").Replace("%%c", "Ø").Replace("%%d", "°").Replace("%%p", "±");
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        private static double Olcek(Transform t)
        {
            var a = t.ApplyTransform(new XYZ(0, 0, 0));
            var b = t.ApplyTransform(new XYZ(0, 1, 0));
            return Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        }

        private int _grupSayac;
        private readonly HashSet<int> _kesitGruplari = new();

        private static double Genislik(string s, double h) => Math.Max(1, s.Length) * h * 0.72;

        /// <summary>Yazının dünya eksenindeki açısı (−π, π]; blok dönüklüğü dahil.</summary>
        private static double DunyaAcisi(double aci, Transform t)
        {
            var a = t.ApplyTransform(new XYZ(0, 0, 0)); var b = t.ApplyTransform(new XYZ(1, 0, 0));
            return Math.IEEERemainder(aci + Math.Atan2(b.Y - a.Y, b.X - a.X), 2 * Math.PI);
        }

        private static bool DikeyAci(double aci) => Math.Abs(aci - Math.PI / 2) < 0.05;

        /// <summary>MText açısı yön vektöründen.</summary>
        private static double MetinAcisi(MText m)
        {
            var d = m.AlignmentPoint;
            return Math.Abs(d.X) + Math.Abs(d.Y) > 1e-9 ? Math.Atan2(d.Y, d.X) : m.Rotation;
        }

        /// <summary>
        /// Açılı yazı. Yalnız 90° (aşağıdan yukarı okunan) yazılar kapakta kullanılır (sahip kutusu yan etiketi):
        /// X0–X1 yazı kalınlığı, Yc boyun ortası, Boy uzunluğu. hiza: 0 baş, 1 orta, 2 son (yazı yönünde).
        /// </summary>
        private void AciliEkle(string s, XYZ p, XYZ ekleme, double h, double w, int hiza, double aci, int grup, string katman)
        {
            double y0 = hiza == 1 ? p.Y - w / 2 : hiza == 2 ? p.Y - w : p.Y;
            Yazilar.Add(new Yazi(s, ekleme.X, ekleme.Y, h, katman ?? "")
            { Grup = grup, X0 = p.X - h, X1 = p.X, Yc = y0 + w / 2, Boy = w, Aci = aci, Egik = true });
        }

        private void MetinEkle(MText m, Transform t, int grup)
        {
            var p = t.ApplyTransform(m.InsertPoint);
            double s = Olcek(t), h = m.Height * s;
            var satirlar = (m.Value ?? "").Split(new[] { @"\P", "\n" }, StringSplitOptions.None).Select(Duz).Where(x => x.Length > 0).ToList();
            int n = Math.Max(1, satirlar.Count);
            double w = Genislik(satirlar.OrderByDescending(x => x.Length).FirstOrDefault() ?? "", h);
            if (m.RectangleWidth > 0) w = Math.Min(w, m.RectangleWidth * s);
            int ap = Math.Clamp((int)m.AttachmentPoint, 1, 9); // üst/orta/alt × sol/orta/sağ
            int kolon = (ap - 1) % 3, sira = (ap - 1) / 3;
            double aci = DunyaAcisi(MetinAcisi(m), t);
            if (Math.Abs(aci) > 0.02) { AciliEkle(Duz(m.Value), p, p, h, w, kolon, aci, grup, m.Layer?.Name); return; }
            double x0 = kolon == 1 ? p.X - w / 2 : kolon == 2 ? p.X - w : p.X;
            double toplam = (n - 1) * 1.667 * h;
            double yc = sira == 0 ? p.Y - h / 2 : sira == 1 ? p.Y + toplam / 2 : p.Y + h / 2 + toplam;
            Yazilar.Add(new Yazi(Duz(m.Value), p.X, p.Y, h, m.Layer?.Name ?? "")
            { Grup = grup, X0 = x0, X1 = x0 + w, Yc = yc });
        }

        private void TextEkle(TextEntity te, Transform t, int grup, string katman, (double, double, double, double)? kutu = null)
        {
            string s = Duz(te.Value);
            double h = te.Height * Olcek(t);
            var ha = te.HorizontalAlignment; var va = te.VerticalAlignment;
            bool hizali = ha != TextHorizontalAlignment.Left || va != TextVerticalAlignmentType.Baseline;
            var ip = t.ApplyTransform(te.InsertPoint);
            var p = hizali ? t.ApplyTransform(te.AlignmentPoint) : ip;
            double w = Genislik(s, h) * (te.WidthFactor > 0 ? te.WidthFactor : 1);
            double aci = DunyaAcisi(te.Rotation, t);
            if (Math.Abs(aci) > 0.02)
            {
                int hz = ha is TextHorizontalAlignment.Center or TextHorizontalAlignment.Middle ? 1 : ha == TextHorizontalAlignment.Right ? 2 : 0;
                AciliEkle(s, p, ip, h, w, hz, aci, grup, katman);
                return;
            }
            double x0, x1;
            if (ha is TextHorizontalAlignment.Aligned or TextHorizontalAlignment.Fit) { x0 = Math.Min(ip.X, p.X); x1 = Math.Max(ip.X, p.X); }
            else
            {
                x0 = ha is TextHorizontalAlignment.Center or TextHorizontalAlignment.Middle ? p.X - w / 2 : ha == TextHorizontalAlignment.Right ? p.X - w : p.X;
                x1 = x0 + w;
            }
            double yc = ha == TextHorizontalAlignment.Middle || va == TextVerticalAlignmentType.Middle ? p.Y
                      : va == TextVerticalAlignmentType.Top ? p.Y - h / 2 : p.Y + h / 2;
            Yazilar.Add(new Yazi(s, ip.X, ip.Y, h, katman ?? "") { Grup = grup, X0 = x0, X1 = x1, Yc = yc, Kutu = kutu });
        }

        private static (double, double, double, double)? BlokKutusu(Insert i, Transform t)
        {
            try
            {
                var bb = i.GetBoundingBox();
                var k = new[] { new XYZ(bb.Min.X, bb.Min.Y, 0), new XYZ(bb.Max.X, bb.Min.Y, 0), new XYZ(bb.Min.X, bb.Max.Y, 0), new XYZ(bb.Max.X, bb.Max.Y, 0) }
                    .Select(p => t.ApplyTransform(p)).ToList();
                return (k.Min(p => p.X), k.Min(p => p.Y), k.Max(p => p.X), k.Max(p => p.Y));
            }
            catch { return null; }
        }

        private void YaziTopla(Entity e, Transform t, int derinlik, int grup)
        {
            switch (e)
            {
                case MText m:
                    MetinEkle(m, t, grup);
                    break;
                case AttributeDefinition:
                    break; // blok tanımındaki öznitelik şablonu; asıl değer Insert.Attributes'ta
                case TextEntity te:
                    TextEkle(te, t, grup, e.Layer?.Name);
                    break;
                case Insert i when i.Block != null && derinlik < 6:
                    {
                        AttributeKotlari(i);
                        int g = ++_grupSayac;
                        var bad = (i.Block.Name ?? "").ToUpper(Tr);
                        if (bad.Contains("KESİT") || bad.Contains("KESIT") || _kesitGruplari.Contains(grup)) _kesitGruplari.Add(g); // kesit kotları başka referansta olabilir
                        // Kapak değerleri çoğu zaman öznitelikte (ör. PROJE KAPAK bloğu): görünür öznitelikler de yazıdır.
                        // Öznitelikte plan başlığı taşıyan pafta bloklarının sınırı çatı planı penceresi için saklanır.
                        (double, double, double, double)? kutu = i.Attributes.Any(a => (a.Value ?? "").ToUpper(Tr).Contains("PLAN")) ? BlokKutusu(i, t) : null;
                        foreach (var a in i.Attributes)
                            if (!a.Flags.HasFlag(AttributeFlags.Hidden) && !string.IsNullOrWhiteSpace(a.Value))
                                TextEkle(a, t, g, a.Layer?.Name, kutu);
                        var tt = new Transform(t.Matrix * i.GetTransform().Matrix);
                        foreach (var c in i.Block.Entities) YaziTopla(c, tt, derinlik + 1, g);
                        break;
                    }
            }
        }

        private static readonly Regex SayiRx = new(@"^\s*([+\-±]?)\s*(\d+[.,]\d+)");

        private static double? Sayi(string s, out bool isaretli)
        {
            isaretli = false;
            var m = SayiRx.Match(s ?? "");
            if (!m.Success) return null;
            isaretli = m.Groups[1].Value.Length > 0;
            double v = double.Parse(m.Groups[2].Value.Replace(',', '.'), Inv);
            return m.Groups[1].Value == "-" ? -v : v;
        }

        private readonly List<double> _sifirAdaylari = new();

        private void AttributeKotlari(Insert i)
        {
            if (i.Attributes.Count == 0) return;
            string ad = (i.Block?.Name ?? "").ToUpper(Tr);
            bool kesit = ad.Contains("KESİT") || ad.Contains("KESIT");
            double? rel = null, abs = null;
            bool mahya = false;
            foreach (var a in i.Attributes)
            {
                var v = a.Value ?? "";
                if (v.ToUpper(Tr).Contains("MAHYA")) mahya = true;
                var s = Sayi(v, out bool isaretli);
                if (s == null) continue;
                if (Math.Abs(s.Value) >= 100 && !isaretli) abs ??= s;
                else if (Math.Abs(s.Value) < 100 && (isaretli || s == 0)) rel ??= s;
            }
            if (rel != null && !kesit)
            {
                _bagilKotlar.Add((rel.Value, i.InsertPoint.X, i.InsertPoint.Y));
                if (mahya) MahyaKotu = Math.Max(MahyaKotu ?? double.MinValue, rel.Value);
            }
            if (rel != null && abs != null) _sifirAdaylari.Add(abs.Value - rel.Value);
        }

        // Düz yazı kotları: "+14.00", "±0.00", "-3.20", "KOT: +12,50", "MAHYA KOTU +14.20" (kot bloğu kullanmayan çizimler)
        private static readonly Regex MetinKotRx = new(@"^\s*(?:KOT\s*[:=]?\s*)?([+±\-])\s*(\d{1,2}[.,]\d{2})\s*(?:M)?\s*$", RegexOptions.IgnoreCase);
        private static readonly Regex AdliKotRx = new(@"\b(MAHYA|SAÇAK|SACAK|PARAPET|ÜST|UST|SU BASMAN|SUBASMAN)\b[^0-9+\-±]{0,20}([+±\-]?)\s*(\d{1,2}[.,]\d{2})", RegexOptions.IgnoreCase);

        private void MetinKotlariniTopla()
        {
            foreach (var y in Yazilar)
            {
                if (y.Metin.Length > 40) continue;
                var m = MetinKotRx.Match(y.Metin);
                bool mahya = false;
                if (!m.Success)
                {
                    m = AdliKotRx.Match(y.Metin);
                    if (!m.Success) continue;
                    mahya = m.Groups[1].Value.ToUpper(Tr).StartsWith("MAHYA");
                }
                int g = m.Groups.Count - 2;
                double v = double.Parse(m.Groups[g + 1].Value.Replace(',', '.'), Inv);
                if (m.Groups[g].Value == "-") v = -v;
                if (_kesitGruplari.Contains(y.Grup)) { _kesitKotlari.Add(v); continue; }
                _bagilKotlar.Add((v, y.X, y.Yc));
                if (mahya) MahyaKotu = Math.Max(MahyaKotu ?? double.MinValue, v);
            }
        }

        /// <summary>KAT ADEDİ'nden zemin üstü kat sayısı: "B+Z+9" → 10, "2B+3" → 3, "B + Z + ASMA KAT +1" → 2,5.</summary>
        public static double? ZeminUstuKat(string katAdedi)
        {
            if (string.IsNullOrWhiteSpace(katAdedi)) return null;
            var u = Regex.Replace(katAdedi.ToUpper(new CultureInfo("tr-TR")), @"\bKAT(I|LI)?\b", " ");
            double n = 0; bool bulundu = false;
            foreach (var p in u.Split('+').Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                if (Regex.IsMatch(p, @"^\d*\s*(B|BODRUM|BK|Y\.?B\.?|YARI BODRUM)\.?$")) { bulundu = true; continue; }
                if (Regex.IsMatch(p, @"^(Z|ZEMİN|ZEMIN|ZK|GİRİŞ)\.?$")) { n += 1; bulundu = true; continue; }
                if (p.StartsWith("ASMA")) { n += 0.5; bulundu = true; continue; }
                if (Regex.IsMatch(p, @"^(Ç|ÇATI|ÇK|TERAS|ÇATI ARASI)\.?$")) { n += 0.5; bulundu = true; continue; }
                var s = Regex.Match(p, @"^(\d{1,2})\s*(N|NK|NORMAL)?\.?$");
                if (s.Success) { n += int.Parse(s.Groups[1].Value); bulundu = true; }
            }
            return bulundu && n > 0 ? n : null;
        }

        /// <summary>
        /// Rüzgâr hesabı için bina yüksekliği: çatı paftasındaki en büyük kot → çizimdeki en büyük kot → kat adedinden tahmin.
        /// Kot, kat adedine göre tahminin (zemin üstü kat × 3,0 m + 1,0 m) %75’inden az ise kabul edilmez (ör. paftada yalnız bodrum kotları).
        /// </summary>
        public double? BinaYuksekligi(PlanCizimi plan, out string kaynak)
        {
            var kat = ZeminUstuKat(Kapak.GetValueOrDefault("KAT ADEDİ"));
            double? tahmin = kat != null ? Math.Round(kat.Value * 3.0 + 1.0, 2) : null;
            bool Uygun(double? h) => h != null && h > 2 && (tahmin == null || (h >= 0.75 * tahmin && h <= 2.0 * tahmin));
            var band = BandYuksekligi(plan);
            if (Uygun(band)) { kaynak = "çatı paftasındaki en büyük kot"; return band; }
            if (Uygun(EnUstKot) && (Bloklar.Count <= 1 || tahmin != null)) { kaynak = "çizimdeki en büyük kot"; return EnUstKot; }
            double? kesitUst = _kesitKotlari.Count > 0 ? UstKot(_kesitKotlari) : null;
            if (Uygun(kesitUst) && (Bloklar.Count <= 1 || tahmin != null)) { kaynak = "kesit kotları"; return kesitUst; }
            if (tahmin != null) { kaynak = $"kat adedinden tahmin ({Kapak["KAT ADEDİ"]}: {kat:0.#} kat × 3,0 m + 1,0 m)"; return tahmin; }
            kaynak = band != null ? "çatı paftasındaki en büyük kot" : EnUstKot != null ? "çizimdeki en büyük kot" : null;
            return band ?? EnUstKot;
        }

        // ------------------------------------------------------------------ çözümleme
        private void Coz()
        {
            foreach (var e in _doc.Entities) YaziTopla(e, new Transform(Matrix4.Identity), 0, 0);

            // ±0.00 mutlak kotu: (mutlak − bağıl) çiftlerinin ortancası
            if (_sifirAdaylari.Count > 0)
            {
                var s = _sifirAdaylari.OrderBy(x => x).ToList();
                SifirKotu = s[s.Count / 2];
            }
            MetinKotlariniTopla();
            if (_bagilKotlar.Count > 0) EnUstKot = UstKot(_bagilKotlar.Select(k => k.Rel));

            var buyuk = Yazilar.Select(y => (y, U: y.Metin.ToUpper(Tr))).ToList();
            GesNotuVar = buyuk.Any(x => x.U.Contains("GES ") && x.U.Contains("ÇATI"));

            // Plan başlıkları
            foreach (var (y, u) in buyuk)
                if (u.Contains("PLAN") && u.Length < 70 && (u.Contains("KAT") || u.Contains("ÇATI")) && !u.Contains("NOT"))
                    PlanBasliklari.Add(y);

            // İl: en çok geçen il adı
            var sayac = new Dictionary<string, int>();
            foreach (var (_, u) in buyuk)
                foreach (var il in BolgeDeposu.Iller)
                    if (Regex.IsMatch(u, $@"(^|[^A-ZÇĞİÖŞÜ]){Regex.Escape(il)}([^A-ZÇĞİÖŞÜ]|$)"))
                        sayac[il] = sayac.GetValueOrDefault(il) + 1;
            _siklikIli = sayac.OrderByDescending(k => k.Value).Select(k => k.Key).FirstOrDefault();

            var tumYazilar = buyuk.Select(b => b.y).ToList();
            BloklariBul(tumYazilar);
            BlokSec(Bloklar[0]);
        }

        private string _siklikIli;

        // Kapakta ilçe yazmayan projeler için bilinen Konya mahalleleri (kullanıcı tablosu önceliklidir)
        private static readonly Dictionary<string, string> KonyaMahalleIlce = new()
        {
            ["HOCACİHAN"] = "SELÇUKLU", ["YAZIR"] = "SELÇUKLU", ["SİLLE"] = "SELÇUKLU", ["MELİKŞAH"] = "MERAM",
            ["LALEBAHÇE"] = "MERAM", ["HACI ŞABAN"] = "MERAM", ["AZİZİYE"] = "KARATAY", ["FEVZİÇAKMAK"] = "KARATAY",
            ["ARDIÇLI"] = "KARATAY",
        };

        /// <summary>İl: kapak → çizimde en çok geçen il. İlçe: kapak → mahalle tablosu → adres dışı yazılarda en çok geçen ilçe.</summary>
        private void IlIlceBelirle()
        {
            Il = Kapak.TryGetValue("İL", out var kil) && kil.Length > 1 ? BolgeDeposu.B(kil) : _siklikIli;
            Ilce = Kapak.TryGetValue("İLÇE", out var kilce) && kilce.Length > 1 ? BolgeDeposu.B(kilce) : null;
            if (Ilce != null || Il == null) return;
            if (Kapak.TryGetValue("MAHALLE", out var mh))
            {
                Ilce = BolgeDeposu.Ilce(Il, mh) ?? (Il == "KONYA" ? KonyaMahalleIlce.GetValueOrDefault(BolgeDeposu.B(mh)) : null);
                var not = $"İlçe, mahalle tablosundan bulundu ({mh} → {Ilce}).";
                if (Ilce != null) { if (!Notlar.Contains(not)) Notlar.Add(not); return; }
            }
            // Büro adresleri (… MAH. … NO … MERAM / KONYA) ilçe sayılmaz
            var sayac = new Dictionary<string, int>();
            foreach (var y in Yazilar)
            {
                var u = y.Metin.ToUpper(Tr);
                if (u.Length > 160 || Kalip(y.Metin) || Regex.IsMatch(u, @"\bNO\s*[:.]?\s*\d|\bSK\b|\bSOK\b|\bCAD\b|CADDES[İI]|SOKA[KĞ]|ADRES|E-POSTA|BÜRO|VERG[İI]")) continue;
                foreach (var ilce in BolgeDeposu.Ilceler(Il))
                    if (Regex.IsMatch(u, $@"(^|[^A-ZÇĞİÖŞÜ]){Regex.Escape(ilce)}([^A-ZÇĞİÖŞÜ]|$)"))
                        sayac[ilce] = sayac.GetValueOrDefault(ilce) + 1;
            }
            Ilce = sayac.OrderByDescending(k => k.Value).Select(k => k.Key).FirstOrDefault();
        }

        // ------------------------------------------------------------------ bloklar
        /// <summary>Çizimdeki her kapak bir bloktur (A BLOK, B BLOK, OTOPARK …).</summary>
        public sealed class Blok
        {
            public string Ad = "";
            public Yazi Etiket;
            public Dictionary<string, string> Kapak { get; } = new();
            public List<Yazi> Planlar { get; } = new();
            public string Gorunen => Etiketle(Ad);
            public override string ToString() => Gorunen;
        }

        /// <summary>"A" → "A BLOK"; uzun adlar (KAPALI OTOPARK) olduğu gibi.</summary>
        public static string Etiketle(string ad)
        {
            ad = (ad ?? "").Trim();
            if (ad.Length == 0) return "";
            return ad.ToUpper(Tr).Contains("BLOK") || ad.Length > 3 ? ad.ToUpper(Tr) : ad.ToUpper(Tr) + " BLOK";
        }

        public List<Blok> Bloklar { get; } = new();

        private static string Norm(string s) => Regex.Replace((s ?? "").ToUpper(Tr).Replace(":", ""), @"\s+", " ").Trim();

        /// <summary>Seçilen bloğun kapak bilgilerini genel alanlara uygular.</summary>
        public void BlokSec(Blok b)
        {
            Kapak.Clear();
            foreach (var (k, v) in b.Kapak) Kapak[k] = v;
            IlIlceBelirle();
        }

        /// <summary>Plan paftasının düşey bandındaki en büyük bağıl kot (bloğa özgü bina yüksekliği).</summary>
        public double? BandYuksekligi(PlanCizimi plan)
        {
            if (plan == null || plan.CerceveY1 <= plan.CerceveY0) return null;
            var k = _bagilKotlar.Where(x => x.Y >= plan.CerceveY0 && x.Y <= plan.CerceveY1).Select(x => x.Rel).ToList();
            if (Environment.GetEnvironmentVariable("GES_DEBUG") == "1")
                Notlar.Add($"[band {plan.CerceveY0:0}..{plan.CerceveY1:0}: {string.Join(",", k.Distinct().OrderByDescending(x => x).Take(12))} | tüm kot={_bagilKotlar.Count} enüst={EnUstKot} mahya={MahyaKotu} sıfır={SifirKotu}]");
            return k.Count > 0 ? UstKot(k) : null;
        }

        /// <summary>En büyük kot; komşusundan 6 m’den fazla kopuk tek tük üst değerler (yazım hatası, başka çizim) atılır.</summary>
        private static double UstKot(IEnumerable<double> kotlar)
        {
            var s = kotlar.Where(x => x < 100).Distinct().OrderByDescending(x => x).ToList();
            if (s.Count == 0) return kotlar.Max();
            int i = 0;
            while (i < s.Count - 1 && s[i] - s[i + 1] > 6) i++;
            return s[i];
        }

        // ------------------------------------------------------------------ çatı planı
        /// <summary>Önce gerçek "ÇATI PLANI", yoksa "ÇATI KATI PLANI" vb.</summary>
        public Yazi VarsayilanCatiBasligi() => VarsayilanCatiBasligi(PlanBasliklari);

        private static bool OlcekliBaslik(Yazi y) =>
            Regex.IsMatch(Regex.Replace(y.Metin.ToUpper(Tr), @"\s+", " "), @"Ö\s*[:.]|ÖLÇEK|\b1\s*/\s*\d{2,3}\b");

        public Yazi VarsayilanCatiBasligi(IList<Yazi> PlanBasliklari)
        {
            string U(Yazi y) => Regex.Replace(y.Metin.ToUpper(Tr), @"\s+", " ");
            // Ölçek yazan başlık gerçek pafta başlığıdır; ölçeksiz "ÇATI PLAN" çoğu zaman kesitteki seviye etiketidir
            bool Olcekli(Yazi y) => OlcekliBaslik(y);
            return PlanBasliklari.FirstOrDefault(y => U(y).Contains("ÇATI PLAN") && Olcekli(y))
                ?? PlanBasliklari.FirstOrDefault(y => U(y).Contains("ÇATI") && !U(y).Contains("KESİT") && Olcekli(y))
                ?? PlanBasliklari.FirstOrDefault(y => U(y).Contains("ÇATI PLAN"))
                ?? PlanBasliklari.FirstOrDefault(y => U(y).Contains("ÇATI") && !U(y).Contains("KESİT"))
                ?? PlanBasliklari.LastOrDefault();
        }

        // Yazı ve ölçü katmanları dışlanmaz: çizerler çatı çizgilerini bu katmanlara da çiziyor (ölçü nesneleri zaten çizilmez).
        private static readonly string[] HaricKatman = { "TARAMA", "TEFRİŞ", "TEFRIS", "MAHAL", "KOT", "PAFTA", "WIPEOUT", "TEXT" };
        private static readonly string[] HaricBlok = { "MAHAL", "KOT", "KUZEY", "PAFTA", "ANTET" };
        private static readonly string[] KalinKatman = { "DUVAR", "ÇATI", "CATI", "KOLON", "PARAPET", "020", "SIVA" };

        private static bool Harici(string katman)
        {
            var u = (katman ?? "").ToUpper(Tr);
            return HaricKatman.Any(u.Contains);
        }

        public sealed class PlanCizimi
        {
            public List<Cizgi> Cizgiler { get; } = new();
            public double MinX, MinY, MaxX, MaxY;
            public double Genislik => MaxX - MinX;
            public double Yukseklik => MaxY - MinY;
            public double BirimMetre = 0.01; // çizim birimi → m
            public double CerceveY0, CerceveY1; // pafta düşey bandı
            public int PanelAdedi;
        }

        /// <summary>Başlığın sağ/solundaki uzun düşey pafta çizgileri arasındaki çizimi çıkarır.</summary>
        public PlanCizimi PlanCiz(Yazi baslik, double panelA, double panelB)
        {
            if (baslik == null) return null;

            // Düşey uzun çizgiler (pafta sınırları)
            var duseyler = new List<(double X, double Y1, double Y2)>();
            void DuseyEkle(XYZ a, XYZ b)
            {
                if (Math.Abs(a.X - b.X) <= Math.Max(0.5, 0.003 * Math.Abs(a.Y - b.Y)) && Math.Abs(a.Y - b.Y) > 0)
                    duseyler.Add(((a.X + b.X) / 2, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)));
            }
            foreach (var e in _doc.Entities)
            {
                if (e is Line l) DuseyEkle(l.StartPoint, l.EndPoint);
                else if (e is LwPolyline p)
                    for (int k = 0; k < p.Vertices.Count; k++)
                    {
                        var a = p.Vertices[k].Location; var b = p.Vertices[(k + 1) % p.Vertices.Count].Location;
                        if (k == p.Vertices.Count - 1 && !p.IsClosed) break;
                        DuseyEkle(new XYZ(a.X, a.Y, 0), new XYZ(b.X, b.Y, 0));
                    }
            }

            double h = Math.Max(baslik.H, 1);
            double minBoy = 25 * h;
            var kapsayan = duseyler.Where(d => d.Y2 - d.Y1 > minBoy && d.Y1 <= baslik.Y + 2 * h && d.Y2 >= baslik.Y).ToList();
            var sagL = kapsayan.Where(d => d.X > baslik.X).OrderBy(d => d.X).ToList();
            var solL = kapsayan.Where(d => d.X < baslik.X - 10 * h).OrderByDescending(d => d.X).ToList();

            double x0, x1, y0, y1;
            if (sagL.Count > 0 && solL.Count > 0)
            {
                var sag = sagL[0]; var sol = solL[0];
                x0 = sol.X; x1 = sag.X;
                y0 = Math.Max(sol.Y1, sag.Y1); y1 = Math.Min(sol.Y2, sag.Y2);
            }
            else if (baslik.Kutu is { } k && k.X1 - k.X0 > 20 * h && k.Y1 - k.Y0 > 20 * h)
            {
                // Başlık bir pafta bloğunun özniteliği: pafta çerçevesi o bloğun kendisi
                (x0, y0, x1, y1) = k;
            }
            else
            {
                if (Environment.GetEnvironmentVariable("GES_DEBUG") == "1" && baslik.Kutu is { } kk) Notlar.Add($"[pafta bloğu küçük: {kk.X1 - kk.X0:0}x{kk.Y1 - kk.Y0:0}]");
                // Pafta çizgisi yoksa: aynı sıradaki komşu başlıklara göre pencere
                var komsu = PlanBasliklari.Where(b => Math.Abs(b.Y - baslik.Y) < 3 * h && b.X < baslik.X - 10 * h).OrderByDescending(b => b.X).FirstOrDefault();
                double gen = komsu != null ? baslik.X - komsu.X : 200 * h;
                x0 = baslik.X - gen; x1 = baslik.X + 5 * h; y0 = baslik.Y - 2 * h; y1 = baslik.Y + gen * 0.7;
                Notlar.Add("Pafta sınır çizgisi bulunamadı; çatı planı penceresi başlık konumundan tahmin edildi.");
            }
            // Aynı paftada başka plan başlıkları varsa (üstte çatı kat planı, yanda başka plan) pencere bu başlığa ait kısma daraltılır.
            // Başlıklar çizimin altındadır: üstteki başlığın altına kadar; yandaki başlıkla ortadan bölünür.
            foreach (var d in PlanBasliklari)
            {
                if (ReferenceEquals(d, baslik) || d.X < x0 || d.X > x1 || d.Y < y0 || d.Y > y1) continue;
                if (d.Y > baslik.Y + 5 * h) y1 = Math.Min(y1, d.Y - h);
                else if (Math.Abs(d.Y - baslik.Y) < 3 * h && Math.Abs(d.X - baslik.X) > 20 * h && OlcekliBaslik(d) && OlcekliBaslik(baslik))
                {
                    double orta = (d.X + baslik.X) / 2;
                    if (d.X > baslik.X) x1 = Math.Min(x1, orta); else x0 = Math.Max(x0, orta);
                }
            }
            if (Environment.GetEnvironmentVariable("GES_DEBUG") == "1") Notlar.Add($"[pafta {baslik.Metin} @{baslik.X:0},{baslik.Y:0} h={h:0.##}: x {x0:0}-{x1:0} y {y0:0}-{y1:0}; aday sol={string.Join(",", kapsayan.Where(d => d.X < baslik.X).Select(d => $"{d.X:0}[{d.Y1:0}..{d.Y2:0}]").Take(8))} sag={string.Join(",", kapsayan.Where(d => d.X > baslik.X).Select(d => $"{d.X:0}[{d.Y1:0}..{d.Y2:0}]").Take(8))}]");
            double pay = (x1 - x0) * 0.005;
            x0 += pay; x1 -= pay; y0 += pay; y1 -= pay;

            var adaylar = new List<(List<Cizgi> C, double Cx, double Cy, double A0, double B0, double A1, double B1)>();
            var plan = new PlanCizimi();
            plan.CerceveY0 = y0; plan.CerceveY1 = y1;
            plan.BirimMetre = BirimTahmini(x1 - x0);
            double pa = panelA / plan.BirimMetre, pb = panelB / plan.BirimMetre;

            foreach (var e in _doc.Entities)
            {
                if (Harici(e.Layer?.Name)) continue;
                BoundingBox bb = default;
                bool kutuVar = true;
                try { bb = e.GetBoundingBox(); } catch { kutuVar = false; }
                bool tamIcinde = kutuVar && bb.Min.X >= x0 && bb.Max.X <= x1 && bb.Min.Y >= y0 && bb.Max.Y <= y1;
                bool kesisir = !kutuVar || (bb.Max.X > x0 && bb.Min.X < x1 && bb.Max.Y > y0 && bb.Min.Y < y1);
                if (!kesisir) continue;
                var liste = new List<Cizgi>();
                if (tamIcinde) Topla(e, new Transform(Matrix4.Identity), liste, 0, e.Layer?.Name);
                else
                {
                    // Paftadan taşan (ör. birden çok paftayı kapsayan kat planı blokları) veya kutusu okunamayan bloklar:
                    // açılır, yalnız pafta içinde kalan çizgiler alınır.
                    if (e is not Insert) continue;
                    var tum = new List<Cizgi>();
                    Topla(e, new Transform(Matrix4.Identity), tum, 0, e.Layer?.Name);
                    bool Ic(double x, double y) => x >= x0 && x <= x1 && y >= y0 && y <= y1;
                    liste.AddRange(tum.Where(c => Ic(c.X1, c.Y1) && Ic(c.X2, c.Y2)));
                    if (liste.Count == 0) continue;
                    bb = new BoundingBox(new XYZ(liste.Min(c => Math.Min(c.X1, c.X2)), liste.Min(c => Math.Min(c.Y1, c.Y2)), 0),
                                         new XYZ(liste.Max(c => Math.Max(c.X1, c.X2)), liste.Max(c => Math.Max(c.Y1, c.Y2)), 0));
                }
                if (liste.Count == 0) continue;
                if (PanelMi(e, pa, pb)) plan.PanelAdedi++;
                adaylar.Add((liste, (bb.Min.X + bb.Max.X) / 2, (bb.Min.Y + bb.Max.Y) / 2, bb.Min.X, bb.Min.Y, bb.Max.X, bb.Max.Y));
            }
            if (adaylar.Count == 0) return null;

            // Asıl planı ayıkla: merkezlerin %3–%97 dilimi (antet/künye tablolarını dışarıda bırakır)
            double Yuzde(IEnumerable<double> v, double q) { var s = v.OrderBy(x => x).ToList(); return s[(int)Math.Clamp(Math.Round(q * (s.Count - 1)), 0, s.Count - 1)]; }
            double cx0 = Yuzde(adaylar.Select(a => a.Cx), 0.03), cx1 = Yuzde(adaylar.Select(a => a.Cx), 0.97);
            double cy0 = Yuzde(adaylar.Select(a => a.Cy), 0.03), cy1 = Yuzde(adaylar.Select(a => a.Cy), 0.97);
            double ex = (cx1 - cx0) * 0.05, ey = (cy1 - cy0) * 0.05;
            cx0 -= ex; cx1 += ex; cy0 -= ey; cy1 += ey;

            plan.MinX = plan.MinY = double.MaxValue; plan.MaxX = plan.MaxY = double.MinValue;
            foreach (var a in adaylar)
            {
                if (a.A1 < cx0 || a.A0 > cx1 || a.B1 < cy0 || a.B0 > cy1) continue;
                foreach (var c in a.C)
                {
                    plan.Cizgiler.Add(c);
                    plan.MinX = Math.Min(plan.MinX, Math.Min(c.X1, c.X2)); plan.MaxX = Math.Max(plan.MaxX, Math.Max(c.X1, c.X2));
                    plan.MinY = Math.Min(plan.MinY, Math.Min(c.Y1, c.Y2)); plan.MaxY = Math.Max(plan.MaxY, Math.Max(c.Y1, c.Y2));
                }
            }
            return plan.Cizgiler.Count > 0 ? plan : null;
        }

        /// <summary>Plan genişliğinden birim tahmini: cm (tipik), mm veya m.</summary>
        private static double BirimTahmini(double paftaGenisligi) =>
            paftaGenisligi > 30000 ? 0.001 : paftaGenisligi > 300 ? 0.01 : 1.0;

        private static bool PanelMi(Entity e, double pa, double pb)
        {
            BoundingBox bb;
            try { bb = e.GetBoundingBox(); } catch { return false; }
            if (e is not LwPolyline && e is not Insert) return false;
            if (e is LwPolyline p && (p.Vertices.Count < 4 || p.Vertices.Count > 5)) return false;
            double w = bb.Max.X - bb.Min.X, h = bb.Max.Y - bb.Min.Y;
            bool Yakin(double x, double y) => Math.Abs(x - y) <= 0.03 * y;
            return (Yakin(w, pa) && Yakin(h, pb)) || (Yakin(w, pb) && Yakin(h, pa));
        }

        private static void Topla(Entity e, Transform t, List<Cizgi> l, int derinlik, string ustKatman)
        {
            string katman = e.Layer?.Name;
            if (katman == "0" && ustKatman != null) katman = ustKatman;
            if (derinlik > 0 && Harici(katman)) return;
            bool kalin = KalinKatman.Any((katman ?? "").ToUpper(Tr).Contains);
            void Ekle(XYZ a, XYZ b)
            {
                var p = t.ApplyTransform(a); var q = t.ApplyTransform(b);
                l.Add(new Cizgi(p.X, p.Y, q.X, q.Y, kalin));
            }
            void YayEkle(XYZ m, double r, double b0, double b1)
            {
                while (b1 < b0) b1 += 2 * Math.PI;
                int n = Math.Clamp((int)((b1 - b0) / (Math.PI / 24)) + 1, 2, 96);
                XYZ onceki = new(m.X + r * Math.Cos(b0), m.Y + r * Math.Sin(b0), 0);
                for (int k = 1; k <= n; k++)
                {
                    double a = b0 + (b1 - b0) * k / n;
                    var s = new XYZ(m.X + r * Math.Cos(a), m.Y + r * Math.Sin(a), 0);
                    Ekle(onceki, s); onceki = s;
                }
            }
            switch (e)
            {
                case Line ln: Ekle(ln.StartPoint, ln.EndPoint); break;
                case Arc ar: YayEkle(ar.Center, ar.Radius, ar.StartAngle, ar.EndAngle); break;
                case Circle ci: YayEkle(ci.Center, ci.Radius, 0, 2 * Math.PI); break;
                case LwPolyline pl:
                    {
                        int n = pl.Vertices.Count;
                        for (int k = 0; k < n; k++)
                        {
                            if (k == n - 1 && !pl.IsClosed) break;
                            var v0 = pl.Vertices[k]; var v1 = pl.Vertices[(k + 1) % n];
                            var a = new XYZ(v0.Location.X, v0.Location.Y, 0); var b = new XYZ(v1.Location.X, v1.Location.Y, 0);
                            if (Math.Abs(v0.Bulge) < 1e-9) { Ekle(a, b); continue; }
                            // yaylı segment
                            double bu = v0.Bulge, ux = b.X - a.X, uy = b.Y - a.Y;
                            if (ux * ux + uy * uy < 1e-18) continue;
                            double k2 = (1 - bu * bu) / (4 * bu);
                            var m = new XYZ(a.X + ux / 2 - k2 * uy, a.Y + uy / 2 + k2 * ux, 0);
                            double r = Math.Sqrt((a.X - m.X) * (a.X - m.X) + (a.Y - m.Y) * (a.Y - m.Y));
                            double a0 = Math.Atan2(a.Y - m.Y, a.X - m.X), a1 = Math.Atan2(b.Y - m.Y, b.X - m.X);
                            if (bu > 0) YayEkle(m, r, a0, a1); else YayEkle(m, r, a1, a0);
                        }
                        break;
                    }
                case Insert ins when ins.Block != null && derinlik < 6:
                    {
                        if (HaricBlok.Any((ins.Block.Name ?? "").ToUpper(Tr).Contains)) break;
                        var tt = new Transform(t.Matrix * ins.GetTransform().Matrix);
                        foreach (var c in ins.Block.Entities) Topla(c, tt, l, derinlik + 1, katman);
                        break;
                    }
            }
        }

        /// <summary>Mahya ile çatı bölgesindeki en düşük kot farkından eğim tahmini (derece).</summary>
        public double? EgimTahmini(PlanCizimi plan)
        {
            if (MahyaKotu == null || plan == null) return null;
            var bolgede = _bagilKotlar.Where(k => k.X >= plan.MinX && k.X <= plan.MaxX && k.Y >= plan.MinY && k.Y <= plan.MaxY && k.Rel < MahyaKotu)
                                      .Select(k => k.Rel).ToList();
            if (bolgede.Count == 0) return null;
            double dh = MahyaKotu.Value - bolgede.Min();
            double yarim = Math.Min(plan.Genislik, plan.Yukseklik) * plan.BirimMetre / 2;
            if (dh <= 0 || yarim <= 0) return null;
            double a = Math.Atan(dh / yarim) * 180 / Math.PI;
            return a is > 1 and < 45 ? a : null;
        }
    }
}
