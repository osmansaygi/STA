using System;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    /// <summary>
    /// Temel çukuru. Şablondaki CIZGI (BEYKENT) açıklamaları form alanıdır.
    /// Varsayılanlar TEMEL CUKURU.dwg ölçülerinden.
    /// </summary>
    internal static class TemelCekuruSettings
    {
        public const double TemplateGrobetonAcisi = 80;
        public const double TemplateTemelYuksekligiCm = 100;
        /// <summary>Şablon: hatıl üstünden perde dibine 150 cm (50+100). Resimdeki toplam ölçü.</summary>
        public const double TemplateCukurDerinligiCm = 150;
        public const double TemplateHatilYuksekligiCm = 50;
        /// <summary>İç yüzden dışa hatıl genişliği. Şablonda 20 cm.</summary>
        public const double TemplateHatilGenisligiCm = 20;
        /// <summary>Subasman döşemesi. Şablonda hatıl üstünden 10 cm.</summary>
        public const double TemplateSubasmanYuksekligiCm = 10;
        public const double TemplateUstTemelYuksekligiCm = 40;
        public const double TemplateAltTemelYuksekligiCm = 40;
        /// <summary>İki perde iç yüzü arası. Şablonda 200 cm.</summary>
        public const double TemplateTemelGenisligiCm = 200;
        /// <summary>Şablondaki paspayı. Çizimde 3 cm.</summary>
        public const double TemplatePaspayiCm = 3;
        public const string TemplateUstDonati = "14\u00F816/20";
        public const string TemplateAltDonati = "16\u00F816/20";
        public const int TemplateUstCapMm = 16;
        public const int TemplateUstAralikCm = 20;
        public const int TemplateAltCapMm = 16;
        public const int TemplateAltAralikCm = 20;
        /// <summary>Temel üst kotu (cm). Şablonda -300 cm = -3.00 m (hatıl altı).</summary>
        public const double TemplateTemelUstKotuCm = -300;

        public static double GrobetonAcisi { get; set; } = TemplateGrobetonAcisi;
        /// <summary>Temel üstünden çukur tabanına derinlik (cm); hatıl hariç.</summary>
        public static double CukurDerinligiCm { get; set; } = TemplateCukurDerinligiCm;

        /// <summary>Temel üstünden çukur tabanına. Hatıl yüksekliğinden bağımsız.</summary>
        public static double StemCm
        {
            get
            {
                double s = CukurDerinligiCm;
                return s < 1 ? 1 : s;
            }
        }
        public static double HatilYuksekligiCm { get; set; } = TemplateHatilYuksekligiCm;
        public static double HatilGenisligiCm { get; set; } = TemplateHatilGenisligiCm;
        public static double SubasmanYuksekligiCm { get; set; } = TemplateSubasmanYuksekligiCm;
        public static double UstTemelYuksekligiCm { get; set; } = TemplateUstTemelYuksekligiCm;
        public static double AltTemelYuksekligiCm { get; set; } = TemplateAltTemelYuksekligiCm;
        public static double TemelGenisligiCm { get; set; } = TemplateTemelGenisligiCm;
        /// <summary>Diğer doğrultudaki çukur genişliği. İkinci kesit bununla çizilir.</summary>
        public static double DigerGenislikCm { get; set; } = TemplateTemelGenisligiCm;
        /// <summary>Paspayı (cm). Şablondaki çizim 3 cm.</summary>
        public static double PaspayiCm { get; set; } = 3;
        public static double TemelUstKotuCm { get; set; } = TemplateTemelUstKotuCm;
        public static int UstCapMm { get; set; } = TemplateUstCapMm;
        public static int UstAralikCm { get; set; } = TemplateUstAralikCm;
        public static int AltCapMm { get; set; } = TemplateAltCapMm;
        public static int AltAralikCm { get; set; } = TemplateAltAralikCm;

        static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var m = BaIni.Read("temelcekuru.ini");
            GrobetonAcisi = BaIni.GetD(m, "GrobetonAcisi", GrobetonAcisi);
            HatilYuksekligiCm = BaIni.GetD(m, "HatilYuksekligiCm", HatilYuksekligiCm);
            HatilGenisligiCm = BaIni.GetD(m, "HatilGenisligiCm", HatilGenisligiCm);
            SubasmanYuksekligiCm = BaIni.GetD(m, "SubasmanYuksekligiCm", SubasmanYuksekligiCm);
            if (m.ContainsKey("CukurDerinligiCm"))
                CukurDerinligiCm = BaIni.GetD(m, "CukurDerinligiCm", CukurDerinligiCm);
            else if (m.ContainsKey("TemelYuksekligiCm"))
                CukurDerinligiCm = BaIni.GetD(m, "TemelYuksekligiCm", TemplateTemelYuksekligiCm);
            UstTemelYuksekligiCm = BaIni.GetD(m, "UstTemelYuksekligiCm", UstTemelYuksekligiCm);
            AltTemelYuksekligiCm = BaIni.GetD(m, "AltTemelYuksekligiCm", AltTemelYuksekligiCm);
            TemelGenisligiCm = BaIni.GetD(m, "TemelGenisligiCm", TemelGenisligiCm);
            DigerGenislikCm = BaIni.GetD(m, "DigerGenislikCm", DigerGenislikCm);
            PaspayiCm = BaIni.GetD(m, "PaspayiCm", PaspayiCm);
            TemelUstKotuCm = BaIni.GetD(m, "TemelUstKotuCm", TemelUstKotuCm);
            UstCapMm = BaIni.GetI(m, "UstCapMm", UstCapMm);
            UstAralikCm = BaIni.GetI(m, "UstAralikCm", UstAralikCm);
            AltCapMm = BaIni.GetI(m, "AltCapMm", AltCapMm);
            AltAralikCm = BaIni.GetI(m, "AltAralikCm", AltAralikCm);
            if (!m.ContainsKey("UstCapMm"))
            {
                int cap = 0, ar = 0;
                if (TryParseCapSlash(BaIni.Get(m, "UstDonati", null), ref cap, ref ar))
                {
                    UstCapMm = cap;
                    UstAralikCm = ar;
                }
            }
            if (!m.ContainsKey("AltCapMm"))
            {
                int cap = 0, ar = 0;
                if (TryParseCapSlash(BaIni.Get(m, "AltDonati", null), ref cap, ref ar))
                {
                    AltCapMm = cap;
                    AltAralikCm = ar;
                }
            }
        }

        static bool TryParseCapSlash(string text, ref int cap, ref int aralik)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            Match match = Regex.Match(text, @"\u00F8\s*(\d+)\s*/\s*(\d+)");
            if (!match.Success) return false;
            cap = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            aralik = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            return cap > 0 && aralik > 0;
        }

        public static void Save()
        {
            EnsureLoaded();
            BaIni.Write("temelcekuru.ini",
                "GrobetonAcisi=" + BaIni.N(GrobetonAcisi),
                "CukurDerinligiCm=" + BaIni.N(CukurDerinligiCm),
                "HatilYuksekligiCm=" + BaIni.N(HatilYuksekligiCm),
                "HatilGenisligiCm=" + BaIni.N(HatilGenisligiCm),
                "SubasmanYuksekligiCm=" + BaIni.N(SubasmanYuksekligiCm),
                "UstTemelYuksekligiCm=" + BaIni.N(UstTemelYuksekligiCm),
                "AltTemelYuksekligiCm=" + BaIni.N(AltTemelYuksekligiCm),
                "TemelGenisligiCm=" + BaIni.N(TemelGenisligiCm),
                "DigerGenislikCm=" + BaIni.N(DigerGenislikCm),
                "PaspayiCm=" + BaIni.N(PaspayiCm),
                "TemelUstKotuCm=" + BaIni.N(TemelUstKotuCm),
                "UstCapMm=" + UstCapMm.ToString(CultureInfo.InvariantCulture),
                "UstAralikCm=" + UstAralikCm.ToString(CultureInfo.InvariantCulture),
                "AltCapMm=" + AltCapMm.ToString(CultureInfo.InvariantCulture),
                "AltAralikCm=" + AltAralikCm.ToString(CultureInfo.InvariantCulture));
        }

        public static string CapSlash(int capMm, int aralikCm)
        {
            return "\u00F8" + capMm.ToString(CultureInfo.InvariantCulture)
                + "/" + aralikCm.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Temel detayı çizimi bu değerleri ödünç alır; bitince geri yazılır.</summary>
        internal struct Snapshot
        {
            public double GrobetonAcisi, CukurDerinligiCm, HatilYuksekligiCm, HatilGenisligiCm;
            public double SubasmanYuksekligiCm, UstTemelYuksekligiCm, AltTemelYuksekligiCm;
            public double TemelGenisligiCm, DigerGenislikCm, PaspayiCm, TemelUstKotuCm;
            public int UstCapMm, UstAralikCm, AltCapMm, AltAralikCm;
        }

        public static Snapshot Capture()
        {
            EnsureLoaded();
            return new Snapshot
            {
                GrobetonAcisi = GrobetonAcisi,
                CukurDerinligiCm = CukurDerinligiCm,
                HatilYuksekligiCm = HatilYuksekligiCm,
                HatilGenisligiCm = HatilGenisligiCm,
                SubasmanYuksekligiCm = SubasmanYuksekligiCm,
                UstTemelYuksekligiCm = UstTemelYuksekligiCm,
                AltTemelYuksekligiCm = AltTemelYuksekligiCm,
                TemelGenisligiCm = TemelGenisligiCm,
                DigerGenislikCm = DigerGenislikCm,
                PaspayiCm = PaspayiCm,
                TemelUstKotuCm = TemelUstKotuCm,
                UstCapMm = UstCapMm,
                UstAralikCm = UstAralikCm,
                AltCapMm = AltCapMm,
                AltAralikCm = AltAralikCm
            };
        }

        public static void Restore(Snapshot s)
        {
            GrobetonAcisi = s.GrobetonAcisi;
            CukurDerinligiCm = s.CukurDerinligiCm;
            HatilYuksekligiCm = s.HatilYuksekligiCm;
            HatilGenisligiCm = s.HatilGenisligiCm;
            SubasmanYuksekligiCm = s.SubasmanYuksekligiCm;
            UstTemelYuksekligiCm = s.UstTemelYuksekligiCm;
            AltTemelYuksekligiCm = s.AltTemelYuksekligiCm;
            TemelGenisligiCm = s.TemelGenisligiCm;
            DigerGenislikCm = s.DigerGenislikCm;
            PaspayiCm = s.PaspayiCm;
            TemelUstKotuCm = s.TemelUstKotuCm;
            UstCapMm = s.UstCapMm;
            UstAralikCm = s.UstAralikCm;
            AltCapMm = s.AltCapMm;
            AltAralikCm = s.AltAralikCm;
        }
    }

    internal static class TemelCekuruForm { internal static void Shutdown() {} }
}
