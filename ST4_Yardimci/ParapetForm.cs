using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Beykent.Monolit.Girdi;

namespace ST4Yardimci
{
    /// <summary>
    /// Parapet detayı formu. Ortak girdi kuralları (07.11.2026): birimli ölçü kutusu,
    /// tek satır donatı (ø sabit), segment yerleşim/pafta seçimi, alt durum şeridi.
    /// </summary>
    internal sealed class ParapetForm : Beykent.Monolit.MonolitForm
    {
        static ParapetForm _instance;
        /// <summary>Anahtar = ParapetSettings alan adı (yansıma ile okunur/yazılır).</summary>
        readonly Dictionary<string, OlcuKutusu> _numbers = new Dictionary<string, OlcuKutusu>();
        readonly SegmentSecim _konum;
        readonly DonatiKutusu _dikeyCap, _yatayCap;
        readonly SegmentSecim _olcek;
        readonly GirdiDenetci Denetci = new GirdiDenetci();
        readonly ToolTip _tips = new ToolTip { AutoPopDelay = 12000 };
        bool _starting;
        internal static ParapetSettings Current;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("PARAPET")) return;
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new ParapetForm();
                AcApp.ShowModelessDialog(_instance);
            }
            else { _instance.Show(); _instance.BringToFront(); }
            BaFormSession.Attach("PARAPET", _instance);
            BaFormSession.EndPick("PARAPET");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("PARAPET");
            if (_instance != null) { _instance.Close(); _instance = null; }
        }

        ParapetForm()
        {
            Current = ParapetSettings.Load();
            var varsayilan = new ParapetSettings();
            Text = "Parapet Detayı";
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MaximizeBox = MinimizeBox = false;
            KeyPreview = true;

            _konum = new SegmentSecim("Döşeme kenarı", "Döşeme ortası", "Kiriş üzeri") { Secili = (int)Current.Konum };

            // Sınırlar eski NumericUpDown Min/Max değerleriyle aynı; 1 ondalık, 0,5 adım.
            Number("DosemeKalinligi", "Döşeme kalınlığı", 8, 150, varsayilan);
            Number("DosemeUstKotu", "Döşeme üst kotu", -100000, 100000, varsayilan);
            Number("ParapetYuksekligi", "Parapet yüksekliği", 15, 600, varsayilan);
            Number("ParapetKalinligi", "Parapet kalınlığı", 10, 150, varsayilan);
            Number("Sakal", "Sakal", 0, 300, varsayilan);
            Number("Paspayi", "Paspayı", 2, 20, varsayilan);
            Number("KirisGenisligi", "Kiriş genişliği", 10, 300, varsayilan);
            Number("KirisYuksekligi", "Kiriş yüksekliği", 8, 300, varsayilan);
            Number("ToplamUzunluk", "Toplam boy", 0, 10000000, varsayilan, 0, 10);

            // Çap + aralık tek satırda; aralık sınırı eski 5–50 cm, çap ø6–ø32 (standart liste).
            _dikeyCap = Denetci.Ekle(new DonatiKutusu("Düşey donatı", CapYuvarla(varsayilan.DikeyCap), varsayilan.DikeyAralik, 6, 40, 5, 50));
            _yatayCap = Denetci.Ekle(new DonatiKutusu("Yatay donatı", CapYuvarla(varsayilan.YatayCap), varsayilan.YatayAralik, 6, 40, 5, 50));
            _dikeyCap.Ayarla(CapYuvarla(Current.DikeyCap), Current.DikeyAralik);
            _yatayCap.Ayarla(CapYuvarla(Current.YatayCap), Current.YatayAralik);

            _olcek = new SegmentSecim("1/50 pafta", "1/100 pafta (×2)") { Secili = BaPaftaOlcek.Yuz("parapet") ? 1 : 0 };
            _olcek.SeciliDegisti += (s, e) => BaPaftaOlcek.Kaydet("parapet", _olcek.Secili == 1);

            // Fiziksel kontroller ParapetSettings.Validate ile aynı kaynaktan (eski MessageBox yerine şerit).
            Denetci.SegmentIzle(_konum);
            var tum = new List<IGirdi>(_numbers.Values) { _dikeyCap, _yatayCap };
            Denetci.HataKurali(() => ReadSettings().Validate(), tum.ToArray());

            var close = new Button { Text = "Kapat" };
            close.Click += (s, e) => Close();
            var draw = new Button { Text = "Detayı çiz" };
            draw.Click += (s, e) => Draw();
            _konum.SeciliDegisti += (s, e) => EnableBeam();
            EnableBeam();
            FormClosed += (s, e) => { ReadSettings().Save(); _instance = null; BaFormSession.Release("PARAPET"); };

            var ui = BeginCompact(390);
            ui.Section("Yerleşim").Control(_konum);
            ui.Section("Kesit")
                .Field("Döşeme kalınlığı", _numbers["DosemeKalinligi"])
                .Field("Döşeme üst kotu", _numbers["DosemeUstKotu"])
                .Field("Parapet yük. / kalınlık", _numbers["ParapetYuksekligi"], _numbers["ParapetKalinligi"])
                .Field("Sakal / paspayı", _numbers["Sakal"], _numbers["Paspayi"]);
            ui.Section("Kiriş").Field("Genişlik / yükseklik", _numbers["KirisGenisligi"], _numbers["KirisYuksekligi"])
                .When(() => _konum.Secili == 2);
            ui.Section("Donatı  (ø çap / aralık)")
                .Field("Düşey", _dikeyCap)
                .Field("Yatay", _yatayCap);
            ui.Section("Metraj").Field("Toplam boy", _numbers["ToplamUzunluk"])
                .Hint("Toplam boy 0 ise metraj tablosu çizilmez.");
            ui.Section("Pafta ölçeği · detay 1/25").Control(_olcek);
            ui.Section(null).Control(Denetci.Serit, 44);
            ui.Footer(draw, close);
            Tip(_numbers["Sakal"], "Döşeme alt yüzünden aşağı sarkan mesafe. 0: sakal yok. Kiriş üzerinde kullanılamaz.");
            Tip(_numbers["ParapetYuksekligi"], "Döşeme üst yüzünden parapet üstüne kadar ölçülür.");
            Tip(_numbers["DosemeUstKotu"], "Üst kotu cm olarak girin. Örnek: +3.00 m için 300.");
            Tip(_dikeyCap, "Alt ve ikinci gönye en az 12ø; toplam boy 5 cm katına tamamlanır.");
            Tip(_yatayCap, "Sakal sıraları yatay donatı adedine dahildir.");
            Tip(_numbers["ToplamUzunluk"], "Stok boyu 1200 cm, bindirme 60 cm. 0: adet ve metraj tablosu yok.");
            Tip(_numbers["KirisGenisligi"], "Kiriş ve parapetin dış yüzleri hizalıdır. Kiriş üst kotu döşeme üst kotudur.");
            ui.Finish();
            Denetci.Guncelle();
        }

        static int CapYuvarla(double mm) => (int)Math.Round(mm, MidpointRounding.AwayFromZero);

        /// <summary>Araç ipucu kutuya ve içindeki metin kutusuna birlikte bağlanır.</summary>
        void Tip(Control c, string text)
        {
            _tips.SetToolTip(c, text);
            foreach (Control child in c.Controls) _tips.SetToolTip(child, text);
        }

        void Number(string key, string ad, double min, double max, ParapetSettings varsayilan, int decimals = 1, double step = 0.5)
        {
            double def = (double)typeof(ParapetSettings).GetField(key).GetValue(varsayilan);
            var n = Denetci.Ekle(new OlcuKutusu(ad, "cm", min, max, def, decimals, step));
            n.Deger = (double)typeof(ParapetSettings).GetField(key).GetValue(Current);
            _numbers.Add(key, n);
        }

        void EnableBeam()
        {
            bool beam = _konum.Secili == 2;
            _numbers["KirisGenisligi"].Enabled = _numbers["KirisYuksekligi"].Enabled = beam;
            _numbers["Sakal"].Enabled = !beam;
            if (beam) _numbers["Sakal"].Deger = 0;
            if (CompactLayoutReady) RefreshCompactLayout();
        }

        ParapetSettings ReadSettings()
        {
            var s = new ParapetSettings { Konum = (ParapetKonumu)_konum.Secili };
            foreach (var kv in _numbers) typeof(ParapetSettings).GetField(kv.Key).SetValue(s, kv.Value.Deger);
            // Çap ve aralık tek donatı kutusundan (DikeyAralik/YatayAralik alanları dahil).
            s.DikeyCap = _dikeyCap.CapMm;
            s.DikeyAralik = _dikeyCap.AralikCm;
            s.YatayCap = _yatayCap.CapMm;
            s.YatayAralik = _yatayCap.AralikCm;
            s.Normalize();
            return s;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            EnableBeam();
        }

        void Draw()
        {
            if (_starting) return;
            if (!Denetci.Onayla()) return;
            var settings = ReadSettings();
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Current = settings;
            Current.Save();
            _starting = true;
            BaFormSession.BeginPick("PARAPET");
            Hide();
            try { doc.SendStringToExecute("PARAPETCIZ ", true, false, false); }
            catch { _starting = false; Show(); BaFormSession.EndPick("PARAPET"); throw; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if ((keyData == Keys.Enter || keyData == Keys.Return) && !(ActiveControl is Button))
            {
                Draw(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tips.Dispose();
            base.Dispose(disposing);
        }
    }
}
