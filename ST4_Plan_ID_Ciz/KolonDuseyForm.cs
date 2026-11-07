using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Beykent.Monolit.Girdi;
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

        /// <summary>Kolon kiriş bölgesi etriye aralığı: 0 normal, 1 kiriş bölgesi, 2 tüm kolon sıklaştırma.</summary>
        public static SegmentSecim CreateKirisBolgesiEtriyeSecim()
        {
            EnsureLoaded();
            return new SegmentSecim("Normal aralık", "Kiriş bölgesi", "Tüm kolon")
            {
                Secili = TumKolonSiklastir ? 2 : (KirisBolgesiSiklastir ? 1 : 0)
            };
        }

        public static void PushKirisBolgesiFrom(SegmentSecim secim)
        {
            if (secim == null) return;
            EnsureLoaded();
            TumKolonSiklastir = secim.Secili == 2;
            KirisBolgesiSiklastir = secim.Secili >= 1;
            Save();
        }

        /// <summary>Başlık geçiş tik + kat sayısı (1–10, tam sayı).</summary>
        public static void CreateBaslikGecisControls(out CheckBox chk, out OlcuKutusu kat)
        {
            EnsureLoaded();
            chk = new CheckBox
            {
                Text = "Başlık kademeli geçiş",
                AutoSize = true,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                BackColor = Color.Transparent,
                Checked = BaslikGecisAktif
            };
            kat = new OlcuKutusu("Geçiş kat sayısı", "kat", 1, 10, 1);
            kat.Deger = Math.Max(1, Math.Min(10, BaslikGecisKatSayisi));
        }

        public static void PushBaslikGecisFrom(CheckBox chk, OlcuKutusu kat)
        {
            if (chk == null || kat == null) return;
            EnsureLoaded();
            BaslikGecisAktif = chk.Checked;
            BaslikGecisKatSayisi = (int)Math.Round(kat.Deger);
            Save();
        }
    }

    /// <summary>
    /// KOLONDUSEY modal form: ölçek + kat alma + kat sınırla.
    /// </summary>
    internal sealed class KolonDuseyForm : Beykent.Monolit.MonolitForm
    {
        private readonly SegmentSecim _olcek;
        private readonly SegmentSecim _pafta;
        private readonly SegmentSecim _katModu;
        private readonly CheckBox _chkSinirla;
        private readonly CheckBox _chkBaslikGecis;
        private readonly OlcuKutusu _baslikGecisKat;
        private readonly SegmentSecim _kirisBolgesi;
        private readonly GirdiDenetci _denetci = new GirdiDenetci();

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

            // Ortak kural 3/4: detay ölçeği (Olcek25) ve pafta ölçeği (Pafta100) ayrı sorulardır; ikisi de segment.
            _olcek = new SegmentSecim("1/50", "1/25") { Secili = KolonDuseySettings.Olcek25 ? 1 : 0 };
            _pafta = new SegmentSecim("1/50 pafta", "1/100 pafta (×2)") { Secili = KolonDuseySettings.Pafta100 ? 1 : 0 };
            // Eski elle dışlanan üç tik (tüm al / ayrı al / benzer birleştir) → tek segment; KatModu 0/1/2 aynı.
            _katModu = new SegmentSecim("Tüm katlar", "Kat kat", "Benzer katlar")
            {
                Secili = Math.Max(0, Math.Min(2, KolonDuseySettings.KatModu))
            };
            _chkSinirla = new CheckBox
            {
                Text = "Kat sınırla",
                AutoSize = true,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                BackColor = Color.Transparent,
                Checked = KolonDuseySettings.KatSinirla
            };
            Controls.Add(_chkSinirla);

            KolonDuseySettings.CreateBaslikGecisControls(out _chkBaslikGecis, out _baslikGecisKat);
            _denetci.Ekle(_baslikGecisKat);
            Controls.Add(_chkBaslikGecis);

            _kirisBolgesi = KolonDuseySettings.CreateKirisBolgesiEtriyeSecim();

            var btnCiz = MkButton("Çiz", 12, 0, 100, true);
            btnCiz.Click += (s, e) => Ciz();
            var btnIptal = MkButton("İptal", 120, 0, 80, false);
            btnIptal.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(btnCiz);
            Controls.Add(btnIptal);
            AcceptButton = btnCiz;
            CancelButton = btnIptal;
            ClientSize = new Size(270, 320);


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

            var ui = BeginCompact(320);
            ui.Section("Katların çizimi").Control(_katModu).Choices(_chkSinirla);
            ui.Tip(_katModu, "Tüm katlar: tek düşey görünüş · Kat kat: her kat ayrı satır · Benzer katlar: benzer katları birleştir");
            ui.Section("Kolon etriye sıklaştırma").Field("Aralık", _kirisBolgesi);
            ui.Section("Perde başlığı")
                .Choices(_chkBaslikGecis)
                .FieldWhen(() => _chkBaslikGecis.Checked, new Label { Text = "Geçiş kat sayısı" }, _baslikGecisKat);
            ui.Section("Çizim").Field("Ölçek", _olcek);
            ui.Section("Pafta ölçeği").Control(_pafta);
            ui.Section(null).Control(_denetci.Serit, 44);
            ui.Footer(btnCiz, btnIptal);
            ui.Finish();
            _denetci.Guncelle();
        }

        private void Ciz()
        {
            if (!_denetci.Onayla()) return;
            PushSettings();
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            if ((keyData == Keys.Enter || keyData == Keys.Return) && !(ActiveControl is Button))
            {
                Ciz();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void PushSettings()
        {
            KolonDuseySettings.Olcek25 = _olcek.Secili == 1;
            KolonDuseySettings.Pafta100 = _pafta.Secili == 1;
            KolonDuseySettings.KatModu = _katModu.Secili;
            KolonDuseySettings.KatSinirla = _chkSinirla.Checked;
            KolonDuseySettings.PushBaslikGecisFrom(_chkBaslikGecis, _baslikGecisKat);
            KolonDuseySettings.PushKirisBolgesiFrom(_kirisBolgesi);
        }

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
