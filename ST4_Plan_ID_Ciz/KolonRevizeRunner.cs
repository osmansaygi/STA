using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// KOLONREVIZE: seçilen IC ANTET içindeki kolon / perde başlığı düşey açılımında
    /// düşey donatı çapını alan korunarak değiştirir. ST4/GPR yok; veri yalnız çizimden.
    /// Kesit (düşey, 2./3. etriye, çiroz, açılımlar) KOLONDUSEY kurallarıyla yeniden çizilir;
    /// bindirme TS 500 ℓb ile orta 1/3'te yeniden hesaplanır.
    /// </summary>
    public static class KolonRevizeRunner
    {
        private const string LayerIcAntet = "IC ANTET (BEYKENT)";
        private const string LayerIcOlcek = "IC OLCEK (BEYKENT)";
        private const string LayerDonati = "DONATI (BEYKENT)";
        private const string LayerDonatiYazisi = "DONATI YAZISI (BEYKENT)";
        private const string LayerEtriye = "ETRIYE (BEYKENT)";
        private const string LayerCiroz = "CIROZ (BEYKENT)";
        private const string LayerKesitGorunus = "KESIT GORUNUS (BEYKENT)";
        private const string LayerKolon = "KOLON (BEYKENT)";
        private const string LayerPerde = "PERDE (BEYKENT)";

        private static readonly Regex RebarTokenRx = new Regex(
            @"(\d+)\s*[x×*]\s*(\d+)\s*[\u00F8ØøφΦ]\s*(\d{1,2})|(\d+)\s*[\u00F8ØøφΦ]\s*(\d{1,2})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex LEqualsRx = new Regex(
            @"L\s*=\s*(\d+(?:[.,]\d+)?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex AcilimTagRx = new Regex(
            @"^((?:\d+x)*)(\d+)\u00F8(\d{1,2})\s*L=(\d+)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex EtriyeBolgeRx = new Regex(
            @"^((?:\d+x)*)(\d+)\u00F8(\d{1,2})/(\d+)$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private const double InsidePadCm = 0.5;

        private struct BarSeg
        {
            public ObjectId Id;
            public double X, Y0, Y1;
            public int NVerts;
        }

        private struct VDim
        {
            public ObjectId Id;
            public double X, Lo, Hi, Meas;
        }

        private struct CircleBar
        {
            public ObjectId Id;
            public Point2d Center;
            public double Radius;
        }

        /// <summary>CommandEntry.KOLONREVIZE üzerinden çağrılır.</summary>
        public static void Run()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                var peo = new PromptEntityOptions(
                    "\nKOLONREVIZE: IC ANTET cercevesini veya icindeki bir nesneyi secin: ");
                peo.SetRejectMessage("\nEntity secin.");
                peo.AllowNone = false;
                var per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK) return;

                int oldDia, oldCount;
                using (var trAsk = db.TransactionManager.StartTransaction())
                {
                    if (!TryResolveAntetBox(trAsk, db, per.ObjectId, out Extents3d antetBox, out ObjectId antetId))
                    {
                        ed.WriteMessage("\nKOLONREVIZE: IC ANTET (BEYKENT) cercevesi bulunamadi.");
                        return;
                    }
                    var contents = CollectEntitiesInside(trAsk, db, antetBox, antetId);
                    if (!TryDetectVerticalDiaAndCount(trAsk, contents, out oldDia, out oldCount))
                    {
                        ed.WriteMessage(
                            "\nKOLONREVIZE: dusey donati (kolon/perde basligi) yazidan okunamadi (or. 20ø14).");
                        return;
                    }
                    if (oldCount < 4)
                        oldCount = ResolveOldBarCount(trAsk, contents, oldDia);
                    trAsk.Commit();
                }
                if (oldCount < 4)
                {
                    ed.WriteMessage("\nKOLONREVIZE: ø{0} icin adet bulunamadi.", oldDia);
                        return;
                    }
                ed.WriteMessage("\nKOLONREVIZE: cizimden dusey donati = {0}ø{1}", oldCount, oldDia);

                var newRes = ed.GetInteger(new PromptIntegerOptions(
                    "\nYeni cap (mm) [mevcut ø" + oldDia.ToString(CultureInfo.InvariantCulture) + "]: ")
                    {
                        AllowNone = false,
                        AllowNegative = false,
                        AllowZero = false
                });
                    if (newRes.Status != PromptStatus.OK) return;
                int newDia = newRes.Value;
                    if (newDia < 6 || newDia > 40)
                    {
                        ed.WriteMessage("\nKOLONREVIZE: yeni cap 6..40 mm olmali.");
                        return;
                    }
                    if (newDia == oldDia)
                    {
                        ed.WriteMessage("\nKOLONREVIZE: yeni cap eski cap ile ayni.");
                        return;
                    }

                int fckDef = LoadSonBetonSinifi();
                string fckDefStr = fckDef.ToString(CultureInfo.InvariantCulture);
                var fckRes = ed.GetInteger(new PromptIntegerOptions(
                    "\nBeton sinifi (C30 icin 30) <" + fckDefStr + ">: ")
                {
                    AllowNone = true,
                    AllowNegative = false,
                    AllowZero = false,
                    DefaultValue = fckDef,
                    UseDefaultValue = true
                });
                if (fckRes.Status != PromptStatus.OK && fckRes.Status != PromptStatus.None) return;
                int fck = fckRes.Status == PromptStatus.None ? fckDef : fckRes.Value;
                if (fck < 16 || fck > 80)
                {
                    ed.WriteMessage("\nKOLONREVIZE: beton sinifi 16..80 olmali.");
                    return;
                }
                SaveSonBetonSinifi(fck);

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    if (!TryResolveAntetBox(tr, db, per.ObjectId, out Extents3d antetBox, out ObjectId antetId))
                    {
                        ed.WriteMessage("\nKOLONREVIZE: IC ANTET cercevesi bulunamadi.");
                        return;
                    }
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    var contents = CollectEntitiesInside(tr, db, antetBox, antetId);

                    if (!TryFindSection(tr, contents, out Extents3d sec, out var oldCircles, out double k))
                    {
                        ed.WriteMessage("\nKOLONREVIZE: kolon kesiti (KOLON/PERDE cercevesi + dusey cemberler) bulunamadi.");
                        return;
                    }
                    double wCm = (sec.MaxPoint.X - sec.MinPoint.X) / k;
                    double hCm = (sec.MaxPoint.Y - sec.MinPoint.Y) / k;
                    double shortCm = Math.Min(wCm, hCm), longCm = Math.Max(wCm, hCm);
                    if (longCm >= 6.0 * shortCm - 0.01)
                    {
                        ed.WriteMessage("\nKOLONREVIZE: perde kesiti desteklenmiyor (yalniz kolon / perde basligi).");
                        return;
                    }
                    bool perdeBasligi = shortCm < 30.0 - 0.01;

                    double tol = 0.3 * k;
                    double xElevMin = ResolveElevationMinX(tr, contents, sec, k);
                    double stripTop = ResolveStripTop(tr, contents, antetBox);

                    ReadEtriyeAcilim(tr, contents, sec, xElevMin, stripTop, k,
                        out int etDia, out int etAdet, out string grupOnek);
                    if (etDia < 6)
                    {
                        ed.WriteMessage("\nKOLONREVIZE: etriye acilim yazisi (or. 39ø10 L=168) bulunamadi.");
                        return;
                    }

                    int newCount = ComputeNewBarCountAreaPreserving(oldCount, oldDia, newDia);
                    double asOld = oldCount * Math.PI * oldDia * oldDia / 400.0;
                    double asNew = newCount * Math.PI * newDia * newDia / 400.0;
                    int nText = RewriteRebarTexts(tr, contents, oldDia, oldCount, newDia, newCount);

                    double lbNew = PlanIdDrawingManager.KolonRevizeLbCm(newDia, fck);
                    var bars = CollectBarSegs(tr, contents, xElevMin, k);
                    var vdims = CollectVerticalDims(tr, contents);
                    var barDims = vdims.Where(d => d.X >= xElevMin - k && IsOnBar(d, bars, k)).ToList();

                    var remaps = new List<(double yOld, double yNew)>();
                    var yCbots = new List<double>();
                    var uyarilar = new List<string>();
                    ComputeLapRemaps(barDims, vdims, perdeBasligi, oldDia, newDia, fck, lbNew, k, tol,
                        remaps, yCbots, uyarilar, out double lapOld, out double lapNew);

                    var lineTpl = CollectEtriyeLines(tr, contents, xElevMin, k);
                    int dEtAdet = RearrangeLapEtriyeleri(tr, btr, contents, vdims, barDims, lineTpl, remaps,
                        xElevMin, k, tol, out int nEtBolge);
                    contents = contents.Where(id => !id.IsErased).ToList();

                    var deltaL = new Dictionary<ObjectId, double>();
                    int nRemap = ApplyRemaps(tr, bars, barDims, remaps, tol, k, deltaL);
                    int nHook = UpdateFilizHooks(tr, contents, xElevMin, newDia, lbNew, yCbots, k, deltaL);
                    int nL = UpdateAcilimLengthLabels(tr, contents, bars, deltaL, k);

                    // Kesit: düşey, etriyeler, çiroz ve etriye/çiroz açılımı sil → kurallarla yeniden çiz.
                        int nSil = EraseSectionAndAcilim(tr, contents, sec, xElevMin, stripTop, k, oldCircles);
                    contents = contents.Where(id => !id.IsErased).ToList();
                    int etAdetYeni = Math.Max(1, etAdet + dEtAdet);
                    var beforeKesit = db.Handseed.Value;
                    PlanIdDrawingManager.KolonRevizeKesitCiz(tr, btr,
                        sec.MinPoint.X, sec.MinPoint.Y, sec.MaxPoint.X, sec.MaxPoint.Y, k,
                        newCount, newDia, etDia, etAdetYeni, perdeBasligi, out var yuzXs);
                    ApplyGrupOnek(tr, btr, beforeKesit, grupOnek);

                    int nGor = RedistributeElevationBars(tr, btr, contents, xElevMin, oldCircles, yuzXs, k);

                    tr.Commit();
                    ed.WriteMessage(
                        "\nKOLONREVIZE: {0}ø{1} → {2}ø{3}  As={4:0.##}→{5:0.##} cm²  C{6}: ℓb={7:0} cm, bindirme {8:0}→{9:0} cm.",
                        oldCount, oldDia, newCount, newDia, asOld, asNew, fck, lbNew, lapOld, lapNew);
                    ed.WriteMessage(
                        "\nKOLONREVIZE: yazi={0}, kesit silinen={1}, gorunus cubuk={2}, kot kaydirma={3}, filiz kanca={4}, L yazisi={5}, etriye bolge={6}, etriye adet {7}→{8}.",
                        nText, nSil, nGor, nRemap, nHook, nL, nEtBolge, etAdet, etAdetYeni);
                    foreach (string u in uyarilar)
                        ed.WriteMessage("\nKOLONREVIZE uyari: " + u);
                }

                AcadDocumentViewUtil.RegenWithoutNestedCommand(doc);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nKOLONREVIZE hata: {0}", ex.Message);
            }
        }

        private static int? _sonBetonSinifi;

        private static string BetonSinifiDosya => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ST4_Plan_ID_Ciz", "kolonrevize_beton.txt");

        /// <summary>Son girilen beton sınıfı (oturum + dosya); yoksa 30.</summary>
        private static int LoadSonBetonSinifi()
        {
            if (_sonBetonSinifi.HasValue) return _sonBetonSinifi.Value;
            int v = 30;
            try
            {
                if (System.IO.File.Exists(BetonSinifiDosya)
                    && int.TryParse(System.IO.File.ReadAllText(BetonSinifiDosya).Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int f)
                    && f >= 16 && f <= 80)
                    v = f;
            }
            catch { }
            _sonBetonSinifi = v;
            return v;
        }

        private static void SaveSonBetonSinifi(int fck)
        {
            _sonBetonSinifi = fck;
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(BetonSinifiDosya));
                System.IO.File.WriteAllText(BetonSinifiDosya, fck.ToString(CultureInfo.InvariantCulture));
            }
            catch { }
        }

        // ───────────────────────── Bindirme (ℓb) ─────────────────────────

        private static void ComputeLapRemaps(
            List<VDim> barDims, List<VDim> allDims, bool perdeBasligi,
            int oldDia, int newDia, int fck, double lbNew, double k, double tol,
            List<(double yOld, double yNew)> remaps, List<double> yCbots, List<string> uyarilar,
            out double lapOld, out double lapNew)
        {
            lapOld = 0;
            lapNew = 0;
            void AddRemap(double yo, double yn)
            {
                if (Math.Abs(yn - yo) < 0.01) return;
                foreach (var r in remaps)
                    if (Math.Abs(r.yOld - yo) < tol) return;
                remaps.Add((yo, yn));
            }

            if (perdeBasligi)
            {
                // Perde başlığı: filiz ve kat geçişi 1,50 ℓb (kolon alt kotundan / kat üstünden).
                lapNew = CeilTo5Cm(Math.Max(1.50 * lbNew, 30.0));
                foreach (var d in barDims)
                {
                    int v = (int)Math.Round(d.Meas);
                    if (!IsPerdeLapLike(v, oldDia)) continue;
                    lapOld = v;
                    yCbots.Add(d.Lo);
                    AddRemap(d.Hi, d.Lo + lapNew * k);
                }
                if (lapOld < 1.0)
                    uyarilar.Add("perde basligi bindirme olcusu (1,50 ℓb) bulunamadi; bindirme boylari degismedi.");
                return;
            }

            lapNew = CeilTo5Cm(Math.Max(lbNew, 30.0));
            var inPair = new HashSet<ObjectId>();
            var pairs = new List<(VDim a, VDim b)>();
            foreach (var a in barDims)
            {
                foreach (var b in barDims)
                {
                    if (a.Id == b.Id) continue;
                    if (Math.Abs(a.X - b.X) > tol || Math.Abs(a.Hi - b.Lo) > tol) continue;
                    pairs.Add((a, b));
                    inPair.Add(a.Id);
                    inPair.Add(b.Id);
                }
            }
            if (pairs.Count == 0)
            {
                uyarilar.Add("bindirme olculeri (kolon alti → ℓb basi → ℓb sonu) bulunamadi; bindirme boylari degismedi.");
                return;
            }

            var hnIds = new HashSet<ObjectId>();
            bool sigmadi = false, hnTahmin = false;
            foreach (var (a, b) in pairs)
            {
                double offBot = Math.Round(a.Meas);
                double lOld = Math.Round(b.Meas);
                lapOld = lOld;
                double yC = a.Lo;
                yCbots.Add(yC);

                // Net yükseklik: kolon altından başlayan ve CeilTo5(hn/3) = ℓb başı olan ölçü.
                double hn = double.NaN;
                foreach (var d in allDims)
                {
                    if (Math.Abs(d.Lo - yC) > 0.5 * k) continue;
                    if (d.Meas < offBot + lOld - 0.5) continue;
                    if (Math.Abs(CeilTo5Cm(d.Meas / 3.0) - offBot) > 0.01) continue;
                    if (double.IsNaN(hn) || d.Meas < hn)
                    {
                        hn = d.Meas;
                        hnIds.Add(d.Id);
                    }
                }
                if (double.IsNaN(hn))
                {
                    hn = 3.0 * offBot;
                    hnTahmin = true;
                }

                PlanIdDrawingManager.KolonRevizeOrtUcBindirme(hn, lapNew, out double ob, out double ot);
                if (ot - ob < lapNew - 0.01) sigmadi = true;
                AddRemap(a.Hi, yC + ob * k);
                AddRemap(b.Hi, yC + ot * k);
            }

            // Üst kata çıkan çubuk: kat üstünden üst katın ℓb sonuna (ℓb başı + ℓb).
            foreach (var d in barDims)
            {
                if (inPair.Contains(d.Id) || hnIds.Contains(d.Id)) continue;
                double ext = Math.Round(d.Meas);
                double offBotN = ext - lapOld;
                if (offBotN < 5.0) continue;
                double lN = Math.Min(lapNew, FloorTo5Cm(offBotN));
                if (lN < lapNew - 0.01) sigmadi = true;
                AddRemap(d.Hi, d.Lo + (offBotN + lN) * k);
            }

            if (sigmadi)
                uyarilar.Add("yeni ℓb bindirmesi net yuksekligin orta 1/3'une sigmiyor; orta 1/3 ile sinirlandi (perde duzeni gerekebilir).");
            if (hnTahmin)
                uyarilar.Add("net kat yuksekligi olcusu okunamadi; ℓb basindan (hn/3) tahmin edildi.");
        }

        private static bool IsPerdeLapLike(int v, int oldDia)
        {
            for (int f = 16; f <= 60; f += 1)
            {
                double lb = PlanIdDrawingManager.KolonRevizeLbCm(oldDia, f);
                if ((int)Math.Round(CeilTo5Cm(Math.Max(1.50 * lb, 30.0))) == v) return true;
            }
            return false;
        }

        /// <summary>Donatı uç noktaları ve donatı üstü ölçü tanım noktalarını eski kot → yeni kot taşır.</summary>
        private static int ApplyRemaps(
            Transaction tr, List<BarSeg> bars, List<VDim> barDims,
            List<(double yOld, double yNew)> remaps, double tol, double k,
            Dictionary<ObjectId, double> deltaL)
        {
            if (remaps.Count == 0) return 0;
            int n = 0;
            bool Find(double y, out double yn)
            {
                foreach (var r in remaps)
                {
                    if (Math.Abs(r.yOld - y) < tol) { yn = r.yNew; return true; }
                }
                yn = y;
                return false;
            }
            void AddDelta(ObjectId id, double dCm)
            {
                deltaL.TryGetValue(id, out double v);
                deltaL[id] = v + dCm;
            }

            foreach (var id in bars.Select(b => b.Id).Distinct())
            {
                Entity ent;
                try { ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity; }
                catch { continue; }
                if (ent is Line ln)
                {
                    var s = ln.StartPoint;
                    var e = ln.EndPoint;
                    if (Math.Abs(s.X - e.X) > 0.01) continue;
                    bool sUp = s.Y > e.Y;
                    if (Find(s.Y, out double ys))
                    {
                        AddDelta(id, (sUp ? ys - s.Y : s.Y - ys) / k);
                        ln.StartPoint = new Point3d(s.X, ys, s.Z);
                        n++;
                    }
                    if (Find(e.Y, out double ye))
                    {
                        AddDelta(id, (sUp ? e.Y - ye : ye - e.Y) / k);
                        ln.EndPoint = new Point3d(e.X, ye, e.Z);
                        n++;
                    }
                }
                else if (ent is Polyline pl && !pl.Closed && pl.NumberOfVertices >= 2)
                {
                    int last = pl.NumberOfVertices - 1;
                    foreach (int i in new[] { 0, last })
                    {
                        var p = pl.GetPoint2dAt(i);
                        var q = pl.GetPoint2dAt(i == 0 ? 1 : last - 1);
                        if (Math.Abs(p.X - q.X) > 0.01) continue;
                        if (!Find(p.Y, out double yn)) continue;
                        bool up = p.Y > q.Y;
                        AddDelta(id, (up ? yn - p.Y : p.Y - yn) / k);
                        pl.SetPointAt(i, new Point2d(p.X, yn));
                        n++;
                    }
                }
            }

            foreach (var d in barDims)
            {
                Entity ent;
                try { ent = tr.GetObject(d.Id, OpenMode.ForWrite, false) as Entity; }
                catch { continue; }
                if (ent is AlignedDimension ad)
                {
                    if (Find(ad.XLine1Point.Y, out double y1))
                        ad.XLine1Point = new Point3d(ad.XLine1Point.X, y1, 0);
                    if (Find(ad.XLine2Point.Y, out double y2))
                        ad.XLine2Point = new Point3d(ad.XLine2Point.X, y2, 0);
                }
                else if (ent is RotatedDimension rd)
                {
                    if (Find(rd.XLine1Point.Y, out double y1))
                        rd.XLine1Point = new Point3d(rd.XLine1Point.X, y1, 0);
                    if (Find(rd.XLine2Point.Y, out double y2))
                        rd.XLine2Point = new Point3d(rd.XLine2Point.X, y2, 0);
                }
            }
            return n;
        }

        /// <summary>
        /// Temel filizi alt gönyesi: 12φ; temel içi a + b &lt; 0,75 ℓb ise b uzatılır (KOLONDUSEY ile aynı).
        /// </summary>
        private static int UpdateFilizHooks(
            Transaction tr, List<ObjectId> contents, double xElevMin, int newDia, double lbNew,
            List<double> yCbots, double k, Dictionary<ObjectId, double> deltaL)
        {
            int n = 0;
            var usedTxt = new HashSet<ObjectId>();
            var texts = contents.Where(id => IsOnLayer(tr, id, LayerDonatiYazisi)).ToList();
            foreach (ObjectId id in contents)
            {
                Entity ent;
                try { ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity; }
                catch { continue; }
                if (!(ent is Polyline pl) || pl.Closed || pl.NumberOfVertices < 4) continue;
                if (!string.Equals(pl.Layer ?? "", LayerDonati, StringComparison.OrdinalIgnoreCase)) continue;
                var v0 = pl.GetPoint2dAt(0);
                var v1 = pl.GetPoint2dAt(1);
                var v2 = pl.GetPoint2dAt(2);
                var v3 = pl.GetPoint2dAt(3);
                if (Math.Abs(v0.Y - v1.Y) > 0.01) continue;
                if (Math.Abs(v2.X - v3.X) > 0.01 || v3.Y < v2.Y + 10.0 * k) continue;
                double barX = v3.X;
                if (barX < xElevMin - k) continue;
                double bOld = Math.Abs(v0.X - barX) / k;
                double bOldR = Math.Round(bOld / 5.0) * 5.0;
                if (bOldR < 5.0 || Math.Abs(bOld - bOldR) > 0.6) continue;
                double dir = v0.X >= barX ? 1.0 : -1.0;

                double bNew = CeilTo5Cm(12.0 * newDia / 10.0);
                double yC = yCbots.Where(y => y >= v0.Y - 0.01).DefaultIfEmpty(double.NaN).Min();
                if (!double.IsNaN(yC))
                {
                    double a = (yC - v0.Y) / k;
                    double lbk = 0.75 * lbNew;
                    if (a + bNew < lbk) bNew = CeilTo5Cm(Math.Max(bNew, lbk - Math.Max(a, 0)));
                }
                if (Math.Abs(bNew - bOldR) < 0.01) continue;

                pl.UpgradeOpen();
                pl.SetPointAt(0, new Point2d(barX + dir * bNew * k, v0.Y));
                deltaL.TryGetValue(id, out double dl);
                deltaL[id] = dl + (bNew - bOldR);
                n++;

                string oldStr = bOldR.ToString("0", CultureInfo.InvariantCulture);
                double hx0 = Math.Min(barX, v0.X) - 2.0 * k, hx1 = Math.Max(barX, v0.X) + 2.0 * k;
                foreach (ObjectId tid in texts)
                {
                    if (usedTxt.Contains(tid)) continue;
                    if (!(tr.GetObject(tid, OpenMode.ForRead, false) is DBText t)) continue;
                    if (!string.Equals((t.TextString ?? "").Trim(), oldStr, StringComparison.Ordinal)) continue;
                    if (Math.Abs(Math.Sin(t.Rotation)) > 0.05) continue;
                    if (!TryGetExtents(t, out Extents3d te)) continue;
                    double cx = 0.5 * (te.MinPoint.X + te.MaxPoint.X);
                    double cy = 0.5 * (te.MinPoint.Y + te.MaxPoint.Y);
                    if (cx < hx0 || cx > hx1 || cy < v0.Y || cy > v0.Y + 25.0 * k) continue;
                    t.UpgradeOpen();
                    t.TextString = bNew.ToString("0", CultureInfo.InvariantCulture);
                    t.TransformBy(Matrix3d.Displacement(new Vector3d(dir * 0.5 * (bNew - bOldR) * k, 0, 0)));
                    usedTxt.Add(tid);
                    break;
                }
            }
            return n;
        }

        /// <summary>Açılım çubuğu etiketi "nøφ L=…": boy farkı çubuğa eklenir.</summary>
        private static int UpdateAcilimLengthLabels(
            Transaction tr, List<ObjectId> contents, List<BarSeg> bars,
            Dictionary<ObjectId, double> deltaL, double k)
        {
            int n = 0;
            double offX = k > 1.5 ? -10.0 : 0.0;
            var labels = new List<(ObjectId id, double cx, double cy)>();
            foreach (ObjectId id in contents)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead, false) is DBText t)) continue;
                if (!string.Equals(t.Layer ?? "", LayerDonatiYazisi, StringComparison.OrdinalIgnoreCase)) continue;
                if (!LEqualsRx.IsMatch(t.TextString ?? "")) continue;
                if (Math.Abs(Math.Cos(t.Rotation)) > 0.05) continue;
                if (!TryGetExtents(t, out Extents3d te)) continue;
                labels.Add((id, 0.5 * (te.MinPoint.X + te.MaxPoint.X), 0.5 * (te.MinPoint.Y + te.MaxPoint.Y)));
            }
            var used = new HashSet<ObjectId>();
            foreach (var kv in deltaL)
            {
                int dl = (int)Math.Round(kv.Value);
                if (dl == 0) continue;
                var seg = bars.Where(b => b.Id == kv.Key).OrderByDescending(b => b.Y1 - b.Y0).FirstOrDefault();
                if (seg.Id.IsNull) continue;
                double want = seg.X + offX;
                ObjectId best = ObjectId.Null;
                double bestD = 6.0 * k;
                foreach (var l in labels)
                {
                    if (used.Contains(l.id)) continue;
                    if (l.cy < seg.Y0 - 5.0 * k || l.cy > seg.Y1 + 5.0 * k) continue;
                    double d = Math.Abs(l.cx - want);
                    if (d < bestD) { bestD = d; best = l.id; }
                }
                if (best.IsNull) continue;
                var txt = (DBText)tr.GetObject(best, OpenMode.ForWrite, false);
                txt.TextString = LEqualsRx.Replace(txt.TextString, m =>
                    {
                        if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out double L))
                            return m.Value;
                    return "L=" + Math.Max(5.0, L + dl).ToString("0", CultureInfo.InvariantCulture);
                });
                used.Add(best);
                n++;
            }
            return n;
        }

        // ───────────────────── Görünüş etriye düzeni (ℓb sınırı) ─────────────────────

        /// <summary>
        /// ℓb sınırlarında sabit etriye varsa (iki sarılma arası &gt; 100 cm) sınırlar yeni kota taşınır;
        /// komşu dilimlerin kopya etriyeleri, zincir ölçüleri ve "nøφ/s" etiketleri yeniden kurulur.
        /// </summary>
        /// <returns>Etriye toplam adet farkı.</returns>
        private static int RearrangeLapEtriyeleri(
            Transaction tr, BlockTableRecord btr, List<ObjectId> contents,
            List<VDim> vdims, List<VDim> barDims, List<(ObjectId id, double y, double x0, double x1)> lines,
            List<(double yOld, double yNew)> remaps, double xElevMin, double k, double tol, out int nBolge)
        {
            nBolge = 0;
            if (remaps.Count == 0 || lines.Count == 0) return 0;
            bool HasLine(double y) => lines.Any(l => Math.Abs(l.y - y) < tol);
            var moved = remaps.Where(r => HasLine(r.yOld)).OrderBy(r => r.yOld).ToList();
            if (moved.Count == 0) return 0;

            var barIds = new HashSet<ObjectId>(barDims.Select(d => d.Id));
            var etDims = vdims.Where(d => !barIds.Contains(d.Id) && d.X >= xElevMin - k
                && HasLine(d.Lo) && HasLine(d.Hi)).ToList();
            var chain = etDims.Where(d => d.Hi - d.Lo >= 35.0 * k
                && moved.Any(r => Math.Abs(d.Lo - r.yOld) < tol || Math.Abs(d.Hi - r.yOld) < tol)).ToList();
            if (chain.Count == 0) return 0;
            double chainX = chain[0].X;
            var chainAll = etDims.Where(d => Math.Abs(d.X - chainX) < tol).ToList();

            double yFirst = moved[0].yOld, yLast = moved[moved.Count - 1].yOld;
            var dA = chainAll.FirstOrDefault(d => Math.Abs(d.Hi - yFirst) < tol);
            var dB = chainAll.FirstOrDefault(d => Math.Abs(d.Lo - yLast) < tol);
            if (dA.Id.IsNull || dB.Id.IsNull) return 0;
            double A = dA.Lo, B = dB.Hi;

            var oldPts = new List<double> { A };
            oldPts.AddRange(moved.Select(r => r.yOld));
            oldPts.Add(B);
            var newPts = new List<double> { A };
            newPts.AddRange(moved.Select(r => r.yNew));
            newPts.Add(B);
            for (int i = 1; i < newPts.Count; i++)
                if (newPts[i] <= newPts[i - 1] + 5.0 * k) return 0;

            var labels = new List<(ObjectId id, double cy, string pre, int adet, int dia, int s)>();
            foreach (ObjectId id in contents)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead, false) is DBText t)) continue;
                if (Math.Abs(Math.Cos(t.Rotation)) > 0.05) continue;
                var m = EtriyeBolgeRx.Match(KolonDonatiTableDrawer.NormalizeDiameterSymbol((t.TextString ?? "").Trim()));
                if (!m.Success) continue;
                if (!TryGetExtents(t, out Extents3d te)) continue;
                double cx = 0.5 * (te.MinPoint.X + te.MaxPoint.X);
                if (cx < xElevMin - k) continue;
                double cy = 0.5 * (te.MinPoint.Y + te.MaxPoint.Y);
                if (cy <= A + tol || cy >= B - tol) continue;
                labels.Add((id, cy, m.Groups[1].Value,
                    int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                    int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)));
            }
            if (labels.Count == 0) return 0;

            var tplLine = lines.First(l => Math.Abs(l.y - A) < tol || Math.Abs(l.y - B) < tol);
            var tplChain = chainAll.First(d => d.Lo >= A - tol && d.Hi <= B + tol);
            var copyDims = etDims.Where(d => Math.Abs(d.X - chainX) > tol && d.Lo >= A - tol && d.Hi <= B + tol).ToList();
            ObjectId tplCopy = copyDims.Count > 0 ? copyDims[0].Id : ObjectId.Null;
            ObjectId tplLabel = labels[0].id;

            int dAdet = 0;
            var intervals = new List<(double za, double zb, string pre, int dia, int s, int adetNew)>();
            for (int i = 0; i + 1 < oldPts.Count; i++)
            {
                double oa = oldPts[i], ob = oldPts[i + 1];
                var lab = labels.FirstOrDefault(l => l.cy > oa && l.cy < ob);
                if (lab.id.IsNull) return 0;
                double za = newPts[i], zb = newPts[i + 1];
                if (!PlanIdDrawingManager.KolonRevizeEtriyeAdetAralik((zb - za) / k, lab.s, out int adetNew, out _))
                    return 0;
                dAdet += adetNew - lab.adet;
                intervals.Add((za, zb, lab.pre, lab.dia, lab.s, adetNew));
            }

            var cloneSrc = new List<ObjectId> { tplLine.id, tplChain.Id, tplLabel };
            if (!tplCopy.IsNull) cloneSrc.Add(tplCopy);
            var clones = new Dictionary<ObjectId, Entity>();
            foreach (var id in cloneSrc)
                clones[id] = (Entity)((Entity)tr.GetObject(id, OpenMode.ForRead, false)).Clone();

            void Erase(ObjectId id)
            {
                try { ((Entity)tr.GetObject(id, OpenMode.ForWrite, false)).Erase(); } catch { }
            }
            foreach (var l in lines.Where(l => l.y > A + tol && l.y < B - tol)) Erase(l.id);
            foreach (var d in chainAll.Where(d => d.Lo >= A - tol && d.Hi <= B + tol)) Erase(d.Id);
            foreach (var d in copyDims) Erase(d.Id);
            foreach (var l in labels) Erase(l.id);

            Entity Add(Entity src)
            {
                var c = (Entity)src.Clone();
                btr.AppendEntity(c);
                tr.AddNewlyCreatedDBObject(c, true);
                return c;
            }
            void AddLine(double y)
            {
                var c = (Line)Add(clones[tplLine.id]);
                c.StartPoint = new Point3d(c.StartPoint.X, y, 0);
                c.EndPoint = new Point3d(c.EndPoint.X, y, 0);
            }
            void AddVDim(ObjectId tpl, double ya, double yb)
            {
                var c = Add(clones[tpl]);
                if (c is AlignedDimension ad)
                {
                    double xl = ad.DimLinePoint.X;
                    ad.XLine1Point = new Point3d(ad.XLine1Point.X, ya, 0);
                    ad.XLine2Point = new Point3d(ad.XLine2Point.X, yb, 0);
                    ad.DimLinePoint = new Point3d(xl, 0.5 * (ya + yb), 0);
                }
                else if (c is RotatedDimension rd)
                {
                    double xl = rd.DimLinePoint.X;
                    rd.XLine1Point = new Point3d(rd.XLine1Point.X, ya, 0);
                    rd.XLine2Point = new Point3d(rd.XLine2Point.X, yb, 0);
                    rd.DimLinePoint = new Point3d(xl, 0.5 * (ya + yb), 0);
                }
            }

            var sabit = new List<double>(newPts);
            var kopya = new List<double>();
            bool Yakin(List<double> src, double y) => src.Any(v => Math.Abs(v - y) < 1.5 * k);
            void KopyaFrom(double yFrom, double yTo)
            {
                if (Math.Abs(yTo - yFrom) < 1.5 * k || Yakin(sabit, yTo)) return;
                if (!Yakin(kopya, yTo))
                {
                    AddLine(yTo);
                    kopya.Add(yTo);
                }
                if (!tplCopy.IsNull) AddVDim(tplCopy, Math.Min(yFrom, yTo), Math.Max(yFrom, yTo));
            }

            for (int i = 1; i + 1 < newPts.Count; i++) AddLine(newPts[i]);
            var srcLabel = clones[tplLabel];
            double srcLabelCy = labels[0].cy;
            foreach (var iv in intervals)
            {
                double sAct = (iv.zb - iv.za) / iv.adetNew;
                KopyaFrom(iv.za, iv.za + sAct);
                KopyaFrom(iv.zb, iv.zb - sAct);
                if (iv.zb - iv.za > 180.0 * k)
                {
                    double zMid = 0.5 * (iv.za + iv.zb);
                    if (!Yakin(sabit, zMid) && !Yakin(kopya, zMid))
                    {
                        AddLine(zMid);
                        kopya.Add(zMid);
                    }
                    KopyaFrom(zMid, zMid - sAct);
                    KopyaFrom(zMid, zMid + sAct);
                }
                AddVDim(tplChain.Id, iv.za, iv.zb);
                var lab = (DBText)Add(srcLabel);
                lab.TextString = iv.pre + iv.adetNew.ToString(CultureInfo.InvariantCulture) + "\u00F8"
                    + iv.dia.ToString(CultureInfo.InvariantCulture) + "/" + iv.s.ToString(CultureInfo.InvariantCulture);
                lab.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0.5 * (iv.za + iv.zb) - srcLabelCy, 0)));
                nBolge++;
            }
            foreach (var c in clones.Values) c.Dispose();
            return dAdet;
        }

        private static List<(ObjectId id, double y, double x0, double x1)> CollectEtriyeLines(
            Transaction tr, List<ObjectId> contents, double xElevMin, double k)
        {
            var list = new List<(ObjectId id, double y, double x0, double x1)>();
            foreach (ObjectId id in contents)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead, false) is Line ln)) continue;
                if (!string.Equals(ln.Layer ?? "", LayerEtriye, StringComparison.OrdinalIgnoreCase)) continue;
                if (Math.Abs(ln.StartPoint.Y - ln.EndPoint.Y) > 0.01) continue;
                double x0 = Math.Min(ln.StartPoint.X, ln.EndPoint.X), x1 = Math.Max(ln.StartPoint.X, ln.EndPoint.X);
                if (x0 < xElevMin - k || x1 - x0 < 2.0 * k) continue;
                list.Add((id, ln.StartPoint.Y, x0, x1));
            }
            return list;
        }

        // ───────────────────────── Kesit ─────────────────────────

        private static bool TryFindSection(
            Transaction tr, List<ObjectId> contents,
            out Extents3d sec, out List<CircleBar> circles, out double k)
        {
            sec = default;
            k = 1.0;
            var all = CollectBarCircleEntities(tr, contents);
            circles = all;
            if (all.Count < 4) return false;

            // Kesit çerçevesi: en çok çember merkezini içeren, eşitlikte en küçük KOLON/PERDE kapalı polyline.
            int bestCount = 0;
            double bestArea = double.MaxValue;
            foreach (ObjectId id in contents)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead, false) is Polyline pl) || !pl.Closed) continue;
                string lay = pl.Layer ?? "";
                if (!string.Equals(lay, LayerKolon, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(lay, LayerPerde, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!TryGetExtents(pl, out Extents3d ex)) continue;
                int cnt = all.Count(c => InBox(c.Center, ex));
                if (cnt < 4) continue;
                double area = (ex.MaxPoint.X - ex.MinPoint.X) * (ex.MaxPoint.Y - ex.MinPoint.Y);
                if (cnt > bestCount || (cnt == bestCount && area < bestArea))
                {
                    bestCount = cnt;
                    bestArea = area;
                    sec = ex;
                }
            }
            if (bestCount < 4) return false;
            var secBox = sec;
            circles = all.Where(c => InBox(c.Center, secBox)).ToList();
            double r = circles.Select(c => c.Radius).OrderBy(v => v).ElementAt(circles.Count / 2);
            k = r / PlanIdDrawingManager.KolonRevizeKesitDonatiRadiusCm > 1.5 ? 2.0 : 1.0;
            return true;
        }

        /// <summary>Görünüş başlangıcı: kesitin sağındaki uzun düşey elemanların en solu.</summary>
        private static double ResolveElevationMinX(Transaction tr, List<ObjectId> contents, Extents3d sec, double k)
        {
            double secH = sec.MaxPoint.Y - sec.MinPoint.Y;
            double hMin = Math.Max(150.0 * k, 2.5 * secH);
            double best = double.MaxValue;
            foreach (ObjectId id in contents)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null || ent is Dimension || ent is DBText || ent is MText) continue;
                if (!TryGetExtents(ent, out Extents3d ex)) continue;
                if (ex.MaxPoint.Y - ex.MinPoint.Y < hMin) continue;
                if (ex.MinPoint.X <= sec.MaxPoint.X) continue;
                if (ex.MinPoint.X < best) best = ex.MinPoint.X;
            }
            return best < double.MaxValue ? best : sec.MaxPoint.X + 60.0 * k;
        }

        private static double ResolveStripTop(Transaction tr, List<ObjectId> contents, Extents3d antetBox)
        {
            double top = antetBox.MinPoint.Y;
            foreach (ObjectId id in contents)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null || !string.Equals(ent.Layer ?? "", LayerIcOlcek, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (TryGetExtents(ent, out Extents3d ex) && ex.MaxPoint.Y > top) top = ex.MaxPoint.Y;
            }
            return top;
        }

        private static bool InAcilimRegion(Extents3d ex, Extents3d sec, double xElevMin, double stripTop, double k)
        {
            return ex.MaxPoint.Y < sec.MinPoint.Y - 2.0 * k
                && ex.MinPoint.Y > stripTop
                && ex.MaxPoint.X < xElevMin - k;
        }

        /// <summary>Kesit altındaki etriye açılımı: en büyük L'li "nøφ L=" etiketi (dış etriye).</summary>
        private static void ReadEtriyeAcilim(
            Transaction tr, List<ObjectId> contents, Extents3d sec, double xElevMin, double stripTop, double k,
            out int etDia, out int etAdet, out string grupOnek)
        {
            etDia = 0;
            etAdet = 1;
            grupOnek = "";
            int bestL = -1;
            foreach (ObjectId id in contents)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead, false) is DBText t)) continue;
                var m = AcilimTagRx.Match(KolonDonatiTableDrawer.NormalizeDiameterSymbol((t.TextString ?? "").Trim()));
                if (!m.Success) continue;
                if (!TryGetExtents(t, out Extents3d ex) || !InAcilimRegion(ex, sec, xElevMin, stripTop, k)) continue;
                int L = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                if (L <= bestL) continue;
                bestL = L;
                grupOnek = m.Groups[1].Value;
                etAdet = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                etDia = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            }
        }

        private static int EraseSectionAndAcilim(
            Transaction tr, List<ObjectId> contents, Extents3d sec, double xElevMin, double stripTop, double k,
            List<CircleBar> circles)
        {
            int n = 0;
            var circleIds = new HashSet<ObjectId>(circles.Select(c => c.Id));
            double pad = k;
            foreach (ObjectId id in contents)
            {
                if (id.IsErased) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null || !TryGetExtents(ent, out Extents3d ex)) continue;
                string lay = ent.Layer ?? "";
                bool kesitIci = ex.MinPoint.X >= sec.MinPoint.X - pad && ex.MaxPoint.X <= sec.MaxPoint.X + pad
                    && ex.MinPoint.Y >= sec.MinPoint.Y - pad && ex.MaxPoint.Y <= sec.MaxPoint.Y + pad;
                bool sil = false;
                if (circleIds.Contains(id))
                    sil = true;
                else if (kesitIci && !(ent is DBText) && !(ent is MText)
                    && (Eq(lay, LayerEtriye) || Eq(lay, LayerCiroz)))
                    sil = true;
                else if (InAcilimRegion(ex, sec, xElevMin, stripTop, k)
                    && (Eq(lay, LayerKesitGorunus) || Eq(lay, LayerEtriye) || Eq(lay, LayerCiroz) || Eq(lay, LayerDonatiYazisi)))
                    sil = true;
                if (!sil) continue;
                ent.UpgradeOpen();
                ent.Erase();
                n++;
            }
            return n;
        }

        private static void ApplyGrupOnek(Transaction tr, BlockTableRecord btr, long handseedBefore, string onek)
        {
            if (string.IsNullOrEmpty(onek)) return;
            foreach (ObjectId id in btr)
            {
                if (id.IsNull || id.Handle.Value < handseedBefore) continue;
                if (!(tr.GetObject(id, OpenMode.ForRead, false) is DBText t)) continue;
                string s = KolonDonatiTableDrawer.NormalizeDiameterSymbol(t.TextString ?? "");
                if (!LEqualsRx.IsMatch(s) || !Regex.IsMatch(s, @"^\d+(x\d+)?\u00F8")) continue;
                t.UpgradeOpen();
                t.TextString = onek + s;
            }
        }

        // ───────────────────────── Görünüş düşeyleri ─────────────────────────

        /// <summary>
        /// Görünüşteki uzun yüz çubuk grupları (aynı kot aralığı, eski yüz adedi) yeni kesitin yüz X'lerine
        /// göre çoğaltılır; alt ara ölçüleri yenilenir.
        /// </summary>
        private static int RedistributeElevationBars(
            Transaction tr, BlockTableRecord btr, List<ObjectId> contents, double xElevMin,
            List<CircleBar> oldCircles, List<double> yuzXs, double k)
        {
            if (yuzXs == null || yuzXs.Count < 2 || oldCircles.Count < 4) return 0;
            var oldXs = DistinctSorted(oldCircles.Select(c => c.Center.X), 0.5 * k);
            int mOld = oldXs.Count;
            double oldSpan = oldXs[oldXs.Count - 1] - oldXs[0];
            var newRel = DistinctSorted(yuzXs, 0.5 * k).Select(x => x - yuzXs.Min()).ToList();
            if (Math.Abs(newRel[newRel.Count - 1] - oldSpan) > 1.5 * k) return 0;

            var live = CollectBarSegs(tr, contents.Where(id => !id.IsErased).ToList(), xElevMin, k);
            var groups = live.GroupBy(b => (Math.Round(b.Y0 / (0.5 * k)), Math.Round(b.Y1 / (0.5 * k)), b.NVerts));
            int n = 0;
            List<double> viewOldXs = null, viewNewXs = null;
            foreach (var g in groups)
            {
                var srt = g.GroupBy(b => b.Id).Select(x => x.First()).OrderBy(b => b.X).ToList();
                if (srt.Count < mOld) continue;
                int bestI = -1;
                double bestErr = 1.0 * k;
                for (int i = 0; i + mOld - 1 < srt.Count; i++)
                {
                    double err = Math.Abs(srt[i + mOld - 1].X - srt[i].X - oldSpan);
                    if (err < bestErr) { bestErr = err; bestI = i; }
                }
                if (bestI < 0) continue;
                var win = srt.Skip(bestI).Take(mOld).ToList();
                double x0 = win[0].X, mid = x0 + 0.5 * oldSpan;
                var newXs = newRel.Select(r => x0 + r).ToList();
                foreach (double nx in newXs)
                {
                    bool left = nx <= mid + 0.01;
                    var tpl = win.Where(b => (b.X <= mid + 0.01) == left)
                        .OrderBy(b => Math.Abs(b.X - nx)).DefaultIfEmpty(win[0]).First();
                    var src = (Entity)tr.GetObject(tpl.Id, OpenMode.ForRead, false);
                    var c = (Entity)src.Clone();
                    c.TransformBy(Matrix3d.Displacement(new Vector3d(nx - tpl.X, 0, 0)));
                    btr.AppendEntity(c);
                    tr.AddNewlyCreatedDBObject(c, true);
                    n++;
                }
                foreach (var b in win)
                {
                    try { ((Entity)tr.GetObject(b.Id, OpenMode.ForWrite, false)).Erase(); } catch { }
                }
                if (viewOldXs == null)
                {
                    viewOldXs = win.Select(b => b.X).ToList();
                    viewNewXs = newXs;
                }
            }
            if (viewOldXs != null)
                RebuildSpacingDims(tr, btr, contents, viewOldXs, viewNewXs, k);
            return n;
        }

        private static void RebuildSpacingDims(
            Transaction tr, BlockTableRecord btr, List<ObjectId> contents,
            List<double> oldXs, List<double> newXs, double k)
        {
            double tol = 0.3 * k;
            bool OnOld(double x) => oldXs.Any(v => Math.Abs(v - x) < tol);
            var old = new List<AlignedDimension>();
            foreach (ObjectId id in contents)
            {
                if (id.IsErased) continue;
                if (!(tr.GetObject(id, OpenMode.ForRead, false) is AlignedDimension ad)) continue;
                if (Math.Abs(ad.XLine1Point.Y - ad.XLine2Point.Y) > 0.01) continue;
                if (!OnOld(ad.XLine1Point.X) || !OnOld(ad.XLine2Point.X)) continue;
                if (Math.Abs(ad.XLine1Point.X - ad.XLine2Point.X) < tol) continue;
                old.Add(ad);
            }
            if (old.Count == 0) return;
            var tpl = (AlignedDimension)old[0].Clone();
            double y = tpl.XLine1Point.Y, yLine = tpl.DimLinePoint.Y;
            foreach (var ad in old)
            {
                ad.UpgradeOpen();
                ad.Erase();
            }
            for (int i = 0; i + 1 < newXs.Count; i++)
            {
                var c = (AlignedDimension)tpl.Clone();
                c.XLine1Point = new Point3d(newXs[i], y, 0);
                c.XLine2Point = new Point3d(newXs[i + 1], y, 0);
                c.DimLinePoint = new Point3d(0.5 * (newXs[i] + newXs[i + 1]), yLine, 0);
                btr.AppendEntity(c);
                tr.AddNewlyCreatedDBObject(c, true);
            }
            tpl.Dispose();
        }

        // ───────────────────────── Ortak okuma ─────────────────────────

        /// <summary>DONATI düşey çubukları: en uzun düşey parça (kesit bölgesi hariç).</summary>
        private static List<BarSeg> CollectBarSegs(Transaction tr, List<ObjectId> contents, double xElevMin, double k)
        {
            var list = new List<BarSeg>();
            foreach (ObjectId id in contents)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null || !Eq(ent.Layer, LayerDonati)) continue;
                if (ent is Line ln)
                {
                    if (Math.Abs(ln.StartPoint.X - ln.EndPoint.X) > 0.01) continue;
                    double y0 = Math.Min(ln.StartPoint.Y, ln.EndPoint.Y), y1 = Math.Max(ln.StartPoint.Y, ln.EndPoint.Y);
                    if (y1 - y0 < 10.0 * k || ln.StartPoint.X < xElevMin - k) continue;
                    list.Add(new BarSeg { Id = id, X = ln.StartPoint.X, Y0 = y0, Y1 = y1, NVerts = 2 });
                }
                else if (ent is Polyline pl && !pl.Closed && pl.NumberOfVertices >= 2)
                {
                    double bestLen = 0;
                    BarSeg best = default;
                    for (int i = 0; i + 1 < pl.NumberOfVertices; i++)
                    {
                        if (Math.Abs(pl.GetBulgeAt(i)) > 1e-6) continue;
                        var a = pl.GetPoint2dAt(i);
                        var b = pl.GetPoint2dAt(i + 1);
                        if (Math.Abs(a.X - b.X) > 0.01) continue;
                        double len = Math.Abs(b.Y - a.Y);
                        if (len > bestLen)
                        {
                            bestLen = len;
                            best = new BarSeg { Id = id, X = a.X, Y0 = Math.Min(a.Y, b.Y), Y1 = Math.Max(a.Y, b.Y), NVerts = pl.NumberOfVertices };
                        }
                    }
                    if (bestLen < 10.0 * k || best.X < xElevMin - k) continue;
                    list.Add(best);
                }
            }
            return list;
        }

        private static List<VDim> CollectVerticalDims(Transaction tr, List<ObjectId> contents)
        {
            var list = new List<VDim>();
            foreach (ObjectId id in contents)
            {
                if (id.IsErased) continue;
                if (!(tr.GetObject(id, OpenMode.ForRead, false) is Dimension dim)) continue;
                Point3d p1, p2;
                if (dim is AlignedDimension ad) { p1 = ad.XLine1Point; p2 = ad.XLine2Point; }
                else if (dim is RotatedDimension rd && Math.Abs(Math.Cos(rd.Rotation)) < 0.01)
                { p1 = rd.XLine1Point; p2 = rd.XLine2Point; }
                else continue;
                if (Math.Abs(p1.X - p2.X) > 0.01 || Math.Abs(p1.Y - p2.Y) < 0.01) continue;
                double meas;
                try { meas = dim.Measurement; }
                catch { continue; }
                list.Add(new VDim
                {
                    Id = id,
                    X = p1.X,
                    Lo = Math.Min(p1.Y, p2.Y),
                    Hi = Math.Max(p1.Y, p2.Y),
                    Meas = meas
                });
            }
            return list;
        }

        private static bool IsOnBar(VDim d, List<BarSeg> bars, double k)
        {
            foreach (var b in bars)
            {
                if (Math.Abs(b.X - d.X) > 0.3 * k) continue;
                if (b.Y0 > d.Lo + k || b.Y1 < d.Hi - k) continue;
                    return true;
                }
            return false;
        }

        private static List<double> DistinctSorted(IEnumerable<double> xs, double tol)
        {
            var res = new List<double>();
            foreach (double x in xs.OrderBy(v => v))
                if (res.Count == 0 || x - res[res.Count - 1] > tol) res.Add(x);
            return res;
        }

        private static bool InBox(Point2d p, Extents3d ex) =>
            p.X >= ex.MinPoint.X && p.X <= ex.MaxPoint.X && p.Y >= ex.MinPoint.Y && p.Y <= ex.MaxPoint.Y;

        private static bool Eq(string a, string b) => string.Equals(a ?? "", b, StringComparison.OrdinalIgnoreCase);

        private static bool IsOnLayer(Transaction tr, ObjectId id, string layer)
        {
            var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
            return ent != null && Eq(ent.Layer, layer);
        }

        /// <summary>
        /// Kolon/perde başlığı düşey donatısı: etriye (küçük çap + yüksek adet) elenir;
        /// en büyük As (n·φ²) aday seçilir.
        /// </summary>
        private static bool TryDetectVerticalDiaAndCount(
            Transaction tr, List<ObjectId> contents, out int dia, out int count)
        {
            dia = 0;
            count = 0;
            var bestAs = 0.0;
            foreach (ObjectId id in contents)
            {
                string text = GetEntityText(tr, id);
                if (string.IsNullOrWhiteSpace(text)) continue;
                string norm = KolonDonatiTableDrawer.NormalizeDiameterSymbol(text);
                // Etriye satırı: "39ø10/8" gibi aralık içerenler düşey değil.
                if (norm.IndexOf('/') >= 0) continue;
                int sum = 0;
                int maxDia = 0;
                foreach (Match m in RebarTokenRx.Matches(norm))
                {
                    if (!TryParseToken(m, out int c, out int d)) continue;
                    // Tipik etriye/çiroz: φ≤12 ve adet yüksek, L= yok → elenir (L= varsa açılım düşeyi).
                    bool hasL = LEqualsRx.IsMatch(norm);
                    if (d <= 12 && c >= 24 && !hasL) continue;
                    if (d < 12 && !hasL) continue;
                    sum += c;
                    if (d > maxDia) maxDia = d;
                }
                if (sum < 4 || maxDia < 12) continue;
                double a = sum * maxDia * maxDia;
                if (a > bestAs)
                {
                    bestAs = a;
                    count = sum;
                    dia = maxDia;
                }
            }
            if (dia >= 12 && count >= 4) return true;

            // Yedek: kesit cember sayisi + yazidaki en buyuk cap.
            var circles = CollectBarCircleEntities(tr, contents);
            if (circles.Count >= 4)
            {
                count = circles.Count;
                foreach (ObjectId id in contents)
                {
                    string text = GetEntityText(tr, id);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    foreach (Match m in RebarTokenRx.Matches(KolonDonatiTableDrawer.NormalizeDiameterSymbol(text)))
                    {
                        if (!TryParseToken(m, out _, out int d)) continue;
                        if (d > dia && d >= 12) dia = d;
                    }
                }
                if (dia >= 12) return true;
            }
            return false;
        }

        private static bool TryResolveAntetBox(
            Transaction tr, Database db, ObjectId seedId, out Extents3d antetBox, out ObjectId antetId)
        {
            antetBox = default;
            antetId = ObjectId.Null;
            var seed = tr.GetObject(seedId, OpenMode.ForRead) as Entity;
            if (seed == null) return false;

            if (IsIcAntetFrame(seed) && TryGetExtents(seed, out antetBox))
            {
                antetId = seedId;
                return true;
            }

            if (!TryGetExtents(seed, out Extents3d seedEx))
                return false;
            double cx = 0.5 * (seedEx.MinPoint.X + seedEx.MaxPoint.X);
            double cy = 0.5 * (seedEx.MinPoint.Y + seedEx.MaxPoint.Y);

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            double bestArea = double.MaxValue;
            ObjectId bestId = ObjectId.Null;
            Extents3d bestEx = default;
            foreach (ObjectId id in btr)
            {
                if (id.IsNull || id.IsErased) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null || !IsIcAntetFrame(ent)) continue;
                if (!TryGetExtents(ent, out Extents3d ex)) continue;
                if (cx < ex.MinPoint.X - 1e-6 || cx > ex.MaxPoint.X + 1e-6) continue;
                if (cy < ex.MinPoint.Y - 1e-6 || cy > ex.MaxPoint.Y + 1e-6) continue;
                double area = (ex.MaxPoint.X - ex.MinPoint.X) * (ex.MaxPoint.Y - ex.MinPoint.Y);
                if (area < 100.0) continue;
                if (area < bestArea)
                {
                    bestArea = area;
                    bestId = id;
                    bestEx = ex;
                }
            }
            if (bestId.IsNull) return false;
            antetId = bestId;
            antetBox = bestEx;
            return true;
        }

        private static bool IsIcAntetFrame(Entity ent)
        {
            if (ent == null) return false;
            if (!Eq(ent.Layer, LayerIcAntet)) return false;
            return ent is Polyline pl && pl.Closed && pl.NumberOfVertices >= 4;
        }

        private static List<ObjectId> CollectEntitiesInside(
            Transaction tr, Database db, Extents3d antetBox, ObjectId antetId)
        {
            var list = new List<ObjectId>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            double minX = antetBox.MinPoint.X + InsidePadCm;
            double maxX = antetBox.MaxPoint.X - InsidePadCm;
            double minY = antetBox.MinPoint.Y + InsidePadCm;
            double maxY = antetBox.MaxPoint.Y - InsidePadCm;
            foreach (ObjectId id in btr)
            {
                if (id.IsNull || id.IsErased || id == antetId) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null) continue;
                if (Eq(ent.Layer, LayerIcAntet)) continue;
                if (!TryGetExtents(ent, out Extents3d ex)) continue;
                double cx = 0.5 * (ex.MinPoint.X + ex.MaxPoint.X);
                double cy = 0.5 * (ex.MinPoint.Y + ex.MaxPoint.Y);
                if (cx < minX || cx > maxX || cy < minY || cy > maxY) continue;
                list.Add(id);
            }
            return list;
        }

        private static int ResolveOldBarCount(Transaction tr, List<ObjectId> contents, int oldDia)
        {
            int fromText = 0;
            foreach (ObjectId id in contents)
            {
                string text = GetEntityText(tr, id);
                if (string.IsNullOrWhiteSpace(text)) continue;
                string norm = KolonDonatiTableDrawer.NormalizeDiameterSymbol(text);
                if (norm.IndexOf('/') >= 0) continue;
                int sum = 0;
                bool hit = false;
                foreach (Match m in RebarTokenRx.Matches(norm))
                {
                    if (!TryParseToken(m, out int count, out int dia)) continue;
                    if (dia != oldDia) continue;
                    sum += count;
                    hit = true;
                }
                if (hit && sum > fromText) fromText = sum;
            }
            if (fromText >= 4) return fromText;
            var circles = CollectBarCircleEntities(tr, contents);
            return circles.Count >= 4 ? circles.Count : 0;
        }

        internal static int ComputeNewBarCountAreaPreserving(int oldCount, int oldDiaMm, int newDiaMm)
        {
            if (oldCount < 4) oldCount = 4;
            if (oldDiaMm < 6 || newDiaMm < 6) return oldCount;
            double nExact = oldCount * (double)(oldDiaMm * oldDiaMm) / (newDiaMm * newDiaMm);
            int n = (int)Math.Ceiling(nExact - 1e-9);
            if (n < 4) n = 4;
            if ((n % 2) != 0) n++;
            double asOld = oldCount * Math.PI * oldDiaMm * oldDiaMm / 400.0;
            while (n * Math.PI * newDiaMm * newDiaMm / 400.0 + 1e-6 < asOld)
                n += 2;
            return n;
        }

        /// <summary>Düşey donatı yazılarında adet/çap (L= boyları bindirme adımında güncellenir).</summary>
        private static int RewriteRebarTexts(
            Transaction tr, List<ObjectId> contents,
            int oldDia, int oldCount, int newDia, int newCount)
        {
            int n = 0;
            string oldTok = "\u00F8" + oldDia.ToString(CultureInfo.InvariantCulture);
            foreach (ObjectId id in contents)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                string raw = ent is DBText d0 ? d0.TextString : ent is MText m0 ? m0.Contents : null;
                if (raw == null) continue;
                string norm = KolonDonatiTableDrawer.NormalizeDiameterSymbol(raw);
                if (norm.IndexOf(oldTok, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (norm.IndexOf('/') >= 0) continue;

                string updated = RebarTokenRx.Replace(norm, m =>
                {
                    if (!TryParseToken(m, out int c, out int d)) return m.Value;
                    if (d != oldDia) return m.Value;
                    int nc = c;
                    if (c == oldCount)
                        nc = newCount;
                    else if (oldCount > 0)
                    {
                        nc = (int)Math.Ceiling(c * (double)newCount / oldCount - 1e-9);
                        if (nc < 1) nc = 1;
                    }
                    return nc.ToString(CultureInfo.InvariantCulture) + "\u00F8" +
                           newDia.ToString(CultureInfo.InvariantCulture);
                });
                if (string.Equals(updated, norm, StringComparison.Ordinal)) continue;
                ent.UpgradeOpen();
                if (ent is DBText dbt) dbt.TextString = updated;
                else if (ent is MText mt) mt.Contents = updated;
                n++;
            }
            return n;
        }

        /// <summary>
        /// Kesit düşey çubuk daireleri: 2 yaylı kapalı polyline (DONATI).
        /// 1:25 sonrası r≈1.5 cm olabilir — baskın yarıçapı çizimden alır.
        /// </summary>
        private static List<CircleBar> CollectBarCircleEntities(Transaction tr, List<ObjectId> contents)
        {
            var raw = new List<CircleBar>();
            foreach (ObjectId id in contents)
            {
                if (id.IsErased) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null || !Eq(ent.Layer, LayerDonati)) continue;
                if (!(ent is Polyline pl) || !pl.Closed || pl.NumberOfVertices != 2) continue;
                var a = pl.GetPoint2dAt(0);
                var b = pl.GetPoint2dAt(1);
                double r = 0.5 * a.GetDistanceTo(b);
                if (r < 0.25 || r > 4.0) continue;
                if (Math.Abs(pl.GetBulgeAt(0)) < 0.5) continue;
                raw.Add(new CircleBar { Id = id, Center = new Point2d(0.5 * (a.X + b.X), 0.5 * (a.Y + b.Y)), Radius = r });
            }
            if (raw.Count < 4) return raw;

            // Baskın yarıçap (1:25'te 1.5, 1:50'de 0.75).
            var radii = raw.Select(c => c.Radius).OrderBy(v => v).ToList();
            double med = radii[radii.Count / 2];
            return raw.Where(c => Math.Abs(c.Radius - med) <= Math.Max(0.35, med * 0.35)).ToList();
        }

        private static double CeilTo5Cm(double v)
        {
            if (v <= 0) return 0;
            return Math.Ceiling(v / 5.0 - 1e-9) * 5.0;
        }

        private static double FloorTo5Cm(double v)
        {
            if (v <= 0) return 0;
            return Math.Floor(v / 5.0 + 1e-9) * 5.0;
        }

        private static bool TryParseToken(Match m, out int count, out int dia)
        {
            count = 0;
            dia = 0;
            if (m.Groups[1].Success)
            {
                if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int a) ||
                    !int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int b) ||
                    !int.TryParse(m.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out dia))
                    return false;
                count = a * b;
            }
            else
            {
                if (!int.TryParse(m.Groups[4].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) ||
                    !int.TryParse(m.Groups[5].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out dia))
                    return false;
            }
            return count > 0 && dia >= 6 && dia <= 40;
        }

        private static string GetEntityText(Transaction tr, ObjectId id)
        {
            var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
            if (ent is DBText dbt) return dbt.TextString;
            if (ent is MText mt) return mt.Contents;
            return null;
        }

        private static bool TryGetExtents(Entity ent, out Extents3d ex)
        {
            ex = default;
            try
            {
                if (ent == null || !ent.Bounds.HasValue) return false;
                ex = ent.GeometricExtents;
                return true;
            }
            catch { return false; }
        }
    }
}
