// KIRISCIZ Monolit simgesi: iki beton kolona oturan fildişi kiriş (hafif derinlikli önden görünüş),
// ön yüzde kobalt gönyeli üst/alt donatı, sağ üstte turuncu kalem (elle girilen kiriş).
// Kullanım: IkonKiris <çıktı klasörü> → kiris_ciz_monolit.png (256) + ba_kirisciz.png (32) + ba_kirisciz_x8.png (inceleme)
using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging;
string cikti = args.Length > 0 ? args[0] : ".";
Color ust = Color.FromArgb(243, 230, 205), on = Color.FromArgb(226, 208, 174), yan = Color.FromArgb(205, 185, 148);
Color cizgi = Color.FromArgb(110, 94, 70), mavi = Color.FromArgb(30, 78, 214), turuncu = Color.FromArgb(244, 122, 24), koyuTuruncu = Color.FromArgb(150, 70, 10);
const float dx = 20, dy = -16; // derinlik kayması
using var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp))
{
    g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
    using var kalem = new Pen(cizgi, 6f) { LineJoin = LineJoin.Round };
    void Yuz(Color c, params PointF[] p) { using var b = new SolidBrush(c); g.FillPolygon(b, p); g.DrawPolygon(kalem, p); }
    void Kutu(float x0, float y0, float x1, float y1, bool ustYuz)
    {
        if (ustYuz) Yuz(ust, new PointF(x0, y0), new PointF(x0 + dx, y0 + dy), new PointF(x1 + dx, y0 + dy), new PointF(x1, y0));
        Yuz(yan, new PointF(x1, y0), new PointF(x1 + dx, y0 + dy), new PointF(x1 + dx, y1 + dy), new PointF(x1, y1));
        Yuz(on, new PointF(x0, y0), new PointF(x1, y0), new PointF(x1, y1), new PointF(x0, y1));
    }
    // kolonlar kirişin altında
    Kutu(30, 150, 68, 236, false);
    Kutu(178, 150, 216, 236, false);
    // kiriş
    Kutu(10, 96, 226, 158, true);
    // ön yüzde donatı açılımı: üst çubuk uçları aşağı, alt çubuk uçları yukarı gönyeli
    using (var mp = new Pen(mavi, 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
    {
        g.DrawLines(mp, new[] { new PointF(24, 122), new PointF(24, 109), new PointF(212, 109), new PointF(212, 122) });
        g.DrawLines(mp, new[] { new PointF(24, 132), new PointF(24, 145), new PointF(212, 145), new PointF(212, 132) });
    }
    // kalem (sağ üst, ucu kirişe doğru)
    PointF a = new PointF(178, 66), b = new PointF(236, 10);
    double vx = b.X - a.X, vy = b.Y - a.Y, L = Math.Sqrt(vx * vx + vy * vy); float ux = (float)(vx / L), uy = (float)(vy / L);
    float nx = -uy * 14, ny = ux * 14;
    PointF uc = new PointF(a.X - ux * 30, a.Y - uy * 30);
    var govde = new[] { new PointF(a.X + nx, a.Y + ny), new PointF(b.X + nx, b.Y + ny), new PointF(b.X - nx, b.Y - ny), new PointF(a.X - nx, a.Y - ny) };
    using (var b1 = new SolidBrush(turuncu)) g.FillPolygon(b1, govde);
    using (var b2 = new SolidBrush(ust)) g.FillPolygon(b2, new[] { new PointF(a.X + nx, a.Y + ny), new PointF(a.X - nx, a.Y - ny), uc });
    using (var b3 = new SolidBrush(koyuTuruncu)) g.FillPolygon(b3, new[] { new PointF(uc.X + ux * 11 + nx * 0.38f, uc.Y + uy * 11 + ny * 0.38f), new PointF(uc.X + ux * 11 - nx * 0.38f, uc.Y + uy * 11 - ny * 0.38f), uc });
    using (var k2 = new Pen(cizgi, 5f) { LineJoin = LineJoin.Round })
        g.DrawPolygon(k2, new[] { new PointF(b.X + nx, b.Y + ny), new PointF(a.X + nx, a.Y + ny), uc, new PointF(a.X - nx, a.Y - ny), new PointF(b.X - nx, b.Y - ny) });
}
bmp.Save(Path.Combine(cikti, "kiris_ciz_monolit.png"), ImageFormat.Png);
int x0 = 256, y0 = 256, x1 = 0, y1 = 0;
for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++) if (bmp.GetPixel(x, y).A >= 48) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
var src = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1); double kk = 30.0 / Math.Max(src.Width, src.Height);
int ww = (int)Math.Round(src.Width * kk), hh = (int)Math.Round(src.Height * kk);
using var kucuk = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(kucuk)) { g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(bmp, new Rectangle((32 - ww) / 2, (32 - hh) / 2, ww, hh), src, GraphicsUnit.Pixel); }
kucuk.Save(Path.Combine(cikti, "ba_kirisciz.png"), ImageFormat.Png);
using var buyuk = new Bitmap(512, 256); using (var g = Graphics.FromImage(buyuk))
{
    g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
    g.FillRectangle(Brushes.WhiteSmoke, 0, 0, 256, 256); using (var k = new SolidBrush(Color.FromArgb(60, 63, 68))) g.FillRectangle(k, 256, 0, 256, 256);
    g.DrawImage(kucuk, new Rectangle(0, 0, 256, 256)); g.DrawImage(kucuk, new Rectangle(256, 0, 256, 256));
}
buyuk.Save(Path.Combine(cikti, "ba_kirisciz_x8.png"), ImageFormat.Png);
Console.WriteLine($"OK kırpma {src}");
