using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

public class AntetFullCheck
{
    const BindingFlags P = BindingFlags.Static | BindingFlags.NonPublic;
    static Extents3d Local(Entity e, double angle)
    {
        using (var copy = (Entity)e.Clone())
        {
            copy.TransformBy(Matrix3d.Rotation(-angle, Vector3d.ZAxis, Point3d.Origin));
            return copy.GeometricExtents;
        }
    }
    static string result;
    static void Log(string s) { File.AppendAllText(result, s + Environment.NewLine); }

    [CommandMethod("CHECK_ANTET_FULL")]
    public void Run()
    {
        result = Environment.GetEnvironmentVariable("ALIGN_RESULT");
        try
        {
            var asm = Assembly.LoadFrom(Environment.GetEnvironmentVariable("ALIGN_PLUGIN"));
            var runner = asm.GetType("ST4Yardimci.DonatiMetrajRunner", true);
            var settings = asm.GetType("ST4Yardimci.DonatiMetrajSettings", true);
            settings.GetField("_loaded", P).SetValue(null, true);
            settings.GetProperty("Renkli").SetValue(null, true);
            var layers = (IList)settings.GetProperty("Katmanlar").GetValue(null); layers.Clear();
            var doc = AcApp.DocumentManager.MdiActiveDocument; var db = doc.Database;
            var oldIds = new HashSet<ObjectId>();
            var sources = new Dictionary<string, ObjectId>();
            int frames = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    oldIds.Add(id);
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null) continue;
                    if (e is Polyline && e.Layer.IndexOf("SheetView", StringComparison.OrdinalIgnoreCase) >= 0) frames++;
                    var owner = (string)runner.GetMethod("MetrajPozHandle", P).Invoke(null, new object[] { e });
                    if (owner != null) continue;
                    string raw = e is DBText tx ? tx.TextString : e is MText mt ? mt.Contents : null;
                    if (raw == null) continue;
                    string plain = (string)runner.GetMethod("StripMText", P).Invoke(null, new object[] { raw });
                    bool mesh = (bool)runner.GetMethod("TryHasirLabel", P).Invoke(null, new object[] { plain, null });
                    bool bars = (bool)runner.GetMethod("TryParse", P).Invoke(null, new object[] { plain, 0, 0, 0, 0.0 });
                    if (!mesh && !bars) continue;
                    sources[e.Handle.ToString()] = id;
                    if (!layers.Contains(e.Layer)) layers.Add(e.Layer);
                }
            }
            Log("BEFORE sourceLabels=" + sources.Count + " frameCandidates=" + frames + " layers=" + layers.Count);
            // Actual command flow: editor GetSelection, window collection, old
            // mark erasure, table insertion and frame-by-frame stamping.
            runner.GetMethod("RunByAntet", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
            doc.Editor.Regen();
            int marks = 0, failures = 0, leftovers = 0, numbers = 0, fills = 0;
            double worstGap = 0, worstNormal = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                var newOwners = new HashSet<string>(); var oldOwners = new List<string>();
                var newBalloons = new Dictionary<string, ObjectId>();
                foreach (ObjectId id in ms)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (!(e is Polyline)) continue;
                    var owner = (string)runner.GetMethod("MetrajPozHandle", P).Invoke(null, new object[] { e });
                    if (owner == null || !sources.TryGetValue(owner, out ObjectId labelId)) continue;
                    if (oldIds.Contains(id)) { oldOwners.Add(owner); continue; }
                    newOwners.Add(owner); newBalloons[owner] = id; marks++;
                    var label = (Entity)tr.GetObject(labelId, OpenMode.ForRead);
                    double angle = label is DBText tx ? tx.Rotation : ((MText)label).Rotation;
                    var l = Local(label, angle); var b = Local(e, angle);
                    double gapError = Math.Abs(l.MinPoint.X - b.MaxPoint.X - 5.5);
                    double normalError = Math.Abs((l.MinPoint.Y + l.MaxPoint.Y - b.MinPoint.Y - b.MaxPoint.Y) * .5);
                    worstGap = Math.Max(worstGap, gapError); worstNormal = Math.Max(worstNormal, normalError);
                    if (gapError > .001 || normalError > .001)
                    { failures++; if (failures < 8) Log("FAIL " + owner + " gapError=" + gapError + " normalError=" + normalError); }
                }
                foreach (var owner in oldOwners) if (newOwners.Contains(owner)) leftovers++;
                foreach (ObjectId id in ms)
                {
                    if (oldIds.Contains(id)) continue;
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (!(e is DBText) && !(e is Hatch)) continue;
                    var owner = (string)runner.GetMethod("MetrajPozHandle", P).Invoke(null, new object[] { e });
                    if (owner == null || !newBalloons.TryGetValue(owner, out ObjectId balloonId)) continue;
                    var label = (Entity)tr.GetObject(sources[owner], OpenMode.ForRead);
                    double angle = label is DBText tx ? tx.Rotation : ((MText)label).Rotation;
                    var b = Local((Entity)tr.GetObject(balloonId, OpenMode.ForRead), angle);
                    var box = Local(e, angle);
                    double dx = (box.MinPoint.X + box.MaxPoint.X - b.MinPoint.X - b.MaxPoint.X) * .5;
                    double dy = (box.MinPoint.Y + box.MaxPoint.Y - b.MinPoint.Y - b.MaxPoint.Y) * .5;
                    if (e is DBText number)
                    {
                        numbers++;
                        if (Math.Abs(Math.Sin(number.Rotation - angle)) > 1e-8 || Math.Cos(number.Rotation - angle) < .999999) failures++;
                    }
                    else fills++;
                    if (Math.Abs(dx) > .001 || Math.Abs(dy) > .001)
                    { failures++; if (failures < 8) Log("FAIL mark center " + owner + " dx=" + dx + " dy=" + dy); }
                }
            }
            Log("AFTER newBalloons=" + marks + " numbers=" + numbers + " fills=" + fills + " failures=" + failures + " oldBalloonsForStampedLabels=" + leftovers +
                " maxGapError=" + worstGap + " maxNormalError=" + worstNormal);
            if (marks > 0 && numbers == marks && fills == marks && failures == 0 && leftovers == 0) Log("COMPLETE");
            else Log("REPRODUCED_OR_INCOMPLETE");
            // The test opens a workspace copy and never saves it.
        }
        catch (System.Exception ex) { Log(ex.ToString()); }
    }
}
