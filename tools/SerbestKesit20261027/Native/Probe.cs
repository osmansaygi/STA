// Core Console dogrulamasi: yuklu Plan DLL ile (yansima) ST4PLANID planini cizer, AKS cizgilerinden bagimsiz kural ile
// yatay, dusey ve egik hat secer, TryResolveSerbestKesitPlanOffset + DrawSerbestKesit cagirir, plani ve kesitleri PNG'ye doker.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using SD = System.Drawing;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

public class SerbestKesitProbe
{
    const BindingFlags SP = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    static string Dir => Environment.GetEnvironmentVariable("SK_DIR");
    static string Ad => Environment.GetEnvironmentVariable("SK_NAME");
    static void Log(string s) => File.AppendAllText(Path.Combine(Dir, Ad + ".txt"), s + Environment.NewLine);

    [CommandMethod("SK_TEST")]
    public static void Test()
    {
        try
        {
            var plan = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            Log("Plan=" + plan.Location);
            var parserT = plan.GetType("ST4AksCizCSharp.St4Parser", true);
            var mgrT = plan.GetType("ST4PlanIdCiz.PlanIdDrawingManager", true);
            plan.GetTypes().First(t => t.Name == "CommandEntry").GetMethod("ConfigureNtsNextGenOverlay", SP)?.Invoke(null, null);
            string st4 = Environment.GetEnvironmentVariable("SK_ST4");
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            Func<object> yeniMgr = () =>
            {
                object model = parserT.GetMethod("Parse").Invoke(Activator.CreateInstance(parserT), new object[] { st4 });
                plan.GetTypes().FirstOrDefault(t => t.Name == "GprYapiAksLabels")?.GetMethod("TryMergeFromGprBesideSt4", SP)?.Invoke(null, new[] { st4, model });
                return Activator.CreateInstance(mgrT, model);
            };
            var basePt = new Point3d(10000, 5000, 0);
            using (doc.LockDocument())
                mgrT.GetMethod("Draw").Invoke(yeniMgr(), new object[] { db, doc.Editor, basePt, st4 });

            // Bagimsiz kural: en soldaki planin dikey/yatay AKS cizgileri.
            var dik = new List<(double X, double Lo, double Hi)>();
            var yat = new List<(double Y, double Lo, double Hi)>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                    if (tr.GetObject(id, OpenMode.ForRead) is Line l && l.Layer == "AKS CIZGISI (BEYKENT)")
                    {
                        if (Math.Abs(l.StartPoint.X - l.EndPoint.X) < 0.01) dik.Add((l.StartPoint.X, Math.Min(l.StartPoint.Y, l.EndPoint.Y), Math.Max(l.StartPoint.Y, l.EndPoint.Y)));
                        else if (Math.Abs(l.StartPoint.Y - l.EndPoint.Y) < 0.01) yat.Add((l.StartPoint.Y, Math.Min(l.StartPoint.X, l.EndPoint.X), Math.Max(l.StartPoint.X, l.EndPoint.X)));
                    }
                tr.Commit();
            }
            Log("AKS cizgisi dikey=" + dik.Count + " yatay=" + yat.Count);
            // Model aks açıklıkları (dik akslar) → en soldaki dikey çizgiden başlayan planın kutusu.
            object mdl = parserT.GetMethod("Parse").Invoke(Activator.CreateInstance(parserT), new object[] { st4 });
            double Span(string ad)
            {
                var vals = ((System.Collections.IEnumerable)mdl.GetType().GetProperty(ad).GetValue(mdl)).Cast<object>()
                    .Where(a => Math.Abs((double)a.GetType().GetProperty("Slope").GetValue(a)) < 1e-9)
                    .Select(a => (double)a.GetType().GetProperty("ValueCm").GetValue(a)).ToList();
                return vals.Max() - vals.Min();
            }
            double spanX = Span("AxisX"), spanY = Span("AxisY");
            var v0 = dik.OrderBy(d => d.X).First();
            var planDik = dik.Where(d => d.X >= v0.X - 1 && d.X <= v0.X + spanX + 1 && d.Hi > v0.Lo && d.Lo < v0.Hi).OrderBy(d => d.X).ToList();
            var planYat = yat.Where(y => y.Y > v0.Lo && y.Y < v0.Hi && y.Hi > v0.X && y.Lo < v0.X + spanX).OrderBy(y => y.Y).ToList();
            Log(string.Format(CI, "model aks acikligi X {0:0} Y {1:0}", spanX, spanY));
            double xL = planDik[0].X, xR = planDik[planDik.Count - 1].X, yB = planYat[0].Y, yT = planYat[planYat.Count - 1].Y;
            Log(string.Format(CI, "Ilk plan aks kutusu x {0:0.0}..{1:0.0} y {2:0.0}..{3:0.0} ({4} dikey, {5} yatay)", xL, xR, yB, yT, planDik.Count, planYat.Count));
            double yKes = planYat.Count >= 3 ? (planYat[1].Y + planYat[2].Y) * 0.5 : (yB + yT) * 0.5;
            double xKes = planDik.Count >= 3 ? (planDik[1].X + planDik[2].X) * 0.5 : (xL + xR) * 0.5;

            var hatlar = new[]
            {
                ("A", new Point3d(xL - 150, yKes, 0), new Point3d(xR + 150, yKes, 0)),
                ("B", new Point3d(xKes, yT + 150, 0), new Point3d(xKes, yB - 150, 0)),
                ("C", new Point3d(xL - 100, yB - 100, 0), new Point3d(xR + 100, yT + 100, 0)),
            };
            double insX = basePt.X, insY0 = Math.Min(yB, 0) - 6000, insY = insY0;
            foreach (var (harf, a, b) in hatlar)
            {
                object mgr = yeniMgr();
                var args = new object[] { db, a, b, null, 0, 0 };
                bool ok = (bool)mgrT.GetMethod("TryResolveSerbestKesitPlanOffset").Invoke(mgr, args);
                var off = ok ? (Vector2d)args[3] : new Vector2d();
                Log(string.Format(CI, "{0}: resolve={1} offset=({2:0.00},{3:0.00}) eslesen X={4} Y={5}", harf, ok, off.X, off.Y, args[4], args[5]));
                if (!ok) continue;
                // Bağımsız kontrol: her kolonun model merkezi + öteleme çizimdeki bir KOLON nesnesinin içine düşmeli (ilk kat).
                if (harf == "A") Log("A: kolon kontrolu " + KolonKontrol(db, mdl, off));
                int n0 = Count(db);
                bool drawn;
                using (doc.LockDocument())
                    drawn = (bool)mgrT.GetMethod("DrawSerbestKesit").Invoke(mgr, new object[] { db, doc.Editor, a, b, off, new Point3d(insX, insY, 0), harf });
                Log(harf + ": draw=" + drawn + " yeni nesne=" + (Count(db) - n0));
                insY -= 2000;
            }
            double w = Math.Max(xR - xL, yT - yB) * 1.5 * 0.25 + 900;
            Render(db, Path.Combine(Dir, Ad + "_plan.png"), xL - 400, yB - 400, xR + 400, yT + 400);
            Render(db, Path.Combine(Dir, Ad + "_kesit.png"), insX - 300, insY + 200, insX + w, insY0 + 1400);
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }

    static string KolonKontrol(Database db, object mdl, Vector2d off)
    {
        var kutular = new List<Extents3d>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead))
                if (tr.GetObject(id, OpenMode.ForRead) is Polyline p && p.Layer.StartsWith("KOLON", StringComparison.OrdinalIgnoreCase))
                    try { kutular.Add(p.GeometricExtents); } catch { }
            tr.Commit();
        }
        int n = 0, icinde = 0;
        foreach (var kol in ((System.Collections.IEnumerable)mdl.GetType().GetProperty("Columns").GetValue(mdl)).Cast<object>())
        {
            var ax = kol.GetType().GetProperty("AxisXId")?.GetValue(kol);
            // Kolon merkezini aks kesişiminden al (eksantrisite kolon boyutundan küçük kabul).
            var xs = ((System.Collections.IEnumerable)mdl.GetType().GetProperty("AxisX").GetValue(mdl)).Cast<object>();
            var ys = ((System.Collections.IEnumerable)mdl.GetType().GetProperty("AxisY").GetValue(mdl)).Cast<object>();
            int xi = (int)kol.GetType().GetProperty("AxisXId").GetValue(kol), yi = (int)kol.GetType().GetProperty("AxisYId").GetValue(kol);
            var a = xs.FirstOrDefault(q => (int)q.GetType().GetProperty("Id").GetValue(q) == xi);
            var b = ys.FirstOrDefault(q => (int)q.GetType().GetProperty("Id").GetValue(q) == yi);
            if (a == null || b == null) continue;
            if (Math.Abs((double)a.GetType().GetProperty("Slope").GetValue(a)) > 1e-9 || Math.Abs((double)b.GetType().GetProperty("Slope").GetValue(b)) > 1e-9) continue;
            double x = (double)a.GetType().GetProperty("ValueCm").GetValue(a) + off.X;
            double y = -(double)b.GetType().GetProperty("ValueCm").GetValue(b) + off.Y;
            n++;
            if (kutular.Any(e => x >= e.MinPoint.X - 30 && x <= e.MaxPoint.X + 30 && y >= e.MinPoint.Y - 30 && y <= e.MaxPoint.Y + 30)) icinde++;
            if (n >= 40) break;
        }
        return icinde + "/" + n + " aks kesisimi kolon nesnesinde (30 cm pay)";
    }

    static int Count(Database db)
    {
        using (var tr = db.TransactionManager.StartTransaction())
        {
            int n = ((BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead)).Cast<ObjectId>().Count();
            tr.Commit();
            return n;
        }
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
                    else if (e is BlockReference br2 && derin < 3)
                    {
                        var col = new DBObjectCollection();
                        try { br2.Explode(col); } catch { }
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
