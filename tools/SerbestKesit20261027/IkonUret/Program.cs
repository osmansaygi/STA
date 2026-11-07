// SERBESTKESIT Monolit simgesi: plan plağı üstünde turuncu kesik kesit hattı + bakış oku, hattın üstünde iki katlı beton çerçeve (kesit), kobalt donatı.
// Kullanım: IkonUret <çıktı klasörü>  → serbest_kesit_monolit.png (256) + serbest_kesit_32.png (önizleme)
using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging;
string cikti = args.Length > 0 ? args[0] : ".";
Color ust = Color.FromArgb(243, 230, 205), on = Color.FromArgb(226, 208, 174), yan = Color.FromArgb(205, 185, 148);
Color cizgi = Color.FromArgb(110, 94, 70), mavi = Color.FromArgb(30, 78, 214), turuncu = Color.FromArgb(244, 122, 24), turuncuK = Color.FromArgb(200, 92, 14);
PointF O = new PointF(128, 160);
const double K = 0.8;
PointF P(double s, double t, double z) => new PointF((float)(O.X + K * (s * 0.866 - t * 0.866)), (float)(O.Y + K * (s * 0.5 + t * 0.5 - z)));
using var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp))
{
    g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
    using var kalem = new Pen(cizgi, 5f) { LineJoin = LineJoin.Round };
    void Yuz(Color c, Pen k, params PointF[] p) { using var b = new SolidBrush(c); g.FillPolygon(b, p); g.DrawPolygon(k, p); }
    void Kutu(double s0, double s1, double t0, double t1, double z0, double z1)
    {
        Yuz(ust, kalem, P(s0, t0, z1), P(s1, t0, z1), P(s1, t1, z1), P(s0, t1, z1));
        Yuz(on, kalem, P(s0, t1, z0), P(s1, t1, z0), P(s1, t1, z1), P(s0, t1, z1));
        Yuz(yan, kalem, P(s1, t0, z0), P(s1, t1, z0), P(s1, t1, z1), P(s1, t0, z1));
    }
    // plan plağı
    Kutu(-78, 78, -78, 78, -14, 0);
    // plan üstünde turuncu kesik kesit hattı (t = 20 boyunca)
    using (var kh = new Pen(turuncu, 8f) { DashPattern = new[] { 2.2f, 1.3f }, StartCap = LineCap.Round, EndCap = LineCap.Round })
        g.DrawLine(kh, P(-74, 20, 0), P(74, 20, 0));
    // bakış oku (hattan -t yönüne, çerçeveye doğru)
    using (var ok = new SolidBrush(turuncu))
    using (var okK = new Pen(turuncuK, 3f) { LineJoin = LineJoin.Round })
    {
        // ok kesit hattından plağın önüne bakıyor: yönü -t (çerçeve arkada)
        var d = new[] { P(-10, 70, 0), P(10, 70, 0), P(10, 44, 0), P(22, 44, 0), P(0, 26, 0), P(-22, 44, 0), P(-10, 44, 0) };
        g.FillPolygon(ok, d); g.DrawPolygon(okK, d);
    }
    // kesit çerçevesi: hattın arkasında (t = -10..10) iki katlı kolon + döşeme
    foreach (double s in new[] { -62.0, -6.0, 50.0 }) Kutu(s, s + 12, -8, 8, 0, 112);
    foreach (double z in new[] { 50.0, 108.0 }) Kutu(-66, 66, -10, 10, z, z + 14);
    // kobalt donatı: döşeme ön yüzünde alt donatı çizgisi
    using (var mp = new Pen(mavi, 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        foreach (double z in new[] { 53.5, 111.5 }) g.DrawLine(mp, P(-60, 10, z), P(60, 10, z));
}
bmp.Save(Path.Combine(cikti, "serbest_kesit_monolit.png"), ImageFormat.Png);
int x0 = 256, y0 = 256, x1 = 0, y1 = 0;
for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++) if (bmp.GetPixel(x, y).A >= 48) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
var src = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1); double k = 30.0 / Math.Max(src.Width, src.Height);
int w = (int)Math.Round(src.Width * k), h = (int)Math.Round(src.Height * k);
using var kucuk = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(kucuk)) { g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(bmp, new Rectangle((32 - w) / 2, (32 - h) / 2, w, h), src, GraphicsUnit.Pixel); }
kucuk.Save(Path.Combine(cikti, "serbest_kesit_32.png"), ImageFormat.Png);
using var buyuk = new Bitmap(128, 128); using (var g = Graphics.FromImage(buyuk)) { g.InterpolationMode = InterpolationMode.NearestNeighbor; g.Clear(Color.FromArgb(59, 68, 83)); g.DrawImage(kucuk, 0, 0, 128, 128); }
buyuk.Save(Path.Combine(cikti, "serbest_kesit_32x4.png"), ImageFormat.Png);
Console.WriteLine($"OK kırpma {src}");
