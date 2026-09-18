using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        /// <summary>KOLONDUSEY: GPR kolon etriye çapı (TS 500 / TBDY 2018 kanca).</summary>
        private Dictionary<string, (string ebat, string donati, string etriye)> _kolonDuseyGpr;
        private string _kolonKesitLayerOverride;

        /// <summary>KOLONDUSEY: ST4 düşey açılım; GPR varsa etriye TS 500 / TBDY 2018.</summary>
        public bool DrawKolonDuseyFromSt4(
            Point3d insertLl,
            Database db,
            Editor ed,
            Transaction tr,
            BlockTableRecord btr,
            string st4SourcePath = null)
        {
            const double s = 1.0;
            _ntsDrawFactory = NtsGeometryServices.Instance.CreateGeometryFactory();
            _kolonDuseyGpr = null;
            try
            {
                string gprPath = ResolveGprPathNextToSt4(st4SourcePath);
                if (!string.IsNullOrEmpty(gprPath))
                {
                    _kolonDuseyGpr = KolonDonatiTableDrawer.ParseKolonBetonarmeFromFile(gprPath, out _);
                    GprPerdePanelDonatiParser.TryReadMaterials(gprPath, out _rebarFckMPa, out _rebarFykMPa);
                }
                if (_model?.Floors == null || _model.Floors.Count == 0 || _model.Columns == null)
                {
                    ed?.WriteMessage("\nKOLONDUSEY: ST4 kat/kolon yok.");
                    return false;
                }
                EnsureLayers(tr, db);
                EnsurePlanLayer(tr, db, LayerKiris, 2, LineWeight.LineWeight030, useDashed: false);
                EnsurePlanLayer(tr, db, LayerPerde, 6, LineWeight.LineWeight040, useDashed: false);
                EnsurePlanLayer(tr, db, LayerKolon, 3, LineWeight.LineWeight040, useDashed: false);
                EnsurePlanLayer(tr, db, LayerEtriye, 150, LineWeight.LineWeight035, useDashed: false);
                EnsurePlanLayer(tr, db, LayerCirozBeykent, 140, LineWeight.LineWeight020, useDashed: false);
                EnsurePlanLayer(tr, db, LayerDonatiGovde, 4, LineWeight.LineWeight040, useDashed: false);
                EnsurePlanLayer(tr, db, LayerTemelBeykent, 2, LineWeight.LineWeight030, useDashed: false);
                EnsurePlanLayer(tr, db, LayerKesitSiniri, 211, LineWeight.LineWeight020, useDashed: true);
                EnsurePlanLayer(tr, db, LayerKesitGorunus, 253, LineWeight.LineWeight020, useDashed: true);
                EnsurePlanLayer(tr, db, LayerIzdusum, 253, LineWeight.LineWeight020, useDashed: true);
                EnsurePlanLayer(tr, db, LayerKolonIsmi, 91, LineWeight.LineWeight020, useDashed: false);
                EnsurePlanLayer(tr, db, LayerDonatiYazisiPerde, 3, LineWeight.LineWeight020, useDashed: false);
                EnsurePlanLayer(tr, db, LayerOlcu, 14, LineWeight.LineWeight020, useDashed: false);
                EnsurePlanLayer(tr, db, LayerKotYazi, 7, LineWeight.LineWeight020, useDashed: false);
                EnsurePlanLayer(tr, db, LayerKotCizgisi, 7, LineWeight.LineWeight020, useDashed: false);

                var groups = new Dictionary<string, List<ColumnAxisInfo>>(StringComparer.Ordinal);
                foreach (var col in _model.Columns)
                {
                    if (col == null) continue;
                    string sig = BuildKolonDuseyGroupSignature(col);
                    if (string.IsNullOrEmpty(sig) || sig.IndexOf('/') < 0) continue;
                    if (!groups.TryGetValue(sig, out var list))
                    {
                        list = new List<ColumnAxisInfo>();
                        groups[sig] = list;
                    }
                    list.Add(col);
                }
                if (groups.Count == 0)
                {
                    ed?.WriteMessage("\nKOLONDUSEY: aktif kolon bulunamadi.");
                    return false;
                }

                double xCursor = insertLl.X;
                int nGrp = 0;
                foreach (var kv in groups.OrderBy(g => g.Value.Min(c => c.ColumnNo)))
                {
                    kv.Value.Sort((a, b) => a.ColumnNo.CompareTo(b.ColumnNo));
                    double w = DrawOneKolonDuseyGroup(tr, btr, db, kv.Value, new Point3d(xCursor, insertLl.Y, 0), s);
                    xCursor += w + 200.0 * s;
                    nGrp++;
                }
                ed?.WriteMessage("\nKOLONDUSEY: {0} benzer kolon grubu (1/50, GPR etriye TS500/TBDY2018).", nGrp);
                return true;
            }
            finally
            {
                _ntsDrawFactory = null;
                _kolonDuseyGpr = null;
            }
        }

        private string BuildKolonDuseyGroupSignature(ColumnAxisInfo col)
        {
            var sb = new StringBuilder();
            bool any = false;
            for (int fi = 0; fi < _model.Floors.Count; fi++)
            {
                var floor = _model.Floors[fi];
                if (!HasColumnOnFloor(floor, col))
                {
                    sb.Append("-;");
                    continue;
                }
                any = true;
                var dims = GetColumnDimensionsForFloor(floor);
                var extra = GetColumnTableExtraData(floor);
                double w = 0, h = 0, alt = 0, yuk = 0;
                if (dims != null && dims.TryGetValue(col.ColumnNo, out var d) && d.columnType != 3)
                {
                    w = d.W;
                    h = d.H;
                }
                if (extra != null && extra.TryGetValue(col.ColumnNo, out var ex))
                {
                    alt = ex.altKotCm;
                    yuk = ex.yukseklikCm;
                }
                var gf = _ntsDrawFactory;
                Geometry poly = gf != null ? GetColumnPolygonForTable(floor, col, 0, 0, gf) : null;
                string kirisImza = BuildKolonKatSapKirisImza(floor, col, poly);
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0}/{1:0}:{2:0}:{3:0}:k{4};", w, h, alt, yuk, kirisImza);
            }
            return any ? sb.ToString() : "";
        }

        private double DrawOneKolonDuseyGroup(
            Transaction tr,
            BlockTableRecord btr,
            Database db,
            List<ColumnAxisInfo> cols,
            Point3d origin,
            double s)
        {
            var col = cols[0];
            var gf = _ntsDrawFactory;
            FloorInfo firstColFloor = null;
            Geometry firstPoly = null;
            foreach (var fl in _model.Floors)
            {
                if (!HasColumnOnFloor(fl, col)) continue;
                firstPoly = GetColumnPolygonForTable(fl, col, 0, 0, gf);
                if (firstPoly != null && !firstPoly.IsEmpty)
                {
                    firstColFloor = fl;
                    break;
                }
            }
            if (firstColFloor == null || firstPoly == null) return 40.0 * s;

            var cxy = firstPoly.Centroid;
            double cx = cxy.X, cy = cxy.Y;
            double majorAngleDeg = ResolveKolonMajorAngleDeg(firstPoly, col.AngleDeg, cx, cy);
            var rot = AffineTransformation.RotationInstance(-majorAngleDeg * Math.PI / 180.0, cx, cy);
            var ident = new AffineTransformation();
            Geometry colRot;
            try { colRot = rot.Transform(firstPoly); }
            catch { colRot = firstPoly; }
            var colEnv = colRot.EnvelopeInternal;
            double colLo = colEnv.MinX, colHi = colEnv.MaxX;
            double sectionY = (colEnv.MinY + colEnv.MaxY) * 0.5;
            double wallHalf = Math.Max(4.0, (colEnv.MaxY - colEnv.MinY) * 0.5 + 2.0);

            var temelSpans = new List<(double x0, double x1, double z0, double z1)>();
            try { CollectTemelSpansAlongWall(firstPoly, rot, ident, 0, 0, firstColFloor, temelSpans, sectionY); }
            catch { temelSpans.Clear(); }

            var stories = new List<(double zBot, double zTop, Geometry poly, double majorDeg, double lo, double hi, int floorIndex)>();
            var beamRuns = new List<(double x0, double x1, double zb, double zt)>();
            var wallRuns = new List<(double x0, double x1, double zb, double zt)>();
            var sapKirisRuns = new List<(double x0, double x1, double zb, double zt)>();
            for (int fi = 0; fi < _model.Floors.Count; fi++)
            {
                var floor = _model.Floors[fi];
                if (!HasColumnOnFloor(floor, col)) continue;
                var extra = GetColumnTableExtraData(floor);
                if (extra == null || !extra.TryGetValue(col.ColumnNo, out var ex) || ex.yukseklikCm < 1.0)
                    continue;
                var polyF = GetColumnPolygonForTable(floor, col, 0, 0, gf) ?? firstPoly;
                double majF = majorAngleDeg;
                if (polyF != null && !polyF.IsEmpty)
                {
                    var pc = polyF.Centroid;
                    majF = ResolveKolonMajorAngleDeg(polyF, col.AngleDeg, pc.X, pc.Y);
                }
                double loF = colLo, hiF = colHi;
                if (polyF != null && !polyF.IsEmpty)
                {
                    Geometry pr;
                    try { pr = rot.Transform(polyF); }
                    catch { pr = polyF; }
                    var er = pr.EnvelopeInternal;
                    loF = er.MinX;
                    hiF = er.MaxX;
                }
                stories.Add((ex.altKotCm, ex.altKotCm + ex.yukseklikCm, polyF, majF, loF, hiF, fi));
                try { CollectKenarKirisOrPerdeSpans(floor, polyF, rot, sectionY, wallHalf, beamRuns, walls: false); }
                catch { }
                try { CollectKenarKirisOrPerdeSpans(floor, polyF, rot, sectionY, wallHalf, wallRuns, walls: true); }
                catch { }
                try { CollectKolonSaplananKirisSpans(floor, polyF, rot, sapKirisRuns); }
                catch { }
            }
            if (stories.Count == 0) return 40.0 * s;
            stories.Sort((a, b) => a.zBot.CompareTo(b.zBot));
            colLo = stories.Min(st => st.lo);
            colHi = stories.Max(st => st.hi);

            const double kenarKesitCm = 100.0;
            const double katHizaUzantiCm = 170.0;
            if (temelSpans.Count == 0)
            {
                var fond = GetColumnFoundationHeights(_model.Floors[0]);
                if (fond.TryGetValue(col.ColumnNo, out var fh) && fh.temelCm.HasValue && fh.temelCm.Value > 1.0)
                {
                    double zTop = stories[0].zBot;
                    double zBot = zTop - fh.temelCm.Value;
                    temelSpans.Add((colLo - kenarKesitCm, colHi + kenarKesitCm, zBot, zTop));
                }
            }

            double zMin = stories.Min(st => st.zBot);
            double zMax = stories.Max(st => st.zTop);
            if (temelSpans.Count > 0)
            {
                zMin = Math.Min(zMin, temelSpans.Min(t => t.z0));
                zMax = Math.Max(zMax, temelSpans.Max(t => t.z1));
            }
            foreach (var b in beamRuns)
            {
                zMin = Math.Min(zMin, b.zb);
                zMax = Math.Max(zMax, b.zt);
            }
            foreach (var w in wallRuns)
            {
                zMin = Math.Min(zMin, w.zb);
                zMax = Math.Max(zMax, w.zt);
            }

            bool anyLeft = KenarHasSide(beamRuns, wallRuns, colLo, colHi, left: true);
            bool anyRight = KenarHasSide(beamRuns, wallRuns, colLo, colHi, left: false);
            double xMin = colLo, xMax = colHi;
            if (anyLeft || temelSpans.Count > 0) xMin = Math.Min(xMin, colLo - kenarKesitCm);
            if (anyRight || temelSpans.Count > 0) xMax = Math.Max(xMax, colHi + kenarKesitCm);
            xMin = Math.Min(xMin, colLo - katHizaUzantiCm);
            xMax = Math.Max(xMax, colHi + katHizaUzantiCm);
            foreach (var t in temelSpans)
            {
                xMin = Math.Min(xMin, t.x0);
                xMax = Math.Max(xMax, t.x1);
            }

            double maxPlanW = 40.0 * s;
            foreach (var st in stories)
            {
                if (st.poly == null || st.poly.IsEmpty) continue;
                var pc = st.poly.Centroid;
                var rPlan = AffineTransformation.RotationInstance(-st.majorDeg * Math.PI / 180.0, pc.X, pc.Y);
                Geometry gPlan;
                try { gPlan = rPlan.Transform(st.poly); }
                catch { gPlan = st.poly; }
                maxPlanW = Math.Max(maxPlanW, gPlan.EnvelopeInternal.Width);
            }

            double nameW = 36.0 * s;
            double kotBand = 40.0 * s + Kolon50GorunusDikeyOlcuSagaCm;
            const double planKesitGapFromElevCm = 60.0;
            double planBand = Kolon50GorunusCiftOlcuAraCm + maxPlanW + planKesitGapFromElevCm;
            double X(double x) => origin.X + planBand + nameW + kotBand + (x - xMin) * s;
            double Y(double z) => origin.Y + 20.0 * s + (z - zMin) * s;

            void Ln(double ax, double ay, double bx, double by, string layer)
            {
                var line = new Line(new Point3d(ax, ay, 0), new Point3d(bx, by, 0));
                line.SetDatabaseDefaults();
                line.Layer = layer;
                AppendEntity(tr, btr, line);
            }

            double zDrawBot = stories[0].zBot;
            if (temelSpans.Count > 0)
                zDrawBot = Math.Max(zDrawBot, temelSpans.Max(t => t.z1));

            var segs = new GorunusLineBag();
            for (int i = 0; i < stories.Count; i++)
            {
                var st = stories[i];
                double y0 = Y(i == 0 ? zDrawBot : st.zBot);
                double y1 = Y(st.zTop);
                segs.AddV(X(st.lo), y0, y1, LayerKolon, KenarYHoles(Y, st.lo, st.hi, left: true, beamRuns));
                segs.AddV(X(st.hi), y0, y1, LayerKolon, KenarYHoles(Y, st.lo, st.hi, left: false, beamRuns));
                if (i + 1 < stories.Count)
                {
                    var up = stories[i + 1];
                    double yJ = Y(st.zTop);
                    if (Math.Abs(up.lo - st.lo) > 2.0)
                        segs.AddH(X(Math.Min(st.lo, up.lo)), X(Math.Max(st.lo, up.lo)), yJ, LayerKolon, null);
                    if (Math.Abs(up.hi - st.hi) > 2.0)
                        segs.AddH(X(Math.Min(st.hi, up.hi)), X(Math.Max(st.hi, up.hi)), yJ, LayerKolon, null);
                }
                else
                    segs.AddH(X(st.lo), X(st.hi), y1, LayerKolon, null);
            }
            segs.Flush(tr, btr);

            double FaceAt(bool left, double zb, double zt)
            {
                double zm = 0.5 * (zb + zt);
                for (int i = stories.Count - 1; i >= 0; i--)
                {
                    var st = stories[i];
                    if (zm >= st.zBot - 2.0 && zm <= st.zTop + 2.0)
                        return left ? st.lo : st.hi;
                }
                return left ? colLo : colHi;
            }
            DrawKenarSpanOutlines(Ln, X, Y, colLo, colHi, wallRuns, LayerPerde, kenarKesitCm, mergeStories: true, FaceAt);
            DrawKenarSpanOutlines(Ln, X, Y, colLo, colHi, beamRuns, LayerKiris, kenarKesitCm, mergeStories: false, FaceAt);
            var etriyeKiris = new List<(double x0, double x1, double zb, double zt)>(beamRuns.Count + sapKirisRuns.Count);
            etriyeKiris.AddRange(beamRuns);
            etriyeKiris.AddRange(sapKirisRuns);
            DrawKolonGorunusDuseyDonatilar(tr, btr, col, stories, rot, X, Y, zDrawBot, temelSpans, etriyeKiris);
            var etriyeZs = new List<double>();
            var etriyeBolgeler = new List<(double zLo, double zHi, int sCm, int diaMm)>();
            DrawKolonGorunusEtriyeler(tr, btr, col, stories, rot, X, Y, zDrawBot, etriyeKiris, Ln, temelSpans, etriyeZs, etriyeBolgeler);

            var katHizaZs = new HashSet<double>();
            foreach (var st in stories)
            {
                katHizaZs.Add(Math.Round(st.zBot, 3));
                katHizaZs.Add(Math.Round(st.zTop, 3));
            }
            if (temelSpans.Count > 0)
                katHizaZs.Add(Math.Round(temelSpans.Min(t => t.z0), 3));
            foreach (var zKat in katHizaZs)
            {
                Ln(X(colLo - katHizaUzantiCm), Y(zKat), X(colHi + katHizaUzantiCm), Y(zKat), LayerKesitGorunus);
            }

            if (temelSpans.Count > 0)
            {
                double zTb0 = temelSpans.Min(t => t.z0);
                double zTt0 = temelSpans.Max(t => t.z1);
                double fx0 = colLo - kenarKesitCm;
                double fx1 = colHi + kenarKesitCm;
                Ln(X(fx0), Y(zTb0), X(fx1), Y(zTb0), LayerTemelBeykent);
                Ln(X(fx0), Y(zTt0), X(colLo), Y(zTt0), LayerTemelBeykent);
                Ln(X(colHi), Y(zTt0), X(fx1), Y(zTt0), LayerTemelBeykent);
                Ln(X(fx0), Y(zTb0), X(fx0), Y(zTt0), LayerKesitSiniri);
                Ln(X(fx1), Y(zTb0), X(fx1), Y(zTt0), LayerKesitSiniri);
            }

            var kotZs = new List<double>();
            if (temelSpans.Count > 0)
            {
                kotZs.Add(temelSpans.Min(t => t.z0));
                kotZs.Add(temelSpans.Max(t => t.z1));
            }
            foreach (var st in stories)
            {
                kotZs.Add(st.zBot);
                kotZs.Add(st.zTop);
            }
            DrawPerdeGorunusKots(tr, btr, X(xMax) + Kolon50GorunusDikeyOlcuSagaCm, Y, kotZs);
            double yMax = Y(zMax);
            double zTb = temelSpans.Count > 0 ? temelSpans.Min(t => t.z0) : stories[0].zBot;
            double zTt = temelSpans.Count > 0 ? temelSpans.Max(t => t.z1) : stories[0].zBot;
            var storyTops = stories.Select(st => st.zTop).ToList();
            var kenarZsRight = new List<double>();
            var kenarZsLeft = new List<double>();
            CollectKenarSideZs(beamRuns, wallRuns, colLo, colHi, left: false, kenarZsRight);
            CollectKenarSideZs(beamRuns, wallRuns, colLo, colHi, left: true, kenarZsLeft);
            DrawPerdeGorunusOlculer(
                tr, btr, X(stories[stories.Count - 1].lo), X(stories[stories.Count - 1].hi), Y, zTb, zTt, storyTops, yMax,
                null, temelSpans.Count > 0, kenarZsRight, kenarZsLeft, X(colLo), X(colHi), etriyeZs, etriyeBolgeler);

            for (int si = 0; si < stories.Count; si++)
            {
                var st = stories[si];
                double planCy = Y((st.zBot + st.zTop) * 0.5);
                Geometry overlayPoly = null;
                int overlayFloor = -1;
                if (si + 1 < stories.Count)
                {
                    var up = stories[si + 1];
                    if (KolonKesitPlanFarkli(st.poly, up.poly, st.majorDeg))
                    {
                        overlayPoly = up.poly;
                        overlayFloor = up.floorIndex;
                    }
                }
                double zSofPlan = KolonNetYukseklikUstKot(etriyeKiris, st.lo, st.hi, st.zBot, st.zTop);
                double h16Plan = st.zTop - zSofPlan;
                if (h16Plan < 6.0) h16Plan = 6.0;
                DrawKolonDuseyFloorPlanKesit(
                    tr, btr, st.poly, st.majorDeg,
                    origin.X + Kolon50GorunusCiftOlcuAraCm + maxPlanW + 100.0, planCy,
                    st.floorIndex, col.ColumnNo, overlayPoly, overlayFloor, h16Plan);
            }

            double yName = Y(zMax) - 8.0 * s;
            double xName = origin.X + planBand + 4.0 * s;
            foreach (var c in cols)
            {
                DrawBeamLabel(tr, btr, db, new Point3d(xName, yName, 0),
                    "S" + c.ColumnNo.ToString(CultureInfo.InvariantCulture),
                    10.0 * s, 0.0, LayerKolonIsmi, bottomLeftAligned: true);
                yName -= 14.0 * s;
            }

            return planBand + nameW + kotBand + (xMax - xMin) * s + PerdeGorunusSagTasimCm + KolonDuseyEtriyeOlcuAraCm;
        }

        /// <summary>
        /// TBDY 2018 7.3.3.1: kolon bindirmesi serbest yüksekliğin orta 1/3'ünde, ℓ0 ≥ ℓb (perde 1,50ℓb değil).
        /// 7.3.3.2: kesit değişiminde eğim ≤ 1/6; sağlanamazsa Şekil 7.2 gönye/firkete (1,50ℓb / 40φ, kanca ≥ 12φ).
        /// </summary>
        private void DrawKolonGorunusDuseyDonatilar(
            Transaction tr,
            BlockTableRecord btr,
            ColumnAxisInfo col,
            List<(double zBot, double zTop, Geometry poly, double majorDeg, double lo, double hi, int floorIndex)> stories,
            AffineTransformation rot,
            Func<double, double> X,
            Func<double, double> Y,
            double zDrawBot,
            List<(double x0, double x1, double z0, double z1)> temelSpans,
            List<(double x0, double x1, double zb, double zt)> beamRuns)
        {
            if (tr == null || btr == null || col == null || stories == null || stories.Count == 0 || X == null || Y == null)
                return;
            const double pas = 4.0;
            const double rBend = 2.0;
            const double k90 = 0.41421356237;
            double fck = _rebarFckMPa > 16.0 ? _rebarFckMPa : 30.0;
            double fyk = _rebarFykMPa > 200.0 ? _rebarFykMPa : 420.0;
            bool hasTemel = temelSpans != null && temelSpans.Count > 0;
            double zTemelBot = hasTemel ? temelSpans.Min(t => t.z0) : stories[0].zBot;
            var spliceBot = new double[stories.Count];
            var spliceTop = new double[stories.Count];
            var zNetTops = new double[stories.Count];
            var barXs = new List<double>[stories.Count];
            var barAllPts = new List<Point2d>[stories.Count];
            var diaMm = new int[stories.Count];
            var colTh = new double[stories.Count];

            for (int i = 0; i < stories.Count; i++)
            {
                var st = stories[i];
                barXs[i] = new List<double>();
                barAllPts[i] = new List<Point2d>();
                diaMm[i] = 14;
                colTh[i] = 25.0;
                if (st.poly == null || st.poly.IsEmpty) continue;
                Geometry g;
                try { g = rot != null ? rot.Transform(st.poly) : st.poly; }
                catch { g = st.poly; }
                var e = g.EnvelopeInternal;
                if (IsKolonKesitPoligonKesit(g, e)) continue;
                double longCm = Math.Max(e.Width, e.Height);
                double shortCm = Math.Min(e.Width, e.Height);
                if (shortCm > 1.0 && longCm >= KolonKesitPerdeMinBoyOrani * shortCm - 0.01)
                    continue;
                colTh[i] = Math.Max(8.0, shortCm);
                int dia = 14;
                if (_kolonDuseyGpr != null && _model?.Floors != null &&
                    KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(_kolonDuseyGpr, _model.Floors, st.floorIndex, col.ColumnNo, out _, out string donati, out _) &&
                    !string.IsNullOrWhiteSpace(donati))
                {
                    KolonDonatiTableDrawer.SumKolonKesitDuseyDonatiAdet(donati, out int d);
                    if (d >= 6) dia = d;
                }
                diaMm[i] = dia;
                barXs[i] = GetKolonGorunusLongFaceBarXs(g, st.floorIndex, col.ColumnNo);
                barAllPts[i] = CollectKolonKesitBarPoints(e, st.floorIndex, col.ColumnNo);
                double zCbot = i == 0 ? zDrawBot : st.zBot;
                double zNetTop = KolonNetYukseklikUstKot(beamRuns, st.lo, st.hi, zCbot, st.zTop);
                zNetTops[i] = zNetTop;
                double lb = Ts500KenetlenmeLbCm(dia, fck, fyk);
                double lap = CeilTo5Cm(Math.Max(lb, 30.0));
                KolonOrtUcdeBindirme(zCbot, zNetTop, lap, out spliceBot[i], out spliceTop[i]);
            }

            for (int i = 0; i < stories.Count; i++)
            {
                if (barXs[i] == null || barXs[i].Count == 0) continue;
                bool last = i == stories.Count - 1;
                bool first = i == 0;
                double zCbot = first ? zDrawBot : stories[i].zBot;
                double hookIn = Math.Max(2.0 * rBend + 1.0, colTh[i] - 2.0 * pas);
                double zStart = spliceBot[i];
                int n = barXs[i].Count;
                int dia = diaMm[i];
                double zEndCont = last ? stories[i].zTop : spliceTop[Math.Min(i + 1, stories.Count - 1)];
                if (!last && (barXs[i + 1] == null || barXs[i + 1].Count == 0))
                    zEndCont = stories[i].zTop;
                double midX = 0.5 * (stories[i].lo + stories[i].hi);
                bool nextExists = !last && i + 1 < stories.Count && stories[i + 1].poly != null && !stories[i + 1].poly.IsEmpty;
                double upLo = nextExists ? stories[i + 1].lo : stories[i].lo;
                double upHi = nextExists ? stories[i + 1].hi : stories[i].hi;
                double midHook = nextExists ? 0.5 * (upLo + upHi) : midX;
                double zHorizPre = stories[i].zTop - pas;
                double aJointStory = KolonBirlesimDuseyA(beamRuns, stories[i].lo, stories[i].hi, stories[i].zTop, zHorizPre);
                double zSof = zNetTops[i];
                if (zSof < zStart + 2.0) zSof = zStart + 2.0;
                if (zSof > stories[i].zTop) zSof = stories[i].zTop;
                double h16 = stories[i].zTop - zSof;
                if (h16 < 6.0) h16 = 6.0;
                int[] matchUp = nextExists
                    ? MatchKolonBarsTbdY16(barXs[i], barXs[i + 1], h16)
                    : null;
                for (int b = 0; b < n; b++)
                {
                    double bx = barXs[i][b];
                    double x = X(bx);
                    int upIdx = matchUp != null && b < matchUp.Length ? matchUp[b] : -1;
                    bool matched = nextExists && upIdx >= 0 && barXs[i + 1] != null && upIdx < barXs[i + 1].Count;
                    bool insideUp = nextExists && bx >= upLo + pas && bx <= upHi - pas;
                    if (last || (!matched && !insideUp))
                    {
                        double zHoriz = zHorizPre;
                        if (zHoriz < zStart + 15.0) zHoriz = zStart + 20.0;
                        double hookDir = bx <= midHook ? 1.0 : -1.0;
                        double aJoint = aJointStory;
                        double need = Math.Max(1.50 * Ts500KenetlenmeLbCm(dia, fck, fyk), 40.0 * dia / 10.0);
                        double phi12 = CeilTo5Cm(12.0 * dia / 10.0);
                        double bCap = hookIn > 2.0 * rBend ? hookIn : 2.0 * rBend;
                        if (aJoint + phi12 >= need - 0.01)
                            DrawKolonDonatiGonye(tr, btr, Y, x, zStart, zHoriz, hookDir, k90, rBend, phi12);
                        else if (aJoint + bCap >= need - 0.01)
                        {
                            double bG = CeilTo5Cm(need - aJoint);
                            if (bG < phi12) bG = phi12;
                            if (bG > bCap) bG = bCap;
                            DrawKolonDonatiGonye(tr, btr, Y, x, zStart, zHoriz, hookDir, k90, rBend, bG);
                        }
                        else
                        {
                            double c = CeilTo5Cm(need - aJoint - bCap);
                            if (c < phi12) c = phi12;
                            DrawKolonDonatiFirkete(tr, btr, Y, x, zStart, zHoriz, hookDir, k90, rBend, bCap, c);
                        }
                    }
                    else if (matched)
                    {
                        double upX = barXs[i + 1][upIdx];
                        DrawKolonDonatiBirAltiGecis(tr, btr, X, Y, bx, upX, zStart, zSof, stories[i].zTop, zEndCont);
                    }
                    else
                    {
                        AppendDonatiPline(tr, btr, new[]
                        {
                            new Point2d(x, Y(zStart)),
                            new Point2d(x, Y(zEndCont))
                        }, null);
                    }
                }

                if (nextExists && barAllPts[i + 1] != null && barAllPts[i + 1].Count > 0)
                {
                    var matchedUpX = new List<double>();
                    if (matchUp != null && barXs[i + 1] != null)
                    {
                        for (int b = 0; b < matchUp.Length; b++)
                        {
                            int ui = matchUp[b];
                            if (ui >= 0 && ui < barXs[i + 1].Count)
                                matchedUpX.Add(barXs[i + 1][ui]);
                        }
                    }
                    var loPts = barAllPts[i] ?? new List<Point2d>();
                    var upPts = barAllPts[i + 1];
                    Envelope eUp = null;
                    try
                    {
                        Geometry gUp = rot != null ? rot.Transform(stories[i + 1].poly) : stories[i + 1].poly;
                        if (gUp != null && !gUp.IsEmpty) eUp = gUp.EnvelopeInternal;
                    }
                    catch { }
                    double yTop = 0, yBot = 0;
                    bool hasLongY = TryKolonKesitLongFaceY(eUp, out yTop, out yBot);
                    const double tol = 2.5;
                    var filizXs = new List<double>();
                    foreach (var u in upPts)
                    {
                        bool match = false;
                        foreach (var lo in loPts)
                        {
                            double dx = lo.X - u.X, dy = lo.Y - u.Y;
                            if (dx * dx + dy * dy <= tol * tol) { match = true; break; }
                        }
                        if (!match && hasLongY && (Math.Abs(u.Y - yTop) < tol || Math.Abs(u.Y - yBot) < tol))
                        {
                            foreach (double mx in matchedUpX)
                            {
                                if (Math.Abs(u.X - mx) < tol) { match = true; break; }
                            }
                        }
                        if (match) continue;
                        bool have = false;
                        foreach (double fx in filizXs)
                        {
                            if (Math.Abs(fx - u.X) < 1.0) { have = true; break; }
                        }
                        if (!have) filizXs.Add(u.X);
                    }
                    int diaU = diaMm[i + 1] >= 6 ? diaMm[i + 1] : dia;
                    double lbU = Ts500KenetlenmeLbCm(diaU, fck, fyk);
                    double eFiliz = CeilTo5Cm(Math.Max(1.50 * lbU, 40.0 * diaU / 10.0));
                    double lbUp = CeilTo5Cm(lbU);
                    double zFilizBot = stories[i].zTop - eFiliz;
                    if (zFilizBot < zCbot + 5.0) zFilizBot = zCbot + 5.0;
                    double zFilizTop = spliceBot[i + 1] + lbUp;
                    if (zFilizTop < stories[i].zTop + lbUp)
                        zFilizTop = stories[i].zTop + lbUp;
                    if (filizXs.Count > 0 && zFilizTop - zFilizBot >= 10.0)
                    {
                        foreach (double fx in filizXs)
                        {
                            double x = X(fx);
                            AppendDonatiPline(tr, btr, new[]
                            {
                                new Point2d(x, Y(zFilizBot)),
                                new Point2d(x, Y(zFilizTop))
                            }, null);
                        }
                    }
                }

                if (first && hasTemel)
                {
                    double bHook = CeilTo5Cm(12.0 * dia / 10.0);
                    double lbk = 0.75 * Ts500KenetlenmeLbCm(dia, fck, fyk);
                    double a = zCbot - (zTemelBot + 5.0);
                    if (a + bHook < lbk) bHook = CeilTo5Cm(Math.Max(bHook, lbk - Math.Max(a, 0)));
                    double zFilizBot = zTemelBot + 5.0;
                    double zFilizTop = spliceTop[i];
                    if (zFilizTop - zFilizBot < 10.0) continue;
                    for (int b = 0; b < n; b++)
                    {
                        double bx = barXs[i][b];
                        double x = X(bx);
                        double hookDir = bx <= midX ? 1.0 : -1.0;
                        double bulge = hookDir > 0 ? -k90 : k90;
                        AppendDonatiPline(tr, btr, new[]
                        {
                            new Point2d(x + hookDir * bHook, Y(zFilizBot)),
                            new Point2d(x + hookDir * rBend, Y(zFilizBot)),
                            new Point2d(x, Y(zFilizBot + rBend)),
                            new Point2d(x, Y(zFilizTop))
                        }, new[] { 0.0, bulge, 0.0, 0.0 });
                    }
                }
            }
        }

        /// <summary>
        /// TBDY 2018 7.3.3.2: kesit değişiminde boyuna donatı eğimi 1/6’dan dik olamaz.
        /// Eşleşme: |Δx| ≤ h/6; h = kiriş yüksekliği (oba → kat üstü). Daha yatık eğim serbest.
        /// </summary>
        private static int[] MatchKolonBarsTbdY16(List<double> lower, List<double> upper, double hAvail)
        {
            int nL = lower == null ? 0 : lower.Count;
            var match = new int[nL];
            for (int i = 0; i < nL; i++) match[i] = -1;
            if (nL == 0 || upper == null || upper.Count == 0) return match;
            double dMax = Math.Max(hAvail, 6.0) / 6.0;
            var usedU = new bool[upper.Count];
            var usedL = new bool[nL];
            var cand = new List<(int l, int u, double d)>(nL * upper.Count);
            for (int l = 0; l < nL; l++)
            {
                for (int u = 0; u < upper.Count; u++)
                    cand.Add((l, u, Math.Abs(lower[l] - upper[u])));
            }
            cand.Sort((a, b) => a.d.CompareTo(b.d));
            foreach (var p in cand)
            {
                if (usedL[p.l] || usedU[p.u]) continue;
                if (p.d > dMax + 0.05) continue;
                usedL[p.l] = true;
                usedU[p.u] = true;
                match[p.l] = p.u;
            }
            return match;
        }

        /// <summary>Eğim yalnız kiriş yüksekliğinde (oba→üst); bitiş X üst kat düşey donatısı.</summary>
        private void DrawKolonDonatiBirAltiGecis(
            Transaction tr, BlockTableRecord btr,
            Func<double, double> X, Func<double, double> Y,
            double xLo, double xUp, double zStart, double zSoffit, double zBeamTop, double zEndCont)
        {
            if (X == null || Y == null) return;
            double x0 = X(xLo);
            double x1 = X(xUp);
            double zSof = zSoffit;
            if (zSof < zStart + 1.0) zSof = zStart + 1.0;
            double zTop = zBeamTop;
            if (zTop < zSof + 1.0) zTop = zSof + 1.0;
            double zEnd = Math.Max(zEndCont, zTop);
            var pts = new List<Point2d> { new Point2d(x0, Y(zStart)) };
            if (zSof > zStart + 0.5)
                pts.Add(new Point2d(x0, Y(zSof)));
            pts.Add(new Point2d(x1, Y(zTop)));
            if (zEnd > zTop + 0.5)
                pts.Add(new Point2d(x1, Y(zEnd)));
            AppendDonatiPline(tr, btr, pts.ToArray(), null);
        }

        /// <summary>
        /// TBDY 2018 Şekil 7.2 sol: gönye. a yetiyorsa b = 12φ; değilse b artar (kısa kenar−2·paspayı üst sınırı çağıranda).
        /// </summary>
        private void DrawKolonDonatiGonye(
            Transaction tr, BlockTableRecord btr,
            Func<double, double> Y,
            double x, double zStart, double zHoriz, double hookDir,
            double k90, double rBend, double bCm)
        {
            if (Y == null) return;
            double b = Math.Max(bCm, 2.0 * rBend);
            double bulge = hookDir > 0 ? -k90 : k90;
            AppendDonatiPline(tr, btr, new[]
            {
                new Point2d(x, Y(zStart)),
                new Point2d(x, Y(zHoriz - rBend)),
                new Point2d(x + hookDir * rBend, Y(zHoriz)),
                new Point2d(x + hookDir * b, Y(zHoriz))
            }, new[] { 0.0, bulge, 0.0, 0.0 });
        }

        /// <summary>
        /// TBDY 2018 Şekil 7.2 sağ: firkete yalnız a+b yetmezse. b = kısa kenar−2·paspayı zorunlu; c yokken çizilmez.
        /// </summary>
        private void DrawKolonDonatiFirkete(
            Transaction tr, BlockTableRecord btr,
            Func<double, double> Y,
            double x, double zStart, double zHoriz, double hookDir,
            double k90, double rBend, double bCm, double cCm)
        {
            if (Y == null) return;
            double b = Math.Max(bCm, 2.0 * rBend);
            double c = Math.Max(cCm, 2.0 * rBend);
            double bulge = hookDir > 0 ? -k90 : k90;
            AppendDonatiPline(tr, btr, new[]
            {
                new Point2d(x, Y(zStart)),
                new Point2d(x, Y(zHoriz - rBend)),
                new Point2d(x + hookDir * rBend, Y(zHoriz)),
                new Point2d(x + hookDir * (b - rBend), Y(zHoriz)),
                new Point2d(x + hookDir * b, Y(zHoriz - rBend)),
                new Point2d(x + hookDir * b, Y(zHoriz - c))
            }, new[] { 0.0, bulge, 0.0, bulge, 0.0, 0.0 });
        }

        private List<double> GetKolonGorunusLongFaceBarXs(Geometry gRot, int floorIndex, int colNo)
        {
            var empty = new List<double>();
            if (gRot == null || gRot.IsEmpty) return empty;
            var e = gRot.EnvelopeInternal;
            double rad = KolonKesitEtriyeRadiusCm;
            double inset = KolonKesitPaspayiCm;
            double x0 = e.MinX + inset, y0 = e.MinY + inset, x1 = e.MaxX - inset, y1 = e.MaxY - inset;
            if (x1 - x0 < 2.0 * rad + 2.0 || y1 - y0 < 2.0 * rad + 2.0) return empty;
            ResolveKolonKesitBarCounts(x0, y0, x1, y1, rad, floorIndex, colNo,
                out var cTl, out var cTr, out _, out _,
                out int top, out int bot, out _, out _);
            return CollectEdgeCoords(cTl, cTr, Math.Max(top, bot), horizontal: true);
        }

        /// <summary>
        /// TBDY 7.3.3.1: net yükseklik = kolon alt kotu → bağlı kirişlerin en alt (oba) kotu.
        /// Bindirme orta 1/3 içinde mümkün olduğunca aşağı; başlangıç/bitiş 5 cm katı.
        /// </summary>
        private static void KolonOrtUcdeBindirme(double zBot, double zNetTop, double lap, out double zSpliceBot, out double zSpliceTop)
        {
            double hn = Math.Max(zNetTop - zBot, 1.0);
            double zoneLo = hn / 3.0;
            double zoneHi = 2.0 * hn / 3.0;
            double L = CeilTo5Cm(Math.Max(lap, 5.0));
            double zoneW5 = FloorTo5Cm(zoneHi - zoneLo);
            if (L > zoneW5 && zoneW5 >= 5.0)
                L = zoneW5;
            double offBot = CeilTo5Cm(zoneLo);
            double offTop = offBot + L;
            double offHi = FloorTo5Cm(zoneHi);
            if (offTop > offHi + 0.05 && offHi - L >= CeilTo5Cm(zoneLo) - 0.05)
            {
                offBot = FloorTo5Cm(offHi - L);
                if (offBot < CeilTo5Cm(zoneLo) - 0.05)
                    offBot = CeilTo5Cm(zoneLo);
                offTop = offBot + L;
            }
            zSpliceBot = zBot + offBot;
            zSpliceTop = zBot + offTop;
        }

        /// <summary>Kolona bağlanan kirişlerin bu kattaki en düşük oba kotu; yoksa kolon üstü.</summary>
        private static double KolonNetYukseklikUstKot(
            List<(double x0, double x1, double zb, double zt)> beamRuns,
            double colLo, double colHi, double zColBot, double zColTop)
        {
            double best = double.NaN;
            if (beamRuns != null)
            {
                foreach (var b in beamRuns)
                {
                    if (Math.Max(b.x0, b.x1) < colLo - 6.0 || Math.Min(b.x0, b.x1) > colHi + 6.0)
                        continue;
                    if (b.zb < zColBot + 30.0 || b.zb > zColTop + 25.0)
                        continue;
                    if (double.IsNaN(best) || b.zb < best)
                        best = b.zb;
                }
            }
            return double.IsNaN(best) ? zColTop : best;
        }

        /// <summary>Kolona saplanan kirişlerden obası en yüksek olanın alt kotu; yoksa kolon üstü.</summary>
        private static double KolonKirisEnYuksekObaKot(
            List<(double x0, double x1, double zb, double zt)> beamRuns,
            double colLo, double colHi, double zColBot, double zColTop)
        {
            double best = double.NaN;
            if (beamRuns != null)
            {
                foreach (var b in beamRuns)
                {
                    if (Math.Max(b.x0, b.x1) < colLo - 6.0 || Math.Min(b.x0, b.x1) > colHi + 6.0)
                        continue;
                    if (b.zb < zColBot + 30.0 || b.zb > zColTop + 25.0)
                        continue;
                    if (double.IsNaN(best) || b.zb > best)
                        best = b.zb;
                }
            }
            return double.IsNaN(best) ? zColTop : best;
        }

        /// <summary>
        /// Şekil 7.2 a: kanca kotu ile kiriş obası arası düşey. Kiriş yoksa 0 (o zaman b veya a+b+c tamamlar).
        /// </summary>
        private static double KolonBirlesimDuseyA(
            List<(double x0, double x1, double zb, double zt)> beamRuns,
            double colLo, double colHi, double zColTop, double zHoriz)
        {
            double soffit = double.NaN;
            if (beamRuns != null)
            {
                foreach (var b in beamRuns)
                {
                    if (b.zt < zColTop - 30.0 || b.zb > zColTop + 15.0)
                        continue;
                    if (Math.Max(b.x0, b.x1) < colLo - 120.0 || Math.Min(b.x0, b.x1) > colHi + 120.0)
                        continue;
                    if (double.IsNaN(soffit) || b.zb < soffit)
                        soffit = b.zb;
                }
            }
            if (double.IsNaN(soffit))
                return 0.0;
            return Math.Max(0.0, zHoriz - soffit);
        }

        private void DrawKolonDuseyFloorPlanKesit(
            Transaction tr,
            BlockTableRecord btr,
            Geometry poly,
            double majorAngleDeg,
            double planRightX,
            double targetCy,
            int floorIndex,
            int colNo,
            Geometry overlayPoly = null,
            int overlayFloorIndex = -1,
            double h16Cm = 30.0)
        {
            if (poly == null || poly.IsEmpty || tr == null || btr == null) return;
            var c = poly.Centroid;
            Geometry g;
            try
            {
                var rot = AffineTransformation.RotationInstance(-majorAngleDeg * Math.PI / 180.0, c.X, c.Y);
                g = rot.Transform(poly);
            }
            catch { g = poly; }
            if (g == null || g.IsEmpty) return;
            var e0 = g.EnvelopeInternal;
            double targetCx = planRightX - e0.Width * 0.5;
            var cc = g.Centroid;
            AffineTransformation moveT = null;
            try
            {
                moveT = AffineTransformation.TranslationInstance(targetCx - cc.X, targetCy - cc.Y);
                g = moveT.Transform(g);
            }
            catch { return; }
            var e = g.EnvelopeInternal;
            if (e.Width < 2.0 && e.Height < 2.0) return;
            double longCm = Math.Max(e.Width, e.Height);
            double shortCm = Math.Min(e.Width, e.Height);
            bool isPerdeKesit = shortCm > 1.0 && longCm >= KolonKesitPerdeMinBoyOrani * shortCm - 0.01;
            bool isPerdeBasligi = !isPerdeKesit
                && shortCm > 1.0
                && shortCm < KolonKesitPerdeBasligiMaxKenarCm - 0.01
                && longCm < KolonKesitPerdeMinBoyOrani * shortCm - 0.01;
            DrawGeometryRingsAsPolylines(tr, btr, g, (isPerdeBasligi || isPerdeKesit) ? LayerPerde : LayerKolon, addHatch: false, applySmallTriangleTrim: false);
            if (!isPerdeKesit && !IsKolonKesitPoligonKesit(g, e))
            {
                DrawKolonKesitEtriye(tr, btr, g, e, floorIndex, colNo, skipIkinciEtriye: isPerdeBasligi);
            }

            if (overlayPoly != null && !overlayPoly.IsEmpty && moveT != null)
                DrawKolonKesitIzdusumOverlay(tr, btr, overlayPoly, c.X, c.Y, majorAngleDeg, moveT, overlayFloorIndex, colNo, e, floorIndex, h16Cm);

            if (!isPerdeKesit && !IsKolonKesitPoligonKesit(g, e))
                DrawKolonKesitDuseyDonatiYazisi(tr, btr, e, floorIndex, colNo, isPerdeBasligi);

            ObjectId dimId = GetOrCreatePlanOlcuDimStyle(tr, btr.Database, 10.0, 1.0, PlanOlcuDonatiDimStyleName);
            double off = Kolon50GorunusCiftOlcuAraCm;
            void Dim(Point3d a, Point3d b, Point3d linePt)
            {
                var dim = new AlignedDimension(a, b, linePt, "", dimId)
                {
                    Layer = LayerOlcu,
                    LineWeight = LineWeight.LineWeight020
                };
                try { dim.DimfxlenOn = true; } catch { }
                try { dim.Dimfxlen = off; } catch { }
                AppendEntity(tr, btr, dim);
            }
            Dim(new Point3d(e.MinX, e.MinY, 0), new Point3d(e.MaxX, e.MinY, 0),
                new Point3d((e.MinX + e.MaxX) * 0.5, e.MinY - off, 0));
            Dim(new Point3d(e.MinX, e.MinY, 0), new Point3d(e.MinX, e.MaxY, 0),
                new Point3d(e.MinX - off, (e.MinY + e.MaxY) * 0.5, 0));
        }

        /// <summary>Kesit değişiminde üst (küçülen) kolon kesiti + donatı, alt kesit üzerine IZDUSUM.</summary>
        private void DrawKolonKesitIzdusumOverlay(
            Transaction tr, BlockTableRecord btr,
            Geometry overlayPoly, double rotOx, double rotOy, double majorAngleDeg,
            AffineTransformation moveT, int overlayFloorIndex, int colNo,
            Envelope eLower, int lowerFloorIndex, double h16Cm)
        {
            Geometry gUp;
            try
            {
                var rot = AffineTransformation.RotationInstance(-majorAngleDeg * Math.PI / 180.0, rotOx, rotOy);
                gUp = rot.Transform(overlayPoly);
                gUp = moveT.Transform(gUp);
            }
            catch { return; }
            if (gUp == null || gUp.IsEmpty) return;
            var eUp = gUp.EnvelopeInternal;
            if (eUp.Width < 2.0 && eUp.Height < 2.0) return;
            double longCm = Math.Max(eUp.Width, eUp.Height);
            double shortCm = Math.Min(eUp.Width, eUp.Height);
            bool isPerdeKesit = shortCm > 1.0 && longCm >= KolonKesitPerdeMinBoyOrani * shortCm - 0.01;
            bool isPerdeBasligi = !isPerdeKesit
                && shortCm > 1.0
                && shortCm < KolonKesitPerdeBasligiMaxKenarCm - 0.01
                && longCm < KolonKesitPerdeMinBoyOrani * shortCm - 0.01;
            _kolonKesitLayerOverride = LayerIzdusum;
            try
            {
                DrawGeometryRingsAsPolylines(tr, btr, gUp, LayerIzdusum, addHatch: false, applySmallTriangleTrim: false);
                if (!isPerdeKesit && !IsKolonKesitPoligonKesit(gUp, eUp))
                    DrawKolonKesitEtriye(tr, btr, gUp, eUp, overlayFloorIndex, colNo, skipIkinciEtriye: isPerdeBasligi);
            }
            finally
            {
                _kolonKesitLayerOverride = null;
            }
            if (!isPerdeKesit && !IsKolonKesitPoligonKesit(gUp, eUp))
                DrawKolonKesitIzdusumEslesmeCemberleri(tr, btr, eLower, lowerFloorIndex, eUp, overlayFloorIndex, colNo, h16Cm);
        }

        private void DrawKolonKesitIzdusumEslesmeCemberleri(
            Transaction tr, BlockTableRecord btr,
            Envelope eLower, int lowerFloorIndex,
            Envelope eUp, int overlayFloorIndex, int colNo, double h16Cm)
        {
            var upPts = CollectKolonKesitBarPoints(eUp, overlayFloorIndex, colNo);
            if (upPts.Count == 0) return;
            var loPts = CollectKolonKesitBarPoints(eLower, lowerFloorIndex, colNo);
            var loXs = CollectKolonKesitLongFaceXs(eLower, lowerFloorIndex, colNo);
            var upXs = CollectKolonKesitLongFaceXs(eUp, overlayFloorIndex, colNo);
            int[] match16 = MatchKolonBarsTbdY16(loXs, upXs, h16Cm);
            var matchedUpX = new List<double>();
            if (match16 != null && loXs != null && upXs != null)
            {
                for (int i = 0; i < match16.Length && i < loXs.Count; i++)
                {
                    int u = match16[i];
                    if (u >= 0 && u < upXs.Count)
                        matchedUpX.Add(upXs[u]);
                }
            }
            const double tol = 2.5;
            double yTop = 0, yBot = 0;
            bool hasLongY = TryKolonKesitLongFaceY(eUp, out yTop, out yBot);
            foreach (var u in upPts)
            {
                bool match = false;
                foreach (var lo in loPts)
                {
                    double dx = lo.X - u.X;
                    double dy = lo.Y - u.Y;
                    if (dx * dx + dy * dy <= tol * tol)
                    {
                        match = true;
                        break;
                    }
                }
                if (!match && hasLongY && (Math.Abs(u.Y - yTop) < tol || Math.Abs(u.Y - yBot) < tol))
                {
                    foreach (double mx in matchedUpX)
                    {
                        if (Math.Abs(u.X - mx) < tol)
                        {
                            match = true;
                            break;
                        }
                    }
                }
                DrawKolonKesitIzdusumMarkCircle(tr, btr, u.X, u.Y, match ? (short)5 : (short)1);
            }
        }

        private void DrawKolonKesitIzdusumMarkCircle(Transaction tr, BlockTableRecord btr, double cx, double cy, short aci)
        {
            if (tr == null || btr == null) return;
            var circ = new Circle(new Point3d(cx, cy, 0), Vector3d.ZAxis, 2.0);
            circ.SetDatabaseDefaults();
            circ.Layer = LayerIzdusum;
            circ.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
            circ.LineWeight = LineWeight.LineWeight020;
            AppendEntity(tr, btr, circ);
        }

        private List<Point2d> CollectKolonKesitBarPoints(Envelope e, int floorIndex, int colNo)
        {
            var pts = new List<Point2d>();
            if (e == null) return pts;
            double rad = KolonKesitEtriyeRadiusCm;
            double inset = KolonKesitPaspayiCm;
            double x0 = e.MinX + inset, y0 = e.MinY + inset, x1 = e.MaxX - inset, y1 = e.MaxY - inset;
            if (x1 - x0 < 2.0 * rad + 2.0 || y1 - y0 < 2.0 * rad + 2.0) return pts;
            ResolveKolonKesitBarCounts(x0, y0, x1, y1, rad, floorIndex, colNo,
                out var cTl, out var cTr, out var cBr, out var cBl,
                out int top, out int bot, out int left, out int right);
            pts.Add(cTl);
            pts.Add(cTr);
            pts.Add(cBr);
            pts.Add(cBl);
            AddKolonKesitEdgeExtras(pts, cTl, cTr, top);
            AddKolonKesitEdgeExtras(pts, cBl, cBr, bot);
            AddKolonKesitEdgeExtras(pts, cBl, cTl, left);
            AddKolonKesitEdgeExtras(pts, cBr, cTr, right);
            return pts;
        }

        private List<double> CollectKolonKesitLongFaceXs(Envelope e, int floorIndex, int colNo)
        {
            var empty = new List<double>();
            if (e == null) return empty;
            double rad = KolonKesitEtriyeRadiusCm;
            double inset = KolonKesitPaspayiCm;
            double x0 = e.MinX + inset, y0 = e.MinY + inset, x1 = e.MaxX - inset, y1 = e.MaxY - inset;
            if (x1 - x0 < 2.0 * rad + 2.0 || y1 - y0 < 2.0 * rad + 2.0) return empty;
            ResolveKolonKesitBarCounts(x0, y0, x1, y1, rad, floorIndex, colNo,
                out var cTl, out var cTr, out _, out _,
                out int top, out int bot, out _, out _);
            return CollectEdgeCoords(cTl, cTr, Math.Max(top, bot), horizontal: true);
        }

        private static bool TryKolonKesitLongFaceY(Envelope e, out double yTop, out double yBot)
        {
            yTop = yBot = 0;
            if (e == null) return false;
            double rad = KolonKesitEtriyeRadiusCm;
            double inset = KolonKesitPaspayiCm;
            yBot = e.MinY + inset + rad;
            yTop = e.MaxY - inset - rad;
            return yTop - yBot > 1.0;
        }

        private static void AddKolonKesitEdgeExtras(List<Point2d> pts, Point2d a, Point2d b, int extras)
        {
            if (pts == null || extras <= 0) return;
            for (int i = 1; i <= extras; i++)
            {
                double t = i / (double)(extras + 1);
                pts.Add(new Point2d(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y)));
            }
        }

        private static bool KolonKesitPlanFarkli(Geometry a, Geometry b, double majorDeg)
        {
            if (a == null || b == null || a.IsEmpty || b.IsEmpty) return false;
            var c = a.Centroid;
            Geometry ga, gb;
            try
            {
                var rot = AffineTransformation.RotationInstance(-majorDeg * Math.PI / 180.0, c.X, c.Y);
                ga = rot.Transform(a);
                gb = rot.Transform(b);
            }
            catch { return true; }
            var ea = ga.EnvelopeInternal;
            var eb = gb.EnvelopeInternal;
            const double t = 2.0;
            return Math.Abs(ea.Width - eb.Width) > t
                || Math.Abs(ea.Height - eb.Height) > t
                || Math.Abs(ea.MinX - eb.MinX) > t
                || Math.Abs(ea.MinY - eb.MinY) > t;
        }

        private const double KolonKesitPerdeBasligiMaxKenarCm = 30.0;
        private const double KolonKesitPerdeMinBoyOrani = 6.0;
        private const double KolonKesitPaspayiCm = 4.0;
        private const double KolonKesitEtriyeRadiusCm = 1.5;
        private const double KolonKesitDuseyDonatiRadiusCm = 0.75;
        private const double KolonKesitDuseyDonatiCemberWidthCm = 1.5;
        private const double KolonKesitIkinciEtriyeMinUzunCm = 60.0;
        private const double KolonKesitIkinciEtriyeBirUcCm = 120.0;
        private const double KolonKesitCirozOfsetCm = 0.5;
        private const double KolonKesitCirozRadiusCm = KolonKesitEtriyeRadiusCm + KolonKesitCirozOfsetCm;
        private const double KolonKesitCirozHook90CizimCm = 5.0;
        private const double KolonKesitCirozAMaxFi = 25.0;
        private const double KolonKesitCirozAMaxFiKare = 23.0;

        /// <summary>L / T / çokgen kolon: kesit donatısı sonra tariflenecek.</summary>
        private static bool IsKolonKesitPoligonKesit(Geometry g, Envelope e)
        {
            Polygon p = null;
            if (g is Polygon gp)
                p = gp;
            else if (g is GeometryCollection gc)
            {
                double best = 0;
                for (int i = 0; i < gc.NumGeometries; i++)
                {
                    if (gc.GetGeometryN(i) is Polygon pi && !pi.IsEmpty && pi.Area > best)
                    {
                        p = pi;
                        best = pi.Area;
                    }
                }
            }
            if (p == null || p.IsEmpty) return false;
            if (Deneme1ExteriorVertexCountExcludingClosure(p) > 4)
                return true;
            if (e == null) return false;
            double envA = e.Width * e.Height;
            if (envA < 1.0) return false;
            double r = 0.5 * Math.Min(e.Width, e.Height);
            double circA = Math.PI * r * r;
            if (circA > 1.0 && Math.Abs(p.Area - circA) < 0.15 * circA)
                return false;
            return p.Area < 0.88 * envA;
        }

        /// <summary>TBDY 2018 7.2.8 / TS 500: 135° kanca uzantısı ≥ 6φ ve ≥ 60 mm.</summary>
        private static double TbdY2018EtriyeHookExtCm(int diaMm)
        {
            if (diaMm < 6) diaMm = 8;
            return Math.Max(6.0, 6.0 * diaMm / 10.0);
        }

        private static bool TryParseEtriyeDiaMm(string etriye, out int diaMm)
        {
            diaMm = 8;
            if (string.IsNullOrWhiteSpace(etriye)) return false;
            var m = Regex.Match(etriye, @"[\u00F8\u00D8ØøφΦ]\s*(\d{1,2})");
            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d) && d >= 6 && d <= 20)
            {
                diaMm = d;
                return true;
            }
            return false;
        }

        /// <summary>GPR ø8/20/9: iki sayı → büyük ara bölge, küçük sıklaştırma. Tek sayı (ø8/8) → mil boyunca aynı (kiriş içi hariç).</summary>
        private static void ParseKolonEtriyeAralikCm(string etriye, out double sMid, out double sConf, out bool tekAralik)
        {
            sMid = 20.0;
            sConf = 10.0;
            tekAralik = false;
            if (string.IsNullOrWhiteSpace(etriye)) return;
            var nums = new List<double>();
            foreach (Match m in Regex.Matches(etriye, @"/(\d{1,2})"))
            {
                if (int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v >= 4 && v <= 30)
                    nums.Add(v);
            }
            if (nums.Count == 1)
            {
                tekAralik = true;
                sConf = sMid = nums[0];
            }
            else if (nums.Count >= 2)
            {
                sMid = Math.Max(nums[0], nums[1]);
                sConf = Math.Min(nums[0], nums[1]);
            }
        }

        /// <summary>TBDY 2018 Şekil 7.3 / 7.3.4.1: sarılma s ≤ min(150 mm, bmin/3, 6φℓ), s ≥ 50 mm.</summary>
        private static double TbdY2018KolonSarilmaSMaxCm(double bMinCm, int diaLongMm)
        {
            double phiCm = Math.Max(diaLongMm, 6) / 10.0;
            double s = 15.0;
            if (bMinCm > 1.0) s = Math.Min(s, bMinCm / 3.0);
            s = Math.Min(s, 6.0 * phiCm);
            if (s < 5.0) s = 5.0;
            return s;
        }

        /// <summary>
        /// Sabit aralık Şekil 7.3 sarılmayı aşarsa sıklaştırmayı ekler (ø10/10 → ø10/10/8).
        /// Perde başlığı yazıları değiştirilmez.
        /// </summary>
        private static string FormatKolonEtriyeYazisiTbdY(
            string etRaw, double bMinCm, int diaLongMm, bool skipSiklastirma, out bool tekAralikCizim)
        {
            ParseKolonEtriyeAralikCm(etRaw, out double sMid, out _, out bool tek);
            string lab = KolonDonatiTableDrawer.FormatEtriyeForTableDisplay(etRaw);
            tekAralikCizim = tek;
            if (skipSiklastirma || !tek || string.IsNullOrWhiteSpace(lab)) return lab;
            double sMax = TbdY2018KolonSarilmaSMaxCm(bMinCm, diaLongMm);
            if (sMid <= sMax + 0.05) return lab;
            int sConf = (int)Math.Floor(sMax + 1e-6);
            if (sConf < 5) sConf = 5;
            if (sConf >= (int)Math.Round(sMid)) return lab;
            tekAralikCizim = false;
            return lab + "/" + sConf.ToString(CultureInfo.InvariantCulture);
        }

        /// TBDY 2018 Şekil 7.3: kolon-kiriş birleşiminde sj ≤ 100 mm (sarılma 6φℓ değil).
        private const int TbdY2018KolonBirlesimSjCm = 10;

        /// <summary>
        /// Dilim boyu L, kod aralığı sCode: adet = ceil(L/s), kopya aralığı L/adet (≥5 cm).
        /// Ör. 40/15 → 3 adet, 13.3 cm. Etiket sCode kalır.
        /// </summary>
        private static bool TryKolonEtriyeAdetAralik(double L, int sCode, out int adet, out double sAct)
        {
            adet = 0;
            sAct = 0;
            if (L < 2.0 || sCode < 1) return false;
            int n = (int)Math.Ceiling(L / sCode - 1e-9);
            if (n < 1) n = 1;
            int nMax = (int)Math.Floor(L / 5.0 + 1e-9);
            if (nMax < 1) return false;
            if (n > nMax) n = nMax;
            adet = n;
            sAct = L / n;
            return sAct >= 4.99;
        }

        private static string FormatKolonEtriyeOlcuEtiket(int adet, int diaMm, int sCode)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}\u00F8{1}/{2}", adet, diaMm, sCode);
        }

        private static int KolonEtriyeBolgeSMin(
            IEnumerable<(double zLo, double zHi, int sCm, int diaMm)> bolgeler,
            double za,
            double zb,
            out int diaMm)
        {
            diaMm = 8;
            int sBest = int.MaxValue;
            if (bolgeler == null) return 8;
            if (zb < za) { double t = za; za = zb; zb = t; }
            foreach (var b in bolgeler)
            {
                double lo = Math.Max(za, b.zLo);
                double hi = Math.Min(zb, b.zHi);
                if (hi - lo < 1.0) continue;
                if (b.sCm < sBest)
                {
                    sBest = b.sCm;
                    if (b.diaMm > 0) diaMm = b.diaMm;
                }
            }
            return sBest >= 100 ? 8 : sBest;
        }

        /// <summary>
        /// TBDY Şekil 7.3 bindirme (ℓb) etriyesi: sc ≤ min(150 mm, bmin/3).
        /// </summary>
        private static double TbdY2018KolonLbEtriyeScCm(double bMinCm)
        {
            double s = 15.0;
            if (bMinCm > 1.0) s = Math.Min(s, bMinCm / 3.0);
            if (s < 5.0) s = 5.0;
            return s;
        }

        /// <summary>
        /// TBDY 2018 Şekil 7.3 / 7.3.4: sarılma ℓo ≥ max(1,5 bmax, ℓn/6, 50 cm);
        /// sarılma s ≤ 15 cm, 8φ, bmin/3; orta s0 ≤ 20 cm, bmin/2; birleşim üst s ≤ 10 cm.
        /// İki sarılma arası > 100 cm ise ℓb sınırlarına da etriye; ≤ 100 cm orta bölge şimdilik boş.
        /// </summary>
        private void DrawKolonGorunusEtriyeler(
            Transaction tr,
            BlockTableRecord btr,
            ColumnAxisInfo col,
            List<(double zBot, double zTop, Geometry poly, double majorDeg, double lo, double hi, int floorIndex)> stories,
            AffineTransformation rot,
            Func<double, double> X,
            Func<double, double> Y,
            double zDrawBot,
            List<(double x0, double x1, double zb, double zt)> beamRuns,
            Action<double, double, double, double, string> Ln,
            List<(double x0, double x1, double z0, double z1)> temelSpans = null,
            List<double> etriyeZs = null,
            List<(double zLo, double zHi, int sCm, int diaMm)> etriyeBolgeler = null)
        {
            if (tr == null || btr == null || col == null || stories == null || X == null || Y == null || Ln == null)
                return;
            for (int i = 0; i < stories.Count; i++)
            {
                var st = stories[i];
                if (st.poly == null || st.poly.IsEmpty) continue;
                Geometry g;
                try { g = rot != null ? rot.Transform(st.poly) : st.poly; }
                catch { g = st.poly; }
                var e = g.EnvelopeInternal;
                if (IsKolonKesitPoligonKesit(g, e)) continue;
                double longCm = Math.Max(e.Width, e.Height);
                double shortCm = Math.Min(e.Width, e.Height);
                if (shortCm > 1.0 && longCm >= KolonKesitPerdeMinBoyOrani * shortCm - 0.01)
                    continue;
                bool isPerdeBasligi = shortCm > 1.0
                    && shortCm < KolonKesitPerdeBasligiMaxKenarCm - 0.01
                    && longCm < KolonKesitPerdeMinBoyOrani * shortCm - 0.01;
                double bMax = Math.Max(st.hi - st.lo, shortCm);
                int diaLong = 14;
                string etRaw = null;
                if (_kolonDuseyGpr != null && _model?.Floors != null &&
                    KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(_kolonDuseyGpr, _model.Floors, st.floorIndex, col.ColumnNo, out _, out string donati, out string etriye))
                {
                    etRaw = etriye;
                    KolonDonatiTableDrawer.SumKolonKesitDuseyDonatiAdet(donati, out int d);
                    if (d >= 6) diaLong = d;
                }
                ParseKolonEtriyeAralikCm(etRaw, out double sMid, out double sGprConf, out bool tekAralikGpr);
                FormatKolonEtriyeYazisiTbdY(etRaw, shortCm, diaLong, isPerdeBasligi, out bool tekAralik);
                double sSik = TbdY2018KolonSarilmaSMaxCm(shortCm, diaLong);
                if (!tekAralikGpr) sSik = Math.Min(sSik, sGprConf);
                int sSikCm = Math.Max(5, (int)Math.Floor(sSik + 1e-6));
                int s0MaxCm = Math.Max(5, (int)Math.Floor(Math.Min(20.0, shortCm * 0.5) + 1e-6));
                int sMidCm = Math.Max(5, (int)Math.Round(sMid));
                if (sMidCm > s0MaxCm) sMidCm = s0MaxCm;
                int sLbCm = Math.Min(sMidCm, Math.Max(5, (int)Math.Floor(TbdY2018KolonLbEtriyeScCm(shortCm) + 1e-6)));
                int diaEt = 10;
                TryParseEtriyeDiaMm(etRaw, out diaEt);

                double z0 = i == 0 ? zDrawBot : st.zBot;
                double zSoff = KolonNetYukseklikUstKot(beamRuns, st.lo, st.hi, z0, st.zTop);
                if (zSoff > st.zTop) zSoff = st.zTop;
                if (zSoff < z0 + 10.0) zSoff = st.zTop;
                double zEtTop = KolonKirisEnYuksekObaKot(beamRuns, st.lo, st.hi, z0, st.zTop);
                if (zEtTop > st.zTop) zEtTop = st.zTop;
                if (zEtTop < z0 + 10.0) zEtTop = zSoff;
                if (zEtTop < zSoff) zEtTop = zSoff;
                double hn = Math.Max(zSoff - z0, 1.0);
                double lo = Math.Max(1.5 * bMax, Math.Max(hn / 6.0, 50.0));
                if (lo > hn * 0.45) lo = hn * 0.45;
                if (lo < 15.0) lo = Math.Min(15.0, hn * 0.4);

                double lb = Ts500KenetlenmeLbCm(diaLong, _rebarFckMPa > 16.0 ? _rebarFckMPa : 30.0, _rebarFykMPa > 200.0 ? _rebarFykMPa : 420.0);
                double lap = CeilTo5Cm(Math.Max(lb, 30.0));
                KolonOrtUcdeBindirme(z0, zSoff, lap, out double zSpBot, out double zSpTop);

                var barXs = GetKolonGorunusLongFaceBarXs(g, st.floorIndex, col.ColumnNo);
                double xBarLo = st.lo + KolonKesitPaspayiCm + KolonKesitDuseyDonatiRadiusCm;
                double xBarHi = st.hi - KolonKesitPaspayiCm - KolonKesitDuseyDonatiRadiusCm;
                if (barXs != null && barXs.Count > 0)
                {
                    xBarLo = barXs.Min();
                    xBarHi = barXs.Max();
                }
                double x0 = X(xBarLo - 1.0);
                double x1 = X(xBarHi + 1.0);
                if (x1 < x0) { double t = x0; x0 = x1; x1 = t; }

                var sabitZs = new List<double>();
                bool isLastStory = i == stories.Count - 1;
                void Et(double z)
                {
                    double zDraw = z;
                    if (isLastStory && z >= st.zTop - 2.5)
                        zDraw = st.zTop - 5.0;
                    if (zDraw < z0 - 1.0) return;
                    bool katCizgisi = !isLastStory && z >= st.zTop - 2.5;
                    if (!katCizgisi)
                        Ln(x0, Y(zDraw), x1, Y(zDraw), LayerEtriye);
                    etriyeZs?.Add(zDraw);
                    sabitZs.Add(zDraw);
                }

                if (tekAralik)
                {
                    // GPR sabit aralık ve sıklaştırma eklenmedi: kolon altı + en yüksek kiriş obası.
                    Et(z0);
                    if (Math.Abs(zEtTop - z0) >= 1.5)
                        Et(zEtTop);
                }
                else
                {
                    var zs = new List<double> { z0, zEtTop };
                    double ortaCm = (zSoff - lo) - (z0 + lo);
                    // İki sarılma arası ≥ 40 cm: alt sarılma üstü + üst sarılma altı sabit etriye.
                    if (ortaCm >= 40.0)
                    {
                        zs.Add(z0 + lo);
                        zs.Add(zSoff - lo);
                    }
                    // İki sarılma arası > 100 cm: bindirme (ℓb) sınırlarına da etriye.
                    double sLbEt = TbdY2018KolonLbEtriyeScCm(shortCm);
                    if (ortaCm > 100.0 && sMid > sLbEt + 0.05)
                    {
                        if (zSpBot - (z0 + lo) >= 40.0)
                            zs.Add(zSpBot);
                        if ((zSoff - lo) - zSpTop >= 40.0)
                            zs.Add(zSpTop);
                    }
                    zs.Sort();
                    double lastZ = double.NaN;
                    foreach (double z in zs)
                    {
                        if (z < z0 - 0.01 || z > zEtTop + 0.01) continue;
                        if (!double.IsNaN(lastZ) && Math.Abs(z - lastZ) < 1.5) continue;
                        lastZ = z;
                        Et(z);
                    }
                }

                if (st.zTop > zEtTop + 1.5)
                    Et(st.zTop);
                if (zSoff > z0 + 1.5 && zSoff + 1.5 < zEtTop)
                    Et(zSoff);

                if (i == 0 && temelSpans != null && temelSpans.Count > 0)
                {
                    // TBDY 2018 7.3.4.1: alt sarılma etriyesi temel içinde ≥ bmin;
                    // çanak/tekil temelde tüm temel yüksekliği. s ≤ min(bmin/3, 150 mm, 6φℓ), s ≥ 50 mm.
                    double zTb = temelSpans.Min(t => t.z0);
                    double zCover = zTb + 5.0;
                    if (z0 - zCover >= 8.0)
                    {
                        FloorInfo fl = (_model?.Floors != null && st.floorIndex >= 0 && st.floorIndex < _model.Floors.Count)
                            ? _model.Floors[st.floorIndex] : null;
                        bool canak = KolonOtururTekilTemel(st.poly, fl);
                        double hNeed = canak ? (z0 - zCover) : shortCm;
                        double zEnd = z0 - hNeed;
                        if (zEnd < zCover) zEnd = zCover;
                        ParseKolonEtriyeAralikCm(etRaw, out _, out double sGprConfFond, out _);
                        double sUse = TbdY2018KolonSarilmaSMaxCm(shortCm, diaLong);
                        if (!tekAralikGpr) sUse = Math.Min(sUse, sGprConfFond);
                        int sCm = Math.Max(5, (int)Math.Floor(sUse + 1e-6));
                        double zFondBotEt = double.NaN;
                        for (double z = z0 - sCm; z >= zEnd - 0.05; z -= sCm)
                        {
                            if (z < zCover - 0.05) break;
                            Ln(x0, Y(z), x1, Y(z), LayerEtriye);
                            zFondBotEt = z;
                        }
                        if (!double.IsNaN(zFondBotEt))
                        {
                            etriyeZs?.Add(zFondBotEt);
                            etriyeBolgeler?.Add((zFondBotEt, z0, sSikCm, diaEt));
                        }
                    }
                }

                if (!isLastStory)
                    etriyeZs?.Add(st.zTop);
                // En düşük oba ile en yüksek oba ayrıysa birleşim yazısı 8'e karışmasın.
                if (zEtTop > zSoff + 2.0)
                    etriyeZs?.Add(zSoff);
                if (etriyeBolgeler != null)
                {
                    if (tekAralik)
                    {
                        etriyeBolgeler.Add((z0, st.zTop, sMidCm, diaEt));
                        if (zSpTop > zSpBot + 1.0)
                            etriyeBolgeler.Add((zSpBot, zSpTop, sLbCm, diaEt));
                        if (st.zTop > zSoff + 1.0)
                            etriyeBolgeler.Add((zSoff, st.zTop, TbdY2018KolonBirlesimSjCm, diaEt));
                    }
                    else
                    {
                        // Sarılma: min(15, bmin/3, 6φℓ). ℓb: min(15, bmin/3) — 6φ yok.
                        // Birleşim (kiriş içi): sj ≤ 10 cm.
                        etriyeBolgeler.Add((z0, z0 + lo, sSikCm, diaEt));
                        if (zSoff - lo > z0 + lo + 1.0)
                            etriyeBolgeler.Add((z0 + lo, zSoff - lo, sMidCm, diaEt));
                        if (zSpTop > zSpBot + 1.0)
                            etriyeBolgeler.Add((zSpBot, zSpTop, sLbCm, diaEt));
                        etriyeBolgeler.Add((zSoff - lo, zSoff, sSikCm, diaEt));
                        if (st.zTop > zSoff + 1.0)
                            etriyeBolgeler.Add((zSoff, st.zTop, TbdY2018KolonBirlesimSjCm, diaEt));
                    }
                }
                DrawKolonEtriyeSabitKopyalar(tr, btr, X, Y, Ln, x0, x1, st.lo, sabitZs, etriyeBolgeler);
            }
        }

        /// <summary>Sabit etriyeyi komşu dilimin eşitlenmiş aralığı kadar aşağı/yukarı kopyala; ölçü solda.</summary>
        private void DrawKolonEtriyeSabitKopyalar(
            Transaction tr,
            BlockTableRecord btr,
            Func<double, double> X,
            Func<double, double> Y,
            Action<double, double, double, double, string> Ln,
            double x0,
            double x1,
            double colLo,
            List<double> sabitZs,
            List<(double zLo, double zHi, int sCm, int diaMm)> bolgeler)
        {
            if (tr == null || btr == null || X == null || Y == null || Ln == null) return;
            if (sabitZs == null || sabitZs.Count < 2) return;
            var zs = sabitZs.Distinct().OrderBy(v => v).ToList();
            var kopyaZs = new List<double>();
            bool Yakin(List<double> src, double z)
            {
                foreach (double d in src)
                    if (Math.Abs(d - z) < 1.5) return true;
                return false;
            }
            void KopyaFrom(double zFrom, double zTo)
            {
                if (Math.Abs(zTo - zFrom) < 1.5) return;
                if (Yakin(zs, zTo)) return;
                if (!Yakin(kopyaZs, zTo))
                {
                    Ln(x0, Y(zTo), x1, Y(zTo), LayerEtriye);
                    kopyaZs.Add(zTo);
                }
                DrawKolonEtriyeKopyaOlcu(tr, btr, X, Y, colLo, zFrom, zTo);
            }
            for (int i = 0; i < zs.Count - 1; i++)
            {
                double za = zs[i], zb = zs[i + 1];
                int sCode = KolonEtriyeBolgeSMin(bolgeler, za, zb, out _);
                if (!TryKolonEtriyeAdetAralik(zb - za, sCode, out _, out double sAct)) continue;
                KopyaFrom(za, za + sAct);
                KopyaFrom(zb, zb - sAct);
                // Uzun boş dilim: ortada 3 kopya, dilim aralığı sAct, sol ölçü.
                if (zb - za >= 100.0)
                {
                    double zMid = 0.5 * (za + zb);
                    if (!Yakin(zs, zMid) && !Yakin(kopyaZs, zMid))
                    {
                        Ln(x0, Y(zMid), x1, Y(zMid), LayerEtriye);
                        kopyaZs.Add(zMid);
                    }
                    KopyaFrom(zMid, zMid - sAct);
                    KopyaFrom(zMid, zMid + sAct);
                }
            }
        }

        /// <summary>Çoğaltılan etriye: solda yalnızca kopya aralığı, zincir toplamı yok.</summary>
        private void DrawKolonEtriyeKopyaOlcu(
            Transaction tr,
            BlockTableRecord btr,
            Func<double, double> X,
            Func<double, double> Y,
            double colLo,
            double zA,
            double zB)
        {
            if (tr == null || btr == null || X == null || Y == null) return;
            if (Math.Abs(zB - zA) < 1.5) return;
            ObjectId dimId = GetOrCreateEtriyeOlcuDimStyle(tr, btr.Database, 10.0);
            double ya = Y(Math.Min(zA, zB));
            double yb = Y(Math.Max(zA, zB));
            double xFace = X(colLo);
            double xLine = xFace - KolonDuseyEtriyeOlcuKolondanCm;
            var dim = new AlignedDimension(
                new Point3d(xFace, ya, 0),
                new Point3d(xFace, yb, 0),
                new Point3d(xLine, (ya + yb) * 0.5, 0),
                "", dimId)
            {
                Layer = LayerOlcu,
                LineWeight = LineWeight.LineWeight020
            };
            try { dim.DimfxlenOn = true; } catch { }
            try { dim.Dimfxlen = KolonDuseyEtriyeOlcuKolondanCm; } catch { }
            try { dim.Dimrnd = 0.1; } catch { }
            try { dim.Dimdec = 1; } catch { }
            try { dim.Dimtdec = 1; } catch { }
            try { dim.Dimzin = 8; } catch { }
            AppendEntity(tr, btr, dim);
        }

        private bool KolonOtururTekilTemel(Geometry colPoly, FloorInfo floor)
        {
            if (colPoly == null || colPoly.IsEmpty || _model?.SingleFootings == null) return false;
            foreach (var sf in _model.SingleFootings)
            {
                try
                {
                    var p = SingleFootingModelPoly(sf, floor);
                    if (p != null && !p.IsEmpty && p.Intersects(colPoly)) return true;
                }
                catch { }
            }
            return false;
        }

        private int ResolveKolonDuseyEtriyeDiaMm(int floorIndex, int colNo)
        {
            if (_kolonDuseyGpr != null && _model?.Floors != null &&
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(_kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out _, out string etriye) &&
                TryParseEtriyeDiaMm(etriye, out int d))
                return d;
            return 8;
        }

        private void DrawKolonKesitEtriye(Transaction tr, BlockTableRecord btr, Geometry g, Envelope e, int floorIndex, int colNo, bool skipIkinciEtriye = false)
        {
            if (tr == null || btr == null || e == null) return;
            int diaMm = ResolveKolonDuseyEtriyeDiaMm(floorIndex, colNo);
            double rad = KolonKesitEtriyeRadiusCm;
            double hook = TbdY2018EtriyeHookExtCm(diaMm);
            double inset = KolonKesitPaspayiCm;
            double x0 = e.MinX + inset, y0 = e.MinY + inset, x1 = e.MaxX - inset, y1 = e.MaxY - inset;
            if (x1 - x0 < 2.0 * rad + 2.0 || y1 - y0 < 2.0 * rad + 2.0) return;

            bool looksCircle = Math.Abs(e.Width - e.Height) < 3.0 && g != null && !g.IsEmpty;
            if (looksCircle)
            {
                double rOut = 0.5 * Math.Min(e.Width, e.Height);
                double rIn = rOut - inset;
                double circA = Math.PI * rOut * rOut;
                if (rIn > rad + 1.0 && circA > 1.0 && Math.Abs(g.Area - circA) < 0.15 * circA)
                {
                    var circ = new Circle(new Point3d(e.Centre.X, e.Centre.Y, 0), Vector3d.ZAxis, rIn);
                    circ.SetDatabaseDefaults();
                    circ.Layer = string.IsNullOrEmpty(_kolonKesitLayerOverride) ? LayerEtriye : _kolonKesitLayerOverride;
                    AppendEntity(tr, btr, circ);
                    return;
                }
            }

            AppendKolonKesitEtriyePline(tr, btr, x0, y0, x1, y1, rad, hook);
            ResolveKolonKesitBarCounts(x0, y0, x1, y1, rad, floorIndex, colNo,
                out var cTl, out var cTr, out var cBr, out var cBl,
                out int top, out int bot, out int left, out int right);
            bool hasInnerX = false, hasInnerY = false;
            double innerXLo = 0, innerXHi = 0, innerYLo = 0, innerYHi = 0;
            if (!skipIkinciEtriye)
                TryDrawKolonKesitIkinciEtriyeler(tr, btr, e, x0, y0, x1, y1, rad, hook, cTl, cTr, cBr, cBl, top, bot, left, right,
                    out hasInnerX, out innerXLo, out innerXHi, out hasInnerY, out innerYLo, out innerYHi);
            TryDrawKolonKesitCiroz(tr, btr, e, x0, y0, x1, y1, rad, hook, diaMm, cTl, cTr, cBr, cBl, top, bot, left, right,
                hasInnerX, innerXLo, innerXHi, hasInnerY, innerYLo, innerYHi);
            DrawKolonKesitDuseyDonatiCemberleri(tr, btr, cTl, cTr, cBr, cBl, top, bot, left, right);
        }

        /// <summary>Kenar ≥ 60 cm ise o doğrultuda kapalı 2. etriye (120+ ise 1/3–2/3).</summary>
        private void TryDrawKolonKesitIkinciEtriyeler(
            Transaction tr, BlockTableRecord btr, Envelope e,
            double x0, double y0, double x1, double y1, double rad, double hook,
            Point2d cTl, Point2d cTr, Point2d cBr, Point2d cBl,
            int top, int bot, int left, int right,
            out bool hasInnerX, out double innerXLo, out double innerXHi,
            out bool hasInnerY, out double innerYLo, out double innerYHi)
        {
            hasInnerX = hasInnerY = false;
            innerXLo = innerXHi = innerYLo = innerYHi = 0;
            if (e == null) return;
            double colW = e.Width, colH = e.Height;
            bool kare = Math.Abs(colW - colH) < 3.0;
            if (colW >= KolonKesitIkinciEtriyeMinUzunCm - 0.01)
            {
                IkinciEtriyeHedefKesir(colW, kare, out double fLo, out double fHi);
                var xs = CollectEdgeCoords(cTl, cTr, Math.Max(top, bot), horizontal: true);
                if (TryNearestBarPair(xs, e.MinX + fLo * colW, e.MinX + fHi * colW, out innerXLo, out innerXHi))
                {
                    double x0s = innerXLo - rad, x1s = innerXHi + rad;
                    if (x1s - x0s >= 2.0 * rad + 2.0 && y1 - y0 >= 2.0 * rad + 2.0)
                    {
                        AppendKolonKesitEtriyePline(tr, btr, x0s, y0, x1s, y1, rad, hook);
                        hasInnerX = true;
                    }
                }
            }
            if (colH >= KolonKesitIkinciEtriyeMinUzunCm - 0.01)
            {
                IkinciEtriyeHedefKesir(colH, kare, out double fLo, out double fHi);
                var ys = CollectEdgeCoords(cBl, cTl, Math.Max(left, right), horizontal: false);
                if (TryNearestBarPair(ys, e.MinY + fLo * colH, e.MinY + fHi * colH, out innerYLo, out innerYHi))
                {
                    double y0s = innerYLo - rad, y1s = innerYHi + rad;
                    if (x1 - x0 >= 2.0 * rad + 2.0 && y1s - y0s >= 2.0 * rad + 2.0)
                    {
                        AppendKolonKesitEtriyePline(tr, btr, x0, y0s, x1, y1s, rad, hook);
                        hasInnerY = true;
                    }
                }
            }
        }

        /// <summary>
        /// TBDY 2018 7.3.4: bağlı düşey donatı merkezleri arası a ≤ 25φ.
        /// Uzun ve kısa kenarda boşluk varsa her iki yönde de; etriye/2. etriye köşelerine atılmaz.
        /// </summary>
        private void TryDrawKolonKesitCiroz(
            Transaction tr, BlockTableRecord btr, Envelope e,
            double x0, double y0, double x1, double y1, double rad, double hook135, int diaMm,
            Point2d cTl, Point2d cTr, Point2d cBr, Point2d cBl,
            int top, int bot, int left, int right,
            bool hasInnerX, double innerXLo, double innerXHi,
            bool hasInnerY, double innerYLo, double innerYHi)
        {
            if (e == null) return;
            double hook90 = KolonKesitCirozHook90CizimCm;
            double colW = e.Width, colH = e.Height;
            bool kare = Math.Abs(colW - colH) < 3.0;
            double aMax = (kare ? KolonKesitCirozAMaxFiKare : KolonKesitCirozAMaxFi) * Math.Max(diaMm, 8) / 10.0;
            double midX = (e.MinX + e.MaxX) * 0.5;
            double midY = (e.MinY + e.MaxY) * 0.5;
            const double tol = 1.0;
            double cr = KolonKesitCirozRadiusCm;

            var xs = CollectEdgeCoords(cTl, cTr, Math.Max(top, bot), horizontal: true);
            var heldX = new List<double> { cTl.X, cTr.X };
            if (hasInnerX)
            {
                heldX.Add(innerXLo);
                heldX.Add(innerXHi);
            }
            var xsPick = PickCirozBarCenters(xs, heldX, aMax, tol);
            xsPick.Sort();
            for (int i = 0; i < xsPick.Count; i++)
            {
                double bx = xsPick[i];
                DrawKolonKesitCirozC(tr, btr, bx, cBl.Y, cTl.Y, cr, hook90, hook135,
                    leftLeg: bx <= midX, hook135OnTop: i % 2 == 0);
            }

            var ys = CollectEdgeCoords(cBl, cTl, Math.Max(left, right), horizontal: false);
            var heldY = new List<double> { cBl.Y, cTl.Y };
            if (hasInnerY)
            {
                heldY.Add(innerYLo);
                heldY.Add(innerYHi);
            }
            var ysPick = PickCirozBarCenters(ys, heldY, aMax, tol);
            ysPick.Sort();
            for (int j = 0; j < ysPick.Count; j++)
            {
                double by = ysPick[j];
                DrawKolonKesitCirozCHorizontal(tr, btr, cBl.X, cBr.X, by, cr, hook90, hook135,
                    bottomLeg: by <= midY, hook135OnRight: j % 2 == 0);
            }
        }

        /// <summary>Tutulu merkezler arası &gt; 25φ ise aradaki mevcut donatıya minimum çiroz.</summary>
        private static List<double> PickCirozBarCenters(List<double> allBars, List<double> held, double aMax, double tol)
        {
            var result = new List<double>();
            if (allBars == null || held == null || aMax < 1.0) return result;
            var live = new List<double>(held);
            for (int n = 0; n < 24; n++)
            {
                live.Sort();
                double bestGap = 0, gapLo = 0, gapHi = 0;
                for (int i = 0; i < live.Count - 1; i++)
                {
                    if (live[i + 1] - live[i] < 0.5) continue;
                    double g = live[i + 1] - live[i];
                    if (g > bestGap)
                    {
                        bestGap = g;
                        gapLo = live[i];
                        gapHi = live[i + 1];
                    }
                }
                if (bestGap <= aMax + 0.05) break;
                double target = (gapLo + gapHi) * 0.5;
                double pick = double.NaN, pickD = double.MaxValue;
                foreach (double b in allBars)
                {
                    if (b <= gapLo + tol || b >= gapHi - tol) continue;
                    bool already = false;
                    foreach (double h in live)
                    {
                        if (Math.Abs(h - b) < tol) { already = true; break; }
                    }
                    if (already) continue;
                    double d = Math.Abs(b - target);
                    if (d < pickD) { pickD = d; pick = b; }
                }
                if (double.IsNaN(pick)) break;
                live.Add(pick);
                result.Add(pick);
            }
            return result;
        }

        private void DrawKolonKesitCirozC(
            Transaction tr, BlockTableRecord btr,
            double bx, double yBot, double yTop, double rad, double hook90, double hook135,
            bool leftLeg, bool hook135OnTop)
        {
            double c45 = Math.Cos(Math.PI / 4.0);
            double b90 = Math.Tan(Math.PI / 8.0);
            double b135 = Math.Tan(135.0 * Math.PI / 180.0 / 4.0);
            if (leftLeg && hook135OnTop)
            {
                var p45 = new Point2d(bx + rad * c45, yTop + rad * c45);
                var tip135 = new Point2d(p45.X + hook135 * c45, p45.Y - hook135 * c45);
                AppendDonatiPline(tr, btr, new[]
                {
                    new Point2d(bx + hook90, yBot - rad),
                    new Point2d(bx, yBot - rad),
                    new Point2d(bx - rad, yBot),
                    new Point2d(bx - rad, yTop),
                    p45,
                    tip135
                }, new[] { 0.0, -b90, 0.0, -b135, 0.0, 0.0 }, LayerCirozBeykent);
            }
            else if (leftLeg)
            {
                var p45 = new Point2d(bx + rad * c45, yBot - rad * c45);
                var tip135 = new Point2d(p45.X + hook135 * c45, p45.Y + hook135 * c45);
                AppendDonatiPline(tr, btr, new[]
                {
                    new Point2d(bx + hook90, yTop + rad),
                    new Point2d(bx, yTop + rad),
                    new Point2d(bx - rad, yTop),
                    new Point2d(bx - rad, yBot),
                    p45,
                    tip135
                }, new[] { 0.0, b90, 0.0, b135, 0.0, 0.0 }, LayerCirozBeykent);
            }
            else if (hook135OnTop)
            {
                var p135 = new Point2d(bx - rad * c45, yTop + rad * c45);
                var tip135 = new Point2d(p135.X - hook135 * c45, p135.Y - hook135 * c45);
                AppendDonatiPline(tr, btr, new[]
                {
                    new Point2d(bx - hook90, yBot - rad),
                    new Point2d(bx, yBot - rad),
                    new Point2d(bx + rad, yBot),
                    new Point2d(bx + rad, yTop),
                    p135,
                    tip135
                }, new[] { 0.0, b90, 0.0, b135, 0.0, 0.0 }, LayerCirozBeykent);
            }
            else
            {
                var p45 = new Point2d(bx - rad * c45, yBot - rad * c45);
                var tip135 = new Point2d(p45.X - hook135 * c45, p45.Y + hook135 * c45);
                AppendDonatiPline(tr, btr, new[]
                {
                    new Point2d(bx - hook90, yTop + rad),
                    new Point2d(bx, yTop + rad),
                    new Point2d(bx + rad, yTop),
                    new Point2d(bx + rad, yBot),
                    p45,
                    tip135
                }, new[] { 0.0, -b90, 0.0, -b135, 0.0, 0.0 }, LayerCirozBeykent);
            }
        }

        private void DrawKolonKesitCirozCHorizontal(
            Transaction tr, BlockTableRecord btr,
            double xLeft, double xRight, double by, double rad, double hook90, double hook135,
            bool bottomLeg, bool hook135OnRight)
        {
            double c45 = Math.Cos(Math.PI / 4.0);
            double b90 = Math.Tan(Math.PI / 8.0);
            double b135 = Math.Tan(135.0 * Math.PI / 180.0 / 4.0);
            if (bottomLeg && hook135OnRight)
            {
                var p45 = new Point2d(xRight + rad * c45, by + rad * c45);
                var tip135 = new Point2d(p45.X - hook135 * c45, p45.Y + hook135 * c45);
                AppendDonatiPline(tr, btr, new[]
                {
                    new Point2d(xLeft - rad, by + hook90),
                    new Point2d(xLeft - rad, by),
                    new Point2d(xLeft, by - rad),
                    new Point2d(xRight, by - rad),
                    p45,
                    tip135
                }, new[] { 0.0, b90, 0.0, b135, 0.0, 0.0 }, LayerCirozBeykent);
            }
            else if (bottomLeg)
            {
                var p45 = new Point2d(xLeft - rad * c45, by + rad * c45);
                var tip135 = new Point2d(p45.X + hook135 * c45, p45.Y + hook135 * c45);
                AppendDonatiPline(tr, btr, new[]
                {
                    new Point2d(xRight + rad, by + hook90),
                    new Point2d(xRight + rad, by),
                    new Point2d(xRight, by - rad),
                    new Point2d(xLeft, by - rad),
                    p45,
                    tip135
                }, new[] { 0.0, -b90, 0.0, -b135, 0.0, 0.0 }, LayerCirozBeykent);
            }
            else if (hook135OnRight)
            {
                var p45 = new Point2d(xRight + rad * c45, by - rad * c45);
                var tip135 = new Point2d(p45.X - hook135 * c45, p45.Y - hook135 * c45);
                AppendDonatiPline(tr, btr, new[]
                {
                    new Point2d(xLeft - rad, by - hook90),
                    new Point2d(xLeft - rad, by),
                    new Point2d(xLeft, by + rad),
                    new Point2d(xRight, by + rad),
                    p45,
                    tip135
                }, new[] { 0.0, -b90, 0.0, -b135, 0.0, 0.0 }, LayerCirozBeykent);
            }
            else
            {
                var p45 = new Point2d(xLeft - rad * c45, by - rad * c45);
                var tip135 = new Point2d(p45.X + hook135 * c45, p45.Y - hook135 * c45);
                AppendDonatiPline(tr, btr, new[]
                {
                    new Point2d(xRight + rad, by - hook90),
                    new Point2d(xRight + rad, by),
                    new Point2d(xRight, by + rad),
                    new Point2d(xLeft, by + rad),
                    p45,
                    tip135
                }, new[] { 0.0, b90, 0.0, b135, 0.0, 0.0 }, LayerCirozBeykent);
            }
        }

        private static void IkinciEtriyeHedefKesir(double colLongCm, bool kare, out double fLo, out double fHi)
        {
            if (kare || colLongCm >= KolonKesitIkinciEtriyeBirUcCm - 0.01)
            {
                fLo = 1.0 / 3.0;
                fHi = 2.0 / 3.0;
            }
            else
            {
                fLo = 0.25;
                fHi = 0.75;
            }
        }

        private static List<double> CollectEdgeCoords(Point2d a, Point2d b, int extras, bool horizontal)
        {
            var list = new List<double>(extras + 2);
            list.Add(horizontal ? a.X : a.Y);
            for (int i = 1; i <= extras; i++)
            {
                double t = i / (double)(extras + 1);
                list.Add(horizontal ? a.X + t * (b.X - a.X) : a.Y + t * (b.Y - a.Y));
            }
            list.Add(horizontal ? b.X : b.Y);
            list.Sort();
            return list;
        }

        private static bool TryNearestBarPair(List<double> coords, double t1, double t2, out double lo, out double hi)
        {
            lo = hi = 0;
            if (coords == null || coords.Count < 2) return false;
            if (t1 > t2) { double tmp = t1; t1 = t2; t2 = tmp; }
            int i1 = IndexNearestCoord(coords, t1);
            int i2 = IndexNearestCoord(coords, t2);
            if (i1 > i2) { int it = i1; i1 = i2; i2 = it; }
            if (i1 == i2)
            {
                if (i1 > 0 && (i1 == coords.Count - 1 ||
                    Math.Abs(coords[i1 - 1] - t1) <= Math.Abs(coords[Math.Min(i1 + 1, coords.Count - 1)] - t2)))
                    i1--;
                else if (i2 < coords.Count - 1)
                    i2++;
                else
                    return false;
            }
            lo = coords[i1];
            hi = coords[i2];
            return hi - lo > 1.0;
        }

        private static int IndexNearestCoord(List<double> coords, double t)
        {
            int best = 0;
            double bestD = Math.Abs(coords[0] - t);
            for (int i = 1; i < coords.Count; i++)
            {
                double d = Math.Abs(coords[i] - t);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private void AppendKolonKesitEtriyePline(
            Transaction tr, BlockTableRecord btr,
            double x0, double y0, double x1, double y1, double rad, double hook)
        {
            double c45 = Math.Cos(Math.PI / 4.0);
            var cTl = new Point2d(x0 + rad, y1 - rad);
            var tTop = new Point2d(x0 + rad, y1);
            var tLeft = new Point2d(x0, y1 - rad);
            var p225 = new Point2d(cTl.X - rad * c45, cTl.Y - rad * c45);
            var p45 = new Point2d(cTl.X + rad * c45, cTl.Y + rad * c45);
            var tipTop = new Point2d(p225.X + hook * c45, p225.Y - hook * c45);
            var tipLeft = new Point2d(p45.X + hook * c45, p45.Y - hook * c45);
            double b90 = -Math.Tan(Math.PI / 8.0);
            double b135 = -Math.Tan(135.0 * Math.PI / 180.0 / 4.0);
            var pts = new[]
            {
                tipTop,
                p225,
                tTop,
                new Point2d(x1 - rad, y1),
                new Point2d(x1, y1 - rad),
                new Point2d(x1, y0 + rad),
                new Point2d(x1 - rad, y0),
                new Point2d(x0 + rad, y0),
                new Point2d(x0, y0 + rad),
                tLeft,
                p45,
                tipLeft
            };
            var bul = new[] { 0.0, b135, 0.0, b90, 0.0, b90, 0.0, b90, 0.0, b135, 0.0, 0.0 };
            AppendDonatiPline(tr, btr, pts, bul, LayerEtriye);
        }

        private void ResolveKolonKesitBarCounts(
            double x0, double y0, double x1, double y1, double rad,
            int floorIndex, int colNo,
            out Point2d cTl, out Point2d cTr, out Point2d cBr, out Point2d cBl,
            out int top, out int bot, out int left, out int right)
        {
            cTl = new Point2d(x0 + rad, y1 - rad);
            cTr = new Point2d(x1 - rad, y1 - rad);
            cBr = new Point2d(x1 - rad, y0 + rad);
            cBl = new Point2d(x0 + rad, y0 + rad);
            top = bot = left = right = 0;
            int n = 4;
            int diaMm = 14;
            if (_kolonDuseyGpr != null && _model?.Floors != null &&
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(_kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out string donati, out _) &&
                !string.IsNullOrWhiteSpace(donati))
            {
                int parsed = KolonDonatiTableDrawer.SumKolonKesitDuseyDonatiAdet(donati, out int d);
                if (parsed > 0) n = parsed;
                if (d >= 6) diaMm = d;
            }
            if (n < 4) n = 4;
            int rest = n - 4;
            if (rest <= 0) return;

            double phiCm = diaMm / 10.0;
            double minCcLong = Math.Max(1.5 * phiCm, 4.0) + phiCm;
            double minCcShort = Math.Max(1.5 * phiCm, 4.0) + phiCm;
            const double maxCc = 20.0;
            double Lx = Math.Abs(cTr.X - cTl.X);
            double Ly = Math.Abs(cTl.Y - cBl.Y);
            if (Math.Abs(Lx - Ly) < 3.0)
                AllocateSquareFaceExtras(rest, Lx, minCcLong, maxCc, out top, out bot, out left, out right);
            else
                AllocateRectFaceExtras(rest, Lx, Ly, minCcLong, minCcShort, maxCc, out top, out bot, out left, out right);
        }

        private void DrawKolonKesitDuseyDonatiCemberleri(
            Transaction tr, BlockTableRecord btr,
            Point2d cTl, Point2d cTr, Point2d cBr, Point2d cBl,
            int top, int bot, int left, int right)
        {
            DrawTwoArcCirclePline(tr, btr, cTl.X, cTl.Y, KolonKesitDuseyDonatiRadiusCm, LayerDonatiGovde);
            DrawTwoArcCirclePline(tr, btr, cTr.X, cTr.Y, KolonKesitDuseyDonatiRadiusCm, LayerDonatiGovde);
            DrawTwoArcCirclePline(tr, btr, cBr.X, cBr.Y, KolonKesitDuseyDonatiRadiusCm, LayerDonatiGovde);
            DrawTwoArcCirclePline(tr, btr, cBl.X, cBl.Y, KolonKesitDuseyDonatiRadiusCm, LayerDonatiGovde);
            PlaceBarsOnEdge(tr, btr, cTl, cTr, top);
            PlaceBarsOnEdge(tr, btr, cBl, cBr, bot);
            PlaceBarsOnEdge(tr, btr, cBl, cTl, left);
            PlaceBarsOnEdge(tr, btr, cBr, cTr, right);
        }

        private static void AllocateSquareFaceExtras(
            int rest, double L, double minCc, double maxCc,
            out int top, out int bot, out int left, out int right)
        {
            FaceExtrasRange(L, minCc, maxCc, out int minE, out int maxE);
            int e = rest / 4;
            int rem = rest - 4 * e;
            if (e < minE)
            {
                int need = minE * 4;
                if (need <= rest)
                {
                    e = minE;
                    rem = rest - need;
                }
            }
            if (maxE > 0 && e > maxE)
            {
                e = maxE;
                rem = rest - 4 * e;
            }
            top = bot = left = right = e;
            AddFacePair(ref top, ref bot, int.MaxValue, int.MaxValue, ref rem);
            AddFacePair(ref left, ref right, int.MaxValue, int.MaxValue, ref rem);
            AddFacePair(ref top, ref left, int.MaxValue, int.MaxValue, ref rem);
        }

        private static void AllocateRectFaceExtras(
            int rest, double Lx, double Ly, double minCcLong, double minCcShort, double maxCc,
            out int top, out int bot, out int left, out int right)
        {
            bool longIsX = Lx >= Ly;
            double Llong = longIsX ? Lx : Ly;
            double Lshort = longIsX ? Ly : Lx;
            FaceExtrasRange(Llong, minCcLong, maxCc, out int minLong, out int maxLong);
            FaceExtrasRange(Lshort, minCcShort, maxCc, out int minShort, out int maxShort);

            int longA = minLong, longB = minLong, shortA = minShort, shortB = minShort;
            int leftover = rest - (longA + longB + shortA + shortB);
            if (leftover < 0)
            {
                ShrinkFaceExtras(ref longA, ref longB, ref shortA, ref shortB, longIsX: true, remove: -leftover);
                leftover = 0;
            }
            if (leftover > 0)
            {
                AddFacePair(ref shortA, ref shortB, maxShort, maxShort, ref leftover);
                AddFacePair(ref longA, ref longB, maxLong, maxLong, ref leftover);
                AddFacePair(ref shortA, ref shortB, int.MaxValue, int.MaxValue, ref leftover);
                AddFacePair(ref longA, ref longB, int.MaxValue, int.MaxValue, ref leftover);
            }
            if (longIsX)
            {
                top = longA;
                bot = longB;
                left = shortA;
                right = shortB;
            }
            else
            {
                left = longA;
                right = longB;
                top = shortA;
                bot = shortB;
            }
        }

        private static void FaceExtrasRange(double L, double minCc, double maxCc, out int mn, out int mx)
        {
            if (L < minCc * 1.01)
            {
                mn = 0;
                mx = 0;
                return;
            }
            mn = Math.Max(0, (int)Math.Ceiling(L / maxCc) - 1);
            mx = Math.Max(0, (int)Math.Floor(L / minCc) - 1);
            if (mn > mx) mn = mx;
        }

        private static void AddFacePair(ref int a, ref int b, int capA, int capB, ref int leftover)
        {
            while (leftover > 0 && (a < capA || b < capB))
            {
                if (a <= b && a < capA) { a++; leftover--; }
                else if (b < capB) { b++; leftover--; }
                else if (a < capA) { a++; leftover--; }
                else break;
            }
        }

        private static void ShrinkFaceExtras(ref int top, ref int bot, ref int left, ref int right, bool longIsX, int remove)
        {
            while (remove > 0)
            {
                if (longIsX)
                {
                    if (left + right > 0)
                    {
                        if (left >= right && left > 0) left--;
                        else if (right > 0) right--;
                        else if (left > 0) left--;
                        else break;
                    }
                    else if (top + bot > 0)
                    {
                        if (top >= bot && top > 0) top--;
                        else if (bot > 0) bot--;
                        else if (top > 0) top--;
                        else break;
                    }
                    else break;
                }
                else
                {
                    if (top + bot > 0)
                    {
                        if (top >= bot && top > 0) top--;
                        else if (bot > 0) bot--;
                        else if (top > 0) top--;
                        else break;
                    }
                    else if (left + right > 0)
                    {
                        if (left >= right && left > 0) left--;
                        else if (right > 0) right--;
                        else if (left > 0) left--;
                        else break;
                    }
                    else break;
                }
                remove--;
            }
        }

        private void PlaceBarsOnEdge(Transaction tr, BlockTableRecord btr, Point2d a, Point2d b, int extras)
        {
            if (extras <= 0) return;
            double r = KolonKesitDuseyDonatiRadiusCm;
            for (int i = 1; i <= extras; i++)
            {
                double t = i / (double)(extras + 1);
                DrawTwoArcCirclePline(tr, btr, a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y), r, LayerDonatiGovde);
            }
        }

        /// <summary>CIRCLE entity değil: kapalı polyline, 2 vertex, her segment 180° yay (bulge=1).</summary>
        private void DrawTwoArcCirclePline(Transaction tr, BlockTableRecord btr, double cx, double cy, double r, string layer)
        {
            if (tr == null || btr == null || r < 0.05) return;
            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            pl.Layer = !string.IsNullOrEmpty(_kolonKesitLayerOverride)
                ? _kolonKesitLayerOverride
                : (string.IsNullOrEmpty(layer) ? LayerDonatiGovde : layer);
            pl.Closed = true;
            double w = KolonKesitDuseyDonatiCemberWidthCm;
            pl.AddVertexAt(0, new Point2d(cx - r, cy), 1.0, w, w);
            pl.AddVertexAt(1, new Point2d(cx + r, cy), 1.0, w, w);
            pl.ConstantWidth = w;
            AppendEntity(tr, btr, pl);
            pl.ConstantWidth = w;
        }

        private void DrawKolonKesitDuseyDonatiYazisi(Transaction tr, BlockTableRecord btr, Envelope e, int floorIndex, int colNo, bool isPerdeBasligi = false)
        {
            if (tr == null || btr == null || e == null || _kolonDuseyGpr == null || _model?.Floors == null) return;
            if (!KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(_kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out string donati, out string etriye))
                return;
            const double h = 10.0;
            string donText = KolonDonatiTableDrawer.FormatKolonKesitDuseyDonatiOzet(donati);
            int diaLong = 14;
            KolonDonatiTableDrawer.SumKolonKesitDuseyDonatiAdet(donati, out int d);
            if (d >= 6) diaLong = d;
            double bMin = Math.Min(e.Width, e.Height);
            string etText = FormatKolonEtriyeYazisiTbdY(etriye, bMin, diaLong, isPerdeBasligi, out _);
            double y = e.MaxY + 3.0;
            if (!string.IsNullOrWhiteSpace(donText))
            {
                DrawBeamLabel(tr, btr, btr.Database, new Point3d(e.MinX, y, 0),
                    donText, h, 0.0, LayerDonatiYazisiPerde, bottomLeftAligned: true);
                y += h + 2.0;
            }
            if (!string.IsNullOrWhiteSpace(etText))
            {
                DrawBeamLabel(tr, btr, btr.Database, new Point3d(e.MinX, y, 0),
                    etText, h, 0.0, LayerDonatiYazisiPerde, bottomLeftAligned: true);
            }
        }

        private static void CollectKenarSideZs(
            List<(double x0, double x1, double zb, double zt)> beams,
            List<(double x0, double x1, double zb, double zt)> walls,
            double colLo,
            double colHi,
            bool left,
            List<double> dest)
        {
            if (dest == null) return;
            void Add(List<(double x0, double x1, double zb, double zt)> runs)
            {
                if (runs == null) return;
                foreach (var b in runs)
                {
                    double lo = Math.Min(b.x0, b.x1), hi = Math.Max(b.x0, b.x1);
                    if (left && lo >= colLo - 2.0) continue;
                    if (!left && hi <= colHi + 2.0) continue;
                    dest.Add(b.zb);
                    dest.Add(b.zt);
                }
            }
            Add(beams);
            Add(walls);
        }

        private static List<(double lo, double hi)> KenarYHoles(
            Func<double, double> Y,
            double colLo,
            double colHi,
            bool left,
            List<(double x0, double x1, double zb, double zt)> beams)
        {
            var holes = new List<(double lo, double hi)>();
            if (beams == null) return holes;
            foreach (var b in beams)
            {
                double lo = Math.Min(b.x0, b.x1), hi = Math.Max(b.x0, b.x1);
                if (left && lo >= colLo - 2.0) continue;
                if (!left && hi <= colHi + 2.0) continue;
                holes.Add((Y(b.zb), Y(b.zt)));
            }
            return holes;
        }

        private static bool KenarHasSide(
            List<(double x0, double x1, double zb, double zt)> beams,
            List<(double x0, double x1, double zb, double zt)> walls,
            double colLo,
            double colHi,
            bool left)
        {
            bool Hit(List<(double x0, double x1, double zb, double zt)> runs)
            {
                if (runs == null) return false;
                foreach (var b in runs)
                {
                    double lo = Math.Min(b.x0, b.x1), hi = Math.Max(b.x0, b.x1);
                    if (left && lo < colLo - 2.0) return true;
                    if (!left && hi > colHi + 2.0) return true;
                }
                return false;
            }
            return Hit(beams) || Hit(walls);
        }

        private static void DrawKenarSpanOutlines(
            Action<double, double, double, double, string> ln,
            Func<double, double> X,
            Func<double, double> Y,
            double colLo,
            double colHi,
            List<(double x0, double x1, double zb, double zt)> runs,
            string layer,
            double kenarKesitCm,
            bool mergeStories,
            Func<bool, double, double, double> faceAtZ = null)
        {
            if (runs == null || ln == null) return;
            var leftZ = new List<(double zb, double zt)>();
            var rightZ = new List<(double zb, double zt)>();
            foreach (var b in runs)
            {
                double lo = Math.Min(b.x0, b.x1), xHi = Math.Max(b.x0, b.x1);
                if (lo < colLo - 2.0) leftZ.Add((b.zb, b.zt));
                if (xHi > colHi + 2.0) rightZ.Add((b.zb, b.zt));
            }
            if (mergeStories)
            {
                leftZ = MergeZRanges(leftZ);
                rightZ = MergeZRanges(rightZ);
            }
            void DrawSide(bool left, List<(double zb, double zt)> zs)
            {
                double xCut = left ? colLo - kenarKesitCm : colHi + kenarKesitCm;
                foreach (var z in zs)
                {
                    double xFace = faceAtZ != null ? faceAtZ(left, z.zb, z.zt) : (left ? colLo : colHi);
                    ln(X(xCut), Y(z.zt), X(xFace), Y(z.zt), layer);
                    ln(X(xCut), Y(z.zb), X(xFace), Y(z.zb), layer);
                    ln(X(xCut), Y(z.zb), X(xCut), Y(z.zt), LayerKesitSiniri);
                }
            }
            DrawSide(true, leftZ);
            DrawSide(false, rightZ);
        }

        private static List<(double zb, double zt)> MergeZRanges(List<(double zb, double zt)> src)
        {
            var res = new List<(double zb, double zt)>();
            if (src == null || src.Count == 0) return res;
            var ordered = src.Select(p => (zb: Math.Min(p.zb, p.zt), zt: Math.Max(p.zb, p.zt)))
                .OrderBy(p => p.zb).ToList();
            foreach (var p in ordered)
            {
                if (res.Count == 0 || p.zb > res[res.Count - 1].zt + 2.0)
                    res.Add(p);
                else
                    res[res.Count - 1] = (res[res.Count - 1].zb, Math.Max(res[res.Count - 1].zt, p.zt));
            }
            return res;
        }

        private static double ResolveKolonMajorAngleDeg(Geometry poly, double colAngleDeg, double cx, double cy)
        {
            Geometry local = poly;
            try
            {
                var toLocal = AffineTransformation.RotationInstance(-colAngleDeg * Math.PI / 180.0, cx, cy);
                local = toLocal.Transform(poly);
            }
            catch { local = poly; }
            if (local == null || local.IsEmpty) return colAngleDeg;
            var e = local.EnvelopeInternal;
            if (e.Height > e.Width + 0.5)
                return colAngleDeg + 90.0;
            return colAngleDeg;
        }

        /// <summary>
        /// Görünüşe girmeyen (kısa kenar / perde arkasındaki) saplanan kirişler dahil;
        /// etriye ℓn ve birleşim için. Çizilmez.
        /// </summary>
        private void CollectKolonSaplananKirisSpans(
            FloorInfo floor,
            Geometry colPoly,
            AffineTransformation rot,
            List<(double x0, double x1, double zb, double zt)> dest)
        {
            ForEachKolonBagliKiris(floor, colPoly, includeWalls: false, (beam, poly, zb, zt) =>
            {
                Geometry rg;
                try { rg = rot != null ? rot.Transform(poly) : poly; }
                catch { rg = poly; }
                if (rg == null || rg.IsEmpty) return;
                var e = rg.EnvelopeInternal;
                dest.Add((e.MinX, e.MaxX, zb, zt));
            });
        }

        /// <summary>
        /// Tipleştirme: kattaki saplanan kirişler. K=görünüş kenarı, S=diğer yüz (çizilmez).
        /// </summary>
        private string BuildKolonKatSapKirisImza(FloorInfo floor, ColumnAxisInfo col, Geometry colPoly)
        {
            if (floor == null || col == null || colPoly == null || colPoly.IsEmpty) return "";
            var cxy = colPoly.Centroid;
            double maj = ResolveKolonMajorAngleDeg(colPoly, col.AngleDeg, cxy.X, cxy.Y);
            AffineTransformation rot = null;
            Geometry colR = colPoly;
            try
            {
                rot = AffineTransformation.RotationInstance(-maj * Math.PI / 180.0, cxy.X, cxy.Y);
                colR = rot.Transform(colPoly);
            }
            catch { rot = null; colR = colPoly; }
            var ce = colR.EnvelopeInternal;
            double colLo = ce.MinX, colHi = ce.MaxX;
            var parts = new List<string>();
            ForEachKolonBagliKiris(floor, colPoly, includeWalls: false, (beam, poly, zb, zt) =>
            {
                bool kenar = false;
                if (rot != null)
                {
                    try
                    {
                        var rg = rot.Transform(poly);
                        if (rg != null && !rg.IsEmpty)
                        {
                            var e = rg.EnvelopeInternal;
                            kenar = e.MinX < colLo - 2.0 || e.MaxX > colHi + 2.0;
                        }
                    }
                    catch { }
                }
                double h = Math.Max(zt - zb, 1.0);
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0}@{1:0}{2}", h, Math.Round(zb), kenar ? "K" : "S"));
            });
            if (parts.Count == 0) return "";
            parts.Sort(StringComparer.Ordinal);
            return string.Join(",", parts);
        }

        private void ForEachKolonBagliKiris(
            FloorInfo floor,
            Geometry colPoly,
            bool includeWalls,
            Action<BeamInfo, Polygon, double, double> visit)
        {
            if (floor == null || colPoly == null || colPoly.IsEmpty || visit == null) return;
            if (_model?.Beams == null || _axisService == null) return;
            var gf = _ntsDrawFactory;
            if (gf == null) return;
            double floorLevelCm = (_model.BuildingBaseKotu + floor.ElevationM) * 100.0;
            foreach (var beam in _model.Beams)
            {
                if (beam == null) continue;
                bool isWall = beam.IsWallFlag == 1;
                if (includeWalls != isWall) continue;
                if (GetBeamFloorNo(beam.BeamId) != floor.FloorNo) continue;
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1) ||
                    !_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2))
                    continue;
                Polygon poly;
                try { poly = BuildBeamSegmentPolygon(p1, p2, beam, gf); }
                catch { continue; }
                if (poly == null || poly.IsEmpty) continue;
                bool hitCol = false;
                try { hitCol = poly.Intersects(colPoly) || poly.Distance(colPoly) < 2.0; }
                catch { hitCol = false; }
                if (!hitCol) continue;
                double zu = floorLevelCm + Math.Max(beam.Point1KotCm, beam.Point2KotCm);
                double h = beam.HeightCm > 0 ? beam.HeightCm : (isWall ? 280.0 : 30.0);
                visit(beam, poly, zu - h, zu);
            }
        }

        private void CollectKenarKirisOrPerdeSpans(
            FloorInfo floor,
            Geometry colPoly,
            AffineTransformation rot,
            double sectionY,
            double wallHalf,
            List<(double x0, double x1, double zb, double zt)> dest,
            bool walls)
        {
            if (rot == null || dest == null) return;
            ForEachKolonBagliKiris(floor, colPoly, includeWalls: walls, (beam, poly, zb, zt) =>
            {
                Geometry rg;
                try { rg = rot.Transform(poly); }
                catch { return; }
                if (rg == null || rg.IsEmpty) return;
                if (!TrySectionCutKolonX(rg, sectionY, wallHalf, out double x0, out double x1))
                {
                    var e = rg.EnvelopeInternal;
                    x0 = e.MinX;
                    x1 = e.MaxX;
                }
                if (x1 - x0 < 2.0) return;
                dest.Add((x0, x1, zb, zt));
            });
        }
    }
}
