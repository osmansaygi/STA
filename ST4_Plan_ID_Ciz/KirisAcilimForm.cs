using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Beykent.Monolit.Girdi;
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
        private readonly OlcuKutusu _yatayNet, _duseyNet, _paspayi;
        private readonly SegmentSecim _duzen, _kapsam, _pafta;
        private readonly CheckBox _ikinciGonye;
        private readonly ComboBox _kat;
        private readonly List<FloorInfo> _katlar;
        private readonly GirdiDenetci _denetci = new GirdiDenetci();
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

            // Ortak kural 3/4: dışlayan seçimler segment; kayıt anahtarları ve anlamları aynı.
            _duzen = new SegmentSecim("Antetli", "Antetsiz") { Secili = _sonAntetli ? 0 : 1 };
            _pafta = new SegmentSecim("1/50 pafta", "1/100 pafta (×2)") { Secili = _sonPafta100 ? 1 : 0 };
            _kapsam = new SegmentSecim("Bütün katlar", "Seçili kat") { Secili = _sonTumKatlar ? 0 : 1 };
            _kat = new ComboBox { Left = 18, Top = 60, Width = 350, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var f in _katlar)
                _kat.Items.Add((string.IsNullOrWhiteSpace(f.Name) ? "Kat " + f.FloorNo : f.Name) + "  (" + (f.ElevationM * 100.0).ToString("0.##") + " cm)");
            if (_katlar.Count > 0)
            {
                int remembered = string.IsNullOrEmpty(_sonKatAdi) ? -1 : _katlar.FindIndex(f => string.Equals(f.Name, _sonKatAdi, StringComparison.OrdinalIgnoreCase));
                if (remembered < 0) remembered = _katlar.FindIndex(f => f.FloorNo == _sonKatNo);
                _kat.SelectedIndex = Math.Max(0, remembered);
            }
            Controls.Add(_kat);
            _ikinciGonye = new CheckBox { Text = "İkinci gönyeyi kaldır (düz ve alt ilave)", Left = 18, Top = 244, Width = 350, Checked = _sonIkinciGonyeKaldir };
            Controls.Add(_ikinciGonye);
            // Sınırlar/adım eski NumericUpDown ile aynı: 0,5–15 cm, 1 ondalık, adım 0,5.
            OlcuKutusu Sayi(string ad, double varsayilan, decimal value)
            {
                var k = _denetci.Ekle(new OlcuKutusu(ad, "cm", 0.5, 15, varsayilan, 1, 0.5));
                k.Deger = (double)value;
                return k;
            }
            _yatayNet = Sayi("Yatay net boşluk", 4, _sonYatayNet);
            _duseyNet = Sayi("Düşey net boşluk", 3, _sonDuseyNet);
            _paspayi = Sayi("Paspayı", 4, _sonPaspayi);
            _denetci.SegmentIzle(_kapsam);
            _kat.SelectedIndexChanged += (s, e) => _denetci.Guncelle();
            _denetci.HataKurali(() => _kapsam.Secili == 1 && _kat.SelectedIndex < 0 ? "Çizilecek katı seçin" : null);

            var draw = new Button { Text = "Çiz", Left = 216, Top = 261, Width = 90, Height = 30 };
            var cancel = new Button { Text = "İptal", Left = 314, Top = 261, Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            draw.Click += (s, e) =>
            {
                if (!_denetci.Onayla()) return;
                bool tum = _kapsam.Secili == 0, yuz = _pafta.Secili == 1, antetli = _duzen.Secili == 0;
                Secenekler = new KirisAcilimSecenekleri { Antetli = antetli,
                    SeciliKatNo = tum ? (int?)null : _katlar[_kat.SelectedIndex].FloorNo,
                    AltIkinciGonyeKaldir = _ikinciGonye.Checked,
                    YatayNetBoslukCm = _yatayNet.Deger, DuseyNetBoslukCm = _duseyNet.Deger,
                    PaspayiCm = _paspayi.Deger, Pafta100 = yuz };
                _sonPafta100 = yuz;
                _sonYatayNet = (decimal)_yatayNet.Deger; _sonDuseyNet = (decimal)_duseyNet.Deger; _sonPaspayi = (decimal)_paspayi.Deger;
                _sonAntetli = antetli; _sonTumKatlar = tum; _sonIkinciGonyeKaldir = _ikinciGonye.Checked;
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

            var ui = BeginCompact(350);
            ui.Section("Kat seçimi").Control(_kapsam).FieldWhen(() => _kapsam.Secili == 1, new Label { Text = "Çizilecek kat" }, _kat);
            ui.Section("Donatı yerleşimi").Field("Yatay net boşluk", _yatayNet)
                .Field("Düşey net boşluk", _duseyNet).Field("Paspayı (etriye dışı)", _paspayi);
            ui.Section("Gönyeler").Choices(_ikinciGonye);
            ui.Section("Çizim düzeni · 1/25").Control(_duzen).Hint("Her kat tek antet · dar yerleşim · takımlar arası 80 cm.");
            ui.Section("Pafta ölçeği").Control(_pafta);
            ui.Section(null).Control(_denetci.Serit, 44);
            ui.Footer(draw, cancel);
            ui.Finish();
            _kapsam.SeciliDegisti += (s, e) => ui.Refresh();
            _denetci.Guncelle();
        }
    }
}
