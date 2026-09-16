using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// KSF keşif tablolarını Line + DBText ile çizer.
    /// Başlık/sütun başlığı: METRAJ YAZI (BEYKENT);
    /// Tablo ızgarası METRAJ TABLO; dış çerçeve METRAJ YAZI; toplam satırı METRAJ TOPLAM.
    /// </summary>
    internal static class KsfTableDrawer
    {
        public const string LayerMetrajYazi = "METRAJ YAZI (BEYKENT)";
        public const string LayerMetrajTablo = "METRAJ TABLO (BEYKENT)";
        public const string LayerDonatiYazisi = "DONATI YAZISI (BEYKENT)";
        public const string LayerYazi = "YAZI (BEYKENT)";
        public const string LayerMetrajToplam = "METRAJ TOPLAM (BEYKENT)";
        private const string TextStyleName = "YAZI (BEYKENT)";
        private const string TextFontName = "Bahnschrift Light Condensed";
        private const double RowHeight = 20.0;
        private const double ColWidth = 150.0;
        private const double NumericColWidth = 60.0;
        private const double KesifCol0 = 90.0;
        private const double KesifCol1 = 200.0;
        private const double KesifColN = 80.0;
        private const double TextHeightCm = 10.0;
        private const double TextWidthFactor = 0.85;
        private const double TitleHeightCm = 14.0;
        private const double TitleGapCm = 8.0;
        private const double TableGapCm = 55.0;
        private const double NoteHeightCm = 10.0;
        private const double NoteGapCm = 6.0;
        private const double CellPadCm = 3.5;

        private static readonly Regex FloorIdPrefix = new Regex(@"^\s*(\d+)\s*>", RegexOptions.Compiled);
        private static readonly Regex MostlyNumeric = new Regex(
            @"^[\s\d\.\+\-%/m²³tnkgøØ\u00F8]+$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool Draw(KsfDocument ksf, Point3d insert, Database db, Editor ed, Transaction tr, BlockTableRecord btr)
        {
            if (ksf == null || ksf.Tables.Count == 0)
            {
                ed?.WriteMessage("\nMETRAJ: Tablosu bulunamadi.");
                return false;
            }

            EnsureMetrajLayers(tr, db);
            ObjectId styleId = GetOrCreateUnicodeTextStyle(tr, db);
            double x = insert.X;
            double y = insert.Y;
            double z = insert.Z;
            int drawn = 0;
            foreach (KsfTableBlock block in ksf.Tables)
            {
                if (block == null || block.ColumnCount < 1 || block.Rows.Count == 0)
                    continue;
                if (IsDuvarMetrajTable(block))
                    continue;
                KesifOzetiPricer.ApplyIfKesifOzeti(block);
                DropAllZeroPlakDolguColumn(block);
                if (!string.IsNullOrWhiteSpace(block.Title))
                {
                    AppendDbText(tr, btr, db, styleId, new Point3d(x, y, z), block.Title.Trim(), TitleHeightCm, TextHorizontalMode.TextLeft, TextVerticalMode.TextBottom, LayerMetrajYazi);
                    y -= TitleGapCm;
                }

                y = DrawGridTable(block, x, y, z, tr, btr, db, styleId);

                if (block.NotesAfter != null)
                {
                    foreach (string note in block.NotesAfter)
                    {
                        if (string.IsNullOrWhiteSpace(note)) continue;
                        y -= NoteGapCm;
                        AppendDbText(tr, btr, db, styleId, new Point3d(x, y, z), note.Trim(), NoteHeightCm, TextHorizontalMode.TextLeft, TextVerticalMode.TextTop, LayerMetrajYazi);
                        y -= NoteHeightCm;
                    }
                }
                y -= TableGapCm * 0.65;
                drawn++;
            }

            if (drawn == 0)
            {
                ed?.WriteMessage("\nMETRAJ: Cizilecek tablo yok.");
                return false;
            }
            ed?.WriteMessage("\nMETRAJ: {0} tablo cizildi (METRAJ YAZI / METRAJ TABLO / DONATI YAZISI).", drawn);
            return true;
        }

        private static bool IsDuvarMetrajTable(KsfTableBlock block)
        {
            return TitleContains(block, "DUVAR METRAJ");
        }

        private static bool UsesNarrowValueColumns(KsfTableBlock block)
        {
            return TitleContains(block, "BETON/KALIP") || TitleContains(block, "DONATI METRAJ");
        }

        private static void DropAllZeroPlakDolguColumn(KsfTableBlock block)
        {
            int col = FindHeaderColumn(block, "PLAK DOLGU");
            if (col < 0) return;
            if (!ColumnBodyIsAllZero(block, col)) return;
            RemoveColumn(block, col);
        }

        private static int FindHeaderColumn(KsfTableBlock block, string asciiNeedle)
        {
            if (block?.Rows == null || block.Rows.Count == 0) return -1;
            KsfTableRow header = block.Rows[0];
            if (header?.Cells == null) return -1;
            int n = Math.Min(block.ColumnCount, header.Cells.Length);
            for (int c = 0; c < n; c++)
            {
                if (CellContainsNorm(header.Cells[c], asciiNeedle))
                    return c;
            }
            return -1;
        }

        private static bool ColumnBodyIsAllZero(KsfTableBlock block, int col)
        {
            for (int r = 1; r < block.Rows.Count; r++)
            {
                KsfTableRow row = block.Rows[r];
                string cell = "";
                if (row?.Cells != null && col < row.Cells.Length)
                    cell = row.Cells[col] ?? "";
                if (string.IsNullOrWhiteSpace(cell)) continue;
                if (!IsZeroNumericCell(cell))
                    return false;
            }
            return true;
        }

        private static bool IsZeroNumericCell(string cell)
        {
            string s = FlattenCellText(cell).Replace(" ", "");
            if (s.Length == 0) return true;
            s = s.Replace(',', '.');
            if (!double.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out double v))
                return false;
            return Math.Abs(v) < 0.0005;
        }

        private static void RemoveColumn(KsfTableBlock block, int col)
        {
            if (col < 0 || col >= block.ColumnCount) return;
            block.ColumnCount--;
            if (block.ColumnCharWidths != null && col < block.ColumnCharWidths.Length)
            {
                var widths = new int[block.ColumnCharWidths.Length - 1];
                int w = 0;
                for (int i = 0; i < block.ColumnCharWidths.Length; i++)
                {
                    if (i == col) continue;
                    widths[w++] = block.ColumnCharWidths[i];
                }
                block.ColumnCharWidths = widths;
            }
            foreach (KsfTableRow row in block.Rows)
            {
                if (row?.Cells == null) continue;
                row.Cells = RemoveAt(row.Cells, col);
                if (row.ColSpans != null)
                    row.ColSpans = RemoveSpanAt(row.ColSpans, col);
            }
        }

        private static string[] RemoveAt(string[] cells, int col)
        {
            if (col >= cells.Length) return cells;
            var next = new string[cells.Length - 1];
            int w = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                if (i == col) continue;
                next[w++] = cells[i];
            }
            return next;
        }

        private static int[] RemoveSpanAt(int[] spans, int col)
        {
            if (col >= spans.Length) return spans;
            var next = new int[spans.Length - 1];
            int w = 0;
            for (int i = 0; i < spans.Length; i++)
            {
                if (i == col) continue;
                int span = spans[i];
                if (span > 0 && i < col && i + span > col)
                    span--;
                next[w++] = span;
            }
            return next;
        }

        private static bool CellContainsNorm(string text, string asciiNeedle)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(asciiNeedle)) return false;
            string n = text.ToUpperInvariant()
                .Replace('\u0131', 'I').Replace('\u0130', 'I')
                .Replace('İ', 'I').Replace('ı', 'I')
                .Replace('\u015E', 'S').Replace('\u015F', 'S')
                .Replace('\u00D6', 'O').Replace('\u00F6', 'O');
            return n.IndexOf(asciiNeedle, StringComparison.Ordinal) >= 0;
        }

        private static bool TitleContains(KsfTableBlock block, string asciiNeedle)
        {
            string title = block?.Title ?? "";
            if (title.Length == 0) return false;
            string n = title.ToUpperInvariant()
                .Replace('\u0131', 'I').Replace('\u0130', 'I')
                .Replace('İ', 'I').Replace('ı', 'I')
                .Replace('\u015E', 'S').Replace('\u015F', 'S')
                .Replace('\u00D6', 'O').Replace('\u00F6', 'O');
            return n.IndexOf(asciiNeedle, StringComparison.Ordinal) >= 0;
        }

        private static double ColumnWidth(KsfTableBlock block, int col)
        {
            if (TitleContains(block, "KESIF OZET"))
            {
                if (col == 0) return KesifCol0;
                if (col == 1) return KesifCol1;
                return KesifColN;
            }
            if (col > 0 && UsesNarrowValueColumns(block))
                return NumericColWidth;
            return ColWidth;
        }

        private static double DrawGridTable(KsfTableBlock block, double x0, double yTop, double z, Transaction tr, BlockTableRecord btr, Database db, ObjectId styleId)
        {
            int rows = block.Rows.Count;
            int cols = block.ColumnCount;
            var colX = new double[cols + 1];
            colX[0] = x0;
            for (int c = 0; c < cols; c++)
                colX[c + 1] = colX[c] + ColumnWidth(block, c);
            var rowY = new double[rows + 1];
            rowY[0] = yTop;
            for (int r = 0; r < rows; r++)
                rowY[r + 1] = rowY[r] - RowHeight;

            double x1 = colX[cols];
            double yBot = rowY[rows];

            for (int r = 0; r <= rows; r++)
                AppendLine(tr, btr, x0, rowY[r], x1, rowY[r], z, LayerMetrajTablo, 0, LineWeight.ByLayer);

            for (int b = 0; b <= cols; b++)
            {
                int r0 = 0;
                while (r0 < rows)
                {
                    if (!DrawsVertical(block, b, r0, cols))
                    {
                        r0++;
                        continue;
                    }
                    int r1 = r0 + 1;
                    while (r1 < rows && DrawsVertical(block, b, r1, cols))
                        r1++;
                    AppendLine(tr, btr, colX[b], rowY[r0], colX[b], rowY[r1], z, LayerMetrajTablo, 0, LineWeight.ByLayer);
                    r0 = r1;
                }
            }

            for (int h = 1; h < rows; h++)
            {
                if (RowBand(block, h - 1) == RowBand(block, h)) continue;
                AppendLine(tr, btr, x0, rowY[h], x1, rowY[h], z, LayerMetrajToplam, 142, LineWeight.LineWeight030);
            }

            for (int r = 0; r < rows; r++)
            {
                if (!IsToplamRow(r, block.Rows[r])) continue;
                AppendRect(tr, btr, x0, rowY[r + 1], x1, rowY[r], z, LayerMetrajToplam, 142, LineWeight.LineWeight030);
            }

            AppendRect(tr, btr, x0, yBot, x1, yTop, z, LayerMetrajYazi, 60, LineWeight.LineWeight025);

            for (int r = 0; r < rows; r++)
            {
                KsfTableRow row = block.Rows[r];
                if (row?.Cells == null) continue;
                for (int c = 0; c < cols && c < row.Cells.Length; c++)
                {
                    int span = 1;
                    if (row.ColSpans != null && c < row.ColSpans.Length)
                        span = row.ColSpans[c];
                    if (span <= 0) continue;
                    int right = Math.Min(cols, c + span);
                    bool toplam = IsToplamRow(r, row);
                    DrawCellTexts(
                        tr, btr, db, styleId,
                        colX[c], rowY[r + 1], colX[right], rowY[r], z,
                        FlattenCellText(row.Cells[c]),
                        ChooseAlign(r, c, row.Cells[c]),
                        ChooseCellLayer(r, c, row.Cells[c], toplam),
                        TextHeightCm,
                        TextWidthFactor,
                        toplam ? LineWeight.LineWeight030 : LineWeight.ByLayer);
                }
            }

            return yBot - TableGapCm * 0.35;
        }

        private static bool DrawsVertical(KsfTableBlock block, int boundary, int row, int cols)
        {
            if (boundary <= 0 || boundary >= cols) return true;
            KsfTableRow data = block.Rows[row];
            if (data?.ColSpans == null || data.Cells == null) return true;
            int c = 0;
            while (c < cols && c < data.ColSpans.Length)
            {
                int span = data.ColSpans[c];
                if (span <= 0)
                {
                    c++;
                    continue;
                }
                if (c < boundary && c + span > boundary)
                    return false;
                c += span;
            }
            return true;
        }

        private static string FlattenCellText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string s = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
            while (s.IndexOf("  ", StringComparison.Ordinal) >= 0)
                s = s.Replace("  ", " ");
            return s;
        }

        private enum AlignKind { Left, Center, Right }

        private static AlignKind ChooseAlign(int row, int col, string text)
        {
            if (row == 0) return AlignKind.Center;
            if (col == 0) return AlignKind.Left;
            if (!string.IsNullOrWhiteSpace(text) && MostlyNumeric.IsMatch(FlattenCellText(text)))
                return AlignKind.Right;
            return AlignKind.Left;
        }

        private static int RowBand(KsfTableBlock block, int row)
        {
            if (row <= 0) return -2;
            KsfTableRow data = (block?.Rows != null && row < block.Rows.Count) ? block.Rows[row] : null;
            string cell0 = "";
            if (data?.Cells != null && data.Cells.Length > 0)
                cell0 = data.Cells[0] ?? "";
            if (IsUnitSubHeader(cell0)) return -1;
            var m = FloorIdPrefix.Match(cell0);
            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                return id;
            return 0;
        }

        private static bool IsUnitSubHeader(string cell0)
        {
            string s = FlattenCellText(cell0);
            if (s.Length == 0) return false;
            return s.Equals("no", StringComparison.OrdinalIgnoreCase)
                || s.Equals("n0", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsToplamRow(int row, KsfTableRow data)
        {
            if (row <= 0 || data?.Cells == null || data.Cells.Length == 0) return false;
            return CellContainsNorm(data.Cells[0], "TOPLAM");
        }

        private static string ChooseCellLayer(int row, int col, string text, bool toplam)
        {
            if (row == 0) return LayerMetrajYazi;
            if (toplam) return LayerMetrajToplam;
            if (col == 0) return LayerYazi;
            if (!string.IsNullOrWhiteSpace(text) && MostlyNumeric.IsMatch(FlattenCellText(text)))
                return LayerDonatiYazisi;
            return LayerYazi;
        }

        private static void DrawCellTexts(
            Transaction tr, BlockTableRecord btr, Database db, ObjectId styleId,
            double xL, double yB, double xR, double yT, double z,
            string text, AlignKind align, string layer, double height, double widthFactor, LineWeight lineWeight)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            double midY = (yT + yB) * 0.5;
            TextHorizontalMode hm = TextHorizontalMode.TextLeft;
            double x = xL + CellPadCm;
            if (align == AlignKind.Center)
            {
                hm = TextHorizontalMode.TextCenter;
                x = (xL + xR) * 0.5;
            }
            else if (align == AlignKind.Right)
            {
                hm = TextHorizontalMode.TextRight;
                x = xR - CellPadCm;
            }
            AppendDbText(tr, btr, db, styleId, new Point3d(x, midY, z), text.Trim(), height, hm, TextVerticalMode.TextVerticalMid, layer, widthFactor, lineWeight);
        }

        private static void AppendRect(Transaction tr, BlockTableRecord btr, double xL, double yB, double xR, double yT, double z, string layer, short aci, LineWeight lw)
        {
            AppendLine(tr, btr, xL, yT, xR, yT, z, layer, aci, lw);
            AppendLine(tr, btr, xL, yB, xR, yB, z, layer, aci, lw);
            AppendLine(tr, btr, xL, yB, xL, yT, z, layer, aci, lw);
            AppendLine(tr, btr, xR, yB, xR, yT, z, layer, aci, lw);
        }

        private static void AppendLine(Transaction tr, BlockTableRecord btr, double x1, double y1, double x2, double y2, double z, string layer, short aci, LineWeight lw)
        {
            var ln = new Line(new Point3d(x1, y1, z), new Point3d(x2, y2, z))
            {
                Layer = layer,
                LineWeight = lw
            };
            if (aci > 0)
                ln.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
            btr.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
        }

        private static void AppendDbText(
            Transaction tr, BlockTableRecord btr, Database db, ObjectId styleId,
            Point3d pt, string text, double height,
            TextHorizontalMode hMode, TextVerticalMode vMode, string layer,
            double widthFactor = TextWidthFactor, LineWeight lineWeight = LineWeight.ByLayer)
        {
            var dbText = new DBText
            {
                Height = height,
                TextString = text ?? "",
                Layer = layer,
                WidthFactor = widthFactor,
                LineWeight = lineWeight,
                TextStyleId = styleId,
                HorizontalMode = hMode,
                VerticalMode = vMode,
                AlignmentPoint = pt,
                Position = pt
            };
            btr.AppendEntity(dbText);
            tr.AddNewlyCreatedDBObject(dbText, true);
            try { dbText.AdjustAlignment(db); }
            catch { }
        }

        private static ObjectId GetOrCreateUnicodeTextStyle(Transaction tr, Database db)
        {
            var txtTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (txtTable.Has(TextStyleName)) return txtTable[TextStyleName];
            var rec = new TextStyleTableRecord { Name = TextStyleName };
            try
            {
                rec.Font = new FontDescriptor(TextFontName, false, false, 0, 0);
            }
            catch
            {
                try { rec.Font = new FontDescriptor("Arial", false, false, 0, 0); }
                catch { }
            }
            try { rec.TextSize = 0.0; } catch { }
            try { rec.XScale = 1.0; } catch { }
            txtTable.UpgradeOpen();
            ObjectId id = txtTable.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            txtTable.DowngradeOpen();
            return id;
        }

        private static void EnsureMetrajLayers(Transaction tr, Database db)
        {
            ApplyBeykentLayer(tr, db, LayerMetrajYazi, 60, LineWeight.LineWeight020, setColor: true);
            ApplyBeykentLayer(tr, db, LayerMetrajTablo, 206, LineWeight.LineWeight020, setColor: true);
            ApplyBeykentLayer(tr, db, LayerDonatiYazisi, 3, LineWeight.LineWeight020, setColor: true);
            ApplyBeykentLayer(tr, db, LayerMetrajToplam, 142, LineWeight.LineWeight030, setColor: true);
            EnsureYaziLayerIfMissing(tr, db);
        }

        private static void ApplyBeykentLayer(Transaction tr, Database db, string name, short colorIndex, LineWeight lw, bool setColor)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            LayerTableRecord rec;
            if (lt.Has(name))
            {
                rec = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
            }
            else
            {
                lt.UpgradeOpen();
                rec = new LayerTableRecord { Name = name };
                lt.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }
            rec.LineWeight = lw;
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has("Continuous"))
                rec.LinetypeObjectId = ltt["Continuous"];
            if (setColor && colorIndex > 0)
                rec.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
            try { rec.IsOff = false; } catch { }
        }

        /// <summary>Plan katmanı zaten varsa renk/kalınlık değiştirilmez.</summary>
        private static void EnsureYaziLayerIfMissing(Transaction tr, Database db)
        {
            EnsureExistingPlanLayer(tr, db, LayerYazi, 4, LineWeight.LineWeight020);
        }

        private static void EnsureExistingPlanLayer(Transaction tr, Database db, string name, short colorIndex, LineWeight lw)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            ApplyBeykentLayer(tr, db, name, colorIndex, lw, setColor: true);
        }
    }
}
