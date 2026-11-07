using System.Globalization;

namespace StatikRaporClaude.Uretec;
public static class Aks
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;
    const char V = '¡';
    static string N(double v, int d) => Fmt.N(v, d);

    public static List<string> Uret(Proj p)
    {
        var o = new List<string>();
        int nx = (int)Sayfa7.SD(p, 5, 1), ny = (int)Sayfa7.SD(p, 5, 2);
        int a = p.Sec("/Axis data/");
        var ax = Enumerable.Range(0, nx + ny).Select(i => p.St4[a + 1 + i].Split(',').Select(Proj.D).ToArray()).ToList();
        var names = p.Lines(".AKS") ?? new string[0];
        string Ad(int i) => i < names.Length ? names[i].Trim() : "";
        o.Add(" YAPI AKS BİLGİLERİ");
        o.Add(" X yönü aks  bilgileri                  Y yönü aks  bilgileri");
        string t = "Ú" + new string('Ä', 5) + "Â" + new string('Ä', 6) + "Â" + new string('Ä', 8) + "Â" + new string('Ä', 8) + "¿";
        string m = t.Replace('Ú', 'Ã').Replace('Â', 'Å').Replace('¿', '´');
        string b = t.Replace('Ú', 'À').Replace('Â', 'Á').Replace('¿', 'Ù');
        o.Add(t + "       " + t);
        o.Add("¡ no  ¡ isim ¡   Ax   ¡   Bx   ¡       ¡ no  ¡ isim ¡   Ay   ¡   By   ¡");
        o.Add(m + "       " + m);
        for (int i = 0; i < Math.Max(nx, ny); i++)
        {
            string xs = i < nx ? V + (i + 1).ToString().PadLeft(3) + "  " + V + "  " + Ad(i).PadRight(4) + V + N(ax[i][0], 2).PadLeft(7) + " " + V + N(ax[i][1], 2).PadLeft(7) + " " + V
                               : i == nx ? b : new string(' ', 32);
            string ys = i < ny ? V + (i + 1).ToString().PadLeft(4) + " " + V + "  " + Ad(nx + i).PadRight(4) + V + N(ax[nx + i][0], 2).PadLeft(7) + " " + V + N(ax[nx + i][1], 2).PadLeft(7) + " " + V
                               : i == ny ? b : new string(' ', 32);
            o.Add(xs + "       " + ys);
        }
        o.Add(((nx >= ny ? b : new string(' ', 32)) + "       " + (ny >= nx ? b : "")).TrimEnd());
        // 1. kat kolon aksları
        int c = p.Sec("/Column axis data/");
        var cols = new List<double[]>();
        for (int i = c + 1; i < p.St4.Length && !p.St4[i].StartsWith("/"); i++) cols.Add(p.St4[i].Split(',').Select(Proj.D).ToArray());
        o.Add("1. KAT KOLONLARI AKS BİLGİLERİ");
        string ct = "Ú" + new string('Ä', 8) + "Â" + new string('Ä', 6) + "Â" + new string('Ä', 6) + "Â" + new string('Ä', 6) + "Â" + new string('Ä', 6) + "Â" + new string('Ä', 8) + "¿";
        string cm = ct.Replace('Ú', 'Ã').Replace('Â', 'Å').Replace('¿', '´');
        string cb = ct.Replace('Ú', 'À').Replace('Â', 'Á').Replace('¿', 'Ù');
        string h = "¡Kolon no¡X aksı¡Y aksı¡  dx  ¡  dy  ¡alt yük.¡";
        bool iki = cols.Count > 1;
        o.Add(ct + (iki ? "      " + ct : ""));
        o.Add(h + (iki ? "      " + h : ""));
        o.Add(cm + (iki ? "      " + cm : ""));
        int cd = p.Sec("/Columns Data/"); int maxNo = 0; for (int i = cd + 1; i < p.St4.Length && !p.St4[i].StartsWith("/"); i += 3) maxNo = Math.Max(maxNo, (int)Proj.D(p.St4[i].Split(',')[0]));
        int taban = maxNo / 100 >= Sayfa7.Katlar(p).Count ? 1000 : 100;
        string Hc(int i)
        {
            var r = cols[i];
            static string A(double a) => a >= 2000 && a < 3000 ? (int)a - 2000 + "Y" : a >= 1000 && a < 2000 ? (int)a - 1000 + "X" : ((int)a).ToString();
            return V + (taban + i + 1).ToString().PadLeft(6) + "  " + V + " " + A(r[1]).PadRight(5) + V + " " + A(r[2]).PadRight(5) + V
                + N(r[3] / 10, 1).PadLeft(6) + V + N(r[4] / 10, 1).PadLeft(6) + V + N(r[5], 2).PadLeft(7) + " " + V;
        }
        for (int i = 0; i < cols.Count; i += 2)
            o.Add(Hc(i) + (i + 1 < cols.Count ? "      " + Hc(i + 1) : (iki ? "      ¡        ¡      ¡      ¡      ¡      ¡        ¡" : "")));
        o.Add(cb + (iki ? "      " + cb : ""));
        // kat diyaframları
        var k = Sayfa7.Katlar(p).Skip(1).ToList();
        if (k.Count <= 1) return o;
        o.Add("         KAT DIYAFRAMLARI");
        o.Add(k[^1].Dyf.ToString().PadLeft(16));
        for (int i = k.Count - 1; i >= 0; i--)
        {
            o.Add("  Kat:" + (i + 1).ToString().PadLeft(3) + (i == k.Count - 1 ? "ÚÄÄÄÄÄÄÄÄÄ¿" : "ÃÄÄÄÄÄÄÄÄÄ´"));
            o.Add("         ¡" + (i > 0 ? k[i - 1].Dyf.ToString().PadLeft(6) + "   " : new string(' ', 9)) + "¡");
        }
        o.Add("         Á         Á");
        return o;
    }
}
