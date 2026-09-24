using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    internal enum DonatiMetrajEtiketFormati
    {
        AdetFiCapL = 0,
        FiCapSlashAralikL = 1,
        SadeceCapL = 2
    }

    /// <summary>Donatı metraj tablosu ayarları.</summary>
    internal static class DonatiMetrajSettings
    {
        public static double PuntoYukseklikCm { get; set; } = 2.5;
        public static DonatiMetrajEtiketFormati EtiketFormati { get; set; } = DonatiMetrajEtiketFormati.AdetFiCapL;
        public static bool AgirlikHesapla { get; set; } = true;
        public static bool ToplamSatiri { get; set; } = true;
        public static bool CapBazindaGrupla { get; set; } = true;
        public static string Baslik { get; set; } = "DONATI METRAJI";
        public static double SatirAraligiCarpan { get; set; } = 1.8;
        public static bool SadeceDonatiYazisiKatmani { get; set; } = false;
    }

    /// <summary>Modeless form — seçilen donatı yazılarından metraj tablosu.</summary>
    internal sealed class DonatiMetrajForm : Form
    {
        private static DonatiMetrajForm _instance;

        private readonly NumericUpDown _numPunto;
        private readonly ComboBox _cmbFormat;
        private readonly CheckBox _chkAgirlik;
        private readonly CheckBox _chkToplam;
        private readonly CheckBox _chkGrupla;
        private readonly CheckBox _chkYaziLayer;
        private readonly TextBox _txtBaslik;
        private readonly NumericUpDown _numSatir;
        private readonly Button _btnRun;
        private readonly Button _btnClose;

        public static void ShowOrActivate()
        {
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new DonatiMetrajForm();
                try { AcApp.ShowModelessDialog(_instance); }
                catch { _instance.Show(); }
            }
            else
            {
                _instance.LoadFromSettings();
                _instance.Visible = true;
                _instance.BringToFront();
            }
        }

        public static void Shutdown()
        {
            if (_instance == null) return;
            try { _instance.Close(); } catch { }
            _instance = null;
        }

        private DonatiMetrajForm()
        {
            Text = "Donatı Metraj";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            Size = new Size(380, 440);
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = Color.White;
            Font = new SdFont("Segoe UI", 9f);

            int y = 14;
            Controls.Add(MakeLabel("Punto yüksekliği (cm)", 14, y)); y += 20;
            _numPunto = MakeNum(14, y, 140, 0.5m, 20, 0.5m, (decimal)DonatiMetrajSettings.PuntoYukseklikCm);
            Controls.Add(_numPunto); y += 36;

            Controls.Add(MakeLabel("Etiket / satır formatı", 14, y)); y += 20;
            _cmbFormat = new ComboBox
            {
                Location = new Point(14, y),
                Size = new Size(320, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = AcadPaletteTheme.InputBg,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _cmbFormat.Items.Add(new FmtItem(DonatiMetrajEtiketFormati.AdetFiCapL, "n Øφ L=boy  (adet×çap×boy)"));
            _cmbFormat.Items.Add(new FmtItem(DonatiMetrajEtiketFormati.FiCapSlashAralikL, "Øφ/aralık L=boy"));
            _cmbFormat.Items.Add(new FmtItem(DonatiMetrajEtiketFormati.SadeceCapL, "Øφ  L=boy"));
            SelectFmt(_cmbFormat, DonatiMetrajSettings.EtiketFormati);
            Controls.Add(_cmbFormat); y += 36;

            Controls.Add(MakeLabel("Tablo başlığı", 14, y)); y += 20;
            _txtBaslik = new TextBox
            {
                Location = new Point(14, y),
                Size = new Size(320, 26),
                Text = DonatiMetrajSettings.Baslik,
                BackColor = AcadPaletteTheme.InputBg,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_txtBaslik); y += 36;

            Controls.Add(MakeLabel("Satır aralığı çarpanı (×punto)", 14, y)); y += 20;
            _numSatir = MakeNum(14, y, 140, 1.2m, 4, 0.1m, (decimal)DonatiMetrajSettings.SatirAraligiCarpan);
            Controls.Add(_numSatir); y += 32;

            _chkAgirlik = MakeCheck("Ağırlık hesapla (kg, ρ=0.00617 kg/mm²/m)", 14, y, DonatiMetrajSettings.AgirlikHesapla);
            Controls.Add(_chkAgirlik); y += 26;
            _chkToplam = MakeCheck("Toplam satırı ekle", 14, y, DonatiMetrajSettings.ToplamSatiri);
            Controls.Add(_chkToplam); y += 26;
            _chkGrupla = MakeCheck("Aynı çap+boy satırlarını birleştir", 14, y, DonatiMetrajSettings.CapBazindaGrupla);
            Controls.Add(_chkGrupla); y += 26;
            _chkYaziLayer = MakeCheck("Yalnız DONATI YAZISI katmanı", 14, y, DonatiMetrajSettings.SadeceDonatiYazisiKatmani);
            Controls.Add(_chkYaziLayer); y += 36;

            _btnRun = MakeButton("Seç ve tablo çiz", 14, y, 210);
            _btnRun.ForeColor = AcadPaletteTheme.Accent;
            _btnRun.Click += OnRun;
            _btnClose = MakeButton("Kapat", 236, y, 80);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnRun);
            Controls.Add(_btnClose);

            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                    _instance = null;
            };

            try
            {
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 140,
                    Screen.PrimaryScreen.WorkingArea.Top + 90);
            }
            catch { StartPosition = FormStartPosition.CenterScreen; }
        }

        private void LoadFromSettings()
        {
            _numPunto.Value = Clamp(_numPunto, (decimal)DonatiMetrajSettings.PuntoYukseklikCm);
            SelectFmt(_cmbFormat, DonatiMetrajSettings.EtiketFormati);
            _txtBaslik.Text = DonatiMetrajSettings.Baslik;
            _numSatir.Value = Clamp(_numSatir, (decimal)DonatiMetrajSettings.SatirAraligiCarpan);
            _chkAgirlik.Checked = DonatiMetrajSettings.AgirlikHesapla;
            _chkToplam.Checked = DonatiMetrajSettings.ToplamSatiri;
            _chkGrupla.Checked = DonatiMetrajSettings.CapBazindaGrupla;
            _chkYaziLayer.Checked = DonatiMetrajSettings.SadeceDonatiYazisiKatmani;
        }

        private void PushSettings()
        {
            DonatiMetrajSettings.PuntoYukseklikCm = (double)_numPunto.Value;
            if (_cmbFormat.SelectedItem is FmtItem fi)
                DonatiMetrajSettings.EtiketFormati = fi.Value;
            DonatiMetrajSettings.Baslik = _txtBaslik.Text?.Trim() ?? "DONATI METRAJI";
            DonatiMetrajSettings.SatirAraligiCarpan = (double)_numSatir.Value;
            DonatiMetrajSettings.AgirlikHesapla = _chkAgirlik.Checked;
            DonatiMetrajSettings.ToplamSatiri = _chkToplam.Checked;
            DonatiMetrajSettings.CapBazindaGrupla = _chkGrupla.Checked;
            DonatiMetrajSettings.SadeceDonatiYazisiKatmani = _chkYaziLayer.Checked;
        }

        private void OnRun(object sender, EventArgs e)
        {
            PushSettings();
            DonatiMetrajRunner.RunInteractive();
        }

        private static decimal Clamp(NumericUpDown n, decimal v)
        {
            if (v < n.Minimum) return n.Minimum;
            if (v > n.Maximum) return n.Maximum;
            return v;
        }

        private static void SelectFmt(ComboBox cmb, DonatiMetrajEtiketFormati v)
        {
            for (int i = 0; i < cmb.Items.Count; i++)
            {
                if (cmb.Items[i] is FmtItem fi && fi.Value == v)
                {
                    cmb.SelectedIndex = i;
                    return;
                }
            }
            if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        }

        private Label MakeLabel(string text, int x, int y) => new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.White,
            BackColor = Color.Transparent
        };

        private CheckBox MakeCheck(string text, int x, int y, bool on) => new CheckBox
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Checked = on
        };

        private Button MakeButton(string text, int x, int y, int w)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = AcadPaletteTheme.BgAlt,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = AcadPaletteTheme.InputBorder;
            b.FlatAppearance.MouseOverBackColor = AcadPaletteTheme.BgHover;
            return b;
        }

        private NumericUpDown MakeNum(int x, int y, int w, decimal min, decimal max, decimal inc, decimal val)
        {
            var n = new NumericUpDown
            {
                Location = new Point(x, y),
                Size = new Size(w, 26),
                Minimum = min,
                Maximum = max,
                Increment = inc,
                DecimalPlaces = 1,
                BackColor = AcadPaletteTheme.InputBg,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            n.Value = Clamp(n, val);
            return n;
        }

        private sealed class FmtItem
        {
            public DonatiMetrajEtiketFormati Value { get; }
            public string Text { get; }
            public FmtItem(DonatiMetrajEtiketFormati v, string t) { Value = v; Text = t; }
            public override string ToString() => Text;
        }
    }
}
