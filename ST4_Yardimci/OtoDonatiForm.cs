using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using Beykent.Monolit.Girdi;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    /// <summary>
    /// Oto Donatı — radye / mantar ana alt+üst. Enter/Space → çizim; sağ tık / Esc → çık.
    /// Ortak girdi kuralları (07.11.2026): birimli ölçü kutusu, tek satır donatı (ø sabit), alt durum şeridi.
    /// </summary>
    internal sealed class OtoDonatiForm : Beykent.Monolit.MonolitForm
    {
        private static OtoDonatiForm _instance;

        // Eski cap listesindeki ilk değer — kayıtlı çap uygun değilse bu seçilir (eski SelectCap davranışı).
        private const int VarsayilanCap = 8;
        private static readonly int[] CapListesi = { 8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 32 };

        private readonly SegmentSecim _tip;
        /// <summary>Pafta ölçeği: 1/100'de yazı ve bindirme ölçüsü 1,5× (tip profiline ayrı kaydedilir).</summary>
        private readonly SegmentSecim _olcek;
        private readonly Label _lblKalinlik;
        private readonly OlcuKutusu _txtKalinlik;
        private readonly OlcuKutusu _txtPaspayi;
        private readonly OlcuKutusu _txtUstKot;
        private readonly OlcuKutusu _txtGonyeBoyu;
        private readonly OlcuKutusu _txtAnalizEsigi;
        private readonly DonatiKutusu _ustX;
        private readonly DonatiKutusu _ustY;
        private readonly DonatiKutusu _altX;
        private readonly DonatiKutusu _altY;
        private readonly ComboBox _cmbSehpaCap;
        private readonly GonyeSemasiPanel _gonyeSemasi;
        private readonly CheckBox _chkAltCiz;
        private readonly CheckBox _chkUstCiz;
        private readonly CheckBox _chkSehpa;
        private readonly Button _btnManuel;
        private readonly Button _btnRun;
        private readonly Button _btnClose;
        private readonly GirdiDenetci Denetci = new GirdiDenetci();
        private bool _starting;
        /// <summary>Tip seçimi değişiminde UI↔profil senkronunu bastırmak için.</summary>
        private bool _suppressTipSwitch;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("OTODONATI")) return;
            OtoDonatiSettings.EnsureLoaded();
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new OtoDonatiForm();
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

            BaFormSession.Attach("OTODONATI", _instance);
            BaFormSession.EndPick("OTODONATI");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("OTODONATI");
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

                BaFormSession.BeginPick("OTODONATI");
                _instance.Visible = false;
            }
            catch { }
        }

        public static void ShowAfterPick()
        {
            BaFormSession.EndPick("OTODONATI");
            try
            {
                if (_instance != null && !_instance.IsDisposed)
                    _instance._starting = false;
                ShowOrActivate();
            }
            catch { }
        }

        private OtoDonatiForm()
        {
            Text = "Oto Donatı";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            Font = new SdFont("Segoe UI", 9f);
            KeyPreview = true;

            _tip = new SegmentSecim("Radye temel", "Mantar döşeme");
            _suppressTipSwitch = true;
            _tip.Secili = OtoDonatiSettings.Tip == OtoDonatiTip.MantarDoseme ? 1 : 0;
            _suppressTipSwitch = false;
            _tip.SeciliDegisti += OnTipChanged;
            _olcek = new SegmentSecim("1/50 pafta", "1/100 pafta (×1,5)");
            _olcek.Secili = OtoDonatiSettings.Olcek100 ? 1 : 0;

            // Sınırlar eski TryPushSettings doğrulamasıyla aynı. Üst kot cm gösterilir, UstKotM (metre) kaydedilir.
            _lblKalinlik = new Label { Text = "Temel kalınlığı", AutoSize = true };
            _txtKalinlik = Denetci.Ekle(new OlcuKutusu("Kalınlık", "cm", 1, 500, OtoDonatiSettings.KalinlikCm, 1));
            _txtPaspayi = Denetci.Ekle(new OlcuKutusu("Paspayı", "cm", 0.5, 20, OtoDonatiSettings.PaspayiCm, 1, 0.5));
            _txtUstKot = Denetci.Ekle(new OlcuKutusu("Üst kot", "cm", -10000, 30000, OtoDonatiSettings.UstKotM * 100.0, 2, 5));
            _txtGonyeBoyu = Denetci.Ekle(new OlcuKutusu("Gönye max", "cm", 1, 200, OtoDonatiSettings.GonyeBoyuMaxCm, 1));
            _txtAnalizEsigi = Denetci.Ekle(new OlcuKutusu("Analiz eşiği", "cm", 1, 5000, OtoDonatiSettings.AnalizEsigiCm, 1, 5));
            // Çap ø8–ø32 (eski liste), aralık 5–80 cm (eski doğrulama).
            _ustX = Denetci.Ekle(new DonatiKutusu("Üst X", Cap(OtoDonatiSettings.UstDonatiCapMm), OtoDonatiSettings.UstAralikCm, 8, 32, 5, 80));
            _ustY = Denetci.Ekle(new DonatiKutusu("Üst Y", Cap(OtoDonatiSettings.UstDonatiCapYMm), OtoDonatiSettings.UstAralikYCm, 8, 32, 5, 80));
            _altX = Denetci.Ekle(new DonatiKutusu("Alt X", Cap(OtoDonatiSettings.AltDonatiCapMm), OtoDonatiSettings.AltAralikCm, 8, 32, 5, 80));
            _altY = Denetci.Ekle(new DonatiKutusu("Alt Y", Cap(OtoDonatiSettings.AltDonatiCapYMm), OtoDonatiSettings.AltAralikYCm, 8, 32, 5, 80));

            _gonyeSemasi = new GonyeSemasiPanel { Size = new Size(276, 120) };
            _gonyeSemasi.SetGonye(
                OtoDonatiSettings.UstGonyeSol,
                OtoDonatiSettings.UstGonyeSag,
                OtoDonatiSettings.AltGonyeSol,
                OtoDonatiSettings.AltGonyeSag);
            Controls.Add(_gonyeSemasi);

            _chkAltCiz = MakeCheck("Alt donatı", OtoDonatiSettings.AltDonatiCiz);
            _chkUstCiz = MakeCheck("Üst donatı", OtoDonatiSettings.UstDonatiCiz);
            _chkSehpa = MakeCheck("Sehpa donatısı çiz", OtoDonatiSettings.SehpaDonatisi);
            Controls.Add(_chkAltCiz);
            Controls.Add(_chkUstCiz);
            Controls.Add(_chkSehpa);
            _cmbSehpaCap = MakeCapCombo(OtoDonatiSettings.SehpaCapMm);
            Controls.Add(_cmbSehpaCap);
            _chkSehpa.CheckedChanged += (s, e) => { if (!_chkSehpa.Checked) SehpaAlanlariniGeriAl(); Denetci.Guncelle(); };

            Denetci.HataKurali(() => !_chkAltCiz.Checked && !_chkUstCiz.Checked
                ? "Alt veya üst donatıdan en az biri çizilsin" : null);
            _chkAltCiz.CheckedChanged += (s, e) => Denetci.Guncelle();
            _chkUstCiz.CheckedChanged += (s, e) => Denetci.Guncelle();

            _btnManuel = MakeButton("Manuel donatı at", accent: false);
            _btnManuel.Click += OnManuel;
            _btnRun = MakeButton("Donatı çiz", accent: true);
            _btnRun.Click += OnRun;
            _btnClose = MakeButton("Kapat", accent: false);
            _btnClose.Click += (s, e) => Close();

            MouseDown += OnFormMouseDown;

            UpdateKalinlikLabel();

            FormClosing += (s, e) =>
            {
                // Uyarı vermeden mümkün olanı kaydet; geçersiz alanlar eski değerde kalır.
                TryPushSettingsQuiet();
                OtoDonatiSettings.Save();
            };
            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                {
                    _instance = null;
                    BaFormSession.Release("OTODONATI");
                }
            };

            try
            {
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 120,
                    Screen.PrimaryScreen.WorkingArea.Top + 70);
            }
            catch
            {
                StartPosition = FormStartPosition.CenterScreen;
            }

            var ui = BeginCompact(360);
            ui.Section(null).Control(_tip);
            ui.Section("Pafta ölçeği").Control(_olcek);
            ui.Tip(_olcek, "1/100: donatı yazısı, gönye boyu yazısı ve bindirme ölçüsü 1,5× (kalıp 1/100 gibi).");
            ui.Section("Kesit")
                .Field("Paspayı", _txtPaspayi);
            ui.Section("Donatı  (ø çap / aralık)")
                .Field("Üst X", _ustX)
                .Field("Üst Y", _ustY)
                .Field("Alt X", _altX)
                .Field("Alt Y", _altY)
                .Options(_chkAltCiz, _chkUstCiz);
            ui.Section("Gönye ve analiz")
                .Field("En büyük gönye", _txtGonyeBoyu)
                .Field("Analiz eşiği", _txtAnalizEsigi)
                .Control(_gonyeSemasi, 120);
            ui.Section("Sehpa")
                .Options(_chkSehpa)
                .FieldWhen(() => _chkSehpa.Checked, new Label { Text = "Sehpa çapı" }, _cmbSehpaCap)
                .FieldWhen(() => _chkSehpa.Checked, _lblKalinlik, _txtKalinlik)
                .FieldWhen(() => _chkSehpa.Checked, new Label { Text = "Üst kot" }, _txtUstKot);
            ui.Section(null).Control(Denetci.Serit, 44);
            ui.Tip(_chkAltCiz, "Alt donatı çizilsin"); ui.Tip(_chkUstCiz, "Üst donatı çizilsin");
            ui.Tip(_chkSehpa, "Temel antetinin üstüne sehpa detayı çizilir.");
            ui.Tip(_cmbSehpaCap, "Sehpa donatı çapı (mm) — temel antetinin üstüne sehpa detayı çizilir.");
            ui.Tip(_txtKalinlik, "Yalnız sehpa detayında kullanılır.");
            ui.Tip(_txtUstKot, "Yalnız sehpa detayında, çizimde kot yazısı bulunamazsa kullanılır.");
            ui.Footer(_btnRun, _btnClose, _btnManuel);
            ui.Finish();
            Denetci.Guncelle();
            foreach (Control c in Controls) WireRightClick(c);
        }

        private void WireRightClick(Control c)
        {
            c.MouseDown += OnFormMouseDown;
            foreach (Control child in c.Controls)
                WireRightClick(child);
        }

        private void OnFormMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
                Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            if (keyData == Keys.Enter || keyData == Keys.Return || keyData == Keys.Space)
            {
                if (ActiveControl is Button)
                    return base.ProcessCmdKey(ref msg, keyData);
                if (keyData == Keys.Space &&
                    (ActiveControl is GirdiKutusu || ActiveControl is TextBoxBase || ActiveControl is ComboBox))
                    return base.ProcessCmdKey(ref msg, keyData);
                OnRun(this, EventArgs.Empty);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void UpdateKalinlikLabel()
        {
            if (_lblKalinlik == null) return;
            _lblKalinlik.Text = _tip != null && _tip.Secili == 1
                ? "Döşeme kalınlığı"
                : "Temel kalınlığı";
        }

        /// <summary>Sehpa kapanınca gizlenen hatalı alanlar son geçerli değere döner (gizli hata komutu kilitlemesin).</summary>
        private void SehpaAlanlariniGeriAl()
        {
            if (!_txtKalinlik.Gecerli) _txtKalinlik.Deger = _txtKalinlik.Deger;
            if (!_txtUstKot.Gecerli) _txtUstKot.Deger = _txtUstKot.Deger;
        }

        private OtoDonatiTip SeciliTip => _tip.Secili == 1 ? OtoDonatiTip.MantarDoseme : OtoDonatiTip.RadyeTemel;

        /// <summary>
        /// Tip değişince: eski tipin alanlarını belleğe yaz → Tip güncelle → yeni tipin alanlarını forma yükle.
        /// </summary>
        private void OnTipChanged(object sender, EventArgs e)
        {
            if (_suppressTipSwitch) return;

            OtoDonatiTip newTip = SeciliTip;
            OtoDonatiTip oldTip = OtoDonatiSettings.Tip;
            if (newTip == oldTip)
            {
                UpdateKalinlikLabel();
                return;
            }

            // Seçim zaten yeni tipi gösteriyor; alanları ESKİ profile yaz (Tip henüz değişmedi).
            PushFieldsQuietTo(oldTip);
            OtoDonatiSettings.Tip = newTip;
            LoadFieldsFromActiveProfile();
            UpdateKalinlikLabel();
            try { OtoDonatiSettings.Save(); } catch { }
        }

        private void LoadFromSettings()
        {
            _suppressTipSwitch = true;
            _tip.Secili = OtoDonatiSettings.Tip == OtoDonatiTip.MantarDoseme ? 1 : 0;
            _suppressTipSwitch = false;
            LoadFieldsFromActiveProfile();
            UpdateKalinlikLabel();
        }

        private void LoadFieldsFromActiveProfile()
        {
            _txtKalinlik.Deger = OtoDonatiSettings.KalinlikCm;
            _txtPaspayi.Deger = OtoDonatiSettings.PaspayiCm;
            _txtUstKot.Deger = OtoDonatiSettings.UstKotM * 100.0;
            _txtGonyeBoyu.Deger = OtoDonatiSettings.GonyeBoyuMaxCm;
            _txtAnalizEsigi.Deger = OtoDonatiSettings.AnalizEsigiCm;
            _altX.Ayarla(Cap(OtoDonatiSettings.AltDonatiCapMm), OtoDonatiSettings.AltAralikCm);
            _ustX.Ayarla(Cap(OtoDonatiSettings.UstDonatiCapMm), OtoDonatiSettings.UstAralikCm);
            _altY.Ayarla(Cap(OtoDonatiSettings.AltDonatiCapYMm), OtoDonatiSettings.AltAralikYCm);
            _ustY.Ayarla(Cap(OtoDonatiSettings.UstDonatiCapYMm), OtoDonatiSettings.UstAralikYCm);
            SelectCap(_cmbSehpaCap, OtoDonatiSettings.SehpaCapMm);
            _gonyeSemasi.SetGonye(
                OtoDonatiSettings.UstGonyeSol,
                OtoDonatiSettings.UstGonyeSag,
                OtoDonatiSettings.AltGonyeSol,
                OtoDonatiSettings.AltGonyeSag);
            _chkSehpa.Checked = OtoDonatiSettings.SehpaDonatisi;
            _olcek.Secili = OtoDonatiSettings.Olcek100 ? 1 : 0;
            _chkAltCiz.Checked = OtoDonatiSettings.AltDonatiCiz;
            _chkUstCiz.Checked = OtoDonatiSettings.UstDonatiCiz;
            Denetci.Guncelle();
        }

        private bool TryPushSettings()
        {
            if (!Denetci.Onayla()) return false;

            OtoDonatiSettings.Tip = SeciliTip;
            OtoDonatiSettings.KalinlikCm = _txtKalinlik.Deger;
            OtoDonatiSettings.PaspayiCm = _txtPaspayi.Deger;
            OtoDonatiSettings.UstKotM = _txtUstKot.Deger / 100.0;
            OtoDonatiSettings.GonyeBoyuMaxCm = _txtGonyeBoyu.Deger;
            OtoDonatiSettings.AnalizEsigiCm = _txtAnalizEsigi.Deger;
            OtoDonatiSettings.AltDonatiCapMm = _altX.CapMm;
            OtoDonatiSettings.UstDonatiCapMm = _ustX.CapMm;
            OtoDonatiSettings.AltAralikCm = _altX.AralikCm;
            OtoDonatiSettings.UstAralikCm = _ustX.AralikCm;
            OtoDonatiSettings.AltDonatiCapYMm = _altY.CapMm;
            OtoDonatiSettings.UstDonatiCapYMm = _ustY.CapMm;
            OtoDonatiSettings.AltAralikYCm = _altY.AralikCm;
            OtoDonatiSettings.UstAralikYCm = _ustY.AralikCm;
            OtoDonatiSettings.SehpaCapMm = ParseCap(_cmbSehpaCap);
            OtoDonatiSettings.UstGonyeSol = _gonyeSemasi.UstSol;
            OtoDonatiSettings.UstGonyeSag = _gonyeSemasi.UstSag;
            OtoDonatiSettings.AltGonyeSol = _gonyeSemasi.AltSol;
            OtoDonatiSettings.AltGonyeSag = _gonyeSemasi.AltSag;
            OtoDonatiSettings.AltDonatiCiz = _chkAltCiz.Checked;
            OtoDonatiSettings.UstDonatiCiz = _chkUstCiz.Checked;
            OtoDonatiSettings.SehpaDonatisi = _chkSehpa.Checked;
            OtoDonatiSettings.Olcek100 = _olcek.Secili == 1;
            return true;
        }

        /// <summary>Kapatırken: geçerli alanları yaz, geçersizleri atla (MessageBox yok).</summary>
        private void TryPushSettingsQuiet()
        {
            try
            {
                OtoDonatiSettings.Tip = SeciliTip;
                PushFieldsQuietTo(OtoDonatiSettings.Tip);
            }
            catch { }
        }

        /// <summary>
        /// Form alanlarını belirtilen profile yazar; Tip property'sine dokunmaz.
        /// Tip değiştirirken eski profili kaydetmek için kullanılır. Geçersiz alan profile yazılmaz.
        /// </summary>
        private void PushFieldsQuietTo(OtoDonatiTip tip)
        {
            try
            {
                var p = OtoDonatiSettings.ProfileFor(tip);
                if (p == null) return;
                if (_txtKalinlik.Gecerli) p.KalinlikCm = _txtKalinlik.Deger;
                if (_txtPaspayi.Gecerli) p.PaspayiCm = _txtPaspayi.Deger;
                if (_txtUstKot.Gecerli) p.UstKotM = _txtUstKot.Deger / 100.0;
                if (_txtGonyeBoyu.Gecerli) p.GonyeBoyuMaxCm = _txtGonyeBoyu.Deger;
                if (_txtAnalizEsigi.Gecerli) p.AnalizEsigiCm = _txtAnalizEsigi.Deger;
                if (_altX.Gecerli) { p.AltDonatiCapMm = _altX.CapMm; p.AltAralikCm = _altX.AralikCm; }
                if (_ustX.Gecerli) { p.UstDonatiCapMm = _ustX.CapMm; p.UstAralikCm = _ustX.AralikCm; }
                if (_altY.Gecerli) { p.AltDonatiCapYMm = _altY.CapMm; p.AltAralikYCm = _altY.AralikCm; }
                if (_ustY.Gecerli) { p.UstDonatiCapYMm = _ustY.CapMm; p.UstAralikYCm = _ustY.AralikCm; }
                p.SehpaCapMm = ParseCap(_cmbSehpaCap);
                p.UstGonyeSol = _gonyeSemasi.UstSol;
                p.UstGonyeSag = _gonyeSemasi.UstSag;
                p.AltGonyeSol = _gonyeSemasi.AltSol;
                p.AltGonyeSag = _gonyeSemasi.AltSag;
                p.AltDonatiCiz = _chkAltCiz.Checked;
                p.UstDonatiCiz = _chkUstCiz.Checked;
                p.SehpaDonatisi = _chkSehpa.Checked;
                p.Olcek100 = _olcek.Secili == 1;
            }
            catch { }
        }

        private void OnManuel(object sender, EventArgs e)
        {
            if (_starting) return;
            if (!TryPushSettings()) return;
            OtoDonatiSettings.Save();
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                _starting = true;
                HideTemporarily();
                doc.SendStringToExecute("OTODONATIMANUEL ", true, false, false);
            }
            catch
            {
                _starting = false;
            }
        }

        private void OnRun(object sender, EventArgs e)
        {
            if (_starting) return;
            if (!TryPushSettings()) return;
            OtoDonatiSettings.Save();
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                _starting = true;
                HideTemporarily();
                doc.SendStringToExecute("OTODONATICIZ ", true, false, false);
            }
            catch
            {
                _starting = false;
            }
        }

        /// <summary>Kayıtlı çap ø8–ø32 standart değilse eski liste davranışı gibi ilk çap (ø8) seçilir.</summary>
        private static int Cap(int mm)
            => mm >= 8 && mm <= 32 && Array.IndexOf(DonatiKutusu.StandartCaplar, mm) >= 0 ? mm : VarsayilanCap;

        private static int ParseCap(ComboBox cmb)
        {
            if (cmb.SelectedItem is string s && int.TryParse(s, out int v))
                return v;
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

        private static CheckBox MakeCheck(string text, bool on) => new CheckBox
        {
            Text = text,
            AutoSize = true,
            ForeColor = Beykent.Monolit.StandardFormTheme.Text,
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Standard,
            Checked = on
        };

        private Button MakeButton(string text, bool accent)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = Beykent.Monolit.StandardFormTheme.BgAlt,
                ForeColor = accent ? Beykent.Monolit.StandardFormTheme.Accent : Beykent.Monolit.StandardFormTheme.Text,
                Font = accent ? new SdFont("Segoe UI", 9f, FontStyle.Bold) : Font,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = Beykent.Monolit.StandardFormTheme.InputBorder;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = Beykent.Monolit.StandardFormTheme.BgAlt;
            b.FlatAppearance.MouseDownBackColor = Beykent.Monolit.StandardFormTheme.BgAlt;
            return b;
        }

        private ComboBox MakeCapCombo(int selectedMm)
        {
            var c = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                FlatStyle = FlatStyle.Flat,
                Font = Font
            };
            foreach (int d in CapListesi)
                c.Items.Add(d.ToString(CultureInfo.InvariantCulture));
            SelectCap(c, selectedMm);
            return c;
        }

        /// <summary>
        /// Canlı gönye şeması: tik kapalıysa o uçtaki gönye çizgisi yok.
        /// </summary>
        private sealed class GonyeSemasiPanel : Panel
        {
            private readonly CheckBox _chkUstSol;
            private readonly CheckBox _chkUstSag;
            private readonly CheckBox _chkAltSol;
            private readonly CheckBox _chkAltSag;

            public bool UstSol => _chkUstSol.Checked;
            public bool UstSag => _chkUstSag.Checked;
            public bool AltSol => _chkAltSol.Checked;
            public bool AltSag => _chkAltSag.Checked;

            public GonyeSemasiPanel()
            {
                DoubleBuffered = true;
                BackColor = Beykent.Monolit.StandardFormTheme.Bg;
                BorderStyle = BorderStyle.None;

                _chkUstSol = MakeTick();
                _chkUstSag = MakeTick();
                _chkAltSol = MakeTick();
                _chkAltSag = MakeTick();
                Controls.Add(_chkUstSol);
                Controls.Add(_chkUstSag);
                Controls.Add(_chkAltSol);
                Controls.Add(_chkAltSag);

                EventHandler redraw = (s, e) => Invalidate();
                _chkUstSol.CheckedChanged += redraw;
                _chkUstSag.CheckedChanged += redraw;
                _chkAltSol.CheckedChanged += redraw;
                _chkAltSag.CheckedChanged += redraw;

                Resize += (s, e) => LayoutTicks();
                LayoutTicks();
            }

            public void SetGonye(bool ustSol, bool ustSag, bool altSol, bool altSag)
            {
                _chkUstSol.Checked = ustSol;
                _chkUstSag.Checked = ustSag;
                _chkAltSol.Checked = altSol;
                _chkAltSag.Checked = altSag;
                Invalidate();
            }

            private void LayoutTicks()
            {
                const int tick = 15;
                float midY = Height * 0.5f;
                float ustY = midY - 28;
                float altY = midY + 28;
                int leftX = 4;
                int rightX = Math.Max(leftX + tick, Width - tick - 4);

                _chkUstSol.SetBounds(leftX, (int)Math.Round(ustY - tick * 0.5f), tick, tick);
                _chkUstSag.SetBounds(rightX, (int)Math.Round(ustY - tick * 0.5f), tick, tick);
                _chkAltSol.SetBounds(leftX, (int)Math.Round(altY - tick * 0.5f), tick, tick);
                _chkAltSag.SetBounds(rightX, (int)Math.Round(altY - tick * 0.5f), tick, tick);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(BackColor);

                const int tick = 15;
                float padL = 4 + tick + 8;
                float padR = 4 + tick + 8;
                float x0 = padL;
                float x1 = Width - padR;
                if (x1 - x0 < 60) return;

                float midY = Height * 0.5f;
                float ustY = midY - 28;
                float altY = midY + 28;
                float hook = 22;

                using (var pen = new Pen(Beykent.Monolit.StandardFormTheme.Text, 2f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                })
                using (var font = new SdFont("Segoe UI", 9f, FontStyle.Bold))
                using (var brush = new SolidBrush(Beykent.Monolit.StandardFormTheme.Text))
                {
                    g.DrawLine(pen, x0, ustY, x1, ustY);
                    if (_chkUstSol.Checked)
                        g.DrawLine(pen, x0, ustY, x0, ustY + hook);
                    if (_chkUstSag.Checked)
                        g.DrawLine(pen, x1, ustY, x1, ustY + hook);

                    string ust = "UST DONATI";
                    SizeF usz = g.MeasureString(ust, font);
                    g.DrawString(ust, font, brush,
                        (x0 + x1) * 0.5f - usz.Width * 0.5f,
                        ustY + 3);

                    g.DrawLine(pen, x0, altY, x1, altY);
                    if (_chkAltSol.Checked)
                        g.DrawLine(pen, x0, altY, x0, altY - hook);
                    if (_chkAltSag.Checked)
                        g.DrawLine(pen, x1, altY, x1, altY - hook);

                    string alt = "ALT DONATI";
                    SizeF asz = g.MeasureString(alt, font);
                    g.DrawString(alt, font, brush,
                        (x0 + x1) * 0.5f - asz.Width * 0.5f,
                        altY - asz.Height - 2);
                }
            }

            private static CheckBox MakeTick()
            {
                return new CheckBox
                {
                    Text = string.Empty,
                    AutoSize = false,
                    Size = new Size(15, 15),
                    FlatStyle = FlatStyle.Standard,
                    CheckAlign = ContentAlignment.MiddleCenter,
                    UseVisualStyleBackColor = true
                };
            }
        }
    }
}
