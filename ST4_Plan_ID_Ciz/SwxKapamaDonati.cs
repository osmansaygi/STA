using System;
using System.Collections.Generic;
using System.IO;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// STA4CAD .SWX: kapama perde (panel) donatısı. 300 bayt kayıt.
    /// Ofset 161: yatay çap, yatay aralık (cm). Ofset 175: düşey adet (2×NøD içindeki N), düşey çap.
    /// </summary>
    public static class SwxKapamaDonati
    {
        public const int RecordBytes = 300;
        public const int YatayOffset = 161;
        public const int DuseyOffset = 175;

        public struct Rec
        {
            public int YatayDiaMm;
            public int YataySpacingCm;
            public int BarCount;
            public int DiaMm;
            public double AsCm2;
        }

        public static string ResolvePath(string st4SourcePath)
        {
            if (string.IsNullOrEmpty(st4SourcePath)) return null;
            string dir = Path.GetDirectoryName(st4SourcePath);
            if (string.IsNullOrEmpty(dir)) return null;
            string baseName = Path.GetFileNameWithoutExtension(st4SourcePath);
            string p1 = Path.Combine(dir, baseName + ".SWX");
            if (File.Exists(p1)) return p1;
            string p2 = Path.Combine(dir, baseName + ".swx");
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
                int o = r * RecordBytes;
                if (o + DuseyOffset + 4 > b.Length) break;
                int yatayDia = BitConverter.ToInt16(b, o + YatayOffset);
                int yatayAralik = BitConverter.ToInt16(b, o + YatayOffset + 2);
                int adet = BitConverter.ToInt16(b, o + DuseyOffset);
                int cap = BitConverter.ToInt16(b, o + DuseyOffset + 2);
                float asCm = 0f;
                if (o + 88 <= b.Length)
                    asCm = BitConverter.ToSingle(b, o + 84);
                if (adet < 2 || cap < 8 || cap > 40) continue;
                if (yatayDia < 6 || yatayDia > 32) yatayDia = 0;
                if (yatayAralik < 4 || yatayAralik > 40) yatayAralik = 0;
                list.Add(new Rec
                {
                    YatayDiaMm = yatayDia,
                    YataySpacingCm = yatayAralik,
                    BarCount = adet,
                    DiaMm = cap,
                    AsCm2 = asCm
                });
            }
            return list;
        }
    }
}
