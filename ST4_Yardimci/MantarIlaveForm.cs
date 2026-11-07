using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Beykent.Monolit.Girdi;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>Mantar döşeme ilave donatısı formu. Çiz → MANTARILAVECIZ (kolon seçimi).</summary>
    internal sealed class MantarIlaveForm : Beykent.Monolit.MonolitForm
    {
        const string Family = "MANTARILAVE";
        static MantarIlaveForm _instance;
        readonly OlcuKutusu _xBoy, _yBoy, _kalinlik, _paspayi;
        readonly DonatiKutusu _xDonati, _yDonati;
        readonly GirdiDenetci Denetci = new GirdiDenetci();
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

            // Sınırlar eski NumericUpDown Min/Max ile aynı; yükleme yine sınıra kırpılır.
            _xBoy = Olcu("X boyu", "cm", 50, 5000, Current.XBoy, 0, 10);
            _yBoy = Olcu("Y boyu", "cm", 50, 5000, Current.YBoy, 0, 10);
            _kalinlik = Olcu("Döşeme kalınlığı (yedek)", "cm", 10, 200, Current.DosemeKalinligi, 1, 1);
            _paspayi = Olcu("Paspayı", "cm", 0, 10, Current.Paspayi, 1, 0.5);
            // Çap: eski liste ø8–ø32, MantarIlaveSettings.Validate en az ø6 kabul eder. Aralık 5–50 cm.
            _xDonati = Denetci.Ekle(new DonatiKutusu("X donatısı", CapYuvarla(Current.XCap), Kirp(Current.XAralik, 5, 50), 6, 32, 5, 50));
            _yDonati = Denetci.Ekle(new DonatiKutusu("Y donatısı", CapYuvarla(Current.YCap), Kirp(Current.YAralik, 5, 50), 6, 32, 5, 50));
            _cizimden = new CheckBox { Text = "Kalınlığı çizimden oku (DOSEME ISMI d=)", Checked = Current.KalinlikCizimden,
                BackColor = Color.Transparent, ForeColor = Beykent.Monolit.StandardFormTheme.Text };
            Controls.Add(_cizimden);

            Denetci.HataKurali(() => _kalinlik.Deger < 2 * _paspayi.Deger + 5
                ? "Döşeme kalınlığı paspayına göre çok küçük (≥ 2·paspayı + 5 cm)" : null, _kalinlik, _paspayi);

            var close = MakeButton("Kapat", false);
            close.Click += (s, e) => Close();
            var draw = MakeButton("Kolon seç ve çiz", true);
            draw.Click += (s, e) => Draw();
            FormClosed += (s, e) => { if (Denetci.Uygun) ReadSettings().Save(); _instance = null; BaFormSession.Release(Family); };
            ResumeLayout(false);

            var ui = BeginCompact(380);
            ui.Section("Döşeme · gönye")
                .Options(_cizimden)
                .Field("Kalınlık (yedek)", _kalinlik)
                .Field("Paspayı", _paspayi)
                .Hint("Üst ilave. KAT SINIRI / KALIP BOSLUK'a denk gelen uç, paspayı kadar içeride kesilir; gönye = kalınlık − 2×paspayı.");
            ui.Section("X yönü (yatay çubuk)  (ø çap / aralık)")
                .Field("Boy", _xBoy)
                .Field("Donatı", _xDonati);
            ui.Section("Y yönü (düşey çubuk)  (ø çap / aralık)")
                .Field("Boy", _yBoy)
                .Field("Donatı", _yDonati)
                .Hint("Donatı kolona ortalanır. X donatısı Y boyu, Y donatısı X boyu genişliğinde dağıtılır.");
            ui.Section(null).Control(Denetci.Serit, 44);
            ui.Footer(draw, close);
            ui.Tip(_kalinlik, "Kalınlık çizimden okunamazsa (tik kapalı ya da DOSEME ISMI d= bulunamadı) bu değer kullanılır.");
            ui.Finish();
            Denetci.Guncelle();
        }

        OlcuKutusu Olcu(string ad, string birim, double min, double max, double value, int ondalik, double adim)
            => Denetci.Ekle(new OlcuKutusu(ad, birim, min, max, Kirp(value, min, max), ondalik, adim));

        static double Kirp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));
        static int CapYuvarla(double mm) => (int)Math.Round(mm, MidpointRounding.AwayFromZero);

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
            s.XBoy = _xBoy.Deger;
            s.YBoy = _yBoy.Deger;
            s.XCap = _xDonati.CapMm;
            s.XAralik = _xDonati.AralikCm;
            s.YCap = _yDonati.CapMm;
            s.YAralik = _yDonati.AralikCm;
            s.DosemeKalinligi = _kalinlik.Deger;
            s.Paspayi = _paspayi.Deger;
            s.KalinlikCizimden = _cizimden.Checked;
            return s;
        }

        void Draw()
        {
            if (_starting) return;
            if (!Denetci.Onayla()) return;
            var settings = ReadSettings();
            // Settings.Validate kuralları denetçide (sınırlar + kalınlık kuralı); yine de güvence olarak korunur.
            if (settings.Validate() != null) return;
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
            if (keyData == Keys.Enter && !(ActiveControl is Button)) { Draw(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
