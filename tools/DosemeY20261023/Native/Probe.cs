using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

public class DosemeYProbe
{
    static string result;
    static void Log(string s) { File.AppendAllText(result, s + Environment.NewLine); }
    const BindingFlags SP = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;

    [CommandMethod("DY_DRAW")]
    public static void Draw()
    {
        result = Environment.GetEnvironmentVariable("DY_RESULT");
        try
        {
            string st4 = Environment.GetEnvironmentVariable("DY_ST4");
            var ce = typeof(ST4PlanIdCiz.PlanIdDrawingManager).Assembly.GetTypes().First(t => t.Name == "CommandEntry");
            ce.GetMethod("ConfigureNtsNextGenOverlay", SP)?.Invoke(null, null);
            var model = new ST4AksCizCSharp.St4Parser().Parse(st4);
            ST4PlanIdCiz.GprYapiAksLabels.TryMergeFromGprBesideSt4(st4, model);
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var mgr = new ST4PlanIdCiz.PlanIdDrawingManager(model);
            using (doc.LockDocument())
                mgr.DrawFormworkPlan50(doc.Database, doc.Editor, Point3d.Origin, st4, ST4PlanIdCiz.KalipPlanScale.Fifty);
            string outDwg = Environment.GetEnvironmentVariable("DY_DWG");
            doc.Database.SaveAs(outDwg, DwgVersion.Current);
            Log("saved " + outDwg);
            DumpLabels(doc.Database);
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }

    static void DumpLabels(Database db)
    {
        var rx = new Regex(Environment.GetEnvironmentVariable("DY_LABELS") ?? "D1-33|D1-43");
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                string s = e is DBText t ? t.TextString : e is MText m ? m.Contents : null;
                if (s == null || !rx.IsMatch(s)) continue;
                var ext = e.GeometricExtents;
                Log($"LBL {e.Layer} '{s.Replace("\n", " ")}' {ext.MinPoint.X:0.0},{ext.MinPoint.Y:0.0} - {ext.MaxPoint.X:0.0},{ext.MaxPoint.Y:0.0}");
            }
            tr.Commit();
        }
    }

    [CommandMethod("DY_DUMP")]
    public static void Dump()
    {
        result = Environment.GetEnvironmentVariable("DY_RESULT");
        double[] v = Environment.GetEnvironmentVariable("DY_LINE").Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var db = AcApp.DocumentManager.MdiActiveDocument.Database;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (e == null) continue;
                Extents3d ext; try { ext = e.GeometricExtents; } catch { continue; }
                if (ext.MaxPoint.X < v[0] || ext.MinPoint.X > v[2] || ext.MaxPoint.Y < v[1] || ext.MinPoint.Y > v[3]) continue;
                string info = e.GetType().Name + " [" + e.Layer + "]";
                if (e is Line l) info += $" {l.StartPoint.X:0.0},{l.StartPoint.Y:0.0} -> {l.EndPoint.X:0.0},{l.EndPoint.Y:0.0}";
                else if (e is Polyline pl) { info += $" closed={pl.Closed} nv={pl.NumberOfVertices}"; for (int i = 0; i < pl.NumberOfVertices && i < 40; i++) info += $" ({pl.GetPoint2dAt(i).X:0.0},{pl.GetPoint2dAt(i).Y:0.0})"; }
                else if (e is DBText t) info += $" '{t.TextString}' @{t.Position.X:0.0},{t.Position.Y:0.0}";
                else if (e is MText mt) info += $" '{mt.Contents}' @{mt.Location.X:0.0},{mt.Location.Y:0.0}";
                else info += $" ext {ext.MinPoint.X:0.0},{ext.MinPoint.Y:0.0}-{ext.MaxPoint.X:0.0},{ext.MaxPoint.Y:0.0}";
                Log(info);
            }
            tr.Commit();
        }
    }

    /// <summary>Yüklü Plan DLL'iyle (adı ne olursa olsun) sürekli temel açılımı; DY_REPEAT kez aynı çizime.</summary>
    [CommandMethod("DY_SUREKLI")]
    public static void Surekli()
    {
        result = Environment.GetEnvironmentVariable("DY_RESULT");
        try
        {
            var plan = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            Log("Plan=" + plan.Location);
            var parserT = plan.GetType("ST4AksCizCSharp.St4Parser", true);
            var mgrT = plan.GetType("ST4PlanIdCiz.PlanIdDrawingManager", true);
            var optT = plan.GetType("ST4PlanIdCiz.KirisAcilimSecenekleri", true);
            plan.GetTypes().First(t => t.Name == "CommandEntry").GetMethod("ConfigureNtsNextGenOverlay", SP)?.Invoke(null, null);
            string st4 = Environment.GetEnvironmentVariable("DY_ST4");
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            int repeat = int.TryParse(Environment.GetEnvironmentVariable("DY_REPEAT"), out int r) ? r : 1;
            for (int i = 0; i < repeat; i++)
            {
                object model = parserT.GetMethod("Parse").Invoke(Activator.CreateInstance(parserT), new object[] { st4 });
                object mgr = Activator.CreateInstance(mgrT, model);
                object opt = Activator.CreateInstance(optT, true);
                optT.GetField("Antetli")?.SetValue(opt, Environment.GetEnvironmentVariable("DY_ANTET") == "1");
                var draw = mgrT.GetMethod("DrawSurekliTemelAcilimlariSecenekli", BindingFlags.NonPublic | BindingFlags.Instance);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                object ok;
                using (doc.LockDocument())
                    ok = draw.Invoke(mgr, new object[] { doc.Database, doc.Editor, new Point3d(0, i * 20000.0, 0), st4, opt });
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                Log($"run {i + 1}/{repeat} ok={ok} ms={sw.ElapsedMilliseconds}");
            }
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }

    [CommandMethod("DY_FIND")]
    public static void Find()
    {
        result = Environment.GetEnvironmentVariable("DY_RESULT");
        var db = AcApp.DocumentManager.MdiActiveDocument.Database;
        DumpLabels(db);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Polyline pl) || !pl.Layer.StartsWith("ALT DUZ")) continue;
                string info = $"ALT h={pl.Handle} nv={pl.NumberOfVertices} area={(pl.Closed ? pl.Area : 0):0}";
                for (int i = 0; i < pl.NumberOfVertices && i < 30; i++) info += $" ({pl.GetPoint2dAt(i).X:0.0},{pl.GetPoint2dAt(i).Y:0.0})";
                Log(info);
            }
            tr.Commit();
        }
    }

    static string PlStr(Polyline pl)
    {
        if (pl == null) return "null";
        string s = $"nv={pl.NumberOfVertices} area={pl.Area:0}";
        for (int i = 0; i < pl.NumberOfVertices && i < 30; i++) s += $" ({pl.GetPoint2dAt(i).X:0.0},{pl.GetPoint2dAt(i).Y:0.0})";
        return s;
    }

    [CommandMethod("DY_SEED")]
    public static void SeedProbe()
    {
        result = Environment.GetEnvironmentVariable("DY_RESULT");
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name.StartsWith("ST4_Yardimci"));
            var R = asm.GetType("ST4Yardimci.DosemeDonatiRunner", true);
            double[] v = Environment.GetEnvironmentVariable("DY_LINE").Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var a = new Point3d(v[0], v[1], 0); var b = new Point3d(v[2], v[3], 0);
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                object model = R.GetMethod("BuildAltDuzAlanModel", SP).Invoke(null, new object[] { tr, ms, a, b });
                var n = new Vector3d(-1, 0, 0);
                object[] hargs = { tr, db, ObjectId.Null };
                var hidden = R.GetMethod("HideNonMesnetForBoundary", SP).Invoke(null, hargs);
                for (int i = 4; i + 1 < v.Length; i += 2)
                {
                    var seed = new Point3d(v[i], v[i + 1], 0);
                    var tb = (Polyline)R.GetMethod("TraceSlabBoundary", SP).Invoke(null, new object[] { doc.Editor, seed, n });
                    var bs = (Polyline)R.GetMethod("BoundaryAtSeed", SP).Invoke(null, new object[] { model, seed });
                    Log($"SEED {seed.X:0},{seed.Y:0}");
                    Log("  TRACE " + PlStr(tb));
                    Log("  NTS   " + PlStr(bs));
                }
                R.GetMethod("RestoreBoundaryLayers", SP).Invoke(null, new object[] { tr, db, hidden, hargs[2] });
                tr.Abort();
            }
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }

    [CommandMethod("DY_LABELCHECK")]
    public static void LabelCheck()
    {
        result = Environment.GetEnvironmentVariable("DY_RESULT");
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name.StartsWith("ST4_Yardimci"));
            var R = asm.GetType("ST4Yardimci.DosemeDonatiRunner", true);
            var check = R.GetMethod("BoundaryHoldsOnlyOwnSlab", SP);
            if (check == null) { Log("FAIL BoundaryHoldsOnlyOwnSlab yok"); return; }
            var db = AcApp.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                object texts = R.GetMethod("CollectSlabLabelTexts", SP).Invoke(null, new object[] { tr, ms });
                Polyline Box(double x0, double y0, double x1, double y1)
                {
                    var p = new Polyline();
                    p.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0); p.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
                    p.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0); p.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
                    p.Closed = true; return p;
                }
                var spanT = R.GetNestedType("SpanRec", BindingFlags.NonPublic);
                var slabT = R.GetNestedType("SlabInfo", BindingFlags.NonPublic);
                object Span(string name)
                {
                    object slab = Activator.CreateInstance(slabT);
                    slabT.GetField("Name").SetValue(slab, name);
                    object sp = Activator.CreateInstance(spanT);
                    spanT.GetField("Slab").SetValue(sp, slab);
                    return sp;
                }
                var cases = new (string name, double[] box, bool expect)[]
                {
                    ("D1-33", new double[] { 54179.9, 506.1, 55114.9, 2111.1 }, false),
                    ("D1-43", new double[] { 54179.9, 506.1, 55114.9, 2111.1 }, false),
                    ("D1-33", new double[] { 54179.9, 1272.1, 55114.9, 2111.1 }, true),
                    ("D1-43", new double[] { 54179.9, 506.1, 55114.9, 1222.1 }, true),
                    ("", new double[] { 54179.9, 506.1, 55114.9, 2111.1 }, false),
                    ("", new double[] { 54179.9, 1272.1, 55114.9, 2111.1 }, true),
                };
                int fail = 0;
                foreach (var c in cases)
                {
                    using (var pl = Box(c.box[0], c.box[1], c.box[2], c.box[3]))
                    {
                        bool ok = (bool)check.Invoke(null, new object[] { pl, Span(c.name), texts });
                        Log($"{(ok == c.expect ? "PASS" : "FAIL")} '{c.name}' y {c.box[1]}-{c.box[3]} -> {ok} (beklenen {c.expect})");
                        if (ok != c.expect) fail++;
                    }
                }
                Log(fail == 0 ? "LABELCHECK PASS" : "LABELCHECK FAIL " + fail);
                tr.Commit();
            }
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }

    [CommandMethod("DY_MODEL")]
    public static void ModelProbe()
    {
        result = Environment.GetEnvironmentVariable("DY_RESULT");
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name.StartsWith("ST4_Yardimci"));
            var R = asm.GetType("ST4Yardimci.DosemeDonatiRunner", true);
            double[] v = Environment.GetEnvironmentVariable("DY_LINE").Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var a = new Point3d(v[0], v[1], 0); var b = new Point3d(v[2], v[3], 0);
            var db = AcApp.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                object model = null;
                try { model = R.GetMethod("BuildAltDuzAlanModel", SP).Invoke(null, new object[] { tr, ms, a, b }); }
                catch (TargetInvocationException tex) { Log("MODEL EXCEPTION " + tex.InnerException); }
                Log("model ms=" + sw.ElapsedMilliseconds);
                if (model != null)
                {
                    foreach (var f in model.GetType().GetFields())
                    {
                        var val = f.GetValue(model);
                        Log(f.Name + " = " + (val is System.Collections.ICollection c ? "count " + c.Count : val?.ToString()));
                    }
                    var envS = (Environment.GetEnvironmentVariable("DY_ENV") ?? "57500,58600,500,2500").Split(',').Select(double.Parse).ToArray();
                    var ne = model.GetType().GetField("NodedEdges").GetValue(model) as System.Collections.IEnumerable;
                    if (ne != null)
                        foreach (object s in ne)
                        {
                            var t = s.GetType();
                            double x0 = (double)t.GetField("X0").GetValue(s), y0 = (double)t.GetField("Y0").GetValue(s), x1 = (double)t.GetField("X1").GetValue(s), y1 = (double)t.GetField("Y1").GetValue(s);
                            if (Math.Max(x0, x1) < envS[0] || Math.Min(x0, x1) > envS[1] || Math.Max(y0, y1) < envS[2] || Math.Min(y0, y1) > envS[3]) continue;
                            Log($"SEG {x0:R},{y0:R} -> {x1:R},{y1:R}");
                        }
                    var bf = model.GetType().GetField("BoundFaces").GetValue(model) as System.Collections.IEnumerable;
                    if (bf != null)
                        foreach (NetTopologySuite.Geometries.Polygon p in bf)
                            if (p.EnvelopeInternal.Intersects(new NetTopologySuite.Geometries.Envelope(envS[0], envS[1], envS[2], envS[3])))
                                Log("BF area=" + p.Area.ToString("0") + " valid=" + p.IsValid + " " + p.ExteriorRing);
                }
                tr.Commit();
            }
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }

    [CommandMethod("DY_RUN")]
    public static void Run()
    {
        result = Environment.GetEnvironmentVariable("DY_RESULT");
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name.StartsWith("ST4_Yardimci"));
            Log("Yardimci=" + asm.Location);
            var R = asm.GetType("ST4Yardimci.DosemeDonatiRunner", true);
            R.GetMethod("EnsureYonLoaded", SP).Invoke(null, null);
            var yonT = R.GetNestedType("DonatiYonu");
            R.GetField("Yon", SP).SetValue(null, Enum.Parse(yonT, Environment.GetEnvironmentVariable("DY_YON") ?? "Y"));
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            DumpLabels(db);
            var before = new HashSet<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms) before.Add(id);
                tr.Commit();
            }
            double[] v = Environment.GetEnvironmentVariable("DY_LINE").Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var a = new Point3d(v[0], v[1], 0); var b = new Point3d(v[2], v[3], 0);
            var m = R.GetMethod("PlaceAlongVirtualLine", SP);
            object n = null;
            try { n = m.Invoke(null, new object[] { doc, db, doc.Editor, a, b, b - a }); }
            catch (TargetInvocationException tex) { Log("EXCEPTION " + tex.InnerException); }
            Log("placed=" + n);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (before.Contains(id)) continue;
                    var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    string info = e.GetType().Name + " " + e.Layer + (e.ColorIndex == 1 ? " [KIRMIZI]" : "");
                    if (e is Polyline pl)
                    {
                        info += $" nv={pl.NumberOfVertices} area={(pl.Closed ? pl.Area : 0):0}";
                        if (e.Layer.StartsWith("ALT DUZ") || Environment.GetEnvironmentVariable("DY_VERTS") == "1")
                            for (int i = 0; i < pl.NumberOfVertices; i++) info += $" ({pl.GetPoint2dAt(i).X:0.0},{pl.GetPoint2dAt(i).Y:0.0})";
                    }
                    if (e is DBText t) info += " '" + t.TextString + "'";
                    try { var ext = e.GeometricExtents; info += $" ext {ext.MinPoint.X:0},{ext.MinPoint.Y:0}-{ext.MaxPoint.X:0},{ext.MaxPoint.Y:0}"; } catch { }
                    Log("NEW " + info);
                }
                tr.Commit();
            }
            string outDxf = Environment.GetEnvironmentVariable("DY_OUT");
            if (!string.IsNullOrEmpty(outDxf)) db.SaveAs(outDxf, DwgVersion.Current);
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }
}
