using System;
using System.Linq;
using ST4AksCizCSharp;
using ST4PlanIdCiz;

foreach (string st4 in args)
{
    Console.WriteLine("=== " + st4);
    var m = new St4Parser().Parse(st4);
    int Fl(int id) => m.BeamFloorKeyStep > 0 ? id / m.BeamFloorKeyStep : (id >= 1000 ? id / 1000 : id / 100);
    int No(int id) => m.BeamFloorKeyStep > 0 ? id % m.BeamFloorKeyStep : (id >= 1000 ? id % 1000 : id % 100);
    var walls = m.Beams.Where(b => b.IsWallFlag == 1).ToList();
    Console.WriteLine($"step={m.BeamFloorKeyStep} kapama={walls.Count} floors=" + string.Join(",", m.Floors.Select(f => f.FloorNo + ":" + f.ShortName)));
    string b0 = System.IO.Path.ChangeExtension(st4, null);
    var swxA = SwxKapamaDonati.ReadSirali(b0 + ".SWX"); var swx = swxA.Select(q => q ?? default).ToList();
    GprPerdePanelDonatiParser.TryParse(b0 + ".GPR", out var gpr, out _);
    for (int i = 0; i < Math.Max(walls.Count, swx.Count); i++)
    {
        string w = i < walls.Count ? $"{walls[i].BeamId,6} f{Fl(walls[i].BeamId)} n{No(walls[i].BeamId),3} b{walls[i].WidthCm}" : "-";
        string s = i < swx.Count ? $"2x{swx[i].BarCount}d{swx[i].DiaMm} y{swx[i].YatayDiaMm}/{swx[i].YataySpacingCm} As{swx[i].AsCm2:F2}" : "-";
        var ax = m.AxisX.Concat(m.AxisY).GroupBy(a => a.Id).ToDictionary(q => q.Key, q => q.First());
        double sp = 0; if (i < walls.Count && ax.TryGetValue(walls[i].StartAxisId, out var aa) && ax.TryGetValue(walls[i].EndAxisId, out var ab)) sp = Math.Abs(ab.ValueCm - aa.ValueCm);
        w += $" ax{sp:F0}";
        string g = "-";
        if (i < walls.Count && gpr != null)
        {
            int f = Fl(walls[i].BeamId), n = No(walls[i].BeamId);
            if (gpr.TryGetValue(GprPerdePanelDonatiParser.Key(f, n), out var d) || gpr.TryGetValue(GprPerdePanelDonatiParser.Key(0, n), out d))
                g = $"2x{d.BarCount}d{d.DiaMm} y{d.YatayDiaMm}/{d.YataySpacingCm} (Bx{d.BxCm} By{d.ByCm})";
        }
        double bxS = i < swx.Count && i < walls.Count ? swx[i].PanelBoyuCm(walls[i].WidthCm) : 0;
        string ok = "";
        if (i < walls.Count && gpr != null && (gpr.TryGetValue(GprPerdePanelDonatiParser.Key(Fl(walls[i].BeamId), No(walls[i].BeamId)), out var dd) || gpr.TryGetValue(GprPerdePanelDonatiParser.Key(0, No(walls[i].BeamId)), out dd))) { double L = Math.Max(dd.BxCm, dd.ByCm); ok = Math.Abs(L - bxS) < 0.5 ? "BXOK" : "BXFARK"; }
        Console.WriteLine($"{i,3} | {w,-26} | SWX {s,-28} Lswx{bxS} | GPR {g} {ok}");
    }
    if (gpr != null) Console.WriteLine("GPR keys: " + string.Join(" ", gpr.Keys.OrderBy(k => k)));
}
