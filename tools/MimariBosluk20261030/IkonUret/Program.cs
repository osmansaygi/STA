// MIMARIBOSLUK Monolit simgesi: beton döşeme plağında şaft boşluğu, kobalt çarpı (boşluk işareti), turuncu aktarım oku.
// Kullanım: IkonUret <çıktı klasörü>  → ba_mimaribosluk_monolit.png (256) + ba_mimaribosluk.png (32)
using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging;
string cikti = args.Length > 0 ? args[0] : ".";
Color ust = Color.FromArgb(243, 230, 205), on = Color.FromArgb(226, 208, 174), yan = Color.FromArgb(205, 185, 148), dip = Color.FromArgb(150, 130, 98);
Color cizgi = Color.FromArgb(110, 94, 70), mavi = Color.FromArgb(30, 78, 214), turuncu = Color.FromArgb(244, 122, 24), turuncuK = Color.FromArgb(200, 92, 14);
PointF O = new PointF(128, 150);
PointF P(double s, double t, double z) => new PointF((float)(O.X + s * 0.866 - t * 0.866), (float)(O.Y + s * 0.5 + t * 0.5 - z));
using var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp))
{
    g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
    using var kalem = new Pen(cizgi, 5f) { LineJoin = LineJoin.Round };
    void Yuz(Color c, Pen k, params PointF[] p) { using var b = new SolidBrush(c); g.FillPolygon(b, p); g.DrawPolygon(k, p); }
    // döşeme plağı
    double a = 64, z0 = -24;
    Yuz(ust, kalem, P(-a, -a, 0), P(a, -a, 0), P(a, a, 0), P(-a, a, 0));
    Yuz(on, kalem, P(-a, a, z0), P(a, a, z0), P(a, a, 0), P(-a, a, 0));
    Yuz(yan, kalem, P(a, -a, z0), P(a, a, z0), P(a, a, 0), P(a, -a, 0));
    // şaft boşluğu: arka iki iç yüz + dip
    double b0 = -27, b1 = 27;
    using (var ince = new Pen(cizgi, 4f) { LineJoin = LineJoin.Round })
    {
        Yuz(dip, ince, P(b0, b0, z0), P(b1, b0, z0), P(b1, b1, z0), P(b0, b1, z0));
        Yuz(yan, ince, P(b0, b0, z0), P(b1, b0, z0), P(b1, b0, 0), P(b0, b0, 0));
        Yuz(on, ince, P(b0, b0, z0), P(b0, b1, z0), P(b0, b1, 0), P(b0, b0, 0));
    }
    // kobalt çarpı: kalıp planındaki boşluk işareti
    using (var mp = new Pen(mavi, 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
    {
        g.DrawLine(mp, P(b0, b0, 0), P(b1, b1, 0));
        g.DrawLine(mp, P(b1, b0, 0), P(b0, b1, 0));
    }
    // turuncu boşluk çerçevesi
    using (var tk = new Pen(turuncu, 7f) { LineJoin = LineJoin.Round })
        g.DrawPolygon(tk, new[] { P(b0, b0, 0), P(b1, b0, 0), P(b1, b1, 0), P(b0, b1, 0) });
    // aktarım oku: mimariden kalıba (yukarıdan boşluğa)
    using (var ok = new SolidBrush(turuncu))
    using (var okK = new Pen(turuncuK, 4f) { LineJoin = LineJoin.Round })
    {
        PointF u = P(0, 0, 122), t = P(0, 0, 40);
        float w1 = 11, w2 = 24;
        var ok1 = new[] { new PointF(u.X - w1, u.Y), new PointF(u.X + w1, u.Y), new PointF(u.X + w1, t.Y - 26), new PointF(u.X + w2, t.Y - 26),
                          new PointF(t.X, t.Y), new PointF(u.X - w2, t.Y - 26), new PointF(u.X - w1, t.Y - 26) };
        g.FillPolygon(ok, ok1); g.DrawPolygon(okK, ok1);
    }
}
bmp.Save(Path.Combine(cikti, "ba_mimaribosluk_monolit.png"), ImageFormat.Png);
int x0 = 256, y0 = 256, x1 = 0, y1 = 0;
for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++) if (bmp.GetPixel(x, y).A >= 48) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
var src = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1); double k = 30.0 / Math.Max(src.Width, src.Height);
int w = (int)Math.Round(src.Width * k), h = (int)Math.Round(src.Height * k);
using var kucuk = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(kucuk)) { g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(bmp, new Rectangle((32 - w) / 2, (32 - h) / 2, w, h), src, GraphicsUnit.Pixel); }
kucuk.Save(Path.Combine(cikti, "ba_mimaribosluk.png"), ImageFormat.Png);
Console.WriteLine($"OK kırpma {src}");
