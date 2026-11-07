using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

// Yüklü Plan DLL'iyle (yansıma) KALIP50 / DENEME2 kalıp planı; katman sayımları KD_RESULT dosyasına.
public class KalipDeneme2Probe
{
    const BindingFlags SP = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;
    static void Log(string s) => File.AppendAllText(Environment.GetEnvironmentVariable("KD_RESULT"), s + Environment.NewLine);

    [CommandMethod("KD_KALIP")]
    public static void Kalip()
    {
        try
        {
            bool deneme2 = Environment.GetEnvironmentVariable("KD_MODE") == "DENEME2";
            var plan = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            Log("Plan=" + plan.Location + " mode=" + (deneme2 ? "DENEME2" : "KALIP50"));
            var parserT = plan.GetType("ST4AksCizCSharp.St4Parser", true);
            var mgrT = plan.GetType("ST4PlanIdCiz.PlanIdDrawingManager", true);
            var flagsT = plan.GetType("ST4PlanIdCiz.Kalip50LayoutFlags", true);
            var scaleT = plan.GetType("ST4PlanIdCiz.KalipPlanScale", true);
            plan.GetTypes().First(t => t.Name == "CommandEntry").GetMethod("ConfigureNtsNextGenOverlay", SP)?.Invoke(null, null);
            string st4 = Environment.GetEnvironmentVariable("KD_ST4");
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            object model = parserT.GetMethod("Parse").Invoke(Activator.CreateInstance(parserT), new object[] { st4 });
            var gprT = plan.GetTypes().FirstOrDefault(t => t.Name == "GprYapiAksLabels");
            gprT?.GetMethod("TryMergeFromGprBesideSt4", SP)?.Invoke(null, new[] { st4, model });
            if (deneme2)
                plan.GetType("ST4AksCizCSharp.SlabAxesDirections", true).GetMethod("PopulateCornerAxisDirections", SP).Invoke(null, new[] { model });
            object mgr = Activator.CreateInstance(mgrT, model);
            var ctor = flagsT.GetConstructors().Single(c => c.GetParameters().Length > 0);
            var args = ctor.GetParameters().Select(p => p.Name == "drawDeneme1EksenOnDonatiCopy" ? (object)deneme2 : p.DefaultValue).ToArray();
            object flags = ctor.Invoke(args);
            var draw = mgrT.GetMethod("DrawFormworkPlan50");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long startHandle = doc.Database.Handseed.Value;
            using (doc.LockDocument())
                draw.Invoke(mgr, new object[] { doc.Database, doc.Editor, Point3d.Origin, st4, Enum.Parse(scaleT, "Fifty"), 0, false, deneme2 ? "DENEME2" : null, flags });
            Log("ms=" + sw.ElapsedMilliseconds);
            if (deneme2 && Environment.GetEnvironmentVariable("KD_DONATI") != "0")
            {
                var runT = plan.GetType("ST4PlanIdCiz.Deneme2DosemeDonati", true);
                var kayit = mgrT.GetProperty("Deneme2EksenKayitlari").GetValue(mgr);
                sw.Restart();
                using (doc.LockDocument())
                    runT.GetMethod("Run", SP).Invoke(null, new object[] { doc, kayit, startHandle, mgrT.GetProperty("Deneme2DmxBySlabId").GetValue(mgr) });
                Log("donati ms=" + sw.ElapsedMilliseconds + " kayit=" + ((System.Collections.ICollection)kayit).Count);
                foreach (var r in (System.Collections.IEnumerable)kayit)
                {
                    var rt = r.GetType();
                    string P(string n) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.####}", rt.GetProperty(n).GetValue(r));
                    Log("K copy=" + P("CopyNo") + " slab=" + P("SlabId") + " x=" + P("X") + " y=" + P("Y") + " ax=" + P("AngleX") + " ay=" + P("AngleY") + " A=" + P("AreaCm2"));
                }
            }
            Dump(doc.Database);
            try { KdRender.Render(doc.Database, Log); } catch (System.Exception pe) { Log("PNG FAIL " + pe.Message); }
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }

    static void Dump(Database db)
    {
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            var byLayer = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var eksen = new List<string>();
            int n = 0;
            foreach (ObjectId id in ms)
            {
                var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                n++;
                string k = e.Layer + " | " + e.GetType().Name + (e.Layer.StartsWith("DONATI") && e.ColorIndex == 1 ? " RED" : "");
                byLayer[k] = byLayer.TryGetValue(k, out int c) ? c + 1 : 1;
                if (e.Layer.StartsWith("EKSEN") && e is Line ln)
                    eksen.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1:0.0},{2:0.0} -> {3:0.0},{4:0.0}", e.Layer, ln.StartPoint.X, ln.StartPoint.Y, ln.EndPoint.X, ln.EndPoint.Y));
            }
            Log("entities=" + n);
            foreach (var kv in byLayer) Log("L " + kv.Key + " = " + kv.Value);
            foreach (var s in eksen) Log("E " + s);
            var rot = new SortedDictionary<int, int>();
            foreach (ObjectId id2 in ms)
                if (tr.GetObject(id2, OpenMode.ForRead) is DBText dt && dt.Layer == "DONATI YAZISI (BEYKENT)")
                {
                    int d = (int)Math.Round(dt.Rotation * 180.0 / Math.PI) % 360;
                    rot[d] = rot.TryGetValue(d, out int rc) ? rc + 1 : 1;
                }
            Log("ROT " + string.Join(" ", rot.Select(kv => kv.Key + ":" + kv.Value)));
            // Donatı parmak izi: çubuk köşeleri (1 cm) + renk, etiket metni + konum; sıralı (sürüm karşılaştırması için).
            var fp = new List<string>();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (ObjectId id3 in ms)
            {
                var e3 = tr.GetObject(id3, OpenMode.ForRead) as Entity;
                if (e3 == null) continue;
                if (e3.Layer == "DONATI (BEYKENT)" && e3 is Polyline pl3)
                {
                    var sb = new System.Text.StringBuilder("P " + e3.ColorIndex + " ");
                    for (int v = 0; v < pl3.NumberOfVertices; v++)
                    {
                        var q = pl3.GetPoint2dAt(v);
                        sb.Append(Math.Round(q.X).ToString(ci)).Append(',').Append(Math.Round(q.Y).ToString(ci)).Append(' ');
                    }
                    fp.Add(sb.ToString());
                }
                else if (e3.Layer == "DONATI YAZISI (BEYKENT)" && e3 is DBText t3)
                    fp.Add("T " + t3.ColorIndex + " " + t3.TextString + " @" + Math.Round(t3.Position.X).ToString(ci) + "," + Math.Round(t3.Position.Y).ToString(ci)
                        + " r" + Math.Round(t3.Rotation * 180 / Math.PI).ToString(ci));
            }
            fp.Sort(StringComparer.Ordinal);
            File.WriteAllLines(Environment.GetEnvironmentVariable("KD_RESULT") + ".donati.txt", fp);
            // Tüm çizim parmak izi (0,1 cm): plan çizim hızlandırmasında çıktı birebir mi.
            string R(double v) => Math.Round(v, 1).ToString("0.0", ci);
            string P2(Point3d p) => R(p.X) + "," + R(p.Y);
            var all = new List<string>();
            foreach (ObjectId id4 in ms)
            {
                var e4 = tr.GetObject(id4, OpenMode.ForRead) as Entity;
                if (e4 == null) continue;
                string h = e4.GetType().Name + "|" + e4.Layer + "|" + e4.ColorIndex + "|";
                switch (e4)
                {
                    case Line l4: h += P2(l4.StartPoint) + " " + P2(l4.EndPoint); break;
                    case Polyline p4:
                        h += (p4.Closed ? "C " : "O ");
                        for (int v = 0; v < p4.NumberOfVertices; v++)
                            h += P2(p4.GetPoint3dAt(v)) + "/" + Math.Round(p4.GetBulgeAt(v), 3).ToString(ci) + " ";
                        break;
                    case Circle c4: h += P2(c4.Center) + " r" + R(c4.Radius); break;
                    case Arc a4: h += P2(a4.Center) + " r" + R(a4.Radius) + " " + Math.Round(a4.StartAngle, 3).ToString(ci) + "-" + Math.Round(a4.EndAngle, 3).ToString(ci); break;
                    case DBText t4: h += t4.TextString + " @" + P2(t4.Position) + " h" + R(t4.Height) + " r" + Math.Round(t4.Rotation, 3).ToString(ci); break;
                    case MText m4: h += m4.Contents + " @" + P2(m4.Location); break;
                    case Hatch ha:
                        double ar = 0; try { ar = ha.Area; } catch { }
                        h += ha.PatternName + " n" + ha.NumberOfLoops + " a" + Math.Round(ar).ToString(ci); break;
                    case Dimension d4:
                        double me = 0; try { me = d4.Measurement; } catch { }
                        h += Math.Round(me, 1).ToString(ci) + " @" + P2(d4.TextPosition); break;
                    case BlockReference b4: h += b4.Name + " @" + P2(b4.Position) + " s" + Math.Round(b4.ScaleFactors.X, 3).ToString(ci) + " r" + Math.Round(b4.Rotation, 3).ToString(ci); break;
                    default:
                        try { var ex4 = e4.GeometricExtents; h += P2(ex4.MinPoint) + " " + P2(ex4.MaxPoint); } catch { }
                        break;
                }
                all.Add(h);
            }
            all.Sort(StringComparer.Ordinal);
            File.WriteAllLines(Environment.GetEnvironmentVariable("KD_RESULT") + ".plan.txt", all);
            tr.Commit();
        }
    }
}
