using System.Drawing;
using System.Windows.Forms;
using Beykent.Monolit.Girdi;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    internal sealed class TemelIlaveDenemeForm : Beykent.Monolit.MonolitForm
    {
        private static decimal _sonGenislik = (decimal)TemelIlaveKutuBol.MinDilimGenislikCm;
        private static decimal _sonPaspay = 5m;
        private static decimal _sonHiza = 100m;
        private readonly OlcuKutusu _genislik;
        private readonly OlcuKutusu _paspay;
        private readonly OlcuKutusu _hiza;
        private readonly SegmentSecim _olcek;
        private readonly GirdiDenetci _denetci = new GirdiDenetci();
        /// <summary>"Akıllı zarf" düğmesiyle kapandı: hizalı kademe adayları denenir, metrajı artırmayan en sadesi çizilir.</summary>
        private bool _akilli;
        /// <summary>TEMELDONATI: formda seçilen plan ölçeği (yalnız ölçek sorulduğunda).</summary>
        internal static bool Olcek100 { get; private set; }

        /// <param name="olcekSor">true: 1/50 – 1/100 seçimi (TEMELDONATI).</param>
        internal static bool TryShow(bool olcekSor = false)
        {
            using (var form = new TemelIlaveDenemeForm(olcekSor))
            {
                if (AcApp.ShowModalDialog(form) != DialogResult.OK) return false;
                if (olcekSor)
                {
                    Olcek100 = form._olcek.Secili == 1;
                    PlanFormSettings.Load("temelilave").Set("Olcek100", Olcek100).Save();
                }
                TemelIlaveDenemeCommands.AkilliZarf = form._akilli;
                TemelIlaveKutuBol.MinDilimGenislikCm = form._genislik.Deger;
                TemelIlaveDenemeCommands.TemelPaspayCm = form._paspay.Deger;
                TemelIlaveDenemeCommands.DuseyHizaFarkCm = form._hiza.Deger;
                _sonGenislik = (decimal)form._genislik.Deger;
                _sonPaspay = (decimal)form._paspay.Deger;
                _sonHiza = (decimal)form._hiza.Deger;
                return true;
            }
        }

        private TemelIlaveDenemeForm(bool olcekSor)
        {
            var saved = PlanFormSettings.Load("temelilave");
            bool sonYuz = saved.Bool("Olcek100", false);
            // Plan ölçeği ayrı çizim yoludur (×2 büyütme değil): etiket düz "1/50 / 1/100".
            _olcek = new SegmentSecim("1/50", "1/100") { Secili = sonYuz ? 1 : 0 };
            _sonGenislik = saved.Decimal("GenislikCm", _sonGenislik, 10m, 1000m);
            _sonPaspay = saved.Decimal("PaspayiCm", _sonPaspay, 0m, 30m);
            _sonHiza = saved.Decimal("HizaFarkiCm", _sonHiza, 0m, 500m);
            Text = "Temel İlave";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(440, 268);

            // Sınırlar ve adımlar eski NumericUpDown ile aynı (10–1000/10, 0–30/0,5, 0–500/10).
            _genislik = _denetci.Ekle(new OlcuKutusu("En az genişlik", "cm", 10, 1000, 120, 0, 10));
            _genislik.Deger = (double)(_sonGenislik < 10 ? 120m : _sonGenislik);
            _paspay = _denetci.Ekle(new OlcuKutusu("Temel paspayı", "cm", 0, 30, 5, 1, 0.5));
            _paspay.Deger = (double)(_sonPaspay < 0 ? 5m : _sonPaspay);
            _hiza = _denetci.Ekle(new OlcuKutusu("Düşey hiza farkı", "cm", 0, 500, 100, 0, 10));
            _hiza.Deger = (double)(_sonHiza < 0 ? 100m : _sonHiza);

            var not = new Label
            {
                Text = "En az genişlik ısıdan taşmaz; aralık katına tamamlanır.\nZarf kenarı temel sınırından bu kadar içeride kalır.\nYakın boy, ısıya girmeden en çok kutuya hizalanır.\nÇakışan zarflar birleşir.",
                Location = new Point(18, 136),
                Size = new Size(404, 74),
                ForeColor = Beykent.Monolit.StandardFormTheme.TextMuted
            };
            var ciz = new Button
            {
                Text = "Çiz",
                Location = new Point(234, 216),
                Size = new Size(90, 30),
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                FlatStyle = FlatStyle.Flat
            };
            var iptal = new Button
            {
                Text = "İptal",
                Location = new Point(332, 216),
                Size = new Size(90, 30),
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.Cancel
            };
            var akilli = new Button
            {
                Text = "Akıllı zarf",
                Size = new Size(110, 30),
                BackColor = Beykent.Monolit.StandardFormTheme.InputBg,
                ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                FlatStyle = FlatStyle.Flat
            };
            new ToolTip().SetToolTip(akilli,
                "İlave aralığı yalnız ana aralık ve 1,5 katı (ör. ana /20 → ilave /20, /30).\n" +
                "Her zarf grubunda en çok 2 çap/aralık; bitişik kutular ortak üst/alt hizada,\n" +
                "aynı kutular tek kutu. Donatı alanı hiçbir yerde düşmez. Kademe adaylarını\n" +
                "çizmeden dener, 12 m kesim planlı sipariş ağırlığı en az olanı çizer.\n" +
                "Adaylar, mevcut zarfın metrajı ve kesim planı komut satırına yazılır.");
            void Onayla(bool akilliZarf)
            {
                if (!_denetci.Onayla()) return;
                PlanFormSettings.Load("temelilave").Set("GenislikCm", (decimal)_genislik.Deger)
                    .Set("PaspayiCm", (decimal)_paspay.Deger).Set("HizaFarkiCm", (decimal)_hiza.Deger).Save();
                _akilli = akilliZarf;
                DialogResult = DialogResult.OK;
                Close();
            }
            ciz.Click += (s, e) => Onayla(false);
            akilli.Click += (s, e) => Onayla(true);
            Controls.AddRange(new Control[] { not, akilli, ciz, iptal });
            AcceptButton = ciz;
            CancelButton = iptal;

            var ui = BeginCompact(350);
            ui.Section("Zarf yerleşimi").Field("En az genişlik", _genislik).Field("Temel paspayı", _paspay).Field("Düşey hiza farkı", _hiza);
            ui.Section(null).Control(not);
            if (olcekSor) ui.Section("Ölçek").Control(_olcek);
            ui.Section(null).Control(_denetci.Serit, 44);
            ui.Footer(ciz, iptal, akilli);
            ui.Finish();
            _denetci.Guncelle();
        }
    }
}
