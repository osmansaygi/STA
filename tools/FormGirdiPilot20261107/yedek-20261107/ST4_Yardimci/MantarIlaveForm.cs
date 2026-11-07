using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>Mantar döşeme ilave donatısı formu. Çiz → MANTARILAVECIZ (kolon seçimi).</summary>
    internal sealed class MantarIlaveForm : Beykent.Monolit.MonolitForm
    {
        const string Family = "MANTARILAVE";
        static MantarIlaveForm _instance;
        readonly Dictionary<string, NumericUpDown> _numbers = new Dictionary<string, NumericUpDown>();
        readonly ComboBox _xCap, _yCap;
        readonly CheckBox _cizimden;
        bool _starting;
        internal static MantarIlaveSettings Current;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter(Family)) return;
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new MantarIlaveForm();
                AcApp.ShowModelessDialog(_instance);
            }
            else { _instance.Show(); _instance.BringToFront(); }
            BaFormSession.Attach(Family, _instance);
            BaFormSession.EndPick(Family);
        }

        public static void Shutdown()
        {
            BaFormSession.Release(Family);
            if (_instance != null) { _instance.Close(); _instance = null; }
        }

        MantarIlaveForm()
        {
            Current = MantarIlaveSettings.Load();
            SuspendLayout();
            Text = "Mantar Döşeme İlave Donatısı";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MaximizeBox = MinimizeBox = false;
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            KeyPreview = true;

            _xCap = CapCombo(Current.XCap);
            _yCap = CapCombo(Current.YCap);
            Number("XBoy", 50, 5000, 0, 10);
            Number("YBoy", 50, 5000, 0, 10);
            Number("XAralik", 5, 50, 1, 0.5m);
            Number("YAralik", 5, 50, 1, 0.5m);
            Number("DosemeKalinligi", 10, 200, 1, 1);
            Number("Paspayi", 0, 10, 1, 0.5m);
            _cizimden = new CheckBox { Text = "Kalınlığı çizimden oku (DOSEME ISMI d=)", Checked = Current.KalinlikCizimden,
                BackColor = Color.Transparent, ForeColor = Beykent.Monolit.StandardFormTheme.Text };
            Controls.AddRange(new Control[] { _xCap, _yCap, _cizimden });

            var close = MakeButton("Kapat", false);
            close.Click += (s, e) => Close();
            var draw = MakeButton("Kolon seç ve çiz", true);
            draw.Click += (s, e) => Draw();
            FormClosed += (s, e) => { ReadSettings().Save(); _instance = null; BaFormSession.Release(Family); };
            ResumeLayout(false);

            var ui = BeginCompact(380);
            ui.Section("X yönü (yatay çubuk)")
                .Field("Boy · cm", _numbers["XBoy"])
                .Field("Ø mm / ara cm", _xCap, _numbers["XAralik"]);
            ui.Section("Y yönü (düşey çubuk)")
                .Field("Boy · cm", _numbers["YBoy"])
                .Field("Ø mm / ara cm", _yCap, _numbers["YAralik"])
                .Hint("Donatı kolona ortalanır. X donatısı Y boyu, Y donatısı X boyu genişliğinde dağıtılır.");
            ui.Section("Döşeme · gönye")
                .Options(_cizimden)
                .Field("Kalınlık / paspayı · cm", _numbers["DosemeKalinligi"], _numbers["Paspayi"])
                .Hint("Üst ilave. KAT SINIRI / KALIP BOSLUK'a denk gelen uç, paspayı kadar içeride kesilir; gönye = kalınlık − 2×paspayı.");
            ui.Footer(draw, close);
            ui.Finish();
        }

        ComboBox CapCombo(double selected)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg, ForeColor = Beykent.Monolit.StandardFormTheme.Text };
            var sizes = new List<double> { 8, 10, 12, 14, 16, 18, 20, 22, 24, 25, 26, 28, 30, 32 };
            if (selected >= 6 && selected <= 40 && !sizes.Contains(selected)) { sizes.Add(selected); sizes.Sort(); }
            foreach (double d in sizes) combo.Items.Add(d);
            combo.FormattingEnabled = true;
            combo.Format += (s, e) => e.Value = "Ø" + Convert.ToDouble(e.ListItem, CultureInfo.InvariantCulture).ToString("0.#", CultureInfo.InvariantCulture);
            combo.SelectedItem = selected;
            if (combo.SelectedIndex < 0) combo.SelectedItem = 14d;
            return combo;
        }

        void Number(string key, decimal min, decimal max, int decimals, decimal step)
        {
            var n = new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = step,
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg, ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right };
            double value = (double)typeof(MantarIlaveSettings).GetField(key).GetValue(Current);
            n.Value = (decimal)Math.Max((double)min, Math.Min((double)max, value));
            YardimciUi.StripUpDownButtons(n);
            _numbers.Add(key, n);
            Controls.Add(n);
        }

        static Button MakeButton(string text, bool primary)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, UseVisualStyleBackColor = false,
                BackColor = primary ? Beykent.Monolit.StandardFormTheme.Accent : Beykent.Monolit.StandardFormTheme.BgAlt,
                ForeColor = primary ? Beykent.Monolit.StandardFormTheme.InputBg : Beykent.Monolit.StandardFormTheme.Text };
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Beykent.Monolit.StandardFormTheme.InputBorder;
            return b;
        }

        MantarIlaveSettings ReadSettings()
        {
            var s = new MantarIlaveSettings();
            foreach (var kv in _numbers) typeof(MantarIlaveSettings).GetField(kv.Key).SetValue(s, (double)kv.Value.Value);
            s.XCap = Convert.ToDouble(_xCap.SelectedItem, CultureInfo.InvariantCulture);
            s.YCap = Convert.ToDouble(_yCap.SelectedItem, CultureInfo.InvariantCulture);
            s.KalinlikCizimden = _cizimden.Checked;
            return s;
        }

        void Draw()
        {
            if (_starting) return;
            var settings = ReadSettings();
            string error = settings.Validate();
            if (error != null) { MessageBox.Show(this, error, Text); return; }
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Current = settings;
            Current.Save();
            _starting = true;
            BaFormSession.BeginPick(Family);
            Hide();
            try { doc.SendStringToExecute("MANTARILAVECIZ ", true, false, false); }
            catch { _starting = false; Show(); BaFormSession.EndPick(Family); throw; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == Keys.Enter)
            {
                if (_xCap.DroppedDown || _yCap.DroppedDown) return base.ProcessCmdKey(ref msg, keyData);
                Draw(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
