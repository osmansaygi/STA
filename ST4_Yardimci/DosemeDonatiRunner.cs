using System;
using System.Collections.Generic;
using System.Globalization;
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
    internal static class DosemeDonatiRunner
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
        private const string LayerDosemeSiniri = "DOSEME SINIRI (BEYKENT)";
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
            @"^\s*(D[A-Za-z]?-?\d+)",
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

        private struct GprSteelRec
        {
            public Point3d Pos;
            public int Adet;
            public int CapMm;
            public int AralikCm;
            public string Text;
        }

        private struct SpanRec
        {
            public int SupportLeft;
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
        }

        public enum DonatiYonu { Serbest, X, Y }

        /// <summary>Formdan seçilir: X/Y ise sanal çizgi o eksene kilitlenir.</summary>
        public static DonatiYonu Yon = DonatiYonu.Serbest;

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
                string yonMsg = Yon == DonatiYonu.X ? " yön=X"
                    : Yon == DonatiYonu.Y ? " yön=Y" : " yön=serbest";
                ed.WriteMessage(
                    "\nDOSEMEDONATICIZ: Kiriş/kolon algı + pilye/düz (pas 3, düz −20).{0} Esc = bitir.",
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
                EnsureKotCache(tr, btr);
                ed.WriteMessage("\nDOSEMEDONATICIZ: KOT YAZI cache={0}", _kotCache?.Count ?? 0);
                var slabTexts = slabTextsEarly;
                var boundaries = CollectBoundaryCurves(tr, btr);
                var gprNotes = CollectGprSteelNotes(tr, btr);
                if (Yon != DonatiYonu.Serbest)
                    ed.WriteMessage("\nDOSEMEDONATICIZ: GPR {0} yazı {1}.",
                        gprNotes.Count, Yon == DonatiYonu.X ? "kırmızı/X" : "mavi/Y");

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
                    if (PointInsideBosluk(closedAreas, mid) || PointInsideMesnetBody(closedAreas, mid))
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
                    if (!TryClassifyGapAsDoseme(
                            boundaries, leftInner, rightInner, u, n, closedAreas, slabTexts, gprNotes,
                            out SlabInfo slab)
                        && !TryAcceptEdgeSiniriSlab(
                            boundaries, leftInner, rightInner, u, n, closedAreas, slabTexts, gprNotes,
                            L, edgeSpan, touchesSiniri, out slab))
                    {
                        skippedBos++;
                        if (inPick)
                            ed.WriteMessage("\n  Atlandı L={0:0} (kapalı döşeme yok / sınır sızması)", L);
                        continue;
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
                    if (barL.DistanceTo(barR) < 20.0)
                        continue;

                    int supportLeft = (!duzOnly && leftD.MesnetIndex >= 0)
                        ? leftD.MesnetIndex
                        : -1000 - i;

                    allSpans.Add(new SpanRec
                    {
                        SupportLeft = supportLeft,
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
                        RightIsMesnet = rightD.Kind == DividerKind.Mesnet
                    });
                }

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
                var spanSlabs = new List<(int supportLeft, SlabInfo slab, int adet, int cap, int aralik)>();
                var drawnSupportLeft = new HashSet<int>();

                for (int si = 0; si < allSpans.Count; si++)
                {
                    SpanRec span = allSpans[si];
                    if (!span.InPick)
                        continue;

                    SpanRec? prev = si > 0 ? allSpans[si - 1] : (SpanRec?)null;
                    SpanRec? next = si + 1 < allSpans.Count ? allSpans[si + 1] : (SpanRec?)null;
                    // Komşu yalnızca bir önceki/sonraki geçerli açıklık VE aynı üst kot
                    bool hasPrev = !span.DuzOnly && prev.HasValue
                        && !prev.Value.DuzOnly
                        && prev.Value.SupportLeft + 1 == span.SupportLeft
                        && SlabsContinue(span.Slab, prev.Value.Slab);
                    bool hasNext = !span.DuzOnly && next.HasValue
                        && !next.Value.DuzOnly
                        && span.SupportLeft + 1 == next.Value.SupportLeft
                        && SlabsContinue(span.Slab, next.Value.Slab);
                    double Lprev = hasPrev ? prev.Value.L : 0;
                    double Lnext = hasNext ? next.Value.L : 0;
                    // Komşu allSpans'te yok (solda perde tek kenar, açıklık atlandı, kot okunamadı…)
                    // ama kapalı alanda aynı üst kot döşeme varsa yine L/4
                    if (!span.ReachLeft) { hasPrev = false; Lprev = 0; }
                    if (!span.ReachRight) { hasNext = false; Lnext = 0; }
                    bool konsolPrev = false, konsolNext = false;
                    double konsolBoyPrev = 0, konsolBoyNext = 0;
                    // Konsol: pick mesnet dışına kadar gitmese bile öte yanı yokla (firkete/ilave).
                    // Pilye L/4 yalnız Reach + aynı kot.
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
                    // allSpans'te bitişik kısa DuzOnly (etiketsiz konsol) — TryNeighbor kaçırsa
                    if (!span.DuzOnly)
                    {
                        for (int j = 0; j < allSpans.Count; j++)
                        {
                            SpanRec d = allSpans[j];
                            if (!d.DuzOnly || d.L < PaspayiCm || d.L > SiniriKonsolMaxCm) continue;
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
                    if (!skipAltDuz)
                        PlaceStraightBar(tr, btr, span.BarL + duzOff, span.BarR + duzOff, donatiLayer);

                    // 2 m'den kısa, sınır/boşluk açıklığı veya sanal hat tam kesmiyor: pilye yok
                    double pilyeLen = 0;
                    bool okPil = !span.DuzOnly
                        && span.ReachLeft && span.ReachRight && span.L >= MinPilyeSpanCm
                        && TryPlacePilyeSpan(
                        tr, btr, span.LeftInner, span.RightInner, span.LeftOuter, span.RightOuter,
                        span.BarL, span.BarR,
                        u, n, span.L, Lprev, Lnext, hasPrev, hasNext, donatiLayer, pilyeLow, out pilyeLen);

                    double topCm = (span.SupportLeft % 2) * StaggerCm + (okPil ? PilyeKirimRiseCm : 0);
                    if (topCm > maxTopAlongNCm) maxTopAlongNCm = topCm;

                    SlabInfo slab = span.Slab;
                    double Lbar = span.BarL.DistanceTo(span.BarR);
                    int cap = slab.CapMm > 0 ? slab.CapMm : 8;
                    int aralik = slab.AralikCm > 0 ? slab.AralikCm : 20;
                    int adet = slab.Adet ?? EstimateAdet(Lbar, aralik);
                    Point3d duzMid = Mid(span.BarL + duzOff, span.BarR + duzOff);
                    ApplyGprSteelNear(slab.GprSteels, duzMid, ref adet, ref cap, ref aralik);
                    // Konsol alt düz: boy tablosu değil, sabit ø8/20
                    if (span.DuzOnly)
                    {
                        cap = 8;
                        aralik = 20;
                        adet = EstimateAdet(Lbar, aralik);
                    }

                    int pilCap = cap, pilAralik = aralik, pilAdet = adet;
                    if (okPil)
                    {
                        Point3d pilMidTmp = Mid(span.LeftInner, span.RightInner) + pilyeLow;
                        ApplyGprSteelNear(slab.GprSteels, pilMidTmp, ref pilAdet, ref pilCap, ref pilAralik);
                    }

                    if (!span.DuzOnly)
                    {
                        spanSlabs.Add((span.SupportLeft, slab, adet, cap, aralik));
                        drawnSupportLeft.Add(span.SupportLeft);
                    }

                    string duzLabel = FormatLabel(adet, cap, aralik, Lbar, upper: false);
                    string pilLabel = FormatLabel(pilAdet, pilCap, pilAralik, okPil ? pilyeLen : Lbar, upper: true);

                    if (!skipAltDuz)
                        PlaceLabel(tr, btr, db, duzMid, duzLabel, yaziLayer, yaziStyleId, u, n);

                    if (okPil)
                    {
                        Point3d pilMid = Mid(span.LeftInner, span.RightInner) + pilyeLow;
                        PlaceLabel(tr, btr, db, pilMid, pilLabel, yaziLayer, yaziStyleId, u, n);
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

                // İlave üst: çizilen açıklıklara komşu mesnetler; devam = aynı d/kot komşu açıklık
                PlaceAllUstIlave(
                    tr, btr, db, supports, allSpans, drawnSupportLeft, u, n,
                    donatiLayer, yaziLayer, yaziStyleId,
                    maxTopAlongNCm + UstIlaveAboveTopCm, spanSlabs);
                // Sadece kiriş+konsol kesildiğinde (bitişik döşeme InPick değil) üst ilave/firkete
                PlaceSoloKonsolIlaves(
                    tr, btr, db, allSpans, boundaries, closedAreas, slabTexts, gprNotes, u, n,
                    Math.Max(maxTopAlongNCm, 0) + UstIlaveAboveTopCm,
                    donatiLayer, yaziLayer, yaziStyleId);

                tr.Commit();
                return placed;
            }
        }

        private static HashSet<string> BuildMesnetLayerSet()
        {
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
            if (exact != null && exact.Contains(lyr)) return true;
            return lyr.IndexOf("KAPAMA PERDESI", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(lyr, LayerPerde, StringComparison.OrdinalIgnoreCase)
                || string.Equals(lyr, LayerKiris, StringComparison.OrdinalIgnoreCase)
                || string.Equals(lyr, LayerKolon, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDosemeSiniriLayer(string lyr)
        {
            return !string.IsNullOrEmpty(lyr)
                && lyr.IndexOf("DOSEME SINIRI", StringComparison.OrdinalIgnoreCase) >= 0;
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

        /// <summary>KALIP BOSLUK — döşeme değil, donatı yok. MERDIVEN çizgileri yoksay (sınır değil).</summary>
        private static bool IsKalipBoslukLayer(string lyr)
        {
            if (string.IsNullOrEmpty(lyr)) return false;
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

        /// <summary>Kapalı boşluk alanı (kalıp boşluk + merdiven poligon içi).</summary>
        private static bool IsBoslukLayer(string lyr)
        {
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
                bool dosSin = string.Equals(lyr, LayerDosemeSiniri, StringComparison.OrdinalIgnoreCase)
                    || lyr.IndexOf("DOSEME SINIRI", StringComparison.OrdinalIgnoreCase) >= 0;
                bool mesnet = string.Equals(lyr, LayerKiris, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(lyr, LayerKolon, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(lyr, LayerPerde, StringComparison.OrdinalIgnoreCase)
                    || lyr.IndexOf("KAPAMA PERDESI", StringComparison.OrdinalIgnoreCase) >= 0;
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
                if (ent is DBText db)
                {
                    // AppendSlabLabel: TextLeft+TextBottom, Position=AlignmentPoint=(leftX,mainY)
                    insert = db.Position;
                    txt = db.TextString ?? "";
                }
                else
                {
                    var mt = (MText)ent;
                    insert = mt.Location;
                    txt = mt.Contents ?? "";
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
                    Text = txt
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
                if (IsMerdivenLayer(lyr)) continue;
                bool ok = string.Equals(lyr, LayerKiris, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(lyr, LayerKolon, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(lyr, LayerPerde, StringComparison.OrdinalIgnoreCase)
                    || lyr.IndexOf("KAPAMA PERDESI", StringComparison.OrdinalIgnoreCase) >= 0
                    || lyr.IndexOf("DOSEME SINIRI", StringComparison.OrdinalIgnoreCase) >= 0
                    || IsKalipBoslukLayer(lyr);
                if (!ok) continue;
                try
                {
                    Extents3d e = c.GeometricExtents;
                    index.Add(new BoundaryRec
                    {
                        Curve = c,
                        MinX = e.MinPoint.X,
                        MinY = e.MinPoint.Y,
                        MaxX = e.MaxPoint.X,
                        MaxY = e.MaxPoint.Y
                    });
                }
                catch { }
            }
            index.Stamp = new int[index.Items.Count];
            return index;
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
            using (var seg = new Line(new Point3d(a.X, a.Y, 0), new Point3d(b.X, b.Y, 0)))
            {
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
                            if (ly.IndexOf("DOSEME SINIRI", StringComparison.OrdinalIgnoreCase) >= 0)
                                continue;
                        }
                        if (CurveHitsProbe(bd.Curve, seg, a, b, endTolCm))
                            return true;
                    }
                }
            }
            return false;
        }

        private static bool CurveHitsProbe(Curve c, Line probe, Point3d a, Point3d b, double endTolCm)
        {
            if (c is Polyline pl)
                return PolylineSegmentsHitProbe(pl, probe, a, b, endTolCm);
            return LineHitsCollected(c, probe, a, b, endTolCm);
        }

        private static bool PolylineSegmentsHitProbe(
            Polyline pl, Line probe, Point3d a, Point3d b, double endTolCm)
        {
            int n = pl.NumberOfVertices;
            if (n < 2) return false;
            var probeSeg = new LineSegment2d(
                new Point2d(probe.StartPoint.X, probe.StartPoint.Y),
                new Point2d(probe.EndPoint.X, probe.EndPoint.Y));
            int segs = pl.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                try
                {
                    Point2d[] pts = null;
                    if (Math.Abs(pl.GetBulgeAt(i)) < 1e-9)
                    {
                        Point3d p0 = pl.GetPoint3dAt(i);
                        Point3d p1 = pl.GetPoint3dAt((i + 1) % n);
                        pts = new LineSegment2d(new Point2d(p0.X, p0.Y), new Point2d(p1.X, p1.Y))
                            .IntersectWith(probeSeg);
                    }
                    else
                        pts = pl.GetArcSegment2dAt(i).IntersectWith(probeSeg);
                    if (pts == null) continue;
                    foreach (Point2d p in pts)
                    {
                        var f = new Point3d(p.X, p.Y, 0);
                        if (f.DistanceTo(a) > endTolCm && f.DistanceTo(b) > endTolCm)
                            return true;
                    }
                }
                catch { }
            }
            return false;
        }

        private static bool LineHitsCollected(Curve c, Line probe, Point3d a, Point3d b, double endTolCm)
        {
            try
            {
                var pts = new Point3dCollection();
                c.IntersectWith(probe, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                foreach (Point3d p in pts)
                {
                    var f = new Point3d(p.X, p.Y, 0);
                    if (f.DistanceTo(a) > endTolCm && f.DistanceTo(b) > endTolCm)
                        return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Başlangıç noktasının bulunduğu alanı ızgara üzerinde sınır kesmeden doldurur (her şekil: L, U, yamuk, eğri).
        /// Alan AÇIKSA (bina dışına / yarıçap sınırına taşıyor, çok büyük) → false: döşeme değildir.
        /// Kapalıysa → true; labelIdx = alan içinde yol boyunca ilk ulaşılan etiket (yoksa −1).
        /// </summary>
        private static bool FloodClosedRegion(
            BoundaryIndex bounds, Point3d start, List<SlabTextRec> labels, out int labelIdx,
            List<int> collectAll = null, List<ClosedArea> solids = null)
        {
            labelIdx = -1;
            double step = FloodStepCm;
            long Key(int i, int j) => BoundaryIndex.Key(i, j);
            bool InSolid(Point3d p) =>
                solids != null && FindSmallestContaining(solids, p, a => a.IsMesnetBody || a.IsBosluk).HasValue;

            if (InSolid(start))
            {
                Point3d nudged = start;
                bool found = false;
                double[] tryOff = { step, step * 2, step * 3 };
                int[] sdi = { 1, -1, 0, 0 };
                int[] sdj = { 0, 0, 1, -1 };
                foreach (double off in tryOff)
                {
                    for (int d = 0; d < 4 && !found; d++)
                    {
                        var q = new Point3d(start.X + sdi[d] * off, start.Y + sdj[d] * off, 0);
                        if (!InSolid(q)) { nudged = q; found = true; }
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
                int bi = (int)Math.Round((p.X - start.X) / step);
                int bj = (int)Math.Round((p.Y - start.Y) / step);
                long k = Key(bi, bj);
                if (!labelBuckets.TryGetValue(k, out var lst))
                    labelBuckets[k] = lst = new List<int>();
                lst.Add(li);
            }

            Point3d Center(int i, int j) => new Point3d(start.X + i * step, start.Y + j * step, 0);
            int maxIdx = (int)(FloodMaxRadiusCm / step);
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

            while (queue.Count > 0)
            {
                if (visited.Count > FloodMaxCells) return false;
                var (ci, cj) = queue.Dequeue();
                Point3d c = Center(ci, cj);
                if (c.X < bxMin || c.X > bxMax || c.Y < byMin || c.Y > byMax) return false;

                if (labelIdx < 0)
                {
                    for (int oi = -1; oi <= 1 && labelIdx < 0; oi++)
                    for (int oj = -1; oj <= 1 && labelIdx < 0; oj++)
                    {
                        if (!labelBuckets.TryGetValue(Key(ci + oi, cj + oj), out var lst)) continue;
                        foreach (int li in lst)
                        {
                            if (!SegmentCrossesBoundary(bounds, c, labels[li].Insert, 0.5, softSiniri))
                            {
                                if (labelIdx < 0) labelIdx = li;
                                if (collectAll != null && !collectAll.Contains(li))
                                    collectAll.Add(li);
                                break;
                            }
                        }
                    }
                }

                for (int d = 0; d < 4; d++)
                {
                    int ni = ci + di[d], nj = cj + dj[d];
                    if (Math.Abs(ni) > maxIdx || Math.Abs(nj) > maxIdx) return false;
                    long nk = Key(ni, nj);
                    if (visited.Contains(nk)) continue;
                    Point3d np = Center(ni, nj);
                    if (InSolid(np)) continue;
                    if (SegmentCrossesBoundary(bounds, c, np, 0.05)) continue;
                    visited.Add(nk);
                    queue.Enqueue((ni, nj));
                }
            }
            return true;
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

            // Alan kapalı mı + etiket. Açık köşe (bina dışı) komşu etiketi çalamaz.
            // Küçük/dar döşemede etiket sınır dışında: yalnız kapalı alanda koridor.
            SlabTextRec label;
            bool flooded = FloodClosedRegion(bounds, mid, candidates, out int li, null, areas);
            if (flooded && li >= 0)
                label = candidates[li];
            else if (flooded && TryPickSlabLabelInCorridor(
                    leftInner, rightInner, along, nn, candidates, bounds, areas, out label))
            { /* kapalı alan, etiket sınır dışında (yalnız mid siniri içindeyse) */ }
            else
                return false;

            FillSlabInfoFromBlob(ref slab, label.Text);
            AttachUstKotFromPlanLabel(label, null, ref slab);
            AttachGprSteelInRegion(bounds, mid, gprNotes, areas, ref slab);
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
                AttachGprSteelInRegion(bounds, mid, gprNotes, areas, ref slab);
            }
            slab.HasLabel = true;
            return true;
        }

        /// <summary>
        /// Küçük/dar döşeme: etiket sınır dışında olabilir.
        /// DOSEME SINIRI yalnız mid sınır poligonunun içindeyse yok sayılır (boş alan üst etiketi çalamaz).
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
            double pad = Math.Max(80.0, Math.Min(160.0, len * 0.6));
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
                if ((t < -pad || t > len + pad || perp > pad + 80.0) && !boxNear)
                    continue;
                if (SegmentCrossesBoundary(bounds, mid, p, 0.5, softSiniri)
                    && SegmentCrossesBoundary(bounds, mid, st.Pos, 0.5, softSiniri))
                    continue;
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

        /// <summary>KALIP50/100 2. kopya: X=kırmızı, Y=mavi GPR yazıları (adet ø çap / aralık L=).</summary>
        private static List<GprSteelRec> CollectGprSteelNotes(Transaction tr, BlockTableRecord btr)
        {
            var list = new List<GprSteelRec>();
            if (Yon == DonatiYonu.Serbest) return list;
            bool wantRed = Yon == DonatiYonu.X;
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
                    Text = txt
                });
            }
            return list;
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
            List<ClosedArea> areas, ref SlabInfo slab)
        {
            if (gprNotes == null || gprNotes.Count == 0) return;
            var asLabels = new List<SlabTextRec>(gprNotes.Count);
            foreach (var g in gprNotes)
                asLabels.Add(new SlabTextRec { Insert = g.Pos, Pos = g.Pos, Text = g.Text });
            var hit = new List<int>();
            if (!FloodClosedRegion(bounds, mid, asLabels, out _, hit, areas) || hit.Count == 0)
                return;
            var found = new List<GprSteelRec>(hit.Count);
            foreach (int i in hit)
                found.Add(gprNotes[i]);
            slab.GprSteels = found;
            ApplyGprSteelNear(found, mid, ref slab.Adet, ref slab.CapMm, ref slab.AralikCm);
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
                if (ent is DBText db)
                {
                    insert = db.Position;
                    txt = db.TextString ?? "";
                }
                else
                {
                    var mt = (MText)ent;
                    insert = mt.Location;
                    txt = mt.Contents ?? "";
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
                    Text = txt
                });
            }
        }

        /// <summary>
        /// Plan_ID AppendSlabLabel geometrisiyle üst kot bul.
        /// Üst kot Position ≈ (leftX + 14.33·ann, mainY − 26·ann); alt ≈ üst − 10·ann.
        /// ann = 1 (1:50) veya 2 (1:100). Çiftte yüksek Y = üst kot.
        /// </summary>
        private static void AttachUstKotFromPlanLabel(SlabTextRec label, Point2d[] cellRing, ref SlabInfo slab)
        {
            if (_kotCache == null || _kotCache.Count == 0) return;

            double leftX = label.Insert.X;
            double mainY = label.Insert.Y;
            double[] anns = { 1.0, 2.0 };

            KotTextRec? bestTop = null;
            double bestScore = double.MaxValue;
            double bestAnn = 1.0;

            foreach (double ann in anns)
            {
                double expectX = leftX + PlanKotOffsetXCm * ann;
                double expectY = mainY - (PlanLabelToQCm + PlanQToUstKotCm) * ann;
                double tolX = 18.0 * ann;
                double tolY = 14.0 * ann;

                foreach (var k in _kotCache)
                {
                    if (cellRing != null &&
                        !PointInRing(cellRing, k.Insert.X, k.Insert.Y) &&
                        !PointInRing(cellRing, k.Pos.X, k.Pos.Y))
                    {
                        // Hücre dışı: yalnız beklenen noktaya çok yakınsa kabul (kiriş kotu ele)
                        double dCell = Math.Abs(k.Insert.X - expectX) + Math.Abs(k.Insert.Y - expectY);
                        if (dCell > 25.0 * ann) continue;
                    }

                    double dx = k.Insert.X - expectX;
                    double dy = k.Insert.Y - expectY;
                    if (Math.Abs(dx) > tolX || Math.Abs(dy) > tolY) continue;

                    double score = dx * dx + dy * dy;
                    // Alt kot satırı da beklenen üstün 10·ann altında — skor kötü, üst kazanır
                    double altY = expectY - PlanKotPairGapCm * ann;
                    double dyAlt = k.Insert.Y - altY;
                    if (Math.Abs(dyAlt) < Math.Abs(dy) && Math.Abs(dx) <= tolX)
                        score += 50.0 * ann * ann; // alt satırı cezalandır

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestTop = k;
                        bestAnn = ann;
                    }
                }
            }

            if (!bestTop.HasValue)
            {
                // Gevşek: mainY altında, leftX sağında, en yüksek Y (üst satır)
                var loose = new List<KotTextRec>();
                foreach (var k in _kotCache)
                {
                    if (k.Insert.Y > mainY - 5.0) continue;
                    if (k.Insert.Y < mainY - 100.0) continue;
                    if (k.Insert.X < leftX - 10.0) continue;
                    if (k.Insert.X > leftX + 90.0) continue;
                    if (cellRing != null &&
                        !PointInRing(cellRing, k.Insert.X, k.Insert.Y) &&
                        k.Insert.DistanceTo(label.Insert) > 80.0)
                        continue;
                    loose.Add(k);
                }
                if (loose.Count == 0) return;
                loose.Sort((a, b) => b.Insert.Y.CompareTo(a.Insert.Y));
                slab.KotM = loose[0].ValueM;
                return;
            }

            // Beklenen üst civarındaki çiftten MaxY (Insert.Y) al
            double pairX = bestTop.Value.Insert.X;
            double pairY = bestTop.Value.Insert.Y;
            double gap = PlanKotPairGapCm * bestAnn + 4.0;
            KotTextRec top = bestTop.Value;
            foreach (var k in _kotCache)
            {
                if (Math.Abs(k.Insert.X - pairX) > 20.0 * bestAnn) continue;
                if (Math.Abs(k.Insert.Y - pairY) > gap) continue;
                if (k.Insert.Y > top.Insert.Y)
                    top = k;
            }

            // d= ile doğrula: alt kot ≈ üst − d/100 → yanlış kiriş kotunu ele
            if (slab.ThicknessCm.HasValue && slab.ThicknessCm.Value > 1.0)
            {
                double expectBotM = top.ValueM - slab.ThicknessCm.Value / 100.0;
                bool pairOk = false;
                foreach (var k in _kotCache)
                {
                    if (Math.Abs(k.Insert.X - top.Insert.X) > 20.0 * bestAnn) continue;
                    double dy = top.Insert.Y - k.Insert.Y;
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
                        if (Math.Abs(candTop.Insert.X - leftX - PlanKotOffsetXCm * bestAnn) > 30.0 * bestAnn)
                            continue;
                        if (candTop.Insert.Y > mainY - 5.0) continue;
                        if (candTop.Insert.Y < mainY - 100.0 * bestAnn) continue;
                        foreach (var candBot in _kotCache)
                        {
                            if (Math.Abs(candBot.Insert.X - candTop.Insert.X) > 15.0 * bestAnn) continue;
                            double dy = candTop.Insert.Y - candBot.Insert.Y;
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
            bool sinirKenar = hitLyr.IndexOf("DOSEME SINIRI", StringComparison.OrdinalIgnoreCase) >= 0
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
            using (var probe = new Line(
                new Point3d(origin.X, origin.Y, 0),
                new Point3d(end.X, end.Y, 0)))
            {
                foreach (var bd in bounds.Items)
                {
                    var pts = new List<Point3d>();
                    CollectCurveHitPoints(bd.Curve, probe, pts);
                    foreach (Point3d p in pts)
                    {
                        double t = (p - origin).DotProduct(dir);
                        if (t < minT || t > tHit) continue;
                        tHit = t;
                        try { layer = bd.Curve.Layer ?? ""; } catch { layer = ""; }
                        any = true;
                    }
                }
            }
            return any;
        }

        private static void CollectCurveHitPoints(Curve c, Line probe, List<Point3d> dest)
        {
            if (c is Polyline pl)
            {
                int n = pl.NumberOfVertices;
                if (n < 2) return;
                int segs = pl.Closed ? n : n - 1;
                for (int i = 0; i < segs; i++)
                {
                    Point3d p0 = pl.GetPoint3dAt(i);
                    Point3d p1 = pl.GetPoint3dAt((i + 1) % n);
                    using (var edge = new Line(new Point3d(p0.X, p0.Y, 0), new Point3d(p1.X, p1.Y, 0)))
                        AppendIntersectPoints(edge, probe, dest);
                }
                return;
            }
            AppendIntersectPoints(c, probe, dest);
        }

        private static void AppendIntersectPoints(Curve c, Line probe, List<Point3d> dest)
        {
            try
            {
                var pts = new Point3dCollection();
                c.IntersectWith(probe, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                foreach (Point3d p in pts)
                    dest.Add(new Point3d(p.X, p.Y, 0));
            }
            catch { }
        }

        private static int EstimateAdet(double clearLenCm, int aralikCm)
        {
            if (aralikCm <= 0) return 1;
            return Math.Max(1, (int)Math.Round(clearLenCm / aralikCm) + 1);
        }

        private static string FormatLabel(int adet, int capMm, int aralikCm, double boyCm, bool upper)
        {
            int boy = (int)Math.Round(boyCm);
            string lMark = upper ? "L" : "l";
            return string.Format(CultureInfo.InvariantCulture,
                "{0}ø{1}/{2} {3}={4}", adet, capMm, aralikCm, lMark, boy);
        }

        private static void PlaceLabel(
            Transaction tr,
            BlockTableRecord btr,
            Database db,
            Point3d barRef,
            string text,
            string layer,
            ObjectId textStyleId,
            Vector3d along,
            Vector3d up,
            bool belowBar = false)
        {
            double ang = Math.Atan2(along.Y, along.X);
            // Yazı okunabilir olsun: (-90, 90]; tam düşeyde +90 → yazı üstü −X (sol)
            if (ang > Math.PI / 2 + 1e-9) ang -= Math.PI;
            if (ang <= -Math.PI / 2 + 1e-9) ang += Math.PI;

            var t = new DBText();
            t.SetDatabaseDefaults();
            t.TextString = text;
            t.Height = TextHeightCm;
            if (!textStyleId.IsNull)
                t.TextStyleId = textStyleId;
            t.Position = barRef;
            t.Rotation = 0;
            t.Layer = layer;
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

        private static void PlaceStraightBar(
            Transaction tr,
            BlockTableRecord btr,
            Point3d s,
            Point3d e,
            string layer)
        {
            PlacePolyline(tr, btr, layer, s, e);
        }

        /// <summary>Düz çubuk; isteğe bağlı uç gönyeleri (−n: kesit içi / aşağı — +n pilye yüksekliği).</summary>
        private static void PlaceBarWithGonye(
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
            PlaceBarWithGonye(tr, btr, s, e, up, gonyeStart, gonyeEnd, GonyeCm, GonyeCm, layer, out unfoldedLen);
        }

        private static void PlaceBarWithGonye(
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
            Vector3d n = up.GetNormal();
            Vector3d gonyeDir = -n;
            var pts = new List<Point3d>();
            if (gonyeStart) pts.Add(s + gonyeDir * gonyeLenStart);
            pts.Add(s);
            pts.Add(e);
            if (gonyeEnd) pts.Add(e + gonyeDir * gonyeLenEnd);
            PlacePolyline(tr, btr, layer, pts.ToArray());
            unfoldedLen = 0;
            for (int i = 0; i < pts.Count - 1; i++)
                unfoldedLen += pts[i].DistanceTo(pts[i + 1]);
        }

        /// <summary>
        /// Sanal hat yalnız kiriş+konsol (SINIRI) kesince üst ilave/firkete.
        /// Arkada aynı kot döşeme varsa kirişte gönyeleme, arka açıklığa L/4 uzat.
        /// Yok / kot farklı → kiriş uzak ucu + 40 cm gönye.
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
            ObjectId yaziStyleId)
        {
            if (allSpans == null || allSpans.Count == 0) return;
            Vector3d off = n * offsetAlongNCm;

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

            foreach (SpanRec span in allSpans)
            {
                if (!span.InPick || !span.DuzOnly) continue;
                if (!span.LeftIsMesnet && !span.RightIsMesnet) continue;
                if (span.LeftIsMesnet && span.RightIsMesnet) continue;
                if (CoveredByPickedNeighbor(span)) continue;

                Point3d tip, beamFar, probeOrigin;
                Vector3d probeDir;
                if (span.LeftIsMesnet && !span.RightIsMesnet)
                {
                    // Mesnet solda, konsol sağda → arka döşeme sola (−u)
                    tip = span.RightInner - u * PaspayiCm;
                    beamFar = span.LeftOuter + u * PaspayiCm;
                    probeOrigin = span.LeftOuter;
                    probeDir = -u;
                }
                else
                {
                    // Mesnet sağda, konsol solda → arka döşeme sağa (+u)
                    tip = span.LeftInner + u * PaspayiCm;
                    beamFar = span.RightOuter - u * PaspayiCm;
                    probeOrigin = span.RightOuter;
                    probeDir = u;
                }

                // Aynı kot arka döşeme → L/4; değilse kirişte 40 cm gönye
                Point3d mainEnd = beamFar;
                double kirisGonye = KonsolKirisGonyeCm;
                if (boundaries != null
                    && TryNeighborSlabBeyond(
                        boundaries, probeOrigin, probeDir, n, closedAreas, slabTexts, gprNotes,
                        span.Slab, span.L, out double Lback, out bool backIsKonsol, out _)
                    && !backIsKonsol && Lback > 1.0)
                {
                    mainEnd = probeOrigin + probeDir * (Lback * 0.25);
                    kirisGonye = 0;
                }

                int cap = span.Slab.CapMm > 0 ? span.Slab.CapMm : 8;
                int aralik = span.Slab.AralikCm > 0 ? span.Slab.AralikCm : 20;
                double Lbar = tip.DistanceTo(mainEnd);
                int adet = span.Slab.Adet ?? EstimateAdet(Math.Max(Lbar, span.L), aralik);
                ApplyGprSteelNear(span.Slab.GprSteels, Mid(tip, mainEnd), ref adet, ref cap, ref aralik);
                DosemeKonsolSettings.ApplySteel(span.L, Lbar, ref adet, ref cap, ref aralik);

                double len;
                if (span.L <= KonsolPilyeMinUzCm)
                {
                    PlaceKonsolFirketeIlave(tr, btr, tip, beamFar, mainEnd, n, off,
                        kirisGonye, donatiLayer, out len);
                }
                else if (span.LeftIsMesnet)
                {
                    // tip sağda: mainEnd (sol/arka) → tip
                    PlaceBarWithGonye(tr, btr, mainEnd + off, tip + off, n,
                        gonyeStart: kirisGonye > 0.5, gonyeEnd: true,
                        kirisGonye > 0.5 ? kirisGonye : GonyeCm,
                        KonsolIlaveGonyeCm, donatiLayer, out len);
                }
                else
                {
                    // tip solda: tip → mainEnd
                    PlaceBarWithGonye(tr, btr, tip + off, mainEnd + off, n,
                        gonyeStart: true, gonyeEnd: kirisGonye > 0.5,
                        KonsolIlaveGonyeCm,
                        kirisGonye > 0.5 ? kirisGonye : GonyeCm, donatiLayer, out len);
                }

                if (len < MinSpanCm * 0.25) continue;
                string label = FormatLabel(adet, cap, aralik, len, upper: true);
                PlaceLabel(tr, btr, db, Mid(tip + off, mainEnd + off), label, yaziLayer, yaziStyleId, u, n);
            }
        }

        /// <summary>
        /// Konsol ≤50: üst ilave firkete — üst bacak, 10 cm kenar gönye, dönüş kiriş uzak ucuna paspayı.
        /// beamTopGonyeCm &gt; 0: kiriş içi üst uca gönye (kot farkı / arkası boş → 40 cm).
        /// </summary>
        private static void PlaceKonsolFirketeIlave(
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
            Vector3d nn = up.GetNormal();
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
            PlacePolyline(tr, btr, layer, pts.ToArray());
            unfoldedLen = 0;
            for (int i = 0; i < pts.Count - 1; i++)
                unfoldedLen += pts[i].DistanceTo(pts[i + 1]);
        }

        private static void PlacePolyline(
            Transaction tr,
            BlockTableRecord btr,
            string layer,
            params Point3d[] pts)
        {
            if (pts == null || pts.Length < 2) return;
            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            int vi = 0;
            pl.AddVertexAt(vi++, To2d(pts[0]), 0, 0, 0);
            // Her iç köşeye BendRadiusCm yay (bulge): köşe → iki teğet noktası
            for (int i = 1; i < pts.Length - 1; i++)
            {
                Vector2d dIn = To2d(pts[i]) - To2d(pts[i - 1]);
                Vector2d dOut = To2d(pts[i + 1]) - To2d(pts[i]);
                double lIn = dIn.Length, lOut = dOut.Length;
                if (lIn < 1e-6 || lOut < 1e-6)
                    continue;
                dIn = dIn / lIn;
                dOut = dOut / lOut;
                double cross = dIn.X * dOut.Y - dIn.Y * dOut.X;
                double dot = Math.Max(-1.0, Math.Min(1.0, dIn.DotProduct(dOut)));
                double theta = Math.Acos(dot); // sapma açısı
                if (theta < 1e-4)
                {
                    pl.AddVertexAt(vi++, To2d(pts[i]), 0, 0, 0);
                    continue;
                }
                double trim = BendRadiusCm * Math.Tan(theta / 2.0);
                double maxTrim = Math.Min(lIn, lOut) * 0.45;
                if (trim > maxTrim) trim = maxTrim;
                Point2d a = To2d(pts[i]) - dIn * trim;
                Point2d b = To2d(pts[i]) + dOut * trim;
                double bulge = Math.Tan(theta / 4.0) * (cross >= 0 ? 1.0 : -1.0);
                pl.AddVertexAt(vi++, a, bulge, 0, 0);
                pl.AddVertexAt(vi++, b, 0, 0, 0);
            }
            pl.AddVertexAt(vi, To2d(pts[pts.Length - 1]), 0, 0, 0);
            pl.Closed = false;
            pl.Layer = layer;
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        /// <summary>
        /// Pilye: mesnetlerde yüksek, açıklık ortasında alçak.
        /// Kırım 10 cm (doğrultu) × 10 cm (dik) = 45°, iç yüzden L·2/9'da başlar.
        /// Devam varsa komşu L/4; yoksa uçta 20 cm gönye.
        /// </summary>
        private static bool TryPlacePilyeSpan(
            Transaction tr,
            BlockTableRecord btr,
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
            string layer,
            Vector3d lowOff,
            out double unfoldedLen)
        {
            unfoldedLen = L;
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

            PlacePolyline(tr, btr, layer, pts.ToArray());
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
            List<(int supportLeft, SlabInfo slab, int adet, int cap, int aralik)> spanSlabs)
        {
            if (supports == null || supports.Count == 0 || allSpans == null || allSpans.Count == 0)
                return;
            Vector3d off = n * offsetAlongNCm;
            Vector3d off2 = n * (offsetAlongNCm + StaggerCm); // çift ilave görünsün

            SpanRec? SpanEndingAt(int supportRight)
            {
                foreach (var sp in allSpans)
                    if (sp.SupportLeft + 1 == supportRight) return sp;
                return null;
            }
            SpanRec? SpanStartingAt(int supportLeft)
            {
                foreach (var sp in allSpans)
                    if (sp.SupportLeft == supportLeft) return sp;
                return null;
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
                int endSpan)
            {
                segs.Add(new IlaveSeg
                {
                    Start = start, End = end, GonyeStart = gonyeStart, GonyeEnd = gonyeEnd,
                    Adet = adet, Cap = cap, Aralik = aralik, Off = segOff,
                    StartSpan = startSpan, EndSpan = endSpan
                });
            }

            void PickSteel(SpanRec sp, out int adet, out int cap, out int aralik)
            {
                adet = sp.Slab.Adet ?? EstimateAdet(sp.L, sp.Slab.AralikCm > 0 ? sp.Slab.AralikCm : 20);
                cap = sp.Slab.CapMm > 0 ? sp.Slab.CapMm : 8;
                aralik = sp.Slab.AralikCm > 0 ? sp.Slab.AralikCm : 20;
                ApplyGprSteelNear(sp.Slab.GprSteels, Mid(sp.LeftInner, sp.RightInner), ref adet, ref cap, ref aralik);
                if (spanSlabs == null) return;
                foreach (var x in spanSlabs)
                {
                    if (x.supportLeft == sp.SupportLeft)
                    {
                        adet = x.adet;
                        cap = x.cap;
                        aralik = x.aralik;
                        return;
                    }
                }
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

                bool contBoth = leftSp.HasValue && rightSp.HasValue &&
                    SlabsContinue(leftSp.Value.Slab, rightSp.Value.Slab);

                // İki döşeme var, üst kot farklı → çift ilave; kısa (≤50) taraf firkete
                if (leftSp.HasValue && rightSp.HasValue && !contBoth)
                {
                    if (leftDrawn)
                    {
                        PickSteel(leftSp.Value, out int adet, out int cap, out int aralik);
                        if (leftSp.Value.L <= KonsolPilyeMinUzCm)
                        {
                            DosemeKonsolSettings.ApplySteel(leftSp.Value.L, leftSp.Value.L, ref adet, ref cap, ref aralik);
                            Point3d tip = supports[s].FaceMin - u * Math.Max(0.0, leftSp.Value.L - PaspayiCm);
                            Point3d beamFar = supports[s].FaceMax - u * PaspayiCm;
                            double len;
                            PlaceKonsolFirketeIlave(tr, btr, tip, beamFar, beamFar, n, off,
                                KonsolKirisGonyeCm, donatiLayer, out len);
                            if (len >= MinSpanCm * 0.25)
                            {
                                string label = FormatLabel(adet, cap, aralik, len, upper: true);
                                PlaceLabel(tr, btr, db, Mid(tip + off, beamFar + off),
                                    label, yaziLayer, yaziStyleId, u, n);
                            }
                        }
                        else
                        {
                            Point3d start = supports[s].FaceMin - u * (leftSp.Value.L * 0.25) + off;
                            Point3d end = supports[s].FaceMax - u * PaspayiCm + off;
                            PlaceOne(start, end, gonyeStart: false, gonyeEnd: true, adet, cap, aralik,
                                off, leftSp.Value.SupportLeft, -1);
                        }
                    }
                    if (rightDrawn)
                    {
                        PickSteel(rightSp.Value, out int adet, out int cap, out int aralik);
                        if (rightSp.Value.L <= KonsolPilyeMinUzCm)
                        {
                            DosemeKonsolSettings.ApplySteel(rightSp.Value.L, rightSp.Value.L, ref adet, ref cap, ref aralik);
                            Point3d tip = supports[s].FaceMax + u * Math.Max(0.0, rightSp.Value.L - PaspayiCm);
                            Point3d beamFar = supports[s].FaceMin + u * PaspayiCm;
                            double len;
                            PlaceKonsolFirketeIlave(tr, btr, tip, beamFar, beamFar, n, off2,
                                KonsolKirisGonyeCm, donatiLayer, out len);
                            if (len >= MinSpanCm * 0.25)
                            {
                                string label = FormatLabel(adet, cap, aralik, len, upper: true);
                                PlaceLabel(tr, btr, db, Mid(tip + off2, beamFar + off2),
                                    label, yaziLayer, yaziStyleId, u, n);
                            }
                        }
                        else
                        {
                            Point3d start = supports[s].FaceMin + u * PaspayiCm + off2;
                            Point3d end = supports[s].FaceMax + u * (rightSp.Value.L * 0.25) + off2;
                            PlaceOne(start, end, gonyeStart: true, gonyeEnd: false, adet, cap, aralik,
                                off2, -1, rightSp.Value.SupportLeft);
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
                if (!leftOk && rightDrawn && rightSp.HasValue && rightSp.Value.ExtPrev && !rightSp.Value.KonsolPrev)
                    leftOk = true;
                if (!rightOk && leftDrawn && leftSp.HasValue && leftSp.Value.ExtNext && !leftSp.Value.KonsolNext)
                    rightOk = true;

                double Ll = leftSp.HasValue ? leftSp.Value.L
                    : (rightSp.HasValue && rightSp.Value.ExtPrev && !rightSp.Value.KonsolPrev
                        ? rightSp.Value.LprevExt : 0);
                double Lr = rightSp.HasValue ? rightSp.Value.L
                    : (leftSp.HasValue && leftSp.Value.ExtNext && !leftSp.Value.KonsolNext
                        ? leftSp.Value.LnextExt : 0);

                int ad = 1, cp = 8, ar = 20;
                if (leftDrawn && leftSp.HasValue) PickSteel(leftSp.Value, out ad, out cp, out ar);
                else if (rightDrawn && rightSp.HasValue) PickSteel(rightSp.Value, out ad, out cp, out ar);

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
                        DosemeKonsolSettings.ApplySteel(boyK, tip.DistanceTo(mainEnd), ref ad, ref cp, ref ar);
                        if (boyK <= KonsolPilyeMinUzCm)
                        {
                            double len;
                            PlaceKonsolFirketeIlave(tr, btr, tip, beamFar, mainEnd, n, off,
                                kirisGonye, donatiLayer, out len);
                            if (len >= MinSpanCm * 0.25)
                            {
                                string label = FormatLabel(ad, cp, ar, len, upper: true);
                                PlaceLabel(tr, btr, db, Mid(tip + off, mainEnd + off),
                                    label, yaziLayer, yaziStyleId, u, n);
                            }
                        }
                        else
                        {
                            double len;
                            PlaceBarWithGonye(tr, btr, tip + off, mainEnd + off, n,
                                gonyeStart: true, gonyeEnd: kirisGonye > 0.5,
                                KonsolIlaveGonyeCm,
                                kirisGonye > 0.5 ? kirisGonye : GonyeCm,
                                donatiLayer, out len);
                            if (len >= MinSpanCm * 0.25)
                            {
                                string label = FormatLabel(ad, cp, ar, len, upper: true);
                                PlaceLabel(tr, btr, db, Mid(tip + off, mainEnd + off),
                                    label, yaziLayer, yaziStyleId, u, n);
                            }
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
                        DosemeKonsolSettings.ApplySteel(boyK, tip.DistanceTo(mainEnd), ref ad, ref cp, ref ar);
                        if (boyK <= KonsolPilyeMinUzCm)
                        {
                            double len;
                            PlaceKonsolFirketeIlave(tr, btr, tip, beamFar, mainEnd, n, off,
                                kirisGonye, donatiLayer, out len);
                            if (len >= MinSpanCm * 0.25)
                            {
                                string label = FormatLabel(ad, cp, ar, len, upper: true);
                                PlaceLabel(tr, btr, db, Mid(tip + off, mainEnd + off),
                                    label, yaziLayer, yaziStyleId, u, n);
                            }
                        }
                        else
                        {
                            double len;
                            PlaceBarWithGonye(tr, btr, mainEnd + off, tip + off, n,
                                gonyeStart: kirisGonye > 0.5, gonyeEnd: true,
                                kirisGonye > 0.5 ? kirisGonye : GonyeCm,
                                KonsolIlaveGonyeCm, donatiLayer, out len);
                            if (len >= MinSpanCm * 0.25)
                            {
                                string label = FormatLabel(ad, cp, ar, len, upper: true);
                                PlaceLabel(tr, btr, db, Mid(tip + off, mainEnd + off),
                                    label, yaziLayer, yaziStyleId, u, n);
                            }
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
                    rightOk && rightSp.HasValue ? rightSp.Value.SupportLeft : -1);
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
                    PickSteel(sp, out int ad2, out int cp2, out int ar2);
                    var m = new IlaveSeg
                    {
                        Start = A.Start,
                        End = B.End - B.Off + A.Off,
                        GonyeStart = A.GonyeStart,
                        GonyeEnd = B.GonyeEnd,
                        Adet = ad2, Cap = cp2, Aralik = ar2,
                        Off = A.Off,
                        StartSpan = A.StartSpan,
                        EndSpan = B.EndSpan
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
                double boy;
                PlaceBarWithGonye(tr, btr, sg.Start, sg.End, n, sg.GonyeStart, sg.GonyeEnd, donatiLayer, out boy);
                string label = FormatLabel(sg.Adet, sg.Cap, sg.Aralik, boy, upper: true);
                PlaceLabel(tr, btr, db, Mid(sg.Start, sg.End), label, yaziLayer, yaziStyleId, u, n);
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
            using (var probe = new Line(
                new Point3d(a.X, a.Y, 0),
                new Point3d(b.X, b.Y, 0)))
            {
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent))
                        continue;
                    if (ent is Dimension || ent is DBText || ent is MText || ent is Hatch)
                        continue;
                    if (layerOk == null || !layerOk(ent.Layer))
                        continue;

                    try
                    {
                        if (ent is Polyline pl)
                            CollectPolylineSegmentHits(pl, probe, origin, u, id, ent.Layer, hits);
                        else
                        {
                            var pts = new Point3dCollection();
                            ent.IntersectWith(probe, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                            foreach (Point3d p in pts)
                                AddHit(hits, p, origin, u, id, ent.Layer);
                        }
                    }
                    catch { }
                }
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

        /// <summary>Kapalı/açık poligon: her segment ayrı (entity.IntersectWith kapalı halkada boş kalabiliyor — OtoÖlçü gibi).</summary>
        private static void CollectPolylineSegmentHits(
            Polyline pl,
            Line probe,
            Point3d origin,
            Vector3d u,
            ObjectId id,
            string layer,
            List<HitRec> hits)
        {
            int n = pl.NumberOfVertices;
            if (n < 2) return;
            var probeSeg = new LineSegment2d(
                new Point2d(probe.StartPoint.X, probe.StartPoint.Y),
                new Point2d(probe.EndPoint.X, probe.EndPoint.Y));
            int segs = pl.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                try
                {
                    Point2d[] pts = null;
                    double bulge = pl.GetBulgeAt(i);
                    if (Math.Abs(bulge) < 1e-9)
                    {
                        Point3d a = pl.GetPoint3dAt(i);
                        Point3d b = pl.GetPoint3dAt((i + 1) % n);
                        var edge = new LineSegment2d(new Point2d(a.X, a.Y), new Point2d(b.X, b.Y));
                        pts = edge.IntersectWith(probeSeg);
                    }
                    else
                    {
                        CircularArc2d arc = pl.GetArcSegment2dAt(i);
                        pts = arc.IntersectWith(probeSeg);
                    }
                    if (pts == null) continue;
                    foreach (Point2d p in pts)
                        AddHit(hits, new Point3d(p.X, p.Y, 0), origin, u, id, layer);
                }
                catch
                {
                    // IntersectWith yedek
                    try
                    {
                        Point3d a = pl.GetPoint3dAt(i);
                        Point3d b = pl.GetPoint3dAt((i + 1) % n);
                        using (var seg = new Line(new Point3d(a.X, a.Y, 0), new Point3d(b.X, b.Y, 0)))
                        {
                            var col = new Point3dCollection();
                            seg.IntersectWith(probe, Intersect.OnBothOperands, col, IntPtr.Zero, IntPtr.Zero);
                            foreach (Point3d p in col)
                                AddHit(hits, p, origin, u, id, layer);
                        }
                    }
                    catch { }
                }
            }
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
    }
}
