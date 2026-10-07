using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// STA4CAD .SMX: kolon/perde donatı kaydı (300 bayt).
    /// Ofset 161'den 9 adet little-endian int16: etriye çap, aralık, üçüncü aralık,
    /// bayrak, yüz adedi, yüzde çubuk, çap, gövde adedi, gövde çapı.
    /// </summary>
    public static class SmxKolonDonati
    {
        public const int RecordBytes = 300;
        public const int RebarOffset = 161;

        public struct Rec
        {
            public int StirrupDia;
            public int Spacing;
            public int Spacing2;
            public int Faces;
            public int Bars;
            public int BarDia;
            public int WebBars;
            public int WebDia;
        }

        public static string ResolvePath(string st4SourcePath)
        {
            if (string.IsNullOrEmpty(st4SourcePath)) return null;
            string dir = Path.GetDirectoryName(st4SourcePath);
            if (string.IsNullOrEmpty(dir)) return null;
            string baseName = Path.GetFileNameWithoutExtension(st4SourcePath);
            string p1 = Path.Combine(dir, baseName + ".SMX");
            if (File.Exists(p1)) return p1;
            string p2 = Path.Combine(dir, baseName + ".smx");
            if (File.Exists(p2)) return p2;
            return null;
        }

        public static List<Rec> Read(string path)
        {
            var list = new List<Rec>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return list;
            byte[] b = File.ReadAllBytes(path);
            int n = b.Length / RecordBytes;
            for (int r = 0; r < n; r++)
            {
                int o = r * RecordBytes + RebarOffset;
                if (o + 18 > b.Length) break;
                var rec = new Rec
                {
                    StirrupDia = I16(b, o),
                    Spacing = I16(b, o + 2),
                    Spacing2 = I16(b, o + 4),
                    Faces = I16(b, o + 8),
                    Bars = I16(b, o + 10),
                    BarDia = I16(b, o + 12),
                    WebBars = I16(b, o + 14),
                    WebDia = I16(b, o + 16)
                };
                list.Add(rec);
            }
            return list;
        }

        /// <summary>GPR ile aynı sözdizimi: 2x3ø14+2x2ø14(govde). Perdede yüz adedi 0 ise 9ø14+7ø12.</summary>
        public static string FormatDonati(Rec r)
        {
            if (r.Bars <= 0 || r.BarDia < 6) return null;
            if (r.Faces > 0)
            {
                string s = r.Faces.ToString(CultureInfo.InvariantCulture) + "x"
                    + r.Bars.ToString(CultureInfo.InvariantCulture) + "\u00F8"
                    + r.BarDia.ToString(CultureInfo.InvariantCulture);
                if (r.WebBars > 0 && r.WebDia >= 6)
                    s += "+" + r.Faces.ToString(CultureInfo.InvariantCulture) + "x"
                        + r.WebBars.ToString(CultureInfo.InvariantCulture) + "\u00F8"
                        + r.WebDia.ToString(CultureInfo.InvariantCulture) + "(govde)";
                return s;
            }
            string p = r.Bars.ToString(CultureInfo.InvariantCulture) + "\u00F8"
                + r.BarDia.ToString(CultureInfo.InvariantCulture);
            if (r.WebBars > 0 && r.WebDia >= 6)
                p += "+" + r.WebBars.ToString(CultureInfo.InvariantCulture) + "\u00F8"
                    + r.WebDia.ToString(CultureInfo.InvariantCulture);
            return p;
        }

        /// <summary>2ø10/15/8. Aralıklar kolon sarılma ayrıştırmasının okuduğu /s biçiminde.</summary>
        public static string FormatEtriye(Rec r)
        {
            if (r.StirrupDia < 6) return null;
            int legs = r.Faces >= 2 ? r.Faces : 2;
            string s = (r.Faces > 0 ? legs.ToString(CultureInfo.InvariantCulture) : "")
                + "\u00F8" + r.StirrupDia.ToString(CultureInfo.InvariantCulture);
            if (r.Spacing >= 4)
                s += "/" + r.Spacing.ToString(CultureInfo.InvariantCulture);
            if (r.Spacing2 >= 4 && r.Spacing2 != r.Spacing)
                s += "/" + r.Spacing2.ToString(CultureInfo.InvariantCulture);
            return s;
        }

        private static int I16(byte[] b, int o)
        {
            return BitConverter.ToInt16(b, o);
        }
    }
}
