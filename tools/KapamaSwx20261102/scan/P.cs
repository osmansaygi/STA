using System;
using System.IO;
var b = File.ReadAllBytes(args[0]);
for (int r = 0; r < b.Length / 300; r++)
{
    int o = r * 300;
    Console.Write($"R{r,2}:");
    for (int i = 0; i + 4 <= 300; i += 4)
    {
        float f = BitConverter.ToSingle(b, o + i);
        if (f != 0 && Math.Abs(f) > 1e-5 && Math.Abs(f) < 1e6) Console.Write($" {i}:{f:G5}");
    }
    Console.WriteLine();
}
