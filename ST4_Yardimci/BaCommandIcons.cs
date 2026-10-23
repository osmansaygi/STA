using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;

namespace ST4Yardimci
{
    internal readonly struct BaTool
    {
        public readonly string Label;
        public readonly string Cmd;
        public readonly string Resource;

        public BaTool(string label, string cmd, string resource)
        {
            Label = label;
            Cmd = cmd;
            Resource = resource;
        }
    }

    /// <summary>BAPANEL ve üst simge şeridinin ortak komut listesi ve simgeleri.</summary>
    internal static class BaCommandIcons
    {
        private static readonly System.Collections.Generic.Dictionary<string, Bitmap> MonolitIcons =
            new System.Collections.Generic.Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static bool _monolitLoaded;
        // Sprite regions in the 1122 x 1402 source artwork. Explicit bounds keep
        // the few protruding arrows/footings out of their neighboring sprites.
        private static readonly int[,] MonolitBounds = {
            { 15, 25, 270, 280 }, { 274, 15, 550, 280 }, { 565, 5, 835, 285 }, { 837, 40, 1122, 280 },
            { 15, 305, 278, 560 }, { 306, 310, 550, 560 }, { 550, 305, 840, 560 }, { 841, 320, 1122, 560 },
            { 10, 565, 309, 837 }, { 316, 560, 550, 837 }, { 560, 565, 845, 837 }, { 851, 560, 1122, 837 },
            { 10, 838, 310, 1105 }, { 315, 837, 550, 1100 }, { 551, 870, 841, 1100 }, { 842, 837, 1122, 1095 },
            { 10, 1107, 303, 1402 }, { 304, 1125, 558, 1402 }, { 560, 1125, 844, 1402 }, { 846, 1096, 1122, 1402 }
        };
        public static bool UseMonolit { get; private set; } =
            BaIni.GetB(BaIni.Read("icons.ini"), "Monolit", true);

        public static void SetMonolit(bool enabled)
        {
            UseMonolit = enabled;
            BaIni.Write("icons.ini", "Monolit=" + BaIni.B(enabled));
        }

        public static readonly BaTool[] Tools =
        {
            new BaTool("Oto Ölçü", "OTOOLCU", "ST4Yardimci.Icons.ba_otoolcu.png"),
            new BaTool("Plan Ölçü", "PLANOLCU", "ST4Yardimci.Icons.ba_planolcu.png"),
            new BaTool("Merdiven", "MERDIVEN", "ST4Yardimci.Icons.ba_merdiven.png"),
            new BaTool("Döşeme Donatı", "DOSEMEDONATI", "ST4Yardimci.Icons.ba_doseme.png"),
            new BaTool("Oto Donatı", "OTODONATI", "ST4Yardimci.Icons.ba_otodonati.png"),
            new BaTool("Sehpa Donatısı", "SEHPADONATI", "ST4Yardimci.Icons.ba_sehpa.png"),
            new BaTool("Hasır Donatı", "HASIRDONATI", "ST4Yardimci.Icons.ba_hasirdonati.png"),
            new BaTool("Radye İlave Donatı", "RADYEILAVE", "ST4Yardimci.Icons.ba_radyeilave.png"),
            new BaTool("Temel Hatılı", "TEMELHATILI", "ST4Yardimci.Icons.ba_temelhatili.png"),
            new BaTool("Parapet Detayı", "PARAPET", "ST4Yardimci.Icons.ba_parapet.png"),
            new BaTool("Temel Çukuru", "TEMELCEKURU", "ST4Yardimci.Icons.ba_temelcekuru.png"),
            new BaTool("Temel Detayı", "TEMELDETAY", "ST4Yardimci.Icons.ba_temeldetay.png"),
            new BaTool("Kuranglez", "KURANGLEZ", "ST4Yardimci.Icons.ba_kuranglez.png"),
            new BaTool("Perde Boşluk", "PERDEBOSLUK", "ST4Yardimci.Icons.ba_kuranglez.png"),
            new BaTool("Donatı Boyu", "DONATIBOYU", "ST4Yardimci.Icons.ba_donatiboyu.png"),
            new BaTool("Donatı Metraj", "DONATIMETRAJ", "ST4Yardimci.Icons.ba_donatimetraj.png"),
            new BaTool("Donatı Adet", "DONATIADET", "ST4Yardimci.Icons.ba_donatiadet.png"),
            new BaTool("Donatı Oku", "DONATIOKU", "ST4Yardimci.Icons.ba_donatioku.png"),
            new BaTool("Kot Değiştir", "KOTDEGISTIR", "ST4Yardimci.Icons.ba_kotdegistir.png"),
            new BaTool("Katman Yükle", "KATMANYUKLE", "ST4Yardimci.Icons.ba_katmanyukle.png"),
        };

        public static Image Load(string cmd, string resourceName)
        {
            Image user = TryUserOverride(cmd);
            if (user != null) return user;
            if (UseMonolit)
            {
                Image monolit = LoadMonolit(cmd);
                if (monolit != null) return monolit;
            }
            return LoadIcon(resourceName);
        }

        private static Image LoadMonolit(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            lock (MonolitIcons)
            {
                if (!_monolitLoaded)
                {
                    _monolitLoaded = true;
                    try
                    {
                        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                            "ST4Yardimci.Icons.monolit_atlas.png"))
                        {
                            if (stream == null) return null;
                            using (var atlas = new Bitmap(stream))
                            {
                                for (int i = 0; i < Math.Min(Tools.Length, MonolitBounds.GetLength(0)); i++)
                                {
                                    int x0 = MonolitBounds[i, 0] * atlas.Width / 1122, x1 = MonolitBounds[i, 2] * atlas.Width / 1122;
                                    int y0 = MonolitBounds[i, 1] * atlas.Height / 1402, y1 = MonolitBounds[i, 3] * atlas.Height / 1402;
                                    int left = x1, right = x0, top = y1, bottom = y0;
                                    // Trim each sprite's transparent gutter without changing the source asset.
                                    for (int y = y0; y < y1; y++)
                                        for (int x = x0; x < x1; x++)
                                            if (atlas.GetPixel(x, y).A >= 48)
                                            {
                                                left = Math.Min(left, x); right = Math.Max(right, x);
                                                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                                            }
                                    if (right < left || bottom < top) continue;
                                    var src = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
                                    double scale = 30.0 / Math.Max(src.Width, src.Height);
                                    int w = Math.Max(1, (int)Math.Round(src.Width * scale));
                                    int h = Math.Max(1, (int)Math.Round(src.Height * scale));
                                    var icon = new Bitmap(32, 32);
                                    using (var g = Graphics.FromImage(icon))
                                    {
                                        g.Clear(Color.Transparent);
                                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                        g.DrawImage(atlas, new Rectangle((32 - w) / 2, (32 - h) / 2, w, h), src, GraphicsUnit.Pixel);
                                    }
                                    MonolitIcons[Tools[i].Cmd] = icon;
                                }
                            }
                        }
                    }
                    catch { /* Preserve the existing icon fallback if an asset cannot be read. */ }
                }
                return MonolitIcons.TryGetValue(cmd, out Bitmap image) ? (Image)image.Clone() : null;
            }
        }

        /// <summary>%LocalAppData%\ST4Icons\KOMUT.png — AutoCAD PNG aktarımı.</summary>
        static Image TryUserOverride(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ST4Icons",
                    cmd.Trim().ToUpperInvariant() + ".png");
                if (!File.Exists(path)) return null;
                byte[] bytes = File.ReadAllBytes(path);
                using (var ms = new MemoryStream(bytes))
                using (var src = Image.FromStream(ms))
                {
                    var dst = new Bitmap(32, 32);
                    using (var g = Graphics.FromImage(dst))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, new Rectangle(0, 0, 32, 32));
                    }
                    return dst;
                }
            }
            catch
            {
                return null;
            }
        }

        static Image LoadIcon(string resourceName)
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    using (var src = Image.FromStream(stream))
                        return new Bitmap(src);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
