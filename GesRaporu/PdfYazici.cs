using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace GesRaporu
{
    /// <summary>Segoe UI (Windows yazı tipi) çözücü — StatikRaporClaude ile aynı aile.</summary>
    public sealed class YaziTipiCozucu : IFontResolver
    {
        private static bool _kuruldu;
        private static readonly string Win = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        public static void Kur()
        {
            if (_kuruldu) return;
            GlobalFontSettings.FontResolver = new YaziTipiCozucu();
            _kuruldu = true;
        }

        public FontResolverInfo ResolveTypeface(string aile, bool kalin, bool italik) => new($"segoe|{(kalin ? 1 : 0)}|{(italik ? 1 : 0)}");

        public byte[] GetFont(string yuz)
        {
            var p = yuz.Split('|');
            bool k = p[1] == "1", i = p[2] == "1";
            string dosya = k && i ? "segoeuiz.ttf" : k ? "segoeuib.ttf" : i ? "segoeuii.ttf" : "segoeui.ttf";
            return File.ReadAllBytes(Path.Combine(Win, dosya));
        }
    }

    /// <summary>Kurumsal renkler (StatikRaporClaude ModernSayfa ile aynı).</summary>
    public static class Renk
    {
        public static readonly XColor Lacivert = XColor.FromArgb(22, 40, 72);
        public static readonly XColor Kobalt = XColor.FromArgb(38, 99, 184);
        public static readonly XColor Cizgi = XColor.FromArgb(214, 220, 228);
        public static readonly XColor Serit = XColor.FromArgb(246, 248, 251);
        public static readonly XColor Gri = XColor.FromArgb(105, 112, 122);
        public static readonly XColor Uygun = XColor.FromArgb(30, 140, 70);
        public static readonly XColor Uygunsuz = XColor.FromArgb(200, 40, 40);
        public static readonly XColor Turuncu = XColor.FromArgb(226, 122, 36);
        public static readonly XColor Koyu = XColor.FromArgb(25, 30, 38);
        public static readonly XColor BaslikZemin = XColor.FromArgb(234, 237, 241);
        public static readonly XColor Ince = XColor.FromArgb(205, 210, 217);
        public static readonly XColor Vurgu = XColor.FromArgb(255, 236, 205);
        public static readonly XColor LogoMavi = XColor.FromArgb(38, 110, 190);
        public static readonly XColor LogoYesil = XColor.FromArgb(96, 182, 68);
        public static XBrush F(XColor c) => new XSolidBrush(c);
    }

    /// <summary>BEYKENT PROJE logosu: siyah/beyaz çizim kurumsal mavi/yeşil ile boyanır (StatikRaporClaude Logo.cs).</summary>
    public static class Logo
    {
        private static byte[] _png;

        public static XImage Al()
        {
            if (_png == null)
            {
                string yol = Path.Combine(AppContext.BaseDirectory, "Varliklar", "beykent.png");
                if (!File.Exists(yol)) return null;
                using var kaynak = new System.Drawing.Bitmap(yol);
                using var bmp = new System.Drawing.Bitmap(kaynak.Width, kaynak.Height, PixelFormat.Format32bppArgb);
                var r = new System.Drawing.Rectangle(0, 0, kaynak.Width, kaynak.Height);
                var kd = kaynak.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var hd = bmp.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                var px = new byte[kd.Stride * kaynak.Height];
                Marshal.Copy(kd.Scan0, px, 0, px.Length);
                float sx = kaynak.Width / 3118f, sy = kaynak.Height / 408f;
                int W = kaynak.Width, H = kaynak.Height, isaretX = (int)(375 * sx);
                bool Koyu(int x, int y) { int i = y * kd.Stride + x * 4; return px[i + 3] > 128 && (px[i] + px[i + 1] + px[i + 2]) / 3 < 160; }
                // B işaretinin alt parçası (kavisli beyaz boşlukla ayrılan bölge): sol alttan taşma doldurma
                var yesil = new bool[W * H];
                var kuyruk = new Queue<(int, int)>();
                int tx = (int)(40 * sx), ty = (int)(345 * sy);
                if (Koyu(tx, ty)) { yesil[ty * W + tx] = true; kuyruk.Enqueue((tx, ty)); }
                while (kuyruk.Count > 0)
                {
                    var (cx, cy) = kuyruk.Dequeue();
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= isaretX || ny >= H || yesil[ny * W + nx] || !Koyu(nx, ny)) continue;
                        yesil[ny * W + nx] = true;
                        kuyruk.Enqueue((nx, ny));
                    }
                }
                // kenar yumuşatma pikselleri için maskeyi 2 piksel genişlet
                var genis = (bool[])yesil.Clone();
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < isaretX; x++)
                        if (yesil[y * W + x])
                            for (int dy = -2; dy <= 2; dy++)
                                for (int dx = -2; dx <= 2; dx++)
                                {
                                    int nx = x + dx, ny = y + dy;
                                    if (nx >= 0 && ny >= 0 && nx < W && ny < H) genis[ny * W + nx] = true;
                                }
                for (int y = 0; y < kaynak.Height; y++)
                    for (int x = 0; x < kaynak.Width; x++)
                    {
                        int i = y * kd.Stride + x * 4;
                        int lum = (px[i] + px[i + 1] + px[i + 2]) / 3;
                        int a = px[i + 3] * (255 - lum) / 255;
                        var c = x < isaretX ? (genis[y * W + x] ? Renk.LogoYesil : Renk.LogoMavi) : x < 2000 * sx ? Renk.LogoYesil : Renk.LogoMavi;
                        px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = (byte)a;
                    }
                Marshal.Copy(px, 0, hd.Scan0, px.Length);
                kaynak.UnlockBits(kd);
                bmp.UnlockBits(hd);
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                _png = ms.ToArray();
            }
            return XImage.FromStream(new MemoryStream(_png));
        }
    }

    /// <summary>A4 dikey akışlı rapor sayfası; StatikRaporClaude modern rapor görünümü (pt birimi).</summary>
    public sealed class PdfYazici
    {
        public const string Aile = "Segoe UI";
        public const double Mm = 72 / 25.4;
        public readonly PdfDocument Belge = new();
        public XGraphics Gfx { get; private set; }
        public PdfPage Sayfa { get; private set; }
        public double Y;
        public const double Sol = 14 * Mm, Sag = 14 * Mm, Ust = 33 * Mm, Alt = 18 * Mm;
        public double Genislik => Sayfa.Width.Point - Sol - Sag;
        public double AltSinir => Sayfa.Height.Point - Alt;

        // Üst/alt bilgi
        public string ProjeAdi = "", AltBaslik = "", Firma = "", Tarih = "", BlokAdi = "";
        private readonly List<PdfPage> _numarali = new();
        private string _bolum = "";
        private readonly List<(string Baslik, int Seviye, PdfPage Sayfa)> _bolumler = new();
        private (PdfPage Sayfa, double Y, double H)? _icindekiler;
        private int _serit;

        /// <summary>Rapor gövdesi punto ölçeği (çağrılar 11 pt verir → 9 pt Segoe UI).</summary>
        public static XFont F(double boy, bool kalin = false, bool italik = false) =>
            new(Aile, boy * 0.82, kalin && italik ? XFontStyleEx.BoldItalic : kalin ? XFontStyleEx.Bold : italik ? XFontStyleEx.Italic : XFontStyleEx.Regular);

        /// <summary>Ölçeksiz punto.</summary>
        public static XFont P(double pt, bool kalin = false, bool italik = false) =>
            new(Aile, pt, kalin && italik ? XFontStyleEx.BoldItalic : kalin ? XFontStyleEx.Bold : italik ? XFontStyleEx.Italic : XFontStyleEx.Regular);

        public PdfYazici() { YaziTipiCozucu.Kur(); }

        public void YeniSayfa(bool numarali = true)
        {
            Gfx?.Dispose();
            Sayfa = Belge.AddPage();
            Sayfa.Size = PdfSharp.PageSize.A4;
            Gfx = XGraphics.FromPdfPage(Sayfa);
            Y = numarali ? Ust : 0;
            _serit = 0;
            if (numarali) _numarali.Add(Sayfa);
        }

        public void Yer(double h) { if (Gfx == null || Y + h > AltSinir) YeniSayfa(); }
        public void Bosluk(double h = 6) => Y += h;

        public void Metin(string m, XFont f, XColor c, double x, double y, double w = 0, XStringFormat hiza = null)
        {
            if (string.IsNullOrEmpty(m)) return;
            if (w <= 0) { Gfx.DrawString(m, f, Renk.F(c), x, y + f.Size); return; }
            Gfx.DrawString(m, f, Renk.F(c), new XRect(x, y, w, f.Size * 1.3), hiza ?? XStringFormats.TopLeft);
        }

        /// <summary>Bölüm (boy ≥ 12: turuncu çubuklu ana başlık), alt bölüm (11) ve ara başlık (altı çizili).</summary>
        public void Baslik(string metin, double boy = 12, bool alticizili = false)
        {
            _serit = 0;
            if (boy >= 12)
            {
                Yer(70);
                if (Y > Ust + 1) Bosluk(10);
                Gfx.DrawRectangle(Renk.F(Renk.Turuncu), Sol, Y + 2, 3.1, 16);
                Metin(metin, P(13, true), Renk.Lacivert, Sol + 9, Y);
                _bolum = metin;
                if (!metin.StartsWith("İÇİNDEKİLER")) _bolumler.Add((metin, 0, Sayfa));
                Y += 26;
            }
            else if (!alticizili)
            {
                Yer(50);
                Bosluk(5);
                Metin(metin, P(10, true), Renk.Kobalt, Sol, Y);
                Gfx.DrawLine(new XPen(Renk.Cizgi, 0.6), Sol, Y + 16, Sol + Genislik, Y + 16);
                if (System.Text.RegularExpressions.Regex.IsMatch(metin, @"^\d+\.\d+\. ")) _bolumler.Add((metin, 1, Sayfa));
                Y += 21;
            }
            else
            {
                Yer(40);
                Bosluk(3);
                Gfx.DrawRectangle(Renk.F(Renk.Kobalt), Sol, Y + 4.5, 4, 4);
                Metin(metin, P(9, true), Renk.Lacivert, Sol + 8, Y);
                Y += 16;
            }
        }

        public void Ortali(string metin, double boy, bool kalin = true)
        {
            Yer(boy * 2);
            Metin(metin, F(boy, kalin), Renk.Lacivert, Sol, Y, Genislik, XStringFormats.TopCenter);
            Y += boy * 1.5;
        }

        private List<string> Sar(string metin, XFont f, double gen)
        {
            var satirlar = new List<string>();
            foreach (var parca in metin.Split('\n'))
            {
                var s = "";
                foreach (var k in parca.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var d = s.Length == 0 ? k : s + " " + k;
                    if (s.Length > 0 && Gfx.MeasureString(d, f).Width > gen) { satirlar.Add(s); s = k; } else s = d;
                }
                satirlar.Add(s);
            }
            return satirlar;
        }

        /// <summary>Kelime kaydırmalı, iki yana yaslı paragraf.</summary>
        public void Paragraf(string metin, double boy = 11, bool kalin = false, bool italik = false, double girinti = 0, bool yasla = true, XBrush renk = null)
        {
            if (Gfx == null) YeniSayfa();
            var f = F(boy, kalin, italik);
            double sh = boy * 1.22, gen = Genislik - girinti;
            var brush = renk ?? Renk.F(italik ? Renk.Gri : Renk.Koyu);
            foreach (var parca in metin.Split('\n'))
            {
                var satirlar = Sar(parca, f, gen);
                for (int i = 0; i < satirlar.Count; i++)
                {
                    Yer(sh);
                    var kel = satirlar[i].Split(' ');
                    bool son = i == satirlar.Count - 1;
                    double x = Sol + girinti;
                    if (!yasla || son || kel.Length < 2)
                        Gfx.DrawString(satirlar[i], f, brush, x, Y + f.Size);
                    else
                    {
                        double top = kel.Sum(k => Gfx.MeasureString(k, f).Width);
                        double ara = (gen - top) / (kel.Length - 1);
                        foreach (var k in kel) { Gfx.DrawString(k, f, brush, x, Y + f.Size); x += Gfx.MeasureString(k, f).Width + ara; }
                    }
                    Y += sh;
                }
            }
            Y += 2;
        }

        public bool SeritAcik = true;

        private void SeritCiz(double h)
        {
            if (_serit++ % 2 == 1 && SeritAcik) Gfx.DrawRectangle(Renk.F(Renk.Serit), Sol - 3, Y, Genislik + 6, h);
        }

        /// <summary>Hesap satırı: etiket | sembol | değer (sağa yaslı) birim | açıklama — zebra şeritli.</summary>
        public void Deger(string etiket, string sembol, string deger, string birim = "", string aciklama = null, bool kalin = false,
                          double xSembol = 175, double xDeger = 300, double xAciklama = 345, double boy = 11)
        {
            double h = boy * 1.36;
            Yer(h);
            SeritCiz(h);
            var f = F(boy, kalin);
            double t = Y + (h - f.Size * 1.2) / 2;
            var renk = kalin ? Renk.Lacivert : Renk.Koyu;
            Metin(etiket, f, renk, Sol, t);
            if (!string.IsNullOrEmpty(sembol)) Metin(sembol, F(boy, kalin), Renk.Gri, Sol + xSembol, t);
            if (!string.IsNullOrEmpty(deger)) Metin(deger, F(boy, true), renk, Sol, t, xDeger, XStringFormats.TopRight);
            if (!string.IsNullOrEmpty(birim)) Metin(birim, F(boy * 0.92), Renk.Gri, Sol + xDeger + 4, t + boy * 0.05);
            if (!string.IsNullOrEmpty(aciklama)) Metin(aciklama, F(boy * 0.95, kalin), kalin ? Renk.Lacivert : Renk.Gri, Sol + xAciklama, t);
            Y += h;
        }

        /// <summary>Serbest sütunlu satır.</summary>
        public void Sutunlar(double boy, bool kalin, params (double X, string Metin)[] s)
        {
            double h = boy * 1.36;
            Yer(h);
            SeritCiz(h);
            var f = F(boy, kalin);
            double t = Y + (h - f.Size * 1.2) / 2;
            foreach (var (x, m) in s) Metin(m, f, kalin ? Renk.Lacivert : Renk.Koyu, Sol + x, t);
            Y += h;
        }

        private void Tik(double x, double y, double s, XColor c, bool dogru)
        {
            var pen = new XPen(c, s * 0.18) { LineCap = XLineCap.Round, LineJoin = XLineJoin.Round };
            if (dogru) Gfx.DrawLines(pen, new[] { new XPoint(x, y + s * 0.55), new XPoint(x + s * 0.38, y + s * 0.9), new XPoint(x + s, y + s * 0.15) });
            else { Gfx.DrawLine(pen, x + s * 0.1, y + s * 0.1, x + s * 0.9, y + s * 0.9); Gfx.DrawLine(pen, x + s * 0.9, y + s * 0.1, x + s * 0.1, y + s * 0.9); }
        }

        /// <summary>Sonuç şeridi: sol renk çubuğu + işaret + kalın metin.</summary>
        public void Sonuc(string metin, bool yeterli, double boy = 11)
        {
            if (Gfx == null) YeniSayfa();
            var renk = yeterli ? Renk.Uygun : Renk.Uygunsuz;
            var f = F(boy, true);
            var satirlar = Sar(metin, f, Genislik - 34);
            double sh = f.Size * 1.3, yuk = satirlar.Count * sh + 12;
            Yer(yuk + 10);
            Bosluk(4);
            var r = new XRect(Sol, Y, Genislik, yuk);
            Gfx.DrawRoundedRectangle(Renk.F(XColor.FromArgb(yeterli ? 236 : 252, yeterli ? 246 : 234, yeterli ? 239 : 234)), r, new XSize(6, 6));
            Gfx.DrawRectangle(Renk.F(renk), r.X, r.Y, 3.2, r.Height);
            Tik(r.X + 11, r.Y + yuk / 2 - 5, 10, renk, yeterli);
            for (int i = 0; i < satirlar.Count; i++) Metin(satirlar[i], f, renk, r.X + 28, r.Y + 6 + i * sh);
            Y += r.Height + 6;
        }

        /// <summary>Özet kutucukları: büyük değer + küçük etiket (StatikRapor Kutucuklar).</summary>
        public void Kutucuklar(IList<(string Deger, string Etiket, XColor? Renk)> k)
        {
            double ara = 8, w = (Genislik - (k.Count - 1) * ara) / k.Count, h = 38;
            Yer(h + 10);
            double x = Sol;
            foreach (var (deger, etiket, renk) in k)
            {
                var c = renk ?? Renk.Kobalt;
                Gfx.DrawRectangle(Renk.F(Renk.Serit), x, Y, w, h);
                Gfx.DrawRectangle(Renk.F(c), x, Y, 2.6, h);
                double pt = 14;
                while (pt > 8 && Gfx.MeasureString(deger, P(pt, true)).Width > w - 14) pt -= 0.5;
                Metin(deger, P(pt, true), renk ?? Renk.Lacivert, x + 10, Y + 4 + (14 - pt) * 0.5);
                Metin(etiket, P(7), Renk.Gri, x + 10, Y + 24);
                x += w + ara;
            }
            Y += h + 10;
        }

        public void Tablo(string[] basliklar, IList<string[]> satirlar, double[] genislikler, Func<int, int, bool> vurgula = null, double boy = 9.5, string[] ustBaslik = null)
        {
            double sh = boy * 1.8, toplam = genislikler.Sum(), x0 = Sol + (Genislik - toplam) / 2;
            double h = sh * (satirlar.Count + 1 + (ustBaslik != null ? 1 : 0));
            Yer(Math.Min(h + 6, AltSinir - Ust));
            var lac = new XPen(Renk.Lacivert, 0.8);
            var ince = new XPen(Renk.Ince, 0.4);
            var fb = F(boy, true);
            void BaslikSatiri(string[] h1)
            {
                Gfx.DrawRectangle(Renk.F(Renk.BaslikZemin), x0, Y, toplam, sh);
                double x = x0;
                for (int c = 0; c < genislikler.Length; c++)
                {
                    Metin(c < h1.Length ? h1[c] : "", fb, Renk.Koyu, x + 2, Y + (sh - fb.Size * 1.3) / 2, genislikler[c] - 4, XStringFormats.TopCenter);
                    x += genislikler[c];
                }
                Y += sh;
            }
            if (ustBaslik != null)
            {
                Yer(sh * 3);
                Gfx.DrawLine(lac, x0, Y, x0 + toplam, Y);
                Gfx.DrawRectangle(Renk.F(Renk.BaslikZemin), x0, Y, toplam, sh);
                Metin(ustBaslik[0], fb, Renk.Koyu, x0, Y + (sh - fb.Size * 1.3) / 2, genislikler[0], XStringFormats.TopCenter);
                Metin(ustBaslik[1], fb, Renk.Kobalt, x0 + genislikler[0], Y + (sh - fb.Size * 1.3) / 2, toplam - genislikler[0], XStringFormats.TopCenter);
                Y += sh;
                Gfx.DrawLine(ince, x0 + genislikler[0], Y, x0 + toplam, Y);
            }
            else Gfx.DrawLine(lac, x0, Y, x0 + toplam, Y);
            BaslikSatiri(basliklar);
            Gfx.DrawLine(lac, x0, Y, x0 + toplam, Y);
            for (int i = 0; i < satirlar.Count; i++)
            {
                Yer(sh);
                if (i % 2 == 1) Gfx.DrawRectangle(Renk.F(Renk.Serit), x0, Y, toplam, sh);
                double x = x0;
                for (int c = 0; c < genislikler.Length; c++)
                {
                    bool v = vurgula != null && vurgula(i, c);
                    if (v) Gfx.DrawRectangle(Renk.F(Renk.Vurgu), x, Y, genislikler[c], sh);
                    var f = F(boy, v || c == 0);
                    var m = c < satirlar[i].Length ? satirlar[i][c] : "";
                    Metin(m, f, v ? Renk.Turuncu : (c == 0 ? Renk.Lacivert : Renk.Koyu), x + 2, Y + (sh - f.Size * 1.3) / 2, genislikler[c] - 4, XStringFormats.TopCenter);
                    x += genislikler[c];
                }
                Y += sh;
                Gfx.DrawLine(ince, x0, Y, x0 + toplam, Y);
            }
            Gfx.DrawLine(lac, x0, Y, x0 + toplam, Y);
            Y += 6;
        }

        public void Cizim(double yukseklik, Action<XGraphics, XRect> ciz)
        {
            Yer(yukseklik);
            ciz(Gfx, new XRect(Sol, Y, Genislik, yukseklik));
            Y += yukseklik;
        }

        /// <summary>İçindekiler için yer ayırır; sayfa numaraları kayıtta doldurulur.</summary>
        public void IcindekilerYeri(double yukseklik)
        {
            Yer(yukseklik);
            _icindekiler = (Sayfa, Y, yukseklik);
            Y += yukseklik;
        }

        private readonly List<XPdfForm> _formlar = new();

        /// <summary>
        /// Dış PDF'in her sayfasını rapor sayfasına gömer: üstte rapor başlığı (logo, blok, n / N ile),
        /// kaynağın üst (kesUst) ve alt (kesAlt) şeritleri kırpılır, kalan içerik ölçeklenerek yerleştirilir.
        /// </summary>
        public void EkGom(string baslik, string yol, double kesUst, double kesAlt)
        {
            var form = XPdfForm.FromFile(yol);
            _formlar.Add(form);
            int n = form.PageCount;
            for (int s = 1; s <= n; s++)
            {
                form.PageNumber = s;
                YeniSayfa();
                Baslik(n > 1 ? $"{baslik}  ({s}/{n})" : baslik, 12);
                double w = form.PointWidth, h = form.PointHeight;
                double ku = s == 1 ? kesUst : 0, ka = s == n ? kesAlt : 0;
                double ch = h * (1 - ku - ka);
                double alanH = AltSinir - Y - 4;
                double o = Math.Min(Genislik / w, alanH / ch);
                double x = Sol + (Genislik - w * o) / 2;
                var kirp = new XRect(x, Y, w * o, ch * o);
                var st = Gfx.Save();
                Gfx.IntersectClip(kirp);
                Gfx.DrawImage(form, x, Y - h * ku * o, w * o, h * o);
                Gfx.Restore(st);
                Gfx.DrawRectangle(new XPen(Renk.Cizgi, 0.6), kirp);
                Y += kirp.Height + 4;
            }
        }

        public void SayfayiKapat()
        {
            Gfx?.Dispose();
            Gfx = null;
        }

        /// <summary>Üst bilgi (logo + proje), alt bilgi (firma, tarih, n / N) ve içindekiler son geçişte çizilir.</summary>
        public void Kaydet(string yol)
        {
            SayfayiKapat();
            int n = _numarali.Count;
            var logo = Logo.Al();
            for (int i = 0; i < n; i++)
            {
                var s = _numarali[i];
                using var g = XGraphics.FromPdfPage(s, XGraphicsPdfPageOptions.Append);
                double w = s.Width.Point, h = s.Height.Point;
                if (logo != null)
                {
                    double lh = 7.2 * Mm, lw = lh * logo.PixelWidth / logo.PixelHeight;
                    g.DrawImage(logo, Sol, 10 * Mm, lw, lh);
                    g.DrawString("MÜHENDİSLİK HİZMETLERİ", P(5.6), Renk.F(XColor.FromArgb(70, 70, 70)), Sol + 0.6, 18.2 * Mm + 5.6);
                }
                g.DrawString(ProjeAdi, P(9.5, true), Renk.F(Renk.Lacivert), new XRect(Sol, 10.4 * Mm, w - Sol - Sag, 14), XStringFormats.TopRight);
                g.DrawString(AltBaslik, P(7), Renk.F(Renk.Gri), new XRect(Sol, 15.3 * Mm, w - Sol - Sag, 10), XStringFormats.TopRight);
                g.DrawLine(new XPen(Renk.Kobalt, 1.4), Sol, 23.5 * Mm, w - Sag, 23.5 * Mm);
                g.DrawLine(new XPen(Renk.LogoYesil, 1.4), Sol, 24.1 * Mm, Sol + 30 * Mm, 24.1 * Mm);
                if (!string.IsNullOrEmpty(BlokAdi))
                {
                    var f = P(7.5, true);
                    double bw = g.MeasureString(BlokAdi, f).Width + 12;
                    var r = new XRect(w - Sag - bw, 25.6 * Mm, bw, 12);
                    g.DrawRoundedRectangle(Renk.F(Renk.Kobalt), r, new XSize(4, 4));
                    g.DrawString(BlokAdi, f, XBrushes.White, r, XStringFormats.Center);
                }
                g.DrawLine(new XPen(Renk.Cizgi, 0.7), Sol, h - 13.5 * Mm, w - Sag, h - 13.5 * Mm);
                g.DrawString($"Hazırlayan: {Firma}", P(6.8), Renk.F(Renk.Gri), Sol, h - 11.6 * Mm + 6.8);
                g.DrawString(Tarih, P(6.8), Renk.F(Renk.Gri), new XRect(0, h - 11.6 * Mm, w, 10), XStringFormats.TopCenter);
                g.DrawString($"{i + 1} / {n}", P(7.5, true), Renk.F(Renk.Lacivert), new XRect(Sol, h - 11.8 * Mm, w - Sol - Sag, 10), XStringFormats.TopRight);
            }

            if (_icindekiler is { } ic)
            {
                using var g = XGraphics.FromPdfPage(ic.Sayfa, XGraphicsPdfPageOptions.Append);
                double y = ic.Y;
                foreach (var (b, sev, s) in _bolumler)
                {
                    int no = _numarali.IndexOf(s) + 1;
                    var f = sev == 0 ? P(9.5, true) : P(8.5);
                    double x = Sol + (sev == 0 ? 0 : 14);
                    var c = sev == 0 ? Renk.Lacivert : Renk.Koyu;
                    g.DrawString(b, f, Renk.F(c), x, y + f.Size);
                    double tw = g.MeasureString(b, f).Width;
                    string ns = no.ToString();
                    double nw = g.MeasureString(ns, f).Width;
                    double sagX = Sol + Genislik;
                    for (double px = x + tw + 6; px < sagX - nw - 6; px += 4)
                        g.DrawEllipse(Renk.F(Renk.Ince), px, y + f.Size - 1.2, 1, 1);
                    g.DrawString(ns, f, Renk.F(c), sagX - nw, y + f.Size);
                    y += sev == 0 ? 15 : 12.5;
                    if (y > ic.Y + ic.H) break;
                }
            }
            Belge.Save(yol);
            foreach (var f in _formlar) f.Dispose();
            _formlar.Clear();
        }

        public int NumaraliSayfa => _numarali.Count;
    }
}
