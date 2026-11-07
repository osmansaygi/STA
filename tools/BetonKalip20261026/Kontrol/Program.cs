// BETONKALIP doğrulaması: STA4 keşif (<proje>@.TXT, KSF ile aynı) kat satırları ile karşılaştırma.
// Kullanım: Kontrol <kök klasör> [x]   |   Kontrol iz <st4>
// BK_DENEME ortam değişkeni: BetonKalipProjesi.Deneme bitleri. BK_NET=1: KSF betonundan donatı kg/7850 geri eklenir (brüt karşılaştırma).
using System.Globalization; using System.Text.RegularExpressions; using ST4Yardimci;
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
var enc = System.Text.Encoding.GetEncoding(1254); var Inv = CultureInfo.InvariantCulture;
bool net = Environment.GetEnvironmentVariable("BK_NET") == "1";
if (args[0] == "iz")
{
    BetonKalipProjesi.Iz = new List<string>(); var pp = BetonKalipProjesi.Oku(args[1]);
    BetonKalipProjesi.Iz.ForEach(Console.WriteLine);
    foreach (var kv in pp.Temel) Console.WriteLine($"TEMEL {kv.Key}: {kv.Value.Beton:F3}/{kv.Value.Kalip:F3}");
    foreach (var kv in pp.Subasman) Console.WriteLine($"SUBASMAN {kv.Key}: {kv.Value.Beton:F3}/{kv.Value.Kalip:F3}");
    foreach (var kk in pp.Katlar) Console.WriteLine($"KAT {kk.Indeks} {kk.Ad}: D {kk.Doseme.Beton:F3}/{kk.Doseme.Kalip:F3} K {kk.Kiris.Beton:F3}/{kk.Kiris.Kalip:F3} C {kk.Kolon.Beton:F3}/{kk.Kolon.Kalip:F3}");
    return;
}
bool ayrinti = args.Length > 1;
var gorulen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var stat = new Dictionary<string, List<(double b, double k)>>();
var satirlar = new List<string>();
foreach (var txt in Directory.EnumerateFiles(args[0], "*@.TXT", SearchOption.AllDirectories))
{
    if (!gorulen.Add(Path.GetFullPath(txt).ToLowerInvariant())) continue;
    string st4 = Path.Combine(Path.GetDirectoryName(txt), Path.GetFileNameWithoutExtension(txt).TrimEnd('@') + ".st4");
    if (!File.Exists(st4)) continue;
    bool eski = File.GetLastWriteTime(st4) > File.GetLastWriteTime(txt).AddMinutes(5);
    { var l4 = File.ReadAllLines(st4, enc); int ia = Array.FindIndex(l4, s => s.StartsWith("/Axis data/")); bool egik = false; for (int i = ia + 1; ia >= 0 && i < l4.Length && !l4[i].StartsWith("/"); i++) if (Math.Abs(double.TryParse(l4[i].Split(',')[0], NumberStyles.Float, Inv, out var sl) ? sl : 0) > 1e-6) egik = true; if (egik && Environment.GetEnvironmentVariable("BK_DIK") == "1") continue; }
    string t = enc.GetString(File.ReadAllBytes(txt));
    int dk = t.IndexOf("DONATI METRAJI"); string tdon = dk > 0 ? t.Substring(dk) : ""; if (dk > 0) t = t.Substring(0, dk);
    // KSF satırları: "n> KAT ADI Döşeme ¡ beton¡ kalıp¡"; donatı tablosu: aynı n, son sütun toplam kg
    var refK = new Dictionary<string, (int no, double b, double k)>();
    foreach (Match m in Regex.Matches(t, @"¡\s*(\d+)>\s*(.+?)\s+(Döşeme|Kiriş|Kolon)\s*¡\s*([\d.]+)¡\s*([\d.]+)¡"))
        refK[Norm(m.Groups[2].Value) + "|" + m.Groups[3].Value] = (int.Parse(m.Groups[1].Value), double.Parse(m.Groups[4].Value, Inv), double.Parse(m.Groups[5].Value, Inv));
    var kg = new Dictionary<string, double>();
    foreach (Match m in Regex.Matches(tdon, @"¡\s*(\d+)>\s*(.+?)\s+(Döşeme|Kiriş|Kolon)\s*¡([^\n]*)"))
    {
        var c = m.Groups[4].Value.Split('¡').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (c.Count > 0 && double.TryParse(c[c.Count - 1], NumberStyles.Float, Inv, out var kgv)) kg[m.Groups[1].Value + "|" + m.Groups[3].Value] = kgv;
    }
    var mt = Regex.Match(t, @"TEMEL\s+¡\s*([\d.]+)¡\s*([\d.]+)¡");
    BetonKalipProjesi p;
    try { p = BetonKalipProjesi.Oku(st4); } catch (Exception ex) { Console.WriteLine($"HATA {st4}: {ex.Message}"); continue; }
    int ok = 0, n = 0; var fark = new List<string>();
    foreach (var kat in p.Katlar)
    {
        string ad = Norm(kat.Ad);
        foreach (var (tip, v) in new[] { ("Döşeme", kat.Doseme), ("Kiriş", kat.Kiris), ("Kolon", kat.Kolon) })
        {
            if (!refK.TryGetValue(ad + "|" + tip, out var r)) continue;
            double rb = r.b + (net && kg.TryGetValue(r.no + "|" + tip, out var kgv) ? kgv / 7850.0 : 0);
            n++;
            bool bOk = Math.Abs(v.Beton - rb) <= 0.011 + 0.001 * rb, kOk = Math.Abs(v.Kalip - r.k) <= 0.011 + 0.001 * r.k;
            if (!eski && rb > 0.5)
            {
                if (!stat.ContainsKey(tip)) stat[tip] = new List<(double, double)>();
                stat[tip].Add(((v.Beton - rb) / rb * 100, r.k > 0.5 ? (v.Kalip - r.k) / r.k * 100 : 0));
            }
            if (bOk && kOk) ok++;
            else fark.Add($"   {ad} {tip}: biz {v.Beton:F3}/{v.Kalip:F3} KSF {rb:F3}/{r.k:F2} (kg {(kg.TryGetValue(r.no + "|" + tip, out var kk2) ? kk2 : 0):F0})");
        }
    }
    double tkg = 0; { var mk = Regex.Match(tdon, @"¡\s*(?:MAT\s+)?TEMEL\s*¡([^\n]*)"); if (mk.Success) { var c = mk.Groups[1].Value.Split('¡').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(); if (c.Count > 0) double.TryParse(c[c.Count - 1], NumberStyles.Float, Inv, out tkg); } }
    double tB = p.Temel.Sum(x => x.Value.Beton), tK = p.Temel.Sum(x => x.Value.Kalip), sB = p.Subasman.Sum(x => x.Value.Beton), sK = p.Subasman.Sum(x => x.Value.Kalip);
    string tm = mt.Success ? $"TEMEL KSF {double.Parse(mt.Groups[1].Value, Inv) + tkg / 7850:F2}/{mt.Groups[2].Value} biz T {tB:F2}/{tK:F2} S {sB:F2}/{sK:F2} [{string.Join("; ", p.Temel.Concat(p.Subasman).Select(x => $"{x.Key} {x.Value.Beton:F2}/{x.Value.Kalip:F2}"))}]" : "";
    satirlar.Add($"{Path.GetFileName(st4),-18} kat hücre {ok}/{n}{(eski ? " (ST4 TXT'den yeni)" : "")}  {tm}");
    if (ayrinti) satirlar.AddRange(fark.Take(40));
}
satirlar.ForEach(Console.WriteLine);
foreach (var s in stat)
{
    var bl = s.Value.Select(x => Math.Abs(x.b)).OrderBy(x => x).ToList(); var kl = s.Value.Select(x => Math.Abs(x.k)).OrderBy(x => x).ToList();
    Console.WriteLine($"{s.Key}: n {bl.Count} | beton medyan %{bl[bl.Count / 2]:F2} p75 %{bl[bl.Count * 3 / 4]:F2} <%0.3 {bl.Count(x => x < 0.3)} | kalıp medyan %{kl[kl.Count / 2]:F2} p75 %{kl[kl.Count * 3 / 4]:F2} <%0.3 {kl.Count(x => x < 0.3)}");
}

static string Norm(string s) => Regex.Replace(s.Trim().ToUpperInvariant().Replace(" KAT", ""), @"\s+", " ").Trim();
