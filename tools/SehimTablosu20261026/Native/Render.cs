// Core Console doğrulaması: model alanındaki çizgi/polyline/yazı/taramayı PNG'ye döker, yazıları ST_TXT dosyasına yazar.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using SD = System.Drawing;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

public class SehimRender
{
    [CommandMethod("ST_RENDER")]
    public void Render()
    {
        Database db = AcApp.DocumentManager.MdiActiveDocument.Database;
        string png = Environment.GetEnvironmentVariable("ST_PNG"), txt = Environment.GetEnvironmentVariable("ST_TXT");
        var ci = CultureInfo.InvariantCulture;
        var satir = new List<string>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            var ents = ms.Cast<ObjectId>().Select(id => (Entity)tr.GetObject(id, OpenMode.ForRead)).ToList();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            SD.Color Renk(Entity e)
            {
                short aci = e.Color.IsByLayer ? ((LayerTableRecord)tr.GetObject(lt[e.Layer], OpenMode.ForRead)).Color.ColorIndex : e.Color.ColorIndex;
                int rgb = EntityColor.LookUpRgb((byte)Math.Max(1, Math.Min(255, (int)aci)));
                return SD.Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            }
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var e in ents) { try { var b = e.GeometricExtents; x0 = Math.Min(x0, b.MinPoint.X); y0 = Math.Min(y0, b.MinPoint.Y); x1 = Math.Max(x1, b.MaxPoint.X); y1 = Math.Max(y1, b.MaxPoint.Y); } catch { } }
            double pad = 10, k = 1800 / (x1 - x0 + 2 * pad);
            int W = 1800, H = (int)((y1 - y0 + 2 * pad) * k);
            SD.PointF P(double x, double y) => new SD.PointF((float)((x - x0 + pad) * k), (float)((y1 + pad - y) * k));
            using (var bmp = new SD.Bitmap(W, H))
            using (var g = SD.Graphics.FromImage(bmp))
            {
                g.Clear(SD.Color.FromArgb(33, 40, 48)); g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias; g.TextRenderingHint = SD.Text.TextRenderingHint.AntiAlias;
                foreach (var h in ents.OfType<Hatch>())
                {
                    var b = h.GeometricExtents; var a = P(b.MinPoint.X, b.MaxPoint.Y); var c = P(b.MaxPoint.X, b.MinPoint.Y);
                    int alpha = h.Transparency.IsByAlpha ? h.Transparency.Alpha : 255;
                    using (var br = new SD.SolidBrush(SD.Color.FromArgb(alpha, Renk(h)))) g.FillRectangle(br, a.X, a.Y, c.X - a.X, c.Y - a.Y);
                    satir.Add("H " + h.Layer + " alpha=" + alpha);
                }
                foreach (var e in ents)
                {
                    if (e is Line l) using (var p = new SD.Pen(Renk(e), 1.2f)) g.DrawLine(p, P(l.StartPoint.X, l.StartPoint.Y), P(l.EndPoint.X, l.EndPoint.Y));
                    else if (e is Polyline pl) using (var p = new SD.Pen(Renk(e), 2.5f)) { var pts = Enumerable.Range(0, pl.NumberOfVertices).Select(i => pl.GetPoint2dAt(i)).Select(q => P(q.X, q.Y)).ToList(); if (pl.Closed) pts.Add(pts[0]); g.DrawLines(p, pts.ToArray()); }
                    else if (e is DBText t)
                    {
                        bool merkez = t.HorizontalMode == TextHorizontalMode.TextCenter;
                        var q = merkez || t.HorizontalMode != TextHorizontalMode.TextLeft || t.VerticalMode != TextVerticalMode.TextBase ? t.AlignmentPoint : t.Position;
                        using (var f = new SD.Font("Bahnschrift Light Condensed", (float)(t.Height * k * 1.25), SD.GraphicsUnit.Pixel))
                        using (var br = new SD.SolidBrush(Renk(e)))
                        using (var sf = new SD.StringFormat { Alignment = merkez ? SD.StringAlignment.Center : t.HorizontalMode == TextHorizontalMode.TextRight ? SD.StringAlignment.Far : SD.StringAlignment.Near,
                            LineAlignment = t.VerticalMode == TextVerticalMode.TextVerticalMid ? SD.StringAlignment.Center : t.VerticalMode == TextVerticalMode.TextTop ? SD.StringAlignment.Near : SD.StringAlignment.Far })
                            g.DrawString(t.TextString, f, br, P(q.X, q.Y), sf);
                        satir.Add("T " + t.Layer + " | " + t.TextString + " @" + q.X.ToString("0.0", ci) + "," + q.Y.ToString("0.0", ci) + " h" + t.Height.ToString(ci) + " stil=" + ((TextStyleTableRecord)tr.GetObject(t.TextStyleId, OpenMode.ForRead)).Name);
                    }
                }
                bmp.Save(png, SD.Imaging.ImageFormat.Png);
            }
            satir.Insert(0, "ENT " + ents.Count + " line=" + ents.OfType<Line>().Count() + " pl=" + ents.OfType<Polyline>().Count() + " text=" + ents.OfType<DBText>().Count() + " hatch=" + ents.OfType<Hatch>().Count()
                + " ext=" + x0.ToString("0.0", ci) + "," + y0.ToString("0.0", ci) + " " + x1.ToString("0.0", ci) + "," + y1.ToString("0.0", ci));
            tr.Commit();
        }
        File.WriteAllLines(txt, satir);
    }
}
