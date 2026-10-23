using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    internal static class PerdeBoslukRunner
    {
        const string App = "BA_PERDE_BOSLUK", Store = "BA_PERDE_BOSLUK_KAYNAK";
        const string BarLayer = "DONATI (BEYKENT)", TextLayer = "DONATI YAZISI (BEYKENT)";
        const string StirrupLayer = "ETRIYE (BEYKENT)";
        static readonly Regex Label = new Regex(@"(?<prefix>(?:\d+\s*[xX×]\s*)*)(?<n>\d+)\s*[øØ⌀φΦ]\s*(?<dia>\d+)\s*/\s*(?<s>\d+(?:[.,]\d+)?)\s*L\s*=\s*(?<len>\d+)", RegexOptions.Compiled);
        internal sealed class Box
        {
            internal double L, R, B, T;
            internal double W => R - L;
            internal double H => T - B;
            internal double X => (L + R) / 2;
            internal double Y => (B + T) / 2;
            internal bool Contains(Box b) => b.L > L + .01 && b.R < R - .01 && b.B > B + .01 && b.T < T - .01;
        }
        sealed class Wall : BoxBase
        {
            internal string Key;
            internal ObjectId Template, Owner;
            internal double Thickness, Cover = 4;
            internal List<ObjectId> Openings = new List<ObjectId>();
        }
        // A separate base keeps wall metadata independent of the selected polyline's lifetime.
        class BoxBase { internal double L, R, B, T; }
        sealed class Source
        {
            internal Polyline Body, Starter;
            internal List<Polyline> Horizontal;
            internal List<DBText> BodyNotes, StarterNotes, HorizontalNotes;
            internal List<Entity> Ciroz, StarterDims;
            internal DBText BodyLabel, HorizontalLabel;
            internal Match BodyMatch, HorizontalMatch;
            internal double Dia, Spacing, HSpacing;
            internal List<Entity> All => new Entity[] { Body, Starter }.Where(e => e != null)
                .Concat(Horizontal).Concat(BodyNotes).Concat(StarterNotes).Concat(HorizontalNotes).Concat(Ciroz)
                .Concat(StarterDims ?? new List<Entity>())
                .GroupBy(e => e.ObjectId).Select(g => g.First()).ToList();
        }

        internal static void RunInteractive()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var opt = new PromptSelectionOptions
            {
                MessageForAdding = "\nKapama perde görünüşündeki dikdörtgen boşluk poligonlarını seçin: ",
                AllowDuplicates = false
            };
            var selection = doc.Editor.GetSelection(opt,
                new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE") }));
            if (selection.Status != PromptStatus.OK) return;
            try
            {
                int n;
                using (doc.LockDocument()) n = Process(doc.Database, selection.Value.GetObjectIds());
                doc.Editor.Regen();
                doc.Editor.WriteMessage("\nPERDE BOSLUK: {0} kapama perde düzenlendi. Boşluklar ve diğer çizimler korundu.", n);
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\nPERDE BOSLUK: {0}\nİşlem geri alındı; mevcut çizim değiştirilmedi.", ex.Message);
            }
        }

        internal static int Process(Database db, IEnumerable<ObjectId> selected)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ids = selected.Distinct().ToList();
                if (ids.Count == 0) return 0;
                var all = new List<Entity>();
                var owner = ((Entity)tr.GetObject(ids[0], OpenMode.ForRead)).OwnerId;
                var space = (BlockTableRecord)tr.GetObject(owner, OpenMode.ForRead);
                foreach (ObjectId id in space)
                    if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Entity e) all.Add(e);
                var stored = ReadWalls(db, tr).Where(w => w.Owner == owner).ToList();
                var jobs = new List<Wall>();
                foreach (var id in ids)
                {
                    var p = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                    if (p == null || p.OwnerId != owner) throw new InvalidOperationException("Boşluklar aynı çizim alanında olmalıdır.");
                    Box b = Rectangle(p);
                    var wall = stored.Concat(jobs).FirstOrDefault(w => Inside(w, b));
                    if (wall == null)
                    {
                        wall = Discover(all, b);
                        wall.Owner = owner;
                        wall.Key = Guid.NewGuid().ToString("N");
                        stored.Add(wall);
                    }
                    if (!jobs.Contains(wall)) jobs.Add(wall);
                    if (!wall.Openings.Contains(id)) wall.Openings.Add(id);
                }
                EnsureLayers(db, tr);
                var drawnSides = new List<SideDraw>();
                foreach (var w in jobs)
                {
                    var holes = w.Openings.Where(id => !id.IsNull && !id.IsErased)
                        .Select(id => Rectangle((Polyline)tr.GetObject(id, OpenMode.ForRead))).OrderBy(b => b.L).ThenBy(b => b.B).ToList();
                    if (holes.Any(b => !Inside(w, b))) throw new InvalidOperationException("Bir boşluk, kaynak kapama perdesinin sınırları dışında.");
                    for (int i = 0; i < holes.Count; i++)
                        for (int j = i + 1; j < holes.Count; j++)
                            if (Math.Min(holes[i].R, holes[j].R) > Math.Max(holes[i].L, holes[j].L) + .01
                                && Math.Min(holes[i].T, holes[j].T) > Math.Max(holes[i].B, holes[j].B) + .01)
                                throw new InvalidOperationException("Birbirinin içine giren boşlukları tek bir çakışmayan dikdörtgen düzenine dönüştürün.");
                    Source src;
                    List<Entity> originals = null;
                    if (w.Template.IsNull)
                    {
                        src = ReadSource(all, w);
                        originals = src.All;
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                        // Anonymous, unreferenced blocks can be erased when a DWG reopens.
                        // Keep the regeneration source in a named, non-inserted block.
                        var block = new BlockTableRecord { Name = "BA_PERDE_BOSLUK_" + w.Key };
                        w.Template = bt.Add(block);
                        tr.AddNewlyCreatedDBObject(block, true);
                        foreach (var e in originals)
                        {
                            var clone = (Entity)e.Clone();
                            block.AppendEntity(clone); tr.AddNewlyCreatedDBObject(clone, true);
                        }
                    }
                    else
                    {
                        var block = (BlockTableRecord)tr.GetObject(w.Template, OpenMode.ForRead);
                        src = ReadSource(block.Cast<ObjectId>().Select(id => (Entity)tr.GetObject(id, OpenMode.ForRead)).ToList(), w);
                    }
                    var generated = new List<Entity>();
                    var target = (BlockTableRecord)tr.GetObject(owner, OpenMode.ForWrite);
                    Action<Entity> add = e =>
                    {
                        e.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, w.Key));
                        target.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); generated.Add(e);
                    };
                    Generate(src, w, holes, add, drawnSides);
                    // Only the source reinforcement bundle or this command's previous result is replaced.
                    foreach (var e in all)
                        if ((originals != null && originals.Contains(e)) || Tagged(e, w.Key))
                        { e.UpgradeOpen(); e.Erase(); }
                    WriteWall(db, tr, w);
                }
                tr.Commit();
                return jobs.Count;
            }
        }

        static bool Inside(Wall w, Box b) => b.L > w.L + .01 && b.R < w.R - .01 && b.B > w.B + .01 && b.T < w.T - .01;
        static Box Bounds(Polyline p)
        {
            var pts = Enumerable.Range(0, p.NumberOfVertices).Select(p.GetPoint2dAt).ToList();
            return new Box { L = pts.Min(q => q.X), R = pts.Max(q => q.X), B = pts.Min(q => q.Y), T = pts.Max(q => q.Y) };
        }
        internal static Box Rectangle(Polyline p)
        {
            if (!p.Closed || p.NumberOfVertices != 4 || Enumerable.Range(0, 4).Any(i => Math.Abs(p.GetBulgeAt(i)) > 1e-8))
                throw new InvalidOperationException("Dört köşeli, kapalı ve yay içermeyen boşluk poligonu seçin.");
            var b = Bounds(p);
            if (b.W < 8 || b.H < 8 || Math.Abs(Math.Abs(p.Area) - b.W * b.H) > .01)
                throw new InvalidOperationException("Boşluk poligonu görünüşe paralel bir dikdörtgen olmalıdır (birim: cm).");
            return b;
        }
        static Wall Discover(List<Entity> all, Box hole)
        {
            var sides = all.OfType<Line>().Where(e => e.Layer == "PERDE (BEYKENT)"
                && Math.Abs(e.StartPoint.X - e.EndPoint.X) < .02
                && Math.Min(e.StartPoint.Y, e.EndPoint.Y) < hole.B && Math.Max(e.StartPoint.Y, e.EndPoint.Y) > hole.T).ToList();
            var left = sides.Where(e => e.StartPoint.X < hole.L).OrderByDescending(e => e.StartPoint.X).FirstOrDefault();
            var right = sides.Where(e => e.StartPoint.X > hole.R).OrderBy(e => e.StartPoint.X).FirstOrDefault();
            if (left == null || right == null) throw new InvalidOperationException("Boşluğu çevreleyen kapama perde görünüşü bulunamadı.");
            var w = new Wall { L = left.StartPoint.X, R = right.StartPoint.X,
                B = Math.Min(left.StartPoint.Y, left.EndPoint.Y), T = Math.Max(left.StartPoint.Y, left.EndPoint.Y) + 100 };
            var body = all.OfType<Polyline>().Where(p => p.Layer == BarLayer && !p.Closed)
                .Where(p => { var b = Bounds(p); return b.W < 65 && b.H > 80 && b.X > w.L && b.X < w.R && b.B < hole.B && b.T > hole.T; })
                .OrderByDescending(p => Bounds(p).H).FirstOrDefault();
            if (body == null) throw new InvalidOperationException("Mevcut düşey donatı açılımı tanınamadı; donatılar değiştirilmedi.");
            var bb = Bounds(body);
            w.B = bb.B;
            var slab = all.OfType<Line>().Where(e => e.Layer == "DOSEME (BEYKENT)"
                && Math.Abs(e.StartPoint.Y - e.EndPoint.Y) < .02 && e.StartPoint.Y > bb.T && e.StartPoint.Y < bb.T + 20
                && Math.Min(e.StartPoint.X, e.EndPoint.X) < hole.X && Math.Max(e.StartPoint.X, e.EndPoint.X) > hole.X)
                .OrderBy(e => e.StartPoint.Y).FirstOrDefault();
            w.T = slab == null ? bb.T + 4 : slab.StartPoint.Y;
            w.Cover = Math.Round((w.T - bb.T) * 2) / 2;
            var plan = all.OfType<Polyline>().Where(p => p.Layer == "PERDE (BEYKENT)" && p.Closed)
                .Select(Bounds).Where(b => Math.Abs(b.L - w.L) < .1 && Math.Abs(b.R - w.R) < .1 && b.T < w.B && b.H < 100)
                .OrderByDescending(b => b.T).FirstOrDefault();
            w.Thickness = plan == null ? bb.W + 2 * w.Cover : plan.H;
            if (w.Cover < 1 || w.Cover > 10 || w.Thickness < 12) throw new InvalidOperationException("Perde kalınlığı/pas payı güvenle okunamadı.");
            return w;
        }
        static List<DBText> Notes(List<Entity> all, Box b, double marginX = 30) => all.OfType<DBText>()
            .Where(t => t.Layer == TextLayer && t.Position.X >= b.L - 20 && t.Position.X <= b.R + marginX
                && t.Position.Y >= b.B - 8 && t.Position.Y <= b.T + 16).ToList();
        static Source ReadSource(List<Entity> all, Wall w)
        {
            var vertical = all.OfType<Polyline>().Where(p => p.Layer == BarLayer && !p.Closed)
                .Where(p => { var b = Bounds(p); return b.W < 65 && b.H > 60 && b.X > w.L && b.X < w.R && b.B < w.T && b.T > w.B; }).ToList();
            var body = vertical.OrderByDescending(p => Bounds(p).H).FirstOrDefault();
            if (body == null) throw new InvalidOperationException("Düşey donatı kaynağı eksik.");
            var b0 = Bounds(body);
            var starter = vertical.Where(p => p != body && Bounds(p).B < b0.B - 1 && Math.Abs(Bounds(p).L - b0.L) < 20)
                .OrderBy(p => Math.Abs(Bounds(p).L - b0.L)).FirstOrDefault();
            var horizontal = all.OfType<Polyline>().Where(p => p.Layer == BarLayer && !p.Closed)
                .Where(p => { var b = Bounds(p); return b.W > (w.R - w.L) * .8 && b.H < 65 && b.X > w.L && b.X < w.R && b.B > w.B && b.T < w.T; })
                .OrderBy(p => Bounds(p).B).Take(2).ToList();
            if (horizontal.Count != 2) throw new InvalidOperationException("İki yatay donatı açılımı tanınamadı.");
            var bodyNotes = Notes(all, b0);
            var bodyLabel = bodyNotes.FirstOrDefault(t => Label.IsMatch(t.TextString.Replace("%%c", "ø").Replace("%%C", "ø")));
            var hb = new Box { L = horizontal.Min(p => Bounds(p).L), R = horizontal.Max(p => Bounds(p).R),
                B = horizontal.Min(p => Bounds(p).B), T = horizontal.Max(p => Bounds(p).T) };
            var hNotes = Notes(all, hb);
            var hLabel = hNotes.FirstOrDefault(t => Label.IsMatch(t.TextString.Replace("%%c", "ø").Replace("%%C", "ø")));
            if (bodyLabel == null || hLabel == null) throw new InvalidOperationException("Mevcut çap, aralık ve boy etiketleri okunamadı.");
            var bm = Label.Match(bodyLabel.TextString.Replace("%%c", "ø").Replace("%%C", "ø"));
            var hm = Label.Match(hLabel.TextString.Replace("%%c", "ø").Replace("%%C", "ø"));
            var ciroz = all.OfType<Polyline>().Where(p => p.Layer == "CIROZ (BEYKENT)")
                .Where(p => { var b = Bounds(p); return b.X > w.L && b.X < w.R && b.Y > w.B && b.Y < w.T; }).FirstOrDefault();
            var cz = new List<Entity>();
            if (ciroz != null)
            {
                cz.Add(ciroz); var cb = Bounds(ciroz); cb.T += 20;
                cz.AddRange(all.OfType<DBText>().Where(t => (t.Layer == TextLayer || t.Layer == "YAZI (BEYKENT)")
                    && t.Position.X >= cb.L - 20 && t.Position.X <= cb.R + 40
                    && t.Position.Y >= cb.B - 8 && t.Position.Y <= cb.T + 16));
            }
            var sb = starter == null ? null : Bounds(starter);
            return new Source { Body = body, Starter = starter, Horizontal = horizontal, BodyNotes = bodyNotes,
                StarterNotes = sb == null ? new List<DBText>() : StarterLabels(all, sb),
                StarterDims = sb == null ? new List<Entity>() : HookDimensions(all, sb),
                HorizontalNotes = hNotes, BodyLabel = bodyLabel, HorizontalLabel = hLabel, BodyMatch = bm, HorizontalMatch = hm,
                Dia = Number(bm, "dia"), Spacing = Number(bm, "s"), HSpacing = Number(hm, "s"), Ciroz = cz };
        }
        static double Number(Match m, string group) => double.Parse(m.Groups[group].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        static int Count(double span, double spacing) => Math.Max(1, (int)Math.Ceiling(span / spacing - 1e-7));
        static double Round5(double length) => Math.Ceiling(length / 5 - 1e-7) * 5;
        static string BarText(Match m, int n, double length, bool preserveLength = false) => m.Groups["prefix"].Value + n + "ø" + m.Groups["dia"].Value
            + "/" + m.Groups["s"].Value + " L=" + (preserveLength ? length : Round5(length)).ToString("0", CultureInfo.InvariantCulture);
        static T Copy<T>(T e, double dx, double dy) where T : Entity
        {
            var c = (T)e.Clone(); c.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0))); return c;
        }

        static void Generate(Source s, Wall w, List<Box> holes, Action<Entity> add, List<SideDraw> drawnSides)
        {
            var body = Bounds(s.Body);
            if (s.Spacing <= 0 || s.HSpacing <= 0) throw new InvalidOperationException("Donatı aralığı sıfır olamaz.");
            var xs = new[] { w.L, w.R }.Concat(holes.SelectMany(b => new[] { b.L, b.R })).Distinct().OrderBy(x => x).ToList();
            for (int i = 0; i + 1 < xs.Count; i++)
            {
                double l = xs[i], r = xs[i + 1], mid = (l + r) / 2;
                var over = holes.Where(b => b.L < mid && b.R > mid).OrderBy(b => b.B).ToList();
                double end = over.Count == 0 ? body.T : over[0].B - w.Cover;
                if (r - l < body.W + 2 || end - body.B < 35)
                    throw new InvalidOperationException("Boşluk kenarında mevcut gönye/bindirme düzeni için yeterli yer yok.");
                int n = Count(r - l, s.Spacing);
                double dx = mid - body.X;
                var bar = Copy(s.Body, dx, 0);
                double delta = end - body.T;
                for (int v = 0; v < bar.NumberOfVertices; v++)
                {
                    var p = bar.GetPoint2dAt(v);
                    if (p.Y > body.B + body.H / 2) bar.SetPointAt(v, new Point2d(p.X, p.Y + delta));
                }
                add(bar);
                foreach (var note in s.BodyNotes)
                {
                    bool sideOfHole = over.Count == 0 && holes.Any(h => Math.Abs(r - h.L) < 1 || Math.Abs(l - h.R) < 1);
                    double dy = note.Position.Y > body.T - 45 ? delta : delta / 2;
                    if (note == s.BodyLabel && sideOfHole) dy -= 2;
                    var text = Copy(note, dx, dy);
                    if (note == s.BodyLabel) text.TextString = BarText(s.BodyMatch, n, Number(s.BodyMatch, "len") + delta, Math.Abs(delta) < .01);
                    else if (double.TryParse(note.TextString, NumberStyles.Float, CultureInfo.InvariantCulture, out double val)
                        && Math.Abs(val - body.H) < 2) text.TextString = (body.H + delta).ToString("0", CultureInfo.InvariantCulture);
                    add(text);
                }
                if (s.Starter != null)
                {
                    add(Copy(s.Starter, dx, 0));
                    foreach (var note in s.StarterNotes)
                    {
                        var text = Copy(note, dx, 0);
                        var m = Label.Match(text.TextString);
                        if (m.Success) text.TextString = BarText(m, n, Number(m, "len"), true);
                        add(text);
                    }
                    if (s.StarterDims != null)
                        foreach (var dim in s.StarterDims) add(CopyFilizDim(dim, dx, 0));
                }
                // Solid pier between vertically stacked openings; retain the donor diameter and hooks.
                for (int h = 0; h + 1 < over.Count; h++)
                {
                    double low = over[h].T + w.Cover, high = over[h + 1].B - w.Cover;
                    if (high - low < 35) throw new InvalidOperationException("Üst üste boşluklar arasında donatı için yeterli yükseklik yok.");
                    double hook = Math.Min(body.W, r - l - 2);
                    var between = Pline(BarLayer, new[] { new Point2d(mid - hook / 2 + hook, low), new Point2d(mid - hook / 2, low),
                        new Point2d(mid - hook / 2, high), new Point2d(mid + hook / 2, high) });
                    add(between); AddText(s.BodyLabel, mid - hook / 2 - 12, (low + high) / 2,
                        BarText(s.BodyMatch, n, high - low + 2 * hook), Math.PI / 2, add);
                }
            }
            Horizontal(s, w, holes, add, drawnSides);
            var headers = new HashSet<string>();
            var schedules = new List<Box>();
            foreach (var hole in holes) Lintel(s, w, hole, holes, headers, schedules, add);
            MoveCiroz(s, w, holes, add);
        }

        static void Horizontal(Source s, Wall w, List<Box> holes, Action<Entity> add, List<SideDraw> drawnSides)
        {
            var hb0 = Bounds(s.Horizontal[0]); var hb1 = Bounds(s.Horizontal[1]);
            double barL = Math.Min(hb0.L, hb1.L), barR = Math.Max(hb0.R, hb1.R);
            var ys = new[] { w.B, w.T }.Concat(holes.SelectMany(b => new[] { b.B, b.T })).Distinct().OrderBy(y => y).ToList();
            var sideBars = new List<SideDraw>();
            double second = Round5(12.0 * Number(s.HorizontalMatch, "dia") / 10.0);
            if (second < 10) second = 10;
            for (int i = 0; i + 1 < ys.Count; i++)
            {
                double low = ys[i], high = ys[i + 1], mid = (low + high) / 2;
                var active = holes.Where(b => b.B < mid && b.T > mid).OrderBy(b => b.L).ToList();
                if (active.Count == 0)
                {
                    if (holes.Any(b => b.T <= low + .01)) continue; // Upper solid bands are drawn as ø12 lintels below.
                    double lowerTop = high - w.Cover;
                    int n = Count(lowerTop - low, s.HSpacing);
                    double dy = hb0.B >= low && hb1.T <= lowerTop ? 0 : (low + lowerTop) / 2 - (hb0.B + hb1.T) / 2;
                    foreach (var p in s.Horizontal) add(Copy(p, 0, dy));
                    foreach (var note in s.HorizontalNotes)
                    {
                        var text = Copy(note, 0, dy); var m = Label.Match(text.TextString);
                        if (m.Success) text.TextString = BarText(m, n, Number(m, "len"), true);
                        add(text);
                    }
                    continue;
                }
                var spans = new List<(double l, double r, bool openL, bool openR)>();
                double cursor = barL;
                foreach (var hole in active)
                {
                    if (hole.L - w.Cover > cursor + 2) spans.Add((cursor, hole.L - w.Cover, cursor > barL + .1, true));
                    cursor = Math.Max(cursor, hole.R + w.Cover);
                }
                if (barR > cursor + 2) spans.Add((cursor, barR, true, false));
                var faces = s.Horizontal.Select(ReadFace).OrderBy(f => f.Y).ToList();
                double gLen = faces.Max(f => f.GLen);
                if (gLen < 1) gLen = Math.Max(4, w.Thickness - 2 * w.Cover);
                double sep = faces.Count > 1 ? faces[1].Y - faces[0].Y : gLen;
                if (sep < 4) sep = Math.Max(8, gLen);
                if (high - low < sep + 2)
                {
                    sep = Math.Max(8, high - low - 2);
                    if (gLen > sep * 0.7) gLen = sep * 0.7;
                }
                bool srcL = faces.Any(f => f.HookL), srcR = faces.Any(f => f.HookR);
                foreach (var span in spans)
                {
                    if (span.r - span.l < 8)
                        throw new InvalidOperationException("Boşluklar arasında yatay donatı gönyesi için yeterli genişlik yok.");
                    bool gL = span.openL || srcL, gR = span.openR || srcR;
                    bool ciftL = span.openL, ciftR = span.openR;
                    double ret = second;
                    if (ciftL || ciftR)
                    {
                        double maxRet = (ciftL && ciftR) ? Math.Max(4, (span.r - span.l) * 0.5 - 5) : Math.Max(4, (span.r - span.l) * 0.5 - 3);
                        if (ret > maxRet) ret = maxRet;
                    }
                    double yLo = mid - sep / 2, yHi = mid + sep / 2;
                    double extra = (gL ? gLen : 0) + (gR ? gLen : 0) + (ciftL ? ret : 0) + (ciftR ? ret : 0);
                    string label = "2x" + Count(high - low, s.HSpacing) + "ø" + s.HorizontalMatch.Groups["dia"].Value
                        + "/" + s.HorizontalMatch.Groups["s"].Value + " L="
                        + Round5(span.r - span.l + extra).ToString("0", CultureInfo.InvariantCulture);
                    sideBars.Add(new SideDraw
                    {
                        L = span.l, R = span.r, YLo = yLo, YHi = yHi, GLen = gLen, Ret = ret, LabelY = yHi + 3,
                        GL = gL, GR = gR, CiftL = ciftL, CiftR = ciftR, Band = i, Label = label
                    });
                }
            }
            StaggerSideBars(sideBars);
            StaggerAtWallJoint(sideBars, drawnSides);
            foreach (var bar in sideBars)
            {
                // Çift açılımın alt sırası çizilmez. Kalan sıra aşağı gönyeli; boşluk tarafı çift gönye.
                add(GonyeBar(bar.L, bar.R, bar.YHi, bar.GL, bar.GR, bar.GLen, false, bar.CiftL, bar.CiftR, bar.Ret));
                AddText(s.HorizontalLabel, (bar.L + bar.R) / 2 - 30, bar.LabelY, bar.Label, 0, add);
                drawnSides.Add(bar);
            }
        }
        struct SideDraw
        {
            internal double L, R, YLo, YHi, GLen, Ret, LabelY;
            internal bool GL, GR, CiftL, CiftR;
            internal int Band;
            internal string Label;
        }
        /// <summary>Aynı mesnette çakışan komşu grupları 5 cm şaşırtır. Aynı boşluk bandı şaşırtılmaz.</summary>
        static void StaggerSideBars(List<SideDraw> bars)
        {
            const double sasirt = 5.0;
            for (int i = 0; i < bars.Count; i++)
            {
                var a = bars[i];
                double dy = 0;
                for (int j = 0; j < i; j++)
                {
                    var b = bars[j];
                    if (a.Band == b.Band) continue;
                    bool overlap = a.L < b.R - 1 && a.R > b.L + 1;
                    double clear = Math.Min(Math.Abs(a.L - b.R), Math.Abs(b.L - a.R));
                    bool beside = !overlap && clear < 25
                        && !(a.R < b.L - 25 || b.R < a.L - 25);
                    if (!overlap && !beside) continue;
                    int guard = 0;
                    while (guard < 4 && Math.Abs(a.YHi + dy - b.YHi) < 3)
                    { dy += sasirt; guard++; }
                }
                if (dy == 0) continue;
                a.YLo += dy; a.YHi += dy; a.LabelY += dy;
                bars[i] = a;
            }
        }
        /// <summary>Bitişik iki perdenin boşluk yanı yatayları aynı hizada ise sonraki perde 5 cm şaşırtılır.</summary>
        static void StaggerAtWallJoint(List<SideDraw> bars, List<SideDraw> neighbors)
        {
            if (neighbors == null || neighbors.Count == 0) return;
            const double sasirt = 5.0;
            for (int i = 0; i < bars.Count; i++)
            {
                var a = bars[i];
                double dy = 0;
                foreach (var b in neighbors)
                {
                    if (!MeetsAtJoint(a, b)) continue;
                    int guard = 0;
                    while (guard < 4 && Math.Abs(a.YHi + dy - b.YHi) < 4)
                    { dy += sasirt; guard++; }
                }
                if (dy == 0) continue;
                a.YLo += dy; a.YHi += dy; a.LabelY += dy;
                bars[i] = a;
            }
        }
        /// <summary>İki perde ucu: arada en çok 200 cm boşluk, ya da birleşimde 100 cm’ye kadar bindirme.</summary>
        static bool MeetsAtJoint(SideDraw a, SideDraw b)
        {
            if (a.R <= b.L) return b.L - a.R <= 200;
            if (b.R <= a.L) return a.L - b.R <= 200;
            return Math.Min(a.R, b.R) - Math.Max(a.L, b.L) < 100;
        }
        struct HFace { internal double L, R, Y, GLen; internal bool HookL, HookR; }
        /// <summary>Kapama yatay çubuğun düz kolu ve uç gönyeleri. Gönye boyu kalınlık − 2 pas payıdır.</summary>
        static HFace ReadFace(Polyline p)
        {
            var pts = Enumerable.Range(0, p.NumberOfVertices).Select(p.GetPoint2dAt).ToList();
            double mainY = pts.GroupBy(q => Math.Round(q.Y, 1))
                .OrderByDescending(g => g.Max(q => q.X) - g.Min(q => q.X)).First().Average(q => q.Y);
            double minX = pts.Min(q => q.X), maxX = pts.Max(q => q.X), midX = (minX + maxX) / 2;
            var offL = pts.Where(q => Math.Abs(q.Y - mainY) > 1 && q.X <= midX).ToList();
            var offR = pts.Where(q => Math.Abs(q.Y - mainY) > 1 && q.X >= midX).ToList();
            double g = offL.Count + offR.Count == 0 ? 0 : offL.Concat(offR).Max(q => Math.Abs(q.Y - mainY));
            return new HFace { L = minX, R = maxX, Y = mainY, GLen = g, HookL = offL.Count > 0, HookR = offR.Count > 0 };
        }
        /// <summary>Kapama perde yatay gönyesi: 90°, boy gLen. Çift gönye ikinci kolu çubuğa paralel, 12φ.</summary>
        static Polyline GonyeBar(double xL, double xR, double y, bool gonyeL, bool gonyeR, double gLen, bool hookUp, bool ciftL = false, bool ciftR = false, double second = 0)
        {
            const double k = 0.41421356237;
            double R = 2;
            if (gLen < 1) { gonyeL = false; gonyeR = false; }
            if (!gonyeL) ciftL = false;
            if (!gonyeR) ciftR = false;
            if (second < 4) { ciftL = false; ciftR = false; }
            if (gonyeL || gonyeR)
            {
                if (R > gLen * 0.45) R = Math.Max(0.5, gLen * 0.45);
                if ((gonyeL && gonyeR) && R > (xR - xL) * 0.4) R = Math.Max(0.3, (xR - xL) * 0.4);
            }
            if (gLen < 2 * R + 1) { ciftL = false; ciftR = false; }
            double ret = second;
            if (ciftL || ciftR)
            {
                double maxRet = (ciftL && ciftR) ? Math.Max(4, (xR - xL) * 0.5 - 2 * R - 1) : Math.Max(4, (xR - xL) * 0.5 - R - 1);
                if (ret > maxRet) ret = maxRet;
            }
            double s = hookUp ? 1 : -1, bulge = s * k;
            var p = new Polyline { Layer = BarLayer };
            Action<double, double, double> v = (x, z, b) => p.AddVertexAt(p.NumberOfVertices, new Point2d(x, z), b, 0, 0);
            if (gonyeL)
            {
                if (ciftL) { v(xL + ret, y + s * gLen, 0); v(xL + R, y + s * gLen, bulge); v(xL, y + s * (gLen - R), 0); }
                else v(xL, y + s * gLen, 0);
                v(xL, y + s * R, bulge); v(xL + R, y, 0);
            }
            else v(xL, y, 0);
            if (gonyeR)
            {
                v(xR - R, y, bulge); v(xR, y + s * R, 0);
                if (ciftR) { v(xR, y + s * (gLen - R), bulge); v(xR - R, y + s * gLen, 0); v(xR - ret, y + s * gLen, 0); }
                else v(xR, y + s * gLen, 0);
            }
            else v(xR, y, 0);
            return p;
        }
        static void Lintel(Source s, Wall w, Box hole, List<Box> holes, HashSet<string> headers, List<Box> schedules, Action<Entity> add)
        {
            double top = holes.Where(b => b.B >= hole.T && b.L < hole.R && b.R > hole.L).Select(b => b.B).DefaultIfEmpty(w.T).Min();
            const double face = 5.5;
            double y0 = hole.T + face, y1 = top - face;
            if (top - hole.T < face * 2 + 4)
                throw new InvalidOperationException("Boşluk üzerinde etriye ve gönyeler için yeterli net yükseklik yok.");
            int gaps = Math.Max(1, (int)Math.Ceiling((y1 - y0) / 30.0 - 1e-9));
            int n = gaps + 1;
            double step = (y1 - y0) / gaps;
            double xL = s.Horizontal.Min(p => Bounds(p).L), xR = s.Horizontal.Max(p => Bounds(p).R);
            var crossed = holes.Where(b => b != hole && b.B < y1 && b.T > y0).ToList();
            if (crossed.Count > 0 && crossed.Any(b => b.L < hole.R && b.R > hole.L))
                throw new InvalidOperationException("Üst lento başka bir boşlukla kesişiyor.");
            // Different-height side-by-side openings: keep lintel bars inside their own solid span.
            double lintelL = xL, lintelR = xR;
            foreach (var other in crossed)
            { if (other.R <= hole.L) lintelL = Math.Max(lintelL, other.R + w.Cover); if (other.L >= hole.R) lintelR = Math.Min(lintelR, other.L - w.Cover); }
            string headerKey = string.Join("/", new[] { y0, y1, lintelL, lintelR }.Select(v => v.ToString("F3", CultureInfo.InvariantCulture)));
            bool newHeader = headers.Add(headerKey);
            double headerLength = Number(s.HorizontalMatch, "len");
            var lintelFaces = s.Horizontal.Select(ReadFace).ToList();
            double gLen = lintelFaces.Max(f => f.GLen);
            if (gLen < 1) gLen = Math.Max(4, w.Thickness - 2 * w.Cover);
            for (int k = 0; newHeader && k < n; k++)
            {
                double y = y0 + k * step;
                if (lintelL == xL && lintelR == xR)
                {
                    var src = lintelFaces[k % lintelFaces.Count];
                    double g = src.GLen > 1 ? src.GLen : gLen;
                    // Boşluk üstü: kaynakta gönye varsa hepsi aşağı bakar.
                    add(GonyeBar(src.L, src.R, y, src.HookL, src.HookR, g, false));
                }
                else
                {
                    double g = Math.Min(gLen, Math.Max(4, y1 - y0 - 2));
                    headerLength = Round5(lintelR - lintelL + 2 * g);
                    add(GonyeBar(lintelL, lintelR, y, true, true, g, false));
                }
            }
            string text = "2x" + n + "ø12 L=" + headerLength.ToString("0", CultureInfo.InvariantCulture);
            if (newHeader) AddText(s.HorizontalLabel, hole.X - 30, (y0 + y1) / 2 + 5, text, 0, add);
            int ns = Count(hole.W, 10);
            for (int k = 0; k < ns; k++)
            {
                double x = hole.L + (k + .5) * hole.W / ns;
                add(new Line(new Point3d(x, y0, 0), new Point3d(x, y1, 0)) { Layer = StirrupLayer });
            }
            double width = w.Thickness - 3, height = y1 - y0;
            double baseY = w.T + 28, left = hole.X - width / 2, right = hole.X + width / 2;
            var schedule = new Box { L = left - 25, R = left + 125, B = baseY - 20, T = baseY + height + 20 };
            foreach (var placed in schedules.OrderBy(b => b.B))
                if (schedule.L < placed.R && schedule.R > placed.L && schedule.B < placed.T && schedule.T > placed.B)
                { double dy = placed.T + 20 - schedule.B; baseY += dy; schedule.B += dy; schedule.T += dy; }
            schedules.Add(schedule);
            const double hook = 10, rad = 1.5;
            add(OpenStirrup(left, baseY, right, baseY + height, rad, hook));
            double lift = OpenStirrupLift(width, rad);
            AddText(s.HorizontalLabel, left - 20, baseY - 17, ns + "ø10/10 L=" + Math.Round(2 * (width + height) + 2 * hook).ToString("0", CultureInfo.InvariantCulture), 0, add);
            AddText(s.HorizontalLabel, hole.X - 6, baseY + 3, width.ToString("0", CultureInfo.InvariantCulture), 0, add, 8);
            AddText(s.HorizontalLabel, left - 6, baseY + height / 2, height.ToString("0", CultureInfo.InvariantCulture), Math.PI / 2, add, 8);
            AddText(s.HorizontalLabel, left + rad, baseY + height + lift + 2, "10", 0, add, 8);
        }
        static void MoveCiroz(Source s, Wall w, List<Box> holes, Action<Entity> add)
        {
            if (s.Ciroz.Count == 0) return;
            var curve = s.Ciroz.OfType<Polyline>().First(); var b = Bounds(curve);
            double target = w.R - 55;
            var intervals = new List<(double l, double r)>(); double cursor = w.L;
            foreach (var hole in holes.OrderBy(h => h.L)) { if (hole.L > cursor) intervals.Add((cursor, hole.L)); cursor = Math.Max(cursor, hole.R); }
            if (cursor < w.R) intervals.Add((cursor, w.R));
            var region = intervals.OrderByDescending(i => i.r - i.l).First();
            target = region.r - Math.Min(55, (region.r - region.l) / 3);
            double rate = 4;
            foreach (var t in s.Ciroz.OfType<DBText>())
            {
                var rm = Regex.Match(t.TextString, @"(\d+(?:[.,]\d+)?)\s*adet\s*/\s*m", RegexOptions.IgnoreCase);
                if (!rm.Success) continue;
                rate = double.Parse(rm.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                break;
            }
            double areaM2 = holes.Sum(h => Math.Max(0, h.W) * Math.Max(0, h.H)) / 10000.0;
            int drop = (int)Math.Ceiling(rate * areaM2 - 1e-9);
            foreach (var e in s.Ciroz)
            {
                var copy = Copy(e, target - b.X, 0);
                if (drop > 0 && copy is DBText tx)
                {
                    var cm = Regex.Match(tx.TextString, @"^(?<n>\d+)\s*(?<rest>[øØ⌀φΦ].+)$");
                    if (cm.Success)
                    {
                        int old = int.Parse(cm.Groups["n"].Value, CultureInfo.InvariantCulture);
                        tx.TextString = Math.Max(0, old - drop).ToString(CultureInfo.InvariantCulture) + cm.Groups["rest"].Value;
                    }
                }
                add(copy);
            }
        }
        static List<DBText> StarterLabels(List<Entity> all, Box b)
        {
            var notes = Notes(all, b);
            foreach (var t in all.OfType<DBText>())
            {
                if (t.Layer != TextLayer || notes.Contains(t)) continue;
                if (t.Position.X < b.L - 35 || t.Position.X > b.R + 35) continue;
                if (t.Position.Y < b.B - 25 || t.Position.Y > b.B + 40) continue;
                if (!double.TryParse((t.TextString ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) continue;
                if (v < 2 || v > 40) continue;
                notes.Add(t);
            }
            return notes;
        }
        static List<Entity> HookDimensions(List<Entity> all, Box b)
        {
            var list = new List<Entity>();
            foreach (var e in all)
            {
                Point3d a, c;
                if (e is AlignedDimension ad) { a = ad.XLine1Point; c = ad.XLine2Point; }
                else if (e is RotatedDimension rd) { a = rd.XLine1Point; c = rd.XLine2Point; }
                else continue;
                double len = a.DistanceTo(c);
                if (len < 3 || len > b.H + 25) continue;
                bool nearX = a.X >= b.L - 60 && a.X <= b.R + 60 && c.X >= b.L - 60 && c.X <= b.R + 60;
                bool nearY = a.Y >= b.B - 25 && a.Y <= b.T + 25 && c.Y >= b.B - 25 && c.Y <= b.T + 25;
                if (!nearX || !nearY) continue;
                list.Add(e);
            }
            return list;
        }
        /// <summary>Klonlanan ölçü eksi değer ve kaymış yazı üretir; tanım noktaları ötelenerek yeniden kurulur.</summary>
        static Entity CopyFilizDim(Entity e, double dx, double dy)
        {
            var move = new Vector3d(dx, dy, 0);
            Dimension dim = null;
            if (e is AlignedDimension ad)
                dim = new AlignedDimension(ad.XLine1Point + move, ad.XLine2Point + move, ad.DimLinePoint + move,
                    ad.DimensionText ?? "", ad.DimensionStyle);
            else if (e is RotatedDimension rd)
                dim = new RotatedDimension(rd.Rotation, rd.XLine1Point + move, rd.XLine2Point + move, rd.DimLinePoint + move,
                    rd.DimensionText ?? "", rd.DimensionStyle);
            if (dim == null) return Copy(e, dx, dy);
            dim.Layer = e.Layer;
            try { dim.Dimscale = ((Dimension)e).Dimscale; } catch { }
            try { dim.Color = e.Color; } catch { }
            return dim;
        }
        /// <summary>Kolon düşey açılımındaki açık ağız: üst kol, sağ üst yay merkezine göre açılır.</summary>
        static Polyline OpenStirrup(double x0, double y0, double x1, double y1, double rad, double hook)
        {
            double c45 = Math.Cos(Math.PI / 4.0);
            var cTl = new Point2d(x0 + rad, y1 - rad);
            var tTop = new Point2d(x0 + rad, y1);
            var tLeft = new Point2d(x0, y1 - rad);
            var p225 = new Point2d(cTl.X - rad * c45, cTl.Y - rad * c45);
            var p45 = new Point2d(cTl.X + rad * c45, cTl.Y + rad * c45);
            var tipTop = new Point2d(p225.X + hook * c45, p225.Y - hook * c45);
            var tipLeft = new Point2d(p45.X + hook * c45, p45.Y - hook * c45);
            var fillet = new Point2d(x1 - rad, y1 - rad);
            double topLen = Math.Max((x1 - rad) - tTop.X, 1.0);
            double s = Math.Min(0.95, Math.Max(1e-6, 6.0 / topLen));
            double ang = 2.0 * Math.Asin(s);
            double ca = Math.Cos(ang), sa = Math.Sin(ang);
            Point2d Lift(Point2d p)
            {
                double dx = p.X - fillet.X, dy = p.Y - fillet.Y;
                return new Point2d(fillet.X + dx * ca + dy * sa, fillet.Y - dx * sa + dy * ca);
            }
            double remainDeg = Math.Max(5.0, 90.0 - ang * 180.0 / Math.PI);
            double bCorner = -Math.Tan(remainDeg * Math.PI / 180.0 / 4.0);
            double b90 = -Math.Tan(Math.PI / 8.0);
            double b135 = -Math.Tan(135.0 * Math.PI / 180.0 / 4.0);
            var pts = new[]
            {
                Lift(tipTop), Lift(p225), Lift(tTop), Lift(new Point2d(x1 - rad, y1)),
                new Point2d(x1, y1 - rad), new Point2d(x1, y0 + rad), new Point2d(x1 - rad, y0),
                new Point2d(x0 + rad, y0), new Point2d(x0, y0 + rad), tLeft, p45, tipLeft
            };
            var bul = new[] { 0.0, b135, 0.0, bCorner, 0.0, b90, 0.0, b90, 0.0, b135, 0.0, 0.0 };
            var pl = new Polyline { Layer = StirrupLayer };
            for (int i = 0; i < pts.Length; i++)
                pl.AddVertexAt(i, pts[i], bul[i], 0, 0);
            return pl;
        }
        static double OpenStirrupLift(double width, double rad)
        {
            double topLen = Math.Max(width - 2.0 * rad, 1.0);
            double s = Math.Min(0.95, Math.Max(1e-6, 6.0 / topLen));
            return topLen * Math.Sin(2.0 * Math.Asin(s));
        }
        static Polyline Pline(string layer, IEnumerable<Point2d> points)
        { var p = new Polyline { Layer = layer }; foreach (var q in points) p.AddVertexAt(p.NumberOfVertices, q, 0, 0, 0); return p; }
        static void AddText(DBText sample, double x, double y, string value, double rotation, Action<Entity> add, double height = 10)
        {
            var t = (DBText)sample.Clone(); t.TextString = value; t.Height = height; t.Rotation = rotation;
            t.HorizontalMode = TextHorizontalMode.TextLeft; t.VerticalMode = TextVerticalMode.TextBottom;
            t.Position = new Point3d(x, y, 0); t.AlignmentPoint = t.Position; add(t);
        }
        static bool Tagged(Entity e, string key)
        {
            using (var data = e.GetXDataForApplication(App))
                return data != null && data.AsArray().Any(v => v.TypeCode == 1000 && (string)v.Value == key);
        }
        static void EnsureLayers(Database db, Transaction tr)
        {
            var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!apps.Has(App)) { apps.UpgradeOpen(); var a = new RegAppTableRecord { Name = App }; apps.Add(a); tr.AddNewlyCreatedDBObject(a, true); }
            BeykentKatmanYukle.Ensure(tr, db, StirrupLayer);
        }
        static List<Wall> ReadWalls(Database db, Transaction tr)
        {
            var result = new List<Wall>(); var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(Store)) return result;
            var dictionary = (DBDictionary)tr.GetObject(nod.GetAt(Store), OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in dictionary)
            {
                var record = (Xrecord)tr.GetObject(entry.Value, OpenMode.ForRead);
                var v = record.Data.AsArray();
                result.Add(new Wall { Key = entry.Key, Template = (ObjectId)v[0].Value, Owner = (ObjectId)v[1].Value,
                    L = (double)v[2].Value, R = (double)v[3].Value, B = (double)v[4].Value, T = (double)v[5].Value,
                    Thickness = (double)v[6].Value, Cover = (double)v[7].Value,
                    Openings = v.Skip(8).Select(x => (ObjectId)x.Value).ToList() });
            }
            return result;
        }
        static void WriteWall(Database db, Transaction tr, Wall w)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            DBDictionary dictionary;
            if (!nod.Contains(Store))
            { nod.UpgradeOpen(); dictionary = new DBDictionary(); nod.SetAt(Store, dictionary); tr.AddNewlyCreatedDBObject(dictionary, true); }
            else dictionary = (DBDictionary)tr.GetObject(nod.GetAt(Store), OpenMode.ForWrite);
            Xrecord record;
            if (dictionary.Contains(w.Key)) record = (Xrecord)tr.GetObject(dictionary.GetAt(w.Key), OpenMode.ForWrite);
            else { record = new Xrecord(); dictionary.SetAt(w.Key, record); tr.AddNewlyCreatedDBObject(record, true); }
            var values = new List<TypedValue> { new TypedValue(340, w.Template), new TypedValue(330, w.Owner),
                new TypedValue(40, w.L), new TypedValue(40, w.R), new TypedValue(40, w.B), new TypedValue(40, w.T),
                new TypedValue(40, w.Thickness), new TypedValue(40, w.Cover) };
            values.AddRange(w.Openings.Select(id => new TypedValue(330, id))); record.Data = new ResultBuffer(values.ToArray());
        }
    }
}
