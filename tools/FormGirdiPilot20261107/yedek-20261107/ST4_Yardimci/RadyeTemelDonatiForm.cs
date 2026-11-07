using System;
using System.Drawing;
using System.Windows.Forms;
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

        private readonly NumericUpDown _numYukseklik;
        private readonly NumericUpDown _numPaspayi;
        private readonly NumericUpDown _numAralik;
        private readonly CheckBox _chkIlaveGonye;
        private readonly NumericUpDown _numGonyeMax;
        private readonly RadioButton _rbAlt;
        private readonly RadioButton _rbUst;
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

            int y = 14;
            Controls.Add(MakeLabel("Radye temel yüksekliği (cm)", 14, y)); y += 20;
            _numYukseklik = MakeNum(14, y, 160, 15, 300, 1, (decimal)RadyeTemelDonatiSettings.TemelYuksekligiCm);
            Controls.Add(_numYukseklik); y += 34;

            Controls.Add(MakeLabel("Paspayı (cm)", 14, y)); y += 20;
            _numPaspayi = MakeNum(14, y, 160, 2, 15, 0.5m, (decimal)RadyeTemelDonatiSettings.PaspayiCm);
            Controls.Add(_numPaspayi); y += 34;

            Controls.Add(MakeLabel("Yedek aralık (cm) — kutu yazısı yoksa", 14, y)); y += 20;
            _numAralik = MakeNum(14, y, 160, 5, 50, 1, (decimal)RadyeTemelDonatiSettings.AralikCm);
            _numAralik.DecimalPlaces = 0;
            Controls.Add(_numAralik); y += 34;

            _chkIlaveGonye = new CheckBox
            {
                Text = "TEMEL gönye",
                Location = new Point(14, y + 2),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Checked = RadyeTemelDonatiSettings.IlaveGonye
            };
            Controls.Add(_chkIlaveGonye);
            Controls.Add(MakeLabel("max (cm)", 130, y));
            _numGonyeMax = MakeNum(200, y - 2, 70, 5, 200, 1, (decimal)RadyeTemelDonatiSettings.GonyeBoyuMaxCm);
            _numGonyeMax.DecimalPlaces = 0;
            Controls.Add(_numGonyeMax);
            y += 34;

            Controls.Add(MakeLabel("İlave kat", 14, y)); y += 22;
            _rbAlt = MakeRadio("Alt ilave", 14, y);
            _rbUst = MakeRadio("Üst ilave", 120, y);
            if (RadyeTemelDonatiSettings.IlaveUst) _rbUst.Checked = true;
            else _rbAlt.Checked = true;
            Controls.Add(_rbAlt);
            Controls.Add(_rbUst);
            y += 36;

            _btnRun = MakeButton("Plan seç / zarfa donatı at", 14, y, 200);
            _btnRun.ForeColor = Beykent.Monolit.StandardFormTheme.Accent;
            _btnRun.Click += (s, e) => OnRun();
            _btnClose = MakeButton("Kapat", 224, y, 72);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnRun);
            Controls.Add(_btnClose);

            FormClosing += (s, e) => { try { PushSettings(); } catch { } };
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

            // Kompakt, içerik odaklı form düzeni.
            var ui = BeginCompact(350);
            _chkIlaveGonye.Text = "Gönye max · cm";
            ui.Section("Radye kesiti").Field("Temel yüksekliği · cm", _numYukseklik).Field("Paspayı · cm", _numPaspayi)
                .Field("Yedek aralık · cm", _numAralik);
            ui.Section("İlave donatı").Options(_rbAlt, _rbUst).Field(_chkIlaveGonye, _numGonyeMax);
            ui.Tip(_numGonyeMax, "Gönye için en büyük boy (cm)");
            ui.Footer(_btnRun, _btnClose);
            ui.Finish();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            if (keyData == Keys.Enter || keyData == Keys.Return || keyData == Keys.Space)
            {
                OnRun();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void LoadFromSettings()
        {
            _numYukseklik.Value = Clamp(_numYukseklik, (decimal)RadyeTemelDonatiSettings.TemelYuksekligiCm);
            _numPaspayi.Value = Clamp(_numPaspayi, (decimal)RadyeTemelDonatiSettings.PaspayiCm);
            _numAralik.Value = Clamp(_numAralik, (decimal)RadyeTemelDonatiSettings.AralikCm);
            _chkIlaveGonye.Checked = RadyeTemelDonatiSettings.IlaveGonye;
            _numGonyeMax.Value = Clamp(_numGonyeMax, (decimal)RadyeTemelDonatiSettings.GonyeBoyuMaxCm);
            _rbUst.Checked = RadyeTemelDonatiSettings.IlaveUst;
            _rbAlt.Checked = !RadyeTemelDonatiSettings.IlaveUst;
        }

        private void PushSettings()
        {
            RadyeTemelDonatiSettings.TemelYuksekligiCm = (double)_numYukseklik.Value;
            RadyeTemelDonatiSettings.PaspayiCm = (double)_numPaspayi.Value;
            RadyeTemelDonatiSettings.AralikCm = (double)_numAralik.Value;
            RadyeTemelDonatiSettings.IlaveGonye = _chkIlaveGonye.Checked;
            RadyeTemelDonatiSettings.GonyeBoyuMaxCm = (double)_numGonyeMax.Value;
            RadyeTemelDonatiSettings.IlaveUst = _rbUst.Checked;
            RadyeTemelDonatiSettings.Save();
        }

        private void OnRun()
        {
            if (_starting) return;
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

        private static decimal Clamp(NumericUpDown n, decimal v)
        {
            if (v < n.Minimum) return n.Minimum;
            if (v > n.Maximum) return n.Maximum;
            return v;
        }

        private static RadioButton MakeRadio(string text, int x, int y) => new RadioButton
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };

        private Label MakeLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent
            };
        }

        private Button MakeButton(string text, int x, int y, int w)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Beykent.Monolit.StandardFormTheme.BgAlt,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Beykent.Monolit.StandardFormTheme.InputBorder;
            b.FlatAppearance.MouseOverBackColor = Beykent.Monolit.StandardFormTheme.BgHover;
            return b;
        }

        private NumericUpDown MakeNum(int x, int y, int w, decimal min, decimal max, decimal inc, decimal val)
        {
            var n = new NumericUpDown
            {
                Location = new Point(x, y),
                Size = new Size(w, 26),
                Minimum = min,
                Maximum = max,
                Increment = inc,
                DecimalPlaces = 1,
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            n.Value = Clamp(n, val);
            YardimciUi.StripUpDownButtons(n);
            return n;
        }
    }
}
