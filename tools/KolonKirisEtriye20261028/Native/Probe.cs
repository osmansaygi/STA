using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using ST4AksCizCSharp;
using ST4PlanIdCiz;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

public class KolonKirisEtriyeProbe
{
    static string result;
    static void Log(string s) { File.AppendAllText(result, s + Environment.NewLine); }

    // KD_SIK=0/1: KolonDuseySettings.KirisBolgesiSiklastir; etriye katmanındaki çizgi Y'leri dökülür.
    [CommandMethod("KKE_DRAW")]
    public void Draw()
    {
        result = Environment.GetEnvironmentVariable("KD_RESULT");
        string src = Environment.GetEnvironmentVariable("KD_SOURCE");
        bool sik = Environment.GetEnvironmentVariable("KD_SIK") == "1" || Environment.GetEnvironmentVariable("KD_SIK") == "2";
        File.WriteAllText(result, "START\r\n");
        try
        {
            var asm = typeof(PlanIdDrawingManager).Assembly;
            asm.GetType("ST4PlanIdCiz.CommandEntry")?
                .GetMethod("ConfigureNtsNextGenOverlay", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
            var st = asm.GetType("ST4PlanIdCiz.KolonDuseySettings");
            st.GetMethod("EnsureLoaded").Invoke(null, null);
            var p = st.GetProperty("KirisBolgesiSiklastir");
            bool eski = (bool)p.GetValue(null);
            p.SetValue(null, sik);
            Log("SIK=" + p.GetValue(null));
            // KD_SIK=2: TumKolonSiklastir.
            var pTum = st.GetProperty("TumKolonSiklastir");
            bool eskiTum = pTum != null && (bool)pTum.GetValue(null);
            pTum?.SetValue(null, Environment.GetEnvironmentVariable("KD_SIK") == "2");
            Log("TUM=" + pTum?.GetValue(null));
            var model = new St4Parser().Parse(src);
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                bool ok = new PlanIdDrawingManager(model).DrawKolonDuseyFromSt4(Point3d.Origin, db, doc.Editor, tr, btr, src,
                    olcek25: true, katKatCiz: false, benzerKatBirlestir: false);
                Log("DRAW ok=" + ok);
                int nEt = 0, nTxt = 0;
                foreach (ObjectId id in btr)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null) continue;
                    if (e is Line ln && e.Layer.IndexOf("ETR", StringComparison.OrdinalIgnoreCase) >= 0) nEt++;
                    if (e is DBText t && (t.TextString.Contains("/") || t.TextString.Contains("ø") || t.TextString.StartsWith("KOL"))) { nTxt++; Log("TXT " + t.TextString + " @" + t.Position.X.ToString("0") + "," + t.Position.Y.ToString("0") + " L=" + t.Layer); }
                    if (e is MText mt && (mt.Contents.Contains("/") || mt.Contents.Contains("ø"))) { nTxt++; Log("MTXT " + mt.Contents); }
                }
                Log("ETRIYE_LINES=" + nEt + " TEXTS=" + nTxt);
                string dumpPath = Environment.GetEnvironmentVariable("KD_DUMP");
                if (!string.IsNullOrEmpty(dumpPath))
                {
                    var bb = (Environment.GetEnvironmentVariable("KD_BOX") ?? "").Split(',').Select(double.Parse).ToArray();
                    var sb = new System.Text.StringBuilder();
                    bool In(Point3d q) => q.X >= bb[0] && q.X <= bb[2] && q.Y >= bb[1] && q.Y <= bb[3];
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    string F(double v) => v.ToString("0.##", inv);
                    foreach (ObjectId id in btr)
                    {
                        var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (e == null) continue;
                        if (e is Circle c && In(c.Center)) sb.AppendLine($"C|{F(c.Center.X)}|{F(c.Center.Y)}|{F(c.Radius)}|{c.Layer}");
                        else if (e is Line l && (In(l.StartPoint) || In(l.EndPoint))) sb.AppendLine($"L|{F(l.StartPoint.X)}|{F(l.StartPoint.Y)}|{F(l.EndPoint.X)}|{F(l.EndPoint.Y)}|{l.Layer}");
                        else if (e is Polyline pl && pl.NumberOfVertices > 0 && In(pl.GetPoint3dAt(0)))
                        {
                            var parts = new List<string>();
                            for (int k = 0; k < pl.NumberOfVertices; k++) { var q = pl.GetPoint2dAt(k); parts.Add(F(q.X) + "," + F(q.Y)); }
                            if (pl.Closed) parts.Add(parts[0]);
                            sb.AppendLine("P|" + string.Join(";", parts) + "|" + pl.Layer);
                        }
                        else if (e is DBText t && In(t.Position)) sb.AppendLine($"T|{F(t.Position.X)}|{F(t.Position.Y)}|{F(t.Height)}|{t.TextString}|{t.Layer}");
                        else if (e is Dimension dm)
                        {
                            Point3d q1 = dm is RotatedDimension rd ? rd.XLine1Point : dm is AlignedDimension ad ? ad.XLine1Point : dm.TextPosition;
                            Point3d q2 = dm is RotatedDimension rd2 ? rd2.XLine2Point : dm is AlignedDimension ad2 ? ad2.XLine2Point : dm.TextPosition;
                            if (In(q1) || In(q2)) sb.AppendLine($"D|{F(q1.X)}|{F(q1.Y)}|{F(q2.X)}|{F(q2.Y)}|{F(q1.X)}|{F(q1.Y)}|{F(dm.Measurement)}|{dm.GetType().Name}|{dm.Layer}");
                        }
                    }
                    File.WriteAllText(dumpPath, sb.ToString());
                }
                tr.Commit();
            }
            p.SetValue(null, eski);
            pTum?.SetValue(null, eskiTum);
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }
}

public class PoligonTara
{
    // KD_LIST: ; ayrılmış ST4 yolları. Dikdörtgen olmayan kolonları ve zarf boyutlarını yazar.
    [CommandMethod("KKE_TARA")]
    public void Tara()
    {
        string result = Environment.GetEnvironmentVariable("KD_RESULT");
        File.WriteAllText(result, "START\r\n");
        const BindingFlags IP = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
        foreach (string src in (Environment.GetEnvironmentVariable("KD_LIST") ?? "").Split(';'))
        {
            if (string.IsNullOrWhiteSpace(src)) continue;
            try
            {
                var model = new St4Parser().Parse(src);
                var m = new PlanIdDrawingManager(model);
                var gf = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
                m.GetType().GetField("_ntsDrawFactory", IP).SetValue(m, gf);
                var miHas = m.GetType().GetMethods(IP).First(x => x.Name == "HasColumnOnFloor" && x.GetParameters().Length == 2);
                var miPoly = m.GetType().GetMethods(IP).First(x => x.Name == "GetColumnPolygonForTable" && x.GetParameters().Length == 5);
                int n = 0;
                foreach (var col in model.Columns)
                {
                    for (int i = 0; i < model.Floors.Count; i++)
                    {
                        var f = model.Floors[i];
                        if (!(bool)miHas.Invoke(m, new object[] { f, col })) continue;
                        var poly = miPoly.Invoke(m, new object[] { f, col, 0.0, 0.0, gf }) as NetTopologySuite.Geometries.Geometry;
                        if (poly == null) continue;
                        var e = poly.EnvelopeInternal;
                        double fill = poly.Area / Math.Max(1.0, e.Area);
                        if (fill < 0.9 && poly.NumPoints > 6)
                        {
                            File.AppendAllText(result, $"{Path.GetFileName(src)} col={col.ColumnNo} fi={i} env={e.Width:0}x{e.Height:0} pts={poly.NumPoints} A={poly.Area:0}\r\n");
                            n++;
                        }
                        break;
                    }
                }
                File.AppendAllText(result, $"{Path.GetFileName(src)} poligon={n}\r\n");
            }
            catch (System.Exception ex) { File.AppendAllText(result, src + " FAIL " + ex.Message + "\r\n"); }
        }
        File.AppendAllText(result, "COMPLETE\r\n");
    }
}

public class AraBaslikSentetik
{
    // KD_RECTS: "x0,y0,x1,y1;..." ; kesit donatı noktaları ve etriye kutuları dökülür.
    [CommandMethod("KKE_ARA")]
    public void Ara()
    {
        string result = Environment.GetEnvironmentVariable("KD_RESULT");
        File.WriteAllText(result, "START\r\n");
        try
        {
            var model = new St4Parser().Parse(Environment.GetEnvironmentVariable("KD_SOURCE"));
            var m = new PlanIdDrawingManager(model);
            const BindingFlags IP = BindingFlags.NonPublic | BindingFlags.Instance;
            var rects = new List<(double x0, double y0, double x1, double y1)>();
            foreach (var r in Environment.GetEnvironmentVariable("KD_RECTS").Split(';'))
            {
                var v = r.Split(',').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                rects.Add((v[0], v[1], v[2], v[3]));
            }
            var mi = typeof(PlanIdDrawingManager).GetMethod("DrawPoligonKolonKesitEtriyeler", IP);
            var args = new object[mi.GetParameters().Length];
            args[0] = null; args[1] = null; args[2] = null; args[3] = rects; args[4] = 0; args[5] = -1;
            mi.Invoke(m, args);
            var pts = (List<(double x, double y)>)args[7];
            File.AppendAllText(result, "N=" + pts.Count + "\r\n");
            foreach (var p in pts.OrderBy(q => q.y).ThenBy(q => q.x))
                File.AppendAllText(result, $"PT {p.x:0.0} {p.y:0.0}\r\n");
            var eb = (System.Collections.IEnumerable)args[12];
            foreach (var b in eb) File.AppendAllText(result, "BOX " + b + "\r\n");
            File.AppendAllText(result, "COMPLETE\r\n");
        }
        catch (System.Exception ex) { File.AppendAllText(result, "FAIL " + ex + "\r\n"); }
    }
}
