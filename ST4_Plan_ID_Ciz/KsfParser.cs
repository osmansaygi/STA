using System;
using System.Collections.Generic;
using System.Text;

namespace ST4PlanIdCiz
{
    internal sealed class KsfDocument
    {
        public string FileName { get; set; }
        public List<KsfTableBlock> Tables { get; } = new List<KsfTableBlock>();
    }

    internal sealed class KsfTableBlock
    {
        public string Title { get; set; }
        public int ColumnCount { get; set; }
        public int[] ColumnCharWidths { get; set; }
        public List<KsfTableRow> Rows { get; } = new List<KsfTableRow>();
        public List<string> NotesAfter { get; } = new List<string>();
    }

    internal sealed class KsfTableRow
    {
        public string[] Cells { get; set; }
        public int[] ColSpans { get; set; }
    }

    /// <summary>STA4CAD *.KSF keşif dosyası: IBM857 metin + DOS kutu çizgileri.</summary>
    internal static class KsfParser
    {
        public const byte H = 0xC4;
        public const byte V = 0xA1;
        public const byte TL = 0xDA;
        public const byte TR = 0xBF;
        public const byte BL = 0xC0;
        public const byte BR = 0xD9;
        public const byte TJ = 0xC2;
        public const byte BJ = 0xC1;
        public const byte LJ = 0xC3;
        public const byte RJ = 0xB4;
        public const byte X = 0xC5;

        public static KsfDocument Parse(byte[] bytes, string fileName)
        {
            var doc = new KsfDocument { FileName = fileName ?? "" };
            if (bytes == null || bytes.Length == 0) return doc;
            var lines = SplitRawLines(bytes);
            int i = 0;
            var pendingTitle = new List<string>();
            while (i < lines.Count)
            {
                byte[] line = lines[i];
                if (IsEmpty(line))
                {
                    i++;
                    continue;
                }
                if (StartsWith(line, TL))
                {
                    var block = ParseTable(lines, ref i);
                    block.Title = JoinTitles(pendingTitle);
                    pendingTitle.Clear();
                    while (i < lines.Count)
                    {
                        if (IsEmpty(lines[i]))
                        {
                            i++;
                            continue;
                        }
                        if (StartsWith(lines[i], TL) || StartsWith(lines[i], V) || StartsWith(lines[i], LJ))
                            break;
                        int look = i + 1;
                        while (look < lines.Count && IsEmpty(lines[look])) look++;
                        if (look < lines.Count && StartsWith(lines[look], TL))
                            break;
                        string note = DecodeText(lines[i]).Trim();
                        if (note.Length > 0)
                            block.NotesAfter.Add(note);
                        i++;
                    }
                    doc.Tables.Add(block);
                    continue;
                }
                if (!StartsWith(line, V) && !StartsWith(line, LJ) && !StartsWith(line, BL))
                    pendingTitle.Add(DecodeText(line).Trim());
                i++;
            }
            return doc;
        }

        private static KsfTableBlock ParseTable(List<byte[]> lines, ref int i)
        {
            var block = new KsfTableBlock();
            byte[] top = lines[i];
            int[] colStops = JunctionStops(top);
            if (colStops.Length < 2)
                colStops = new[] { 0, Math.Max(1, top.Length - 1) };
            block.ColumnCount = colStops.Length - 1;
            block.ColumnCharWidths = new int[block.ColumnCount];
            for (int c = 0; c < block.ColumnCount; c++)
                block.ColumnCharWidths[c] = Math.Max(1, colStops[c + 1] - colStops[c] - 1);
            i++;
            var group = new List<byte[]>();
            while (i < lines.Count)
            {
                byte[] line = lines[i];
                if (StartsWith(line, BL))
                {
                    FlushContentGroup(block, group, colStops);
                    group.Clear();
                    i++;
                    break;
                }
                if (StartsWith(line, LJ) || StartsWith(line, TL))
                {
                    FlushContentGroup(block, group, colStops);
                    group.Clear();
                    if (StartsWith(line, TL))
                        break;
                    i++;
                    continue;
                }
                if (StartsWith(line, V))
                    group.Add(line);
                i++;
            }
            FlushContentGroup(block, group, colStops);
            return block;
        }

        private static void FlushContentGroup(KsfTableBlock block, List<byte[]> group, int[] colStops)
        {
            if (group == null || group.Count == 0) return;
            var parsed = new List<KsfTableRow>(group.Count);
            foreach (byte[] line in group)
                parsed.Add(ParseContentRow(line, colStops));
            KsfTableRow acc = parsed[0];
            for (int r = 1; r < parsed.Count; r++)
            {
                if (IsBlankCell(FirstSpannedCell(parsed[r])))
                {
                    MergeRowText(acc, parsed[r]);
                    continue;
                }
                block.Rows.Add(acc);
                acc = parsed[r];
            }
            block.Rows.Add(acc);
        }

        private static KsfTableRow ParseContentRow(byte[] line, int[] colStops)
        {
            int n = colStops.Length - 1;
            var cells = new string[n];
            var spans = new int[n];
            for (int c = 0; c < n; c++)
            {
                cells[c] = "";
                spans[c] = 0;
            }
            int col = 0;
            while (col < n)
            {
                int span = 1;
                while (col + span < n && !HasVerticalAt(line, colStops[col + span]))
                    span++;
                int from = colStops[col] + 1;
                int to = colStops[col + span] - 1;
                cells[col] = ExtractCell(line, from, to);
                spans[col] = span;
                for (int k = 1; k < span; k++)
                {
                    cells[col + k] = "";
                    spans[col + k] = 0;
                }
                col += span;
            }
            return new KsfTableRow { Cells = cells, ColSpans = spans };
        }

        private static bool HasVerticalAt(byte[] line, int index)
        {
            if (line == null || index < 0 || index >= line.Length) return false;
            byte b = line[index];
            return b == V || IsJunction(b);
        }

        private static string ExtractCell(byte[] line, int from, int to)
        {
            if (line == null || from > to) return "";
            int a = Math.Max(0, from);
            int b = Math.Min(line.Length - 1, to);
            if (b < a) return "";
            int len = b - a + 1;
            var slice = new byte[len];
            Buffer.BlockCopy(line, a, slice, 0, len);
            return DecodeText(slice).Trim();
        }

        private static void MergeRowText(KsfTableRow dest, KsfTableRow extra)
        {
            if (dest?.Cells == null || extra?.Cells == null) return;
            int n = Math.Min(dest.Cells.Length, extra.Cells.Length);
            for (int c = 0; c < n; c++)
            {
                string add = extra.Cells[c];
                if (string.IsNullOrEmpty(add)) continue;
                if (string.IsNullOrEmpty(dest.Cells[c]))
                    dest.Cells[c] = add;
                else
                    dest.Cells[c] = dest.Cells[c] + " " + add;
            }
        }

        private static string FirstSpannedCell(KsfTableRow row)
        {
            if (row?.Cells == null) return "";
            for (int i = 0; i < row.Cells.Length; i++)
            {
                if (row.ColSpans != null && i < row.ColSpans.Length && row.ColSpans[i] <= 0)
                    continue;
                return row.Cells[i] ?? "";
            }
            return "";
        }

        private static bool IsBlankCell(string s) => string.IsNullOrWhiteSpace(s);

        private static int[] JunctionStops(byte[] line)
        {
            var list = new List<int>();
            if (line == null) return Array.Empty<int>();
            for (int i = 0; i < line.Length; i++)
            {
                if (IsJunction(line[i]))
                    list.Add(i);
            }
            return list.ToArray();
        }

        public static bool IsJunction(byte b) =>
            b == TL || b == TR || b == BL || b == BR || b == TJ || b == BJ || b == LJ || b == RJ || b == X;

        private static bool StartsWith(byte[] line, byte b) =>
            line != null && line.Length > 0 && line[0] == b;

        private static bool IsEmpty(byte[] line) => line == null || line.Length == 0;

        private static List<byte[]> SplitRawLines(byte[] bytes)
        {
            var lines = new List<byte[]>();
            int i = 0;
            while (i < bytes.Length)
            {
                int start = i;
                while (i < bytes.Length && bytes[i] != 13 && bytes[i] != 10)
                    i++;
                int len = i - start;
                var row = new byte[len];
                if (len > 0)
                    Buffer.BlockCopy(bytes, start, row, 0, len);
                lines.Add(row);
                if (i < bytes.Length && bytes[i] == 13) i++;
                if (i < bytes.Length && bytes[i] == 10) i++;
            }
            return lines;
        }

        private static string JoinTitles(List<string> parts)
        {
            if (parts == null || parts.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (string p in parts)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(p.Trim());
            }
            return sb.ToString();
        }

        public static string DecodeText(byte[] slice)
        {
            if (slice == null || slice.Length == 0) return "";
            var chars = new char[slice.Length];
            for (int i = 0; i < slice.Length; i++)
                chars[i] = DecodeIbm857Byte(slice[i]);
            return new string(chars);
        }

        /// <summary>STA4CAD KSF: OEM Türkçe (IBM857). 0xFE=ş, 0xFD=ı, 0xDE=Ş, 0xDD=İ.</summary>
        private static char DecodeIbm857Byte(byte b)
        {
            if (b < 0x80) return (char)b;
            switch (b)
            {
                case 0x80: return '\u00C7';
                case 0x81: return '\u00FC';
                case 0x82: return '\u00E9';
                case 0x83: return '\u00E2';
                case 0x84: return '\u00E4';
                case 0x85: return '\u00E0';
                case 0x86: return '\u00E5';
                case 0x87: return '\u00E7';
                case 0x88: return '\u00EA';
                case 0x89: return '\u00EB';
                case 0x8A: return '\u00E8';
                case 0x8B: return '\u00EF';
                case 0x8C: return '\u00EE';
                case 0x8D: return '\u0131';
                case 0x8E: return '\u00C4';
                case 0x8F: return '\u00C5';
                case 0x90: return '\u00C9';
                case 0x91: return '\u00E6';
                case 0x92: return '\u00C6';
                case 0x93: return '\u00F4';
                case 0x94: return '\u00F6';
                case 0x95: return '\u00F2';
                case 0x96: return '\u00FB';
                case 0x97: return '\u00F9';
                case 0x98: return '\u0130';
                case 0x99: return '\u00D4';
                case 0x9A: return '\u00D2';
                case 0x9B: return '\u00FB';
                case 0x9C: return '\u00A3';
                case 0x9D: return '\u011E';
                case 0x9E: return '\u015E';
                case 0x9F: return '\u015F';
                case 0xA0: return '\u00E1';
                case 0xA5: return '\u00D1';
                case 0xA6: return '\u011E';
                case 0xA7: return '\u011F';
                case 0xB2: return '\u00B2';
                case 0xB3: return '\u00B3';
                case 0xC7: return '\u00C7';
                case 0xD0: return '\u011E';
                case 0xD6: return '\u00D6';
                case 0xD7: return '\u00D7';
                case 0xDC: return '\u00DC';
                case 0xDD: return '\u0130';
                case 0xDE: return '\u015E';
                case 0xE7: return '\u00E7';
                case 0xF0: return '\u011F';
                case 0xF6: return '\u00F6';
                case 0xF8: return '\u00F8';
                case 0xFC: return '\u00FC';
                case 0xFD: return '\u0131';
                case 0xFE: return '\u015F';
                default:
                    return WindowsAnsiEncodings.DecodeWindows1254Bytes(new[] { b })[0];
            }
        }
    }
}
