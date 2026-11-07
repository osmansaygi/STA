using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Beykent.Monolit.Girdi;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;
using SdPoint = System.Drawing.Point;
using SdSize = System.Drawing.Size;

namespace ST4Yardimci
{
    internal enum SehpaDonatiTip
    {
        RadyeTemel = 0,
        MantarDoseme = 1
    }

    internal sealed class SehpaDonatiProfile
    {
        public double KalinlikCm;
        public int CapMm;
        public double UstKotM;
        public int MinGenislikCm;
        /// <summary>Radye / döşeme alanı (m²). 1 adet/m² ile çarpılır.</summary>
        public double AlanM2;
        /// <summary>Paspayı (cm). Radye varsayılan 5, mantar 3.</summary>
        public double PaspayiCm;

        public static SehpaDonatiProfile CreateRadyeDefault() => new SehpaDonatiProfile
        {
            KalinlikCm = 50,
            CapMm = 12,
            UstKotM = -0.80,
            MinGenislikCm = 30,
            AlanM2 = 322,
            PaspayiCm = 5
        };

        public static SehpaDonatiProfile CreateMantarDefault() => new SehpaDonatiProfile
        {
            KalinlikCm = 25,
            CapMm = 10,
            UstKotM = 0,
            MinGenislikCm = 20,
            AlanM2 = 322,
            PaspayiCm = 3
        };
    }

    /// <summary>
    /// Sehpa donatısı — %LocalAppData%\ST4Yardimci\sehpa_donati.ini
    /// Radye ve mantar profilleri ayrı saklanır.
    /// </summary>
    internal static class SehpaDonatiSettings
    {
        private static readonly object Sync = new object();
        private static bool _loaded;
        private static SehpaDonatiProfile _radye = SehpaDonatiProfile.CreateRadyeDefault();
        private static SehpaDonatiProfile _mantar = SehpaDonatiProfile.CreateMantarDefault();

        public static SehpaDonatiTip Tip { get; set; } = SehpaDonatiTip.RadyeTemel;

        private static SehpaDonatiProfile Active =>
            Tip == SehpaDonatiTip.MantarDoseme ? _mantar : _radye;

        public static SehpaDonatiProfile ProfileFor(SehpaDonatiTip tip) =>
            tip == SehpaDonatiTip.MantarDoseme ? _mantar : _radye;

        public static double KalinlikCm { get => Active.KalinlikCm; set => Active.KalinlikCm = value; }
        public static int CapMm { get => Active.CapMm; set => Active.CapMm = value; }
        /// <summary>Döşeme / temel üst kotu (m).</summary>
        public static double UstKotM { get => Active.UstKotM; set => Active.UstKotM = value; }
        public static int MinGenislikCm { get => Active.MinGenislikCm; set => Active.MinGenislikCm = value; }
        /// <summary>Radye temel / döşeme alanı (m²).</summary>
        public static double AlanM2 { get => Active.AlanM2; set => Active.AlanM2 = value; }
        /// <summary>Paspayı (cm).</summary>
        public static double PaspayiCm { get => Active.PaspayiCm; set => Active.PaspayiCm = value; }

        /// <summary>Alt ayak (gönye) boyu, her iki uç (cm).</summary>
        public const double AyakCm = 15.0;

        /// <summary>
        /// Radye: kalınlık − (paspayı + 2 cm).
        /// Mantar: kalınlık − 2×paspayı − 2 cm.
        /// </summary>
        public static double SehpaYuksekligiCm
        {
            get
            {
                double h = Tip == SehpaDonatiTip.MantarDoseme
                    ? KalinlikCm - 2.0 * PaspayiCm - 2.0
                    : KalinlikCm - (PaspayiCm + 2.0);
                return h < 1 ? 1 : h;
            }
        }

        private static string StorePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ST4Yardimci");
                return Path.Combine(dir, "sehpa_donati.ini");
            }
        }

        public static void EnsureLoaded()
        {
            lock (Sync)
            {
                if (_loaded) return;
                _loaded = true;
                try { LoadUnlocked(); }
                catch { }
            }
        }

        public static void Save()
        {
            EnsureLoaded();
            lock (Sync)
            {
                try
                {
                    string path = StorePath;
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var sb = new StringBuilder();
                    sb.AppendLine("; ST4 Yardimci — Sehpa donatisi (Radye + Mantar)");
                    Write(sb, "Tip", Tip == SehpaDonatiTip.MantarDoseme ? "Mantar" : "Radye");
                    WriteProfile(sb, "Radye", _radye);
                    WriteProfile(sb, "Mantar", _mantar);
                    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                }
                catch { }
            }
        }

        private static void WriteProfile(StringBuilder sb, string prefix, SehpaDonatiProfile p)
        {
            if (p == null) return;
            Write(sb, prefix + "_KalinlikCm", p.KalinlikCm);
            Write(sb, prefix + "_CapMm", p.CapMm);
            Write(sb, prefix + "_UstKotM", p.UstKotM);
            Write(sb, prefix + "_MinGenislikCm", p.MinGenislikCm);
            Write(sb, prefix + "_AlanM2", p.AlanM2);
            Write(sb, prefix + "_PaspayiCm", p.PaspayiCm);
        }

        private static void Write(StringBuilder sb, string key, double v)
        {
            sb.Append(key).Append('=')
                .Append(v.ToString("0.####", CultureInfo.InvariantCulture))
                .AppendLine();
        }

        private static void Write(StringBuilder sb, string key, int v)
        {
            sb.Append(key).Append('=').Append(v).AppendLine();
        }

        private static void Write(StringBuilder sb, string key, string v)
        {
            sb.Append(key).Append('=').Append(v ?? "").AppendLine();
        }

        private static void LoadUnlocked()
        {
            string path = StorePath;
            if (!File.Exists(path)) return;

            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line[0] == ';' || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                if (key.Equals("Tip", StringComparison.OrdinalIgnoreCase))
                {
                    Tip = val.StartsWith("M", StringComparison.OrdinalIgnoreCase)
                        ? SehpaDonatiTip.MantarDoseme
                        : SehpaDonatiTip.RadyeTemel;
                    continue;
                }

                SehpaDonatiProfile p = null;
                string field = key;
                if (key.StartsWith("Radye_", StringComparison.OrdinalIgnoreCase))
                {
                    p = _radye;
                    field = key.Substring(6);
                }
                else if (key.StartsWith("Mantar_", StringComparison.OrdinalIgnoreCase))
                {
                    p = _mantar;
                    field = key.Substring(7);
                }
                if (p == null) continue;
                ApplyField(p, field, val);
            }
        }

        private static void ApplyField(SehpaDonatiProfile p, string key, string val)
        {
            if (key.Equals("KalinlikCm", StringComparison.OrdinalIgnoreCase) && TryD(val, out double kal))
                p.KalinlikCm = kal;
            else if (key.Equals("CapMm", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cap))
                p.CapMm = cap;
            else if (key.Equals("UstKotM", StringComparison.OrdinalIgnoreCase) && TryD(val, out double kot))
                p.UstKotM = kot;
            else if (key.Equals("MinGenislikCm", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int g))
                p.MinGenislikCm = g;
            else if (key.Equals("AlanM2", StringComparison.OrdinalIgnoreCase) && TryD(val, out double alan))
                p.AlanM2 = alan;
            else if (key.Equals("PaspayiCm", StringComparison.OrdinalIgnoreCase) && TryD(val, out double pas))
                p.PaspayiCm = pas;
        }

        private static bool TryD(string s, out double v)
        {
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return true;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v);
        }
    }

    /// <summary>Sehpa donatısı formu — radye / mantar. Enter çiz, Esc kapat.</summary>
    internal sealed class SehpaDonatiForm : Beykent.Monolit.MonolitForm
    {
        private const string OlcekAnahtar = "sehpa";
        private static SehpaDonatiForm _instance;

        private readonly SegmentSecim _tip;
        private readonly Label _lblKalinlik;
        private readonly Label _lblAlan;
        private readonly OlcuKutusu _numKalinlik;
        private readonly ComboBox _cmbCap;
        private readonly OlcuKutusu _numUstKot;
        private readonly OlcuKutusu _numGenislik;
        private readonly OlcuKutusu _numAlan;
        private readonly OlcuKutusu _numPas;
        private readonly SegmentSecim _olcek;
        private readonly GirdiDenetci Denetci = new GirdiDenetci();
        private bool _starting;
        private bool _suppressTipSwitch;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("SEHPADONATI")) return;
            SehpaDonatiSettings.EnsureLoaded();
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new SehpaDonatiForm();
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

            BaFormSession.Attach("SEHPADONATI", _instance);
            BaFormSession.EndPick("SEHPADONATI");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("SEHPADONATI");
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

                BaFormSession.BeginPick("SEHPADONATI");
                _instance.Visible = false;
            }
            catch { }
        }

        private SehpaDonatiForm()
        {
            Text = "Sehpa Donatısı";
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
            _tip.Secili = SehpaDonatiSettings.Tip == SehpaDonatiTip.MantarDoseme ? 1 : 0;
            _suppressTipSwitch = false;
            _tip.SeciliDegisti += OnTipChanged;

            // Sınırlar ve ondalıklar eski NumericUpDown ile aynı. Üst kot cm gösterilir, UstKotM (metre) kaydedilir.
            _lblKalinlik = new Label { Text = "Temel kalınlığı", AutoSize = true };
            _lblAlan = new Label { Text = "Radye alanı", AutoSize = true };
            _numKalinlik = Denetci.Ekle(new OlcuKutusu("Kalınlık", "cm", 10, 400, 50));
            _numPas = Denetci.Ekle(new OlcuKutusu("Paspayı", "cm", 1, 15, 5, 1, 0.5));
            _numUstKot = Denetci.Ekle(new OlcuKutusu("Üst kot", "cm", -8000, 20000, -80, 2, 5));
            _numGenislik = Denetci.Ekle(new OlcuKutusu("En az genişlik", "cm", 5, 400, 30));
            _numAlan = Denetci.Ekle(new OlcuKutusu("Alan", "m²", 0.1, 1000000, 322, 1));
            _cmbCap = MakeCapCombo(SehpaDonatiSettings.CapMm);
            Controls.Add(_cmbCap);
            _olcek = new SegmentSecim("1/50 pafta", "1/100 pafta (×2)") { Secili = BaPaftaOlcek.Yuz(OlcekAnahtar) ? 1 : 0 };
            _olcek.SeciliDegisti += (s, e) => BaPaftaOlcek.Kaydet(OlcekAnahtar, _olcek.Secili == 1);
            LoadFieldsFromActive();

            Denetci.UyariKurali(() =>
            {
                bool mantar = _tip.Secili == 1;
                double h = mantar
                    ? _numKalinlik.Deger - 2.0 * _numPas.Deger - 2.0
                    : _numKalinlik.Deger - (_numPas.Deger + 2.0);
                return h < 1 ? "Sehpa yüksekliği 1 cm'nin altında; 1 cm alınır" : null;
            });

            var btnRun = MakeButton("Sehpa çiz", true);
            btnRun.Click += (s, e) => OnRun();
            var btnClose = MakeButton("Kapat", false);
            btnClose.Click += (s, e) => Close();

            UpdateKalinlikLabel();

            FormClosing += (s, e) =>
            {
                TryPushQuiet();
                SehpaDonatiSettings.Save();
            };
            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                {
                    _instance = null;
                    BaFormSession.Release("SEHPADONATI");
                }
            };

            try
            {
                Location = new SdPoint(Screen.PrimaryScreen.WorkingArea.Left + 140,
                    Screen.PrimaryScreen.WorkingArea.Top + 80);
            }
            catch
            {
                StartPosition = FormStartPosition.CenterScreen;
            }

            var ui = BeginCompact(310);
            ui.Section(null).Control(_tip);
            ui.Section("Kesit")
                .Field(_lblKalinlik, _numKalinlik)
                .Field("Paspayı", _numPas)
                .Field("Üst kot", _numUstKot)
                .Field(_lblAlan, _numAlan);
            ui.Section("Sehpa")
                .Field("Donatı çapı (mm)", _cmbCap)
                .Field("En az genişlik", _numGenislik)
                .Hint("Adet = alan × 1 adet/m².");
            ui.Section("Pafta ölçeği · detay 1/25").Control(_olcek);
            ui.Section(null).Control(Denetci.Serit, 44);
            ui.Footer(btnRun, btnClose);
            ui.Finish();
            Denetci.Guncelle();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            if ((keyData == Keys.Enter || keyData == Keys.Return) && !(ActiveControl is Button))
            {
                OnRun();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void UpdateKalinlikLabel()
        {
            if (_lblKalinlik == null) return;
            bool mantar = _tip != null && _tip.Secili == 1;
            _lblKalinlik.Text = mantar ? "Döşeme kalınlığı" : "Temel kalınlığı";
            if (_lblAlan != null)
                _lblAlan.Text = mantar ? "Döşeme alanı" : "Radye alanı";
        }

        private SehpaDonatiTip SeciliTip => _tip.Secili == 1 ? SehpaDonatiTip.MantarDoseme : SehpaDonatiTip.RadyeTemel;

        private void OnTipChanged(object sender, EventArgs e)
        {
            if (_suppressTipSwitch) return;

            SehpaDonatiTip newTip = SeciliTip;
            SehpaDonatiTip oldTip = SehpaDonatiSettings.Tip;
            if (newTip == oldTip)
            {
                UpdateKalinlikLabel();
                return;
            }

            PushQuietTo(oldTip);
            SehpaDonatiSettings.Tip = newTip;
            LoadFieldsFromActive();
            UpdateKalinlikLabel();
            Denetci.Guncelle();
            try { SehpaDonatiSettings.Save(); } catch { }
        }

        private void LoadFromSettings()
        {
            _suppressTipSwitch = true;
            _tip.Secili = SehpaDonatiSettings.Tip == SehpaDonatiTip.MantarDoseme ? 1 : 0;
            _suppressTipSwitch = false;
            LoadFieldsFromActive();
            UpdateKalinlikLabel();
            Denetci.Guncelle();
        }

        /// <summary>Eski NumericUpDown gibi yüklenen değer sınıra kırpılır ve ondalığa yuvarlanır.</summary>
        private void LoadFieldsFromActive()
        {
            _numKalinlik.Deger = Kirp(Math.Round(SehpaDonatiSettings.KalinlikCm), 10, 400);
            SelectCap(_cmbCap, SehpaDonatiSettings.CapMm);
            _numUstKot.Deger = Kirp(Math.Round(SehpaDonatiSettings.UstKotM * 100.0, 2), -8000, 20000);
            _numGenislik.Deger = Kirp(SehpaDonatiSettings.MinGenislikCm, 5, 400);
            _numAlan.Deger = Kirp(Math.Round(SehpaDonatiSettings.AlanM2, 1), 0.1, 1000000);
            _numPas.Deger = Kirp(Math.Round(SehpaDonatiSettings.PaspayiCm, 1), 1, 15);
        }

        private static double Kirp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));

        private bool TryPush()
        {
            if (!Denetci.Onayla()) return false;
            SehpaDonatiSettings.Tip = SeciliTip;
            SehpaDonatiSettings.KalinlikCm = _numKalinlik.Deger;
            SehpaDonatiSettings.CapMm = ParseCap(_cmbCap);
            SehpaDonatiSettings.UstKotM = _numUstKot.Deger / 100.0;
            SehpaDonatiSettings.MinGenislikCm = (int)Math.Round(_numGenislik.Deger);
            SehpaDonatiSettings.AlanM2 = _numAlan.Deger;
            SehpaDonatiSettings.PaspayiCm = _numPas.Deger;
            return true;
        }

        private void TryPushQuiet()
        {
            try
            {
                SehpaDonatiSettings.Tip = SeciliTip;
                PushQuietTo(SehpaDonatiSettings.Tip);
            }
            catch { }
        }

        /// <summary>Geçerli alanları profile yazar; geçersiz alan eski değerde kalır.</summary>
        private void PushQuietTo(SehpaDonatiTip tip)
        {
            var p = SehpaDonatiSettings.ProfileFor(tip);
            if (p == null) return;
            if (_numKalinlik.Gecerli) p.KalinlikCm = _numKalinlik.Deger;
            p.CapMm = ParseCap(_cmbCap);
            if (_numUstKot.Gecerli) p.UstKotM = _numUstKot.Deger / 100.0;
            if (_numGenislik.Gecerli) p.MinGenislikCm = (int)Math.Round(_numGenislik.Deger);
            if (_numAlan.Gecerli) p.AlanM2 = _numAlan.Deger;
            if (_numPas.Gecerli) p.PaspayiCm = _numPas.Deger;
        }

        private void OnRun()
        {
            if (_starting) return;
            if (!TryPush()) return;
            SehpaDonatiSettings.Save();
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                _starting = true;
                HideTemporarily();
                doc.SendStringToExecute("SEHPADONATICIZ ", true, false, false);
            }
            catch
            {
                _starting = false;
            }
        }

        private static int ParseCap(ComboBox cmb)
        {
            if (cmb?.SelectedItem is string s && int.TryParse(s, out int v))
                return v;
            return 12;
        }

        private static void SelectCap(ComboBox cmb, int mm)
        {
            string s = mm.ToString(CultureInfo.InvariantCulture);
            int i = cmb.Items.IndexOf(s);
            cmb.SelectedIndex = i >= 0 ? i : 0;
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
            foreach (int d in new[] { 8, 10, 12, 14, 16, 18, 20 })
                c.Items.Add(d.ToString(CultureInfo.InvariantCulture));
            SelectCap(c, selectedMm);
            return c;
        }

        private Button MakeButton(string text, bool accent)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = Beykent.Monolit.StandardFormTheme.BgAlt,
                ForeColor = accent ? Beykent.Monolit.StandardFormTheme.Accent : Beykent.Monolit.StandardFormTheme.Text,
                Font = accent ? new SdFont("Segoe UI", 9f, System.Drawing.FontStyle.Bold) : Font,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = Beykent.Monolit.StandardFormTheme.InputBorder;
            b.FlatAppearance.BorderSize = 1;
            return b;
        }
    }
}
