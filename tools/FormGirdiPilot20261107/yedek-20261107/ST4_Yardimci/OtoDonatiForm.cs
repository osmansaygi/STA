using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    /// <summary>
    /// Oto Donatı — radye / mantar ana alt+üst. Enter/Space → çizim; sağ tık / Esc → çık.
    /// </summary>
    internal sealed class OtoDonatiForm : Beykent.Monolit.MonolitForm
    {
        private const int FormW = 300;
        private const int Pad = 12;
        private const int Gap = 10;
        private const int FieldH = 24;

        private static OtoDonatiForm _instance;

        private readonly int _fieldW;
        private readonly int _col2X;

        private readonly RadioButton _rbRadye;
        private readonly RadioButton _rbMantar;
        private readonly Label _lblKalinlik;
        private readonly TextBox _txtKalinlik;
        private readonly TextBox _txtPaspayi;
        private readonly TextBox _txtUstKot;
        private readonly TextBox _txtGonyeBoyu;
        private readonly TextBox _txtAnalizEsigi;
        private readonly ComboBox _cmbAltCap;
        private readonly ComboBox _cmbUstCap;
        private readonly TextBox _txtAltAralik;
        private readonly TextBox _txtUstAralik;
        private readonly ComboBox _cmbAltCapY;
        private readonly ComboBox _cmbUstCapY;
        private readonly TextBox _txtAltAralikY;
        private readonly TextBox _txtUstAralikY;
        private readonly ComboBox _cmbSehpaCap;
        private readonly GonyeSemasiPanel _gonyeSemasi;
        private readonly CheckBox _chkAltCiz;
        private readonly CheckBox _chkUstCiz;
        private readonly CheckBox _chkSehpa;
        private readonly Button _btnManuel;
        private readonly Button _btnRun;
        private readonly Button _btnClose;
        private bool _starting;
        /// <summary>Tip radio değişiminde UI↔profil senkronunu bastırmak için.</summary>
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

            int contentW = FormW - Pad * 2;
            _fieldW = (contentW - Gap) / 2;
            _col2X = Pad + _fieldW + Gap;

            int y = 10;

            _rbRadye = MakeRadio("Radye temel", Pad, y);
            _rbMantar = MakeRadio("Mantar döşeme", Pad + 118, y);
            _suppressTipSwitch = true;
            if (OtoDonatiSettings.Tip == OtoDonatiTip.MantarDoseme)
                _rbMantar.Checked = true;
            else
                _rbRadye.Checked = true;
            _suppressTipSwitch = false;
            _rbRadye.CheckedChanged += OnTipRadioChanged;
            _rbMantar.CheckedChanged += OnTipRadioChanged;
            Controls.Add(_rbRadye);
            Controls.Add(_rbMantar);
            y += 26;

            _lblKalinlik = MakeLabel("Temel kalınlığı (cm)", Pad, y);
            Controls.Add(_lblKalinlik);
            Controls.Add(MakeLabel("Paspayı (cm)", _col2X, y)); y += 16;
            _txtKalinlik = MakeText(Pad, y, FormatNum(OtoDonatiSettings.KalinlikCm, 1));
            _txtPaspayi = MakeText(_col2X, y, FormatNum(OtoDonatiSettings.PaspayiCm, 1));
            y += FieldH + 8;

            Controls.Add(MakeLabel("Üst kot (cm)", Pad, y));
            Controls.Add(MakeLabel("Gönye max (cm)", _col2X, y)); y += 16;
            _txtUstKot = MakeText(Pad, y, FormatNum(OtoDonatiSettings.UstKotM * 100.0, 2));
            _txtGonyeBoyu = MakeText(_col2X, y, FormatNum(OtoDonatiSettings.GonyeBoyuMaxCm, 1));
            y += FieldH + 8;

            Controls.Add(MakeLabel("Analiz eşiği (cm)", Pad, y)); y += 16;
            _txtAnalizEsigi = MakeText(Pad, y, FormatNum(OtoDonatiSettings.AnalizEsigiCm, 1));
            y += FieldH + 8;

            Controls.Add(MakeLabel("Üst çap (mm)", Pad, y));
            Controls.Add(MakeLabel("Üst aralık (cm)", _col2X, y)); y += 16;
            _cmbUstCap = MakeCapCombo(Pad, y, OtoDonatiSettings.UstDonatiCapMm);
            _txtUstAralik = MakeText(_col2X, y, FormatNum(OtoDonatiSettings.UstAralikCm, 1));
            Controls.Add(_cmbUstCap);
            y += FieldH + 8;

            Controls.Add(MakeLabel("Alt çap (mm)", Pad, y));
            Controls.Add(MakeLabel("Alt aralık (cm)", _col2X, y)); y += 16;
            _cmbAltCap = MakeCapCombo(Pad, y, OtoDonatiSettings.AltDonatiCapMm);
            _txtAltAralik = MakeText(_col2X, y, FormatNum(OtoDonatiSettings.AltAralikCm, 1));
            Controls.Add(_cmbAltCap);
            y += FieldH + 8;

            _cmbUstCapY = MakeCapCombo(Pad, y, OtoDonatiSettings.UstDonatiCapYMm);
            _txtUstAralikY = MakeText(_col2X, y, FormatNum(OtoDonatiSettings.UstAralikYCm, 1));
            Controls.Add(_cmbUstCapY);
            y += FieldH + 8;
            _cmbAltCapY = MakeCapCombo(Pad, y, OtoDonatiSettings.AltDonatiCapYMm);
            _txtAltAralikY = MakeText(_col2X, y, FormatNum(OtoDonatiSettings.AltAralikYCm, 1));
            Controls.Add(_cmbAltCapY);
            y += FieldH + 8;

            Controls.Add(MakeLabel("Gönye", Pad, y)); y += 16;
            int gonyeH = 120;
            _gonyeSemasi = new GonyeSemasiPanel
            {
                Size = new Size(contentW, gonyeH),
                Location = new Point(Pad, y)
            };
            _gonyeSemasi.SetGonye(
                OtoDonatiSettings.UstGonyeSol,
                OtoDonatiSettings.UstGonyeSag,
                OtoDonatiSettings.AltGonyeSol,
                OtoDonatiSettings.AltGonyeSag);
            Controls.Add(_gonyeSemasi); y += gonyeH + 10;

            _chkAltCiz = MakeCheck("Alt donatı çizilsin", Pad, y, OtoDonatiSettings.AltDonatiCiz);
            Controls.Add(_chkAltCiz); y += 22;
            _chkUstCiz = MakeCheck("Üst donatı çizilsin", Pad, y, OtoDonatiSettings.UstDonatiCiz);
            Controls.Add(_chkUstCiz); y += 22;

            _chkSehpa = MakeCheck("Sehpa donatısı", Pad, y, OtoDonatiSettings.SehpaDonatisi);
            Controls.Add(_chkSehpa); y += 30;
            _cmbSehpaCap = MakeCapCombo(Pad, y, OtoDonatiSettings.SehpaCapMm);
            _cmbSehpaCap.Enabled = _chkSehpa.Checked;
            _chkSehpa.CheckedChanged += (s, e) => _cmbSehpaCap.Enabled = _chkSehpa.Checked;
            Controls.Add(_cmbSehpaCap); y += FieldH + 8;

            _btnManuel = MakeButton("Manuel donatı at", Pad, y, contentW, accent: false);
            _btnManuel.Click += OnManuel;
            Controls.Add(_btnManuel);
            y += 28 + 8;

            _btnRun = MakeButton("Donatı çiz", Pad, y, contentW - 86, accent: true);
            _btnRun.Click += OnRun;
            _btnClose = MakeButton("Kapat", Pad + contentW - 80, y, 80, accent: false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnRun);
            Controls.Add(_btnClose);
            y += 28 + 10;

            ClientSize = new Size(FormW, y);

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

            // Kompakt, içerik odaklı form düzeni.
            var ui = BeginCompact(360);
            ui.Tip(_chkAltCiz, _chkAltCiz.Text); ui.Tip(_chkUstCiz, _chkUstCiz.Text); ui.Tip(_chkSehpa, _chkSehpa.Text);
            _chkAltCiz.Text = "Alt donatı"; _chkUstCiz.Text = "Üst donatı"; _chkSehpa.Text = "Sehpa";
            ui.Section(null).Options(_rbRadye, _rbMantar);
            ui.Tip(_cmbSehpaCap, "Sehpa donatı çapı (mm) — temel antetinin üstüne sehpa detayı çizilir.");
            ui.Tabs(330,
                ui.Page("Kesit ve donatı", p => p.Field(_lblKalinlik, _txtKalinlik).Field("Paspayı · cm", _txtPaspayi)
                    .Field("Üst kot · cm", _txtUstKot)
                    .Field("Üst X · Ø mm / ara cm", _cmbUstCap, _txtUstAralik)
                    .Field("Üst Y · Ø mm / ara cm", _cmbUstCapY, _txtUstAralikY)
                    .Field("Alt X · Ø mm / ara cm", _cmbAltCap, _txtAltAralik)
                    .Field("Alt Y · Ø mm / ara cm", _cmbAltCapY, _txtAltAralikY)
                    .Choices(_chkAltCiz, _chkUstCiz, _chkSehpa)
                    .Field("Sehpa · Ø mm", _cmbSehpaCap)),
                ui.Page("Gönye ve analiz", p => p.Field("En büyük gönye · cm", _txtGonyeBoyu).Field("Analiz eşiği · cm", _txtAnalizEsigi)
                    .Control(_gonyeSemasi, 120)));
            ui.Footer(_btnRun, _btnClose, _btnManuel);
            ui.Finish();
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
                if (keyData == Keys.Space &&
                    (ActiveControl is TextBox || ActiveControl is ComboBox))
                    return base.ProcessCmdKey(ref msg, keyData);
                OnRun(this, EventArgs.Empty);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void UpdateKalinlikLabel()
        {
            if (_lblKalinlik == null) return;
            _lblKalinlik.Text = _rbMantar != null && _rbMantar.Checked
                ? "Döşeme kalınlığı (cm)"
                : "Temel kalınlığı (cm)";
        }

        /// <summary>
        /// Tip değişince: eski tipin alanlarını belleğe yaz → Tip güncelle → yeni tipin alanlarını forma yükle.
        /// </summary>
        private void OnTipRadioChanged(object sender, EventArgs e)
        {
            if (_suppressTipSwitch) return;
            var rb = sender as RadioButton;
            if (rb == null || !rb.Checked) return;

            OtoDonatiTip newTip = _rbMantar.Checked
                ? OtoDonatiTip.MantarDoseme
                : OtoDonatiTip.RadyeTemel;
            OtoDonatiTip oldTip = OtoDonatiSettings.Tip;
            if (newTip == oldTip)
            {
                UpdateKalinlikLabel();
                return;
            }

            // Radio zaten yeni tipi gösteriyor; alanları ESKİ profile yaz (Tip henüz değişmedi).
            PushFieldsQuietTo(oldTip);
            OtoDonatiSettings.Tip = newTip;
            LoadFieldsFromActiveProfile();
            UpdateKalinlikLabel();
            try { OtoDonatiSettings.Save(); } catch { }
        }

        private void LoadFromSettings()
        {
            _suppressTipSwitch = true;
            if (OtoDonatiSettings.Tip == OtoDonatiTip.MantarDoseme)
                _rbMantar.Checked = true;
            else
                _rbRadye.Checked = true;
            _suppressTipSwitch = false;
            LoadFieldsFromActiveProfile();
            UpdateKalinlikLabel();
        }

        private void LoadFieldsFromActiveProfile()
        {
            _txtKalinlik.Text = FormatNum(OtoDonatiSettings.KalinlikCm, 1);
            _txtPaspayi.Text = FormatNum(OtoDonatiSettings.PaspayiCm, 1);
            _txtUstKot.Text = FormatNum(OtoDonatiSettings.UstKotM * 100.0, 2);
            _txtGonyeBoyu.Text = FormatNum(OtoDonatiSettings.GonyeBoyuMaxCm, 1);
            _txtAnalizEsigi.Text = FormatNum(OtoDonatiSettings.AnalizEsigiCm, 1);
            SelectCap(_cmbAltCap, OtoDonatiSettings.AltDonatiCapMm);
            SelectCap(_cmbUstCap, OtoDonatiSettings.UstDonatiCapMm);
            _txtAltAralik.Text = FormatNum(OtoDonatiSettings.AltAralikCm, 1);
            _txtUstAralik.Text = FormatNum(OtoDonatiSettings.UstAralikCm, 1);
            SelectCap(_cmbAltCapY, OtoDonatiSettings.AltDonatiCapYMm);
            SelectCap(_cmbUstCapY, OtoDonatiSettings.UstDonatiCapYMm);
            _txtAltAralikY.Text = FormatNum(OtoDonatiSettings.AltAralikYCm, 1);
            _txtUstAralikY.Text = FormatNum(OtoDonatiSettings.UstAralikYCm, 1);
            SelectCap(_cmbSehpaCap, OtoDonatiSettings.SehpaCapMm);
            _gonyeSemasi.SetGonye(
                OtoDonatiSettings.UstGonyeSol,
                OtoDonatiSettings.UstGonyeSag,
                OtoDonatiSettings.AltGonyeSol,
                OtoDonatiSettings.AltGonyeSag);
            _chkSehpa.Checked = OtoDonatiSettings.SehpaDonatisi;
            _cmbSehpaCap.Enabled = _chkSehpa.Checked;
            _chkAltCiz.Checked = OtoDonatiSettings.AltDonatiCiz;
            _chkUstCiz.Checked = OtoDonatiSettings.UstDonatiCiz;
        }

        private bool TryPushSettings()
        {
            if (!TryParseDouble(_txtKalinlik.Text, out double kal) || kal < 1 || kal > 500)
            {
                Warn("Kalınlık geçersiz (cm).");
                return false;
            }
            if (!TryParseDouble(_txtPaspayi.Text, out double pas) || pas < 0.5 || pas > 20)
            {
                Warn("Paspayı geçersiz (cm).");
                return false;
            }
            if (!TryParseDouble(_txtUstKot.Text, out double kot) || kot < -10000 || kot > 30000)
            {
                Warn("Üst kot geçersiz (cm).");
                return false;
            }
            if (!TryParseDouble(_txtGonyeBoyu.Text, out double gonyeMax) || gonyeMax < 1 || gonyeMax > 200)
            {
                Warn("Gönye boyu max geçersiz (cm).");
                return false;
            }
            if (!TryParseDouble(_txtAnalizEsigi.Text, out double analiz) || analiz < 1 || analiz > 5000)
            {
                Warn("Analiz eşiği geçersiz (cm).");
                return false;
            }
            if (!TryParseDouble(_txtAltAralik.Text, out double aAr) || aAr < 5 || aAr > 80)
            {
                Warn("Alt X aralık geçersiz (cm).");
                return false;
            }
            if (!TryParseDouble(_txtUstAralik.Text, out double uAr) || uAr < 5 || uAr > 80)
            {
                Warn("Üst X aralık geçersiz (cm).");
                return false;
            }
            if (!TryParseDouble(_txtAltAralikY.Text, out double aArY) || aArY < 5 || aArY > 80)
            {
                Warn("Alt Y aralık geçersiz (cm).");
                return false;
            }
            if (!TryParseDouble(_txtUstAralikY.Text, out double uArY) || uArY < 5 || uArY > 80)
            {
                Warn("Üst Y aralık geçersiz (cm).");
                return false;
            }

            OtoDonatiSettings.Tip = _rbMantar.Checked
                ? OtoDonatiTip.MantarDoseme
                : OtoDonatiTip.RadyeTemel;
            OtoDonatiSettings.KalinlikCm = kal;
            OtoDonatiSettings.PaspayiCm = pas;
            OtoDonatiSettings.UstKotM = kot / 100.0;
            OtoDonatiSettings.GonyeBoyuMaxCm = gonyeMax;
            OtoDonatiSettings.AnalizEsigiCm = analiz;
            OtoDonatiSettings.AltDonatiCapMm = ParseCap(_cmbAltCap);
            OtoDonatiSettings.UstDonatiCapMm = ParseCap(_cmbUstCap);
            OtoDonatiSettings.AltAralikCm = aAr;
            OtoDonatiSettings.UstAralikCm = uAr;
            OtoDonatiSettings.AltDonatiCapYMm = ParseCap(_cmbAltCapY);
            OtoDonatiSettings.UstDonatiCapYMm = ParseCap(_cmbUstCapY);
            OtoDonatiSettings.AltAralikYCm = aArY;
            OtoDonatiSettings.UstAralikYCm = uArY;
            OtoDonatiSettings.SehpaCapMm = ParseCap(_cmbSehpaCap);
            OtoDonatiSettings.UstGonyeSol = _gonyeSemasi.UstSol;
            OtoDonatiSettings.UstGonyeSag = _gonyeSemasi.UstSag;
            OtoDonatiSettings.AltGonyeSol = _gonyeSemasi.AltSol;
            OtoDonatiSettings.AltGonyeSag = _gonyeSemasi.AltSag;
            OtoDonatiSettings.AltDonatiCiz = _chkAltCiz.Checked;
            OtoDonatiSettings.UstDonatiCiz = _chkUstCiz.Checked;
            OtoDonatiSettings.SehpaDonatisi = _chkSehpa.Checked;
            if (!_chkAltCiz.Checked && !_chkUstCiz.Checked)
            {
                Warn("Alt veya üst donatıdan en az biri çizilsin.");
                return false;
            }
            return true;
        }

        /// <summary>Kapatırken: geçerli alanları yaz, geçersizleri atla (MessageBox yok).</summary>
        private void TryPushSettingsQuiet()
        {
            try
            {
                OtoDonatiSettings.Tip = _rbMantar.Checked
                    ? OtoDonatiTip.MantarDoseme
                    : OtoDonatiTip.RadyeTemel;
                PushFieldsQuietTo(OtoDonatiSettings.Tip);
            }
            catch { }
        }

        /// <summary>
        /// Form alanlarını belirtilen profile yazar; Tip property'sine dokunmaz.
        /// Tip değiştirirken eski profili kaydetmek için kullanılır.
        /// </summary>
        private void PushFieldsQuietTo(OtoDonatiTip tip)
        {
            try
            {
                var p = OtoDonatiSettings.ProfileFor(tip);
                if (p == null) return;
                if (TryParseDouble(_txtKalinlik.Text, out double kal) && kal >= 1 && kal <= 500)
                    p.KalinlikCm = kal;
                if (TryParseDouble(_txtPaspayi.Text, out double pas) && pas >= 0.5 && pas <= 20)
                    p.PaspayiCm = pas;
                if (TryParseDouble(_txtUstKot.Text, out double kot) && kot >= -10000 && kot <= 30000)
                    p.UstKotM = kot / 100.0;
                if (TryParseDouble(_txtGonyeBoyu.Text, out double gonyeMax) && gonyeMax >= 1 && gonyeMax <= 200)
                    p.GonyeBoyuMaxCm = gonyeMax;
                if (TryParseDouble(_txtAnalizEsigi.Text, out double analiz) && analiz >= 1 && analiz <= 5000)
                    p.AnalizEsigiCm = analiz;
                if (TryParseDouble(_txtAltAralik.Text, out double aAr) && aAr >= 5 && aAr <= 80)
                    p.AltAralikCm = aAr;
                if (TryParseDouble(_txtUstAralik.Text, out double uAr) && uAr >= 5 && uAr <= 80)
                    p.UstAralikCm = uAr;
                if (TryParseDouble(_txtAltAralikY.Text, out double aArY) && aArY >= 5 && aArY <= 80)
                    p.AltAralikYCm = aArY;
                if (TryParseDouble(_txtUstAralikY.Text, out double uArY) && uArY >= 5 && uArY <= 80)
                    p.UstAralikYCm = uArY;
                p.AltDonatiCapMm = ParseCap(_cmbAltCap);
                p.UstDonatiCapMm = ParseCap(_cmbUstCap);
                p.AltDonatiCapYMm = ParseCap(_cmbAltCapY);
                p.UstDonatiCapYMm = ParseCap(_cmbUstCapY);
                p.SehpaCapMm = ParseCap(_cmbSehpaCap);
                p.UstGonyeSol = _gonyeSemasi.UstSol;
                p.UstGonyeSag = _gonyeSemasi.UstSag;
                p.AltGonyeSol = _gonyeSemasi.AltSol;
                p.AltGonyeSag = _gonyeSemasi.AltSag;
                p.AltDonatiCiz = _chkAltCiz.Checked;
                p.UstDonatiCiz = _chkUstCiz.Checked;
                p.SehpaDonatisi = _chkSehpa.Checked;
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

        private static void Warn(string msg)
        {
            try
            {
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nOto Donatı: " + msg);
            }
            catch { }
            MessageBox.Show(msg, "Oto Donatı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static string FormatNum(double v, int decimals)
        {
            return v.ToString("0." + new string('#', decimals), CultureInfo.CurrentCulture);
        }

        private static bool TryParseDouble(string text, out double v)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
                return true;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

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

        private static Label MakeLabel(string text, int x, int y) => new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Beykent.Monolit.StandardFormTheme.Text,
            BackColor = Color.Transparent
        };

        private static RadioButton MakeRadio(string text, int x, int y) => new RadioButton
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Beykent.Monolit.StandardFormTheme.Text,
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Standard
        };

        private static CheckBox MakeCheck(string text, int x, int y, bool on) => new CheckBox
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Beykent.Monolit.StandardFormTheme.Text,
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Standard,
            Checked = on
        };

        /// <summary>
        /// Kenarlıklı kutu + BorderStyle.None TextBox — yazı düşey ortalı.
        /// Host paneli forma eklenir; dönüş değeri iç TextBox.
        /// </summary>
        private TextBox MakeText(int x, int y, string text)
        {
            var host = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(_fieldW, FieldH),
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                BorderStyle = BorderStyle.FixedSingle
            };
            var t = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                Font = Font,
                Text = text,
                TabStop = true
            };
            host.Controls.Add(t);
            EventHandler layout = (s, e) =>
            {
                int th = TextRenderer.MeasureText("Ag", t.Font).Height;
                int innerH = Math.Max(th, 14);
                t.Height = Math.Min(innerH, Math.Max(1, host.ClientSize.Height - 2));
                t.Width = Math.Max(0, host.ClientSize.Width - 6);
                t.Left = 3;
                t.Top = Math.Max(0, (host.ClientSize.Height - t.Height) / 2);
            };
            host.Resize += layout;
            host.HandleCreated += layout;
            t.FontChanged += (s, e) => layout(s, e);
            layout(null, EventArgs.Empty);
            Controls.Add(host);
            return t;
        }

        private Button MakeButton(string text, int x, int y, int w, bool accent)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 28),
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

        private ComboBox MakeCapCombo(int x, int y, int selectedMm)
        {
            var c = new ComboBox
            {
                Location = new Point(x, y),
                Size = new Size(_fieldW, FieldH),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                FlatStyle = FlatStyle.Flat,
                Font = Font
            };
            foreach (int d in new[] { 8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 32 })
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
