using System;
using System.Drawing;
using System.Windows.Forms;
using Beykent.Monolit.Girdi;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    /// <summary>Kiriş açılım formuyla aynı düzen: Antetli/Antetsiz. Sürekli temel kat seçmez.</summary>
    internal sealed class SurekliTemelAcilimForm : Beykent.Monolit.MonolitForm
    {
        private static bool _sonAntetli = true, _sonBirlestir = true;
        private readonly SegmentSecim _duzen, _temel, _pafta;
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
            var saved = PlanFormSettings.Load("sureklitemelacilim");
            _sonAntetli = saved.Bool("Antetli", _sonAntetli);
            _sonBirlestir = saved.Bool("Birlestir", _sonBirlestir);
            Text = "Sürekli Temel Açılımları";
            ClientSize = new Size(420, 220);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Beykent.Monolit.StandardFormTheme.Text;

            // Ortak kural 3/4: birbirini dışlayan seçimler segment; kayıt anahtarları ve anlamları aynı.
            _duzen = new SegmentSecim("Antetli", "Antetsiz") { Secili = _sonAntetli ? 0 : 1 };
            // Birleştir: aynı hizadaki (orta çizgileri buluşan) temeller tek açılım. Ayrı: her temel kendi açılımı.
            _temel = new SegmentSecim("Aynı hizadakileri birleştir", "Her temel ayrı") { Secili = _sonBirlestir ? 0 : 1 };
            _pafta = new SegmentSecim("1/50 pafta", "1/100 pafta (×2)") { Secili = saved.Bool("Pafta100", false) ? 1 : 0 };

            var draw = new Button { Text = "Çiz", Left = 216, Top = 160, Width = 90, Height = 30 };
            var cancel = new Button { Text = "İptal", Left = 314, Top = 160, Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            draw.Click += (s, e) =>
            {
                bool antetli = _duzen.Secili == 0, birlestir = _temel.Secili == 0, yuz = _pafta.Secili == 1;
                Secenekler = new KirisAcilimSecenekleri { Antetli = antetli, SeciliKatNo = null,
                    TemelBirlestir = birlestir, Pafta100 = yuz };
                _sonAntetli = antetli;
                _sonBirlestir = birlestir;
                PlanFormSettings.Load("sureklitemelacilim").Set("Antetli", _sonAntetli).Set("Birlestir", _sonBirlestir)
                    .Set("Pafta100", yuz).Save();
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.AddRange(new Control[] { draw, cancel });
            AcceptButton = draw;
            CancelButton = cancel;

            var ui = BeginCompact(340);
            ui.Section("Temeller").Control(_temel);
            ui.Section("Çizim düzeni · 1/25").Control(_duzen);
            ui.Section("Pafta ölçeği").Control(_pafta);
            ui.Section(null).Hint("Beton, kolon, donatı ve etriye açılımları.\nMontaj altta; düz donatı üstte çizilir.");
            ui.Footer(draw, cancel);
            ui.Finish();
        }
    }
}
