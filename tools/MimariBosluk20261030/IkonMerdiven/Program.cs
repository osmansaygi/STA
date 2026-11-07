// MIMARIMERDIVEN Monolit simgesi: beton merdiven kolu (izometrik basamaklar), kobalt çıkış hattı, turuncu yön oku.
// Kullanım: IkonMerdiven <çıktı klasörü>  → ba_mimarimerdiven_monolit.png (256) + ba_mimarimerdiven.png (32)
using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging;
string cikti = args.Length > 0 ? args[0] : ".";
Color ust = Color.FromArgb(243, 230, 205), on = Color.FromArgb(226, 208, 174), yan = Color.FromArgb(205, 185, 148);
Color cizgi = Color.FromArgb(110, 94, 70), mavi = Color.FromArgb(30, 78, 214), turuncu = Color.FromArgb(244, 122, 24), turuncuK = Color.FromArgb(200, 92, 14);
PointF O = new PointF(124, 196);
PointF P(double s, double t, double z) => new PointF((float)(O.X + s * 0.866 - t * 0.866), (float)(O.Y + s * 0.5 + t * 0.5 - z));
using var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp))
{
    g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
    using var kalem = new Pen(cizgi, 5f) { LineJoin = LineJoin.Round };
    void Yuz(Color c, params PointF[] p) { using var b = new SolidBrush(c); g.FillPolygon(b, p); g.DrawPolygon(kalem, p); }
    // 5 basamak: t ekseni boyunca geriye doğru yükselir (genişlik s: -45..45, basamak 26, rıht 22)
    int n = 5; double w0 = -42, w1 = 42, d = 24, h = 21;
    for (int i = n - 1; i >= 0; i--)
    {
        double t0 = 40 - i * d, t1 = t0 - d, z = (i + 1) * h;
        Yuz(ust, P(w0, t1, z), P(w1, t1, z), P(w1, t0, z), P(w0, t0, z));        // basamak üstü
        Yuz(on, P(w0, t0, z - h), P(w1, t0, z - h), P(w1, t0, z), P(w0, t0, z));  // rıht
        Yuz(yan, P(w1, t1, 0), P(w1, t0, 0), P(w1, t0, z), P(w1, t1, z));        // yan yüz
    }
    // kobalt çıkış hattı basamak ortasından
    using (var mp = new Pen(mavi, 8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
    {
        var yol = new List<PointF>();
        for (int i = 0; i < n; i++) { double t0 = 40 - i * d; yol.Add(P(0, t0 - d / 2, (i + 1) * h + 1)); }
        g.DrawLines(mp, yol.ToArray());
        using var dolu = new SolidBrush(Color.White);
        var bas = P(0, 40 - d / 2, h + 1); g.FillEllipse(dolu, bas.X - 8, bas.Y - 8, 16, 16); g.DrawEllipse(mp, bas.X - 8, bas.Y - 8, 16, 16);
    }
    // turuncu ok: üst basamağın üstünde çıkış yönünü gösterir
    var uc = P(0, 40 - (n - 1) * d - d / 2 - 34, n * h + 1);
    var arka = P(0, 40 - (n - 1) * d - d / 2 + 4, n * h + 1);
    float vx = uc.X - arka.X, vy = uc.Y - arka.Y; float L = (float)Math.Sqrt(vx * vx + vy * vy); vx /= L; vy /= L;
    var ok = new[] { uc, new PointF(arka.X - vy * 20, arka.Y + vx * 20), new PointF(arka.X + vy * 20, arka.Y - vx * 20) };
    using (var b = new SolidBrush(turuncu)) g.FillPolygon(b, ok);
    using (var k = new Pen(turuncuK, 4f) { LineJoin = LineJoin.Round }) g.DrawPolygon(k, ok);
}
bmp.Save(Path.Combine(cikti, "ba_mimarimerdiven_monolit.png"), ImageFormat.Png);
int x0 = 256, y0 = 256, x1 = 0, y1 = 0;
for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++) if (bmp.GetPixel(x, y).A >= 48) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
var src = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1); double kk = 30.0 / Math.Max(src.Width, src.Height);
int ww = (int)Math.Round(src.Width * kk), hh = (int)Math.Round(src.Height * kk);
using var kucuk = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(kucuk)) { g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(bmp, new Rectangle((32 - ww) / 2, (32 - hh) / 2, ww, hh), src, GraphicsUnit.Pixel); }
kucuk.Save(Path.Combine(cikti, "ba_mimarimerdiven.png"), ImageFormat.Png);
Console.WriteLine($"OK kırpma {src}");
