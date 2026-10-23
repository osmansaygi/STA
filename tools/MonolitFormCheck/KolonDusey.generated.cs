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
    /// <summary>
    /// KOLONDUSEY modal form: ölçek + kat alma + kat sınırla.
    /// </summary>
    internal sealed class KolonDuseyForm : Beykent.Monolit.MonolitForm
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

            int y = 74;
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
        
            // Kompakt, içerik odaklı form düzeni.
            var ui = BeginCompact(330);
            ui.Section("Çizim").Field("Ölçek", _cmbOlcek);
            ui.Tip(_chkTum, _chkTum.Text); ui.Tip(_chkAyri, _chkAyri.Text); ui.Tip(_chkBenzer, _chkBenzer.Text);
            _chkTum.Text = "Tüm katlar"; _chkAyri.Text = "Kat kat"; _chkBenzer.Text = "Benzer katlar";
            ui.Section("Katların çizimi").Options(_chkTum, _chkAyri, _chkBenzer).Choices(_chkSinirla);
            ui.Footer(btnCiz, btnIptal);
            ui.Finish();
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
    internal sealed class KolonDuseyKatSinirForm : Beykent.Monolit.MonolitForm
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
        
            // Kompakt, içerik odaklı form düzeni.
            var ui = BeginCompact(330);
            ui.Section("Kat sınırı").Field("Son kat", _cmbSon).Hint("Seçilen kat ve altı çizilir; üst katlar alınmaz.");
            ui.Footer(btnOk, btnCancel);
            ui.Finish();
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
