using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.AutoCAD.Runtime;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using ST4AksCizCSharp;
using ST4PlanIdCiz;

public class PolyGeomCmd
{
    [CommandMethod("KD_POLYGEOM")]
    public void Run()
    {
        string res = Environment.GetEnvironmentVariable("KD_RESULT");
        var sb = new StringBuilder();
        try
        {
            var m = new St4Parser().Parse(Environment.GetEnvironmentVariable("KD_SOURCE"));
            var mgr = new PlanIdDrawingManager(m);
            var gf = NtsGeometryServices.Instance.CreateGeometryFactory();
            var mi = typeof(PlanIdDrawingManager).GetMethod("GetColumnPolygonForTable", BindingFlags.NonPublic | BindingFlags.Instance);
            var fl = m.Floors[0];
            foreach (var c in m.Columns.Where(c => c.ColumnType == 3))
            {
                var g = (Geometry)mi.Invoke(mgr, new object[] { fl, c, 0.0, 0.0, gf });
                if (g == null) { sb.AppendLine(c.ColumnNo + " null"); continue; }
                var e = g.EnvelopeInternal; var ct = g.Centroid;
                sb.AppendLine(c.ColumnNo + " ang=" + c.AngleDeg + " env " + e.Width.ToString("0") + "x" + e.Height.ToString("0")
                    + " centroidRel(" + ((ct.X - e.Centre.X) / e.Width).ToString("0.00") + "," + ((ct.Y - e.Centre.Y) / e.Height).ToString("0.00") + ")");
                sb.AppendLine("  " + g.AsText());
            }
        }
        catch (System.Exception ex) { sb.AppendLine("FAIL " + ex); }
        sb.AppendLine("COMPLETE");
        File.WriteAllText(res, sb.ToString());
    }
}
