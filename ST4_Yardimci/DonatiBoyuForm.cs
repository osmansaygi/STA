using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    internal enum DonatiBoyuYuvarlama
    {
        Yok = 0,
        BirCm = 1,
        BesCm = 5,
        OnCm = 10
    }

    /// <summary>Donatı boyu (L=) güncelleme ayarları.</summary>
    internal static class DonatiBoyuSettings
    {
        /// <summary>Çizim birimi → cm. 1:50→1, 1:25 (×2)→0.5, 1:25 (×4)→0.25.</summary>
        public static double OlcekCarpani { get; set; } = 1.0;
        public static DonatiBoyuYuvarlama Yuvarlama { get; set; } = DonatiBoyuYuvarlama.Yok;
        /// <summary>Etikette aranan çap işareti. Varsayılan ø; %%C / Ø ile de eşleşir.</summary>
        public static string FiIsareti { get; set; } = "ø";
        public static string LEtiketFormati { get; set; } = "L=";
        /// <summary>Katman listesi boşken DONATI/ETRIYE/HASIR/CIROZ süzgeci.</summary>
        public static bool SadeceDonatiKatmani { get; set; } = true;
        public static double MinFarkCm { get; set; } = 0.5;
        public static bool OnizlemeMesaji { get; set; } = true;
        public static List<string> Katmanlar { get; } = new List<string>();

        static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var m = BaIni.Read("donatiboyu.ini");
            OlcekCarpani = BaIni.GetD(m, "OlcekCarpani", OlcekCarpani);
            Yuvarlama = (DonatiBoyuYuvarlama)BaIni.GetI(m, "Yuvarlama", (int)Yuvarlama);
            FiIsareti = BaIni.Get(m, "FiIsareti", FiIsareti);
            if (string.IsNullOrWhiteSpace(FiIsareti)) FiIsareti = "ø";
            LEtiketFormati = BaIni.Get(m, "LEtiketFormati", LEtiketFormati);
            SadeceDonatiKatmani = BaIni.GetB(m, "SadeceDonatiKatmani", SadeceDonatiKatmani);
            MinFarkCm = BaIni.GetD(m, "MinFarkCm", MinFarkCm);
            OnizlemeMesaji = BaIni.GetB(m, "OnizlemeMesaji", OnizlemeMesaji);
            ReplaceKatmanlar(SplitKatmanlar(BaIni.Get(m, "Katmanlar", "")));
        }

        public static void Save()
        {
            EnsureLoaded();
            BaIni.Write("donatiboyu.ini",
                "OlcekCarpani=" + BaIni.N(OlcekCarpani),
                "Yuvarlama=" + ((int)Yuvarlama).ToString(CultureInfo.InvariantCulture),
                "FiIsareti=" + (string.IsNullOrWhiteSpace(FiIsareti) ? "ø" : FiIsareti.Trim()),
                "LEtiketFormati=" + (LEtiketFormati ?? "L="),
                "SadeceDonatiKatmani=" + BaIni.B(SadeceDonatiKatmani),
                "MinFarkCm=" + BaIni.N(MinFarkCm),
                "OnizlemeMesaji=" + BaIni.B(OnizlemeMesaji),
                "Katmanlar=" + JoinKatmanlar());
        }

        public static void SetKatmanlar(IEnumerable<string> names)
        {
            EnsureLoaded();
            ReplaceKatmanlar(names);
            Save();
        }

        public static bool AddKatman(string name)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(name)) return false;
            string t = name.Trim();
            foreach (string e in Katmanlar)
                if (string.Equals(e, t, StringComparison.OrdinalIgnoreCase))
                    return false;
            Katmanlar.Add(t);
            Save();
            return true;
        }

        public static bool RemoveKatman(string name)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(name)) return false;
            bool removed = false;
            for (int i = Katmanlar.Count - 1; i >= 0; i--)
            {
                if (string.Equals(Katmanlar[i], name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    Katmanlar.RemoveAt(i);
                    removed = true;
                }
            }
            if (removed) Save();
            return removed;
        }

        static void ReplaceKatmanlar(IEnumerable<string> names)
        {
            Katmanlar.Clear();
            if (names == null) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in names)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                string t = n.Trim();
                if (seen.Add(t)) Katmanlar.Add(t);
            }
        }

        static IEnumerable<string> SplitKatmanlar(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) yield break;
            foreach (string p in raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = p.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        static string JoinKatmanlar()
        {
            var sb = new StringBuilder();
            foreach (string n in Katmanlar)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                if (sb.Length > 0) sb.Append('|');
                sb.Append(n.Trim());
            }
            return sb.ToString();
        }
    }

    /// <summary>Modeless form — toplu L= güncelleme.</summary>
    internal sealed class DonatiBoyuForm : Form
    {
        private static DonatiBoyuForm _instance;

        private readonly ComboBox _cmbOlcek;
        private readonly CheckBox _chkYuvarla;
        private readonly TextBox _txtFi;
        private readonly ComboBox _cmbLFormat;
        private readonly ListBox _lstLayers;
        private readonly CheckBox _chkOnizleme;

        static readonly OlcekItem[] Olcekler =
        {
            new OlcekItem(1.0, "1:50 - 1:1"),
            new OlcekItem(0.5, "1:25 - 1:2"),
            new OlcekItem(0.25, "1:25 - 1:4")
        };

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("DONATIBOYU")) return;
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new DonatiBoyuForm();
                try { AcApp.ShowModelessDialog(_instance); }
                catch { _instance.Show(); }
            }
            else
            {
                _instance.LoadFromSettings();
                _instance.Visible = true;
                _instance.BringToFront();
            }
            BaFormSession.Attach("DONATIBOYU", _instance);
            BaFormSession.EndPick("DONATIBOYU");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("DONATIBOYU");
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
                
                BaFormSession.BeginPick("DONATIBOYU");
                _instance.Visible = false;
            }
            catch { }
        }

        public static void ShowAfterPick()
        {
            BaFormSession.EndPick("DONATIBOYU");
            if (_instance == null || _instance.IsDisposed) return;
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
                foreach (object o in _instance._lstLayers.Items)
                    if (o is string s && string.Equals(s, layerName, StringComparison.OrdinalIgnoreCase))
                        return;
                _instance._lstLayers.Items.Add(layerName);
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
                FillList(_instance._lstLayers, DonatiBoyuSettings.Katmanlar);
            }
            catch { }
        }

        private DonatiBoyuForm()
        {
            DonatiBoyuSettings.EnsureLoaded();
            Text = "Donatı Boyu";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = AcadPaletteTheme.Text;
            Font = new SdFont("Segoe UI", 8.5f);
            KeyPreview = true;

            int y = 8;
            Controls.Add(MkLabel("Donatı ölçeği", 10, y + 3, 100));
            _cmbOlcek = MkCombo(114, y, 196);
            foreach (OlcekItem oi in Olcekler)
                _cmbOlcek.Items.Add(oi);
            SelectOlcek(_cmbOlcek, DonatiBoyuSettings.OlcekCarpani);
            Controls.Add(_cmbOlcek);
            y += 28;

            _chkYuvarla = new CheckBox
            {
                Text = "5cm katlarına yuvarla",
                Location = new Point(10, y + 1),
                AutoSize = true,
                ForeColor = AcadPaletteTheme.Text,
                BackColor = Color.Transparent,
                Checked = DonatiBoyuSettings.Yuvarlama == DonatiBoyuYuvarlama.BesCm
            };
            Controls.Add(_chkYuvarla);
            y += 26;

            Controls.Add(MkLabel("Fi işareti", 10, y + 3, 100));
            _txtFi = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(DonatiBoyuSettings.FiIsareti) ? "ø" : DonatiBoyuSettings.FiIsareti,
                Location = new Point(114, y),
                Size = new Size(48, 22),
                MaxLength = 6,
                TextAlign = HorizontalAlignment.Center,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = AcadPaletteTheme.InputBg,
                ForeColor = AcadPaletteTheme.Text
            };
            Controls.Add(_txtFi);
            y += 26;

            _cmbLFormat = AddComboRow("L formatı", ref y, 90);
            _cmbLFormat.Items.Add("L=");
            _cmbLFormat.Items.Add("l=");
            _cmbLFormat.Items.Add("L =");
            _cmbLFormat.Items.Add("l =");
            SelectString(_cmbLFormat, DonatiBoyuSettings.LEtiketFormati);

            Controls.Add(MkLabel("Donatı etiketi katmanı", 10, y + 2, 280));
            y += 18;
            _lstLayers = new ListBox
            {
                Location = new Point(10, y),
                Size = new Size(300, 78),
                BackColor = AcadPaletteTheme.InputBg,
                ForeColor = AcadPaletteTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
                SelectionMode = SelectionMode.MultiExtended
            };
            Controls.Add(_lstLayers);
            y += 82;
            FillList(_lstLayers, DonatiBoyuSettings.Katmanlar);

            var btnAdd = MkButton("Katman ekle", 10, y, 96, false);
            var btnDel = MkButton("Listeden kaldır", 110, y, 100, false);
            var btnRem = MkButton("Katman kaldır", 214, y, 96, false);
            btnAdd.Click += (s, e) => StartLayerPick(add: true);
            btnDel.Click += (s, e) => RemoveSelectedFromList();
            btnRem.Click += (s, e) => StartLayerPick(add: false);
            Controls.Add(btnAdd);
            Controls.Add(btnDel);
            Controls.Add(btnRem);
            y += 30;

            _chkOnizleme = new CheckBox
            {
                Text = "Komut satırında özet yaz",
                Location = new Point(10, y),
                AutoSize = true,
                ForeColor = AcadPaletteTheme.Text,
                BackColor = Color.Transparent,
                Checked = DonatiBoyuSettings.OnizlemeMesaji
            };
            Controls.Add(_chkOnizleme);
            y += 26;

            var btnRun = MkButton("Seç ve güncelle", 10, y, 168, true);
            btnRun.Click += OnRun;
            var btnClose = MkButton("Kapat", 186, y, 70, false);
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnRun);
            Controls.Add(btnClose);
            AcceptButton = btnRun;
            CancelButton = btnClose;
            ClientSize = new Size(320, y + 36);

            FormClosing += (s, e) => { try { PushSettings(); } catch { } };
            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                {
                    _instance = null;
                    BaFormSession.Release("DONATIBOYU");
                }
            };

            try
            {
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 80,
                    Screen.PrimaryScreen.WorkingArea.Top + 60);
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
            if (keyData == Keys.Enter || keyData == Keys.Return)
            {
                OnRun(this, EventArgs.Empty);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void LoadFromSettings()
        {
            SelectOlcek(_cmbOlcek, DonatiBoyuSettings.OlcekCarpani);
            _chkYuvarla.Checked = DonatiBoyuSettings.Yuvarlama == DonatiBoyuYuvarlama.BesCm;
            _txtFi.Text = string.IsNullOrWhiteSpace(DonatiBoyuSettings.FiIsareti) ? "ø" : DonatiBoyuSettings.FiIsareti;
            SelectString(_cmbLFormat, DonatiBoyuSettings.LEtiketFormati);
            _chkOnizleme.Checked = DonatiBoyuSettings.OnizlemeMesaji;
            FillList(_lstLayers, DonatiBoyuSettings.Katmanlar);
        }

        private void PushSettings()
        {
            if (_cmbOlcek.SelectedItem is OlcekItem oi)
                DonatiBoyuSettings.OlcekCarpani = oi.Factor;
            DonatiBoyuSettings.Yuvarlama = _chkYuvarla.Checked
                ? DonatiBoyuYuvarlama.BesCm
                : DonatiBoyuYuvarlama.Yok;
            string fi = (_txtFi.Text ?? "").Trim();
            DonatiBoyuSettings.FiIsareti = fi.Length == 0 ? "ø" : fi;
            if (_cmbLFormat.SelectedItem is string fmt)
                DonatiBoyuSettings.LEtiketFormati = fmt;
            DonatiBoyuSettings.OnizlemeMesaji = _chkOnizleme.Checked;
            DonatiBoyuSettings.SetKatmanlar(CollectList(_lstLayers));
        }

        private void OnRun(object sender, EventArgs e)
        {
            PushSettings();
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                doc.SendStringToExecute("DONATIBOYUCIZ ", true, false, false);
                Close();
            }
            catch { }
        }

        private void StartLayerPick(bool add)
        {
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                PushSettings();
                HideTemporarily();
                doc.SendStringToExecute(add ? "DONATIBOYUKATMANEKLE " : "DONATIBOYUKATMANKALDIR ", true, false, false);
            }
            catch { }
        }

        private void RemoveSelectedFromList()
        {
            if (_lstLayers.SelectedItems.Count == 0) return;
            var toRemove = new List<string>();
            foreach (object o in _lstLayers.SelectedItems)
            {
                if (o is string s && !string.IsNullOrWhiteSpace(s))
                    toRemove.Add(s);
            }
            foreach (string name in toRemove)
            {
                DonatiBoyuSettings.RemoveKatman(name);
                _lstLayers.Items.Remove(name);
            }
            PushSettings();
        }

        private ComboBox AddComboRow(string label, ref int y, int w)
        {
            Controls.Add(MkLabel(label, 10, y + 3, 100));
            var c = MkCombo(114, y, w);
            Controls.Add(c);
            y += 26;
            return c;
        }

        private static void FillList(ListBox lst, IReadOnlyList<string> layers)
        {
            lst.Items.Clear();
            if (layers == null) return;
            foreach (string n in layers)
            {
                if (!string.IsNullOrWhiteSpace(n))
                    lst.Items.Add(n.Trim());
            }
        }

        private static List<string> CollectList(ListBox lst)
        {
            var layers = new List<string>();
            foreach (object o in lst.Items)
                if (o is string s) layers.Add(s);
            return layers;
        }

        private static void SelectOlcek(ComboBox cmb, double f)
        {
            double mapped = f;
            if (Math.Abs(f - 2.0) < 1e-6) mapped = 0.5;
            else if (Math.Abs(f - 4.0) < 1e-6) mapped = 0.25;
            for (int i = 0; i < cmb.Items.Count; i++)
            {
                if (cmb.Items[i] is OlcekItem oi && Math.Abs(oi.Factor - mapped) < 1e-9)
                {
                    cmb.SelectedIndex = i;
                    return;
                }
            }
            if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        }

        private static void SelectString(ComboBox cmb, string s)
        {
            for (int i = 0; i < cmb.Items.Count; i++)
            {
                if (cmb.Items[i] is string t && t == s)
                {
                    cmb.SelectedIndex = i;
                    return;
                }
            }
            if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        }

        private static Label MkLabel(string text, int x, int y, int w) => new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = false,
            Size = new Size(w, 18),
            ForeColor = AcadPaletteTheme.TextMuted,
            BackColor = Color.Transparent
        };

        private static Button MkButton(string text, int x, int y, int w, bool accent)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = AcadPaletteTheme.BgAlt,
                ForeColor = accent ? AcadPaletteTheme.Accent : AcadPaletteTheme.Text,
                Cursor = Cursors.Hand,
                Font = new SdFont("Segoe UI", 8f)
            };
            b.FlatAppearance.BorderColor = AcadPaletteTheme.InputBorder;
            b.FlatAppearance.MouseOverBackColor = AcadPaletteTheme.BgHover;
            return b;
        }

        private static ComboBox MkCombo(int x, int y, int w) => new ComboBox
        {
            Location = new Point(x, y),
            Size = new Size(w, 22),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = AcadPaletteTheme.InputBg,
            ForeColor = AcadPaletteTheme.Text,
            FlatStyle = FlatStyle.Flat
        };

        private sealed class OlcekItem
        {
            public double Factor { get; }
            public string Text { get; }
            public OlcekItem(double f, string t) { Factor = f; Text = t; }
            public override string ToString() => Text;
        }
    }
}
