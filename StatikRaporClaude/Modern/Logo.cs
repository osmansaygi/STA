using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace StatikRaporClaude.Modern
{
    /// <summary>
    /// BEYKENT PROJE logosu. Elde yalnız siyah/beyaz çizim (Varliklar/beykent.png) olduğundan
    /// kurumsal renklerle boyanır: B işaretinin üstü ve PROJE mavi, B'nin alt bandı ve BEYKENT yeşil.
    /// </summary>
    public static class Logo
    {
        public static readonly Color Mavi = Color.FromArgb(38, 110, 190);
        public static readonly Color Yesil = Color.FromArgb(96, 182, 68);
        private static Bitmap _renkli;

        public static Bitmap Renkli()
        {
            if (_renkli != null) return _renkli;
            string yol = Path.Combine(AppContext.BaseDirectory, "Varliklar", "beykent.png");
            if (!File.Exists(yol)) return null;
            using var kaynak = new Bitmap(yol);
            var bmp = new Bitmap(kaynak.Width, kaynak.Height, PixelFormat.Format32bppArgb);
            var r = new Rectangle(0, 0, kaynak.Width, kaynak.Height);
            var kd = kaynak.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var hd = bmp.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var px = new byte[kd.Stride * kaynak.Height];
            Marshal.Copy(kd.Scan0, px, 0, px.Length);
            float sx = kaynak.Width / 3118f, sy = kaynak.Height / 408f;
            int W = kaynak.Width, H = kaynak.Height, isaretX = (int)(375 * sx);
            bool Koyu(int x, int y) { int i = y * kd.Stride + x * 4; return px[i + 3] > 128 && (px[i] + px[i + 1] + px[i + 2]) / 3 < 160; }
            // B işaretinin alt parçası kavisli beyaz boşlukla ayrılır; düz bir y sınırı kavisli ucu mavi bırakır.
            // Alt parça sol alttan taşma doldurmayla bulunur, kenar yumuşatma için 2 piksel genişletilir.
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
                    Color c = x < isaretX ? (genis[y * W + x] ? Yesil : Mavi) : x < 2000 * sx ? Yesil : Mavi;
                    px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = (byte)a;
                }
            Marshal.Copy(px, 0, hd.Scan0, px.Length);
            kaynak.UnlockBits(kd);
            bmp.UnlockBits(hd);
            return _renkli = bmp;
        }
    }
}
