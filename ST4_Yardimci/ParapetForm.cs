using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    internal sealed class ParapetForm : Form
    {
        static ParapetForm _instance;
        readonly Dictionary<string, NumericUpDown> _numbers = new Dictionary<string, NumericUpDown>();
        readonly ComboBox _konum;
        bool _starting;
        internal static ParapetSettings Current;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("PARAPET")) return;
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new ParapetForm();
                AcApp.ShowModelessDialog(_instance);
            }
            else { _instance.Show(); _instance.BringToFront(); }
            BaFormSession.Attach("PARAPET", _instance);
            BaFormSession.EndPick("PARAPET");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("PARAPET");
            if (_instance != null) { _instance.Close(); _instance = null; }
        }

        ParapetForm()
        {
            Current = ParapetSettings.Load();
            Text = "Parapet Detayı";
            ClientSize = new Size(610, 395);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = AcadPaletteTheme.Text;
            Font = new Font("Segoe UI", 9);
            Controls.Add(new Label { Text = "Oturma konumu", Location = new Point(12, 15), AutoSize = true });
            _konum = new ComboBox { Location = new Point(150, 10), Width = 440, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = AcadPaletteTheme.InputBg, ForeColor = AcadPaletteTheme.Text };
            _konum.Items.AddRange(new object[] { "Döşeme kenarı (döşeme solda)", "Döşeme ortası", "Kiriş üzeri (dış yüzler hizalı)" });
            _konum.SelectedIndex = (int)Current.Konum;
            Controls.Add(_konum);
            Row("DosemeKalinligi", "Döşeme kalınlığı", 0, 0, 8, 150);
            Row("DosemeUstKotu", "Döşeme üst kotu", 0, 1, -100000, 100000);
            Row("ParapetYuksekligi", "Parapet yüksekliği", 0, 2, 15, 600);
            Row("ParapetKalinligi", "Parapet kalınlığı", 0, 3, 10, 150);
            Row("Sakal", "Sakal (döşeme altı)", 0, 4, 0, 300);
            Row("KirisGenisligi", "Kiriş genişliği", 0, 5, 10, 300);
            Row("KirisYuksekligi", "Kiriş yüksekliği", 0, 6, 8, 300);
            Row("ToplamUzunluk", "Toplam parapet boyu", 0, 7, 0, 10000000);
            Row("Paspayi", "Paspayı", 1, 0, 2, 20);
            Row("DikeyCap", "Dikey çap (mm)", 1, 1, 6, 40);
            Row("DikeyAralik", "Dikey aralık", 1, 2, 5, 50);
            Row("YatayCap", "Yatay çap (mm)", 1, 3, 6, 40);
            Row("YatayAralik", "Yatay aralık", 1, 4, 5, 50);
            Controls.Add(new Label { Text = "Alt / ikinci gönye en az 12ø.\nSığmazsa yukarı ek gönye yapılır.\nToplam boy 5 cm katına tamamlanır.",
                Location = new Point(312, 194), Size = new Size(275, 60), ForeColor = AcadPaletteTheme.TextMuted });
            Controls.Add(new Label { Text = "Uzunluklar cm. Parapet yüksekliği döşeme üstünden ölçülür.\nSakal: döşeme altından sarkma (0 = yok). Kiriş üstü = döşeme üst kotu.",
                Location = new Point(12, 289), Size = new Size(580, 45), ForeColor = AcadPaletteTheme.TextMuted });
            Controls.Add(new Label { Text = "Toplam boy 0: metraj yok.\nStok 1200 cm / bindirme 60 cm.", Location = new Point(312,254), Size = new Size(275,35), ForeColor = AcadPaletteTheme.TextMuted });
            var draw = new Button { Text = "Manuel çiz", Location = new Point(310, 350), Size = new Size(140, 30), BackColor = AcadPaletteTheme.BgAlt };
            draw.Click += (s, e) => Draw();
            Controls.Add(draw);
            var close = new Button { Text = "Kapat", Location = new Point(462, 350), Size = new Size(125, 30), BackColor = AcadPaletteTheme.BgAlt };
            close.Click += (s, e) => Close();
            Controls.Add(close);
            _konum.SelectedIndexChanged += (s, e) => EnableBeam();
            EnableBeam();
            FormClosed += (s, e) => { ReadSettings().Save(); _instance = null; BaFormSession.Release("PARAPET"); };
        }

        void EnableBeam()
        {
            _numbers["KirisGenisligi"].Enabled = _numbers["KirisYuksekligi"].Enabled = _konum.SelectedIndex == 2;
            bool beam = _konum.SelectedIndex == 2;
            _numbers["Sakal"].Enabled = !beam;
            if (beam) _numbers["Sakal"].Value = 0;
        }

        void Row(string key, string label, int col, int row, decimal min, decimal max)
        {
            int x = 12 + 300 * col, y = 49 + 29 * row;
            Controls.Add(new Label { Text = label, Location = new Point(x, y + 3), Size = new Size(175, 22) });
            var n = new NumericUpDown { Location = new Point(x + 180, y), Size = new Size(95, 24), Minimum = min, Maximum = max,
                DecimalPlaces = 1, Increment = 0.5m, BackColor = AcadPaletteTheme.InputBg, ForeColor = AcadPaletteTheme.Text };
            double value = (double)typeof(ParapetSettings).GetField(key).GetValue(Current);
            n.Value = (decimal)Math.Max((double)min, Math.Min((double)max, value));
            YardimciUi.StripUpDownButtons(n);
            _numbers.Add(key, n);
            Controls.Add(n);
        }

        ParapetSettings ReadSettings()
        {
            var s = new ParapetSettings { Konum = (ParapetKonumu)_konum.SelectedIndex };
            foreach (var kv in _numbers) typeof(ParapetSettings).GetField(kv.Key).SetValue(s, (double)kv.Value.Value);
            s.Normalize();
            return s;
        }

        void Draw()
        {
            if (_starting) return;
            var settings = ReadSettings();
            string error = settings.Validate();
            if (error != null) { MessageBox.Show(this, error, "Parapet Detayı"); return; }
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Current = settings;
            Current.Save();
            _starting = true;
            BaFormSession.BeginPick("PARAPET");
            Hide();
            try { doc.SendStringToExecute("PARAPETCIZ ", true, false, false); }
            catch { _starting = false; Show(); BaFormSession.EndPick("PARAPET"); throw; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == Keys.Enter) { Draw(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
