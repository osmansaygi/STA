using System;
using System.Globalization;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;
using SdPoint = System.Drawing.Point;
using SdSize = System.Drawing.Size;

namespace ST4Yardimci
{
    /// <summary>Hasır donatı ayar formu — tip, standart ebat, bindirme, paspayı.</summary>
    internal sealed class HasirDonatiForm : Form
    {
        private const int Pad = 12;
        private const int FormW = 300;
        private static HasirDonatiForm _instance;

        private readonly ComboBox _cmbTip;
        private readonly NumericUpDown _numBoy;
        private readonly NumericUpDown _numEn;
        private readonly NumericUpDown _numBindirme;
        private readonly NumericUpDown _numPas;
        private readonly RadioButton _rbTek;
        private readonly RadioButton _rbCift;
        private bool _starting;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("HASIRDONATI")) return;
            HasirDonatiSettings.EnsureLoaded();
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new HasirDonatiForm();
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
            BaFormSession.Attach("HASIRDONATI", _instance);
            BaFormSession.EndPick("HASIRDONATI");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("HASIRDONATI");
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
                BaFormSession.BeginPick("HASIRDONATI");
                _instance.Visible = false;
            }
            catch { }
        }

        private HasirDonatiForm()
        {
            Text = "Hasır Donatı";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = AcadPaletteTheme.Text;
            Font = new SdFont("Segoe UI", 9f);
            KeyPreview = true;

            int y = 10;
            _rbTek = MakeRadio("Tek hasır", Pad, y);
            _rbCift = MakeRadio("Çift hasır", Pad + 120, y);
            _rbTek.Checked = !HasirDonatiSettings.CiftHasir;
            _rbCift.Checked = HasirDonatiSettings.CiftHasir;
            Controls.Add(_rbTek);
            Controls.Add(_rbCift);
            y += 26;

            Controls.Add(MakeLabel("Hasır çeşidi", Pad, y + 2));
            _cmbTip = MakeTipCombo(130, y, HasirDonatiSettings.Tip);
            Controls.Add(_cmbTip);
            y += 30;

            Controls.Add(MakeLabel("Standart boy", Pad, y + 2));
            _numBoy = MakeNum(130, y, 70, 50, 2000, 1, 0, (decimal)HasirDonatiSettings.BoyCm);
            Controls.Add(_numBoy);
            Controls.Add(MakeUnit("cm", 206, y + 2));
            y += 28;

            Controls.Add(MakeLabel("Standart en", Pad, y + 2));
            _numEn = MakeNum(130, y, 70, 50, 2000, 1, 0, (decimal)HasirDonatiSettings.EnCm);
            Controls.Add(_numEn);
            Controls.Add(MakeUnit("cm", 206, y + 2));
            y += 28;

            Controls.Add(MakeLabel("Bindirme boyu", Pad, y + 2));
            _numBindirme = MakeNum(130, y, 70, 5, 200, 1, 0, (decimal)HasirDonatiSettings.BindirmeCm);
            Controls.Add(_numBindirme);
            Controls.Add(MakeUnit("cm", 206, y + 2));
            y += 28;

            Controls.Add(MakeLabel("Paspayı", Pad, y + 2));
            _numPas = MakeNum(130, y, 70, 0, 20, 0.5m, 1, (decimal)HasirDonatiSettings.PaspayiCm);
            Controls.Add(_numPas);
            Controls.Add(MakeUnit("cm", 206, y + 2));
            y += 34;

            var btnRun = MakeButton("Hasır çiz", Pad, y, 120, true);
            btnRun.Click += (s, e) => OnRun();
            var btnClose = MakeButton("Kapat", Pad + 128, y, 80, false);
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnRun);
            Controls.Add(btnClose);
            y += 36;

            ClientSize = new SdSize(FormW, y);

            FormClosing += (s, e) =>
            {
                TryPushQuiet();
                HasirDonatiSettings.Save();
            };
            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                {
                    _instance = null;
                    BaFormSession.Release("HASIRDONATI");
                }
            };

            try
            {
                Location = new SdPoint(Screen.PrimaryScreen.WorkingArea.Left + 160,
                    Screen.PrimaryScreen.WorkingArea.Top + 100);
            }
            catch
            {
                StartPosition = FormStartPosition.CenterScreen;
            }
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

        private void LoadFromSettings()
        {
            _rbCift.Checked = HasirDonatiSettings.CiftHasir;
            _rbTek.Checked = !HasirDonatiSettings.CiftHasir;
            SelectTip(_cmbTip, HasirDonatiSettings.Tip);
            _numBoy.Value = Clamp(_numBoy, (decimal)HasirDonatiSettings.BoyCm);
            _numEn.Value = Clamp(_numEn, (decimal)HasirDonatiSettings.EnCm);
            _numBindirme.Value = Clamp(_numBindirme, (decimal)HasirDonatiSettings.BindirmeCm);
            _numPas.Value = Clamp(_numPas, (decimal)HasirDonatiSettings.PaspayiCm);
        }

        private bool TryPush()
        {
            double boy = (double)_numBoy.Value;
            double en = (double)_numEn.Value;
            double bin = (double)_numBindirme.Value;
            if (boy < 50 || en < 50)
            {
                Warn("Standart boy/en geçersiz (cm).");
                return false;
            }
            if (bin >= boy || bin >= en)
            {
                Warn("Bindirme, boy ve en'den küçük olmalı.");
                return false;
            }
            HasirDonatiSettings.Tip = _cmbTip.SelectedItem as string ?? "Q188/188";
            HasirDonatiSettings.BoyCm = boy;
            HasirDonatiSettings.EnCm = en;
            HasirDonatiSettings.BindirmeCm = bin;
            HasirDonatiSettings.PaspayiCm = (double)_numPas.Value;
            HasirDonatiSettings.CiftHasir = _rbCift.Checked;
            return true;
        }

        private void TryPushQuiet()
        {
            try
            {
                HasirDonatiSettings.Tip = _cmbTip.SelectedItem as string ?? HasirDonatiSettings.Tip;
                HasirDonatiSettings.BoyCm = (double)_numBoy.Value;
                HasirDonatiSettings.EnCm = (double)_numEn.Value;
                HasirDonatiSettings.BindirmeCm = (double)_numBindirme.Value;
                HasirDonatiSettings.PaspayiCm = (double)_numPas.Value;
                HasirDonatiSettings.CiftHasir = _rbCift.Checked;
            }
            catch { }
        }

        private void OnRun()
        {
            if (_starting) return;
            if (!TryPush()) return;
            HasirDonatiSettings.Save();
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                _starting = true;
                HideTemporarily();
                doc.SendStringToExecute("HASIRDONATICIZ ", true, false, false);
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
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nHasır: " + msg);
            }
            catch { }
            MessageBox.Show(msg, "Hasır Donatı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static void SelectTip(ComboBox cmb, string tip)
        {
            if (cmb == null) return;
            string t = tip ?? "Q188/188";
            int i = cmb.Items.IndexOf(t);
            if (i < 0)
            {
                for (int k = 0; k < cmb.Items.Count; k++)
                {
                    if (string.Equals(cmb.Items[k] as string, t, StringComparison.OrdinalIgnoreCase))
                    {
                        i = k;
                        break;
                    }
                }
            }
            cmb.SelectedIndex = i >= 0 ? i : Math.Max(0, cmb.Items.IndexOf("Q188/188"));
        }

        private static decimal Clamp(NumericUpDown n, decimal v)
        {
            if (v < n.Minimum) return n.Minimum;
            if (v > n.Maximum) return n.Maximum;
            return v;
        }

        private Label MakeLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new SdPoint(x, y),
                AutoSize = true,
                ForeColor = AcadPaletteTheme.TextMuted,
                BackColor = AcadPaletteTheme.Bg
            };
        }

        private Label MakeUnit(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new SdPoint(x, y),
                AutoSize = true,
                ForeColor = AcadPaletteTheme.TextMuted,
                BackColor = AcadPaletteTheme.Bg
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
                BackColor = AcadPaletteTheme.InputBg,
                ForeColor = AcadPaletteTheme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            n.Value = Clamp(n, val);
            YardimciUi.StripUpDownButtons(n);
            return n;
        }

        private ComboBox MakeTipCombo(int x, int y, string selected)
        {
            var c = new ComboBox
            {
                Location = new SdPoint(x, y),
                Size = new SdSize(140, 22),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = AcadPaletteTheme.InputBg,
                ForeColor = AcadPaletteTheme.Text,
                FlatStyle = FlatStyle.Flat,
                Font = Font
            };
            foreach (string tip in HasirDonatiSettings.TipListesi)
                c.Items.Add(tip);
            SelectTip(c, selected);
            return c;
        }

        private RadioButton MakeRadio(string text, int x, int y)
        {
            return new RadioButton
            {
                Text = text,
                Location = new SdPoint(x, y),
                AutoSize = true,
                ForeColor = AcadPaletteTheme.Text,
                BackColor = AcadPaletteTheme.Bg
            };
        }

        private Button MakeButton(string text, int x, int y, int w, bool accent)
        {
            var b = new Button
            {
                Text = text,
                Location = new SdPoint(x, y),
                Size = new SdSize(w, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = AcadPaletteTheme.BgAlt,
                ForeColor = accent ? AcadPaletteTheme.Accent : AcadPaletteTheme.Text,
                Font = accent ? new SdFont("Segoe UI", 9f, System.Drawing.FontStyle.Bold) : Font,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = AcadPaletteTheme.InputBorder;
            b.FlatAppearance.BorderSize = 1;
            return b;
        }
    }
}
