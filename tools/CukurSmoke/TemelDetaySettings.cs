using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    /// <summary>
    /// Temel detayı. Temel çukurunun tek kesit hali; sol/sağ kademe ve kalınlıklar ayrı.
    /// Tek kesit; ortak alt temel, bağımsız sol/sağ kademe ve üst temel kalınlıkları.
    /// </summary>
    internal static class TemelDetaySettings
    {
        public static double GrobetonAcisi { get; set; } = TemelCekuruSettings.TemplateGrobetonAcisi;
        public static double SolCukurCm { get; set; } = TemelCekuruSettings.TemplateCukurDerinligiCm;
        public static double SagCukurCm { get; set; } = TemelCekuruSettings.TemplateCukurDerinligiCm;
        public static double HatilYuksekligiCm { get; set; } = TemelCekuruSettings.TemplateHatilYuksekligiCm;
        public static double HatilGenisligiCm { get; set; } = TemelCekuruSettings.TemplateHatilGenisligiCm;
        public static double SubasmanYuksekligiCm { get; set; } = TemelCekuruSettings.TemplateSubasmanYuksekligiCm;
        public static double UstTemelYuksekligiCm { get; set; } = TemelCekuruSettings.TemplateUstTemelYuksekligiCm;
        public static double AltTemelYuksekligiCm { get; set; } = TemelCekuruSettings.TemplateAltTemelYuksekligiCm;
        public static double TemelGenisligiCm { get; set; } = TemelCekuruSettings.TemplateTemelGenisligiCm;
        public static double KademeUzunluguCm { get; set; } = 200;
        public static double SagUstTemelYuksekligiCm { get; set; } = TemelCekuruSettings.TemplateUstTemelYuksekligiCm;
        public static bool SolKademe { get; set; } = true;
        public static bool SagKademe { get; set; } = true;
        public static double PaspayiCm { get; set; } = 3;
        public static double TemelUstKotuCm { get; set; } = TemelCekuruSettings.TemplateTemelUstKotuCm;
        public static int UstCapMm { get; set; } = TemelCekuruSettings.TemplateUstCapMm;
        public static int UstAralikCm { get; set; } = TemelCekuruSettings.TemplateUstAralikCm;
        public static int AltCapMm { get; set; } = TemelCekuruSettings.TemplateAltCapMm;
        public static int AltAralikCm { get; set; } = TemelCekuruSettings.TemplateAltAralikCm;

        static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var m = BaIni.Read("temeldetay.ini");
            GrobetonAcisi = BaIni.GetD(m, "GrobetonAcisi", GrobetonAcisi);
            SolCukurCm = BaIni.GetD(m, "SolCukurCm", SolCukurCm);
            SagCukurCm = BaIni.GetD(m, "SagCukurCm", SagCukurCm);
            HatilYuksekligiCm = BaIni.GetD(m, "HatilYuksekligiCm", HatilYuksekligiCm);
            HatilGenisligiCm = BaIni.GetD(m, "HatilGenisligiCm", HatilGenisligiCm);
            SubasmanYuksekligiCm = BaIni.GetD(m, "SubasmanYuksekligiCm", SubasmanYuksekligiCm);
            UstTemelYuksekligiCm = BaIni.GetD(m, "UstTemelYuksekligiCm", UstTemelYuksekligiCm);
            AltTemelYuksekligiCm = BaIni.GetD(m, "AltTemelYuksekligiCm", AltTemelYuksekligiCm);
            TemelGenisligiCm = BaIni.GetD(m, "TemelGenisligiCm", TemelGenisligiCm);
            KademeUzunluguCm = BaIni.GetD(m, "KademeUzunluguCm", KademeUzunluguCm);
            SagUstTemelYuksekligiCm = BaIni.GetD(m, "SagUstTemelYuksekligiCm", UstTemelYuksekligiCm);
            SolKademe = BaIni.GetI(m, "SolKademe", 1) != 0;
            SagKademe = BaIni.GetI(m, "SagKademe", 1) != 0;
            PaspayiCm = BaIni.GetD(m, "PaspayiCm", PaspayiCm);
            TemelUstKotuCm = BaIni.GetD(m, "TemelUstKotuCm", TemelUstKotuCm);
            UstCapMm = BaIni.GetI(m, "UstCapMm", UstCapMm);
            UstAralikCm = BaIni.GetI(m, "UstAralikCm", UstAralikCm);
            AltCapMm = BaIni.GetI(m, "AltCapMm", AltCapMm);
            AltAralikCm = BaIni.GetI(m, "AltAralikCm", AltAralikCm);
        }

        public static void Save()
        {
            EnsureLoaded();
            BaIni.Write("temeldetay.ini",
                "GrobetonAcisi=" + BaIni.N(GrobetonAcisi),
                "SolCukurCm=" + BaIni.N(SolCukurCm),
                "SagCukurCm=" + BaIni.N(SagCukurCm),
                "HatilYuksekligiCm=" + BaIni.N(HatilYuksekligiCm),
                "HatilGenisligiCm=" + BaIni.N(HatilGenisligiCm),
                "SubasmanYuksekligiCm=" + BaIni.N(SubasmanYuksekligiCm),
                "UstTemelYuksekligiCm=" + BaIni.N(UstTemelYuksekligiCm),
                "AltTemelYuksekligiCm=" + BaIni.N(AltTemelYuksekligiCm),
                "TemelGenisligiCm=" + BaIni.N(TemelGenisligiCm),
                "KademeUzunluguCm=" + BaIni.N(KademeUzunluguCm),
                "SagUstTemelYuksekligiCm=" + BaIni.N(SagUstTemelYuksekligiCm),
                "SolKademe=" + (SolKademe ? "1" : "0"),
                "SagKademe=" + (SagKademe ? "1" : "0"),
                "PaspayiCm=" + BaIni.N(PaspayiCm),
                "TemelUstKotuCm=" + BaIni.N(TemelUstKotuCm),
                "UstCapMm=" + UstCapMm.ToString(CultureInfo.InvariantCulture),
                "UstAralikCm=" + UstAralikCm.ToString(CultureInfo.InvariantCulture),
                "AltCapMm=" + AltCapMm.ToString(CultureInfo.InvariantCulture),
                "AltAralikCm=" + AltAralikCm.ToString(CultureInfo.InvariantCulture));
        }
    }

    internal static class TemelDetayForm { internal static void Shutdown() {} }
}
