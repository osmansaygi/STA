using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

// Pafta 1/100 denemesi (OF_MODE): kapama / dusey / mantar / temeldonati / kolon; OF_SCALE=100 -> 1/100 (detayda x2).
public class PaftaProbe
{
    const BindingFlags SP = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    static void Log(string s) => File.AppendAllText(Environment.GetEnvironmentVariable("OF_RESULT"), s + Environment.NewLine);

    // tür|katman|yükseklik|görünen ölçü değeri / desen ölçeği|metin + model kutusu
    static void Dump(Database db)
    {
        var rows = new List<string>();
        Extents3d? kutu = null;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (e == null) continue;
                try { var x = e.GeometricExtents; if (kutu.HasValue) { var k = kutu.Value; k.AddExtents(x); kutu = k; } else kutu = x; } catch { }
                string h = "", v = "", txt = "";
                if (e is DBText t) { h = t.Height.ToString("0.0", CI); txt = t.TextString; }
                else if (e is MText m) { h = m.TextHeight.ToString("0.0", CI); txt = m.Contents; }
                else if (e is Dimension d)
                {
                    h = (d.Dimtxt * d.Dimscale).ToString("0.0", CI);
                    // Görünen değer geometriden: uzatma noktaları arası (döndürülmüşte doğrultu izdüşümü) × DIMLFAC.
                    double g = d.Measurement;
                    if (d is RotatedDimension rd) { var u = new Vector3d(Math.Cos(rd.Rotation), Math.Sin(rd.Rotation), 0); g = Math.Abs((rd.XLine2Point - rd.XLine1Point).DotProduct(u)); }
                    else if (d is AlignedDimension ad) g = ad.XLine1Point.DistanceTo(ad.XLine2Point);
                    v = (g * d.Dimlfac).ToString("0.0", CI);
                    txt = "blok=";
                    try
                    {
                        var blk = (BlockTableRecord)tr.GetObject(d.DimBlockId, OpenMode.ForRead);
                        foreach (ObjectId bid in blk)
                            if (tr.GetObject(bid, OpenMode.ForRead) is MText bm) txt += bm.Text;
                    }
                    catch { }
                }
                else if (e is Hatch ha) { v = ha.PatternName + ":" + ha.PatternScale.ToString("0.###", CI); }
                rows.Add(e.GetType().Name + "|" + e.Layer + "|" + h + "|" + v + "|" + txt);
            }
            tr.Commit();
        }
        rows.Sort(StringComparer.Ordinal);
        File.WriteAllLines(Environment.GetEnvironmentVariable("OF_RESULT") + ".ent.txt", rows);
        Log("ent=" + rows.Count);
        if (kutu.HasValue) Log(string.Format(CI, "kutu={0:0},{1:0} {2:0}x{3:0}", kutu.Value.MinPoint.X, kutu.Value.MinPoint.Y,
            kutu.Value.MaxPoint.X - kutu.Value.MinPoint.X, kutu.Value.MaxPoint.Y - kutu.Value.MinPoint.Y));
    }

    [CommandMethod("OF_RUN2")]
    public static void Run()
    {
        try
        {
            var plan = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            Log("Plan=" + plan.Location);
            plan.GetTypes().First(t => t.Name == "CommandEntry").GetMethod("ConfigureNtsNextGenOverlay", SP)?.Invoke(null, null);
            string st4 = Environment.GetEnvironmentVariable("OF_ST4");
            string mode = Environment.GetEnvironmentVariable("OF_MODE");
            bool yuz = Environment.GetEnvironmentVariable("OF_SCALE") == "100";
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var parserT = plan.GetType("ST4AksCizCSharp.St4Parser", true);
            object model = parserT.GetMethod("Parse").Invoke(Activator.CreateInstance(parserT), new object[] { st4 });
            plan.GetTypes().First(t => t.Name == "GprYapiAksLabels").GetMethod("TryMergeFromGprBesideSt4", SP)?.Invoke(null, new[] { st4, model });
            var mgrT = plan.GetType("ST4PlanIdCiz.PlanIdDrawingManager", true);
            object mgr = Activator.CreateInstance(mgrT, model);
            var pafta = plan.GetType("BeykentOrtak.PaftaOlcek", false);
            using (doc.LockDocument())
            {
                long ilk = db.Handseed.Value;
                if (mode == "kapama" || mode == "dusey")
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                        object ok = mode == "kapama"
                            ? mgrT.GetMethod("DrawKapamaDetayFromSt4").Invoke(mgr, new object[] { Point3d.Origin, db, doc.Editor, tr, ms, st4, true })
                            : InvokeDefaults(mgrT.GetMethod("DrawKolonDuseyFromSt4"), mgr, new Dictionary<string, object> {
                                { "insertLl", Point3d.Origin }, { "db", db }, { "ed", doc.Editor }, { "tr", tr }, { "btr", ms }, { "st4SourcePath", st4 },
                                { "katKatCiz", true }, { "benzerKatBirlestir", true } });
                        Log(mode + " ok=" + ok);
                        tr.Commit();
                    }
                    if (yuz) Log("buyut=" + pafta.GetMethod("BuyutYeni", SP).Invoke(null, new object[] { db, ilk, Point3d.Origin, 2.0, null }));
                }
                else if (mode == "mantar")
                {
                    var scaleT = plan.GetType("ST4PlanIdCiz.KalipPlanScale", true);
                    var flagsT = plan.GetType("ST4PlanIdCiz.Kalip50LayoutFlags", true);
                    var ctor = flagsT.GetConstructors().First(c => c.GetParameters().Length > 5);
                    var flags = ctor.Invoke(ctor.GetParameters().Select(p => p.Name == "includeMushroomBeams" ? true : p.Name == "copiesPerFloor" ? (object)3 : p.DefaultValue).ToArray());
                    mgrT.GetMethod("DrawFormworkPlan50").Invoke(mgr, new object[] { db, doc.Editor, Point3d.Origin, st4, Enum.Parse(scaleT, yuz ? "Hundred" : "Fifty"), 0, false, "MANTARDOSEME", flags });
                }
                else if (mode == "kolon")
                {
                    var scaleT = plan.GetType("ST4PlanIdCiz.KolonApplicationPlanScale", true);
                    mgrT.GetMethod("DrawColumnApplicationPlan").Invoke(mgr, new object[] { db, doc.Editor, Point3d.Origin, st4, Enum.Parse(scaleT, yuz ? "Hundred" : "Fifty"), false, true, true, true, true });
                }
                else if (mode == "temeldonati")
                {
                    var layT = plan.GetType("ST4PlanIdCiz.PlanIdDrawingManager+TemelPlanCopyLayout", true);
                    var scaleT = plan.GetType("ST4PlanIdCiz.TemelFoundationPlanScale", true);
                    var copies = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(layT));
                    var draw = mgrT.GetMethods().First(m => m.Name == "DrawFoundationPlanWithSections" && m.GetParameters().Length == 8);
                    var zT = plan.GetTypes().First(t => t.Name == "GprZimbalamaParser");
                    var zl = (IList)zT.GetMethod("TryRead", SP).Invoke(null, new object[] { st4, null });
                    int kopya = zl.Count > 0 ? 5 : 4;
                    string[] bas = kopya == 5 ? new[] { "1 - OLCU", "2 - ANA DONATI", "3 - ALT ILAVE DONATI", "4 - UST ILAVE DONATI", "5 - ZIMBALAMA DONATISI" } : null;
                    draw.Invoke(mgr, new object[] { db, doc.Editor, Point3d.Origin, st4, Enum.Parse(scaleT, yuz ? "Hundred" : "Fifty"), kopya, copies, bas });
                    var ilT = plan.GetType("ST4PlanIdCiz.TemelIlaveDenemeCommands", true);
                    ilT.GetField("YaziCarpani", SP).SetValue(null, yuz ? 1.5 : 1.0);
                    try { ilT.GetMethod("RdPlanZarflari", SP).Invoke(null, new object[] { db, doc.Editor, st4, copies }); }
                    finally { ilT.GetField("YaziCarpani", SP).SetValue(null, 1.0); }
                    if (copies.Count >= 5) Log("zim=" + mgrT.GetMethod("DrawZimbalamaPlan").Invoke(mgr, new object[] { db, doc.Editor, copies[4], zl }));
                    Log("kopya=" + copies.Count);
                }
                Log(mode + " scale=" + (yuz ? 100 : 50) + " drawn");
            }
            Dump(db);
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }

    static object InvokeDefaults(MethodInfo m, object target, Dictionary<string, object> args)
        => m.Invoke(target, m.GetParameters().Select(p => args.TryGetValue(p.Name, out var v) ? v : p.DefaultValue).ToArray());
}
