using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>Kolon donatı tablosu: GPR/PRN'den veri okur, ST4 kat isimleriyle tabloyu çizer. Birim: cm.</summary>
    public sealed class KolonDonatiTableDrawer
    {
        private const string LayerGrid = "donatiplus.com";
        private const string LayerPenc4 = "PENC4";
        private const string LayerPenc2 = "PENC2";
        private const string LayerHeader = "PENC4";
        private const string LayerYazi = "YAZI (BEYKENT)";
        private const string LayerDonatiYazisi = "DONATI YAZISI (BEYKENT)";
        private const string LayerKolonIsmi = "KOLON ISMI (BEYKENT)";
        private const string LayerSubasman = "SUBASMAN (BEYKENT)";
        private const string LayerCizgi = "CIZGI (BEYKENT)";
        private const string LayerKot = "KOT (BEYKENT)";
        private const string LayerKirisIsmi = "KIRIS ISMI (BEYKENT)";
        private const short AciThinLine = 251;
        private const short AciLabelGray = 252;
        private const double ColWidthKatNo = 50;
        private const double ColWidthSubHeader = 100;
        private const double ColWidthFloor = 150;
        private const double RowHeightHeader = 50;
        private const double RowHeightDataBlock = 75;  // 3 x 25
        private const double TextHeightMain = 11.5;
        private const double TextHeightSmall = 9.0;
        private const double TextWidthFactor = 0.7;
        private const double Penc4TextShiftRightCm = 5.0;
        private const string TextStyleYazi = "YAZI (BEYKENT)";
        private const string FontName = "Bahnschrift Light Condensed";
        /// <summary>Bx satırı 9. sütun boyuna donatı — boyutun 25 cm altı, DONATI YAZISI.</summary>
        private const double DonatiBoyunaBoyutAltinaCm = 25.0;
        /// <summary>By satırı 9. sütun etriye — boyutun 50 cm altı, DONATI YAZISI.</summary>
        private const double EtriyeBoyutAltinaCm = 50.0;

        /// <summary>Donatı notasyonunda fi / φ → ø (U+00F8, Alt+0248).</summary>
        public static string NormalizeDiameterSymbol(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("\uFB01", "\u00F8");
            s = s.Replace("\u03C6", "\u00F8").Replace("\u03A6", "\u00F8");
            s = Regex.Replace(s, @"(?<=\d)\s*fi\s*(?=\d)", "\u00F8", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"(?<=\d)fi(?=\d)", "\u00F8", RegexOptions.IgnoreCase);
            return s;
        }

        private static string StripGovdeSuffix(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return Regex.Replace(s.Trim(), @"\s*\(govde\)\s*$", "", RegexOptions.IgnoreCase).Trim();
        }

        /// <summary>GPR: ¡ sütun ayırıcı; 1-tabanlı sütun no (9 = donatı metni).</summary>
        private static string GetGprColumnByInvertedExclamation(string line, int oneBasedColumn)
        {
            if (string.IsNullOrEmpty(line) || oneBasedColumn < 1) return null;
            var parts = line.Split('\u00A1');
            if (parts.Length <= oneBasedColumn) return null;
            string p = parts[oneBasedColumn].Trim();
            return string.IsNullOrEmpty(p) ? null : p;
        }

        private static string GetGprDonatiCell(string line, bool isGpr)
        {
            if (isGpr)
            {
                string c9 = GetGprColumnByInvertedExclamation(line, 9);
                if (!string.IsNullOrEmpty(c9))
                    return StripGovdeSuffix(c9);
            }
            return StripGovdeSuffix(GetLastTableColumn(line).Trim());
        }

        private static string ReplaceTimesWithAsciiX(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\u00D7", "x");
        }

        private static string FormatDonatiDisplay(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return ReplaceTimesWithAsciiX(NormalizeDiameterSymbol(StripGovdeSuffix(raw)));
        }

        private static bool TryGetDonatiForFloorColumn(Dictionary<string, (string ebat, string donati, string etriye)> data, int floorIndex, int colNo, bool gprHasSaAfterSz, bool gprHasScCati, int catiFloorIndex, out string donati)
        {
            donati = null;
            if (data == null || data.Count == 0) return false;
            string prefix = GetFloorPrefix(floorIndex, gprHasSaAfterSz, gprHasScCati, catiFloorIndex) + "-";
            foreach (var kv in data)
            {
                if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string rest = kv.Key.Substring(prefix.Length).Trim();
                if (int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed == colNo)
                {
                    donati = kv.Value.donati;
                    return !string.IsNullOrWhiteSpace(donati);
                }
            }
            return false;
        }

        private static bool TryGetEtriyeForFloorColumn(Dictionary<string, (string ebat, string donati, string etriye)> data, int floorIndex, int colNo, bool gprHasSaAfterSz, bool gprHasScCati, int catiFloorIndex, out string etriye)
        {
            etriye = null;
            if (data == null || data.Count == 0) return false;
            string prefix = GetFloorPrefix(floorIndex, gprHasSaAfterSz, gprHasScCati, catiFloorIndex) + "-";
            foreach (var kv in data)
            {
                if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string rest = kv.Key.Substring(prefix.Length).Trim();
                if (int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed == colNo)
                {
                    etriye = kv.Value.etriye;
                    return !string.IsNullOrWhiteSpace(etriye);
                }
            }
            return false;
        }

        /// <summary>GPR 9. sütun etriye: (etriye) ve [ ] kaldırılır; başta rakam+ø ise araya x → 3xø8/15/8.</summary>
        private static string FormatEtriyeForTableDisplay(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string s = raw.Trim();
            s = Regex.Replace(s, @"\s*\(etriye\)\s*$", "", RegexOptions.IgnoreCase).Trim();
            s = s.Replace("[", string.Empty).Replace("]", string.Empty);
            s = NormalizeDiameterSymbol(s.Trim());
            s = Regex.Replace(s, @"^(\d+)(\u00F8|\u03C6|\u03A6)", "$1x$2");
            s = ReplaceTimesWithAsciiX(s);
            return string.IsNullOrEmpty(s) ? null : s;
        }

        /// <summary>
        /// SL_09.GPR: UTF-8, sütun ayırıcı ¡ (C2 A1). ASLIM/A_AKD gibi eski çıktılar: Windows-1252, tek bayt A1=¡, D7=×, F8=ø.
        /// </summary>
        private static string[] ReadGprPrnLines(string filePath)
        {
            byte[] b = File.ReadAllBytes(filePath);
            int n = Math.Min(b.Length, 600000);
            int utf8Delim = 0;
            int winDelim = 0;
            for (int i = 0; i < n - 1; i++)
            {
                if (b[i] == 0xC2 && b[i + 1] == 0xA1)
                {
                    utf8Delim++;
                    i++;
                }
                else if (b[i] == 0xA1 && (i == 0 || b[i - 1] != 0xC2))
                    winDelim++;
            }
            Encoding enc;
            if (utf8Delim >= 25 && utf8Delim >= winDelim)
                enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
            else if (winDelim >= 25)
                enc = Encoding.GetEncoding(1252);
            else if (winDelim > utf8Delim * 2)
                enc = Encoding.GetEncoding(1252);
            else
                enc = new UTF8Encoding(false, false);
            return enc.GetString(b).Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        }

        /// <summary>Kolon id (örn. SB-01) -> (ebat, donati, etriye).</summary>
        public static Dictionary<string, (string ebat, string donati, string etriye)> ParseKolonBetonarmeFromFile(string filePath, out string error)
        {
            error = null;
            var result = new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase);
            string[] lines;
            try
            {
                lines = ReadGprPrnLines(filePath);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return result;
            }

            bool isGpr = filePath.EndsWith(".gpr", StringComparison.OrdinalIgnoreCase);
            int startIdx = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i] ?? string.Empty;
                string content = StripGprLinePrefix(raw);
                if (content.IndexOf("KOLON BETONARME HESAP", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    startIdx = i + 1;
                    break;
                }
            }
            if (startIdx < 0) { error = "KOLON BETONARME HESAP SONUCLARI bolumu bulunamadi."; return result; }

            var colIdRegex = new Regex(@"([A-Z]{1,2}\d*-\d+)", RegexOptions.IgnoreCase);
            var bxRegex = new Regex(@"Bx[=\s]*(\d+)", RegexOptions.IgnoreCase);
            var byRegex = new Regex(@"By[=\s]*(\d+)", RegexOptions.IgnoreCase);
            var polygonRegex = new Regex(@"Polygon", RegexOptions.IgnoreCase);

            string currentId = null;
            string currentBx = null, currentBy = null;
            string currentDonati = null, currentEtriye = null;
            bool inBlock = false;

            for (int i = startIdx; i < lines.Length; i++)
            {
                string raw = lines[i] ?? string.Empty;
                string content = StripGprLinePrefix(raw).Trim();
                if (content.Length == 0) continue;
                if (content.StartsWith("Panel ", StringComparison.OrdinalIgnoreCase) ||
                    content.IndexOf("POLIGON KOLON", StringComparison.OrdinalIgnoreCase) >= 0)
                    break;

                var idMatch = colIdRegex.Match(content);
                if (idMatch.Success && idMatch.Index < 20)
                {
                    if (currentId != null && (currentDonati != null || currentEtriye != null))
                    {
                        string ebat = GetEbatString(currentBx, currentBy, (currentBx != null && currentBx.Equals("Polygon", StringComparison.OrdinalIgnoreCase)));
                        result[currentId] = (NormalizeDiameterSymbol(ebat ?? ""), NormalizeDiameterSymbol(currentDonati ?? ""), NormalizeDiameterSymbol(currentEtriye ?? ""));
                    }
                    currentId = idMatch.Groups[1].Value.Trim().ToUpperInvariant();
                    currentBx = null; currentBy = null; currentDonati = null; currentEtriye = null;
                    inBlock = true;

                    var bx = bxRegex.Match(content);
                    if (bx.Success) currentBx = bx.Groups[1].Value;
                    var by = byRegex.Match(content);
                    if (by.Success) currentBy = by.Groups[1].Value;
                    if (polygonRegex.IsMatch(content)) { currentBx = "Polygon"; currentBy = null; }

                    string donatiCell = GetGprDonatiCell(content, isGpr);
                    SplitDonatiEtriye(donatiCell, out string don, out string et);
                    if (!string.IsNullOrEmpty(don)) currentDonati = don;
                    if (!string.IsNullOrEmpty(et)) currentEtriye = et;
                }
                else if (inBlock && currentId != null)
                {
                    var by = byRegex.Match(content);
                    if (by.Success) currentBy = by.Groups[1].Value;
                    if (polygonRegex.IsMatch(content)) { currentBx = "Polygon"; currentBy = null; }
                    string donatiCell = GetGprDonatiCell(content, isGpr);
                    SplitDonatiEtriye(donatiCell, out string don, out string et);
                    if (!string.IsNullOrEmpty(et)) currentEtriye = et;
                    if (!string.IsNullOrEmpty(don) && currentDonati == null) currentDonati = don;
                }
            }
            if (currentId != null && (currentDonati != null || currentEtriye != null))
            {
                string ebat = GetEbatString(currentBx, currentBy, currentBx == "Polygon");
                result[currentId] = (NormalizeDiameterSymbol(ebat ?? ""), NormalizeDiameterSymbol(currentDonati ?? ""), NormalizeDiameterSymbol(currentEtriye ?? ""));
            }
            return result;
        }

        private static string StripGprLinePrefix(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            var m = Regex.Match(line.Trim(), @"^\d+,\d+,\d+,\d+,-?\d+,\d+\s*");
            return m.Success ? line.Trim().Substring(m.Length) : line.Trim();
        }

        private static string GetLastTableColumn(string line)
        {
            char[] sep = { '\u00a7', '\u00c0', '\u00c1', '\u00c2', '\u00c3', '\u00c4', '\u00c5', '\u00c6', '\u00c7', '\u00d9', '\u00da', '\u00db', '\u00dc', '\u00dd', '\u00de', '\u00df', '\u00b5', '\u00b6', '\u00b7', '\u00af', '\u00b0', '\u00b1', '\u00b2', '\u00b3', '\u00b4', '¡', '¦', '|', '\t' };
            string s = line.Trim();
            int maxLast = -1;
            foreach (char c in sep)
            {
                int last = s.LastIndexOf(c);
                if (last > maxLast) maxLast = last;
            }
            if (maxLast >= 0 && maxLast < s.Length - 1)
                s = s.Substring(maxLast + 1).Trim();
            return s.Trim();
        }

        private static void SplitDonatiEtriye(string cell, out string donati, out string etriye)
        {
            donati = null; etriye = null;
            if (string.IsNullOrWhiteSpace(cell)) return;
            int idx = cell.IndexOf("(etriye)", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                etriye = cell.Substring(0, idx).Trim();
                if (etriye.StartsWith("ø", StringComparison.Ordinal) || etriye.StartsWith("Ã¸", StringComparison.Ordinal))
                    etriye = "ø" + etriye.TrimStart('ø', 'Ã', '¸');
                string rest = cell.Substring(idx + 8).Trim();
                if (rest.Length > 0) donati = rest;
            }
            else
                donati = cell;
        }

        private static string GetEbatString(string bx, string by, bool isPolygon)
        {
            if (isPolygon || (bx != null && bx.Equals("Polygon", StringComparison.OrdinalIgnoreCase)))
                return "Polygon";
            if (string.IsNullOrEmpty(bx) && string.IsNullOrEmpty(by)) return "";
            int bxi = 0, byi = 0;
            int.TryParse(bx, NumberStyles.Integer, CultureInfo.InvariantCulture, out bxi);
            int.TryParse(by, NumberStyles.Integer, CultureInfo.InvariantCulture, out byi);
            if (bxi == byi && bxi > 0) return "R= " + bxi.ToString(CultureInfo.InvariantCulture);
            if (bxi > 0 && byi > 0) return bxi.ToString(CultureInfo.InvariantCulture) + "/" + byi.ToString(CultureInfo.InvariantCulture);
            if (bxi > 0) return bxi.ToString(CultureInfo.InvariantCulture);
            if (byi > 0) return byi.ToString(CultureInfo.InvariantCulture);
            return bx ?? by ?? "";
        }

        /// <summary>GPR/PRN'de SA- kodlu kolon varsa (ASLIM vb.): 0=SB, 1=SZ, 2=SA, 3=S1, ... Aksi: 0=SB, 1=SZ, 2=S1, ...</summary>
        private static bool GprContainsSaFloorPrefix(Dictionary<string, (string ebat, string donati, string etriye)> data)
        {
            if (data == null) return false;
            foreach (var k in data.Keys)
            {
                if (string.IsNullOrEmpty(k)) continue;
                if (k.StartsWith("SA-", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>GPR çatı kolon kodu SC-39, SC-40 (ASLIM CATI katı).</summary>
        private static bool GprContainsScCatiPrefix(Dictionary<string, (string ebat, string donati, string etriye)> data)
        {
            if (data == null) return false;
            foreach (var k in data.Keys)
            {
                if (string.IsNullOrEmpty(k)) continue;
                if (k.StartsWith("SC-", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>Son eşleşen CATI/ÇATI katı (GPR SC- ile çatı donatısı).</summary>
        private static int FindCatiFloorIndex(IReadOnlyList<string> floorNames)
        {
            if (floorNames == null) return -1;
            int last = -1;
            for (int i = 0; i < floorNames.Count; i++)
            {
                string n = floorNames[i] ?? string.Empty;
                if (n.IndexOf("CATI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("ÇATI", StringComparison.OrdinalIgnoreCase) >= 0)
                    last = i;
            }
            return last;
        }

        private static string GetFloorPrefix(int floorIndex, bool gprHasSaAfterSz, bool gprHasScCati, int catiFloorIndex)
        {
            if (floorIndex == 0) return "SB";
            if (floorIndex == 1) return "SZ";

            if (gprHasSaAfterSz)
            {
                if (floorIndex == 2) return "SA";
                if (floorIndex >= 3)
                {
                    if (gprHasScCati && catiFloorIndex >= 0 && floorIndex == catiFloorIndex)
                        return "SC";
                    if (gprHasScCati && catiFloorIndex >= 0 && floorIndex < catiFloorIndex)
                        return "S" + (floorIndex - 2).ToString(CultureInfo.InvariantCulture);
                    if (gprHasScCati && catiFloorIndex >= 0 && floorIndex > catiFloorIndex)
                        return "S" + (floorIndex - 3).ToString(CultureInfo.InvariantCulture);
                    return "S" + (floorIndex - 2).ToString(CultureInfo.InvariantCulture);
                }
            }
            else
            {
                if (gprHasScCati && catiFloorIndex >= 0 && floorIndex == catiFloorIndex)
                    return "SC";
                if (floorIndex >= 2)
                {
                    if (gprHasScCati && catiFloorIndex >= 0 && floorIndex < catiFloorIndex)
                        return "S" + (floorIndex - 1).ToString(CultureInfo.InvariantCulture);
                    if (gprHasScCati && catiFloorIndex >= 0 && floorIndex > catiFloorIndex)
                        return "S" + (floorIndex - 2).ToString(CultureInfo.InvariantCulture);
                    return "S" + (floorIndex - 1).ToString(CultureInfo.InvariantCulture);
                }
            }

            return "S1";
        }

        public static bool Draw(St4Model model, Dictionary<string, (string ebat, string donati, string etriye)> columnData,
            Point3d insertPoint, Database db, Editor ed, Transaction tr, BlockTableRecord btr,
            Dictionary<int, (double? temelCm, double? hatilCm)> columnFoundationHeights = null,
            List<Dictionary<int, (int columnType, double W, double H)>> columnDimsByFloor = null,
            List<Dictionary<int, (double altKotCm, double yukseklikCm, double? kirisUstAltFarkCm)>> columnTableExtraByFloor = null,
            HashSet<(int floorIndex, int columnNo)> columnActiveCells = null)
        {
            if (model == null || model.Floors == null || model.Floors.Count == 0)
            {
                ed.WriteMessage("\nKOLONDATA: ST4'te kat bulunamadi.");
                return false;
            }
            int numFloors = model.Floors.Count;
            var floorNames = model.Floors.Select(f => f.Name ?? "").ToList();

            bool gprHasSaAfterSz = GprContainsSaFloorPrefix(columnData);
            bool gprHasScCati = GprContainsScCatiPrefix(columnData);
            int catiFloorIndex = FindCatiFloorIndex(floorNames);
            if (gprHasScCati && catiFloorIndex < 0)
                catiFloorIndex = numFloors - 1;

            var columnIds = new HashSet<string>(columnData?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var rowColumnNumbers = new List<int>();
            for (int fn = 0; fn < numFloors; fn++)
            {
                string prefix = GetFloorPrefix(fn, gprHasSaAfterSz, gprHasScCati, catiFloorIndex);
                foreach (var k in columnData?.Keys ?? Enumerable.Empty<string>())
                {
                    if (k.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase))
                    {
                        string numPart = k.Substring(prefix.Length + 1).Trim();
                        if (int.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int colNo) && !rowColumnNumbers.Contains(colNo))
                            rowColumnNumbers.Add(colNo);
                    }
                }
            }
            rowColumnNumbers.Sort();
            if (rowColumnNumbers.Count == 0) rowColumnNumbers.Add(1);

            double totalWidth = ColWidthKatNo + ColWidthSubHeader + numFloors * ColWidthFloor;
            double totalHeight = RowHeightHeader + rowColumnNumbers.Count * RowHeightDataBlock;

            EnsureLayers(tr, db);
            EnsureTextStyle(tr, db, TextStyleYazi);

            double x0 = insertPoint.X;
            double y0 = insertPoint.Y;
            double yTop = y0 + totalHeight;

            DrawGrid(tr, btr, x0, yTop, numFloors, rowColumnNumbers.Count);
            DrawHeaderRow(tr, btr, db, x0, yTop, floorNames);
            DrawDataRows(tr, btr, db, x0, yTop - RowHeightHeader, numFloors, floorNames, rowColumnNumbers, columnData ?? new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase), columnFoundationHeights, columnDimsByFloor, columnTableExtraByFloor, columnActiveCells, gprHasSaAfterSz, gprHasScCati, catiFloorIndex);
            return true;
        }

        private static void EnsureLayers(Transaction tr, Database db)
        {
            LayerService.EnsureLayer(tr, db, LayerGrid, 7);
            LayerService.EnsureLayer(tr, db, LayerPenc4, 7);
            LayerService.EnsureLayer(tr, db, LayerPenc2, 7);
            LayerService.EnsureLayer(tr, db, LayerHeader, 7);
            LayerService.EnsureLayer(tr, db, LayerYazi, 140);
            LayerService.EnsureLayer(tr, db, LayerDonatiYazisi, 3, LineWeight.LineWeight020);
            LayerService.EnsureLayer(tr, db, LayerKolonIsmi, 91);
            LayerService.EnsureLayer(tr, db, LayerSubasman, 197);
            LayerService.EnsureLayer(tr, db, LayerCizgi, 1, LineWeight.LineWeight020);
            LayerService.EnsureLayer(tr, db, LayerKot, 7);
            LayerService.EnsureLayer(tr, db, LayerKirisIsmi, 40);
        }

        private static void EnsureTextStyle(Transaction tr, Database db, string styleName)
        {
            var ts = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (ts.Has(styleName)) return;
            ts.UpgradeOpen();
            var str = new TextStyleTableRecord { Name = styleName };
            try { str.Font = new FontDescriptor(FontName, false, false, 0, 0); }
            catch { try { str.Font = new FontDescriptor("Bahnschrift", false, false, 0, 0); } catch { } }
            ts.Add(str);
            tr.AddNewlyCreatedDBObject(str, true);
        }

        /// <summary>Sol ust (0,0), Y asagi negatif. User (ux,uy) -> cizim (x0+ux, yTop+uy).</summary>
        private static Point3d U(double x0, double yTop, double ux, double uy)
        {
            return new Point3d(x0 + ux, yTop + uy, 0);
        }

        private static void DrawGrid(Transaction tr, BlockTableRecord btr, double x0, double yTop, int numFloors, int numRows)
        {
            const double wLeft = 150; // 50 Kat no + 100 ebat/donati/etriye
            double totalWidth = wLeft + numFloors * ColWidthFloor; // 150 + numFloors*150
            double bottomY = -RowHeightHeader - numRows * RowHeightDataBlock; // -50 - numRows*75

            // --- PENC4: Ana tablo çizgileri ---
            // Kat no alanı: LINE 0,0/150,0 - 0,0/0,-50 - 150,0/150,-50 - 0,-50/150,-50
            AppendLine(tr, btr, U(x0, yTop, 0, 0), U(x0, yTop, totalWidth, 0), LayerPenc4);
            AppendLine(tr, btr, U(x0, yTop, 0, 0), U(x0, yTop, 0, -50), LayerPenc4);
            AppendLine(tr, btr, U(x0, yTop, 150, 0), U(x0, yTop, 150, -50), LayerPenc4);
            AppendLine(tr, btr, U(x0, yTop, 0, -50), U(x0, yTop, totalWidth, -50), LayerPenc4);

            // İlk kat alanı: yatay çizgiler tek parça, dikeyler ayrı
            for (int f = 0; f < numFloors; f++)
            {
                double xL = 150 + f * 150;
                double xR = 150 + (f + 1) * 150;
                AppendLine(tr, btr, U(x0, yTop, xR, 0), U(x0, yTop, xR, -50), LayerPenc4);
            }

            // İlk kolon alanı (her kolon için 75 aşağı): LINE 0,-50/0,-125 - 50,-50/50,-125 - 0,-125/150,-125 - 150,-50/150,-125
            for (int r = 0; r < numRows; r++)
            {
                double yT = -50 - r * 75;
                double yB = -125 - r * 75;
                AppendLine(tr, btr, U(x0, yTop, 0, yT), U(x0, yTop, 0, yB), LayerPenc4);
                AppendLine(tr, btr, U(x0, yTop, 50, yT), U(x0, yTop, 50, yB), LayerPenc4);
                AppendLine(tr, btr, U(x0, yTop, 150, yT), U(x0, yTop, 150, yB), LayerPenc4);
                AppendLine(tr, btr, U(x0, yTop, 0, yB), U(x0, yTop, totalWidth, yB), LayerPenc4);
            }

            // İlk kolon ile kat kesişimi: LINE 150,-125/300,-125 - 300,-50/300,-125 (kata göre 150 sağa, kolon sayısına 75 aşağı)
            for (int r = 0; r < numRows; r++)
            {
                double yT = -50 - r * 75;
                double yB = -125 - r * 75;
                for (int f = 0; f < numFloors; f++)
                {
                    double xL = 150 + f * 150;
                    double xR = 150 + (f + 1) * 150;
                    AppendLine(tr, btr, U(x0, yTop, xR, yT), U(x0, yTop, xR, yB), LayerPenc4);
                }
            }

            // Tablo alt ve sağ dış çizgi
            AppendLine(tr, btr, U(x0, yTop, 0, bottomY), U(x0, yTop, totalWidth, bottomY), LayerPenc4);
            AppendLine(tr, btr, U(x0, yTop, totalWidth, 0), U(x0, yTop, totalWidth, bottomY), LayerPenc4);

            // --- PENC2: Kolon alanı içi ince çizgiler (ebat-donati, donati-etriye) ---
            // İlk kolon alanı: LINE 50,-75/150,-75 - 50,-100/150,-100 (kolon sayısına 75 aşağı)
            for (int r = 0; r < numRows; r++)
            {
                double y75 = -75 - r * 75;
                double y100 = -100 - r * 75;
                AppendLine(tr, btr, U(x0, yTop, 50, y75), U(x0, yTop, totalWidth, y75), LayerPenc2);
                AppendLine(tr, btr, U(x0, yTop, 50, y100), U(x0, yTop, totalWidth, y100), LayerPenc2);
            }
        }

        private static void AppendLine(Transaction tr, BlockTableRecord btr, Point3d from, Point3d to, string layer, short? colorIndex = null)
        {
            var line = new Line(from, to) { Layer = layer };
            if (colorIndex.HasValue)
                line.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex.Value);
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        private static void DrawHeaderRow(Transaction tr, BlockTableRecord btr, Database db, double x0, double yTop, List<string> floorNames)
        {
            DrawTextLeftBottom(tr, btr, db, x0 + 15 + Penc4TextShiftRightCm, yTop - 20, "Kat no", TextHeightMain, LayerHeader, TextStyleYazi, null);
            double cellLeft = x0 + ColWidthKatNo + ColWidthSubHeader;
            for (int i = 0; i < floorNames.Count; i++)
            {
                string name = string.IsNullOrEmpty(floorNames[i]) ? (i + 1).ToString(CultureInfo.InvariantCulture) + ". NORMAL" : floorNames[i];
                DrawTextLeftBottom(tr, btr, db, cellLeft + 40 + Penc4TextShiftRightCm, yTop - 25, name, TextHeightMain, LayerHeader, TextStyleYazi, null);
                cellLeft += ColWidthFloor;
            }
        }

        private static double EstimateTextWidthCm(string text, double heightCm)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return text.Length * heightCm * TextWidthFactor * 0.65;
        }

        private static void DrawDataRows(Transaction tr, BlockTableRecord btr, Database db, double x0, double yRowTop, int numFloors, List<string> floorNames, List<int> rowColumnNumbers, Dictionary<string, (string ebat, string donati, string etriye)> columnData, Dictionary<int, (double? temelCm, double? hatilCm)> columnFoundationHeights, List<Dictionary<int, (int columnType, double W, double H)>> columnDimsByFloor, List<Dictionary<int, (double altKotCm, double yukseklikCm, double? kirisUstAltFarkCm)>> columnTableExtraByFloor, HashSet<(int floorIndex, int columnNo)> columnActiveCells, bool gprHasSaAfterSz, bool gprHasScCati, int catiFloorIndex)
        {
            const double labelX = 5;
            double ebadDonatiX = x0 + ColWidthKatNo + 10;
            double temelTextX = ebadDonatiX + 40;
            double hatilTextX = ebadDonatiX + 70;
            const double cizgiLengthCm = 15.0;
            const double cizgiYOffsetCm = 7.5;
            const double cellBoyutOffset = 10;
            const double cellKotOffset = 50;
            const double cellYukseklikOffset = 85;
            const double cellKirisOffset = 120;

            for (int r = 0; r < rowColumnNumbers.Count; r++)
            {
                int colNo = rowColumnNumbers[r];
                double rowTop = yRowTop - r * RowHeightDataBlock;

                DrawTextLeftBottom(tr, btr, db, ebadDonatiX, rowTop - 20, "ebad", TextHeightSmall, LayerGrid, TextStyleYazi, AciLabelGray);
                DrawTextLeftBottom(tr, btr, db, ebadDonatiX, rowTop - 45, "donati", TextHeightSmall, LayerGrid, TextStyleYazi, AciLabelGray);
                DrawTextLeftBottom(tr, btr, db, ebadDonatiX, rowTop - 70, "etriye", TextHeightSmall, LayerGrid, TextStyleYazi, AciLabelGray);

                if (columnFoundationHeights != null && columnFoundationHeights.TryGetValue(colNo, out var heights))
                {
                    if (heights.temelCm.HasValue)
                        DrawTextLeftBottom(tr, btr, db, temelTextX, rowTop - 20, ((int)Math.Round(heights.temelCm.Value)).ToString(CultureInfo.InvariantCulture), TextHeightMain, LayerYazi, TextStyleYazi, null);
                    if (heights.temelCm.HasValue && heights.hatilCm.HasValue)
                    {
                        double hatilFark = heights.hatilCm.Value - heights.temelCm.Value;
                        DrawTextLeftBottom(tr, btr, db, hatilTextX, rowTop - 20, ((int)Math.Round(hatilFark)).ToString(CultureInfo.InvariantCulture), TextHeightMain, LayerSubasman, TextStyleYazi, null);
                    }
                }

                for (int f = 0; f < numFloors; f++)
                {
                    if (columnActiveCells != null && !columnActiveCells.Contains((f, colNo)))
                        continue;

                    double cellLeft = x0 + ColWidthKatNo + ColWidthSubHeader + f * ColWidthFloor + 5;
                    var dimsForFloor = columnDimsByFloor != null && f < columnDimsByFloor.Count ? columnDimsByFloor[f] : null;
                    var extraForFloor = columnTableExtraByFloor != null && f < columnTableExtraByFloor.Count ? columnTableExtraByFloor[f] : null;

                    double boyutX = cellLeft + cellBoyutOffset;
                    double kotX = cellLeft + cellKotOffset;
                    double yukX = cellLeft + cellYukseklikOffset;
                    double kirisX = cellLeft + cellKirisOffset;

                    if (dimsForFloor != null && dimsForFloor.TryGetValue(colNo, out var dims))
                    {
                        if (dims.columnType == 3)
                        {
                            if (TryGetDonatiForFloorColumn(columnData, f, colNo, gprHasSaAfterSz, gprHasScCati, catiFloorIndex, out string donRawP))
                            {
                                string donDisp = FormatDonatiDisplay(donRawP);
                                if (!string.IsNullOrEmpty(donDisp))
                                    DrawDonatiTextUnderBoyut(tr, btr, db, boyutX, rowTop - 20 - DonatiBoyunaBoyutAltinaCm, donDisp);
                            }
                            if (TryGetEtriyeForFloorColumn(columnData, f, colNo, gprHasSaAfterSz, gprHasScCati, catiFloorIndex, out string etRawP))
                            {
                                string etDisp = FormatEtriyeForTableDisplay(etRawP);
                                if (!string.IsNullOrEmpty(etDisp))
                                    DrawDonatiTextUnderBoyut(tr, btr, db, boyutX, rowTop - 20 - EtriyeBoyutAltinaCm, etDisp, 30);
                            }
                        }
                        else
                        {
                            string dimStr;
                            bool drawCizgi = false;
                            if (dims.columnType == 2 && dims.W > 0)
                            {
                                dimStr = "R= " + ((int)Math.Round(dims.W)).ToString(CultureInfo.InvariantCulture);
                                drawCizgi = dims.W < 30;
                            }
                            else if (dims.W > 0 && dims.H > 0)
                            {
                                int wi = (int)Math.Round(dims.W);
                                int hi = (int)Math.Round(dims.H);
                                dimStr = wi.ToString(CultureInfo.InvariantCulture) + "/" + hi.ToString(CultureInfo.InvariantCulture);
                                bool kenar25 = wi == 25 || hi == 25;
                                if (kenar25)
                                {
                                    int diger = wi == 25 ? hi : wi;
                                    drawCizgi = diger <= 149;
                                }
                                else
                                    drawCizgi = wi < 30 || hi < 30;
                            }
                            else
                                dimStr = null;
                            if (dimStr != null)
                            {
                                DrawTextLeftBottom(tr, btr, db, boyutX, rowTop - 20, dimStr, TextHeightMain, LayerKolonIsmi, TextStyleYazi, null);
                                if (drawCizgi)
                                {
                                    double textW = EstimateTextWidthCm(dimStr, TextHeightMain);
                                    double lineX0 = boyutX + textW;
                                    double lineY = rowTop - 20 + cizgiYOffsetCm;
                                    AppendLine(tr, btr, new Point3d(lineX0, lineY, 0), new Point3d(lineX0 + cizgiLengthCm, lineY, 0), LayerCizgi);
                                }
                                if (TryGetDonatiForFloorColumn(columnData, f, colNo, gprHasSaAfterSz, gprHasScCati, catiFloorIndex, out string donRaw))
                                {
                                    string donDisp = FormatDonatiDisplay(donRaw);
                                    if (!string.IsNullOrEmpty(donDisp))
                                        DrawDonatiTextUnderBoyut(tr, btr, db, boyutX, rowTop - 20 - DonatiBoyunaBoyutAltinaCm, donDisp);
                                }
                                if (TryGetEtriyeForFloorColumn(columnData, f, colNo, gprHasSaAfterSz, gprHasScCati, catiFloorIndex, out string etRaw))
                                {
                                    string etDisp = FormatEtriyeForTableDisplay(etRaw);
                                    if (!string.IsNullOrEmpty(etDisp))
                                        DrawDonatiTextUnderBoyut(tr, btr, db, boyutX, rowTop - 20 - EtriyeBoyutAltinaCm, etDisp, 30);
                                }
                            }
                        }
                    }

                    if (extraForFloor != null && extraForFloor.TryGetValue(colNo, out var extra))
                    {
                        DrawTextLeftBottom(tr, btr, db, kotX, rowTop - 20, ((int)Math.Round(extra.altKotCm)).ToString(CultureInfo.InvariantCulture), TextHeightMain, LayerKot, TextStyleYazi, null);
                        double yukGosterCm = extra.yukseklikCm;
                        if (f == 0 && columnFoundationHeights != null && columnFoundationHeights.TryGetValue(colNo, out var fh))
                        {
                            if (fh.temelCm.HasValue && fh.hatilCm.HasValue)
                                yukGosterCm -= fh.hatilCm.Value - fh.temelCm.Value;
                            else if (fh.hatilCm.HasValue)
                                yukGosterCm -= fh.hatilCm.Value;
                        }
                        if (yukGosterCm < 0) yukGosterCm = 0;
                        DrawTextLeftBottom(tr, btr, db, yukX, rowTop - 20, ((int)Math.Round(yukGosterCm)).ToString(CultureInfo.InvariantCulture), TextHeightMain, LayerYazi, TextStyleYazi, null);
                        if (extra.kirisUstAltFarkCm.HasValue)
                            DrawTextLeftBottom(tr, btr, db, kirisX, rowTop - 20, ((int)Math.Round(extra.kirisUstAltFarkCm.Value)).ToString(CultureInfo.InvariantCulture), TextHeightMain, LayerKirisIsmi, TextStyleYazi, null);
                    }
                }

                DrawTextLeftBottom(tr, btr, db, x0 + labelX + Penc4TextShiftRightCm, rowTop - 45, "S" + (r + 1).ToString(CultureInfo.InvariantCulture), TextHeightMain, LayerHeader, TextStyleYazi, null);
            }
        }

        private static void DrawDonatiTextUnderBoyut(Transaction tr, BlockTableRecord btr, Database db, double x, double y, string text, short? colorIndex = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            var ts = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            ObjectId styleId = ts.Has(TextStyleYazi) ? ts[TextStyleYazi] : db.Textstyle;
            var txt = new DBText
            {
                Position = new Point3d(x - 5, y, 0),
                Height = TextHeightMain,
                TextString = text,
                Layer = LayerDonatiYazisi,
                TextStyleId = styleId,
                HorizontalMode = TextHorizontalMode.TextLeft,
                WidthFactor = TextWidthFactor,
                LineWeight = LineWeight.LineWeight020
            };
            if (colorIndex.HasValue)
                txt.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex.Value);
            btr.AppendEntity(txt);
            tr.AddNewlyCreatedDBObject(txt, true);
        }

        /// <summary>Referans DXF ile uyumlu: sol-alt (baseline) yerleşim, genişlik 0.7, isteğe bağlı ACI renk.</summary>
        private static void DrawTextLeftBottom(Transaction tr, BlockTableRecord btr, Database db, double x, double y, string text, double height, string layer, string styleName, short? colorIndex)
        {
            if (string.IsNullOrEmpty(text)) return;
            var ts = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            ObjectId styleId = ts.Has(styleName) ? ts[styleName] : db.Textstyle;
            var txt = new DBText
            {
                Position = new Point3d(x - 5, y, 0),
                Height = height,
                TextString = text,
                Layer = layer,
                TextStyleId = styleId,
                HorizontalMode = TextHorizontalMode.TextLeft,
                WidthFactor = TextWidthFactor
            };
            if (colorIndex.HasValue)
                txt.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex.Value);
            btr.AppendEntity(txt);
            tr.AddNewlyCreatedDBObject(txt, true);
        }
    }
}
