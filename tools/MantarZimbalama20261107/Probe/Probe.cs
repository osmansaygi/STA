using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

// Mantar zımbalama: yüklü Plan DLL'iyle MANTARDOSEME çizimi (form/dosya sorusu olmadan) + GPR okuma dökümü.
public class MantarZimbalamaProbe
{
    static void Log(string s) => File.AppendAllText(Environment.GetEnvironmentVariable("MZ_RESULT"), s + Environment.NewLine);

    [CommandMethod("MZ_RUN")]
    public static void Run()
    {
        try
        {
            var plan = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            Log("Plan=" + plan.Location);
            string st4 = Environment.GetEnvironmentVariable("MZ_ST4");
            var bfS = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;
            plan.GetTypes().First(t => t.Name == "CommandEntry").GetMethod("ConfigureNtsNextGenOverlay", bfS)?.Invoke(null, null);

            var parserT = plan.GetType("ST4AksCizCSharp.St4Parser", true);
            var model = parserT.GetMethod("Parse").Invoke(Activator.CreateInstance(parserT), new object[] { st4 });
            plan.GetType("ST4PlanIdCiz.GprYapiAksLabels", true).GetMethod("TryMergeFromGprBesideSt4", bfS)?.Invoke(null, new object[] { st4, model });

            var parT = plan.GetType("ST4PlanIdCiz.GprZimbalamaParser", true);
            var args = new object[] { st4, null };
            var mOku = parT.GetMethod("TryReadMantar"); var zim = mOku != null ? (IList)mOku.Invoke(null, args) : new System.Collections.ArrayList();
            Log("zimbalama=" + zim.Count + " not=" + args[1]);
            foreach (var z in zim)
            {
                var t = z.GetType();
                Func<string, object> f = n => t.GetField(n).GetValue(z);
                Log(string.Format("  {0} kat={1} kolon={2} D={3} d={4} {5}ø{6}/{7} Asb={8} Lk={9}",
                    f("Ad"), f("KatKisa"), f("KolonNo"), f("KalinlikCm"), f("DCm"), f("Adet"), f("CapMm"), f("AralikCm"), f("AsbCm2"), f("LkCm")));
            }
            bool bos = Environment.GetEnvironmentVariable("MZ_BOS") == "1";

            var flagsT = plan.GetType("ST4PlanIdCiz.Kalip50LayoutFlags", true);
            var ctor = flagsT.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            var ps = ctor.GetParameters();
            var vals = ps.Select(p => p.DefaultValue == DBNull.Value ? null : p.DefaultValue).ToArray();
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].Name == "includeMushroomBeams") vals[i] = true;
                if (ps[i].Name == "copiesPerFloor") vals[i] = 3;
                if (ps[i].Name == "mantarZimbalama") vals[i] = bos ? null : zim;
            }
            object flags = ctor.Invoke(vals);

            var mgrT = plan.GetType("ST4PlanIdCiz.PlanIdDrawingManager", true);
            var mgr = Activator.CreateInstance(mgrT, model);
            var scaleT = plan.GetType("ST4PlanIdCiz.KalipPlanScale", true);
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var draw = mgrT.GetMethod("DrawFormworkPlan50");
            draw.Invoke(mgr, new object[] { doc.Database, doc.Editor, new Point3d(0, 0, 0), st4, Enum.Parse(scaleT, "Fifty"), 0, false, "MANTARDOSEME", flags });

            // Katman sayımı + zımbalama yazıları.
            var db = doc.Database;
            var say = new System.Collections.Generic.SortedDictionary<string, int>();
            var yazilar = new System.Collections.Generic.List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    string k = e.Layer + "|" + e.GetType().Name;
                    say[k] = say.TryGetValue(k, out int n) ? n + 1 : 1;
                    if (e is DBText tx && (tx.TextString.Contains("ZIMBALAMA") || tx.TextString.Contains("4%%c12") || tx.TextString.Contains("ø") || tx.TextString.Contains("%%c")) && e.Layer.StartsWith("DONATI YAZISI") || e is DBText t2 && t2.TextString.Contains("ZIMBALAMA"))
                    {
                        var x = e.GeometricExtents;
                        yazilar.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0},{1:0} {2}", x.MinPoint.X, x.MinPoint.Y, ((DBText)e).TextString));
                    }
                }
                tr.Commit();
            }
            foreach (var kv in say) Log("say " + kv.Key + "=" + kv.Value);
            foreach (var y in yazilar) Log("yazi " + y);
            Log("OK");
        }
        catch (System.Exception ex)
        {
            Log("HATA " + (ex.InnerException ?? ex));
        }
    }
}
