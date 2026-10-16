using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

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

    internal sealed class TemelDetayForm : Form
    {
        private static TemelDetayForm _instance;

        private readonly NumericUpDown _ustKot;
        private readonly NumericUpDown _aci;
        private readonly NumericUpDown _sol;
        private readonly NumericUpDown _sag;
        private readonly NumericUpDown _hatil;
        private readonly NumericUpDown _hatilGen;
        private readonly NumericUpDown _doseme;
        private readonly NumericUpDown _ust;
        private readonly NumericUpDown _alt;
        private readonly NumericUpDown _gen;
        private readonly NumericUpDown _uzunluk;
        private readonly NumericUpDown _ustSag;
        private readonly CheckBox _solKademe;
        private readonly CheckBox _sagKademe;
        private readonly NumericUpDown _pas;
        private readonly ComboBox _ustCap;
        private readonly NumericUpDown _ustAralik;
        private readonly ComboBox _altCap;
        private readonly NumericUpDown _altAralik;
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
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = AcadPaletteTheme.Text;
            Font = new SdFont("Segoe UI", 8.5f);
            KeyPreview = true;

            const int formW = 492;
            int y = 8;
            Section("Kesit", ref y);
            _pas = Cell(0, y, "Paspayı", "cm", 2, 10, 0.5m, 1, (decimal)TemelDetaySettings.PaspayiCm);
            _ustKot = Cell(1, y, "Üst kot (yüksek)", "cm", -5000, 5000, 1, 0, (decimal)TemelDetaySettings.TemelUstKotuCm);
            y += 28;
            _solKademe = new CheckBox { Text = "Sol kademe", Checked = TemelDetaySettings.SolKademe, Location = new Point(12, y), AutoSize = true };
            _sagKademe = new CheckBox { Text = "Sağ kademe", Checked = TemelDetaySettings.SagKademe, Location = new Point(252, y), AutoSize = true };
            Controls.Add(_solKademe);
            Controls.Add(_sagKademe);
            y += 28;
            _sol = Cell(0, y, "Sol kademe yük.", "cm", 1, 600, 1, 0, (decimal)TemelDetaySettings.SolCukurCm);
            _sag = Cell(1, y, "Sağ kademe yük.", "cm", 1, 600, 1, 0, (decimal)TemelDetaySettings.SagCukurCm);
            y += 28;
            _ust = Cell(0, y, "Sol üst kalınlık", "cm", 15, 200, 1, 0, (decimal)TemelDetaySettings.UstTemelYuksekligiCm);
            _ustSag = Cell(1, y, "Sağ üst kalınlık", "cm", 15, 200, 1, 0, (decimal)TemelDetaySettings.SagUstTemelYuksekligiCm);
            y += 28;
            _alt = Cell(0, y, "Alt temel kalınlık", "cm", 20, 200, 1, 0, (decimal)TemelDetaySettings.AltTemelYuksekligiCm);
            _gen = Cell(1, y, "İç açıklık", "cm", 40, 2000, 1, 0, (decimal)TemelDetaySettings.TemelGenisligiCm);
            y += 28;
            _aci = Cell(0, y, "Grobeton açısı", "°", 50, 89, 1, 0, (decimal)TemelDetaySettings.GrobetonAcisi);
            _uzunluk = Cell(1, y, "Kademe uzunluğu", "cm", 20, 20000, 1, 0, (decimal)TemelDetaySettings.KademeUzunluguCm);
            y += 32;
            _solKademe.CheckedChanged += (s, e) => UpdateKademeControls();
            _sagKademe.CheckedChanged += (s, e) => UpdateKademeControls();
            UpdateKademeControls();
            Section("Hatıl ve döşeme", ref y);
            _hatil = Cell(0, y, "Hatıl yüksekliği", "cm", 0, 250, 1, 0, (decimal)TemelDetaySettings.HatilYuksekligiCm);
            _hatilGen = Cell(1, y, "Hatıl genişliği", "cm", 15, 80, 1, 0, (decimal)TemelDetaySettings.HatilGenisligiCm);
            y += 28;
            _doseme = Cell(0, y, "Subasman yük.", "cm", 5, 40, 1, 0, (decimal)TemelDetaySettings.SubasmanYuksekligiCm);
            y += 32;

            Section("Donatı", ref y);
            _ustCap = AddCapSlash("Üst donatı", ref y, TemelDetaySettings.UstCapMm,
                TemelDetaySettings.UstAralikCm, out _ustAralik);
            _altCap = AddCapSlash("Alt donatı", ref y, TemelDetaySettings.AltCapMm,
                TemelDetaySettings.AltAralikCm, out _altAralik);

            int yBtn = y + 4;
            ClientSize = new Size(formW, yBtn + 36);
            var btn = MakeButton("Çiz", formW - 168, yBtn, 80, true);
            btn.Click += (s, e) => OnCiz();
            var close = MakeButton("Kapat", formW - 80, yBtn, 68, false);
            close.Click += (s, e) => Close();
            Controls.Add(btn);
            Controls.Add(close);

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
                OnCiz();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void LoadFromSettings()
        {
            _ustKot.Value = Clamp(_ustKot, (decimal)TemelDetaySettings.TemelUstKotuCm);
            _aci.Value = Clamp(_aci, (decimal)TemelDetaySettings.GrobetonAcisi);
            _sol.Value = Clamp(_sol, (decimal)TemelDetaySettings.SolCukurCm);
            _sag.Value = Clamp(_sag, (decimal)TemelDetaySettings.SagCukurCm);
            _hatil.Value = Clamp(_hatil, (decimal)TemelDetaySettings.HatilYuksekligiCm);
            _hatilGen.Value = Clamp(_hatilGen, (decimal)TemelDetaySettings.HatilGenisligiCm);
            _doseme.Value = Clamp(_doseme, (decimal)TemelDetaySettings.SubasmanYuksekligiCm);
            _ust.Value = Clamp(_ust, (decimal)TemelDetaySettings.UstTemelYuksekligiCm);
            _alt.Value = Clamp(_alt, (decimal)TemelDetaySettings.AltTemelYuksekligiCm);
            _gen.Value = Clamp(_gen, (decimal)TemelDetaySettings.TemelGenisligiCm);
            _uzunluk.Value = Clamp(_uzunluk, (decimal)TemelDetaySettings.KademeUzunluguCm);
            _ustSag.Value = Clamp(_ustSag, (decimal)TemelDetaySettings.SagUstTemelYuksekligiCm);
            _solKademe.Checked = TemelDetaySettings.SolKademe;
            _sagKademe.Checked = TemelDetaySettings.SagKademe;
            UpdateKademeControls();
            _pas.Value = Clamp(_pas, (decimal)TemelDetaySettings.PaspayiCm);
            SelectCap(_ustCap, TemelDetaySettings.UstCapMm);
            _ustAralik.Value = Clamp(_ustAralik, TemelDetaySettings.UstAralikCm);
            SelectCap(_altCap, TemelDetaySettings.AltCapMm);
            _altAralik.Value = Clamp(_altAralik, TemelDetaySettings.AltAralikCm);
        }

        private void PushSettings()
        {
            TemelDetaySettings.TemelUstKotuCm = (double)_ustKot.Value;
            TemelDetaySettings.GrobetonAcisi = (double)_aci.Value;
            TemelDetaySettings.SolCukurCm = (double)_sol.Value;
            TemelDetaySettings.SagCukurCm = (double)_sag.Value;
            TemelDetaySettings.HatilYuksekligiCm = (double)_hatil.Value;
            TemelDetaySettings.HatilGenisligiCm = (double)_hatilGen.Value;
            TemelDetaySettings.SubasmanYuksekligiCm = (double)_doseme.Value;
            TemelDetaySettings.UstTemelYuksekligiCm = (double)_ust.Value;
            TemelDetaySettings.AltTemelYuksekligiCm = (double)_alt.Value;
            TemelDetaySettings.TemelGenisligiCm = (double)_gen.Value;
            TemelDetaySettings.KademeUzunluguCm = (double)_uzunluk.Value;
            TemelDetaySettings.SagUstTemelYuksekligiCm = (double)_ustSag.Value;
            TemelDetaySettings.SolKademe = _solKademe.Checked;
            TemelDetaySettings.SagKademe = _sagKademe.Checked;
            TemelDetaySettings.PaspayiCm = (double)_pas.Value;
            TemelDetaySettings.UstCapMm = ParseCap(_ustCap);
            TemelDetaySettings.UstAralikCm = (int)_ustAralik.Value;
            TemelDetaySettings.AltCapMm = ParseCap(_altCap);
            TemelDetaySettings.AltAralikCm = (int)_altAralik.Value;
            TemelDetaySettings.Save();
        }

        private void UpdateKademeControls()
        {
            _sol.Enabled = _ust.Enabled = _solKademe.Checked;
            _sag.Enabled = _ustSag.Enabled = _sagKademe.Checked;
        }
        private void OnCiz()
        {
            if (_starting) return;
            PushSettings();
            if (TemelDetaySettings.PaspayiCm * 2.0 + 2.0 > TemelDetaySettings.AltTemelYuksekligiCm)
            {
                MessageBox.Show(
                    "Paspayı, alt temel yüksekliğine sığmıyor.",
                    "Temel Detayı",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            if (!TemelDetaySettings.SolKademe && !TemelDetaySettings.SagKademe)
            {
                MessageBox.Show("Sol veya sağ kademeden en az birini seçin.", "Temel Detayı");
                return;
            }
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

        private void Section(string title, ref int y)
        {
            var bar = new Panel
            {
                Location = new Point(12, y),
                Size = new Size(468, 22),
                BackColor = AcadPaletteTheme.BgAlt
            };
            bar.Controls.Add(new Label
            {
                Text = title,
                Location = new Point(8, 3),
                AutoSize = true,
                ForeColor = AcadPaletteTheme.Accent,
                BackColor = Color.Transparent,
                Font = new SdFont(Font, FontStyle.Bold)
            });
            Controls.Add(bar);
            y += 28;
        }

        private NumericUpDown Cell(int col, int y, string label, string unit,
            decimal min, decimal max, decimal inc, int decimals, decimal val)
        {
            int x = 12 + col * 240;
            Controls.Add(new Label
            {
                Text = label,
                Location = new Point(x, y + 3),
                AutoSize = false,
                Size = new Size(108, 18),
                ForeColor = AcadPaletteTheme.TextMuted,
                BackColor = Color.Transparent
            });
            var n = new NumericUpDown
            {
                Location = new Point(x + 110, y),
                Size = new Size(58, 22),
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
            Controls.Add(n);
            Controls.Add(new Label
            {
                Text = unit,
                Location = new Point(x + 172, y + 3),
                AutoSize = true,
                ForeColor = AcadPaletteTheme.TextMuted,
                BackColor = Color.Transparent
            });
            return n;
        }

        private ComboBox AddCapSlash(string label, ref int y, int selectedMm, int aralikCm, out NumericUpDown aralik)
        {
            Controls.Add(new Label
            {
                Text = label,
                Location = new Point(12, y + 3),
                AutoSize = false,
                Size = new Size(108, 18),
                ForeColor = AcadPaletteTheme.TextMuted,
                BackColor = Color.Transparent
            });
            var c = new ComboBox
            {
                Location = new Point(122, y),
                Size = new Size(52, 22),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = AcadPaletteTheme.InputBg,
                ForeColor = AcadPaletteTheme.Text,
                FlatStyle = FlatStyle.Flat
            };
            foreach (int d in new[] { 8, 10, 12, 14, 16, 18, 20, 22, 25 })
                c.Items.Add(d.ToString(CultureInfo.InvariantCulture));
            SelectCap(c, selectedMm);
            Controls.Add(c);
            Controls.Add(new Label
            {
                Text = "/",
                Location = new Point(180, y + 3),
                AutoSize = true,
                ForeColor = AcadPaletteTheme.Text,
                BackColor = Color.Transparent
            });
            aralik = new NumericUpDown
            {
                Location = new Point(196, y),
                Size = new Size(52, 22),
                Minimum = 5,
                Maximum = 40,
                Increment = 1,
                DecimalPlaces = 0,
                BackColor = AcadPaletteTheme.InputBg,
                ForeColor = AcadPaletteTheme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            aralik.Value = Clamp(aralik, aralikCm);
            YardimciUi.StripUpDownButtons(aralik);
            Controls.Add(aralik);
            y += 28;
            return c;
        }

        private static int ParseCap(ComboBox cmb)
        {
            if (cmb.SelectedItem is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                return v;
            return 16;
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
                BackColor = AcadPaletteTheme.BgAlt,
                ForeColor = accent ? AcadPaletteTheme.Accent : AcadPaletteTheme.Text,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = AcadPaletteTheme.InputBorder;
            b.FlatAppearance.MouseOverBackColor = AcadPaletteTheme.BgHover;
            return b;
        }
    }
}
