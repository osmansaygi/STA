using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Beykent.Monolit.Girdi;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    /// <summary>
    /// Döşeme Donatı formu. Donatı Çiz serbest yönle çalışır.
    /// Sınır listesi konsol kenarı, mesnet listesi mesnet kabulü.
    /// </summary>
    internal sealed class DosemeDonatiForm : Beykent.Monolit.MonolitForm
    {
        private static DosemeDonatiForm _instance;

        private readonly SegmentSecim _yon;
        private readonly SegmentSecim _olcek;
        private readonly ListBox _lstSinir;
        private readonly ListBox _lstMesnet;
        private readonly ListBox _lstBosluk;
        private readonly ListBox _lstKatSinir;
        private readonly Button _btnCizgisel;
        private readonly Button _btnClose;
        private bool _starting;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("DOSEMEDONATI")) return;
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new DosemeDonatiForm();
                try { AcApp.ShowModelessDialog(_instance); }
                catch { _instance.Show(); }
            }
            else
            {
                _instance._starting = false;
                _instance.RefreshLists();
                _instance.Visible = true;
                _instance.BringToFront();
            }

            BaFormSession.Attach("DOSEMEDONATI", _instance);
            BaFormSession.EndPick("DOSEMEDONATI");
        }

        /// <summary>Plan ölçeği 1/100: donatı yazıları ve aralıkları ×1,5 (pafta_olcek.ini, dosemedonati).</summary>
        internal static bool Olcek100 => BaPaftaOlcek.Yuz(OlcekAnahtar);
        private const string OlcekAnahtar = "dosemedonati";

        public static void Shutdown()
        {
            BaFormSession.Release("DOSEMEDONATI");
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
                
                BaFormSession.BeginPick("DOSEMEDONATI");
                _instance.Visible = false;
            }
            catch { }
        }

        public static void ShowAfterPick()
        {
            BaFormSession.EndPick("DOSEMEDONATI");
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
            ClientSize = new Size(368, 420);
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Color.White;
            Font = new SdFont("Segoe UI", 9f);
            KeyPreview = true;

            DosemeKonsolSettings.EnsureLoaded();
            DosemeDonatiSettings.EnsureLoaded();
            DosemeDonatiRunner.EnsureYonLoaded();

            int y = 10;
            // Yön ve ölçek ortak SegmentSecim; kayıt yolları aynı (DosemeDonatiRunner.SaveYon, BaPaftaOlcek "dosemedonati").
            _yon = new SegmentSecim("Serbest", "X yönü", "Y yönü");
            ApplyYonToSegment(DosemeDonatiRunner.Yon);
            _yon.SeciliDegisti += (s, e) =>
            {
                DosemeDonatiRunner.Yon = SeciliYon();
                try { DosemeDonatiRunner.SaveYon(); } catch { }
            };

            // Bu formda 1/100 = yazı ve aralık ×1,5 (DosemeDonatiRunner); etiket bu anlamı korur.
            _olcek = new SegmentSecim("1/50 pafta", "1/100 pafta (×1,5)") { Secili = Olcek100 ? 1 : 0 };
            _olcek.SeciliDegisti += (s, e) => { try { BaPaftaOlcek.Kaydet(OlcekAnahtar, _olcek.Secili == 1); } catch { } };

            var btnAyar = MakeButton("Konsol donatı ayarları…", 12, y, 344);
            btnAyar.Click += (s, e) =>
            {
                using (var f = new DosemeKonsolAyarForm())
                    BaFormSession.ShowChildDialog(this, f);
            };
            Controls.Add(btnAyar);
            y += 36;

            _btnCizgisel = MakeButton("Donatı Çiz", 12, y, 230);
            _btnCizgisel.Font = new SdFont("Segoe UI", 9f, FontStyle.Bold);
            _btnCizgisel.ForeColor = Beykent.Monolit.StandardFormTheme.Accent;
            _btnCizgisel.Click += OnCizgisel;
            _btnClose = MakeButton("Kapat", 250, y, 106);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnCizgisel);
            Controls.Add(_btnClose);
            y += 36;

            Controls.Add(MakeLabel("Konsol sınır çizgileri", 12, y)); y += 18;
            _lstSinir = MakeList(12, y, 344, 56);
            Controls.Add(_lstSinir); y += 60;
            AddLayerButtons(y, DosemeKatmanListe.Sinir, _lstSinir); y += 34;

            Controls.Add(MakeLabel("Mesnet çizgileri", 12, y)); y += 18;
            _lstMesnet = MakeList(12, y, 344, 56);
            Controls.Add(_lstMesnet); y += 60;
            AddLayerButtons(y, DosemeKatmanListe.Mesnet, _lstMesnet); y += 34;

            Controls.Add(MakeLabel("Boşluk poligonları (içine donatı yok)", 12, y)); y += 18;
            _lstBosluk = MakeList(12, y, 344, 48);
            Controls.Add(_lstBosluk); y += 52;
            AddLayerButtons(y, DosemeKatmanListe.Bosluk, _lstBosluk); y += 34;

            Controls.Add(MakeLabel("Kat sınırı poligonları (dışına donatı yok)", 12, y)); y += 18;
            _lstKatSinir = MakeList(12, y, 344, 48);
            Controls.Add(_lstKatSinir); y += 52;
            AddLayerButtons(y, DosemeKatmanListe.KatSinir, _lstKatSinir);
            y += 40;
            ClientSize = new Size(368, y);

            RefreshLists();

            FormClosing += (s, e) => { try { DosemeDonatiSettings.Save(); } catch { } };
            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                {
                    _instance = null;
                    BaFormSession.Release("DOSEMEDONATI");
                }
            };

            try
            {
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 110,
                    Screen.PrimaryScreen.WorkingArea.Top + 110);
            }
            catch { StartPosition = FormStartPosition.CenterScreen; }

            // Kompakt, içerik odaklı form düzeni.
            var ui = BeginCompact(370);
            ui.Section("Donatı yönü  (GPR yazı: X kırmızı / Y mavi)").Control(_yon);
            ui.Section("Pafta ölçeği").Control(_olcek);
            ui.Section(null).Actions(btnAyar);
            ui.Tabs(146,
                ui.Page("Konsol", p => p.Layers(_lstSinir, ui.LayerButtons(_lstSinir))),
                ui.Page("Mesnet", p => p.Layers(_lstMesnet, ui.LayerButtons(_lstMesnet))),
                ui.Page("Boşluk", p => p.Layers(_lstBosluk, ui.LayerButtons(_lstBosluk))),
                ui.Page("Kat sınırı", p => p.Layers(_lstKatSinir, ui.LayerButtons(_lstKatSinir))));
            ui.Footer(_btnCizgisel, _btnClose);
            ui.Finish();
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
                if (ActiveControl is ListBox)
                    return base.ProcessCmdKey(ref msg, keyData);
                OnCizgisel(this, EventArgs.Empty);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ApplyYonToSegment(DosemeDonatiRunner.DonatiYonu yon)
        {
            _yon.Secili = yon == DosemeDonatiRunner.DonatiYonu.X ? 1 : yon == DosemeDonatiRunner.DonatiYonu.Y ? 2 : 0;
        }

        private DosemeDonatiRunner.DonatiYonu SeciliYon()
        {
            if (_yon.Secili == 1) return DosemeDonatiRunner.DonatiYonu.X;
            if (_yon.Secili == 2) return DosemeDonatiRunner.DonatiYonu.Y;
            return DosemeDonatiRunner.DonatiYonu.Serbest;
        }

        private void RefreshLists()
        {
            DosemeDonatiSettings.EnsureLoaded();
            FillList(_lstSinir, DosemeDonatiSettings.SinirKatmanlar);
            FillList(_lstMesnet, DosemeDonatiSettings.MesnetKatmanlar);
            FillList(_lstBosluk, DosemeDonatiSettings.BoslukKatmanlar);
            FillList(_lstKatSinir, DosemeDonatiSettings.KatSinirKatmanlar);
        }

        private void AddLayerButtons(int y, DosemeKatmanListe which, ListBox list)
        {
            var btnAdd = MakeButton("Katman ekle", 12, y, 110);
            var btnDel = MakeButton("Listeden sil", 128, y, 110);
            var btnRem = MakeButton("Katman kaldır", 244, y, 112);
            btnAdd.Click += (s, e) => StartLayerPick(which, add: true);
            btnDel.Click += (s, e) => RemoveSelected(which, list);
            btnRem.Click += (s, e) => StartLayerPick(which, add: false);
            Controls.Add(btnAdd);
            Controls.Add(btnDel);
            Controls.Add(btnRem);
        }

        private void StartLayerPick(DosemeKatmanListe which, bool add)
        {
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                DosemeDonatiSettings.PickList = which;
                DosemeDonatiSettings.Save();
                HideTemporarily();
                doc.SendStringToExecute(
                    add ? "DOSEMEDONATIKATMANEKLE " : "DOSEMEDONATIKATMANKALDIR ",
                    true, false, false);
            }
            catch { }
        }

        private void RemoveSelected(DosemeKatmanListe which, ListBox list)
        {
            if (list.SelectedItems.Count == 0) return;
            var toRemove = new List<string>();
            foreach (object o in list.SelectedItems)
            {
                if (o is string s && !string.IsNullOrWhiteSpace(s))
                    toRemove.Add(s);
            }
            foreach (string name in toRemove)
                DosemeDonatiSettings.RemoveKatman(which, name);
            RefreshLists();
        }

        private static void FillList(ListBox list, IEnumerable<string> names)
        {
            if (list == null) return;
            list.Items.Clear();
            if (names == null) return;
            foreach (string n in names)
            {
                if (!string.IsNullOrWhiteSpace(n))
                    list.Items.Add(n);
            }
        }

        private static ListBox MakeList(int x, int y, int w, int h) => new ListBox
        {
            Location = new Point(x, y),
            Size = new Size(w, h),
            BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
            ForeColor = Beykent.Monolit.StandardFormTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false,
            SelectionMode = SelectionMode.MultiExtended
        };

        private void OnCizgisel(object sender, EventArgs e)
        {
            if (_starting) return;
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                DosemeDonatiRunner.Yon = SeciliYon();
                try { DosemeDonatiRunner.SaveYon(); } catch { }
                DosemeDonatiSettings.Save();
                _starting = true;
                HideTemporarily();
                doc.SendStringToExecute("DOSEMEDONATICIZGISEL ", true, false, false);
            }
            catch
            {
                _starting = false;
            }
        }

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
                BackColor = Beykent.Monolit.StandardFormTheme.BgAlt,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Beykent.Monolit.StandardFormTheme.InputBorder;
            b.FlatAppearance.MouseOverBackColor = Beykent.Monolit.StandardFormTheme.BgHover;
            return b;
        }
    }
}
