using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;

namespace RaporBirlestir
{
    /// <summary>
    /// STA4CAD statik raporundaki "Etriye paspayı / Boyuna donatı paspayı ....cm.= a / b" satırının
    /// değerini, aynı gömülü yazı tipinin glifleri ve aynı harf aralığı düzeniyle "4 / 5,5" yapar.
    /// Yalnız o satırın metin komutu (TJ/Tj) değişir; sayfanın geri kalanı aynen yazılır.
    /// </summary>
    internal static class PaspayDuzelt
    {
        public const string Hedef = "4 / 5,5";
        private static readonly Regex SatirDeseni = new(@"Etriye\s+paspay.{0,3}\s*/\s*Boyuna", RegexOptions.IgnoreCase);

        public sealed class Sonuc
        {
            public int Sayfa;           // 1 tabanlı (çıktı belgesinde)
            public string Eski = "";
            public bool Degisti;
            public string Hata;
            public string HedefDeger = Hedef;
            public override string ToString() =>
                Hata != null ? $"s.{Sayfa}: {Hata}"
                : Degisti ? $"s.{Sayfa}: {Eski} → {HedefDeger}"
                : $"s.{Sayfa}: zaten {HedefDeger}";
        }

        // ------------------------------------------------------------ yazı tipi çözümü

        private sealed class Yazitipi
        {
            public bool IkiBayt;
            public readonly Dictionary<int, string> KodMetin = new();
            public readonly Dictionary<char, int> MetinKod = new();
            public Encoding TekBayt;

            public string Coz(string ham)
            {
                var sb = new StringBuilder();
                foreach (var k in Kodlar(ham)) sb.Append(Metin(k));
                return sb.ToString();
            }

            public List<int> Kodlar(string ham)
            {
                var l = new List<int>();
                if (IkiBayt) for (int i = 0; i + 1 < ham.Length; i += 2) l.Add((ham[i] << 8) | ham[i + 1]);
                else foreach (var c in ham) l.Add(c & 0xFF);
                return l;
            }

            public string Metin(int kod)
            {
                if (KodMetin.TryGetValue(kod, out var m)) return m;
                if (!IkiBayt && TekBayt != null) return TekBayt.GetString(new[] { (byte)kod });
                return "�";
            }

            public bool Kodla(char c, out string ham)
            {
                ham = null;
                if (MetinKod.TryGetValue(c, out var k)) { }
                else if (!IkiBayt && TekBayt != null && TekBayt.GetBytes(c.ToString()) is { Length: 1 } b && b[0] >= 32 && !KodMetin.ContainsKey(b[0])) k = b[0];
                else return false;
                ham = IkiBayt ? new string(new[] { (char)(k >> 8), (char)(k & 0xFF) }) : ((char)k).ToString();
                return true;
            }
        }

        private static Yazitipi YazitipiOku(PdfDictionary f)
        {
            var y = new Yazitipi { IkiBayt = f.Elements.GetName("/Subtype") == "/Type0" };
            if (!y.IkiBayt)
            {
                try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
                y.TekBayt = Encoding.GetEncoding(1254);
            }
            if (f.Elements.GetObject("/ToUnicode") is PdfDictionary tu && tu.Stream != null)
                CMapOku(Encoding.Latin1.GetString(tu.Stream.UnfilteredValue), y);
            foreach (var (k, m) in y.KodMetin)
                if (m.Length == 1 && !y.MetinKod.ContainsKey(m[0])) y.MetinKod[m[0]] = k;
            return y;
        }

        private static int Hex(string s) => Convert.ToInt32(s, 16);

        private static string HexMetin(string h)
        {
            var sb = new StringBuilder();
            if (h.Length <= 2) return ((char)Hex(h)).ToString();
            for (int i = 0; i + 4 <= h.Length; i += 4) sb.Append((char)Hex(h.Substring(i, 4)));
            return sb.ToString();
        }

        private static void CMapOku(string cmap, Yazitipi y)
        {
            foreach (Match blok in Regex.Matches(cmap, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
                foreach (Match m in Regex.Matches(blok.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                    y.KodMetin[Hex(m.Groups[1].Value)] = HexMetin(m.Groups[2].Value);
            foreach (Match blok in Regex.Matches(cmap, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
                foreach (Match m in Regex.Matches(blok.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(<([0-9A-Fa-f]+)>|\[([^\]]*)\])"))
                {
                    int a = Hex(m.Groups[1].Value), b = Hex(m.Groups[2].Value);
                    if (m.Groups[4].Success)
                    {
                        int u = Hex(m.Groups[4].Value);
                        for (int k = a; k <= b && k - a < 65536; k++) y.KodMetin[k] = ((char)(u + k - a)).ToString();
                    }
                    else
                    {
                        var liste = Regex.Matches(m.Groups[5].Value, @"<([0-9A-Fa-f]+)>");
                        for (int k = a; k <= b && k - a < liste.Count; k++) y.KodMetin[k] = HexMetin(liste[k - a].Groups[1].Value);
                    }
                }
        }

        // ------------------------------------------------------------ sayfa işleme

        /// <summary>Bir raporun sayfaları arasında taşınan durum: en son görülen "... OPSIYONLARI" başlığı.</summary>
        public sealed class Tarama
        {
            public string Blok = "";
            public bool KolonBlogu => Blok.Contains("KOLON", StringComparison.OrdinalIgnoreCase);
        }

        private static readonly Regex BlokDeseni = new(@"OPS.YON", RegexOptions.IgnoreCase);

        /// <summary>
        /// Sayfayı tarar; KOLON/PERDE opsiyon bloğundaki paspay satırını (gerekiyorsa) düzeltir.
        /// Kiriş ve temel bloklarındaki aynı başlıklı satırlara dokunulmaz.
        /// </summary>
        public static List<Sonuc> SayfaDuzelt(PdfPage sayfa, int sayfaNo, Tarama tarama, string hedef = Hedef)
        {
            var sonuclar = new List<Sonuc>();
            var kaynaklar = sayfa.Elements.GetDictionary("/Resources");
            var fontlar = kaynaklar?.Elements.GetDictionary("/Font");
            if (fontlar == null) return sonuclar;

            CSequence icerik;
            try { icerik = ContentReader.ReadContent(sayfa); }
            catch { return sonuclar; }   // okunamayan sayfa: dokunma

            var yazitipleri = new Dictionary<string, Yazitipi>();
            Yazitipi FontAl(CName ad)
            {
                if (!yazitipleri.TryGetValue(ad.Name, out var y))
                {
                    y = fontlar.Elements.GetObject(ad.Name) is PdfDictionary fd ? YazitipiOku(fd) : null;
                    yazitipleri[ad.Name] = y;
                }
                return y;
            }

            var ops = icerik.OfType<COperator>().ToList();
            Yazitipi aktif = null;
            bool degisti = false;

            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                if (op.OpCode.OpCodeName == OpCodeName.Tf && op.Operands.Count >= 1 && op.Operands[0] is CName ad) { aktif = FontAl(ad); continue; }
                if (!GosterimMi(op) || aktif == null || op.Operands.Count == 0) continue;

                var parcalar = Parcala(op, aktif);
                if (parcalar == null) continue;
                string metin = string.Concat(parcalar.Glifler.Select(g => g.Metin));
                var m = SatirDeseni.Match(metin);
                if (!m.Success)
                {
                    if (BlokDeseni.IsMatch(metin)) tarama.Blok = metin.Trim();
                    continue;
                }
                if (!tarama.KolonBlogu) continue;   // kiriş / temel paspayına dokunma

                var bu = new Sonuc { Sayfa = sayfaNo, HedefDeger = hedef };
                sonuclar.Add(bu);
                int esit = metin.IndexOf('=', m.Index);
                if (esit < 0) { bu.Hata = "satırda \"=\" bulunamadı, değiştirilmedi"; continue; }
                int karakter = esit + 1;
                while (karakter < metin.Length && char.IsWhiteSpace(metin[karakter])) karakter++;

                // Değerin bulunduğu yer: aynı komut, ya da (elle düzenlenmiş PDF'lerde) aynı satırdaki sonraki komutlar.
                var ayriKomutlar = new List<COperator>();
                string eski;
                int degerBas;
                if (karakter < metin.Length)
                {
                    degerBas = parcalar.GlifIndeksi(karakter);
                    eski = string.Concat(parcalar.Glifler.Skip(degerBas).Select(g => g.Metin));
                }
                else
                {
                    degerBas = parcalar.Glifler.Count;
                    var sb = new StringBuilder();
                    var f = aktif;
                    for (int j = i + 1; j < ops.Count; j++)
                    {
                        var o2 = ops[j];
                        var n = o2.OpCode.OpCodeName;
                        if (n == OpCodeName.Tf && o2.Operands.Count >= 1 && o2.Operands[0] is CName ad2) { f = FontAl(ad2); continue; }
                        if (SatirSonuMu(o2)) break;
                        if (!GosterimMi(o2) || f == null || o2.Operands.Count == 0) continue;
                        var p2 = Parcala(o2, f);
                        if (p2 == null) { ayriKomutlar = null; break; }
                        sb.Append(string.Concat(p2.Glifler.Select(g => g.Metin)));
                        ayriKomutlar.Add(o2);
                    }
                    if (ayriKomutlar == null || ayriKomutlar.Count == 0) { bu.Hata = "değer okunamadı, değiştirilmedi"; continue; }
                    eski = sb.ToString();
                }
                eski = Regex.Replace(eski.Trim(), @"\s+", " ");
                bu.Eski = eski;
                if (Normal(eski) == Normal(hedef)) continue;
                if (!Regex.IsMatch(eski, @"^[\d\s.,/]+$")) { bu.Hata = $"değer tanınmadı (\"{eski}\"), değiştirilmedi"; continue; }

                // Yeni değer satırın kendi yazı tipiyle yazılır; "=" ile değer arasında boşluk yoksa eklenir.
                string yazilacak = hedef;
                if (degerBas == parcalar.Glifler.Count && (degerBas == 0 || !string.IsNullOrWhiteSpace(parcalar.Glifler[^1].Metin)))
                    yazilacak = " " + hedef;
                var yeni = new List<string>();
                foreach (var c in yazilacak)
                {
                    if (!aktif.Kodla(c, out var ham)) { yeni = null; bu.Hata = $"yazı tipinde \"{c}\" karakteri yok, değiştirilmedi"; break; }
                    yeni.Add(ham);
                }
                if (yeni == null) continue;

                op.Operands.Clear();
                op.Operands.Add((CObject)parcalar.YenidenKur(degerBas, yeni, false));   // CObject: dizinin açılıp eklenmemesi için
                foreach (var o2 in ayriKomutlar)   // eski (elle eklenmiş) değer parçalarını boşalt
                {
                    bool dizi = o2.OpCode.OpCodeName == OpCodeName.TJ;
                    o2.Operands.Clear();
                    o2.Operands.Add(dizi ? (CObject)new CArray() : new CString { Value = "", CStringType = CStringType.HexString });
                }
                bu.Degisti = true;
                degisti = true;
            }

            if (degisti)
            {
                Tjdonustur(icerik);   // Tj ile kurulan dizilerin operatörü TJ olur
                byte[] yeniIcerik;
                try { yeniIcerik = IcerikYaz(icerik); }
                catch (Exception ex)
                {
                    foreach (var s in sonuclar.Where(s => s.Degisti)) { s.Degisti = false; s.Hata = $"sayfa içeriği yazılamadı ({ex.Message}), değiştirilmedi"; }
                    return sonuclar;
                }
                // Yeni tek içerik akışı (zlib/Flate) oluşturulup sayfaya bağlanır; eski akışlar dokunulmadan kalır.
                var ms = new MemoryStream();
                using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
                    z.Write(yeniIcerik);
                var belge = sayfa.Owner;
                var akis = new PdfDictionary(belge);
                akis.CreateStream(ms.ToArray());
                akis.Elements.SetName("/Filter", "/FlateDecode");
                belge.Internals.AddObject(akis);
                sayfa.Elements["/Contents"] = akis.Reference;
            }
            return sonuclar;
        }

        // ------------------------------------------------------------ içerik yazıcı
        // PDFsharp'ın yazıcısı bazı işaretli içerik (BDC/MP sözlükleri) ve dize türlerini yazamıyor;
        // okunan her öğe burada birebir karşılığıyla yazılır, tanınmayan öğede sayfa değiştirilmez.

        private static byte[] IcerikYaz(CSequence seq)
        {
            var sb = new StringBuilder();
            foreach (var o in seq)
            {
                if (o is not COperator op) throw new NotSupportedException("beklenmeyen içerik öğesi " + o.GetType().Name);
                if (op.Name == "TJ" && (op.Operands.Count != 1 || op.Operands[0] is not CArray)) throw new InvalidOperationException("TJ işleneni dizi değil");
                if (op.Name == "Tj" && (op.Operands.Count != 1 || op.Operands[0] is not CString)) throw new InvalidOperationException("Tj işleneni dize değil");
                foreach (var a in op.Operands) { OgeYaz(sb, a); sb.Append(' '); }
                sb.Append(op.Name).Append('\n');
            }
            var bayt = Encoding.Latin1.GetBytes(sb.ToString());

            // Güvenlik: yazılanı yeniden okuyup öğe öğe aynı olduğunu doğrula.
            var geri = ContentReader.ReadContent(bayt);
            if (!Esit(seq, geri))
            {
                string Goster(CObject o) { var t = new StringBuilder(); try { if (o is COperator op) { foreach (var a in op.Operands) { OgeYaz(t, a); t.Append(' '); } t.Append(op.Name); } else OgeYaz(t, o); } catch { t.Append(o?.GetType().Name); } return t.Length > 160 ? t.ToString(0, 160) : t.ToString(); }
                int i = 0;
                while (i < Math.Min(seq.Count, geri.Count) && Esit(seq[i], geri[i])) i++;
                throw new InvalidOperationException($"yeniden okuma farklı ({seq.Count}/{geri.Count} öğe, #{i}: " +
                    (i < seq.Count ? Goster(seq[i]) : "-") + " ≠ " + (i < geri.Count ? Goster(geri[i]) : "-") + ")");
            }
            return bayt;
        }

        private static bool Esit(CObject a, CObject b)
        {
            switch (a)
            {
                case COperator x when b is COperator y:
                    return x.Name == y.Name && Esit(x.Operands, y.Operands);
                case CArray x when b is CArray y:
                    return x.Count == y.Count && x.Zip(y).All(p => Esit(p.First, p.Second));
                case CSequence x when b is CSequence y && a is not CArray && b is not CArray:
                    return x.Count == y.Count && x.Zip(y).All(p => Esit(p.First, p.Second));
                case CNumber when b is CNumber: return Math.Abs(Sayi(a) - Sayi(b)) < 1e-9;
                case CName x when b is CName y: return x.Name == y.Name;
                case CString x when b is CString y: return x.Value == y.Value;
                default: return false;
            }
        }

        private static void OgeYaz(StringBuilder sb, CObject o)
        {
            switch (o)
            {
                case CInteger n: sb.Append(n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
                case CReal r:
                    var t = r.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                    if (t.Contains('E')) t = r.Value.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
                    sb.Append(t);
                    break;
                case CName ad: sb.Append(ad.Name); break;
                case CString s:
                    switch (s.CStringType)
                    {
                        case CStringType.Dictionary:
                            sb.Append(s.Value);
                            break;
                        case CStringType.UnicodeString:
                        case CStringType.UnicodeHexString:
                            sb.Append("<FEFF");
                            foreach (var c in s.Value) sb.Append(((int)c).ToString("X4"));
                            sb.Append('>');
                            break;
                        default:
                            sb.Append('<');
                            foreach (var c in s.Value)
                            {
                                if (c > 0xFF) throw new NotSupportedException("dize baytı aralık dışı");
                                sb.Append(((int)c).ToString("X2"));
                            }
                            sb.Append('>');
                            break;
                    }
                    break;
                case CArray a:
                    sb.Append('[');
                    foreach (var x in a) { OgeYaz(sb, x); sb.Append(' '); }
                    sb.Append(']');
                    break;
                default:
                    throw new NotSupportedException("beklenmeyen öğe " + o.GetType().Name);
            }
        }

        private static bool GosterimMi(COperator op) =>
            op.OpCode.OpCodeName is OpCodeName.Tj or OpCodeName.TJ;

        /// <summary>Satırın bittiğini gösteren komut: ET, Tm, T*, ' , " ya da dikey kaydırmalı Td/TD.</summary>
        private static bool SatirSonuMu(COperator op)
        {
            switch (op.OpCode.OpCodeName)
            {
                case OpCodeName.ET:
                case OpCodeName.Tm:
                case OpCodeName.Tx:
                case OpCodeName.QuoteSingle:
                case OpCodeName.BT:
                    return true;
                case OpCodeName.Td:
                case OpCodeName.TD:
                    return op.Operands.Count >= 2 && Math.Abs(Sayi(op.Operands[1])) > 0.01;
                default:
                    return op.Name == "\"";
            }
        }

        private static double Sayi(CObject o) => o switch
        {
            CReal r => r.Value,
            CInteger n => n.Value,
            _ => 0
        };


        private static string Normal(string s) => Regex.Replace(s.Replace('.', ','), @"\s+", "");

        private static void Tjdonustur(CSequence seq)
        {
            for (int i = 0; i < seq.Count; i++)
                if (seq[i] is COperator op && op.OpCode.OpCodeName == OpCodeName.Tj && op.Operands.Count == 1 && op.Operands[0] is CArray)
                {
                    var yeni = OpCodes.OperatorFromName("TJ");
                    yeni.Operands.Add((CObject)op.Operands[0]);
                    seq[i] = yeni;
                }
        }

        // ------------------------------------------------------------ TJ dizisi modeli

        private sealed class Glif
        {
            public string Ham;          // ham kod baytları
            public string Metin;
            public CObject Sonraki;     // bu glifin ardından gelen aralık sayısı (yoksa null)
            public CStringType Tur;
        }

        private sealed class Dizi
        {
            public readonly List<Glif> Glifler = new();
            public CObject Bastaki;      // ilk glif öncesi sayı (varsa)

            public int GlifIndeksi(int karakter)
            {
                int say = 0;
                for (int i = 0; i < Glifler.Count; i++)
                {
                    if (say >= karakter) return i;
                    say += Glifler[i].Metin.Length;
                }
                return Glifler.Count;
            }

            /// <summary>degerBas'tan sonrasını yeni gliflerle kurar; aralık düzeni eski gliflerden (2'li periyotla) sürdürülür.</summary>
            public CArray YenidenKur(int degerBas, List<string> yeni, bool tj)
            {
                // (glif, ardından gelen aralık) listesi; aralıksız art arda glifler sonra tek dizede birleştirilir.
                var liste = new List<(string Ham, CStringType Tur, CObject Aralik)>();
                var tur = Glifler.Count > 0 ? Glifler[Math.Min(degerBas, Glifler.Count - 1)].Tur : CStringType.HexString;
                for (int i = 0; i < degerBas; i++) liste.Add((Glifler[i].Ham, Glifler[i].Tur, Glifler[i].Sonraki));
                int sonEski = Glifler.Count - 1;
                for (int k = 0; k < yeni.Count; k++)
                {
                    bool son = k == yeni.Count - 1;
                    CObject aralik;
                    if (son) aralik = sonEski >= 0 && degerBas <= sonEski ? Glifler[sonEski].Sonraki : null;
                    else
                    {
                        int j = degerBas + k;
                        while (j >= sonEski && j - 2 >= 0) j -= 2;  // son glifin ardındaki (satır sonu) aralığı kullanma
                        aralik = j >= 0 && j < sonEski ? Glifler[j].Sonraki : null;
                        if (Math.Abs(Sayi(aralik)) > 100) aralik = null;   // boşluk atlama sıçraması
                    }
                    liste.Add((yeni[k], tur, aralik));
                }

                var dizi = new CArray();
                if (Bastaki != null) dizi.Add(Bastaki);
                var sb = new StringBuilder();
                CStringType sbTur = tur;
                foreach (var (ham, t, aralik) in liste)
                {
                    if (sb.Length == 0) sbTur = t;
                    sb.Append(ham);
                    if (aralik != null)
                    {
                        dizi.Add(new CString { Value = sb.ToString(), CStringType = sbTur });
                        sb.Clear();
                        dizi.Add(aralik);
                    }
                }
                if (sb.Length > 0) dizi.Add(new CString { Value = sb.ToString(), CStringType = sbTur });
                return dizi;
            }
        }

        private static Dizi Parcala(COperator op, Yazitipi y)
        {
            var d = new Dizi();
            IEnumerable<CObject> ogeler = op.Operands[0] is CArray a ? a : new[] { op.Operands[0] };
            foreach (var o in ogeler)
            {
                if (o is CString s)
                {
                    var kodlar = y.Kodlar(s.Value);
                    int adim = y.IkiBayt ? 2 : 1;
                    for (int i = 0; i < kodlar.Count; i++)
                        d.Glifler.Add(new Glif { Ham = s.Value.Substring(i * adim, adim), Metin = y.Metin(kodlar[i]), Tur = s.CStringType });
                }
                else if (o is CNumber)
                {
                    if (d.Glifler.Count == 0) d.Bastaki = o;
                    else if (d.Glifler[^1].Sonraki == null) d.Glifler[^1].Sonraki = o;
                    else return null; // ardışık sayılar: beklenmedik biçim
                }
                else return null;
            }
            return d.Glifler.Count == 0 ? null : d;
        }
    }
}
