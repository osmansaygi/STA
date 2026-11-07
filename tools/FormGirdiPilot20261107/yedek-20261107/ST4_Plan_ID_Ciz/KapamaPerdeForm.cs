using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
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
        private readonly RadioButton _rbHatiliVar;
        private readonly RadioButton _rbHatiliYok;
        private readonly RadioButton[] _pafta;
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

            int y = 10;
            Controls.Add(MkLabel("Temel hatılı", 12, y + 3, 70));
            y += 22;

            _rbHatiliVar = MkRadio("Temel hatılı olsun", 12, y, KapamaPerdeSettings.TemelHatili);
            Controls.Add(_rbHatiliVar);
            y += 22;

            _rbHatiliYok = MkRadio("Temel hatılı olmasın", 12, y, !KapamaPerdeSettings.TemelHatili);
            Controls.Add(_rbHatiliYok);
            _pafta = BeykentOrtak.PaftaOlcek.Secenekler(KapamaPerdeSettings.Pafta100);
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
            _rbHatiliVar.Text = "Hatıllı"; _rbHatiliYok.Text = "Hatılsız";
            ui.Section("Hatıl durumu").Options(_rbHatiliVar, _rbHatiliYok);
            ui.Section("Pafta ölçeği · detay 1/25").Options(_pafta);
            ui.Section("Benzer perdeler").Options(_chkBenzer);
            ui.Tip(_chkBenzer, "Birebir aynı perde hatları tek antette çizilir; donatı yazılarına adet çarpanı (2x…) eklenir.");
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
            if (keyData == Keys.Enter || keyData == Keys.Return)
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
            KapamaPerdeSettings.TemelHatili = _rbHatiliVar.Checked;
            KapamaPerdeSettings.Pafta100 = _pafta[1].Checked;
            KapamaPerdeSettings.BenzerleriBirlestir = _chkBenzer.Checked;
            KapamaPerdeSettings.Save();
        }

        private static Label MkLabel(string text, int x, int y, int w) => new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = false,
            Size = new Size(w, 18),
            ForeColor = Beykent.Monolit.StandardFormTheme.TextMuted,
            BackColor = Color.Transparent
        };

        private static RadioButton MkRadio(string text, int x, int y, bool on) => new RadioButton
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Beykent.Monolit.StandardFormTheme.Text,
            BackColor = Color.Transparent,
            Checked = on
        };

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
