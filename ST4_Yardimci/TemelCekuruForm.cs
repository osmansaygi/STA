using System;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Beykent.Monolit.Girdi;

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

    /// <summary>
    /// Temel çukuru formu. Ortak girdi kuralları (07.11.2026); alan sırası, etiketleri, sınırları
    /// ve bileşenleri Temel Detayı formuyla birebir aynıdır (ayarlar ayrı tutulur).
    /// </summary>
    internal sealed class TemelCekuruForm : Beykent.Monolit.MonolitForm
    {
        private static TemelCekuruForm _instance;

        private readonly OlcuKutusu _ustKot;
        private readonly OlcuKutusu _aci;
        private readonly OlcuKutusu _temel;
        private readonly OlcuKutusu _hatil;
        private readonly OlcuKutusu _hatilGen;
        private readonly OlcuKutusu _doseme;
        private readonly OlcuKutusu _ust;
        private readonly OlcuKutusu _alt;
        private readonly OlcuKutusu _gen;
        private readonly OlcuKutusu _diger;
        private readonly OlcuKutusu _pas;
        private readonly DonatiKutusu _ustDonati;
        private readonly DonatiKutusu _altDonati;
        private readonly SegmentSecim _olcek;
        private readonly GirdiDenetci Denetci = new GirdiDenetci();
        private bool _starting;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("TEMELCEKURU")) return;
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new TemelCekuruForm();
                try { AcApp.ShowModelessDialog(_instance); }
                catch { _instance.Show(); }
            }
            else
            {
                _instance._starting = false;
                _instance.LoadFromSettings();
                _instance.Visible = true;
                _instance.BringToFront();
            }
            BaFormSession.Attach("TEMELCEKURU", _instance);
            BaFormSession.EndPick("TEMELCEKURU");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("TEMELCEKURU");
            if (_instance == null) return;
            try { _instance.Close(); } catch { }
            _instance = null;
        }

        public static void HideTemporarily()
        {
            if (_instance == null || _instance.IsDisposed) return;
            try
            {
                if (_instance.InvokeRequired)
                {
                    _instance.BeginInvoke(new Action(HideTemporarily));
                    return;
                }
                BaFormSession.BeginPick("TEMELCEKURU");
                _instance.Visible = false;
            }
            catch { }
        }

        private TemelCekuruForm()
        {
            TemelCekuruSettings.EnsureLoaded();
            Text = "Temel Çukuru";
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            KeyPreview = true;

            // Sınırlar eski NumericUpDown Min/Max ile aynı; varsayılanlar şablon (Template*) değerleri.
            _ustKot = Denetci.Ekle(new OlcuKutusu("Temel üst kotu", "cm", -5000, 5000, TemelCekuruSettings.TemplateTemelUstKotuCm) { AdimDegeri = 5 });
            _temel = Denetci.Ekle(new OlcuKutusu("Çukur derinliği", "cm", 1, 600, TemelCekuruSettings.TemplateCukurDerinligiCm) { AdimDegeri = 5 });
            _ust = Denetci.Ekle(new OlcuKutusu("Üst temel", "cm", 15, 200, TemelCekuruSettings.TemplateUstTemelYuksekligiCm) { AdimDegeri = 5 });
            _alt = Denetci.Ekle(new OlcuKutusu("Alt temel", "cm", 20, 200, TemelCekuruSettings.TemplateAltTemelYuksekligiCm) { AdimDegeri = 5 });
            _gen = Denetci.Ekle(new OlcuKutusu("İç açıklık", "cm", 40, 2000, TemelCekuruSettings.TemplateTemelGenisligiCm) { AdimDegeri = 5 });
            _diger = Denetci.Ekle(new OlcuKutusu("Çukur eni", "cm", 40, 2000, TemelCekuruSettings.TemplateTemelGenisligiCm) { AdimDegeri = 5 });
            _aci = Denetci.Ekle(new OlcuKutusu("Grobeton açısı", "°", 50, 89, TemelCekuruSettings.TemplateGrobetonAcisi));
            _hatil = Denetci.Ekle(new OlcuKutusu("Hatıl yüksekliği", "cm", 0, 250, TemelCekuruSettings.TemplateHatilYuksekligiCm) { AdimDegeri = 5 });
            _hatilGen = Denetci.Ekle(new OlcuKutusu("Hatıl genişliği", "cm", 15, 80, TemelCekuruSettings.TemplateHatilGenisligiCm) { AdimDegeri = 5 });
            _doseme = Denetci.Ekle(new OlcuKutusu("Subasman", "cm", 5, 40, TemelCekuruSettings.TemplateSubasmanYuksekligiCm));
            _pas = Denetci.Ekle(new OlcuKutusu("Paspayı", "cm", 2, 10, TemelCekuruSettings.TemplatePaspayiCm, 1, 0.5));
            // Çap listesi eskiden ø8–ø25, aralık 5–40 cm tam sayı.
            _ustDonati = Denetci.Ekle(new DonatiKutusu("Üst donatı", TemelCekuruSettings.TemplateUstCapMm, TemelCekuruSettings.TemplateUstAralikCm, 8, 25, 5, 40));
            _altDonati = Denetci.Ekle(new DonatiKutusu("Alt donatı", TemelCekuruSettings.TemplateAltCapMm, TemelCekuruSettings.TemplateAltAralikCm, 8, 25, 5, 40));
            _olcek = new SegmentSecim("1/50 pafta", "1/100 pafta (×2)") { Secili = BaPaftaOlcek.Yuz("temelcekuru") ? 1 : 0 };
            _olcek.SeciliDegisti += (s, e) => BaPaftaOlcek.Kaydet("temelcekuru", _olcek.Secili == 1);
            LoadFromSettings();

            Denetci.HataKurali(() => _pas.Deger * 2.0 + 2.0 > _alt.Deger
                ? "Paspayı, alt temel yüksekliğine sığmıyor" : null, _pas, _alt);
            Denetci.HataKurali(() => TamSayiDegil(_ustDonati.AralikCm) ? "Üst donatı aralığı tam sayı cm olmalı" : null, _ustDonati);
            Denetci.HataKurali(() => TamSayiDegil(_altDonati.AralikCm) ? "Alt donatı aralığı tam sayı cm olmalı" : null, _altDonati);

            var plan = new Button { Text = "Temel planından al" };
            plan.Click += (s, e) => OnPlan();
            var btn = new Button { Text = "Çiz" };
            btn.Click += (s, e) => OnCiz();
            var close = new Button { Text = "Kapat" };
            close.Click += (s, e) => Close();

            FormClosing += (s, e) => { try { PushSettings(); } catch { } };
            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                {
                    _instance = null;
                    BaFormSession.Release("TEMELCEKURU");
                }
            };

            try
            {
                Location = new Point(
                    Screen.PrimaryScreen.WorkingArea.Left + 80,
                    Screen.PrimaryScreen.WorkingArea.Top + 60);
            }
            catch
            {
                StartPosition = FormStartPosition.CenterScreen;
            }

            var ui = BeginCompact(390);
            ui.Section("Kesit")
                .Field("Temel üst kotu", _ustKot)
                .Field("Çukur derinliği", _temel)
                .Field("Üst temel", _ust)
                .Field("Alt temel", _alt)
                .Field("İç açıklık / çukur eni", _gen, _diger)
                .Field("Grobeton açısı", _aci);
            ui.Section("Hatıl ve döşeme")
                .Field("Hatıl yük. / genişlik", _hatil, _hatilGen)
                .Field("Subasman", _doseme);
            ui.Section("Donatı  (ø çap / aralık)")
                .Field("Paspayı", _pas)
                .Field("Üst", _ustDonati)
                .Field("Alt", _altDonati);
            ui.Section("Pafta ölçeği · detay 1/25").Control(_olcek);
            ui.Section(null).Control(Denetci.Serit, 44);
            ui.Footer(btn, close, plan);
            ui.Tip(_gen, "İki perde iç yüzü arası (kesit doğrultusu).");
            ui.Tip(_diger, "Diğer doğrultudaki çukur genişliği; ikinci kesit bununla çizilir.");
            ui.Finish();
            Denetci.Guncelle();
        }

        private static bool TamSayiDegil(double v) => Math.Abs(v - Math.Round(v)) > 1e-9;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            if ((keyData == Keys.Enter || keyData == Keys.Return) && !(ActiveControl is Button))
            {
                OnCiz();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void LoadFromSettings()
        {
            _ustKot.Deger = TemelCekuruSettings.TemelUstKotuCm;
            _aci.Deger = TemelCekuruSettings.GrobetonAcisi;
            _temel.Deger = TemelCekuruSettings.CukurDerinligiCm;
            _hatil.Deger = TemelCekuruSettings.HatilYuksekligiCm;
            _hatilGen.Deger = TemelCekuruSettings.HatilGenisligiCm;
            _doseme.Deger = TemelCekuruSettings.SubasmanYuksekligiCm;
            _ust.Deger = TemelCekuruSettings.UstTemelYuksekligiCm;
            _alt.Deger = TemelCekuruSettings.AltTemelYuksekligiCm;
            _gen.Deger = TemelCekuruSettings.TemelGenisligiCm;
            _diger.Deger = TemelCekuruSettings.DigerGenislikCm;
            _pas.Deger = TemelCekuruSettings.PaspayiCm;
            _ustDonati.Ayarla(TemelCekuruSettings.UstCapMm, TemelCekuruSettings.UstAralikCm);
            _altDonati.Ayarla(TemelCekuruSettings.AltCapMm, TemelCekuruSettings.AltAralikCm);
            Denetci.Guncelle();
        }

        /// <summary>Runner'ın okuduğu ayar alanları değişmeden doldurulur.</summary>
        private void PushSettings()
        {
            TemelCekuruSettings.TemelUstKotuCm = _ustKot.Deger;
            TemelCekuruSettings.GrobetonAcisi = _aci.Deger;
            TemelCekuruSettings.CukurDerinligiCm = _temel.Deger;
            TemelCekuruSettings.HatilYuksekligiCm = _hatil.Deger;
            TemelCekuruSettings.HatilGenisligiCm = _hatilGen.Deger;
            TemelCekuruSettings.SubasmanYuksekligiCm = _doseme.Deger;
            TemelCekuruSettings.UstTemelYuksekligiCm = _ust.Deger;
            TemelCekuruSettings.AltTemelYuksekligiCm = _alt.Deger;
            TemelCekuruSettings.TemelGenisligiCm = _gen.Deger;
            TemelCekuruSettings.DigerGenislikCm = _diger.Deger;
            TemelCekuruSettings.PaspayiCm = _pas.Deger;
            TemelCekuruSettings.UstCapMm = _ustDonati.CapMm;
            TemelCekuruSettings.UstAralikCm = (int)Math.Round(_ustDonati.AralikCm);
            TemelCekuruSettings.AltCapMm = _altDonati.CapMm;
            TemelCekuruSettings.AltAralikCm = (int)Math.Round(_altDonati.AralikCm);
            TemelCekuruSettings.Save();
        }

        private void OnPlan()
        {
            if (_starting) return;
            if (!Denetci.Onayla()) return;
            PushSettings();
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                _starting = true;
                HideTemporarily();
                doc.SendStringToExecute("TEMELCEKURUPLAN ", true, false, false);
            }
            catch
            {
                _starting = false;
            }
        }

        private void OnCiz()
        {
            if (_starting) return;
            if (!Denetci.Onayla()) return;
            PushSettings();
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                _starting = true;
                HideTemporarily();
                doc.SendStringToExecute("TEMELCEKURUCIZ ", true, false, false);
            }
            catch
            {
                _starting = false;
            }
        }
    }
}
