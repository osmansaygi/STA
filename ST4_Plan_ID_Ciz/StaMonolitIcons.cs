using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;

namespace ST4PlanIdCiz
{
    /// <summary>Presentation assets only; independent of AutoCAD drawing commands.</summary>
    internal static class StaMonolitIcons
    {
        internal static readonly string[] Commands = {
            "KALIP50ST4", "KOLON50ST4", "KOLONDUSEY", "TEMEL50ST4",
            "TEMELDONATI", "KALIP100ST4", "KOLON100ST4", "TEMEL100ST4",
            "ISKELECIZ", "ISKELEKESIT", "ST4PLANID", "KOLONDATA",
            "ST4KESIT", "ST4KATMANTEMIZLE", "DENEME1", "MANTARDOSEME",
            "KAPAMADETAY", "KOLONREVIZE", "BINAGORUNUS", "BINAKESITI",
            "KIRISACILIMLARI", "KIRISDUZELT", "TEMELKIRISDUZELT", "METRAJ"
            // SUREKLITEMELACILIMLARI: ayrı gömülü PNG (atlas sırası kaydırılmaz)
        };
        private static readonly Dictionary<string, Bitmap> Icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;
        // Regions in the original 1024 x 1536 atlas. A few footing corners and
        // dimension ticks extend beyond the nominal grid; keep those isolated.
        private static readonly int[,] Bounds = {
            { 5, 40, 274, 280 }, { 277, 60, 522, 275 }, { 573, 10, 739, 281 }, { 774, 70, 1024, 283 },
            { 5, 302, 270, 510 }, { 278, 285, 529, 514 }, { 531, 302, 778, 515 }, { 779, 290, 1024, 515 },
            { 20, 520, 253, 779 }, { 310, 520, 500, 779 }, { 515, 550, 778, 779 }, { 779, 525, 1024, 779 },
            { 10, 787, 269, 1023 }, { 278, 787, 526, 1023 }, { 531, 787, 770, 1023 }, { 772, 787, 1024, 1023 },
            { 5, 1030, 270, 1290 }, { 288, 1030, 520, 1290 }, { 533, 1040, 771, 1290 }, { 775, 1030, 1024, 1290 },
            { 5, 1310, 275, 1536 }, { 278, 1300, 523, 1536 }, { 525, 1300, 773, 1536 }, { 776, 1295, 1024, 1536 }
        };
        private static string SettingsPath => Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "ST4_Plan_ID_Ciz", "icons.ini");
        internal static bool Enabled { get; private set; } = ReadEnabled();
        internal static event Action Changed;

        private static bool ReadEnabled()
        {
            try { return !File.Exists(SettingsPath) || File.ReadAllText(SettingsPath).Trim() != "Monolit=0"; }
            catch { return true; }
        }

        internal static void Select(bool enabled)
        {
            Enabled = enabled;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, enabled ? "Monolit=1" : "Monolit=0");
            }
            catch { }
            Changed?.Invoke();
        }

        internal static Image TryLoad(string command)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(command)) return null;
            string key = command.Trim();
            if (key.Equals("KOLONDUSEY2", StringComparison.OrdinalIgnoreCase)
                || key.Equals("KOLONDUSEY25", StringComparison.OrdinalIgnoreCase)) key = "KOLONDUSEY";
            lock (Icons)
            {
                if (!_loaded)
                {
                    _loaded = true;
                    try
                    {
                        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ST4PlanIdCiz.Icons.sta_monolit_atlas.png"))
                        {
                            if (stream == null) return null;
                            using (var atlas = new Bitmap(stream))
                            {
                                for (int i = 0; i < Commands.Length; i++)
                                {
                                    int x0 = Bounds[i, 0] * atlas.Width / 1024, x1 = Bounds[i, 2] * atlas.Width / 1024;
                                    int y0 = Bounds[i, 1] * atlas.Height / 1536, y1 = Bounds[i, 3] * atlas.Height / 1536;
                                    int left = x1, right = x0, top = y1, bottom = y0;
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
                                    int w = Math.Max(1, (int)Math.Round(src.Width * scale)), h = Math.Max(1, (int)Math.Round(src.Height * scale));
                                    var icon = new Bitmap(32, 32);
                                    using (var g = Graphics.FromImage(icon))
                                    {
                                        g.Clear(Color.Transparent);
                                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                        g.DrawImage(atlas, new Rectangle((32 - w) / 2, (32 - h) / 2, w, h), src, GraphicsUnit.Pixel);
                                    }
                                    Icons[Commands[i]] = icon;
                                }
                            }
                        }
                        TryLoadExtra("SUREKLITEMELACILIMLARI", "ST4PlanIdCiz.Icons.surekli_temel_acilim_monolit.png");
                        TryLoadExtra("TEMELILAVEDENEME", "ST4PlanIdCiz.Icons.temel_ilave_deneme_monolit.png");
                        TryLoadExtra("DENEME2", "ST4PlanIdCiz.Icons.kalip_deneme2_monolit.png");
                        TryLoadExtra("SERBESTKESIT", "ST4PlanIdCiz.Icons.serbest_kesit_monolit.png");
                    }
                    catch { /* Embedded classic icons remain available as the fallback. */ }
                }
                return Icons.TryGetValue(key, out Bitmap iconImage) ? (Image)iconImage.Clone() : null;
            }
        }

        private static void TryLoadExtra(string command, string resourceName)
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return;
                    using (var src = new Bitmap(stream))
                    {
                        var icon = new Bitmap(32, 32);
                        using (var g = Graphics.FromImage(icon))
                        {
                            g.Clear(Color.Transparent);
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            double scale = 30.0 / Math.Max(src.Width, src.Height);
                            int w = Math.Max(1, (int)Math.Round(src.Width * scale));
                            int h = Math.Max(1, (int)Math.Round(src.Height * scale));
                            g.DrawImage(src, new Rectangle((32 - w) / 2, (32 - h) / 2, w, h));
                        }
                        Icons[command] = icon;
                    }
                }
            }
            catch { }
        }
    }
}
