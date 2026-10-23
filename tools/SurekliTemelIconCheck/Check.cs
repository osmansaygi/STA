using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

class Check
{
    static void Main(string[] args)
    {
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var type = assembly.GetType("ST4PlanIdCiz.StaMonolitIcons", true);
        type.GetField("<Enabled>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, true);
        var load = type.GetMethod("TryLoad", BindingFlags.NonPublic | BindingFlags.Static);
        string[] commands = { "KIRISACILIMLARI", "TEMELKIRISDUZELT", "SUREKLITEMELACILIMLARI" };
        using (var board = new Bitmap(570, 200))
        using (var g = Graphics.FromImage(board))
        using (var font = new Font("Segoe UI", 9))
        {
            g.Clear(Color.FromArgb(36, 42, 50));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            for (int i = 0; i < commands.Length; i++)
            using (var icon = (Bitmap)load.Invoke(null, new object[] { commands[i] }))
            {
                if (icon == null || icon.Width != 32 || icon.Height != 32) throw new Exception(commands[i]);
                int transparent = 0, visible = 0;
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                {
                    int a = icon.GetPixel(x, y).A;
                    if (a == 0) transparent++;
                    if (a > 128) visible++;
                }
                if (transparent == 0 || visible < 100) throw new Exception("Alpha/content: " + commands[i]);
                int left = 10 + i * 190;
                g.DrawImage(icon, new Rectangle(left, 10, 128, 128));
                g.DrawImage(icon, new Rectangle(left, 145, 28, 28));
                g.DrawImage(icon, new Rectangle(left + 40, 143, 32, 32));
                g.DrawString(i == 0 ? "Kiriş açılımı" : i == 1 ? "Temel kiriş düzelt" : "Sürekli temel açılımı", font, Brushes.White, left, 180);
                if (i == 2) icon.Save(Path.Combine(args[1], "icon-32.png"), ImageFormat.Png);
                Console.WriteLine("PASS: " + commands[i] + " 32x32 alpha/content");
            }
            board.Save(Path.Combine(args[1], "preview.png"), ImageFormat.Png);
        }
        type.GetField("<Enabled>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
        if (load.Invoke(null, new object[] { commands[2] }) != null) throw new Exception("Classic preference ignored");
        Console.WriteLine("PASS: classic preference respected; actual plugin resource/loader verified. AutoCAD not started.");
    }
}
