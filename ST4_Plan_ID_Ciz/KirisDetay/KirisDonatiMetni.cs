using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ST4PlanIdCiz.KirisDetay
{
    /// <summary>
    /// Kiriş tablosundaki donatı yazısını motora çevirir.
    /// Örnek: 2ø12(mon.)+2ø12(göv.) / 2ø14(düz) / 1ø14(sol üst ila.) / ø8/20/9(etriye).
    /// Çap işareti ø (U+00F8), Ø, φ veya "fi" / "phi" olabilir. Türkçe harf ve ASCII karşılığı kabul edilir.
    /// Etriye sırası ideCAD kiriş tablosu ile aynıdır: çap / orta bölge aralığı (cm) / sıklaştırma aralığı (cm).
    /// Repoda bu metin üretilmez; kolon tablosundaki ø8/15/8 yalnız gösterimdir ve bölge sırasını tanımlamaz.
    /// Aralıklar cm'den mm'ye çevrilir ve kullanıcının verdiği değer olarak saklanır; motor bunları kısmaz.
    /// </summary>
    public static class KirisDonatiMetni
    {
        const char FiKucuk = '\u00F8';
        const char FiBuyuk = '\u00D8';

        static readonly Regex RxParca = new Regex(
            @"^\s*(?<adet>\d+)?\s*fi\s*(?<cap>\d+(?:[.,]\d+)?)(?:\s*/\s*(?<s1>\d+(?:[.,]\d+)?))?(?:\s*/\s*(?<s2>\d+(?:[.,]\d+)?))?\s*(?:\(\s*(?<tur>[^)]*?)\s*\))?\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static KirisDonatiMetinSonuc Ayikla(string metin)
        {
            var sonuc = new KirisDonatiMetinSonuc();
            sonuc.Donati = new KirisDonatiGirdisi();
            if (string.IsNullOrWhiteSpace(metin))
            {
                sonuc.Hatalar.Add("Donatı metni boş.");
                sonuc.Tamam = false;
                return sonuc;
            }

            string[] parcalar = metin.Replace('\r', '\n').Replace('\n', '+').Replace(';', '+').Split('+');
            bool etriyeGoruldu = false;
            for (int i = 0; i < parcalar.Length; i++)
            {
                string ham = parcalar[i] == null ? "" : parcalar[i].Trim();
                if (ham.Length == 0) continue;
                Parca p;
                string hata;
                if (!ParcaOku(ham, out p, out hata))
                {
                    sonuc.Hatalar.Add(hata);
                    continue;
                }
                Uygula(sonuc, p, ham, ref etriyeGoruldu);
            }

            if (!etriyeGoruldu)
            {
                sonuc.Notlar.Add("Metinde etriye yok. Çap 8 mm bırakıldı; aralık verilmediği için konstrüktif üretilecek.");
            }
            sonuc.Tamam = sonuc.Hatalar.Count == 0;
            return sonuc;
        }

        /// <summary>
        /// Tablo satırı. bCm ve dCm santimetredir (10 ile çarpılarak mm olur). Açıklık ve mesnetler mm.
        /// Ayar nesnesindeki etriye aralıkları, metinde varsa kullanıcının değeriyle doldurulur.
        /// </summary>
        public static KirisDetayGirdi TabloSatiri(
            string ad,
            double bCm,
            double dCm,
            string donatiMetni,
            double lnMm,
            KirisMesnet sol,
            KirisMesnet sag,
            KirisDetayAyarlari ayarlar)
        {
            var g = new KirisDetayGirdi();
            g.Ad = ad ?? "";
            g.BwMm = bCm * 10.0;
            g.HMm = dCm * 10.0;
            g.LnMm = lnMm;
            g.Sol = sol ?? new KirisMesnet { Tur = MesnetTuru.Kenar };
            g.Sag = sag ?? new KirisMesnet { Tur = MesnetTuru.Kenar };
            g.Ayarlar = ayarlar ?? new KirisDetayAyarlari();
            KirisDonatiMetinSonuc okunan = Ayikla(donatiMetni);
            g.Donati = okunan.Donati ?? new KirisDonatiGirdisi();
            if (okunan.OrtaAralikMm.HasValue)
                g.Ayarlar.KullaniciOrtaAralikMm = okunan.OrtaAralikMm;
            if (okunan.SarilmaAraligiMm.HasValue)
                g.Ayarlar.KullaniciSarilmaAraligiMm = okunan.SarilmaAraligiMm;
            g.OkumaHatalari = okunan.Hatalar;
            g.OkumaNotlari = okunan.Notlar;
            return g;
        }

        static void Uygula(KirisDonatiMetinSonuc sonuc, Parca p, string ham, ref bool etriyeGoruldu)
        {
            if (p.Tur == ParcaTuru.Etriye)
            {
                if (etriyeGoruldu)
                {
                    sonuc.Hatalar.Add("Etriye birden fazla yazılmış: " + ham);
                    return;
                }
                etriyeGoruldu = true;
                sonuc.Donati.EtriyeCapMm = p.Cap;
                if (p.Adet > 0)
                {
                    sonuc.Notlar.Add("Etriye satırındaki adet kol sayısı sayılmaz; kol sayısı kesit genişliğinden hesaplanır.");
                }
                if (!p.S1.HasValue)
                {
                    sonuc.Notlar.Add("Etriye aralığı yazılmadı. Aralık konstrüktif üretilecek, çap " + p.Cap.ToString("0.#", CultureInfo.InvariantCulture) + " mm.");
                    return;
                }
                double ortaMm = p.S1.Value * 10.0;
                double sarMm = p.S2.HasValue ? p.S2.Value * 10.0 : ortaMm;
                sonuc.OrtaAralikMm = ortaMm;
                sonuc.SarilmaAraligiMm = sarMm;
                if (!p.S2.HasValue)
                {
                    sonuc.Notlar.Add("Etriyede tek aralık var. Sarılma ve orta bölge aynı alındı: " + ortaMm.ToString("0.#", CultureInfo.InvariantCulture) + " mm.");
                }
                else
                {
                    sonuc.Notlar.Add(
                        "Etriye ø" + p.Cap.ToString("0.#", CultureInfo.InvariantCulture)
                        + "/" + p.S1.Value.ToString("0.#", CultureInfo.InvariantCulture)
                        + "/" + p.S2.Value.ToString("0.#", CultureInfo.InvariantCulture)
                        + " cm: orta bölge " + ortaMm.ToString("0.#", CultureInfo.InvariantCulture)
                        + " mm, sarılma (sıklaştırma) " + sarMm.ToString("0.#", CultureInfo.InvariantCulture)
                        + " mm. Sıra ideCAD kiriş tablosundaki gibi çap / orta / sıklaştırma.");
                }
                return;
            }

            int adet = p.Adet > 0 ? p.Adet : 1;
            if (p.Adet <= 0)
                sonuc.Notlar.Add(ham + " satırında adet yok, 1 alındı.");

            if (p.Tur == ParcaTuru.Govde)
            {
                if (sonuc.Donati.GovdeToplamAdet > 0 && Math.Abs(sonuc.Donati.Govde.CapMm - p.Cap) > 0.11)
                {
                    sonuc.Hatalar.Add("Gövde parçalarında çap farklı: " + ham);
                    return;
                }
                if (sonuc.Donati.GovdeToplamAdet == 0)
                    sonuc.Notlar.Add("Gövde yazısı toplam çubuk sayılır ve iki yüze bölünür (yüz başına değil).");
                sonuc.Donati.Govde.CapMm = p.Cap;
                sonuc.Donati.GovdeToplamAdet += adet;
                return;
            }

            DonatiGrubu grup = Grup(sonuc.Donati, p.Tur);
            if (grup == null)
            {
                sonuc.Hatalar.Add("Tür tanınmadı: " + ham);
                return;
            }
            if (grup.Var && Math.Abs(grup.CapMm - p.Cap) > 0.11)
            {
                sonuc.Hatalar.Add("Aynı türde farklı çap: " + ham);
                return;
            }
            if (grup.Var)
                sonuc.Notlar.Add(TurAdi(p.Tur) + " birden fazla parçada geldi, adet toplandı.");
            grup.Adet += adet;
            grup.CapMm = p.Cap;
        }

        static DonatiGrubu Grup(KirisDonatiGirdisi d, ParcaTuru tur)
        {
            if (tur == ParcaTuru.Montaj) return d.Montaj;
            if (tur == ParcaTuru.Duz) return d.AltDuz;
            if (tur == ParcaTuru.UstSol) return d.UstSolIlave;
            if (tur == ParcaTuru.UstSag) return d.UstSagIlave;
            if (tur == ParcaTuru.AltSol) return d.AltSolIlave;
            if (tur == ParcaTuru.AltSag) return d.AltSagIlave;
            return null;
        }

        static string TurAdi(ParcaTuru tur)
        {
            if (tur == ParcaTuru.Montaj) return "Montaj";
            if (tur == ParcaTuru.Duz) return "Alt düz";
            if (tur == ParcaTuru.UstSol) return "Sol üst ilave";
            if (tur == ParcaTuru.UstSag) return "Sağ üst ilave";
            if (tur == ParcaTuru.AltSol) return "Sol alt ilave";
            if (tur == ParcaTuru.AltSag) return "Sağ alt ilave";
            if (tur == ParcaTuru.Govde) return "Gövde";
            if (tur == ParcaTuru.Etriye) return "Etriye";
            return "Donatı";
        }

        static bool ParcaOku(string ham, out Parca p, out string hata)
        {
            p = null;
            hata = "";
            if (ham.IndexOf('(') >= 0 && ham.IndexOf(')') < 0)
            {
                hata = "Parantez kapanmamış: " + ham;
                return false;
            }
            string n = Norm(CapIsaretleriniFiYap(ham));
            n = n.Replace("phi", "fi");
            Match m = RxParca.Match(n);
            if (!m.Success)
            {
                hata = "Donatı parçası okunamadı: " + ham;
                return false;
            }
            p = new Parca();
            if (m.Groups["adet"].Success)
            {
                int adet;
                if (!int.TryParse(m.Groups["adet"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out adet) || adet <= 0)
                {
                    hata = "Adet geçersiz: " + ham;
                    return false;
                }
                p.Adet = adet;
            }
            double cap;
            if (!Sayi(m.Groups["cap"].Value, out cap) || cap <= 0)
            {
                hata = "Çap geçersiz: " + ham;
                return false;
            }
            p.Cap = cap;
            if (m.Groups["s1"].Success)
            {
                double s1;
                if (!Sayi(m.Groups["s1"].Value, out s1) || s1 <= 0)
                {
                    hata = "Etriye aralığı geçersiz: " + ham;
                    return false;
                }
                p.S1 = s1;
            }
            if (m.Groups["s2"].Success)
            {
                double s2;
                if (!Sayi(m.Groups["s2"].Value, out s2) || s2 <= 0)
                {
                    hata = "Etriye aralığı geçersiz: " + ham;
                    return false;
                }
                p.S2 = s2;
            }
            string turHam = m.Groups["tur"].Success ? m.Groups["tur"].Value : "";
            p.Tur = Sinifla(turHam);
            if (p.Tur == ParcaTuru.Bilinmiyor && p.S1.HasValue)
            {
                p.Tur = ParcaTuru.Etriye;
            }
            if (p.Tur == ParcaTuru.Bilinmiyor)
            {
                hata = "Donatı türü tanınmadı: " + ham;
                return false;
            }
            return true;
        }

        static ParcaTuru Sinifla(string tur)
        {
            string n = TurSade(tur);
            if (n.Length == 0) return ParcaTuru.Bilinmiyor;
            if (n.Contains("etri") || n.Contains("sargi") || n == "et") return ParcaTuru.Etriye;
            if (n.Contains("gov") || n.Contains("web") || n == "yan") return ParcaTuru.Govde;
            if (n.Contains("mon")) return ParcaTuru.Montaj;
            if (n.Contains("duz") || n == "dz") return ParcaTuru.Duz;

            bool ilave = n.Contains("ila") || n.Contains("ilave");
            bool sol = n.Contains("sol");
            bool sag = n.Contains("sag");
            bool ust = n.Contains("ust");
            bool alt = n.Contains("alt");
            if (!ust && !alt && (sol || sag) && ilave)
                ust = true;
            if (sol && ust && !alt) return ParcaTuru.UstSol;
            if (sag && ust && !alt) return ParcaTuru.UstSag;
            if (sol && alt && !ust) return ParcaTuru.AltSol;
            if (sag && alt && !ust) return ParcaTuru.AltSag;
            if (sol && ust && alt) return ParcaTuru.Bilinmiyor;
            return ParcaTuru.Bilinmiyor;
        }

        static string TurSade(string tur)
        {
            string n = Norm(tur ?? "");
            var b = new StringBuilder(n.Length);
            for (int i = 0; i < n.Length; i++)
            {
                char c = n[i];
                if (c == '.' || c == ',' || c == ':' || c == ';') continue;
                b.Append(c);
            }
            string s = b.ToString();
            while (s.IndexOf("  ", StringComparison.Ordinal) >= 0)
                s = s.Replace("  ", " ");
            return s.Trim();
        }

        static string CapIsaretleriniFiYap(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var b = new StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == FiKucuk || c == FiBuyuk || c == '\u03C6' || c == '\u03A6' || c == '\u2300' || c == '\u2205')
                    b.Append("fi");
                else
                    b.Append(c);
            }
            return b.ToString();
        }

        /// <summary>Küçük harf ve Türkçe harflerin ASCII karşılığı. Büyük I ve İ de i olur.</summary>
        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var b = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case 'İ':
                    case 'I':
                    case 'ı':
                    case 'i':
                        b.Append('i');
                        break;
                    case 'Ğ':
                    case 'ğ':
                        b.Append('g');
                        break;
                    case 'Ü':
                    case 'ü':
                        b.Append('u');
                        break;
                    case 'Ş':
                    case 'ş':
                        b.Append('s');
                        break;
                    case 'Ö':
                    case 'ö':
                        b.Append('o');
                        break;
                    case 'Ç':
                    case 'ç':
                        b.Append('c');
                        break;
                    default:
                        if (c >= 'A' && c <= 'Z') b.Append((char)(c + 32));
                        else b.Append(c);
                        break;
                }
            }
            return b.ToString();
        }

        static bool Sayi(string s, out double v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        enum ParcaTuru
        {
            Bilinmiyor,
            Montaj,
            Govde,
            Duz,
            UstSol,
            UstSag,
            AltSol,
            AltSag,
            Etriye
        }

        sealed class Parca
        {
            public int Adet;
            public double Cap;
            public double? S1;
            public double? S2;
            public ParcaTuru Tur;
        }
    }

    public sealed class KirisDonatiMetinSonuc
    {
        public bool Tamam { get; set; }
        public List<string> Hatalar { get; set; }
        public List<string> Notlar { get; set; }
        public KirisDonatiGirdisi Donati { get; set; }
        public double? OrtaAralikMm { get; set; }
        public double? SarilmaAraligiMm { get; set; }

        public KirisDonatiMetinSonuc()
        {
            Hatalar = new List<string>();
            Notlar = new List<string>();
            Tamam = true;
        }
    }
}
