using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
// GPR deprem bilgisi (zemin, R, D, A sınıfı, DTS, BYS, Hn) ile ST4 başlık sütunlarını yan yana döker.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var enc = Encoding.GetEncoding(1254);
var files = Directory.EnumerateFiles(args[0], "*.*", SearchOption.AllDirectories)
    .Where(f => f.EndsWith(".st4", StringComparison.OrdinalIgnoreCase));
foreach (var st4 in files)
{
    string b = Path.Combine(Path.GetDirectoryName(st4), Path.GetFileNameWithoutExtension(st4));
    string gpr = File.Exists(b + ".GPR") ? b + ".GPR" : File.Exists(b + ".gpr") ? b + ".gpr" : null;
    if (gpr == null) continue;
    string firstA = "";
    string z = "", r = "", d = "", a = "", dts = "", bys = "", hn = "";
    try
    {
        int n = 0;
        foreach (var x in File.ReadLines(gpr, enc))
        {
            if (++n > 6000) break;
            Match m;
            if (firstA == "" && (m = Regex.Match(x, @"\bA\d+\b")).Success) firstA = m.Value;
            if (z == "" && (m = Regex.Match(x, @"SINIFI\.*\s*:\s*(Z[A-F])\b")).Success) z = m.Groups[1].Value;
            if (r == "" && (m = Regex.Match(x, @"KATSAYISI\s+R\s+:\s*([\d.,]+)")).Success) r = m.Groups[1].Value;
            if (d == "" && (m = Regex.Match(x, @"KATSAYISI\s+D\s+:\s*([\d.,]+)")).Success) d = m.Groups[1].Value;
            if (a == "" && x.Contains("KATSAYISI") && (m = Regex.Match(x, @"-\s*(A\d+)\s*$")).Success) a = m.Groups[1].Value;
            if (dts == "" && (m = Regex.Match(x, @"DTS\s+:\s*(\w+)")).Success) dts = m.Groups[1].Value;
            if (bys == "" && (m = Regex.Match(x, @"BYS\s+:\s*(\d+)\s+Hn=\s*([\d.]+)")).Success) { bys = m.Groups[1].Value; hn = m.Groups[2].Value; }
            if (dts != "" && bys != "" && z != "" && r != "" && d != "" && a != "") break;
        }
        var all = File.ReadLines(st4).Take(400).ToList();
        int si = all.FindIndex(q => q.Trim().Equals("/Story/", StringComparison.OrdinalIgnoreCase));
        var el = new System.Collections.Generic.List<string>();
        if (si >= 0) for (int k = si + 1; k + 2 < all.Count && !all[k].StartsWith("/"); k += 3) { var p = all[k + 2].Split(','); if (p.Length < 3) break; el.Add(all[k].Trim() + "=" + p[0]); }
        string story = string.Join(" ", el);
        string tp1 = File.Exists(b + ".TP1") ? File.ReadLines(b + ".TP1").FirstOrDefault() ?? "" : "-";
        var L = File.ReadLines(st4).Take(14).ToArray();
        if (L.Length < 14) continue;
        string c(int line, int col) { var p = L[line - 1].Split(','); return col <= p.Length ? p[col - 1] : "?"; }
        Console.WriteLine($"{z}|R{r}|D{d}|{a}|DTS{dts}|BYS{bys}|Hn{hn}|| L4c1={c(4,1)} L4c5={c(4,5)} L4c6={c(4,6)} L13={L[12]} L14c1={c(14,1)} L14c2={c(14,2)} L14c6={c(14,6)} L10c1={c(10,1)}| ST {story} | TP1 " + tp1 + " | RAW " + string.Join(";", all.Skip(3).Take(13)) + " |" + st4 + "| FIRSTA=" + firstA);
    }
    catch { }
}
