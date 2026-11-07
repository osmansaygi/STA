using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StatikRaporClaude.Uretec;

public sealed class Proj
{
    public static readonly CultureInfo ci = CultureInfo.InvariantCulture;
    public string Dir, Base, GprPath;
    public string[] St4, Ops, Use;
    public Proj(string gpr)
    {
        GprPath = gpr; Dir = Path.GetDirectoryName(gpr); Base = Path.GetFileNameWithoutExtension(gpr);
        St4 = Lines(".ST4"); Ops = Lines(".OPS"); Use = Lines(".USE");
    }
    public string FilePath(string ext)
    {
        var p = Path.Combine(Dir, Base + ext);
        if (File.Exists(p)) return p;
        var m = Directory.GetFiles(Dir, Base + ".*").FirstOrDefault(f => string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetFileNameWithoutExtension(f), Base, StringComparison.OrdinalIgnoreCase));
        return m;
    }
    public string[] Lines(string ext) { var p = FilePath(ext); return p == null ? null : PaylasimliDosya.Lines(p, GprKod.Enc); }
    public byte[] Bytes(string ext) { var p = FilePath(ext); return p == null ? null : PaylasimliDosya.Bytes(p); }
    public static string[] F(string line) => line.Split(',');
    public static double D(string s) => double.TryParse(s.Trim(), NumberStyles.Float, ci, out var d) ? d : 0;
    public double St4D(int line1, int field1) { var f = F(St4[line1 - 1]); return field1 <= f.Length ? D(f[field1 - 1]) : 0; }
    public double OpsD(int line1, int field1) { if (Ops == null || line1 > Ops.Length) return double.NaN; var f = F(Ops[line1 - 1]); return field1 <= f.Length ? D(f[field1 - 1]) : double.NaN; }
    public int Sec(string name) => Array.FindIndex(St4, l => l.StartsWith(name));

}

public static class Fmt
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;
    /// STA4: float32 -> G7 -> yarım yukarı
    public static string N(double v, int dec)
    {
        float f = (float)v;
        decimal d = decimal.Parse(f.ToString("G7", ci), NumberStyles.Float, ci);
        d = Math.Round(d, dec, MidpointRounding.AwayFromZero);
        if (d == 0) d = 0m;
        return d.ToString("F" + dec, ci);
    }
    public static string ND(double v, int dec)
    {
        decimal d = (decimal)v;
        d = Math.Round(d, dec, MidpointRounding.AwayFromZero);
        if (d == 0) d = 0m;
        return d.ToString("F" + dec, ci);
    }
}

