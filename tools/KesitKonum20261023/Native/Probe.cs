using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

// Yüklü Plan DLL'iyle (yansıma) sürekli temel / kiriş açılımı; sonuç KK_RESULT dosyasına.
public class KesitKonumProbe
{
    const BindingFlags SP = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;
    static void Log(string s) => File.AppendAllText(Environment.GetEnvironmentVariable("KK_RESULT"), s + Environment.NewLine);

    static void Run(string method)
    {
        try
        {
            var plan = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            Log("Plan=" + plan.Location + " method=" + method + " antet=" + Environment.GetEnvironmentVariable("KK_ANTET"));
            var parserT = plan.GetType("ST4AksCizCSharp.St4Parser", true);
            var mgrT = plan.GetType("ST4PlanIdCiz.PlanIdDrawingManager", true);
            var optT = plan.GetType("ST4PlanIdCiz.KirisAcilimSecenekleri", true);
            plan.GetTypes().First(t => t.Name == "CommandEntry").GetMethod("ConfigureNtsNextGenOverlay", SP)?.Invoke(null, null);
            string st4 = Environment.GetEnvironmentVariable("KK_ST4");
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            object model = parserT.GetMethod("Parse").Invoke(Activator.CreateInstance(parserT), new object[] { st4 });
            object mgr = Activator.CreateInstance(mgrT, model);
            object opt = Activator.CreateInstance(optT, true);
            optT.GetField("Antetli")?.SetValue(opt, Environment.GetEnvironmentVariable("KK_ANTET") == "1");
            optT.GetField("AltIkinciGonyeKaldir")?.SetValue(opt, Environment.GetEnvironmentVariable("KK_IKINCI") == "1");
            var draw = mgrT.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            object ok;
            using (doc.LockDocument())
                ok = draw.Invoke(mgr, new object[] { doc.Database, doc.Editor, Point3d.Origin, st4, opt });
            Log("ok=" + ok + " ms=" + sw.ElapsedMilliseconds + " entities=" + Count(doc.Database));
            try { PngRender.Render(doc.Database, Log); } catch (System.Exception pe) { Log("PNG FAIL " + pe.Message); }
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }

    static int Count(Autodesk.AutoCAD.DatabaseServices.Database db)
    {
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var bt = (Autodesk.AutoCAD.DatabaseServices.BlockTable)tr.GetObject(db.BlockTableId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
            var ms = (Autodesk.AutoCAD.DatabaseServices.BlockTableRecord)tr.GetObject(bt[Autodesk.AutoCAD.DatabaseServices.BlockTableRecord.ModelSpace], Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
            int n = 0; var gov = new System.Collections.Generic.SortedDictionary<string, int>();
            foreach (var id in ms)
            {
                n++;
                if (tr.GetObject(id, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead) is Autodesk.AutoCAD.DatabaseServices.DBText t && (t.TextString.Contains("gov") || t.TextString.Contains("DÜZ") || t.TextString.Contains("dağ") || t.TextString.Contains("DAĞ") || t.TextString.Contains("İLAVE") || t.TextString.Contains("ALT") || t.TextString.Contains("GÖVDE") || t.TextString.Contains("FİLİZ")))
                    gov[t.TextString] = gov.TryGetValue(t.TextString, out int k) ? k + 1 : 1;
            }
            Log("gov+duz yazilari: " + string.Join(" | ", gov.Select(p => p.Key + " x" + p.Value)));
            tr.Commit();
            return n;
        }
    }

    [CommandMethod("KK_SUREKLI")] public static void Surekli() => Run("DrawSurekliTemelAcilimlariSecenekli");
    [CommandMethod("KK_KIRIS")] public static void Kiris() => Run("DrawKirisAcilimlariSecenekli");
}

public class KolonDumpProbe
{
    const BindingFlags I = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    static void Log(string s) => File.AppendAllText(Environment.GetEnvironmentVariable("KK_RESULT"), s + Environment.NewLine);

    /// <summary>KK_KOLONLAR (virgüllü kolon no) için kat kat: bu katta var mı, kesit id, boyut, alt/üst kot.</summary>
    [CommandMethod("KK_KOLON")]
    public static void Kolon()
    {
        try
        {
            var plan = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            var parserT = plan.GetType("ST4AksCizCSharp.St4Parser", true);
            var mgrT = plan.GetType("ST4PlanIdCiz.PlanIdDrawingManager", true);
            string st4 = Environment.GetEnvironmentVariable("KK_ST4");
            object model = parserT.GetMethod("Parse").Invoke(Activator.CreateInstance(parserT), new object[] { st4 });
            object mgr = Activator.CreateInstance(mgrT, model);
            var nos = (Environment.GetEnvironmentVariable("KK_KOLONLAR") ?? "53,54").Split(',').Select(int.Parse).ToList();
            var floors = (System.Collections.IList)model.GetType().GetProperty("Floors").GetValue(model);
            var cols = (System.Collections.IList)model.GetType().GetProperty("Columns").GetValue(model);
            var has = mgrT.GetMethod("HasColumnOnFloor", I);
            var sec = mgrT.GetMethod("ResolveColumnSectionId", I);
            var dims = (System.Collections.IDictionary)model.GetType().GetProperty("ColumnDimsBySectionId").GetValue(model);
            var kots = (System.Collections.IDictionary)model.GetType().GetProperty("ColumnKotMFromBinaTabaniBySectionId").GetValue(model);
            var adi = mgrT.GetMethod("KirisAcilimKolonAdi", I);
            foreach (var f in floors)
            {
                int fn = (int)f.GetType().GetProperty("FloorNo").GetValue(f);
                Log("KAT " + fn + " " + f.GetType().GetProperty("Name")?.GetValue(f) + " elev=" + f.GetType().GetProperty("ElevationM").GetValue(f));
            }
            foreach (var c in cols)
            {
                int no = (int)c.GetType().GetProperty("ColumnNo").GetValue(c);
                if (!nos.Contains(no)) continue;
                Log("KOLON " + no + " tip=" + c.GetType().GetProperty("ColumnType").GetValue(c) + " id=" + c.GetType().GetProperty("ColumnId").GetValue(c)
                    + " aks=" + c.GetType().GetProperty("AxisXId").GetValue(c) + "/" + c.GetType().GetProperty("AxisYId").GetValue(c)
                    + " aci=" + c.GetType().GetProperty("AngleDeg").GetValue(c));
                foreach (var f in floors)
                {
                    int fn = (int)f.GetType().GetProperty("FloorNo").GetValue(f);
                    int sid = (int)sec.Invoke(mgr, new object[] { fn, no });
                    Log("  kat=" + fn + " ad=" + adi?.Invoke(mgr, new object[] { f, no }) + " var=" + has.Invoke(mgr, new[] { f, c }) + " sid=" + sid
                        + " dim=" + (dims.Contains(sid) ? dims[sid] : "-") + " kot=" + (kots.Contains(sid) ? kots[sid] : "-"));
                }
            }
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }
}

public static class PngRender
{
    /// <summary>KK_PNG="metin;solPay;sagPay;altPay;ustPay;dosya": metni içeren ilk DBText çevresini PNG'ye çizer.</summary>
    public static void Render(Autodesk.AutoCAD.DatabaseServices.Database db, Action<string> log)
    {
        string spec = Environment.GetEnvironmentVariable("KK_PNG");
        if (string.IsNullOrEmpty(spec)) return;
        var p = spec.Split(';');
        double sol = double.Parse(p[1]), sag = double.Parse(p[2]), alt = double.Parse(p[3]), ust = double.Parse(p[4]);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var bt = (Autodesk.AutoCAD.DatabaseServices.BlockTable)tr.GetObject(db.BlockTableId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
            var ms = (Autodesk.AutoCAD.DatabaseServices.BlockTableRecord)tr.GetObject(bt[Autodesk.AutoCAD.DatabaseServices.BlockTableRecord.ModelSpace], Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
            Point3d? anchor = null;
            var ents = new List<Autodesk.AutoCAD.DatabaseServices.Entity>();
            foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId id in ms)
            {
                var e = (Autodesk.AutoCAD.DatabaseServices.Entity)tr.GetObject(id, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                ents.Add(e);
                if (anchor == null && e is Autodesk.AutoCAD.DatabaseServices.DBText t0 && t0.TextString.Contains(p[0])) anchor = t0.Position;
            }
            if (anchor == null) { log("PNG: metin yok " + p[0]); tr.Commit(); return; }
            double x0 = anchor.Value.X - sol, x1 = anchor.Value.X + sag, y0 = anchor.Value.Y - alt, y1 = anchor.Value.Y + ust;
            const double px = 2400; double k = px / (x1 - x0); int W = (int)px, H = (int)((y1 - y0) * k);
            using (var bmp = new System.Drawing.Bitmap(W, H))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.FromArgb(33, 40, 48));
                System.Drawing.PointF P(double x, double y) => new System.Drawing.PointF((float)((x - x0) * k), (float)((y1 - y) * k));
                System.Drawing.Color C(Autodesk.AutoCAD.DatabaseServices.Entity e)
                {
                    string l = e.Layer.ToUpperInvariant();
                    if (l.Contains("ETRIYE")) return System.Drawing.Color.DodgerBlue;
                    if (l.Contains("DONATI")) return System.Drawing.Color.Cyan;
                    if (l.Contains("KOLON")) return System.Drawing.Color.LimeGreen;
                    if (l.Contains("OLCU")) return System.Drawing.Color.IndianRed;
                    if (l.Contains("KIRIS")) return System.Drawing.Color.Yellow;
                    return System.Drawing.Color.Gray;
                }
                bool In(Extents3d ex) => ex.MaxPoint.X >= x0 && ex.MinPoint.X <= x1 && ex.MaxPoint.Y >= y0 && ex.MinPoint.Y <= y1;
                foreach (var e in ents)
                {
                    Extents3d ex; try { ex = e.GeometricExtents; } catch { continue; }
                    if (!In(ex)) continue;
                    using (var pen = new System.Drawing.Pen(C(e), 1f))
                    {
                        if (e is Autodesk.AutoCAD.DatabaseServices.Line ln) g.DrawLine(pen, P(ln.StartPoint.X, ln.StartPoint.Y), P(ln.EndPoint.X, ln.EndPoint.Y));
                        else if (e is Autodesk.AutoCAD.DatabaseServices.Polyline pl)
                            for (int i = 1; i < pl.NumberOfVertices + (pl.Closed ? 1 : 0); i++)
                            { var a = pl.GetPoint2dAt(i - 1); var b = pl.GetPoint2dAt(i % pl.NumberOfVertices); g.DrawLine(pen, P(a.X, a.Y), P(b.X, b.Y)); }
                        else if (e is RotatedDimension rd)
                        {
                            g.DrawLine(pen, P(rd.XLine1Point.X, rd.DimLinePoint.Y), P(rd.XLine2Point.X, rd.DimLinePoint.Y));
                            using (var f = new System.Drawing.Font("Arial", 11f, System.Drawing.GraphicsUnit.Pixel))
                            using (var br = new System.Drawing.SolidBrush(System.Drawing.Color.White))
                                g.DrawString(rd.Measurement.ToString("0"), f, br, P(0.5 * (rd.XLine1Point.X + rd.XLine2Point.X), rd.DimLinePoint.Y));
                        }
                        else if (e is Autodesk.AutoCAD.DatabaseServices.DBText t)
                        {
                            float fh = Math.Max(6f, (float)(t.Height * k));
                            using (var f = new System.Drawing.Font("Arial", fh, System.Drawing.GraphicsUnit.Pixel))
                            using (var br = new System.Drawing.SolidBrush(System.Drawing.Color.LightGreen))
                                g.DrawString(t.TextString, f, br, P(ex.MinPoint.X, ex.MaxPoint.Y));
                        }
                    }
                }
                bmp.Save(p[5], System.Drawing.Imaging.ImageFormat.Png);
            }
            log("PNG: " + p[5]);
            tr.Commit();
        }
    }
}
