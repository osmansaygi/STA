using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

public class HasirDiagonalProbe
{
    [CommandMethod("HDPROBE")]
    public static void Run()
    {
        var sb = new StringBuilder();
        try
        {
            Type rt = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name.StartsWith("ST4_Yardimci"))
                .Select(a => a.GetType("ST4Yardimci.HasirDonatiRunner")).First(t => t != null);
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Static;
            string path = (string)rt.GetMethod("EnsureTempDwg", F).Invoke(null, null);
            sb.AppendLine("asm=" + rt.Assembly.GetName().Name + " tpl=" + path);

            Type cellT = rt.GetNestedType("SheetCell", BindingFlags.NonPublic);
            var list = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(cellT));
            double[][] cells = { new[] { 0.0, 0, 500, 215 }, new[] { 0.0, 400, 335, 215 }, new[] { 700.0, 0, 500, 140 }, new[] { 700.0, 400, 335, 140 } };
            foreach (var c in cells)
            {
                object o = Activator.CreateInstance(cellT);
                cellT.GetField("U0").SetValue(o, c[0]); cellT.GetField("V0").SetValue(o, c[1]);
                cellT.GetField("W").SetValue(o, c[2]); cellT.GetField("H").SetValue(o, c[3]);
                list.Add(o);
            }

            Database db = HostApplicationServices.WorkingDatabase;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                object n = rt.GetMethod("PlaceSheets", F).Invoke(null, new object[] { tr, db, path, Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, list, 5000.0, 5000.0 });
                sb.AppendLine("placed=" + n);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (var c in cells)
                {
                    int diag = 0, other = 0;
                    foreach (ObjectId id in ms)
                    {
                        var e = tr.GetObject(id, OpenMode.ForRead) as Curve;
                        if (e == null || e is Arc || e is Circle) continue;
                        Point3d a = e.StartPoint, b = e.EndPoint;
                        if (!In(a, c) || !In(b, c)) continue;
                        double dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
                        bool full = Math.Abs(dx - c[2]) < 0.5 && Math.Abs(dy - c[3]) < 0.5;
                        if (full && e.Layer.StartsWith("HASIR CIZGI")) diag++; else other++;
                    }
                    sb.AppendLine(string.Format("{0}x{1}: diagonal={2} diger={3}", c[2], c[3], diag, other));
                }
                tr.Commit();
            }
        }
        catch (System.Exception ex) { sb.AppendLine("HATA " + ex); }
        File.WriteAllText(Environment.GetEnvironmentVariable("HD_RESULT"), sb.ToString());
    }

    static bool In(Point3d p, double[] c) =>
        p.X >= c[0] - 0.5 && p.X <= c[0] + c[2] + 0.5 && p.Y >= c[1] - 0.5 && p.Y <= c[1] + c[3] + 0.5;
}
