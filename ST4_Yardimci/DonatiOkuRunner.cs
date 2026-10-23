using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// DONATI ADET ile aynı adet hesabı.
    /// İki nokta = mesafe, üçüncü nokta = ölçü çizgisinin geçeceği yer.
    /// Ölçü DONATI OKU stilinde; DONATI (BEYKENT) kesişimine donatı halkası.
    /// Sonra seçilen etiketlerin adedi mesafe/aralık üst tamsayısı olur.
    /// </summary>
    internal static class DonatiOkuRunner
    {
        private const string LayerDonati = "DONATI (BEYKENT)";
        private const string LayerDonatiOku = "DONATI OKU (BEYKENT)";
        private const string LayerDonatiHalkasi = "DONATI HALKASI (BEYKENT)";
        private const string DimStyleDonatiOku = "DONATI OKU (BEYKENT)";
        private const string TextStyleYazi = "YAZI (BEYKENT)";

        private const short DonatiOkuAci = 150;
        private const short DonatiHalkasiAci = 2;
        private const double CircleDiaCm = 4.0;
        private const double CircleWidthCm = 4.0;
        private const double TextClearCm = 20.0;
        private const double HalkaDedupeCm = 2.0;

        public static void RunInteractive()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            ed.WriteMessage(
                "\nDONATI OKU: İki nokta ile mesafe, sonra ölçü çizgisinin geçeceği nokta. " +
                "Ardından adedi değişecek etiketler. Esc = bitir.");

            while (true)
            {
                var p1opt = new PromptPointOptions("\nMesafe — birinci nokta (bitir: Esc): ")
                {
                    AllowNone = true
                };
                PromptPointResult p1res = ed.GetPoint(p1opt);
                if (p1res.Status != PromptStatus.OK)
                    break;

                Point3d p1 = Flat(p1res.Value);
                var p2opt = new PromptPointOptions("\nMesafe — ikinci nokta: ")
                {
                    UseBasePoint = true,
                    BasePoint = p1,
                    UseDashedLine = true,
                    AllowNone = false
                };
                PromptPointResult p2res = ed.GetPoint(p2opt);
                if (p2res.Status != PromptStatus.OK)
                    break;

                Point3d p2 = Flat(p2res.Value);
                double mesafe = p1.DistanceTo(p2);
                if (mesafe <= 1e-6)
                {
                    ed.WriteMessage("\nDONATI OKU: Mesafe geçersiz.");
                    continue;
                }

                var jig = new DimLineJig(p1, p2);
                PromptResult jigRes = ed.Drag(jig);
                if (jigRes.Status != PromptStatus.OK)
                    break;

                Point3d p3 = Flat(jig.Result);
                int halka = 0;
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    halka = PlaceOku(tr, db, p1, p2, p3);
                    tr.Commit();
                }

                try { ed.UpdateScreen(); } catch { }
                ed.WriteMessage(
                    "\nDONATI OKU: mesafe={0:0.##}  halka={1}",
                    mesafe, halka);
                if (halka == 0)
                    ed.WriteMessage("\n  DONATI (BEYKENT) kesişimi yok; halka atılmadı.");

                var peo = new PromptSelectionOptions
                {
                    MessageForAdding = "\nAdedi değişecek donatı etiketlerini seçin: ",
                    MessageForRemoval = "\nÇıkarılacak etiket: ",
                    AllowDuplicates = false
                };
                var filter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Operator, "<OR"),
                    new TypedValue((int)DxfCode.Start, "TEXT"),
                    new TypedValue((int)DxfCode.Start, "MTEXT"),
                    new TypedValue((int)DxfCode.Operator, "OR>")
                });

                PromptSelectionResult psr = ed.GetSelection(peo, filter);
                if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
                {
                    if (psr.Status == PromptStatus.Cancel)
                        break;
                    ed.WriteMessage("\nDONATI OKU: Etiket seçimi yok.");
                    continue;
                }

                int updated = 0, skipped = 0;
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject so in psr.Value)
                    {
                        if (so == null) continue;
                        var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent == null || ent.IsErased)
                        {
                            skipped++;
                            continue;
                        }

                        string raw;
                        if (ent is DBText dbt)
                            raw = dbt.TextString ?? "";
                        else if (ent is MText mt)
                            raw = mt.Contents ?? "";
                        else
                        {
                            skipped++;
                            continue;
                        }

                        string plain = DonatiAdetRunner.StripMText(raw);
                        if (!DonatiAdetRunner.TryComputeAdet(plain, mesafe, out int newAdet, out double aralik, out string err))
                        {
                            skipped++;
                            ed.WriteMessage("\n  Atlandı: {0}", err);
                            continue;
                        }

                        if (!DonatiAdetRunner.TryReplaceAdet(raw, plain, newAdet, out string newRaw, out string newPlain, out string oldAdetDisp))
                        {
                            skipped++;
                            ed.WriteMessage("\n  Atlandı: Adet yazılamadı.");
                            continue;
                        }

                        ent.UpgradeOpen();
                        if (ent is DBText tx)
                            tx.TextString = newPlain;
                        else if (ent is MText mtx)
                            mtx.Contents = newRaw;

                        updated++;
                        ed.WriteMessage(
                            "\n  {0:0.##}/{1:0.##} → adet {2} → {3}  ({4})",
                            mesafe, aralik, oldAdetDisp, newAdet, newPlain);
                    }

                    tr.Commit();
                }

                ed.WriteMessage(
                    "\nDONATI OKU: mesafe={0:0.##}  guncellenen={1}  atlanan={2}",
                    mesafe, updated, skipped);
            }
        }

        private static int PlaceOku(Transaction tr, Database db, Point3d p1, Point3d p2, Point3d p3)
        {
            if (!TryDimLine(p1, p2, p3, out Point3d a, out Point3d b, out Vector3d dir, out double len))
                return 0;

            var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var hitT = new List<double>();
            var hitPts = new List<Point3d>();
            CollectDonatiHits(tr, btr, a, b, dir, len, hitT, hitPts);
            string okuLyr = EnsureLayer(tr, db, LayerDonatiOku, DonatiOkuAci, LineWeight.LineWeight020);
            string halkaLyr = EnsureLayer(tr, db, LayerDonatiHalkasi, DonatiHalkasiAci, LineWeight.LineWeight030);
            ObjectId yaziStyleId = GetOrCreateYaziBeykentTextStyle(tr, db);
            ObjectId dimStyleId = EnsureDonatiOkuDimStyle(tr, db, yaziStyleId);

            var dim = new AlignedDimension();
            dim.SetDatabaseDefaults(db);
            dim.XLine1Point = p1;
            dim.XLine2Point = p2;
            dim.DimLinePoint = p3;
            dim.DimensionText = "";
            if (!dimStyleId.IsNull)
                dim.DimensionStyle = dimStyleId;
            dim.Layer = okuLyr;
            BaDimensionStyles.Apply(tr, db, dim, dimStyleId, DimStyleDonatiOku);

            btr.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
            PlaceDimText(dim, a, dir, len, hitT);

            for (int i = 0; i < hitPts.Count; i++)
                DrawHalka(tr, btr, halkaLyr, hitPts[i]);

            return hitPts.Count;
        }

        /// <summary>Ölçü çizgisi p1–p2’ye paraleldir ve p3’ten geçer; uçlar p1/p2 izdüşümüdür.</summary>
        private static bool TryDimLine(
            Point3d p1, Point3d p2, Point3d p3,
            out Point3d a, out Point3d b, out Vector3d dir, out double len)
        {
            a = p1;
            b = p2;
            dir = new Vector3d(1, 0, 0);
            len = p1.DistanceTo(p2);
            if (len <= 1e-6) return false;
            dir = (p2 - p1) / len;
            Vector3d w = p3 - p1;
            double along = w.DotProduct(dir);
            Vector3d offset = w - dir * along;
            a = p1 + offset;
            b = p2 + offset;
            return true;
        }

        private static void CollectDonatiHits(
            Transaction tr, BlockTableRecord btr,
            Point3d a, Point3d b, Vector3d dir, double len,
            List<double> hitT, List<Point3d> hitPts)
        {
            using var probe = new LineSegment2d(new Point2d(a.X, a.Y), new Point2d(b.X, b.Y));
            double minX = Math.Min(a.X, b.X) - 1.0;
            double maxX = Math.Max(a.X, b.X) + 1.0;
            double minY = Math.Min(a.Y, b.Y) - 1.0;
            double maxY = Math.Max(a.Y, b.Y) + 1.0;

            foreach (ObjectId id in btr)
            {
                if (!id.IsValid) continue;
                Entity ent;
                try { ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity; }
                catch { continue; }
                if (ent == null || ent.IsErased) continue;
                if (!LayerEquals(ent.Layer, LayerDonati)) continue;
                if (!ExtentsOverlap(ent, minX, minY, maxX, maxY)) continue;

                if (ent is Polyline pl)
                    HitPolyline(pl, probe, a, dir, len, hitT, hitPts);
                else if (ent is Line ln)
                    HitSegment(new Point2d(ln.StartPoint.X, ln.StartPoint.Y),
                        new Point2d(ln.EndPoint.X, ln.EndPoint.Y), probe, a, dir, len, hitT, hitPts);
                else if (ent is Arc arc)
                    HitArc(arc, probe, a, dir, len, hitT, hitPts);
                else if (ent is Circle c)
                    HitCircle(c, probe, a, dir, len, hitT, hitPts);
            }

            SortUniqueHits(a, dir, hitT, hitPts);
        }

        private static void HitPolyline(
            Polyline pl, LineSegment2d probe,
            Point3d origin, Vector3d dir, double len,
            List<double> hitT, List<Point3d> hitPts)
        {
            int n = pl.NumberOfVertices;
            if (n < 2) return;
            int segs = pl.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                try
                {
                    Point2d[] pts = null;
                    double bulge = pl.GetBulgeAt(i);
                    if (Math.Abs(bulge) < 1e-9)
                    {
                        Point3d s = pl.GetPoint3dAt(i);
                        Point3d e = pl.GetPoint3dAt((i + 1) % n);
                        using var edge = new LineSegment2d(new Point2d(s.X, s.Y), new Point2d(e.X, e.Y));
                        pts = edge.IntersectWith(probe);
                    }
                    else
                    {
                        using CircularArc2d arc = pl.GetArcSegment2dAt(i);
                        if (arc != null)
                            pts = arc.IntersectWith(probe);
                    }
                    AddHits(pts, origin, dir, len, hitT, hitPts);
                }
                catch { }
            }
        }

        private static void HitSegment(
            Point2d s, Point2d e, LineSegment2d probe,
            Point3d origin, Vector3d dir, double len,
            List<double> hitT, List<Point3d> hitPts)
        {
            try
            {
                if (s.GetDistanceTo(e) < 1e-6) return;
                using var edge = new LineSegment2d(s, e);
                AddHits(edge.IntersectWith(probe), origin, dir, len, hitT, hitPts);
            }
            catch { }
        }

        private static void HitArc(
            Arc arc, LineSegment2d probe,
            Point3d origin, Vector3d dir, double len,
            List<double> hitT, List<Point3d> hitPts)
        {
            try
            {
                Point3d mid = arc.GetPointAtParameter((arc.StartParam + arc.EndParam) * 0.5);
                using var arc2d = new CircularArc2d(
                    new Point2d(arc.StartPoint.X, arc.StartPoint.Y),
                    new Point2d(mid.X, mid.Y),
                    new Point2d(arc.EndPoint.X, arc.EndPoint.Y));
                AddHits(arc2d.IntersectWith(probe), origin, dir, len, hitT, hitPts);
            }
            catch { }
        }

        private static void HitCircle(
            Circle c, LineSegment2d probe,
            Point3d origin, Vector3d dir, double len,
            List<double> hitT, List<Point3d> hitPts)
        {
            try
            {
                using var arc2d = new CircularArc2d(new Point2d(c.Center.X, c.Center.Y), c.Radius);
                AddHits(arc2d.IntersectWith(probe), origin, dir, len, hitT, hitPts);
            }
            catch { }
        }

        private static void AddHits(
            Point2d[] pts, Point3d origin, Vector3d dir, double len,
            List<double> hitT, List<Point3d> hitPts)
        {
            if (pts == null) return;
            for (int i = 0; i < pts.Length; i++)
            {
                var p = new Point3d(pts[i].X, pts[i].Y, 0);
                double t = (p - origin).DotProduct(dir);
                if (t < -0.05 || t > len + 0.05) continue;
                hitT.Add(t);
                hitPts.Add(p);
            }
        }

        private static void SortUniqueHits(Point3d origin, Vector3d dir, List<double> hitT, List<Point3d> hitPts)
        {
            int n = hitPts.Count;
            if (n <= 1) return;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (i, j) => hitT[i].CompareTo(hitT[j]));

            var t2 = new List<double>(n);
            var p2 = new List<Point3d>(n);
            for (int k = 0; k < n; k++)
            {
                Point3d p = hitPts[order[k]];
                if (p2.Count > 0 && p.DistanceTo(p2[p2.Count - 1]) <= HalkaDedupeCm)
                    continue;
                p2.Add(p);
                t2.Add((p - origin).DotProduct(dir));
            }
            hitT.Clear();
            hitPts.Clear();
            hitT.AddRange(t2);
            hitPts.AddRange(p2);
        }

        private static void PlaceDimText(
            AlignedDimension dim, Point3d a, Vector3d dir, double len, List<double> hitT)
        {
            if (hitT == null || hitT.Count == 0 || len <= 1e-6) return;
            try
            {
                double mid = 0.5 * len;
                double anchor = mid;
                double bestD = double.MaxValue;
                for (int i = 0; i < hitT.Count; i++)
                {
                    double f = hitT[i];
                    if (f < -0.5 || f > len + 0.5) continue;
                    double d = Math.Abs(f - mid);
                    if (d < bestD)
                    {
                        bestD = d;
                        anchor = f;
                    }
                }

                double clear = TextClearCm;
                double edgeClear = 10.0;
                bool horizontal = Math.Abs(dir.X) >= Math.Abs(dir.Y);
                double preferred;
                double flipped;
                if (horizontal)
                {
                    if (dir.X >= 0) { preferred = anchor - clear; flipped = anchor + clear; }
                    else { preferred = anchor + clear; flipped = anchor - clear; }
                }
                else
                {
                    if (dir.Y >= 0) { preferred = anchor + clear; flipped = anchor - clear; }
                    else { preferred = anchor - clear; flipped = anchor + clear; }
                }

                bool Fits(double t) => t >= edgeClear && t <= len - edgeClear;
                double alongText;
                if (Fits(preferred))
                    alongText = preferred;
                else if (Fits(flipped))
                    alongText = flipped;
                else
                {
                    double scorePref = Math.Min(preferred, len - preferred);
                    double scoreFlip = Math.Min(flipped, len - flipped);
                    alongText = scoreFlip > scorePref + 0.05 ? flipped : preferred;
                    alongText = Math.Max(1.0, Math.Min(len - 1.0, alongText));
                }

                dim.UsingDefaultTextPosition = false;
                dim.TextPosition = a + dir * alongText;
                try { dim.Dimtad = 0; } catch { }
            }
            catch { }
        }

        /// <summary>4 cm çap, global width 4; radye ilave ile aynı halka.</summary>
        private static void DrawHalka(Transaction tr, BlockTableRecord btr, string layer, Point3d c)
        {
            double r = CircleDiaCm * 0.5;
            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            pl.AddVertexAt(0, new Point2d(c.X - r, c.Y), 1.0, 0, 0);
            pl.AddVertexAt(1, new Point2d(c.X + r, c.Y), 1.0, 0, 0);
            pl.Closed = true;
            pl.ConstantWidth = CircleWidthCm;
            pl.Layer = layer;
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static string EnsureLayer(Transaction tr, Database db, string name, short aci, LineWeight lw)
        {
            BeykentKatmanYukle.Ensure(tr, db, name, aci, lw);
            return name;
        }

        private static ObjectId GetOrCreateYaziBeykentTextStyle(Transaction tr, Database db)
        {
            return BaDimensionStyles.EnsureYazi(tr, db);
        }

        /// <summary>
        /// DONATI OKU (BEYKENT): ekstansiyon kapalı, ok 10, yazı 10, gap 2.
        /// Var olan stilin renk/ölçü ayarı değiştirilmez.
        /// </summary>
        private static ObjectId EnsureDonatiOkuDimStyle(Transaction tr, Database db, ObjectId yaziStyleId)
        {
            return BaDimensionStyles.Ensure(tr, db, DimStyleDonatiOku);
        }

        private static bool ExtentsOverlap(Entity ent, double minX, double minY, double maxX, double maxY)
        {
            try
            {
                Extents3d ex = ent.GeometricExtents;
                if (ex.MaxPoint.X < minX || ex.MinPoint.X > maxX) return false;
                if (ex.MaxPoint.Y < minY || ex.MinPoint.Y > maxY) return false;
                return true;
            }
            catch
            {
                return true;
            }
        }

        private static bool LayerEquals(string layer, string name)
        {
            return string.Equals(layer ?? "", name, StringComparison.OrdinalIgnoreCase);
        }

        private static Point3d Flat(Point3d p) => new Point3d(p.X, p.Y, 0);

        /// <summary>Üçüncü nokta sürüklenirken ölçü çizgisini (p1–p2’ye paralel) gösterir.</summary>
        private sealed class DimLineJig : DrawJig
        {
            private readonly Point3d _p1;
            private readonly Point3d _p2;
            private Point3d _cursor;

            public DimLineJig(Point3d p1, Point3d p2)
            {
                _p1 = p1;
                _p2 = p2;
                _cursor = new Point3d((p1.X + p2.X) * 0.5, (p1.Y + p2.Y) * 0.5, 0);
            }

            public Point3d Result => _cursor;

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var opt = new JigPromptPointOptions("\nÖlçü çizgisinin geçeceği nokta: ")
                {
                    UserInputControls = UserInputControls.Accept3dCoordinates
                };
                PromptPointResult res = prompts.AcquirePoint(opt);
                if (res.Status != PromptStatus.OK)
                    return SamplerStatus.Cancel;
                Point3d p = Flat(res.Value);
                if (p.DistanceTo(_cursor) < 1e-6)
                    return SamplerStatus.NoChange;
                _cursor = p;
                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(Autodesk.AutoCAD.GraphicsInterface.WorldDraw draw)
            {
                if (!TryDimLine(_p1, _p2, _cursor, out Point3d a, out Point3d b, out Vector3d _, out double _))
                    return true;
                try { draw.SubEntityTraits.Color = DonatiOkuAci; } catch { }
                draw.Geometry.WorldLine(a, b);
                return true;
            }
        }
    }
}
