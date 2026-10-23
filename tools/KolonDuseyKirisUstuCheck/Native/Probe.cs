using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using ST4AksCizCSharp;
using ST4PlanIdCiz;
using NetTopologySuite.Geometries;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

public class KolonDuseyProbe
{
    static string result;
    const BindingFlags IP = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Log(string s) { File.AppendAllText(result, s + Environment.NewLine); }
    static object Call(object o, string m, params object[] a)
    {
        var mi = o.GetType().GetMethods(IP | BindingFlags.Public).First(x => x.Name == m && x.GetParameters().Length == a.Length);
        return mi.Invoke(o, a);
    }
    static void SetF(object o, string f, object v) { o.GetType().GetField(f, IP).SetValue(o, v); }
    static object GetF(object o, string f) { return o.GetType().GetField(f, IP).GetValue(o); }

    [CommandMethod("KOLONDUSEY_PROBE")]
    public void Run()
    {
        result = Environment.GetEnvironmentVariable("KD_RESULT");
        string src = Environment.GetEnvironmentVariable("KD_SOURCE");
        File.WriteAllText(result, "START\r\n");
        try
        {
            var model = new St4Parser().Parse(src);
            var m = new PlanIdDrawingManager(model);
            var gf = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
            SetF(m, "_ntsDrawFactory", gf);
            Log("BaseKotu=" + model.BuildingBaseKotu);
            for (int i = 0; i < model.Floors.Count; i++)
            {
                var f = model.Floors[i];
                Log($"FLOOR idx={i} no={f.FloorNo} name={f.Name} short={f.ShortName} elev={f.ElevationM}");
            }
            var want = (Environment.GetEnvironmentVariable("KD_COLS") ?? "53,54,40,39").Split(',').Select(int.Parse).ToArray();
            foreach (int no in want)
            {
                var col = model.Columns.FirstOrDefault(c => c.ColumnNo == no);
                if (col == null) { Log("COL " + no + " yok"); continue; }
                Log($"COL {no} type={col.ColumnType} ax={col.AxisXId}/{col.AxisYId} ang={col.AngleDeg} id={col.ColumnId}");
                for (int i = 0; i < model.Floors.Count; i++)
                {
                    var f = model.Floors[i];
                    bool has = (bool)Call(m, "HasColumnOnFloor", f, col);
                    var extra = (IDictionary)Call(m, "GetColumnTableExtraData", f);
                    string ex = extra.Contains(no) ? extra[no].ToString() : "-";
                    string env = "";
                    if (has)
                    {
                        var poly = (Geometry)Call(m, "GetColumnPolygonForTable", f, col, 0.0, 0.0, gf);
                        if (poly != null) env = $" env=({poly.EnvelopeInternal.MinX:0},{poly.EnvelopeInternal.MinY:0})-({poly.EnvelopeInternal.MaxX:0},{poly.EnvelopeInternal.MaxY:0}) A={poly.Area:0}";
                    }
                    Log($"  fi={i} has={has} extra={ex}{env}");
                }
                var chain = (IList)Call(m, "BuildKolonDuseyFloorChain", col);
                Log("  chain: " + string.Join(" ", chain.Cast<object>().Select(o => o.ToString())));
                var fond = (IDictionary)Call(m, "GetColumnFoundationHeights", model.Floors[0]);
                Log("  fond[fl0]: " + (fond.Contains(no) ? fond[no].ToString() : "-"));
            }
            Log("CF count=" + model.ContinuousFoundations.Count + " slab=" + model.SlabFoundations.Count);
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }

    /// <summary>Canlı akış: aynı çizimde KOLONDUSEY ardından SUREKLITEMELACILIMLARI (antetli), KD_REPEAT kez.</summary>
    [CommandMethod("KOLONDUSEY_TEMEL")]
    public void DrawThenFoundation()
    {
        result = Environment.GetEnvironmentVariable("KD_RESULT");
        string src = Environment.GetEnvironmentVariable("KD_SOURCE");
        int rep = int.Parse(Environment.GetEnvironmentVariable("KD_REPEAT") ?? "2");
        File.WriteAllText(result, "START\r\n");
        try
        {
            typeof(PlanIdDrawingManager).Assembly.GetType("ST4PlanIdCiz.CommandEntry")?
                .GetMethod("ConfigureNtsNextGenOverlay", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            for (int r = 0; r < rep; r++)
            {
                var model = new St4Parser().Parse(src);
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    bool ok = new PlanIdDrawingManager(model).DrawKolonDuseyFromSt4(new Point3d(0, r * 20000, 0), db, doc.Editor, tr, btr, src,
                        olcek25: true, katKatCiz: true, benzerKatBirlestir: true);
                    Log("KOLONDUSEY " + r + " ok=" + ok);
                    tr.Commit();
                }
                var manager = new PlanIdDrawingManager(new St4Parser().Parse(src));
                var optionType = typeof(PlanIdDrawingManager).Assembly.GetType("ST4PlanIdCiz.KirisAcilimSecenekleri");
                var options = Activator.CreateInstance(optionType, true);
                optionType.GetField("Antetli").SetValue(options, true);
                var draw = typeof(PlanIdDrawingManager).GetMethod("DrawSurekliTemelAcilimlariSecenekli", IP);
                using (doc.LockDocument())
                    Log("SUREKLI " + r + " ok=" + draw.Invoke(manager, new object[] { db, doc.Editor, new Point3d(0, -20000 - r * 20000, 0), src, options }));
            }
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }

    [CommandMethod("KOLON_APLIKASYON")]
    public void Aplikasyon()
    {
        result = Environment.GetEnvironmentVariable("KD_RESULT");
        string src = Environment.GetEnvironmentVariable("KD_SOURCE");
        string dxf = Path.ChangeExtension(Environment.GetEnvironmentVariable("KD_DWG"), ".dxf");
        File.WriteAllText(result, "START\r\n");
        try
        {
            typeof(PlanIdDrawingManager).Assembly.GetType("ST4PlanIdCiz.CommandEntry")?
                .GetMethod("ConfigureNtsNextGenOverlay", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
            var model = new St4Parser().Parse(src);
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            using (doc.LockDocument())
                new PlanIdDrawingManager(model).DrawColumnApplicationPlan(doc.Database, doc.Editor, Point3d.Origin, src,
                    KolonApplicationPlanScale.Fifty, mergeSimilarFloors: true, drawInformationLabels: true,
                    addVerticalDetails: false, addKapamaDetails: false);
            doc.Database.DxfOut(dxf, 16, DwgVersion.Current);
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }

    [CommandMethod("KOLONDUSEY_DRAW")]
    public void Draw()
    {
        result = Environment.GetEnvironmentVariable("KD_RESULT");
        string src = Environment.GetEnvironmentVariable("KD_SOURCE");
        string dxf = Path.ChangeExtension(Environment.GetEnvironmentVariable("KD_DWG"), ".dxf");
        bool o25 = Environment.GetEnvironmentVariable("KD_O25") != "0";
        int mod = int.Parse(Environment.GetEnvironmentVariable("KD_MOD") ?? "2");
        File.WriteAllText(result, "START\r\n");
        try
        {
            typeof(PlanIdDrawingManager).Assembly.GetType("ST4PlanIdCiz.CommandEntry")?
                .GetMethod("ConfigureNtsNextGenOverlay", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
            var model = new St4Parser().Parse(src);
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                bool ok = new PlanIdDrawingManager(model).DrawKolonDuseyFromSt4(Point3d.Origin, db, doc.Editor, tr, btr, src,
                    olcek25: o25, katKatCiz: mod != 0, benzerKatBirlestir: mod == 2);
                Log("DRAW ok=" + ok);
                tr.Commit();
            }
            db.DxfOut(dxf, 16, DwgVersion.Current);
            Log("DXF " + dxf);
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }
}
