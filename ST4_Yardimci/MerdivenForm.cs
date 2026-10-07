using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    /// <summary>Merdiven beton kesiti ayarları (form açıkken runner buradan okur).</summary>
    internal static class MerdivenSettings
    {
        public static double KatYuksekligiCm { get; set; } = 300;
        public static double AltKotCm { get; set; } = 0;
        /// <summary>Aynı katta benzer merdiven adedi. L= etiketinin önüne çarpan.</summary>
        public static int BenzerAdet { get; set; } = 1;
        /// <summary>Benzer kat sayısı (çizilen kat dahil). Ek çarpan ve kot kopyası.</summary>
        public static int BenzerKat { get; set; } = 1;
        /// <summary>Merdiven plak / döşeme kalınlığı (cm) — kesitte eğik alt yüz.</summary>
        public static double DosemeKalinligiCm { get; set; } = 15;
        /// <summary>Plandan okunamayan kiriş yüksekliği varsayılanı (cm).</summary>
        public static double KirisYuksekligiCm { get; set; } = 50;
        /// <summary>Kesit donatı paspayı (cm).</summary>
        public static double PaspayiCm { get; set; } = 3;
        /// <summary>Ana donatı aralığı (cm).</summary>
        public static double AnaDonatiAralikCm { get; set; } = 20;
        /// <summary>Dağılım (tevzi) işaret aralığı (cm).</summary>
        public static double TevziAralikCm { get; set; } = 20;
        /// <summary>Mesnet yüzünden yatay gömülme (cm).</summary>
        public static double GommeCm { get; set; } = 22;
        public static bool TemelVar { get; set; } = true;
        public static double TemelKalinligiCm { get; set; } = 50;
        public static int AnaDonatiCapMm { get; set; } = 12;
        public static int TevziDonatiCapMm { get; set; } = 8;

        /// <summary>Plandan mod basamak genişliği (cm) — hesaplarda kullanılır.</summary>
        public static double BasamakGenisligiCm { get; set; } = 28;
        /// <summary>Rıht yüksekliği = kat / rıht adedi (gerçek değer; yazıda 2 ondalık).</summary>
        public static double RihtYuksekligiCm { get; set; } = 13.636363636363637;
        /// <summary>Son plandan sayılan rıht adedi (uçlar dahil).</summary>
        public static int RihtAdet { get; set; } = 22;

        static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var m = BaIni.Read("merdiven.ini");
            KatYuksekligiCm = BaIni.GetD(m, "KatYuksekligiCm", KatYuksekligiCm);
            AltKotCm = BaIni.GetD(m, "AltKotCm", AltKotCm);
            BenzerAdet = BaIni.GetI(m, "BenzerAdet", BenzerAdet);
            BenzerKat = BaIni.GetI(m, "BenzerKat", BenzerKat);
            DosemeKalinligiCm = BaIni.GetD(m, "DosemeKalinligiCm", DosemeKalinligiCm);
            KirisYuksekligiCm = BaIni.GetD(m, "KirisYuksekligiCm", KirisYuksekligiCm);
            PaspayiCm = BaIni.GetD(m, "PaspayiCm", PaspayiCm);
            AnaDonatiAralikCm = BaIni.GetD(m, "AnaDonatiAralikCm", AnaDonatiAralikCm);
            TevziAralikCm = BaIni.GetD(m, "TevziAralikCm", TevziAralikCm);
            GommeCm = BaIni.GetD(m, "GommeCm", GommeCm);
            TemelVar = BaIni.GetB(m, "TemelVar", TemelVar);
            TemelKalinligiCm = BaIni.GetD(m, "TemelKalinligiCm", TemelKalinligiCm);
            AnaDonatiCapMm = BaIni.GetI(m, "AnaDonatiCapMm", AnaDonatiCapMm);
            TevziDonatiCapMm = BaIni.GetI(m, "TevziDonatiCapMm", TevziDonatiCapMm);
            BasamakGenisligiCm = BaIni.GetD(m, "BasamakGenisligiCm", BasamakGenisligiCm);
            RihtYuksekligiCm = BaIni.GetD(m, "RihtYuksekligiCm", RihtYuksekligiCm);
            RihtAdet = BaIni.GetI(m, "RihtAdet", RihtAdet);
        }

        public static void Save()
        {
            EnsureLoaded();
            BaIni.Write("merdiven.ini",
                "KatYuksekligiCm=" + BaIni.N(KatYuksekligiCm),
                "AltKotCm=" + BaIni.N(AltKotCm),
                "BenzerAdet=" + BenzerAdet.ToString(CultureInfo.InvariantCulture),
                "BenzerKat=" + BenzerKat.ToString(CultureInfo.InvariantCulture),
                "DosemeKalinligiCm=" + BaIni.N(DosemeKalinligiCm),
                "KirisYuksekligiCm=" + BaIni.N(KirisYuksekligiCm),
                "PaspayiCm=" + BaIni.N(PaspayiCm),
                "AnaDonatiAralikCm=" + BaIni.N(AnaDonatiAralikCm),
                "TevziAralikCm=" + BaIni.N(TevziAralikCm),
                "GommeCm=" + BaIni.N(GommeCm),
                "TemelVar=" + BaIni.B(TemelVar),
                "TemelKalinligiCm=" + BaIni.N(TemelKalinligiCm),
                "AnaDonatiCapMm=" + AnaDonatiCapMm.ToString(CultureInfo.InvariantCulture),
                "TevziDonatiCapMm=" + TevziDonatiCapMm.ToString(CultureInfo.InvariantCulture),
                "BasamakGenisligiCm=" + BaIni.N(BasamakGenisligiCm),
                "RihtYuksekligiCm=" + BaIni.N(RihtYuksekligiCm),
                "RihtAdet=" + RihtAdet.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Merdiven formu — MERDIVEN modal açar; Plandan çiz → form kapanır, komut çizime devam eder.
    /// Çizim bitince form açılmaz; Enter/Space MERDIVEN tekrarlar → form yeniden açılır.
    /// </summary>
    internal sealed class MerdivenForm : Form
    {
        private static MerdivenForm _instance;

        private readonly NumericUpDown _numKat;
        private readonly NumericUpDown _numAltKot;
        private readonly NumericUpDown _numAdet;
        private readonly NumericUpDown _numBenzerKat;
        private readonly NumericUpDown _numDoseme;
        private readonly NumericUpDown _numKirisH;
        private readonly NumericUpDown _numPaspayi;
        private readonly NumericUpDown _numAnaAralik;
        private readonly NumericUpDown _numTevziAralik;
        private readonly CheckBox _chkTemel;
        private readonly NumericUpDown _numTemelKal;
        private readonly ComboBox _cmbAna;
        private readonly ComboBox _cmbTevzi;

        /// <summary>Modal form. true = «Plandan çiz» (ayarlar kaydedildi).</summary>
        public static bool ShowModalAndAcceptDraw()
        {
            if (!BaFormSession.TryEnter("MERDIVEN")) return false;
            Shutdown();
            using (var f = new MerdivenForm())
            {
                BaFormSession.Attach("MERDIVEN", f);
                DialogResult dr;
                try { dr = AcApp.ShowModalDialog(f); }
                catch { dr = f.ShowDialog(); }
                BaFormSession.Release("MERDIVEN");
                return dr == DialogResult.OK;
            }
        }

        public static void Shutdown()
        {
            BaFormSession.Release("MERDIVEN");
            if (_instance == null) return;
            try { _instance.Close(); } catch { }
            _instance = null;
        }

        private MerdivenForm()
        {
            MerdivenSettings.EnsureLoaded();
            Text = "Merdiven";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(280, 280);
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = AcadPaletteTheme.Text;
            Font = new SdFont("Segoe UI", 8.5f);
            KeyPreview = true;
            DialogResult = DialogResult.Cancel;

            int y = 8;
            _numKat = AddRow("Kat yükseklik", "cm", ref y, 50, 600, 1, 0,
                (decimal)MerdivenSettings.KatYuksekligiCm);
            _numAltKot = AddRow("Alt kot", "cm", ref y, -5000, 5000, 1, 0,
                (decimal)MerdivenSettings.AltKotCm);
            _numAdet = AddRow("Adet", "ad", ref y, 1, 50, 1, 0,
                MerdivenSettings.BenzerAdet);
            _numBenzerKat = AddRow("Benzer kat", "kat", ref y, 1, 40, 1, 0,
                MerdivenSettings.BenzerKat);
            _numDoseme = AddRow("Döşeme kalınlık", "cm", ref y, 8, 40, 1, 0,
                (decimal)MerdivenSettings.DosemeKalinligiCm);
            _numKirisH = AddRow("Kiriş yükseklik*", "cm", ref y, 20, 150, 1, 0,
                (decimal)MerdivenSettings.KirisYuksekligiCm);

            _chkTemel = new CheckBox
            {
                Text = "Temel var",
                Location = new Point(10, y + 2),
                AutoSize = true,
                ForeColor = AcadPaletteTheme.Text,
                BackColor = Color.Transparent,
                Checked = MerdivenSettings.TemelVar
            };
            _chkTemel.CheckedChanged += (s, e) =>
            {
                _numTemelKal.Enabled = _chkTemel.Checked;
            };
            Controls.Add(_chkTemel);

            var lblTemel = new Label
            {
                Text = "kalınlık",
                Location = new Point(108, y + 3),
                AutoSize = false,
                Size = new Size(52, 18),
                ForeColor = AcadPaletteTheme.TextMuted,
                BackColor = Color.Transparent
            };
            Controls.Add(lblTemel);
            _numTemelKal = MakeNum(162, y, 58, 10, 200, 1, 0,
                (decimal)MerdivenSettings.TemelKalinligiCm);
            _numTemelKal.Enabled = MerdivenSettings.TemelVar;
            Controls.Add(_numTemelKal);
            Controls.Add(new Label
            {
                Text = "cm",
                Location = new Point(222, y + 3),
                AutoSize = true,
                ForeColor = AcadPaletteTheme.TextMuted,
                BackColor = Color.Transparent
            });
            y += 26;

            _cmbAna = AddCapSlashRow("Ana donatı", ref y, MerdivenSettings.AnaDonatiCapMm,
                (decimal)MerdivenSettings.AnaDonatiAralikCm, out _numAnaAralik);
            _cmbTevzi = AddCapSlashRow("Tevzi donatı", ref y, MerdivenSettings.TevziDonatiCapMm,
                (decimal)MerdivenSettings.TevziAralikCm, out _numTevziAralik);
            _numPaspayi = AddRow("Paspayı", "cm", ref y, 1, 5, 0.5m, 1,
                (decimal)MerdivenSettings.PaspayiCm);

            var hint = new Label
            {
                Text = "Seç → numara + S kesit (+donatı). *kiriş h.",
                Location = new Point(10, y + 2),
                AutoSize = false,
                Size = new Size(260, 18),
                ForeColor = AcadPaletteTheme.TextMuted,
                BackColor = Color.Transparent
            };
            Controls.Add(hint);
            y += 24;

            var btnPlan = MakeButton("Plandan çiz", 10, y, 110, true);
            btnPlan.Click += (s, e) => OnPlan();
            var btnClose = MakeButton("Kapat", 126, y, 70, false);
            btnClose.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(btnPlan);
            Controls.Add(btnClose);
            ClientSize = new Size(280, y + 36);
            AcceptButton = btnPlan;
            CancelButton = btnClose;

            FormClosing += (s, e) => { try { PushSettings(); } catch { } };
            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                    _instance = null;
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
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            if (keyData == Keys.Enter || keyData == Keys.Return)
            {
                OnPlan();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OnPlan()
        {
            PushSettings();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void PushSettings()
        {
            MerdivenSettings.KatYuksekligiCm = (double)_numKat.Value;
            MerdivenSettings.AltKotCm = (double)_numAltKot.Value;
            MerdivenSettings.BenzerAdet = (int)_numAdet.Value;
            MerdivenSettings.BenzerKat = (int)_numBenzerKat.Value;
            MerdivenSettings.DosemeKalinligiCm = (double)_numDoseme.Value;
            MerdivenSettings.KirisYuksekligiCm = (double)_numKirisH.Value;
            MerdivenSettings.TemelVar = _chkTemel.Checked;
            MerdivenSettings.TemelKalinligiCm = (double)_numTemelKal.Value;
            MerdivenSettings.AnaDonatiCapMm = ParseCap(_cmbAna);
            MerdivenSettings.TevziDonatiCapMm = ParseCap(_cmbTevzi);
            MerdivenSettings.AnaDonatiAralikCm = (double)_numAnaAralik.Value;
            MerdivenSettings.TevziAralikCm = (double)_numTevziAralik.Value;
            MerdivenSettings.PaspayiCm = (double)_numPaspayi.Value;
            MerdivenSettings.Save();
        }

        private NumericUpDown AddRow(string label, string unit, ref int y,
            decimal min, decimal max, decimal inc, int decimals, decimal val)
        {
            Controls.Add(new Label
            {
                Text = label,
                Location = new Point(10, y + 3),
                AutoSize = false,
                Size = new Size(110, 18),
                ForeColor = AcadPaletteTheme.TextMuted,
                BackColor = Color.Transparent
            });
            var n = MakeNum(122, y, 58, min, max, inc, decimals, val);
            Controls.Add(n);
            Controls.Add(new Label
            {
                Text = unit,
                Location = new Point(182, y + 3),
                AutoSize = true,
                ForeColor = AcadPaletteTheme.TextMuted,
                BackColor = Color.Transparent
            });
            y += 26;
            return n;
        }

        /// <summary>Çap kutusu, sağında yalnız «/» ve aralık. Birim yazısı yok.</summary>
        private ComboBox AddCapSlashRow(string label, ref int y, int selectedMm, decimal aralikCm,
            out NumericUpDown aralik)
        {
            Controls.Add(new Label
            {
                Text = label,
                Location = new Point(10, y + 3),
                AutoSize = false,
                Size = new Size(110, 18),
                ForeColor = AcadPaletteTheme.TextMuted,
                BackColor = Color.Transparent
            });
            var c = MakeCapCombo(122, y, 52, selectedMm);
            Controls.Add(c);
            Controls.Add(new Label
            {
                Text = "/",
                Location = new Point(176, y + 3),
                AutoSize = true,
                ForeColor = AcadPaletteTheme.Text,
                BackColor = Color.Transparent
            });
            aralik = MakeNum(190, y, 52, 8, 40, 1, 0, aralikCm);
            Controls.Add(aralik);
            y += 26;
            return c;
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

        private static NumericUpDown MakeNum(int x, int y, int w, decimal min, decimal max,
            decimal inc, int decimals, decimal val)
        {
            var n = new NumericUpDown
            {
                Location = new Point(x, y),
                Size = new Size(w, 22),
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

        private static ComboBox MakeCapCombo(int x, int y, int w, int selectedMm)
        {
            var c = new ComboBox
            {
                Location = new Point(x, y),
                Size = new Size(w, 22),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = AcadPaletteTheme.InputBg,
                ForeColor = AcadPaletteTheme.Text,
                FlatStyle = FlatStyle.Flat
            };
            foreach (int d in new[] { 8, 10, 12, 14, 16, 18, 20, 22, 25 })
                c.Items.Add(d.ToString(CultureInfo.InvariantCulture));
            SelectCap(c, selectedMm);
            return c;
        }
    }
}
