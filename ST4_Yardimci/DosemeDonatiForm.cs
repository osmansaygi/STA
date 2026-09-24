using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    internal enum DosemeTipi
    {
        Pilesiz = 0,
        Pileli = 1
    }

    internal static class DosemeDonatiSettings
    {
        private static readonly object Sync = new object();
        private static readonly List<string> Layers = new List<string>();

        public static DosemeTipi Tip { get; set; } = DosemeTipi.Pilesiz;

        public static IReadOnlyList<string> GetLayers()
        {
            lock (Sync) return Layers.ToArray();
        }

        public static void SetLayers(IEnumerable<string> names)
        {
            lock (Sync)
            {
                Layers.Clear();
                if (names == null) return;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string n in names)
                {
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    string t = n.Trim();
                    if (seen.Add(t)) Layers.Add(t);
                }
            }
        }

        public static bool AddLayer(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string t = name.Trim();
            lock (Sync)
            {
                foreach (string e in Layers)
                    if (string.Equals(e, t, StringComparison.OrdinalIgnoreCase))
                        return false;
                Layers.Add(t);
                return true;
            }
        }

        public static bool RemoveLayer(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            lock (Sync)
            {
                for (int i = Layers.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(Layers[i], name.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        Layers.RemoveAt(i);
                        return true;
                    }
                }
            }
            return false;
        }
    }

    /// <summary>Döşeme donatı ayar formu: pileli/pilesiz + katman listesi.</summary>
    internal sealed class DosemeDonatiForm : Form
    {
        private static DosemeDonatiForm _instance;

        private readonly RadioButton _rbPilesiz;
        private readonly RadioButton _rbPileli;
        private readonly ListBox _lstLayers;
        private readonly Button _btnAdd;
        private readonly Button _btnRemove;
        private readonly Button _btnRun;
        private readonly Button _btnClose;
        private bool _syncing;

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
                _instance.RefreshUi();
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

        public static void NotifyLayerAdded(string layerName)
        {
            if (_instance == null || _instance.IsDisposed || string.IsNullOrWhiteSpace(layerName))
                return;
            try
            {
                if (_instance.InvokeRequired)
                {
                    _instance.BeginInvoke(new Action(() => NotifyLayerAdded(layerName)));
                    return;
                }
                foreach (object o in _instance._lstLayers.Items)
                    if (o is string s && string.Equals(s, layerName, StringComparison.OrdinalIgnoreCase))
                        return;
                _instance._lstLayers.Items.Add(layerName);
                _instance.PushSettings();
            }
            catch { }
        }

        private DosemeDonatiForm()
        {
            Text = "Döşeme Donatı";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            Size = new Size(340, 400);
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = Color.White;
            Font = new SdFont("Segoe UI", 9f);

            int y = 12;
            Controls.Add(MakeLabel("Tip", 14, y)); y += 22;
            _rbPilesiz = new RadioButton
            {
                Text = "Pilesiz",
                Location = new Point(14, y),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Checked = DosemeDonatiSettings.Tip == DosemeTipi.Pilesiz
            };
            _rbPileli = new RadioButton
            {
                Text = "Pileli",
                Location = new Point(120, y),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Checked = DosemeDonatiSettings.Tip == DosemeTipi.Pileli
            };
            _rbPilesiz.CheckedChanged += (s, e) => PushSettings();
            _rbPileli.CheckedChanged += (s, e) => PushSettings();
            Controls.Add(_rbPilesiz);
            Controls.Add(_rbPileli);
            y += 32;

            Controls.Add(MakeLabel("Katmanlar (arasında çalışılacak)", 14, y)); y += 22;
            _lstLayers = new ListBox
            {
                Location = new Point(14, y),
                Size = new Size(296, 140),
                BackColor = AcadPaletteTheme.InputBg,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };
            Controls.Add(_lstLayers);
            y += 148;

            _btnAdd = MakeButton("Katman ekle", 14, y, 140);
            _btnRemove = MakeButton("Kaldır", 170, y, 140);
            _btnAdd.Click += OnAddLayer;
            _btnRemove.Click += OnRemoveLayer;
            Controls.Add(_btnAdd);
            Controls.Add(_btnRemove);
            y += 40;

            _btnRun = MakeButton("Devam (çizim sonra)", 14, y, 200);
            _btnRun.ForeColor = AcadPaletteTheme.Accent;
            _btnRun.Click += OnRun;
            _btnClose = MakeButton("Kapat", 230, y, 80);
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
            catch
            {
                StartPosition = FormStartPosition.CenterScreen;
            }

            RefreshUi();
        }

        private void RefreshUi()
        {
            _syncing = true;
            try
            {
                _rbPilesiz.Checked = DosemeDonatiSettings.Tip == DosemeTipi.Pilesiz;
                _rbPileli.Checked = DosemeDonatiSettings.Tip == DosemeTipi.Pileli;
                _lstLayers.Items.Clear();
                foreach (string lyr in DosemeDonatiSettings.GetLayers())
                    _lstLayers.Items.Add(lyr);
            }
            finally
            {
                _syncing = false;
                PushSettings();
            }
        }

        private void PushSettings()
        {
            if (_syncing) return;
            DosemeDonatiSettings.Tip = _rbPileli.Checked ? DosemeTipi.Pileli : DosemeTipi.Pilesiz;
            var layers = new List<string>();
            foreach (object o in _lstLayers.Items)
                if (o is string s) layers.Add(s);
            DosemeDonatiSettings.SetLayers(layers);
        }

        private void OnAddLayer(object sender, EventArgs e)
        {
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                doc.SendStringToExecute("DOSEMEKATMANEKLE ", true, false, false);
            }
            catch { }
        }

        private void OnRemoveLayer(object sender, EventArgs e)
        {
            if (_lstLayers.SelectedItem is string name)
            {
                DosemeDonatiSettings.RemoveLayer(name);
                _lstLayers.Items.Remove(name);
                PushSettings();
            }
        }

        private void OnRun(object sender, EventArgs e)
        {
            PushSettings();
            if (DosemeDonatiSettings.GetLayers().Count == 0)
            {
                MessageBox.Show(this, "En az bir katman ekleyin.", "Döşeme Donatı",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    "\nDöşeme Donatı: {0}, {1} katman. Çizim adımı sonra eklenecek.",
                    DosemeDonatiSettings.Tip == DosemeTipi.Pileli ? "Pileli" : "Pilesiz",
                    DosemeDonatiSettings.GetLayers().Count);
            }
            catch { }
        }

        private static Label MakeLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent
            };
        }

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
