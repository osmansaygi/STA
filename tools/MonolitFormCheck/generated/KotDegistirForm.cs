using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    /// <summary>Kot değiştir: eski/yeni kot ve yazı katmanları.</summary>
    internal static class KotDegistirSettings
    {
        public const string DefaultLayer = "KOT YAZI (BEYKENT)";

        public static string EskiKot { get; set; } = "";
        public static string YeniKot { get; set; } = "";
        public static List<string> Katmanlar { get; } = new List<string>();

        static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            string path = Path.Combine(BaIni.Dir(), "kotdegistir.ini");
            bool exists = TestStore.Exists(path);
            var m = BaIni.Read("kotdegistir.ini");
            EskiKot = BaIni.Get(m, "EskiKot", "");
            YeniKot = BaIni.Get(m, "YeniKot", "");
            if (!exists)
                ReplaceKatmanlar(new[] { DefaultLayer });
            else
                ReplaceKatmanlar(SplitKatmanlar(BaIni.Get(m, "Katmanlar", "")));
        }

        public static void Save()
        {
            EnsureLoaded();
            BaIni.Write("kotdegistir.ini",
                "EskiKot=" + (EskiKot ?? ""),
                "YeniKot=" + (YeniKot ?? ""),
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

    /// <summary>Modeless form — seçili katmanlardaki kot yazısını eski değerden yeni değere çevirir.</summary>
    internal sealed class KotDegistirForm : Beykent.Monolit.MonolitForm
    {
        private static KotDegistirForm _instance;

        private readonly TextBox _txtEski;
        private readonly TextBox _txtYeni;
        private readonly ListBox _lstLayers;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("KOTDEGISTIR")) return;
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new KotDegistirForm();
                try { AcApp.ShowModelessDialog(_instance); }
                catch { _instance.Show(); }
            }
            else
            {
                _instance.LoadFromSettings();
                _instance.Visible = true;
                _instance.BringToFront();
            }

            BaFormSession.Attach("KOTDEGISTIR", _instance);
            BaFormSession.EndPick("KOTDEGISTIR");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("KOTDEGISTIR");
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
                
                BaFormSession.BeginPick("KOTDEGISTIR");
                _instance.Visible = false;
            }
            catch { }
        }

        public static void ShowAfterPick()
        {
            BaFormSession.EndPick("KOTDEGISTIR");
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
                FillList(_instance._lstLayers, KotDegistirSettings.Katmanlar);
            }
            catch { }
        }

        private KotDegistirForm()
        {
            KotDegistirSettings.EnsureLoaded();
            Text = "Kot Değiştir";
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
            Controls.Add(MkLabel("Eski kot (cm)", 10, y + 3, 104));
            _txtEski = MkBox(118, y, 192);
            _txtEski.Text = KotDegistirSettings.EskiKot ?? "";
            Controls.Add(_txtEski);
            y += 28;

            Controls.Add(MkLabel("Yeni kot (cm)", 10, y + 3, 104));
            _txtYeni = MkBox(118, y, 192);
            _txtYeni.Text = KotDegistirSettings.YeniKot ?? "";
            Controls.Add(_txtYeni);
            y += 30;

            Controls.Add(MkLabel("Kot yazısı katmanı", 10, y + 2, 280));
            y += 18;
            _lstLayers = new ListBox
            {
                Location = new Point(10, y),
                Size = new Size(300, 90),
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
                SelectionMode = SelectionMode.MultiExtended
            };
            Controls.Add(_lstLayers);
            y += 94;
            FillList(_lstLayers, KotDegistirSettings.Katmanlar);

            var btnAdd = MkButton("Katman ekle", 10, y, 96, false);
            var btnDel = MkButton("Listeden kaldır", 110, y, 100, false);
            var btnRem = MkButton("Katman kaldır", 214, y, 96, false);
            btnAdd.Click += (s, e) => StartLayerPick(add: true);
            btnDel.Click += (s, e) => RemoveSelectedFromList();
            btnRem.Click += (s, e) => StartLayerPick(add: false);
            Controls.Add(btnAdd);
            Controls.Add(btnDel);
            Controls.Add(btnRem);
            y += 32;

            var btnRun = MkButton("Seç ve değiştir", 10, y, 168, true);
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
                    BaFormSession.Release("KOTDEGISTIR");
                }
            };

            try
            {
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 80,
                    Screen.PrimaryScreen.WorkingArea.Top + 60);
            }
            catch { StartPosition = FormStartPosition.CenterScreen; }

            // Kompakt, içerik odaklı form düzeni.
            var ui = BeginCompact(320);
            ui.Section(null).Grid(new[] { "Eski kot · cm", "Yeni kot · cm" }, new Control[] { _txtEski, _txtYeni });
            ui.Section("Kot yazısı katmanları").Layers(_lstLayers, btnAdd, btnDel, btnRem);
            ui.Footer(btnRun, btnClose);
            ui.Finish();
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
            _txtEski.Text = KotDegistirSettings.EskiKot ?? "";
            _txtYeni.Text = KotDegistirSettings.YeniKot ?? "";
            FillList(_lstLayers, KotDegistirSettings.Katmanlar);
        }

        private void PushSettings()
        {
            KotDegistirSettings.EskiKot = (_txtEski.Text ?? "").Trim();
            KotDegistirSettings.YeniKot = (_txtYeni.Text ?? "").Trim();
            KotDegistirSettings.SetKatmanlar(CollectList(_lstLayers));
        }

        private void OnRun(object sender, EventArgs e)
        {
            PushSettings();
            if (!KotDegistirRunner.TryParseKot(KotDegistirSettings.EskiKot, out double eskiCm) ||
                !KotDegistirRunner.TryParseKot(KotDegistirSettings.YeniKot, out double yeniCm))
            {
                MessageBox.Show(this, "Eski kot ve yeni kot cm cinsinden sayı olmalı.", "Kot Değiştir",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (Math.Abs(yeniCm - eskiCm) < 0.05)
            {
                MessageBox.Show(this, "Eski kot ile yeni kot aynı.", "Kot Değiştir",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (KotDegistirSettings.Katmanlar.Count == 0)
            {
                MessageBox.Show(this, "Kot yazısı katmanı ekleyin.", "Kot Değiştir",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                doc.SendStringToExecute("KOTDEGISTIRUYGULA ", true, false, false);
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
                doc.SendStringToExecute(add ? "KOTDEGISTIRKATMANEKLE " : "KOTDEGISTIRKATMANKALDIR ", true, false, false);
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
                KotDegistirSettings.RemoveKatman(name);
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

        private static TextBox MkBox(int x, int y, int w) => new TextBox
        {
            Location = new Point(x, y),
            Size = new Size(w, 22),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
            ForeColor = Beykent.Monolit.StandardFormTheme.Text,
            Font = new SdFont("Segoe UI", 9f)
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
