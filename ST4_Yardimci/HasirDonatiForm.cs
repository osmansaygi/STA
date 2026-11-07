using System;
using System.Globalization;
using System.Windows.Forms;
using Beykent.Monolit.Girdi;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;
using SdPoint = System.Drawing.Point;
using SdSize = System.Drawing.Size;

namespace ST4Yardimci
{
    /// <summary>Hasır donatı ayar formu — tip, standart ebat, bindirme, paspayı.</summary>
    internal sealed class HasirDonatiForm : Beykent.Monolit.MonolitForm
    {
        private static HasirDonatiForm _instance;

        private readonly ComboBox _cmbTip;
        private readonly OlcuKutusu _numBoy;
        private readonly OlcuKutusu _numEn;
        private readonly OlcuKutusu _numBindirme;
        private readonly OlcuKutusu _numPas;
        private readonly SegmentSecim _katman;
        private readonly GirdiDenetci Denetci = new GirdiDenetci();
        private bool _starting;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("HASIRDONATI")) return;
            HasirDonatiSettings.EnsureLoaded();
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new HasirDonatiForm();
                try { AcApp.ShowModelessDialog(_instance); }
                catch { _instance.Show(); }
            }
            else
            {
                _instance._starting = false;
                _instance.LoadFromSettings();
                _instance.Visible = true;
                _instance.BringToFront();
            }
            BaFormSession.Attach("HASIRDONATI", _instance);
            BaFormSession.EndPick("HASIRDONATI");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("HASIRDONATI");
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
                BaFormSession.BeginPick("HASIRDONATI");
                _instance.Visible = false;
            }
            catch { }
        }

        private HasirDonatiForm()
        {
            Text = "Hasır Donatı";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            Font = new SdFont("Segoe UI", 9f);
            KeyPreview = true;

            _katman = new SegmentSecim("Tek hasır", "Çift hasır");
            _cmbTip = MakeTipCombo(HasirDonatiSettings.Tip);
            Controls.Add(_cmbTip);
            // Sınırlar ve ondalıklar eski NumericUpDown ile aynı.
            _numBoy = Denetci.Ekle(new OlcuKutusu("Standart boy", "cm", 50, 2000, 500));
            _numEn = Denetci.Ekle(new OlcuKutusu("Standart en", "cm", 50, 2000, 215));
            _numBindirme = Denetci.Ekle(new OlcuKutusu("Bindirme boyu", "cm", 5, 200, 45));
            _numPas = Denetci.Ekle(new OlcuKutusu("Paspayı", "cm", 0, 20, 3, 1, 0.5));
            LoadFromSettings();
            Denetci.HataKurali(() => _numBindirme.Deger >= _numBoy.Deger || _numBindirme.Deger >= _numEn.Deger
                ? "Bindirme, boy ve en'den küçük olmalı" : null, _numBindirme, _numBoy, _numEn);

            var btnRun = MakeButton("Hasır çiz", true);
            btnRun.Click += (s, e) => OnRun();
            var btnClose = MakeButton("Kapat", false);
            btnClose.Click += (s, e) => Close();

            FormClosing += (s, e) =>
            {
                TryPushQuiet();
                HasirDonatiSettings.Save();
            };
            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                {
                    _instance = null;
                    BaFormSession.Release("HASIRDONATI");
                }
            };

            try
            {
                Location = new SdPoint(Screen.PrimaryScreen.WorkingArea.Left + 160,
                    Screen.PrimaryScreen.WorkingArea.Top + 100);
            }
            catch
            {
                StartPosition = FormStartPosition.CenterScreen;
            }

            var ui = BeginCompact(300);
            ui.Section("Hasır ayarları")
                .Control(_katman)
                .Field("Hasır çeşidi", _cmbTip)
                .Field("Boy / en", _numBoy, _numEn)
                .Field("Bindirme", _numBindirme)
                .Field("Paspayı", _numPas);
            ui.Section(null).Control(Denetci.Serit, 44);
            ui.Footer(btnRun, btnClose);
            ui.Finish();
            Denetci.Guncelle();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            if ((keyData == Keys.Enter || keyData == Keys.Return) && !(ActiveControl is Button))
            {
                OnRun();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void LoadFromSettings()
        {
            _katman.Secili = HasirDonatiSettings.CiftHasir ? 1 : 0;
            SelectTip(_cmbTip, HasirDonatiSettings.Tip);
            _numBoy.Deger = Kirp(Math.Round(HasirDonatiSettings.BoyCm), 50, 2000);
            _numEn.Deger = Kirp(Math.Round(HasirDonatiSettings.EnCm), 50, 2000);
            _numBindirme.Deger = Kirp(Math.Round(HasirDonatiSettings.BindirmeCm), 5, 200);
            _numPas.Deger = Kirp(Math.Round(HasirDonatiSettings.PaspayiCm, 1), 0, 20);
        }

        private void PushAll()
        {
            HasirDonatiSettings.Tip = _cmbTip.SelectedItem as string ?? "Q188/188";
            HasirDonatiSettings.BoyCm = _numBoy.Deger;
            HasirDonatiSettings.EnCm = _numEn.Deger;
            HasirDonatiSettings.BindirmeCm = _numBindirme.Deger;
            HasirDonatiSettings.PaspayiCm = _numPas.Deger;
            HasirDonatiSettings.CiftHasir = _katman.Secili == 1;
        }

        private void TryPushQuiet()
        {
            try
            {
                // Kapanışta geçersiz kutu son geçerli değerini tutar; hata varsa ayar değiştirilmez.
                if (!Denetci.Uygun) return;
                string tip = HasirDonatiSettings.Tip;
                PushAll();
                if (_cmbTip.SelectedItem == null) HasirDonatiSettings.Tip = tip;
            }
            catch { }
        }

        private void OnRun()
        {
            if (_starting) return;
            if (!Denetci.Onayla()) return;
            PushAll();
            HasirDonatiSettings.Save();
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                _starting = true;
                HideTemporarily();
                doc.SendStringToExecute("HASIRDONATICIZ ", true, false, false);
            }
            catch
            {
                _starting = false;
            }
        }

        private static void SelectTip(ComboBox cmb, string tip)
        {
            if (cmb == null) return;
            string t = tip ?? "Q188/188";
            int i = cmb.Items.IndexOf(t);
            if (i < 0)
            {
                for (int k = 0; k < cmb.Items.Count; k++)
                {
                    if (string.Equals(cmb.Items[k] as string, t, StringComparison.OrdinalIgnoreCase))
                    {
                        i = k;
                        break;
                    }
                }
            }
            cmb.SelectedIndex = i >= 0 ? i : Math.Max(0, cmb.Items.IndexOf("Q188/188"));
        }

        private static double Kirp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));

        private ComboBox MakeTipCombo(string selected)
        {
            var c = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                FlatStyle = FlatStyle.Flat,
                Font = Font
            };
            foreach (string tip in HasirDonatiSettings.TipListesi)
                c.Items.Add(tip);
            SelectTip(c, selected);
            return c;
        }

        private Button MakeButton(string text, bool accent)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = Beykent.Monolit.StandardFormTheme.BgAlt,
                ForeColor = accent ? Beykent.Monolit.StandardFormTheme.Accent : Beykent.Monolit.StandardFormTheme.Text,
                Font = accent ? new SdFont("Segoe UI", 9f, System.Drawing.FontStyle.Bold) : Font,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = Beykent.Monolit.StandardFormTheme.InputBorder;
            b.FlatAppearance.BorderSize = 1;
            return b;
        }
    }
}
