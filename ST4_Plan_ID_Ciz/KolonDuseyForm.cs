using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>KOLONDUSEY form seçenekleri — oturum + %LocalAppData%\ST4_Plan_ID_Ciz\kolondusey.ini.</summary>
    internal static class KolonDuseySettings
    {
        /// <summary>true = 1/25, false = 1/50.</summary>
        public static bool Olcek25 { get; set; }

        /// <summary>
        /// 0 = katları tüm al (tek düşey görünüş),
        /// 1 = katları ayrı al (her kat ayrı satır),
        /// 2 = benzer katları birleştir.
        /// </summary>
        public static int KatModu { get; set; }

        /// <summary>true = ST4 sonrası hangi katın son kat sayılacağı sorulur.</summary>
        public static bool KatSinirla { get; set; }

        /// <summary>Son seçilen «son kat» adı (kat sınırla diyaloğu).</summary>
        public static string SonKatAdi { get; set; }

        public static bool KatKatCiz => KatModu == 1 || KatModu == 2;
        public static bool BenzerKatBirlestir => KatModu == 2;

        static bool _loaded;

        static string IniPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ST4_Plan_ID_Ciz");
            try { Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "kolondusey.ini");
        }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string path = IniPath();
                if (!File.Exists(path)) return;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    if (string.Equals(key, "Olcek25", StringComparison.OrdinalIgnoreCase))
                        Olcek25 = val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                    else if (string.Equals(key, "KatModu", StringComparison.OrdinalIgnoreCase)
                             && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int km)
                             && km >= 0 && km <= 2)
                        KatModu = km;
                    else if (string.Equals(key, "KatSinirla", StringComparison.OrdinalIgnoreCase))
                        KatSinirla = val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                    else if (string.Equals(key, "SonKatAdi", StringComparison.OrdinalIgnoreCase))
                        SonKatAdi = val;
                }
            }
            catch { }
        }

        public static void Save()
        {
            EnsureLoaded();
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Olcek25=" + (Olcek25 ? "1" : "0"));
                sb.AppendLine("KatModu=" + KatModu.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("KatSinirla=" + (KatSinirla ? "1" : "0"));
                sb.AppendLine("SonKatAdi=" + (SonKatAdi ?? ""));
                File.WriteAllText(IniPath(), sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }

    /// <summary>
    /// KOLONDUSEY modal form: ölçek + kat alma + kat sınırla.
    /// </summary>
    internal sealed class KolonDuseyForm : Form
    {
        private readonly ComboBox _cmbOlcek;
        private readonly CheckBox _chkTum;
        private readonly CheckBox _chkAyri;
        private readonly CheckBox _chkBenzer;
        private readonly CheckBox _chkSinirla;
        private bool _syncing;

        /// <summary>Modal form. true = Çiz (ayarlar kaydedildi).</summary>
        public static bool ShowModalAndAccept()
        {
            KolonDuseySettings.EnsureLoaded();
            using (var f = new KolonDuseyForm())
            {
                DialogResult dr;
                try { dr = AcApp.ShowModalDialog(f); }
                catch { dr = f.ShowDialog(); }
                return dr == DialogResult.OK;
            }
        }

        /// <summary>
        /// Kat sınırla: son kat seçimi. Seçilen kat ve altındakiler çizilir;
        /// üst katlar yok sayılır (seçilen kat çatı gibi işlenir).
        /// </summary>
        public static bool TryPickSonKat(St4Model model, out HashSet<int> allowed)
        {
            allowed = null;
            KolonDuseySettings.EnsureLoaded();
            if (model?.Floors == null || model.Floors.Count == 0)
                return false;

            var items = new List<(int fi, string name, double elev)>();
            for (int i = 0; i < model.Floors.Count; i++)
            {
                var fl = model.Floors[i];
                if (fl == null) continue;
                string nm = !string.IsNullOrWhiteSpace(fl.Name) ? fl.Name.Trim()
                    : (!string.IsNullOrWhiteSpace(fl.ShortName) ? fl.ShortName.Trim()
                    : ("Kat " + (i + 1)));
                items.Add((i, nm, fl.ElevationM));
            }
            if (items.Count == 0) return false;
            items.Sort((a, b) => a.elev.CompareTo(b.elev));

            using (var f = new KolonDuseyKatSinirForm(items))
            {
                DialogResult dr;
                try { dr = AcApp.ShowModalDialog(f); }
                catch { dr = f.ShowDialog(); }
                if (dr != DialogResult.OK) return false;
                allowed = f.BuildAllowedUpToSonKat();
                if (allowed == null || allowed.Count == 0) return false;
                KolonDuseySettings.SonKatAdi = f.SelectedSonKatAdi;
                KolonDuseySettings.Save();
                return true;
            }
        }

        private KolonDuseyForm()
        {
            KolonDuseySettings.EnsureLoaded();
            Text = "Kolon Düşey";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = AcadPaletteTheme.Text;
            Font = new SdFont("Segoe UI", 8.5f);
            KeyPreview = true;
            DialogResult = DialogResult.Cancel;

            int y = 10;
            Controls.Add(MkLabel("Ölçek", 12, y + 3, 70));
            _cmbOlcek = new ComboBox
            {
                Location = new Point(90, y),
                Size = new Size(160, 22),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = AcadPaletteTheme.InputBg,
                ForeColor = AcadPaletteTheme.Text,
                FlatStyle = FlatStyle.Flat
            };
            _cmbOlcek.Items.Add("1/50");
            _cmbOlcek.Items.Add("1/25");
            _cmbOlcek.SelectedIndex = KolonDuseySettings.Olcek25 ? 1 : 0;
            Controls.Add(_cmbOlcek);
            y += 32;

            Controls.Add(MkLabel("Kat alma", 12, y, 200));
            y += 20;

            _chkTum = MkCheck("Katları tüm al", 12, y, KolonDuseySettings.KatModu == 0);
            _chkTum.CheckedChanged += (s, e) => OnKatTick(_chkTum);
            Controls.Add(_chkTum);
            y += 24;

            _chkAyri = MkCheck("Katları ayrı al", 12, y, KolonDuseySettings.KatModu == 1);
            _chkAyri.CheckedChanged += (s, e) => OnKatTick(_chkAyri);
            Controls.Add(_chkAyri);
            y += 24;

            _chkBenzer = MkCheck("Benzer katları birleştir", 12, y, KolonDuseySettings.KatModu == 2);
            _chkBenzer.CheckedChanged += (s, e) => OnKatTick(_chkBenzer);
            Controls.Add(_chkBenzer);
            y += 26;

            _chkSinirla = MkCheck("Kat sınırla", 12, y, KolonDuseySettings.KatSinirla);
            Controls.Add(_chkSinirla);
            y += 30;

            EnsureOneKatChecked();

            var btnCiz = MkButton("Çiz", 12, y, 100, true);
            btnCiz.Click += (s, e) =>
            {
                PushSettings();
                DialogResult = DialogResult.OK;
                Close();
            };
            var btnIptal = MkButton("İptal", 120, y, 80, false);
            btnIptal.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(btnCiz);
            Controls.Add(btnIptal);
            AcceptButton = btnCiz;
            CancelButton = btnIptal;
            ClientSize = new Size(270, y + 40);

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
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            if (keyData == Keys.Enter || keyData == Keys.Return)
            {
                PushSettings();
                DialogResult = DialogResult.OK;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OnKatTick(CheckBox source)
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                if (source.Checked)
                {
                    if (!ReferenceEquals(source, _chkTum)) _chkTum.Checked = false;
                    if (!ReferenceEquals(source, _chkAyri)) _chkAyri.Checked = false;
                    if (!ReferenceEquals(source, _chkBenzer)) _chkBenzer.Checked = false;
                }
                EnsureOneKatChecked();
            }
            finally
            {
                _syncing = false;
            }
        }

        private void EnsureOneKatChecked()
        {
            if (_chkTum.Checked || _chkAyri.Checked || _chkBenzer.Checked) return;
            _chkTum.Checked = true;
        }

        private void PushSettings()
        {
            KolonDuseySettings.Olcek25 = _cmbOlcek.SelectedIndex == 1;
            if (_chkBenzer.Checked) KolonDuseySettings.KatModu = 2;
            else if (_chkAyri.Checked) KolonDuseySettings.KatModu = 1;
            else KolonDuseySettings.KatModu = 0;
            KolonDuseySettings.KatSinirla = _chkSinirla.Checked;
            KolonDuseySettings.Save();
        }

        private static Label MkLabel(string text, int x, int y, int w) => new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = false,
            Size = new Size(w, 18),
            ForeColor = AcadPaletteTheme.TextMuted,
            BackColor = Color.Transparent
        };

        private static CheckBox MkCheck(string text, int x, int y, bool on) => new CheckBox
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = AcadPaletteTheme.Text,
            BackColor = Color.Transparent,
            Checked = on
        };

        private static Button MkButton(string text, int x, int y, int w, bool accent)
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

    /// <summary>Kat sınırla: hangi kat son kat sayılsın.</summary>
    internal sealed class KolonDuseyKatSinirForm : Form
    {
        private readonly List<(int fi, string name, double elev)> _items;
        private readonly ComboBox _cmbSon;

        public string SelectedSonKatAdi
        {
            get
            {
                int sel = Math.Max(0, Math.Min(_cmbSon.SelectedIndex, _items.Count - 1));
                return _items.Count > 0 ? _items[sel].name : null;
            }
        }

        public KolonDuseyKatSinirForm(List<(int fi, string name, double elev)> items)
        {
            _items = items ?? new List<(int fi, string name, double elev)>();
            Text = "Kat sınırla — son kat";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = AcadPaletteTheme.Text;
            Font = new SdFont("Segoe UI", 8.5f);
            KeyPreview = true;
            DialogResult = DialogResult.Cancel;

            int y = 12;
            Controls.Add(new Label
            {
                Text = "Son kat olarak kabul edilecek kat:",
                Location = new Point(12, y),
                AutoSize = false,
                Size = new Size(280, 18),
                ForeColor = AcadPaletteTheme.TextMuted,
                BackColor = Color.Transparent
            });
            y += 22;

            _cmbSon = MkCombo(12, y, 260);
            for (int i = 0; i < _items.Count; i++)
                _cmbSon.Items.Add(_items[i].name);
            SelectRememberedOrDefault();
            Controls.Add(_cmbSon);
            y += 34;

            Controls.Add(new Label
            {
                Text = "Seçilen kat ve altı çizilir; üst katlar yok sayılır.",
                Location = new Point(12, y),
                AutoSize = false,
                Size = new Size(280, 18),
                ForeColor = AcadPaletteTheme.TextMuted,
                BackColor = Color.Transparent
            });
            y += 28;

            var btnOk = MkBtn("Tamam", 12, y, 100, true);
            btnOk.Click += (s, e) =>
            {
                DialogResult = DialogResult.OK;
                Close();
            };
            var btnCancel = MkBtn("İptal", 120, y, 80, false);
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            ClientSize = new Size(300, y + 40);
        }

        private void SelectRememberedOrDefault()
        {
            if (_cmbSon.Items.Count == 0) return;
            string remember = KolonDuseySettings.SonKatAdi;
            if (!string.IsNullOrWhiteSpace(remember))
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (string.Equals(_items[i].name, remember, StringComparison.OrdinalIgnoreCase))
                    {
                        _cmbSon.SelectedIndex = i;
                        return;
                    }
                }
            }
            if (_cmbSon.Items.Count > 1)
                _cmbSon.SelectedIndex = _cmbSon.Items.Count - 2;
            else
                _cmbSon.SelectedIndex = 0;
        }

        /// <summary>Seçilen son kat + kotu onun altında/eşit olan tüm katlar.</summary>
        public HashSet<int> BuildAllowedUpToSonKat()
        {
            var hs = new HashSet<int>();
            int sel = Math.Max(0, Math.Min(_cmbSon.SelectedIndex, _items.Count - 1));
            if (_items.Count == 0) return hs;
            double zMax = _items[sel].elev;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].elev <= zMax + 1e-6)
                    hs.Add(_items[i].fi);
            }
            hs.Add(_items[sel].fi);
            return hs;
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
                DialogResult = DialogResult.OK;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static ComboBox MkCombo(int x, int y, int w) => new ComboBox
        {
            Location = new Point(x, y),
            Size = new Size(w, 22),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = AcadPaletteTheme.InputBg,
            ForeColor = AcadPaletteTheme.Text,
            FlatStyle = FlatStyle.Flat
        };

        private static Button MkBtn(string text, int x, int y, int w, bool accent)
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
