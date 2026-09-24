using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    internal enum DonatiBoyuYuvarlama
    {
        Yok = 0,
        BirCm = 1,
        BesCm = 5,
        OnCm = 10
    }

    /// <summary>Donatı boyu (L=) güncelleme ayarları.</summary>
    internal static class DonatiBoyuSettings
    {
        /// <summary>Çizim birimi → cm çarpanı (1 = birim cm, 0.1 = birim mm).</summary>
        public static double OlcekCarpani { get; set; } = 1.0;
        public static DonatiBoyuYuvarlama Yuvarlama { get; set; } = DonatiBoyuYuvarlama.BirCm;
        /// <summary>Yazıya en yakın donatı arama yarıçapı (çizim birimi).</summary>
        public static double AramaYaricapi { get; set; } = 80;
        public static string LEtiketFormati { get; set; } = "L=";
        public static bool SadeceDonatiKatmani { get; set; } = true;
        public static double MinFarkCm { get; set; } = 0.5;
        public static bool OnizlemeMesaji { get; set; } = true;
    }

    /// <summary>Modeless form — toplu L= güncelleme.</summary>
    internal sealed class DonatiBoyuForm : Form
    {
        private static DonatiBoyuForm _instance;

        private readonly ComboBox _cmbOlcek;
        private readonly ComboBox _cmbYuvarlama;
        private readonly NumericUpDown _numArama;
        private readonly ComboBox _cmbLFormat;
        private readonly CheckBox _chkDonatiLayer;
        private readonly NumericUpDown _numMinFark;
        private readonly CheckBox _chkOnizleme;
        private readonly Button _btnRun;
        private readonly Button _btnClose;

        public static void ShowOrActivate()
        {
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new DonatiBoyuForm();
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

        private DonatiBoyuForm()
        {
            Text = "Donatı Boyu (L=)";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            Size = new Size(360, 420);
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = Color.White;
            Font = new SdFont("Segoe UI", 9f);

            int y = 14;
            Controls.Add(MakeLabel("Donatı ölçeği (birim → cm)", 14, y)); y += 20;
            _cmbOlcek = MakeCombo(14, y, 200);
            _cmbOlcek.Items.Add(new OlcekItem(1.0, "1 birim = 1 cm"));
            _cmbOlcek.Items.Add(new OlcekItem(0.1, "1 birim = 1 mm (×0.1)"));
            _cmbOlcek.Items.Add(new OlcekItem(0.01, "1 birim = 0.01 cm"));
            _cmbOlcek.Items.Add(new OlcekItem(2.0, "1/50 çizim (×2)"));
            SelectOlcek(_cmbOlcek, DonatiBoyuSettings.OlcekCarpani);
            Controls.Add(_cmbOlcek); y += 36;

            Controls.Add(MakeLabel("Yuvarlama", 14, y)); y += 20;
            _cmbYuvarlama = MakeCombo(14, y, 200);
            _cmbYuvarlama.Items.Add(new YuvarItem(DonatiBoyuYuvarlama.Yok, "Yuvarlama yok"));
            _cmbYuvarlama.Items.Add(new YuvarItem(DonatiBoyuYuvarlama.BirCm, "1 cm"));
            _cmbYuvarlama.Items.Add(new YuvarItem(DonatiBoyuYuvarlama.BesCm, "5 cm"));
            _cmbYuvarlama.Items.Add(new YuvarItem(DonatiBoyuYuvarlama.OnCm, "10 cm"));
            SelectYuvar(_cmbYuvarlama, DonatiBoyuSettings.Yuvarlama);
            Controls.Add(_cmbYuvarlama); y += 36;

            Controls.Add(MakeLabel("Arama yarıçapı (çizim birimi)", 14, y)); y += 20;
            _numArama = MakeNum(14, y, 160, 5, 5000, 5, (decimal)DonatiBoyuSettings.AramaYaricapi);
            Controls.Add(_numArama); y += 36;

            Controls.Add(MakeLabel("L etiket formatı", 14, y)); y += 20;
            _cmbLFormat = MakeCombo(14, y, 160);
            _cmbLFormat.Items.Add("L=");
            _cmbLFormat.Items.Add("l=");
            _cmbLFormat.Items.Add("L =");
            _cmbLFormat.Items.Add("l =");
            SelectString(_cmbLFormat, DonatiBoyuSettings.LEtiketFormati);
            Controls.Add(_cmbLFormat); y += 36;

            Controls.Add(MakeLabel("Min. fark (cm) — altında dokunma", 14, y)); y += 20;
            _numMinFark = MakeNum(14, y, 160, 0, 50, 0.5m, (decimal)DonatiBoyuSettings.MinFarkCm);
            Controls.Add(_numMinFark); y += 32;

            _chkDonatiLayer = new CheckBox
            {
                Text = "Yalnız DONATI katmanındaki çizgiler",
                Location = new Point(14, y),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Checked = DonatiBoyuSettings.SadeceDonatiKatmani
            };
            Controls.Add(_chkDonatiLayer); y += 28;

            _chkOnizleme = new CheckBox
            {
                Text = "Komut satırında özet yaz",
                Location = new Point(14, y),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Checked = DonatiBoyuSettings.OnizlemeMesaji
            };
            Controls.Add(_chkOnizleme); y += 36;

            _btnRun = MakeButton("Seç ve güncelle", 14, y, 200);
            _btnRun.ForeColor = AcadPaletteTheme.Accent;
            _btnRun.Click += OnRun;
            _btnClose = MakeButton("Kapat", 226, y, 80);
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
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 100,
                    Screen.PrimaryScreen.WorkingArea.Top + 100);
            }
            catch { StartPosition = FormStartPosition.CenterScreen; }
        }

        private void LoadFromSettings()
        {
            SelectOlcek(_cmbOlcek, DonatiBoyuSettings.OlcekCarpani);
            SelectYuvar(_cmbYuvarlama, DonatiBoyuSettings.Yuvarlama);
            _numArama.Value = Clamp(_numArama, (decimal)DonatiBoyuSettings.AramaYaricapi);
            SelectString(_cmbLFormat, DonatiBoyuSettings.LEtiketFormati);
            _numMinFark.Value = Clamp(_numMinFark, (decimal)DonatiBoyuSettings.MinFarkCm);
            _chkDonatiLayer.Checked = DonatiBoyuSettings.SadeceDonatiKatmani;
            _chkOnizleme.Checked = DonatiBoyuSettings.OnizlemeMesaji;
        }

        private void PushSettings()
        {
            if (_cmbOlcek.SelectedItem is OlcekItem oi)
                DonatiBoyuSettings.OlcekCarpani = oi.Factor;
            if (_cmbYuvarlama.SelectedItem is YuvarItem yi)
                DonatiBoyuSettings.Yuvarlama = yi.Value;
            DonatiBoyuSettings.AramaYaricapi = (double)_numArama.Value;
            if (_cmbLFormat.SelectedItem is string fmt)
                DonatiBoyuSettings.LEtiketFormati = fmt;
            DonatiBoyuSettings.MinFarkCm = (double)_numMinFark.Value;
            DonatiBoyuSettings.SadeceDonatiKatmani = _chkDonatiLayer.Checked;
            DonatiBoyuSettings.OnizlemeMesaji = _chkOnizleme.Checked;
        }

        private void OnRun(object sender, EventArgs e)
        {
            PushSettings();
            DonatiBoyuRunner.RunInteractive();
        }

        private static decimal Clamp(NumericUpDown n, decimal v)
        {
            if (v < n.Minimum) return n.Minimum;
            if (v > n.Maximum) return n.Maximum;
            return v;
        }

        private static void SelectOlcek(ComboBox cmb, double f)
        {
            for (int i = 0; i < cmb.Items.Count; i++)
            {
                if (cmb.Items[i] is OlcekItem oi && Math.Abs(oi.Factor - f) < 1e-9)
                {
                    cmb.SelectedIndex = i;
                    return;
                }
            }
            if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        }

        private static void SelectYuvar(ComboBox cmb, DonatiBoyuYuvarlama v)
        {
            for (int i = 0; i < cmb.Items.Count; i++)
            {
                if (cmb.Items[i] is YuvarItem yi && yi.Value == v)
                {
                    cmb.SelectedIndex = i;
                    return;
                }
            }
            if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        }

        private static void SelectString(ComboBox cmb, string s)
        {
            for (int i = 0; i < cmb.Items.Count; i++)
            {
                if (cmb.Items[i] is string t && t == s)
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

        private ComboBox MakeCombo(int x, int y, int w) => new ComboBox
        {
            Location = new Point(x, y),
            Size = new Size(w, 26),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = AcadPaletteTheme.InputBg,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };

        private sealed class OlcekItem
        {
            public double Factor { get; }
            public string Text { get; }
            public OlcekItem(double f, string t) { Factor = f; Text = t; }
            public override string ToString() => Text;
        }

        private sealed class YuvarItem
        {
            public DonatiBoyuYuvarlama Value { get; }
            public string Text { get; }
            public YuvarItem(DonatiBoyuYuvarlama v, string t) { Value = v; Text = t; }
            public override string ToString() => Text;
        }
    }
}
