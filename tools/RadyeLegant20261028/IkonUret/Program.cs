// RADYELEGANTKARSILASTIRMA Monolit simgesi: üst üste iki izometrik radye plağı; her birinde turuncu ilave ısı
// bantları ve kobalt donatı çizgileri (altta GPR, üstte hesap). Kullanım: IkonUret <çıktı klasörü>
using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging;
string cikti = args.Length > 0 ? args[0] : ".";
Color ust = Color.FromArgb(243, 230, 205), on = Color.FromArgb(226, 208, 174), yan = Color.FromArgb(205, 185, 148);
Color cizgi = Color.FromArgb(110, 94, 70), mavi = Color.FromArgb(30, 78, 214);
Color[] isi = { Color.FromArgb(255, 196, 64), Color.FromArgb(244, 122, 24), Color.FromArgb(214, 64, 24) };
PointF O = new PointF(128, 128);
const double K = 0.78;
PointF P(double s, double t, double z) => new PointF((float)(O.X + K * (s * 0.866 - t * 0.866)), (float)(O.Y + K * (s * 0.5 + t * 0.5 - z)));
using var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp))
{
    g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
    using var kalem = new Pen(cizgi, 5f) { LineJoin = LineJoin.Round };
    void Yuz(Color c, Pen k, params PointF[] p) { using var b = new SolidBrush(c); g.FillPolygon(b, p); g.DrawPolygon(k, p); }
    void Plak(double z0, double ds, double dt)
    {
        double s0 = -70 + ds, s1 = 70 + ds, t0 = -70 + dt, t1 = 70 + dt, z1 = z0 + 14;
        Yuz(ust, kalem, P(s0, t0, z1), P(s1, t0, z1), P(s1, t1, z1), P(s0, t1, z1));
        Yuz(on, kalem, P(s0, t1, z0), P(s1, t1, z0), P(s1, t1, z1), P(s0, t1, z1));
        Yuz(yan, kalem, P(s1, t0, z0), P(s1, t1, z0), P(s1, t1, z1), P(s1, t0, z1));
        // kobalt ana donatı çizgileri (üst yüzde, X yönü)
        using (var mp = new Pen(mavi, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            foreach (double t in new[] { -44.0, 0.0, 44.0 }) g.DrawLine(mp, P(s0 + 10, t + dt, z1), P(s1 - 10, t + dt, z1));
        // turuncu ilave ısı bantları: iç içe üç kademe (lejant kademeleri)
        double cs = 20 + ds, ct = -10 + dt;
        double[] r = { 40, 27, 14 };
        for (int i = 0; i < 3; i++)
        {
            var pts = new PointF[24];
            for (int j = 0; j < 24; j++) { double a = j * Math.PI / 12; pts[j] = P(cs + r[i] * Math.Cos(a), ct + r[i] * 0.8 * Math.Sin(a), z1); }
            using var b = new SolidBrush(isi[i]); using var k2 = new Pen(Color.FromArgb(160, 70, 10), 2.5f);
            g.FillPolygon(b, pts); g.DrawPolygon(k2, pts);
        }
    }
    Plak(-62, 0, 0);   // alt: GPR
    Plak(34, 0, 0);    // üst: hesap
}
bmp.Save(Path.Combine(cikti, "radye_legant_karsilastirma_monolit.png"), ImageFormat.Png);
int x0 = 256, y0 = 256, x1 = 0, y1 = 0;
for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++) if (bmp.GetPixel(x, y).A >= 48) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
var src = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1); double k = 30.0 / Math.Max(src.Width, src.Height);
int w = (int)Math.Round(src.Width * k), h = (int)Math.Round(src.Height * k);
using var kucuk = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(kucuk)) { g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(bmp, new Rectangle((32 - w) / 2, (32 - h) / 2, w, h), src, GraphicsUnit.Pixel); }
kucuk.Save(Path.Combine(cikti, "radye_legant_32.png"), ImageFormat.Png);
using var buyuk = new Bitmap(128, 128); using (var g = Graphics.FromImage(buyuk)) { g.InterpolationMode = InterpolationMode.NearestNeighbor; g.Clear(Color.FromArgb(59, 68, 83)); g.DrawImage(kucuk, 0, 0, 128, 128); }
buyuk.Save(Path.Combine(cikti, "radye_legant_32x4.png"), ImageFormat.Png);
Console.WriteLine($"OK kırpma {src}");
