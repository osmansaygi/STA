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
        private HashSet<string> _kolonDuseyGprHcr;
        private string _kolonKesitLayerOverride;
        /// <summary>KOLONDUSEY benzer grup: kesit isim etiketinde listelenen kolon noları.</summary>
        private int[] _kolonDuseyGrupColNos;

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
            _kolonDuseyGprHcr = null;
            _gprPerdePanelDonati = null;
            try
            {
                string gprPath = ResolveGprPathNextToSt4(st4SourcePath);
                if (!string.IsNullOrEmpty(gprPath))
                {
                    _kolonDuseyGpr = KolonDonatiTableDrawer.ParseKolonBetonarmeFromFile(gprPath, out _, out _kolonDuseyGprHcr);
                    GprPerdePanelDonatiParser.TryParse(gprPath, out _gprPerdePanelDonati, out _);
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
                EnsurePlanLayer(tr, db, LayerPerdeYazisi, 240, LineWeight.LineWeight020, useDashed: false);
                EnsurePlanLayer(tr, db, LayerYazi, 4, LineWeight.LineWeight020, useDashed: false);
                EnsurePlanLayer(tr, db, LayerDonatiYazisiPerde, 3, LineWeight.LineWeight020, useDashed: false);
                EnsurePlanLayer(tr, db, LayerEtiketCizgisi, 64, LineWeight.LineWeight015, useDashed: false);
                EnsurePlanLayer(tr, db, LayerOlcu, 14, LineWeight.LineWeight020, useDashed: false);
                EnsurePlanLayer(tr, db, LayerKotYazi, 7, LineWeight.LineWeight020, useDashed: false);
                EnsurePlanLayer(tr, db, LayerKotCizgisi, 7, LineWeight.LineWeight020, useDashed: false);

                var groups = new Dictionary<string, List<ColumnAxisInfo>>(StringComparer.Ordinal);
                foreach (var col in _model.Columns)
                {
                    if (col == null) continue;
                    string sig = BuildKolonDuseyGroupSignature(col);
                    if (string.IsNullOrEmpty(sig)) continue;
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
                    if (kv.Value.Any(c => c.ColumnType == 3))
                    {
                        var nos = string.Join(",", kv.Value.Select(c => c.ColumnNo.ToString(CultureInfo.InvariantCulture)));
                        ed?.WriteMessage("\nKOLONDUSEY poligon grup: {0}", nos);
                    }
                }
                ed?.WriteMessage("\nKOLONDUSEY: {0} benzer kolon grubu (1/50, GPR etriye TS500/TBDY2018).", nGrp);
                return true;
            }
            finally
            {
                _ntsDrawFactory = null;
                _kolonDuseyGpr = null;
                _kolonDuseyGprHcr = null;
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
                if (col.ColumnType == 3)
                {
                    int posId = ResolvePolygonPositionSectionId(floor.FloorNo, col.ColumnNo);
                    Geometry localPoly = posId > 0 ? TryPolygonSectionLocalGeometry(posId) : null;
                    if (localPoly != null && !localPoly.IsEmpty)
                    {
                        try { sb.Append(CanonicalPoligonKolonTipImza(localPoly)); }
                        catch
                        {
                            var e3 = localPoly.EnvelopeInternal;
                            sb.AppendFormat(CultureInfo.InvariantCulture, "T{0:0}x{1:0}",
                                Math.Round(Math.Max(e3.Width, e3.Height)),
                                Math.Round(Math.Min(e3.Width, e3.Height)));
                        }
                    }
                    else
                        sb.Append("P3");
                    sb.AppendFormat(CultureInfo.InvariantCulture, ":{0:0}:{1:0};",
                        Math.Round(alt / 5.0) * 5.0, Math.Round(yuk / 5.0) * 5.0);
                    continue;
                }
                var gfPoly = _ntsDrawFactory;
                Geometry poly = gfPoly != null ? GetColumnPolygonForTable(floor, col, 0, 0, gfPoly) : null;
                if (poly != null && !poly.IsEmpty && IsKolonKesitPoligonKesit(poly, poly.EnvelopeInternal))
                {
                    try
                    {
                        sb.Append(CanonicalPoligonKolonImza(poly));
                    }
                    catch
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture, "P{0:0.0}x{1:0.0}",
                            QuantizePoligonCm(poly.EnvelopeInternal.Width),
                            QuantizePoligonCm(poly.EnvelopeInternal.Height));
                    }
                    sb.AppendFormat(CultureInfo.InvariantCulture, ":{0:0.0}:{1:0.0};", alt, yuk);
                    continue;
                }
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
            double s,
            Geometry viewPoly = null,
            double? viewAngleDeg = null,
            bool drawPolygonKesit = true,
            bool skipViewSplit = false,
            bool skipDuseyDonati = false,
            bool polygonArmView = false,
            bool emptyGorunus = false,
            string polygonParcaEtiket = null,
            bool drawDuseyAcilim = false,
            bool duseyAcilimOnly = false,
            int acilimAdetCarpan = 1,
            Dictionary<int, SortedDictionary<int, int>> acilimByDiaOverride = null,
            bool skipKot = false,
            string polygonAcilimEtiket = null)
        {
            var col = cols[0];
            _kolonDuseyGrupColNos = cols.Select(c => c.ColumnNo).Distinct().OrderBy(n => n).ToArray();
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
            if ((firstColFloor == null || firstPoly == null) && col.ColumnType == 3 && gf != null)
            {
                foreach (var fl in _model.Floors)
                {
                    if (!HasColumnOnFloor(fl, col)) continue;
                    int ps = ResolvePolygonPositionSectionId(fl.FloorNo, col.ColumnNo);
                    if (ps <= 0) continue;
                    if (!TryGetPolygonColumn(ps, new Point2d(0, 0), col.AngleDeg, out var pts) || pts == null || pts.Length < 3)
                        continue;
                    var coords = new Coordinate[pts.Length + 1];
                    for (int i = 0; i < pts.Length; i++)
                        coords[i] = new Coordinate(pts[i].X, pts[i].Y);
                    coords[pts.Length] = coords[0];
                    try
                    {
                        firstPoly = gf.CreatePolygon(gf.CreateLinearRing(coords));
                    }
                    catch { firstPoly = null; }
                    if (firstPoly != null && !firstPoly.IsEmpty)
                    {
                        firstColFloor = fl;
                        break;
                    }
                }
            }
            if (firstColFloor == null || firstPoly == null) return 40.0 * s;

            var cxy = firstPoly.Centroid;
            double cx = cxy.X, cy = cxy.Y;
            double planAngDeg = col.AngleDeg;
            double majorAngleDeg = ResolveKolonMajorAngleDeg(firstPoly, col.AngleDeg, cx, cy);
            bool polyKolon = IsKolonKesitPoligonKesit(firstPoly, firstPoly.EnvelopeInternal);
            double polySnapDeg = 0.0;
            if (polyKolon)
            {
                try
                {
                    var rotAlign = AffineTransformation.RotationInstance(-planAngDeg * Math.PI / 180.0, cx, cy);
                    Geometry aligned = rotAlign.Transform(firstPoly);
                    polySnapDeg = PoligonKesitOpeningSnapDeg(aligned);
                }
                catch { polySnapDeg = 0.0; }
            }
            if (!skipViewSplit && viewPoly == null)
            {
                var views = CollectPoligonKolonKolViews(
                    firstPoly, polyKolon ? planAngDeg : majorAngleDeg, cx, cy, polyKolon ? polySnapDeg : 0.0);
                if (views.Count >= 1)
                {
                    double x = origin.X;
                    double acc = 0;
                    double w0 = DrawOneKolonDuseyGroup(
                        tr, btr, db, cols, new Point3d(x, origin.Y, 0), s,
                        viewPoly: null, viewAngleDeg: null,
                        drawPolygonKesit: true, skipViewSplit: true,
                        skipDuseyDonati: true, polygonArmView: false, emptyGorunus: true);
                    x += w0;
                    acc += w0;
                    var liveViews = new List<(Geometry rectPoly, double viewAngDeg, double majorCm, double minorCm, string etiket)>();
                    for (int i = 0; i < views.Count; i++)
                    {
                        var v = views[i];
                        if (PoligonArmGorunusBos(col, v.rectPoly, v.viewAngDeg, cx, cy))
                            continue;
                        liveViews.Add(v);
                    }
                    var names = new List<string>();
                    for (int i = 0; i < liveViews.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(liveViews[i].etiket))
                            names.Add(liveViews[i].etiket);
                    }
                    var rotPlan = AffineTransformation.RotationInstance(-planAngDeg * Math.PI / 180.0, cx, cy);
                    var sumByFloor = new Dictionary<int, SortedDictionary<int, int>>();
                    if (_model?.Floors != null)
                    {
                        for (int fi = 0; fi < _model.Floors.Count; fi++)
                        {
                            if (!HasColumnOnFloor(_model.Floors[fi], col)) continue;
                            Geometry fullF = TryGetKolonFloorPolygon(fi, col) ?? firstPoly;
                            var adet = CollectPoligonArmKesitAdetByDia(fullF, fullF, rotPlan, fi, col.ColumnNo);
                            if (adet != null && adet.Count > 0)
                                sumByFloor[fi] = adet;
                        }
                    }
                    string acilimNames = names.Count > 0 ? string.Join(" + ", names) : null;
                    var armGroups = new List<List<(Geometry rectPoly, double viewAngDeg, double majorCm, double minorCm, string etiket)>>();
                    var armUsed = new bool[liveViews.Count];
                    for (int i = 0; i < liveViews.Count; i++)
                    {
                        if (armUsed[i]) continue;
                        string sig = PoligonArmGorunusTipImza(liveViews[i].majorCm, liveViews[i].minorCm);
                        var grp = new List<(Geometry rectPoly, double viewAngDeg, double majorCm, double minorCm, string etiket)>();
                        grp.Add(liveViews[i]);
                        armUsed[i] = true;
                        for (int j = i + 1; j < liveViews.Count; j++)
                        {
                            if (armUsed[j]) continue;
                            if (PoligonArmGorunusTipImza(liveViews[j].majorCm, liveViews[j].minorCm) != sig)
                                continue;
                            grp.Add(liveViews[j]);
                            armUsed[j] = true;
                        }
                        armGroups.Add(grp);
                    }
                    for (int gi = 0; gi < armGroups.Count; gi++)
                    {
                        var grp = armGroups[gi];
                        var v = grp[0];
                        bool last = gi == armGroups.Count - 1;
                        var etiketler = new List<string>();
                        for (int k = 0; k < grp.Count; k++)
                        {
                            if (!string.IsNullOrEmpty(grp[k].etiket))
                                etiketler.Add(grp[k].etiket);
                        }
                        etiketler.Sort(StringComparer.Ordinal);
                        string stacked = etiketler.Count > 0 ? string.Join("\n", etiketler) : v.etiket;
                        double w = DrawOneKolonDuseyGroup(
                            tr, btr, db, cols, new Point3d(x, origin.Y, 0), s,
                            v.rectPoly, v.viewAngDeg,
                            drawPolygonKesit: false, skipViewSplit: true,
                            skipDuseyDonati: false, polygonArmView: true, emptyGorunus: true,
                            polygonParcaEtiket: stacked,
                            drawDuseyAcilim: last,
                            duseyAcilimOnly: false,
                            acilimAdetCarpan: 1,
                            acilimByDiaOverride: last ? sumByFloor : null,
                            skipKot: false,
                            polygonAcilimEtiket: last ? acilimNames : null);
                        if (last)
                        {
                            x += w;
                            acc += w;
                        }
                        else
                        {
                            x += w + PoligonKesitTakimGorunusAraCm * s;
                            acc += w + PoligonKesitTakimGorunusAraCm * s;
                        }
                    }
                    return acc;
                }
            }
            if (viewAngleDeg.HasValue)
                majorAngleDeg = viewAngleDeg.Value;
            Geometry shaftPoly = viewPoly ?? firstPoly;
            var rot = AffineTransformation.RotationInstance(-majorAngleDeg * Math.PI / 180.0, cx, cy);
            var ident = new AffineTransformation();
            Geometry colRot;
            try { colRot = rot.Transform(shaftPoly); }
            catch { colRot = shaftPoly; }
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
                var polyView = viewPoly ?? polyF;
                double majF = majorAngleDeg;
                if (viewAngleDeg == null && polyF != null && !polyF.IsEmpty)
                {
                    var pc = polyF.Centroid;
                    majF = ResolveKolonMajorAngleDeg(polyF, col.AngleDeg, pc.X, pc.Y);
                }
                double loF = colLo, hiF = colHi;
                if (polyView != null && !polyView.IsEmpty)
                {
                    Geometry pr;
                    try { pr = rot.Transform(polyView); }
                    catch { pr = polyView; }
                    var er = pr.EnvelopeInternal;
                    loF = er.MinX;
                    hiF = er.MaxX;
                }
                stories.Add((ex.altKotCm, ex.altKotCm + ex.yukseklikCm, polyView, majF, loF, hiF, fi));
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
            if (anyLeft) xMin = Math.Min(xMin, colLo - KolonDuseyKenarKesitSolCm);
            if (anyRight) xMax = Math.Max(xMax, colHi + KolonDuseyKenarKesitSagCm);
            if (temelSpans.Count > 0)
            {
                xMin = Math.Min(xMin, colLo - kenarKesitCm);
                xMax = Math.Max(xMax, colHi + kenarKesitCm);
            }
            xMin = Math.Min(xMin, colLo - KolonDuseyKatHizaKenarBoslukCm);
            xMax = Math.Max(xMax, colHi + KolonDuseyKatHizaKenarBoslukCm);
            foreach (var t in temelSpans)
            {
                xMin = Math.Min(xMin, t.x0);
                xMax = Math.Max(xMax, t.x1);
            }

            double maxPlanW = 40.0 * s;
            double maxPlanH = 40.0 * s;
            foreach (var st in stories)
            {
                if (st.poly == null || st.poly.IsEmpty) continue;
                var pc = st.poly.Centroid;
                var rPlan = AffineTransformation.RotationInstance(-st.majorDeg * Math.PI / 180.0, pc.X, pc.Y);
                Geometry gPlan;
                try { gPlan = rPlan.Transform(st.poly); }
                catch { gPlan = st.poly; }
                var ePlan = gPlan.EnvelopeInternal;
                maxPlanW = Math.Max(maxPlanW, ePlan.Width);
                maxPlanH = Math.Max(maxPlanH, ePlan.Height);
            }

            double nameW = 36.0 * s;
            double kotBand = 40.0 * s + Kolon50GorunusDikeyOlcuSagaCm;
            const double planKesitGapFromElevCm = 60.0;
            bool polyKesitYer = drawPolygonKesit
                && firstPoly != null && !firstPoly.IsEmpty
                && IsKolonKesitPoligonKesit(firstPoly, firstPoly.EnvelopeInternal);
            bool kesitOnly = emptyGorunus && polyKesitYer && !polygonArmView;
            double yanPay = polyKesitYer ? PoligonKesitTakimYanPayCm : 0.0;
            double ustPay = polyKesitYer ? PoligonKesitTakimUstPayCm : 0.0;
            double altPay = polyKesitYer ? PoligonKesitTakimAltPayCm : 0.0;
            double takimW = maxPlanW + 2.0 * yanPay;
            double takimH = maxPlanH + ustPay + altPay;
            int nKesitCol = 1;
            if (polyKesitYer && stories.Count > 1)
            {
                double elevH = Math.Max(300.0, (zMax - zMin) * s);
                double gorH = Math.Max(elevH, PoligonKesitKompaktMaxYukseklikCm);
                int nSt = stories.Count;
                nKesitCol = 1;
                for (int c = 1; c <= nSt && c <= 6; c++)
                {
                    int nRow = (nSt + c - 1) / c;
                    double gh = nRow * takimH + Math.Max(0, nRow - 1) * PoligonKesitTakimAraCm;
                    nKesitCol = c;
                    if (gh <= gorH + 50.0) break;
                }
            }
            double planBand = (emptyGorunus && !polyKesitYer)
                ? 0.0
                : (polyKesitYer
                    ? nKesitCol * takimW + Math.Max(0, nKesitCol - 1) * PoligonKesitTakimAraCm
                        + PoligonKesitTakimGorunusSolBoslukCm
                    : Kolon50GorunusCiftOlcuAraCm + maxPlanW + planKesitGapFromElevCm);
            if (kesitOnly)
            {
                nameW = 36.0 * s;
                kotBand = 0.0;
            }
            else if (polygonArmView && emptyGorunus)
            {
                nameW = 0.0;
                kotBand = 0.0;
            }
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
            var etriyeKiris = new List<(double x0, double x1, double zb, double zt)>(sapKirisRuns);
            bool doDuseyAcilim = drawDuseyAcilim || (!skipDuseyDonati && !polygonArmView && !duseyAcilimOnly);
            if (duseyAcilimOnly)
            {
                double xAcilim = origin.X + 10.0 * s;
                double xEnd = DrawKolonDuseyDonatiAcilim(
                    tr, btr, col, stories, rot, X, Y, zDrawBot, temelSpans, etriyeKiris, xAcilim,
                    polygonArmView, acilimAdetCarpan, acilimByDiaOverride);
                if (!string.IsNullOrEmpty(polygonParcaEtiket))
                {
                    double yLab = Y(zMin) - 24.0 * s;
                    DrawPoligonKolGorunusTipEtiketleri(tr, btr, db,
                        0.5 * (xAcilim + xEnd), yLab, polygonParcaEtiket, s);
                }
                return Math.Max(KolonDuseyAcilimWidthCm, xEnd - origin.X + 20.0 * s);
            }

            if (kesitOnly)
            {
                int nSt = stories.Count;
                int nRow = nKesitCol > 0 ? (nSt + nKesitCol - 1) / nKesitCol : nSt;
                double yGridBot = Y(zDrawBot);
                for (int si = 0; si < nSt; si++)
                {
                    var st = stories[si];
                    Geometry overlayPoly = null;
                    int overlayFloor = -1;
                    if (si + 1 < nSt)
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
                    Geometry kesitPoly = firstPoly;
                    double kesitAng = col.AngleDeg - polySnapDeg;
                    var flKesit = st.floorIndex >= 0 && st.floorIndex < _model.Floors.Count
                        ? _model.Floors[st.floorIndex] : firstColFloor;
                    var fullKesit = flKesit != null
                        ? GetColumnPolygonForTable(flKesit, col, 0, 0, gf) : null;
                    if (fullKesit != null && !fullKesit.IsEmpty) kesitPoly = fullKesit;
                    int colI = si % nKesitCol;
                    int rowI = si / nKesitCol;
                    double xKesit = origin.X + (colI + 1) * takimW + colI * PoligonKesitTakimAraCm
                        - PoligonKesitTakimSolaKaydirCm;
                    double yKesit = yGridBot + rowI * (takimH + PoligonKesitTakimAraCm) + altPay + maxPlanH;
                    DrawKolonDuseyFloorPlanKesit(
                        tr, btr, kesitPoly, kesitAng,
                        xKesit, yKesit,
                        st.floorIndex, col.ColumnNo, overlayPoly, overlayFloor, h16Plan,
                        null, st.zBot, st.zTop, null, st.zBot, false);
                }
                double gridH = nRow * takimH + Math.Max(0, nRow - 1) * PoligonKesitTakimAraCm;
                double yNameK = yGridBot + gridH + 12.0 * s;
                int[] grupNos = _kolonDuseyGrupColNos != null && _kolonDuseyGrupColNos.Length > 0
                    ? _kolonDuseyGrupColNos
                    : new[] { col.ColumnNo };
                foreach (int n in grupNos)
                {
                    DrawBeamLabel(tr, btr, db, new Point3d(origin.X + 4.0 * s - PoligonKesitTakimSolaKaydirCm, yNameK, 0),
                        "S" + n.ToString(CultureInfo.InvariantCulture),
                        10.0 * s, 0.0, LayerKolonIsmi, bottomLeftAligned: true);
                    yNameK -= 14.0 * s;
                }
                double gridW = nKesitCol * takimW + Math.Max(0, nKesitCol - 1) * PoligonKesitTakimAraCm;
                return gridW + PoligonKesitTakimGorunusSolBoslukCm;
            }

            var segs = new GorunusLineBag();
            for (int i = 0; i < stories.Count; i++)
            {
                var st = stories[i];
                double y0 = Y(i == 0 ? zDrawBot : st.zBot);
                double y1 = Y(st.zTop);
                string shaftLayer = StoryIsDepremPerde(st.poly, rot) ? LayerPerde : LayerKolon;
                segs.AddV(X(st.lo), y0, y1, shaftLayer, KenarYHoles(Y, st.lo, st.hi, left: true, beamRuns));
                segs.AddV(X(st.hi), y0, y1, shaftLayer, KenarYHoles(Y, st.lo, st.hi, left: false, beamRuns));
                if (i + 1 < stories.Count)
                {
                    var up = stories[i + 1];
                    double yJ = Y(st.zTop);
                    string jointLayer = (StoryIsDepremPerde(st.poly, rot) || StoryIsDepremPerde(up.poly, rot)) ? LayerPerde : LayerKolon;
                    if (Math.Abs(up.lo - st.lo) > 2.0)
                        segs.AddH(X(Math.Min(st.lo, up.lo)), X(Math.Max(st.lo, up.lo)), yJ, jointLayer, null);
                    if (Math.Abs(up.hi - st.hi) > 2.0)
                        segs.AddH(X(Math.Min(st.hi, up.hi)), X(Math.Max(st.hi, up.hi)), yJ, jointLayer, null);
                }
                else
                    segs.AddH(X(st.lo), X(st.hi), y1, shaftLayer, null);
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
            DrawKenarSpanOutlines(Ln, X, Y, colLo, colHi, wallRuns, LayerPerde,
                KolonDuseyKenarKesitSolCm, KolonDuseyKenarKesitSagCm, mergeStories: true, FaceAt);
            DrawKenarSpanOutlines(Ln, X, Y, colLo, colHi, beamRuns, LayerKiris,
                KolonDuseyKenarKesitSolCm, KolonDuseyKenarKesitSagCm, mergeStories: false, FaceAt);
            // Etriye / düşey donatı: tüm saplanan kirişler (görünüş süzgeci yok).
            if (!skipDuseyDonati)
            {
                DrawKolonGorunusDuseyDonatilar(tr, btr, col, stories, rot, X, Y, zDrawBot, temelSpans, etriyeKiris, polygonArmView);
                DrawPerdeGorunusDuseyDonatilar(tr, btr, col, stories, rot, X, Y, zDrawBot, temelSpans, etriyeKiris, polygonArmView);
            }
            var etriyeZs = new List<double>();
            var etriyeBolgeler = new List<(double zLo, double zHi, int sCm, int diaMm)>();
            var govdeYatayBolgeler = new List<(double zLo, double zHi, int sCm, int diaMm)>();
            if (!emptyGorunus || polygonArmView)
            {
                DrawKolonGorunusEtriyeler(tr, btr, col, stories, rot, X, Y, zDrawBot, etriyeKiris, Ln, temelSpans, etriyeZs, etriyeBolgeler, polygonArmView);
                DrawPerdeGorunusEtriyeler(tr, btr, col, stories, rot, X, Y, zDrawBot, etriyeKiris, Ln, temelSpans, etriyeZs, etriyeBolgeler, govdeYatayBolgeler, polygonArmView);
            }

            var katHizaZs = new HashSet<double>();
            foreach (var st in stories)
            {
                katHizaZs.Add(Math.Round(st.zBot, 3));
                katHizaZs.Add(Math.Round(st.zTop, 3));
            }
            if (temelSpans.Count > 0)
                katHizaZs.Add(Math.Round(temelSpans.Min(t => t.z0), 3));

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
                null, temelSpans.Count > 0, kenarZsRight, kenarZsLeft, X(colLo), X(colHi), etriyeZs, etriyeBolgeler, govdeYatayBolgeler);
            double xOlcuSag = X(colHi) + KolonDuseyGorunusSagOlcuYiginCm(
                govdeYatayBolgeler != null && govdeYatayBolgeler.Count > 0);
            double xDonatiSag = xOlcuSag;
            if (doDuseyAcilim)
            {
                double xAcilim = xOlcuSag + KolonDuseyAcilimGapFromOlcuCm;
                xDonatiSag = DrawKolonDuseyDonatiAcilim(
                    tr, btr, col, stories, rot, X, Y, zDrawBot, temelSpans, etriyeKiris, xAcilim,
                    polygonArmView, acilimAdetCarpan, acilimByDiaOverride);
                if (!string.IsNullOrEmpty(polygonAcilimEtiket))
                {
                    double yLabA = Y(zMin) - 24.0 * s;
                    DrawBeamLabel(tr, btr, db, new Point3d(0.5 * (xAcilim + xDonatiSag), yLabA, 0),
                        polygonAcilimEtiket, 12.0 * s, 0.0, LayerYazi, useMiddleCenter: true, colorAci: 14);
                }
            }
            double apexKotX = xDonatiSag + KolonDuseyKotAcilimSagCm;
            if (!skipKot)
            {
                DrawPerdeGorunusKots(tr, btr, apexKotX, Y, kotZs, xIsApex: true);
                double xKotLeft = apexKotX - KesitKotExtTowardSectionCm;
                foreach (var zKat in katHizaZs)
                {
                    double faceLo = FaceAt(true, zKat - 1.0, zKat + 1.0);
                    double x0 = X(faceLo);
                    double x1 = xKotLeft - KolonDuseyKatHizaKenarBoslukCm;
                    if (x1 - x0 >= 2.0)
                        Ln(x0, Y(zKat), x1, Y(zKat), LayerKesitGorunus);
                }
            }

            if (!emptyGorunus)
            {
                for (int si = 0; si < stories.Count; si++)
                {
                    var st = stories[si];
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
                    double zEtAdetBot = st.zBot;
                    if (si == 0 && etriyeZs != null && etriyeZs.Count > 0)
                    {
                        double zMinEt = etriyeZs[0];
                        foreach (double z in etriyeZs)
                        {
                            if (z < zMinEt) zMinEt = z;
                        }
                        if (zMinEt < zEtAdetBot - 0.5)
                            zEtAdetBot = zMinEt;
                    }
                    Geometry kesitPoly = st.poly;
                    double kesitAng = st.majorDeg;
                    if (drawPolygonKesit)
                    {
                        var flKesit = st.floorIndex >= 0 && st.floorIndex < _model.Floors.Count
                            ? _model.Floors[st.floorIndex] : firstColFloor;
                        var fullKesit = flKesit != null
                            ? GetColumnPolygonForTable(flKesit, col, 0, 0, gf) : null;
                        if (fullKesit == null || fullKesit.IsEmpty) fullKesit = firstPoly;
                        kesitPoly = fullKesit;
                        if (fullKesit != null && !fullKesit.IsEmpty
                            && IsKolonKesitPoligonKesit(fullKesit, fullKesit.EnvelopeInternal))
                            kesitAng = col.AngleDeg;
                        else if (fullKesit != null && !fullKesit.IsEmpty)
                        {
                            var pcK = fullKesit.Centroid;
                            kesitAng = ResolveKolonMajorAngleDeg(fullKesit, col.AngleDeg, pcK.X, pcK.Y);
                        }
                    }
                    double xKesit = X(st.lo) - KolonDuseyKesitSoldaCm;
                    double yKesit = Y(st.zTop) - KolonDuseyKesitUstKottanAsagiCm;
                    if (polyKesitYer && nKesitCol > 1)
                        xKesit -= (si % nKesitCol) * (takimW + PoligonKesitTakimAraCm);
                    DrawKolonDuseyFloorPlanKesit(
                        tr, btr, kesitPoly, kesitAng,
                        xKesit,
                        yKesit,
                        st.floorIndex, col.ColumnNo, overlayPoly, overlayFloor, h16Plan,
                        etriyeBolgeler, st.zBot, st.zTop, etriyeZs, zEtAdetBot, polygonArmView);
                }
            }

            if (!(polygonArmView && emptyGorunus))
            {
                double yName = Y(zMax) - 8.0 * s;
                double xName = origin.X + planBand + 4.0 * s;
                foreach (var c in cols)
                {
                    DrawBeamLabel(tr, btr, db, new Point3d(xName, yName, 0),
                        "S" + c.ColumnNo.ToString(CultureInfo.InvariantCulture),
                        10.0 * s, 0.0, LayerKolonIsmi, bottomLeftAligned: true);
                    yName -= 14.0 * s;
                }
            }
            if (!string.IsNullOrEmpty(polygonParcaEtiket))
            {
                double xLab = 0.5 * (X(stories[0].lo) + X(stories[0].hi));
                double yLab = Y(zMin) - 24.0 * s;
                DrawPoligonKolGorunusTipEtiketleri(tr, btr, db, xLab, yLab, polygonParcaEtiket, s);
            }

            if (polygonArmView && emptyGorunus)
            {
                double xRight = Math.Max(X(xMax), xOlcuSag);
                if (doDuseyAcilim)
                    xRight = Math.Max(xRight, xDonatiSag);
                if (!skipKot)
                    xRight = Math.Max(xRight, apexKotX + 40.0 * s);
                return Math.Max(40.0 * s, xRight - origin.X);
            }

            return planBand + nameW + kotBand + (xMax - xMin) * s
                + PerdeGorunusSagTasimCm + KolonDuseyGorunusSagOlcuYiginCm(true)
                + (doDuseyAcilim ? KolonDuseyAcilimGapFromOlcuCm + KolonDuseyAcilimWidthCm : 0.0)
                + KolonDuseyKotAcilimSagCm;
        }

        /// <summary>
        /// Görünüş sağı: düşey donatı açılımı (kolon / perde / perde başlığı).
        /// En sağ ölçüden sonra; kotlar en sağ donatı çizgisinden 60 cm sağda.
        /// Dönen değer: çizilen en sağ düşey donatı çizgisinin X’i.
        /// </summary>
        private double DrawKolonDuseyDonatiAcilim(
            Transaction tr,
            BlockTableRecord btr,
            ColumnAxisInfo col,
            List<(double zBot, double zTop, Geometry poly, double majorDeg, double lo, double hi, int floorIndex)> stories,
            AffineTransformation rot,
            Func<double, double> X,
            Func<double, double> Y,
            double zDrawBot,
            List<(double x0, double x1, double z0, double z1)> temelSpans,
            List<(double x0, double x1, double zb, double zt)> beamRuns,
            double x0,
            bool polygonArmView = false,
            int acilimAdetCarpan = 1,
            Dictionary<int, SortedDictionary<int, int>> byDiaOverride = null)
        {
            if (tr == null || btr == null || col == null || stories == null || stories.Count == 0 || Y == null)
                return x0;
            const double pas = 4.0;
            const double rBend = 2.0;
            const double k90 = 0.41421356237;
            const double txtH = 10.0;
            double fck = _rebarFckMPa > 16.0 ? _rebarFckMPa : 30.0;
            double fyk = _rebarFykMPa > 200.0 ? _rebarFykMPa : 420.0;
            bool hasTemel = temelSpans != null && temelSpans.Count > 0;
            double zTemelBot = hasTemel ? temelSpans.Min(t => t.z0) : stories[0].zBot;
            ObjectId dimId = GetOrCreatePlanOlcuDimStyle(tr, btr.Database, 10.0, 1.0, PlanOlcuDonatiDimStyleName);

            void DimZ(double xFace, double za, double zb, double xLine)
            {
                if (Math.Abs(zb - za) < 2.0) return;
                double ya = Y(Math.Min(za, zb));
                double yb = Y(Math.Max(za, zb));
                var dim = new AlignedDimension(
                    new Point3d(xFace, ya, 0), new Point3d(xFace, yb, 0),
                    new Point3d(xLine, (ya + yb) * 0.5, 0), "", dimId)
                {
                    Layer = LayerOlcu,
                    LineWeight = LineWeight.LineWeight020
                };
                try { dim.DimfxlenOn = true; } catch { }
                try { dim.Dimfxlen = 18.0; } catch { }
                AppendEntity(tr, btr, dim);
            }

            void Label(double x, double zMid, int n, int dia, double L, bool filizTag = false)
            {
                string s = string.Format(CultureInfo.InvariantCulture, "{0}\u00F8{1} L={2:0}", n, dia, L);
                if (filizTag) s += " FILIZ";
                DrawBeamLabel(tr, btr, btr.Database, new Point3d(x - 12.0 + KolonDuseyAcilimDuseyEtiketSagaCm, Y(zMid), 0),
                    s, txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
            }

            var kinds = new int[stories.Count]; // 0 kolon, 1 perde, 2 başlık
            var th = new double[stories.Count];
            var byDia = new SortedDictionary<int, int>[stories.Count];
            var faceBars = new List<(double x, int dia)>[stories.Count];
            var allPtsDia = new List<(Point2d p, int dia)>[stories.Count];
            var env = new Envelope[stories.Count];
            for (int i = 0; i < stories.Count; i++)
            {
                th[i] = 25.0;
                byDia[i] = new SortedDictionary<int, int>();
                faceBars[i] = new List<(double x, int dia)>();
                allPtsDia[i] = new List<(Point2d p, int dia)>();
                var st = stories[i];
                if (st.poly == null || st.poly.IsEmpty) continue;
                Geometry g;
                try { g = rot != null ? rot.Transform(st.poly) : st.poly; }
                catch { g = st.poly; }
                var e = g.EnvelopeInternal;
                env[i] = e;
                if (IsKolonKesitPoligonKesit(g, e)) continue;
                double longCm = Math.Max(e.Width, e.Height);
                double shortCm = Math.Min(e.Width, e.Height);
                th[i] = Math.Max(8.0, shortCm);
                if (IsDepremPerdeBoyOrani(longCm, shortCm)) kinds[i] = 1;
                else if (polygonArmView
                    || (shortCm > 1.0 && shortCm < KolonKesitPerdeBasligiMaxKenarCm - 0.01
                        && longCm < KolonKesitPerdeMinBoyOrani * shortCm - 0.01))
                    kinds[i] = 2;
                string donati = null;
                if (_kolonDuseyGpr != null && _model?.Floors != null)
                    KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(
                        _kolonDuseyGpr, _model.Floors, st.floorIndex, col.ColumnNo, out _, out donati, out _);
                if (!KolonDonatiTableDrawer.TryParseKolonKesitDuseyDonatiByDia(donati, out byDia[i]) || byDia[i].Count == 0)
                {
                    int dia0 = 14;
                    KolonDonatiTableDrawer.SumKolonKesitDuseyDonatiAdet(donati, out int d);
                    if (d >= 6) dia0 = d;
                    int n0 = kinds[i] == 1 ? 18 : 8;
                    byDia[i] = new SortedDictionary<int, int> { [dia0] = n0 };
                }
                if (kinds[i] == 1)
                {
                    if (polygonArmView)
                    {
                        Geometry fullF = TryGetKolonFloorPolygon(st.floorIndex, col) ?? st.poly;
                        faceBars[i] = CollectPoligonArmGorunusBars(fullF, st.poly, rot, st.floorIndex, col.ColumnNo);
                        allPtsDia[i] = new List<(Point2d p, int dia)>(faceBars[i].Count);
                        byDia[i] = CollectPoligonArmKesitAdetByDia(fullF, st.poly, rot, st.floorIndex, col.ColumnNo);
                        for (int k = 0; k < faceBars[i].Count; k++)
                        {
                            int d = faceBars[i][k].dia >= 6 ? faceBars[i][k].dia : 14;
                            allPtsDia[i].Add((new Point2d(faceBars[i][k].x, 0.5 * (e.MinY + e.MaxY)), d));
                        }
                    }
                    else
                    {
                        faceBars[i] = CollectPerdeGorunusLongFaceBars(g, st.floorIndex, col.ColumnNo);
                        allPtsDia[i] = CollectPerdeKesitBarPoints(e, st.floorIndex, col.ColumnNo)
                            ?? new List<(Point2d p, int dia)>();
                        OverlayPerdeKesitDuseyAdet(byDia[i], e, st.floorIndex, col.ColumnNo);
                    }
                }
                else
                {
                    if (polygonArmView)
                    {
                        Geometry fullF = TryGetKolonFloorPolygon(st.floorIndex, col) ?? st.poly;
                        faceBars[i] = CollectPoligonArmGorunusBars(fullF, st.poly, rot, st.floorIndex, col.ColumnNo);
                        allPtsDia[i] = new List<(Point2d p, int dia)>(faceBars[i].Count);
                        byDia[i] = CollectPoligonArmKesitAdetByDia(fullF, st.poly, rot, st.floorIndex, col.ColumnNo);
                        for (int k = 0; k < faceBars[i].Count; k++)
                        {
                            int d = faceBars[i][k].dia >= 6 ? faceBars[i][k].dia : 14;
                            allPtsDia[i].Add((new Point2d(faceBars[i][k].x, 0.5 * (e.MinY + e.MaxY)), d));
                        }
                    }
                    else
                    {
                        faceBars[i] = CollectKolonGorunusLongFaceBars(g, st.floorIndex, col.ColumnNo);
                        allPtsDia[i] = CollectKolonKesitBarPointsWithDia(e, st.floorIndex, col.ColumnNo);
                        OverlayKolonBaslikDuseyAdet(st.floorIndex, col.ColumnNo, ref byDia[i]);
                    }
                }
                if (byDiaOverride != null && byDiaOverride.TryGetValue(st.floorIndex, out var ovDia) && ovDia != null && ovDia.Count > 0)
                    byDia[i] = ovDia;
            }

            if (acilimAdetCarpan > 1 && byDiaOverride == null)
            {
                for (int i = 0; i < byDia.Length; i++)
                {
                    if (byDia[i] == null || byDia[i].Count == 0) continue;
                    var keys = new List<int>(byDia[i].Keys);
                    for (int k = 0; k < keys.Count; k++)
                        byDia[i][keys[k]] *= acilimAdetCarpan;
                }
            }

            int ScaleFace(int facePart, int faceAll, int gprN)
            {
                if (facePart <= 0) return 0;
                if (faceAll <= 0 || gprN <= 0) return facePart;
                int s = (int)Math.Round(facePart * (double)gprN / faceAll);
                if (s < 1) s = 1;
                if (gprN > 0 && s > gprN) s = gprN;
                return s;
            }

            var diaOrder = new List<int>();
            foreach (var d in byDia)
            {
                if (d == null) continue;
                foreach (int k in d.Keys)
                    if (!diaOrder.Contains(k)) diaOrder.Add(k);
            }
            diaOrder.Sort((a, b) => b.CompareTo(a));
            if (diaOrder.Count == 0) return x0;
            double xBarLineMax = x0;
            double xKesitExtra0 = x0 + diaOrder.Count * KolonDuseyAcilimColGapCm;
            int extraSlot = 0;
            double NextExtraX()
            {
                double x = xKesitExtra0 + extraSlot * KolonDuseyAcilimKesitExtraGapCm;
                extraSlot++;
                if (x > xBarLineMax) xBarLineMax = x;
                return x;
            }

            for (int di = 0; di < diaOrder.Count; di++)
            {
                int dia = diaOrder[di];
                double xBar = x0 + di * KolonDuseyAcilimColGapCm;
                double hookMax = 20.0;
                int sasirIdx = 0;
                double NextX()
                {
                    double x = xBar + (sasirIdx % 2 == 0 ? 0.0 : KolonDuseyAcilimSasirCm);
                    sasirIdx++;
                    if (x > xBarLineMax) xBarLineMax = x;
                    return x;
                }

                for (int i = 0; i < stories.Count; i++)
                {
                    int n = 0;
                    bool hasThis = byDia[i] != null && byDia[i].TryGetValue(dia, out n) && n > 0;
                    var st = stories[i];
                    bool last = i == stories.Count - 1;
                    bool first = i == 0;
                    bool perdeLike = kinds[i] == 1 || kinds[i] == 2;
                    bool kolonLike = kinds[i] == 0;
                    double zCbot = first ? zDrawBot : st.zBot;
                    double zNet = KolonNetYukseklikUstKot(beamRuns, st.lo, st.hi, zCbot, st.zTop);
                    if (zNet < zCbot + 2.0) zNet = zCbot + 2.0;
                    if (zNet > st.zTop) zNet = st.zTop;
                    double lb = Ts500KenetlenmeLbCm(dia, fck, fyk);
                    double lap = CeilTo5Cm(Math.Max(perdeLike ? 1.50 * lb : lb, 30.0));
                    KolonOrtUcdeBindirme(zCbot, zNet, CeilTo5Cm(Math.Max(lb, 30.0)), out double spBot, out double spTop);
                    double hookIn = Math.Max(2.0 * rBend + 1.0, th[i] - 2.0 * pas);
                    double phi12 = CeilTo5Cm(12.0 * dia / 10.0);
                    double bHook = phi12;
                    if (hasTemel)
                    {
                        double a = zCbot - (zTemelBot + 5.0);
                        double lbk = 0.75 * lb;
                        if (a + bHook < lbk) bHook = CeilTo5Cm(Math.Max(bHook, lbk - Math.Max(a, 0)));
                    }
                    if (bHook > hookMax) hookMax = bHook;
                    if (hookIn > hookMax) hookMax = hookIn;
                    bool nextExists = !last && i + 1 < stories.Count && stories[i + 1].poly != null && !stories[i + 1].poly.IsEmpty;
                    double zHoriz = st.zTop - pas;
                    if (zHoriz < zCbot + 15.0) zHoriz = zCbot + 20.0;
                    double hStory = st.zTop - zCbot;
                    bool combine = hasThis && first && hasTemel && hStory <= 200.0 + 1e-6;
                    double zStart = perdeLike ? zCbot : spBot;

                    int nCont = hasThis ? n : 0;
                    int nStop = 0;
                    int nFilizEx = 0;
                    double zFilizEx0 = 0, zFilizEx1 = 0;
                    ResolveFinisGonyeFirkete(dia, hookIn, rBend, out bool extraFirkete, out double extraB, out double extraC);

                    bool nextPerde = nextExists && kinds[i + 1] == 1;
                    bool nextKolon = nextExists && (kinds[i + 1] == 0 || kinds[i + 1] == 2);
                    bool kesitDegisti = nextExists && KolonKesitPlanFarkli(st.poly, stories[i + 1].poly, st.majorDeg);
                    double h16 = st.zTop - zNet;
                    if (h16 < 6.0) h16 = 6.0;
                    double upLo = nextExists ? stories[i + 1].lo : st.lo;
                    double upHi = nextExists ? stories[i + 1].hi : st.hi;
                    const double kolonIcKenarCm = 5.5;

                    if (nextExists && faceBars[i] != null)
                    {
                        var lo = faceBars[i];
                        var up = faceBars[i + 1] ?? new List<(double x, int dia)>();
                        int[] matchUp = null;
                        if (kinds[i] == 1)
                        {
                            if (nextKolon)
                                matchUp = MatchSameDiaBarsTbdY16(lo, up, h16);
                            else if (nextPerde && kesitDegisti)
                                matchUp = MatchKolonBarsTbdY16(lo.Select(b => b.x).ToList(), up.Select(b => b.x).ToList(), h16);
                        }
                        else
                            matchUp = MatchKolonBarsTbdY16(lo.Select(b => b.x).ToList(), up.Select(b => b.x).ToList(), h16);

                        if (matchUp != null && hasThis)
                        {
                            int nStopFace = 0;
                            int nFaceDia = 0;
                            for (int b = 0; b < lo.Count; b++)
                            {
                                int dB = lo[b].dia >= 6 ? lo[b].dia : 14;
                                if (dB != dia) continue;
                                nFaceDia++;
                                int upIdx = b < matchUp.Length ? matchUp[b] : -1;
                                bool matched = upIdx >= 0 && upIdx < up.Count;
                                bool insideUp = nextKolon && kinds[i] == 1
                                    ? (lo[b].x >= upLo + kolonIcKenarCm && lo[b].x <= upHi - kolonIcKenarCm)
                                    : (lo[b].x >= upLo + pas && lo[b].x <= upHi - pas);
                                if (!matched && !insideUp) nStopFace++;
                            }
                            nStop = ScaleFace(nStopFace, nFaceDia, n);
                            if (nStop > n) nStop = n;
                            nCont = Math.Max(0, n - nStop);
                        }
                    }

                    if (nextExists)
                    {
                        var matchedUpX = new List<double>();
                        var upFace = faceBars[i + 1] ?? new List<(double x, int dia)>();
                        var loXs = (faceBars[i] ?? new List<(double x, int dia)>()).Select(b => b.x).ToList();
                        var upXs = upFace.Select(b => b.x).ToList();
                        int[] matchX = kinds[i] == 1 && nextKolon
                            ? MatchSameDiaBarsTbdY16(faceBars[i] ?? new List<(double x, int dia)>(), upFace, h16)
                            : MatchKolonBarsTbdY16(loXs, upXs, h16);
                        if (matchX != null)
                        {
                            for (int b = 0; b < matchX.Length; b++)
                            {
                                int ui = matchX[b];
                                if (ui >= 0 && ui < upXs.Count)
                                    matchedUpX.Add(upXs[ui]);
                            }
                        }
                        bool countFiliz = (kinds[i] == 1 && nextKolon) || kinds[i] != 1;
                        if (countFiliz && allPtsDia[i + 1] != null && allPtsDia[i + 1].Count > 0)
                        {
                            nFilizEx = CountKesitDegisimUnmatchedBars(
                                allPtsDia[i], allPtsDia[i + 1], matchedUpX, env[i + 1], dia);
                            if (acilimAdetCarpan > 1 && nFilizEx > 0)
                                nFilizEx *= acilimAdetCarpan;
                            if (nFilizEx > 0)
                            {
                                double eFiliz = CeilTo5Cm(Math.Max(1.50 * lb, kinds[i] == 1 ? 30.0 : 40.0 * dia / 10.0));
                                zFilizEx0 = st.zTop - eFiliz;
                                if (zFilizEx0 < zCbot + 5.0) zFilizEx0 = zCbot + 5.0;
                                double lbUp = CeilTo5Cm(Math.Max(lb, 30.0));
                                double zUpBot = stories[i + 1].zBot;
                                double zUpNet = KolonNetYukseklikUstKot(
                                    beamRuns, stories[i + 1].lo, stories[i + 1].hi, zUpBot, stories[i + 1].zTop);
                                if (zUpNet < zUpBot + 2.0) zUpNet = zUpBot + 2.0;
                                KolonOrtUcdeBindirme(zUpBot, zUpNet, lbUp, out double spBup, out zFilizEx1);
                                if (kinds[i] != 1)
                                    zFilizEx1 = spBup + lbUp;
                                if (zFilizEx1 < st.zTop + lbUp)
                                    zFilizEx1 = st.zTop + lbUp;
                            }
                        }
                    }

                    if (byDiaOverride != null)
                    {
                        nStop = 0;
                        nFilizEx = 0;
                        nCont = hasThis ? n : 0;
                    }

                    void DimKolonLb(double x)
                    {
                        DimZ(x, zCbot, spBot, x + 18.0);
                        DimZ(x, spBot, spTop, x + 18.0);
                    }
                    void DimPerdeFiliz(double x)
                    {
                        DimZ(x, zCbot, zCbot + lap, x + 18.0);
                    }
                    void DimPerdeKatGecis(double x, double zEnd)
                    {
                        if (!perdeLike || nextKolon) return;
                        double zLapTop = st.zTop + lap;
                        double zDimTop = Math.Min(zEnd, zLapTop);
                        if (zDimTop - st.zTop >= 8.0)
                            DimZ(x, st.zTop, zDimTop, x + 18.0);
                    }
                    void HookTxt(double x, double z, double len, bool horiz)
                    {
                        DrawBeamLabel(tr, btr, btr.Database,
                            new Point3d(horiz ? x + len * 0.5 : x + 8.0,
                                horiz ? Y(z) + 5.0 + KolonDuseyAcilimFilizGonyeYaziYukariCm : Y(z), 0),
                            len.ToString("0", CultureInfo.InvariantCulture),
                            txtH, horiz ? 0.0 : Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                    }

                    bool drewFiliz = false;
                    if (hasThis && first && hasTemel)
                    {
                        double zFbot = zTemelBot + 5.0;
                        double zFtop = kolonLike ? spTop : zCbot + lap;
                        if (combine)
                        {
                            if (last || !nextExists || nCont > 0)
                            {
                                double xUse = NextX();
                                if (last || !nextExists)
                                {
                                    if (extraFirkete)
                                    {
                                        AppendDonatiPline(tr, btr, new[]
                                        {
                                            new Point2d(xUse + bHook, Y(zFbot)),
                                            new Point2d(xUse + rBend, Y(zFbot)),
                                            new Point2d(xUse, Y(zFbot + rBend)),
                                            new Point2d(xUse, Y(zHoriz - rBend)),
                                            new Point2d(xUse + rBend, Y(zHoriz)),
                                            new Point2d(xUse + (extraB - rBend), Y(zHoriz)),
                                            new Point2d(xUse + extraB, Y(zHoriz - rBend)),
                                            new Point2d(xUse + extraB, Y(zHoriz - extraC))
                                        }, new[] { 0.0, -k90, 0.0, -k90, 0.0, -k90, 0.0, 0.0 });
                                        Label(xUse, (zCbot + zHoriz) * 0.5, n, dia, CeilTo5Cm((zHoriz - zFbot) + bHook + extraB + extraC));
                                        HookTxt(xUse, zFbot, bHook, true);
                                        DrawBeamLabel(tr, btr, btr.Database, new Point3d(xUse + extraB * 0.5, Y(zHoriz) + 8.0, 0),
                                            extraB.ToString("0", CultureInfo.InvariantCulture), txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                                        DrawBeamLabel(tr, btr, btr.Database, new Point3d(xUse + extraB + 8.0, Y(zHoriz - extraC * 0.5), 0),
                                            extraC.ToString("0", CultureInfo.InvariantCulture), txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                                    }
                                    else
                                    {
                                        AppendDonatiPline(tr, btr, new[]
                                        {
                                            new Point2d(xUse + bHook, Y(zFbot)),
                                            new Point2d(xUse + rBend, Y(zFbot)),
                                            new Point2d(xUse, Y(zFbot + rBend)),
                                            new Point2d(xUse, Y(zHoriz - rBend)),
                                            new Point2d(xUse + rBend, Y(zHoriz)),
                                            new Point2d(xUse + extraB, Y(zHoriz))
                                        }, new[] { 0.0, -k90, 0.0, -k90, 0.0, 0.0 });
                                        Label(xUse, (zCbot + zHoriz) * 0.5, n, dia, CeilTo5Cm((zHoriz - zFbot) + bHook + extraB));
                                        HookTxt(xUse, zFbot, bHook, true);
                                        DrawBeamLabel(tr, btr, btr.Database, new Point3d(xUse + extraB * 0.5, Y(zHoriz) + 8.0, 0),
                                            extraB.ToString("0", CultureInfo.InvariantCulture), txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                                    }
                                }
                                else
                                {
                                    double zEnd = kolonLike ? spTop : st.zTop + lap;
                                    if (nextExists && kinds[i] == 1 && kinds[i + 1] == 0)
                                    {
                                        double lbC = CeilTo5Cm(Math.Max(lb, 30.0));
                                        double zColNet = KolonNetYukseklikUstKot(
                                            beamRuns, stories[i + 1].lo, stories[i + 1].hi, stories[i + 1].zBot, stories[i + 1].zTop);
                                        KolonOrtUcdeBindirme(stories[i + 1].zBot, zColNet, lbC, out _, out zEnd);
                                    }
                                    else if (kolonLike && nextExists)
                                    {
                                        var up = stories[i + 1];
                                        double zUpNet = KolonNetYukseklikUstKot(beamRuns, up.lo, up.hi, up.zBot, up.zTop);
                                        if (zUpNet < up.zBot + 2.0) zUpNet = up.zBot + 2.0;
                                        KolonOrtUcdeBindirme(up.zBot, zUpNet, CeilTo5Cm(Math.Max(lb, 30.0)), out _, out zEnd);
                                    }
                                    AppendDonatiPline(tr, btr, new[]
                                    {
                                        new Point2d(xUse + bHook, Y(zFbot)),
                                        new Point2d(xUse + rBend, Y(zFbot)),
                                        new Point2d(xUse, Y(zFbot + rBend)),
                                        new Point2d(xUse, Y(zEnd))
                                    }, new[] { 0.0, -k90, 0.0, 0.0 });
                                    Label(xUse, (zCbot + st.zTop) * 0.5, nCont, dia, CeilTo5Cm((zEnd - zFbot) + bHook));
                                    HookTxt(xUse, zFbot, bHook, true);
                                    DimPerdeKatGecis(xUse, zEnd);
                                }
                                if (kolonLike) DimKolonLb(xUse);
                                else DimPerdeFiliz(xUse);
                                drewFiliz = true;
                            }
                        }
                        else if (zFtop - zFbot >= 10.0)
                        {
                            double xFiliz = NextX();
                            AppendDonatiPline(tr, btr, new[]
                            {
                                new Point2d(xFiliz + bHook, Y(zFbot)),
                                new Point2d(xFiliz + rBend, Y(zFbot)),
                                new Point2d(xFiliz, Y(zFbot + rBend)),
                                new Point2d(xFiliz, Y(zFtop))
                            }, new[] { 0.0, -k90, 0.0, 0.0 });
                            Label(xFiliz, (zFbot + zFtop) * 0.5, n, dia, CeilTo5Cm((zFtop - zFbot) + bHook));
                            HookTxt(xFiliz, zFbot, bHook, true);
                            if (kolonLike) DimKolonLb(xFiliz);
                            else DimPerdeFiliz(xFiliz);
                            drewFiliz = true;
                        }
                    }

                    if (hasThis && !combine)
                    {
                        bool firketeTop = last || !nextExists;
                        double zEndBar = zHoriz;
                        if (!firketeTop)
                        {
                            if (kolonLike && nextExists)
                            {
                                var up = stories[i + 1];
                                double zUpNet = KolonNetYukseklikUstKot(beamRuns, up.lo, up.hi, up.zBot, up.zTop);
                                if (zUpNet < up.zBot + 2.0) zUpNet = up.zBot + 2.0;
                                KolonOrtUcdeBindirme(up.zBot, zUpNet, CeilTo5Cm(Math.Max(lb, 30.0)), out _, out zEndBar);
                            }
                            else if (nextExists && kinds[i] == 1 && kinds[i + 1] == 0)
                            {
                                double lbC = CeilTo5Cm(Math.Max(lb, 30.0));
                                double zColNet = KolonNetYukseklikUstKot(
                                    beamRuns, stories[i + 1].lo, stories[i + 1].hi, stories[i + 1].zBot, stories[i + 1].zTop);
                                KolonOrtUcdeBindirme(stories[i + 1].zBot, zColNet, lbC, out _, out zEndBar);
                            }
                            else
                                zEndBar = st.zTop + lap;
                        }

                        int nMain = firketeTop ? n : nCont;
                        if (nMain > 0)
                        {
                            double xUse = NextX();
                            if (firketeTop)
                            {
                                if (extraFirkete)
                                {
                                    DrawKolonDonatiFirkete(tr, btr, Y, xUse, zStart, zHoriz, 1.0, k90, rBend, extraB, extraC);
                                    Label(xUse, (zStart + zHoriz) * 0.5, nMain, dia, CeilTo5Cm((zHoriz - zStart) + extraB + extraC));
                                    DrawBeamLabel(tr, btr, btr.Database, new Point3d(xUse + extraB * 0.5, Y(zHoriz) + 8.0, 0),
                                        extraB.ToString("0", CultureInfo.InvariantCulture), txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                                    DrawBeamLabel(tr, btr, btr.Database, new Point3d(xUse + extraB + 8.0, Y(zHoriz - extraC * 0.5), 0),
                                        extraC.ToString("0", CultureInfo.InvariantCulture), txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                                }
                                else
                                {
                                    DrawKolonDonatiGonye(tr, btr, Y, xUse, zStart, zHoriz, 1.0, k90, rBend, extraB);
                                    Label(xUse, (zStart + zHoriz) * 0.5, nMain, dia, CeilTo5Cm((zHoriz - zStart) + extraB));
                                    DrawBeamLabel(tr, btr, btr.Database, new Point3d(xUse + extraB * 0.5, Y(zHoriz) + 8.0, 0),
                                        extraB.ToString("0", CultureInfo.InvariantCulture), txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                                }
                            }
                            else
                            {
                                AppendDonatiPline(tr, btr, new[]
                                {
                                    new Point2d(xUse, Y(zStart)),
                                    new Point2d(xUse, Y(zEndBar))
                                }, null);
                                Label(xUse, (zStart + Math.Min(st.zTop, zEndBar)) * 0.5, nMain, dia, CeilTo5Cm(zEndBar - zStart));
                                DimPerdeKatGecis(xUse, zEndBar);
                            }
                            if (kolonLike && !(first && drewFiliz))
                                DimKolonLb(xUse);
                            else if (perdeLike && first && !drewFiliz)
                                DimPerdeFiliz(xUse);
                        }
                    }

                    if (nStop > 0 && nextExists)
                    {
                        double xEx = NextExtraX();
                        if (extraFirkete)
                        {
                            DrawKolonDonatiFirkete(tr, btr, Y, xEx, zStart, zHoriz, 1.0, k90, rBend, extraB, extraC);
                            Label(xEx, (zStart + zHoriz) * 0.5, nStop, dia, CeilTo5Cm((zHoriz - zStart) + extraB + extraC));
                            DrawBeamLabel(tr, btr, btr.Database, new Point3d(xEx + extraB * 0.5, Y(zHoriz) + 8.0, 0),
                                extraB.ToString("0", CultureInfo.InvariantCulture), txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                            DrawBeamLabel(tr, btr, btr.Database, new Point3d(xEx + extraB + 8.0, Y(zHoriz - extraC * 0.5), 0),
                                extraC.ToString("0", CultureInfo.InvariantCulture), txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                        }
                        else
                        {
                            DrawKolonDonatiGonye(tr, btr, Y, xEx, zStart, zHoriz, 1.0, k90, rBend, extraB);
                            Label(xEx, (zStart + zHoriz) * 0.5, nStop, dia, CeilTo5Cm((zHoriz - zStart) + extraB));
                            DrawBeamLabel(tr, btr, btr.Database, new Point3d(xEx + extraB * 0.5, Y(zHoriz) + 8.0, 0),
                                extraB.ToString("0", CultureInfo.InvariantCulture), txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                        }
                    }

                    if (nFilizEx > 0 && zFilizEx1 - zFilizEx0 >= 10.0)
                    {
                        double xFx = NextExtraX();
                        AppendDonatiPline(tr, btr, new[]
                        {
                            new Point2d(xFx, Y(zFilizEx0)),
                            new Point2d(xFx, Y(zFilizEx1))
                        }, null);
                        Label(xFx, (zFilizEx0 + zFilizEx1) * 0.5, nFilizEx, dia, CeilTo5Cm(zFilizEx1 - zFilizEx0), filizTag: true);
                    }
                }
            }
            if (extraSlot > 0)
                xBarLineMax = Math.Max(xBarLineMax, xKesitExtra0 + (extraSlot - 1) * KolonDuseyAcilimKesitExtraGapCm);
            return xBarLineMax;
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
            List<(double x0, double x1, double zb, double zt)> beamRuns,
            bool polygonArmView = false)
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
            var isBaslik = new bool[stories.Count];

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
                if (IsDepremPerdeBoyOrani(longCm, shortCm))
                    continue;
                isBaslik[i] = polygonArmView
                    || (shortCm > 1.0
                        && shortCm < KolonKesitPerdeBasligiMaxKenarCm - 0.01
                        && longCm < KolonKesitPerdeMinBoyOrani * shortCm - 0.01);
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
                if (polygonArmView)
                {
                    Geometry fullF = TryGetKolonFloorPolygon(st.floorIndex, col) ?? st.poly;
                    var face = CollectPoligonArmGorunusBars(fullF, st.poly, rot, st.floorIndex, col.ColumnNo);
                    barXs[i] = new List<double>(face.Count);
                    barAllPts[i] = new List<Point2d>(face.Count);
                    for (int k = 0; k < face.Count; k++)
                    {
                        barXs[i].Add(face[k].x);
                        barAllPts[i].Add(new Point2d(face[k].x, 0.5 * (e.MinY + e.MaxY)));
                    }
                }
                else
                {
                    barXs[i] = GetKolonGorunusLongFaceBarXs(g, st.floorIndex, col.ColumnNo);
                    barAllPts[i] = CollectKolonKesitBarPoints(e, st.floorIndex, col.ColumnNo);
                }
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
                if (first && isBaslik[i])
                    zStart = zCbot;
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
                        DrawKolonFinisGonyeFirkete(tr, btr, Y, x, zStart, zHoriz, hookDir, k90, rBend, dia, hookIn);
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
                    Envelope eUp = null;
                    try
                    {
                        Geometry gUp = rot != null ? rot.Transform(stories[i + 1].poly) : stories[i + 1].poly;
                        if (gUp != null && !gUp.IsEmpty) eUp = gUp.EnvelopeInternal;
                    }
                    catch { }
                    var filizXs = CollectKesitDegisimExtraFilizXs(
                        barAllPts[i], barAllPts[i + 1], matchedUpX, eUp);
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
                    // Perde başlığı: perde görünüşü gibi temel filiz, kolon altından 1,50 ℓb.
                    if (isBaslik[i])
                    {
                        double lbF = Ts500KenetlenmeLbCm(dia, fck, fyk);
                        zFilizTop = zCbot + CeilTo5Cm(Math.Max(1.50 * lbF, 30.0));
                    }
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
        /// KOLONDUSEY perde görünüşü: kesitteki uzun-yüz düşeyler.
        /// Filiz 1,50 ℓb, perde tabanından. Kesit değişmeden eşleştirme yok (Hcr→normal düz 1,50 ℓb).
        /// Üstte kolon varsa aynı çap + kiriş 1/6; kolon içi düz ve ekstra filiz, kolon ℓb bölgesi üstüne.
        /// </summary>
        private void DrawPerdeGorunusDuseyDonatilar(
            Transaction tr,
            BlockTableRecord btr,
            ColumnAxisInfo col,
            List<(double zBot, double zTop, Geometry poly, double majorDeg, double lo, double hi, int floorIndex)> stories,
            AffineTransformation rot,
            Func<double, double> X,
            Func<double, double> Y,
            double zDrawBot,
            List<(double x0, double x1, double z0, double z1)> temelSpans,
            List<(double x0, double x1, double zb, double zt)> beamRuns,
            bool polygonArmView = false)
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
            var bars = new List<(double x, int dia)>[stories.Count];
            var allPts = new List<Point2d>[stories.Count];
            var env = new Envelope[stories.Count];
            var colTh = new double[stories.Count];
            var isPerde = new bool[stories.Count];
            var isKolon = new bool[stories.Count];

            for (int i = 0; i < stories.Count; i++)
            {
                var st = stories[i];
                bars[i] = new List<(double x, int dia)>();
                allPts[i] = new List<Point2d>();
                colTh[i] = 25.0;
                if (st.poly == null || st.poly.IsEmpty) continue;
                Geometry g;
                try { g = rot != null ? rot.Transform(st.poly) : st.poly; }
                catch { g = st.poly; }
                var e = g.EnvelopeInternal;
                env[i] = e;
                if (IsKolonKesitPoligonKesit(g, e)) continue;
                double longCm = Math.Max(e.Width, e.Height);
                double shortCm = Math.Min(e.Width, e.Height);
                if (IsDepremPerdeBoyOrani(longCm, shortCm))
                {
                    isPerde[i] = true;
                    colTh[i] = Math.Max(8.0, shortCm);
                    if (polygonArmView)
                    {
                        Geometry fullF = TryGetKolonFloorPolygon(st.floorIndex, col) ?? st.poly;
                        bars[i] = CollectPoligonArmGorunusBars(fullF, st.poly, rot, st.floorIndex, col.ColumnNo);
                        allPts[i] = new List<Point2d>(bars[i].Count);
                        for (int k = 0; k < bars[i].Count; k++)
                            allPts[i].Add(new Point2d(bars[i][k].x, 0.5 * (e.MinY + e.MaxY)));
                    }
                    else
                    {
                        bars[i] = CollectPerdeGorunusLongFaceBars(g, st.floorIndex, col.ColumnNo);
                        var pPts = CollectPerdeKesitBarPoints(e, st.floorIndex, col.ColumnNo);
                        if (pPts != null)
                        {
                            allPts[i] = new List<Point2d>(pPts.Count);
                            for (int k = 0; k < pPts.Count; k++)
                                allPts[i].Add(pPts[k].p);
                        }
                    }
                }
                else
                {
                    isKolon[i] = true;
                    colTh[i] = Math.Max(8.0, shortCm);
                    if (polygonArmView)
                    {
                        Geometry fullF = TryGetKolonFloorPolygon(st.floorIndex, col) ?? st.poly;
                        bars[i] = CollectPoligonArmGorunusBars(fullF, st.poly, rot, st.floorIndex, col.ColumnNo);
                        allPts[i] = new List<Point2d>(bars[i].Count);
                        for (int k = 0; k < bars[i].Count; k++)
                            allPts[i].Add(new Point2d(bars[i][k].x, 0.5 * (e.MinY + e.MaxY)));
                    }
                    else
                    {
                        bars[i] = CollectKolonGorunusLongFaceBars(g, st.floorIndex, col.ColumnNo);
                        allPts[i] = CollectKolonKesitBarPoints(e, st.floorIndex, col.ColumnNo);
                    }
                }
            }

            for (int i = 0; i < stories.Count; i++)
            {
                if (!isPerde[i] || bars[i] == null || bars[i].Count == 0) continue;
                bool last = i == stories.Count - 1;
                bool first = i == 0;
                double zCbot = first ? zDrawBot : stories[i].zBot;
                double hookIn = Math.Max(2.0 * rBend + 1.0, colTh[i] - 2.0 * pas);
                int n = bars[i].Count;
                bool nextPerde = !last && i + 1 < stories.Count && isPerde[i + 1]
                    && bars[i + 1] != null && bars[i + 1].Count > 0;
                bool nextKolon = !last && i + 1 < stories.Count && isKolon[i + 1]
                    && bars[i + 1] != null && bars[i + 1].Count > 0;
                bool kesitDegisti = nextPerde
                    && KolonKesitPlanFarkli(stories[i].poly, stories[i + 1].poly, stories[i].majorDeg);
                double midX = 0.5 * (stories[i].lo + stories[i].hi);
                double zHorizPre = stories[i].zTop - pas;
                double zSof = KolonNetYukseklikUstKot(beamRuns, stories[i].lo, stories[i].hi, zCbot, stories[i].zTop);
                if (zSof < zCbot + 2.0) zSof = zCbot + 2.0;
                if (zSof > stories[i].zTop) zSof = stories[i].zTop;
                double h16 = stories[i].zTop - zSof;
                if (h16 < 6.0) h16 = 6.0;
                var xsNow = bars[i].Select(b => b.x).ToList();
                var xsUp = (nextPerde || nextKolon) ? bars[i + 1].Select(b => b.x).ToList() : null;
                int[] matchUp = nextKolon
                    ? MatchSameDiaBarsTbdY16(bars[i], bars[i + 1], h16)
                    : ((nextPerde && kesitDegisti) ? MatchKolonBarsTbdY16(xsNow, xsUp, h16) : null);
                double upLo = (nextPerde || nextKolon) ? stories[i + 1].lo : stories[i].lo;
                double upHi = (nextPerde || nextKolon) ? stories[i + 1].hi : stories[i].hi;
                double midHook = (nextPerde || nextKolon) ? 0.5 * (upLo + upHi) : midX;
                const double kolonIcKenarCm = 5.5;
                double lbKolonBolge = 0;
                double zKolonLbUst = 0;
                if (nextKolon)
                {
                    int dCol = 14;
                    foreach (var cb in bars[i + 1])
                        if (cb.dia > dCol) dCol = cb.dia;
                    lbKolonBolge = CeilTo5Cm(Math.Max(Ts500KenetlenmeLbCm(dCol, fck, fyk), 30.0));
                    double zColBot = stories[i + 1].zBot;
                    double zColNet = KolonNetYukseklikUstKot(
                        beamRuns, stories[i + 1].lo, stories[i + 1].hi, zColBot, stories[i + 1].zTop);
                    if (zColNet < zColBot + 2.0) zColNet = zColBot + 2.0;
                    KolonOrtUcdeBindirme(zColBot, zColNet, lbKolonBolge, out _, out zKolonLbUst);
                    if (zKolonLbUst < stories[i].zTop + lbKolonBolge)
                        zKolonLbUst = stories[i].zTop + lbKolonBolge;
                }

                for (int b = 0; b < n; b++)
                {
                    double bx = bars[i][b].x;
                    int dia = bars[i][b].dia >= 6 ? bars[i][b].dia : 14;
                    double lb = Ts500KenetlenmeLbCm(dia, fck, fyk);
                    double lapWall = CeilTo5Cm(Math.Max(1.50 * lb, 30.0));
                    double zEndCont;
                    if (nextKolon)
                        zEndCont = zKolonLbUst;
                    else if (last || !nextPerde)
                        zEndCont = stories[i].zTop;
                    else
                        zEndCont = stories[i].zTop + lapWall;
                    double x = X(bx);
                    int upIdx = matchUp != null && b < matchUp.Length ? matchUp[b] : -1;
                    bool matched = (nextPerde || nextKolon) && upIdx >= 0 && xsUp != null && upIdx < xsUp.Count;
                    bool insideUp = nextKolon
                        ? (bx >= upLo + kolonIcKenarCm && bx <= upHi - kolonIcKenarCm)
                        : (nextPerde && bx >= upLo + pas && bx <= upHi - pas);
                    if ((last && !nextKolon) || (!matched && !insideUp))
                    {
                        double zHoriz = zHorizPre;
                        if (zHoriz < zCbot + 15.0) zHoriz = zCbot + 20.0;
                        double hookDir = bx <= midHook ? 1.0 : -1.0;
                        DrawKolonFinisGonyeFirkete(tr, btr, Y, x, zCbot, zHoriz, hookDir, k90, rBend, dia, hookIn);
                    }
                    else if (matched)
                    {
                        double upX = xsUp[upIdx];
                        DrawKolonDonatiBirAltiGecis(tr, btr, X, Y, bx, upX, zCbot, zSof, stories[i].zTop, zEndCont);
                    }
                    else
                    {
                        AppendDonatiPline(tr, btr, new[]
                        {
                            new Point2d(x, Y(zCbot)),
                            new Point2d(x, Y(zEndCont))
                        }, null);
                    }
                }

                if (nextKolon)
                {
                    var matchedUpX = new List<double>();
                    if (matchUp != null && bars[i + 1] != null)
                    {
                        for (int b = 0; b < matchUp.Length; b++)
                        {
                            int ui = matchUp[b];
                            if (ui >= 0 && ui < bars[i + 1].Count)
                                matchedUpX.Add(bars[i + 1][ui].x);
                        }
                    }
                    var filizXs = CollectKesitDegisimExtraFilizXs(
                        allPts[i], allPts[i + 1], matchedUpX, env[i + 1]);
                    int diaU = 14;
                    foreach (var cb in bars[i + 1])
                        if (cb.dia > diaU) diaU = cb.dia;
                    double lbU = Ts500KenetlenmeLbCm(diaU, fck, fyk);
                    double filizDown = CeilTo5Cm(Math.Max(1.50 * lbU, 30.0));
                    double zFbot = stories[i].zTop - filizDown;
                    if (zFbot < zCbot + 5.0) zFbot = zCbot + 5.0;
                    if (filizXs != null && filizXs.Count > 0 && zKolonLbUst - zFbot >= 10.0)
                    {
                        foreach (double fx in filizXs)
                        {
                            double x = X(fx);
                            AppendDonatiPline(tr, btr, new[]
                            {
                                new Point2d(x, Y(zFbot)),
                                new Point2d(x, Y(zKolonLbUst))
                            }, null);
                        }
                    }
                }

                if (first)
                {
                    for (int b = 0; b < n; b++)
                    {
                        double bx = bars[i][b].x;
                        int dia = bars[i][b].dia >= 6 ? bars[i][b].dia : 14;
                        double lb = Ts500KenetlenmeLbCm(dia, fck, fyk);
                        double lap = CeilTo5Cm(Math.Max(1.50 * lb, 30.0));
                        double bHook = CeilTo5Cm(12.0 * dia / 10.0);
                        double zFilizBot = hasTemel ? zTemelBot + 5.0 : zCbot;
                        if (hasTemel)
                        {
                            double lbk = 0.75 * lb;
                            double a = zCbot - zFilizBot;
                            if (a + bHook < lbk) bHook = CeilTo5Cm(Math.Max(bHook, lbk - Math.Max(a, 0)));
                        }
                        double zFilizTop = zCbot + lap;
                        if (zFilizTop - zFilizBot < 10.0) continue;
                        double x = X(bx);
                        double hookDir = bx <= midX ? 1.0 : -1.0;
                        double bulge = hookDir > 0 ? -k90 : k90;
                        if (hasTemel && zFilizBot < zCbot - 1.0)
                        {
                            AppendDonatiPline(tr, btr, new[]
                            {
                                new Point2d(x + hookDir * bHook, Y(zFilizBot)),
                                new Point2d(x + hookDir * rBend, Y(zFilizBot)),
                                new Point2d(x, Y(zFilizBot + rBend)),
                                new Point2d(x, Y(zFilizTop))
                            }, new[] { 0.0, bulge, 0.0, 0.0 });
                        }
                        else
                        {
                            AppendDonatiPline(tr, btr, new[]
                            {
                                new Point2d(x, Y(zCbot)),
                                new Point2d(x, Y(zFilizTop))
                            }, null);
                        }
                    }
                }
            }
        }

        /// <summary>Perde kesit uzun yüz düşeyler: uç ve gövde çapı ayrı (1,50 ℓb için).</summary>
        private List<(double x, int dia)> CollectPerdeGorunusLongFaceBars(Geometry gRot, int floorIndex, int colNo)
        {
            var bars = new List<(double x, int dia)>();
            if (gRot == null || gRot.IsEmpty) return bars;
            var e = gRot.EnvelopeInternal;
            double lw = Math.Max(e.Width, e.Height);
            double bw = Math.Min(e.Width, e.Height);
            if (bw < 8.0 || lw < 6.0 * bw - 0.01) return bars;
            double lu = ResolvePerdeUcBolgeLuCm(lw, bw, floorIndex, colNo);
            if (lu < 8.0) return bars;

            double pas = KolonKesitPaspayiCm;
            double rad = KolonKesitEtriyeRadiusCm;
            int ucPerLayer = 8, ucDia = 14, govdePerLayer = 0, govdeDia = 12;
            if (_kolonDuseyGpr != null && _model?.Floors != null &&
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(_kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out string donatiRaw, out _) &&
                KolonDonatiTableDrawer.TryParsePerdeUcGovdeDonati(donatiRaw, out int pUc, out int dUc, out int pGv, out int dGv))
            {
                ucPerLayer = pUc;
                ucDia = dUc;
                govdePerLayer = pGv;
                govdeDia = dGv;
            }
            if (ucDia < 6) ucDia = 14;
            if (govdeDia < 6) govdeDia = 12;
            int nUcEnd = Math.Max(4, ucPerLayer);
            double govdeSpan = Math.Max(1.0, lw - 2.0 * lu - 2.0 * pas);
            int nGovdeFace = Math.Max(1, govdePerLayer);
            const double tbdYGovdeSMaxCm = 25.0;
            int nGovdeMin = 1 + (int)Math.Ceiling(govdeSpan / tbdYGovdeSMaxCm - 1e-9);
            if (nGovdeMin < 2) nGovdeMin = 2;
            if (nGovdeFace < nGovdeMin) nGovdeFace = nGovdeMin;

            double minInner = 2.0 * rad + 2.0;
            double innerPas = pas;
            if (lu - 2.0 * pas < minInner)
                innerPas = Math.Max(0.0, (lu - minInner) * 0.5);
            double barLo = pas + rad;
            double sKenar = bw - 2.0 * barLo;
            double longSpan = lu - innerPas - pas - 2.0 * rad;
            if (sKenar < 1.0) sKenar = 1.0;
            if (longSpan < 1.0) longSpan = 1.0;
            ResolvePerdeUcBarDagilim(nUcEnd, longSpan, sKenar, ucDia / 10.0, out int nLongUse, out _, out _);

            bool longIsX = e.Width >= e.Height;
            if (longIsX)
            {
                double xL0 = e.MinX + barLo, xL1 = e.MinX + lu - innerPas - rad;
                double xR0 = e.MaxX - lu + innerPas + rad, xR1 = e.MaxX - barLo;
                if (xL1 < xL0 + 1.0) xL1 = xL0;
                if (xR1 < xR0 + 1.0) xR0 = xR1;
                var xsL = PerdeKesitEsitKonumlar(xL0, xL1, nLongUse);
                var xsR = PerdeKesitEsitKonumlar(xR0, xR1, nLongUse);
                double innerL = xsL[xsL.Length - 1], innerR = xsR[0];
                while (nGovdeFace > 0 && (innerR - innerL) / (nGovdeFace + 1) > 25.01)
                    nGovdeFace++;
                var xsG = PerdeKesitAraKonumlar(innerL, innerR, nGovdeFace);
                AddUniquePerdeGorunusBars(bars, xsL, ucDia);
                AddUniquePerdeGorunusBars(bars, xsG, govdeDia);
                AddUniquePerdeGorunusBars(bars, xsR, ucDia);
            }
            else
            {
                AddUniquePerdeGorunusBars(bars, new[] { e.MinX + barLo, e.MaxX - barLo }, ucDia);
            }
            bars.Sort((a, b) => a.x.CompareTo(b.x));
            return bars;
        }

        private static void AddUniquePerdeGorunusBars(List<(double x, int dia)> dst, double[] src, int dia)
        {
            if (dst == null || src == null) return;
            for (int i = 0; i < src.Length; i++)
            {
                bool have = false;
                for (int j = 0; j < dst.Count; j++)
                {
                    if (Math.Abs(dst[j].x - src[i]) < 0.8) { have = true; break; }
                }
                if (!have) dst.Add((src[i], dia));
            }
        }

        /// <summary>
        /// Perde→kolon: yalnız aynı çap, kiriş yüksekliğinde |Δx| ≤ h/6.
        /// </summary>
        private static int[] MatchSameDiaBarsTbdY16(
            List<(double x, int dia)> lower,
            List<(double x, int dia)> upper,
            double hAvail)
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
                int dL = lower[l].dia >= 6 ? lower[l].dia : 14;
                for (int u = 0; u < upper.Count; u++)
                {
                    int dU = upper[u].dia >= 6 ? upper[u].dia : 14;
                    if (dL != dU) continue;
                    cand.Add((l, u, Math.Abs(lower[l].x - upper[u].x)));
                }
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

        /// <summary>
        /// Kesit değişiminde üst kesitte eşleşmeyen düşeylerin unique X'i — görünüş extra filiz adedi ile aynı.
        /// </summary>
        private static List<double> CollectKesitDegisimExtraFilizXs(
            List<Point2d> loPts,
            List<Point2d> upPts,
            List<double> matchedUpX,
            Envelope eUp)
        {
            var filizXs = new List<double>();
            if (upPts == null || upPts.Count == 0) return filizXs;
            if (loPts == null) loPts = new List<Point2d>();
            if (matchedUpX == null) matchedUpX = new List<double>();
            double yTop = 0, yBot = 0;
            bool hasLongY = TryKolonKesitLongFaceY(eUp, out yTop, out yBot);
            const double tol = 2.5;
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
            return filizXs;
        }

        /// <summary>
        /// Kesit izdüşümündeki kırmızı (eşleşmeyen) daire adedi ile aynı: unique X yok, aynı çap XY eşleşmesi.
        /// </summary>
        private static int CountKesitDegisimUnmatchedBars(
            List<(Point2d p, int dia)> loPts,
            List<(Point2d p, int dia)> upPts,
            List<double> matchedUpX,
            Envelope eUp,
            int diaFilter)
        {
            if (upPts == null || upPts.Count == 0) return 0;
            if (loPts == null) loPts = new List<(Point2d p, int dia)>();
            if (matchedUpX == null) matchedUpX = new List<double>();
            double yTop = 0, yBot = 0;
            bool hasLongY = TryKolonKesitLongFaceY(eUp, out yTop, out yBot);
            const double tol = 2.5;
            int n = 0;
            foreach (var u in upPts)
            {
                int dU = u.dia >= 6 ? u.dia : 14;
                if (diaFilter >= 6 && dU != diaFilter) continue;
                bool match = false;
                foreach (var lo in loPts)
                {
                    int dL = lo.dia >= 6 ? lo.dia : 14;
                    if (dL != dU) continue;
                    double dx = lo.p.X - u.p.X;
                    double dy = lo.p.Y - u.p.Y;
                    if (dx * dx + dy * dy <= tol * tol)
                    {
                        match = true;
                        break;
                    }
                }
                if (!match && hasLongY && (Math.Abs(u.p.Y - yTop) < tol || Math.Abs(u.p.Y - yBot) < tol))
                {
                    foreach (double mx in matchedUpX)
                    {
                        if (Math.Abs(u.p.X - mx) < tol)
                        {
                            match = true;
                            break;
                        }
                    }
                }
                if (!match) n++;
            }
            return n;
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
        /// Finiş: yönetmelik yok. φ≤12 gönye = 12φ (5 cm katları); dar−2·pas’ı geçerse firkete c=20.
        /// φ≥14: b = dar−2·pas, firkete c=30.
        /// </summary>
        private static void ResolveFinisGonyeFirkete(int diaMm, double darEksi2Pas, double rBend, out bool firkete, out double bCm, out double cCm)
        {
            double bCap = darEksi2Pas > 2.0 * rBend ? darEksi2Pas : 2.0 * rBend;
            if (diaMm <= 12)
            {
                double gonye = CeilTo5Cm(12.0 * diaMm / 10.0);
                if (gonye < 5.0) gonye = 5.0;
                if (gonye > bCap + 0.01)
                {
                    firkete = true;
                    bCm = bCap;
                    cCm = 20.0;
                }
                else
                {
                    firkete = false;
                    bCm = gonye;
                    cCm = 0.0;
                }
            }
            else
            {
                firkete = true;
                bCm = bCap;
                cCm = 30.0;
            }
        }

        private void DrawKolonFinisGonyeFirkete(
            Transaction tr, BlockTableRecord btr,
            Func<double, double> Y,
            double x, double zStart, double zHoriz, double hookDir,
            double k90, double rBend, int diaMm, double darEksi2Pas)
        {
            ResolveFinisGonyeFirkete(diaMm, darEksi2Pas, rBend, out bool firkete, out double b, out double c);
            if (firkete)
                DrawKolonDonatiFirkete(tr, btr, Y, x, zStart, zHoriz, hookDir, k90, rBend, b, c);
            else
                DrawKolonDonatiGonye(tr, btr, Y, x, zStart, zHoriz, hookDir, k90, rBend, b);
        }

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

        private List<(double x, int dia)> CollectKolonGorunusLongFaceBars(Geometry gRot, int floorIndex, int colNo)
        {
            var list = new List<(double x, int dia)>();
            var xs = GetKolonGorunusLongFaceBarXs(gRot, floorIndex, colNo);
            if (xs == null || xs.Count == 0) return list;
            int dia = 14;
            if (_kolonDuseyGpr != null && _model?.Floors != null &&
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(_kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out string donati, out _) &&
                !string.IsNullOrWhiteSpace(donati))
            {
                KolonDonatiTableDrawer.SumKolonKesitDuseyDonatiAdet(donati, out int d);
                if (d >= 6) dia = d;
            }
            for (int i = 0; i < xs.Count; i++)
                list.Add((xs[i], dia));
            return list;
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

        /// <summary>
        /// Plan kesit + etriye/çiroz açılım takımı.
        /// Kesit üst çizgisi, ait olduğu kat görünüşünün üst çizgisinin 120 cm altında.
        /// </summary>
        private void DrawKolonDuseyFloorPlanKesit(
            Transaction tr,
            BlockTableRecord btr,
            Geometry poly,
            double majorAngleDeg,
            double planRightX,
            double kesitTopY,
            int floorIndex,
            int colNo,
            Geometry overlayPoly = null,
            int overlayFloorIndex = -1,
            double h16Cm = 30.0,
            List<(double zLo, double zHi, int sCm, int diaMm)> etriyeBolgeler = null,
            double storyZBot = 0,
            double storyZTop = 0,
            List<double> etriyeZs = null,
            double etriyeAdetZBot = double.NaN,
            bool polygonArmKesit = false)
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
            double long0 = Math.Max(e0.Width, e0.Height);
            double short0 = Math.Min(e0.Width, e0.Height);
            bool isPerdeKesit0 = short0 > 1.0 && long0 >= KolonKesitPerdeMinBoyOrani * short0 - 0.01;
            bool poly0 = IsKolonKesitPoligonKesit(g, e0);
            double extraRight = poly0
                ? PoligonKesitTakimYanPayCm
                : EstimateKesitTakimExtraRightCm(e0, isPerdeKesit0);
            double sectionRight = planRightX - extraRight;
            double targetCx = sectionRight - e0.Width * 0.5;
            double targetCy = kesitTopY - e0.Height * 0.5;
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
                && (polygonArmKesit
                    || (shortCm < KolonKesitPerdeBasligiMaxKenarCm - 0.01
                        && longCm < KolonKesitPerdeMinBoyOrani * shortCm - 0.01));
            DrawGeometryRingsAsPolylines(tr, btr, g, (isPerdeBasligi || isPerdeKesit) ? LayerPerde : LayerKolon, addHatch: false, applySmallTriangleTrim: false);
            bool isPoligonKesit = IsKolonKesitPoligonKesit(g, e);
            if (!isPoligonKesit)
                DrawKolonKesitKatBoyutEtiket(tr, btr, e, floorIndex, colNo, isPerdeKesit || isPerdeBasligi);
            var etriyeBoxes = new List<(double midX, double w, double h)>();
            var cirozStems = new List<(double stem, bool govde)>();
            List<(double x0, double y0, double x1, double y1)> parcaRects = null;
            List<(Envelope env, bool longIsX, double luLo, double luHi, double ipLo, double ipHi)> poligonKoller = null;
            List<(double x, double y)> poligonPts = null;
            List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)> poligonEtBoxes = null;
            List<(double stem, bool govde)> poligonCirozStems = null;
            int poligonNUc = 0, poligonNGv = 0, poligonUcDia = 14, poligonGvDia = 12;
            if (isPoligonKesit)
            {
                parcaRects = CollectKolonPoligonParcaDortgenleri(g);
                DrawPoligonKolonKesitEtriyeler(tr, btr, g, parcaRects, floorIndex, colNo,
                    out poligonKoller, out poligonPts, out poligonNUc, out poligonNGv, out poligonUcDia, out poligonGvDia,
                    out poligonEtBoxes, out _, out poligonCirozStems);
            }
            else
            {
                if (isPerdeKesit)
                    DrawPerdeKesitUcBolgeleri(tr, btr, e, floorIndex, colNo, etriyeBoxes, cirozStems);
                else
                    DrawKolonKesitEtriye(tr, btr, g, e, floorIndex, colNo, isPerdeBasligi, etriyeBoxes, cirozStems);
                DrawKolonKesitDuseyDonatiYazisi(tr, btr, e, floorIndex, colNo, isPerdeBasligi, isPerdeKesit);
            }

            if (overlayPoly != null && !overlayPoly.IsEmpty && moveT != null)
                DrawKolonKesitIzdusumOverlay(tr, btr, overlayPoly, c.X, c.Y, majorAngleDeg, moveT, overlayFloorIndex, colNo, e, floorIndex, h16Cm);

            if (isPoligonKesit)
            {
                DrawPoligonKolonKesitOlculeri(tr, btr, e, g, poligonKoller, parcaRects);
                DrawPoligonKolonKesitDuseyEtiketleri(tr, btr, e, poligonKoller, poligonPts, poligonUcDia, poligonGvDia, parcaRects);
                int diaEtP = ResolveKolonDuseyEtriyeDiaMm(floorIndex, colNo);
                int adetEtP = 1;
                int sEtP = 10;
                double zAdetBot = !double.IsNaN(etriyeAdetZBot) ? etriyeAdetZBot : storyZBot;
                SumKolonKesitEtriyeAdetAralik(etriyeBolgeler, etriyeZs, zAdetBot, storyZTop, floorIndex, colNo, e,
                    out adetEtP, out sEtP, out int diaEt2);
                if (diaEt2 >= 6) diaEtP = diaEt2;
                double takimMaxY = DrawPoligonKolonKesitParcaAcilimlari(
                    tr, btr, e, g, parcaRects, poligonEtBoxes, poligonKoller, adetEtP, diaEtP,
                    floorIndex, colNo, storyZBot, storyZTop);
                DrawPoligonKolonKesitIsimVeDonati(
                    tr, btr, e, floorIndex, colNo,
                    poligonNUc, poligonNGv, poligonUcDia, poligonGvDia, takimMaxY);
                DrawPoligonParcaNumaralari(tr, btr, parcaRects);
                if (poligonCirozStems != null && poligonCirozStems.Count > 0)
                {
                    int nGovdeC = CountPoligonGovdeCirozAdet(
                        poligonKoller, floorIndex, colNo, storyZBot, storyZTop, out double perM2C);
                    DrawKolonKesitCirozAcilim(
                        tr, btr, e, poligonCirozStems, diaEtP, adetEtP, true,
                        floorIndex, colNo, storyZBot, storyZTop,
                        e.MaxX + 40.0, e.MinY - 28.0,
                        nGovdeOverride: nGovdeC, govdePerM2Override: perM2C);
                }
                return;
            }

            ObjectId dimId = GetOrCreatePlanOlcuDimStyle(tr, btr.Database, 10.0, 1.0, PlanOlcuDonatiDimStyleName);
            double off = Kolon50GorunusCiftOlcuAraCm;
            double offBot = isPerdeKesit
                ? PerdeKesitBaslikOlcuOfsetCm + PerdeKesitUzunlukOlcuBaslikAltiCm
                : off;
            void Dim(Point3d a, Point3d b, Point3d linePt, double fxlen)
            {
                var dim = new AlignedDimension(a, b, linePt, "", dimId)
                {
                    Layer = LayerOlcu,
                    LineWeight = LineWeight.LineWeight020
                };
                try { dim.DimfxlenOn = true; } catch { }
                try { dim.Dimfxlen = fxlen; } catch { }
                AppendEntity(tr, btr, dim);
            }
            Dim(new Point3d(e.MinX, e.MinY, 0), new Point3d(e.MaxX, e.MinY, 0),
                new Point3d((e.MinX + e.MaxX) * 0.5, e.MinY - offBot, 0), offBot);
            Dim(new Point3d(e.MinX, e.MinY, 0), new Point3d(e.MinX, e.MaxY, 0),
                new Point3d(e.MinX - off, (e.MinY + e.MaxY) * 0.5, 0), off);
            double acilimGap = isPerdeKesit ? PerdeKesitEtriyeAcilimOlcuAltiCm : KolonKesitEtriyeAcilimGapCm;
            double yEtTop = e.MinY - offBot - acilimGap;
            double yAcilimBot = yEtTop;
            int diaEt = ResolveKolonDuseyEtriyeDiaMm(floorIndex, colNo);
            int adetEt = 1;
            int sEt = 10;
            if (etriyeBoxes.Count > 0 || cirozStems.Count > 0)
            {
                double zAdetBot = !double.IsNaN(etriyeAdetZBot) ? etriyeAdetZBot : storyZBot;
                SumKolonKesitEtriyeAdetAralik(etriyeBolgeler, etriyeZs, zAdetBot, storyZTop, floorIndex, colNo, e,
                    out adetEt, out sEt, out int diaEt2);
                if (diaEt2 >= 6) diaEt = diaEt2;
            }
            double yCirozTop = yEtTop;
            if (etriyeBoxes.Count > 0)
                yAcilimBot = DrawKolonKesitEtriyeAcilim(tr, btr, e, etriyeBoxes, diaEt, adetEt, sEt, isPerdeKesit, out _, out yCirozTop);
            if (isPerdeKesit)
            {
                double yYatay = DrawPerdeKesitGovdeYatayAcilim(tr, btr, e, floorIndex, colNo, storyZBot, storyZTop, yAcilimBot);
                if (yYatay < yCirozTop)
                    yCirozTop = yYatay;
            }
            if (cirozStems.Count > 0)
                DrawKolonKesitCirozAcilim(tr, btr, e, cirozStems, diaEt, adetEt, isPerdeKesit, floorIndex, colNo, storyZBot, storyZTop,
                    e.MinX, yCirozTop - KolonKesitEtriyeAcilimAltGapCm);
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
            var upPts = CollectKolonKesitBarPointsWithDia(eUp, overlayFloorIndex, colNo);
            if (upPts.Count == 0) return;
            bool loIsPerde = eLower != null && IsDepremPerdeBoyOrani(
                Math.Max(eLower.Width, eLower.Height), Math.Min(eLower.Width, eLower.Height));
            var loPts = loIsPerde
                ? CollectPerdeKesitBarPoints(eLower, lowerFloorIndex, colNo)
                : CollectKolonKesitBarPointsWithDia(eLower, lowerFloorIndex, colNo);
            var loXs = CollectKesitLongFaceBarsWithDia(loPts, eLower);
            var upXs = CollectKesitLongFaceBarsWithDia(upPts, eUp);
            int[] match16 = MatchSameDiaBarsTbdY16(loXs, upXs, h16Cm);
            var matchedUpX = new List<double>();
            if (match16 != null && loXs != null && upXs != null)
            {
                for (int i = 0; i < match16.Length && i < loXs.Count; i++)
                {
                    int u = match16[i];
                    if (u >= 0 && u < upXs.Count)
                        matchedUpX.Add(upXs[u].x);
                }
            }
            const double tol = 2.5;
            double yTop = 0, yBot = 0;
            bool hasLongY = TryKolonKesitLongFaceY(eUp, out yTop, out yBot);
            foreach (var u in upPts)
            {
                int dU = u.dia >= 6 ? u.dia : 14;
                bool match = false;
                foreach (var lo in loPts)
                {
                    int dL = lo.dia >= 6 ? lo.dia : 14;
                    if (dL != dU) continue;
                    double dx = lo.p.X - u.p.X;
                    double dy = lo.p.Y - u.p.Y;
                    if (dx * dx + dy * dy <= tol * tol)
                    {
                        match = true;
                        break;
                    }
                }
                if (!match && hasLongY && (Math.Abs(u.p.Y - yTop) < tol || Math.Abs(u.p.Y - yBot) < tol))
                {
                    foreach (double mx in matchedUpX)
                    {
                        if (Math.Abs(u.p.X - mx) < tol)
                        {
                            match = true;
                            break;
                        }
                    }
                }
                DrawKolonKesitIzdusumMarkCircle(tr, btr, u.p.X, u.p.Y, match ? (short)5 : (short)1);
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

        private static List<(double x, int dia)> CollectKesitLongFaceBarsWithDia(
            List<(Point2d p, int dia)> pts, Envelope e)
        {
            var list = new List<(double x, int dia)>();
            if (pts == null || e == null) return list;
            if (!TryKolonKesitLongFaceY(e, out double yTop, out double yBot)) return list;
            const double tol = 2.5;
            foreach (var t in pts)
            {
                if (Math.Abs(t.p.Y - yTop) > tol && Math.Abs(t.p.Y - yBot) > tol) continue;
                bool have = false;
                for (int i = 0; i < list.Count; i++)
                {
                    if (Math.Abs(list[i].x - t.p.X) < 0.8) { have = true; break; }
                }
                if (!have) list.Add((t.p.X, t.dia >= 6 ? t.dia : 14));
            }
            list.Sort((a, b) => a.x.CompareTo(b.x));
            return list;
        }

        private int ResolveKolonKesitDonatiDiaMm(int floorIndex, int colNo)
        {
            int dia = 14;
            if (_kolonDuseyGpr != null && _model?.Floors != null &&
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(_kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out string donati, out _) &&
                !string.IsNullOrWhiteSpace(donati))
            {
                KolonDonatiTableDrawer.SumKolonKesitDuseyDonatiAdet(donati, out int d);
                if (d >= 6) dia = d;
            }
            return dia;
        }

        private List<(Point2d p, int dia)> CollectKolonKesitBarPointsWithDia(Envelope e, int floorIndex, int colNo)
        {
            var list = new List<(Point2d p, int dia)>();
            var raw = CollectKolonKesitBarPoints(e, floorIndex, colNo);
            int dia = ResolveKolonKesitDonatiDiaMm(floorIndex, colNo);
            for (int i = 0; i < raw.Count; i++)
                list.Add((raw[i], dia));
            return list;
        }

        /// <summary>Perde kesitte çizilen düşeyler: uç ve gövde çapı ayrı (kesit eşleşmesi görünüşle aynı).</summary>
        private List<(Point2d p, int dia)> CollectPerdeKesitBarPoints(Envelope e, int floorIndex, int colNo)
        {
            var pts = new List<(Point2d p, int dia)>();
            if (e == null) return pts;
            double lw = Math.Max(e.Width, e.Height);
            double bw = Math.Min(e.Width, e.Height);
            if (bw < 8.0 || lw < 6.0 * bw - 0.01) return pts;
            bool hasHcr = KolonDonatiTableDrawer.TryGetKolonBetonarmeHcr(_kolonDuseyGprHcr, _model?.Floors, floorIndex, colNo);
            double lu = hasHcr ? Math.Max(2.0 * bw, 0.2 * lw) : Math.Max(bw, 0.1 * lw);
            if (lu < 40.0) lu = 40.0;
            double maxLu = (lw - Math.Max(10.0, bw)) * 0.5;
            if (maxLu < bw) maxLu = lw * 0.45;
            if (lu > maxLu) lu = maxLu;
            if (lu < 8.0) return pts;

            double pas = KolonKesitPaspayiCm;
            double rad = KolonKesitEtriyeRadiusCm;
            int ucPerLayer = 8, ucDia = 14, govdePerLayer = 0, govdeDia = 12;
            if (_kolonDuseyGpr != null && _model?.Floors != null &&
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(_kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out string donatiRaw, out _) &&
                KolonDonatiTableDrawer.TryParsePerdeUcGovdeDonati(donatiRaw, out int pUc, out int dUc, out int pGv, out int dGv))
            {
                ucPerLayer = pUc;
                ucDia = dUc;
                govdePerLayer = pGv;
                govdeDia = dGv;
            }
            if (ucDia < 6) ucDia = 14;
            if (govdeDia < 6) govdeDia = 12;
            int nUcEnd = Math.Max(4, ucPerLayer);
            double govdeSpan = Math.Max(1.0, lw - 2.0 * lu - 2.0 * pas);
            int nGovdeFace = Math.Max(1, govdePerLayer);
            const double tbdYGovdeSMaxCm = 25.0;
            int nGovdeMin = 1 + (int)Math.Ceiling(govdeSpan / tbdYGovdeSMaxCm - 1e-9);
            if (nGovdeMin < 2) nGovdeMin = 2;
            if (nGovdeFace < nGovdeMin) nGovdeFace = nGovdeMin;

            double minInner = 2.0 * rad + 2.0;
            double innerPas = pas;
            if (lu - 2.0 * pas < minInner)
                innerPas = Math.Max(0.0, (lu - minInner) * 0.5);
            double barLo = pas + rad;
            double sKenar = bw - 2.0 * barLo;
            double longSpan = lu - innerPas - pas - 2.0 * rad;
            if (sKenar < 1.0) sKenar = 1.0;
            if (longSpan < 1.0) longSpan = 1.0;
            ResolvePerdeUcBarDagilim(nUcEnd, longSpan, sKenar, ucDia / 10.0, out int nLongUse, out int nEndUse, out bool useInner);

            void Add(double x, double y, int dia)
            {
                pts.Add((new Point2d(x, y), dia));
            }
            void AddCiftX(double[] xs, double yBot, double yTop, int dia)
            {
                if (xs == null) return;
                for (int i = 0; i < xs.Length; i++)
                {
                    Add(xs[i], yBot, dia);
                    Add(xs[i], yTop, dia);
                }
            }
            void AddCiftY(double xL, double xR, double[] ys, int dia)
            {
                if (ys == null) return;
                for (int i = 0; i < ys.Length; i++)
                {
                    Add(xL, ys[i], dia);
                    Add(xR, ys[i], dia);
                }
            }
            void AddKenar(double fixedC, double a, double b, int nEnd, bool alongY, int dia)
            {
                if (nEnd < 3) return;
                var ps = PerdeKesitEsitKonumlar(a, b, nEnd);
                for (int i = 1; i < ps.Length - 1; i++)
                {
                    if (alongY) Add(fixedC, ps[i], dia);
                    else Add(ps[i], fixedC, dia);
                }
            }

            bool longIsX = e.Width >= e.Height;
            if (longIsX)
            {
                double yBot = e.MinY + barLo, yTop = e.MaxY - barLo;
                double xL0 = e.MinX + barLo, xL1 = e.MinX + lu - innerPas - rad;
                double xR0 = e.MaxX - lu + innerPas + rad, xR1 = e.MaxX - barLo;
                if (xL1 < xL0 + 1.0) xL1 = xL0;
                if (xR1 < xR0 + 1.0) xR0 = xR1;
                var xsL = PerdeKesitEsitKonumlar(xL0, xL1, nLongUse);
                var xsR = PerdeKesitEsitKonumlar(xR0, xR1, nLongUse);
                AddCiftX(xsL, yBot, yTop, ucDia);
                AddCiftX(xsR, yBot, yTop, ucDia);
                AddKenar(xL0, yBot, yTop, nEndUse, alongY: true, ucDia);
                AddKenar(xR1, yBot, yTop, nEndUse, alongY: true, ucDia);
                if (useInner)
                {
                    AddKenar(xL1, yBot, yTop, nEndUse, alongY: true, ucDia);
                    AddKenar(xR0, yBot, yTop, nEndUse, alongY: true, ucDia);
                }
                double innerL = xsL[xsL.Length - 1], innerR = xsR[0];
                while (nGovdeFace > 0 && (innerR - innerL) / (nGovdeFace + 1) > 25.01)
                    nGovdeFace++;
                var xsG = PerdeKesitAraKonumlar(innerL, innerR, nGovdeFace);
                AddCiftX(xsG, yBot, yTop, govdeDia);
            }
            else
            {
                double xL = e.MinX + barLo, xR = e.MaxX - barLo;
                double yB0 = e.MinY + barLo, yB1 = e.MinY + lu - innerPas - rad;
                double yT0 = e.MaxY - lu + innerPas + rad, yT1 = e.MaxY - barLo;
                if (yB1 < yB0 + 1.0) yB1 = yB0;
                if (yT1 < yT0 + 1.0) yT0 = yT1;
                var ysB = PerdeKesitEsitKonumlar(yB0, yB1, nLongUse);
                var ysT = PerdeKesitEsitKonumlar(yT0, yT1, nLongUse);
                AddCiftY(xL, xR, ysB, ucDia);
                AddCiftY(xL, xR, ysT, ucDia);
                AddKenar(yB0, xL, xR, nEndUse, alongY: false, ucDia);
                AddKenar(yT1, xL, xR, nEndUse, alongY: false, ucDia);
                if (useInner)
                {
                    AddKenar(yB1, xL, xR, nEndUse, alongY: false, ucDia);
                    AddKenar(yT0, xL, xR, nEndUse, alongY: false, ucDia);
                }
                double innerB = ysB[ysB.Length - 1], innerT = ysT[0];
                while (nGovdeFace > 0 && (innerT - innerB) / (nGovdeFace + 1) > 25.01)
                    nGovdeFace++;
                var ysG = PerdeKesitAraKonumlar(innerB, innerT, nGovdeFace);
                AddCiftY(xL, xR, ysG, govdeDia);
            }
            return pts;
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

        private static bool IsDepremPerdeBoyOrani(double longCm, double shortCm)
        {
            return shortCm > 1.0 && longCm >= KolonKesitPerdeMinBoyOrani * shortCm - 0.01;
        }

        private bool StoryIsDepremPerde(Geometry poly, AffineTransformation rot)
        {
            if (poly == null || poly.IsEmpty) return false;
            Geometry g;
            try { g = rot != null ? rot.Transform(poly) : poly; }
            catch { g = poly; }
            if (g == null || g.IsEmpty) return false;
            var e = g.EnvelopeInternal;
            if (IsKolonKesitPoligonKesit(g, e)) return false;
            return IsDepremPerdeBoyOrani(Math.Max(e.Width, e.Height), Math.Min(e.Width, e.Height));
        }
        private const double KolonKesitPaspayiCm = 4.0;
        /// <summary>Poligon kolon düşey: net aralık ≥ 5 cm (beton girsin). Merkez = net + φ.</summary>
        private const double PoligonDonatiNetAralikMinCm = 5.0;
        private const double PerdeKesitYatayKenarCm = 7.0;
        private const double KolonKesitEtriyeRadiusCm = 1.5;
        private const double KolonKesitDuseyDonatiRadiusCm = 0.75;
        private const double KolonKesitDuseyDonatiCemberWidthCm = 1.5;
        private const double KolonKesitIkinciEtriyeMinUzunCm = 60.0;
        private const double KolonKesitIkinciEtriyeBirUcCm = 120.0;
        private const double KolonKesitEtriyeAcilimGapCm = 30.0;
        /// <summary>Perde kesit başlık ölçüsü, perde yüzünden (cm).</summary>
        private const double PerdeKesitBaslikOlcuOfsetCm = 15.0;
        /// <summary>Perde uzunluk ölçüsü, başlık ölçüsünün altından (cm).</summary>
        private const double PerdeKesitUzunlukOlcuBaslikAltiCm = 15.0;
        /// <summary>Perde etriye/açılım, uzunluk ölçüsünün altından (cm).</summary>
        private const double PerdeKesitEtriyeAcilimOlcuAltiCm = 25.0;
        /// <summary>Perde kesit düşey donatı etiketi yukarı (cm).</summary>
        private const double PerdeKesitDuseyEtiketYukariCm = 2.0;
        /// <summary>Poligon kolon dış kontur 1. detay ölçü ofseti (cm).</summary>
        private const double PoligonKolonDisOlcuDetayCm = 40.0;
        /// <summary>Poligon kolon dış kontur toplam ölçü ofseti (cm).</summary>
        private const double PoligonKolonDisOlcuToplamCm = 58.0;
        /// <summary>Poligon iç etriye ölçüsü, iç poligon çizgisinden (cm).</summary>
        private const double PoligonKolonIcEtriyeOlcuCm = 15.0;
        /// <summary>Etiket çizgisi, poligon dış çizgisinden dışarı (cm).</summary>
        private const double PoligonKolonEtiketDisCm = 6.5;
        /// <summary>Etiket yazısı, etiket çizgisinden (cm).</summary>
        private const double PoligonKolonEtiketYaziAraCm = 1.0;
        /// <summary>İsim + toplam donatı, kesit takımının üstünden (cm).</summary>
        private const double PoligonKesitTakimUstEtiketBoslukCm = 20.0;
        /// <summary>Poligon kesit takımı sol/sağ: dış ölçü + açılım + yazı payı.</summary>
        private const double PoligonKesitTakimYanPayCm = 155.0;
        /// <summary>Poligon kesit takımı üst: açılım + isim/donatı etiketleri.</summary>
        private const double PoligonKesitTakimUstPayCm = 195.0;
        /// <summary>Poligon kesit takımı alt: çiroz açılımı + notlar.</summary>
        private const double PoligonKesitTakimAltPayCm = 75.0;
        /// <summary>Kesit takımları (yazılar dahil) arası en az boşluk (cm).</summary>
        private const double PoligonKesitTakimAraCm = 100.0;
        /// <summary>Kompakt kesit ızgarası: görünüşten bağımsız kullanılabilecek en fazla yükseklik (cm).</summary>
        private const double PoligonKesitKompaktMaxYukseklikCm = 4000.0;
        /// <summary>Kesit takımlarının sağından en sol görünüşe (cm).</summary>
        private const double PoligonKesitTakimGorunusSolBoslukCm = 25.0;
        /// <summary>Poligon kesit ızgarasını görünüşe göre sola kaydır (cm).</summary>
        private const double PoligonKesitTakimSolaKaydirCm = 50.0;
        /// <summary>Poligon kolon görünüşleri arası (cm).</summary>
        private const double PoligonKesitTakimGorunusAraCm = 50.0;
        private const double KolonKesitEtriyeAcilimYanGapCm = 40.0;
        private const double KolonKesitEtriyeAcilimAltGapCm = 20.0;
        private const double KolonKesitCirozAcilimSagGapCm = 22.0;
        /// <summary>Kolon sağı 2. çiroz açılımını yukarı.</summary>
        private const double KolonKesitCirozAcilimIkinciYukariCm = 17.0;
        /// <summary>Perde çiroz adet notunu açılımın altına kaydır.</summary>
        private const double KolonKesitCirozAdetNotuAsagiCm = 12.0;
        /// <summary>Çiroz açılım gövdesi: kesit kenarından 3 cm pas (40 cm → 34 cm).</summary>
        private const double CirozAcilimPaspayiCm = 3.0;
        private const double PerdeGovdeCirozHcrPerM2 = 10.0;
        private const double PerdeGovdeCirozNormalPerM2 = 4.0;
        private const double PerdeKesitGovdeYatayAcilimAraCm = 18.0;
        private const double PerdeKesitGovdeYatayCiftAralikCm = 20.0;
        private const double PerdeKesitGovdeYataySasirtmaCm = 5.0;
        private const double KolonKesitEtriyeAcikAgizKancaMerkezAraCm = 12.0;
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

        private static Polygon TryKolonKesitAsPolygon(Geometry g)
        {
            if (g == null || g.IsEmpty) return null;
            if (g is Polygon gp && !gp.IsEmpty) return gp;
            Polygon best = null;
            double bestA = 0;
            if (g is GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                {
                    if (gc.GetGeometryN(i) is Polygon pi && !pi.IsEmpty && pi.Area > bestA)
                    {
                        best = pi;
                        bestA = pi.Area;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Poligon kolon kolları (L/T/U/C). Çizilmez; etriye/başlık için bellekte tutulur.
        /// </summary>
        private static List<(double x0, double y0, double x1, double y1)> CollectKolonPoligonParcaDortgenleri(Geometry g)
        {
            var p = TryKolonKesitAsPolygon(g);
            if (p == null) return new List<(double x0, double y0, double x1, double y1)>();
            return DecomposeOrthogonalPolygonToRects(p);
        }

        private Geometry TryGetKolonFloorPolygon(int floorIndex, ColumnAxisInfo col)
        {
            if (col == null || _model?.Floors == null) return null;
            if (floorIndex < 0 || floorIndex >= _model.Floors.Count) return null;
            var gf = _ntsDrawFactory;
            if (gf == null) return null;
            return GetColumnPolygonForTable(_model.Floors[floorIndex], col, 0, 0, gf);
        }

        /// <summary>
        /// Kesitteki renkli dörtgen içindeki tüm düşey donatılar (eşsiz X yok).
        /// </summary>
        private List<(double x, double y, int dia)> CollectPoligonArmKesitPts(
            Geometry fullPoly, Geometry viewPoly, AffineTransformation rot, int floorIndex, int colNo)
        {
            return CollectPoligonArmKesitPts(fullPoly, viewPoly, rot, floorIndex, colNo,
                out _, out _, out _);
        }

        /// <summary>
        /// Görünüş X'te kesitteki başlık izi (ℓuLo / ℓuHi, birleşim genişletmesi dahil).
        /// longIsX kol: iz MinX+ℓuLo ve MaxX−ℓuHi.
        /// </summary>
        private static bool TryPickPoligonArmBaslikSplitX(
            List<(Envelope e, bool longIsX, double luLo, double luHi, double ipLo, double ipHi)> koller,
            Envelope viewEnv,
            out double splitL, out double splitR)
        {
            splitL = 0;
            splitR = 0;
            if (koller == null || koller.Count == 0 || viewEnv == null) return false;
            int best = -1;
            double bestOv = 0;
            for (int i = 0; i < koller.Count; i++)
            {
                if (!koller[i].longIsX || koller[i].luLo < 8.0 || koller[i].luHi < 8.0) continue;
                var e = koller[i].e;
                if (e == null) continue;
                double ox0 = Math.Max(viewEnv.MinX, e.MinX), ox1 = Math.Min(viewEnv.MaxX, e.MaxX);
                double oy0 = Math.Max(viewEnv.MinY, e.MinY), oy1 = Math.Min(viewEnv.MaxY, e.MaxY);
                double ov = Math.Max(0.0, ox1 - ox0) * Math.Max(0.0, oy1 - oy0);
                if (ov > bestOv)
                {
                    bestOv = ov;
                    best = i;
                }
            }
            if (best < 0 || bestOv < 20.0) return false;
            var k = koller[best];
            splitL = k.e.MinX + k.luLo;
            splitR = k.e.MaxX - k.luHi;
            return splitR > splitL + 4.0;
        }

        private List<(double x, double y, int dia)> CollectPoligonArmKesitPts(
            Geometry fullPoly, Geometry viewPoly, AffineTransformation rot, int floorIndex, int colNo,
            out double splitL, out double splitR, out bool haveBaslikLu)
        {
            splitL = 0;
            splitR = 0;
            haveBaslikLu = false;
            var all = new List<(double x, double y, int dia)>();
            if (viewPoly == null || viewPoly.IsEmpty) return all;
            Geometry gFull = fullPoly;
            if (gFull == null || gFull.IsEmpty) gFull = viewPoly;
            Geometry gView = viewPoly;
            if (rot != null)
            {
                try { gFull = rot.Transform(gFull); } catch { }
                try { gView = rot.Transform(gView); } catch { }
            }
            if (gFull == null || gFull.IsEmpty || gView == null || gView.IsEmpty) return all;
            var rects = CollectKolonPoligonParcaDortgenleri(gFull);
            DrawPoligonKolonKesitEtriyeler(null, null, gFull, rects, floorIndex, colNo,
                out var koller, out var pts, out _, out _, out int ucDia, out int gvDia,
                out _, out double govdeSmax, out _);
            if (ucDia < 6) ucDia = 14;
            if (gvDia < 6) gvDia = 12;
            if (pts != null)
            {
                for (int i = 0; i < pts.Count; i++)
                    all.Add((pts[i].x, pts[i].y, ucDia));
            }
            if (koller != null)
            {
                double sMin = PoligonDonatiMerkezMinCm(ucDia);
                double sMax = govdeSmax > 2.0 ? govdeSmax : 20.0;
                for (int i = 0; i < koller.Count; i++)
                {
                    ForEachPoligonGovdeDuseyKonumOnKol(koller[i], pts, sMin, (x, y) =>
                    {
                        all.Add((x, y, gvDia));
                    }, sMax);
                }
            }
            var ve = gView.EnvelopeInternal;
            haveBaslikLu = TryPickPoligonArmBaslikSplitX(koller, ve, out splitL, out splitR);
            const double pad = 1.5;
            var inside = new List<(double x, double y, int dia)>();
            for (int i = 0; i < all.Count; i++)
            {
                double x = all[i].x, y = all[i].y;
                if (x < ve.MinX - pad || x > ve.MaxX + pad || y < ve.MinY - pad || y > ve.MaxY + pad)
                    continue;
                int d = all[i].dia >= 6 ? all[i].dia : ucDia;
                inside.Add((x, y, d));
            }
            return inside;
        }

        private SortedDictionary<int, int> CollectPoligonArmKesitAdetByDia(
            Geometry fullPoly, Geometry viewPoly, AffineTransformation rot, int floorIndex, int colNo)
        {
            var byDia = new SortedDictionary<int, int>();
            var pts = CollectPoligonArmKesitPts(fullPoly, viewPoly, rot, floorIndex, colNo);
            if (pts == null) return byDia;
            for (int i = 0; i < pts.Count; i++)
            {
                int d = pts[i].dia >= 6 ? pts[i].dia : 14;
                if (!byDia.ContainsKey(d)) byDia[d] = 0;
                byDia[d]++;
            }
            return byDia;
        }

        private bool PoligonArmGorunusBos(ColumnAxisInfo col, Geometry viewPoly, double viewAngDeg, double cx, double cy)
        {
            if (col == null || viewPoly == null || viewPoly.IsEmpty || _model?.Floors == null)
                return true;
            var rot = AffineTransformation.RotationInstance(-viewAngDeg * Math.PI / 180.0, cx, cy);
            for (int fi = 0; fi < _model.Floors.Count; fi++)
            {
                if (!HasColumnOnFloor(_model.Floors[fi], col)) continue;
                Geometry fullF = TryGetKolonFloorPolygon(fi, col) ?? viewPoly;
                var adet = CollectPoligonArmKesitAdetByDia(fullF, viewPoly, rot, fi, col.ColumnNo);
                if (adet == null) continue;
                foreach (var kv in adet)
                    if (kv.Value > 0) return false;
            }
            return true;
        }

        /// <summary>
        /// Kesitteki renkli dörtgen içindeki düşey donatıların görünüş X konumları (uzun yüz, eşsiz).
        /// </summary>
        private List<(double x, int dia)> CollectPoligonArmGorunusBars(
            Geometry fullPoly, Geometry viewPoly, AffineTransformation rot, int floorIndex, int colNo)
        {
            return CollectPoligonArmGorunusBars(fullPoly, viewPoly, rot, floorIndex, colNo,
                out _, out _, out _);
        }

        private List<(double x, int dia)> CollectPoligonArmGorunusBars(
            Geometry fullPoly, Geometry viewPoly, AffineTransformation rot, int floorIndex, int colNo,
            out double splitL, out double splitR, out bool haveBaslikLu)
        {
            var bars = new List<(double x, int dia)>();
            var pts = CollectPoligonArmKesitPts(fullPoly, viewPoly, rot, floorIndex, colNo,
                out splitL, out splitR, out haveBaslikLu);
            if (pts == null) return bars;
            for (int i = 0; i < pts.Count; i++)
            {
                double x = pts[i].x;
                int d = pts[i].dia >= 6 ? pts[i].dia : 14;
                bool have = false;
                for (int j = 0; j < bars.Count; j++)
                {
                    if (Math.Abs(bars[j].x - x) < 0.8)
                    {
                        if (d > bars[j].dia)
                            bars[j] = (bars[j].x, d);
                        have = true;
                        break;
                    }
                }
                if (!have) bars.Add((x, d));
            }
            bars.Sort((a, b) => a.x.CompareTo(b.x));
            return bars;
        }

        /// <summary>
        /// Parça ebatına göre yalnız perde uç etriyesi / kolon 1. etriye.
        /// </summary>
        private void DrawPoligonKolonKesitEtriyeler(
            Transaction tr,
            BlockTableRecord btr,
            Geometry g,
            List<(double x0, double y0, double x1, double y1)> rects,
            int floorIndex,
            int colNo,
            out List<(Envelope e, bool longIsX, double luLo, double luHi, double ipLo, double ipHi)> koller,
            out List<(double x, double y)> pts,
            out int nUc,
            out int nGovde,
            out int ucDia,
            out int gvDia,
            out List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)> etBoxes,
            out double govdeSmax,
            out List<(double stem, bool govde)> cirozStems)
        {
            koller = new List<(Envelope e, bool longIsX, double luLo, double luHi, double ipLo, double ipHi)>();
            pts = new List<(double x, double y)>();
            etBoxes = new List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)>();
            nUc = 0;
            nGovde = 0;
            ucDia = 14;
            gvDia = 12;
            govdeSmax = 20.0;
            cirozStems = new List<(double stem, bool govde)>();
            if (rects != null)
            {
                for (int i = 0; i < rects.Count; i++)
                {
                    var r = rects[i];
                    double x0 = Math.Min(r.x0, r.x1), x1 = Math.Max(r.x0, r.x1);
                    double y0 = Math.Min(r.y0, r.y1), y1 = Math.Max(r.y0, r.y1);
                    double w = x1 - x0, h = y1 - y0;
                    if (w < 4.0 || h < 4.0) continue;
                    var env = new Envelope(x0, x1, y0, y1);
                    if (IsDepremPerdeBoyOrani(Math.Max(w, h), Math.Min(w, h)))
                        DrawPoligonPerdeKolUcEtriye(tr, btr, env, floorIndex, colNo, rects, etBoxes, pts, koller);
                    else
                        DrawPoligonKolonBirinciEtriye(tr, btr, env, floorIndex, colNo, etBoxes);
                }
            }
            var overlapAnchors = new List<(double x, double y)>();
            PlacePoligonEtriyeIcKesisimDonatisi(pts, overlapAnchors, etBoxes);
            PlacePoligonEtriyeKoseDonati(etBoxes, pts);
            ClampPoligonEtriyeKapsamaIciDonati(etBoxes, pts);
            string donati0 = null;
            if (_kolonDuseyGpr != null && _model?.Floors != null)
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(
                    _kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out donati0, out _);
            KolonDonatiTableDrawer.SumKolonKesitDuseyDonatiAdet(donati0, out ucDia);
            if (ucDia < 6) ucDia = 14;
            gvDia = KolonDonatiTableDrawer.MinKolonKesitDuseyDonatiCapMm(donati0);
            if (gvDia < 6) gvDia = 12;
            double sMin = PoligonDonatiMerkezMinCm(ucDia);
            FillPoligonEtriyeKenarMaxAralik(etBoxes, pts, overlapAnchors, 20.0, sMin);
            EnsurePoligonKoseBaslikAdet(pts, etBoxes, rects, floorIndex, colNo, sMin);
            PlacePoligonKolonFormatDuseyDonati(rects, pts, floorIndex, colNo, sMin, etBoxes);
            govdeSmax = EnsurePoligonKesitAsEnAzGpr(pts, etBoxes, koller, rects, floorIndex, colNo, sMin);
            EnforcePoligonDonatiNetMinAralik(etBoxes, pts, sMin);
            PlacePoligonEtriyeKoseDonati(etBoxes, pts);
            EqualizePoligonKolonFormatKenarlari(rects, pts, overlapAnchors);
            PlacePoligonEtriyeKoseDonati(etBoxes, pts);
            PlacePoligonEtriyeIcKesisimDonatisi(pts, overlapAnchors, etBoxes);
            if (tr != null && btr != null)
            {
                for (int i = 0; i < pts.Count; i++)
                    DrawTwoArcCirclePline(tr, btr, pts[i].x, pts[i].y, KolonKesitDuseyDonatiRadiusCm, LayerDonatiGovde);
                nGovde = DrawPoligonUcPursantajVeGovde(tr, btr, koller, pts, floorIndex, colNo, sMin, govdeSmax);
                DrawPoligonKolonKesitCirozlar(tr, btr, rects, koller, etBoxes, pts, floorIndex, colNo, cirozStems);
            }
            else
                nGovde = CountPoligonGovdeDusey(koller, pts, sMin, govdeSmax);
            nUc = pts.Count;
        }

        /// <summary>TBDY 2018 Şekil 7.6: birleşimde uç, iç köşeden ℓu (Hcr/normal) ve ≥ max(bw, 30 cm).</summary>
        private const double PerdeBirlesimMinKenarCm = 30.0;

        /// <summary>
        /// Poligon perde kolu ℓu: serbest uçta TBDY ℓu (Hcr’de max(2bw, 0.2ℓw), normalde max(bw, 0.1ℓw)).
        /// Birleşimde dış yüzden t + max(ℓu, max(bw, 30 cm)) — kritik/normal iz farklı kalır.
        /// </summary>
        private void ComputePoligonPerdeKolLu(
            Envelope e, int floorIndex, int colNo,
            List<(double x0, double y0, double x1, double y1)> allRects,
            bool longIsX,
            out double luLo, out double luHi)
        {
            if (e == null)
            {
                luLo = 0;
                luHi = 0;
                return;
            }
            double lw = Math.Max(e.Width, e.Height);
            double bw = Math.Min(e.Width, e.Height);
            double lu = ResolvePerdeUcBolgeLuCm(lw, bw, floorIndex, colNo);
            luLo = lu;
            luHi = lu;
            if (lu < 8.0) return;
            double jMin = Math.Max(bw, PerdeBirlesimMinKenarCm);
            double tLo = PoligonKolBirlestirmeKalinlikCm(
                e.MinX, e.MinY, e.MaxX, e.MaxY, allRects, longIsX, atMin: true);
            double tHi = PoligonKolBirlestirmeKalinlikCm(
                e.MinX, e.MinY, e.MaxX, e.MaxY, allRects, longIsX, atMin: false);
            if (tLo >= 4.0)
                luLo = tLo + Math.Max(lu, jMin);
            if (tHi >= 4.0)
                luHi = tHi + Math.Max(lu, jMin);
            double room = lw - 10.0;
            if (luLo + luHi > room && room > 16.0)
            {
                double f = room / (luLo + luHi);
                luLo *= f;
                luHi *= f;
            }
        }

        /// <summary>Poligon perde kolu: başlık çizgileri + uç etriyeleri. Hcr ve normal kat ℓu ayrı.</summary>
        private void DrawPoligonPerdeKolUcEtriye(
            Transaction tr, BlockTableRecord btr, Envelope e, int floorIndex, int colNo,
            List<(double x0, double y0, double x1, double y1)> allRects,
            List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)> etBoxes,
            List<(double x, double y)> pts,
            List<(Envelope e, bool longIsX, double luLo, double luHi, double ipLo, double ipHi)> koller)
        {
            if (e == null) return;
            double lw = Math.Max(e.Width, e.Height);
            double bw = Math.Min(e.Width, e.Height);
            if (bw < 8.0 || !IsDepremPerdeBoyOrani(lw, bw)) return;
            bool longIsX = e.Width >= e.Height;
            ComputePoligonPerdeKolLu(e, floorIndex, colNo, allRects, longIsX, out double luLo, out double luHi);
            if (luLo < 8.0 || luHi < 8.0) return;
            double tLo = PoligonKolBirlestirmeKalinlikCm(
                e.MinX, e.MinY, e.MaxX, e.MaxY, allRects, longIsX, atMin: true);
            double tHi = PoligonKolBirlestirmeKalinlikCm(
                e.MinX, e.MinY, e.MaxX, e.MaxY, allRects, longIsX, atMin: false);
            double pas = KolonKesitPaspayiCm;
            double rad = KolonKesitEtriyeRadiusCm;
            int diaMm = ResolveKolonDuseyEtriyeDiaMm(floorIndex, colNo);
            double hook = TbdY2018EtriyeHookExtCm(diaMm);
            double minInner = 2.0 * rad + 2.0;
            double InnerPas(double luUse)
            {
                if (luUse - 2.0 * pas < minInner)
                    return Math.Max(0.0, (luUse - minInner) * 0.5);
                return pas;
            }

            void Dash(double ax, double ay, double bx, double by)
            {
                if (tr == null || btr == null) return;
                var line = new Line(new Point3d(ax, ay, 0), new Point3d(bx, by, 0));
                line.SetDatabaseDefaults();
                line.Layer = LayerKesitGorunus;
                line.LineWeight = LineWeight.LineWeight020;
                AppendEntity(tr, btr, line);
            }
            void UcEtriye(double ax, double ay, double bx, double by, bool innerIsMax)
            {
                if (bx < ax) { double t = ax; ax = bx; bx = t; }
                if (by < ay) { double t = ay; ay = by; by = t; }
                if (bx - ax < 2.0 * rad + 2.0 || by - ay < 2.0 * rad + 2.0) return;
                if (tr != null && btr != null)
                    AppendKolonKesitEtriyePline(tr, btr, ax, ay, bx, by, rad, hook);
                if (etBoxes != null)
                    etBoxes.Add((ax, ay, bx, by, longIsX, true, innerIsMax));
            }

            if (longIsX)
            {
                Dash(e.MinX + luLo, e.MinY, e.MinX + luLo, e.MaxY);
                Dash(e.MaxX - luHi, e.MinY, e.MaxX - luHi, e.MaxY);
                UcEtriye(e.MinX + pas, e.MinY + pas, e.MinX + luLo - InnerPas(luLo), e.MaxY - pas, innerIsMax: true);
                UcEtriye(e.MaxX - luHi + InnerPas(luHi), e.MinY + pas, e.MaxX - pas, e.MaxY - pas, innerIsMax: false);
                if (!PoligonUcOverlapsKolonFormat(e, longIsX, true, luLo, allRects))
                    PlacePoligonBaslikMinDuseyDonati(pts, e, floorIndex, colNo, luLo, InnerPas(luLo), longIsX, atMin: true, bw, outerIsJunction: tLo >= 4.0);
                if (!PoligonUcOverlapsKolonFormat(e, longIsX, false, luHi, allRects))
                    PlacePoligonBaslikMinDuseyDonati(pts, e, floorIndex, colNo, luHi, InnerPas(luHi), longIsX, atMin: false, bw, outerIsJunction: tHi >= 4.0);
            }
            else
            {
                Dash(e.MinX, e.MinY + luLo, e.MaxX, e.MinY + luLo);
                Dash(e.MinX, e.MaxY - luHi, e.MaxX, e.MaxY - luHi);
                UcEtriye(e.MinX + pas, e.MinY + pas, e.MaxX - pas, e.MinY + luLo - InnerPas(luLo), innerIsMax: true);
                UcEtriye(e.MinX + pas, e.MaxY - luHi + InnerPas(luHi), e.MaxX - pas, e.MaxY - pas, innerIsMax: false);
                if (!PoligonUcOverlapsKolonFormat(e, longIsX, true, luLo, allRects))
                    PlacePoligonBaslikMinDuseyDonati(pts, e, floorIndex, colNo, luLo, InnerPas(luLo), longIsX, atMin: true, bw, outerIsJunction: tLo >= 4.0);
                if (!PoligonUcOverlapsKolonFormat(e, longIsX, false, luHi, allRects))
                    PlacePoligonBaslikMinDuseyDonati(pts, e, floorIndex, colNo, luHi, InnerPas(luHi), longIsX, atMin: false, bw, outerIsJunction: tHi >= 4.0);
            }
            if (koller != null)
                koller.Add((e, longIsX, luLo, luHi, InnerPas(luLo), InnerPas(luHi)));
        }

        /// <summary>
        /// Gövde düşey/yatay. Gövde çapı = GPR düşey çaplarının küçüğü; merkez aralığı ≤ 20 cm.
        /// </summary>
        private int DrawPoligonUcPursantajVeGovde(
            Transaction tr, BlockTableRecord btr,
            List<(Envelope e, bool longIsX, double luLo, double luHi, double ipLo, double ipHi)> koller,
            List<(double x, double y)> pts,
            int floorIndex, int colNo, double sMinCc = 2.0, double sMaxCc = 20.0)
        {
            if (tr == null || btr == null || koller == null || koller.Count == 0) return 0;
            if (sMinCc < 2.0) sMinCc = 2.0;
            int nGovde = 0;
            double kenar = PerdeKesitYatayKenarCm;
            int etDia = ResolveKolonDuseyEtriyeDiaMm(floorIndex, colNo);

            for (int i = 0; i < koller.Count; i++)
            {
                var k = koller[i];
                var e = k.e;
                if (e == null) continue;

                if (k.longIsX)
                {
                    ForEachPoligonGovdeDuseyKonumOnKol(k, pts, sMinCc, (x, y) =>
                    {
                        DrawTwoArcCirclePline(tr, btr, x, y, KolonKesitDuseyDonatiRadiusCm, LayerDonatiGovde);
                        nGovde++;
                    }, sMaxCc);

                    double x0 = e.MinX + kenar, x1 = e.MaxX - kenar;
                    double y0 = e.MinY + kenar, y1 = e.MaxY - kenar;
                    if (x1 - x0 > 10.0 && y1 - y0 > 2.0)
                    {
                        double fck = _rebarFckMPa > 16.0 ? _rebarFckMPa : 30.0;
                        double fyk = _rebarFykMPa > 200.0 ? _rebarFykMPa : 420.0;
                        double lb = Ts500KenetlenmeLbCm(etDia, fck, fyk);
                        bool gonyeLo = (k.luLo - kenar) + 0.01 < lb;
                        bool gonyeHi = (k.luHi - kenar) + 0.01 < lb;
                        double phi12 = 0.0;
                        if (gonyeLo || gonyeHi)
                        {
                            phi12 = CeilTo5Cm(12.0 * etDia / 10.0);
                            if (phi12 > (y1 - y0) * 0.45) phi12 = Math.Max(2.0, (y1 - y0) * 0.45);
                        }
                        DrawPerdeKesitYatayBar(tr, btr, x0, y0, x1, y0, gonyeLo, gonyeHi, phi12, inwardPlus: true);
                        DrawPerdeKesitYatayBar(tr, btr, x0, y1, x1, y1, gonyeLo, gonyeHi, phi12, inwardPlus: false);
                    }
                }
                else
                {
                    ForEachPoligonGovdeDuseyKonumOnKol(k, pts, sMinCc, (x, y) =>
                    {
                        DrawTwoArcCirclePline(tr, btr, x, y, KolonKesitDuseyDonatiRadiusCm, LayerDonatiGovde);
                        nGovde++;
                    }, sMaxCc);

                    double x0 = e.MinX + kenar, x1 = e.MaxX - kenar;
                    double y0 = e.MinY + kenar, y1 = e.MaxY - kenar;
                    if (x1 - x0 > 2.0 && y1 - y0 > 10.0)
                    {
                        double fck = _rebarFckMPa > 16.0 ? _rebarFckMPa : 30.0;
                        double fyk = _rebarFykMPa > 200.0 ? _rebarFykMPa : 420.0;
                        double lb = Ts500KenetlenmeLbCm(etDia, fck, fyk);
                        bool gonyeLo = (k.luLo - kenar) + 0.01 < lb;
                        bool gonyeHi = (k.luHi - kenar) + 0.01 < lb;
                        double phi12 = 0.0;
                        if (gonyeLo || gonyeHi)
                        {
                            phi12 = CeilTo5Cm(12.0 * etDia / 10.0);
                            if (phi12 > (x1 - x0) * 0.45) phi12 = Math.Max(2.0, (x1 - x0) * 0.45);
                        }
                        DrawPerdeKesitYatayBar(tr, btr, x0, y0, x0, y1, gonyeLo, gonyeHi, phi12, inwardPlus: true);
                        DrawPerdeKesitYatayBar(tr, btr, x1, y0, x1, y1, gonyeLo, gonyeHi, phi12, inwardPlus: false);
                    }
                }
            }
            return nGovde;
        }

        /// <summary>Poligon kesit: TBDY 7.3.4 çiroz (25φ / kare 23φ). Kolon dörtgeni iki yön, perde uç+gövde kalınlık yönü. Kesişim dörtgenine çiroz yok.</summary>
        private void DrawPoligonKolonKesitCirozlar(
            Transaction tr, BlockTableRecord btr,
            List<(double x0, double y0, double x1, double y1)> rects,
            List<(Envelope e, bool longIsX, double luLo, double luHi, double ipLo, double ipHi)> koller,
            List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)> etBoxes,
            List<(double x, double y)> pts,
            int floorIndex, int colNo,
            List<(double stem, bool govde)> acilimStems = null)
        {
            if (tr == null || btr == null) return;
            int diaMm = ResolveKolonDuseyEtriyeDiaMm(floorIndex, colNo);
            double hook135 = TbdY2018EtriyeHookExtCm(diaMm);
            double pas = KolonKesitPaspayiCm;
            double rad = KolonKesitEtriyeRadiusCm;
            double barLo = pas + rad;
            var overlaps = CollectPoligonEtriyeOverlaps(etBoxes);

            if (rects != null)
            {
                for (int i = 0; i < rects.Count; i++)
                {
                    var r = rects[i];
                    double rx0 = Math.Min(r.x0, r.x1), rx1 = Math.Max(r.x0, r.x1);
                    double ry0 = Math.Min(r.y0, r.y1), ry1 = Math.Max(r.y0, r.y1);
                    double w = rx1 - rx0, h = ry1 - ry0;
                    if (w < 8.0 || h < 8.0) continue;
                    if (IsDepremPerdeBoyOrani(Math.Max(w, h), Math.Min(w, h))) continue;
                    double xLo = rx0 + barLo, xHi = rx1 - barLo;
                    double yLo = ry0 + barLo, yHi = ry1 - barLo;
                    ClipPoligonCirozBoxAgainstOverlaps(ref xLo, ref yLo, ref xHi, ref yHi, overlaps);
                    DrawPoligonCirozInBarBox(tr, btr, xLo, yLo, xHi, yHi, pts, diaMm, hook135, true, true, overlaps, w, h, acilimStems, govde: false);
                }
            }

            if (koller == null) return;
            for (int i = 0; i < koller.Count; i++)
            {
                var k = koller[i];
                var e = k.e;
                if (e == null) continue;
                double yBot = e.MinY + barLo, yTop = e.MaxY - barLo;
                double xL = e.MinX + barLo, xR = e.MaxX - barLo;
                if (k.longIsX)
                {
                    double xA0 = e.MinX + barLo, xA1 = e.MinX + k.luLo - k.ipLo - rad;
                    double yA0 = yBot, yA1 = yTop;
                    ClipPoligonCirozBoxAgainstOverlaps(ref xA0, ref yA0, ref xA1, ref yA1, overlaps);
                    DrawPoligonCirozInBarBox(tr, btr, xA0, yA0, xA1, yA1, pts, diaMm, hook135, true, true, overlaps, 0, 0, acilimStems, govde: false);
                    double xB0 = e.MaxX - k.luHi + k.ipHi + rad, xB1 = e.MaxX - barLo;
                    double yB0 = yBot, yB1 = yTop;
                    ClipPoligonCirozBoxAgainstOverlaps(ref xB0, ref yB0, ref xB1, ref yB1, overlaps);
                    DrawPoligonCirozInBarBox(tr, btr, xB0, yB0, xB1, yB1, pts, diaMm, hook135, true, true, overlaps, 0, 0, acilimStems, govde: false);
                    var xsG = new List<double>();
                    ForEachPoligonGovdeDuseyKonumOnKol(k, pts, 2.0, (x, y) =>
                    {
                        if (Math.Abs(y - yTop) < 2.0 || Math.Abs(y - yBot) < 2.0)
                        {
                            bool dup = false;
                            for (int t = 0; t < xsG.Count; t++)
                            {
                                if (Math.Abs(xsG[t] - x) < 1.0) { dup = true; break; }
                            }
                            if (!dup) xsG.Add(x);
                        }
                    });
                    double innerL = e.MinX + k.luLo - k.ipLo - rad;
                    double innerR = e.MaxX - k.luHi + k.ipHi + rad;
                    DrawPerdeKesitGovdeCirozX(tr, btr, xsG.ToArray(), innerL, innerR, yBot, yTop, diaMm, hook135, acilimStems);
                }
                else
                {
                    double xA0 = xL, xA1 = xR, yA0 = e.MinY + barLo, yA1 = e.MinY + k.luLo - k.ipLo - rad;
                    ClipPoligonCirozBoxAgainstOverlaps(ref xA0, ref yA0, ref xA1, ref yA1, overlaps);
                    DrawPoligonCirozInBarBox(tr, btr, xA0, yA0, xA1, yA1, pts, diaMm, hook135, true, true, overlaps, 0, 0, acilimStems, govde: false);
                    double xB0 = xL, xB1 = xR, yB0 = e.MaxY - k.luHi + k.ipHi + rad, yB1 = e.MaxY - barLo;
                    ClipPoligonCirozBoxAgainstOverlaps(ref xB0, ref yB0, ref xB1, ref yB1, overlaps);
                    DrawPoligonCirozInBarBox(tr, btr, xB0, yB0, xB1, yB1, pts, diaMm, hook135, true, true, overlaps, 0, 0, acilimStems, govde: false);
                    var ysG = new List<double>();
                    ForEachPoligonGovdeDuseyKonumOnKol(k, pts, 2.0, (x, y) =>
                    {
                        if (Math.Abs(x - xL) < 2.0 || Math.Abs(x - xR) < 2.0)
                        {
                            bool dup = false;
                            for (int t = 0; t < ysG.Count; t++)
                            {
                                if (Math.Abs(ysG[t] - y) < 1.0) { dup = true; break; }
                            }
                            if (!dup) ysG.Add(y);
                        }
                    });
                    double innerB = e.MinY + k.luLo - k.ipLo - rad;
                    double innerT = e.MaxY - k.luHi + k.ipHi + rad;
                    DrawPerdeKesitGovdeCirozY(tr, btr, ysG.ToArray(), innerB, innerT, xL, xR, diaMm, hook135, acilimStems);
                }
            }
        }

        private static List<(double x0, double y0, double x1, double y1)> CollectPoligonEtriyeOverlaps(
            List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)> etBoxes)
        {
            var overlaps = new List<(double x0, double y0, double x1, double y1)>();
            if (etBoxes == null) return overlaps;
            for (int i = 0; i < etBoxes.Count; i++)
            {
                var a = etBoxes[i];
                for (int j = i + 1; j < etBoxes.Count; j++)
                {
                    var b = etBoxes[j];
                    double ix0 = Math.Max(a.x0, b.x0), ix1 = Math.Min(a.x1, b.x1);
                    double iy0 = Math.Max(a.y0, b.y0), iy1 = Math.Min(a.y1, b.y1);
                    if (ix1 - ix0 >= 2.0 && iy1 - iy0 >= 2.0)
                        overlaps.Add((ix0, iy0, ix1, iy1));
                }
            }
            return overlaps;
        }

        /// <summary>Çiroz kutusunu etriye kesişiminden çıkarır.</summary>
        private static void ClipPoligonCirozBoxAgainstOverlaps(
            ref double xLo, ref double yLo, ref double xHi, ref double yHi,
            List<(double x0, double y0, double x1, double y1)> overlaps)
        {
            if (overlaps == null || overlaps.Count == 0) return;
            if (xHi < xLo) { double t = xLo; xLo = xHi; xHi = t; }
            if (yHi < yLo) { double t = yLo; yLo = yHi; yHi = t; }
            for (int i = 0; i < overlaps.Count; i++)
            {
                var o = overlaps[i];
                double ix0 = Math.Max(xLo, o.x0), ix1 = Math.Min(xHi, o.x1);
                double iy0 = Math.Max(yLo, o.y0), iy1 = Math.Min(yHi, o.y1);
                if (ix1 - ix0 < 2.0 || iy1 - iy0 < 2.0) continue;
                double bw = xHi - xLo, bh = yHi - yLo;
                if (ix1 - ix0 > 0.45 * bw)
                {
                    if (iy0 <= yLo + 2.0 && iy1 < yHi - 2.0) yLo = iy1;
                    else if (iy1 >= yHi - 2.0 && iy0 > yLo + 2.0) yHi = iy0;
                }
                if (iy1 - iy0 > 0.45 * bh)
                {
                    if (ix0 <= xLo + 2.0 && ix1 < xHi - 2.0) xLo = ix1;
                    else if (ix1 >= xHi - 2.0 && ix0 > xLo + 2.0) xHi = ix0;
                }
            }
        }

        /// <summary>Çiroz gövdesini kesişim dörtgeninin dışına kısaltır. Kalan boy yetersizse false.</summary>
        private static bool TryClipPoligonCirozSpanAgainstOverlaps(
            bool vertical, double pos, ref double a0, ref double a1,
            List<(double x0, double y0, double x1, double y1)> overlaps)
        {
            if (overlaps == null || overlaps.Count == 0) return a1 - a0 >= 4.0;
            if (a1 < a0) { double t = a0; a0 = a1; a1 = t; }
            for (int i = 0; i < overlaps.Count; i++)
            {
                var o = overlaps[i];
                if (vertical)
                {
                    if (pos <= o.x0 + 1.0 || pos >= o.x1 - 1.0) continue;
                    double iy0 = Math.Max(a0, o.y0), iy1 = Math.Min(a1, o.y1);
                    if (iy1 - iy0 < 2.0) continue;
                    if (iy0 <= a0 + 2.0 && iy1 >= a1 - 2.0) return false;
                    if (iy0 <= a0 + 2.0) a0 = iy1;
                    else if (iy1 >= a1 - 2.0) a1 = iy0;
                }
                else
                {
                    if (pos <= o.y0 + 1.0 || pos >= o.y1 - 1.0) continue;
                    double ix0 = Math.Max(a0, o.x0), ix1 = Math.Min(a1, o.x1);
                    if (ix1 - ix0 < 2.0) continue;
                    if (ix0 <= a0 + 2.0 && ix1 >= a1 - 2.0) return false;
                    if (ix0 <= a0 + 2.0) a0 = ix1;
                    else if (ix1 >= a1 - 2.0) a1 = ix0;
                }
            }
            return a1 - a0 >= 4.0;
        }

        private void DrawPoligonCirozInBarBox(
            Transaction tr, BlockTableRecord btr,
            double xLo, double yLo, double xHi, double yHi,
            List<(double x, double y)> pts,
            int diaMm, double hook135, bool drawAlongX, bool drawAlongY,
            List<(double x0, double y0, double x1, double y1)> overlaps = null,
            double concreteW = 0, double concreteH = 0,
            List<(double stem, bool govde)> acilimStems = null,
            bool govde = false)
        {
            if (tr == null || btr == null) return;
            if (xHi < xLo) { double t = xLo; xLo = xHi; xHi = t; }
            if (yHi < yLo) { double t = yLo; yLo = yHi; yHi = t; }
            if (xHi - xLo < 4.0 || yHi - yLo < 4.0) return;
            double colW = concreteW > 1.0 ? concreteW : (xHi - xLo);
            double colH = concreteH > 1.0 ? concreteH : (yHi - yLo);
            bool kare = Math.Abs(colW - colH) < 3.0;
            double aMax = (kare ? KolonKesitCirozAMaxFiKare : KolonKesitCirozAMaxFi) * Math.Max(diaMm, 8) / 10.0;
            const double face = 1.8;
            const double tol = 1.0;
            var xs = new List<double> { xLo, xHi };
            var ys = new List<double> { yLo, yHi };
            if (pts != null)
            {
                for (int i = 0; i < pts.Count; i++)
                {
                    double x = pts[i].x, y = pts[i].y;
                    if (x >= xLo - 1.0 && x <= xHi + 1.0 &&
                        (Math.Abs(y - yLo) <= face || Math.Abs(y - yHi) <= face))
                    {
                        bool dup = false;
                        for (int j = 0; j < xs.Count; j++)
                        {
                            if (Math.Abs(xs[j] - x) < tol) { dup = true; break; }
                        }
                        if (!dup) xs.Add(x);
                    }
                    if (y >= yLo - 1.0 && y <= yHi + 1.0 &&
                        (Math.Abs(x - xLo) <= face || Math.Abs(x - xHi) <= face))
                    {
                        bool dup = false;
                        for (int j = 0; j < ys.Count; j++)
                        {
                            if (Math.Abs(ys[j] - y) < tol) { dup = true; break; }
                        }
                        if (!dup) ys.Add(y);
                    }
                }
            }
            xs.Sort();
            ys.Sort();
            double cr = KolonKesitCirozRadiusCm;
            double hook90 = KolonKesitCirozHook90CizimCm;
            double midX = 0.5 * (xLo + xHi);
            double midY = 0.5 * (yLo + yHi);
            if (drawAlongY)
            {
                var pick = PickCirozBarCenters(xs, new List<double> { xLo, xHi }, aMax, tol);
                pick.Sort();
                for (int i = 0; i < pick.Count; i++)
                {
                    double y0c = yLo, y1c = yHi;
                    if (!TryClipPoligonCirozSpanAgainstOverlaps(true, pick[i], ref y0c, ref y1c, overlaps))
                        continue;
                    DrawKolonKesitCirozC(tr, btr, pick[i], y0c, y1c, cr, hook90, hook135,
                        leftLeg: pick[i] <= midX, hook135OnTop: i % 2 == 0);
                    if (acilimStems != null)
                        acilimStems.Add((CirozAcilimStemCm(Math.Abs(y1c - y0c)), govde));
                }
            }
            if (drawAlongX)
            {
                var pick = PickCirozBarCenters(ys, new List<double> { yLo, yHi }, aMax, tol);
                pick.Sort();
                for (int j = 0; j < pick.Count; j++)
                {
                    double x0c = xLo, x1c = xHi;
                    if (!TryClipPoligonCirozSpanAgainstOverlaps(false, pick[j], ref x0c, ref x1c, overlaps))
                        continue;
                    DrawKolonKesitCirozCHorizontal(tr, btr, x0c, x1c, pick[j], cr, hook90, hook135,
                        bottomLeg: pick[j] <= midY, hook135OnRight: j % 2 == 0);
                    if (acilimStems != null)
                        acilimStems.Add((CirozAcilimStemCm(Math.Abs(x1c - x0c)), govde));
                }
            }
        }

        /// <summary>Gövde düşey konumları (uç etriye iç yüzleri arası, s ≤ 20 cm). Başlık çubuğuna denk gelen atlanır.</summary>
        private static void ForEachPoligonGovdeDuseyKonumOnKol(
            (Envelope e, bool longIsX, double luLo, double luHi, double ipLo, double ipHi) k,
            List<(double x, double y)> pts,
            Action<double, double> onBar)
        {
            ForEachPoligonGovdeDuseyKonumOnKol(k, pts, 2.0, onBar, 20.0);
        }

        private static void ForEachPoligonGovdeDuseyKonumOnKol(
            (Envelope e, bool longIsX, double luLo, double luHi, double ipLo, double ipHi) k,
            List<(double x, double y)> pts,
            double minCc,
            Action<double, double> onBar,
            double sMax = 20.0)
        {
            if (onBar == null || k.e == null) return;
            if (minCc < 2.0) minCc = 2.0;
            if (sMax < minCc) sMax = minCc;
            double pas = KolonKesitPaspayiCm;
            double rad = KolonKesitEtriyeRadiusCm;
            double barLo = pas + rad;
            var e = k.e;
            bool NearPts(double x, double y)
            {
                if (pts == null) return false;
                double m2 = minCc * minCc;
                for (int i = 0; i < pts.Count; i++)
                {
                    double dx = pts[i].x - x, dy = pts[i].y - y;
                    if (dx * dx + dy * dy < m2) return true;
                }
                return false;
            }
            if (k.longIsX)
            {
                double yBot = e.MinY + barLo, yTop = e.MaxY - barLo;
                double innerL = e.MinX + k.luLo - k.ipLo - rad;
                double innerR = e.MaxX - k.luHi + k.ipHi + rad;
                double span = innerR - innerL;
                if (span <= sMax + 0.01) return;
                int n = (int)Math.Ceiling(span / sMax - 1e-9) - 1;
                if (n < 1) n = 1;
                while (span / (n + 1) > sMax + 0.01) n++;
                var xsG = PerdeKesitAraKonumlar(innerL, innerR, n);
                for (int j = 0; j < xsG.Length; j++)
                {
                    if (!NearPts(xsG[j], yBot)) onBar(xsG[j], yBot);
                    if (!NearPts(xsG[j], yTop)) onBar(xsG[j], yTop);
                }
            }
            else
            {
                double xL = e.MinX + barLo, xR = e.MaxX - barLo;
                double innerB = e.MinY + k.luLo - k.ipLo - rad;
                double innerT = e.MaxY - k.luHi + k.ipHi + rad;
                double span = innerT - innerB;
                if (span <= sMax + 0.01) return;
                int n = (int)Math.Ceiling(span / sMax - 1e-9) - 1;
                if (n < 1) n = 1;
                while (span / (n + 1) > sMax + 0.01) n++;
                var ysG = PerdeKesitAraKonumlar(innerB, innerT, n);
                for (int j = 0; j < ysG.Length; j++)
                {
                    if (!NearPts(xL, ysG[j])) onBar(xL, ysG[j]);
                    if (!NearPts(xR, ysG[j])) onBar(xR, ysG[j]);
                }
            }
        }

        private static int CountPoligonGovdeDusey(
            List<(Envelope e, bool longIsX, double luLo, double luHi, double ipLo, double ipHi)> koller,
            List<(double x, double y)> pts,
            double minCc = 2.0,
            double sMax = 20.0)
        {
            if (koller == null) return 0;
            int n = 0;
            for (int i = 0; i < koller.Count; i++)
                ForEachPoligonGovdeDuseyKonumOnKol(koller[i], pts, minCc, (x, y) => n++, sMax);
            return n;
        }

        /// <summary>
        /// Kesit As ≥ GPR As. Başlıkta nMin (KR ρ) doluysa ekleme; açığı gövde aralığını sıkıştırarak kapat.
        /// </summary>
        private double EnsurePoligonKesitAsEnAzGpr(
            List<(double x, double y)> pts,
            List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)> etBoxes,
            List<(Envelope e, bool longIsX, double luLo, double luHi, double ipLo, double ipHi)> koller,
            List<(double x0, double y0, double x1, double y1)> rects,
            int floorIndex,
            int colNo,
            double sMinCc = 0)
        {
            double govdeSmax = 20.0;
            if (pts == null || etBoxes == null || etBoxes.Count == 0) return govdeSmax;
            string donati = null;
            if (_kolonDuseyGpr != null && _model?.Floors != null)
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(
                    _kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out donati, out _);
            double gprAs = KolonDonatiTableDrawer.SumKolonKesitDuseyDonatiAlanCm2(donati);
            if (gprAs < 0.05)
            {
                string donText = null;
                if (TryResolveKolonAltKatDuseyDonati(floorIndex, colNo, out _, out _, out _, out string drawOzet) &&
                    !string.IsNullOrWhiteSpace(drawOzet))
                    donText = drawOzet;
                else
                    donText = KolonDonatiTableDrawer.FormatKolonKesitDuseyDonatiOzet(donati);
                gprAs = KolonDonatiTableDrawer.SumKolonKesitDuseyDonatiAlanCm2(donText);
            }
            if (gprAs < 0.05) return govdeSmax;

            int ucDia = 14;
            KolonDonatiTableDrawer.SumKolonKesitDuseyDonatiAdet(donati, out ucDia);
            if (ucDia < 6) ucDia = 14;
            int gvDia = KolonDonatiTableDrawer.MinKolonKesitDuseyDonatiCapMm(donati);
            if (gvDia < 6) gvDia = 12;
            double aUc = Math.PI * ucDia * ucDia / 400.0;
            double aGv = Math.PI * gvDia * gvDia / 400.0;
            if (aUc < 0.05) aUc = 1.54;
            if (aGv < 0.05) aGv = 1.13;
            double minCcGv = sMinCc > 0.5 ? sMinCc : PoligonDonatiMerkezMinCm(gvDia);

            double KesitAs()
            {
                return pts.Count * aUc + CountPoligonGovdeDusey(koller, pts, minCcGv, govdeSmax) * aGv;
            }

            if (KesitAs() + 1e-6 >= gprAs) return govdeSmax;

            bool hcr = KolonDonatiTableDrawer.TryGetKolonBetonarmeHcr(
                _kolonDuseyGprHcr, _model?.Floors, floorIndex, colNo);
            double rho = hcr ? 0.002 : 0.001;
            double rad = KolonKesitEtriyeRadiusCm;

            int CountIn(double x0, double y0, double x1, double y1)
            {
                int n = 0;
                for (int i = 0; i < pts.Count; i++)
                {
                    if (pts[i].x >= x0 - 1.0 && pts[i].x <= x1 + 1.0 &&
                        pts[i].y >= y0 - 1.0 && pts[i].y <= y1 + 1.0)
                        n++;
                }
                return n;
            }

            var overlaps = new List<(double x0, double y0, double x1, double y1)>();
            for (int i = 0; i < etBoxes.Count; i++)
            {
                var a = etBoxes[i];
                for (int j = i + 1; j < etBoxes.Count; j++)
                {
                    var b = etBoxes[j];
                    double ix0 = Math.Max(a.x0, b.x0), ix1 = Math.Min(a.x1, b.x1);
                    double iy0 = Math.Max(a.y0, b.y0), iy1 = Math.Min(a.y1, b.y1);
                    if (ix1 - ix0 >= 2.0 && iy1 - iy0 >= 2.0)
                        overlaps.Add((ix0, iy0, ix1, iy1));
                }
            }

            int guard = 0;
            while (KesitAs() + 1e-6 < gprAs && guard++ < 80)
            {
                int bestN = 0;
                double bestGap = 0;
                bool longIsX = false;
                double ba0 = 0, ba1 = 0, bc0 = 0, bc1 = 0;
                bool found = false;
                for (int i = 0; i < etBoxes.Count; i++)
                {
                    var b = etBoxes[i];
                    if (!b.hasInnerCut) continue;
                    int nMin = PoligonBaslikNmin(b.x0, b.y0, b.x1, b.y1, rects, rho, aUc);
                    if (CountIn(b.x0, b.y0, b.x1, b.y1) >= nMin) continue;
                    double a0, a1, c0, c1;
                    if (!TryPoligonBaslikCiftSiraRange(b, overlaps, rad, out a0, out a1, out c0, out c1))
                        continue;
                    var st = CollectPoligonCiftSiraStations(pts, b.longIsX, a0, a1, c0, c1);
                    if (st.Count < 2) continue;
                    double gap = 0;
                    for (int s = 0; s < st.Count - 1; s++)
                        gap = Math.Max(gap, st[s + 1] - st[s]);
                    if (sMinCc > 0.5 && (a1 - a0) / st.Count < sMinCc - 1e-6) continue;
                    if (gap < Math.Max(4.0, sMinCc)) continue;
                    if (gap < bestGap - 1e-6) continue;
                    bestGap = gap;
                    bestN = st.Count;
                    longIsX = b.longIsX;
                    ba0 = a0; ba1 = a1; bc0 = c0; bc1 = c1;
                    found = true;
                }
                if (!found) break;
                int nBefore = pts.Count;
                EqualizePoligonCiftSira(pts, longIsX, ba0, ba1, bc0, bc1, bestN + 1);
                if (pts.Count <= nBefore) break;
            }

            while (KesitAs() + 1e-6 < gprAs && govdeSmax > minCcGv + 0.5)
            {
                govdeSmax -= 1.0;
                if (govdeSmax < minCcGv) govdeSmax = minCcGv;
            }
            return govdeSmax;
        }

        private static int PoligonBaslikNmin(
            double x0, double y0, double x1, double y1,
            List<(double x0, double y0, double x1, double y1)> rects,
            double rho, double aBar)
        {
            if (aBar < 0.05) aBar = 1.54;
            double ag = 0;
            if (rects != null)
            {
                for (int i = 0; i < rects.Count; i++)
                {
                    var r = rects[i];
                    double rx0 = Math.Min(r.x0, r.x1), rx1 = Math.Max(r.x0, r.x1);
                    double ry0 = Math.Min(r.y0, r.y1), ry1 = Math.Max(r.y0, r.y1);
                    double ix0 = Math.Max(x0, rx0), ix1 = Math.Min(x1, rx1);
                    double iy0 = Math.Max(y0, ry0), iy1 = Math.Min(y1, ry1);
                    if (ix1 - ix0 < 2.0 || iy1 - iy0 < 2.0) continue;
                    ag = Math.Max(ag, Math.Abs((rx1 - rx0) * (ry1 - ry0)));
                }
            }
            if (ag < 1.0) ag = Math.Max(1.0, Math.Abs((x1 - x0) * (y1 - y0)));
            int nMin = (int)Math.Ceiling(rho * ag / aBar - 1e-9);
            return nMin < 4 ? 4 : nMin;
        }

        /// <summary>Perde ucu kolon formatı dörtgenle örtüşüyorsa başlık ρ o dörtgende kolon kuralı ile atılır.</summary>
        private static bool PoligonUcOverlapsKolonFormat(
            Envelope e, bool longIsX, bool atMin, double luUse,
            List<(double x0, double y0, double x1, double y1)> rects)
        {
            if (e == null || rects == null || luUse < 4.0) return false;
            double ux0, uy0, ux1, uy1;
            if (longIsX)
            {
                uy0 = e.MinY;
                uy1 = e.MaxY;
                if (atMin) { ux0 = e.MinX; ux1 = e.MinX + luUse; }
                else { ux0 = e.MaxX - luUse; ux1 = e.MaxX; }
            }
            else
            {
                ux0 = e.MinX;
                ux1 = e.MaxX;
                if (atMin) { uy0 = e.MinY; uy1 = e.MinY + luUse; }
                else { uy0 = e.MaxY - luUse; uy1 = e.MaxY; }
            }
            double uA = Math.Max(1.0, (ux1 - ux0) * (uy1 - uy0));
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                double rx0 = Math.Min(r.x0, r.x1), rx1 = Math.Max(r.x0, r.x1);
                double ry0 = Math.Min(r.y0, r.y1), ry1 = Math.Max(r.y0, r.y1);
                double w = rx1 - rx0, h = ry1 - ry0;
                if (w < 8.0 || h < 8.0) continue;
                if (IsDepremPerdeBoyOrani(Math.Max(w, h), Math.Min(w, h))) continue;
                double ix0 = Math.Max(ux0, rx0), ix1 = Math.Min(ux1, rx1);
                double iy0 = Math.Max(uy0, ry0), iy1 = Math.Min(uy1, ry1);
                if (ix1 - ix0 < 8.0 || iy1 - iy0 < 8.0) continue;
                double iA = (ix1 - ix0) * (iy1 - iy0);
                if (iA > 0.25 * uA || iA > 0.25 * w * h) return true;
            }
            return false;
        }

        /// <summary>Birleşimde T artığı; serbest uçta tüm başlık uzun yüzü.</summary>
        private static bool TryPoligonBaslikCiftSiraRange(
            (double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax) b,
            List<(double x0, double y0, double x1, double y1)> overlaps,
            double rad,
            out double a0, out double a1, out double c0, out double c1)
        {
            if (TryPoligonLeftoverRange(b, overlaps, rad, out a0, out a1, out c0, out c1))
                return true;
            a0 = a1 = c0 = c1 = 0;
            if (!b.hasInnerCut) return false;
            double x0 = b.x0 + rad, y0 = b.y0 + rad, x1 = b.x1 - rad, y1 = b.y1 - rad;
            a0 = b.longIsX ? x0 : y0;
            a1 = b.longIsX ? x1 : y1;
            c0 = b.longIsX ? y0 : x0;
            c1 = b.longIsX ? y1 : x1;
            return a1 - a0 >= 4.0;
        }

        private static List<double> CollectPoligonCiftSiraStations(
            List<(double x, double y)> pts,
            bool longIsX, double a0, double a1, double c0, double c1)
        {
            const double same = 2.0;
            var st = new List<double> { a0, a1 };
            if (pts != null)
            {
                for (int i = 0; i < pts.Count; i++)
                {
                    double x = pts[i].x, y = pts[i].y;
                    bool on = longIsX
                        ? (x >= a0 - same && x <= a1 + same && (Math.Abs(y - c0) <= 1.8 || Math.Abs(y - c1) <= 1.8))
                        : (y >= a0 - same && y <= a1 + same && (Math.Abs(x - c0) <= 1.8 || Math.Abs(x - c1) <= 1.8));
                    if (!on) continue;
                    double s = longIsX ? x : y;
                    bool dup = false;
                    for (int j = 0; j < st.Count; j++)
                    {
                        if (Math.Abs(st[j] - s) < same) { dup = true; break; }
                    }
                    if (!dup) st.Add(s);
                }
            }
            st.Sort();
            return st;
        }

        /// <summary>
        /// TBDY 2018 7.6.5.1: her uçta As / Ag ≥ 0.002 (kritik yükseklik / Hcr), normal katta ≥ 0.001.
        /// Ag = başlığın bağlı olduğu renkli dikdörtgenin brüt alanı (lw × bw), uç alanı değil.
        /// Çap = GPR poligon düşeyinin büyük φ. En az 4 çubuk.
        /// İki uzun yüz (poligon kenarı) + dış kısa uç. İç kısa = kesit görünüş, donatı yok.
        /// Birleşimde uzun kenara yalnız köşe; artığı Fill eşit aralıkla doldurur.
        /// </summary>
        private void PlacePoligonBaslikMinDuseyDonati(
            List<(double x, double y)> pts,
            Envelope e,
            int floorIndex, int colNo,
            double luUse, double innerPas, bool longIsX, bool atMin, double bw,
            bool outerIsJunction)
        {
            if (pts == null || e == null || luUse < 8.0) return;
            int diaMm = 14;
            string donati = null;
            if (_kolonDuseyGpr != null && _model?.Floors != null)
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(
                    _kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out donati, out _);
            KolonDonatiTableDrawer.SumKolonKesitDuseyDonatiAdet(donati, out diaMm);
            if (diaMm < 6) diaMm = 14;
            bool hcr = KolonDonatiTableDrawer.TryGetKolonBetonarmeHcr(
                _kolonDuseyGprHcr, _model?.Floors, floorIndex, colNo);
            double rho = hcr ? 0.002 : 0.001;
            double ag = Math.Max(1.0, Math.Abs(e.Width * e.Height));
            double aBar = Math.PI * diaMm * diaMm / 400.0;
            if (aBar < 0.05) aBar = 1.54;
            int nMin = (int)Math.Ceiling(rho * ag / aBar - 1e-9);
            if (nMin < 4) nMin = 4;

            double pas = KolonKesitPaspayiCm;
            double rad = KolonKesitEtriyeRadiusCm;
            double barLo = pas + rad;
            double longSpan = luUse - innerPas - pas - 2.0 * rad;
            double sKenar = bw - 2.0 * barLo;
            if (sKenar < 1.0) sKenar = 1.0;
            if (longSpan < 1.0) longSpan = 1.0;
            const double sMaxKenar = 20.0;
            double sMinKenar = PoligonDonatiMerkezMinCm(diaMm);
            int nLongUse = MinPoligonKenarDonatiAdet(longSpan, sMaxKenar);
            int nEndUse = MinPoligonKenarDonatiAdet(sKenar, sMaxKenar);
            int nLongMax = MaxPoligonKenarDonatiAdet(longSpan, sMinKenar);
            int nEndMax = MaxPoligonKenarDonatiAdet(sKenar, sMinKenar);
            if (nLongUse > nLongMax) nLongUse = nLongMax;
            if (nEndUse > nEndMax) nEndUse = nEndMax;
            if (!outerIsJunction)
            {
                int OuterEndExtras() => Math.Max(0, nEndUse - 2);
                int NDrawn() => 2 * nLongUse + OuterEndExtras();
                while (NDrawn() < nMin)
                {
                    int need = nMin - NDrawn();
                    if (need == 1 && nEndUse <= 2 && nEndUse < nEndMax)
                        nEndUse++;
                    else if (nLongUse < nLongMax)
                        nLongUse++;
                    else if (nEndUse < nEndMax)
                        nEndUse++;
                    else
                        break;
                    if (nLongUse > 40) break;
                }
            }

            int nLongDraw = outerIsJunction ? 2 : nLongUse;
            if (longIsX)
            {
                double yBot = e.MinY + barLo, yTop = e.MaxY - barLo;
                double x0, x1;
                if (atMin)
                {
                    x0 = e.MinX + barLo;
                    x1 = e.MinX + luUse - innerPas - rad;
                }
                else
                {
                    x0 = e.MaxX - luUse + innerPas + rad;
                    x1 = e.MaxX - barLo;
                }
                if (x1 < x0 + 1.0) x1 = x0;
                var xs = PerdeKesitEsitKonumlar(x0, x1, nLongDraw);
                for (int i = 0; i < xs.Length; i++)
                {
                    pts.Add((xs[i], yTop));
                    pts.Add((xs[i], yBot));
                }
                CollectPerdeKesitUcKenarAralari(pts, atMin ? x0 : x1, yBot, yTop, nEndUse, alongY: true);
            }
            else
            {
                double xL = e.MinX + barLo, xR = e.MaxX - barLo;
                double y0, y1;
                if (atMin)
                {
                    y0 = e.MinY + barLo;
                    y1 = e.MinY + luUse - innerPas - rad;
                }
                else
                {
                    y0 = e.MaxY - luUse + innerPas + rad;
                    y1 = e.MaxY - barLo;
                }
                if (y1 < y0 + 1.0) y1 = y0;
                var ys = PerdeKesitEsitKonumlar(y0, y1, nLongDraw);
                for (int i = 0; i < ys.Length; i++)
                {
                    pts.Add((xL, ys[i]));
                    pts.Add((xR, ys[i]));
                }
                CollectPerdeKesitUcKenarAralari(pts, atMin ? y0 : y1, xL, xR, nEndUse, alongY: false);
            }
        }

        /// <summary>Her etriye köşesinde düşey donatı (iç kısa kesit görünüş köşeleri dahil).</summary>
        private static void PlacePoligonEtriyeKoseDonati(
            List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)> etBoxes,
            List<(double x, double y)> pts)
        {
            if (etBoxes == null || pts == null) return;
            const double same = 2.0;
            double rad = KolonKesitEtriyeRadiusCm;
            bool Near(double x, double y)
            {
                for (int i = 0; i < pts.Count; i++)
                {
                    double dx = pts[i].x - x, dy = pts[i].y - y;
                    if (dx * dx + dy * dy < same * same) return true;
                }
                return false;
            }
            for (int i = 0; i < etBoxes.Count; i++)
            {
                var b = etBoxes[i];
                double x0 = b.x0 + rad, y0 = b.y0 + rad, x1 = b.x1 - rad, y1 = b.y1 - rad;
                if (x1 - x0 < 2.0 || y1 - y0 < 2.0) continue;
                if (!Near(x0, y0)) pts.Add((x0, y0));
                if (!Near(x1, y0)) pts.Add((x1, y0));
                if (!Near(x1, y1)) pts.Add((x1, y1));
                if (!Near(x0, y1)) pts.Add((x0, y1));
            }
        }

        /// <summary>
        /// İki etriyenin örtüşme dikdörtgeni: dört köşe donatısı etriye pas+yarıçap hizasında.
        /// Kaymış çubuklar bu köşelere çekilir.
        /// </summary>
        private static void PlacePoligonEtriyeIcKesisimDonatisi(
            List<(double x, double y)> pts,
            List<(double x, double y)> overlapAnchors,
            List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)> etBoxes)
        {
            if (pts == null || overlapAnchors == null || etBoxes == null || etBoxes.Count < 2) return;
            const double same = 2.0;
            double rad = KolonKesitEtriyeRadiusCm;
            bool NearPts(double x, double y)
            {
                for (int i = 0; i < pts.Count; i++)
                {
                    double dx = pts[i].x - x, dy = pts[i].y - y;
                    if (dx * dx + dy * dy < same * same) return true;
                }
                return false;
            }

            for (int i = 0; i < etBoxes.Count; i++)
            {
                var a = etBoxes[i];
                for (int j = i + 1; j < etBoxes.Count; j++)
                {
                    var b = etBoxes[j];
                    double ix0 = Math.Max(a.x0, b.x0), ix1 = Math.Min(a.x1, b.x1);
                    double iy0 = Math.Max(a.y0, b.y0), iy1 = Math.Min(a.y1, b.y1);
                    if (ix1 - ix0 < 2.0 * rad + 2.0 || iy1 - iy0 < 2.0 * rad + 2.0) continue;
                    double cx = 0.5 * (ix0 + ix1), cy = 0.5 * (iy0 + iy1);
                    var corners = new[] { (ix0, iy0), (ix1, iy0), (ix1, iy1), (ix0, iy1) };
                    for (int k = 0; k < 4; k++)
                    {
                        double ox = corners[k].Item1, oy = corners[k].Item2;
                        double px = ox < cx ? ox + rad : ox - rad;
                        double py = oy < cy ? oy + rad : oy - rad;
                        overlapAnchors.Add((px, py));
                        const double snap = 4.0;
                        int best = -1;
                        double bestD = snap * snap;
                        for (int p = 0; p < pts.Count; p++)
                        {
                            double dx = pts[p].x - px, dy = pts[p].y - py;
                            double d2 = dx * dx + dy * dy;
                            if (d2 < bestD) { bestD = d2; best = p; }
                        }
                        if (best >= 0)
                            pts[best] = (px, py);
                        else if (!NearPts(px, py))
                            pts.Add((px, py));
                    }
                }
            }
        }

        private static void CollectPerdeKesitDuseyCiftSira(
            List<(double x, double y)> pts, double[] xs, double yBot, double yTop)
        {
            if (pts == null || xs == null) return;
            for (int i = 0; i < xs.Length; i++)
            {
                pts.Add((xs[i], yBot));
                pts.Add((xs[i], yTop));
            }
        }

        private static void CollectPerdeKesitDuseyCiftSiraY(
            List<(double x, double y)> pts, double xL, double xR, double[] ys)
        {
            if (pts == null || ys == null) return;
            for (int i = 0; i < ys.Length; i++)
            {
                pts.Add((xL, ys[i]));
                pts.Add((xR, ys[i]));
            }
        }

        private static void CollectPerdeKesitUcKenarAralari(
            List<(double x, double y)> pts,
            double fixedCoord, double a, double b, int nEnd, bool alongY)
        {
            if (pts == null || nEnd < 3) return;
            var ps = PerdeKesitEsitKonumlar(a, b, nEnd);
            for (int i = 1; i < ps.Length - 1; i++)
            {
                if (alongY) pts.Add((fixedCoord, ps[i]));
                else pts.Add((ps[i], fixedCoord));
            }
        }

        private static void DedupPoligonDonatiPts(List<(double x, double y)> pts, double same)
        {
            if (pts == null || pts.Count < 2) return;
            double s2 = same * same;
            var keep = new List<(double x, double y)>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                bool dup = false;
                for (int j = 0; j < keep.Count; j++)
                {
                    double dx = keep[j].x - pts[i].x, dy = keep[j].y - pts[i].y;
                    if (dx * dx + dy * dy < s2) { dup = true; break; }
                }
                if (!dup) keep.Add(pts[i]);
            }
            pts.Clear();
            pts.AddRange(keep);
        }

        /// <summary>Kenar boyunca merkez aralığı maxCc'yi aşmayacak en az çubuk (köşeler dahil).</summary>
        private static int MinPoligonKenarDonatiAdet(double span, double maxCc)
        {
            if (span <= maxCc + 1e-6) return 2;
            int nSpaces = (int)Math.Ceiling(span / maxCc - 1e-9);
            if (nSpaces < 1) nSpaces = 1;
            return nSpaces + 1;
        }

        private static double PoligonDonatiMerkezMinCm(int diaMm)
        {
            double phi = Math.Max(6, diaMm) / 10.0;
            return PoligonDonatiNetAralikMinCm + phi;
        }

        /// <summary>Net 5 cm için kenarda en fazla çubuk (köşeler dahil).</summary>
        private static int MaxPoligonKenarDonatiAdet(double span, double minCc)
        {
            if (minCc < 0.5) minCc = 0.5;
            if (span < minCc - 1e-6) return 2;
            int n = 1 + (int)Math.Floor(span / minCc + 1e-9);
            return n < 2 ? 2 : n;
        }

        /// <summary>
        /// İki etriyenin kapsadığı dikdörtgende kalan donatı merkezlerini 1.5 cm içeri alır; etriye dışına taşmaz.
        /// </summary>
        private static void ClampPoligonEtriyeKapsamaIciDonati(
            List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)> etBoxes,
            List<(double x, double y)> pts)
        {
            if (etBoxes == null || pts == null || etBoxes.Count < 2 || pts.Count == 0) return;
            double inset = KolonKesitEtriyeRadiusCm;
            double eps = Math.Max(KolonKesitDuseyDonatiRadiusCm, 0.8);
            for (int i = 0; i < etBoxes.Count; i++)
            {
                var a = etBoxes[i];
                for (int j = i + 1; j < etBoxes.Count; j++)
                {
                    var b = etBoxes[j];
                    double ix0 = Math.Max(a.x0, b.x0), ix1 = Math.Min(a.x1, b.x1);
                    double iy0 = Math.Max(a.y0, b.y0), iy1 = Math.Min(a.y1, b.y1);
                    if (ix1 - ix0 < 2.0 * inset + 2.0 || iy1 - iy0 < 2.0 * inset + 2.0) continue;
                    double xLo = ix0 + inset, xHi = ix1 - inset;
                    double yLo = iy0 + inset, yHi = iy1 - inset;
                    for (int k = 0; k < pts.Count; k++)
                    {
                        double x = pts[k].x, y = pts[k].y;
                        if (x < ix0 - eps || x > ix1 + eps || y < iy0 - eps || y > iy1 + eps)
                            continue;
                        if (x < xLo) x = xLo;
                        if (x > xHi) x = xHi;
                        if (y < yLo) y = yLo;
                        if (y > yHi) y = yHi;
                        pts[k] = (x, y);
                    }
                }
            }
        }

        /// <summary>
        /// İki uzun yüzde aynı istasyon (çift sıra), artığı eşit aralık. Kesit görünüş atlanır.
        /// </summary>
        private static void FillPoligonEtriyeKenarMaxAralik(
            List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)> etBoxes,
            List<(double x, double y)> pts,
            List<(double x, double y)> overlapAnchors,
            double maxCc, double minCc = 2.0)
        {
            if (etBoxes == null || pts == null || etBoxes.Count == 0 || maxCc < 4.0) return;
            if (minCc < 2.0) minCc = 2.0;
            const double same = 2.0;
            double rad = KolonKesitEtriyeRadiusCm;
            DedupPoligonDonatiPts(pts, same);

            var overlaps = new List<(double x0, double y0, double x1, double y1)>();
            for (int i = 0; i < etBoxes.Count; i++)
            {
                var a = etBoxes[i];
                for (int j = i + 1; j < etBoxes.Count; j++)
                {
                    var b = etBoxes[j];
                    double ix0 = Math.Max(a.x0, b.x0), ix1 = Math.Min(a.x1, b.x1);
                    double iy0 = Math.Max(a.y0, b.y0), iy1 = Math.Min(a.y1, b.y1);
                    if (ix1 - ix0 >= 2.0 && iy1 - iy0 >= 2.0)
                        overlaps.Add((ix0, iy0, ix1, iy1));
                }
            }

            bool InsideOverlap(double x, double y)
            {
                for (int i = 0; i < overlaps.Count; i++)
                {
                    var o = overlaps[i];
                    if (x >= o.x0 - 0.5 && x <= o.x1 + 0.5 && y >= o.y0 - 0.5 && y <= o.y1 + 0.5)
                        return true;
                }
                return false;
            }

            bool NearPts(double x, double y)
            {
                double m2 = minCc * minCc;
                for (int i = 0; i < pts.Count; i++)
                {
                    double dx = pts[i].x - x, dy = pts[i].y - y;
                    if (dx * dx + dy * dy < m2) return true;
                }
                return false;
            }

            bool TryOnEdge(double ax, double ay, double ux, double uy, double len, double x, double y, out double t)
            {
                t = 0;
                double vx = x - ax, vy = y - ay;
                t = vx * ux + vy * uy;
                if (t < -same || t > len + same) return false;
                double px = ax + t * ux, py = ay + t * uy;
                double ox = x - px, oy = y - py;
                return ox * ox + oy * oy <= 1.8 * 1.8;
            }

            bool IsOverlapAnchor(double x, double y)
            {
                if (overlapAnchors == null) return false;
                for (int i = 0; i < overlapAnchors.Count; i++)
                {
                    double dx = overlapAnchors[i].x - x, dy = overlapAnchors[i].y - y;
                    if (dx * dx + dy * dy < same * same) return true;
                }
                return false;
            }

            void FillEdge(double ax, double ay, double bx, double by)
            {
                double lx = bx - ax, ly = by - ay;
                double len = Math.Sqrt(lx * lx + ly * ly);
                if (len < 4.0) return;
                double ux = lx / len, uy = ly / len;

                bool hasOv = false;
                if (overlapAnchors != null)
                {
                    for (int i = 0; i < overlapAnchors.Count; i++)
                    {
                        double tOv;
                        if (!TryOnEdge(ax, ay, ux, uy, len, overlapAnchors[i].x, overlapAnchors[i].y, out tOv))
                            continue;
                        if (tOv > same && tOv < len - same) { hasOv = true; break; }
                    }
                }

                if (hasOv)
                {
                    for (int i = pts.Count - 1; i >= 0; i--)
                    {
                        double t;
                        if (!TryOnEdge(ax, ay, ux, uy, len, pts[i].x, pts[i].y, out t)) continue;
                        if (t <= same || t >= len - same) continue;
                        if (InsideOverlap(pts[i].x, pts[i].y)) continue;
                        if (IsOverlapAnchor(pts[i].x, pts[i].y)) continue;
                        pts.RemoveAt(i);
                    }
                }

                var along = new List<(double t, double x, double y)>();
                void AddAlong(double t, double x, double y)
                {
                    for (int i = 0; i < along.Count; i++)
                    {
                        if (Math.Abs(along[i].t - t) < same) return;
                    }
                    along.Add((t, x, y));
                }
                AddAlong(0, ax, ay);
                AddAlong(len, bx, by);
                for (int i = 0; i < pts.Count; i++)
                {
                    double t;
                    if (TryOnEdge(ax, ay, ux, uy, len, pts[i].x, pts[i].y, out t))
                        AddAlong(t, pts[i].x, pts[i].y);
                }
                if (overlapAnchors != null)
                {
                    for (int i = 0; i < overlapAnchors.Count; i++)
                    {
                        double t;
                        if (TryOnEdge(ax, ay, ux, uy, len, overlapAnchors[i].x, overlapAnchors[i].y, out t))
                            AddAlong(t, overlapAnchors[i].x, overlapAnchors[i].y);
                    }
                }
                along.Sort((p, q) => p.t.CompareTo(q.t));
                var add = new List<(double x, double y)>();
                for (int i = 0; i < along.Count - 1; i++)
                {
                    double L = along[i + 1].t - along[i].t;
                    if (L <= maxCc + 1e-6) continue;
                    double tm = 0.5 * (along[i].t + along[i + 1].t);
                    if (InsideOverlap(ax + tm * ux, ay + tm * uy)) continue;
                    int nSpaces = (int)Math.Ceiling(L / maxCc - 1e-9);
                    if (nSpaces < 2) nSpaces = 2;
                    double s = L / nSpaces;
                    if (s < minCc - 1e-6) continue;
                    for (int n = 1; n < nSpaces; n++)
                    {
                        double t = along[i].t + n * s;
                        double x = ax + t * ux, y = ay + t * uy;
                        if (InsideOverlap(x, y)) continue;
                        if (!NearPts(x, y)) add.Add((x, y));
                    }
                }
                pts.AddRange(add);
            }

            void FillLongPair(bool longIsX, double x0, double y0, double x1, double y1)
            {
                double a0 = longIsX ? x0 : y0;
                double a1 = longIsX ? x1 : y1;
                double c0 = longIsX ? y0 : x0;
                double c1 = longIsX ? y1 : x1;
                if (a1 - a0 < 4.0) return;

                bool OnLongFace(double x, double y)
                {
                    if (longIsX)
                        return x >= x0 - same && x <= x1 + same &&
                               (Math.Abs(y - y0) <= 1.8 || Math.Abs(y - y1) <= 1.8);
                    return y >= y0 - same && y <= y1 + same &&
                           (Math.Abs(x - x0) <= 1.8 || Math.Abs(x - x1) <= 1.8);
                }

                bool LongInOverlap(double s)
                {
                    for (int i = 0; i < overlaps.Count; i++)
                    {
                        var o = overlaps[i];
                        if (longIsX)
                        {
                            if (s >= o.x0 - 0.5 && s <= o.x1 + 0.5) return true;
                        }
                        else if (s >= o.y0 - 0.5 && s <= o.y1 + 0.5) return true;
                    }
                    return false;
                }

                var stations = new List<double>();
                void AddS(double s)
                {
                    if (s < a0 - same || s > a1 + same) return;
                    if (s < a0) s = a0;
                    if (s > a1) s = a1;
                    for (int i = 0; i < stations.Count; i++)
                    {
                        if (Math.Abs(stations[i] - s) < same) return;
                    }
                    stations.Add(s);
                }
                AddS(a0);
                AddS(a1);
                bool hasOvA = false;
                if (overlapAnchors != null)
                {
                    for (int i = 0; i < overlapAnchors.Count; i++)
                    {
                        double ax = overlapAnchors[i].x, ay = overlapAnchors[i].y;
                        if (ax < x0 - same || ax > x1 + same || ay < y0 - same || ay > y1 + same)
                            continue;
                        hasOvA = true;
                        AddS(longIsX ? ax : ay);
                    }
                }
                if (hasOvA)
                {
                    for (int i = pts.Count - 1; i >= 0; i--)
                    {
                        if (!OnLongFace(pts[i].x, pts[i].y)) continue;
                        double s = longIsX ? pts[i].x : pts[i].y;
                        if (Math.Abs(s - a0) < same || Math.Abs(s - a1) < same) continue;
                        if (IsOverlapAnchor(pts[i].x, pts[i].y)) continue;
                        pts.RemoveAt(i);
                    }
                }
                else
                {
                    for (int i = 0; i < pts.Count; i++)
                    {
                        if (!OnLongFace(pts[i].x, pts[i].y)) continue;
                        AddS(longIsX ? pts[i].x : pts[i].y);
                    }
                }
                stations.Sort();

                var extra = new List<double>();
                for (int i = 0; i < stations.Count - 1; i++)
                {
                    double L = stations[i + 1] - stations[i];
                    if (L <= maxCc + 1e-6) continue;
                    double mid = 0.5 * (stations[i] + stations[i + 1]);
                    if (LongInOverlap(mid)) continue;
                    int nSpaces = (int)Math.Ceiling(L / maxCc - 1e-9);
                    if (nSpaces < 2) nSpaces = 2;
                    double sp = L / nSpaces;
                    if (sp < minCc - 1e-6) continue;
                    for (int n = 1; n < nSpaces; n++)
                        extra.Add(stations[i] + n * sp);
                }

                void PlacePair(double s)
                {
                    double xa, ya, xb, yb;
                    if (longIsX)
                    {
                        xa = s; ya = c0; xb = s; yb = c1;
                    }
                    else
                    {
                        xa = c0; ya = s; xb = c1; yb = s;
                    }
                    if (!NearPts(xa, ya)) pts.Add((xa, ya));
                    if (!NearPts(xb, yb)) pts.Add((xb, yb));
                }

                for (int i = 0; i < extra.Count; i++)
                    PlacePair(extra[i]);
                PlacePair(a0);
                PlacePair(a1);
                if (overlapAnchors != null)
                {
                    for (int i = 0; i < overlapAnchors.Count; i++)
                    {
                        double ax = overlapAnchors[i].x, ay = overlapAnchors[i].y;
                        if (ax < x0 - same || ax > x1 + same || ay < y0 - same || ay > y1 + same)
                            continue;
                        PlacePair(longIsX ? ax : ay);
                    }
                }
            }

            for (int i = 0; i < etBoxes.Count; i++)
            {
                var b = etBoxes[i];
                if (!b.hasInnerCut) continue;
                double x0 = b.x0 + rad, y0 = b.y0 + rad, x1 = b.x1 - rad, y1 = b.y1 - rad;
                if (x1 - x0 < 4.0 || y1 - y0 < 4.0) continue;
                FillLongPair(b.longIsX, x0, y0, x1, y1);
                if (b.longIsX)
                {
                    if (!PoligonEtriyeKenarKesitGorunus(b.longIsX, b.hasInnerCut, b.innerIsMax, true, false))
                        FillEdge(x0, y0, x0, y1);
                    if (!PoligonEtriyeKenarKesitGorunus(b.longIsX, b.hasInnerCut, b.innerIsMax, true, true))
                        FillEdge(x1, y0, x1, y1);
                }
                else
                {
                    if (!PoligonEtriyeKenarKesitGorunus(b.longIsX, b.hasInnerCut, b.innerIsMax, false, false))
                        FillEdge(x0, y0, x1, y0);
                    if (!PoligonEtriyeKenarKesitGorunus(b.longIsX, b.hasInnerCut, b.innerIsMax, false, true))
                        FillEdge(x0, y1, x1, y1);
                }
            }

            FillPoligonOverlapDisYuzey(etBoxes, pts, overlaps, maxCc, minCc);
        }

        /// <summary>
        /// İç kısa kenar = kesit görünüş (duvar gövdesine bakan lu kesiti). Uzun yüzler poligon kenarıdır.
        /// </summary>
        private static bool PoligonEtriyeKenarKesitGorunus(
            bool longIsX, bool hasInnerCut, bool innerIsMax,
            bool edgeIsVertical, bool edgeIsMaxSide)
        {
            if (!hasInnerCut) return false;
            bool edgeIsShort = longIsX ? edgeIsVertical : !edgeIsVertical;
            if (!edgeIsShort) return false;
            return innerIsMax == edgeIsMaxSide;
        }

        /// <summary>
        /// Örtüşme dikdörtgeninin dışa bakan kenarı: mevcut donatı aralığı &gt; 20 cm ise eşit aralıkla donatı.
        /// </summary>
        private static void FillPoligonOverlapDisYuzey(
            List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)> etBoxes,
            List<(double x, double y)> pts,
            List<(double x0, double y0, double x1, double y1)> overlaps,
            double maxCc, double minCc = 2.0)
        {
            if (etBoxes == null || pts == null || overlaps == null || overlaps.Count == 0) return;
            if (minCc < 2.0) minCc = 2.0;
            const double same = 2.0;
            double rad = KolonKesitEtriyeRadiusCm;

            bool InBox(double x, double y)
            {
                for (int i = 0; i < etBoxes.Count; i++)
                {
                    var b = etBoxes[i];
                    if (x >= b.x0 - 0.2 && x <= b.x1 + 0.2 && y >= b.y0 - 0.2 && y <= b.y1 + 0.2)
                        return true;
                }
                return false;
            }

            bool NearPts(double x, double y)
            {
                double m2 = minCc * minCc;
                for (int i = 0; i < pts.Count; i++)
                {
                    double dx = pts[i].x - x, dy = pts[i].y - y;
                    if (dx * dx + dy * dy < m2) return true;
                }
                return false;
            }

            void FillIfGap(double ax, double ay, double bx, double by)
            {
                double lx = bx - ax, ly = by - ay;
                double len = Math.Sqrt(lx * lx + ly * ly);
                if (len <= maxCc + 1e-6) return;
                double ux = lx / len, uy = ly / len;
                var along = new List<double> { 0, len };
                for (int i = 0; i < pts.Count; i++)
                {
                    double vx = pts[i].x - ax, vy = pts[i].y - ay;
                    double t = vx * ux + vy * uy;
                    if (t < -same || t > len + same) continue;
                    double px = ax + t * ux, py = ay + t * uy;
                    double ox = pts[i].x - px, oy = pts[i].y - py;
                    if (ox * ox + oy * oy > 1.8 * 1.8) continue;
                    bool dup = false;
                    for (int j = 0; j < along.Count; j++)
                    {
                        if (Math.Abs(along[j] - t) < same) { dup = true; break; }
                    }
                    if (!dup) along.Add(t);
                }
                along.Sort();
                for (int i = 0; i < along.Count - 1; i++)
                {
                    double L = along[i + 1] - along[i];
                    if (L <= maxCc + 1e-6) continue;
                    int nSpaces = (int)Math.Ceiling(L / maxCc - 1e-9);
                    if (nSpaces < 2) nSpaces = 2;
                    double s = L / nSpaces;
                    if (s < minCc - 1e-6) continue;
                    for (int n = 1; n < nSpaces; n++)
                    {
                        double t = along[i] + n * s;
                        double x = ax + t * ux, y = ay + t * uy;
                        if (!NearPts(x, y)) pts.Add((x, y));
                    }
                }
            }

            for (int i = 0; i < overlaps.Count; i++)
            {
                var o = overlaps[i];
                double mx = 0.5 * (o.x0 + o.x1), my = 0.5 * (o.y0 + o.y1);
                double x0 = o.x0 + rad, y0 = o.y0 + rad, x1 = o.x1 - rad, y1 = o.y1 - rad;
                if (x1 - x0 < 4.0 || y1 - y0 < 4.0) continue;
                if (!InBox(mx, o.y1 + 2.0)) FillIfGap(x0, y1, x1, y1);
                if (!InBox(mx, o.y0 - 2.0)) FillIfGap(x1, y0, x0, y0);
                if (!InBox(o.x1 + 2.0, my)) FillIfGap(x1, y0, x1, y1);
                if (!InBox(o.x0 - 2.0, my)) FillIfGap(x0, y1, x0, y0);
            }
        }

        private static bool TryPoligonLeftoverRange(
            (double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax) b,
            List<(double x0, double y0, double x1, double y1)> overlaps,
            double rad,
            out double a0, out double a1, out double c0, out double c1)
        {
            a0 = a1 = c0 = c1 = 0;
            if (!b.hasInnerCut || overlaps == null) return false;
            double x0 = b.x0 + rad, y0 = b.y0 + rad, x1 = b.x1 - rad, y1 = b.y1 - rad;
            bool longIsX = b.longIsX;
            double aLo = longIsX ? x0 : y0;
            double aHi = longIsX ? x1 : y1;
            c0 = longIsX ? y0 : x0;
            c1 = longIsX ? y1 : x1;
            bool found = false;
            double ovLo = aHi, ovHi = aLo;
            for (int i = 0; i < overlaps.Count; i++)
            {
                var o = overlaps[i];
                double ix0 = Math.Max(b.x0, o.x0), ix1 = Math.Min(b.x1, o.x1);
                double iy0 = Math.Max(b.y0, o.y0), iy1 = Math.Min(b.y1, o.y1);
                if (ix1 - ix0 < 2.0 || iy1 - iy0 < 2.0) continue;
                found = true;
                if (longIsX)
                {
                    ovLo = Math.Min(ovLo, ix0);
                    ovHi = Math.Max(ovHi, ix1);
                }
                else
                {
                    ovLo = Math.Min(ovLo, iy0);
                    ovHi = Math.Max(ovHi, iy1);
                }
            }
            if (!found) return false;
            if (b.innerIsMax)
            {
                a0 = ovHi - rad;
                a1 = aHi;
            }
            else
            {
                a0 = aLo;
                a1 = ovLo + rad;
            }
            if (a0 > a1) { double t = a0; a0 = a1; a1 = t; }
            return a1 - a0 >= 4.0;
        }

        private static int CountPoligonCiftSiraStations(
            List<(double x, double y)> pts,
            bool longIsX, double a0, double a1, double c0, double c1)
        {
            if (pts == null) return 0;
            const double same = 2.0;
            var st = new List<double>();
            for (int i = 0; i < pts.Count; i++)
            {
                double x = pts[i].x, y = pts[i].y;
                bool on = longIsX
                    ? (x >= a0 - same && x <= a1 + same && (Math.Abs(y - c0) <= 1.8 || Math.Abs(y - c1) <= 1.8))
                    : (y >= a0 - same && y <= a1 + same && (Math.Abs(x - c0) <= 1.8 || Math.Abs(x - c1) <= 1.8));
                if (!on) continue;
                double s = longIsX ? x : y;
                bool dup = false;
                for (int j = 0; j < st.Count; j++)
                {
                    if (Math.Abs(st[j] - s) < same) { dup = true; break; }
                }
                if (!dup) st.Add(s);
            }
            return st.Count;
        }

        private static void EqualizePoligonCiftSira(
            List<(double x, double y)> pts,
            bool longIsX, double a0, double a1, double c0, double c1, int n)
        {
            if (pts == null || n < 2) return;
            const double same = 2.0;
            for (int i = pts.Count - 1; i >= 0; i--)
            {
                double x = pts[i].x, y = pts[i].y;
                bool on = longIsX
                    ? (x >= a0 - same && x <= a1 + same && (Math.Abs(y - c0) <= 1.8 || Math.Abs(y - c1) <= 1.8))
                    : (y >= a0 - same && y <= a1 + same && (Math.Abs(x - c0) <= 1.8 || Math.Abs(x - c1) <= 1.8));
                if (!on) continue;
                double s = longIsX ? x : y;
                if (Math.Abs(s - a0) < same || Math.Abs(s - a1) < same) continue;
                pts.RemoveAt(i);
            }
            var ps = PerdeKesitEsitKonumlar(a0, a1, n);
            bool Near(double x, double y)
            {
                for (int i = 0; i < pts.Count; i++)
                {
                    double dx = pts[i].x - x, dy = pts[i].y - y;
                    if (dx * dx + dy * dy < same * same) return true;
                }
                return false;
            }
            for (int i = 0; i < ps.Length; i++)
            {
                double s = ps[i];
                double xa, ya, xb, yb;
                if (longIsX) { xa = s; ya = c0; xb = s; yb = c1; }
                else { xa = c0; ya = s; xb = c1; yb = s; }
                if (!Near(xa, ya)) pts.Add((xa, ya));
                if (!Near(xb, yb)) pts.Add((xb, yb));
            }
        }

        /// <summary>Köşe başlıkta toplam As; iki kol ayrı ayrı değil. Eksik çubuk uzun yüzde / dış uçta.</summary>
        private void EnsurePoligonKoseBaslikAdet(
            List<(double x, double y)> pts,
            List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)> etBoxes,
            List<(double x0, double y0, double x1, double y1)> rects,
            int floorIndex,
            int colNo,
            double sMinCc = 0)
        {
            if (pts == null || etBoxes == null || etBoxes.Count < 2) return;
            int diaMm = 14;
            string donati = null;
            if (_kolonDuseyGpr != null && _model?.Floors != null)
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(
                    _kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out donati, out _);
            KolonDonatiTableDrawer.SumKolonKesitDuseyDonatiAdet(donati, out diaMm);
            if (diaMm < 6) diaMm = 14;
            bool hcr = KolonDonatiTableDrawer.TryGetKolonBetonarmeHcr(
                _kolonDuseyGprHcr, _model?.Floors, floorIndex, colNo);
            double rho = hcr ? 0.002 : 0.001;
            double aBar = Math.PI * diaMm * diaMm / 400.0;
            if (aBar < 0.05) aBar = 1.54;
            double rad = KolonKesitEtriyeRadiusCm;

            int CountIn(double x0, double y0, double x1, double y1)
            {
                int n = 0;
                for (int i = 0; i < pts.Count; i++)
                {
                    if (pts[i].x >= x0 - 1.0 && pts[i].x <= x1 + 1.0 &&
                        pts[i].y >= y0 - 1.0 && pts[i].y <= y1 + 1.0)
                        n++;
                }
                return n;
            }

            double MaxAg(double x0, double y0, double x1, double y1)
            {
                double best = 0;
                if (rects == null) return Math.Max(1.0, (x1 - x0) * (y1 - y0));
                for (int i = 0; i < rects.Count; i++)
                {
                    var r = rects[i];
                    double rx0 = Math.Min(r.x0, r.x1), rx1 = Math.Max(r.x0, r.x1);
                    double ry0 = Math.Min(r.y0, r.y1), ry1 = Math.Max(r.y0, r.y1);
                    double ix0 = Math.Max(x0, rx0), ix1 = Math.Min(x1, rx1);
                    double iy0 = Math.Max(y0, ry0), iy1 = Math.Min(y1, ry1);
                    if (ix1 - ix0 < 2.0 || iy1 - iy0 < 2.0) continue;
                    best = Math.Max(best, Math.Abs((rx1 - rx0) * (ry1 - ry0)));
                }
                return best > 1.0 ? best : Math.Max(1.0, (x1 - x0) * (y1 - y0));
            }

            var overlaps = new List<(double x0, double y0, double x1, double y1)>();
            for (int i = 0; i < etBoxes.Count; i++)
            {
                var a = etBoxes[i];
                for (int j = i + 1; j < etBoxes.Count; j++)
                {
                    var b = etBoxes[j];
                    double ix0 = Math.Max(a.x0, b.x0), ix1 = Math.Min(a.x1, b.x1);
                    double iy0 = Math.Max(a.y0, b.y0), iy1 = Math.Min(a.y1, b.y1);
                    if (ix1 - ix0 >= 2.0 && iy1 - iy0 >= 2.0)
                        overlaps.Add((ix0, iy0, ix1, iy1));
                }
            }

            for (int i = 0; i < etBoxes.Count; i++)
            {
                var a = etBoxes[i];
                for (int j = i + 1; j < etBoxes.Count; j++)
                {
                    var b = etBoxes[j];
                    double ix0 = Math.Max(a.x0, b.x0), ix1 = Math.Min(a.x1, b.x1);
                    double iy0 = Math.Max(a.y0, b.y0), iy1 = Math.Min(a.y1, b.y1);
                    if (ix1 - ix0 < 2.0 || iy1 - iy0 < 2.0) continue;
                    if (!a.hasInnerCut || !b.hasInnerCut) continue;
                    double ux0 = Math.Min(a.x0, b.x0), ux1 = Math.Max(a.x1, b.x1);
                    double uy0 = Math.Min(a.y0, b.y0), uy1 = Math.Max(a.y1, b.y1);
                    double ag = MaxAg(ux0, uy0, ux1, uy1);
                    int nMin = (int)Math.Ceiling(rho * ag / aBar - 1e-9);
                    if (nMin < 4) nMin = 4;
                    var boxes = new[] { a, b };
                    int guard = 0;
                    while (CountIn(ux0, uy0, ux1, uy1) < nMin && guard++ < 30)
                    {
                        int bestK = -1;
                        int bestN = 0;
                        double bestSpan = 0;
                        double ba0 = 0, ba1 = 0, bc0 = 0, bc1 = 0;
                        for (int k = 0; k < 2; k++)
                        {
                            double a0, a1, c0, c1;
                            if (!TryPoligonLeftoverRange(boxes[k], overlaps, rad, out a0, out a1, out c0, out c1))
                                continue;
                            double span = a1 - a0;
                            int nNow = CountPoligonCiftSiraStations(pts, boxes[k].longIsX, a0, a1, c0, c1);
                            if (sMinCc > 0.5 && nNow >= 2 && span / nNow < sMinCc - 1e-6) continue;
                            if (span <= bestSpan) continue;
                            bestSpan = span;
                            bestK = k;
                            bestN = nNow;
                            ba0 = a0; ba1 = a1; bc0 = c0; bc1 = c1;
                        }
                        if (bestK < 0) break;
                        EqualizePoligonCiftSira(pts, boxes[bestK].longIsX, ba0, ba1, bc0, bc1, bestN + 1);
                    }
                }
            }
        }

        /// <summary>
        /// Kol ucunda başka dikdörtgenle örtüşme kalınlığı (birleşim). Yoksa 0.
        /// </summary>
        private static double PoligonKolBirlestirmeKalinlikCm(
            double x0, double y0, double x1, double y1,
            List<(double x0, double y0, double x1, double y1)> rects,
            bool longIsX, bool atMin)
        {
            if (rects == null || rects.Count == 0) return 0;
            double sx0 = Math.Min(x0, x1), sx1 = Math.Max(x0, x1);
            double sy0 = Math.Min(y0, y1), sy1 = Math.Max(y0, y1);
            double best = 0;
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                double ox0 = Math.Min(r.x0, r.x1), ox1 = Math.Max(r.x0, r.x1);
                double oy0 = Math.Min(r.y0, r.y1), oy1 = Math.Max(r.y0, r.y1);
                if (Math.Abs(ox0 - sx0) < 0.4 && Math.Abs(ox1 - sx1) < 0.4 &&
                    Math.Abs(oy0 - sy0) < 0.4 && Math.Abs(oy1 - sy1) < 0.4)
                    continue;
                double ix0 = Math.Max(sx0, ox0), ix1 = Math.Min(sx1, ox1);
                double iy0 = Math.Max(sy0, oy0), iy1 = Math.Min(sy1, oy1);
                if (ix1 - ix0 < 2.0 || iy1 - iy0 < 2.0) continue;
                if (longIsX)
                {
                    if (atMin)
                    {
                        if (ix0 > sx0 + 4.0) continue;
                        best = Math.Max(best, ix1 - sx0);
                    }
                    else
                    {
                        if (ix1 < sx1 - 4.0) continue;
                        best = Math.Max(best, sx1 - ix0);
                    }
                }
                else
                {
                    if (atMin)
                    {
                        if (iy0 > sy0 + 4.0) continue;
                        best = Math.Max(best, iy1 - sy0);
                    }
                    else
                    {
                        if (iy1 < sy1 - 4.0) continue;
                        best = Math.Max(best, sy1 - iy0);
                    }
                }
            }
            return best;
        }

        /// <summary>Poligon kolon kolu: yalnız 1. (dış) etriye. İç etriye / çiroz / düşey yok.</summary>
        private void DrawPoligonKolonBirinciEtriye(
            Transaction tr, BlockTableRecord btr, Envelope e, int floorIndex, int colNo,
            List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)> etBoxes)
        {
            if (e == null) return;
            int diaMm = ResolveKolonDuseyEtriyeDiaMm(floorIndex, colNo);
            double rad = KolonKesitEtriyeRadiusCm;
            double hook = TbdY2018EtriyeHookExtCm(diaMm);
            double inset = KolonKesitPaspayiCm;
            double x0 = e.MinX + inset, y0 = e.MinY + inset, x1 = e.MaxX - inset, y1 = e.MaxY - inset;
            if (x1 - x0 < 2.0 * rad + 2.0 || y1 - y0 < 2.0 * rad + 2.0) return;
            if (tr != null && btr != null)
                AppendKolonKesitEtriyePline(tr, btr, x0, y0, x1, y1, rad, hook);
            if (etBoxes != null)
                etBoxes.Add((x0, y0, x1, y1, e.Width >= e.Height, false, false));
        }

        /// <summary>
        /// Poligon içindeki kolon formatı dörtgen: Ag üzerinden As/Ag ≥ 0.01, GPR büyük φ.
        /// Yerleşim kolon kesiti: 4 köşe + kenar (aralık ≤ 20 cm).
        /// </summary>
        private void PlacePoligonKolonFormatDuseyDonati(
            List<(double x0, double y0, double x1, double y1)> rects,
            List<(double x, double y)> pts,
            int floorIndex, int colNo, double sMinCc = 0,
            List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)> etBoxes = null)
        {
            if (pts == null || rects == null) return;
            int diaMm = 14;
            string donati = null;
            if (_kolonDuseyGpr != null && _model?.Floors != null)
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(
                    _kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out donati, out _);
            KolonDonatiTableDrawer.SumKolonKesitDuseyDonatiAdet(donati, out diaMm);
            if (diaMm < 6) diaMm = 14;
            double aBar = Math.PI * diaMm * diaMm / 400.0;
            if (aBar < 0.05) aBar = 1.54;
            const double rho = 0.01;
            const double maxCc = 20.0;
            double pas = KolonKesitPaspayiCm;
            double rad = KolonKesitEtriyeRadiusCm;
            double minCc = sMinCc > 0.5 ? sMinCc : PoligonDonatiMerkezMinCm(diaMm);

            bool NearPts(double x, double y)
            {
                double m2 = minCc * minCc;
                for (int i = 0; i < pts.Count; i++)
                {
                    double dx = pts[i].x - x, dy = pts[i].y - y;
                    if (dx * dx + dy * dy < m2) return true;
                }
                return false;
            }

            var overlaps = CollectPoligonEtriyeOverlaps(etBoxes);
            bool SkipInOverlap(double x, double y)
            {
                if (overlaps == null || overlaps.Count == 0) return false;
                for (int i = 0; i < overlaps.Count; i++)
                {
                    var o = overlaps[i];
                    if (x < o.x0 - 0.5 || x > o.x1 + 0.5 || y < o.y0 - 0.5 || y > o.y1 + 0.5)
                        continue;
                    double ox0 = o.x0 + rad, oy0 = o.y0 + rad, ox1 = o.x1 - rad, oy1 = o.y1 - rad;
                    if ((Math.Abs(x - ox0) < 2.0 && Math.Abs(y - oy0) < 2.0) ||
                        (Math.Abs(x - ox1) < 2.0 && Math.Abs(y - oy0) < 2.0) ||
                        (Math.Abs(x - ox1) < 2.0 && Math.Abs(y - oy1) < 2.0) ||
                        (Math.Abs(x - ox0) < 2.0 && Math.Abs(y - oy1) < 2.0))
                        return false;
                    return true;
                }
                return false;
            }

            void AddPt(double x, double y)
            {
                if (SkipInOverlap(x, y)) return;
                if (!NearPts(x, y)) pts.Add((x, y));
            }

            void AddEdge(Point2d a, Point2d b, int extras)
            {
                if (extras <= 0) return;
                for (int i = 1; i <= extras; i++)
                {
                    double t = i / (double)(extras + 1);
                    AddPt(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
                }
            }

            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                double rx0 = Math.Min(r.x0, r.x1), rx1 = Math.Max(r.x0, r.x1);
                double ry0 = Math.Min(r.y0, r.y1), ry1 = Math.Max(r.y0, r.y1);
                double w = rx1 - rx0, h = ry1 - ry0;
                if (w < 8.0 || h < 8.0) continue;
                if (IsDepremPerdeBoyOrani(Math.Max(w, h), Math.Min(w, h))) continue;
                double ag = Math.Max(1.0, w * h);
                int nMin = (int)Math.Ceiling(rho * ag / aBar - 1e-9);
                if (nMin < 4) nMin = 4;

                double x0 = rx0 + pas, y0 = ry0 + pas, x1 = rx1 - pas, y1 = ry1 - pas;
                if (x1 - x0 < 2.0 * rad + 2.0 || y1 - y0 < 2.0 * rad + 2.0) continue;
                var cTl = new Point2d(x0 + rad, y1 - rad);
                var cTr = new Point2d(x1 - rad, y1 - rad);
                var cBr = new Point2d(x1 - rad, y0 + rad);
                var cBl = new Point2d(x0 + rad, y0 + rad);
                double Lx = Math.Abs(cTr.X - cTl.X);
                double Ly = Math.Abs(cTl.Y - cBl.Y);
                FaceExtrasRange(Lx, minCc, maxCc, out int minX, out int maxX);
                FaceExtrasRange(Ly, minCc, maxCc, out int minY, out int maxY);
                int nCc = 4 + 2 * minX + 2 * minY;
                int nUse = Math.Max(nMin, nCc);
                int nCap = 4 + 2 * maxX + 2 * maxY;
                if (nUse > nCap) nUse = nCap;

                int CountIn()
                {
                    int n = 0;
                    for (int p = 0; p < pts.Count; p++)
                    {
                        if (pts[p].x >= rx0 - 1.0 && pts[p].x <= rx1 + 1.0 &&
                            pts[p].y >= ry0 - 1.0 && pts[p].y <= ry1 + 1.0)
                            n++;
                    }
                    return n;
                }

                AddPt(cTl.X, cTl.Y);
                AddPt(cTr.X, cTr.Y);
                AddPt(cBr.X, cBr.Y);
                AddPt(cBl.X, cBl.Y);
                int nHave = CountIn();
                int top = minX, bot = minX, left = minY, right = minY;
                if (nHave < nUse)
                {
                    int rest = nUse - 4;
                    if (Math.Abs(Lx - Ly) < 3.0)
                        AllocateSquareFaceExtras(rest, Lx, minCc, maxCc, out top, out bot, out left, out right);
                    else
                        AllocateRectFaceExtras(rest, Lx, Ly, minCc, minCc, maxCc, out top, out bot, out left, out right);
                    if (top > maxX) top = maxX;
                    if (bot > maxX) bot = maxX;
                    if (left > maxY) left = maxY;
                    if (right > maxY) right = maxY;
                    if (top < bot) top = bot; else bot = top;
                    if (left < right) left = right; else right = left;
                }
                AddEdge(cTl, cTr, top);
                AddEdge(cBl, cBr, bot);
                AddEdge(cBl, cTl, left);
                AddEdge(cBr, cTr, right);
            }
        }

        /// <summary>Kolon formatı kenarları: karşı yüzler aynı adet, perde gibi eşit aralık.</summary>
        private static void EqualizePoligonKolonFormatKenarlari(
            List<(double x0, double y0, double x1, double y1)> rects,
            List<(double x, double y)> pts,
            List<(double x, double y)> overlapAnchors = null)
        {
            if (rects == null || pts == null) return;
            double pas = KolonKesitPaspayiCm;
            double rad = KolonKesitEtriyeRadiusCm;
            const double same = 2.0;

            int CountEdge(double ax, double ay, double bx, double by)
            {
                double lx = bx - ax, ly = by - ay;
                double len = Math.Sqrt(lx * lx + ly * ly);
                if (len < 4.0) return 0;
                double ux = lx / len, uy = ly / len;
                var ts = new List<double> { 0, len };
                for (int i = 0; i < pts.Count; i++)
                {
                    double vx = pts[i].x - ax, vy = pts[i].y - ay;
                    double t = vx * ux + vy * uy;
                    if (t < -same || t > len + same) continue;
                    double px = ax + t * ux, py = ay + t * uy;
                    double ox = pts[i].x - px, oy = pts[i].y - py;
                    if (ox * ox + oy * oy > 1.8 * 1.8) continue;
                    bool dup = false;
                    for (int j = 0; j < ts.Count; j++)
                    {
                        if (Math.Abs(ts[j] - t) < same) { dup = true; break; }
                    }
                    if (!dup) ts.Add(t);
                }
                return ts.Count;
            }

            void EqualizeEdge(double ax, double ay, double bx, double by, int n)
            {
                double lx = bx - ax, ly = by - ay;
                double len = Math.Sqrt(lx * lx + ly * ly);
                if (len < 4.0 || n < 2) return;
                double ux = lx / len, uy = ly / len;
                if (overlapAnchors != null)
                {
                    for (int a = 0; a < overlapAnchors.Count; a++)
                    {
                        double vxA = overlapAnchors[a].x - ax, vyA = overlapAnchors[a].y - ay;
                        double tA = vxA * ux + vyA * uy;
                        if (tA <= same || tA >= len - same) continue;
                        double pxA = ax + tA * ux, pyA = ay + tA * uy;
                        double oxA = overlapAnchors[a].x - pxA, oyA = overlapAnchors[a].y - pyA;
                        if (oxA * oxA + oyA * oyA <= 1.8 * 1.8)
                            return;
                    }
                }
                for (int i = pts.Count - 1; i >= 0; i--)
                {
                    double vx = pts[i].x - ax, vy = pts[i].y - ay;
                    double t = vx * ux + vy * uy;
                    if (t <= same || t >= len - same) continue;
                    double px = ax + t * ux, py = ay + t * uy;
                    double ox = pts[i].x - px, oy = pts[i].y - py;
                    if (ox * ox + oy * oy > 1.8 * 1.8) continue;
                    pts.RemoveAt(i);
                }
                var ps = PerdeKesitEsitKonumlar(0, len, n);
                for (int i = 0; i < ps.Length; i++)
                {
                    double x = ax + ps[i] * ux, y = ay + ps[i] * uy;
                    bool near = false;
                    for (int j = 0; j < pts.Count; j++)
                    {
                        double dx = pts[j].x - x, dy = pts[j].y - y;
                        if (dx * dx + dy * dy < same * same) { near = true; break; }
                    }
                    if (!near) pts.Add((x, y));
                }
            }

            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                double rx0 = Math.Min(r.x0, r.x1), rx1 = Math.Max(r.x0, r.x1);
                double ry0 = Math.Min(r.y0, r.y1), ry1 = Math.Max(r.y0, r.y1);
                double w = rx1 - rx0, h = ry1 - ry0;
                if (w < 8.0 || h < 8.0) continue;
                if (IsDepremPerdeBoyOrani(Math.Max(w, h), Math.Min(w, h))) continue;
                double x0 = rx0 + pas, y0 = ry0 + pas, x1 = rx1 - pas, y1 = ry1 - pas;
                if (x1 - x0 < 2.0 * rad + 2.0 || y1 - y0 < 2.0 * rad + 2.0) continue;
                var cTl = new Point2d(x0 + rad, y1 - rad);
                var cTr = new Point2d(x1 - rad, y1 - rad);
                var cBr = new Point2d(x1 - rad, y0 + rad);
                var cBl = new Point2d(x0 + rad, y0 + rad);
                int nX = Math.Max(CountEdge(cTl.X, cTl.Y, cTr.X, cTr.Y), CountEdge(cBl.X, cBl.Y, cBr.X, cBr.Y));
                int nY = Math.Max(CountEdge(cBl.X, cBl.Y, cTl.X, cTl.Y), CountEdge(cBr.X, cBr.Y, cTr.X, cTr.Y));
                if (nX < 2) nX = 2;
                if (nY < 2) nY = 2;
                EqualizeEdge(cTl.X, cTl.Y, cTr.X, cTr.Y, nX);
                EqualizeEdge(cBl.X, cBl.Y, cBr.X, cBr.Y, nX);
                EqualizeEdge(cBl.X, cBl.Y, cTl.X, cTl.Y, nY);
                EqualizeEdge(cBr.X, cBr.Y, cTr.X, cTr.Y, nY);
            }
        }

        /// <summary>Düşey çubuklar net ≥ 5 cm: çakışanları birleştir, kenarı yeniden eşitle.</summary>
        private static void EnforcePoligonDonatiNetMinAralik(
            List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)> etBoxes,
            List<(double x, double y)> pts,
            double minCc)
        {
            if (pts == null || minCc < 0.5) return;
            DedupPoligonDonatiPts(pts, minCc);
            if (etBoxes == null || etBoxes.Count == 0) return;
            double rad = KolonKesitEtriyeRadiusCm;
            const double same = 2.0;

            bool Near(double x, double y)
            {
                double m2 = minCc * minCc;
                for (int i = 0; i < pts.Count; i++)
                {
                    double dx = pts[i].x - x, dy = pts[i].y - y;
                    if (dx * dx + dy * dy < m2) return true;
                }
                return false;
            }

            void FixEdge(double ax, double ay, double bx, double by)
            {
                double lx = bx - ax, ly = by - ay;
                double len = Math.Sqrt(lx * lx + ly * ly);
                if (len < 4.0) return;
                double ux = lx / len, uy = ly / len;
                var ts = new List<double> { 0, len };
                for (int i = 0; i < pts.Count; i++)
                {
                    double vx = pts[i].x - ax, vy = pts[i].y - ay;
                    double t = vx * ux + vy * uy;
                    if (t < -same || t > len + same) continue;
                    double px = ax + t * ux, py = ay + t * uy;
                    double ox = pts[i].x - px, oy = pts[i].y - py;
                    if (ox * ox + oy * oy > 1.8 * 1.8) continue;
                    bool dup = false;
                    for (int j = 0; j < ts.Count; j++)
                    {
                        if (Math.Abs(ts[j] - t) < same) { dup = true; break; }
                    }
                    if (!dup) ts.Add(t);
                }
                ts.Sort();
                bool tight = false;
                for (int i = 0; i < ts.Count - 1; i++)
                {
                    if (ts[i + 1] - ts[i] < minCc - 1e-6) { tight = true; break; }
                }
                int nMax = MaxPoligonKenarDonatiAdet(len, minCc);
                int n = ts.Count;
                if (n > nMax) n = nMax;
                if (n < 2) n = 2;
                if (!tight && ts.Count <= nMax) return;
                for (int i = pts.Count - 1; i >= 0; i--)
                {
                    double vx = pts[i].x - ax, vy = pts[i].y - ay;
                    double t = vx * ux + vy * uy;
                    if (t <= same || t >= len - same) continue;
                    double px = ax + t * ux, py = ay + t * uy;
                    double ox = pts[i].x - px, oy = pts[i].y - py;
                    if (ox * ox + oy * oy > 1.8 * 1.8) continue;
                    pts.RemoveAt(i);
                }
                var ps = PerdeKesitEsitKonumlar(0, len, n);
                for (int i = 0; i < ps.Length; i++)
                {
                    double x = ax + ps[i] * ux, y = ay + ps[i] * uy;
                    if (!Near(x, y)) pts.Add((x, y));
                }
            }

            var overlaps = new List<(double x0, double y0, double x1, double y1)>();
            for (int i = 0; i < etBoxes.Count; i++)
            {
                var a = etBoxes[i];
                for (int j = i + 1; j < etBoxes.Count; j++)
                {
                    var b = etBoxes[j];
                    double ix0 = Math.Max(a.x0, b.x0), ix1 = Math.Min(a.x1, b.x1);
                    double iy0 = Math.Max(a.y0, b.y0), iy1 = Math.Min(a.y1, b.y1);
                    if (ix1 - ix0 >= 2.0 && iy1 - iy0 >= 2.0)
                        overlaps.Add((ix0, iy0, ix1, iy1));
                }
            }

            for (int i = 0; i < etBoxes.Count; i++)
            {
                var b = etBoxes[i];
                double x0 = b.x0 + rad, y0 = b.y0 + rad, x1 = b.x1 - rad, y1 = b.y1 - rad;
                if (x1 - x0 < 4.0 || y1 - y0 < 4.0) continue;
                if (!b.hasInnerCut)
                {
                    FixEdge(x0, y0, x1, y0);
                    FixEdge(x1, y0, x1, y1);
                    FixEdge(x1, y1, x0, y1);
                    FixEdge(x0, y1, x0, y0);
                    continue;
                }
                double a0, a1, c0, c1;
                if (!TryPoligonLeftoverRange(b, overlaps, rad, out a0, out a1, out c0, out c1))
                    continue;
                var st = CollectPoligonCiftSiraStations(pts, b.longIsX, a0, a1, c0, c1);
                if (st.Count < 2) continue;
                bool tight = false;
                for (int s = 0; s < st.Count - 1; s++)
                {
                    if (st[s + 1] - st[s] < minCc - 1e-6) { tight = true; break; }
                }
                int nMax = MaxPoligonKenarDonatiAdet(a1 - a0, minCc);
                if (!tight && st.Count <= nMax) continue;
                int n = st.Count;
                if (n > nMax) n = nMax;
                if (n < 2) n = 2;
                EqualizePoligonCiftSira(pts, b.longIsX, a0, a1, c0, c1, n);
            }
            DedupPoligonDonatiPts(pts, minCc);
        }

        /// <summary>
        /// Aynı düşey açılım şekli: tür, kat yükseklikleri, çap (adet yok — ebat+çap aynıysa birleşir).
        /// </summary>
        private string BuildPoligonArmDuseyAcilimImza(
            ColumnAxisInfo col, Geometry viewPoly, double viewAngDeg, double cx, double cy)
        {
            if (col == null || viewPoly == null || viewPoly.IsEmpty || _model?.Floors == null)
                return "";
            var rot = AffineTransformation.RotationInstance(-viewAngDeg * Math.PI / 180.0, cx, cy);
            var sb = new StringBuilder();
            for (int fi = 0; fi < _model.Floors.Count; fi++)
            {
                var floor = _model.Floors[fi];
                if (!HasColumnOnFloor(floor, col)) continue;
                var extra = GetColumnTableExtraData(floor);
                if (extra == null || !extra.TryGetValue(col.ColumnNo, out var ex) || ex.yukseklikCm < 1.0)
                    continue;
                Geometry g;
                try { g = rot.Transform(viewPoly); }
                catch { g = viewPoly; }
                var e = g.EnvelopeInternal;
                double longCm = Math.Max(e.Width, e.Height);
                double shortCm = Math.Min(e.Width, e.Height);
                int kind = IsDepremPerdeBoyOrani(longCm, shortCm) ? 1 : 2;
                Geometry fullF = TryGetKolonFloorPolygon(fi, col) ?? viewPoly;
                var byDia = CollectPoligonArmKesitAdetByDia(fullF, viewPoly, rot, fi, col.ColumnNo);
                sb.Append(kind).Append('|');
                sb.Append(((int)Math.Round(ex.altKotCm))).Append('|');
                sb.Append(((int)Math.Round(ex.yukseklikCm))).Append('|');
                if (byDia != null)
                {
                    foreach (int d in byDia.Keys)
                        sb.Append(d).Append(',');
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Poligon her kol: plan/kolon açısı çerçevesinde ayrı görünüş. Majör yataylanmaz.
        /// </summary>
        private List<(Geometry rectPoly, double viewAngDeg, double majorCm, double minorCm, string etiket)> CollectPoligonKolonKolViews(
            Geometry firstPoly, double planAngDeg, double cx, double cy, double snapDeg = 0.0)
        {
            var result = new List<(Geometry rectPoly, double viewAngDeg, double majorCm, double minorCm, string etiket)>();
            var p = TryKolonKesitAsPolygon(firstPoly);
            if (p == null) return result;
            if (!IsKolonKesitPoligonKesit(p, p.EnvelopeInternal)) return result;
            var rot = AffineTransformation.RotationInstance(-planAngDeg * Math.PI / 180.0, cx, cy);
            Geometry pr;
            try { pr = rot.Transform(p); }
            catch { pr = p; }
            if (Math.Abs(snapDeg) > 0.5)
            {
                try
                {
                    var cc = pr.Centroid;
                    pr = AffineTransformation.RotationInstance(snapDeg * Math.PI / 180.0, cc.X, cc.Y).Transform(pr);
                }
                catch { }
            }
            var prPoly = TryKolonKesitAsPolygon(pr);
            if (prPoly == null) return result;
            var rects = DecomposeOrthogonalPolygonToRects(prPoly);
            if (rects == null || rects.Count == 0) return result;
            AssignPoligonParcaNumaralari(rects, out int[] nums, out bool[] isPerde);
            var gf = _ntsDrawFactory ?? p.Factory;
            if (gf == null) return result;
            var invPlan = AffineTransformation.RotationInstance(planAngDeg * Math.PI / 180.0, cx, cy);
            var items = new List<(Geometry rectPlan, double viewAngDeg, double majorCm, double minorCm, string etiket, double minX, double minY)>();
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                double w = Math.Abs(r.x1 - r.x0);
                double h = Math.Abs(r.y1 - r.y0);
                double major = Math.Max(w, h);
                double minor = Math.Min(w, h);
                if (major < 8.0 || minor < 4.0) continue;
                if (nums[i] < 1) continue;
                bool xMajor = w + 0.01 >= h;
                double viewAng = xMajor ? (planAngDeg - snapDeg) : (planAngDeg - snapDeg + 90.0);
                string etiket = PoligonParcaEtiketMetni(isPerde[i], nums[i]);
                var coords = new[]
                {
                    new Coordinate(r.x0, r.y0),
                    new Coordinate(r.x1, r.y0),
                    new Coordinate(r.x1, r.y1),
                    new Coordinate(r.x0, r.y1),
                    new Coordinate(r.x0, r.y0)
                };
                Geometry rectRot;
                try { rectRot = gf.CreatePolygon(gf.CreateLinearRing(coords)); }
                catch { continue; }
                if (rectRot == null || rectRot.IsEmpty) continue;
                Geometry rectPlan = rectRot;
                try
                {
                    if (Math.Abs(snapDeg) > 0.5)
                    {
                        var cc = pr.Centroid;
                        rectPlan = AffineTransformation.RotationInstance(-snapDeg * Math.PI / 180.0, cc.X, cc.Y).Transform(rectRot);
                    }
                    rectPlan = invPlan.Transform(rectPlan);
                }
                catch { rectPlan = rectRot; }
                items.Add((rectPlan, viewAng, major, minor, etiket, Math.Min(r.x0, r.x1), Math.Min(r.y0, r.y1)));
            }
            items.Sort((a, b) =>
            {
                int c = b.majorCm.CompareTo(a.majorCm);
                if (c != 0) return c;
                c = a.minX.CompareTo(b.minX);
                if (c != 0) return c;
                return a.minY.CompareTo(b.minY);
            });
            foreach (var it in items)
                result.Add((it.rectPlan, it.viewAngDeg, it.majorCm, it.minorCm, it.etiket));
            return result;
        }

        /// <summary>Açık taraf (U/C ağzı) alta gelecek ek dönüş (derece, plan hizasından sonra).</summary>
        private static double PoligonKesitOpeningSnapDeg(Geometry g)
        {
            var p = TryKolonKesitAsPolygon(g);
            if (p == null || p.IsEmpty) return 0.0;
            var c0 = p.Centroid;
            if (c0 == null || c0.IsEmpty) return 0.0;
            double best = double.NegativeInfinity;
            int bestK = 0;
            for (int k = 0; k < 4; k++)
            {
                Geometry r;
                try
                {
                    r = AffineTransformation.RotationInstance(k * 0.5 * Math.PI, c0.X, c0.Y).Transform(p);
                }
                catch { continue; }
                if (r == null || r.IsEmpty) continue;
                var e = r.EnvelopeInternal;
                var cc = r.Centroid;
                if (e == null || cc == null || cc.IsEmpty) continue;
                double dy = (cc.Y - 0.5 * (e.MinY + e.MaxY)) / Math.Max(1.0, e.Height);
                double dx = (cc.X - 0.5 * (e.MinX + e.MaxX)) / Math.Max(1.0, e.Width);
                double score = dy * 10.0 - Math.Abs(dx);
                if (score > best) { best = score; bestK = k; }
            }
            return bestK * 90.0;
        }

        private Geometry TryPolygonSectionLocalGeometry(int posSectionId)
        {
            if (_ntsDrawFactory == null || posSectionId <= 0) return null;
            if (!TryGetPolygonColumn(posSectionId, new Point2d(0, 0), 0.0, out var pts) || pts == null || pts.Length < 3)
                return null;
            var coords = new Coordinate[pts.Length + 1];
            for (int i = 0; i < pts.Length; i++)
                coords[i] = new Coordinate(pts[i].X, pts[i].Y);
            coords[pts.Length] = coords[0];
            try
            {
                var poly = _ntsDrawFactory.CreatePolygon(_ntsDrawFactory.CreateLinearRing(coords));
                if (poly != null && !poly.IsValid)
                {
                    var b = poly.Buffer(0);
                    if (b != null && !b.IsEmpty) return b;
                }
                return poly;
            }
            catch { return null; }
        }

        /// <summary>
        /// Poligon kolon tipi: zarf + kol ebatları (döndürme/ayna/yerleşim yok). Aynı U kesit tek tip.
        /// </summary>
        private static string CanonicalPoligonKolonTipImza(Geometry g)
        {
            var p = TryKolonKesitAsPolygon(g);
            if (p == null || p.IsEmpty) return "P";
            var e = p.EnvelopeInternal;
            double longCm = Math.Round(Math.Max(e.Width, e.Height));
            double shortCm = Math.Round(Math.Min(e.Width, e.Height));
            var sizes = new List<string>();
            var rects = DecomposeOrthogonalPolygonToRects(p);
            if (rects != null)
            {
                for (int i = 0; i < rects.Count; i++)
                {
                    double w = Math.Abs(rects[i].x1 - rects[i].x0);
                    double h = Math.Abs(rects[i].y1 - rects[i].y0);
                    double a = Math.Max(w, h), b = Math.Min(w, h);
                    if (a < 8.0 || b < 4.0) continue;
                    sizes.Add(Math.Round(a).ToString("0", CultureInfo.InvariantCulture)
                        + "x" + Math.Round(b).ToString("0", CultureInfo.InvariantCulture));
                }
                sizes.Sort(StringComparer.Ordinal);
            }
            return "T" + longCm.ToString("0", CultureInfo.InvariantCulture)
                + "x" + shortCm.ToString("0", CultureInfo.InvariantCulture)
                + ":" + string.Join(",", sizes);
        }

        private const double PoligonKolonBenzerTolCm = 0.3;

        private static string PoligonArmGorunusTipImza(double majorCm, double minorCm)
        {
            return Math.Round(Math.Max(majorCm, minorCm)).ToString("0", CultureInfo.InvariantCulture)
                + "x" + Math.Round(Math.Min(majorCm, minorCm)).ToString("0", CultureInfo.InvariantCulture);
        }

        private void DrawPoligonKolGorunusTipEtiketleri(
            Transaction tr, BlockTableRecord btr, Database db,
            double xMid, double yTop, string etiket, double s)
        {
            if (tr == null || btr == null || string.IsNullOrWhiteSpace(etiket)) return;
            var lines = etiket.Split(new[] { '\n', '|' }, StringSplitOptions.RemoveEmptyEntries);
            double y = yTop;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                DrawBeamLabel(tr, btr, db, new Point3d(xMid, y, 0),
                    line, 12.0 * s, 0.0, LayerYazi, useMiddleCenter: true, colorAci: 14);
                y -= 14.0 * s;
            }
        }

        private static string PoligonParcaEtiketMetni(bool isPerde, int n)
        {
            if (n < 1) return "";
            return "KOL-" + n.ToString(CultureInfo.InvariantCulture);
        }

        private static void AssignPoligonParcaNumaralari(
            List<(double x0, double y0, double x1, double y1)> rects,
            out int[] nums, out bool[] isPerde)
        {
            int n = rects == null ? 0 : rects.Count;
            nums = new int[n];
            isPerde = new bool[n];
            if (n == 0) return;
            var idx = new List<int>();
            for (int i = 0; i < n; i++)
            {
                var r = rects[i];
                double w = Math.Abs(r.x1 - r.x0), h = Math.Abs(r.y1 - r.y0);
                if (w < 8.0 || h < 8.0) continue;
                isPerde[i] = IsDepremPerdeBoyOrani(Math.Max(w, h), Math.Min(w, h));
                idx.Add(i);
            }
            int Cmp(int a, int b)
            {
                var ra = rects[a]; var rb = rects[b];
                double aa = Math.Abs(ra.x1 - ra.x0) * Math.Abs(ra.y1 - ra.y0);
                double bb = Math.Abs(rb.x1 - rb.x0) * Math.Abs(rb.y1 - rb.y0);
                int c = bb.CompareTo(aa);
                if (c != 0) return c;
                c = Math.Min(ra.x0, ra.x1).CompareTo(Math.Min(rb.x0, rb.x1));
                if (c != 0) return c;
                return Math.Min(ra.y0, ra.y1).CompareTo(Math.Min(rb.y0, rb.y1));
            }
            idx.Sort(Cmp);
            for (int i = 0; i < idx.Count; i++) nums[idx[i]] = i + 1;
        }

        private void DrawPoligonParcaNumaralari(
            Transaction tr, BlockTableRecord btr,
            List<(double x0, double y0, double x1, double y1)> rects)
        {
            if (tr == null || btr == null || rects == null || rects.Count == 0) return;
            AssignPoligonParcaNumaralari(rects, out int[] nums, out bool[] isPerde);
            for (int i = 0; i < rects.Count; i++)
            {
                if (nums[i] < 1) continue;
                var r = rects[i];
                double mx = 0.5 * (r.x0 + r.x1), my = 0.5 * (r.y0 + r.y1);
                double rw = Math.Abs(r.x1 - r.x0), rh = Math.Abs(r.y1 - r.y0);
                double ang = rh > rw + 0.01 ? Math.PI / 2.0 : 0.0;
                string lab = PoligonParcaEtiketMetni(isPerde[i], nums[i]);
                DrawBeamLabel(tr, btr, btr.Database, new Point3d(mx, my, 0),
                    lab, 8.0, ang, LayerYazi, useMiddleCenter: true, colorAci: 14);
            }
        }

        private static string CanonicalPoligonKolonImza(Geometry g)
        {
            var p = TryKolonKesitAsPolygon(g);
            if (p == null || p.IsEmpty) return "P";
            var rects = DecomposeOrthogonalPolygonToRects(p);
            if (rects != null && rects.Count > 0)
            {
                var e = p.EnvelopeInternal;
                double cx = 0.5 * (e.MinX + e.MaxX);
                double cy = 0.5 * (e.MinY + e.MaxY);
                string best = null;
                for (int flip = 0; flip < 2; flip++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        double ang = k * 0.5 * Math.PI;
                        double ca = Math.Cos(ang), sa = Math.Sin(ang);
                        var parts = new List<string>(rects.Count);
                        for (int i = 0; i < rects.Count; i++)
                        {
                            var r = rects[i];
                            double mx = 0.5 * (r.x0 + r.x1) - cx;
                            double my = 0.5 * (r.y0 + r.y1) - cy;
                            double w = Math.Abs(r.x1 - r.x0), h = Math.Abs(r.y1 - r.y0);
                            double xr = mx * ca - my * sa, yr = mx * sa + my * ca;
                            if (flip != 0) xr = -xr;
                            double rw = k % 2 == 1 ? h : w;
                            double rh = k % 2 == 1 ? w : h;
                            parts.Add(
                                QuantizePoligonCm(xr).ToString("0.0", CultureInfo.InvariantCulture) + ","
                                + QuantizePoligonCm(yr).ToString("0.0", CultureInfo.InvariantCulture) + ","
                                + QuantizePoligonCm(rw).ToString("0.0", CultureInfo.InvariantCulture) + "x"
                                + QuantizePoligonCm(rh).ToString("0.0", CultureInfo.InvariantCulture));
                        }
                        parts.Sort(StringComparer.Ordinal);
                        string s = string.Join(";", parts);
                        if (best == null || string.CompareOrdinal(s, best) < 0) best = s;
                    }
                }
                return "P" + QuantizePoligonCm(p.Area).ToString("0.0", CultureInfo.InvariantCulture) + ":" + (best ?? "");
            }
            var cs = p.ExteriorRing == null ? null : p.ExteriorRing.Coordinates;
            if (cs == null || cs.Length < 4) return "P";
            double bestAng = 0, bestL = 0;
            for (int i = 0; i < cs.Length - 1; i++)
            {
                if (cs[i] == null || cs[i + 1] == null) continue;
                double dx = cs[i + 1].X - cs[i].X, dy = cs[i + 1].Y - cs[i].Y;
                double L = Math.Sqrt(dx * dx + dy * dy);
                if (L > bestL) { bestL = L; bestAng = Math.Atan2(dy, dx); }
            }
            var c = p.Centroid;
            string bestV = null;
            for (int flip = 0; flip < 2; flip++)
            {
                for (int k = 0; k < 4; k++)
                {
                    double ang = -bestAng + k * 0.5 * Math.PI;
                    double ca = Math.Cos(ang), sa = Math.Sin(ang);
                    var parts = new List<string>();
                    for (int i = 0; i < cs.Length - 1; i++)
                    {
                        if (cs[i] == null) continue;
                        double x = cs[i].X - c.X, y = cs[i].Y - c.Y;
                        double xr = x * ca - y * sa, yr = x * sa + y * ca;
                        if (flip != 0) xr = -xr;
                        parts.Add(QuantizePoligonCm(xr).ToString("0.0", CultureInfo.InvariantCulture)
                            + "," + QuantizePoligonCm(yr).ToString("0.0", CultureInfo.InvariantCulture));
                    }
                    parts.Sort(StringComparer.Ordinal);
                    string s = string.Join(";", parts);
                    if (bestV == null || string.CompareOrdinal(s, bestV) < 0) bestV = s;
                }
            }
            return "P" + QuantizePoligonCm(p.Area).ToString("0.0", CultureInfo.InvariantCulture) + ":" + (bestV ?? "");
        }

        private static double QuantizePoligonCm(double v)
        {
            return Math.Round(v / PoligonKolonBenzerTolCm) * PoligonKolonBenzerTolCm;
        }

        private static void AddUniqueCoord(List<double> dst, double v, double eps)
        {
            if (dst == null) return;
            for (int i = 0; i < dst.Count; i++)
            {
                if (Math.Abs(dst[i] - v) < eps) return;
            }
            dst.Add(v);
        }

        private static List<double> ClusterAxisCoords(List<double> raw, double eps, double envMin, double envMax)
        {
            var reps = new List<double>();
            if (raw == null) return reps;
            raw.Add(envMin);
            raw.Add(envMax);
            raw.Sort();
            int i = 0;
            while (i < raw.Count)
            {
                double lo = raw[i];
                double sum = raw[i];
                int n = 1;
                int j = i + 1;
                while (j < raw.Count && raw[j] - lo <= eps)
                {
                    sum += raw[j];
                    n++;
                    j++;
                }
                reps.Add(sum / n);
                i = j;
            }
            if (reps.Count == 0) return reps;
            reps[0] = envMin;
            reps[reps.Count - 1] = envMax;
            var uniq = new List<double>();
            for (int k = 0; k < reps.Count; k++)
            {
                if (uniq.Count == 0 || Math.Abs(reps[k] - uniq[uniq.Count - 1]) > 0.05)
                    uniq.Add(reps[k]);
                else
                    uniq[uniq.Count - 1] = k == reps.Count - 1 ? envMax : uniq[uniq.Count - 1];
            }
            if (uniq.Count > 0)
            {
                uniq[0] = envMin;
                uniq[uniq.Count - 1] = envMax;
            }
            return uniq;
        }

        private static List<(double x0, double y0, double x1, double y1)> DecomposeOrthogonalPolygonToRects(Polygon p)
        {
            var list = new List<(double x0, double y0, double x1, double y1)>();
            if (p == null || p.IsEmpty || p.ExteriorRing == null) return list;
            Geometry cover = p;
            try
            {
                if (!p.IsValid)
                {
                    var b = p.Buffer(0);
                    if (b != null && !b.IsEmpty) cover = b;
                }
            }
            catch { }
            var env = cover.EnvelopeInternal ?? p.EnvelopeInternal;
            if (env == null || env.Width < 1.0 || env.Height < 1.0) return list;
            var xsRaw = new List<double>();
            var ysRaw = new List<double>();
            var cs = cover.Coordinates;
            if (cs != null)
            {
                for (int i = 0; i < cs.Length; i++)
                {
                    if (cs[i] == null) continue;
                    xsRaw.Add(cs[i].X);
                    ysRaw.Add(cs[i].Y);
                }
            }
            var xs = ClusterAxisCoords(xsRaw, 0.35, env.MinX, env.MaxX);
            var ys = ClusterAxisCoords(ysRaw, 0.35, env.MinY, env.MaxY);
            if (xs.Count < 2 || ys.Count < 2) return list;
            int nx = xs.Count - 1;
            int ny = ys.Count - 1;
            if (nx > 40 || ny > 40) return list;
            var occ = new bool[nx, ny];
            var gf = cover.Factory;
            for (int i = 0; i < nx; i++)
            {
                double w = xs[i + 1] - xs[i];
                if (w < 0.05) continue;
                for (int j = 0; j < ny; j++)
                {
                    double h = ys[j + 1] - ys[j];
                    if (h < 0.05) continue;
                    double cx = 0.5 * (xs[i] + xs[i + 1]);
                    double cy = 0.5 * (ys[j] + ys[j + 1]);
                    bool inside = false;
                    try
                    {
                        if (gf != null)
                        {
                            // C/U boşluğunda ST4 kenarı milimetrik eğik olunca kesişim saç teli
                            // (ör. 0.7 cm²) üretir. Eski eşik Min(0.2, %4) büyük hücrede 0.2 cm²
                            // olduğu için boşluk dolu sayılıp 3 kol tek zarfa birleşiyordu.
                            inside = cover.Covers(gf.CreatePoint(new Coordinate(cx, cy)));
                            if (!inside)
                            {
                                var cellCs = new[]
                                {
                                    new Coordinate(xs[i], ys[j]),
                                    new Coordinate(xs[i + 1], ys[j]),
                                    new Coordinate(xs[i + 1], ys[j + 1]),
                                    new Coordinate(xs[i], ys[j + 1]),
                                    new Coordinate(xs[i], ys[j])
                                };
                                var cell = gf.CreatePolygon(gf.CreateLinearRing(cellCs));
                                var inter = cover.Intersection(cell);
                                double cellA = w * h;
                                if (inter != null && !inter.IsEmpty && cellA > 1e-6 && inter.Area > 0.50 * cellA)
                                    inside = true;
                            }
                        }
                    }
                    catch { inside = false; }
                    occ[i, j] = inside;
                }
            }

            bool Filled(int i0, int i1, int j0, int j1)
            {
                for (int i = i0; i < i1; i++)
                    for (int j = j0; j < j1; j++)
                        if (!occ[i, j]) return false;
                return true;
            }

            bool CanExpand(int i0, int i1, int j0, int j1)
            {
                if (i0 > 0 && Filled(i0 - 1, i0, j0, j1)) return true;
                if (i1 < nx && Filled(i1, i1 + 1, j0, j1)) return true;
                if (j0 > 0 && Filled(i0, i1, j0 - 1, j0)) return true;
                if (j1 < ny && Filled(i0, i1, j1, j1 + 1)) return true;
                return false;
            }

            var used = new bool[nx, ny];
            for (int i0 = 0; i0 < nx; i0++)
            for (int i1 = i0 + 1; i1 <= nx; i1++)
            for (int j0 = 0; j0 < ny; j0++)
            for (int j1 = j0 + 1; j1 <= ny; j1++)
            {
                if (xs[i1] - xs[i0] < 0.5 || ys[j1] - ys[j0] < 0.5) continue;
                if (!Filled(i0, i1, j0, j1)) continue;
                if (CanExpand(i0, i1, j0, j1)) continue;
                list.Add((xs[i0], ys[j0], xs[i1], ys[j1]));
                for (int i = i0; i < i1; i++)
                    for (int j = j0; j < j1; j++)
                        used[i, j] = true;
            }

            for (int i = 0; i < nx; i++)
            {
                for (int j = 0; j < ny; j++)
                {
                    if (!occ[i, j] || used[i, j]) continue;
                    list.Add((xs[i], ys[j], xs[i + 1], ys[j + 1]));
                    used[i, j] = true;
                }
            }

            list.Sort((a, b) =>
            {
                double aa = (a.x1 - a.x0) * (a.y1 - a.y0);
                double bb = (b.x1 - b.x0) * (b.y1 - b.y0);
                int c = bb.CompareTo(aa);
                if (c != 0) return c;
                c = a.y1.CompareTo(b.y1);
                if (c != 0) return -c;
                return a.x0.CompareTo(b.x0);
            });
            return list;
        }

        /// <summary>
        /// TBDY 2018 7.2.8.1: özel deprem etriyesi 135° kanca uç düz boyu, kıvrım son teğetinden
        /// nervürlüde ≥ 6φ ve ≥ 80 mm. TS 500 9.3.2: 135° etriye kancası (iç çap ≥ 4φ; TBDY iç büküm ≥ 5φ).
        /// </summary>
        private static double TbdY2018EtriyeHookExtCm(int diaMm)
        {
            if (diaMm < 6) diaMm = 8;
            double hook = Math.Max(8.0, 6.0 * diaMm / 10.0);
            return Math.Ceiling(hook - 1e-9);
        }

        /// <summary>Açılım kanca düz boyu: 10φ (cm). φ8 → 8, φ10 → 10.</summary>
        private static double EtriyeAcilimKancaCm(int diaMm)
        {
            if (diaMm < 6) diaMm = 8;
            return 10.0 * diaMm / 10.0;
        }

        /// <summary>Çiroz açılım 90° kanca: TS 500 9.3.1.b ≥ 12φ.</summary>
        private static double CirozAcilimHook90Cm(int diaMm)
        {
            if (diaMm < 6) diaMm = 8;
            return Math.Ceiling(12.0 * diaMm / 10.0 - 1e-9);
        }

        /// <summary>Donatı merkez açıklığı → 3 cm paspaylı gövde (40−2×3=34).</summary>
        private static double CirozAcilimStemCm(double barCenterSpan)
        {
            double conc = barCenterSpan + 2.0 * (KolonKesitPaspayiCm + KolonKesitEtriyeRadiusCm);
            return Math.Max(4.0, Math.Round(conc - 2.0 * CirozAcilimPaspayiCm));
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

        private static List<double> UniqueSortedEtriyeZs(IEnumerable<double> zs)
        {
            var zEt = new List<double>();
            if (zs == null) return zEt;
            foreach (double z in zs.OrderBy(v => v))
            {
                if (zEt.Count == 0 || Math.Abs(z - zEt[zEt.Count - 1]) > 1.5)
                    zEt.Add(z);
            }
            return zEt;
        }

        /// <summary>
        /// Görünüş etriye etiketi ile aynı: dilim (za,zb) içinde s_min, adet = ceil(L/s).
        /// </summary>
        private static bool TryEtriyeIntervalAdet(
            double za, double zb,
            IEnumerable<(double zLo, double zHi, int sCm, int diaMm)> bolgeler,
            out int adet, out int sCm, out int diaMm)
        {
            adet = 0;
            sCm = 10;
            diaMm = 8;
            if (zb - za < 2.0) return false;
            int sBest = int.MaxValue;
            if (bolgeler != null)
            {
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
            }
            if (sBest >= 100) return false;
            sCm = sBest;
            return TryKolonEtriyeAdetAralik(zb - za, sBest, out adet, out _);
        }

        /// <summary>
        /// Açılım adedi = görünüşteki dilim etiketleri toplamı (örtüşen bölge toplamı değil).
        /// Dilim, orta noktası [zBot,zTop] içindeyse o kata yazılır.
        /// İlk katta zBot temel içi etriye altına inebilir (kolon / perde başlık); gövde yatay bu aralığı kullanmaz.
        /// </summary>
        private static int SumEtriyeAdetFromGorunusEtiketleri(
            IEnumerable<double> etriyeZs,
            IEnumerable<(double zLo, double zHi, int sCm, int diaMm)> bolgeler,
            double zBot, double zTop,
            out int sCm, out int diaMm)
        {
            sCm = 10;
            diaMm = 8;
            int adet = 0;
            int sMin = int.MaxValue;
            var zEt = UniqueSortedEtriyeZs(etriyeZs);
            for (int i = 0; i < zEt.Count - 1; i++)
            {
                double za = zEt[i], zb = zEt[i + 1];
                double mid = 0.5 * (za + zb);
                if (mid < zBot - 0.5 || mid > zTop + 0.5) continue;
                if (!TryEtriyeIntervalAdet(za, zb, bolgeler, out int n, out int s, out int d))
                    continue;
                adet += n;
                if (s < sMin)
                {
                    sMin = s;
                    sCm = s;
                }
                if (d >= 6) diaMm = d;
            }
            return adet;
        }

        /// <summary>Şekil 7.3 min ℓo'yu sıklaştırma aralığının katına büyüt (90, s=8 → 96).</summary>
        private static double BuyutSarilmaLoKat(double loMin, int sCm, double loMax)
        {
            if (sCm < 1 || loMin < 1.0) return loMin;
            int n = (int)Math.Ceiling(loMin / sCm - 1e-9);
            if (n < 1) n = 1;
            double lo = n * (double)sCm;
            if (lo <= loMax + 0.05) return lo;
            return loMin;
        }

        private static string FormatKolonEtriyeOlcuEtiket(int adet, int diaMm, int sCode)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}\u00F8{1}/{2}", adet, diaMm, sCode);
        }

        private static string FormatPerdeYatayOlcuEtiket(int adet, int diaMm, int sCode, string rol)
        {
            string core = string.Format(CultureInfo.InvariantCulture, "2x{0}\u00F8{1}/{2}", adet, diaMm, sCode);
            if (string.IsNullOrWhiteSpace(rol)) return core;
            return core + " (" + rol + ")";
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
            List<(double zLo, double zHi, int sCm, int diaMm)> etriyeBolgeler = null,
            bool polygonArmView = false)
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
                if (IsDepremPerdeBoyOrani(longCm, shortCm))
                    continue;
                bool isPerdeBasligi = shortCm > 1.0
                    && longCm < KolonKesitPerdeMinBoyOrani * shortCm - 0.01
                    && (polygonArmView || shortCm < KolonKesitPerdeBasligiMaxKenarCm - 0.01);
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
                int sSikCm;
                int sMidCm;
                int sLbCm;
                if (isPerdeBasligi)
                {
                    // Perde başlığı: GPR aralığı olduğu gibi, TBDY Şekil 7.3 sıkılaştırması yok.
                    tekAralik = tekAralikGpr;
                    sSikCm = Math.Max(5, (int)Math.Round(sGprConf));
                    sMidCm = Math.Max(5, (int)Math.Round(sMid));
                    sLbCm = sMidCm;
                }
                else
                {
                    double sSik = TbdY2018KolonSarilmaSMaxCm(shortCm, diaLong);
                    if (!tekAralikGpr) sSik = Math.Min(sSik, sGprConf);
                    sSikCm = Math.Max(5, (int)Math.Floor(sSik + 1e-6));
                    int s0MaxCm = Math.Max(5, (int)Math.Floor(Math.Min(20.0, shortCm * 0.5) + 1e-6));
                    sMidCm = Math.Max(5, (int)Math.Round(sMid));
                    if (sMidCm > s0MaxCm) sMidCm = s0MaxCm;
                    sLbCm = Math.Min(sMidCm, Math.Max(5, (int)Math.Floor(TbdY2018KolonLbEtriyeScCm(shortCm) + 1e-6)));
                }
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
                double bMax = Math.Max(st.hi - st.lo, shortCm);
                double lo = Math.Max(1.5 * bMax, Math.Max(hn / 6.0, 50.0));
                if (lo > hn * 0.45) lo = hn * 0.45;
                if (lo < 15.0) lo = Math.Min(15.0, hn * 0.4);
                // Alt sıklaştırma: Şekil 7.3 min ℓo, etriye aralığının katına büyüt (90/8 → 96).
                double loAlt = BuyutSarilmaLoKat(lo, sSikCm, Math.Max(lo, hn - lo));
                // Üst sıklaştırma üstü: en yüksek kiriş obası. Altı eski ℓo, yükseklik s katına alta kayar.
                double zUstTop = zEtTop;
                double zUstBot = zSoff - lo;
                double hUst = Math.Max(zUstTop - zUstBot, lo);
                double hUstMax = Math.Max(hUst, zUstTop - (z0 + loAlt));
                hUst = BuyutSarilmaLoKat(hUst, sSikCm, hUstMax);
                zUstBot = zUstTop - hUst;
                if (zUstBot < z0 + loAlt)
                    zUstBot = z0 + loAlt;

                double lb = Ts500KenetlenmeLbCm(diaLong, _rebarFckMPa > 16.0 ? _rebarFckMPa : 30.0, _rebarFykMPa > 200.0 ? _rebarFykMPa : 420.0);
                double lap = CeilTo5Cm(Math.Max(lb, 30.0));
                KolonOrtUcdeBindirme(z0, zSoff, lap, out double zSpBot, out double zSpTop);

                var barXs = polygonArmView
                    ? CollectPoligonArmGorunusBars(
                        TryGetKolonFloorPolygon(st.floorIndex, col) ?? st.poly,
                        st.poly, rot, st.floorIndex, col.ColumnNo)
                        .Select(b => b.x).ToList()
                    : GetKolonGorunusLongFaceBarXs(g, st.floorIndex, col.ColumnNo);
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
                    var zs = new List<double> { z0, zUstTop };
                    double ortaCm = zUstBot - (z0 + loAlt);
                    // İki sarılma arası ≥ 40 cm: alt sarılma üstü + üst sarılma altı sabit etriye.
                    if (ortaCm >= 40.0)
                    {
                        zs.Add(z0 + loAlt);
                        zs.Add(zUstBot);
                    }
                    // İki sarılma arası > 100 cm: bindirme (ℓb) sınırlarına da etriye.
                    double sLbEt = TbdY2018KolonLbEtriyeScCm(shortCm);
                    if (ortaCm > 100.0 && sMid > sLbEt + 0.05)
                    {
                        if (zSpBot - (z0 + loAlt) >= 40.0)
                            zs.Add(zSpBot);
                        if (zUstBot - zSpTop >= 40.0)
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

                if (st.zTop > zUstTop + 1.5)
                    Et(st.zTop);

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
                        double sUse;
                        if (isPerdeBasligi)
                            sUse = sGprConfFond > 1.0 ? sGprConfFond : sMid;
                        else
                        {
                            sUse = TbdY2018KolonSarilmaSMaxCm(shortCm, diaLong);
                            if (!tekAralikGpr) sUse = Math.Min(sUse, sGprConfFond);
                        }
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
                if (etriyeBolgeler != null)
                {
                    if (tekAralik)
                    {
                        etriyeBolgeler.Add((z0, st.zTop, sMidCm, diaEt));
                        if (!isPerdeBasligi)
                        {
                            if (zSpTop > zSpBot + 1.0)
                                etriyeBolgeler.Add((zSpBot, zSpTop, sLbCm, diaEt));
                            if (st.zTop > zSoff + 1.0)
                                etriyeBolgeler.Add((zSoff, st.zTop, TbdY2018KolonBirlesimSjCm, diaEt));
                        }
                    }
                    else
                    {
                        etriyeBolgeler.Add((z0, z0 + loAlt, sSikCm, diaEt));
                        if (zUstBot > z0 + loAlt + 1.0)
                            etriyeBolgeler.Add((z0 + loAlt, zUstBot, sMidCm, diaEt));
                        if (zSpTop > zSpBot + 1.0)
                            etriyeBolgeler.Add((zSpBot, zSpTop, sLbCm, diaEt));
                        etriyeBolgeler.Add((zUstBot, zUstTop, sSikCm, diaEt));
                        if (st.zTop > zUstTop + 1.0)
                            etriyeBolgeler.Add((zUstTop, st.zTop, TbdY2018KolonBirlesimSjCm, diaEt));
                    }
                }
                DrawKolonEtriyeSabitKopyalar(tr, btr, X, Y, Ln, x0, x1, st.lo, sabitZs, etriyeBolgeler);
            }
        }

        /// <summary>
        /// TBDY 2018 7.6.4–7.6.5: kolon etriye şeması (hepsi değil). Uç ℓu’da s_son, gövde web’de s_2.
        /// </summary>
        private void DrawPerdeGorunusEtriyeler(
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
            List<(double x0, double x1, double z0, double z1)> temelSpans,
            List<double> etriyeZs,
            List<(double zLo, double zHi, int sCm, int diaMm)> etriyeBolgeler,
            List<(double zLo, double zHi, int sCm, int diaMm)> govdeYatayBolgeler = null,
            bool polygonArmView = false)
        {
            if (tr == null || btr == null || col == null || stories == null || X == null || Y == null || Ln == null)
                return;
            double prevSplitL = double.NaN;
            double prevSplitR = double.NaN;
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
                if (!IsDepremPerdeBoyOrani(longCm, shortCm)) continue;

                string etRaw = null;
                if (_kolonDuseyGpr != null && _model?.Floors != null)
                    KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(
                        _kolonDuseyGpr, _model.Floors, st.floorIndex, col.ColumnNo, out _, out _, out etRaw);
                ParsePerdeEtriyeAralikCm(etRaw, out double sGovde, out double sUc);
                int sUcCm = Math.Max(5, (int)Math.Round(sUc));
                int sGvCm = Math.Max(5, (int)Math.Round(sGovde));
                int diaEt = 8;
                TryParseEtriyeDiaMm(etRaw, out diaEt);

                double lu = ResolvePerdeUcBolgeLuCm(longCm, shortCm, st.floorIndex, col.ColumnNo);
                double z0 = i == 0 ? zDrawBot : st.zBot;
                double z1 = st.zTop;
                bool isLastStory = i == stories.Count - 1;
                if (isLastStory) z1 = st.zTop - 5.0;
                if (z1 <= z0 + 8.0) z1 = st.zTop;

                double splitL = st.lo + lu;
                double splitR = st.hi - lu;
                List<(double x, int dia)> faceBars;
                if (polygonArmView)
                {
                    Geometry fullF = TryGetKolonFloorPolygon(st.floorIndex, col) ?? st.poly;
                    faceBars = CollectPoligonArmGorunusBars(
                        fullF, st.poly, rot, st.floorIndex, col.ColumnNo,
                        out double armL, out double armR, out bool haveLu);
                    if (haveLu)
                    {
                        splitL = armL;
                        splitR = armR;
                    }
                }
                else
                    faceBars = CollectPerdeGorunusLongFaceBars(g, st.floorIndex, col.ColumnNo);

                double xBasL = X(splitL);
                double xBasR = X(splitR);
                Ln(xBasL, Y(z0), xBasL, Y(st.zTop), LayerKesitGorunus);
                Ln(xBasR, Y(z0), xBasR, Y(st.zTop), LayerKesitGorunus);
                if (!double.IsNaN(prevSplitL) && Math.Abs(prevSplitL - splitL) > 1.0)
                    Ln(X(prevSplitL), Y(z0), xBasL, Y(z0), LayerKesitGorunus);
                if (!double.IsNaN(prevSplitR) && Math.Abs(prevSplitR - splitR) > 1.0)
                    Ln(X(prevSplitR), Y(z0), xBasR, Y(z0), LayerKesitGorunus);
                prevSplitL = splitL;
                prevSplitR = splitR;

                double pasBar = KolonKesitPaspayiCm;
                double radBar = KolonKesitDuseyDonatiRadiusCm;
                double lLo = st.lo + pasBar + radBar;
                double lHi = splitL - pasBar - radBar;
                double rLo = splitR + pasBar + radBar;
                double rHi = st.hi - pasBar - radBar;
                if (faceBars != null && faceBars.Count > 0)
                {
                    var leftXs = new List<double>();
                    var rightXs = new List<double>();
                    foreach (var b in faceBars)
                    {
                        if (b.x <= splitL + 0.8) leftXs.Add(b.x);
                        if (b.x >= splitR - 0.8) rightXs.Add(b.x);
                    }
                    if (leftXs.Count > 0)
                    {
                        lLo = leftXs.Min();
                        lHi = leftXs.Max();
                    }
                    if (rightXs.Count > 0)
                    {
                        rLo = rightXs.Min();
                        rHi = rightXs.Max();
                    }
                }
                if (lHi < lLo) lHi = lLo;
                if (rHi < rLo) rHi = rLo;
                // Başlık dış donatılarının 1 cm dışı (kolon görünüşü ile aynı).
                double xUcL0 = X(lLo - 1.0);
                double xUcL1 = X(lHi + 1.0);
                double xUcR0 = X(rLo - 1.0);
                double xUcR1 = X(rHi + 1.0);
                if (xUcL1 < xUcL0) { double t = xUcL0; xUcL0 = xUcL1; xUcL1 = t; }
                if (xUcR1 < xUcR0) { double t = xUcR0; xUcR0 = xUcR1; xUcR1 = t; }

                void UcEt(double z)
                {
                    Ln(xUcL0, Y(z), xUcL1, Y(z), LayerEtriye);
                    Ln(xUcR0, Y(z), xUcR1, Y(z), LayerEtriye);
                }

                UcEt(z0);
                UcEt(z1);
                etriyeZs?.Add(z0);
                etriyeZs?.Add(z1);
                if (!isLastStory)
                    etriyeZs?.Add(st.zTop);
                if (etriyeBolgeler != null)
                    etriyeBolgeler.Add((z0, st.zTop, sUcCm, diaEt));
                govdeYatayBolgeler?.Add((z0, st.zTop, sGvCm, diaEt));

                var bolUc = new List<(double zLo, double zHi, int sCm, int diaMm)> { (z0, z1, sUcCm, diaEt) };
                DrawPerdeEtriyeSabitKopyalar(tr, btr, X, Y, st.lo, new List<double> { z0, z1 }, bolUc, UcEt, kopyaOlcu: true);

                if (i == 0 && temelSpans != null && temelSpans.Count > 0)
                {
                    // Kolon görünüşü ile aynı: TBDY 7.3.4.1 alt sarılma temel içinde ≥ bmin;
                    // çanak/tekil temelde tüm temel yüksekliği. Perde uç etriye aralığı s_son.
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
                        int sCm = sUcCm;
                        double zFondBotEt = double.NaN;
                        for (double z = z0 - sCm; z >= zEnd - 0.05; z -= sCm)
                        {
                            if (z < zCover - 0.05) break;
                            UcEt(z);
                            zFondBotEt = z;
                        }
                        if (!double.IsNaN(zFondBotEt))
                        {
                            etriyeZs?.Add(zFondBotEt);
                            etriyeBolgeler?.Add((zFondBotEt, z0, sUcCm, diaEt));
                        }
                    }
                }

                DrawPerdeGorunusGovdeYataySematik(tr, btr, X, Y, Ln, st.lo, st.hi, z0, z1, sUcCm, sGvCm);
            }
        }

        /// <summary>GPR φ8/[13]/8: köşeli parantez gövde aralığı; çap sonrası ilk sayı gövde yatay, son sayı uç etriye.</summary>
        private static void ParsePerdeEtriyeAralikCm(string etriye, out double sGovde, out double sUc)
        {
            sGovde = 15.0;
            sUc = 8.0;
            if (string.IsNullOrWhiteSpace(etriye)) return;
            string s = etriye.Replace("[", string.Empty).Replace("]", string.Empty);
            var m = Regex.Match(s,
                @"[\u00F8\u00D8ØøφΦ]?\s*\d{1,2}\s*/\s*(\d{1,2})(?:\s*/\s*(\d{1,2}))?");
            if (!m.Success)
            {
                var nums = new List<double>();
                foreach (Match n in Regex.Matches(s, @"/(\d{1,2})"))
                {
                    if (int.TryParse(n.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v >= 4 && v <= 30)
                        nums.Add(v);
                }
                if (nums.Count == 1)
                    sGovde = sUc = nums[0];
                else if (nums.Count >= 2)
                {
                    sGovde = nums[0];
                    sUc = nums[nums.Count - 1];
                }
                return;
            }
            if (int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int a) && a >= 4 && a <= 30)
                sGovde = a;
            if (m.Groups[2].Success &&
                int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int b) && b >= 4 && b <= 30)
                sUc = b;
            else
                sUc = sGovde;
        }

        /// <summary>
        /// Görünüş gövde yatay: uç etriye orta kopyasının biraz altında 3 şematik çubuk + aralarında kopya ölçü.
        /// Kesit gibi perde kenarından 7 cm’ye kadar uzar.
        /// </summary>
        private void DrawPerdeGorunusGovdeYataySematik(
            Transaction tr,
            BlockTableRecord btr,
            Func<double, double> X,
            Func<double, double> Y,
            Action<double, double, double, double, string> Ln,
            double lo,
            double hi,
            double z0,
            double z1,
            int sUcCm,
            int sGvCm)
        {
            if (tr == null || btr == null || X == null || Y == null || Ln == null) return;
            if (z1 <= z0 + 20.0 || sGvCm < 4) return;
            double gLo = lo + PerdeKesitYatayKenarCm;
            double gHi = hi - PerdeKesitYatayKenarCm;
            if (gHi < gLo) { double t = gLo; gLo = gHi; gHi = t; }
            if (gHi - gLo < 8.0) return;

            double zMid = 0.5 * (z0 + z1);
            double sActUc = sUcCm;
            if (z1 - z0 > 180.0 && TryKolonEtriyeAdetAralik(z1 - z0, sUcCm, out _, out double sA) && sA >= 5.0)
                sActUc = sA;
            const double gapCm = 10.0;
            double zHiGv = zMid - sActUc - gapCm;
            double zMidGv = zHiGv - sGvCm;
            double zLoGv = zMidGv - sGvCm;
            double zBotLimit = z0 + sActUc + 4.0;
            if (zLoGv < zBotLimit)
            {
                double shift = zBotLimit - zLoGv;
                zLoGv += shift;
                zMidGv += shift;
                zHiGv += shift;
            }
            if (zHiGv > z1 - 4.0 || zLoGv < z0 + 2.0) return;

            double x0 = X(gLo);
            double x1 = X(gHi);
            if (x1 < x0) { double t = x0; x0 = x1; x1 = t; }
            Ln(x0, Y(zLoGv), x1, Y(zLoGv), LayerDonatiGovde);
            Ln(x0, Y(zMidGv), x1, Y(zMidGv), LayerDonatiGovde);
            Ln(x0, Y(zHiGv), x1, Y(zHiGv), LayerDonatiGovde);
            double olcuX = 0.5 * (gLo + gHi);
            DrawKolonEtriyeKopyaOlcu(tr, btr, X, Y, olcuX, zLoGv, zMidGv);
            DrawKolonEtriyeKopyaOlcu(tr, btr, X, Y, olcuX, zMidGv, zHiGv);
        }

        private double ResolvePerdeUcBolgeLuCm(double lw, double bw, int floorIndex, int colNo)
        {
            bool hasHcr = KolonDonatiTableDrawer.TryGetKolonBetonarmeHcr(_kolonDuseyGprHcr, _model?.Floors, floorIndex, colNo);
            double lu = hasHcr ? Math.Max(2.0 * bw, 0.2 * lw) : Math.Max(bw, 0.1 * lw);
            if (lu < 40.0) lu = 40.0;
            double maxLu = (lw - Math.Max(10.0, bw)) * 0.5;
            if (maxLu < bw) maxLu = lw * 0.45;
            if (lu > maxLu) lu = maxLu;
            if (lu < 8.0) lu = 8.0;
            return lu;
        }

        private void DrawPerdeEtriyeSabitKopyalar(
            Transaction tr,
            BlockTableRecord btr,
            Func<double, double> X,
            Func<double, double> Y,
            double olcuX,
            List<double> sabitZs,
            List<(double zLo, double zHi, int sCm, int diaMm)> bolgeler,
            Action<double> drawAtZ,
            bool kopyaOlcu)
        {
            if (tr == null || btr == null || X == null || Y == null || drawAtZ == null) return;
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
                    drawAtZ(zTo);
                    kopyaZs.Add(zTo);
                }
                if (kopyaOlcu)
                    DrawKolonEtriyeKopyaOlcu(tr, btr, X, Y, olcuX, zFrom, zTo);
            }
            for (int i = 0; i < zs.Count - 1; i++)
            {
                double za = zs[i], zb = zs[i + 1];
                int sCode = KolonEtriyeBolgeSMin(bolgeler, za, zb, out _);
                if (!TryKolonEtriyeAdetAralik(zb - za, sCode, out _, out double sAct)) continue;
                KopyaFrom(za, za + sAct);
                KopyaFrom(zb, zb - sAct);
                if (zb - za > 180.0)
                {
                    double zMid = 0.5 * (za + zb);
                    if (!Yakin(zs, zMid) && !Yakin(kopyaZs, zMid))
                    {
                        drawAtZ(zMid);
                        kopyaZs.Add(zMid);
                    }
                    KopyaFrom(zMid, zMid - sAct);
                    KopyaFrom(zMid, zMid + sAct);
                }
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
                // Uzun boş dilim (>180 cm): ortada 3 kopya, dilim aralığı sAct, sol ölçü.
                if (zb - za > 180.0)
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

        /// <summary>
        /// TBDY 2018 7.6.3 / Şekil 7.6: perde uç bölgesi.
        /// Kritik yükseklikte ℓu ≥ max(2 bw, 0.2 ℓw); dışında ℓu ≥ max(bw, 0.1 ℓw).
        /// Ayrıca ℓu ≥ 40 cm. Uç/gövde ayrımı KESIT GORUNUS; uçta 4 cm paspayı etriye + 4 köşe donatısı.
        /// Gövde yatay donatısı perde kenarından 7 cm; uç gömmesi &lt; ℓb ise 12φ gönye.
        /// </summary>
        private void DrawPerdeKesitUcBolgeleri(Transaction tr, BlockTableRecord btr, Envelope e, int floorIndex, int colNo, List<(double midX, double w, double h)> acilimBoxes = null, List<(double stem, bool govde)> cirozStems = null)
        {
            if (tr == null || btr == null || e == null) return;
            double lw = Math.Max(e.Width, e.Height);
            double bw = Math.Min(e.Width, e.Height);
            if (bw < 8.0 || lw < 6.0 * bw - 0.01) return;
            bool hasHcr = KolonDonatiTableDrawer.TryGetKolonBetonarmeHcr(_kolonDuseyGprHcr, _model?.Floors, floorIndex, colNo);
            double lu = hasHcr ? Math.Max(2.0 * bw, 0.2 * lw) : Math.Max(bw, 0.1 * lw);
            if (lu < 40.0) lu = 40.0;
            double maxLu = (lw - Math.Max(10.0, bw)) * 0.5;
            if (maxLu < bw) maxLu = lw * 0.45;
            if (lu > maxLu) lu = maxLu;
            if (lu < 8.0) return;

            double pas = KolonKesitPaspayiCm;
            double rad = KolonKesitEtriyeRadiusCm;
            int diaMm = ResolveKolonDuseyEtriyeDiaMm(floorIndex, colNo);
            double hook = TbdY2018EtriyeHookExtCm(diaMm);
            bool longIsX = e.Width >= e.Height;

            int ucPerLayer = 8, ucDia = 14, govdePerLayer = 0, govdeDia = 12;
            if (_kolonDuseyGpr != null && _model?.Floors != null &&
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(_kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out string donatiRaw, out _) &&
                KolonDonatiTableDrawer.TryParsePerdeUcGovdeDonati(donatiRaw, out int pUc, out int dUc, out int pGv, out int dGv))
            {
                ucPerLayer = pUc;
                ucDia = dUc;
                govdePerLayer = pGv;
                govdeDia = dGv;
            }
            int nUcEnd = Math.Max(4, ucPerLayer);
            double govdeSpan = Math.Max(1.0, lw - 2.0 * lu - 2.0 * pas);
            int nGovdeFace = Math.Max(1, govdePerLayer);
            const double tbdYGovdeSMaxCm = 25.0;
            int nGovdeMin = 1 + (int)Math.Ceiling(govdeSpan / tbdYGovdeSMaxCm - 1e-9);
            if (nGovdeMin < 2) nGovdeMin = 2;
            if (nGovdeFace < nGovdeMin) nGovdeFace = nGovdeMin;

            void Dash(double ax, double ay, double bx, double by)
            {
                var line = new Line(new Point3d(ax, ay, 0), new Point3d(bx, by, 0));
                line.SetDatabaseDefaults();
                line.Layer = LayerKesitGorunus;
                line.LineWeight = LineWeight.LineWeight020;
                AppendEntity(tr, btr, line);
            }

            void UcEtriye(double x0, double y0, double x1, double y1)
            {
                if (x1 < x0)
                {
                    double t = x0; x0 = x1; x1 = t;
                }
                if (y1 < y0)
                {
                    double t = y0; y0 = y1; y1 = t;
                }
                if (x1 - x0 < 2.0 * rad + 2.0 || y1 - y0 < 2.0 * rad + 2.0) return;
                AppendKolonKesitEtriyePline(tr, btr, x0, y0, x1, y1, rad, hook);
                AddKesitEtriyeBox(acilimBoxes, 0.5 * (x0 + x1), x1 - x0, y1 - y0);
            }

            double minInner = 2.0 * rad + 2.0;
            double innerPas = pas;
            if (lu - 2.0 * pas < minInner)
                innerPas = Math.Max(0.0, (lu - minInner) * 0.5);

            ObjectId dimId = GetOrCreateEtriyeOlcuDimStyle(tr, btr.Database, 10.0);
            double offUc = PerdeKesitBaslikOlcuOfsetCm;
            void UcDim(Point3d a, Point3d b, Point3d linePt)
            {
                var dim = new AlignedDimension(a, b, linePt, "", dimId)
                {
                    Layer = LayerOlcu,
                    LineWeight = LineWeight.LineWeight020
                };
                try { dim.DimfxlenOn = true; } catch { }
                try { dim.Dimfxlen = offUc; } catch { }
                try { dim.Dimrnd = 0.1; } catch { }
                try { dim.Dimdec = 1; } catch { }
                try { dim.Dimtdec = 1; } catch { }
                try { dim.Dimzin = 8; } catch { }
                AppendEntity(tr, btr, dim);
            }

            if (longIsX)
            {
                Dash(e.MinX + lu, e.MinY, e.MinX + lu, e.MaxY);
                Dash(e.MaxX - lu, e.MinY, e.MaxX - lu, e.MaxY);
                UcEtriye(e.MinX + pas, e.MinY + pas, e.MinX + lu - innerPas, e.MaxY - pas);
                UcEtriye(e.MaxX - lu + innerPas, e.MinY + pas, e.MaxX - pas, e.MaxY - pas);
                UcDim(new Point3d(e.MinX, e.MinY, 0), new Point3d(e.MinX + lu, e.MinY, 0),
                    new Point3d(e.MinX + lu * 0.5, e.MinY - offUc, 0));
                UcDim(new Point3d(e.MaxX - lu, e.MinY, 0), new Point3d(e.MaxX, e.MinY, 0),
                    new Point3d(e.MaxX - lu * 0.5, e.MinY - offUc, 0));
            }
            else
            {
                Dash(e.MinX, e.MinY + lu, e.MaxX, e.MinY + lu);
                Dash(e.MinX, e.MaxY - lu, e.MaxX, e.MaxY - lu);
                UcEtriye(e.MinX + pas, e.MinY + pas, e.MaxX - pas, e.MinY + lu - innerPas);
                UcEtriye(e.MinX + pas, e.MaxY - lu + innerPas, e.MaxX - pas, e.MaxY - pas);
                UcDim(new Point3d(e.MinX, e.MinY, 0), new Point3d(e.MinX, e.MinY + lu, 0),
                    new Point3d(e.MinX - offUc, e.MinY + lu * 0.5, 0));
                UcDim(new Point3d(e.MinX, e.MaxY - lu, 0), new Point3d(e.MinX, e.MaxY, 0),
                    new Point3d(e.MinX - offUc, e.MaxY - lu * 0.5, 0));
            }

            double kenar = PerdeKesitYatayKenarCm;
            double x0 = e.MinX + kenar, x1 = e.MaxX - kenar;
            double y0 = e.MinY + kenar, y1 = e.MaxY - kenar;
            if (x1 - x0 > 10.0 && y1 - y0 > 2.0)
            {
                double embedUc = lu - kenar;
                double fck = _rebarFckMPa > 16.0 ? _rebarFckMPa : 30.0;
                double fyk = _rebarFykMPa > 200.0 ? _rebarFykMPa : 420.0;
                double lb = Ts500KenetlenmeLbCm(diaMm, fck, fyk);
                bool gonye12 = embedUc + 0.01 < lb;
                double phi12 = 0.0;
                if (gonye12)
                {
                    phi12 = CeilTo5Cm(12.0 * diaMm / 10.0);
                    double gap = longIsX ? (y1 - y0) : (x1 - x0);
                    if (phi12 > gap * 0.45) phi12 = Math.Max(2.0, gap * 0.45);
                }
                if (longIsX)
                {
                    DrawPerdeKesitYatayBar(tr, btr, x0, y0, x1, y0, gonye12, phi12, inwardPlus: true);
                    DrawPerdeKesitYatayBar(tr, btr, x0, y1, x1, y1, gonye12, phi12, inwardPlus: false);
                }
                else
                {
                    DrawPerdeKesitYatayBar(tr, btr, x0, y0, x0, y1, gonye12, phi12, inwardPlus: true);
                    DrawPerdeKesitYatayBar(tr, btr, x1, y0, x1, y1, gonye12, phi12, inwardPlus: false);
                }
            }

            double barLo = pas + rad;
            double sKenar = bw - 2.0 * barLo;
            double longSpan = lu - innerPas - pas - 2.0 * rad;
            if (sKenar < 1.0) sKenar = 1.0;
            if (longSpan < 1.0) longSpan = 1.0;
            ResolvePerdeUcBarDagilim(nUcEnd, longSpan, sKenar, ucDia / 10.0, out int nLongUse, out int nEndUse, out bool useInner);
            int nDrawn = 2 * nLongUse + Math.Max(0, nEndUse - 2) + (useInner ? Math.Max(0, nEndUse - 2) : 0);
            if (nDrawn < 4) nDrawn = nUcEnd;
            string ucLab = nDrawn.ToString(CultureInfo.InvariantCulture) + "\u00F8" + ucDia.ToString(CultureInfo.InvariantCulture);
            string gvLab = "2x" + nGovdeFace.ToString(CultureInfo.InvariantCulture) + "\u00F8" + govdeDia.ToString(CultureInfo.InvariantCulture);
            if (longIsX)
            {
                double yBot = e.MinY + barLo, yTop = e.MaxY - barLo;
                double xL0 = e.MinX + barLo, xL1 = e.MinX + lu - innerPas - rad;
                double xR0 = e.MaxX - lu + innerPas + rad, xR1 = e.MaxX - barLo;
                if (xL1 < xL0 + 1.0) xL1 = xL0;
                if (xR1 < xR0 + 1.0) xR0 = xR1;
                var xsL = PerdeKesitEsitKonumlar(xL0, xL1, nLongUse);
                var xsR = PerdeKesitEsitKonumlar(xR0, xR1, nLongUse);
                PlacePerdeKesitDuseyCiftSira(tr, btr, xsL, yBot, yTop);
                PlacePerdeKesitDuseyCiftSira(tr, btr, xsR, yBot, yTop);
                PlacePerdeKesitUcKenarAralari(tr, btr, xL0, yBot, yTop, nEndUse, alongY: true);
                PlacePerdeKesitUcKenarAralari(tr, btr, xR1, yBot, yTop, nEndUse, alongY: true);
                if (useInner)
                {
                    PlacePerdeKesitUcKenarAralari(tr, btr, xL1, yBot, yTop, nEndUse, alongY: true);
                    PlacePerdeKesitUcKenarAralari(tr, btr, xR0, yBot, yTop, nEndUse, alongY: true);
                }
                int exL = Math.Max(0, nLongUse - 2);
                int exE = Math.Max(0, nEndUse - 2);
                int exI = useInner ? exE : 0;
                DrawPerdeKesitUcCiroz(tr, btr, xL0, yBot, xL1, yTop, exL, exL, exE, exI, diaMm, hook, drawAlongX: useInner, drawAlongY: true, acilimStems: cirozStems);
                DrawPerdeKesitUcCiroz(tr, btr, xR0, yBot, xR1, yTop, exL, exL, exI, exE, diaMm, hook, drawAlongX: useInner, drawAlongY: true, acilimStems: cirozStems);
                double innerL = xsL[xsL.Length - 1], innerR = xsR[0];
                while (nGovdeFace > 0 && (innerR - innerL) / (nGovdeFace + 1) > 25.01)
                {
                    nGovdeFace++;
                    gvLab = "2x" + nGovdeFace.ToString(CultureInfo.InvariantCulture) + "\u00F8" + govdeDia.ToString(CultureInfo.InvariantCulture);
                }
                var xsG = PerdeKesitAraKonumlar(innerL, innerR, nGovdeFace);
                PlacePerdeKesitDuseyCiftSira(tr, btr, xsG, yBot, yTop);
                DrawPerdeKesitGovdeCirozX(tr, btr, xsG, innerL, innerR, yBot, yTop, diaMm, hook, cirozStems);
                DrawPerdeKesitDonatiCizgiEtiket(tr, btr, xsL[0], yTop, xsL[xsL.Length - 1], yTop, ucLab, alongX: true);
                if (xsG.Length > 0)
                    DrawPerdeKesitDonatiCizgiEtiket(tr, btr, xsG[0], yTop, xsG[xsG.Length - 1], yTop, gvLab, alongX: true);
                DrawPerdeKesitDonatiCizgiEtiket(tr, btr, xsR[0], yTop, xsR[xsR.Length - 1], yTop, ucLab, alongX: true);
            }
            else
            {
                double xL = e.MinX + barLo, xR = e.MaxX - barLo;
                double yB0 = e.MinY + barLo, yB1 = e.MinY + lu - innerPas - rad;
                double yT0 = e.MaxY - lu + innerPas + rad, yT1 = e.MaxY - barLo;
                if (yB1 < yB0 + 1.0) yB1 = yB0;
                if (yT1 < yT0 + 1.0) yT0 = yT1;
                var ysB = PerdeKesitEsitKonumlar(yB0, yB1, nLongUse);
                var ysT = PerdeKesitEsitKonumlar(yT0, yT1, nLongUse);
                PlacePerdeKesitDuseyCiftSiraY(tr, btr, xL, xR, ysB);
                PlacePerdeKesitDuseyCiftSiraY(tr, btr, xL, xR, ysT);
                PlacePerdeKesitUcKenarAralari(tr, btr, yB0, xL, xR, nEndUse, alongY: false);
                PlacePerdeKesitUcKenarAralari(tr, btr, yT1, xL, xR, nEndUse, alongY: false);
                if (useInner)
                {
                    PlacePerdeKesitUcKenarAralari(tr, btr, yB1, xL, xR, nEndUse, alongY: false);
                    PlacePerdeKesitUcKenarAralari(tr, btr, yT0, xL, xR, nEndUse, alongY: false);
                }
                int exL = Math.Max(0, nLongUse - 2);
                int exE = Math.Max(0, nEndUse - 2);
                int exI = useInner ? exE : 0;
                DrawPerdeKesitUcCiroz(tr, btr, xL, yB0, xR, yB1, exI, exE, exL, exL, diaMm, hook, drawAlongX: true, drawAlongY: useInner, acilimStems: cirozStems);
                DrawPerdeKesitUcCiroz(tr, btr, xL, yT0, xR, yT1, exE, exI, exL, exL, diaMm, hook, drawAlongX: true, drawAlongY: useInner, acilimStems: cirozStems);
                double innerB = ysB[ysB.Length - 1], innerT = ysT[0];
                while (nGovdeFace > 0 && (innerT - innerB) / (nGovdeFace + 1) > 25.01)
                {
                    nGovdeFace++;
                    gvLab = "2x" + nGovdeFace.ToString(CultureInfo.InvariantCulture) + "\u00F8" + govdeDia.ToString(CultureInfo.InvariantCulture);
                }
                var ysG = PerdeKesitAraKonumlar(innerB, innerT, nGovdeFace);
                PlacePerdeKesitDuseyCiftSiraY(tr, btr, xL, xR, ysG);
                DrawPerdeKesitGovdeCirozY(tr, btr, ysG, innerB, innerT, xL, xR, diaMm, hook, cirozStems);
                DrawPerdeKesitDonatiCizgiEtiket(tr, btr, xR, ysB[0], xR, ysB[ysB.Length - 1], ucLab, alongX: false);
                if (ysG.Length > 0)
                    DrawPerdeKesitDonatiCizgiEtiket(tr, btr, xR, ysG[0], xR, ysG[ysG.Length - 1], gvLab, alongX: false);
                DrawPerdeKesitDonatiCizgiEtiket(tr, btr, xR, ysT[0], xR, ysT[ysT.Length - 1], ucLab, alongX: false);
            }
        }

        private static double[] PerdeKesitEsitKonumlar(double a, double b, int n)
        {
            if (n < 1) return Array.Empty<double>();
            var p = new double[n];
            if (n == 1)
            {
                p[0] = 0.5 * (a + b);
                return p;
            }
            double s = (b - a) / (n - 1);
            for (int i = 0; i < n; i++)
                p[i] = a + i * s;
            return p;
        }

        private static double[] PerdeKesitAraKonumlar(double a, double b, int n)
        {
            if (n < 1 || b - a < 2.0) return Array.Empty<double>();
            var p = new double[n];
            for (int i = 0; i < n; i++)
                p[i] = a + (i + 1) / (double)(n + 1) * (b - a);
            return p;
        }

        private void PlacePerdeKesitDuseyCiftSira(Transaction tr, BlockTableRecord btr, double[] xs, double yBot, double yTop)
        {
            if (tr == null || btr == null || xs == null) return;
            for (int i = 0; i < xs.Length; i++)
            {
                DrawTwoArcCirclePline(tr, btr, xs[i], yBot, KolonKesitDuseyDonatiRadiusCm, LayerDonatiGovde);
                DrawTwoArcCirclePline(tr, btr, xs[i], yTop, KolonKesitDuseyDonatiRadiusCm, LayerDonatiGovde);
            }
        }

        private void PlacePerdeKesitDuseyCiftSiraY(Transaction tr, BlockTableRecord btr, double xL, double xR, double[] ys)
        {
            if (tr == null || btr == null || ys == null) return;
            for (int i = 0; i < ys.Length; i++)
            {
                DrawTwoArcCirclePline(tr, btr, xL, ys[i], KolonKesitDuseyDonatiRadiusCm, LayerDonatiGovde);
                DrawTwoArcCirclePline(tr, btr, xR, ys[i], KolonKesitDuseyDonatiRadiusCm, LayerDonatiGovde);
            }
        }

        /// <summary>
        /// Uç düşeyler: (1) uzun yüz ve kısa kenar aralıkları birbirine yakın,
        /// (2) iki uzun yüzde aynı konum (karşılıklı),
        /// (3) TBDY 2018 7.6.4 s≤25 cm sağlanmıyorsa araya çubuk,
        /// (4) perde ucu merkezler arası &gt; 20 cm ise kenara çubuk,
        /// (5) 3 kenarda net aralık &lt; 5 cm ise 4. kenar (iç uç) kullanılır.
        /// nLong = her uzun yüz, nEnd = her kısa kenar (köşeler dahil).
        /// 3 kenar toplam = 2*nLong+(nEnd-2); 4 kenar = 2*nLong+2*(nEnd-2).
        /// </summary>
        private static void ResolvePerdeUcBarDagilim(
            int nUcEnd, double longSpan, double sKenar, double diaCm,
            out int nLongUse, out int nEndUse, out bool useInner)
        {
            const double sMax = 25.0;
            const double sKenarMaxCm = 20.0;
            const double sNetMinCm = 5.0;
            useInner = false;
            nLongUse = Math.Max(2, nUcEnd / 2);
            nEndUse = nUcEnd - 2 * nLongUse + 2;
            if (nEndUse < 2)
            {
                nLongUse = Math.Max(2, (nUcEnd - 2) / 2);
                nEndUse = 2;
            }
            PickBestUc3Kenar(nUcEnd, longSpan, sKenar, sMax, ref nLongUse, ref nEndUse);
            ApplyUcAralikEkleri(longSpan, sKenar, sMax, sKenarMaxCm, ref nLongUse, ref nEndUse);
            double phi = diaCm > 0.4 ? diaCm : 1.4;
            double sL = nLongUse > 1 ? longSpan / (nLongUse - 1) : longSpan;
            double sE = nEndUse > 1 ? sKenar / (nEndUse - 1) : sKenar;
            if (sL - phi >= sNetMinCm - 0.01 && sE - phi >= sNetMinCm - 0.01)
                return;
            useInner = true;
            int n3 = 2 * nLongUse + Math.Max(0, nEndUse - 2);
            if (n3 < nUcEnd) n3 = nUcEnd;
            PickBestUc4Kenar(n3, longSpan, sKenar, sMax, ref nLongUse, ref nEndUse);
            ApplyUcAralikEkleri(longSpan, sKenar, sMax, sKenarMaxCm, ref nLongUse, ref nEndUse);
        }

        private static void PickBestUc3Kenar(
            int nUcEnd, double longSpan, double sKenar, double sMax,
            ref int nLongUse, ref int nEndUse)
        {
            double best = double.MaxValue;
            int bestL = nLongUse, bestE = nEndUse;
            int nLMax = Math.Max(2, nUcEnd / 2);
            for (int nL = 2; nL <= nLMax; nL++)
            {
                int nE = nUcEnd - 2 * nL + 2;
                if (nE < 2) continue;
                double score = ScoreUcAralik(nL, nE, longSpan, sKenar, sMax);
                if (score < best - 1e-6)
                {
                    best = score;
                    bestL = nL;
                    bestE = nE;
                }
            }
            nLongUse = bestL;
            nEndUse = bestE;
        }

        private static void PickBestUc4Kenar(
            int nTot, double longSpan, double sKenar, double sMax,
            ref int nLongUse, ref int nEndUse)
        {
            int core = nTot - (nTot % 2);
            if (core < 4) core = 4;
            double best = double.MaxValue;
            int bestL = nLongUse, bestE = nEndUse;
            int nLMax = Math.Max(2, core / 2);
            for (int nL = 2; nL <= nLMax; nL++)
            {
                int nE = core / 2 - nL + 2;
                if (nE < 2) continue;
                double score = ScoreUcAralik(nL, nE, longSpan, sKenar, sMax);
                if (score < best - 1e-6)
                {
                    best = score;
                    bestL = nL;
                    bestE = nE;
                }
            }
            nLongUse = bestL;
            nEndUse = bestE;
        }

        private static double ScoreUcAralik(int nL, int nE, double longSpan, double sKenar, double sMax)
        {
            double sL = nL > 1 ? longSpan / (nL - 1) : longSpan;
            double sE = nE > 1 ? sKenar / (nE - 1) : sKenar;
            double score = Math.Abs(sL - sE);
            if (sL > sMax + 0.01) score += 1000.0 + (sL - sMax);
            if (sE > sMax + 0.01) score += 1000.0 + (sE - sMax);
            return score;
        }

        private static void ApplyUcAralikEkleri(
            double longSpan, double sKenar, double sMax, double sKenarMaxCm,
            ref int nLongUse, ref int nEndUse)
        {
            for (int k = 0; k < 8; k++)
            {
                double sL = nLongUse > 1 ? longSpan / (nLongUse - 1) : longSpan;
                double sE = nEndUse > 1 ? sKenar / (nEndUse - 1) : sKenar;
                if (sE > sMax + 0.01)
                    nEndUse++;
                else if (sL > sMax + 0.01)
                    nLongUse++;
                else
                    break;
            }
            while (nEndUse < 12)
            {
                double sE = nEndUse > 1 ? sKenar / (nEndUse - 1) : sKenar;
                if (sE <= sKenarMaxCm + 0.01) break;
                nEndUse++;
            }
        }

        private void PlacePerdeKesitUcKenarAralari(
            Transaction tr, BlockTableRecord btr,
            double fixedCoord, double a, double b, int nEnd, bool alongY)
        {
            if (tr == null || btr == null || nEnd < 3) return;
            var ps = PerdeKesitEsitKonumlar(a, b, nEnd);
            for (int i = 1; i < ps.Length - 1; i++)
            {
                if (alongY)
                    DrawTwoArcCirclePline(tr, btr, fixedCoord, ps[i], KolonKesitDuseyDonatiRadiusCm, LayerDonatiGovde);
                else
                    DrawTwoArcCirclePline(tr, btr, ps[i], fixedCoord, KolonKesitDuseyDonatiRadiusCm, LayerDonatiGovde);
            }
        }

        private void DrawPerdeKesitUcCiroz(
            Transaction tr, BlockTableRecord btr,
            double xLo, double yLo, double xHi, double yHi,
            int extraTop, int extraBot, int extraLeft, int extraRight,
            int diaMm, double hook135, bool drawAlongX, bool drawAlongY,
            List<(double stem, bool govde)> acilimStems = null)
        {
            if (tr == null || btr == null) return;
            if (xHi < xLo) { double t = xLo; xLo = xHi; xHi = t; }
            if (yHi < yLo) { double t = yLo; yLo = yHi; yHi = t; }
            if (xHi - xLo < 4.0 || yHi - yLo < 4.0) return;
            var env = new Envelope(xLo, xHi, yLo, yHi);
            var cTl = new Point2d(xLo, yHi);
            var cTr = new Point2d(xHi, yHi);
            var cBr = new Point2d(xHi, yLo);
            var cBl = new Point2d(xLo, yLo);
            TryDrawKolonKesitCiroz(tr, btr, env,
                xLo, yLo, xHi, yHi, KolonKesitEtriyeRadiusCm, hook135, diaMm,
                cTl, cTr, cBr, cBl,
                extraTop, extraBot, extraLeft, extraRight,
                false, 0, 0, false, 0, 0,
                drawAlongX, drawAlongY, acilimStems);
        }

        private void DrawPerdeKesitGovdeCirozX(
            Transaction tr, BlockTableRecord btr,
            double[] xsG, double innerL, double innerR, double yBot, double yTop,
            int diaMm, double hook135, List<(double stem, bool govde)> acilimStems = null)
        {
            if (tr == null || btr == null || yTop - yBot < 4.0) return;
            var all = new List<double>();
            if (xsG != null) all.AddRange(xsG);
            all.Add(innerL);
            all.Add(innerR);
            double aMax = KolonKesitCirozAMaxFi * Math.Max(diaMm, 8) / 10.0;
            var pick = PickCirozBarCenters(all, new List<double> { innerL, innerR }, aMax, 1.0);
            pick.Sort();
            double midX = 0.5 * (innerL + innerR);
            double cr = KolonKesitCirozRadiusCm;
            double hook90 = KolonKesitCirozHook90CizimCm;
            double stem = Math.Abs(yTop - yBot);
            for (int i = 0; i < pick.Count; i++)
            {
                DrawKolonKesitCirozC(tr, btr, pick[i], yBot, yTop, cr, hook90, hook135,
                    leftLeg: pick[i] <= midX, hook135OnTop: i % 2 == 0);
                if (acilimStems != null)
                    acilimStems.Add((CirozAcilimStemCm(stem), true));
            }
        }

        private void DrawPerdeKesitGovdeCirozY(
            Transaction tr, BlockTableRecord btr,
            double[] ysG, double innerB, double innerT, double xL, double xR,
            int diaMm, double hook135, List<(double stem, bool govde)> acilimStems = null)
        {
            if (tr == null || btr == null || xR - xL < 4.0) return;
            var all = new List<double>();
            if (ysG != null) all.AddRange(ysG);
            all.Add(innerB);
            all.Add(innerT);
            double aMax = KolonKesitCirozAMaxFi * Math.Max(diaMm, 8) / 10.0;
            var pick = PickCirozBarCenters(all, new List<double> { innerB, innerT }, aMax, 1.0);
            pick.Sort();
            double midY = 0.5 * (innerB + innerT);
            double cr = KolonKesitCirozRadiusCm;
            double hook90 = KolonKesitCirozHook90CizimCm;
            double stem = Math.Abs(xR - xL);
            for (int j = 0; j < pick.Count; j++)
            {
                DrawKolonKesitCirozCHorizontal(tr, btr, xL, xR, pick[j], cr, hook90, hook135,
                    bottomLeg: pick[j] <= midY, hook135OnRight: j % 2 == 0);
                if (acilimStems != null)
                    acilimStems.Add((CirozAcilimStemCm(stem), true));
            }
        }

        private void DrawPerdeKesitDonatiCizgiEtiket(
            Transaction tr, BlockTableRecord btr,
            double ax, double ay, double bx, double by, string text, bool alongX, string origNote = null,
            double stemSign = 1.0, double stemCmOverride = double.NaN, double txtFromLineCm = double.NaN)
        {
            if (tr == null || btr == null || string.IsNullOrWhiteSpace(text)) return;
            if (stemSign >= 0.0) stemSign = 1.0;
            else stemSign = -1.0;
            double stemCm = !double.IsNaN(stemCmOverride) ? stemCmOverride : 10.0 + PerdeKesitDuseyEtiketYukariCm;
            const double txtH = 8.0;
            double txtOff = !double.IsNaN(txtFromLineCm)
                ? txtFromLineCm + txtH * 0.5
                : txtH * 0.5 + 1.0;
            Point2d p0, p1, p2, p3;
            Point3d txt;
            if (alongX)
            {
                double hy = ay + stemSign * stemCm;
                p0 = new Point2d(ax, ay);
                p1 = new Point2d(ax, hy);
                p2 = new Point2d(bx, hy);
                p3 = new Point2d(bx, by);
                txt = new Point3d(0.5 * (ax + bx), hy + stemSign * txtOff, 0);
            }
            else
            {
                double hx = ax + stemSign * stemCm;
                p0 = new Point2d(ax, ay);
                p1 = new Point2d(hx, ay);
                p2 = new Point2d(hx, by);
                p3 = new Point2d(bx, by);
                txt = new Point3d(hx + stemSign * txtOff, 0.5 * (ay + by), 0);
            }
            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            pl.Layer = LayerEtiketCizgisi;
            pl.LineWeight = LineWeight.LineWeight015;
            pl.Color = Color.FromColorIndex(ColorMethod.ByAci, 64);
            pl.AddVertexAt(0, p0, 0, 0, 0);
            pl.AddVertexAt(1, p1, 0, 0, 0);
            pl.AddVertexAt(2, p2, 0, 0, 0);
            pl.AddVertexAt(3, p3, 0, 0, 0);
            AppendEntity(tr, btr, pl);
            double rot = alongX ? 0.0 : Math.PI / 2.0;
            DrawBeamLabel(tr, btr, btr.Database, txt, text, txtH, rot,
                LayerDonatiYazisiPerde, useMiddleCenter: true);
            if (string.IsNullOrWhiteSpace(origNote)) return;
            string note = "(" + origNote + ")";
            double half = 0.55 * txtH * text.Length;
            Point3d pNote = alongX
                ? new Point3d(txt.X + half + 1.5, txt.Y, 0)
                : new Point3d(txt.X, txt.Y + half + 1.5, 0);
            ObjectId styleId = GetOrCreateYaziBeykentTextStyle(tr, btr.Database);
            var red = new DBText
            {
                Layer = LayerDonatiYazisiPerde,
                TextStyleId = styleId,
                Height = txtH,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(note),
                Position = pNote,
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                AlignmentPoint = pNote,
                Rotation = rot,
                Color = Color.FromColorIndex(ColorMethod.ByAci, 1),
                LineWeight = LineWeight.LineWeight020
            };
            try { red.AdjustAlignment(btr.Database); } catch { }
            AppendEntity(tr, btr, red);
        }

        /// <summary>
        /// Uç çizgisinden kenara 7 cm’lik gömme &lt; ℓb ise o uçta 12φ gönye (R=1.5 cm); aksi halde düz.
        /// inwardPlus: gönye diğer yatağa doğru (+Y veya +X).
        /// </summary>
        private void DrawPerdeKesitYatayBar(
            Transaction tr, BlockTableRecord btr,
            double ax, double ay, double bx, double by,
            bool gonye12, double phi12, bool inwardPlus)
        {
            DrawPerdeKesitYatayBar(tr, btr, ax, ay, bx, by, gonye12, gonye12, phi12, inwardPlus);
        }

        private void DrawPerdeKesitYatayBar(
            Transaction tr, BlockTableRecord btr,
            double ax, double ay, double bx, double by,
            bool gonyeA, bool gonyeB, double phi12, bool inwardPlus)
        {
            bool alongX = Math.Abs(by - ay) < 0.5;
            double hk = ((!gonyeA && !gonyeB) || phi12 < 1.0) ? 0.0 : (inwardPlus ? phi12 : -phi12);
            if (alongX)
                AppendGonyeliDonatiBar(tr, btr, true, ax, bx, ay, gonyeA, gonyeB, hk);
            else
                AppendGonyeliDonatiBar(tr, btr, false, ay, by, ax, gonyeA, gonyeB, hk);
        }

        /// <summary>Gövde yatay: uçta gönye varsa 1.5 cm radius; gönye gerekmeyen uç düz.</summary>
        private void AppendGonyeliDonatiBar(
            Transaction tr, BlockTableRecord btr,
            bool alongX, double a0, double a1, double p,
            bool gonye0, bool gonye1, double hk,
            bool writeHookLabel = false)
        {
            if (a1 < a0)
            {
                double t = a0; a0 = a1; a1 = t;
                bool g = gonye0; gonye0 = gonye1; gonye1 = g;
            }
            if (Math.Abs(hk) < 1.0)
            {
                gonye0 = false;
                gonye1 = false;
            }
            if (!gonye0 && !gonye1)
            {
                if (alongX)
                    AppendDonatiPline(tr, btr, new[] { new Point2d(a0, p), new Point2d(a1, p) }, null, LayerDonatiGovde);
                else
                    AppendDonatiPline(tr, btr, new[] { new Point2d(p, a0), new Point2d(p, a1) }, null, LayerDonatiGovde);
                return;
            }
            const double k90 = 0.41421356237;
            double R = KolonKesitEtriyeRadiusCm;
            double absHk = Math.Abs(hk);
            if (R > absHk * 0.45) R = Math.Max(0.5, absHk * 0.45);
            if (R > (a1 - a0) * 0.4) R = Math.Max(0.3, (a1 - a0) * 0.4);
            double s = hk >= 0 ? 1.0 : -1.0;
            double bulge = alongX ? (s > 0 ? k90 : -k90) : (s > 0 ? -k90 : k90);
            Point2d Pt(double a, double q) => alongX ? new Point2d(a, q) : new Point2d(q, a);
            double pHook = p + hk;
            double pR = p + s * R;
            var pts = new List<Point2d>();
            var bul = new List<double>();
            if (gonye0)
            {
                pts.Add(Pt(a0, pHook));
                bul.Add(0);
                pts.Add(Pt(a0, pR));
                bul.Add(bulge);
                pts.Add(Pt(a0 + R, p));
                bul.Add(0);
            }
            else
            {
                pts.Add(Pt(a0, p));
                bul.Add(0);
            }
            if (gonye1)
            {
                pts.Add(Pt(a1 - R, p));
                bul.Add(bulge);
                pts.Add(Pt(a1, pR));
                bul.Add(0);
                pts.Add(Pt(a1, pHook));
                bul.Add(0);
            }
            else
            {
                pts.Add(Pt(a1, p));
                bul.Add(0);
            }
            AppendDonatiPline(tr, btr, pts.ToArray(), bul.ToArray(), LayerDonatiGovde);
            if (writeHookLabel && (gonye0 || gonye1) && absHk >= 1.0)
            {
                string lab = absHk.ToString("0", CultureInfo.InvariantCulture);
                bool use0 = gonye0;
                double aHook = use0 ? a0 : a1;
                const double txtH = 8.0;
                const double txtOff = 6.0;
                if (alongX)
                {
                    double xTxt = aHook + (use0 ? -txtOff : txtOff);
                    double yTxt = p + hk * 0.5;
                    DrawBeamLabel(tr, btr, btr.Database, new Point3d(xTxt, yTxt, 0),
                        lab, txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                }
                else
                {
                    double yTxt = aHook + (use0 ? -txtOff : txtOff);
                    double xTxt = p + hk * 0.5;
                    DrawBeamLabel(tr, btr, btr.Database, new Point3d(xTxt, yTxt, 0),
                        lab, txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                }
            }
        }

        private void DrawKolonKesitEtriye(Transaction tr, BlockTableRecord btr, Geometry g, Envelope e, int floorIndex, int colNo, bool skipIkinciEtriye = false, List<(double midX, double w, double h)> acilimBoxes = null, List<(double stem, bool govde)> cirozStems = null)
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
            AddKesitEtriyeBox(acilimBoxes, 0.5 * (x0 + x1), x1 - x0, y1 - y0);
            ResolveKolonKesitBarCounts(x0, y0, x1, y1, rad, floorIndex, colNo,
                out var cTl, out var cTr, out var cBr, out var cBl,
                out int top, out int bot, out int left, out int right);
            bool hasInnerX = false, hasInnerY = false;
            double innerXLo = 0, innerXHi = 0, innerYLo = 0, innerYHi = 0;
            if (!skipIkinciEtriye)
                TryDrawKolonKesitIkinciEtriyeler(tr, btr, e, x0, y0, x1, y1, rad, hook, cTl, cTr, cBr, cBl, top, bot, left, right,
                    out hasInnerX, out innerXLo, out innerXHi, out hasInnerY, out innerYLo, out innerYHi, acilimBoxes);
            TryDrawKolonKesitCiroz(tr, btr, e, x0, y0, x1, y1, rad, hook, diaMm, cTl, cTr, cBr, cBl, top, bot, left, right,
                hasInnerX, innerXLo, innerXHi, hasInnerY, innerYLo, innerYHi, true, true, cirozStems);
            DrawKolonKesitDuseyDonatiCemberleri(tr, btr, cTl, cTr, cBr, cBl, top, bot, left, right);
        }

        /// <summary>
        /// Kenar ≥ 60 cm ise o doğrultuda kapalı iç etriye.
        /// Tek iç etriye: ≥120 (veya kare) → 1/3–2/3, değilse 1/4–3/4.
        /// İki iç etriye (her iki kenar ≥ 60, 3. etriye var): ikisi de 1/3–2/3.
        /// </summary>
        private void TryDrawKolonKesitIkinciEtriyeler(
            Transaction tr, BlockTableRecord btr, Envelope e,
            double x0, double y0, double x1, double y1, double rad, double hook,
            Point2d cTl, Point2d cTr, Point2d cBr, Point2d cBl,
            int top, int bot, int left, int right,
            out bool hasInnerX, out double innerXLo, out double innerXHi,
            out bool hasInnerY, out double innerYLo, out double innerYHi,
            List<(double midX, double w, double h)> acilimBoxes = null)
        {
            hasInnerX = hasInnerY = false;
            innerXLo = innerXHi = innerYLo = innerYHi = 0;
            if (e == null) return;
            double colW = e.Width, colH = e.Height;
            bool kare = Math.Abs(colW - colH) < 3.0;
            bool ucEtriye = colW >= KolonKesitIkinciEtriyeMinUzunCm - 0.01
                && colH >= KolonKesitIkinciEtriyeMinUzunCm - 0.01;
            if (colW >= KolonKesitIkinciEtriyeMinUzunCm - 0.01)
            {
                IkinciEtriyeHedefKesir(colW, kare, ucEtriye, out double fLo, out double fHi);
                var xs = CollectEdgeCoords(cTl, cTr, Math.Max(top, bot), horizontal: true);
                if (TryNearestBarPair(xs, e.MinX + fLo * colW, e.MinX + fHi * colW, out innerXLo, out innerXHi))
                {
                    double x0s = innerXLo - rad, x1s = innerXHi + rad;
                    if (x1s - x0s >= 2.0 * rad + 2.0 && y1 - y0 >= 2.0 * rad + 2.0)
                    {
                        AppendKolonKesitEtriyePline(tr, btr, x0s, y0, x1s, y1, rad, hook);
                        AddKesitEtriyeBox(acilimBoxes, 0.5 * (x0s + x1s), x1s - x0s, y1 - y0);
                        hasInnerX = true;
                    }
                }
            }
            if (colH >= KolonKesitIkinciEtriyeMinUzunCm - 0.01)
            {
                IkinciEtriyeHedefKesir(colH, kare, ucEtriye, out double fLo, out double fHi);
                var ys = CollectEdgeCoords(cBl, cTl, Math.Max(left, right), horizontal: false);
                if (TryNearestBarPair(ys, e.MinY + fLo * colH, e.MinY + fHi * colH, out innerYLo, out innerYHi))
                {
                    double y0s = innerYLo - rad, y1s = innerYHi + rad;
                    if (x1 - x0 >= 2.0 * rad + 2.0 && y1s - y0s >= 2.0 * rad + 2.0)
                    {
                        AppendKolonKesitEtriyePline(tr, btr, x0, y0s, x1, y1s, rad, hook);
                        AddKesitEtriyeBox(acilimBoxes, 0.5 * (x0 + x1), x1 - x0, y1s - y0s);
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
            bool hasInnerY, double innerYLo, double innerYHi,
            bool drawAlongX = true, bool drawAlongY = true,
            List<(double stem, bool govde)> acilimStems = null,
            bool govde = false)
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

            if (drawAlongY)
            {
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
                    if (acilimStems != null)
                        acilimStems.Add((CirozAcilimStemCm(Math.Abs(cTl.Y - cBl.Y)), govde));
                }
            }

            if (drawAlongX)
            {
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
                    if (acilimStems != null)
                        acilimStems.Add((CirozAcilimStemCm(Math.Abs(cBr.X - cBl.X)), govde));
                }
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

        private static void IkinciEtriyeHedefKesir(double colLongCm, bool kare, bool forceUc, out double fLo, out double fHi)
        {
            if (forceUc || kare || colLongCm >= KolonKesitIkinciEtriyeBirUcCm - 0.01)
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
            double x0, double y0, double x1, double y1, double rad, double hook,
            string layer = null)
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
            AppendDonatiPline(tr, btr, pts, bul, string.IsNullOrEmpty(layer) ? LayerEtriye : layer);
        }

        /// <summary>
        /// Açık etriye: üst kol, sağ üst radius yayının merkezine göre döner; çizgi yaya teğet kalır.
        /// </summary>
        private void AppendKolonKesitEtriyeAcikAgiz(
            Transaction tr, BlockTableRecord btr,
            double x0, double y0, double x1, double y1, double rad, double hook)
        {
            if (x1 - x0 < 2.0 * rad + 2.0 || y1 - y0 < 2.0 * rad + 2.0) return;
            double c45 = Math.Cos(Math.PI / 4.0);
            var cTl = new Point2d(x0 + rad, y1 - rad);
            var tTop = new Point2d(x0 + rad, y1);
            var tLeft = new Point2d(x0, y1 - rad);
            var p225 = new Point2d(cTl.X - rad * c45, cTl.Y - rad * c45);
            var p45 = new Point2d(cTl.X + rad * c45, cTl.Y + rad * c45);
            var tipTop = new Point2d(p225.X + hook * c45, p225.Y - hook * c45);
            var tipLeft = new Point2d(p45.X + hook * c45, p45.Y - hook * c45);
            var fillet = new Point2d(x1 - rad, y1 - rad);
            double topLen = Math.Max((x1 - rad) - tTop.X, 1.0);
            double ang = KesitEtriyeAcikAgizAngRad(topLen);
            double ca = Math.Cos(ang), sa = Math.Sin(ang);
            Point2d Lift(Point2d p)
            {
                double dx = p.X - fillet.X, dy = p.Y - fillet.Y;
                return new Point2d(fillet.X + dx * ca + dy * sa, fillet.Y - dx * sa + dy * ca);
            }
            var tTan = Lift(new Point2d(x1 - rad, y1));
            double remainDeg = Math.Max(5.0, 90.0 - ang * 180.0 / Math.PI);
            double bCorner = -Math.Tan(remainDeg * Math.PI / 180.0 / 4.0);
            double b90 = -Math.Tan(Math.PI / 8.0);
            double b135 = -Math.Tan(135.0 * Math.PI / 180.0 / 4.0);
            var pts = new[]
            {
                Lift(tipTop),
                Lift(p225),
                Lift(tTop),
                tTan,
                new Point2d(x1, y1 - rad),
                new Point2d(x1, y0 + rad),
                new Point2d(x1 - rad, y0),
                new Point2d(x0 + rad, y0),
                new Point2d(x0, y0 + rad),
                tLeft,
                p45,
                tipLeft
            };
            var bul = new[] { 0.0, b135, 0.0, bCorner, 0.0, b90, 0.0, b90, 0.0, b135, 0.0, 0.0 };
            AppendDonatiPline(tr, btr, pts, bul, LayerEtriye);
        }

        /// <summary>
        /// Açık ağız: sol üst kanca radius merkezi ile kaldırılmış kanca radius merkezi arası 12 cm.
        /// Chord = 2·topLen·sin(θ/2) → θ = 2·asin(6/topLen).
        /// </summary>
        private static double KesitEtriyeAcikAgizAngRad(double topLen)
        {
            if (topLen < 1.0) topLen = 1.0;
            double s = 0.5 * KolonKesitEtriyeAcikAgizKancaMerkezAraCm / topLen;
            if (s > 0.95) s = 0.95;
            if (s < 1e-9) return 0.0;
            return 2.0 * Math.Asin(s);
        }

        private static double KesitEtriyeAcikAgizLiftCm(double w, double rad)
        {
            double topLen = Math.Max(w - 2.0 * rad, 1.0);
            return topLen * Math.Sin(KesitEtriyeAcikAgizAngRad(topLen));
        }

        private static void AddKesitEtriyeBox(List<(double midX, double w, double h)> dest, double midX, double w, double h)
        {
            if (dest == null || w < 4.0 || h < 4.0) return;
            dest.Add((midX, w, h));
        }

        /// <summary>
        /// Takım (kesit + etriye açılım) sağ taşıması: 2. kolon etriyesi kesitin sağına çıkar.
        /// 120 cm ölçü takımın en sağ elemanına göre; taşma kesit genişliğinden düşülür.
        /// </summary>
        private static double EstimateKesitTakimExtraRightCm(Envelope e, bool isPerdeKesit)
        {
            if (e == null || isPerdeKesit) return 0.0;
            double pas = KolonKesitPaspayiCm;
            double w0 = e.Width - 2.0 * pas;
            if (w0 < 4.0) return 0.0;
            if (e.Width < KolonKesitIkinciEtriyeMinUzunCm - 0.01)
                return 0.0;
            bool uc = e.Width >= KolonKesitIkinciEtriyeMinUzunCm - 0.01
                && e.Height >= KolonKesitIkinciEtriyeMinUzunCm - 0.01;
            bool kare = Math.Abs(e.Width - e.Height) < 3.0;
            double spanFrac = (uc || kare || e.Width >= KolonKesitIkinciEtriyeBirUcCm - 0.01)
                ? (1.0 / 3.0) : 0.5;
            double w1 = spanFrac * e.Width + 2.0 * KolonKesitEtriyeRadiusCm;
            if (w1 < 4.0) w1 = 4.0;
            double firstX1 = e.MaxX - pas;
            double secondX1 = firstX1 + KolonKesitEtriyeAcilimYanGapCm + w1;
            return Math.Max(0.0, secondX1 - e.MaxX);
        }

        private bool TryPerdeKesitDuseyAcilimAdet(
            Envelope e, int floorIndex, int colNo,
            out int ucDia, out int nUcToplam, out int gvDia, out int nGvToplam)
        {
            ucDia = 14;
            gvDia = 12;
            nUcToplam = 0;
            nGvToplam = 0;
            if (e == null) return false;
            double lw = Math.Max(e.Width, e.Height);
            double bw = Math.Min(e.Width, e.Height);
            if (bw < 8.0 || lw < 6.0 * bw - 0.01) return false;
            bool hasHcr = KolonDonatiTableDrawer.TryGetKolonBetonarmeHcr(_kolonDuseyGprHcr, _model?.Floors, floorIndex, colNo);
            double lu = hasHcr ? Math.Max(2.0 * bw, 0.2 * lw) : Math.Max(bw, 0.1 * lw);
            if (lu < 40.0) lu = 40.0;
            double maxLu = (lw - Math.Max(10.0, bw)) * 0.5;
            if (maxLu < bw) maxLu = lw * 0.45;
            if (lu > maxLu) lu = maxLu;
            if (lu < 8.0) return false;

            double pas = KolonKesitPaspayiCm;
            double rad = KolonKesitEtriyeRadiusCm;
            int ucPerLayer = 8, govdePerLayer = 0;
            if (_kolonDuseyGpr != null && _model?.Floors != null &&
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(_kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out string donatiRaw, out _) &&
                KolonDonatiTableDrawer.TryParsePerdeUcGovdeDonati(donatiRaw, out int pUc, out int dUc, out int pGv, out int dGv))
            {
                ucPerLayer = pUc;
                ucDia = dUc;
                govdePerLayer = pGv;
                gvDia = dGv;
            }
            if (ucDia < 6) ucDia = 14;
            if (gvDia < 6) gvDia = 12;
            int nUcEnd = Math.Max(4, ucPerLayer);
            double govdeSpan = Math.Max(1.0, lw - 2.0 * lu - 2.0 * pas);
            int nGovdeFace = Math.Max(1, govdePerLayer);
            const double tbdYGovdeSMaxCm = 25.0;
            int nGovdeMin = 1 + (int)Math.Ceiling(govdeSpan / tbdYGovdeSMaxCm - 1e-9);
            if (nGovdeMin < 2) nGovdeMin = 2;
            if (nGovdeFace < nGovdeMin) nGovdeFace = nGovdeMin;

            double minInner = 2.0 * rad + 2.0;
            double innerPas = pas;
            if (lu - 2.0 * pas < minInner)
                innerPas = Math.Max(0.0, (lu - minInner) * 0.5);
            double barLo = pas + rad;
            double sKenar = bw - 2.0 * barLo;
            double longSpan = lu - innerPas - pas - 2.0 * rad;
            if (sKenar < 1.0) sKenar = 1.0;
            if (longSpan < 1.0) longSpan = 1.0;
            ResolvePerdeUcBarDagilim(nUcEnd, longSpan, sKenar, ucDia / 10.0, out int nLongUse, out int nEndUse, out bool useInner);
            int nDrawn = 2 * nLongUse + Math.Max(0, nEndUse - 2) + (useInner ? Math.Max(0, nEndUse - 2) : 0);
            if (nDrawn < 4) nDrawn = nUcEnd;
            nUcToplam = 2 * nDrawn;

            bool longIsX = e.Width >= e.Height;
            if (longIsX)
            {
                double xL0 = e.MinX + barLo, xL1 = e.MinX + lu - innerPas - rad;
                double xR0 = e.MaxX - lu + innerPas + rad, xR1 = e.MaxX - barLo;
                if (xL1 < xL0 + 1.0) xL1 = xL0;
                if (xR1 < xR0 + 1.0) xR0 = xR1;
                var xsL = PerdeKesitEsitKonumlar(xL0, xL1, nLongUse);
                var xsR = PerdeKesitEsitKonumlar(xR0, xR1, nLongUse);
                if (xsL.Length > 0 && xsR.Length > 0)
                {
                    double innerL = xsL[xsL.Length - 1], innerR = xsR[0];
                    while (nGovdeFace > 0 && (innerR - innerL) / (nGovdeFace + 1) > 25.01)
                        nGovdeFace++;
                }
            }
            else
            {
                double yB0 = e.MinY + barLo, yB1 = e.MinY + lu - innerPas - rad;
                double yT0 = e.MaxY - lu + innerPas + rad, yT1 = e.MaxY - barLo;
                if (yB1 < yB0 + 1.0) yB1 = yB0;
                if (yT1 < yT0 + 1.0) yT0 = yT1;
                var ysB = PerdeKesitEsitKonumlar(yB0, yB1, nLongUse);
                var ysT = PerdeKesitEsitKonumlar(yT0, yT1, nLongUse);
                if (ysB.Length > 0 && ysT.Length > 0)
                {
                    double innerB = ysB[ysB.Length - 1], innerT = ysT[0];
                    while (nGovdeFace > 0 && (innerT - innerB) / (nGovdeFace + 1) > 25.01)
                        nGovdeFace++;
                }
            }
            nGvToplam = 2 * nGovdeFace;
            return nUcToplam > 0 || nGvToplam > 0;
        }

        private void OverlayPerdeKesitDuseyAdet(
            SortedDictionary<int, int> byDia, Envelope e, int floorIndex, int colNo)
        {
            if (byDia == null) return;
            if (!TryPerdeKesitDuseyAcilimAdet(e, floorIndex, colNo, out int ucDia, out int nUc, out int gvDia, out int nGv))
                return;
            if (ucDia == gvDia)
            {
                int n = nUc + nGv;
                if (n > 0) byDia[ucDia] = n;
                return;
            }
            if (ucDia >= 6 && nUc > 0) byDia[ucDia] = nUc;
            if (gvDia >= 6 && nGv > 0) byDia[gvDia] = nGv;
        }

        /// <summary>
        /// Kesitte alt kat/başlık düşeyi üst kattan az olmasın diye arttırılır.
        /// Açılım etiketi aynı adedi kullansın.
        /// </summary>
        private void OverlayKolonBaslikDuseyAdet(
            int floorIndex, int colNo, ref SortedDictionary<int, int> byDia)
        {
            if (!TryResolveKolonAltKatDuseyDonati(floorIndex, colNo, out _, out _, out _, out string drawOzet))
                return;
            if (string.IsNullOrWhiteSpace(drawOzet)) return;
            if (!KolonDonatiTableDrawer.TryParseKolonKesitDuseyDonatiByDia(drawOzet, out var adj) ||
                adj == null || adj.Count == 0)
                return;
            byDia = adj;
        }

        /// <summary>
        /// Poligon kesit: perde uç etriyesi ve perde yatay gövde açılımı, kola paralel, poligon dışına.
        /// Düşey/gövde donatı açılıma işlenmez.
        /// </summary>
        private double DrawPoligonKolonKesitParcaAcilimlari(
            Transaction tr, BlockTableRecord btr, Envelope overall, Geometry g,
            List<(double x0, double y0, double x1, double y1)> rects,
            List<(double x0, double y0, double x1, double y1, bool longIsX, bool hasInnerCut, bool innerIsMax)> etBoxes,
            List<(Envelope env, bool longIsX, double luLo, double luHi, double ipLo, double ipHi)> koller,
            int adet, int diaMm,
            int floorIndex, int colNo, double storyZBot, double storyZTop)
        {
            double takimMaxY = overall != null ? overall.MaxY + PoligonKolonDisOlcuToplamCm : 0.0;
            if (tr == null || btr == null) return takimMaxY;
            double cx = overall != null ? 0.5 * (overall.MinX + overall.MaxX) : 0.0;
            double cy = overall != null ? 0.5 * (overall.MinY + overall.MaxY) : 0.0;
            if (g != null && !g.IsEmpty)
            {
                try
                {
                    var c = g.Centroid;
                    cx = c.X;
                    cy = c.Y;
                }
                catch { }
            }
            double gap = PoligonKolonDisOlcuToplamCm + 12.0;
            if (etBoxes != null)
            for (int i = 0; i < etBoxes.Count; i++)
            {
                var b = etBoxes[i];
                double bx0 = Math.Min(b.x0, b.x1), bx1 = Math.Max(b.x0, b.x1);
                double by0 = Math.Min(b.y0, b.y1), by1 = Math.Max(b.y0, b.y1);
                double ew = bx1 - bx0, eh = by1 - by0;
                if (ew < 4.0 || eh < 4.0) continue;
                double mx = 0.5 * (bx0 + bx1), my = 0.5 * (by0 + by1);
                double prx0 = bx0, pry0 = by0, prx1 = bx1, pry1 = by1;
                bool longIsX = b.longIsX;
                bool isPerdeEt = b.hasInnerCut;
                if (rects != null)
                {
                    double bestA = isPerdeEt ? -1.0 : double.MaxValue;
                    for (int k = 0; k < rects.Count; k++)
                    {
                        var r = rects[k];
                        double rx0 = Math.Min(r.x0, r.x1), rx1 = Math.Max(r.x0, r.x1);
                        double ry0 = Math.Min(r.y0, r.y1), ry1 = Math.Max(r.y0, r.y1);
                        if (mx < rx0 - 1.0 || mx > rx1 + 1.0 || my < ry0 - 1.0 || my > ry1 + 1.0)
                            continue;
                        double rw = rx1 - rx0, rh = ry1 - ry0;
                        bool rLongX = rw + 0.01 >= rh;
                        bool rPerde = IsDepremPerdeBoyOrani(Math.Max(rw, rh), Math.Min(rw, rh));
                        double a = Math.Max(1.0, rw * rh);
                        if (isPerdeEt)
                        {
                            if (!rPerde || rLongX != longIsX) continue;
                            if (a <= bestA) continue;
                        }
                        else if (a >= bestA)
                            continue;
                        else
                            longIsX = rLongX;
                        bestA = a;
                        prx0 = rx0; pry0 = ry0; prx1 = rx1; pry1 = ry1;
                    }
                }
                double rcx = 0.5 * (prx0 + prx1), rcy = 0.5 * (pry0 + pry1);
                int dirX = 0, dirY = 0;
                if (longIsX)
                    dirY = rcy >= cy - 1e-6 ? 1 : -1;
                else
                    dirX = rcx >= cx - 1e-6 ? 1 : -1;
                double ax0, ay0, ax1, ay1;
                if (dirY > 0)
                {
                    ay0 = pry1 + gap;
                    ay1 = ay0 + eh;
                    ax0 = bx0;
                    ax1 = bx1;
                }
                else if (dirY < 0)
                {
                    ay1 = pry0 - gap;
                    ay0 = ay1 - eh;
                    ax0 = bx0;
                    ax1 = bx1;
                }
                else if (dirX > 0)
                {
                    ax0 = prx1 + gap;
                    ax1 = ax0 + ew;
                    ay0 = by0;
                    ay1 = by1;
                }
                else
                {
                    ax1 = prx0 - gap;
                    ax0 = ax1 - ew;
                    ay0 = by0;
                    ay1 = by1;
                }
                DrawPoligonParcaEtriyeAcilimAt(tr, btr, ax0, ay0, ax1, ay1, dirX, dirY, adet, diaMm);
                if (dirY > 0)
                {
                    double lift = KesitEtriyeAcikAgizLiftCm(Math.Max(ax1 - ax0, 1.0), KolonKesitEtriyeRadiusCm);
                    takimMaxY = Math.Max(takimMaxY, ay1 + lift + 4.0);
                }
            }

            if (rects == null) return takimMaxY;
            double govdeGap = PoligonKolonDisOlcuToplamCm + 12.0 + 50.0;
            for (int k = 0; k < rects.Count; k++)
            {
                var r = rects[k];
                double rx0 = Math.Min(r.x0, r.x1), rx1 = Math.Max(r.x0, r.x1);
                double ry0 = Math.Min(r.y0, r.y1), ry1 = Math.Max(r.y0, r.y1);
                double rw = rx1 - rx0, rh = ry1 - ry0;
                if (rw < 8.0 || rh < 8.0) continue;
                if (!IsDepremPerdeBoyOrani(Math.Max(rw, rh), Math.Min(rw, rh))) continue;
                bool longIsX = rw + 0.01 >= rh;
                double rcx = 0.5 * (rx0 + rx1), rcy = 0.5 * (ry0 + ry1);
                int dirX = 0, dirY = 0;
                if (longIsX)
                    dirY = rcy >= cy - 1e-6 ? 1 : -1;
                else
                    dirX = rcx >= cx - 1e-6 ? 1 : -1;
                double far;
                if (dirY > 0) far = ry1 + govdeGap;
                else if (dirY < 0) far = ry0 - govdeGap;
                else if (dirX > 0) far = rx1 + govdeGap;
                else far = rx0 - govdeGap;
                DrawPoligonParcaGovdeYatayAcilim(
                    tr, btr, rx0, ry0, rx1, ry1, longIsX, dirX, dirY, far,
                    floorIndex, colNo, storyZBot, storyZTop, koller);
                if (dirY > 0)
                    takimMaxY = Math.Max(takimMaxY, far + 5.0);
            }
            return takimMaxY;
        }

        /// <summary>Kol görünüşü: yalnız o renkli dikdörtgenin etriye açılımları (düşey donatı değil).</summary>
        private void DrawPoligonArmGorunusEtriyeAcilimlari(
            Transaction tr, BlockTableRecord btr,
            List<(double zBot, double zTop, Geometry poly, double majorDeg, double lo, double hi, int floorIndex)> stories,
            AffineTransformation rot,
            Func<double, double> X, Func<double, double> Y,
            double zMin, int adet, int diaMm, int colNo)
        {
            if (tr == null || btr == null || stories == null || stories.Count == 0 || X == null || Y == null)
                return;
            var st = stories[0];
            if (st.poly == null || st.poly.IsEmpty) return;
            Geometry g;
            try { g = rot != null ? rot.Transform(st.poly) : st.poly; }
            catch { g = st.poly; }
            var e = g.EnvelopeInternal;
            if (e == null || e.Width < 8.0 || e.Height < 4.0) return;
            var boxes = CollectPoligonRectEtriyeKutulari(e, st.floorIndex, colNo);
            if (boxes == null || boxes.Count == 0) return;
            double gap = 40.0;
            double y1 = Y(zMin) - gap;
            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                double w = Math.Abs(b.x1 - b.x0);
                double h = Math.Abs(b.y1 - b.y0);
                if (w < 4.0 || h < 4.0) continue;
                double x0 = X(Math.Min(b.x0, b.x1));
                double x1 = X(Math.Max(b.x0, b.x1));
                if (x1 < x0) { double t = x0; x0 = x1; x1 = t; }
                double y0 = y1 - h;
                DrawPoligonParcaEtriyeAcilimAt(tr, btr, x0, y0, x1, y1, 0, -1, adet, diaMm);
            }
        }

        /// <summary>Kesitteki etriye kutuları: perde uçları veya kolon 1. etriye.</summary>
        private List<(double x0, double y0, double x1, double y1)> CollectPoligonRectEtriyeKutulari(
            Envelope e, int floorIndex, int colNo)
        {
            var list = new List<(double x0, double y0, double x1, double y1)>();
            if (e == null) return list;
            double lw = Math.Max(e.Width, e.Height);
            double bw = Math.Min(e.Width, e.Height);
            if (bw < 8.0) return list;
            double pas = KolonKesitPaspayiCm;
            double rad = KolonKesitEtriyeRadiusCm;
            double minInner = 2.0 * rad + 2.0;
            if (IsDepremPerdeBoyOrani(lw, bw))
            {
                bool longIsX = e.Width >= e.Height;
                double lu = ResolvePerdeUcBolgeLuCm(lw, bw, floorIndex, colNo);
                if (lu < 8.0) lu = Math.Max(bw, 30.0);
                double InnerPas(double luUse)
                {
                    if (luUse - 2.0 * pas < minInner)
                        return Math.Max(0.0, (luUse - minInner) * 0.5);
                    return pas;
                }
                if (longIsX)
                {
                    list.Add((e.MinX + pas, e.MinY + pas, e.MinX + lu - InnerPas(lu), e.MaxY - pas));
                    list.Add((e.MaxX - lu + InnerPas(lu), e.MinY + pas, e.MaxX - pas, e.MaxY - pas));
                }
                else
                {
                    list.Add((e.MinX + pas, e.MinY + pas, e.MaxX - pas, e.MinY + lu - InnerPas(lu)));
                    list.Add((e.MinX + pas, e.MaxY - lu + InnerPas(lu), e.MaxX - pas, e.MaxY - pas));
                }
            }
            else
            {
                list.Add((e.MinX + pas, e.MinY + pas, e.MaxX - pas, e.MaxY - pas));
            }
            return list;
        }

        private void DrawPoligonParcaEtriyeAcilimAt(
            Transaction tr, BlockTableRecord btr,
            double x0, double y0, double x1, double y1,
            int dirX, int dirY, int adet, int diaMm)
        {
            if (x1 < x0) { double t = x0; x0 = x1; x1 = t; }
            if (y1 < y0) { double t = y0; y0 = y1; y1 = t; }
            double w = x1 - x0, h = y1 - y0;
            if (w < 4.0 || h < 4.0) return;
            if (adet < 1) adet = 1;
            if (diaMm < 6) diaMm = 8;
            double rad = KolonKesitEtriyeRadiusCm;
            double hook = EtriyeAcilimKancaCm(diaMm);
            const double txtH = 8.0;
            const double txtOff = 7.0;
            var db = btr.Database;
            AppendKolonKesitEtriyePline(tr, btr, x0, y0, x1, y1, rad, hook, LayerKesitGorunus);
            AppendKolonKesitEtriyeAcikAgiz(tr, btr, x0, y0, x1, y1, rad, hook);
            double lift = KesitEtriyeAcikAgizLiftCm(w, rad);
            if (dirX != 0)
            {
                DrawBeamLabel(tr, btr, db, new Point3d(x1 - txtOff, 0.5 * (y0 + y1), 0),
                    h.ToString("0", CultureInfo.InvariantCulture),
                    txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                DrawBeamLabel(tr, btr, db, new Point3d(0.5 * (x0 + x1), y0 + txtH - 15.0, 0),
                    w.ToString("0", CultureInfo.InvariantCulture),
                    txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
            }
            else
            {
                DrawBeamLabel(tr, btr, db, new Point3d(x0 - txtOff, 0.5 * (y0 + y1), 0),
                    h.ToString("0", CultureInfo.InvariantCulture),
                    txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                DrawBeamLabel(tr, btr, db, new Point3d(0.5 * (x0 + x1), y0 + txtH, 0),
                    w.ToString("0", CultureInfo.InvariantCulture),
                    txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
            }
            DrawBeamLabel(tr, btr, db, new Point3d(x0 - txtOff, y1 + lift, 0),
                hook.ToString("0", CultureInfo.InvariantCulture),
                txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
            double L = Math.Round(2.0 * (w + h) + 2.0 * hook);
            string tag = string.Format(CultureInfo.InvariantCulture, "{0}\u00F8{1} L={2:0}", adet, diaMm, L);
            double mx = 0.5 * (x0 + x1), my = 0.5 * (y0 + y1);
            double ang = 0.0;
            if (dirX != 0)
            {
                ang = Math.PI / 2.0;
                mx = x1 + txtOff + 6.0;
            }
            else
                my = y0 - txtOff - 6.0;
            DrawBeamLabel(tr, btr, db, new Point3d(mx, my, 0),
                tag, 10.0, ang, LayerDonatiYazisiPerde, useMiddleCenter: true);
        }

        private void DrawPoligonParcaGovdeYatayAcilim(
            Transaction tr, BlockTableRecord btr,
            double rx0, double ry0, double rx1, double ry1,
            bool longIsX, int dirX, int dirY, double far,
            int floorIndex, int colNo, double storyZBot, double storyZTop,
            List<(Envelope env, bool longIsX, double luLo, double luHi, double ipLo, double ipHi)> koller = null)
        {
            if (tr == null || btr == null) return;
            double lw = Math.Max(rx1 - rx0, ry1 - ry0);
            double bw = Math.Min(rx1 - rx0, ry1 - ry0);
            if (bw < 8.0 || !IsDepremPerdeBoyOrani(lw, bw)) return;
            double kenar = PerdeKesitYatayKenarCm;
            double stem = lw - 2.0 * kenar;
            if (stem < 8.0) return;
            int diaMm = ResolveKolonDuseyEtriyeDiaMm(floorIndex, colNo);
            if (diaMm < 6) diaMm = 8;
            string etRaw = null;
            if (_kolonDuseyGpr != null && _model?.Floors != null)
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(
                    _kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out _, out etRaw);
            ParsePerdeEtriyeAralikCm(etRaw, out double sGovde, out _);
            int sGvCm = Math.Max(5, (int)Math.Round(sGovde));
            double hStory = storyZTop > storyZBot + 1.0 ? storyZTop - storyZBot : 280.0;
            if (!TryKolonEtriyeAdetAralik(hStory, sGvCm, out int adet, out _) || adet < 1)
                adet = 1;
            double lu = ResolvePerdeUcBolgeLuCm(lw, bw, floorIndex, colNo);
            double luLoUse = lu, luHiUse = lu;
            if (koller != null)
            {
                double mx = 0.5 * (rx0 + rx1), my = 0.5 * (ry0 + ry1);
                for (int i = 0; i < koller.Count; i++)
                {
                    var k = koller[i];
                    if (k.env == null || k.longIsX != longIsX) continue;
                    if (mx < k.env.MinX - 1.0 || mx > k.env.MaxX + 1.0 ||
                        my < k.env.MinY - 1.0 || my > k.env.MaxY + 1.0)
                        continue;
                    luLoUse = k.luLo;
                    luHiUse = k.luHi;
                    break;
                }
            }
            double fck = _rebarFckMPa > 16.0 ? _rebarFckMPa : 30.0;
            double fyk = _rebarFykMPa > 200.0 ? _rebarFykMPa : 420.0;
            double lb = Ts500KenetlenmeLbCm(diaMm, fck, fyk);
            bool gonyeLo = (luLoUse - kenar) + 0.01 < lb;
            bool gonyeHi = (luHiUse - kenar) + 0.01 < lb;
            double hk = 0.0;
            if (gonyeLo || gonyeHi)
            {
                hk = CeilTo5Cm(12.0 * diaMm / 10.0);
                if (hk > bw * 0.45) hk = Math.Max(2.0, bw * 0.45);
            }
            double L = Math.Round(stem + (gonyeLo ? hk : 0.0) + (gonyeHi ? hk : 0.0));
            double half = 0.5 * PerdeKesitGovdeYatayCiftAralikCm;
            string tag = string.Format(CultureInfo.InvariantCulture, "2x{0}\u00F8{1} L={2:0}", adet, diaMm, L);
            double hkToward = hk;
            if (longIsX)
            {
                if (dirY > 0) hkToward = -hk;
                double cx = 0.5 * (rx0 + rx1);
                double a0 = cx - 0.5 * stem, a1 = cx + 0.5 * stem;
                double yMid = far;
                double yA = yMid + (dirY >= 0 ? half : -half);
                double yB = yMid - (dirY >= 0 ? half : -half);
                DrawPoligonGovdeYatayCift(tr, btr, true, a0, a1, yA, yB, hkToward, gonyeLo, gonyeHi);
                DrawBeamLabel(tr, btr, btr.Database, new Point3d(cx, yMid, 0),
                    tag, 10.0, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
            }
            else
            {
                if (dirX > 0) hkToward = -hk;
                double cy = 0.5 * (ry0 + ry1);
                double a0 = cy - 0.5 * stem, a1 = cy + 0.5 * stem;
                double xMid = far;
                double xA = xMid + (dirX >= 0 ? half : -half);
                double xB = xMid - (dirX >= 0 ? half : -half);
                DrawPoligonGovdeYatayCift(tr, btr, false, a0, a1, xA, xB, hkToward, gonyeLo, gonyeHi);
                DrawBeamLabel(tr, btr, btr.Database, new Point3d(xMid, cy, 0),
                    tag, 10.0, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
            }
        }

        /// <summary>Poligon parça çiroz açılımı: kalınlık yönü gövde, dışa.</summary>
        private void DrawPoligonParcaCirozAcilim(
            Transaction tr, BlockTableRecord btr,
            double rx0, double ry0, double rx1, double ry1,
            bool longIsX, int dirX, int dirY, double far,
            int floorIndex, int colNo, double storyZBot, double storyZTop, int adet)
        {
            if (tr == null || btr == null) return;
            double rw = rx1 - rx0, rh = ry1 - ry0;
            if (rw < 8.0 || rh < 8.0) return;
            int diaMm = ResolveKolonDuseyEtriyeDiaMm(floorIndex, colNo);
            if (diaMm < 6) diaMm = 8;
            if (adet < 1) adet = 1;
            double pas = KolonKesitPaspayiCm;
            double radEt = KolonKesitEtriyeRadiusCm;
            double barLo = pas + radEt;
            double spanShort = Math.Min(rw, rh) - 2.0 * barLo;
            if (spanShort < 4.0) spanShort = Math.Max(4.0, Math.Min(rw, rh) - 8.0);
            double stemShort = CirozAcilimStemCm(spanShort);
            double spanLong = Math.Max(rw, rh) - 2.0 * barLo;
            if (spanLong < 4.0) spanLong = Math.Max(4.0, Math.Max(rw, rh) - 8.0);
            double stemLong = CirozAcilimStemCm(spanLong);
            double hook135 = EtriyeAcilimKancaCm(diaMm);
            double hook90 = CirozAcilimHook90Cm(diaMm);
            double cr = KolonKesitCirozRadiusCm;
            bool isPerde = IsDepremPerdeBoyOrani(Math.Max(rw, rh), Math.Min(rw, rh));
            void DrawOne(double stem, double midA, double midB, bool alongX, int shift)
            {
                if (stem < 4.0) return;
                double L = Math.Round(stem + hook90 + hook135);
                string tag = string.Format(CultureInfo.InvariantCulture, "{0}\u00F8{1} L={2:0}", adet, diaMm, L);
                double off = shift * 22.0;
                if (alongX)
                {
                    double y = midB + off;
                    double xL = midA - 0.5 * stem, xR = midA + 0.5 * stem;
                    DrawKolonKesitCirozCHorizontal(tr, btr, xL, xR, y, cr, hook90, hook135,
                        bottomLeg: dirY < 0, hook135OnRight: false);
                    DrawBeamLabel(tr, btr, btr.Database, new Point3d(midA, y + 10.0, 0),
                        tag, 8.0, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                }
                else
                {
                    double x = midB + off;
                    double y0 = midA - 0.5 * stem, y1 = midA + 0.5 * stem;
                    DrawKolonKesitCirozC(tr, btr, x, y0, y1, cr, hook90, hook135,
                        leftLeg: dirX < 0, hook135OnTop: false);
                    DrawBeamLabel(tr, btr, btr.Database, new Point3d(x, midA, 0),
                        tag, 8.0, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                }
            }
            if (longIsX)
            {
                double cx = 0.5 * (rx0 + rx1);
                DrawOne(stemShort, cx, far, true, 0);
                if (!isPerde)
                    DrawOne(stemLong, cx, far, true, dirY >= 0 ? 1 : -1);
            }
            else
            {
                double cy = 0.5 * (ry0 + ry1);
                DrawOne(stemShort, cy, far, false, 0);
                if (!isPerde)
                    DrawOne(stemLong, cy, far, false, dirX >= 0 ? 1 : -1);
            }
        }

        private void DrawPoligonGovdeYatayCift(
            Transaction tr, BlockTableRecord btr, bool alongX,
            double a0, double a1, double pA, double pB, double hk, bool gonye0, bool gonye1)
        {
            double mag = Math.Abs(hk);
            double hkA = (pB >= pA) ? mag : -mag;
            double hkB = -hkA;
            bool anyG = (gonye0 || gonye1) && mag >= 1.0;
            double stg = anyG ? PerdeKesitGovdeYataySasirtmaCm : 0.0;
            AppendGonyeliDonatiBar(tr, btr, alongX, a0 - 0.5 * stg, a1 - 0.5 * stg, pA, gonye0, gonye1, hkA, writeHookLabel: anyG);
            AppendGonyeliDonatiBar(tr, btr, alongX, a0 + 0.5 * stg, a1 + 0.5 * stg, pB, gonye0, gonye1, hkB, writeHookLabel: false);
        }

        /// <summary>
        /// Kesit altı etriye açılımı: kancalı orijinal (KESIT GORUNUS) + açık ağız (ETRIYE).
        /// Kolon: 1. kesit altı, 2. onun sağı, 3. birincinin altı. Perde uçları kesit midX hizası.
        /// Kanca 10φ; kanca yazısı açık kancanın solunda/üstünde.
        /// </summary>
        private double DrawKolonKesitEtriyeAcilim(
            Transaction tr, BlockTableRecord btr,
            Envelope sectionE,
            List<(double midX, double w, double h)> boxes,
            int diaMm,
            int adet,
            int sCm,
            bool isPerdeKesit,
            out double xMax,
            out double yCirozBelow,
            double yTopOverride = double.NaN)
        {
            xMax = sectionE != null ? sectionE.MaxX : 0.0;
            double yTop = sectionE != null
                ? sectionE.MinY - (isPerdeKesit
                    ? PerdeKesitBaslikOlcuOfsetCm + PerdeKesitUzunlukOlcuBaslikAltiCm + PerdeKesitEtriyeAcilimOlcuAltiCm
                    : Kolon50GorunusCiftOlcuAraCm + KolonKesitEtriyeAcilimGapCm)
                : 0.0;
            if (!double.IsNaN(yTopOverride))
                yTop = yTopOverride;
            yCirozBelow = yTop;
            if (tr == null || btr == null || sectionE == null || boxes == null || boxes.Count == 0)
                return yTop;
            double rad = KolonKesitEtriyeRadiusCm;
            double hook = EtriyeAcilimKancaCm(diaMm);
            if (adet < 1) adet = 1;
            if (diaMm < 6) diaMm = 8;
            const double txtH = 8.0;
            const double txtOff = 7.0;
            var db = btr.Database;
            string hookTxt = hook.ToString("0", CultureInfo.InvariantCulture);

            var ordered = new List<(double midX, double w, double h)>();
            foreach (var b in boxes)
            {
                if (b.w >= 4.0 && b.h >= 4.0) ordered.Add(b);
            }
            if (isPerdeKesit)
            {
                ordered.Sort((a, b) =>
                {
                    if (Math.Abs(a.midX - b.midX) < 8.0)
                        return b.w.CompareTo(a.w);
                    return a.midX.CompareTo(b.midX);
                });
            }

            double AcikAgizLift(double w) => KesitEtriyeAcikAgizLiftCm(w, rad);

            double prevX1 = double.NegativeInfinity;
            double yBot = yTop;
            double firstX0 = 0, firstX1 = 0, firstTagBot = 0;
            for (int i = 0; i < ordered.Count; i++)
            {
                var b = ordered[i];
                double x0;
                double y1;
                if (isPerdeKesit)
                {
                    x0 = b.midX - b.w * 0.5;
                    if (x0 < prevX1 + KolonKesitEtriyeAcilimYanGapCm)
                        x0 = prevX1 + KolonKesitEtriyeAcilimYanGapCm;
                    y1 = yTop;
                }
                else if (i == 0)
                {
                    x0 = 0.5 * (sectionE.MinX + sectionE.MaxX) - b.w * 0.5;
                    y1 = yTop;
                }
                else if (i == 1)
                {
                    x0 = firstX1 + KolonKesitEtriyeAcilimYanGapCm;
                    y1 = yTop;
                }
                else
                {
                    x0 = firstX0;
                    y1 = firstTagBot - KolonKesitEtriyeAcilimAltGapCm - AcikAgizLift(b.w);
                }
                double x1 = x0 + b.w;
                double y0 = y1 - b.h;
                AppendKolonKesitEtriyePline(tr, btr, x0, y0, x1, y1, rad, hook, LayerKesitGorunus);
                AppendKolonKesitEtriyeAcikAgiz(tr, btr, x0, y0, x1, y1, rad, hook);

                DrawBeamLabel(tr, btr, db, new Point3d(x0 - txtOff, (y0 + y1) * 0.5, 0),
                    b.h.ToString("0", CultureInfo.InvariantCulture),
                    txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                DrawBeamLabel(tr, btr, db, new Point3d((x0 + x1) * 0.5, y0 + txtH, 0),
                    b.w.ToString("0", CultureInfo.InvariantCulture),
                    txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);

                double lift = AcikAgizLift(b.w);
                DrawBeamLabel(tr, btr, db, new Point3d(x0 - txtOff, y1 + lift, 0),
                    hookTxt, txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);

                double L = Math.Round(2.0 * (b.w + b.h) + 2.0 * hook);
                string tag = string.Format(CultureInfo.InvariantCulture,
                    "{0}\u00F8{1} L={2:0}", adet, diaMm, L);
                double tagY = y0 - txtOff - 6.0;
                DrawBeamLabel(tr, btr, db, new Point3d((x0 + x1) * 0.5, tagY, 0),
                    tag, 10.0, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                double tagBot = tagY - 5.0;
                yBot = Math.Min(yBot, tagBot);

                prevX1 = x1;
                xMax = Math.Max(xMax, x1);
                if (!isPerdeKesit && i == 0)
                {
                    firstX0 = x0;
                    firstX1 = x1;
                    firstTagBot = tagBot;
                    yCirozBelow = tagBot;
                }
                else if (!isPerdeKesit && i == 2)
                    yCirozBelow = tagBot;
            }
            return yBot;
        }

        /// <summary>
        /// Çiroz açılımı: kesitin altında. Düz gövde üstte, kancalar aşağı (90° / 135°).
        /// Perde: başlık = etriye adedi; gövde = Hcr 10/m² + normal 4/m² (tüm kat yüksekliği).
        /// Kolon: 3. etriye varsa onun, yoksa 1. etriyenin altı. Perde: gövde yatayın altı.
        /// </summary>
        private void DrawKolonKesitCirozAcilim(
            Transaction tr, BlockTableRecord btr,
            Envelope e,
            List<(double stem, bool govde)> stems,
            int diaMm,
            int adet,
            bool isPerdeKesit,
            int floorIndex,
            int colNo,
            double storyZBot,
            double storyZTop,
            double originX,
            double cursorY,
            int nGovdeOverride = -1,
            double govdePerM2Override = double.NaN)
        {
            if (tr == null || btr == null || e == null || stems == null || stems.Count == 0)
                return;
            if (adet < 1) adet = 1;
            if (diaMm < 6) diaMm = 8;
            double hook135 = EtriyeAcilimKancaCm(diaMm);
            double hook90 = CirozAcilimHook90Cm(diaMm);
            double rad = KolonKesitCirozRadiusCm;
            const double txtH = 10.0;
            const double txtOff = 8.0;
            var db = btr.Database;
            double c45 = Math.Cos(Math.PI / 4.0);

            var groups = new List<(double stem, int nBaslik, int nGovdeSec)>();
            foreach (var item in stems)
            {
                double stem = Math.Round(item.stem);
                if (stem < 4.0) continue;
                int found = -1;
                for (int i = 0; i < groups.Count; i++)
                {
                    if (Math.Abs(groups[i].stem - stem) < 1.0) { found = i; break; }
                }
                if (found < 0)
                {
                    groups.Add((stem, item.govde ? 0 : 1, item.govde ? 1 : 0));
                }
                else
                {
                    var g0 = groups[found];
                    groups[found] = (g0.stem,
                        g0.nBaslik + (item.govde ? 0 : 1),
                        g0.nGovdeSec + (item.govde ? 1 : 0));
                }
            }
            if (groups.Count == 0) return;

            int nGovdeWall = 0;
            double govdePerM2 = PerdeGovdeCirozNormalPerM2;
            if (nGovdeOverride >= 0)
            {
                nGovdeWall = nGovdeOverride;
                if (!double.IsNaN(govdePerM2Override) && govdePerM2Override > 0.1)
                    govdePerM2 = govdePerM2Override;
            }
            else if (isPerdeKesit)
                nGovdeWall = CountPerdeGovdeCirozAdet(e, floorIndex, colNo, storyZBot, storyZTop, out govdePerM2);
            bool govdeAssigned = false;

            double x10 = originX;
            double yCursor = cursorY;
            for (int g = 0; g < groups.Count; g++)
            {
                double stem = groups[g].stem;
                int nSecB = groups[g].nBaslik;
                int nB = isPerdeKesit ? nSecB * adet : (nSecB + groups[g].nGovdeSec) * adet;
                int nG = 0;
                if (isPerdeKesit && groups[g].nGovdeSec > 0 && !govdeAssigned)
                {
                    nG = nGovdeWall;
                    govdeAssigned = true;
                }
                int nTag = nB + nG;
                if (!isPerdeKesit)
                    nTag = Math.Max(nSecB + groups[g].nGovdeSec, 1) * adet;
                if (nTag < 1) nTag = 1;
                double L = Math.Round(stem + hook90 + hook135);
                string tag = nSecB + groups[g].nGovdeSec > 1 && !isPerdeKesit
                    ? string.Format(CultureInfo.InvariantCulture, "{0}x{1}\u00F8{2} L={3:0}", nSecB + groups[g].nGovdeSec, adet, diaMm, L)
                    : string.Format(CultureInfo.InvariantCulture, "{0}\u00F8{1} L={2:0}", nTag, diaMm, L);

                double yStem0 = yCursor - 12.0;
                double yStem = yStem0 + (g == 1 ? KolonKesitCirozAcilimIkinciYukariCm : 0.0);
                double by = yStem - rad;
                double by0 = yStem0 - rad;
                double xL = x10 + txtOff;
                double xR = xL + stem;
                DrawKolonKesitCirozCHorizontal(tr, btr, xL, xR, by, rad, hook90, hook135,
                    bottomLeg: false, hook135OnRight: false);
                var p45 = new Point2d(xL - rad * c45, by - rad * c45);
                var tip135 = new Point2d(p45.X + hook135 * c45, p45.Y - hook135 * c45);

                DrawBeamLabel(tr, btr, db, new Point3d((xL + xR) * 0.5, yCursor + (g == 1 ? KolonKesitCirozAcilimIkinciYukariCm : 0.0), 0),
                    tag, txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                double yNoteBot = by0 - hook90;
                if (isPerdeKesit && (nB > 0 || nG > 0))
                {
                    double xNote = x10 - 15.0;
                    double yNote = by - hook90 - 8.0 - KolonKesitCirozAdetNotuAsagiCm;
                    if (nB > 0)
                    {
                        string noteB = string.Format(CultureInfo.InvariantCulture,
                            "(baslık {0} adet)", nB);
                        DrawBeamLabel(tr, btr, db, new Point3d(xNote, yNote, 0),
                            noteB, txtH, 0.0, LayerYazi, bottomLeftAligned: true);
                        yNote -= txtH + 4.0;
                    }
                    if (nG > 0)
                    {
                        string noteG = string.Format(CultureInfo.InvariantCulture,
                            "(govde {0} adet - m\u00B2'de {1:0} adet)", nG, govdePerM2);
                        DrawBeamLabel(tr, btr, db, new Point3d(xNote, yNote, 0),
                            noteG, txtH, 0.0, LayerYazi, bottomLeftAligned: true);
                        yNote -= txtH + 4.0;
                    }
                    yNoteBot = yNote;
                }
                DrawBeamLabel(tr, btr, db, new Point3d((xL + xR) * 0.5, yStem - 8.0, 0),
                    stem.ToString("0", CultureInfo.InvariantCulture),
                    txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                DrawBeamLabel(tr, btr, db, new Point3d(xR + rad + txtOff, by - hook90 * 0.5, 0),
                    hook90.ToString("0", CultureInfo.InvariantCulture),
                    txtH, Math.PI / 2.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
                DrawBeamLabel(tr, btr, db, new Point3d(x10 - 2.0, 0.5 * (p45.Y + tip135.Y), 0),
                    hook135.ToString("0", CultureInfo.InvariantCulture),
                    txtH, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);

                yCursor = Math.Min(
                    by0 - hook90 - KolonKesitEtriyeAcilimAltGapCm - 12.0,
                    yNoteBot - KolonKesitEtriyeAcilimAltGapCm);
            }
        }

        /// <summary>
        /// Bu katın gövde çirozu: alan = (ℓw−2ℓu)×kat yüksekliği (m²).
        /// Hcr ise 10 adet/m², değilse 4 adet/m².
        /// </summary>
        private int CountPerdeGovdeCirozAdet(
            Envelope e, int thisFloor, int colNo, double thisZBot, double thisZTop,
            out double perM2)
        {
            perM2 = PerdeGovdeCirozNormalPerM2;
            if (e == null) return 1;
            double lw = Math.Max(e.Width, e.Height);
            double bw = Math.Min(e.Width, e.Height);
            double hCm = thisZTop > thisZBot + 1.0 ? thisZTop - thisZBot : 280.0;
            double lu = ResolvePerdeUcBolgeLuCm(lw, bw, thisFloor, colNo);
            double govde = Math.Max(1.0, lw - 2.0 * lu);
            double aM2 = (govde / 100.0) * (hCm / 100.0);
            bool hcr = KolonDonatiTableDrawer.TryGetKolonBetonarmeHcr(
                _kolonDuseyGprHcr, _model?.Floors, thisFloor, colNo);
            perM2 = hcr ? PerdeGovdeCirozHcrPerM2 : PerdeGovdeCirozNormalPerM2;
            int n = (int)Math.Ceiling(perM2 * aM2 - 1e-9);
            return n < 1 ? 1 : n;
        }

        /// <summary>
        /// Poligon: her kolun çizilen başlıkları (ℓuLo/ℓuHi) çıkarılır; gövde alanları toplanır, bir kez tavan.
        /// Simetrik 2ℓu + kol başına tavan, birleşim başlığını gövdeye katıp 163 gibi fazla adet üretiyordu.
        /// </summary>
        private int CountPoligonGovdeCirozAdet(
            List<(Envelope env, bool longIsX, double luLo, double luHi, double ipLo, double ipHi)> koller,
            int thisFloor, int colNo, double thisZBot, double thisZTop,
            out double perM2)
        {
            perM2 = PerdeGovdeCirozNormalPerM2;
            bool hcr = KolonDonatiTableDrawer.TryGetKolonBetonarmeHcr(
                _kolonDuseyGprHcr, _model?.Floors, thisFloor, colNo);
            perM2 = hcr ? PerdeGovdeCirozHcrPerM2 : PerdeGovdeCirozNormalPerM2;
            if (koller == null || koller.Count == 0) return 1;
            double hCm = thisZTop > thisZBot + 1.0 ? thisZTop - thisZBot : 280.0;
            double aM2 = 0.0;
            for (int i = 0; i < koller.Count; i++)
            {
                var k = koller[i];
                if (k.env == null) continue;
                double lw = Math.Max(k.env.Width, k.env.Height);
                double bw = Math.Min(k.env.Width, k.env.Height);
                double lu = ResolvePerdeUcBolgeLuCm(lw, bw, thisFloor, colNo);
                double gLo = k.luLo >= 8.0 ? k.luLo : lu;
                double gHi = k.luHi >= 8.0 ? k.luHi : lu;
                double govde = Math.Max(1.0, lw - gLo - gHi);
                aM2 += (govde / 100.0) * (hCm / 100.0);
            }
            int n = (int)Math.Ceiling(perM2 * aM2 - 1e-9);
            return n < 1 ? 1 : n;
        }

        /// <summary>
        /// Perde kesit altı gövde yatay açılımı: iki yüz (2x), kenardan 7 cm.
        /// L = çizilen gövde boyu; uç gömmesi &lt; ℓb ise + 2×12φ gönye.
        /// Dönen değer: açılımın alt Y’si (çizilmezse yAbove).
        /// </summary>
        private double DrawPerdeKesitGovdeYatayAcilim(
            Transaction tr, BlockTableRecord btr,
            Envelope e, int floorIndex, int colNo,
            double storyZBot, double storyZTop, double yAbove)
        {
            if (tr == null || btr == null || e == null) return yAbove;
            double lw = Math.Max(e.Width, e.Height);
            double bw = Math.Min(e.Width, e.Height);
            if (bw < 8.0 || lw < 6.0 * bw - 0.01) return yAbove;
            double kenar = PerdeKesitYatayKenarCm;
            double stem = lw - 2.0 * kenar;
            if (stem < 8.0) return yAbove;

            int diaMm = ResolveKolonDuseyEtriyeDiaMm(floorIndex, colNo);
            if (diaMm < 6) diaMm = 8;
            string etRaw = null;
            if (_kolonDuseyGpr != null && _model?.Floors != null)
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(
                    _kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out _, out etRaw);
            ParsePerdeEtriyeAralikCm(etRaw, out double sGovde, out _);
            int sGvCm = Math.Max(5, (int)Math.Round(sGovde));
            double hStory = storyZTop > storyZBot + 1.0 ? storyZTop - storyZBot : 280.0;
            if (!TryKolonEtriyeAdetAralik(hStory, sGvCm, out int adet, out _) || adet < 1)
                adet = 1;

            double lu = ResolvePerdeUcBolgeLuCm(lw, bw, floorIndex, colNo);
            double embedUc = lu - kenar;
            double fck = _rebarFckMPa > 16.0 ? _rebarFckMPa : 30.0;
            double fyk = _rebarFykMPa > 200.0 ? _rebarFykMPa : 420.0;
            double lb = Ts500KenetlenmeLbCm(diaMm, fck, fyk);
            bool gonye12 = embedUc + 0.01 < lb;
            double hk = 0.0;
            if (gonye12)
            {
                hk = CeilTo5Cm(12.0 * diaMm / 10.0);
                if (hk > bw * 0.45) hk = Math.Max(2.0, bw * 0.45);
            }
            double L = Math.Round(stem + 2.0 * hk);

            double cx = 0.5 * (e.MinX + e.MaxX);
            double x0 = cx - stem * 0.5;
            double x1 = cx + stem * 0.5;
            double yMid = yAbove - PerdeKesitGovdeYatayAcilimAraCm;
            double yHi = yMid + PerdeKesitGovdeYatayCiftAralikCm * 0.5;
            double yLo = yMid - PerdeKesitGovdeYatayCiftAralikCm * 0.5;
            if (gonye12 && hk >= 1.0)
            {
                double stg = PerdeKesitGovdeYataySasirtmaCm;
                AppendGonyeliDonatiBar(tr, btr, true, x0 - 0.5 * stg, x1 - 0.5 * stg, yHi, true, true, yLo - yHi, writeHookLabel: true);
                AppendGonyeliDonatiBar(tr, btr, true, x0 + 0.5 * stg, x1 + 0.5 * stg, yLo, true, true, yHi - yLo, writeHookLabel: false);
            }
            else
            {
                AppendDonatiPline(tr, btr, new[]
                {
                    new Point2d(x0, yHi),
                    new Point2d(x1, yHi)
                }, null, LayerDonatiGovde);
                AppendDonatiPline(tr, btr, new[]
                {
                    new Point2d(x0, yLo),
                    new Point2d(x1, yLo)
                }, null, LayerDonatiGovde);
            }

            string tag = string.Format(CultureInfo.InvariantCulture,
                "2x{0}\u00F8{1} L={2:0}", adet, diaMm, L);
            DrawBeamLabel(tr, btr, btr.Database, new Point3d(cx, yMid, 0),
                tag, 10.0, 0.0, LayerDonatiYazisiPerde, useMiddleCenter: true);
            return yLo;
        }

        private void SumKolonKesitEtriyeAdetAralik(
            List<(double zLo, double zHi, int sCm, int diaMm)> bolgeler,
            IEnumerable<double> etriyeZs,
            double zBot, double zTop,
            int floorIndex, int colNo, Envelope sectionE,
            out int adet, out int sCm, out int diaMm)
        {
            adet = 0;
            sCm = 10;
            diaMm = 8;
            if (etriyeZs != null && bolgeler != null && zTop > zBot + 1.0)
            {
                adet = SumEtriyeAdetFromGorunusEtiketleri(etriyeZs, bolgeler, zBot, zTop, out sCm, out diaMm);
                if (adet > 0) return;
            }
            int sMin = int.MaxValue;
            if (bolgeler != null && zTop > zBot + 1.0)
            {
                foreach (var b in bolgeler)
                {
                    double lo = Math.Max(zBot, b.zLo);
                    double hi = Math.Min(zTop, b.zHi);
                    if (hi - lo < 1.0) continue;
                    if (TryKolonEtriyeAdetAralik(hi - lo, b.sCm, out int n, out _))
                        adet += n;
                    if (b.sCm < sMin)
                    {
                        sMin = b.sCm;
                        sCm = b.sCm;
                    }
                    if (b.diaMm >= 6) diaMm = b.diaMm;
                }
            }
            if (adet > 0 && sMin < int.MaxValue) return;

            string etRaw = null;
            if (_kolonDuseyGpr != null && _model?.Floors != null)
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(
                    _kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out _, out etRaw);
            ParseKolonEtriyeAralikCm(etRaw, out double sMid, out double sConf, out _);
            TryParseEtriyeDiaMm(etRaw, out diaMm);
            sCm = Math.Max(5, (int)Math.Round(Math.Min(sMid, sConf)));
            double hStory = zTop > zBot + 1.0 ? zTop - zBot
                : (sectionE != null ? Math.Max(sectionE.Width, sectionE.Height) : 280.0);
            if (!TryKolonEtriyeAdetAralik(hStory, sCm, out adet, out _) || adet < 1)
                adet = 1;
        }

        private void AppendKolonKesitEtriyeKapali(
            Transaction tr, BlockTableRecord btr,
            double x0, double y0, double x1, double y1, double rad, string layer)
        {
            if (tr == null || btr == null) return;
            if (x1 - x0 < 2.0 * rad + 2.0 || y1 - y0 < 2.0 * rad + 2.0) return;
            double b90 = Math.Tan(Math.PI / 8.0);
            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            pl.Layer = string.IsNullOrEmpty(layer) ? LayerKesitGorunus : layer;
            pl.LineWeight = LineWeight.LineWeight020;
            pl.Closed = true;
            pl.AddVertexAt(0, new Point2d(x0 + rad, y0), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x1 - rad, y0), b90, 0, 0);
            pl.AddVertexAt(2, new Point2d(x1, y0 + rad), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x1, y1 - rad), b90, 0, 0);
            pl.AddVertexAt(4, new Point2d(x1 - rad, y1), 0, 0, 0);
            pl.AddVertexAt(5, new Point2d(x0 + rad, y1), b90, 0, 0);
            pl.AddVertexAt(6, new Point2d(x0, y1 - rad), 0, 0, 0);
            pl.AddVertexAt(7, new Point2d(x0, y0 + rad), b90, 0, 0);
            AppendEntity(tr, btr, pl);
        }

        /// <summary>
        /// Aynı ebatlı üst üste kolonlarda alt kat düşey donatısı (adet / As)
        /// üst kattakinden az olamaz; GPR alt katı daha az yazsa bile arttırılır.
        /// </summary>
        private bool TryResolveKolonAltKatDuseyDonati(
            int floorIndex, int colNo,
            out int n, out int diaMm, out string gprOzet, out string drawOzet)
        {
            n = 0;
            diaMm = 14;
            gprOzet = null;
            drawOzet = null;
            if (_kolonDuseyGpr == null || _model?.Floors == null) return false;
            if (!KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(
                    _kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out string donati, out _) ||
                string.IsNullOrWhiteSpace(donati))
                return false;
            if (!KolonDonatiTableDrawer.TryParseKolonKesitDuseyDonatiByDia(donati, out var byDia) ||
                byDia == null || byDia.Count == 0)
                return false;

            gprOzet = KolonDonatiTableDrawer.FormatKolonKesitDuseyDonatiOzet(donati);
            n = SumDonatiAdet(byDia);
            diaMm = byDia.Keys.First();
            if (diaMm < 6) diaMm = 14;

            var col = _model.Columns?.FirstOrDefault(c => c.ColumnNo == colNo);
            for (int fiUp = floorIndex + 1; fiUp < _model.Floors.Count; fiUp++)
            {
                if (col != null && !HasColumnOnFloor(_model.Floors[fiUp], col)) continue;
                if (!KolonKatEbatAyni(floorIndex, fiUp, colNo)) break;
                if (!KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(
                        _kolonDuseyGpr, _model.Floors, fiUp, colNo, out _, out string upDon, out _) ||
                    string.IsNullOrWhiteSpace(upDon) ||
                    !KolonDonatiTableDrawer.TryParseKolonKesitDuseyDonatiByDia(upDon, out var upByDia) ||
                    upByDia == null || upByDia.Count == 0)
                    continue;

                double asNeed = SumDonatiAsMm2(upByDia);
                double asThis = SumDonatiAsMm2(byDia);
                int nNeed = SumDonatiAdet(upByDia);
                int nThis = SumDonatiAdet(byDia);
                if (asNeed <= asThis + 0.5 && nNeed <= nThis) continue;

                int addDia = byDia.Keys.First();
                if (addDia < 6) addDia = diaMm;
                if (asNeed > asThis + 0.5)
                {
                    int add = (int)Math.Ceiling((asNeed - asThis) / (addDia * addDia) - 1e-9);
                    if (add < 1) add = 1;
                    byDia[addDia] = (byDia.ContainsKey(addDia) ? byDia[addDia] : 0) + add;
                }
                nThis = SumDonatiAdet(byDia);
                if (nNeed > nThis)
                    byDia[addDia] = (byDia.ContainsKey(addDia) ? byDia[addDia] : 0) + (nNeed - nThis);
            }

            n = SumDonatiAdet(byDia);
            if (byDia.Count > 0) diaMm = byDia.Keys.First();
            drawOzet = FormatKolonDuseyByDia(byDia);
            if (string.IsNullOrWhiteSpace(drawOzet)) drawOzet = gprOzet;
            return n > 0;
        }

        private bool KolonKatEbatAyni(int fiA, int fiB, int colNo)
        {
            if (_model?.Floors == null || _model.Columns == null) return false;
            if (fiA < 0 || fiB < 0 || fiA >= _model.Floors.Count || fiB >= _model.Floors.Count) return false;
            var col = _model.Columns.FirstOrDefault(c => c.ColumnNo == colNo);
            if (col == null) return false;
            var fa = _model.Floors[fiA];
            var fb = _model.Floors[fiB];
            if (!HasColumnOnFloor(fa, col) || !HasColumnOnFloor(fb, col)) return false;

            var da = GetColumnDimensionsForFloor(fa);
            var db = GetColumnDimensionsForFloor(fb);
            if (da != null && db != null &&
                da.TryGetValue(colNo, out var a) && db.TryGetValue(colNo, out var b))
            {
                if (a.columnType == 2 && b.columnType == 2 && a.W > 0.5 && b.W > 0.5)
                    return Math.Abs(a.W - b.W) < 1.0;
                if (a.columnType != 3 && b.columnType != 3 && a.columnType != 2 && b.columnType != 2 &&
                    a.W > 0.5 && a.H > 0.5 && b.W > 0.5 && b.H > 0.5)
                {
                    double aMin = Math.Min(a.W, a.H), aMax = Math.Max(a.W, a.H);
                    double bMin = Math.Min(b.W, b.H), bMax = Math.Max(b.W, b.H);
                    return Math.Abs(aMin - bMin) < 1.0 && Math.Abs(aMax - bMax) < 1.0;
                }
                if (a.columnType == 3 && b.columnType == 3 && _ntsDrawFactory != null)
                {
                    var pa = GetColumnPolygonForTable(fa, col, 0, 0, _ntsDrawFactory);
                    var pb = GetColumnPolygonForTable(fb, col, 0, 0, _ntsDrawFactory);
                    if (pa != null && pb != null && !pa.IsEmpty && !pb.IsEmpty)
                        return !KolonKesitPlanFarkli(pa, pb, 0);
                }
            }

            string ea = null, eb = null;
            KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(
                _kolonDuseyGpr, _model.Floors, fiA, colNo, out ea, out _, out _);
            KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(
                _kolonDuseyGpr, _model.Floors, fiB, colNo, out eb, out _, out _);
            return SameKolonGprEbat(ea, eb);
        }

        private static bool SameKolonGprEbat(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            if (TryParseKolonGprEbatCm(a, out double wa, out double ha) &&
                TryParseKolonGprEbatCm(b, out double wb, out double hb))
            {
                return (Math.Abs(wa - wb) < 1.0 && Math.Abs(ha - hb) < 1.0) ||
                       (Math.Abs(wa - hb) < 1.0 && Math.Abs(ha - wb) < 1.0);
            }
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseKolonGprEbatCm(string raw, out double w, out double h)
        {
            w = h = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var m = Regex.Match(raw.Trim(), @"(\d+(?:[.,]\d+)?)\s*[x×X/]\s*(\d+(?:[.,]\d+)?)");
            if (!m.Success) return false;
            if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out w))
                return false;
            return double.TryParse(m.Groups[2].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out h);
        }

        private static int SumDonatiAdet(SortedDictionary<int, int> byDia)
        {
            if (byDia == null) return 0;
            int n = 0;
            foreach (var kv in byDia) n += kv.Value;
            return n;
        }

        private static double SumDonatiAsMm2(SortedDictionary<int, int> byDia)
        {
            if (byDia == null) return 0;
            double a = 0;
            foreach (var kv in byDia) a += kv.Value * (double)kv.Key * kv.Key;
            return a;
        }

        private static string FormatKolonDuseyByDia(SortedDictionary<int, int> byDia)
        {
            if (byDia == null || byDia.Count == 0) return null;
            var sb = new StringBuilder();
            foreach (var kv in byDia)
            {
                if (kv.Value <= 0) continue;
                if (sb.Length > 0) sb.Append('+');
                sb.Append(kv.Value.ToString(CultureInfo.InvariantCulture));
                sb.Append('\u00F8');
                sb.Append(kv.Key.ToString(CultureInfo.InvariantCulture));
            }
            return sb.Length == 0 ? null : sb.ToString();
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
            if (TryResolveKolonAltKatDuseyDonati(floorIndex, colNo, out int resolvedN, out int resolvedDia, out _, out _))
            {
                if (resolvedN > 0) n = resolvedN;
                if (resolvedDia >= 6) diaMm = resolvedDia;
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

        /// <summary>Kesit üstü: SB-08 (80/35). Kolon = KOLON ISMI, perde/başlık = PERDE ISMI.</summary>
        private void DrawKolonKesitKatBoyutEtiket(
            Transaction tr, BlockTableRecord btr, Envelope e, int floorIndex, int colNo, bool isPerde,
            double extraClearCm = 0.0)
        {
            if (tr == null || btr == null || e == null) return;
            string text = KolonDonatiTableDrawer.FormatKolonPerdeKesitEtiket(
                _model?.Floors, floorIndex, colNo, e.Width, e.Height);
            if (string.IsNullOrWhiteSpace(text)) return;
            const double h = 12.0;
            double clear = isPerde
                ? 10.0 + PerdeKesitDuseyEtiketYukariCm + 8.0 + 8.0
                : 3.0 + 10.0 + 8.0;
            clear += extraClearCm;
            double x = 0.5 * (e.MinX + e.MaxX);
            double y = e.MaxY + clear + h * 0.5;
            string layer = isPerde ? LayerPerdeYazisi : LayerKolonIsmi;
            DrawBeamLabel(tr, btr, btr.Database, new Point3d(x, y, 0),
                text, h, 0.0, layer, useMiddleCenter: true);
        }

        private void DrawKolonKesitDuseyDonatiYazisi(
            Transaction tr,
            BlockTableRecord btr,
            Envelope e,
            int floorIndex,
            int colNo,
            bool isPerdeBasligi = false,
            bool isPerdeKesit = false)
        {
            if (tr == null || btr == null || e == null || _model?.Floors == null) return;
            if (isPerdeKesit) return;
            string donati = null;
            bool hasCell = _kolonDuseyGpr != null &&
                KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(_kolonDuseyGpr, _model.Floors, floorIndex, colNo, out _, out donati, out _);
            if (!hasCell) return;
            const double h = 10.0;
            double y = e.MaxY + 3.0;
            string donText = null;
            if (TryResolveKolonAltKatDuseyDonati(floorIndex, colNo, out _, out _, out _, out string drawOzet) &&
                !string.IsNullOrWhiteSpace(drawOzet))
                donText = drawOzet;
            else
                donText = KolonDonatiTableDrawer.FormatKolonKesitDuseyDonatiOzet(donati);
            if (!string.IsNullOrWhiteSpace(donText))
            {
                DrawBeamLabel(tr, btr, btr.Database, new Point3d(e.MinX, y, 0),
                    donText, h, 0.0, LayerDonatiYazisiPerde, bottomLeftAligned: true);
            }
        }

        /// <summary>Poligon kesit: isim, toplam demirin hemen üstünde; ikisi takımın 20 cm üstünde.</summary>
        private void DrawPoligonKolonKesitIsimVeDonati(
            Transaction tr, BlockTableRecord btr, Envelope e,
            int floorIndex, int colNo,
            int nUc, int nGovde, int ucDia, int gvDia,
            double takimMaxY)
        {
            if (tr == null || btr == null || e == null) return;
            if (ucDia < 6) ucDia = 14;
            if (gvDia < 6) gvDia = 12;
            var sb = new StringBuilder();
            if (nUc > 0)
            {
                sb.Append(nUc.ToString(CultureInfo.InvariantCulture));
                sb.Append('\u00F8');
                sb.Append(ucDia.ToString(CultureInfo.InvariantCulture));
            }
            if (nGovde > 0)
            {
                if (sb.Length > 0) sb.Append('+');
                sb.Append(nGovde.ToString(CultureInfo.InvariantCulture));
                sb.Append('\u00F8');
                sb.Append(gvDia.ToString(CultureInfo.InvariantCulture));
            }
            string donText = sb.Length > 0
                ? KolonDonatiTableDrawer.NormalizeDiameterSymbol(sb.ToString())
                : null;
            int etiketNo = colNo;
            if (_kolonDuseyGrupColNos != null && _kolonDuseyGrupColNos.Length > 0)
            {
                etiketNo = _kolonDuseyGrupColNos[0];
                for (int i = 1; i < _kolonDuseyGrupColNos.Length; i++)
                {
                    if (_kolonDuseyGrupColNos[i] < etiketNo)
                        etiketNo = _kolonDuseyGrupColNos[i];
                }
            }
            string nameText = KolonDonatiTableDrawer.FormatPoligonKolonKesitEtiket(
                _model?.Floors, floorIndex, new[] { etiketNo });
            const double hDon = 10.0;
            const double hName = 12.0;
            const double ara = 2.0;
            double x = e.MinX;
            double yDon = takimMaxY + PoligonKesitTakimUstEtiketBoslukCm;
            if (!string.IsNullOrWhiteSpace(donText))
            {
                DrawBeamLabel(tr, btr, btr.Database, new Point3d(x, yDon, 0),
                    donText, hDon, 0.0, LayerDonatiYazisiPerde, bottomLeftAligned: true);
            }
            if (string.IsNullOrWhiteSpace(nameText)) return;
            double yName = yDon + (!string.IsNullOrWhiteSpace(donText) ? hDon + ara : 0.0) + 5.0;
            DrawBeamLabel(tr, btr, btr.Database, new Point3d(x, yName, 0),
                nameText, hName, 0.0, LayerKolonIsmi, bottomLeftAligned: true);
        }

        /// <summary>
        /// Poligon kolon: dışarı 40 cm detay + 58 cm toplam. İçeride 15 cm etriye stili
        /// (poligon çizgisi + kesit görünüş).
        /// </summary>
        private void DrawPoligonKolonKesitOlculeri(
            Transaction tr, BlockTableRecord btr, Envelope overall, Geometry g,
            List<(Envelope e, bool longIsX, double luLo, double luHi, double ipLo, double ipHi)> koller,
            List<(double x0, double y0, double x1, double y1)> rects = null)
        {
            if (tr == null || btr == null) return;
            ObjectId dimPlan = GetOrCreatePlanOlcuDimStyle(tr, btr.Database, 10.0, 1.0, PlanOlcuDonatiDimStyleName);
            ObjectId dimEt = GetOrCreateEtriyeOlcuDimStyle(tr, btr.Database, 10.0);
            double offDetay = PoligonKolonDisOlcuDetayCm;
            double offToplam = PoligonKolonDisOlcuToplamCm;
            double offIc = PoligonKolonIcEtriyeOlcuCm;
            var poly = TryKolonKesitAsPolygon(g);
            if (poly == null || poly.IsEmpty || poly.ExteriorRing == null)
                return;
            var gf = poly.Factory ?? _ntsDrawFactory;
            double cx = overall != null ? 0.5 * (overall.MinX + overall.MaxX) : poly.Centroid.X;
            double cy = overall != null ? 0.5 * (overall.MinY + overall.MaxY) : poly.Centroid.Y;

            void Dim(ObjectId style, Point3d a, Point3d b, Point3d linePt, double fxlen)
            {
                var dim = new AlignedDimension(a, b, linePt, "", style)
                {
                    Layer = LayerOlcu,
                    LineWeight = LineWeight.LineWeight020
                };
                try { dim.DimfxlenOn = true; } catch { }
                try { dim.Dimfxlen = fxlen; } catch { }
                try { dim.Dimrnd = 0.1; } catch { }
                try { dim.Dimdec = 1; } catch { }
                try { dim.Dimtdec = 1; } catch { }
                try { dim.Dimzin = 8; } catch { }
                AppendEntity(tr, btr, dim);
            }

            bool Near(double a, double b) => Math.Abs(a - b) < 1.0;
            void AddSt(List<double> st, double v)
            {
                if (st == null) return;
                for (int i = 0; i < st.Count; i++)
                {
                    if (Math.Abs(st[i] - v) < 1.0) return;
                }
                st.Add(v);
            }

            var chains = new List<(bool horiz, double face, double sign, bool silhoutte, List<double> st)>();
            int FindChain(bool horiz, double face, double sign, bool sil)
            {
                for (int i = 0; i < chains.Count; i++)
                {
                    if (chains[i].horiz == horiz && chains[i].silhoutte == sil &&
                        Math.Abs(chains[i].sign - sign) < 0.5 && Near(chains[i].face, face))
                        return i;
                }
                return -1;
            }
            void AddEdge(bool horiz, double face, double a, double b, double sign, bool sil)
            {
                if (b < a) { double t = a; a = b; b = t; }
                if (b - a < 2.0) return;
                int ix = FindChain(horiz, face, sign, sil);
                if (ix < 0)
                {
                    var st = new List<double>();
                    AddSt(st, a);
                    AddSt(st, b);
                    chains.Add((horiz, face, sign, sil, st));
                }
                else
                {
                    AddSt(chains[ix].st, a);
                    AddSt(chains[ix].st, b);
                }
            }

            var ring = poly.ExteriorRing.Coordinates;
            if (ring == null || ring.Length < 4) return;
            for (int i = 0; i < ring.Length - 1; i++)
            {
                if (ring[i] == null || ring[i + 1] == null) continue;
                double x0 = ring[i].X, y0 = ring[i].Y, x1 = ring[i + 1].X, y1 = ring[i + 1].Y;
                double dx = x1 - x0, dy = y1 - y0;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 2.0) continue;
                bool horiz = Math.Abs(dy) <= 1.0 && Math.Abs(dx) >= 2.0;
                bool vert = Math.Abs(dx) <= 1.0 && Math.Abs(dy) >= 2.0;
                if (!horiz && !vert) continue;
                double mx = 0.5 * (x0 + x1), my = 0.5 * (y0 + y1);
                double inx = -dy / len, iny = dx / len;
                bool leftInside = false;
                if (gf != null)
                {
                    try
                    {
                        leftInside = poly.Covers(gf.CreatePoint(new Coordinate(mx + inx * 0.8, my + iny * 0.8)));
                    }
                    catch { leftInside = false; }
                }
                double extX = leftInside ? -inx : inx;
                double extY = leftInside ? -iny : iny;
                double silDot = extX * (mx - cx) + extY * (my - cy);
                bool sil = silDot >= 0.0;
                if (horiz)
                {
                    double sign = extY >= 0 ? 1.0 : -1.0;
                    AddEdge(true, 0.5 * (y0 + y1), x0, x1, sign, sil);
                }
                else
                {
                    double sign = extX >= 0 ? 1.0 : -1.0;
                    AddEdge(false, 0.5 * (x0 + x1), y0, y1, sign, sil);
                }
            }

            for (int i = 0; i < ring.Length; i++)
            {
                if (ring[i] == null) continue;
                double vx = ring[i].X, vy = ring[i].Y;
                for (int c = 0; c < chains.Count; c++)
                {
                    var ch = chains[c];
                    if (ch.horiz)
                    {
                        if (Near(vy, ch.face)) AddSt(ch.st, vx);
                    }
                    else if (Near(vx, ch.face))
                        AddSt(ch.st, vy);
                }
            }

            if (koller != null)
            {
                for (int i = 0; i < koller.Count; i++)
                {
                    var k = koller[i];
                    var ke = k.e;
                    if (ke == null) continue;
                    double luA = k.longIsX ? ke.MinX + k.luLo : ke.MinY + k.luLo;
                    double luB = k.longIsX ? ke.MaxX - k.luHi : ke.MaxY - k.luHi;
                    for (int c = 0; c < chains.Count; c++)
                    {
                        var ch = chains[c];
                        if (ch.silhoutte) continue;
                        if (k.longIsX && ch.horiz)
                        {
                            if (ch.face < ke.MinY - 1.0 || ch.face > ke.MaxY + 1.0) continue;
                            AddSt(ch.st, luA);
                            AddSt(ch.st, luB);
                        }
                        else if (!k.longIsX && !ch.horiz)
                        {
                            if (ch.face < ke.MinX - 1.0 || ch.face > ke.MaxX + 1.0) continue;
                            AddSt(ch.st, luA);
                            AddSt(ch.st, luB);
                        }
                    }
                }
            }

            for (int c = 0; c < chains.Count; c++)
            {
                var ch = chains[c];
                if (ch.silhoutte) continue;
                if (ch.st == null || ch.st.Count < 2) continue;
                ch.st.Sort();
                for (int i = 0; i < ch.st.Count - 1; i++)
                {
                    double a = ch.st[i], b = ch.st[i + 1];
                    if (b - a < 2.0) continue;
                    if (ch.horiz)
                    {
                        double y = ch.face;
                        Dim(dimEt, new Point3d(a, y, 0), new Point3d(b, y, 0),
                            new Point3d(0.5 * (a + b), y + ch.sign * offIc, 0), offIc);
                    }
                    else
                    {
                        double x = ch.face;
                        Dim(dimEt, new Point3d(x, a, 0), new Point3d(x, b, 0),
                            new Point3d(x + ch.sign * offIc, 0.5 * (a + b), 0), offIc);
                    }
                }
            }

            var botXs = new List<double>();
            var topXs = new List<double>();
            var leftYs = new List<double>();
            var rightYs = new List<double>();
            double envMinX = overall != null ? overall.MinX : poly.EnvelopeInternal.MinX;
            double envMaxX = overall != null ? overall.MaxX : poly.EnvelopeInternal.MaxX;
            double envMinY = overall != null ? overall.MinY : poly.EnvelopeInternal.MinY;
            double envMaxY = overall != null ? overall.MaxY : poly.EnvelopeInternal.MaxY;
            AddSt(topXs, envMinX);
            AddSt(topXs, envMaxX);
            AddSt(botXs, envMinX);
            AddSt(botXs, envMaxX);
            AddSt(leftYs, envMinY);
            AddSt(leftYs, envMaxY);
            AddSt(rightYs, envMinY);
            AddSt(rightYs, envMaxY);
            for (int c = 0; c < chains.Count; c++)
            {
                var ch = chains[c];
                if (!ch.silhoutte || ch.st == null) continue;
                if (ch.horiz)
                {
                    var dest = ch.sign < 0 ? botXs : topXs;
                    for (int s = 0; s < ch.st.Count; s++)
                        AddSt(dest, ch.st[s]);
                }
                else
                {
                    var dest = ch.sign < 0 ? leftYs : rightYs;
                    for (int s = 0; s < ch.st.Count; s++)
                        AddSt(dest, ch.st[s]);
                }
            }
            if (koller != null)
            {
                for (int i = 0; i < koller.Count; i++)
                {
                    var ke = koller[i].e;
                    if (ke == null) continue;
                    if (Near(ke.MinY, envMinY))
                    {
                        AddSt(botXs, ke.MinX);
                        AddSt(botXs, ke.MaxX);
                        AddSt(leftYs, ke.MinY);
                        AddSt(leftYs, ke.MaxY);
                        AddSt(rightYs, ke.MinY);
                        AddSt(rightYs, ke.MaxY);
                    }
                    if (Near(ke.MaxY, envMaxY))
                    {
                        AddSt(topXs, ke.MinX);
                        AddSt(topXs, ke.MaxX);
                        AddSt(leftYs, ke.MinY);
                        AddSt(leftYs, ke.MaxY);
                        AddSt(rightYs, ke.MinY);
                        AddSt(rightYs, ke.MaxY);
                    }
                    if (Near(ke.MinX, envMinX))
                    {
                        AddSt(leftYs, ke.MinY);
                        AddSt(leftYs, ke.MaxY);
                    }
                    if (Near(ke.MaxX, envMaxX))
                    {
                        AddSt(rightYs, ke.MinY);
                        AddSt(rightYs, ke.MaxY);
                    }
                }
            }
            if (rects != null)
            {
                for (int i = 0; i < rects.Count; i++)
                {
                    var r = rects[i];
                    double rx0 = Math.Min(r.x0, r.x1), rx1 = Math.Max(r.x0, r.x1);
                    double ry0 = Math.Min(r.y0, r.y1), ry1 = Math.Max(r.y0, r.y1);
                    if (rx1 - rx0 < 4.0 || ry1 - ry0 < 4.0) continue;
                    if (Near(ry1, envMaxY) || Near(ry0, envMaxY))
                    {
                        AddSt(topXs, rx0);
                        AddSt(topXs, rx1);
                    }
                    if (Near(ry0, envMinY) || Near(ry1, envMinY))
                    {
                        AddSt(botXs, rx0);
                        AddSt(botXs, rx1);
                    }
                    if (Near(rx0, envMinX) || Near(rx1, envMinX))
                    {
                        AddSt(leftYs, ry0);
                        AddSt(leftYs, ry1);
                    }
                    if (Near(rx1, envMaxX) || Near(rx0, envMaxX))
                    {
                        AddSt(rightYs, ry0);
                        AddSt(rightYs, ry1);
                    }
                }
            }
            for (int i = 0; i < ring.Length; i++)
            {
                if (ring[i] == null) continue;
                double vx = ring[i].X, vy = ring[i].Y;
                if (Near(vy, envMaxY)) AddSt(topXs, vx);
                if (Near(vy, envMinY)) AddSt(botXs, vx);
                if (Near(vx, envMinX)) AddSt(leftYs, vy);
                if (Near(vx, envMaxX)) AddSt(rightYs, vy);
            }

            void DrawDisCiftSira(bool horiz, double face, double sign, List<double> st, double dDetay, double dToplam)
            {
                if (st == null || st.Count < 2) return;
                if (dDetay < 8.0) dDetay = 8.0;
                if (dToplam < dDetay + 8.0) dToplam = dDetay + 8.0;
                st.Sort();
                for (int i = 0; i < st.Count - 1; i++)
                {
                    double a = st[i], b = st[i + 1];
                    if (b - a < 2.0) continue;
                    if (horiz)
                        Dim(dimPlan, new Point3d(a, face, 0), new Point3d(b, face, 0),
                            new Point3d(0.5 * (a + b), face + sign * dDetay, 0), dDetay);
                    else
                        Dim(dimPlan, new Point3d(face, a, 0), new Point3d(face, b, 0),
                            new Point3d(face + sign * dDetay, 0.5 * (a + b), 0), dDetay);
                }
                if (st.Count < 3) return;
                double a0 = st[0], b0 = st[st.Count - 1];
                if (b0 - a0 < 4.0) return;
                if (horiz)
                    Dim(dimPlan, new Point3d(a0, face, 0), new Point3d(b0, face, 0),
                        new Point3d(0.5 * (a0 + b0), face + sign * dToplam, 0), dToplam);
                else
                    Dim(dimPlan, new Point3d(face, a0, 0), new Point3d(face, b0, 0),
                        new Point3d(face + sign * dToplam, 0.5 * (a0 + b0), 0), dToplam);
            }

            DrawDisCiftSira(true, envMinY, -1.0, botXs, offDetay - 5.0, offToplam - 5.0);
            DrawDisCiftSira(true, envMaxY, 1.0, topXs, offDetay - 20.0, offToplam - 20.0);
            DrawDisCiftSira(false, envMinX, -1.0, leftYs, offDetay - 20.0, offToplam - 20.0);
            DrawDisCiftSira(false, envMaxX, 1.0, rightYs, offDetay - 5.0, offToplam - 5.0);
        }

        /// <summary>
        /// Uç etiket kutuları: ℓu + örtüşen kolon-format (L bacak 25×100 dahil) + birleşen kol ℓu.
        /// Adet AABB değil birleşim üyeliği ile sayılır (boş L oyuğu şişirmez).
        /// </summary>
        private static bool ExpandPoligonUcEtiketKutu(
            ref double x0, ref double y0, ref double x1, ref double y1,
            List<(Envelope e, bool longIsX, double luLo, double luHi, double ipLo, double ipHi)> koller,
            List<(double x0, double y0, double x1, double y1)> rects,
            List<(double x0, double y0, double x1, double y1)> boxes = null)
        {
            double cx0 = x0, cy0 = y0, cx1 = x1, cy1 = y1;
            if (cx1 < cx0) { double t = cx0; cx0 = cx1; cx1 = t; }
            if (cy1 < cy0) { double t = cy0; cy0 = cy1; cy1 = t; }
            void AddBox(double ax0, double ay0, double ax1, double ay1)
            {
                if (ax1 < ax0) { double t = ax0; ax0 = ax1; ax1 = t; }
                if (ay1 < ay0) { double t = ay0; ay0 = ay1; ay1 = t; }
                if (boxes == null) return;
                for (int b = 0; b < boxes.Count; b++)
                {
                    var o = boxes[b];
                    if (Math.Abs(o.x0 - ax0) < 0.5 && Math.Abs(o.y0 - ay0) < 0.5 &&
                        Math.Abs(o.x1 - ax1) < 0.5 && Math.Abs(o.y1 - ay1) < 0.5)
                        return;
                }
                boxes.Add((ax0, ay0, ax1, ay1));
            }
            AddBox(cx0, cy0, cx1, cy1);
            bool joinsKolon = false;

            if (rects != null)
            {
                for (int i = 0; i < rects.Count; i++)
                {
                    var r = rects[i];
                    double rx0 = Math.Min(r.x0, r.x1), rx1 = Math.Max(r.x0, r.x1);
                    double ry0 = Math.Min(r.y0, r.y1), ry1 = Math.Max(r.y0, r.y1);
                    double w = rx1 - rx0, h = ry1 - ry0;
                    if (w < 8.0 || h < 8.0) continue;
                    if (IsDepremPerdeBoyOrani(Math.Max(w, h), Math.Min(w, h))) continue;
                    double ox0 = Math.Max(cx0, rx0), ox1 = Math.Min(cx1, rx1);
                    double oy0 = Math.Max(cy0, ry0), oy1 = Math.Min(cy1, ry1);
                    bool overlap = ox1 - ox0 >= 4.0 && oy1 - oy0 >= 4.0;
                    bool touchX = ox1 - ox0 >= 8.0
                        && (Math.Abs(ry1 - cy0) <= 1.5 || Math.Abs(ry0 - cy1) <= 1.5);
                    bool touchY = oy1 - oy0 >= 8.0
                        && (Math.Abs(rx1 - cx0) <= 1.5 || Math.Abs(rx0 - cx1) <= 1.5);
                    if (!overlap && !touchX && !touchY) continue;
                    joinsKolon = true;
                    AddBox(rx0, ry0, rx1, ry1);
                    cx0 = Math.Min(cx0, rx0);
                    cy0 = Math.Min(cy0, ry0);
                    cx1 = Math.Max(cx1, rx1);
                    cy1 = Math.Max(cy1, ry1);
                }
            }
            if (koller != null)
            {
                for (int i = 0; i < koller.Count; i++)
                {
                    var k = koller[i];
                    var e = k.e;
                    if (e == null) continue;
                    for (int side = 0; side < 2; side++)
                    {
                        double ax0, ay0, ax1, ay1;
                        if (k.longIsX)
                        {
                            if (side == 0) { ax0 = e.MinX; ay0 = e.MinY; ax1 = e.MinX + k.luLo; ay1 = e.MaxY; }
                            else { ax0 = e.MaxX - k.luHi; ay0 = e.MinY; ax1 = e.MaxX; ay1 = e.MaxY; }
                        }
                        else
                        {
                            if (side == 0) { ax0 = e.MinX; ay0 = e.MinY; ax1 = e.MaxX; ay1 = e.MinY + k.luLo; }
                            else { ax0 = e.MinX; ay0 = e.MaxY - k.luHi; ax1 = e.MaxX; ay1 = e.MaxY; }
                        }
                        if (ax1 < ax0) { double t = ax0; ax0 = ax1; ax1 = t; }
                        if (ay1 < ay0) { double t = ay0; ay0 = ay1; ay1 = t; }
                        double ox0 = Math.Max(cx0, ax0), ox1 = Math.Min(cx1, ax1);
                        double oy0 = Math.Max(cy0, ay0), oy1 = Math.Min(cy1, ay1);
                        if (ox1 - ox0 < 2.0 || oy1 - oy0 < 2.0) continue;
                        AddBox(ax0, ay0, ax1, ay1);
                        cx0 = Math.Min(cx0, ax0);
                        cy0 = Math.Min(cy0, ay0);
                        cx1 = Math.Max(cx1, ax1);
                        cy1 = Math.Max(cy1, ay1);
                    }
                }
            }
            x0 = cx0;
            y0 = cy0;
            x1 = cx1;
            y1 = cy1;
            return joinsKolon;
        }

        /// <summary>Perde kesit gibi uç / gövde düşey etiket (ETIKET CIZGISI). Gövde/ölçü poligon dışına.</summary>
        private void DrawPoligonKolonKesitDuseyEtiketleri(
            Transaction tr, BlockTableRecord btr, Envelope overall,
            List<(Envelope e, bool longIsX, double luLo, double luHi, double ipLo, double ipHi)> koller,
            List<(double x, double y)> pts,
            int ucDia, int gvDia,
            List<(double x0, double y0, double x1, double y1)> rects = null)
        {
            if (tr == null || btr == null || koller == null || koller.Count == 0) return;
            if (ucDia < 6) ucDia = 14;
            if (gvDia < 6) gvDia = 12;
            double barLo = KolonKesitPaspayiCm + KolonKesitEtriyeRadiusCm;
            const double same = 2.0;
            double cx = overall != null ? 0.5 * (overall.MinX + overall.MaxX) : 0.0;
            double cy = overall != null ? 0.5 * (overall.MinY + overall.MaxY) : 0.0;

            bool InRect(double x, double y, double a0, double b0, double a1, double b1)
            {
                if (a1 < a0) { double t = a0; a0 = a1; a1 = t; }
                if (b1 < b0) { double t = b0; b0 = b1; b1 = t; }
                return x >= a0 - 1.0 && x <= a1 + 1.0 && y >= b0 - 1.0 && y <= b1 + 1.0;
            }
            bool IsGovdeZoneBar(
                (Envelope e, bool longIsX, double luLo, double luHi, double ipLo, double ipHi) kk,
                double x, double y)
            {
                var ee = kk.e;
                if (ee == null) return false;
                double rad = KolonKesitEtriyeRadiusCm;
                if (kk.longIsX)
                {
                    double innerL = ee.MinX + kk.luLo - kk.ipLo - rad;
                    double innerR = ee.MaxX - kk.luHi + kk.ipHi + rad;
                    if (x < innerL + 0.5 || x > innerR - 0.5) return false;
                    return Math.Abs(y - (ee.MinY + barLo)) <= same || Math.Abs(y - (ee.MaxY - barLo)) <= same;
                }
                double innerB = ee.MinY + kk.luLo - kk.ipLo - rad;
                double innerT = ee.MaxY - kk.luHi + kk.ipHi + rad;
                if (y < innerB + 0.5 || y > innerT - 0.5) return false;
                return Math.Abs(x - (ee.MinX + barLo)) <= same || Math.Abs(x - (ee.MaxX - barLo)) <= same;
            }
            int CountInUnion(
                List<(double x0, double y0, double x1, double y1)> boxes,
                (Envelope e, bool longIsX, double luLo, double luHi, double ipLo, double ipHi) kk)
            {
                if (pts == null || boxes == null || boxes.Count == 0) return 0;
                int n = 0;
                for (int pi = 0; pi < pts.Count; pi++)
                {
                    if (IsGovdeZoneBar(kk, pts[pi].x, pts[pi].y)) continue;
                    bool hit = false;
                    for (int b = 0; b < boxes.Count; b++)
                    {
                        if (!InRect(pts[pi].x, pts[pi].y, boxes[b].x0, boxes[b].y0, boxes[b].x1, boxes[b].y1))
                            continue;
                        hit = true;
                        break;
                    }
                    if (hit) n++;
                }
                return n;
            }

            string Lab(int n, int dia) =>
                n.ToString(CultureInfo.InvariantCulture) + "\u00F8" + dia.ToString(CultureInfo.InvariantCulture);

            var labeled = new List<(double x0, double y0, double x1, double y1)>();
            bool TryClaimBaslik(double x0, double y0, double x1, double y1)
            {
                if (x1 < x0) { double t = x0; x0 = x1; x1 = t; }
                if (y1 < y0) { double t = y0; y0 = y1; y1 = t; }
                for (int b = 0; b < labeled.Count; b++)
                {
                    var o = labeled[b];
                    double ox0 = Math.Max(x0, o.x0), ox1 = Math.Min(x1, o.x1);
                    double oy0 = Math.Max(y0, o.y0), oy1 = Math.Min(y1, o.y1);
                    if (ox1 - ox0 >= 6.0 && oy1 - oy0 >= 6.0)
                        return false;
                }
                labeled.Add((x0, y0, x1, y1));
                return true;
            }

            var kolOrder = new List<int>();
            for (int i = 0; i < koller.Count; i++)
            {
                if (koller[i].e != null && koller[i].longIsX) kolOrder.Add(i);
            }
            for (int i = 0; i < koller.Count; i++)
            {
                if (koller[i].e != null && !koller[i].longIsX) kolOrder.Add(i);
            }

            for (int oi = 0; oi < kolOrder.Count; oi++)
            {
                int i = kolOrder[oi];
                var k = koller[i];
                var e = k.e;
                if (e == null) continue;
                bool outLeft = overall == null || 0.5 * (e.MinX + e.MaxX) <= cx + 1e-6;
                bool outDown = overall == null || 0.5 * (e.MinY + e.MaxY) <= cy + 1e-6;
                if (k.longIsX)
                {
                    double yBar = outDown ? e.MinY + barLo : e.MaxY - barLo;
                    double yPoly = outDown ? e.MinY : e.MaxY;
                    double stemSign = outDown ? -1.0 : 1.0;
                    void EtiketUcX(double xLo, double xHi)
                    {
                        double bx0 = xLo, by0 = e.MinY, bx1 = xHi, by1 = e.MaxY;
                        var ucBoxes = new List<(double x0, double y0, double x1, double y1)>();
                        bool joinsKol = ExpandPoligonUcEtiketKutu(ref bx0, ref by0, ref bx1, ref by1, koller, rects, ucBoxes);
                        if (joinsKol && overall != null && Math.Abs(by1 - overall.MaxY) <= 2.0)
                        {
                            bool dikeyKolUstteCizer = false;
                            for (int ki = 0; ki < koller.Count; ki++)
                            {
                                if (koller[ki].longIsX) continue;
                                var ke = koller[ki].e;
                                if (ke == null) continue;
                                if (Math.Abs(ke.MaxY - overall.MaxY) <= 1.0)
                                {
                                    dikeyKolUstteCizer = true;
                                    break;
                                }
                            }
                            if (dikeyKolUstteCizer) return;
                        }
                        if (!TryClaimBaslik(xLo, e.MinY, xHi, e.MaxY)) return;
                        int n = CountInUnion(ucBoxes, k);
                        if (n < 1) return;
                        double xa = xHi, xb = xLo;
                        bool any = false;
                        if (pts != null)
                        {
                            for (int p = 0; p < pts.Count; p++)
                            {
                                if (pts[p].x < xLo - 1.0 || pts[p].x > xHi + 1.0) continue;
                                if (Math.Abs(pts[p].y - yBar) > same) continue;
                                if (pts[p].x < xa) xa = pts[p].x;
                                if (pts[p].x > xb) xb = pts[p].x;
                                any = true;
                            }
                        }
                        if (!any) { xa = xLo + barLo; xb = xHi - barLo; }
                        if (xb - xa < 2.0) return;
                        DrawPerdeKesitDonatiCizgiEtiket(tr, btr, xa, yPoly, xb, yPoly, Lab(n, ucDia), alongX: true,
                            origNote: null, stemSign: stemSign,
                            stemCmOverride: PoligonKolonEtiketDisCm, txtFromLineCm: PoligonKolonEtiketYaziAraCm);
                    }
                    EtiketUcX(e.MinX, e.MinX + k.luLo);
                    EtiketUcX(e.MaxX - k.luHi, e.MaxX);

                    var gpos = new List<(double x, double y)>();
                    ForEachPoligonGovdeDuseyKonumOnKol(k, pts, (x, y) => gpos.Add((x, y)));
                    double g0 = double.MaxValue, g1 = double.MinValue;
                    int nFace = 0;
                    for (int p = 0; p < gpos.Count; p++)
                    {
                        if (Math.Abs(gpos[p].y - yBar) > same) continue;
                        nFace++;
                        if (gpos[p].x < g0) g0 = gpos[p].x;
                        if (gpos[p].x > g1) g1 = gpos[p].x;
                    }
                    if (nFace >= 1 && g1 - g0 >= 2.0)
                    {
                        string gvLab = "2x" + nFace.ToString(CultureInfo.InvariantCulture) + "\u00F8"
                            + gvDia.ToString(CultureInfo.InvariantCulture);
                        DrawPerdeKesitDonatiCizgiEtiket(tr, btr, g0, yPoly, g1, yPoly, gvLab, alongX: true,
                            origNote: null, stemSign: stemSign,
                            stemCmOverride: PoligonKolonEtiketDisCm, txtFromLineCm: PoligonKolonEtiketYaziAraCm);
                    }
                }
                else
                {
                    double xBar = outLeft ? e.MinX + barLo : e.MaxX - barLo;
                    double xPoly = outLeft ? e.MinX : e.MaxX;
                    double stemSign = outLeft ? -1.0 : 1.0;
                    void DrawBaslikYatay(double yLo, double yHi, bool atBottom)
                    {
                        double bx0 = e.MinX, by0 = yLo, bx1 = e.MaxX, by1 = yHi;
                        var ucBoxes = new List<(double x0, double y0, double x1, double y1)>();
                        bool joinsKol = ExpandPoligonUcEtiketKutu(ref bx0, ref by0, ref bx1, ref by1, koller, rects, ucBoxes);
                        int n = CountInUnion(ucBoxes, k);
                        if (!joinsKol)
                        {
                            bx0 = e.MinX; by0 = yLo; bx1 = e.MaxX; by1 = yHi;
                            if (bx1 < bx0) { double t = bx0; bx0 = bx1; bx1 = t; }
                            if (by1 < by0) { double t = by0; by0 = by1; by1 = t; }
                            if (n < 1 && pts != null)
                            {
                                for (int p = 0; p < pts.Count; p++)
                                {
                                    if (InRect(pts[p].x, pts[p].y, bx0, by0, bx1, by1))
                                        n++;
                                }
                            }
                        }
                        if (n < 1) return;
                        if (!TryClaimBaslik(e.MinX, yLo, e.MaxX, yHi)) return;
                        double yFace = atBottom ? by0 : by1;
                        double yBarFace = atBottom ? yFace + barLo : yFace - barLo;
                        double xa = bx1, xb = bx0;
                        bool any = false;
                        if (pts != null)
                        {
                            for (int p = 0; p < pts.Count; p++)
                            {
                                if (pts[p].x < bx0 - 1.0 || pts[p].x > bx1 + 1.0) continue;
                                if (Math.Abs(pts[p].y - yBarFace) > same) continue;
                                if (pts[p].x < xa) xa = pts[p].x;
                                if (pts[p].x > xb) xb = pts[p].x;
                                any = true;
                            }
                        }
                        if (!any) { xa = bx0 + barLo; xb = bx1 - barLo; }
                        if (xb - xa < 2.0) return;
                        DrawPerdeKesitDonatiCizgiEtiket(tr, btr, xa, yFace, xb, yFace, Lab(n, ucDia), alongX: true,
                            origNote: null, stemSign: atBottom ? -1.0 : 1.0,
                            stemCmOverride: PoligonKolonEtiketDisCm, txtFromLineCm: PoligonKolonEtiketYaziAraCm);
                    }
                    void EtiketUcY(double yLo, double yHi)
                    {
                        double bx0 = e.MinX, by0 = yLo, bx1 = e.MaxX, by1 = yHi;
                        var ucBoxes = new List<(double x0, double y0, double x1, double y1)>();
                        ExpandPoligonUcEtiketKutu(ref bx0, ref by0, ref bx1, ref by1, koller, rects, ucBoxes);
                        if (overall != null && Math.Abs(by0 - overall.MinY) <= 2.0)
                        {
                            DrawBaslikYatay(yLo, yHi, atBottom: true);
                            return;
                        }
                        if (overall != null && Math.Abs(by1 - overall.MaxY) <= 2.0)
                        {
                            DrawBaslikYatay(yLo, yHi, atBottom: false);
                            return;
                        }
                        int n = CountInUnion(ucBoxes, k);
                        if (n < 1) return;
                        if (!TryClaimBaslik(e.MinX, yLo, e.MaxX, yHi)) return;
                        double ya = yHi, yb = yLo;
                        bool any = false;
                        if (pts != null)
                        {
                            for (int p = 0; p < pts.Count; p++)
                            {
                                if (pts[p].y < yLo - 1.0 || pts[p].y > yHi + 1.0) continue;
                                if (Math.Abs(pts[p].x - xBar) > same) continue;
                                if (pts[p].y < ya) ya = pts[p].y;
                                if (pts[p].y > yb) yb = pts[p].y;
                                any = true;
                            }
                        }
                        if (!any) { ya = yLo + barLo; yb = yHi - barLo; }
                        if (yb - ya < 2.0) return;
                        DrawPerdeKesitDonatiCizgiEtiket(tr, btr, xPoly, ya, xPoly, yb, Lab(n, ucDia), alongX: false,
                            origNote: null, stemSign: stemSign,
                            stemCmOverride: PoligonKolonEtiketDisCm, txtFromLineCm: PoligonKolonEtiketYaziAraCm);
                    }
                    if (overall == null || Math.Abs(e.MinY - overall.MinY) > 1.0)
                        EtiketUcY(e.MinY, e.MinY + k.luLo);
                    else
                        DrawBaslikYatay(e.MinY, e.MinY + k.luLo, atBottom: true);
                    if (overall == null || Math.Abs(e.MaxY - overall.MaxY) > 1.0)
                        EtiketUcY(e.MaxY - k.luHi, e.MaxY);
                    else
                        DrawBaslikYatay(e.MaxY - k.luHi, e.MaxY, atBottom: false);

                    var gpos = new List<(double x, double y)>();
                    ForEachPoligonGovdeDuseyKonumOnKol(k, pts, (x, y) => gpos.Add((x, y)));
                    double g0 = double.MaxValue, g1 = double.MinValue;
                    int nFace = 0;
                    for (int p = 0; p < gpos.Count; p++)
                    {
                        if (Math.Abs(gpos[p].x - xBar) > same) continue;
                        nFace++;
                        if (gpos[p].y < g0) g0 = gpos[p].y;
                        if (gpos[p].y > g1) g1 = gpos[p].y;
                    }
                    if (nFace >= 1 && g1 - g0 >= 2.0)
                    {
                        string gvLab = "2x" + nFace.ToString(CultureInfo.InvariantCulture) + "\u00F8"
                            + gvDia.ToString(CultureInfo.InvariantCulture);
                        DrawPerdeKesitDonatiCizgiEtiket(tr, btr, xPoly, g0, xPoly, g1, gvLab, alongX: false,
                            origNote: null, stemSign: stemSign,
                            stemCmOverride: PoligonKolonEtiketDisCm, txtFromLineCm: PoligonKolonEtiketYaziAraCm);
                    }
                }
            }
        }

        /// <summary>Perde kesit: φ8/[13]/8 → φ8/8 etriye, φ8/13 gövde yatay. Uç=gövde olsa da (φ8/[13]/13) kırmızı gövde yazılır.</summary>
        private static void FormatPerdeEtriyeKesitYazilari(string etRaw, out string etUc, out string etGovde)
        {
            etUc = null;
            etGovde = null;
            string lab = KolonDonatiTableDrawer.FormatEtriyeForTableDisplay(etRaw);
            if (string.IsNullOrWhiteSpace(lab)) lab = etRaw;
            if (string.IsNullOrWhiteSpace(lab)) return;
            lab = lab.Replace("[", string.Empty).Replace("]", string.Empty);
            var m = Regex.Match(lab,
                @"([\u00F8\u00D8ØøφΦ\u03C6\u03A6]\s*\d{1,2})\s*/\s*(\d{1,2})(?:\s*/\s*(\d{1,2}))?");
            if (m.Success)
            {
                string dia = m.Groups[1].Value.Replace(" ", "");
                string sGv = m.Groups[2].Value;
                string sUc = m.Groups[3].Success && m.Groups[3].Value.Length > 0 ? m.Groups[3].Value : sGv;
                etUc = KolonDonatiTableDrawer.NormalizeDiameterSymbol(dia + "/" + sUc);
                etGovde = KolonDonatiTableDrawer.NormalizeDiameterSymbol(dia + "/" + sGv);
                return;
            }
            ParsePerdeEtriyeAralikCm(etRaw ?? lab, out double sGvCmD, out double sUcCmD);
            int diaMm = 8;
            TryParseEtriyeDiaMm(etRaw ?? lab, out diaMm);
            string d = "\u00F8" + diaMm.ToString(CultureInfo.InvariantCulture);
            int sUcCm = Math.Max(5, (int)Math.Round(sUcCmD));
            int sGvCm = Math.Max(5, (int)Math.Round(sGvCmD));
            etUc = KolonDonatiTableDrawer.NormalizeDiameterSymbol(d + "/" + sUcCm.ToString(CultureInfo.InvariantCulture));
            etGovde = KolonDonatiTableDrawer.NormalizeDiameterSymbol(d + "/" + sGvCm.ToString(CultureInfo.InvariantCulture));
        }

        private double DrawKolonKesitRenkliYazi(Transaction tr, BlockTableRecord btr, Point3d pos, string text, double h, short aci)
        {
            if (tr == null || btr == null || string.IsNullOrWhiteSpace(text)) return pos.X;
            ObjectId styleId = GetOrCreateYaziBeykentTextStyle(tr, btr.Database);
            var txt = new DBText
            {
                Layer = LayerDonatiYazisiPerde,
                TextStyleId = styleId,
                Height = h,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(text),
                Position = pos,
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = TextVerticalMode.TextBottom,
                AlignmentPoint = pos,
                Rotation = 0,
                Color = Color.FromColorIndex(ColorMethod.ByAci, aci),
                LineWeight = LineWeight.LineWeight020
            };
            AppendEntity(tr, btr, txt);
            try
            {
                var ext = txt.GeometricExtents;
                if (ext.MaxPoint.X > pos.X + 1.0)
                    return ext.MaxPoint.X;
            }
            catch { }
            return pos.X + text.Length * h * 0.72;
        }

        private void DrawKolonKesitEskiGprDonatiNotu(
            Transaction tr, BlockTableRecord btr, Point3d pos, string orig, double h)
        {
            if (tr == null || btr == null || string.IsNullOrWhiteSpace(orig)) return;
            ObjectId styleId = GetOrCreateYaziBeykentTextStyle(tr, btr.Database);
            var red = new DBText
            {
                Layer = LayerDonatiYazisiPerde,
                TextStyleId = styleId,
                Height = h,
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol("(" + orig + ")"),
                Position = pos,
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                AlignmentPoint = pos,
                Rotation = 0,
                Color = Color.FromColorIndex(ColorMethod.ByAci, 1),
                LineWeight = LineWeight.LineWeight020
            };
            try { red.AdjustAlignment(btr.Database); } catch { }
            AppendEntity(tr, btr, red);
        }

        /// <summary>
        /// Görünüş kenarı: kolon yüzünden bu kadar taşmayan kiriş/perde çizilmez.
        /// Enine kiriş genişliği (~7–10 cm taşma) boyuna kirişin içine kutu basmasın.
        /// </summary>
        private const double KenarKirisMinUzantiCm = 15.0;

        private static bool KenarRunOnSide(double x0, double x1, double colLo, double colHi, bool left)
        {
            double lo = Math.Min(x0, x1), hi = Math.Max(x0, x1);
            if (left) return lo < colLo - KenarKirisMinUzantiCm;
            return hi > colHi + KenarKirisMinUzantiCm;
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
                var zs = new List<(double zb, double zt)>();
                foreach (var b in runs)
                {
                    if (!KenarRunOnSide(b.x0, b.x1, colLo, colHi, left)) continue;
                    zs.Add((b.zb, b.zt));
                }
                foreach (var z in PickPrimaryKenarZs(zs))
                {
                    dest.Add(z.zb);
                    dest.Add(z.zt);
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
            var zs = new List<(double zb, double zt)>();
            foreach (var b in beams)
            {
                if (!KenarRunOnSide(b.x0, b.x1, colLo, colHi, left)) continue;
                zs.Add((b.zb, b.zt));
            }
            foreach (var z in PickPrimaryKenarZs(zs))
                holes.Add((Y(z.zb), Y(z.zt)));
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
                    if (KenarRunOnSide(b.x0, b.x1, colLo, colHi, left)) return true;
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
            double kenarKesitSolCm,
            double kenarKesitSagCm,
            bool mergeStories,
            Func<bool, double, double, double> faceAtZ = null)
        {
            if (runs == null || ln == null) return;
            var leftZ = new List<(double zb, double zt)>();
            var rightZ = new List<(double zb, double zt)>();
            foreach (var b in runs)
            {
                if (KenarRunOnSide(b.x0, b.x1, colLo, colHi, left: true)) leftZ.Add((b.zb, b.zt));
                if (KenarRunOnSide(b.x0, b.x1, colLo, colHi, left: false)) rightZ.Add((b.zb, b.zt));
            }
            leftZ = PickPrimaryKenarZs(leftZ);
            rightZ = PickPrimaryKenarZs(rightZ);
            if (mergeStories)
            {
                leftZ = MergeZRanges(leftZ);
                rightZ = MergeZRanges(rightZ);
            }
            void DrawSide(bool left, List<(double zb, double zt)> zs)
            {
                double xCut = left ? colLo - kenarKesitSolCm : colHi + kenarKesitSagCm;
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

        /// <summary>Aynı yüzde binen ikinci kiriş/perde çizilmez; kot birleştirilmez, en yüksek olan kalır.</summary>
        private static List<(double zb, double zt)> PickPrimaryKenarZs(List<(double zb, double zt)> src)
        {
            var res = new List<(double zb, double zt)>();
            if (src == null || src.Count == 0) return res;
            var ordered = src.Select(p => (zb: Math.Min(p.zb, p.zt), zt: Math.Max(p.zb, p.zt)))
                .OrderByDescending(p => p.zt - p.zb)
                .ThenBy(p => p.zb)
                .ToList();
            foreach (var p in ordered)
            {
                bool hit = false;
                foreach (var k in res)
                {
                    if (p.zb < k.zt - 2.0 && p.zt > k.zb + 2.0)
                    {
                        hit = true;
                        break;
                    }
                }
                if (!hit) res.Add(p);
            }
            res.Sort((a, b) => a.zb.CompareTo(b.zb));
            return res;
        }

        /// <summary>Görünüş X'i (kolon major) ile kiriş/perde aksı en fazla 20°.</summary>
        private bool BeamAxisAlongElevX(BeamInfo beam, AffineTransformation rot)
        {
            if (beam == null || rot == null || _axisService == null) return false;
            if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1) ||
                !_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2))
                return false;
            var a = new Coordinate(p1.X, p1.Y);
            var b = new Coordinate(p2.X, p2.Y);
            rot.Transform(a, a);
            rot.Transform(b, b);
            double dx = b.X - a.X, dy = b.Y - a.Y;
            if (dx * dx + dy * dy < 1.0) return false;
            double ang = Math.Abs(Math.Atan2(dy, dx));
            if (ang > Math.PI * 0.5) ang = Math.PI - ang;
            return ang <= (20.0 * Math.PI / 180.0);
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

        private FloorInfo FloorAtIndex(int floorIndex)
        {
            if (_model?.Floors == null || floorIndex < 0 || floorIndex >= _model.Floors.Count)
                return null;
            return _model.Floors[floorIndex];
        }

        /// <summary>
        /// Kapama perdesi: GPR PANEL BETONARME (P/PB indisi). S kolon tablosunda değil.
        /// Finiş katında elemana herhangi bir yüzünden bağlanırsa KALIP50 panel firketesi (TBDY/TS500 Şekil 7.2 değil).
        /// </summary>
        private bool KolonKataKapamaPerdeBagli(FloorInfo floor, Geometry colPoly)
        {
            if (floor == null || colPoly == null || colPoly.IsEmpty) return false;
            if (_gprPerdePanelDonati == null || _gprPerdePanelDonati.Count == 0) return false;
            bool hit = false;
            ForEachKolonBagliKiris(floor, colPoly, includeWalls: true, (beam, poly, zb, zt) =>
            {
                if (hit || poly == null || poly.IsEmpty) return;
                if (PolygonsMostlyOverlap(poly, colPoly)) return;
                if (IsGprPanelWall(floor, beam))
                    hit = true;
            });
            return hit;
        }

        private bool IsGprPanelWall(FloorInfo floor, BeamInfo beam)
        {
            if (beam == null || beam.IsWallFlag != 1) return false;
            if (_gprPerdePanelDonati == null || _gprPerdePanelDonati.Count == 0) return false;
            int wallNo = GetBeamNumero(beam.BeamId);
            if (wallNo <= 0) return false;
            if (TryFindGprPerdePanel(floor, wallNo, out var don) && don != null && don.BarCount > 0)
                return true;
            foreach (var kv in _gprPerdePanelDonati)
            {
                if (kv.Value != null && kv.Value.WallNo == wallNo && kv.Value.BarCount > 0)
                    return true;
            }
            return false;
        }

        private static bool PolygonsMostlyOverlap(Geometry a, Geometry b)
        {
            if (a == null || b == null || a.IsEmpty || b.IsEmpty) return false;
            try
            {
                double ia = a.Intersection(b).Area;
                double m = Math.Min(a.Area, b.Area);
                return m > 1.0 && ia > 0.5 * m;
            }
            catch { return false; }
        }

        /// <summary>Perde başlığı firkete c: döşeme altına inmez, en az 12φ.</summary>
        private static double PerdeBasligiFirketeCCm(double zHoriz, double zSof, double zTop, double phi12, double rBend)
        {
            double cMin = Math.Max(phi12, 5.0);
            double zDown = zSof - 10.0;
            if (zSof >= zTop - 1.0)
                zDown = zHoriz - cMin;
            if (zDown > zHoriz - cMin)
                zDown = zHoriz - cMin;
            double c = zHoriz - zDown;
            if (c < 2.0 * rBend + 1.0)
                c = Math.Max(cMin, 2.0 * rBend + 1.0);
            return c;
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
            Geometry colR;
            try { colR = rot.Transform(colPoly); }
            catch { colR = colPoly; }
            if (colR == null || colR.IsEmpty) return;
            var ce = colR.EnvelopeInternal;
            ForEachKolonBagliKiris(floor, colPoly, includeWalls: walls, (beam, poly, zb, zt) =>
            {
                Geometry rg;
                try { rg = rot.Transform(poly); }
                catch { return; }
                if (rg == null || rg.IsEmpty) return;
                // Enine kiriş: kesit kaçırırsa envelope ile boyuna kiriş gibi çizilmesin.
                if (!TrySectionCutKolonX(rg, sectionY, wallHalf, out double x0, out double x1))
                    return;
                if (x1 - x0 < 2.0) return;
                if (x0 >= ce.MinX - KenarKirisMinUzantiCm && x1 <= ce.MaxX + KenarKirisMinUzantiCm)
                    return;
                if (!BeamAxisAlongElevX(beam, rot))
                    return;
                dest.Add((x0, x1, zb, zt));
            });
        }
    }
}
