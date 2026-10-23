using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    internal static class RadyeOtomatikRunner
    {
        public static void RunInteractive()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (var form = new RadyeOtomatikForm())
            {
                if (AcApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK) return;
                var pick = doc.Editor.GetPoint("\nRadye karşılaştırma planının sol-alt yerini seçin (cm): ");
                if (pick.Status != PromptStatus.OK) return;
                try
                {
                    using (doc.LockDocument())
                    {
                        int count = Draw(doc.Database, form.ProjectPath, pick.Value, form.IncludeReference, out string summary);
                        doc.Editor.WriteMessage("\nRADYETEMELPLANIOTOMATIK: {0}\nPlan ve üç As haritası yerleştirildi. PDF/DXF girdisi kullanılmadı.", summary);
                    }
                }
                catch (System.Exception ex) { doc.Editor.WriteMessage("\nRADYETEMELPLANIOTOMATIK: " + ex.Message); }
            }
        }

        // Mevcut RADYEILAVE formu/ayarları ve çizilmiş konturlar değiştirilmez.
        // GPR recorded As controls the covered regions. STA annotations provide
        // selected diameter/spacing only; existing bar placement rules draw anew.
        internal static int Draw(Database target, string path, Point3d insertion, bool reference, out string summary)
        {
            summary = "";
            using (var source = RadyeOtomatikSource.Open(path))
            {
                var heat = RadyeRecordedHeatmap.Read(source); // Validate before changing the target.
                source.EnsureUnchanged();
                double width = (source.MaxX - source.MinX) * source.Scale;
                int count = 0;
                using (var atomic = target.TransactionManager.StartTransaction())
                {
                    if (reference) count += CopyPlan(target, source, insertion, false);
                    Point3d automatic = reference ? insertion + new Vector3d(width + 600, 0, 0) : insertion;
                    count += CopyPlan(target, source, automatic, true);
                    using (var tr = target.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(target.BlockTableId, OpenMode.ForRead);
                        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        BeykentKatmanYukle.Ensure(tr, target, "TEMEL (BEYKENT)", 7, LineWeight.LineWeight020);
                        using (var outline = new Polyline())
                        {
                            for (int i = 0; i < source.Foundation.Count; i++)
                            {
                                var p = source.Foundation[i]; var pos = heat.Place(automatic, new Point3d(p.X, p.Y, 0));
                                outline.AddVertexAt(i, new Point2d(pos.X, pos.Y), 0, 0, 0);
                            }
                            outline.Closed = true; outline.Layer = "TEMEL (BEYKENT)"; ms.AppendEntity(outline); tr.AddNewlyCreatedDBObject(outline, true);
                        }
                        tr.Commit();
                    }
                    var jobs = heat.Jobs.Select(j => new RadyeIlaveRunner.RecordedJob { Diameter = j.Diameter, Spacing = j.Spacing, IsY = j.IsY, Upper = j.Upper,
                        Ring = j.Ring.Select(p => { var q = heat.Place(automatic, new Point3d(p.X, p.Y, 0)); return new Point2d(q.X, q.Y); }).ToList() }).ToList();
                    int bands = RadyeIlaveRunner.DrawRecorded(AcApp.DocumentManager.MdiActiveDocument, target, jobs);
                    count += DrawHeatmaps(target, heat, insertion + new Vector3d(0, -heat.Height * 100 - 1100, 0));
                    summary = Path.GetFileName(source.ProjectBase) + ": " + jobs.Count + " As alanı, " + bands + " donatı bandı, " + source.NodeCount + " düğüm, " + source.ElementCount + " eleman.";
                    source.EnsureUnchanged();
                    atomic.Commit();
                }
                return count;
            }
        }

        private static int CopyPlan(Database target, RadyeOtomatikSource source, Point3d point, bool automatic)
        {
            var layerNames = new Dictionary<ObjectId, string>();
            var isBar = new HashSet<ObjectId>();
            using (var read = source.Drawing.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in source.PlanIds)
                {
                    var e = (Entity)read.GetObject(id, OpenMode.ForRead);
                    if (automatic && (e.Layer.Equals("REBAR", StringComparison.OrdinalIgnoreCase) ||
                        e.Layer.Equals("PENC2", StringComparison.OrdinalIgnoreCase) ||
                        (e is DBText label && RadyeOtomatikSource.BarLabel.IsMatch(label.TextString ?? "")))) continue;
                    layerNames[id] = e.Layer;
                    if (e is DBText t && RadyeOtomatikSource.BarLabel.IsMatch(t.TextString ?? "")) isBar.Add(id);
                }
            }
            ObjectId targetSpace;
            using (var read = target.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)read.GetObject(target.BlockTableId, OpenMode.ForRead);
                targetSpace = bt[BlockTableRecord.ModelSpace];
            }
            var mapping = new IdMapping();
            var ids = new ObjectIdCollection(); foreach (ObjectId id in layerNames.Keys) ids.Add(id);
            source.Drawing.WblockCloneObjects(ids, targetSpace, mapping, DuplicateRecordCloning.MangleName, false);
            var transform = Matrix3d.Displacement(new Vector3d(point.X - source.MinX * source.Scale,
                point.Y - source.MinY * source.Scale, point.Z)) * Matrix3d.Scaling(source.Scale, Point3d.Origin);
            int count = 0;
            using (var tr = target.TransactionManager.StartTransaction())
            {
                ObjectId style = BaDimensionStyles.EnsureYazi(tr, target);
                var ms = (BlockTableRecord)tr.GetObject(targetSpace, OpenMode.ForWrite);
                var apps = (RegAppTable)tr.GetObject(target.RegAppTableId, OpenMode.ForRead);
                if (!apps.Has("RADYEOTOMATIK"))
                {
                    apps.UpgradeOpen(); var app = new RegAppTableRecord { Name = "RADYEOTOMATIK" };
                    apps.Add(app); tr.AddNewlyCreatedDBObject(app, true);
                }
                foreach (IdPair pair in mapping)
                {
                    if (!pair.IsCloned || !layerNames.TryGetValue(pair.Key, out string original)) continue;
                    var e = tr.GetObject(pair.Value, OpenMode.ForWrite) as Entity;
                    if (e == null) continue;
                    e.TransformBy(transform);
                    if (automatic)
                    {
                        string layer = PlanLayer(original, e, isBar.Contains(pair.Key));
                        BeykentKatmanYukle.Ensure(tr, target, layer, 7, LineWeight.LineWeight020);
                        e.Layer = layer;
                        e.ColorIndex = 256;
                        if (e is DBText text)
                        {
                            text.TextStyleId = style;
                            text.TextString = (text.TextString ?? "").Replace('ƒ', 'ø').Replace("ÿ", "")
                                .Replace(" l=", " L=").Replace("(ust)", "(üst)");
                            text.AdjustAlignment(target);
                        }
                    }
                    using (var tag = new ResultBuffer(new TypedValue(1001, "RADYEOTOMATIK"),
                        new TypedValue(1000, Path.GetFileName(source.ProjectBase)),
                        new TypedValue(1000, automatic ? "STA_KAYITLI_ILAVE" : "STA_REFERANS"))) e.XData = tag;
                    count++;
                }
                string title = automatic ? "RADYE TEMEL PLANI OTOMATİK — GPR AS ALANLARI" : "STA REFERANS — KAYITLI İLAVE PLANI";
                AddText(tr, target, ms, style, point + new Vector3d(0, (source.MaxY - source.MinY) * source.Scale + 380, 0), title, 22);
                AddText(tr, target, ms, style, point + new Vector3d(0, -380, 0),
                    automatic ? "GPR As + RAD ağ + SO1 genel As. Çap/aralık: STA seçimi (MatTem/MATBQF)." :
                    "Referans: STA MatTem.dwg kayıtlı ilave planı.", 12);
                if (automatic) AddText(tr, target, ms, style, point + new Vector3d(0, -355, 0),
                    "Karşılaştırma: As alanının dış sınır kutusu + mevcut radye bant kuralları. GPR hassasiyeti 0.01 cm²/m.", 12);
                string[] axes = { "X üst", "X alt", "Y üst", "Y alt" };
                for (int i = 0; i < 4; i++)
                    AddText(tr, target, ms, style, point + new Vector3d(0, -420 - i * 25, 0),
                        string.Format(CultureInfo.InvariantCulture, "{0}: genel As={1:0.00}; tepe ek As={2:0.00} cm²/m", axes[i], source.BaseAs[i], Math.Max(0, source.PeakAs[i] - source.BaseAs[i])), 12);
                if (!automatic)
                    for (int i = 0; i < source.Bars.Count; i++)
                        AddText(tr, target, ms, style, point + new Vector3d(0, -550 - i * 25, 0), source.Bars[i], 12);
                tr.Commit();
            }
            return count;
        }

        private static int DrawHeatmaps(Database db, RadyeRecordedHeatmap heat, Point3d origin)
        {
            int count = 0; int[] channels = { 1, 2, 3 }; string[] labels = { "X ALT", "Y UST", "Y ALT" };
            int[] colors = { 0xc8e6f3, 0x7fc8de, 0x47b9a5, 0xa4d858, 0xf2ce49, 0xed933b, 0xd64936 };
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                ObjectId style = BaDimensionStyles.EnsureYazi(tr, db);
                for (int panel = 0; panel < channels.Length; panel++)
                {
                    int ch = channels[panel]; Point3d place = origin + new Vector3d(panel * (heat.Width * 100 + 350), 0, 0);
                    string layer = "RADYE AS " + labels[panel] + " (BEYKENT)";
                    BeykentKatmanYukle.Ensure(tr, db, layer, 7, LineWeight.LineWeight009);
                    double peak = Math.Max(0, heat.Peak[ch] - heat.Base[ch]);
                    Action<List<Point3d>, int> faces = (points, rgb) =>
                    {
                        for (int j = 1; j < points.Count - 1; j++)
                        {
                            using (var solid = new Solid(heat.Place(place, points[0]), heat.Place(place, points[j]),
                                heat.Place(place, points[j + 1]), heat.Place(place, points[j + 1])))
                            {
                                solid.Layer = layer;
                                solid.Color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                                ms.AppendEntity(solid); tr.AddNewlyCreatedDBObject(solid, true); count++;
                            }
                        }
                    };
                    foreach (int[] ids in heat.Elements)
                    {
                        var points = ids.Select(i => new Point3d(heat.Nodes[i].X, heat.Nodes[i].Y, Math.Max(0, heat.As[i, ch] - heat.Base[ch]))).ToList();
                        faces(points, 0xeeeeee);
                        var triangles = new List<List<Point3d>>();
                        if (ids.Length == 3) triangles.Add(points);
                        else
                        {
                            var center = new Point3d(points.Average(p => p.X), points.Average(p => p.Y), points.Average(p => p.Z));
                            for (int j = 0; j < points.Count; j++) triangles.Add(new List<Point3d> { points[j], points[(j + 1) % points.Count], center });
                        }
                        foreach (var triangle in triangles)
                            for (int band = 0; band < colors.Length; band++) faces(ClipAs(triangle, Math.Max(1e-9, peak * band / colors.Length)), colors[band]);
                    }
                    // Draw the mesh after fills, so the source connectivity stays visible.
                    foreach (int[] ids in heat.Elements)
                        for (int j = 0; j < ids.Length; j++)
                            using (var line = new Line(heat.Place(place, heat.Nodes[ids[j]]), heat.Place(place, heat.Nodes[ids[(j + 1) % ids.Length]])))
                            { line.Layer = layer; line.ColorIndex = 8; ms.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true); count++; }
                    AddText(tr, db, ms, style, place + new Vector3d(0, heat.Height * 100 + 100, 0), labels[panel] + " ILAVE AS — GPR/RAD/SO1", 25);
                    AddText(tr, db, ms, style, place + new Vector3d(0, -65, 0), string.Format(CultureInfo.InvariantCulture,
                        "Genel As={0:0.0000}; tepe ilave={1:0.00} cm²/m. GPR hassasiyeti 0.01 cm²/m.", heat.Base[ch], peak), 14);
                    AddText(tr, db, ms, style, place + new Vector3d(0, -100, 0), "Renk konturları düğüm As değerlerinden enterpole edilmiştir; STA konturlarıyla bit eşitliği iddiası yoktur.", 12);
                }
                tr.Commit();
            }
            return count;
        }

        private static List<Point3d> ClipAs(List<Point3d> points, double level)
        {
            var output = new List<Point3d>();
            for (int i = 0; i < points.Count; i++)
            {
                Point3d a = points[i], b = points[(i + 1) % points.Count];
                bool ia = a.Z >= level, ib = b.Z >= level;
                if (ia) output.Add(a);
                if (ia != ib)
                {
                    double t = (level - a.Z) / (b.Z - a.Z);
                    output.Add(new Point3d(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y), level));
                }
            }
            return output;
        }

        private static string PlanLayer(string original, Entity entity, bool barLabel)
        {
            if (barLabel) return "DONATI YAZISI (BEYKENT)";
            if (original.Equals("REBAR", StringComparison.OrdinalIgnoreCase)) return "DONATI (BEYKENT)";
            if (original.StartsWith("COLUMN", StringComparison.OrdinalIgnoreCase) || original == "0")
                return entity is DBText ? "KOLON ISMI (BEYKENT)" : "KOLON (BEYKENT)";
            if (original.StartsWith("AXIS", StringComparison.OrdinalIgnoreCase)) return "AKS (BEYKENT)";
            if (original.StartsWith("MAT_FND", StringComparison.OrdinalIgnoreCase) || original.StartsWith("CON_FND", StringComparison.OrdinalIgnoreCase) || original == "PENC3")
                return entity is DBText ? "YAZI (BEYKENT)" : "TEMEL (BEYKENT)";
            return entity is DBText ? "YAZI (BEYKENT)" : "CIZGI (BEYKENT)";
        }
        private static void AddText(Transaction tr, Database db, BlockTableRecord ms, ObjectId style, Point3d p, string value, double height)
        {
            BeykentKatmanYukle.Ensure(tr, db, "YAZI (BEYKENT)", 7, LineWeight.LineWeight020);
            var text = new DBText { TextString = value, Height = height, Position = p, TextStyleId = style, Layer = "YAZI (BEYKENT)" };
            ms.AppendEntity(text); tr.AddNewlyCreatedDBObject(text, true);
        }
    }
}
