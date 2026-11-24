// KAPAK komutu yardımcı aracı.
//   Arac logo <beykent.png> <KapakLogoVeri.cs>  → logo dış hatlarını (delikler dahil) vektör halkalara çevirir (yeşil/mavi).
//   Arac ikon <çıktı klasörü>                    → ba_kapak_monolit.png (256) + ba_kapak.png (32) Monolit simgesi.
using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging; using System.Globalization; using System.Runtime.InteropServices; using System.Text;

if (args.Length >= 3 && args[0] == "logo") return Arac.Logo(args[1], args[2]);
if (args.Length >= 2 && args[0] == "ikon") return Arac.Ikon(args[1]);
Console.WriteLine("Kullanım: Arac logo <png> <cs> | Arac ikon <klasör>"); return 1;

static class Arac
{
    public static int Logo(string png, string cs)
    {
        using var bmp = new Bitmap(png);
        int W = bmp.Width, H = bmp.Height;
        var bd = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = bd.Stride;
        var px = new byte[stride * H]; Marshal.Copy(bd.Scan0, px, 0, px.Length); bmp.UnlockBits(bd);
        // Koyuluk alanı (Logo.cs ile aynı: alfa × (255 − parlaklık)); dışı 0. Yumuşatılmış kenarlar alt-piksel konum verir.
        double V(int x, int y)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return 0;
            int i = y * stride + x * 4; int lum = (px[i] + px[i + 1] + px[i + 2]) / 3;
            return px[i + 3] * (255 - lum) / 255.0;
        }
        const double esik = 127.5;

        // Marching squares: ızgara noktaları piksel merkezleri; her kenar kesişimi bir anahtar (yatay h / düşey v).
        var nokta = new Dictionary<long, (double X, double Y)>();
        var komsu = new Dictionary<long, List<long>>();
        long Anahtar(int tip, int i, int j) => ((long)tip << 62) | ((long)(i + 2) << 31) | (uint)(j + 2);
        long Kes(int tip, int i, int j)
        {
            long k = Anahtar(tip, i, j);
            if (!nokta.ContainsKey(k))
            {
                double a = tip == 0 ? V(i, j) : V(i, j), b = tip == 0 ? V(i + 1, j) : V(i, j + 1);
                double t = Math.Abs(b - a) < 1e-9 ? 0.5 : (esik - a) / (b - a);
                t = Math.Clamp(t, 0, 1);
                nokta[k] = tip == 0 ? (i + 0.5 + t, j + 0.5) : (i + 0.5, j + 0.5 + t);
            }
            return k;
        }
        void Bag(long a, long b)
        {
            if (!komsu.TryGetValue(a, out var la)) komsu[a] = la = new(); la.Add(b);
            if (!komsu.TryGetValue(b, out var lb)) komsu[b] = lb = new(); lb.Add(a);
        }
        for (int j = -1; j < H; j++) for (int i = -1; i < W; i++)
        {
            bool tl = V(i, j) > esik, tr = V(i + 1, j) > esik, br = V(i + 1, j + 1) > esik, bl = V(i, j + 1) > esik;
            int durum = (tl ? 8 : 0) | (tr ? 4 : 0) | (br ? 2 : 0) | (bl ? 1 : 0);
            if (durum == 0 || durum == 15) continue;
            long ust = 0, sag = 0, alt = 0, sol = 0;
            if (tl != tr) ust = Kes(0, i, j);
            if (bl != br) alt = Kes(0, i, j + 1);
            if (tl != bl) sol = Kes(1, i, j);
            if (tr != br) sag = Kes(1, i + 1, j);
            var kenarlar = new List<long>();
            if (tl != tr) kenarlar.Add(ust);
            if (tr != br) kenarlar.Add(sag);
            if (bl != br) kenarlar.Add(alt);
            if (tl != bl) kenarlar.Add(sol);
            if (kenarlar.Count == 2) Bag(kenarlar[0], kenarlar[1]);
            else
            {   // eyer: merkez değerine göre köşeler birleşik veya ayrık
                bool merkez = (V(i, j) + V(i + 1, j) + V(i + 1, j + 1) + V(i, j + 1)) / 4 > esik;
                if (tl == merkez) { Bag(ust, sag); Bag(alt, sol); } else { Bag(ust, sol); Bag(sag, alt); }
            }
        }
        var halkalar = new List<List<(double X, double Y)>>();
        var gezildi = new HashSet<long>();
        foreach (long bas in komsu.Keys)
        {
            if (gezildi.Contains(bas)) continue;
            var h = new List<(double, double)>(); long onceki = -1, p = bas;
            while (true)
            {
                gezildi.Add(p); h.Add(nokta[p]);
                long q = komsu[p].FirstOrDefault(c => c != onceki && !gezildi.Contains(c));
                if (q == 0 && !komsu[p].Contains(0)) break;
                onceki = p; p = q;
            }
            halkalar.Add(h);
        }

        double tol = 0.35; // piksel: düz kenarlar düz kalır, kavisler pürüzsüz
        var sonuc = new List<(bool Yesil, List<(double X, double Y)> P)>();
        double isaretX = 375.0 * W / 3118, ayrimX = 2000.0 * W / 3118;
        foreach (var h in halkalar)
        {
            if (h.Count < 8) continue;
            var pts = h;
            int i1 = 0; double en = -1;
            for (int i = 1; i < pts.Count; i++) { double d = Uz(pts[0], pts[i]); if (d > en) { en = d; i1 = i; } }
            var a = Dp(pts.GetRange(0, i1 + 1), tol);
            var bList = pts.GetRange(i1, pts.Count - i1); bList.Add(pts[0]);
            var b = Dp(bList, tol);
            var birlesik = a.Take(a.Count - 1).Concat(b.Take(b.Count - 1)).ToList();
            if (birlesik.Count < 3) continue;
            double minX = birlesik.Min(q => q.Item1), maxX = birlesik.Max(q => q.Item1), minY = birlesik.Min(q => q.Item2), maxY = birlesik.Max(q => q.Item2);
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            bool yesil = cx < isaretX ? cy > 0.56 * H : cx < ayrimX;
            sonuc.Add((yesil, birlesik.Select(q => (q.Item1 / H, (H - q.Item2) / H)).ToList()));
        }

        var inv = CultureInfo.InvariantCulture; var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated> tools/Kapak20261124/Arac logo ile üretildi; elle düzenlemeyin. </auto-generated>");
        sb.AppendLine("namespace ST4Yardimci");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>BEYKENT PROJE logosunun vektör dış hatları. Birim: logo yüksekliği = 1 (genişlik = En); y yukarı.</summary>");
        sb.AppendLine("    internal static class KapakLogoVeri");
        sb.AppendLine("    {");
        sb.AppendLine($"        internal const double En = {((double)W / H).ToString("0.#####", inv)};");
        foreach (var (ad, yesil) in new[] { ("Yesil", true), ("Mavi", false) })
        {
            sb.AppendLine($"        internal static readonly double[][] {ad} =");
            sb.AppendLine("        {");
            foreach (var s in sonuc.Where(x => x.Yesil == yesil))
                sb.AppendLine("            new double[] { " + string.Join(", ", s.P.Select(q => q.X.ToString("0.####", inv) + ", " + q.Y.ToString("0.####", inv))) + " },");
            sb.AppendLine("        };");
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");
        File.WriteAllText(cs, sb.ToString(), new UTF8Encoding(true));
        Console.WriteLine($"OK {W}x{H}: {sonuc.Count} halka, {sonuc.Sum(s => s.P.Count)} nokta (yesil {sonuc.Count(s => s.Yesil)}, mavi {sonuc.Count(s => !s.Yesil)})");
        return 0;
    }

    static double Uz((double, double) a, (double, double) b) => Math.Sqrt((a.Item1 - b.Item1) * (a.Item1 - b.Item1) + (a.Item2 - b.Item2) * (a.Item2 - b.Item2));

    static List<(double, double)> Dp(List<(double, double)> p, double tol)
    {
        if (p.Count < 3) return new(p);
        var tut = new bool[p.Count]; tut[0] = tut[^1] = true;
        var yigin = new Stack<(int, int)>(); yigin.Push((0, p.Count - 1));
        while (yigin.Count > 0)
        {
            var (a, b) = yigin.Pop(); double en = 0; int ei = -1;
            double ax = p[a].Item1, ay = p[a].Item2, bx = p[b].Item1, by = p[b].Item2, L = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            for (int i = a + 1; i < b; i++)
            {
                double d = L < 1e-9 ? Uz(p[i], p[a]) : Math.Abs((bx - ax) * (ay - p[i].Item2) - (ax - p[i].Item1) * (by - ay)) / L;
                if (d > en) { en = d; ei = i; }
            }
            if (ei >= 0 && en > tol) { tut[ei] = true; yigin.Push((a, ei)); yigin.Push((ei, b)); }
        }
        var r = new List<(double, double)>(); for (int i = 0; i < p.Count; i++) if (tut[i]) r.Add(p[i]); return r;
    }

    public static int Ikon(string cikti)
    {
        // Monolit: izometrik fildişi beton levha (kapak sayfası), kobalt başlık bandı ve satırlar, turuncu onay kutusu.
        Color ust = Color.FromArgb(243, 230, 205), on = Color.FromArgb(226, 208, 174), yan = Color.FromArgb(205, 185, 148);
        Color cizgi = Color.FromArgb(110, 94, 70), mavi = Color.FromArgb(30, 78, 214), turuncu = Color.FromArgb(244, 122, 24), turuncuK = Color.FromArgb(200, 92, 14);
        PointF O = new PointF(128, 150);
        PointF P(double s, double t, double z) => new PointF((float)(O.X + s * 0.866 - t * 0.866), (float)(O.Y + s * 0.5 + t * 0.5 - z));
        using var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
            using var kalem = new Pen(cizgi, 5f) { LineJoin = LineJoin.Round };
            void Yuz(Color c, Pen k, params PointF[] p) { using var b = new SolidBrush(c); g.FillPolygon(b, p); if (k != null) g.DrawPolygon(k, p); }
            double a = 58, b2 = 84, z0 = -18; // s: en, t: boy
            Yuz(ust, kalem, P(-a, -b2, 0), P(a, -b2, 0), P(a, b2, 0), P(-a, b2, 0));
            Yuz(on, kalem, P(-a, b2, z0), P(a, b2, z0), P(a, b2, 0), P(-a, b2, 0));
            Yuz(yan, kalem, P(a, -b2, z0), P(a, b2, z0), P(a, b2, 0), P(a, -b2, 0));
            Yuz(mavi, null, P(-a + 12, -b2 + 12, 0), P(a - 12, -b2 + 12, 0), P(a - 12, -b2 + 40, 0), P(-a + 12, -b2 + 40, 0));
            using (var mp = new Pen(mavi, 8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(mp, P(-a + 14, -b2 + 60, 0), P(a - 14, -b2 + 60, 0));
                g.DrawLine(mp, P(-a + 14, -b2 + 82, 0), P(a - 30, -b2 + 82, 0));
            }
            using (var tk = new Pen(turuncuK, 4f) { LineJoin = LineJoin.Round })
                Yuz(turuncu, tk, P(-a + 14, b2 - 56, 0), P(a - 14, b2 - 56, 0), P(a - 14, b2 - 14, 0), P(-a + 14, b2 - 14, 0));
        }
        bmp.Save(Path.Combine(cikti, "ba_kapak_monolit.png"), ImageFormat.Png);
        int bx0 = 256, by0 = 256, bx1 = 0, by1 = 0;
        for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++) if (bmp.GetPixel(x, y).A >= 48) { bx0 = Math.Min(bx0, x); by0 = Math.Min(by0, y); bx1 = Math.Max(bx1, x); by1 = Math.Max(by1, y); }
        var src = Rectangle.FromLTRB(bx0, by0, bx1 + 1, by1 + 1); double k = 30.0 / Math.Max(src.Width, src.Height);
        int w2 = (int)Math.Round(src.Width * k), h2 = (int)Math.Round(src.Height * k);
        using var kucuk = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(kucuk)) { g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(bmp, new Rectangle((32 - w2) / 2, (32 - h2) / 2, w2, h2), src, GraphicsUnit.Pixel); }
        kucuk.Save(Path.Combine(cikti, "ba_kapak.png"), ImageFormat.Png);
        Console.WriteLine($"OK kirpma {src}");
        return 0;
    }
}
