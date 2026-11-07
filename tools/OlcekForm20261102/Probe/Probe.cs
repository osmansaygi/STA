using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

// Ölçek formu denemesi: DENEME1 / DENEME2 / KALIP çizimini formsuz, OF_SCALE (50/100) ile çağırır.
public class OlcekFormProbe
{
    static void Log(string s) => File.AppendAllText(Environment.GetEnvironmentVariable("OF_RESULT"), s + Environment.NewLine);

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
                    string h = e is DBText t ? t.Height.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : e is MText m ? m.TextHeight.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "";
                    string txt = e is DBText t2 ? t2.TextString : e is MText m2 ? m2.Contents : "";
                    rows.Add(e.GetType().Name + "|" + e.Layer + "|" + h + "|" + txt);
                }
            }
            tr.Commit();
        }
        rows.Sort(StringComparer.Ordinal);
        File.WriteAllLines(Environment.GetEnvironmentVariable("OF_RESULT") + ".ent.txt", rows);
        Log("ent=" + rows.Count);
        Cakisma(db);
    }

    // Donatı yazısı: başka yazıyla kutu çakışması ve kutusunu kesen donatı çubuğu sayısı (model alanı).
    static void Cakisma(Database db)
    {
        var kutular = new System.Collections.Generic.List<Extents3d>(); var acilar = new System.Collections.Generic.List<double>(); int ycP = 0;
        var cubuklar = new System.Collections.Generic.List<Curve>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (e is DBText t && t.Layer == "DONATI YAZISI (BEYKENT)") { try { kutular.Add(t.GeometricExtents); acilar.Add(t.Rotation); } catch { } }
                else if (e is Curve c && e.Layer == "DONATI (BEYKENT)") cubuklar.Add(c);
            }
            int yy = 0, yc = 0;
            for (int i = 0; i < kutular.Count; i++)
            {
                var a = kutular[i];
                for (int j = i + 1; j < kutular.Count; j++)
                {
                    var b = kutular[j];
                    if (a.MinPoint.X < b.MaxPoint.X - 0.5 && b.MinPoint.X < a.MaxPoint.X - 0.5 && a.MinPoint.Y < b.MaxPoint.Y - 0.5 && b.MinPoint.Y < a.MaxPoint.Y - 0.5) yy++;
                }
                using (var pl = new Polyline())
                {
                    double k = 0.5;
                    pl.AddVertexAt(0, new Point2d(a.MinPoint.X + k, a.MinPoint.Y + k), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(a.MaxPoint.X - k, a.MinPoint.Y + k), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(a.MaxPoint.X - k, a.MaxPoint.Y - k), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(a.MinPoint.X + k, a.MaxPoint.Y - k), 0, 0, 0);
                    pl.Closed = true;
                    foreach (var c in cubuklar)
                    {
                        var pts = new Point3dCollection();
                        try { pl.IntersectWith(c, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero); } catch { }
                        if (pts.Count > 0) { yc++; try { var d = c.GetFirstDerivative(c.GetClosestPointTo(pts[0], false)); if (Math.Abs(Math.Sin(Math.Atan2(d.Y, d.X) - acilar[i])) < 0.2) ycP++; } catch { } break; }
                    }
                }
            }
            tr.Commit();
            Log("donatiYazisi=" + kutular.Count + " yaziYazi=" + yy + " yaziCubuk=" + yc + " paralel=" + ycP);
        }
    }

    [CommandMethod("OF_RUN")]
    public static void Run()
    {
        try
        {
            var plan = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            Log("Plan=" + plan.Location);
            var parserT = plan.GetType("ST4AksCizCSharp.St4Parser", true);
            var mgrT = plan.GetType("ST4PlanIdCiz.PlanIdDrawingManager", true);
            var ceT = plan.GetTypes().First(t => t.Name == "CommandEntry");
            ceT.GetMethod("ConfigureNtsNextGenOverlay", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
            string st4 = Environment.GetEnvironmentVariable("OF_ST4");
            string mode = Environment.GetEnvironmentVariable("OF_MODE");
            bool yuz = Environment.GetEnvironmentVariable("OF_SCALE") == "100";
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var model = parserT.GetMethod("Parse").Invoke(Activator.CreateInstance(parserT), new object[] { st4 });
            plan.GetTypes().First(t => t.Name == "GprYapiAksLabels").GetMethod("TryMergeFromGprBesideSt4", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)?.Invoke(null, new[] { st4, model });
            if (mode != "kalip")
                plan.GetTypes().First(t => t.Name == "SlabAxesDirections").GetMethod("PopulateCornerAxisDirections", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public).Invoke(null, new[] { model });
            var mgr = Activator.CreateInstance(mgrT, model);
            var scaleT = plan.GetType("ST4PlanIdCiz.KalipPlanScale", true);
            var flagsT = plan.GetType("ST4PlanIdCiz.Kalip50LayoutFlags", true);
            var ctor = flagsT.GetConstructors().First(c => c.GetParameters().Length > 5);
            object Flags(params string[] trues)
            {
                var ps = ctor.GetParameters();
                return ctor.Invoke(ps.Select(p => trues.Contains(p.Name) ? (object)true : p.DefaultValue).ToArray());
            }
            object scale = Enum.Parse(scaleT, yuz ? "Hundred" : "Fifty");
            var draw = mgrT.GetMethod("DrawFormworkPlan50");
            using (doc.LockDocument())
            {
                long startHandle = doc.Database.Handseed.Value;
                if (mode == "deneme1")
                    draw.Invoke(mgr, new object[] { doc.Database, doc.Editor, Point3d.Origin, st4, scale, 2, true, "DENEME1",
                        Flags("plainInsertPoint", "singleCopyPerFloor", "noAntetAndBinaSema", "noAxes", "noSections",
                              "drawSlabBoundaryPolylinesOnDosemeHattiLayer", "excludeStairAdjacentSlabsAndSlabsBelowFloorKot",
                              "drawInteriorVoidsOnBosLayerNotKalipBosluk", "classifyDosemeHattiIntoTopologyLayers") });
                else if (mode == "deneme2")
                {
                    draw.Invoke(mgr, new object[] { doc.Database, doc.Editor, Point3d.Origin, st4, scale, 0, false, "DENEME2", Flags("drawDeneme1EksenOnDonatiCopy") });
                    var kayit = mgrT.GetProperty("Deneme2EksenKayitlari", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).GetValue(mgr);
                    var dmx = mgrT.GetProperty("Deneme2DmxBySlabId", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).GetValue(mgr);
                    var d2 = plan.GetTypes().First(t => t.Name == "Deneme2DosemeDonati");
                    d2.GetMethod("Run", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { doc, kayit, startHandle, dmx, yuz ? 1.5 : 1.0 });
                }
                else
                    draw.Invoke(mgr, new object[] { doc.Database, doc.Editor, Point3d.Origin, st4, scale, 0, false, null, Activator.CreateInstance(flagsT) });
                Log(mode + " scale=" + (yuz ? 100 : 50) + " drawn");
            }
            Dump(doc.Database);
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }
}
