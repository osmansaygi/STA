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

        static bool _loaded;

        static string IniPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ST4_Plan_ID_Ciz");
            try { TestStore.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "kapamaperde.ini");
        }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string path = IniPath();
                if (!TestStore.Exists(path)) return;
                foreach (string raw in TestStore.ReadAllLines(path))
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    if (string.Equals(key, "TemelHatili", StringComparison.OrdinalIgnoreCase))
                        TemelHatili = val != "0" && !string.Equals(val, "false", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
        }

        public static void Save()
        {
            EnsureLoaded();
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("TemelHatili=" + (TemelHatili ? "1" : "0"));
                TestStore.WriteAllText(IniPath(), sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }

    /// <summary>
    /// KAPAMADETAY modal form: temel hatılı olsun / olmasın.
    /// </summary>
    internal sealed class KapamaPerdeForm : Beykent.Monolit.MonolitForm
    {
        private readonly RadioButton _rbHatiliVar;
        private readonly RadioButton _rbHatiliYok;

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
