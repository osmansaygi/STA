using System.Globalization;

namespace StatikRaporClaude.Uretec;
public static class Yuk
{
    const char V = '\u00A1';
    static string F3(double v) => Fmt.N(v, 3);
    static string W7(double v) { for (int d = 3; d >= 0; d--) { var s = Fmt.N(v, d); if (s.Length <= 7) return s.PadLeft(7); } return Fmt.N(v, 0); }
    static string Box(char a, char b) => a + new string('Ä', 63) + b;

    /// USE: 1. satır "Load Version", 2. satır azaltma %, 3-4 satır katsayılar, 5. satır PRE dosyası, sonra döşeme blokları (ad, kaplama adı, boş?, 9 sayı) ve duvar blokları (ad, 10 sayı).
    public static (List<(string Ad, string Kap, double[] v)> Dos, List<(string Ad, double[] v)> Duv) Oku(string[] use)
    {
        var dos = new List<(string, string, double[])>(); var duv = new List<(string, double[])>();
        if (use.Length < 7) return (dos, duv);
        int i = 6; var say = use[2].Split(','); int nd = (int)Proj.D(say[0]), nw = (int)Proj.D(say[1]);
        while (i + 3 < use.Length)
        {
            // döşeme bloğu: ad, kaplama, (boş), 9 sayı
            var c = use[i + 3].Split(',');
            if (dos.Count < nd && c.Length >= 9) { dos.Add((use[i].TrimEnd(), use[i + 1].TrimEnd() + "	" + use[i + 2].TrimEnd(), c.Select(Proj.D).ToArray())); i += 4; continue; }
            break;
        }
        while (i + 1 < use.Length)
        {
            var c = use[i + 1].Split(',');
            if (duv.Count < nw && c.Length >= 10) { duv.Add((use[i].TrimEnd(), c.Select(Proj.D).ToArray())); i += 2; continue; }
            break;
        }
        return (dos, duv);
    }

    public static List<string> Doseme(string[] use)
    {
        var (dos, _) = Oku(use);
        var o = new List<string> { " DÖŞEME YÜK ANALİZİ", Box('Ú', '¿') };
        bool ilk = true;
        foreach (var (ad, kap, v) in dos)
        {
            if (!ilk) o.Add(Box('Ã', '´')); ilk = false;
            o.Add(V + ad.PadRight(63) + V);
            double top = 0;
            void S(string lab, double d, double t)
            {
                double y = (float)d * (float)t; top += y;
                o.Add(V + lab.PadRight(28) + W7(d) + " t/m³ × " + F3(t).PadLeft(6) + " m   :" + W7(y) + " " + V);
            }
            if (v[1] != 0) S("Kaplama  (" + kap.Split('	')[0].PadRight(12) + ")", v[0], v[1]);
            if (v[2] != 0) S("Kaplama harcı", 2.2, v[2]);
            if (v[3] != 0) S("Tesviye betonu", 2.0, v[3]);
            if (v[4] != 0) S("Sıva", 2.2, v[4]);
            if (v[6] != 0) S("Dolgu", v[5], v[6]);
            if (v.Length > 8 && v[8] != 0) S("İlave    (" + kap.Split('	')[1].PadRight(12) + ")", v[7], v[8]);
            o.Add(V + "TOPLAM".PadRight(54, '.') + ":" + W7(top) + " " + V);
        }
        o.Add(Box('À', 'Ù'));
        o.Add(" (Döşeme zatileri, döşeme yük hesabında ilave edilecek)");
        return o;
    }

    public static List<string> Kiris(string[] use)
    {
        var (_, duv) = Oku(use);
        var o = new List<string> { " KİRİŞ YÜK ANALİZİ", Box('Ú', '¿') };
        bool ilk = true;
        foreach (var (ad, v) in duv)
        {
            if (!ilk) o.Add(Box('Ã', '´')); ilk = false;
            o.Add(V + ad.PadRight(63) + V);
            double top = 0; int n = 0;
            void S(string lab, double d, double t)
            {
                double y = (float)d * (float)t; top += y; n++;
                o.Add(V + lab.PadRight(29) + W7(d) + " t/m² × " + F3(t).PadLeft(6) + " m:" + W7(y) + "   " + V);
            }
            S("Duvar  yükü (" + (v[1].ToString(CultureInfo.InvariantCulture) + " cm").PadRight(14) + ")", v[2], v[3]);
            if (v[4] != 0) S("Pencere", v[4], v[5]);
            if (n > 1) o.Add(V + "TOPLAM".PadRight(52, '.') + ":" + W7(top) + "   " + V);
        }
        o.Add(Box('À', 'Ù'));
        o.Add(" (Kiriş zati, Kiriş yük hesabında ilave edilecek)");
        return o;
    }
}
