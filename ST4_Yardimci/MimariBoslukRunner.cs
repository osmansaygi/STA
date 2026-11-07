using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using static ST4Yardimci.MimariBoslukAnaliz;
using RXClass = Autodesk.AutoCAD.Runtime.RXClass;
using RXObject = Autodesk.AutoCAD.Runtime.RXObject;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// MIMARIBOSLUK: ayrı mimari DWG dosyasından (ya da bu çizimdeki kopya/blok/XREF) kalıp planına şaft/döşeme boşluğu aktarır.
    /// Mimari ve kalıp pencereleri seçilir (üst üste de olabilir: mimari = BEYKENT dışı katmanlar, kalıp = BEYKENT katmanları).
    /// Hizalama kolon eşleşmesiyle otomatik; yeni boşluk KALIP BOSLUK + CIZGI çarpısı, fark işaretleri MIMARI KONTROL katmanı.
    /// </summary>
    internal static class MimariBoslukRunner
    {
        private const string LayerKolon = "KOLON (BEYKENT)";
        private const string LayerPerde = "PERDE (BEYKENT)";
        private const string LayerKalipBosluk = "KALIP BOSLUK (BEYKENT)";
        private const string LayerKatSiniri = "KAT SINIRI (BEYKENT)";
        private const string LayerCizgi = "CIZGI (BEYKENT)";
        private const string LayerKontrol = "MIMARI KONTROL (BEYKENT)";
        private const string Etiket = "\nMIMARIBOSLUK: ";

        /// <summary>İki pencere, okuma ve hizalama sonucu (MIMARIBOSLUK ve MIMARIMERDIVEN ortak).</summary>
        internal sealed class Baglam
        {
            public Kutu KalipPencere;
            public readonly List<Kutu> KalipKolon = new List<Kutu>();
            public readonly List<Kutu> KalipBosluk = new List<Kutu>();
            public readonly List<List<double[]>> KatSiniri = new List<List<double[]>>();
            public readonly Kaynak Mimari = new Kaynak();
            public Hizalama H;
            /// <summary>Açık çizim mimari: kalıp verisi yok, çıktı mimari koordinatında bu çizime.</summary>
            public bool MimariModu;
        }

        public static void Run(bool komutSatiri) => KatKat(komutSatiri, true, false);

        /// <summary>
        /// Basit akış (mimari projede): kat kat plan penceresi seçilir, Enter ile biter. Her katta güçlü şaft/bacalar
        /// ve/veya merdivenler bizim katmanlarımızla doğrudan çizilir; zayıf adaylar kontrol katmanında işaretlenir.
        /// İlk soruda "Kalip" ile eski akış (statik kalıp + mimari dosya/çizim) seçilebilir.
        /// </summary>
        internal static void KatKat(bool komutSatiri, bool bosluk, bool merdiven)
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            CommandEntryKomutSatiri = komutSatiri;
            string etiket = bosluk && merdiven ? "\nMIMARIKAT: " : bosluk ? Etiket : "\nMIMARIMERDIVEN: ";
            ed.WriteMessage(etiket + "Kalıp tavana bakar: her kalıp için ÜSTÜNDEKİ katın mimari planını seçin.");
            for (int kat = 1; ; kat++)
            {
                var po = new PromptPointOptions(etiket + kat + ". kat planı penceresi ilk köşe (Enter: bitir)");
                po.AllowNone = true;
                if (kat == 1 && bosluk != merdiven) po.Keywords.Add("Kalip");
                var p1 = ed.GetPoint(po);
                if (p1.Status == PromptStatus.Keyword)
                {
                    if (bosluk) KalipAkisi(ed, db, komutSatiri);
                    else MimariMerdivenRunner.KalipAkisi(ed, db, komutSatiri);
                    return;
                }
                if (p1.Status != PromptStatus.OK) break;
                var p2 = ed.GetCorner(new PromptCornerOptions(etiket + "Karşı köşe: ", p1.Value));
                if (p2.Status != PromptStatus.OK) break;
                Point3d a = p1.Value.TransformBy(ed.CurrentUserCoordinateSystem), c = p2.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                var pencere = new Kutu(Math.Min(a.X, c.X), Math.Min(a.Y, c.Y), Math.Max(a.X, c.X), Math.Max(a.Y, c.Y));
                if (pencere.W < 1 || pencere.H < 1) continue;
                var b = new Baglam { KalipPencere = pencere };
                if (!MimariModu(ed, db, etiket, merdiven, b)) continue;
                if (bosluk) BoslukCiz(ed, db, b, etiket);
                if (merdiven) MimariMerdivenRunner.MerdivenCiz(ed, db, b, etiket);
            }
        }

        /// <summary>Mimari modunda bir katın şaft/bacalarını onay sormadan çizer (güçlüler), zayıfları ve farkları işaretler.</summary>
        private static void BoslukCiz(Editor ed, Database db, Baglam b, string etiket)
        {
            List<Sonuc> sonuc = Karsilastir(b.Mimari, b.H, b.KalipBosluk, b.KalipKolon, b.KatSiniri);
            var yeni = sonuc.Where(s => s.Tur == Tur.Yeni).ToList();
            var guclu = yeni.Where(s => !s.Zayif).ToList();
            var zayif = yeni.Where(s => s.Zayif).ToList();
            var fark = sonuc.Where(s => s.Tur == Tur.OlcuFarki || s.Tur == Tur.MimarideYok).ToList();
            Ciz(db, guclu);
            Isaretle(db, b.KalipPencere, fark, zayif);
            ed.WriteMessage(etiket + "{0} şaft/baca çizildi ({1}), {2} zaten çizili, {3} zayıf aday {4} katmanında \"ADAY?\" işaretli.",
                guclu.Count, LayerKalipBosluk, sonuc.Count(s => s.Tur == Tur.Mevcut), zayif.Count, LayerKontrol);
            foreach (var s in yeni.Take(40))
                ed.WriteMessage("\n  {6} {0:0}x{1:0} @({2:0},{3:0}) [{4}] {5}", s.K.W, s.K.H, s.K.Cx, s.K.Cy, s.Katman, s.Neden, s.Zayif ? "zayıf" : "çizildi");
        }

        /// <summary>Eski akış: açık çizim statik kalıp; mimari ayrı dosyadan ya da bu çizimden hizalanır, onaylı çizilir.</summary>
        private static void KalipAkisi(Editor ed, Database db, bool komutSatiri)
        {
            if (!HizaliOku(ed, db, Etiket, false, out Baglam b)) return;
            Kutu kalipPencere = b.KalipPencere;
            List<Kutu> kalipKolon = b.KalipKolon, kalipBosluk = b.KalipBosluk;
            List<List<double[]>> katSiniri = b.KatSiniri;
            Kaynak mimari = b.Mimari;
            Hizalama h = b.H;

            List<Sonuc> sonuc = Karsilastir(mimari, h, kalipBosluk, kalipKolon, katSiniri);
            var yeni = sonuc.Where(s => s.Tur == Tur.Yeni).OrderBy(s => s.Zayif).ToList();
            int mevcut = sonuc.Count(s => s.Tur == Tur.Mevcut), zayif = yeni.Count(s => s.Zayif);
            var fark = sonuc.Where(s => s.Tur == Tur.OlcuFarki || s.Tur == Tur.MimarideYok).ToList();
            ed.WriteMessage(Etiket + "{0} yeni aday ({1} güçlü, {2} zayıf), {3} boşluk kalıpta zaten var, {4} ölçü farkı, {5} kalıp boşluğunun mimaride karşılığı yok.",
                yeni.Count, yeni.Count - zayif, zayif, mevcut, sonuc.Count(s => s.Tur == Tur.OlcuFarki), sonuc.Count(s => s.Tur == Tur.MimarideYok));

            // Çizilmeyen adaylar kontrol katmanında "ADAY?" olarak kalır; hiçbir bulgu kaybolmaz.
            int cizilen = 0;
            var bekleyen = new List<Sonuc>(yeni);
            if (yeni.Count > 0)
            {
                string secim = Secim(ed, komutSatiri
                    ? new[] { "Tumu", "Hicbiri" }
                    : new[] { "Tektek", "Tumu", "Hicbiri" }, "Yeni boşluklar (Tumu: yalnız güçlüler)");
                if (secim == null) return;
                if (secim == "Tumu")
                {
                    var guclu = yeni.Where(s => !s.Zayif).ToList();
                    Ciz(db, guclu);
                    cizilen = guclu.Count;
                    bekleyen = yeni.Where(s => s.Zayif).ToList();
                }
                else if (secim == "Tektek") cizilen = TekTek(ed, db, yeni, out bekleyen);
            }
            Isaretle(db, kalipPencere, fark, bekleyen);
            ed.WriteMessage(Etiket + "{0} boşluk çizildi ({1}); {2} fark ve {3} onaysız aday {4} katmanında işaretlendi.",
                cizilen, LayerKalipBosluk, fark.Count, bekleyen.Count, LayerKontrol);
            foreach (var s in yeni.Take(40))
                ed.WriteMessage("\n  {6} {0:0}x{1:0} @({2:0},{3:0}) [{4}] {5}", s.K.W, s.K.H, s.K.Cx, s.K.Cy, s.Katman, s.Neden, s.Zayif ? "zayıf" : "güçlü");
        }

        private const string IniDosya = "mimari.ini";

        /// <summary>
        /// Kalıp penceresini sorar; mimariyi ayrı DWG dosyasından (varsayılan, çizime dokunmadan salt okunur) ya da
        /// bu çizimdeki bir pencereden okur ve hizalar. Dosyada birden çok kat varsa kat planı başlığıyla seçtirir.
        /// </summary>
        internal static bool HizaliOku(Editor ed, Database db, string etiket, bool merdiven, out Baglam b)
        {
            b = new Baglam();
            if (!PencereSor(ed, etiket, "Kalıp kat planı ilk köşe", out b.KalipPencere)) return false;
            string kaynak = Secim(ed, etiket, new[] { "Dosya", "Cizim" }, "Mimari kaynağı (Dosya: ayrı mimari DWG, Cizim: bu çizimde)");
            if (kaynak == null) return false;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                KalipOku(tr, db, b.KalipPencere, b.KalipKolon, b.KalipBosluk, b.KatSiniri);
                tr.Commit();
            }
            if (kaynak == "Cizim")
            {
                if (!PencereSor(ed, etiket, "Mimari kat planı (kalıp tavana bakar: kalıbın ÜSTÜNDEKİ katın mimarisi) ilk köşe", out Kutu mimariPencere)) return false;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    new MimariOkuyucu(tr, db, mimariPencere, b.Mimari, merdiven).Oku();
                    tr.Commit();
                }
            }
            else
            {
                string yol = MimariDosyaSor(ed, etiket, db);
                if (yol == null) return false;
                ed.WriteMessage(etiket + "Mimari okunuyor: {0}", yol);
                try
                {
                    using (var mdb = new Database(false, true))
                    {
                        mdb.ReadDwgFile(yol, System.IO.FileShare.ReadWrite, true, null);
                        mdb.CloseInput(true);
                        using (var tr = mdb.TransactionManager.StartTransaction())
                        {
                            new MimariOkuyucu(tr, mdb, new Kutu(-1e15, -1e15, 1e15, 1e15), b.Mimari, merdiven).Oku();
                            tr.Commit();
                        }
                    }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    ed.WriteMessage(etiket + "Mimari dosya okunamadı ({0}): {1}", ex.ErrorStatus, yol);
                    return false;
                }
            }
            ed.WriteMessage(etiket + "kalıpta {0} kolon/perde, {1} boşluk; mimaride {2} kontur, {3} anahtar yazı, {4} çizgi, {5} sayı.",
                b.KalipKolon.Count, b.KalipBosluk.Count, b.Mimari.Konturlar.Count, b.Mimari.Yazilar.Count, b.Mimari.Cizgiler.Count, b.Mimari.Sayilar.Count);
            if (b.KalipKolon.Count == 0)
            {
                ed.WriteMessage(etiket + "Kalıp penceresinde {0} / {1} katmanında kapalı kolon yok.", LayerKolon, LayerPerde);
                return false;
            }

            b.H = Hizala(b.KalipKolon, MimariKolonlar(b.Mimari));
            bool hizaZayif = b.H.Eslesen < 4 || b.H.Eslesen < 0.25 * b.KalipKolon.Count;
            ed.WriteMessage(etiket + "Hizalama: " + b.H.Ozet);
            if (hizaZayif)
            {
                ed.WriteMessage(etiket + "Kolon eşleşmesi zayıf (mimari kolonları farklı çizilmiş, döndürülmüş ya da yansıtılmış olabilir).");
                if (!IkiNoktaHizala(ed, etiket, b.H)) return false;
            }
            else if (b.H.Adaylar.Count > 1 && !KatSec(ed, etiket, b))
                return false;
            return true;
        }

        /// <summary>
        /// Mimari modu: açık çizim mimaridir. Seçilen plan penceresi hem mimari kaynak hem çizim yeridir; hizalama birimdir.
        /// Kolon zarfı mimari kolonlarından; daha önce bu çizime çizilmiş KALIP BOSLUK / MERDIVEN çıktıları "mevcut" sayılır.
        /// </summary>
        private static bool MimariModu(Editor ed, Database db, string etiket, bool merdiven, Baglam b)
        {
            b.MimariModu = true;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var kolon = new List<Kutu>();
                KalipOku(tr, db, b.KalipPencere, kolon, b.KalipBosluk, b.KatSiniri);
                b.KatSiniri.Clear();
                new MimariOkuyucu(tr, db, b.KalipPencere, b.Mimari, merdiven).Oku();
                tr.Commit();
            }
            b.KalipKolon.AddRange(MimariKolonlar(b.Mimari));
            b.H = new Hizalama { Olcek = 1, Eslesen = b.KalipKolon.Count, KalipKolon = b.KalipKolon.Count, MimariKolon = b.KalipKolon.Count };
            ed.WriteMessage(etiket + "Kat okundu: {0} kolon, {1} kontur, {2} anahtar yazı, {3} çizgi, {4} sayı.",
                b.KalipKolon.Count, b.Mimari.Konturlar.Count, b.Mimari.Yazilar.Count, b.Mimari.Cizgiler.Count, b.Mimari.Sayilar.Count);
            // Kurallar cm içindir (basamak 22–38, kolon 20–400): mm çizimde kolonlar 10 kat büyük görünür.
            var genislik = b.KalipKolon.Select(k => Math.Min(k.W, k.H)).OrderBy(v => v).ToList();
            if (genislik.Count >= 4 && genislik[genislik.Count / 2] > 150)
            {
                ed.WriteMessage(etiket + "Mimari mm birimli görünüyor (kolon genişliği ~{0:0}); şimdilik cm çizimler destekleniyor.", genislik[genislik.Count / 2]);
                return false;
            }
            return true;
        }

        /// <summary>Mimari dosya yolu: diyalog (son dosya hatırlanır) ya da betikte komut satırı.</summary>
        private static string MimariDosyaSor(Editor ed, string etiket, Database db)
        {
            var ini = BaIni.Read(IniDosya);
            string son = BaIni.Get(ini, "Dosya", "");
            string yol;
            if (CommandEntryKomutSatiri)
            {
                var ps = new PromptStringOptions(etiket + "Mimari DWG dosyası: ") { AllowSpaces = true };
                if (!string.IsNullOrEmpty(son)) { ps.DefaultValue = son; ps.UseDefaultValue = true; }
                var pr = ed.GetString(ps);
                if (pr.Status != PromptStatus.OK) return null;
                yol = (pr.StringResult ?? "").Trim().Trim('"');
            }
            else
            {
                string klasor = null;
                try { klasor = System.IO.File.Exists(son) ? System.IO.Path.GetDirectoryName(son) : System.IO.Path.GetDirectoryName(db.Filename); } catch { }
                using (var dlg = new System.Windows.Forms.OpenFileDialog
                {
                    Title = "Mimari proje DWG dosyasını seçin",
                    Filter = "AutoCAD çizimi (*.dwg)|*.dwg",
                    InitialDirectory = klasor ?? "",
                    FileName = System.IO.File.Exists(son) ? System.IO.Path.GetFileName(son) : ""
                })
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;
                    yol = dlg.FileName;
                }
            }
            if (!System.IO.File.Exists(yol)) { ed.WriteMessage(etiket + "Dosya yok: {0}", yol); return null; }
            if (string.Equals(System.IO.Path.GetFullPath(yol), db.Filename, StringComparison.OrdinalIgnoreCase))
            {
                ed.WriteMessage(etiket + "Seçilen dosya açık kalıp çiziminin kendisi; mimari dosyayı seçin.");
                return null;
            }
            BaIni.Write(IniDosya, "Dosya=" + yol);
            return yol;
        }

        /// <summary>Betik sürümü (-MIMARI…) iletişim penceresi açmaz.</summary>
        [ThreadStatic] internal static bool CommandEntryKomutSatiri;

        /// <summary>Mimari dosyada kolonları kalıba uyan birden çok kat: plan başlığıyla listeler, kullanıcı seçer.</summary>
        private static bool KatSec(Editor ed, string etiket, Baglam b)
        {
            var a = b.H.Adaylar;
            ed.WriteMessage(etiket + "Mimari dosyada kalıba uyan {0} kat planı bulundu (kalıp tavana bakar: kalıbın ÜSTÜNDEKİ katı seçin):", a.Count);
            var zarf = new Kutu(b.KalipKolon.Min(k => k.X0), b.KalipKolon.Min(k => k.Y0), b.KalipKolon.Max(k => k.X1), b.KalipKolon.Max(k => k.Y1));
            // Mimari dosyadaki dizilişe göre sıra (üst sıradan alta, soldan sağa): kullanıcı katların yerleşimini bilir.
            var merkez = a.Select(h => new[] { (zarf.Cx - h.Dx) / h.Olcek, (zarf.Cy - h.Dy) / h.Olcek }).ToList();
            double sira = zarf.H / a[0].Olcek;
            var dizilis = Enumerable.Range(0, a.Count)
                .OrderByDescending(i => Math.Round(merkez[i][1] / sira)).ThenBy(i => merkez[i][0]).ToList();
            for (int i = 0; i < a.Count; i++)
            {
                int no = dizilis.IndexOf(i) + 1;
                // Blok adı yalnız planın en az yarısını kaplayan blok için (kesit işareti, ölçü bloğu değil).
                var h0 = a[i];
                var bolge = new Kutu((zarf.X0 - h0.Dx) / h0.Olcek, (zarf.Y0 - h0.Dy) / h0.Olcek, (zarf.X1 - h0.Dx) / h0.Olcek, (zarf.Y1 - h0.Dy) / h0.Olcek);
                string blok = b.Mimari.Bloklar.Where(kv => kv.Value.Kesisim(bolge) >= 0.5 * bolge.Alan && kv.Value.Alan <= 4 * bolge.Alan)
                    .OrderBy(kv => kv.Value.Alan).Select(kv => kv.Key).FirstOrDefault();
                ed.WriteMessage("\n  {0}: dosyada {1}. plan (üstten, soldan){2}  {3}  ({4}/{5} kolon)", i + 1, no,
                    blok != null ? "  blok: " + blok : "", PlanBasligi(b.Mimari, a[i], zarf), a[i].Eslesen, a[i].KalipKolon);
            }
            var po = new PromptIntegerOptions(etiket + "Kat planı numarası <1>: ") { AllowNone = true, LowerLimit = 1, UpperLimit = a.Count };
            var r = ed.GetInteger(po);
            if (r.Status == PromptStatus.None) { b.H = a[0]; return true; }
            if (r.Status != PromptStatus.OK) return false;
            b.H = a[r.Value - 1];
            ed.WriteMessage(etiket + "Seçilen: " + PlanBasligi(b.Mimari, b.H, zarf));
            return true;
        }

        /// <summary>Hizalamaya karşılık gelen mimari bölgenin yakınındaki "…PLANI" yazısı (yoksa konum).</summary>
        private static string PlanBasligi(Kaynak m, Hizalama h, Kutu kalipZarf)
        {
            double Geri(double v, bool y) => (v - (y ? h.Dy : h.Dx)) / h.Olcek;
            var r = new Kutu(Geri(kalipZarf.X0, false), Geri(kalipZarf.Y0, true), Geri(kalipZarf.X1, false), Geri(kalipZarf.Y1, true));
            var ara = new Kutu(r.X0 - 0.3 * r.W, r.Y0 - 0.6 * r.H, r.X1 + 0.3 * r.W, r.Y1 + 0.3 * r.H);
            var en = m.Basliklar.Where(t => ara.Icerir(t.X, t.Y))
                .OrderBy(t => Ust(t.Metin).Contains("KAT") ? 0 : 1)
                .ThenBy(t => Math.Abs(t.X - r.Cx) + Math.Abs(t.Y - r.Y0))
                .FirstOrDefault();
            string s = en != null ? en.Metin.Replace("%%U", "").Replace("%%u", "").Replace("\r", " ").Replace("\n", " ").Trim() : "başlık yok";
            if (s.Length > 50) s = s.Substring(0, 50) + "…";
            return string.Format("{0}  [mimari konum {1:0},{2:0}]", s, r.Cx, r.Cy);
        }

        private static string Ust(string s) => (s ?? "").ToUpper(new System.Globalization.CultureInfo("tr-TR"));

        private static bool PencereSor(Editor ed, string etiket, string mesaj, out Kutu k)
        {
            k = default;
            var p1 = ed.GetPoint(new PromptPointOptions(etiket + mesaj + ": "));
            if (p1.Status != PromptStatus.OK) return false;
            var p2 = ed.GetCorner(new PromptCornerOptions(etiket + "Karşı köşe: ", p1.Value));
            if (p2.Status != PromptStatus.OK) return false;
            Point3d a = p1.Value.TransformBy(ed.CurrentUserCoordinateSystem), b = p2.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            k = new Kutu(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
            return k.W > 1 && k.H > 1;
        }

        private static bool IkiNoktaHizala(Editor ed, string etiket, Hizalama h)
        {
            // İki nokta çifti: ölçek (birim farkı) + öteleme. Döndürülmüş/yansıtılmış mimari desteklenmez.
            if (Secim(ed, etiket, new[] { "Ikinokta", "Vazgec" }, "Elle hizalama") != "Ikinokta") return false;
            var p = new Point3d[4];
            string[] soru = { "Mimaride 1. referans nokta (ör. aks kesişimi)", "Kalıpta aynı nokta", "Mimaride 2. referans nokta (uzak bir aks kesişimi)", "Kalıpta aynı nokta" };
            for (int i = 0; i < 4; i++)
            {
                var r = ed.GetPoint(new PromptPointOptions(etiket + soru[i] + ": "));
                if (r.Status != PromptStatus.OK) return false;
                p[i] = r.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            }
            Vector3d vm = p[2] - p[0], vk = p[3] - p[1];
            if (vm.Length < 1 || vk.Length < 1) { ed.WriteMessage(etiket + "Referans noktaları çok yakın."); return false; }
            if (Math.Abs(vm.GetAngleTo(vk)) > Math.PI / 180)
                ed.WriteMessage(etiket + "Uyarı: iki nokta çifti arasında dönme var; döndürülmüş mimari desteklenmiyor, yalnız ölçek/öteleme uygulandı.");
            h.Olcek = vk.Length / vm.Length;
            h.Dx = p[1].X - p[0].X * h.Olcek; h.Dy = p[1].Y - p[0].Y * h.Olcek;
            ed.WriteMessage(etiket + "Elle hizalama: " + h.Ozet);
            return true;
        }

        private static string Secim(Editor ed, string[] secenekler, string baslik) => Secim(ed, Etiket, secenekler, baslik);

        internal static string Secim(Editor ed, string etiket, string[] secenekler, string baslik)
        {
            var po = new PromptKeywordOptions(etiket + baslik + " [" + string.Join("/", secenekler) + "] <" + secenekler[0] + ">: ", string.Join(" ", secenekler));
            po.AllowNone = true;
            var r = ed.GetKeywords(po);
            if (r.Status == PromptStatus.None) return secenekler[0];
            return r.Status == PromptStatus.OK ? r.StringResult : null;
        }

        private static void KalipOku(Transaction tr, Database db, Kutu pencere, List<Kutu> kolon, List<Kutu> bosluk, List<List<double[]>> katSiniri)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (!id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Polyline)))) continue;
                var pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                if (pl == null || pl.NumberOfVertices < 3) continue;
                bool kol = Ayni(pl.Layer, LayerKolon) || Ayni(pl.Layer, LayerPerde), bos = Ayni(pl.Layer, LayerKalipBosluk);
                bool sinir = Ayni(pl.Layer, LayerKatSiniri);
                if (!kol && !bos && !sinir) continue;
                if (!Kapali(pl)) continue;
                var p = Noktalar(pl, Matrix3d.Identity);
                var k = Kutu.Of(p);
                if (!pencere.Icerir(k.Cx, k.Cy)) continue;
                if (sinir) katSiniri.Add(p);
                else (kol ? kolon : bosluk).Add(k);
            }
        }

        internal static bool Ayni(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static bool Kapali(Polyline pl)
        {
            if (pl.Closed) return true;
            return pl.GetPoint2dAt(0).GetDistanceTo(pl.GetPoint2dAt(pl.NumberOfVertices - 1)) < 0.5;
        }

        internal static List<double[]> Noktalar(Polyline pl, Matrix3d m)
        {
            int n = pl.NumberOfVertices;
            if (!pl.Closed && n > 3 && pl.GetPoint2dAt(0).GetDistanceTo(pl.GetPoint2dAt(n - 1)) < 0.5) n--;
            var r = new List<double[]>(n);
            for (int i = 0; i < n; i++)
            {
                Point3d p = pl.GetPoint3dAt(i).TransformBy(m);
                r.Add(new[] { p.X, p.Y });
            }
            return r;
        }

        /// <summary>
        /// Mimari pencerede (blok ve XREF içleri dahil) kontur, köşegen parça ve anahtar yazı toplar.
        /// Merdiven kipinde ayrıca tüm düz çizgi parçaları (kapalı kontur kenarları dahil) ve sayı yazıları toplanır.
        /// </summary>
        private sealed class MimariOkuyucu
        {
            private readonly Transaction _tr;
            private readonly Database _db;
            private readonly Kutu _pencere;
            private readonly Kaynak _k;
            private readonly bool _merdiven;
            private readonly HashSet<string> _kapali = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _donuk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private static readonly RXClass PlSinif = RXObject.GetClass(typeof(Polyline));
            private static readonly RXClass LineSinif = RXObject.GetClass(typeof(Line));
            private static readonly RXClass TextSinif = RXObject.GetClass(typeof(DBText));
            private static readonly RXClass MTextSinif = RXObject.GetClass(typeof(MText));
            private static readonly RXClass RefSinif = RXObject.GetClass(typeof(BlockReference));

            public MimariOkuyucu(Transaction tr, Database db, Kutu pencere, Kaynak k, bool merdiven)
            {
                _tr = tr; _db = db; _pencere = pencere; _k = k; _merdiven = merdiven;
            }

            public void Oku()
            {
                var lt = (LayerTable)_tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt)
                {
                    var l = (LayerTableRecord)_tr.GetObject(id, OpenMode.ForRead);
                    if (l.IsFrozen) _donuk.Add(l.Name);
                    if (l.IsOff) _kapali.Add(l.Name);
                }
                var bt = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
                Gez((BlockTableRecord)_tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead), Matrix3d.Identity, "0", 0);
            }

            private void Gez(BlockTableRecord btr, Matrix3d m, string ustKatman, int derinlik)
            {
                foreach (ObjectId id in btr)
                {
                    RXClass c = id.ObjectClass;
                    if (!(c.IsDerivedFrom(PlSinif) || c.IsDerivedFrom(LineSinif) || c.IsDerivedFrom(TextSinif)
                          || c.IsDerivedFrom(MTextSinif) || c.IsDerivedFrom(RefSinif))) continue;
                    var e = _tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null || !e.Visible) continue;
                    string katman = e.Layer == "0" ? ustKatman : e.Layer;
                    if (_donuk.Contains(katman)) continue;
                    bool beykent = katman.EndsWith("(BEYKENT)", StringComparison.OrdinalIgnoreCase);

                    if (e is BlockReference br)
                    {
                        // Yapıştırılan mimari blok o an etkin BEYKENT katmanına düşmüş olabilir: içi yine okunur.
                        if (derinlik >= 8 || br is MInsertBlock || (_merdiven ? MerdivenRetKatmani(katman) : RetKatmani(katman))) continue;
                        if (derinlik == 0 && !KutuKesisir(br, m)) continue;
                        if (derinlik <= 1) BlokKaydet(br, m);
                        var alt = _tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                        if (alt == null || alt.IsLayout) continue;
                        Gez(alt, br.BlockTransform.PreMultiplyBy(m), beykent ? "0" : katman, derinlik + 1);
                        continue;
                    }
                    if (beykent) continue;
                    // Kapalı katmandaki blok gizlenmez; yalnız içindeki 0 katmanlı nesneler gizlenir.
                    if (_kapali.Contains(katman)) continue;
                    // Yazının yeri: görünen merkezi (sola yaslı yazının ekleme noktası kutunun dışında kalabilir).
                    if (e is DBText t) { YaziEkle(t.TextString, Merkez(t, t.Position).TransformBy(m)); continue; }
                    if (e is MText mt) { YaziEkle(mt.Text, Merkez(mt, mt.Location).TransformBy(m)); continue; }
                    if (_merdiven ? MerdivenRetKatmani(katman) : RetKatmani(katman)) continue;
                    if (e is Line ln) { ParcaEkle(ln.StartPoint.TransformBy(m), ln.EndPoint.TransformBy(m), katman); continue; }
                    if (e is Polyline pl && pl.NumberOfVertices >= 2)
                    {
                        if (pl.NumberOfVertices >= 3 && Kapali(pl))
                        {
                            var p = Noktalar(pl, m);
                            var k = Kutu.Of(p);
                            if (_pencere.Icerir(k.Cx, k.Cy) && k.W > 0.01 && k.H > 0.01)
                                _k.Konturlar.Add(new Kontur { Katman = katman, Noktalar = p, K = k });
                            // Kenarlar hem rıht adayı (basamak kutu çizilmiş olabilir) hem şaft sınırı poligonlaması için toplanır.
                            for (int i = 0; i < p.Count; i++)
                            {
                                var q = p[(i + 1) % p.Count];
                                CizgiEkle(p[i][0], p[i][1], q[0], q[1], katman);
                            }
                        }
                        else
                            for (int i = 0; i + 1 < pl.NumberOfVertices; i++)
                                ParcaEkle(pl.GetPoint3dAt(i).TransformBy(m), pl.GetPoint3dAt(i + 1).TransformBy(m), katman);
                    }
                }
            }

            /// <summary>Adlı blok (ör. ArchiCAD "ZEMİN KAT") kat planı seçiminde etiket olur; anonim/dinamik bloklar atlanır.</summary>
            private void BlokKaydet(BlockReference br, Matrix3d m)
            {
                string ad;
                try { ad = br.IsDynamicBlock ? ((BlockTableRecord)_tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name : br.Name; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { return; }
                if (string.IsNullOrEmpty(ad) || ad.StartsWith("*") || ad.StartsWith("A$C")) return;
                Extents3d ex;
                try { ex = br.GeometricExtents; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { return; }
                var a = ex.MinPoint.TransformBy(m); var b = ex.MaxPoint.TransformBy(m);
                var k = new Kutu(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                if (k.W > 500 && k.H > 500) _k.Bloklar.Add(new KeyValuePair<string, Kutu>(ad, k));
            }

            private static Point3d Merkez(Entity e, Point3d yedek)
            {
                try
                {
                    Extents3d ex = e.GeometricExtents;
                    return new Point3d((ex.MinPoint.X + ex.MaxPoint.X) / 2, (ex.MinPoint.Y + ex.MaxPoint.Y) / 2, 0);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { return yedek; }
            }

            private bool KutuKesisir(BlockReference br, Matrix3d m)
            {
                Extents3d ex;
                try { ex = br.GeometricExtents; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { return true; }
                var a = ex.MinPoint.TransformBy(m); var b = ex.MaxPoint.TransformBy(m);
                var k = new Kutu(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                return k.Kesisim(_pencere) > 0 || _pencere.IcindeMi(k, 0) || k.IcindeMi(_pencere, 0);
            }

            private void YaziEkle(string s, Point3d p)
            {
                if (string.IsNullOrWhiteSpace(s) || !_pencere.Icerir(p.X, p.Y)) return;
                string u = s.ToUpper(new System.Globalization.CultureInfo("tr-TR"));
                // Kat planı başlığı: kelime başında PLAN (ZEMİN KAT PLANI, 1. KAT PLANI); TOPLANTI gibi içte geçen sayılmaz.
                if (s.Length <= 120 && System.Text.RegularExpressions.Regex.IsMatch(u, @"(^|[^A-ZÇĞİÖŞÜ])PLAN"))
                    _k.Basliklar.Add(new MimariBoslukAnaliz.Yazi { Metin = s, X = p.X, Y = p.Y });
                if (_merdiven && int.TryParse(s.Trim(), out int no) && no >= 1 && no <= 80)
                    _k.Sayilar.Add(new MimariBoslukAnaliz.Yazi { Metin = no.ToString(), X = p.X, Y = p.Y });
                if (!AnahtarYazi(s)) return;
                _k.Yazilar.Add(new MimariBoslukAnaliz.Yazi { Metin = s, X = p.X, Y = p.Y });
            }

            private void CizgiEkle(double x0, double y0, double x1, double y1, string katman)
            {
                if (!_pencere.Icerir((x0 + x1) / 2, (y0 + y1) / 2)) return;
                _k.Cizgiler.Add(new MimariBoslukAnaliz.Parca { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Katman = katman });
            }

            private void ParcaEkle(Point3d a, Point3d b, string katman)
            {
                CizgiEkle(a.X, a.Y, b.X, b.Y, katman);
                double dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
                if (dx < 1e-6 || dy < 1e-6 || Math.Min(dx, dy) < 0.15 * Math.Max(dx, dy)) return; // yalnız köşegen
                if (!_pencere.Icerir((a.X + b.X) / 2, (a.Y + b.Y) / 2)) return;
                _k.Parcalar.Add(new MimariBoslukAnaliz.Parca { X0 = a.X, Y0 = a.Y, X1 = b.X, Y1 = b.Y });
            }
        }

        private static int TekTek(Editor ed, Database db, List<Sonuc> yeni, out List<Sonuc> sorulmayan)
        {
            int n = 0;
            sorulmayan = new List<Sonuc>();
            for (int i = 0; i < yeni.Count; i++)
            {
                var s = yeni[i];
                Yakinlas(ed, s.K);
                ObjectId isaret;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    BeykentKatmanYukle.Ensure(tr, db, LayerKontrol, 6, LineWeight.LineWeight030);
                    isaret = Ekle(tr, db, Cerceve(s.K, 8), LayerKontrol, 1);
                    tr.Commit();
                }
                ed.UpdateScreen();
                string c = Secim(ed, new[] { "Evet", "Hayir", "Tumu", "Cik" },
                    string.Format("{0}/{1} {2} {3:0}x{4:0} ({5}) çizilsin mi", i + 1, yeni.Count, s.Zayif ? "zayıf" : "güçlü", s.K.W, s.K.H, s.Neden));
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(isaret, OpenMode.ForWrite, false) is Entity e) e.Erase();
                    tr.Commit();
                }
                if (c == "Evet") { Ciz(db, new[] { s }); n++; }
                else if (c == "Tumu") { var kalan = yeni.Skip(i).ToList(); Ciz(db, kalan); return n + kalan.Count; }
                else if (c == null || c == "Cik") { sorulmayan = yeni.Skip(i).ToList(); return n; }
            }
            return n;
        }

        internal static void Yakinlas(Editor ed, Kutu k)
        {
            using (ViewTableRecord v = ed.GetCurrentView())
            {
                double oran = v.Height > 0 ? v.Width / v.Height : 1.5;
                double h = Math.Max(k.H, k.W / oran) * 6 + 200;
                v.CenterPoint = new Point2d(k.Cx, k.Cy);
                v.Height = h; v.Width = h * oran;
                ed.SetCurrentView(v);
            }
        }

        private static void Ciz(Database db, IEnumerable<Sonuc> liste)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                BeykentKatmanYukle.Ensure(tr, db, LayerKalipBosluk, 30, LineWeight.LineWeight025);
                BeykentKatmanYukle.Ensure(tr, db, LayerCizgi, 1, LineWeight.LineWeight020);
                foreach (var s in liste)
                {
                    Ekle(tr, db, s.Noktalar, LayerKalipBosluk, -1);
                    EkleCizgi(tr, db, s.K.X0, s.K.Y0, s.K.X1, s.K.Y1);
                    EkleCizgi(tr, db, s.K.X0, s.K.Y1, s.K.X1, s.K.Y0);
                }
                tr.Commit();
            }
        }

        /// <summary>Kalıp penceresindeki eski kontrol işaretlerini silip farkları yeniden işaretler.</summary>
        private static void Isaretle(Database db, Kutu kalipPencere, List<Sonuc> fark, List<Sonuc> aday)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null || !Ayni(e.Layer, LayerKontrol)) continue;
                    Extents3d ex;
                    try { ex = e.GeometricExtents; } catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                    if (!kalipPencere.Icerir((ex.MinPoint.X + ex.MaxPoint.X) / 2, (ex.MinPoint.Y + ex.MaxPoint.Y) / 2)) continue;
                    e.UpgradeOpen(); e.Erase();
                }
                if (fark.Count + aday.Count > 0)
                {
                    BeykentKatmanYukle.Ensure(tr, db, LayerKontrol, 6, LineWeight.LineWeight030);
                    foreach (var s in fark.Concat(aday))
                    {
                        Ekle(tr, db, Cerceve(s.K, 10), LayerKontrol, -1);
                        string yazi = s.Tur == Tur.MimarideYok ? "MİMARİDE YOK"
                            : s.Tur == Tur.Yeni ? string.Format("ADAY? {0:0}x{1:0} ({2})", s.K.W, s.K.H, s.Neden)
                            : string.Format("MİMARİ {0:0}x{1:0} / KALIP {2:0}x{3:0}", s.K.W, s.K.H, s.KalipK.W, s.KalipK.H);
                        var t = new DBText();
                        t.SetDatabaseDefaults(db);
                        t.TextString = yazi; t.Height = 10; t.Position = new Point3d(s.K.X0 - 10, s.K.Y1 + 16, 0);
                        t.Layer = LayerKontrol;
                        Ekle(tr, db, t);
                    }
                }
                tr.Commit();
            }
        }

        internal static List<double[]> Cerceve(Kutu k, double pay) => new List<double[]>
        {
            new[] { k.X0 - pay, k.Y0 - pay }, new[] { k.X1 + pay, k.Y0 - pay }, new[] { k.X1 + pay, k.Y1 + pay }, new[] { k.X0 - pay, k.Y1 + pay }
        };

        internal static ObjectId Ekle(Transaction tr, Database db, List<double[]> p, string katman, short renk)
        {
            var pl = new Polyline(p.Count);
            for (int i = 0; i < p.Count; i++) pl.AddVertexAt(i, new Point2d(p[i][0], p[i][1]), 0, 0, 0);
            pl.Closed = true;
            pl.SetDatabaseDefaults(db);
            pl.Layer = katman;
            if (renk >= 0) pl.ColorIndex = renk;
            return Ekle(tr, db, pl);
        }

        private static void EkleCizgi(Transaction tr, Database db, double x0, double y0, double x1, double y1)
        {
            var ln = new Line(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0));
            ln.SetDatabaseDefaults(db);
            ln.Layer = LayerCizgi;
            Ekle(tr, db, ln);
        }

        internal static ObjectId Ekle(Transaction tr, Database db, Entity e)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            ObjectId id = ms.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
            return id;
        }
    }
}
