using System.Text.Json;

namespace GesRaporu
{
    /// <summary>Rapor için tüm girdiler. DWG'den dolanlar kullanıcı tarafından düzeltilebilir.</summary>
    public sealed class Girdi
    {
        // --- Proje / kapak ---
        public string DwgYolu { get; set; } = "";
        public string CatiPlaniBasligi { get; set; } = "";
        public string YapiSahibi { get; set; } = "";
        public string Il { get; set; } = "";
        public string Ilce { get; set; } = "";
        public string Mahalle { get; set; } = "";
        public string Pafta { get; set; } = "";
        public string Ada { get; set; } = "";
        public string Parsel { get; set; } = "";
        public string Blok { get; set; } = "";
        public string YapiCinsi { get; set; } = "";
        public string KatAdedi { get; set; } = "";
        public string KullanimAmaci { get; set; } = "";
        public string Muellif { get; set; } = "";
        public string Hazirlayan { get; set; } = "BEYKENT PROJE MİM. MÜH. İNŞ. DAN. SAN. TİC. LTD. ŞTİ.";
        public string HazirlayanMuhendis { get; set; } = "";

        // --- Yük ---
        public double Rakim { get; set; } = 1000;          // m
        public int KarBolgesi { get; set; } = 7;            // TS498:2021 1..9
        public double BinaYuksekligi { get; set; } = 10;    // m (zeminden)
        public double RuzgarHiziKmSa { get; set; } = 130;   // km/sa (FV modül dayanımı)
        public double CatiEgimi { get; set; } = 10;         // derece

        // --- Panel ---
        public string PanelModeli { get; set; } = "CWT465-144MB";
        public double PanelA { get; set; } = 2.095;         // m uzun kenar
        public double PanelB { get; set; } = 1.039;         // m kısa kenar
        public double PanelT { get; set; } = 40;            // mm
        public double PanelAgirlik { get; set; } = 24.5;    // kg
        public double PanelGucWp { get; set; } = 465;
        public int PanelAdedi { get; set; } = 0;

        // --- Mevcut çatı kaplaması ---
        public string KaplamaTipi { get; set; } = "Trapez 38/151";
        public double SacKalinligi { get; set; } = 0.70;    // mm
        public double AsikAraligi { get; set; } = 1.00;     // m
        public double KaplamaAgirligi { get; set; } = 10;   // kg/m²
        public double KaplamaKapasitesi { get; set; } = 0;  // kg/m² (0 = tablodan)

        // --- Taşıyıcı / bağlantı ---
        public double BaglantiAgirligi { get; set; } = 5;   // kg/m
        public double ProfilMesnetAraligi { get; set; } = 0.30; // m
        public double BulonCapi { get; set; } = 8;          // mm
        public double BaglantiProfilKalinligi { get; set; } = 1.8; // mm
        public int MesnetBulonAdedi { get; set; } = 1;
        public double VidaCapi { get; set; } = 5;           // mm
        public int ProfilVidaAdedi { get; set; } = 2;

        public List<string> EkPdfler { get; set; } = new();

        public Girdi Kopya() => JsonSerializer.Deserialize<Girdi>(JsonSerializer.Serialize(this));
    }

    /// <summary>%APPDATA%\GesRaporu altında son girdiler ve öğrenilen tablolar.</summary>
    public static class Depo
    {
        public static string Klasor
        {
            get
            {
                var k = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GesRaporu");
                Directory.CreateDirectory(k);
                return k;
            }
        }

        private static string SonGirdiYolu => Path.Combine(Klasor, "son-girdi.json");
        private static readonly JsonSerializerOptions Js = new() { WriteIndented = true };

        public static Girdi SonGirdiyiOku()
        {
            try
            {
                if (File.Exists(SonGirdiYolu))
                    return JsonSerializer.Deserialize<Girdi>(File.ReadAllText(SonGirdiYolu)) ?? new Girdi();
            }
            catch { }
            return new Girdi();
        }

        public static void SonGirdiyiYaz(Girdi g)
        {
            try { File.WriteAllText(SonGirdiYolu, JsonSerializer.Serialize(g, Js)); } catch { }
        }

        /// <summary>Proje klasörüne dosya bırakmamak için DWG yolunun özetiyle AppData'da tutulur.</summary>
        public static string ProjeGirdiYolu(string dwg, string blok = "")
        {
            var b = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(dwg).ToUpperInvariant() + "|" + (blok ?? "").Trim().ToUpperInvariant()));
            var k = Path.Combine(Klasor, "projeler");
            Directory.CreateDirectory(k);
            return Path.Combine(k, Convert.ToHexString(b)[..16] + ".json");
        }

        public static Girdi ProjeGirdisiniOku(string dwg, string blok = "")
        {
            try
            {
                var y = ProjeGirdiYolu(dwg, blok);
                if (File.Exists(y)) return JsonSerializer.Deserialize<Girdi>(File.ReadAllText(y));
            }
            catch { }
            return null;
        }

        public static void ProjeGirdisiniYaz(Girdi g)
        {
            if (string.IsNullOrWhiteSpace(g.DwgYolu)) return;
            try { File.WriteAllText(ProjeGirdiYolu(g.DwgYolu, g.Blok), JsonSerializer.Serialize(g, Js)); } catch { }
        }
    }
}
