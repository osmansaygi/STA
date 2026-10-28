using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using BeykentOrtak;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using SdColor = System.Drawing.Color;
using SdFont = System.Drawing.Font;
using SdPoint = System.Drawing.Point;
using SdSize = System.Drawing.Size;

namespace ST4Yardimci
{
    /// <summary>
    /// SEHIMTABLOSU: STA4CAD projesinden seçilen katın kiriş ve döşeme uzun süreli sehim tabloları (mm), Line + DBText.
    /// Düzen Yardımcı DONATIMETRAJ demir tablosu (DEMIR_TABLOSU.dwg) gibi: çerçeveli başlık kutusu, ayrı sütun başlığı kutusu,
    /// 10 cm altta veri ızgarası; çizgiler IC ANTET, başlıklar ANTET BASLIK 2, ad POZ NO, değer DONATI YAZISI, hepsi ortalı.
    /// Birim cm (yazı 10, başlık 15, satır 20).
    /// </summary>
    internal static class SehimTablosuRunner
    {
        internal const string LayerCizgi = "IC ANTET (BEYKENT)";
        internal const string LayerBaslik = "ANTET BASLIK 2 (BEYKENT)";
        internal const string LayerIsim = "POZ NO (BEYKENT)";
        internal const string LayerDeger = "DONATI YAZISI (BEYKENT)";
        private const string IniDosya = "sehimtablosu.ini";

        private const int Cift = 4;              // satırdaki ad | sehim çifti
        private const double SatirH = 20.0;
        private const double NoW = 80.0;      // 4 çift × (80 + 62.5) = 570: DONATIMETRAJ toplam tablosu genişliği
        private const double DegerW = 62.5;
        private const double YaziH = 10.0;
        private const double GenislikFaktoru = 1.0;
        private const double BaslikH = 15.0;
        private const double BaslikKutuH = 25.0;
        private const double KutuBosluk = 10.0;  // başlık kutusu / sütun başlığı / veri arası (demir tablosu)
        private const double TabloBosluk = 15.0; // kiriş ve döşeme tablosu arası

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        public static void Run()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var ini = BaIni.Read(IniDosya);
            string sonProje = BaIni.Get(ini, "Proje", "");
            string sonKat = BaIni.Get(ini, "Kat", "");
            string cizimKlasoru = null;
            try { cizimKlasoru = Path.GetDirectoryName(db.Filename); } catch { }

            SehimProjesi proje;
            SehimKat kat;
            using (var form = new SehimTablosuForm(sonProje, sonKat, cizimKlasoru))
            {
                if (AcApp.ShowModalDialog(form) != DialogResult.OK || form.Proje == null || form.SeciliKat == null) return;
                proje = form.Proje;
                kat = form.SeciliKat;
            }
            BaIni.Write(IniDosya, "Proje=" + proje.St4Yolu, "Kat=" + kat.Kod);
            NoktaSorVeCiz(doc, proje, kat);
        }

        /// <summary>-SEHIMTABLOSU: diyalogsuz (betik / Core Console). ST4 yolu, kat kodu ve köşe noktası sorulur.</summary>
        public static void RunKomutSatiri()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            var ini = BaIni.Read(IniDosya);
            var ps = new PromptStringOptions("\nSEHIMTABLOSU: STA4CAD proje dosyası (.ST4): ") { AllowSpaces = true };
            string son = BaIni.Get(ini, "Proje", "");
            if (!string.IsNullOrEmpty(son)) { ps.DefaultValue = son; ps.UseDefaultValue = true; }
            var pr = ed.GetString(ps);
            if (pr.Status != PromptStatus.OK) return;
            string yol = (pr.StringResult ?? "").Trim().Trim('"');
            if (!File.Exists(yol)) { ed.WriteMessage("\nSEHIMTABLOSU: Dosya yok: {0}", yol); return; }

            SehimProjesi proje;
            try { proje = SehimProjesi.Oku(yol); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ed.WriteMessage("\nSEHIMTABLOSU: Okunamadı: {0}", ex.Message);
                return;
            }
            if (proje.Katlar.Count == 0) { ed.WriteMessage("\nSEHIMTABLOSU: Analiz sonucu (BEA/FLO) yok."); return; }
            ed.WriteMessage("\nSEHIMTABLOSU: Katlar: {0}", string.Join(", ", proje.Katlar.Select(k => k.Kod + " = " + k.Ad)));
            var pk = ed.GetString(new PromptStringOptions("\nSEHIMTABLOSU: Kat kodu: ") { AllowSpaces = false });
            if (pk.Status != PromptStatus.OK) return;
            SehimKat kat = proje.Katlar.FirstOrDefault(k => string.Equals(k.Kod, (pk.StringResult ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            if (kat == null) { ed.WriteMessage("\nSEHIMTABLOSU: Kat bulunamadı: {0}", pk.StringResult); return; }
            BaIni.Write(IniDosya, "Proje=" + proje.St4Yolu, "Kat=" + kat.Kod);
            NoktaSorVeCiz(doc, proje, kat);
        }

        private static void NoktaSorVeCiz(Document doc, SehimProjesi proje, SehimKat kat)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;
            var ppr = ed.GetPoint("\nSEHIMTABLOSU: Tablonun sol üst köşesi: ");
            if (ppr.Status != PromptStatus.OK) return;
            Point3d p0 = ppr.Value.TransformBy(ed.CurrentUserCoordinateSystem);

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                BeykentKatmanYukle.Ensure(tr, db, LayerCizgi, 152, LineWeight.LineWeight030);
                BeykentKatmanYukle.Ensure(tr, db, LayerBaslik, 1, LineWeight.LineWeight020);
                BeykentKatmanYukle.Ensure(tr, db, LayerIsim, 2, LineWeight.LineWeight020);
                BeykentKatmanYukle.Ensure(tr, db, LayerDeger, 3, LineWeight.LineWeight020);
                ObjectId stil = BeykentStilUygula.YaziStili(tr, db, BeykentStilAyar.YaziBeykent, false);
                var c = new Cizici(tr, ms, stil);
                Ciz(c, proje, kat, p0);
                tr.Commit();
            }

            ed.WriteMessage("\nSEHIMTABLOSU: {0} — {1} kiriş, {2} döşeme uzun süreli sehimi çizildi.", kat.Ad, kat.Kirisler.Count, kat.Dosemeler.Count);
        }

        private static void Ciz(Cizici c, SehimProjesi proje, SehimKat kat, Point3d p0)
        {
            double x0 = p0.X, y = p0.Y, z = p0.Z;
            string katAdi = (kat.Ad ?? "").Trim().ToUpper(Tr);
            if (!katAdi.EndsWith("KAT", StringComparison.Ordinal)) katAdi += " KAT";
            double x1 = x0 + Cift * (NoW + DegerW);

            // başlık kutusu (DEMIR TABLOSU gibi)
            c.Kutu(x0, y - BaslikKutuH, x1, y, z);
            c.Yazi((x0 + x1) / 2, y - BaslikKutuH / 2, z, katAdi + " UZUN SÜRELİ TERS SEHİM TABLOSU (mm)", BaslikH,
                TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid, LayerBaslik);
            y -= BaslikKutuH + KutuBosluk;

            // kiriş ve döşeme ayrı tablolar, altlı üstlü, aralarında TabloBosluk
            if (kat.Kirisler.Count > 0) y = Tablo(c, "KİRİŞ", kat.Kirisler, x0, y, z) - TabloBosluk;
            if (kat.Dosemeler.Count > 0) Tablo(c, "DÖŞEME", kat.Dosemeler, x0, y, z);
        }

        /// <summary>
        /// DEMIR TABLOSU düzeni: sütun başlığı kutusu (ANTET BASLIK 2), KutuBosluk altında veri ızgarası; tüm çizgiler IC ANTET,
        /// yazılar ortalı. Satır satır dolan 4 çift `ad | SEHİM`. Dönüş: veri ızgarasının alt kenarı.
        /// </summary>
        private static double Tablo(Cizici c, string baslik, List<SehimSatiri> satirlar, double x0, double yUst, double z)
        {
            double x1 = x0 + Cift * (NoW + DegerW);
            int n = (satirlar.Count + Cift - 1) / Cift;

            // sütun başlığı kutusu
            double hAlt = yUst - SatirH;
            c.Kutu(x0, hAlt, x1, yUst, z);
            for (int j = 0; j < Cift; j++)
            {
                double xa = x0 + j * (NoW + DegerW);
                if (j > 0) c.Cizgi(xa, hAlt, xa, yUst, z, LayerCizgi);
                c.Cizgi(xa + NoW, hAlt, xa + NoW, yUst, z, LayerCizgi);
                c.Yazi(xa + NoW / 2, yUst - SatirH / 2, z, baslik, YaziH, TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid, LayerBaslik);
                c.Yazi(xa + NoW + DegerW / 2, yUst - SatirH / 2, z, "SEHİM", YaziH, TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid, LayerBaslik);
            }

            // veri ızgarası
            double vUst = hAlt - KutuBosluk, alt = vUst - n * SatirH;
            c.Kutu(x0, alt, x1, vUst, z);
            for (int r = 1; r < n; r++)
                c.Cizgi(x0, vUst - r * SatirH, x1, vUst - r * SatirH, z, LayerCizgi);
            for (int j = 0; j < Cift; j++)
            {
                double xa = x0 + j * (NoW + DegerW);
                if (j > 0) c.Cizgi(xa, alt, xa, vUst, z, LayerCizgi);
                c.Cizgi(xa + NoW, alt, xa + NoW, vUst, z, LayerCizgi);
            }
            for (int i = 0; i < satirlar.Count; i++)
            {
                int satir = i / Cift, sutun = i % Cift;   // STA4 çizimi gibi satır satır
                double xa = x0 + sutun * (NoW + DegerW), ym = vUst - satir * SatirH - SatirH / 2;
                c.Yazi(xa + NoW / 2, ym, z, satirlar[i].Ad, YaziH, TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid, LayerIsim);
                c.Yazi(xa + NoW + DegerW / 2, ym, z, Deger(satirlar[i].Mm), YaziH, TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid, LayerDeger);
            }
            return alt;
        }

        /// <summary>mm, 1 ondalık (STA4 çizimi); "-0.0" basılmaz.</summary>
        private static string Deger(double mm)
        {
            string s = mm.ToString("0.0", Inv);
            return s == "-0.0" ? "0.0" : s;
        }

        /// <summary>Demir tablosu düzeninde çizgi/yazı yardımcısı (BETONKALIP da kullanır).</summary>
        internal sealed class Cizici
        {
            private readonly Transaction _tr;
            private readonly BlockTableRecord _ms;
            private readonly ObjectId _stil;
            public Cizici(Transaction tr, BlockTableRecord ms, ObjectId stil) { _tr = tr; _ms = ms; _stil = stil; }

            private void Ekle(Entity e)
            {
                _ms.AppendEntity(e);
                _tr.AddNewlyCreatedDBObject(e, true);
            }

            public void Cizgi(double xa, double ya, double xb, double yb, double z, string katman,
                LineWeight lw = LineWeight.ByLayer)
            {
                Ekle(new Line(new Point3d(xa, ya, z), new Point3d(xb, yb, z)) { Layer = katman, LineWeight = lw });
            }

            /// <summary>IC ANTET dikdörtgeni (dört çizgi, demir tablosundaki gibi).</summary>
            public void Kutu(double xa, double ya, double xb, double yb, double z)
            {
                Cizgi(xa, yb, xb, yb, z, LayerCizgi);
                Cizgi(xa, ya, xb, ya, z, LayerCizgi);
                Cizgi(xa, ya, xa, yb, z, LayerCizgi);
                Cizgi(xb, ya, xb, yb, z, LayerCizgi);
            }

            public void Yazi(double x, double y, double z, string metin, double boy,
                TextHorizontalMode hm, TextVerticalMode vm, string katman)
            {
                var t = new DBText
                {
                    TextString = metin, Height = boy, Layer = katman, HorizontalMode = hm, VerticalMode = vm,
                    Position = new Point3d(x, y, z), WidthFactor = GenislikFaktoru
                };
                if (!_stil.IsNull) t.TextStyleId = _stil;
                if (hm != TextHorizontalMode.TextLeft || vm != TextVerticalMode.TextBase)
                    t.AlignmentPoint = new Point3d(x, y, z);
                Ekle(t);
                t.AdjustAlignment(_ms.Database);
            }
        }
    }

    /// <summary>Proje (.ST4) ve kat seçimi.</summary>
    internal sealed class SehimTablosuForm : Beykent.Monolit.MonolitForm
    {
        private readonly TextBox _txtProje;
        private readonly ListBox _lstKat;
        private readonly Label _lblBilgi;
        private readonly Button _btnCiz;
        private readonly string _cizimKlasoru;
        private readonly string _sonKat;

        public SehimProjesi Proje { get; private set; }
        public SehimKat SeciliKat => _lstKat.SelectedItem as SehimKat;

        public SehimTablosuForm(string sonProje, string sonKat, string cizimKlasoru)
        {
            _cizimKlasoru = cizimKlasoru;
            _sonKat = sonKat;
            Text = "Sehim Tablosu";
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
            y += 34;

            Controls.Add(new Label { Text = "Kat", Location = new SdPoint(10, y), AutoSize = true });
            y += 20;
            _lstKat = new ListBox { Location = new SdPoint(10, y), Size = new SdSize(370, 170), IntegralHeight = false };
            _lstKat.DoubleClick += (s, e) => { if (SeciliKat != null) { DialogResult = DialogResult.OK; Close(); } };
            _lstKat.SelectedIndexChanged += (s, e) => _btnCiz.Enabled = SeciliKat != null;
            Controls.Add(_lstKat);
            y += 176;

            _lblBilgi = new Label { Location = new SdPoint(10, y), Size = new SdSize(370, 34), ForeColor = SdColor.DimGray };
            Controls.Add(_lblBilgi);
            y += 40;

            _btnCiz = new Button { Text = "Tabloyu çiz", Location = new SdPoint(210, y), Size = new SdSize(96, 28), Enabled = false };
            _btnCiz.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            var btnIptal = new Button { Text = "İptal", Location = new SdPoint(312, y), Size = new SdSize(68, 28), DialogResult = DialogResult.Cancel };
            Controls.Add(_btnCiz);
            Controls.Add(btnIptal);
            AcceptButton = _btnCiz;
            CancelButton = btnIptal;
            ClientSize = new SdSize(390, y + 38);

            if (!string.IsNullOrEmpty(sonProje) && File.Exists(sonProje)) Yukle(sonProje);
            else _lblBilgi.Text = "Proje klasöründeki .ST4 dosyasını seçin. Analiz sonuçları (BEA/FLO) aynı klasörde olmalı.";
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
            _lstKat.Items.Clear();
            Proje = null;
            _btnCiz.Enabled = false;
            _txtProje.Text = yol;
            try
            {
                Cursor = Cursors.WaitCursor;
                Proje = SehimProjesi.Oku(yol);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException)
            {
                _lblBilgi.Text = "Okunamadı: " + ex.Message;
                return;
            }
            finally { Cursor = Cursors.Default; }

            if (!Proje.KirisVar && !Proje.DosemeVar)
            {
                _lblBilgi.Text = "Analiz sonucu yok (BEA/FLO). Projeyi STA4CAD'de çözüp tekrar deneyin.";
                return;
            }
            foreach (var k in Proje.Katlar) _lstKat.Items.Add(k);
            int sec = Proje.Katlar.FindIndex(k => string.Equals(k.Kod, _sonKat, StringComparison.OrdinalIgnoreCase));
            if (_lstKat.Items.Count > 0) _lstKat.SelectedIndex = sec >= 0 ? sec : 0;
            _lblBilgi.Text = !Proje.KirisVar ? "Kiriş analiz sonucu (BEA) yok; yalnız döşemeler."
                : !Proje.DosemeVar ? "Döşeme analiz sonucu (FLO) yok; yalnız kirişler."
                : "Uzun süreli sehimler: kiriş BEA, döşeme FLO (STA4 sehim tablosu ile aynı).";
        }
    }
}
