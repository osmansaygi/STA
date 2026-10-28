using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using BeykentOrtak;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SdColor = System.Drawing.Color;
using SdFont = System.Drawing.Font;
using SdPoint = System.Drawing.Point;
using SdSize = System.Drawing.Size;

namespace ST4Yardimci
{
    /// <summary>
    /// BETONKALIP: STA4CAD projesinden TEMEL, SUBASMAN ve kat kat beton (m³, brüt) / kalıp (m²) metraj tablosu.
    /// Düzen SEHIMTABLOSU ile aynı (DONATIMETRAJ demir tablosu): başlık kutusu, sütun başlığı kutusu, veri ızgarası.
    /// Kolonlu subasman (S) katı varsa SUBASMAN grubuna katılır.
    /// </summary>
    internal static class BetonKalipRunner
    {
        private const string IniDosya = "betonkalip.ini";
        private const double SatirH = 20.0, AdW = 290.0, DegerW = 140.0, YaziH = 10.0, BaslikH = 15.0, BaslikKutuH = 25.0;
        // 290 + 2 × 140 = 570: DONATIMETRAJ toplam tablosu genişliği
        private const double KutuBosluk = 10.0;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        /// <summary>Tablo satırı: ad, miktar; Toplam satırı başlık katmanında yazılır.</summary>
        internal sealed class Satir { public string Ad; public BetonKalip Deger; public bool Toplam; }

        public static void Run()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ini = BaIni.Read(IniDosya);
            string klasor = null;
            try { klasor = Path.GetDirectoryName(doc.Database.Filename); } catch { }
            using (var form = new BetonKalipForm(BaIni.Get(ini, "Proje", ""), BaIni.Get(ini, "Kirilim", "1") == "1", klasor))
            {
                if (AcApp.ShowModalDialog(form) != DialogResult.OK || form.Proje == null) return;
                BaIni.Write(IniDosya, "Proje=" + form.Proje.St4Yolu, "Kirilim=" + (form.Kirilimli ? "1" : "0"));
                NoktaSorVeCiz(doc, form.Proje, form.Kirilimli);
            }
        }

        /// <summary>-BETONKALIP: diyalogsuz (betik / Core Console). ST4 yolu, kırılım (E: eleman / K: kat), köşe noktası.</summary>
        public static void RunKomutSatiri()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            var ini = BaIni.Read(IniDosya);
            var ps = new PromptStringOptions("\nBETONKALIP: STA4CAD proje dosyası (.ST4): ") { AllowSpaces = true };
            string son = BaIni.Get(ini, "Proje", "");
            if (!string.IsNullOrEmpty(son)) { ps.DefaultValue = son; ps.UseDefaultValue = true; }
            var pr = ed.GetString(ps);
            if (pr.Status != PromptStatus.OK) return;
            string yol = (pr.StringResult ?? "").Trim().Trim('"');
            if (!File.Exists(yol)) { ed.WriteMessage("\nBETONKALIP: Dosya yok: {0}", yol); return; }
            var pk = new PromptKeywordOptions("\nBETONKALIP: Tablo [Eleman/Kat] <Eleman>: ");
            pk.Keywords.Add("Eleman"); pk.Keywords.Add("Kat"); pk.AllowNone = true;
            var kr = ed.GetKeywords(pk);
            if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
            bool kirilimli = kr.Status == PromptStatus.None || kr.StringResult != "Kat";
            BetonKalipProjesi proje;
            try { proje = BetonKalipProjesi.Oku(yol); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ed.WriteMessage("\nBETONKALIP: Okunamadı: {0}", ex.Message);
                return;
            }
            BaIni.Write(IniDosya, "Proje=" + proje.St4Yolu, "Kirilim=" + (kirilimli ? "1" : "0"));
            NoktaSorVeCiz(doc, proje, kirilimli);
        }

        /// <summary>TEMEL, SUBASMAN ve katlar; kırılımlı ise kalemler + grup toplamı, değilse grup başına tek satır.</summary>
        internal static List<Satir> Satirlar(BetonKalipProjesi p, bool kirilimli)
        {
            var r = new List<Satir>();
            var genel = new BetonKalip();
            void Grup(string ad, IEnumerable<KeyValuePair<string, BetonKalip>> kalemler)
            {
                var l = kalemler.Where(x => x.Value.Beton > 1e-6 || x.Value.Kalip > 1e-6).ToList();
                if (l.Count == 0) return;
                var t = new BetonKalip();
                foreach (var x in l) t.Ekle(x.Value);
                genel.Ekle(t);
                if (kirilimli)
                {
                    foreach (var x in l) r.Add(new Satir { Ad = "   " + x.Key, Deger = x.Value });
                    r.Add(new Satir { Ad = ad + " TOPLAM", Deger = t, Toplam = true });
                }
                else r.Add(new Satir { Ad = ad, Deger = t });
            }
            KeyValuePair<string, BetonKalip> K(string a, BetonKalip b) => new KeyValuePair<string, BetonKalip>(a, b);

            Grup("TEMEL", p.Temel);
            // kolonlu subasman katı (S) subasman grubuna
            var sKat = p.Katlar.Where(k => string.Equals(k.Kod, "S", StringComparison.OrdinalIgnoreCase)).ToList();
            var sub = p.Subasman.ToList();
            foreach (var k in sKat)
            {
                sub.Add(K("Subasman döşemesi", k.Doseme));
                sub.Add(K("Subasman kirişi", k.Kiris));
                sub.Add(K("Subasman kolonu / perdesi", k.Kolon));
            }
            Grup("SUBASMAN", sub);
            foreach (var k in p.Katlar.Except(sKat))
            {
                string ad = (k.Ad ?? "").Trim().ToUpper(Tr);
                if (!ad.EndsWith("KAT", StringComparison.Ordinal)) ad += " KAT";
                Grup(ad, new[] { K("Döşeme", k.Doseme), K("Kiriş", k.Kiris), K("Kolon / perde", k.Kolon) });
            }
            r.Add(new Satir { Ad = "GENEL TOPLAM", Deger = genel, Toplam = true });
            return r;
        }

        private static void NoktaSorVeCiz(Document doc, BetonKalipProjesi proje, bool kirilimli)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;
            var ppr = ed.GetPoint("\nBETONKALIP: Tablonun sol üst köşesi: ");
            if (ppr.Status != PromptStatus.OK) return;
            Point3d p0 = ppr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            var satirlar = Satirlar(proje, kirilimli);

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                BeykentKatmanYukle.Ensure(tr, db, SehimTablosuRunner.LayerCizgi, 152, LineWeight.LineWeight030);
                BeykentKatmanYukle.Ensure(tr, db, SehimTablosuRunner.LayerBaslik, 1, LineWeight.LineWeight020);
                BeykentKatmanYukle.Ensure(tr, db, SehimTablosuRunner.LayerIsim, 2, LineWeight.LineWeight020);
                BeykentKatmanYukle.Ensure(tr, db, SehimTablosuRunner.LayerDeger, 3, LineWeight.LineWeight020);
                ObjectId stil = BeykentStilUygula.YaziStili(tr, db, BeykentStilAyar.YaziBeykent, false);
                Ciz(new SehimTablosuRunner.Cizici(tr, ms, stil), proje, satirlar, p0);
                tr.Commit();
            }
            var g = satirlar[satirlar.Count - 1].Deger;
            ed.WriteMessage("\nBETONKALIP: {0} satır çizildi. Genel toplam: beton {1} m³, kalıp {2} m².",
                satirlar.Count, g.Beton.ToString("0.00", Inv), g.Kalip.ToString("0.00", Inv));
        }

        private static void Ciz(SehimTablosuRunner.Cizici c, BetonKalipProjesi proje, List<Satir> satirlar, Point3d p0)
        {
            double x0 = p0.X, y = p0.Y, z = p0.Z, x1 = x0 + AdW + 2 * DegerW;
            var orta = TextVerticalMode.TextVerticalMid;
            string ad = Path.GetFileNameWithoutExtension(proje.St4Yolu).ToUpper(Tr);
            c.Kutu(x0, y - BaslikKutuH, x1, y, z);
            c.Yazi((x0 + x1) / 2, y - BaslikKutuH / 2, z, ad + " BETON / KALIP METRAJI", BaslikH, TextHorizontalMode.TextCenter, orta, SehimTablosuRunner.LayerBaslik);
            y -= BaslikKutuH + KutuBosluk;

            // sütun başlığı kutusu
            double hAlt = y - SatirH;
            c.Kutu(x0, hAlt, x1, y, z);
            c.Cizgi(x0 + AdW, hAlt, x0 + AdW, y, z, SehimTablosuRunner.LayerCizgi);
            c.Cizgi(x0 + AdW + DegerW, hAlt, x0 + AdW + DegerW, y, z, SehimTablosuRunner.LayerCizgi);
            c.Yazi(x0 + AdW / 2, y - SatirH / 2, z, "KAT / ELEMAN", YaziH, TextHorizontalMode.TextCenter, orta, SehimTablosuRunner.LayerBaslik);
            c.Yazi(x0 + AdW + DegerW / 2, y - SatirH / 2, z, "BETON (m³)", YaziH, TextHorizontalMode.TextCenter, orta, SehimTablosuRunner.LayerBaslik);
            c.Yazi(x0 + AdW + 1.5 * DegerW, y - SatirH / 2, z, "KALIP (m²)", YaziH, TextHorizontalMode.TextCenter, orta, SehimTablosuRunner.LayerBaslik);

            // veri ızgarası
            double vUst = hAlt - KutuBosluk, alt = vUst - satirlar.Count * SatirH;
            c.Kutu(x0, alt, x1, vUst, z);
            c.Cizgi(x0 + AdW, alt, x0 + AdW, vUst, z, SehimTablosuRunner.LayerCizgi);
            c.Cizgi(x0 + AdW + DegerW, alt, x0 + AdW + DegerW, vUst, z, SehimTablosuRunner.LayerCizgi);
            for (int i = 0; i < satirlar.Count; i++)
            {
                double ys = vUst - i * SatirH;
                if (i > 0) c.Cizgi(x0, ys, x1, ys, z, SehimTablosuRunner.LayerCizgi);
                var s = satirlar[i];
                string katman = s.Toplam ? SehimTablosuRunner.LayerBaslik : SehimTablosuRunner.LayerIsim;
                c.Yazi(x0 + 4, ys - SatirH / 2, z, s.Ad, YaziH, TextHorizontalMode.TextLeft, orta, katman);
                c.Yazi(x0 + AdW + DegerW / 2, ys - SatirH / 2, z, s.Deger.Beton.ToString("0.00", Inv), YaziH, TextHorizontalMode.TextCenter, orta, SehimTablosuRunner.LayerDeger);
                c.Yazi(x0 + AdW + 1.5 * DegerW, ys - SatirH / 2, z, s.Deger.Kalip.ToString("0.00", Inv), YaziH, TextHorizontalMode.TextCenter, orta, SehimTablosuRunner.LayerDeger);
            }
        }
    }

    /// <summary>Proje (.ST4) ve tablo kırılımı seçimi.</summary>
    internal sealed class BetonKalipForm : Beykent.Monolit.MonolitForm
    {
        private readonly TextBox _txtProje;
        private readonly RadioButton _rbEleman, _rbKat;
        private readonly Label _lblBilgi;
        private readonly Button _btnCiz;
        private readonly string _cizimKlasoru;

        public BetonKalipProjesi Proje { get; private set; }
        public bool Kirilimli => _rbEleman.Checked;

        public BetonKalipForm(string sonProje, bool kirilimli, string cizimKlasoru)
        {
            _cizimKlasoru = cizimKlasoru;
            Text = "Beton / Kalıp Metrajı";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            Font = new SdFont("Segoe UI", 9f);

            int y = 10;
            Controls.Add(new Label { Text = "STA4CAD projesi (.ST4)", Location = new SdPoint(10, y), AutoSize = true });
            y += 20;
            _txtProje = new TextBox { Location = new SdPoint(10, y), Size = new SdSize(300, 24), ReadOnly = true };
            var btnSec = new Button { Text = "Seç…", Location = new SdPoint(316, y - 1), Size = new SdSize(64, 26) };
            btnSec.Click += (s, e) => ProjeSec();
            Controls.Add(_txtProje);
            Controls.Add(btnSec);
            y += 36;

            Controls.Add(new Label { Text = "Tablo", Location = new SdPoint(10, y), AutoSize = true });
            y += 20;
            _rbEleman = new RadioButton { Text = "Eleman kırılımlı (döşeme / kiriş / kolon ve kat toplamı)", Location = new SdPoint(14, y), AutoSize = true, Checked = kirilimli };
            Controls.Add(_rbEleman);
            y += 24;
            _rbKat = new RadioButton { Text = "Kat başına tek satır", Location = new SdPoint(14, y), AutoSize = true, Checked = !kirilimli };
            Controls.Add(_rbKat);
            y += 32;

            _lblBilgi = new Label { Location = new SdPoint(10, y), Size = new SdSize(370, 50), ForeColor = SdColor.DimGray };
            Controls.Add(_lblBilgi);
            y += 56;

            _btnCiz = new Button { Text = "Tabloyu çiz", Location = new SdPoint(210, y), Size = new SdSize(96, 28), Enabled = false };
            _btnCiz.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            var btnIptal = new Button { Text = "İptal", Location = new SdPoint(312, y), Size = new SdSize(68, 28), DialogResult = DialogResult.Cancel };
            Controls.Add(_btnCiz);
            Controls.Add(btnIptal);
            AcceptButton = _btnCiz;
            CancelButton = btnIptal;
            ClientSize = new SdSize(390, y + 38);

            if (!string.IsNullOrEmpty(sonProje) && File.Exists(sonProje)) Yukle(sonProje);
            else _lblBilgi.Text = "Proje klasöründeki .ST4 dosyasını seçin. Kat elemanları ve temeller ST4 verisinden hesaplanır.";
        }

        private void ProjeSec()
        {
            using (var d = new OpenFileDialog { Title = "STA4CAD projesi", Filter = "STA4CAD proje (*.st4)|*.st4", CheckFileExists = true })
            {
                string klasor = Proje != null ? Path.GetDirectoryName(Proje.St4Yolu) : _cizimKlasoru;
                if (!string.IsNullOrEmpty(klasor) && Directory.Exists(klasor)) d.InitialDirectory = klasor;
                if (d.ShowDialog(this) == DialogResult.OK) Yukle(d.FileName);
            }
        }

        private void Yukle(string yol)
        {
            Proje = null;
            _btnCiz.Enabled = false;
            _txtProje.Text = yol;
            try
            {
                Cursor = Cursors.WaitCursor;
                Proje = BetonKalipProjesi.Oku(yol);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException || ex is ArgumentException)
            {
                _lblBilgi.Text = "Okunamadı: " + ex.Message;
                return;
            }
            finally { Cursor = Cursors.Default; }

            var t = BetonKalipRunner.Satirlar(Proje, false);
            var g = t[t.Count - 1].Deger;
            _lblBilgi.Text = string.Format(CultureInfo.InvariantCulture, "{0} kat, temel kalemi {1}. Genel toplam: beton {2:0.00} m³, kalıp {3:0.00} m².",
                Proje.Katlar.Count, Proje.Temel.Count, g.Beton, g.Kalip);
            _btnCiz.Enabled = Proje.Katlar.Count + Proje.Temel.Count > 0;
        }
    }
}
