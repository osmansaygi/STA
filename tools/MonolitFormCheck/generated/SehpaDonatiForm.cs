using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;
using SdPoint = System.Drawing.Point;
using SdSize = System.Drawing.Size;

namespace ST4Yardimci
{
    internal enum SehpaDonatiTip
    {
        RadyeTemel = 0,
        MantarDoseme = 1
    }

    internal sealed class SehpaDonatiProfile
    {
        public double KalinlikCm;
        public int CapMm;
        public double UstKotM;
        public int MinGenislikCm;
        /// <summary>Radye / döşeme alanı (m²). 1 adet/m² ile çarpılır.</summary>
        public double AlanM2;
        /// <summary>Paspayı (cm). Radye varsayılan 5, mantar 3.</summary>
        public double PaspayiCm;

        public static SehpaDonatiProfile CreateRadyeDefault() => new SehpaDonatiProfile
        {
            KalinlikCm = 50,
            CapMm = 12,
            UstKotM = -0.80,
            MinGenislikCm = 30,
            AlanM2 = 322,
            PaspayiCm = 5
        };

        public static SehpaDonatiProfile CreateMantarDefault() => new SehpaDonatiProfile
        {
            KalinlikCm = 25,
            CapMm = 10,
            UstKotM = 0,
            MinGenislikCm = 20,
            AlanM2 = 322,
            PaspayiCm = 3
        };
    }

    /// <summary>
    /// Sehpa donatısı — %LocalAppData%\ST4Yardimci\sehpa_donati.ini
    /// Radye ve mantar profilleri ayrı saklanır.
    /// </summary>
    internal static class SehpaDonatiSettings
    {
        private static readonly object Sync = new object();
        private static bool _loaded;
        private static SehpaDonatiProfile _radye = SehpaDonatiProfile.CreateRadyeDefault();
        private static SehpaDonatiProfile _mantar = SehpaDonatiProfile.CreateMantarDefault();

        public static SehpaDonatiTip Tip { get; set; } = SehpaDonatiTip.RadyeTemel;

        private static SehpaDonatiProfile Active =>
            Tip == SehpaDonatiTip.MantarDoseme ? _mantar : _radye;

        public static SehpaDonatiProfile ProfileFor(SehpaDonatiTip tip) =>
            tip == SehpaDonatiTip.MantarDoseme ? _mantar : _radye;

        public static double KalinlikCm { get => Active.KalinlikCm; set => Active.KalinlikCm = value; }
        public static int CapMm { get => Active.CapMm; set => Active.CapMm = value; }
        /// <summary>Döşeme / temel üst kotu (m).</summary>
        public static double UstKotM { get => Active.UstKotM; set => Active.UstKotM = value; }
        public static int MinGenislikCm { get => Active.MinGenislikCm; set => Active.MinGenislikCm = value; }
        /// <summary>Radye temel / döşeme alanı (m²).</summary>
        public static double AlanM2 { get => Active.AlanM2; set => Active.AlanM2 = value; }
        /// <summary>Paspayı (cm).</summary>
        public static double PaspayiCm { get => Active.PaspayiCm; set => Active.PaspayiCm = value; }

        /// <summary>Alt ayak (gönye) boyu, her iki uç (cm).</summary>
        public const double AyakCm = 15.0;

        /// <summary>
        /// Radye: kalınlık − (paspayı + 2 cm).
        /// Mantar: kalınlık − 2×paspayı − 2 cm.
        /// </summary>
        public static double SehpaYuksekligiCm
        {
            get
            {
                double h = Tip == SehpaDonatiTip.MantarDoseme
                    ? KalinlikCm - 2.0 * PaspayiCm - 2.0
                    : KalinlikCm - (PaspayiCm + 2.0);
                return h < 1 ? 1 : h;
            }
        }

        private static string StorePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ST4Yardimci");
                return Path.Combine(dir, "sehpa_donati.ini");
            }
        }

        public static void EnsureLoaded()
        {
            lock (Sync)
            {
                if (_loaded) return;
                _loaded = true;
                try { LoadUnlocked(); }
                catch { }
            }
        }

        public static void Save()
        {
            EnsureLoaded();
            lock (Sync)
            {
                try
                {
                    string path = StorePath;
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        TestStore.CreateDirectory(dir);

                    var sb = new StringBuilder();
                    sb.AppendLine("; ST4 Yardimci — Sehpa donatisi (Radye + Mantar)");
                    Write(sb, "Tip", Tip == SehpaDonatiTip.MantarDoseme ? "Mantar" : "Radye");
                    WriteProfile(sb, "Radye", _radye);
                    WriteProfile(sb, "Mantar", _mantar);
                    TestStore.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                }
                catch { }
            }
        }

        private static void WriteProfile(StringBuilder sb, string prefix, SehpaDonatiProfile p)
        {
            if (p == null) return;
            Write(sb, prefix + "_KalinlikCm", p.KalinlikCm);
            Write(sb, prefix + "_CapMm", p.CapMm);
            Write(sb, prefix + "_UstKotM", p.UstKotM);
            Write(sb, prefix + "_MinGenislikCm", p.MinGenislikCm);
            Write(sb, prefix + "_AlanM2", p.AlanM2);
            Write(sb, prefix + "_PaspayiCm", p.PaspayiCm);
        }

        private static void Write(StringBuilder sb, string key, double v)
        {
            sb.Append(key).Append('=')
                .Append(v.ToString("0.####", CultureInfo.InvariantCulture))
                .AppendLine();
        }

        private static void Write(StringBuilder sb, string key, int v)
        {
            sb.Append(key).Append('=').Append(v).AppendLine();
        }

        private static void Write(StringBuilder sb, string key, string v)
        {
            sb.Append(key).Append('=').Append(v ?? "").AppendLine();
        }

        private static void LoadUnlocked()
        {
            string path = StorePath;
            if (!TestStore.Exists(path)) return;

            foreach (string raw in TestStore.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line[0] == ';' || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                if (key.Equals("Tip", StringComparison.OrdinalIgnoreCase))
                {
                    Tip = val.StartsWith("M", StringComparison.OrdinalIgnoreCase)
                        ? SehpaDonatiTip.MantarDoseme
                        : SehpaDonatiTip.RadyeTemel;
                    continue;
                }

                SehpaDonatiProfile p = null;
                string field = key;
                if (key.StartsWith("Radye_", StringComparison.OrdinalIgnoreCase))
                {
                    p = _radye;
                    field = key.Substring(6);
                }
                else if (key.StartsWith("Mantar_", StringComparison.OrdinalIgnoreCase))
                {
                    p = _mantar;
                    field = key.Substring(7);
                }
                if (p == null) continue;
                ApplyField(p, field, val);
            }
        }

        private static void ApplyField(SehpaDonatiProfile p, string key, string val)
        {
            if (key.Equals("KalinlikCm", StringComparison.OrdinalIgnoreCase) && TryD(val, out double kal))
                p.KalinlikCm = kal;
            else if (key.Equals("CapMm", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cap))
                p.CapMm = cap;
            else if (key.Equals("UstKotM", StringComparison.OrdinalIgnoreCase) && TryD(val, out double kot))
                p.UstKotM = kot;
            else if (key.Equals("MinGenislikCm", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int g))
                p.MinGenislikCm = g;
            else if (key.Equals("AlanM2", StringComparison.OrdinalIgnoreCase) && TryD(val, out double alan))
                p.AlanM2 = alan;
            else if (key.Equals("PaspayiCm", StringComparison.OrdinalIgnoreCase) && TryD(val, out double pas))
                p.PaspayiCm = pas;
        }

        private static bool TryD(string s, out double v)
        {
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return true;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v);
        }
    }

    /// <summary>Sehpa donatısı formu — radye / mantar. Enter çiz, Esc kapat.</summary>
    internal sealed class SehpaDonatiForm : Beykent.Monolit.MonolitForm
    {
        private const int FormW = 300;
        private const int Pad = 12;

        private static SehpaDonatiForm _instance;

        private readonly RadioButton _rbRadye;
        private readonly RadioButton _rbMantar;
        private readonly Label _lblKalinlik;
        private readonly Label _lblAlan;
        private readonly NumericUpDown _numKalinlik;
        private readonly ComboBox _cmbCap;
        private readonly NumericUpDown _numUstKot;
        private readonly NumericUpDown _numGenislik;
        private readonly NumericUpDown _numAlan;
        private readonly NumericUpDown _numPas;
        private bool _starting;
        private bool _suppressTipSwitch;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("SEHPADONATI")) return;
            SehpaDonatiSettings.EnsureLoaded();
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new SehpaDonatiForm();
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

            BaFormSession.Attach("SEHPADONATI", _instance);
            BaFormSession.EndPick("SEHPADONATI");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("SEHPADONATI");
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
                
                BaFormSession.BeginPick("SEHPADONATI");
                _instance.Visible = false;
            }
            catch { }
        }

        private SehpaDonatiForm()
        {
            Text = "Sehpa Donatısı";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            Font = new SdFont("Segoe UI", 9f);
            KeyPreview = true;

            int y = 10;
            _rbRadye = MakeRadio("Radye temel", Pad, y);
            _rbMantar = MakeRadio("Mantar döşeme", Pad + 130, y);
            _suppressTipSwitch = true;
            if (SehpaDonatiSettings.Tip == SehpaDonatiTip.MantarDoseme)
                _rbMantar.Checked = true;
            else
                _rbRadye.Checked = true;
            _suppressTipSwitch = false;
            _rbRadye.CheckedChanged += OnTipRadioChanged;
            _rbMantar.CheckedChanged += OnTipRadioChanged;
            Controls.Add(_rbRadye);
            Controls.Add(_rbMantar);
            y += 28;

            _lblKalinlik = MakeLabel("Temel kalınlığı", Pad, y + 2);
            Controls.Add(_lblKalinlik);
            _numKalinlik = MakeNum(150, y, 70, 10, 400, 1, 0, (decimal)SehpaDonatiSettings.KalinlikCm);
            Controls.Add(_numKalinlik);
            Controls.Add(MakeUnit("cm", 226, y + 2));
            y += 30;

            Controls.Add(MakeLabel("Paspayı", Pad, y + 2));
            _numPas = MakeNum(150, y, 70, 1, 15, 0.5m, 1, (decimal)SehpaDonatiSettings.PaspayiCm);
            Controls.Add(_numPas);
            Controls.Add(MakeUnit("cm", 226, y + 2));
            y += 30;

            Controls.Add(MakeLabel("Donatı çapı", Pad, y + 2));
            _cmbCap = MakeCapCombo(150, y, SehpaDonatiSettings.CapMm);
            Controls.Add(_cmbCap);
            Controls.Add(MakeUnit("mm", 226, y + 2));
            y += 30;

            Controls.Add(MakeLabel("Üst kot", Pad, y + 2));
            _numUstKot = MakeNum(150, y, 70, -8000, 20000, 5m, 2, (decimal)(SehpaDonatiSettings.UstKotM * 100.0));
            Controls.Add(_numUstKot);
            Controls.Add(MakeUnit("cm", 226, y + 2));
            y += 32;

            Controls.Add(MakeLabel("Min genişlik", Pad, y + 2));
            _numGenislik = MakeNum(150, y, 70, 5, 400, 1, 0, SehpaDonatiSettings.MinGenislikCm);
            Controls.Add(_numGenislik);
            Controls.Add(MakeUnit("cm", 226, y + 2));
            y += 32;

            _lblAlan = MakeLabel("Radye alanı", Pad, y + 2);
            Controls.Add(_lblAlan);
            _numAlan = MakeNum(150, y, 70, 0.1m, 1000000, 1, 1, (decimal)SehpaDonatiSettings.AlanM2);
            Controls.Add(_numAlan);
            Controls.Add(MakeUnit("m²", 226, y + 2));
            y += 32;

            var btnRun = MakeButton("Sehpa çiz", Pad, y, 120, true);
            btnRun.Click += (s, e) => OnRun();
            var btnClose = MakeButton("Kapat", Pad + 128, y, 80, false);
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnRun);
            Controls.Add(btnClose);
            y += 36;

            ClientSize = new SdSize(FormW, y);
            UpdateKalinlikLabel();

            FormClosing += (s, e) =>
            {
                TryPushQuiet();
                SehpaDonatiSettings.Save();
            };
            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                {
                    _instance = null;
                    BaFormSession.Release("SEHPADONATI");
                }
            };

            try
            {
                Location = new SdPoint(Screen.PrimaryScreen.WorkingArea.Left + 140,
                    Screen.PrimaryScreen.WorkingArea.Top + 80);
            }
            catch
            {
                StartPosition = FormStartPosition.CenterScreen;
            }

            // Kompakt, içerik odaklı form düzeni.
            var ui = BeginCompact(310);
            ui.Section(null).Options(_rbRadye, _rbMantar);
            ui.Section("Kesit ve sehpa").Field(_lblKalinlik, _numKalinlik).Field("Paspayı · cm", _numPas)
                .Field("Çap · mm / üst kot · cm", _cmbCap, _numUstKot)
                .Field("En az genişlik · cm", _numGenislik).Field(_lblAlan, _numAlan);
            ui.Footer(btnRun, btnClose);
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
                OnRun();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void UpdateKalinlikLabel()
        {
            if (_lblKalinlik == null) return;
            bool mantar = _rbMantar != null && _rbMantar.Checked;
            _lblKalinlik.Text = mantar ? "Döşeme kalınlığı · cm" : "Temel kalınlığı · cm";
            if (_lblAlan != null)
                _lblAlan.Text = mantar ? "Döşeme alanı · m²" : "Radye alanı · m²";
        }

        private void OnTipRadioChanged(object sender, EventArgs e)
        {
            if (_suppressTipSwitch) return;
            var rb = sender as RadioButton;
            if (rb == null || !rb.Checked) return;

            SehpaDonatiTip newTip = _rbMantar.Checked
                ? SehpaDonatiTip.MantarDoseme
                : SehpaDonatiTip.RadyeTemel;
            SehpaDonatiTip oldTip = SehpaDonatiSettings.Tip;
            if (newTip == oldTip)
            {
                UpdateKalinlikLabel();
                return;
            }

            PushQuietTo(oldTip);
            SehpaDonatiSettings.Tip = newTip;
            LoadFieldsFromActive();
            UpdateKalinlikLabel();
            try { SehpaDonatiSettings.Save(); } catch { }
        }

        private void LoadFromSettings()
        {
            _suppressTipSwitch = true;
            if (SehpaDonatiSettings.Tip == SehpaDonatiTip.MantarDoseme)
                _rbMantar.Checked = true;
            else
                _rbRadye.Checked = true;
            _suppressTipSwitch = false;
            LoadFieldsFromActive();
            UpdateKalinlikLabel();
        }

        private void LoadFieldsFromActive()
        {
            _numKalinlik.Value = Clamp(_numKalinlik, (decimal)SehpaDonatiSettings.KalinlikCm);
            SelectCap(_cmbCap, SehpaDonatiSettings.CapMm);
            _numUstKot.Value = Clamp(_numUstKot, (decimal)(SehpaDonatiSettings.UstKotM * 100.0));
            _numGenislik.Value = Clamp(_numGenislik, SehpaDonatiSettings.MinGenislikCm);
            _numAlan.Value = Clamp(_numAlan, (decimal)SehpaDonatiSettings.AlanM2);
            _numPas.Value = Clamp(_numPas, (decimal)SehpaDonatiSettings.PaspayiCm);
        }

        private bool TryPush()
        {
            double kal = (double)_numKalinlik.Value;
            if (kal < 10 || kal > 400)
            {
                Warn("Kalınlık geçersiz (cm).");
                return false;
            }
            SehpaDonatiSettings.Tip = _rbMantar.Checked
                ? SehpaDonatiTip.MantarDoseme
                : SehpaDonatiTip.RadyeTemel;
            SehpaDonatiSettings.KalinlikCm = kal;
            SehpaDonatiSettings.CapMm = ParseCap(_cmbCap);
            SehpaDonatiSettings.UstKotM = (double)_numUstKot.Value / 100.0;
            SehpaDonatiSettings.MinGenislikCm = (int)_numGenislik.Value;
            SehpaDonatiSettings.AlanM2 = (double)_numAlan.Value;
            SehpaDonatiSettings.PaspayiCm = (double)_numPas.Value;
            return true;
        }

        private void TryPushQuiet()
        {
            try
            {
                SehpaDonatiSettings.Tip = _rbMantar.Checked
                    ? SehpaDonatiTip.MantarDoseme
                    : SehpaDonatiTip.RadyeTemel;
                PushQuietTo(SehpaDonatiSettings.Tip);
            }
            catch { }
        }

        private void PushQuietTo(SehpaDonatiTip tip)
        {
            var p = SehpaDonatiSettings.ProfileFor(tip);
            if (p == null) return;
            double kal = (double)_numKalinlik.Value;
            if (kal >= 10 && kal <= 400)
                p.KalinlikCm = kal;
            p.CapMm = ParseCap(_cmbCap);
            p.UstKotM = (double)_numUstKot.Value / 100.0;
            p.MinGenislikCm = (int)_numGenislik.Value;
            double alan = (double)_numAlan.Value;
            if (alan >= 0.1 && alan <= 1000000)
                p.AlanM2 = alan;
            double pas = (double)_numPas.Value;
            if (pas >= 1 && pas <= 15)
                p.PaspayiCm = pas;
        }

        private void OnRun()
        {
            if (_starting) return;
            if (!TryPush()) return;
            SehpaDonatiSettings.Save();
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                _starting = true;
                HideTemporarily();
                doc.SendStringToExecute("SEHPADONATICIZ ", true, false, false);
            }
            catch
            {
                _starting = false;
            }
        }

        private static void Warn(string msg)
        {
            try
            {
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nSehpa: " + msg);
            }
            catch { }
            MessageBox.Show(msg, "Sehpa Donatısı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static int ParseCap(ComboBox cmb)
        {
            if (cmb?.SelectedItem is string s && int.TryParse(s, out int v))
                return v;
            return 12;
        }

        private static void SelectCap(ComboBox cmb, int mm)
        {
            string s = mm.ToString(CultureInfo.InvariantCulture);
            int i = cmb.Items.IndexOf(s);
            cmb.SelectedIndex = i >= 0 ? i : 0;
        }

        private static decimal Clamp(NumericUpDown n, decimal v)
        {
            if (v < n.Minimum) return n.Minimum;
            if (v > n.Maximum) return n.Maximum;
            return v;
        }

        private RadioButton MakeRadio(string text, int x, int y)
        {
            return new RadioButton
            {
                Text = text,
                Location = new SdPoint(x, y),
                AutoSize = true,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                BackColor = Beykent.Monolit.StandardFormTheme.Bg
            };
        }

        private Label MakeLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new SdPoint(x, y),
                AutoSize = true,
                ForeColor = Beykent.Monolit.StandardFormTheme.TextMuted,
                BackColor = Beykent.Monolit.StandardFormTheme.Bg
            };
        }

        private Label MakeUnit(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new SdPoint(x, y),
                AutoSize = true,
                ForeColor = Beykent.Monolit.StandardFormTheme.TextMuted,
                BackColor = Beykent.Monolit.StandardFormTheme.Bg
            };
        }

        private NumericUpDown MakeNum(int x, int y, int w, decimal min, decimal max, decimal inc, int decimals, decimal val)
        {
            var n = new NumericUpDown
            {
                Location = new SdPoint(x, y),
                Size = new SdSize(w, 22),
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

        private ComboBox MakeCapCombo(int x, int y, int selectedMm)
        {
            var c = new ComboBox
            {
                Location = new SdPoint(x, y),
                Size = new SdSize(70, 22),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                FlatStyle = FlatStyle.Flat,
                Font = Font
            };
            foreach (int d in new[] { 8, 10, 12, 14, 16, 18, 20 })
                c.Items.Add(d.ToString(CultureInfo.InvariantCulture));
            SelectCap(c, selectedMm);
            return c;
        }

        private Button MakeButton(string text, int x, int y, int w, bool accent)
        {
            var b = new Button
            {
                Text = text,
                Location = new SdPoint(x, y),
                Size = new SdSize(w, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Beykent.Monolit.StandardFormTheme.BgAlt,
                ForeColor = accent ? Beykent.Monolit.StandardFormTheme.Accent : Beykent.Monolit.StandardFormTheme.Text,
                Font = accent ? new SdFont("Segoe UI", 9f, System.Drawing.FontStyle.Bold) : Font,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = Beykent.Monolit.StandardFormTheme.InputBorder;
            b.FlatAppearance.BorderSize = 1;
            return b;
        }
    }
}
