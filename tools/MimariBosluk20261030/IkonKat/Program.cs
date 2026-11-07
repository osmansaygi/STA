// MIMARIKAT Monolit simgesi: beton döşeme plağında kobalt çarpılı şaft + plağa çıkan üç beton basamak + turuncu çerçeve.
// Kullanım: IkonKat <çıktı klasörü>  → ba_mimarikat_monolit.png (256) + ba_mimarikat.png (32)
using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging;
string cikti = args.Length > 0 ? args[0] : ".";
Color ust = Color.FromArgb(243, 230, 205), on = Color.FromArgb(226, 208, 174), yan = Color.FromArgb(205, 185, 148), dip = Color.FromArgb(150, 130, 98);
Color cizgi = Color.FromArgb(110, 94, 70), mavi = Color.FromArgb(30, 78, 214), turuncu = Color.FromArgb(244, 122, 24);
PointF O = new PointF(158, 92);
PointF P(double s, double t, double z) => new PointF((float)(O.X + s * 0.866 - t * 0.866), (float)(O.Y + s * 0.5 + t * 0.5 - z));
using var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp))
{
    g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
    using var kalem = new Pen(cizgi, 5f) { LineJoin = LineJoin.Round };
    void Yuz(Color c, Pen k, params PointF[] p) { using var b = new SolidBrush(c); g.FillPolygon(b, p); g.DrawPolygon(k, p); }
    double a = 54, z0 = -20, h = 20;
    // basamaklar (önde, plağa doğru yükselir)
    for (int i = 2; i >= 0; i--)
    {
        double t0 = a + 22 * (3 - i), t1 = t0 - 22, zt = z0 + h - (i + 1) * 0 - (2 - i) * 14 - 14;
        double zb = z0 - 42;
        Yuz(ust, kalem, P(-40, t1, zt), P(10, t1, zt), P(10, t0, zt), P(-40, t0, zt));
        Yuz(on, kalem, P(-40, t0, zb), P(10, t0, zb), P(10, t0, zt), P(-40, t0, zt));
        Yuz(yan, kalem, P(10, t1, zb), P(10, t0, zb), P(10, t0, zt), P(10, t1, zt));
    }
    // döşeme plağı
    Yuz(ust, kalem, P(-a, -a, 0), P(a, -a, 0), P(a, a, 0), P(-a, a, 0));
    Yuz(on, kalem, P(-a, a, z0), P(a, a, z0), P(a, a, 0), P(-a, a, 0));
    Yuz(yan, kalem, P(a, -a, z0), P(a, a, z0), P(a, a, 0), P(a, -a, 0));
    // şaft boşluğu
    double b0 = -20, b1 = 34, c0 = -46, c1 = 8;
    using (var ince = new Pen(cizgi, 4f) { LineJoin = LineJoin.Round })
    {
        Yuz(dip, ince, P(b0, c0, z0), P(b1, c0, z0), P(b1, c1, z0), P(b0, c1, z0));
        Yuz(yan, ince, P(b0, c0, z0), P(b1, c0, z0), P(b1, c0, 0), P(b0, c0, 0));
        Yuz(on, ince, P(b0, c0, z0), P(b0, c1, z0), P(b0, c1, 0), P(b0, c0, 0));
    }
    using (var mp = new Pen(mavi, 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
    { g.DrawLine(mp, P(b0, c0, 0), P(b1, c1, 0)); g.DrawLine(mp, P(b1, c0, 0), P(b0, c1, 0)); }
    using (var tk = new Pen(turuncu, 7f) { LineJoin = LineJoin.Round })
        g.DrawPolygon(tk, new[] { P(b0, c0, 0), P(b1, c0, 0), P(b1, c1, 0), P(b0, c1, 0) });
}
bmp.Save(Path.Combine(cikti, "ba_mimarikat_monolit.png"), ImageFormat.Png);
int x0 = 256, y0 = 256, x1 = 0, y1 = 0;
for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++) if (bmp.GetPixel(x, y).A >= 48) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
var src = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1); double kk = 30.0 / Math.Max(src.Width, src.Height);
int ww = (int)Math.Round(src.Width * kk), hh = (int)Math.Round(src.Height * kk);
using var kucuk = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(kucuk)) { g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(bmp, new Rectangle((32 - ww) / 2, (32 - hh) / 2, ww, hh), src, GraphicsUnit.Pixel); }
kucuk.Save(Path.Combine(cikti, "ba_mimarikat.png"), ImageFormat.Png);
Console.WriteLine($"OK kırpma {src}");
