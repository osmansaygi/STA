using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    /// Sanal çizgi: KIRIS/KOLON/PERDE çift-çift mesnet (pilye+düz+ilave);
    /// DOSEME SINIRI / KALIP BOSLUK kesişi + döşeme (1.5 m) → yalnız alt düz.
    /// İki SINIRI (mesnet yok) → yalnız alt düz. KALIP BOSLUK/MERDIVEN içi her zaman boş.
    /// </summary>
    internal static partial class DosemeDonatiRunner
    {
        private const double MinSpanCm = 40.0;
        /// <summary>
        /// Kirişten sonra DOSEME SINIRI ≤ 1.5 m → küçük konsol (etiket sığmaz, sınır dışında kalabilir).
        /// 1.5 m'den büyük konsolda etiket içeri sığar; normal taşkın/etiket yeter.
        /// </summary>
        private const double SiniriKonsolMaxCm = 150.0;
        /// <summary>Konsola uzayan pilye: min uzatma (cm). ≤50 konsolda serbest kenara paspayı; ilave firkete.</summary>
        private const double KonsolPilyeMinUzCm = 50.0;
        /// <summary>Konsol üst ilave gönye boyu (cm) — serbest kenar.</summary>
        private const double KonsolIlaveGonyeCm = 10.0;
        /// <summary>Kot farkı / arkası boş: kiriş içindeki üst uç gönye (cm).</summary>
        private const double KonsolKirisGonyeCm = 40.0;
        /// <summary>Bir mesnet çiftinin (1.–2. çizgi) makul genişliği üst sınırı (cm).</summary>
        private const double MaxMesnetWidthCm = 120.0;
        /// <summary>Kolon kutusu gibi gövde içi iki yüz (25/150 vb.) — 1.2 m üstü yalnız gövde içinde eşleşir.</summary>
        private const double MaxMesnetBodyWidthCm = 220.0;
        /// <summary>DOSEME SINIRI kiriş/kolon/perde ile ≤0.2 cm üst üste → tek çizgi (mesnet öncelikli).</summary>
        private const double CoincidentLineTolCm = 0.2;
        private const double PilyeKirimAlongCm = 10.0;
        private const double PilyeKirimRiseCm = 10.0; // 10×10 → tam 45°
        /// <summary>Pilye kırımı mesnet iç yüzünden açıklığın 2/9'unda başlar.</summary>
        private const double PilyeKirimOran = 2.0 / 9.0;
        /// <summary>Bu açıklıktan kısa döşemede pilye yok; iki mesnetin ilavesi birleşir.</summary>
        private const double MinPilyeSpanCm = 200.0;
        /// <summary>Tüm donatı kıvrımlarında büküm yarıçapı.</summary>
        private const double BendRadiusCm = 3.0;
        private const double StaggerCm = 5.0;
        private const double PaspayiCm = 3.0;
        private const double DuzBelowPilyeCm = 20.0;
        private const double GonyeCm = 20.0;
        /// <summary>Ticari çubuk 12 m. Aşan donatı Lb bindirme ile bölünür.</summary>
        private const double StockLenCm = 1200.0;
        /// <summary>Son parça bundan kısaysa ilk parça 600 cm.</summary>
        private const double StockMinRemnantCm = 200.0;
        private const double StockFirstPieceCm = 600.0;
        /// <summary>Bindirme kırımı: eksen boyu (cm).</summary>
        private const double StockCrankAlongCm = 20.0;
        /// <summary>Bindirme kırımı: dik şaşırtma (cm).</summary>
        private const double StockCrankOffsetCm = 5.0;
        /// <summary>Pilye eki: 45° kırım 3×3 cm, pilye 10 cm yüksekliği içinde.</summary>
        private const double PilyeLapCrankAlongCm = 3.0;
        private const double PilyeLapCrankOffsetCm = 3.0;
        private const double StockMinBarCm = 5.0;
        private const double ProbeExtendCm = 5000.0;
        private const double KotMatchTolM = 0.02;
        // Plan_ID AppendSlabLabel (ann=1 / 1:50; 1:100 → ×2):
        // DOSEME ISMI Position=(leftX,mainY); Q: mainY−15; üst kot: (leftX+14.3261, mainY−26); alt: −10.
        private const double PlanLabelToQCm = 15.0;
        private const double PlanQToUstKotCm = 11.0;
        private const double PlanKotPairGapCm = 10.0;
        private const double PlanKotOffsetXCm = 19.5 - 5.1739; // 14.3261
        private const double LabelGapCm = 3.0;
        private const double TextHeightCm = 10.0;
        private const double UstIlaveAboveTopCm = 25.0;

        private const string LayerKiris = "KIRIS (BEYKENT)";
        private const string LayerKolon = "KOLON (BEYKENT)";
        private const string LayerPerde = "PERDE (BEYKENT)";
        private const string LayerKapamaPerdesi = "KAPAMA PERDESI (BEYKENT)";
        private const string LayerKalipBosluk = "KALIP BOSLUK (BEYKENT)";
        private const string LayerMerdiven = "MERDIVEN (BEYKENT)";
        private const string LayerDosemeIsmi = "DOSEME ISMI (BEYKENT)";
        private const string LayerKotYazi = "KOT YAZI (BEYKENT)";
        private const string LayerKirisIsmi = "KIRIS ISMI (BEYKENT)";
        private const string LayerPerdeIsmi = "PERDE ISMI (BEYKENT)";
        private const string LayerKolonIsmi = "KOLON ISMI (BEYKENT)";
        private const string LayerDonati = "DONATI (BEYKENT)";
        private const string LayerDonatiYazi = "DONATI YAZISI (BEYKENT)";
        private const string LayerGprKirmizi = "DONATI YAZISI KALIP GPR KIRMIZI (BEYKENT)";
        private const string LayerGprMavi = "DONATI YAZISI KALIP GPR MAVI (BEYKENT)";
        private const string TextStyleYazi = "YAZI (BEYKENT)";

        private static readonly Regex RxKalınlık = new Regex(
            @"d\s*=\s*(\d+(?:[.,]\d+)?)\s*cm",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxSlabName = new Regex(
            @"^\s*(D[A-Za-z]?-?\d+(?:-\d+)?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxKot = new Regex(
            @"(?<![A-Za-zØøΦφ])([+\-±\u00B1\u2212]?\d+[.,]\d{1,3})(?!\d)",
            RegexOptions.Compiled);
        private static readonly Regex RxAdetCapAralik = new Regex(
            @"(\d+)\s*[ØøΦφ]\s*(\d+)\s*/\s*(\d+)",
            RegexOptions.Compiled);
        private static readonly Regex RxCapAralik = new Regex(
            @"[ØøΦφ]\s*(\d+)\s*/\s*(\d+)",
            RegexOptions.Compiled);

        private enum DividerKind { Mesnet, Siniri, Bosluk }

        private struct SupportCluster
        {
            public double TMin;
            public double TMax;
            public Point3d FaceMin;
            public Point3d FaceMax;
            public string MesnetLabel; // KIRIS/PERDE/KOLON ISMI
        }

        private struct Divider
        {
            public DividerKind Kind;
            public double TMin;
            public double TMax;
            public Point3d FaceMin;
            public Point3d FaceMax;
            public int MesnetIndex;
        }

        private struct HitRec
        {
            public Point3d Point;
            public ObjectId EntId;
            public double T;
            public string Layer;
        }

        private struct SlabInfo
        {
            public bool HasLabel;
            /// <summary>DOSEME ISMI'den: DB-15 / DB15 vb.</summary>
            public string Name;
            public double? ThicknessCm;
            public double? KotM;
            public int? Adet;
            public int CapMm;
            public int AralikCm;
            /// <summary>KALIP50/100 GPR yazıları (X=kırmızı, Y=mavi) bu kapalı alanda.</summary>
            public List<GprSteelRec> GprSteels;
        }

        private enum GprSteelKind
        {
            Unknown = 0,
            Duz = 1,
            Pilye = 2,
            SolIlave = 3,
            SagIlave = 4,
            Montaj = 5
        }

        private struct GprSteelRec
        {
            public Point3d Pos;
            public int Adet;
            public int CapMm;
            public int AralikCm;
            public string Text;
            public GprSteelKind Kind;
            public ObjectId Id;
        }

        private struct KirmiziGprCizgi
        {
            public Point3d A;
            public Point3d B;
            public bool Ust;
            public ObjectId Id;
            /// <summary>Bu komutun yönü (X kırmızı / Y mavi). Diğer renk yalnız taşıma için.</summary>
            public bool Active;
        }

        private static List<KirmiziGprCizgi> _kirmiziGprCizgi;
        private static readonly HashSet<ObjectId> _parkBelow = new HashSet<ObjectId>();
        /// <summary>Bu komutta çizilen donatı bu poligonun dışında kalmaz. Poligon çizimine karışmaz.</summary>
        private static List<ClosedArea> _katSinirClamp;
        /// <summary>Yeni donatı yazısına aktarılan GPR yazıları. Çift çizgi ancak poligondakilerin hepsi buradaysa iner.</summary>
        private static readonly HashSet<ObjectId> _gprTransferred = new HashSet<ObjectId>();
        private const double ParkBelowCm = 10000.0;
        /// <summary>Aktarılan GPR yazısı ve (hepsi aktarıldıysa) çift çizgi 10000 cm aşağı iner.</summary>
        private const bool ParkBelowEnabled = true;

        private struct SpanRec
        {
            public int SupportLeft;
            /// <summary>Sağ mesnet indeksi; yoksa −1 (orta delik sağ parçası için ilave anahtarı).</summary>
            public int SupportRight;
            public double L;
            public Point3d LeftInner, RightInner, LeftOuter, RightOuter, BarL, BarR, Mid;
            public SlabInfo Slab;
            public bool InPick;
            /// <summary>Sanal hat sol/sağ iç yüze ulaştı; değilse o uçta kes (tam açıklık/pilye/ilave yok).</summary>
            public bool ReachLeft, ReachRight;
            /// <summary>allSpans dışı komşu (perde/atlanmış açıklık) aynı üst kot → pilye L/4 veya konsol.</summary>
            public bool ExtPrev, ExtNext;
            /// <summary>Pilye uzatması için (L×1/4); konsolda yarım/min50 formülü.</summary>
            public double LprevExt, LnextExt;
            /// <summary>Öte yan konsol (DOSEME SINIRI); üst ilave ayrı geometri.</summary>
            public bool KonsolPrev, KonsolNext;
            /// <summary>Konsol boyu: mesnet dış yüz → serbest kenar (cm).</summary>
            public double KonsolBoyPrev, KonsolBoyNext;
            /// <summary>Sınır/boşluk kenarı veya yalnız iki DOSEME SINIRI: pilye/ilave yok, yalnız alt düz.</summary>
            public bool DuzOnly;
            /// <summary>DuzOnly: hangi uç mesnet (kiriş).</summary>
            public bool LeftIsMesnet, RightIsMesnet;
            /// <summary>DuzOnly ve serbest uç BOŞLUK kenarı (orta delik) — SINIRI konsol değil.</summary>
            public bool FreeEndBosluk;
        }

        public enum DonatiYonu { Serbest, X, Y }

        /// <summary>Formdan seçilir: X/Y ise sanal çizgi o eksene kilitlenir.</summary>
        public static DonatiYonu Yon = DonatiYonu.Serbest;

        /// <summary>Çizgisel Donatı At: yön kilidi yok, serbest donatı gibi çizilir.</summary>
        public static bool CizgiselMod;

        /// <summary>Serbest: GPR yok. X kırmızı, Y mavi GPR.</summary>
        static DonatiYonu EtkinYon => Yon;

        static bool _yonLoaded;

        public static void EnsureYonLoaded()
        {
            if (_yonLoaded) return;
            _yonLoaded = true;
            string y = BaIni.Get(BaIni.Read("doseme_donati.ini"), "Yon", "Serbest");
            if (string.Equals(y, "X", StringComparison.OrdinalIgnoreCase)) Yon = DonatiYonu.X;
            else if (string.Equals(y, "Y", StringComparison.OrdinalIgnoreCase)) Yon = DonatiYonu.Y;
            else Yon = DonatiYonu.Serbest;
        }

        public static void SaveYon()
        {
            EnsureYonLoaded();
            DosemeDonatiSettings.Save();
        }

        /// <summary>Çizgisel donatı: serbest yön, formdaki sınır/mesnet katman listeleri.</summary>
        public static void RunCizgisel()
        {
            CizgiselMod = true;
            try { RunInteractive(); }
            finally { CizgiselMod = false; }
        }

        public static void RunInteractive()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                DosemeDonatiForm.ShowAfterPick();
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                if (!TryPreloadSlabGeometryAssemblies(ed))
                {
                    DosemeDonatiForm.ShowAfterPick();
                    return;
                }

                string yonMsg = EtkinYon == DonatiYonu.X ? " yön=X (kırmızı GPR)"
                    : EtkinYon == DonatiYonu.Y ? " yön=Y (mavi GPR)"
                    : " çizgisel (serbest)";
                ed.WriteMessage(
                    "\nDOSEMEDONATICIZ: Kiriş/kolon algı + pilye/düz/ilave (pas 3, düz −20).{0} Esc = bitir.",
                    yonMsg);

                while (true)
                {
                    var opt1 = new PromptPointOptions("\nSanal çizgi birinci nokta (bitir: Esc/sağ tık): ")
                    {
                        AllowNone = true
                    };
                    PromptPointResult p1 = ed.GetPoint(opt1);
                    if (p1.Status != PromptStatus.OK)
                        break;

                    var opt2 = new PromptPointOptions("\nSanal çizgi ikinci nokta: ")
                    {
                        BasePoint = p1.Value,
                        UseBasePoint = true,
                        AllowNone = true
                    };
                    PromptPointResult p2 = ed.GetPoint(opt2);
                    if (p2.Status != PromptStatus.OK)
                        break;

                    // GetPoint = WCS; UCS ile tekrar dönüştürme çizimi kaydırır.
                    Point3d a = p1.Value;
                    Point3d b = p2.Value;
                    Vector3d dir = b - a;
                    if (dir.Length < MinSpanCm)
                    {
                        ed.WriteMessage("\nDOSEMEDONATICIZ: Çizgi çok kısa.");
                        continue;
                    }

                    try
                    {
                        int n = PlaceAlongVirtualLine(doc, db, ed, a, b, dir);
                        ed.WriteMessage("\nDOSEMEDONATICIZ: {0} döşeme açıklığı çizildi.", n);
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage("\nDOSEMEDONATICIZ hata: {0}", ex.Message);
                    }
                }
            }
            finally
            {
                DosemeDonatiForm.ShowAfterPick();
            }
        }

        private static int PlaceAlongVirtualLine(
            Document doc,
            Database db,
            Editor ed,
            Point3d a,
            Point3d b,
            Vector3d dir)
        {
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                string donatiLayer = EnsureLayer(tr, db, LayerDonati, 4, LineWeight.LineWeight040);
                string yaziLayer = EnsureLayer(tr, db, LayerDonatiYazi, 3, LineWeight.LineWeight020);
                ObjectId yaziStyleId = GetOrCreateYaziBeykentTextStyle(tr, db);

                var structLayers = BuildMesnetLayerSet();
                Vector3d u = dir.GetNormal();
                Vector3d n = StabilizeUpNormal(u);

                Point3d probeA = a - u * ProbeExtendCm;
                Point3d probeB = b + u * ProbeExtendCm;
                double tPick0 = 0;
                double tPick1 = (b - a).DotProduct(u);
                if (tPick1 < tPick0) { double tmp = tPick0; tPick0 = tPick1; tPick1 = tmp; }

                // KIRIS / KOLON / PERDE (poligon segmentleri = ayrı kenar)
                var rawHits = new List<HitRec>();
                CollectIntersections(tr, btr, probeA, probeB, a, u, structLayers, rawHits);
                rawHits.Sort((p, q) => p.T.CompareTo(q.T));
                DedupHitsNear(rawHits, 0.5);

                var slabTextsEarly = CollectSlabLabelTexts(tr, btr);
                var closedAreas = CollectClosedAreas(tr, btr);
                var katSinirAreas = CollectKatSinirAreas(tr, btr);
                _katSinirClamp = katSinirAreas;
                if (katSinirAreas.Count > 0)
                    ed.WriteMessage("\nDOSEMEDONATICIZ: Kat sınırı poligon {0} — dışarı donatı yok.",
                        katSinirAreas.Count);
                // Aynı poligonun iki segmenti / yakın iki çizgi = bir mesnet (kiriş gibi).
                // Arada DOSEME ISMI varsa küçük konsol — yutma.
                var supports = PairHitsAsMesnets(tr, btr, rawHits, ed, slabTextsEarly, a, u, closedAreas);

                var edgeHits = new List<HitRec>();
                CollectIntersections(tr, btr, probeA, probeB, a, u, IsSiniriOrBoslukLayer, edgeHits);
                edgeHits.Sort((p, q) => p.T.CompareTo(q.T));
                DedupHitsNear(edgeHits, 0.5);
                int nEdgeRaw = edgeHits.Count;
                SuppressSiniriOnMesnet(edgeHits, rawHits, supports);
                if (nEdgeRaw > 0)
                    ed.WriteMessage("\nDOSEMEDONATICIZ: Sınır/boşluk kenar {0} (mesnetle üst üste atılan {1}).",
                        edgeHits.Count, nEdgeRaw - edgeHits.Count);
                var dividers = BuildDividers(supports, edgeHits);
                if (dividers.Count < 2)
                {
                    ed.WriteMessage("\nDOSEMEDONATICIZ: Geçerli mesnet/sınır çifti yok.");
                    tr.Commit();
                    return 0;
                }
                bool lineOnlyTwoSiniri = supports.Count == 0
                    && dividers.Count == 2
                    && dividers[0].Kind == DividerKind.Siniri
                    && dividers[1].Kind == DividerKind.Siniri;

                // Kapalı alanlar bir kez (zoom'dan bağımsız model geometrisi)
                _kotCache = null;
                _parkBelow.Clear();
                _gprTransferred.Clear();
                EnsureKotCache(tr, btr);
                ed.WriteMessage("\nDOSEMEDONATICIZ: KOT YAZI cache={0}", _kotCache?.Count ?? 0);
                var slabTexts = slabTextsEarly;
                var boundaries = CollectBoundaryCurves(tr, btr);
                var gprNotes = CollectGprSteelNotes(tr, btr);
                CollectKirmiziGprCizgileri(tr, btr);
                if (EtkinYon != DonatiYonu.Serbest)
                    ed.WriteMessage("\nDOSEMEDONATICIZ: GPR {0} yazı {1}.",
                        gprNotes.Count, EtkinYon == DonatiYonu.X ? "kırmızı/X" : "mavi/Y");

                AltDuzAlanModel altDuzAlan;
                try { altDuzAlan = BuildAltDuzAlanModel(tr, btr, a, b); }
                catch
                {
                    altDuzAlan = new AltDuzAlanModel { Faces = new List<NetTopologySuite.Geometries.Polygon>() };
                }
                ed.WriteMessage("\nDOSEMEDONATICIZ: Mesnet hücresi {0}.",
                    altDuzAlan?.Faces?.Count ?? 0);

                var allSpans = new List<SpanRec>();
                int skippedBos = 0;
                for (int i = 0; i < dividers.Count - 1; i++)
                {
                    Divider leftD = dividers[i];
                    Divider rightD = dividers[i + 1];
                    Point3d leftInner = leftD.FaceMax;
                    Point3d rightInner = rightD.FaceMin;
                    Point3d leftOuter = leftD.FaceMin;
                    Point3d rightOuter = rightD.FaceMax;
                    double L = leftInner.DistanceTo(rightInner);
                    // İki mesnet → tam donatı. SINIRI/BOŞLUK kenarı → yalnız alt düz + konsol ilave.
                    bool duzOnly = lineOnlyTwoSiniri
                        || leftD.Kind != DividerKind.Mesnet
                        || rightD.Kind != DividerKind.Mesnet;
                    double minL = duzOnly ? 20.0 : MinSpanCm;
                    if (L < minL)
                    {
                        skippedBos++;
                        continue;
                    }

                    Point3d mid = Mid(leftInner, rightInner);
                    if (PointInsideBosluk(closedAreas, mid))
                    {
                        skippedBos++;
                        continue;
                    }
                    // Kat sınırı listesi doluysa yalnızca poligon içi (plan dışı donatı yok)
                    if (katSinirAreas.Count > 0 && !PointInsideKatSinir(katSinirAreas, mid))
                    {
                        skippedBos++;
                        continue;
                    }
                    // Çapraz kiriş / kalın mesnet: mid gövde içindeyse açıklık boyunca kaydır
                    if (PointInsideMesnetBody(closedAreas, mid)
                        && !TryNudgeMidOutOfMesnet(leftInner, rightInner, u, n, closedAreas, out mid))
                    {
                        skippedBos++;
                        continue;
                    }

                    double tL = (leftInner - a).DotProduct(u);
                    double tR = (rightInner - a).DotProduct(u);
                    if (tR < tL) { double tmpT = tL; tL = tR; tR = tmpT; }
                    double overlap = Math.Min(tR, tPick1) - Math.Max(tL, tPick0);
                    bool reachLeft = tPick0 <= tL + 20.0;
                    bool reachRight = tPick1 >= tR - 20.0;
                    // Mesnet açıklığı: iki yüze kadar gitmeli. Sınır/boşluk (alt düz): sanal hat
                    // aralığa ≥20 cm giriyorsa yeter (uçlar pick dışında kalabilir).
                    bool inPick = duzOnly
                        ? overlap >= 20.0
                        : (overlap >= 20.0 && reachLeft && reachRight);

                    bool edgeSpan = leftD.Kind != DividerKind.Mesnet || rightD.Kind != DividerKind.Mesnet;
                    bool touchesSiniri = leftD.Kind == DividerKind.Siniri || rightD.Kind == DividerKind.Siniri;
                    bool fromLabeledFace = false;
                    if (!TryClassifyGapAsDoseme(
                            boundaries, leftInner, rightInner, u, n, closedAreas, slabTexts, gprNotes,
                            out SlabInfo slab)
                        && !TryAcceptEdgeSiniriSlab(
                            boundaries, leftInner, rightInner, u, n, closedAreas, slabTexts, gprNotes,
                            L, edgeSpan, touchesSiniri, out slab))
                    {
                        if (!TrySlabFromLabeledFace(
                                altDuzAlan, mid, a, u, n, L, boundaries, closedAreas, slabTexts, gprNotes,
                                out slab))
                        {
                            skippedBos++;
                            if (inPick)
                                ed.WriteMessage("\n  Atlandı L={0:0} (kapalı döşeme yok / sınır sızması)", L);
                            continue;
                        }
                        fromLabeledFace = true;
                    }

                    // Kenardan girmek yetmez: döşemenin kendi donatısı yalnız hat iki yüzü de
                    // geçince (yukarıda) ya da hat tamamen bu açıklığın içindeyse çizilir.
                    // Uç döşeme (hat orada başlar/biter, öbür yüze varmaz) açılmaz.
                    // İlave/L/4, boydan kesilen komşunun uzatması olarak kalır.
                    bool pickInsideSpan = overlap >= 20.0
                        && tPick0 >= tL - 20.0 && tPick1 <= tR + 20.0;
                    if (!inPick && pickInsideSpan
                        && (fromLabeledFace || SpanMidInLabeledFace(altDuzAlan, mid, slabTexts)))
                    {
                        inPick = true;
                        reachLeft = true;
                        reachRight = true;
                    }

                    // Normal mesnet: dış yüz − paspayı (kirişi boydan geçer).
                    // Konsol/sınır (duzOnly): serbest kenarda paspayı içeri;
                    // mesnet ucunda diğer kiriş yüzüne paspayı kadar yaklaş (boydan gir).
                    Point3d barL;
                    Point3d barR;
                    if (duzOnly)
                    {
                        barL = leftD.Kind == DividerKind.Mesnet
                            ? leftOuter + u * PaspayiCm
                            : leftInner + u * PaspayiCm;
                        barR = rightD.Kind == DividerKind.Mesnet
                            ? rightOuter - u * PaspayiCm
                            : rightInner - u * PaspayiCm;
                    }
                    else
                    {
                        barL = leftOuter + u * PaspayiCm;
                        barR = rightOuter - u * PaspayiCm;
                    }
                    if (katSinirAreas.Count > 0 && PointInsideKatSinir(katSinirAreas, mid))
                    {
                        barL = ClampEndToKatSinir(katSinirAreas, mid, barL);
                        barR = ClampEndToKatSinir(katSinirAreas, mid, barR);
                    }
                    if (barL.DistanceTo(barR) < 20.0)
                        continue;

                    // DuzOnly da gerçek mesnet indeksi tutulsun (konsol L/4 komşu eşlemesi).
                    // PlaceUstIlaves DuzOnly'yi SpanStarting/Ending'de atlar.
                    int supportLeft = leftD.MesnetIndex >= 0 ? leftD.MesnetIndex : -1000 - i;
                    int supportRight = rightD.MesnetIndex >= 0 ? rightD.MesnetIndex : -1;
                    bool freeEndBosluk = duzOnly && (
                        (leftD.Kind == DividerKind.Bosluk && rightD.Kind == DividerKind.Mesnet)
                        || (rightD.Kind == DividerKind.Bosluk && leftD.Kind == DividerKind.Mesnet));

                    allSpans.Add(new SpanRec
                    {
                        SupportLeft = supportLeft,
                        SupportRight = supportRight,
                        L = L,
                        LeftInner = leftInner,
                        RightInner = rightInner,
                        LeftOuter = leftOuter,
                        RightOuter = rightOuter,
                        BarL = barL,
                        BarR = barR,
                        Mid = mid,
                        Slab = slab,
                        InPick = inPick,
                        ReachLeft = reachLeft,
                        ReachRight = reachRight,
                        DuzOnly = duzOnly,
                        LeftIsMesnet = leftD.Kind == DividerKind.Mesnet,
                        RightIsMesnet = rightD.Kind == DividerKind.Mesnet,
                        FreeEndBosluk = freeEndBosluk
                    });
                }

                PromoteLabeledFacePicks(allSpans, altDuzAlan, slabTexts, a, u, tPick0, tPick1);
                int addedCells = AddSpansForLabeledFaces(
                    allSpans, altDuzAlan, slabTexts, gprNotes, boundaries, closedAreas,
                    a, u, n, tPick0, tPick1);
                if (addedCells > 0)
                    ed.WriteMessage("\nDOSEMEDONATICIZ: Etiketli hücreden ek açıklık {0}.", addedCells);
                allSpans.Sort((p, q) =>
                    (p.Mid - a).DotProduct(u).CompareTo((q.Mid - a).DotProduct(u)));

                if (allSpans.Count == 0)
                {
                    ed.WriteMessage(
                        "\nDOSEMEDONATICIZ: Döşeme açıklığı yok (mesnet {0}, boş/atlanan {1}).",
                        supports.Count, skippedBos);
                    tr.Commit();
                    return 0;
                }

                ed.WriteMessage(
                    "\nDOSEMEDONATICIZ: {0} mesnet, {1} döşeme, boş/atlanan {2}.",
                    supports.Count, allSpans.Count, skippedBos);

                int placed = 0;
                double maxTopAlongNCm = 0;
                var placedKonsolAlt = new List<KonsolPlacedBar>();
                var spanSlabs = new List<(int supportLeft, SlabInfo slab, int adet, int cap, int aralik)>();
                var drawnSupportLeft = new HashSet<int>();
                ApplyNormalSlabKotFromArea(allSpans, altDuzAlan, slabTexts);
                DropDuplicateSlabSpans(allSpans, slabTexts);
                Polyline[] spanBounds = CollectSpanBoundaries(allSpans, altDuzAlan, a, u, n);
                Polyline[] ownPoly = spanBounds == null ? null : (Polyline[])spanBounds.Clone();

                // BOUNDARY: yalnız mesnet katmanı. Ekran yenilenmez; katmanlar komut bitmeden geri açılır.
                ObjectId boundaryClayer = ObjectId.Null;
                List<ObjectId> boundaryHidden = null;
                if (EtkinYon != DonatiYonu.Serbest)
                    boundaryHidden = HideNonMesnetForBoundary(tr, db, out boundaryClayer);
                try
                {
                for (int si = 0; si < allSpans.Count; si++)
                {
                    SpanRec span = allSpans[si];
                if (!span.InPick)
                    continue;

                SpanRec? prev = si > 0 ? allSpans[si - 1] : (SpanRec?)null;
                    SpanRec? next = si + 1 < allSpans.Count ? allSpans[si + 1] : (SpanRec?)null;

                    // Boşluklu döşeme parçası (mesnet↔boşluk): normal açıklıktan L/4 pilye/ilave uzar.
                    // SINIRI konsol (FreeEndBosluk=false) buraya girmez.
                    bool HoleNeighborPrev(SpanRec nb) =>
                        nb.DuzOnly && nb.FreeEndBosluk && nb.RightIsMesnet && !nb.LeftIsMesnet
                        && (nb.SupportRight == span.SupportLeft
                            || (span.SupportLeft >= 0 && nb.SupportRight == span.SupportLeft)
                            || span.LeftOuter.DistanceTo(nb.RightOuter) <= 15.0);

                    bool HoleNeighborNext(SpanRec nb) =>
                        nb.DuzOnly && nb.FreeEndBosluk && nb.LeftIsMesnet && !nb.RightIsMesnet
                        && (nb.SupportLeft == span.SupportLeft + 1
                            || (span.SupportRight >= 0 && nb.SupportLeft == span.SupportRight)
                            || span.RightOuter.DistanceTo(nb.LeftOuter) <= 15.0);

                    // Komşu: iki mesnet açıklık VEYA aynı kot boşluklu döşeme parçası
                    bool hasPrev = !span.DuzOnly && prev.HasValue
                        && SlabsContinue(span.Slab, prev.Value.Slab)
                        && ((!prev.Value.DuzOnly && prev.Value.SupportLeft + 1 == span.SupportLeft)
                            || HoleNeighborPrev(prev.Value));
                    bool hasNext = !span.DuzOnly && next.HasValue
                        && SlabsContinue(span.Slab, next.Value.Slab)
                        && ((!next.Value.DuzOnly && span.SupportLeft + 1 == next.Value.SupportLeft)
                            || HoleNeighborNext(next.Value));
                    // allSpans sırası atlanmış olabilir — boşluk parçası için tara
                    if (!span.DuzOnly && !hasPrev)
                    {
                        for (int j = 0; j < allSpans.Count && !hasPrev; j++)
                        {
                            if (HoleNeighborPrev(allSpans[j])
                                && SlabsContinue(span.Slab, allSpans[j].Slab))
                            {
                                hasPrev = true;
                                prev = allSpans[j];
                            }
                        }
                    }
                    if (!span.DuzOnly && !hasNext)
                    {
                        for (int j = 0; j < allSpans.Count && !hasNext; j++)
                        {
                            if (HoleNeighborNext(allSpans[j])
                                && SlabsContinue(span.Slab, allSpans[j].Slab))
                            {
                                hasNext = true;
                                next = allSpans[j];
                            }
                        }
                    }
                    double Lprev = hasPrev && prev.HasValue ? prev.Value.L : 0;
                    double Lnext = hasNext && next.HasValue ? next.Value.L : 0;
                    // Boşluk parçasına pilye: min 50 cm; alan <50 ise sınırda paspayı kadar yaklaş
                    // (TryPlacePilyeSpan L×1/4 kullandığı için referans boy = uz×4)
                    if (hasPrev && prev.HasValue && prev.Value.FreeEndBosluk)
                        Lprev = HolePilyeUzatmaRefLen(prev.Value.L);
                    if (hasNext && next.HasValue && next.Value.FreeEndBosluk)
                        Lnext = HolePilyeUzatmaRefLen(next.Value.L);
                    // Komşu allSpans'te yok (solda perde tek kenar, açıklık atlandı, kot okunamadı…)
                    // ama kapalı alanda aynı üst kot döşeme varsa yine L/4
                    if (!span.ReachLeft) { hasPrev = false; Lprev = 0; }
                    if (!span.ReachRight) { hasNext = false; Lnext = 0; }
                    bool konsolPrev = false, konsolNext = false;
                    double konsolBoyPrev = 0, konsolBoyNext = 0;
                    // Konsol: pick mesnet dışına kadar gitmese bile öte yanı yokla (firkete/ilave).
                    // Pilye L/4 yalnız Reach + aynı kot. Boşluk parçasına uzatma hasPrev/Next ile.
                    if (!span.DuzOnly && !hasPrev && TryNeighborSlabBeyond(
                            boundaries, span.LeftOuter, -u, n, closedAreas, slabTexts, gprNotes,
                            span.Slab, span.L, out double Lp, out bool kP, out double kBoyP))
                    {
                        if (kP)
                        {
                            konsolPrev = true;
                            konsolBoyPrev = kBoyP;
                        }
                        if (Lp > 1.0 && span.ReachLeft)
                        {
                            hasPrev = true;
                            Lprev = Lp;
                        }
                    }
                    if (!span.DuzOnly && !hasNext && TryNeighborSlabBeyond(
                            boundaries, span.RightOuter, u, n, closedAreas, slabTexts, gprNotes,
                            span.Slab, span.L, out double Ln, out bool kN, out double kBoyN))
                    {
                        if (kN)
                        {
                            konsolNext = true;
                            konsolBoyNext = kBoyN;
                        }
                        if (Ln > 1.0 && span.ReachRight)
                        {
                            hasNext = true;
                            Lnext = Ln;
                        }
                    }
                    // Bitişik kısa SINIRI konsol (boşluk kenarı değil) — TryNeighbor kaçırsa
                    if (!span.DuzOnly)
                    {
                        for (int j = 0; j < allSpans.Count; j++)
                        {
                            SpanRec d = allSpans[j];
                            if (!d.DuzOnly || d.FreeEndBosluk) continue; // boşluk → pilye/ilave, konsol değil
                            if (d.L < PaspayiCm || d.L > SiniriKonsolMaxCm) continue;
                            if (!konsolNext
                                && (span.RightOuter.DistanceTo(d.LeftInner) <= 8.0
                                    || span.RightOuter.DistanceTo(d.LeftOuter) <= 8.0))
                            {
                                konsolNext = true;
                                konsolBoyNext = d.L;
                            }
                            if (!konsolPrev
                                && (span.LeftOuter.DistanceTo(d.RightInner) <= 8.0
                                    || span.LeftOuter.DistanceTo(d.RightOuter) <= 8.0))
                            {
                                konsolPrev = true;
                                konsolBoyPrev = d.L;
                            }
                        }
                    }
                    // Dilatasyon / iki kat sınırı: yüzün 3 cm dışı kendi halkadan çıkıyorsa
                    // komşu blokta devam yok → gönye (L/4 veya konsol uzatması yok).
                    Point3d spanMidKat = Mid(span.LeftInner, span.RightInner);
                    if (hasPrev && !KatSinirAllowsContinue(katSinirAreas, spanMidKat, span.LeftOuter, -u))
                    {
                        hasPrev = false;
                        Lprev = 0;
                    }
                    if (hasNext && !KatSinirAllowsContinue(katSinirAreas, spanMidKat, span.RightOuter, u))
                    {
                        hasNext = false;
                        Lnext = 0;
                    }
                    if (konsolPrev && !KatSinirAllowsContinue(katSinirAreas, spanMidKat, span.LeftOuter, -u))
                    {
                        konsolPrev = false;
                        konsolBoyPrev = 0;
                    }
                    if (konsolNext && !KatSinirAllowsContinue(katSinirAreas, spanMidKat, span.RightOuter, u))
                    {
                        konsolNext = false;
                        konsolBoyNext = 0;
                    }

                    span.ExtPrev = hasPrev || konsolPrev;
                    span.ExtNext = hasNext || konsolNext;
                    span.LprevExt = Lprev;
                    span.LnextExt = Lnext;
                    span.KonsolPrev = konsolPrev;
                    span.KonsolNext = konsolNext;
                    span.KonsolBoyPrev = konsolBoyPrev;
                    span.KonsolBoyNext = konsolBoyNext;
                    allSpans[si] = span;

                    int staggerIdx = span.SupportLeft >= 0 ? span.SupportLeft : si;
                    Vector3d stagger = n * ((staggerIdx % 2) * StaggerCm);
                    Vector3d pilyeLow = stagger;
                    Vector3d duzOff = stagger - n * DuzBelowPilyeCm;
                    // Konsol/sınır alt düz: komşu (normal) döşeme altından her zaman +5 cm yukarı
                    if (span.DuzOnly)
                    {
                        int ni = -1;
                        if (prev.HasValue && !prev.Value.DuzOnly) ni = si - 1;
                        else if (next.HasValue && !next.Value.DuzOnly) ni = si + 1;
                        else
                        {
                            for (int j = si - 1; j >= 0 && ni < 0; j--)
                                if (!allSpans[j].DuzOnly) ni = j;
                            for (int j = si + 1; j < allSpans.Count && ni < 0; j++)
                                if (!allSpans[j].DuzOnly) ni = j;
                        }
                        if (ni >= 0)
                        {
                            SpanRec nb = allSpans[ni];
                            int nbIdx = nb.SupportLeft >= 0 ? nb.SupportLeft : ni;
                            Vector3d nbDuz = n * ((nbIdx % 2) * StaggerCm) - n * DuzBelowPilyeCm;
                            duzOff = nbDuz + n * StaggerCm;
                        }
                        else
                            duzOff = -n * DuzBelowPilyeCm + n * StaggerCm;
                    }

                    // ≤50 cm konsol: firkete üst ilave altın yerini tutar — alt düz yok
                    bool skipAltDuz = span.DuzOnly && span.L <= KonsolPilyeMinUzCm;
                    if (!skipAltDuz && span.DuzOnly)
                    {
                        // Üst üste iki konsol alt düz: boy ekseninde örtüşen ikincisi +5 cm
                        double alongAlt = ResolveKonsolAlongN(
                            placedKonsolAlt, span.BarL, span.BarR, u, duzOff.DotProduct(n));
                        duzOff = n * alongAlt;
                    }
                    // 2 m'den kısa, sınır/boşluk açıklığı veya sanal hat tam kesmiyor: pilye yok
                    List<Point3d> pilPath = null;
                    bool okPil = !span.DuzOnly
                        && span.ReachLeft && span.ReachRight && span.L >= MinPilyeSpanCm
                        && TryBuildPilyeSpan(
                        span.LeftInner, span.RightInner, span.LeftOuter, span.RightOuter,
                        span.BarL, span.BarR,
                        u, n, span.L, Lprev, Lnext, hasPrev, hasNext, pilyeLow,
                        out _, out pilPath);

                    double topCm = (span.SupportLeft % 2) * StaggerCm + (okPil ? PilyeKirimRiseCm : 0);
                    if (topCm > maxTopAlongNCm) maxTopAlongNCm = topCm;

                    allSpans[si] = span;
                    SlabInfo slab = span.Slab;
                    double Lbar = span.BarL.DistanceTo(span.BarR);
                    int cap = slab.CapMm > 0 ? slab.CapMm : 8;
                    int aralik = slab.AralikCm > 0 ? slab.AralikCm : 20;
                    int adet = slab.Adet ?? EstimateAdet(Lbar, aralik);
                    Point3d duzMid = Mid(span.BarL + duzOff, span.BarR + duzOff);
                    if (PointInsideAltMesnet(altDuzAlan, duzMid))
                    {
                        // Kiriş şeridine değiyorsa yalnız çubuğa dik, en fazla 8 cm.
                        // Açıklık ortasına (pilye hizası) zıplamasın.
                        for (double s = 2.0; s <= 8.0; s += 2.0)
                        {
                            Point3d q = duzMid + n * s;
                            if (!PointInsideAltMesnet(altDuzAlan, q))
                            {
                                duzMid = q;
                                break;
                            }
                        }
                    }
                    if (EtkinYon != DonatiYonu.Serbest && ownPoly != null && si < ownPoly.Length)
                    {
                        // AutoCAD BOUNDARY, ada algılama açık. Tohum: alt düz ortası.
                        Polyline built = TraceSlabBoundary(ed, duzMid, n);
                        if (built == null)
                            built = BoundaryAtSeed(altDuzAlan, duzMid);
                        if (built != null)
                        {
                            try { ownPoly[si]?.Dispose(); } catch { }
                            ownPoly[si] = built;
                            if (spanBounds != null && si < spanBounds.Length)
                                spanBounds[si] = built;
                            SpanRec spG = span;
                            AssignXGprFromKirmiziCizgi(ref spG, gprNotes, u, built);
                            span = spG;
                            allSpans[si] = span;
                            slab = span.Slab;
                        }
                    }
                    bool duzCopied = TakeOwnedGpr(slab.GprSteels, duzMid, GprSteelKind.Duz, ref adet, ref cap, ref aralik,
                        span.LeftInner, span.RightInner, u, si, allSpans, ownPoly);
                    bool duzGpr = duzCopied;
                    // Konsol alt düz: boy tablosu değil, sabit ø8/20
                    if (span.DuzOnly)
                    {
                        cap = 8;
                        aralik = 20;
                        adet = EstimateAdet(Lbar, aralik);
                        duzGpr = true; // konsol alt sabit — kırmızı değil
                    }

                    int pilCap = cap, pilAralik = aralik, pilAdet = adet;
                    bool pilCopied = false;
                    bool pilGpr = false;
                    if (okPil)
                    {
                        Point3d pilMidTmp = Mid(span.LeftInner, span.RightInner) + pilyeLow;
                        pilCopied = TakeOwnedGpr(slab.GprSteels, pilMidTmp, GprSteelKind.Pilye, ref pilAdet, ref pilCap, ref pilAralik,
                            span.LeftInner, span.RightInner, u, si, allSpans, ownPoly);
                        pilGpr = pilCopied;
                        if (!pilCopied && duzCopied)
                        {
                            pilAdet = adet;
                            pilCap = cap;
                            pilAralik = aralik;
                            pilGpr = true;
                        }
                    }
                    if (!span.DuzOnly)
                    {
                        spanSlabs.Add((span.SupportLeft, slab, adet, cap, aralik));
                        drawnSupportLeft.Add(span.SupportLeft);
                    }

                    if (!skipAltDuz)
                    {
                        var duzPath = new[] { span.BarL + duzOff, span.BarR + duzOff };
                        DrawLabeledStock(
                            tr, btr, db, duzPath, cap, adet, aralik, n, u,
                            donatiLayer, yaziLayer, yaziStyleId, upper: false,
                            duzMid, missingGprMatch: !duzGpr,
                            !duzGpr ? StockParkKind.Unmatched : StockParkKind.None, 0);
                    }
                    if (okPil && pilPath != null)
                    {
                        Point3d pilMid = Mid(span.LeftInner, span.RightInner) + pilyeLow;
                        StockParkKind pilPark = StockParkKind.None;
                        if (!pilCopied && EtkinYon != DonatiYonu.Serbest)
                            pilPark = StockParkKind.MoveDown;
                        else if (!pilGpr)
                            pilPark = StockParkKind.Unmatched;
                        DrawLabeledStock(
                            tr, btr, db, pilPath, pilCap, pilAdet, pilAralik, n, u,
                            donatiLayer, yaziLayer, yaziStyleId, upper: true,
                            pilMid, missingGprMatch: !pilGpr, pilPark, 0, pilyeOrtada: true);
                    }

                    if (EtkinYon != DonatiYonu.Serbest && !span.DuzOnly
                        && TryFindGprSteel(slab.GprSteels, Mid(span.LeftInner, span.RightInner),
                            GprSteelKind.Montaj, out GprSteelRec monGpr,
                            span.LeftInner, span.RightInner, u))
                    {
                        double monCm = PilyeKirimRiseCm + 14.0;
                        if (monCm > maxTopAlongNCm) maxTopAlongNCm = monCm;
                        Vector3d monOff = n * monCm;
                        if (SpanOwnsGprText(monGpr.Pos, si, allSpans, ownPoly))
                            MarkPark(monGpr.Id);
                        int mAdet = monGpr.Adet > 0 ? monGpr.Adet : adet;
                        int mCap = monGpr.CapMm > 0 ? monGpr.CapMm : cap;
                        int mAr = monGpr.AralikCm > 0 ? monGpr.AralikCm : aralik;
                        var monPath = new[] { span.BarL + monOff, span.BarR + monOff };
                        DrawLabeledStock(
                            tr, btr, db, monPath, mCap, mAdet, mAr, n, u,
                            donatiLayer, yaziLayer, yaziStyleId, upper: true,
                            Mid(span.BarL + monOff, span.BarR + monOff),
                            missingGprMatch: false, StockParkKind.None, 0);
                    }

                    string kotMsg = slab.KotM.HasValue
                        ? string.Format(CultureInfo.InvariantCulture, " kot={0:0.00}", slab.KotM.Value)
                        : " kot=YOK";
                    string dMsg = slab.ThicknessCm.HasValue
                        ? string.Format(CultureInfo.InvariantCulture, " d={0:0}", slab.ThicknessCm.Value)
                        : "";
                    string contMsg = span.DuzOnly
                        ? " konsol/sınır"
                        : (hasPrev ? " ←L/4" : " ←gönye") + (hasNext ? " L/4→" : " gönye→");
                    string slabName = string.IsNullOrEmpty(slab.Name) ? "?" : slab.Name;
                    ed.WriteMessage("\n  Açıklık {0} {1}: L={2:0}{3}{4}{5}",
                        span.DuzOnly ? "S" : (span.SupportLeft + 1).ToString(),
                        slabName, span.L, dMsg, kotMsg, contMsg);

                    placed++;
                }
                }
                finally
                {
                    RestoreBoundaryLayers(tr, db, boundaryHidden, boundaryClayer);
                }

                // İlave üst: çizilen açıklıklara komşu mesnetler; devam = aynı d/kot komşu açıklık
                var placedKonsolUst = new List<KonsolPlacedBar>();
                PlaceAllUstIlave(
                    tr, btr, db, supports, allSpans, drawnSupportLeft, u, n,
                    donatiLayer, yaziLayer, yaziStyleId,
                    maxTopAlongNCm + UstIlaveAboveTopCm, spanSlabs, placedKonsolUst,
                    gprNotes, altDuzAlan != null ? altDuzAlan.SupportSegs : null);
                PlaceSoloKonsolIlaves(
                    tr, btr, db, allSpans, boundaries, closedAreas, slabTexts, gprNotes, u, n,
                    Math.Max(maxTopAlongNCm, 0) + UstIlaveAboveTopCm,
                    donatiLayer, yaziLayer, yaziStyleId, placedKonsolUst);
                ParkGuidePairIfTextsConsumed(allSpans, ownPoly, gprNotes);

                int boundN = 0;
                if (ownPoly != null)
                {
                    string polyLayer = EnsureLayer(tr, db, LayerAltDuzAlan, 6, LineWeight.LineWeight020);
                    ForceLayerVisible(tr, db, polyLayer);
                    for (int i = 0; i < ownPoly.Length; i++)
                    {
                        Polyline pl = ownPoly[i];
                        if (pl == null) continue;
                        boundN++;
                        try
                        {
                            pl.Layer = polyLayer;
                            pl.ColorIndex = 6;
                            if (pl.ObjectId.IsNull)
                            {
                                btr.AppendEntity(pl);
                                tr.AddNewlyCreatedDBObject(pl, true);
                            }
                        }
                        catch { }
                        ownPoly[i] = null;
                        if (spanBounds != null && i < spanBounds.Length)
                            spanBounds[i] = null;
                    }
                }

                ed.WriteMessage("\nDOSEMEDONATICIZ: Açıklık poligonu {0} çizildi (kontrol, magenta).",
                    boundN);
                FlushParkBelow(tr);

                tr.Commit();
                return placed;
            }
        }

        private static HashSet<string> BuildMesnetLayerSet()
        {
            DosemeDonatiSettings.EnsureLoaded();
            if (DosemeDonatiSettings.MesnetKatmanlar.Count > 0)
                return new HashSet<string>(DosemeDonatiSettings.MesnetKatmanlar, StringComparer.OrdinalIgnoreCase);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                LayerKiris,
                LayerKolon,
                LayerPerde,
                LayerKapamaPerdesi
            };
        }

        private static bool IsMesnetLayer(string lyr, HashSet<string> exact)
        {
            if (string.IsNullOrEmpty(lyr)) return false;
            DosemeDonatiSettings.EnsureLoaded();
            if (DosemeDonatiSettings.MesnetKatmanlar.Count > 0)
                return DosemeDonatiSettings.ListHas(DosemeDonatiSettings.MesnetKatmanlar, lyr);
            if (exact != null && exact.Contains(lyr)) return true;
            return lyr.IndexOf("KAPAMA PERDESI", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(lyr, LayerPerde, StringComparison.OrdinalIgnoreCase)
                || string.Equals(lyr, LayerKiris, StringComparison.OrdinalIgnoreCase)
                || string.Equals(lyr, LayerKolon, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDosemeSiniriLayer(string lyr)
        {
            if (string.IsNullOrEmpty(lyr)) return false;
            DosemeDonatiSettings.EnsureLoaded();
            if (DosemeDonatiSettings.SinirKatmanlar.Count > 0)
                return DosemeDonatiSettings.ListHas(DosemeDonatiSettings.SinirKatmanlar, lyr);
            return lyr.IndexOf("DOSEME SINIRI", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSiniriOrBoslukLayer(string lyr)
        {
            // Merdiven çizgileri yoksayılır (sınır/kenar sayılmaz).
            return IsDosemeSiniriLayer(lyr) || IsKalipBoslukLayer(lyr);
        }

        private static bool PointInsideBosluk(List<ClosedArea> areas, Point3d p)
        {
            return areas != null
                && FindSmallestContaining(areas, new Point3d(p.X, p.Y, 0), a => a.IsBosluk).HasValue;
        }

        private static bool IsKatSinirLayer(string lyr)
        {
            if (string.IsNullOrEmpty(lyr)) return false;
            DosemeDonatiSettings.EnsureLoaded();
            return DosemeDonatiSettings.KatSinirKatmanlar.Count > 0
                && DosemeDonatiSettings.ListHas(DosemeDonatiSettings.KatSinirKatmanlar, lyr);
        }

        private static bool PointInsideKatSinir(List<ClosedArea> areas, Point3d p)
        {
            if (areas == null || areas.Count == 0) return true;
            return TryFindKatSinirContaining(areas, p, out _);
        }

        /// <summary>
        /// Yüzün dışına 3 cm sonda hâlâ aynı kat sınırındaysa komşu döşemeye devam vardır.
        /// Dilatasyon boşluğunda sonda halka dışına çıkar → devam yok (gönye).
        /// </summary>
        private static bool KatSinirAllowsContinue(
            List<ClosedArea> areas, Point3d spanMid, Point3d faceOuter, Vector3d outward)
        {
            if (areas == null || areas.Count == 0) return true;
            if (!TryFindKatSinirContaining(areas, spanMid, out ClosedArea home))
                return true;
            if (outward.LengthSqrd < 1e-12) return true;
            Point3d probe = faceOuter + outward.GetNormal() * 3.0;
            return PointInClosedArea(home, probe);
        }

        /// <summary>
        /// Noktayı içeren kat sınırı (en küçük halka). İki dilatasyon = iki poligon;
        /// birleşim değil, her blok kendi halkası.
        /// </summary>
        private static bool TryFindKatSinirContaining(
            List<ClosedArea> areas, Point3d p, out ClosedArea found)
        {
            found = default;
            if (areas == null || areas.Count == 0) return false;
            double best = double.MaxValue;
            bool ok = false;
            for (int i = 0; i < areas.Count; i++)
            {
                ClosedArea a = areas[i];
                if (a.Ring == null) continue;
                if (!PointInRing(a.Ring, p.X, p.Y)) continue;
                if (a.Area >= best) continue;
                best = a.Area;
                found = a;
                ok = true;
            }
            return ok;
        }

        private static bool PointInClosedArea(ClosedArea a, Point3d p)
        {
            return a.Ring != null && PointInRing(a.Ring, p.X, p.Y);
        }

        /// <summary>Uç, referansın bulunduğu kat sınırı dışındaysa o halkada kesilir (öteki dilatasyona geçmez).</summary>
        private static Point3d ClampEndToKatSinir(List<ClosedArea> areas, Point3d insideRef, Point3d end)
        {
            if (!TryFindKatSinirContaining(areas, insideRef, out ClosedArea home))
                return end;
            return ClampEndToOneKatSinir(home, insideRef, end);
        }

        private static Point3d ClampEndToOneKatSinir(ClosedArea home, Point3d insideRef, Point3d end)
        {
            if (PointInClosedArea(home, end)) return end;
            if (!PointInClosedArea(home, insideRef)) return end;
            Point3d lo = insideRef;
            Point3d hi = end;
            for (int k = 0; k < 24; k++)
            {
                Point3d m = Mid(lo, hi);
                if (PointInClosedArea(home, m)) lo = m;
                else hi = m;
            }
            return lo;
        }

        /// <summary>Yol, ilk noktanın kat sınırı halkasında kalır; dilatasyon ötesi / diğer blok kesilir.</summary>
        private static IList<Point3d> ClipPathToKatSinir(IList<Point3d> path)
        {
            if (path == null || _katSinirClamp == null || _katSinirClamp.Count == 0)
                return path;
            ClosedArea home = default;
            bool have = false;
            for (int i = 0; i < path.Count; i++)
            {
                if (!TryFindKatSinirContaining(_katSinirClamp, path[i], out home)) continue;
                have = true;
                break;
            }
            if (!have) return new List<Point3d>();

            bool Inside(Point3d p) => PointInClosedArea(home, p);
            var dst = new List<Point3d>();
            if (path.Count > 0 && Inside(path[0]))
                dst.Add(path[0]);
            for (int i = 1; i < path.Count; i++)
            {
                Point3d a = path[i - 1];
                Point3d b = path[i];
                bool aIn = Inside(a);
                bool bIn = Inside(b);
                if (aIn && bIn)
                {
                    dst.Add(b);
                    continue;
                }
                if (aIn)
                {
                    Point3d cut = ClampEndToOneKatSinir(home, a, b);
                    if (dst.Count == 0 || dst[dst.Count - 1].DistanceTo(cut) > 0.5)
                        dst.Add(cut);
                    continue;
                }
                if (bIn)
                {
                    Point3d cut = ClampEndToOneKatSinir(home, b, a);
                    if (dst.Count == 0 || dst[dst.Count - 1].DistanceTo(cut) > 0.5)
                        dst.Add(cut);
                    dst.Add(b);
                }
            }
            return dst;
        }

        /// <summary>Formdaki kat sınırı katmanlarından kapalı poligonlar.</summary>
        private static List<ClosedArea> CollectKatSinirAreas(Transaction tr, BlockTableRecord btr)
        {
            var list = new List<ClosedArea>();
            DosemeDonatiSettings.EnsureLoaded();
            if (DosemeDonatiSettings.KatSinirKatmanlar.Count == 0) return list;
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || !ent.Visible) continue;
                string lyr = ent.Layer ?? "";
                if (!DosemeDonatiSettings.ListHas(DosemeDonatiSettings.KatSinirKatmanlar, lyr))
                    continue;
                if (!TryGetKatSinirRing(tr, ent, out Point2d[] ring, out double area))
                    continue;
                list.Add(new ClosedArea
                {
                    Ring = ring,
                    Area = area,
                    IsBosluk = false,
                    IsDosemeSiniri = false,
                    IsMesnetBody = false
                });
            }
            list.Sort((a, b) => b.Area.CompareTo(a.Area)); // büyük (dış) önce
            return list;
        }

        /// <summary>Kapalı polyline veya ucu ucuna değen halka. Eski polyline dahil.</summary>
        private static bool TryGetKatSinirRing(Transaction tr, Entity ent, out Point2d[] ring, out double area)
        {
            ring = null;
            area = 0;
            var verts = new List<Point2d>();
            bool flagged = false;
            if (ent is Polyline pl && pl.NumberOfVertices >= 3)
            {
                flagged = pl.Closed;
                int n = pl.NumberOfVertices;
                for (int i = 0; i < n; i++)
                    verts.Add(pl.GetPoint2dAt(i));
            }
            else if (ent is Polyline2d p2)
            {
                flagged = p2.Closed;
                foreach (ObjectId vid in p2)
                {
                    try
                    {
                        if (tr.GetObject(vid, OpenMode.ForRead) is Vertex2d v)
                            verts.Add(new Point2d(v.Position.X, v.Position.Y));
                    }
                    catch { }
                }
            }
            else
                return false;

            int count = verts.Count;
            if (!flagged)
            {
                if (count < 4 || verts[0].GetDistanceTo(verts[count - 1]) > 1.0)
                    return false;
                count--;
            }
            if (count < 3) return false;
            ring = new Point2d[count];
            for (int i = 0; i < count; i++)
                ring[i] = verts[i];
            area = Math.Abs(RingSignedArea(ring));
            return area > 1.0;
        }

        private static bool PointInsideMesnetBody(List<ClosedArea> areas, Point3d p)
        {
            return areas != null
                && FindSmallestContaining(areas, new Point3d(p.X, p.Y, 0), a => a.IsMesnetBody).HasValue;
        }

        private static bool PointInsideDosemeSiniri(List<ClosedArea> areas, Point3d p)
        {
            return areas != null
                && FindSmallestContaining(areas, new Point3d(p.X, p.Y, 0), a => a.IsDosemeSiniri).HasValue;
        }

        /// <summary>
        /// DOSEME SINIRI (veya boşluk kenarı) kiriş/kolon/perde ile ≤0.2 cm üst üste → yok say
        /// (tek çizgi; mesnet öncelikli).
        /// </summary>
        private static void SuppressSiniriOnMesnet(
            List<HitRec> edgeHits, List<HitRec> mesnetHits, List<SupportCluster> supports)
        {
            if (edgeHits == null || edgeHits.Count == 0) return;
            for (int i = edgeHits.Count - 1; i >= 0; i--)
            {
                if (EdgeHitOnMesnet(edgeHits[i], mesnetHits, supports))
                    edgeHits.RemoveAt(i);
            }
        }

        private static bool EdgeHitOnMesnet(
            HitRec h, List<HitRec> mesnetHits, List<SupportCluster> supports)
        {
            if (mesnetHits != null)
            {
                foreach (HitRec m in mesnetHits)
                {
                    if (Math.Abs(m.T - h.T) <= CoincidentLineTolCm) return true;
                    if (m.Point.DistanceTo(h.Point) <= CoincidentLineTolCm) return true;
                }
            }
            if (supports != null)
            {
                foreach (var s in supports)
                {
                    if (Math.Abs(h.T - s.TMin) <= CoincidentLineTolCm) return true;
                    if (Math.Abs(h.T - s.TMax) <= CoincidentLineTolCm) return true;
                    if (h.Point.DistanceTo(s.FaceMin) <= CoincidentLineTolCm) return true;
                    if (h.Point.DistanceTo(s.FaceMax) <= CoincidentLineTolCm) return true;
                    if (s.TMax - s.TMin > 1.0
                        && h.T > s.TMin + CoincidentLineTolCm
                        && h.T < s.TMax - CoincidentLineTolCm)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Mesnetler + DOSEME SINIRI / KALIP BOSLUK kenarları (mesnetle üst üste sınır atılır).
        /// </summary>
        private static List<Divider> BuildDividers(List<SupportCluster> supports, List<HitRec> edgeHits)
        {
            var list = new List<Divider>();
            if (supports != null)
            {
                for (int i = 0; i < supports.Count; i++)
                {
                    SupportCluster s = supports[i];
                    list.Add(new Divider
                    {
                        Kind = DividerKind.Mesnet,
                        TMin = s.TMin,
                        TMax = s.TMax,
                        FaceMin = s.FaceMin,
                        FaceMax = s.FaceMax,
                        MesnetIndex = i
                    });
                }
            }
            if (edgeHits != null)
            {
                foreach (HitRec h in edgeHits)
                {
                    if (EdgeHitOnMesnet(h, null, supports)) continue;
                    // Boşluk kenarı → Bosluk; salt sınır → Siniri (ikisi de DuzOnly + konsol ilave).
                    bool bos = IsBoslukLayer(h.Layer);
                    list.Add(new Divider
                    {
                        Kind = bos ? DividerKind.Bosluk : DividerKind.Siniri,
                        TMin = h.T,
                        TMax = h.T,
                        FaceMin = h.Point,
                        FaceMax = h.Point,
                        MesnetIndex = -1
                    });
                }
            }
            list.Sort((p, q) => p.TMin.CompareTo(q.TMin));
            for (int i = list.Count - 1; i > 0; i--)
            {
                if (Math.Abs(list[i].TMin - list[i - 1].TMax) > CoincidentLineTolCm) continue;
                Divider a = list[i - 1], b = list[i];
                if (a.Kind == DividerKind.Mesnet) { list.RemoveAt(i); continue; }
                if (b.Kind == DividerKind.Mesnet) { list.RemoveAt(i - 1); continue; }
                if (a.Kind == DividerKind.Bosluk) { list.RemoveAt(i); continue; }
                if (b.Kind == DividerKind.Bosluk) { list.RemoveAt(i - 1); continue; }
                list.RemoveAt(i);
            }
            return list;
        }

        /// <summary>
        /// Çizgisel: formdaki boşluk listesi. Diğer: KALIP BOSLUK.
        /// MERDIVEN çizgileri sınır kenarı sayılmaz (CollectBoundaryCurves yoksayar).
        /// </summary>
        private static bool IsKalipBoslukLayer(string lyr)
        {
            if (string.IsNullOrEmpty(lyr)) return false;
            if (CizgiselMod)
            {
                DosemeDonatiSettings.EnsureLoaded();
                if (DosemeDonatiSettings.BoslukKatmanlar.Count > 0)
                    return DosemeDonatiSettings.ListHas(DosemeDonatiSettings.BoslukKatmanlar, lyr);
            }
            return string.Equals(lyr, LayerKalipBosluk, StringComparison.OrdinalIgnoreCase)
                || lyr.IndexOf("KALIP BOSLUK", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(lyr, "BOS", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMerdivenLayer(string lyr)
        {
            if (string.IsNullOrEmpty(lyr)) return false;
            if (string.Equals(lyr, LayerMerdiven, StringComparison.OrdinalIgnoreCase))
                return true;
            return lyr.IndexOf("MERDIVEN", StringComparison.OrdinalIgnoreCase) >= 0
                && lyr.IndexOf("YAZI", StringComparison.OrdinalIgnoreCase) < 0
                && lyr.IndexOf("ISMI", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>
        /// Kapalı boşluk alanı — içine donatı çizilmez.
        /// Çizgiselde formdaki boşluk katman listesi; diğerinde kalıp boşluk + merdiven.
        /// </summary>
        private static bool IsBoslukLayer(string lyr)
        {
            if (CizgiselMod)
            {
                DosemeDonatiSettings.EnsureLoaded();
                if (DosemeDonatiSettings.BoslukKatmanlar.Count > 0)
                    return DosemeDonatiSettings.ListHas(DosemeDonatiSettings.BoslukKatmanlar, lyr);
            }
            return IsKalipBoslukLayer(lyr) || IsMerdivenLayer(lyr);
        }

        /// <summary>
        /// Kesişimleri mesnete kümele (kiriş gibi):
        /// aynı poligonun iki segmenti (aynı entity) veya MaxMesnetWidthCm içindeki iki kenar = 1 mesnet.
        /// Tek kalan kenar (tek çizgi perde) = sıfır kalınlıklı mesnet. Ara boşluk = döşeme/boş.
        /// Kör (0,1)/(2,3) değil — perde+kiriş arası açıklık yutulmasın.
        /// </summary>
        private static List<SupportCluster> PairHitsAsMesnets(
            Transaction tr,
            BlockTableRecord btr,
            List<HitRec> hits,
            Editor ed,
            List<SlabTextRec> slabTexts,
            Point3d origin,
            Vector3d u,
            List<ClosedArea> areas)
        {
            var list = new List<SupportCluster>();
            if (hits == null || hits.Count == 0) return list;

            var used = new bool[hits.Count];
            int i = 0;
            while (i < hits.Count)
            {
                if (used[i]) { i++; continue; }
                HitRec h0 = hits[i];
                int j = FindMesnetPairIndex(hits, i, slabTexts, origin, u, areas);
                if (j > i)
                {
                    HitRec h1 = hits[j];
                    double width = h1.T - h0.T;
                    bool sameEnt = !h0.EntId.IsNull && h0.EntId == h1.EntId;
                    Point3d mid = Mid(h0.Point, h1.Point);
                    string label = FindMesnetLabelNear(tr, btr, mid, 80.0);
                    if (!string.IsNullOrEmpty(label))
                        ed?.WriteMessage("\n  Mesnet {0}: {1} (w={2:0}{3})",
                            list.Count + 1, label, width, sameEnt ? " poligon" : "");
                    list.Add(new SupportCluster
                    {
                        TMin = h0.T,
                        TMax = h1.T,
                        FaceMin = h0.Point,
                        FaceMax = h1.Point,
                        MesnetLabel = label
                    });
                    for (int k = i; k <= j; k++) used[k] = true;
                    i = j + 1;
                    continue;
                }

                string lab1 = FindMesnetLabelNear(tr, btr, h0.Point, 80.0);
                if (!string.IsNullOrEmpty(lab1))
                    ed?.WriteMessage("\n  Mesnet {0}: {1} (tek kenar {2})",
                        list.Count + 1, lab1, ShortLayer(h0.Layer));
                list.Add(new SupportCluster
                {
                    TMin = h0.T,
                    TMax = h0.T,
                    FaceMin = h0.Point,
                    FaceMax = h0.Point,
                    MesnetLabel = lab1
                });
                used[i] = true;
                i++;
            }
            return list;
        }

        private static string ShortLayer(string lyr)
        {
            if (string.IsNullOrEmpty(lyr)) return "";
            int p = lyr.IndexOf(" (", StringComparison.Ordinal);
            return p > 0 ? lyr.Substring(0, p) : lyr;
        }

        /// <summary>Aynı poligon veya aynı aile (iki kiriş yüzü). PERDE↔KIRIS eşleşmez.</summary>
        private static int FindMesnetPairIndex(
            List<HitRec> hits, int i, List<SlabTextRec> slabTexts, Point3d origin, Vector3d u,
            List<ClosedArea> areas)
        {
            HitRec h0 = hits[i];
            int bestLayer = -1;
            for (int j = i + 1; j < hits.Count; j++)
            {
                double width = hits[j].T - h0.T;
                if (width > MaxMesnetBodyWidthCm) break;
                if (!CanPairMesnetHits(h0, hits[j])) continue;
                if (width > MaxMesnetWidthCm)
                {
                    Point3d midW = Mid(h0.Point, hits[j].Point);
                    if (!PointInsideMesnetBody(areas, midW)) continue;
                }
                // Küçük konsol: iki kiriş çizgisi arası, sanal hatta yakın DOSEME ISMI
                if (width > 22.0 && HasSlabLabelBetweenHits(h0, hits[j], slabTexts, origin, u))
                    continue;
                bool sameEnt = !h0.EntId.IsNull && h0.EntId == hits[j].EntId;
                if (sameEnt) return j;
                if (bestLayer < 0) bestLayer = j;
            }
            return bestLayer;
        }

        private static bool HasSlabLabelBetweenHits(
            HitRec h0, HitRec h1, List<SlabTextRec> texts, Point3d origin, Vector3d u)
        {
            if (texts == null || texts.Count == 0) return false;
            double t0 = h0.T, t1 = h1.T;
            Vector3d d = h1.Point - h0.Point;
            double len = d.Length;
            if (len < 1.0) return false;
            d = d / len;
            foreach (var st in texts)
            {
                double t = (st.Insert - origin).DotProduct(u);
                if (t <= t0 + 6.0 || t >= t1 - 6.0) continue;
                double along = (st.Insert - h0.Point).DotProduct(d);
                if (along < 4.0 || along > len - 4.0) continue;
                double perp = (st.Insert - (h0.Point + d * along)).Length;
                // Yalnız sanal hatta yakın etiket (yan döşeme yazısı kolon yüzünü ayırmasın)
                if (perp <= 30.0) return true;
            }
            return false;
        }

        private static bool CanPairMesnetHits(HitRec a, HitRec b)
        {
            if (!a.EntId.IsNull && !b.EntId.IsNull && a.EntId == b.EntId)
                return true;
            return SameMesnetFamily(a.Layer, b.Layer);
        }

        private static bool SameMesnetFamily(string la, string lb)
        {
            int fa = MesnetFamily(la), fb = MesnetFamily(lb);
            return fa != 0 && fa == fb;
        }

        /// <summary>1=kiriş, 2=perde, 3=kolon. Farklı aile birleşmez.</summary>
        private static int MesnetFamily(string lyr)
        {
            if (string.IsNullOrEmpty(lyr)) return 0;
            if (lyr.IndexOf("KAPAMA PERDESI", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (lyr.IndexOf("PERDE", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (lyr.IndexOf("KIRIS", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            if (lyr.IndexOf("KOLON", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            return 0;
        }

        /// <summary>Mesnet ortasına yakın KIRIS/PERDE/KOLON ISMI.</summary>
        private static string FindMesnetLabelNear(
            Transaction tr,
            BlockTableRecord btr,
            Point3d mid,
            double radiusCm)
        {
            string best = null;
            double bestDist = radiusCm;
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                if (!(ent is DBText || ent is MText)) continue;
                string lyr = ent.Layer ?? "";
                bool ok =
                    string.Equals(lyr, LayerKirisIsmi, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(lyr, LayerPerdeIsmi, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(lyr, LayerKolonIsmi, StringComparison.OrdinalIgnoreCase) ||
                    lyr.IndexOf("KIRIS ISMI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    lyr.IndexOf("PERDE ISMI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    lyr.IndexOf("KOLON ISMI", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!ok) continue;

                Point3d pos;
                string txt;
                if (ent is DBText db)
                {
                    pos = db.Position;
                    txt = db.TextString ?? "";
                }
                else
                {
                    var mt = (MText)ent;
                    pos = mt.Location;
                    txt = mt.Contents ?? "";
                }
                if (string.IsNullOrWhiteSpace(txt)) continue;
                double d = mid.DistanceTo(pos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = txt.Replace("\\P", " ").Trim();
                }
            }
            return best;
        }

        private struct ClosedArea
        {
            public Point2d[] Ring;
            public double Area;
            public bool IsBosluk;       // KALIP BOSLUK / MERDIVEN
            public bool IsDosemeSiniri; // DOSEME SINIRI
            public bool IsMesnetBody;   // kapalı KIRIS/KOLON/PERDE (kolon kutusu vb.)
        }

        private struct SlabTextRec
        {
            /// <summary>Plan_ID DBText.Position = (leftX, mainY).</summary>
            public Point3d Insert;
            /// <summary>Yazı kutusu (GeometricExtents) — hücre içi test.</summary>
            public Point3d Pos;
            public double MinX, MaxX, MinY, MaxY;
            public string Text;
            /// <summary>DBText/MText dönüşü (rad). Çevrilmiş etikette kot bu eksendedir.</summary>
            public double Rotation;
        }

        /// <summary>
        /// DOSEME SINIRI / KALIP BOSLUK / MERDIVEN / kapalı KIRIS-KOLON-PERDE halkaları.
        /// </summary>
        private static List<ClosedArea> CollectClosedAreas(Transaction tr, BlockTableRecord btr)
        {
            var list = new List<ClosedArea>();
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || !ent.Visible) continue;
                string lyr = ent.Layer ?? "";
                bool bos = IsBoslukLayer(lyr);
                bool dosSin = IsDosemeSiniriLayer(lyr);
                bool mesnet = IsMesnetLayer(lyr, null);
                if (!bos && !dosSin && !mesnet) continue;

                if (!TryGetClosedRing(ent, out Point2d[] ring, out double area))
                    continue;
                bool isPerde = string.Equals(lyr, LayerPerde, StringComparison.OrdinalIgnoreCase)
                    || lyr.IndexOf("KAPAMA PERDESI", StringComparison.OrdinalIgnoreCase) >= 0
                    || (lyr.IndexOf("PERDE", StringComparison.OrdinalIgnoreCase) >= 0
                        && lyr.IndexOf("ISMI", StringComparison.OrdinalIgnoreCase) < 0);
                // Uzun kiriş halkası mesnet gövdesi değil; perde duvarı her boyutta katı
                if (mesnet && !bos && !dosSin && area > 25000.0 && !isPerde)
                    continue;

                list.Add(new ClosedArea
                {
                    Ring = ring,
                    Area = area,
                    IsBosluk = bos,
                    IsDosemeSiniri = dosSin,
                    IsMesnetBody = mesnet && !bos && !dosSin
                });
            }
            // Küçük alan önce (iç içe: en dar hücre)
            list.Sort((a, b) => a.Area.CompareTo(b.Area));
            return list;
        }

        private static bool TryGetClosedRing(Entity ent, out Point2d[] ring, out double area)
        {
            ring = null;
            area = 0;
            try
            {
                if (ent is Polyline pl && pl.Closed && pl.NumberOfVertices >= 3)
                {
                    int n = pl.NumberOfVertices;
                    ring = new Point2d[n];
                    for (int i = 0; i < n; i++)
                    {
                        Point2d p = pl.GetPoint2dAt(i);
                        ring[i] = new Point2d(p.X, p.Y);
                    }
                    area = Math.Abs(RingSignedArea(ring));
                    return area > 1.0;
                }
            }
            catch { }
            return false;
        }

        private static double RingSignedArea(Point2d[] ring)
        {
            double a = 0;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                a += (ring[j].X + ring[i].X) * (ring[j].Y - ring[i].Y);
            return a * 0.5;
        }

        private static bool PointInRing(Point2d[] ring, double x, double y)
        {
            if (ring == null || ring.Length < 3) return false;
            bool inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                double xi = ring[i].X, yi = ring[i].Y;
                double xj = ring[j].X, yj = ring[j].Y;
                if (((yi > y) != (yj > y)) &&
                    (x < (xj - xi) * (y - yi) / (yj - yi + 1e-15) + xi))
                    inside = !inside;
            }
            return inside;
        }

        private static ClosedArea? FindSmallestContaining(List<ClosedArea> areas, Point3d p, Func<ClosedArea, bool> pred)
        {
            double x = p.X, y = p.Y;
            ClosedArea? best = null;
            double bestA = double.MaxValue;
            foreach (var a in areas)
            {
                if (!pred(a)) continue;
                if (!PointInRing(a.Ring, x, y)) continue;
                if (a.Area < bestA)
                {
                    bestA = a.Area;
                    best = a;
                }
            }
            return best;
        }

        private static List<SlabTextRec> CollectSlabLabelTexts(Transaction tr, BlockTableRecord btr)
        {
            var list = new List<SlabTextRec>();
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                if (!(ent is DBText || ent is MText)) continue;
                string lyr = ent.Layer ?? "";
                if (!string.Equals(lyr, LayerDosemeIsmi, StringComparison.OrdinalIgnoreCase) &&
                    lyr.IndexOf("DOSEME ISMI", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Point3d insert;
                string txt;
                double rot;
                if (ent is DBText db)
                {
                    // AppendSlabLabel: TextLeft+TextBottom, Position=AlignmentPoint=(leftX,mainY)
                    insert = db.Position;
                    txt = db.TextString ?? "";
                    rot = db.Rotation;
                }
                else
                {
                    var mt = (MText)ent;
                    insert = mt.Location;
                    txt = mt.Contents ?? "";
                    rot = mt.Rotation;
                }
                if (string.IsNullOrWhiteSpace(txt)) continue;
                txt = txt.Replace("\\P", "\n").Replace("{\\(", "").Replace(")", "");
                if (txt.IndexOf("d=", StringComparison.OrdinalIgnoreCase) < 0 &&
                    txt.IndexOf("d =", StringComparison.OrdinalIgnoreCase) < 0 &&
                    !Regex.IsMatch(txt, @"D[A-Za-z]?\d", RegexOptions.IgnoreCase))
                    continue;

                if (!TryGetTextBox(ent, out double minX, out double minY, out double maxX, out double maxY))
                {
                    minX = insert.X;
                    minY = insert.Y;
                    maxX = insert.X + 80;
                    maxY = insert.Y + 12;
                }
                list.Add(new SlabTextRec
                {
                    Insert = new Point3d(insert.X, insert.Y, 0),
                    Pos = new Point3d((minX + maxX) * 0.5, (minY + maxY) * 0.5, 0),
                    MinX = minX,
                    MaxX = maxX,
                    MinY = minY,
                    MaxY = maxY,
                    Text = txt,
                    Rotation = rot
                });
            }
            return list;
        }

        private static bool TryGetTextBox(Entity ent, out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = minY = maxX = maxY = 0;
            try
            {
                Extents3d ext = ent.GeometricExtents;
                minX = ext.MinPoint.X;
                minY = ext.MinPoint.Y;
                maxX = ext.MaxPoint.X;
                maxY = ext.MaxPoint.Y;
                return maxX >= minX && maxY >= minY;
            }
            catch
            {
                try
                {
                    if (ent is DBText db)
                    {
                        Point3d p = db.Position;
                        double h = db.Height > 0 ? db.Height : 10.0;
                        double w = h * Math.Max(1.0, (db.TextString ?? "").Length * 0.5);
                        minX = p.X; minY = p.Y; maxX = p.X + w; maxY = p.Y + h;
                        return true;
                    }
                    if (ent is MText mt)
                    {
                        Point3d p = mt.Location;
                        double h = mt.TextHeight > 0 ? mt.TextHeight : 10.0;
                        minX = p.X; minY = p.Y; maxX = p.X + h * 4; maxY = p.Y + h;
                        return true;
                    }
                }
                catch { }
                return false;
            }
        }

        private struct BoundaryRec
        {
            public Curve Curve;
            public double MinX, MinY, MaxX, MaxY;
        }

        /// <summary>Sınır çizgileri + kaba ızgara indeksi (segment sorgusu hızlı).</summary>
        private sealed class BoundaryIndex
        {
            public const double BucketCm = 200.0;
            public readonly List<BoundaryRec> Items = new List<BoundaryRec>();
            public readonly Dictionary<long, List<int>> Buckets = new Dictionary<long, List<int>>();
            public double MinX = double.MaxValue, MinY = double.MaxValue;
            public double MaxX = double.MinValue, MaxY = double.MinValue;
            public int[] Stamp = new int[0];
            public int StampGen;

            public static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;

            public void Add(BoundaryRec r)
            {
                int idx = Items.Count;
                Items.Add(r);
                if (r.MinX < MinX) MinX = r.MinX;
                if (r.MinY < MinY) MinY = r.MinY;
                if (r.MaxX > MaxX) MaxX = r.MaxX;
                if (r.MaxY > MaxY) MaxY = r.MaxY;
                int i0 = (int)Math.Floor(r.MinX / BucketCm), i1 = (int)Math.Floor(r.MaxX / BucketCm);
                int j0 = (int)Math.Floor(r.MinY / BucketCm), j1 = (int)Math.Floor(r.MaxY / BucketCm);
                for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    long k = Key(i, j);
                    if (!Buckets.TryGetValue(k, out var lst))
                        Buckets[k] = lst = new List<int>();
                    lst.Add(idx);
                }
            }
        }

        /// <summary>Kapalı alan araması: ızgara adımı (cm), yarıçap, en fazla hücre (≈ alan).</summary>
        private const double FloodStepCm = 20.0;
        private const double FloodMaxRadiusCm = 3000.0;
        private const int FloodMaxCells = 25000; // 20×20 cm → ~1000 m²; aşılırsa alan açık

        /// <summary>Döşemeyi sınırlayan çizgiler: KIRIS/KOLON/PERDE/DOSEME SINIRI/KALIP BOSLUK. Merdiven yoksay.</summary>
        private static BoundaryIndex CollectBoundaryCurves(Transaction tr, BlockTableRecord btr)
        {
            var index = new BoundaryIndex();
            foreach (ObjectId id in btr)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Curve c)) continue;
                if (!c.Visible) continue;
                string lyr = c.Layer ?? "";
                // Merdiven kenarı varsayılan yoksay; çizgiselde boşluk listesindeyse sınır say.
                if (IsMerdivenLayer(lyr) && !(CizgiselMod && IsBoslukLayer(lyr))) continue;
                bool ok = IsMesnetLayer(lyr, null)
                    || IsDosemeSiniriLayer(lyr)
                    || IsKalipBoslukLayer(lyr);
                if (!ok) continue;
                try
                {
                    if (!TryCurveAabb(c, out double minX, out double minY, out double maxX, out double maxY))
                        continue;
                    index.Add(new BoundaryRec
                    {
                        Curve = c,
                        MinX = minX,
                        MinY = minY,
                        MaxX = maxX,
                        MaxY = maxY
                    });
                }
                catch { }
            }
            index.Stamp = new int[index.Items.Count];
            return index;
        }

        private static bool TryCurveAabb(Curve c, out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = minY = double.MaxValue;
            maxX = maxY = double.MinValue;
            try
            {
                if (c is Line ln)
                {
                    minX = Math.Min(ln.StartPoint.X, ln.EndPoint.X);
                    maxX = Math.Max(ln.StartPoint.X, ln.EndPoint.X);
                    minY = Math.Min(ln.StartPoint.Y, ln.EndPoint.Y);
                    maxY = Math.Max(ln.StartPoint.Y, ln.EndPoint.Y);
                    return true;
                }
                if (c is Polyline pl)
                {
                    int n = pl.NumberOfVertices;
                    if (n < 1) return false;
                    for (int i = 0; i < n; i++)
                    {
                        Point2d p = pl.GetPoint2dAt(i);
                        if (p.X < minX) minX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y > maxY) maxY = p.Y;
                    }
                    return minX <= maxX;
                }
                if (c is Circle cir)
                {
                    minX = cir.Center.X - cir.Radius;
                    minY = cir.Center.Y - cir.Radius;
                    maxX = cir.Center.X + cir.Radius;
                    maxY = cir.Center.Y + cir.Radius;
                    return true;
                }
                if (c is Arc arc)
                {
                    minX = arc.Center.X - arc.Radius;
                    minY = arc.Center.Y - arc.Radius;
                    maxX = arc.Center.X + arc.Radius;
                    maxY = arc.Center.Y + arc.Radius;
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// a→b doğru parçası sınır kesiyor mu.
        /// Kapalı PERDE poligonunda entity.IntersectWith sıklıkla boş döner; her kenar ayrı Line.
        /// </summary>
        private static bool SegmentCrossesBoundary(
            BoundaryIndex bounds, Point3d a, Point3d b, double endTolCm = 0.5,
            bool ignoreDosemeSiniri = false)
        {
            if (a.DistanceTo(b) < 1e-6) return false;
            double x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X);
            double y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
            int i0 = (int)Math.Floor(x0 / BoundaryIndex.BucketCm), i1 = (int)Math.Floor(x1 / BoundaryIndex.BucketCm);
            int j0 = (int)Math.Floor(y0 / BoundaryIndex.BucketCm), j1 = (int)Math.Floor(y1 / BoundaryIndex.BucketCm);
            int gen = ++bounds.StampGen;
            var probeSeg = MakeProbeSeg(a, b);
            for (int i = i0; i <= i1; i++)
            for (int j = j0; j <= j1; j++)
            {
                if (!bounds.Buckets.TryGetValue(BoundaryIndex.Key(i, j), out var lst)) continue;
                foreach (int idx in lst)
                {
                    if (bounds.Stamp[idx] == gen) continue;
                    bounds.Stamp[idx] = gen;
                    var bd = bounds.Items[idx];
                    if (bd.MaxX < x0 || bd.MinX > x1 || bd.MaxY < y0 || bd.MinY > y1) continue;
                    if (ignoreDosemeSiniri)
                    {
                        string ly = "";
                        try { ly = bd.Curve.Layer ?? ""; } catch { }
                        if (IsDosemeSiniriLayer(ly))
                            continue;
                    }
                    if (CurveHitsProbe(bd.Curve, probeSeg, a, b, endTolCm))
                        return true;
                }
            }
            return false;
        }

        private static bool CurveHitsProbe(Curve c, LineSegment2d probeSeg, Point3d a, Point3d b, double endTolCm)
        {
            var hits = new List<Point3d>();
            CollectCurveHitPoints(c, probeSeg, hits);
            foreach (Point3d p in hits)
            {
                var f = new Point3d(p.X, p.Y, 0);
                if (f.DistanceTo(a) > endTolCm && f.DistanceTo(b) > endTolCm)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Başlangıç noktasının bulunduğu alanı ızgara üzerinde sınır kesmeden doldurur (her şekil: L, U, yamuk, eğri).
        /// Alan AÇIKSA (bina dışına / yarıçap sınırına taşıyor, çok büyük) → false: döşeme değildir.
        /// Kapalıysa → true; labelIdx = alan içinde yol boyunca ilk ulaşılan etiket (yoksa −1).
        /// clipAlong + clipPerpCm: mesnet–mesnet koridoru — yan açıklığa/dışarı sızmayı duvar say;
        /// çoklu açıklıkta yalnız sol hücrenin kapalı kalması durumunu önler.
        /// </summary>
        private static bool FloodClosedRegion(
            BoundaryIndex bounds, Point3d start, List<SlabTextRec> labels, out int labelIdx,
            List<int> collectAll = null, List<ClosedArea> solids = null,
            Point3d? clipLeft = null, Point3d? clipRight = null, Vector3d? clipAlong = null,
            double clipPerpCm = 0)
        {
            labelIdx = -1;
            double step = FloodStepCm;
            long Key(int i, int j) => BoundaryIndex.Key(i, j);
            bool InSolid(Point3d p) =>
                solids != null && FindSmallestContaining(solids, p, a => a.IsMesnetBody || a.IsBosluk).HasValue;

            bool useClip = clipLeft.HasValue && clipRight.HasValue && clipAlong.HasValue
                && clipAlong.Value.LengthSqrd > 1e-12 && clipPerpCm > step;
            Vector3d axis = useClip ? clipAlong.Value.GetNormal() : Vector3d.XAxis;
            Vector3d perp = useClip ? new Vector3d(-axis.Y, axis.X, 0) : Vector3d.YAxis;
            if (useClip && perp.LengthSqrd > 1e-12) perp = perp.GetNormal();
            Point3d clipA = useClip ? clipLeft.Value : default;
            Point3d clipB = useClip ? clipRight.Value : default;
            double t0 = 0, t1 = 0;
            if (useClip)
            {
                t0 = 0;
                t1 = (clipB - clipA).DotProduct(axis);
                if (t1 < t0) { double tmp = t0; t0 = t1; t1 = tmp; }
                // Mesnet yüzüne yapışmayı önle
                t0 += step * 0.5;
                t1 -= step * 0.5;
                if (t1 <= t0) return false;
            }

            bool InsideClip(Point3d p)
            {
                if (!useClip) return true;
                double t = (p - clipA).DotProduct(axis);
                if (t < t0 || t > t1) return false;
                double q = Math.Abs((p - clipA).DotProduct(perp));
                return q <= clipPerpCm;
            }

            if (InSolid(start) || (useClip && !InsideClip(start)))
            {
                Point3d nudged = start;
                bool found = false;
                double[] tryOff = { step, step * 2, step * 3, step * 4 };
                // Koridorda önce eksene dik (açıklık içi), sonra eksen boyunca dene
                int[] sdi = useClip
                    ? new[] { 0, 0, 1, -1, 1, -1, 1, -1 }
                    : new[] { 1, -1, 0, 0 };
                int[] sdj = useClip
                    ? new[] { 1, -1, 0, 0, 1, 1, -1, -1 }
                    : new[] { 0, 0, 1, -1 };
                int nDir = useClip ? 8 : 4;
                foreach (double off in tryOff)
                {
                    for (int d = 0; d < nDir && !found; d++)
                    {
                        Point3d q;
                        if (useClip)
                            q = start + axis * (sdi[d] * off) + perp * (sdj[d] * off);
                        else
                            q = new Point3d(start.X + sdi[d] * off, start.Y + sdj[d] * off, 0);
                        if (InSolid(q)) continue;
                        if (useClip && !InsideClip(q)) continue;
                        nudged = q;
                        found = true;
                    }
                    if (found) break;
                }
                if (!found) return false;
                start = nudged;
            }

            var labelBuckets = new Dictionary<long, List<int>>();
            for (int li = 0; li < labels.Count; li++)
            {
                Point3d p = labels[li].Insert;
                if (useClip && !InsideClip(p)
                    && Math.Abs((p - clipA).DotProduct(perp)) > clipPerpCm + 80.0)
                    continue;
                int bi = (int)Math.Round((p.X - start.X) / step);
                int bj = (int)Math.Round((p.Y - start.Y) / step);
                long k = Key(bi, bj);
                if (!labelBuckets.TryGetValue(k, out var lst))
                    labelBuckets[k] = lst = new List<int>();
                lst.Add(li);
            }

            Point3d Center(int i, int j) => new Point3d(start.X + i * step, start.Y + j * step, 0);
            int maxIdx = (int)(FloodMaxRadiusCm / step);
            if (useClip)
            {
                // Koridor kutusu: eksen boyu + dik pad
                double spanCm = Math.Max(t1 - t0, step);
                maxIdx = Math.Max(maxIdx, (int)((spanCm + 2 * clipPerpCm) / step) + 4);
            }
            // Bina dış sınırı (tüm sınır çizgilerinin kutusu) — dışına çıkan alan açıktır
            double bxMin = bounds.MinX - step, bxMax = bounds.MaxX + step;
            double byMin = bounds.MinY - step, byMax = bounds.MaxY + step;

            // Etiket: DOSEME SINIRI kesilmeden. Yalnız mid sınır poligonunun İÇİNDEYSE
            // (küçük konsol, etiket dışarıda) sinırı yok say — boş alan üstteki etiketi çalamaz.
            bool softSiniri = solids != null && PointInsideDosemeSiniri(solids, start);

            var visited = new HashSet<long> { Key(0, 0) };
            var queue = new Queue<(int i, int j)>();
            queue.Enqueue((0, 0));
            int[] di = { 1, -1, 0, 0 };
            int[] dj = { 0, 0, 1, -1 };
            int cellCap = useClip
                ? Math.Max(FloodMaxCells, (int)((t1 - t0) / step + 2) * (int)(2 * clipPerpCm / step + 2) + 100)
                : FloodMaxCells;

            while (queue.Count > 0)
            {
                if (visited.Count > cellCap) return useClip && labelIdx >= 0;
                var (ci, cj) = queue.Dequeue();
                Point3d c = Center(ci, cj);
                if (c.X < bxMin || c.X > bxMax || c.Y < byMin || c.Y > byMax)
                {
                    // Koridorda dış kutu = duvar (sızma sayma); globalsızda açık alan
                    if (useClip) continue;
                    return false;
                }

                if (labelIdx < 0 || collectAll != null)
                {
                    for (int oi = -1; oi <= 1; oi++)
                    for (int oj = -1; oj <= 1; oj++)
                    {
                        if (!labelBuckets.TryGetValue(Key(ci + oi, cj + oj), out var lst)) continue;
                        foreach (int li in lst)
                        {
                            if (SegmentCrossesBoundary(bounds, c, labels[li].Insert, 0.5, softSiniri))
                                continue;
                            if (labelIdx < 0) labelIdx = li;
                            if (collectAll != null && !collectAll.Contains(li))
                                collectAll.Add(li);
                            if (collectAll == null) break;
                        }
                        if (collectAll == null && labelIdx >= 0) break;
                    }
                }

                for (int d = 0; d < 4; d++)
                {
                    int ni = ci + di[d], nj = cj + dj[d];
                    if (Math.Abs(ni) > maxIdx || Math.Abs(nj) > maxIdx)
                    {
                        if (useClip) continue;
                        return false;
                    }
                    long nk = Key(ni, nj);
                    if (visited.Contains(nk)) continue;
                    Point3d np = Center(ni, nj);
                    if (useClip && !InsideClip(np)) continue;
                    if (InSolid(np)) continue;
                    if (SegmentCrossesBoundary(bounds, c, np, 0.05)) continue;
                    visited.Add(nk);
                    queue.Enqueue((ni, nj));
                }
            }
            // Koridor: keşif bitti → kapalı say (yan mesnetler zaten clip duvarı); etiket ayrıca aranır
            return true;
        }

        /// <summary>Açıklık ortası kiriş gövdesindeyse örnek noktalarla dışarı kaydır.</summary>
        private static bool TryNudgeMidOutOfMesnet(
            Point3d leftInner, Point3d rightInner, Vector3d u, Vector3d n,
            List<ClosedArea> areas, out Point3d mid)
        {
            Vector3d along = rightInner - leftInner;
            double len = along.Length;
            mid = Mid(leftInner, rightInner);
            if (len < 1e-6) return false;
            along = along / len;
            Vector3d nn = n.GetNormal();
            double[] ts = { 0.35, 0.5, 0.65, 0.25, 0.75, 0.2, 0.8 };
            double[] offs = { 0, 25, -25, 50, -50, 80, -80 };
            foreach (double t in ts)
            {
                Point3d baseP = leftInner + along * (len * t);
                foreach (double o in offs)
                {
                    Point3d q = baseP + nn * o;
                    if (PointInsideBosluk(areas, q) || PointInsideMesnetBody(areas, q))
                        continue;
                    mid = q;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Açıklık (2.↔3. çizgi): KIRIS/KOLON/PERDE/DOSEME SINIRI/KALIP BOSLUK/MERDIVEN ile sınırlı KAPALI alan.
        /// Alan açıksa (bina dışı) → döşeme değil. Etiket = alan içinde sınır kesmeden ulaşılan DOSEME ISMI.
        /// Etiket yoksa → boş (donatı yok).
        /// </summary>
        private static bool TryClassifyGapAsDoseme(
            BoundaryIndex bounds,
            Point3d leftInner,
            Point3d rightInner,
            Vector3d u,
            Vector3d n,
            List<ClosedArea> areas,
            List<SlabTextRec> slabTexts,
            List<GprSteelRec> gprNotes,
            out SlabInfo slab)
        {
            slab = new SlabInfo { CapMm = 8, AralikCm = 20, HasLabel = false };
            Vector3d along = rightInner - leftInner;
            double len = along.Length;
            if (len < 1e-6) return false;
            along = along / len;
            Vector3d nn = n.GetNormal();
            Point3d mid = Mid(leftInner, rightInner);
            if (PointInsideMesnetBody(areas, mid))
                TryNudgeMidOutOfMesnet(leftInner, rightInner, u, n, areas, out mid);

            var samples = new List<Point3d>(10) { mid };
            double[] ts = { 0.08, 0.5, 0.92 };
            double[] offs = { 0, 15.0, -15.0 };
            foreach (double t in ts)
            {
                Point3d baseP = leftInner + along * (len * t);
                foreach (double o in offs)
                    samples.Add(baseP + nn * o);
            }

            int bosVotes = 0, dosVotes = 0;
            foreach (Point3d s in samples)
            {
                Point3d pt = new Point3d(s.X, s.Y, 0);
                if (FindSmallestContaining(areas, pt, a => a.IsMesnetBody).HasValue)
                    continue;
                if (FindSmallestContaining(areas, pt, a => a.IsBosluk).HasValue)
                    bosVotes++;
                else
                    dosVotes++;
            }

            if (bosVotes > dosVotes && bosVotes > 0)
                return false;

            var candidates = new List<SlabTextRec>();
            foreach (var st in slabTexts)
            {
                if (st.Insert.DistanceTo(mid) > FloodMaxRadiusCm) continue;
                if (FindSmallestContaining(areas, st.Insert, a => a.IsBosluk).HasValue) continue;
                candidates.Add(st);
            }

            if (candidates.Count == 0)
                return false;

            // Önce koridor (çapraz kiriş flood'u engelleyebilir); sonra flood
            double clipPerp = Math.Max(400.0, Math.Min(1200.0, len * 0.85 + 200.0));
            SlabTextRec label;
            if (TryPickSlabLabelInCorridor(
                    leftInner, rightInner, along, nn, candidates, bounds, areas, out label))
            { /* açıklık içi DOSEME ISMI */ }
            else
            {
                bool flooded = FloodClosedRegion(
                    bounds, mid, candidates, out int li, null, areas,
                    leftInner, rightInner, along, clipPerp);
                if (flooded && li >= 0)
                    label = candidates[li];
                else
                    return false;
            }

            FillSlabInfoFromBlob(ref slab, label.Text);
            AttachUstKotFromPlanLabel(label, null, ref slab);
            AttachGprSteelInRegion(bounds, mid, gprNotes, areas, leftInner, rightInner, along, nn,
                slabTexts, slab.Name, ref slab);
            slab.HasLabel = true;
            return true;
        }

        /// <summary>
        /// Sınır/boşluk kenar açıklığı: döşeme varsa alt düz.
        /// Etiket koridorda bulunursa her boyutta kabul; bulunamazsa ≤1.5 m soft (küçük konsol).
        /// KALIP BOSLUK / MERDIVEN içi asla.
        /// </summary>
        private static bool TryAcceptEdgeSiniriSlab(
            BoundaryIndex bounds,
            Point3d leftInner,
            Point3d rightInner,
            Vector3d u,
            Vector3d n,
            List<ClosedArea> areas,
            List<SlabTextRec> slabTexts,
            List<GprSteelRec> gprNotes,
            double gapLen,
            bool edgeSpan,
            bool touchesSiniri,
            out SlabInfo slab)
        {
            slab = new SlabInfo { CapMm = 8, AralikCm = 20, HasLabel = false };
            if (!edgeSpan) return false;
            Point3d mid = Mid(leftInner, rightInner);
            if (PointInsideBosluk(areas, mid) || PointInsideMesnetBody(areas, mid))
                return false;

            bool insideSiniri = PointInsideDosemeSiniri(areas, mid);
            bool smallKonsol = touchesSiniri && gapLen <= SiniriKonsolMaxCm;
            if (!insideSiniri && !smallKonsol) return false;

            Vector3d along = rightInner - leftInner;
            if (along.Length < 1e-6) return false;
            along = along / along.Length;
            var cand = slabTexts ?? new List<SlabTextRec>();
            if (TryPickSlabLabelInCorridor(leftInner, rightInner, along, n.GetNormal(), cand, bounds, areas, out SlabTextRec lab))
            {
                FillSlabInfoFromBlob(ref slab, lab.Text);
                AttachUstKotFromPlanLabel(lab, null, ref slab);
                AttachGprSteelInRegion(bounds, mid, gprNotes, areas, leftInner, rightInner, along, n.GetNormal(),
                    slabTexts, slab.Name, ref slab);
            }
            slab.HasLabel = true;
            return true;
        }

        /// <summary>
        /// Küçük/dar döşeme: etiket sınır dışında olabilir.
        /// DOSEME SINIRI yalnız mid sınır poligonunun içindeyse yok sayılır (boş alan üst etiketi çalamaz).
        /// Açıklık içi (t∈%15–%85) etiket: çapraz kiriş kesese bile kabul (DB-06 gibi).
        /// </summary>
        private static bool TryPickSlabLabelInCorridor(
            Point3d leftInner,
            Point3d rightInner,
            Vector3d along,
            Vector3d nn,
            List<SlabTextRec> candidates,
            BoundaryIndex bounds,
            List<ClosedArea> areas,
            out SlabTextRec found)
        {
            found = default;
            double len = leftInner.DistanceTo(rightInner);
            if (len < 8.0 || candidates == null || candidates.Count == 0) return false;
            Point3d mid = Mid(leftInner, rightInner);
            bool softSiniri = PointInsideDosemeSiniri(areas, mid);
            double pad = Math.Max(80.0, Math.Min(200.0, len * 0.65));
            // Aynı döşeme: hat GPR yığınının biraz yanında da etiketi görsün.
            const double perpMax = 900.0;
            double best = double.MaxValue;
            bool ok = false;
            foreach (var st in candidates)
            {
                Point3d p = st.Insert;
                double t = (p - leftInner).DotProduct(along);
                double perp = Math.Abs((p - leftInner).DotProduct(nn));
                // Yazı kutusu da koridora değiyorsa (etiket sınır dışında)
                bool boxNear = st.MaxX >= Math.Min(leftInner.X, rightInner.X) - pad
                    && st.MinX <= Math.Max(leftInner.X, rightInner.X) + pad
                    && st.MaxY >= Math.Min(leftInner.Y, rightInner.Y) - pad
                    && st.MinY <= Math.Max(leftInner.Y, rightInner.Y) + pad;
                if ((t < -pad || t > len + pad || perp > perpMax) && !boxNear)
                    continue;

                bool clearlyInSpan = t >= len * 0.12 && t <= len * 0.88 && perp <= pad + 40.0;
                if (!clearlyInSpan)
                {
                    if (SegmentCrossesBoundary(bounds, mid, p, 0.5, softSiniri)
                        && SegmentCrossesBoundary(bounds, mid, st.Pos, 0.5, softSiniri))
                        continue;
                }

                double d = p.DistanceTo(mid) + perp * 0.25;
                if (d < best)
                {
                    best = d;
                    found = st;
                    ok = true;
                }
            }
            return ok;
        }

        /// <summary>X kırmızı, Y mavi üst/alt çizgi. Yazı türü bu çifte göre.</summary>
        private static void CollectKirmiziGprCizgileri(Transaction tr, BlockTableRecord btr)
        {
            _kirmiziGprCizgi = null;
            if (EtkinYon == DonatiYonu.Serbest) return;
            bool mavi = EtkinYon == DonatiYonu.Y;
            var list = new List<KirmiziGprCizgi>();
            CollectGuideColor(tr, btr, list, mavi, active: true);
            CollectGuideColor(tr, btr, list, !mavi, active: false);
            _kirmiziGprCizgi = list;
        }

        private static void CollectGuideColor(
            Transaction tr, BlockTableRecord btr, List<KirmiziGprCizgi> list, bool mavi, bool active)
        {
            string ustKey = mavi ? "GPR MAVI UST" : "GPR KIRMIZI UST";
            string altKey = mavi ? "GPR MAVI ALT" : "GPR KIRMIZI ALT";
            string ustKeyTr = mavi ? "GPR MAVİ UST" : ustKey;
            string altKeyTr = mavi ? "GPR MAVİ ALT" : altKey;
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || !ent.Visible) continue;
                string lyr = ent.Layer ?? "";
                bool ust = lyr.IndexOf(ustKey, StringComparison.OrdinalIgnoreCase) >= 0
                    || lyr.IndexOf(ustKeyTr, StringComparison.OrdinalIgnoreCase) >= 0;
                bool alt = lyr.IndexOf(altKey, StringComparison.OrdinalIgnoreCase) >= 0
                    || lyr.IndexOf(altKeyTr, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!ust && !alt) continue;
                if (ent is Line ln)
                    AddKirmiziCizgi(list, ln.StartPoint, ln.EndPoint, ust, id, active);
                else if (ent is Polyline pl)
                {
                    int n = pl.NumberOfVertices;
                    int segs = pl.Closed ? n : n - 1;
                    for (int i = 0; i < segs; i++)
                        AddKirmiziCizgi(list, pl.GetPoint3dAt(i), pl.GetPoint3dAt((i + 1) % n), ust, id, active);
                }
            }
        }

        private static void AddKirmiziCizgi(
            List<KirmiziGprCizgi> list, Point3d a, Point3d b, bool ust, ObjectId id, bool active)
        {
            if (a.DistanceTo(b) < 8.0) return;
            list.Add(new KirmiziGprCizgi
            {
                A = new Point3d(a.X, a.Y, 0),
                B = new Point3d(b.X, b.Y, 0),
                Ust = ust,
                Id = id,
                Active = active
            });
        }

        /// <summary>
        /// Poligon içi: çift çizginin altı düz, arası pilye, üstü montaj.
        /// Üstteki yazılardan her uca en yakın olan ilavedir.
        /// X’te yukarı ekran +Y, Y’de bakış solu. Katman tersse çizgiler yer değiştirir.
        /// </summary>
        private static void AssignXGprFromKirmiziCizgi(
            ref SpanRec span, List<GprSteelRec> notes, Vector3d u, Polyline opening)
        {
            if (EtkinYon == DonatiYonu.Serbest) return;
            if (!TryZoneGpr(span, notes, u, opening, out List<GprSteelRec> zoned))
            {
                SlabInfo empty = span.Slab;
                empty.GprSteels = new List<GprSteelRec>();
                span.Slab = empty;
                return;
            }
            SlabInfo info = span.Slab;
            info.GprSteels = zoned;
            span.Slab = info;
        }

        private static bool TryZoneGpr(
            SpanRec span, List<GprSteelRec> notes, Vector3d u, Polyline opening,
            out List<GprSteelRec> zoned)
        {
            zoned = null;
            if (notes == null || opening == null || _kirmiziGprCizgi == null) return false;

            var inside = new List<GprSteelRec>();
            for (int i = 0; i < notes.Count; i++)
            {
                if (PolyContains(opening, notes[i].Pos))
                    inside.Add(notes[i]);
            }
            if (inside.Count == 0) return false;

            KirmiziGprCizgi ust = default, alt = default;
            double bestU = 0, bestA = 0;
            foreach (KirmiziGprCizgi c in _kirmiziGprCizgi)
            {
                if (!c.Active) continue;
                double chord = ChordInsidePolyline(c.A, c.B, opening);
                if (chord <= 0) continue;
                if (c.Ust && chord > bestU) { bestU = chord; ust = c; }
                if (!c.Ust && chord > bestA) { bestA = chord; alt = c; }
            }
            if (bestU <= 0 || bestA <= 0) return false;

            Vector3d up = EtkinYon == DonatiYonu.Y ? BarUpNormal(u) : Vector3d.YAxis;
            Point3d ma = Mid(alt.A, alt.B);
            Point3d mu = Mid(ust.A, ust.B);
            if ((mu - ma).DotProduct(up) < 0)
            {
                Point3d tmp = mu;
                mu = ma;
                ma = tmp;
                KirmiziGprCizgi swap = ust;
                ust = alt;
                alt = swap;
            }
            double yUst = (mu - ma).DotProduct(up);
            if (yUst <= 1e-6) return false;

            for (int i = 0; i < inside.Count; i++)
            {
                GprSteelRec g = inside[i];
                double y = (g.Pos - ma).DotProduct(up);
                if (y < 0)
                    g.Kind = GprSteelKind.Duz;
                else if (y <= yUst)
                    g.Kind = GprSteelKind.Pilye;
                else
                    g.Kind = GprSteelKind.Montaj;
                inside[i] = g;
            }

            // İlave: üst çizgi × poligon kenarı kesişiminde, üst çizginin üstündeki (çizgi üstü dahil) yazı.
            // Kesişim bölgesinde yazı yoksa başka yerden çalma.
            if (!TryGuidePolyEndHits(ust, opening, span.LeftInner, span.RightInner,
                    out Point3d hitL, out Point3d hitR))
            {
                zoned = inside;
                return true;
            }

            int NoteIndexOf(ObjectId id)
            {
                for (int k = 0; k < inside.Count; k++)
                    if (inside[k].Id == id) return k;
                return -1;
            }

            // Bölge: iki kesişim arasının yarısı. Orta montaj yazısı uç ilavesi olmasın.
            double half = hitL.DistanceTo(hitR) * 0.5;
            if (half < 1.0) half = Math.Max(span.L * 0.5, 1.0);
            Point3d midSpan = Mid(span.LeftInner, span.RightInner);
            Vector3d along = span.RightInner - span.LeftInner;
            double spanLen = along.Length;
            Vector3d ax = spanLen > 1e-9 ? along / spanLen : u;

            int PickIlaveAtHit(Point3d hit, Point3d otherHit)
            {
                int best = -1;
                double bestD = double.MaxValue;
                for (int n = 0; n < notes.Count; n++)
                {
                    Point3d p = notes[n].Pos;
                    double y = (p - ma).DotProduct(up);
                    // Yazı kutusu üst çizgiye oturabilir; çizgi altı (pilye/düz) alınmaz.
                    if (y < yUst - 3.0) continue;
                    double d = p.DistanceTo(hit);
                    if (d >= p.DistanceTo(otherHit)) continue;
                    if (d > half) continue;
                    // Orta montaj: kesişimden çok açıklık ortasına yakınsa ilave değil.
                    if (d >= p.DistanceTo(midSpan)) continue;
                    if (d < bestD) { bestD = d; best = n; }
                }
                return best;
            }

            int nLeft = PickIlaveAtHit(hitL, hitR);
            int nRight = PickIlaveAtHit(hitR, hitL);
            if (nLeft >= 0 && nRight >= 0 && notes[nLeft].Id == notes[nRight].Id)
            {
                if (notes[nLeft].Pos.DistanceTo(hitL) <= notes[nLeft].Pos.DistanceTo(hitR))
                    nRight = -1;
                else
                    nLeft = -1;
            }

            void ApplyIlave(int noteIdx, bool leftSide)
            {
                if (noteIdx < 0) return;
                GprSteelKind kind = leftSide ? GprSteelKind.SolIlave : GprSteelKind.SagIlave;
                int at = NoteIndexOf(notes[noteIdx].Id);
                if (at >= 0)
                {
                    GprSteelRec g = inside[at];
                    g.Kind = kind;
                    inside[at] = g;
                }
                else
                {
                    GprSteelRec g = notes[noteIdx];
                    g.Kind = kind;
                    inside.Add(g);
                }
            }
            ApplyIlave(nLeft, true);
            ApplyIlave(nRight, false);

            // Uç bandındaki (ilave bölgesi) montaj kalmasın → düz/montaj çalmasın.
            if (spanLen > 1e-6)
            {
                for (int i = 0; i < inside.Count; i++)
                {
                    GprSteelRec g = inside[i];
                    if (g.Kind != GprSteelKind.Montaj
                        && g.Kind != GprSteelKind.Duz
                        && g.Kind != GprSteelKind.Pilye)
                        continue;
                    if (g.Kind == GprSteelKind.SolIlave || g.Kind == GprSteelKind.SagIlave)
                        continue;
                    double t = (g.Pos - span.LeftInner).DotProduct(ax) / spanLen;
                    if (t >= 0.22 && t <= 0.78) continue;
                    // Düz/pilye uçta kalmışsa orta türden çıkar (ilave değilse unused).
                    if (g.Kind == GprSteelKind.Duz || g.Kind == GprSteelKind.Pilye
                        || g.Kind == GprSteelKind.Montaj)
                    {
                        g.Kind = GprSteelKind.Unknown;
                        inside[i] = g;
                    }
                }
            }

            zoned = inside;
            return true;
        }

        /// <summary>
        /// Üst GPR çizgisinin poligon kenarlarıyla kesişimleri; sol/sağ uca en yakın iki nokta.
        /// </summary>
        private static bool TryGuidePolyEndHits(
            KirmiziGprCizgi ust, Polyline pl, Point3d leftInner, Point3d rightInner,
            out Point3d hitL, out Point3d hitR)
        {
            hitL = default;
            hitR = default;
            if (pl == null) return false;
            int n = pl.NumberOfVertices;
            if (n < 2) return false;
            int segs = pl.Closed ? n : n - 1;
            var hits = new List<Point3d>();
            for (int i = 0; i < segs; i++)
            {
                Point3d a = pl.GetPoint3dAt(i);
                Point3d b = pl.GetPoint3dAt((i + 1) % n);
                if (TryLineSegHit(ust.A, ust.B, a, b, out Point3d h))
                    hits.Add(h);
            }
            if (hits.Count == 0) return false;
            int iL = 0, iR = 0;
            double dL = double.MaxValue, dR = double.MaxValue;
            for (int i = 0; i < hits.Count; i++)
            {
                double dl = hits[i].DistanceTo(leftInner);
                double dr = hits[i].DistanceTo(rightInner);
                if (dl < dL) { dL = dl; iL = i; }
                if (dr < dR) { dR = dr; iR = i; }
            }
            if (iL == iR && hits.Count > 1)
            {
                // Tek nokta her iki uca da en yakınsa diğer uca ikinci en yakını ver.
                int i2 = -1;
                double d2 = double.MaxValue;
                Point3d other = dL <= dR ? rightInner : leftInner;
                for (int i = 0; i < hits.Count; i++)
                {
                    if (i == iL) continue;
                    double d = hits[i].DistanceTo(other);
                    if (d < d2) { d2 = d; i2 = i; }
                }
                if (i2 >= 0)
                {
                    if (dL <= dR) iR = i2;
                    else iL = i2;
                }
            }
            hitL = hits[iL];
            hitR = hits[iR];
            return true;
        }

        /// <summary>Sonsuz doğru (la–lb) ile segment (sa–sb) kesişimi.</summary>
        private static bool TryLineSegHit(
            Point3d la, Point3d lb, Point3d sa, Point3d sb, out Point3d hit)
        {
            hit = default;
            double rx = lb.X - la.X, ry = lb.Y - la.Y;
            double sx = sb.X - sa.X, sy = sb.Y - sa.Y;
            double den = rx * sy - ry * sx;
            if (Math.Abs(den) < 1e-8) return false;
            double qx = sa.X - la.X, qy = sa.Y - la.Y;
            double t = (qx * sy - qy * sx) / den;
            double uu = (qx * ry - qy * rx) / den;
            if (uu < -1e-6 || uu > 1.0 + 1e-6) return false;
            hit = new Point3d(la.X + t * rx, la.Y + t * ry, 0);
            return true;
        }

        private static Point3d CloserEnd(KirmiziGprCizgi c, Point3d target)
        {
            return c.A.DistanceTo(target) <= c.B.DistanceTo(target) ? c.A : c.B;
        }

        private static double ChordInsidePolyline(Point3d a, Point3d b, Polyline pl)
        {
            if (pl == null) return 0;
            bool aIn = PolyContains(pl, a);
            bool bIn = PolyContains(pl, b);
            var ts = new List<double>();
            if (aIn) ts.Add(0);
            if (bIn) ts.Add(1);
            int n = pl.NumberOfVertices;
            if (n < 2) return 0;
            int segs = pl.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                Point3d p = pl.GetPoint3dAt(i);
                Point3d q = pl.GetPoint3dAt((i + 1) % n);
                if (TrySegmentT(a, b, p, q, out double t))
                    ts.Add(t);
            }
            if (ts.Count == 0) return 0;
            ts.Sort();
            double lineLen = a.DistanceTo(b);
            double len = 0;
            for (int i = 0; i + 1 < ts.Count; i++)
            {
                double t0 = ts[i], t1 = ts[i + 1];
                if (t1 - t0 < 1e-6) continue;
                Point3d mid = a + (b - a) * ((t0 + t1) * 0.5);
                if (PolyContains(pl, mid))
                    len += (t1 - t0) * lineLen;
            }
            return len;
        }

        private static bool TrySegmentT(Point3d a, Point3d b, Point3d c, Point3d d, out double t)
        {
            t = 0;
            double rx = b.X - a.X, ry = b.Y - a.Y;
            double sx = d.X - c.X, sy = d.Y - c.Y;
            double den = rx * sy - ry * sx;
            if (Math.Abs(den) < 1e-8) return false;
            double qx = c.X - a.X, qy = c.Y - a.Y;
            t = (qx * sy - qy * sx) / den;
            double uu = (qx * ry - qy * rx) / den;
            if (t < -1e-6 || t > 1.0 + 1e-6 || uu < -1e-6 || uu > 1.0 + 1e-6) return false;
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            return true;
        }

        /// <summary>
        /// Yazı bu açıklığın boundary'sindeyse alınır. Kiriş üstündeki uç yazısı
        /// poligonun 120 cm yakınında da ilave sayılır. Boundary yoksa eski yüz.
        /// </summary>
        private static bool NoteInOpening(
            Polyline opening, NetTopologySuite.Geometries.Polygon face,
            Point3d pos, Point3d left, Vector3d ax, double spanLen)
        {
            if (opening != null)
            {
                if (PolyContains(opening, pos)) return true;
                if (spanLen < 1.0) return false;
                double t = (pos - left).DotProduct(ax) / spanLen;
                if (t > 0.22 && t < 0.78) return false;
                return DistToPolyline(opening, pos) <= 120.0;
            }
            return PointOfFace(face, pos, 20.0)
                || GprOnEndMesnet(face, pos, left, ax, spanLen);
        }

        /// <summary>Üst/alt çizgi bu açıklığın üstünden geçiyorsa (orta noktası başka döşemede olsa da).</summary>
        private static bool LineOverOpening(
            KirmiziGprCizgi c, Polyline opening, NetTopologySuite.Geometries.Polygon face,
            Point3d left, Vector3d ax, double spanLen)
        {
            if (OverlapOnSpan(c.A, c.B, left, ax, spanLen) < 30.0) return false;
            if (!SampleOnSpan(c.A, c.B, left, ax, spanLen, out Point3d sample)) return false;
            if (opening != null)
            {
                if (PolyContains(opening, sample)) return true;
                return DistToPolyline(opening, sample) <= 40.0;
            }
            return PointOfFace(face, sample, 30.0);
        }

        private static double OverlapOnSpan(Point3d a, Point3d b, Point3d left, Vector3d ax, double spanLen)
        {
            double t0 = (a - left).DotProduct(ax);
            double t1 = (b - left).DotProduct(ax);
            double lo = Math.Max(0.0, Math.Min(t0, t1));
            double hi = Math.Min(spanLen, Math.Max(t0, t1));
            return hi - lo;
        }

        private static bool SampleOnSpan(
            Point3d a, Point3d b, Point3d left, Vector3d ax, double spanLen, out Point3d sample)
        {
            sample = Mid(a, b);
            double t0 = (a - left).DotProduct(ax);
            double t1 = (b - left).DotProduct(ax);
            double lo = Math.Max(0.0, Math.Min(t0, t1));
            double hi = Math.Min(spanLen, Math.Max(t0, t1));
            if (hi - lo < 1.0) return false;
            double tm = (lo + hi) * 0.5;
            double seg = t1 - t0;
            double f = Math.Abs(seg) < 1e-6 ? 0.5 : (tm - t0) / seg;
            if (f < 0) f = 0;
            else if (f > 1) f = 1;
            sample = a + (b - a) * f;
            return true;
        }

        /// <summary>
        /// Mesnet yazısı kirişin üzerinde, döşeme poligonunun dışında kalabilir.
        /// Yalnız açıklık uçlarına bakılır; ortadaki komşu döşeme yazısı alınmaz.
        /// </summary>
        private static bool GprOnEndMesnet(NetTopologySuite.Geometries.Polygon face, Point3d pos, Point3d left, Vector3d ax, double spanLen)
        {
            if (face == null || spanLen < 1.0) return false;
            double t = (pos - left).DotProduct(ax) / spanLen;
            if (t > 0.22 && t < 0.78) return false;
            try
            {
                var pt = face.Factory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(pos.X, pos.Y));
                return face.Distance(pt) <= 120.0;
            }
            catch { return false; }
        }

        /// <summary>KALIP50/100 2. kopya: X=kırmızı, Y=mavi GPR yazıları (adet ø çap / aralık L=).</summary>
        private static List<GprSteelRec> CollectGprSteelNotes(Transaction tr, BlockTableRecord btr)
        {
            var list = new List<GprSteelRec>();
            if (EtkinYon == DonatiYonu.Serbest) return list;
            bool wantRed = EtkinYon == DonatiYonu.X;
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || !ent.Visible) continue;
                if (!(ent is DBText || ent is MText)) continue;
                string lyr = ent.Layer ?? "";
                bool layerOk = wantRed
                    ? (string.Equals(lyr, LayerGprKirmizi, StringComparison.OrdinalIgnoreCase)
                       || lyr.IndexOf("GPR KIRMIZI", StringComparison.OrdinalIgnoreCase) >= 0)
                    : (string.Equals(lyr, LayerGprMavi, StringComparison.OrdinalIgnoreCase)
                       || lyr.IndexOf("GPR MAVI", StringComparison.OrdinalIgnoreCase) >= 0
                       || lyr.IndexOf("GPR MAVİ", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!layerOk)
                {
                    short aci = 0;
                    try { aci = ent.Color.ColorIndex; } catch { }
                    if (ent.Color.ColorMethod == ColorMethod.ByLayer)
                    {
                        try
                        {
                            var lt = (LayerTable)tr.GetObject(btr.Database.LayerTableId, OpenMode.ForRead);
                            if (lt.Has(lyr))
                                aci = ((LayerTableRecord)tr.GetObject(lt[lyr], OpenMode.ForRead)).Color.ColorIndex;
                        }
                        catch { }
                    }
                    if (wantRed && aci != 1) continue;
                    if (!wantRed && aci != 5) continue;
                    if (lyr.IndexOf("DONATI YAZISI", StringComparison.OrdinalIgnoreCase) < 0 &&
                        lyr.IndexOf("GPR", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                string txt;
                Point3d pos;
                if (ent is DBText db)
                {
                    txt = db.TextString ?? "";
                    pos = db.Position;
                }
                else
                {
                    var mt = (MText)ent;
                    txt = mt.Contents ?? "";
                    pos = mt.Location;
                }
                if (string.IsNullOrWhiteSpace(txt)) continue;
                txt = txt.Replace("\\P", " ").Trim();
                if (!TryParseGprSteel(txt, out int adet, out int cap, out int aralik)) continue;
                if (TryGetTextBox(ent, out double minX, out double minY, out double maxX, out double maxY))
                    pos = new Point3d((minX + maxX) * 0.5, (minY + maxY) * 0.5, 0);
                list.Add(new GprSteelRec
                {
                    Pos = pos,
                    Adet = adet,
                    CapMm = cap,
                    AralikCm = aralik,
                    Text = txt,
                    Kind = ClassifyGprKindFromText(txt),
                    Id = id
                });
            }
            return list;
        }

        private static GprSteelKind ClassifyGprKindFromText(string txt)
        {
            if (string.IsNullOrWhiteSpace(txt)) return GprSteelKind.Unknown;
            string n = txt.Trim().ToLowerInvariant()
                .Replace('ı', 'i').Replace('İ', 'i').Replace('ğ', 'g')
                .Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
            // Parantez içi (KALIP bazen siler; DMX/eski yazıda kalabilir)
            int lp = n.LastIndexOf('(');
            int rp = n.LastIndexOf(')');
            string paren = (lp >= 0 && rp > lp) ? n.Substring(lp + 1, rp - lp - 1) : n;
            if (paren.Contains("sol") && (paren.Contains("ila") || paren == "sol"))
                return GprSteelKind.SolIlave;
            if ((paren.Contains("sag") || paren.Contains("sağ")) && paren.Contains("ila"))
                return GprSteelKind.SagIlave;
            if (paren == "s" || paren == "sa" || paren == "sag")
                return GprSteelKind.SagIlave;
            if (paren.Contains("mon") || paren == "m" || paren == "mo")
                return GprSteelKind.Montaj;
            if (paren.Contains("pil"))
                return GprSteelKind.Pilye;
            if (paren.Contains("duz") || paren.Contains("düz") || paren == "dz")
                return GprSteelKind.Duz;
            // Metin gövdesinde tür ipucu
            if (n.Contains("sol ila") || n.Contains("(sol"))
                return GprSteelKind.SolIlave;
            if (n.Contains("sag ila") || n.Contains("sağ ila") || n.Contains("(sag") || n.Contains("(sağ"))
                return GprSteelKind.SagIlave;
            if (n.Contains("(pil") || n.Contains("pilye"))
                return GprSteelKind.Pilye;
            if (n.Contains("(duz") || n.Contains("(düz") || n.Contains("duz)"))
                return GprSteelKind.Duz;
            return GprSteelKind.Unknown;
        }

        /// <summary>
        /// KALIP50 yerleşimi (referans kırmızı çizgilere göre):
        /// kenar → sol/sağ ilave; orta alt → düz; orta üst → pilye;
        /// üst kırmızı çizginin üstü (orta) → montaj.
        /// </summary>
        private static void ClassifyUnknownGprByPosition(
            List<GprSteelRec> notes, Point3d left, Point3d right, Vector3d along, Vector3d nn)
        {
            if (notes == null || notes.Count == 0) return;
            double len = left.DistanceTo(right);
            if (len < 1e-6) return;
            Vector3d ax = along.GetNormal();
            Vector3d ny = nn.GetNormal();

            // Metinden türü belli olmayanları konumla ayır; hepsi Unknown ise tamamını sınıflandır
            var idxs = new List<int>();
            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i].Kind == GprSteelKind.Unknown)
                    idxs.Add(i);
            }
            if (idxs.Count == 0) return;

            double[] t = new double[idxs.Count];
            double[] p = new double[idxs.Count];
            for (int k = 0; k < idxs.Count; k++)
            {
                Point3d pos = notes[idxs[k]].Pos;
                t[k] = (pos - left).DotProduct(ax) / len;
                p[k] = (pos - left).DotProduct(ny);
            }

            var midIdx = new List<int>();
            for (int k = 0; k < idxs.Count; k++)
            {
                // Mesnet/kenar bandı: sol / sağ ilave (görüntüde destek üstünde)
                if (t[k] <= 0.32)
                {
                    var g = notes[idxs[k]];
                    g.Kind = GprSteelKind.SolIlave;
                    notes[idxs[k]] = g;
                }
                else if (t[k] >= 0.68)
                {
                    var g = notes[idxs[k]];
                    g.Kind = GprSteelKind.SagIlave;
                    notes[idxs[k]] = g;
                }
                else
                    midIdx.Add(k);
            }

            // Uçta hiç kimse yoksa ama ≥3 yazı varsa en sol/sağ yine ilave
            if (midIdx.Count == idxs.Count && idxs.Count >= 3)
            {
                int iMin = 0, iMax = 0;
                for (int k = 1; k < idxs.Count; k++)
                {
                    if (t[k] < t[iMin]) iMin = k;
                    if (t[k] > t[iMax]) iMax = k;
                }
                if (iMin != iMax)
                {
                    var gL = notes[idxs[iMin]];
                    gL.Kind = GprSteelKind.SolIlave;
                    notes[idxs[iMin]] = gL;
                    var gR = notes[idxs[iMax]];
                    gR.Kind = GprSteelKind.SagIlave;
                    notes[idxs[iMax]] = gR;
                    midIdx.Clear();
                    for (int k = 0; k < idxs.Count; k++)
                        if (k != iMin && k != iMax) midIdx.Add(k);
                }
            }

            if (midIdx.Count == 0) return;
            if (midIdx.Count == 1)
            {
                var g = notes[idxs[midIdx[0]]];
                g.Kind = GprSteelKind.Duz;
                notes[idxs[midIdx[0]]] = g;
                return;
            }

            // Ortadakiler: p yüksek → üst. Büyük boşluk varsa en üst montaj.
            midIdx.Sort((a, b) => p[b].CompareTo(p[a])); // yüksekten alçağa
            int start = 0;
            if (midIdx.Count >= 3 && p[midIdx[0]] - p[midIdx[1]] >= 18.0)
            {
                var gM = notes[idxs[midIdx[0]]];
                gM.Kind = GprSteelKind.Montaj;
                notes[idxs[midIdx[0]]] = gM;
                start = 1;
            }

            int remain = midIdx.Count - start;
            if (remain == 1)
            {
                var g = notes[idxs[midIdx[start]]];
                g.Kind = GprSteelKind.Duz;
                notes[idxs[midIdx[start]]] = g;
                return;
            }

            // En yüksek kalan = pilye, en alçak = düz
            double pHi = p[midIdx[start]];
            double pLo = p[midIdx[midIdx.Count - 1]];
            if (Math.Abs(pHi - pLo) < 8.0)
            {
                // Aynı bandda (iki kırmızı çizgi arası çift yazı): üst sıradakiler pilye
                for (int i = start; i < midIdx.Count; i++)
                {
                    var g = notes[idxs[midIdx[i]]];
                    g.Kind = (i - start) < (remain + 1) / 2 ? GprSteelKind.Pilye : GprSteelKind.Duz;
                    notes[idxs[midIdx[i]]] = g;
                }
                return;
            }
            double midP = 0.5 * (pHi + pLo);
            for (int i = start; i < midIdx.Count; i++)
            {
                var g = notes[idxs[midIdx[i]]];
                g.Kind = p[midIdx[i]] >= midP - 1e-6 ? GprSteelKind.Pilye : GprSteelKind.Duz;
                notes[idxs[midIdx[i]]] = g;
            }
        }

        private static bool TryParseGprSteel(string txt, out int adet, out int cap, out int aralik)
        {
            adet = 0; cap = 0; aralik = 0;
            if (string.IsNullOrWhiteSpace(txt)) return false;
            Match m = RxAdetCapAralik.Match(txt);
            if (m.Success)
            {
                adet = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                cap = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                aralik = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                return adet > 0 && cap > 0 && aralik > 0;
            }
            m = RxCapAralik.Match(txt);
            if (!m.Success) return false;
            cap = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            aralik = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return cap > 0 && aralik > 0;
        }

        private static void AttachGprSteelInRegion(
            BoundaryIndex bounds, Point3d mid, List<GprSteelRec> gprNotes,
            List<ClosedArea> areas,
            Point3d leftInner, Point3d rightInner, Vector3d along, Vector3d nn,
            List<SlabTextRec> slabTexts, string slabName,
            ref SlabInfo slab)
        {
            if (gprNotes == null || gprNotes.Count == 0) return;

            double len = leftInner.DistanceTo(rightInner);
            if (len < 1e-6) return;
            Vector3d ax = along.GetNormal();
            Vector3d ny = nn.GetNormal();

            // Orta (duz/pilye): kendi doseme hucresi flood
            double floodPerp = Math.Max(500.0, Math.Min(1600.0, len * 1.15 + 200.0));
            // Ilave: iki komsu doseme arasindaki mesnet seridi (eksen-ince, kiris boyunca genis)
            double mesnetAlong = Math.Max(55.0, Math.Min(130.0, len * 0.07 + 40.0));
            double mesnetAlongBeam = Math.Max(350.0, Math.Min(1400.0, len * 1.0 + 200.0));

            Point3d ownLabel = mid;
            if (!TryFindSlabLabelPoint(slabTexts, slabName, out ownLabel))
                ownLabel = mid;

            Point3d floodStart = ownLabel;
            if (PointInsideMesnetBody(areas, floodStart) || PointInsideBosluk(areas, floodStart))
                floodStart = mid;
            if (PointInsideMesnetBody(areas, floodStart) || PointInsideBosluk(areas, floodStart))
                TryNudgeMidOutOfMesnet(leftInner, rightInner, ax, ny, areas, out floodStart);

            var asLabels = new List<SlabTextRec>();
            var idxMap = new List<int>();
            double reach = Math.Max(FloodMaxRadiusCm, floodPerp + len);
            for (int i = 0; i < gprNotes.Count; i++)
            {
                if (gprNotes[i].Pos.DistanceTo(floodStart) > reach
                    && gprNotes[i].Pos.DistanceTo(mid) > reach)
                    continue;
                asLabels.Add(new SlabTextRec
                {
                    Insert = gprNotes[i].Pos,
                    Pos = gprNotes[i].Pos,
                    Text = gprNotes[i].Text
                });
                idxMap.Add(i);
            }
            if (asLabels.Count == 0) return;

            var hitFlood = new List<int>();
            Point3d clipL = leftInner + ax * (len * 0.02);
            Point3d clipR = leftInner + ax * (len * 0.98);
            bool flooded = FloodClosedRegion(bounds, floodStart, asLabels, out _, hitFlood, areas,
                clipL, clipR, along, floodPerp);

            bool softSiniri = PointInsideDosemeSiniri(areas, floodStart);
            var hitMid = new HashSet<int>();
            void TryAddMid(int gi)
            {
                Point3d p = gprNotes[gi].Pos;
                double t = (p - leftInner).DotProduct(ax) / len;
                // Orta bant — mesnet/ilave ucundan uzak (Classify kenara cevirmesin)
                if (t < 0.34 || t > 0.66) return;
                if (PointInsideBosluk(areas, p)) return;
                if (PointInsideMesnetBody(areas, p)) return;
                hitMid.Add(gi);
            }

            if (flooded)
            {
                foreach (int li in hitFlood)
                    TryAddMid(idxMap[li]);
            }
            // Flood kacirdiysa: hucre ici, sinir kesmeden
            for (int li = 0; li < asLabels.Count; li++)
            {
                int gi = idxMap[li];
                if (hitMid.Contains(gi)) continue;
                Point3d p = gprNotes[gi].Pos;
                double t = (p - leftInner).DotProduct(ax) / len;
                if (t < 0.34 || t > 0.66) continue;
                if (PointInsideBosluk(areas, p) || PointInsideMesnetBody(areas, p)) continue;
                if (SegmentCrossesBoundary(bounds, floodStart, p, 0.5, softSiniri)
                    && SegmentCrossesBoundary(bounds, mid, p, 0.5, softSiniri))
                    continue;
                double tt = Math.Max(0, Math.Min(len, (p - leftInner).DotProduct(ax)));
                double q = Math.Abs((p - (leftInner + ax * tt)).DotProduct(ny));
                if (!flooded && q > floodPerp * 0.55) continue;
                hitMid.Add(gi);
            }

            // Ilave: yalniz sol/sag mesnet seridi (bu doseme ile komsu arasinda)
            var hitLeft = new List<int>();
            var hitRight = new List<int>();
            for (int i = 0; i < gprNotes.Count; i++)
            {
                Point3d p = gprNotes[i].Pos;
                double t = (p - leftInner).DotProduct(ax) / len;
                double alongL = Math.Abs((p - leftInner).DotProduct(ax));
                double alongR = Math.Abs((p - rightInner).DotProduct(ax));
                double tt = Math.Max(0, Math.Min(len, (p - leftInner).DotProduct(ax)));
                double q = Math.Abs((p - (leftInner + ax * tt)).DotProduct(ny));
                if (q > mesnetAlongBeam) continue;

                // Sol mesnet: iki doseme arasi — orta acikliga girme
                if (alongL <= mesnetAlong && t >= -0.22 && t <= 0.22)
                {
                    hitLeft.Add(i);
                    continue;
                }
                // Sag mesnet
                if (alongR <= mesnetAlong && t >= 0.78 && t <= 1.22)
                    hitRight.Add(i);
            }

            if (hitMid.Count == 0 && hitLeft.Count == 0 && hitRight.Count == 0) return;

            var found = new List<GprSteelRec>();
            void AddUnique(int i, GprSteelKind forceKind)
            {
                for (int k = 0; k < found.Count; k++)
                    if (found[k].Pos.DistanceTo(gprNotes[i].Pos) < 1.0) return;
                var g = gprNotes[i];
                if (forceKind != GprSteelKind.Unknown)
                    g.Kind = forceKind;
                found.Add(g);
            }

            foreach (int i in hitLeft) AddUnique(i, GprSteelKind.SolIlave);
            foreach (int i in hitRight) AddUnique(i, GprSteelKind.SagIlave);
            foreach (int i in hitMid)
            {
                bool edge = false;
                for (int h = 0; h < hitLeft.Count && !edge; h++)
                    if (hitLeft[h] == i) edge = true;
                for (int h = 0; h < hitRight.Count && !edge; h++)
                    if (hitRight[h] == i) edge = true;
                if (!edge) AddUnique(i, GprSteelKind.Unknown);
            }

            if (found.Count == 0) return;

            // Yalniz orta Unknown sinifla; sol/sag ilave zorlanmis kalsin
            ClassifyUnknownGprByPosition(found, leftInner, rightInner, along, nn);
            for (int i = 0; i < found.Count; i++)
            {
                bool fromLeft = false, fromRight = false;
                for (int h = 0; h < hitLeft.Count && !fromLeft; h++)
                    if (gprNotes[hitLeft[h]].Pos.DistanceTo(found[i].Pos) < 1.0) fromLeft = true;
                for (int h = 0; h < hitRight.Count && !fromRight; h++)
                    if (gprNotes[hitRight[h]].Pos.DistanceTo(found[i].Pos) < 1.0) fromRight = true;
                if (fromLeft)
                {
                    var g = found[i];
                    g.Kind = GprSteelKind.SolIlave;
                    found[i] = g;
                }
                else if (fromRight)
                {
                    var g = found[i];
                    g.Kind = GprSteelKind.SagIlave;
                    found[i] = g;
                }
            }

            CullForeignMiddleGpr(found, leftInner, rightInner, along, mid);

            if (found.Count == 0) return;
            slab.GprSteels = found;
            ApplyGprSteelOfKind(found, mid, GprSteelKind.Duz, ref slab.Adet, ref slab.CapMm, ref slab.AralikCm,
                fallbackAny: false, leftInner, rightInner, along);
        }

        /// <summary>
        /// Sahiplik: yalnız bu açıklık koridorundaki DOSEME ISMI'leri yarışır.
        /// Alt/üst sıradaki isimler dik mesafe ile elenir → kenar etiketleri çalınmaz.
        /// </summary>
        private static bool CorridorOwnsNote(
            Point3d notePos, List<SlabTextRec> slabTexts, string slabName,
            Point3d left, Point3d right, Vector3d ax, Vector3d ny,
            double corridorPerp, bool midOnly)
        {
            if (slabTexts == null || slabTexts.Count == 0) return true;
            string key = NormSlabKey(slabName);
            if (string.IsNullOrEmpty(key)) return true;
            double len = left.DistanceTo(right);
            if (len < 1e-6) return true;

            int best = -1;
            double bestD = double.MaxValue;
            bool sawOwn = false;
            for (int i = 0; i < slabTexts.Count; i++)
            {
                Point3d lab = slabTexts[i].Insert;
                double tLab = (lab - left).DotProduct(ax) / len;
                double qLab = Math.Abs((lab - (left + ax * Math.Max(0, Math.Min(len, (lab - left).DotProduct(ax))))).DotProduct(ny));
                // Koridor dışı = başka sıra / uzak panel
                if (qLab > corridorPerp) continue;
                if (midOnly)
                {
                    if (tLab < -0.05 || tLab > 1.05) continue;
                }
                else if (tLab < -0.45 || tLab > 1.45)
                    continue;

                Match m = RxSlabName.Match(slabTexts[i].Text ?? "");
                if (!m.Success) continue;
                string nm = NormSlabKey(m.Groups[1].Value);
                if (string.Equals(nm, key, StringComparison.Ordinal)) sawOwn = true;

                double d = Math.Min(lab.DistanceTo(notePos), DistanceToTextBox(notePos, slabTexts[i]));
                if (d < bestD) { bestD = d; best = i; }
            }

            // Koridorda kendi ismi yoksa: not kendi etiketine yakınsa kabul (tek sıra)
            if (best < 0)
            {
                if (!TryFindSlabLabelPoint(slabTexts, slabName, out Point3d own)) return true;
                return notePos.DistanceTo(own) <= Math.Max(len * 0.7, 350.0);
            }

            Match bm = RxSlabName.Match(slabTexts[best].Text ?? "");
            if (!bm.Success) return sawOwn;
            return string.Equals(NormSlabKey(bm.Groups[1].Value), key, StringComparison.Ordinal);
        }

        /// <summary>İlave: koridorda en yakın isim sol/sağ komşu (açıklık dışı t).</summary>
        private static bool CorridorEdgeNeighborOwns(
            Point3d notePos, List<SlabTextRec> slabTexts, string thisSlab,
            Point3d left, Point3d right, Vector3d ax, Vector3d ny,
            double corridorPerp, bool leftEdge)
        {
            if (slabTexts == null || slabTexts.Count == 0) return false;
            string own = NormSlabKey(thisSlab);
            double len = left.DistanceTo(right);
            if (len < 1e-6) return false;

            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < slabTexts.Count; i++)
            {
                Point3d lab = slabTexts[i].Insert;
                double tt = (lab - left).DotProduct(ax);
                double qLab = Math.Abs((lab - (left + ax * Math.Max(0, Math.Min(len, tt)))).DotProduct(ny));
                if (qLab > corridorPerp) continue;
                double tLab = tt / len;
                if (tLab < -0.85 || tLab > 1.85) continue;

                double d = Math.Min(lab.DistanceTo(notePos), DistanceToTextBox(notePos, slabTexts[i]));
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return false;
            Match m = RxSlabName.Match(slabTexts[best].Text ?? "");
            if (!m.Success) return false;
            string nm = NormSlabKey(m.Groups[1].Value);
            if (string.Equals(nm, own, StringComparison.Ordinal)) return true;
            double tBest = (slabTexts[best].Insert - left).DotProduct(ax) / len;
            if (leftEdge) return tBest < 0.38;
            return tBest > 0.62;
        }

        private static string NormSlabKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            return name.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");
        }

        private static bool TryFindSlabLabelPoint(List<SlabTextRec> slabTexts, string slabName, out Point3d pt)
        {
            pt = default;
            string key = NormSlabKey(slabName);
            if (string.IsNullOrEmpty(key) || slabTexts == null) return false;
            double best = double.MaxValue;
            bool ok = false;
            foreach (var st in slabTexts)
            {
                Match m = RxSlabName.Match(st.Text ?? "");
                if (!m.Success) continue;
                if (!string.Equals(NormSlabKey(m.Groups[1].Value), key, StringComparison.Ordinal)) continue;
                // En merkezi / ilk
                double d = st.Insert.X + st.Insert.Y;
                if (!ok || d < best) { best = d; pt = st.Insert; ok = true; }
            }
            return ok;
        }

        /// <summary>En yakın DOSEME ISMI bu döşeme mi.</summary>
        private static bool NearestSlabIs(Point3d notePos, List<SlabTextRec> slabTexts, string slabName)
        {
            if (slabTexts == null || slabTexts.Count == 0) return true;
            string key = NormSlabKey(slabName);
            if (string.IsNullOrEmpty(key)) return true;

            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < slabTexts.Count; i++)
            {
                double d = slabTexts[i].Insert.DistanceTo(notePos);
                // Yazı kutusu da yakınsa
                double dBox = DistanceToTextBox(notePos, slabTexts[i]);
                d = Math.Min(d, dBox);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return true;
            Match m = RxSlabName.Match(slabTexts[best].Text ?? "");
            if (!m.Success) return false;
            return string.Equals(NormSlabKey(m.Groups[1].Value), key, StringComparison.Ordinal);
        }

        private static double DistanceToTextBox(Point3d p, SlabTextRec st)
        {
            double x = Math.Max(st.MinX, Math.Min(p.X, st.MaxX));
            double y = Math.Max(st.MinY, Math.Min(p.Y, st.MaxY));
            return p.DistanceTo(new Point3d(x, y, 0));
        }

        /// <summary>
        /// İlave: komşuluk hattı — en yakın etiket sol/sağ komşu döşeme (bu span dışında).
        /// </summary>
        private static bool NearestSlabIsNeighborAlongEdge(
            Point3d notePos, List<SlabTextRec> slabTexts, string thisSlab,
            Point3d left, Point3d right, Vector3d along, bool leftEdge)
        {
            if (slabTexts == null || slabTexts.Count == 0) return false;
            string own = NormSlabKey(thisSlab);
            double len = left.DistanceTo(right);
            if (len < 1e-6) return false;
            Vector3d ax = along.GetNormal();

            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < slabTexts.Count; i++)
            {
                double d = Math.Min(slabTexts[i].Insert.DistanceTo(notePos), DistanceToTextBox(notePos, slabTexts[i]));
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return false;
            Match m = RxSlabName.Match(slabTexts[best].Text ?? "");
            if (!m.Success) return false;
            string nm = NormSlabKey(m.Groups[1].Value);
            if (string.Equals(nm, own, StringComparison.Ordinal)) return true; // kendi

            // Komşu: etiketi bu açıklığın dışında (sol uçta t küçük / sağda t büyük)
            double tLab = (slabTexts[best].Insert - left).DotProduct(ax) / len;
            if (leftEdge) return tLab < 0.35; // sol taraftaki / ortadaki komşu
            return tLab > 0.65;
        }

        /// <summary>
        /// Mesnet yakını yazı düz/pilye/montaj diye kalmışsa sol/sağ ilave'ye çevir.
        /// </summary>
        private static void RescueEdgeIlaveKinds(
            List<GprSteelRec> notes, Point3d left, Point3d right, Vector3d along)
        {
            if (notes == null || notes.Count == 0) return;
            double len = left.DistanceTo(right);
            if (len < 1e-6) return;
            Vector3d ax = along.GetNormal();
            double edgeDist = Math.Max(len * 0.22, 120.0);
            for (int i = 0; i < notes.Count; i++)
            {
                GprSteelKind k = notes[i].Kind;
                if (k == GprSteelKind.SolIlave || k == GprSteelKind.SagIlave)
                    continue;
                Point3d p = notes[i].Pos;
                double dL = p.DistanceTo(left);
                double dR = p.DistanceTo(right);
                double t = (p - left).DotProduct(ax) / len;
                if (dL <= edgeDist && dL <= dR + 1.0 && t < 0.45)
                {
                    var g = notes[i];
                    g.Kind = GprSteelKind.SolIlave;
                    notes[i] = g;
                }
                else if (dR <= edgeDist && dR < dL && t > 0.55)
                {
                    var g = notes[i];
                    g.Kind = GprSteelKind.SagIlave;
                    notes[i] = g;
                }
            }
        }

        /// <summary>
        /// Orta (düz/pilye/montaj) yazılar bu açıklığın ortasına uzaksa veya uç bandındaysa at.
        /// İlave (sol/sağ) korunur.
        /// </summary>
        private static void CullForeignMiddleGpr(
            List<GprSteelRec> notes, Point3d left, Point3d right, Vector3d along, Point3d mid)
        {
            if (notes == null || notes.Count == 0) return;
            double len = left.DistanceTo(right);
            if (len < 1e-6) return;
            Vector3d ax = along.GetNormal();
            double maxMidDist = Math.Max(len * 0.55, 250.0);
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                GprSteelKind k = notes[i].Kind;
                if (k == GprSteelKind.SolIlave || k == GprSteelKind.SagIlave)
                    continue;
                Point3d p = notes[i].Pos;
                double t = (p - left).DotProduct(ax) / len;
                // Kenarda kalan ama ilave diye işaretlenmemiş orta tür → at (yanlış sınıflama)
                if (t < 0.22 || t > 0.78)
                {
                    notes.RemoveAt(i);
                    continue;
                }
                if (p.DistanceTo(mid) > maxMidDist)
                    notes.RemoveAt(i);
            }
        }

        /// <summary>1 m'de As (cm²/m) — adet yok sayılır; yalnız çap/aralık.</summary>
        private static double AsPerMeterCm2(int capMm, int aralikCm)
        {
            if (capMm <= 0 || aralikCm <= 0) return 0;
            double dCm = capMm / 10.0;
            return (Math.PI * dCm * dCm / 4.0) * (100.0 / aralikCm);
        }

        private static bool TryFindGprSteel(
            List<GprSteelRec> notes, Point3d near, GprSteelKind want, out GprSteelRec found,
            Point3d? spanLeft = null, Point3d? spanRight = null, Vector3d? spanAlong = null)
        {
            found = default;
            if (notes == null || notes.Count == 0) return false;
            bool midKind = want == GprSteelKind.Duz || want == GprSteelKind.Pilye || want == GprSteelKind.Montaj;
            bool useSpan = midKind && spanLeft.HasValue && spanRight.HasValue && spanAlong.HasValue
                && spanAlong.Value.LengthSqrd > 1e-12;
            double len = useSpan ? spanLeft.Value.DistanceTo(spanRight.Value) : 0;
            Vector3d ax = useSpan ? spanAlong.Value.GetNormal() : Vector3d.XAxis;

            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i].Kind != want) continue;
                if (useSpan && len > 1e-6)
                {
                    double t = (notes[i].Pos - spanLeft.Value).DotProduct(ax) / len;
                    // Düz/pilye yalnız açıklık ortası — kenar ilave bandı değil
                    if (t < 0.22 || t > 0.78) continue;
                }
                double d = notes[i].Pos.DistanceTo(near);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return false;
            found = notes[best];
            return true;
        }

        private static bool ApplyGprSteelOfKind(
            List<GprSteelRec> notes, Point3d near, GprSteelKind want,
            ref int? adet, ref int cap, ref int aralik, bool fallbackAny,
            Point3d? spanLeft = null, Point3d? spanRight = null, Vector3d? spanAlong = null)
        {
            if (TryFindGprSteel(notes, near, want, out GprSteelRec n, spanLeft, spanRight, spanAlong))
            {
                if (n.Adet > 0) adet = n.Adet;
                if (n.CapMm > 0) cap = n.CapMm;
                if (n.AralikCm > 0) aralik = n.AralikCm;
                return true;
            }
            if (fallbackAny && TryFindGprSteel(notes, near, GprSteelKind.Unknown, out n, spanLeft, spanRight, spanAlong))
            {
                if (n.Adet > 0) adet = n.Adet;
                if (n.CapMm > 0) cap = n.CapMm;
                if (n.AralikCm > 0) aralik = n.AralikCm;
                return false;
            }
            if (fallbackAny)
            {
                ApplyGprSteelNear(notes, near, ref adet, ref cap, ref aralik);
                return false;
            }
            return false;
        }

        private static bool ApplyGprSteelOfKind(
            List<GprSteelRec> notes, Point3d near, GprSteelKind want,
            ref int adet, ref int cap, ref int aralik, bool fallbackAny,
            Point3d? spanLeft = null, Point3d? spanRight = null, Vector3d? spanAlong = null)
        {
            int? a = adet;
            bool ok = ApplyGprSteelOfKind(notes, near, want, ref a, ref cap, ref aralik, fallbackAny,
                spanLeft, spanRight, spanAlong);
            if (a.HasValue) adet = a.Value;
            return ok;
        }

        /// <summary>
        /// Üst ilave yazısı bu döşemenin: poligonun içi, ya da kiriş üstünde 120 cm
        /// ve en yakın döşeme adı bu açıklık. Yandaki döşemenin yazısı alınmaz.
        /// </summary>
        private static bool IlaveTextOfThisSlab(
            Point3d p, Polyline opening, List<SlabTextRec> slabTexts, string slabName)
        {
            if (opening != null && PolyContains(opening, p)) return true;
            if (opening != null && DistToPolyline(opening, p) > 120.0) return false;
            if (!string.IsNullOrEmpty(slabName) && !NearestSlabIs(p, slabTexts, slabName))
                return false;
            return true;
        }

        /// <summary>
        /// Mesnet demiri iki döşemenin arasında, kestiği kiriş çizgisinin üstündedir.
        /// Yazı bu döşemenin poligonunda veya 120 cm yakınında olmalıdır; yandaki döşemenin ilavesi kopyalanmaz.
        /// </summary>
        private static bool TryCopyIlaveOnMesnetLine(
            Transaction tr,
            List<GprSteelRec> notes, List<RawSeg> segs,
            Point3d atFace, Vector3d u, Vector3d up,
            ref int adet, ref int cap, ref int aralik,
            Polyline opening, List<SlabTextRec> slabTexts, string slabName)
        {
            if (notes == null || notes.Count == 0 || segs == null || segs.Count == 0) return false;
            Vector3d ax = u.Length > 1e-9 ? u.GetNormal() : Vector3d.XAxis;
            Vector3d nn = up.Length > 1e-9 ? up.GetNormal() : Vector3d.YAxis;
            var hits = new List<int>();
            int best = -1;
            double bestDist = double.MaxValue;
            for (int i = 0; i < notes.Count; i++)
            {
                Point3d p = notes[i].Pos;
                if (!IlaveTextOfThisSlab(p, opening, slabTexts, slabName)) continue;
                double upCm = (p - atFace).DotProduct(nn);
                if (upCm < -200.0 || upCm > 550.0) continue;
                bool onBeam = false;
                for (int s = 0; s < segs.Count; s++)
                {
                    RawSeg seg = segs[s];
                    double len = SegLen(seg);
                    if (len < 20.0) continue;
                    double ux = (seg.X1 - seg.X0) / len;
                    double uy = (seg.Y1 - seg.Y0) / len;
                    if (Math.Abs(ux * ax.X + uy * ax.Y) > 0.80) continue;
                    if (DistToSeg(atFace.X, atFace.Y, seg) > 80.0) continue;
                    ClosestOnSeg(p.X, p.Y, seg, out double cx, out double cy, out double d2);
                    double perp = Math.Sqrt(d2);
                    // Uzun düşey yazının kutusu kiriş çizgisinden yarım boy kayabilir.
                    if (perp > 100.0) continue;
                    ClosestOnSeg(atFace.X, atFace.Y, seg, out double fx, out double fy, out _);
                    double along = Math.Sqrt((cx - fx) * (cx - fx) + (cy - fy) * (cy - fy));
                    if (along > 550.0) continue;
                    onBeam = true;
                    break;
                }
                if (!onBeam) continue;
                hits.Add(i);
                // Çiftin kendisi: hattın kestiği noktaya en yakın yazı. Uzaktaki başka yazı seçilmesin.
                double dFace = p.DistanceTo(atFace);
                if (dFace >= bestDist) continue;
                bestDist = dFace;
                best = i;
            }
            if (best < 0) return false;

            // Çift yazı: birbirine 120 cm'den yakın olanlar aynı mesnet. As/m büyük olan kalır.
            const double pairCm = 120.0;
            Point3d anchor = notes[best].Pos;
            int win = best;
            double winAs = AsPerMeterCm2(notes[best].CapMm, notes[best].AralikCm);
            var cluster = new List<int> { best };
            for (int k = 0; k < hits.Count; k++)
            {
                int i = hits[k];
                if (i == best) continue;
                if (notes[i].Pos.DistanceTo(anchor) > pairCm) continue;
                cluster.Add(i);
                double a = AsPerMeterCm2(notes[i].CapMm, notes[i].AralikCm);
                if (a > winAs + 1e-9)
                {
                    winAs = a;
                    win = i;
                }
            }
            if (cluster.Count > 1)
            {
                for (int k = 0; k < cluster.Count; k++)
                {
                    int i = cluster[k];
                    if (i == win) continue;
                    double a = AsPerMeterCm2(notes[i].CapMm, notes[i].AralikCm);
                    if (a + 1e-9 < winAs)
                        EraseGprText(tr, notes[i].Id);
                }
            }

            GprSteelRec g = notes[win];
            if (g.Adet <= 0 && g.CapMm <= 0 && g.AralikCm <= 0) return false;
            if (g.CapMm > 0) cap = g.CapMm;
            if (g.AralikCm > 0) aralik = g.AralikCm;
            if (g.Adet > 0) adet = g.Adet;
            MarkPark(g.Id);
            return true;
        }

        private static void EraseGprText(Transaction tr, ObjectId id)
        {
            if (tr == null || id.IsNull || !id.IsValid || id.IsErased) return;
            try
            {
                var ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                if (ent == null || ent.IsErased) return;
                ent.Erase();
            }
            catch { }
        }

        private static void MarkPark(ObjectId id)
        {
            if (!id.IsNull && id.IsValid && !id.IsErased)
            {
                _parkBelow.Add(id);
                _gprTransferred.Add(id);
            }
        }

        private static bool HasGprKind(List<GprSteelRec> notes, GprSteelKind kind)
        {
            if (notes == null) return false;
            for (int i = 0; i < notes.Count; i++)
                if (notes[i].Kind == kind) return true;
            return false;
        }

        /// <summary>
        /// Yazı bu açıklığın poligonundaysa ve başka açıklığın ortasına daha yakın değilse bu döşemenindir.
        /// </summary>
        private static bool SpanOwnsGprText(
            Point3d pos, int spanIndex, List<SpanRec> spans, Polyline[] ownPoly)
        {
            if (spans == null || spanIndex < 0 || spanIndex >= spans.Count) return false;
            Polyline mine = ownPoly != null && spanIndex < ownPoly.Length ? ownPoly[spanIndex] : null;
            if (mine != null && !PolyContains(mine, pos))
                return false;
            double myD = pos.DistanceTo(spans[spanIndex].Mid);
            int n = ownPoly != null ? Math.Min(ownPoly.Length, spans.Count) : spans.Count;
            for (int i = 0; i < n; i++)
            {
                if (i == spanIndex) continue;
                if (!spans[i].InPick) continue;
                Polyline other = ownPoly != null && i < ownPoly.Length ? ownPoly[i] : null;
                if (other != null && !PolyContains(other, pos)) continue;
                if (other == null && mine != null) continue;
                if (mine == null && other != null) return false;
                if (pos.DistanceTo(spans[i].Mid) + 5.0 < myD)
                    return false;
            }
            return mine != null || myD < 1.0e9;
        }

        /// <summary>Düz/pilye: yalnız bu döşemenin orta bant yazısı kopyalanır ve taşınır.</summary>
        private static bool TakeOwnedGpr(
            List<GprSteelRec> notes, Point3d near, GprSteelKind want,
            ref int adet, ref int cap, ref int aralik,
            Point3d spanLeft, Point3d spanRight, Vector3d spanAlong,
            int spanIndex, List<SpanRec> spans, Polyline[] ownPoly)
        {
            if (notes == null || notes.Count == 0) return false;
            double len = spanLeft.DistanceTo(spanRight);
            Vector3d ax = len > 1e-9 ? (spanRight - spanLeft) / len : spanAlong;
            if (ax.LengthSqrd > 1e-12) ax = ax.GetNormal();
            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i].Kind != want) continue;
                if (!SpanOwnsGprText(notes[i].Pos, spanIndex, spans, ownPoly)) continue;
                // Uç ilave bandı (t&lt;0.22 / t&gt;0.78) düz/pilye/montaj sayılmaz.
                if (len > 1e-6)
                {
                    double t = (notes[i].Pos - spanLeft).DotProduct(ax) / len;
                    if (t < 0.22 || t > 0.78) continue;
                }
                double d = notes[i].Pos.DistanceTo(near);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return false;
            GprSteelRec n = notes[best];
            if (n.Adet > 0) adet = n.Adet;
            if (n.CapMm > 0) cap = n.CapMm;
            if (n.AralikCm > 0) aralik = n.AralikCm;
            MarkPark(n.Id);
            return true;
        }

        /// <summary>İlave yazısı yok: çubuk kırmızı kalır ve aşağı iner. Düz bu yola girmez.</summary>
        private static void ParkIlaveAsagi(Transaction tr, ObjectId bar, ObjectId label)
        {
            ParkUnmatched(tr, bar, label);
            if (EtkinYon == DonatiYonu.Serbest) return;
            MarkPark(bar);
            MarkPark(label);
        }

        /// <summary>GPR eşleşmeyen donatı yerinde kalır, kırmızı olur. Aşağı taşınmaz.</summary>
        private static void ParkUnmatched(Transaction tr, ObjectId bar, ObjectId label)
        {
            if (EtkinYon == DonatiYonu.Serbest) return;
            if (!bar.IsNull && bar.IsValid && !bar.IsErased)
            {
                try
                {
                    var ent = tr.GetObject(bar, OpenMode.ForWrite, false) as Entity;
                    if (ent != null && !ent.IsErased)
                        ent.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
                }
                catch { }
            }
            if (!label.IsNull && label.IsValid && !label.IsErased)
            {
                try
                {
                    var ent = tr.GetObject(label, OpenMode.ForWrite, false) as Entity;
                    if (ent != null && !ent.IsErased)
                        ent.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
                }
                catch { }
            }
        }

        /// <summary>Bu açıklığın üzerinden geçen kırmızı ve mavi GPR çizgileri 10000 cm aşağı iner.</summary>
        private static void ParkGuidesOverSpan(
            SpanRec span, AltDuzAlanModel model, Polyline opening, Vector3d u)
        {
            if (_kirmiziGprCizgi == null) return;
            TryFaceContaining(model, span.Mid, out var face);
            double spanLen = span.LeftInner.DistanceTo(span.RightInner);
            Vector3d ax = u.Length > 1e-9 ? u.GetNormal() : Vector3d.XAxis;
            foreach (KirmiziGprCizgi c in _kirmiziGprCizgi)
            {
                if (!GuideOverThisSpan(c, opening, face, span.LeftInner, ax, spanLen)) continue;
                MarkPark(c.Id);
            }
        }

        /// <summary>Çubuğa paralel bant ya da açıklığı kesen dik çizgi (diğer renk).</summary>
        private static bool GuideOverThisSpan(
            KirmiziGprCizgi c, Polyline opening, NetTopologySuite.Geometries.Polygon face,
            Point3d left, Vector3d ax, double spanLen)
        {
            if (LineOverOpening(c, opening, face, left, ax, spanLen)) return true;
            if (opening == null) return false;
            if (PolyContains(opening, c.A) || PolyContains(opening, c.B)) return true;
            return SegmentCutsPolyline(c.A, c.B, opening);
        }

        private static bool SegmentCutsPolyline(Point3d a, Point3d b, Polyline pl)
        {
            if (pl == null) return false;
            int n = pl.NumberOfVertices;
            if (n < 2) return false;
            int segs = pl.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                Point3d p = pl.GetPoint3dAt(i);
                Point3d q = pl.GetPoint3dAt((i + 1) % n);
                if (SegmentsCross(a, b, p, q)) return true;
            }
            return false;
        }

        private static bool SegmentsCross(Point3d a, Point3d b, Point3d c, Point3d d)
        {
            double rx = b.X - a.X, ry = b.Y - a.Y;
            double sx = d.X - c.X, sy = d.Y - c.Y;
            double den = rx * sy - ry * sx;
            if (Math.Abs(den) < 1e-8) return false;
            double qx = c.X - a.X, qy = c.Y - a.Y;
            double t = (qx * sy - qy * sx) / den;
            double uu = (qx * ry - qy * rx) / den;
            return t >= -1e-6 && t <= 1.0 + 1e-6 && uu >= -1e-6 && uu <= 1.0 + 1e-6;
        }

        private static bool TryGetClassedIlave(SpanRec sp, bool sagSide, out GprSteelRec found)
        {
            found = default;
            List<GprSteelRec> notes = sp.Slab.GprSteels;
            if (notes == null || notes.Count == 0) return false;
            GprSteelKind want = sagSide ? GprSteelKind.SagIlave : GprSteelKind.SolIlave;
            Point3d near = sagSide ? sp.RightInner : sp.LeftInner;
            return TryFindGprSteel(notes, near, want, out found);
        }

        /// <summary>
        /// Bu rengin poligon içindeki GPR yazılarının hepsi yeni donatıya aktarıldıysa çift çizgi 10000 cm iner.
        /// </summary>
        private static void ParkGuidePairIfTextsConsumed(
            List<SpanRec> spans, Polyline[] ownPoly, List<GprSteelRec> notes)
        {
            if (EtkinYon == DonatiYonu.Serbest || spans == null || ownPoly == null || notes == null)
                return;
            if (_kirmiziGprCizgi == null) return;
            int n = Math.Min(spans.Count, ownPoly.Length);
            for (int i = 0; i < n; i++)
            {
                if (!spans[i].InPick) continue;
                Polyline op = ownPoly[i];
                if (op == null) continue;
                int total = 0, done = 0;
                for (int k = 0; k < notes.Count; k++)
                {
                    if (!PolyContains(op, notes[k].Pos)) continue;
                    total++;
                    if (_gprTransferred.Contains(notes[k].Id)) done++;
                }
                if (total == 0 || done != total) continue;
                foreach (KirmiziGprCizgi c in _kirmiziGprCizgi)
                {
                    if (!c.Active) continue;
                    if (ChordInsidePolyline(c.A, c.B, op) <= 0) continue;
                    MarkPark(c.Id);
                }
            }
        }

        private static void FlushParkBelow(Transaction tr)
        {
            if (!ParkBelowEnabled || _parkBelow.Count == 0) return;
            var move = Matrix3d.Displacement(new Vector3d(0, -ParkBelowCm, 0));
            foreach (ObjectId id in _parkBelow)
            {
                if (id.IsNull || !id.IsValid || id.IsErased) continue;
                try
                {
                    var ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                    if (ent == null || ent.IsErased) continue;
                    ent.TransformBy(move);
                }
                catch { }
            }
            _parkBelow.Clear();
        }

        /// <summary>
        /// Normal üst ilave: yalnız bu döşemenin sol/sağ ilave GPR'si.
        /// Yoksa false → çağıran kırmızı etiket (komşu döşemeden çalma yok).
        /// </summary>
        private static bool TryApplyGprIlaveSteel(
            List<GprSteelRec> notes, Point3d near, bool sagSide,
            double adetRefLenCm, ref int adet, ref int cap, ref int aralik)
        {
            GprSteelKind want = sagSide ? GprSteelKind.SagIlave : GprSteelKind.SolIlave;
            if (!TryFindGprSteel(notes, near, want, out GprSteelRec g))
                return false;
            if (g.CapMm > 0) cap = g.CapMm;
            if (g.AralikCm > 0) aralik = g.AralikCm;
            if (g.Adet > 0) adet = g.Adet;
            else if (aralik > 0)
                adet = Math.Max(1, (int)Math.Round(Math.Max(adetRefLenCm, 1) / aralik) + 1);
            MarkPark(g.Id);
            return true;
        }

        /// <summary>
        /// Konsol: önce program tablosu; proje As (cm²/m, adetsiz) ≥ program As ise çap/aralık güncelle.
        /// Proje daha küçükse yazı değişmez. Dönüş: GPR ilave bulundu mu (renk için).
        /// </summary>
        private static bool ApplyKonsolSteelWithGprGate(
            List<GprSteelRec> notes, Point3d near, bool sagSide,
            double konsolBoyCm, double adetRefLenCm,
            ref int adet, ref int cap, ref int aralik)
        {
            DosemeKonsolSettings.ApplySteel(konsolBoyCm, adetRefLenCm, ref adet, ref cap, ref aralik);
            GprSteelKind want = sagSide ? GprSteelKind.SagIlave : GprSteelKind.SolIlave;
            if (!TryFindGprSteel(notes, near, want, out GprSteelRec g))
                return false;
            if (g.CapMm <= 0 || g.AralikCm <= 0) return true; // bulundu ama eksik — eş say
            double progAs = AsPerMeterCm2(cap, aralik);
            double projAs = AsPerMeterCm2(g.CapMm, g.AralikCm);
            if (projAs + 1e-9 < progAs)
                return true; // proje zayıf → program yazısı, ama eş etiket vardı
            cap = g.CapMm;
            aralik = g.AralikCm;
            adet = Math.Max(1, (int)Math.Round(Math.Max(adetRefLenCm, 1) / aralik) + 1);
            MarkPark(g.Id);
            return true;
        }

        private static void ApplyGprSteelNear(
            List<GprSteelRec> notes, Point3d near, ref int? adet, ref int cap, ref int aralik)
        {
            if (notes == null || notes.Count == 0) return;
            int best = 0;
            double bestD = notes[0].Pos.DistanceTo(near);
            for (int i = 1; i < notes.Count; i++)
            {
                double d = notes[i].Pos.DistanceTo(near);
                if (d < bestD) { bestD = d; best = i; }
            }
            var n = notes[best];
            if (n.Adet > 0) adet = n.Adet;
            if (n.CapMm > 0) cap = n.CapMm;
            if (n.AralikCm > 0) aralik = n.AralikCm;
        }

        private static void ApplyGprSteelNear(
            List<GprSteelRec> notes, Point3d near, ref int adet, ref int cap, ref int aralik)
        {
            int? a = adet;
            ApplyGprSteelNear(notes, near, ref a, ref cap, ref aralik);
            if (a.HasValue) adet = a.Value;
        }

        private struct KotTextRec
        {
            /// <summary>Plan_ID DBText.Position (üst/alt kot baseline).</summary>
            public Point3d Insert;
            public Point3d Pos;
            public double ValueM;
            public string Text;
            public double Rotation;
        }

        private static List<KotTextRec> _kotCache;

        private static void EnsureKotCache(Transaction tr, BlockTableRecord btr)
        {
            _kotCache = new List<KotTextRec>();
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                if (!(ent is DBText || ent is MText)) continue;
                string lyr = ent.Layer ?? "";
                if (!string.Equals(lyr, LayerKotYazi, StringComparison.OrdinalIgnoreCase) &&
                    lyr.IndexOf("KOT YAZI", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Point3d insert;
                string txt;
                double rot;
                if (ent is DBText db)
                {
                    insert = db.Position;
                    txt = db.TextString ?? "";
                    rot = db.Rotation;
                }
                else
                {
                    var mt = (MText)ent;
                    insert = mt.Location;
                    txt = mt.Contents ?? "";
                    rot = mt.Rotation;
                }
                if (string.IsNullOrWhiteSpace(txt)) continue;
                txt = txt.Replace("\\P", " ").Replace("{\\(", "").Replace(")", "").Trim();
                if (!TryParseKotMeters(txt, out double kot)) continue;

                Point3d boxPos = insert;
                if (TryGetTextBox(ent, out double minX, out double minY, out double maxX, out double maxY))
                    boxPos = new Point3d((minX + maxX) * 0.5, (minY + maxY) * 0.5, 0);

                _kotCache.Add(new KotTextRec
                {
                    Insert = new Point3d(insert.X, insert.Y, 0),
                    Pos = boxPos,
                    ValueM = kot,
                    Text = txt,
                    Rotation = rot
                });
            }
        }

        /// <summary>İki açı farkı, 0…π (rad).</summary>
        private static double RotDelta(double a, double b)
        {
            double d = Math.Abs(a - b) % (Math.PI * 2.0);
            if (d > Math.PI) d = Math.PI * 2.0 - d;
            return d;
        }

        /// <summary>Etiket ekseni: +X yazı yönü, +Y yazının üstü. Dönüş 0 iken dünya X/Y.</summary>
        private static void ToLabelLocal(Point3d origin, double rot, Point3d p, out double lx, out double ly)
        {
            double c = Math.Cos(rot), s = Math.Sin(rot);
            double dx = p.X - origin.X, dy = p.Y - origin.Y;
            lx = dx * c + dy * s;
            ly = -dx * s + dy * c;
        }

        /// <summary>
        /// Plan_ID AppendSlabLabel geometrisiyle üst kot bul.
        /// Üst kot, etiket sol-altından yerel (14.33·ann, −26·ann); alt ≈ bir 10·ann daha aşağı.
        /// Etiket çevrilmişse arama yazının kendi ekseninde yapılır (kotlar birlikte döndüyse).
        /// ann = 1 (1:50) veya 2 (1:100). Çiftte yerel +Y büyük olan = üst kot.
        /// </summary>
        private static void AttachUstKotFromPlanLabel(SlabTextRec label, Point2d[] cellRing, ref SlabInfo slab)
        {
            if (_kotCache == null || _kotCache.Count == 0) return;

            double labelRot = label.Rotation;
            bool labelTurned = RotDelta(labelRot, 0) >= 0.25;
            KotTextRec top;
            double bestAnn;
            double frame;
            bool found = TryPickUstKot(label, cellRing, labelRot, out top, out bestAnn);
            frame = labelRot;
            if (!found && labelTurned && TryPickUstKot(label, cellRing, 0, out top, out bestAnn))
            {
                found = true;
                frame = 0;
            }
            if (!found)
            {
                if (TryLooseUstKot(label, cellRing, labelRot, out top)
                    || (labelTurned && TryLooseUstKot(label, cellRing, 0, out top)))
                    slab.KotM = top.ValueM;
                return;
            }

            // Beklenen üst civarındaki çiftten yerel +Y büyük olan (üst satır)
            ToLabelLocal(label.Insert, frame, top.Insert, out double pairLx, out double pairLy);
            double gap = PlanKotPairGapCm * bestAnn + 4.0;
            foreach (var k in _kotCache)
            {
                if (RotDelta(k.Rotation, top.Rotation) > 0.35) continue;
                ToLabelLocal(label.Insert, frame, k.Insert, out double lx, out double ly);
                if (Math.Abs(lx - pairLx) > 20.0 * bestAnn) continue;
                if (Math.Abs(ly - pairLy) > gap) continue;
                if (ly <= pairLy) continue;
                top = k;
                pairLx = lx;
                pairLy = ly;
            }

            // d= ile doğrula: alt kot ≈ üst − d/100 → yanlış kiriş kotunu ele
            if (slab.ThicknessCm.HasValue && slab.ThicknessCm.Value > 1.0)
            {
                double expectBotM = top.ValueM - slab.ThicknessCm.Value / 100.0;
                bool pairOk = false;
                ToLabelLocal(label.Insert, frame, top.Insert, out double topLx, out double topLy);
                foreach (var k in _kotCache)
                {
                    if (RotDelta(k.Rotation, top.Rotation) > 0.35) continue;
                    ToLabelLocal(label.Insert, frame, k.Insert, out double lx, out double ly);
                    if (Math.Abs(lx - topLx) > 20.0 * bestAnn) continue;
                    double dy = topLy - ly;
                    if (dy < 4.0 * bestAnn || dy > 18.0 * bestAnn) continue;
                    if (Math.Abs(k.ValueM - expectBotM) <= 0.06)
                    {
                        pairOk = true;
                        break;
                    }
                }
                if (!pairOk)
                {
                    foreach (var candTop in _kotCache)
                    {
                        if (RotDelta(candTop.Rotation, frame) > 0.35) continue;
                        ToLabelLocal(label.Insert, frame, candTop.Insert, out double cx, out double cy);
                        if (Math.Abs(cx - PlanKotOffsetXCm * bestAnn) > 30.0 * bestAnn) continue;
                        if (cy > -5.0 || cy < -100.0 * bestAnn) continue;
                        foreach (var candBot in _kotCache)
                        {
                            if (RotDelta(candBot.Rotation, candTop.Rotation) > 0.35) continue;
                            ToLabelLocal(label.Insert, frame, candBot.Insert, out double bx, out double by);
                            if (Math.Abs(bx - cx) > 15.0 * bestAnn) continue;
                            double dy = cy - by;
                            if (dy < 4.0 * bestAnn || dy > 18.0 * bestAnn) continue;
                            double thCm = (candTop.ValueM - candBot.ValueM) * 100.0;
                            if (Math.Abs(thCm - slab.ThicknessCm.Value) <= 1.5)
                            {
                                top = candTop;
                                pairOk = true;
                                break;
                            }
                        }
                        if (pairOk) break;
                    }
                }
            }

            slab.KotM = top.ValueM;
        }

        /// <summary>Etiket ekseninde beklenen üst kot noktasına en yakın KOT YAZI.</summary>
        private static bool TryPickUstKot(
            SlabTextRec label, Point2d[] cellRing, double frame,
            out KotTextRec top, out double annUsed)
        {
            top = default;
            annUsed = 1.0;
            double bestScore = double.MaxValue;
            bool found = false;
            foreach (double ann in new[] { 1.0, 2.0 })
            {
                double expectLx = PlanKotOffsetXCm * ann;
                double expectLy = -(PlanLabelToQCm + PlanQToUstKotCm) * ann;
                double tolX = 18.0 * ann;
                double tolY = 14.0 * ann;
                foreach (var k in _kotCache)
                {
                    ToLabelLocal(label.Insert, frame, k.Insert, out double lx, out double ly);
                    if (cellRing != null &&
                        !PointInRing(cellRing, k.Insert.X, k.Insert.Y) &&
                        !PointInRing(cellRing, k.Pos.X, k.Pos.Y))
                    {
                        if (Math.Abs(lx - expectLx) + Math.Abs(ly - expectLy) > 25.0 * ann) continue;
                    }

                    double dx = lx - expectLx;
                    double dy = ly - expectLy;
                    if (Math.Abs(dx) > tolX || Math.Abs(dy) > tolY) continue;

                    double score = dx * dx + dy * dy;
                    double altLy = expectLy - PlanKotPairGapCm * ann;
                    if (Math.Abs(ly - altLy) < Math.Abs(dy) && Math.Abs(dx) <= tolX)
                        score += 50.0 * ann * ann;
                    if (RotDelta(k.Rotation, frame) > 0.35)
                        score += 400.0 * ann * ann;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        top = k;
                        annUsed = ann;
                        found = true;
                    }
                }
            }
            return found;
        }

        /// <summary>Gevşek: yazının yerel altında ve sağında, en üst satır.</summary>
        private static bool TryLooseUstKot(
            SlabTextRec label, Point2d[] cellRing, double frame, out KotTextRec top)
        {
            top = default;
            bool found = false;
            double bestRank = double.MinValue;
            foreach (var k in _kotCache)
            {
                ToLabelLocal(label.Insert, frame, k.Insert, out double lx, out double ly);
                if (ly > -5.0 || ly < -100.0) continue;
                if (lx < -10.0 || lx > 90.0) continue;
                if (cellRing != null &&
                    !PointInRing(cellRing, k.Insert.X, k.Insert.Y) &&
                    !PointInRing(cellRing, k.Pos.X, k.Pos.Y) &&
                    k.Insert.DistanceTo(label.Insert) > 80.0)
                    continue;
                double rank = ly;
                if (RotDelta(k.Rotation, frame) > 0.35)
                    rank -= 1000.0;
                if (rank <= bestRank) continue;
                bestRank = rank;
                top = k;
                found = true;
            }
            return found;
        }

        private static bool TryParseKotMeters(string txt, out double kotM)
        {
            kotM = 0;
            if (string.IsNullOrWhiteSpace(txt)) return false;
            string s = txt.Trim()
                .Replace('\u2212', '-')
                .Replace('\u2013', '-')
                .Replace('\u2014', '-')
                .Replace('\u00B1', '+')
                .Replace('±', '+');
            // Plan_ID FormatKotElevationMeters: "+0.00" / "-0.90" / "±0.00"
            Match m = RxKot.Match(s);
            if (!m.Success) return false;
            string num = m.Groups[1].Value
                .Replace('\u2212', '-')
                .Replace('±', '+')
                .Replace('\u00B1', '+')
                .Replace(',', '.');
            if (num.StartsWith("+", StringComparison.Ordinal)) num = num.Substring(1);
            if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out kotM))
                return false;
            return Math.Abs(kotM) < 50.0;
        }

        private static void FillSlabInfoFromBlob(ref SlabInfo info, string blob)
        {
            if (!string.IsNullOrWhiteSpace(blob))
            {
                Match mName = RxSlabName.Match(blob.Trim());
                if (mName.Success)
                    info.Name = mName.Groups[1].Value.ToUpperInvariant();
            }

            Match mD = RxKalınlık.Match(blob);
            if (mD.Success && double.TryParse(mD.Groups[1].Value.Replace(',', '.'),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double th))
                info.ThicknessCm = th;

            Match mA = RxAdetCapAralik.Match(blob);
            if (mA.Success)
            {
                info.Adet = int.Parse(mA.Groups[1].Value, CultureInfo.InvariantCulture);
                info.CapMm = int.Parse(mA.Groups[2].Value, CultureInfo.InvariantCulture);
                info.AralikCm = int.Parse(mA.Groups[3].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                Match mC = RxCapAralik.Match(blob);
                if (mC.Success)
                {
                    info.CapMm = int.Parse(mC.Groups[1].Value, CultureInfo.InvariantCulture);
                    info.AralikCm = int.Parse(mC.Groups[2].Value, CultureInfo.InvariantCulture);
                }
            }
        }

        private static bool PointInPolyline(Polyline pl, Point3d p)
        {
            int n = pl.NumberOfVertices;
            if (n < 3) return false;
            var ring = new Point2d[n];
            for (int i = 0; i < n; i++)
                ring[i] = pl.GetPoint2dAt(i);
            return PointInRing(ring, p.X, p.Y);
        }

        /// <summary>
        /// Boşluk parçasına pilye uzatma referans boyu (TryPlacePilyeSpan L×1/4 kullanır).
        /// Uzatma ≥50 cm; alan &lt;50 ise boşluk kenarına paspayı kadar yaklaş.
        /// </summary>
        private static double HolePilyeUzatmaRefLen(double holeSpanL)
        {
            double maxUz = Math.Max(0.0, holeSpanL - PaspayiCm);
            double uz;
            if (maxUz < KonsolPilyeMinUzCm)
                uz = maxUz;
            else
                uz = Math.Min(maxUz, Math.Max(KonsolPilyeMinUzCm, holeSpanL * 0.25));
            return uz * 4.0;
        }

        /// <summary>
        /// Pilye/üst ilave L/4: yalnız üst kot (KOT YAZI üst satır) aynıysa.
        /// Üst kot farklı → kes / gönye. Alt kot ve kalınlık (d=) bakılmaz.
        /// Kot okunamadıysa L/4 kesilmez.
        /// </summary>
        private static bool SlabsContinue(SlabInfo a, SlabInfo b)
        {
            if (!a.HasLabel || !b.HasLabel)
                return false;
            if (a.KotM.HasValue && b.KotM.HasValue &&
                Math.Abs(a.KotM.Value - b.KotM.Value) > KotMatchTolM)
                return false;
            return true;
        }

        /// <summary>
        /// Mesnetin öte yanında kapalı döşeme.
        /// Konsol pilye: max(50, konsol/2); konsol &lt; 50 → kenara paspayı. Üst ilave ayrı (KonsolBoy).
        /// Diğer komşu: yalnız aynı üst kot → L/4.
        /// </summary>
        private static bool TryNeighborSlabBeyond(
            BoundaryIndex bounds,
            Point3d outerFace,
            Vector3d outDir,
            Vector3d n,
            List<ClosedArea> areas,
            List<SlabTextRec> slabTexts,
            List<GprSteelRec> gprNotes,
            SlabInfo self,
            double selfL,
            out double LneighPilye,
            out bool isKonsol,
            out double konsolBoy)
        {
            LneighPilye = selfL;
            isKonsol = false;
            konsolBoy = 0;
            Vector3d o = outDir.GetNormal();
            double maxRay = Math.Max(4000.0, selfL * 2);
            if (!TryNearestBoundaryHit(bounds, outerFace, o, maxRay, 4.0, out double tHit, out string hitLyr))
                return false;
            if (IsBoslukLayer(hitLyr))
                return false;

            Point3d near = outerFace + o * Math.Min(8.0, tHit * 0.35);
            Point3d far = outerFace + o * tHit;
            // Kalıp boşluğun içindeki döşeme sınırı komşu döşeme değildir; donatı boşluğa uzamaz.
            Point3d insideGap = outerFace + o * Math.Min(12.0, Math.Max(6.0, tHit * 0.3));
            if (PointInsideBosluk(areas, insideGap))
                return false;
            if (tHit > 30.0 && PointInsideBosluk(areas, outerFace + o * (tHit * 0.5)))
                return false;
            bool sinirKenar = IsDosemeSiniriLayer(hitLyr)
                && !IsMesnetLayer(hitLyr, null);
            bool mesnetKenar = IsMesnetLayer(hitLyr, null);
            // Arkası boş: ilk sınırdan sonra ~90 cm içinde ikinci yüz yok
            bool tekYuz = !TryNearestBoundaryHit(bounds, outerFace + o * (tHit + 6.0), o, 90.0, 2.0, out _, out _);
            bool yakinSinir = sinirKenar && tHit <= SiniriKonsolMaxCm;
            // Etiketsiz / SINIRI olmayan kısa çıkıntı (kenar KIRIS veya tek yüzey)
            bool kisaBosKonsol = tHit <= SiniriKonsolMaxCm && tHit >= PaspayiCm
                && (sinirKenar || tekYuz || (mesnetKenar && tekYuz));

            SlabInfo nb;
            if (!TryClassifyGapAsDoseme(bounds, near, far, o, n, areas, slabTexts, gprNotes, out nb))
            {
                // Etiket yok (küçük konsol / kot uzak DB): kısa çıkıntı yine konsol
                if (!yakinSinir && !kisaBosKonsol) return false;
                nb = new SlabInfo { CapMm = 8, AralikCm = 20, HasLabel = true };
                Vector3d nn = n.GetNormal();
                var cand = slabTexts ?? new List<SlabTextRec>();
                if (TryPickSlabLabelInCorridor(near, far, o, nn, cand, bounds, areas, out SlabTextRec lab))
                {
                    FillSlabInfoFromBlob(ref nb, lab.Text);
                    AttachUstKotFromPlanLabel(lab, null, ref nb);
                    nb.HasLabel = true;
                }
            }

            bool konsol = sinirKenar || yakinSinir || kisaBosKonsol || (tHit <= 220.0 && tekYuz);
            bool sameKot = SlabsContinue(self, nb);

            if (konsol)
            {
                isKonsol = true;
                konsolBoy = tHit;
                // Kot farklı: pilye uzamaz; üst ilave yine konsol (firkete) için boy döner
                if (!sameKot)
                {
                    LneighPilye = 0;
                    return true;
                }
                double maxUz = Math.Max(0.0, tHit - PaspayiCm);
                double uz;
                if (tHit < KonsolPilyeMinUzCm)
                    uz = maxUz;
                else
                    uz = Math.Min(maxUz, Math.Max(KonsolPilyeMinUzCm, tHit * 0.5));
                LneighPilye = uz * 4.0;
                return true;
            }
            if (!sameKot)
                return false;
            LneighPilye = tHit;
            return true;
        }

        /// <summary>origin'den dir yönünde ilk sınır (T &gt; minT).</summary>
        private static bool TryNearestBoundaryHit(
            BoundaryIndex bounds,
            Point3d origin,
            Vector3d dir,
            double maxLen,
            double minT,
            out double tHit,
            out string layer)
        {
            tHit = maxLen;
            layer = "";
            bool any = false;
            Point3d end = origin + dir * maxLen;
            var probeSeg = MakeProbeSeg(origin, end);
            foreach (var bd in bounds.Items)
            {
                var pts = new List<Point3d>();
                CollectCurveHitPoints(bd.Curve, probeSeg, pts);
                foreach (Point3d p in pts)
                {
                    double t = (p - origin).DotProduct(dir);
                    if (t < minT || t > tHit) continue;
                    tHit = t;
                    try { layer = bd.Curve.Layer ?? ""; } catch { layer = ""; }
                    any = true;
                }
            }
            return any;
        }

        private static LineSegment2d MakeProbeSeg(Point3d a, Point3d b)
        {
            return new LineSegment2d(new Point2d(a.X, a.Y), new Point2d(b.X, b.Y));
        }

        /// <summary>
        /// AutoCAD Entity.IntersectWith + bellek Line süreç düşürür; kesişim yalnız 2D geometri.
        /// </summary>
        private static void CollectCurveHitPoints(Curve c, LineSegment2d probeSeg, List<Point3d> dest)
        {
            if (c == null || dest == null) return;
            try
            {
                if (c is Polyline pl)
                {
                    CollectPolyline2dHits(pl, probeSeg, dest);
                    return;
                }
                AppendEntity2dHits(c, probeSeg, dest);
            }
            catch { }
        }

        private static void CollectPolyline2dHits(Polyline pl, LineSegment2d probeSeg, List<Point3d> dest)
        {
            int n = pl.NumberOfVertices;
            if (n < 2) return;
            int segs = pl.Closed ? n : n - 1;
            Point2d pr0 = probeSeg.StartPoint, pr1 = probeSeg.EndPoint;
            for (int i = 0; i < segs; i++)
            {
                try
                {
                    Point2d p0 = pl.GetPoint2dAt(i);
                    Point2d p1 = pl.GetPoint2dAt((i + 1) % n);
                    if (TrySegIntersect(p0, p1, pr0, pr1, out Point2d hit))
                        dest.Add(new Point3d(hit.X, hit.Y, 0));
                }
                catch { }
            }
        }

        private static void AppendEntity2dHits(Entity ent, LineSegment2d probeSeg, List<Point3d> dest)
        {
            Point2d pr0 = probeSeg.StartPoint, pr1 = probeSeg.EndPoint;
            try
            {
                if (ent is Line ln)
                {
                    if (TrySegIntersect(
                        new Point2d(ln.StartPoint.X, ln.StartPoint.Y),
                        new Point2d(ln.EndPoint.X, ln.EndPoint.Y),
                        pr0, pr1, out Point2d hit))
                        dest.Add(new Point3d(hit.X, hit.Y, 0));
                    return;
                }
                if (ent is Arc arc)
                {
                    if (TrySegIntersect(
                        new Point2d(arc.StartPoint.X, arc.StartPoint.Y),
                        new Point2d(arc.EndPoint.X, arc.EndPoint.Y),
                        pr0, pr1, out Point2d hitA))
                        dest.Add(new Point3d(hitA.X, hitA.Y, 0));
                    return;
                }
                if (ent is Circle cir)
                {
                    AddCircleSegHits(cir.Center.X, cir.Center.Y, cir.Radius, pr0, pr1, dest);
                }
            }
            catch { }
        }

        /// <summary>AutoCAD LineSegment2d.IntersectWith native göçme; yalnız C# doğru parçası.</summary>
        private static bool TrySegIntersect(Point2d a, Point2d b, Point2d c, Point2d d, out Point2d p)
        {
            p = new Point2d();
            double x1 = a.X, y1 = a.Y, x2 = b.X, y2 = b.Y;
            double x3 = c.X, y3 = c.Y, x4 = d.X, y4 = d.Y;
            if (double.IsNaN(x1) || double.IsNaN(y1) || double.IsNaN(x2) || double.IsNaN(y2)
                || double.IsNaN(x3) || double.IsNaN(y3) || double.IsNaN(x4) || double.IsNaN(y4))
                return false;
            double den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(den) < 1e-18) return false;
            double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / den;
            double u = -((x1 - x2) * (y1 - y3) - (y1 - y2) * (x1 - x3)) / den;
            if (t < -1e-8 || t > 1.0 + 1e-8 || u < -1e-8 || u > 1.0 + 1e-8) return false;
            double x = x1 + t * (x2 - x1);
            double y = y1 + t * (y2 - y1);
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y))
                return false;
            p = new Point2d(x, y);
            return true;
        }

        private static void AddCircleSegHits(
            double cx, double cy, double r, Point2d a, Point2d b, List<Point3d> dest)
        {
            if (r < 1e-6 || dest == null) return;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double fx = a.X - cx, fy = a.Y - cy;
            double A = dx * dx + dy * dy;
            if (A < 1e-18) return;
            double B = 2.0 * (fx * dx + fy * dy);
            double C = fx * fx + fy * fy - r * r;
            double disc = B * B - 4.0 * A * C;
            if (disc < 0) return;
            double s = Math.Sqrt(disc);
            foreach (double t in new[] { (-B - s) / (2.0 * A), (-B + s) / (2.0 * A) })
            {
                if (t < -1e-8 || t > 1.0 + 1e-8) continue;
                dest.Add(new Point3d(a.X + t * dx, a.Y + t * dy, 0));
            }
        }

        private static Point2d[] IntersectBulgeSeg(Point2d p0, Point2d p1, double bulge, LineSegment2d probeSeg)
        {
            if (TrySegIntersect(p0, p1, probeSeg.StartPoint, probeSeg.EndPoint, out Point2d hit))
                return new[] { hit };
            return null;
        }

        private static bool TryPreloadSlabGeometryAssemblies(Editor ed)
        {
            try
            {
                string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(dir)) return true;
                string[] files =
                {
                    "System.Runtime.CompilerServices.Unsafe.dll",
                    "System.Buffers.dll",
                    "System.Numerics.Vectors.dll",
                    "System.Memory.dll",
                    "Microsoft.Bcl.HashCode.dll",
                    "NetTopologySuite.dll"
                };
                foreach (string name in files)
                {
                    string p = Path.Combine(dir, name);
                    if (!File.Exists(p)) continue;
                    try { System.Reflection.Assembly.LoadFrom(p); } catch { }
                }
                Type t = typeof(NetTopologySuite.Geometries.GeometryFactory);
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
                return true;
            }
            catch (System.Exception ex)
            {
                try
                {
                    ed?.WriteMessage(
                        "\nDOSEMEDONATI: NetTopologySuite yuklenemedi ({0}). AutoCAD'i kapatip Setup-Ofis\\Kur.cmd ile yeniden kurun.",
                        ex.Message);
                }
                catch { }
                return false;
            }
        }

        private static int EstimateAdet(double clearLenCm, int aralikCm)
        {
            if (aralikCm <= 0) return 1;
            return Math.Max(1, (int)Math.Round(clearLenCm / aralikCm) + 1);
        }

        private static string FormatLabel(int adet, int capMm, int aralikCm, double boyCm, bool upper)
        {
            // DONATIBOYU 5 cm kuralı (artık ≤1 → alta, fazlası üste); çizgi boyu değişmez.
            int boy = (int)DonatiBoyuRunner.RoundLen(boyCm, DonatiBoyuYuvarlama.BesCm);
            string lMark = upper ? "L" : "l";
            // Serbest: adet yazılmaz → ø12/20 L=350. X/Y: GPR adedi durur.
            if (CizgiselMod && EtkinYon == DonatiYonu.Serbest)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "ø{0}/{1} {2}={3}", capMm, aralikCm, lMark, boy);
            }
            return string.Format(CultureInfo.InvariantCulture,
                "{0}ø{1}/{2} {3}={4}", adet, capMm, aralikCm, lMark, boy);
        }

        private static ObjectId PlaceLabel(
            Transaction tr,
            BlockTableRecord btr,
            Database db,
            Point3d barRef,
            string text,
            string layer,
            ObjectId textStyleId,
            Vector3d along,
            Vector3d up,
            bool belowBar = false,
            bool missingGprMatch = false)
        {
            double ang = ReadableTextAngle(along);

            var t = new DBText();
            t.SetDatabaseDefaults();
            t.TextString = text;
            t.Height = TextHeightCm;
            if (!textStyleId.IsNull)
                t.TextStyleId = textStyleId;
            t.Position = barRef;
            t.Rotation = 0;
            t.Layer = layer;
            // X/Y: eş GPR etiketi yoksa kırmızı (ByLayer değil)
            if (missingGprMatch && EtkinYon != DonatiYonu.Serbest)
                t.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
            t.HorizontalMode = TextHorizontalMode.TextCenter;
            t.VerticalMode = TextVerticalMode.TextBottom;
            t.AlignmentPoint = barRef;
            t.AdjustAlignment(db);
            btr.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);

            // Yazı kutusu kendi (döndürülmemiş) çerçevesinde oturtulur, sonra donatı açısıyla döndürülür.
            Vector3d textUp = new Vector3d(-Math.Sin(ang), Math.Cos(ang), 0);
            bool worldAbove = !belowBar;
            bool localAbove = textUp.DotProduct(up) >= 0 ? worldAbove : !worldAbove;
            try
            {
                Extents3d ext = t.GeometricExtents;
                double cx = (ext.MinPoint.X + ext.MaxPoint.X) * 0.5;
                double dx = barRef.X - cx;
                double dy = localAbove
                    ? (barRef.Y + LabelGapCm) - ext.MinPoint.Y
                    : (barRef.Y - LabelGapCm) - ext.MaxPoint.Y;
                t.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
            }
            catch { }
            if (Math.Abs(ang) > 1e-12)
                t.TransformBy(Matrix3d.Rotation(ang, Vector3d.ZAxis, barRef));
            return t.ObjectId;
        }

        /// <summary>Yazı açısı (-90°, 90°]; tam düşeyde +90 → yazı üstü −X.</summary>
        private static double ReadableTextAngle(Vector3d along)
        {
            double ang = Math.Atan2(along.Y, along.X);
            if (ang > Math.PI / 2.0 + 1e-9) ang -= Math.PI;
            if (ang <= -Math.PI / 2.0 + 1e-9) ang += Math.PI;
            return ang;
        }

        /// <summary>
        /// Yalnız GPR okuması. Çizim normale StabilizeUpNormal kalır.
        /// Y ve hat 90° üstündeyse bakış solu korunur.
        /// </summary>
        private static Vector3d BarUpNormal(Vector3d along)
        {
            Vector3d n = StabilizeUpNormal(along);
            if (EtkinYon != DonatiYonu.Y) return n;
            double ang = Math.Atan2(along.Y, along.X);
            if (ang > Math.PI / 2.0 + 1e-9)
                n = -n;
            return n;
        }

        /// <summary>
        /// Pilye +n (yukarı), düz −n (altında). Çizgi yönünden bağımsız: n mümkünse +Y'ye bakar.
        /// </summary>
        private static Vector3d StabilizeUpNormal(Vector3d along)
        {
            Vector3d u = along.GetNormal();
            Vector3d n = u.CrossProduct(Vector3d.ZAxis);
            if (n.Length < 1e-9)
                n = Vector3d.YAxis;
            else
                n = n.GetNormal();

            // "Yukarı" tercihi: önce +Y; tam düşey donatıda −X (ilave solda, pilye ortada, düz sağda)
            if (n.Y < -1e-9 || (Math.Abs(n.Y) < 1e-9 && n.X > 0))
                n = -n;
            return n;
        }

        private static ObjectId GetOrCreateYaziBeykentTextStyle(Transaction tr, Database db)
        {
            var txtTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (txtTable.Has(TextStyleYazi))
                return txtTable[TextStyleYazi];

            var rec = new TextStyleTableRecord { Name = TextStyleYazi };
            try
            {
                rec.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor(
                    "Bahnschrift Light Condensed", false, false, 0, 0);
            }
            catch
            {
                try
                {
                    rec.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor(
                        "Arial", false, false, 0, 0);
                }
                catch { }
            }
            try { rec.TextSize = 0.0; } catch { }
            try { rec.XScale = 1.0; } catch { }
            txtTable.UpgradeOpen();
            ObjectId id = txtTable.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            txtTable.DowngradeOpen();
            return id;
        }

        private enum StockParkKind { None, Unmatched, MoveDown, IlaveDown }

        private struct StockSeg
        {
            public ObjectId Id;
            public double Boy;
            public Point3d Mid;
        }

        /// <summary>
        /// Düz/ilave/montaj: 1200 cm'yi aşarsa TS 500 ℓb ile böler; son parça 200 cm altındaysa ilk parça 600.
        /// Pilye: 1200'ü aşarsa ek, pilye bükümünün ortasındadır (600/1200 kuralı yok).
        /// Kısa çubuk tek parça kalır.
        /// </summary>
        private static void DrawLabeledStock(
            Transaction tr,
            BlockTableRecord btr,
            Database db,
            IList<Point3d> path,
            int capMm,
            int adet,
            int aralik,
            Vector3d up,
            Vector3d textAlong,
            string donatiLayer,
            string yaziLayer,
            ObjectId yaziStyleId,
            bool upper,
            Point3d singleLabelAt,
            bool missingGprMatch,
            StockParkKind park,
            double minLen,
            bool pilyeOrtada = false)
        {
            if (path == null || path.Count < 2) return;
            IList<Point3d> drawPath = ClipPathToKatSinir(path);
            if (drawPath == null || drawPath.Count < 2) return;
            List<StockSeg> pieces = SplitPathToStock(tr, btr, donatiLayer, drawPath, capMm, up, pilyeOrtada);
            if (pieces.Count == 0) return;
            double cover = pieces.Count == 1 ? pieces[0].Boy : PathLengthOf(drawPath);
            if (cover < minLen) return;

            for (int i = 0; i < pieces.Count; i++)
            {
                StockSeg pc = pieces[i];
                Point3d at = pieces.Count == 1 ? singleLabelAt : pc.Mid;
                if (pieces.Count == 1 && _katSinirClamp != null && _katSinirClamp.Count > 0
                    && !PointInsideKatSinir(_katSinirClamp, at))
                    at = pc.Mid;
                string label = FormatLabel(adet, capMm, aralik, pc.Boy, upper);
                ObjectId lbl = PlaceLabel(tr, btr, db, at, label, yaziLayer, yaziStyleId, textAlong, up,
                    missingGprMatch: missingGprMatch);
                if (park == StockParkKind.Unmatched)
                    ParkUnmatched(tr, pc.Id, lbl);
                else if (park == StockParkKind.MoveDown)
                {
                    MarkPark(pc.Id);
                    MarkPark(lbl);
                }
                else if (park == StockParkKind.IlaveDown && missingGprMatch)
                    ParkIlaveAsagi(tr, pc.Id, lbl);
            }
        }

        private static List<StockSeg> SplitPathToStock(
            Transaction tr,
            BlockTableRecord btr,
            string layer,
            IList<Point3d> path,
            int capMm,
            Vector3d up,
            bool pilyeOrtada)
        {
            var done = new List<StockSeg>();
            if (path == null || path.Count < 2) return done;
            double[] cum;
            double total = PathStations(path, out cum);
            if (total < StockMinBarCm) return done;

            Vector3d n = up.Length > 1e-9 ? up.GetNormal() : Vector3d.YAxis;
            if (total <= StockLenCm + 1e-6)
            {
                ObjectId id = PlacePolyline(tr, btr, layer, ToArray(path));
                done.Add(new StockSeg { Id = id, Boy = total, Mid = PointOnPath(path, cum, total * 0.5) });
                return done;
            }

            // Düz/ilave: 1,5·ℓb. Pilye eki: yalnız burada 2·ℓb.
            double lap = pilyeOrtada ? StockLapCm(capMm, 2.0) : StockLapCm(capMm);
            List<(double from, double to)> spans;
            if (pilyeOrtada)
            {
                double mid = PilyeBendCenterStation(path, cum, total);
                double from2 = mid - lap * 0.5;
                double to1 = mid + lap * 0.5;
                if (from2 < 0) from2 = 0;
                if (to1 > total) to1 = total;
                if (to1 - from2 < StockMinBarCm)
                {
                    from2 = Math.Max(0, mid - StockMinBarCm);
                    to1 = Math.Min(total, mid + StockMinBarCm);
                }
                spans = new List<(double from, double to)> { (0, to1), (from2, total) };
            }
            else
            {
                var bodies = BuildStockPieceBodies(
                    total, lap, StockLenCm, StockLenCm, StockLenCm, 0, 0);
                spans = PlacePiecesWithLap(0, total, bodies, lap);
            }
            for (int i = 0; i < spans.Count; i++)
            {
                double a = spans[i].from;
                double b = spans[i].to;
                // Pilye: üst parça +3, alt parça üst bandın 3 cm altında (10 cm içinde).
                List<Point3d> pts = pilyeOrtada
                    ? PilyeLapPiece(
                        path, cum, a, b, n,
                        i > 0 ? PilyeLapCrankOffsetCm : 0,
                        i == 0 ? PilyeLapCrankOffsetCm : 0)
                    : PiecePoints(path, cum, a, b, i > 0, lap, n);
                if (pts.Count < 2) continue;
                ObjectId id = PlacePolyline(tr, btr, layer, pts.ToArray());
                done.Add(new StockSeg
                {
                    Id = id,
                    Boy = b - a,
                    Mid = PointOnPath(path, cum, 0.5 * (a + b))
                });
            }
            return done;
        }

        /// <summary>
        /// Pilye parçası: üst +3 cm, alt parça üst bandın 3 cm altında (topClear).
        /// Toplam pilye yüksekliği 10 cm bandını aşmaz.
        /// </summary>
        private static List<Point3d> PilyeLapPiece(
            IList<Point3d> pts, double[] cum, double a, double b, Vector3d up,
            double off, double topClear)
        {
            var stations = new List<double> { a, b };
            for (int i = 0; i < cum.Length; i++)
            {
                if (cum[i] > a + 0.05 && cum[i] < b - 0.05)
                    stations.Add(cum[i]);
            }
            stations.Sort();
            double nMin = 0, nMax = 0;
            bool haveN = false;
            for (int i = 0; i < pts.Count; i++)
            {
                double nv = pts[i].X * up.X + pts[i].Y * up.Y;
                if (!haveN) { nMin = nMax = nv; haveN = true; }
                else
                {
                    if (nv < nMin) nMin = nv;
                    if (nv > nMax) nMax = nv;
                }
            }
            double ceil = haveN ? nMax - Math.Max(0, topClear) : 0;
            var result = new List<Point3d>();
            double prev = -1;
            for (int i = 0; i < stations.Count; i++)
            {
                double s = stations[i];
                if (prev >= 0 && s - prev < 0.05) continue;
                Point3d p = PointOnPath(pts, cum, s);
                if (off > 0.5)
                    p += up * off;
                if (haveN)
                {
                    double nv = p.X * up.X + p.Y * up.Y;
                    if (nv > ceil) p -= up * (nv - ceil);
                    else if (nv < nMin) p += up * (nMin - nv);
                }
                result.Add(p);
                prev = s;
            }
            return result;
        }

        /// <summary>
        /// Yükselen 45° köşegenin ortası (pilye kırım noktası). Bindirme bu noktada ortalanır.
        /// </summary>
        private static double PilyeBendCenterStation(IList<Point3d> path, double[] cum, double total)
        {
            int iDown = -1, iUp = -1;
            for (int i = 1; i < path.Count; i++)
            {
                double len = cum[i] - cum[i - 1];
                if (len < 12.0 || len > 18.0) continue;
                if (iDown < 0) iDown = i;
                else iUp = i;
            }
            int kink = iUp > iDown ? iUp : iDown;
            if (kink > 0)
                return 0.5 * (cum[kink - 1] + cum[kink]);
            return total * 0.5;
        }

        private static Point3d[] ToArray(IList<Point3d> path)
        {
            var arr = new Point3d[path.Count];
            for (int i = 0; i < path.Count; i++) arr[i] = path[i];
            return arr;
        }

        private static double PathLengthOf(IList<Point3d> path)
        {
            double[] cum;
            return PathStations(path, out cum);
        }

        /// <summary>
        /// TS 500 9.1.2 kenetlenme (Konum II). TBDY 2018 7.2.6 aynı boyu gösterir.
        /// Varsayılan bindirme = 1,5·ℓb (düz). Pilye eki 2·ℓb için <paramref name="lbCarpan"/> = 2.
        /// ℓb = 0,12·φ·fyd/fctd ≥ 20φ. C30, B420C.
        /// </summary>
        private static double StockLapCm(int capMm, double lbCarpan = 1.5)
        {
            if (capMm < 6) capMm = 8;
            if (lbCarpan < 1.0) lbCarpan = 1.5;
            const double fck = 30.0;
            const double fyk = 420.0;
            double fyd = fyk / 1.15;
            double fctd = (0.35 * Math.Sqrt(fck)) / 1.50;
            if (fctd < 0.20) fctd = 0.20;
            double lbMm = 0.12 * capMm * fyd / fctd;
            double minMm = 20.0 * capMm;
            if (lbMm < minMm) lbMm = minMm;
            double lbCm = Math.Max(StockMinBarCm, Math.Round(lbMm / 10.0));
            double lap = Math.Round(lbCarpan * lbCm);
            if (lap < StockMinBarCm) lap = StockMinBarCm;
            if (lap > StockLenCm * 0.45) lap = StockLenCm * 0.45;
            return lap;
        }

        /// <summary>Gövde boyları: parça ≤ 1200; son &lt; 200 ise ilk parça 600.</summary>
        private static List<double> BuildStockPieceBodies(
            double S, double lap,
            double maxBodyFirst, double maxBodyMid, double maxBodyLast,
            double h0, double h1)
        {
            double advM = Math.Max(StockMinBarCm, maxBodyMid - lap);
            int n = 2;
            for (; n <= 64; n++)
            {
                double maxCover = maxBodyFirst + maxBodyLast + (n - 2) * maxBodyMid - (n - 1) * lap;
                if (maxCover >= S - 1e-6) break;
            }
            if (n > 64) n = 64;

            double lastBody = LastBodyAfterFullFront(S, maxBodyFirst, lap, advM, n);
            while (lastBody > maxBodyLast + 1e-6 && n < 64)
            {
                n++;
                lastBody = LastBodyAfterFullFront(S, maxBodyFirst, lap, advM, n);
            }

            double lastSteel = lastBody + h1;
            if (lastSteel >= StockMinRemnantCm - 1e-6 && lastBody <= maxBodyLast + 1e-6)
            {
                var lens = new List<double>(n) { maxBodyFirst };
                for (int i = 0; i < n - 2; i++)
                    lens.Add(maxBodyMid);
                lens.Add(ClampStockBody(lastBody, maxBodyLast));
                return lens;
            }

            double firstSteel = Math.Min(StockFirstPieceCm, StockLenCm);
            double firstBody = firstSteel - h0;
            if (firstBody < StockMinBarCm) firstBody = StockMinBarCm;
            if (firstBody > maxBodyFirst) firstBody = maxBodyFirst;

            double remCover = S - (firstBody - lap);
            if (remCover < StockMinBarCm)
                return new List<double> { ClampStockBody(S, Math.Min(maxBodyFirst, maxBodyLast)) };

            if (remCover <= maxBodyLast + 1e-6)
                return new List<double> { firstBody, ClampStockBody(remCover, maxBodyLast) };

            int m = 2;
            for (; m <= 64; m++)
            {
                double maxCover = (m - 1) * maxBodyMid + maxBodyLast - (m - 1) * lap;
                if (maxCover >= remCover - 1e-6) break;
            }
            if (m > 64) m = 64;

            double remLast = LastBodyAfterFullFront(remCover, maxBodyMid, lap, advM, m);
            while (remLast > maxBodyLast + 1e-6 && m < 64)
            {
                m++;
                remLast = LastBodyAfterFullFront(remCover, maxBodyMid, lap, advM, m);
            }

            var alt = new List<double>(m + 1) { firstBody };
            for (int i = 0; i < m - 1; i++)
                alt.Add(maxBodyMid);
            alt.Add(ClampStockBody(remLast, maxBodyLast));
            return alt;
        }

        private static double LastBodyAfterFullFront(
            double S, double firstBody, double lap, double advMid, int n)
        {
            double lastStart = firstBody - lap;
            for (int i = 0; i < n - 2; i++)
                lastStart += advMid;
            return S - lastStart;
        }

        private static double ClampStockBody(double body, double maxBody)
        {
            if (body < StockMinBarCm) return StockMinBarCm;
            if (body > maxBody) return maxBody;
            return body;
        }

        private static List<(double from, double to)> PlacePiecesWithLap(
            double a, double b, List<double> lens, double lap)
        {
            var segs = new List<(double from, double to)>(lens.Count);
            double pos = a;
            for (int i = 0; i < lens.Count; i++)
            {
                double from = pos;
                double to = from + lens[i];
                if (i == lens.Count - 1) to = b;
                if (to > b) to = b;
                if (to - from >= StockMinBarCm * 0.5)
                    segs.Add((from, to));
                if (i < lens.Count - 1)
                    pos = from + lens[i] - lap;
            }
            if (segs.Count == 0)
                segs.Add((a, b));
            else
            {
                var last = segs[segs.Count - 1];
                segs[segs.Count - 1] = (last.from, b);
            }
            return segs;
        }

        private static double PathStations(IList<Point3d> pts, out double[] cum)
        {
            cum = new double[pts.Count];
            double len = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                len += pts[i - 1].DistanceTo(pts[i]);
                cum[i] = len;
            }
            return len;
        }

        private static Point3d PointOnPath(IList<Point3d> pts, double[] cum, double s)
        {
            if (pts.Count == 0) return Point3d.Origin;
            if (s <= 0) return pts[0];
            if (s >= cum[cum.Length - 1]) return pts[pts.Count - 1];
            for (int i = 1; i < pts.Count; i++)
            {
                if (cum[i] + 1e-9 < s) continue;
                double seg = cum[i] - cum[i - 1];
                double t = seg < 1e-9 ? 0 : (s - cum[i - 1]) / seg;
                return pts[i - 1] + (pts[i] - pts[i - 1]) * t;
            }
            return pts[pts.Count - 1];
        }

        /// <summary>Sonraki parçanın bindirmesi 5 cm şaşırır, 20 cm'de eksene döner.</summary>
        private static List<Point3d> PiecePoints(
            IList<Point3d> pts, double[] cum,
            double a, double b, bool crank, double lap, Vector3d up)
        {
            double offEnd = a + lap;
            double axisBack = a + lap + StockCrankAlongCm;
            bool doCrank = crank
                && (b - a) >= lap + StockCrankAlongCm + StockMinBarCm
                && axisBack < b - StockMinBarCm * 0.5;
            var stations = new List<double> { a, b };
            if (doCrank)
            {
                stations.Add(offEnd);
                stations.Add(axisBack);
            }
            for (int i = 0; i < cum.Length; i++)
            {
                if (cum[i] > a + 0.05 && cum[i] < b - 0.05)
                    stations.Add(cum[i]);
            }
            stations.Sort();
            var result = new List<Point3d>();
            double prev = -1;
            Vector3d shift = up * StockCrankOffsetCm;
            for (int i = 0; i < stations.Count; i++)
            {
                double s = stations[i];
                if (prev >= 0 && s - prev < 0.05) continue;
                if (doCrank && s > offEnd + 1e-6 && s < axisBack - 1e-6) continue;
                Point3d p = PointOnPath(pts, cum, s);
                if (doCrank && s <= offEnd + 1e-6) p += shift;
                result.Add(p);
                prev = s;
            }
            return result;
        }

        private static ObjectId PlaceStraightBar(
            Transaction tr,
            BlockTableRecord btr,
            Point3d s,
            Point3d e,
            string layer)
        {
            return PlacePolyline(tr, btr, layer, s, e);
        }

        /// <summary>Düz çubuk; isteğe bağlı uç gönyeleri (−n: kesit içi / aşağı — +n pilye yüksekliği).</summary>
        private static ObjectId PlaceBarWithGonye(
            Transaction tr,
            BlockTableRecord btr,
            Point3d s,
            Point3d e,
            Vector3d up,
            bool gonyeStart,
            bool gonyeEnd,
            string layer,
            out double unfoldedLen)
        {
            return PlaceBarWithGonye(tr, btr, s, e, up, gonyeStart, gonyeEnd, GonyeCm, GonyeCm, layer, out unfoldedLen);
        }

        private static List<Point3d> BuildGonyePoints(
            Point3d s,
            Point3d e,
            Vector3d up,
            bool gonyeStart,
            bool gonyeEnd,
            double gonyeLenStart,
            double gonyeLenEnd)
        {
            Vector3d n = up.Length > 1e-9 ? up.GetNormal() : Vector3d.YAxis;
            Vector3d gonyeDir = -n;
            var pts = new List<Point3d>();
            if (gonyeStart) pts.Add(s + gonyeDir * gonyeLenStart);
            pts.Add(s);
            pts.Add(e);
            if (gonyeEnd) pts.Add(e + gonyeDir * gonyeLenEnd);
            return pts;
        }

        private static ObjectId PlaceBarWithGonye(
            Transaction tr,
            BlockTableRecord btr,
            Point3d s,
            Point3d e,
            Vector3d up,
            bool gonyeStart,
            bool gonyeEnd,
            double gonyeLenStart,
            double gonyeLenEnd,
            string layer,
            out double unfoldedLen)
        {
            List<Point3d> pts = BuildGonyePoints(s, e, up, gonyeStart, gonyeEnd, gonyeLenStart, gonyeLenEnd);
            ObjectId id = PlacePolyline(tr, btr, layer, pts.ToArray());
            unfoldedLen = PathLengthOf(pts);
            return id;
        }

        /// <summary>Yerleşmiş konsol çubuğu: boy ekseni uçları ve n ofseti (cm).</summary>
        private struct KonsolPlacedBar
        {
            public Point3d A;
            public Point3d B;
            public double AlongN;
        }

        /// <summary>
        /// İki konsol çubuğu boy ekseninde örtüşüyorsa ikincisini +5 cm şaşırt.
        /// Orta nokta mesafesine bakılmaz; uzun çubuklar da kayar.
        /// </summary>
        private static double ResolveKonsolAlongN(
            List<KonsolPlacedBar> placed,
            Point3d a,
            Point3d b,
            Vector3d u,
            double baseAlong)
        {
            double along = baseAlong;
            if (placed == null) return along;
            for (int guard = 0; guard < 6; guard++)
            {
                bool clash = false;
                foreach (KonsolPlacedBar p in placed)
                {
                    if (Math.Abs(along - p.AlongN) >= 2.5) continue;
                    if (!KonsolSegmentsOverlap(a, b, p.A, p.B, u, 15.0)) continue;
                    along = p.AlongN + StaggerCm;
                    clash = true;
                    break;
                }
                if (!clash) break;
            }
            placed.Add(new KonsolPlacedBar { A = a, B = b, AlongN = along });
            return along;
        }

        private static bool KonsolSegmentsOverlap(
            Point3d a0, Point3d a1, Point3d b0, Point3d b1, Vector3d u, double minOverlapCm)
        {
            Vector3d uu = u.Length > 1e-9 ? u.GetNormal() : Vector3d.XAxis;
            double Proj(Point3d p) => p.X * uu.X + p.Y * uu.Y;
            double aLo = Math.Min(Proj(a0), Proj(a1));
            double aHi = Math.Max(Proj(a0), Proj(a1));
            double bLo = Math.Min(Proj(b0), Proj(b1));
            double bHi = Math.Max(Proj(b0), Proj(b1));
            return Math.Min(aHi, bHi) - Math.Max(aLo, bLo) >= minOverlapCm;
        }

        /// <summary>
        /// Sanal hat kiriş+konsol/boşluk kenarı kesince üst konsol ilave/firkete.
        /// Aynı üst kot arka döşeme varsa L/4 uzat (mesnet içinde gönye yok); yoksa kiriş + 40 cm gönye.
        /// </summary>
        private static void PlaceSoloKonsolIlaves(
            Transaction tr,
            BlockTableRecord btr,
            Database db,
            List<SpanRec> allSpans,
            BoundaryIndex boundaries,
            List<ClosedArea> closedAreas,
            List<SlabTextRec> slabTexts,
            List<GprSteelRec> gprNotes,
            Vector3d u,
            Vector3d n,
            double offsetAlongNCm,
            string donatiLayer,
            string yaziLayer,
            ObjectId yaziStyleId,
            List<KonsolPlacedBar> placedKonsol)
        {
            if (allSpans == null || allSpans.Count == 0) return;

            Vector3d KonsolOff(Point3d tip, Point3d mainEnd)
            {
                double along = ResolveKonsolAlongN(placedKonsol, tip, mainEnd, u, offsetAlongNCm);
                return n * along;
            }

            bool CoveredByPickedNeighbor(SpanRec d)
            {
                for (int j = 0; j < allSpans.Count; j++)
                {
                    SpanRec s = allSpans[j];
                    if (s.DuzOnly || !s.InPick) continue;
                    if (s.KonsolNext
                        && (s.RightOuter.DistanceTo(d.LeftInner) <= 8.0
                            || s.RightOuter.DistanceTo(d.LeftOuter) <= 8.0))
                        return true;
                    if (s.KonsolPrev
                        && (s.LeftOuter.DistanceTo(d.RightInner) <= 8.0
                            || s.LeftOuter.DistanceTo(d.RightOuter) <= 8.0))
                        return true;
                }
                return false;
            }

            // Aynı kot komşu açıklık boyu — önce allSpans (boşluk sınırı TryNeighbor'ı bozar).
            // Tam (iki mesnet) açıklık tercih; yoksa bitişik DuzOnly.
            bool TryAllSpansNeighborLen(SpanRec span, bool mesnetOnLeft, out double Lback)
            {
                Lback = 0;
                double bestFull = 0;
                double bestAny = 0;
                for (int j = 0; j < allSpans.Count; j++)
                {
                    SpanRec o = allSpans[j];
                    if (!o.InPick) continue;
                    bool adj;
                    if (mesnetOnLeft)
                    {
                        if (!o.RightIsMesnet || !span.LeftIsMesnet) continue;
                        adj = o.RightOuter.DistanceTo(span.LeftOuter) <= 15.0
                            || o.RightInner.DistanceTo(span.LeftInner) <= 15.0
                            || (o.SupportRight >= 0 && o.SupportRight == span.SupportLeft)
                            || (span.SupportLeft >= 0 && o.SupportLeft + 1 == span.SupportLeft
                                && !o.DuzOnly);
                    }
                    else
                    {
                        if (!o.LeftIsMesnet || !span.RightIsMesnet) continue;
                        adj = o.LeftOuter.DistanceTo(span.RightOuter) <= 15.0
                            || o.LeftInner.DistanceTo(span.RightInner) <= 15.0
                            || (span.SupportRight >= 0 && o.SupportLeft == span.SupportRight)
                            || (span.SupportRight >= 0 && span.SupportLeft >= 0
                                && span.SupportLeft + 1 == o.SupportLeft && !span.DuzOnly);
                    }
                    if (!adj) continue;
                    if (!SlabsContinue(span.Slab, o.Slab)) continue;
                    if (o.L <= 1.0) continue;
                    if (!o.DuzOnly && o.L > bestFull) bestFull = o.L;
                    if (o.L > bestAny) bestAny = o.L;
                }
                Lback = bestFull > 1.0 ? bestFull : bestAny;
                return Lback > 1.0;
            }

            foreach (SpanRec span in allSpans)
            {
                if (!span.InPick || !span.DuzOnly) continue;
                if (!span.LeftIsMesnet && !span.RightIsMesnet) continue;
                if (span.LeftIsMesnet && span.RightIsMesnet) continue;
                if (CoveredByPickedNeighbor(span)) continue;

                Point3d tip, beamFar, probeOrigin;
                Vector3d probeDir;
                bool mesnetOnLeft = span.LeftIsMesnet && !span.RightIsMesnet;
                if (mesnetOnLeft)
                {
                    // Mesnet solda, konsol/boşluk sağda → arka döşeme sola (−u)
                    tip = span.RightInner - u * PaspayiCm;
                    beamFar = span.LeftOuter + u * PaspayiCm;
                    probeOrigin = span.LeftOuter;
                    probeDir = -u;
                }
                else
                {
                    // Mesnet sağda, konsol/boşluk solda → arka döşeme sağa (+u)
                    tip = span.LeftInner + u * PaspayiCm;
                    beamFar = span.RightOuter - u * PaspayiCm;
                    probeOrigin = span.RightOuter;
                    probeDir = u;
                }

                // Aynı kot arka döşeme → L/4; değilse kirişte 40 cm gönye
                Point3d mainEnd = beamFar;
                double kirisGonye = KonsolKirisGonyeCm;
                double Lback = 0;
                bool foundBack = TryAllSpansNeighborLen(span, mesnetOnLeft, out Lback);
                if (!foundBack && boundaries != null
                    && TryNeighborSlabBeyond(
                        boundaries, probeOrigin, probeDir, n, closedAreas, slabTexts, gprNotes,
                        span.Slab, span.L, out Lback, out bool backIsKonsol, out _)
                    && !backIsKonsol && Lback > 1.0)
                    foundBack = true;

                if (foundBack && Lback > 1.0)
                {
                    mainEnd = probeOrigin + probeDir * (Lback * 0.25);
                    kirisGonye = 0;
                }

                int cap = span.Slab.CapMm > 0 ? span.Slab.CapMm : 8;
                int aralik = span.Slab.AralikCm > 0 ? span.Slab.AralikCm : 20;
                double Lbar = tip.DistanceTo(mainEnd);
                int adet = span.Slab.Adet ?? EstimateAdet(Math.Max(Lbar, span.L), aralik);
                // tip sağda → sağ ilave; tip solda → sol ilave
                bool sagSide = span.LeftIsMesnet && !span.RightIsMesnet;
                bool ilaveGpr = ApplyKonsolSteelWithGprGate(
                    span.Slab.GprSteels, Mid(tip, mainEnd), sagSide,
                    span.L, Lbar, ref adet, ref cap, ref aralik);

                Vector3d off = KonsolOff(tip, mainEnd);
                List<Point3d> fig;
                if (span.L <= KonsolPilyeMinUzCm)
                    fig = BuildFirketePoints(tip, beamFar, mainEnd, n, off, kirisGonye);
                else if (span.LeftIsMesnet)
                    fig = BuildGonyePoints(mainEnd + off, tip + off, n,
                        kirisGonye > 0.5, true,
                        kirisGonye > 0.5 ? kirisGonye : GonyeCm,
                        KonsolIlaveGonyeCm);
                else
                    fig = BuildGonyePoints(tip + off, mainEnd + off, n,
                        true, kirisGonye > 0.5,
                        KonsolIlaveGonyeCm,
                        kirisGonye > 0.5 ? kirisGonye : GonyeCm);
                DrawLabeledStock(
                    tr, btr, db, fig, cap, adet, aralik, n, u,
                    donatiLayer, yaziLayer, yaziStyleId, true,
                    Mid(tip + off, mainEnd + off),
                    !ilaveGpr, StockParkKind.IlaveDown, MinSpanCm * 0.25);
            }
        }

        /// <summary>
        /// Konsol ≤50: üst ilave firkete — üst bacak, 10 cm kenar gönye, dönüş kiriş uzak ucuna paspayı.
        /// beamTopGonyeCm &gt; 0: kiriş içi üst uca gönye (kot farkı / arkası boş → 40 cm).
        /// </summary>
        private static List<Point3d> BuildFirketePoints(
            Point3d tip,
            Point3d beamFar,
            Point3d mainEnd,
            Vector3d up,
            Vector3d off,
            double beamTopGonyeCm)
        {
            Vector3d nn = up.Length > 1e-9 ? up.GetNormal() : Vector3d.YAxis;
            Point3d topMain = mainEnd + off;
            Point3d topTip = tip + off;
            Point3d botTip = tip + off - nn * KonsolIlaveGonyeCm;
            Point3d botFar = beamFar + off - nn * KonsolIlaveGonyeCm;
            var pts = new List<Point3d>();
            if (beamTopGonyeCm > 0.5)
                pts.Add(topMain - nn * beamTopGonyeCm);
            pts.Add(topMain);
            pts.Add(topTip);
            pts.Add(botTip);
            pts.Add(botFar);
            return pts;
        }

        private static ObjectId PlaceKonsolFirketeIlave(
            Transaction tr,
            BlockTableRecord btr,
            Point3d tip,
            Point3d beamFar,
            Point3d mainEnd,
            Vector3d up,
            Vector3d off,
            double beamTopGonyeCm,
            string layer,
            out double unfoldedLen)
        {
            List<Point3d> pts = BuildFirketePoints(tip, beamFar, mainEnd, up, off, beamTopGonyeCm);
            ObjectId id = PlacePolyline(tr, btr, layer, pts.ToArray());
            unfoldedLen = PathLengthOf(pts);
            return id;
        }

        private static ObjectId PlacePolyline(
            Transaction tr,
            BlockTableRecord btr,
            string layer,
            params Point3d[] pts)
        {
            if (pts == null || pts.Length < 2) return ObjectId.Null;
            Polyline pl = null;
            try
            {
                pl = new Polyline();
                pl.SetDatabaseDefaults();
                int vi = 0;
                if (!TryAddPolyVertex(pl, ref vi, To2d(pts[0]), 0))
                {
                    pl.Dispose();
                    return ObjectId.Null;
                }
                for (int i = 1; i < pts.Length - 1; i++)
                {
                    Vector2d dIn = To2d(pts[i]) - To2d(pts[i - 1]);
                    Vector2d dOut = To2d(pts[i + 1]) - To2d(pts[i]);
                    double lIn = dIn.Length, lOut = dOut.Length;
                    if (lIn < 0.05 || lOut < 0.05)
                        continue;
                    dIn = dIn / lIn;
                    dOut = dOut / lOut;
                    double cross = dIn.X * dOut.Y - dIn.Y * dOut.X;
                    double dot = dIn.DotProduct(dOut);
                    if (double.IsNaN(dot)) continue;
                    dot = Math.Max(-1.0, Math.Min(1.0, dot));
                    double theta = Math.Acos(dot);
                    if (double.IsNaN(theta) || theta < 1e-3)
                    {
                        TryAddPolyVertex(pl, ref vi, To2d(pts[i]), 0);
                        continue;
                    }
                    double trim = BendRadiusCm * Math.Tan(theta / 2.0);
                    double maxTrim = Math.Min(lIn, lOut) * 0.45;
                    if (double.IsNaN(trim) || double.IsInfinity(trim) || trim > maxTrim) trim = maxTrim;
                    if (trim < 0.05)
                    {
                        TryAddPolyVertex(pl, ref vi, To2d(pts[i]), 0);
                        continue;
                    }
                    Point2d a = To2d(pts[i]) - dIn * trim;
                    Point2d b = To2d(pts[i]) + dOut * trim;
                    double bulge = Math.Tan(theta / 4.0) * (cross >= 0 ? 1.0 : -1.0);
                    if (a.GetDistanceTo(b) < 0.2 || double.IsNaN(bulge) || Math.Abs(bulge) > 1.5)
                    {
                        TryAddPolyVertex(pl, ref vi, To2d(pts[i]), 0);
                        continue;
                    }
                    if (!TryAddPolyVertex(pl, ref vi, a, bulge)) continue;
                    TryAddPolyVertex(pl, ref vi, b, 0);
                }
                TryAddPolyVertex(pl, ref vi, To2d(pts[pts.Length - 1]), 0);
                if (vi < 2)
                {
                    pl.Dispose();
                    return ObjectId.Null;
                }
                pl.Closed = false;
                pl.Layer = layer;
                btr.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                return pl.ObjectId;
            }
            catch
            {
                try
                {
                    if (pl != null && pl.ObjectId.IsNull)
                        pl.Dispose();
                }
                catch { }
                return ObjectId.Null;
            }
        }

        private static bool TryAddPolyVertex(Polyline pl, ref int vi, Point2d p, double bulge)
        {
            if (pl == null || double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y))
                return false;
            if (vi > 0 && pl.GetPoint2dAt(vi - 1).GetDistanceTo(p) < 0.05)
                return false;
            pl.AddVertexAt(vi, p, bulge, 0, 0);
            vi++;
            return true;
        }

        /// <summary>
        /// Pilye: mesnetlerde yüksek, açıklık ortasında alçak.
        /// Kırım 10 cm (doğrultu) × 10 cm (dik) = 45°, iç yüzden L·2/9'da başlar.
        /// Devam varsa komşu L/4; yoksa uçta 20 cm gönye.
        /// </summary>
        private static bool TryBuildPilyeSpan(
            Point3d leftInner,
            Point3d rightInner,
            Point3d leftOuter,
            Point3d rightOuter,
            Point3d barL,
            Point3d barR,
            Vector3d u,
            Vector3d n,
            double L,
            double Lprev,
            double Lnext,
            bool hasPrev,
            bool hasNext,
            Vector3d lowOff,
            out double unfoldedLen,
            out List<Point3d> path)
        {
            unfoldedLen = L;
            path = null;
            double along = PilyeKirimAlongCm;
            double rise = PilyeKirimRiseCm;
            if (L < 4.0 * along + 1.0)
                return false;

            double q = L * PilyeKirimOran;
            Vector3d highOff = lowOff + n * rise;

            // Devam: komşu açıklık L/4 (dış yüzden). Yoksa: dış−paspayı + gönye.
            Point3d p0 = hasPrev
                ? leftOuter - u * (Lprev * 0.25) + highOff
                : barL + highOff;
            Point3d p5 = hasNext
                ? rightOuter + u * (Lnext * 0.25) + highOff
                : barR + highOff;

            Point3d p1 = leftInner + u * q + highOff;
            Point3d p2 = p1 + u * along - n * rise;
            Point3d p4 = rightInner - u * q + highOff;
            Point3d p3 = p4 - u * along - n * rise;

            if ((p3 - p2).DotProduct(u) < along * 0.5)
                return false;

            var pts = new List<Point3d>();
            // Gönye −n: pilye yüksekliğinin tersi (kesit içi; +n kiriş→döşeme üstü açıkta kalır)
            if (!hasPrev) pts.Add(p0 - n * GonyeCm);
            pts.Add(p0);
            pts.Add(p1);
            pts.Add(p2);
            pts.Add(p3);
            pts.Add(p4);
            pts.Add(p5);
            if (!hasNext) pts.Add(p5 - n * GonyeCm);

            unfoldedLen = 0;
            for (int i = 0; i < pts.Count - 1; i++)
                unfoldedLen += pts[i].DistanceTo(pts[i + 1]);

            path = pts;
            return true;
        }

        /// <summary>
        /// İlave üst: aynı üst kot → tek çubuk L/4+L/4.
        /// İki döşeme farklı üst kot → her döşeme için ayrı ilave (kendi L/4 + mesnette gönye).
        /// </summary>
        private static void PlaceAllUstIlave(
            Transaction tr,
            BlockTableRecord btr,
            Database db,
            List<SupportCluster> supports,
            List<SpanRec> allSpans,
            HashSet<int> drawnSupportLeft,
            Vector3d u,
            Vector3d n,
            string donatiLayer,
            string yaziLayer,
            ObjectId yaziStyleId,
            double offsetAlongNCm,
            List<(int supportLeft, SlabInfo slab, int adet, int cap, int aralik)> spanSlabs,
            List<KonsolPlacedBar> placedKonsol,
            List<GprSteelRec> gprNotes,
            List<RawSeg> mesnetSegs)
        {
            if (supports == null || supports.Count == 0 || allSpans == null || allSpans.Count == 0)
                return;
            Vector3d off = n * offsetAlongNCm;
            Vector3d off2 = n * (offsetAlongNCm + StaggerCm); // çift ilave görünsün

            SpanRec? SpanEndingAt(int supportRight)
            {
                foreach (var sp in allSpans)
                {
                    if (sp.DuzOnly) continue; // konsol/boşluk parçası — yalnız PlaceSoloKonsolIlaves
                    if (sp.SupportRight == supportRight) return sp;
                    if (sp.SupportRight < 0 && sp.RightIsMesnet && sp.SupportLeft >= 0
                        && sp.SupportLeft + 1 == supportRight)
                        return sp;
                }
                return null;
            }
            SpanRec? SpanStartingAt(int supportLeft)
            {
                foreach (var sp in allSpans)
                {
                    if (sp.DuzOnly) continue;
                    if (sp.SupportLeft == supportLeft && sp.LeftIsMesnet) return sp;
                }
                return null;
            }

            // Yalnız orta delik (FreeEndBosluk): PlaceSolo konsol atar → ilave yok.
            // Dış SINIRI konsol burada yok sayılır (kNext / PlaceSolo kendi yolundan gider).
            bool SoloHoleAt(int supportIdx, bool onRightOfSupport)
            {
                foreach (var sp in allSpans)
                {
                    if (!sp.InPick || !sp.DuzOnly || !sp.FreeEndBosluk) continue;
                    if (sp.LeftIsMesnet == sp.RightIsMesnet) continue;
                    if (onRightOfSupport)
                    {
                        if (sp.LeftIsMesnet && sp.SupportLeft == supportIdx) return true;
                    }
                    else
                    {
                        if (sp.RightIsMesnet && sp.SupportRight == supportIdx) return true;
                    }
                }
                return false;
            }

            // Önce topla: kısa (< 2 m) açıklığın iki yanındaki ilaveler tek çubukta birleşir
            var segs = new List<IlaveSeg>();
            void PlaceOne(
                Point3d start,
                Point3d end,
                bool gonyeStart,
                bool gonyeEnd,
                int adet,
                int cap,
                int aralik,
                Vector3d segOff,
                int startSpan,
                int endSpan,
                bool gprMatched)
            {
                segs.Add(new IlaveSeg
                {
                    Start = start, End = end, GonyeStart = gonyeStart, GonyeEnd = gonyeEnd,
                    Adet = adet, Cap = cap, Aralik = aralik, Off = segOff,
                    StartSpan = startSpan, EndSpan = endSpan,
                    MissingGprMatch = !gprMatched
                });
            }

            void PickSteel(SpanRec sp, out int adet, out int cap, out int aralik)
            {
                adet = sp.Slab.Adet ?? EstimateAdet(sp.L, sp.Slab.AralikCm > 0 ? sp.Slab.AralikCm : 20);
                cap = sp.Slab.CapMm > 0 ? sp.Slab.CapMm : 8;
                aralik = sp.Slab.AralikCm > 0 ? sp.Slab.AralikCm : 20;
                if (spanSlabs != null)
                {
                    foreach (var x in spanSlabs)
                    {
                        if (x.supportLeft == sp.SupportLeft)
                        {
                            adet = x.adet;
                            cap = x.cap;
                            aralik = x.aralik;
                            break;
                        }
                    }
                }
            }

            /// <summary>sagSide: açıklığın sağ ucundaki ilave (sağ ila GPR). false → sol ila.</summary>
            bool PickIlaveSteel(SpanRec sp, bool sagSide, double adetRefLen, out int adet, out int cap, out int aralik)
            {
                PickSteel(sp, out adet, out cap, out aralik);
                Point3d near = sagSide ? sp.RightInner : sp.LeftInner;
                return TryApplyGprIlaveSteel(sp.Slab.GprSteels, near, sagSide, adetRefLen, ref adet, ref cap, ref aralik);
            }

            bool PickKonsolIlaveSteel(
                SpanRec sp, bool sagSide, double konsolBoy, double adetRefLen,
                out int adet, out int cap, out int aralik)
            {
                PickSteel(sp, out adet, out cap, out aralik);
                Point3d near = sagSide ? sp.RightInner : sp.LeftInner;
                return ApplyKonsolSteelWithGprGate(
                    sp.Slab.GprSteels, near, sagSide, konsolBoy, adetRefLen,
                    ref adet, ref cap, ref aralik);
            }

            for (int s = 0; s < supports.Count; s++)
            {
                SpanRec? leftSp = SpanEndingAt(s);
                SpanRec? rightSp = SpanStartingAt(s);
                if (!leftSp.HasValue && !rightSp.HasValue) continue;

                bool leftDrawn = leftSp.HasValue && drawnSupportLeft != null &&
                    drawnSupportLeft.Contains(leftSp.Value.SupportLeft) && leftSp.Value.ReachRight;
                bool rightDrawn = rightSp.HasValue && drawnSupportLeft != null &&
                    drawnSupportLeft.Contains(rightSp.Value.SupportLeft) && rightSp.Value.ReachLeft;
                // Komşu etiketli döşeme (seçim dışı dahil) — farklı kotta çift ilave için gerekli
                if (!leftDrawn && !rightDrawn) continue;
                if (!leftSp.HasValue && !rightDrawn) continue;
                if (!rightSp.HasValue && !leftDrawn) continue;

                // Orta delik konsolu varsa ilave yok (dış SINIRI konsola dokunma)
                if (leftDrawn && !rightDrawn && SoloHoleAt(s, onRightOfSupport: true))
                    continue;
                if (rightDrawn && !leftDrawn && SoloHoleAt(s, onRightOfSupport: false))
                    continue;

                bool contBoth = leftSp.HasValue && rightSp.HasValue &&
                    SlabsContinue(leftSp.Value.Slab, rightSp.Value.Slab);

                // İki döşeme var, üst kot farklı → çift ilave; kısa (≤50) taraf firkete
                if (leftSp.HasValue && rightSp.HasValue && !contBoth)
                {
                    if (leftDrawn)
                    {
                        // Sol açıklığın sağ ucu → sağ ilave
                        if (leftSp.Value.L <= KonsolPilyeMinUzCm)
                        {
                            bool gprOk = PickKonsolIlaveSteel(leftSp.Value, sagSide: true, leftSp.Value.L, leftSp.Value.L,
                                out int adet, out int cap, out int aralik);
                            Point3d tip = supports[s].FaceMin - u * Math.Max(0.0, leftSp.Value.L - PaspayiCm);
                            Point3d beamFar = supports[s].FaceMax - u * PaspayiCm;
                            if (placedKonsol != null)
                                placedKonsol.Add(new KonsolPlacedBar { A = tip, B = beamFar, AlongN = offsetAlongNCm });
                            DrawLabeledStock(
                                tr, btr, db,
                                BuildFirketePoints(tip, beamFar, beamFar, n, off, KonsolKirisGonyeCm),
                                cap, adet, aralik, n, u,
                                donatiLayer, yaziLayer, yaziStyleId, true,
                                Mid(tip + off, beamFar + off),
                                !gprOk, StockParkKind.IlaveDown, MinSpanCm * 0.25);
                        }
                        else
                        {
                            bool gprOk = PickIlaveSteel(leftSp.Value, sagSide: true, leftSp.Value.L * 0.25,
                                out int adet, out int cap, out int aralik);
                            Point3d start = supports[s].FaceMin - u * (leftSp.Value.L * 0.25) + off;
                            Point3d end = supports[s].FaceMax - u * PaspayiCm + off;
                            PlaceOne(start, end, gonyeStart: false, gonyeEnd: true, adet, cap, aralik,
                                off, leftSp.Value.SupportLeft, -1, gprOk);
                        }
                    }
                    if (rightDrawn)
                    {
                        // Sağ açıklığın sol ucu → sol ilave
                        if (rightSp.Value.L <= KonsolPilyeMinUzCm)
                        {
                            bool gprOk = PickKonsolIlaveSteel(rightSp.Value, sagSide: false, rightSp.Value.L, rightSp.Value.L,
                                out int adet, out int cap, out int aralik);
                            Point3d tip = supports[s].FaceMax + u * Math.Max(0.0, rightSp.Value.L - PaspayiCm);
                            Point3d beamFar = supports[s].FaceMin + u * PaspayiCm;
                            if (placedKonsol != null)
                                placedKonsol.Add(new KonsolPlacedBar { A = tip, B = beamFar, AlongN = offsetAlongNCm + StaggerCm });
                            DrawLabeledStock(
                                tr, btr, db,
                                BuildFirketePoints(tip, beamFar, beamFar, n, off2, KonsolKirisGonyeCm),
                                cap, adet, aralik, n, u,
                                donatiLayer, yaziLayer, yaziStyleId, true,
                                Mid(tip + off2, beamFar + off2),
                                !gprOk, StockParkKind.IlaveDown, MinSpanCm * 0.25);
                        }
                        else
                        {
                            bool gprOk = PickIlaveSteel(rightSp.Value, sagSide: false, rightSp.Value.L * 0.25,
                                out int adet, out int cap, out int aralik);
                            Point3d start = supports[s].FaceMin + u * PaspayiCm + off2;
                            Point3d end = supports[s].FaceMax + u * (rightSp.Value.L * 0.25) + off2;
                            PlaceOne(start, end, gonyeStart: true, gonyeEnd: false, adet, cap, aralik,
                                off2, -1, rightSp.Value.SupportLeft, gprOk);
                        }
                    }
                    continue;
                }

                bool leftOk = leftSp.HasValue && (!rightSp.HasValue || contBoth);
                bool rightOk = rightSp.HasValue && (!leftSp.HasValue || contBoth);
                // allSpans'te yok ama öte yanda aynı üst kot (ExtPrev/ExtNext) — konsol ayrı
                bool kPrev = !leftSp.HasValue && rightDrawn && rightSp.HasValue
                    && rightSp.Value.ExtPrev && rightSp.Value.KonsolPrev;
                bool kNext = !rightSp.HasValue && leftDrawn && leftSp.HasValue
                    && leftSp.Value.ExtNext && leftSp.Value.KonsolNext;
                // Ext ile öte yan: boşluk parçasına sürekli ilave yok (PlaceSolo); dış konsol kNext ile kalır
                if (!leftOk && rightDrawn && rightSp.HasValue && rightSp.Value.ExtPrev && !rightSp.Value.KonsolPrev
                    && !SoloHoleAt(s, onRightOfSupport: false))
                    leftOk = true;
                if (!rightOk && leftDrawn && leftSp.HasValue && leftSp.Value.ExtNext && !leftSp.Value.KonsolNext
                    && !SoloHoleAt(s, onRightOfSupport: true))
                    rightOk = true;

                double Ll = leftSp.HasValue ? leftSp.Value.L
                    : (rightSp.HasValue && rightSp.Value.ExtPrev && !rightSp.Value.KonsolPrev
                        ? rightSp.Value.LprevExt : 0);
                double Lr = rightSp.HasValue ? rightSp.Value.L
                    : (leftSp.HasValue && leftSp.Value.ExtNext && !leftSp.Value.KonsolNext
                        ? leftSp.Value.LnextExt : 0);

                int ad = 1, cp = 8, ar = 20;
                bool contGpr = false;
                GprSteelRec gL = default, gR = default;
                bool hasL = leftSp.HasValue && TryGetClassedIlave(leftSp.Value, true, out gL);
                bool hasR = rightSp.HasValue && TryGetClassedIlave(rightSp.Value, false, out gR);
                if (hasL && hasR)
                {
                    double asL = AsPerMeterCm2(gL.CapMm, gL.AralikCm);
                    double asR = AsPerMeterCm2(gR.CapMm, gR.AralikCm);
                    GprSteelRec win = asL >= asR ? gL : gR;
                    if (win.CapMm > 0) cp = win.CapMm;
                    if (win.AralikCm > 0) ar = win.AralikCm;
                    if (win.Adet > 0) ad = win.Adet;
                    else if (ar > 0)
                        ad = Math.Max(1, (int)Math.Round(Math.Max(Ll, Lr) * 0.25 / ar) + 1);
                    MarkPark(gL.Id);
                    MarkPark(gR.Id);
                    contGpr = true;
                }
                else if (leftDrawn && leftSp.HasValue)
                    contGpr = PickIlaveSteel(leftSp.Value, sagSide: true, Math.Max(Ll, Lr) * 0.25, out ad, out cp, out ar);
                else if (rightDrawn && rightSp.HasValue)
                    contGpr = PickIlaveSteel(rightSp.Value, sagSide: false, Math.Max(Ll, Lr) * 0.25, out ad, out cp, out ar);

                if (kPrev || kNext)
                {
                    if (kPrev)
                    {
                        double boyK = rightSp.Value.KonsolBoyPrev;
                        Point3d tip = supports[s].FaceMin - u * Math.Max(0.0, boyK - PaspayiCm);
                        Point3d beamFar = supports[s].FaceMax - u * PaspayiCm;
                        // Aynı üst kot → açıklığa L/4; kot farkı / arkası boş → kiriş + 40 cm gönye
                        bool devamAciklik = rightOk && rightSp.Value.LprevExt > 1.0;
                        Point3d mainEnd = devamAciklik
                            ? supports[s].FaceMax + u * (rightSp.Value.L * 0.25)
                            : beamFar;
                        double kirisGonye = devamAciklik ? 0.0 : KonsolKirisGonyeCm;
                        // Örtüşen ikinci konsol +5 cm (aynı mesnet veya uzun çubuk)
                        Vector3d offK1 = n * ResolveKonsolAlongN(placedKonsol, tip, mainEnd, u, offsetAlongNCm);
                        // Konsol solda (prev) → sol ilave
                        bool gprOk = PickKonsolIlaveSteel(rightSp.Value, sagSide: false, boyK, tip.DistanceTo(mainEnd),
                            out ad, out cp, out ar);
                        if (boyK <= KonsolPilyeMinUzCm)
                        {
                            DrawLabeledStock(
                                tr, btr, db,
                                BuildFirketePoints(tip, beamFar, mainEnd, n, offK1, kirisGonye),
                                cp, ad, ar, n, u,
                                donatiLayer, yaziLayer, yaziStyleId, true,
                                Mid(tip + offK1, mainEnd + offK1),
                                !gprOk, StockParkKind.IlaveDown, MinSpanCm * 0.25);
                        }
                        else
                        {
                            DrawLabeledStock(
                                tr, btr, db,
                                BuildGonyePoints(tip + offK1, mainEnd + offK1, n,
                                    true, kirisGonye > 0.5,
                                    KonsolIlaveGonyeCm,
                                    kirisGonye > 0.5 ? kirisGonye : GonyeCm),
                                cp, ad, ar, n, u,
                                donatiLayer, yaziLayer, yaziStyleId, true,
                                Mid(tip + offK1, mainEnd + offK1),
                                !gprOk, StockParkKind.IlaveDown, MinSpanCm * 0.25);
                        }
                    }
                    if (kNext)
                    {
                        double boyK = leftSp.Value.KonsolBoyNext;
                        Point3d tip = supports[s].FaceMax + u * Math.Max(0.0, boyK - PaspayiCm);
                        Point3d beamFar = supports[s].FaceMin + u * PaspayiCm;
                        bool devamAciklik = leftOk && leftSp.Value.LnextExt > 1.0;
                        Point3d mainEnd = devamAciklik
                            ? supports[s].FaceMin - u * (leftSp.Value.L * 0.25)
                            : beamFar;
                        double kirisGonye = devamAciklik ? 0.0 : KonsolKirisGonyeCm;
                        Vector3d offKn = n * ResolveKonsolAlongN(placedKonsol, tip, mainEnd, u, offsetAlongNCm);
                        // Konsol sağda (next) → sağ ilave
                        bool gprOk = PickKonsolIlaveSteel(leftSp.Value, sagSide: true, boyK, tip.DistanceTo(mainEnd),
                            out ad, out cp, out ar);
                        if (boyK <= KonsolPilyeMinUzCm)
                        {
                            DrawLabeledStock(
                                tr, btr, db,
                                BuildFirketePoints(tip, beamFar, mainEnd, n, offKn, kirisGonye),
                                cp, ad, ar, n, u,
                                donatiLayer, yaziLayer, yaziStyleId, true,
                                Mid(tip + offKn, mainEnd + offKn),
                                !gprOk, StockParkKind.IlaveDown, MinSpanCm * 0.25);
                        }
                        else
                        {
                            DrawLabeledStock(
                                tr, btr, db,
                                BuildGonyePoints(mainEnd + offKn, tip + offKn, n,
                                    kirisGonye > 0.5, true,
                                    kirisGonye > 0.5 ? kirisGonye : GonyeCm,
                                    KonsolIlaveGonyeCm),
                                cp, ad, ar, n, u,
                                donatiLayer, yaziLayer, yaziStyleId, true,
                                Mid(tip + offKn, mainEnd + offKn),
                                !gprOk, StockParkKind.IlaveDown, MinSpanCm * 0.25);
                        }
                    }
                    continue;
                }

                Point3d st = leftOk
                    ? supports[s].FaceMin - u * (Ll * 0.25) + off
                    : supports[s].FaceMin + u * PaspayiCm + off;
                Point3d en = rightOk
                    ? supports[s].FaceMax + u * (Lr * 0.25) + off
                    : supports[s].FaceMax - u * PaspayiCm + off;

                PlaceOne(st, en, gonyeStart: !leftOk, gonyeEnd: !rightOk, ad, cp, ar,
                    off,
                    leftOk && leftSp.HasValue ? leftSp.Value.SupportLeft : -1,
                    rightOk && rightSp.HasValue ? rightSp.Value.SupportLeft : -1,
                    contGpr);
            }

            // Kısa açıklık: A (sağ ucu açıklığa uzanan) + B (sol ucu aynı açıklığa uzanan) → tek ilave
            bool merged = true;
            while (merged)
            {
                merged = false;
                foreach (var sp in allSpans)
                {
                    if (sp.L >= MinPilyeSpanCm) continue;
                    if (drawnSupportLeft == null || !drawnSupportLeft.Contains(sp.SupportLeft)) continue;
                    int ia = segs.FindIndex(x => x.EndSpan == sp.SupportLeft);
                    int ib = segs.FindIndex(x => x.StartSpan == sp.SupportLeft);
                    if (ia < 0 || ib < 0 || ia == ib) continue;
                    IlaveSeg A = segs[ia], B = segs[ib];
                    bool gprOk = PickIlaveSteel(sp, sagSide: true, sp.L, out int ad2, out int cp2, out int ar2);
                    if (TryApplyGprIlaveSteel(sp.Slab.GprSteels, sp.LeftInner, sagSide: false, sp.L, ref ad2, ref cp2, ref ar2))
                        gprOk = true;
                    var m = new IlaveSeg
                    {
                        Start = A.Start,
                        End = B.End - B.Off + A.Off,
                        GonyeStart = A.GonyeStart,
                        GonyeEnd = B.GonyeEnd,
                        Adet = ad2, Cap = cp2, Aralik = ar2,
                        Off = A.Off,
                        StartSpan = A.StartSpan,
                        EndSpan = B.EndSpan,
                        MissingGprMatch = !gprOk || A.MissingGprMatch || B.MissingGprMatch
                    };
                    segs.RemoveAt(Math.Max(ia, ib));
                    segs.RemoveAt(Math.Min(ia, ib));
                    segs.Add(m);
                    merged = true;
                    break;
                }
            }

            foreach (var sg in segs)
            {
                if (sg.Start.DistanceTo(sg.End) < MinSpanCm * 0.25) continue;
                DrawLabeledStock(
                    tr, btr, db,
                    BuildGonyePoints(sg.Start, sg.End, n, sg.GonyeStart, sg.GonyeEnd, GonyeCm, GonyeCm),
                    sg.Cap, sg.Adet, sg.Aralik, n, u,
                    donatiLayer, yaziLayer, yaziStyleId, true,
                    Mid(sg.Start, sg.End),
                    sg.MissingGprMatch, StockParkKind.IlaveDown, MinSpanCm * 0.25);
            }
        }

        private struct IlaveSeg
        {
            public Point3d Start, End;
            public bool GonyeStart, GonyeEnd;
            public int Adet, Cap, Aralik;
            public Vector3d Off;
            /// <summary>Başlangıç/bitiş ucunun uzandığı açıklık (SupportLeft); −1 = mesnette biter.</summary>
            public int StartSpan, EndSpan;
            public bool MissingGprMatch;
        }

        private static Point3d Mid(Point3d a, Point3d b) =>
            new Point3d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, 0);

        private static Point2d To2d(Point3d p) => new Point2d(p.X, p.Y);

        private static void CollectIntersections(
            Transaction tr,
            BlockTableRecord btr,
            Point3d a,
            Point3d b,
            Point3d origin,
            Vector3d u,
            HashSet<string> layers,
            List<HitRec> hits)
        {
            CollectIntersections(tr, btr, a, b, origin, u, lyr => IsMesnetLayer(lyr, layers), hits);
        }

        private static void CollectIntersections(
            Transaction tr,
            BlockTableRecord btr,
            Point3d a,
            Point3d b,
            Point3d origin,
            Vector3d u,
            Func<string, bool> layerOk,
            List<HitRec> hits)
        {
            var probeSeg = MakeProbeSeg(a, b);
            foreach (ObjectId id in btr)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent))
                    continue;
                if (ent is Dimension || ent is DBText || ent is MText || ent is Hatch
                    || ent is Spline || ent is Ellipse || ent is Leader || ent is MLeader
                    || ent is Region || ent is Solid || ent is Face || ent is ProxyEntity
                    || ent is Polyline3d || ent is DBPoint || ent is Mline)
                    continue;
                if (layerOk == null || !layerOk(ent.Layer))
                    continue;

                try
                {
                    var pts = new List<Point3d>();
                    if (ent is Polyline pl)
                        CollectPolyline2dHits(pl, probeSeg, pts);
                    else if (ent is Polyline2d p2)
                        CollectOldPolyline2dHits(tr, p2, probeSeg, pts);
                    else
                        AppendEntity2dHits(ent, probeSeg, pts);
                    foreach (Point3d p in pts)
                        AddHit(hits, p, origin, u, id, ent.Layer);
                }
                catch { }
            }
        }

        private static void CollectOldPolyline2dHits(
            Transaction tr, Polyline2d p2, LineSegment2d probeSeg, List<Point3d> dest)
        {
            var verts = new List<Point2d>();
            foreach (ObjectId vid in p2)
            {
                try
                {
                    if (tr.GetObject(vid, OpenMode.ForRead) is Vertex2d v)
                        verts.Add(new Point2d(v.Position.X, v.Position.Y));
                }
                catch { }
            }
            int n = verts.Count;
            if (n < 2) return;
            int segs = p2.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                Point2d[] hits = IntersectBulgeSeg(verts[i], verts[(i + 1) % n], 0, probeSeg);
                if (hits == null) continue;
                foreach (Point2d p in hits)
                    dest.Add(new Point3d(p.X, p.Y, 0));
            }
        }

        private static void AddHit(
            List<HitRec> hits, Point3d p, Point3d origin, Vector3d u, ObjectId id, string layer)
        {
            var flat = new Point3d(p.X, p.Y, 0);
            hits.Add(new HitRec
            {
                Point = flat,
                EntId = id,
                T = (flat - origin).DotProduct(u),
                Layer = layer ?? ""
            });
        }

        private static void DedupHitsNear(List<HitRec> hits, double tol)
        {
            for (int i = hits.Count - 1; i > 0; i--)
            {
                if (hits[i].Point.DistanceTo(hits[i - 1].Point) <= tol)
                    hits.RemoveAt(i);
            }
        }

        private static string EnsureLayer(
            Transaction tr,
            Database db,
            string name,
            short aci,
            LineWeight lw)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name))
                return name;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, aci),
                LineWeight = lw
            };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return name;
        }

        private static bool BoundaryLayerOn(string name)
        {
            if (IsMesnetLayer(name, null)) return true;
            int bar = string.IsNullOrEmpty(name) ? -1 : name.LastIndexOf('|');
            return bar >= 0 && bar + 1 < name.Length && IsMesnetLayer(name.Substring(bar + 1), null);
        }

        /// <summary>Mesnet dışı katmanları kapatır. Regen yok; TraceBoundary katman tablosunu okur.</summary>
        private static List<ObjectId> HideNonMesnetForBoundary(Transaction tr, Database db, out ObjectId prevClayer)
        {
            prevClayer = db.Clayer;
            var hidden = new List<ObjectId>();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            bool currentOk = false;
            try
            {
                var cur = (LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead);
                currentOk = BoundaryLayerOn(cur.Name) && !cur.IsOff && !cur.IsFrozen;
            }
            catch { currentOk = false; }
            if (!currentOk)
            {
                foreach (ObjectId id in lt)
                {
                    var rec = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (!BoundaryLayerOn(rec.Name) || rec.IsOff || rec.IsFrozen) continue;
                    db.Clayer = id;
                    break;
                }
            }
            foreach (ObjectId id in lt)
            {
                if (id == db.Clayer) continue;
                LayerTableRecord rec;
                try { rec = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead); }
                catch { continue; }
                if (BoundaryLayerOn(rec.Name) || rec.IsOff || rec.IsFrozen) continue;
                try
                {
                    rec.UpgradeOpen();
                    rec.IsOff = true;
                    hidden.Add(id);
                }
                catch { }
            }
            return hidden;
        }

        private static void RestoreBoundaryLayers(Transaction tr, Database db, List<ObjectId> hidden, ObjectId prevClayer)
        {
            if (hidden != null)
            {
                foreach (ObjectId id in hidden)
                {
                    try
                    {
                        var rec = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                        rec.IsOff = false;
                    }
                    catch { }
                }
            }
            if (!prevClayer.IsNull && prevClayer.IsValid)
            {
                try { db.Clayer = prevClayer; }
                catch { }
            }
        }

        /// <summary>
        /// BOUNDARY (ada kapalı). Ada açıkken kolon/kiriş kapalıları
        /// "Error analyzing internal islands" diyaloğu üretebiliyor; sonuç yine de gelebilir.
        /// </summary>
        private static Polyline TraceSlabBoundary(Editor ed, Point3d seed, Vector3d n)
        {
            Polyline pl = TraceSmallestBoundary(ed, seed);
            if (pl != null) return pl;
            if (n.Length < 1e-9) return null;
            Vector3d nn = n.GetNormal();
            for (double s = 8.0; s <= 40.0; s += 8.0)
            {
                pl = TraceSmallestBoundary(ed, seed + nn * s);
                if (pl != null) return pl;
                pl = TraceSmallestBoundary(ed, seed - nn * s);
                if (pl != null) return pl;
            }
            return null;
        }

        private static Polyline TraceSmallestBoundary(Editor ed, Point3d seed)
        {
            // detectIslands=false: iç ada analizi yok → diyalog yok; tohumun hücre sınırı alınır.
            DBObjectCollection col;
            try { col = ed.TraceBoundary(seed, false); }
            catch { return null; }
            if (col == null || col.Count == 0) return null;
            Polyline best = null;
            double bestArea = double.MaxValue;
            foreach (DBObject obj in col)
            {
                if (!(obj is Polyline pl) || pl.NumberOfVertices < 3)
                {
                    obj?.Dispose();
                    continue;
                }
                pl.Closed = true;
                bool inside = PolyContains(pl, seed) || DistToPolyline(pl, seed) <= 1.0;
                double area = RingArea(pl);
                if (!inside || area < 20.0 || area >= bestArea)
                {
                    pl.Dispose();
                    continue;
                }
                if (best != null) best.Dispose();
                best = pl;
                bestArea = area;
            }
            return best;
        }

        private static void ForceLayerVisible(Transaction tr, Database db, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(name)) return;
            var rec = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForRead);
            if (!rec.IsOff && !rec.IsFrozen) return;
            rec.UpgradeOpen();
            rec.IsOff = false;
            rec.IsFrozen = false;
        }
    }
}
