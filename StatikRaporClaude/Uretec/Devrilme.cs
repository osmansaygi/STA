using System.Globalization;

namespace StatikRaporClaude.Uretec;
public static class Devrilme
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;
    const char V = '¡';
    static string N(double v, int d) => Fmt.ND(v, d);

    /// FeaSeismic: 1. satır ?, 2. satır (K, Tx, Ty, R, Wx, Wy, Vtx, Vty, Ftx, Fty), 3. satır toplamlar,
    /// K satır kat kuvvetleri (modal X, modal Y, eşdeğer X, eşdeğer Y, deprem X, deprem Y; 1. kattan yukarı), K satır (Wg, Wq).
    public static (int K, double[][] F, double[][] W) Sismik(Proj p)
    {
        var l = p.Lines(".FeaSeismic"); int K = (int)Proj.D(l[1].Split(',')[0]);
        var F = Enumerable.Range(0, K).Select(i => l[3 + i].Split(',').Select(Proj.D).ToArray()).ToArray();
        var W = Enumerable.Range(0, K).Select(i => l[3 + K + i].Split(',').Select(Proj.D).ToArray()).ToArray();
        return (K, F, W);
    }

    public static List<string> KatMomenti(Proj p, out double sFx, out double sFy, out double sMx, out double sMy)
    {
        var (K, F, _) = Sismik(p);
        var kat = Sayfa7.Katlar(p).Skip(1).ToList();
        double hbod = Sayfa7.SD(p, 10, 6);   // Hbodrum = ST4 10. satır 6. alan
        double hb = -hbod;
        var o = new List<string> { "    Kat deprem momenti (tm)" };
        string[] w = { new string('Ä', 6), new string('Ä', 8), new string('Ä', 11), new string('Ä', 13), new string('Ä', 8), new string('Ä', 11), new string('Ä', 13) };
        o.Add("   Ú" + string.Join("Â", w) + "¿");
        o.Add("   ¡  Kat ¡  H (m) ¡     Fx    ¡     Fx . H  ¡  H (m) ¡     Fy    ¡     Fy . H  ¡");
        o.Add("   Ã" + string.Join("Å", w) + "´");
        sFx = sFy = sMx = sMy = 0;
        for (int i = K - 1; i >= 0; i--)
        {
            double h = kat[i].Kot + hb;
            if (kat[i].Kot <= hbod + 0.0001 && hbod > 0)
            {
                o.Add("   " + V + (i + 1).ToString().PadLeft(5) + " " + V + " BODRUM " + V + "      -    " + V + "       -     " + V + " BODRUM " + V + "      -    " + V + "       -     " + V);
                continue;
            }
            double fx = F[i][4], fy = F[i][5];
            sFx += fx; sFy += fy; sMx += fx * h; sMy += fy * h;
            o.Add("   " + V + (i + 1).ToString().PadLeft(5) + " " + V + N(h, 2).PadLeft(7) + " " + V + N(fx, 2).PadLeft(10) + " " + V + N(fx * h, 2).PadLeft(12) + " " + V
                + N(h, 2).PadLeft(7) + " " + V + N(fy, 2).PadLeft(10) + " " + V + N(fy * h, 2).PadLeft(12) + " " + V);
        }
        o.Add("   À" + string.Join("Á", w) + "Ù");
        o.Add(N(sFx, 2).PadLeft(30) + N(sMx, 2).PadLeft(14) + N(sFy, 2).PadLeft(21) + N(sMy, 2).PadLeft(14));
        return o;
    }

    /// RAD: 2. satır plak sayısı; her plak 2 satır: "no,?,?,t,..." ve köşe koordinatları (x,y,z)…
    public static List<(double A, double X, double Y, double T, double MinX, double MinY)> Plaklar(Proj p)
    {
        var r = p.Lines(".RAD"); var o = new List<(double, double, double, double, double, double)>();
        if (r == null) return o;
        int n = (int)Proj.D(r[1].Split(',')[0]);
        int i = Array.FindIndex(r, l => l.Contains(".ST4")) + 1;
        for (int k = 0; k < n && i + 1 < r.Length; k++, i += 2)
        {
            double t = Proj.D(r[i].Split(',')[3]);
            var c = r[i + 1].Split(',').Select(Proj.D).ToArray();
            int m = c.Length / 3; double a = 0, cx = 0, cy = 0;
            for (int j = 0; j < m; j++)
            {
                double x0 = c[3 * j], y0 = c[3 * j + 1], x1 = c[3 * ((j + 1) % m)], y1 = c[3 * ((j + 1) % m) + 1];
                double cr = x0 * y1 - x1 * y0; a += cr; cx += (x0 + x1) * cr; cy += (y0 + y1) * cr;
            }
            a /= 2; cx /= 6 * a; cy /= 6 * a;
            o.Add((Math.Abs(a), cx, cy, t, Enumerable.Range(0, m).Min(j => c[3 * j]), Enumerable.Range(0, m).Min(j => c[3 * j + 1])));
        }
        return o;
    }

    public static List<string> Uret(Proj p)
    {
        var o = new List<string> { "    DEPREM YAPI DEVRILME KONTROLU" };
        o.AddRange(KatMomenti(p, out _, out _, out double sMx, out double sMy));
        var (K, _, W) = Sismik(p);
        double n = Sayfa7.SD(p, 4, 9);
        var ear = p.Lines(".EAR"); int ci0 = 1 + K + 1 + 2 * K + K;
        var pl = Plaklar(p);
        double gam = Proj.D(p.St4[p.Sec("/Materials/") + 1].Split(',')[3]);
        double X0 = pl.Count > 0 ? pl.Min(q => q.MinX) : 0, Y0 = pl.Count > 0 ? pl.Min(q => q.MinY) : 0;
        o.Add("    Kat düşey yük momenti (tm)       X=" + X0.ToString("0.###", ci) + "m     Y=" + Y0.ToString("0.###", ci) + "m   moment noktası");
        string[] w = { new string('Ä', 6), new string('Ä', 12), new string('Ä', 8), new string('Ä', 18), new string('Ä', 8), new string('Ä', 18) };
        o.Add("   Ú" + string.Join("Â", w) + "¿");
        o.Add("   ¡ Kat  ¡  Wg+0.3·Wq ¡  Xg-X  ¡(Xg-X)·(Wg+0.3·Wq)¡  Yg-Y  ¡(Yg-Y)·(Wg+0.3·Wq)¡");
        o.Add("   Ã" + string.Join("Å", w) + "´");
        double tx = 0, ty = 0;
        void R(string k, double wk, double dx, double dy)
        {
            tx += wk * dx; ty += wk * dy;
            o.Add("   " + V + k.PadLeft(5) + " " + V + N(wk, 2).PadLeft(11) + " " + V + N(dx, 3).PadLeft(7) + " " + V + N(wk * dx, 2).PadLeft(17) + " " + V + N(dy, 3).PadLeft(7) + " " + V + N(wk * dy, 2).PadLeft(17) + " " + V);
        }
        for (int i = K - 1; i >= 0; i--)
        {
            var c = ear[ci0 + i].Split(',').Select(Proj.D).ToArray();
            R((i + 1).ToString(), W[i][0] + n * W[i][1], c[0] - X0, c[2] - Y0);
        }
        if (pl.Count > 0)
        {
            double a = pl.Sum(q => q.A * q.T * gam), cx = pl.Sum(q => q.A * q.T * gam * q.X) / a, cy = pl.Sum(q => q.A * q.T * gam * q.Y) / a;
            R("", a, cx - X0, cy - Y0);
        }
        o.Add("   À" + string.Join("Á", w) + "Ù");
        o.Add(N(tx, 2).PadLeft(50) + N(ty, 2).PadLeft(28));
        string G(double v) => ((float)v).ToString("G7", ci);
        o.Add($"   X yönü devrilme kontrolu={G(tx)}/{N(sMx, 3)}={N(tx / sMx, 3)} > 1.5  ò");
        o.Add($"   Y yönü devrilme kontrolu={G(ty)}/{N(sMy, 3)}={N(ty / sMy, 3)} > 1.5  ò");
        return o;
    }
}
