using System.Globalization;
using System.Text;

namespace GesRaporu
{
    /// <summary>TS 498:2021 Çizelge 3 — karakteristik kar yükü Sk (kN/m²).</summary>
    public static class KarTablosu
    {
        public static readonly int[] Kotlar = { 200, 300, 400, 500, 600, 700, 800, 900, 1000 };
        public static readonly string[] KotEtiket = { "0-200", "300", "400", "500", "600", "700", "800", "900", "1000" };

        // [kot satırı, bölge-1]
        public static readonly double[,] Sk =
        {
            { 0.75, 0.75, 0.75, 0.75, 0.75, 0.75, 0.75, 0.80, 0.85 },
            { 0.75, 0.75, 0.75, 0.75, 0.75, 0.75, 0.75, 0.85, 0.90 },
            { 0.75, 0.75, 0.75, 0.75, 0.75, 0.75, 0.80, 0.90, 0.95 },
            { 0.75, 0.75, 0.75, 0.80, 0.80, 0.80, 0.85, 0.95, 1.00 },
            { 0.75, 0.75, 0.80, 0.85, 0.85, 0.85, 0.90, 1.00, 1.10 },
            { 0.75, 0.80, 0.85, 0.90, 0.90, 0.90, 0.95, 1.10, 1.20 },
            { 0.80, 0.90, 0.95, 0.95, 0.95, 0.95, 1.10, 1.20, 1.30 },
            { 0.80, 0.95, 1.05, 1.10, 1.10, 1.10, 1.25, 1.30, 1.40 },
            { 0.80, 1.05, 1.10, 1.20, 1.30, 1.35, 1.40, 1.50, 1.60 },
        };

        /// <summary>Rakımın tekabül ettiği satır (ara değerde bir üst satır, güvenli taraf).</summary>
        public static int Satir(double rakim)
        {
            for (int i = 0; i < Kotlar.Length; i++)
                if (rakim <= Kotlar[i] + 1e-9) return i;
            return Kotlar.Length - 1;
        }

        /// <summary>1000 m üstü artış katsayısı: 1500 m'ye kadar %10, üstü %15.</summary>
        public static double Artis(double rakim) => rakim <= 1000 ? 1.0 : rakim <= 1500 ? 1.10 : 1.15;

        public static double Hesapla(double rakim, int bolge) =>
            Sk[Satir(rakim), Math.Clamp(bolge, 1, 9) - 1] * Artis(rakim);
    }

    /// <summary>TS 498 Çizelge 4 — yüksekliğe bağlı rüzgar hızı ve emme basıncı.</summary>
    public static class RuzgarTablosu
    {
        public static readonly (string Aralik, double Hiz, double Q)[] Satirlar =
        {
            ("0 - 8", 28, 0.5), ("9 - 20", 36, 0.8), ("21 - 100", 42, 1.1), ("> 100", 46, 1.3),
        };

        public static int Satir(double h) => h <= 8 ? 0 : h <= 20 ? 1 : h <= 100 ? 2 : 3;
    }

    /// <summary>Trapez sac kaplama — aşık aralığına göre yük taşıma (kg/m², 3 açıklık L/200, Panelsan).</summary>
    public static class TrapezTablosu
    {
        public static readonly double[] Acikliklar = { 1.0, 1.5, 2.0, 2.5 };
        public static readonly double[] Kalinliklar = { 0.50, 0.60, 0.70 };

        public static readonly Dictionary<string, double[,]> Tablo = new()
        {
            ["Trapez 38/151"] = new double[,] { { 285, 270, 200, 130 }, { 465, 415, 265, 155 }, { 705, 555, 330, 195 } },
            ["Trapez 34/250"] = new double[,] { { 280, 265, 195, 125 }, { 460, 410, 260, 155 }, { 675, 550, 325, 190 } },
        };

        public const string Kullanici = "Kullanıcı tanımlı";

        public static IEnumerable<string> Tipler => Tablo.Keys.Append(Kullanici);

        /// <summary>Kalınlık için en yakın alt (güvenli) satır, açıklıkta doğrusal ara değer; tablo dışı NaN.</summary>
        public static double Kapasite(string tip, double kalinlik, double aciklik)
        {
            if (!Tablo.TryGetValue(tip, out var t)) return double.NaN;
            int r = -1;
            for (int i = 0; i < Kalinliklar.Length; i++) if (kalinlik >= Kalinliklar[i] - 1e-6) r = i;
            if (r < 0 || aciklik > Acikliklar[^1] + 1e-6) return double.NaN;
            // 1,00 m altı tablo dışı: 1,00 m değeri × (1,00/L) — eğilmede kapasite ~1/L² arttığından güvenli taraf
            if (aciklik <= Acikliklar[0]) return aciklik > 0.05 ? t[r, 0] * Acikliklar[0] / aciklik : t[r, 0];
            for (int j = 0; j < Acikliklar.Length - 1; j++)
            {
                if (aciklik <= Acikliklar[j + 1] + 1e-9)
                {
                    double u = (aciklik - Acikliklar[j]) / (Acikliklar[j + 1] - Acikliklar[j]);
                    return t[r, j] + u * (t[r, j + 1] - t[r, j]);
                }
            }
            return double.NaN;
        }
    }

    public sealed record PanelModeli(string Ad, double A, double B, double T, double Kg, double Wp);

    public static class PanelKatalogu
    {
        public static readonly PanelModeli[] Modeller =
        {
            new("CWT465-144MB", 2.095, 1.039, 40, 24.5, 465),
            new("CWT450-144MB", 2.095, 1.039, 40, 24.5, 450),
            new("CWT420-144MB", 2.095, 1.039, 40, 24.5, 420),
            new("1669x1002x35 (19 kg)", 1.669, 1.002, 35, 19, 300),
        };
        public const string Kullanici = "Kullanıcı tanımlı";
    }

    /// <summary>Alüminyum 6063-T6 ve Sigma 80x55x1.2 taşıyıcı profil sabitleri (örnek rapordan).</summary>
    public static class Aluminyum
    {
        public const string Alasim = "6063 / T6";
        public const double Gamma = 2.70;      // gr/cm³
        public const double Akma = 1700;       // kg/cm² (170 N/mm²)
        public const double E = 700000;        // kg/cm² (70 000 N/mm²)
        public const double SigmaEm = 1020;    // kg/cm² (102 N/mm²)

        public const string Profil = "Sigma 80x55x1.2";
        public const double ProfilG = 0.969;   // kg/m
        public const double Jx = 17.07, Jy = 12.5, Wx = 5.58, Wy = 3.12; // cm⁴, cm³
        public const double ProfilT = 1.2;     // mm
    }

    /// <summary>Bulon / vida emniyet gerilmeleri (kg/cm²).</summary>
    public static class Baglanti
    {
        public const double TauEm = 1120, SigmaLEm = 2400, SigmaZEm = 1120;   // bulon (örnek rapor)
        public const double VidaTauEm = 1400, VidaSigmaZEm = 1120, VidaEzilmeEm = 2800; // vida (örnek DOCX)
    }

    /// <summary>İl/ilçe → kar bölgesi ve mahalle → ilçe tabloları; kullanıcı girdikçe öğrenir.</summary>
    public static class BolgeDeposu
    {
        public static readonly string[] Iller =
        {
            "ADANA","ADIYAMAN","AFYONKARAHİSAR","AĞRI","AMASYA","ANKARA","ANTALYA","ARTVİN","AYDIN","BALIKESİR","BİLECİK","BİNGÖL",
            "BİTLİS","BOLU","BURDUR","BURSA","ÇANAKKALE","ÇANKIRI","ÇORUM","DENİZLİ","DİYARBAKIR","EDİRNE","ELAZIĞ","ERZİNCAN",
            "ERZURUM","ESKİŞEHİR","GAZİANTEP","GİRESUN","GÜMÜŞHANE","HAKKARİ","HATAY","ISPARTA","MERSİN","İSTANBUL","İZMİR","KARS",
            "KASTAMONU","KAYSERİ","KIRKLARELİ","KIRŞEHİR","KOCAELİ","KONYA","KÜTAHYA","MALATYA","MANİSA","KAHRAMANMARAŞ","MARDİN",
            "MUĞLA","MUŞ","NEVŞEHİR","NİĞDE","ORDU","RİZE","SAKARYA","SAMSUN","SİİRT","SİNOP","SİVAS","TEKİRDAĞ","TOKAT","TRABZON",
            "TUNCELİ","ŞANLIURFA","UŞAK","VAN","YOZGAT","ZONGULDAK","AKSARAY","BAYBURT","KARAMAN","KIRIKKALE","BATMAN","ŞIRNAK",
            "BARTIN","ARDAHAN","IĞDIR","YALOVA","KARABÜK","KİLİS","OSMANİYE","DÜZCE",
        };

        public static readonly string[] KonyaIlceleri =
        {
            "SELÇUKLU","MERAM","KARATAY","AHIRLI","AKÖREN","AKŞEHİR","ALTINEKİN","BEYŞEHİR","BOZKIR","CİHANBEYLİ","ÇELTİK",
            "ÇUMRA","DERBENT","DEREBUCAK","DOĞANHİSAR","EMİRGAZİ","EREĞLİ","GÜNEYSINIR","HADİM","HALKAPINAR","HÜYÜK","ILGIN",
            "KADINHANI","KARAPINAR","KULU","SARAYÖNÜ","SEYDİŞEHİR","TAŞKENT","TUZLUKÇU","YALIHÜYÜK","YUNAK",
        };

        private static readonly CultureInfo Tr = new("tr-TR");
        public static string B(string s) => (s ?? "").Trim().ToUpper(Tr);

        private static string BolgeYolu => Path.Combine(Depo.Klasor, "kar-bolgeleri.csv");
        private static string MahalleYolu => Path.Combine(Depo.Klasor, "mahalle-ilce.csv");

        private static void Tohumla()
        {
            if (!File.Exists(BolgeYolu))
                File.WriteAllText(BolgeYolu,
                    "# İL;İLÇE (* = tüm ilçeler);TS498:2021 KAR BÖLGESİ\n" +
                    "KONYA;*;7\n", Encoding.UTF8);
            if (!File.Exists(MahalleYolu))
                File.WriteAllText(MahalleYolu,
                    "# İL;MAHALLE;İLÇE\n" +
                    "KONYA;HOCACİHAN;SELÇUKLU\n", Encoding.UTF8);
        }

        private static List<string[]> Oku(string yol)
        {
            Tohumla();
            return File.ReadAllLines(yol, Encoding.UTF8)
                .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
                .Select(l => l.Split(';').Select(B).ToArray())
                .Where(p => p.Length >= 3).ToList();
        }

        public static int? Bolge(string il, string ilce)
        {
            var s = Oku(BolgeYolu);
            var tam = s.LastOrDefault(p => p[0] == B(il) && p[1] == B(ilce));
            var genel = s.LastOrDefault(p => p[0] == B(il) && p[1] == "*");
            var r = tam ?? genel;
            return r != null && int.TryParse(r[2], out int b) ? b : null;
        }

        public static string Ilce(string il, string mahalle)
        {
            var r = Oku(MahalleYolu).LastOrDefault(p => p[0] == B(il) && p[1] == B(mahalle));
            return r?[2];
        }

        public static IEnumerable<string> Ilceler(string il) =>
            B(il) == "KONYA" ? KonyaIlceleri : Oku(MahalleYolu).Where(p => p[0] == B(il)).Select(p => p[2]).Distinct();

        /// <summary>Kullanıcının girdiği bilgiyi tablolara ekler (aynısı varsa yazmaz).</summary>
        public static void Ogren(string il, string ilce, string mahalle, int bolge)
        {
            if (string.IsNullOrWhiteSpace(il)) return;
            try
            {
                Tohumla();
                if (!string.IsNullOrWhiteSpace(mahalle) && !string.IsNullOrWhiteSpace(ilce) && Ilce(il, mahalle) != B(ilce))
                    File.AppendAllText(MahalleYolu, $"{B(il)};{B(mahalle)};{B(ilce)}\n", Encoding.UTF8);
                if (Bolge(il, ilce) != bolge)
                    File.AppendAllText(BolgeYolu, $"{B(il)};{(string.IsNullOrWhiteSpace(ilce) ? "*" : B(ilce))};{bolge}\n", Encoding.UTF8);
            }
            catch { }
        }
    }
}
