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
    /// Modeless Oto Ölçü ayar formu.
    /// </summary>
    internal sealed class OtoOlcuForm : Form
    {
        private static OtoOlcuForm _instance;

        private readonly ComboBox _cmbPreset;
        private readonly Button _btnPresetAdd;
        private readonly Button _btnPresetRemove;
        private readonly ListBox _lstLayers1;
        private readonly ListBox _lstLayers2;
        private readonly Panel _pnlSecond;
        private readonly CheckBox _chkIkinciToplam;
        private readonly Label _lblStyle;
        private readonly ComboBox _cmbDimStyle;
        private readonly ComboBox _cmbOlcuTipi;
        private readonly Button _btnAdd1;
        private readonly Button _btnDelList1;
        private readonly Button _btnRemove1;
        private readonly Button _btnAdd2;
        private readonly Button _btnDelList2;
        private readonly Button _btnRemove2;
        private readonly Button _btnRun;
        private readonly Button _btnClose;
        private readonly Label _lblLayers1;
        private readonly Label _lblOlcuTipi;
        private readonly Label _lblAksHint;
        private readonly int _yAfterOlcuTipi;
        private readonly int _yAfterPreset;
        private bool _syncing;
        private bool _switchingPreset;

        public static void ShowOrActivate()
        {
            OtoOlcuSettings.EnsureLoaded();
            OtoOlcuSettings.Save();
            try { OtoOlcuDrawingBootstrap.EnsureForActiveDocument(); } catch { }
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new OtoOlcuForm();
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

        private OtoOlcuForm()
        {
            OtoOlcuSettings.EnsureLoaded();

            Text = "Oto Ölçü";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            Size = new Size(340, 620);
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = AcadPaletteTheme.Text;
            Font = new SdFont("Segoe UI", 9f);
            KeyPreview = true;
            KeyDown += OnFormKeyDown;

            int y = 10;
            Controls.Add(MakeLabel("Hazır şablon", 12, y)); y += 20;
            _cmbPreset = MakeCombo(12, y, 300);
            _cmbPreset.SelectedIndexChanged += OnPresetChanged;
            Controls.Add(_cmbPreset); y += 30;
            _btnPresetAdd = MakeButton("Şablon ekle", 12, y, 145);
            _btnPresetRemove = MakeButton("Şablon kaldır", 167, y, 145);
            _btnPresetAdd.Click += OnPresetAdd;
            _btnPresetRemove.Click += OnPresetRemove;
            Controls.Add(_btnPresetAdd);
            Controls.Add(_btnPresetRemove); y += 36;
            _yAfterPreset = y;

            _lblAksHint = new Label
            {
                Text = "AKS OLCU: aks çizgilerini seçin → balon tarafını seçin.\n" +
                       "Toplam ölçü balon kenarına 35 cm, ara ölçüler +20 cm (akslara dik).\n" +
                       "Stil: AKS_OLCU · Katman: AKS OLCU (BEYKENT)",
                Location = new Point(12, y),
                Size = new Size(300, 70),
                ForeColor = Color.FromArgb(180, 220, 255),
                BackColor = Color.Transparent
            };
            Controls.Add(_lblAksHint);

            _lblLayers1 = MakeLabel("1. ölçü katmanları", 12, y);
            Controls.Add(_lblLayers1); y += 20;
            _lstLayers1 = MakeList(12, y, 300, 90);
            Controls.Add(_lstLayers1); y += 96;
            _btnAdd1 = MakeButton("Katman ekle", 12, y, 96);
            _btnDelList1 = MakeButton("Listeden sil", 114, y, 96);
            _btnRemove1 = MakeButton("Katman kaldır", 216, y, 96);
            _btnAdd1.Click += (s, e) => StartLayerPick(OtoOlcuLayerList.Birinci, add: true);
            _btnDelList1.Click += (s, e) => RemoveSelectedFromList(_lstLayers1, OtoOlcuLayerList.Birinci);
            _btnRemove1.Click += (s, e) => StartLayerPick(OtoOlcuLayerList.Birinci, add: false);
            Controls.Add(_btnAdd1);
            Controls.Add(_btnDelList1);
            Controls.Add(_btnRemove1); y += 36;

            _lblOlcuTipi = MakeLabel("Ölçü tipi", 12, y);
            Controls.Add(_lblOlcuTipi); y += 20;
            _cmbOlcuTipi = MakeCombo(12, y, 300);
            _cmbOlcuTipi.Items.Add("Tek ölçü");
            _cmbOlcuTipi.Items.Add("Çift ölçü");
            _cmbOlcuTipi.SelectedIndexChanged += (s, e) =>
            {
                UpdateSecondVisibility();
                PushSettings();
            };
            Controls.Add(_cmbOlcuTipi); y += 34;
            _yAfterOlcuTipi = y;

            _pnlSecond = new Panel
            {
                Location = new Point(0, y),
                Size = new Size(340, 160),
                BackColor = AcadPaletteTheme.Bg
            };
            int sy = 0;
            _pnlSecond.Controls.Add(MakeLabel("2. ölçü katmanları", 12, sy)); sy += 20;
            _lstLayers2 = MakeList(12, sy, 300, 70);
            _pnlSecond.Controls.Add(_lstLayers2); sy += 76;
            _btnAdd2 = MakeButton("Katman ekle", 12, sy, 96);
            _btnDelList2 = MakeButton("Listeden sil", 114, sy, 96);
            _btnRemove2 = MakeButton("Katman kaldır", 216, sy, 96);
            _btnAdd2.Click += (s, e) => StartLayerPick(OtoOlcuLayerList.Ikinci, add: true);
            _btnDelList2.Click += (s, e) => RemoveSelectedFromList(_lstLayers2, OtoOlcuLayerList.Ikinci);
            _btnRemove2.Click += (s, e) => StartLayerPick(OtoOlcuLayerList.Ikinci, add: false);
            _pnlSecond.Controls.Add(_btnAdd2);
            _pnlSecond.Controls.Add(_btnDelList2);
            _pnlSecond.Controls.Add(_btnRemove2); sy += 32;
            _chkIkinciToplam = new CheckBox
            {
                Text = "İkinci ölçü toplam (1. ölçünün toplamı)",
                Location = new Point(12, sy),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Checked = OtoOlcuSettings.IkinciOlcuToplam
            };
            _chkIkinciToplam.CheckedChanged += (s, e) =>
            {
                PushSettings();
                UpdateSecondVisibility();
            };
            _pnlSecond.Controls.Add(_chkIkinciToplam);
            Controls.Add(_pnlSecond);

            // Stil + düğmeler: konum UpdateSecondVisibility ile ayarlanır
            _lblStyle = MakeLabel("Ölçü stili", 12, y);
            Controls.Add(_lblStyle);
            _cmbDimStyle = MakeCombo(12, y, 300);
            _cmbDimStyle.SelectedIndexChanged += (s, e) => PushSettings();
            Controls.Add(_cmbDimStyle);

            _btnRun = MakeButton("Ölçü al", 12, y, 200);
            _btnRun.Font = new SdFont("Segoe UI", 9f, FontStyle.Bold);
            _btnRun.ForeColor = AcadPaletteTheme.Accent;
            _btnRun.Click += OnRun;
            _btnClose = MakeButton("Kapat", 224, y, 88);
            _btnClose.Click += (s, e) => { PushSettings(); Close(); };
            Controls.Add(_btnRun);
            Controls.Add(_btnClose);

            FormClosed += (s, e) =>
            {
                try { PushSettings(); } catch { }
                if (ReferenceEquals(_instance, this))
                    _instance = null;
            };

            try
            {
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 80,
                    Screen.PrimaryScreen.WorkingArea.Top + 80);
            }
            catch { StartPosition = FormStartPosition.CenterScreen; }

            RefreshFromDrawing();
            UpdateSecondVisibility();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { PushSettings(); Close(); return true; }
            if (keyData == Keys.Enter || keyData == Keys.Return || keyData == Keys.Space)
            {
                StartMeasure();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void UpdateSecondVisibility()
        {
            bool aks = OtoOlcuSettings.IsAksOlcuPreset;
            _lblAksHint.Visible = aks;
            _lblAksHint.Top = _yAfterPreset;

            _lblLayers1.Visible = !aks;
            _lstLayers1.Visible = !aks;
            _btnAdd1.Visible = !aks;
            _btnDelList1.Visible = !aks;
            _btnRemove1.Visible = !aks;
            _lblOlcuTipi.Visible = !aks;
            _cmbOlcuTipi.Visible = !aks;

            bool cift = !aks && _cmbOlcuTipi.SelectedIndex == 1;
            _pnlSecond.Visible = cift;

            _btnPresetRemove.Enabled = !aks;
            _cmbDimStyle.Enabled = !aks;

            int y;
            if (aks)
            {
                y = _yAfterPreset + _lblAksHint.Height + 8;
            }
            else
            {
                y = _yAfterOlcuTipi;
                if (cift)
                {
                    _pnlSecond.Top = y;
                    y += _pnlSecond.Height + 6;
                }
            }

            _lblStyle.Top = y;
            y += 20;
            _cmbDimStyle.Top = y;
            y += 36;
            _btnRun.Top = y;
            _btnClose.Top = y;
            y += 40;

            ClientSize = new Size(ClientSize.Width, Math.Max(y + 8, 120));
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
            OtoOlcuSettings.EnsureLoaded();
            _syncing = true;
            try
            {
                FillPresetCombo();

                _cmbDimStyle.Items.Clear();
                foreach (string name in ReadDimStyleNames())
                    _cmbDimStyle.Items.Add(name);

                string want = OtoOlcuSettings.DimStyleName;
                int idx = IndexOfIgnoreCase(_cmbDimStyle, want);
                if (idx < 0 && _cmbDimStyle.Items.Count > 0)
                {
                    idx = IndexOfIgnoreCase(_cmbDimStyle, "OLCU (BEYKENT)");
                    if (idx < 0) idx = IndexOfIgnoreCase(_cmbDimStyle, "PLAN_OLCU");
                    if (idx < 0) idx = 0;
                }
                if (idx >= 0) _cmbDimStyle.SelectedIndex = idx;

                _cmbOlcuTipi.SelectedIndex = OtoOlcuSettings.Sablon == OtoOlcuSablon.CiftOlcu ? 1 : 0;
                _chkIkinciToplam.Checked = OtoOlcuSettings.IkinciOlcuToplam;

                FillList(_lstLayers1, OtoOlcuSettings.GetLayers(OtoOlcuLayerList.Birinci));
                FillList(_lstLayers2, OtoOlcuSettings.GetLayers(OtoOlcuLayerList.Ikinci));
            }
            finally
            {
                _syncing = false;
                UpdateSecondVisibility();
            }
        }

        private void FillPresetCombo()
        {
            string active = OtoOlcuSettings.ActivePresetName;
            _cmbPreset.Items.Clear();
            foreach (string n in OtoOlcuSettings.GetPresetNames())
                _cmbPreset.Items.Add(n);
            int i = IndexOfIgnoreCase(_cmbPreset, active);
            if (i < 0 && _cmbPreset.Items.Count > 0) i = 0;
            if (i >= 0) _cmbPreset.SelectedIndex = i;
        }

        private void OnPresetChanged(object sender, EventArgs e)
        {
            if (_syncing || _switchingPreset) return;
            if (!(_cmbPreset.SelectedItem is string name)) return;
            if (string.Equals(name, OtoOlcuSettings.ActivePresetName, StringComparison.OrdinalIgnoreCase))
                return;

            _switchingPreset = true;
            try
            {
                // Mevcut şablonun ayarlarını kaydet, sonra yeniyi yükle
                PushSettings();
                if (OtoOlcuSettings.SelectPreset(name))
                    RefreshFromDrawing();
            }
            finally
            {
                _switchingPreset = false;
            }
        }

        private void OnPresetAdd(object sender, EventArgs e)
        {
            string name = PromptText("Yeni hazır şablon adı:", "Şablon ekle");
            if (string.IsNullOrWhiteSpace(name)) return;
            PushSettings();
            if (OtoOlcuSettings.IsLockedPreset(name.Trim()))
            {
                MessageBox.Show(this, "AKS OLCU adı rezerve; silinemez şablon zaten mevcut.", "Oto Ölçü",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!OtoOlcuSettings.AddPreset(name.Trim()))
            {
                MessageBox.Show(this, "Bu isimde şablon zaten var.", "Oto Ölçü",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            RefreshFromDrawing();
        }

        private void OnPresetRemove(object sender, EventArgs e)
        {
            if (!(_cmbPreset.SelectedItem is string name)) return;
            if (OtoOlcuSettings.IsLockedPreset(name))
            {
                MessageBox.Show(this, "AKS OLCU şablonu silinemez.", "Oto Ölçü",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (OtoOlcuSettings.GetPresetNames().Count <= 1)
            {
                MessageBox.Show(this, "En az bir hazır şablon kalmalı.", "Oto Ölçü",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this,
                    "'" + name + "' şablonu silinsin mi?",
                    "Şablon kaldır",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            PushSettings();
            if (OtoOlcuSettings.RemovePreset(name))
                RefreshFromDrawing();
        }

        private static string PromptText(string prompt, string title)
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.ClientSize = new Size(320, 110);
                f.BackColor = AcadPaletteTheme.Bg;
                f.ForeColor = Color.White;

                var lbl = new Label
                {
                    Text = prompt,
                    Location = new Point(12, 12),
                    AutoSize = true,
                    ForeColor = Color.White
                };
                var tb = new TextBox
                {
                    Location = new Point(12, 36),
                    Size = new Size(296, 24),
                    BackColor = AcadPaletteTheme.InputBg,
                    ForeColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle
                };
                var ok = new Button
                {
                    Text = "Tamam",
                    DialogResult = DialogResult.OK,
                    Location = new Point(148, 72),
                    Size = new Size(80, 28),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = AcadPaletteTheme.BgAlt,
                    ForeColor = Color.White
                };
                var cancel = new Button
                {
                    Text = "İptal",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(236, 72),
                    Size = new Size(72, 28),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = AcadPaletteTheme.BgAlt,
                    ForeColor = Color.White
                };
                f.Controls.Add(lbl);
                f.Controls.Add(tb);
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? tb.Text : null;
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
            if (OtoOlcuSettings.IsAksOlcuPreset)
            {
                OtoOlcuSettings.Save();
                return;
            }
            if (_cmbDimStyle.SelectedItem is string st)
                OtoOlcuSettings.DimStyleName = st;
            OtoOlcuSettings.Sablon = _cmbOlcuTipi.SelectedIndex == 1
                ? OtoOlcuSablon.CiftOlcu
                : OtoOlcuSablon.TekOlcu;
            OtoOlcuSettings.IkinciOlcuToplam = _chkIkinciToplam.Checked;

            OtoOlcuSettings.SetLayers(OtoOlcuLayerList.Birinci, CollectList(_lstLayers1));
            OtoOlcuSettings.SetLayers(OtoOlcuLayerList.Ikinci, CollectList(_lstLayers2));
            OtoOlcuSettings.Save();
        }

        private static List<string> CollectList(ListBox lst)
        {
            var layers = new List<string>();
            foreach (object o in lst.Items)
                if (o is string s) layers.Add(s);
            return layers;
        }

        private void RemoveSelectedFromList(ListBox lst, OtoOlcuLayerList which)
        {
            if (lst.SelectedItems.Count == 0) return;
            var toRemove = new List<string>();
            foreach (object o in lst.SelectedItems)
            {
                if (o is string s && !string.IsNullOrWhiteSpace(s))
                    toRemove.Add(s);
            }
            foreach (string name in toRemove)
            {
                OtoOlcuSettings.RemoveLayer(which, name);
                lst.Items.Remove(name);
            }
            PushSettings();
        }

        private void StartLayerPick(OtoOlcuLayerList target, bool add)
        {
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                PushSettings();
                OtoOlcuSettings.LayerPickTarget = target;
                HideTemporarily();
                doc.SendStringToExecute(add ? "OTOOLCUKATMANEKLE " : "OTOOLCUKATMANKALDIR ", true, false, false);
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
            if (_instance == null || _instance.IsDisposed || string.IsNullOrWhiteSpace(layerName))
                return;
            try
            {
                if (_instance.InvokeRequired)
                {
                    _instance.BeginInvoke(new Action(() => NotifyLayerAdded(layerName)));
                    return;
                }
                ListBox lst = OtoOlcuSettings.LayerPickTarget == OtoOlcuLayerList.Ikinci
                    ? _instance._lstLayers2
                    : _instance._lstLayers1;
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
            if (_instance == null || _instance.IsDisposed) return;
            try
            {
                if (_instance.InvokeRequired)
                {
                    _instance.BeginInvoke(new Action(NotifyLayersChanged));
                    return;
                }
                FillList(_instance._lstLayers1, OtoOlcuSettings.GetLayers(OtoOlcuLayerList.Birinci));
                FillList(_instance._lstLayers2, OtoOlcuSettings.GetLayers(OtoOlcuLayerList.Ikinci));
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

        private void OnRun(object sender, EventArgs e) => StartMeasure();

        private void StartMeasure()
        {
            PushSettings();
            if (!OtoOlcuSettings.IsAksOlcuPreset)
            {
                if (OtoOlcuSettings.GetLayers(OtoOlcuLayerList.Birinci).Count == 0)
                {
                    MessageBox.Show(this, "1. ölçü için en az bir katman ekleyin.", "Oto Ölçü",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (OtoOlcuSettings.Sablon == OtoOlcuSablon.CiftOlcu &&
                    !OtoOlcuSettings.IkinciOlcuToplam &&
                    OtoOlcuSettings.GetLayers(OtoOlcuLayerList.Ikinci).Count == 0)
                {
                    MessageBox.Show(this,
                        "Çift ölçüde 2. ölçü katmanı ekleyin veya «İkinci ölçü toplam» seçin.",
                        "Oto Ölçü", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                HideTemporarily();
                doc.SendStringToExecute("OTOOLCUCIZ ", true, false, false);
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
