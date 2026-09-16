using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Simplify;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// STA sonlu eleman (CONTOOL) temel PNG: mavi zemin + renk geçişi = ilave donatı.
    /// Mesh çizgileri yok sayılır; STA dikdörtgen etiketlerinin kestiği/kapsadığı lekelere bakılır.
    /// </summary>
    internal static class TemelIlaveDonatiFromPng
    {
        public const string LayerName = "TEMEL ILAVE DONATI (BEYKENT)";
        public const string LayerBoxName = "TEMEL ILAVE KUTU (BEYKENT)";
        public const string LayerTextName = "DONATI YAZISI (BEYKENT)";
        private const int MaxGrid = 560;

        public struct StaCadBox
        {
            public double MinX, MaxX, MinY, MaxY;
        }

        public static int Draw(
            string pngPath,
            Envelope temelWorld,
            Geometry temelGeom,
            Database db,
            Editor ed,
            Transaction tr,
            BlockTableRecord btr,
            bool drawStaBoxes = true,
            List<StaCadBox> staCadBoxes = null,
            string heatLayer = null,
            short heatAci = 1)
        {
            if (string.IsNullOrWhiteSpace(pngPath) || !File.Exists(pngPath))
            {
                ed?.WriteMessage("\nTEMELDONATI: PNG dosyasi bulunamadi.");
                return 0;
            }
            using (var bmp = LoadBitmap(pngPath))
                return DrawBitmap(bmp, temelWorld, temelGeom, db, ed, tr, btr, drawStaBoxes, staCadBoxes, heatLayer, heatAci);
        }

        public static int DrawBitmap(
            Bitmap bmp,
            Envelope temelWorld,
            Geometry temelGeom,
            Database db,
            Editor ed,
            Transaction tr,
            BlockTableRecord btr,
            bool drawStaBoxes = false,
            List<StaCadBox> staCadBoxes = null,
            string heatLayer = null,
            short heatAci = 1)
        {
            if (bmp == null)
            {
                ed?.WriteMessage("\nTEMELDONATI: Isi haritasi resmi yok.");
                return 0;
            }
            if (temelWorld == null || temelWorld.Width < 1 || temelWorld.Height < 1)
            {
                ed?.WriteMessage("\nTEMELDONATI: Temel zarfi alinamadi.");
                return 0;
            }

            try
            {
                if (LooksLikeLightPdfGraph(bmp, 0, 0, bmp.Width - 1, bmp.Height - 1)
                    && staCadBoxes != null && staCadBoxes.Count > 0)
                {
                    int locked = DrawHeatLockedToStaBoxes(bmp, temelWorld, temelGeom, staCadBoxes, db, ed, tr, btr, heatLayer, heatAci);
                    if (locked > 0) return locked;
                }

                bool[,] mask;
                int cols, rows;
                int boxCount;
                List<Box> boxes;
                int pngMinX, pngMinY, pngMaxX, pngMaxY;
                if (!TryBuildExtraRebarMask(bmp, temelWorld, temelGeom, db, ed, tr, btr, staCadBoxes, out mask, out cols, out rows, out boxCount, out Envelope mappedWorld, out boxes, out pngMinX, out pngMinY, out pngMaxX, out pngMaxY))
                {
                    ed?.WriteMessage("\nTEMELDONATI: Ilave donati renk gecisi bulunamadi (mavi mesh + kutu ici isi haritasi beklenir).");
                    return 0;
                }
                temelWorld = mappedWorld ?? temelWorld;
                EnsureLayer(tr, db, LayerName, 1, LineWeight.LineWeight025);
                if (drawStaBoxes)
                {
                    EnsureLayer(tr, db, LayerBoxName, 1, LineWeight.LineWeight020);
                    EnsureLayer(tr, db, LayerTextName, 3, LineWeight.LineWeight020);
                    int nLab = DrawStaBoxesAndLabels(bmp, boxes, pngMinX, pngMinY, pngMaxX, pngMaxY, cols, rows, temelWorld, tr, btr, ed, out int nTxt);
                    ed?.WriteMessage("\nTEMELDONATI: {0} kutu, {1} donati yazisi ({2} / {3}).", nLab, nTxt, LayerBoxName, LayerTextName);
                }

                Geometry union = BuildSmoothedUnion(mask, cols, rows, temelWorld);
                if (union == null || union.IsEmpty)
                {
                    ed?.WriteMessage("\nTEMELDONATI: Ilave donati bolgesi cikarilamadi.");
                    return 0;
                }

                union = ClipToTemel(union, temelGeom);
                if (union == null || union.IsEmpty)
                {
                    ed?.WriteMessage("\nTEMELDONATI: Temel siniri disinda kalan kisim trimlenince bolge kalmadi.");
                    return 0;
                }

                string ringLayer = string.IsNullOrEmpty(heatLayer) ? LayerName : heatLayer;
                EnsureLayer(tr, db, ringLayer, heatAci, LineWeight.LineWeight025);
                int n = DrawRings(tr, btr, union, ringLayer);
                ed?.WriteMessage(
                    "\nTEMELDONATI: {0} polyline (renk gecisi, temel icine kirpildi). Mesh yok sayildi.{1} Katman {2}.",
                    n, drawStaBoxes ? " STA kutusu: " + boxCount + "." : "", ringLayer);
                return n;
            }
            catch (System.Exception ex)
            {
                ed?.WriteMessage("\nTEMELDONATI overlay hata: {0}", ex.Message);
                return 0;
            }
        }

        private static Geometry ClipToTemel(Geometry extra, Geometry temel)
        {
            if (extra == null || extra.IsEmpty) return extra;
            if (temel == null || temel.IsEmpty) return extra;
            try
            {
                Geometry clip = temel;
                try { clip = clip.Buffer(0); } catch { }
                if (clip == null || clip.IsEmpty) return extra;
                Geometry cut = extra.Intersection(clip);
                try { cut = cut.Buffer(0); } catch { }
                return DropTinyParts(cut, 80);
            }
            catch
            {
                return extra;
            }
        }

        private static Geometry DropTinyParts(Geometry g, double minArea)
        {
            if (g == null || g.IsEmpty) return g;
            if (g is Polygon pg)
                return pg.Area < minArea ? g.Factory.CreatePolygon() : pg;
            if (g is GeometryCollection gc)
            {
                var keep = new List<Geometry>();
                for (int i = 0; i < gc.NumGeometries; i++)
                {
                    Geometry p = DropTinyParts(gc.GetGeometryN(i), minArea);
                    if (p != null && !p.IsEmpty) keep.Add(p);
                }
                if (keep.Count == 0) return g.Factory.CreatePolygon();
                if (keep.Count == 1) return keep[0];
                return g.Factory.BuildGeometry(keep);
            }
            return g;
        }

        private static Bitmap LoadBitmap(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var tmp = new Bitmap(fs))
                return new Bitmap(tmp);
        }

        private static int DrawHeatLockedToStaBoxes(
            Bitmap bmp,
            Envelope cadEnv,
            Geometry temelGeom,
            List<StaCadBox> staCad,
            Database db,
            Editor ed,
            Transaction tr,
            BlockTableRecord btr,
            string heatLayer,
            short heatAci)
        {
            int minX, minY, maxX, maxY, gw, gh;
            bool[,] foundMask;
            if (!TryFindGrayMeshFoundationExtent(bmp, out minX, out minY, out maxX, out maxY, out foundMask, out gw, out gh))
            {
                minX = 0; minY = 0; maxX = bmp.Width - 1; maxY = bmp.Height - 1;
                foundMask = null; gw = gh = 0;
            }

            if (!TryBuildBoxAffine(bmp, staCad, minX, minY, maxX, maxY, cadEnv, ed, out double a, out double b, out double c0, out double d))
                return 0;

            Geometry heat = TraceHeatAffine(bmp, minX, minY, maxX, maxY, a, b, c0, d);
            if (heat == null || heat.IsEmpty)
            {
                ed?.WriteMessage("\nTEMELDONATI: kutu kilidi var ama renk konturu cikmadi.");
                return 0;
            }
            heat = ClipToTemel(heat, temelGeom);
            if (heat == null || heat.IsEmpty) return 0;
            if (string.IsNullOrEmpty(heatLayer)) heatLayer = LayerName;
            EnsureLayer(tr, db, heatLayer, heatAci, LineWeight.LineWeight025);
            int n = DrawRings(tr, btr, heat, heatLayer);
            ed?.WriteMessage("\nTEMELDONATI: {0} isi konturu (piksel iz + STA kutu kilidi). Katman {1}.", n, LayerName);
            return n;
        }

        private static bool TryBuildBoxAffine(
            Bitmap bmp,
            List<StaCadBox> staCad,
            int minX, int minY, int maxX, int maxY,
            Envelope cadEnv,
            Editor ed,
            out double a, out double b, out double c0, out double d)
        {
            a = c0 = 0; b = d = 1;
            if (cadEnv == null || cadEnv.Width < 1 || cadEnv.Height < 1) return false;
            int pw = maxX - minX + 1, ph = maxY - minY + 1;
            if (pw < 8 || ph < 8) return false;
            var px = new List<double>();
            var py = new List<double>();
            var cx = new List<double>();
            var cy = new List<double>();
            int snapped = 0;
            foreach (var box in staCad)
            {
                int x0 = minX + (int)Math.Round((box.MinX - cadEnv.MinX) / cadEnv.Width * pw);
                int x1 = minX + (int)Math.Round((box.MaxX - cadEnv.MinX) / cadEnv.Width * pw);
                int y0 = minY + (int)Math.Round((cadEnv.MaxY - box.MaxY) / cadEnv.Height * ph);
                int y1 = minY + (int)Math.Round((cadEnv.MaxY - box.MinY) / cadEnv.Height * ph);
                if (x1 < x0) { int t = x0; x0 = x1; x1 = t; }
                if (y1 < y0) { int t = y0; y0 = y1; y1 = t; }
                if (!SnapBlackRect(bmp, ref x0, ref y0, ref x1, ref y1))
                    continue;
                snapped++;
                AddCorner(px, py, cx, cy, x0, y1, box.MinX, box.MinY);
                AddCorner(px, py, cx, cy, x1, y1, box.MaxX, box.MinY);
                AddCorner(px, py, cx, cy, x1, y0, box.MaxX, box.MaxY);
                AddCorner(px, py, cx, cy, x0, y0, box.MinX, box.MaxY);
            }
            if (snapped == 0 || !FitLine(px, cx, out a, out b) || !FitLine(py, cy, out c0, out d))
            {
                ed?.WriteMessage("\nTEMELDONATI: STA kutulari resimde kilitlenemedi.");
                return false;
            }
            if (Math.Abs(b) < 1e-6 || Math.Abs(d) < 1e-6) return false;
            ed?.WriteMessage("\nTEMELDONATI: {0}/{1} kutu kenari resmi kilitledi. cm/px X={2:0.000} Y={3:0.000}.",
                snapped, staCad.Count, b, d);
            return true;
        }

        private static bool SnapBlackRect(Bitmap bmp, ref int x0, ref int y0, ref int x1, ref int y1)
        {
            int w = bmp.Width, h = bmp.Height;
            x0 = Math.Max(1, Math.Min(w - 2, x0));
            x1 = Math.Max(1, Math.Min(w - 2, x1));
            y0 = Math.Max(1, Math.Min(h - 2, y0));
            y1 = Math.Max(1, Math.Min(h - 2, y1));
            if (x1 - x0 < 12 || y1 - y0 < 12) return false;
            int s = Math.Max(10, Math.Min(40, Math.Min(x1 - x0, y1 - y0) / 4));
            x0 = BestVerticalBlack(bmp, x0, y0, y1, s);
            x1 = BestVerticalBlack(bmp, x1, y0, y1, s);
            y0 = BestHorizontalBlack(bmp, y0, x0, x1, s);
            y1 = BestHorizontalBlack(bmp, y1, x0, x1, s);
            return x1 - x0 >= 12 && y1 - y0 >= 12;
        }

        private static int BestVerticalBlack(Bitmap bmp, int xGuess, int y0, int y1, int search)
        {
            int w = bmp.Width, h = bmp.Height;
            int bestX = xGuess, best = -1;
            int ya = Math.Max(0, Math.Min(y0, y1));
            int yb = Math.Min(h - 1, Math.Max(y0, y1));
            for (int dx = -search; dx <= search; dx++)
            {
                int x = xGuess + dx;
                if (x < 0 || x >= w) continue;
                int n = 0, tot = 0;
                for (int y = ya; y <= yb; y += 2)
                {
                    tot++;
                    if (IsPdfBlackFrame(bmp.GetPixel(x, y))) n++;
                }
                if (tot > 0 && n > best && n * 5 >= tot)
                {
                    best = n;
                    bestX = x;
                }
            }
            return bestX;
        }

        private static int BestHorizontalBlack(Bitmap bmp, int yGuess, int x0, int x1, int search)
        {
            int w = bmp.Width, h = bmp.Height;
            int bestY = yGuess, best = -1;
            int xa = Math.Max(0, Math.Min(x0, x1));
            int xb = Math.Min(w - 1, Math.Max(x0, x1));
            for (int dy = -search; dy <= search; dy++)
            {
                int y = yGuess + dy;
                if (y < 0 || y >= h) continue;
                int n = 0, tot = 0;
                for (int x = xa; x <= xb; x += 2)
                {
                    tot++;
                    if (IsPdfBlackFrame(bmp.GetPixel(x, y))) n++;
                }
                if (tot > 0 && n > best && n * 5 >= tot)
                {
                    best = n;
                    bestY = y;
                }
            }
            return bestY;
        }

        private static Geometry TraceHeatAffine(
            Bitmap bmp, int minX, int minY, int maxX, int maxY,
            double a, double b, double c0, double d)
        {
            int pw = maxX - minX + 1, ph = maxY - minY + 1;
            int step = Math.Max(2, (int)Math.Ceiling(Math.Max(pw, ph) / 480.0));
            int cols = Math.Max(12, pw / step);
            int rows = Math.Max(12, ph / step);
            var fill = new bool[rows, cols];
            int on = 0;
            for (int r = 0; r < rows; r++)
            {
                int y = minY + Math.Min(ph - 1, r * ph / rows + ph / (rows * 2));
                for (int c = 0; c < cols; c++)
                {
                    int x = minX + Math.Min(pw - 1, c * pw / cols + pw / (cols * 2));
                    var col = bmp.GetPixel(x, y);
                    if (!IsLightHeatColor(col) && !IsSoftHeatEdge(col)) continue;
                    fill[r, c] = true;
                    on++;
                }
            }
            if (on < 8) return null;
            var labs = LabelComponents(fill, rows, cols);
            var keep = new bool[rows, cols];
            int minA = 6, maxA = Math.Max(80, rows * cols * 4 / 5);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int id = labs.id[r, c];
                    if (id <= 0) continue;
                    if (labs.area[id] < minA || labs.area[id] > maxA) continue;
                    keep[r, c] = true;
                }
            }
            var factory = new GeometryFactory();
            var geoms = new List<Geometry>();
            for (int r = 0; r < rows; r++)
            {
                int py0 = minY + r * ph / rows;
                int py1 = minY + (r + 1) * ph / rows;
                if (py1 <= py0) py1 = py0 + 1;
                for (int c = 0; c < cols; c++)
                {
                    if (!keep[r, c]) continue;
                    int px0 = minX + c * pw / cols;
                    int px1 = minX + (c + 1) * pw / cols;
                    if (px1 <= px0) px1 = px0 + 1;
                    double x0 = a + b * px0, x1 = a + b * px1;
                    double yA = c0 + d * py0, yB = c0 + d * py1;
                    double yLo = Math.Min(yA, yB), yHi = Math.Max(yA, yB);
                    double xLo = Math.Min(x0, x1), xHi = Math.Max(x0, x1);
                    geoms.Add(factory.CreatePolygon(factory.CreateLinearRing(new[]
                    {
                        new Coordinate(xLo, yLo),
                        new Coordinate(xHi, yLo),
                        new Coordinate(xHi, yHi),
                        new Coordinate(xLo, yHi),
                        new Coordinate(xLo, yLo)
                    })));
                }
            }
            if (geoms.Count == 0) return null;
            Geometry u = geoms.Count == 1 ? geoms[0] : CascadedPolygonUnion.Union(geoms);
            try { u = u.Buffer(0); } catch { }
            double cell = Math.Max(Math.Abs(b) * pw / (double)cols, Math.Abs(d) * ph / (double)rows);
            if (cell < 4) cell = 4;
            try { u = u.Buffer(cell * 0.9).Buffer(-cell * 0.85); } catch { }
            u = CollapsePixelStairs(u, cell);
            try { u = DouglasPeuckerSimplifier.Simplify(u, Math.Max(8.0, cell * 1.15)); } catch { }
            return u;
        }

        private static bool IsSoftHeatEdge(System.Drawing.Color c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            int min = Math.Min(c.R, Math.Min(c.G, c.B));
            if (max < 80 || max - min < 28) return false;
            return !IsPdfBlackFrame(c);
        }

        private static bool TryBuildExtraRebarMask(
            Bitmap bmp,
            Envelope cadEnv,
            Geometry cadGeom,
            Database db,
            Editor ed,
            Transaction tr,
            BlockTableRecord btr,
            List<StaCadBox> staCadBoxes,
            out bool[,] mask,
            out int cols,
            out int rows,
            out int boxCount,
            out Envelope mappedWorld,
            out List<Box> boxes,
            out int pngMinX,
            out int pngMinY,
            out int pngMaxX,
            out int pngMaxY)
        {
            mask = null;
            mappedWorld = cadEnv;
            boxes = new List<Box>();
            pngMinX = pngMinY = pngMaxX = pngMaxY = 0;
            cols = rows = 0;
            boxCount = 0;
            int w = bmp.Width, h = bmp.Height;
            if (w < 20 || h < 20) return false;

            bool light = LooksLikeLightPdfGraph(bmp, 0, 0, w - 1, h - 1);
            int minX, minY, maxX, maxY;
            bool[,] foundMask;
            int gw, gh;
            if (light && TryFindGrayMeshFoundationExtent(bmp, out minX, out minY, out maxX, out maxY, out foundMask, out gw, out gh))
            {
                ed?.WriteMessage("\nTEMELDONATI: PDF ızgara silueti (resim cercevesi/lejant haric) {0}x{1}px.", maxX - minX + 1, maxY - minY + 1);
            }
            else if (!TryFindFoundationPixelExtent(bmp, out minX, out minY, out maxX, out maxY, out foundMask, out gw, out gh))
            {
                ed?.WriteMessage("\nTEMELDONATI: PNG temel govdesi bulunamadi, mavi mesh zarfina dusuluyor.");
                if (!TryFindMeshBlueBBox(bmp, out minX, out int minYb, out maxX, out int maxYb))
                {
                    if (!TryFindLightSlabBBox(bmp, out minX, out minY, out maxX, out maxY))
                        return false;
                }
                else
                {
                    minY = minYb;
                    maxY = maxYb;
                }
                foundMask = null;
                gw = gh = 0;
            }
            else
                light = light || LooksLikeLightPdfGraph(bmp, minX, minY, maxX, maxY);

            pngMinX = minX; pngMinY = minY; pngMaxX = maxX; pngMaxY = maxY;

            mappedWorld = cadEnv;
            if (TryFitHeatByStaBoxes(bmp, staCadBoxes, minX, minY, maxX, maxY, cadEnv, ed, out Envelope boxEnv))
                mappedWorld = boxEnv;
            ed?.WriteMessage(
                "\nTEMELDONATI hizalama: PDF izgara {0}x{1}px -> CAD {2:0} x {3:0} cm.",
                maxX - minX + 1, maxY - minY + 1, mappedWorld.Width, mappedWorld.Height);

            int pw = maxX - minX + 1, ph = maxY - minY + 1;
            int step = Math.Max(1, (int)Math.Ceiling(Math.Max(pw, ph) / (double)MaxGrid));
            cols = Math.Max(12, pw / step);
            rows = Math.Max(12, ph / step);

            EstimateDefaultBlue(bmp, minX, minY, maxX, maxY, out double br, out double bg, out double bb);
            if (light)
            {
                br = 230; bg = 230; bb = 230;
                ed?.WriteMessage("\nTEMELDONATI: varsayilan zemin acik gri/beyaz (cogunluk = ilave yok).");
            }
            else
                ed?.WriteMessage("\nTEMELDONATI: varsayilan mavi RGB {0:0},{1:0},{2:0} (cogunluk = ilave yok).", br, bg, bb);

            var fill = new bool[rows, cols];
            var ring = new bool[rows, cols];
            var annot = new bool[rows, cols];
            byte[] px; int stride, bw, bh;
            CopyPixels(bmp, out px, out stride, out bw, out bh);
            int defCells = 0, heatCells = 0;
            for (int r = 0; r < rows; r++)
            {
                int y0 = minY + r * ph / rows;
                int y1 = minY + (r + 1) * ph / rows;
                if (y1 <= y0) y1 = y0 + 1;
                int dy = Math.Max(1, (y1 - y0) / 3);
                for (int c = 0; c < cols; c++)
                {
                    int x0 = minX + c * pw / cols;
                    int x1 = minX + (c + 1) * pw / cols;
                    if (x1 <= x0) x1 = x0 + 1;
                    int dx = Math.Max(1, (x1 - x0) / 3);
                    int nDef = 0, nStrong = 0, nRing = 0, nBox = 0;
                    for (int y = y0; y < y1 && y < bh; y += dy)
                    {
                        for (int x = x0; x < x1 && x < bw; x += dx)
                        {
                            var col = PixelAt(px, stride, x, y);
                            if (IsAnnotBoxStroke(col)) nBox++;
                            if (IsBlackBackground(col)) continue;
                            if (!light && IsUiPanel(col)) continue;
                            if (IsMeshGridColor(col) && !light) continue;
                            if (light && IsLightDefault(col)) { nDef++; continue; }
                            if (light)
                            {
                                if (IsLightHeatColor(col)) nStrong++;
                                else if (IsLightOuterColor(col)) nRing++;
                                else nDef++;
                                continue;
                            }
                            if (IsMeshGridColor(col)) continue;
                            if (IsStrongHeatmapColor(col, br, bg, bb)) nStrong++;
                            else if (IsOuterTransitionColor(col, br, bg, bb)) nRing++;
                            else if (IsDefaultFoundationBlue(col, br, bg, bb)) nDef++;
                            else nDef++;
                        }
                    }
                    annot[r, c] = nBox > 0;
                    bool heat;
                    if (light)
                        heat = nStrong >= 2 && nStrong * 4 > nDef;
                    else
                        heat = nStrong >= 2 && nStrong > nDef;
                    fill[r, c] = heat;
                    ring[r, c] = !heat && nRing >= 2 && nRing >= nDef;
                    if (heat) heatCells++;
                    else if (nDef > 0) defCells++;
                }
            }

            boxes = ExtractAnnotBoxes(annot, rows, cols);
            boxCount = boxes.Count;
            var boxFilter = light ? null : boxes;
            int maxBlob = light ? Math.Max(50, cols * rows / 2) : -1;
            mask = KeepCompactHeatBlobs(fill, rows, cols, boxFilter, out int kept, maxBlob);
            if (!light)
                mask = GrowMaskInto(mask, ring, rows, cols, 10);
            kept = CountTrue(mask, rows, cols);
            ed?.WriteMessage("\nTEMELDONATI: default hucre {0}, isi {1}, cikan {2}, kutu {3}.", defCells, heatCells, kept, boxCount);
            return kept >= 8;
        }

        private static int CountTrue(bool[,] m, int rows, int cols)
        {
            int n = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    if (m[r, c]) n++;
            return n;
        }

        private static bool[,] GrowMaskInto(bool[,] seed, bool[,] allow, int rows, int cols, int passes)
        {
            var mask = (bool[,])seed.Clone();
            int start = CountTrue(mask, rows, cols);
            int cap = Math.Max(start * 3 + 8, start + rows * cols / 20);
            for (int pass = 0; pass < passes; pass++)
            {
                var next = (bool[,])mask.Clone();
                int added = 0;
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        if (mask[r, c] || !allow[r, c]) continue;
                        bool near = false;
                        for (int dr = -1; dr <= 1 && !near; dr++)
                        {
                            int rr = r + dr;
                            if (rr < 0 || rr >= rows) continue;
                            for (int dc = -1; dc <= 1; dc++)
                            {
                                int cc = c + dc;
                                if (cc < 0 || cc >= cols) continue;
                                if (mask[rr, cc]) { near = true; break; }
                            }
                        }
                        if (!near) continue;
                        next[r, c] = true;
                        added++;
                    }
                }
                mask = next;
                if (added == 0) break;
                if (CountTrue(mask, rows, cols) > cap) break;
            }
            return mask;
        }

        private static bool[,] KeepCompactHeatBlobs(bool[,] fill, int rows, int cols, List<Box> boxes, out int kept, int maxAreaOverride = -1)
        {
            var mask = new bool[rows, cols];
            kept = 0;
            var labels = LabelComponents(fill, rows, cols);
            int compCount = labels.maxId;
            var hit = new bool[compCount + 1];
            int maxArea = maxAreaOverride > 0 ? maxAreaOverride : Math.Max(20, cols * rows / 10);
            int minArea = 4;

            bool useBoxes = boxes != null && boxes.Count > 0;
            if (useBoxes)
            {
                int pad = Math.Max(2, Math.Min(cols, rows) / 50);
                foreach (var box in boxes)
                {
                    int r0 = Math.Max(0, box.r0 - pad);
                    int r1 = Math.Min(rows - 1, box.r1 + pad);
                    int c0 = Math.Max(0, box.c0 - pad);
                    int c1 = Math.Min(cols - 1, box.c1 + pad);
                    for (int r = r0; r <= r1; r++)
                    {
                        for (int c = c0; c <= c1; c++)
                        {
                            int id = labels.id[r, c];
                            if (id > 0 && labels.area[id] <= maxArea)
                                hit[id] = true;
                        }
                    }
                }
            }

            int marked = 0;
            for (int id = 1; id <= compCount; id++)
            {
                if (labels.area[id] < minArea || labels.area[id] > maxArea) continue;
                if (useBoxes && !hit[id]) continue;
                hit[id] = true;
                marked++;
            }
            if (marked == 0)
            {
                for (int id = 1; id <= compCount; id++)
                {
                    if (labels.area[id] < minArea || labels.area[id] > maxArea) continue;
                    hit[id] = true;
                    marked++;
                }
            }

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int id = labels.id[r, c];
                    if (id <= 0 || !hit[id]) continue;
                    if (labels.area[id] < minArea || labels.area[id] > maxArea) continue;
                    mask[r, c] = true;
                    kept++;
                }
            }
            return mask;
        }

        private static bool TryFindGrayMeshFoundationExtent(
            Bitmap bmp,
            out int minX,
            out int minY,
            out int maxX,
            out int maxY,
            out bool[,] foundMask,
            out int gw,
            out int gh)
        {
            foundMask = null;
            gw = gh = 0;
            minX = minY = maxX = maxY = 0;
            int w = bmp.Width, h = bmp.Height;
            int legendY = FindPdfLegendTop(bmp);
            int step = Math.Max(2, Math.Max(w, h) / 480);
            gw = Math.Max(8, (w + step - 1) / step);
            gh = Math.Max(8, (h + step - 1) / step);
            var lines = new bool[gh, gw];
            int on = 0;
            for (int gy = 0; gy < gh; gy++)
            {
                int y = Math.Min(h - 1, gy * step + step / 2);
                if (y >= legendY) continue;
                for (int gx = 0; gx < gw; gx++)
                {
                    int x = Math.Min(w - 1, gx * step + step / 2);
                    if (!IsPdfGridLine(bmp.GetPixel(x, y)) && !IsLightHeatColor(bmp.GetPixel(x, y)))
                        continue;
                    lines[gy, gx] = true;
                    on++;
                }
            }
            if (on < 80) return false;

            int pitch = EstimateGridPitchPx(bmp, legendY);
            int rad = Math.Max(2, Math.Min(18, (pitch / step + 1) / 2));
            var sealedMesh = Dilate(lines, gh, gw, rad);

            var outside = new bool[gh, gw];
            FloodUnsealedFromBorder(sealedMesh, outside, gh, gw);

            foundMask = new bool[gh, gw];
            int inside = 0;
            int r0 = gh, r1 = -1, c0 = gw, c1 = -1;
            for (int gy = 0; gy < gh; gy++)
            {
                for (int gx = 0; gx < gw; gx++)
                {
                    if (outside[gy, gx]) continue;
                    foundMask[gy, gx] = true;
                    inside++;
                    if (gx < c0) c0 = gx;
                    if (gx > c1) c1 = gx;
                    if (gy < r0) r0 = gy;
                    if (gy > r1) r1 = gy;
                }
            }
            if (inside < 80 || c1 - c0 < 10 || r1 - r0 < 10) return false;

            var labs = LabelComponents(foundMask, gh, gw);
            int best = 0, bestA = 0;
            for (int i = 1; i <= labs.maxId; i++)
            {
                if (labs.area[i] > bestA)
                {
                    bestA = labs.area[i];
                    best = i;
                }
            }
            if (best <= 0) return false;
            foundMask = new bool[gh, gw];
            r0 = gh; r1 = -1; c0 = gw; c1 = -1;
            for (int gy = 0; gy < gh; gy++)
            {
                for (int gx = 0; gx < gw; gx++)
                {
                    if (labs.id[gy, gx] != best) continue;
                    foundMask[gy, gx] = true;
                    if (gx < c0) c0 = gx;
                    if (gx > c1) c1 = gx;
                    if (gy < r0) r0 = gy;
                    if (gy > r1) r1 = gy;
                }
            }

            minX = Math.Max(0, c0 * step);
            minY = Math.Max(0, r0 * step);
            maxX = Math.Min(w - 1, (c1 + 1) * step - 1);
            maxY = Math.Min(legendY - 1, Math.Min(h - 1, (r1 + 1) * step - 1));
            return maxX - minX >= 40 && maxY - minY >= 40;
        }

        private static bool IsPdfGridLine(System.Drawing.Color c)
        {
            int lum = (c.R + c.G + c.B) / 3;
            int chroma = Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
            if (chroma > 28) return false;
            return lum >= 120 && lum <= 247;
        }

        private static int EstimateGridPitchPx(Bitmap bmp, int legendY)
        {
            int w = bmp.Width, h = bmp.Height;
            int y = Math.Min(legendY - 8, h * 45 / 100);
            if (y < 8) y = Math.Min(h / 2, legendY - 4);
            var gaps = new List<int>();
            int last = -1;
            for (int x = 2; x < w - 2; x++)
            {
                if (!IsPdfGridLine(bmp.GetPixel(x, y))) continue;
                if (last >= 0 && x - last >= 4 && x - last <= 80)
                    gaps.Add(x - last);
                last = x;
            }
            if (gaps.Count < 6)
            {
                int x = w * 45 / 100;
                last = -1;
                int yHi = Math.Min(h - 2, legendY - 2);
                for (int yy = 2; yy < yHi; yy++)
                {
                    if (!IsPdfGridLine(bmp.GetPixel(x, yy))) continue;
                    if (last >= 0 && yy - last >= 4 && yy - last <= 80)
                        gaps.Add(yy - last);
                    last = yy;
                }
            }
            if (gaps.Count < 4) return 28;
            gaps.Sort();
            return Math.Max(8, gaps[gaps.Count / 2]);
        }

        private static void FloodUnsealedFromBorder(bool[,] sealedMesh, bool[,] outside, int rows, int cols)
        {
            int cap = rows * cols;
            var qr = new int[cap];
            var qc = new int[cap];
            int tail = 0;
            for (int r = 0; r < rows; r++)
            {
                TryEnqPaper(sealedMesh, outside, qr, qc, ref tail, r, 0, rows, cols);
                TryEnqPaper(sealedMesh, outside, qr, qc, ref tail, r, cols - 1, rows, cols);
            }
            for (int c = 0; c < cols; c++)
            {
                TryEnqPaper(sealedMesh, outside, qr, qc, ref tail, 0, c, rows, cols);
                TryEnqPaper(sealedMesh, outside, qr, qc, ref tail, rows - 1, c, rows, cols);
            }
            int head = 0;
            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };
            while (head < tail)
            {
                int r = qr[head], c = qc[head];
                head++;
                for (int k = 0; k < 4; k++)
                    TryEnqPaper(sealedMesh, outside, qr, qc, ref tail, r + dr[k], c + dc[k], rows, cols);
            }
        }

        private static void TryEnqPaper(bool[,] sealedMesh, bool[,] outside, int[] qr, int[] qc, ref int tail, int r, int c, int rows, int cols)
        {
            if (r < 0 || c < 0 || r >= rows || c >= cols) return;
            if (sealedMesh[r, c] || outside[r, c]) return;
            outside[r, c] = true;
            qr[tail] = r;
            qc[tail] = c;
            tail++;
        }

        private static bool IsPdfMeshOrHeat(System.Drawing.Color c)
        {
            if (IsLightHeatColor(c)) return true;
            int lum = (c.R + c.G + c.B) / 3;
            int chroma = Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
            if (chroma > 36) return false;
            if (lum < 90) return false;
            if (lum >= 252) return false;
            return true;
        }

        private static bool[,] FillConcaveSlab(bool[,] seed, int rows, int cols, int r0, int r1, int c0, int c1)
        {
            var outside = new bool[rows, cols];
            var qr = new int[(r1 - r0 + 3) * (c1 - c0 + 3)];
            var qc = new int[qr.Length];
            int head = 0, tail = 0;
            for (int r = r0; r <= r1; r++)
            {
                TryEnqOutside(seed, outside, qr, qc, ref tail, r, c0, r0, r1, c0, c1);
                TryEnqOutside(seed, outside, qr, qc, ref tail, r, c1, r0, r1, c0, c1);
            }
            for (int c = c0; c <= c1; c++)
            {
                TryEnqOutside(seed, outside, qr, qc, ref tail, r0, c, r0, r1, c0, c1);
                TryEnqOutside(seed, outside, qr, qc, ref tail, r1, c, r0, r1, c0, c1);
            }
            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };
            while (head < tail)
            {
                int r = qr[head], c = qc[head];
                head++;
                for (int k = 0; k < 4; k++)
                {
                    int nr = r + dr[k], nc = c + dc[k];
                    if (nr < r0 || nr > r1 || nc < c0 || nc > c1) continue;
                    TryEnqOutside(seed, outside, qr, qc, ref tail, nr, nc, r0, r1, c0, c1);
                }
            }
            var dst = new bool[rows, cols];
            for (int r = r0; r <= r1; r++)
            {
                for (int c = c0; c <= c1; c++)
                    dst[r, c] = !outside[r, c];
            }
            return dst;
        }

        private static void TryEnqOutside(bool[,] seed, bool[,] outside, int[] qr, int[] qc, ref int tail, int r, int c, int r0, int r1, int c0, int c1)
        {
            if (r < r0 || r > r1 || c < c0 || c > c1) return;
            if (seed[r, c] || outside[r, c]) return;
            outside[r, c] = true;
            qr[tail] = r;
            qc[tail] = c;
            tail++;
        }

        private static int FindPdfLegendTop(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            int y0 = h * 82 / 100;
            int legendTop = h;
            bool saw = false;
            for (int y = h - 1; y >= y0; y -= 2)
            {
                int heat = 0, n = 0;
                for (int x = 0; x < w; x += 4)
                {
                    n++;
                    if (IsLightHeatColor(bmp.GetPixel(x, y))) heat++;
                }
                if (n > 10 && heat * 100 >= n * 16)
                {
                    legendTop = y;
                    saw = true;
                }
                else if (saw)
                    break;
            }
            return saw ? Math.Max(y0, legendTop - 2) : h;
        }

        private static bool IsGrayMeshLine(System.Drawing.Color c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            int min = Math.Min(c.R, Math.Min(c.G, c.B));
            int lum = (c.R + c.G + c.B) / 3;
            if (max - min > 34) return false;
            return lum >= 72 && lum <= 212;
        }

        private static void TrimBottomLegendBand(Bitmap bmp, ref int minX, ref int minY, ref int maxX, ref int maxY)
        {
            int band = Math.Max(8, (maxY - minY + 1) / 16);
            int heat = 0, n = 0;
            for (int y = Math.Max(minY, maxY - band); y <= maxY; y += 2)
            {
                for (int x = minX; x <= maxX; x += 3)
                {
                    n++;
                    if (IsLightHeatColor(bmp.GetPixel(x, y))) heat++;
                }
            }
            if (n > 40 && heat * 4 > n)
                maxY = Math.Max(minY + 50, maxY - band);
        }

        private static bool TryFindFoundationPixelExtent(
            Bitmap bmp,
            out int minX,
            out int minY,
            out int maxX,
            out int maxY,
            out bool[,] foundMask,
            out int gw,
            out int gh)
        {
            foundMask = null;
            gw = gh = 0;
            int w = bmp.Width, h = bmp.Height;
            minX = w; minY = h; maxX = 0; maxY = 0;
            int step = Math.Max(1, Math.Max(w, h) / 720);
            gw = Math.Max(8, (w + step - 1) / step);
            gh = Math.Max(8, (h + step - 1) / step);
            var m = new bool[gh, gw];
            for (int gy = 0; gy < gh; gy++)
            {
                int y = Math.Min(h - 1, gy * step + step / 2);
                for (int gx = 0; gx < gw; gx++)
                {
                    int x = Math.Min(w - 1, gx * step + step / 2);
                    if (IsFoundationBodyPixel(bmp.GetPixel(x, y)))
                        m[gy, gx] = true;
                }
            }
            var labs = LabelComponents(m, gh, gw);
            int best = 0, bestA = 0;
            for (int i = 1; i <= labs.maxId; i++)
            {
                if (labs.area[i] > bestA)
                {
                    bestA = labs.area[i];
                    best = i;
                }
            }
            if (best <= 0 || bestA < 40) return false;

            foundMask = new bool[gh, gw];
            int r0 = gh, r1 = -1, c0 = gw, c1 = -1;
            for (int gy = 0; gy < gh; gy++)
            {
                for (int gx = 0; gx < gw; gx++)
                {
                    if (labs.id[gy, gx] != best) continue;
                    foundMask[gy, gx] = true;
                    if (gx < c0) c0 = gx;
                    if (gx > c1) c1 = gx;
                    if (gy < r0) r0 = gy;
                    if (gy > r1) r1 = gy;
                }
            }
            if (c1 - c0 < 8 || r1 - r0 < 8) return false;

            minX = Math.Max(0, c0 * step);
            minY = Math.Max(0, r0 * step);
            maxX = Math.Min(w - 1, (c1 + 1) * step - 1);
            maxY = Math.Min(h - 1, (r1 + 1) * step - 1);
            return maxX - minX >= 30 && maxY - minY >= 30;
        }

        private static bool IsFoundationBodyPixel(System.Drawing.Color c)
        {
            if (IsBlackBackground(c)) return false;
            if (IsMeshBlue(c) || IsHeatmapFill(c) || IsLightHeatColor(c)) return true;
            if (c.B > 70 && c.G > 50 && c.B + c.G > c.R + 70) return true;
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            int min = Math.Min(c.R, Math.Min(c.G, c.B));
            if (max > 135 && max - min < 55) return true;
            if (max > 85 && max - min < 28) return true;
            return false;
        }

        private static bool LooksLikeLightPdfGraph(Bitmap bmp, int minX, int minY, int maxX, int maxY)
        {
            int light = 0, navy = 0, n = 0;
            int step = Math.Max(3, Math.Max(maxX - minX, maxY - minY) / 80);
            for (int y = minY; y <= maxY; y += step)
            {
                for (int x = minX; x <= maxX; x += step)
                {
                    var c = bmp.GetPixel(x, y);
                    n++;
                    int max = Math.Max(c.R, Math.Max(c.G, c.B));
                    int min = Math.Min(c.R, Math.Min(c.G, c.B));
                    if (max > 160 && max - min < 45) light++;
                    if (IsMeshBlue(c) && c.G < 100 && c.R < 90) navy++;
                }
            }
            return n > 20 && light > navy * 2 && light * 4 > n;
        }

        private static bool TryFindLightSlabBBox(Bitmap bmp, out int minX, out int minY, out int maxX, out int maxY)
        {
            int w = bmp.Width, h = bmp.Height;
            minX = w; minY = h; maxX = 0; maxY = 0;
            for (int y = 0; y < h; y += 2)
            {
                for (int x = 0; x < w; x += 2)
                {
                    if (!IsFoundationBodyPixel(bmp.GetPixel(x, y))) continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
            return maxX - minX >= 30 && maxY - minY >= 30;
        }

        private static bool IsLightDefault(System.Drawing.Color c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            int min = Math.Min(c.R, Math.Min(c.G, c.B));
            if (max < 70) return false;
            return max - min < 38;
        }

        private static bool IsLightHeatColor(System.Drawing.Color c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            int min = Math.Min(c.R, Math.Min(c.G, c.B));
            if (max < 70 || max - min < 40) return false;
            if (IsAnnotBoxStroke(c)) return false;
            return true;
        }

        private static bool IsLightOuterColor(System.Drawing.Color c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            int min = Math.Min(c.R, Math.Min(c.G, c.B));
            if (max - min < 28 || max - min >= 40) return false;
            return c.B > 90 || c.G > 90;
        }

        private static bool IsBlackBackground(System.Drawing.Color c)
        {
            return Math.Max(c.R, Math.Max(c.G, c.B)) < 42;
        }

        private static bool IsUiPanel(System.Drawing.Color c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            int min = Math.Min(c.R, Math.Min(c.G, c.B));
            return max > 178 && max - min < 38;
        }

        private static bool TryFindMeshBlueBBox(Bitmap bmp, out int minX, out int minY, out int maxX, out int maxY)
        {
            int w = bmp.Width, h = bmp.Height;
            minX = w; minY = h; maxX = 0; maxY = 0;
            for (int y = 0; y < h; y += 2)
            {
                for (int x = 0; x < w; x += 2)
                {
                    var col = bmp.GetPixel(x, y);
                    if (IsBlackBackground(col) || IsUiPanel(col)) continue;
                    if (!IsMeshBlue(col) && !IsHeatmapFill(col)) continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
            return maxX - minX >= 30 && maxY - minY >= 30;
        }

        private static void RecalcMaskBBox(bool[,] m, int rows, int cols, ref int r0, ref int r1, ref int c0, ref int c1)
        {
            r0 = rows; r1 = -1; c0 = cols; c1 = -1;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (!m[r, c]) continue;
                    if (r < r0) r0 = r;
                    if (r > r1) r1 = r;
                    if (c < c0) c0 = c;
                    if (c > c1) c1 = c;
                }
            }
        }

        private static Envelope FitOutlineToCad(bool[,] pngMask, int rows, int cols, Envelope cadEnv, Geometry cadGeom)
        {
            if (cadEnv == null || cadGeom == null || cadGeom.IsEmpty) return cadEnv;
            const int n = 44;
            var cadR = RasterizeGeom(cadGeom, cadEnv, n);
            var pngR = ResizeMask(pngMask, rows, cols, n);
            int cadOn = CountTrue(cadR, n, n);
            int pngOn = CountTrue(pngR, n, n);
            if (cadOn < 12 || pngOn < 12) return cadEnv;

            double bestIou = IouAniso(pngR, cadR, n, 1, 1, 0, 0);
            double bsx = 1, bsy = 1, bdx = 0, bdy = 0;
            double[] scales = { 0.84, 0.88, 0.92, 0.95, 0.97, 0.99, 1.0, 1.01, 1.03, 1.06, 1.10, 1.14 };
            for (int i = 0; i < scales.Length; i++)
            {
                double sx = scales[i];
                for (int j = 0; j < scales.Length; j++)
                {
                    double sy = scales[j];
                    for (int ox = -6; ox <= 6; ox++)
                    {
                        for (int oy = -6; oy <= 6; oy++)
                        {
                            double dx = ox / (double)n;
                            double dy = oy / (double)n;
                            double iou = IouAniso(pngR, cadR, n, sx, sy, dx, dy);
                            if (iou > bestIou)
                            {
                                bestIou = iou;
                                bsx = sx; bsy = sy; bdx = dx; bdy = dy;
                            }
                        }
                    }
                }
            }

            for (int k = 0; k < 2; k++)
            {
                double stepS = k == 0 ? 0.015 : 0.007;
                double stepD = 1.0 / n;
                double lsx = bsx, lsy = bsy, ldx = bdx, ldy = bdy;
                for (int isx = -2; isx <= 2; isx++)
                {
                    double sx = lsx + isx * stepS;
                    if (sx < 0.75 || sx > 1.25) continue;
                    for (int isy = -2; isy <= 2; isy++)
                    {
                        double sy = lsy + isy * stepS;
                        if (sy < 0.75 || sy > 1.25) continue;
                        for (int ox = -2; ox <= 2; ox++)
                        {
                            for (int oy = -2; oy <= 2; oy++)
                            {
                                double dx = ldx + ox * stepD;
                                double dy = ldy + oy * stepD;
                                double iou = IouAniso(pngR, cadR, n, sx, sy, dx, dy);
                                if (iou > bestIou)
                                {
                                    bestIou = iou;
                                    bsx = sx; bsy = sy; bdx = dx; bdy = dy;
                                }
                            }
                        }
                    }
                }
            }

            if (bestIou < 0.12) return cadEnv;
            double minX = cadEnv.MinX + bdx * cadEnv.Width;
            double maxY = cadEnv.MaxY - bdy * cadEnv.Height;
            return new Envelope(minX, minX + bsx * cadEnv.Width, maxY - bsy * cadEnv.Height, maxY);
        }

        private static Envelope FitAnisoToCad(bool[,] pngMask, int rows, int cols, Envelope cadEnv, Geometry cadGeom)
        {
            return FitOutlineToCad(pngMask, rows, cols, cadEnv, cadGeom);
        }

        private static double IouAniso(bool[,] png, bool[,] cad, int n, double sx, double sy, double dx, double dy)
        {
            if (sx < 0.2 || sy < 0.2) return 0;
            int inter = 0, uni = 0;
            for (int r = 0; r < n; r++)
            {
                double v = (r + 0.5) / n;
                double vp = (v - dy) / sy;
                int pr = (int)(vp * n);
                for (int c = 0; c < n; c++)
                {
                    double u = (c + 0.5) / n;
                    double up = (u - dx) / sx;
                    int pc = (int)(up * n);
                    bool p = up >= 0 && up < 1 && vp >= 0 && vp < 1 && pr >= 0 && pr < n && pc >= 0 && pc < n && png[pr, pc];
                    bool q = cad[r, c];
                    if (p && q) inter++;
                    if (p || q) uni++;
                }
            }
            return uni == 0 ? 0 : inter / (double)uni;
        }

        private static bool[,] RasterizeGeom(Geometry geom, Envelope env, int n)
        {
            var m = new bool[n, n];
            IPreparedGeometry prep;
            try { prep = PreparedGeometryFactory.Prepare(geom); }
            catch { prep = null; }
            var gf = geom.Factory;
            for (int r = 0; r < n; r++)
            {
                double y = env.MaxY - (r + 0.5) * env.Height / n;
                for (int c = 0; c < n; c++)
                {
                    double x = env.MinX + (c + 0.5) * env.Width / n;
                    var pt = gf.CreatePoint(new Coordinate(x, y));
                    try
                    {
                        m[r, c] = prep != null ? prep.Covers(pt) : geom.Covers(pt);
                    }
                    catch { }
                }
            }
            return m;
        }

        private static bool[,] ResizeMask(bool[,] src, int sr, int sc, int n)
        {
            int r0 = sr, r1 = -1, c0 = sc, c1 = -1;
            RecalcMaskBBox(src, sr, sc, ref r0, ref r1, ref c0, ref c1);
            var dst = new bool[n, n];
            if (r1 < r0 || c1 < c0) return dst;
            int ph = r1 - r0 + 1, pw = c1 - c0 + 1;
            for (int r = 0; r < n; r++)
            {
                int sy = r0 + r * ph / n;
                if (sy > r1) sy = r1;
                for (int c = 0; c < n; c++)
                {
                    int sx = c0 + c * pw / n;
                    if (sx > c1) sx = c1;
                    dst[r, c] = src[sy, sx];
                }
            }
            return dst;
        }

        private static bool IsMeshBlue(System.Drawing.Color c)
        {
            if (c.B < 80) return false;
            return c.B >= c.G + 22 && c.B >= c.R + 28;
        }

        private static bool IsHeatmapFill(System.Drawing.Color c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            int min = Math.Min(c.R, Math.Min(c.G, c.B));
            if (max < 55 || max - min < 22) return false;
            if (IsMeshBlue(c) && c.G < 95) return false;
            if (c.R > 210 && c.G > 210 && c.B > 210) return false;
            bool paleCyan = c.G > 70 && c.B > 90 && c.G > c.R + 8 && c.G + 25 >= c.B;
            bool cyanGreen = c.G > 85 && c.G + 8 >= c.B - 20 && c.G > c.R + 6;
            bool yellow = c.R > 140 && c.G > 140 && c.B < 140 && c.R + c.G > 2 * c.B + 40;
            bool orangeRed = c.R > 130 && c.R > c.B + 25 && c.G < c.R + 20;
            bool magenta = c.R > 140 && c.B > 100 && c.G < 120 && c.R > c.G + 30;
            return paleCyan || cyanGreen || yellow || orangeRed || magenta;
        }

        private static void EstimateDefaultBlue(Bitmap bmp, int minX, int minY, int maxX, int maxY, out double br, out double bg, out double bb)
        {
            long sr = 0, sg = 0, sb = 0;
            int n = 0;
            int step = Math.Max(2, Math.Max(maxX - minX, maxY - minY) / 180);
            for (int y = minY; y <= maxY; y += step)
            {
                for (int x = minX; x <= maxX; x += step)
                {
                    var c = bmp.GetPixel(x, y);
                    if (IsBlackBackground(c) || IsUiPanel(c)) continue;
                    if (c.B < 90) continue;
                    if (c.B < c.G + 18 || c.B < c.R + 22) continue;
                    if (c.G > 130) continue;
                    sr += c.R; sg += c.G; sb += c.B;
                    n++;
                }
            }
            if (n < 8)
            {
                br = 40; bg = 50; bb = 180;
                return;
            }
            br = sr / (double)n;
            bg = sg / (double)n;
            bb = sb / (double)n;
        }

        private static bool IsMeshGridColor(System.Drawing.Color c)
        {
            bool yellow = c.R > 115 && c.G > 115 && Math.Abs(c.R - c.G) < 55 && c.B < 205
                          && (c.R + c.G) > (2 * c.B + 10);
            bool white = c.R > 155 && c.G > 155 && c.B > 145 && Math.Abs(c.R - c.G) < 45 && Math.Abs(c.G - c.B) < 50;
            return yellow || white;
        }

        private static bool IsDefaultFoundationBlue(System.Drawing.Color c, double br, double bg, double bb)
        {
            if (IsMeshGridColor(c)) return false;
            if (c.B < 80) return false;
            if (c.B < c.G + 28) return false;
            if (c.B < c.R + 28) return false;
            if (c.G > bg + 14) return false;
            return true;
        }

        private static bool IsOuterTransitionColor(System.Drawing.Color c, double br, double bg, double bb)
        {
            if (IsMeshGridColor(c) || IsBlackBackground(c) || IsUiPanel(c)) return false;
            if (IsStrongHeatmapColor(c, br, bg, bb)) return false;
            if (c.B < 75) return false;
            if (c.G <= bg + 8 && c.R <= br + 10) return false;
            bool stillCool = c.B + 8 >= c.R && c.B + 25 >= c.G;
            if (!stillCool) return false;
            double dR = c.R - br, dG = c.G - bg, dB = c.B - bb;
            double dist = Math.Sqrt(dR * dR + dG * dG + dB * dB);
            return dist >= 20 && (c.G > bg + 10 || (c.R + c.G) > (br + bg) + 22);
        }

        private static bool IsStrongHeatmapColor(System.Drawing.Color c, double br, double bg, double bb)
        {
            if (IsMeshGridColor(c)) return false;
            if (c.R > 210 && c.G > 210 && c.B > 210) return false;
            if (c.B >= c.G + 28 && c.B >= c.R + 28 && c.G <= bg + 14) return false;
            bool paleCyan = c.G > 88 && c.G > c.R + 12 && c.B > 75 && c.G + 30 >= c.B;
            bool cyan = c.G > 105 && c.G > c.R + 18 && c.B > 80 && c.G + 8 >= c.B * 0.55;
            bool green = c.G > 125 && c.G > c.R + 8 && c.G >= c.B - 15;
            bool orangeRed = c.R > 145 && c.R > c.B + 35 && c.R > c.G;
            bool magenta = c.R > 150 && c.B > 110 && c.G < 110 && c.R > c.G + 40;
            return paleCyan || cyan || green || orangeRed || magenta;
        }

        private static bool IsHeatmapAgainstDefault(System.Drawing.Color c, double br, double bg, double bb)
        {
            return IsStrongHeatmapColor(c, br, bg, bb);
        }

        private static void CopyPixels(Bitmap bmp, out byte[] buf, out int stride, out int w, out int h)
        {
            w = bmp.Width;
            h = bmp.Height;
            using (var conv = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(conv))
                    g.DrawImageUnscaled(bmp, 0, 0);
                var data = conv.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    stride = data.Stride;
                    buf = new byte[Math.Abs(stride) * h];
                    Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                }
                finally
                {
                    conv.UnlockBits(data);
                }
            }
        }

        private static System.Drawing.Color PixelAt(byte[] buf, int stride, int x, int y)
        {
            int i = y * stride + x * 4;
            if (i < 0 || i + 2 >= buf.Length) return System.Drawing.Color.Black;
            return System.Drawing.Color.FromArgb(buf[i + 2], buf[i + 1], buf[i]);
        }

        private static int NeighborBlueCount(byte[] buf, int stride, int w, int h, int x, int y)
        {
            int blueN = 0, n = 0;
            for (int dy = -2; dy <= 2; dy += 2)
            {
                for (int dx = -2; dx <= 2; dx += 2)
                {
                    if (dx == 0 && dy == 0) continue;
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    n++;
                    if (IsMeshBlue(PixelAt(buf, stride, xx, yy))) blueN++;
                }
            }
            return n == 0 ? 0 : blueN * 10 / n;
        }

        private static bool LooksLikeMeshLineColor(System.Drawing.Color c, int blueNeighborTenths)
        {
            bool yellowish = c.R > 130 && c.G > 130 && c.B < 190 && (c.R + c.G) > 2 * c.B + 20;
            if (!yellowish && !(c.R > 160 && c.G > 160 && c.B > 140 && Math.Abs(c.R - c.G) < 40))
                return false;
            return blueNeighborTenths * 2 >= 10;
        }

        private static bool IsAnnotBoxStroke(System.Drawing.Color c)
        {
            if (c.R < 70) return false;
            if (c.G > 155 || c.B > 155) return false;
            return c.R >= c.G + 28 && c.R >= c.B + 28;
        }

        private static bool LooksLikeMeshLine(Bitmap bmp, int x, int y, System.Drawing.Color c)
        {
            bool yellowish = c.R > 130 && c.G > 130 && c.B < 190 && (c.R + c.G) > 2 * c.B + 20;
            if (!yellowish && !(c.R > 160 && c.G > 160 && c.B > 140 && Math.Abs(c.R - c.G) < 40))
                return false;
            int w = bmp.Width, h = bmp.Height;
            int blueN = 0, n = 0;
            for (int dy = -2; dy <= 2; dy += 2)
            {
                for (int dx = -2; dx <= 2; dx += 2)
                {
                    if (dx == 0 && dy == 0) continue;
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    n++;
                    if (IsMeshBlue(bmp.GetPixel(xx, yy))) blueN++;
                }
            }
            return n > 0 && blueN * 2 >= n;
        }

        private static bool[,] OpenMask(bool[,] src, int rows, int cols, int radius)
        {
            return Dilate(Erode(src, rows, cols, radius), rows, cols, radius);
        }

        private static bool[,] CloseMask(bool[,] src, int rows, int cols, int radius)
        {
            return Erode(Dilate(src, rows, cols, radius), rows, cols, radius);
        }

        private static bool[,] Erode(bool[,] src, int rows, int cols, int radius)
        {
            var dst = new bool[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (!src[r, c]) continue;
                    bool ok = true;
                    for (int dr = -radius; dr <= radius && ok; dr++)
                    {
                        int rr = r + dr;
                        if (rr < 0 || rr >= rows) { ok = false; break; }
                        for (int dc = -radius; dc <= radius; dc++)
                        {
                            int cc = c + dc;
                            if (cc < 0 || cc >= cols || !src[rr, cc]) { ok = false; break; }
                        }
                    }
                    dst[r, c] = ok;
                }
            }
            return dst;
        }

        private static bool[,] Dilate(bool[,] src, int rows, int cols, int radius)
        {
            var dst = new bool[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (!src[r, c]) continue;
                    for (int dr = -radius; dr <= radius; dr++)
                    {
                        int rr = r + dr;
                        if (rr < 0 || rr >= rows) continue;
                        for (int dc = -radius; dc <= radius; dc++)
                        {
                            int cc = c + dc;
                            if (cc < 0 || cc >= cols) continue;
                            dst[rr, cc] = true;
                        }
                    }
                }
            }
            return dst;
        }

        private struct Box
        {
            public int r0, r1, c0, c1;
        }

        private static List<Box> ExtractAnnotBoxes(bool[,] annot, int rows, int cols)
        {
            var labs = LabelComponents(annot, rows, cols);
            var boxes = new List<Box>();
            var r0 = new int[labs.maxId + 1];
            var r1 = new int[labs.maxId + 1];
            var c0 = new int[labs.maxId + 1];
            var c1 = new int[labs.maxId + 1];
            for (int i = 1; i <= labs.maxId; i++)
            {
                r0[i] = rows; r1[i] = -1; c0[i] = cols; c1[i] = -1;
            }
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int id = labs.id[r, c];
                    if (id <= 0) continue;
                    if (r < r0[id]) r0[id] = r;
                    if (r > r1[id]) r1[id] = r;
                    if (c < c0[id]) c0[id] = c;
                    if (c > c1[id]) c1[id] = c;
                }
            }
            int minSide = Math.Max(4, Math.Min(rows, cols) / 55);
            int maxSide = Math.Max(rows, cols) * 9 / 10;
            for (int id = 1; id <= labs.maxId; id++)
            {
                if (labs.area[id] < 8) continue;
                int bw = c1[id] - c0[id] + 1;
                int bh = r1[id] - r0[id] + 1;
                if (bw < minSide || bh < minSide) continue;
                if (bw > maxSide && bh > maxSide) continue;
                int boxArea = bw * bh;
                if (boxArea <= 0) continue;
                double fill = labs.area[id] / (double)boxArea;
                if (fill > 0.55) continue;
                boxes.Add(new Box { r0 = r0[id], r1 = r1[id], c0 = c0[id], c1 = c1[id] });
            }
            return boxes;
        }

        private static (int[,] id, int[] area, int maxId) LabelComponents(bool[,] mask, int rows, int cols, bool eightWay = false)
        {
            var id = new int[rows, cols];
            var area = new List<int> { 0 };
            int cur = 0;
            var qr = new int[rows * cols];
            var qc = new int[rows * cols];
            int[] dr = eightWay
                ? new[] { -1, -1, -1, 0, 0, 1, 1, 1 }
                : new[] { -1, 1, 0, 0 };
            int[] dc = eightWay
                ? new[] { -1, 0, 1, -1, 1, -1, 0, 1 }
                : new[] { 0, 0, -1, 1 };
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (!mask[r, c] || id[r, c] != 0) continue;
                    cur++;
                    int head = 0, tail = 0;
                    qr[tail] = r; qc[tail] = c; tail++;
                    id[r, c] = cur;
                    int a = 0;
                    while (head < tail)
                    {
                        int rr = qr[head], cc = qc[head];
                        head++;
                        a++;
                        for (int k = 0; k < dr.Length; k++)
                        {
                            int nr = rr + dr[k], nc = cc + dc[k];
                            if (nr < 0 || nc < 0 || nr >= rows || nc >= cols) continue;
                            if (!mask[nr, nc] || id[nr, nc] != 0) continue;
                            id[nr, nc] = cur;
                            qr[tail] = nr; qc[tail] = nc; tail++;
                        }
                    }
                    area.Add(a);
                }
            }
            return (id, area.ToArray(), cur);
        }

        private static Geometry BuildSmoothedUnion(bool[,] mask, int cols, int rows, Envelope env)
        {
            var factory = new GeometryFactory();
            var geoms = new List<Geometry>();
            double cellW = env.Width / cols;
            double cellH = env.Height / rows;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (!mask[r, c]) continue;
                    double x0 = env.MinX + c * cellW;
                    double x1 = x0 + cellW;
                    double y1 = env.MaxY - r * cellH;
                    double y0 = y1 - cellH;
                    var ring = factory.CreateLinearRing(new[]
                    {
                        new Coordinate(x0, y0),
                        new Coordinate(x1, y0),
                        new Coordinate(x1, y1),
                        new Coordinate(x0, y1),
                        new Coordinate(x0, y0)
                    });
                    geoms.Add(factory.CreatePolygon(ring));
                }
            }
            if (geoms.Count == 0) return null;
            Geometry u = geoms.Count == 1 ? geoms[0] : CascadedPolygonUnion.Union(geoms);
            try { u = u.Buffer(0); } catch { }
            double sm = Math.Max(cellW, cellH);
            try { u = u.Buffer(sm * 0.6).Buffer(-sm * 0.55); } catch { }
            try { u = DouglasPeuckerSimplifier.Simplify(u, sm * 1.25); } catch { }
            u = CollapsePixelStairs(u, sm);
            try { u = DouglasPeuckerSimplifier.Simplify(u, sm * 0.7); } catch { }
            return u;
        }

        private static Geometry CollapsePixelStairs(Geometry g, double cell)
        {
            if (g == null || g.IsEmpty) return g;
            var f = g.Factory;
            if (g is Polygon pg)
            {
                var ext = CollapseRing(pg.ExteriorRing, cell);
                if (ext == null || ext.Length < 4) return pg;
                var holes = new List<LinearRing>();
                for (int i = 0; i < pg.NumInteriorRings; i++)
                {
                    var h = CollapseRing(pg.GetInteriorRingN(i), cell);
                    if (h != null && h.Length >= 4)
                        holes.Add(f.CreateLinearRing(h));
                }
                try { return f.CreatePolygon(f.CreateLinearRing(ext), holes.ToArray()); }
                catch { return pg; }
            }
            if (g is GeometryCollection gc)
            {
                var parts = new List<Geometry>();
                for (int i = 0; i < gc.NumGeometries; i++)
                {
                    Geometry p = CollapsePixelStairs(gc.GetGeometryN(i), cell);
                    if (p != null && !p.IsEmpty) parts.Add(p);
                }
                if (parts.Count == 0) return g;
                if (parts.Count == 1) return parts[0];
                return f.BuildGeometry(parts);
            }
            return g;
        }

        private static Coordinate[] CollapseRing(LineString ring, double cell)
        {
            if (ring == null || ring.NumPoints < 4) return null;
            var pts = new List<Coordinate>(ring.NumPoints);
            for (int i = 0; i < ring.NumPoints; i++)
                pts.Add(ring.GetCoordinateN(i).Copy());
            if (pts.Count >= 2 && pts[0].Equals2D(pts[pts.Count - 1]))
                pts.RemoveAt(pts.Count - 1);
            double maxStep = cell * 2.5;
            for (int pass = 0; pass < 48; pass++)
            {
                bool changed = false;
                for (int i = 0; i < pts.Count; i++)
                {
                    var A = pts[(i - 1 + pts.Count) % pts.Count];
                    var B = pts[i];
                    var C = pts[(i + 1) % pts.Count];
                    double dx1 = B.X - A.X, dy1 = B.Y - A.Y;
                    double dx2 = C.X - B.X, dy2 = C.Y - B.Y;
                    double l1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
                    double l2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
                    if (l1 < 0.15 || l2 < 0.15) { pts.RemoveAt(i); changed = true; break; }
                    double turn = VertexTurnDeg(dx1, dy1, dx2, dy2);
                    if (turn < 2.5 && l1 + l2 < cell * 12)
                    {
                        pts.RemoveAt(i);
                        changed = true;
                        break;
                    }
                    if (l1 > maxStep || l2 > maxStep) continue;
                    bool h1 = Math.Abs(dy1) <= 0.22 * l1 && Math.Abs(dx1) >= 0.78 * l1;
                    bool v1 = Math.Abs(dx1) <= 0.22 * l1 && Math.Abs(dy1) >= 0.78 * l1;
                    bool h2 = Math.Abs(dy2) <= 0.22 * l2 && Math.Abs(dx2) >= 0.78 * l2;
                    bool v2 = Math.Abs(dx2) <= 0.22 * l2 && Math.Abs(dy2) >= 0.78 * l2;
                    if ((h1 && v2) || (v1 && h2))
                    {
                        pts.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
                if (!changed) break;
            }
            if (pts.Count < 3) return null;
            pts.Add(pts[0].Copy());
            return pts.ToArray();
        }

        private static double VertexTurnDeg(double dx1, double dy1, double dx2, double dy2)
        {
            double n1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
            double n2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
            if (n1 < 1e-9 || n2 < 1e-9) return 0;
            double dot = (dx1 * dx2 + dy1 * dy2) / (n1 * n2);
            if (dot > 1) dot = 1;
            if (dot < -1) dot = -1;
            return Math.Acos(dot) * 180.0 / Math.PI;
        }

        private static int DrawRings(Transaction tr, BlockTableRecord btr, Geometry geom)
        {
            return DrawRings(tr, btr, geom, LayerName);
        }

        private static int DrawRings(Transaction tr, BlockTableRecord btr, Geometry geom, string layer)
        {
            int n = 0;
            if (geom is Polygon pg)
                n += DrawPolygon(tr, btr, pg, layer);
            else if (geom is GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                    n += DrawRings(tr, btr, gc.GetGeometryN(i), layer);
            }
            return n;
        }

        private static int DrawPolygon(Transaction tr, BlockTableRecord btr, Polygon pg)
        {
            return DrawPolygon(tr, btr, pg, LayerName);
        }

        private static int DrawPolygon(Transaction tr, BlockTableRecord btr, Polygon pg, string layer)
        {
            if (pg == null || pg.IsEmpty || pg.Area < 40) return 0;
            DrawRing(tr, btr, pg.ExteriorRing, layer);
            return 1;
        }

        private static int DrawStaBoxesAndLabels(
            Bitmap bmp,
            List<Box> boxes,
            int minX, int minY, int maxX, int maxY,
            int cols, int rows,
            Envelope env,
            Transaction tr,
            BlockTableRecord btr,
            Editor ed,
            out int nTxt)
        {
            nTxt = 0;
            if (env == null || bmp == null) return 0;
            int pw = maxX - minX + 1, ph = maxY - minY + 1;
            if (pw < 8 || ph < 8) return 0;
            byte[] px; int stride, bw, bh;
            CopyPixels(bmp, out px, out stride, out bw, out bh);
            var pixBoxes = ExtractRedBoxesPixels(px, stride, bw, bh, minX, minY, maxX, maxY);
            if (pixBoxes.Count == 0 && boxes != null)
            {
                foreach (var box in boxes)
                {
                    pixBoxes.Add(new PixBox
                    {
                        x0 = minX + box.c0 * pw / cols,
                        x1 = minX + (box.c1 + 1) * pw / cols,
                        y0 = minY + box.r0 * ph / rows,
                        y1 = minY + (box.r1 + 1) * ph / rows
                    });
                }
            }

            int n = 0;
            foreach (var box in pixBoxes)
            {
                int x0p = Math.Max(0, box.x0);
                int x1p = Math.Min(bw - 1, box.x1);
                int y0p = Math.Max(0, box.y0);
                int y1p = Math.Min(bh - 1, box.y1);
                if (x1p - x0p < 16 || y1p - y0p < 16) continue;

                double x0 = env.MinX + (x0p - minX) * env.Width / pw;
                double x1 = env.MinX + (x1p - minX) * env.Width / pw;
                double yTop = env.MaxY - (y0p - minY) * env.Height / ph;
                double yBot = env.MaxY - (y1p - minY) * env.Height / ph;
                if (x1 - x0 < 8 || yTop - yBot < 8) continue;

                var pl = new Polyline { Layer = LayerBoxName, Closed = true };
                pl.AddVertexAt(0, new Point2d(x0, yBot), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(x1, yBot), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(x1, yTop), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(x0, yTop), 0, 0, 0);
                btr.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                n++;

                string raw;
                string label = TryReadPozLabel(ed, px, stride, bw, bh, x0p, y0p, x1p, y1p, out raw);
                ed?.WriteMessage("\nTEMELDONATI OCR: {0} -> {1}", string.IsNullOrEmpty(raw) ? "(bos)" : raw, label ?? "-");
                if (string.IsNullOrEmpty(label)) continue;

                double hTxt = Math.Max(16.0, Math.Min(28.0, (yTop - yBot) * 0.1));
                var txt = new DBText
                {
                    Layer = LayerTextName,
                    Height = hTxt,
                    WidthFactor = 0.85,
                    TextString = label,
                    Position = new Point3d(x0, yTop + hTxt * 0.2, 0)
                };
                btr.AppendEntity(txt);
                tr.AddNewlyCreatedDBObject(txt, true);
                nTxt++;
            }
            return n;
        }

        private struct PixBox
        {
            public int x0, y0, x1, y1;
        }

        private static bool TryFitHeatByStaBoxes(
            Bitmap bmp,
            List<StaCadBox> staCad,
            int minX, int minY, int maxX, int maxY,
            Envelope cadEnv,
            Editor ed,
            out Envelope mapped)
        {
            mapped = cadEnv;
            if (bmp == null || staCad == null || staCad.Count == 0) return false;
            var pix = ExtractBlackFrameBoxes(bmp);
            if (pix.Count == 0)
            {
                ed?.WriteMessage("\nTEMELDONATI: PDF siyah kutu bulunamadi, temel zarfi kullanildi.");
                return false;
            }
            var pairs = PairPixToCadBoxes(pix, staCad);
            if (pairs.Count == 0)
            {
                ed?.WriteMessage("\nTEMELDONATI: kutu eslesmesi yok ({0} resim / {1} STA).", pix.Count, staCad.Count);
                return false;
            }

            var px = new List<double>();
            var py = new List<double>();
            var cx = new List<double>();
            var cy = new List<double>();
            for (int i = 0; i < pairs.Count; i++)
            {
                var p = pairs[i].pix;
                var c = pairs[i].cad;
                AddCorner(px, py, cx, cy, p.x0, p.y1, c.MinX, c.MinY);
                AddCorner(px, py, cx, cy, p.x1, p.y1, c.MaxX, c.MinY);
                AddCorner(px, py, cx, cy, p.x1, p.y0, c.MaxX, c.MaxY);
                AddCorner(px, py, cx, cy, p.x0, p.y0, c.MinX, c.MaxY);
            }
            if (!FitLine(px, cx, out double a, out double b) || Math.Abs(b) < 1e-9) return false;
            if (!FitLine(py, cy, out double c0, out double d) || Math.Abs(d) < 1e-9) return false;

            double xA = a + b * minX, xB = a + b * maxX;
            double yA = c0 + d * minY, yB = c0 + d * maxY;
            mapped = new Envelope(Math.Min(xA, xB), Math.Max(xA, xB), Math.Min(yA, yB), Math.Max(yA, yB));
            if (mapped.Width < 50 || mapped.Height < 50) return false;
            ed?.WriteMessage("\nTEMELDONATI: isi {0} kutu ile kilitlendi (STA kutu = PDF kutu). cm/px X={1:0.000} Y={2:0.000}.",
                pairs.Count, b, d);
            return true;
        }

        private static void AddCorner(List<double> px, List<double> py, List<double> cx, List<double> cy,
            double ix, double iy, double wx, double wy)
        {
            px.Add(ix); py.Add(iy); cx.Add(wx); cy.Add(wy);
        }

        private static bool FitLine(List<double> u, List<double> v, out double a, out double b)
        {
            a = 0; b = 0;
            int n = u.Count;
            if (n < 2) return false;
            double su = 0, sv = 0, suu = 0, suv = 0;
            for (int i = 0; i < n; i++)
            {
                su += u[i];
                sv += v[i];
                suu += u[i] * u[i];
                suv += u[i] * v[i];
            }
            double den = n * suu - su * su;
            if (Math.Abs(den) < 1e-6) return false;
            b = (n * suv - su * sv) / den;
            a = (sv - b * su) / n;
            return true;
        }

        private static List<(PixBox pix, StaCadBox cad)> PairPixToCadBoxes(List<PixBox> pix, List<StaCadBox> cad)
        {
            var used = new bool[pix.Count];
            var pairs = new List<(PixBox, StaCadBox)>();
            double cadMinX = double.MaxValue, cadMaxX = double.MinValue, cadMinY = double.MaxValue, cadMaxY = double.MinValue;
            for (int i = 0; i < cad.Count; i++)
            {
                if (cad[i].MinX < cadMinX) cadMinX = cad[i].MinX;
                if (cad[i].MaxX > cadMaxX) cadMaxX = cad[i].MaxX;
                if (cad[i].MinY < cadMinY) cadMinY = cad[i].MinY;
                if (cad[i].MaxY > cadMaxY) cadMaxY = cad[i].MaxY;
            }
            double cadW = Math.Max(1, cadMaxX - cadMinX);
            double cadH = Math.Max(1, cadMaxY - cadMinY);
            int pMinX = int.MaxValue, pMaxX = 0, pMinY = int.MaxValue, pMaxY = 0;
            for (int i = 0; i < pix.Count; i++)
            {
                if (pix[i].x0 < pMinX) pMinX = pix[i].x0;
                if (pix[i].x1 > pMaxX) pMaxX = pix[i].x1;
                if (pix[i].y0 < pMinY) pMinY = pix[i].y0;
                if (pix[i].y1 > pMaxY) pMaxY = pix[i].y1;
            }
            double pW = Math.Max(1, pMaxX - pMinX);
            double pH = Math.Max(1, pMaxY - pMinY);

            for (int ci = 0; ci < cad.Count; ci++)
            {
                double ccx = (0.5 * (cad[ci].MinX + cad[ci].MaxX) - cadMinX) / cadW;
                double ccy = (0.5 * (cad[ci].MinY + cad[ci].MaxY) - cadMinY) / cadH;
                double cAspect = (cad[ci].MaxX - cad[ci].MinX) / Math.Max(1, cad[ci].MaxY - cad[ci].MinY);
                int best = -1;
                double bestD = 0.22;
                for (int pi = 0; pi < pix.Count; pi++)
                {
                    if (used[pi]) continue;
                    double pcx = (0.5 * (pix[pi].x0 + pix[pi].x1) - pMinX) / pW;
                    double pcy = 1.0 - (0.5 * (pix[pi].y0 + pix[pi].y1) - pMinY) / pH;
                    double pAspect = (pix[pi].x1 - pix[pi].x0) / Math.Max(1.0, pix[pi].y1 - pix[pi].y0);
                    double d = Math.Abs(pcx - ccx) + Math.Abs(pcy - ccy) + 0.15 * Math.Abs(Math.Log(Math.Max(0.05, pAspect / cAspect)));
                    if (d < bestD)
                    {
                        bestD = d;
                        best = pi;
                    }
                }
                if (best < 0) continue;
                used[best] = true;
                pairs.Add((pix[best], cad[ci]));
            }
            return pairs;
        }

        private static List<PixBox> ExtractBlackFrameBoxes(Bitmap bmp)
        {
            int bw = bmp.Width, bh = bmp.Height;
            int step = Math.Max(1, Math.Max(bw, bh) / 900);
            int gw = (bw + step - 1) / step, gh = (bh + step - 1) / step;
            var m = new bool[gh, gw];
            for (int gy = 0; gy < gh; gy++)
            {
                int y = Math.Min(bh - 1, gy * step);
                for (int gx = 0; gx < gw; gx++)
                {
                    int x = Math.Min(bw - 1, gx * step);
                    if (IsPdfBlackFrame(bmp.GetPixel(x, y)))
                        m[gy, gx] = true;
                }
            }
            m = Dilate(m, gh, gw, 2);
            var labs = LabelComponents(m, gh, gw);
            var list = new List<PixBox>();
            var r0a = new int[labs.maxId + 1];
            var r1a = new int[labs.maxId + 1];
            var c0a = new int[labs.maxId + 1];
            var c1a = new int[labs.maxId + 1];
            for (int i = 1; i <= labs.maxId; i++)
            {
                r0a[i] = gh; r1a[i] = -1; c0a[i] = gw; c1a[i] = -1;
            }
            for (int r = 0; r < gh; r++)
            {
                for (int c = 0; c < gw; c++)
                {
                    int id = labs.id[r, c];
                    if (id <= 0) continue;
                    if (r < r0a[id]) r0a[id] = r;
                    if (r > r1a[id]) r1a[id] = r;
                    if (c < c0a[id]) c0a[id] = c;
                    if (c > c1a[id]) c1a[id] = c;
                }
            }
            for (int id = 1; id <= labs.maxId; id++)
            {
                if (labs.area[id] < 20) continue;
                int boxW = c1a[id] - c0a[id] + 1;
                int boxH = r1a[id] - r0a[id] + 1;
                if (boxW < 8 || boxH < 8) continue;
                if (boxW > gw * 8 / 10 && boxH > gh * 8 / 10) continue;
                double fill = labs.area[id] / (double)Math.Max(1, boxW * boxH);
                if (fill > 0.48) continue;
                list.Add(new PixBox
                {
                    x0 = c0a[id] * step,
                    y0 = r0a[id] * step,
                    x1 = Math.Min(bw - 1, (c1a[id] + 1) * step - 1),
                    y1 = Math.Min(bh - 1, (r1a[id] + 1) * step - 1)
                });
            }
            return list;
        }

        private static bool IsPdfBlackFrame(System.Drawing.Color c)
        {
            int chroma = Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
            int lum = (c.R + c.G + c.B) / 3;
            if (chroma > 38) return false;
            return lum <= 58;
        }

        private static List<PixBox> ExtractRedBoxesPixels(byte[] buf, int stride, int bw, int bh, int minX, int minY, int maxX, int maxY)
        {
            int x0 = Math.Max(0, minX), y0 = Math.Max(0, minY);
            int x1 = Math.Min(bw - 1, maxX), y1 = Math.Min(bh - 1, maxY);
            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            var red = new bool[h, w];
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (IsAnnotBoxStroke(PixelAt(buf, stride, x, y)))
                        red[y - y0, x - x0] = true;
                }
            }
            red = Dilate(red, h, w, 3);
            var labs = LabelComponents(red, h, w);
            var list = new List<PixBox>();
            var r0a = new int[labs.maxId + 1];
            var r1a = new int[labs.maxId + 1];
            var c0a = new int[labs.maxId + 1];
            var c1a = new int[labs.maxId + 1];
            for (int i = 1; i <= labs.maxId; i++)
            {
                r0a[i] = h; r1a[i] = -1; c0a[i] = w; c1a[i] = -1;
            }
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    int id = labs.id[r, c];
                    if (id <= 0) continue;
                    if (r < r0a[id]) r0a[id] = r;
                    if (r > r1a[id]) r1a[id] = r;
                    if (c < c0a[id]) c0a[id] = c;
                    if (c > c1a[id]) c1a[id] = c;
                }
            }
            for (int id = 1; id <= labs.maxId; id++)
            {
                if (labs.area[id] < 40) continue;
                int bwBox = c1a[id] - c0a[id] + 1;
                int bhBox = r1a[id] - r0a[id] + 1;
                if (bwBox < 18 || bhBox < 18) continue;
                if (bwBox > w * 9 / 10 && bhBox > h * 9 / 10) continue;
                double fill = labs.area[id] / (double)Math.Max(1, bwBox * bhBox);
                if (fill > 0.50) continue;
                list.Add(new PixBox
                {
                    x0 = x0 + c0a[id] + 3,
                    y0 = y0 + r0a[id] + 3,
                    x1 = x0 + c1a[id] - 3,
                    y1 = y0 + r1a[id] - 3
                });
            }
            return list;
        }

        private static string TryReadPozLabel(Editor ed, byte[] buf, int stride, int bw, int bh, int x0, int y0, int x1, int y1, out string raw)
        {
            raw = null;
            int boxW = Math.Max(8, x1 - x0);
            int[][] crops =
            {
                new[] { Math.Max(0, x0 - 14), Math.Max(0, y0 - 72), Math.Min(bw, x0 + Math.Max(280, boxW * 3 / 4)), Math.Min(bh, y0 + 14) },
                new[] { Math.Max(0, x0 - 8), Math.Max(0, y0 - 16), Math.Min(bw, x0 + Math.Max(280, boxW * 3 / 4)), Math.Min(bh, y0 + 40) }
            };
            string lastWhy = null;
            foreach (var t in crops)
            {
                if (t[2] - t[0] < 16 || t[3] - t[1] < 6) continue;
                string r, why;
                string s = ReadPozByFindingFi(buf, stride, bw, bh, t[0], t[1], t[2], t[3], out r, out why);
                if (!string.IsNullOrEmpty(why)) lastWhy = why;
                if (!string.IsNullOrEmpty(r) && string.IsNullOrEmpty(raw)) raw = r;
                if (!string.IsNullOrEmpty(s)) { raw = r; return s; }
            }
            if (string.IsNullOrEmpty(raw)) raw = lastWhy;
            return null;
        }

        /// <summary>
        /// STA poz yazisi beyaz degil: lacivert zemin uzerinde acik lavanta-mavi (or. 210,207,247).
        /// </summary>
        private static bool IsWhiteLabelPixel(System.Drawing.Color c)
        {
            if (IsAnnotBoxStroke(c)) return false;
            if (c.R > 140 && c.G > 140 && c.B < 175 && (c.R + c.G) > 2 * c.B + 15)
                return false;
            if (c.B < 170) return false;
            if (c.R < 95 || c.G < 95) return false;
            return (c.R + c.G) >= 210;
        }

        private struct Glyph
        {
            public bool[,] m;
            public int rows, cols, x0, x1;
        }

        /// <summary>
        /// STA etiketi daima adet + Ø + çap / aralık. Önce fi (Ø) bulunur; soldaki rakamlar adet, sağdakiler çap ve aralıktır.
        /// </summary>
        private static string ReadPozByFindingFi(byte[] buf, int stride, int bw, int bh, int x0, int y0, int x1, int y1, out string raw, out string why)
        {
            raw = null;
            why = null;
            bool[,] ink = BuildInkMask(buf, stride, bw, bh, x0, y0, x1, y1, out int ih, out int iw);
            if (ink == null)
            {
                why = "yazi pikseli yok (lavanta-mavi arandi)";
                return null;
            }
            ink = UpscaleMask(ink, ih, iw, 4, out ih, out iw);
            var glyphs = SplitGlyphsByColumns(ink, ih, iw);
            glyphs = FilterLineGlyphs(glyphs);
            if (glyphs.Count < 4)
            {
                why = "harf=" + glyphs.Count;
                return null;
            }

            int fi = -1;
            double bestFi = 0;
            int i0 = 1, i1 = glyphs.Count - 2;
            if (i1 < i0) { i0 = 0; i1 = glyphs.Count - 1; }
            for (int i = i0; i <= i1; i++)
            {
                double sc = FiScore(glyphs[i]);
                if (sc > bestFi)
                {
                    bestFi = sc;
                    fi = i;
                }
            }
            if (fi < 0 || bestFi < 0.18)
            {
                why = "fi yok harf=" + glyphs.Count;
                return null;
            }

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < glyphs.Count; i++)
            {
                if (i == fi) sb.Append('Ø');
                else if (LooksLikeSlash(glyphs[i])) sb.Append('/');
                else sb.Append(ClassifyDigit(glyphs[i]));
            }
            raw = sb.ToString();
            string assembled = AssembleFromFi(glyphs, fi);
            if (assembled == null) why = raw + " (format yok)";
            return assembled;
        }

        private static string AssembleFromFi(List<Glyph> glyphs, int fi)
        {
            var left = new System.Text.StringBuilder();
            for (int i = 0; i < fi; i++)
                left.Append(ClassifyDigit(glyphs[i]));
            int slash = -1;
            for (int i = fi + 1; i < glyphs.Count; i++)
            {
                if (LooksLikeSlash(glyphs[i])) { slash = i; break; }
            }
            var dia = new System.Text.StringBuilder();
            var spc = new System.Text.StringBuilder();
            if (slash > fi)
            {
                for (int i = fi + 1; i < slash; i++)
                    dia.Append(ClassifyDigit(glyphs[i]));
                for (int i = slash + 1; i < glyphs.Count; i++)
                    spc.Append(ClassifyDigit(glyphs[i]));
            }
            else
            {
                for (int i = fi + 1; i < glyphs.Count; i++)
                    dia.Append(ClassifyDigit(glyphs[i]));
                string d = dia.ToString();
                if (d.Length >= 4)
                {
                    spc.Append(d.Substring(2));
                    dia.Clear();
                    dia.Append(d.Substring(0, 2));
                }
            }

            string count = Regex.Replace(left.ToString(), @"\D", "");
            string bar = Regex.Replace(dia.ToString(), @"\D", "");
            string spacing = Regex.Replace(spc.ToString(), @"\D", "");
            if (count.Length < 1 || count.Length > 3) return null;
            if (bar.Length == 1) bar = "0" + bar;
            if (bar.Length > 2) bar = bar.Substring(0, 2);
            if (spacing.Length < 2 || spacing.Length > 3) return null;
            if (!IsBar(bar)) return null;
            int nCount;
            if (!int.TryParse(count, out nCount) || nCount < 1 || nCount > 400) return null;
            return count + "Ø" + bar + "/" + spacing;
        }

        private static bool[,] BuildInkMask(byte[] buf, int stride, int bw, int bh, int x0, int y0, int x1, int y1, out int h, out int w)
        {
            h = 0; w = 0;
            int cw = x1 - x0, ch = y1 - y0;
            if (cw < 16 || ch < 8) return null;
            var tmp = new bool[ch, cw];
            int on = 0, minR = ch, maxR = -1, minC = cw, maxC = -1;
            for (int y = 0; y < ch; y++)
            {
                int py = y0 + y;
                if (py < 0 || py >= bh) continue;
                for (int x = 0; x < cw; x++)
                {
                    int px = x0 + x;
                    if (px < 0 || px >= bw) continue;
                    if (!IsWhiteLabelPixel(PixelAt(buf, stride, px, py))) continue;
                    tmp[y, x] = true;
                    on++;
                    if (y < minR) minR = y;
                    if (y > maxR) maxR = y;
                    if (x < minC) minC = x;
                    if (x > maxC) maxC = x;
                }
            }
            if (on < 10 || maxR < minR || maxC < minC) return null;
            minR = Math.Max(0, minR - 1);
            maxR = Math.Min(ch - 1, maxR + 1);
            minC = Math.Max(0, minC - 1);
            maxC = Math.Min(cw - 1, maxC + 1);
            h = maxR - minR + 1;
            w = maxC - minC + 1;
            var ink = new bool[h, w];
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                    ink[r, c] = tmp[minR + r, minC + c];
            return ink;
        }

        private static bool[,] UpscaleMask(bool[,] src, int rows, int cols, int k, out int nr, out int nc)
        {
            nr = rows * k;
            nc = cols * k;
            var dst = new bool[nr, nc];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (!src[r, c]) continue;
                    for (int dr = 0; dr < k; dr++)
                        for (int dc = 0; dc < k; dc++)
                            dst[r * k + dr, c * k + dc] = true;
                }
            return dst;
        }

        private static List<Glyph> SplitGlyphsByColumns(bool[,] ink, int rows, int cols)
        {
            var coln = new int[cols];
            int mx = 0;
            for (int c = 0; c < cols; c++)
            {
                int n = 0;
                for (int r = 0; r < rows; r++)
                    if (ink[r, c]) n++;
                coln[c] = n;
                if (n > mx) mx = n;
            }
            int thr = Math.Max(1, mx / 8);
            var glyphs = new List<Glyph>();
            int c0 = -1;
            for (int c = 0; c <= cols; c++)
            {
                bool on = c < cols && coln[c] >= thr;
                if (on && c0 < 0) c0 = c;
                if (on || c0 < 0) continue;
                int c1 = c - 1;
                int r0 = rows, r1 = -1;
                for (int cc = c0; cc <= c1; cc++)
                {
                    for (int r = 0; r < rows; r++)
                    {
                        if (!ink[r, cc]) continue;
                        if (r < r0) r0 = r;
                        if (r > r1) r1 = r;
                    }
                }
                int gw = c1 - c0 + 1, gh = r1 - r0 + 1;
                if (gh >= 6 && gw >= 1 && gw * gh >= 12)
                {
                    var m = new bool[gh, gw];
                    for (int r = r0; r <= r1; r++)
                        for (int cc = c0; cc <= c1; cc++)
                            if (ink[r, cc]) m[r - r0, cc - c0] = true;
                    glyphs.Add(new Glyph { m = m, rows = gh, cols = gw, x0 = c0, x1 = c1 });
                }
                c0 = -1;
            }
            return glyphs;
        }

        private static List<Glyph> FilterLineGlyphs(List<Glyph> src)
        {
            var keep = new List<Glyph>();
            if (src == null || src.Count == 0) return keep;
            var hs = new List<int>();
            foreach (var g in src)
                if (g.rows >= 8) hs.Add(g.rows);
            if (hs.Count == 0) return keep;
            hs.Sort();
            int med = hs[hs.Count / 2];
            foreach (var g in src)
            {
                if (g.rows < med * 0.45 || g.rows > med * 1.55) continue;
                keep.Add(g);
            }
            return keep;
        }

        private static double FiScore(Glyph g)
        {
            double ar = g.cols / (double)Math.Max(1, g.rows);
            if (ar < 0.38 || ar > 1.25) return 0;
            int holes = CountHoles(g.m, g.rows, g.cols);
            if (holes != 1) return 0;
            double fill = FillRatio(g.m, g.rows, g.cols);
            if (fill < 0.22 || fill > 0.62) return 0;
            double slash = Math.Max(DiagScore(g.m, g.rows, g.cols, false), DiagScore(g.m, g.rows, g.cols, true));
            if (slash < 0.28) return 0;
            return 0.45 * slash + 0.25 * (1.0 - Math.Abs(ar - 0.72)) + 0.30;
        }

        private static bool LooksLikeSlash(Glyph g)
        {
            double ar = g.cols / (double)Math.Max(1, g.rows);
            if (ar > 0.85) return false;
            if (CountHoles(g.m, g.rows, g.cols) != 0) return false;
            double fill = FillRatio(g.m, g.rows, g.cols);
            if (fill > 0.48) return false;
            double slash = Math.Max(DiagScore(g.m, g.rows, g.cols, false), DiagScore(g.m, g.rows, g.cols, true));
            return slash > 0.48;
        }

        private static readonly object DigitLock = new object();
        private static Dictionary<char, bool[,]> _digitTpl;

        private static char ClassifyDigit(Glyph g)
        {
            double ar = g.cols / (double)Math.Max(1, g.rows);
            if (ar < 0.34) return '1';
            EnsureDigitTemplates();
            bool[,] n = ToFixed(g, 12, 20);
            char best = '?';
            double bestJ = 0;
            if (_digitTpl != null)
            {
                foreach (var kv in _digitTpl)
                {
                    double j = Jaccard(n, kv.Value);
                    if (j > bestJ)
                    {
                        bestJ = j;
                        best = kv.Key;
                    }
                }
            }
            if (bestJ >= 0.30) return best;
            return DigitHeuristic(g);
        }

        private static char DigitHeuristic(Glyph g)
        {
            int holes = CountHoles(g.m, g.rows, g.cols);
            double fill = FillRatio(g.m, g.rows, g.cols);
            double ar = g.cols / (double)Math.Max(1, g.rows);
            if (ar < 0.36) return '1';
            double top = BandFill(g.m, g.rows, g.cols, 0.0, 0.38);
            double mid = BandFill(g.m, g.rows, g.cols, 0.32, 0.68);
            double bot = BandFill(g.m, g.rows, g.cols, 0.62, 1.0);
            double left = ColBand(g.m, g.rows, g.cols, 0.0, 0.45);
            double right = ColBand(g.m, g.rows, g.cols, 0.55, 1.0);
            if (holes >= 2) return '8';
            if (holes == 1)
            {
                if (bot > top * 1.2) return '6';
                if (top > bot * 1.2) return '9';
                if (right > left * 1.15 && mid < 0.55) return '4';
                return '0';
            }
            if (top > 0.42 && bot < 0.28) return '7';
            if (right > left * 1.2) return '3';
            if (top > 0.26 && bot > 0.26 && left > right && mid < 0.45) return '5';
            if (top > 0.22 && bot > 0.22 && mid < 0.4) return '2';
            if (fill > 0.5) return '0';
            return '?';
        }

        private static void EnsureDigitTemplates()
        {
            if (_digitTpl != null) return;
            lock (DigitLock)
            {
                if (_digitTpl != null) return;
                var map = new Dictionary<char, bool[,]>();
                foreach (char ch in "0123456789")
                {
                    bool[,] t = RenderDigitTemplate(ch);
                    if (t != null) map[ch] = t;
                }
                _digitTpl = map;
            }
        }

        private static bool[,] RenderDigitTemplate(char ch)
        {
            const int tw = 12, th = 20;
            try
            {
                using (var bmp = new Bitmap(tw, th, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.Black);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (var font = new System.Drawing.Font("Arial", 16f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var br = new SolidBrush(System.Drawing.Color.White))
                        g.DrawString(ch.ToString(), font, br, new RectangleF(-1, -2, tw + 2, th + 2));
                    var m = new bool[th, tw];
                    for (int y = 0; y < th; y++)
                        for (int x = 0; x < tw; x++)
                            m[y, x] = bmp.GetPixel(x, y).R > 90;
                    return m;
                }
            }
            catch { return null; }
        }

        private static bool[,] ToFixed(Glyph g, int tw, int th)
        {
            var dst = new bool[th, tw];
            if (g.rows < 1 || g.cols < 1) return dst;
            for (int y = 0; y < th; y++)
            {
                int sy = y * g.rows / th;
                for (int x = 0; x < tw; x++)
                    dst[y, x] = g.m[sy, x * g.cols / tw];
            }
            return dst;
        }

        private static double Jaccard(bool[,] a, bool[,] b)
        {
            int inter = 0, uni = 0;
            int rows = a.GetLength(0), cols = a.GetLength(1);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    bool x = a[r, c], y = b[r, c];
                    if (x || y) uni++;
                    if (x && y) inter++;
                }
            return uni == 0 ? 0 : inter / (double)uni;
        }

        private static double FillRatio(bool[,] m, int rows, int cols)
        {
            int n = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    if (m[r, c]) n++;
            return n / (double)Math.Max(1, rows * cols);
        }

        private static double BandFill(bool[,] m, int rows, int cols, double y0, double y1)
        {
            int r0 = (int)(y0 * rows), r1 = Math.Min(rows, (int)Math.Ceiling(y1 * rows));
            if (r1 <= r0) return 0;
            int n = 0, tot = 0;
            for (int r = r0; r < r1; r++)
                for (int c = 0; c < cols; c++, tot++)
                    if (m[r, c]) n++;
            return tot == 0 ? 0 : n / (double)tot;
        }

        private static double ColBand(bool[,] m, int rows, int cols, double x0, double x1)
        {
            int c0 = (int)(x0 * cols), c1 = Math.Min(cols, (int)Math.Ceiling(x1 * cols));
            if (c1 <= c0) return 0;
            int n = 0, tot = 0;
            for (int r = 0; r < rows; r++)
                for (int c = c0; c < c1; c++, tot++)
                    if (m[r, c]) n++;
            return tot == 0 ? 0 : n / (double)tot;
        }

        private static double DiagScore(bool[,] m, int rows, int cols, bool anti)
        {
            int hit = 0, tot = 0;
            int n = Math.Max(rows, cols);
            for (int i = 0; i < n; i++)
            {
                int r = i * (rows - 1) / Math.Max(1, n - 1);
                int c = anti
                    ? (n - 1 - i) * (cols - 1) / Math.Max(1, n - 1)
                    : i * (cols - 1) / Math.Max(1, n - 1);
                tot++;
                if (m[r, c]) hit++;
            }
            return tot == 0 ? 0 : hit / (double)tot;
        }

        private static int CountHoles(bool[,] m, int rows, int cols)
        {
            var seen = new bool[rows, cols];
            var qr = new int[rows * cols + 8];
            var qc = new int[rows * cols + 8];
            int tail = 0;
            for (int r = 0; r < rows; r++)
            {
                EnqBg(m, seen, qr, qc, ref tail, r, 0, rows, cols);
                EnqBg(m, seen, qr, qc, ref tail, r, cols - 1, rows, cols);
            }
            for (int c = 0; c < cols; c++)
            {
                EnqBg(m, seen, qr, qc, ref tail, 0, c, rows, cols);
                EnqBg(m, seen, qr, qc, ref tail, rows - 1, c, rows, cols);
            }
            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };
            int head = 0;
            while (head < tail)
            {
                int r = qr[head], c = qc[head];
                head++;
                for (int k = 0; k < 4; k++)
                    EnqBg(m, seen, qr, qc, ref tail, r + dr[k], c + dc[k], rows, cols);
            }
            int holes = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (m[r, c] || seen[r, c]) continue;
                    holes++;
                    int h = 0, t = 0;
                    qr[t] = r; qc[t] = c; t++;
                    seen[r, c] = true;
                    while (h < t)
                    {
                        int rr = qr[h], cc = qc[h];
                        h++;
                        for (int k = 0; k < 4; k++)
                        {
                            int nr = rr + dr[k], nc = cc + dc[k];
                            if (nr < 0 || nc < 0 || nr >= rows || nc >= cols) continue;
                            if (m[nr, nc] || seen[nr, nc]) continue;
                            seen[nr, nc] = true;
                            qr[t] = nr; qc[t] = nc; t++;
                        }
                    }
                }
            }
            return holes;
        }

        private static void EnqBg(bool[,] m, bool[,] seen, int[] qr, int[] qc, ref int tail, int r, int c, int rows, int cols)
        {
            if (r < 0 || c < 0 || r >= rows || c >= cols) return;
            if (m[r, c] || seen[r, c]) return;
            seen[r, c] = true;
            qr[tail] = r; qc[tail] = c; tail++;
        }

        private static readonly int[] BarDia = { 8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 32 };

        private static bool IsBar(string d)
        {
            int v;
            if (!int.TryParse(d, out v)) return false;
            for (int i = 0; i < BarDia.Length; i++)
                if (BarDia[i] == v) return true;
            return false;
        }

        private static void DrawRing(Transaction tr, BlockTableRecord btr, LineString ring)
        {
            DrawRing(tr, btr, ring, LayerName);
        }

        private static void DrawRing(Transaction tr, BlockTableRecord btr, LineString ring, string layer)
        {
            if (ring == null || ring.NumPoints < 4) return;
            if (string.IsNullOrEmpty(layer)) layer = LayerName;
            var pl = new Polyline { Layer = layer, Closed = true };
            int added = 0;
            Coordinate prev = null;
            for (int i = 0; i < ring.NumPoints - 1; i++)
            {
                Coordinate p = ring.GetCoordinateN(i);
                if (prev != null && p.Distance(prev) < 0.4) continue;
                pl.AddVertexAt(added++, new Point2d(p.X, p.Y), 0, 0, 0);
                prev = p;
            }
            if (added < 3) { pl.Dispose(); return; }
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static void EnsureLayer(Transaction tr, Database db, string name, short aci, LineWeight lw)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            {
                Name = name,
                Color = AcColor.FromColorIndex(ColorMethod.ByAci, aci),
                LineWeight = lw
            };
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has("Continuous"))
                rec.LinetypeObjectId = ltt["Continuous"];
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }
    }
}
