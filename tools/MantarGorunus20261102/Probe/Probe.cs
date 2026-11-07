using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

// Mantar fiktif kiriş: yüklü Plan DLLiyle BINAKESITI X/Y ve BINAGORUNUS 4 bakış.
public class MantarGorunusProbe
{
    static void Log(string s) => File.AppendAllText(Environment.GetEnvironmentVariable("MG_RESULT"), s + Environment.NewLine);

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
        File.WriteAllLines(Environment.GetEnvironmentVariable("MG_RESULT") + ".geo.txt", rows);
        Log("geo=" + rows.Count);
    }

    [CommandMethod("MG_RUN")]
    public static void RunAll()
    {
        try
        {
            var plan = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            Log("Plan=" + plan.Location);
            var parserT = plan.GetType("ST4AksCizCSharp.St4Parser", true);
            var mgrT = plan.GetType("ST4PlanIdCiz.PlanIdDrawingManager", true);
            plan.GetTypes().First(t => t.Name == "CommandEntry").GetMethod("ConfigureNtsNextGenOverlay", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
            string st4 = Environment.GetEnvironmentVariable("MG_ST4");
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            Func<object> mgr = () => Activator.CreateInstance(mgrT, parserT.GetMethod("Parse").Invoke(Activator.CreateInstance(parserT), new object[] { st4 }));
            var yonT = plan.GetType("ST4PlanIdCiz.BinaKesitYon", true);
            var bakisT = plan.GetType("ST4PlanIdCiz.BinaGorunusBakis", true);
            // Tanı: kat başına görünür mantar kirişleri (plan süzgeci sonrası) ve otomatik kesit hattı konumu.
            {
                var m = mgr();
                var bf = BindingFlags.NonPublic | BindingFlags.Instance;
                var ff = mgrT.GetField("_ntsDrawFactory", bf);
                if (ff.GetValue(m) == null)
                {
                    var nts = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "NetTopologySuite").GetType("NetTopologySuite.NtsGeometryServices");
                    var inst = nts.GetProperty("Instance").GetValue(null);
                    ff.SetValue(m, nts.GetMethod("CreateGeometryFactory", Type.EmptyTypes).Invoke(inst, null));
                }
                var model = mgrT.GetField("_model", bf).GetValue(m);
                double baseKot = Convert.ToDouble(model.GetType().GetProperty("BuildingBaseKotu").GetValue(model));
                var envs = new System.Collections.Generic.List<(double x0, double x1, double y0, double y1)>();
                var mv = mgrT.GetMethod("BinaViewMushroomBeams", bf);
                foreach (var fl in (System.Collections.IEnumerable)model.GetType().GetProperty("Floors").GetValue(model))
                {
                    int no = (int)fl.GetType().GetProperty("FloorNo").GetValue(fl);
                    double el = Convert.ToDouble(fl.GetType().GetProperty("ElevationM").GetValue(fl));
                    int n = 0;
                    if (mv != null)
                        foreach (var t in (System.Collections.IEnumerable)mv.Invoke(m, new object[] { no, (baseKot + el) * 100.0 }))
                        {
                            n++;
                            var tt = t.GetType();
                            var g = tt.GetField("Item2").GetValue(t);
                            var e = g.GetType().GetProperty("EnvelopeInternal").GetValue(g);
                            Func<string, object> ep = k => e.GetType().GetProperty(k).GetValue(e);
                            Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  MB kat={0} x={1:0}..{2:0} y={3:0}..{4:0} z={5:0}..{6:0}", no, ep("MinX"), ep("MaxX"), ep("MinY"), ep("MaxY"), tt.GetField("Item3").GetValue(t), tt.GetField("Item4").GetValue(t)));
                            envs.Add((Convert.ToDouble(ep("MinX")), Convert.ToDouble(ep("MaxX")), Convert.ToDouble(ep("MinY")), Convert.ToDouble(ep("MaxY"))));
                        }
                    Log("MANTAR gorunur kat=" + no + " adet=" + n);
                }
                var cutM = mgrT.GetMethod("TryFindBinaKesitCut", bf);
                foreach (var yon in Enum.GetValues(yonT))
                {
                    var args = new object[] { yon, 0.0, null, null, null, null, 0.0, 0.0 };
                    cutM.Invoke(m, args);
                    double c = (double)args[1]; bool isX = yon.ToString() == "X";
                    Log("KESIT " + yon + " cutPos=" + c + " mantarKesilen=" + envs.Count(q => isX ? (c > q.y0 && c < q.y1) : (c > q.x0 && c < q.x1)));
                }
            }
            using (doc.LockDocument())
            {
                double y = 0;
                bool tumu = true;
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
            }
            Dump(doc.Database);
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }
}
