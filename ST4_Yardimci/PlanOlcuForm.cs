using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    /// <summary>
    /// Modeless Plan Ölçü ayar formu. Ölçü al → kat/temel sınırı poligonu.
    /// </summary>
    internal sealed class PlanOlcuForm : Form
    {
        private static PlanOlcuForm _instance;

        private readonly ComboBox _cmbPreset;
        private readonly ComboBox _cmbDimStyle;
        private readonly TextBox _txtBandWidth;
        private readonly TextBox _txtTekCiftSinir;
        private readonly CheckBox _chkToplamOlcu;
        private readonly CheckBox _chkCiftOlcu;
        private readonly ListBox _lstLayers;
        private readonly ListBox _lstCiftLayers;
        private readonly Button _btnAdd;
        private readonly Button _btnDelList;
        private readonly Button _btnRemove;
        private readonly Button _btnCiftAdd;
        private readonly Button _btnCiftDelList;
        private readonly Button _btnCiftRemove;
        private readonly Button _btnRun;
        private readonly Button _btnClose;
        private bool _syncing;
        private bool _switchingPreset;

        public static void ShowOrActivate()
        {
            PlanOlcuSettings.EnsureLoaded();
            PlanOlcuSettings.Save();
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new PlanOlcuForm();
                try { AcApp.ShowModelessDialog(_instance); }
                catch { _instance.Show(); }
            }
            else
            {
                _instance.RefreshFromDrawing();
                _instance.Visible = true;
                _instance.BringToFront();
            }
        }

        public static void Shutdown()
        {
            if (_instance == null) return;
            try
            {
                _instance.PushSettings();
                _instance.Close();
            }
            catch { }
            _instance = null;
        }

        private PlanOlcuForm()
        {
            PlanOlcuSettings.EnsureLoaded();

            Text = "Plan Ölçü";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            Size = new Size(340, 720);
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = AcadPaletteTheme.Text;
            Font = new SdFont("Segoe UI", 9f);
            KeyPreview = true;
            KeyDown += OnFormKeyDown;

            int y = 10;
            Controls.Add(MakeLabel("Hazır şablon", 12, y)); y += 20;
            _cmbPreset = MakeCombo(12, y, 300);
            _cmbPreset.SelectedIndexChanged += OnPresetChanged;
            Controls.Add(_cmbPreset); y += 34;

            Controls.Add(MakeLabel("Ölçü stili", 12, y)); y += 20;
            _cmbDimStyle = MakeCombo(12, y, 300);
            _cmbDimStyle.SelectedIndexChanged += (s, e) => PushSettings();
            Controls.Add(_cmbDimStyle); y += 36;

            Controls.Add(MakeLabel("Bant genişliği (cm)", 12, y)); y += 20;
            _txtBandWidth = new TextBox
            {
                Location = new Point(12, y),
                Size = new Size(120, 24),
                BackColor = AcadPaletteTheme.InputBg,
                ForeColor = AcadPaletteTheme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            _txtBandWidth.Leave += (s, e) => PushSettings();
            Controls.Add(_txtBandWidth); y += 32;

            Controls.Add(MakeLabel("Tek/çift ölçü sınırı (cm)", 12, y)); y += 20;
            _txtTekCiftSinir = new TextBox
            {
                Location = new Point(12, y),
                Size = new Size(120, 24),
                BackColor = AcadPaletteTheme.InputBg,
                ForeColor = AcadPaletteTheme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            _txtTekCiftSinir.Leave += (s, e) => PushSettings();
            Controls.Add(_txtTekCiftSinir); y += 32;

            _chkToplamOlcu = new CheckBox
            {
                Text = "Toplam ölçü",
                Location = new Point(12, y),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent
            };
            _chkToplamOlcu.CheckedChanged += (s, e) => PushSettings();
            Controls.Add(_chkToplamOlcu);

            _chkCiftOlcu = new CheckBox
            {
                Text = "Çift ölçü",
                Location = new Point(160, y),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent
            };
            _chkCiftOlcu.CheckedChanged += (s, e) => PushSettings();
            Controls.Add(_chkCiftOlcu); y += 30;

            Controls.Add(MakeLabel("Ölçülendirilecek katmanlar", 12, y)); y += 20;
            _lstLayers = MakeList(12, y, 300, 90);
            Controls.Add(_lstLayers); y += 96;
            _btnAdd = MakeButton("Katman ekle", 12, y, 96);
            _btnDelList = MakeButton("Listeden sil", 114, y, 96);
            _btnRemove = MakeButton("Katman kaldır", 216, y, 96);
            _btnAdd.Click += (s, e) => StartLayerPick(add: true, cift: false);
            _btnDelList.Click += (s, e) => RemoveSelectedFromList(cift: false);
            _btnRemove.Click += (s, e) => StartLayerPick(add: false, cift: false);
            Controls.Add(_btnAdd);
            Controls.Add(_btnDelList);
            Controls.Add(_btnRemove); y += 36;

            Controls.Add(MakeLabel("Çift ölçü katmanları", 12, y)); y += 20;
            _lstCiftLayers = MakeList(12, y, 300, 90);
            Controls.Add(_lstCiftLayers); y += 96;
            _btnCiftAdd = MakeButton("Katman ekle", 12, y, 96);
            _btnCiftDelList = MakeButton("Listeden sil", 114, y, 96);
            _btnCiftRemove = MakeButton("Katman kaldır", 216, y, 96);
            _btnCiftAdd.Click += (s, e) => StartLayerPick(add: true, cift: true);
            _btnCiftDelList.Click += (s, e) => RemoveSelectedFromList(cift: true);
            _btnCiftRemove.Click += (s, e) => StartLayerPick(add: false, cift: true);
            Controls.Add(_btnCiftAdd);
            Controls.Add(_btnCiftDelList);
            Controls.Add(_btnCiftRemove); y += 40;

            _btnRun = MakeButton("Ölçü al", 12, y, 200);
            _btnRun.Font = new SdFont("Segoe UI", 9f, FontStyle.Bold);
            _btnRun.ForeColor = AcadPaletteTheme.Accent;
            _btnRun.Click += (s, e) => StartMeasure();
            _btnClose = MakeButton("Kapat", 224, y, 88);
            _btnClose.Click += (s, e) => { PushSettings(); Close(); };
            Controls.Add(_btnRun);
            Controls.Add(_btnClose);
            y += 40;

            ClientSize = new Size(324, y + 8);

            FormClosed += (s, e) =>
            {
                try { PushSettings(); } catch { }
                if (ReferenceEquals(_instance, this))
                    _instance = null;
            };

            try
            {
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 120,
                    Screen.PrimaryScreen.WorkingArea.Top + 80);
            }
            catch { StartPosition = FormStartPosition.CenterScreen; }

            RefreshFromDrawing();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                PushSettings();
                Close();
                return true;
            }
            if (keyData == Keys.Enter || keyData == Keys.Return || keyData == Keys.Space)
            {
                StartMeasure();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private ListBox MakeList(int x, int y, int w, int h) => new ListBox
        {
            Location = new Point(x, y),
            Size = new Size(w, h),
            BackColor = AcadPaletteTheme.InputBg,
            ForeColor = AcadPaletteTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false,
            SelectionMode = SelectionMode.MultiExtended
        };

        private Label MakeLabel(string text, int x, int y) => new Label
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
                ForeColor = AcadPaletteTheme.Text,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = AcadPaletteTheme.InputBorder;
            b.FlatAppearance.MouseOverBackColor = AcadPaletteTheme.BgHover;
            b.FlatAppearance.MouseDownBackColor = AcadPaletteTheme.BgPressed;
            return b;
        }

        private ComboBox MakeCombo(int x, int y, int w) => new ComboBox
        {
            Location = new Point(x, y),
            Size = new Size(w, 26),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = AcadPaletteTheme.InputBg,
            ForeColor = AcadPaletteTheme.Text,
            FlatStyle = FlatStyle.Flat
        };

        public void RefreshFromDrawing()
        {
            PlanOlcuSettings.EnsureLoaded();
            _syncing = true;
            try
            {
                FillPresetCombo();

                _cmbDimStyle.Items.Clear();
                foreach (string name in ReadDimStyleNames())
                    _cmbDimStyle.Items.Add(name);

                string want = PlanOlcuSettings.DimStyleName;
                int idx = IndexOfIgnoreCase(_cmbDimStyle, want);
                if (idx < 0 && _cmbDimStyle.Items.Count > 0)
                {
                    idx = IndexOfIgnoreCase(_cmbDimStyle, OtoOlcuSettings.DefaultDimStyleName);
                    if (idx < 0) idx = 0;
                }
                if (idx >= 0) _cmbDimStyle.SelectedIndex = idx;

                _txtBandWidth.Text = PlanOlcuSettings.BandWidthCm.ToString(
                    "0.##", System.Globalization.CultureInfo.InvariantCulture);
                _txtTekCiftSinir.Text = PlanOlcuSettings.TekCiftSinirCm.ToString(
                    "0.##", System.Globalization.CultureInfo.InvariantCulture);
                _chkToplamOlcu.Checked = PlanOlcuSettings.PlaceTotalDim;
                _chkCiftOlcu.Checked = PlanOlcuSettings.PlaceCiftOlcu;

                FillList(_lstLayers, PlanOlcuSettings.GetLayers());
                FillList(_lstCiftLayers, PlanOlcuSettings.GetCiftLayers());
            }
            finally
            {
                _syncing = false;
            }
        }

        private void FillPresetCombo()
        {
            string active = PlanOlcuSettings.ActivePresetName;
            _cmbPreset.Items.Clear();
            foreach (string n in PlanOlcuSettings.GetPresetNames())
                _cmbPreset.Items.Add(n);
            int i = IndexOfIgnoreCase(_cmbPreset, active);
            if (i < 0 && _cmbPreset.Items.Count > 0) i = 0;
            if (i >= 0) _cmbPreset.SelectedIndex = i;
        }

        private void OnPresetChanged(object sender, EventArgs e)
        {
            if (_syncing || _switchingPreset) return;
            if (!(_cmbPreset.SelectedItem is string name)) return;
            if (string.Equals(name, PlanOlcuSettings.ActivePresetName, StringComparison.OrdinalIgnoreCase))
                return;

            _switchingPreset = true;
            try
            {
                PushSettings();
                if (PlanOlcuSettings.SelectPreset(name))
                    RefreshFromDrawing();
            }
            finally
            {
                _switchingPreset = false;
            }
        }

        private static void FillList(ListBox lst, IReadOnlyList<string> layers)
        {
            lst.Items.Clear();
            foreach (string lyr in layers)
                lst.Items.Add(lyr);
        }

        private void PushSettings()
        {
            if (_syncing) return;
            if (_cmbDimStyle.SelectedItem is string st)
                PlanOlcuSettings.DimStyleName = st;
            if (double.TryParse((_txtBandWidth.Text ?? "").Trim().Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double bw) && bw >= 1.0)
                PlanOlcuSettings.BandWidthCm = bw;
            if (double.TryParse((_txtTekCiftSinir.Text ?? "").Trim().Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double lim) && lim >= 1.0)
                PlanOlcuSettings.TekCiftSinirCm = lim;
            PlanOlcuSettings.PlaceTotalDim = _chkToplamOlcu.Checked;
            PlanOlcuSettings.PlaceCiftOlcu = _chkCiftOlcu.Checked;
            PlanOlcuSettings.SetLayers(CollectList(_lstLayers));
            PlanOlcuSettings.SetCiftLayers(CollectList(_lstCiftLayers));
            PlanOlcuSettings.Save();
        }

        private static List<string> CollectList(ListBox lst)
        {
            var layers = new List<string>();
            foreach (object o in lst.Items)
                if (o is string s) layers.Add(s);
            return layers;
        }

        private void RemoveSelectedFromList(bool cift)
        {
            ListBox lst = cift ? _lstCiftLayers : _lstLayers;
            if (lst.SelectedItems.Count == 0) return;
            var toRemove = new List<string>();
            foreach (object o in lst.SelectedItems)
            {
                if (o is string s && !string.IsNullOrWhiteSpace(s))
                    toRemove.Add(s);
            }
            foreach (string name in toRemove)
            {
                if (cift) PlanOlcuSettings.RemoveCiftLayer(name);
                else PlanOlcuSettings.RemoveLayer(name);
                lst.Items.Remove(name);
            }
            PushSettings();
        }

        private void StartLayerPick(bool add, bool cift)
        {
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                PushSettings();
                HideTemporarily();
                string cmd = cift
                    ? (add ? "PLANOLCUCIFTKATMANEKLE " : "PLANOLCUCIFTKATMANKALDIR ")
                    : (add ? "PLANOLCUKATMANEKLE " : "PLANOLCUKATMANKALDIR ");
                doc.SendStringToExecute(cmd, true, false, false);
            }
            catch { }
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

        public static void NotifyLayerAdded(string layerName)
        {
            NotifyLayerAdded(layerName, cift: false);
        }

        public static void NotifyCiftLayerAdded(string layerName)
        {
            NotifyLayerAdded(layerName, cift: true);
        }

        private static void NotifyLayerAdded(string layerName, bool cift)
        {
            if (_instance == null || _instance.IsDisposed || string.IsNullOrWhiteSpace(layerName))
                return;
            try
            {
                if (_instance.InvokeRequired)
                {
                    _instance.BeginInvoke(new Action(() => NotifyLayerAdded(layerName, cift)));
                    return;
                }
                ListBox lst = cift ? _instance._lstCiftLayers : _instance._lstLayers;
                foreach (object o in lst.Items)
                    if (o is string s && string.Equals(s, layerName, StringComparison.OrdinalIgnoreCase))
                        return;
                lst.Items.Add(layerName);
                _instance.PushSettings();
            }
            catch { }
        }

        public static void NotifyLayersChanged()
        {
            NotifyLayersChanged(cift: false);
        }

        public static void NotifyCiftLayersChanged()
        {
            NotifyLayersChanged(cift: true);
        }

        private static void NotifyLayersChanged(bool cift)
        {
            if (_instance == null || _instance.IsDisposed) return;
            try
            {
                if (_instance.InvokeRequired)
                {
                    _instance.BeginInvoke(new Action(() => NotifyLayersChanged(cift)));
                    return;
                }
                if (cift)
                    FillList(_instance._lstCiftLayers, PlanOlcuSettings.GetCiftLayers());
                else
                    FillList(_instance._lstLayers, PlanOlcuSettings.GetLayers());
            }
            catch { }
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                PushSettings();
                Close();
                return;
            }
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return || e.KeyCode == Keys.Space)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                StartMeasure();
            }
        }

        private void StartMeasure()
        {
            PushSettings();
            string preset = PlanOlcuSettings.ActivePresetName ?? "";
            string ascii = preset
                .Replace('İ', 'I').Replace('ı', 'i')
                .Replace('Ü', 'U').Replace('ü', 'u');
            bool needsLayers =
                preset.IndexOf("RADYE", StringComparison.OrdinalIgnoreCase) >= 0
                || preset.IndexOf("MANTAR", StringComparison.OrdinalIgnoreCase) >= 0
                || ascii.IndexOf("SUREKLI", StringComparison.OrdinalIgnoreCase) >= 0;
            if (needsLayers && PlanOlcuSettings.GetLayers().Count == 0)
            {
                MessageBox.Show(this, "Ölçülendirilecek en az bir katman ekleyin.", "Plan Ölçü",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                HideTemporarily();
                doc.SendStringToExecute("PLANOLCUCIZ ", true, false, false);
            }
            catch { }
        }

        private static int IndexOfIgnoreCase(ComboBox cmb, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return -1;
            for (int i = 0; i < cmb.Items.Count; i++)
            {
                if (cmb.Items[i] is string s &&
                    string.Equals(s, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static List<string> ReadDimStyleNames()
        {
            var list = new List<string>();
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return list;
                Database db = doc.Database;
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                    foreach (ObjectId id in dst)
                    {
                        var rec = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        if (rec != null && !string.IsNullOrWhiteSpace(rec.Name))
                            list.Add(rec.Name);
                    }
                    tr.Commit();
                }
            }
            catch { }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }
    }
}
