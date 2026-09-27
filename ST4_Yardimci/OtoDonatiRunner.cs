using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Oto Donatı — plan üzerinde KESİT şeması (alt/üst ofsetli, gönyeler görünür).
    /// Gerçekte üstten bakınca alt+üst aynı hizada, gönye bakışa dik (görünmez);
    /// aynı aralıkta adet/gövde boyu eşit olmalı (gönye hariç). Şematik çizim sürer;
    /// adet ve gövde kirişi ortak hizadan hesaplanır.
    /// Analiz eşiği: minAdet=⌊eşik/aralık⌋; az adetli şerit komşudaki uzun demire eklenir.
    /// Stok 12 m: gövde+gerçek gönye (kalınlık−2×pas) ≤1200; bindirme ≥75ø; son &lt;300 → ilk 600.
    /// Esc / sağ tık = bitir.
    /// </summary>
    internal static class OtoDonatiRunner
    {
        private const string LayerDonati = "DONATI (BEYKENT)";
        private const string LayerYazi = "DONATI YAZISI (BEYKENT)";
        private const string LayerOlcu = "OLCU (BEYKENT)";
        private const double MinBarCm = 5.0;
        /// <summary>Bu net açıklıktan (paspayı öncesi kiriş boyu) kısa boşluklara donatı çizilmez.</summary>
        private const double MinClearOpeningCm = 40.0;
        private const double WidthChangeTolCm = 3.0;
        private const double ScanStepCm = 2.0;
        private const double AltUstExtraGapCm = 5.0;
        private const double BendRadiusCm = 3.0;
        /// <summary>Bindirme kırığı (lap crank) köşe radiusu — gönye bükümüne uygulanmaz.</summary>
        private const double CrankBendRadiusCm = 50.0;
        private const double LabelHeightCm = 10.0;
        private const double LabelGapCm = 3.0;
        /// <summary>Etiket yazı kutusu ↔ diğer yazılar min boşluk (mümkünse).</summary>
        private const double LabelMinClearCm = 40.0;
        /// <summary>Etiket yazı kutusu ↔ başka yöndeki donatı min boşluk.</summary>
        private const double LabelBarClearCm = 12.0;
        /// <summary>Etiket donatı ortasından tercih edilen max kayma.</summary>
        private const double LabelMaxShiftFromMidCm = 80.0;
        /// <summary>Ticari çubuk boyu (12 m).</summary>
        private const double StockLenCm = 1200.0;
        /// <summary>Son parça bu boydan kısaysa ilk parça 600 cm alınır.</summary>
        private const double MinRemnantCm = 300.0;
        private const double FirstPieceAltCm = 600.0;
        /// <summary>Bindirme ≥ 75ø.</summary>
        private const double LapDiameters = 75.0;
        /// <summary>Bindirme kırımı: eksen boyu (cm).</summary>
        private const double CrankAlongCm = 20.0;
        /// <summary>Bindirme kırımı: dik ofset (cm).</summary>
        private const double CrankOffsetCm = 5.0;
        /// <summary>Bindirme ölçüsü: ölçülendirilen (alt/sağ) koldan ofset (cm) — radye / alt.</summary>
        private const double LapDimOffsetCm = 17.0;
        /// <summary>Mantar üst donatı bindirme: üst/sol koldan ofset (cm).</summary>
        private const double LapDimOffsetMantarUstCm = 10.0;
        /// <summary>Gönye boyu yazı kutusu: gönyeye dik dış mesafe (cm).</summary>
        private const double GonyeLabelClearCm = 4.0;
        private const double GonyeLabelHeightCm = 8.0;
        private const string TextStyleYazi = "YAZI (BEYKENT)";

        public static void RunInteractive()
        {
            OtoDonatiSettings.EnsureLoaded();
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                OtoDonatiForm.ShowAfterPick();
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                string tip = OtoDonatiSettings.Tip == OtoDonatiTip.MantarDoseme
                    ? "Mantar döşeme"
                    : "Radye temel";
                int minAlt = MinAdetFromEsik(OtoDonatiSettings.AnalizEsigiCm, OtoDonatiSettings.AltAralikCm);
                int minUst = MinAdetFromEsik(OtoDonatiSettings.AnalizEsigiCm, OtoDonatiSettings.UstAralikCm);
                ed.WriteMessage(
                    "\nOTODONATICIZ ({0}): eşik={1:0.##}→minAdet alt≥{2} üst≥{3} | alt Φ{4}/{5} üst Φ{6}/{7}  Esc/sağ tık=bitir.",
                    tip,
                    OtoDonatiSettings.AnalizEsigiCm,
                    minAlt,
                    minUst,
                    OtoDonatiSettings.AltDonatiCapMm,
                    OtoDonatiSettings.AltAralikCm.ToString("0.##", CultureInfo.InvariantCulture),
                    OtoDonatiSettings.UstDonatiCapMm,
                    OtoDonatiSettings.UstAralikCm.ToString("0.##", CultureInfo.InvariantCulture));

                while (true)
                {
                    ObjectId polyId = PickClosedPolygon(ed,
                        "\nTemel / donatı alanı poligonu seçin (kapalı LWPOLYLINE): ");
                    if (polyId.IsNull)
                        break;

                    List<ObjectId> voidIds = PickVoidPolygons(ed);
                    int n = DrawForPolygon(doc, db, polyId, voidIds);
                    if (n < 0)
                        ed.WriteMessage("\nOTODONATICIZ: poligon okunamadı / geçersiz.");
                    else
                        ed.WriteMessage(
                            "\nOTODONATICIZ: {0} donatı (+etiket), {1} boşluk. Başka alan (Esc=bitir).",
                            n, voidIds?.Count ?? 0);
                }
            }
            catch (System.Exception ex)
            {
                try { ed.WriteMessage("\nOTODONATICIZ hata: {0}", ex.Message); } catch { }
            }
            finally
            {
                OtoDonatiForm.ShowAfterPick();
            }
        }

        private static ObjectId PickClosedPolygon(Editor ed, string message)
        {
            var opt = new PromptEntityOptions(message)
            {
                AllowNone = true
            };
            opt.SetRejectMessage("\nYalnız kapalı poligon.");
            opt.AddAllowedClass(typeof(Polyline), exactMatch: false);

            PromptEntityResult res = ed.GetEntity(opt);
            if (res.Status != PromptStatus.OK)
                return ObjectId.Null;
            return res.ObjectId;
        }

        /// <summary>Ana alan içinden eksiltilecek boşluklar (asansör çukuru vb.). Esc = tamam.</summary>
        private static List<ObjectId> PickVoidPolygons(Editor ed)
        {
            var ids = new List<ObjectId>();
            ed.WriteMessage(
                "\nPoligon içinden eksiltilecek boşlukları gösterin (asansör çukuru vb.). Bitirmek için Esc.");
            while (true)
            {
                ObjectId id = PickClosedPolygon(ed,
                    "\nEksiltilecek boşluk poligonu (bitir: Esc): ");
                if (id.IsNull)
                    break;
                if (ids.Contains(id))
                {
                    ed.WriteMessage("\nBu poligon zaten seçildi.");
                    continue;
                }
                ids.Add(id);
                ed.WriteMessage("\n  + boşluk eklendi ({0}).", ids.Count);
            }
            return ids;
        }

        private static int DrawForPolygon(
            Document doc, Database db, ObjectId polyId, List<ObjectId> voidIds)
        {
            List<Point2d> ring = null;
            var voids = new List<List<Point2d>>();
            Extents3d ext = default;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = tr.GetObject(polyId, OpenMode.ForRead) as Entity;
                if (!(ent is Polyline pl) || pl.NumberOfVertices < 3)
                {
                    tr.Commit();
                    return -1;
                }

                ring = ExtractRing(pl);
                try { ext = pl.GeometricExtents; }
                catch
                {
                    tr.Commit();
                    return -1;
                }

                if (voidIds != null)
                {
                    foreach (ObjectId vid in voidIds)
                    {
                        if (vid.IsNull || vid == polyId) continue;
                        var ve = tr.GetObject(vid, OpenMode.ForRead) as Entity;
                        if (!(ve is Polyline vpl) || vpl.NumberOfVertices < 3) continue;
                        List<Point2d> vr = ExtractRing(vpl);
                        EnsureClosed(vr);
                        if (vr.Count >= 3)
                        {
                            if (!IsRingCcw(vr)) vr.Reverse();
                            voids.Add(vr);
                        }
                    }
                }
                tr.Commit();
            }

            if (ring == null || ring.Count < 3)
                return -1;

            EnsureClosed(ring);
            if (!IsRingCcw(ring))
                ring.Reverse();

            double pas = OtoDonatiSettings.PaspayiCm;
            double gonye = OtoDonatiSettings.EffectiveGonyeBoyuCm;
            double xmin = ext.MinPoint.X;
            double xmax = ext.MaxPoint.X;
            double ymin = ext.MinPoint.Y;
            double ymax = ext.MaxPoint.Y;

            var bars = new List<BarPath>();
            var gonyeLabels = new List<GonyeLenLabel>();

            CollectPairBarsAlongY(bars, gonyeLabels, ring, voids, ymin, ymax, pas, gonye);
            CollectPairBarsAlongX(bars, gonyeLabels, ring, voids, xmin, xmax, pas, gonye);
            DeduplicateSideBySideBars(bars);

            if (bars.Count == 0)
                return 0;

            int placed = 0;
            using (doc.LockDocument())
            {
                // Kilit altında: mevcut kolon/perde/yazı kutularını oku, etiketleri kaydır, çiz
                List<TextBoxObs> existing = CollectExistingTextBoxes(db);
                RelocateLabelsAlongBars(bars, existing);

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    string donatiLyr = EnsureLayer(tr, db, LayerDonati, 4, LineWeight.LineWeight040);
                    string yaziLyr = EnsureLayer(tr, db, LayerYazi, 3, LineWeight.LineWeight020);
                    string olcuLyr = EnsureLayer(tr, db, LayerOlcu, 3, LineWeight.LineWeight030);
                    ObjectId yaziStyleId = GetOrCreateYaziBeykentTextStyle(tr, db);
                    ObjectId dimStyleId = ResolveDimStyleId(tr, db);
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    foreach (BarPath bar in bars)
                    {
                        if (bar.Pts == null || bar.Pts.Count < 2) continue;
                        PlacePolylineWithBends(tr, btr, donatiLyr, bar.Pts, bar.CrankCornerIndices);
                        if (!string.IsNullOrEmpty(bar.Label))
                            PlaceBarLabel(tr, btr, db, yaziLyr, yaziStyleId, bar);
                        if (bar.HasLapDim)
                            PlaceLapDimension(tr, btr, olcuLyr, dimStyleId, bar);
                        placed++;
                    }

                    // Gönye boyu yazısı yalnız radye temelde; mantar döşemede ölçülendirme yok.
                    if (OtoDonatiSettings.Tip == OtoDonatiTip.RadyeTemel)
                    {
                        foreach (GonyeLenLabel gl in gonyeLabels)
                            PlaceGonyeLenLabel(tr, btr, db, yaziLyr, yaziStyleId, gl);
                    }

                    tr.Commit();
                }
            }

            return placed;
        }

        /// <summary>Y boyunca tara → şematik: alt hiza + üst (gönye max+5) yukarı; gövde aynı kiriş.</summary>
        private static void CollectPairBarsAlongY(
            List<BarPath> bars, List<GonyeLenLabel> gonyeLabels,
            List<Point2d> ring, List<List<Point2d>> voids,
            double ymin, double ymax, double pas, double gonyeLen)
        {
            double y0 = ymin + pas;
            double y1 = ymax - pas;
            if (y1 - y0 < MinBarCm) return;

            List<(double a, double b)> raw = BuildZonesFromScan(
                y0, y1, y => EffectiveHorizontalChords(ring, voids, y));
            List<ZoneWork> zones = BuildZoneWorks(raw, ring, voids, horizontal: true);
            MergeSmallZonesPaired(zones);

            double gap = Math.Max(1.0, OtoDonatiSettings.GonyeBoyuMaxCm) + AltUstExtraGapCm;
            foreach (ZoneWork z in zones)
            {
                if (z.AdetAlt <= 0 && z.AdetUst <= 0) continue;

                double midAlt = FindCleanestMid(
                    z.A, z.B, gap, üstAbove: true,
                    p => EffectiveHorizontalChords(ring, voids, p));
                var chords = EffectiveHorizontalChords(ring, voids, midAlt);
                if (chords == null || chords.Count == 0) continue;

                bool hasAlt = z.AdetAlt > 0;
                bool hasUst = z.AdetUst > 0;
                if (hasAlt)
                {
                    AddBarsFromChords(
                        bars, chords, horizontal: true, midAlt, pas,
                        OtoDonatiSettings.AltGonyeSol, OtoDonatiSettings.AltGonyeSag,
                        gonyeLen,
                        OtoDonatiSettings.AltDonatiCapMm,
                        OtoDonatiSettings.AltAralikCm,
                        isUst: false,
                        z.AdetAlt);
                }
                if (hasUst)
                {
                    AddBarsFromChords(
                        bars, chords, horizontal: true, midAlt + gap, pas,
                        OtoDonatiSettings.UstGonyeSol, OtoDonatiSettings.UstGonyeSag,
                        gonyeLen,
                        OtoDonatiSettings.UstDonatiCapMm,
                        OtoDonatiSettings.UstAralikCm,
                        isUst: true,
                        z.AdetUst);
                }

                if (OtoDonatiSettings.Tip == OtoDonatiTip.RadyeTemel)
                {
                    CollectGonyeLenLabels(
                        gonyeLabels, chords, horizontal: true,
                        midAlt, midAlt + gap, pas, gonyeLen,
                        hasAlt, hasUst,
                        OtoDonatiSettings.AltGonyeSol, OtoDonatiSettings.AltGonyeSag,
                        OtoDonatiSettings.UstGonyeSol, OtoDonatiSettings.UstGonyeSag);
                }
            }
        }

        /// <summary>X boyunca tara → şematik: alt hiza + üst (gönye max+5) solda; gövde aynı kiriş.</summary>
        private static void CollectPairBarsAlongX(
            List<BarPath> bars, List<GonyeLenLabel> gonyeLabels,
            List<Point2d> ring, List<List<Point2d>> voids,
            double xmin, double xmax, double pas, double gonyeLen)
        {
            double x0 = xmin + pas;
            double x1 = xmax - pas;
            if (x1 - x0 < MinBarCm) return;

            List<(double a, double b)> raw = BuildZonesFromScan(
                x0, x1, x => EffectiveVerticalChords(ring, voids, x));
            List<ZoneWork> zones = BuildZoneWorks(raw, ring, voids, horizontal: false);
            MergeSmallZonesPaired(zones);

            double gap = Math.Max(1.0, OtoDonatiSettings.GonyeBoyuMaxCm) + AltUstExtraGapCm;
            foreach (ZoneWork z in zones)
            {
                if (z.AdetAlt <= 0 && z.AdetUst <= 0) continue;

                double midAlt = FindCleanestMid(
                    z.A, z.B, gap, üstAbove: false,
                    p => EffectiveVerticalChords(ring, voids, p));
                var chords = EffectiveVerticalChords(ring, voids, midAlt);
                if (chords == null || chords.Count == 0) continue;

                bool hasAlt = z.AdetAlt > 0;
                bool hasUst = z.AdetUst > 0;
                if (hasAlt)
                {
                    AddBarsFromChords(
                        bars, chords, horizontal: false, midAlt, pas,
                        OtoDonatiSettings.AltGonyeSol, OtoDonatiSettings.AltGonyeSag,
                        gonyeLen,
                        OtoDonatiSettings.AltDonatiCapMm,
                        OtoDonatiSettings.AltAralikCm,
                        isUst: false,
                        z.AdetAlt);
                }
                if (hasUst)
                {
                    AddBarsFromChords(
                        bars, chords, horizontal: false, midAlt - gap, pas,
                        OtoDonatiSettings.UstGonyeSol, OtoDonatiSettings.UstGonyeSag,
                        gonyeLen,
                        OtoDonatiSettings.UstDonatiCapMm,
                        OtoDonatiSettings.UstAralikCm,
                        isUst: true,
                        z.AdetUst);
                }

                if (OtoDonatiSettings.Tip == OtoDonatiTip.RadyeTemel)
                {
                    CollectGonyeLenLabels(
                        gonyeLabels, chords, horizontal: false,
                        midAlt, midAlt - gap, pas, gonyeLen,
                        hasAlt, hasUst,
                        OtoDonatiSettings.AltGonyeSol, OtoDonatiSettings.AltGonyeSag,
                        OtoDonatiSettings.UstGonyeSol, OtoDonatiSettings.UstGonyeSag);
                }
            }
        }

        /// <summary>
        /// Gönye boyu yazısı (gerçek = kalınlık−2×pas). Alt+üst aynı uçta gönyeliyse tek yazı.
        /// Yalnız radye temelde çağrılır.
        /// </summary>
        private static void CollectGonyeLenLabels(
            List<GonyeLenLabel> labels,
            List<(double a, double b)> chords,
            bool horizontal,
            double midAlt,
            double midUst,
            double pas,
            double gonyeDrawLen,
            bool hasAlt,
            bool hasUst,
            bool altSol,
            bool altSag,
            bool ustSol,
            bool ustSag)
        {
            if (labels == null || chords == null) return;
            if (gonyeDrawLen < 0.5) return;
            double realBoy = OtoDonatiSettings.RealGonyeBoyuCm;
            if (realBoy < 0.5) realBoy = gonyeDrawLen;
            string text = ((int)Math.Round(realBoy)).ToString(CultureInfo.InvariantCulture);

            foreach (var ch in chords)
            {
                // 40 cm altı açıklık: gönye yazısı da yok
                if (ch.b - ch.a < MinClearOpeningCm) continue;
                double a = ch.a + pas;
                double b = ch.b - pas;
                if (b - a < MinBarCm) continue;

                bool solAlt = hasAlt && altSol;
                bool solUst = hasUst && ustSol;
                if (solAlt || solUst)
                {
                    labels.Add(MakeGonyeLenLabel(
                        text, horizontal, a, midAlt, midUst, solAlt, solUst, isStart: true, gonyeDrawLen));
                }

                bool sagAlt = hasAlt && altSag;
                bool sagUst = hasUst && ustSag;
                if (sagAlt || sagUst)
                {
                    labels.Add(MakeGonyeLenLabel(
                        text, horizontal, b, midAlt, midUst, sagAlt, sagUst, isStart: false, gonyeDrawLen));
                }
            }
        }

        private static GonyeLenLabel MakeGonyeLenLabel(
            string text,
            bool horizontal,
            double sEnd,
            double midAlt,
            double midUst,
            bool altHook,
            bool ustHook,
            bool isStart,
            double gonyeLen)
        {
            double half = gonyeLen * 0.5;
            Point3d onGonye;
            if (horizontal)
            {
                double sum = 0;
                int n = 0;
                if (altHook) { sum += midAlt + half; n++; }
                if (ustHook) { sum += midUst - half; n++; }
                double y = n > 0 ? sum / n : midAlt;
                onGonye = new Point3d(sEnd, y, 0);
            }
            else
            {
                double sum = 0;
                int n = 0;
                if (altHook) { sum += midAlt - half; n++; }
                if (ustHook) { sum += midUst + half; n++; }
                double x = n > 0 ? sum / n : midAlt;
                onGonye = new Point3d(x, sEnd, 0);
            }

            return new GonyeLenLabel
            {
                Text = text,
                At = onGonye,
                HorizontalBar = horizontal,
                // Yatay donatı: gönye düşey → dış ±X; düşey donatı: gönye yatay → dış ±Y
                OutwardSign = isStart ? -1 : 1,
                GonyeLineCoord = sEnd
            };
        }

        /// <summary>
        /// Şerit [lo,hi] içinde uçlara yapışmadan, yoğunluğu nispeten düşük hiza.
        /// üstAbove: yatay (üst = mid+gap) → mid ∈ [lo, hi−gap];
        /// değilse düşey (üst = mid−gap) → mid ∈ [lo+gap, hi].
        /// En geniş kiriş değil: kenar payı + merkez ağırlığı + düşük parçalanma.
        /// </summary>
        private static double FindCleanestMid(
            double lo,
            double hi,
            double gap,
            bool üstAbove,
            Func<double, List<(double a, double b)>> chordsAt)
        {
            double midLo = üstAbove ? lo : lo + gap;
            double midHi = üstAbove ? hi - gap : hi;
            if (midHi < midLo + 0.5)
                return 0.5 * (lo + hi);

            double span = midHi - midLo;
            // Uç noktalara fazla yaklaşma: en az ~%18 / min 8 cm kenar payı (şerit çok kısaysa küçült).
            double edgePad = Math.Min(span * 0.18, Math.Max(8.0, span * 0.12));
            if (span < 40.0) edgePad = Math.Min(edgePad, span * 0.12);
            if (span < 20.0) edgePad = Math.Min(edgePad, span * 0.08);
            double scanLo = midLo + edgePad;
            double scanHi = midHi - edgePad;
            if (scanHi < scanLo + 0.5)
            {
                scanLo = midLo;
                scanHi = midHi;
            }

            double best = 0.5 * (scanLo + scanHi);
            double bestScore = double.NegativeInfinity;
            double step = Math.Min(ScanStepCm, Math.Max(1.0, (scanHi - scanLo) / 24.0));
            for (double p = scanLo; p <= scanHi + 1e-6; p += step)
            {
                double pp = Math.Min(p, scanHi);
                var ch = chordsAt(pp);
                if (ch == null || ch.Count == 0) continue;

                // Merkez tercihi (sin): uçlarda 0, ortada 1 — en geniş uçları cezalandırır.
                double t = span > 1e-6 ? (pp - midLo) / span : 0.5;
                t = Math.Max(0.0, Math.Min(1.0, t));
                double centerW = Math.Sin(Math.PI * t);
                centerW *= centerW; // daha güçlü orta bant

                double score = ScoreLowDensity(ch) * (0.25 + 0.75 * centerW);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = pp;
                }
                if (pp >= scanHi - 1e-9) break;
            }
            return best;
        }

        /// <summary>
        /// Düşük yoğunluk: uzun sürekli kiriş iyi; çok parçalı / kısa kırıklar kötü.
        /// Log ile aşırı geniş uçların skoru domine etmesi engellenir.
        /// </summary>
        private static double ScoreLowDensity(List<(double a, double b)> chords)
        {
            double sum = 0;
            double max = 0;
            int n = 0;
            foreach (var c in chords)
            {
                double L = c.b - c.a;
                if (L < MinBarCm * 0.5) continue;
                sum += L;
                if (L > max) max = L;
                n++;
            }
            if (n == 0 || sum < MinBarCm) return double.NegativeInfinity;
            // Parçalanma cezası: aynı toplamda daha az parça = daha sakin çizim.
            double fragPen = 1.0 + 0.65 * (n - 1);
            return (Math.Log(1.0 + sum) * 2.0 + Math.Log(1.0 + max)) / fragPen;
        }

        private static List<ZoneWork> BuildZoneWorks(
            List<(double a, double b)> raw,
            List<Point2d> ring,
            List<List<Point2d>> voids,
            bool horizontal)
        {
            var list = new List<ZoneWork>();
            foreach (var z in raw)
            {
                double mid = 0.5 * (z.a + z.b);
                double zoneLen = Math.Max(0, z.b - z.a);
                var chords = horizontal
                    ? EffectiveHorizontalChords(ring, voids, mid)
                    : EffectiveVerticalChords(ring, voids, mid);
                double maxChord = 0;
                foreach (var ch in chords)
                {
                    double L = ch.b - ch.a;
                    if (L > maxChord) maxChord = L;
                }

                int adetAlt = AdetFromAralik(zoneLen, OtoDonatiSettings.AltAralikCm);
                int adetUst = AdetFromAralik(zoneLen, OtoDonatiSettings.UstAralikCm);
                if (Math.Abs(OtoDonatiSettings.AltAralikCm - OtoDonatiSettings.UstAralikCm) < 0.05)
                {
                    int a = Math.Max(adetAlt, adetUst);
                    adetAlt = a;
                    adetUst = a;
                }

                list.Add(new ZoneWork
                {
                    A = z.a,
                    B = z.b,
                    Mid = mid,
                    ZoneLenCm = zoneLen,
                    MaxChordCm = maxChord,
                    Chords = chords,
                    AdetAlt = adetAlt,
                    AdetUst = adetUst
                });
            }
            return list;
        }

        /// <summary>
        /// Az adetli şeridi çizme; alt+üst birlikte komşudaki daha uzun demire ekle
        /// (gerçekte aynı hiza istasyonları).
        /// </summary>
        private static void MergeSmallZonesPaired(List<ZoneWork> zones)
        {
            if (zones == null || zones.Count == 0) return;
            int minAlt = MinAdetFromEsik(OtoDonatiSettings.AnalizEsigiCm, OtoDonatiSettings.AltAralikCm);
            int minUst = MinAdetFromEsik(OtoDonatiSettings.AnalizEsigiCm, OtoDonatiSettings.UstAralikCm);
            if (minAlt <= 1 && minUst <= 1) return;

            bool changed = true;
            int guard = 0;
            while (changed && guard++ < zones.Count * 4)
            {
                changed = false;
                for (int i = 0; i < zones.Count; i++)
                {
                    bool smallAlt = zones[i].AdetAlt > 0 && zones[i].AdetAlt < minAlt;
                    bool smallUst = zones[i].AdetUst > 0 && zones[i].AdetUst < minUst;
                    if (!smallAlt && !smallUst) continue;

                    int best = PickLongerNeighborPaired(zones, i);
                    if (best < 0)
                    {
                        zones[i].AdetAlt = 0;
                        zones[i].AdetUst = 0;
                        changed = true;
                        continue;
                    }

                    zones[best].AdetAlt += zones[i].AdetAlt;
                    zones[best].AdetUst += zones[i].AdetUst;
                    // Aynı aralıkta alıcıda da eşit tut
                    if (Math.Abs(OtoDonatiSettings.AltAralikCm - OtoDonatiSettings.UstAralikCm) < 0.05)
                    {
                        int a = Math.Max(zones[best].AdetAlt, zones[best].AdetUst);
                        zones[best].AdetAlt = a;
                        zones[best].AdetUst = a;
                    }
                    zones[i].AdetAlt = 0;
                    zones[i].AdetUst = 0;
                    changed = true;
                }
            }
        }

        private static int PickLongerNeighborPaired(List<ZoneWork> zones, int i)
        {
            int left = -1, right = -1;
            for (int j = i - 1; j >= 0; j--)
            {
                if (zones[j].AdetAlt > 0 || zones[j].AdetUst > 0) { left = j; break; }
            }
            for (int j = i + 1; j < zones.Count; j++)
            {
                if (zones[j].AdetAlt > 0 || zones[j].AdetUst > 0) { right = j; break; }
            }

            if (left < 0 && right < 0) return -1;
            if (left < 0) return right;
            if (right < 0) return left;

            double cL = zones[left].MaxChordCm;
            double cR = zones[right].MaxChordCm;
            if (cL > cR + 0.5) return left;
            if (cR > cL + 0.5) return right;
            int aL = Math.Max(zones[left].AdetAlt, zones[left].AdetUst);
            int aR = Math.Max(zones[right].AdetAlt, zones[right].AdetUst);
            return aL >= aR ? left : right;
        }

        /// <summary>
        /// Yalnızca gerçekten yanyana (FixedCoord yakın) aynı ölçü/hizadaki çift şematikleri birleştir.
        /// Uzak zonları / stok parçalarını birleştirmez.
        /// </summary>
        private static void DeduplicateSideBySideBars(List<BarPath> bars)
        {
            if (bars == null || bars.Count < 2) return;

            // Alt↔üst gap genelde ~45; yanyana çift < ~35 cm olmalı. IsUst zaten ayırır.
            double linkTol = 35.0;
            const double spanTol = 8.0;
            const double boyTol = 8.0;

            var keep = new bool[bars.Count];
            for (int i = 0; i < keep.Length; i++) keep[i] = true;

            for (int i = 0; i < bars.Count; i++)
            {
                if (!keep[i]) continue;
                BarPath bi = bars[i];
                if (bi == null) { keep[i] = false; continue; }

                for (int j = i + 1; j < bars.Count; j++)
                {
                    if (!keep[j]) continue;
                    BarPath bj = bars[j];
                    if (bj == null) { keep[j] = false; continue; }
                    if (bi.Horizontal != bj.Horizontal) continue;
                    if (bi.IsUst != bj.IsUst) continue;
                    if (bi.CapMm != bj.CapMm) continue;
                    if (Math.Abs(bi.AralikCm - bj.AralikCm) > 0.05) continue;
                    if (Math.Abs(bi.BoyCm - bj.BoyCm) > boyTol) continue;
                    if (Math.Abs(bi.AxisS0 - bj.AxisS0) > spanTol) continue;
                    if (Math.Abs(bi.AxisS1 - bj.AxisS1) > spanTol) continue;
                    if (Math.Abs(bi.FixedCoord - bj.FixedCoord) > linkTol) continue;

                    // Aynı hizada çift basım → adet max (toplama yok; uzak şerit değil)
                    int adet = Math.Max(bi.Adet, bj.Adet);
                    if (bj.Adet > bi.Adet)
                    {
                        bi.Pts = bj.Pts;
                        bi.CrankCornerIndices = bj.CrankCornerIndices;
                        bi.LabelAt = bj.LabelAt;
                        bi.Along = bj.Along;
                        bi.Up = bj.Up;
                        bi.FixedCoord = bj.FixedCoord;
                        bi.AxisS0 = bj.AxisS0;
                        bi.AxisS1 = bj.AxisS1;
                        bi.BoyCm = bj.BoyCm;
                        bi.HasLapDim = bj.HasLapDim;
                        bi.LapDimFrom = bj.LapDimFrom;
                        bi.LapDimTo = bj.LapDimTo;
                        bi.LapDimRefCoord = bj.LapDimRefCoord;
                        bi.LapDimOffset = bj.LapDimOffset;
                    }
                    bi.Adet = adet;
                    bi.Label = FormatLabel(bi.Adet, bi.CapMm, bi.AralikCm, bi.BoyCm, bi.IsUst);
                    keep[j] = false;
                }
            }

            for (int i = bars.Count - 1; i >= 0; i--)
            {
                if (!keep[i]) bars.RemoveAt(i);
            }
        }

        private static int MinAdetFromEsik(double esikCm, double aralikCm)
        {
            double s = aralikCm > 0.5 ? aralikCm : 15.0;
            double e = esikCm > 0.5 ? esikCm : 100.0;
            return Math.Max(1, (int)Math.Floor(e / s + 1e-9));
        }

        private static List<(double a, double b)> BuildZonesFromScan(
            double from, double to, Func<double, List<(double a, double b)>> chordsAt)
        {
            var samples = new List<Sample>();
            for (double p = from; p <= to + 1e-6; p += ScanStepCm)
            {
                double pp = Math.Min(p, to);
                samples.Add(new Sample { Pos = pp, Chords = chordsAt(pp) });
                if (pp >= to - 1e-9) break;
            }
            if (samples.Count == 0) return new List<(double a, double b)>();
            if (samples[samples.Count - 1].Pos < to - 0.5)
                samples.Add(new Sample { Pos = to, Chords = chordsAt(to) });

            return BuildZones(samples);
        }

        private static List<(double a, double b)> BuildZones(List<Sample> samples)
        {
            var zones = new List<(double a, double b)>();
            if (samples.Count == 0) return zones;

            int start = 0;
            for (int i = 1; i < samples.Count; i++)
            {
                if (!SameSignature(samples[i - 1].Chords, samples[i].Chords))
                {
                    zones.Add((samples[start].Pos, samples[i - 1].Pos));
                    start = i;
                }
            }
            zones.Add((samples[start].Pos, samples[samples.Count - 1].Pos));
            return zones;
        }

        private static bool SameSignature(List<(double a, double b)> a, List<(double a, double b)> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                double la = a[i].b - a[i].a;
                double lb = b[i].b - b[i].a;
                if (Math.Abs(la - lb) > WidthChangeTolCm) return false;
                if (Math.Abs(a[i].a - b[i].a) > WidthChangeTolCm * 2) return false;
                if (Math.Abs(a[i].b - b[i].b) > WidthChangeTolCm * 2) return false;
            }
            return true;
        }

        private static void AddBarsFromChords(
            List<BarPath> bars,
            List<(double a, double b)> chords,
            bool horizontal,
            double fixedCoord,
            double pas,
            bool gonyeStart,
            bool gonyeEnd,
            double gonyeLen,
            int capMm,
            double aralikCm,
            bool isUst,
            int adet)
        {
            if (adet <= 0 || chords == null) return;
            double realGonye = OtoDonatiSettings.RealGonyeBoyuCm;
            double phiCm = Math.Max(0.6, capMm / 10.0);
            double lap = LapDiameters * phiCm;
            if (lap < MinBarCm) lap = MinBarCm;
            if (lap > StockLenCm * 0.45) lap = StockLenCm * 0.45;

            foreach (var ch in chords)
            {
                // Net açıklık (paspayı öncesi) < 40 cm → donatı çizme (dar koridor / boşluk).
                if (ch.b - ch.a < MinClearOpeningCm) continue;
                double a = ch.a + pas;
                double b = ch.b - pas;
                if (b - a < MinBarCm) continue;

                List<(double from, double to)> segs = SplitSpanWithStockAndLap(
                    a, b, capMm, gonyeStart, gonyeEnd);
                for (int si = 0; si < segs.Count; si++)
                {
                    double s0 = segs[si].from;
                    double s1 = segs[si].to;
                    if (s1 - s0 < MinBarCm) continue;

                    bool hookStart = gonyeStart && si == 0;
                    bool hookEnd = gonyeEnd && si == segs.Count - 1;
                    // Bindirme: sonraki parçanın başında kırım (önceki düz; resimdeki gibi)
                    bool crankStart = si > 0;

                    List<int> crankCorners;
                    List<Point3d> pts = BuildBarPointsWithOptionalCrank(
                        s0, s1, horizontal, fixedCoord,
                        hookStart, hookEnd, gonyeLen, isUst,
                        crankStart, lap, out crankCorners);
                    if (pts == null || pts.Count < 2) continue;

                    // Etiket boyu = gövde + gerçek gönye (stok ile aynı; çizim gönyesi değil)
                    double boy = s1 - s0;
                    if (hookStart) boy += realGonye;
                    if (hookEnd) boy += realGonye;
                    string label = FormatLabel(adet, capMm, aralikCm, boy, isUst);

                    Point3d mid = BarPointOnAxis(0.5 * (s0 + s1), horizontal, fixedCoord, 0);
                    Vector3d along = horizontal ? Vector3d.XAxis : Vector3d.YAxis;
                    Vector3d up = StabilizeUp(along);

                    // Bindirme ölçüsü:
                    // Radye / alt: yatayda alttaki koldan −17; dikeyde sağdaki koldan +17
                    // Mantar üst: yatayda üstteki koldan +10; dikeyde soldaki koldan −10
                    double crankOff = 0.0;
                    if (crankStart)
                    {
                        crankOff = horizontal
                            ? (isUst ? -CrankOffsetCm : CrankOffsetCm)
                            : (isUst ? CrankOffsetCm : -CrankOffsetCm);
                    }
                    bool mantarUst = isUst &&
                        OtoDonatiSettings.Tip == OtoDonatiTip.MantarDoseme;
                    double lapRef;
                    double lapOff;
                    if (mantarUst)
                    {
                        lapRef = horizontal
                            ? Math.Max(fixedCoord, fixedCoord + crankOff)   // üstteki Y
                            : Math.Min(fixedCoord, fixedCoord + crankOff);  // soldaki X
                        lapOff = horizontal
                            ? LapDimOffsetMantarUstCm
                            : -LapDimOffsetMantarUstCm;
                    }
                    else
                    {
                        lapRef = horizontal
                            ? Math.Min(fixedCoord, fixedCoord + crankOff)   // alttaki Y
                            : Math.Max(fixedCoord, fixedCoord + crankOff);  // sağdaki X
                        lapOff = horizontal ? -LapDimOffsetCm : LapDimOffsetCm;
                    }

                    bars.Add(new BarPath
                    {
                        Pts = pts,
                        CrankCornerIndices = crankCorners,
                        Label = label,
                        LabelAt = mid,
                        Along = along,
                        Up = up,
                        Horizontal = horizontal,
                        FixedCoord = fixedCoord,
                        AxisS0 = s0,
                        AxisS1 = s1,
                        IsUst = isUst,
                        Adet = adet,
                        CapMm = capMm,
                        AralikCm = aralikCm,
                        BoyCm = boy,
                        HasLapDim = crankStart && lap > MinBarCm,
                        LapDimFrom = s0,
                        LapDimTo = s0 + lap,
                        LapDimRefCoord = lapRef,
                        LapDimOffset = lapOff
                    });
                }
            }
        }

        /// <summary>
        /// Etiket: önce diğer yazı (≥40) ve çapraz donatıdan uzak aday;
        /// bunlar arasında donatı ortasına en yakın. Kenara ancak zorunluysa.
        /// </summary>
        private static void RelocateLabelsAlongBars(List<BarPath> bars, List<TextBoxObs> existing)
        {
            if (bars == null || bars.Count == 0) return;
            if (existing == null) existing = new List<TextBoxObs>();

            var order = new List<int>(bars.Count);
            for (int i = 0; i < bars.Count; i++) order.Add(i);
            order.Sort((ia, ib) =>
            {
                double la = bars[ia].AxisS1 - bars[ia].AxisS0;
                double lb = bars[ib].AxisS1 - bars[ib].AxisS0;
                return lb.CompareTo(la);
            });

            foreach (int i in order)
            {
                BarPath bar = bars[i];
                if (string.IsNullOrEmpty(bar.Label) || bar.Pts == null || bar.Pts.Count < 2)
                    continue;

                double halfAlong = EstimateLabelHalfAlongCm(bar.Label);
                double span = bar.AxisS1 - bar.AxisS0;
                if (span < MinBarCm * 2) continue;

                double mid = 0.5 * (bar.AxisS0 + bar.AxisS1);
                double hardLo = bar.AxisS0 + halfAlong + 5.0;
                double hardHi = bar.AxisS1 - halfAlong - 5.0;
                if (hardHi < hardLo + 1.0)
                {
                    hardLo = bar.AxisS0;
                    hardHi = bar.AxisS1;
                }

                // Arama pencereleri: önce ortaya yakın, yetmezse genişlet
                double[] shiftCaps =
                {
                    Math.Min(LabelMaxShiftFromMidCm, Math.Max(30.0, span * 0.18)),
                    Math.Min(180.0, Math.Max(60.0, span * 0.35)),
                    Math.Max(hardHi - mid, mid - hardLo) // tüm kullanılabilir gövde
                };

                double bestS = mid;
                bool found = false;

                for (int stage = 0; stage < shiftCaps.Length && !found; stage++)
                {
                    double lo = Math.Max(hardLo, mid - shiftCaps[stage]);
                    double hi = Math.Min(hardHi, mid + shiftCaps[stage]);
                    if (hi < lo + 0.5) continue;

                    // 1) Tam geçerli (yazı≥40, demir≥12) → ortaya en yakın
                    if (TryPickLabelS(lo, hi, mid, bar, bars, i, halfAlong, existing,
                            LabelBarClearCm, LabelMinClearCm, out bestS))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    // 2) Gevşet: demir 5 / yazı 20
                    double lo = hardLo, hi = hardHi;
                    if (!TryPickLabelS(lo, hi, mid, bar, bars, i, halfAlong, existing,
                            5.0, 20.0, out bestS))
                    {
                        // 3) En az çakışan + ortaya yakın
                        bestS = PickLeastConflictS(lo, hi, mid, bar, bars, i, halfAlong, existing);
                    }
                }

                bar.LabelAt = BarPointOnAxis(bestS, bar.Horizontal, bar.FixedCoord, 0);
            }
        }

        private static bool TryPickLabelS(
            double lo, double hi, double mid,
            BarPath bar, List<BarPath> all, int selfIdx, double halfAlong,
            List<TextBoxObs> existing,
            double needBar, double needText,
            out double bestS)
        {
            bestS = mid;
            double bestMidDist = double.PositiveInfinity;
            bool any = false;
            int steps = Math.Max(14, (int)Math.Ceiling((hi - lo) / 4.0));
            double step = (hi - lo) / steps;
            for (int k = 0; k <= steps; k++)
            {
                double s = (k == steps) ? hi : lo + k * step;
                Point3d at = BarPointOnAxis(s, bar.Horizontal, bar.FixedCoord, 0);
                EvalLabelClearance(at, bar, all, selfIdx, halfAlong, existing,
                    out double minBar, out double minText);
                if (minBar < needBar || minText < needText) continue;
                double md = Math.Abs(s - mid);
                if (!any || md < bestMidDist - 1e-9)
                {
                    any = true;
                    bestMidDist = md;
                    bestS = s;
                }
            }
            return any;
        }

        private static double PickLeastConflictS(
            double lo, double hi, double mid,
            BarPath bar, List<BarPath> all, int selfIdx, double halfAlong,
            List<TextBoxObs> existing)
        {
            double bestS = mid;
            double bestScore = double.NegativeInfinity;
            int steps = Math.Max(16, (int)Math.Ceiling((hi - lo) / 4.0));
            double step = (hi - lo) / steps;
            for (int k = 0; k <= steps; k++)
            {
                double s = (k == steps) ? hi : lo + k * step;
                Point3d at = BarPointOnAxis(s, bar.Horizontal, bar.FixedCoord, 0);
                EvalLabelClearance(at, bar, all, selfIdx, halfAlong, existing,
                    out double minBar, out double minText);
                // Çakışma cezası ağır; orta mesafe hafif
                double score = Math.Min(minBar, 40.0) * 8.0
                    + Math.Min(minText, 80.0) * 10.0
                    - Math.Abs(s - mid) * 1.5;
                if (minBar < LabelBarClearCm) score -= 500.0;
                if (minText < LabelMinClearCm) score -= 700.0;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestS = s;
                }
            }
            return bestS;
        }

        private static double EstimateLabelHalfAlongCm(string label)
        {
            int n = string.IsNullOrEmpty(label) ? 8 : label.Length;
            return Math.Max(12.0, 0.5 * n * LabelHeightCm * 0.50);
        }

        private static void EvalLabelClearance(
            Point3d barRef,
            BarPath self,
            List<BarPath> all,
            int selfIdx,
            double halfAlong,
            List<TextBoxObs> existing,
            out double minBar,
            out double minText)
        {
            Vector3d along = self.Along;
            Vector3d up = self.Up;
            if (along.LengthSqrd < 1e-12)
                along = self.Horizontal ? Vector3d.XAxis : Vector3d.YAxis;
            along = along.GetNormal();
            if (up.LengthSqrd < 1e-12)
                up = StabilizeUp(along);
            else
                up = up.GetNormal();

            Point3d textCenter = barRef + up * (LabelGapCm + LabelHeightCm * 0.5);
            double halfPerp = LabelHeightCm * 0.5;

            minBar = 1e9;
            minText = 1e9;

            // Yazı kutusu örnekleri (orta + uçlar + kenarlar)
            for (int t = -2; t <= 2; t++)
            {
                Point3d p = textCenter + along * (halfAlong * t / 2.0);
                Point3d pLo = p - up * halfPerp;
                Point3d pHi = p + up * halfPerp;

                for (int j = 0; j < all.Count; j++)
                {
                    if (j == selfIdx) continue;
                    BarPath other = all[j];
                    // Kendi çiftindeki paralel donatıya (alt↔üst ofset) daha yumuşak bak:
                    // yalnız gerçekten üstüne binen / çapraz olanı sert say.
                    bool sameDir = other.Horizontal == self.Horizontal;
                    double d = Math.Min(
                        DistPointToBarPath(p, other),
                        Math.Min(DistPointToBarPath(pLo, other), DistPointToBarPath(pHi, other)));
                    if (sameDir)
                    {
                        // Paralel komşu: yalnız çok yakınsa (üstüne binme) say
                        if (d < LabelHeightCm * 0.8 && d < minBar) minBar = d;
                    }
                    else
                    {
                        if (d < minBar) minBar = d;
                    }

                    if (!string.IsNullOrEmpty(other.Label))
                    {
                        Vector3d oAlong = other.Along.LengthSqrd > 1e-12
                            ? other.Along.GetNormal()
                            : (other.Horizontal ? Vector3d.XAxis : Vector3d.YAxis);
                        Vector3d oUp = other.Up.LengthSqrd > 1e-12
                            ? other.Up.GetNormal()
                            : StabilizeUp(oAlong);
                        Point3d oCenter = other.LabelAt
                            + oUp * (LabelGapCm + LabelHeightCm * 0.5);
                        double oHalf = EstimateLabelHalfAlongCm(other.Label);
                        double sep = OrientedBoxSeparation(
                            textCenter, along, up, halfAlong, halfPerp,
                            oCenter, oAlong, oUp, oHalf, halfPerp);
                        if (sep < minText) minText = sep;
                    }
                }
            }

            if (existing != null)
            {
                for (int e = 0; e < existing.Count; e++)
                {
                    TextBoxObs ob = existing[e];
                    double sep = OrientedBoxToAabbSeparation(
                        textCenter, along, up, halfAlong, halfPerp,
                        ob.Center, ob.HalfX, ob.HalfY);
                    if (sep < minText) minText = sep;
                }
            }
        }

        private static List<TextBoxObs> CollectExistingTextBoxes(Database db)
        {
            var list = new List<TextBoxObs>();
            if (db == null) return list;
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId id in btr)
                    {
                        DBObject obj = tr.GetObject(id, OpenMode.ForRead, false);
                        if (obj == null) continue;

                        if (obj is DBText || obj is MText)
                        {
                            TryAddEntityExtents(obj as Entity, list);
                        }
                        else if (obj is BlockReference br)
                        {
                            try
                            {
                                foreach (ObjectId aid in br.AttributeCollection)
                                {
                                    var att = tr.GetObject(aid, OpenMode.ForRead, false) as AttributeReference;
                                    if (att == null || att.IsErased) continue;
                                    if (string.IsNullOrWhiteSpace(att.TextString)) continue;
                                    TryAddEntityExtents(att, list);
                                }
                            }
                            catch { }
                        }
                    }
                    tr.Commit();
                }
            }
            catch { }
            return list;
        }

        private static void TryAddEntityExtents(Entity ent, List<TextBoxObs> list)
        {
            if (ent == null || ent.IsErased) return;
            try
            {
                Extents3d ext = ent.GeometricExtents;
                double hx = 0.5 * (ext.MaxPoint.X - ext.MinPoint.X);
                double hy = 0.5 * (ext.MaxPoint.Y - ext.MinPoint.Y);
                if (hx < 0.5 && hy < 0.5) return;
                list.Add(new TextBoxObs
                {
                    Center = new Point3d(
                        0.5 * (ext.MinPoint.X + ext.MaxPoint.X),
                        0.5 * (ext.MinPoint.Y + ext.MaxPoint.Y), 0),
                    HalfX = Math.Max(hx, 2.0),
                    HalfY = Math.Max(hy, 2.0)
                });
            }
            catch { }
        }

        /// <summary>İki yönlü kutunun merkez doğrusu üzerindeki yaklaşık ayırma mesafesi (cm).</summary>
        private static double OrientedBoxSeparation(
            Point3d c1, Vector3d a1, Vector3d u1, double ha1, double hu1,
            Point3d c2, Vector3d a2, Vector3d u2, double ha2, double hu2)
        {
            Vector3d d = c2 - c1;
            double dist = d.Length;
            if (dist < 1e-9) return -ha1 - ha2;
            Vector3d dir = d / dist;
            double e1 = Math.Abs(dir.DotProduct(a1)) * ha1 + Math.Abs(dir.DotProduct(u1)) * hu1;
            double e2 = Math.Abs(dir.DotProduct(a2)) * ha2 + Math.Abs(dir.DotProduct(u2)) * hu2;
            return dist - e1 - e2;
        }

        private static double OrientedBoxToAabbSeparation(
            Point3d c, Vector3d along, Vector3d up, double ha, double hu,
            Point3d aabbC, double halfX, double halfY)
        {
            Vector3d d = aabbC - c;
            double dist = d.Length;
            if (dist < 1e-9) return -(ha + hu + halfX + halfY);
            Vector3d dir = d / dist;
            double e1 = Math.Abs(dir.DotProduct(along)) * ha + Math.Abs(dir.DotProduct(up)) * hu;
            double e2 = Math.Abs(dir.X) * halfX + Math.Abs(dir.Y) * halfY;
            return dist - e1 - e2;
        }

        private static double DistPointToBarPath(Point3d p, BarPath bar)
        {
            if (bar == null || bar.Pts == null || bar.Pts.Count < 2) return 1e9;
            double min = 1e9;
            for (int i = 0; i < bar.Pts.Count - 1; i++)
            {
                double d = DistPointToSegment2d(p, bar.Pts[i], bar.Pts[i + 1]);
                if (d < min) min = d;
            }
            return min;
        }

        private static double DistPointToSegment2d(Point3d p, Point3d a, Point3d b)
        {
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double apx = p.X - a.X, apy = p.Y - a.Y;
            double ab2 = abx * abx + aby * aby;
            if (ab2 < 1e-12)
            {
                double dx = p.X - a.X, dy = p.Y - a.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }
            double t = (apx * abx + apy * aby) / ab2;
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            double qx = a.X + t * abx - p.X;
            double qy = a.Y + t * aby - p.Y;
            return Math.Sqrt(qx * qx + qy * qy);
        }

        /// <summary>
        /// Ana eksende gövde; bindirme başında (isteğe bağlı) 75ø paralel + 20×5 kırım.
        /// Alt → üste (+Y / −X), üst → alta (−Y / +X). Resim: binen uçlardan biri kırık.
        /// crankCorners: bindirme kırığı köşe indeksleri (R=50); gönye köşeleri dahil değil.
        /// </summary>
        private static List<Point3d> BuildBarPointsWithOptionalCrank(
            double s0,
            double s1,
            bool horizontal,
            double fixedCoord,
            bool hookStart,
            bool hookEnd,
            double gonyeLen,
            bool isUst,
            bool crankStart,
            double lap,
            out List<int> crankCorners)
        {
            crankCorners = new List<int>();
            double off = horizontal
                ? (isUst ? -CrankOffsetCm : CrankOffsetCm)
                : (isUst ? CrankOffsetCm : -CrankOffsetCm);

            bool doCrank = crankStart
                && lap > MinBarCm
                && (s1 - s0) >= lap + CrankAlongCm + MinBarCm;
            double crank1 = s0 + lap + CrankAlongCm; // diyagonal bitiş / ana eksene dönüş
            if (doCrank && crank1 > s1 - MinBarCm * 0.5)
                doCrank = false;

            var pts = new List<Point3d>();

            if (doCrank)
            {
                // Uç (ofset) → 75ø paralel → 20×5 kırım → ana gövde
                Point3d tip = BarPointOnAxis(s0, horizontal, fixedCoord, off);
                if (hookStart && gonyeLen > 0.5)
                    pts.Add(HookTip(tip, horizontal, isUst, gonyeLen));
                pts.Add(tip);
                pts.Add(BarPointOnAxis(s0 + lap, horizontal, fixedCoord, off));
                crankCorners.Add(pts.Count - 1); // paralel → diyagonal
                pts.Add(BarPointOnAxis(crank1, horizontal, fixedCoord, 0));
                crankCorners.Add(pts.Count - 1); // diyagonal → gövde
                pts.Add(BarPointOnAxis(s1, horizontal, fixedCoord, 0));
            }
            else
            {
                Point3d pStart = BarPointOnAxis(s0, horizontal, fixedCoord, 0);
                if (hookStart && gonyeLen > 0.5)
                    pts.Add(HookTip(pStart, horizontal, isUst, gonyeLen));
                pts.Add(pStart);
                pts.Add(BarPointOnAxis(s1, horizontal, fixedCoord, 0));
            }

            if (hookEnd && gonyeLen > 0.5)
            {
                Point3d pEnd = BarPointOnAxis(s1, horizontal, fixedCoord, 0);
                pts.Add(HookTip(pEnd, horizontal, isUst, gonyeLen));
            }
            return pts;
        }

        private static Point3d BarPointOnAxis(
            double s, bool horizontal, double fixedCoord, double perp)
        {
            if (horizontal)
                return new Point3d(s, fixedCoord + perp, 0);
            return new Point3d(fixedCoord + perp, s, 0);
        }

        /// <summary>
        /// Stok 12 m: gövde + gerçek gönye (kalınlık−2×pas) ≤ 1200; bindirme ≥ 75ø.
        /// Son parça çeliği &lt; 300 ise ilk parça çeliği 600 cm.
        /// </summary>
        private static List<(double from, double to)> SplitSpanWithStockAndLap(
            double a, double b, int capMm, bool gonyeStart, bool gonyeEnd)
        {
            double S = b - a;
            var one = new List<(double from, double to)> { (a, b) };
            if (S < MinBarCm) return one;

            double h0 = gonyeStart ? OtoDonatiSettings.RealGonyeBoyuCm : 0.0;
            double h1 = gonyeEnd ? OtoDonatiSettings.RealGonyeBoyuCm : 0.0;

            double maxBodySingle = StockLenCm - h0 - h1;
            if (maxBodySingle < MinBarCm) maxBodySingle = MinBarCm;
            if (S <= maxBodySingle + 1e-6)
                return one;

            double phiCm = Math.Max(0.6, capMm / 10.0);
            double lap = LapDiameters * phiCm;
            if (lap < MinBarCm) lap = MinBarCm;
            if (lap > StockLenCm * 0.45)
                lap = StockLenCm * 0.45;

            double maxBodyFirst = StockLenCm - h0;
            double maxBodyMid = StockLenCm;
            double maxBodyLast = StockLenCm - h1;
            if (maxBodyFirst < MinBarCm) maxBodyFirst = MinBarCm;
            if (maxBodyLast < MinBarCm) maxBodyLast = MinBarCm;

            List<double> bodies = BuildStockPieceBodies(
                S, lap, maxBodyFirst, maxBodyMid, maxBodyLast, h0, h1);
            if (bodies == null || bodies.Count == 0)
                return one;

            return PlacePiecesWithLap(a, b, bodies, lap);
        }

        /// <summary>
        /// Gövde boyları: her parçada gövde+gönye ≤ 1200; örtü bindirmeli S.
        /// h0/h1 = uçlardaki gerçek gönye (yalnız ilk/son parçada).
        /// </summary>
        private static List<double> BuildStockPieceBodies(
            double S,
            double lap,
            double maxBodyFirst,
            double maxBodyMid,
            double maxBodyLast,
            double h0,
            double h1)
        {
            double advM = Math.Max(MinBarCm, maxBodyMid - lap);

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
            if (lastSteel >= MinRemnantCm - 1e-6 && lastBody <= maxBodyLast + 1e-6)
            {
                var lens = new List<double>(n) { maxBodyFirst };
                for (int i = 0; i < n - 2; i++)
                    lens.Add(maxBodyMid);
                lens.Add(ClampBody(lastBody, maxBodyLast));
                return lens;
            }

            // Son parça çeliği < 300 → ilk parça çeliği 600 cm
            double firstSteel = Math.Min(FirstPieceAltCm, StockLenCm);
            double firstBody = firstSteel - h0;
            if (firstBody < MinBarCm) firstBody = MinBarCm;
            if (firstBody > maxBodyFirst) firstBody = maxBodyFirst;

            double remCover = S - (firstBody - lap);
            if (remCover < MinBarCm)
                return new List<double> { ClampBody(S, Math.Min(maxBodyFirst, maxBodyLast)) };

            if (remCover <= maxBodyLast + 1e-6)
                return new List<double> { firstBody, ClampBody(remCover, maxBodyLast) };

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
            alt.Add(ClampBody(remLast, maxBodyLast));
            return alt;
        }

        /// <summary>
        /// İlk gövde + (n−2) orta sonrası son gövde.
        /// Son başlangıç = firstBody − lap + (n−2)·advMid.
        /// </summary>
        private static double LastBodyAfterFullFront(
            double S, double firstBody, double lap, double advMid, int n)
        {
            double lastStart = firstBody - lap;
            for (int i = 0; i < n - 2; i++)
                lastStart += advMid;
            return S - lastStart;
        }

        private static double ClampBody(double body, double maxBody)
        {
            if (body < MinBarCm) return MinBarCm;
            if (body > maxBody) return maxBody;
            return body;
        }

        /// <summary>Parçaları uç uca değil, en az <paramref name="lap"/> bindirmeli yerleştir; son uç tam b.</summary>
        private static List<(double from, double to)> PlacePiecesWithLap(
            double a, double b, List<double> lens, double lap)
        {
            var segs = new List<(double from, double to)>(lens.Count);
            double pos = a;
            for (int i = 0; i < lens.Count; i++)
            {
                double from = pos;
                double to = from + lens[i];
                if (i == lens.Count - 1)
                    to = b;
                if (to > b) to = b;
                if (to - from >= MinBarCm * 0.5)
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

        /// <summary>Şerit boyu / aralık → adet (uçlar dahil: ⌊L/s⌋+1).</summary>
        private static int AdetFromAralik(double zoneLenCm, double aralikCm)
        {
            double s = aralikCm > 0.5 ? aralikCm : 15.0;
            if (zoneLenCm < 0.5)
                return 1;
            return Math.Max(1, (int)Math.Floor(zoneLenCm / s + 1e-9) + 1);
        }

        private static string FormatLabel(int adet, int capMm, double aralikCm, double boyCm, bool isUst)
        {
            int boy = (int)Math.Round(boyCm);
            int ar = (int)Math.Round(aralikCm);
            string lMark = isUst ? "L" : "l";
            string side = isUst ? " (üst)" : " (alt)";
            return string.Format(CultureInfo.InvariantCulture,
                "{0}ø{1}/{2} {3}={4}{5}", adet, capMm, ar, lMark, boy, side);
        }

        /// <summary>
        /// Gönye yönü:
        /// yatay üst → aşağı; yatay alt → yukarı;
        /// düşey alt → sola; düşey üst → sağa.
        /// </summary>
        private static Point3d HookTip(Point3d end, bool horizontalBar, bool isUst, double len)
        {
            if (horizontalBar)
            {
                // Üst: aşağı (−Y); Alt: yukarı (+Y)
                double dy = isUst ? -len : len;
                return new Point3d(end.X, end.Y + dy, 0);
            }
            // Düşey alt: sola (−X); düşey üst: sağa (+X)
            double dx = isUst ? len : -len;
            return new Point3d(end.X + dx, end.Y, 0);
        }

        private static Vector3d StabilizeUp(Vector3d along)
        {
            Vector3d n = along.CrossProduct(Vector3d.ZAxis);
            if (n.Length < 1e-9)
                n = Vector3d.YAxis;
            else
                n = n.GetNormal();
            if (n.Y < -1e-9 || (Math.Abs(n.Y) < 1e-9 && n.X > 0))
                n = -n;
            return n;
        }

        // ——— Geometri ———

        /// <summary>Ana alan kirişleri − boşluk kirişleri.</summary>
        private static List<(double a, double b)> EffectiveHorizontalChords(
            List<Point2d> outer, List<List<Point2d>> voids, double y)
        {
            var baseCh = HorizontalChords(outer, y);
            if (voids == null || voids.Count == 0) return baseCh;
            var holes = new List<(double a, double b)>();
            foreach (var v in voids)
                holes.AddRange(HorizontalChords(v, y));
            return SubtractIntervals(baseCh, holes);
        }

        private static List<(double a, double b)> EffectiveVerticalChords(
            List<Point2d> outer, List<List<Point2d>> voids, double x)
        {
            var baseCh = VerticalChords(outer, x);
            if (voids == null || voids.Count == 0) return baseCh;
            var holes = new List<(double a, double b)>();
            foreach (var v in voids)
                holes.AddRange(VerticalChords(v, x));
            return SubtractIntervals(baseCh, holes);
        }

        private static List<(double a, double b)> SubtractIntervals(
            List<(double a, double b)> bases,
            List<(double a, double b)> holes)
        {
            if (bases == null || bases.Count == 0) return new List<(double a, double b)>();
            if (holes == null || holes.Count == 0) return new List<(double a, double b)>(bases);

            var holesN = NormalizeIntervals(holes);
            var cur = new List<(double a, double b)>(bases);
            foreach (var h in holesN)
            {
                var next = new List<(double a, double b)>();
                foreach (var s in cur)
                {
                    if (h.b <= s.a + 1e-6 || h.a >= s.b - 1e-6)
                    {
                        next.Add(s);
                        continue;
                    }
                    if (h.a > s.a + MinBarCm * 0.5)
                        next.Add((s.a, Math.Min(h.a, s.b)));
                    if (h.b < s.b - MinBarCm * 0.5)
                        next.Add((Math.Max(h.b, s.a), s.b));
                }
                cur = next;
            }
            return NormalizeIntervals(cur);
        }

        private static List<(double a, double b)> NormalizeIntervals(List<(double a, double b)> src)
        {
            var list = new List<(double a, double b)>();
            if (src == null) return list;
            foreach (var s in src)
            {
                double a = Math.Min(s.a, s.b);
                double b = Math.Max(s.a, s.b);
                if (b - a >= MinBarCm * 0.5)
                    list.Add((a, b));
            }
            list.Sort((p, q) => p.a.CompareTo(q.a));
            // merge overlapping
            var merged = new List<(double a, double b)>();
            foreach (var s in list)
            {
                if (merged.Count == 0) { merged.Add(s); continue; }
                var last = merged[merged.Count - 1];
                if (s.a <= last.b + 0.05)
                    merged[merged.Count - 1] = (last.a, Math.Max(last.b, s.b));
                else
                    merged.Add(s);
            }
            return merged;
        }

        private static List<(double a, double b)> HorizontalChords(List<Point2d> ring, double y)
        {
            var xs = new List<double>();
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                Point2d p0 = ring[i];
                Point2d p1 = ring[(i + 1) % n];
                if (Math.Abs(p0.Y - p1.Y) < 1e-9)
                {
                    if (Math.Abs(p0.Y - y) < 1e-6)
                    {
                        xs.Add(p0.X);
                        xs.Add(p1.X);
                    }
                    continue;
                }
                if ((p0.Y > y) == (p1.Y > y)) continue;
                double t = (y - p0.Y) / (p1.Y - p0.Y);
                if (t < -1e-9 || t > 1 + 1e-9) continue;
                xs.Add(p0.X + t * (p1.X - p0.X));
            }
            return PairSorted(xs);
        }

        private static List<(double a, double b)> VerticalChords(List<Point2d> ring, double x)
        {
            var ys = new List<double>();
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                Point2d p0 = ring[i];
                Point2d p1 = ring[(i + 1) % n];
                if (Math.Abs(p0.X - p1.X) < 1e-9)
                {
                    if (Math.Abs(p0.X - x) < 1e-6)
                    {
                        ys.Add(p0.Y);
                        ys.Add(p1.Y);
                    }
                    continue;
                }
                if ((p0.X > x) == (p1.X > x)) continue;
                double t = (x - p0.X) / (p1.X - p0.X);
                if (t < -1e-9 || t > 1 + 1e-9) continue;
                ys.Add(p0.Y + t * (p1.Y - p0.Y));
            }
            return PairSorted(ys);
        }

        private static List<(double a, double b)> PairSorted(List<double> vals)
        {
            var res = new List<(double a, double b)>();
            if (vals.Count < 2) return res;
            vals.Sort();
            for (int i = vals.Count - 1; i > 0; i--)
            {
                if (Math.Abs(vals[i] - vals[i - 1]) < 0.05)
                    vals.RemoveAt(i);
            }
            for (int i = 0; i + 1 < vals.Count; i += 2)
            {
                double a = vals[i];
                double b = vals[i + 1];
                if (b - a >= MinBarCm * 0.5)
                    res.Add((a, b));
            }
            return res;
        }

        private static List<Point2d> ExtractRing(Polyline pl)
        {
            var pts = new List<Point2d>();
            int n = pl.NumberOfVertices;
            for (int i = 0; i < n; i++)
            {
                Point3d p = pl.GetPoint3dAt(i);
                pts.Add(new Point2d(p.X, p.Y));
            }
            return pts;
        }

        private static void EnsureClosed(List<Point2d> ring)
        {
            if (ring.Count < 2) return;
            if (ring[0].GetDistanceTo(ring[ring.Count - 1]) <= 0.05 && ring.Count > 2)
                ring.RemoveAt(ring.Count - 1);
        }

        private static bool IsRingCcw(List<Point2d> ring)
        {
            double a = 0;
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                Point2d p0 = ring[i];
                Point2d p1 = ring[(i + 1) % n];
                a += p0.X * p1.Y - p1.X * p0.Y;
            }
            return a > 0;
        }

        private static Point2d RingCentroid(List<Point2d> ring)
        {
            double cx = 0, cy = 0;
            int n = ring.Count;
            if (n == 0) return new Point2d(0, 0);
            for (int i = 0; i < n; i++)
            {
                cx += ring[i].X;
                cy += ring[i].Y;
            }
            return new Point2d(cx / n, cy / n);
        }

        private static bool PointInRing(List<Point2d> ring, Point2d p)
        {
            bool inside = false;
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Point2d pi = ring[i];
                Point2d pj = ring[j];
                if (((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                    (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y + 1e-15) + pi.X))
                    inside = !inside;
            }
            return inside;
        }

        private static void PlacePolylineWithBends(
            Transaction tr, BlockTableRecord btr, string layer, List<Point3d> pts,
            List<int> crankCornerIndices = null)
        {
            if (pts == null || pts.Count < 2) return;
            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            int vi = 0;
            pl.AddVertexAt(vi++, To2d(pts[0]), 0, 0, 0);
            for (int i = 1; i < pts.Count - 1; i++)
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
                double theta = Math.Acos(dot);
                if (theta < 1e-4)
                {
                    pl.AddVertexAt(vi++, To2d(pts[i]), 0, 0, 0);
                    continue;
                }

                bool isCrank = crankCornerIndices != null && crankCornerIndices.Contains(i);
                double radius = isCrank ? CrankBendRadiusCm : BendRadiusCm;
                double trim = radius * Math.Tan(theta / 2.0);
                double maxTrim = Math.Min(lIn, lOut) * 0.45;
                if (trim > maxTrim) trim = maxTrim;
                Point2d a = To2d(pts[i]) - dIn * trim;
                Point2d b = To2d(pts[i]) + dOut * trim;
                double bulge = Math.Tan(theta / 4.0) * (cross >= 0 ? 1.0 : -1.0);
                pl.AddVertexAt(vi++, a, bulge, 0, 0);
                pl.AddVertexAt(vi++, b, 0, 0, 0);
            }
            pl.AddVertexAt(vi, To2d(pts[pts.Count - 1]), 0, 0, 0);
            pl.Closed = false;
            pl.Layer = layer;
            pl.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static Point2d To2d(Point3d p) => new Point2d(p.X, p.Y);

        private static void PlaceBarLabel(
            Transaction tr, BlockTableRecord btr, Database db, string layer,
            ObjectId textStyleId, BarPath bar)
        {
            Point3d barRef = bar.LabelAt;
            Vector3d along = bar.Along;
            Vector3d up = bar.Up;

            double ang = Math.Atan2(along.Y, along.X);
            if (ang > Math.PI / 2 + 1e-9) ang -= Math.PI;
            if (ang <= -Math.PI / 2 + 1e-9) ang += Math.PI;

            var t = new DBText();
            t.SetDatabaseDefaults();
            t.TextString = bar.Label;
            t.Height = LabelHeightCm;
            if (!textStyleId.IsNull)
                t.TextStyleId = textStyleId;
            t.Position = barRef;
            t.Rotation = 0;
            t.Layer = layer;
            t.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            t.HorizontalMode = TextHorizontalMode.TextCenter;
            t.VerticalMode = TextVerticalMode.TextBottom;
            t.AlignmentPoint = barRef;
            t.AdjustAlignment(db);
            btr.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);

            Vector3d textUp = new Vector3d(-Math.Sin(ang), Math.Cos(ang), 0);
            bool localAbove = textUp.DotProduct(up) >= 0;
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

        private static void PlaceGonyeLenLabel(
            Transaction tr, BlockTableRecord btr, Database db, string layer,
            ObjectId textStyleId, GonyeLenLabel gl)
        {
            if (gl == null || string.IsNullOrEmpty(gl.Text)) return;
            Point3d at = gl.At;
            // Yatay donatı gönyesi düşey → yazı düşey; düşey donatı gönyesi yatay → yazı yatay
            double ang = gl.HorizontalBar ? Math.PI / 2.0 : 0.0;

            var t = new DBText();
            t.SetDatabaseDefaults();
            t.TextString = gl.Text;
            t.Height = GonyeLabelHeightCm;
            if (!textStyleId.IsNull)
                t.TextStyleId = textStyleId;
            t.Position = at;
            t.Rotation = 0;
            t.Layer = layer;
            t.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            t.HorizontalMode = TextHorizontalMode.TextCenter;
            t.VerticalMode = TextVerticalMode.TextVerticalMid;
            t.AlignmentPoint = at;
            t.AdjustAlignment(db);
            btr.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);

            // Önce döndür, sonra yazı kutusu kenarını gönyeye dik 4 cm dışarı oturt
            if (Math.Abs(ang) > 1e-12)
                t.TransformBy(Matrix3d.Rotation(ang, Vector3d.ZAxis, at));

            try
            {
                Extents3d ext = t.GeometricExtents;
                double dx = 0, dy = 0;
                double line = gl.GonyeLineCoord;
                int outS = gl.OutwardSign >= 0 ? 1 : -1;

                if (gl.HorizontalBar)
                {
                    // Gönye düşey (X=line): kutu kenarı line'dan 4 cm dışarı
                    // outS=-1 → solda, kutunun MaxX = line - 4
                    // outS=+1 → sağda, kutunun MinX = line + 4
                    if (outS < 0)
                        dx = (line - GonyeLabelClearCm) - ext.MaxPoint.X;
                    else
                        dx = (line + GonyeLabelClearCm) - ext.MinPoint.X;
                    // Gönye boyunca ortala
                    double cy = 0.5 * (ext.MinPoint.Y + ext.MaxPoint.Y);
                    dy = at.Y - cy;
                }
                else
                {
                    // Gönye yatay (Y=line): kutu kenarı line'dan 4 cm dışarı
                    if (outS < 0)
                        dy = (line - GonyeLabelClearCm) - ext.MaxPoint.Y;
                    else
                        dy = (line + GonyeLabelClearCm) - ext.MinPoint.Y;
                    double cx = 0.5 * (ext.MinPoint.X + ext.MaxPoint.X);
                    dx = at.X - cx;
                }

                t.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
            }
            catch { }
        }

        /// <summary>
        /// Bindirme (75ø) ölçüsü.
        /// Radye/alt: yatayda alttan −17, dikeyde sağdan +17.
        /// Mantar üst: yatayda üstten +10, dikeyde soldan −10 (LapDimOffset).
        /// </summary>
        private static void PlaceLapDimension(
            Transaction tr, BlockTableRecord btr, string layer, ObjectId dimStyleId, BarPath bar)
        {
            if (bar == null || !bar.HasLapDim) return;
            double a = bar.LapDimFrom;
            double b = bar.LapDimTo;
            if (b - a < MinBarCm * 0.5) return;

            double refC = bar.LapDimRefCoord;
            double off = bar.LapDimOffset;
            Point3d x1, x2, dimPt;
            if (bar.Horizontal)
            {
                x1 = new Point3d(a, refC, 0);
                x2 = new Point3d(b, refC, 0);
                dimPt = new Point3d(0.5 * (a + b), refC + off, 0);
            }
            else
            {
                x1 = new Point3d(refC, a, 0);
                x2 = new Point3d(refC, b, 0);
                dimPt = new Point3d(refC + off, 0.5 * (a + b), 0);
            }

            OtoOlcuDimFactory.PlaceRotated(tr, btr, x1, x2, dimPt, dimStyleId, layer);
        }

        private static ObjectId ResolveDimStyleId(Transaction tr, Database db)
        {
            try
            {
                var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                string[] prefer =
                {
                    OtoOlcuSettings.DefaultDimStyleName, // PLAN_OLCU
                    OtoOlcuSettings.AksOlcuDimStyleName, // AKS_OLCU
                    "Standard"
                };
                foreach (string name in prefer)
                {
                    if (dst.Has(name))
                        return dst[name];
                }
            }
            catch { }
            return db.Dimstyle;
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

        private static string EnsureLayer(Transaction tr, Database db, string name, short aci, LineWeight lw)
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

        private struct Sample
        {
            public double Pos;
            public List<(double a, double b)> Chords;
        }

        private sealed class ZoneWork
        {
            public double A, B, Mid, ZoneLenCm, MaxChordCm;
            public List<(double a, double b)> Chords;
            public int AdetAlt, AdetUst;
        }

        private sealed class TextBoxObs
        {
            public Point3d Center;
            public double HalfX;
            public double HalfY;
        }

        private sealed class GonyeLenLabel
        {
            public string Text;
            /// <summary>Gönye ortası (gönye çizgisi üzerinde).</summary>
            public Point3d At;
            /// <summary>true = yatay donatı (gönye düşey yazı).</summary>
            public bool HorizontalBar;
            /// <summary>Gönye çizgisinin sabit koordinatı (H→X, V→Y).</summary>
            public double GonyeLineCoord;
            /// <summary>Dış yön: başlangıç ucu −1, bitiş ucu +1.</summary>
            public int OutwardSign;
        }

        private sealed class BarPath
        {
            public List<Point3d> Pts;
            /// <summary>Pts içinde bindirme kırığı köşe indeksleri (R=50 cm).</summary>
            public List<int> CrankCornerIndices;
            public string Label;
            public Point3d LabelAt;
            public Vector3d Along;
            public Vector3d Up;
            public bool Horizontal;
            public double FixedCoord;
            public double AxisS0;
            public double AxisS1;
            public bool IsUst;
            public int Adet;
            public int CapMm;
            public double AralikCm;
            public double BoyCm;
            /// <summary>Kırımlı bindirme ucunda 75ø ölçüsü çizilsin.</summary>
            public bool HasLapDim;
            public double LapDimFrom;
            public double LapDimTo;
            /// <summary>Ölçü hattının dayandığı kol (yatay Y / dikey X).</summary>
            public double LapDimRefCoord;
            /// <summary>Yatayda ΔY, dikeyde ΔX (işaretli ofset).</summary>
            public double LapDimOffset;
        }
    }
}
