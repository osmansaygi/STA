using System;
using System.IO;
using System.Linq;
using System.Text;
using ST4PlanIdCiz;

internal static class Program
{
    private static int Main(string[] args)
    {
        string gpr = args.Length > 0
            ? args[0]
            : @"tools\RadyeHeatmapInspect\sta-copy\N_ULK_02.GPR";
        string[] lines = File.ReadAllLines(gpr, Encoding.GetEncoding(1254));
        int cut = GprRadyeAyir.IlkRaporSatiri(lines);
        if (cut < 15000)
        {
            Console.WriteLine("Radye kesimi cok erken: " + cut);
            return 1;
        }
        if (!GprRadyeAyir.TryReadMesh(lines, out var nodes, out string error))
        {
            Console.WriteLine(error);
            return 1;
        }
        var n93 = nodes.FirstOrDefault(n => n.No == 93);
        var n75 = nodes.FirstOrDefault(n => n.No == 75);
        if (n93 == null || n75 == null || nodes.Count < 700)
        {
            Console.WriteLine("Eksik dugum: " + nodes.Count);
            return 1;
        }
        if (Math.Abs(n93.AsXAlt - 4.25) > 0.001 || Math.Abs(n93.AsXUst) > 0.001
            || Math.Abs(n93.AsYAlt - 3.30) > 0.001 || Math.Abs(n93.AsYUst - 3.30) > 0.001)
        {
            Console.WriteLine("Nokta 93 As uyusmadi");
            return 1;
        }
        if (Math.Abs(n75.AsXAlt - 3.30) > 0.001 || Math.Abs(n75.AsXUst) > 0.001)
        {
            Console.WriteLine("Nokta 75 As uyusmadi");
            return 1;
        }
        string head = GprRadyeAyir.YapidanOnce(string.Join("\n", lines));
        if (head.IndexOf("Asax", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Console.WriteLine("Yapi metninde mesh basligi kaldi");
            return 1;
        }
        if (!GprRadyeAyir.TryReadGenel(lines, out var genel, out string genelHata))
        {
            Console.WriteLine(genelHata);
            return 1;
        }
        if (genel.CapXAltMm != 14 || genel.AralikXAltCm != 20 || genel.CapXUstMm != 12 || genel.CapYAltMm != 14 || genel.CapYUstMm != 12)
        {
            Console.WriteLine("Genel donati cap/aralik uyusmadi");
            return 1;
        }
        int altX = 0, altY = 0, ustX = 0, ustY = 0;
        foreach (var node in nodes)
        {
            if (node.AsXAlt > genel.AsXAlt + 0.005) altX++;
            if (node.AsYAlt > genel.AsYAlt + 0.005) altY++;
            if (node.AsXUst > genel.AsXUst + 0.005) ustX++;
            if (node.AsYUst > genel.AsYUst + 0.005) ustY++;
        }
        string rad = Path.ChangeExtension(gpr, ".RAD");
        if (!RadyeEsikPoligon.TryRadElemanlari(rad, nodes, out var eleman, out string radHata))
        {
            Console.WriteLine(radHata);
            return 1;
        }
        var yuzler = new[]
        {
            new { Ad = "ALT X", Alan = RadyeEsikPoligon.Hesapla(nodes, eleman, n => n.AsXAlt, genel.AsXAlt) },
            new { Ad = "ALT Y", Alan = RadyeEsikPoligon.Hesapla(nodes, eleman, n => n.AsYAlt, genel.AsYAlt) },
            new { Ad = "UST X", Alan = RadyeEsikPoligon.Hesapla(nodes, eleman, n => n.AsXUst, genel.AsXUst) },
            new { Ad = "UST Y", Alan = RadyeEsikPoligon.Hesapla(nodes, eleman, n => n.AsYUst, genel.AsYUst) }
        };
        if (yuzler[0].Alan.Count != 3 || yuzler[2].Alan.Count != 0)
        {
            Console.WriteLine("Alan sayisi ALT X=" + yuzler[0].Alan.Count + " UST X=" + yuzler[2].Alan.Count);
            return 1;
        }
        Console.WriteLine("PASS dugum=" + nodes.Count + " kesim=" + (cut + 1)
            + " n93 Xalt=" + n93.AsXAlt.ToString("0.00")
            + " ilave " + altX + "/" + altY + "/" + ustX + "/" + ustY
            + " alan " + yuzler[0].Alan.Count + "/" + yuzler[1].Alan.Count + "/" + yuzler[2].Alan.Count + "/" + yuzler[3].Alan.Count);
        foreach (var yuz in yuzler)
        {
            foreach (var poly in yuz.Alan)
                Console.WriteLine("  " + yuz.Ad + " m2=" + poly.Area.ToString("0.00")
                    + " c=" + poly.Centroid.X.ToString("0.00") + "," + poly.Centroid.Y.ToString("0.00"));
        }
        var ofset = RadyeEsikPoligon.Hesapla(nodes, eleman, n => n.AsXAlt, genel.AsXAlt, 0, 0.40);
        if (ofset.Count != 3)
        {
            Console.WriteLine("Ofset 40 cm alan sayisi " + ofset.Count);
            return 1;
        }
        double ofsetM2 = 0;
        foreach (var poly in ofset) ofsetM2 += poly.Area;
        double ekstra = nodes.Max(n => n.AsXAlt) - genel.AsXAlt;
        Console.WriteLine("OFSET40 n=" + ofset.Count + " m2=" + ofsetM2.ToString("0.00")
            + " ekstraMax=" + ekstra.ToString("0.00")
            + " 12/30=" + GprRadyeAyir.AlanCm2Pm(12, 30).ToString("0.00")
            + " 14/10=" + GprRadyeAyir.AlanCm2Pm(14, 10).ToString("0.00"));
        return 0;
    }
}
