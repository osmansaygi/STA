using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ST4Yardimci
{
    internal static partial class TemelCekuruRunner
    {
        private sealed partial class Fit
        {
            private void FinishSingleDetail(Transaction tr, BlockTableRecord ms)
            {
                if (!SingleDetail) return;
                double cut = DetailCutX();
                var ids = new List<ObjectId>();
                foreach (ObjectId id in ms) ids.Add(id);
                foreach (ObjectId id in ids)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Entity ent) || ent.IsErased) continue;
                    if (ent is LineAngularDimension2 angle)
                    {
                        if (!KeepDetailX(angle.XLine1Start.X)) angle.Erase();
                        continue;
                    }
                    if (ent is Dimension || ent.Layer.IndexOf("KOT", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (TryDotCenter(ent, out Point3d dot))
                    {
                        double inner = DetailLeft ? XInnerL : XInnerR + DW;
                        bool inFlat = DetailLeft ? dot.X > inner + 1 : dot.X < inner - 1;
                        double upper = PhysicalDot(6).Y, lower = PhysicalDot(8).Y;
                        if (!KeepDetailX(dot.X) || (inFlat && (Math.Abs(dot.Y - upper) < 1 || Math.Abs(dot.Y - lower) < 1))) ent.Erase();
                        continue;
                    }
                    if (ent is DBText text)
                    {
                        if (!KeepDetailX(text.Position.X)) text.Erase();
                        continue;
                    }
                    if (ent is Hatch hatch) { ClipDetailHatch(hatch, cut, DetailLeft); continue; }
                    if (ent is Line line)
                    {
                        bool a = KeepDetailX(line.StartPoint.X), b = KeepDetailX(line.EndPoint.X);
                        if (!a && !b) { line.Erase(); continue; }
                        if (a == b) continue;
                        Point3d p = line.StartPoint, q = line.EndPoint;
                        double t = (cut - p.X) / (q.X - p.X);
                        Point3d edge = p + (q - p) * t;
                        if (a) line.EndPoint = edge; else line.StartPoint = edge;
                        continue;
                    }
                    if (ent is Polyline polyline) ClipDetailPolyline(tr, ms, polyline, cut);
                }
                double floor = Y3 + Dy3;
                double gro = MapPt(new Point3d(XKotCenter, YGro, 0)).Y;
                // Üst temel ucundaki gibi düz kesit sınırı: üst ve alt kenardan 5 birim taşar.
                EnsureStyledLayer(tr, ms.Database, LayerKesitSiniri, 7, LineWeight.LineWeight020, null);
                var cutMark = new Line(new Point3d(cut, floor + 5, 0), new Point3d(cut, gro - 5, 0))
                {
                    Layer = LayerKesitSiniri,
                    Color = Color.FromColorIndex(ColorMethod.ByLayer, 256)
                };
                ms.AppendEntity(cutMark);
                tr.AddNewlyCreatedDBObject(cutMark, true);
                try { DrawDetailLowerTraces(tr, ms, cut); }
                catch { }
            }

            private const string LayerKesitSiniri = "KESIT SINIRI (BEYKENT)";

            /// <summary>
            /// Alt temel: üst temeldeki gibi üst/alt donatı izi ve iz noktaları. İz, kesit sınırından
            /// perdenin uzak çubuğuna kadar; noktalar yakın çubuktan sınıra paspayı kalana dek eşit aralıklı.
            /// </summary>
            private void DrawDetailLowerTraces(Transaction tr, BlockTableRecord ms, double cut)
            {
                bool right = DetailRight;
                double toCut = right ? -1 : 1;
                Point3d near = PhysicalDot(right ? 13 : 6), far = PhysicalDot(right ? 14 : 5);
                double cover = Math.Max(1, TemelCekuruSettings.PaspayiCm) * DuPerCm;
                var rows = new[]
                {
                    new { Dot = PhysicalDot(6).Y, Line = Y3 + Dy3 - cover, Du = Math.Max(1, TemelCekuruSettings.UstAralikCm) * DuPerCm },
                    new { Dot = PhysicalDot(8).Y, Line = Y4 + Dy4 + cover, Du = Math.Max(1, TemelCekuruSettings.AltAralikCm) * DuPerCm }
                };
                double xLo = Math.Min(near.X, far.X) - 1, xHi = Math.Max(near.X, far.X) + 1;
                Polyline sample = null;
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead, false) is Entity ent) || ent.IsErased) continue;
                    if (!TryDotCenter(ent, out Point3d c) || c.X < xLo || c.X > xHi) continue;
                    if (Array.FindIndex(rows, r => Math.Abs(c.Y - r.Dot) < 1) < 0) continue;
                    if (sample == null && ent is Polyline pl) sample = pl;
                    // Perde içindeki sıra noktaları da üst temeldeki gibi iz katmanında.
                    ent.UpgradeOpen();
                    ent.Layer = LayerDonatiIzi;
                    ent.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                }
                if (sample == null) return;
                Extents3d box = sample.GeometricExtents;
                double rad = (box.MaxPoint.X - box.MinPoint.X) / 2;
                Point3d sampleCenter;
                TryDotCenter(sample, out sampleCenter);
                foreach (var row in rows)
                {
                    var trace = new Polyline(2);
                    trace.AddVertexAt(0, new Point2d(cut, row.Line), 0, 0, 0);
                    trace.AddVertexAt(1, new Point2d(far.X, row.Line), 0, 0, 0);
                    trace.Layer = LayerDonatiIzi;
                    trace.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                    trace.LineWeight = LineWeight.ByLayer;
                    ms.AppendEntity(trace);
                    tr.AddNewlyCreatedDBObject(trace, true);

                    double limit = cut - toCut * (cover + rad);
                    double len = (limit - near.X) * toCut;
                    if (len < row.Du * 0.25) continue;
                    int n = Math.Max(1, (int)Math.Ceiling(len / row.Du - 1e-6));
                    for (int i = 1; i <= n; i++)
                    {
                        var dot = (Polyline)sample.Clone();
                        double x = near.X + toCut * len * i / n;
                        dot.TransformBy(Matrix3d.Displacement(new Vector3d(x - sampleCenter.X, row.Dot - sampleCenter.Y, 0)));
                        dot.Layer = LayerDonatiIzi;
                        dot.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                        ms.AppendEntity(dot);
                        tr.AddNewlyCreatedDBObject(dot, true);
                    }
                }
            }

            /// <summary>
            /// Kesite dik (kademe boyunca uzanan) perde yatay donatılarının metrajı — temel hatılı / parapet
            /// tablosu düzeni: kademe uzunluğu − 2 paspayı, 1200 cm stok ve 60 cm bindirmeyle parçalanır.
            /// Tablo çizimin sağında, üst kenarı kesit üstünde.
            /// </summary>
            private void DrawDetailYatayMetraj(Transaction tr, BlockTableRecord ms)
            {
                double kademe = TemelDetaySettings.KademeUzunluguCm;
                double barLen = kademe - 2 * TemelCekuruSettings.PaspayiCm;
                if (barLen < 1) return;
                // İki uçta gönye: temel (perde) kalınlığı − 2 paspayı.
                double gonye = Math.Max(0, TemelCekuruSettings.AltTemelYuksekligiCm - 2 * TemelCekuruSettings.PaspayiCm);
                barLen += 2 * gonye;
                int ic = (DetailLeft ? SideAdet(false, false) : 0) + (DetailRight ? SideAdet(false, true) : 0);
                int dis = (DetailLeft ? SideAdet(true, false) : 0) + (DetailRight ? SideAdet(true, true) : 0);
                var rows = new List<string>();
                void AddRows(int adet, int cap, string yuz)
                {
                    if (adet <= 0) return;
                    foreach (KeyValuePair<double, int> part in YatayStokParcalari(barLen))
                        rows.Add(string.Format(CultureInfo.InvariantCulture, "{0}ø{1} L={2:0} (YATAY {3})",
                            adet * part.Value, cap, part.Key, yuz));
                }
                AddRows(ic, TemelCekuruSettings.UstCapMm, "İÇ");
                AddRows(dis, TemelCekuruSettings.AltCapMm, "DIŞ");
                if (!SingleDetail)
                {
                    // Çift kademede çukur tabanı kesitte etiketli gerçek donatıdır (tek kademede yalnız iz).
                    AddRows(UstAdet, TemelCekuruSettings.UstCapMm, "TEMEL ÜST");
                    AddRows(AltAdet, TemelCekuruSettings.AltCapMm, "TEMEL ALT");
                }
                if (rows.Count == 0) return;

                double maxX = double.MinValue;
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead, false) is Entity ent) || ent.IsErased) continue;
                    try { maxX = Math.Max(maxX, ent.GeometricExtents.MaxPoint.X); } catch { }
                }
                if (maxX == double.MinValue) return;
                double hatil = TemelCekuruSettings.HatilYuksekligiCm * DuPerCm;
                double top = Y1 + Math.Max(DetailLeft ? DetailDy1L : double.MinValue, DetailRight ? DetailDy1R : double.MinValue) + hatil;

                const string layerTable = "IC ANTET (BEYKENT)";
                const string layerText = "DONATI YAZISI (BEYKENT)";
                const string layerTitle = "ANTET BASLIK 1 (BEYKENT)";
                foreach (string layer in new[] { layerTable, layerText, layerTitle })
                    BeykentKatmanYukle.Ensure(tr, ms.Database, layer);
                ObjectId style = EnsureYaziStyle(tr, ms.Database);
                const double rowH = 20, textH = 10, titleH = 12, leftCol = 30, pad = 10;
                double x0 = maxX + 15 * DuPerCm, divider = x0 + leftCol;

                DBText AddText(string s, double h, string layer, Point3d at, TextHorizontalMode hm, TextVerticalMode vm)
                {
                    var t = new DBText
                    {
                        TextString = s, Height = h, Layer = layer, TextStyleId = style,
                        Color = Color.FromColorIndex(ColorMethod.ByLayer, 256),
                        HorizontalMode = hm, VerticalMode = vm, AlignmentPoint = at
                    };
                    ms.AppendEntity(t);
                    tr.AddNewlyCreatedDBObject(t, true);
                    t.AdjustAlignment(ms.Database);
                    return t;
                }
                void AddLine(double ax, double ay, double bx, double by)
                {
                    var l = new Line(new Point3d(ax, ay, 0), new Point3d(bx, by, 0))
                    { Layer = layerTable, Color = Color.FromColorIndex(ColorMethod.ByLayer, 256) };
                    ms.AppendEntity(l);
                    tr.AddNewlyCreatedDBObject(l, true);
                }

                double textRight = divider + pad + 40;
                for (int i = 0; i < rows.Count; i++)
                {
                    DBText t = AddText(rows[i], textH, layerText, new Point3d(divider + pad, top - (i + 0.5) * rowH, 0),
                        TextHorizontalMode.TextLeft, TextVerticalMode.TextVerticalMid);
                    try { textRight = Math.Max(textRight, t.GeometricExtents.MaxPoint.X); } catch { }
                }
                double x1 = textRight + pad, bottom = top - rows.Count * rowH;
                AddLine(x0, top, x1, top);
                AddLine(x0, bottom, x1, bottom);
                AddLine(x0, bottom, x0, top);
                AddLine(x1, bottom, x1, top);
                AddLine(divider, bottom, divider, top);
                for (int i = 1; i < rows.Count; i++) AddLine(x0, top - i * rowH, x1, top - i * rowH);
                AddText(string.Format(CultureInfo.InvariantCulture, "YATAY DONATI (L={0:0} cm)", kademe), titleH, layerTitle,
                    new Point3d((x0 + x1) / 2, top + 5, 0), TextHorizontalMode.TextCenter, TextVerticalMode.TextBottom);
            }

            /// <summary>Parapet yatay donatısıyla aynı: 1200 cm stok, 60 cm bindirme, son parça 5 cm katına yukarı.</summary>
            private static List<KeyValuePair<double, int>> YatayStokParcalari(double total)
            {
                const double stok = 1200, bindirme = 60;
                var result = new List<KeyValuePair<double, int>>();
                int pieces = Math.Max(1, (int)Math.Ceiling((total - bindirme) / (stok - bindirme) - 1e-9));
                int full = pieces - 1;
                double last = Math.Ceiling((total - full * (stok - bindirme)) / 5 - 1e-9) * 5;
                if (Math.Abs(last - stok) < 1e-6) { full++; last = 0; }
                if (full > 0) result.Add(new KeyValuePair<double, int>(stok, full));
                if (last > 0) result.Add(new KeyValuePair<double, int>(last, 1));
                return result;
            }

            private void ClipDetailPolyline(Transaction tr, BlockTableRecord ms, Polyline original, double cut)
            {
                int n = original.NumberOfVertices;
                if (n < 2) return;
                bool inside = false, outside = false;
                for (int i = 0; i < n; i++)
                    if (KeepDetailX(original.GetPoint2dAt(i).X)) inside = true; else outside = true;
                if (!outside) return;
                if (!inside) { original.Erase(); return; }
                var chains = new List<List<Point2d>>();
                List<Point2d> current = null;
                int segments = original.Closed ? n : n - 1;
                for (int i = 0; i < segments; i++)
                {
                    Point2d a = original.GetPoint2dAt(i), b = original.GetPoint2dAt((i + 1) % n);
                    bool keepA = KeepDetailX(a.X), keepB = KeepDetailX(b.X);
                    if (!keepA && !keepB) { current = null; continue; }
                    if (keepA != keepB)
                    {
                        Point2d intersection = DetailIntersection(a, b, cut);
                        if (keepA) b = intersection; else a = intersection;
                    }
                    if (current == null || current[current.Count - 1].GetDistanceTo(a) > NamedDotTolerance)
                    {
                        current = new List<Point2d> { a };
                        chains.Add(current);
                    }
                    current.Add(b);
                    if (!keepB) current = null;
                }
                foreach (List<Point2d> chain in chains)
                {
                    var clipped = new Polyline(chain.Count);
                    clipped.SetPropertiesFrom(original);
                    for (int i = 0; i < chain.Count; i++) clipped.AddVertexAt(i, chain[i], 0, 0, 0);
                    ms.AppendEntity(clipped);
                    tr.AddNewlyCreatedDBObject(clipped, true);
                }
                original.Erase();
            }

            private static Point2d DetailIntersection(Point2d a, Point2d b, double cut)
            {
                double t = (cut - a.X) / (b.X - a.X);
                return new Point2d(cut, a.Y + (b.Y - a.Y) * t);
            }

            private static List<Point2d> ClipDetailPolygon(List<Point2d> points, double cut, bool keepLeft)
            {
                var result = new List<Point2d>();
                if (points.Count == 0) return result;
                Point2d previous = points[points.Count - 1];
                bool previousInside = keepLeft ? previous.X <= cut : previous.X >= cut;
                foreach (Point2d point in points)
                {
                    bool inside = keepLeft ? point.X <= cut : point.X >= cut;
                    if (inside != previousInside) result.Add(DetailIntersection(previous, point, cut));
                    if (inside) result.Add(point);
                    previous = point;
                    previousInside = inside;
                }
                return result;
            }

            private static void ClipDetailHatch(Hatch hatch, double cut, bool keepLeft)
            {
                var loops = new List<KeyValuePair<HatchLoopTypes, List<Point2d>>>();
                int count = hatch.NumberOfLoops;
                for (int i = 0; i < count; i++)
                {
                    HatchLoop loop = hatch.GetLoopAt(i);
                    var points = new List<Point2d>();
                    if (loop.IsPolyline)
                        foreach (BulgeVertex vertex in loop.Polyline) points.Add(vertex.Vertex);
                    else if (loop.Curves != null)
                        foreach (Curve2d curve in loop.Curves)
                            if (curve is LineSegment2d line) points.Add(line.StartPoint);
                    points = ClipDetailPolygon(points, cut, keepLeft);
                    if (points.Count >= 3) loops.Add(new KeyValuePair<HatchLoopTypes, List<Point2d>>(loop.LoopType, points));
                }
                if (loops.Count == 0) { hatch.Erase(); return; }
                hatch.Associative = false;
                for (int i = count - 1; i >= 0; i--) hatch.RemoveLoopAt(i);
                foreach (var loop in loops)
                {
                    var points = new Point2dCollection();
                    var bulges = new DoubleCollection();
                    foreach (Point2d point in loop.Value) { points.Add(point); bulges.Add(0); }
                    hatch.AppendLoop(loop.Key, points, bulges);
                }
                hatch.EvaluateHatch(true);
            }
        }
    }
}
