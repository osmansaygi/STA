using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

public class RealDrawingCheck
{
    const BindingFlags P = BindingFlags.Static | BindingFlags.NonPublic;
    static string result;
    static void Log(string s) { File.AppendAllText(result, s + Environment.NewLine); }
    static void Set(object o, string n, object v) { o.GetType().GetField(n).SetValue(o, v); }
    static object Get(object o, string n) { return o.GetType().GetField(n).GetValue(o); }
    static Point3d Mid(Entity e) { var b = e.GeometricExtents; return b.MinPoint + (b.MaxPoint - b.MinPoint) * .5; }
    static Extents3d Local(Entity e, double angle)
    {
        using (var copy = (Entity)e.Clone())
        {
            copy.TransformBy(Matrix3d.Rotation(-angle, Vector3d.ZAxis, Point3d.Origin));
            return copy.GeometricExtents;
        }
    }
    static double Offset(Entity label, Entity mark)
    {
        double a = label is DBText t ? t.Rotation : ((MText)label).Rotation;
        return (Mid(mark) - Mid(label)).DotProduct(new Vector3d(-Math.Sin(a), Math.Cos(a), 0));
    }

    [CommandMethod("CHECK_REAL_POZ")]
    public void Run()
    {
        result = Environment.GetEnvironmentVariable("ALIGN_RESULT");
        try
        {
            var asm = Assembly.LoadFrom(Environment.GetEnvironmentVariable("ALIGN_PLUGIN"));
            var r = asm.GetType("ST4Yardimci.DonatiMetrajRunner", true);
            var settings = asm.GetType("ST4Yardimci.DonatiMetrajSettings", true);
            settings.GetField("_loaded", P).SetValue(null, true);
            settings.GetProperty("Renkli").SetValue(null, true);
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(Environment.GetEnvironmentVariable("ALIGN_DWG"), FileOpenMode.OpenForReadAndAllShare, false, null);
                db.CloseInput(true);
                var previous = HostApplicationServices.WorkingDatabase;
                HostApplicationServices.WorkingDatabase = db;
                try
                {
                    var ht = r.GetNestedType("Hit", BindingFlags.NonPublic);
                    var pt = r.GetNestedType("Poz", BindingFlags.NonPublic);
                    var mh = r.GetNestedType("HasirHit", BindingFlags.NonPublic);
                    var mp = r.GetNestedType("HasirPoz", BindingFlags.NonPublic);
                    var hits = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(ht));
                    var mesh = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(mh));
                    var poz = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(pt));
                    var meshPoz = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(mp));
                    var labels = new Dictionary<string, ObjectId>();
                    var existing = new List<ObjectId>();
                    var fonts = new HashSet<string>();
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                        foreach (ObjectId id in ms)
                        {
                            var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            if (e == null) continue;
                            var owner = (string)r.GetMethod("MetrajPozHandle", P).Invoke(null, new object[] { e });
                            if (owner != null) { existing.Add(id); continue; }
                            string raw = e is DBText tx ? tx.TextString : e is MText mt ? mt.Contents : null;
                            if (raw == null) continue;
                            string plain = (string)r.GetMethod("StripMText", P).Invoke(null, new object[] { raw });
                            var ma = new object[] { plain, null };
                            if ((bool)r.GetMethod("TryHasirLabel", P).Invoke(null, ma))
                            {
                                object h = ma[1]; Set(h, "Id", id); mesh.Add(h);
                                var p = Activator.CreateInstance(mp, true); Set(p, "No", mesh.Count);
                                foreach (string field in new[] { "Tip", "Wcm", "Hcm" }) Set(p, field, Get(h, field));
                                meshPoz.Add(p);
                            }
                            else
                            {
                                var a = new object[] { plain, 0, 0, 0, 0.0 };
                                if (!(bool)r.GetMethod("TryParse", P).Invoke(null, a)) continue;
                                var h = Activator.CreateInstance(ht, true); Set(h, "Id", id); Set(h, "Cap", a[2]); Set(h, "Boy", a[3]); hits.Add(h);
                                var p = Activator.CreateInstance(pt, true); Set(p, "No", hits.Count); Set(p, "Cap", a[2]); Set(p, "BoyCm", a[3]); poz.Add(p);
                            }
                            labels[e.Handle.ToString()] = id;
                            ObjectId style = e is DBText st ? st.TextStyleId : ((MText)e).TextStyleId;
                            var font = (TextStyleTableRecord)tr.GetObject(style, OpenMode.ForRead);
                            fonts.Add(font.Name + "|" + font.FileName);
                        }
                        Log("SOURCE labels=" + labels.Count + " rebar=" + hits.Count + " mesh=" + mesh.Count + " existingMarks=" + existing.Count);
                        foreach (var font in fonts) Log("FONT " + font);
                        int oldBalloons = 0, oldShift = 0;
                        foreach (ObjectId id in existing)
                        {
                            var e = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                            var owner = (string)r.GetMethod("MetrajPozHandle", P).Invoke(null, new object[] { e });
                            if (e is Polyline && labels.TryGetValue(owner, out ObjectId labelId))
                            {
                                var label = (Entity)tr.GetObject(labelId, OpenMode.ForRead);
                                double off = Offset(label, e); oldBalloons++;
                                if (Math.Abs(off) > .1) oldShift++;
                            }
                            e.Erase();
                        }
                        Log("EXISTING balloons=" + oldBalloons + " offsetAbove0.1=" + oldShift);
                        tr.Commit();
                    }
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        r.GetMethod("StampLabelsFast", P).Invoke(null, new object[] { tr, db, hits, poz });
                        r.GetMethod("StampHasirPozFast", P).Invoke(null, new object[] { tr, db, mesh, meshPoz });
                        tr.Commit();
                    }
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                        int count = 0, fail = 0; double maximum = 0, maxGapError = 0;
                        foreach (ObjectId id in ms)
                        {
                            var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            if (!(e is Polyline)) continue;
                            var owner = (string)r.GetMethod("MetrajPozHandle", P).Invoke(null, new object[] { e });
                            if (owner == null || !labels.TryGetValue(owner, out ObjectId labelId)) continue;
                            var label = (Entity)tr.GetObject(labelId, OpenMode.ForRead);
                            double off = Math.Abs(Offset(label, e)); maximum = Math.Max(off, maximum); count++;
                            if (off > .001) { fail++; if (fail < 8) Log("FAIL label=" + owner + " offset=" + off); }
                            double angle = label is DBText text ? text.Rotation : ((MText)label).Rotation;
                            double gapError = Math.Abs(Local(label, angle).MinPoint.X - Local(e, angle).MaxPoint.X - 5.5);
                            maxGapError = Math.Max(maxGapError, gapError);
                            if (gapError > .001) { fail++; if (fail < 8) Log("FAIL gap label=" + owner + " error=" + gapError); }
                        }
                        Log("RESULT balloons=" + count + " failures=" + fail + " maxNormalOffset=" + maximum + " maxGapError=" + maxGapError);
                        if (count == labels.Count && fail == 0 && count > 0) Log("COMPLETE");
                        else Log("REPRODUCED_OR_INCOMPLETE");
                    }
                }
                finally { HostApplicationServices.WorkingDatabase = previous; }
                // Deliberately no SaveAs: all regeneration and erasures were in memory.
            }
        }
        catch (System.Exception ex) { Log(ex.ToString()); }
    }
}
