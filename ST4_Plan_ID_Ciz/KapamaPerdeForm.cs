using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Beykent.Monolit.Girdi;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    /// <summary>KAPAMADETAY form seçenekleri — oturum + %LocalAppData%\ST4_Plan_ID_Ciz\kapamaperde.ini.</summary>
    internal static class KapamaPerdeSettings
    {
        /// <summary>true = görünüşte temel hatılı çizilir; false = çizilmez.</summary>
        public static bool TemelHatili { get; set; } = true;
        /// <summary>true = 1/100 paftaya konacak: çizim (yazı/ölçü dahil) ×2.</summary>
        public static bool Pafta100 { get; set; }
        /// <summary>true = birebir aynı perde hatları tek antette; donatı yazılarına adet çarpanı.</summary>
        public static bool BenzerleriBirlestir { get; set; } = true;

        static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var s = PlanFormSettings.Load("kapamaperde");
            TemelHatili = s.Bool("TemelHatili", TemelHatili);
            Pafta100 = s.Bool("Pafta100", Pafta100);
            BenzerleriBirlestir = s.Bool("BenzerleriBirlestir", BenzerleriBirlestir);
        }

        public static void Save()
        {
            EnsureLoaded();
            PlanFormSettings.Load("kapamaperde").Set("TemelHatili", TemelHatili).Set("Pafta100", Pafta100)
                .Set("BenzerleriBirlestir", BenzerleriBirlestir).Save();
        }
    }

    /// <summary>
    /// KAPAMADETAY modal form: temel hatılı olsun / olmasın.
    /// </summary>
    internal sealed class KapamaPerdeForm : Beykent.Monolit.MonolitForm
    {
        private readonly SegmentSecim _hatil;
        private readonly SegmentSecim _pafta;
        private readonly CheckBox _chkBenzer;

        /// <summary>Modal form. true = Çiz (ayarlar kaydedildi).</summary>
        public static bool ShowModalAndAccept()
        {
            KapamaPerdeSettings.EnsureLoaded();
            using (var f = new KapamaPerdeForm())
            {
                DialogResult dr;
                try { dr = AcApp.ShowModalDialog(f); }
                catch { dr = f.ShowDialog(); }
                return dr == DialogResult.OK;
            }
        }

        private KapamaPerdeForm()
        {
            KapamaPerdeSettings.EnsureLoaded();
            Text = "Kapama Perde";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            Font = new Font("Segoe UI", 8.5f);
            KeyPreview = true;
            DialogResult = DialogResult.Cancel;

            int y = 54;
            // Ortak kural 3/4: hatıl var/yok ve pafta ölçeği segment; KapamaPerdeSettings anlamı aynı.
            _hatil = new SegmentSecim("Hatıllı", "Hatılsız") { Secili = KapamaPerdeSettings.TemelHatili ? 0 : 1 };
            _pafta = new SegmentSecim("1/50 pafta", "1/100 pafta (×2)") { Secili = KapamaPerdeSettings.Pafta100 ? 1 : 0 };
            _chkBenzer = new CheckBox
            {
                Text = "Benzerleri beraber çiz",
                AutoSize = true,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                BackColor = Color.Transparent,
                Checked = KapamaPerdeSettings.BenzerleriBirlestir
            };
            Controls.Add(_chkBenzer);
            y += 30;

            var btnCiz = MkButton("Çiz", 12, y, 100, true);
            btnCiz.Click += (s, e) =>
            {
                PushSettings();
                DialogResult = DialogResult.OK;
                Close();
            };
            var btnIptal = MkButton("İptal", 120, y, 80, false);
            btnIptal.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(btnCiz);
            Controls.Add(btnIptal);
            AcceptButton = btnCiz;
            CancelButton = btnIptal;
            ClientSize = new Size(270, y + 40);

            try
            {
                Location = new Point(
                    Screen.PrimaryScreen.WorkingArea.Left + 80,
                    Screen.PrimaryScreen.WorkingArea.Top + 60);
            }
            catch
            {
                StartPosition = FormStartPosition.CenterScreen;
            }

            // Kompakt, içerik odaklı form düzeni.
            var ui = BeginCompact(270);
            ui.Section("Hatıl durumu").Control(_hatil);
            ui.Tip(_hatil, "Temel hatılı olsun / olmasın");
            ui.Section("Benzer perdeler").Options(_chkBenzer);
            ui.Tip(_chkBenzer, "Birebir aynı perde hatları tek antette çizilir; donatı yazılarına adet çarpanı (2x…) eklenir.");
            ui.Section("Pafta ölçeği · detay 1/25").Control(_pafta);
            ui.Footer(btnCiz, btnIptal);
            ui.Finish();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            if ((keyData == Keys.Enter || keyData == Keys.Return) && !(ActiveControl is Button))
            {
                PushSettings();
                DialogResult = DialogResult.OK;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void PushSettings()
        {
            KapamaPerdeSettings.TemelHatili = _hatil.Secili == 0;
            KapamaPerdeSettings.Pafta100 = _pafta.Secili == 1;
            KapamaPerdeSettings.BenzerleriBirlestir = _chkBenzer.Checked;
            KapamaPerdeSettings.Save();
        }

        private static Button MkButton(string text, int x, int y, int w, bool accent)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Beykent.Monolit.StandardFormTheme.BgAlt,
                ForeColor = accent ? Beykent.Monolit.StandardFormTheme.Accent : Beykent.Monolit.StandardFormTheme.Text,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Beykent.Monolit.StandardFormTheme.InputBorder;
            b.FlatAppearance.MouseOverBackColor = Beykent.Monolit.StandardFormTheme.BgHover;
            return b;
        }
    }
}
