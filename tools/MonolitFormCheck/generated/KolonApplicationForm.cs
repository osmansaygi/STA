using System;
using System.Drawing;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    internal sealed class KolonApplicationForm : Beykent.Monolit.MonolitForm
    {
        // Oturum boyunca son seçim korunur.
        internal static bool BenzerKatlariBeraberCiz { get; private set; } = true;
        internal static bool BilgiYazilariniYaz { get; private set; } = true;
        internal static bool KolonDuseyAcilimiEkle { get; private set; }
        internal static bool KapamaPerdeAcilimiEkle { get; private set; }

        internal static bool ShowModalAndAccept()
        {
            using (var form = new KolonApplicationForm())
                return AcApp.ShowModalDialog(form) == DialogResult.OK;
        }

        private KolonApplicationForm()
        {
            Text = "Kolon Aplikasyon Planı";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(430, 275);

            var merge = new CheckBox
            {
                Text = "Benzer katları beraber çiz",
                Location = new Point(18, 18), AutoSize = true,
                Checked = BenzerKatlariBeraberCiz
            };
            var description = new Label
            {
                Text = "Düşey açılımdaki benzerlik hesabını kullanır.\nBirleşen katların adları aynı paftada gösterilir.",
                Location = new Point(38, 48), Size = new Size(375, 42),
                ForeColor = Beykent.Monolit.StandardFormTheme.TextMuted
            };
            var info = new CheckBox
            {
                Text = "Donatı bilgilerini ve notları yaz",
                Location = new Point(18, 102), AutoSize = true,
                Checked = BilgiYazilariniYaz
            };
            var draw = new Button
            {
                Text = "Çiz", Location = new Point(218, 225), Size = new Size(90, 30),
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg, ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                FlatStyle = FlatStyle.Flat
            };
            var cancel = new Button
            {
                Text = "İptal", Location = new Point(320, 225), Size = new Size(90, 30),
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg, ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel
            };
            var vertical = new CheckBox
            {
                Text = "Kolon düşey açılımı ekle (1/25, antetli, sağa)",
                Location = new Point(18, 137), AutoSize = true,
                Checked = KolonDuseyAcilimiEkle
            };
            var kapama = new CheckBox
            {
                Text = "Kapama perde açılımı ekle (ilk paftanın sağına)",
                Location = new Point(18, 172), AutoSize = true,
                Checked = KapamaPerdeAcilimiEkle
            };
            draw.Click += (s, e) =>
            {
                BenzerKatlariBeraberCiz = merge.Checked;
                BilgiYazilariniYaz = info.Checked;
                KolonDuseyAcilimiEkle = vertical.Checked;
                KapamaPerdeAcilimiEkle = kapama.Checked;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.AddRange(new Control[] { merge, description, info, vertical, kapama, draw, cancel });
            AcceptButton = draw;
            CancelButton = cancel;

            // Kompakt, içerik odaklı form düzeni.
            var ui = BeginCompact(390);
            ui.Section("Pafta düzeni").Choices(merge, info);
            ui.Section("Eklenecek açılımlar").Choices(vertical, kapama);
            ui.Section(null).Control(description);
            ui.Footer(draw, cancel);
            ui.Finish();
        }
    }
}
