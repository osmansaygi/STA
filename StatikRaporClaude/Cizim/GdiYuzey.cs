using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace StatikRaporClaude.Cizim
{
    /// <summary>GDI+ yüzeyi (önizleme ve PNG). Graphics dünya birimine dönüştürülmüş olarak gelir.</summary>
    public sealed class GdiYuzey : ICizimYuzeyi
    {
        private static PrivateFontCollection _staFontlari;
        private static FontFamily _sta;

        private readonly Graphics _g;
        private readonly Stack<GraphicsState> _durum = new Stack<GraphicsState>();
        private readonly Dictionary<YaziTipi, Font> _fontlar = new Dictionary<YaziTipi, Font>();
        private readonly StringFormat _fmt;

        public GdiYuzey(Graphics g)
        {
            _g = g;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            _fmt = (StringFormat)StringFormat.GenericTypographic.Clone();
            _fmt.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        }

        public static FontFamily StaAilesi()
        {
            if (_staFontlari == null)
            {
                _staFontlari = new PrivateFontCollection();
                foreach (var f in FontCozucu.StaDosyalari()) _staFontlari.AddFontFile(f);
                _sta = _staFontlari.Families.FirstOrDefault();
            }
            return _sta;
        }

        private Font Font(YaziTipi f)
        {
            if (_fontlar.TryGetValue(f, out var font)) return font;
            var stil = f.Kalin ? FontStyle.Bold : FontStyle.Regular;
            float boy = Math.Max(0.01f, f.Boy);
            if (f.Aile == YaziTipi.Sta && StaAilesi() != null)
                font = new Font(_sta, boy, _sta.IsStyleAvailable(stil) ? stil : FontStyle.Regular, GraphicsUnit.World);
            else
                font = new Font(f.Aile == YaziTipi.Sta ? YaziTipi.Sabit : f.Aile, boy, stil, GraphicsUnit.World);
            return _fontlar[f] = font;
        }

        public void Kaydet() => _durum.Push(_g.Save());
        public void GeriYukle() => _g.Restore(_durum.Pop());
        public void Tasi(float dx, float dy) => _g.TranslateTransform(dx, dy);
        public void Olcekle(float s) => _g.ScaleTransform(s, s);
        public void Kirp(RectangleF r) => _g.IntersectClip(r);

        public void Cizgi(Color renk, float kalinlik, float x1, float y1, float x2, float y2, bool kesik = false)
        {
            using var p = new Pen(renk, kalinlik);
            if (kesik) p.DashStyle = DashStyle.Dash;
            _g.DrawLine(p, x1, y1, x2, y2);
        }

        public void Dikdortgen(RectangleF r, Color? dolgu, Color? cerceve = null, float kalinlik = 0)
        {
            if (dolgu.HasValue) using (var b = new SolidBrush(dolgu.Value)) _g.FillRectangle(b, r);
            if (cerceve.HasValue) using (var p = new Pen(cerceve.Value, kalinlik)) _g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
        }

        public void Poligon(PointF[] n, Color dolgu) { using var b = new SolidBrush(dolgu); _g.FillPolygon(b, n); }

        public void Daire(PointF m, float r, Color? dolgu, Color? cerceve, float kalinlik)
        {
            if (dolgu.HasValue) using (var b = new SolidBrush(dolgu.Value)) _g.FillEllipse(b, m.X - r, m.Y - r, 2 * r, 2 * r);
            if (cerceve.HasValue) using (var p = new Pen(cerceve.Value, kalinlik)) _g.DrawEllipse(p, m.X - r, m.Y - r, 2 * r, 2 * r);
        }
        public void CokluCizgi(PointF[] n, Color renk, float kalinlik) { using var p = new Pen(renk, kalinlik); _g.DrawLines(p, n); }

        private Font FontParca(YaziTipi f, bool sembol) =>
            sembol && f.Aile != YaziTipi.Sta ? Font(new YaziTipi(CizimUzanti.SembolAilesi, f.Boy, false)) : Font(f);

        public void Yazi(string m, YaziTipi f, Color renk, float x, float y)
        {
            if (string.IsNullOrEmpty(m)) return;
            using var b = new SolidBrush(renk);
            foreach (var (p, s) in CizimUzanti.Parcala(m))
            {
                var font = FontParca(f, s);
                _g.DrawString(p, font, b, x, y, _fmt);
                x += _g.MeasureString(p, font, new PointF(0, 0), _fmt).Width;
            }
        }

        public SizeF Olc(string m, YaziTipi f)
        {
            if (string.IsNullOrEmpty(m)) return new SizeF(0, f.Boy);
            float w = 0, h = 0;
            foreach (var (p, s) in CizimUzanti.Parcala(m))
            {
                var o = _g.MeasureString(p, FontParca(f, s), new PointF(0, 0), _fmt);
                w += o.Width; h = Math.Max(h, o.Height);
            }
            return new SizeF(w, h);
        }

        public void Resim(Image resim, RectangleF r) => _g.DrawImage(resim, r);

        public void Baglanti(RectangleF r, int hedefSayfa) { }
    }
}
