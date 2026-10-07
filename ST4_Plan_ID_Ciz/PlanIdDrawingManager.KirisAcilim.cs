using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// KIRISACILIMLARI: kirişin ana aksı boyunca düşey kesit (açılım).
    /// Yatay kirişe planda aşağıdan yukarı, düşey kirişe sağdan sola bakılır.
    /// Aynı kiriş numarası tek çizilir. ≤25° kırılan komşu kirişler aynı şeritte kalır.
    /// Kiriş ve kolon tek kontur (kesişen iç çizgi yok). Kolon/perde en fazla 30 cm uzar, kesit sınırıyla kesilir.
    /// Diğer yönde saplanan kiriş, ana konturdan bağımsız, kendi genişlik × yüksekliğinde kesit sınırı poligonudur. Kolon görünüşünün içindeki kenarlar mavi.
    /// </summary>
    public sealed partial class PlanIdDrawingManager
    {
        private const double KirisAcilimMaxDonusDerece = 25.0;
        private const double KirisAcilimUcToleransCm = 5.0;
        /// <summary>1/20 kâğıtta 5 mm başlık.</summary>
        private const double KirisAcilimBaslikYukseklikCm = 10.0;
        private const double KirisAcilimAksYaziCm = 10.0;
        private const double KirisAcilimBoyYaziCm = 4.0;
        private const double KirisAcilimBalonYaricapCm = 10.0;
        /// <summary>Şeritler arası boşluk (1/20’de 20 mm kâğıt).</summary>
        private const double KirisAcilimSeritAraCm = 40.0;
        private const double KirisAcilimKatAraCm = 80.0;
        /// <summary>Yukarı veya aşağı devam eden kolon/perde, kiriş çizgisinden bu kadar gösterilip kesit sınırıyla kesilir.</summary>
        private const double KirisAcilimKutupCm = 30.0;

        private sealed class KirisAcilimSeg
        {
            public BeamInfo Beam;
            public Point2d P0;
            public Point2d P1;
            public double Kot0;
            public double Kot1;
            public int Cross0;
            public int Cross1;
            public Vector2d Dir;
            public double Len;
        }

        private sealed class KirisAcilimSpan
        {
            public BeamInfo Beam;
            public double X0;
            public double X1;
            public double Kot0;
            public double Kot1;
            public Point2d World0;
            public Point2d World1;
            public Vector2d Dir;
            public int Cross0;
            public int Cross1;
        }

        private struct KirisAcilimSupport
        {
            public double X0;
            public double X1;
            public bool IsWall;
            public bool ExtendUp;
            public bool ExtendDown;
        }

        /// <summary>Saplanan kirişin görünüşteki yeri: kaçıklıklı şerit kesişimi ve kendi kot/yüksekliği.</summary>
        private struct KirisAcilimSaplama
        {
            public int BeamId;
            public double X0;
            public double X1;
            public double KotTop;
            public double KotBot;
        }

        private struct KirisAcilimKutu
        {
            public double X0;
            public double Y0;
            public double X1;
            public double Y1;
        }

        public bool DrawKirisAcilimlari(Database db, Editor ed, Point3d insert)
        {
            if (db == null || ed == null || _model?.Beams == null || _axisService == null)
            {
                ed?.WriteMessage("\nKIRISACILIMLARI: Model okunamadi.");
                return false;
            }

            var floors = new List<FloorInfo>(_model.Floors ?? new List<FloorInfo>());
            floors.Sort((a, b) => a.ElevationM.CompareTo(b.ElevationM));
            if (floors.Count == 0)
            {
                ed.WriteMessage("\nKIRISACILIMLARI: Kat bulunamadi.");
                return false;
            }

            int maxNo = 0;
            foreach (var b in _model.Beams)
            {
                if (b == null || b.IsWallFlag == 1) continue;
                int n = GetBeamNumero(b.BeamId);
                if (n > maxNo) maxNo = n;
            }
            int pad = GetLabelPadWidth(maxNo);

            int chainCount = 0;
            int spanCount = 0;
            double cursorY = insert.Y;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                EnsureLayers(tr, db);

                foreach (var floor in floors)
                {
                    var chains = BuildKirisAcilimChains(floor.FloorNo);
                    if (chains.Count == 0) continue;

                    double floorTop = cursorY;
                    foreach (var chain in chains)
                    {
                        DrawKirisAcilimChain(tr, ms, db, floor, chain, insert.X, ref cursorY, pad);
                        cursorY += KirisAcilimSeritAraCm;
                        chainCount++;
                        spanCount += chain.Count;
                    }

                    string kat = string.IsNullOrEmpty(floor.ShortName) ? floor.FloorNo.ToString(CultureInfo.InvariantCulture) : floor.ShortName;
                    string title = string.Format(CultureInfo.InvariantCulture, "{0}  KIRIS ACILIMI  (1/20)", kat);
                    DrawBeamLabel(tr, ms, db, new Point3d(insert.X, cursorY + 6.0, 0), title, 12.0, 0.0, LayerBaslik, bottomLeftAligned: true);
                    if (cursorY + 24.0 > floorTop) floorTop = cursorY + 24.0;
                    cursorY = floorTop + KirisAcilimKatAraCm;
                }

                tr.Commit();
            }

            if (chainCount == 0)
            {
                ed.WriteMessage("\nKIRISACILIMLARI: Cizilecek kiris yok.");
                return false;
            }

            ed.WriteMessage(
                "\nKIRISACILIMLARI: {0} serit, {1} aciklik (1/20, cm). Yatay kiris alttan, dusey kiris sagdan. Ayni isim tek cizim. Donati yok.",
                chainCount, spanCount);
            return true;
        }

        private List<List<KirisAcilimSpan>> BuildKirisAcilimChains(int floorNo)
        {
            var segs = new List<KirisAcilimSeg>();
            var seen = new HashSet<string>();
            foreach (var beam in GetRawBeamsOnFloor(floorNo))
            {
                if (beam == null || beam.IsWallFlag == 1 || beam.HeightCm <= 1.0) continue;
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p0)) continue;
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p1)) continue;
                Vector2d v = p1 - p0;
                if (v.Length < 1.0) continue;
                int a = Math.Min(beam.StartAxisId, beam.EndAxisId);
                int b = Math.Max(beam.StartAxisId, beam.EndAxisId);
                string key = string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}", beam.FixedAxisId, a, b);
                if (!seen.Add(key)) continue;
                segs.Add(new KirisAcilimSeg
                {
                    Beam = beam,
                    P0 = p0,
                    P1 = p1,
                    Kot0 = beam.Point1KotCm,
                    Kot1 = beam.Point2KotCm,
                    Cross0 = beam.StartAxisId,
                    Cross1 = beam.EndAxisId,
                    Dir = v.GetNormal(),
                    Len = v.Length
                });
            }

            var pieces = KirisAcilimSameNamePieces(segs);
            int n = pieces.Count;
            var linkSeg = new int[n, 2];
            var linkEnd = new int[n, 2];
            var bestAng = new double[n, 2];
            var bestSeg = new int[n, 2];
            var bestEnd = new int[n, 2];
            for (int i = 0; i < n; i++)
            {
                linkSeg[i, 0] = linkSeg[i, 1] = -1;
                linkEnd[i, 0] = linkEnd[i, 1] = -1;
                bestAng[i, 0] = bestAng[i, 1] = 999.0;
                bestSeg[i, 0] = bestSeg[i, 1] = -1;
                bestEnd[i, 0] = bestEnd[i, 1] = -1;
            }

            for (int i = 0; i < n; i++)
            {
                for (int e = 0; e < 2; e++)
                {
                    Point2d pi = KirisAcilimPieceEnd(pieces[i], e, out Vector2d di);
                    for (int j = 0; j < n; j++)
                    {
                        if (j == i) continue;
                        for (int f = 0; f < 2; f++)
                        {
                            Point2d pj = KirisAcilimPieceEnd(pieces[j], f, out Vector2d dj);
                            if (pi.GetDistanceTo(pj) > KirisAcilimUcToleransCm) continue;
                            double ang = KirisAcilimHatAcisiDerece(di, dj);
                            if (ang > KirisAcilimMaxDonusDerece + 1e-6) continue;
                            if (ang < bestAng[i, e] - 1e-9)
                            {
                                bestAng[i, e] = ang;
                                bestSeg[i, e] = j;
                                bestEnd[i, e] = f;
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < n; i++)
            {
                for (int e = 0; e < 2; e++)
                {
                    int j = bestSeg[i, e];
                    int f = bestEnd[i, e];
                    if (j < 0) continue;
                    if (bestSeg[j, f] != i || bestEnd[j, f] != e) continue;
                    linkSeg[i, e] = j;
                    linkEnd[i, e] = f;
                }
            }

            var used = new bool[n];
            var chains = new List<List<KirisAcilimSpan>>();
            for (int i = 0; i < n; i++)
            {
                if (used[i]) continue;
                int entryEnd = linkSeg[i, 0] < 0 ? 0 : (linkSeg[i, 1] < 0 ? 1 : 0);
                var chain = new List<KirisAcilimSpan>();
                int cur = i;
                int enter = entryEnd;
                while (cur >= 0 && !used[cur])
                {
                    used[cur] = true;
                    var piece = pieces[cur];
                    if (enter == 1) KirisAcilimReverseSpans(piece);
                    chain.AddRange(piece);
                    int exit = enter == 0 ? 1 : 0;
                    int nxt = linkSeg[cur, exit];
                    enter = linkEnd[cur, exit];
                    cur = nxt;
                }
                if (chain.Count == 0) continue;
                KirisAcilimOrientToView(chain);
                chains.Add(chain);
            }

            chains.Sort(KirisAcilimSheetOrder);
            return chains;
        }

        /// <summary>Aynı kiriş numarası tek parça. Farklı numaralar sonra ≤25° ile birleşir.</summary>
        private static List<List<KirisAcilimSpan>> KirisAcilimSameNamePieces(List<KirisAcilimSeg> segs)
        {
            var pieces = new List<List<KirisAcilimSpan>>();
            var groups = new Dictionary<int, List<KirisAcilimSeg>>();
            foreach (var s in segs)
            {
                if (!groups.TryGetValue(s.Beam.BeamId, out var list))
                {
                    list = new List<KirisAcilimSeg>();
                    groups[s.Beam.BeamId] = list;
                }
                list.Add(s);
            }

            foreach (var kv in groups)
            {
                var list = kv.Value;
                KirisAcilimSeg longest = list[0];
                for (int i = 1; i < list.Count; i++)
                    if (list[i].Len > longest.Len) longest = list[i];
                Vector2d u = longest.Dir;
                var ordered = new List<KirisAcilimSpan>();
                foreach (var s in list)
                {
                    double t0 = s.P0.X * u.X + s.P0.Y * u.Y;
                    double t1 = s.P1.X * u.X + s.P1.Y * u.Y;
                    bool rev = t1 < t0;
                    ordered.Add(KirisAcilimSpanFrom(s, rev));
                }
                ordered.Sort((a, b) =>
                {
                    double ta = a.World0.X * u.X + a.World0.Y * u.Y;
                    double tb = b.World0.X * u.X + b.World0.Y * u.Y;
                    return ta.CompareTo(tb);
                });
                pieces.Add(ordered);
            }
            return pieces;
        }

        private static KirisAcilimSpan KirisAcilimSpanFrom(KirisAcilimSeg s, bool rev)
        {
            return new KirisAcilimSpan
            {
                Beam = s.Beam,
                Kot0 = rev ? s.Kot1 : s.Kot0,
                Kot1 = rev ? s.Kot0 : s.Kot1,
                World0 = rev ? s.P1 : s.P0,
                World1 = rev ? s.P0 : s.P1,
                Dir = rev ? -s.Dir : s.Dir,
                Cross0 = rev ? s.Cross1 : s.Cross0,
                Cross1 = rev ? s.Cross0 : s.Cross1
            };
        }

        private static void KirisAcilimReverseSpans(List<KirisAcilimSpan> spans)
        {
            spans.Reverse();
            foreach (var s in spans)
            {
                double k = s.Kot0; s.Kot0 = s.Kot1; s.Kot1 = k;
                Point2d w = s.World0; s.World0 = s.World1; s.World1 = w;
                int c = s.Cross0; s.Cross0 = s.Cross1; s.Cross1 = c;
                s.Dir = -s.Dir;
            }
        }

        private static Point2d KirisAcilimPieceEnd(List<KirisAcilimSpan> piece, int end, out Vector2d dir)
        {
            if (end == 0)
            {
                dir = piece[0].Dir;
                return piece[0].World0;
            }
            dir = piece[piece.Count - 1].Dir;
            return piece[piece.Count - 1].World1;
        }

        /// <summary>
        /// Yatay kiriş: planda aşağıdan yukarı bakılır, solda küçük X.
        /// Düşey kiriş: planda sağdan sola bakılır, solda küçük Y.
        /// </summary>
        private static void KirisAcilimOrientToView(List<KirisAcilimSpan> chain)
        {
            Point2d a = chain[0].World0;
            Point2d b = chain[chain.Count - 1].World1;
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            bool horizontal = Math.Abs(dx) >= Math.Abs(dy);
            bool reverse = horizontal ? dx < -1.0 : dy < -1.0;
            if (reverse) KirisAcilimReverseSpans(chain);

            double x = 0;
            for (int i = 0; i < chain.Count; i++)
            {
                if (i > 0)
                {
                    double gap = chain[i - 1].World1.GetDistanceTo(chain[i].World0);
                    if (gap > 1.0) x += gap;
                }
                double len = chain[i].World0.GetDistanceTo(chain[i].World1);
                chain[i].X0 = x;
                chain[i].X1 = x + len;
                x += len;
            }
        }

        private static int KirisAcilimSheetOrder(List<KirisAcilimSpan> a, List<KirisAcilimSpan> b)
        {
            int fa = KirisAcilimIsHorizontal(a) ? 0 : 1;
            int fb = KirisAcilimIsHorizontal(b) ? 0 : 1;
            if (fa != fb) return fa.CompareTo(fb);
            if (fa == 0)
                return KirisAcilimMid(a).Y.CompareTo(KirisAcilimMid(b).Y);
            return KirisAcilimMid(b).X.CompareTo(KirisAcilimMid(a).X);
        }

        private static bool KirisAcilimIsHorizontal(List<KirisAcilimSpan> chain)
        {
            Point2d a = chain[0].World0;
            Point2d b = chain[chain.Count - 1].World1;
            return Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y);
        }

        private static Point2d KirisAcilimMid(List<KirisAcilimSpan> chain)
        {
            Point2d a = chain[0].World0;
            Point2d b = chain[chain.Count - 1].World1;
            return new Point2d(0.5 * (a.X + b.X), 0.5 * (a.Y + b.Y));
        }

        private static double KirisAcilimHatAcisiDerece(Vector2d u, Vector2d v)
        {
            double d = Math.Abs(u.X * v.X + u.Y * v.Y);
            if (d > 1.0) d = 1.0;
            return Math.Acos(d) * (180.0 / Math.PI);
        }

        private void DrawKirisAcilimChain(
            Transaction tr, BlockTableRecord ms, Database db, FloorInfo floor,
            List<KirisAcilimSpan> chain, double x0, ref double cursorY, int pad)
        {
            int m = chain.Count;
            var sup = new KirisAcilimSupport[m + 1];
            double minY = double.MaxValue;
            double maxY = double.MinValue;
            double baseCm = (_model.BuildingBaseKotu + floor.ElevationM) * 100.0;

            for (int i = 0; i <= m; i++)
            {
                KirisAcilimSpanAt(chain, i, out KirisAcilimSpan left, out KirisAcilimSpan right);
                Point2d world = right != null ? right.World0 : left.World1;
                Vector2d dir = right != null ? right.Dir : left.Dir;
                int cross = right != null ? right.Cross0 : left.Cross1;
                int fixedAxis = right != null ? right.Beam.FixedAxisId : left.Beam.FixedAxisId;
                int fixedAxis2 = left != null ? left.Beam.FixedAxisId : fixedAxis;
                int cross2 = left != null ? left.Cross1 : cross;
                double kot = KirisAcilimJointTop(left, right);
                double soffit = KirisAcilimJointSoffit(left, right, kot);
                bool has = TryKirisAcilimSupportWidth(
                    floor.FloorNo, world, dir, fixedAxis, cross, fixedAxis2, cross2,
                    baseCm + kot, baseCm + soffit,
                    out double xLo, out double xHi, out bool isWall, out bool extendUp, out bool extendDown);
                sup[i] = new KirisAcilimSupport
                {
                    X0 = has ? xLo : 0,
                    X1 = has ? xHi : 0,
                    IsWall = isWall,
                    ExtendUp = has && extendUp,
                    ExtendDown = has && extendDown
                };
                double yLow = soffit - (sup[i].ExtendDown ? KirisAcilimKutupCm : 0);
                double yHigh = kot + (sup[i].ExtendUp ? KirisAcilimKutupCm : 0);
                if (yLow - 8.0 < minY) minY = yLow - 8.0;
                if (yHigh + 28.0 > maxY) maxY = yHigh + 28.0;
            }

            var saplanan = KirisAcilimCollectSaplanan(floor.FloorNo, chain);
            foreach (var spm in saplanan)
            {
                if (spm.KotBot - 8.0 < minY) minY = spm.KotBot - 8.0;
                if (spm.KotTop + 8.0 > maxY) maxY = spm.KotTop + 8.0;
            }

            double yShift = cursorY - minY;
            double chainTop = yShift + maxY;

            var gf = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
            var concrete = new List<Geometry>();
            int run = 0;
            while (run < m)
            {
                int runEnd = run;
                while (runEnd + 1 < m && chain[runEnd + 1].Beam.BeamId == chain[run].Beam.BeamId)
                    runEnd++;
                KirisAcilimDrawRun(tr, ms, db, chain, run, runEnd, x0, yShift, pad, gf, concrete, saplanan);
                run = runEnd + 1;
            }

            var kolonGorunus = new List<KirisAcilimKutu>();
            for (int i = 0; i <= m; i++)
            {
                KirisAcilimSpanAt(chain, i, out KirisAcilimSpan leftC, out KirisAcilimSpan rightC);
                double stationC = rightC != null ? rightC.X0 : leftC.X1;
                double kotC = KirisAcilimJointTop(leftC, rightC);
                double soffitC = KirisAcilimJointSoffit(leftC, rightC, kotC);
                KirisAcilimSupport spC = sup[i];
                if (spC.X1 - spC.X0 <= 2.0) continue;
                double yHiC = yShift + kotC + (spC.ExtendUp ? KirisAcilimKutupCm : 0);
                double yLoC = yShift + soffitC - (spC.ExtendDown ? KirisAcilimKutupCm : 0);
                double xLc = x0 + stationC + spC.X0;
                double xRc = x0 + stationC + spC.X1;
                Polygon colPoly = KirisAcilimRect(gf, xLc, yLoC, xRc, yHiC);
                if (colPoly != null) concrete.Add(colPoly);
                if (!spC.IsWall)
                    kolonGorunus.Add(new KirisAcilimKutu { X0 = xLc, Y0 = yLoC, X1 = xRc, Y1 = yHiC });
            }

            if (concrete.Count > 0)
            {
                Geometry united = null;
                try
                {
                    united = concrete.Count == 1 ? concrete[0] : CascadedPolygonUnion.Union(concrete);
                    if (united != null && !united.IsEmpty) united = united.Buffer(0);
                }
                catch { united = null; }
                if (united != null && !united.IsEmpty)
                    DrawGeometryRingsAsPolylines(tr, ms, united, LayerKiris, exteriorRingsOnly: true, applySmallTriangleTrim: true);
                else
                {
                    foreach (var g in concrete)
                        DrawGeometryRingsAsPolylines(tr, ms, g, LayerKiris, exteriorRingsOnly: true, applySmallTriangleTrim: true);
                }
            }

            foreach (var spm in saplanan)
                KirisAcilimDrawSaplamaKesit(tr, ms, x0 + spm.X0, yShift + spm.KotBot, x0 + spm.X1, yShift + spm.KotTop, kolonGorunus);

            for (int i = 0; i <= m; i++)
            {
                KirisAcilimSpanAt(chain, i, out KirisAcilimSpan left, out KirisAcilimSpan right);
                double station = right != null ? right.X0 : left.X1;
                double kot = KirisAcilimJointTop(left, right);
                double soffit = KirisAcilimJointSoffit(left, right, kot);
                double yTop = yShift + kot;
                double yBot = yShift + soffit;
                KirisAcilimSupport sp = sup[i];
                if (sp.X1 - sp.X0 <= 2.0) continue;
                double xL = x0 + station + sp.X0;
                double xR = x0 + station + sp.X1;
                if (sp.ExtendUp)
                    KirisAcilimKesitSiniri(tr, ms, xL, xR, yTop + KirisAcilimKutupCm);
                if (sp.ExtendDown)
                    KirisAcilimKesitSiniri(tr, ms, xL, xR, yBot - KirisAcilimKutupCm);
            }

            if (chainTop > cursorY) cursorY = chainTop;
        }

        private static void KirisAcilimSpanAt(List<KirisAcilimSpan> chain, int i, out KirisAcilimSpan left, out KirisAcilimSpan right)
        {
            left = i > 0 ? chain[i - 1] : null;
            right = i < chain.Count ? chain[i] : null;
        }

        private static double KirisAcilimJointTop(KirisAcilimSpan left, KirisAcilimSpan right)
        {
            if (left != null && right != null) return Math.Max(left.Kot1, right.Kot0);
            return right != null ? right.Kot0 : left.Kot1;
        }

        private static double KirisAcilimJointSoffit(KirisAcilimSpan left, KirisAcilimSpan right, double top)
        {
            double soffit = top;
            if (left != null) soffit = Math.Min(soffit, left.Kot1 - left.Beam.HeightCm);
            if (right != null) soffit = Math.Min(soffit, right.Kot0 - right.Beam.HeightCm);
            if (left == null && right != null) soffit = right.Kot0 - right.Beam.HeightCm;
            if (right == null && left != null) soffit = left.Kot1 - left.Beam.HeightCm;
            return soffit;
        }

        private void KirisAcilimDrawRun(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, int i0, int i1, double x0, double yShift, int pad,
            GeometryFactory gf, List<Geometry> concrete, List<KirisAcilimSaplama> saplanan)
        {
            int part = i0;
            while (part <= i1)
            {
                int j = part;
                while (j + 1 <= i1 && chain[j + 1].X0 - chain[j].X1 < 2.0)
                    j++;
                var pts = new List<Point2d>();
                KirisAcilimSpan first = chain[part];
                pts.Add(new Point2d(x0 + first.X0, yShift + first.Kot0 - first.Beam.HeightCm));
                for (int k = part; k <= j; k++)
                {
                    KirisAcilimSpan s = chain[k];
                    pts.Add(new Point2d(x0 + s.X1, yShift + s.Kot1 - s.Beam.HeightCm));
                }
                for (int k = j; k >= part; k--)
                {
                    KirisAcilimSpan s = chain[k];
                    pts.Add(new Point2d(x0 + s.X1, yShift + s.Kot1));
                }
                pts.Add(new Point2d(x0 + first.X0, yShift + first.Kot0));
                Polygon beamPoly = KirisAcilimPolygon(gf, pts);
                if (beamPoly != null) concrete.Add(beamPoly);
                part = j + 1;
            }

            double xMid = 0.5 * (chain[i0].X0 + chain[i1].X1);
            double yMax = double.MinValue;
            for (int i = i0; i <= i1; i++)
                yMax = Math.Max(yMax, Math.Max(chain[i].Kot0, chain[i].Kot1));
            if (saplanan != null)
            {
                double xA = chain[i0].X0;
                double xB = chain[i1].X1;
                foreach (var spm in saplanan)
                {
                    if (spm.X1 < xA - 2.0 || spm.X0 > xB + 2.0) continue;
                    if (spm.KotTop > yMax) yMax = spm.KotTop;
                }
            }
            DrawBeamLabel(tr, ms, db,
                new Point3d(x0 + xMid, yShift + yMax + 8.0 + KirisAcilimBaslikYukseklikCm * 0.5, 0),
                KirisAcilimEtiket(chain[i0].Beam, pad),
                KirisAcilimBaslikYukseklikCm, 0.0, LayerKirisYazisi, useMiddleCenter: true);
        }

        private static Polygon KirisAcilimRect(GeometryFactory gf, double x0, double y0, double x1, double y1)
        {
            double xa = Math.Min(x0, x1);
            double xb = Math.Max(x0, x1);
            double ya = Math.Min(y0, y1);
            double yb = Math.Max(y0, y1);
            if (xb - xa < 0.5 || yb - ya < 0.5 || gf == null) return null;
            try
            {
                return gf.CreatePolygon(new[]
                {
                    new Coordinate(xa, ya),
                    new Coordinate(xb, ya),
                    new Coordinate(xb, yb),
                    new Coordinate(xa, yb),
                    new Coordinate(xa, ya)
                });
            }
            catch { return null; }
        }

        private static Polygon KirisAcilimPolygon(GeometryFactory gf, List<Point2d> pts)
        {
            if (gf == null || pts == null || pts.Count < 3) return null;
            var coords = new List<Coordinate>();
            foreach (var p in pts)
            {
                if (coords.Count > 0)
                {
                    double dx = p.X - coords[coords.Count - 1].X;
                    double dy = p.Y - coords[coords.Count - 1].Y;
                    if (dx * dx + dy * dy < 0.04) continue;
                }
                coords.Add(new Coordinate(p.X, p.Y));
            }
            if (coords.Count < 3) return null;
            if (coords[0].Distance(coords[coords.Count - 1]) > 0.2)
                coords.Add(new Coordinate(coords[0].X, coords[0].Y));
            try
            {
                var poly = gf.CreatePolygon(coords.ToArray());
                if (poly == null || poly.IsEmpty) return null;
                if (!poly.IsValid)
                {
                    var fixedPoly = poly.Buffer(0);
                    return fixedPoly as Polygon;
                }
                return poly;
            }
            catch { return null; }
        }

        /// <summary>Saplanan kiriş poligonu. Kolon görünüşünün içine düşen kenarlar ACI mavi (5).</summary>
        private static void KirisAcilimDrawSaplamaKesit(
            Transaction tr, BlockTableRecord ms,
            double x0, double y0, double x1, double y1,
            List<KirisAcilimKutu> kolonlar)
        {
            if (x1 < x0) { double t = x0; x0 = x1; x1 = t; }
            if (y1 < y0) { double t = y0; y0 = y1; y1 = t; }
            if (x1 - x0 < 0.5 || y1 - y0 < 0.5) return;
            KirisAcilimDrawKesitEdge(tr, ms, x0, y0, x1, y0, true, kolonlar);
            KirisAcilimDrawKesitEdge(tr, ms, x0, y1, x1, y1, true, kolonlar);
            KirisAcilimDrawKesitEdge(tr, ms, x0, y0, x0, y1, false, kolonlar);
            KirisAcilimDrawKesitEdge(tr, ms, x1, y0, x1, y1, false, kolonlar);
        }

        private static void KirisAcilimDrawKesitEdge(
            Transaction tr, BlockTableRecord ms,
            double ax, double ay, double bx, double by, bool horizontal,
            List<KirisAcilimKutu> kolonlar)
        {
            var parts = new List<(double a, double b, bool inside)> { (horizontal ? ax : ay, horizontal ? bx : by, false) };
            if (kolonlar != null)
            {
                foreach (var col in kolonlar)
                {
                    double c0 = horizontal ? col.X0 : col.Y0;
                    double c1 = horizontal ? col.X1 : col.Y1;
                    double fixed0 = horizontal ? col.Y0 : col.X0;
                    double fixed1 = horizontal ? col.Y1 : col.X1;
                    double at = horizontal ? ay : ax;
                    if (c1 < c0) { double t = c0; c0 = c1; c1 = t; }
                    if (fixed1 < fixed0) { double t = fixed0; fixed0 = fixed1; fixed1 = t; }
                    bool crosses = at >= fixed0 - 0.5 && at <= fixed1 + 0.5;
                    var next = new List<(double a, double b, bool inside)>();
                    foreach (var p in parts)
                    {
                        if (p.inside || !crosses || p.b - p.a < 0.5)
                        {
                            next.Add(p);
                            continue;
                        }
                        double lo = Math.Max(p.a, c0);
                        double hi = Math.Min(p.b, c1);
                        if (hi - lo < 0.5)
                        {
                            next.Add(p);
                            continue;
                        }
                        if (lo - p.a > 0.5) next.Add((p.a, lo, false));
                        next.Add((lo, hi, true));
                        if (p.b - hi > 0.5) next.Add((hi, p.b, false));
                    }
                    parts = next;
                }
            }

            foreach (var p in parts)
            {
                if (p.b - p.a < 0.5) continue;
                Line line = horizontal
                    ? new Line(new Point3d(p.a, ay, 0), new Point3d(p.b, ay, 0))
                    : new Line(new Point3d(ax, p.a, 0), new Point3d(ax, p.b, 0));
                line.Layer = LayerKesitSiniri;
                if (p.inside)
                    line.Color = Color.FromColorIndex(ColorMethod.ByAci, 5);
                AppendEntity(tr, ms, line);
            }
        }

        private static void KirisAcilimKesitSiniri(Transaction tr, BlockTableRecord ms, double xL, double xR, double y)
        {
            const double overhang = 8.0;
            AppendEntity(tr, ms, new Line(
                new Point3d(xL - overhang, y, 0),
                new Point3d(xR + overhang, y, 0))
            { Layer = LayerKesitSiniri });
        }

        /// <summary>
        /// Diğer yöndeki kirişin şeridi (genişlik + kaçıklık) bu kirişin şeridiyle kesişiyorsa
        /// görünüşte o aralığa, saplanan kirişin kendi üst ve alt kotuyla oturtulur.
        /// </summary>
        private List<KirisAcilimSaplama> KirisAcilimCollectSaplanan(int floorNo, List<KirisAcilimSpan> chain)
        {
            var list = new List<KirisAcilimSaplama>();
            if (chain == null || chain.Count == 0) return list;
            var gf = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
            var inChain = new HashSet<int>();
            var hostPoly = new Polygon[chain.Count];
            for (int i = 0; i < chain.Count; i++)
            {
                if (chain[i]?.Beam != null) inChain.Add(chain[i].Beam.BeamId);
                if (TryKirisAcilimBeamStrip(chain[i].Beam, gf, out Polygon hp, out _, out _, out _, out _))
                    hostPoly[i] = hp;
            }

            var seenAxis = new HashSet<string>();
            foreach (var other in GetRawBeamsOnFloor(floorNo))
            {
                if (other == null || other.IsWallFlag == 1 || other.HeightCm <= 1.0) continue;
                if (inChain.Contains(other.BeamId)) continue;
                int ax0 = Math.Min(other.StartAxisId, other.EndAxisId);
                int ax1 = Math.Max(other.StartAxisId, other.EndAxisId);
                string axisKey = string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}:{3}", other.FixedAxisId, ax0, ax1, other.OffsetRaw);
                if (!seenAxis.Add(axisKey)) continue;
                if (!TryKirisAcilimBeamStrip(other, gf, out Polygon otherPoly, out Point2d oa, out Point2d ob, out double kotA, out double kotB))
                    continue;
                Vector2d ov = ob - oa;
                if (ov.Length < 1.0) continue;
                Vector2d ou = ov.GetNormal();

                for (int i = 0; i < chain.Count; i++)
                {
                    if (hostPoly[i] == null) continue;
                    KirisAcilimSpan host = chain[i];
                    Vector2d hv = host.World1 - host.World0;
                    if (hv.Length < 1.0) continue;
                    Vector2d hu = hv.GetNormal();
                    if (KirisAcilimHatAcisiDerece(hu, ou) <= KirisAcilimMaxDonusDerece + 1e-6) continue;
                    if (Math.Abs(hu.X * ou.Y - hu.Y * ou.X) < 0.20) continue;

                    Geometry inter;
                    try { inter = hostPoly[i].Intersection(otherPoly); }
                    catch { continue; }
                    if (inter == null || inter.IsEmpty || inter.Coordinates == null || inter.Coordinates.Length == 0)
                        continue;

                    double tMin = double.MaxValue;
                    double tMax = double.MinValue;
                    double sx = 0, sy = 0;
                    int n = 0;
                    foreach (Coordinate c in inter.Coordinates)
                    {
                        if (c == null) continue;
                        double t = (c.X - host.World0.X) * hu.X + (c.Y - host.World0.Y) * hu.Y;
                        if (t < tMin) tMin = t;
                        if (t > tMax) tMax = t;
                        sx += c.X;
                        sy += c.Y;
                        n++;
                    }
                    if (n == 0 || tMax - tMin < 2.0) continue;

                    double along = (sx / n - oa.X) * ou.X + (sy / n - oa.Y) * ou.Y;
                    double u = along / ov.Length;
                    if (u < 0) u = 0;
                    if (u > 1) u = 1;
                    double top = kotA + (kotB - kotA) * u;
                    double bot = top - other.HeightCm;
                    double mid = host.X0 + 0.5 * (tMin + tMax);
                    double half = other.WidthCm * 0.5;
                    KirisAcilimAddSaplama(list, other.BeamId, mid - half, mid + half, top, bot);
                }
            }
            return list;
        }

        private static void KirisAcilimAddSaplama(List<KirisAcilimSaplama> list, int beamId, double x0, double x1, double top, double bot)
        {
            if (x1 < x0) { double t = x0; x0 = x1; x1 = t; }
            if (top < bot) { double t = top; top = bot; bot = t; }
            if (x1 - x0 < 2.0 || top - bot < 2.0) return;
            double mid = 0.5 * (x0 + x1);
            double half = 0.5 * (x1 - x0);
            for (int i = 0; i < list.Count; i++)
            {
                KirisAcilimSaplama s = list[i];
                if (s.BeamId != beamId) continue;
                double sm = 0.5 * (s.X0 + s.X1);
                if (Math.Abs(sm - mid) > half + 40.0) continue;
                double c = 0.5 * (sm + mid);
                double h = 0.5 * ((s.KotTop - s.KotBot) + (top - bot));
                double tTop = 0.5 * (s.KotTop + top);
                s.X0 = c - half;
                s.X1 = c + half;
                s.KotTop = tTop;
                s.KotBot = tTop - h;
                list[i] = s;
                return;
            }
            list.Add(new KirisAcilimSaplama { BeamId = beamId, X0 = x0, X1 = x1, KotTop = top, KotBot = bot });
        }

        private bool TryKirisAcilimBeamStrip(
            BeamInfo beam, GeometryFactory gf, out Polygon poly,
            out Point2d a, out Point2d b, out double kotA, out double kotB)
        {
            poly = null;
            a = b = default;
            kotA = kotB = 0;
            if (beam == null || gf == null || _axisService == null) return false;
            if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d start)) return false;
            if (!_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d end)) return false;
            a = start;
            b = end;
            NormalizeBeamDirection(beam.FixedAxisId, ref a, ref b);
            bool aIsStart = a.GetDistanceTo(start) <= a.GetDistanceTo(end);
            kotA = aIsStart ? beam.Point1KotCm : beam.Point2KotCm;
            kotB = aIsStart ? beam.Point2KotCm : beam.Point1KotCm;
            Vector2d dir = b - a;
            if (dir.Length < 1.0) return false;
            Vector2d u = dir.GetNormal();
            Vector2d perp = new Vector2d(-u.Y, u.X);
            double hw = beam.WidthCm * 0.5;
            if (hw < 0.5) return false;
            ComputeBeamEdgeOffsets(beam.OffsetRaw, hw, out double upper, out double lower);
            Point2d q1 = a + perp.MultiplyBy(upper);
            Point2d q2 = b + perp.MultiplyBy(upper);
            Point2d q3 = b + perp.MultiplyBy(lower);
            Point2d q4 = a + perp.MultiplyBy(lower);
            try
            {
                poly = gf.CreatePolygon(new[]
                {
                    new Coordinate(q1.X, q1.Y),
                    new Coordinate(q2.X, q2.Y),
                    new Coordinate(q3.X, q3.Y),
                    new Coordinate(q4.X, q4.Y),
                    new Coordinate(q1.X, q1.Y)
                });
                return poly != null && !poly.IsEmpty && poly.Area > 1.0;
            }
            catch
            {
                poly = null;
                return false;
            }
        }

        /// <summary>Mesnet genişliği, aks noktasından kiriş doğrultusuna göre (cm). Kolon varsa perdeye bakılmaz.</summary>
        private static bool KirisAcilimColumnOnAxes(ColumnAxisInfo col, int axisA, int axisB)
        {
            if (col == null || axisA == 0 || axisB == 0) return false;
            return (col.AxisXId == axisA && col.AxisYId == axisB)
                || (col.AxisXId == axisB && col.AxisYId == axisA);
        }

        private bool TryKirisAcilimSupportWidth(
            int floorNo, Point2d node, Vector2d beamDir,
            int fixedAxis, int crossAxis, int fixedAxis2, int crossAxis2,
            double beamTopAbsCm, double beamBotAbsCm,
            out double xLo, out double xHi, out bool isWall, out bool extendUp, out bool extendDown)
        {
            xLo = xHi = 0;
            isWall = false;
            extendUp = extendDown = false;
            if (beamDir.Length < 1e-9) return false;
            Vector2d u = beamDir.GetNormal();

            if (_model.Columns != null)
            {
                double best = double.MaxValue;
                bool found = false;
                double blo = 0, bhi = 0;
                ColumnAxisInfo matchedCol = null;
                foreach (var col in _model.Columns)
                {
                    if (col == null) continue;
                    if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                    if (axisNode.GetDistanceTo(node) > 160.0) continue;
                    bool onAxes = KirisAcilimColumnOnAxes(col, fixedAxis, crossAxis)
                        || KirisAcilimColumnOnAxes(col, fixedAxis2, crossAxis2);
                    if (!TryKirisAcilimColumnInterval(floorNo, col, axisNode, node, u, out double lo, out double hi, out bool coversNode))
                        continue;
                    if (!onAxes && !coversNode) continue;
                    if (onAxes && axisNode.GetDistanceTo(node) > 40.0 && !coversNode) continue;
                    double d = Math.Abs(0.5 * (lo + hi));
                    if (d < best)
                    {
                        best = d;
                        blo = lo;
                        bhi = hi;
                        matchedCol = col;
                        found = true;
                    }
                }
                if (found && bhi - blo > 2.0)
                {
                    xLo = blo;
                    xHi = bhi;
                    KirisAcilimColumnExtend(floorNo, matchedCol, beamTopAbsCm, beamBotAbsCm, out extendUp, out extendDown);
                    return true;
                }
            }

            if (TryKirisAcilimWallInterval(floorNo, node, u, crossAxis, out double wLo, out double wHi)
                || (crossAxis2 != crossAxis && TryKirisAcilimWallInterval(floorNo, node, u, crossAxis2, out wLo, out wHi)))
            {
                xLo = wLo;
                xHi = wHi;
                isWall = true;
                extendUp = true;
                extendDown = true;
                return true;
            }
            return false;
        }

        private void KirisAcilimColumnExtend(
            int floorNo, ColumnAxisInfo col, double beamTopAbsCm, double beamBotAbsCm,
            out bool extendUp, out bool extendDown)
        {
            extendUp = true;
            extendDown = true;
            if (col == null) return;
            bool upperFloor = KirisAcilimColumnOnAnotherFloor(floorNo, col, above: true);
            bool lowerFloor = KirisAcilimColumnOnAnotherFloor(floorNo, col, above: false);
            double altCm, ustCm;
            if (!TryKirisAcilimColumnAbsCm(floorNo, col, out altCm, out ustCm))
                return;
            // Üst katta aynı kolon varsa kütük çıkar. Bu katın üst kotu çoğu zaman kiriş üstüne eşittir.
            extendUp = upperFloor || ustCm > beamTopAbsCm + 5.0;
            extendDown = lowerFloor || altCm < beamBotAbsCm - 5.0;
        }

        private bool KirisAcilimColumnOnAnotherFloor(int floorNo, ColumnAxisInfo col, bool above)
        {
            if (col == null || _model?.Floors == null) return false;
            FloorInfo here = null;
            foreach (var f in _model.Floors)
            {
                if (f.FloorNo == floorNo) { here = f; break; }
            }
            if (here == null) return false;
            foreach (var f in _model.Floors)
            {
                if (f.FloorNo == floorNo) continue;
                if (above && f.ElevationM <= here.ElevationM + 0.02) continue;
                if (!above && f.ElevationM >= here.ElevationM - 0.02) continue;
                if (HasColumnOnFloor(f, col)) return true;
            }
            return false;
        }

        private bool TryKirisAcilimColumnAbsCm(int floorNo, ColumnAxisInfo col, out double altCm, out double ustCm)
        {
            altCm = ustCm = 0;
            double baseK = _model.BuildingBaseKotu;
            if (col.ColumnType == 3)
            {
                int posId = ResolvePolygonPositionSectionId(floorNo, col.ColumnNo);
                if (posId > 0 && _model.PolygonColumnKotMFromBinaTabaniByPositionId.TryGetValue(posId, out var pk))
                {
                    altCm = (pk.altM + baseK) * 100.0;
                    ustCm = (pk.ustM + baseK) * 100.0;
                    return ustCm > altCm + 1.0;
                }
                return false;
            }

            int sectionId = ResolveColumnSectionId(floorNo, col.ColumnNo);
            if ((sectionId <= 0 || !_model.ColumnKotMFromBinaTabaniBySectionId.ContainsKey(sectionId))
                && col.ColumnId > 0 && _model.ColumnKotMFromBinaTabaniBySectionId.ContainsKey(col.ColumnId))
                sectionId = col.ColumnId;
            if (sectionId > 0 && _model.ColumnKotMFromBinaTabaniBySectionId.TryGetValue(sectionId, out var kotM))
            {
                altCm = (kotM.altM + baseK) * 100.0;
                ustCm = (kotM.ustM + baseK) * 100.0;
                return ustCm > altCm + 1.0;
            }
            return false;
        }

        private bool TryKirisAcilimColumnInterval(
            int floorNo, ColumnAxisInfo col, Point2d axisNode, Point2d origin, Vector2d u,
            out double lo, out double hi, out bool coversNode)
        {
            lo = hi = 0;
            coversNode = false;
            int sectionId = ResolveColumnSectionId(floorNo, col.ColumnNo);
            int polygonSectionId = ResolvePolygonPositionSectionId(floorNo, col.ColumnNo);
            if (col.ColumnType != 3 && (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId))
                && col.ColumnId > 0 && _model.ColumnDimsBySectionId.ContainsKey(col.ColumnId))
                sectionId = col.ColumnId;

            bool hasDim = sectionId > 0 && _model.ColumnDimsBySectionId.ContainsKey(sectionId);
            if (col.ColumnType != 3 && !hasDim && _model.Floors != null)
            {
                foreach (var f in _model.Floors)
                {
                    if (f.FloorNo == floorNo) continue;
                    int sid = ResolveColumnSectionId(f.FloorNo, col.ColumnNo);
                    if (sid > 0 && _model.ColumnDimsBySectionId.ContainsKey(sid))
                    {
                        sectionId = sid;
                        hasDim = true;
                        break;
                    }
                }
            }
            if (col.ColumnType != 3 && !hasDim) return false;
            var dim = hasDim ? _model.ColumnDimsBySectionId[sectionId] : (W: 40.0, H: 40.0);
            double hw = dim.W / 2.0;
            double hh = dim.H / 2.0;
            var offsetLocal = col.ColumnType == 2
                ? ComputeColumnOffsetCircle(col.OffsetXRaw, col.OffsetYRaw)
                : ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
            var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
            var center = new Point2d(axisNode.X + offsetGlobal.X, axisNode.Y + offsetGlobal.Y);

            if (col.ColumnType == 2)
            {
                double r = Math.Max(hw, hh);
                double c = (center.X - origin.X) * u.X + (center.Y - origin.Y) * u.Y;
                lo = c - r;
                hi = c + r;
                double dx = origin.X - center.X;
                double dy = origin.Y - center.Y;
                coversNode = dx * dx + dy * dy <= (r + 12.0) * (r + 12.0);
                return true;
            }

            Point2d[] pts = null;
            if (col.ColumnType == 3)
            {
                if (!TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out pts) || pts == null || pts.Length < 3)
                    return false;
            }
            else
            {
                pts = BuildRect(center, hw, hh, col.AngleDeg);
            }

            lo = double.MaxValue;
            hi = double.MinValue;
            foreach (var p in pts)
            {
                double t = (p.X - origin.X) * u.X + (p.Y - origin.Y) * u.Y;
                if (t < lo) lo = t;
                if (t > hi) hi = t;
            }
            coversNode = KirisAcilimPointInPoly(origin, pts, 12.0);
            return hi > lo;
        }

        private static bool KirisAcilimPointInPoly(Point2d p, Point2d[] poly, double tolCm)
        {
            if (poly == null || poly.Length < 3) return false;
            bool inside = false;
            double tol2 = tolCm * tolCm;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                double xi = poly[i].X, yi = poly[i].Y;
                double xj = poly[j].X, yj = poly[j].Y;
                if (((yi > p.Y) != (yj > p.Y)) &&
                    (p.X < (xj - xi) * (p.Y - yi) / ((yj - yi) == 0 ? 1e-12 : (yj - yi)) + xi))
                    inside = !inside;
                double vx = xj - xi, vy = yj - yi;
                double len2 = vx * vx + vy * vy;
                double t = len2 < 1e-9 ? 0 : ((p.X - xi) * vx + (p.Y - yi) * vy) / len2;
                if (t < 0) t = 0;
                else if (t > 1) t = 1;
                double ex = xi + t * vx - p.X;
                double ey = yi + t * vy - p.Y;
                if (ex * ex + ey * ey <= tol2) return true;
            }
            return inside;
        }

        private bool TryKirisAcilimWallInterval(
            int floorNo, Point2d node, Vector2d beamU, int crossAxisId,
            out double lo, out double hi)
        {
            lo = hi = 0;
            bool found = false;
            double best = double.MaxValue;
            foreach (var wall in GetRawBeamsOnFloor(floorNo))
            {
                if (wall == null || wall.IsWallFlag != 1 || wall.WidthCm <= 1.0) continue;
                if (wall.FixedAxisId != crossAxisId) continue;
                if (!_axisService.TryIntersect(wall.FixedAxisId, wall.StartAxisId, out Point2d a)) continue;
                if (!_axisService.TryIntersect(wall.FixedAxisId, wall.EndAxisId, out Point2d b)) continue;
                Vector2d wv = b - a;
                if (wv.Length < 1.0) continue;
                Vector2d wu = wv.GetNormal();
                double sin = Math.Abs(wu.X * beamU.Y - wu.Y * beamU.X);
                if (sin < 0.25) continue;
                double t = (node.X - a.X) * wu.X + (node.Y - a.Y) * wu.Y;
                if (t < -15.0 || t > wv.Length + 15.0) continue;
                double lat = Math.Abs((node.X - a.X) * (-wu.Y) + (node.Y - a.Y) * wu.X);
                if (lat > wall.WidthCm * 0.5 + 20.0) continue;
                double half = (wall.WidthCm * 0.5) / sin;
                if (half < best)
                {
                    best = half;
                    lo = -half;
                    hi = half;
                    found = true;
                }
            }
            return found;
        }

        private string KirisAcilimEtiket(BeamInfo beam, int pad)
        {
            int floorNo = GetBeamFloorNo(beam.BeamId);
            FloorInfo floor = null;
            if (_model.Floors != null)
            {
                foreach (var f in _model.Floors)
                {
                    if (f.FloorNo == floorNo) { floor = f; break; }
                }
            }
            string kat = floor != null && !string.IsNullOrEmpty(floor.ShortName)
                ? floor.ShortName
                : floorNo.ToString(CultureInfo.InvariantCulture);
            int no = GetBeamNumero(beam.BeamId);
            string num = no.ToString("D" + pad, CultureInfo.InvariantCulture);
            return string.Format(CultureInfo.InvariantCulture, "K{0}{1} ({2}/{3})",
                kat, num, (int)Math.Round(beam.WidthCm), (int)Math.Round(beam.HeightCm));
        }

        private string KirisAcilimAksYazi(int axisId)
        {
            if (_model.AxisX != null)
            {
                for (int i = 0; i < _model.AxisX.Count; i++)
                {
                    if (_model.AxisX[i].Id != axisId) continue;
                    if (_model.GprAxisXLabelByRow.TryGetValue(i + 1, out var gx) && !string.IsNullOrWhiteSpace(gx))
                        return gx.Trim();
                    return (i + 1).ToString(CultureInfo.InvariantCulture);
                }
            }
            if (_model.AxisY != null)
            {
                for (int j = 0; j < _model.AxisY.Count; j++)
                {
                    if (_model.AxisY[j].Id != axisId) continue;
                    if (_model.GprAxisYLabelByRow.TryGetValue(j + 1, out var gy) && !string.IsNullOrWhiteSpace(gy))
                        return gy.Trim();
                    return j < 26
                        ? ((char)('A' + j)).ToString()
                        : "A" + (j - 25).ToString(CultureInfo.InvariantCulture);
                }
            }
            return axisId.ToString(CultureInfo.InvariantCulture);
        }
    }
}
