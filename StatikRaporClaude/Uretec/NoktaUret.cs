using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StatikRaporClaude.Uretec;
public static class NoktaUret
{
    static readonly System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
    /// SEE "//NOKTA KOORDINATLARI//" satırları: no -> (X,Y,Z) fiziksel
    public static Dictionary<int, (double X, double Y, double Z)> See(Proj p)
    {
        var d = new Dictionary<int, (double, double, double)>(); bool on = false;
        foreach (var l in p.Lines(".SEE") ?? new string[0])
        {
            if (l.StartsWith("//")) { on = l.Contains("NOKTA KOORD"); continue; }
            if (!on) continue;
            var f = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length == 4 && int.TryParse(f[0], out int n)) d[n] = (Proj.D(f[1]), Proj.D(f[2]), Proj.D(f[3]));
        }
        return d;
    }
    public static (double[] X, double[] Y) Akslar(Proj p)
    {
        int nx = (int)Sayfa7.SD(p, 5, 1), ny = (int)Sayfa7.SD(p, 5, 2), a = p.Sec("/Axis data/");
        var X = Enumerable.Range(0, nx).Select(i => Proj.D(p.St4[a + 1 + i].Split(',')[1])).ToArray();
        var Y = Enumerable.Range(0, ny).Select(i => Proj.D(p.St4[a + 1 + nx + i].Split(',')[1])).ToArray();
        return (X, Y);
    }
    /// düğüm -> (kat 1..K, aks X, aks Y); temel düğümleri listede yok
    public static Dictionary<int, (int Kat, double X, double Y)> Konum(Proj p, int K)
    {
        var (AX, AY) = Akslar(p);
        var katlar = Sayfa7.Katlar(p).Skip(1).ToList();   // su basman hariç
        int KatOf(double z) { for (int i = 0; i < katlar.Count; i++) if (Math.Abs(katlar[i].Kot - z) < 0.006) return i + 1; return -1; }
        // kolon eksen kesişimleri
        int ca = p.Sec("/Column axis data/");
        var pos = new List<double[]>(); for (int i = ca + 1; i < p.St4.Length && !p.St4[i].StartsWith("/"); i++) pos.Add(p.St4[i].Split(',').Select(Proj.D).ToArray());
        (double, double) Kes(double a1, double a2)
        {
            double x = double.NaN, y = double.NaN;
            foreach (var a in new[] { a1, a2 }) { int ai = (int)a; if (ai >= 1000 && ai < 2000) x = AX[ai - 1001]; else if (ai >= 2000 && ai < 3000) y = AY[ai - 2001]; }
            return (x, y);
        }
        var see = See(p);
        var res = new Dictionary<int, (int, double, double)>();
        int cd = p.Sec("/Columns Data/"); var nos = new List<int>();
        for (int i = cd + 1; i < p.St4.Length && !p.St4[i].StartsWith("/"); i += 3) nos.Add((int)Proj.D(p.St4[i].Split(',')[0]));
        int taban = nos.DefaultIfEmpty(0).Max() / 100 >= Sayfa7.Katlar(p).Count ? 1000 : 100;
        var col = p.Bytes(".COL");
        for (int r = 0; r < nos.Count && (r + 1) * 900 <= col.Length; r++)
        {
            int top = BitConverter.ToInt16(col, r * 900);
            int ps = nos[r] % taban; if (ps < 1 || ps > pos.Count) continue;
            var (x, y) = Kes(pos[ps - 1][1], pos[ps - 1][2]);
            int kat = see.TryGetValue(top, out var s) ? KatOf(s.Z) : nos[r] / taban;
            if (top > 0 && !double.IsNaN(x) && !double.IsNaN(y)) res[top] = (kat, x, y);
        }
        // kiriş uçları (kolonsuz düğümler): ST4 kiriş 1. satır: no,b,h,?,boyunca aks, aks1, aks2 ; 2. satır 5./6. alan uç kolon no (0 = kolonsuz)
        int bd = p.Sec("/Beams Data/");
        var aday = new List<(int Kat, double X, double Y)>();
        for (int i = bd + 1; i + 1 < p.St4.Length && !p.St4[i].StartsWith("/"); i += 3)
        {
            var f = p.St4[i].Split(',').Select(Proj.D).ToArray(); var g = p.St4[i + 1].Split(',').Select(Proj.D).ToArray();
            int kat = (int)f[0] / taban;
            if (g.Length > 5 && g[4] == 0) { var (x, y) = Kes(f[4], f[5]); if (!double.IsNaN(x) && !double.IsNaN(y)) aday.Add((kat, x, y)); }
            if (g.Length > 5 && g[5] == 0) { var (x, y) = Kes(f[4], f[6]); if (!double.IsNaN(x) && !double.IsNaN(y)) aday.Add((kat, x, y)); }
        }
        foreach (var (n, s) in see)
        {
            if (res.ContainsKey(n) || s.Z <= 0.0001) continue;
            int kat = KatOf(s.Z); if (kat < 1) continue;
            var c = aday.Where(a => a.Kat == kat).OrderBy(a => Math.Abs(a.X - s.X) + Math.Abs(a.Y - s.Y)).FirstOrDefault();
            if (c.Kat == kat) res[n] = (kat, c.X, c.Y);
        }
        return res;
    }

    public static List<string> Uret(Proj p)
    {
        var (N, K, v) = Nokta.Dis(p);
        int S = N + K, blok = S * 3;
        var ear = p.Lines(".EAR");
        int ci0 = 1 + K + 1 + 2 * K + K;   // merkez satırları başlangıcı (0 tabanlı)
        var merk = Enumerable.Range(0, K).Select(k => ear[ci0 + k].Split(',').Select(Proj.D).ToArray()).ToArray();
        var kon = Konum(p, K);
        string sep = "ÃÄÄÄÄÄÅ" + new string('Ä', 16) + string.Concat(Enumerable.Repeat("Å" + new string('Ä', 12), 5)) + "´";
        var o = new List<string>
        {
            "  NOKTA DEPLASMANLARI",
            sep.Replace('Ã', 'Ú').Replace('Å', 'Â').Replace('´', '¿'),
            "¡Nokta¡    Kombinasyon ¡      ëx    ¡      ëy    ¡      ëz    ¡      éx    ¡      éy    ¡",
            "¡  no ¡                ¡      mm    ¡      mm    ¡      mm    ¡1000x radyan¡1000x radyan¡",
            sep,
        };
        string F(double x) { if (Environment.GetEnvironmentVariable("NF") == "d") { var s = decimal.Parse(x.ToString("G7", ci), System.Globalization.NumberStyles.Float, ci); s = Math.Round(s, 8, MidpointRounding.AwayFromZero); return s.ToString("F8", ci).PadLeft(12); } return Fmt.N((float)x, 8).PadLeft(12); }
        for (int n = 1; n <= N; n++)
        {
            for (int c = 0; c < Nokta.Etiket.Length; c++)
            {
                int o0 = Nokta.Vaka[c] * blok + (n - 1) * 3;
                double ex = 0, ey = 0;
                if (kon.TryGetValue(n, out var q) && q.Kat >= 1 && q.Kat <= K)
                {
                    int od = Nokta.Vaka[c] * blok + N * 3, k = q.Kat - 1;
                    double dx = v[od + k], dy = -v[od + K + k], rz = v[od + 2 * K + k];
                    double xr = merk[k][1], yr = merk[k][3];
                    float fdx = (float)dx, fdy = (float)dy, frz = (float)rz, fxr = (float)xr, fyr = (float)yr, fX = (float)q.X, fY = (float)q.Y;
                    var vv = Environment.GetEnvironmentVariable("NV") ?? "4"; float tx, ty;
                    if (vv == "2") { tx = fdx * 1000f + frz * 1000f * (fY - fyr); ty = fdy * 1000f - frz * 1000f * (fX - fxr); ex = tx; ey = ty; }
                    else if (vv == "3") { tx = fdx + (frz * fY - frz * fyr); ty = fdy - (frz * fX - frz * fxr); ex = tx * 1000f; ey = ty * 1000f; }
                    else if (vv == "7") { ex = (dx + rz * (q.Y - yr)) * 1000; ey = (dy - rz * (q.X - xr)) * 1000; }
                    else if (vv == "8") { ex = (dx + rz * ((float)q.Y - yr)) * 1000; ey = (dy - rz * ((float)q.X - xr)) * 1000; }
                    else if (vv == "9") { double t1 = dx + rz * ((double)(float)q.Y - (float)yr), t2 = dy - rz * ((double)(float)q.X - (float)xr); ex = (float)t1 * 1000f; ey = (float)t2 * 1000f; }
                    else if (vv == "10") { double t1 = dx + rz * ((double)(float)q.Y - yr), t2 = dy - rz * ((double)(float)q.X - xr); ex = (float)(t1 * 1000); ey = (float)(t2 * 1000); }
                    else if (vv == "11") { double t1 = dx*1000 + rz*1000 * (q.Y - yr), t2 = dy*1000 - rz*1000 * (q.X - xr); ex = (float)t1; ey = (float)t2; }
                    else if (vv == "4") { double t1 = dx + rz * (q.Y - yr), t2 = dy - rz * (q.X - xr); ex = (float)t1 * 1000f; ey = (float)t2 * 1000f; }
                    else if (vv == "5") { tx = (fdx + frz * (fY - fyr)) * 1000f; ty = (fdy + frz * (fxr - fX)) * 1000f; ex = tx; ey = ty; }
                    else if (vv == "6") { tx = fdx - frz * (fyr - fY); ty = fdy + frz * (fxr - fX); ex = tx * 1000f; ey = ty * 1000f; }
                    else { tx = fdx + frz * (fY - fyr); ty = fdy - frz * (fX - fxr); ex = (float)(tx * 1000f); ey = (float)(ty * 1000f); }
                }
                o.Add("¡" + n.ToString().PadLeft(4) + " ¡ " + Nokta.Etiket[c].PadRight(15) + "¡" + F(ex) + "¡" + F(ey) + "¡" + F(v[o0 + 2] * 1000.0) + "¡" + F(v[o0 + 1] * 1000.0) + "¡" + F(v[o0] * 1000.0) + "¡");
            }
            if (n < N) o.Add(sep);
        }
        o.Add(sep.Replace('Ã', 'À').Replace('Å', 'Á').Replace('´', 'Ù'));
        return o;
    }
}
