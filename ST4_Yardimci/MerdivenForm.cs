using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    /// <summary>Merdiven detay ayarları (form açıkken runner buradan okur).</summary>
    internal static class MerdivenSettings
    {
        public static double KatYuksekligiCm { get; set; } = 300;
        public static double AltKotCm { get; set; } = 0;
        public static bool TemelVar { get; set; } = true;
        public static double TemelKalinligiCm { get; set; } = 50;
        public static int AnaDonatiCapMm { get; set; } = 12;
        public static int TevziDonatiCapMm { get; set; } = 8;
    }

    /// <summary>Modeless Merdiven formu — şimdilik ayar girişi; çizim sonra.</summary>
    internal sealed class MerdivenForm : Form
    {
        private static MerdivenForm _instance;

        private readonly NumericUpDown _numKat;
        private readonly NumericUpDown _numAltKot;
        private readonly CheckBox _chkTemel;
        private readonly NumericUpDown _numTemelKal;
        private readonly ComboBox _cmbAna;
        private readonly ComboBox _cmbTevzi;
        private readonly Button _btnRun;
        private readonly Button _btnClose;

        public static void ShowOrActivate()
        {
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new MerdivenForm();
                try { AcApp.ShowModelessDialog(_instance); }
                catch { _instance.Show(); }
            }
            else
            {
                _instance.LoadFromSettings();
                _instance.Visible = true;
                _instance.BringToFront();
            }
        }

        public static void Shutdown()
        {
            if (_instance == null) return;
            try { _instance.Close(); } catch { }
            _instance = null;
        }

        private MerdivenForm()
        {
            Text = "Merdiven Detayı";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            Size = new Size(340, 380);
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = Color.White;
            Font = new SdFont("Segoe UI", 9f);

            int y = 14;
            Controls.Add(MakeLabel("Kat yüksekliği (cm)", 14, y)); y += 20;
            _numKat = MakeNum(14, y, 140, 50, 600, 1, (decimal)MerdivenSettings.KatYuksekligiCm);
            Controls.Add(_numKat); y += 36;

            Controls.Add(MakeLabel("Alt kot (cm)", 14, y)); y += 20;
            _numAltKot = MakeNum(14, y, 140, -5000, 5000, 1, (decimal)MerdivenSettings.AltKotCm);
            Controls.Add(_numAltKot); y += 36;

            _chkTemel = new CheckBox
            {
                Text = "Temel var",
                Location = new Point(14, y),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Checked = MerdivenSettings.TemelVar
            };
            _chkTemel.CheckedChanged += (s, e) =>
            {
                _numTemelKal.Enabled = _chkTemel.Checked;
            };
            Controls.Add(_chkTemel); y += 28;

            Controls.Add(MakeLabel("Temel kalınlığı (cm)", 14, y)); y += 20;
            _numTemelKal = MakeNum(14, y, 140, 10, 200, 1, (decimal)MerdivenSettings.TemelKalinligiCm);
            _numTemelKal.Enabled = MerdivenSettings.TemelVar;
            Controls.Add(_numTemelKal); y += 36;

            Controls.Add(MakeLabel("Ana donatı çapı (mm)", 14, y)); y += 20;
            _cmbAna = MakeCapCombo(14, y, 140, MerdivenSettings.AnaDonatiCapMm);
            Controls.Add(_cmbAna); y += 36;

            Controls.Add(MakeLabel("Tevzi donatı çapı (mm)", 14, y)); y += 20;
            _cmbTevzi = MakeCapCombo(14, y, 140, MerdivenSettings.TevziDonatiCapMm);
            Controls.Add(_cmbTevzi); y += 40;

            _btnRun = MakeButton("Devam (çizim sonra)", 14, y, 200);
            _btnRun.ForeColor = AcadPaletteTheme.Accent;
            _btnRun.Click += OnRun;
            _btnClose = MakeButton("Kapat", 226, y, 80);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnRun);
            Controls.Add(_btnClose);

            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                    _instance = null;
            };

            try
            {
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 100,
                    Screen.PrimaryScreen.WorkingArea.Top + 100);
            }
            catch
            {
                StartPosition = FormStartPosition.CenterScreen;
            }
        }

        private void LoadFromSettings()
        {
            _numKat.Value = Clamp(_numKat, (decimal)MerdivenSettings.KatYuksekligiCm);
            _numAltKot.Value = Clamp(_numAltKot, (decimal)MerdivenSettings.AltKotCm);
            _chkTemel.Checked = MerdivenSettings.TemelVar;
            _numTemelKal.Value = Clamp(_numTemelKal, (decimal)MerdivenSettings.TemelKalinligiCm);
            SelectCap(_cmbAna, MerdivenSettings.AnaDonatiCapMm);
            SelectCap(_cmbTevzi, MerdivenSettings.TevziDonatiCapMm);
        }

        private void PushSettings()
        {
            MerdivenSettings.KatYuksekligiCm = (double)_numKat.Value;
            MerdivenSettings.AltKotCm = (double)_numAltKot.Value;
            MerdivenSettings.TemelVar = _chkTemel.Checked;
            MerdivenSettings.TemelKalinligiCm = (double)_numTemelKal.Value;
            MerdivenSettings.AnaDonatiCapMm = ParseCap(_cmbAna);
            MerdivenSettings.TevziDonatiCapMm = ParseCap(_cmbTevzi);
        }

        private void OnRun(object sender, EventArgs e)
        {
            PushSettings();
            try
            {
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    "\nMerdiven: ayarlar kaydedildi (kat {0} cm, alt kot {1}, temel {2}). Çizim adımı sonra eklenecek.",
                    MerdivenSettings.KatYuksekligiCm.ToString("0.##", CultureInfo.InvariantCulture),
                    MerdivenSettings.AltKotCm.ToString("0.##", CultureInfo.InvariantCulture),
                    MerdivenSettings.TemelVar ? MerdivenSettings.TemelKalinligiCm.ToString("0.##", CultureInfo.InvariantCulture) + " cm" : "yok");
            }
            catch { }
        }

        private static decimal Clamp(NumericUpDown n, decimal v)
        {
            if (v < n.Minimum) return n.Minimum;
            if (v > n.Maximum) return n.Maximum;
            return v;
        }

        private static int ParseCap(ComboBox cmb)
        {
            if (cmb.SelectedItem is string s)
            {
                int v;
                if (int.TryParse(s, out v)) return v;
            }
            return 12;
        }

        private static void SelectCap(ComboBox cmb, int mm)
        {
            string key = mm.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < cmb.Items.Count; i++)
            {
                if (cmb.Items[i] is string s && s == key)
                {
                    cmb.SelectedIndex = i;
                    return;
                }
            }
            if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        }

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
                BackColor = AcadPaletteTheme.BgAlt,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = AcadPaletteTheme.InputBorder;
            b.FlatAppearance.MouseOverBackColor = AcadPaletteTheme.BgHover;
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
                BackColor = AcadPaletteTheme.InputBg,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            n.Value = Clamp(n, val);
            return n;
        }

        private ComboBox MakeCapCombo(int x, int y, int w, int selectedMm)
        {
            var c = new ComboBox
            {
                Location = new Point(x, y),
                Size = new Size(w, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = AcadPaletteTheme.InputBg,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            foreach (int d in new[] { 8, 10, 12, 14, 16, 18, 20, 22, 25 })
                c.Items.Add(d.ToString(CultureInfo.InvariantCulture));
            SelectCap(c, selectedMm);
            return c;
        }
    }
}
