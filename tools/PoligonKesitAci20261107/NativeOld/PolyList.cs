using System;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.Runtime;
using ST4AksCizCSharp;

public class PolyListCmd
{
    [CommandMethod("KD_POLYLIST")]
    public void Run()
    {
        string res = Environment.GetEnvironmentVariable("KD_RESULT");
        var sb = new StringBuilder();
        foreach (var path in File.ReadAllLines(Environment.GetEnvironmentVariable("KD_LIST")))
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            try
            {
                var m = new St4Parser().Parse(path.Trim());
                var polys = m.Columns.Where(c => c.ColumnType == 3).ToList();
                if (polys.Count == 0) continue;
                sb.AppendLine(path + " | " + string.Join(" ", polys.Select(c => c.ColumnNo + "@" + c.AngleDeg.ToString("0.#"))));
            }
            catch (System.Exception ex) { sb.AppendLine(path + " | FAIL " + ex.Message); }
            File.WriteAllText(res, sb.ToString());
        }
        sb.AppendLine("COMPLETE");
        File.WriteAllText(res, sb.ToString());
    }
}
