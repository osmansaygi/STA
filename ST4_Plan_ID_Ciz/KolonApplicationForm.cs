using System;
using System.Drawing;
using System.Windows.Forms;
using Beykent.Monolit.Girdi;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    internal sealed class KolonApplicationForm : Beykent.Monolit.MonolitForm
    {
        // Son onaylı seçimler AutoCAD yeniden açıldığında da korunur.
        internal static bool BenzerKatlariBeraberCiz { get; private set; } = true;
        internal static bool BilgiYazilariniYaz { get; private set; } = true;
        internal static bool KolonDuseyAcilimiEkle { get; private set; }
        internal static bool KapamaPerdeAcilimiEkle { get; private set; }
        /// <summary>KOLONST4: formda seçilen ölçek.</summary>
        internal static bool Olcek100 { get; private set; }

        /// <param name="olcekSor">true: formun başında 1/50 – 1/100 seçimi (KOLONST4).</param>
        internal static bool ShowModalAndAccept(bool olcekSor = false)
        {
            using (var form = new KolonApplicationForm(olcekSor))
                return AcApp.ShowModalDialog(form) == DialogResult.OK;
        }

        private KolonApplicationForm(bool olcekSor)
        {
            var saved = PlanFormSettings.Load("kolonaplikasyon");
            BenzerKatlariBeraberCiz = saved.Bool("BenzerKatlariBeraberCiz", BenzerKatlariBeraberCiz);
            BilgiYazilariniYaz = saved.Bool("BilgiYazilariniYaz", BilgiYazilariniYaz);
            KolonDuseyAcilimiEkle = saved.Bool("KolonDuseyAcilimiEkle", KolonDuseyAcilimiEkle);
            KapamaPerdeAcilimiEkle = saved.Bool("KapamaPerdeAcilimiEkle", KapamaPerdeAcilimiEkle);
            Olcek100 = saved.Bool("Olcek100", Olcek100);
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

            // Ortak kural 8 (bir ayar tek formda): "Benzer kapama perdelerini beraber çiz", "Başlık kademeli geçiş"
            // + geçiş kat sayısı ve kolon etriye "Aralık" bu formdan kaldırıldı. Çizim akışı bunları kontrol
            // olmadan ortak ini'den okur: KapamaPerdeSettings (kapamaperde.ini, KAPAMADETAY formu) ve
            // KolonDuseySettings (kolondusey.ini, KOLONDUSEY formu). Yükleme burada garanti edilir.
            KapamaPerdeSettings.EnsureLoaded();
            KolonDuseySettings.EnsureLoaded();

            // Plan ölçeği ayrı çizim yoludur (×2 büyütme değil): etiket düz "1/50 / 1/100".
            var olcek = new SegmentSecim("1/50", "1/100") { Secili = Olcek100 ? 1 : 0 };

            draw.Click += (s, e) =>
            {
                var kayit = PlanFormSettings.Load("kolonaplikasyon");
                if (olcekSor)
                {
                    Olcek100 = olcek.Secili == 1;
                    kayit.Set("Olcek100", Olcek100);
                }
                BenzerKatlariBeraberCiz = merge.Checked;
                BilgiYazilariniYaz = info.Checked;
                KolonDuseyAcilimiEkle = vertical.Checked;
                KapamaPerdeAcilimiEkle = kapama.Checked;
                kayit.Set("BenzerKatlariBeraberCiz", BenzerKatlariBeraberCiz)
                    .Set("BilgiYazilariniYaz", BilgiYazilariniYaz).Set("KolonDuseyAcilimiEkle", KolonDuseyAcilimiEkle)
                    .Set("KapamaPerdeAcilimiEkle", KapamaPerdeAcilimiEkle).Save();
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.AddRange(new Control[] { merge, description, info, vertical, kapama, draw, cancel });
            AcceptButton = draw;
            CancelButton = cancel;

            var ui = BeginCompact(390);
            ui.Section("Pafta düzeni").Choices(merge, info);
            ui.Section(null).Control(description);
            ui.Section("Eklenecek açılımlar").Choices(vertical, kapama)
                .Hint("Etriye aralığı ve perde başlığı geçişi KOLONDUSEY,\nbenzer kapama perdeleri KAPAMADETAY ayarından alınır.");
            if (olcekSor) ui.Section("Ölçek").Control(olcek);
            ui.Footer(draw, cancel);
            ui.Finish();
        }
    }
}
