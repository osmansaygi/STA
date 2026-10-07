using System;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    /// <summary>
    /// Temel çukuru. Şablondaki CIZGI (BEYKENT) açıklamaları form alanıdır.
    /// Varsayılanlar TEMEL CUKURU.dwg ölçülerinden.
    /// </summary>
    internal static class TemelCekuruSettings
    {
        public const double TemplateGrobetonAcisi = 80;
        public const double TemplateTemelYuksekligiCm = 100;
        /// <summary>Şablon: hatıl üstünden perde dibine 150 cm (50+100). Resimdeki toplam ölçü.</summary>
        public const double TemplateCukurDerinligiCm = 150;
        public const double TemplateHatilYuksekligiCm = 50;
        /// <summary>İç yüzden dışa hatıl genişliği. Şablonda 20 cm.</summary>
        public const double TemplateHatilGenisligiCm = 20;
        /// <summary>Subasman döşemesi. Şablonda hatıl üstünden 10 cm.</summary>
        public const double TemplateSubasmanYuksekligiCm = 10;
        public const double TemplateUstTemelYuksekligiCm = 40;
        public const double TemplateAltTemelYuksekligiCm = 40;
        /// <summary>İki perde iç yüzü arası. Şablonda 200 cm.</summary>
        public const double TemplateTemelGenisligiCm = 200;
        /// <summary>Şablondaki paspayı. Çizimde 3 cm.</summary>
        public const double TemplatePaspayiCm = 3;
        public const string TemplateUstDonati = "14\u00F816/20";
        public const string TemplateAltDonati = "16\u00F816/20";
        public const int TemplateUstCapMm = 16;
        public const int TemplateUstAralikCm = 20;
        public const int TemplateAltCapMm = 16;
        public const int TemplateAltAralikCm = 20;
        /// <summary>Temel üst kotu (cm). Şablonda -300 cm = -3.00 m (hatıl altı).</summary>
        public const double TemplateTemelUstKotuCm = -300;

        public static double GrobetonAcisi { get; set; } = TemplateGrobetonAcisi;
        /// <summary>Hatıl üstünden perde dibine (cm). Dairedeki toplam düşey ölçü.</summary>
        public static double CukurDerinligiCm { get; set; } = TemplateCukurDerinligiCm;

        /// <summary>Hatıl altından perde dibine. Çukur derinliği eksi hatıl.</summary>
        public static double StemCm
        {
            get
            {
                double s = CukurDerinligiCm - HatilYuksekligiCm;
                return s < 1 ? 1 : s;
            }
        }
        public static double HatilYuksekligiCm { get; set; } = TemplateHatilYuksekligiCm;
        public static double HatilGenisligiCm { get; set; } = TemplateHatilGenisligiCm;
        public static double SubasmanYuksekligiCm { get; set; } = TemplateSubasmanYuksekligiCm;
        public static double UstTemelYuksekligiCm { get; set; } = TemplateUstTemelYuksekligiCm;
        public static double AltTemelYuksekligiCm { get; set; } = TemplateAltTemelYuksekligiCm;
        public static double TemelGenisligiCm { get; set; } = TemplateTemelGenisligiCm;
        /// <summary>Diğer doğrultudaki çukur genişliği. İkinci kesit bununla çizilir.</summary>
        public static double DigerGenislikCm { get; set; } = TemplateTemelGenisligiCm;
        /// <summary>Paspayı (cm). Şablondaki çizim 3 cm.</summary>
        public static double PaspayiCm { get; set; } = 3;
        public static double TemelUstKotuCm { get; set; } = TemplateTemelUstKotuCm;
        public static int UstCapMm { get; set; } = TemplateUstCapMm;
        public static int UstAralikCm { get; set; } = TemplateUstAralikCm;
        public static int AltCapMm { get; set; } = TemplateAltCapMm;
        public static int AltAralikCm { get; set; } = TemplateAltAralikCm;

        static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var m = BaIni.Read("temelcekuru.ini");
            GrobetonAcisi = BaIni.GetD(m, "GrobetonAcisi", GrobetonAcisi);
            HatilYuksekligiCm = BaIni.GetD(m, "HatilYuksekligiCm", HatilYuksekligiCm);
            HatilGenisligiCm = BaIni.GetD(m, "HatilGenisligiCm", HatilGenisligiCm);
            SubasmanYuksekligiCm = BaIni.GetD(m, "SubasmanYuksekligiCm", SubasmanYuksekligiCm);
            if (m.ContainsKey("CukurDerinligiCm"))
                CukurDerinligiCm = BaIni.GetD(m, "CukurDerinligiCm", CukurDerinligiCm);
            else if (m.ContainsKey("TemelYuksekligiCm"))
                CukurDerinligiCm = BaIni.GetD(m, "TemelYuksekligiCm", TemplateTemelYuksekligiCm) + HatilYuksekligiCm;
            UstTemelYuksekligiCm = BaIni.GetD(m, "UstTemelYuksekligiCm", UstTemelYuksekligiCm);
            AltTemelYuksekligiCm = BaIni.GetD(m, "AltTemelYuksekligiCm", AltTemelYuksekligiCm);
            TemelGenisligiCm = BaIni.GetD(m, "TemelGenisligiCm", TemelGenisligiCm);
            DigerGenislikCm = BaIni.GetD(m, "DigerGenislikCm", DigerGenislikCm);
            PaspayiCm = BaIni.GetD(m, "PaspayiCm", PaspayiCm);
            TemelUstKotuCm = BaIni.GetD(m, "TemelUstKotuCm", TemelUstKotuCm);
            UstCapMm = BaIni.GetI(m, "UstCapMm", UstCapMm);
            UstAralikCm = BaIni.GetI(m, "UstAralikCm", UstAralikCm);
            AltCapMm = BaIni.GetI(m, "AltCapMm", AltCapMm);
            AltAralikCm = BaIni.GetI(m, "AltAralikCm", AltAralikCm);
            if (!m.ContainsKey("UstCapMm"))
            {
                int cap = 0, ar = 0;
                if (TryParseCapSlash(BaIni.Get(m, "UstDonati", null), ref cap, ref ar))
                {
                    UstCapMm = cap;
                    UstAralikCm = ar;
                }
            }
            if (!m.ContainsKey("AltCapMm"))
            {
                int cap = 0, ar = 0;
                if (TryParseCapSlash(BaIni.Get(m, "AltDonati", null), ref cap, ref ar))
                {
                    AltCapMm = cap;
                    AltAralikCm = ar;
                }
            }
        }

        static bool TryParseCapSlash(string text, ref int cap, ref int aralik)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            Match match = Regex.Match(text, @"\u00F8\s*(\d+)\s*/\s*(\d+)");
            if (!match.Success) return false;
            cap = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            aralik = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            return cap > 0 && aralik > 0;
        }

        public static void Save()
        {
            EnsureLoaded();
            BaIni.Write("temelcekuru.ini",
                "GrobetonAcisi=" + BaIni.N(GrobetonAcisi),
                "CukurDerinligiCm=" + BaIni.N(CukurDerinligiCm),
                "HatilYuksekligiCm=" + BaIni.N(HatilYuksekligiCm),
                "HatilGenisligiCm=" + BaIni.N(HatilGenisligiCm),
                "SubasmanYuksekligiCm=" + BaIni.N(SubasmanYuksekligiCm),
                "UstTemelYuksekligiCm=" + BaIni.N(UstTemelYuksekligiCm),
                "AltTemelYuksekligiCm=" + BaIni.N(AltTemelYuksekligiCm),
                "TemelGenisligiCm=" + BaIni.N(TemelGenisligiCm),
                "DigerGenislikCm=" + BaIni.N(DigerGenislikCm),
                "PaspayiCm=" + BaIni.N(PaspayiCm),
                "TemelUstKotuCm=" + BaIni.N(TemelUstKotuCm),
                "UstCapMm=" + UstCapMm.ToString(CultureInfo.InvariantCulture),
                "UstAralikCm=" + UstAralikCm.ToString(CultureInfo.InvariantCulture),
                "AltCapMm=" + AltCapMm.ToString(CultureInfo.InvariantCulture),
                "AltAralikCm=" + AltAralikCm.ToString(CultureInfo.InvariantCulture));
        }

        public static string CapSlash(int capMm, int aralikCm)
        {
            return "\u00F8" + capMm.ToString(CultureInfo.InvariantCulture)
                + "/" + aralikCm.ToString(CultureInfo.InvariantCulture);
        }
    }

    internal sealed class TemelCekuruForm : Form
    {
        private static TemelCekuruForm _instance;

        private readonly NumericUpDown _ustKot;
        private readonly NumericUpDown _aci;
        private readonly NumericUpDown _temel;
        private readonly NumericUpDown _hatil;
        private readonly NumericUpDown _hatilGen;
        private readonly NumericUpDown _doseme;
        private readonly NumericUpDown _ust;
        private readonly NumericUpDown _alt;
        private readonly NumericUpDown _gen;
        private readonly NumericUpDown _diger;
        private readonly NumericUpDown _pas;
        private readonly ComboBox _ustCap;
        private readonly NumericUpDown _ustAralik;
        private readonly ComboBox _altCap;
        private readonly NumericUpDown _altAralik;
        private bool _starting;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("TEMELCEKURU")) return;
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new TemelCekuruForm();
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
            BaFormSession.Attach("TEMELCEKURU", _instance);
            BaFormSession.EndPick("TEMELCEKURU");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("TEMELCEKURU");
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
                BaFormSession.BeginPick("TEMELCEKURU");
                _instance.Visible = false;
            }
            catch { }
        }

        private TemelCekuruForm()
        {
            TemelCekuruSettings.EnsureLoaded();
            Text = "Temel Çukuru";
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
            _pas = Cell(0, y, "Paspayı", "cm", 2, 10, 0.5m, 1, (decimal)TemelCekuruSettings.PaspayiCm);
            _ustKot = Cell(1, y, "Temel üst kotu", "cm", -5000, 5000, 1, 0, (decimal)TemelCekuruSettings.TemelUstKotuCm);
            y += 28;
            _temel = Cell(0, y, "Çukur derinliği", "cm", 80, 600, 1, 0, (decimal)TemelCekuruSettings.CukurDerinligiCm);
            _aci = Cell(1, y, "Grobeton açısı", "°", 50, 89, 1, 0, (decimal)TemelCekuruSettings.GrobetonAcisi);
            y += 28;
            _ust = Cell(0, y, "Üst temel yük.", "cm", 15, 200, 1, 0, (decimal)TemelCekuruSettings.UstTemelYuksekligiCm);
            _alt = Cell(1, y, "Alt temel yük.", "cm", 20, 200, 1, 0, (decimal)TemelCekuruSettings.AltTemelYuksekligiCm);
            y += 28;
            _gen = Cell(0, y, "Çukur genişliği", "cm", 40, 2000, 1, 0, (decimal)TemelCekuruSettings.TemelGenisligiCm);
            _diger = Cell(1, y, "Çukur eni", "cm", 40, 2000, 1, 0, (decimal)TemelCekuruSettings.DigerGenislikCm);
            y += 32;

            Section("Hatıl ve döşeme", ref y);
            _hatil = Cell(0, y, "Hatıl yüksekliği", "cm", 0, 250, 1, 0, (decimal)TemelCekuruSettings.HatilYuksekligiCm);
            _hatilGen = Cell(1, y, "Hatıl genişliği", "cm", 15, 80, 1, 0, (decimal)TemelCekuruSettings.HatilGenisligiCm);
            y += 28;
            _doseme = Cell(0, y, "Subasman yük.", "cm", 5, 40, 1, 0, (decimal)TemelCekuruSettings.SubasmanYuksekligiCm);
            y += 32;

            Section("Donatı", ref y);
            _ustCap = AddCapSlash("Üst donatı", ref y, TemelCekuruSettings.UstCapMm,
                TemelCekuruSettings.UstAralikCm, out _ustAralik);
            _altCap = AddCapSlash("Alt donatı", ref y, TemelCekuruSettings.AltCapMm,
                TemelCekuruSettings.AltAralikCm, out _altAralik);

            int yBtn = y + 4;
            ClientSize = new Size(formW, yBtn + 36);
            var plan = MakeButton("Temel planından al", 12, yBtn, 148, false);
            plan.Click += (s, e) => OnPlan();
            var btn = MakeButton("Çiz", formW - 168, yBtn, 80, true);
            btn.Click += (s, e) => OnCiz();
            var close = MakeButton("Kapat", formW - 80, yBtn, 68, false);
            close.Click += (s, e) => Close();
            Controls.Add(plan);
            Controls.Add(btn);
            Controls.Add(close);

            FormClosing += (s, e) => { try { PushSettings(); } catch { } };
            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                {
                    _instance = null;
                    BaFormSession.Release("TEMELCEKURU");
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
            _ustKot.Value = Clamp(_ustKot, (decimal)TemelCekuruSettings.TemelUstKotuCm);
            _aci.Value = Clamp(_aci, (decimal)TemelCekuruSettings.GrobetonAcisi);
            _temel.Value = Clamp(_temel, (decimal)TemelCekuruSettings.CukurDerinligiCm);
            _hatil.Value = Clamp(_hatil, (decimal)TemelCekuruSettings.HatilYuksekligiCm);
            _hatilGen.Value = Clamp(_hatilGen, (decimal)TemelCekuruSettings.HatilGenisligiCm);
            _doseme.Value = Clamp(_doseme, (decimal)TemelCekuruSettings.SubasmanYuksekligiCm);
            _ust.Value = Clamp(_ust, (decimal)TemelCekuruSettings.UstTemelYuksekligiCm);
            _alt.Value = Clamp(_alt, (decimal)TemelCekuruSettings.AltTemelYuksekligiCm);
            _gen.Value = Clamp(_gen, (decimal)TemelCekuruSettings.TemelGenisligiCm);
            _diger.Value = Clamp(_diger, (decimal)TemelCekuruSettings.DigerGenislikCm);
            _pas.Value = Clamp(_pas, (decimal)TemelCekuruSettings.PaspayiCm);
            SelectCap(_ustCap, TemelCekuruSettings.UstCapMm);
            _ustAralik.Value = Clamp(_ustAralik, TemelCekuruSettings.UstAralikCm);
            SelectCap(_altCap, TemelCekuruSettings.AltCapMm);
            _altAralik.Value = Clamp(_altAralik, TemelCekuruSettings.AltAralikCm);
        }

        private void PushSettings()
        {
            TemelCekuruSettings.TemelUstKotuCm = (double)_ustKot.Value;
            TemelCekuruSettings.GrobetonAcisi = (double)_aci.Value;
            TemelCekuruSettings.CukurDerinligiCm = (double)_temel.Value;
            TemelCekuruSettings.HatilYuksekligiCm = (double)_hatil.Value;
            TemelCekuruSettings.HatilGenisligiCm = (double)_hatilGen.Value;
            TemelCekuruSettings.SubasmanYuksekligiCm = (double)_doseme.Value;
            TemelCekuruSettings.UstTemelYuksekligiCm = (double)_ust.Value;
            TemelCekuruSettings.AltTemelYuksekligiCm = (double)_alt.Value;
            TemelCekuruSettings.TemelGenisligiCm = (double)_gen.Value;
            TemelCekuruSettings.DigerGenislikCm = (double)_diger.Value;
            TemelCekuruSettings.PaspayiCm = (double)_pas.Value;
            TemelCekuruSettings.UstCapMm = ParseCap(_ustCap);
            TemelCekuruSettings.UstAralikCm = (int)_ustAralik.Value;
            TemelCekuruSettings.AltCapMm = ParseCap(_altCap);
            TemelCekuruSettings.AltAralikCm = (int)_altAralik.Value;
            TemelCekuruSettings.Save();
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
                doc.SendStringToExecute("TEMELCEKURUPLAN ", true, false, false);
            }
            catch
            {
                _starting = false;
            }
        }

        private void OnCiz()
        {
            if (_starting) return;
            PushSettings();
            if (TemelCekuruSettings.PaspayiCm * 2.0 + 2.0 > TemelCekuruSettings.AltTemelYuksekligiCm)
            {
                MessageBox.Show(
                    "Paspayı, alt temel yüksekliğine sığmıyor.",
                    "Temel Çukuru",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            if (TemelCekuruSettings.HatilYuksekligiCm > 0.5
                && TemelCekuruSettings.SubasmanYuksekligiCm + 10.0 > TemelCekuruSettings.HatilYuksekligiCm)
            {
                MessageBox.Show(
                    "Subasman yüksekliği, hatıl yüksekliğinin en az 10 cm altında olmalı.",
                    "Temel Çukuru",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            if (TemelCekuruSettings.StemCm + 0.5 < TemelCekuruSettings.UstTemelYuksekligiCm)
            {
                MessageBox.Show(
                    "Çukur derinliği, hatıl yüksekliği + üst temelden küçük olamaz.",
                    "Temel Çukuru",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                _starting = true;
                HideTemporarily();
                doc.SendStringToExecute("TEMELCEKURUCIZ ", true, false, false);
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

        /// <summary>Çap kutusu, sağında «/» ve aralık.</summary>
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
