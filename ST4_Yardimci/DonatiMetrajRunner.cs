using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Seçilen donatı yazılarından (adet/çap/L) birleşik metraj tablosu çizer.
    /// Mevcut METRAJ (KSF) komutundan ayrıdır → DONATIMETRAJ.
    /// </summary>
    internal static class DonatiMetrajRunner
    {
        // 6Ø14 L=420  |  6xØ14 L=420  |  Ø12/15 l=306  |  Φ10 L=250
        private static readonly Regex RxAdetCapL = new Regex(
            @"(?:(?<adet>\d+)\s*[x×*]?\s*)?[ØøΦφ]\s*(?<cap>\d{1,2})(?:\s*/\s*(?<aralik>\d+(?:[.,]\d+)?))?\s*[Ll]\s*=\s*(?<boy>\d+(?:[.,]\d+)?)",
            RegexOptions.Compiled);

        private const string LayerTablo = "METRAJ TABLO (BEYKENT)";
        private const string LayerYazi = "METRAJ YAZI (BEYKENT)";
        private const string LayerToplam = "METRAJ TOPLAM (BEYKENT)";
        private const string LayerSayi = "DONATI YAZISI (BEYKENT)";

        private sealed class Row
        {
            public int CapMm;
            public int Adet;
            public double BoyCm;
            public double? AralikCm;
            public double ToplamBoyCm => Adet * BoyCm;
            public double Kg => ToplamBoyCm / 100.0 * CapMm * CapMm * 0.006165; // ≈ π/4*7.85e-3
        }

        public static void RunInteractive()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var peo = new PromptSelectionOptions
            {
                MessageForAdding = "\nMetraja girecek donatı yazılarını seçin: ",
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
                ed.WriteMessage("\nDONATIMETRAJ: Seçim yok.");
                return;
            }

            var parsed = new List<Row>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    if (DonatiMetrajSettings.SadeceDonatiYazisiKatmani &&
                        !(ent.Layer ?? "").ToUpperInvariant().Contains("DONATI YAZISI"))
                        continue;

                    string text = ent is DBText d ? d.TextString : (ent is MText m ? m.Contents : null);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    text = StripMText(text);

                    Match match = RxAdetCapL.Match(text);
                    if (!match.Success) continue;

                    int adet = 1;
                    if (match.Groups["adet"].Success)
                        int.TryParse(match.Groups["adet"].Value, out adet);
                    if (adet < 1) adet = 1;

                    int cap;
                    if (!int.TryParse(match.Groups["cap"].Value, out cap)) continue;

                    double boy;
                    if (!TryParseD(match.Groups["boy"].Value, out boy)) continue;

                    double? aralik = null;
                    if (match.Groups["aralik"].Success)
                    {
                        double a;
                        if (TryParseD(match.Groups["aralik"].Value, out a))
                            aralik = a;
                    }

                    parsed.Add(new Row { CapMm = cap, Adet = adet, BoyCm = boy, AralikCm = aralik });
                }
                tr.Commit();
            }

            if (parsed.Count == 0)
            {
                ed.WriteMessage("\nDONATIMETRAJ: Tanınan donatı yazısı yok (ör. 6Ø14 L=420).");
                return;
            }

            List<Row> rows = DonatiMetrajSettings.CapBazindaGrupla
                ? GroupRows(parsed)
                : parsed;

            PromptPointResult pIns = ed.GetPoint("\nTablo sol-üst köşesi: ");
            if (pIns.Status != PromptStatus.OK) return;
            Point3d origin = pIns.Value;

            double h = DonatiMetrajSettings.PuntoYukseklikCm;
            double rowH = h * DonatiMetrajSettings.SatirAraligiCarpan;
            bool kg = DonatiMetrajSettings.AgirlikHesapla;

            // sütun genişlikleri (cm)
            double[] colW = kg
                ? new[] { 18.0, 22.0, 22.0, 28.0, 22.0 }
                : new[] { 18.0, 22.0, 22.0, 28.0 };
            string[] headers = kg
                ? new[] { "Çap", "Adet", "Boy", "Toplam", "kg" }
                : new[] { "Çap", "Adet", "Boy", "Toplam" };

            int dataRows = rows.Count + (DonatiMetrajSettings.ToplamSatiri ? 1 : 0);
            int totalRows = dataRows + 2; // başlık + sütun başlığı
            double tableW = 0;
            foreach (double w in colW) tableW += w;
            double tableH = totalRows * rowH;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                EnsureLayer(tr, db, LayerTablo, 206, 0.20);
                EnsureLayer(tr, db, LayerYazi, 60, 0.20);
                EnsureLayer(tr, db, LayerToplam, 142, 0.30);
                EnsureLayer(tr, db, LayerSayi, 3, 0.20);

                // dış çerçeve + yatay çizgiler
                DrawRect(tr, btr, origin.X, origin.Y - tableH, tableW, tableH, LayerTablo);
                for (int r = 1; r < totalRows; r++)
                {
                    double y = origin.Y - r * rowH;
                    string lyr = (DonatiMetrajSettings.ToplamSatiri && r == totalRows - 1)
                        ? LayerToplam
                        : LayerTablo;
                    DrawHLine(tr, btr, origin.X, y, tableW, lyr);
                }
                double xAcc = origin.X;
                for (int c = 0; c < colW.Length - 1; c++)
                {
                    xAcc += colW[c];
                    DrawVLine(tr, btr, xAcc, origin.Y, tableH, LayerTablo);
                }

                // başlık
                AddText(tr, btr, DonatiMetrajSettings.Baslik,
                    origin.X + tableW * 0.5, origin.Y - rowH * 0.55, h * 1.1, LayerYazi, true);

                // sütun başlıkları
                double yHead = origin.Y - rowH - rowH * 0.55;
                DrawHeaderRow(tr, btr, headers, colW, origin.X, yHead, h, LayerYazi);

                double sumAdet = 0, sumBoy = 0, sumKg = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    Row row = rows[i];
                    double y = origin.Y - (2 + i) * rowH - rowH * 0.55;
                    string[] cells = kg
                        ? new[]
                        {
                            "Ø" + row.CapMm,
                            row.Adet.ToString(CultureInfo.InvariantCulture),
                            FormatNum(row.BoyCm),
                            FormatNum(row.ToplamBoyCm),
                            row.Kg.ToString("0.0", CultureInfo.InvariantCulture)
                        }
                        : new[]
                        {
                            "Ø" + row.CapMm,
                            row.Adet.ToString(CultureInfo.InvariantCulture),
                            FormatNum(row.BoyCm),
                            FormatNum(row.ToplamBoyCm)
                        };
                    DrawDataRow(tr, btr, cells, colW, origin.X, y, h, LayerSayi);
                    sumAdet += row.Adet;
                    sumBoy += row.ToplamBoyCm;
                    sumKg += row.Kg;
                }

                if (DonatiMetrajSettings.ToplamSatiri)
                {
                    double y = origin.Y - (2 + rows.Count) * rowH - rowH * 0.55;
                    string[] cells = kg
                        ? new[]
                        {
                            "TOPLAM",
                            ((int)sumAdet).ToString(CultureInfo.InvariantCulture),
                            "",
                            FormatNum(sumBoy),
                            sumKg.ToString("0.0", CultureInfo.InvariantCulture)
                        }
                        : new[]
                        {
                            "TOPLAM",
                            ((int)sumAdet).ToString(CultureInfo.InvariantCulture),
                            "",
                            FormatNum(sumBoy)
                        };
                    DrawDataRow(tr, btr, cells, colW, origin.X, y, h, LayerToplam);
                }

                tr.Commit();
            }

            ed.WriteMessage("\nDONATIMETRAJ: {0} etiket → {1} satır tablo.", parsed.Count, rows.Count);
        }

        private static List<Row> GroupRows(List<Row> src)
        {
            var map = new Dictionary<string, Row>(StringComparer.Ordinal);
            foreach (Row r in src)
            {
                string key = r.CapMm + "|" + r.BoyCm.ToString("0.###", CultureInfo.InvariantCulture);
                Row g;
                if (!map.TryGetValue(key, out g))
                {
                    map[key] = new Row
                    {
                        CapMm = r.CapMm,
                        Adet = r.Adet,
                        BoyCm = r.BoyCm,
                        AralikCm = r.AralikCm
                    };
                }
                else
                    g.Adet += r.Adet;
            }
            return map.Values.OrderBy(r => r.CapMm).ThenBy(r => r.BoyCm).ToList();
        }

        private static bool TryParseD(string s, out double v)
        {
            return double.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private static string FormatNum(double v)
        {
            if (Math.Abs(v - Math.Round(v)) < 1e-6)
                return ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
            return v.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static string StripMText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, @"\\[PpLlOoKk]", " ");
            s = Regex.Replace(s, @"\{[^;]*;", "");
            return s.Replace("{", "").Replace("}", "");
        }

        private static void DrawHeaderRow(Transaction tr, BlockTableRecord btr, string[] cells, double[] colW, double x0, double y, double h, string layer)
        {
            double x = x0;
            for (int i = 0; i < cells.Length; i++)
            {
                AddText(tr, btr, cells[i], x + colW[i] * 0.5, y, h, layer, true);
                x += colW[i];
            }
        }

        private static void DrawDataRow(Transaction tr, BlockTableRecord btr, string[] cells, double[] colW, double x0, double y, double h, string layer)
        {
            double x = x0;
            for (int i = 0; i < cells.Length; i++)
            {
                AddText(tr, btr, cells[i], x + colW[i] * 0.5, y, h, layer, true);
                x += colW[i];
            }
        }

        private static void AddText(Transaction tr, BlockTableRecord btr, string s, double x, double y, double h, string layer, bool mid)
        {
            if (string.IsNullOrEmpty(s)) return;
            var t = new DBText
            {
                TextString = s,
                Height = h,
                Layer = layer,
                Position = new Point3d(x, y, 0)
            };
            if (mid)
            {
                t.HorizontalMode = TextHorizontalMode.TextCenter;
                t.VerticalMode = TextVerticalMode.TextVerticalMid;
                t.AlignmentPoint = new Point3d(x, y, 0);
            }
            btr.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
        }

        private static void DrawRect(Transaction tr, BlockTableRecord btr, double xmin, double ymin, double w, double h, string layer)
        {
            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(xmin, ymin), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(xmin + w, ymin), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(xmin + w, ymin + h), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(xmin, ymin + h), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layer;
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static void DrawHLine(Transaction tr, BlockTableRecord btr, double x, double y, double w, string layer)
        {
            var ln = new Line(new Point3d(x, y, 0), new Point3d(x + w, y, 0)) { Layer = layer };
            btr.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
        }

        private static void DrawVLine(Transaction tr, BlockTableRecord btr, double x, double yTop, double h, string layer)
        {
            var ln = new Line(new Point3d(x, yTop, 0), new Point3d(x, yTop - h, 0)) { Layer = layer };
            btr.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
        }

        private static void EnsureLayer(Transaction tr, Database db, string name, short aci, double lineWeightMm)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, aci)
            };
            try
            {
                // 0.20 m ≈ LineWeight020
                ltr.LineWeight = lineWeightMm >= 0.25 ? LineWeight.LineWeight030 : LineWeight.LineWeight020;
            }
            catch { }
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }
    }
}
