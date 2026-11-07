using System;
using System.Globalization;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdPoint = System.Drawing.Point;
using Beykent.Monolit.Girdi;

namespace ST4Yardimci
{
    /// <summary>
    /// Kuranglez detayı, ölçek 1/25. Kapak, grobeton, taşma, diş ve paspayı standarttır.
    /// Bindirme 1,5ℓb (TS 500, Konum II); 90° gönye en az 12φ.
    /// </summary>
    internal static class KuranglezSettings
    {
        public const double GrobetonCm = 10;
        /// <summary>Planda kapama perdesinin kuranglez dışına çıkması (cm).</summary>
        public const double KapamaTasmaCm = 20;
        public const double TemelSolCm = 20;
        public const double TemelSagCm = 20;
        public const double DisEnCm = 5;
        public const double DisBoyCm = 5;
        public const double KapakCm = 5;
        public const double TemelPasCm = 5;
        /// <summary>Kapama perde paspayı (standart, cm).</summary>
        public const double KapamaPaspayiCm = 4;
        /// <summary>Kesitte görülen donatı merkezi, yüzden paspayı + bu kadar içeride (cm).</summary>
        public const double KesitMerkezEkCm = 1.5;
        /// <summary>Kapama perdesi, ±0.00 üstüne taşma (cm).</summary>
        public const double KapamaUstCm = 20;

        public static double BoyCm { get; set; } = 270;
        public static double EnCm { get; set; } = 100;
        public static double KapamaCm { get; set; } = 25;
        public static double PerdeCm { get; set; } = 15;
        public static double YukseklikCm { get; set; } = 270;
        public static double TemelCm { get; set; } = 35;
        /// <summary>Temel üst kotu, metre. Örnek −2.70.</summary>
        public static double TemelUstKotM { get; set; } = -2.70;

        public static int DuseyCapMm { get; set; } = 10;
        public static int DuseyAralikCm { get; set; } = 20;
        public static int YatayCapMm { get; set; } = 8;
        public static int YatayAralikCm { get; set; } = 20;
        public static double PaspayiCm { get; set; } = 3;
        /// <summary>Beton sınıfı, fck (MPa). C30 = 30.</summary>
        public static int BetonFck { get; set; } = 30;
        /// <summary>Metraj çarpanı: L boyu olan etiketlere önek (ör. 3x2x14ø…).</summary>
        public static int Adet { get; set; } = 1;
        static bool _loaded;

        public static double BindirmeCm =>
            CeilTo5Cm(Math.Max(1.5 * Ts500LbCm(DuseyCapMm, BetonFck), 30.0));

        /// <summary>90° gönye serbest ucu: 12φ (cm).</summary>
        public static double GonyeCm(int diaMm)
        {
            if (diaMm < 6) diaMm = 8;
            return 12.0 * diaMm / 10.0;
        }

        /// <summary>
        /// İlk gönye duvar içine. Kalınlık − 2×paspayı, 12φ'den kısaysa ilk bacak o kadar kalır,
        /// ikinci gönye duvar boyunca 12φ olur.
        /// </summary>
        public static void GonyeBacak(int diaMm, double wallCm, double pasCm, out double ilk, out double ikinci)
        {
            double need = GonyeCm(diaMm);
            double room = wallCm - 2.0 * pasCm;
            if (room >= need - 0.05)
            {
                ilk = need;
                ikinci = 0;
                return;
            }
            ilk = room > 2.0 ? room : need;
            ikinci = need;
        }

        /// <summary>
        /// TS 500 9.1.2.1 nervürlü düz kenetlenme (Konum II):
        /// ℓb = 0,12·φ·fyd/fctd ≥ 20φ; fyd = fyk/1,15; fctd = 0,35√fck / 1,50.
        /// </summary>
        public static double Ts500LbCm(int diaMm, double fck, double fyk = 420.0)
        {
            if (diaMm < 6) diaMm = 8;
            if (fck < 16.0) fck = 30.0;
            if (fyk < 200.0) fyk = 420.0;
            double fyd = fyk / 1.15;
            double fctd = (0.35 * Math.Sqrt(fck)) / 1.50;
            if (fctd < 0.20) fctd = 0.20;
            double lbMm = 0.12 * diaMm * fyd / fctd;
            double minMm = 20.0 * diaMm;
            if (lbMm < minMm) lbMm = minMm;
            return Math.Max(1.0, Math.Round(lbMm / 10.0));
        }

        public static double CeilTo5Cm(double cm)
        {
            if (cm <= 0) return 5.0;
            return Math.Ceiling(cm / 5.0 - 1e-9) * 5.0;
        }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var m = BaIni.Read("kuranglez.ini");
            BoyCm = BaIni.GetD(m, "BoyCm", BoyCm);
            EnCm = BaIni.GetD(m, "EnCm", EnCm);
            KapamaCm = BaIni.GetD(m, "KapamaCm", KapamaCm);
            PerdeCm = BaIni.GetD(m, "PerdeCm", PerdeCm);
            YukseklikCm = BaIni.GetD(m, "YukseklikCm", YukseklikCm);
            TemelCm = BaIni.GetD(m, "TemelCm", TemelCm);
            TemelUstKotM = BaIni.GetD(m, "TemelUstKotM", TemelUstKotM);
            DuseyCapMm = BaIni.GetI(m, "DuseyCapMm", DuseyCapMm);
            DuseyAralikCm = BaIni.GetI(m, "DuseyAralikCm", DuseyAralikCm);
            YatayCapMm = BaIni.GetI(m, "YatayCapMm", YatayCapMm);
            YatayAralikCm = BaIni.GetI(m, "YatayAralikCm", YatayAralikCm);
            PaspayiCm = BaIni.GetD(m, "PaspayiCm", PaspayiCm);
            BetonFck = BaIni.GetI(m, "BetonFck", BetonFck);
            if (BetonFck < 25) BetonFck = 30;
            Adet = BaIni.GetI(m, "Adet", Adet);
            if (Adet < 1) Adet = 1;
        }

        public static void Save()
        {
            EnsureLoaded();
            BaIni.Write("kuranglez.ini",
                "BoyCm=" + BaIni.N(BoyCm),
                "EnCm=" + BaIni.N(EnCm),
                "KapamaCm=" + BaIni.N(KapamaCm),
                "PerdeCm=" + BaIni.N(PerdeCm),
                "YukseklikCm=" + BaIni.N(YukseklikCm),
                "TemelCm=" + BaIni.N(TemelCm),
                "TemelUstKotM=" + BaIni.N(TemelUstKotM),
                "DuseyCapMm=" + DuseyCapMm.ToString(CultureInfo.InvariantCulture),
                "DuseyAralikCm=" + DuseyAralikCm.ToString(CultureInfo.InvariantCulture),
                "YatayCapMm=" + YatayCapMm.ToString(CultureInfo.InvariantCulture),
                "YatayAralikCm=" + YatayAralikCm.ToString(CultureInfo.InvariantCulture),
                "PaspayiCm=" + BaIni.N(PaspayiCm),
                "BetonFck=" + BetonFck.ToString(CultureInfo.InvariantCulture),
                "Adet=" + Adet.ToString(CultureInfo.InvariantCulture));
        }

        public static string Validate()
        {
            if (BoyCm < 40 || EnCm < 40) return "Açıklık boy/en en az 40 cm olmalı.";
            if (KapamaCm < 12 || PerdeCm < 10) return "Kapama ve perde kalınlığı çok ince.";
            double insetK = (PaspayiCm + KesitMerkezEkCm) * 2.0 + 1.0;
            if (insetK > PerdeCm)
                return "Kuranglez paspayı + 1,5 cm, perde kalınlığına sığmıyor.";
            double insetKap = (KapamaPaspayiCm + KesitMerkezEkCm) * 2.0 + 1.0;
            if (insetKap > KapamaCm)
                return "Kapama paspayı (4 cm) kalınlığa sığmıyor.";
            if (YukseklikCm < BindirmeCm + 20) return "Yükseklik, bindirmeden en az 20 cm büyük olmalı.";
            if (TemelCm < (TemelPasCm + KesitMerkezEkCm) * 2.0 + 2.0)
                return "Temel kalınlığı paspayına sığmıyor.";
            if (DuseyAralikCm < 5 || YatayAralikCm < 5) return "Donatı aralığı en az 5 cm.";
            if (Adet < 1) return "Adet en az 1 olmalı.";
            return null;
        }
    }

    /// <summary>
    /// Kuranglez formu. Ortak girdi kuralları (07.11.2026): birimli ölçü kutusu,
    /// tek satır donatı (ø sabit), segment beton/pafta seçimi, alt durum şeridi.
    /// </summary>
    internal sealed class KuranglezForm : Beykent.Monolit.MonolitForm
    {
        static KuranglezForm _instance;
        bool _starting, _loading;

        static readonly int[] BetonSiniflari = { 25, 30, 35, 40 };

        readonly OlcuKutusu _boy, _en, _kapama, _perde, _yuk, _temel, _kot, _pas, _adet;
        readonly DonatiKutusu _dusey, _yatay;
        readonly SegmentSecim _beton, _olcek;
        readonly GirdiDenetci Denetci = new GirdiDenetci();

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("KURANGLEZ")) return;
            KuranglezSettings.EnsureLoaded();
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new KuranglezForm();
                try { AcApp.ShowModelessDialog(_instance); }
                catch { _instance.Show(); }
            }
            else
            {
                _instance._starting = false;
                _instance.LoadFromSettings();
                _instance.Visible = true;
                _instance.BringToFront();
            }
            BaFormSession.Attach("KURANGLEZ", _instance);
            BaFormSession.EndPick("KURANGLEZ");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("KURANGLEZ");
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
                BaFormSession.BeginPick("KURANGLEZ");
                _instance.Visible = false;
            }
            catch { }
        }

        KuranglezForm()
        {
            KuranglezSettings.EnsureLoaded();
            Text = "Kuranglez Detayı  1/25";
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            KeyPreview = true;

            // Sınırlar eski NumericUpDown Min/Max ile aynı; varsayılanlar KuranglezSettings ilk değerleri.
            _boy = Denetci.Ekle(new OlcuKutusu("Açıklık boy", "cm", 40, 2000, 270) { AdimDegeri = 5 });
            _en = Denetci.Ekle(new OlcuKutusu("Açıklık en", "cm", 40, 2000, 100) { AdimDegeri = 5 });
            _kapama = Denetci.Ekle(new OlcuKutusu("Kapama perde", "cm", 12, 80, 25));
            _perde = Denetci.Ekle(new OlcuKutusu("Kuranglez perde", "cm", 10, 40, 15));
            _yuk = Denetci.Ekle(new OlcuKutusu("Yükseklik", "cm", 40, 800, 270) { AdimDegeri = 5 });
            _temel = Denetci.Ekle(new OlcuKutusu("Temel kalınlığı", "cm", 20, 200, 35) { AdimDegeri = 5 });
            // Kot kutusu cm (Standart A); ayar metre olarak saklanır.
            _kot = Denetci.Ekle(new OlcuKutusu("Temel üst kotu", "cm", -5000, 500, -270) { AdimDegeri = 5 });
            _beton = new SegmentSecim("C25", "C30", "C35", "C40") { Secili = 1 };
            _dusey = Denetci.Ekle(new DonatiKutusu("Düşey donatı", 10, 20, 8, 20, 5, 40));
            _yatay = Denetci.Ekle(new DonatiKutusu("Yatay donatı", 8, 20, 8, 20, 5, 40));
            _pas = Denetci.Ekle(new OlcuKutusu("Kuranglez paspayı", "cm", 2, 8, 3, 1, 0.5));
            _adet = Denetci.Ekle(new OlcuKutusu("Adet", "adet", 1, 999, 1));
            _olcek = new SegmentSecim("1/50 pafta", "1/100 pafta (×2)") { Secili = BaPaftaOlcek.Yuz("kuranglez") ? 1 : 0 };
            _olcek.SeciliDegisti += (s, e) => BaPaftaOlcek.Kaydet("kuranglez", _olcek.Secili == 1);
            Denetci.SegmentIzle(_beton);
            LoadFromSettings();

            // Ayar aralıkları tam sayı cm saklanır (KuranglezSettings int).
            Denetci.HataKurali(() => TamSayiDegil(_dusey.AralikCm) ? "Düşey aralık tam sayı cm olmalı" : null, _dusey);
            Denetci.HataKurali(() => TamSayiDegil(_yatay.AralikCm) ? "Yatay aralık tam sayı cm olmalı" : null, _yatay);
            // Fiziksel kontroller KuranglezSettings.Validate ile aynı kaynaktan (eski MessageBox yerine şerit).
            Denetci.HataKurali(() =>
            {
                if (_loading) return null;
                Push();
                return KuranglezSettings.Validate();
            }, _boy, _en, _kapama, _perde, _yuk, _temel, _pas, _dusey, _yatay, _adet);

            var btn = new Button { Text = "Kuranglez çiz" };
            btn.Click += (s, e) => OnRun();
            var close = new Button { Text = "Kapat" };
            close.Click += (s, e) => Close();

            FormClosing += (s, e) =>
            {
                TryPushQuiet();
                KuranglezSettings.Save();
            };
            FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_instance, this))
                {
                    _instance = null;
                    BaFormSession.Release("KURANGLEZ");
                }
            };
            try
            {
                Location = new SdPoint(Screen.PrimaryScreen.WorkingArea.Left + 80,
                    Screen.PrimaryScreen.WorkingArea.Top + 40);
            }
            catch { StartPosition = FormStartPosition.CenterScreen; }

            var ui = BeginCompact(390);
            ui.Section("Geometri")
                .Field("Açıklık boy / en", _boy, _en)
                .Field("Kapama / perde", _kapama, _perde)
                .Field("Temelden yükseklik", _yuk)
                .Field("Temel kalınlığı", _temel)
                .Field("Temel üst kotu", _kot);
            ui.Section("Donatı  (ø çap / aralık)")
                .Field("Beton sınıfı", _beton)
                .Field("Düşey", _dusey)
                .Field("Yatay", _yatay)
                .Field("Paspayı", _pas);
            ui.Section("Metraj").Field("Adet", _adet);
            ui.Section("Pafta ölçeği · detay 1/25").Control(_olcek);
            ui.Section(null).Control(Denetci.Serit, 44);
            ui.Footer(btn, close);
            ui.Finish();
            Denetci.Guncelle();
        }

        static bool TamSayiDegil(double v) => Math.Abs(v - Math.Round(v)) > 1e-9;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            if ((keyData == Keys.Enter || keyData == Keys.Return) && !(ActiveControl is Button))
            {
                OnRun();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void LoadFromSettings()
        {
            _loading = true;
            try
            {
                _boy.Deger = KuranglezSettings.BoyCm;
                _en.Deger = KuranglezSettings.EnCm;
                _kapama.Deger = KuranglezSettings.KapamaCm;
                _perde.Deger = KuranglezSettings.PerdeCm;
                _yuk.Deger = KuranglezSettings.YukseklikCm;
                _temel.Deger = KuranglezSettings.TemelCm;
                _kot.Deger = Math.Round(KuranglezSettings.TemelUstKotM * 100.0);
                int bi = Array.IndexOf(BetonSiniflari, KuranglezSettings.BetonFck);
                _beton.Secili = bi >= 0 ? bi : 1;
                _dusey.Ayarla(KuranglezSettings.DuseyCapMm, KuranglezSettings.DuseyAralikCm);
                _yatay.Ayarla(KuranglezSettings.YatayCapMm, KuranglezSettings.YatayAralikCm);
                _pas.Deger = KuranglezSettings.PaspayiCm;
                _adet.Deger = KuranglezSettings.Adet;
            }
            finally { _loading = false; }
            Denetci.Guncelle();
        }

        void TryPushQuiet()
        {
            try { Push(); } catch { }
        }

        /// <summary>Runner'ın okuduğu ayar alanları değişmeden doldurulur.</summary>
        void Push()
        {
            KuranglezSettings.BoyCm = _boy.Deger;
            KuranglezSettings.EnCm = _en.Deger;
            KuranglezSettings.KapamaCm = _kapama.Deger;
            KuranglezSettings.PerdeCm = _perde.Deger;
            KuranglezSettings.YukseklikCm = _yuk.Deger;
            KuranglezSettings.TemelCm = _temel.Deger;
            KuranglezSettings.TemelUstKotM = _kot.Deger / 100.0;
            KuranglezSettings.BetonFck = BetonSiniflari[_beton.Secili];
            KuranglezSettings.DuseyCapMm = _dusey.CapMm;
            KuranglezSettings.DuseyAralikCm = (int)Math.Round(_dusey.AralikCm);
            KuranglezSettings.YatayCapMm = _yatay.CapMm;
            KuranglezSettings.YatayAralikCm = (int)Math.Round(_yatay.AralikCm);
            KuranglezSettings.PaspayiCm = _pas.Deger;
            KuranglezSettings.Adet = (int)_adet.Deger;
            if (KuranglezSettings.Adet < 1) KuranglezSettings.Adet = 1;
        }

        void OnRun()
        {
            if (_starting) return;
            if (!Denetci.Onayla()) return;
            Push();
            KuranglezSettings.Save();
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                _starting = true;
                HideTemporarily();
                doc.SendStringToExecute("KURANGLEZCIZ ", true, false, false);
            }
            catch { _starting = false; }
        }
    }
}
