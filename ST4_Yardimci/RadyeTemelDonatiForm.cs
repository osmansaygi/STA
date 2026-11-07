using System;
using System.Drawing;
using System.Windows.Forms;
using Beykent.Monolit.Girdi;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    /// <summary>Radye ilave donatı ayarları (form açıkken runner buradan okur).</summary>
    internal static class RadyeTemelDonatiSettings
    {
        /// <summary>Radye temel yüksekliği / kalınlığı (cm).</summary>
        public static double TemelYuksekligiCm { get; set; } = 50;
        public static double PaspayiCm { get; set; } = 5;
        /// <summary>TEMEL kenarı / trim ucunda gönye.</summary>
        public static bool IlaveGonye { get; set; } = true;
        /// <summary>Çizimde gösterilecek max gönye boyu (cm).</summary>
        public static double GonyeBoyuMaxCm { get; set; } = 40;
        /// <summary>Yedek aralık (cm) — KUTU X yazısı yoksa.</summary>
        public static double AralikCm { get; set; } = 15;
        /// <summary>true = üst ilave (üst); false = alt ilave (alt).</summary>
        public static bool IlaveUst { get; set; }

        static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var m = BaIni.Read("radyeilave.ini");
            TemelYuksekligiCm = BaIni.GetD(m, "TemelYuksekligiCm", TemelYuksekligiCm);
            PaspayiCm = BaIni.GetD(m, "PaspayiCm", PaspayiCm);
            IlaveGonye = BaIni.GetB(m, "IlaveGonye", IlaveGonye);
            GonyeBoyuMaxCm = BaIni.GetD(m, "GonyeBoyuMaxCm", GonyeBoyuMaxCm);
            AralikCm = BaIni.GetD(m, "AralikCm", AralikCm);
            IlaveUst = BaIni.GetB(m, "IlaveUst", IlaveUst);
        }

        public static void Save()
        {
            EnsureLoaded();
            BaIni.Write("radyeilave.ini",
                "TemelYuksekligiCm=" + BaIni.N(TemelYuksekligiCm),
                "PaspayiCm=" + BaIni.N(PaspayiCm),
                "IlaveGonye=" + BaIni.B(IlaveGonye),
                "GonyeBoyuMaxCm=" + BaIni.N(GonyeBoyuMaxCm),
                "AralikCm=" + BaIni.N(AralikCm),
                "IlaveUst=" + BaIni.B(IlaveUst));
        }

        /// <summary>Etiket son eki: "(üst)" / "(alt)".</summary>
        public static string IlaveKatEtiketi => IlaveUst ? "(üst)" : "(alt)";

        /// <summary>Gerçek gönye = yükseklik − 2×paspayı.</summary>
        public static double RealGonyeBoyuCm
        {
            get
            {
                double g = TemelYuksekligiCm - 2.0 * PaspayiCm;
                return g > 0 ? g : 0.0;
            }
        }

        /// <summary>Çizim gönyesi = min(gerçek, max).</summary>
        public static double EffectiveGonyeBoyuCm
        {
            get
            {
                double avail = RealGonyeBoyuCm;
                double max = GonyeBoyuMaxCm;
                if (max < 0) max = 0;
                double g = avail < max ? avail : max;
                return g > 0.5 ? g : 0.0;
            }
        }
    }

    /// <summary>Modeless Radye İlave Donatı formu — alt/üst ilave → ısı seç → X çizer.</summary>
    internal sealed class RadyeTemelDonatiForm : Beykent.Monolit.MonolitForm
    {
        private static RadyeTemelDonatiForm _instance;

        private readonly OlcuKutusu _numYukseklik;
        private readonly OlcuKutusu _numPaspayi;
        private readonly OlcuKutusu _numAralik;
        private readonly CheckBox _chkIlaveGonye;
        private readonly OlcuKutusu _numGonyeMax;
        private readonly SegmentSecim _ilaveKat;
        private readonly GirdiDenetci Denetci = new GirdiDenetci();
        private readonly Button _btnRun;
        private readonly Button _btnClose;
        private bool _starting;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("RADYEILAVE")) return;
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new RadyeTemelDonatiForm();
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
            BaFormSession.Attach("RADYEILAVE", _instance);
            BaFormSession.EndPick("RADYEILAVE");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("RADYEILAVE");
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
                
                BaFormSession.BeginPick("RADYEILAVE");
                _instance.Visible = false;
            }
            catch { }
        }

        public static void ShowAfterPick()
        {
            BaFormSession.EndPick("RADYEILAVE");
            try
            {
                if (_instance != null && !_instance.IsDisposed)
                    _instance._starting = false;
                ShowOrActivate();
            }
            catch { }
        }

        private RadyeTemelDonatiForm()
        {
            RadyeTemelDonatiSettings.EnsureLoaded();
            Text = "Radye İlave Donatı";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(360, 360);
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Color.White;
            Font = new SdFont("Segoe UI", 9f);
            KeyPreview = true;

            // Sınırlar ve ondalıklar eski NumericUpDown ile aynı.
            _numYukseklik = Denetci.Ekle(new OlcuKutusu("Temel yüksekliği", "cm", 15, 300, 50, 1, 1));
            _numPaspayi = Denetci.Ekle(new OlcuKutusu("Paspayı", "cm", 2, 15, 5, 1, 0.5));
            _numAralik = Denetci.Ekle(new OlcuKutusu("Yedek aralık", "cm", 5, 50, 15));
            _numGonyeMax = Denetci.Ekle(new OlcuKutusu("Gönye max", "cm", 5, 200, 40));
            _chkIlaveGonye = new CheckBox
            {
                Text = "TEMEL kenarında gönye",
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent
            };
            Controls.Add(_chkIlaveGonye);
            _ilaveKat = new SegmentSecim("Alt ilave", "Üst ilave");
            LoadFromSettings();

            Denetci.UyariKurali(() => _chkIlaveGonye.Checked && _numYukseklik.Deger - 2 * _numPaspayi.Deger <= 0.5
                ? "Gönye çizilmez: yükseklik − 2·paspayı ≤ 0,5 cm" : null);

            _btnRun = MakeButton("Plan seç / zarfa donatı at");
            _btnRun.ForeColor = Beykent.Monolit.StandardFormTheme.Accent;
            _btnRun.Click += (s, e) => OnRun();
            _btnClose = MakeButton("Kapat");
            _btnClose.Click += (s, e) => Close();

            FormClosing += (s, e) => { try { if (Denetci.Uygun) PushSettings(); } catch { } };
            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                {
                    _instance = null;
                    BaFormSession.Release("RADYEILAVE");
                }
            };

            try
            {
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 120,
                    Screen.PrimaryScreen.WorkingArea.Top + 80);
            }
            catch
            {
                StartPosition = FormStartPosition.CenterScreen;
            }

            var ui = BeginCompact(350);
            ui.Section("Radye kesiti")
                .Field("Temel yüksekliği", _numYukseklik)
                .Field("Paspayı", _numPaspayi);
            ui.Section("İlave donatı")
                .Control(_ilaveKat)
                .Field("Yedek aralık", _numAralik)
                .Options(_chkIlaveGonye)
                .FieldWhen(() => _chkIlaveGonye.Checked, new Label { Text = "Gönye max" }, _numGonyeMax);
            ui.Section(null).Control(Denetci.Serit, 44);
            ui.Tip(_numAralik, "KUTU X yazısı yoksa kullanılan aralık.");
            ui.Tip(_numGonyeMax, "Gönye için en büyük boy; gerçek gönye = yükseklik − 2×paspayı.");
            ui.Footer(_btnRun, _btnClose);
            ui.Finish();
            Denetci.Guncelle();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            bool yaziGirisi = ActiveControl is GirdiKutusu || ActiveControl is TextBoxBase;
            if (((keyData == Keys.Enter || keyData == Keys.Return) && !(ActiveControl is Button))
                || (keyData == Keys.Space && !yaziGirisi && !(ActiveControl is Button)))
            {
                OnRun();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void LoadFromSettings()
        {
            _numYukseklik.Deger = Kirp(RadyeTemelDonatiSettings.TemelYuksekligiCm, 15, 300);
            _numPaspayi.Deger = Kirp(RadyeTemelDonatiSettings.PaspayiCm, 2, 15);
            _numAralik.Deger = Kirp(Math.Round(RadyeTemelDonatiSettings.AralikCm), 5, 50);
            _chkIlaveGonye.Checked = RadyeTemelDonatiSettings.IlaveGonye;
            _numGonyeMax.Deger = Kirp(Math.Round(RadyeTemelDonatiSettings.GonyeBoyuMaxCm), 5, 200);
            _ilaveKat.Secili = RadyeTemelDonatiSettings.IlaveUst ? 1 : 0;
        }

        private void PushSettings()
        {
            RadyeTemelDonatiSettings.TemelYuksekligiCm = _numYukseklik.Deger;
            RadyeTemelDonatiSettings.PaspayiCm = _numPaspayi.Deger;
            RadyeTemelDonatiSettings.AralikCm = _numAralik.Deger;
            RadyeTemelDonatiSettings.IlaveGonye = _chkIlaveGonye.Checked;
            RadyeTemelDonatiSettings.GonyeBoyuMaxCm = _numGonyeMax.Deger;
            RadyeTemelDonatiSettings.IlaveUst = _ilaveKat.Secili == 1;
            RadyeTemelDonatiSettings.Save();
        }

        private void OnRun()
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
                doc.SendStringToExecute("RADYEILAVECIZ ", true, false, false);
            }
            catch
            {
                _starting = false;
            }
        }

        private static double Kirp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));

        private Button MakeButton(string text)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = Beykent.Monolit.StandardFormTheme.BgAlt,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Beykent.Monolit.StandardFormTheme.InputBorder;
            b.FlatAppearance.MouseOverBackColor = Beykent.Monolit.StandardFormTheme.BgHover;
            return b;
        }
    }
}
