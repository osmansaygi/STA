using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    /// <summary>Gönye / uç detayı seçenekleri.</summary>
    internal enum GonyeSecenek
    {
        Yok = 0,
        TekUc = 1,
        CiftUc = 2,
        Kanca90 = 3,
        Kanca135 = 4
    }

    /// <summary>Radye temel donatı ayarları (form açıkken runner buradan okur).</summary>
    internal static class RadyeTemelDonatiSettings
    {
        public static double TemelKalinligiCm { get; set; } = 50;
        public static double PaspayiCm { get; set; } = 5;
        public static int AltDonatiCapMm { get; set; } = 14;
        public static int UstDonatiCapMm { get; set; } = 12;
        public static double AltAralikCm { get; set; } = 15;
        public static double UstAralikCm { get; set; } = 15;
        public static GonyeSecenek AltGonye { get; set; } = GonyeSecenek.CiftUc;
        public static GonyeSecenek UstGonye { get; set; } = GonyeSecenek.CiftUc;
        /// <summary>Gönye boyu = bu çarpan × Φ (mm cinsinden çap).</summary>
        public static double GonyeBoyuFiCarpani { get; set; } = 12;
        /// <summary>Bindirme boyu Ld = bu çarpan × Φ.</summary>
        public static double BindirmeBoyuFiCarpani { get; set; } = 50;
        public static bool CiftYonDonati { get; set; } = true;
    }

    /// <summary>Modeless Radye Temel Donatı formu — şimdilik ayar girişi; çizim sonra.</summary>
    internal sealed class RadyeTemelDonatiForm : Form
    {
        private static RadyeTemelDonatiForm _instance;

        private readonly NumericUpDown _numKalinlik;
        private readonly NumericUpDown _numPaspayi;
        private readonly ComboBox _cmbAltCap;
        private readonly ComboBox _cmbUstCap;
        private readonly NumericUpDown _numAltAralik;
        private readonly NumericUpDown _numUstAralik;
        private readonly ComboBox _cmbAltGonye;
        private readonly ComboBox _cmbUstGonye;
        private readonly NumericUpDown _numGonyeFi;
        private readonly NumericUpDown _numBindirmeFi;
        private readonly CheckBox _chkCiftYon;
        private readonly Button _btnRun;
        private readonly Button _btnClose;

        public static void ShowOrActivate()
        {
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new RadyeTemelDonatiForm();
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

        private RadyeTemelDonatiForm()
        {
            Text = "Radye Temel Donatısı";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            Size = new Size(360, 620);
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = Color.White;
            Font = new SdFont("Segoe UI", 9f);

            int y = 14;
            Controls.Add(MakeLabel("Temel kalınlığı (cm)", 14, y)); y += 20;
            _numKalinlik = MakeNum(14, y, 160, 15, 300, 1, (decimal)RadyeTemelDonatiSettings.TemelKalinligiCm);
            Controls.Add(_numKalinlik); y += 34;

            Controls.Add(MakeLabel("Paspayı (cm)", 14, y)); y += 20;
            _numPaspayi = MakeNum(14, y, 160, 2, 15, 0.5m, (decimal)RadyeTemelDonatiSettings.PaspayiCm);
            Controls.Add(_numPaspayi); y += 34;

            Controls.Add(MakeLabel("Alt donatı çapı (mm)", 14, y)); y += 20;
            _cmbAltCap = MakeCapCombo(14, y, 160, RadyeTemelDonatiSettings.AltDonatiCapMm);
            Controls.Add(_cmbAltCap); y += 34;

            Controls.Add(MakeLabel("Üst donatı çapı (mm)", 14, y)); y += 20;
            _cmbUstCap = MakeCapCombo(14, y, 160, RadyeTemelDonatiSettings.UstDonatiCapMm);
            Controls.Add(_cmbUstCap); y += 34;

            Controls.Add(MakeLabel("Alt donatı aralığı (cm)", 14, y)); y += 20;
            _numAltAralik = MakeNum(14, y, 160, 5, 40, 1, (decimal)RadyeTemelDonatiSettings.AltAralikCm);
            Controls.Add(_numAltAralik); y += 34;

            Controls.Add(MakeLabel("Üst donatı aralığı (cm)", 14, y)); y += 20;
            _numUstAralik = MakeNum(14, y, 160, 5, 40, 1, (decimal)RadyeTemelDonatiSettings.UstAralikCm);
            Controls.Add(_numUstAralik); y += 34;

            Controls.Add(MakeLabel("Alt donatı gönye", 14, y)); y += 20;
            _cmbAltGonye = MakeGonyeCombo(14, y, 200, RadyeTemelDonatiSettings.AltGonye);
            Controls.Add(_cmbAltGonye); y += 34;

            Controls.Add(MakeLabel("Üst donatı gönye", 14, y)); y += 20;
            _cmbUstGonye = MakeGonyeCombo(14, y, 200, RadyeTemelDonatiSettings.UstGonye);
            Controls.Add(_cmbUstGonye); y += 34;

            Controls.Add(MakeLabel("Gönye boyu Φ çarpanı", 14, y)); y += 20;
            _numGonyeFi = MakeNum(14, y, 160, 5, 40, 1, (decimal)RadyeTemelDonatiSettings.GonyeBoyuFiCarpani);
            _numGonyeFi.DecimalPlaces = 0;
            Controls.Add(_numGonyeFi); y += 34;

            Controls.Add(MakeLabel("Bindirme boyu Φ çarpanı", 14, y)); y += 20;
            _numBindirmeFi = MakeNum(14, y, 160, 20, 80, 1, (decimal)RadyeTemelDonatiSettings.BindirmeBoyuFiCarpani);
            _numBindirmeFi.DecimalPlaces = 0;
            Controls.Add(_numBindirmeFi); y += 34;

            _chkCiftYon = new CheckBox
            {
                Text = "Çift yön donatı (X + Y)",
                Location = new Point(14, y),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Checked = RadyeTemelDonatiSettings.CiftYonDonati
            };
            Controls.Add(_chkCiftYon); y += 36;

            _btnRun = MakeButton("Devam (çizim sonra)", 14, y, 210);
            _btnRun.ForeColor = AcadPaletteTheme.Accent;
            _btnRun.Click += OnRun;
            _btnClose = MakeButton("Kapat", 236, y, 80);
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
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 120,
                    Screen.PrimaryScreen.WorkingArea.Top + 80);
            }
            catch
            {
                StartPosition = FormStartPosition.CenterScreen;
            }
        }

        private void LoadFromSettings()
        {
            _numKalinlik.Value = Clamp(_numKalinlik, (decimal)RadyeTemelDonatiSettings.TemelKalinligiCm);
            _numPaspayi.Value = Clamp(_numPaspayi, (decimal)RadyeTemelDonatiSettings.PaspayiCm);
            SelectCap(_cmbAltCap, RadyeTemelDonatiSettings.AltDonatiCapMm);
            SelectCap(_cmbUstCap, RadyeTemelDonatiSettings.UstDonatiCapMm);
            _numAltAralik.Value = Clamp(_numAltAralik, (decimal)RadyeTemelDonatiSettings.AltAralikCm);
            _numUstAralik.Value = Clamp(_numUstAralik, (decimal)RadyeTemelDonatiSettings.UstAralikCm);
            SelectGonye(_cmbAltGonye, RadyeTemelDonatiSettings.AltGonye);
            SelectGonye(_cmbUstGonye, RadyeTemelDonatiSettings.UstGonye);
            _numGonyeFi.Value = Clamp(_numGonyeFi, (decimal)RadyeTemelDonatiSettings.GonyeBoyuFiCarpani);
            _numBindirmeFi.Value = Clamp(_numBindirmeFi, (decimal)RadyeTemelDonatiSettings.BindirmeBoyuFiCarpani);
            _chkCiftYon.Checked = RadyeTemelDonatiSettings.CiftYonDonati;
        }

        private void PushSettings()
        {
            RadyeTemelDonatiSettings.TemelKalinligiCm = (double)_numKalinlik.Value;
            RadyeTemelDonatiSettings.PaspayiCm = (double)_numPaspayi.Value;
            RadyeTemelDonatiSettings.AltDonatiCapMm = ParseCap(_cmbAltCap);
            RadyeTemelDonatiSettings.UstDonatiCapMm = ParseCap(_cmbUstCap);
            RadyeTemelDonatiSettings.AltAralikCm = (double)_numAltAralik.Value;
            RadyeTemelDonatiSettings.UstAralikCm = (double)_numUstAralik.Value;
            RadyeTemelDonatiSettings.AltGonye = ParseGonye(_cmbAltGonye);
            RadyeTemelDonatiSettings.UstGonye = ParseGonye(_cmbUstGonye);
            RadyeTemelDonatiSettings.GonyeBoyuFiCarpani = (double)_numGonyeFi.Value;
            RadyeTemelDonatiSettings.BindirmeBoyuFiCarpani = (double)_numBindirmeFi.Value;
            RadyeTemelDonatiSettings.CiftYonDonati = _chkCiftYon.Checked;
        }

        private void OnRun(object sender, EventArgs e)
        {
            PushSettings();
            try
            {
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    "\nRadye Temel Donatı: h={0} cm, alt Φ{1}/@{2}, üst Φ{3}/@{4}, bindirme {5}Φ. Çizim sonra.",
                    RadyeTemelDonatiSettings.TemelKalinligiCm.ToString("0.##", CultureInfo.InvariantCulture),
                    RadyeTemelDonatiSettings.AltDonatiCapMm,
                    RadyeTemelDonatiSettings.AltAralikCm.ToString("0.##", CultureInfo.InvariantCulture),
                    RadyeTemelDonatiSettings.UstDonatiCapMm,
                    RadyeTemelDonatiSettings.UstAralikCm.ToString("0.##", CultureInfo.InvariantCulture),
                    RadyeTemelDonatiSettings.BindirmeBoyuFiCarpani.ToString("0.##", CultureInfo.InvariantCulture));
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

        private static GonyeSecenek ParseGonye(ComboBox cmb)
        {
            if (cmb.SelectedItem is GonyeItem gi) return gi.Value;
            return GonyeSecenek.Yok;
        }

        private static void SelectGonye(ComboBox cmb, GonyeSecenek v)
        {
            for (int i = 0; i < cmb.Items.Count; i++)
            {
                if (cmb.Items[i] is GonyeItem gi && gi.Value == v)
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
            foreach (int d in new[] { 8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 32 })
                c.Items.Add(d.ToString(CultureInfo.InvariantCulture));
            SelectCap(c, selectedMm);
            return c;
        }

        private ComboBox MakeGonyeCombo(int x, int y, int w, GonyeSecenek selected)
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
            c.Items.Add(new GonyeItem(GonyeSecenek.Yok, "Yok (düz)"));
            c.Items.Add(new GonyeItem(GonyeSecenek.TekUc, "Tek uç gönye"));
            c.Items.Add(new GonyeItem(GonyeSecenek.CiftUc, "Çift uç gönye"));
            c.Items.Add(new GonyeItem(GonyeSecenek.Kanca90, "90° kanca"));
            c.Items.Add(new GonyeItem(GonyeSecenek.Kanca135, "135° kanca"));
            SelectGonye(c, selected);
            return c;
        }

        private sealed class GonyeItem
        {
            public GonyeSecenek Value { get; }
            public string Text { get; }
            public GonyeItem(GonyeSecenek v, string t) { Value = v; Text = t; }
            public override string ToString() => Text;
        }
    }
}
