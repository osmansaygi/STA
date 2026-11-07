using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using ST4AksCizCSharp;
using ST4PlanIdCiz;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

public class PolyCountCmd
{
    [CommandMethod("KD_POLYCOUNT")]
    public void Run()
    {
        string res = Environment.GetEnvironmentVariable("KD_RESULT");
        string src = Environment.GetEnvironmentVariable("KD_SOURCE");
        var sb = new StringBuilder();
        try
        {
            typeof(PlanIdDrawingManager).Assembly.GetType("ST4PlanIdCiz.CommandEntry")?
                .GetMethod("ConfigureNtsNextGenOverlay", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
            var m = new St4Parser().Parse(src);
            var mgr = new PlanIdDrawingManager(m);
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                mgr.DrawKolonDuseyFromSt4(Point3d.Origin, db, doc.Editor, tr, btr, src, olcek25: true, katKatCiz: true, benzerKatBirlestir: true);
                tr.Commit();
            }
            var T = typeof(PlanIdDrawingManager);
            string gpr = Path.ChangeExtension(src, ".GPR");
            var gd = KolonDonatiTableDrawer.ParseKolonBetonarmeFromFile(gpr, out _, out var hcr);
            T.GetField("_kolonDuseyGpr", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(mgr, gd);
            T.GetField("_kolonDuseyGprHcr", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(mgr, hcr);
            sb.AppendLine("gpr=" + (gd == null ? -1 : gd.Count));
            string low = Environment.GetEnvironmentVariable("KD_LOWER");
            if (!string.IsNullOrEmpty(low) && gd != null) { var p = low.Split(':'); string k = p.Length == 2 ? p[0] : KolonDonatiTableDrawer.FormatStoryColumnId(m.Floors, int.Parse(p[1]), int.Parse(p[0])); if (p.Length == 2) p = new[] { p[0], "", p[1] }; if (gd.TryGetValue(k, out var v)) { gd[k] = (v.ebat, p[2], v.etriye); sb.AppendLine("LOWER " + k + " " + v.donati + " -> " + p[2]); } else sb.AppendLine("LOWER key yok " + k); }
            var cf = T.GetField("_poligonKatCizilenAdet", BindingFlags.NonPublic | BindingFlags.Instance); if (cf != null) ((IDictionary)cf.GetValue(mgr)).Clear();
            var gf = NtsGeometryServices.Instance.CreateGeometryFactory();
            var getPoly = T.GetMethod("GetColumnPolygonForTable", BindingFlags.NonPublic | BindingFlags.Instance);
            var rectsM = T.GetMethod("CollectKolonPoligonParcaDortgenleri", BindingFlags.NonPublic | BindingFlags.Static);
            var etM = T.GetMethod("DrawPoligonKolonKesitEtriyeler", BindingFlags.NonPublic | BindingFlags.Instance);
            var hasM = T.GetMethod("HasColumnOnFloor", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(FloorInfo), typeof(ColumnAxisInfo) }, null);
            foreach (var c in m.Columns.Where(c => c.ColumnType == 3))
            {
                for (int fi = 0; fi < m.Floors.Count; fi++)
                {
                    var fl = m.Floors[fi];
                    if (hasM != null && !(bool)hasM.Invoke(mgr, new object[] { fl, c })) continue;
                    var g = (Geometry)getPoly.Invoke(mgr, new object[] { fl, c, 0.0, 0.0, gf });
                    if (g == null || g.IsEmpty) continue;
                    var rects = rectsM.Invoke(null, new object[] { g });
                    var args = new object[] { null, null, g, rects, fi, c.ColumnNo, null, null, 0, 0, 0, 0, null, 0.0, null };
                    etM.Invoke(mgr, args);
                    int nUc = (int)args[8], nGv = (int)args[9], ucD = (int)args[10], gvD = (int)args[11];
                    double sMax = (double)args[13];
                    double As = nUc * Math.PI * ucD * ucD / 400.0 + nGv * Math.PI * gvD * gvD / 400.0;
                    var ugM = T.GetMethod("PoligonUstKatGereksinim", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (ugM != null) { var ua = new object[] { fi, c.ColumnNo, 0.0, 0 }; ugM.Invoke(mgr, ua); sb.Append("  ust As=" + ((double)ua[2]).ToString("0.00") + " adet=" + ua[3] + " | "); }
                    string gk = KolonDonatiTableDrawer.FormatStoryColumnId(m.Floors, fi, c.ColumnNo); if (gd != null && gd.TryGetValue(gk, out var cell)) sb.Append(" gpr=" + cell.donati + " ");
                    sb.AppendLine(string.Format("S{0} kat{1}: bas={2}ø{3} gov={4}ø{5} top={6} As={7:0.00} sMax={8}", c.ColumnNo, fi, nUc, ucD, nGv, gvD, nUc + nGv, As, sMax));
                }
            }
        }
        catch (System.Exception ex) { sb.AppendLine("FAIL " + ex); }
        sb.AppendLine("COMPLETE");
        File.WriteAllText(res, sb.ToString());
    }
}
