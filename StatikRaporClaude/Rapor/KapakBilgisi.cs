using System.Text.Json;

namespace StatikRaporClaude.Rapor
{
    /// <summary>Kapak alanları. Proje bazında %APPDATA%\StatikRaporClaude\kapak\ altında saklanır; müellif bilgisi ortak varsayılandır.</summary>
    public sealed class KapakBilgisi
    {
        public string RaporBasligi { get; set; } = "STATİK HESAPLAR";
        // Arsa
        public string Yeri { get; set; } = "";
        public string PaftaAdaParsel { get; set; } = "";
        public string Yuzolcumu { get; set; } = "";
        public string IhtivaEttigiYapi { get; set; } = "";
        // Bina
        public string Cinsi { get; set; } = "B.A.KARKAS";
        public string KatAdedi { get; set; } = "";
        public string InsaatAlani { get; set; } = "";
        public string KullanimAmaci { get; set; } = "";
        public string YapiSinifi { get; set; } = "";
        // Müellif
        public string MuellifAdi { get; set; } = "";
        public string Unvani { get; set; } = "İNŞAAT MÜHENDİSİ";
        public string OdaSicilNo { get; set; } = "";
        public string BuroTescilNo { get; set; } = "";
        // Taraflar
        public string YapiSahibi { get; set; } = "";
        public string YapiDenetim { get; set; } = "";

        private static readonly string Kok = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StatikRaporClaude");
        private static readonly JsonSerializerOptions Js = new JsonSerializerOptions { WriteIndented = true };

        private static string ProjeYolu(string st4)
        {
            string ad = string.Concat(Path.GetFullPath(st4).ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_'));
            return Path.Combine(Kok, "kapak", ad + ".json");
        }

        public static KapakBilgisi Yukle(string st4)
        {
            try
            {
                string y = ProjeYolu(st4);
                if (File.Exists(y)) return JsonSerializer.Deserialize<KapakBilgisi>(File.ReadAllText(y)) ?? new KapakBilgisi();
                string v = Path.Combine(Kok, "kapak-muellif.json");
                if (File.Exists(v)) return JsonSerializer.Deserialize<KapakBilgisi>(File.ReadAllText(v)) ?? new KapakBilgisi();
            }
            catch { }
            return new KapakBilgisi();
        }

        public void Kaydet(string st4)
        {
            Directory.CreateDirectory(Path.Combine(Kok, "kapak"));
            File.WriteAllText(ProjeYolu(st4), JsonSerializer.Serialize(this, Js));
            // müellif alanları sonraki yeni projeler için varsayılan
            var m = new KapakBilgisi { MuellifAdi = MuellifAdi, Unvani = Unvani, OdaSicilNo = OdaSicilNo, BuroTescilNo = BuroTescilNo, Cinsi = Cinsi };
            File.WriteAllText(Path.Combine(Kok, "kapak-muellif.json"), JsonSerializer.Serialize(m, Js));
        }

        /// <summary>Formda gösterilecek alanlar: (grup, etiket, özellik adı).</summary>
        public static readonly (string Grup, string Etiket, string Alan)[] Alanlar =
        {
            ("Rapor", "Kapak başlığı", nameof(RaporBasligi)),
            ("Arsa", "Yeri", nameof(Yeri)),
            ("Arsa", "Pafta - ada - parsel", nameof(PaftaAdaParsel)),
            ("Arsa", "Yüzölçümü", nameof(Yuzolcumu)),
            ("Arsa", "İhtiva ettiği yapı", nameof(IhtivaEttigiYapi)),
            ("Bina", "Cinsi", nameof(Cinsi)),
            ("Bina", "Kat adedi / yüksekliği", nameof(KatAdedi)),
            ("Bina", "Bina inşaat alanı", nameof(InsaatAlani)),
            ("Bina", "Kullanım amacı", nameof(KullanimAmaci)),
            ("Bina", "Yapı sınıfı / grubu", nameof(YapiSinifi)),
            ("Statik proje müellifi", "Adı soyadı", nameof(MuellifAdi)),
            ("Statik proje müellifi", "Ünvanı", nameof(Unvani)),
            ("Statik proje müellifi", "Oda sicil no", nameof(OdaSicilNo)),
            ("Statik proje müellifi", "Büro tescil no", nameof(BuroTescilNo)),
            ("Taraflar", "Yapı sahibi", nameof(YapiSahibi)),
            ("Taraflar", "Yapı denetim kuruluşu", nameof(YapiDenetim)),
        };

        public string Al(string alan) => (string)GetType().GetProperty(alan)!.GetValue(this) ?? "";
        public void Ver(string alan, string deger) => GetType().GetProperty(alan)!.SetValue(this, deger ?? "");
    }
}
