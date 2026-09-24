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
    /// Seçilen donatı yazılarındaki L=/l= boyunu, yakındaki Line/Polyline uzunluğuna göre günceller.
    /// </summary>
    internal static class DonatiBoyuRunner
    {
        private static readonly Regex LRx = new Regex(
            @"(?<prefix>[Ll]\s*=\s*)(?<val>\d+(?:[.,]\d+)?)",
            RegexOptions.Compiled);

        public static void RunInteractive()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var peo = new PromptSelectionOptions
            {
                MessageForAdding = "\nL= güncellenecek donatı yazılarını seçin: ",
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
                ed.WriteMessage("\nDONATIBOYU: Seçim yok.");
                return;
            }

            int updated = 0, skipped = 0, noCurve = 0, noL = 0;
            double scale = DonatiBoyuSettings.OlcekCarpani;
            double searchR = DonatiBoyuSettings.AramaYaricapi;
            double minDiff = DonatiBoyuSettings.MinFarkCm;
            string lFmt = DonatiBoyuSettings.LEtiketFormati ?? "L=";
            bool onlyDonati = DonatiBoyuSettings.SadeceDonatiKatmani;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                var curves = CollectCurves(tr, btr, onlyDonati);

                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    string text;
                    Point3d anchor;
                    if (ent is DBText dbText)
                    {
                        text = dbText.TextString ?? "";
                        anchor = dbText.AlignmentPoint.DistanceTo(Point3d.Origin) > 1e-9 &&
                                 dbText.Justify != AttachmentPoint.BaseLeft
                            ? dbText.AlignmentPoint
                            : dbText.Position;
                    }
                    else if (ent is MText mtext)
                    {
                        text = mtext.Contents ?? "";
                        anchor = mtext.Location;
                    }
                    else continue;

                    if (!LRx.IsMatch(StripMText(text)))
                    {
                        noL++;
                        continue;
                    }

                    Curve nearest = FindNearestCurve(curves, anchor, searchR);
                    if (nearest == null)
                    {
                        noCurve++;
                        continue;
                    }

                    double lenDraw = Math.Abs(
                        nearest.GetDistanceAtParameter(nearest.EndParam)
                        - nearest.GetDistanceAtParameter(nearest.StartParam));
                    double lenCm = lenDraw * scale;
                    double rounded = RoundLen(lenCm, DonatiBoyuSettings.Yuvarlama);

                    string plain = StripMText(text);
                    Match m = LRx.Match(plain);
                    if (!m.Success) { noL++; continue; }

                    double oldVal;
                    string raw = m.Groups["val"].Value.Replace(',', '.');
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out oldVal))
                    {
                        noL++;
                        continue;
                    }

                    if (Math.Abs(oldVal - rounded) < minDiff)
                    {
                        skipped++;
                        continue;
                    }

                    string newL = lFmt + FormatLen(rounded);
                    string newPlain = LRx.Replace(plain, newL, 1);

                    ent.UpgradeOpen();
                    if (ent is DBText dbt)
                        dbt.TextString = newPlain;
                    else if (ent is MText mt)
                        mt.Contents = ReplacePlainKeepingMText(text, plain, newPlain);

                    updated++;
                    if (DonatiBoyuSettings.OnizlemeMesaji)
                        ed.WriteMessage("\n  {0} → {1} (cizgi {2})",
                            oldVal.ToString("0.##", CultureInfo.InvariantCulture),
                            FormatLen(rounded),
                            lenCm.ToString("0.##", CultureInfo.InvariantCulture));
                }

                tr.Commit();
            }

            ed.WriteMessage(
                "\nDONATIBOYU: guncellenen={0}, ayni/min fark={1}, L yok={2}, yakin cizgi yok={3}.",
                updated, skipped, noL, noCurve);
        }

        private static List<Curve> CollectCurves(Transaction tr, BlockTableRecord btr, bool onlyDonati)
        {
            var list = new List<Curve>();
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased) continue;
                if (!(ent is Line || ent is Polyline || ent is Polyline2d || ent is Polyline3d))
                    continue;
                if (onlyDonati && !LayerLooksLikeDonati(ent.Layer))
                    continue;
                if (ent is Curve c)
                    list.Add(c);
            }
            return list;
        }

        private static bool LayerLooksLikeDonati(string layer)
        {
            if (string.IsNullOrEmpty(layer)) return false;
            string u = layer.ToUpperInvariant();
            return u.Contains("DONATI") || u.Contains("ETRIYE") || u.Contains("HASIR") || u.Contains("CIROZ");
        }

        private static Curve FindNearestCurve(List<Curve> curves, Point3d pt, double maxDist)
        {
            Curve best = null;
            double bestD = maxDist;
            foreach (Curve c in curves)
            {
                try
                {
                    Point3d closest = c.GetClosestPointTo(pt, false);
                    double d = closest.DistanceTo(pt);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = c;
                    }
                }
                catch { }
            }
            return best;
        }

        private static double RoundLen(double cm, DonatiBoyuYuvarlama y)
        {
            if (y == DonatiBoyuYuvarlama.Yok) return Math.Round(cm, 1);
            int step = (int)y;
            if (step <= 0) return Math.Round(cm, 1);
            return Math.Round(cm / step) * step;
        }

        private static string FormatLen(double cm)
        {
            if (Math.Abs(cm - Math.Round(cm)) < 1e-6)
                return ((int)Math.Round(cm)).ToString(CultureInfo.InvariantCulture);
            return cm.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static string StripMText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Basit MText kaçışlarını temizle
            s = Regex.Replace(s, @"\\[PpLlOoKk]", " ");
            s = Regex.Replace(s, @"\{[^;]*;", "");
            s = s.Replace("{", "").Replace("}", "");
            return s;
        }

        private static string ReplacePlainKeepingMText(string original, string plainOld, string plainNew)
        {
            // MText içeriği sade ise doğrudan yeni düz metin
            if (original.IndexOf('\\') < 0 && original.IndexOf('{') < 0)
                return plainNew;
            // L= değerini orijinal içinde regex ile değiştir
            return LRx.Replace(original, plainNew, 1);
        }
    }
}
