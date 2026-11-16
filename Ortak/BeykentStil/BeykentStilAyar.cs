using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace BeykentOrtak
{
    /// <summary>
    /// BEYKENT katman / ölçü stili / yazı stili ayarları. Bu dosya ST4_Yardimci ve ST4_Plan_ID_Ciz
    /// projelerine link'li derlenir; iki DLL aynı JSON dosyasını okur.
    /// Öncelik: %APPDATA%\BEYKENT\katman-stil.json, yoksa DLL yanındaki katman-stil.json (ofis ortak), yoksa varsayılanlar.
    /// Kaydet her zaman %APPDATA% dosyasına yazar.
    /// </summary>
    [DataContract(Namespace = "")]
    internal sealed class KatmanAyar
    {
        [DataMember(Name = "Ad", Order = 0)] public string Ad;
        /// <summary>ACI renk (1..255). Rgb doluysa kullanılmaz.</summary>
        [DataMember(Name = "Aci", Order = 1)] public short Aci = 7;
        /// <summary>"R,G,B" (gerçek renk) ya da boş.</summary>
        [DataMember(Name = "Rgb", Order = 2, EmitDefaultValue = false)] public string Rgb;
        /// <summary>Kalınlık, 1/100 mm (20 = 0.20 mm). -3 varsayılan.</summary>
        [DataMember(Name = "Kalinlik", Order = 3)] public int Kalinlik = 20;
        /// <summary>Continuous, DASHED ...</summary>
        [DataMember(Name = "CizgiTipi", Order = 4)] public string CizgiTipi = "Continuous";
        /// <summary>Saydamlık yüzdesi 0..90 (0 = yok).</summary>
        [DataMember(Name = "Saydamlik", Order = 5)] public int Saydamlik;
        [DataMember(Name = "Cizdir", Order = 6)] public bool Cizdir = true;
        /// <summary>Yükle bu katmanı çizimde yoksa da oluşturur. Var olan katman her durumda güncellenir.</summary>
        [DataMember(Name = "Yukle", Order = 7)] public bool Yukle = true;
        /// <summary>Komutlar çalışırken var olan katmanın rengi de bu renge çekilir (ETRIYE / DONATI OKU).</summary>
        [DataMember(Name = "RenkZorla", Order = 8)] public bool RenkZorla;

        public bool GercekRenk => TryRgb(out _, out _, out _);

        public bool TryRgb(out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrWhiteSpace(Rgb)) return false;
            string[] p = Rgb.Split(',');
            return p.Length == 3 && byte.TryParse(p[0].Trim(), out r) && byte.TryParse(p[1].Trim(), out g) && byte.TryParse(p[2].Trim(), out b);
        }

        public bool Kesikli => !string.IsNullOrWhiteSpace(CizgiTipi) &&
            !string.Equals(CizgiTipi.Trim(), "Continuous", StringComparison.OrdinalIgnoreCase);

        public KatmanAyar Kopya() => (KatmanAyar)MemberwiseClone();

        /// <summary>DataContract yapıcı çağırmaz; elle yazılmış dosyada eksik alanlar varsayılan kalsın.</summary>
        [OnDeserializing]
        private void Ilk(StreamingContext c) { Aci = 7; Kalinlik = 20; CizgiTipi = "Continuous"; Cizdir = true; Yukle = true; }
    }

    [DataContract(Namespace = "")]
    internal sealed class OlcuAyar
    {
        [DataMember(Name = "Ad", Order = 0)] public string Ad;
        [DataMember(Name = "YaziYuksekligi", Order = 1)] public double YaziYuksekligi = 12;
        /// <summary>DIMLFAC (1/25 ölçülerde 0.5).</summary>
        [DataMember(Name = "Olcek", Order = 2)] public double Olcek = 1;
        [DataMember(Name = "OkBoyu", Order = 3)] public double OkBoyu = 3;
        /// <summary>true: eğik tik (DIMTSZ = ok boyu); false: dolu ok.</summary>
        [DataMember(Name = "Tik", Order = 4)] public bool Tik = true;
        /// <summary>DIMEXO, ölçülen noktadan uzatma çizgisi ofseti.</summary>
        [DataMember(Name = "Ofset", Order = 5)] public double Ofset = 3;
        /// <summary>DIMEXE, ölçü çizgisinden taşma.</summary>
        [DataMember(Name = "Uzanti", Order = 6)] public double Uzanti = 6;
        /// <summary>Sabit uzunluklu uzatma çizgisi (DIMFXLEN = Uzanti).</summary>
        [DataMember(Name = "SabitUzanti", Order = 7)] public bool SabitUzanti;
        [DataMember(Name = "Bosluk", Order = 8)] public double Bosluk = 2;
        [DataMember(Name = "Yuvarlama", Order = 9)] public double Yuvarlama = 0.5;
        [DataMember(Name = "Ondalik", Order = 10)] public short Ondalik = 1;
        /// <summary>DIMZIN (12: baştaki ve sondaki sıfırlar yazılmaz).</summary>
        [DataMember(Name = "SifirBastir", Order = 11)] public short SifirBastir = 12;
        /// <summary>true: yazı ölçü çizgisinin üstünde (DIMTAD 1).</summary>
        [DataMember(Name = "YaziUstte", Order = 12)] public bool YaziUstte = true;
        /// <summary>Uzatma çizgileri gizli (donatı oku).</summary>
        [DataMember(Name = "UzatmaGizle", Order = 13)] public bool UzatmaGizle;
        [DataMember(Name = "YaziStili", Order = 14)] public string YaziStili = BeykentStilAyar.YaziBeykent;
        /// <summary>DIMSCALE: 1/100 paftaya ×2 büyütülen detay stillerinde 2, diğerlerinde 1.</summary>
        [DataMember(Name = "Buyutme", Order = 15)] public double Buyutme = 1;

        public OlcuAyar Kopya() => (OlcuAyar)MemberwiseClone();

        [OnDeserializing]
        private void Ilk(StreamingContext c)
        {
            YaziYuksekligi = 12; Olcek = 1; OkBoyu = 3; Tik = true; Ofset = 3; Uzanti = 6; Bosluk = 2;
            Yuvarlama = 0.5; Ondalik = 1; SifirBastir = 12; YaziUstte = true; YaziStili = BeykentStilAyar.YaziBeykent;
            Buyutme = 1;
        }
    }

    [DataContract(Namespace = "")]
    internal sealed class YaziAyar
    {
        [DataMember(Name = "Ad", Order = 0)] public string Ad;
        /// <summary>TrueType yazı tipi adı ya da .shx dosyası.</summary>
        [DataMember(Name = "Font", Order = 1)] public string Font = "Bahnschrift Light Condensed";
        [DataMember(Name = "Genislik", Order = 2)] public double Genislik = 1;
        /// <summary>Eğik açı, derece.</summary>
        [DataMember(Name = "EgikAci", Order = 3)] public double EgikAci = 0;
        /// <summary>Sabit yazı yüksekliği; 0 = komut belirler.</summary>
        [DataMember(Name = "Yukseklik", Order = 4)] public double Yukseklik = 0;

        public YaziAyar Kopya() => (YaziAyar)MemberwiseClone();

        [OnDeserializing]
        private void Ilk(StreamingContext c) { Font = "Bahnschrift Light Condensed"; Genislik = 1; }
    }

    [DataContract(Namespace = "")]
    internal sealed class BeykentStilDosya
    {
        [DataMember(Name = "Surum", Order = 0)] public int Surum = 1;
        [DataMember(Name = "Katmanlar", Order = 1)] public List<KatmanAyar> Katmanlar = new List<KatmanAyar>();
        [DataMember(Name = "OlcuStilleri", Order = 2)] public List<OlcuAyar> OlcuStilleri = new List<OlcuAyar>();
        [DataMember(Name = "YaziStilleri", Order = 3)] public List<YaziAyar> YaziStilleri = new List<YaziAyar>();
    }

    internal static class BeykentStilAyar
    {
        internal const string YaziBeykent = "YAZI (BEYKENT)";
        internal const string DosyaAdi = "katman-stil.json";

        private static readonly object Kilit = new object();
        private static BeykentStilDosya _aktif;
        private static string _aktifYol;
        private static DateTime _aktifZaman;
        private static DateTime _sonKontrol;

        internal static string KullaniciYolu =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BEYKENT", DosyaAdi);

        internal static string OfisYolu
        {
            get
            {
                try
                {
                    string dir = Path.GetDirectoryName(typeof(BeykentStilAyar).Assembly.Location);
                    return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, DosyaAdi);
                }
                catch { return null; }
            }
        }

        /// <summary>Okunan dosyanın yolu; yoksa null (varsayılanlar).</summary>
        internal static string AktifYol { get { Aktif(); return _aktifYol; } }

        /// <summary>Geçerli ayarlar (dosya değişince yeniden okunur; en fazla saniyede bir kontrol).</summary>
        internal static BeykentStilDosya Aktif()
        {
            lock (Kilit)
            {
                DateTime now = DateTime.UtcNow;
                if (_aktif != null && (now - _sonKontrol).TotalSeconds < 1) return _aktif;
                _sonKontrol = now;
                string yol = MevcutYol();
                DateTime zaman = DateTime.MinValue;
                if (yol != null) { try { zaman = File.GetLastWriteTimeUtc(yol); } catch { } }
                if (_aktif != null && string.Equals(yol, _aktifYol, StringComparison.OrdinalIgnoreCase) && zaman == _aktifZaman)
                    return _aktif;
                _aktif = Oku(yol);
                _aktifYol = _aktif != null && yol != null ? yol : null;
                _aktifZaman = zaman;
                if (_aktif == null) _aktif = Varsayilan();
                return _aktif;
            }
        }

        private static string MevcutYol()
        {
            try { if (File.Exists(KullaniciYolu)) return KullaniciYolu; } catch { }
            string ofis = OfisYolu;
            try { if (ofis != null && File.Exists(ofis)) return ofis; } catch { }
            return null;
        }

        /// <summary>Dosyayı okur ve varsayılanlarla tamamlar; okunamazsa null.</summary>
        private static BeykentStilDosya Oku(string yol)
        {
            if (yol == null) return null;
            try
            {
                BeykentStilDosya okunan;
                using (var fs = File.OpenRead(yol))
                    okunan = (BeykentStilDosya)new DataContractJsonSerializer(typeof(BeykentStilDosya)).ReadObject(fs);
                return Tamamla(okunan);
            }
            catch { return null; }
        }

        /// <summary>Varsayılan listeyi sırasıyla alır, dosyadaki aynı adlı kayıtlarla değiştirir; dosyadaki ek kayıtlar sona eklenir.</summary>
        internal static BeykentStilDosya Tamamla(BeykentStilDosya okunan)
        {
            BeykentStilDosya v = Varsayilan();
            if (okunan == null) return v;
            EskiAdlariTasi(okunan);
            v.Katmanlar = Birlestir(v.Katmanlar, okunan.Katmanlar, k => k.Ad);
            v.OlcuStilleri = Birlestir(v.OlcuStilleri, okunan.OlcuStilleri, o => o.Ad);
            Pafta100Turet(v.OlcuStilleri, okunan.OlcuStilleri);
            v.YaziStilleri = Birlestir(v.YaziStilleri, okunan.YaziStilleri, y => y.Ad);
            return v;
        }

        /// <summary>Eski ölçü/yazı stili adlarıyla kaydedilmiş ayarları yeni adlara çevirir (<see cref="OlcuStilAdlari"/>).</summary>
        private static void EskiAdlariTasi(BeykentStilDosya d)
        {
            if (d.OlcuStilleri != null)
                foreach (OlcuAyar o in d.OlcuStilleri)
                {
                    if (o == null) continue;
                    o.Ad = OlcuStilAdlari.Yeni(o.Ad);
                    o.YaziStili = OlcuStilAdlari.YeniYazi(o.YaziStili);
                    if (o.Buyutme <= 0) o.Buyutme = 1;
                }
            if (d.YaziStilleri != null)
                foreach (YaziAyar y in d.YaziStilleri)
                    if (y != null) y.Ad = OlcuStilAdlari.YeniYazi(y.Ad);
        }

        /// <summary>
        /// Dosyada kaydı olmayan 1/100 detay stilleri (… 100-25 / … 100-50) kullanıcının 1/50 kaynağından türetilir
        /// (aynı değerler, DIMLFAC yarı, DIMSCALE 2); böylece 1/50'de değiştirilen yazı/ok 1/100'e de geçer.
        /// </summary>
        private static void Pafta100Turet(List<OlcuAyar> liste, List<OlcuAyar> okunan)
        {
            var kayitli = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (okunan != null)
                foreach (OlcuAyar o in okunan)
                    if (o != null && !string.IsNullOrWhiteSpace(o.Ad)) kayitli.Add(o.Ad.Trim());
            for (int i = 0; i < liste.Count; i++)
            {
                OlcuAyar o = liste[i];
                if (o == null || kayitli.Contains(o.Ad)) continue;
                string kaynak = null;
                if (o.Ad.EndsWith(" 100-25" + OlcuStilAdlari.Ek, StringComparison.OrdinalIgnoreCase))
                    kaynak = o.Ad.Replace(" 100-25" + OlcuStilAdlari.Ek, " 50-25" + OlcuStilAdlari.Ek);
                else if (o.Ad.EndsWith(" 100-50" + OlcuStilAdlari.Ek, StringComparison.OrdinalIgnoreCase))
                    kaynak = o.Ad.Replace(" 100-50" + OlcuStilAdlari.Ek, " 50" + OlcuStilAdlari.Ek);
                if (kaynak == null || !kayitli.Contains(kaynak)) continue;
                OlcuAyar src = Bul(liste, kaynak, x => x.Ad);
                if (src != null) liste[i] = P100(o.Ad, src);
            }
        }

        private static List<T> Birlestir<T>(List<T> varsayilan, List<T> okunan, Func<T, string> ad) where T : class
        {
            if (okunan == null) return varsayilan;
            var map = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            foreach (T x in okunan)
                if (x != null && !string.IsNullOrWhiteSpace(ad(x))) map[ad(x).Trim()] = x;
            var sonuc = new List<T>();
            var kullanilan = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (T d in varsayilan)
            {
                string k = ad(d);
                sonuc.Add(map.TryGetValue(k, out T o) ? o : d);
                kullanilan.Add(k);
            }
            foreach (T x in okunan)
                if (x != null && !string.IsNullOrWhiteSpace(ad(x)) && kullanilan.Add(ad(x).Trim())) sonuc.Add(x);
            return sonuc;
        }

        internal static void Kaydet(BeykentStilDosya ayar)
        {
            string yol = KullaniciYolu;
            Directory.CreateDirectory(Path.GetDirectoryName(yol));
            string tmp = yol + ".tmp";
            using (var fs = File.Create(tmp))
            using (var w = JsonReaderWriterFactory.CreateJsonWriter(fs, new UTF8Encoding(false), false, true, "  "))
            {
                new DataContractJsonSerializer(typeof(BeykentStilDosya)).WriteObject(w, ayar);
                w.Flush();
            }
            if (File.Exists(yol)) File.Delete(yol);
            File.Move(tmp, yol);
            lock (Kilit) { _aktif = null; _sonKontrol = DateTime.MinValue; }
        }

        internal static KatmanAyar Katman(string ad)
        {
            if (string.IsNullOrWhiteSpace(ad)) return null;
            ad = ad.Trim();
            foreach (KatmanAyar k in Aktif().Katmanlar)
                if (string.Equals(k.Ad, ad, StringComparison.OrdinalIgnoreCase)) return k;
            return null;
        }

        internal static OlcuAyar Olcu(string ad) => Bul(Aktif().OlcuStilleri, OlcuStilAdlari.Yeni(ad), o => o.Ad);
        internal static OlcuAyar OlcuVarsayilan(string ad) => Bul(Varsayilan().OlcuStilleri, OlcuStilAdlari.Yeni(ad), o => o.Ad);
        internal static YaziAyar Yazi(string ad) => Bul(Aktif().YaziStilleri, OlcuStilAdlari.YeniYazi(ad), y => y.Ad);

        private static T Bul<T>(List<T> liste, string ad, Func<T, string> f) where T : class
        {
            if (string.IsNullOrWhiteSpace(ad)) return null;
            ad = ad.Trim();
            foreach (T x in liste)
                if (string.Equals(f(x), ad, StringComparison.OrdinalIgnoreCase)) return x;
            return null;
        }

        // ------------------------------------------------------------------ varsayılanlar (bugünkü değerler)

        private static KatmanAyar K(string ad, short aci, int lw, bool dashed = false, bool yukle = true) =>
            new KatmanAyar { Ad = ad, Aci = aci, Kalinlik = lw, CizgiTipi = dashed ? "DASHED" : "Continuous", Yukle = yukle };

        private static KatmanAyar KRgb(string ad, string rgb, int lw) =>
            new KatmanAyar { Ad = ad, Aci = 7, Rgb = rgb, Kalinlik = lw, CizgiTipi = "Continuous" };

        private static OlcuAyar O(string ad, double yazi, double ok = 3, double ofset = 3, double? uzanti = null,
            double gap = 2, double olcek = 1, bool sabit = false, double rnd = 0.5, short dec = 1, short zin = 12,
            string yaziStili = YaziBeykent) =>
            new OlcuAyar
            {
                Ad = ad, YaziYuksekligi = yazi, OkBoyu = ok, Ofset = ofset, Uzanti = uzanti ?? yazi / 2, Bosluk = gap,
                Olcek = olcek, SabitUzanti = sabit, Yuvarlama = rnd, Ondalik = dec, SifirBastir = zin, YaziStili = yaziStili
            };

        /// <summary>1/100 paftaya ×2 büyütülen detayın stili: kaynakla aynı değerler, DIMLFAC yarı, DIMSCALE 2.</summary>
        private static OlcuAyar P100(string ad, OlcuAyar kaynak)
        {
            OlcuAyar o = kaynak.Kopya();
            o.Ad = ad;
            o.Olcek = kaynak.Olcek / 2.0;
            o.Buyutme = 2;
            return o;
        }

        private static OlcuAyar Ad(OlcuAyar kaynak, string ad, double olcek)
        {
            OlcuAyar o = kaynak.Kopya();
            o.Ad = ad;
            o.Olcek = olcek;
            return o;
        }

        internal static BeykentStilDosya Varsayilan()
        {
            var d = new BeykentStilDosya();
            d.Katmanlar.AddRange(new[]
            {
                K("CIZGI (BEYKENT)", 1, 20),
                K("AKS CIZGISI (BEYKENT)", 252, 20, true),
                K("AKS BALONU (BEYKENT)", 7, 30),
                K("AKS YAZISI (BEYKENT)", 3, 20),
                K("AKS OLCU (BEYKENT)", 6, 18),
                K("KIRIS (BEYKENT)", 2, 30),
                K("KIRIS ISMI (BEYKENT)", 40, 20),
                K("BANT (BEYKENT)", 23, 25, true),
                K("MANTAR KIRIS ISMI (BEYKENT)", 30, 20),
                K("KOLON (BEYKENT)", 3, 40),
                K("KOLON ISMI (BEYKENT)", 91, 20),
                K("PERDE (BEYKENT)", 6, 40),
                K("PERDE ISMI (BEYKENT)", 240, 20),
                K("TARAMA (BEYKENT)", 8, 15),
                K("DOSEME SINIRI (BEYKENT)", 71, 30),
                K("DOSEME ISMI (BEYKENT)", 9, 20),
                K("DOSEME PAFTA (BEYKENT)", 3, 20),
                K("DOSEME (BEYKENT)", 2, 30),
                K("MERDIVEN (BEYKENT)", 5, 30),
                K("MERDIVEN YAZI (BEYKENT)", 2, 20),
                K("YAZI (BEYKENT)", 4, 20),
                K("TEMEL (BEYKENT)", 2, 40),
                K("TEMEL AMPATMAN (BEYKENT)", 21, 40),
                K("TEMEL HATILI (BEYKENT)", 230, 30),
                K("TEMEL HATILI ISMI (BEYKENT)", 243, 20),
                K("TEMEL ISMI (BEYKENT)", 40, 20),
                K("KAT SINIRI (BEYKENT)", 91, 25),
                K("KALIP BOSLUK (BEYKENT)", 30, 25),
                K("OLCU (BEYKENT)", 14, 20),
                K("DONATI (BEYKENT)", 4, 40),
                K("DONATI YAZISI (BEYKENT)", 3, 20),
                K("DONATI HALKASI (BEYKENT)", 2, 30, true),
                K("DONATI IZI (BEYKENT)", 8, 25),
                new KatmanAyar { Ad = "DONATI OKU (BEYKENT)", Aci = 150, Kalinlik = 20, RenkZorla = true },
                K("HASIR (BEYKENT)", 4, 35),
                K("HASIR CIZGI (BEYKENT)", 1, 20),
                K("HASIR YAZI (BEYKENT)", 3, 20),
                K("HASIR YONU (BEYKENT)", 5, 25),
                K("CIROZ (BEYKENT)", 140, 20),
                K("YUK YAZISI (BEYKENT)", 140, 20),
                K("KOT YAZI (BEYKENT)", 7, 20),
                K("KOT CIZGISI (BEYKENT)", 7, 20),
                K("KESIT (BEYKENT)", 151, 60),
                K("KESIT CIZGISI (BEYKENT)", 3, 50),
                K("KESIT ISMI (BEYKENT)", 6, 20, true),
                K("KESIT SINIRI (BEYKENT)", 241, 20, true),
                K("KESIT GORUNUS (BEYKENT)", 253, 20, true),
                K("D.GORUNUS (BEYKENT)", 8, 20, true),
                K("IZDUSUM (BEYKENT)", 253, 20, true),
                K("GROBETON (BEYKENT)", 70, 50),
                K("ZEMIN (BEYKENT)", 43, 50),
                new KatmanAyar { Ad = "BLOKAJ (BEYKENT)", Aci = 8, Kalinlik = 15, Saydamlik = 65 },
                K("ETIKET CIZGISI (BEYKENT)", 64, 15),
                // 07.11.2026 kullanıcı seçimi: açık mavi 151, 0.25; eski çizimlerde de renk güncellenir.
                new KatmanAyar { Ad = "IC ANTET (BEYKENT)", Aci = 151, Kalinlik = 25, RenkZorla = true },
                K("ANTET BASLIK 1 (BEYKENT)", 1, 20),
                K("KAPAMA PERDESI (BEYKENT)", 5, 30),
                K("KAPAMA PERDESI ISMI (BEYKENT)", 5, 20),
                K("POZ NO (BEYKENT)", 2, 20),
                K("POZ BALON (BEYKENT)", 4, 20),
                new KatmanAyar { Ad = "POZ TARAMA (BEYKENT)", Aci = 8, Kalinlik = 20, Cizdir = false },
                K("PARAPET (BEYKENT)", 5, 20),
                new KatmanAyar { Ad = "ETRIYE (BEYKENT)", Aci = 150, Kalinlik = 35, RenkZorla = true },
                KRgb("TEMEL ILAVE X (BEYKENT)", "196,72,72", 20),
                KRgb("TEMEL ILAVE Y (BEYKENT)", "64,112,176", 20),
                KRgb("TEMEL ILAVE KUTU X (BEYKENT)", "255,176,176", 20),
                KRgb("TEMEL ILAVE KUTU Y (BEYKENT)", "176,204,236", 20),
                // Yükle'de yalnız çizimde varsa güncellenir (eski KATMANYUKLE dışında bırakılanlar).
                K("BOS (BEYKENT)", 252, 20, yukle: false),
                K("BIRLESIK KATMAN (BEYKENT)", 8, 25, yukle: false),
                K("DOSEME GPR KIRMIZI UST (BEYKENT)", 1, 18, yukle: false),
                K("DOSEME GPR KIRMIZI ALT (BEYKENT)", 1, 18, yukle: false),
                K("DOSEME GPR MAVI UST (BEYKENT)", 5, 18, yukle: false),
                K("DOSEME GPR MAVI ALT (BEYKENT)", 5, 18, yukle: false),
                K("DONATI YAZISI KALIP GPR KIRMIZI (BEYKENT)", 1, 18, yukle: false),
                K("DONATI YAZISI KALIP GPR MAVI (BEYKENT)", 5, 18, yukle: false),
                K("KIRIS UZATMA ISARET (BEYKENT)", 1, 25, yukle: false),
                K("KIRIS UZATMA ISARET MAVI (BEYKENT)", 5, 25, yukle: false),
                K("GORUNUS ON (BEYKENT)", 150, 25, yukle: false),
                K("GORUNUS ORTA (BEYKENT)", 116, 20, yukle: false),
                K("GORUNUS ARKA (BEYKENT)", 251, 18, yukle: false),
                K("IC OLCEK (BEYKENT)", 8, 15, yukle: false),
                K("DOSEME HATTI (BEYKENT)", 71, 30, yukle: false),
                K("DOSEME HATTI 1 (BEYKENT)", 71, 30, yukle: false),
                K("DOSEME HATTI 2 (BEYKENT)", 170, 30, yukle: false),
                K("DOSEME HATTI 3 (BEYKENT)", 40, 30, yukle: false),
                K("DOSEME HATTI 4 (BEYKENT)", 30, 30, yukle: false),
                K("1-5 (BEYKENT)", 1, 20, yukle: false),
                K("1-5 BAGLANTI X (BEYKENT)", 5, 20, yukle: false),
                K("1-5 BAGLANTI Y (BEYKENT)", 1, 20, yukle: false),
                K("DTX (BEYKENT)", 5, 20, yukle: false),
                K("DTY (BEYKENT)", 1, 20, yukle: false),
                K("EKSEN (BEYKENT)", 3, 20, yukle: false),
                K("EKSEN X (BEYKENT)", 5, 20, yukle: false),
                K("EKSEN Y (BEYKENT)", 1, 20, yukle: false),
                K("DOSEME DONATI EKSEN X (BEYKENT)", 3, 25, yukle: false),
                K("DOSEME DONATI EKSEN Y (BEYKENT)", 6, 25, yukle: false),
                K("ALT DUZ ALAN (BEYKENT)", 6, 20, yukle: false),
            });

            OlcuAyar donati50 = O(OlcuStilAdlari.Donati50, 10);
            OlcuAyar etriye50 = O(OlcuStilAdlari.Etriye50, 10, uzanti: 0.18, rnd: 0.1, dec: 1, zin: 8, yaziStili: OlcuStilAdlari.YaziEtriye);
            OlcuAyar acilim25 = O(OlcuStilAdlari.Acilim50_25, 10, olcek: 0.5, uzanti: 1.25);
            OlcuAyar detay25 = O(OlcuStilAdlari.Detay50_25, 12, olcek: 0.5, uzanti: 5, sabit: true);
            OlcuAyar donati25 = O(OlcuStilAdlari.Donati50_25, 10, olcek: 0.5);
            OlcuAyar etriye25 = Ad(etriye50, OlcuStilAdlari.Etriye50_25, 0.5);
            d.OlcuStilleri.AddRange(new[]
            {
                // 1/50 plan ve 1/50 detaylar (×1)
                O(OlcuStilAdlari.Aks50, 12),
                O(OlcuStilAdlari.Plan50, 12),
                donati50,
                etriye50,
                O(OlcuStilAdlari.Ilave50, 10, rnd: 1, dec: 0),
                new OlcuAyar
                {
                    Ad = OlcuStilAdlari.DonatiOku50, YaziYuksekligi = 10, OkBoyu = 10, Tik = false, Ofset = 5, Uzanti = 5,
                    Bosluk = 2, Yuvarlama = 0, Ondalik = 0, YaziUstte = false, UzatmaGizle = true
                },
                // 1/100 plan (yazı, ok, ofset, boşluk ×1.5)
                O(OlcuStilAdlari.Aks100, 18, ok: 4.5, ofset: 4.5, gap: 3),
                O(OlcuStilAdlari.Plan100, 18, ok: 4.5, ofset: 4.5, gap: 3),
                O(OlcuStilAdlari.Ilave100, 15, ok: 4.5, ofset: 4.5, gap: 3, rnd: 1, dec: 0),
                new OlcuAyar
                {
                    Ad = OlcuStilAdlari.DonatiOku100, YaziYuksekligi = 15, OkBoyu = 15, Tik = false, Ofset = 7.5, Uzanti = 7.5,
                    Bosluk = 3, Yuvarlama = 0, Ondalik = 0, YaziUstte = false, UzatmaGizle = true
                },
                // 1/50 pafta, 1/25 detay (×2, DIMLFAC 0.5)
                acilim25,
                detay25,
                donati25,
                etriye25,
                // 1/100 pafta, 1/50 detay (×2, DIMLFAC 0.5, DIMSCALE 2)
                P100(OlcuStilAdlari.Donati100_50, donati50),
                P100(OlcuStilAdlari.Etriye100_50, etriye50),
                // 1/100 pafta, 1/25 detay (×4, DIMLFAC 0.25, DIMSCALE 2)
                P100(OlcuStilAdlari.Acilim100_25, acilim25),
                P100(OlcuStilAdlari.Detay100_25, detay25),
                P100(OlcuStilAdlari.Donati100_25, donati25),
                P100(OlcuStilAdlari.Etriye100_25, etriye25),
                // OLCU ISKELE 50-100 (DIMLFAC 2) ISKELEPLAN'ın kendi kurallarıyla kurulur; burada yok (davranış korunur).
            });

            d.YaziStilleri.AddRange(new[]
            {
                new YaziAyar { Ad = YaziBeykent },
                new YaziAyar { Ad = OlcuStilAdlari.YaziEtriye, Genislik = 0.7 },
            });
            return d;
        }
    }
}
