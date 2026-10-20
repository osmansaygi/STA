using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ST4AksCizCSharp;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    internal sealed class KirisAcilimSecenekleri
    {
        public bool Antetli;
        public int? SeciliKatNo;
    }

    internal sealed class KirisAcilimForm : Form
    {
        private static bool _sonAntetli = true, _sonTumKatlar = true;
        private static int? _sonKatNo;
        private readonly RadioButton _antetli, _tum;
        private readonly ComboBox _kat;
        private readonly List<FloorInfo> _katlar;
        public KirisAcilimSecenekleri Secenekler { get; private set; }

        internal static bool TryShow(St4Model model, out KirisAcilimSecenekleri secenekler)
        {
            secenekler = null;
            using (var form = new KirisAcilimForm(model))
            {
                if (AcApp.ShowModalDialog(form) != DialogResult.OK) return false;
                secenekler = form.Secenekler;
                return true;
            }
        }

        private KirisAcilimForm(St4Model model)
        {
            _katlar = (model.Floors ?? new List<FloorInfo>()).Where(f => f != null)
                .OrderBy(f => f.ElevationM).ToList();
            Text = "Kiriş Açılımları";
            ClientSize = new Size(420, 306);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            BackColor = AcadPaletteTheme.Bg; ForeColor = AcadPaletteTheme.Text;
            var drawing = new GroupBox { Text = "Çizim düzeni · 1/25", Left = 16, Top = 12, Width = 388, Height = 76, ForeColor = ForeColor };
            _antetli = new RadioButton { Text = "Antetli", Left = 18, Top = 30, Width = 150, Checked = _sonAntetli };
            var plain = new RadioButton { Text = "Antetsiz", Left = 198, Top = 30, Width = 150, Checked = !_sonAntetli };
            drawing.Controls.AddRange(new Control[] { _antetli, plain }); Controls.Add(drawing);
            var floors = new GroupBox { Text = "Çizilecek katlar", Left = 16, Top = 98, Width = 388, Height = 108, ForeColor = ForeColor };
            _tum = new RadioButton { Text = "Bütün katlar", Left = 18, Top = 24, Width = 150, Checked = _sonTumKatlar };
            var selected = new RadioButton { Text = "Seçili kat", Left = 198, Top = 24, Width = 150, Checked = !_sonTumKatlar };
            _kat = new ComboBox { Left = 18, Top = 60, Width = 350, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = !_sonTumKatlar };
            foreach (var f in _katlar)
                _kat.Items.Add((string.IsNullOrWhiteSpace(f.Name) ? "Kat " + f.FloorNo : f.Name) + "  (" + f.ElevationM.ToString("0.00") + " m)");
            if (_katlar.Count > 0) _kat.SelectedIndex = Math.Max(0, _katlar.FindIndex(f => f.FloorNo == _sonKatNo));
            _tum.CheckedChanged += (s, e) => _kat.Enabled = !_tum.Checked;
            floors.Controls.AddRange(new Control[] { _tum, selected, _kat }); Controls.Add(floors);
            Controls.Add(new Label { Text = "Her kat tek antet · dar yerleşim · takımlar arası 80 cm.", Left = 18, Top = 218, Width = 388, Height = 25 });
            var draw = new Button { Text = "Çiz", Left = 216, Top = 261, Width = 90, Height = 30 };
            var cancel = new Button { Text = "İptal", Left = 314, Top = 261, Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            draw.Click += (s, e) =>
            {
                if (!_tum.Checked && _kat.SelectedIndex < 0)
                { MessageBox.Show(this, "Çizilecek katı seçin.", Text); return; }
                Secenekler = new KirisAcilimSecenekleri { Antetli = _antetli.Checked,
                    SeciliKatNo = _tum.Checked ? (int?)null : _katlar[_kat.SelectedIndex].FloorNo };
                _sonAntetli = _antetli.Checked; _sonTumKatlar = _tum.Checked;
                if (_kat.SelectedIndex >= 0) _sonKatNo = _katlar[_kat.SelectedIndex].FloorNo;
                DialogResult = DialogResult.OK; Close();
            };
            Controls.AddRange(new Control[] { draw, cancel });
            AcceptButton = draw; CancelButton = cancel;
        }
    }
}
