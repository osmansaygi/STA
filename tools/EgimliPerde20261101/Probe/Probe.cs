using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

// Eğimli kapama perdesi denemesi: yüklü Plan DLL'iyle BINAKESITI X/Y, BINAGORUNUS 4 bakış ve KAPAMADETAY.
public class EgimliPerdeProbe
{
    static void Log(string s) => File.AppendAllText(Environment.GetEnvironmentVariable("EP_RESULT"), s + Environment.NewLine);

    // Tüm blok tanımları + model: tür|katman|yuvarlatılmış sınırlar|metin, sıralı (blok adı GUID olduğundan yazılmaz).
    static void Dump(Database db)
    {
        var rows = new System.Collections.Generic.List<string>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId bid in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(bid, OpenMode.ForRead);
                if (btr.IsLayout && !string.Equals(btr.Name, BlockTableRecord.ModelSpace, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (ObjectId id in btr)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null || e is BlockReference) continue;
                    string ext = "";
                    try { var x = e.GeometricExtents; ext = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.0},{1:0.0},{2:0.0},{3:0.0}", x.MinPoint.X, x.MinPoint.Y, x.MaxPoint.X, x.MaxPoint.Y); } catch { }
                    string txt = e is DBText t ? t.TextString : e is MText m ? m.Contents : "";
                    rows.Add(e.GetType().Name + "|" + e.Layer + "|" + ext + "|" + txt);
                }
            }
            tr.Commit();
        }
        rows.Sort(StringComparer.Ordinal);
        File.WriteAllLines(Environment.GetEnvironmentVariable("EP_RESULT") + ".geo.txt", rows);
        Log("geo=" + rows.Count);
    }

    [CommandMethod("EP_RUN")]
    public static void RunAll()
    {
        try
        {
            var plan = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            Log("Plan=" + plan.Location);
            var parserT = plan.GetType("ST4AksCizCSharp.St4Parser", true);
            var mgrT = plan.GetType("ST4PlanIdCiz.PlanIdDrawingManager", true);
            plan.GetTypes().First(t => t.Name == "CommandEntry").GetMethod("ConfigureNtsNextGenOverlay", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
            string st4 = Environment.GetEnvironmentVariable("EP_ST4");
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            Func<object> mgr = () => Activator.CreateInstance(mgrT, parserT.GetMethod("Parse").Invoke(Activator.CreateInstance(parserT), new object[] { st4 }));
            var yonT = plan.GetType("ST4PlanIdCiz.BinaKesitYon", true);
            var bakisT = plan.GetType("ST4PlanIdCiz.BinaGorunusBakis", true);
            using (doc.LockDocument())
            {
                double y = 0;
                bool tumu = Environment.GetEnvironmentVariable("EP_ONLYKAPAMA") != "1";
                foreach (var yon in tumu ? Enum.GetValues(yonT) : Array.Empty<object>())
                {
                    var ok = mgrT.GetMethod("DrawBinaKesit").Invoke(mgr(), new object[] { doc.Database, doc.Editor, new Point3d(0, y, 0), yon });
                    Log("BINAKESITI " + yon + " ok=" + ok + " y=" + y);
                    y -= 20000;
                }
                foreach (var b in tumu ? Enum.GetValues(bakisT) : Array.Empty<object>())
                {
                    var ok = mgrT.GetMethod("DrawBinaGorunus").Invoke(mgr(), new object[] { doc.Database, doc.Editor, new Point3d(0, y, 0), b });
                    Log("BINAGORUNUS " + b + " ok=" + ok + " y=" + y);
                    y -= 20000;
                }
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    var m = mgr();
                    var ok = mgrT.GetMethod("DrawKapamaDetayFromSt4").Invoke(m, new object[] { new Point3d(60000, 0, 0), doc.Database, doc.Editor, tr, ms, st4, true });
                    Log("KAPAMADETAY ok=" + ok);
                    tr.Commit();
                }
            }
            Dump(doc.Database);
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }
}
