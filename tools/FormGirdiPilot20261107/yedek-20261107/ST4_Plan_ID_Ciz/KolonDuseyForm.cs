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
        /// <summary>true = 1/100 paftaya konacak: açılım (yazı/ölçü/antet dahil) ×2.</summary>
        public static bool Pafta100 { get; set; }

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

        /// <summary>
        /// Perde başlığında Hcr→normal ℓu farkı &gt; 20 cm ise ara katlarda sabit ortalama ℓu.
        /// Yalnız perde / poligon kol başlığı; kolon formu değişmez.
        /// </summary>
        public static bool BaslikGecisAktif { get; set; }

        /// <summary>Hcr üstündeki geçiş kat sayısı (hepsi aynı ara ℓu). 1–10.</summary>
        public static int BaslikGecisKatSayisi { get; set; } = 1;

        /// <summary>
        /// Kolon-kiriş birleşim bölgesi etriyeleri: false = normal aralık (sj = 10 cm),
        /// true = sıklaştırma bölgesi aralığı (10 cm'yi aşmaz).
        /// </summary>
        public static bool KirisBolgesiSiklastir { get; set; }

        /// <summary>
        /// Tüm kolon boyunca sıklaştırma aralığında etriye (orta ve bindirme bölgesi dahil).
        /// Birleşim bölgesi de sıklaştırılır.
        /// </summary>
        public static bool TumKolonSiklastir { get; set; }

        public static bool KatKatCiz => KatModu == 1 || KatModu == 2;
        public static bool BenzerKatBirlestir => KatModu == 2;

        static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var saved = PlanFormSettings.Load("kolondusey");
            Olcek25 = saved.Bool("Olcek25", Olcek25);
            Pafta100 = saved.Bool("Pafta100", Pafta100);
            KatModu = saved.Int("KatModu", KatModu, 0, 2);
            KatSinirla = saved.Bool("KatSinirla", KatSinirla);
            SonKatAdi = saved.Text("SonKatAdi", SonKatAdi);
            BaslikGecisAktif = saved.Bool("BaslikGecisAktif", BaslikGecisAktif);
            BaslikGecisKatSayisi = saved.Int("BaslikGecisKatSayisi", BaslikGecisKatSayisi, 1, 10);
            KirisBolgesiSiklastir = saved.Bool("KirisBolgesiSiklastir", KirisBolgesiSiklastir);
            TumKolonSiklastir = saved.Bool("TumKolonSiklastir", TumKolonSiklastir);
        }

        public static void Save()
        {
            EnsureLoaded();
            PlanFormSettings.Load("kolondusey").Set("Olcek25", Olcek25).Set("Pafta100", Pafta100).Set("KatModu", KatModu)
                .Set("KatSinirla", KatSinirla).Set("SonKatAdi", SonKatAdi)
                .Set("BaslikGecisAktif", BaslikGecisAktif)
                .Set("BaslikGecisKatSayisi", BaslikGecisKatSayisi)
                .Set("KirisBolgesiSiklastir", KirisBolgesiSiklastir)
                .Set("TumKolonSiklastir", TumKolonSiklastir).Save();
        }

        /// <summary>KOLONDUSEY / aplikasyon formları ortak: kolon kiriş bölgesi etriye aralığı.</summary>
        public static ComboBox CreateKirisBolgesiEtriyeCombo()
        {
            EnsureLoaded();
            var cmb = new ComboBox
            {
                Size = new Size(190, 22),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                FlatStyle = FlatStyle.Flat
            };
            cmb.Items.Add("Normal aralık");
            cmb.Items.Add("Kiriş bölgesi sıklaştırılsın");
            cmb.Items.Add("Tüm kolon sıklaştırılsın");
            cmb.SelectedIndex = TumKolonSiklastir ? 2 : (KirisBolgesiSiklastir ? 1 : 0);
            return cmb;
        }

        public static void PushKirisBolgesiFrom(ComboBox cmb)
        {
            if (cmb == null) return;
            EnsureLoaded();
            TumKolonSiklastir = cmb.SelectedIndex == 2;
            KirisBolgesiSiklastir = cmb.SelectedIndex >= 1;
            Save();
        }

        /// <summary>KOLONDUSEY / aplikasyon formları ortak: başlık geçiş tik + kat sayısı.</summary>
        public static void CreateBaslikGecisControls(out CheckBox chk, out NumericUpDown num)
        {
            EnsureLoaded();
            chk = new CheckBox
            {
                Text = "Başlık kademeli geçiş",
                Location = new Point(12, 0),
                AutoSize = true,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                BackColor = Color.Transparent,
                Checked = BaslikGecisAktif
            };
            num = new NumericUpDown
            {
                Location = new Point(90, 0),
                Size = new Size(80, 22),
                Minimum = 1,
                Maximum = 10,
                DecimalPlaces = 0,
                Value = Math.Max(1, Math.Min(10, BaslikGecisKatSayisi)),
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                TextAlign = HorizontalAlignment.Right,
                ThousandsSeparator = false,
                Enabled = chk.Checked
            };
        }

        /// <summary>num yalnız chk açıkken ve chk.Enabled iken aktif.</summary>
        public static void WireBaslikGecisEnable(CheckBox chk, NumericUpDown num)
        {
            if (chk == null || num == null) return;
            void Sync() { num.Enabled = chk.Enabled && chk.Checked; }
            chk.CheckedChanged += (s, e) => Sync();
            chk.EnabledChanged += (s, e) => Sync();
            Sync();
        }

        public static void PushBaslikGecisFrom(CheckBox chk, NumericUpDown num)
        {
            if (chk == null || num == null) return;
            EnsureLoaded();
            BaslikGecisAktif = chk.Checked;
            BaslikGecisKatSayisi = (int)num.Value;
            Save();
        }
    }

    /// <summary>
    /// KOLONDUSEY modal form: ölçek + kat alma + kat sınırla.
    /// </summary>
    internal sealed class KolonDuseyForm : Beykent.Monolit.MonolitForm
    {
        private readonly ComboBox _cmbOlcek;
        private readonly RadioButton[] _pafta;
        private readonly CheckBox _chkTum;
        private readonly CheckBox _chkAyri;
        private readonly CheckBox _chkBenzer;
        private readonly CheckBox _chkSinirla;
        private readonly CheckBox _chkBaslikGecis;
        private readonly NumericUpDown _numBaslikGecisKat;
        private readonly ComboBox _cmbKirisBolgesi;
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
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Beykent.Monolit.StandardFormTheme.Text;
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
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
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

            KolonDuseySettings.CreateBaslikGecisControls(out _chkBaslikGecis, out _numBaslikGecisKat);
            KolonDuseySettings.WireBaslikGecisEnable(_chkBaslikGecis, _numBaslikGecisKat);
            _chkBaslikGecis.Location = new Point(12, y);
            Controls.Add(_chkBaslikGecis);
            y += 26;
            _numBaslikGecisKat.Location = new Point(90, y);
            Controls.Add(_numBaslikGecisKat);
            y += 30;

            _cmbKirisBolgesi = KolonDuseySettings.CreateKirisBolgesiEtriyeCombo();
            _cmbKirisBolgesi.Location = new Point(90, y);
            Controls.Add(_cmbKirisBolgesi);
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
            var ui = BeginCompact(320);
            _pafta = BeykentOrtak.PaftaOlcek.Secenekler(KolonDuseySettings.Pafta100);
            ui.Section("Çizim").Field("Ölçek", _cmbOlcek);
            ui.Section("Pafta ölçeği").Options(_pafta);
            ui.Tip(_chkTum, _chkTum.Text); ui.Tip(_chkAyri, _chkAyri.Text); ui.Tip(_chkBenzer, _chkBenzer.Text);
            _chkTum.Text = "Tüm katlar"; _chkAyri.Text = "Kat kat"; _chkBenzer.Text = "Benzer katlar";
            ui.Section("Katların çizimi").Options(_chkTum, _chkAyri, _chkBenzer).Choices(_chkSinirla);
            ui.Section("Kolon etriye sıklaştırma").Field("Aralık", _cmbKirisBolgesi);
            ui.Section("Perde başlığı")
                .Choices(_chkBaslikGecis)
                .Field("Geçiş kat sayısı", _numBaslikGecisKat);
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
            if (_pafta != null) KolonDuseySettings.Pafta100 = _pafta[1].Checked;
            if (_chkBenzer.Checked) KolonDuseySettings.KatModu = 2;
            else if (_chkAyri.Checked) KolonDuseySettings.KatModu = 1;
            else KolonDuseySettings.KatModu = 0;
            KolonDuseySettings.KatSinirla = _chkSinirla.Checked;
            KolonDuseySettings.PushBaslikGecisFrom(_chkBaslikGecis, _numBaslikGecisKat);
            KolonDuseySettings.PushKirisBolgesiFrom(_cmbKirisBolgesi);
        }

        private static Label MkLabel(string text, int x, int y, int w) => new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = false,
            Size = new Size(w, 18),
            ForeColor = Beykent.Monolit.StandardFormTheme.TextMuted,
            BackColor = Color.Transparent
        };

        private static CheckBox MkCheck(string text, int x, int y, bool on) => new CheckBox
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Beykent.Monolit.StandardFormTheme.Text,
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
                BackColor = Beykent.Monolit.StandardFormTheme.BgAlt,
                ForeColor = accent ? Beykent.Monolit.StandardFormTheme.Accent : Beykent.Monolit.StandardFormTheme.Text,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Beykent.Monolit.StandardFormTheme.InputBorder;
            b.FlatAppearance.MouseOverBackColor = Beykent.Monolit.StandardFormTheme.BgHover;
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
            KolonDuseySettings.EnsureLoaded();
            _items = items ?? new List<(int fi, string name, double elev)>();
            Text = "Kat sınırla — son kat";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Beykent.Monolit.StandardFormTheme.Text;
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
                ForeColor = Beykent.Monolit.StandardFormTheme.TextMuted,
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
                ForeColor = Beykent.Monolit.StandardFormTheme.TextMuted,
                BackColor = Color.Transparent
            });
            y += 28;

            var btnOk = MkBtn("Tamam", 12, y, 100, true);
            btnOk.Click += (s, e) =>
            {
                if (_cmbSon.SelectedIndex < 0) return;
                KolonDuseySettings.SonKatAdi = SelectedSonKatAdi;
                KolonDuseySettings.Save();
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
            var ui = BeginCompact(300);
            ui.Section(null).Field("Son kat", _cmbSon).Hint("Seçilen kat ve altı çizilir.");
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
            BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
            ForeColor = Beykent.Monolit.StandardFormTheme.Text,
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
                BackColor = Beykent.Monolit.StandardFormTheme.BgAlt,
                ForeColor = accent ? Beykent.Monolit.StandardFormTheme.Accent : Beykent.Monolit.StandardFormTheme.Text,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Beykent.Monolit.StandardFormTheme.InputBorder;
            b.FlatAppearance.MouseOverBackColor = Beykent.Monolit.StandardFormTheme.BgHover;
            return b;
        }
    }
}
