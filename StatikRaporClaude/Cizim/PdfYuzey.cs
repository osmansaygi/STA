using System.Drawing;
using System.Drawing.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;

namespace StatikRaporClaude.Cizim
{
    /// <summary>PDFsharp yüzeyi. Sayfa başında dünya birimi mm'ye çevrilir (1 mm = 72/25.4 pt).</summary>
    public sealed class PdfYuzey : ICizimYuzeyi, IDisposable
    {
        private static readonly Dictionary<Image, XImage> Resimler = new Dictionary<Image, XImage>();
        private readonly XGraphics _g;
        private readonly PdfPage _sayfa;
        private readonly Stack<XGraphicsState> _durum = new Stack<XGraphicsState>();
        private readonly Dictionary<YaziTipi, XFont> _fontlar = new Dictionary<YaziTipi, XFont>();

        public PdfYuzey(PdfPage sayfa)
        {
            FontCozucu.Kur();
            _sayfa = sayfa;
            _g = XGraphics.FromPdfPage(sayfa);
            _g.ScaleTransform(72.0 / 25.4);
        }

        private XFont Font(YaziTipi f)
        {
            if (_fontlar.TryGetValue(f, out var x)) return x;
            string aile = f.Aile == YaziTipi.Sta && FontCozucu.StaDosyalari().Count == 0 ? YaziTipi.Sabit : f.Aile;
            x = new XFont(aile, Math.Max(0.01, f.Boy), f.Kalin ? XFontStyleEx.Bold : XFontStyleEx.Regular,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
            return _fontlar[f] = x;
        }

        private static XColor R(Color c) => XColor.FromArgb(c.A, c.R, c.G, c.B);

        public void Kaydet() => _durum.Push(_g.Save());
        public void GeriYukle() => _g.Restore(_durum.Pop());
        public void Tasi(float dx, float dy) => _g.TranslateTransform(dx, dy);
        public void Olcekle(float s) => _g.ScaleTransform(s);
        public void Kirp(RectangleF r) => _g.IntersectClip(new XRect(r.X, r.Y, r.Width, r.Height));

        public void Cizgi(Color renk, float kalinlik, float x1, float y1, float x2, float y2, bool kesik = false)
        {
            var p = new XPen(R(renk), kalinlik);
            if (kesik) p.DashStyle = XDashStyle.Dash;
            _g.DrawLine(p, x1, y1, x2, y2);
        }

        public void Dikdortgen(RectangleF r, Color? dolgu, Color? cerceve = null, float kalinlik = 0)
        {
            var xr = new XRect(r.X, r.Y, r.Width, r.Height);
            if (dolgu.HasValue) _g.DrawRectangle(new XSolidBrush(R(dolgu.Value)), xr);
            if (cerceve.HasValue) _g.DrawRectangle(new XPen(R(cerceve.Value), kalinlik), xr);
        }

        public void Poligon(PointF[] n, Color dolgu) =>
            _g.DrawPolygon(new XSolidBrush(R(dolgu)), n.Select(p => new XPoint(p.X, p.Y)).ToArray(), XFillMode.Winding);

        public void Daire(PointF m, float r, Color? dolgu, Color? cerceve, float kalinlik)
        {
            var xr = new XRect(m.X - r, m.Y - r, 2 * r, 2 * r);
            if (dolgu.HasValue) _g.DrawEllipse(new XSolidBrush(R(dolgu.Value)), xr);
            if (cerceve.HasValue) _g.DrawEllipse(new XPen(R(cerceve.Value), kalinlik), xr);
        }

        public void CokluCizgi(PointF[] n, Color renk, float kalinlik) =>
            _g.DrawLines(new XPen(R(renk), kalinlik), n.Select(p => new XPoint(p.X, p.Y)).ToArray());

        private XFont FontParca(YaziTipi f, bool sembol) =>
            sembol && f.Aile != YaziTipi.Sta ? Font(new YaziTipi(CizimUzanti.SembolAilesi, f.Boy, false)) : Font(f);

        public void Yazi(string m, YaziTipi f, Color renk, float x, float y)
        {
            if (string.IsNullOrEmpty(m)) return;
            var firca = new XSolidBrush(R(renk));
            foreach (var (p, s) in CizimUzanti.Parcala(m))
            {
                var font = FontParca(f, s);
                _g.DrawString(p, font, firca, new XRect(x, y, 0, 0), XStringFormats.TopLeft);
                x += (float)_g.MeasureString(p, font).Width;
            }
        }

        public SizeF Olc(string m, YaziTipi f)
        {
            if (string.IsNullOrEmpty(m)) return new SizeF(0, f.Boy);
            float w = 0, h = 0;
            foreach (var (p, s) in CizimUzanti.Parcala(m))
            {
                var o = _g.MeasureString(p, FontParca(f, s));
                w += (float)o.Width; h = Math.Max(h, (float)o.Height);
            }
            return new SizeF(w, h);
        }

        public void Resim(Image resim, RectangleF r)
        {
            if (!Resimler.TryGetValue(resim, out var x))
            {
                var ms = new MemoryStream();
                resim.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                Resimler[resim] = x = XImage.FromStream(ms);
            }
            _g.DrawImage(x, r.X, r.Y, r.Width, r.Height);
        }

        public void Baglanti(RectangleF r, int hedefSayfa)
        {
            // dünya birimi → PDF sayfa koordinatı (alt-sol köşe esaslı)
            var pdfR = _g.Transformer.WorldToDefaultPage(new XRect(r.X, r.Y, r.Width, r.Height));
            _sayfa.AddDocumentLink(new PdfRectangle(pdfR), hedefSayfa);
        }

        public void Dispose() => _g.Dispose();
    }

    /// <summary>PDFsharp yazı tipi çözücü: Windows yazı tipleri + STA4CAD Coursta.</summary>
    public sealed class FontCozucu : IFontResolver
    {
        private static bool _kuruldu;
        private static readonly string Win = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        private static readonly Dictionary<string, (string Normal, string Kalin)> Dosyalar = new(StringComparer.OrdinalIgnoreCase)
        {
            [YaziTipi.Arayuz] = ("segoeui.ttf", "segoeuib.ttf"),
            [YaziTipi.Oransal] = ("arial.ttf", "arialbd.ttf"),
            [YaziTipi.Sabit] = ("cour.ttf", "courbd.ttf"),
            [CizimUzanti.SembolAilesi] = ("seguisym.ttf", "seguisym.ttf"),
        };

        public static List<string> StaDosyalari() =>
            new[] { @"C:\Sta4V14\Coursta.ttf", @"C:\Sta4V14\Courstabd.ttf" }.Where(File.Exists).ToList();

        public static void Kur()
        {
            if (_kuruldu) return;
            GlobalFontSettings.FontResolver = new FontCozucu();
            _kuruldu = true;
        }

        public FontResolverInfo ResolveTypeface(string aile, bool kalin, bool italik)
        {
            if (aile.Equals(YaziTipi.Sta, StringComparison.OrdinalIgnoreCase))
                return new FontResolverInfo(kalin && StaDosyalari().Count > 1 ? "sta|1" : "sta|0");
            if (!Dosyalar.ContainsKey(aile)) aile = YaziTipi.Oransal;
            return new FontResolverInfo($"{aile}|{(kalin ? 1 : 0)}");
        }

        public byte[] GetFont(string yuz)
        {
            var p = yuz.Split('|');
            bool kalin = p[1] == "1";
            if (p[0] == "sta")
            {
                var sta = StaDosyalari();
                return File.ReadAllBytes(kalin ? sta[^1] : sta[0]);
            }
            var d = Dosyalar[p[0]];
            return File.ReadAllBytes(Path.Combine(Win, kalin ? d.Kalin : d.Normal));
        }
    }
}
