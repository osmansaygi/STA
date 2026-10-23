using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

class CheckOverride
{
    static void Main(string[] args)
    {
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var type = assembly.GetType("ST4PlanIdCiz.StaToolIcons", true);
        using (var loaded = (Bitmap)type.GetMethod("ForCommand").Invoke(null, new object[] { "SUREKLITEMELACILIMLARI" }))
        using (var source = new Bitmap(args[1]))
        using (var expected = (Bitmap)type.GetMethod("Fit32", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { source }))
        {
            if (loaded.Width != expected.Width || loaded.Height != expected.Height) throw new Exception("Size differs");
            for (int y = 0; y < loaded.Height; y++)
                for (int x = 0; x < loaded.Width; x++)
                    if (loaded.GetPixel(x,y).ToArgb() != expected.GetPixel(x,y).ToArgb())
                        throw new Exception("Override differs at " + x + "," + y);
            loaded.Save(args[2], ImageFormat.Png);
            Console.WriteLine("PASS: " + assembly.GetName().Name + " ForCommand reads installed Monolit override; every pixel matches.");
        }
    }
}
