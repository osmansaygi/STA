using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ST4Yardimci
{
    /// <summary>Çizim yazısı. X0–X1 tahmini yatay kapsam, Yc ilk satırın düşey ortası, Grup blok örneği (0 = model).</summary>
    internal sealed class KapakYazi
    {
        public string Metin = "";
        public double X, Y, H;
        public int Grup;
        public double X0, X1, Yc;
        public bool Egik;
        public double Aci, Boy;
        public double Cx => (X0 + X1) / 2;
    }

    /// <summary>Mimari projedeki bir kapak (blok) ve okunan alanları.</summary>
    internal sealed class MimariKapak
    {
        public string Ad = "";
        public readonly Dictionary<string, string> Alan = new Dictionary<string, string>();
        public string Al(string anahtar) => Alan.TryGetValue(anahtar, out string v) ? v : "";
        public string Gorunen
        {
            get
            {
                string a = (Ad ?? "").Trim();
                if (a.Length == 0) return "(adsız kapak)";
                string u = a.ToUpper(MimariKapakOku.Tr);
                return u.Contains("BLOK") || a.Length > 3 ? u : u + " BLOK";
            }
        }
        public override string ToString() => Gorunen;
    }

    /// <summary>
    /// Mimari proje kapağı (antet/künye) okuyucu. GesRaporu/DwgKapak.cs mantığının AutoCAD API'siyle çalışan karşılığı
    /// (aynı etiket sözlüğü ve geometrik arama); statik kapak için NİTELİĞİ, İHTİVA ETTİĞİ YER, İNŞAAT ALANI, maliyetler,
    /// yapı sınıfı ve yapı denetim firması eklendi. Dosya ayrı bir veritabanına okunur, açık çizime dokunulmaz.
    /// </summary>
    internal static class MimariKapakOku
    {
        internal static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        internal const string AMahalle = "MAHALLE", APafta = "PAFTA", AAda = "ADA", AParsel = "PARSEL", AYuzolcumu = "YÜZÖLÇÜMÜ",
            AIhtiva = "İHTİVA", ACinsi = "CİNSİ", AKat = "KAT ADEDİ", AInsaatAlani = "İNŞAAT ALANI", AKullanim = "KULLANIM AMACI",
            AM2 = "M2 MALİYETİ", AToplam = "TOPLAM MALİYET", ASinif = "YAPI SINIFI", ABlok = "BLOK", ANitelik = "NİTELİĞİ",
            ASahip = "YAPI SAHİBİ", ADenetim = "YAPI DENETİM", AIl = "İL", AIlce = "İLÇE", AMuellif = "MÜELLİF";

        private const string Pap = "PAFTA/ADA/PARSEL", MahIlce = "MAH/İLÇE", IlceMah = "İLÇE/MAH", Yeri = "YERİ";

        // ------------------------------------------------------------------ dosyadan okuma
        /// <summary>Mimari DWG'deki kapakları okur. Dosya başka yerde açıksa geçici kopyası okunur.</summary>
        public static List<MimariKapak> Oku(string dwgYolu)
        {
            try { return OkuDosya(dwgYolu); }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Paylaşım ihlali (dosya yazma kilidiyle açık): geçici kopyadan oku
                string gecici = Path.Combine(Path.GetTempPath(), "st4_kapak_" + Guid.NewGuid().ToString("N") + ".dwg");
                try
                {
                    using (var kaynak = new FileStream(dwgYolu, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var hedef = new FileStream(gecici, FileMode.Create, FileAccess.Write, FileShare.None))
                        kaynak.CopyTo(hedef);
                    return OkuDosya(gecici);
                }
                finally { try { if (File.Exists(gecici)) File.Delete(gecici); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
        }

        private static List<MimariKapak> OkuDosya(string yol)
        {
            var yazilar = new List<KapakYazi>();
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(yol, FileOpenMode.OpenForReadAndAllShare, true, null);
                db.CloseInput(true);
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var toplayici = new Toplayici(tr, yazilar);
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    foreach (ObjectId id in ms) toplayici.Topla(id, Matrix3d.Identity, 0, 0);
                    tr.Commit();
                }
            }
            Dok(yazilar);
            return Coz(yazilar);
        }

        /// <summary>Geliştirme denetimi: KAPAK_DOKUM ortam değişkeni bir dosya yoluysa okunan yazılar oraya dökülür.</summary>
        private static void Dok(List<KapakYazi> yazilar)
        {
            string yol = Environment.GetEnvironmentVariable("KAPAK_DOKUM");
            if (string.IsNullOrWhiteSpace(yol)) return;
            try
            {
                var inv = CultureInfo.InvariantCulture;
                File.WriteAllLines(yol, yazilar.Where(y => y.Metin.Length <= 120).OrderByDescending(y => y.Yc).ThenBy(y => y.X0)
                    .Select(y => string.Format(inv, "{0:0.#}\t{1:0.#}\t{2:0.#}\t{3:0.##}\t{4}\t{5}\t{6}", y.X0, y.X1, y.Yc, y.H, y.Grup, y.Egik ? "dik" : "", y.Metin)),
                    Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Model alanı ve iç içe bloklardaki yazıları (görünür öznitelikler dahil) dünya koordinatında toplar.</summary>
        private sealed class Toplayici
        {
            private readonly Transaction _tr;
            private readonly List<KapakYazi> _yazilar;
            private readonly Dictionary<ObjectId, bool> _yaziVar = new Dictionary<ObjectId, bool>();
            private int _grupSayac;

            public Toplayici(Transaction tr, List<KapakYazi> yazilar) { _tr = tr; _yazilar = yazilar; }

            /// <summary>Blok tanımında (iç içe dahil) yazı veya öznitelikli blok var mı? Yazısız bloklar gezilmez.</summary>
            private bool YaziVar(ObjectId btrId, int derinlik)
            {
                if (_yaziVar.TryGetValue(btrId, out bool v)) return v;
                _yaziVar[btrId] = false; // döngüsel başvuruya karşı
                bool var = false;
                if (derinlik < 8 && _tr.GetObject(btrId, OpenMode.ForRead) is BlockTableRecord btr)
                {
                    foreach (ObjectId id in btr)
                    {
                        string ad = id.ObjectClass.DxfName;
                        if (ad == "TEXT" || ad == "MTEXT") { var = true; break; }
                        if (ad == "INSERT" || ad == "ACAD_TABLE")
                        {
                            if (!(_tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                            if (br.AttributeCollection.Count > 0 || YaziVar(br.BlockTableRecord, derinlik + 1)) { var = true; break; }
                        }
                    }
                }
                _yaziVar[btrId] = var;
                return var;
            }

            public void Topla(ObjectId id, Matrix3d t, int derinlik, int grup)
            {
                string dxf = id.ObjectClass.DxfName;
                if (dxf != "TEXT" && dxf != "MTEXT" && dxf != "INSERT" && dxf != "ACAD_TABLE") return;
                DBObject o = _tr.GetObject(id, OpenMode.ForRead);
                if (o is MText m) { MetinEkle(m, t, grup); return; }
                if (o is AttributeDefinition) return; // blok tanımındaki şablon; asıl değer örnekteki öznitelikte
                if (o is DBText te) { TextEkle(te, t, grup); return; }
                if (!(o is BlockReference br) || derinlik >= 6) return;

                int g = ++_grupSayac;
                foreach (ObjectId aid in br.AttributeCollection)
                {
                    if (aid.IsErased) continue;
                    if (_tr.GetObject(aid, OpenMode.ForRead) is AttributeReference a && !a.Invisible && !string.IsNullOrWhiteSpace(a.TextString))
                        TextEkle(a, t, g);
                }
                if (br.BlockTableRecord.IsNull || !YaziVar(br.BlockTableRecord, 0)) return;
                Matrix3d tt = t * br.BlockTransform;
                var btr = (BlockTableRecord)_tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                if (btr.IsFromExternalReference && !btr.IsResolved) return;
                foreach (ObjectId cid in btr) Topla(cid, tt, derinlik + 1, g);
            }

            private static double Olcek(Matrix3d t) => new Vector3d(0, 1, 0).TransformBy(t).Length;

            private static double Genislik(string s, double h) => Math.Max(1, s.Length) * h * 0.72;

            private static double DunyaAcisi(double aci, Matrix3d t)
            {
                Vector3d b = new Vector3d(1, 0, 0).TransformBy(t);
                return Math.IEEERemainder(aci + Math.Atan2(b.Y, b.X), 2 * Math.PI);
            }

            private void AciliEkle(string s, Point3d p, Point3d ekleme, double h, double w, int hiza, double aci, int grup)
            {
                double y0 = hiza == 1 ? p.Y - w / 2 : hiza == 2 ? p.Y - w : p.Y;
                _yazilar.Add(new KapakYazi
                { Metin = s, X = ekleme.X, Y = ekleme.Y, H = h, Grup = grup, X0 = p.X - h, X1 = p.X, Yc = y0 + w / 2, Boy = w, Aci = aci, Egik = true });
            }

            private void MetinEkle(MText m, Matrix3d t, int grup)
            {
                string ham = m.Contents ?? "";
                Point3d p = m.Location.TransformBy(t);
                double s = Olcek(t), h = m.TextHeight * s;
                var satirlar = ham.Split(new[] { @"\P", "\n" }, StringSplitOptions.None).Select(Duz).Where(x => x.Length > 0).ToList();
                int n = Math.Max(1, satirlar.Count);
                double w = Genislik(satirlar.OrderByDescending(x => x.Length).FirstOrDefault() ?? "", h);
                if (m.Width > 0) w = Math.Min(w, m.Width * s);
                int ap = Math.Max(1, Math.Min(9, (int)m.Attachment)); // üst/orta/alt × sol/orta/sağ
                int kolon = (ap - 1) % 3, sira = (ap - 1) / 3;
                Vector3d d = m.Direction;
                double aci = DunyaAcisi(Math.Abs(d.X) + Math.Abs(d.Y) > 1e-9 ? Math.Atan2(d.Y, d.X) : m.Rotation, t);
                string duz = Duz(ham);
                if (duz.Length == 0) return;
                if (Math.Abs(aci) > 0.02) { AciliEkle(duz, p, p, h, w, kolon, aci, grup); return; }
                double x0 = kolon == 1 ? p.X - w / 2 : kolon == 2 ? p.X - w : p.X;
                double toplam = (n - 1) * 1.667 * h;
                double yc = sira == 0 ? p.Y - h / 2 : sira == 1 ? p.Y + toplam / 2 : p.Y + h / 2 + toplam;
                _yazilar.Add(new KapakYazi { Metin = duz, X = p.X, Y = p.Y, H = h, Grup = grup, X0 = x0, X1 = x0 + w, Yc = yc });
            }

            private void TextEkle(DBText te, Matrix3d t, int grup)
            {
                string s = Duz(te.TextString);
                if (s.Length == 0) return;
                double h = te.Height * Olcek(t);
                TextHorizontalMode ha = te.HorizontalMode; TextVerticalMode va = te.VerticalMode;
                bool hizali = ha != TextHorizontalMode.TextLeft || va != TextVerticalMode.TextBase;
                Point3d ip = te.Position.TransformBy(t);
                Point3d p = hizali ? te.AlignmentPoint.TransformBy(t) : ip;
                double w = Genislik(s, h) * (te.WidthFactor > 0 ? te.WidthFactor : 1);
                double aci = DunyaAcisi(te.Rotation, t);
                bool orta = ha == TextHorizontalMode.TextCenter || ha == TextHorizontalMode.TextMid;
                if (Math.Abs(aci) > 0.02)
                {
                    AciliEkle(s, p, ip, h, w, orta ? 1 : ha == TextHorizontalMode.TextRight ? 2 : 0, aci, grup);
                    return;
                }
                double x0, x1;
                if (ha == TextHorizontalMode.TextAlign || ha == TextHorizontalMode.TextFit) { x0 = Math.Min(ip.X, p.X); x1 = Math.Max(ip.X, p.X); }
                else
                {
                    x0 = orta ? p.X - w / 2 : ha == TextHorizontalMode.TextRight ? p.X - w : p.X;
                    x1 = x0 + w;
                }
                double yc = ha == TextHorizontalMode.TextMid || va == TextVerticalMode.TextVerticalMid ? p.Y
                          : va == TextVerticalMode.TextTop ? p.Y - h / 2 : p.Y + h / 2;
                _yazilar.Add(new KapakYazi { Metin = s, X = ip.X, Y = ip.Y, H = h, Grup = grup, X0 = x0, X1 = x1, Yc = yc });
            }
        }

        // ------------------------------------------------------------------ dosya adından proje sahibi
        /// <summary>Dosya adında sahibin bittiğini gösteren sözcükler (ASCII anahtar).</summary>
        private static readonly HashSet<string> DosyaDurakSozcukleri = new HashSet<string>(
            ("MIMARI MIMARIPROJE PROJE PROJESI REVIZE REVIZYON REV RUHSAT STATIK BELEDIYE TESLIM PLAN PLANI PLANLARI PLANLAR " +
             "TADILAT KESIT KESITLER GORUNUS GORUNUSLER VAZIYET AVAN UYGULAMA FINAL SON CIZIM ASMATAVAN TAVAN ASMA KAT KATLAR " +
             "BLOK BLOKLAR ADA PARSEL DETAY DETAYLAR DETAYLARI KAPAK IHALE NOTLAR APLIKASYON SUPERPOZE KOTLANDIRMA KOTLAR DOSEME " +
             "TIPI BINA BINASI IDARI ONAYLI RECOVER YUP BUP STA DWG CEPHELER CEPHE MERDIVEN SISTEM HIZASI BLK OTP OTOPARK").Split(' '));

        /// <summary>
        /// Mimari DWG dosya adından proje sahibi: "17.08.2026 - ADEM TAFLI MİMARİ" → ADEM TAFLI, "Musa KARACAN - Mimari Proje" → MUSA KARACAN,
        /// "MEHMET SEVİÇ VE HİSS.- 16555 ADA - 1 PARSEL…" → MEHMET SEVİÇ VE HİSS. Baştaki tarih/numara/proje sözcükleri atlanır; ilk
        /// tire, sayı veya proje sözcüğünde (MİMARİ, PROJE, BLOK, PLAN …) durulur. En az iki harf sözcüğü yoksa null.
        /// </summary>
        internal static string SahipDosyaAdindan(string dwgYolu)
        {
            string ad;
            try { ad = Path.GetFileNameWithoutExtension(dwgYolu ?? ""); } catch (ArgumentException) { return null; }
            if (string.IsNullOrWhiteSpace(ad)) return null;
            string s = Regex.Replace(ad.Replace('_', ' '), @"([\-(),])", " $1 ");
            var parca = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var alinan = new List<string>();
            foreach (string p in parca)
            {
                string anahtar = Anahtar(p);
                bool ayirac = p == "-" || p == "(" || p == ")" || p == ",";
                bool sayili = Regex.IsMatch(p, @"\d");
                bool durak = anahtar.Length > 0 && DosyaDurakSozcukleri.Contains(anahtar);
                if (alinan.Count == 0)
                {
                    if (ayirac || sayili || durak || anahtar.Length == 0) continue; // baştaki tarih, numara, "MİMARİ -" atlanır
                    alinan.Add(p);
                    continue;
                }
                if (ayirac || sayili || durak) break;
                alinan.Add(p);
            }
            while (alinan.Count > 0 && Anahtar(alinan[alinan.Count - 1]).Length <= 1) alinan.RemoveAt(alinan.Count - 1); // "… A BLOK"
            if (alinan.Count(x => Regex.Matches(x, @"\p{L}").Count >= 2) < 2) return null;
            string sonuc = string.Join(" ", alinan).Trim(' ', '-', ',').ToUpper(Tr);
            return sonuc.Length >= 5 && sonuc.Length <= 80 ? sonuc : null;
        }

        /// <summary>MText biçim kodlarını ve özel işaretleri düz yazıya çevirir.</summary>
        internal static string Duz(string m)
        {
            if (string.IsNullOrEmpty(m)) return "";
            string s = m.Replace("\\P", " ").Replace("\\~", " ");
            s = Regex.Replace(s, @"\\S([^;^/#]*)[\^/#]([^;]*);", "$1/$2");
            s = Regex.Replace(s, @"\\[fFHhCcTQWApq][^;]*;", "");
            s = Regex.Replace(s, @"\\[LlOoKk]", "");
            s = s.Replace("{", "").Replace("}", "").Replace("%%c", "Ø").Replace("%%d", "°").Replace("%%p", "±")
                 .Replace("%%C", "Ø").Replace("%%D", "°").Replace("%%P", "±");
            s = Regex.Replace(s, @"\\U\+([0-9A-Fa-f]{4})", x => ((char)Convert.ToInt32(x.Groups[1].Value, 16)).ToString());
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        // ------------------------------------------------------------------ etiket sözlüğü
        /// <summary>Karşılaştırma anahtarı: büyük harf, Türkçe harfler ASCII, yalnız harf ve rakam.</summary>
        internal static string Anahtar(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string u = s.ToUpper(Tr);
            var sb = new StringBuilder(u.Length);
            foreach (char c in u)
            {
                char d;
                switch (c)
                {
                    case 'İ': case 'I': case 'ı': case 'Î': d = 'I'; break;
                    case 'Ş': d = 'S'; break;
                    case 'Ğ': d = 'G'; break;
                    case 'Ü': case 'Û': d = 'U'; break;
                    case 'Ö': d = 'O'; break;
                    case 'Ç': d = 'C'; break;
                    case 'Â': d = 'A'; break;
                    default: d = c; break;
                }
                if (char.IsLetterOrDigit(d)) sb.Append(d);
            }
            return sb.ToString();
        }

        private static readonly Dictionary<string, string> EtiketAlani = Sozluk(
            new[] { AIl, "IL ILI" },
            new[] { AIlce, "ILCE ILCESI" },
            new[] { AMahalle, "MAHALLE MAHALLESI MAHALLESOKAK MAHALLECADDESOKAK MAHALLEMEVKI MAHALLEKOY MAHALLEKOYU MAHALLEKOYMEVKI" },
            new[] { MahIlce, "MAHALLESIILCESI MAHALLEILCE MAHALLEILCESI" },
            new[] { IlceMah, "ILCEMAHALLE ILCESIMAHALLESI ILCEMAHALLESI" },
            new[] { Yeri, "YERI YAPININYERI" },
            new[] { Pap, "PAFTAADAPARSEL PAFTAADAPARSELNO ADAPARSEL ADAPARSELNO PAFTAADAPARSELNUMARASI" },
            new[] { APafta, "PAFTA PAFTANO" },
            new[] { AAda, "ADA ADANO" },
            new[] { AParsel, "PARSEL PARSELNO" },
            new[] { AYuzolcumu, "YUZOLCUMU PARSELALANI ARSAALANI ARSAYUZOLCUMU" },
            new[] { AIhtiva, "IHTIVAETTIGIYER IHTIVAETTIGIYAPI IHTIVAETTIGI" },
            new[] { ACinsi, "CINSI YAPICINSI YAPININCINSI INSAATTURU YAPITURU TASIYICISISTEM" },
            new[] { AKat, "KATADEDI KATADETI KATSAYISI INSAATKATSAYISI" },
            new[] { AInsaatAlani, "BINAINSAATALANI YAPIINSAATALANI INSAATALANI TOPLAMINSAATALANI TOPLAMYAPIINSAATALANI YAPIALANI TOPLAMALAN" },
            new[] { AKullanim, "KULLANIMAMACI" },
            new[] { AM2, "MMALIYETI M2MALIYETI M2MALIYET MMALIYET M2BIRIMMALIYETI BIRIMMALIYET BIRIMMALIYETI" },
            new[] { AToplam, "TOPLAMMALIYETI TOPLAMMALIYET YAPIMALIYETI TOPLAMYAPIMALIYETI" },
            new[] { ASinif, "YAPISINIFI MIMARISINIFI MIMARIHIZMETSINIFI MIMHIZSINORANI MIMHIZSNFORANI MIMHIZSINIFORANI MIMHIZSNFVEORANI MIMHIZSINORTAL YAPISINIFIVEGRUBU" },
            new[] { ANitelik, "NITELIGI YAPININNITELIGI PROJENINNITELIGI INSAATINNITELIGI" },
            new[] { ABlok, "BLOKADI BLOK BLOKNO" },
            new[] { ASahip, "YAPISAHIBI PROJESAHIBI PROJENINSAHIBI SAHIBI MALSAHIBI ISSAHIBI ISVEREN MALIKI MALIK YAPISAHIBIVEKILI YAPISAHIPLERI " +
                            "PROJESAHIPLERI PROJESAHIBIADISOYADI PROJESAHIBININADISOYADI PROJESAHIBIUNVANI YAPISAHIBIADISOYADI YAPISAHIBININADISOYADI " +
                            "MALSAHIPLERI SAHIPLERI ARSASAHIBI ARSASAHIPLERI" },
            new[] { ADenetim, "YAPIDENETIMFIRMASI YAPIDENETIMKURULUSU YAPIDENETIMSIRKETI YAPIDENETIM YAPIDENETIMFIRMAYETKILISI YAPIDENETIMKURULUSUYETKILISI YAPIDENETIMFIRMASIYETKILISI" },
            new[] { AMuellif, "MIMARIPROJEMUELLIFI PROJEMUELLIFI PROJENINMUELLIFI" });

        /// <summary>Alan değeri olmayan ama etiket olan kapak yazıları (değer aranırken etiket sayılır).</summary>
        private static readonly HashSet<string> DigerEtiketler = new HashSet<string>(
            ("MALIYETI ARSA BINA ARSABILGILERI BINABILGILERI PARSELBILGILERI ADISOYADI ADSOYAD ADI SOYADI ODASICIL ODASICILNO SICILNO " +
             "BUROTESCILI BUROTESNO BUROTESCILNO UNVANI YAPIMUTEAHHITI YAPIMUTEAHITI MUTEAHHIT DENETCI DENETCIMIMAR " +
             "BELEDIYEONAYI BELEDIYEONAY TAKS KAKS HMAX SOKAK CADDE CADDESOKAK YAPININ OLCEK PAFTAISMI BOLUM CIZEN " +
             "TARIH IMZA BELGE SICIL PROJE ADRES YASALUYARI STATIKPROJEMUELLIFI ELEKTRIKPROJEMUELLIFI MEKANIKPROJEMUELLIFI FENNIMESUL")
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

        /// <summary>Kişi kutusundaki alt etiketler (sahip değeri bunların sağında/altında olabilir).</summary>
        private static readonly HashSet<string> AltEtiketler = new HashSet<string>("ADISOYADI ADSOYAD ADISOYADIUNVANI UNVANI ADI FIRMAADI FIRMAUNVANI".Split(' '));

        private static readonly string[] EtiketKokleri =
            { "MALIYETI", "MALIYET", "SINIFI", "ORANI", "ONAYI", "SICIL", "TESCIL", "SOYADI", "MUELLIFI", "DENETCI", "DENETIM",
              "MUTEAHHITI", "MUTEAHITI", "ALANI", "IMZA", "UNVANI", "KURULUSU", "BILGILERI" };

        private static Dictionary<string, string> Sozluk(params string[][] l)
        {
            var d = new Dictionary<string, string>();
            foreach (string[] cift in l)
                foreach (string e in cift[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) d[e] = cift[0];
            return d;
        }

        private static string AlanOf(KapakYazi y) => EtiketAlani.TryGetValue(Anahtar(y.Metin), out string a) ? a : null;

        /// <summary>Yazı bir kapak etiketi mi (bilinen etiket, ':' ile biten veya etiket kökü içeren kısa yazı)?</summary>
        private static bool EtiketMi(KapakYazi y)
        {
            string a = Anahtar(y.Metin);
            if (a.Length == 0) return false;
            if (EtiketAlani.ContainsKey(a) || DigerEtiketler.Contains(a)) return true;
            string m = y.Metin.Trim();
            if (m.EndsWith(":", StringComparison.Ordinal) && m.Length < 40) return true;
            var kelimeler = Regex.Split(m, @"[\s/.\-]+").Select(Anahtar).Where(k => k.Length > 0).ToList();
            return kelimeler.Count <= 5 && kelimeler.Any(k => EtiketKokleri.Contains(k));
        }

        private static bool BilinenEtiket(KapakYazi y)
        {
            string a = Anahtar(y.Metin);
            return EtiketAlani.ContainsKey(a) || DigerEtiketler.Contains(a);
        }

        /// <summary>"YAPI DENETÇİSİ", "… FENNİ MESUL", "YAPI DENETİM ONAYI" gibi kutu başlıkları (değer değil).</summary>
        private static bool DenetimBasligi(string m) => Regex.IsMatch(Anahtar(m), "DENETCI|FENNIMESUL|ONAYI$|YETKILISI$|MUELLIFI$");

        private static bool BosMu(string m) => !Regex.IsMatch(m ?? "", @"[\p{L}\p{N}]");

        /// <summary>Kanun/uyarı metni, adres, iletişim satırı: değer olamaz.</summary>
        private static bool Kalip(string m)
        {
            string u = m.ToUpper(Tr);
            return m.Length > 90 || u.Contains("SAYILI") || u.Contains("KANUN") || u.Contains("YÖNETMELİ") || u.IndexOf('@') >= 0
                || Regex.IsMatch(u, @"\bTEL\s*[:.]|\bFAX\b|\bGSM\b|WWW\.");
        }

        // ------------------------------------------------------------------ geometrik arama
        private static double Gen(KapakYazi y) => Math.Max(y.X1 - y.X0, y.H);

        private static bool AyniSatir(KapakYazi a, KapakYazi b) => Math.Abs(a.Yc - b.Yc) <= 0.6 * Math.Max(a.H, b.H);

        /// <summary>Yazı yüksekliği etiketle uyumlu mu (süs harfleri, başlıklar ve dipnotlar elenir)?</summary>
        private static bool BoyUygun(KapakYazi lab, KapakYazi c) => c.H >= 0.4 * lab.H && c.H <= 1.6 * lab.H;

        private static bool DikeyAci(double aci) => Math.Abs(aci - Math.PI / 2) < 0.05;

        private sealed class Bolge
        {
            public readonly List<KapakYazi> Liste; // yatay yazılar, Yc'ye göre sıralı
            public readonly List<KapakYazi> Dikey; // 90° yan etiketler
            private readonly double[] _yc;
            public Bolge(IEnumerable<KapakYazi> y)
            {
                var l = y.Where(x => x.Metin.Length > 0).ToList();
                Liste = l.Where(x => !x.Egik).OrderBy(x => x.Yc).ToList();
                Dikey = l.Where(x => x.Egik && DikeyAci(x.Aci)).ToList();
                _yc = Liste.Select(x => x.Yc).ToArray();
            }
            public IEnumerable<KapakYazi> Bant(double y0, double y1)
            {
                int i = Array.BinarySearch(_yc, y0);
                if (i < 0) i = ~i;
                while (i > 0 && _yc[i - 1] >= y0) i--;
                for (; i < _yc.Length && _yc[i] <= y1; i++) yield return Liste[i];
            }
        }

        /// <summary>Etiketin sağındaki ilk yazı (aynı satır). Etiket blok içindeyse önce aynı blok aranır.</summary>
        private static KapakYazi SagYazi(KapakYazi lab, Bolge b)
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
        private static List<KapakYazi> SatirEtiketleri(KapakYazi lab, Bolge b) =>
            b.Bant(lab.Yc - 1.6 * lab.H, lab.Yc + 1.6 * lab.H)
             .Where(c => !ReferenceEquals(c, lab) && AyniSatir(lab, c) && Math.Abs(c.Cx - lab.Cx) < 45 * lab.H && EtiketMi(c)
                         && (lab.Grup == 0 || c.Grup == lab.Grup))
             .ToList();

        /// <summary>
        /// Sütunda etiketin altındaki (yon=-1) veya üstündeki (yon=+1) ilk değer. Aday, satırdaki diğer sütun başlıklarına
        /// değil bu etikete en yakın olmalı. İlk rastlanan etiket aramayı keser (alt etiketler kişi aramasında atlanır).
        /// </summary>
        private static KapakYazi SutunYazisi(KapakYazi lab, Bolge b, int yon, double enFazla, bool kisi) =>
            lab.Grup != 0
                ? SutunYazisi(lab, b, yon, enFazla, kisi, true) ?? SutunYazisi(lab, b, yon, enFazla, kisi, false)
                : SutunYazisi(lab, b, yon, enFazla, kisi, false);

        private static KapakYazi SutunYazisi(KapakYazi lab, Bolge b, int yon, double enFazla, bool kisi, bool ayniGrup)
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
            foreach (KapakYazi c in adaylar)
            {
                string a = Anahtar(c.Metin);
                if (kisi && (AltEtiketler.Contains(a) || UnvanMi(c.Metin) || Regex.IsMatch(c.Metin, @"^\s*AD[Iİ]\s*[-/]?\s*SOYAD[Iİ]\s*:?\s*$", RegexOptions.IgnoreCase))) continue;
                if (kisi && DenetimBasligi(c.Metin)) return null;
                if (EtiketMi(c)) return null;
                if (Kalip(c.Metin)) continue;
                return c;
            }
            return null;
        }

        /// <summary>Etiketin aynı satırdaki en yakın komşusu da etiketse satır bir sütun başlığı satırıdır.</summary>
        private static bool BaslikSatiriMi(KapakYazi lab, Bolge b)
        {
            KapakYazi en = b.Bant(lab.Yc - 1.6 * lab.H, lab.Yc + 1.6 * lab.H)
                .Where(c => !ReferenceEquals(c, lab) && AyniSatir(lab, c) && !BosMu(c.Metin) && BoyUygun(lab, c)
                            && (lab.Grup == 0 || c.Grup == lab.Grup))
                .Select(c => new { c, Bosluk = c.X0 >= lab.X1 ? c.X0 - lab.X1 : c.X1 <= lab.X0 ? lab.X0 - c.X1 : 0.0 })
                .Where(x => x.Bosluk < 35 * lab.H)
                .OrderBy(x => x.Bosluk).Select(x => x.c).FirstOrDefault();
            return en != null && EtiketMi(en);
        }

        /// <summary>Dik yan etiketin sağındaki, etiket boyu hizasındaki ilk değer.</summary>
        private static KapakYazi DikeyDeger(KapakYazi lab, Bolge b)
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
        private static void KapakOku(Bolge b, KapakYazi capa, Dictionary<string, string> kapak)
        {
            var ham = new Dictionary<string, string>();
            var etiketler = b.Liste.Concat(b.Dikey).Select(y => new { y, Alan = AlanOf(y) }).Where(x => x.Alan != null)
                .OrderBy(x => capa == null ? 0 : Math.Abs(x.y.Cx - capa.Cx) + Math.Abs(x.y.Yc - capa.Yc)).ToList();

            foreach (var e in etiketler)
            {
                KapakYazi lab = e.y; string alan = e.Alan;
                if (ham.ContainsKey(alan)) continue;
                bool kisi = alan == ASahip || alan == AMuellif || alan == ADenetim;
                if (lab.Egik)
                {
                    // Kutunun yanına dik yazılmış etiket (PROJE SAHİBİ): değer kutunun içinde, sağda
                    if (kisi) { KapakYazi d = DikeyDeger(lab, b); if (d != null) ham[alan] = KisiSade(d.Metin); }
                    continue;
                }
                if (alan == AMuellif)
                {
                    KapakYazi alt = SutunYazisi(lab, b, -1, 8, true);
                    if (alt != null) ham[alan] = KisiSade(alt.Metin);
                    continue;
                }
                KapakYazi sag = SagYazi(lab, b);
                // İHTİVA ETTİĞİ YER değeri çoğu zaman "ARSA" / "BİNA": etiket sözlüğündedir ama burada değerdir.
                bool sagEtiket = sag != null && alan != AIhtiva && (EtiketMi(sag) && !(kisi && AltEtiketler.Contains(Anahtar(sag.Metin))));
                // Firma adı "… YAPI DENETİM" etiket kökü taşır; yalnız sözlükteki etiketler ve denetçi/onay başlıkları etikettir.
                if (sag != null && alan == ADenetim) sagEtiket = BilinenEtiket(sag) || DenetimBasligi(sag.Metin);
                if (sag != null && !sagEtiket && !Kalip(sag.Metin) && !(kisi && UnvanMi(sag.Metin)))
                {
                    ham[alan] = kisi ? KisiSade(sag.Metin) : Temizle(sag.Metin);
                    continue;
                }

                KapakYazi bulunan = null;
                if (kisi)
                {
                    // Kişi kutusu: önce altta, yoksa üstte (kutunun alt kenarına yazılmış etiket)
                    bulunan = SutunYazisi(lab, b, -1, 10, true) ?? (alan == ASahip ? SutunYazisi(lab, b, +1, 12, true) : null);
                    if (bulunan != null)
                    {
                        string deger = bulunan.Metin.Trim();
                        // "… LTD. ŞTİ. VE" + alt satırda "HİSSEDARLARI"
                        if (Regex.IsMatch(deger, @"(\bVE|&|,)$", RegexOptions.IgnoreCase))
                        {
                            KapakYazi devam = SutunYazisi(bulunan, b, -1, 2.5, true);
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
            foreach (KapakYazi y in b.Liste)
            {
                int i = y.Metin.IndexOf(':');
                if (i <= 0 || i >= y.Metin.Length - 1) continue;
                if (!EtiketAlani.TryGetValue(Anahtar(y.Metin.Substring(0, i)), out string alan)) continue;
                string deger = y.Metin.Substring(i + 1).Trim();
                if (ham.ContainsKey(alan) || BosMu(deger) || deger.Length > 60 || Kalip(y.Metin)) continue;
                ham[alan] = deger;
            }

            Yerlestir(ham, kapak);
            CumleyiOku(b.Liste.Where(y => y.Metin.Length < 160), kapak);
        }

        /// <summary>Ham etiket değerlerini kapak alanlarına çevirir (birleşik alanları böler).</summary>
        private static void Yerlestir(Dictionary<string, string> ham, Dictionary<string, string> kapak)
        {
            void Koy(string k, string v)
            {
                v = (v ?? "").Trim().Trim(',', ';').Trim();
                if (BosMu(v) || kapak.ContainsKey(k)) return;
                // Sayısal alanlar: doldurulmamış şablonda yalnız birim ("m²", "TL") yazar
                if ((k == AYuzolcumu || k == AInsaatAlani || k == AM2 || k == AToplam) && !Regex.IsMatch(v, @"\d")) return;
                kapak[k] = v;
            }
            foreach (string k in new[] { AIl, AIlce, ACinsi, AKat, AKullanim, ABlok, ASahip, AMuellif, AYuzolcumu, AIhtiva, AInsaatAlani, AM2, AToplam, ASinif, ANitelik, ADenetim })
                if (ham.TryGetValue(k, out string v)) Koy(k, k == AIl || k == AIlce ? v.ToUpper(Tr) : v);
            if (ham.TryGetValue(AMahalle, out string mh)) Koy(AMahalle, MahalleSade(mh));

            if (ham.TryGetValue(MahIlce, out string mi)) { string[] p = Bol(mi); Koy(AMahalle, MahalleSade(p[0])); if (p.Length > 1) Koy(AIlce, p[p.Length - 1].ToUpper(Tr)); }
            if (ham.TryGetValue(IlceMah, out string im))
            {
                string[] p = Bol(im);
                if (p.Length > 1) { Koy(AIlce, p[0].ToUpper(Tr)); Koy(AMahalle, MahalleSade(p[p.Length - 1])); } else Koy(AMahalle, MahalleSade(p[0]));
            }
            if (ham.TryGetValue(Yeri, out string yer)) YerCoz(yer, Koy);

            if (ham.TryGetValue(Pap, out string pap))
            {
                PapCoz(pap, out string pf, out string ada, out string par);
                Koy(APafta, pf); Koy(AAda, ada); Koy(AParsel, par);
            }
            foreach (string k in new[] { APafta, AAda, AParsel })
                if (ham.TryGetValue(k, out string v)) Koy(k, v.Trim('.', ' ', '-'));
            if (kapak.ContainsKey(ABlok)) kapak[ABlok] = BlokAdiSade(kapak[ABlok]);
        }

        private static string[] Bol(string v)
        {
            var l = v.Split(new[] { '/', '\\', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim(' ', '-')).Where(x => x.Length > 0).ToList();
            if (l.Count == 0) l.Add("");
            return l.ToArray();
        }

        internal static string MahalleSade(string m)
        {
            m = Regex.Replace((m ?? "").Trim(), @"\s*\b(MAHALLES[İI]|MAHALLE|MAH\.?|MH\.?)\s*$", "", RegexOptions.IgnoreCase);
            return m.Trim().ToUpper(Tr);
        }

        private static readonly HashSet<string> KonyaIlceleri = new HashSet<string>(
            ("AHIRLI AKÖREN AKŞEHİR ALTINEKİN BEYŞEHİR BOZKIR CİHANBEYLİ ÇELTİK ÇUMRA DERBENT DEREBUCAK DOĞANHİSAR EMİRGAZİ EREĞLİ " +
             "GÜNEYSINIR HADİM HALKAPINAR HÜYÜK ILGIN KADINHANI KARAPINAR KARATAY KULU MERAM SARAYÖNÜ SELÇUKLU SEYDİŞEHİR TAŞKENT " +
             "TUZLUKÇU YALIHÜYÜK YUNAK KONYA").Split(' '));

        /// <summary>"YERİ" değeri: SELÇUKLU/YAZIR, ARDIÇLI MAH., FEVZİÇAKMAK → ilçe ve/veya mahalle.</summary>
        private static void YerCoz(string yer, Action<string, string> koy)
        {
            string[] parca = Bol(yer);
            string ilce = parca.Select(p => p.ToUpper(Tr)).FirstOrDefault(p => KonyaIlceleri.Contains(p) && p != "KONYA");
            var kalan = parca.Where(p => !KonyaIlceleri.Contains(p.ToUpper(Tr))).ToList();
            if (ilce != null) koy(AIlce, ilce);
            string mah = kalan.FirstOrDefault(p => Regex.IsMatch(p, @"\bMAH", RegexOptions.IgnoreCase)) ?? kalan.FirstOrDefault();
            if (mah != null) koy(AMahalle, MahalleSade(mah));
        }

        /// <summary>Pafta/ada/parsel: sondan iki sayı parsel ve ada, kalan pafta (M28b05b2a-43704- 1, -/ 15644 / 7, 26870/5).</summary>
        internal static void PapCoz(string v, out string pafta, out string ada, out string parsel)
        {
            string s = Regex.Replace(v ?? "", @"\.{2,}|…", " ");
            var parca = Regex.Split(s, @"[/\\\-,;]").Select(p => p.Trim(' ', '.', ':')).Where(p => p.Length > 0).ToList();
            if (parca.Count == 1) parca = Regex.Split(parca[0], @"\s+").Where(p => p.Length > 0).ToList();
            bool Sayi(string p) => Regex.IsMatch(p, @"^\d+[A-Za-z]?$");
            string par = null, a = null;
            if (parca.Count > 0 && Sayi(parca[parca.Count - 1])) { par = parca[parca.Count - 1]; parca.RemoveAt(parca.Count - 1); }
            if (parca.Count > 0 && Sayi(parca[parca.Count - 1])) { a = parca[parca.Count - 1]; parca.RemoveAt(parca.Count - 1); }
            string pf = string.Join("-", parca).Trim();
            if (par != null && a == null && pf.Length > 0 && !Regex.IsMatch(pf, @"\p{L}")) { a = pf; pf = ""; }
            pafta = pf; ada = a ?? ""; parsel = par ?? "";
        }

        /// <summary>Kapak tablosunda bulunmayan bilgiler için başlık cümlesi: "KONYA İLİ MERAM İLÇESİ MELİKŞAH MAHALLESİ 2645 ADA 103 PARSEL".</summary>
        private static void CumleyiOku(IEnumerable<KapakYazi> yazilar, Dictionary<string, string> kapak)
        {
            foreach (KapakYazi y in yazilar)
            {
                string u = y.Metin.ToUpper(Tr);
                Match ada = Regex.Match(u, @"(\d+)\s*ADA\b|\bADA\s*(?:NO)?\s*:?\s*(\d+)");
                Match par = Regex.Match(u, @"(\d+)\s*PARSEL\b|\bPARSEL\s*(?:NO)?\s*:?\s*(\d+)");
                if (!ada.Success || !par.Success) continue;
                void Koy(string k, string v) { if (!string.IsNullOrWhiteSpace(v) && !kapak.ContainsKey(k)) kapak[k] = v.Trim(); }
                Koy(AAda, ada.Groups[1].Success ? ada.Groups[1].Value : ada.Groups[2].Value);
                Koy(AParsel, par.Groups[1].Success ? par.Groups[1].Value : par.Groups[2].Value);
                Match il = Regex.Match(u, @"([\p{Lu}]+)\s+İL[İI]\b"); if (il.Success) Koy(AIl, il.Groups[1].Value);
                Match ilce = Regex.Match(u, @"([\p{Lu}]+)\s+İLÇES[İI]\b"); if (ilce.Success) Koy(AIlce, ilce.Groups[1].Value);
                Match mah = Regex.Match(u, @"([\p{Lu}]+)\s+(?:MAHALLES[İI]|MAH\.)"); if (mah.Success) Koy(AMahalle, mah.Groups[1].Value);
            }
        }

        private static readonly Regex BlokRx = new Regex(@"(?:^|\(|\s)([A-ZÇĞİÖŞÜ0-9]{1,3})\s*-?\s*BLO[KĞ]U?\s*(?:\)|$)");

        internal static string BlokAdiSade(string b)
        {
            string u = (b ?? "").Trim().ToUpper(Tr);
            Match m = BlokRx.Match(u);
            return m.Success ? m.Groups[1].Value : u;
        }

        /// <summary>Kapakta "BLOK ADI" yoksa çapa çevresindeki "A BLOK" / "MİMARİ PROJE (A BLOK)" yazısı.</summary>
        private static string BlokAdiBul(Bolge b, KapakYazi capa, double r)
        {
            if (capa == null) return null;
            return b.Liste
                .Where(y => y.Metin.Length <= 40 && y.H >= 0.8 * capa.H && Math.Abs(y.Cx - capa.Cx) <= Math.Min(r, 60 * capa.H)
                            && y.Yc - capa.Yc <= Math.Min(r, 40 * capa.H) && capa.Yc - y.Yc <= Math.Min(r, 10 * capa.H))
                .Select(y => new { y, M = BlokRx.Match(y.Metin.ToUpper(Tr)) })
                .Where(x => x.M.Success)
                .OrderBy(x => Math.Abs(x.y.Cx - capa.Cx) + Math.Abs(x.y.Yc - capa.Yc))
                .Select(x => x.M.Groups[1].Value).FirstOrDefault();
        }

        // ------------------------------------------------------------------ kapakları ayırma
        /// <summary>Çizimdeki kapakları bulur: her PAFTA/ADA/PARSEL (yoksa MAHALLE, yoksa BLOK ADI) etiketi bir kapak çapasıdır.</summary>
        internal static List<MimariKapak> Coz(List<KapakYazi> yazilar)
        {
            var sonuc = new List<MimariKapak>();
            var tumu = new Bolge(yazilar);
            List<KapakYazi> Capalar(params string[] alanlar) => tumu.Liste.Where(y => alanlar.Contains(AlanOf(y))).ToList();
            var capalar = Capalar(Pap);
            if (capalar.Count == 0) capalar = Capalar(AMahalle, MahIlce, IlceMah);
            if (capalar.Count == 0) capalar = Capalar(ABlok);

            if (capalar.Count <= 1)
            {
                KapakYazi capa = capalar.FirstOrDefault();
                var k = new MimariKapak();
                KapakOku(tumu, capa, k.Alan);
                if (!k.Alan.ContainsKey(ABlok)) { string ad = BlokAdiBul(tumu, capa, double.MaxValue); if (ad != null) k.Alan[ABlok] = ad; }
                k.Ad = k.Al(ABlok);
                sonuc.Add(k);
                return sonuc;
            }

            // Kapak bölgesi: en yakın iki çapa arasındaki mesafenin %45'i
            double minD = double.MaxValue;
            for (int i = 0; i < capalar.Count; i++)
                for (int j = i + 1; j < capalar.Count; j++)
                    minD = Math.Min(minD, Math.Max(Math.Abs(capalar[i].Cx - capalar[j].Cx), Math.Abs(capalar[i].Yc - capalar[j].Yc)));
            double r = Math.Max(minD * 0.45, 1e-6);
            var hamlar = new List<MimariKapak>();
            foreach (KapakYazi c in capalar)
            {
                var bolge = new Bolge(tumu.Liste.Concat(tumu.Dikey).Where(y => Math.Abs(y.Cx - c.Cx) <= r && Math.Abs(y.Yc - c.Yc) <= r));
                var k = new MimariKapak();
                KapakOku(bolge, c, k.Alan);
                if (!k.Alan.ContainsKey(ABlok)) { string ad = BlokAdiBul(bolge, c, r); if (ad != null) k.Alan[ABlok] = ad; }
                k.Ad = k.Al(ABlok);
                hamlar.Add(k);
            }
            // Her paftada tekrarlanan aynı kapaklar birleştirilir (aynı içerik + aynı blok adı)
            string Imza(MimariKapak k) => string.Join("|", k.Alan.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value));
            foreach (var grup in hamlar.GroupBy(Imza)) sonuc.Add(grup.First());
            // Blok adı olmayan kapaklar (TRAFO, GÜVENLİK …) kullanım amacıyla, o da yoksa sırayla adlandırılır
            for (int i = 0; i < sonuc.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(sonuc[i].Ad) || sonuc.Count == 1) continue;
                string amac = sonuc[i].Al(AKullanim).ToUpper(Tr);
                bool tek = amac.Length > 3 && sonuc.Count(x => x.Al(AKullanim).ToUpper(Tr) == amac) == 1;
                sonuc[i].Ad = tek ? amac : "Kapak " + (i + 1).ToString(CultureInfo.InvariantCulture);
            }
            return sonuc.OrderBy(k => k.Ad, StringComparer.Create(Tr, true)).ToList();
        }
    }
}
