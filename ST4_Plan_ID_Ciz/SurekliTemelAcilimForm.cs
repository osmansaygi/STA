using System;
using System.Drawing;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    /// <summary>Kiriş açılım formuyla aynı düzen: Antetli/Antetsiz. Sürekli temel kat seçmez.</summary>
    internal sealed class SurekliTemelAcilimForm : Form
    {
        private static bool _sonAntetli = true;
        private readonly RadioButton _antetli;
        public KirisAcilimSecenekleri Secenekler { get; private set; }

        internal static bool TryShow(out KirisAcilimSecenekleri secenekler)
        {
            secenekler = null;
            using (var form = new SurekliTemelAcilimForm())
            {
                if (AcApp.ShowModalDialog(form) != DialogResult.OK) return false;
                secenekler = form.Secenekler;
                return true;
            }
        }

        private SurekliTemelAcilimForm()
        {
            Text = "Sürekli Temel Açılımları";
            ClientSize = new Size(420, 220);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = AcadPaletteTheme.Text;

            var drawing = new GroupBox
            {
                Text = "Çizim düzeni · 1/25",
                Left = 16,
                Top = 12,
                Width = 388,
                Height = 76,
                ForeColor = ForeColor
            };
            _antetli = new RadioButton { Text = "Antetli", Left = 18, Top = 30, Width = 150, Checked = _sonAntetli };
            var plain = new RadioButton { Text = "Antetsiz", Left = 198, Top = 30, Width = 150, Checked = !_sonAntetli };
            drawing.Controls.AddRange(new Control[] { _antetli, plain });
            Controls.Add(drawing);

            Controls.Add(new Label
            {
                Text = "Beton, kolon, donatı ve etriye açılımları.\nMontaj altta, düz donatı üstte çizilir.",
                Left = 18,
                Top = 102,
                Width = 388,
                Height = 40
            });

            var draw = new Button { Text = "Çiz", Left = 216, Top = 160, Width = 90, Height = 30 };
            var cancel = new Button { Text = "İptal", Left = 314, Top = 160, Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            draw.Click += (s, e) =>
            {
                Secenekler = new KirisAcilimSecenekleri { Antetli = _antetli.Checked, SeciliKatNo = null };
                _sonAntetli = _antetli.Checked;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.AddRange(new Control[] { draw, cancel });
            AcceptButton = draw;
            CancelButton = cancel;
        }
    }
}
