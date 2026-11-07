var b = File.ReadAllBytes(args[0]);
string B(int o) { int d = BitConverter.ToInt16(b, o), s = BitConverter.ToInt16(b, o + 2); return d >= 6 && d <= 40 && s > 0 && s < 200 ? $"ø{d}/{s}" : "-"; }
for (int r = 0; r < b.Length / 250; r++) { int o = r * 250 + 96; Console.WriteLine($"{r + 1,3} X düz {B(o)} pil {B(o + 6)} sol {B(o + 10)} sağ {B(o + 14)} | Y düz {B(o + 18)} pil {B(o + 24)} sağ {B(o + 28)} sol {B(o + 32)}"); }
