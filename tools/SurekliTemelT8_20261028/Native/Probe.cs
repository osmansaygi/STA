// Core Console: yuklu Plan DLL ile (yansima) SUREKLITEMELACILIMLARI cizer; "T-8" basligi cevresini PNG'ye ve yazilari loga doker.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using SD = System.Drawing;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

public class T8Probe
{
    const BindingFlags IP = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    static string Dir => Environment.GetEnvironmentVariable("T8_DIR");
    static string Ad => Environment.GetEnvironmentVariable("T8_NAME");
    static void Log(string s) => File.AppendAllText(Path.Combine(Dir, Ad + ".txt"), s + Environment.NewLine);

    [CommandMethod("T8_TEST")]
    public static void Test()
    {
        try
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            string st4 = Environment.GetEnvironmentVariable("T8_ST4");
            string hedef = Environment.GetEnvironmentVariable("T8_BASLIK") ?? "T-8";
            var plan = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            Log("Plan=" + plan.Location);
            var parser = Activator.CreateInstance(plan.GetType("ST4AksCizCSharp.St4Parser", true));
            object model = parser.GetType().GetMethod("Parse", new[] { typeof(string) }).Invoke(parser, new object[] { st4 });
            var mgrT = plan.GetType("ST4PlanIdCiz.PlanIdDrawingManager", true);
            var manager = Activator.CreateInstance(mgrT, model);
            var optT = plan.GetType("ST4PlanIdCiz.KirisAcilimSecenekleri", true);
            var opt = Activator.CreateInstance(optT, true);
            optT.GetField("Antetli").SetValue(opt, false);
            var draw = mgrT.GetMethod("DrawSurekliTemelAcilimlariSecenekli", IP);
            Log("draw=" + draw.Invoke(manager, new object[] { db, doc.Editor, Point3d.Origin, st4, opt }));

            Point3d? baslik = null;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead))
                {
                    var e = tr.GetObject(id, OpenMode.ForRead);
                    string s = e is DBText t ? t.TextString : e is MText m ? m.Contents : null;
                    if (s == null) continue;
                    Point3d p = e is DBText t2 ? t2.Position : ((MText)e).Location;
                    if ((s.StartsWith(hedef + " ") || s == hedef) && (baslik == null || p.Y > baslik.Value.Y)) { baslik = p; Log("BASLIK " + s + " @ " + p.X.ToString("F0", CI) + "," + p.Y.ToString("F0", CI)); }
                }
                tr.Commit();
            }
            if (baslik == null) { Log("baslik yok"); Log("COMPLETE"); return; }
            double bx = baslik.Value.X, by = baslik.Value.Y;
            double x0 = bx - 400, x1 = bx + 1400, y0 = by - 1300, y1 = by + 100;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead))
                {
                    var e = tr.GetObject(id, OpenMode.ForRead);
                    string s = e is DBText t ? t.TextString : e is MText m ? m.Contents : null;
                    if (s == null) continue;
                    Point3d p = e is DBText t2 ? t2.Position : ((MText)e).Location;
                    if (p.X < x0 || p.X > x1 || p.Y < y0 || p.Y > y1) continue;
                    Log("  " + s + " @ " + (p.X - bx).ToString("F0", CI) + "," + (p.Y - by).ToString("F0", CI));
                }
                tr.Commit();
            }
            Render(db, Path.Combine(Dir, Ad + ".png"), x0, y0, x1, y1);
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("HATA " + ex); }
    }

    static void Render(Database db, string png, double x0, double y0, double x1, double y1)
    {
        const double px = 2000;
        double k = px / (x1 - x0);
        int W = (int)px, H = Math.Max(10, (int)((y1 - y0) * k));
        using (var tr = db.TransactionManager.StartTransaction())
        using (var bmp = new SD.Bitmap(W, H))
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.Clear(SD.Color.FromArgb(33, 40, 48));
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            SD.Color C(Entity e)
            {
                short aci = e.Color.IsByLayer && lt.Has(e.Layer) ? ((LayerTableRecord)tr.GetObject(lt[e.Layer], OpenMode.ForRead)).Color.ColorIndex : e.Color.ColorIndex;
                int rgb = Autodesk.AutoCAD.Colors.EntityColor.LookUpRgb((byte)Math.Max(1, Math.Min(255, (int)aci)));
                return SD.Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            }
            SD.PointF P(double x, double y) => new SD.PointF((float)((x - x0) * k), (float)((y1 - y) * k));
            void Ciz(Entity e, int derin)
            {
                using (var pen = new SD.Pen(C(e), 1.2f))
                {
                    if (e is Line l) g.DrawLine(pen, P(l.StartPoint.X, l.StartPoint.Y), P(l.EndPoint.X, l.EndPoint.Y));
                    else if (e is Polyline pl && pl.NumberOfVertices > 1)
                    {
                        var pts = Enumerable.Range(0, pl.NumberOfVertices).Select(i => pl.GetPoint2dAt(i)).Select(q => P(q.X, q.Y)).ToList();
                        if (pl.Closed) pts.Add(pts[0]);
                        g.DrawLines(pen, pts.ToArray());
                    }
                    else if (e is Circle c)
                    {
                        var a = P(c.Center.X - c.Radius, c.Center.Y + c.Radius);
                        g.DrawEllipse(pen, a.X, a.Y, (float)(2 * c.Radius * k), (float)(2 * c.Radius * k));
                    }
                    else if (e is DBText t)
                    {
                        var q = t.HorizontalMode == TextHorizontalMode.TextLeft && t.VerticalMode == TextVerticalMode.TextBase ? t.Position : t.AlignmentPoint;
                        using (var f = new SD.Font("Arial", (float)Math.Max(6, t.Height * k * 1.2), SD.GraphicsUnit.Pixel))
                        using (var br = new SD.SolidBrush(C(e)))
                        using (var sf = new SD.StringFormat
                        {
                            Alignment = t.HorizontalMode == TextHorizontalMode.TextCenter ? SD.StringAlignment.Center
                                : t.HorizontalMode == TextHorizontalMode.TextRight ? SD.StringAlignment.Far : SD.StringAlignment.Near,
                            LineAlignment = SD.StringAlignment.Far
                        })
                            g.DrawString(t.TextString, f, br, P(q.X, q.Y), sf);
                    }
                    else if ((e is BlockReference || e is Dimension) && derin < 3)
                    {
                        var col = new DBObjectCollection();
                        try { e.Explode(col); } catch { }
                        foreach (DBObject o in col) { if (o is Entity ce) Ciz(ce, derin + 1); o.Dispose(); }
                    }
                }
            }
            foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead))
            {
                var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                try
                {
                    var b = e.GeometricExtents;
                    if (b.MaxPoint.X < x0 || b.MinPoint.X > x1 || b.MaxPoint.Y < y0 || b.MinPoint.Y > y1) continue;
                }
                catch { continue; }
                Ciz(e, 0);
            }
            bmp.Save(png, SD.Imaging.ImageFormat.Png);
            tr.Commit();
        }
    }
}
public class T8Sayim
{
    [CommandMethod("T8_SAY")]
    public static void Say()
    {
        string dir = Environment.GetEnvironmentVariable("T8_DIR"), ad = Environment.GetEnvironmentVariable("T8_NAME");
        string st4 = Environment.GetEnvironmentVariable("T8_ST4");
        void Log(string s) => File.AppendAllText(Path.Combine(dir, ad + ".txt"), s + Environment.NewLine);
        try
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument; var db = doc.Database;
            var plan = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            var parser = Activator.CreateInstance(plan.GetType("ST4AksCizCSharp.St4Parser", true));
            object model = parser.GetType().GetMethod("Parse", new[] { typeof(string) }).Invoke(parser, new object[] { st4 });
            var mgrT = plan.GetType("ST4PlanIdCiz.PlanIdDrawingManager", true);
            var manager = Activator.CreateInstance(mgrT, model);
            var optT = plan.GetType("ST4PlanIdCiz.KirisAcilimSecenekleri", true);
            var opt = Activator.CreateInstance(optT, true);
            optT.GetField("Antetli").SetValue(opt, false);
            Log("draw=" + mgrT.GetMethod("DrawSurekliTemelAcilimlariSecenekli", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(manager, new object[] { db, doc.Editor, Point3d.Origin, st4, opt }));
            var satir = new System.Collections.Generic.List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead))
                {
                    var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    string s = e is DBText t ? t.TextString + "@" + t.Position.X.ToString("F1", CultureInfo.InvariantCulture) + "," + t.Position.Y.ToString("F1", CultureInfo.InvariantCulture)
                        : e is Line l ? "L" + l.StartPoint.X.ToString("F1", CultureInfo.InvariantCulture) + "," + l.StartPoint.Y.ToString("F1", CultureInfo.InvariantCulture) + "-" + l.EndPoint.X.ToString("F1", CultureInfo.InvariantCulture)
                        : e.GetType().Name + "|" + e.Layer;
                    satir.Add(s);
                }
                tr.Commit();
            }
            satir.Sort(StringComparer.Ordinal);
            File.WriteAllLines(Path.Combine(dir, ad + ".ent"), satir);
            Log("ent=" + satir.Count);
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("HATA " + ex); }
    }
}
public class T8Donati
{
    [CommandMethod("T8_DONATI")]
    public static void Dok()
    {
        string dir = Environment.GetEnvironmentVariable("T8_DIR"), ad = Environment.GetEnvironmentVariable("T8_NAME");
        string st4 = Environment.GetEnvironmentVariable("T8_ST4");
        void Log(string s) => File.AppendAllText(Path.Combine(dir, ad + ".txt"), s + Environment.NewLine);
        var CI = CultureInfo.InvariantCulture;
        try
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument; var db = doc.Database;
            var plan = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            var parser = Activator.CreateInstance(plan.GetType("ST4AksCizCSharp.St4Parser", true));
            object model = parser.GetType().GetMethod("Parse", new[] { typeof(string) }).Invoke(parser, new object[] { st4 });
            var mgrT = plan.GetType("ST4PlanIdCiz.PlanIdDrawingManager", true);
            var manager = Activator.CreateInstance(mgrT, model);
            var optT = plan.GetType("ST4PlanIdCiz.KirisAcilimSecenekleri", true);
            var opt = Activator.CreateInstance(optT, true);
            optT.GetField("Antetli").SetValue(opt, false);
            mgrT.GetMethod("DrawSurekliTemelAcilimlariSecenekli", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(manager, new object[] { db, doc.Editor, Point3d.Origin, st4, opt });
            // Basliklar: her T-x en ust baslik; altindaki 0..-260 bolgesi gorunus.
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                var ents = ms.Cast<ObjectId>().Select(i => (Entity)tr.GetObject(i, OpenMode.ForRead)).ToList();
                foreach (string h in (Environment.GetEnvironmentVariable("T8_BASLIKLAR") ?? "T-8 (100/50)").Split(';'))
                {
                    var t = ents.OfType<DBText>().Where(x => x.TextString == h).OrderByDescending(x => x.Position.Y).FirstOrDefault();
                    if (t == null) { Log("yok " + h); continue; }
                    double bx = t.Position.X, by = t.Position.Y;
                    Log("== " + h + " @" + bx.ToString("F0", CI) + "," + by.ToString("F0", CI));
                    foreach (var e in ents)
                    {
                        if (!(e is Polyline || e is Line)) continue;
                        Extents3d b; try { b = e.GeometricExtents; } catch { continue; }
                        if (b.MinPoint.Y < by - 260 || b.MaxPoint.Y > by - 20 || b.MaxPoint.X < bx - 500 || b.MinPoint.X > bx + 1500) continue;
                        string geo = e is Polyline p ? "PL n=" + p.NumberOfVertices + " " + string.Join(" ", Enumerable.Range(0, Math.Min(p.NumberOfVertices, 8)).Select(k => p.GetPoint2dAt(k)).Select(q => "(" + (q.X - bx).ToString("F1", CI) + "," + (q.Y - by).ToString("F1", CI) + ")"))
                            : "L (" + (((Line)e).StartPoint.X - bx).ToString("F1", CI) + "," + (((Line)e).StartPoint.Y - by).ToString("F1", CI) + ")-(" + (((Line)e).EndPoint.X - bx).ToString("F1", CI) + "," + (((Line)e).EndPoint.Y - by).ToString("F1", CI) + ")";
                        Log(e.Layer + " | " + geo);
                    }
                }
                tr.Commit();
            }
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("HATA " + ex); }
    }
}
