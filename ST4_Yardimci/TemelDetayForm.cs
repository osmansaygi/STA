using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Beykent.Monolit.Girdi;

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

    /// <summary>
    /// Temel detayı formu. Ortak girdi kuralları (07.11.2026); alan sırası, etiketleri, sınırları
    /// ve bileşenleri Temel Çukuru formuyla birebir aynıdır (ayarlar ayrı tutulur).
    /// </summary>
    internal sealed class TemelDetayForm : Beykent.Monolit.MonolitForm
    {
        private static TemelDetayForm _instance;

        private readonly OlcuKutusu _ustKot;
        private readonly OlcuKutusu _aci;
        private readonly OlcuKutusu _sol;
        private readonly OlcuKutusu _sag;
        private readonly OlcuKutusu _hatil;
        private readonly OlcuKutusu _hatilGen;
        private readonly OlcuKutusu _doseme;
        private readonly OlcuKutusu _ust;
        private readonly OlcuKutusu _alt;
        private readonly OlcuKutusu _gen;
        private readonly OlcuKutusu _uzunluk;
        private readonly OlcuKutusu _ustSag;
        private readonly CheckBox _solKademe;
        private readonly CheckBox _sagKademe;
        private readonly OlcuKutusu _pas;
        private readonly DonatiKutusu _ustDonati;
        private readonly DonatiKutusu _altDonati;
        private readonly SegmentSecim _olcek;
        private readonly GirdiDenetci Denetci = new GirdiDenetci();
        private bool _starting;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("TEMELDETAY")) return;
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new TemelDetayForm();
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
            BaFormSession.Attach("TEMELDETAY", _instance);
            BaFormSession.EndPick("TEMELDETAY");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("TEMELDETAY");
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
                BaFormSession.BeginPick("TEMELDETAY");
                _instance.Visible = false;
            }
            catch { }
        }

        private TemelDetayForm()
        {
            TemelDetaySettings.EnsureLoaded();
            Text = "Temel Detayı";
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            KeyPreview = true;

            // Sınırlar eski NumericUpDown Min/Max ile aynı; varsayılanlar TemelDetaySettings ilk değerleri.
            _ustKot = Denetci.Ekle(new OlcuKutusu("Temel üst kotu", "cm", -5000, 5000, TemelCekuruSettings.TemplateTemelUstKotuCm) { AdimDegeri = 5 });
            _solKademe = new CheckBox { Text = "Sol kademe", Checked = TemelDetaySettings.SolKademe, AutoSize = true };
            _sagKademe = new CheckBox { Text = "Sağ kademe", Checked = TemelDetaySettings.SagKademe, AutoSize = true };
            _sol = Denetci.Ekle(new OlcuKutusu("Sol kademe yüksekliği", "cm", 1, 600, TemelCekuruSettings.TemplateCukurDerinligiCm) { AdimDegeri = 5 });
            _sag = Denetci.Ekle(new OlcuKutusu("Sağ kademe yüksekliği", "cm", 1, 600, TemelCekuruSettings.TemplateCukurDerinligiCm) { AdimDegeri = 5 });
            _ust = Denetci.Ekle(new OlcuKutusu("Sol üst temel", "cm", 15, 200, TemelCekuruSettings.TemplateUstTemelYuksekligiCm) { AdimDegeri = 5 });
            _ustSag = Denetci.Ekle(new OlcuKutusu("Sağ üst temel", "cm", 15, 200, TemelCekuruSettings.TemplateUstTemelYuksekligiCm) { AdimDegeri = 5 });
            _alt = Denetci.Ekle(new OlcuKutusu("Alt temel", "cm", 20, 200, TemelCekuruSettings.TemplateAltTemelYuksekligiCm) { AdimDegeri = 5 });
            _gen = Denetci.Ekle(new OlcuKutusu("İç açıklık", "cm", 40, 2000, TemelCekuruSettings.TemplateTemelGenisligiCm) { AdimDegeri = 5 });
            _uzunluk = Denetci.Ekle(new OlcuKutusu("Kademe uzunluğu", "cm", 20, 20000, 200) { AdimDegeri = 5 });
            _aci = Denetci.Ekle(new OlcuKutusu("Grobeton açısı", "°", 50, 89, TemelCekuruSettings.TemplateGrobetonAcisi));
            _hatil = Denetci.Ekle(new OlcuKutusu("Hatıl yüksekliği", "cm", 0, 250, TemelCekuruSettings.TemplateHatilYuksekligiCm) { AdimDegeri = 5 });
            _hatilGen = Denetci.Ekle(new OlcuKutusu("Hatıl genişliği", "cm", 15, 80, TemelCekuruSettings.TemplateHatilGenisligiCm) { AdimDegeri = 5 });
            _doseme = Denetci.Ekle(new OlcuKutusu("Subasman", "cm", 5, 40, TemelCekuruSettings.TemplateSubasmanYuksekligiCm));
            _pas = Denetci.Ekle(new OlcuKutusu("Paspayı", "cm", 2, 10, 3, 1, 0.5));
            // Çap listesi eskiden ø8–ø25, aralık 5–40 cm tam sayı.
            _ustDonati = Denetci.Ekle(new DonatiKutusu("Üst donatı", TemelCekuruSettings.TemplateUstCapMm, TemelCekuruSettings.TemplateUstAralikCm, 8, 25, 5, 40));
            _altDonati = Denetci.Ekle(new DonatiKutusu("Alt donatı", TemelCekuruSettings.TemplateAltCapMm, TemelCekuruSettings.TemplateAltAralikCm, 8, 25, 5, 40));
            _olcek = new SegmentSecim("1/50 pafta", "1/100 pafta (×2)") { Secili = BaPaftaOlcek.Yuz("temeldetay") ? 1 : 0 };
            _olcek.SeciliDegisti += (s, e) => BaPaftaOlcek.Kaydet("temeldetay", _olcek.Secili == 1);
            _solKademe.CheckedChanged += (s, e) => UpdateKademeControls();
            _sagKademe.CheckedChanged += (s, e) => UpdateKademeControls();
            LoadFromSettings();

            Denetci.HataKurali(() => _pas.Deger * 2.0 + 2.0 > _alt.Deger
                ? "Paspayı, alt temel yüksekliğine sığmıyor" : null, _pas, _alt);
            Denetci.HataKurali(() => !_solKademe.Checked && !_sagKademe.Checked
                ? "Sol veya sağ kademeden en az birini seçin" : null);
            Denetci.HataKurali(() => TamSayiDegil(_ustDonati.AralikCm) ? "Üst donatı aralığı tam sayı cm olmalı" : null, _ustDonati);
            Denetci.HataKurali(() => TamSayiDegil(_altDonati.AralikCm) ? "Alt donatı aralığı tam sayı cm olmalı" : null, _altDonati);

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
                    BaFormSession.Release("TEMELDETAY");
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
                .Options(_solKademe, _sagKademe)
                .Field("Sol / sağ kademe yük.", _sol, _sag)
                .Field("Sol / sağ üst temel", _ust, _ustSag)
                .Field("Alt temel", _alt)
                .Field("İç açıklık / kademe boyu", _gen, _uzunluk)
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
            ui.Footer(btn, close);
            ui.Tip(_ustKot, "Yüksek taraftaki temel üst kotu.");
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
            _ustKot.Deger = TemelDetaySettings.TemelUstKotuCm;
            _aci.Deger = TemelDetaySettings.GrobetonAcisi;
            _sol.Deger = TemelDetaySettings.SolCukurCm;
            _sag.Deger = TemelDetaySettings.SagCukurCm;
            _hatil.Deger = TemelDetaySettings.HatilYuksekligiCm;
            _hatilGen.Deger = TemelDetaySettings.HatilGenisligiCm;
            _doseme.Deger = TemelDetaySettings.SubasmanYuksekligiCm;
            _ust.Deger = TemelDetaySettings.UstTemelYuksekligiCm;
            _alt.Deger = TemelDetaySettings.AltTemelYuksekligiCm;
            _gen.Deger = TemelDetaySettings.TemelGenisligiCm;
            _uzunluk.Deger = TemelDetaySettings.KademeUzunluguCm;
            _ustSag.Deger = TemelDetaySettings.SagUstTemelYuksekligiCm;
            _solKademe.Checked = TemelDetaySettings.SolKademe;
            _sagKademe.Checked = TemelDetaySettings.SagKademe;
            _pas.Deger = TemelDetaySettings.PaspayiCm;
            _ustDonati.Ayarla(TemelDetaySettings.UstCapMm, TemelDetaySettings.UstAralikCm);
            _altDonati.Ayarla(TemelDetaySettings.AltCapMm, TemelDetaySettings.AltAralikCm);
            UpdateKademeControls();
        }

        /// <summary>Runner'ın okuduğu ayar alanları değişmeden doldurulur.</summary>
        private void PushSettings()
        {
            TemelDetaySettings.TemelUstKotuCm = _ustKot.Deger;
            TemelDetaySettings.GrobetonAcisi = _aci.Deger;
            TemelDetaySettings.SolCukurCm = _sol.Deger;
            TemelDetaySettings.SagCukurCm = _sag.Deger;
            TemelDetaySettings.HatilYuksekligiCm = _hatil.Deger;
            TemelDetaySettings.HatilGenisligiCm = _hatilGen.Deger;
            TemelDetaySettings.SubasmanYuksekligiCm = _doseme.Deger;
            TemelDetaySettings.UstTemelYuksekligiCm = _ust.Deger;
            TemelDetaySettings.AltTemelYuksekligiCm = _alt.Deger;
            TemelDetaySettings.TemelGenisligiCm = _gen.Deger;
            TemelDetaySettings.KademeUzunluguCm = _uzunluk.Deger;
            TemelDetaySettings.SagUstTemelYuksekligiCm = _ustSag.Deger;
            TemelDetaySettings.SolKademe = _solKademe.Checked;
            TemelDetaySettings.SagKademe = _sagKademe.Checked;
            TemelDetaySettings.PaspayiCm = _pas.Deger;
            TemelDetaySettings.UstCapMm = _ustDonati.CapMm;
            TemelDetaySettings.UstAralikCm = (int)Math.Round(_ustDonati.AralikCm);
            TemelDetaySettings.AltCapMm = _altDonati.CapMm;
            TemelDetaySettings.AltAralikCm = (int)Math.Round(_altDonati.AralikCm);
            TemelDetaySettings.Save();
        }

        /// <summary>Kapalı kademenin yükseklik ve üst temel alanları pasif (eski davranış).</summary>
        private void UpdateKademeControls()
        {
            _sol.Enabled = _ust.Enabled = _solKademe.Checked;
            _sag.Enabled = _ustSag.Enabled = _sagKademe.Checked;
            Denetci.Guncelle();
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
                doc.SendStringToExecute("TEMELDETAYCIZ ", true, false, false);
            }
            catch
            {
                _starting = false;
            }
        }
    }
}
