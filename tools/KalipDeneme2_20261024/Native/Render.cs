using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using SD = System.Drawing;

// İlk EKSEN kümesinin çevresini (KD_PNG dosyası) PNG'ye çizer; yalnız görsel kontrol.
public static class KdRender
{
    public static void Render(Database db, Action<string> log)
    {
        string all = Environment.GetEnvironmentVariable("KD_PNGC") ?? "";
        string baseFile = Environment.GetEnvironmentVariable("KD_PNG");
        if (!all.Contains(";")) { RenderOne(db, log); return; }
        string[] parts = all.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            Environment.SetEnvironmentVariable("KD_PNGC", parts[i]);
            Environment.SetEnvironmentVariable("KD_PNG", System.IO.Path.ChangeExtension(baseFile, null) + "_" + (i + 1) + ".png");
            RenderOne(db, log);
        }
    }

    static void RenderOne(Database db, Action<string> log)
    {
        string file = Environment.GetEnvironmentVariable("KD_PNG");
        if (string.IsNullOrEmpty(file)) return;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            var ents = new List<Entity>();
            foreach (ObjectId id in ms) ents.Add((Entity)tr.GetObject(id, OpenMode.ForRead));
            var eks = ents.OfType<Line>().Where(l => l.Layer.StartsWith("EKSEN")).ToList();
            if (eks.Count == 0) { log("PNG: eksen yok"); tr.Commit(); return; }
            double mx = eks.Min(l => Math.Min(l.StartPoint.X, l.EndPoint.X));
            double span = double.Parse(Environment.GetEnvironmentVariable("KD_SPAN") ?? "2600");
            var first = eks.Where(l => l.StartPoint.X < mx + span).ToList();
            double x0 = mx - 150, x1 = mx + span, y0 = first.Min(l => l.StartPoint.Y) - 300, y1 = first.Max(l => l.StartPoint.Y) + 300;
            string pngc = Environment.GetEnvironmentVariable("KD_PNGC");
            if (pngc == "auto")
            {
                // İlk donatılı kopya: x'e göre sıralı eksen çizgilerinde 3000 cm'den büyük boşlukta kopya biter.
                var xs = eks.Select(l => l.StartPoint.X).OrderBy(v => v).ToList();
                double end = xs[0];
                foreach (double v in xs) { if (v - end > 3000) break; end = v; }
                var c1 = eks.Where(l => l.StartPoint.X <= end + 1).ToList();
                x0 = xs[0] - 400; x1 = end + 400;
                y0 = c1.Min(l => l.StartPoint.Y) - 400; y1 = c1.Max(l => l.StartPoint.Y) + 400;
            }
            else if (!string.IsNullOrEmpty(pngc))
            {
                var q = pngc.Split(',').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                x0 = q[0] - q[2] / 2; x1 = q[0] + q[2] / 2; y0 = q[1] - q[3] / 2; y1 = q[1] + q[3] / 2;
            }
            double k = 2400.0 / (x1 - x0); int W = 2400, H = (int)((y1 - y0) * k);
            using (var bmp = new SD.Bitmap(W, H))
            using (var g = SD.Graphics.FromImage(bmp))
            {
                g.Clear(SD.Color.FromArgb(33, 40, 48));
                SD.PointF P(double x, double y) => new SD.PointF((float)((x - x0) * k), (float)((y1 - y) * k));
                SD.Color C(Entity e)
                {
                    string l = e.Layer.ToUpperInvariant();
                    if (l == "EKSEN X (BEYKENT)") return SD.Color.DeepSkyBlue;
                    if (l == "EKSEN Y (BEYKENT)") return SD.Color.Red;
                    if (e.ColorIndex == 1 && l.StartsWith("DONATI")) return SD.Color.OrangeRed;
                    if (l == "DONATI (BEYKENT)") return SD.Color.Cyan;
                    if (l == "ALT DUZ ALAN (BEYKENT)") return SD.Color.Magenta;
                    if (l.Contains("GPR KIRMIZI")) return SD.Color.Salmon;
                    if (l.Contains("GPR MAVI")) return SD.Color.LightSkyBlue;
                    if (l.Contains("KOLON")) return SD.Color.LimeGreen;
                    if (l.Contains("KIRIS") || l.Contains("PERDE")) return SD.Color.Yellow;
                    return SD.Color.Gray;
                }
                bool In(Extents3d ex) => ex.MaxPoint.X >= x0 && ex.MinPoint.X <= x1 && ex.MaxPoint.Y >= y0 && ex.MinPoint.Y <= y1;
                foreach (var e in ents.OrderBy(e => e.Layer.StartsWith("EKSEN") ? 1 : 0))
                {
                    Extents3d ex; try { ex = e.GeometricExtents; } catch { continue; }
                    if (!In(ex)) continue;
                    bool eksen = e.Layer.StartsWith("EKSEN");
                    using (var pen = new SD.Pen(C(e), eksen ? 3f : 1f))
                    {
                        if (e is Line ln) g.DrawLine(pen, P(ln.StartPoint.X, ln.StartPoint.Y), P(ln.EndPoint.X, ln.EndPoint.Y));
                        else if (e is Polyline pl)
                            for (int i = 1; i < pl.NumberOfVertices + (pl.Closed ? 1 : 0); i++)
                            { var a = pl.GetPoint2dAt(i - 1); var b = pl.GetPoint2dAt(i % pl.NumberOfVertices); g.DrawLine(pen, P(a.X, a.Y), P(b.X, b.Y)); }
                        else if (e is Circle c) g.DrawEllipse(pen, P(c.Center.X - c.Radius, c.Center.Y + c.Radius).X, P(c.Center.X - c.Radius, c.Center.Y + c.Radius).Y, (float)(2 * c.Radius * k), (float)(2 * c.Radius * k));
                        else if (e is DBText t)
                        {
                            float fh = Math.Max(6f, (float)(t.Height * k));
                            using (var f = new SD.Font("Arial", fh, SD.GraphicsUnit.Pixel))
                            using (var br = new SD.SolidBrush(C(e) == SD.Color.Gray ? SD.Color.LightGreen : C(e)))
                                g.DrawString(t.TextString, f, br, P(ex.MinPoint.X, ex.MaxPoint.Y));
                        }
                    }
                }
                bmp.Save(file, SD.Imaging.ImageFormat.Png);
            }
            log("PNG: " + file);
            tr.Commit();
        }
    }
}
