// Core Console doğrulaması (RL_TEST): yüklü Plan DLL'de (yansıma)
// 1) RadyeAsHesap.TryHesapla sonucunu GPR düğüm As tablosuyla karşılaştırır (C# portu = araştırma aracı mı),
// 2) GPR varsa CizTakim(GPR) ve üstüne CizTakim(hesap) çizer, katman/nesne sayısı ve kapsamı yazar, DWG saklar.
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
using SD = System.Drawing;

public class RadyeLegantProbe
{
    const BindingFlags SP = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    static string Dir => Environment.GetEnvironmentVariable("RL_DIR");
    static string Ad => Environment.GetEnvironmentVariable("RL_NAME");
    static void Log(string s) => File.AppendAllText(Path.Combine(Dir, Ad + ".txt"), s + Environment.NewLine);

    [CommandMethod("RL_TEST")]
    public static void Test()
    {
        try
        {
            var plan = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            Log("Plan=" + plan.Location);
            plan.GetTypes().First(t => t.Name == "CommandEntry").GetMethod("ConfigureNtsNextGenOverlay", SP)?.Invoke(null, null);
            string st4 = Environment.GetEnvironmentVariable("RL_ST4");
            var hesapT = plan.GetType("ST4PlanIdCiz.RadyeAsHesap", true);
            var args = new object[] { st4, null, null, null };
            bool ok = (bool)hesapT.GetMethod("TryHesapla", SP).Invoke(null, args);
            Log("TryHesapla=" + ok + " hata=" + args[3]);
            if (!ok) return;
            var hesap = ((IEnumerable)args[1]).Cast<object>().ToList();
            object hesapGenel = args[2];
            string Yazi(object g, int i) => (string)g.GetType().GetMethod("Yazi").Invoke(g, new object[] { i });
            Log("Hesap genel: X ust " + Yazi(hesapGenel, 0) + ", X alt " + Yazi(hesapGenel, 1) + ", Y ust " + Yazi(hesapGenel, 2) + ", Y alt " + Yazi(hesapGenel, 3));
            double F(object o, string ad) => Convert.ToDouble(o.GetType().GetField(ad).GetValue(o), CI);

            var ayirT = plan.GetType("ST4PlanIdCiz.GprRadyeAyir", true);
            string gprPath = Path.ChangeExtension(st4, ".GPR");
            List<object> gpr = null; object gprGenel = null;
            if (File.Exists(gprPath))
            {
                var a2 = new object[] { gprPath, null, null };
                if ((bool)ayirT.GetMethod("TryReadMeshFile", SP).Invoke(null, a2)) gpr = ((IEnumerable)a2[1]).Cast<object>().ToList();
                var encT = plan.GetType("ST4PlanIdCiz.WindowsAnsiEncodings", true);
                encT.GetMethod("EnsureRegistered", SP).Invoke(null, null);
                var lines = (string[])encT.GetMethod("ReadAllLines", SP, null, new[] { typeof(string) }, null).Invoke(null, new object[] { gprPath });
                var a3 = new object[] { lines, null, null };
                if ((bool)ayirT.GetMethod("TryReadGenel", SP).Invoke(null, a3)) gprGenel = a3[1];
            }
            if (gpr != null && gprGenel != null)
            {
                Log("GPR genel: X ust " + Yazi(gprGenel, 0) + ", X alt " + Yazi(gprGenel, 1) + ", Y ust " + Yazi(gprGenel, 2) + ", Y alt " + Yazi(gprGenel, 3));
                var byNo = hesap.ToDictionary(o => (int)o.GetType().GetField("No").GetValue(o));
                string[] alan = { "AsXAlt", "AsXUst", "AsYAlt", "AsYUst" };
                double[] sinir = { F(gprGenel, "AsXAlt"), F(gprGenel, "AsXUst"), F(gprGenel, "AsYAlt"), F(gprGenel, "AsYUst") };
                for (int i = 0; i < 4; i++)
                {
                    int n = 0, yakin = 0, g = 0, h = 0, yg = 0, yh = 0;
                    foreach (var d in gpr)
                    {
                        if (F(d, "Z") < -0.01) continue;
                        var c = byNo[(int)d.GetType().GetField("No").GetValue(d)];
                        double ag = F(d, alan[i]), ah = F(c, alan[i]); n++;
                        if (Math.Abs(ag - ah) <= 0.25) yakin++;
                        bool a = ag > sinir[i] + 1e-6, b = ah > sinir[i] + 1e-6;
                        if (a) g++; if (b) h++; if (a && !b) yg++; if (b && !a) yh++;
                    }
                    Log(string.Format(CI, "{0}: ±0.25 {1}/{2}; ilave GPR {3} hesap {4} yalniz GPR {5} yalniz hesap {6}", alan[i], yakin, n, g, h, yg, yh));
                }
            }

            if (Environment.GetEnvironmentVariable("RL_CIZ") != "1") return;
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var cmdT = plan.GetType("ST4PlanIdCiz.TemelIlaveDenemeCommands", true);
            var parserT = plan.GetType("ST4AksCizCSharp.St4Parser", true);
            object model = parserT.GetMethod("Parse").Invoke(Activator.CreateInstance(parserT), new object[] { st4 });
            plan.GetTypes().FirstOrDefault(t => t.Name == "GprYapiAksLabels")?.GetMethod("TryMergeFromGprBesideSt4", SP)?.Invoke(null, new[] { st4, model });
            // NTS servisi komuttaki ile aynı kurulsun
            var ntsT = plan.GetTypes().First(t => t.Name == "CommandEntry");
            var ciz = cmdT.GetMethod("CizTakim", SP);
            var ins = new Point3d(0, 0, 0);
            using (doc.LockDocument())
            {
                if (gpr != null)
                {
                    var p = new object[] { db, doc.Editor, model, st4, ins, Liste(plan, gpr), gprGenel, "GPR (STA)", "RL_TEST", null };
                    bool r = (bool)ciz.Invoke(null, p);
                    var ext = (Extents3d)p[9];
                    Log(string.Format(CI, "CizTakim GPR={0} kapsam {1:0}..{2:0} x {3:0}..{4:0}", r, ext.MinPoint.Y, ext.MaxPoint.Y, ext.MinPoint.X, ext.MaxPoint.X));
                    ins = new Point3d(0, ext.MaxPoint.Y - ext.MinPoint.Y + 2000.0, 0);
                }
                var p2 = new object[] { db, doc.Editor, model, st4, ins, Liste(plan, hesap), hesapGenel, "HESAP (GPR'SIZ, MOD 4)", "RL_TEST", null };
                bool r2 = (bool)ciz.Invoke(null, p2);
                var e2 = (Extents3d)p2[9];
                Log(string.Format(CI, "CizTakim HESAP={0} kapsam {1:0}..{2:0}", r2, e2.MinPoint.Y, e2.MaxPoint.Y));
            }
            var say = new SortedDictionary<string, int>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    if (!e.Layer.StartsWith("TEMEL ILAVE") && !e.Layer.StartsWith("DONATI YAZISI")) continue;
                    say.TryGetValue(e.Layer, out int c); say[e.Layer] = c + 1;
                    if (e is DBText t && t.TextString.StartsWith("RADYE LEGANT")) Log(string.Format(CI, "Baslik '{0}' y={1:0}", t.TextString, t.Position.Y));
                }
                tr.Commit();
            }
            foreach (var kv in say) Log("Katman " + kv.Key + ": " + kv.Value);
            string dwg = Path.Combine(Dir, Ad + "_karsilastirma.dwg");
            db.SaveAs(dwg, DwgVersion.Current);
            Log("DWG=" + dwg); Log("COMPLETE");
            var dbExt = new Extents3d(db.Extmin, db.Extmax);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                bool dolu = false;
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead))
                    try { var b = ((Entity)tr.GetObject(id, OpenMode.ForRead)).GeometricExtents; if (dolu) dbExt.AddExtents(b); else { dbExt = b; dolu = true; } } catch { }
                tr.Commit();
            }
            Render(db, Path.Combine(Dir, Ad + "_tum.png"), dbExt.MinPoint.X - 200, dbExt.MinPoint.Y - 200, dbExt.MaxPoint.X + 200, dbExt.MaxPoint.Y + 200);
            Log(string.Format(CI, "PNG tum: {0:0},{1:0} - {2:0},{3:0}", dbExt.MinPoint.X, dbExt.MinPoint.Y, dbExt.MaxPoint.X, dbExt.MaxPoint.Y));
        }
        catch (System.Exception ex)
        {
            Log("HATA " + (ex.InnerException ?? ex));
        }
    }

    // TEMELDONATI ile aynı yol (form/istem yok): 4-5 temel plan kopyası + RdPlanZarflari.
    [CommandMethod("RD_TEST")]
    public static void RdTest()
    {
        try
        {
            var plan = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            Log("Plan=" + plan.Location);
            plan.GetTypes().First(t => t.Name == "CommandEntry").GetMethod("ConfigureNtsNextGenOverlay", SP)?.Invoke(null, null);
            string st4 = Environment.GetEnvironmentVariable("RL_ST4");
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var parserT = plan.GetType("ST4AksCizCSharp.St4Parser", true);
            object model = parserT.GetMethod("Parse").Invoke(Activator.CreateInstance(parserT), new object[] { st4 });
            plan.GetTypes().FirstOrDefault(t => t.Name == "GprYapiAksLabels")?.GetMethod("TryMergeFromGprBesideSt4", SP)?.Invoke(null, new[] { st4, model });
            var mgrT = plan.GetType("ST4PlanIdCiz.PlanIdDrawingManager", true);
            var layT = plan.GetType("ST4PlanIdCiz.PlanIdDrawingManager+TemelPlanCopyLayout", true);
            var scaleT = plan.GetType("ST4PlanIdCiz.TemelFoundationPlanScale", true);
            object mgr = Activator.CreateInstance(mgrT, model);
            var copies = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(layT));
            var draw = mgrT.GetMethods().First(m => m.Name == "DrawFoundationPlanWithSections" && m.GetParameters().Length == 8);
            // TEMELDONATI: GPR zımbalama listesi doluysa 5 kopya + başlıklar.
            var zT = plan.GetTypes().First(t => t.Name == "GprZimbalamaParser");
            var zl = (ICollection)zT.GetMethod("TryRead", SP).Invoke(null, new object[] { st4, null });
            int kopya = zl.Count > 0 ? 5 : 4;
            string[] bas = kopya == 5 ? new[] { "1 - OLCU", "2 - ANA DONATI", "3 - ALT ILAVE DONATI", "4 - UST ILAVE DONATI", "5 - ZIMBALAMA DONATISI" } : null;
            Log("Zimbalama=" + zl.Count + " kopya=" + kopya);
            using (doc.LockDocument())
            {
                var insXy = (Environment.GetEnvironmentVariable("RD_INS") ?? "0,0").Split(',').Select(v => double.Parse(v, CI)).ToArray();
                Log("Ins=" + insXy[0].ToString(CI) + "," + insXy[1].ToString(CI));
                draw.Invoke(mgr, new object[] { db, doc.Editor, new Point3d(insXy[0], insXy[1], 0), st4, Enum.ToObject(scaleT, 1), kopya, copies, bas });
                Log("Kopya=" + copies.Count);
                if (copies.Count < 4) return;
                Log("RdPlanZarflari basladi");
                plan.GetType("ST4PlanIdCiz.TemelIlaveDenemeCommands", true).GetMethod("RdPlanZarflari", SP).Invoke(null, new object[] { db, doc.Editor, st4, copies });
                Log("RdPlanZarflari bitti");
            }
            var say = new SortedDictionary<string, int>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead))
                {
                    var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    if (!e.Layer.StartsWith("TEMEL ILAVE")) continue;
                    say.TryGetValue(e.Layer, out int c); say[e.Layer] = c + 1;
                }
                tr.Commit();
            }
            foreach (var kv in say) Log("Katman " + kv.Key + ": " + kv.Value);
            Log("COMPLETE");
        }
        catch (System.Exception ex)
        {
            Log("HATA " + (ex.InnerException ?? ex));
        }
    }

    static object Liste(Assembly plan, List<object> items)
    {
        var t = plan.GetType("ST4PlanIdCiz.GprRadyeDugumAs", true);
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(t));
        foreach (var o in items) list.Add(o);
        return list;
    }

    static void Render(Database db, string png, double x0, double y0, double x1, double y1)
    {
        const double px = 2000;
        double k = px / (x1 - x0);
        int W = (int)px, H = Math.Max(10, (int)((y1 - y0) * k));
        using (var tr = db.TransactionManager.StartTransaction())
        using (var bmp = new SD.Bitmap(W, H))
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.Clear(SD.Color.FromArgb(33, 40, 48));
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            SD.Color C(Entity e)
            {
                short aci = e.Color.IsByLayer && lt.Has(e.Layer) ? ((LayerTableRecord)tr.GetObject(lt[e.Layer], OpenMode.ForRead)).Color.ColorIndex : e.Color.ColorIndex;
                int rgb = Autodesk.AutoCAD.Colors.EntityColor.LookUpRgb((byte)Math.Max(1, Math.Min(255, (int)aci)));
                return SD.Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            }
            SD.PointF P(double x, double y) => new SD.PointF((float)((x - x0) * k), (float)((y1 - y) * k));
            void Ciz(Entity e, int derin)
            {
                using (var pen = new SD.Pen(C(e), 1.2f))
                {
                    if (e is Line l) g.DrawLine(pen, P(l.StartPoint.X, l.StartPoint.Y), P(l.EndPoint.X, l.EndPoint.Y));
                    else if (e is Polyline pl && pl.NumberOfVertices > 1)
                    {
                        var pts = Enumerable.Range(0, pl.NumberOfVertices).Select(i => pl.GetPoint2dAt(i)).Select(q => P(q.X, q.Y)).ToList();
                        if (pl.Closed) pts.Add(pts[0]);
                        g.DrawLines(pen, pts.ToArray());
                    }
                    else if (e is Circle c)
                    {
                        var a = P(c.Center.X - c.Radius, c.Center.Y + c.Radius);
                        g.DrawEllipse(pen, a.X, a.Y, (float)(2 * c.Radius * k), (float)(2 * c.Radius * k));
                    }
                    else if (e is DBText t)
                    {
                        var q = t.HorizontalMode == TextHorizontalMode.TextLeft && t.VerticalMode == TextVerticalMode.TextBase ? t.Position : t.AlignmentPoint;
                        using (var f = new SD.Font("Arial", (float)Math.Max(6, t.Height * k * 1.2), SD.GraphicsUnit.Pixel))
                        using (var br = new SD.SolidBrush(C(e)))
                        using (var sf = new SD.StringFormat
                        {
                            Alignment = t.HorizontalMode == TextHorizontalMode.TextCenter ? SD.StringAlignment.Center
                                : t.HorizontalMode == TextHorizontalMode.TextRight ? SD.StringAlignment.Far : SD.StringAlignment.Near,
                            LineAlignment = SD.StringAlignment.Far
                        })
                            g.DrawString(t.TextString, f, br, P(q.X, q.Y), sf);
                    }
                    else if (e is Hatch h)
                    {
                        using (var fb = new SD.SolidBrush(SD.Color.FromArgb(150, C(e))))
                            for (int li = 0; li < h.NumberOfLoops; li++)
                            {
                                var lp = h.GetLoopAt(li);
                                if (!lp.IsPolyline) continue;
                                var pts = lp.Polyline.Cast<BulgeVertex>().Select(v => P(v.Vertex.X, v.Vertex.Y)).ToArray();
                                if (pts.Length > 2) g.FillPolygon(fb, pts);
                            }
                    }
                    else if (e is BlockReference br2 && derin < 3)
                    {
                        var col = new DBObjectCollection();
                        try { br2.Explode(col); } catch { }
                        foreach (DBObject o in col) { if (o is Entity ce) Ciz(ce, derin + 1); o.Dispose(); }
                    }
                }
            }
            foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead))
            {
                var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                try
                {
                    var b = e.GeometricExtents;
                    if (b.MaxPoint.X < x0 || b.MinPoint.X > x1 || b.MaxPoint.Y < y0 || b.MinPoint.Y > y1) continue;
                }
                catch { continue; }
                Ciz(e, 0);
            }
            bmp.Save(png, SD.Imaging.ImageFormat.Png);
            tr.Commit();
        }
}
}
