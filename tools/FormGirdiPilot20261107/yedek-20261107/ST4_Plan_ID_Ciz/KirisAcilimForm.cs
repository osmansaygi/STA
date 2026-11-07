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
        /// <summary>Sürekli temel: aynı hizadaki temeller tek açılım (false: her temel ayrı).</summary>
        public bool TemelBirlestir = true;
        /// <summary>Kiriş açılımı: düz ve alt ilave donatıların ikinci gönyesi (a+b ℓb'yi tutmayınca eklenen kol) çizilmez.</summary>
        public bool AltIkinciGonyeKaldir;
        public double YatayNetBoslukCm = 4.0;
        public double DuseyNetBoslukCm = 3.0;
        /// <summary>Beton yüzü ile etriye dış çizgisi arasındaki örtü (cm).</summary>
        public double PaspayiCm = 4.0;
        /// <summary>1/100 paftaya konacak: açılım (yazı/ölçü/antet dahil) ×2.</summary>
        public bool Pafta100;
    }

    internal sealed class KirisAcilimForm : Beykent.Monolit.MonolitForm
    {
        private static bool _sonAntetli = true, _sonTumKatlar = true, _sonIkinciGonyeKaldir, _sonPafta100;
        private static int? _sonKatNo;
        private static string _sonKatAdi;
        private static decimal _sonYatayNet = 4m, _sonDuseyNet = 3m, _sonPaspayi = 4m;
        private readonly NumericUpDown _yatayNet, _duseyNet, _paspayi;
        private readonly RadioButton _antetli, _tum;
        private readonly CheckBox _ikinciGonye;
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
            var saved = PlanFormSettings.Load("kirisacilim");
            _sonAntetli = saved.Bool("Antetli", _sonAntetli);
            _sonTumKatlar = saved.Bool("TumKatlar", _sonTumKatlar);
            _sonIkinciGonyeKaldir = saved.Bool("IkinciGonyeKaldir", _sonIkinciGonyeKaldir);
            _sonPafta100 = saved.Bool("Pafta100", _sonPafta100);
            var pafta = BeykentOrtak.PaftaOlcek.Secenekler(_sonPafta100);
            _sonYatayNet = saved.Decimal("YatayNetBoslukCm", _sonYatayNet, 0.5m, 15m);
            _sonDuseyNet = saved.Decimal("DuseyNetBoslukCm", _sonDuseyNet, 0.5m, 15m);
            _sonPaspayi = saved.Decimal("PaspayiCm", _sonPaspayi, 0.5m, 15m);
            _sonKatNo = saved.NullableInt("KatNo", _sonKatNo);
            _sonKatAdi = saved.Text("KatAdi", _sonKatAdi);
            _katlar = (model.Floors ?? new List<FloorInfo>()).Where(f => f != null)
                .OrderBy(f => f.ElevationM).ToList();
            Text = "Kiriş Açılımları";
            ClientSize = new Size(420, 306);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            BackColor = Beykent.Monolit.StandardFormTheme.Bg; ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            var drawing = new GroupBox { Text = "Çizim düzeni · 1/25", Left = 16, Top = 12, Width = 388, Height = 76, ForeColor = ForeColor };
            _antetli = new RadioButton { Text = "Antetli", Left = 18, Top = 30, Width = 150, Checked = _sonAntetli };
            var plain = new RadioButton { Text = "Antetsiz", Left = 198, Top = 30, Width = 150, Checked = !_sonAntetli };
            drawing.Controls.AddRange(new Control[] { _antetli, plain }); Controls.Add(drawing);
            var floors = new GroupBox { Text = "Çizilecek katlar", Left = 16, Top = 98, Width = 388, Height = 108, ForeColor = ForeColor };
            _tum = new RadioButton { Text = "Bütün katlar", Left = 18, Top = 24, Width = 150, Checked = _sonTumKatlar };
            var selected = new RadioButton { Text = "Seçili kat", Left = 198, Top = 24, Width = 150, Checked = !_sonTumKatlar };
            _kat = new ComboBox { Left = 18, Top = 60, Width = 350, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = !_sonTumKatlar };
            foreach (var f in _katlar)
                _kat.Items.Add((string.IsNullOrWhiteSpace(f.Name) ? "Kat " + f.FloorNo : f.Name) + "  (" + (f.ElevationM * 100.0).ToString("0.##") + " cm)");
            if (_katlar.Count > 0)
            {
                int remembered = string.IsNullOrEmpty(_sonKatAdi) ? -1 : _katlar.FindIndex(f => string.Equals(f.Name, _sonKatAdi, StringComparison.OrdinalIgnoreCase));
                if (remembered < 0) remembered = _katlar.FindIndex(f => f.FloorNo == _sonKatNo);
                _kat.SelectedIndex = Math.Max(0, remembered);
            }
            _tum.CheckedChanged += (s, e) => _kat.Enabled = !_tum.Checked;
            floors.Controls.AddRange(new Control[] { _tum, selected, _kat }); Controls.Add(floors);
            _ikinciGonye = new CheckBox { Text = "İkinci gönyeyi kaldır (düz ve alt ilave)", Left = 18, Top = 244, Width = 350, Checked = _sonIkinciGonyeKaldir };
            Controls.Add(_ikinciGonye);
            NumericUpDown Sayi(decimal value) => new NumericUpDown
            { DecimalPlaces = 1, Increment = 0.5m, Minimum = 0.5m, Maximum = 15m, Value = value, Width = 100 };
            _yatayNet = Sayi(_sonYatayNet); _duseyNet = Sayi(_sonDuseyNet); _paspayi = Sayi(_sonPaspayi);
            Controls.AddRange(new Control[] { _yatayNet, _duseyNet, _paspayi });
            Controls.Add(new Label { Text = "Her kat tek antet · dar yerleşim · takımlar arası 80 cm.", Left = 18, Top = 218, Width = 388, Height = 25 });
            var draw = new Button { Text = "Çiz", Left = 216, Top = 261, Width = 90, Height = 30 };
            var cancel = new Button { Text = "İptal", Left = 314, Top = 261, Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            draw.Click += (s, e) =>
            {
                if (!_tum.Checked && _kat.SelectedIndex < 0)
                { MessageBox.Show(this, "Çizilecek katı seçin.", Text); return; }
                Secenekler = new KirisAcilimSecenekleri { Antetli = _antetli.Checked,
                    SeciliKatNo = _tum.Checked ? (int?)null : _katlar[_kat.SelectedIndex].FloorNo,
                    AltIkinciGonyeKaldir = _ikinciGonye.Checked,
                    YatayNetBoslukCm = (double)_yatayNet.Value, DuseyNetBoslukCm = (double)_duseyNet.Value,
                    PaspayiCm = (double)_paspayi.Value, Pafta100 = pafta[1].Checked };
                _sonPafta100 = pafta[1].Checked;
                _sonYatayNet = _yatayNet.Value; _sonDuseyNet = _duseyNet.Value; _sonPaspayi = _paspayi.Value;
                _sonAntetli = _antetli.Checked; _sonTumKatlar = _tum.Checked; _sonIkinciGonyeKaldir = _ikinciGonye.Checked;
                if (_kat.SelectedIndex >= 0)
                { _sonKatNo = _katlar[_kat.SelectedIndex].FloorNo; _sonKatAdi = _katlar[_kat.SelectedIndex].Name; }
                PlanFormSettings.Load("kirisacilim").Set("Antetli", _sonAntetli).Set("TumKatlar", _sonTumKatlar)
                    .Set("IkinciGonyeKaldir", _sonIkinciGonyeKaldir).Set("Pafta100", _sonPafta100).Set("YatayNetBoslukCm", _sonYatayNet)
                    .Set("DuseyNetBoslukCm", _sonDuseyNet).Set("PaspayiCm", _sonPaspayi)
                    .Set("KatNo", _sonKatNo).Set("KatAdi", _sonKatAdi).Save();
                DialogResult = DialogResult.OK; Close();
            };
            Controls.AddRange(new Control[] { draw, cancel });
            AcceptButton = draw; CancelButton = cancel;

            // Kompakt, içerik odaklı form düzeni.
            var ui = BeginCompact(350);
            ui.Section("Çizim düzeni · 1/25").Options(_antetli, plain);
            ui.Section("Pafta ölçeği").Options(pafta);
            ui.Section("Kat seçimi").Choices(_tum, selected).Field("Çizilecek kat", _kat);
            ui.Section("Gönyeler").Choices(_ikinciGonye);
            ui.Section("Donatı yerleşimi").Field("Yatay net boşluk · cm", _yatayNet)
                .Field("Düşey net boşluk · cm", _duseyNet).Field("Paspayı (etriye dışı) · cm", _paspayi);
            ui.Footer(draw, cancel);
            ui.Finish();
        }
    }
}
