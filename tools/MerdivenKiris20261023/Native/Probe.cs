using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

// MERDIVEN kiriş genişlik/yükseklik probu: 1/50 kalıp gibi kiriş kenarları ayrı Line,
// etiket kirişin kesişim noktasına yakın ama çizgi merkezinden uzak.
public class MerdivenKirisProbe
{
    static string result;
    static void Log(string s) { File.AppendAllText(result, s + Environment.NewLine); }
    const BindingFlags SP = BindingFlags.NonPublic | BindingFlags.Static;

    static void Add(Transaction tr, BlockTableRecord ms, Entity e, string layer)
    {
        e.SetDatabaseDefaults(); e.Layer = layer; ms.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true);
    }
    static void Lyr(Transaction tr, Database db, string n)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(n)) return; lt.UpgradeOpen();
        var r = new LayerTableRecord { Name = n }; lt.Add(r); tr.AddNewlyCreatedDBObject(r, true);
    }
    static Polyline Pl(params double[] xy)
    {
        var p = new Polyline();
        for (int i = 0; i < xy.Length / 2; i++) p.AddVertexAt(i, new Point2d(xy[2 * i], xy[2 * i + 1]), 0, 0, 0);
        return p;
    }
    static void Txt(Transaction tr, BlockTableRecord ms, double x, double y, string s, double rot = 0)
    {
        var t = new DBText();
        t.SetDatabaseDefaults();
        t.TextString = s; t.Height = 10; t.Rotation = rot;
        t.HorizontalMode = TextHorizontalMode.TextCenter; t.VerticalMode = TextVerticalMode.TextVerticalMid;
        t.Position = new Point3d(x, y, 0); t.AlignmentPoint = new Point3d(x, y, 0);
        t.Layer = "KIRIS ISMI (BEYKENT)"; ms.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
        t.AdjustAlignment(ms.Database);
    }

    [CommandMethod("MERKIRIS_PROBE")]
    public static void Run()
    {
        result = Environment.GetEnvironmentVariable("MK_RESULT");
        try { RunCore(); }
        catch (System.Exception ex) { Log("FAIL exception " + ex); }
    }

    static void RunCore()
    {
        var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name.StartsWith("ST4_Yardimci"));
        Log("Yardimci=" + asm.GetName().Name + " " + asm.Location);
        var R = asm.GetType("ST4Yardimci.MerdivenRunner", true);
        var db = AcApp.DocumentManager.MdiActiveDocument.Database;
        var ids = new List<ObjectId>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            foreach (var n in new[] { "MERDIVEN YAZI (BEYKENT)", "MERDIVEN (BEYKENT)", "KIRIS (BEYKENT)", "KIRIS ISMI (BEYKENT)" }) Lyr(tr, db, n);
            var before = new HashSet<ObjectId>(); foreach (ObjectId id in ms) before.Add(id);
            // Çıkış hattı x=60, y=-100 → 380; 10 rıht y=0..270 (basamak 30)
            Add(tr, ms, Pl(60, -100, 60, 380), "MERDIVEN YAZI (BEYKENT)");
            Add(tr, ms, new Circle(new Point3d(60, -100, 0), Vector3d.ZAxis, 5), "MERDIVEN YAZI (BEYKENT)");
            var tri = Pl(55, 375, 65, 375, 60, 385); tri.Closed = true; Add(tr, ms, tri, "MERDIVEN YAZI (BEYKENT)");
            for (int i = 0; i < 10; i++) Add(tr, ms, new Line(new Point3d(0, 30 * i, 0), new Point3d(120, 30 * i, 0)), "MERDIVEN (BEYKENT)");
            // Alt kiriş K101 30/60: yüzler y=-150 / -180 (ayrı Line, uzun)
            if (Environment.GetEnvironmentVariable("MK_POLY") == "1")
            {
                var k1 = Pl(-300, -150, 900, -150, 900, -180, -300, -180); k1.Closed = true; Add(tr, ms, k1, "KIRIS (BEYKENT)");
                var k3 = Pl(-300, 420, 900, 420, 900, 455, -300, 455); k3.Closed = true; Add(tr, ms, k3, "KIRIS (BEYKENT)");
            }
            else
            {
            Add(tr, ms, new Line(new Point3d(-300, -150, 0), new Point3d(900, -150, 0)), "KIRIS (BEYKENT)");
            Add(tr, ms, new Line(new Point3d(-300, -180, 0), new Point3d(900, -180, 0)), "KIRIS (BEYKENT)");
            // Üst kiriş K103 35/70: yüzler y=420 / 455
            Add(tr, ms, new Line(new Point3d(-300, 420, 0), new Point3d(900, 420, 0)), "KIRIS (BEYKENT)");
            Add(tr, ms, new Line(new Point3d(-300, 455, 0), new Point3d(900, 455, 0)), "KIRIS (BEYKENT)");
            }
            Txt(tr, ms, 60, -195, "K101 (30/60)");
            Txt(tr, ms, 60, 467, "K103 (35/70)");
            // Aynı doğrultuda komşu açıklık etiketi (kesişimden uzak)
            Txt(tr, ms, 700, 467, "K104 (25/40)");
            // Dik kiriş etiketi (açı paralel değil) — kesişime yakın olsa da alınmamalı
            Txt(tr, ms, 140, 400, "K105 (20/35)", Math.PI / 2);
            foreach (ObjectId id in ms) if (!before.Contains(id)) ids.Add(id);
            tr.Commit();
        }

        using (var tr = db.TransactionManager.StartTransaction())
        {
            var bArgs = new object[] { tr, ids.ToArray(), null, null, null, null };
            bool ok = (bool)R.GetMethod("TryBuildOrientedPath", SP).Invoke(null, bArgs);
            Log("TryBuildOrientedPath=" + ok + " err=" + bArgs[5]);
            if (!ok) { tr.Commit(); return; }
            var path = bArgs[2]; var rihtSegs = bArgs[3]; var supports = (IList)bArgs[4];
            Log("supports=" + supports.Count);
            // Rıht uçlarından: geriye (alt kiriş) ve ileriye (üst kiriş)
            var tryEnd = R.GetMethod("TryEndBoundary", SP);
            Report(tryEnd, new Point2d(60, 0), supports, false, "alt(K101 30/60)");
            Report(tryEnd, new Point2d(60, 270), supports, true, "ust(K103 35/70)");

            foreach (var fi in R.GetFields(SP).Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name.StartsWith("Layer")))
                Lyr(tr, db, (string)fi.GetRawConstantValue());
            var S = asm.GetType("ST4Yardimci.MerdivenSettings", true);
            S.GetMethod("EnsureLoaded").Invoke(null, null);
            S.GetProperty("TemelVar").SetValue(null, false);
            var hits = R.GetMethod("CollectHitsAlongPath", SP).Invoke(null, new[] { path, rihtSegs });
            R.GetMethod("EnsureEndpointRihtNumbers", SP).Invoke(null, new[] { path, hits });
            var groups = R.GetMethod("BuildCikisHattiGroups", SP).Invoke(null, new[] { path });
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var before = new HashSet<ObjectId>(); foreach (ObjectId id in ms) before.Add(id);
            var sectionIds = new List<HashSet<ObjectId>>();
            int drawn = (int)R.GetMethod("DrawKesitlerForGroups", SP).Invoke(null, new object[] {
                tr, db, new Point3d(5000, 0, 0), path, hits, groups, supports, rihtSegs,
                30.0, 15.0, 15.0, sectionIds, before });
            Log("kesit=" + drawn);
            var dims = new List<double>();
            var vert = new List<double>();
            foreach (ObjectId id in ms)
            {
                if (before.Contains(id)) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead);
                if (ent is Dimension d) dims.Add(Math.Round(d.Measurement, 1));
                if (ent is Line l && l.Layer.IndexOf("KESIT CIZGISI", StringComparison.OrdinalIgnoreCase) >= 0
                    && Math.Abs(l.StartPoint.X - l.EndPoint.X) < 1e-6)
                    vert.Add(Math.Round(Math.Abs(l.StartPoint.Y - l.EndPoint.Y) / 2.0, 1));
            }
            dims.Sort(); vert.Sort();
            Log("olculer(Measurement)=" + string.Join(" ", dims));
            Log("kesit_duseyler(cm)=" + string.Join(" ", vert));
            tr.Commit();
        }
        Log("COMPLETE");
    }

    static void Report(MethodInfo tryEnd, Point2d o, IList supports, bool forward, string name)
    {
        var a = new object[] { o, new Vector2d(0, 1), supports, forward, null };
        bool ok = (bool)tryEnd.Invoke(null, a);
        var b = a[4];
        if (!ok || b == null) { Log(name + ": sinir yok"); return; }
        var t = b.GetType();
        double F(string n) => (double)t.GetField(n).GetValue(b);
        Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0}: kiris={1} gap={2:0.#} genislik={3:0.#} yukseklik={4:0.#}",
            name, t.GetField("IsKiris").GetValue(b), F("GapCm"), F("ThickCm"), F("HeightCm")));
    }
}
