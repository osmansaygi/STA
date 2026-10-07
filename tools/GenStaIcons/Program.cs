using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

/// <summary>
/// STAPANEL: her komut için 40 simge (beyaz + ACI 94 yeşil).
/// ACI 94 ≈ RGB(153,204,51).
/// </summary>
static class Program
{
    static readonly Color Green = Color.FromArgb(153, 204, 51);
    static readonly Color White = Color.FromArgb(0xF2, 0xF2, 0xF2);
    static readonly Color Dim = Color.FromArgb(0xA8, 0xA8, 0xA8);
    static readonly Color SheetBg = Color.FromArgb(0x2D, 0x33, 0x3C);
    static readonly Color Card = Color.FromArgb(0x3A, 0x41, 0x4C);

    static void Px(Bitmap b, int x, int y, Color c)
    {
        if (x >= 0 && y >= 0 && x < b.Width && y < b.Height) b.SetPixel(x, y, c);
    }
    static void Box(Bitmap b, int x, int y, int w, int h, Color c)
    {
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++) Px(b, xx, yy, c);
    }
    static void H(Bitmap b, int x, int y, int w, Color c) { for (int i = 0; i < w; i++) Px(b, x + i, y, c); }
    static void V(Bitmap b, int x, int y, int h, Color c) { for (int i = 0; i < h; i++) Px(b, x, y + i, c); }
    static void Rect(Bitmap b, int x, int y, int w, int h, Color c)
    {
        H(b, x, y, w, c); H(b, x, y + h - 1, w, c); V(b, x, y, h, c); V(b, x + w - 1, y, h, c);
    }
    static void Dot(Bitmap b, int x, int y, Color c) { Box(b, x, y, 2, 2, c); }
    static Bitmap New32()
    {
        var b = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b)) g.Clear(Color.Transparent);
        return b;
    }
    static void ScaleNN(Bitmap src, Bitmap dst)
    {
        using (var g = Graphics.FromImage(dst))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.Clear(Color.Transparent);
            g.DrawImage(src, new Rectangle(0, 0, dst.Width, dst.Height));
        }
    }

    // ---- family drawers: base motif + variant index 0..39 ----
    static Bitmap FamKalip(int v)
    {
        var b = New32();
        Rect(b, 4, 4, 24, 24, White);
        int n = 2 + (v % 5);
        for (int i = 0; i < n; i++) H(b, 7, 8 + i * (16 / n), 18, i == v % n ? Green : Dim);
        for (int i = 0; i < n; i++) V(b, 8 + i * (16 / n), 7, 18, (i + 1) % n == v % n ? Green : White);
        if (v >= 20) Dot(b, 15, 15, Green);
        return b;
    }
    static Bitmap FamKolonAplik(int v)
    {
        var b = New32();
        int s = 6 + (v % 4);
        Rect(b, 16 - s / 2, 16 - s / 2, s, s, White);
        Box(b, 16 - s / 2 + 2, 16 - s / 2 + 2, Math.Max(2, s - 4), Math.Max(2, s - 4), Green);
        if (v % 3 == 0) { H(b, 4, 16, 6, Dim); H(b, 22, 16, 6, Dim); V(b, 16, 4, 6, Dim); V(b, 16, 22, 6, Dim); }
        if (v % 5 == 1) Rect(b, 6, 6, 20, 20, Dim);
        return b;
    }
    static Bitmap FamKolonDusey(int v)
    {
        var b = New32();
        Rect(b, 10, 3, 12, 26, White);
        int bars = 2 + (v % 3);
        for (int i = 0; i < bars; i++)
            V(b, 12 + i * (8 / bars), 5, 22, i == 0 ? Green : Dim);
        for (int i = 0; i < 3 + v % 4; i++)
            H(b, 12, 6 + i * 5, 8, (v + i) % 2 == 0 ? Green : Dim);
        if (v >= 25) { Box(b, 10, 3, 12, 4, Green); Box(b, 10, 25, 12, 4, Green); }
        return b;
    }
    static Bitmap FamTemelPlan(int v)
    {
        var b = New32();
        Rect(b, 3, 10, 26, 14, White);
        for (int i = 0; i < 3 + v % 3; i++)
            H(b, 5, 13 + i * 3, 22, i == 1 ? Green : Dim);
        if (v % 4 == 0) { Box(b, 8, 6, 6, 6, Dim); Box(b, 18, 6, 6, 6, Dim); }
        if (v % 5 == 2) Rect(b, 10, 8, 12, 18, Green);
        return b;
    }
    static Bitmap FamTemelDonati(int v)
    {
        var b = New32();
        Rect(b, 4, 10, 24, 12, White);
        H(b, 6, 12, 20, Green);
        H(b, 6, 19, 20, v % 2 == 0 ? Green : Dim);
        if (v % 3 == 0) { V(b, 8, 12, 4, Green); V(b, 23, 16, 4, Green); }
        if (v % 4 == 1) for (int i = 0; i < 5; i++) Dot(b, 8 + i * 4, 15, Green);
        return b;
    }
    static Bitmap FamIskelePlan(int v)
    {
        var b = New32();
        V(b, 8, 4, 24, White); V(b, 23, 4, 24, White);
        for (int i = 0; i < 3 + v % 3; i++)
            H(b, 8, 8 + i * 6, 16, i == 1 ? Green : Dim);
        if (v % 2 == 0) { V(b, 15, 6, 20, Dim); }
        return b;
    }
    static Bitmap FamIskeleKesit(int v)
    {
        var b = New32();
        for (int i = 0; i < 4; i++) H(b, 5, 6 + i * 6, 22, White);
        V(b, 15, 6, 22, Green);
        if (v % 3 == 0) V(b, 10, 6, 22, Dim);
        if (v % 3 == 1) V(b, 20, 6, 22, Dim);
        return b;
    }
    static Bitmap FamPlanId(int v)
    {
        var b = New32();
        Rect(b, 5, 5, 22, 22, White);
        H(b, 8, 12, 16, Green); V(b, 16, 8, 16, Green);
        if (v % 4 == 0) Dot(b, 10, 10, Green);
        if (v % 4 == 1) Rect(b, 10, 10, 12, 12, Dim);
        if (v % 2 == 0) for (int i = 0; i < 3; i++) H(b, 8, 18 + i * 2, 10, Dim);
        return b;
    }
    static Bitmap FamKolonData(int v)
    {
        var b = New32();
        for (int i = 0; i < 4; i++) H(b, 6, 6 + i * 6, 20, i == 0 ? Green : White);
        Dot(b, 8, 8, Green); Dot(b, 14, 14, Green); Dot(b, 20, 20, v % 2 == 0 ? Green : Dim);
        return b;
    }
    static Bitmap FamKesit(int v)
    {
        var b = New32();
        Rect(b, 4, 8, 24, 16, White);
        H(b, 4, 15, 24, Green);
        if (v % 3 == 0) { V(b, 10, 10, 12, Dim); V(b, 22, 10, 12, Dim); }
        if (v % 2 == 1) Box(b, 12, 11, 8, 8, Color.FromArgb(80, Green));
        return b;
    }
    static Bitmap FamKatmanTemizle(int v)
    {
        var b = New32();
        Box(b, 6, 6, 20, 5, White);
        Box(b, 6, 13, 20, 5, Dim);
        Box(b, 6, 20, 20, 5, Green);
        if (v % 2 == 0) { // X sil
            for (int i = 0; i < 8; i++) { Px(b, 12 + i, 12 + i, Green); Px(b, 19 - i, 12 + i, Green); }
        }
        return b;
    }
    static Bitmap FamDeneme(int v)
    {
        var b = New32();
        Rect(b, 6, 6, 20, 20, White);
        for (int i = 0; i < 2 + v % 3; i++)
            Rect(b, 8 + i, 8 + i, 16 - i * 2, 16 - i * 2, i == 0 ? Green : Dim);
        return b;
    }
    static Bitmap FamKapamaPerde(int v)
    {
        var b = New32();
        Rect(b, 6, 4, 20, 24, White);
        for (int i = 0; i < 3 + v % 3; i++)
            V(b, 9 + i * 5, 6, 20, i == 1 ? Green : Dim);
        if (v % 2 == 0) H(b, 8, 16, 16, Green);
        return b;
    }
    static Bitmap FamKolonRevize(int v)
    {
        var b = New32();
        Rect(b, 8, 6, 16, 20, White);
        V(b, 12, 8, 16, Green); V(b, 19, 8, 16, Dim);
        // refresh arcs as pixels
        for (int i = 0; i < 6; i++) { Px(b, 22 + (i < 3 ? 0 : 1), 8 + i, Green); }
        H(b, 20, 8, 4, Green); H(b, 20, 20, 4, Green);
        if (v % 3 == 0) Box(b, 10, 10, 4, 4, Green);
        return b;
    }
    static Bitmap FamKiris(int v)
    {
        var b = New32();
        Box(b, 3, 12, 26, 8, White);
        Box(b, 5, 14, 22, 4, Green);
        if (v % 3 == 0) { V(b, 8, 8, 6, Dim); V(b, 23, 8, 6, Dim); }
        if (v % 4 == 1) for (int i = 0; i < 4; i++) Dot(b, 8 + i * 5, 15, Dim);
        if (v >= 20) H(b, 5, 10, 22, Dim);
        return b;
    }
    static Bitmap FamTemelKiris(int v)
    {
        var b = New32();
        Rect(b, 4, 8, 24, 16, White);
        Box(b, 6, 14, 20, 6, Green);
        if (v % 2 == 0) { Box(b, 10, 4, 12, 8, Dim); }
        if (v % 3 == 1) for (int i = 0; i < 3; i++) H(b, 7, 10 + i * 4, 18, Dim);
        return b;
    }
    static Bitmap FamMetraj(int v)
    {
        var b = New32();
        Rect(b, 4, 4, 24, 24, White);
        H(b, 4, 10, 24, Green);
        for (int i = 0; i < 3; i++) H(b, 4, 14 + i * 4, 24, Dim);
        int cols = 2 + v % 3;
        for (int i = 1; i < cols; i++) V(b, 4 + i * (24 / cols), 4, 24, i == 1 ? Green : Dim);
        if (v % 4 == 0) Box(b, 6, 6, 20, 3, Green);
        return b;
    }

    delegate Bitmap Maker(int v);

    sealed class CmdDef
    {
        public string Cmd;
        public string Title;
        public Maker Make;
        public CmdDef(string cmd, string title, Maker make) { Cmd = cmd; Title = title; Make = make; }
    }

    static readonly CmdDef[] Commands = new CmdDef[]
    {
        new CmdDef("KALIP50ST4", "Kalip Plani 1:50", FamKalip),
        new CmdDef("KOLON50ST4", "Kolon Aplikasyon 1:50", FamKolonAplik),
        new CmdDef("KOLONDUSEY", "Kolon Dusey 1:50", FamKolonDusey),
        new CmdDef("TEMEL50ST4", "Temel Plani 1:50", FamTemelPlan),
        new CmdDef("TEMELDONATI", "Rd. Temel Donati", FamTemelDonati),
        new CmdDef("KOLONDUSEY25", "Kolon Dusey 1:25", FamKolonDusey),
        new CmdDef("KOLONDUSEY2", "Kolon Dusey - 2", FamKolonDusey),
        new CmdDef("KALIP100ST4", "Kalip Plani 1:100", FamKalip),
        new CmdDef("KOLON100ST4", "Kolon Aplikasyon 1:100", FamKolonAplik),
        new CmdDef("TEMEL100ST4", "Temel Plani 1:100", FamTemelPlan),
        new CmdDef("ISKELECIZ", "Iskele Plani", FamIskelePlan),
        new CmdDef("ISKELEKESIT", "Iskele Kesiti", FamIskeleKesit),
        new CmdDef("ST4PLANID", "ST4 Plan ID", FamPlanId),
        new CmdDef("KOLONDATA", "Kolon Data", FamKolonData),
        new CmdDef("ST4KESIT", "ST4 Kesit", FamKesit),
        new CmdDef("ST4KATMANTEMIZLE", "Katman temizle", FamKatmanTemizle),
        new CmdDef("DENEME1", "Deneme1", FamDeneme),
        new CmdDef("KAPAMADETAY", "Kapama Perde", FamKapamaPerde),
        new CmdDef("KOLONREVIZE", "Kolon Revize", FamKolonRevize),
        new CmdDef("KIRISDUZELT", "Kiris duzelt", FamKiris),
        new CmdDef("TEMELKIRISDUZELT", "Temel kiris duzelt", FamTemelKiris),
        new CmdDef("METRAJ", "Metraj (KSF)", FamMetraj),
    };

    static void SaveSheet(string path, string cmd, string title, Bitmap[] icons)
    {
        int cols = 5, rows = 8;
        int cellW = 200, cellH = 140;
        int pad = 20, top = 64;
        int W = pad * 2 + cols * cellW;
        int Hgt = top + pad + rows * cellH;
        using (var sheet = new Bitmap(W, Hgt, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(SheetBg);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var fTitle = new Font("Segoe UI", 16, FontStyle.Bold))
            using (var fSub = new Font("Segoe UI", 9f))
            using (var fLab = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            using (var fSmall = new Font("Segoe UI", 8f))
            using (var brInk = new SolidBrush(White))
            using (var brMute = new SolidBrush(Dim))
            using (var brG = new SolidBrush(Green))
            {
                g.DrawString(cmd + " — 40 simge", fTitle, brInk, pad, 12);
                g.DrawString(title + "  ·  beyaz + ACI 94 yeşil (#99CC33)  ·  1…40 yazın", fSub, brMute, pad, 40);
                for (int i = 0; i < 40; i++)
                {
                    int c = i % cols, r = i / cols;
                    int x = pad + c * cellW;
                    int y = top + r * cellH;
                    using (var card = new SolidBrush(Card))
                        g.FillRectangle(card, x + 2, y + 2, cellW - 8, cellH - 8);
                    g.DrawString((i + 1).ToString(), fLab, brG, x + 10, y + 8);
                    var big = new Bitmap(56, 56, PixelFormat.Format32bppArgb);
                    ScaleNN(icons[i], big);
                    g.DrawImage(big, x + 20, y + 32);
                    big.Dispose();
                    using (var miniBg = new SolidBrush(Color.FromArgb(0x28, 0x2C, 0x34)))
                        g.FillRectangle(miniBg, x + 90, y + 40, 90, 36);
                    g.DrawImage(icons[i], x + 98, y + 42);
                    g.DrawString("STA", fSmall, brMute, x + 134, y + 50);
                }
            }
            sheet.Save(path, ImageFormat.Png);
        }
    }

    static void Main()
    {
        string outDir = @"C:\Users\BEYKENT\Documents\CURSOR\Osman_donusum\lisp\ikon-ornek\sta-panel";
        Directory.CreateDirectory(outDir);
        var index = new System.Text.StringBuilder();
        index.AppendLine("STAPANEL ikon secenekleri (ACI 94 yesil)");
        index.AppendLine("Klasor: " + outDir);
        index.AppendLine();

        foreach (CmdDef cmd in Commands)
        {
            var icons = new Bitmap[40];
            for (int i = 0; i < 40; i++)
                icons[i] = cmd.Make(i);
            string file = Path.Combine(outDir, cmd.Cmd.ToLowerInvariant() + "-ikon-40.png");
            SaveSheet(file, cmd.Cmd, cmd.Title, icons);
            index.AppendLine(cmd.Cmd + "\t" + cmd.Title + "\t" + Path.GetFileName(file));
            foreach (var ic in icons) ic.Dispose();
            Console.WriteLine("OK " + cmd.Cmd);
        }

        File.WriteAllText(Path.Combine(outDir, "INDEX.txt"), index.ToString());
        Console.WriteLine("DONE " + Commands.Length + " sheets");
    }
}
