using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

public class KirisAralikProbe
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static string Dir => Environment.GetEnvironmentVariable("KA_DIR");
    static string Name => Environment.GetEnvironmentVariable("KA_NAME");
    static void Log(string text) => File.AppendAllText(Path.Combine(Dir, Name + ".txt"), text + Environment.NewLine);
    static void Check(bool ok, string text) { if (!ok) throw new System.Exception(text); Log("PASS " + text); }
    static double N(object o, string field) => Convert.ToDouble(o.GetType().GetField(field, All).GetValue(o));
    static string F(double v) => v.ToString("F5", CultureInfo.InvariantCulture);

    [CommandMethod("KA_TEST")]
    public static void Run()
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            Log("Plan=" + asm.Location);
            var managerType = asm.GetType("ST4PlanIdCiz.PlanIdDrawingManager", true);
            asm.GetType("ST4PlanIdCiz.CommandEntry").GetMethod("ConfigureNtsNextGenOverlay", All)?.Invoke(null, null);
            var st4 = Path.Combine(Dir, "input", "CK_20827_2_01.st4");
            var parser = Activator.CreateInstance(asm.GetType("ST4AksCizCSharp.St4Parser"));
            var model = parser.GetType().GetMethod("Parse", new[] { typeof(string) }).Invoke(parser, new object[] { st4 });
            var manager = Activator.CreateInstance(managerType, model);
            var optType = asm.GetType("ST4PlanIdCiz.KirisAcilimSecenekleri", true);
            var opt = Activator.CreateInstance(optType, true);
            optType.GetField("Antetli").SetValue(opt, false);
            bool modern = optType.GetField("YatayNetBoslukCm") != null;
            if (modern)
            {
                Check(N(opt, "YatayNetBoslukCm") == 4 && N(opt, "DuseyNetBoslukCm") == 3 && N(opt, "PaspayiCm") == 4, "options defaults 4/3/4");
                LayoutTests(managerType);
                // Gerçek Monolit formu: varsayılanlar, alan aktarımı ve çizim düğmesi.
                using (var form = (Form)Activator.CreateInstance(asm.GetType("ST4PlanIdCiz.KirisAcilimForm"), All, null, new[] { model }, null))
                {
                    var ft = form.GetType();
                    Check(((NumericUpDown)ft.GetField("_yatayNet", All).GetValue(form)).Value == 4m, "form default horizontal=4");
                    foreach (var pair in new[] { ("_yatayNet", 6m), ("_duseyNet", 5m), ("_paspayi", 6m) })
                        ((NumericUpDown)ft.GetField(pair.Item1, All).GetValue(form)).Value = pair.Item2;
                    form.Show(); Application.DoEvents();
                    using (var bmp = new System.Drawing.Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height)); bmp.Save(Path.Combine(Dir, "form.png")); }
                    Button FindDraw(Control c) => c.Controls.Cast<Control>().Select(x => x is Button b && b.Text == "Çiz" ? b : FindDraw(x)).FirstOrDefault(b => b != null);
                    FindDraw(form).PerformClick();
                    var chosen = ft.GetProperty("Secenekler").GetValue(form);
                    Check(N(chosen, "YatayNetBoslukCm") == 6 && N(chosen, "DuseyNetBoslukCm") == 5 && N(chosen, "PaspayiCm") == 6, "form values reach drawing options");
                }
                if (Name.Contains("custom"))
                {
                    optType.GetField("YatayNetBoslukCm").SetValue(opt, 6.0);
                    optType.GetField("DuseyNetBoslukCm").SetValue(opt, 5.0);
                    optType.GetField("PaspayiCm").SetValue(opt, 6.0);
                }
            }
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var drawName = Name.Contains("foundation") ? "DrawSurekliTemelAcilimlariSecenekli" : "DrawKirisAcilimlariSecenekli";
            Check((bool)managerType.GetMethod(drawName, All).Invoke(manager, new object[] { doc.Database, doc.Editor, Point3d.Origin, st4, opt }), "native drawing " + drawName);
            if (modern && !Name.Contains("foundation"))
            {
                var cover = (double)managerType.GetProperty("KirisAcilimMontajPaspayiCm", All).GetValue(manager);
                Check(Math.Abs(cover - (Name.Contains("custom") ? 7.5 : 5.5)) < 1e-8, "elevation cover follows form");
            }
            Fingerprint(doc.Database);
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }

    static void LayoutTests(Type managerType)
    {
        var rowType = managerType.GetNestedType("KirisKesitSira", BindingFlags.NonPublic);
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType));
        var row = Activator.CreateInstance(rowType, true);
        rowType.GetField("Adet").SetValue(row, 10); rowType.GetField("Cap").SetValue(row, 16);
        rowType.GetField("Tur").SetValue(row, "duz"); list.Add(row);
        var layout = managerType.GetMethod("KirisKesitCubukYerlesimi", All);
        List<(Point2d P, int Row)> Place(double b, double cover, double net, double vertical)
            => ((IEnumerable)layout.Invoke(null, new object[] { b, 100.0, list, true, cover, net, vertical })).Cast<object>()
                .Select(o => ((Point2d)o.GetType().GetField("Merkez", All).GetValue(o), (int)o.GetType().GetField("Sira", All).GetValue(o))).ToList();
        var regular = Place(30, 5, 4, 3); var custom = Place(30, 7, 6, 5);
        Check(regular.Count == 10 && custom.Count == 10, "all bars retained across rows");
        foreach (var spec in new[] { (Bars: regular, Cover: 5.0, Net: 4.0, Vertical: 3.0), (Bars: custom, Cover: 7.0, Net: 6.0, Vertical: 5.0) })
        {
            Check(Math.Abs(spec.Bars.Min(o => o.P.X) - spec.Cover) < 1e-8, "section cover " + spec.Cover);
            var groups = spec.Bars.GroupBy(o => o.Row).OrderBy(g => g.Key).ToList();
            foreach (var g in groups)
            {
                var x = g.Select(o => o.P.X).OrderBy(v => v).ToArray();
                Check(x.Zip(x.Skip(1), (a, b) => b - a - 1.6).All(v => v >= spec.Net - 1e-8), "horizontal net >= " + spec.Net);
            }
            Check(groups.Zip(groups.Skip(1), (a, b) => Math.Abs(a.First().P.Y - b.First().P.Y) - 1.6)
                .All(v => Math.Abs(v - spec.Vertical) < 1e-8), "vertical net = " + spec.Vertical);
        }
        // Büyük boşluk çok kollu etriye köşe aralığından büyük olsa da çubuklar sıkışmamalı.
        var wide = Place(50, 5, 15, 3);
        Check(wide.GroupBy(o => o.Row).All(g => { var x = g.Select(o => o.P.X).OrderBy(v => v).ToArray(); return x.Zip(x.Skip(1), (a, b) => b - a - 1.6).All(v => v >= 15 - 1e-8); }), "large gap respects clear spacing");
    }

    static void Fingerprint(Database db)
    {
        var rows = new List<string>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            string P(Point3d p) => F(p.X) + "," + F(p.Y) + "," + F(p.Z);
            foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead))
            {
                var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                string s = e.GetType().Name + "|" + e.Layer + "|" + e.ColorIndex;
                if (e is Line l) s += "|" + P(l.StartPoint) + "|" + P(l.EndPoint);
                else if (e is Polyline pl) for (int i = 0; i < pl.NumberOfVertices; i++) s += "|" + P(pl.GetPoint3dAt(i)) + ":" + F(pl.GetBulgeAt(i));
                else if (e is DBText t) s += "|" + t.TextString + "|" + P(t.Position) + "|" + F(t.Height);
                else if (e is MText mt) s += "|" + mt.Contents + "|" + P(mt.Location);
                else if (e is RotatedDimension d) s += "|" + P(d.XLine1Point) + "|" + P(d.XLine2Point) + "|" + P(d.DimLinePoint) + "|" + d.DimensionText;
                rows.Add(s);
            }
            tr.Commit();
        }
        rows.Sort(StringComparer.Ordinal);
        File.WriteAllLines(Path.Combine(Dir, Name + ".entities"), rows);
        Log("ENTITIES=" + rows.Count + " SHA256=" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows)))));
    }
}
