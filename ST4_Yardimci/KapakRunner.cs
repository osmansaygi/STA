using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.PlottingServices;
using BeykentOrtak;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace ST4Yardimci
{
    /// <summary>Statik proje kapağına yazılan bilgiler.</summary>
    internal sealed class KapakVeri
    {
        public string Baslik = "STATİK PROJE";
        // Proje alanları varsayılan boş: mimari kapaktan okunamayan alan formda boş görünür (kullanıcı isteği 24.11.2026).
        public string Nitelik = "YENİ YAPI", Blok = ""; // mimari kapakta yoksa varsayılan
        public string Mahalle = "", Pap = "", Yuzolcumu = "", Ihtiva = "", Aciklama = "";
        public string Cinsi = "", Kat = "", InsaatAlani = "", Kullanim = "", M2 = "", Toplam = "", Sinif = "";
        /// <summary>Tadilat konuları, satır başına bir madde; kapağın altındaki boşluğa numaralı yazılır.</summary>
        public string Tadilat = "";

        public KapakVeri Kopya() => (KapakVeri)MemberwiseClone();
        public string Sahip = "";
        public string MuellifAd = "", MuellifUnvan = "İNŞAAT MÜHENDİSİ", MuellifSicil = "", MuellifBuro = "";
        public string FenniAd = "", FenniUnvan = "İNŞAAT MÜHENDİSİ", FenniSicil = "";
        public string DenetimFirma = "", DenetimBelge = "";
        /// <summary>Pafta ölçeği paydası (50 → çizim birimi cm, 1 mm kâğıt = 5 birim).</summary>
        public int Olcek = 50;
    }

    /// <summary>
    /// KAPAK: BEYKENT PROJE statik proje kapağı (A4). Selçuklu Belediyesi şablonundaki e-imza kutuları (3 adet) ve
    /// Belediye Onay alanı aynı konum ve ölçüde korunur; üst bölüm büroya göre yeniden düzenlendi. Bilgiler mimari projenin
    /// kapağından okunur (<see cref="MimariKapakOku"/>), form üzerinde düzeltilebilir. Ölçüler kâğıt mm'sidir; çizim birimi
    /// pafta ölçeğine göre büyütülür (1/50: 1 mm = 5 birim, şablonla aynı). Logo vektör dolgudur (dış dosya gerekmez).
    /// </summary>
    internal static partial class KapakRunner
    {
        internal const string LayerCizgi = "KAPAK (BEYKENT)";
        internal const string LayerYazi = "KAPAK YAZI (BEYKENT)";
        internal const string LayerDolgu = "KAPAK DOLGU (BEYKENT)";
        internal const string LayerLogo = "KAPAK LOGO (BEYKENT)";
        internal const string LayerSinir = "KAPAK SINIR (BEYKENT)";
        private const string StilNormal = "KAPAK (BEYKENT)";
        private const string StilKalin = "KAPAK KALIN (BEYKENT)";
        private const string IniDosya = "kapak.ini";
        /// <summary>Logo bloğu adı. Logo verisi değişirse ad sürümlenir (eski çizimlerdeki blok ezilmez).</summary>
        internal const string LogoBlokAdi = "BEYKENT PROJE LOGO";

        /// <summary>Arial büyük harf yüksekliği / punto oranı: onaylı tasarım punto (em) ölçüsüyle hazırlandı.</summary>
        private const double BuyukHarf = 0.716;

        internal static readonly string[] Basliklar =
        {
            "STATİK PROJE", "STATİK HESAP RAPORLARI", "İSKELE PROJESİ", "KALIP İSKELE PROJESİ", "İSKELE HESAP RAPORLARI", "KALIP İSKELE HESAP RAPORLARI"
        };
        /// <summary>-KAPAK anahtar sözcükleri (<see cref="Basliklar"/> ile aynı sıra).</summary>
        private static readonly string[] BaslikAnahtar = { "Proje", "Rapor", "Iskele", "KalipIskele", "IskeleRapor", "KalipIskeleRapor" };
        internal static readonly int[] Olcekler = { 50, 100, 25, 20 };

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        // Renk düzeni "B — Arduvaz mavi" (kullanıcı seçimi 24.11.2026, output/beykent-kapak-claude/renk-secenekleri.html):
        // değerler ACI 7 (ekranda beyaz, kâğıtta siyah); etiket, harf şeridi, başlıklar ve çizgiler yumuşak gri-mavi.
        private static readonly AcColor Beyaz = AcColor.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7);
        /// <summary>Etiketler (CİNSİ, MAHALLE …) ve ARSA/BİNA harfleri: açık mavi #A9C4E0.</summary>
        private static readonly AcColor Gri = AcColor.FromRgb(169, 196, 224);
        /// <summary>İmza kutusu ve belediye onay başlıkları: #C3D4E6.</summary>
        private static readonly AcColor Koyu = AcColor.FromRgb(195, 212, 230);
        /// <summary>Kapak başlığı (STATİK PROJE …): #D6E6F7.</summary>
        private static readonly AcColor BaslikRenk = AcColor.FromRgb(214, 230, 247);
        /// <summary>Kutu çizgileri (KAPAK katmanı rengi): #7F93AB.</summary>
        private static readonly AcColor CizgiRenk = AcColor.FromRgb(127, 147, 171);
        /// <summary>Tablo içi ince satır çizgileri: #56677B.</summary>
        private static readonly AcColor InceCizgi = AcColor.FromRgb(86, 103, 123);
        // Etiket dolgusu yalnız ekranda: koyu arduvaz #34404E, %45 saydam, katman yazdırılmaz.
        private static readonly AcColor DolguRenk = AcColor.FromRgb(52, 64, 78);
        private const byte DolguAlfa = 140;
        // PDF (kâğıt) baskı renkleri: çizgi/başlık ACI 7 (siyah), etiketler koyu arduvaz, ince çizgiler gri.
        private static readonly AcColor BaskiEtiket = AcColor.FromRgb(52, 64, 78);
        private static readonly AcColor BaskiInce = AcColor.FromRgb(138, 148, 158);
        private static readonly AcColor LogoYesil = AcColor.FromRgb(96, 182, 68);
        private static readonly AcColor LogoMavi = AcColor.FromRgb(38, 110, 190);

        // ------------------------------------------------------------------ komutlar
        public static void Run()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            KapakVeri veri = SonVeri(out string sonDwg);
            string cizimKlasoru = null;
            try { cizimKlasoru = Path.GetDirectoryName(doc.Database.Filename); } catch (ArgumentException) { }
            // Bu çizim için daha önce girilen form değerleri (mimari proje yeniden okunmadıkça formda kırmızı görünür)
            string projeAnahtari = ProjeAnahtari(doc);
            bool hatirlandi = ProjeOku(projeAnahtari, veri, out string projeDwg);
            if (hatirlandi) sonDwg = projeDwg;
            string pdfKlasoru = null;
            using (var form = new KapakForm(veri, sonDwg, cizimKlasoru, BaIni.Get(BaIni.Read(IniDosya), "PdfKlasor", ""), hatirlandi))
            {
                DialogResult sonuc = AcApp.ShowModalDialog(form);
                form.Topla(); // vazgeçilse de değiştirilen alanlar bu çizim için saklanır
                veri = form.Veri;
                ProjeKaydet(projeAnahtari, veri, form.MimariDwg);
                if (sonuc != DialogResult.OK && sonuc != DialogResult.Yes) return;
                Kaydet(veri, form.MimariDwg, sonuc == DialogResult.Yes ? form.PdfKlasoru : null);
                if (sonuc == DialogResult.Yes) pdfKlasoru = form.PdfKlasoru;
            }
            if (pdfKlasoru != null) RaporPdfleri(doc, veri, pdfKlasoru);
            else NoktaSorVeCiz(doc, veri);
        }

        /// <summary>-KAPAKPDF: diyalogsuz rapor kapağı PDF'leri (betik / Core Console). Mimari DWG, kapak sırası, klasör sorulur.</summary>
        public static void RunPdfKomutSatiri()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            KapakVeri veri = SonVeri(out _);
            PromptResult pr = ed.GetString(new PromptStringOptions("\nKAPAKPDF: Mimari proje DWG yolu (boş = okumadan): ") { AllowSpaces = true });
            if (pr.Status != PromptStatus.OK) return;
            string yol = (pr.StringResult ?? "").Trim().Trim('"');
            if (yol.Length > 0)
            {
                List<MimariKapak> kapaklar;
                try { kapaklar = MimariKapakOku.Oku(yol); }
                catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Autodesk.AutoCAD.Runtime.Exception)
                {
                    ed.WriteMessage("\nKAPAKPDF: Mimari proje okunamadı: {0}", ex.Message);
                    return;
                }
                var pi = new PromptIntegerOptions("\nKAPAKPDF: Kapak sırası: ") { LowerLimit = 1, UpperLimit = Math.Max(1, kapaklar.Count), DefaultValue = 1, UseDefaultValue = true };
                PromptIntegerResult ri = ed.GetInteger(pi);
                if (ri.Status != PromptStatus.OK) return;
                if (kapaklar.Count > 0) Uygula(veri, kapaklar[ri.Value - 1], yol);
            }
            PromptResult rk = ed.GetString(new PromptStringOptions("\nKAPAKPDF: PDF klasörü: ") { AllowSpaces = true });
            if (rk.Status != PromptStatus.OK) return;
            RaporPdfleri(doc, veri, (rk.StringResult ?? "").Trim().Trim('"'));
        }

        /// <summary>PDF'i kaydedilen rapor kapakları (her biri ayrı A4 PDF).</summary>
        internal static readonly string[] RaporBasliklari = { "STATİK HESAP RAPORLARI", "KALIP İSKELE HESAP RAPORLARI", "İSKELE HESAP RAPORLARI" };

        /// <summary>
        /// Rapor kapaklarını ayrı A4 PDF'ler olarak kaydeder. Kapak doğrudan PDF dosyasına yazılır (<see cref="PdfYaz"/>):
        /// açık çizime hiçbir şey eklenmez. Yazı tipi okunamazsa yedek yol: kapak çizimin boş bir yerine geçici çizilir,
        /// "DWG To PDF" ile basılır ve silinir.
        /// </summary>
        internal static void RaporPdfleri(Document doc, KapakVeri veri, string klasor)
        {
            Editor ed = doc.Editor;
            if (string.IsNullOrWhiteSpace(klasor)) { ed.WriteMessage("\nKAPAK PDF: Klasör seçilmedi."); return; }
            try { Directory.CreateDirectory(klasor); }
            catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                ed.WriteMessage("\nKAPAK PDF: Klasör kullanılamıyor: {0}", ex.Message);
                return;
            }
            if (string.IsNullOrWhiteSpace(veri.Toplam)) veri.Toplam = ToplamHesapla(veri.InsaatAlani, veri.M2);

            var yazilan = new List<string>();
            var yedek = new List<KeyValuePair<string, string>>(); // başlık → yol (çizerek basılacak)
            foreach (string baslik in RaporBasliklari)
            {
                string ad = "KAPAK" + (Bos(veri.Blok) ? "" : " - " + MimariKapakOku.BlokAdiSade(veri.Blok) + " BLOK") + " - " + baslik + ".pdf";
                string yol = Path.Combine(klasor, DosyaAdi(ad));
                var v = (KapakVeri)veri.Kopya();
                v.Baslik = baslik;
                try
                {
                    PdfYaz(v, yol);
                    yazilan.Add(yol);
                }
                catch (IOException ex) when (!(ex is FileNotFoundException))
                { ed.WriteMessage("\nKAPAK PDF: {0} yazılamadı (dosya açık olabilir): {1}", Path.GetFileName(yol), ex.Message); }
                catch (UnauthorizedAccessException ex) { ed.WriteMessage("\nKAPAK PDF: {0} yazılamadı: {1}", Path.GetFileName(yol), ex.Message); }
                catch (System.Exception ex) when (ex is FileNotFoundException || ex is InvalidDataException || ex is IndexOutOfRangeException || ex is ArgumentException)
                {
                    ed.WriteMessage("\nKAPAK PDF: Doğrudan PDF yazılamadı ({0}); çizim üzerinden basılacak.", ex.Message);
                    yedek.Add(new KeyValuePair<string, string>(baslik, yol));
                }
            }
            if (yedek.Count > 0) yazilan.AddRange(CizerekBas(doc, veri, yedek));
            ed.WriteMessage("\nKAPAK PDF: {0} rapor kapağı kaydedildi: {1}", yazilan.Count, klasor);
            foreach (string y in yazilan) ed.WriteMessage("\n   {0}", Path.GetFileName(y));
        }

        /// <summary>Yedek yol: kapak çizimin boş bir yerine geçici çizilir (baskı renkleri), "DWG To PDF" ile basılır, sonra silinir.</summary>
        private static List<string> CizerekBas(Document doc, KapakVeri veri, List<KeyValuePair<string, string>> isler)
        {
            Editor ed = doc.Editor;
            var yazilan = new List<string>();
            if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
            {
                ed.WriteMessage("\nKAPAK PDF: Başka bir yazdırma sürüyor; bitince tekrar deneyin.");
                return yazilan;
            }
            Database db = doc.Database;
            const double s = 5.0; // 1/50: 1 mm = 5 birim
            Point3d p0;
            {
                Point3d emax = db.Extmax, emin = db.Extmin;
                bool gecerli = emax.X > emin.X && Math.Abs(emax.X) < 1e15;
                p0 = new Point3d((gecerli ? emax.X : 0) + 300 * s, gecerli ? emin.Y : 0, 0);
            }
            object eskiArka = AcApp.GetSystemVariable("BACKGROUNDPLOT");
            try
            {
                AcApp.SetSystemVariable("BACKGROUNDPLOT", (short)0);
                foreach (var isi in isler)
                {
                    string yol = isi.Value;
                    var ids = new List<ObjectId>();
                    try
                    {
                        using (doc.LockDocument())
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            KatmanlariHazirla(tr, db);
                            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                            var c = new Cizici(tr, ms, p0, s, YaziStili(tr, db, StilNormal, false), YaziStili(tr, db, StilKalin, true)) { Baski = true };
                            var v = (KapakVeri)veri.Kopya();
                            v.Baslik = isi.Key;
                            Ciz(c, v);
                            c.DolgulariArkayaAl();
                            ids.AddRange(c.Olusan);
                            tr.Commit();
                        }
                        if (File.Exists(yol)) File.Delete(yol);
                        PdfBas(doc, new Extents2d(p0.X, p0.Y, p0.X + 210 * s, p0.Y + 297 * s), s, yol);
                        if (File.Exists(yol)) yazilan.Add(yol);
                        else ed.WriteMessage("\nKAPAK PDF: Oluşmadı: {0}", yol);
                    }
                    catch (IOException ex) { ed.WriteMessage("\nKAPAK PDF: {0} yazılamadı (dosya açık olabilir): {1}", Path.GetFileName(yol), ex.Message); }
                    catch (UnauthorizedAccessException ex) { ed.WriteMessage("\nKAPAK PDF: {0} yazılamadı: {1}", Path.GetFileName(yol), ex.Message); }
                    finally { Sil(doc, ids); }
                }
            }
            finally
            {
                try { AcApp.SetSystemVariable("BACKGROUNDPLOT", eskiArka); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
            }
            return yazilan;
        }

        private static string DosyaAdi(string ad)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) ad = ad.Replace(c, '_');
            return ad;
        }

        private static void Sil(Document doc, List<ObjectId> ids)
        {
            if (ids.Count == 0) return;
            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                    if (!id.IsNull && !id.IsErased && tr.GetObject(id, OpenMode.ForWrite, true) is Entity e && !e.IsErased) e.Erase();
                tr.Commit();
            }
        }

        /// <summary>Model alanı penceresini A4 dikey (tam kenar) PDF'e 1 mm = <paramref name="s"/> birim ölçekle basar.</summary>
        private static void PdfBas(Document doc, Extents2d pencere, double s, string pdfYolu)
        {
            Database db = doc.Database;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var msr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                var lo = (Layout)tr.GetObject(msr.LayoutId, OpenMode.ForRead);
                var ps = new PlotSettings(lo.ModelType);
                ps.CopyFrom(lo);
                PlotSettingsValidator v = PlotSettingsValidator.Current;
                v.SetPlotConfigurationName(ps, "DWG To PDF.pc3", "ISO_full_bleed_A4_(210.00_x_297.00_MM)");
                v.SetPlotPaperUnits(ps, PlotPaperUnit.Millimeters);
                v.SetPlotRotation(ps, PlotRotation.Degrees000);
                v.SetPlotWindowArea(ps, pencere);
                v.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
                v.SetUseStandardScale(ps, false);
                v.SetCustomPrintScale(ps, new CustomScale(1, s));
                v.SetPlotCentered(ps, true);
                try { v.SetCurrentStyleSheet(ps, "acad.ctb"); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                ps.PrintLineweights = true;
                var pi = new PlotInfo { Layout = msr.LayoutId, OverrideSettings = ps };
                var piv = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
                piv.Validate(pi);
                using (PlotEngine pe = PlotFactory.CreatePublishEngine())
                {
                    pe.BeginPlot(null, null);
                    pe.BeginDocument(pi, doc.Name, null, 1, true, pdfYolu);
                    pe.BeginPage(new PlotPageInfo(), pi, true, null);
                    pe.BeginGenerateGraphics(null);
                    pe.EndGenerateGraphics(null);
                    pe.EndPage(null);
                    pe.EndDocument(null);
                    pe.EndPlot(null);
                }
                tr.Commit();
            }
        }

        /// <summary>-KAPAK: diyalogsuz (betik / Core Console). Mimari DWG yolu, kapak sırası, başlık ve köşe noktası sorulur.</summary>
        public static void RunKomutSatiri()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            KapakVeri veri = SonVeri(out string sonDwg);

            var ps = new PromptStringOptions("\nKAPAK: Mimari proje DWG yolu (boş = okumadan çiz): ") { AllowSpaces = true };
            PromptResult pr = ed.GetString(ps);
            if (pr.Status != PromptStatus.OK) return;
            string yol = (pr.StringResult ?? "").Trim().Trim('"');
            if (yol.Length > 0)
            {
                if (!File.Exists(yol)) { ed.WriteMessage("\nKAPAK: Dosya yok: {0}", yol); return; }
                List<MimariKapak> kapaklar;
                try { kapaklar = MimariKapakOku.Oku(yol); }
                catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Autodesk.AutoCAD.Runtime.Exception)
                {
                    ed.WriteMessage("\nKAPAK: Mimari proje okunamadı: {0}", ex.Message);
                    return;
                }
                for (int i = 0; i < kapaklar.Count; i++)
                {
                    ed.WriteMessage("\nKAPAK: [{0}] {1}", i + 1, kapaklar[i].Gorunen);
                    foreach (var kv in kapaklar[i].Alan.OrderBy(k => k.Key, StringComparer.Ordinal))
                        ed.WriteMessage("\n         {0} = {1}", kv.Key, kv.Value);
                }
                int sec = 1;
                if (kapaklar.Count > 0)
                {
                    // Tek kapakta da sorulur: betikler kapak sayısını bilmeden aynı girdi sırasını kullanabilsin.
                    var pi = new PromptIntegerOptions("\nKAPAK: Kapak sırası: ") { LowerLimit = 1, UpperLimit = kapaklar.Count, DefaultValue = 1, UseDefaultValue = true };
                    PromptIntegerResult ri = ed.GetInteger(pi);
                    if (ri.Status != PromptStatus.OK) return;
                    sec = ri.Value;
                }
                if (kapaklar.Count > 0 && Uygula(veri, kapaklar[sec - 1], yol)) ed.WriteMessage("\nKAPAK: Proje sahibi dosya adından alındı: {0}", veri.Sahip);
            }
            var pb = new PromptKeywordOptions("\nKAPAK: Başlık") { AllowNone = true };
            foreach (string k in BaslikAnahtar) pb.Keywords.Add(k);
            pb.Keywords.Default = BaslikAnahtar[0];
            PromptResult rb = ed.GetKeywords(pb);
            if (rb.Status != PromptStatus.OK && rb.Status != PromptStatus.None) return;
            int bi = Array.IndexOf(BaslikAnahtar, rb.StringResult);
            veri.Baslik = Basliklar[bi >= 0 ? bi : 0];
            PromptResult rt = ed.GetString(new PromptStringOptions("\nKAPAK: Tadilat konuları (| ile ayırın, boş = yok): ") { AllowSpaces = true });
            if (rt.Status != PromptStatus.OK) return;
            veri.Tadilat = string.Join("\n", (rt.StringResult ?? "").Split('|'));
            NoktaSorVeCiz(doc, veri);
        }

        private static void KatmanlariHazirla(Transaction tr, Database db)
        {
            BeykentKatmanYukle.Ensure(tr, db, LayerCizgi, 7, LineWeight.LineWeight030);
            BeykentKatmanYukle.Ensure(tr, db, LayerYazi, 7, LineWeight.LineWeight020);
            BeykentStilUygula.KatmanHazirla(tr, db, new KatmanAyar { Ad = LayerDolgu, Aci = 8, Rgb = "52,64,78", Kalinlik = 0, CizgiTipi = "Continuous", Saydamlik = 45, Cizdir = false });
            DolguKatmaniYazdirma(tr, db);
            BeykentKatmanYukle.Ensure(tr, db, LayerLogo, 7, LineWeight.LineWeight000);
            BeykentStilUygula.KatmanHazirla(tr, db, new KatmanAyar { Ad = LayerSinir, Aci = 8, Kalinlik = 0, CizgiTipi = "Continuous", Cizdir = false });
        }

        private static void NoktaSorVeCiz(Document doc, KapakVeri veri)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;
            PromptPointResult ppr = ed.GetPoint("\nKAPAK: Kapağın sol alt köşesi: ");
            if (ppr.Status != PromptStatus.OK) return;
            Point3d p0 = ppr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            if (string.IsNullOrWhiteSpace(veri.Toplam)) veri.Toplam = ToplamHesapla(veri.InsaatAlani, veri.M2);

            int adet;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                KatmanlariHazirla(tr, db);
                var c = new Cizici(tr, ms, p0, Math.Max(1, veri.Olcek) / 10.0,
                    YaziStili(tr, db, StilNormal, false), YaziStili(tr, db, StilKalin, true));
                Ciz(c, veri);
                c.DolgulariArkayaAl();
                adet = c.Adet;
                tr.Commit();
            }
            ed.WriteMessage("\nKAPAK: {0} kapağı çizildi ({1} nesne, 1/{2}).", veri.Baslik, adet, veri.Olcek);
        }

        /// <summary>
        /// KAPAK DOLGU ve KAPAK (çizgi) yalnız KAPAK'ın katmanlarıdır: önceki sürümlerin oluşturduğu katmanlar da güncel
        /// renk düzenine çekilir (dolgu yazdırılmaz ve saydam, çizgi katmanı arduvaz mavi).
        /// </summary>
        private static void DolguKatmaniYazdirma(Transaction tr, Database db)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(LayerDolgu))
            {
                var ltr = (LayerTableRecord)tr.GetObject(lt[LayerDolgu], OpenMode.ForWrite);
                ltr.IsPlottable = false;
                ltr.Color = DolguRenk;
                ltr.Transparency = new Autodesk.AutoCAD.Colors.Transparency(DolguAlfa);
            }
            if (lt.Has(LayerCizgi))
            {
                var ltr = (LayerTableRecord)tr.GetObject(lt[LayerCizgi], OpenMode.ForWrite);
                ltr.Color = CizgiRenk;
            }
        }

        private static ObjectId YaziStili(Transaction tr, Database db, string ad, bool kalin)
        {
            var tablo = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (tablo.Has(ad)) return tablo[ad];
            var rec = new TextStyleTableRecord { Name = ad, Font = new FontDescriptor("Arial", kalin, false, 0, 0), TextSize = 0, XScale = 1 };
            tablo.UpgradeOpen();
            ObjectId id = tablo.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return id;
        }

        // ------------------------------------------------------------------ çizim (ölçüler kâğıt mm, y yukarıdan aşağı)
        private static void Ciz(IKapakCizici c, KapakVeri v)
        {
            c.Sinir(0, 0, 210, 297);
            c.Kutu(3, 3, 204, 291);

            // başlık
            c.Kutu(7, 7, 196, 30);
            c.Yazi(105, 25.5, 10, v.Baslik, 1, true, BaslikRenk, 186);
            c.PdfSus(7, 37.6, 196, 0.8, true); // PDF: başlık altı yeşil çizgi

            // niteliği / blok adı (yan yana kutular 2 mm, alt alta bloklar 6 mm aralıklı)
            UstHucre(c, 7, "NİTELİĞİ", v.Nitelik);
            UstHucre(c, 106, "BLOK ADI", v.Blok);

            // arsa / bina
            var arsa = new[]
            {
                new[] { "MAHALLE", v.Mahalle }, new[] { "PAFTA / ADA / PARSEL", v.Pap },
                new[] { "YÜZÖLÇÜMÜ", Birimli(v.Yuzolcumu, "m²") }, new[] { "İHTİVA ETTİĞİ YER", v.Ihtiva },
            };
            var bina = new[]
            {
                new[] { "CİNSİ", v.Cinsi }, new[] { "KAT ADEDİ", v.Kat }, new[] { "BİNA İNŞAAT ALANI", Birimli(v.InsaatAlani, "m²") },
                new[] { "KULLANIM AMACI", v.Kullanim }, new[] { "m² MALİYETİ", Parali(v.M2) }, new[] { "TOPLAM MALİYETİ", Parali(v.Toplam) },
                new[] { "MİM.HİZ.SIN.ORANI", v.Sinif },
            };
            Tablo(c, 7, "ARSA", arsa, 7);
            Tablo(c, 106, "BİNA", bina, 8);
            c.Yazi(17.5, 91.6, 3.2, "AÇIKLAMA", 0, true, Gri, 0);
            if (!string.IsNullOrWhiteSpace(v.Aciklama)) c.Yazi(17.5, 97.6, 3.2, v.Aciklama.Trim(), 0, true, Beyaz, 84);

            // logo ve proje sahibi
            c.Logo(105, 135, 140);
            c.Yazi(105, 159, 3.2, "PROJE SAHİBİ", 1, true, Gri, 0);
            c.Yazi(105, 167.5, 5.2, (v.Sahip ?? "").Trim(), 1, true, Beyaz, 190);

            // --- sabit alan: e-imza kutuları ve belediye onayı (Selçuklu şablonu; konum ve ölçü değişmez) ---
            var imza = new[]
            {
                new Imza(7, 64.4, new[] { "İNŞAAT MÜHENDİSİ", "PROJE MÜELLİFİ" },
                    Satirlar(v.MuellifAd, v.MuellifUnvan, Etiketli("Oda Sicil No", v.MuellifSicil), Etiketli("Büro Tescil No", v.MuellifBuro))),
                new Imza(73.4, 63.2, new[] { "İNŞ.MÜH. PROJE VE UYGULAMA", "DENETÇİSİ / İNŞ.MÜH. FENNİ MESUL" },
                    Satirlar(v.FenniAd, Bos(v.FenniAd) ? "" : v.FenniUnvan, Etiketli("Oda Sicil No", v.FenniSicil))),
                new Imza(138.6, 64.4, new[] { "YAPI DENETİM FİRMA YETKİLİSİ" },
                    Satirlar(v.DenetimFirma, Etiketli("Yapı Denetim İzin Belge No", v.DenetimBelge))),
            };
            foreach (Imza k in imza)
            {
                c.Dolgu(k.X, 180, k.W, 12);
                c.Kutu(k.X, 180, k.W, 42);
                c.Cizgi(k.X, 192, k.X + k.W, 192, false);
                double xm = k.X + k.W / 2;
                if (k.Baslik.Length == 2)
                {
                    c.Yazi(xm, 185.3, 2.5, k.Baslik[0], 1, false, Koyu, k.W - 3);
                    c.Yazi(xm, 189.3, 2.5, k.Baslik[1], 1, false, Koyu, k.W - 3);
                }
                else c.Yazi(xm, 187.2, 2.5, k.Baslik[0], 1, false, Koyu, k.W - 3);
                for (int i = 0; i < k.Icerik.Count; i++)
                    c.Yazi(xm, 197.4 + i * 3.9 + (i > 0 ? 0.4 : 0), i == 0 ? 3.3 : 2.6, k.Icerik[i], 1, i == 0, Beyaz, k.W - 4);
            }
            c.Dolgu(7, 228, 196, 7);
            c.Kutu(7, 228, 196, 37);
            c.Cizgi(7, 235, 203, 235, false);
            c.Yazi(105, 233, 2.8, "BELEDİYE ONAY", 1, false, Koyu, 0);

            TadilatYaz(c, v.Tadilat);
        }

        /// <summary>Tadilat maddeleri (satır başına bir); baştaki numara/çizgi atılıp yeniden numaralanır.</summary>
        internal static List<string> TadilatMaddeleri(string metin) =>
            (metin ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Regex.Replace(s.Trim(), @"^(\d+\s*[.)\-]|[-–•*])\s*", "").Trim())
                .Where(s => !Bos(s)).ToList();

        /// <summary>
        /// Sayfa altı (belediye onayının 6 mm altı, y 271–291 mm): "TADİLAT KONULARI" başlığı ve numaralı maddeler.
        /// 3 maddeye kadar tek, fazlasında iki sütun; satırlar sığmazsa aralık ve yazı boyu birlikte küçülür.
        /// </summary>
        private static void TadilatYaz(IKapakCizici c, string metin)
        {
            List<string> m = TadilatMaddeleri(metin);
            if (m.Count == 0) return;
            c.Yazi(7, 275, 3.2, "TADİLAT KONULARI", 0, true, Gri, 0);
            int sutun = m.Count <= 3 ? 1 : 2;
            int satir = (m.Count + sutun - 1) / sutun;
            const double ilk = 280.5, son = 291;
            double adim = satir > 1 ? Math.Min(5, (son - ilk) / (satir - 1)) : 5;
            double boy = Math.Min(3.2, adim * 0.66);
            double genislik = 196.0 / sutun;
            for (int i = 0; i < m.Count; i++)
            {
                int s = i / satir, r = i % satir;
                c.Yazi(7 + s * genislik, ilk + r * adim, boy, (i + 1).ToString(Inv) + ". " + m[i], 0, false, Beyaz, genislik - 3);
            }
        }

        private sealed class Imza
        {
            public readonly double X, W; public readonly string[] Baslik; public readonly List<string> Icerik;
            public Imza(double x, double w, string[] baslik, List<string> icerik) { X = x; W = w; Baslik = baslik; Icerik = icerik; }
        }

        /// <summary>Boş veya yalnız işaret ("-", "–", ".") olan değer yazılmaz.</summary>
        private static bool Bos(string x) => !Regex.IsMatch(x ?? "", @"[\p{L}\p{N}]");

        private static List<string> Satirlar(params string[] s) => s.Where(x => !Bos(x)).Select(x => x.Trim()).ToList();

        private static string Etiketli(string etiket, string deger) => Bos(deger) ? "" : etiket + ": " + deger.Trim();

        /// <summary>Üst satır kutusu: gri etiket hücresi (40 mm) + değer hücresi (57 mm), y = 43–51.</summary>
        private static void UstHucre(IKapakCizici c, double x, string etiket, string deger)
        {
            c.Dolgu(x, 43, 40, 8);
            c.Kutu(x, 43, 97, 8);
            c.Cizgi(x + 40, 43, x + 40, 51, false);
            c.Yazi(x + 20, 48.6, 3.6, etiket, 1, true, Gri, 37);
            c.Yazi(x + 40 + 28.5, 48.4, 3.6, (deger ?? "").Trim(), 1, true, Beyaz, 54);
        }

        /// <summary>ARSA / BİNA kutusu (97×56 mm, y = 57–113): dik harf şeridi + etiket/değer satırları.</summary>
        private static void Tablo(IKapakCizici c, double x, string harfler, string[][] satirlar, double satirH)
        {
            const double y = 57, h = 56, serit = 9, etiketW = 38;
            c.Dolgu(x, y, serit, h);
            c.PdfSus(x + serit, y, etiketW, satirlar.Length * satirH, false); // PDF: etiket sütunu zemini
            c.Kutu(x, y, 97, h);
            c.Cizgi(x + serit, y, x + serit, y + h, false);
            for (int i = 0; i < harfler.Length; i++)
                c.Yazi(x + serit / 2, 75 + i * 6.5, 4.6, harfler[i].ToString(), 1, true, Gri, 0);
            double tx = x + serit, tw = 97 - serit;
            for (int i = 0; i < satirlar.Length; i++)
            {
                double yy = y + i * satirH;
                c.Yazi(tx + 1.5, yy + satirH / 2 + 0.85, 3.2, satirlar[i][0], 0, true, Gri, etiketW - 2.5);
                c.Yazi(tx + etiketW + 1.5, yy + satirH / 2 + 0.85, 3.2, (satirlar[i][1] ?? "").Trim(), 0, true, Beyaz, tw - etiketW - 3);
                if (yy + satirH < y + h - 0.01) c.Cizgi(tx, yy + satirH, tx + tw, yy + satirH, true);
            }
            c.Cizgi(tx + etiketW, y, tx + etiketW, y + satirlar.Length * satirH, true);
        }

        /// <summary>Kâğıt mm ölçülerini çizim birimine çevirerek çizer.</summary>
        private sealed class Cizici : IKapakCizici
        {
            private readonly Transaction _tr;
            private readonly BlockTableRecord _ms;
            private readonly Point3d _p0;
            private readonly double _s;
            private readonly ObjectId _stil, _stilKalin;
            private readonly ObjectIdCollection _dolgular = new ObjectIdCollection();
            /// <summary>PDF baskısı: koyu yazı/çizgi renkleri (kâğıtta okunur), logo renkli kalır.</summary>
            public bool Baski;
            /// <summary>Bu çizicinin model alanına eklediği nesneler (geçici PDF kapağını silmek için).</summary>
            public readonly List<ObjectId> Olusan = new List<ObjectId>();

            private AcColor Renk(AcColor c) => Baski ? BaskiRengi(c) : c;
            public int Adet { get; private set; }

            public Cizici(Transaction tr, BlockTableRecord ms, Point3d p0, double s, ObjectId stil, ObjectId stilKalin)
            { _tr = tr; _ms = ms; _p0 = p0; _s = s; _stil = stil; _stilKalin = stilKalin; }

            private double X(double mm) => _p0.X + mm * _s;
            private double Y(double mm) => _p0.Y + (297 - mm) * _s;
            private Point3d P(double x, double y) => new Point3d(X(x), Y(y), _p0.Z);

            private void Ekle(Entity e)
            {
                _ms.AppendEntity(e);
                _tr.AddNewlyCreatedDBObject(e, true);
                Adet++;
                if (e.OwnerId == _ms.ObjectId) Olusan.Add(e.ObjectId);
            }

            private AcPolyline Dikdortgen(double x, double y, double w, double h)
            {
                var pl = new AcPolyline(4) { Elevation = _p0.Z, Closed = true };
                pl.AddVertexAt(0, new Point2d(X(x), Y(y)), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(X(x + w), Y(y)), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(X(x + w), Y(y + h)), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(X(x), Y(y + h)), 0, 0, 0);
                return pl;
            }

            public void Kutu(double x, double y, double w, double h)
            {
                AcPolyline pl = Dikdortgen(x, y, w, h);
                pl.Layer = LayerCizgi;
                if (Baski) pl.Color = Beyaz;
                Ekle(pl);
            }

            /// <summary>A4 kâğıt kenarı (yazdırılmayan katman): pencereyle yazdırmada köşe noktaları.</summary>
            public void PdfSus(double x, double y, double w, double h, bool vurgu) { } // yalnız PDF tasarımında

            public void Sinir(double x, double y, double w, double h)
            {
                AcPolyline pl = Dikdortgen(x, y, w, h);
                pl.Layer = LayerSinir;
                Ekle(pl);
            }

            public void Cizgi(double xa, double ya, double xb, double yb, bool ince)
            {
                var l = new Line(P(xa, ya), P(xb, yb)) { Layer = LayerCizgi };
                if (ince) { l.Color = Renk(InceCizgi); l.LineWeight = LineWeight.LineWeight020; }
                else if (Baski) l.Color = Beyaz;
                Ekle(l);
            }

            /// <summary>Açık gri etiket dolgusu (SOLID tarama; sınır çizgisi bırakılmaz).</summary>
            public void Dolgu(double x, double y, double w, double h)
            {
                // Geçici sınır: transaction'a kayıtlı (ömrünü transaction yönetir), tarama sonrası silinir.
                AcPolyline pl = Dikdortgen(x, y, w, h);
                _ms.AppendEntity(pl);
                _tr.AddNewlyCreatedDBObject(pl, true);
                Tarama(new[] { pl.ObjectId }, LayerDolgu, DolguRenk, DolguAlfa);
                pl.Erase();
            }

            private void Tarama(IEnumerable<ObjectId> halkalar, string katman, AcColor renk, byte alfa = 255)
            {
                var h = new Hatch();
                Ekle(h);
                h.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                h.Layer = katman;
                h.Color = renk;
                if (alfa < 255) h.Transparency = new Autodesk.AutoCAD.Colors.Transparency(alfa);
                h.Normal = Vector3d.ZAxis;
                h.Elevation = _p0.Z;
                h.Associative = false;
                h.HatchStyle = HatchStyle.Normal;
                foreach (ObjectId id in halkalar) h.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { id });
                h.EvaluateHatch(true);
                _dolgular.Add(h.ObjectId);
            }

            /// <summary>Vektör logo: merkez (cx, cy), genişlik w mm. Yeşil ve mavi parçalar ayrı dolgu.</summary>
            /// <summary>
            /// Logo bloğu (<see cref="LogoBlokAdi"/>): merkez (cx, cy), genişlik w mm. Blok bir kez tanımlanır (yükseklik 1 birim,
            /// sol alt köşe taban noktası); yeşil ve mavi parçalar blok içinde ayrı SOLID tarama, katman 0, sabit renk.
            /// </summary>
            public void Logo(double cx, double cy, double w)
            {
                double birim = w / KapakLogoVeri.En; // logo yüksekliği, mm
                ObjectId blok = LogoBlogu();
                var br = new BlockReference(P(cx - w / 2, cy + birim / 2), blok)
                {
                    Layer = LayerLogo,
                    ScaleFactors = new Scale3d(birim * _s)
                };
                Ekle(br);
            }

            private ObjectId LogoBlogu()
            {
                Database db = _ms.Database;
                var bt = (BlockTable)_tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                if (bt.Has(LogoBlokAdi)) return bt[LogoBlokAdi];
                var btr = new BlockTableRecord { Name = LogoBlokAdi, Origin = Point3d.Origin, Units = UnitsValue.Undefined };
                bt.UpgradeOpen();
                ObjectId id = bt.Add(btr);
                _tr.AddNewlyCreatedDBObject(btr, true);
                foreach (var parca in new[] { new { Veri = KapakLogoVeri.Yesil, Renk = LogoYesil }, new { Veri = KapakLogoVeri.Mavi, Renk = LogoMavi } })
                {
                    var h = new Hatch();
                    btr.AppendEntity(h);
                    _tr.AddNewlyCreatedDBObject(h, true);
                    h.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                    h.Layer = "0";
                    h.Color = parca.Renk;
                    h.Normal = Vector3d.ZAxis;
                    h.Associative = false;
                    h.HatchStyle = HatchStyle.Normal;
                    foreach (double[] halka in parca.Veri)
                    {
                        var pts = new Point2dCollection();
                        var bulge = new DoubleCollection();
                        for (int i = 0; i + 1 < halka.Length; i += 2) { pts.Add(new Point2d(halka[i], halka[i + 1])); bulge.Add(0); }
                        pts.Add(new Point2d(halka[0], halka[1])); bulge.Add(0);
                        h.AppendLoop(HatchLoopTypes.Polyline, pts, bulge);
                    }
                    h.EvaluateHatch(true);
                }
                return id;
            }

            /// <summary>
            /// Yazı: (x, y) taban çizgisi noktası; boy punto (mm); hiza 0 sol, 1 orta, 2 sağ. <paramref name="enFazla"/> &gt; 0 ise
            /// yazı bu genişliğe (mm) sığacak şekilde önce daraltılır (en çok 0,6), yetmezse küçültülür.
            /// </summary>
            public void Yazi(double x, double y, double punto, string metin, int hiza, bool kalin, AcColor renk, double enFazla)
            {
                if (string.IsNullOrWhiteSpace(metin)) return;
                var p = P(x, y);
                var t = new DBText
                {
                    TextString = metin, Height = punto * BuyukHarf * _s, Layer = LayerYazi, Color = Renk(renk), WidthFactor = 1,
                    Position = p, TextStyleId = kalin ? _stilKalin : _stil
                };
                if (hiza != 0)
                {
                    t.HorizontalMode = hiza == 1 ? TextHorizontalMode.TextCenter : TextHorizontalMode.TextRight;
                    t.VerticalMode = TextVerticalMode.TextBase;
                    t.AlignmentPoint = p;
                }
                Ekle(t);
                if (hiza != 0) t.AdjustAlignment(_ms.Database);
                if (enFazla <= 0) return;
                double sinir = enFazla * _s, gen = Genislik(t);
                if (gen <= sinir || gen <= 0) return;
                t.WidthFactor = Math.Max(0.6, sinir / gen);
                if (hiza != 0) t.AdjustAlignment(_ms.Database);
                gen = Genislik(t);
                if (gen > sinir && gen > 0)
                {
                    t.Height *= sinir / gen;
                    if (hiza != 0) t.AdjustAlignment(_ms.Database);
                }
            }

            private static double Genislik(DBText t)
            {
                try { Extents3d e = t.GeometricExtents; return e.MaxPoint.X - e.MinPoint.X; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { return 0; }
            }

            public void DolgulariArkayaAl()
            {
                if (_dolgular.Count == 0) return;
                var sira = (DrawOrderTable)_tr.GetObject(_ms.DrawOrderTableId, OpenMode.ForWrite);
                sira.MoveToBottom(_dolgular);
            }
        }

        // ------------------------------------------------------------------ değer biçimleri
        /// <summary>"1190,36" → "1190,36 m²"; "1000 m2" → "1000 m²".</summary>
        internal static string Birimli(string v, string birim)
        {
            v = (v ?? "").Trim();
            if (v.Length == 0) return "";
            v = Regex.Replace(v, @"\s*m\s*(2|²)\s*$", "", RegexOptions.IgnoreCase).Trim();
            return Regex.IsMatch(v, @"\d\s*$") ? v + " " + birim : v;
        }

        /// <summary>"18.200,00" veya "18200 TL" → "… ₺".</summary>
        internal static string Parali(string v)
        {
            v = (v ?? "").Trim();
            if (v.Length == 0) return "";
            v = Regex.Replace(v, @"\s*(TL|₺|YTL)\.?\s*$", "", RegexOptions.IgnoreCase).Trim();
            return Regex.IsMatch(v, @"\d\s*$") ? v + " ₺" : v;
        }

        /// <summary>Türkçe/İngilizce yazılmış sayıyı çözer ("18.200,00", "1000.50", "10 000").</summary>
        internal static double? Sayi(string v)
        {
            string s = Regex.Replace(v ?? "", @"[^\d.,]", "");
            if (s.Length == 0) return null;
            if (s.IndexOf(',') >= 0) s = s.Replace(".", "").Replace(',', '.');
            else if (Regex.IsMatch(s, @"^\d{1,3}(\.\d{3})+$")) s = s.Replace(".", "");
            return double.TryParse(s, NumberStyles.Float, Inv, out double d) ? d : (double?)null;
        }

        /// <summary>Toplam maliyet boşsa inşaat alanı × m² maliyetinden hesaplanır.</summary>
        internal static string ToplamHesapla(string alan, string m2)
        {
            double? a = Sayi(alan), b = Sayi(m2);
            return a != null && b != null && a > 0 && b > 0 ? (a.Value * b.Value).ToString("N2", Tr) : "";
        }

        // ------------------------------------------------------------------ mimari kapak → veri
        /// <summary>Mimari kapaktan okunan (dolu) alanları kapak verisine yazar; okunamayan alanlar olduğu gibi kalır.</summary>
        /// <returns>Proje sahibi kapakta yoktu ve mimari DWG dosya adından alındıysa true.</returns>
        internal static bool Uygula(KapakVeri v, MimariKapak k, string dwgYolu = null)
        {
            if (k == null) return false;
            void Al(string alan, Action<string> koy) { string d = k.Al(alan); if (!string.IsNullOrWhiteSpace(d)) koy(d.Trim()); }
            Al(MimariKapakOku.AMahalle, d => v.Mahalle = d);
            string pap = string.Join(" / ", new[] { k.Al(MimariKapakOku.APafta), k.Al(MimariKapakOku.AAda), k.Al(MimariKapakOku.AParsel) }
                .Select(x => string.IsNullOrWhiteSpace(x) ? "-" : x.Trim()));
            if (pap != "- / - / -") v.Pap = pap;
            Al(MimariKapakOku.AYuzolcumu, d => v.Yuzolcumu = d);
            Al(MimariKapakOku.AIhtiva, d => v.Ihtiva = d);
            Al(MimariKapakOku.ACinsi, d => v.Cinsi = d);
            Al(MimariKapakOku.AKat, d => v.Kat = d);
            Al(MimariKapakOku.AInsaatAlani, d => v.InsaatAlani = d);
            Al(MimariKapakOku.AKullanim, d => v.Kullanim = d);
            Al(MimariKapakOku.AM2, d => v.M2 = d);
            Al(MimariKapakOku.AToplam, d => v.Toplam = d);
            Al(MimariKapakOku.ASinif, d => v.Sinif = d);
            Al(MimariKapakOku.ABlok, d => v.Blok = d);
            Al(MimariKapakOku.ANitelik, d => v.Nitelik = d);
            Al(MimariKapakOku.ASahip, d => v.Sahip = d);
            Al(MimariKapakOku.ADenetim, d => v.DenetimFirma = d);
            if (!Bos(v.Sahip) || string.IsNullOrWhiteSpace(dwgYolu)) return false;
            // Kapakta proje sahibi yok: mimari DWG dosya adında yazıyor olabilir ("17.08.2026 - ADEM TAFLI MİMARİ.dwg")
            string dosyadan = MimariKapakOku.SahipDosyaAdindan(dwgYolu);
            if (dosyadan == null) return false;
            v.Sahip = dosyadan;
            return true;
        }

        // ------------------------------------------------------------------ ayarlar
        /// <summary>Mimari projeden gelen / projeye özgü alanlar: hatırlanmaz, yeni mimari proje okununca boşaltılır.</summary>
        private static readonly string[] ProjeAlanlari =
            { "Nitelik", "Blok", "Mahalle", "Pap", "Yuzolcumu", "Ihtiva", "Cinsi", "Kat", "InsaatAlani", "Kullanim", "M2", "Toplam", "Sinif",
              "Sahip", "FenniAd", "FenniSicil", "DenetimFirma", "DenetimBelge" };
        /// <summary>Hatırlanan alanlar (kapak.ini): proje müellifi ve son başlık.</summary>
        private static readonly string[] BuroAlanlari = { "Baslik", "MuellifAd", "MuellifUnvan", "MuellifSicil", "MuellifBuro" };

        /// <summary>Yeni mimari proje okunmadan önce: projeye özgü alanlar boşalır (açıklama ve tadilat kullanıcı girdisi kalır).</summary>
        internal static void ProjeyiTemizle(KapakVeri v)
        {
            foreach (string ad in ProjeAlanlari) Yaz(v, ad, "");
            v.FenniUnvan = "İNŞAAT MÜHENDİSİ";
            v.Nitelik = "YENİ YAPI"; // mimari kapakta okunmazsa varsayılan
        }

        private static string Oku(KapakVeri v, string ad) => (string)typeof(KapakVeri).GetField(ad).GetValue(v) ?? "";
        private static void Yaz(KapakVeri v, string ad, string deger) => typeof(KapakVeri).GetField(ad).SetValue(v, deger ?? "");

        // ------------------------------------------------------------------ çizim bazında form kaydı
        /// <summary>Çizim (statik proje dosyası) bazında saklanan alanlar; formda geri gelince kırmızı gösterilir.</summary>
        internal static readonly string[] HatirlananAlanlar = ProjeAlanlari.Concat(new[] { "FenniUnvan", "Aciklama", "Tadilat" }).ToArray();

        /// <summary>Çizimin kimliği: kayıtlı çizimde tam yol, kaydedilmemişte belge adı.</summary>
        internal static string ProjeAnahtari(Document doc)
        {
            string ad = null;
            try { ad = doc.IsNamedDrawing ? doc.Database.Filename : doc.Name; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
            if (string.IsNullOrWhiteSpace(ad)) ad = doc.Name ?? "";
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                byte[] h = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(ad.Trim().ToUpperInvariant()));
                return "kapak_" + BitConverter.ToString(h, 0, 8).Replace("-", "").ToLowerInvariant() + ".ini";
            }
        }

        private static string Kodla(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n");
        private static string Coz(string s)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < (s ?? "").Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length) { sb.Append(s[i + 1] == 'n' ? "\r\n" : s[i + 1].ToString()); i++; }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>Bu çizim için saklanan form değerlerini yükler (varsa true).</summary>
        internal static bool ProjeOku(string anahtar, KapakVeri v, out string mimariDwg)
        {
            mimariDwg = "";
            Dictionary<string, string> d = BaIni.Read(anahtar);
            if (d.Count == 0) return false;
            foreach (string ad in HatirlananAlanlar)
                if (d.TryGetValue(ad, out string deger)) Yaz(v, ad, Coz(deger));
            if (string.IsNullOrWhiteSpace(v.Nitelik)) v.Nitelik = "YENİ YAPI";
            if (d.TryGetValue("Baslik", out string b) && b.Length > 0) v.Baslik = b;
            if (int.TryParse(BaIni.Get(d, "Olcek", ""), NumberStyles.Integer, Inv, out int o) && Array.IndexOf(Olcekler, o) >= 0) v.Olcek = o;
            mimariDwg = BaIni.Get(d, "MimariDwg", "");
            return true;
        }

        internal static void ProjeKaydet(string anahtar, KapakVeri v, string mimariDwg)
        {
            var satirlar = HatirlananAlanlar.Select(ad => ad + "=" + Kodla(Oku(v, ad))).ToList();
            satirlar.Add("Baslik=" + (v.Baslik ?? ""));
            satirlar.Add("Olcek=" + v.Olcek.ToString(Inv));
            satirlar.Add("MimariDwg=" + (mimariDwg ?? ""));
            BaIni.Write(anahtar, satirlar.ToArray());
        }

        /// <summary>Son kullanılan değerler (kapak.ini); müellif ilk kullanımda StatikRaporClaude kapak ayarından alınır.</summary>
        internal static KapakVeri SonVeri(out string sonDwg)
        {
            var v = new KapakVeri();
            Dictionary<string, string> ini = BaIni.Read(IniDosya);
            foreach (string ad in BuroAlanlari)
                if (ini.TryGetValue(ad, out string d)) Yaz(v, ad, d);
            if (Array.IndexOf(Basliklar, v.Baslik) < 0 && v.Baslik.StartsWith("STATİK HESAP", StringComparison.Ordinal)) v.Baslik = Basliklar[1];
            v.Olcek = BaIni.GetI(ini, "Olcek", 50);
            if (Array.IndexOf(Olcekler, v.Olcek) < 0) v.Olcek = 50;
            sonDwg = BaIni.Get(ini, "MimariDwg", "");
            if (string.IsNullOrWhiteSpace(v.MuellifAd)) MuellifTohumla(v);
            return v;
        }

        internal static void Kaydet(KapakVeri v, string mimariDwg, string pdfKlasoru = null)
        {
            string eskiPdf = BaIni.Get(BaIni.Read(IniDosya), "PdfKlasor", "");
            var satirlar = BuroAlanlari.Select(ad => ad + "=" + Oku(v, ad).Replace("\r", " ").Replace("\n", " ")).ToList();
            satirlar.Add("Olcek=" + v.Olcek.ToString(Inv));
            satirlar.Add("MimariDwg=" + (mimariDwg ?? ""));
            satirlar.Add("PdfKlasor=" + (string.IsNullOrWhiteSpace(pdfKlasoru) ? eskiPdf : pdfKlasoru));
            BaIni.Write(IniDosya, satirlar.ToArray());
            // Yapı denetim / fenni mesul hatırlanmaz (kullanıcı isteği 24.11.2026: yalnız proje müellifi). Eski kapak-denetim.ini okunmaz.
        }

        private static void MuellifTohumla(KapakVeri v)
        {
            try
            {
                string klasor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StatikRaporClaude", "kapak");
                if (!Directory.Exists(klasor)) return;
                string dosya = new DirectoryInfo(klasor).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Select(f => f.FullName).FirstOrDefault();
                if (dosya == null) return;
                string json = File.ReadAllText(dosya);
                string Al(string ad)
                {
                    Match m = Regex.Match(json, "\"" + ad + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                    return m.Success ? Regex.Unescape(m.Groups[1].Value).Trim() : "";
                }
                v.MuellifAd = Al("MuellifAdi");
                string unvan = Al("Unvani"); if (unvan.Length > 0) v.MuellifUnvan = unvan;
                v.MuellifSicil = Al("OdaSicilNo");
                v.MuellifBuro = Al("BuroTescilNo");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }
    }
}
