using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using Beykent.Monolit.Girdi;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    /// <summary>Donatı metraj ayarları.</summary>
    internal static class DonatiMetrajSettings
    {
        /// <summary>Bu değerin üstündeki L boyu komutta uyarılır. Varsayılan 1200 cm.</summary>
        public static double MaxBoyCm { get; set; } = 1200;
        /// <summary>Etikette aranan çap işareti. Varsayılan ø; %%C / Ø ile de eşleşir.</summary>
        public static string FiIsareti { get; set; } = "ø";
        public static string LEtiketFormati { get; set; } = "L=";
        /// <summary>Katman listesi boşken DONATI/ETRIYE/HASIR/CIROZ süzgeci.</summary>
        public static bool SadeceDonatiKatmani { get; set; } = true;
        public static double MinFarkCm { get; set; } = 0.5;
        public static bool OnizlemeMesaji { get; set; } = true;
        /// <summary>Poz balonunun içi çapa göre pastel dolar. Tarama katmanı çıktıda kapalıdır.</summary>
        public static bool Renkli { get; set; } = true;
        public static List<string> Katmanlar { get; } = new List<string>();

        static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var m = BaIni.Read("donatimetraj.ini");
            MaxBoyCm = BaIni.GetD(m, "MaxBoyCm", MaxBoyCm);
            if (MaxBoyCm < 1) MaxBoyCm = 1200;
            FiIsareti = BaIni.Get(m, "FiIsareti", FiIsareti);
            if (string.IsNullOrWhiteSpace(FiIsareti)) FiIsareti = "ø";
            LEtiketFormati = BaIni.Get(m, "LEtiketFormati", LEtiketFormati);
            SadeceDonatiKatmani = BaIni.GetB(m, "SadeceDonatiKatmani", SadeceDonatiKatmani);
            MinFarkCm = BaIni.GetD(m, "MinFarkCm", MinFarkCm);
            OnizlemeMesaji = BaIni.GetB(m, "OnizlemeMesaji", OnizlemeMesaji);
            Renkli = BaIni.GetB(m, "Renkli", Renkli);
            ReplaceKatmanlar(SplitKatmanlar(BaIni.Get(m, "Katmanlar", "")));
        }

        public static void Save()
        {
            EnsureLoaded();
            BaIni.Write("donatimetraj.ini",
                "MaxBoyCm=" + BaIni.N(MaxBoyCm),
                "FiIsareti=" + (string.IsNullOrWhiteSpace(FiIsareti) ? "ø" : FiIsareti.Trim()),
                "LEtiketFormati=" + (LEtiketFormati ?? "L="),
                "SadeceDonatiKatmani=" + BaIni.B(SadeceDonatiKatmani),
                "MinFarkCm=" + BaIni.N(MinFarkCm),
                "OnizlemeMesaji=" + BaIni.B(OnizlemeMesaji),
                "Renkli=" + BaIni.B(Renkli),
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

    /// <summary>Modeless form — gömülü demir tablosunu yerleştirir. Kapanış donatı boyu ile aynıdır.</summary>
    internal sealed class DonatiMetrajForm : Beykent.Monolit.MonolitForm
    {
        private static DonatiMetrajForm _instance;

        private readonly CapIsaretiSecim _cmbFi;
        private readonly OlcuKutusu _txtMax;
        private readonly GirdiDenetci Denetci = new GirdiDenetci();
        private readonly ListBox _lstLayers;
        private readonly CheckBox _chkOnizleme;
        private readonly CheckBox _chkRenkli;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("DONATIMETRAJ")) return;
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new DonatiMetrajForm();
                try { AcApp.ShowModelessDialog(_instance); }
                catch { _instance.Show(); }
            }
            else
            {
                _instance.LoadFromSettings();
                _instance.Visible = true;
                _instance.BringToFront();
            }
            BaFormSession.Attach("DONATIMETRAJ", _instance);
            BaFormSession.EndPick("DONATIMETRAJ");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("DONATIMETRAJ");
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
                
                BaFormSession.BeginPick("DONATIMETRAJ");
                _instance.Visible = false;
            }
            catch { }
        }

        public static void ShowAfterPick()
        {
            BaFormSession.EndPick("DONATIMETRAJ");
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
                FillList(_instance._lstLayers, DonatiMetrajSettings.Katmanlar);
            }
            catch { }
        }

        private DonatiMetrajForm()
        {
            DonatiMetrajSettings.EnsureLoaded();
            Text = "Donatı Metraj";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            Font = new SdFont("Segoe UI", 8.5f);
            KeyPreview = true;

            int y = 8;
            // Ayar property'si ve ini anahtarı (FiIsareti) aynı; listede olmayan eski kayıt korunur.
            _cmbFi = new CapIsaretiSecim(DonatiMetrajSettings.FiIsareti);
            Controls.Add(_cmbFi);
            // Alt sınır eski ParseMax (≥ 1) ile aynı; üst sınır 10000 cm (100 m) makul tavan.
            _txtMax = Denetci.Ekle(new OlcuKutusu("L boyu sınırı", "cm", 1, 10000, 1200, 2, 10));
            _txtMax.Deger = DonatiMetrajSettings.MaxBoyCm;

            Controls.Add(MkLabel("Donatı etiketi katmanı", 10, y + 2, 280));
            y += 18;
            _lstLayers = new ListBox
            {
                Location = new Point(10, y),
                Size = new Size(300, 78),
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
                SelectionMode = SelectionMode.MultiExtended
            };
            Controls.Add(_lstLayers);
            y += 82;
            FillList(_lstLayers, DonatiMetrajSettings.Katmanlar);

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
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                BackColor = Color.Transparent,
                Checked = DonatiMetrajSettings.OnizlemeMesaji
            };
            Controls.Add(_chkOnizleme);
            y += 22;

            _chkRenkli = new CheckBox
            {
                Text = "Renkli poz balonu",
                Location = new Point(10, y),
                AutoSize = true,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                BackColor = Color.Transparent,
                Checked = DonatiMetrajSettings.Renkli
            };
            Controls.Add(_chkRenkli);
            y += 26;

            var btnAntet = MkButton("Antet seç", 10, y, 146, false);
            btnAntet.Click += OnAntet;
            var btnToplam = MkButton("Toplam metraj", 164, y, 146, false);
            btnToplam.Click += OnToplam;
            Controls.Add(btnAntet);
            Controls.Add(btnToplam);
            y += 30;

            var btnRun = MkButton("METRAJ AL", 10, y, 168, true);
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
                    BaFormSession.Release("DONATIMETRAJ");
                }
            };

            try
            {
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 80,
                    Screen.PrimaryScreen.WorkingArea.Top + 60);
            }
            catch { StartPosition = FormStartPosition.CenterScreen; }

            var ui = BeginCompact(320);
            ui.Section("Etiket ve sınır")
                .Field("Çap işareti", _cmbFi)
                .Field("L boyu sınırı", _txtMax);
            ui.Section("Etiket katmanları").Layers(_lstLayers, btnAdd, btnDel, btnRem);
            ui.Tip(_chkOnizleme, _chkOnizleme.Text); _chkOnizleme.Text = "Özet yaz";
            ui.Section(null).Choices(_chkRenkli, _chkOnizleme);
            ui.Section(null).Control(Denetci.Serit, 44);
            ui.Tip(_cmbFi, "Etikette aranan çap işareti (%%c / Ø ile de eşleşir).");
            ui.Tip(_txtMax, "Bu değerin üstündeki L boyu komutta uyarılır.");
            ui.Footer(btnRun, btnClose, btnAntet, btnToplam);
            btnRun.Text = "Metraj al";
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
                OnRun(this, EventArgs.Empty);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void LoadFromSettings()
        {
            _cmbFi.Deger = DonatiMetrajSettings.FiIsareti;
            _txtMax.Deger = DonatiMetrajSettings.MaxBoyCm;
            _chkOnizleme.Checked = DonatiMetrajSettings.OnizlemeMesaji;
            _chkRenkli.Checked = DonatiMetrajSettings.Renkli;
            FillList(_lstLayers, DonatiMetrajSettings.Katmanlar);
            Denetci.Guncelle();
        }

        /// <summary>Geçersiz sınır kutusu son geçerli değeri tutar; sessiz 1200'e dönüş yok.</summary>
        private void PushSettings()
        {
            DonatiMetrajSettings.MaxBoyCm = _txtMax.Deger;
            DonatiMetrajSettings.FiIsareti = _cmbFi.Deger;
            DonatiMetrajSettings.OnizlemeMesaji = _chkOnizleme.Checked;
            DonatiMetrajSettings.Renkli = _chkRenkli.Checked;
            DonatiMetrajSettings.SetKatmanlar(CollectList(_lstLayers));
        }

        private void OnAntet(object sender, EventArgs e)
        {
            if (!Denetci.Onayla()) return;
            PushSettings();
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                doc.SendStringToExecute("DONATIMETRAJANTET ", true, false, false);
                Close();
            }
            catch { }
        }

        private void OnToplam(object sender, EventArgs e)
        {
            if (!Denetci.Onayla()) return;
            PushSettings();
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                doc.SendStringToExecute("DONATIMETRAJTOPLAM ", true, false, false);
                Close();
            }
            catch { }
        }

        private void OnRun(object sender, EventArgs e)
        {
            if (!Denetci.Onayla()) return;
            PushSettings();
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                doc.SendStringToExecute("DONATIMETRAJCIZ ", true, false, false);
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
                doc.SendStringToExecute(add ? "DONATIMETRAJKATMANEKLE " : "DONATIMETRAJKATMANKALDIR ", true, false, false);
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
                DonatiMetrajSettings.RemoveKatman(name);
                _lstLayers.Items.Remove(name);
            }
            PushSettings();
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

        private static Label MkLabel(string text, int x, int y, int w) => new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = false,
            Size = new Size(w, 18),
            ForeColor = Beykent.Monolit.StandardFormTheme.TextMuted,
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
                BackColor = Beykent.Monolit.StandardFormTheme.BgAlt,
                ForeColor = accent ? Beykent.Monolit.StandardFormTheme.Accent : Beykent.Monolit.StandardFormTheme.Text,
                Cursor = Cursors.Hand,
                Font = new SdFont("Segoe UI", 8f)
            };
            b.FlatAppearance.BorderColor = Beykent.Monolit.StandardFormTheme.InputBorder;
            b.FlatAppearance.MouseOverBackColor = Beykent.Monolit.StandardFormTheme.BgHover;
            return b;
        }

    }
}
