using System;
using System.Drawing;
using System.Windows.Forms;
using Beykent.Monolit.Girdi;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    /// <summary>Temel/Kalıp planı ve kalıp denemeleri için 1/50 – 1/100 seçimi. Son seçim komut başına korunur.</summary>
    internal sealed class PlanOlcekForm : Beykent.Monolit.MonolitForm
    {
        private readonly SegmentSecim _olcek;
        private bool Yuz => _olcek.Secili == 1;

        /// <param name="anahtar">PlanFormSettings bölümü (komut başına ayrı hatırlanır).</param>
        internal static bool TrySor(string anahtar, string baslik, string ipucu, out bool yuz)
        {
            yuz = false;
            using (var form = new PlanOlcekForm(anahtar, baslik, ipucu))
            {
                if (AcApp.ShowModalDialog(form) != DialogResult.OK) return false;
                yuz = form.Yuz;
                return true;
            }
        }

        private PlanOlcekForm(string anahtar, string baslik, string ipucu)
        {
            bool sonYuz = PlanFormSettings.Load(anahtar).Bool("Olcek100", false);
            Text = baslik;
            ClientSize = new Size(360, 170);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Beykent.Monolit.StandardFormTheme.Text;

            // Plan ölçeği segment seçimi (kayıt anahtarı ve anlamı aynı: Olcek100). Plan 1/100 ayrı çizim
            // yoludur (×2 büyütme değil), bu yüzden etiket "1/100 pafta (×2)" değil düz "1/100".
            _olcek = new SegmentSecim("1/50", "1/100") { Secili = sonYuz ? 1 : 0 };
            var draw = new Button { Text = "Çiz", Left = 160, Top = 120, Width = 90, Height = 30 };
            var cancel = new Button { Text = "İptal", Left = 258, Top = 120, Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            draw.Click += (s, e) =>
            {
                PlanFormSettings.Load(anahtar).Set("Olcek100", Yuz).Save();
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.AddRange(new Control[] { draw, cancel });
            AcceptButton = draw;
            CancelButton = cancel;

            var ui = BeginCompact(300);
            ui.Section("Ölçek").Control(_olcek);
            if (!string.IsNullOrEmpty(ipucu)) ui.Section(null).Hint(ipucu);
            ui.Footer(draw, cancel);
            ui.Finish();
        }
    }
}
