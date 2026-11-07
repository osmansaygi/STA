using System;
using System.Drawing;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Beykent.Monolit.Girdi;

namespace ST4Yardimci
{
    /// <summary>Temel üstü hatıl detayı — form açıkken runner buradan okur.</summary>
    internal static class TemelHatiliSettings
    {
        /// <summary>Temel yüksekliği (cm).</summary>
        public static double TemelYuksekligiCm { get; set; } = 40;
        /// <summary>Temel üst kotu (cm). Hatıl bunun üstüne oturur.</summary>
        public static double TemelUstKotuCm { get; set; } = -80;
        /// <summary>Hatıl üstü döşeme kalınlığı (cm). 0 ise döşeme bandı çizilmez.</summary>
        public static double DosemeKalinligiCm { get; set; } = 15;
        /// <summary>Temel üstü hatıl yüksekliği (cm) — döşeme üstüne kadar.</summary>
        public static double HatilYuksekligiCm { get; set; } = 120;
        /// <summary>Temel hatılı kalınlığı / genişliği (cm).</summary>
        public static double HatilKalinligiCm { get; set; } = 20;
        /// <summary>Paspayı (cm).</summary>
        public static double PaspayiCm { get; set; } = 3;
        /// <summary>Dikey donatı çapı (mm).</summary>
        public static double DikeyCapMm { get; set; } = 10;
        /// <summary>Dikey donatı aralığı (cm).</summary>
        public static double DikeyAralikCm { get; set; } = 20;
        /// <summary>Yatay donatı çapı (mm).</summary>
        public static double YatayCapMm { get; set; } = 10;
        /// <summary>Yatay donatı aralığı (cm).</summary>
        public static double YatayAralikCm { get; set; } = 20;

        static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var m = BaIni.Read("temelhatili.ini");
            TemelYuksekligiCm = BaIni.GetD(m, "TemelYuksekligiCm", TemelYuksekligiCm);
            TemelUstKotuCm = BaIni.GetD(m, "TemelUstKotuCm", TemelUstKotuCm);
            DosemeKalinligiCm = BaIni.GetD(m, "DosemeKalinligiCm", DosemeKalinligiCm);
            HatilYuksekligiCm = BaIni.GetD(m, "HatilYuksekligiCm", HatilYuksekligiCm);
            HatilKalinligiCm = BaIni.GetD(m, "HatilKalinligiCm", HatilKalinligiCm);
            PaspayiCm = BaIni.GetD(m, "PaspayiCm", PaspayiCm);
            DikeyCapMm = BaIni.GetD(m, "DikeyCapMm", DikeyCapMm);
            DikeyAralikCm = BaIni.GetD(m, "DikeyAralikCm", DikeyAralikCm);
            YatayCapMm = BaIni.GetD(m, "YatayCapMm", YatayCapMm);
            YatayAralikCm = BaIni.GetD(m, "YatayAralikCm", YatayAralikCm);
        }

        public static void Save()
        {
            EnsureLoaded();
            BaIni.Write("temelhatili.ini",
                "TemelYuksekligiCm=" + BaIni.N(TemelYuksekligiCm),
                "TemelUstKotuCm=" + BaIni.N(TemelUstKotuCm),
                "DosemeKalinligiCm=" + BaIni.N(DosemeKalinligiCm),
                "HatilYuksekligiCm=" + BaIni.N(HatilYuksekligiCm),
                "HatilKalinligiCm=" + BaIni.N(HatilKalinligiCm),
                "PaspayiCm=" + BaIni.N(PaspayiCm),
                "DikeyCapMm=" + BaIni.N(DikeyCapMm),
                "DikeyAralikCm=" + BaIni.N(DikeyAralikCm),
                "YatayCapMm=" + BaIni.N(YatayCapMm),
                "YatayAralikCm=" + BaIni.N(YatayAralikCm));
        }
    }

    /// <summary>
    /// Temel Hatılı formu — plandan çiz / manuel çiz. Ortak girdi kuralları (07.11.2026):
    /// birimli ölçü kutusu, tek satır donatı (ø sabit), segment pafta seçimi, alt durum şeridi.
    /// </summary>
    internal sealed class TemelHatiliForm : Beykent.Monolit.MonolitForm
    {
        private static TemelHatiliForm _instance;

        internal readonly OlcuKutusu TemelH, Kot, Doseme, HatilH, HatilW, Pas;
        internal readonly DonatiKutusu Dusey, Yatay;
        internal readonly SegmentSecim Olcek;
        internal readonly GirdiDenetci Denetci = new GirdiDenetci();
        internal readonly Button BtnManuel, BtnPlan, BtnKapat;
        private bool _starting;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("TEMELHATILI")) return;
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new TemelHatiliForm();
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
            BaFormSession.Attach("TEMELHATILI", _instance);
            BaFormSession.EndPick("TEMELHATILI");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("TEMELHATILI");
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
                BaFormSession.BeginPick("TEMELHATILI");
                _instance.Visible = false;
            }
            catch { }
        }

        private TemelHatiliForm()
        {
            TemelHatiliSettings.EnsureLoaded();
            Text = "Temel Hatılı";
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            KeyPreview = true;

            // Varsayılanlar TemelHatiliSettings ilk değerleriyle aynı ("Varsayılana dön").
            TemelH = Denetci.Ekle(new OlcuKutusu("Temel yüksekliği", "cm", 20, 400, 40) { AdimDegeri = 5 });
            Kot = Denetci.Ekle(new OlcuKutusu("Temel üst kotu", "cm", -8000, 8000, -80) { AdimDegeri = 5 });
            Doseme = Denetci.Ekle(new OlcuKutusu("Döşeme kalınlığı", "cm", 0, 80, 15));
            HatilH = Denetci.Ekle(new OlcuKutusu("Hatıl yüksekliği", "cm", 20, 400, 120) { AdimDegeri = 5 });
            HatilW = Denetci.Ekle(new OlcuKutusu("Hatıl kalınlığı", "cm", 10, 80, 20) { AdimDegeri = 5 });
            Pas = Denetci.Ekle(new OlcuKutusu("Paspayı", "cm", 2, 20, 3, 1, 0.5));
            Dusey = Denetci.Ekle(new DonatiKutusu("Düşey donatı", 10, 20));
            Yatay = Denetci.Ekle(new DonatiKutusu("Yatay donatı", 10, 20));
            Olcek = new SegmentSecim("1/50 pafta", "1/100 pafta (×2)") { Secili = BaPaftaOlcek.Yuz("temelhatili") ? 1 : 0 };
            Olcek.SeciliDegisti += (s, e) => BaPaftaOlcek.Kaydet("temelhatili", Olcek.Secili == 1);
            LoadFromSettings();

            Denetci.HataKurali(() => HatilH.Deger <= Doseme.Deger
                ? "Hatıl yüksekliği döşeme kalınlığından büyük olmalı" : null, HatilH, Doseme);
            Denetci.HataKurali(() =>
            {
                double gerekli = 2 * Pas.Deger + 2 * (Dusey.CapMm + Yatay.CapMm) / 10.0;
                return gerekli >= HatilW.Deger
                    ? "Donatı sığmıyor: 2·paspayı + 2·(øD+øY) = " + GirdiTema.Yaz(gerekli, 1) + " cm ≥ hatıl kalınlığı" : null;
            }, HatilW, Pas, Dusey, Yatay);
            Denetci.UyariKurali(() => AralikUyarisi("Düşey", Dusey.AralikCm));
            Denetci.UyariKurali(() => AralikUyarisi("Yatay", Yatay.AralikCm));

            BtnPlan = new Button { Text = "Plandan çiz" };
            BtnManuel = new Button { Text = "Manuel çiz" };
            BtnKapat = new Button { Text = "Kapat" };
            BtnPlan.Click += (s, e) => Calistir("TEMELHATILIPLAN ");
            BtnManuel.Click += (s, e) => Calistir("TEMELHATILICIZ ");
            BtnKapat.Click += (s, e) => Close();

            FormClosing += (s, e) => { try { if (Denetci.Uygun) PushSettings(); } catch { } };
            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                {
                    _instance = null;
                    BaFormSession.Release("TEMELHATILI");
                }
            };

            try
            {
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 80,
                    Screen.PrimaryScreen.WorkingArea.Top + 60);
            }
            catch
            {
                StartPosition = FormStartPosition.CenterScreen;
            }

            var ui = BeginCompact(390);
            ui.Section("Temel ve döşeme")
                .Field("Temel yüksekliği", TemelH)
                .Field("Temel üst kotu", Kot)
                .Field("Döşeme kalınlığı", Doseme);
            ui.Section("Hatıl")
                .Field("Yükseklik / kalınlık", HatilH, HatilW)
                .Field("Paspayı", Pas);
            ui.Section("Donatı  (ø çap / aralık)")
                .Field("Düşey", Dusey)
                .Field("Yatay", Yatay)
                .Hint("Plandan çizimde çaplar plandaki hatıl tipinden alınır.");
            ui.Section("Pafta ölçeği · detay 1/25").Control(Olcek);
            ui.Section(null).Control(Denetci.Serit, 44);
            ui.Footer(BtnManuel, BtnKapat, BtnPlan);
            ui.Tip(Doseme, "0 girilirse döşeme bandı çizilmez.");
            ui.Finish();
            Denetci.Guncelle();
        }

        private string AralikUyarisi(string ad, double aralik)
        {
            double sinir = Math.Min(1.5 * HatilW.Deger, 30);
            return aralik > sinir + 1e-9
                ? ad + " aralık " + GirdiTema.Yaz(aralik, 1) + " cm > " + GirdiTema.Yaz(sinir, 1) + " cm (≤ 1,5·t ve ≤ 30 cm önerilir)"
                : null;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            if ((keyData == Keys.Enter || keyData == Keys.Return) && !(ActiveControl is Button))
            {
                Calistir("TEMELHATILICIZ ");
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void LoadFromSettings()
        {
            TemelH.Deger = TemelHatiliSettings.TemelYuksekligiCm;
            Kot.Deger = TemelHatiliSettings.TemelUstKotuCm;
            Doseme.Deger = TemelHatiliSettings.DosemeKalinligiCm;
            HatilH.Deger = TemelHatiliSettings.HatilYuksekligiCm;
            HatilW.Deger = TemelHatiliSettings.HatilKalinligiCm;
            Pas.Deger = TemelHatiliSettings.PaspayiCm;
            Dusey.Ayarla(CapYuvarla(TemelHatiliSettings.DikeyCapMm), TemelHatiliSettings.DikeyAralikCm);
            Yatay.Ayarla(CapYuvarla(TemelHatiliSettings.YatayCapMm), TemelHatiliSettings.YatayAralikCm);
        }

        private static int CapYuvarla(double mm) => (int)Math.Round(mm, MidpointRounding.AwayFromZero);

        /// <summary>Runner'ın okuduğu ayar alanları değişmeden doldurulur.</summary>
        internal void PushSettings()
        {
            TemelHatiliSettings.TemelYuksekligiCm = TemelH.Deger;
            TemelHatiliSettings.TemelUstKotuCm = Kot.Deger;
            TemelHatiliSettings.DosemeKalinligiCm = Doseme.Deger;
            TemelHatiliSettings.HatilYuksekligiCm = HatilH.Deger;
            TemelHatiliSettings.HatilKalinligiCm = HatilW.Deger;
            TemelHatiliSettings.PaspayiCm = Pas.Deger;
            TemelHatiliSettings.DikeyCapMm = Dusey.CapMm;
            TemelHatiliSettings.DikeyAralikCm = Dusey.AralikCm;
            TemelHatiliSettings.YatayCapMm = Yatay.CapMm;
            TemelHatiliSettings.YatayAralikCm = Yatay.AralikCm;
            TemelHatiliSettings.Save();
        }

        private void Calistir(string komut)
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
                doc.SendStringToExecute(komut, true, false, false);
            }
            catch
            {
                _starting = false;
            }
        }
    }
}
