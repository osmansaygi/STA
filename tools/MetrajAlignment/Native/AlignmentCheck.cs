using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

public class AlignmentCheck
{
    const BindingFlags P = BindingFlags.Static | BindingFlags.NonPublic;
    static string result;
    static int checkedCount, failures;
    static void Log(string s) { File.AppendAllText(result, s + Environment.NewLine); }
    static void Check(bool ok, string context)
    { checkedCount++; if (!ok) { failures++; Log("FAIL " + context); } }
    static void Set(object o, string n, object v) { o.GetType().GetField(n).SetValue(o, v); }
    static Point3d Mid(Entity e)
    { var x = e.GeometricExtents; return x.MinPoint + (x.MaxPoint - x.MinPoint) * .5; }
    static Extents3d Local(Entity entity, double angle)
    {
        using (var copy = (Entity)entity.Clone())
        {
            copy.TransformBy(Matrix3d.Rotation(-angle, Vector3d.ZAxis, Point3d.Origin));
            return copy.GeometricExtents;
        }
    }

    [CommandMethod("CHECK_POZ_ALIGNMENT")]
    public void Run()
    {
        result = Environment.GetEnvironmentVariable("ALIGN_RESULT");
        try
        {
            var asm = Assembly.LoadFrom(Environment.GetEnvironmentVariable("ALIGN_PLUGIN"));
            var runner = asm.GetType("ST4Yardimci.DonatiMetrajRunner", true);
            var settings = asm.GetType("ST4Yardimci.DonatiMetrajSettings", true);
            settings.GetField("_loaded", P).SetValue(null, true);
            foreach (bool fast in new[] { false, true })
            foreach (bool mesh in new[] { false, true })
            foreach (bool color in new[] { false, true })
            using (var db = new Database(true, true))
            {
                var previous = HostApplicationServices.WorkingDatabase;
                HostApplicationServices.WorkingDatabase = db;
                try
                {
                    settings.GetProperty("Renkli").SetValue(null, color);
                    using (var template = new Polyline())
                    {
                        runner.GetMethod("WriteStadium", P).Invoke(null,
                            new object[] { template, Point3d.Origin, 0.0, 6.0, 10.0 });
                        runner.GetMethod("CaptureBal", P).Invoke(null, new object[] { template });
                    }
                    var hitType = runner.GetNestedType(mesh ? "HasirHit" : "Hit", BindingFlags.NonPublic);
                    var pozType = runner.GetNestedType(mesh ? "HasirPoz" : "Poz", BindingFlags.NonPublic);
                    var hits = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(hitType));
                    var pozlar = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(pozType));
                    var poz = Activator.CreateInstance(pozType, true);
                    Set(poz, "No", 16);
                    if (mesh) { Set(poz, "Tip", "Q188"); Set(poz, "Wcm", 200); Set(poz, "Hcm", 500); }
                    else { Set(poz, "Cap", 12); Set(poz, "BoyCm", 240); }
                    pozlar.Add(poz);
                    var labels = new List<ObjectId>();
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        foreach (double angle in new[] { 0.0, Math.PI / 6, Math.PI / 2, Math.PI, Math.PI * 1.5 })
                        for (int kind = 0; kind < 8; kind++)
                        {
                            var pos = new Point3d(labels.Count * 500, 100, 0);
                            Entity ent;
                            if (kind < 4)
                            {
                                var t = new DBText { TextString = mesh ? "Q188 200x500" : "7ø12/33 L=240 (alt)", Height = 10,
                                    Rotation = angle, Position = pos, WidthFactor = kind == 3 ? .65 : 1 };
                                if (kind > 0)
                                {
                                    t.HorizontalMode = kind == 1 ? TextHorizontalMode.TextLeft : kind == 2 ? TextHorizontalMode.TextCenter : TextHorizontalMode.TextRight;
                                    t.VerticalMode = kind == 1 ? TextVerticalMode.TextBottom : kind == 2 ? TextVerticalMode.TextVerticalMid : TextVerticalMode.TextTop;
                                    t.AlignmentPoint = pos;
                                }
                                ent = t;
                            }
                            else ent = new MText { Contents = mesh ? "Q188 200x500" : "7ø12/33 L=240 (alt)", TextHeight = 10,
                                Rotation = angle, Location = pos, Attachment = new[] { AttachmentPoint.TopLeft, AttachmentPoint.MiddleCenter,
                                    AttachmentPoint.BottomRight, AttachmentPoint.TopCenter }[kind - 4] };
                            ms.AppendEntity(ent); tr.AddNewlyCreatedDBObject(ent, true);
                            if (ent is DBText text) text.AdjustAlignment(db);
                            labels.Add(ent.ObjectId);
                            var hit = Activator.CreateInstance(hitType, true); Set(hit, "Id", ent.ObjectId);
                            if (mesh) { Set(hit, "Tip", "Q188"); Set(hit, "Wcm", 200); Set(hit, "Hcm", 500); }
                            else { Set(hit, "Cap", 12); Set(hit, "Boy", 240); Set(hit, "Adet", 7); }
                            hits.Add(hit);
                        }
                        tr.Commit();
                    }
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        string method = (mesh ? "StampHasirPoz" : "StampLabels") + (fast ? "Fast" : "");
                        runner.GetMethod(method, P).Invoke(null, new object[] { tr, db, hits, pozlar });
                        Log("STAMP method=" + method + " color=" + color + " labels=" + labels.Count + " ms=" + sw.ElapsedMilliseconds);
                        tr.Commit();
                    }
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                        var balloons = new List<Polyline>(); var numbers = new List<DBText>(); var fills = new List<Hatch>();
                        foreach (ObjectId id in ms)
                        {
                            var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            if (e is Polyline pl && pl.Layer == "POZ BALON (BEYKENT)") balloons.Add(pl);
                            if (e is DBText tx && tx.TextString == "16") numbers.Add(tx);
                            if (e is Hatch hatch) fills.Add(hatch);
                        }
                        Check(balloons.Count == labels.Count && numbers.Count == labels.Count, "entity counts");
                        for (int i = 0; i < labels.Count; i++)
                        {
                            var e = (Entity)tr.GetObject(labels[i], OpenMode.ForRead);
                            double angle = e is DBText t ? t.Rotation : ((MText)e).Rotation;
                            var dir = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
                            var normal = new Vector3d(-dir.Y, dir.X, 0);
                            var center = Mid(balloons[i]);
                            double offset = (center - Mid(e)).DotProduct(normal);
                            Check(Math.Abs(offset) < .001, "label center i=" + i + " offset=" + offset);
                            double numOffset = (Mid(numbers[i]) - center).Length;
                            Check(numOffset < .05, "number center i=" + i + " offset=" + numOffset);
                            var labelBox = Local(e, angle);
                            var balloonBox = Local(balloons[i], angle);
                            double gap = labelBox.MinPoint.X - balloonBox.MaxPoint.X;
                            Check(Math.Abs(gap - 5.5) < .001, "local edge gap i=" + i + " gap=" + gap);
                            Check(Math.Abs(Math.Sin(numbers[i].Rotation - angle)) < 1e-8 &&
                                Math.Cos(numbers[i].Rotation - angle) > .999999, "number rotation i=" + i);
                            if (color)
                                Check((Mid(fills[i]) - center).Length < .001, "fill center i=" + i);
                        }
                    }
                }
                finally { HostApplicationServices.WorkingDatabase = previous; }
            }
            Log("CHECKS=" + checkedCount + " FAILURES=" + failures);
            if (failures == 0) Log("COMPLETE"); else Log("REGRESSION_REPRODUCED");
        }
        catch (System.Exception ex) { Log(ex.ToString()); }
    }
}
