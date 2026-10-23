namespace ST4Yardimci
{
    /// <summary>
    /// Plan ID'ye bağlanan Yardımcı TemelHatiliRunner.cs için ayar köprüsü: Yardımcı TEMELHATILI formunun
    /// kaydettiği %LocalAppData%\ST4_Yardimci\temelhatili.ini okunur (yazılmaz). Ölçüler çağıran tarafça atanır.
    /// </summary>
    internal static class TemelHatiliSettings
    {
        public static double TemelYuksekligiCm { get; set; } = 40;
        public static double TemelUstKotuCm { get; set; } = -80;
        public static double DosemeKalinligiCm { get; set; } = 15;
        public static double HatilYuksekligiCm { get; set; } = 120;
        public static double HatilKalinligiCm { get; set; } = 20;
        public static double PaspayiCm { get; set; } = 3;
        public static double DikeyCapMm { get; set; } = 10;
        public static double DikeyAralikCm { get; set; } = 20;
        public static double YatayCapMm { get; set; } = 10;
        public static double YatayAralikCm { get; set; } = 20;

        /// <summary>Her çizimde ini yeniden okunur (Yardımcı formunda yapılan son değişiklik geçerli olsun).</summary>
        public static void Yukle()
        {
            var m = BaIni.Read("temelhatili.ini");
            TemelYuksekligiCm = BaIni.GetD(m, "TemelYuksekligiCm", 40);
            TemelUstKotuCm = BaIni.GetD(m, "TemelUstKotuCm", -80);
            DosemeKalinligiCm = BaIni.GetD(m, "DosemeKalinligiCm", 15);
            HatilYuksekligiCm = BaIni.GetD(m, "HatilYuksekligiCm", 120);
            HatilKalinligiCm = BaIni.GetD(m, "HatilKalinligiCm", 20);
            PaspayiCm = BaIni.GetD(m, "PaspayiCm", 3);
            DikeyCapMm = BaIni.GetD(m, "DikeyCapMm", 10);
            DikeyAralikCm = BaIni.GetD(m, "DikeyAralikCm", 20);
            YatayCapMm = BaIni.GetD(m, "YatayCapMm", 10);
            YatayAralikCm = BaIni.GetD(m, "YatayAralikCm", 20);
        }
    }

    /// <summary>Plan ID'de form yok; TemelHatiliRunner'ın form kapatma çağrısı boş.</summary>
    internal static class TemelHatiliForm
    {
        public static void Shutdown() { }
    }

    /// <summary>Yardımcı BeykentKatmanYukle.Ensure ile aynı (KATMANYUKLE formu/lisansı Plan ID'ye taşınmaz).</summary>
    internal static class BeykentKatmanYukle
    {
        internal static void Ensure(Autodesk.AutoCAD.DatabaseServices.Transaction tr, Autodesk.AutoCAD.DatabaseServices.Database db,
            string name, short fallbackAci = 7,
            Autodesk.AutoCAD.DatabaseServices.LineWeight fallbackWeight = Autodesk.AutoCAD.DatabaseServices.LineWeight.LineWeight020,
            bool fallbackDashed = false)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            BeykentOrtak.BeykentStilUygula.KatmanHazirla(tr, db, new BeykentOrtak.KatmanAyar
            {
                Ad = name.Trim(),
                Aci = fallbackAci,
                Kalinlik = (int)fallbackWeight,
                CizgiTipi = fallbackDashed ? "DASHED" : "Continuous"
            });
        }
    }
}
