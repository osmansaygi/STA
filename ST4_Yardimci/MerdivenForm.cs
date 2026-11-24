using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Beykent.Monolit.Girdi;

namespace ST4Yardimci
{
    /// <summary>Merdiven beton kesiti ayarları (form açıkken runner buradan okur).</summary>
    internal static class MerdivenSettings
    {
        public static double KatYuksekligiCm { get; set; } = 300;
        public static double AltKotCm { get; set; } = 0;
        /// <summary>Aynı katta benzer merdiven adedi. L= etiketinin önüne çarpan.</summary>
        public static int BenzerAdet { get; set; } = 1;
        /// <summary>Benzer kat sayısı (çizilen kat dahil). Ek çarpan ve kot kopyası.</summary>
        public static int BenzerKat { get; set; } = 1;
        /// <summary>Merdiven plak / döşeme kalınlığı (cm) — kesitte eğik alt yüz.</summary>
        public static double DosemeKalinligiCm { get; set; } = 15;
        /// <summary>Plandan okunamayan kiriş yüksekliği varsayılanı (cm).</summary>
        public static double KirisYuksekligiCm { get; set; } = 50;
        /// <summary>Kesit donatı paspayı (cm).</summary>
        public static double PaspayiCm { get; set; } = 3;
        /// <summary>Ana donatı aralığı (cm).</summary>
        public static double AnaDonatiAralikCm { get; set; } = 20;
        /// <summary>Dağılım (tevzi) işaret aralığı (cm).</summary>
        public static double TevziAralikCm { get; set; } = 20;
        /// <summary>Mesnet yüzünden yatay gömülme (cm).</summary>
        public static double GommeCm { get; set; } = 22;
        public static bool TemelVar { get; set; } = true;
        public static double TemelKalinligiCm { get; set; } = 50;
        public static int AnaDonatiCapMm { get; set; } = 12;
        public static int TevziDonatiCapMm { get; set; } = 8;

        /// <summary>Plandan mod basamak genişliği (cm) — hesaplarda kullanılır.</summary>
        public static double BasamakGenisligiCm { get; set; } = 28;
        /// <summary>Rıht yüksekliği = kat / rıht adedi (gerçek değer; yazıda 2 ondalık).</summary>
        public static double RihtYuksekligiCm { get; set; } = 13.636363636363637;
        /// <summary>Son plandan sayılan rıht adedi (uçlar dahil).</summary>
        public static int RihtAdet { get; set; } = 22;
        /// <summary>Plan 1/25 rıht numaralarının başlangıç değeri (varsayılan 1).</summary>
        public static int RihtBaslangicNo { get; set; } = 1;

        static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var m = BaIni.Read("merdiven.ini");
            KatYuksekligiCm = BaIni.GetD(m, "KatYuksekligiCm", KatYuksekligiCm);
            AltKotCm = BaIni.GetD(m, "AltKotCm", AltKotCm);
            BenzerAdet = BaIni.GetI(m, "BenzerAdet", BenzerAdet);
            BenzerKat = BaIni.GetI(m, "BenzerKat", BenzerKat);
            DosemeKalinligiCm = BaIni.GetD(m, "DosemeKalinligiCm", DosemeKalinligiCm);
            KirisYuksekligiCm = BaIni.GetD(m, "KirisYuksekligiCm", KirisYuksekligiCm);
            PaspayiCm = BaIni.GetD(m, "PaspayiCm", PaspayiCm);
            AnaDonatiAralikCm = BaIni.GetD(m, "AnaDonatiAralikCm", AnaDonatiAralikCm);
            TevziAralikCm = BaIni.GetD(m, "TevziAralikCm", TevziAralikCm);
            GommeCm = BaIni.GetD(m, "GommeCm", GommeCm);
            TemelVar = BaIni.GetB(m, "TemelVar", TemelVar);
            TemelKalinligiCm = BaIni.GetD(m, "TemelKalinligiCm", TemelKalinligiCm);
            AnaDonatiCapMm = BaIni.GetI(m, "AnaDonatiCapMm", AnaDonatiCapMm);
            TevziDonatiCapMm = BaIni.GetI(m, "TevziDonatiCapMm", TevziDonatiCapMm);
            BasamakGenisligiCm = BaIni.GetD(m, "BasamakGenisligiCm", BasamakGenisligiCm);
            RihtYuksekligiCm = BaIni.GetD(m, "RihtYuksekligiCm", RihtYuksekligiCm);
            RihtAdet = BaIni.GetI(m, "RihtAdet", RihtAdet);
            RihtBaslangicNo = BaIni.GetI(m, "RihtBaslangicNo", RihtBaslangicNo);
        }

        public static void Save()
        {
            EnsureLoaded();
            BaIni.Write("merdiven.ini",
                "KatYuksekligiCm=" + BaIni.N(KatYuksekligiCm),
                "AltKotCm=" + BaIni.N(AltKotCm),
                "BenzerAdet=" + BenzerAdet.ToString(CultureInfo.InvariantCulture),
                "BenzerKat=" + BenzerKat.ToString(CultureInfo.InvariantCulture),
                "DosemeKalinligiCm=" + BaIni.N(DosemeKalinligiCm),
                "KirisYuksekligiCm=" + BaIni.N(KirisYuksekligiCm),
                "PaspayiCm=" + BaIni.N(PaspayiCm),
                "AnaDonatiAralikCm=" + BaIni.N(AnaDonatiAralikCm),
                "TevziAralikCm=" + BaIni.N(TevziAralikCm),
                "GommeCm=" + BaIni.N(GommeCm),
                "TemelVar=" + BaIni.B(TemelVar),
                "TemelKalinligiCm=" + BaIni.N(TemelKalinligiCm),
                "AnaDonatiCapMm=" + AnaDonatiCapMm.ToString(CultureInfo.InvariantCulture),
                "TevziDonatiCapMm=" + TevziDonatiCapMm.ToString(CultureInfo.InvariantCulture),
                "BasamakGenisligiCm=" + BaIni.N(BasamakGenisligiCm),
                "RihtYuksekligiCm=" + BaIni.N(RihtYuksekligiCm),
                "RihtAdet=" + RihtAdet.ToString(CultureInfo.InvariantCulture),
                "RihtBaslangicNo=" + RihtBaslangicNo.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Merdiven formu — MERDIVEN modal açar; Plandan çiz → form kapanır, komut çizime devam eder.
    /// Çizim bitince form açılmaz; Enter/Space MERDIVEN tekrarlar → form yeniden açılır.
    /// Ortak girdi kuralları (07.11.2026): birimli ölçü kutusu, tek satır donatı, segment pafta, durum şeridi.
    /// </summary>
    internal sealed class MerdivenForm : Beykent.Monolit.MonolitForm
    {
        private static MerdivenForm _instance;

        private readonly OlcuKutusu _numKat;
        private readonly OlcuKutusu _numAltKot;
        private readonly OlcuKutusu _numAdet;
        private readonly OlcuKutusu _numBenzerKat;
        private readonly OlcuKutusu _numDoseme;
        private readonly OlcuKutusu _numKirisH;
        private readonly OlcuKutusu _numPaspayi;
        private readonly OlcuKutusu _numRihtBas;
        private readonly CheckBox _chkTemel;
        private readonly OlcuKutusu _numTemelKal;
        private readonly DonatiKutusu _ana;
        private readonly DonatiKutusu _tevzi;
        private readonly SegmentSecim _olcek;
        private readonly GirdiDenetci Denetci = new GirdiDenetci();

        /// <summary>Modal form. true = «Plandan çiz» (ayarlar kaydedildi).</summary>
        public static bool ShowModalAndAcceptDraw()
        {
            if (!BaFormSession.TryEnter("MERDIVEN")) return false;
            Shutdown();
            using (var f = new MerdivenForm())
            {
                BaFormSession.Attach("MERDIVEN", f);
                DialogResult dr;
                try { dr = AcApp.ShowModalDialog(f); }
                catch { dr = f.ShowDialog(); }
                BaFormSession.Release("MERDIVEN");
                return dr == DialogResult.OK;
            }
        }

        public static void Shutdown()
        {
            BaFormSession.Release("MERDIVEN");
            if (_instance == null) return;
            try { _instance.Close(); } catch { }
            _instance = null;
        }

        private MerdivenForm()
        {
            MerdivenSettings.EnsureLoaded();
            Text = "Merdiven";
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            KeyPreview = true;
            DialogResult = DialogResult.Cancel;

            // Sınırlar eski NumericUpDown Min/Max ile aynı; varsayılanlar MerdivenSettings ilk değerleri.
            _numKat = Denetci.Ekle(new OlcuKutusu("Kat yüksekliği", "cm", 50, 600, 300) { AdimDegeri = 5 });
            _numAltKot = Denetci.Ekle(new OlcuKutusu("Alt kot", "cm", -5000, 5000, 0) { AdimDegeri = 5 });
            _numAdet = Denetci.Ekle(new OlcuKutusu("Adet", "ad", 1, 50, 1));
            _numBenzerKat = Denetci.Ekle(new OlcuKutusu("Benzer kat", "kat", 1, 40, 1));
            _numDoseme = Denetci.Ekle(new OlcuKutusu("Döşeme kalınlığı", "cm", 8, 40, 15));
            _numKirisH = Denetci.Ekle(new OlcuKutusu("Kiriş yüksekliği", "cm", 20, 150, 50) { AdimDegeri = 5 });
            _numRihtBas = Denetci.Ekle(new OlcuKutusu("Rıht başlangıç no", "no", 1, 999, 1));
            _chkTemel = new CheckBox { Text = "Temel var", AutoSize = true, Checked = MerdivenSettings.TemelVar };
            _numTemelKal = Denetci.Ekle(new OlcuKutusu("Temel kalınlığı", "cm", 10, 200, 50) { AdimDegeri = 5 });
            // Çap listesi eskiden ø8–ø25, aralık 8–40 cm.
            _ana = Denetci.Ekle(new DonatiKutusu("Ana donatı", 12, 20, 8, 25, 8, 40));
            _tevzi = Denetci.Ekle(new DonatiKutusu("Tevzi donatı", 8, 20, 8, 25, 8, 40));
            _numPaspayi = Denetci.Ekle(new OlcuKutusu("Paspayı", "cm", 1, 5, 3, 1, 0.5));
            _olcek = new SegmentSecim("1/50 pafta", "1/100 pafta (×2)") { Secili = BaPaftaOlcek.Yuz("merdiven") ? 1 : 0 };
            _olcek.SeciliDegisti += (s, e) => BaPaftaOlcek.Kaydet("merdiven", _olcek.Secili == 1);
            LoadFromSettings();

            var btnPlan = new Button { Text = "Plandan çiz" };
            btnPlan.Click += (s, e) => OnPlan();
            var btnClose = new Button { Text = "Kapat" };
            btnClose.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            CancelButton = btnClose;

            FormClosing += (s, e) => { try { PushSettings(); } catch { } };
            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                    _instance = null;
            };

            try
            {
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 80,
                    Screen.PrimaryScreen.WorkingArea.Top + 60);
            }
            catch
            {
                StartPosition = FormStartPosition.CenterScreen;
            }

            var ui = BeginCompact(390);
            ui.Section("Geometri")
                .Field("Kat yüksekliği", _numKat)
                .Field("Alt kot", _numAltKot)
                .Field("Döşeme / kiriş*", _numDoseme, _numKirisH)
                .Field("Adet / benzer kat", _numAdet, _numBenzerKat)
                .Field("Rıht başlangıç no", _numRihtBas)
                .Options(_chkTemel)
                .FieldWhen(() => _chkTemel.Checked, new Label { Text = "Temel kalınlığı" }, _numTemelKal);
            ui.Section("Donatı  (ø çap / aralık)")
                .Field("Ana", _ana)
                .Field("Tevzi", _tevzi)
                .Field("Paspayı", _numPaspayi)
                .Hint("Seç → numara + S kesit (+donatı). *kiriş h.: plandan okunamazsa.");
            ui.Section("Pafta ölçeği · detay 1/25").Control(_olcek);
            ui.Section(null).Control(Denetci.Serit, 44);
            ui.Footer(btnPlan, btnClose);
            ui.Finish();
            Denetci.Guncelle();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            if ((keyData == Keys.Enter || keyData == Keys.Return) && !(ActiveControl is Button))
            {
                OnPlan();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void LoadFromSettings()
        {
            _numKat.Deger = MerdivenSettings.KatYuksekligiCm;
            _numAltKot.Deger = MerdivenSettings.AltKotCm;
            _numAdet.Deger = MerdivenSettings.BenzerAdet;
            _numBenzerKat.Deger = MerdivenSettings.BenzerKat;
            _numDoseme.Deger = MerdivenSettings.DosemeKalinligiCm;
            _numKirisH.Deger = MerdivenSettings.KirisYuksekligiCm;
            _numTemelKal.Deger = MerdivenSettings.TemelKalinligiCm;
            _ana.Ayarla(MerdivenSettings.AnaDonatiCapMm, MerdivenSettings.AnaDonatiAralikCm);
            _tevzi.Ayarla(MerdivenSettings.TevziDonatiCapMm, MerdivenSettings.TevziAralikCm);
            _numPaspayi.Deger = MerdivenSettings.PaspayiCm;
            _numRihtBas.Deger = MerdivenSettings.RihtBaslangicNo;
        }

        private void OnPlan()
        {
            if (!Denetci.Onayla()) return;
            PushSettings();
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Runner'ın okuduğu ayar alanları değişmeden doldurulur.</summary>
        private void PushSettings()
        {
            MerdivenSettings.KatYuksekligiCm = _numKat.Deger;
            MerdivenSettings.AltKotCm = _numAltKot.Deger;
            MerdivenSettings.BenzerAdet = (int)_numAdet.Deger;
            MerdivenSettings.BenzerKat = (int)_numBenzerKat.Deger;
            MerdivenSettings.DosemeKalinligiCm = _numDoseme.Deger;
            MerdivenSettings.KirisYuksekligiCm = _numKirisH.Deger;
            MerdivenSettings.TemelVar = _chkTemel.Checked;
            MerdivenSettings.TemelKalinligiCm = _numTemelKal.Deger;
            MerdivenSettings.AnaDonatiCapMm = _ana.CapMm;
            MerdivenSettings.TevziDonatiCapMm = _tevzi.CapMm;
            MerdivenSettings.AnaDonatiAralikCm = _ana.AralikCm;
            MerdivenSettings.TevziAralikCm = _tevzi.AralikCm;
            MerdivenSettings.PaspayiCm = _numPaspayi.Deger;
            MerdivenSettings.RihtBaslangicNo = Math.Max(1, (int)Math.Round(_numRihtBas.Deger));
            MerdivenSettings.Save();
        }
    }
}
