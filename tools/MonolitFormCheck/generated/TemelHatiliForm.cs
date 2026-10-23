using System;
using System.Drawing;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

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

    /// <summary>Temel Hatılı formu — plandan çiz (sonra) / manuel çiz.</summary>
    internal sealed class TemelHatiliForm : Beykent.Monolit.MonolitForm
    {
        private static TemelHatiliForm _instance;

        private readonly NumericUpDown _numTemelH;
        private readonly NumericUpDown _numKotu;
        private readonly NumericUpDown _numDoseme;
        private readonly NumericUpDown _numHatilH;
        private readonly NumericUpDown _numHatilW;
        private readonly NumericUpDown _numPas;
        private readonly NumericUpDown _numDikeyCap;
        private readonly NumericUpDown _numDikeyAralik;
        private readonly NumericUpDown _numYatayCap;
        private readonly NumericUpDown _numYatayAralik;
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
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(420, 292);
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            Font = new SdFont("Segoe UI", 8.5f);
            KeyPreview = true;

            int y = 8;
            _numTemelH = AddRow("Temel yükseklik", "cm", 0, ref y, 20, 400, 1, 0, (decimal)TemelHatiliSettings.TemelYuksekligiCm);
            _numKotu = AddRow("Temel üst kot", "cm", 0, ref y, -8000, 8000, 1, 0, (decimal)TemelHatiliSettings.TemelUstKotuCm);
            _numDoseme = AddRow("Döşeme kalınlık", "cm", 0, ref y, 0, 80, 1, 0, (decimal)TemelHatiliSettings.DosemeKalinligiCm);
            _numHatilH = AddRow("Hatıl yükseklik", "cm", 0, ref y, 20, 400, 1, 0, (decimal)TemelHatiliSettings.HatilYuksekligiCm);
            _numHatilW = AddRow("Hatıl kalınlık", "cm", 0, ref y, 10, 80, 1, 0, (decimal)TemelHatiliSettings.HatilKalinligiCm);
            _numPas = AddRow("Paspayı", "cm", 0, ref y, 2, 20, 0.5m, 1, (decimal)TemelHatiliSettings.PaspayiCm);

            y = 8;
            _numDikeyCap = AddRow("Dikey çap", "mm", 1, ref y, 8, 40, 1, 0, (decimal)TemelHatiliSettings.DikeyCapMm);
            _numDikeyAralik = AddRow("Dikey aralık", "cm", 1, ref y, 5, 50, 1, 0, (decimal)TemelHatiliSettings.DikeyAralikCm);
            _numYatayCap = AddRow("Yatay çap", "mm", 1, ref y, 8, 40, 1, 0, (decimal)TemelHatiliSettings.YatayCapMm);
            _numYatayAralik = AddRow("Yatay aralık", "cm", 1, ref y, 5, 50, 1, 0, (decimal)TemelHatiliSettings.YatayAralikCm);

            int yBtn = 252;
            var btnPlan = MakeButton("Plandan çiz", 10, yBtn, 100, false);
            btnPlan.Click += (s, e) => OnPlan();
            var btnManuel = MakeButton("Manuel çiz", 116, yBtn, 100, true);
            btnManuel.Click += (s, e) => OnManuel();
            var btnClose = MakeButton("Kapat", 222, yBtn, 70, false);
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnPlan);
            Controls.Add(btnManuel);
            Controls.Add(btnClose);

            FormClosing += (s, e) => { try { PushSettings(); } catch { } };
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

            // Kompakt, içerik odaklı form düzeni.
            var ui = BeginCompact(350);
            ui.Tabs(202,
                ui.Page("Kesit", p => p.Field("Temel yüksekliği · cm", _numTemelH).Field("Temel üst kotu · cm", _numKotu)
                    .Field("Döşeme kalınlığı · cm", _numDoseme).Field("Hatıl yük. / en · cm", _numHatilH, _numHatilW).Field("Paspayı · cm", _numPas)),
                ui.Page("Donatı", p => p.Field("Düşey · Ø mm / ara cm", _numDikeyCap, _numDikeyAralik)
                    .Field("Yatay · Ø mm / ara cm", _numYatayCap, _numYatayAralik)));
            ui.Footer(btnManuel, btnClose, btnPlan);
            ui.Finish();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            if (keyData == Keys.Enter || keyData == Keys.Return)
            {
                OnManuel();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void LoadFromSettings()
        {
            _numTemelH.Value = Clamp(_numTemelH, (decimal)TemelHatiliSettings.TemelYuksekligiCm);
            _numKotu.Value = Clamp(_numKotu, (decimal)TemelHatiliSettings.TemelUstKotuCm);
            _numDoseme.Value = Clamp(_numDoseme, (decimal)TemelHatiliSettings.DosemeKalinligiCm);
            _numHatilH.Value = Clamp(_numHatilH, (decimal)TemelHatiliSettings.HatilYuksekligiCm);
            _numHatilW.Value = Clamp(_numHatilW, (decimal)TemelHatiliSettings.HatilKalinligiCm);
            _numPas.Value = Clamp(_numPas, (decimal)TemelHatiliSettings.PaspayiCm);
            _numDikeyCap.Value = Clamp(_numDikeyCap, (decimal)TemelHatiliSettings.DikeyCapMm);
            _numDikeyAralik.Value = Clamp(_numDikeyAralik, (decimal)TemelHatiliSettings.DikeyAralikCm);
            _numYatayCap.Value = Clamp(_numYatayCap, (decimal)TemelHatiliSettings.YatayCapMm);
            _numYatayAralik.Value = Clamp(_numYatayAralik, (decimal)TemelHatiliSettings.YatayAralikCm);
        }

        private void PushSettings()
        {
            TemelHatiliSettings.TemelYuksekligiCm = (double)_numTemelH.Value;
            TemelHatiliSettings.TemelUstKotuCm = (double)_numKotu.Value;
            TemelHatiliSettings.DosemeKalinligiCm = (double)_numDoseme.Value;
            TemelHatiliSettings.HatilYuksekligiCm = (double)_numHatilH.Value;
            TemelHatiliSettings.HatilKalinligiCm = (double)_numHatilW.Value;
            TemelHatiliSettings.PaspayiCm = (double)_numPas.Value;
            TemelHatiliSettings.DikeyCapMm = (double)_numDikeyCap.Value;
            TemelHatiliSettings.DikeyAralikCm = (double)_numDikeyAralik.Value;
            TemelHatiliSettings.YatayCapMm = (double)_numYatayCap.Value;
            TemelHatiliSettings.YatayAralikCm = (double)_numYatayAralik.Value;
            TemelHatiliSettings.Save();
        }

        private void OnPlan()
        {
            if (_starting) return;
            PushSettings();
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                _starting = true;
                HideTemporarily();
                doc.SendStringToExecute("TEMELHATILIPLAN ", true, false, false);
            }
            catch
            {
                _starting = false;
            }
        }

        private void OnManuel()
        {
            if (_starting) return;
            PushSettings();
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                _starting = true;
                HideTemporarily();
                doc.SendStringToExecute("TEMELHATILICIZ ", true, false, false);
            }
            catch
            {
                _starting = false;
            }
        }

        /// <summary>col 0 = sol, 1 = sağ. Satır yüksekliği 26 px.</summary>
        private NumericUpDown AddRow(string label, string unit, int col, ref int y,
            decimal min, decimal max, decimal inc, int decimals, decimal val)
        {
            int x0 = col == 0 ? 10 : 215;
            var lbl = new Label
            {
                Text = label,
                Location = new Point(x0, y + 3),
                AutoSize = false,
                Size = new Size(100, 18),
                ForeColor = Beykent.Monolit.StandardFormTheme.TextMuted,
                BackColor = Color.Transparent
            };
            Controls.Add(lbl);
            var n = MakeNum(x0 + 102, y, 58, min, max, inc, decimals, val);
            Controls.Add(n);
            var u = new Label
            {
                Text = unit,
                Location = new Point(x0 + 162, y + 3),
                AutoSize = true,
                ForeColor = Beykent.Monolit.StandardFormTheme.TextMuted,
                BackColor = Color.Transparent
            };
            Controls.Add(u);
            y += 26;
            return n;
        }

        private static decimal Clamp(NumericUpDown n, decimal v)
        {
            if (v < n.Minimum) return n.Minimum;
            if (v > n.Maximum) return n.Maximum;
            return v;
        }

        private static Button MakeButton(string text, int x, int y, int w, bool accent)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Beykent.Monolit.StandardFormTheme.BgAlt,
                ForeColor = accent ? Beykent.Monolit.StandardFormTheme.Accent : Beykent.Monolit.StandardFormTheme.Text,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Beykent.Monolit.StandardFormTheme.InputBorder;
            b.FlatAppearance.MouseOverBackColor = Beykent.Monolit.StandardFormTheme.BgHover;
            return b;
        }

        private static NumericUpDown MakeNum(int x, int y, int w, decimal min, decimal max, decimal inc, int decimals, decimal val)
        {
            var n = new NumericUpDown
            {
                Location = new Point(x, y),
                Size = new Size(w, 22),
                Minimum = min,
                Maximum = max,
                Increment = inc,
                DecimalPlaces = decimals,
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            n.Value = Clamp(n, val);
            YardimciUi.StripUpDownButtons(n);
            return n;
        }
    }
}
