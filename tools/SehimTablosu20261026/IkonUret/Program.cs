// SEHIMTABLOSU Monolit simgesi: iki beton mesnet, sarkan kiriş, kobalt donatı, turuncu sehim oku.
// Kullanım: IkonUret <çıktı klasörü>  → ba_sehimtablosu_monolit.png (256) + ba_sehimtablosu.png (32)
using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging;
string cikti = args.Length > 0 ? args[0] : ".";
Color ust = Color.FromArgb(243, 230, 205), on = Color.FromArgb(226, 208, 174), yan = Color.FromArgb(205, 185, 148);
Color cizgi = Color.FromArgb(110, 94, 70), mavi = Color.FromArgb(30, 78, 214), turuncu = Color.FromArgb(244, 122, 24);
PointF O = new PointF(58, 66);
const double L = 150, W = 30, H = 26, D = 16, ML = 28, MH = 44;
PointF P(double s, double t, double z) => new PointF((float)(O.X + s * 0.866 - t * 0.866), (float)(O.Y + s * 0.5 + t * 0.5 - z));
double Sag(double s) { double u = Math.Clamp((s - ML) / (L - 2 * ML), 0, 1); return -D * 4 * u * (1 - u); }
using var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp))
{
    g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
    using var kalem = new Pen(cizgi, 5f) { LineJoin = LineJoin.Round };
    void Yuz(Color c, params PointF[] p) { using var b = new SolidBrush(c); g.FillPolygon(b, p); g.DrawPolygon(kalem, p); }
    void Kutu(double s0, double s1, double t0, double t1, double z0, double z1)
    {
        Yuz(ust, P(s0, t0, z1), P(s1, t0, z1), P(s1, t1, z1), P(s0, t1, z1));
        Yuz(on, P(s0, t1, z0), P(s1, t1, z0), P(s1, t1, z1), P(s0, t1, z1));
        Yuz(yan, P(s1, t0, z0), P(s1, t1, z0), P(s1, t1, z1), P(s1, t0, z1));
    }
    // mesnetler (kolon başlığı gibi), kirişten biraz geniş
    Kutu(0, ML, -4, W + 4, -MH, 0);
    Kutu(L - ML, L, -4, W + 4, -MH, 0);
    // sarkan kiriş: üst yüz, ön yüz, sağ uç yüzü
    int n = 40; var top = new List<PointF>(); var front = new List<PointF>();
    for (int i = 0; i <= n; i++) { double s = L * i / n; top.Add(P(s, 0, H + Sag(s))); }
    for (int i = n; i >= 0; i--) { double s = L * i / n; top.Add(P(s, W, H + Sag(s))); }
    Yuz(ust, top.ToArray());
    for (int i = 0; i <= n; i++) { double s = L * i / n; front.Add(P(s, W, Sag(s))); }
    for (int i = n; i >= 0; i--) { double s = L * i / n; front.Add(P(s, W, H + Sag(s))); }
    Yuz(on, front.ToArray());
    Yuz(yan, P(L, 0, 0), P(L, W, 0), P(L, W, H), P(L, 0, H));
    // kobalt donatı ön yüzde, sehim eğrisini izler
    var bar = new List<PointF>();
    for (int i = 0; i <= n; i++) { double s = 10 + (L - 20) * i / n; bar.Add(P(s, W, 7 + Sag(s))); }
    using (var mp = new Pen(mavi, 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) g.DrawLines(mp, bar.ToArray());
    // turuncu sehim oku: kiriş altı, açıklık ortası
    PointF a = P(L / 2, W, Sag(L / 2) - 8), b = new PointF(a.X, a.Y + 52);
    using (var op = new Pen(turuncu, 12f) { StartCap = LineCap.Round }) g.DrawLine(op, a, new PointF(b.X, b.Y - 16));
    using (var ob = new SolidBrush(turuncu)) g.FillPolygon(ob, new[] { new PointF(b.X - 17, b.Y - 20), new PointF(b.X + 17, b.Y - 20), new PointF(b.X, b.Y + 4) });
}
bmp.Save(Path.Combine(cikti, "ba_sehimtablosu_monolit.png"), ImageFormat.Png);
// 32 px: saydam kenar kırpılır, 30 px'e sığdırılır (BaCommandIcons atlas kuralı)
int x0 = 256, y0 = 256, x1 = 0, y1 = 0;
for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++) if (bmp.GetPixel(x, y).A >= 48) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
var src = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1); double k = 30.0 / Math.Max(src.Width, src.Height);
int w = (int)Math.Round(src.Width * k), h = (int)Math.Round(src.Height * k);
using var kucuk = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(kucuk)) { g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(bmp, new Rectangle((32 - w) / 2, (32 - h) / 2, w, h), src, GraphicsUnit.Pixel); }
kucuk.Save(Path.Combine(cikti, "ba_sehimtablosu.png"), ImageFormat.Png);
Console.WriteLine($"OK kırpma {src}");
