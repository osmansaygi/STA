using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Seçilen donatı etiketlerinde (ø ve L=) ait olduğu çizginin boyunu,
    /// gönye yayını keskin köşe kabul ederek yazar.
    /// </summary>
    internal static class DonatiBoyuRunner
    {
        const double MaxGap = 120;
        const double ParallelRad = 25.0 * Math.PI / 180.0;
        const string MetrajPozApp = "ST4METRAJPOZ";

        private static readonly Regex LRx = new Regex(
            @"(?<prefix>[Ll]\s*=\s*)(?<val>\d+(?:[.,]\d+)?)",
            RegexOptions.Compiled);

        public static void RunInteractive()
        {
            RunCore();
        }

        private static void RunCore()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            DonatiBoyuSettings.EnsureLoaded();

            if (DonatiBoyuSettings.Katmanlar.Count == 0)
            {
                ed.WriteMessage("\nDONATIBOYU: Donatı etiketi katmanı ekleyin.");
                return;
            }

            if (DonatiBoyuSettings.CizimKatmanlar.Count == 0)
            {
                ed.WriteMessage("\nDONATIBOYU: Önce Donatı çizimi katmanı ekleyin.");
                return;
            }

            var peo = new PromptSelectionOptions
            {
                MessageForAdding = "\nDonatı etiketlerini ve donatı çizgilerini seçin: ",
                AllowDuplicates = false
            };
            PromptSelectionResult psr = ed.GetSelection(peo);
            if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
            {
                ed.WriteMessage("\nDONATIBOYU: Seçim yok.");
                return;
            }

            int updated = 0, same = 0, notLabel = 0, noBar = 0;
            double scale = DonatiBoyuSettings.OlcekCarpani;
            string lFmt = DonatiBoyuSettings.LEtiketFormati ?? "L=";

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var labels = new List<LabelHit>();
                var bars = new List<Curve>();
                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null || ent.IsErased) continue;

                    if (ent is DBText || ent is MText)
                    {
                        if (!LabelLayerOk(ent.Layer)) continue;
                        string raw = ent is DBText tx ? (tx.TextString ?? "") : ((MText)ent).Contents ?? "";
                        string plain = StripMText(raw);
                        if (!IsDonatiEtiket(plain))
                        {
                            notLabel++;
                            continue;
                        }
                        labels.Add(new LabelHit
                        {
                            Ent = ent,
                            Raw = raw,
                            Plain = plain,
                            Anchor = TextAnchor(ent),
                            Rotation = ent is DBText dbt ? dbt.Rotation : ((MText)ent).Rotation
                        });
                    }
                    else if (ent is Line || ent is Polyline || ent is Polyline2d)
                    {
                        if (!DrawingLayerOk(ent.Layer) || IsMetrajBalloon(ent)) continue;
                        if (ent is Curve c)
                            bars.Add(c);
                    }
                }

                if (labels.Count == 0)
                {
                    ed.WriteMessage("\nDONATIBOYU: Seçimde ø ve L= etiketi yok.");
                    return;
                }

                if (bars.Count == 0)
                    bars = CollectModelBars(tr, db);

                var pairs = MatchLabels(labels, bars);
                var used = new HashSet<int>();
                foreach (MatchPair pair in pairs)
                    used.Add(pair.LabelIndex);

                for (int i = 0; i < labels.Count; i++)
                {
                    if (!used.Contains(i))
                        noBar++;
                }

                foreach (MatchPair pair in pairs)
                {
                    LabelHit lab = labels[pair.LabelIndex];
                    double lenDraw = SharpLength(bars[pair.BarIndex], tr);
                    if (lenDraw <= 1e-6)
                    {
                        noBar++;
                        continue;
                    }
                    // 1:1 tam boy, 1:2 yarısı, 1:4 dörtte biri
                    double lenCm = lenDraw * scale;
                    double rounded = RoundLen(lenCm, DonatiBoyuSettings.Yuvarlama);

                    Match m = LRx.Match(lab.Plain);
                    if (!m.Success) { notLabel++; continue; }
                    string rawVal = m.Groups["val"].Value.Replace(',', '.');
                    if (!double.TryParse(rawVal, NumberStyles.Float, CultureInfo.InvariantCulture, out double oldVal))
                    {
                        notLabel++;
                        continue;
                    }
                    if (Math.Abs(oldVal - rounded) < 0.05)
                    {
                        same++;
                        continue;
                    }

                    string newL = lFmt + FormatLen(rounded);
                    string newPlain = LRx.Replace(lab.Plain, newL, 1);
                    lab.Ent.UpgradeOpen();
                    if (lab.Ent is DBText dbt)
                        dbt.TextString = newPlain;
                    else if (lab.Ent is MText mt)
                        mt.Contents = ReplacePlainKeepingMText(lab.Raw, lab.Plain, newPlain);

                    updated++;
                    if (DonatiBoyuSettings.OnizlemeMesaji)
                        ed.WriteMessage("\n  {0} → {1}",
                            oldVal.ToString("0.##", CultureInfo.InvariantCulture),
                            FormatLen(rounded));
                }

                tr.Commit();
            }

            ed.WriteMessage(
                "\nDONATIBOYU: guncellenen={0}, ayni={1}, etiket degil={2}, donati yok={3}.",
                updated, same, notLabel, noBar);
        }

        private sealed class LabelHit
        {
            public Entity Ent;
            public string Raw;
            public string Plain;
            public Point3d Anchor;
            public double Rotation;
        }

        private struct MatchPair
        {
            public int LabelIndex;
            public int BarIndex;
        }

        private struct Seg
        {
            public Point2d A;
            public Point2d B;
            public bool Arc;
            public double ArcLen;
        }

        private static List<MatchPair> MatchLabels(List<LabelHit> labels, List<Curve> bars)
        {
            var cands = new List<(int Li, int Bi, double Dist, bool Par)>();
            for (int li = 0; li < labels.Count; li++)
            {
                for (int bi = 0; bi < bars.Count; bi++)
                {
                    try
                    {
                        Point3d close = bars[bi].GetClosestPointTo(labels[li].Anchor, false);
                        double d = close.DistanceTo(labels[li].Anchor);
                        if (d > MaxGap) continue;
                        bool par = ParallelToText(bars[bi], close, labels[li].Rotation);
                        cands.Add((li, bi, d, par));
                    }
                    catch { }
                }
            }

            var order = new List<int>();
            for (int li = 0; li < labels.Count; li++)
                order.Add(li);
            order.Sort((a, b) => BestScore(cands, a).CompareTo(BestScore(cands, b)));

            var usedB = new HashSet<int>();
            var pairs = new List<MatchPair>();
            foreach (int li in order)
            {
                bool hasPar = false;
                for (int k = 0; k < cands.Count; k++)
                {
                    if (cands[k].Li == li && cands[k].Par && !usedB.Contains(cands[k].Bi))
                    {
                        hasPar = true;
                        break;
                    }
                }
                int bestBi = -1;
                double bestD = double.MaxValue;
                bool bestPar = false;
                for (int k = 0; k < cands.Count; k++)
                {
                    var cand = cands[k];
                    if (cand.Li != li || usedB.Contains(cand.Bi)) continue;
                    if (hasPar && !cand.Par) continue;
                    if (bestBi < 0 || cand.Par && !bestPar || cand.Par == bestPar && cand.Dist < bestD)
                    {
                        bestBi = cand.Bi;
                        bestD = cand.Dist;
                        bestPar = cand.Par;
                    }
                }
                if (bestBi < 0) continue;
                usedB.Add(bestBi);
                pairs.Add(new MatchPair { LabelIndex = li, BarIndex = bestBi });
            }
            return pairs;
        }

        private static double BestScore(List<(int Li, int Bi, double Dist, bool Par)> cands, int li)
        {
            double best = double.MaxValue;
            for (int k = 0; k < cands.Count; k++)
            {
                if (cands[k].Li != li) continue;
                double s = cands[k].Par ? cands[k].Dist : cands[k].Dist + 100000;
                if (s < best) best = s;
            }
            return best;
        }

        private static bool ParallelToText(Curve c, Point3d onCurve, double textRot)
        {
            try
            {
                Vector3d d = c.GetFirstDerivative(onCurve);
                if (d.Length < 1e-8) return false;
                double ang = Math.Atan2(d.Y, d.X);
                return UndirectedDelta(ang, textRot) < ParallelRad;
            }
            catch { return false; }
        }

        private static double UndirectedDelta(double a, double b)
        {
            double d = a - b;
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d < -Math.PI) d += 2 * Math.PI;
            d = Math.Abs(d);
            if (d > Math.PI / 2) d = Math.PI - d;
            return d;
        }

        private static List<Curve> CollectModelBars(Transaction tr, Database db)
        {
            var list = new List<Curve>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased) continue;
                if (!DrawingLayerOk(ent.Layer) || IsMetrajBalloon(ent)) continue;
                if (ent is Line || ent is Polyline || ent is Polyline2d)
                {
                    if (ent is Curve c)
                        list.Add(c);
                }
            }
            return list;
        }

        /// <summary>Metraj poz balonu donatı çizgisi değildir; boyu ölçülmez.</summary>
        private static bool IsMetrajBalloon(Entity ent)
        {
            if (ent == null) return false;
            string layer = ent.Layer ?? "";
            if (layer.IndexOf("POZ BALON", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (layer.IndexOf("HPOZ BALON", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (layer.IndexOf("POZ TARAMA", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            try
            {
                ResultBuffer rb = ent.GetXDataForApplication(MetrajPozApp);
                if (rb == null) return false;
                rb.Dispose();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool DrawingLayerOk(string layer)
        {
            return DonatiBoyuSettings.CizimKatmanlar.Exists(
                name => string.Equals(name, layer, StringComparison.OrdinalIgnoreCase));
        }

        private static bool LabelLayerOk(string layer)
        {
            var named = DonatiBoyuSettings.Katmanlar;
            if (named == null) return false;
            for (int i = 0; i < named.Count; i++)
            {
                if (string.Equals(named[i], layer, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsDonatiEtiket(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return false;
            if (!LRx.IsMatch(plain)) return false;
            return ContainsPhi(plain, DonatiBoyuSettings.FiIsareti);
        }

        private static bool ContainsPhi(string plain, string fi)
        {
            if (string.IsNullOrWhiteSpace(fi)) return true;
            if (plain.IndexOf(fi.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!IsPhiToken(fi)) return false;
            if (plain.IndexOf("%%c", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (plain.IndexOf('ø') >= 0 || plain.IndexOf('Ø') >= 0 || plain.IndexOf('⌀') >= 0)
                return true;
            if (plain.IndexOf("\\U+00D8", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (plain.IndexOf("\\U+00F8", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (plain.IndexOf("\\U+2205", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool IsPhiToken(string fi)
        {
            string t = (fi ?? "").Trim();
            return t == "ø" || t == "Ø" || t == "⌀" ||
                   t.Equals("%%c", StringComparison.OrdinalIgnoreCase);
        }

        private static Point3d TextAnchor(Entity ent)
        {
            if (ent is DBText dbText)
            {
                return dbText.AlignmentPoint.DistanceTo(Point3d.Origin) > 1e-9 &&
                       dbText.Justify != AttachmentPoint.BaseLeft
                    ? dbText.AlignmentPoint
                    : dbText.Position;
            }
            if (ent is MText mtext)
                return mtext.Location;
            return Point3d.Origin;
        }

        /// <summary>Gönye yayı varsa keskin köşe boyu; düz çizgide geometrik boy.</summary>
        private static double SharpLength(Curve c, Transaction tr)
        {
            if (c is Line ln) return ln.Length;
            if (c is Polyline pl) return SharpFromSegs(PolySegs(pl), pl.Closed);
            if (c is Polyline2d p2) return SharpFromSegs(Poly2dSegs(p2, tr), p2.Closed);
            try
            {
                return Math.Abs(c.GetDistanceAtParameter(c.EndParam) - c.GetDistanceAtParameter(c.StartParam));
            }
            catch { return 0; }
        }

        private static List<Seg> PolySegs(Polyline pl)
        {
            var segs = new List<Seg>();
            int n = pl.NumberOfVertices;
            if (n < 2) return segs;
            int segCount = pl.Closed ? n : n - 1;
            for (int i = 0; i < segCount; i++)
            {
                int j = (i + 1) % n;
                Point2d a = pl.GetPoint2dAt(i);
                Point2d b = pl.GetPoint2dAt(j);
                double bulge = pl.GetBulgeAt(i);
                bool arc = Math.Abs(bulge) > 1e-8;
                segs.Add(new Seg
                {
                    A = a,
                    B = b,
                    Arc = arc,
                    ArcLen = arc ? ArcLength(a, b, bulge) : a.GetDistanceTo(b)
                });
            }
            return segs;
        }

        private static List<Seg> Poly2dSegs(Polyline2d pl, Transaction tr)
        {
            var verts = new List<(Point2d P, double B)>();
            foreach (ObjectId id in pl)
            {
                var v = tr.GetObject(id, OpenMode.ForRead) as Vertex2d;
                if (v == null) continue;
                verts.Add((new Point2d(v.Position.X, v.Position.Y), v.Bulge));
            }
            var segs = new List<Seg>();
            if (verts.Count < 2) return segs;
            int n = verts.Count;
            int segCount = pl.Closed ? n : n - 1;
            for (int i = 0; i < segCount; i++)
            {
                var a = verts[i];
                var b = verts[(i + 1) % n];
                bool arc = Math.Abs(a.B) > 1e-8;
                segs.Add(new Seg
                {
                    A = a.P,
                    B = b.P,
                    Arc = arc,
                    ArcLen = arc ? ArcLength(a.P, b.P, a.B) : a.P.GetDistanceTo(b.P)
                });
            }
            return segs;
        }

        private static double ArcLength(Point2d a, Point2d b, double bulge)
        {
            double chord = a.GetDistanceTo(b);
            double theta = 4.0 * Math.Atan(bulge);
            double s = Math.Sin(theta * 0.5);
            if (Math.Abs(s) < 1e-8) return chord;
            double radius = chord / (2.0 * s);
            return Math.Abs(radius * theta);
        }

        private static double SharpFromSegs(List<Seg> segs, bool closed)
        {
            if (segs == null || segs.Count == 0) return 0;
            bool anyArc = false;
            for (int i = 0; i < segs.Count; i++)
                if (segs[i].Arc) { anyArc = true; break; }
            if (!anyArc)
            {
                double sum = 0;
                for (int i = 0; i < segs.Count; i++)
                    sum += segs[i].A.GetDistanceTo(segs[i].B);
                return sum;
            }
            if (closed) return ClosedSharp(segs);
            return OpenSharp(segs);
        }

        private static double OpenSharp(List<Seg> segs)
        {
            double len = 0;
            Point2d cursor = segs[0].A;
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                if (!s.Arc)
                {
                    if (IsLeadIn(segs, i))
                        continue;
                    len += cursor.GetDistanceTo(s.B);
                    cursor = s.B;
                    continue;
                }

                bool prevLine = i > 0 && !segs[i - 1].Arc;
                bool nextLine = i + 1 < segs.Count && !segs[i + 1].Arc;
                if (prevLine && nextLine &&
                    TryLineIntersect(segs[i - 1].A, segs[i - 1].B, segs[i + 1].A, segs[i + 1].B, out Point2d corner) &&
                    CornerSane(segs[i - 1], segs[i + 1], corner))
                {
                    len += cursor.GetDistanceTo(corner);
                    cursor = corner;
                }
                else
                {
                    len += cursor.GetDistanceTo(s.A);
                    len += s.ArcLen > 0 ? s.ArcLen : s.A.GetDistanceTo(s.B);
                    cursor = s.B;
                }
            }
            return len;
        }

        private static bool IsLeadIn(List<Seg> segs, int i)
        {
            if (segs[i].Arc) return false;
            if (i + 1 >= segs.Count || !segs[i + 1].Arc) return false;
            if (i + 2 >= segs.Count || segs[i + 2].Arc) return false;
            return true;
        }

        private static double ClosedSharp(List<Seg> segs)
        {
            int n = segs.Count;
            var corners = new List<Point2d>();
            for (int i = 0; i < n; i++)
            {
                int prev = (i + n - 1) % n;
                int next = (i + 1) % n;
                if (segs[i].Arc && !segs[prev].Arc && !segs[next].Arc &&
                    TryLineIntersect(segs[prev].A, segs[prev].B, segs[next].A, segs[next].B, out Point2d c) &&
                    CornerSane(segs[prev], segs[next], c))
                {
                    corners.Add(c);
                }
                else if (!segs[i].Arc && !segs[prev].Arc)
                {
                    corners.Add(segs[i].A);
                }
            }
            if (corners.Count < 2)
            {
                double sum = 0;
                for (int i = 0; i < n; i++)
                    sum += segs[i].Arc ? segs[i].ArcLen : segs[i].A.GetDistanceTo(segs[i].B);
                return sum;
            }
            double len = 0;
            for (int i = 0; i < corners.Count; i++)
                len += corners[i].GetDistanceTo(corners[(i + 1) % corners.Count]);
            return len;
        }

        private static bool CornerSane(Seg incoming, Seg outgoing, Point2d c)
        {
            double cutIn = incoming.B.GetDistanceTo(c);
            double cutOut = outgoing.A.GetDistanceTo(c);
            double limit = Math.Max(80,
                Math.Max(incoming.A.GetDistanceTo(incoming.B), outgoing.A.GetDistanceTo(outgoing.B)));
            if (cutIn > limit || cutOut > limit) return false;
            Vector2d vin = incoming.B - incoming.A;
            Vector2d toC = c - incoming.A;
            if (vin.X * toC.X + vin.Y * toC.Y <= 0) return false;
            return true;
        }

        private static bool TryLineIntersect(Point2d p1, Point2d p2, Point2d p3, Point2d p4, out Point2d hit)
        {
            double d = (p1.X - p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X - p4.X);
            if (Math.Abs(d) < 1e-10)
            {
                hit = Point2d.Origin;
                return false;
            }
            double px = ((p1.X * p2.Y - p1.Y * p2.X) * (p3.X - p4.X) -
                         (p1.X - p2.X) * (p3.X * p4.Y - p3.Y * p4.X)) / d;
            double py = ((p1.X * p2.Y - p1.Y * p2.X) * (p3.Y - p4.Y) -
                         (p1.Y - p2.Y) * (p3.X * p4.Y - p3.Y * p4.X)) / d;
            hit = new Point2d(px, py);
            return true;
        }

        /// <summary>
        /// Tam sayı. 5 cm tikliyse alt 5'li kata göre artık ≤ 1 cm ise alta, fazlası üste.
        /// </summary>
        internal static double RoundLen(double cm, DonatiBoyuYuvarlama y)
        {
            if (cm < 0) cm = 0;
            if (y != DonatiBoyuYuvarlama.BesCm)
                return Math.Round(cm, MidpointRounding.AwayFromZero);

            double floor5 = Math.Floor(cm / 5.0) * 5.0;
            double rem = cm - floor5;
            if (rem <= 1.0 + 1e-6)
                return floor5;
            return floor5 + 5.0;
        }

        private static string FormatLen(double cm)
        {
            long n = (long)Math.Round(cm, MidpointRounding.AwayFromZero);
            if (n < 0) n = 0;
            return n.ToString(CultureInfo.InvariantCulture);
        }

        private static string StripMText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, @"\\[PpLlOoKk]", " ");
            s = Regex.Replace(s, @"\{[^;]*;", "");
            s = s.Replace("{", "").Replace("}", "");
            return s;
        }

        private static string ReplacePlainKeepingMText(string original, string plainOld, string plainNew)
        {
            if (original.IndexOf('\\') < 0 && original.IndexOf('{') < 0)
                return plainNew;
            return LRx.Replace(original, plainNew, 1);
        }
    }
}
