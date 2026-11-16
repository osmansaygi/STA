using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using static ST4Yardimci.MimariStaAnaliz;
using Kutu = ST4Yardimci.MimariBoslukAnaliz.Kutu;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using ColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;

namespace ST4Yardimci
{
    /// <summary>
    /// MIMARISTA: mimari DWG'yi STA4CAD V15 "DWG çiziminden akıllı kontrolle yapı modeli" kurallarına hazırlar.
    /// Açık çizime dokunmaz: seçilen kat planları ayrı veritabanına alınır, bloklar patlatılır, aks/kolon/duvar/merdiven
    /// katmanları STA4'ün tanıdığı adlara toplanır, akslar balondan balona tek CENTER çizgisi yapılır, kolonlar kapalı
    /// çokgene çevrilir, basamak numarası ve ölçü tamamlanır, kullanılmayan tanımlar temizlenir ve &lt;ad&gt;_STA.dwg yazılır.
    /// </summary>
    internal static class MimariStaRunner
    {
        private const string Etiket = "\nMIMARISTA: ";
        // BEYKENT katman standardı (kullanıcı isteği 14.11.2026); adlar STA4'ün aradığı AKS/KOLON/DUVAR/MERDIVEN sözcüklerini içerir.
        // Katman adları: STA kipi (varsayılan) STA örnek çizimleri ve kuralındaki düz adlar; BEYKENT kipi bizim katman standardı.
        internal static string KAks, KAksBalon, KAksYazi, KKolon, KDuvar, KMerdiven, KMerdivenYazi, KSinir, KOlcu, KCizgi, KKapi, KPencere;
        private static string KKolonTarama, KDuvarTarama;

        internal static void KatmanAdlari(bool sta)
        {
            if (sta)
            {
                // STA kuralı: AKS, KOLON, DUVAR, MERDIVEN. Aks balonu ve adı da AKS katmanında; çıkış hattı MERDIVEN sözcüğü içermez.
                KAks = KAksBalon = KAksYazi = "AKS"; KKolon = "KOLON"; KDuvar = "DUVAR"; KMerdiven = "MERDIVEN"; KMerdivenYazi = "CIKIS HATTI";
                KSinir = "DOSEME SINIRI"; KOlcu = "OLCU"; KCizgi = "CIZGI"; KKapi = "KAPI"; KPencere = "PENCERE"; KKolonTarama = KDuvarTarama = "TARAMA";
            }
            else
            {
                KAks = "AKS CIZGISI (BEYKENT)"; KAksBalon = "AKS BALONU (BEYKENT)"; KAksYazi = "AKS YAZISI (BEYKENT)";
                KKolon = "KOLON (BEYKENT)"; KDuvar = "DUVAR (BEYKENT)"; KMerdiven = "MERDIVEN (BEYKENT)"; KMerdivenYazi = "MERDIVEN YAZI (BEYKENT)";
                KSinir = "DOSEME SINIRI (BEYKENT)"; KOlcu = "AKS OLCU (BEYKENT)"; KCizgi = "CIZGI (BEYKENT)";
                KKapi = "KAPI (BEYKENT)"; KPencere = "PENCERE (BEYKENT)"; KKolonTarama = KDuvarTarama = "TARAMA (BEYKENT)";
            }
        }

        internal sealed class KatmanOzet
        {
            public string Ad;
            public int Adet;
            public Sinif Oneri;
        }

        internal sealed class Ayar
        {
            public Dictionary<string, Sinif> Katman = new Dictionary<string, Sinif>(StringComparer.OrdinalIgnoreCase);
            /// <summary>1 cm'nin çizim birimi karşılığı (cm 1, mm 10, m 0.01).</summary>
            public double Birim = 1;
            public bool BalonEkle = true, BasamakNo = true, OlcuPatlat = true, OlcuEkle = true, AksTamamla = true, OlcuYok, KapamaAyikla = true;
            /// <summary>STA4 DWG okuyucusu (VectorDraw 2004) eski sürüm ister; STA örnekleri AutoCAD 2000 (AC1015).</summary>
            public DwgVersion Surum = DwgVersion.AC1015;
            /// <summary>true: STA katman adları (AKS, KOLON, DUVAR, MERDIVEN); false: BEYKENT katmanları.</summary>
            public bool StaKatman = true;
            /// <summary>Formdaki "Yalnız aks ve kolon" düğmesi: duvar, merdiven, kapı/pencere ve ölçü işlenmez; aks ve kolon dışındaki her şey silinir.</summary>
            public bool YalnizAksKolon;
            /// <summary>Kat pencereleri (x0, y0, x1, y1), seçim sırasıyla = alt kattan yukarı; aks adları bu sırayla taşınır.</summary>
            public List<double[]> Katlar;
        }

        internal sealed class Rapor
        {
            public readonly List<string> Satir = new List<string>();
            public readonly List<string> Uyari = new List<string>();
            public void Yaz(string f, params object[] a) => Satir.Add(a.Length == 0 ? f : string.Format(CultureInfo.InvariantCulture, f, a));
            public void Uyar(string f, params object[] a) => Uyari.Add(a.Length == 0 ? f : string.Format(CultureInfo.InvariantCulture, f, a));
            public string Metin(string baslik)
            {
                var l = new List<string> { baslik };
                l.AddRange(Satir.Select(s => "  " + s));
                if (Uyari.Count > 0) { l.Add("Kontrol edilecekler:"); l.AddRange(Uyari.Select(s => "  ! " + s)); }
                return string.Join(Environment.NewLine, l);
            }
        }

        public static void Run(bool komutSatiri)
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            ed.WriteMessage(Etiket + "Kat planlarını EN ALT KATTAN YUKARI sırayla pencereyle seçin (vaziyet, kesit, görünüş dışarıda kalsın); üst katlarda aynı yerdeki aks alttakinin adını alır. Bu çizim değişmez; sonuç ayrı dosyaya yazılır.");
            var pencereler = new List<Kutu>();
            bool tumu = false;
            for (int i = 1; ; i++)
            {
                var po = new PromptPointOptions(Etiket + i + ". kat planı penceresi ilk köşe" + (i == 1 ? " [Tumu]" : "") + " (Enter: bitir)") { AllowNone = true };
                if (i == 1) po.Keywords.Add("Tumu");
                var p1 = ed.GetPoint(po);
                if (p1.Status == PromptStatus.Keyword) { tumu = true; break; }
                if (p1.Status == PromptStatus.Cancel) return;
                if (p1.Status != PromptStatus.OK) break;
                var p2 = ed.GetCorner(new PromptCornerOptions(Etiket + "Karşı köşe: ", p1.Value));
                if (p2.Status != PromptStatus.OK) return;
                Point3d a = p1.Value.TransformBy(ed.CurrentUserCoordinateSystem), c = p2.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                var k = new Kutu(Math.Min(a.X, c.X), Math.Min(a.Y, c.Y), Math.Max(a.X, c.X), Math.Max(a.Y, c.Y));
                if (k.W < 1e-6 || k.H < 1e-6) { i--; continue; }
                pencereler.Add(k);
            }
            if (!tumu && pencereler.Count == 0) { ed.WriteMessage(Etiket + "Plan seçilmedi."); return; }

            string hedef = HedefYol(doc);
            var rapor = new Rapor();
            Database yan = null;
            Database onceki = HostApplicationServices.WorkingDatabase;
            try
            {
                yan = Ayikla(db, tumu ? null : pencereler, rapor);
                if (yan == null) { ed.WriteMessage(Etiket + "Seçilen pencerelerde nesne yok."); return; }
                HostApplicationServices.WorkingDatabase = yan;
                List<KatmanOzet> katmanlar = Hazirla(yan, rapor);
                var ayar = new Ayar { Birim = BirimBul(yan, katmanlar, tumu ? 0 : pencereler.Max(k => k.W)) };
                if (!tumu) ayar.Katlar = pencereler.Select(k => new[] { k.X0, k.Y0, k.X1, k.Y1 }).ToList();
                foreach (var k in katmanlar) ayar.Katman[k.Ad] = k.Oneri;
                AyarYukle(ayar);
                if (!komutSatiri)
                {
                    HostApplicationServices.WorkingDatabase = onceki;
                    using (var f = new MimariStaForm(katmanlar, ayar, hedef))
                    {
                        if (AcApp.ShowModalDialog(f) != System.Windows.Forms.DialogResult.OK) { ed.WriteMessage(Etiket + "İptal edildi."); return; }
                        hedef = f.Hedef;
                        AyarKaydet(ayar);
                    }
                    HostApplicationServices.WorkingDatabase = yan;
                }
                Uygula(yan, ayar, rapor);
                Temizle(yan, rapor);
                if (ayar.StaKatman) AsciiKatman(yan, rapor);
                Gorunum(yan);
                HostApplicationServices.WorkingDatabase = onceki;
                yan.SaveAs(hedef, ayar.Surum);
                rapor.Yaz("Dosya: {0}", hedef);
            }
            catch (System.Exception ex)
            {
                HostApplicationServices.WorkingDatabase = onceki;
                ed.WriteMessage(Etiket + "Tamamlanamadı: " + ex.Message);
                return;
            }
            finally
            {
                HostApplicationServices.WorkingDatabase = onceki;
                yan?.Dispose();
            }

            string metin = rapor.Metin("MIMARISTA — STA4CAD V15 DWG hazırlığı");
            ed.WriteMessage("\n" + metin.Replace(Environment.NewLine, "\n"));
            try { File.WriteAllText(Path.ChangeExtension(hedef, null) + "_rapor.txt", metin, System.Text.Encoding.UTF8); } catch (IOException) { } catch (UnauthorizedAccessException) { }

            if (komutSatiri) return;
            var ac = new PromptKeywordOptions(Etiket + "Hazırlanan dosya açılsın mı? [Evet/Hayir]", "Evet Hayir") { AllowNone = true };
            ac.Keywords.Default = "Evet";
            var cevap = ed.GetKeywords(ac);
            if (cevap.Status == PromptStatus.OK || cevap.Status == PromptStatus.None)
                if (cevap.Status == PromptStatus.None || cevap.StringResult == "Evet")
                    AcApp.DocumentManager.ExecuteInApplicationContext(o => DocumentCollectionExtension.Open(AcApp.DocumentManager, (string)o, false), hedef);
        }

        private const string AyarDosyasi = "mimarista.ini";

        internal static string SurumAdi(DwgVersion v)
        {
            switch (v)
            {
                case DwgVersion.AC1015: return "2000";
                case DwgVersion.AC1024: return "2010";
                case DwgVersion.AC1027: return "2013";
                case DwgVersion.AC1032: return "2018";
                default: return v.ToString();
            }
        }

        /// <summary>Formun son seçimleri (DWG sürümü, onay kutuları) ve katman başına seçilen STA4 karşılığı geri yüklenir.</summary>
        private static void AyarYukle(Ayar a)
        {
            var m = BaIni.Read(AyarDosyasi);
            // Eski "Surum" anahtarı (2010/2013/2018) okunmaz: STA o sürümleri açamıyor.
            int surum = BaIni.GetI(m, "SurumSta", (int)a.Surum);
            a.StaKatman = BaIni.GetB(m, "StaKatman", a.StaKatman);
            if (Enum.IsDefined(typeof(DwgVersion), surum)) a.Surum = (DwgVersion)surum;
            a.BalonEkle = BaIni.GetB(m, "BalonEkle", a.BalonEkle);
            a.BasamakNo = BaIni.GetB(m, "BasamakNo", a.BasamakNo);
            a.OlcuPatlat = BaIni.GetB(m, "OlcuPatlat", a.OlcuPatlat);
            a.OlcuEkle = BaIni.GetB(m, "OlcuEkle", a.OlcuEkle);
            a.AksTamamla = BaIni.GetB(m, "AksTamamla", a.AksTamamla);
            a.OlcuYok = BaIni.GetB(m, "OlcuYok", a.OlcuYok);
            a.KapamaAyikla = BaIni.GetB(m, "KapamaAyikla", a.KapamaAyikla);
            foreach (string ad in a.Katman.Keys.ToList())
                if (m.TryGetValue("K|" + ad, out string v) && int.TryParse(v, out int s) && Enum.IsDefined(typeof(Sinif), s)) a.Katman[ad] = (Sinif)s;
        }

        private static void AyarKaydet(Ayar a)
        {
            var eski = BaIni.Read(AyarDosyasi);
            var katman = eski.Where(kv => kv.Key.StartsWith("K|", StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in a.Katman) if (kv.Key.IndexOf('=') < 0) katman["K|" + kv.Key] = ((int)kv.Value).ToString(CultureInfo.InvariantCulture);
            var satir = new List<string>
            {
                "SurumSta=" + ((int)a.Surum).ToString(CultureInfo.InvariantCulture), "StaKatman=" + BaIni.B(a.StaKatman),
                "BalonEkle=" + BaIni.B(a.BalonEkle), "BasamakNo=" + BaIni.B(a.BasamakNo), "OlcuPatlat=" + BaIni.B(a.OlcuPatlat),
                "OlcuEkle=" + BaIni.B(a.OlcuEkle), "AksTamamla=" + BaIni.B(a.AksTamamla), "OlcuYok=" + BaIni.B(a.OlcuYok), "KapamaAyikla=" + BaIni.B(a.KapamaAyikla)
            };
            satir.AddRange(katman.Take(3000).Select(kv => kv.Key + "=" + kv.Value));
            BaIni.Write(AyarDosyasi, satir.ToArray());
        }

        private static string HedefYol(Document doc)
        {
            string ad = doc.Name, klasor;
            if (!string.IsNullOrEmpty(ad) && Path.IsPathRooted(ad)) klasor = Path.GetDirectoryName(ad);
            else klasor = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string kok = Path.Combine(klasor, Path.GetFileNameWithoutExtension(ad) + "_STA");
            string yol = kok + ".dwg";
            for (int i = 2; File.Exists(yol) && Kilitli(yol) && i < 50; i++) yol = kok + i + ".dwg";
            return yol;
        }

        private static bool Kilitli(string yol)
        {
            try { using (File.Open(yol, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return false; }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        // ───────────── 1. Plan ayıklama ─────────────

        /// <summary>Model uzayında merkezi pencerelerden birine düşen nesneleri ayrı veritabanına alır (pencere yoksa tümü).</summary>
        internal static Database Ayikla(Database db, List<Kutu> pencereler, Rapor r)
        {
            var ids = new ObjectIdCollection();
            int toplam = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null) continue;
                    toplam++;
                    if (pencereler == null) { ids.Add(id); continue; }
                    if (!Zarf(e, out Kutu k)) continue;
                    foreach (var p in pencereler)
                        if (p.Icerir(k.Cx, k.Cy) && k.W <= p.W * 1.05 && k.H <= p.H * 1.05) { ids.Add(id); break; }
                }
                tr.Commit();
            }
            if (ids.Count == 0) return null;
            r.Yaz("Plan seçimi: {0} pencere, {1} / {2} üst düzey nesne alındı.", pencereler == null ? "tüm çizim" : pencereler.Count.ToString(), ids.Count, toplam);
            // Database.Wblock çizimin taban noktasına göre öteler; koordinatlar aynen kalsın diye boş veritabanına kopyalanır
            // (mimari revizyonda STA4 modeliyle üst üste gelmesi için). Yalnız kullanılan tanımlar taşındığından temizdir.
            var yan = new Database(true, true);
            try
            {
                yan.Insunits = db.Insunits; yan.Measurement = db.Measurement; yan.Ltscale = db.Ltscale;
                using (var harita = new IdMapping())
                    db.WblockCloneObjects(ids, SymbolUtilityServices.GetBlockModelSpaceId(yan), harita, DuplicateRecordCloning.Replace, false);
            }
            catch { yan.Dispose(); throw; }
            return yan;
        }

        private static bool Zarf(Entity e, out Kutu k)
        {
            k = default(Kutu);
            try
            {
                Extents3d x = e.GeometricExtents;
                k = new Kutu(x.MinPoint.X, x.MinPoint.Y, x.MaxPoint.X, x.MaxPoint.Y);
                return true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return false; }
        }

        // ───────────── 2. Blok patlatma, gizli katman, düzleme ─────────────

        /// <summary>Katman kilitlerini açar, gizli katman nesnelerini siler, blokları iç içe patlatır, Z'yi sıfırlar; katman özetini döndürür.</summary>
        internal static List<KatmanOzet> Hazirla(Database db, Rapor r)
        {
            var gizli = new HashSet<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt)
                {
                    var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (l.IsOff || l.IsFrozen) gizli.Add(id);
                    if (l.IsLocked) { l.UpgradeOpen(); l.IsLocked = false; }
                }
                tr.Commit();
            }

            ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
            var atla = new HashSet<ObjectId>();
            int patlayan = 0, xref = 0, basarisiz = 0, gizliSilinen = 0;
            var refSinif = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference));
            for (int tur = 0; tur < 24; tur++)
            {
                var refs = new List<ObjectId>();
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                        if (id.ObjectClass.IsDerivedFrom(refSinif) && !atla.Contains(id)) refs.Add(id);
                    tr.Commit();
                }
                if (refs.Count == 0) break;
                for (int bas = 0; bas < refs.Count; bas += 400)
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);
                        for (int i = bas; i < Math.Min(refs.Count, bas + 400); i++)
                        {
                            var br = (BlockReference)tr.GetObject(refs[i], OpenMode.ForWrite);
                            if (gizli.Contains(br.LayerId) || !br.Visible) { br.Erase(); gizliSilinen++; continue; }
                            var tanim = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                            if (tanim.IsFromExternalReference || tanim.IsFromOverlayReference) { atla.Add(refs[i]); xref++; continue; }
                            var parcalar = new DBObjectCollection();
                            try { br.Explode(parcalar); }
                            catch (Autodesk.AutoCAD.Runtime.Exception)
                            {
                                foreach (DBObject o in parcalar) o.Dispose();
                                atla.Add(refs[i]); basarisiz++; continue;
                            }
                            foreach (ObjectId aid in br.AttributeCollection)
                            {
                                var ar = tr.GetObject(aid, OpenMode.ForRead) as AttributeReference;
                                if (ar == null || ar.Invisible || string.IsNullOrWhiteSpace(ar.TextString)) continue;
                                var t = new DBText();
                                t.SetDatabaseDefaults(db);
                                t.TextString = ar.TextString; t.Height = ar.Height; t.Rotation = ar.Rotation;
                                t.WidthFactor = ar.WidthFactor; t.TextStyleId = ar.TextStyleId; t.LayerId = ar.LayerId; t.Color = ar.Color;
                                t.Position = ar.Position;
                                if (ar.Justify != AttachmentPoint.BaseLeft) { t.Justify = ar.Justify; t.AlignmentPoint = ar.AlignmentPoint; }
                                parcalar.Add(t);
                            }
                            foreach (DBObject o in parcalar)
                            {
                                var e = o as Entity;
                                if (e == null || e is AttributeDefinition || !e.Visible || gizli.Contains(e.LayerId)) { o.Dispose(); continue; }
                                // Blok içindeki "0" katmanı ve ByBlock özellikleri patlatınca bloğun kendi katman/özelliğine döner.
                                if (e.Layer == "0") e.LayerId = br.LayerId;
                                if (e.Color.IsByBlock) e.Color = br.Color;
                                if (e.LinetypeId == db.ByBlockLinetype) e.LinetypeId = br.LinetypeId;
                                ms.AppendEntity(e);
                                tr.AddNewlyCreatedDBObject(e, true);
                            }
                            br.Erase();
                            patlayan++;
                        }
                        tr.Commit();
                    }
            }
            r.Yaz("Blok: {0} blok patlatıldı (iç içe dahil).", patlayan);
            if (xref > 0) r.Uyar("{0} XREF patlatılamadı; mimari dosyada XREF > Bind yapıp komutu yeniden çalıştırın.", xref);
            if (basarisiz > 0) r.Uyar("{0} blok patlatılamadı (farklı X/Y ölçekli ya da bozuk); çizimde blok olarak kaldı.", basarisiz);

            var sayim = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int duzlenen = 0, yaziCevrilen = 0;
            var yeni = new List<DBText>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);
                foreach (ObjectId id in ms)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null) continue;
                    if (gizli.Contains(e.LayerId) || !e.Visible) { e.UpgradeOpen(); e.Erase(); gizliSilinen++; continue; }
                    if (e is MText mt)
                    {
                        // STA örnek çizimlerinde yazılar düz yazıdır (aks adı, basamak no, ölçü); çok satırlı yazı parçalanır.
                        var parcalar = new DBObjectCollection();
                        try { mt.Explode(parcalar); }
                        catch (Autodesk.AutoCAD.Runtime.Exception) { foreach (DBObject x in parcalar) x.Dispose(); parcalar.Clear(); }
                        int eklenen = 0;
                        foreach (DBObject x in parcalar)
                        {
                            var t = x as DBText;
                            if (t == null || string.IsNullOrWhiteSpace(t.TextString)) { x.Dispose(); continue; }
                            t.LayerId = mt.LayerId;
                            yeni.Add(t); eklenen++;
                            sayim[e.Layer] = sayim.TryGetValue(e.Layer, out int cc) ? cc + 1 : 1;
                        }
                        if (eklenen > 0 || string.IsNullOrWhiteSpace(mt.Text)) { mt.UpgradeOpen(); mt.Erase(); yaziCevrilen++; continue; }
                    }
                    if (Duzle(e)) duzlenen++;
                    sayim[e.Layer] = sayim.TryGetValue(e.Layer, out int c) ? c + 1 : 1;
                }
                foreach (var t in yeni)
                {
                    if (Math.Abs(t.Position.Z) > 1e-9) t.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, -t.Position.Z)));
                    ms.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
                }
                tr.Commit();
            }
            if (gizliSilinen > 0) r.Yaz("Kapalı/dondurulmuş katman ve görünmez nesne: {0} nesne silindi.", gizliSilinen);
            if (yaziCevrilen > 0) r.Yaz("Yazı: {0} çok satırlı yazı (MTEXT) düz yazıya çevrildi.", yaziCevrilen);
            if (duzlenen > 0) r.Yaz("Z kotu: {0} nesne Z=0 düzlemine alındı.", duzlenen);
            return sayim.OrderByDescending(k => k.Value).Select(k => new KatmanOzet { Ad = k.Key, Adet = k.Value, Oneri = Oner(k.Key) }).ToList();
        }

        private static bool Duzle(Entity e)
        {
            const double tol = 1e-9;
            if (e is Line l)
            {
                if (Math.Abs(l.StartPoint.Z) < tol && Math.Abs(l.EndPoint.Z) < tol) return false;
                l.UpgradeOpen();
                l.StartPoint = new Point3d(l.StartPoint.X, l.StartPoint.Y, 0); l.EndPoint = new Point3d(l.EndPoint.X, l.EndPoint.Y, 0);
                return true;
            }
            if (e is Polyline pl)
            {
                if (Math.Abs(pl.Elevation) < tol || !pl.Normal.IsEqualTo(Vector3d.ZAxis)) return false;
                pl.UpgradeOpen(); pl.Elevation = 0; return true;
            }
            double z;
            if (e is Circle c && c.Normal.IsEqualTo(Vector3d.ZAxis)) z = c.Center.Z;
            else if (e is Arc a && a.Normal.IsEqualTo(Vector3d.ZAxis)) z = a.Center.Z;
            else if (e is DBText t && t.Normal.IsEqualTo(Vector3d.ZAxis)) z = t.Position.Z;
            else if (e is MText m && m.Normal.IsEqualTo(Vector3d.ZAxis)) z = m.Location.Z;
            else return false;
            if (Math.Abs(z) < tol) return false;
            e.UpgradeOpen();
            e.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, -z)));
            return true;
        }

        /// <summary>Kolon önerili katmanlardaki kapalı dörtgenlerin kısa kenarından çizim birimi.</summary>
        internal static double BirimBul(Database db, List<KatmanOzet> katmanlar, double planGenislik)
        {
            var kolonKatman = new HashSet<string>(katmanlar.Where(k => k.Oneri == Sinif.Kolon).Select(k => k.Ad), StringComparer.OrdinalIgnoreCase);
            var kisa = new List<double>();
            double x0 = double.MaxValue, x1 = double.MinValue;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                    if (pl == null || !kolonKatman.Contains(pl.Layer) || !Kapali(pl) || pl.NumberOfVertices > 5) continue;
                    if (!Zarf(pl, out Kutu k)) continue;
                    kisa.Add(Math.Min(k.W, k.H));
                    x0 = Math.Min(x0, k.X0); x1 = Math.Max(x1, k.X1);
                }
                tr.Commit();
            }
            return BirimTahmin(kisa, planGenislik);
        }

        private static bool Kapali(Polyline pl)
        {
            if (pl.NumberOfVertices < 3) return false;
            if (pl.Closed) return true;
            return pl.NumberOfVertices >= 4 && pl.GetPoint2dAt(0).GetDistanceTo(pl.GetPoint2dAt(pl.NumberOfVertices - 1)) < 1e-6 * Math.Max(1, pl.Length);
        }

        // ───────────── 3. Kuralların uygulanması ─────────────

        private sealed class Ortam
        {
            public Database Db;
            public Transaction Tr;
            public BlockTableRecord Ms;
            public Ayar A;
            public Rapor R;
            public double B;
            public readonly Dictionary<ObjectId, string> KatmanTipi = new Dictionary<ObjectId, string>();
            public readonly Dictionary<ObjectId, Sinif> KatmanSinif = new Dictionary<ObjectId, Sinif>();
            /// <summary>Kolon kapsamları x0, y0, x1, y1 (kat çakıştırma ve kolondan geçmeyen aks tamamlaması).</summary>
            public readonly List<double[]> KolonMerkez = new List<double[]>();

            public Sinif SinifOf(Entity e) => KatmanSinif.TryGetValue(e.LayerId, out Sinif s) ? s : Sinif.Diger;

            public bool Surekli(Entity e)
            {
                string ad = e.Linetype;
                if (string.Equals(ad, "ByLayer", StringComparison.OrdinalIgnoreCase)) KatmanTipi.TryGetValue(e.LayerId, out ad);
                return string.IsNullOrEmpty(ad) || ad.Equals("Continuous", StringComparison.OrdinalIgnoreCase) || ad.Equals("ByBlock", StringComparison.OrdinalIgnoreCase);
            }

            public ObjectId Ekle(Entity e, ObjectId katman)
            {
                e.LayerId = katman;
                ObjectId id = Ms.AppendEntity(e);
                Tr.AddNewlyCreatedDBObject(e, true);
                return id;
            }
        }

        internal static void Uygula(Database db, Ayar a, Rapor r)
        {
            KatmanAdlari(a.StaKatman);
            // Mimarlar cm çizip INSUNITS'i çoğu zaman mm bırakıyor; çıktıda bulunan birim yazılır.
            db.Insunits = a.Birim >= 5 ? UnitsValue.Millimeters : a.Birim <= 0.05 ? UnitsValue.Meters : UnitsValue.Centimeters;
            r.Yaz("Katman adları: {0}; DWG sürümü {1}.", a.StaKatman ? "STA (AKS, KOLON, DUVAR, MERDIVEN)" : "BEYKENT", SurumAdi(a.Surum));
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var o = new Ortam { Db = db, Tr = tr, A = a, R = r, B = a.Birim };
                o.Ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt)
                {
                    var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    o.KatmanTipi[id] = ((LinetypeTableRecord)tr.GetObject(l.LinetypeObjectId, OpenMode.ForRead)).Name;
                    o.KatmanSinif[id] = a.Katman.TryGetValue(l.Name, out Sinif s) ? s : Sinif.Diger;
                }
                r.Yaz("Çizim birimi: {0}.", BirimAdi(a.Birim));

                var hepsi = new List<Entity>();
                foreach (ObjectId id in o.Ms)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e != null) hepsi.Add(e);
                }

                // "Sil" katmanları en son silinir: aks balonu adı ve basamak numarası çoğu zaman yazı katmanındadır;
                // kullanılan yazılar önce AKS/MERDIVEN katmanına taşınır, kalanlar silinir.
                KolonUygula(o, hepsi);
                AksUygula(o, hepsi);
                if (a.YalnizAksKolon)
                {
                    // Kullanıcı isteği 15.11.2026: yalnız aks (balon, ad dahil) ve kolon (taraması dahil) kalır.
                    var kalan = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { KAks, KAksBalon, KAksYazi, KKolon, KKolonTarama };
                    int atilan = 0;
                    foreach (ObjectId id in o.Ms)
                    {
                        var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (e == null || e.IsErased || kalan.Contains(e.Layer)) continue;
                        e.UpgradeOpen(); e.Erase(); atilan++;
                    }
                    r.Yaz("Yalnız aks ve kolon: {0} başka nesne silindi (duvar, merdiven, kapı/pencere, ölçü işlenmedi).", atilan);
                }
                else
                {
                    DuvarUygula(o, hepsi);
                    KapiPencereUygula(o, hepsi);
                    MerdivenUygula(o, hepsi);
                    int silinen = 0;
                    foreach (var e in hepsi)
                        if (!e.IsErased && o.SinifOf(e) == Sinif.Sil) { e.UpgradeOpen(); e.Erase(); silinen++; }
                    if (silinen > 0) r.Yaz("Silinecek katmanlar: {0} nesne silindi.", silinen);
                    OlcuUygula(o);
                }
                CizgiOlcegiBir(db, tr, r);
                tr.Commit();
            }
        }

        /// <summary>Kullanıcı isteği 15.11.2026: her nesnenin çizgi tipi ölçeği (Linetype scale) 1; mimaride farklı değerler kalıyordu.</summary>
        private static void CizgiOlcegiBir(Database db, Transaction tr, Rapor r)
        {
            int n = 0;
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId btrId in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                if (btr.IsFromExternalReference || btr.IsDependent) continue;
                foreach (ObjectId id in btr)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null || e.IsErased || Math.Abs(e.LinetypeScale - 1) < 1e-9) continue;
                    e.UpgradeOpen(); e.LinetypeScale = 1; n++;
                }
            }
            if (n > 0) r.Yaz("Çizgi tipi ölçeği: {0} nesnede 1 yapıldı (genel LTSCALE {1:0.###}).", n, db.Ltscale);
        }

        private static ObjectId Katman(Ortam o, string ad, short renk, ObjectId cizgiTipi)
        {
            var lt = (LayerTable)o.Tr.GetObject(o.Db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(ad))
            {
                var var_ = (LayerTableRecord)o.Tr.GetObject(lt[ad], OpenMode.ForWrite);
                if (!cizgiTipi.IsNull) var_.LinetypeObjectId = cizgiTipi;
                var_.IsOff = false; var_.IsFrozen = false; var_.IsLocked = false;
                o.KatmanTipi[var_.ObjectId] = ((LinetypeTableRecord)o.Tr.GetObject(var_.LinetypeObjectId, OpenMode.ForRead)).Name;
                return var_.ObjectId;
            }
            // BEYKENT katman ayarı (renk, kalınlık) varsa onunla; yoksa verilen renkle.
            try { BeykentKatmanYukle.Ensure(o.Tr, o.Db, ad, renk); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
            lt = (LayerTable)o.Tr.GetObject(o.Db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(ad))
            {
                var y = (LayerTableRecord)o.Tr.GetObject(lt[ad], OpenMode.ForWrite);
                if (!cizgiTipi.IsNull) y.LinetypeObjectId = cizgiTipi;
                o.KatmanTipi[y.ObjectId] = ((LinetypeTableRecord)o.Tr.GetObject(y.LinetypeObjectId, OpenMode.ForRead)).Name;
                return y.ObjectId;
            }
            lt.UpgradeOpen();
            var l = new LayerTableRecord { Name = ad, Color = AcColor.FromColorIndex(ColorMethod.ByAci, renk) };
            l.LinetypeObjectId = cizgiTipi.IsNull ? o.Db.ContinuousLinetype : cizgiTipi;
            ObjectId id = lt.Add(l);
            o.Tr.AddNewlyCreatedDBObject(l, true);
            o.KatmanTipi[id] = ((LinetypeTableRecord)o.Tr.GetObject(l.LinetypeObjectId, OpenMode.ForRead)).Name;
            return id;
        }

        /// <summary>STA4 aks çizgi tipi (çizgi–nokta–çizgi): CENTER. Çizimde yoksa acad.lin/acadiso.lin'den, o da olmazsa elle tanımlanır.</summary>
        private static ObjectId CenterTipi(Ortam o)
        {
            var tt = (LinetypeTable)o.Tr.GetObject(o.Db.LinetypeTableId, OpenMode.ForRead);
            if (tt.Has("CENTER")) return tt["CENTER"];
            foreach (string dosya in new[] { "acad.lin", "acadiso.lin" })
            {
                try { o.Db.LoadLineTypeFile("CENTER", dosya); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                tt = (LinetypeTable)o.Tr.GetObject(o.Db.LinetypeTableId, OpenMode.ForRead);
                if (tt.Has("CENTER")) return tt["CENTER"];
            }
            tt.UpgradeOpen();
            var t = new LinetypeTableRecord { Name = "CENTER", AsciiDescription = "Center ____ _ ____ _ ____ _ ____ _", PatternLength = 2.0, NumDashes = 4 };
            t.SetDashLengthAt(0, 1.25); t.SetDashLengthAt(1, -0.25); t.SetDashLengthAt(2, 0.25); t.SetDashLengthAt(3, -0.25);
            ObjectId id = tt.Add(t);
            o.Tr.AddNewlyCreatedDBObject(t, true);
            return id;
        }

        private static bool Merkez(Entity e, out double x, out double y)
        {
            x = y = 0;
            if (!Zarf(e, out Kutu k)) return false;
            x = k.Cx; y = k.Cy; return true;
        }

        private static string YaziMetni(Entity e)
        {
            if (e is DBText t) return (t.TextString ?? "").Trim();
            if (e is MText m) return (m.Text ?? "").Trim();
            return null;
        }

        // ── Aks ──

        private static void AksUygula(Ortam o, List<Entity> hepsi)
        {
            double b = o.B;
            var aksNesne = hepsi.Where(e => !e.IsErased && o.SinifOf(e) == Sinif.Aks).ToList();
            if (aksNesne.Count == 0)
            {
                o.R.Uyar("Aks katmanı bulunamadı. STA4 aksları AKS/AXIS adlı katmandan tanır; formda aks katmanını \"Aks\" olarak işaretleyin.");
                return;
            }
            ObjectId center = CenterTipi(o);
            ObjectId katman = Katman(o, KAks, 1, center);
            ObjectId balonKatman = Katman(o, KAksBalon, 7, ObjectId.Null), yaziKatman = Katman(o, KAksYazi, 3, ObjectId.Null);

            var parcalar = new List<AksParca>();
            foreach (var e in aksNesne)
            {
                if (e is Line l) parcalar.Add(new AksParca { X0 = l.StartPoint.X, Y0 = l.StartPoint.Y, X1 = l.EndPoint.X, Y1 = l.EndPoint.Y, Kaynak = e });
                else if (e is Polyline pl && DuzCizgi(pl, 0.5 * b))
                {
                    Point2d p0 = pl.GetPoint2dAt(0), p1 = pl.GetPoint2dAt(pl.NumberOfVertices - 1);
                    parcalar.Add(new AksParca { X0 = p0.X, Y0 = p0.Y, X1 = p1.X, Y1 = p1.Y, Kaynak = e });
                }
            }

            // Balon: içinde kısa yazı bulunan daire (yarıçap 8–150 cm), hangi katmanda olursa olsun.
            var yazilar = new List<KeyValuePair<Entity, double[]>>();
            foreach (var e in hepsi)
            {
                if (e.IsErased) continue;
                string m = YaziMetni(e);
                if (m == null || m.Length == 0 || m.Length > 4) continue;
                if (Merkez(e, out double x, out double y)) yazilar.Add(new KeyValuePair<Entity, double[]>(e, new[] { x, y }));
                if (e is DBText dt) yazilar.Add(new KeyValuePair<Entity, double[]>(e, new[] { dt.AlignmentPoint.X, dt.AlignmentPoint.Y }));
            }
            double hucre = 300 * b;
            var izgara = new Dictionary<long, List<int>>();
            Func<double, double, long> anahtar = (x, y) => ((long)Math.Floor(x / hucre) << 32) ^ ((long)Math.Floor(y / hucre) & 0xffffffffL);
            for (int i = 0; i < yazilar.Count; i++)
            {
                long k = anahtar(yazilar[i].Value[0], yazilar[i].Value[1]);
                if (!izgara.TryGetValue(k, out var l)) izgara[k] = l = new List<int>();
                l.Add(i);
            }
            var balonlar = new List<Balon>();
            foreach (var e in hepsi)
            {
                var c = e as Circle;
                if (c == null || c.IsErased || c.Radius < 8 * b || c.Radius > 150 * b) continue;
                Entity yazi = null; double en = double.MaxValue;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (!izgara.TryGetValue(anahtar(c.Center.X + dx * hucre, c.Center.Y + dy * hucre), out var l)) continue;
                        foreach (int i in l)
                        {
                            double d = Math.Sqrt(Math.Pow(yazilar[i].Value[0] - c.Center.X, 2) + Math.Pow(yazilar[i].Value[1] - c.Center.Y, 2));
                            if (d < 0.8 * c.Radius && d < en) { en = d; yazi = yazilar[i].Key; }
                        }
                    }
                bool aksKatmani = o.SinifOf(c) == Sinif.Aks;
                if (yazi == null && !aksKatmani) continue;
                balonlar.Add(new Balon { X = c.Center.X, Y = c.Center.Y, R = c.Radius, Ad = yazi != null ? YaziMetni(yazi) : "", Kaynak = c, YaziKaynak = yazi });
            }
            // Eş merkezli çift daire tek balondur (dıştaki kalır).
            balonlar = balonlar.OrderByDescending(q => q.R)
                .Aggregate(new List<Balon>(), (l, q) => { if (!l.Any(p => Math.Abs(p.X - q.X) < 0.3 * p.R && Math.Abs(p.Y - q.Y) < 0.3 * p.R)) l.Add(q); return l; });

            List<Aks> akslar = AksKur(parcalar, balonlar, b);
            int katEslesen = 0;
            int eklenenAks = 0;
            int adUretilen = o.A.BalonEkle || o.A.AksTamamla ? AdUret(akslar, o.A.Katlar, 3 * b, o.KolonMerkez, o.A.AksTamamla, out katEslesen, out eklenenAks) : 0;
            double varsayilanR = balonlar.Where(q => q.Kullanildi).Select(q => q.R).DefaultIfEmpty(40 * b).OrderBy(v => v).ElementAt(balonlar.Count(q => q.Kullanildi) / 2);
            ObjectId yaziStili = o.Db.Textstyle;

            int yeniBalon = 0, birlesen = 0, uzayan = 0, yazilanAd = 0;
            var degisen = new HashSet<Entity>();
            foreach (var a in akslar)
            {
                foreach (var p in a.Parcalar)
                {
                    var e = (Entity)p.Kaynak;
                    if (!e.IsErased) { e.UpgradeOpen(); e.Erase(); }
                    degisen.Add(e);
                }
                var cizgi = new Line(new Point3d(a.X0, a.Y0, 0), new Point3d(a.X1, a.Y1, 0));
                cizgi.SetDatabaseDefaults(o.Db);
                cizgi.LinetypeId = center;
                cizgi.LinetypeScale = 1;
                o.Ekle(cizgi, katman);
                if (a.Birlesti) birlesen++;
                if (a.Uzadi) uzayan++;
                foreach (var bl in new[] { a.Bas, a.Son })
                {
                    if (bl == null) continue;
                    foreach (Entity e in new[] { bl.Kaynak as Entity, bl.YaziKaynak as Entity })
                        if (e != null && !e.IsErased) { e.UpgradeOpen(); e.LayerId = e is Circle ? balonKatman : yaziKatman; degisen.Add(e); }
                    // Yazısız mevcut balona aksın adı (karşı uçtaki balondan ya da üretilen) yazılır.
                    if (bl.YaziKaynak == null && o.A.BalonEkle && a.Ad != "") { BalonYazisi(o, yaziKatman, yaziStili, bl.X, bl.Y, bl.R, a.Ad); yazilanAd++; }
                }
                if (!o.A.BalonEkle) continue;
                for (int uc = 0; uc < 2; uc++)
                {
                    if (uc == 0 ? !a.BasYeni : !a.SonYeni) continue;
                    double rr = (uc == 0 ? a.Son : a.Bas)?.R ?? varsayilanR;
                    double yon = uc == 0 ? -1 : 1;
                    double cx = (uc == 0 ? a.X0 : a.X1) + yon * a.Ux * rr, cy = (uc == 0 ? a.Y0 : a.Y1) + yon * a.Uy * rr;
                    var daire = new Circle(new Point3d(cx, cy, 0), Vector3d.ZAxis, rr);
                    daire.SetDatabaseDefaults(o.Db);
                    o.Ekle(daire, balonKatman);
                    BalonYazisi(o, yaziKatman, yaziStili, cx, cy, rr, a.Ad);
                    yeniBalon++;
                }
            }
            int tasinan = 0;
            foreach (var e in aksNesne)
            {
                if (e.IsErased || degisen.Contains(e)) continue;
                e.UpgradeOpen(); e.LayerId = e is Circle ? balonKatman : e is DBText || e is MText ? yaziKatman : katman; tasinan++;
                if (e is Line || e is Polyline) { e.LinetypeId = o.Db.ByLayerLinetype; e.LinetypeScale = 1; }
            }
            int balonsuz = akslar.Count(a => a.Bas == null && a.Son == null);
            o.R.Yaz("Aks: {0} aks {1} katmanında CENTER çizgi tipiyle; {2} aks parçalardan birleştirildi, {3} aksın ucu balona uzatıldı/kırpıldı.", akslar.Count, KAks, birlesen, uzayan);
            if (yeniBalon > 0) o.R.Yaz("Aks balonu: {0} eksik uca balon eklendi{1}.", yeniBalon, adUretilen > 0 ? " (" + adUretilen + " aksa ad üretildi)" : "");
            if (eklenenAks > 0) o.R.Yaz("Aks: içinden aks geçmeyen kolonlar için {0} aks eklendi (ad komşu aksın asalı, örn. 2 ile 3 arası 2').", eklenenAks);
            if (katEslesen > 0) o.R.Yaz("Aks adı: katlar çakıştırıldı; üst katlarda {0} aks alt kattaki aynı yerdeki aksın adını aldı.", katEslesen);
            if (yazilanAd > 0) o.R.Yaz("Aks balonu: {0} yazısız balona aks adı yazıldı.", yazilanAd);
            int adli = akslar.Count(q => !q.AdUretildi && !q.Eklendi && q.Ad != "");
            if (adli > 0) o.R.Yaz("Aks adı: {0} aksın adı orijinal balondan alındı.", adli);
            if (adUretilen > 0) o.R.Uyar("{0} aksın balonunda ad yoktu; her planda soldan sağa 1, 2, 3… ve yukarıdan aşağıya A, B, C… verildi. Aks adlarını kontrol edin.", adUretilen);
            else if (balonsuz > 0 && !o.A.BalonEkle) o.R.Uyar("{0} aksın balonu yok; STA4 aks balonu ister.", balonsuz);
            int kisa = parcalar.Count(p => !degisen.Contains((Entity)p.Kaynak));
            if (kisa > 0 && akslar.Count == 0) o.R.Uyar("Aks katmanında {0} çizgi var ama aks boyunda (≥ 1 m) çizgi bulunamadı; çizim birimini kontrol edin.", kisa);
        }

        /// <summary>Tüm köşeleri ilk–son doğrusu üzerinde olan yaysız çoklu çizgi (tek aks parçası sayılır).</summary>
        private static void BalonYazisi(Ortam o, ObjectId katman, ObjectId stil, double x, double y, double r, string ad)
        {
            var yazi = new DBText();
            yazi.SetDatabaseDefaults(o.Db);
            yazi.TextStyleId = stil;
            yazi.TextString = ad; yazi.Height = r * (ad.Length > 2 ? 0.55 : 0.8);
            yazi.HorizontalMode = TextHorizontalMode.TextCenter; yazi.VerticalMode = TextVerticalMode.TextVerticalMid;
            yazi.AlignmentPoint = new Point3d(x, y, 0);
            o.Ekle(yazi, katman);
            yazi.AdjustAlignment(o.Db);
        }

        private static bool DuzCizgi(Polyline pl, double tol)
        {
            int n = pl.NumberOfVertices;
            if (n < 2 || pl.Closed) return false;
            Point2d a = pl.GetPoint2dAt(0), c = pl.GetPoint2dAt(n - 1);
            double boy = a.GetDistanceTo(c);
            if (boy < 1e-9) return false;
            for (int i = 0; i < n; i++)
            {
                if (i < n - 1 && Math.Abs(pl.GetBulgeAt(i)) > 1e-9) return false;
                Point2d p = pl.GetPoint2dAt(i);
                if (Math.Abs((c.X - a.X) * (p.Y - a.Y) - (c.Y - a.Y) * (p.X - a.X)) / boy > tol) return false;
            }
            return true;
        }

        // ── Kolon ──

        private static Polyline Cokgen(Ortam o, IList<double[]> p)
        {
            var pl = new Polyline();
            pl.SetDatabaseDefaults(o.Db);
            for (int i = 0; i < p.Count; i++) pl.AddVertexAt(i, new Point2d(p[i][0], p[i][1]), p[i].Length > 2 ? p[i][2] : 0, 0, 0);
            pl.Closed = true;
            return pl;
        }

        private static bool KolonBoyu(Kutu k, double b) => Math.Min(k.W, k.H) >= 10 * b && Math.Max(k.W, k.H) <= 2500 * b;

        private static void KolonUygula(Ortam o, List<Entity> hepsi)
        {
            double b = o.B;
            var nesne = hepsi.Where(e => !e.IsErased && o.SinifOf(e) == Sinif.Kolon).ToList();
            if (nesne.Count == 0)
            {
                o.R.Uyar("Kolon katmanı bulunamadı. STA4 kolonları Kolon/Column adlı katmandaki kapalı çokgenlerden tanır; formda kolon katmanını işaretleyin.");
                return;
            }
            ObjectId katman = Katman(o, KKolon, 5, ObjectId.Null);
            ObjectId tarama = ObjectId.Null;
            var kutular = new List<Kutu>();
            int hazir = 0, kapatilan = 0, cizgiden = 0, taramadan = 0, daireden = 0, atlanan = 0, kapama = 0;
            bool ayikla = o.A.KapamaAyikla;
            var zarflar = KatZarflari(o, nesne);
            // Kolon katmanındaki kapalı şekillerin kapsamları: kapama perdesinin ucu bir kolona değer.
            var sekiller = new List<Kutu>();
            foreach (var e in nesne)
            {
                if (e is Polyline q && Kapali(q) && Zarf(q, out Kutu kq)) sekiller.Add(kq);
                else if (e is Hatch h0 && h0.Normal.IsEqualTo(Vector3d.ZAxis))
                    for (int i = 0; i < h0.NumberOfLoops; i++) { var lp = TaramaSiniri(h0.GetLoopAt(i)); if (lp != null && lp.Count >= 3) sekiller.Add(Kutu.Of(lp)); }
            }
            // Kapama zinciri: kesin kapamalardan başlayıp ucu bir kolona ya da bulunmuş bir kapamaya değen ince düz şeritler
            // değişiklik kalmayana dek eklenir (kolonlar arası parça parça çizilmiş kapama, köşede dönen kapama).
            var kapamalar = new List<Kutu>();
            if (ayikla)
            {
                var adaylar = new List<List<double[]>>();
                foreach (var e in nesne)
                {
                    if (e is Polyline q && Kapali(q)) adaylar.Add(CokgenNoktalari(q));
                    else if (e is Hatch h0 && h0.Normal.IsEqualTo(Vector3d.ZAxis))
                        for (int i = 0; i < h0.NumberOfLoops; i++) { var lp = TaramaSiniri(h0.GetLoopAt(i)); if (lp != null && lp.Count >= 3) adaylar.Add(lp); }
                }
                var isaretli = new bool[adaylar.Count];
                for (int tur = 0; tur < 30; tur++)
                {
                    bool degisti = false;
                    for (int i = 0; i < adaylar.Count; i++)
                    {
                        if (isaretli[i] || !KapamaMi(adaylar[i], b, ZarfOf(o, zarflar, adaylar[i]), sekiller, kapamalar)) continue;
                        isaretli[i] = true; kapamalar.Add(Kutu.Of(adaylar[i])); degisti = true;
                    }
                    if (!degisti) break;
                }
            }
            Func<IList<double[]>, bool> kapamaMi = p => KapamaMi(p, b, ZarfOf(o, zarflar, p), sekiller, kapamalar);
            var yollar = new List<Yol>();

            foreach (var e in nesne)
            {
                if (e is Polyline pl)
                {
                    if (!o.Surekli(pl)) { atlanan++; continue; }
                    if (Kapali(pl))
                    {
                        if (ayikla && kapamaMi(CokgenNoktalari(pl))) { pl.UpgradeOpen(); pl.Erase(); kapama++; continue; }
                        if (!Zarf(pl, out Kutu k) || !KolonBoyu(k, b)) { atlanan++; continue; }
                        pl.UpgradeOpen();
                        if (!pl.Closed)
                        {
                            pl.RemoveVertexAt(pl.NumberOfVertices - 1);
                            pl.Closed = true; kapatilan++;
                        }
                        else hazir++;
                        pl.LayerId = katman; kutular.Add(k);
                    }
                    else
                    {
                        bool duz = true;
                        var n = new List<double[]>();
                        for (int i = 0; i < pl.NumberOfVertices; i++)
                        {
                            if (i < pl.NumberOfVertices - 1 && Math.Abs(pl.GetBulgeAt(i)) > 1e-9) { duz = false; break; }
                            Point2d p = pl.GetPoint2dAt(i); n.Add(new[] { p.X, p.Y });
                        }
                        if (duz && n.Count >= 2) yollar.Add(new Yol { Noktalar = n, Kaynak = e });
                    }
                }
                else if (e is Line l)
                {
                    if (!o.Surekli(l)) { atlanan++; continue; }
                    yollar.Add(new Yol { Noktalar = new List<double[]> { new[] { l.StartPoint.X, l.StartPoint.Y }, new[] { l.EndPoint.X, l.EndPoint.Y } }, Kaynak = e });
                }
                else if (e is Circle c)
                {
                    if (c.Radius < 5 * b || c.Radius > 150 * b || !o.Surekli(c)) continue;
                    // Yuvarlak kolon: iki yaylı kapalı çokgen.
                    var p = Cokgen(o, new[] { new[] { c.Center.X - c.Radius, c.Center.Y, 1.0 }, new[] { c.Center.X + c.Radius, c.Center.Y, 1.0 } });
                    o.Ekle(p, katman);
                    c.UpgradeOpen(); c.Erase(); daireden++;
                    kutular.Add(new Kutu(c.Center.X - c.Radius, c.Center.Y - c.Radius, c.Center.X + c.Radius, c.Center.Y + c.Radius));
                }
                else if (e is Solid s)
                {
                    var p = new List<double[]>();
                    foreach (short i in new short[] { 0, 1, 3, 2 })
                    {
                        Point3d q = s.GetPointAt(i);
                        if (p.Count == 0 || Math.Abs(p[p.Count - 1][0] - q.X) > 1e-9 || Math.Abs(p[p.Count - 1][1] - q.Y) > 1e-9) p.Add(new[] { q.X, q.Y });
                    }
                    Kutu k = Kutu.Of(p);
                    if (p.Count < 3 || !KolonBoyu(k, b)) continue;
                    o.Ekle(Cokgen(o, p), katman);
                    s.UpgradeOpen(); s.Erase(); taramadan++; kutular.Add(k);
                }
            }

            // Uçları değen çizgi/açık çoklu çizgilerden kapalı çevrim → tek kapalı çokgen.
            foreach (var c in CevrimBul(yollar, Math.Max(0.2 * b, 1e-6)))
            {
                Kutu k = Kutu.Of(c.Noktalar);
                if (ayikla && kapamaMi(c.Noktalar))
                {
                    foreach (var y in c.Yollar) { var e = (Entity)y.Kaynak; e.UpgradeOpen(); e.Erase(); }
                    kapama++; continue;
                }
                if (!KolonBoyu(k, b)) continue;
                o.Ekle(Cokgen(o, c.Noktalar), katman);
                foreach (var y in c.Yollar) { var e = (Entity)y.Kaynak; e.UpgradeOpen(); e.Erase(); }
                cizgiden++; kutular.Add(k);
            }

            // Tarama: sınırında kapalı çokgen yoksa sınırdan çokgen üretilir; tarama ayrı katmana alınır.
            foreach (var e in nesne)
            {
                var h = e as Hatch;
                if (h == null || h.IsErased) continue;
                bool kapamaTarama = false;
                if (h.Normal.IsEqualTo(Vector3d.ZAxis))
                    for (int i = 0; i < h.NumberOfLoops; i++)
                    {
                        List<double[]> p = TaramaSiniri(h.GetLoopAt(i));
                        if (p == null || p.Count < 3) continue;
                        if (ayikla && kapamaMi(p)) { kapamaTarama = true; continue; }
                        Kutu k = Kutu.Of(p);
                        if (!KolonBoyu(k, b) || kutular.Any(q => q.KenarFarki(k) <= 1.5 * b)) continue;
                        o.Ekle(Cokgen(o, p), katman);
                        taramadan++; kutular.Add(k);
                    }
                h.UpgradeOpen();
                if (kapamaTarama) { h.Erase(); continue; }
                if (tarama.IsNull) tarama = Katman(o, KKolonTarama, 8, ObjectId.Null);
                h.LayerId = tarama;
            }

            o.KolonMerkez.AddRange(kutular.Select(k => new[] { k.X0, k.Y0, k.X1, k.Y1 }));
            int toplam = hazir + kapatilan + cizgiden + taramadan + daireden;
            o.R.Yaz("Kolon: {0} kapalı çokgen {1} katmanında ({2} hazır, {3} açık çoklu çizgi kapatıldı, {4} çizgilerden birleştirildi, {5} tarama/dolgu sınırından, {6} daireden).",
                toplam, KKolon, hazir, kapatilan, cizgiden, taramadan, daireden);
            if (toplam == 0) o.R.Uyar("Kolon katmanında kapalı çokgen bulunamadı; kolonları kapalı çokgen çizip komutu yeniden çalıştırın.");
            if (kapama > 0) o.R.Yaz("Kolon: {0} kapama perdesi (ince uzun perde: kalınlık ≤ 35 cm; çok uzun, kat dış çevresinde ya da ucu kolona değen) kolon sayılmadı, silindi; bunlara aks eklenmez.", kapama);
            if (atlanan > 0) o.R.Yaz("Kolon katmanında {0} nesne kolon sayılmadı (kesikli çizgi ya da kolon ölçüsü dışında); kendi katmanında bırakıldı.", atlanan);
        }

        /// <summary>
        /// Kapama perdesi (kullanıcı isteği 14.11.2026): bodrum çevresindeki ince ve çok uzun perde kolon gibi çizilmez, ona aks
        /// atılmaz (aks bolluğu olur). Ölçüt çokgenin ortalama kalınlığı t = 2·alan/çevre ve boyu L = çevre/2:
        /// t ≤ 35 cm, L ≥ 20·t ve L ≥ 3 m (köşeyi dönen L/U biçimli kapama da yakalanır; 25×300 perde ya da kolon yakalanmaz).
        /// </summary>
        /// <summary>Kat dış zarfları: kolon katmanı nesnelerinin kapsamı, kat penceresine göre (pencere seçilmediyse tek zarf).</summary>
        private static List<Kutu> KatZarflari(Ortam o, List<Entity> nesne)
        {
            var katlar = o.A.Katlar;
            int n = katlar == null || katlar.Count == 0 ? 1 : katlar.Count;
            var x0 = Enumerable.Repeat(double.MaxValue, n).ToArray(); var y0 = Enumerable.Repeat(double.MaxValue, n).ToArray();
            var x1 = Enumerable.Repeat(double.MinValue, n).ToArray(); var y1 = Enumerable.Repeat(double.MinValue, n).ToArray();
            foreach (var e in nesne)
            {
                if (!Zarf(e, out Kutu k)) continue;
                int i = KatNo(katlar, k.Cx, k.Cy);
                if (i < 0) continue;
                x0[i] = Math.Min(x0[i], k.X0); y0[i] = Math.Min(y0[i], k.Y0); x1[i] = Math.Max(x1[i], k.X1); y1[i] = Math.Max(y1[i], k.Y1);
            }
            return Enumerable.Range(0, n).Select(i => x0[i] <= x1[i] ? new Kutu(x0[i], y0[i], x1[i], y1[i]) : new Kutu(0, 0, -1, -1)).ToList();
        }

        private static int KatNo(List<double[]> katlar, double x, double y)
        {
            if (katlar == null || katlar.Count == 0) return 0;
            for (int i = 0; i < katlar.Count; i++)
                if (x >= katlar[i][0] && x <= katlar[i][2] && y >= katlar[i][1] && y <= katlar[i][3]) return i;
            return -1;
        }

        private static Kutu? ZarfOf(Ortam o, List<Kutu> zarflar, IList<double[]> p)
        {
            double cx = p.Average(q => q[0]), cy = p.Average(q => q[1]);
            int i = KatNo(o.A.Katlar, cx, cy);
            if (i < 0 || i >= zarflar.Count || zarflar[i].W < 0) return null;
            return zarflar[i];
        }

        internal static bool KapamaMi(IList<double[]> p, double b) => KapamaMi(p, b, null, null, null);

        /// <param name="zarf">Katın dış zarfı: uzun kenarı zarfa 1 m'den yakın ince perde (boy ≥ 3 m ve ≥ 10 × kalınlık) de kapamadır.</param>
        /// <param name="sekiller">Kolon katmanındaki kapalı şekiller: ucu kalın bir şekle (kolon) değen ince perde de kapamadır.</param>
        /// <param name="kapamalar">Bulunmuş kapamalar: ucu bunlardan birine değen ince düz şerit de zincirin parçasıdır.</param>
        internal static bool KapamaMi(IList<double[]> p, double b, Kutu? zarf, List<Kutu> sekiller, List<Kutu> kapamalar)
        {
            if (p == null || p.Count < 3) return false;
            double alan = 0, cevre = 0;
            for (int i = 0; i < p.Count; i++)
            {
                double[] a = p[i], c = p[(i + 1) % p.Count];
                alan += a[0] * c[1] - c[0] * a[1];
                cevre += Math.Sqrt((c[0] - a[0]) * (c[0] - a[0]) + (c[1] - a[1]) * (c[1] - a[1]));
            }
            alan = Math.Abs(alan) / 2;
            if (cevre <= 0 || alan <= 0) return false;
            // En ve boy: w + h = çevre/2, w·h = alan (dikdörtgende kesin; ince uzun çokgende yaklaşık).
            double s = cevre / 2, d = s * s - 4 * alan;
            double t = d > 0 ? (s - Math.Sqrt(d)) / 2 : s / 2, L = d > 0 ? (s + Math.Sqrt(d)) / 2 : s / 2;
            if (t > 35 * b) return false;
            // Düz şerit: dış kutunun kısa kenarı kalınlığın en çok 3 katı (kolonu saran kapama ~2,5 kat). U/C biçimli çekirdek ve
            // şaft perdesi (bacaklı; 3,5 × kalınlık ve üstü) iç kurallarda elenir; dış çevredeki girintili kapama (C biçimli) ise kapamadır.
            Kutu kk = Kutu.Of(p.ToList());
            bool duz = Math.Min(kk.W, kk.H) <= 3 * t;
            if (duz && L >= 20 * t && L >= 300 * b) return true;                   // çok uzun düz ince perde: her yerde kapama
            Kutu k = Kutu.Of(p.ToList());
            bool yatay = k.W >= k.H;
            // (c) İki kolonun arasını dolduran düz ince perde (her iki ucu da kalın bir şekle değer): boyu ne olursa olsun kapamadır.
            // Normal kattaki taşıyıcı perde kolonun yerine geçer, iki kolon arasını doldurmaz.
            if (duz && L > 4 * t && L >= 80 * b && UcKolon(k, yatay, sekiller, kapamalar, b, true) && UcKolon(k, yatay, sekiller, kapamalar, b, false)) return true;
            // (d) Zincirin ucu: bir ucu bulunmuş bir kapamaya değen düz ince şerit (diğer ucu duvara açılır).
            if (duz && L > 4 * t && L >= 100 * b && kapamalar != null && (UcKolon(k, yatay, null, kapamalar, b, true) || UcKolon(k, yatay, null, kapamalar, b, false))) return true;
            // Çevre/kolon kuralları: en az 10 × kalınlık; boy çevrede en az 2,5 m (kolonlar arası kapama parçası ~2,75 m),
            // kolona değme kuralında en az 3 m (normal kattaki cephe perdesi ~2,4 m kapama sayılmaz).
            if (L < 10 * t || L < 250 * b) return false;
            // (a) Kat dış çevresinde: perdenin uzun doğrultusuna paralel zarf kenarına 1 m'den yakın.
            // Girintili kapama ~6 × kalınlık derin; cephedeki merdiven/asansör çekirdeği (U) 10 × üstü: çevre kuralında derinlik ≤ 7 × kalınlık.
            if (zarf != null && Math.Min(kk.W, kk.H) <= 7 * t)
            {
                Kutu z = zarf.Value;
                double pay = 100 * b;
                if (yatay ? (k.Y0 - z.Y0 <= pay || z.Y1 - k.Y1 <= pay) : (k.X0 - z.X0 <= pay || z.X1 - k.X1 <= pay)) return true;
            }
            if (L < 300 * b || !duz) return false;
            // (b) Kolonlar arasına çizilmiş uzun perde: bir ucu kalın bir şekle (kolon) 5 cm'den yakın.
            return UcKolon(k, yatay, sekiller, kapamalar, b, true) || UcKolon(k, yatay, sekiller, kapamalar, b, false);
        }

        /// <summary>Şeridin baş (bas = true: sol/alt) ya da son ucu, kalın bir şekle (kolon; kısa ≥ 10 cm, uzun/kısa ≤ 4) 5 cm'den yakın mı.</summary>
        private static bool UcKolon(Kutu k, bool yatay, List<Kutu> sekiller, List<Kutu> kapamalar, double b, bool bas)
        {
            double e = 5 * b;
            bool Deger(Kutu q)
            {
                bool enine = yatay ? (q.Y1 >= k.Y0 - e && q.Y0 <= k.Y1 + e) : (q.X1 >= k.X0 - e && q.X0 <= k.X1 + e);
                if (!enine) return false;
                double uc = yatay ? (bas ? k.X0 : k.X1) : (bas ? k.Y0 : k.Y1);
                double q0 = yatay ? q.X0 : q.Y0, q1 = yatay ? q.X1 : q.Y1;
                return uc >= q0 - e && uc <= q1 + e;
            }
            if (sekiller != null)
                foreach (var q in sekiller)
                {
                    if (q.KenarFarki(k) <= 1 * b) continue;                                   // kendisi
                    double kisa = Math.Min(q.W, q.H), uzun = Math.Max(q.W, q.H);
                    if (kisa < 10 * b || uzun > 4 * kisa) continue;                            // kolon değil (ince perde)
                    if (Deger(q)) return true;
                }
            if (kapamalar != null)
                foreach (var q in kapamalar)
                    if (q.KenarFarki(k) > 1 * b && Deger(q)) return true;                     // zincirdeki kapama (düz devam ya da köşe)
            return false;
        }

        private static List<double[]> CokgenNoktalari(Polyline pl)
        {
            var p = new List<double[]>();
            int n = pl.NumberOfVertices;
            if (!pl.Closed && n > 3) n--;   // son köşe ilkiyle aynı (kapalı sayılan açık çoklu çizgi)
            for (int i = 0; i < n; i++) { Point2d q = pl.GetPoint2dAt(i); p.Add(new[] { q.X, q.Y }); }
            return p;
        }

        private static List<double[]> TaramaSiniri(HatchLoop loop)
        {
            var p = new List<double[]>();
            if (loop.IsPolyline)
            {
                foreach (BulgeVertex v in loop.Polyline) p.Add(new[] { v.Vertex.X, v.Vertex.Y, v.Bulge });
            }
            else
            {
                foreach (Curve2d c in loop.Curves)
                {
                    var s = c as LineSegment2d;
                    if (s == null) return null;
                    p.Add(new[] { s.StartPoint.X, s.StartPoint.Y });
                }
            }
            if (p.Count >= 2 && Math.Abs(p[0][0] - p[p.Count - 1][0]) < 1e-9 && Math.Abs(p[0][1] - p[p.Count - 1][1]) < 1e-9) p.RemoveAt(p.Count - 1);
            return p;
        }

        // ── Duvar ──

        /// <summary>Kapı ve pencere çizimleri korunur ve KAPI / PENCERE katmanlarına toplanır (STA duvar boşluklarını bunlarla görür).</summary>
        private static void KapiPencereUygula(Ortam o, List<Entity> hepsi)
        {
            ObjectId kapi = ObjectId.Null, pencere = ObjectId.Null;
            int nk = 0, np = 0;
            foreach (var e in hepsi)
            {
                if (e.IsErased) continue;
                Sinif s = o.SinifOf(e);
                if (s == Sinif.Kapi) { if (kapi.IsNull) kapi = Katman(o, KKapi, 4, ObjectId.Null); e.UpgradeOpen(); e.LayerId = kapi; nk++; }
                else if (s == Sinif.Pencere) { if (pencere.IsNull) pencere = Katman(o, KPencere, 140, ObjectId.Null); e.UpgradeOpen(); e.LayerId = pencere; np++; }
            }
            if (nk + np > 0) o.R.Yaz("Kapı/pencere: {0} kapı nesnesi {1}, {2} pencere nesnesi {3} katmanında.", nk, KKapi, np, KPencere);
            else o.R.Uyar("Kapı/pencere katmanı seçilmedi; STA duvar boşluklarını kapı/pencere çizimiyle görür. Formda kapı ve pencere katmanlarını işaretleyin.");
        }

        private static void DuvarUygula(Ortam o, List<Entity> hepsi)
        {
            var nesne = hepsi.Where(e => !e.IsErased && o.SinifOf(e) == Sinif.Duvar).ToList();
            if (nesne.Count == 0)
            {
                o.R.Uyar("Duvar katmanı bulunamadı. STA4 duvarları Duvar/Wall adlı katmandan tanır (kiriş sistemi buna göre önerilir); formda duvar katmanını işaretleyin.");
                return;
            }
            ObjectId katman = Katman(o, KDuvar, 7, ObjectId.Null), tarama = ObjectId.Null;
            int cizgi = 0;
            foreach (var e in nesne)
            {
                e.UpgradeOpen();
                if (e is Hatch || e is Solid)
                {
                    if (tarama.IsNull) tarama = Katman(o, KDuvarTarama, 8, ObjectId.Null);
                    e.LayerId = tarama;
                }
                else { e.LayerId = katman; if (e is Curve) cizgi++; }
            }
            o.R.Yaz("Duvar: {0} duvar çizgisi {1} katmanında.", cizgi, KDuvar);
        }

        // ── Merdiven ──

        /// <summary>
        /// Merdiven (kullanıcı kuralları 14.11.2026): mimarideki her basamak çoğu zaman çift çizgidir (rıht + basamak burnu,
        /// farklı boyda da olabilir). Çiftler eşitlenip tek rıhta indirilir; çıkış hattına göre İKİNCİ çizgi kalır. Birbirine
        /// paralel kolların bütün basamakları aynı genişliktedir (sahanlık ayrı, rıht çizilmez). Bulunan merdivenin eski çizgileri
        /// silinir, BEYKENT merdiven tekniğiyle yeniden çizilir: MERDIVEN (BEYKENT) tek rıht + basamak no, MERDIVEN YAZI (BEYKENT)
        /// çıkış hattı/daire/üçgen, DOSEME SINIRI (BEYKENT) ters kollar arasındaki göz (orta boşluk). Çözülemeyen merdiven
        /// çizgileri CIZGI (BEYKENT) katmanına alınır (STA4 merdiven katmanında gürültü kalmasın).
        /// </summary>
        private static void MerdivenUygula(Ortam o, List<Entity> hepsi)
        {
            double b = o.B;
            var nesne = hepsi.Where(e => !e.IsErased && o.SinifOf(e) == Sinif.Merdiven).ToList();
            if (nesne.Count == 0)
            {
                o.R.Uyar("Merdiven katmanı bulunamadı. STA4 merdiveni Merdiven/Stairs/Ladder adlı katmandan tanır; formda merdiven katmanını işaretleyin.");
                return;
            }
            ObjectId katman = Katman(o, KMerdiven, 5, ObjectId.Null);
            var kaynak = new MimariBoslukAnaliz.Kaynak();
            var cizgiler = new List<MimariBoslukAnaliz.Parca>();
            foreach (var e in nesne)
            {
                if (e is Line l) CizgiEkle(cizgiler, l.StartPoint.X, l.StartPoint.Y, l.EndPoint.X, l.EndPoint.Y, b);
                else if (e is Polyline pl)
                {
                    int n = pl.NumberOfVertices;
                    for (int i = 0; i < (pl.Closed ? n : n - 1); i++)
                    {
                        if (Math.Abs(pl.GetBulgeAt(i)) > 1e-9) continue;
                        Point2d p0 = pl.GetPoint2dAt(i), p1 = pl.GetPoint2dAt((i + 1) % n);
                        CizgiEkle(cizgiler, p0.X, p0.Y, p1.X, p1.Y, b);
                    }
                }
            }
            int esitlenen = CiftEsitle(cizgiler);
            kaynak.Cizgiler.AddRange(cizgiler);
            // Basamak numaraları hangi katmanda olursa olsun okunur.
            var noYazi = new List<KeyValuePair<Entity, double[]>>();
            foreach (var e in hepsi)
            {
                if (e.IsErased) continue;
                string m = YaziMetni(e);
                if (m == null || m.Length == 0 || m.Length > 2 || !int.TryParse(m, NumberStyles.None, CultureInfo.InvariantCulture, out int no) || no < 1 || no > 80) continue;
                if (!Merkez(e, out double x, out double y)) continue;
                kaynak.Sayilar.Add(new MimariBoslukAnaliz.Yazi { Metin = no.ToString(CultureInfo.InvariantCulture), X = x / b, Y = y / b });
                noYazi.Add(new KeyValuePair<Entity, double[]>(e, new[] { x / b, y / b }));
            }

            List<MimariMerdivenAnaliz.Merdiven> merdivenler;
            try
            {
                // Kalıp kipi: mimari kipindeki "ilk/son çizgi kat kenarıdır" ayıklaması yapılmaz, bütün rıhtlar kalır.
                merdivenler = MimariMerdivenAnaliz.Bul(kaynak, new MimariBoslukAnaliz.Hizalama(), new List<MimariBoslukAnaliz.Parca>(),
                    new List<MimariBoslukAnaliz.Parca>(), (x, y) => true, false, CiftEnCok);
            }
            catch (System.Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is FormatException)
            {
                o.R.Uyar("Merdiven kolları çözümlenemedi ({0}); merdiveni elle kontrol edin.", ex.Message);
                merdivenler = new List<MimariMerdivenAnaliz.Merdiven>();
            }

            ObjectId yaziKatman = ObjectId.Null, sinirKatman = ObjectId.Null, cizgiKatman = ObjectId.Null;
            int numarali = 0, numaralanan = 0, yazilan = 0, tasinan = 0, yonBelirsiz = 0, cift = 0, rihtSayisi = 0, goz = 0, silinen = 0, genislik = 0, aralik = 0, donusRiht = 0;
            var basamaklar = new List<double>();
            var dolu = new List<MimariMerdivenAnaliz.Kol>();
            var cizilen = new List<MimariMerdivenAnaliz.Merdiven>();
            var donusler = new Dictionary<MimariMerdivenAnaliz.Merdiven, List<Donus>>();
            var donusYer = new List<string>();
            foreach (var m in merdivenler.OrderBy(q => q.Kollar.Any(k => !double.IsNaN(k.OrtNo)) ? 0 : 1).ThenByDescending(q => q.RihtSayisi))
            {
                // Aynı kolu ikinci kez bulan (çift çizgiden) kol atlanır.
                var kollar = m.Kollar.Where(k => !dolu.Any(d => Ortusur(d, k))).ToList();
                if (kollar.Count < m.Kollar.Count) { cift += m.Kollar.Count - kollar.Count; if (kollar.Count == 0) continue; m.Kollar = kollar; }
                dolu.AddRange(m.Kollar);
                genislik += GenislikEsitle(m.Kollar);
                aralik += BasamakEsitle(m.Kollar);
                basamaklar.Add(m.Kollar[0].Basamak);
                cizilen.Add(m);
                foreach (var k in m.Kollar) { k.BasSinir = k.S[0]; k.SonSinir = k.S[k.S.Count - 1]; }
                m.Yol = MimariMerdivenAnaliz.Yol(m.Kollar);
                GozKur(m);
                var liste = new List<Donus>();
                for (int i = 0; i + 1 < m.Kollar.Count; i++)
                {
                    var d = DonusBul(m.Kollar[i], m.Kollar[i + 1], cizgiler, m.Gozler);
                    if (d != null) { d.Sonra = i; liste.Add(d); donusRiht += d.Rihtler.Count; donusYer.Add(string.Format(CultureInfo.InvariantCulture, "({0:0},{1:0})", d.Bolge.Average(q => q[0]) * b, d.Bolge.Average(q => q[1]) * b)); }
                }
                donusler[m] = liste;
            }

            // Eski çizimi temizle (merdiven yeniden çizilecek):
            // - merdiven katmanı: her noktası kol alanında (uçlarda yarım basamak pay), gözde (25 cm pay, korkuluk/küpeşte) ya da
            //   dönüş bölgesinde kalan çizgiler (kırık kesit çizgisi, korkuluk, eski çift rıhtlar);
            // - başka katman: kol alanında ya da gözde kalan kaplama/korkuluk çizgileri. Yazı, ölçü, aks, kolon, duvar kalır.
            foreach (var e in hepsi)
            {
                if (e.IsErased || e is DBText || e is MText || e is Dimension) continue;
                Sinif sinif = o.SinifOf(e);
                bool merdiven = sinif == Sinif.Merdiven;
                if (!merdiven && sinif != Sinif.Diger && sinif != Sinif.Sil) continue;
                List<double[]> noktalar = Noktalar(e, b);
                if (noktalar == null) continue;
                bool icinde;
                if (merdiven)
                    icinde = cizilen.Any(m => noktalar.All(p => m.Kollar.Any(k => KolIcinde(k, p, 10, 1.0 * k.Basamak))
                                                             || m.Gozler.Any(g => GozIcinde(g, p, 1.6 * m.Kollar[0].Basamak, 10))
                                                             || donusler[m].Any(d => CokgenIcinde(d.Bolge, p, 3))));
                else
                    icinde = cizilen.Any(m => m.Kollar.Any(k => noktalar.All(p => KolIcinde(k, p, 3, 0)))
                                           || m.Gozler.Any(g => noktalar.All(p => CokgenIcinde(g, p, 15))));
                if (icinde) { e.UpgradeOpen(); e.Erase(); silinen++; }
            }

            foreach (var m in cizilen)
            {
                // Merdiven zarfındaki mevcut basamak numaraları rıht katmanına alınır.
                foreach (var y in noYazi)
                    if (m.K.Icerir(y.Value[0], y.Value[1], 40) && !y.Key.IsErased && y.Key.LayerId != katman)
                    { y.Key.UpgradeOpen(); y.Key.LayerId = katman; tasinan++; }

                foreach (var k in m.Kollar)
                    foreach (double s in k.S)
                    {
                        RihtCiz(o, k.P(k.A0, s), k.P(k.A1, s), b, katman);
                        rihtSayisi++;
                    }
                foreach (var d in donusler[m])
                    foreach (var r in d.Rihtler) { RihtCiz(o, r[0], r[1], b, katman); rihtSayisi++; }
                if (m.Gozler.Count > 0)
                {
                    if (sinirKatman.IsNull) sinirKatman = Katman(o, KSinir, 71, ObjectId.Null);
                    foreach (var g in m.Gozler) { o.Ekle(Cokgen(o, g.Select(p => new[] { p[0] * b, p[1] * b }).ToList()), sinirKatman); goz++; }
                }
                if (yaziKatman.IsNull) yaziKatman = Katman(o, KMerdivenYazi, 2, ObjectId.Null);
                CikisCiz(o, m.Yol, b, yaziKatman);

                double yukseklik = Math.Min(12, m.Kollar[0].Basamak * 0.45);
                if (m.Kollar.Any(k => !double.IsNaN(k.OrtNo)))
                {
                    numarali++;
                    // Numaralı merdivende dönüş basamaklarının eksik numarası: önceki kolun son basamağındaki numaradan sürdürülür.
                    if (!o.A.BasamakNo) continue;
                    foreach (var d in donusler[m])
                    {
                        var a = m.Kollar[d.Sonra];
                        if (a.S.Count < 2) continue;
                        // Son basamağın numarası: o basamağın rıhtları arasında (numara basamağın herhangi bir yerinde olabilir).
                        double sa = a.S[a.S.Count - 2], sb = a.S[a.S.Count - 1];
                        var bulunan = kaynak.Sayilar.Where(t =>
                        {
                            double u = t.X * a.Ux + t.Y * a.Uy, s = t.X * a.Nx + t.Y * a.Ny;
                            return s >= Math.Min(sa, sb) - 1 && s <= Math.Max(sa, sb) + 1 && u >= a.A0 - 10 && u <= a.A1 + 10;
                        }).FirstOrDefault();
                        if (bulunan == null || !int.TryParse(bulunan.Metin, out int v)) continue;
                        for (int j = 0; j < d.Etiket.Count; j++)
                        {
                            double[] p = d.Etiket[j];
                            if (kaynak.Sayilar.Any(t => Uzak(new[] { t.X, t.Y }, p) < 0.35 * a.W)) continue;
                            NumaraYaz(o, p, v + 1 + j, yukseklik, b, katman); yazilan++;
                        }
                    }
                    continue;
                }
                if (!o.A.BasamakNo) continue;
                // Numara iki rıht arasındaki basamağın ortasına; kollar ve dönüş basamakları boyunca kesintisiz.
                int no = 1;
                for (int ki = 0; ki < m.Kollar.Count; ki++)
                {
                    var k = m.Kollar[ki];
                    for (int i = 0; i + 1 < k.S.Count; i++) { NumaraYaz(o, k.P(k.Am, (k.S[i] + k.S[i + 1]) / 2), no++, yukseklik, b, katman); yazilan++; }
                    foreach (var d in donusler[m].Where(q => q.Sonra == ki))
                        foreach (var p in d.Etiket) { NumaraYaz(o, p, no++, yukseklik, b, katman); yazilan++; }
                }
                numaralanan++;
                if (!m.YonBelli) yonBelirsiz++;
            }
            if (donusRiht > 0) o.R.Yaz("Merdiven: dönüş bölgesinde {0} ışınsal rıht (dönerek çıkan basamak) çizildi; yer {1}.", donusRiht, string.Join(" ", donusYer));

            // Kalan (merdiven olmayan: asansör, korkuluk vb.) çizgiler silinir (kullanıcı kararı 14.11.2026). Hiç merdiven
            // çözülemediyse silinmez, CIZGI (BEYKENT) katmanına alınır: tanınmayan bir merdiven kaybolmasın.
            int kalan = 0, kalanSilinen = 0;
            foreach (var e in nesne)
            {
                if (e.IsErased || e.LayerId == katman) continue;
                e.UpgradeOpen();
                if (cizilen.Count > 0) { e.Erase(); kalanSilinen++; continue; }
                if (cizgiKatman.IsNull) cizgiKatman = Katman(o, KCizgi, 8, ObjectId.Null);
                e.LayerId = cizgiKatman; kalan++;
            }

            o.R.Yaz("Merdiven: {0} merdiven yeniden çizildi ({1} tek rıht, {2} göz sınırı); eski {3} çizgi silindi{4}.",
                cizilen.Count, rihtSayisi, goz, silinen, esitlenen > 0 ? ", " + esitlenen + " çift çizgi (rıht + basamak burnu) tek rıhta indirildi" : "");
            if (genislik > 0) o.R.Yaz("Merdiven: {0} kolun rıht boyu paralel kolla eşitlendi.", genislik);
            if (basamaklar.Count > 0) o.R.Yaz("Merdiven: basamak genişliği {0} cm{1}.", string.Join(", ", basamaklar.Select(v => v.ToString("0.#", CultureInfo.InvariantCulture))), aralik > 0 ? "; " + aralik + " kolda rıhtlar eşit aralığa oturtuldu" : "");
            int disinda = basamaklar.Count(v => v < 25 || v > 30);
            if (disinda > 0) o.R.Uyar("{0} merdivenin basamak genişliği 25–30 cm dışında; merdiven çizimini kontrol edin.", disinda);
            o.R.Yaz("Merdiven: {0} numaralı{1}.", numarali, numaralanan > 0 ? ", " + numaralanan + " merdivene " + yazilan + " basamak numarası yazıldı" : "");
            if (tasinan > 0) o.R.Yaz("Merdiven: {0} basamak numarası yazısı {1} katmanına alındı.", tasinan, KMerdiven);
            if (cift > 0) o.R.Yaz("Merdiven: {0} kol aynı merdivenin ikinci çizgisi sayıldı, tekrar çizilmedi.", cift);
            if (kalanSilinen > 0) o.R.Yaz("Merdiven katmanındaki merdiven olmayan {0} nesne (asansör, korkuluk vb.) silindi.", kalanSilinen);
            if (kalan > 0) o.R.Uyar("Merdiven katmanında merdiven çözülemedi; {0} nesne silinmedi, {1} katmanına alındı. Merdiveni elle kontrol edin.", kalan, KCizgi);
            if (merdivenler.Count == 0) o.R.Uyar("Merdiven katmanında basamak dizisi (en az 4 eşit aralıklı rıht) bulunamadı; merdiveni elle kontrol edin.");
            if (numaralanan > 0) o.R.Uyar("{0} merdivene basamak numarası yazıldı; çıkış yönü çizimden tahmin edildi{1}. Numaraların yönünü kontrol edin.",
                numaralanan, yonBelirsiz > 0 ? " (" + yonBelirsiz + " merdivende yön ipucu yoktu)" : "");
        }

        /// <summary>Rıht + basamak burnu en çok bu kadar arayla (cm) çift sayılır; basamak 22 cm'den küçük olamaz.</summary>
        private const double CiftEnCok = 12;

        /// <summary>
        /// Kural: bir merdivende birbirine paralel (aynı ya da ters yönlü) kolların bütün basamakları aynı genişliktedir.
        /// Dar kol en genişine tamamlanır; merdiven ortasına (göze) bakan kenar sabit kalır, dış (duvar) kenarı uzar.
        /// Sahanlık kol değildir, dokunulmaz. Döner: genişliği değişen kol sayısı.
        /// </summary>
        internal static int GenislikEsitle(List<MimariMerdivenAnaliz.Kol> kollar)
        {
            int sayi = 0;
            double mx = kollar.Average(k => k.P(k.Am, (k.S[0] + k.S[k.S.Count - 1]) / 2)[0]);
            double my = kollar.Average(k => k.P(k.Am, (k.S[0] + k.S[k.S.Count - 1]) / 2)[1]);
            foreach (var k in kollar)
            {
                var paralel = kollar.Where(q => Math.Abs(q.Ux * k.Uy - q.Uy * k.Ux) < 0.1).ToList();
                double w = paralel.Max(q => q.W);
                if (w - k.W < 1) continue;
                double orta = mx * k.Ux + my * k.Uy;   // merdiven ortasının kol enine izdüşümü
                if (Math.Abs(k.A0 - orta) <= Math.Abs(k.A1 - orta)) k.A1 = k.A0 + w; else k.A0 = k.A1 - w;
                sayi++;
            }
            return sayi;
        }

        /// <summary>
        /// Kural: bir merdivende birbirine paralel kolların basamak genişliği (rıht arası) aynıdır, genelde 25–30 cm; sahanlık ayrıdır.
        /// Ortak genişlik paralel kolların rıht aralıklarının ortancası; her kolun rıhtları en az kaydırmayla bu aralığa oturtulur
        /// (kayma 3 cm'yi geçerse kol olduğu gibi bırakılır). Döner: rıhtı kayan kol sayısı.
        /// </summary>
        internal static int BasamakEsitle(List<MimariMerdivenAnaliz.Kol> kollar)
        {
            int sayi = 0;
            foreach (var k in kollar)
            {
                var paralel = kollar.Where(q => Math.Abs(q.Ux * k.Uy - q.Uy * k.Ux) < 0.1).ToList();
                var araliklar = paralel.SelectMany(q => q.S.Zip(q.S.Skip(1), (x, y) => Math.Abs(y - x))).Where(v => v >= 22 && v <= 38).OrderBy(v => v).ToList();
                if (araliklar.Count == 0 || k.S.Count < 2) continue;
                double bas = araliklar[araliklar.Count / 2];
                double yon = k.S[k.S.Count - 1] >= k.S[0] ? 1 : -1;
                var r = k.S.Select((s, i) => s - (k.S[0] + yon * i * bas)).ToList();
                double ort = r.Average();
                if (r.Max(v => Math.Abs(v - ort)) > 3) continue;
                var yeni = k.S.Select((s, i) => k.S[0] + ort + yon * i * bas).ToList();
                if (yeni.Zip(k.S, (x, y) => Math.Abs(x - y)).Max() > 0.05) sayi++;
                k.S = yeni;
                k.Cift = k.S.Select(_ => 0.0).ToList();
                k.Basamak = bas;
            }
            return sayi;
        }

        /// <summary>
        /// Paralel, arası 1–12 cm ve boyca en az %60 örtüşen çizgi çiftinin uçları birleşimine eşitlenir: farklı boydaki
        /// rıht ile basamak burnu çekirdekte aynı kolun çifti olarak tanınsın (aksi hâlde iki ayrı kol, çift numara).
        /// </summary>
        internal static int CiftEsitle(List<MimariBoslukAnaliz.Parca> cizgiler)
        {
            var bilgi = cizgiler.Select(c =>
            {
                double dx = c.X1 - c.X0, dy = c.Y1 - c.Y0, L = Math.Sqrt(dx * dx + dy * dy);
                double aci = Math.Atan2(dy, dx); if (aci < 0) aci += Math.PI; if (aci >= Math.PI - 1e-6) aci -= Math.PI;
                return new { C = c, L, Aci = aci };
            }).Where(q => q.L >= 40).ToList();
            int sayi = 0;
            foreach (var grup in bilgi.GroupBy(q => (int)Math.Round(q.Aci / (0.5 * Math.PI / 180))))
            {
                double aci = grup.Average(q => q.Aci), ux = Math.Cos(aci), uy = Math.Sin(aci), nx = -uy, ny = ux;
                var l = grup.Select(q => new
                {
                    q.C,
                    S = (q.C.X0 + q.C.X1) / 2 * nx + (q.C.Y0 + q.C.Y1) / 2 * ny,
                    T0 = Math.Min(q.C.X0 * ux + q.C.Y0 * uy, q.C.X1 * ux + q.C.Y1 * uy),
                    T1 = Math.Max(q.C.X0 * ux + q.C.Y0 * uy, q.C.X1 * ux + q.C.Y1 * uy)
                }).OrderBy(q => q.S).ToList();
                var t0 = l.Select(q => q.T0).ToArray(); var t1 = l.Select(q => q.T1).ToArray();
                for (int i = 0; i < l.Count; i++)
                    for (int j = i + 1; j < l.Count && l[j].S - l[i].S <= CiftEnCok; j++)
                    {
                        if (l[j].S - l[i].S < 1) continue;
                        double ort = Math.Min(l[i].T1, l[j].T1) - Math.Max(l[i].T0, l[j].T0);
                        if (ort < 0.6 * Math.Min(l[i].T1 - l[i].T0, l[j].T1 - l[j].T0)) continue;
                        double a = Math.Min(t0[i], t0[j]), c = Math.Max(t1[i], t1[j]);
                        if (Math.Abs(a - t0[i]) + Math.Abs(c - t1[i]) + Math.Abs(a - t0[j]) + Math.Abs(c - t1[j]) > 0.5) sayi++;
                        t0[i] = t0[j] = a; t1[i] = t1[j] = c;
                    }
                for (int i = 0; i < l.Count; i++)
                {
                    var c = l[i].C;
                    c.X0 = ux * t0[i] + nx * l[i].S; c.Y0 = uy * t0[i] + ny * l[i].S;
                    c.X1 = ux * t1[i] + nx * l[i].S; c.Y1 = uy * t1[i] + ny * l[i].S;
                }
            }
            return sayi;
        }

        /// <summary>Nesnenin karakteristik noktaları (cm): çizgi/çoklu çizgi köşeleri, diğerlerinde kapsam köşeleri.</summary>
        private static List<double[]> Noktalar(Entity e, double b)
        {
            var p = new List<double[]>();
            if (e is Line l) { p.Add(new[] { l.StartPoint.X / b, l.StartPoint.Y / b }); p.Add(new[] { l.EndPoint.X / b, l.EndPoint.Y / b }); return p; }
            if (e is Polyline pl)
            {
                for (int i = 0; i < pl.NumberOfVertices; i++) { Point2d q = pl.GetPoint2dAt(i); p.Add(new[] { q.X / b, q.Y / b }); }
                return p;
            }
            if (!Zarf(e, out Kutu k)) return null;
            return new List<double[]> { new[] { k.X0 / b, k.Y0 / b }, new[] { k.X1 / b, k.Y1 / b }, new[] { k.X0 / b, k.Y1 / b }, new[] { k.X1 / b, k.Y0 / b } };
        }

        private static bool KolIcinde(MimariMerdivenAnaliz.Kol k, double[] p, double payA, double payS)
        {
            double a = p[0] * k.Ux + p[1] * k.Uy, s = p[0] * k.Nx + p[1] * k.Ny;
            double s0 = Math.Min(k.S[0], k.S[k.S.Count - 1]), s1 = Math.Max(k.S[0], k.S[k.S.Count - 1]);
            return a >= k.A0 - payA && a <= k.A1 + payA && s >= s0 - payS && s <= s1 + payS;
        }

        /// <summary>Göz çevresi: korkuluk gözün uçlarından taşar; uzun doğrultuda payUzun, enine payEn.</summary>
        private static bool GozIcinde(List<double[]> g, double[] p, double payUzun, double payEn)
        {
            var k = Kutu.Of(g);
            double px = k.W >= k.H ? payUzun : payEn, py = k.W >= k.H ? payEn : payUzun;
            return p[0] >= k.X0 - px && p[0] <= k.X1 + px && p[1] >= k.Y0 - py && p[1] <= k.Y1 + py;
        }

        private static bool CokgenIcinde(List<double[]> g, double[] p, double pay)
        {
            var k = Kutu.Of(g);
            return p[0] >= k.X0 - pay && p[0] <= k.X1 + pay && p[1] >= k.Y0 - pay && p[1] <= k.Y1 + pay;
        }

        /// <summary>Dönerek çıkan merdivenin dönüş bölgesi: ışınsal rıhtlar, basamak numarası yerleri ve temizlenecek alan (cm).</summary>
        internal sealed class Donus
        {
            public int Sonra;                                         // dönüşten önceki kolun sırası
            public readonly List<double[][]> Rihtler = new List<double[][]>();
            public readonly List<double[]> Etiket = new List<double[]>();
            public List<double[]> Bolge;
        }

        private static double Uzak(double[] p, double[] q) => Math.Sqrt((p[0] - q[0]) * (p[0] - q[0]) + (p[1] - q[1]) * (p[1] - q[1]));

        private static void RihtCiz(Ortam o, double[] a, double[] c, double b, ObjectId katman)
        {
            var ln = new Line(new Point3d(a[0] * b, a[1] * b, 0), new Point3d(c[0] * b, c[1] * b, 0));
            ln.SetDatabaseDefaults(o.Db);
            o.Ekle(ln, katman);
        }

        private static void NumaraYaz(Ortam o, double[] p, int no, double h, double b, ObjectId katman)
        {
            var t = new DBText();
            t.SetDatabaseDefaults(o.Db);
            t.TextStyleId = o.Db.Textstyle;
            t.TextString = no.ToString(CultureInfo.InvariantCulture);
            t.Height = h * b;
            t.HorizontalMode = TextHorizontalMode.TextCenter; t.VerticalMode = TextVerticalMode.TextVerticalMid;
            t.AlignmentPoint = new Point3d(p[0] * b, p[1] * b, 0);
            o.Ekle(t, katman);
            t.AdjustAlignment(o.Db);
        }

        /// <summary>
        /// Göz (kullanıcı kuralı 14.11.2026): ters yönlü kol çiftinin arasındaki 3–400 cm boşluk, iki uçta da en son rıhta
        /// (iki kolun rıhtlarının en dışına) kadar uzanır.
        /// </summary>
        internal static void GozKur(MimariMerdivenAnaliz.Merdiven m)
        {
            m.Gozler.Clear();
            int n = m.Kollar.Count;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    var a = m.Kollar[i]; var c = m.Kollar[j];
                    if (a.Nx * c.Nx + a.Ny * c.Ny > -0.9) continue;
                    var ck = new[] { c.P(c.A0, c.S[0]), c.P(c.A1, c.S[0]), c.P(c.A0, c.S[c.S.Count - 1]), c.P(c.A1, c.S[c.S.Count - 1]) };
                    double cu0 = ck.Min(p => p[0] * a.Ux + p[1] * a.Uy), cu1 = ck.Max(p => p[0] * a.Ux + p[1] * a.Uy);
                    double cs0 = ck.Min(p => p[0] * a.Nx + p[1] * a.Ny), cs1 = ck.Max(p => p[0] * a.Nx + p[1] * a.Ny);
                    double g0, g1;
                    if (cu0 >= a.A1 - 0.5) { g0 = a.A1; g1 = cu0; }
                    else if (cu1 <= a.A0 + 0.5) { g0 = cu1; g1 = a.A0; }
                    else continue;
                    double t0 = Math.Min(a.S.Min(), cs0), t1 = Math.Max(a.S.Max(), cs1);
                    if (g1 - g0 < 3 || g1 - g0 > 400) continue;
                    // Kollar boyca hiç örtüşmüyorsa (yan yana değil) göz yoktur.
                    if (Math.Min(a.S.Max(), cs1) - Math.Max(a.S.Min(), cs0) < 50) continue;
                    m.Gozler.Add(new List<double[]> { a.P(g0, t0), a.P(g1, t0), a.P(g1, t1), a.P(g0, t1) });
                }
        }

        /// <summary>
        /// Dönerek çıkan merdiven (kullanıcı isteği 14.11.2026): iki kol arasındaki dönüş bölgesinde rıhtlar bir merkezden
        /// (U merdivende gözün ucu, L merdivende iç köşe) ışınsal çıkar ve çoğu zaman çifttir. Bölgedeki, merkezden geçen ve
        /// tamamı bölgede kalan çizgiler rıhttır; çiftin çıkış yönüne göre ikincisi alınır. Gözün kenarındaki korkuluk gözün
        /// içinden başladığı, merdiveni kesen kırık çizgi merkezden geçmediği için rıht sayılmaz. Işınsal çizgi yoksa (düz
        /// sahanlık) null döner.
        /// </summary>
        private static Donus DonusBul(MimariMerdivenAnaliz.Kol a, MimariMerdivenAnaliz.Kol c, List<MimariBoslukAnaliz.Parca> cizgiler, List<List<double[]>> gozler)
        {
            double dot = a.Nx * c.Nx + a.Ny * c.Ny;
            if (dot > 0.3) return null;
            Func<double[], double> pu = p => p[0] * a.Ux + p[1] * a.Uy, ps = p => p[0] * a.Nx + p[1] * a.Ny;
            Func<double, double, double[]> xy = (u, s) => new[] { a.Ux * u + a.Nx * s, a.Uy * u + a.Ny * s };
            var ck = new[] { c.P(c.A0, c.S[0]), c.P(c.A1, c.S[0]), c.P(c.A0, c.S[c.S.Count - 1]), c.P(c.A1, c.S[c.S.Count - 1]) };
            double cu0 = ck.Min(pu), cu1 = ck.Max(pu), cs0 = ck.Min(ps), cs1 = ck.Max(ps);
            double aSon = a.S[a.S.Count - 1], w = Math.Min(a.W, c.W), wMax = Math.Max(a.W, c.W);
            double[] aR0 = a.P(a.A0, aSon), aR1 = a.P(a.A1, aSon), cR0 = c.P(c.A0, c.S[0]), cR1 = c.P(c.A1, c.S[0]);
            double u0, u1, s0, s1; double[] merkez;
            if (dot < -0.7)
            {
                double cIlk = ps(c.P(c.Am, c.S[0]));
                double uc = Math.Max(aSon, cIlk);
                double ai, ci;
                if (cu0 >= a.A1 - 1) { ai = a.A1; ci = cu0; }
                else if (cu1 <= a.A0 + 1) { ai = a.A0; ci = cu1; }
                else return null;
                merkez = xy((ai + ci) / 2, uc);
                u0 = Math.Min(a.A0, cu0) - 5; u1 = Math.Max(a.A1, cu1) + 5;
                s0 = Math.Min(aSon, cIlk) + 2; s1 = uc + 2.5 * wMax;   // dönüş derinliği: duvara kadar (genişliğin 2,5 katına dek)
            }
            else
            {
                double ai = (cu0 + cu1) / 2 > a.Am ? a.A1 : a.A0;
                merkez = xy(ai, cs0);
                u0 = a.A0 - 5; u1 = a.A1 + 5; s0 = aSon + 2; s1 = cs1 + 5;
            }
            var bolge = new List<double[]> { xy(u0, s0), xy(u1, s0), xy(u1, s1), xy(u0, s1) };
            bool Icinde(double[] p) { double u = pu(p), s = ps(p); return u >= u0 && u <= u1 && s >= s0 - 3 && s <= s1; }

            // Dönme yönü: önceki kolun son rıhtından, kol ilerisine (a.N) doğru dönerek sonraki kolun ilk rıhtına.
            double[] Dis(double[] p, double[] q) => Uzak(p, merkez) >= Uzak(q, merkez) ? p : q;
            double[] va = Dis(aR0, aR1), vc = Dis(cR0, cR1);
            double Aci(double[] p) => Math.Atan2(p[1] - merkez[1], p[0] - merkez[0]);
            double ta = Aci(va);
            double yon = Math.Sign((va[0] - merkez[0]) * a.Ny - (va[1] - merkez[1]) * a.Nx);
            if (yon == 0) yon = 1;
            double Param(double[] p) { double d = (Aci(p) - ta) * yon; while (d < 0) d += 2 * Math.PI; while (d >= 2 * Math.PI) d -= 2 * Math.PI; return d; }
            double tc = Param(vc);

            var aday = new List<(double t, double[] p0, double[] p1)>();
            foreach (var cz in cizgiler)
            {
                double[] p0 = { cz.X0, cz.Y0 }, p1 = { cz.X1, cz.Y1 };
                double L = Uzak(p0, p1);
                if (L < 0.4 * w || !Icinde(p0) || !Icinde(p1)) continue;
                // Ortası gözde kalan çizgi (korkuluk, göz kenarı) rıht değildir; ışınsal rıht gözün ucundan başlar ama ortası dışarıdadır.
                if (gozler.Any(g => CokgenIcinde(g, new[] { (p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2 }, 2))) continue;
                double dx = (p1[0] - p0[0]) / L, dy = (p1[1] - p0[1]) / L;
                // Kolların rıhtlarına paralel çizgi dönüş rıhtı değildir (sahanlık kenarı, rıhtın çift çizgisi).
                if (Math.Abs(dx * a.Nx + dy * a.Ny) < 0.1 || Math.Abs(dx * c.Nx + dy * c.Ny) < 0.1) continue;
                double uzaklik = Math.Abs((merkez[0] - p0[0]) * dy - (merkez[1] - p0[1]) * dx);
                if (uzaklik > 0.35 * w) continue;                                  // merkezden geçmeyen (kırık çizgi, sahanlık kenarı)
                double t = Param(Dis(p0, p1));
                if (t < 0.03 || t > tc - 0.03) continue;                           // kolların kendi uç rıhtları
                aday.Add((t, p0, p1));
            }
            if (aday.Count == 0) return null;
            // Çift ışınsal çizgi (4°'den yakın): çıkış yönünde ikincisi kalır.
            aday = aday.OrderBy(q => q.t).ToList();
            var tek = new List<(double t, double[] p0, double[] p1)>();
            foreach (var q in aday)
            {
                if (tek.Count > 0 && q.t - tek[tek.Count - 1].t < 4 * Math.PI / 180) tek[tek.Count - 1] = q;
                else tek.Add(q);
            }
            var d0 = new Donus { Bolge = bolge };
            foreach (var q in tek) d0.Rihtler.Add(new[] { q.p0, q.p1 });
            // Basamak numarası yerleri: [önceki kolun son rıhtı, dönüş rıhtları…, sonraki kolun ilk rıhtı] ardışık çiftlerinin ortası.
            var dizi = new List<double[][]> { new[] { aR0, aR1 } };
            dizi.AddRange(d0.Rihtler);
            dizi.Add(new[] { cR0, cR1 });
            for (int i = 0; i + 1 < dizi.Count; i++)
                d0.Etiket.Add(new[] { (dizi[i][0][0] + dizi[i][1][0] + dizi[i + 1][0][0] + dizi[i + 1][1][0]) / 4,
                                      (dizi[i][0][1] + dizi[i][1][1] + dizi[i + 1][0][1] + dizi[i + 1][1][1]) / 4 });
            return d0;
        }

        /// <summary>Çıkış hattı (BEYKENT merdiven tekniği): yol çoklu çizgisi, başta daire (alt kot), sonda üçgen (üst kot).</summary>
        private static void CikisCiz(Ortam o, List<double[]> yol, double b, ObjectId katman)
        {
            if (yol == null || yol.Count < 2) return;
            var pl = new Polyline(yol.Count);
            for (int i = 0; i < yol.Count; i++) pl.AddVertexAt(i, new Point2d(yol[i][0] * b, yol[i][1] * b), 0, 0, 0);
            pl.SetDatabaseDefaults(o.Db);
            o.Ekle(pl, katman);
            var c = new Circle(new Point3d(yol[0][0] * b, yol[0][1] * b, 0), Vector3d.ZAxis, 5 * b);
            c.SetDatabaseDefaults(o.Db);
            o.Ekle(c, katman);
            double[] p = yol[yol.Count - 1], q = yol[yol.Count - 2];
            double dx = p[0] - q[0], dy = p[1] - q[1], L = Math.Sqrt(dx * dx + dy * dy);
            if (L < 1e-6) return;
            dx /= L; dy /= L;
            var u = new Polyline(3);
            u.AddVertexAt(0, new Point2d((p[0] + dx * 8) * b, (p[1] + dy * 8) * b), 0, 0, 0);
            u.AddVertexAt(1, new Point2d((p[0] - dx * 4 - dy * 7) * b, (p[1] - dy * 4 + dx * 7) * b), 0, 0, 0);
            u.AddVertexAt(2, new Point2d((p[0] - dx * 4 + dy * 7) * b, (p[1] - dy * 4 - dx * 7) * b), 0, 0, 0);
            u.Closed = true;
            u.SetDatabaseDefaults(o.Db);
            o.Ekle(u, katman);
        }

        /// <summary>İki kol aynı doğrultuda ve kol genişliği ile boyunun yarısından fazlası üst üste mi.</summary>
        internal static bool Ortusur(MimariMerdivenAnaliz.Kol a, MimariMerdivenAnaliz.Kol k)
        {
            if (Math.Abs(a.Ux * k.Uy - a.Uy * k.Ux) > 0.1) return false;
            var koseler = new[] { k.P(k.A0, k.S[0]), k.P(k.A1, k.S[0]), k.P(k.A0, k.S[k.S.Count - 1]), k.P(k.A1, k.S[k.S.Count - 1]) };
            double ka0 = koseler.Min(p => p[0] * a.Ux + p[1] * a.Uy), ka1 = koseler.Max(p => p[0] * a.Ux + p[1] * a.Uy);
            double ks0 = koseler.Min(p => p[0] * a.Nx + p[1] * a.Ny), ks1 = koseler.Max(p => p[0] * a.Nx + p[1] * a.Ny);
            double as0 = Math.Min(a.S[0], a.S[a.S.Count - 1]), as1 = Math.Max(a.S[0], a.S[a.S.Count - 1]);
            double ortA = Math.Min(a.A1, ka1) - Math.Max(a.A0, ka0), ortS = Math.Min(as1, ks1) - Math.Max(as0, ks0);
            double boyA = Math.Min(a.A1 - a.A0, ka1 - ka0), boyS = Math.Max(a.Basamak, Math.Min(as1 - as0, ks1 - ks0));
            return ortA > 0.5 * boyA && ortS > 0.5 * boyS;
        }

        private static void CizgiEkle(List<MimariBoslukAnaliz.Parca> k, double x0, double y0, double x1, double y1, double b)
        {
            k.Add(new MimariBoslukAnaliz.Parca { X0 = x0 / b, Y0 = y0 / b, X1 = x1 / b, Y1 = y1 / b, Katman = KMerdiven });
        }

        // ── Ölçü ──

        /// <summary>
        /// STA4 çizim ölçeğini ölçülerden bulur. Ölçü nesneleri STA örneklerindeki gibi çizgi + sayı yazısına patlatılır;
        /// hiç ölçü yoksa komşu aksların arasına ölçü (çizgi + cm yazısı) eklenir.
        /// </summary>
        private static void OlcuUygula(Ortam o)
        {
            if (o.A.OlcuYok)
            {
                // Kullanıcı seçeneği: çizimde hiç ölçü olmasın (ölçü nesneleri ve ölçü katmanlarındaki her şey silinir, ölçü eklenmez).
                int sil = 0;
                foreach (ObjectId id in o.Ms)
                {
                    if (id.IsErased) continue;
                    var e = o.Tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null || !(e is Dimension || o.SinifOf(e) == Sinif.Olcu)) continue;
                    e.UpgradeOpen(); e.Erase(); sil++;
                }
                o.R.Yaz("Ölçü: 'Hiç ölçü olmasın' seçili; {0} ölçü nesnesi silindi, ölçü eklenmedi.", sil);
                o.R.Uyar("STA kuralı: çizim ölçeği ölçülerden bulunur. 'Hiç ölçü olmasın' seçiliyken STA ölçeği bulamayabilir; okumazsa bu seçeneği kaldırın.");
                return;
            }
            double b = o.B;
            var olculer = new List<Dimension>();
            var aksCizgi = new List<Line>();
            ObjectId aksKatman = ObjectId.Null;
            var lt = (LayerTable)o.Tr.GetObject(o.Db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(KAks)) aksKatman = lt[KAks];
            foreach (ObjectId id in o.Ms)
            {
                if (id.IsErased) continue;
                var e = o.Tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (e is Dimension d) olculer.Add(d);
                else if (e is Line l && l.LayerId == aksKatman && l.Length >= 400 * b) aksCizgi.Add(l);
            }
            if (olculer.Count > 0)
            {
                if (!o.A.OlcuPatlat) { o.R.Yaz("Ölçü: {0} ölçü nesnesi olduğu gibi bırakıldı.", olculer.Count); return; }
                int patlayan = 0;
                foreach (var d in olculer)
                {
                    var parcalar = new DBObjectCollection();
                    try { d.Explode(parcalar); }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { foreach (DBObject x in parcalar) x.Dispose(); continue; }
                    foreach (DBObject x in parcalar)
                    {
                        if (x is Line || x is Arc) { o.Ekle((Entity)x, d.LayerId); continue; }
                        if (x is MText mt && !string.IsNullOrWhiteSpace(mt.Text))
                        {
                            var t = new DBText();
                            t.SetDatabaseDefaults(o.Db);
                            t.TextStyleId = mt.TextStyleId; t.TextString = mt.Text.Trim(); t.Height = mt.TextHeight; t.Rotation = mt.Rotation;
                            t.HorizontalMode = TextHorizontalMode.TextCenter; t.VerticalMode = TextVerticalMode.TextVerticalMid;
                            Point3d yer = mt.Location;
                            try { Extents3d z = mt.GeometricExtents; yer = new Point3d((z.MinPoint.X + z.MaxPoint.X) / 2, (z.MinPoint.Y + z.MaxPoint.Y) / 2, 0); }
                            catch (Autodesk.AutoCAD.Runtime.Exception) { }
                            t.AlignmentPoint = yer;
                            o.Ekle(t, d.LayerId);
                            t.AdjustAlignment(o.Db);
                        }
                        x.Dispose();
                    }
                    d.UpgradeOpen(); d.Erase(); patlayan++;
                }
                o.R.Yaz("Ölçü: {0} ölçü nesnesi çizgi + yazıya patlatıldı (STA4 örnek çizim biçimi).", patlayan);
                return;
            }
            if (!o.A.OlcuEkle) { o.R.Uyar("Çizimde ölçü nesnesi yok; STA4 ölçeği ölçülerden bulur."); return; }
            if (aksCizgi.Count < 2) { o.R.Uyar("Çizimde ölçü nesnesi yok ve aks bulunamadığı için aks ölçüsü eklenemedi; STA4 ölçeği ölçülerden bulur."); return; }

            ObjectId katman = Katman(o, KOlcu, 2, ObjectId.Null);
            int eklenen = 0;
            foreach (bool dusey in new[] { true, false })
            {
                // Aynı doğrultudaki akslar konum sırasıyla; yalnız açıklıkları örtüşen komşular ölçülür (yan yana planlar arası ölçü çıkmaz).
                var grup = aksCizgi.Where(l => (Math.Abs(l.EndPoint.Y - l.StartPoint.Y) > Math.Abs(l.EndPoint.X - l.StartPoint.X)) == dusey
                        && (dusey ? Math.Abs(l.EndPoint.X - l.StartPoint.X) : Math.Abs(l.EndPoint.Y - l.StartPoint.Y)) < 0.01 * l.Length)
                    .OrderBy(l => dusey ? l.StartPoint.X : l.StartPoint.Y).ToList();
                for (int i = 0; i + 1 < grup.Count; i++)
                {
                    Line p = grup[i], q = grup[i + 1];
                    double p0 = dusey ? Math.Min(p.StartPoint.Y, p.EndPoint.Y) : Math.Min(p.StartPoint.X, p.EndPoint.X);
                    double p1 = dusey ? Math.Max(p.StartPoint.Y, p.EndPoint.Y) : Math.Max(p.StartPoint.X, p.EndPoint.X);
                    double q0 = dusey ? Math.Min(q.StartPoint.Y, q.EndPoint.Y) : Math.Min(q.StartPoint.X, q.EndPoint.X);
                    double q1 = dusey ? Math.Max(q.StartPoint.Y, q.EndPoint.Y) : Math.Max(q.StartPoint.X, q.EndPoint.X);
                    double ortak = Math.Min(p1, q1) - Math.Max(p0, q0);
                    double a = dusey ? p.StartPoint.X : p.StartPoint.Y, c = dusey ? q.StartPoint.X : q.StartPoint.Y;
                    double mesafe = c - a;
                    if (ortak < 0.5 * Math.Min(p1 - p0, q1 - q0) || mesafe < 5 * b || mesafe > 3000 * b) continue;
                    double hat = Math.Max(p0, q0) + 60 * b;       // aks başından 60 cm içeride ölçü çizgisi
                    Point3d u0 = dusey ? new Point3d(a, hat, 0) : new Point3d(hat, a, 0), u1 = dusey ? new Point3d(c, hat, 0) : new Point3d(hat, c, 0);
                    var cizgi = new Line(u0, u1);
                    cizgi.SetDatabaseDefaults(o.Db);
                    o.Ekle(cizgi, katman);
                    var t = new DBText();
                    t.SetDatabaseDefaults(o.Db);
                    t.TextStyleId = o.Db.Textstyle;
                    t.TextString = Math.Round(mesafe / b).ToString(CultureInfo.InvariantCulture);
                    t.Height = 15 * b; t.Rotation = dusey ? 0 : Math.PI / 2;
                    t.HorizontalMode = TextHorizontalMode.TextCenter; t.VerticalMode = TextVerticalMode.TextBottom;
                    t.AlignmentPoint = dusey ? new Point3d((a + c) / 2, hat + 4 * b, 0) : new Point3d(hat - 4 * b, (a + c) / 2, 0);
                    o.Ekle(t, katman);
                    t.AdjustAlignment(o.Db);
                    eklenen++;
                }
            }
            if (eklenen > 0) o.R.Yaz("Ölçü: çizimde ölçü nesnesi yoktu; {0} aks arası ölçü (cm) {1} katmanına eklendi.", eklenen, KOlcu);
            else o.R.Uyar("Çizimde ölçü nesnesi yok ve komşu aks bulunamadı; STA4 ölçeği ölçülerden bulur.");
        }

        /// <summary>
        /// STA kipi: STA4'ün DWG okuyucusu eski (VectorDraw 2004); Türkçe karakterli katman adları (med_ölçü, STC-ZEMİN) ASCII'ye
        /// çevrilir (ı→i, ş→s, ğ→g, ü→u, ö→o, ç→c). Aynı ada düşen katman varsa sonuna sayı eklenir.
        /// </summary>
        internal static void AsciiKatman(Database db, Rapor r)
        {
            int sayi = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                var adlar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var kayitlar = new List<LayerTableRecord>();
                foreach (ObjectId id in lt) { var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead); adlar.Add(l.Name); kayitlar.Add(l); }
                foreach (var l in kayitlar)
                {
                    if (l.Name.All(ch => ch < 128)) continue;
                    var sb = new System.Text.StringBuilder();
                    foreach (char ch in l.Name)
                        switch (ch)
                        {
                            case 'ı': sb.Append('i'); break; case 'İ': sb.Append('I'); break;
                            case 'ş': sb.Append('s'); break; case 'Ş': sb.Append('S'); break;
                            case 'ğ': sb.Append('g'); break; case 'Ğ': sb.Append('G'); break;
                            case 'ü': sb.Append('u'); break; case 'Ü': sb.Append('U'); break;
                            case 'ö': sb.Append('o'); break; case 'Ö': sb.Append('O'); break;
                            case 'ç': sb.Append('c'); break; case 'Ç': sb.Append('C'); break;
                            default: sb.Append(ch < 128 ? ch : '_'); break;
                        }
                    string yeni = sb.ToString(), ad = yeni;
                    for (int i = 2; adlar.Contains(ad); i++) ad = yeni + "_" + i;
                    l.UpgradeOpen(); adlar.Remove(l.Name); l.Name = ad; adlar.Add(ad); sayi++;
                }
                tr.Commit();
            }
            if (sayi > 0) r.Yaz("Katman adı: {0} Türkçe karakterli katman adı ASCII'ye çevrildi (STA okuyucusu için).", sayi);
        }

        /// <summary>Kayıtlı model görünümü çizimin tamamını ortalar: dosya açılınca plan ekranın ortasında görünür.</summary>
        internal static void Gorunum(Database db)
        {
            db.UpdateExt(true);
            Point3d a = db.Extmin, c = db.Extmax;
            if (a.X > c.X || a.Y > c.Y) return;
            db.TileMode = true;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var vt = (ViewportTable)tr.GetObject(db.ViewportTableId, OpenMode.ForRead);
                foreach (ObjectId id in vt)
                {
                    var v = (ViewportTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (!string.Equals(v.Name, "*Active", StringComparison.OrdinalIgnoreCase)) continue;
                    v.UpgradeOpen();
                    double w = Math.Max(c.X - a.X, 1e-6), h = Math.Max(c.Y - a.Y, 1e-6);
                    double oran = v.Width > 0 && v.Height > 0 ? v.Width / v.Height : 1.6;
                    v.ViewDirection = Vector3d.ZAxis; v.Target = Point3d.Origin; v.ViewTwist = 0;
                    v.CenterPoint = new Point2d((a.X + c.X) / 2, (a.Y + c.Y) / 2);
                    v.Height = Math.Max(h, w / oran) * 1.05;
                    v.Width = v.Height * oran;
                }
                tr.Commit();
            }
        }

        // ───────────── 4. Temizlik (PURGE) ─────────────

        internal static void Temizle(Database db, Rapor r)
        {
            int toplam = 0;
            for (int tur = 0; tur < 12; tur++)
            {
                var ids = new ObjectIdCollection();
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId tablo in new[] { db.BlockTableId, db.LayerTableId, db.LinetypeTableId, db.TextStyleTableId, db.DimStyleTableId })
                        foreach (ObjectId id in (SymbolTable)tr.GetObject(tablo, OpenMode.ForRead)) ids.Add(id);
                    tr.Commit();
                }
                db.Purge(ids);
                if (ids.Count == 0) break;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in ids)
                    {
                        try { tr.GetObject(id, OpenMode.ForWrite).Erase(); toplam++; }
                        catch (Autodesk.AutoCAD.Runtime.Exception) { }
                    }
                    tr.Commit();
                }
            }
            r.Yaz("Temizlik (PURGE): {0} kullanılmayan blok/katman/çizgi tipi/stil tanımı silindi.", toplam);
        }

        // ───────────── Test girişi (Core Console) ─────────────

        /// <summary>Diyalogsuz tam akış: pencereler (null = tümü), katman sınıfları öneriden; sonucu yazar ve raporu döndürür.</summary>
        internal static string Test(Database db, double[] pencere, string hedef, string sinifEk)
        {
            var kutular = new List<Kutu>();
            if (pencere != null) for (int i = 0; i + 3 < pencere.Length; i += 4) kutular.Add(new Kutu(pencere[i], pencere[i + 1], pencere[i + 2], pencere[i + 3]));
            var rapor = new Rapor();
            Database onceki = HostApplicationServices.WorkingDatabase;
            Database yan = Ayikla(db, kutular.Count == 0 ? null : kutular, rapor);
            if (yan == null) return "bos";
            try
            {
                HostApplicationServices.WorkingDatabase = yan;
                var katmanlar = Hazirla(yan, rapor);
                var ayar = new Ayar { Birim = BirimBul(yan, katmanlar, kutular.Count == 0 ? 0 : kutular.Max(k => k.W)) };
                if (kutular.Count > 0) ayar.Katlar = kutular.Select(k => new[] { k.X0, k.Y0, k.X1, k.Y1 }).ToList();
                if ((sinifEk ?? "").Split(';').Contains("olcuyok")) ayar.OlcuYok = true;
                if ((sinifEk ?? "").Split(';').Contains("r2018")) ayar.Surum = DwgVersion.AC1032;
                if ((sinifEk ?? "").Split(';').Contains("beykent")) ayar.StaKatman = false;
                if ((sinifEk ?? "").Split(';').Contains("kapamakalsin")) ayar.KapamaAyikla = false;
                if ((sinifEk ?? "").Split(';').Contains("akskolon")) ayar.YalnizAksKolon = true;
                foreach (var k in katmanlar) ayar.Katman[k.Ad] = k.Oneri;
                // "katman=Sinif;katman=Sinif" ile öneri ezilir (formdaki kullanıcı seçimi karşılığı).
                foreach (string s in (sinifEk ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int i = s.LastIndexOf('=');
                    if (i > 0) ayar.Katman[s.Substring(0, i)] = (Sinif)Enum.Parse(typeof(Sinif), s.Substring(i + 1));
                }
                // "form=<png>" verilirse form gösterilmeden çizilip kaydedilir (görsel kontrol).
                string formPng;
                if (ayar.Katman.TryGetValue("form", out _) == false && (formPng = (sinifEk ?? "").Split(';').FirstOrDefault(s => s.StartsWith("form>"))) != null)
                    using (var f = new MimariStaForm(katmanlar, ayar, hedef))
                    using (var bmp = new System.Drawing.Bitmap(f.Width, f.Height))
                    {
                        f.StartPosition = System.Windows.Forms.FormStartPosition.Manual; f.Location = new System.Drawing.Point(-4000, -4000);
                        f.Show(); System.Windows.Forms.Application.DoEvents();
                        f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                        bmp.Save(formPng.Substring(5), System.Drawing.Imaging.ImageFormat.Png);
                    }
                foreach (var k in katmanlar) rapor.Yaz("  katman {0} [{1}] → {2}", k.Ad, k.Adet, ayar.Katman[k.Ad]);
                Uygula(yan, ayar, rapor);
                Temizle(yan, rapor);
                if (ayar.StaKatman) AsciiKatman(yan, rapor);
                Gorunum(yan);
                HostApplicationServices.WorkingDatabase = onceki;
                yan.SaveAs(hedef, ayar.Surum);
            }
            finally
            {
                HostApplicationServices.WorkingDatabase = onceki;
                yan.Dispose();
            }
            return rapor.Metin("MIMARISTA test");
        }
    }
}
