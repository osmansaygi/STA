using System;
using System.Drawing;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    /// <summary>
    /// Döşeme Donatı formu — katmanlar sabit (KIRIS/KOLON/PERDE/DOSEME SINIRI/KALIP BOSLUK/MERDIVEN).
    /// </summary>
    internal sealed class DosemeDonatiForm : Form
    {
        private static DosemeDonatiForm _instance;

        private readonly RadioButton _rbSerbest;
        private readonly RadioButton _rbX;
        private readonly RadioButton _rbY;
        private readonly Button _btnRun;
        private readonly Button _btnClose;
        private bool _starting;

        public static void ShowOrActivate()
        {
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new DosemeDonatiForm();
                try { AcApp.ShowModelessDialog(_instance); }
                catch { _instance.Show(); }
            }
            else
            {
                _instance._starting = false;
                _instance.Visible = true;
                _instance.BringToFront();
            }
        }

        public static void Shutdown()
        {
            if (_instance == null) return;
            try { _instance.Close(); } catch { }
            _instance = null;
        }

        public static void HideTemporarily()
        {
            if (_instance == null || _instance.IsDisposed) return;
            try
            {
                if (_instance.InvokeRequired)
                {
                    _instance.BeginInvoke(new Action(HideTemporarily));
                    return;
                }
                _instance.Visible = false;
            }
            catch { }
        }

        public static void ShowAfterPick()
        {
            try { ShowOrActivate(); } catch { }
        }

        private DosemeDonatiForm()
        {
            Text = "Döşeme Donatı";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(360, 240);
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = Color.White;
            Font = new SdFont("Segoe UI", 9f);
            KeyPreview = true;

            DosemeKonsolSettings.EnsureLoaded();

            int y = 12;
            Controls.Add(MakeLabel("Katmanlar: KIRIS / KOLON / PERDE", 14, y)); y += 18;
            Controls.Add(MakeLabel("DOSEME SINIRI · KALIP BOSLUK · MERDIVEN (sabit)", 14, y)); y += 22;

            Controls.Add(MakeLabel("Donatı yönü (GPR yazı: X kırmızı / Y mavi)", 14, y)); y += 20;
            _rbSerbest = MakeRadio("Serbest", 14, y);
            _rbX = MakeRadio("X yönü", 110, y);
            _rbY = MakeRadio("Y yönü", 200, y);
            ApplyYonToRadios(DosemeDonatiRunner.Yon);
            EventHandler yonChanged = (s, e) =>
            {
                if (_rbX.Checked) DosemeDonatiRunner.Yon = DosemeDonatiRunner.DonatiYonu.X;
                else if (_rbY.Checked) DosemeDonatiRunner.Yon = DosemeDonatiRunner.DonatiYonu.Y;
                else DosemeDonatiRunner.Yon = DosemeDonatiRunner.DonatiYonu.Serbest;
            };
            _rbSerbest.CheckedChanged += yonChanged;
            _rbX.CheckedChanged += yonChanged;
            _rbY.CheckedChanged += yonChanged;
            Controls.Add(_rbSerbest);
            Controls.Add(_rbX);
            Controls.Add(_rbY);
            y += 32;

            var btnAyar = MakeButton("Konsol donatı ayarları…", 14, y, 312);
            btnAyar.Click += (s, e) =>
            {
                using (var f = new DosemeKonsolAyarForm())
                {
                    try { f.ShowDialog(this); }
                    catch { f.ShowDialog(); }
                }
            };
            Controls.Add(btnAyar);
            y += 36;

            _btnRun = MakeButton("Donatı çiz", 14, y, 210);
            _btnRun.Font = new SdFont("Segoe UI", 9f, FontStyle.Bold);
            _btnRun.ForeColor = AcadPaletteTheme.Accent;
            _btnRun.Click += OnRun;
            _btnClose = MakeButton("Kapat", 232, y, 94);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnRun);
            Controls.Add(_btnClose);

            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                    _instance = null;
            };

            try
            {
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 110,
                    Screen.PrimaryScreen.WorkingArea.Top + 110);
            }
            catch { StartPosition = FormStartPosition.CenterScreen; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            if (keyData == Keys.Enter || keyData == Keys.Return || keyData == Keys.Space)
            {
                OnRun(this, EventArgs.Empty);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ApplyYonToRadios(DosemeDonatiRunner.DonatiYonu yon)
        {
            _rbSerbest.Checked = yon == DosemeDonatiRunner.DonatiYonu.Serbest;
            _rbX.Checked = yon == DosemeDonatiRunner.DonatiYonu.X;
            _rbY.Checked = yon == DosemeDonatiRunner.DonatiYonu.Y;
            if (!_rbSerbest.Checked && !_rbX.Checked && !_rbY.Checked)
                _rbSerbest.Checked = true;
        }

        private void OnRun(object sender, EventArgs e)
        {
            if (_starting) return;
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                if (_rbX.Checked) DosemeDonatiRunner.Yon = DosemeDonatiRunner.DonatiYonu.X;
                else if (_rbY.Checked) DosemeDonatiRunner.Yon = DosemeDonatiRunner.DonatiYonu.Y;
                else DosemeDonatiRunner.Yon = DosemeDonatiRunner.DonatiYonu.Serbest;
                _starting = true;
                HideTemporarily();
                doc.SendStringToExecute("DOSEMEDONATICIZ ", true, false, false);
            }
            catch
            {
                _starting = false;
            }
        }

        private static RadioButton MakeRadio(string text, int x, int y) => new RadioButton
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };

        private static Label MakeLabel(string text, int x, int y) => new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.White,
            BackColor = Color.Transparent
        };

        private Button MakeButton(string text, int x, int y, int w)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = AcadPaletteTheme.BgAlt,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = AcadPaletteTheme.InputBorder;
            b.FlatAppearance.MouseOverBackColor = AcadPaletteTheme.BgHover;
            return b;
        }
    }
}
