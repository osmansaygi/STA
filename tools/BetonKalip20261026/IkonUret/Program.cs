// BETONKALIP Monolit simgesi: döşeme plağı üstünde beton kolon, ön yüzde turuncu kalıp panosu, kobalt kelepçeler.
// Kullanım: IkonUret <çıktı klasörü>  → ba_betonkalip_monolit.png (256) + ba_betonkalip.png (32)
using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging;
string cikti = args.Length > 0 ? args[0] : ".";
Color ust = Color.FromArgb(243, 230, 205), on = Color.FromArgb(226, 208, 174), yan = Color.FromArgb(205, 185, 148);
Color cizgi = Color.FromArgb(110, 94, 70), mavi = Color.FromArgb(30, 78, 214), turuncu = Color.FromArgb(244, 122, 24), turuncuK = Color.FromArgb(200, 92, 14);
PointF O = new PointF(128, 150);
PointF P(double s, double t, double z) => new PointF((float)(O.X + s * 0.866 - t * 0.866), (float)(O.Y + s * 0.5 + t * 0.5 - z));
using var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp))
{
    g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
    using var kalem = new Pen(cizgi, 5f) { LineJoin = LineJoin.Round };
    void Yuz(Color c, Pen k, params PointF[] p) { using var b = new SolidBrush(c); g.FillPolygon(b, p); g.DrawPolygon(k, p); }
    void Kutu(double s0, double s1, double t0, double t1, double z0, double z1, Color cu, Color co, Color cy, Pen k)
    {
        Yuz(cu, k, P(s0, t0, z1), P(s1, t0, z1), P(s1, t1, z1), P(s0, t1, z1));
        Yuz(co, k, P(s0, t1, z0), P(s1, t1, z0), P(s1, t1, z1), P(s0, t1, z1));
        Yuz(cy, k, P(s1, t0, z0), P(s1, t1, z0), P(s1, t1, z1), P(s1, t0, z1));
    }
    // döşeme plağı
    Kutu(-60, 60, -60, 60, -20, 0, ust, on, yan, kalem);
    // beton kolon
    Kutu(-28, 28, -28, 28, 0, 100, ust, on, yan, kalem);
    // ön yüzde kalıp panosu (kolondan biraz dışarıda), dikey tahta çizgileri
    using (var kk = new Pen(turuncuK, 4f) { LineJoin = LineJoin.Round })
    {
        Yuz(turuncu, kk, P(-30, 36, 4), P(30, 36, 4), P(30, 36, 96), P(-30, 36, 96));
        using var ince = new Pen(turuncuK, 3f);
        foreach (double s in new[] { -10.0, 10.0 }) g.DrawLine(ince, P(s, 36, 6), P(s, 36, 94));
    }
    // kobalt kelepçeler: kalıp panosu ve kolon sağ yüzü boyunca
    using (var mp = new Pen(mavi, 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        foreach (double z in new[] { 28.0, 72.0 })
            g.DrawLines(mp, new[] { P(-34, 40, z), P(34, 40, z), P(34, -34, z) });
}
bmp.Save(Path.Combine(cikti, "ba_betonkalip_monolit.png"), ImageFormat.Png);
int x0 = 256, y0 = 256, x1 = 0, y1 = 0;
for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++) if (bmp.GetPixel(x, y).A >= 48) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
var src = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1); double k = 30.0 / Math.Max(src.Width, src.Height);
int w = (int)Math.Round(src.Width * k), h = (int)Math.Round(src.Height * k);
using var kucuk = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(kucuk)) { g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(bmp, new Rectangle((32 - w) / 2, (32 - h) / 2, w, h), src, GraphicsUnit.Pixel); }
kucuk.Save(Path.Combine(cikti, "ba_betonkalip.png"), ImageFormat.Png);
Console.WriteLine($"OK kırpma {src}");
