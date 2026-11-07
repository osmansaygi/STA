using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StatikRaporClaude.Uretec;
public static class Nokta
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;
    public static readonly string[] Etiket = { "1.(G+G+G+G)", "2.(Q+Q+Q+Q)", "3.(o+Q+o+Q)", "4.(Q+o+Q+o)", "5.(Q+Q+o+Q)", "6.(o+Q+Q+o)", "7.(Q+o+Q+Q)",
        "X-Deprem+%5", "X-Deprem-%5", "Y-Deprem+%5", "Y-Deprem-%5", "X-Ruzgar", "Y-Ruzgar" };
    public static readonly int[] Vaka = { 0, 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13 };

    public static (int N, int K, float[] v) Dis(Proj p)
    {
        var ear = p.Lines(".EAR"); var f = ear[0].Split(',');
        int k = (int)Proj.D(f[0]), n = (int)Proj.D(f[1]);
        var b = p.Bytes(".DIS"); var v = new float[b.Length / 4];
        Buffer.BlockCopy(b, 0, v, 0, v.Length * 4);
        return (n, k, v);
    }
    public static string F8(float x) { var s = Fmt.N(x, 8); return s; }
}
