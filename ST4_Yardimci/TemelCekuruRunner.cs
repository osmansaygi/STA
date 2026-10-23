using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Gömülü TEMEL_CUKURU.dwg. CIZGI ve KESIT GORUNUS (5 cm boşluk) çizilmez.
    /// Grobeton 10 cm, alt profil tek ZEMIN polyline. Hatıl genişliği ve subasman yüksekliği formdan.
    /// Formdaki yükseklik, grobeton açısı ve donatı yazıları şablondaki açıklama çizgilerinin yerini alır.
    /// Üst kenar (hatıl üstü, kot -2.50) sabitlenir; kesit aşağı büyür.
    /// </summary>
    internal static partial class TemelCekuruRunner
    {
        private const string EmbeddedResourceName = "ST4Yardimci.TemelCekuru.TEMEL_CUKURU.dwg";
        private const string LayerCizgi = "CIZGI (BEYKENT)";
        private const string LayerKesitGorunus = "KESIT GORUNUS (BEYKENT)";
        private const string LayerTemel = "TEMEL (BEYKENT)";
        private const string LayerZemin = "ZEMIN (BEYKENT)";
        private const string LayerHatil = "TEMEL HATILI (BEYKENT)";
        private const string LayerDoseme = "DOSEME (BEYKENT)";
        private const string LayerDonatiIzi = "DONATI IZI (BEYKENT)";
        private const string LayerBlokaj = "BLOKAJ (BEYKENT)";
        private const string LayerKotCizgi = "KOT CIZGISI (BEYKENT)";
        private const string LayerKotYazi = "KOT YAZI (BEYKENT)";
        private const string HatchArConc = "AR-CONC";
        private const double BlokajHatchScale = 0.1;
        /// <summary>BLOKAJ katmanı şeffaflık (0=opak … 90=en şeffaf) — Temel Hatılı ile aynı.</summary>
        private const int LayerBlokajTransparencyPercent = 90;
        private const string TextStyleYazi = "YAZI (BEYKENT)";
        private const string DimStylePlanOlcu25 = "PLAN_OLCU 25";
        private const double PlanOlcuDimExe = 5.0;
        /// <summary>Sağ kot tepesi, kazı dış yüzünden (çizim birimi).</summary>
        private const double KotRightGapDu = 40.0;
        /// <summary>Kot yazısından yan açılıma boşluk.</summary>
        private const double KotTextClearDu = 100.0;
        /// <summary>Sol açılımın keside en yakın noktası (şablon).</summary>
        private const double SideLeftMaxX = 1753.4;
        /// <summary>Sağ açılımın keside en yakın noktası (şablon).</summary>
        private const double SideRightMinX = 2773.9;
        /// <summary>Sol açılım ile kesit arası boşluk. Kot soldan kalktı.</summary>
        private const double SideLeftGapDu = 30.0;
        private const double XKotCenter = 2288.64717020909;

        private const double DuPerCm = 2.0;
        private const double AcilimTextGapDu = 2 * DuPerCm;
        /// <summary>Hatıl üstü.</summary>
        private const double Y0 = 1516.388795085607;
        /// <summary>Hatıl altı / perde (üst temel) üstü.</summary>
        private const double Y1 = 1416.388795085607;
        /// <summary>Geniş temel üstü.</summary>
        private const double Y2 = 1336.388795085607;
        /// <summary>Perde altı (temel yüksekliği alt kotu).</summary>
        private const double Y3 = 1216.388795085607;
        /// <summary>Temel altı.</summary>
        private const double Y4 = 1136.388795085607;
        /// <summary>Grobeton yan sehpası (temel üstünden 15 cm aşağı).</summary>
        private const double YBench = 1306.388795085607;
        /// <summary>Şablonda grobeton altı (temel altından 15 cm). Çizimde 10 cm'e çekilir.</summary>
        private const double YGro = 1106.388795085607;
        private const double TemplateGrobetonCm = 15.0;
        private const double GrobetonKalinlikCm = 10.0;
        /// <summary>Hatıl üstünden 10 cm aşağıdaki yatay çizgi.</summary>
        private const double YLip = 1496.388795085607;

        private const double XOutL = 1853.381774067395;
        private const double XSlopeL = 1953.381774067395;
        private const double XToeL = 1988.647170209089;
        private const double XToeR = 2588.647170209089;
        private const double XSlopeR = 2623.91256635078;
        private const double XOutR = 2723.91256635078;
        /// <summary>Perde iç yüzleri. Aralık 400 çizim birimi = 200 cm.</summary>
        private const double XInnerL = 2088.647170209089;
        private const double XInnerR = 2488.647170209089;
        /// <summary>Geniş temel düşey yüzü. İç yüzden 40 cm (şablon alt temel).</summary>
        private const double XStepL = 2008.647170209089;
        private const double XStepR = 2568.647170209089;
        /// <summary>Alt donatı dış çubuğu. Basamak yüzünden 4,5 cm.</summary>
        private const double RebarOutL = 2017.647170209089;
        private const double RebarOutR = 2559.647170209089;
        private const double TemplateRunDu = XToeL - XSlopeL;
        private const double BenchDu = 100.0;

        private static string _tempDwg;
        private static readonly object TempLock = new object();
        /// <summary>Temel detayı: tek kesit, sol/sağ sapma. Temel çukuru bunu açmaz.</summary>
        private static bool _detayMode;
        private static double _detayStemSol;
        private static double _detayStemSag;

        internal static void BeginDetay(double stemSolCm, double stemSagCm)
        {
            _detayMode = true;
            _detayStemSol = stemSolCm;
            _detayStemSag = stemSagCm;
        }

        internal static void EndDetay()
        {
            _detayMode = false;
        }

        public static void RunInteractive()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                TemelCekuruForm.Shutdown();
                return;
            }

            Editor ed = doc.Editor;
            try
            {
                TemelCekuruSettings.EnsureLoaded();
                if (TemelCekuruSettings.PaspayiCm * 2.0 + 2.0 > TemelCekuruSettings.AltTemelYuksekligiCm)
                {
                    ed.WriteMessage("\nTEMELCEKURU: paspayı alt temel yüksekliğine sığmıyor.");
                    return;
                }

                string path = EnsureTempDwg();
                if (string.IsNullOrEmpty(path))
                {
                    ed.WriteMessage("\nTEMELCEKURU: gömülü TEMEL_CUKURU.dwg bulunamadı.");
                    return;
                }

                var opt = new PromptPointOptions("\nTemel çukuru — sol üst köşe: ")
                {
                    AllowNone = false
                };
                PromptPointResult ppr = ed.GetPoint(opt);
                if (ppr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nTEMELCEKURU: iptal.");
                    return;
                }

                int n;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    double right;
                    int ust1, alt1, ust2, alt2;
                    Fit.PeekAdet(TemelCekuruSettings.TemelGenisligiCm, out ust1, out alt1);
                    Fit.PeekAdet(TemelCekuruSettings.DigerGenislikCm, out ust2, out alt2);
                    int yan1 = Fit.YanAcilimAdet(TemelCekuruSettings.DigerGenislikCm);
                    int yan2 = Fit.YanAcilimAdet(TemelCekuruSettings.TemelGenisligiCm);
                    n = Insert(tr, doc.Database, path, ppr.Value, TemelCekuruSettings.TemelGenisligiCm, 1, ust2, alt2, yan1, 0, out right);
                    double gap = 100.0 * DuPerCm;
                    var pick2 = new Point3d(right + gap, ppr.Value.Y, 0);
                    double ignored;
                    n += Insert(tr, doc.Database, path, pick2, TemelCekuruSettings.DigerGenislikCm, 2, ust1, alt1, yan2, 0, out ignored);
                    tr.Commit();
                }

                ed.WriteMessage(
                    "\nTEMELCEKURU: hatıl {0:0.#} cm, üst {1:0.#} cm, çukur {2:0.#} cm, alt {3:0.#} cm, genişlik {4:0.#} cm, paspayı {5:0.#} cm, grobeton {6:0.#}° ({7} nesne).",
                    TemelCekuruSettings.HatilYuksekligiCm,
                    TemelCekuruSettings.UstTemelYuksekligiCm,
                    TemelCekuruSettings.CukurDerinligiCm,
                    TemelCekuruSettings.AltTemelYuksekligiCm,
                    TemelCekuruSettings.TemelGenisligiCm,
                    TemelCekuruSettings.PaspayiCm,
                    TemelCekuruSettings.GrobetonAcisi,
                    n);
            }
            catch (System.Exception ex)
            {
                try { ed.WriteMessage("\nTEMELCEKURU hata: {0}", ex.Message); }
                catch { }
            }
            finally
            {
                TemelCekuruForm.Shutdown();
            }
        }

        /// <summary>Temel detayı — tek kesit. Sol ve sağ çukur yüksekliği ayrı. Ayarlar çizim bitince geri alınır.</summary>
        public static void RunDetay()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                TemelDetayForm.Shutdown();
                return;
            }

            Editor ed = doc.Editor;
            TemelCekuruSettings.Snapshot snap = TemelCekuruSettings.Capture();
            try
            {
                TemelDetaySettings.EnsureLoaded();
                if (!TemelDetaySettings.SolKademe && !TemelDetaySettings.SagKademe)
                {
                    ed.WriteMessage("\nTEMELDETAY: sol veya sağ kademeden en az birini seçin.");
                    return;
                }
                TemelCekuruSettings.GrobetonAcisi = TemelDetaySettings.GrobetonAcisi;
                TemelCekuruSettings.HatilYuksekligiCm = TemelDetaySettings.HatilYuksekligiCm;
                TemelCekuruSettings.HatilGenisligiCm = TemelDetaySettings.HatilGenisligiCm;
                TemelCekuruSettings.SubasmanYuksekligiCm = TemelDetaySettings.SubasmanYuksekligiCm;
                TemelCekuruSettings.UstTemelYuksekligiCm = TemelDetaySettings.UstTemelYuksekligiCm;
                TemelCekuruSettings.AltTemelYuksekligiCm = TemelDetaySettings.AltTemelYuksekligiCm;
                TemelCekuruSettings.TemelGenisligiCm = TemelDetaySettings.TemelGenisligiCm;
                // Tek kesit: diğer doğrultuda genişlik/adet kullanılmaz.
                TemelCekuruSettings.PaspayiCm = TemelDetaySettings.PaspayiCm;
                TemelCekuruSettings.TemelUstKotuCm = TemelDetaySettings.TemelUstKotuCm;
                TemelCekuruSettings.UstCapMm = TemelDetaySettings.UstCapMm;
                TemelCekuruSettings.UstAralikCm = TemelDetaySettings.UstAralikCm;
                TemelCekuruSettings.AltCapMm = TemelDetaySettings.AltCapMm;
                TemelCekuruSettings.AltAralikCm = TemelDetaySettings.AltAralikCm;
                double sol = TemelDetaySettings.SolKademe ? TemelDetaySettings.SolCukurCm : 0;
                double sag = TemelDetaySettings.SagKademe ? TemelDetaySettings.SagCukurCm : 0;
                double deep = sol > sag ? sol : sag;
                TemelCekuruSettings.CukurDerinligiCm = deep;

                if (TemelCekuruSettings.PaspayiCm * 2.0 + 2.0 > TemelCekuruSettings.AltTemelYuksekligiCm)
                {
                    ed.WriteMessage("\nTEMELDETAY: paspayı alt temel yüksekliğine sığmıyor.");
                    return;
                }

                string path = EnsureTempDwg();
                if (string.IsNullOrEmpty(path))
                {
                    ed.WriteMessage("\nTEMELDETAY: gömülü TEMEL_CUKURU.dwg bulunamadı.");
                    return;
                }

                var opt = new PromptPointOptions("\nTemel detayı — sol üst köşe: ")
                {
                    AllowNone = false
                };
                PromptPointResult ppr = ed.GetPoint(opt);
                if (ppr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nTEMELDETAY: iptal.");
                    return;
                }

                double stemSol = sol;
                double stemSag = sag;


                int n;
                BeginDetay(stemSol, stemSag);
                try
                {
                    using (doc.LockDocument())
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        int ust2 = 1, alt2 = 1, yan1 = 1;
                        double right;
                        n = Insert(tr, doc.Database, path, ppr.Value, TemelCekuruSettings.TemelGenisligiCm, 1, ust2, alt2, yan1, 0, out right);
                        tr.Commit();
                    }
                }
                finally
                {
                    EndDetay();
                }

                ed.WriteMessage(
                    "\nTEMELDETAY: sol çukur {0:0.#} cm, sağ çukur {1:0.#} cm, genişlik {2:0.#} cm ({3} nesne).",
                    sol, sag, TemelCekuruSettings.TemelGenisligiCm, n);
            }
            catch (System.Exception ex)
            {
                try { ed.WriteMessage("\nTEMELDETAY hata: {0}", ex.Message); }
                catch { }
            }
            finally
            {
                EndDetay();
                TemelCekuruSettings.Restore(snap);
                TemelDetayForm.Shutdown();
            }
        }

        private static int Insert(Transaction tr, Database destDb, string path, Point3d pick, double genislikCm, int kesitNo, int karsiUst, int karsiAlt, int yanAdet, int carpan, out double placedMaxX)
        {
            var bt = (BlockTable)tr.GetObject(destDb.BlockTableId, OpenMode.ForRead);
            var msDest = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            using (var src = new Database(false, true))
            {
                src.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
                Database prev = HostApplicationServices.WorkingDatabase;
                HostApplicationServices.WorkingDatabase = src;
                ObjectIdCollection ids;
                try
                {
                    using (var trs = src.TransactionManager.StartTransaction())
                    {
                        var btSrc = (BlockTable)trs.GetObject(src.BlockTableId, OpenMode.ForRead);
                        var ms = (BlockTableRecord)trs.GetObject(btSrc[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        var fit = _detayMode
                            ? Fit.FromDetay(genislikCm, kesitNo, karsiUst, karsiAlt, yanAdet, carpan, _detayStemSol, _detayStemSag)
                            : Fit.FromSettings(genislikCm, kesitNo, karsiUst, karsiAlt, yanAdet, carpan);
                        fit.Apply(trs, ms);
                        ids = new ObjectIdCollection();
                        foreach (ObjectId id in ms)
                        {
                            if (!id.IsValid || id.IsErased) continue;
                            if (trs.GetObject(id, OpenMode.ForRead) is Entity)
                                ids.Add(id);
                        }
                        trs.Commit();
                    }
                }
                finally
                {
                    HostApplicationServices.WorkingDatabase = prev;
                }

                placedMaxX = pick.X;
                if (ids.Count == 0)
                    return 0;

                var mapping = new IdMapping();
                destDb.WblockCloneObjects(ids, msDest.ObjectId, mapping, DuplicateRecordCloning.Ignore, false);

                var cloned = new List<Entity>();
                double minX = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                bool any = false;
                foreach (IdPair pair in mapping)
                {
                    if (!pair.IsCloned || !pair.Value.IsValid) continue;
                    if (!(tr.GetObject(pair.Value, OpenMode.ForWrite, false) is Entity ent)) continue;
                    if (ent.IsErased) continue;
                    cloned.Add(ent);
                    try
                    {
                        Extents3d e = ent.GeometricExtents;
                        if (e.MinPoint.X < minX) minX = e.MinPoint.X;
                        if (e.MaxPoint.X > maxX) maxX = e.MaxPoint.X;
                        if (e.MaxPoint.Y > maxY) maxY = e.MaxPoint.Y;
                        any = true;
                    }
                    catch { }
                }

                if (cloned.Count > 0)
                {
                    ObjectId dimStyle = ResolvePlanOlcu25(tr, destDb);
                    Matrix3d move = Matrix3d.Identity;
                    if (any)
                        move = Matrix3d.Displacement(new Vector3d(pick.X - minX, pick.Y - maxY, 0));
                    foreach (Entity ent in cloned)
                    {
                        if (any)
                        {
                            try { ent.TransformBy(move); } catch { }
                        }
                        if (ent is Dimension dim)
                            ApplyPlanOlcuStyle(tr, dim, dimStyle);
                    }
                    if (any)
                        placedMaxX = pick.X + (maxX - minX);
                }
                return cloned.Count;
            }
        }

        /// <summary>Merdiven detayı ile aynı: PLAN_OLCU 25, sabit uzatma 5.</summary>
        private static void ApplyPlanOlcuStyle(Transaction tr, Dimension dim, ObjectId dimStyle)
        {
            BaDimensionStyles.Apply(tr, dim.Database, dim, dimStyle, DimStylePlanOlcu25);
        }

        private static ObjectId ResolvePlanOlcu25(Transaction tr, Database db)
        {
            return BaDimensionStyles.Ensure(tr, db, DimStylePlanOlcu25);
        }

        /// <summary>Şablon koordinatını form değerlerine taşır.</summary>
        private sealed partial class Fit
        {
            public double Dy1, Dy2, Dy3, Dy4, DRun, DW, DSide;
            public double Dy2L, Dy2R, Dy3L, Dy3R, DRunL, DRunR;
            public double StemSolCm, StemSagCm;
            public bool SplitHeight;
            public bool DetayBaslik;
            public bool GeometryChanged;
            public int StraightCm, LVert, YatayDuzCm;
            public int UstAdet, AltAdet, DisAdet, IcAdet;
            public int KarsiUstAdet, KarsiAltAdet, YanAdet;
            public int KesitNo;
            /// <summary>Aynı çukurdan kaç tane. 1 ve altı yazılmaz.</summary>
            public int Carpan;

            /// <summary>(diğer kesit genişliği + 2×alt temel) / alt donatı aralığı.</summary>
            public static int YanAcilimAdet(double karsiGenislikCm)
            {
                double ar = TemelCekuruSettings.AltAralikCm;
                if (ar < 1) ar = 1;
                double n = (karsiGenislikCm + 2.0 * TemelCekuruSettings.AltTemelYuksekligiCm) / ar;
                int adet = (int)Math.Round(n, MidpointRounding.AwayFromZero);
                return adet < 1 ? 1 : adet;
            }

            public static Fit FromSettings(double genislikCm, int kesitNo, int karsiUst, int karsiAlt, int yanAdet, int carpan)
            {
                double sHatil = -TemelCekuruSettings.TemplateHatilYuksekligiCm * DuPerCm;
                double sUst = (TemelCekuruSettings.UstTemelYuksekligiCm - TemelCekuruSettings.TemplateUstTemelYuksekligiCm) * DuPerCm;
                double stem = TemelCekuruSettings.StemCm;
                double sTemel = (stem - TemelCekuruSettings.TemplateTemelYuksekligiCm) * DuPerCm;
                double sAlt = (TemelCekuruSettings.AltTemelYuksekligiCm - TemelCekuruSettings.TemplateAltTemelYuksekligiCm) * DuPerCm;
                double aci = TemelCekuruSettings.GrobetonAcisi;
                if (aci < 50) aci = 50;
                if (aci > 89) aci = 89;
                double riseCm = stem
                    - TemelCekuruSettings.UstTemelYuksekligiCm
                    + TemelCekuruSettings.AltTemelYuksekligiCm;
                if (riseCm < 20) riseCm = 20;
                double runDu = riseCm / Math.Tan(aci * Math.PI / 180.0) * DuPerCm;
                int straight = (int)Math.Round(
                    stem + TemelCekuruSettings.AltTemelYuksekligiCm - 2.0 * TemelCekuruSettings.PaspayiCm,
                    MidpointRounding.AwayFromZero);
                if (straight < 20) straight = 20;
                int yatay = 274
                    + (int)Math.Round(genislikCm - TemelCekuruSettings.TemplateTemelGenisligiCm, MidpointRounding.AwayFromZero)
                    + (int)Math.Round(2.0 * (TemelCekuruSettings.AltTemelYuksekligiCm - TemelCekuruSettings.TemplateAltTemelYuksekligiCm), MidpointRounding.AwayFromZero)
                    - (int)Math.Round(2.0 * (TemelCekuruSettings.PaspayiCm - TemelCekuruSettings.TemplatePaspayiCm), MidpointRounding.AwayFromZero);
                if (yatay < 20) yatay = 20;
                double dW = (genislikCm - TemelCekuruSettings.TemplateTemelGenisligiCm) * DuPerCm;
                double dSide = (TemelCekuruSettings.AltTemelYuksekligiCm - TemelCekuruSettings.TemplateAltTemelYuksekligiCm) * DuPerCm;
                bool geom = Math.Abs(sHatil) > 0.01 || Math.Abs(sUst) > 0.01 || Math.Abs(sTemel) > 0.01
                    || Math.Abs(sAlt) > 0.01 || Math.Abs(aci - TemelCekuruSettings.TemplateGrobetonAcisi) > 0.01
                    || Math.Abs(dW) > 0.01 || Math.Abs(dSide) > 0.01;
                return new Fit
                {
                    Dy1 = -sHatil,
                    Dy2 = -sHatil - sUst,
                    Dy3 = -sHatil - sTemel,
                    Dy4 = -sHatil - sTemel - sAlt,
                    DRun = runDu - TemplateRunDu,
                    DW = dW,
                    DSide = dSide,
                    GeometryChanged = geom,
                    StraightCm = straight,
                    YatayDuzCm = yatay,
                    LVert = straight + 106,
                    KesitNo = kesitNo < 1 ? 1 : kesitNo,
                    Carpan = carpan,
                    KarsiUstAdet = karsiUst < 1 ? 1 : karsiUst,
                    KarsiAltAdet = karsiAlt < 1 ? 1 : karsiAlt,
                    YanAdet = yanAdet < 1 ? 1 : yanAdet
                };
            }

            /// <summary>
            /// Tek kesit. Alt temel ortak kotta; sol/sağ üst kotları ve üst temel kalınlıkları bağımsızdır.
            /// Hatıl, temel geometrisini etkilemeyen ek detaydır.
            /// </summary>
            public static Fit FromDetay(double genislikCm, int kesitNo, int karsiUst, int karsiAlt, int yanAdet, int carpan, double stemSol, double stemSag)
            {

                Fit f = FromSettings(genislikCm, kesitNo, karsiUst, karsiAlt, yanAdet, carpan);
                f.ConfigureDetail(stemSol, stemSag);
                return f;
            }
            public void Apply(Transaction tr, BlockTableRecord ms)
            {
                SetDonatiAdet();
                EnsureDonatiIziAndBlokaj(tr, ms.Database);
                var ids = new List<ObjectId>();
                foreach (ObjectId id in ms)
                    ids.Add(id);
                foreach (ObjectId id in ids)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Entity ent) || ent.IsErased)
                        continue;
                    string layer = ent.Layer ?? "";
                    if (DetayBaslik && OmitDetailEntity(ent)) { ent.Erase(); continue; }
                    if (ent is DBText kesitte && (kesitte.TextString ?? "").TrimStart().StartsWith("KESİTTE", StringComparison.OrdinalIgnoreCase))
                    {
                        ent.Erase();
                        continue;
                    }
                    if (LayerIs(layer, LayerCizgi) || LayerIs(layer, LayerKesitGorunus) || IsTemplateKot(layer)
                        || (LayerIs(layer, "OLCU (BEYKENT)") && (ent is RotatedDimension || ent is AlignedDimension || ent is LineAngularDimension2)))
                    {
                        ent.Erase();
                        continue;
                    }
                    if (ent is Polyline gro && IsGrobetonOutline(gro))
                    {
                        MoveGrobetonToZemin(tr, ms, gro);
                        continue;
                    }
                    if (IsDGorunusLayer(layer))
                        AssignDonatiIzi(ent);
                    bool isHatch = ent is Hatch;
                    bool rebar = IsRebarGeom(ent);
                    if (!GeometryChanged && !rebar && !(ent is DBText) && !isHatch)
                        continue;
                    try { MapEntity(ent); }
                    catch { }
                    if (isHatch)
                        RestyleBlokajHatch((Hatch)ent);
                }
                try { RespaceNamedSpans(tr, ms); }
                catch { }
                try { FitSectionHooks(tr, ms); }
                catch { }
                try { FitGonyeHooks(tr, ms); }
                catch { }
                try { CopySectionShapes(tr, ms); }
                catch { }
                try { DropStretchedFillets(tr, ms); }
                catch { }
                try { ShortenShallowWalls(tr, ms); }
                catch { }
                try { DrawKots(tr, ms); }
                catch { }
                try { DrawHatilVeSubasman(tr, ms); }
                catch { }
                // Donatı numaraları etiket yerleşiminden sonra eklenir.
                try { DrawOlculer(tr, ms); }
                catch { }
                try { EnsureSabitDonatiDots(tr, ms); }
                catch { }
                try { PlacePieceLabels(tr, ms); }
                catch { }
                // Donatı numaraları şimdilik kapalı; sabit noktalar ve hesap kimlikleri korunur.
                if (DetayBaslik) FinishSingleDetail(tr, ms);
                try { DrawKesitNo(tr, ms); }
                catch { }
            }

            /// <summary>Alt donatı açılımının altında. Yazı kutusu üst kenarı, açılım dibinden 38 cm aşağıda.</summary>
            private void DrawKesitNo(Transaction tr, BlockTableRecord ms)
            {
                const string layer = "KESIT ISMI (BEYKENT)";
                EnsureStyledLayer(tr, ms.Database, layer, 6, LineWeight.LineWeight020, null);
                double x = SingleDetail ? (DetailCutX() + (DetailLeft ? XOutL - DSide - DRunL : XOutR + DW + DSide + DRunR)) / 2 : XKotCenter + DW * 0.5;
                double yFloor = Y4 + Dy4;
                double xL = XOutL - 20.0;
                double xR = XOutR + DW + 20.0;
                double yBot = double.MaxValue;
                bool found = false;
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead, false) is Polyline pl) || pl.IsErased) continue;
                    if (!IsRebarGeom(pl)) continue;
                    double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue;
                    int n = pl.NumberOfVertices;
                    for (int i = 0; i < n; i++)
                    {
                        Point2d p = pl.GetPoint2dAt(i);
                        if (p.X < minX) minX = p.X;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y < minY) minY = p.Y;
                    }
                    if (maxX < xL || minX > xR) continue;
                    if (minY >= yFloor - 5.0) continue;
                    if (minY < yBot) { yBot = minY; found = true; }
                }
                if (!found) yBot = yFloor - 80.0;
                double yTop = yBot - 16.0 - 30.0 * DuPerCm;
                string no = KesitNo.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string text = DetayBaslik
                    ? "TEMEL DETAYI 1:25"
                    : no + "-" + no + " KESİTİ 1:25";
                ObjectId style = EnsureYaziStyle(tr, ms.Database);
                var t = new DBText
                {
                    TextString = text,
                    Height = 20.0,
                    Layer = layer,
                    Color = Color.FromColorIndex(ColorMethod.ByLayer, 256),
                    TextStyleId = style,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextTop,
                    AlignmentPoint = new Point3d(x, yTop, 0)
                };
                ms.AppendEntity(t);
                tr.AddNewlyCreatedDBObject(t, true);
                try
                {
                    t.AdjustAlignment(ms.Database);
                    Extents3d e = t.GeometricExtents;
                    double dx = x - (e.MinPoint.X + e.MaxPoint.X) * 0.5;
                    double dy = yTop - e.MaxPoint.Y;
                    if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                        t.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
                }
                catch { }
            }

            /// <summary>Adetler fiziksel sıralardan hesaplanır; görünen numaralar değişse de yerleşim korunur.</summary>
            private void SetDonatiAdet()
            {
                double ustDu = TemelCekuruSettings.UstAralikCm * DuPerCm;
                double altDu = TemelCekuruSettings.AltAralikCm * DuPerCm;
                if (ustDu < DuPerCm) ustDu = DuPerCm;
                if (altDu < DuPerCm) altDu = DuPerCm;
                UstAdet = BarCount(PhysicalDot(6), PhysicalDot(13), ustDu);
                int c78 = BarCount(PhysicalDot(7), PhysicalDot(8), altDu);
                int c815 = BarCount(PhysicalDot(8), PhysicalDot(15), altDu);
                int c1516 = BarCount(PhysicalDot(15), PhysicalDot(16), altDu);
                AltAdet = c78 + c815 + c1516 - 2;

                int c35 = BarCount(NamedDot(3), NamedDot(5), altDu);
                int c57 = BarCount(NamedDot(5), NamedDot(7), altDu);
                DisAdet = c35 + c57 - 3;
                if (DisAdet < 0) DisAdet = 0;
                int c24 = BarCount(NamedDot(2), NamedDot(4), ustDu);
                int c46 = BarCount(NamedDot(4), NamedDot(6), ustDu);
                IcAdet = c24 + c46 - 4;
                if (IcAdet < 0) IcAdet = 0;
                SetRightDetailAdet();
            }

            /// <summary>Dış düşey etiket (elips) sol/sağ dış çubuğa daha yakındır.</summary>
            private static bool IsDisDonatiEtiket(double x)
            {
                const double oL = 2017.647170209089;
                const double iL = 2079.647170209089;
                const double iR = 2497.647170209089;
                const double oR = 2559.647170209089;
                if (x < XKotCenter)
                    return Math.Abs(x - oL) <= Math.Abs(x - iL);
                return Math.Abs(x - oR) <= Math.Abs(x - iR);
            }

            private static int BarCount(Point3d a, Point3d b, double maxDu)
            {
                double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                return BarCountLen(len, maxDu);
            }

            /// <summary>Açılıma yazılacak adet: çukur + iki yan. Üstte iç (4ø), altta dış (4ø).</summary>
            public static void PeekAdet(double genislikCm, out int ust, out int alt)
            {
                // Kesit ve açılım aynı geometri, paspayı ve yan genişliği hesabını kullanır.
                Fit fit = FromSettings(genislikCm, 1, 1, 1, 1, 1);
                fit.SetDonatiAdet();
                ust = fit.UstAdet + 2 * fit.IcAdet;
                alt = fit.AltAdet + 2 * fit.DisAdet;
            }
            private static int BarCountLen(double len, double maxDu)
            {
                if (len <= NamedDotTolerance) return 1;
                if (maxDu < 1) maxDu = 1;
                int spaces = (int)Math.Ceiling(len / maxDu - 1e-6);
                if (spaces < 1) spaces = 1;
                return spaces + 1;
            }

            /// <summary>Kesit altındaki yatay açılım. ÜST veya ALT yazar, L= taşır.</summary>
            private static bool IsYatayAcilim(string s, out bool ust)
            {
                ust = false;
                if (string.IsNullOrEmpty(s) || s.IndexOf("L=", StringComparison.Ordinal) < 0)
                    return false;
                bool hasUst = s.IndexOf("ÜST", StringComparison.Ordinal) >= 0;
                bool hasAlt = s.IndexOf("ALT", StringComparison.Ordinal) >= 0;
                if (hasUst && !hasAlt)
                {
                    ust = true;
                    return true;
                }
                if (hasAlt && !hasUst)
                    return true;
                return false;
            }

            /// <summary>
            /// İşaretli 16 donatı. Sol üstten alta 1–8, sağ üstten alta 9–16.
            /// Her sırada önce dıştaki, sonra içteki (sağda önce içteki).
            /// </summary>
            private const double NamedDotTolerance = 0.01;

            // Yerleşim kimliği şablondaki fiziksel sırayı izler; görünen numaradan bağımsızdır.
            private Point3d PhysicalDot(int number)
            {
                int side = number <= 8 ? 0 : 1;
                int local = (number - 1) % 8;
                int row = local / 2;
                int col = side == 0 ? local % 2 : 2 + local % 2;
                double[] xs = { RebarOutL, 2079.647170209089, 2497.647170209089, RebarOutR };
                double[] ys = { 1407.388795085607, 1345.388795085607, 1207.388795085607, 1145.388795085607 };
                return CagePt(new Point3d(xs[col], ys[row], 0));
            }

            // Yalnız etiketler üstten alta sıralanır. Donatı yerleşimi PhysicalDot kullanır.
            private Point3d NamedDot(int number)
            {
                int row = ((number - 1) % 8) / 2;
                if (row != 1 && row != 2) return PhysicalDot(number);
                int stepNumber = row == 1 ? number : number - 2;
                Point3d step = PhysicalDot(stepNumber);
                Point3d up = PhysicalDot(stepNumber + 2);
                if (row == 1) return step.Y >= up.Y ? step : up;
                return step.Y >= up.Y ? up : step;
            }
            // Numaralı 16 konum, aradaki donatıların aralığından bağımsız korunur.
            private void EnsureSabitDonatiDots(Transaction tr, BlockTableRecord ms)
            {
                var centers = new List<Point3d>();
                Entity prototype = null;
                Point3d prototypeCenter = Point3d.Origin;
                var ids = new List<ObjectId>();
                foreach (ObjectId id in ms) ids.Add(id);
                foreach (ObjectId id in ids)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead, false) is Entity ent) || ent.IsErased) continue;
                    string layer = ent.Layer ?? "";
                    if (layer.IndexOf("DONATI", StringComparison.OrdinalIgnoreCase) < 0
                        && layer.IndexOf("CIROZ", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!TryDotCenter(ent, out Point3d center)) continue;
                    if (prototype == null) { prototype = ent; prototypeCenter = center; }
                    if (centers.Exists(c => c.DistanceTo(center) <= NamedDotTolerance))
                    {
                        if (!ent.IsWriteEnabled) ent.UpgradeOpen();
                        ent.Erase();
                    }
                    else centers.Add(center);
                }
                for (int number = 1; number <= 16; number++)
                {
                    Point3d point = NamedDot(number);
                    if (centers.Exists(c => c.DistanceTo(point) <= NamedDotTolerance)) continue;
                    Entity dot;
                    if (prototype != null)
                    {
                        dot = (Entity)prototype.Clone();
                        dot.TransformBy(Matrix3d.Displacement(point - prototypeCenter));
                    }
                    else dot = new Circle(point, Vector3d.ZAxis, 1.5);
                    bool cyan = false;
                    foreach (int n in new[] { 5, 6, 7, 8, 13, 14, 15, 16 })
                        if (PhysicalDot(n).DistanceTo(point) <= NamedDotTolerance) cyan = true;
                    dot.Layer = cyan ? "DONATI (BEYKENT)" : LayerDonatiIzi;
                    dot.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                    ms.AppendEntity(dot);
                    tr.AddNewlyCreatedDBObject(dot, true);
                    centers.Add(point);
                }
            }
            private void DrawSabitDonatiNo(Transaction tr, BlockTableRecord ms)
            {
                Database db = ms.Database;
                ObjectId style = EnsureYaziStyle(tr, db);
                var points = new List<Point3d>();
                var labels = new List<string>();
                for (int number = 1; number <= 16; number++)
                {
                    Point3d point = NamedDot(number);
                    int index = points.FindIndex(c => c.DistanceTo(point) <= NamedDotTolerance);
                    string label = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (index < 0) { points.Add(point); labels.Add(label); }
                    else labels[index] += "," + label;
                }
                for (int index = 0; index < points.Count; index++)
                {
                    Point3d c = points[index];
                    var t = new DBText
                    {
                        TextString = labels[index],
                        Height = 10.0,
                        Layer = "DONATI YAZISI (BEYKENT)",
                        Color = Color.FromColorIndex(ColorMethod.ByAci, 5),
                        TextStyleId = style,
                        HorizontalMode = TextHorizontalMode.TextCenter,
                        VerticalMode = TextVerticalMode.TextBottom,
                        Position = c,
                        AlignmentPoint = c
                    };
                    ms.AppendEntity(t);
                    tr.AddNewlyCreatedDBObject(t, true);
                    try
                    {
                        t.AdjustAlignment(db);
                        Extents3d e = t.GeometricExtents;
                        double dx = c.X - (e.MinPoint.X + e.MaxPoint.X) * 0.5;
                        double dy = (c.Y + 5.0) - e.MinPoint.Y;
                        if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                            t.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
                    }
                    catch { }
                }
            }

            /// <summary>Şablondaki D.GORUNUS (BEYKENT). Nokta ve boşluk yok sayılır.</summary>
            private static bool IsDGorunusLayer(string layer)
            {
                if (string.IsNullOrEmpty(layer)) return false;
                string n = layer.Replace(" ", "").Replace(".", "");
                return n.Equals("DGORUNUS(BEYKENT)", StringComparison.OrdinalIgnoreCase);
            }

            private static void AssignDonatiIzi(Entity ent)
            {
                ent.Layer = LayerDonatiIzi;
                ent.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                ent.LineWeight = LineWeight.ByLayer;
                try { ent.Linetype = "ByLayer"; } catch { }
            }

            /// <summary>Temel Hatılı dolgu taraması: BLOKAJ, AR-CONC, ölçek 0.1.</summary>
            private static void RestyleBlokajHatch(Hatch hatch)
            {
                try
                {
                    hatch.Layer = LayerBlokaj;
                    hatch.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                    hatch.LineWeight = LineWeight.ByLayer;
                    try { hatch.Linetype = "ByLayer"; } catch { }
                    hatch.HatchStyle = HatchStyle.Normal;
                    hatch.PatternAngle = 0;
                    hatch.SetHatchPattern(HatchPatternType.PreDefined, HatchArConc);
                    hatch.PatternScale = BlokajHatchScale;
                    hatch.SetHatchPattern(hatch.PatternType, hatch.PatternName);
                    try { hatch.EvaluateHatch(true); }
                    catch { try { hatch.EvaluateHatch(false); } catch { } }
                }
                catch { }
            }

            private static void EnsureDonatiIziAndBlokaj(Transaction tr, Database db)
            {
                EnsureStyledLayer(tr, db, LayerDonatiIzi, 8, LineWeight.LineWeight025, null);
                EnsureStyledLayer(tr, db, LayerBlokaj, 8, LineWeight.LineWeight015, LayerBlokajTransparencyPercent);
            }

            private static void EnsureStyledLayer(Transaction tr, Database db, string name, short aci, LineWeight lw, int? transparencyPercent)
            {
                BeykentKatmanYukle.Ensure(tr, db, name, aci, lw);
            }

            /// <summary>Grobeton dış çizgisi. Alt kenar iki topuk arasında, YGro.</summary>
            private static bool IsGrobetonOutline(Polyline pl)
            {
                if (!LayerIs(pl.Layer, LayerTemel)) return false;
                int n = pl.NumberOfVertices;
                for (int i = 0; i < n - 1; i++)
                {
                    Point2d a = pl.GetPoint2dAt(i);
                    Point2d b = pl.GetPoint2dAt(i + 1);
                    if (Math.Abs(a.Y - YGro) > 2.0 || Math.Abs(b.Y - YGro) > 2.0) continue;
                    bool toes = (Math.Abs(a.X - XToeL) < 2.0 && Math.Abs(b.X - XToeR) < 2.0)
                        || (Math.Abs(a.X - XToeR) < 2.0 && Math.Abs(b.X - XToeL) < 2.0);
                    if (toes) return true;
                }
                return false;
            }

            /// <summary>Grobeton alt profili tek polyline, ZEMIN. Bir kez taşınır.</summary>
            private void MoveGrobetonToZemin(Transaction tr, BlockTableRecord ms, Polyline pl)
            {
                EnsureStyledLayer(tr, ms.Database, LayerZemin, 43, LineWeight.LineWeight050, null);
                int n = pl.NumberOfVertices;
                for (int i = 0; i < n; i++)
                {
                    Point2d p = pl.GetPoint2dAt(i);
                    Point3d m = MapPt(new Point3d(p.X, p.Y, 0));
                    pl.SetPointAt(i, new Point2d(m.X, m.Y));
                }
                pl.Layer = LayerZemin;
                pl.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                if (n >= 6)
                {
                    Point2d p0 = pl.GetPoint2dAt(1);
                    Point2d pToeR = pl.GetPoint2dAt(2);
                    Point2d pToeL = pl.GetPoint2dAt(3);
                    Point2d pSlopeL = pl.GetPoint2dAt(4);
                    DrawGrobetonAcisi(tr, ms,
                        new Point3d(pToeL.X, pToeL.Y, 0),
                        new Point3d(pSlopeL.X, pSlopeL.Y, 0),
                        new Point3d(pToeR.X, pToeR.Y, 0));
                    DrawGrobetonAcisi(tr, ms,
                        new Point3d(pToeR.X, pToeR.Y, 0),
                        new Point3d(p0.X, p0.Y, 0),
                        new Point3d(pToeL.X, pToeL.Y, 0));
                }
            }

            /// <summary>Topuktaki açı: şev ile grobeton altının dış uzantısı. 80° dışarıdan.</summary>
            private static void DrawGrobetonAcisi(Transaction tr, BlockTableRecord ms, Point3d toe, Point3d slope, Point3d alongBottom)
            {
                Vector3d u = slope - toe;
                Vector3d inward = alongBottom - toe;
                if (u.Length < 1.0 || inward.Length < 1.0) return;
                u = u.GetNormal();
                Vector3d v = -inward.GetNormal();
                Vector3d bis = u + v;
                if (bis.Length < 0.2) return;
                Point3d arc = toe + bis.GetNormal() * 28.0;
                Point3d outward = toe + v * 40.0;
                var dim = new LineAngularDimension2(
                    slope, toe,
                    outward, toe,
                    arc,
                    "",
                    BaDimensionStyles.Ensure(tr, ms.Database, DimStylePlanOlcu25));
                dim.Layer = "OLCU (BEYKENT)";
                BaDimensionStyles.Apply(tr, ms.Database, dim, dim.DimensionStyle, DimStylePlanOlcu25);
                ms.AppendEntity(dim);
                tr.AddNewlyCreatedDBObject(dim, true);
            }

            /// <summary>
            /// Hatıl gövdesi form genişliği kadar. Üstte subasman döşemesi, kalınlığı formdan.
            /// </summary>
            private void DrawHatilVeSubasman(Transaction tr, BlockTableRecord ms)
            {
                if (TemelCekuruSettings.HatilYuksekligiCm < 0.5) return;
                Database db = ms.Database;
                EnsureStyledLayer(tr, db, LayerHatil, 230, LineWeight.LineWeight030, null);
                EnsureStyledLayer(tr, db, LayerDoseme, 2, LineWeight.LineWeight030, null);
                double slab = TemelCekuruSettings.SubasmanYuksekligiCm * DuPerCm;
                if (slab < DuPerCm) slab = DuPerCm;
                double width = TemelCekuruSettings.HatilGenisligiCm * DuPerCm;
                if (width < DuPerCm) width = DuPerCm;
                DrawHatilSide(tr, ms, XInnerL, XOutL, -1.0, slab, width);
                DrawHatilSide(tr, ms, XInnerR, XOutR, 1.0, slab, width);
            }

            /// <summary>sign: sol −1 (genişlik dışa, küçük X), sağ +1.</summary>
            private void DrawHatilSide(Transaction tr, BlockTableRecord ms, double xInner, double xOut, double sign, double slabDu, double widthDu)
            {
                if (DetayBaslik && !DetailEnabled(sign > 0)) return;
                Point3d botIn = MapPt(new Point3d(xInner, Y1, 0));
                double hatilTop = botIn.Y + TemelCekuruSettings.HatilYuksekligiCm * DuPerCm;
                Point3d topIn = new Point3d(botIn.X, hatilTop, 0);
                Point3d mappedOut = MapPt(new Point3d(xOut, Y1, 0));
                Point3d topOut = new Point3d(mappedOut.X, hatilTop, 0);
                double ySlab = topIn.Y - slabDu;
                if (ySlab < botIn.Y + DuPerCm) ySlab = botIn.Y + DuPerCm;
                double xHat = topIn.X + sign * widthDu;
                if (sign < 0 && xHat < topOut.X + 4.0) xHat = topOut.X + 4.0;
                if (sign > 0 && xHat > topOut.X - 4.0) xHat = topOut.X - 4.0;

                var pl = new Polyline();
                pl.AddVertexAt(0, new Point2d(topIn.X, ySlab), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(topIn.X, topIn.Y), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(topOut.X, topOut.Y), 0, 0, 0);
                pl.Layer = LayerDoseme;
                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);

                var slabBot = new Line(new Point3d(xHat, ySlab, 0), new Point3d(topOut.X, ySlab, 0));
                slabBot.Layer = LayerDoseme;
                ms.AppendEntity(slabBot);
                tr.AddNewlyCreatedDBObject(slabBot, true);

                var face = new Line(new Point3d(topIn.X, botIn.Y, 0), new Point3d(topIn.X, ySlab, 0));
                face.Layer = LayerHatil;
                ms.AppendEntity(face);
                tr.AddNewlyCreatedDBObject(face, true);

                var outer = new Line(new Point3d(xHat, botIn.Y, 0), new Point3d(xHat, ySlab, 0));
                outer.Layer = LayerHatil;
                ms.AppendEntity(outer);
                tr.AddNewlyCreatedDBObject(outer, true);
            }

            /// <summary>Şablondaki eski kot (KOT CIZGI). Yeni sembol KOT CIZGISI katmanına çizilir.</summary>
            private static bool IsTemplateKot(string layer)
            {
                if (string.IsNullOrEmpty(layer)) return false;
                if (LayerIs(layer, LayerKotYazi)) return true;
                return layer.IndexOf("KOT CIZGI", StringComparison.OrdinalIgnoreCase) >= 0
                    && layer.IndexOf("KOT CIZGISI", StringComparison.OrdinalIgnoreCase) < 0;
            }

            /// <summary>
            /// KOLONDUSEY / merdiven kesit kotu. Kot kolonu kesitin sağında, yazı sağa;
            /// perde alt kotu kesit ortasında, yazı sağa.
            /// </summary>
            private void DrawKots(Transaction tr, BlockTableRecord ms)
            {
                if (DetayBaslik) { DrawDetailKots(tr, ms); return; }
                Database db = ms.Database;
                EnsureKotLayer(tr, db, LayerKotCizgi);
                EnsureKotLayer(tr, db, LayerKotYazi);
                double k1 = TemelCekuruSettings.TemelUstKotuCm / 100.0;
                double k0 = k1 + TemelCekuruSettings.HatilYuksekligiCm / 100.0;
                double k2 = k1 - TemelCekuruSettings.UstTemelYuksekligiCm / 100.0;
                double k3 = k1 - (Y1 + Dy1 - Y3 - Dy3) / DuPerCm / 100.0;
                double k4 = k3 - TemelCekuruSettings.AltTemelYuksekligiCm / 100.0;
                double xRight = (XOutR + DW + DRun + DSide) + KotRightGapDu;
                double y2 = Y2 + Dy2;
                if (TemelCekuruSettings.HatilYuksekligiCm >= 0.5)
                    DrawKotSymbol(tr, db, ms, k0, xRight, Y1 + Dy1 + TemelCekuruSettings.HatilYuksekligiCm * DuPerCm, textToRight: true);
                DrawKotSymbol(tr, db, ms, k1, xRight, Y1 + Dy1, textToRight: true);
                DrawKotSymbol(tr, db, ms, k2, xRight, y2, textToRight: true);
                DrawKotSymbol(tr, db, ms, k4, xRight, Y4 + Dy4, textToRight: true);
                DrawKotSymbol(tr, db, ms, k3, XKotCenter + DW * 0.5, Y3 + Dy3, textToRight: true);
                if (SplitHeight && Math.Abs(Dy3L - Dy3) > 1.0)
                {
                    double k3L = k1 - StemSolCm / 100.0;
                    DrawKotSymbol(tr, db, ms, k3L, XInnerL + 30.0, Y3 + Dy3L, textToRight: false);
                }
                if (SplitHeight && Math.Abs(Dy3R - Dy3) > 1.0)
                {
                    double k3R = k1 - StemSagCm / 100.0;
                    DrawKotSymbol(tr, db, ms, k3R, XInnerR + DW - 30.0, Y3 + Dy3R, textToRight: true);
                }
            }

            /// <summary>
            /// Tepe kot çizgisinde. Sol yarım dolu üçgen, yazı 10 birim.
            /// textToRight: merdiven gibi yazı sağda. Aksi halde yazı solda (kesit sağda).
            /// </summary>
            private static void DrawKotSymbol(
                Transaction tr, Database db, BlockTableRecord ms,
                double kotM, double apexX, double apexY, bool textToRight)
            {
                const double triH = 12.0;
                const double halfW = 12.0;
                const double extToward = 20.0;
                const double extTail = 26.0;
                const double textH = 10.0;
                const double textAbove = 5.7;

                double dir = textToRight ? 1.0 : -1.0;
                AddKotLine(tr, ms, apexX, apexY, apexX - dir * extToward, apexY);

                double xIn = apexX - dir * halfW;
                double xOut = apexX + dir * halfW;
                double yTop = apexY + triH;
                var wedge = new Polyline(3);
                wedge.AddVertexAt(0, new Point2d(apexX, apexY), 0, 0, 0);
                wedge.AddVertexAt(1, new Point2d(xIn, yTop), 0, 0, 0);
                wedge.AddVertexAt(2, new Point2d(apexX, yTop), 0, 0, 0);
                wedge.Closed = true;
                wedge.Layer = LayerKotCizgi;
                ms.AppendEntity(wedge);
                tr.AddNewlyCreatedDBObject(wedge, true);

                var hatch = new Hatch();
                hatch.SetDatabaseDefaults(db);
                hatch.Layer = LayerKotCizgi;
                hatch.Color = Color.FromColorIndex(ColorMethod.ByAci, 250);
                ms.AppendEntity(hatch);
                tr.AddNewlyCreatedDBObject(hatch, true);
                try
                {
                    hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                    hatch.Associative = false;
                    hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { wedge.ObjectId });
                    try { hatch.EvaluateHatch(true); } catch { try { hatch.EvaluateHatch(false); } catch { } }
                }
                catch
                {
                    try { hatch.Erase(); } catch { }
                }

                AddKotLine(tr, ms, xIn, yTop, xOut, yTop);
                AddKotLine(tr, ms, apexX, apexY, xOut, yTop);
                AddKotLine(tr, ms, xOut, yTop, apexX, yTop);
                AddKotLine(tr, ms, xOut, yTop, xOut + dir * extTail, yTop);

                string txt = FormatKotM(kotM);
                ObjectId styleId = EnsureYaziStyle(tr, db);
                double textX = xOut;
                double textY = yTop;
                var t = new DBText
                {
                    TextString = txt,
                    Height = textH,
                    Layer = LayerKotYazi,
                    TextStyleId = styleId,
                    HorizontalMode = textToRight ? TextHorizontalMode.TextLeft : TextHorizontalMode.TextRight,
                    VerticalMode = TextVerticalMode.TextBottom,
                    Position = new Point3d(textX, textY, 0),
                    AlignmentPoint = new Point3d(textX, textY, 0)
                };
                ms.AppendEntity(t);
                tr.AddNewlyCreatedDBObject(t, true);
                try
                {
                    t.AdjustAlignment(db);
                    Extents3d ext = t.GeometricExtents;
                    double dy = (textY + textAbove) - ext.MinPoint.Y;
                    if (Math.Abs(dy) > 0.01)
                        t.TransformBy(Matrix3d.Displacement(new Vector3d(0, dy, 0)));
                }
                catch { }
            }

            private static string FormatKotM(double m)
            {
                double rounded = Math.Round(m, 2, MidpointRounding.AwayFromZero);
                if (Math.Abs(rounded) < 0.005) return "\u00B10.00";
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:+0.00;-0.00}", rounded);
            }

            private static void AddKotLine(Transaction tr, BlockTableRecord ms, double x0, double y0, double x1, double y1)
            {
                var ln = new Line(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0));
                ln.Layer = LayerKotCizgi;
                ms.AppendEntity(ln);
                tr.AddNewlyCreatedDBObject(ln, true);
            }

            private static void EnsureKotLayer(Transaction tr, Database db, string name)
            {
                BeykentKatmanYukle.Ensure(tr, db, name, 7, LineWeight.LineWeight020);
            }

            private static ObjectId EnsureYaziStyle(Transaction tr, Database db)
            {
                return BaDimensionStyles.EnsureYazi(tr, db);
            }

            private void MapEntity(Entity ent)
            {
                if (ent is DBText tx)
                {
                    MapText(tx);
                    return;
                }
                bool rebar = IsRebarGeom(ent);
                if (!GeometryChanged && !rebar) return;
                Point3d M(Point3d p) => rebar ? CagePt(p) : MapPt(p);
                if (ent is Line ln)
                {
                    ln.StartPoint = M(ln.StartPoint);
                    ln.EndPoint = M(ln.EndPoint);
                }
                else if (ent is Polyline pl)
                {
                    bool acilim = rebar && IsAcilimBar(pl);
                    bool sideBar = acilim && AcilimIsSide(pl);
                    bool icHook = acilim && IsIcHookBar(pl);
                    int nv = pl.NumberOfVertices;
                    Point2d[] before = null;
                    double[] bulges = null;
                    if (rebar && nv > 1)
                    {
                        before = new Point2d[nv];
                        bulges = new double[nv];
                        for (int i = 0; i < nv; i++)
                        {
                            before[i] = pl.GetPoint2dAt(i);
                            try { bulges[i] = pl.GetBulgeAt(i); }
                            catch { bulges[i] = 0; }
                        }
                    }
                    for (int i = 0; i < nv; i++)
                    {
                        Point2d p = pl.GetPoint2dAt(i);
                        Point3d m = M(new Point3d(p.X, p.Y, 0));
                        pl.SetPointAt(i, new Point2d(m.X, m.Y));
                    }
                    if (before != null)
                        HoldShortFillets(pl, before, bulges);
                    if (acilim)
                        ScaleAcilim(pl, sideBar, icHook);
                    if (LayerIs(pl.Layer, LayerTemel))
                        InsertFloorSteps(pl);
                }
                else if (ent is Solid sol)
                {
                    for (short i = 0; i < 4; i++)
                    {
                        try { sol.SetPointAt(i, MapPt(sol.GetPointAt(i))); }
                        catch { }
                    }
                }
                else if (ent is RotatedDimension rd)
                {
                    rd.XLine1Point = MapPt(rd.XLine1Point);
                    rd.XLine2Point = MapPt(rd.XLine2Point);
                    rd.DimLinePoint = MapPt(rd.DimLinePoint);
                    TryMapTextPos(rd);
                    TryRecompute(rd);
                }
                else if (ent is AlignedDimension ad)
                {
                    ad.XLine1Point = MapPt(ad.XLine1Point);
                    ad.XLine2Point = MapPt(ad.XLine2Point);
                    ad.DimLinePoint = MapPt(ad.DimLinePoint);
                    TryMapTextPos(ad);
                    TryRecompute(ad);
                }
                else if (ent is LineAngularDimension2 ang)
                {
                    ang.XLine1Start = MapPt(ang.XLine1Start);
                    ang.XLine1End = MapPt(ang.XLine1End);
                    ang.XLine2Start = MapPt(ang.XLine2Start);
                    ang.XLine2End = MapPt(ang.XLine2End);
                    TryMapTextPos(ang);
                    TryRecompute(ang);
                }
                else if (ent is Hatch hatch)
                {
                    MapHatch(hatch);
                }
            }

            private void MapText(DBText tx)
            {
                string s = tx.TextString ?? "";
                double x0 = tx.Position.X;
                double y0 = tx.Position.Y;
                if (GeometryChanged && s.Trim().Equals("GROBETON", StringComparison.OrdinalIgnoreCase))
                {
                    PlaceGrobeton(tx);
                }
                else if (GeometryChanged || IsSideBar(x0))
                {
                    Point3d p = MapPt(tx.Position);
                    Point3d a = MapPt(tx.AlignmentPoint);
                    tx.Position = p;
                    if (tx.HorizontalMode != TextHorizontalMode.TextLeft || tx.VerticalMode != TextVerticalMode.TextBase)
                        tx.AlignmentPoint = a;
                }

                string next;
                if (s.StartsWith("14\u00F8", StringComparison.Ordinal))
                    next = UstAdet.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + TemelCekuruSettings.CapSlash(TemelCekuruSettings.UstCapMm, TemelCekuruSettings.UstAralikCm);
                else if (s.StartsWith("16\u00F8", StringComparison.Ordinal))
                    next = AltAdet.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + TemelCekuruSettings.CapSlash(TemelCekuruSettings.AltCapMm, TemelCekuruSettings.AltAralikCm);
                else if (IsYatayAcilim(s, out bool acilimUst))
                {
                    string body = RewriteDonati(s);
                    int adet = acilimUst ? KarsiUstAdet : KarsiAltAdet;
                    next = adet.ToString(System.Globalization.CultureInfo.InvariantCulture) + body;
                    int lCm = YatayDuzCm + 2 * GonyeCm();
                    next = Regex.Replace(next, @"L=\d+", "L=" + lCm.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (s.Trim() == "34")
                    next = GonyeCm().ToString(System.Globalization.CultureInfo.InvariantCulture);
                else if (s.Trim() == "274")
                    next = YatayDuzCm.ToString(System.Globalization.CultureInfo.InvariantCulture);
                else if (s.Trim() == "134")
                    next = StraightForX(x0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                else if (s.Trim() == "72")
                    next = ElliCapCm(y0 > 1200.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                else if (s.StartsWith("3\u00F8", StringComparison.Ordinal))
                {
                    bool dis = IsDisDonatiEtiket(x0);
                    next = SideAdet(dis, x0 > XKotCenter).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + (dis
                            ? TemelCekuruSettings.CapSlash(TemelCekuruSettings.AltCapMm, TemelCekuruSettings.AltAralikCm)
                            : TemelCekuruSettings.CapSlash(TemelCekuruSettings.UstCapMm, TemelCekuruSettings.UstAralikCm));
                }
                else
                    next = RewriteDonati(s);
                if (next.IndexOf("DİKEY", StringComparison.Ordinal) >= 0)
                {
                    bool dis = next.IndexOf("DIŞ", StringComparison.Ordinal) >= 0;
                    string token = dis
                        ? TemelCekuruSettings.CapSlash(TemelCekuruSettings.AltCapMm, TemelCekuruSettings.AltAralikCm)
                        : TemelCekuruSettings.CapSlash(TemelCekuruSettings.UstCapMm, TemelCekuruSettings.UstAralikCm);
                    next = Regex.Replace(next, @"\u00F8\d+/\d+", token);
                    int lCm = StraightForX(x0) + ElliCapCm(!dis) + GonyeCm();
                    next = Regex.Replace(next, @"L=\d+", "L=" + lCm.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    string yan = (DetayBaslik ? DetailLengthAdet(dis) : YanAdet).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (next.Length > 0 && char.IsDigit(next[0]))
                        next = Regex.Replace(next, @"^\d+", yan);
                    else
                        next = yan + next;
                }
                if (Carpan > 1
                    && (IsYatayAcilim(next, out _) || next.IndexOf("DİKEY", StringComparison.Ordinal) >= 0))
                    next = Regex.Replace(next, @"^\d+",
                        Carpan.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x$0");
                if (!string.Equals(next, s, StringComparison.Ordinal))
                    tx.TextString = next;
            }

            /// <summary>øçap/aralık. Alt yazılar alt kutudan, diğer donatı yazıları üst kutudan.</summary>
            private static string RewriteDonati(string s)
            {
                if (string.IsNullOrEmpty(s) || !Regex.IsMatch(s, @"\u00F8\d+/\d+"))
                    return s;
                bool alt = s == TemelCekuruSettings.TemplateAltDonati
                    || (s.IndexOf("ALT", StringComparison.Ordinal) >= 0
                        && s.IndexOf("ÜST", StringComparison.Ordinal) < 0);
                string token = alt
                    ? TemelCekuruSettings.CapSlash(TemelCekuruSettings.AltCapMm, TemelCekuruSettings.AltAralikCm)
                    : TemelCekuruSettings.CapSlash(TemelCekuruSettings.UstCapMm, TemelCekuruSettings.UstAralikCm);
                return Regex.Replace(s, @"\u00F8\d+/\d+", token);
            }

            private void PlaceGrobeton(DBText tx)
            {
                bool left = tx.Position.X < (XToeL + XToeR) * 0.5;
                Point3d toe = left
                    ? MapPt(new Point3d(XToeL, YGro, 0))
                    : MapPt(new Point3d(XToeR, YGro, 0));
                Point3d top = left
                    ? MapPt(new Point3d(XSlopeL, YBench, 0))
                    : MapPt(new Point3d(XSlopeR, YBench, 0));
                Vector3d v = top - toe;
                double len = v.Length;
                if (len < 1) return;
                Vector3d u = v / len;
                Vector3d n = left
                    ? new Vector3d(u.Y, -u.X, 0)
                    : new Vector3d(-u.Y, u.X, 0);
                double t = left ? 0.8075 : 0.526;
                Point3d p = toe + u * (t * len) + n * 14.0;
                tx.Position = p;
                if (tx.HorizontalMode != TextHorizontalMode.TextLeft || tx.VerticalMode != TextVerticalMode.TextBase)
                    tx.AlignmentPoint = p;
                double rot = Math.Atan2(u.Y, u.X);
                if (u.X < 0) rot += Math.PI;
                tx.Rotation = rot;
            }

            private static bool NearX(double x, double target)
            {
                return Math.Abs(x - target) <= 3.0;
            }

            /// <summary>Kesit altındaki yatay açılım veya yandaki düşey açılım.</summary>
            private static bool IsAcilimBar(Polyline pl)
            {
                int n = pl.NumberOfVertices;
                if (n < 4) return false;
                bool side = true;
                double minY = double.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    Point2d p = pl.GetPoint2dAt(i);
                    if (!IsSideBar(p.X)) side = false;
                    if (p.Y < minY) minY = p.Y;
                }
                return side || minY < 1120.0;
            }

            private static bool AcilimIsSide(Polyline pl)
            {
                int n = pl.NumberOfVertices;
                for (int i = 0; i < n; i++)
                    if (!IsSideBar(pl.GetPoint2dAt(i).X)) return false;
                return n >= 4;
            }

            /// <summary>
            /// Kalınlık artınca kısa köşe yayı (bulge) kirişle birlikte uzamasın.
            /// Yay yarıçapı kalsın; uzayan düz parçanın bulge'ı sıfırlanır.
            /// </summary>
            private const double FilletMaxDu = 39.0;

            private static void HoldShortFillets(Polyline pl, Point2d[] before, double[] bulges)
            {
                int n = before.Length;
                if (n < 2) return;
                int seg = pl.Closed ? n : n - 1;
                for (int pass = 0; pass < n; pass++)
                {
                    bool any = false;
                    for (int i = 0; i < seg; i++)
                    {
                        double b = bulges[i];
                        if (Math.Abs(b) < 1e-6) continue;
                        int j = (i + 1) % n;
                        double oldL = Dist2d(before[i], before[j]);
                        if (oldL < 1e-3) continue;
                        Point2d a = pl.GetPoint2dAt(i);
                        Point2d c = pl.GetPoint2dAt(j);
                        double neu = Dist2d(a, c);
                        if (oldL > FilletMaxDu)
                        {
                            if (neu > oldL + 2.0)
                            {
                                try { pl.SetBulgeAt(i, 0); } catch { }
                                bulges[i] = 0;
                            }
                            continue;
                        }
                        if (neu <= oldL + 1.0) continue;
                        double da = Dist2d(a, before[i]);
                        double db = Dist2d(c, before[j]);
                        if (da >= db)
                            pl.SetPointAt(j, new Point2d(
                                a.X + before[j].X - before[i].X,
                                a.Y + before[j].Y - before[i].Y));
                        else
                            pl.SetPointAt(i, new Point2d(
                                c.X + before[i].X - before[j].X,
                                c.Y + before[i].Y - before[j].Y));
                        any = true;
                    }
                    if (!any) break;
                }
            }

            private static double Dist2d(Point2d a, Point2d b)
            {
                double dx = a.X - b.X;
                double dy = a.Y - b.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }

            /// <summary>
            /// Açılım parçası, etiketteki cm ile aynı boyda (1 cm = 2 çizim birimi).
            /// Köşe kıvrımı (kısa fillet) yerinde kalır; düz parça uzar.
            /// </summary>
            private void ScaleAcilim(Polyline pl, bool side, bool icHook)
            {
                int n = pl.NumberOfVertices;
                var pts = new Point2d[n];
                var orig = new Point2d[n];
                var bulge = new double[Math.Max(0, n - 1)];
                for (int i = 0; i < n; i++)
                    orig[i] = pts[i] = pl.GetPoint2dAt(i);
                for (int i = 0; i < n - 1; i++)
                {
                    try { bulge[i] = pl.GetBulgeAt(i); }
                    catch { bulge[i] = 0; }
                }
                var cur = new double[n - 1];
                var target = new double[n - 1];
                int iVert = -1, iHoriz = -1;
                int iVertAny = -1, iHorizAny = -1;
                double bestV = 0, bestH = 0, bestVAny = 0, bestHAny = 0;
                for (int i = 0; i < n - 1; i++)
                {
                    double dx = pts[i + 1].X - pts[i].X;
                    double dy = pts[i + 1].Y - pts[i].Y;
                    double L = Math.Sqrt(dx * dx + dy * dy);
                    cur[i] = L;
                    target[i] = L;
                    bool straight = Math.Abs(bulge[i]) < 0.15;
                    if (Math.Abs(dy) > Math.Abs(dx))
                    {
                        if (L > bestVAny) { bestVAny = L; iVertAny = i; }
                        if (straight && L > bestV) { bestV = L; iVert = i; }
                    }
                    else
                    {
                        if (L > bestHAny) { bestHAny = L; iHorizAny = i; }
                        if (straight && L > bestH) { bestH = L; iHoriz = i; }
                    }
                }
                if (iVert < 0) { iVert = iVertAny; bestV = bestVAny; }
                if (iHoriz < 0) { iHoriz = iHorizAny; bestH = bestHAny; }
                int hookCm = ElliCapCm(icHook);
                if (side && iVert >= 0 && bestV > 80.0)
                    target[iVert] = StraightForX(pts[0].X) * DuPerCm - 6.0;
                if (side && iHoriz >= 0 && bestH > 80.0)
                    target[iHoriz] = hookCm * DuPerCm - 3.0;
                if (!side && iHoriz >= 0 && bestH > 80.0)
                    target[iHoriz] = YatayDuzCm * DuPerCm - 6.0;
                double gonyeDu = GonyeCm() * DuPerCm - 3.0;
                for (int i = 0; i < n - 1; i++)
                {
                    if (i == iVert || i == iHoriz) continue;
                    if (cur[i] >= 40.0 && cur[i] <= 120.0)
                        target[i] = gonyeDu;
                }

                if (side)
                {
                    double cx = XKotCenter + DW * 0.5;
                    int anchor = Math.Abs(pts[n - 1].X - cx) < Math.Abs(pts[0].X - cx) ? n - 1 : 0;
                    if (anchor == 0)
                    {
                        for (int i = 0; i < n - 1; i++)
                            pts[i + 1] = StepDir(pts[i], orig[i], orig[i + 1], target[i]);
                    }
                    else
                    {
                        for (int i = n - 2; i >= 0; i--)
                            pts[i] = StepDir(pts[i + 1], orig[i + 1], orig[i], target[i]);
                    }
                }
                else if (iHoriz >= 0)
                {
                    int s = iHoriz;
                    double dxs = orig[s + 1].X - orig[s].X;
                    double dys = orig[s + 1].Y - orig[s].Y;
                    double ls = Math.Sqrt(dxs * dxs + dys * dys);
                    if (ls < 1.0) ls = 1.0;
                    double ux = dxs / ls, uy = dys / ls;
                    double midX = (pts[s].X + pts[s + 1].X) * 0.5;
                    double midY = (pts[s].Y + pts[s + 1].Y) * 0.5;
                    double half = target[s] * 0.5;
                    pts[s] = new Point2d(midX - ux * half, midY - uy * half);
                    pts[s + 1] = new Point2d(midX + ux * half, midY + uy * half);
                    for (int i = s - 1; i >= 0; i--)
                        pts[i] = StepDir(pts[i + 1], orig[i + 1], orig[i], target[i]);
                    for (int i = s + 1; i < n - 1; i++)
                        pts[i + 1] = StepDir(pts[i], orig[i], orig[i + 1], target[i]);
                }
                for (int i = 0; i < n; i++)
                    pl.SetPointAt(i, pts[i]);
                for (int i = 0; i < n - 1; i++)
                {
                    if (Math.Abs(target[i] - cur[i]) <= 0.5) continue;
                    try { pl.SetBulgeAt(i, 0); } catch { }
                }
            }

            private static Point2d StepAlong(Point2d from, Point2d toward, double len)
            {
                return StepDir(from, from, toward, len);
            }

            /// <summary>Parçayı şablondaki doğrultuda uzatır. Komşu kısalınca köşe yayı yana devrilmez.</summary>
            private static Point2d StepDir(Point2d from, Point2d origFrom, Point2d origTo, double len)
            {
                double dx = origTo.X - origFrom.X;
                double dy = origTo.Y - origFrom.Y;
                double L = Math.Sqrt(dx * dx + dy * dy);
                if (L < 1e-6) return from;
                return new Point2d(from.X + dx / L * len, from.Y + dy / L * len);
            }

            /// <summary>Parça yazısını (34, 274, 134, 72) ölçeklenmiş parçanın yanına oturtur.</summary>
            private void PlacePieceLabels(Transaction tr, BlockTableRecord ms)
            {
                var segs = new List<SegRef>();
                int bar = 0;
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead, false) is Polyline pl) || pl.IsErased) continue;
                    if (!IsRebarGeom(pl) || pl.NumberOfVertices < 4) continue;
                    bool outside = false;
                    double minY = double.MaxValue;
                    int n = pl.NumberOfVertices;
                    double xOutL = XOutL - DSide;
                    double xOutR = XOutR + DW + DSide;
                    double sx0 = 0, sy0 = 0;
                    for (int i = 0; i < n; i++)
                    {
                        Point2d p = pl.GetPoint2dAt(i);
                        sx0 += p.X;
                        sy0 += p.Y;
                        if (p.X < xOutL - 20.0 || p.X > xOutR + 20.0) outside = true;
                        if (p.Y < minY) minY = p.Y;
                    }
                    if (!outside && minY >= Y4 + Dy4 - 10.0) continue;
                    var cent = new Point2d(sx0 / n, sy0 / n);
                    int before = segs.Count;
                    for (int i = 0; i < n - 1; i++)
                    {
                        Point2d a = pl.GetPoint2dAt(i);
                        Point2d b = pl.GetPoint2dAt(i + 1);
                        double dx = b.X - a.X, dy = b.Y - a.Y;
                        double L = Math.Sqrt(dx * dx + dy * dy);
                        if (L < 30.0) continue;
                        segs.Add(new SegRef { A = a, B = b, Len = L, Bar = bar, Cent = cent });
                    }
                    if (segs.Count > before) bar++;
                }
                if (segs.Count == 0) return;
                var used = new bool[segs.Count];
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForWrite, false) is DBText tx) || tx.IsErased) continue;
                    string s = (tx.TextString ?? "").Trim();
                    if (s.Length == 0 || s.Length > 4) continue;
                    bool digits = true;
                    for (int k = 0; k < s.Length; k++)
                        if (!char.IsDigit(s[k])) { digits = false; break; }
                    if (!digits) continue;
                    if (!int.TryParse(s, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int cm))
                        continue;
                    double want = cm * DuPerCm;
                    Point3d pos = tx.Position;
                    int own = -1;
                    double ownD = 280.0;
                    for (int i = 0; i < segs.Count; i++)
                    {
                        double mx = (segs[i].A.X + segs[i].B.X) * 0.5;
                        double my = (segs[i].A.Y + segs[i].B.Y) * 0.5;
                        double d = Math.Abs(pos.X - mx) + Math.Abs(pos.Y - my);
                        if (d < ownD) { ownD = d; own = segs[i].Bar; }
                    }
                    if (own < 0) continue;
                    int best = -1;
                    double bestScore = double.MaxValue;
                    for (int i = 0; i < segs.Count; i++)
                    {
                        if (used[i] || segs[i].Bar != own) continue;
                        double lenErr = Math.Abs(segs[i].Len - want);
                        if (lenErr > 16.0) continue;
                        double mx = (segs[i].A.X + segs[i].B.X) * 0.5;
                        double my = (segs[i].A.Y + segs[i].B.Y) * 0.5;
                        double d = Math.Abs(pos.X - mx) + Math.Abs(pos.Y - my);
                        double score = lenErr * 80.0 + d;
                        if (score < bestScore) { bestScore = score; best = i; }
                    }
                    if (best < 0) continue;
                    used[best] = true;
                    SegRef g = segs[best];
                    double gx = g.B.X - g.A.X, gy = g.B.Y - g.A.Y;
                    double gl = Math.Sqrt(gx * gx + gy * gy);
                    if (gl < 1) continue;
                    double mx2 = (g.A.X + g.B.X) * 0.5;
                    double my2 = (g.A.Y + g.B.Y) * 0.5;
                    double nx = -gy / gl, ny = gx / gl;
                    double toward = nx * (g.Cent.X - mx2) + ny * (g.Cent.Y - my2);
                    if (toward > 0) { nx = -nx; ny = -ny; }
                    else if (Math.Abs(toward) < 1.0)
                    {
                        double od = (pos.X - mx2) * nx + (pos.Y - my2) * ny;
                        if (od < 0) { nx = -nx; ny = -ny; }
                    }
                    double angle = Math.Atan2(gy, gx);
                    if (angle > Math.PI / 2) angle -= Math.PI;
                    if (angle <= -Math.PI / 2) angle += Math.PI;
                    tx.Rotation = angle;
                    SeatBoxOffEdge(tx, ms.Database, mx2, my2, nx, ny, AcilimTextGapDu);
                }
                PlaceWallHookLabels(tr, ms);
                PlaceYatayAcilimLabels(tr, ms);
                PlaceSideAcilimTexts(tr, ms);
                PlaceSectionEtiket(tr, ms);
            }

            /// <summary>Kesitteki ø etiketi ait olduğu çubuğun yanında. Kutu kenarı çubuğa bakar.</summary>
            private void PlaceSectionEtiket(Transaction tr, BlockTableRecord ms)
            {
                double cx = XKotCenter + DW * 0.5;
                double xLo = XOutL - DSide - 40.0;
                double xHi = XOutR + DW + DSide + 40.0;
                double yCut = Y4 + Dy4 - 30.0;
                string ustTok = TemelCekuruSettings.CapSlash(TemelCekuruSettings.UstCapMm, TemelCekuruSettings.UstAralikCm);
                string altTok = TemelCekuruSettings.CapSlash(TemelCekuruSettings.AltCapMm, TemelCekuruSettings.AltAralikCm);
                string horizUst = UstAdet.ToString(System.Globalization.CultureInfo.InvariantCulture) + ustTok;
                string horizAlt = AltAdet.ToString(System.Globalization.CultureInfo.InvariantCulture) + altTok;
                string vertIc = IcAdet.ToString(System.Globalization.CultureInfo.InvariantCulture) + ustTok;
                string vertDis = DisAdet.ToString(System.Globalization.CultureInfo.InvariantCulture) + altTok;
                string vertIcR = SideAdet(false, true).ToString(System.Globalization.CultureInfo.InvariantCulture) + ustTok;
                string vertDisR = SideAdet(true, true).ToString(System.Globalization.CultureInfo.InvariantCulture) + altTok;
                var bag = new Dictionary<string, List<DBText>>(StringComparer.Ordinal);
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForWrite, false) is DBText tx) || tx.IsErased) continue;
                    string s = (tx.TextString ?? "").Trim();
                    if (s != horizUst && s != horizAlt && s != vertIc && s != vertDis && s != vertIcR && s != vertDisR) continue;
                    if (tx.Position.X < xLo || tx.Position.X > xHi || tx.Position.Y < yCut) continue;
                    if (!bag.TryGetValue(s, out List<DBText> list))
                    {
                        list = new List<DBText>();
                        bag[s] = list;
                    }
                    list.Add(tx);
                }

                DBText ustTx = TakeCenter(bag, horizUst, cx);
                DBText altTx = TakeCenter(bag, horizAlt, cx);
                if (ustTx != null)
                    PlaceSectionGroup(tr, ms, ustTx, PhysicalDot(6), PhysicalDot(13), UstAdet, 0, -10);
                if (altTx != null)
                    PlaceSectionGroup(tr, ms, altTx, PhysicalDot(7), PhysicalDot(16), AltAdet, 0, 10);

                DBText icL = TakeSide(bag, vertIc, cx, true);
                DBText icR = TakeSide(bag, vertIcR, cx, false);
                DBText disL = TakeSide(bag, vertDis, cx, true);
                DBText disR = TakeSide(bag, vertDisR, cx, false);
                if (disL != null) PlaceSectionGroup(tr, ms, disL, NamedDot(3), NamedDot(7), DisAdet, -15, 0);
                if (icL != null) PlaceSectionGroup(tr, ms, icL, NamedDot(2), NamedDot(6), IcAdet, -10, 0);
                if (disR != null) PlaceSectionGroup(tr, ms, disR, NamedDot(12), NamedDot(16), SideAdet(true, true), 15, 0);
                if (icR != null) PlaceSectionGroup(tr, ms, icR, NamedDot(9), NamedDot(13), SideAdet(false, true), 10, 0);
            }

            // The reference DWG labels dot rows, rather than the adjacent continuous bars.
            private void PlaceSectionGroup(Transaction tr, BlockTableRecord ms, DBText text,
                Point3d start, Point3d end, int count, double dx, double dy)
            {
                if (count <= 0) { text.Erase(); return; }
                bool vertical = dx != 0;
                var dots = new List<Point3d>();
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity entity) || !TryDotCenter(entity, out Point3d dot)) continue;
                    if (entity.Layer.IndexOf("DONATI", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    double cross = vertical ? Math.Abs(dot.X - start.X) : Math.Abs(dot.Y - start.Y);
                    double t = vertical ? dot.Y : dot.X;
                    double lo = vertical ? Math.Min(start.Y, end.Y) : Math.Min(start.X, end.X);
                    double hi = vertical ? Math.Max(start.Y, end.Y) : Math.Max(start.X, end.X);
                    if (cross > NamedDotTolerance || t < lo - NamedDotTolerance || t > hi + NamedDotTolerance) continue;
                    // Inner groups exclude their junction; outer groups include the intermediate corner.
                    if (vertical)
                    {
                        bool corner = false;
                        for (int number = 1; number <= 16; number++)
                            if (PhysicalDot(number).DistanceTo(dot) < NamedDotTolerance)
                            {
                                bool outerJunction = Math.Abs(dx) == 15
                                    && dot.DistanceTo(start) > NamedDotTolerance && dot.DistanceTo(end) > NamedDotTolerance;
                                corner = !outerJunction; break;
                            }
                        if (corner) continue;
                    }
                    if (!dots.Exists(p => p.DistanceTo(dot) < NamedDotTolerance)) dots.Add(dot);
                }
                double center = vertical ? (start.Y + end.Y) / 2 : (start.X + end.X) / 2;
                dots.Sort((a, b) => Math.Abs((vertical ? a.Y : a.X) - center).CompareTo(Math.Abs((vertical ? b.Y : b.X) - center)));
                if (dots.Count > count) dots.RemoveRange(count, dots.Count - count);
                dots.Sort((a, b) => (vertical ? a.Y : a.X).CompareTo(vertical ? b.Y : b.X));
                if (dots.Count == 0) return;
                const string layer = "ETIKET CIZGISI (BEYKENT)";
                EnsureKotLayer(tr, ms.Database, layer);
                var offset = new Vector3d(dx, dy, 0);
                Point3d first = dots[0] + offset, last = dots[dots.Count - 1] + offset;
                void LineTo(Point3d a, Point3d b)
                {
                    if (a.DistanceTo(b) < NamedDotTolerance) return;
                    var line = new Line(a, b) { Layer = layer, ColorIndex = 256 };
                    ms.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                }
                LineTo(first, last);
                foreach (Point3d dot in dots) LineTo(dot, dot + offset);
                double x = (first.X + last.X) / 2, y = (first.Y + last.Y) / 2;
                text.Rotation = vertical ? Math.PI / 2 : 0;
                const double labelGap = 1 * DuPerCm;
                if (vertical) SeatBoxOffEdge(text, ms.Database, x, y, Math.Sign(dx), 0, labelGap);
                else if (dy < 0) SeatMaxY(text, ms.Database, x, y - labelGap);
                else SeatMinY(text, ms.Database, x, y + labelGap);
            }

            private static DBText TakeCenter(Dictionary<string, List<DBText>> bag, string key, double cx)
            {
                if (!bag.TryGetValue(key, out List<DBText> list) || list.Count == 0) return null;
                DBText best = list[0];
                double bestD = Math.Abs(best.Position.X - cx);
                for (int i = 1; i < list.Count; i++)
                {
                    double d = Math.Abs(list[i].Position.X - cx);
                    if (d < bestD) { bestD = d; best = list[i]; }
                }
                list.Remove(best);
                return best;
            }

            private static DBText TakeSide(Dictionary<string, List<DBText>> bag, string key, double cx, bool left)
            {
                if (!bag.TryGetValue(key, out List<DBText> list) || list.Count == 0) return null;
                DBText best = null;
                foreach (DBText tx in list)
                {
                    if ((tx.Position.X < cx) != left) continue;
                    best = tx;
                    break;
                }
                if (best == null) return null;
                list.Remove(best);
                return best;
            }

            private static bool TryLongHoriz(Polyline pl, out double y, out double x0, out double x1, out double len)
            {
                y = 0;
                x0 = x1 = 0;
                len = 0;
                int n = pl.NumberOfVertices;
                int best = -1;
                double bestL = 0;
                for (int i = 0; i < n - 1; i++)
                {
                    Point2d a = pl.GetPoint2dAt(i);
                    Point2d b = pl.GetPoint2dAt(i + 1);
                    double dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
                    if (dx <= dy) continue;
                    double L = Math.Sqrt(dx * dx + dy * dy);
                    if (L <= bestL) continue;
                    bestL = L;
                    best = i;
                }
                if (best < 0) return false;
                Point2d p = pl.GetPoint2dAt(best);
                Point2d q = pl.GetPoint2dAt(best + 1);
                y = (p.Y + q.Y) * 0.5;
                x0 = Math.Min(p.X, q.X);
                x1 = Math.Max(p.X, q.X);
                len = bestL;
                return true;
            }

            private static double VertMidY(Polyline pl)
            {
                int n = pl.NumberOfVertices;
                double best = 0, y = 0;
                for (int i = 0; i < n - 1; i++)
                {
                    Point2d a = pl.GetPoint2dAt(i);
                    Point2d b = pl.GetPoint2dAt(i + 1);
                    double dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
                    if (dy <= dx) continue;
                    double L = Math.Sqrt(dx * dx + dy * dy);
                    if (L <= best) continue;
                    best = L;
                    y = (a.Y + b.Y) * 0.5;
                }
                return y;
            }

            /// <summary>Yazı kutusu kesit boşluğunun üstüne taşmasın.</summary>
            private static void PullBelow(DBText tx, double yMax)
            {
                try
                {
                    Extents3d e = tx.GeometricExtents;
                    if (e.MaxPoint.Y <= yMax) return;
                    tx.TransformBy(Matrix3d.Displacement(new Vector3d(0, yMax - e.MaxPoint.Y, 0)));
                }
                catch { }
            }

            /// <summary>Aynı hizada binen iki etiketi üst üste ayırır. Dıştaki yukarıda kalır.</summary>
            private static void Unstack(DBText outer, DBText inner)
            {
                try
                {
                    Extents3d a = outer.GeometricExtents;
                    Extents3d b = inner.GeometricExtents;
                    if (a.MaxPoint.X < b.MinPoint.X - 2.0 || b.MaxPoint.X < a.MinPoint.X - 2.0) return;
                    double overlap = Math.Min(a.MaxPoint.Y, b.MaxPoint.Y) - Math.Max(a.MinPoint.Y, b.MinPoint.Y);
                    if (overlap <= 0) return;
                    double shift = overlap * 0.5 + 4.0;
                    outer.TransformBy(Matrix3d.Displacement(new Vector3d(0, shift, 0)));
                    inner.TransformBy(Matrix3d.Displacement(new Vector3d(0, -shift, 0)));
                }
                catch { }
            }

            /// <summary>Yan açılımın boy yazısı ve düşey etiketi çubuğun üzerinde.</summary>
            private void PlaceSideAcilimTexts(Transaction tr, BlockTableRecord ms)
            {
                double cx = XKotCenter + DW * 0.5;
                double xLo = XOutL - DSide - 30.0;
                double xHi = XOutR + DW + DSide + 30.0;
                double yCut = Y4 + Dy4 - 20.0;
                var bars = new List<Polyline>();
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead, false) is Polyline pl) || pl.IsErased) continue;
                    if (!IsRebarGeom(pl) || !BarIsWall(pl)) continue;
                    bool outside = false;
                    double minY = double.MaxValue;
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                    {
                        Point2d p = pl.GetPoint2dAt(i);
                        if (p.X < xLo || p.X > xHi) outside = true;
                        if (p.Y < minY) minY = p.Y;
                    }
                    if (outside || minY < yCut) bars.Add(pl);
                }
                var nums = new List<DBText>();
                var tags = new List<DBText>();
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForWrite, false) is DBText tx) || tx.IsErased) continue;
                    string s = (tx.TextString ?? "").Trim();
                    if (s.IndexOf("DİKEY", StringComparison.Ordinal) >= 0)
                    {
                        tags.Add(tx);
                        continue;
                    }
                    if (tx.Position.X > xLo && tx.Position.X < xHi) continue;
                    if (s.Length == 0 || s.Length > 4) continue;
                    bool digits = true;
                    for (int k = 0; k < s.Length; k++)
                        if (!char.IsDigit(s[k])) { digits = false; break; }
                    if (digits) nums.Add(tx);
                }
                var used = new HashSet<DBText>();
                foreach (Polyline bar in bars)
                {
                    double vx = VertX(bar);
                    bool left = vx < cx;
                    if (!TryLegs(bar, out Point2d v0, out Point2d v1, out Point2d h0, out Point2d h1, out Point2d g0, out Point2d g1))
                        continue;
                    double vLen = Dist(v0, v1);
                    double hLen = Dist(h0, h1);
                    double gLen = Dist(g0, g1);
                    double vmy = (v0.Y + v1.Y) * 0.5;
                    bool hookBottom = (h0.Y + h1.Y) * 0.5 < vmy;
                    bool gonyeTop = (g0.Y + g1.Y) * 0.5 > vmy;
                    SeatNumber(tr, ms, nums, used, left, cx, vLen, new Point3d(vx + 14.0, vmy, 0), true);
                    SeatNumber(tr, ms, nums, used, left, cx, hLen, new Point3d((h0.X + h1.X) * 0.5, (h0.Y + h1.Y) * 0.5, 0), normalY: hookBottom ? -1 : 1);
                    SeatNumber(tr, ms, nums, used, left, cx, gLen, new Point3d((g0.X + g1.X) * 0.5, (g0.Y + g1.Y) * 0.5, 0), normalY: gonyeTop ? 1 : -1);
                    bool dis = IsOuterWall(bar, bars, cx);
                    foreach (DBText tag in tags)
                    {
                        if (used.Contains(tag)) continue;
                        if ((tag.Position.X < cx) != left) continue;
                        string ts = tag.TextString ?? "";
                        bool tagDis = ts.IndexOf("DIŞ", StringComparison.Ordinal) >= 0;
                        if (tagDis != dis) continue;
                        used.Add(tag);
                        SeatAlongBar(tag, ms.Database, vx - AcilimTextGapDu, vmy, edgeIsRight: true);
                        break;
                    }
                }
            }

            private static bool IsOuterWall(Polyline bar, List<Polyline> bars, double cx)
            {
                double x = VertX(bar);
                bool left = x < cx;
                double best = x;
                foreach (Polyline p in bars)
                {
                    double px = VertX(p);
                    if ((px < cx) != left) continue;
                    if (left ? px > best : px < best) best = px;
                }
                return Math.Abs(x - best) < 1.0;
            }

            private static bool TryLegs(Polyline pl, out Point2d v0, out Point2d v1, out Point2d h0, out Point2d h1, out Point2d g0, out Point2d g1)
            {
                v0 = v1 = h0 = h1 = g0 = g1 = Point2d.Origin;
                int n = pl.NumberOfVertices;
                int iV = -1, iH = -1, iG = -1;
                double bestV = 0, bestH = 0;
                for (int i = 0; i < n - 1; i++)
                {
                    Point2d a = pl.GetPoint2dAt(i);
                    Point2d b = pl.GetPoint2dAt(i + 1);
                    double dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
                    double L = Math.Sqrt(dx * dx + dy * dy);
                    if (L < 20.0) continue;
                    if (dy > dx)
                    {
                        if (L > bestV) { bestV = L; iV = i; }
                    }
                    else if (L > bestH)
                    {
                        bestH = L;
                        iH = i;
                    }
                }
                double bestG = 0;
                for (int i = 0; i < n - 1; i++)
                {
                    if (i == iH) continue;
                    Point2d a = pl.GetPoint2dAt(i);
                    Point2d b = pl.GetPoint2dAt(i + 1);
                    double dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
                    if (dx <= dy) continue;
                    double L = Math.Sqrt(dx * dx + dy * dy);
                    if (L < 20.0 || L <= bestG) continue;
                    bestG = L;
                    iG = i;
                }
                if (iV < 0 || iH < 0 || iG < 0) return false;
                v0 = pl.GetPoint2dAt(iV);
                v1 = pl.GetPoint2dAt(iV + 1);
                h0 = pl.GetPoint2dAt(iH);
                h1 = pl.GetPoint2dAt(iH + 1);
                g0 = pl.GetPoint2dAt(iG);
                g1 = pl.GetPoint2dAt(iG + 1);
                return true;
            }

            private static double Dist(Point2d a, Point2d b)
            {
                double dx = b.X - a.X, dy = b.Y - a.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }

            private static void SeatNumber(Transaction tr, BlockTableRecord ms, List<DBText> nums, HashSet<DBText> used, bool left, double cx, double segLen, Point3d at, bool vertical = false, int normalY = 0)
            {
                DBText best = null;
                double bestD = double.MaxValue;
                foreach (DBText tx in nums)
                {
                    if (used.Contains(tx)) continue;
                    if ((tx.Position.X < cx) != left) continue;
                    if (!int.TryParse((tx.TextString ?? "").Trim(), System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int cm))
                        continue;
                    if (Math.Abs(cm * DuPerCm - segLen) > 12.0) continue;
                    double d = Math.Abs(tx.Position.X - at.X) + Math.Abs(tx.Position.Y - at.Y);
                    if (d < bestD) { bestD = d; best = tx; }
                }
                if (best == null) return;
                used.Add(best);
                best.Rotation = vertical ? Math.PI / 2 : 0;
                if (vertical) SeatBoxOffEdge(best, ms.Database, at.X - 14, at.Y, 1, 0, AcilimTextGapDu);
                else if (normalY > 0) SeatMinY(best, ms.Database, at.X, at.Y + AcilimTextGapDu);
                else if (normalY < 0) SeatMaxY(best, ms.Database, at.X, at.Y - AcilimTextGapDu);
                else MoveTextCenter(best, ms.Database, at);
            }

            private static void MoveTextCenter(DBText tx, Database db, Point3d center)
            {
                tx.Position = center;
                if (tx.HorizontalMode != TextHorizontalMode.TextLeft || tx.VerticalMode != TextVerticalMode.TextBase)
                    tx.AlignmentPoint = center;
                try
                {
                    tx.AdjustAlignment(db);
                    Extents3d e = tx.GeometricExtents;
                    var c = new Point3d((e.MinPoint.X + e.MaxPoint.X) * 0.5, (e.MinPoint.Y + e.MaxPoint.Y) * 0.5, 0);
                    tx.TransformBy(Matrix3d.Displacement(center - c));
                }
                catch { }
            }

            /// <summary>Yazı kutusunun üst kenarı çubuğun hemen altında.</summary>
            private static void SeatMaxY(DBText tx, Database db, double centerX, double maxY)
            {
                tx.Rotation = 0;
                var rough = new Point3d(centerX, maxY, 0);
                tx.Position = rough;
                if (tx.HorizontalMode != TextHorizontalMode.TextLeft || tx.VerticalMode != TextVerticalMode.TextBase)
                    tx.AlignmentPoint = rough;
                try
                {
                    tx.AdjustAlignment(db);
                    Extents3d e = tx.GeometricExtents;
                    double cx = (e.MinPoint.X + e.MaxPoint.X) * 0.5;
                    tx.TransformBy(Matrix3d.Displacement(new Vector3d(centerX - cx, maxY - e.MaxPoint.Y, 0)));
                }
                catch { }
            }

            /// <summary>Yazı kutusunun alt kenarı çubuğun hemen üstünde.</summary>
            private static void SeatMinY(DBText tx, Database db, double centerX, double minY)
            {
                tx.Rotation = 0;
                var rough = new Point3d(centerX, minY, 0);
                tx.Position = rough;
                if (tx.HorizontalMode != TextHorizontalMode.TextLeft || tx.VerticalMode != TextVerticalMode.TextBase)
                    tx.AlignmentPoint = rough;
                try
                {
                    tx.AdjustAlignment(db);
                    Extents3d e = tx.GeometricExtents;
                    double cx = (e.MinPoint.X + e.MaxPoint.X) * 0.5;
                    tx.TransformBy(Matrix3d.Displacement(new Vector3d(centerX - cx, minY - e.MinPoint.Y, 0)));
                }
                catch { }
            }

            /// <summary>Etiket ana donatı boyunca dikey. Kutu kenarı çubuğa bakar.</summary>
            private static void SeatAlongBar(DBText tx, Database db, double edgeX, double centerY, bool edgeIsRight)
            {
                tx.Rotation = Math.PI / 2.0;
                var rough = new Point3d(edgeX, centerY, 0);
                tx.Position = rough;
                if (tx.HorizontalMode != TextHorizontalMode.TextLeft || tx.VerticalMode != TextVerticalMode.TextBase)
                    tx.AlignmentPoint = rough;
                try
                {
                    tx.AdjustAlignment(db);
                    Extents3d e = tx.GeometricExtents;
                    double edge = edgeIsRight ? e.MaxPoint.X : e.MinPoint.X;
                    double cy = (e.MinPoint.Y + e.MaxPoint.Y) * 0.5;
                    tx.TransformBy(Matrix3d.Displacement(new Vector3d(edgeX - edge, centerY - cy, 0)));
                }
                catch { }
            }

            /// <summary>
            /// Alt açılımlar: donatı etiketi üstte, düz boy altta; gönye yazıları düşey kola paralel.
            /// </summary>
            private void PlaceYatayAcilimLabels(Transaction tr, BlockTableRecord ms)
            {
                double yCut = Y4 + Dy4 - 20.0;
                double xLo = XOutL - DSide - 40.0;
                double xHi = XOutR + DW + DSide + 40.0;
                var bars = new List<Polyline>();
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead, false) is Polyline pl) || pl.IsErased) continue;
                    if (!IsRebarGeom(pl) || BarIsWall(pl) || pl.NumberOfVertices < 4) continue;
                    double minY = double.MaxValue, maxX = double.MinValue, minX = double.MaxValue;
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                    {
                        Point2d p = pl.GetPoint2dAt(i);
                        if (p.Y < minY) minY = p.Y;
                        if (p.X < minX) minX = p.X;
                        if (p.X > maxX) maxX = p.X;
                    }
                    if (minY >= yCut) continue;
                    if (maxX < xLo || minX > xHi) continue;
                    if (!TryLongHoriz(pl, out _, out _, out _, out double hLen) || hLen < 150.0) continue;
                    bars.Add(pl);
                }
                if (bars.Count == 0) return;
                int gCm = GonyeCm();
                var calls = new List<DBText>();
                var gonye = new List<DBText>();
                var lengths = new List<DBText>();
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForWrite, false) is DBText tx) || tx.IsErased) continue;
                    if (tx.Position.Y > yCut + 40.0) continue;
                    string s = (tx.TextString ?? "").Trim();
                    if (IsYatayAcilim(s, out _) && s.IndexOf("DİKEY", StringComparison.Ordinal) < 0)
                    {
                        calls.Add(tx);
                        continue;
                    }
                    if (ParseCm(tx) == gCm) gonye.Add(tx);
                    if (ParseCm(tx) > 0) lengths.Add(tx);
                }
                var used = new HashSet<DBText>();
                foreach (Polyline pl in bars)
                {
                    if (!TryLongHoriz(pl, out double hy, out double x0, out double x1, out double mainLength)) continue;
                    bool upper = !HooksUp(pl);
                    double midX = (x0 + x1) * 0.5;
                    DBText call = null;
                    double callD = double.MaxValue;
                    foreach (DBText tx in calls)
                    {
                        if (used.Contains(tx)) continue;
                        if (!IsYatayAcilim(tx.TextString, out bool isUpper) || isUpper != upper) continue;
                        if (tx.Position.X < x0 - 80.0 || tx.Position.X > x1 + 80.0) continue;
                        double d = Math.Abs(tx.Position.X - midX) + Math.Abs(tx.Position.Y - hy);
                        if (d < callD) { callD = d; call = tx; }
                    }
                    if (call != null)
                    {
                        used.Add(call);
                        SeatMinY(call, ms.Database, midX, hy + AcilimTextGapDu);
                    }
                    DBText length = null;
                    double lengthDistance = double.MaxValue;
                    foreach (DBText tx in lengths)
                    {
                        if (used.Contains(tx) || Math.Abs(ParseCm(tx) * DuPerCm - mainLength) > 12) continue;
                        double d = Math.Abs(tx.Position.X - midX) + Math.Abs(tx.Position.Y - hy);
                        if (d < lengthDistance) { lengthDistance = d; length = tx; }
                    }
                    if (length != null)
                    {
                        used.Add(length);
                        SeatMaxY(length, ms.Database, midX, hy - AcilimTextGapDu);
                    }
                    int n = pl.NumberOfVertices;
                    int leftSeg = -1, rightSeg = -1;
                    double leftX = double.MaxValue, rightX = double.MinValue;
                    for (int i = 0; i < n - 1; i++)
                    {
                        Point2d a = pl.GetPoint2dAt(i);
                        Point2d b = pl.GetPoint2dAt(i + 1);
                        double dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
                        double L = Math.Sqrt(dx * dx + dy * dy);
                        if (dy <= dx || L < 40.0) continue;
                        double mx = (a.X + b.X) * 0.5;
                        if (mx < leftX) { leftX = mx; leftSeg = i; }
                        if (mx > rightX) { rightX = mx; rightSeg = i; }
                    }
                    if (leftSeg >= 0)
                        SeatAcilimGonye(pl, leftSeg, gonye, used, ms.Database, inward: upper ? -1.0 : 1.0);
                    if (rightSeg >= 0 && rightSeg != leftSeg)
                        SeatAcilimGonye(pl, rightSeg, gonye, used, ms.Database, inward: upper ? -1.0 : 1.0);
                }
            }

            private static void SeatAcilimGonye(Polyline pl, int seg, List<DBText> texts, HashSet<DBText> used, Database db, double inward)
            {
                Point2d a = pl.GetPoint2dAt(seg);
                Point2d b = pl.GetPoint2dAt(seg + 1);
                double hx = (a.X + b.X) * 0.5;
                double hy = (a.Y + b.Y) * 0.5;
                DBText best = null;
                double bestD = double.MaxValue;
                foreach (DBText tx in texts)
                {
                    if (used.Contains(tx)) continue;
                    double d = Math.Abs(tx.Position.X - hx) + Math.Abs(tx.Position.Y - hy);
                    if (d < bestD) { bestD = d; best = tx; }
                }
                if (best == null) return;
                used.Add(best);
                best.Rotation = Math.PI / 2;
                SeatBoxOffEdge(best, db, hx, hy, inward, 0, AcilimTextGapDu);
            }

            /// <summary>
            /// Yan açılımda üst/alt kanca yazısını o kancanın dış kenarına alır.
            /// İç çubukta 50ø üstte, gönye altta. Dış çubukta gönye üstte, 50ø altta.
            /// </summary>
            private void PlaceWallHookLabels(Transaction tr, BlockTableRecord ms)
            {
                double xLo = XOutL - DSide - 30.0;
                double xHi = XOutR + DW + DSide + 30.0;
                double cx = XKotCenter + DW * 0.5;
                double gLen = GonyeCm() * DuPerCm - 3.0;
                int gCm = GonyeCm();
                var bars = new List<Polyline>();
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead, false) is Polyline pl) || pl.IsErased) continue;
                    if (!IsRebarGeom(pl) || !BarIsWall(pl) || pl.NumberOfVertices < 6) continue;
                    bool outside = false;
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                    {
                        double x = pl.GetPoint2dAt(i).X;
                        if (x < xLo || x > xHi) outside = true;
                    }
                    if (outside) bars.Add(pl);
                }
                if (bars.Count == 0) return;
                var texts = new List<DBText>();
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForWrite, false) is DBText tx) || tx.IsErased) continue;
                    string s = (tx.TextString ?? "").Trim();
                    int cm;
                    if (!int.TryParse(s, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out cm))
                        continue;
                    if (cm != gCm && cm != ElliCapCm(true) && cm != ElliCapCm(false)) continue;
                    texts.Add(tx);
                }
                var hooks = new List<HookSlot>();
                foreach (Polyline pl in bars)
                {
                    int n = pl.NumberOfVertices;
                    int topSeg = -1, botSeg = -1;
                    double topY = double.MinValue, botY = double.MaxValue;
                    int[] ends = { 0, n - 2 };
                    for (int e = 0; e < ends.Length; e++)
                    {
                        int seg = ends[e];
                        Point2d a = pl.GetPoint2dAt(seg);
                        Point2d b = pl.GetPoint2dAt(seg + 1);
                        if (Math.Abs(b.X - a.X) <= Math.Abs(b.Y - a.Y)) continue;
                        double my = (a.Y + b.Y) * 0.5;
                        if (my > topY) { topY = my; topSeg = seg; }
                        if (my < botY) { botY = my; botSeg = seg; }
                    }
                    if (topSeg < 0 || botSeg < 0) continue;
                    double topL = Dist2d(pl.GetPoint2dAt(topSeg), pl.GetPoint2dAt(topSeg + 1));
                    double botL = Dist2d(pl.GetPoint2dAt(botSeg), pl.GetPoint2dAt(botSeg + 1));
                    bool topIsGonye = Math.Abs(topL - gLen) <= Math.Abs(botL - gLen);
                    hooks.Add(new HookSlot { Pl = pl, Seg = topSeg, Cm = topIsGonye ? gCm : ClosestElli(topL) });
                    hooks.Add(new HookSlot { Pl = pl, Seg = botSeg, Cm = topIsGonye ? ClosestElli(botL) : gCm });
                }
                var usedTx = new bool[texts.Count];
                var usedHk = new bool[hooks.Count];
                var pairs = new List<HookPair>();
                for (int h = 0; h < hooks.Count; h++)
                {
                    Point2d a = hooks[h].Pl.GetPoint2dAt(hooks[h].Seg);
                    Point2d b = hooks[h].Pl.GetPoint2dAt(hooks[h].Seg + 1);
                    double mx = (a.X + b.X) * 0.5, my = (a.Y + b.Y) * 0.5;
                    bool left = mx < cx;
                    for (int t = 0; t < texts.Count; t++)
                    {
                        if (ParseCm(texts[t]) != hooks[h].Cm) continue;
                        bool txLeft = texts[t].Position.X < cx;
                        if (txLeft != left) continue;
                        double d = Math.Abs(texts[t].Position.X - mx) + Math.Abs(texts[t].Position.Y - my);
                        pairs.Add(new HookPair { H = h, T = t, D = d });
                    }
                }
                pairs.Sort((p, q) => p.D.CompareTo(q.D));
                foreach (HookPair pair in pairs)
                {
                    if (usedHk[pair.H] || usedTx[pair.T]) continue;
                    usedHk[pair.H] = true;
                    usedTx[pair.T] = true;
                    HookSlot hk = hooks[pair.H];
                    Point2d a = hk.Pl.GetPoint2dAt(hk.Seg);
                    Point2d b = hk.Pl.GetPoint2dAt(hk.Seg + 1);
                    double gl = Dist2d(a, b);
                    if (gl < 1) continue;
                    double mx = (a.X + b.X) * 0.5, my = (a.Y + b.Y) * 0.5;
                    double nx = -(b.Y - a.Y) / gl, ny = (b.X - a.X) / gl;
                    double vx = 0, vy = 0;
                    int n = hk.Pl.NumberOfVertices;
                    for (int i = 0; i < n; i++)
                    {
                        vx += hk.Pl.GetPoint2dAt(i).X;
                        vy += hk.Pl.GetPoint2dAt(i).Y;
                    }
                    vx = vx / n - mx;
                    vy = vy / n - my;
                    if (nx * vx + ny * vy > 0) { nx = -nx; ny = -ny; }
                    SeatBoxOffEdge(texts[pair.T], ms.Database, mx, my, nx, ny, AcilimTextGapDu);
                }
            }

            private static int ClosestElli(double lenDu)
            {
                int ust = ElliCapCm(true);
                int alt = ElliCapCm(false);
                double du = lenDu / DuPerCm;
                return Math.Abs(du - ust) <= Math.Abs(du - alt) ? ust : alt;
            }

            private static int ParseCm(DBText tx)
            {
                int cm;
                if (!int.TryParse((tx.TextString ?? "").Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out cm))
                    return -1;
                return cm;
            }

            private struct HookSlot
            {
                public Polyline Pl;
                public int Seg;
                public int Cm;
            }

            private struct HookPair
            {
                public int H, T;
                public double D;
            }

            /// <summary>Yazı kutusunun gönyeye bakan kenarı, parçadan <paramref name="gap"/> kadar dışarıda.</summary>
            private static void SeatBoxOffEdge(DBText tx, Database db, double mx, double my, double nx, double ny, double gap)
            {
                var p = new Point3d(mx, my, 0);
                tx.Position = p;
                if (tx.HorizontalMode != TextHorizontalMode.TextLeft || tx.VerticalMode != TextVerticalMode.TextBase)
                    tx.AlignmentPoint = p;
                try
                {
                    tx.AdjustAlignment(db);
                    Extents3d e = tx.GeometricExtents;
                    double minP = double.MaxValue;
                    double[] xs = { e.MinPoint.X, e.MaxPoint.X };
                    double[] ys = { e.MinPoint.Y, e.MaxPoint.Y };
                    for (int i = 0; i < 2; i++)
                        for (int j = 0; j < 2; j++)
                        {
                            double pr = xs[i] * nx + ys[j] * ny;
                            if (pr < minP) minP = pr;
                        }
                    double shift = (mx * nx + my * ny + gap) - minP;
                    tx.TransformBy(Matrix3d.Displacement(new Vector3d(nx * shift, ny * shift, 0)));
                }
                catch { }
            }

            private struct SegRef
            {
                public Point2d A, B, Cent;
                public double Len;
                public int Bar;
            }

            /// <summary>İşaretli 72 cm gönye = 50ø. İç üst çap, dış alt çap.</summary>
            private static int ElliCapCm(bool ic)
            {
                int cap = ic ? TemelCekuruSettings.UstCapMm : TemelCekuruSettings.AltCapMm;
                int cm = (int)Math.Round(5.0 * cap, MidpointRounding.AwayFromZero);
                return cm < 2 ? 2 : cm;
            }

            /// <summary>72 cm kancayı 50ø boyuna çeker. Düz parça şablonda 70,5 cm.</summary>
            private static void AdjustElliGonye(Polyline pl)
            {
                int n = pl.NumberOfVertices;
                if (n < 4) return;
                int cm = ElliCapCm(IsIcHookBar(pl));
                double delta = (cm - 72) * DuPerCm;
                if (Math.Abs(delta) < 0.01) return;
                StretchLongHook(pl, 0, delta);
                StretchLongHook(pl, n - 1, delta);
            }

            private static bool IsIcHookBar(Polyline pl)
            {
                int n = pl.NumberOfVertices;
                double legX = pl.GetPoint2dAt(0).X;
                double best = 0;
                for (int i = 0; i < n - 1; i++)
                {
                    Point2d a = pl.GetPoint2dAt(i);
                    Point2d b = pl.GetPoint2dAt(i + 1);
                    double dx = Math.Abs(b.X - a.X);
                    double dy = Math.Abs(b.Y - a.Y);
                    if (dy <= dx || dy <= best) continue;
                    best = dy;
                    legX = (a.X + b.X) * 0.5;
                }
                if (legX < 1900.0) return legX > 1653.0;
                if (legX > 2700.0) return legX < 2873.0;
                const double iL = 2079.647170209089;
                const double oL = 2017.647170209089;
                const double iR = 2497.647170209089;
                const double oR = 2559.647170209089;
                double dIn = Math.Min(Math.Abs(legX - iL), Math.Abs(legX - iR));
                double dOut = Math.Min(Math.Abs(legX - oL), Math.Abs(legX - oR));
                return dIn <= dOut;
            }

            private static void StretchLongHook(Polyline pl, int tip, double delta)
            {
                int neck = tip == 0 ? 1 : tip - 1;
                Point2d a = pl.GetPoint2dAt(tip);
                Point2d b = pl.GetPoint2dAt(neck);
                double dx = a.X - b.X;
                double dy = a.Y - b.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 130.0 || len > 155.0) return;
                double neu = len + delta;
                if (neu < DuPerCm) neu = DuPerCm;
                double s = neu / len;
                pl.SetPointAt(tip, new Point2d(b.X + dx * s, b.Y + dy * s));
            }

            /// <summary>Yandaki düşey açılımın düz boyu: perde + alt temel − 2×paspayı.</summary>
            private void AdjustDikeyDuz(Polyline pl)
            {
                double delta = (StraightForX(pl.GetPoint2dAt(0).X) - 134) * DuPerCm;
                if (Math.Abs(delta) < 0.01) return;
                int n = pl.NumberOfVertices;
                if (n < 4) return;
                for (int i = 0; i < n; i++)
                {
                    if (!IsSideBar(pl.GetPoint2dAt(i).X)) return;
                }
                double yBot = 0;
                double best = 0;
                bool found = false;
                for (int i = 0; i < n - 1; i++)
                {
                    Point2d a = pl.GetPoint2dAt(i);
                    Point2d b = pl.GetPoint2dAt(i + 1);
                    double dy = Math.Abs(b.Y - a.Y);
                    double dx = Math.Abs(b.X - a.X);
                    if (dy <= dx || dy < 200.0 || dy <= best) continue;
                    best = dy;
                    yBot = Math.Min(a.Y, b.Y);
                    found = true;
                }
                if (!found) return;
                double shift = -delta;
                for (int i = 0; i < n; i++)
                {
                    Point2d p = pl.GetPoint2dAt(i);
                    if (p.Y <= yBot + 1.0)
                        pl.SetPointAt(i, new Point2d(p.X, p.Y + shift));
                }
            }

            /// <summary>Gönye = alt temel − 2×paspayı. Şablonda 40−6 = 34 cm.</summary>
            private static int GonyeCm()
            {
                double g = TemelCekuruSettings.AltTemelYuksekligiCm - 2.0 * TemelCekuruSettings.PaspayiCm;
                int n = (int)Math.Round(g, MidpointRounding.AwayFromZero);
                return n < 2 ? 2 : n;
            }

            /// <summary>Dışarıdaki açılım, eşleşen kesit çubuğunun aynı şeklidir.</summary>
            private void CopySectionShapes(Transaction tr, BlockTableRecord ms)
            {
                double xLo = XOutL - DSide - 30.0;
                double xHi = XOutR + DW + DSide + 30.0;
                double yCut = Y4 + Dy4 - 20.0;
                double cx = XKotCenter + DW * 0.5;
                var secU = new List<Polyline>();
                var aciU = new List<Polyline>();
                var secW = new List<Polyline>();
                var aciW = new List<Polyline>();
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Polyline pl) || pl.IsErased) continue;
                    if (!IsRebarGeom(pl) || pl.NumberOfVertices != 6) continue;
                    bool outside = false;
                    double minY = double.MaxValue;
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                    {
                        Point2d p = pl.GetPoint2dAt(i);
                        if (p.X < xLo || p.X > xHi) outside = true;
                        if (p.Y < minY) minY = p.Y;
                    }
                    bool acilim = outside || minY < yCut;
                    bool wall = BarIsWall(pl);
                    if (wall)
                    {
                        if (acilim) aciW.Add(pl);
                        else secW.Add(pl);
                    }
                    else if (acilim) aciU.Add(pl);
                    else secU.Add(pl);
                }
                foreach (Polyline src in secU)
                {
                    bool up = HooksUp(src);
                    Polyline dst = null;
                    foreach (Polyline p in aciU)
                    {
                        if (HooksUp(p) == up) { dst = p; break; }
                    }
                    if (dst == null) continue;
                    aciU.Remove(dst);
                    PasteShape(src, dst, false, false);
                }
                PairWalls(secW, aciW, cx, left: true);
                PairWalls(secW, aciW, cx, left: false);
            }

            private void PairWalls(List<Polyline> sec, List<Polyline> aci, double cx, bool left)
            {
                var s = new List<Polyline>();
                var a = new List<Polyline>();
                foreach (Polyline p in sec)
                {
                    double x = VertX(p);
                    if (left ? x < cx : x >= cx) s.Add(p);
                }
                foreach (Polyline p in aci)
                {
                    double x = CentroidX(p);
                    if (left ? x < cx : x >= cx) a.Add(p);
                }
                s.Sort((p, q) => left ? VertX(p).CompareTo(VertX(q)) : VertX(q).CompareTo(VertX(p)));
                a.Sort((p, q) => left ? CentroidX(q).CompareTo(CentroidX(p)) : CentroidX(p).CompareTo(CentroidX(q)));
                int n = Math.Min(s.Count, a.Count);
                double runSide = SplitHeight ? (left ? DRunL : DRunR) : DRun;
                double cursor = left
                    ? (XOutL - runSide - DSide) - SideLeftGapDu
                    : (XOutR + DW + runSide + DSide) + KotRightGapDu + KotTextClearDu;
                const double between = 80.0;
                for (int i = 0; i < n; i++)
                    PlaceWallBeside(s[i], a[i], left, ref cursor, between);
            }

            /// <summary>Yan açılım kesitle aynı kotta; dış çubuk keside yakın, iç çubuk dışarıda.</summary>
            private static void PlaceWallBeside(Polyline src, Polyline dst, bool left, ref double cursor, double gap)
            {
                int n = src.NumberOfVertices;
                if (dst.NumberOfVertices != n || n < 2) return;
                double minX = double.MaxValue, maxX = double.MinValue;
                var s = new Point2d[n];
                for (int i = 0; i < n; i++)
                {
                    s[i] = src.GetPoint2dAt(i);
                    if (s[i].X < minX) minX = s[i].X;
                    if (s[i].X > maxX) maxX = s[i].X;
                }
                double dx = left ? cursor - maxX : cursor - minX;
                for (int i = 0; i < n; i++)
                {
                    dst.SetPointAt(i, new Point2d(s[i].X + dx, s[i].Y));
                    try { dst.SetBulgeAt(i, src.GetBulgeAt(i)); } catch { }
                }
                cursor = left ? minX + dx - gap : maxX + dx + gap;
            }

            private static void PasteShape(Polyline src, Polyline dst, bool wall, bool left)
            {
                int n = src.NumberOfVertices;
                if (dst.NumberOfVertices != n || n < 2) return;
                var s = new Point2d[n];
                var d = new Point2d[n];
                for (int i = 0; i < n; i++)
                {
                    s[i] = src.GetPoint2dAt(i);
                    d[i] = dst.GetPoint2dAt(i);
                }
                double dx, dy;
                if (!wall)
                {
                    int h = LongHoriz(s);
                    Point2d sm = Mid(s[h], s[h + 1]);
                    Point2d dm = Mid(d[h], d[h + 1]);
                    dx = dm.X - sm.X;
                    dy = dm.Y - sm.Y;
                }
                else
                {
                    int a = 0;
                    for (int i = 1; i < n; i++)
                    {
                        if (left ? d[i].X > d[a].X : d[i].X < d[a].X)
                            a = i;
                    }
                    dx = d[a].X - s[a].X;
                    dy = d[a].Y - s[a].Y;
                }
                for (int i = 0; i < n; i++)
                {
                    dst.SetPointAt(i, new Point2d(s[i].X + dx, s[i].Y + dy));
                    try { dst.SetBulgeAt(i, src.GetBulgeAt(i)); } catch { }
                }
            }

            private static bool BarIsWall(Polyline pl)
            {
                double bestV = 0, bestH = 0;
                int n = pl.NumberOfVertices;
                for (int i = 0; i < n - 1; i++)
                {
                    Point2d a = pl.GetPoint2dAt(i);
                    Point2d b = pl.GetPoint2dAt(i + 1);
                    double dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
                    double L = Math.Sqrt(dx * dx + dy * dy);
                    if (dy > dx) { if (L > bestV) bestV = L; }
                    else if (L > bestH) bestH = L;
                }
                return bestV > bestH && bestV > 80.0;
            }

            private static bool HooksUp(Polyline pl)
            {
                int n = pl.NumberOfVertices;
                var pts = new Point2d[n];
                for (int i = 0; i < n; i++) pts[i] = pl.GetPoint2dAt(i);
                int h = LongHoriz(pts);
                double midY = (pts[h].Y + pts[h + 1].Y) * 0.5;
                double endY = (pts[0].Y + pts[n - 1].Y) * 0.5;
                return endY > midY;
            }

            private static int LongHoriz(Point2d[] pts)
            {
                int best = 0;
                double bestL = -1;
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    double dx = Math.Abs(pts[i + 1].X - pts[i].X);
                    double dy = Math.Abs(pts[i + 1].Y - pts[i].Y);
                    if (dx <= dy) continue;
                    double L = Math.Sqrt(dx * dx + dy * dy);
                    if (L > bestL) { bestL = L; best = i; }
                }
                return best;
            }

            private static double VertX(Polyline pl)
            {
                int n = pl.NumberOfVertices;
                double best = 0, x = 0;
                for (int i = 0; i < n - 1; i++)
                {
                    Point2d a = pl.GetPoint2dAt(i);
                    Point2d b = pl.GetPoint2dAt(i + 1);
                    double dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
                    if (dy <= dx) continue;
                    double L = Math.Sqrt(dx * dx + dy * dy);
                    if (L > best) { best = L; x = (a.X + b.X) * 0.5; }
                }
                return x;
            }

            private static double CentroidX(Polyline pl)
            {
                double x = 0;
                int n = pl.NumberOfVertices;
                for (int i = 0; i < n; i++) x += pl.GetPoint2dAt(i).X;
                return n > 0 ? x / n : 0;
            }

            private static Point2d Mid(Point2d a, Point2d b)
            {
                return new Point2d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
            }

            /// <summary>
            /// Uçtaki gönye düz parçasını alt temel − 2×paspayı boyuna çeker.
            /// 50ø kancaya (daha uzun uç) dokunmaz. Köşe yayı yerinde kalır.
            /// </summary>
            private void FitGonyeHooks(Transaction tr, BlockTableRecord ms)
            {
                double gonye = GonyeCm() * DuPerCm - 3.0;
                if (gonye < 20.0) gonye = 20.0;
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Polyline pl) || pl.IsErased) continue;
                    if (!IsRebarGeom(pl) || pl.NumberOfVertices < 6) continue;
                    if (BarIsWall(pl)) continue;
                    int n = pl.NumberOfVertices;
                    int[] ends = { 0, n - 2 };
                    for (int e = 0; e < ends.Length; e++)
                    {
                        int seg = ends[e];
                        if (seg < 0 || seg >= n - 1) continue;
                        double bulge = 0;
                        try { bulge = pl.GetBulgeAt(seg); } catch { }
                        if (Math.Abs(bulge) > 0.05) continue;
                        Point2d a = pl.GetPoint2dAt(seg);
                        Point2d b = pl.GetPoint2dAt(seg + 1);
                        double L = Dist2d(a, b);
                        if (L < 40.0) continue;
                        if (L >= 110.0 && Math.Abs(L - gonye) > 6.0) continue;
                        int tip = seg == 0 ? 0 : n - 1;
                        int neck = seg == 0 ? 1 : n - 2;
                        pl.SetPointAt(tip, StepAlong(pl.GetPoint2dAt(neck), pl.GetPoint2dAt(tip), gonye));
                    }
                }
            }

            /// <summary>
            /// Kesitteki yan çubuğun 50ø kancası açıklıkla gerilmesin.
            /// Uzunluk, açılımdaki kanca ile aynı (etiket − köşe kıvrımı).
            /// </summary>
            private void FitSectionHooks(Transaction tr, BlockTableRecord ms)
            {
                double d = (TemelCekuruSettings.PaspayiCm - TemelCekuruSettings.TemplatePaspayiCm) * DuPerCm;
                const double iL0 = 2079.647170209089;
                const double iR0 = 2497.647170209089;
                double oL = RebarOutL - DSide + d;
                double iL = iL0 - d;
                double oR = RebarOutR + DW + DSide - d;
                double iR = iR0 + DW + d;
                double xLo = XOutL - DSide - 30.0;
                double xHi = XOutR + DW + DSide + 30.0;
                double yCut = Y4 + Dy4 - 20.0;
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Polyline pl) || pl.IsErased) continue;
                    if (!IsRebarGeom(pl) || pl.NumberOfVertices < 6) continue;
                    int n = pl.NumberOfVertices;
                    bool outside = false;
                    double minY = double.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        Point2d p = pl.GetPoint2dAt(i);
                        if (p.X < xLo || p.X > xHi) outside = true;
                        if (p.Y < minY) minY = p.Y;
                    }
                    if (outside || minY < yCut) continue;
                    int iVert = -1;
                    double bestV = 0, bestH = 0;
                    double vertX = 0;
                    for (int i = 0; i < n - 1; i++)
                    {
                        Point2d a = pl.GetPoint2dAt(i);
                        Point2d b = pl.GetPoint2dAt(i + 1);
                        double dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
                        double L = Math.Sqrt(dx * dx + dy * dy);
                        if (dy > dx)
                        {
                            if (L > bestV) { bestV = L; iVert = i; vertX = (a.X + b.X) * 0.5; }
                        }
                        else if (L > bestH)
                            bestH = L;
                    }
                    // Çukur alt donatısının yatay gövdesi 50ø kanca değildir.
                    if (iVert < 0 || bestV < 80.0 || bestV <= bestH) continue;
                    bool ic = Math.Min(Math.Abs(vertX - iL), Math.Abs(vertX - iR))
                        <= Math.Min(Math.Abs(vertX - oL), Math.Abs(vertX - oR));
                    // İç çubuk: üst uç 50ø, alt uç gönye. Dış çubuk: alt uç 50ø, üst uç gönye.
                    int topSeg = -1, botSeg = -1;
                    double topY = double.MinValue, botY = double.MaxValue;
                    int[] ends = { 0, n - 2 };
                    for (int e = 0; e < ends.Length; e++)
                    {
                        int seg = ends[e];
                        if (seg < 0 || seg >= n - 1) continue;
                        Point2d a = pl.GetPoint2dAt(seg);
                        Point2d b = pl.GetPoint2dAt(seg + 1);
                        if (Math.Abs(b.X - a.X) <= Math.Abs(b.Y - a.Y)) continue;
                        double my = (a.Y + b.Y) * 0.5;
                        if (my > topY) { topY = my; topSeg = seg; }
                        if (my < botY) { botY = my; botSeg = seg; }
                    }
                    if (topSeg < 0 || botSeg < 0 || topSeg == botSeg) continue;
                    double elli = ElliCapCm(ic) * DuPerCm - 3.0;
                    double gonye = GonyeCm() * DuPerCm - 3.0;
                    if (elli < 20.0) elli = 20.0;
                    if (gonye < 20.0) gonye = 20.0;
                    SetEndHook(pl, ic ? topSeg : botSeg, elli);
                    SetEndHook(pl, ic ? botSeg : topSeg, gonye);
                }
            }

            private static void SetEndHook(Polyline pl, int seg, double len)
            {
                int n = pl.NumberOfVertices;
                if (seg < 0 || seg >= n - 1) return;
                int tip = seg == 0 ? 0 : n - 1;
                int neck = seg == 0 ? 1 : n - 2;
                pl.SetPointAt(tip, StepAlong(pl.GetPoint2dAt(neck), pl.GetPoint2dAt(tip), len));
            }

            /// <summary>Köşe yayı kirişi 12 birimi geçerse bulge silinir. Şablon köşesi 4 birimdir.</summary>
            private static void DropStretchedFillets(Transaction tr, BlockTableRecord ms)
            {
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Polyline pl) || pl.IsErased) continue;
                    if (!IsRebarGeom(pl)) continue;
                    int n = pl.NumberOfVertices;
                    int seg = pl.Closed ? n : n - 1;
                    for (int i = 0; i < seg; i++)
                    {
                        double b;
                        try { b = pl.GetBulgeAt(i); }
                        catch { continue; }
                        if (Math.Abs(b) < 0.05 || Math.Abs(b) > 0.95) continue;
                        int j = (i + 1) % n;
                        if (Dist2d(pl.GetPoint2dAt(i), pl.GetPoint2dAt(j)) <= 12.0) continue;
                        try { pl.SetBulgeAt(i, 0); } catch { }
                    }
                }
            }

            /// <summary>Açılımdaki 34 cm kanca (düz parça 32,5 cm). Kesitteki çubuğa dokunmaz.</summary>
            private static void AdjustGonye(Polyline pl)
            {
                int n = pl.NumberOfVertices;
                if (n < 3) return;
                for (int i = 0; i < n; i++)
                {
                    Point2d p = pl.GetPoint2dAt(i);
                    if (!IsSideBar(p.X) && p.Y >= 1120.0) return;
                }
                double delta = (GonyeCm() - 34) * DuPerCm;
                if (Math.Abs(delta) < 0.01) return;
                StretchHook(pl, 0, delta);
                StretchHook(pl, n - 1, delta);
            }

            private static void StretchHook(Polyline pl, int tip, double delta)
            {
                int neck = tip == 0 ? 1 : tip - 1;
                Point2d a = pl.GetPoint2dAt(tip);
                Point2d b = pl.GetPoint2dAt(neck);
                double dx = a.X - b.X;
                double dy = a.Y - b.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 60.0 || len > 70.0) return;
                double neu = len + delta;
                if (neu < DuPerCm) neu = DuPerCm;
                double s = neu / len;
                pl.SetPointAt(tip, new Point2d(b.X + dx * s, b.Y + dy * s));
            }

            /// <summary>Donatı merkezi. Şablonda beton yüzünden 4,5 cm; paspayı kadar içeri alınır.</summary>
            private Point3d CagePt(Point3d p)
            {
                Point3d m = MapPt(p, 36.0);
                if (p.Y < 1120.0) return m;
                return new Point3d(m.X + PasDx(p.X), m.Y + PasDy(p.Y), m.Z);
            }

            private static double PasDx(double x)
            {
                double d = (TemelCekuruSettings.PaspayiCm - TemelCekuruSettings.TemplatePaspayiCm) * DuPerCm;
                if (Math.Abs(d) < 1e-6) return 0;
                const double tol = 4.0;
                const double oL = 2017.647170209089;
                const double iL = 2079.647170209089;
                const double iR = 2497.647170209089;
                const double oR = 2559.647170209089;
                if (Math.Abs(x - oL) <= tol) return d;
                if (Math.Abs(x - iL) <= tol) return -d;
                if (Math.Abs(x - iR) <= tol) return d;
                if (Math.Abs(x - oR) <= tol) return -d;
                return 0;
            }

            private static double PasDy(double y)
            {
                double d = (TemelCekuruSettings.PaspayiCm - TemelCekuruSettings.TemplatePaspayiCm) * DuPerCm;
                if (Math.Abs(d) < 1e-6) return 0;
                const double tol = 4.0;
                const double yBot = 1145.388795085607;
                const double yUp = 1207.388795085607;
                const double yStep = 1345.388795085607;
                const double yTop = 1407.388795085607;
                if (Math.Abs(y - yBot) <= tol) return d;
                if (Math.Abs(y - yUp) <= tol) return -d;
                if (Math.Abs(y - yStep) <= tol) return d;
                if (Math.Abs(y - yTop) <= tol) return -d;
                return 0;
            }

            /// <summary>Kesitteki donatı çizgisi ve noktası. Yazı kaymaz.</summary>
            private static bool IsRebarGeom(Entity ent)
            {
                if (!(ent is Polyline) && !(ent is Line)) return false;
                string layer = ent.Layer ?? "";
                if (layer.IndexOf("DONATI", StringComparison.OrdinalIgnoreCase) < 0) return false;
                return layer.IndexOf("YAZI", StringComparison.OrdinalIgnoreCase) < 0;
            }

            private Point3d MapPt(Point3d p)
            {
                return MapPt(p, 4.0);
            }

            /// <summary>outerRebarTol: dış donatı çizgisinin köşe yayını da aynı DSide ile taşır.</summary>
            private Point3d MapPt(Point3d p, double outerRebarTol)
            {
                if (IsSideBar(p.X))
                {
                    double sx = p.X + SideShift(p.X);
                    return new Point3d(sx, p.Y + (DetayBaslik ? DyDetailSide(p.Y, p.X > XKotCenter) : DySide(p.Y)), p.Z);
                }

                double dy = DyOf(p.Y, p.X);
                double groLift = (TemplateGrobetonCm - GrobetonKalinlikCm) * DuPerCm;
                if (Math.Abs(p.Y - YGro) <= 2.0)
                    dy += groLift;
                bool outside = p.X < XToeL - 1.0 || p.X > XToeR + 1.0;
                if (outside && Math.Abs(p.Y - YBench) < 8.0)
                {
                    double dyBench = SplitHeight ? (p.X < XKotCenter ? Dy2L : Dy2R) : Dy2;
                    dy = dyBench + groLift;
                }

                double x = p.X;
                if (x >= XInnerR - 0.5)
                    x += DW;
                else if (x > XInnerL + 0.5)
                {
                    double span = XInnerR - XInnerL;
                    double t = (x - XInnerL) / span;
                    x = XInnerL + t * (span + DW);
                }
                double runL = SplitHeight ? DRunL : DRun;
                double runR = SplitHeight ? DRunR : DRun;
                if (Math.Abs(p.X - XOutL) < 2.0 || Math.Abs(p.X - XSlopeL) < 2.0)
                    x -= runL;
                else if (Math.Abs(p.X - XOutR) < 2.0 || Math.Abs(p.X - XSlopeR) < 2.0)
                    x += runR;
                if (NearX(p.X, XStepL) || NearX(p.X, XToeL) || NearX(p.X, XOutL) || NearX(p.X, XSlopeL)
                    || Math.Abs(p.X - RebarOutL) <= outerRebarTol)
                    x -= DSide;
                else if (NearX(p.X, XStepR) || NearX(p.X, XToeR) || NearX(p.X, XOutR) || NearX(p.X, XSlopeR)
                    || Math.Abs(p.X - RebarOutR) <= outerRebarTol)
                    x += DSide;
                return new Point3d(x, p.Y + dy, p.Z);
            }

            /// <summary>
            /// Fiziksel şablon kimlikleri (görünen numaralar değil): alt 7–8, 8–15, 15–16, 16–14, 14–12, 7–5, 5–3, 3–4, 11–12.
            /// Üst: 2–4, 4–6, 6–13, 13–11, 11–9, 1–2, 9–10.
            /// Uçlar sabit. Ara donatı kendi aralığını aşmayacak eşit bölünür.
            /// </summary>
            private void RespaceNamedSpans(Transaction tr, BlockTableRecord ms)
            {
                double altDu = TemelCekuruSettings.AltAralikCm * DuPerCm;
                double ustDu = TemelCekuruSettings.UstAralikCm * DuPerCm;
                if (altDu < DuPerCm) altDu = DuPerCm;
                if (ustDu < DuPerCm) ustDu = DuPerCm;

                const double yStep = 1345.388795085607;
                const double yTop = 1407.388795085607;
                const string layerIzi = "DONATI IZI (BEYKENT)";
                // Düşey aralıklar görünen numaralara, yatay sıralar fiziksel konuma bağlıdır.
                var fixedSpans = new[]
                {
                    new Span(PhysicalDot(7), PhysicalDot(8), altDu, null),
                    new Span(PhysicalDot(8), PhysicalDot(15), altDu, null),
                    new Span(PhysicalDot(15), PhysicalDot(16), altDu, null),
                    new Span(NamedDot(16), NamedDot(14), altDu, null),
                    new Span(NamedDot(14), NamedDot(12), altDu, null),
                    new Span(NamedDot(7), NamedDot(5), altDu, null),
                    new Span(NamedDot(5), NamedDot(3), altDu, null),
                    new Span(PhysicalDot(1), PhysicalDot(2), ustDu, layerIzi),
                    new Span(PhysicalDot(9), PhysicalDot(10), ustDu, layerIzi),
                    new Span(PhysicalDot(3), PhysicalDot(4), altDu, layerIzi),
                    new Span(PhysicalDot(11), PhysicalDot(12), altDu, layerIzi),
                    new Span(NamedDot(2), NamedDot(4), ustDu, null),
                    new Span(NamedDot(4), NamedDot(6), ustDu, null),
                    new Span(PhysicalDot(6), PhysicalDot(13), ustDu, null),
                    new Span(NamedDot(13), NamedDot(11), ustDu, null),
                    new Span(NamedDot(11), NamedDot(9), ustDu, null)
                };
                Point3d b1 = PhysicalDot(1);
                Point3d b3 = PhysicalDot(3);
                Point3d b10 = PhysicalDot(10);
                Point3d b12 = PhysicalDot(12);
                var spans = new List<Span>(fixedSpans);
                if (DetayBaslik)
                {
                    if (!DetailLeft) spans.RemoveAll(span => span.A.DistanceTo(PhysicalDot(3)) < NamedDotTolerance
                        && span.B.DistanceTo(PhysicalDot(4)) < NamedDotTolerance);
                    if (!DetailRight) spans.RemoveAll(span => span.A.DistanceTo(PhysicalDot(11)) < NamedDotTolerance
                        && span.B.DistanceTo(PhysicalDot(12)) < NamedDotTolerance);
                }

                double bulge = 1.0;
                double rad = 1.5;
                double width = 0;
                string layer = "DONATI (BEYKENT)";
                bool have = false;
                var kill = new List<ObjectId>();
                var dots = new List<KeyValuePair<ObjectId, Point3d>>();
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead, false) is Entity ent) || ent.IsErased) continue;
                    if (!TryDotCenter(ent, out Point3d c)) continue;
                    string ly = ent.Layer ?? "";
                    if (ly.IndexOf("DONATI", StringComparison.OrdinalIgnoreCase) < 0
                        && ly.IndexOf("CIROZ", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    dots.Add(new KeyValuePair<ObjectId, Point3d>(id, c));
                    if (!have && ent is Polyline pl && pl.NumberOfVertices == 2
                        && ly.Equals("DONATI (BEYKENT)", StringComparison.OrdinalIgnoreCase))
                    {
                        bulge = pl.GetBulgeAt(0);
                        rad = Math.Abs(pl.GetPoint2dAt(1).X - pl.GetPoint2dAt(0).X) * 0.5;
                        if (rad < 0.2 || rad > 8) rad = 1.5;
                        try { width = pl.ConstantWidth; } catch { }
                        layer = ly;
                        have = true;
                    }
                    bool onVertical = false;
                    foreach (int topNumber in new[] { 1, 2, 9, 10 })
                    {
                        Point3d top = NamedDot(topNumber);
                        Point3d bottom = NamedDot(topNumber + 6);
                        if (Math.Abs(c.X - top.X) <= 1.0
                            && c.Y <= Math.Max(top.Y, bottom.Y) + 1.0
                            && c.Y >= Math.Min(top.Y, bottom.Y) - 1.0)
                        {
                            onVertical = true;
                            break;
                        }
                    }
                    if (onVertical)
                    {
                        kill.Add(id);
                        continue;
                    }
                    for (int s = 0; s < spans.Count; s++)
                    {
                        if (!spans[s].HitsInterior(c)) continue;
                        kill.Add(id);
                        break;
                    }
                }
                AddOutsideRow(tr, ms, spans, b1, yTop, true, ustDu, layerIzi);
                if (!DetayBaslik || DetailLeft) AddOutsideRow(tr, ms, spans, b3, yStep, true, altDu, layerIzi);
                AddOutsideRow(tr, ms, spans, b10, yTop, false, ustDu, layerIzi);
                if (!DetayBaslik || DetailRight) AddOutsideRow(tr, ms, spans, b12, yStep, false, altDu, layerIzi);
                foreach (ObjectId id in kill)
                {
                    if (tr.GetObject(id, OpenMode.ForWrite, false) is Entity ent && !ent.IsErased)
                        ent.Erase();
                }
                foreach (Span s in spans)
                {
                    if (DetayBaslik && !DetailEnabled(s.A.X > XKotCenter + DW / 2)
                        && Math.Abs(s.A.X - s.B.X) < 1.0) continue;
                    s.DrawDots(tr, ms, string.IsNullOrEmpty(s.Layer) ? layer : s.Layer, rad, bulge, width);
                }
            }

            /// <summary>
            /// 1 ve 3'ün solu, 10 ve 12'nin sağı. Kesit dış yüzüne paspayı kadar yaklaşana dek kopyalanır.
            /// Eski noktalar katman aranmadan silinir.
            /// </summary>
            private void AddOutsideRow(Transaction tr, BlockTableRecord ms, List<Span> spans, Point3d bar, double templateY, bool toLeft, double maxDu, string layerIzi)
            {
                double faceX = MapPt(new Point3d(toLeft ? XOutL : XOutR, templateY, 0)).X;
                double cover = TemelCekuruSettings.PaspayiCm * DuPerCm;
                if (cover < DuPerCm) cover = DuPerCm;
                double limitX = toLeft ? faceX + cover : faceX - cover;
                const double keep = 2.5;
                double pocket = maxDu * 0.75;
                if (pocket < 16.0) pocket = 16.0;
                double x0 = toLeft ? faceX - 80.0 : bar.X + keep;
                double x1 = toLeft ? bar.X - keep : faceX + 80.0;
                EraseMarksOnRow(tr, ms, bar.Y, x0, x1);
                double ix0 = toLeft ? bar.X + keep : bar.X - pocket;
                double ix1 = toLeft ? bar.X + pocket : bar.X - keep;
                EraseMarksOnRow(tr, ms, bar.Y, ix0, ix1);
                double len = toLeft ? bar.X - limitX : limitX - bar.X;
                if (len < maxDu * 0.25) return;
                var span = new Span(
                    new Point3d(toLeft ? limitX : bar.X, bar.Y, 0),
                    new Point3d(toLeft ? bar.X : limitX, bar.Y, 0),
                    maxDu,
                    layerIzi);
                if (toLeft) span.DrawA = true;
                else span.DrawB = true;
                spans.Add(span);
            }

            /// <summary>Satırdaki küçük donatı noktasını siler. Çizgi, yazı ve ölçü kalır.</summary>
            private static void EraseMarksOnRow(Transaction tr, BlockTableRecord ms, double rowY, double x0, double x1)
            {
                if (x1 < x0)
                {
                    double swap = x0;
                    x0 = x1;
                    x1 = swap;
                }
                var ids = new List<ObjectId>();
                foreach (ObjectId id in ms)
                    ids.Add(id);
                foreach (ObjectId id in ids)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead, false) is Entity ent) || ent.IsErased) continue;
                    if (ent is DBText || ent is MText || ent is Dimension || ent is Leader || ent is MLeader)
                        continue;
                    string ly = ent.Layer ?? "";
                    if (ly.IndexOf("YAZI", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (ly.IndexOf("OLCU", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (ly.IndexOf("KOT", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    Extents3d e;
                    try { e = ent.GeometricExtents; }
                    catch { continue; }
                    double w = e.MaxPoint.X - e.MinPoint.X;
                    double h = e.MaxPoint.Y - e.MinPoint.Y;
                    if (w > 30.0 || h > 30.0 || w < 0.3 || h < 0.3) continue;
                    if (w > h * 4.0 || h > w * 4.0) continue;
                    double cx = (e.MinPoint.X + e.MaxPoint.X) * 0.5;
                    double cy = (e.MinPoint.Y + e.MaxPoint.Y) * 0.5;
                    if (cx < x0 || cx > x1) continue;
                    if (Math.Abs(cy - rowY) > 18.0) continue;
                    if (!ent.IsWriteEnabled) ent.UpgradeOpen();
                    ent.Erase();
                }
            }

            private static bool TryDotCenter(Entity ent, out Point3d c)
            {
                c = Point3d.Origin;
                if (ent is Circle cir)
                {
                    c = cir.Center;
                    return cir.Radius > 0.2 && cir.Radius < 12.0;
                }
                if (ent is Polyline pl) return TryDotCenter(pl, out c);
                return false;
            }

            private static bool TryDotCenter(Polyline pl, out Point3d c)
            {
                c = Point3d.Origin;
                int n = pl.NumberOfVertices;
                if (n < 2) return false;
                if (!pl.Closed && n > 2) return false;
                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;
                for (int i = 0; i < n; i++)
                {
                    Point2d p = pl.GetPoint2dAt(i);
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }
                if (maxX - minX > 20 || maxY - minY > 20) return false;
                if (maxX - minX < 0.4 && maxY - minY < 0.4) return false;
                c = new Point3d((minX + maxX) * 0.5, (minY + maxY) * 0.5, 0);
                return true;
            }

            /// <summary>Dikey ölçüler çukur içinde, soldan açıklığın 1/5'i. Aralık 10 cm.</summary>
            private void DrawOlculer(Transaction tr, BlockTableRecord ms)
            {
                DrawDetailDimensions(tr, ms); // Temel çukuru ve detayı için ortak ölçü düzeni.
            }

            private static void AddVDim(Transaction tr, BlockTableRecord ms, double xMeas, double yA, double yB, double xDim, string layer)
            {
                if (Math.Abs(yB - yA) < 1.0) return;
                double lo = Math.Min(yA, yB);
                double hi = Math.Max(yA, yB);
                var dim = new RotatedDimension(
                    Math.PI / 2.0,
                    new Point3d(xMeas, lo, 0),
                    new Point3d(xMeas, hi, 0),
                    new Point3d(xDim, (lo + hi) * 0.5, 0),
                    "",
                    BaDimensionStyles.Ensure(tr, ms.Database, DimStylePlanOlcu25));
                dim.Layer = layer;
                BaDimensionStyles.Apply(tr, ms.Database, dim, dim.DimensionStyle, DimStylePlanOlcu25);
                ms.AppendEntity(dim);
                tr.AddNewlyCreatedDBObject(dim, true);
            }

            private static void AddHDim(Transaction tr, BlockTableRecord ms, double xA, double xB, double yMeas, double yDim, string layer)
            {
                if (Math.Abs(xB - xA) < 1.0) return;
                double xLo = Math.Min(xA, xB);
                double xHi = Math.Max(xA, xB);
                var dim = new RotatedDimension(
                    0.0,
                    new Point3d(xLo, yMeas, 0),
                    new Point3d(xHi, yMeas, 0),
                    new Point3d((xLo + xHi) * 0.5, yDim, 0),
                    "",
                    BaDimensionStyles.Ensure(tr, ms.Database, DimStylePlanOlcu25));
                dim.Layer = layer;
                BaDimensionStyles.Apply(tr, ms.Database, dim, dim.DimensionStyle, DimStylePlanOlcu25);
                ms.AppendEntity(dim);
                tr.AddNewlyCreatedDBObject(dim, true);
            }

            private struct Span
            {
                public Point3d A, B;
                public double MaxDu;
                public string Layer;
                public bool DrawA;
                public bool DrawB;
                public Span(Point3d a, Point3d b, double maxDu, string layer)
                {
                    A = a;
                    B = b;
                    MaxDu = maxDu;
                    Layer = layer;
                    DrawA = false;
                    DrawB = false;
                }

                public bool HitsInterior(Point3d p)
                {
                    double vx = B.X - A.X, vy = B.Y - A.Y;
                    double len2 = vx * vx + vy * vy;
                    if (len2 < 1) return false;
                    double t = ((p.X - A.X) * vx + (p.Y - A.Y) * vy) / len2;
                    if (t <= 0.04 || t >= 0.96) return false;
                    double dx = p.X - (A.X + t * vx);
                    double dy = p.Y - (A.Y + t * vy);
                    return dx * dx + dy * dy <= 64.0;
                }

                public void DrawDots(Transaction tr, BlockTableRecord ms, string layer, double rad, double bulge, double width)
                {
                    double len = Math.Sqrt((B.X - A.X) * (B.X - A.X) + (B.Y - A.Y) * (B.Y - A.Y));
                    if (len < 1) return;
                    double maxDu = MaxDu < 1 ? 1 : MaxDu;
                    int n = (int)Math.Ceiling(len / maxDu - 1e-6);
                    if (n < 1) n = 1;
                    int first = DrawA ? 0 : 1;
                    int last = DrawB ? n : n - 1;
                    for (int i = first; i <= last; i++)
                    {
                        double t = i / (double)n;
                        double x = A.X + t * (B.X - A.X);
                        double y = A.Y + t * (B.Y - A.Y);
                        var pl = new Polyline(2);
                        pl.AddVertexAt(0, new Point2d(x - rad, y), bulge, width, width);
                        pl.AddVertexAt(1, new Point2d(x + rad, y), bulge, width, width);
                        pl.Closed = true;
                        pl.Layer = layer;
                        pl.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                        pl.LineWeight = LineWeight.ByLayer;
                        ms.AppendEntity(pl);
                        tr.AddNewlyCreatedDBObject(pl, true);
                    }
                }
            }

            /// <summary>Sol açılım keside yaklaşır. Sağ açılım kot yazısının sağına gider.</summary>
            private double SideShift(double x)
            {
                double runR = SplitHeight ? DRunR : DRun;
                double runL = SplitHeight ? DRunL : DRun;
                if (x > XOutR)
                {
                    double kotApex = (XOutR + DW + runR + DSide) + KotRightGapDu;
                    double targetMin = kotApex + KotTextClearDu;
                    return targetMin - SideRightMinX;
                }
                double targetMax = (XOutL - runL - DSide) - SideLeftGapDu;
                return targetMax - SideLeftMaxX;
            }

            private static bool IsSideBar(double x)
            {
                return x < XOutL - 40.0 || x > XOutR + 40.0;
            }

            /// <summary>Yandaki çubuk açılımı: kanca boyu sabit, düşey gövde temel+alt ile uzar.</summary>
            private double DySide(double y)
            {
                const double yTop = 1330.0;
                const double yBot = 1160.0;
                if (y >= yTop) return Dy1;
                if (y <= yBot) return Dy4;
                double t = (y - yBot) / (yTop - yBot);
                return Dy4 + t * (Dy1 - Dy4);
            }

            private double DyOf(double y)
            {
                return DyOf(y, double.NaN);
            }

            private double DyOf(double y, double x)
            {
                if (DetayBaslik && !double.IsNaN(x)) return DyDetail(y, x > XKotCenter);
                if (double.IsNaN(x))
                    return DyUniform(y);
                return DyUniform(y);
            }

            /// <summary>
            /// Sığ tarafın perde dibini yükseltir. Üst temel sehpasına dokunmaz.
            /// Çukur tabanı derin tarafta kalır; sığ perdede düşey basamak oluşur.
            /// </summary>
            private void ShortenShallowWalls(Transaction tr, BlockTableRecord ms)
            {
                if (!SplitHeight) return;
                if (Dy3L > Dy3 + 1.0)
                    ShortenOneWall(tr, ms, XInnerL, Dy3L - Dy3, false);
                if (Dy3R > Dy3 + 1.0)
                    ShortenOneWall(tr, ms, XInnerR + DW, Dy3R - Dy3, true);
            }

            private void ShortenOneWall(Transaction tr, BlockTableRecord ms, double xWall, double raise, bool right)
            {
                double yFloor = Y3 + Dy3;
                double yLip = Y2 + Dy2;
                double yNew = yFloor + raise;
                if (yNew > yLip - DuPerCm) yNew = yLip - DuPerCm;
                if (yNew - yFloor < 1.0) return;

                var ids = new List<ObjectId>();
                foreach (ObjectId id in ms)
                    ids.Add(id);
                foreach (ObjectId id in ids)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Entity ent) || ent.IsErased)
                        continue;
                    if (ent is Polyline pl)
                    {
                        if (LayerIs(pl.Layer, LayerTemel))
                        {
                            LiftWallBottom(pl, xWall, yFloor, yNew, yLip);
                            InsertFloorSteps(pl);
                        }
                        else if (IsRebarGeom(pl))
                            LiftVerticalBar(pl, xWall, yFloor, yNew, yLip, right);
                    }
                    else if (ent is Hatch hatch && LayerIs(hatch.Layer, LayerBlokaj))
                        LiftHatchBottom(hatch, xWall, yFloor, yNew, yLip);
                    else
                        EraseDotBelow(ent, xWall, yFloor, yNew, right);
                }
            }

            private static void LiftWallBottom(Polyline pl, double xWall, double yFloor, double yNew, double yLip)
            {
                int n = pl.NumberOfVertices;
                for (int i = 0; i < n; i++)
                {
                    Point2d p = pl.GetPoint2dAt(i);
                    if (Math.Abs(p.X - xWall) > 35.0) continue;
                    if (p.Y > yLip - 4.0) continue;
                    if (p.Y < yFloor - 12.0) continue;
                    if (p.Y > yFloor + 30.0) continue;
                    pl.SetPointAt(i, new Point2d(p.X, yNew));
                    try { pl.SetBulgeAt(i, 0); } catch { }
                }
            }

            private static void LiftVerticalBar(Polyline pl, double xWall, double yFloor, double yNew, double yLip, bool right)
            {
                int n = pl.NumberOfVertices;
                if (n < 2) return;
                int on = 0;
                double minY = double.MaxValue, maxY = double.MinValue;
                for (int i = 0; i < n; i++)
                {
                    Point2d p = pl.GetPoint2dAt(i);
                    if (OnWallBand(p.X, xWall, right)) on++;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }
                if (on < n - 1) return;
                if (maxY - minY < 40.0) return;
                double delta = yNew - yFloor;
                for (int i = 0; i < n; i++)
                {
                    Point2d p = pl.GetPoint2dAt(i);
                    if (p.Y > yLip - 8.0) continue;
                    if (p.Y > yFloor + 22.0) continue;
                    pl.SetPointAt(i, new Point2d(p.X, p.Y + delta));
                }
            }

            private static bool OnWallBand(double x, double xWall, bool right)
            {
                if (right) return x >= xWall - 12.0 && x <= xWall + 100.0;
                return x <= xWall + 12.0 && x >= xWall - 100.0;
            }

            private void LiftHatchBottom(Hatch hatch, double xWall, double yFloor, double yNew, double yLip)
            {
                try
                {
                    int n = hatch.NumberOfLoops;
                    var loops = new List<LoopCopy>();
                    bool changed = false;
                    for (int i = 0; i < n; i++)
                    {
                        HatchLoop loop = hatch.GetLoopAt(i);
                        var pts = new Point2dCollection();
                        var bulges = new DoubleCollection();
                        if (loop.IsPolyline)
                        {
                            foreach (BulgeVertex v in loop.Polyline)
                            {
                                double x = v.Vertex.X;
                                double y = v.Vertex.Y;
                                if (Math.Abs(x - xWall) <= 35.0 && y <= yLip - 4.0 && y >= yFloor - 12.0 && y <= yFloor + 30.0)
                                {
                                    y = yNew;
                                    changed = true;
                                }
                                if (pts.Count > 0 && TryFloorStep(pts[pts.Count - 1], new Point2d(x, y), out Point2d mid))
                                {
                                    pts.Add(mid);
                                    bulges.Add(0);
                                }
                                pts.Add(new Point2d(x, y));
                                bulges.Add(v.Bulge);
                            }
                        }
                        else if (loop.Curves != null)
                        {
                            foreach (Curve2d c in loop.Curves)
                            {
                                if (!(c is LineSegment2d ln)) continue;
                                double x = ln.StartPoint.X;
                                double y = ln.StartPoint.Y;
                                if (Math.Abs(x - xWall) <= 35.0 && y <= yLip - 4.0 && y >= yFloor - 12.0 && y <= yFloor + 30.0)
                                {
                                    y = yNew;
                                    changed = true;
                                }
                                pts.Add(new Point2d(x, y));
                                bulges.Add(0);
                            }
                        }
                        if (pts.Count >= 3)
                            loops.Add(new LoopCopy { Type = loop.LoopType, Pts = pts, Bulges = bulges });
                    }
                    if (!changed || loops.Count == 0) return;
                    try { hatch.Associative = false; } catch { }
                    for (int i = n - 1; i >= 0; i--)
                        hatch.RemoveLoopAt(i);
                    foreach (LoopCopy lp in loops)
                        hatch.AppendLoop(lp.Type, lp.Pts, lp.Bulges);
                    try { hatch.EvaluateHatch(true); } catch { }
                }
                catch { }
            }

            private static void EraseDotBelow(Entity ent, double xWall, double yFloor, double yNew, bool right)
            {
                if (ent is DBText || ent is Dimension || ent is Hatch) return;
                Extents3d e;
                try { e = ent.GeometricExtents; }
                catch { return; }
                double w = e.MaxPoint.X - e.MinPoint.X;
                double h = e.MaxPoint.Y - e.MinPoint.Y;
                if (w > 25.0 || h > 25.0) return;
                double cx = (e.MinPoint.X + e.MaxPoint.X) * 0.5;
                double cy = (e.MinPoint.Y + e.MaxPoint.Y) * 0.5;
                if (!OnWallBand(cx, xWall, right)) return;
                if (cy <= yFloor + 8.0 || cy >= yNew - 6.0) return;
                try { ent.Erase(); } catch { }
            }

            private double DyUniform(double y)
            {
                if (y >= Y0 - 0.8) return 0;
                if (Math.Abs(y - YLip) <= 3.0) return 0;
                if (Math.Abs(y - Y1) <= 12.0) return Dy1;
                if (Math.Abs(y - Y2) <= 12.0) return Dy2;
                if (Math.Abs(y - Y3) <= 12.0) return Dy3;
                if (Math.Abs(y - Y4) <= 12.0) return Dy4;
                if (y > Y1) return Lerp(y, Y1, Y0, Dy1, 0);
                if (y > Y2) return Lerp(y, Y2, Y1, Dy2, Dy1);
                if (y > Y3) return Lerp(y, Y3, Y2, Dy3, Dy2);
                if (y > Y4) return Lerp(y, Y4, Y3, Dy4, Dy3);
                return Dy4;
            }

            private bool TryFloorStep(Point2d a, Point2d b, out Point2d mid)
            {
                mid = default;
                if (!SplitHeight || DetayBaslik) return false;
                double yFloor = Y3 + Dy3;
                double hiY = a.Y > b.Y ? a.Y : b.Y;
                double loY = a.Y < b.Y ? a.Y : b.Y;
                if (hiY - loY < 8.0) return false;
                if (Math.Abs(loY - yFloor) > 20.0) return false;
                if (hiY - yFloor < 8.0) return false;
                if (Math.Abs(a.X - b.X) < 2.0) return false;
                Point2d hi = a.Y >= b.Y ? a : b;
                double xL = XInnerL;
                double xR = XInnerR + DW;
                if (Math.Abs(hi.X - xL) > 8.0 && Math.Abs(hi.X - xR) > 8.0)
                    return false;
                mid = new Point2d(hi.X, yFloor);
                return true;
            }

            private void InsertFloorSteps(Polyline pl)
            {
                if (!SplitHeight || pl == null) return;
                int guard = 0;
                int i = 0;
                while (guard++ < 40)
                {
                    int n = pl.NumberOfVertices;
                    if (n < 2) return;
                    if (!pl.Closed && i >= n - 1) return;
                    if (pl.Closed && i >= n) return;
                    int j = i + 1;
                    bool closing = false;
                    if (j >= n)
                    {
                        if (!pl.Closed) return;
                        j = 0;
                        closing = true;
                    }
                    Point2d a = pl.GetPoint2dAt(i);
                    Point2d b = pl.GetPoint2dAt(j);
                    if (TryFloorStep(a, b, out Point2d mid))
                    {
                        int at = closing ? n : i + 1;
                        pl.AddVertexAt(at, mid, 0, 0, 0);
                        try { pl.SetBulgeAt(i, 0); } catch { }
                        i += 2;
                        continue;
                    }
                    i++;
                }
            }

            private static double Lerp(double y, double y0, double y1, double d0, double d1)
            {
                double span = y1 - y0;
                if (Math.Abs(span) < 1e-6) return d0;
                double t = (y - y0) / span;
                if (t < 0) t = 0;
                if (t > 1) t = 1;
                return d0 + t * (d1 - d0);
            }

            private void MapHatch(Hatch hatch)
            {
                int n = hatch.NumberOfLoops;
                var loops = new List<LoopCopy>();
                for (int i = 0; i < n; i++)
                {
                    HatchLoop loop = hatch.GetLoopAt(i);
                    var pts = new Point2dCollection();
                    var bulges = new DoubleCollection();
                    double yZemin = MapPt(new Point3d(XKotCenter, YGro, 0)).Y;
                    if (loop.IsPolyline)
                    {
                        foreach (BulgeVertex v in loop.Polyline)
                        {
                            Point3d m = MapHatchPt(v.Vertex.X, v.Vertex.Y, yZemin);
                            AddHatchVertex(pts, bulges, m, v.Bulge);
                        }
                    }
                    else if (loop.Curves != null)
                    {
                        foreach (Curve2d c in loop.Curves)
                        {
                            if (!(c is LineSegment2d ln)) continue;
                            Point3d m = MapHatchPt(ln.StartPoint.X, ln.StartPoint.Y, yZemin);
                            AddHatchVertex(pts, bulges, m, 0);
                        }
                    }
                    if (pts.Count < 3) return;
                    loops.Add(new LoopCopy { Type = loop.LoopType, Pts = pts, Bulges = bulges });
                }
                try { hatch.Associative = false; } catch { }
                for (int i = n - 1; i >= 0; i--)
                    hatch.RemoveLoopAt(i);
                foreach (LoopCopy lp in loops)
                    hatch.AppendLoop(lp.Type, lp.Pts, lp.Bulges);
                try { hatch.EvaluateHatch(true); } catch { }
            }

            /// <summary>Grobeton taraması zemin çizgisinin altına inmesin.</summary>
            private Point3d MapHatchPt(double x, double y, double yZemin)
            {
                if (Math.Abs(y - (Y4 - 10.0)) <= 1.5) y = Y4;
                if (Math.Abs(y - (Y2 - 10.0)) <= 1.5) y = Y2;
                Point3d m = MapPt(new Point3d(x, y, 0));
                if (m.Y < yZemin - 0.5)
                    m = new Point3d(m.X, yZemin, m.Z);
                return m;
            }

            private void AddHatchVertex(Point2dCollection pts, DoubleCollection bulges, Point3d m, double bulge)
            {
                if (pts.Count > 0 && TryFloorStep(pts[pts.Count - 1], new Point2d(m.X, m.Y), out Point2d mid))
                {
                    pts.Add(mid);
                    bulges.Add(0);
                }
                pts.Add(new Point2d(m.X, m.Y));
                bulges.Add(bulge);
            }

            private sealed class LoopCopy
            {
                public HatchLoopTypes Type;
                public Point2dCollection Pts;
                public DoubleCollection Bulges;
            }

            private void TryMapTextPos(RotatedDimension rd)
            {
                try { rd.TextPosition = MapPt(rd.TextPosition); } catch { }
            }

            private void TryMapTextPos(AlignedDimension ad)
            {
                try { ad.TextPosition = MapPt(ad.TextPosition); } catch { }
            }

            private void TryMapTextPos(LineAngularDimension2 ang)
            {
                try { ang.TextPosition = MapPt(ang.TextPosition); } catch { }
                try { ang.ArcPoint = MapPt(ang.ArcPoint); } catch { }
            }

            private static void TryRecompute(Dimension dim)
            {
                try { dim.RecomputeDimensionBlock(true); } catch { }
            }

        }

        private static bool LayerIs(string layer, string name)
        {
            return string.Equals((layer ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase);
        }

        private static string EnsureTempDwg()
        {
            lock (TempLock)
            {
                Assembly asm = typeof(TemelCekuruRunner).Assembly;
                using (Stream stream = asm.GetManifestResourceStream(EmbeddedResourceName))
                {
                    if (stream == null) return null;
                    string path = Path.Combine(Path.GetTempPath(),
                        "ST4Yardimci_temelcekuru_" + asm.ManifestModule.ModuleVersionId.ToString("N") + ".dwg");
                    if (!File.Exists(path) || new FileInfo(path).Length != stream.Length)
                    {
                        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.Read))
                            stream.CopyTo(fs);
                        try
                        {
                            if (File.Exists(path)) File.Delete(path);
                            File.Move(tmp, path);
                        }
                        catch
                        {
                            try { File.Delete(tmp); } catch { }
                            if (!File.Exists(path)) return null;
                        }
                    }
                    _tempDwg = path;
                    return path;
                }
            }
        }
    }
}
