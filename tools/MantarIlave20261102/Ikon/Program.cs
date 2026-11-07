// MANTARILAVE Monolit simgesi: izometrik fildişi mantar döşeme plağı, altında kolon,
// plakta kolon üstünde kesişen kobalt X/Y ilave çubukları (uçları gönyeli), turuncu kolon izi.
// Kullanım: Ikon <çıktı klasörü> → mantar_ilave_monolit.png (256) + ba_mantarilave.png (32) + ba_mantarilave_x8.png (inceleme)
using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging;
string cikti = args.Length > 0 ? args[0] : ".";
Color ust = Color.FromArgb(243, 230, 205), on = Color.FromArgb(226, 208, 174), yan = Color.FromArgb(205, 185, 148);
Color cizgi = Color.FromArgb(110, 94, 70), mavi = Color.FromArgb(30, 78, 214), turuncu = Color.FromArgb(244, 122, 24);
const float k = 0.66f, cx = 128, cy = 98;
PointF P(float x, float y, float z) => new PointF(cx + (x - y) * 0.866f * k, cy + (x + y) * 0.5f * k - z * k);
using var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp))
{
    g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
    using var kalem = new Pen(cizgi, 6f) { LineJoin = LineJoin.Round };
    void Yuz(Color c, params PointF[] p) { using var b = new SolidBrush(c); g.FillPolygon(b, p); g.DrawPolygon(kalem, p); }
    // kolon (plak altında, merkezde): üst kotu plak altı z=-16
    float a = 30, zb = -18, za = -120;
    Yuz(on, P(a, -a, zb), P(a, a, zb), P(a, a, za), P(a, -a, za));
    Yuz(yan, P(-a, a, zb), P(a, a, zb), P(a, a, za), P(-a, a, za));
    // plak: 200×200, kalınlık 16
    float s = 96, t = 18;
    Yuz(on, P(s, -s, 0), P(s, s, 0), P(s, s, -t), P(s, -s, -t));
    Yuz(yan, P(-s, s, 0), P(s, s, 0), P(s, s, -t), P(-s, s, -t));
    Yuz(ust, P(-s, -s, 0), P(s, -s, 0), P(s, s, 0), P(-s, s, 0));
    // turuncu kolon izi (plak üstünde)
    using (var tp = new Pen(turuncu, 10f) { LineJoin = LineJoin.Round })
        g.DrawPolygon(tp, new[] { P(-a, -a, 0), P(a, -a, 0), P(a, a, 0), P(-a, a, 0) });
    // kobalt ilave çubuklar: her yönde 3 çubuk, kolona ortalı, uçlarda aşağı gönye
    using var mp = new Pen(mavi, 13f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
    float L = 74, z = 8, h = 22;
    foreach (float o in new[] { -26f, 26f })
    {
        g.DrawLines(mp, new[] { P(-L, o, z - h), P(-L, o, z), P(L, o, z), P(L, o, z - h) });
    }
    foreach (float o in new[] { -26f, 26f })
    {
        g.DrawLines(mp, new[] { P(o, -L, z + 4 - h), P(o, -L, z + 4), P(o, L, z + 4), P(o, L, z + 4 - h) });
    }
}
bmp.Save(Path.Combine(cikti, "mantar_ilave_monolit.png"), ImageFormat.Png);
int x0 = 256, y0 = 256, x1 = 0, y1 = 0;
for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++) if (bmp.GetPixel(x, y).A >= 48) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
var src = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1); double kk = 30.0 / Math.Max(src.Width, src.Height);
int ww = (int)Math.Round(src.Width * kk), hh = (int)Math.Round(src.Height * kk);
using var kucuk = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(kucuk)) { g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(bmp, new Rectangle((32 - ww) / 2, (32 - hh) / 2, ww, hh), src, GraphicsUnit.Pixel); }
kucuk.Save(Path.Combine(cikti, "ba_mantarilave.png"), ImageFormat.Png);
using var buyuk = new Bitmap(512, 256); using (var g = Graphics.FromImage(buyuk))
{
    g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
    g.FillRectangle(Brushes.WhiteSmoke, 0, 0, 256, 256); using (var b = new SolidBrush(Color.FromArgb(60, 63, 68))) g.FillRectangle(b, 256, 0, 256, 256);
    g.DrawImage(kucuk, new Rectangle(0, 0, 256, 256)); g.DrawImage(kucuk, new Rectangle(256, 0, 256, 256));
}
buyuk.Save(Path.Combine(cikti, "ba_mantarilave_x8.png"), ImageFormat.Png);
Console.WriteLine($"OK kırpma {src}");
