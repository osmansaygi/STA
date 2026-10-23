using System;
using System.Globalization;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdFont = System.Drawing.Font;
using SdPoint = System.Drawing.Point;
using SdSize = System.Drawing.Size;

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

    internal sealed class KuranglezForm : Beykent.Monolit.MonolitForm
    {
        static KuranglezForm _instance;
        bool _starting;

        readonly NumericUpDown _boy, _en, _kapama, _perde, _yuk, _temel, _kot;
        readonly ComboBox _beton, _duseyCap, _yatayCap;
        readonly NumericUpDown _duseyAr, _yatayAr, _pas, _adet;

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
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            Font = new SdFont("Segoe UI", 9f);
            KeyPreview = true;

            const int pad = 12;
            const int colW = 312;
            const int colH = 232;
            var geo = Group("Geometri", pad, pad, colW, colH);
            int y = 24;
            _boy = Row(geo, "Boy (açıklık)", ref y, 40, 2000, 1, 0, (decimal)KuranglezSettings.BoyCm);
            _en = Row(geo, "En (açıklık)", ref y, 40, 2000, 1, 0, (decimal)KuranglezSettings.EnCm);
            _kapama = Row(geo, "Kapama perde", ref y, 12, 80, 1, 0, (decimal)KuranglezSettings.KapamaCm);
            _perde = Row(geo, "Kuranglez perde", ref y, 10, 40, 1, 0, (decimal)KuranglezSettings.PerdeCm);
            _yuk = Row(geo, "Yükseklik (temelden)", ref y, 40, 800, 1, 0, (decimal)KuranglezSettings.YukseklikCm);
            _temel = Row(geo, "Temel kalınlığı", ref y, 20, 200, 1, 0, (decimal)KuranglezSettings.TemelCm);
            _kot = Row(geo, "Temel üst kotu", ref y, -5000, 500, 1, 0,
                (decimal)Math.Round(KuranglezSettings.TemelUstKotM * 100.0), "cm");
            Controls.Add(geo);

            var don = Group("Donatı", pad + colW + 10, pad, colW, colH);
            y = 24;
            _beton = BetonRow(don, ref y, KuranglezSettings.BetonFck);
            _duseyCap = CapRow(don, "Düşey donatı", ref y, KuranglezSettings.DuseyCapMm, out _duseyAr, KuranglezSettings.DuseyAralikCm);
            _yatayCap = CapRow(don, "Yatay donatı", ref y, KuranglezSettings.YatayCapMm, out _yatayAr, KuranglezSettings.YatayAralikCm);
            _pas = Row(don, "Kuranglez paspayı", ref y, 2, 8, 0.5m, 1, (decimal)KuranglezSettings.PaspayiCm);
            _adet = Row(don, "Adet (metraj)", ref y, 1, 999, 1, 0, KuranglezSettings.Adet, "");
            Controls.Add(don);

            int formW = pad * 2 + colW * 2 + 10;
            int btnY = pad + colH + 12;
            var btn = MakeButton("Kuranglez çiz", formW - pad - 220, btnY, 130, true);
            btn.Click += (s, e) => OnRun();
            var close = MakeButton("Kapat", formW - pad - 82, btnY, 82, false);
            close.Click += (s, e) => Close();
            Controls.Add(btn);
            Controls.Add(close);
            ClientSize = new SdSize(formW, btnY + 38);

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

            // Kompakt, içerik odaklı form düzeni.
            var ui = BeginCompact(360);
            ui.Tabs(212,
                ui.Page("Geometri", p => p.Field("Açıklık boy / en · cm", _boy, _en).Field("Kapama / perde · cm", _kapama, _perde)
                    .Field("Temelden yük. · cm", _yuk).Field("Temel kalınlığı · cm", _temel).Field("Temel üst kotu · cm", _kot)),
                ui.Page("Donatı ve metraj", p => p.Field("Beton sınıfı", _beton).Field("Düşey · Ø mm / ara cm", _duseyCap, _duseyAr)
                    .Field("Yatay · Ø mm / ara cm", _yatayCap, _yatayAr).Field("Paspayı · cm", _pas).Field("Adet", _adet)));
            ui.Footer(btn, close);
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
                OnRun();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void LoadFromSettings()
        {
            Set(_boy, KuranglezSettings.BoyCm);
            Set(_en, KuranglezSettings.EnCm);
            Set(_kapama, KuranglezSettings.KapamaCm);
            Set(_perde, KuranglezSettings.PerdeCm);
            Set(_yuk, KuranglezSettings.YukseklikCm);
            Set(_temel, KuranglezSettings.TemelCm);
            Set(_kot, Math.Round(KuranglezSettings.TemelUstKotM * 100.0));
            SelectBeton(_beton, KuranglezSettings.BetonFck);
            SelectCap(_duseyCap, KuranglezSettings.DuseyCapMm);
            SelectCap(_yatayCap, KuranglezSettings.YatayCapMm);
            Set(_duseyAr, KuranglezSettings.DuseyAralikCm);
            Set(_yatayAr, KuranglezSettings.YatayAralikCm);
            Set(_pas, KuranglezSettings.PaspayiCm);
            Set(_adet, KuranglezSettings.Adet);
        }

        bool TryPush()
        {
            Push();
            string err = KuranglezSettings.Validate();
            if (err != null)
            {
                MessageBox.Show(err, "Kuranglez", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        void TryPushQuiet()
        {
            try { Push(); } catch { }
        }

        void Push()
        {
            KuranglezSettings.BoyCm = (double)_boy.Value;
            KuranglezSettings.EnCm = (double)_en.Value;
            KuranglezSettings.KapamaCm = (double)_kapama.Value;
            KuranglezSettings.PerdeCm = (double)_perde.Value;
            KuranglezSettings.YukseklikCm = (double)_yuk.Value;
            KuranglezSettings.TemelCm = (double)_temel.Value;
            KuranglezSettings.TemelUstKotM = (double)_kot.Value / 100.0;
            KuranglezSettings.BetonFck = ReadBeton(_beton);
            KuranglezSettings.DuseyCapMm = ReadCap(_duseyCap, 10);
            KuranglezSettings.DuseyAralikCm = (int)_duseyAr.Value;
            KuranglezSettings.YatayCapMm = ReadCap(_yatayCap, 8);
            KuranglezSettings.YatayAralikCm = (int)_yatayAr.Value;
            KuranglezSettings.PaspayiCm = (double)_pas.Value;
            KuranglezSettings.Adet = (int)_adet.Value;
            if (KuranglezSettings.Adet < 1) KuranglezSettings.Adet = 1;
        }

        void OnRun()
        {
            if (_starting) return;
            if (!TryPush()) return;
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

        static GroupBox Group(string title, int x, int y, int w, int h)
        {
            return new GroupBox
            {
                Text = title,
                Location = new SdPoint(x, y),
                Size = new SdSize(w, h),
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                BackColor = Beykent.Monolit.StandardFormTheme.BgAlt
            };
        }

        NumericUpDown Row(Control host, string label, ref int y, decimal min, decimal max, decimal inc, int dec, decimal val, string unit = "cm")
        {
            host.Controls.Add(Lbl(label, 12, y + 3));
            var n = Num(168, y, 72, min, max, inc, dec, val);
            host.Controls.Add(n);
            if (!string.IsNullOrEmpty(unit))
                host.Controls.Add(Lbl(unit, 246, y + 3));
            y += 28;
            return n;
        }

        ComboBox BetonRow(Control host, ref int y, int fck)
        {
            host.Controls.Add(Lbl("Beton sınıfı", 12, y + 3));
            var c = new ComboBox
            {
                Location = new SdPoint(168, y),
                Size = new SdSize(72, 22),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                FlatStyle = FlatStyle.Flat
            };
            foreach (int f in new[] { 25, 30, 35, 40 })
                c.Items.Add("C" + f.ToString(CultureInfo.InvariantCulture));
            SelectBeton(c, fck);
            host.Controls.Add(c);
            y += 28;
            return c;
        }

        ComboBox CapRow(Control host, string label, ref int y, int cap, out NumericUpDown aralik, int aralikVal)
        {
            host.Controls.Add(Lbl(label, 12, y + 3));
            var c = new ComboBox
            {
                Location = new SdPoint(168, y),
                Size = new SdSize(64, 22),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                FlatStyle = FlatStyle.Flat
            };
            foreach (int d in new[] { 8, 10, 12, 14, 16, 18, 20 })
                c.Items.Add("\u00F8" + d.ToString(CultureInfo.InvariantCulture));
            SelectCap(c, cap);
            host.Controls.Add(c);
            host.Controls.Add(Lbl("/", 236, y + 3));
            aralik = Num(250, y, 52, 5, 40, 1, 0, aralikVal);
            host.Controls.Add(aralik);
            y += 28;
            return c;
        }

        static Label Lbl(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new SdPoint(x, y),
                AutoSize = true,
                ForeColor = Beykent.Monolit.StandardFormTheme.TextMuted,
                BackColor = Beykent.Monolit.StandardFormTheme.BgAlt
            };
        }

        static NumericUpDown Num(int x, int y, int w, decimal min, decimal max, decimal inc, int dec, decimal val)
        {
            var n = new NumericUpDown
            {
                Location = new SdPoint(x, y),
                Size = new SdSize(w, 22),
                Minimum = min,
                Maximum = max,
                Increment = inc,
                DecimalPlaces = dec,
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            n.Value = Clamp(n, val);
            YardimciUi.StripUpDownButtons(n);
            return n;
        }

        static Button MakeButton(string text, int x, int y, int w, bool accent)
        {
            var b = new Button
            {
                Text = text,
                Location = new SdPoint(x, y),
                Size = new SdSize(w, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Beykent.Monolit.StandardFormTheme.BgAlt,
                ForeColor = accent ? Beykent.Monolit.StandardFormTheme.Accent : Beykent.Monolit.StandardFormTheme.Text,
                Font = accent ? new SdFont("Segoe UI", 9f, System.Drawing.FontStyle.Bold) : new SdFont("Segoe UI", 9f),
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = Beykent.Monolit.StandardFormTheme.InputBorder;
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        static void Set(NumericUpDown n, double v) => n.Value = Clamp(n, (decimal)v);

        static decimal Clamp(NumericUpDown n, decimal v)
        {
            if (v < n.Minimum) return n.Minimum;
            if (v > n.Maximum) return n.Maximum;
            return v;
        }

        static void SelectCap(ComboBox c, int mm)
        {
            string t = "\u00F8" + mm.ToString(CultureInfo.InvariantCulture);
            int i = c.Items.IndexOf(t);
            c.SelectedIndex = i >= 0 ? i : 0;
        }

        static void SelectBeton(ComboBox c, int fck)
        {
            string t = "C" + fck.ToString(CultureInfo.InvariantCulture);
            int i = c.Items.IndexOf(t);
            c.SelectedIndex = i >= 0 ? i : 1;
        }

        static int ReadCap(ComboBox c, int fallback)
        {
            string s = c.SelectedItem as string;
            if (string.IsNullOrEmpty(s)) return fallback;
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] >= '0' && s[i] <= '9') n = n * 10 + (s[i] - '0');
            }
            return n > 0 ? n : fallback;
        }

        static int ReadBeton(ComboBox c)
        {
            int n = ReadCap(c, 30);
            if (n < 25) return 30;
            return n;
        }
    }
}
