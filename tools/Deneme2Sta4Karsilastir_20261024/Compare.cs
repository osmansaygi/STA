// dotnet run Compare.cs -- A B C : biz\<h>.txt.donati.txt (DENEME2) ile ka\<h>.txt (STA4CAD ka1) donatı etiketlerini döşeme bazında karşılaştırır.
// Dönüşüm STA4 -> biz: DB-xx döşeme adı yazıları (donatılı kopya) üzerinden benzerlik uydurması.
using System.Globalization;
using System.Text.RegularExpressions;

var ci = CultureInfo.InvariantCulture;
var rx = new Regex(@"(\d+)\s*[ƒøØ]\s*(\d+)/(\d+)\s+[lL]=(\d+)");
var rxAt = new Regex(@"@(-?[\d.E-]+),(-?[\d.E-]+)(?: h[\d.]+)? r(-?\d+)");
string dir = Directory.GetCurrentDirectory();
bool detay = args.Contains("-d");
foreach (var h in args.Where(a => !a.StartsWith("-")).DefaultIfEmpty("A").ToArray())
{
    var bizT = File.ReadAllLines(Path.Combine(dir, "biz", h + ".txt.donati.txt"));
    var bizPlan = File.ReadAllLines(Path.Combine(dir, "biz", h + ".txt.plan.txt"));
    var staAll = File.ReadAllLines(Path.Combine(dir, "ka", h + ".txt"));
    var biz = new List<Lbl>();
    foreach (var l in bizT.Where(l => l.StartsWith("T ")))
    {
        var m = rx.Match(l); var at = rxAt.Match(l);
        if (!m.Success || !at.Success) { Console.WriteLine("? biz " + l); continue; }
        biz.Add(new Lbl(m, D(at, 1), D(at, 2), int.Parse(at.Groups[3].Value)));
    }
    var sta = new List<Lbl>();
    foreach (var l in staAll.Where(l => l.StartsWith("DBText|REBAR2|")))
    {
        var m = rx.Match(l); var at = rxAt.Match(l);
        if (!m.Success || !at.Success) { Console.WriteLine("? sta " + l); continue; }
        sta.Add(new Lbl(m, D(at, 1), D(at, 2), int.Parse(at.Groups[3].Value)));
    }
    double minDonX = biz.Min(b => b.X) - 1500;
    var rxSlab = new Regex(@"\|(D[A-Z]?-?\d+)\b[^@]*@(-?[\d.E-]+),(-?[\d.E-]+)");
    var bizSlab = new Dictionary<string, (double x, double y)>();
    foreach (var l in bizPlan.Where(l => l.StartsWith("DBText|DOSEME ISMI")))
    {
        var m = rxSlab.Match(l); if (!m.Success) continue;
        double x = D(m, 2), y = D(m, 3);
        if (x < minDonX) continue;
        bizSlab[m.Groups[1].Value] = (x, y);
    }
    var staSlab = new Dictionary<string, (double x, double y)>();
    foreach (var l in staAll.Where(l => l.StartsWith("DBText|LGP-SLAB3|")))
    {
        var m = rxSlab.Match(l); if (m.Success) staSlab[m.Groups[1].Value] = (D(m, 2), D(m, 3));
    }
    var pairs = staSlab.Keys.Where(bizSlab.ContainsKey).Select(k => (staSlab[k].x, staSlab[k].y, bizSlab[k].x, bizSlab[k].y)).ToList();
    var T = Fit(pairs);
    for (int it = 0; it < 3; it++)
    {
        var res = pairs.Select(p => (p, d: Dis(T, p))).OrderBy(q => q.d).ToList();
        double lim = Math.Max(60, res[res.Count / 2].d * 3);
        T = Fit(res.Where(q => q.d < lim).Select(q => q.p).ToList());
    }
    foreach (var s in sta) (s.X, s.Y) = Ap(T, s.X, s.Y);
    var staSlabT = staSlab.ToDictionary(kv => kv.Key, kv => Ap(T, kv.Value.x, kv.Value.y));
    double resMed = pairs.Select(p => Dis(T, p)).OrderBy(d => d).ElementAt(pairs.Count / 2);
    Console.WriteLine($"== OR_OTP_{h}: biz {biz.Count} etiket / {biz.Sum(b => b.N)} çubuk, STA4 {sta.Count} etiket / {sta.Sum(s => s.N)} çubuk; döşeme adı eşleşmesi {pairs.Count} (medyan sapma {resMed:0} cm, ölçek {Math.Sqrt(T.a * T.a + T.b * T.b):0.#})");
    // Etiket–etiket eşleme: aynı doğrultu (±10°), 120 cm içinde, en yakın; önce birebir aynı etiket.
    var cand = new List<(Lbl s, Lbl b, double d)>();
    foreach (var s in sta) foreach (var b in biz)
    {
        double d = Hy(s.X - b.X, s.Y - b.Y);
        int dr = Math.Abs(s.R - b.R) % 180; if (dr > 90) dr = 180 - dr;
        if (d < 150 && dr <= 10) cand.Add((s, b, d));
    }
    foreach (var c in cand.Where(c => c.s.Key == c.b.Key).OrderBy(c => c.d).Concat(cand.OrderBy(c => c.d)))
        if (c.s.M == null && c.b.M == null) { c.s.M = c.b; c.b.M = c.s; }
    int ayni = sta.Count(s => s.M != null && s.M.Key == s.Key);
    var tip = new Dictionary<string, int>();
    foreach (var s in sta.Where(s => s.M != null && s.M.Key != s.Key))
    {
        var b = s.M!;
        var f = new List<string>();
        if (s.D != b.D) f.Add("çap"); if (s.S != b.S) f.Add("aralık"); if (s.N != b.N) f.Add("adet"); if (s.L != b.L) f.Add("boy");
        string k = string.Join("+", f); tip[k] = tip.TryGetValue(k, out int c) ? c + 1 : 1;
    }
    Console.WriteLine($"   eşleşen {sta.Count(s => s.M != null)}: birebir {ayni}; farklılar: " + string.Join(", ", tip.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "=" + kv.Value)));
    Console.WriteLine($"   eşleşmeyen: STA4 {sta.Count(s => s.M == null)}, biz {biz.Count(b => b.M == null)}");
    int capAralikAyni = sta.Count(s => s.M != null && s.M.D == s.D && s.M.S == s.S);
    Console.WriteLine($"   ADET aynı {sta.Count(s => s.M != null && s.M.N == s.N)}, ±1 {sta.Count(s => s.M != null && Math.Abs(s.M.N - s.N) <= 1)} / {sta.Count(s => s.M != null)}");
    Console.WriteLine($"   çap/aralık aynı {capAralikAyni}/{sta.Count(s => s.M != null)}; adet farkı ort {sta.Where(s => s.M != null).Select(s => (double)(s.M!.N - s.N)).DefaultIfEmpty().Average():+0.0;-0.0} (biz−STA4), boy farkı ort {sta.Where(s => s.M != null).Select(s => (double)(s.M!.L - s.L)).DefaultIfEmpty().Average():+0;-0} cm");
    // Döşeme bazında: etiket en yakın döşeme adına atanır.
    string Near(double x, double y, Dictionary<string, (double, double)> d) => d.OrderBy(kv => Hy(kv.Value.Item1 - x, kv.Value.Item2 - y)).First().Key;
    var bizSlabD = bizSlab.ToDictionary(kv => kv.Key, kv => (kv.Value.x, kv.Value.y));
    if (args.Contains("-w"))
    {
        // biz adedi = ölçülen net genişlik (ST4_DENEME2_ADET_W tanısı). Aday formüllerin STA4 adedini tutturma oranı.
        var ws = sta.Where(s => s.M != null && s.M.N > 60).Select(s => (n: s.N, sp: s.S, w: (double)s.M!.N, s)).ToList();
        Console.WriteLine($"   genişlik örneği {ws.Count}");
        var forms = new List<(string ad, Func<double, int, int> f)>();
        foreach (int k in new[] { -20, -10, 0, 5, 10, 15, 20, 25, 30, 40, 50 })
        {
            int kk = k;
            forms.Add(($"floor((W-{kk})/s)+1", (w, sp) => (int)Math.Floor((w - kk) / sp) + 1));
            forms.Add(($"round((W-{kk})/s)", (w, sp) => (int)Math.Round((w - kk) / sp)));
            forms.Add(($"ceil((W-{kk})/s)", (w, sp) => (int)Math.Ceiling((w - kk) / sp)));
            forms.Add(($"floor((W-{kk})/s)", (w, sp) => (int)Math.Floor((w - kk) / sp)));
        }
        foreach (var f in forms.Select(f => (f.ad, ok: ws.Count(q => f.f(q.w, q.sp) == q.n), pm1: ws.Count(q => Math.Abs(f.f(q.w, q.sp) - q.n) <= 1))).OrderByDescending(z => z.ok).Take(6))
            Console.WriteLine($"   {f.ad,-20} tam {f.ok}/{ws.Count}  ±1 {f.pm1}");
        if (detay)
            foreach (var q in ws.OrderBy(q => q.n - q.w / q.sp))
                Console.WriteLine($"     STA4 {q.s.Txt,-18} W={q.w,5} W/s={q.w / q.sp,6:0.00} N-W/s={q.n - q.w / q.sp,6:0.00} @{q.s.X:0},{q.s.Y:0} r{q.s.R}");
        continue;
    }
    if (!detay) continue;
    foreach (var s in sta.OrderBy(s => Near(s.X, s.Y, bizSlabD)).ThenBy(s => s.R % 180).ThenBy(s => s.Y).ThenBy(s => s.X))
    {
        var b = s.M;
        string fark = b == null ? "YOK" : b.Key == s.Key ? "=" : string.Join(",", new[] { s.N != b.N ? "adet" : null, s.D != b.D ? "çap" : null, s.S != b.S ? "aralık" : null, s.L != b.L ? "boy" : null }.Where(z => z != null));
        Console.WriteLine($"   {Near(s.X, s.Y, bizSlabD),-6} r{s.R % 180,3} STA4 {s.Txt,-18} biz {(b == null ? "-" : b.Txt),-18} {fark,-14} @{s.X:0},{s.Y:0}");
    }
    foreach (var b in biz.Where(b => b.M == null).OrderBy(b => Near(b.X, b.Y, bizSlabD)))
        Console.WriteLine($"   {Near(b.X, b.Y, bizSlabD),-6} r{b.R % 180,3} STA4 {"-",-18} biz {b.Txt,-18} YALNIZ BİZ     @{b.X:0},{b.Y:0}");
}

double D(Match m, int g) => double.Parse(m.Groups[g].Value, ci);
static (double a, double b, double tx, double ty) Fit(List<(double sx, double sy, double bx, double by)> p)
{
    double msx = p.Average(q => q.sx), msy = p.Average(q => q.sy), mbx = p.Average(q => q.bx), mby = p.Average(q => q.by);
    double sxx = 0, sab = 0, sba = 0;
    foreach (var q in p) { double x = q.sx - msx, y = q.sy - msy, u = q.bx - mbx, v = q.by - mby; sxx += x * x + y * y; sab += x * u + y * v; sba += x * v - y * u; }
    double a = sab / sxx, b = sba / sxx;
    return (a, b, mbx - (a * msx - b * msy), mby - (b * msx + a * msy));
}
static (double, double) Ap((double a, double b, double tx, double ty) T, double x, double y) => (T.a * x - T.b * y + T.tx, T.b * x + T.a * y + T.ty);
static double Hy(double x, double y) => Math.Sqrt(x * x + y * y);
static double Dis((double a, double b, double tx, double ty) T, (double sx, double sy, double bx, double by) p) { var (x, y) = Ap(T, p.sx, p.sy); return Hy(x - p.bx, y - p.by); }

class Lbl
{
    public int N, D, S, L, R; public double X, Y; public Lbl? M;
    public string Key => $"{N}/{D}/{S}/{L}";
    public string Txt => $"{N}ø{D}/{S} L={L}";
    public Lbl(Match m, double x, double y, int r) { N = int.Parse(m.Groups[1].Value); D = int.Parse(m.Groups[2].Value); S = int.Parse(m.Groups[3].Value); L = int.Parse(m.Groups[4].Value); X = x; Y = y; R = ((r % 360) + 360) % 360; }
}
