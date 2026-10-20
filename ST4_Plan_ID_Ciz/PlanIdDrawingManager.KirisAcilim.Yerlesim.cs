using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        private sealed class KirisYerlesimTakim
        {
            public FloorInfo Floor;
            public int No;
            public double BeamTop;
            public long StartHandle, EndHandle;
            public List<ObjectId> Ids = new List<ObjectId>();
        }
        private sealed class KirisYerlesimKaydi
        {
            public readonly List<KirisYerlesimTakim> Takimlar = new List<KirisYerlesimTakim>();
        }
        private KirisYerlesimKaydi _kirisYerlesim;
        private Extents3d? _kirisAcilimGorunum;
        private Point3d _kirisAcilimGorunumOlcekMerkezi;

        private void KirisAcilimGorunumEkle(Extents3d box)
        {
            if (_kirisAcilimGorunum.HasValue)
            {
                var old = _kirisAcilimGorunum.Value;
                box = new Extents3d(new Point3d(Math.Min(old.MinPoint.X, box.MinPoint.X), Math.Min(old.MinPoint.Y, box.MinPoint.Y), 0),
                    new Point3d(Math.Max(old.MaxPoint.X, box.MaxPoint.X), Math.Max(old.MaxPoint.Y, box.MaxPoint.Y), 0));
            }
            _kirisAcilimGorunum = box;
        }

        // Yeni Dimension bloklarını ve yazı şekillerini Database.UpdateExt(true) ile zorlamadan görünüm.
        // UpdateScreen/regen sonrası AV görülmesin diye yalnızca SetCurrentView; hata yutulur.
        internal void KirisAcilimGorunumeAl(Editor ed)
        {
            if (ed == null || !_kirisAcilimGorunum.HasValue) return;
            try
            {
                var b = _kirisAcilimGorunum.Value;
                double w = b.MaxPoint.X - b.MinPoint.X, h = b.MaxPoint.Y - b.MinPoint.Y;
                if (w <= 0 || h <= 0 || double.IsNaN(w) || double.IsNaN(h) || w > 1e8 || h > 1e8) return;
                KirisAcilimTani("VIEW_BEGIN numeric_bounds");
                using (var view = ed.GetCurrentView())
                {
                    view.CenterPoint = new Point2d((b.MinPoint.X + b.MaxPoint.X) * 0.5, (b.MinPoint.Y + b.MaxPoint.Y) * 0.5);
                    view.Width = w * 1.08;
                    view.Height = h * 1.08;
                    ed.SetCurrentView(view);
                }
                KirisAcilimTani("VIEW_COMPLETE");
            }
            catch
            {
                KirisAcilimTani("VIEW_SKIP");
            }
        }

        private static long KirisYerlesimSonrakiHandle(Database db)
            => Convert.ToInt64(db.Handseed.ToString(), 16);

        internal bool DrawKirisAcilimlariSecenekli(Database db, Editor ed, Point3d insert, string path, KirisAcilimSecenekleri options)
        {
            _kirisAcilimGorunum = null;
            _kirisAcilimGorunumOlcekMerkezi = insert;
            if (options?.Antetli != true) return DrawKirisAcilimlariCore(db, ed, insert, path, options);
            if (db == null) return false;
            _kirisYerlesim = new KirisYerlesimKaydi();
            try { return DrawKirisAcilimlariCore(db, ed, insert, path, options); }
            finally { _kirisYerlesim = null; }
        }

        private sealed class KirisYerlesimBoyut
        {
            public double Width, Above, Below;
        }
        private sealed class KirisYerlesimKonum
        {
            public int Index, Page;
            public double X, BeamY;
        }

        // Nihai çizim birimleri. Aynı satırda kiriş üst yüzleri ortak, tüm zarflar arası80cm.
        private static List<KirisYerlesimKonum> KirisYerlesimPlanla(List<KirisYerlesimBoyut> items, double width, double height)
        {
            const double gap = 80;
            if (items.Count == 0) return new List<KirisYerlesimKonum>();
            // Bir kat daima tek antet. Yalnız tek takımın gerektirdiği yükseklik kadar büyüyebilir.
            height = Math.Max(height, items.Max(v => v.Above) + items.Max(v => v.Below));
            var indices = Enumerable.Range(0, items.Count).ToArray();
            var orders = new[] {
                indices,
                indices.OrderByDescending(i => items[i].Width).ThenBy(i => i).ToArray(),
                indices.OrderByDescending(i => items[i].Above + items[i].Below).ThenByDescending(i => items[i].Width).ThenBy(i => i).ToArray(),
                indices.OrderByDescending(i => (items[i].Width + gap) * (items[i].Above + items[i].Below + gap)).ThenBy(i => i).ToArray(),
                indices.OrderByDescending(i => items[i].Above).ThenByDescending(i => items[i].Width).ThenBy(i => i).ToArray(),
                indices.OrderByDescending(i => items[i].Below).ThenByDescending(i => items[i].Width).ThenBy(i => i).ToArray()
            };
            List<KirisYerlesimKonum> Fit(double cap)
            {
                List<KirisYerlesimKonum> best = null; double bestWidth = double.MaxValue, bestHeight = double.MaxValue;
                foreach (var order in orders)
                for (int mode = 0; mode < 2; mode++)
                {
                    var rows = new List<List<int>>();
                    var widths = new List<double>(); var above = new List<double>(); var below = new List<double>();
                    double used = 0; bool valid = true;
                    foreach (int i in order)
                    {
                        var item = items[i]; int chosen = -1;
                        double bestExtra = double.MaxValue, bestWaste = double.MaxValue;
                        if (item.Width > cap + 1e-7) { valid = false; break; }
                        for (int r = 0; r < rows.Count; r++)
                        {
                            double waste = cap - widths[r] - gap - item.Width;
                            double extra = Math.Max(above[r], item.Above) + Math.Max(below[r], item.Below) - above[r] - below[r];
                            if (waste < -1e-7 || used + extra > height + 1e-7) continue;
                            bool better = mode == 0
                                ? extra < bestExtra - 1e-7 || (Math.Abs(extra - bestExtra) < 1e-7 && waste < bestWaste)
                                : waste < bestWaste - 1e-7 || (Math.Abs(waste - bestWaste) < 1e-7 && extra < bestExtra);
                            if (better) { chosen = r; bestExtra = extra; bestWaste = waste; }
                        }
                        if (chosen < 0)
                        {
                            double extra = item.Above + item.Below + (rows.Count > 0 ? gap : 0);
                            if (used + extra > height + 1e-7) { valid = false; break; }
                            rows.Add(new List<int> { i }); widths.Add(item.Width); above.Add(item.Above); below.Add(item.Below); used += extra;
                        }
                        else
                        {
                            rows[chosen].Add(i); widths[chosen] += gap + item.Width;
                            above[chosen] = Math.Max(above[chosen], item.Above); below[chosen] = Math.Max(below[chosen], item.Below); used += bestExtra;
                        }
                    }
                    if (!valid) continue;
                    double actualWidth = widths.Max();
                    if (actualWidth > bestWidth + 1e-7 || (Math.Abs(actualWidth - bestWidth) < 1e-7 && used >= bestHeight)) continue;
                    bestWidth = actualWidth; bestHeight = used; best = new List<KirisYerlesimKonum>();
                    double y = 0;
                    for (int r = 0; r < rows.Count; r++)
                    {
                        double x = 0;
                        // Aynı satırın iç sırasını isim sırasına döndürmek genişliği değiştirmez.
                        foreach (int i in rows[r].OrderBy(i => i))
                        {
                            best.Add(new KirisYerlesimKonum { Index = i, Page = 0, X = x, BeamY = -y - above[r] });
                            x += items[i].Width + gap;
                        }
                        y += above[r] + below[r] + gap;
                    }
                }
                return best;
            }
            double lower = Math.Max(items.Max(v => v.Width),
                items.Sum(v => (v.Width + gap) * (v.Above + v.Below + gap)) / (height + gap) - gap);
            double upper = items.Sum(v => v.Width) + gap * (items.Count - 1);
            var result = Fit(upper); // Tek satır daima geçerli bir başlangıçtır.
            double seed = Math.Max(lower, Math.Min(width, upper));
            var initial = Fit(seed);
            if (initial != null) { result = initial; upper = initial.Max(p => p.X + items[p.Index].Width); }
            for (int pass = 0; pass < 28 && upper - lower > 0.01; pass++)
            {
                double candidate = (lower + upper) * 0.5;
                var packed = Fit(candidate);
                if (packed == null) lower = candidate;
                else { result = packed; upper = packed.Max(p => p.X + items[p.Index].Width); }
            }
            // Satırları antetin altından yukarı başlat; her satırda üst yüz hizası korunur.
            foreach (var row in result.GroupBy(p => p.BeamY))
            {
                double rowTop = row.Key + row.Max(p => items[p.Index].Above);
                double beamY = -rowTop + row.Max(p => items[p.Index].Below);
                foreach (var p in row) p.BeamY = beamY;
            }
            return result;
        }

        private static Extents3d KirisYerlesimSinirlari(Transaction tr, List<ObjectId> ids)
        {
            Extents3d? bounds = null;
            void Add(Extents3d box)
            {
                if (bounds.HasValue) { var b = bounds.Value; b.AddExtents(box); bounds = b; }
                else bounds = box;
            }
            foreach (var id in ids)
            {
                if (id.IsNull || id.IsErased) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                // Yeni Dimension / Hatch GeometricExtents native AV üretebiliyor.
                if (ent is RotatedDimension dim)
                {
                    var pts = new[] { dim.XLine1Point, dim.XLine2Point, dim.DimLinePoint };
                    double pad = 15;
                    Add(new Extents3d(new Point3d(pts.Min(p => p.X) - pad, pts.Min(p => p.Y) - pad, 0),
                        new Point3d(pts.Max(p => p.X) + pad, pts.Max(p => p.Y) + pad, 0)));
                    continue;
                }
                if (ent is Dimension || ent is Hatch) continue;
                if (ent is DBText tx)
                {
                    Point3d p = tx.Position;
                    try
                    {
                        if (tx.Justify != AttachmentPoint.BaseLeft &&
                            tx.Justify != AttachmentPoint.BaseAlign &&
                            tx.Justify != AttachmentPoint.BaseFit)
                            p = tx.AlignmentPoint;
                    }
                    catch { }
                    Add(new Extents3d(new Point3d(p.X - 5, p.Y - 5, 0), new Point3d(p.X + 5, p.Y + 5, 0)));
                    continue;
                }
                if (ent is MText mx)
                {
                    Point3d p = mx.Location;
                    Add(new Extents3d(new Point3d(p.X - 5, p.Y - 5, 0), new Point3d(p.X + 5, p.Y + 5, 0)));
                    continue;
                }
                try { Add(ent.GeometricExtents); }
                catch { }
            }
            if (!bounds.HasValue) throw new InvalidOperationException("Kiriş takımı yerleşim sınırı bulunamadı.");
            return bounds.Value;
        }

        private void KirisAcilimAnteteYerlestirKatKat(Database db, Editor ed, Point3d insert, string path, List<ObjectId> contentIds)
        {
            // Takım başı/sonu Handle aralıkları: olay zamanlamasına bağımlı değil.
            var handleTeams = _kirisYerlesim.Takimlar.OrderBy(t => t.StartHandle).ToList();
            foreach (var t in handleTeams) t.Ids.Clear();
            int at = 0;
            foreach (var id in contentIds.OrderBy(id => id.Handle.Value))
            {
                long handle = id.Handle.Value;
                while (at < handleTeams.Count && handle >= handleTeams[at].EndHandle) at++;
                if (at < handleTeams.Count && handle >= handleTeams[at].StartHandle)
                    handleTeams[at].Ids.Add(id);
            }
            const double pay = 40, sheetHeight = 4400, header = 0;
            double sheetWidth = AntetDxfSheetViewXmax - AntetDxfSheetViewXmin;
            if (!TryGetEmbeddedAntetSheetViewOutOffsets(out double outDx, out double outDy, ed))
                throw new InvalidOperationException("Antet yerleşim sınırları okunamadı.");
            double outerLeft = insert.X + AntetYerlesimSheetViewOutSolPayCm;
            _kirisAcilimGorunum = null;
            int pageCount = 0;
            foreach (var floorGroup in _kirisYerlesim.Takimlar.GroupBy(t => t.Floor.FloorNo).OrderBy(g => g.Key))
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    var teams = floorGroup.OrderBy(t => t.No).ToList();
                    var boxes = teams.Select(t => KirisYerlesimSinirlari(tr, t.Ids)).ToList();
                    var tops = teams.Select(t => insert.Y + 2 * (t.BeamTop - insert.Y)).ToList();
                    var sizes = boxes.Select((b, i) => new KirisYerlesimBoyut
                    {
                        Width = b.MaxPoint.X - b.MinPoint.X,
                        Above = Math.Max(0, b.MaxPoint.Y - tops[i]),
                        Below = Math.Max(0, tops[i] - b.MinPoint.Y)
                    }).ToList();
                    var positions = KirisYerlesimPlanla(sizes, sheetWidth - 2 * pay, sheetHeight - 2 * pay - header);
                    foreach (var page in positions.GroupBy(p => p.Page))
                    {
                        double requiredW = page.Max(p => p.X + sizes[p.Index].Width);
                        double requiredH = page.Max(p => p.BeamY + sizes[p.Index].Above);
                        double left = outerLeft - outDx, bottom = insert.Y - outDy;
                        double width = requiredW + 2 * pay;
                        double height = Math.Max(sheetHeight, requiredH + 2 * pay + header);
                        foreach (var p in page)
                        {
                            var box = boxes[p.Index];
                            var team = teams[p.Index];
                            double dx = left + pay + p.X - box.MinPoint.X;
                            double dy = bottom + pay + p.BeamY - tops[p.Index];
                            var move = Matrix3d.Displacement(new Vector3d(dx, dy, 0));
                            foreach (var id in team.Ids)
                                if (!id.IsNull && !id.IsErased)
                                    ((Entity)tr.GetObject(id, OpenMode.ForWrite)).TransformBy(move);
                            KirisAcilimTani("PACK floor=" + team.Floor.FloorNo + " beam=" + team.No + " page=" + pageCount
                                + " x=" + p.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                                + " top=" + p.BeamY.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                        }
                        string floorName = string.IsNullOrWhiteSpace(teams[0].Floor.Name)
                            ? "KAT " + floorGroup.Key
                            : teams[0].Floor.Name;
                        string antetBaslik = _surekliTemelAcilim
                            ? "SÜREKLİ TEMEL AÇILIMLARI"
                            : floorName + " KİRİŞ AÇILIMLARI";
                        if (!TryDrawAntetFromEmbeddedTemplate(tr, ms, left + pay, bottom + pay, bottom + height,
                            left, bottom, left + width, path, ed, antetBaslik, null,
                            out double placedLeft, out double right, out double placedTop,
                            fitSheetViewHeightToContent: true, antetScaleTextOverride: "OLCEK: 1/25"))
                            throw new InvalidOperationException(
                                (_surekliTemelAcilim ? "Sürekli temel" : "Kiriş") + " açılımı anteti oluşturulamadı; işlem geri alındı.");
                        KirisAcilimGorunumEkle(new Extents3d(
                            new Point3d(placedLeft, insert.Y, 0),
                            new Point3d(right, placedTop, 0)));
                        KirisAcilimTani("PACK_SHEET floor=" + floorGroup.Key
                            + " width=" + width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                            + " height=" + height.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                        outerLeft = right + 50;
                        pageCount++;
                    }
                    KirisAcilimTani("COMMIT_FLOOR_" + floorGroup.Key + "_BEGIN");
                    tr.Commit();
                    KirisAcilimTani("COMMIT_FLOOR_" + floorGroup.Key + "_OK");
                }
            }
            ed.WriteMessage(_surekliTemelAcilim
                ? "\nSUREKLITEMELACILIMLARI: {0} antet, takimlar arasi80cm; montaj altta, duz ustte."
                : "\nKIRISACILIMLARI: {0} antet, takimlar arasi80cm; ayni satirda kiris ust yuzleri hizali.", pageCount);
        }
    }
}
