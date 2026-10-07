using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

/// <summary>
/// BAPANEL ve STAPANEL komut simgeleri: 32×32, şeffaf, beyaz çizgi + ACI 94 yeşil.
/// Çıktı gömülü PNG; kullanıcı aynı adlı dosyayı %LocalAppData%\ST4Icons içine koyarsa o kullanılır.
/// </summary>
static class Program
{
    static readonly Color White = Color.FromArgb(242, 242, 242);
    static readonly Color Green = Color.FromArgb(153, 204, 51);
    static readonly Color Dim = Color.FromArgb(168, 174, 184);
    static readonly Color Sheet = Color.FromArgb(0x2D, 0x33, 0x3C);

    static int Main()
    {
        string root = FindRoot();
        string baDir = Path.Combine(root, "ST4_Yardimci", "Icons");
        string staDir = Path.Combine(root, "ST4_Plan_ID_Ciz", "Icons");
        Directory.CreateDirectory(baDir);
        Directory.CreateDirectory(staDir);

        var items = new List<Item>();
        void Add(string group, string cmd, string title, string file, Action<Ico> draw)
        {
            string dir = group == "BA" ? baDir : staDir;
            string path = Path.Combine(dir, file);
            using (var ico = new Ico())
            {
                draw(ico);
                ico.Bmp.Save(path, ImageFormat.Png);
            }
            items.Add(new Item { Group = group, Cmd = cmd, Title = title, Path = path });
        }

        Add("BA", "OTOOLCU", "Oto Ölçü", "ba_otoolcu.png", OtoOlcu);
        Add("BA", "PLANOLCU", "Plan Ölçü", "ba_planolcu.png", PlanOlcu);
        Add("BA", "MERDIVEN", "Merdiven", "ba_merdiven.png", Merdiven);
        Add("BA", "DOSEMEDONATI", "Döşeme Donatı", "ba_doseme.png", Doseme);
        Add("BA", "OTODONATI", "Oto Donatı", "ba_otodonati.png", OtoDonati);
        Add("BA", "SEHPADONATI", "Sehpa Donatısı", "ba_sehpa.png", Sehpa);
        Add("BA", "RADYEILAVE", "Radye İlave", "ba_radyeilave.png", RadyeIlave);
        Add("BA", "TEMELHATILI", "Temel Hatılı", "ba_temelhatili.png", TemelHatili);
        Add("BA", "DONATIBOYU", "Donatı Boyu", "ba_donatiboyu.png", DonatiBoyu);
        Add("BA", "DONATIMETRAJ", "Donatı Metraj", "ba_donatimetraj.png", DonatiMetraj);
        Add("BA", "DONATIADET", "Donatı Adet", "ba_donatiadet.png", DonatiAdet);
        Add("BA", "DONATIOKU", "Donatı Oku", "ba_donatioku.png", DonatiOku);
        Add("BA", "KATMANYUKLE", "Katman Yükle", "ba_katmanyukle.png", KatmanYukle);

        Add("STA", "KALIP50ST4", "Kalıp 1:50", "sta_kalip50st4.png", Kalip50);
        Add("STA", "KOLON50ST4", "Kolon aplikasyon 1:50", "sta_kolon50st4.png", Kolon50);
        Add("STA", "KOLONDUSEY", "Kolon düşey 1:50", "sta_kolondusey.png", KolonDusey);
        Add("STA", "TEMEL50ST4", "Temel planı 1:50", "sta_temel50st4.png", Temel50);
        Add("STA", "TEMELDONATI", "Radye temel donatı", "sta_temeldonati.png", TemelDonati);
        Add("STA", "KOLONDUSEY25", "Kolon düşey 1:25", "sta_kolondusey25.png", KolonDusey25);
        Add("STA", "KOLONDUSEY2", "Kolon düşey kat kat", "sta_kolondusey2.png", KolonDusey2);
        Add("STA", "KALIP100ST4", "Kalıp 1:100", "sta_kalip100st4.png", Kalip100);
        Add("STA", "KOLON100ST4", "Kolon aplikasyon 1:100", "sta_kolon100st4.png", Kolon100);
        Add("STA", "TEMEL100ST4", "Temel planı 1:100", "sta_temel100st4.png", Temel100);
        Add("STA", "ISKELECIZ", "İskele planı", "sta_iskeleciz.png", IskelePlan);
        Add("STA", "ISKELEKESIT", "İskele kesiti", "sta_iskelekesit.png", IskeleKesit);
        Add("STA", "ST4PLANID", "ST4 Plan ID", "sta_st4planid.png", PlanId);
        Add("STA", "KOLONDATA", "Kolon data", "sta_kolondata.png", KolonData);
        Add("STA", "ST4KESIT", "ST4 kesit", "sta_st4kesit.png", Kesit);
        Add("STA", "ST4KATMANTEMIZLE", "Katman temizle", "sta_st4katmantemizle.png", Katman);
        Add("STA", "DENEME1", "Deneme (2 kat)", "sta_deneme1.png", Deneme);
        Add("STA", "MANTARDOSEME", "Mantar döşeme", "sta_mantardoseme.png", Mantar);
        Add("STA", "KAPAMADETAY", "Kapama perde", "sta_kapamadetay.png", Kapama);
        Add("STA", "KOLONREVIZE", "Kolon revize", "sta_kolonrevize.png", KolonRevize);
        Add("STA", "BINAGORUNUS", "Bina görünüşü", "sta_binagorunus.png", Bina);
        Add("STA", "KIRISDUZELT", "Kiriş düzelt", "sta_kirisduzelt.png", Kiris);
        Add("STA", "TEMELKIRISDUZELT", "Temel kiriş düzelt", "sta_temelkirisduzelt.png", TemelKiris);
        Add("STA", "METRAJ", "Metraj", "sta_metraj.png", Metraj);

        string sheet = Path.Combine(root, "lisp", "ikon-ornek", "panel-yeni-simgeler.png");
        Directory.CreateDirectory(Path.GetDirectoryName(sheet));
        SaveSheet(sheet, items);
        Console.WriteLine("simge=" + items.Count + " pafta=" + sheet);
        return 0;
    }

    static string FindRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ST4_Yardimci", "ST4_Yardimci.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
    }

    sealed class Item
    {
        public string Group, Cmd, Title, Path;
    }

    sealed class Ico : IDisposable
    {
        public readonly Bitmap Bmp;
        public readonly Graphics G;
        readonly Pen _w, _g, _d;
        public Ico()
        {
            Bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            G = Graphics.FromImage(Bmp);
            G.Clear(Color.Transparent);
            G.SmoothingMode = SmoothingMode.AntiAlias;
            G.PixelOffsetMode = PixelOffsetMode.HighQuality;
            G.CompositingQuality = CompositingQuality.HighQuality;
            _w = Pen(White, 1.45f);
            _g = Pen(Green, 1.65f);
            _d = Pen(Dim, 1.15f);
        }
        static Pen Pen(Color c, float w)
        {
            return new Pen(c, w)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
        }
        public void W(float x1, float y1, float x2, float y2) => G.DrawLine(_w, x1, y1, x2, y2);
        public void Gr(float x1, float y1, float x2, float y2) => G.DrawLine(_g, x1, y1, x2, y2);
        public void D(float x1, float y1, float x2, float y2) => G.DrawLine(_d, x1, y1, x2, y2);
        public void Rw(float x, float y, float w, float h) => G.DrawRectangle(_w, x, y, w, h);
        public void Rg(float x, float y, float w, float h) => G.DrawRectangle(_g, x, y, w, h);
        public void Rd(float x, float y, float w, float h) => G.DrawRectangle(_d, x, y, w, h);
        public void Dot(float x, float y)
        {
            using (var b = new SolidBrush(Green))
                G.FillEllipse(b, x - 1.35f, y - 1.35f, 2.7f, 2.7f);
        }
        public void Tick(float x, float y)
        {
            Gr(x - 2.1f, y + 2.1f, x + 2.1f, y - 2.1f);
        }
        public void Arrow(float x1, float y1, float x2, float y2, bool green)
        {
            if (green) Gr(x1, y1, x2, y2); else W(x1, y1, x2, y2);
            float dx = x2 - x1, dy = y2 - y1;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.1f) return;
            dx /= len; dy /= len;
            float px = -dy, py = dx;
            float ax = x2 - dx * 4.2f, ay = y2 - dy * 4.2f;
            if (green)
            {
                Gr(x2, y2, ax + px * 2.1f, ay + py * 2.1f);
                Gr(x2, y2, ax - px * 2.1f, ay - py * 2.1f);
            }
            else
            {
                W(x2, y2, ax + px * 2.1f, ay + py * 2.1f);
                W(x2, y2, ax - px * 2.1f, ay - py * 2.1f);
            }
        }
        public void Badge(string text)
        {
            using (var f = new Font("Segoe UI", 6.5f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var br = new SolidBrush(Green))
            {
                G.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                var sz = G.MeasureString(text, f);
                G.DrawString(text, f, br, 31.5f - sz.Width, 31.5f - sz.Height);
            }
        }
        public void Dispose()
        {
            _w.Dispose();
            _g.Dispose();
            _d.Dispose();
            G.Dispose();
            Bmp.Dispose();
        }
    }

    static void OtoOlcu(Ico c)
    {
        c.W(8, 7, 8, 25);
        c.W(24, 7, 24, 25);
        c.Gr(8, 16, 24, 16);
        c.Tick(8, 16);
        c.Tick(24, 16);
    }

    static void PlanOlcu(Ico c)
    {
        c.Rw(6, 4, 16, 14);
        c.D(6, 11, 22, 11);
        c.D(14, 4, 14, 18);
        c.W(6, 18, 6, 24);
        c.W(22, 18, 22, 24);
        c.Gr(6, 24, 22, 24);
        c.Tick(6, 24);
        c.Tick(22, 24);
    }

    static void Merdiven(Ico c)
    {
        c.W(5, 26, 5, 21);
        c.W(5, 21, 11, 21);
        c.W(11, 21, 11, 16);
        c.W(11, 16, 17, 16);
        c.W(17, 16, 17, 11);
        c.W(17, 11, 23, 11);
        c.W(23, 11, 23, 6);
        c.W(23, 6, 28, 6);
        c.Gr(5, 21, 10, 21);
        c.Gr(11, 16, 16, 16);
        c.Gr(17, 11, 22, 11);
    }

    static void Doseme(Ico c)
    {
        c.Rw(5, 5, 22, 22);
        c.Arrow(8, 16, 24, 16, true);
        c.Arrow(16, 24, 16, 8, true);
    }

    static void OtoDonati(Ico c)
    {
        c.Rw(4, 9, 24, 14);
        c.Gr(7, 12, 25, 12);
        c.Gr(7, 20, 25, 20);
        c.D(7, 9, 7, 23);
        c.D(25, 9, 25, 23);
    }

    static void Sehpa(Ico c)
    {
        c.Rw(3, 6, 26, 18);
        c.Gr(8, 11, 24, 11);
        c.Gr(8, 11, 8, 20);
        c.Gr(24, 11, 24, 20);
        c.Gr(8, 20, 5, 20);
        c.Gr(24, 20, 27, 20);
    }

    static void RadyeIlave(Ico c)
    {
        c.Rd(4, 6, 24, 20);
        c.Gr(7, 12, 25, 12);
        c.Gr(7, 18, 20, 18);
        c.Gr(20, 18, 20, 23);
        c.Gr(20, 23, 26, 23);
    }

    static void TemelHatili(Ico c)
    {
        c.Rw(4, 4, 7, 12);
        c.Rw(21, 4, 7, 12);
        c.Rw(4, 16, 24, 9);
        c.Gr(7, 20.5f, 25, 20.5f);
    }

    static void DonatiBoyu(Ico c)
    {
        c.Gr(4, 11, 26, 11);
        c.Gr(26, 11, 26, 16);
        c.Gr(26, 16, 29, 16);
        c.Dot(8, 11);
        c.Dot(16, 11);
        c.Dot(23, 11);
        c.W(8, 22, 8, 26);
        c.W(23, 22, 23, 26);
        c.W(8, 24, 23, 24);
        c.Tick(8, 24);
        c.Tick(23, 24);
    }

    static void DonatiMetraj(Ico c)
    {
        c.Rw(4, 4, 24, 24);
        c.Gr(4, 10, 28, 10);
        c.D(4, 16, 28, 16);
        c.D(4, 21, 28, 21);
        c.D(12, 10, 12, 28);
        c.Gr(7, 7, 10, 7);
    }

    /// <summary>Yatay donatı + altta ölçü; sağda adet vurgusu.</summary>
    static void DonatiAdet(Ico c)
    {
        c.Gr(3, 10, 20, 10);
        c.Dot(6, 10);
        c.Dot(11, 10);
        c.Dot(16, 10);
        c.W(3, 18, 3, 22);
        c.W(20, 18, 20, 22);
        c.W(3, 20, 20, 20);
        c.Tick(3, 20);
        c.Tick(20, 20);
        c.Badge("N");
    }

    static void DonatiOku(Ico c)
    {
        c.Gr(3, 8, 24, 8);
        c.Gr(3, 16, 24, 16);
        c.Gr(3, 24, 24, 24);
        c.W(16, 4, 16, 28);
        c.Tick(16, 4);
        c.Tick(16, 28);
        Ring(c, 16, 8);
        Ring(c, 16, 16);
        Ring(c, 16, 24);
    }

    static void Ring(Ico c, float x, float y)
    {
        using (var pen = new Pen(Green, 1.35f))
            c.G.DrawEllipse(pen, x - 2.15f, y - 2.15f, 4.3f, 4.3f);
    }

    static void Kalip50(Ico c)
    {
        c.Rw(3, 3, 22, 20);
        c.W(3, 10, 25, 10);
        c.W(3, 17, 25, 17);
        c.W(10, 3, 10, 23);
        c.W(18, 3, 18, 23);
        c.Gr(3, 13.5f, 25, 13.5f);
        c.Badge("50");
    }

    static void Kalip100(Ico c)
    {
        c.Rw(2, 2, 24, 22);
        c.Rd(5, 5, 18, 16);
        c.W(5, 13, 23, 13);
        c.W(14, 5, 14, 21);
        c.Badge("100");
    }

    static void Kolon50(Ico c)
    {
        c.D(16, 2, 16, 26);
        c.D(2, 14, 26, 14);
        c.Rw(10, 8, 12, 12);
        c.Badge("50");
    }

    static void Kolon100(Ico c)
    {
        c.D(16, 2, 16, 24);
        c.D(3, 13, 27, 13);
        c.Rw(12, 9, 8, 8);
        c.Badge("100");
    }

    static void KolonDusey(Ico c)
    {
        c.Rw(8, 2, 14, 26);
        c.Gr(11, 4, 11, 26);
        c.Gr(19, 4, 19, 26);
        c.W(10, 8, 20, 8);
        c.W(10, 14, 20, 14);
        c.W(10, 20, 20, 20);
        c.Badge("50");
    }

    static void KolonDusey25(Ico c)
    {
        c.Rw(7, 2, 15, 24);
        c.Gr(10, 4, 10, 24);
        c.Gr(14.5f, 4, 14.5f, 24);
        c.Gr(19, 4, 19, 24);
        c.W(9, 6, 20, 6);
        c.W(9, 10, 20, 10);
        c.W(9, 14, 20, 14);
        c.W(9, 18, 20, 18);
        c.W(9, 22, 20, 22);
        c.Badge("25");
    }

    static void KolonDusey2(Ico c)
    {
        c.Rw(6, 2, 12, 11);
        c.Gr(9, 3, 9, 12);
        c.Gr(15, 3, 15, 12);
        c.W(8, 7, 16, 7);
        c.Rw(6, 16, 12, 11);
        c.Gr(9, 17, 9, 26);
        c.Gr(15, 17, 15, 26);
        c.W(8, 21, 16, 21);
        c.Badge("2");
    }

    static void Temel50(Ico c)
    {
        c.Rw(3, 14, 26, 10);
        c.Rw(6, 6, 6, 8);
        c.Rw(20, 6, 6, 8);
        c.Badge("50");
    }

    static void Temel100(Ico c)
    {
        c.Rw(4, 15, 22, 8);
        c.Rw(12, 8, 6, 7);
        c.Badge("100");
    }

    static void TemelDonati(Ico c)
    {
        c.Rw(3, 8, 26, 16);
        c.Gr(6, 12, 26, 12);
        c.Gr(6, 16, 26, 16);
        c.Gr(6, 20, 26, 20);
        c.Gr(10, 10, 10, 22);
        c.Gr(16, 10, 16, 22);
        c.Gr(22, 10, 22, 22);
    }

    static void IskelePlan(Ico c)
    {
        c.W(4, 6, 4, 26);
        c.Rd(7, 6, 8, 8);
        c.Rd(16, 6, 8, 8);
        c.Rd(7, 16, 8, 8);
        c.Rd(16, 16, 8, 8);
        c.Gr(7, 10, 15, 14);
        c.Gr(15, 10, 7, 14);
    }

    static void IskeleKesit(Ico c)
    {
        c.W(5, 4, 5, 28);
        c.D(9, 6, 9, 27);
        c.D(18, 6, 18, 27);
        c.Gr(9, 10, 18, 10);
        c.Gr(9, 18, 18, 18);
        c.Gr(9, 26, 18, 26);
        c.W(9, 10, 18, 18);
    }

    static void PlanId(Ico c)
    {
        c.Rw(3, 4, 26, 24);
        c.D(3, 20, 29, 20);
        c.Gr(6, 8, 16, 8);
        c.Gr(6, 12, 13, 12);
        c.W(18, 22, 26, 22);
        c.W(18, 25, 24, 25);
    }

    static void KolonData(Ico c)
    {
        c.Rw(3, 8, 12, 12);
        c.Dot(6, 11);
        c.Dot(12, 11);
        c.Dot(6, 17);
        c.Dot(12, 17);
        c.Gr(18, 8, 28, 8);
        c.D(18, 13, 28, 13);
        c.D(18, 18, 26, 18);
        c.D(18, 23, 24, 23);
    }

    static void Kesit(Ico c)
    {
        c.Rw(8, 8, 16, 16);
        c.Arrow(2, 16, 14, 16, true);
        c.Arrow(30, 16, 18, 16, true);
    }

    static void Katman(Ico c)
    {
        c.Rw(5, 5, 18, 5);
        c.Rw(5, 13, 18, 5);
        c.Rg(5, 21, 18, 5);
        c.Gr(24, 6, 29, 11);
        c.Gr(29, 6, 24, 11);
    }

    static void KatmanYukle(Ico c)
    {
        c.Rw(4, 6, 16, 4);
        c.Rw(4, 14, 16, 4);
        c.Rg(4, 22, 16, 4);
        c.Gr(23, 16, 30, 16);
        c.Gr(26.5f, 12.5f, 26.5f, 19.5f);
    }

    static void Deneme(Ico c)
    {
        c.W(6, 27, 26, 27);
        c.W(8, 27, 8, 6);
        c.W(24, 27, 24, 6);
        c.W(8, 16, 24, 16);
        c.W(8, 6, 24, 6);
        c.Gr(14, 16, 14, 6);
        c.Badge("2");
    }

    static void Mantar(Ico c)
    {
        c.W(3, 8, 29, 8);
        c.W(8, 8, 12, 14);
        c.W(24, 8, 20, 14);
        c.W(12, 14, 20, 14);
        c.Rw(13, 14, 6, 13);
        c.Gr(6, 11, 26, 11);
    }

    static void Kapama(Ico c)
    {
        c.Rw(7, 3, 18, 26);
        c.Gr(11, 5, 11, 27);
        c.Gr(16, 5, 16, 27);
        c.Gr(21, 5, 21, 27);
        c.D(9, 10, 23, 10);
        c.D(9, 18, 23, 18);
    }

    static void KolonRevize(Ico c)
    {
        c.Rw(2, 9, 8, 13);
        c.D(4.5f, 11, 4.5f, 20);
        c.D(7.5f, 11, 7.5f, 20);
        c.Arrow(11, 15.5f, 17, 15.5f, true);
        c.Rw(18, 7, 11, 17);
        c.Gr(21.5f, 9, 21.5f, 22);
        c.Gr(25.5f, 9, 25.5f, 22);
    }

    static void Bina(Ico c)
    {
        c.Rw(6, 4, 20, 24);
        c.W(6, 12, 26, 12);
        c.W(6, 20, 26, 20);
        c.Rd(9, 6, 4, 4);
        c.Rd(17, 6, 4, 4);
        c.Rd(9, 14, 4, 4);
        c.Rd(17, 14, 4, 4);
        c.W(13, 22, 19, 22);
        c.W(13, 22, 13, 28);
        c.W(19, 22, 19, 28);
    }

    static void Kiris(Ico c)
    {
        c.Rw(3, 10, 26, 10);
        c.W(8, 12, 8, 18);
        c.W(8, 18, 12, 18);
        c.W(16, 12, 16, 18);
        c.W(16, 18, 20, 18);
        c.W(23, 12, 23, 18);
        c.Gr(5, 13, 27, 13);
        c.Gr(5, 17, 27, 17);
    }

    static void TemelKiris(Ico c)
    {
        c.Rw(3, 16, 8, 8);
        c.Rw(21, 16, 8, 8);
        c.Rw(3, 12, 26, 6);
        c.Gr(6, 14.5f, 26, 14.5f);
        c.D(2, 26, 30, 26);
    }

    static void Metraj(Ico c)
    {
        c.Rw(4, 3, 24, 26);
        c.Gr(4, 9, 28, 9);
        c.D(4, 15, 28, 15);
        c.D(4, 20, 28, 20);
        c.Gr(4, 24, 28, 24);
        c.D(13, 9, 13, 29);
        c.D(20, 9, 20, 29);
    }

    static void SaveSheet(string path, List<Item> items)
    {
        int rowH = 76;
        int w = 520;
        int h = 48 + items.Count * rowH + 16;
        using (var bmp = new Bitmap(w, h))
        using (var g = Graphics.FromImage(bmp))
        using (var titleF = new Font("Segoe UI", 13f, FontStyle.Bold))
        using (var f = new Font("Segoe UI", 10f))
        using (var small = new Font("Segoe UI", 8f))
        using (var white = new SolidBrush(Color.White))
        using (var green = new SolidBrush(Green))
        using (var muted = new SolidBrush(Color.FromArgb(180, 186, 196)))
        {
            g.Clear(Sheet);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.DrawString("BAPANEL + STAPANEL simgeleri", titleF, white, 16, 8);
            string prev = null;
            int y = 40;
            foreach (var it in items)
            {
                if (it.Group != prev)
                {
                    g.DrawString(it.Group == "BA" ? "BETONARME" : "STA PLAN", small, green, 16, y - 2);
                    prev = it.Group;
                }
                using (var src = Image.FromFile(it.Path))
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(src, new Rectangle(16, y + 4, 64, 64));
                }
                g.DrawString(it.Title, f, white, 96, y + 16);
                g.DrawString(it.Cmd, small, muted, 96, y + 38);
                y += rowH;
            }
            // row height was for 32; we drew 64 so expand. Rebuild with correct height.
            bmp.Save(path, ImageFormat.Png);
        }
    }
}
