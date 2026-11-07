using System.Drawing;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    internal sealed class TemelIlaveDenemeForm : Beykent.Monolit.MonolitForm
    {
        private static decimal _sonGenislik = (decimal)TemelIlaveKutuBol.MinDilimGenislikCm;
        private static decimal _sonPaspay = 5m;
        private static decimal _sonHiza = 100m;
        private readonly NumericUpDown _genislik;
        private readonly NumericUpDown _paspay;
        private readonly NumericUpDown _hiza;

        internal static bool TryShow()
        {
            using (var form = new TemelIlaveDenemeForm())
            {
                if (AcApp.ShowModalDialog(form) != DialogResult.OK) return false;
                TemelIlaveKutuBol.MinDilimGenislikCm = (double)form._genislik.Value;
                TemelIlaveDenemeCommands.TemelPaspayCm = (double)form._paspay.Value;
                TemelIlaveDenemeCommands.DuseyHizaFarkCm = (double)form._hiza.Value;
                _sonGenislik = form._genislik.Value;
                _sonPaspay = form._paspay.Value;
                _sonHiza = form._hiza.Value;
                return true;
            }
        }

        private TemelIlaveDenemeForm()
        {
            Text = "Temel İlave";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(440, 268);

            var adetYazi = new Label
            {
                Text = "En az dilim genişliği (cm)",
                Location = new Point(18, 22),
                AutoSize = true
            };
            _genislik = SayiKutu(280, 18, 0);
            _genislik.Minimum = 10;
            _genislik.Maximum = 1000;
            _genislik.Increment = 10;
            _genislik.Value = _sonGenislik < 10 ? 120m : _sonGenislik;

            var paspayYazi = new Label
            {
                Text = "Temel paspayı (cm)",
                Location = new Point(18, 62),
                AutoSize = true
            };
            _paspay = SayiKutu(280, 58, 1);
            _paspay.Minimum = 0;
            _paspay.Maximum = 30;
            _paspay.Increment = 0.5m;
            _paspay.Value = _sonPaspay < 0 ? 5m : _sonPaspay;

            var hizaYazi = new Label
            {
                Text = "Düşey hiza farkı (cm)",
                Location = new Point(18, 102),
                AutoSize = true
            };
            _hiza = SayiKutu(280, 98, 0);
            _hiza.Minimum = 0;
            _hiza.Maximum = 500;
            _hiza.Increment = 10;
            _hiza.Value = _sonHiza < 0 ? 100m : _sonHiza;

            var not = new Label
            {
                Text = "En az genişlik ısıdan taşmaz; aralık katına tamamlanır.\nZarf kenarı temel sınırından bu kadar içeride kalır.\nYakın boy, ısıya girmeden en çok kutuya hizalanır.\nÇakışan zarflar birleşir.",
                Location = new Point(18, 136),
                Size = new Size(404, 74),
                ForeColor = Beykent.Monolit.StandardFormTheme.TextMuted
            };
            var ciz = new Button
            {
                Text = "Çiz",
                Location = new Point(234, 216),
                Size = new Size(90, 30),
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                FlatStyle = FlatStyle.Flat
            };
            var iptal = new Button
            {
                Text = "İptal",
                Location = new Point(332, 216),
                Size = new Size(90, 30),
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.Cancel
            };
            ciz.Click += (s, e) =>
            {
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.AddRange(new Control[] { adetYazi, _genislik, paspayYazi, _paspay, hizaYazi, _hiza, not, ciz, iptal });
            AcceptButton = ciz;
            CancelButton = iptal;

            // Kompakt, içerik odaklı form düzeni.
            var ui = BeginCompact(350);
            ui.Section("Zarf yerleşimi").Field("En az genişlik · cm", _genislik).Field("Temel paspayı · cm", _paspay).Field("Düşey hiza farkı · cm", _hiza);
            ui.Section(null).Control(not);
            ui.Footer(ciz, iptal);
            ui.Finish();
        }

        private static NumericUpDown SayiKutu(int x, int y, int ondalik)
        {
            return new NumericUpDown
            {
                Location = new Point(x, y),
                Size = new Size(90, 24),
                DecimalPlaces = ondalik,
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                TextAlign = HorizontalAlignment.Right,
                ThousandsSeparator = false
            };
        }
    }
}
