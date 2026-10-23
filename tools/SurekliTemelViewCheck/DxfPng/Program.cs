// DXF geometri QA önizlemesi: LINE/LWPOLYLINE/TEXT/CIRCLE/ARC. Ölçü blokları çizilmez.
// Kullanım: DxfPng <dxf> find <metin-başı>      → eşleşen TEXT konumlarını yazar
//           DxfPng <dxf> <png> xmin ymin xmax ymax genişlikPx
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;

class E { public string Type, Layer, Text = ""; public List<(int C, string V)> P = new List<(int, string)>(); }

static class Program
{
    static double D(string s) => double.Parse(s.Trim(), CultureInfo.InvariantCulture);
    static string G(E e, int c, string d = "0") => e.P.FirstOrDefault(p => p.C == c).V?.Trim() ?? d;

    static List<E> Read(string path)
    {
        var lines = File.ReadAllLines(path);
        var list = new List<E>(); E cur = null; bool on = false;
        for (int i = 0; i + 1 < lines.Length; i += 2)
        {
            int c = int.Parse(lines[i].Trim()); string v = lines[i + 1];
            if (c == 2 && v.Trim() == "ENTITIES") on = true;
            if (!on) continue;
            if (c == 0)
            {
                if (cur != null) list.Add(cur);
                if (v.Trim() == "ENDSEC") break;
                cur = new E { Type = v.Trim() };
            }
            else if (cur != null) { cur.P.Add((c, v)); if (c == 8) cur.Layer = v.Trim(); if (c == 1) cur.Text = v; }
        }
        return list;
    }

    static Color Col(string layer)
    {
        if (layer == null) return Color.Gray;
        if (layer.StartsWith("DONATI YAZ")) return Color.SeaGreen;
        if (layer.StartsWith("DONATI")) return Color.RoyalBlue;
        if (layer.StartsWith("ETRIYE")) return Color.DeepSkyBlue;
        if (layer.StartsWith("KOLON ISMI")) return Color.DarkCyan;
        if (layer.StartsWith("KOLON")) return Color.Green;
        if (layer.StartsWith("PERDE")) return Color.Magenta;
        if (layer.StartsWith("TEMEL HATILI")) return Color.DarkOrange;
        if (layer.StartsWith("TEMEL ISMI")) return Color.Brown;
        if (layer.StartsWith("TEMEL")) return Color.Black;
        if (layer.StartsWith("IZDUSUM")) return Color.Silver;
        if (layer.StartsWith("KESIT GORUNUS")) return Color.Red;
        if (layer.StartsWith("KESIT SINIRI")) return Color.Purple;
        if (layer.StartsWith("OLCU")) return Color.LightGray;
        return Color.DimGray;
    }

    static int Main(string[] a)
    {
        var ents = Read(a[0]);
        if (a[1] == "find")
        {
            foreach (var e in ents.Where(e => e.Type == "TEXT" && e.Text.StartsWith(a[2])))
                Console.WriteLine($"{e.Text} @ {G(e, 10)},{G(e, 20)} [{e.Layer}]");
            return 0;
        }
        if (a[1] == "staila")
        {
            // STA4CAD temel açılımı: her "Tn TEMELI" bloğunda kolon adları ve "ila." etiketleri;
            // ilave en yakın kolona (merkez ≈ yazı x + l, 1 birim = 0,5 cm) atanır.
            var tx = ents.Where(e => e.Type == "TEXT").Select(e => (e, x: D(G(e, 10)), y: D(G(e, 20)), s: e.Text.Trim())).ToList();
            var titles = tx.Select(t => (t, m: System.Text.RegularExpressions.Regex.Match(t.s, @"^T(\d+)\s+TEMELI"))).Where(q => q.m.Success)
                .Select(q => (no: int.Parse(q.m.Groups[1].Value), q.t.x, q.t.y)).ToList();
            var ila = new System.Text.RegularExpressions.Regex(@"(\d+)\s*ƒ\s*(\d+)\s+ila\.\s+l=\s*(\d+)");
            foreach (var t in titles.OrderBy(t => t.no))
            {
                double xNext = titles.Where(o => Math.Abs(o.y - t.y) < 1 && o.x > t.x + 1).Select(o => o.x).DefaultIfEmpty(double.MaxValue).Min();
                double bx0 = t.x - 400, bx1 = xNext == double.MaxValue ? double.MaxValue : xNext - 400;
                var cols = tx.Where(c => c.e.Layer == "BEAM4" && Math.Abs(c.y - (t.y - 66)) < 3 && c.x > bx0 && c.x < bx1).ToList();
                foreach (var l in tx.Where(l => l.e.Layer.StartsWith("REBAR_DET") && l.y < t.y - 300 && l.y > t.y - 1000))
                {
                    var m = ila.Match(l.s);
                    if (!m.Success || cols.Count == 0) continue;
                    double merkez = l.x + double.Parse(m.Groups[3].Value);
                    if (merkez <= bx0 || merkez >= bx1) continue;
                    var c = cols.OrderBy(c => Math.Abs(c.x + 15 - merkez)).First();
                    Console.WriteLine($"STA T{t.no} {c.s} {m.Groups[1].Value}x{m.Groups[2].Value} l={m.Groups[3].Value}");
                }
            }
            return 0;
        }
        if (a[1] == "texts")
        {
            double bx0 = D(a[2]), by0 = D(a[3]), bx1 = D(a[4]), by1 = D(a[5]);
            foreach (var e in ents.Where(e => e.Type == "TEXT" || e.Type == "MTEXT")
                .Select(e => (e, x: D(G(e, 10)), y: D(G(e, 20))))
                .Where(t => t.x >= bx0 && t.x <= bx1 && t.y >= by0 && t.y <= by1)
                .OrderByDescending(t => Math.Round(t.y)).ThenBy(t => t.x))
                Console.WriteLine($"{e.x,10:F1} {e.y,10:F1} [{e.e.Layer}] {e.e.Text}");
            return 0;
        }
        double x0 = D(a[2]), y0 = D(a[3]), x1 = D(a[4]), y1 = D(a[5]); int w = int.Parse(a[6]);
        double s = w / (x1 - x0); int h = (int)Math.Ceiling((y1 - y0) * s);
        using var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        PointF P(double x, double y) => new PointF((float)((x - x0) * s), (float)((y1 - y) * s));
        foreach (var e in ents)
        {
            using var pen = new Pen(Col(e.Layer), 1f);
            try
            {
                switch (e.Type)
                {
                    case "LINE": g.DrawLine(pen, P(D(G(e, 10)), D(G(e, 20))), P(D(G(e, 11)), D(G(e, 21)))); break;
                    case "LWPOLYLINE":
                        var xs = e.P.Where(p => p.C == 10).Select(p => D(p.V)).ToList();
                        var ys = e.P.Where(p => p.C == 20).Select(p => D(p.V)).ToList();
                        var pts = xs.Zip(ys, (x, y) => P(x, y)).ToList();
                        if ((int.Parse(G(e, 70)) & 1) == 1 && pts.Count > 2) pts.Add(pts[0]);
                        if (pts.Count > 1) g.DrawLines(pen, pts.ToArray());
                        break;
                    case "DIMENSION":
                        {
                            // Döndürülmüş yatay/düşey ölçü: 13/14 uzatma noktaları, 10 ölçü çizgisi.
                            double ax = D(G(e, 13)), ay = D(G(e, 23)), bx = D(G(e, 14)), by = D(G(e, 24)), dx = D(G(e, 10)), dy = D(G(e, 20));
                            bool dus = Math.Abs(D(G(e, 50))) > 1.0;
                            using var dp = new Pen(Color.Orange, 1f);
                            if (!dus)
                            {
                                g.DrawLine(dp, P(ax, dy), P(bx, dy)); g.DrawLine(dp, P(ax, ay), P(ax, dy)); g.DrawLine(dp, P(bx, by), P(bx, dy));
                                var tp2 = P((ax + bx) / 2, dy);
                                using var f2 = new Font("Arial", 9f, GraphicsUnit.Pixel);
                                g.DrawString((Math.Abs(bx - ax) / 2).ToString("0"), f2, Brushes.DarkOrange, tp2.X - 6, tp2.Y - 11);
                            }
                            else { g.DrawLine(dp, P(dx, ay), P(dx, by)); g.DrawLine(dp, P(ax, ay), P(dx, ay)); g.DrawLine(dp, P(bx, by), P(dx, by)); }
                        }
                        break;
                    case "CIRCLE":
                        var c = P(D(G(e, 10)), D(G(e, 20))); float r = (float)(D(G(e, 40)) * s);
                        g.DrawEllipse(pen, c.X - r, c.Y - r, 2 * r, 2 * r); break;
                    case "TEXT":
                        double th = D(G(e, 40)) * s; if (th < 4) break;
                        bool al = G(e, 72) != "0" || G(e, 73) != "0";
                        var tp = al ? P(D(G(e, 11)), D(G(e, 21))) : P(D(G(e, 10)), D(G(e, 20)));
                        using (var f = new Font("Arial", (float)th, GraphicsUnit.Pixel))
                        {
                            var sz = g.MeasureString(e.Text, f);
                            float ox = G(e, 72) == "1" || G(e, 72) == "4" ? -sz.Width / 2 : G(e, 72) == "2" ? -sz.Width : 0;
                            float oy = G(e, 73) == "2" || G(e, 72) == "4" ? -sz.Height / 2 : G(e, 73) == "3" ? 0 : -sz.Height;
                            double rot = -D(G(e, 50));
                            var st = g.Save(); g.TranslateTransform(tp.X, tp.Y); g.RotateTransform((float)rot);
                            g.DrawString(e.Text, f, new SolidBrush(Col(e.Layer)), ox, oy); g.Restore(st);
                        }
                        break;
                }
            }
            catch (FormatException) { }
        }
        bmp.Save(a[1], System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"{a[1]} {w}x{h}");
        return 0;
    }
}
