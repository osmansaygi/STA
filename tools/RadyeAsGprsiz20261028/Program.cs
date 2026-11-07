using System.Globalization;
using System.Text;

var inv = CultureInfo.InvariantCulture;
string dir = args.Length > 0 ? args[0] : @"..\data";
string name = args.Length > 1 ? args[1] : "N_ULK_02";
string B(string ext) => Path.Combine(dir, name + ext);

// ---- RAD
var L = File.ReadAllLines(B(".RAD"));
var h = L[1].Split(',');
int nPlak = int.Parse(h[0]), nNode = int.Parse(h[1]), nMesh = int.Parse(h[2]);
int li = 0; while (!(L[li].Contains(":\\") || L[li].ToUpperInvariant().Contains(".ST4"))) li++;
li++; li += 2 * nPlak;
var mesh = new List<int[]>(); var meshD = new List<double>(); var meshP = new List<int>();
for (int m = 0; m < nMesh; m++, li++) { var f = L[li].Split(','); mesh.Add(f.Skip(1).Take(4).Select(int.Parse).ToArray()); meshP.Add(int.Parse(f[0])); meshD.Add(double.Parse(f[6], inv)); }
double rhoMin = double.Parse(L[3].Split(",")[0], inv);
var nodeZ = new double[nNode + 1]; var nodeP = new int[nNode + 1];
for (int n = 0; n < nNode; n++, li++) { var f = L[li].Split(','); nodeZ[n + 1] = double.Parse(f[3], inv); nodeP[n + 1] = int.Parse(f[0]); }

// ---- SO1 corners [e][case 0..16][corner 0..3][comp 0..7]
var so = File.ReadAllBytes(B(".SO1"));
float S(int e, int c, int k, int comp) => BitConverter.ToSingle(so, 4 * (e * 21 * 33 + (1 + c) * 33 + 1 + k * 8 + comp));
float Hdr(int idx) => BitConverter.ToSingle(so, 4 * idx);
Console.WriteLine($"SO1 hdr genel As: {Hdr(2):F4} {Hdr(3):F4} {Hdr(4):F4} {Hdr(5):F4}  max: {Hdr(6):F3} {Hdr(7):F3} {Hdr(8):F3} {Hdr(9):F3}");

// ---- GPR
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var G = File.Exists(B(".GPR")) ? File.ReadAllLines(B(".GPR"), Encoding.GetEncoding(1254)).ToList() : new List<string>(); if (args.Length > 2) G.AddRange(File.ReadAllLines(args[2], Encoding.UTF8));
List<double> Cells(string l) { var p = l.Split('¡'); var r = new List<double>(); for (int i = 1; i < p.Length - 1; i++) { if (!double.TryParse(p[i].Trim(), NumberStyles.Float, inv, out var v)) return null; r.Add(v); } return r; }
var gMx = new Dictionary<int, double[]>(); var gMy = new Dictionary<int, double[]>(); var gA = new Dictionary<int, double[]>();
string sec = "";
foreach (var l in G)
{
    if (l.Contains("NOKTALARIN X")) sec = "X"; else if (l.Contains("NOKTALARIN Y")) sec = "Y";
    else if (l.Contains("Asax") && l.Contains("Asuy")) sec = "A"; else if (l.Contains("TEMEL DONATI SONU")) sec = "";
    if (sec == "" || l.IndexOf('¡') < 0) continue;
    var c = Cells(l); if (c == null) continue;
    if ((sec == "X" || sec == "Y") && c.Count == 16) (sec == "X" ? gMx : gMy)[(int)c[0]] = c.Skip(1).ToArray();
    if (sec == "A" && c.Count == 14 && !gA.ContainsKey((int)c[0])) gA[(int)c[0]] = c.ToArray();
}
Console.WriteLine($"GPR: Mx {gMx.Count}, My {gMy.Count}, analiz {gA.Count}");

// ---- node -> (element, corner)
var conn = new List<(int e, int k)>[nNode + 1];
for (int i = 0; i <= nNode; i++) conn[i] = new();
for (int e = 0; e < nMesh; e++) for (int k = 0; k < 4; k++) { int n = mesh[e][k]; if (n > 0) conn[n].Add((e, k)); }
if (Environment.GetEnvironmentVariable("PLAKFILT") == "1") for (int n = 1; n <= nNode; n++) if (conn[n].Any(t => meshP[t.e] == nodeP[n])) conn[n].RemoveAll(t => meshP[t.e] != nodeP[n]);
{ int coklu = 0; for (int n = 1; n <= nNode; n++) if (conn[n].Select(t => meshP[t.e]).Distinct().Count() > 1) coklu++; Console.WriteLine($"coklu plak dugum: {coklu}"); }
int[] caseIdx = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 16 };
double[] Corner(int e, int k, int comp) => caseIdx.Select(c => (double)S(e, c, k, comp)).ToArray();
bool TriZero = Environment.GetEnvironmentVariable("TRIZERO") == "1";
double[] Avg(int n, int comp) { var r = new double[15]; foreach (var (e, k) in conn[n]) { var v = Corner(e, k, comp); if (TriZero && mesh[e][3] == 0 && ((k == 2 && comp == 3) || (k == 1 && comp == 4))) v = new double[15]; for (int i = 0; i < 15; i++) r[i] += v[i]; } for (int i = 0; i < 15; i++) r[i] /= Math.Max(1, conn[n].Count); return r; }

if (gMx.Count == 0) { for (int n = 1; n <= nNode; n++) if (conn[n].Count > 0) { gMx[n] = Avg(n, 3).Select(v => Math.Round(v, 2)).ToArray(); gMy[n] = Avg(n, 4).Select(v => Math.Round(v, 2)).ToArray(); } Console.WriteLine("GPR statik tablo yok: SO1 ortalamasi kullanildi"); }
// 1) statics check
foreach (var (comp, gm, nm) in new[] { (3, gMx, "Mx"), (4, gMy, "My") })
{
    int ok = 0, tot = 0;
    foreach (var kv in gm) { if (conn[kv.Key].Count == 0) continue; var a = Avg(kv.Key, comp); for (int i = 0; i < 15; i++) { tot++; if (Math.Abs(Math.Round(a[i], 2, MidpointRounding.AwayFromZero) - kv.Value[i]) <= 0.0101) ok++; } }
    Console.WriteLine($"Statik {nm} (SO1 kose ortalamasi): {ok}/{tot}");
}

// 2) design moment hypotheses
double[][] Cod = {
    new[]{1.4,1.6,0,0,0}, new[]{1.4,1.6,1.6,0,0}, new[]{1.0,1.2,0,0,0}, new[]{1.0,1,0,1,0}, new[]{1.0,1,1,1,0},
    new[]{.9,0,0,1,0}, new[]{1.0,1.3,0,0,1.3}, new[]{1.0,1.3,1,0,1.3}, new[]{.9,0,0,0,1.3}, new[]{.9,0,.9,0,1.3} };
List<double> Eqs(double[] m, int mode)
{
    var r = new List<double>();
    double[] e4 = { m[8], m[9], m[10], m[11] };
    switch (mode)
    {
        case 0: foreach (var e in e4) { r.Add(e); r.Add(-e); } break;
        case 1: foreach (var ex in new[] { m[8], m[9] }) foreach (var ey in new[] { m[10], m[11] }) foreach (int s1 in new[] { 1, -1 }) foreach (int s2 in new[] { 1, -1 }) { r.Add(s1 * (ex + s2 * 0.3 * ey)); r.Add(s1 * (ey + s2 * 0.3 * ex)); } break;
        case 2: foreach (var ex in new[] { m[8], m[9] }) foreach (var ey in new[] { m[10], m[11] }) foreach (int s1 in new[] { 1, -1 }) foreach (int s2 in new[] { 1, -1 }) foreach (int s3 in new[] { 1, -1 }) { r.Add(s1 * (ex + s2 * 0.3 * ey) + s3 * 0.3 * m[14]); r.Add(s1 * (ey + s2 * 0.3 * ex) + s3 * 0.3 * m[14]); } break;
        case 3: foreach (var e in e4) foreach (int s in new[] { 1, -1 }) foreach (int s3 in new[] { 1, -1 }) r.Add(s * e + s3 * 0.3 * m[14]); break;
        case 4: foreach (var e in e4) foreach (int s in new[] { 1, -1 }) foreach (int s3 in new[] { 1, -1 }) r.Add(s * e + s3 * m[14]); break;
    }
    return r;
}
(double alt, double ust) Env(double[] m, int eqMode)
{
    double mx = 0, mn = 0;
    var qs = new[] { m[1], m[2], m[3], m[4], m[5], m[6] };
    var eqs = Eqs(m, eqMode);
    var ws = new[] { m[12], -m[12], m[13], -m[13] };
    foreach (var c in Cod) foreach (var q in qs)
            foreach (var e in c[3] != 0 ? eqs : new List<double> { 0 })
                foreach (var w in c[4] != 0 ? ws : new[] { 0.0 })
                { double v = c[0] * m[0] + c[1] * q + c[2] * m[7] + c[3] * e + c[4] * w; mx = Math.Max(mx, v); mn = Math.Min(mn, v); }
    return (mx, mn);
}
// GPR analiz kolonlari: 6 Mxalt 7 Mxust 8 Myalt 9 Myust 10 Asax 11 Asux 12 Asay 13 Asuy
double[] DesA(int n, int mode) { var ax = Env(Avg(n, 3), mode); var ay = Env(Avg(n, 4), mode); return new[] { ax.alt, ax.ust, ay.alt, ay.ust }; }
double[] DesB(int n, int mode)
{
    var r = new double[4];
    foreach (var (e, k) in conn[n]) { var ax = Env(Corner(e, k, 3), mode); var ay = Env(Corner(e, k, 4), mode); r[0] += ax.alt; r[1] += ax.ust; r[2] += ay.alt; r[3] += ay.ust; }
    for (int i = 0; i < 4; i++) r[i] /= Math.Max(1, conn[n].Count); return r;
}
var designCache = new Dictionary<(int, int), double[]>();
void Score(string label, Func<int, double[]> f)
{
    int ok = 0, tot = 0; double sum = 0;
    var bad = new List<string>();
    foreach (var kv in gA)
    {
        int n = kv.Key; if (conn[n].Count == 0) continue;
        var d = f(n);
        for (int i = 0; i < 4; i++) { tot++; double diff = Math.Abs(Math.Round(d[i], 2, MidpointRounding.AwayFromZero) - kv.Value[6 + i]); sum += diff; if (diff <= 0.0101) ok++; else if (bad.Count < 6) bad.Add($"n{n}[{i}] {d[i]:F3}/{kv.Value[6 + i]:F2}"); }
    }
    Console.WriteLine($"{label,-28} {ok}/{tot}  ort|fark|={sum / tot:F4}  {string.Join("; ", bad)}");
}
for (int mode = 0; mode <= 4; mode++)
{
    int md = mode;
    Score($"A env(ort) eq{md}", n => DesA(n, md));
    Score($"B ort(env kose) eq{md}", n => DesB(n, md));
}

// 2b) bagli dugum ortalamasi: dugum tasarim momentlerinin komsu kumesinde ortalamasi
List<int> Set(int n, int kind)
{
    var r = new List<int>();
    foreach (var (e, k) in conn[n])
    {
        var nn = mesh[e].Where(x => x > 0).ToArray(); int cnt = nn.Length; int kk = Array.IndexOf(nn, n);
        if (kind == 1 || kind == 2) r.AddRange(nn);
        else if (kind == 6 && cnt == 3) { if (kk == 0) r.Add(nn[1]); else if (kk == 1) { r.Add(nn[0]); r.Add(nn[2]); } else r.Add(nn[1]); }
        else if (kind == 7 && cnt == 3) { if (kk == 0) r.Add(nn[2]); else if (kk == 2) r.Add(nn[0]); }
        else { r.Add(nn[(kk + 1) % cnt]); r.Add(nn[(kk + cnt - 1) % cnt]); }
    }
    if (kind == 1) r = r.Distinct().ToList();
    if (kind == 3 || kind == 6 || kind == 7) { r = r.Distinct().ToList(); r.Add(n); }
    if (kind == 4) r = r.Distinct().ToList();
    if (kind == 5) r.Add(n);
    return r;
}
for (int mode = 0; mode <= 2; mode++)
    for (int kind = 1; kind <= 5; kind++)
    {
        int md = mode, kd = kind;
        var baseD = new Dictionary<int, double[]>();
        double[] D(int x) { if (!baseD.TryGetValue(x, out var v)) baseD[x] = v = DesA(x, md); return v; }
        Score($"N{kd} komsu ort eq{md}", n => { var s = Set(n, kd); var r = new double[4]; foreach (var x in s) { var v = D(x); for (int i = 0; i < 4; i++) r[i] += v[i]; } for (int i = 0; i < 4; i++) r[i] /= s.Count; return r; });
    }

// 3) As from GPR design moment -> check As formula
double fcd = 250 / 1.5, fyd = 4200 / 1.15;
foreach (var (fy, lab) in new[] { (4200 / 1.15, "fyd=fyk/1.15"), (4200 / 1.5, "fy=fyk/1.5") })
foreach (double cover in new[] { 5.0, 7.0 })
{
    int ok = 0, tot = 0; var bad = new List<string>();
    foreach (var kv in gA)
    {
        var v = kv.Value; if (v[1] >= 500) continue;
        double hcm = v[1], d = hcm - cover, asmin = 0.001 * 100 * (hcm - 7);
        for (int i = 0; i < 4; i++)
        {
            double M = Math.Abs(v[6 + i]) * 1e5; // kgcm per m
            // rectangular block: As = 0.85 fcd b a / fyd, a from M = 0.85 fcd b a (d - a/2)
            double b = 100, A = 0.85 * fcd * b;
            double disc = d * d - 2 * M / A; double a = disc > 0 ? d - Math.Sqrt(disc) : d;
            double As = Math.Max(A * a / fy, asmin);
            tot++; double g = v[10 + (i == 0 ? 0 : i == 1 ? 1 : i == 2 ? 2 : 3)];
            if (Math.Abs(Math.Round(As, 2, MidpointRounding.AwayFromZero) - g) <= 0.0101) ok++; else if (bad.Count < 5) bad.Add($"n{kv.Key}[{i}] M={v[6 + i]} {As:F3}/{g}");
        }
    }
    Console.WriteLine($"As formulu {lab} pas={cover}: {ok}/{tot}  {string.Join("; ", bad)}");
}

// ---- 4) bilesen istatistigi + Wood-Armer
{
    for (int comp = 0; comp < 8; comp++)
    {
        double mx = 0; for (int e = 0; e < nMesh; e++) for (int k = 0; k < 4; k++) mx = Math.Max(mx, Math.Abs(S(e, 0, k, comp)));
        Console.WriteLine($"comp{comp} max|G| = {mx:F3}");
    }
}

// kombinasyon katsayi vektorleri (15 durum)
List<double[]> Combos(int eqMode)
{
    var list = new List<double[]>();
    var eqv = new List<double[]>();
    if (eqMode == 0) for (int j = 8; j <= 11; j++) foreach (int s in new[] { 1, -1 }) { var v = new double[15]; v[j] = s; eqv.Add(v); }
    else for (int a = 8; a <= 9; a++) for (int b = 10; b <= 11; b++) foreach (int s1 in new[] { 1, -1 }) foreach (int s2 in new[] { 1, -1 })
    { var v = new double[15]; v[a] = s1; v[b] = s1 * s2 * 0.3; eqv.Add(v); var u = new double[15]; u[b] = s1; u[a] = s1 * s2 * 0.3; eqv.Add(u); }
    var wv = new List<double[]>(); foreach (int j in new[] { 12, 13 }) foreach (int s in new[] { 1, -1 }) { var v = new double[15]; v[j] = s; wv.Add(v); }
    foreach (var c in Cod) for (int q = 1; q <= 6; q++)
        foreach (var e in c[3] != 0 ? eqv : new List<double[]> { new double[15] })
            foreach (var w in c[4] != 0 ? wv : new List<double[]> { new double[15] })
            { var v = new double[15]; v[0] = c[0]; v[q] = c[1]; v[7] = c[2]; for (int i = 0; i < 15; i++) v[i] += c[3] * e[i] + c[4] * w[i]; list.Add(v); }
    return list;
}
double Dot(double[] a, double[] b) { double s = 0; for (int i = 0; i < 15; i++) s += a[i] * b[i]; return s; }
foreach (int eqm in new[] { 0, 1 })
foreach (int wa in new[] { 0, 1, 2 })
{
    var cl = Combos(eqm);
    var baseD = new Dictionary<int, double[]>();
    double[] DW(int n)
    {
        if (baseD.TryGetValue(n, out var r0)) return r0;
        var ax = Avg(n, 3); var ay = Avg(n, 4); var axy = Avg(n, 5);
        double xa = 0, xu = 0, ya = 0, yu = 0;
        foreach (var c in cl)
        {
            double mx = Dot(c, ax), my = Dot(c, ay), mxy = Math.Abs(Dot(c, axy));
            if (wa == 0) mxy = 0;
            if (wa == 2) { // tam Wood-Armer
                double pxa = mx + mxy, pya = my + mxy; if (pxa < 0) { pxa = 0; pya = my + mxy * mxy / Math.Max(1e-9, Math.Abs(mx)); } else if (pya < 0) { pya = 0; pxa = mx + mxy * mxy / Math.Max(1e-9, Math.Abs(my)); }
                double pxu = mx - mxy, pyu = my - mxy; if (pxu > 0) { pxu = 0; pyu = my - mxy * mxy / Math.Max(1e-9, Math.Abs(mx)); } else if (pyu > 0) { pyu = 0; pxu = mx - mxy * mxy / Math.Max(1e-9, Math.Abs(my)); }
                xa = Math.Max(xa, pxa); ya = Math.Max(ya, pya); xu = Math.Min(xu, pxu); yu = Math.Min(yu, pyu); continue; }
            xa = Math.Max(xa, mx + mxy); ya = Math.Max(ya, my + mxy); xu = Math.Min(xu, mx - mxy); yu = Math.Min(yu, my - mxy);
        }
        return baseD[n] = new[] { xa, xu, ya, yu };
    }
    Score($"WA{wa} kendi eq{eqm}", DW);
    foreach (int kd in new[] { 2, 3, 5 })
        Score($"WA{wa} N{kd} eq{eqm}", n => { var s = Set(n, kd); var r = new double[4]; foreach (var x in s) { var v = DW(x); for (int i = 0; i < 4; i++) r[i] += v[i]; } for (int i = 0; i < 4; i++) r[i] /= s.Count; return r; });
}

// ---- 5) GPR statik girdiyle (yuvarlanmis) kombinasyon adimini izole et
{
    var cl = Combos(0);
    double[] DG(int n) { var ax = gMx[n]; var ay = gMy[n]; double xa = 0, xu = 0, ya = 0, yu = 0; foreach (var c in cl) { double mx = Dot(c, ax), my = Dot(c, ay); xa = Math.Max(xa, mx); xu = Math.Min(xu, mx); ya = Math.Max(ya, my); yu = Math.Min(yu, my); } return new[] { xa, xu, ya, yu }; }
    Score("GPRstat kendi eq0", DG);
    for (int kd = 1; kd <= 5; kd++) { int k2 = kd; Score($"GPRstat N{k2} eq0", n => { var s = Set(n, k2); var r = new double[4]; foreach (var x in s) { var v = DG(x); for (int i = 0; i < 4; i++) r[i] += v[i]; } for (int i = 0; i < 4; i++) r[i] /= s.Count; return r; }); }
    // basari dagilimi N3: bagli eleman sayisi ve ucgen icerip icermeme
    var stat = new Dictionary<string, (int ok, int tot)>();
    foreach (var kv in gA)
    {
        int n = kv.Key; if (conn[n].Count == 0) continue;
        var s = Set(n, 3); var r = new double[4]; foreach (var x in s) { var v = DG(x); for (int i = 0; i < 4; i++) r[i] += v[i]; } for (int i = 0; i < 4; i++) r[i] /= s.Count;
        bool tri = s.Any(x => conn[x].Any(t => mesh[t.e][3] == 0));
        string key = $"eleman={conn[n].Count} ucgenKomsu={tri}";
        var st = stat.GetValueOrDefault(key);
        for (int i = 0; i < 4; i++) { st.tot++; if (Math.Abs(Math.Round(r[i], 2, MidpointRounding.AwayFromZero) - kv.Value[6 + i]) <= 0.0101) st.ok++; }
        stat[key] = st;
    }
    foreach (var kv in stat.OrderBy(x => x.Key)) Console.WriteLine($"  {kv.Key}: {kv.Value.ok}/{kv.Value.tot}");
}

// ---- 6) basarisiz dugumlerde alt kume aramasi
{
    var cl = Combos(0);
    var nx = new double[nNode + 1]; var ny = new double[nNode + 1];
    foreach (var kv in gA) { nx[kv.Key] = kv.Value[3]; ny[kv.Key] = kv.Value[4]; }
    double[] DG(int n) { var ax = gMx[n]; var ay = gMy[n]; double xa = 0, xu = 0, ya = 0, yu = 0; foreach (var c in cl) { double mx = Dot(c, ax), my = Dot(c, ay); xa = Math.Max(xa, mx); xu = Math.Min(xu, mx); ya = Math.Max(ya, my); yu = Math.Min(yu, my); } return new[] { xa, xu, ya, yu }; }
    int shown = 0;
    foreach (var kv in gA)
    {
        int n = kv.Key; if (conn[n].Count == 0 || shown >= 14) continue;
        var s3 = Set(n, 3); var r3 = new double[4]; foreach (var x in s3) { var v = DG(x); for (int i = 0; i < 4; i++) r3[i] += v[i]; } for (int i = 0; i < 4; i++) r3[i] /= s3.Count;
        bool okN3 = Enumerable.Range(0, 4).All(i => Math.Abs(Math.Round(r3[i], 2) - kv.Value[6 + i]) <= 0.0101);
        if (okN3) continue;
        var cand = conn[n].SelectMany(t => mesh[t.e].Where(x => x > 0)).Distinct().ToList();
        // genisletilmis aday: 2. halka
        var cand2 = cand.SelectMany(c => conn[c].SelectMany(t => mesh[t.e].Where(x => x > 0))).Distinct().Where(x => !cand.Contains(x)).ToList();
        var all = cand.Concat(cand2).Take(16).ToList();
        var found = new List<string>();
        for (int mask = 1; mask < (1 << all.Count) && found.Count < 3; mask++)
        {
            var sub = Enumerable.Range(0, all.Count).Where(b => (mask >> b & 1) == 1).Select(b => all[b]).ToList();
            if (sub.Count > 9) continue;
            var r = new double[4]; foreach (var x in sub) { var v = DG(x); for (int i = 0; i < 4; i++) r[i] += v[i]; }
            if (Enumerable.Range(0, 4).All(i => Math.Abs(r[i] / sub.Count - kv.Value[6 + i]) <= 0.006))
                found.Add(string.Join(",", sub.Select(x => $"{x}({nx[x] - nx[n]:+0.00;-0.00},{ny[x] - ny[n]:+0.00;-0.00})")));
        }
        Console.WriteLine($"n{n} el={conn[n].Count} N3={string.Join(",", s3.Distinct())} -> {(found.Count == 0 ? "YOK" : string.Join(" | ", found))}");
        shown++;
    }
}

// ---- 7) As formulu (yeni GPR, rho_min=0.0015)
double AsF(double Mtm, double hcm, double fcdK, double fydK, double cover, int kind)
{
    if (Math.Abs(Mtm) < 0.005) return 0;
    double d = hcm - cover, b = 100, M = Math.Abs(Mtm) * 1e5;
    double As;
    if (kind == 0) { double A = 0.85 * fcdK * b; double disc = d * d - 2 * M / A; double a = disc > 0 ? d - Math.Sqrt(disc) : d; As = A * a / fydK; }
    else As = M / (fydK * 0.9 * d);
    return Math.Max(As, 0.0015 * b * (hcm - 7));
}
foreach (var (fcdK, fydK, lab) in new[] { (250 / 1.5, 4200 / 1.15, "fcd=fck/1.5 fyd=fyk/1.15"), (250 / 1.5, 4200 / 1.5, "fyd=fyk/1.5"), (250 / 1.5, 4200.0, "fyd=fyk") })
foreach (double cover in new[] { 5.0, 7.0 })
foreach (int kind in new[] { 0, 1 })
{
    int ok = 0, tot = 0; var bad = new List<string>();
    foreach (var kv in gA)
    {
        var v = kv.Value; if (v[1] >= 500) continue;
        for (int i = 0; i < 4; i++) { double g = v[10 + i]; if (g <= 4.96) continue; double As = AsF(v[6 + i], v[1], fcdK, fydK, cover, kind); tot++; if (Math.Abs(As - g) <= 0.0151) ok++; else if (bad.Count < 4) bad.Add($"n{kv.Key} M={v[6 + i]} {As:F3}/{g}"); }
    }
    Console.WriteLine($"AsF {lab} pas={cover} {(kind == 0 ? "blok" : "0.9d")}: {ok}/{tot}  {string.Join("; ", bad)}");
}

// ---- 8) As formulu izgara: As = k*a/fy, a: M = k*a*(d-a/2)
{
    var best = new List<(int ok, double k, double fy, double d)>();
    foreach (double fy in new[] { 2800.0, 3652.17, 4200.0, 3500.0 })
    foreach (double cover in new[] { 5.0, 6.0, 7.0 })
    for (double k = 60; k <= 400; k += 0.5)
    {
        int ok = 0;
        foreach (var kv in gA)
        {
            var v = kv.Value; if (v[1] >= 500) continue;
            double d = v[1] - cover;
            for (int i = 0; i < 4; i++) { double g = v[10 + i]; if (g <= 4.96) continue; double M = Math.Abs(v[6 + i]) * 1e5; double A = k * 100; double disc = d * d - 2 * M / A; if (disc < 0) continue; double a = d - Math.Sqrt(disc); if (Math.Abs(A * a / fy - g) <= 0.0151) ok++; }
        }
        best.Add((ok, k, fy, cover));
    }
    foreach (var b in best.OrderByDescending(x => x.ok).Take(6)) Console.WriteLine($"GRID ok={b.ok}/77 k(0.85fcd)={b.k} fy={b.fy} pas={b.d}");
}

// ---- 9) GPR'siz uctan uca: SO1 -> N3 eq0 tasarim -> As ; GPR As ile pratik karsilastirma
{
    int SETK = int.Parse(Environment.GetEnvironmentVariable("SETK") ?? "3");
    double AsK(double Mtm, double hcm) { if (Math.Abs(Mtm) < 0.005) return 0; double d = hcm - 7, M = Math.Abs(Mtm) * 1e5, A = 158 * 100; double disc = d * d - 2 * M / A; double a = disc > 0 ? d - Math.Sqrt(disc) : d; return Math.Max(A * a / 2800, 0.0015 * 100 * d); }
    double[] genel = { Hdr(3), Hdr(2), Hdr(5), Hdr(4) }; // X alt, X ust, Y alt, Y ust
    string[] ad = { "X alt", "X ust", "Y alt", "Y ust" };
    var baseD = new Dictionary<int, double[]>();
    double[] D0(int x) { if (!baseD.TryGetValue(x, out var v)) baseD[x] = v = DesA(x, 0); return v; }
    for (int i = 0; i < 4; i++)
    {
        int tp = 0, fp = 0, fn = 0, tn = 0; double sum = 0, maxG = 0, maxB = 0; int cnt = 0, within = 0;
        foreach (var kv in gA)
        {
            int n = kv.Key; if (conn[n].Count == 0) continue;
            var s = Set(n, SETK); double m = 0; foreach (var x in s) m += D0(x)[i]; m /= s.Count;
            double asB = AsK(m, kv.Value[1]), asG = kv.Value[10 + i];
            cnt++; sum += Math.Abs(asB - asG); if (Math.Abs(asB - asG) <= 0.25) within++;
            maxG = Math.Max(maxG, asG); maxB = Math.Max(maxB, asB);
            bool g = asG > genel[i] + 1e-6, b = asB > genel[i] + 1e-6;
            if (g && b) tp++; else if (!g && b) fp++; else if (g && !b) fn++; else tn++;
        }
        if (Environment.GetEnvironmentVariable("TOPU") == "1") { }
        Console.WriteLine($"UCTAN {ad[i]} genel={genel[i]:F2}: ort|dAs|={sum / cnt:F3} (|d|<=0.25: {within}/{cnt}); max GPR={maxG:F2} biz={maxB:F2}; ilave: ikisi={tp} sadeceBiz={fp} sadeceGPR={fn}");
    }
}
{
    double AsK(double Mtm, double hcm) { if (Math.Abs(Mtm) < 0.005) return 0; double d = hcm - 7, M = Math.Abs(Mtm) * 1e5, A = 158 * 100; double disc = d * d - 2 * M / A; double a = disc > 0 ? d - Math.Sqrt(disc) : d; return Math.Max(A * a / 2800, 0.0015 * 100 * d); }
    var cl = Combos(0);
    double[] DG(int n) { var ax = gMx[n]; var ay = gMy[n]; double xa = 0, xu = 0, ya = 0, yu = 0; foreach (var c in cl) { double mx = Dot(c, ax), my = Dot(c, ay); xa = Math.Max(xa, mx); xu = Math.Min(xu, mx); ya = Math.Max(ya, my); yu = Math.Min(yu, my); } return new[] { xa, xu, ya, yu }; }
    foreach (int i in new[] { 0, 2 })
    {
        var rows = new List<(double d, string s)>();
        foreach (var kv in gA)
        {
            int n = kv.Key; if (conn[n].Count == 0) continue;
            var s = Set(n, 3); double m = 0, mg = 0; foreach (var x in s) { m += DesA(x, 0)[i]; mg += DG(x)[i]; } m /= s.Count; mg /= s.Count;
            double a = AsK(m, 40), ag = AsK(mg, 40), g = kv.Value[10 + i];
            bool tri = conn[n].Any(t => mesh[t.e][3] == 0);
            rows.Add((Math.Abs(a - g), $"n{n} el={conn[n].Count} ucgen={tri} M: SO1={m:F2} GPRstat={mg:F2} GPR={kv.Value[6 + i]:F2} | As: SO1={a:F2} GPRstat={ag:F2} GPR={g:F2}"));
        }
        foreach (var r in rows.OrderByDescending(x => x.d).Take(6)) Console.WriteLine($"TOP{i} {r.s}");
    }
}
{
    double AsK(double Mtm, double hcm) { if (Math.Abs(Mtm) < 1e-6) return 0; double d = hcm - 7, M = Math.Abs(Mtm) * 1e5, A = 158 * 100; double disc = d * d - 2 * M / A; double a = disc > 0 ? d - Math.Sqrt(disc) : d; return Math.Max(A * a / 2800, 0.0015 * 100 * d); }
    var cl = Combos(0);
    double[] DG(int n) { var ax = gMx[n]; var ay = gMy[n]; double xa = 0, xu = 0, ya = 0, yu = 0; foreach (var c in cl) { double mx = Dot(c, ax), my = Dot(c, ay); xa = Math.Max(xa, mx); xu = Math.Min(xu, mx); ya = Math.Max(ya, my); yu = Math.Min(yu, my); } return new[] { xa, xu, ya, yu }; }
    foreach (var (src, f) in new (string, Func<int, double[]>)[] { ("GPRstat", DG), ("SO1", n => DesA(n, 0)) })
    foreach (int kd in new[] { 0, 2, 3, 5 })
    {
        int ok = 0, tot = 0, ok25 = 0;
        foreach (var kv in gA)
        {
            int n = kv.Key; if (conn[n].Count == 0) continue;
            var s = kd == 0 ? new List<int> { n } : Set(n, kd);
            for (int i = 0; i < 4; i++) { double a = s.Average(x => AsK(f(x)[i], 40)); double g = kv.Value[10 + i]; tot++; if (Math.Abs(a - g) <= 0.0151) ok++; if (Math.Abs(a - g) <= 0.25) ok25++; }
        }
        Console.WriteLine($"ORT-As {src} N{kd}: tam {ok}/{tot}, ±0.25 {ok25}/{tot}");
    }
}

// ---- 10) ucgen komsuluk hipotezleri (GPR statik girdisiyle)
{
    var cl = Combos(0);
    var dgc = new Dictionary<int, double[]>();
    double[] DG(int n) { if (n <= 0) return new double[4]; if (dgc.TryGetValue(n, out var r0)) return r0; var ax = gMx[n]; var ay = gMy[n]; double xa = 0, xu = 0, ya = 0, yu = 0; foreach (var c in cl) { double mx = Dot(c, ax), my = Dot(c, ay); xa = Math.Max(xa, mx); xu = Math.Min(xu, mx); ya = Math.Max(ya, my); yu = Math.Min(yu, my); } return dgc[n] = new[] { xa, xu, ya, yu }; }
    // triMode: 0 gercek ucgen kenarlari, 1 L=0 dugumu (deger 0) katilir, 2 L=K, 3 L=I, 4 ucgende yalniz I-J,J-K kenarlari
    List<int> Nb(int n, int triMode, bool distinct, bool selfOnce)
    {
        var r = new List<int>();
        foreach (var (e, k) in conn[n])
        {
            var q = (int[])mesh[e].Clone();
            bool tri = q[3] == 0;
            if (tri && triMode == 2) q[3] = q[2]; if (tri && triMode == 3) q[3] = q[0];
            int cnt = (tri && triMode == 0) ? 3 : 4;
            if (tri && triMode == 4)
            {
                int kk = k; if (kk == 0) r.Add(q[1]); else if (kk == 1) { r.Add(q[0]); r.Add(q[2]); } else r.Add(q[1]);
                continue;
            }
            r.Add(q[(k + 1) % cnt]); r.Add(q[(k + cnt - 1) % cnt]);
        }
        if (distinct) r = r.Distinct().ToList();
        if (selfOnce) { r.RemoveAll(x => x == n); r.Add(n); }
        return r;
    }
    foreach (int tm in new[] { 0, 1, 2, 3, 4 })
    foreach (bool dist in new[] { true, false })
    {
        int okIrr = 0, totIrr = 0, okAll = 0, totAll = 0;
        foreach (var kv in gA)
        {
            int n = kv.Key; if (conn[n].Count == 0) continue;
            var s = Nb(n, tm, dist, true);
            bool irr = s.Any(x => x > 0 && conn[x].Any(t => mesh[t.e][3] == 0)) || conn[n].Count != 4;
            for (int i = 0; i < 4; i++) { double m = s.Average(x => DG(x)[i]); bool ok = Math.Abs(Math.Round(m, 2, MidpointRounding.AwayFromZero) - kv.Value[6 + i]) <= 0.0101; totAll++; if (ok) okAll++; if (irr) { totIrr++; if (ok) okIrr++; } }
        }
        Console.WriteLine($"KOMSU tri{tm} distinct={dist}: tum {okAll}/{totAll}, duzensiz {okIrr}/{totIrr}");
    }
}

// ---- 11) dokum
if (Environment.GetEnvironmentVariable("DUMP") is string dl)
{
    var cl = Combos(0);
    double[] DG(int n) { var ax = gMx[n]; var ay = gMy[n]; double xa = 0, xu = 0, ya = 0, yu = 0; foreach (var c in cl) { double mx = Dot(c, ax), my = Dot(c, ay); xa = Math.Max(xa, mx); xu = Math.Min(xu, mx); ya = Math.Max(ya, my); yu = Math.Min(yu, my); } return new[] { xa, xu, ya, yu }; }
    foreach (var ns in dl.Split(','))
    {
        int n = int.Parse(ns); var v = gA[n];
        Console.WriteLine($"== n{n} ({v[3]},{v[4]}) GPR M: {v[6]} {v[7]} {v[8]} {v[9]}  As: {v[10]} {v[11]} {v[12]} {v[13]}  own: {string.Join(" ", DG(n).Select(x => x.ToString("F3")))}");
        foreach (var (e, k) in conn[n]) Console.WriteLine($"   e{e + 1} [{string.Join(",", mesh[e])}] kose{k}");
        foreach (var x in conn[n].SelectMany(t => mesh[t.e].Where(y => y > 0)).Distinct().Where(y => y != n))
            Console.WriteLine($"   nb{x} ({gA[x][3] - v[3]:+0.00;-0.00},{gA[x][4] - v[4]:+0.00;-0.00}) own: {string.Join(" ", DG(x).Select(y => y.ToString("F3")))}  GPR: {gA[x][6]} {gA[x][7]} {gA[x][8]} {gA[x][9]}");
    }
}

// ---- 12) zarf(komsu ortalamasi statik) 
{
    var cl = Combos(0);
    double[] EnvS(double[] ax, double[] ay) { double xa = 0, xu = 0, ya = 0, yu = 0; foreach (var c in cl) { double mx = Dot(c, ax), my = Dot(c, ay); xa = Math.Max(xa, mx); xu = Math.Min(xu, mx); ya = Math.Max(ya, my); yu = Math.Min(yu, my); } return new[] { xa, xu, ya, yu }; }
    foreach (int kd in new[] { 1, 2, 3, 5 })
    {
        int k2 = kd;
        Score($"ZARF(ort statik) N{k2}", n => { var s = Set(n, k2); var ax = new double[15]; var ay = new double[15]; foreach (var x in s) for (int i = 0; i < 15; i++) { ax[i] += gMx[x][i] / s.Count; ay[i] += gMy[x][i] / s.Count; } return EnvS(ax, ay); });
    }
}

// ---- 13) dugumun kendi tasarim degeri: farkli kose secimleri + N3
{
    var cl = Combos(0);
    double[] EnvS(double[] ax, double[] ay) { double xa = 0, xu = 0, ya = 0, yu = 0; foreach (var c in cl) { double mx = Dot(c, ax), my = Dot(c, ay); xa = Math.Max(xa, mx); xu = Math.Min(xu, mx); ya = Math.Max(ya, my); yu = Math.Min(yu, my); } return new[] { xa, xu, ya, yu }; }
    for (int om = 0; om <= 4; om++)
    {
        int o = om; var cache = new Dictionary<int, double[]>();
        double[] Own(int n)
        {
            if (cache.TryGetValue(n, out var r)) return r;
            var cs = conn[n];
            if (o == 0) r = EnvS(Avg(n, 3), Avg(n, 4));
            else if (o == 1) r = EnvS(Corner(cs[0].e, cs[0].k, 3), Corner(cs[0].e, cs[0].k, 4));
            else if (o == 2) r = EnvS(Corner(cs[^1].e, cs[^1].k, 3), Corner(cs[^1].e, cs[^1].k, 4));
            else if (o == 3) { r = new double[4]; foreach (var (e, k) in cs) { var v = EnvS(Corner(e, k, 3), Corner(e, k, 4)); for (int i = 0; i < 4; i++) r[i] += v[i] / cs.Count; } }
            else { r = new double[4]; foreach (var (e, k) in cs) { var v = EnvS(Corner(e, k, 3), Corner(e, k, 4)); r[0] = Math.Max(r[0], v[0]); r[1] = Math.Min(r[1], v[1]); r[2] = Math.Max(r[2], v[2]); r[3] = Math.Min(r[3], v[3]); } }
            return cache[n] = r;
        }
        Score($"OWN{o} N3", n => { var s = Set(n, 3); var r = new double[4]; foreach (var x in s) { var v = Own(x); for (int i = 0; i < 4; i++) r[i] += v[i] / s.Count; } return r; });
        // eleman bazli: dugumun bagli elemanlarindaki tum kose zarflarinin ortalamasi
        Score($"OWN{o} ElemKose", n => { var r = new double[4]; int c = 0; foreach (var (e, k) in conn[n]) foreach (var (kk, x) in mesh[e].Select((x, i) => (i, x)).Where(t => t.x > 0)) { var v = EnvS(Corner(e, kk, 3), Corner(e, kk, 4)); for (int i = 0; i < 4; i++) r[i] += v[i]; c++; } for (int i = 0; i < 4; i++) r[i] /= c; return r; });
    }
}

// ---- 14) bilesen bazli alt kume (kendisi dahil), yon etiketleriyle
if (Environment.GetEnvironmentVariable("SUB") is string sl)
{
    var cl = Combos(0);
    double[] DG(int n) { var ax = gMx[n]; var ay = gMy[n]; double xa = 0, xu = 0, ya = 0, yu = 0; foreach (var c in cl) { double mx = Dot(c, ax), my = Dot(c, ay); xa = Math.Max(xa, mx); xu = Math.Min(xu, mx); ya = Math.Max(ya, my); yu = Math.Min(yu, my); } return new[] { xa, xu, ya, yu }; }
    string Dir(int n, int x) { double dx = gA[x][3] - gA[n][3], dy = gA[x][4] - gA[n][4]; if (x == n) return "O"; string s = ""; if (dx > 0.05) s += "E"; if (dx < -0.05) s += "W"; if (dy > 0.05) s += "N"; if (dy < -0.05) s += "S"; return s; }
    foreach (var ns in sl.Split(','))
    {
        int n = int.Parse(ns);
        var cand = conn[n].SelectMany(t => mesh[t.e].Where(x => x > 0)).Distinct().Where(x => x != n).ToList();
        var e3 = Set(n, 3).Distinct().ToList();
        Console.Write($"n{n} el={conn[n].Count} N3={string.Join("", e3.Select(x => Dir(n, x) + " "))}|");
        for (int i = 0; i < 4; i++)
        {
            var sols = new List<string>();
            for (int mask = 0; mask < (1 << cand.Count); mask++)
            {
                var sub = new List<int> { n }; for (int b = 0; b < cand.Count; b++) if ((mask >> b & 1) == 1) sub.Add(cand[b]);
                double m = sub.Average(x => DG(x)[i]);
                if (Math.Abs(m - gA[n][6 + i]) <= 0.0055) sols.Add(string.Join("", sub.Select(x => Dir(n, x))));
            }
            Console.Write($" c{i}:[{string.Join(" ", sols.Take(4))}]");
        }
        Console.WriteLine();
    }
}

// ---- 15) kose bazli zarf dokumu
if (Environment.GetEnvironmentVariable("KOSE") is string kl)
{
    var cl = Combos(0);
    double[] EnvS(double[] ax, double[] ay) { double xa = 0, xu = 0, ya = 0, yu = 0; foreach (var c in cl) { double mx = Dot(c, ax), my = Dot(c, ay); xa = Math.Max(xa, mx); xu = Math.Min(xu, mx); ya = Math.Max(ya, my); yu = Math.Min(yu, my); } return new[] { xa, xu, ya, yu }; }
    foreach (var ns in kl.Split(','))
    {
        int n = int.Parse(ns);
        Console.WriteLine($"K n{n} GPR {gA[n][6]} {gA[n][7]} {gA[n][8]} {gA[n][9]} | stat-env {string.Join(" ", EnvS(gMx[n], gMy[n]).Select(x => x.ToString("F3")))} | GPRstat G Mx={gMx[n][0]} My={gMy[n][0]}");
        foreach (var (e, k) in conn[n]) Console.WriteLine($"K    e{e + 1} k{k}: env {string.Join(" ", EnvS(Corner(e, k, 3), Corner(e, k, 4)).Select(x => x.ToString("F3")))}  G Mx={S(e, 0, k, 3):F3} My={S(e, 0, k, 4):F3}");
    }
}

// ---- 16) komsu dahil olma istatistigi (bilesen x yon x kenar tipi)
if (Environment.GetEnvironmentVariable("STAT") == "1")
{
    var cl = Combos(0);
    var dgc = new Dictionary<int, double[]>();
    double[] DG(int n) { if (dgc.TryGetValue(n, out var r0)) return r0; var ax = gMx[n]; var ay = gMy[n]; double xa = 0, xu = 0, ya = 0, yu = 0; foreach (var c in cl) { double mx = Dot(c, ax), my = Dot(c, ay); xa = Math.Max(xa, mx); xu = Math.Min(xu, mx); ya = Math.Max(ya, my); yu = Math.Min(yu, my); } return dgc[n] = new[] { xa, xu, ya, yu }; }
    // kenar sayaci: kenar kac elemanda (1 = sinir kenari)
    var edgeCnt = new Dictionary<(int, int), int>();
    foreach (var q in mesh) { var nn = q.Where(x => x > 0).ToArray(); for (int i = 0; i < nn.Length; i++) { int a = nn[i], b = nn[(i + 1) % nn.Length]; var key = (Math.Min(a, b), Math.Max(a, b)); edgeCnt[key] = edgeCnt.GetValueOrDefault(key) + 1; } }
    string Lab(int n, int x)
    {
        double dx = gA[x][3] - gA[n][3], dy = gA[x][4] - gA[n][4];
        string d = Math.Abs(dy) < 0.05 ? "yatay" : Math.Abs(dx) < 0.05 ? "dusey" : "egik";
        int ec = edgeCnt.GetValueOrDefault((Math.Min(n, x), Math.Max(n, x)));
        return $"{d}/kenar{ec}";
    }
    var inc = new Dictionary<string, (int a, int b)>(); int solved = 0, tot = 0, uniq = 0;
    foreach (var kv in gA)
    {
        int n = kv.Key; if (conn[n].Count == 0) continue;
        var nb = Set(n, 3).Distinct().Where(x => x != n).ToList(); if (nb.Count > 10) continue;
        for (int i = 0; i < 4; i++)
        {
            tot++;
            if (Math.Abs(kv.Value[6 + i]) < 0.02) continue; // sifirlar bilgi vermez
            var sols = new List<int>();
            for (int mask = 0; mask < (1 << nb.Count); mask++)
            {
                double s = DG(n)[i]; int c = 1; for (int b = 0; b < nb.Count; b++) if ((mask >> b & 1) == 1) { s += DG(nb[b])[i]; c++; }
                if (Math.Abs(s / c - kv.Value[6 + i]) <= 0.0052) sols.Add(mask);
            }
            if (sols.Count > 0) solved++;
            if (sols.Count != 1) continue; uniq++;
            for (int b = 0; b < nb.Count; b++) { string key = $"{(i < 2 ? "Mx" : "My")} {Lab(n, nb[b])}"; var v = inc.GetValueOrDefault(key); if ((sols[0] >> b & 1) == 1) v.a++; v.b++; inc[key] = v; }
        }
    }
    Console.WriteLine($"STAT cozulen {solved}/{tot}, tekil {uniq}");
    foreach (var kv in inc.OrderBy(x => x.Key)) Console.WriteLine($"STAT {kv.Key}: dahil {kv.Value.a}/{kv.Value.b}");
}

// ---- 17) ters cozum: GPR(n) = ort_{j in N(n)} own(j)  -> own'u bul
if (Environment.GetEnvironmentVariable("TERS") is string ts)
{
    int kd = int.Parse(ts);
    var cl = Combos(0);
    var dgc = new Dictionary<int, double[]>();
    double[] DG(int n) { if (dgc.TryGetValue(n, out var r0)) return r0; var ax = gMx[n]; var ay = gMy[n]; double xa = 0, xu = 0, ya = 0, yu = 0; foreach (var c in cl) { double mx = Dot(c, ax), my = Dot(c, ay); xa = Math.Max(xa, mx); xu = Math.Min(xu, mx); ya = Math.Max(ya, my); yu = Math.Min(yu, my); } return dgc[n] = new[] { xa, xu, ya, yu }; }
    var ids = gA.Keys.Where(n => conn[n].Count > 0).OrderBy(x => x).ToList(); int N = ids.Count; var ix = ids.Select((n, i) => (n, i)).ToDictionary(t => t.n, t => t.i);
    double lam = 1e-4;
    for (int comp = 0; comp < 4; comp++)
    {
        // normal denklemler: (A^T A + lam I) x = A^T g + lam own
        var A = new double[N, N]; var g = new double[N];
        foreach (var n in ids) { var s = Set(n, kd); int r = ix[n]; foreach (var xx in s) A[r, ix[xx]] += 1.0 / s.Count; g[r] = gA[n][6 + comp]; }
        var M = new double[N, N + 1];
        for (int i = 0; i < N; i++) { for (int j = 0; j < N; j++) { double s = 0; for (int k = 0; k < N; k++) s += A[k, i] * A[k, j]; M[i, j] = s; } M[i, i] += lam; double b = 0; for (int k = 0; k < N; k++) b += A[k, i] * g[k]; M[i, N] = b + lam * DG(ids[i])[comp]; }
        for (int c = 0; c < N; c++) { int p = c; for (int r = c + 1; r < N; r++) if (Math.Abs(M[r, c]) > Math.Abs(M[p, c])) p = r; for (int j = 0; j <= N; j++) (M[c, j], M[p, j]) = (M[p, j], M[c, j]); for (int r = 0; r < N; r++) { if (r == c) continue; double f = M[r, c] / M[c, c]; if (f == 0) continue; for (int j = c; j <= N; j++) M[r, j] -= f * M[c, j]; } }
        var x = new double[N]; for (int i = 0; i < N; i++) x[i] = M[i, N] / M[i, i];
        double res = 0; int resOk = 0; for (int r = 0; r < N; r++) { double s = 0; for (int j = 0; j < N; j++) s += A[r, j] * x[j]; if (Math.Abs(s - g[r]) <= 0.006) resOk++; res = Math.Max(res, Math.Abs(s - g[r])); }
        int same = 0; var diffs = new List<(double d, int n)>();
        for (int i = 0; i < N; i++) { double d = x[i] - DG(ids[i])[comp]; if (Math.Abs(d) <= 0.03) same++; else diffs.Add((d, ids[i])); }
        Console.WriteLine($"TERS N{kd} c{comp}: artik ok {resOk}/{N} (max {res:F3}); own ayni(±0.03) {same}/{N}");
        foreach (var (d, n) in diffs.OrderByDescending(t => Math.Abs(t.d)).Take(12))
        {
            bool tri = conn[n].Any(t => mesh[t.e][3] == 0);
            Console.WriteLine($"TERS    n{n} el={conn[n].Count} ucgen={tri} cozum={x[ix[n]]:F3} bizim={DG(n)[comp]:F3}");
        }
    }
}

// ---- 18) kategori bazli basari (SO1 yolu, TRIZERO + SETK)
if (Environment.GetEnvironmentVariable("KAT") == "1")
{
    int kd = int.Parse(Environment.GetEnvironmentVariable("SETK") ?? "6");
    var edgeCnt = new Dictionary<(int, int), int>();
    foreach (var q in mesh) { var nn = q.Where(x => x > 0).ToArray(); for (int i = 0; i < nn.Length; i++) { int a = nn[i], b = nn[(i + 1) % nn.Length]; var key = (Math.Min(a, b), Math.Max(a, b)); edgeCnt[key] = edgeCnt.GetValueOrDefault(key) + 1; } }
    bool Sinir(int n) => conn[n].Any(t => { var nn = mesh[t.e].Where(x => x > 0).ToArray(); int c = nn.Length, k = Array.IndexOf(nn, n); return edgeCnt[(Math.Min(n, nn[(k + 1) % c]), Math.Max(n, nn[(k + 1) % c]))] == 1 || edgeCnt[(Math.Min(n, nn[(k + c - 1) % c]), Math.Max(n, nn[(k + c - 1) % c]))] == 1; });
    var cache = new Dictionary<int, double[]>(); double[] D0(int x) { if (!cache.TryGetValue(x, out var v)) cache[x] = v = DesA(x, int.Parse(Environment.GetEnvironmentVariable("EQM") ?? "0")); return v; }
    var st = new Dictionary<string, (int ok, int tot)>();
    foreach (var kv in gA)
    {
        int n = kv.Key; if (conn[n].Count == 0) continue;
        var s = Set(n, kd); bool s2 = s.Any(Sinir); bool sn = Sinir(n);
        int plk = conn[n].Select(t => meshP[t.e]).Distinct().Count();
        bool tri = s.Any(x => conn[x].Any(t => mesh[t.e][3] == 0));
        string key = $"sinir={(sn ? "dugum" : s2 ? "komsu" : "yok"),-5} plak={plk} ucgen={tri}";
        var v = st.GetValueOrDefault(key);
        for (int i = 0; i < 4; i++) { double m = s.Average(x => D0(x)[i]); v.tot++; if (Math.Abs(Math.Round(m, 2, MidpointRounding.AwayFromZero) - kv.Value[6 + i]) <= 0.0101) v.ok++; }
        st[key] = v;
    }
    foreach (var kv in st.OrderBy(x => x.Key)) Console.WriteLine($"KAT {kv.Key}: {kv.Value.ok}/{kv.Value.tot}");
}

// ---- 19) sinir dugumleri: komsu dahil olma (SO1 TRIZERO kendi degerleri)
if (Environment.GetEnvironmentVariable("SSTAT") == "1")
{
    var edgeCnt = new Dictionary<(int, int), int>();
    foreach (var q in mesh) { var nn = q.Where(x => x > 0).ToArray(); for (int i = 0; i < nn.Length; i++) { int a = nn[i], b = nn[(i + 1) % nn.Length]; var key = (Math.Min(a, b), Math.Max(a, b)); edgeCnt[key] = edgeCnt.GetValueOrDefault(key) + 1; } }
    int EC(int a, int b) => edgeCnt.GetValueOrDefault((Math.Min(a, b), Math.Max(a, b)));
    var cache = new Dictionary<int, double[]>(); double[] D0(int x) { if (!cache.TryGetValue(x, out var v)) cache[x] = v = DesA(x, 0); return v; }
    string Lab(int n, int x)
    {
        double dx = gA[x][3] - gA[n][3], dy = gA[x][4] - gA[n][4];
        string d = Math.Abs(dy) < 0.05 ? "yatay" : Math.Abs(dx) < 0.05 ? "dusey" : "egik";
        return $"{d}/{(EC(n, x) == 1 ? "SINIR" : EC(n, x) == 0 ? "kenarDegil" : "ic")}";
    }
    var inc = new Dictionary<string, (int a, int b)>(); int uniq = 0, tot = 0, none = 0;
    foreach (var kv in gA)
    {
        int n = kv.Key; if (conn[n].Count == 0) continue;
        var all = conn[n].SelectMany(t => mesh[t.e].Where(x => x > 0)).Distinct().Where(x => x != n).ToList();
        if (!all.Any(x => EC(n, x) == 1) || all.Count > 10) continue;
        for (int i = 0; i < 4; i++)
        {
            if (Math.Abs(kv.Value[6 + i]) < 0.02) continue; tot++;
            var sols = new List<int>();
            for (int mask = 0; mask < (1 << all.Count); mask++) { double s = D0(n)[i]; int c = 1; for (int b = 0; b < all.Count; b++) if ((mask >> b & 1) == 1) { s += D0(all[b])[i]; c++; } if (Math.Abs(s / c - kv.Value[6 + i]) <= 0.0052) sols.Add(mask); }
            if (sols.Count == 0) none++;
            if (sols.Count != 1) continue; uniq++;
            for (int b = 0; b < all.Count; b++) { string key = $"{(i < 2 ? "Mx" : "My")} {Lab(n, all[b])}"; var v = inc.GetValueOrDefault(key); if ((sols[0] >> b & 1) == 1) v.a++; v.b++; inc[key] = v; }
        }
    }
    Console.WriteLine($"SSTAT toplam {tot}, tekil {uniq}, cozumsuz {none}");
    foreach (var kv in inc.OrderBy(x => x.Key)) Console.WriteLine($"SSTAT {kv.Key}: dahil {kv.Value.a}/{kv.Value.b}");
}

// ---- 20) yerinde (sirali) ortalama hipotezi
if (Environment.GetEnvironmentVariable("SIRALI") == "1")
{
    foreach (int kd in new[] { 3, 6 })
    foreach (int order in new[] { 0, 1 })
    {
        var cur = new Dictionary<int, double[]>();
        var ids = Enumerable.Range(1, nNode).Where(n => conn[n].Count > 0).ToList();
        foreach (var n in ids) cur[n] = (double[])DesA(n, 0).Clone();
        if (order == 1) ids.Reverse();
        foreach (var n in ids) { var s = Set(n, kd); var r = new double[4]; foreach (var x in s) for (int i = 0; i < 4; i++) r[i] += cur[x][i] / s.Count; cur[n] = r; }
        Score($"SIRALI N{kd} {(order == 0 ? "artan" : "azalan")}", n => cur[n]);
    }
}

// ---- 21) yalniz sinir dugumleri bilinmeyen: ters cozum
if (Environment.GetEnvironmentVariable("STERS") is string sts)
{
    int kd = int.Parse(sts);
    var edgeCnt = new Dictionary<(int, int), int>();
    foreach (var q in mesh) { var nn = q.Where(x => x > 0).ToArray(); for (int i = 0; i < nn.Length; i++) { int a = nn[i], b = nn[(i + 1) % nn.Length]; var key = (Math.Min(a, b), Math.Max(a, b)); edgeCnt[key] = edgeCnt.GetValueOrDefault(key) + 1; } }
    bool Sinir(int n) => conn[n].Any(t => { var nn = mesh[t.e].Where(x => x > 0).ToArray(); int c = nn.Length, k = Array.IndexOf(nn, n); return edgeCnt[(Math.Min(n, nn[(k + 1) % c]), Math.Max(n, nn[(k + 1) % c]))] == 1 || edgeCnt[(Math.Min(n, nn[(k + c - 1) % c]), Math.Max(n, nn[(k + c - 1) % c]))] == 1; });
    var cache = new Dictionary<int, double[]>(); double[] D0(int x) { if (!cache.TryGetValue(x, out var v)) cache[x] = v = DesA(x, 0); return v; }
    var ids = gA.Keys.Where(n => conn[n].Count > 0).OrderBy(x => x).ToList();
    var unk = ids.Where(Sinir).ToList(); var ux = unk.Select((n, i) => (n, i)).ToDictionary(t => t.n, t => t.i); int U = unk.Count;
    Console.WriteLine($"STERS sinir dugum {U}");
    for (int comp = 0; comp < 4; comp++)
    {
        var rows = new List<(double[] a, double b)>();
        foreach (var n in ids) { var s = Set(n, kd); var a = new double[U]; double b = (Environment.GetEnvironmentVariable("STEST") == "1" ? Set(n, kd).Average(z => D0(z)[comp] + (ux.ContainsKey(z) ? 0.5 : 0)) : gA[n][6 + comp]); foreach (var x in s) { if (ux.TryGetValue(x, out int j)) a[j] += 1.0 / s.Count; else b -= D0(x)[comp] / s.Count; } if (a.Any(v => v != 0)) rows.Add((a, b)); }
        var M = new double[U, U + 1];
        foreach (var (a, b) in rows) for (int i = 0; i < U; i++) { if (a[i] == 0) continue; for (int j = 0; j < U; j++) M[i, j] += a[i] * a[j]; M[i, U] += a[i] * b; }
        for (int i = 0; i < U; i++) { M[i, i] += 1e-3; M[i, U] += 1e-3 * D0(unk[i])[comp]; }
        for (int c = 0; c < U; c++) { int p = c; for (int r = c + 1; r < U; r++) if (Math.Abs(M[r, c]) > Math.Abs(M[p, c])) p = r; for (int j = 0; j <= U; j++) (M[c, j], M[p, j]) = (M[p, j], M[c, j]); for (int r = 0; r < U; r++) { if (r == c) continue; double f = M[r, c] / M[c, c]; if (f == 0) continue; for (int j = c; j <= U; j++) M[r, j] -= f * M[c, j]; } }
        var x0 = new double[U]; for (int i = 0; i < U; i++) x0[i] = M[i, U] / M[i, i];
        int ok = 0; foreach (var (a, b) in rows) { double s = 0; for (int i = 0; i < U; i++) s += a[i] * x0[i]; if (Math.Abs(s - b) <= 0.006) ok++; }
        Console.WriteLine($"STERS c{comp}: denklem ok {ok}/{rows.Count}");
        foreach (int n in new[] { 1, 3, 9, 19, 29, 20, 147 }) if (ux.ContainsKey(n)) Console.WriteLine($"STERS    n{n}: cozum {x0[ux[n]]:F3} bizim {D0(n)[comp]:F3}");
    }
}

// ---- 22) guvenli taraf varyantlari
if (Environment.GetEnvironmentVariable("GUVEN") == "1")
{
    double AsK(double Mtm, double hcm) { if (Math.Abs(Mtm) < 1e-6) return 0; double d = hcm - 7, M = Math.Abs(Mtm) * 1e5, A = 158 * 100; double disc = d * d - 2 * M / A; double a = disc > 0 ? d - Math.Sqrt(disc) : d; return Math.Max(A * a / 2800, rhoMin * 100 * d); }
    var edgeCnt = new Dictionary<(int, int), int>();
    foreach (var q in mesh) { var nn = q.Where(x => x > 0).ToArray(); for (int i = 0; i < nn.Length; i++) { int a = nn[i], b = nn[(i + 1) % nn.Length]; var key = (Math.Min(a, b), Math.Max(a, b)); edgeCnt[key] = edgeCnt.GetValueOrDefault(key) + 1; } }
    bool Sinir(int n) => conn[n].Any(t => { var nn = mesh[t.e].Where(x => x > 0).ToArray(); int c = nn.Length, k = Array.IndexOf(nn, n); return edgeCnt[(Math.Min(n, nn[(k + 1) % c]), Math.Max(n, nn[(k + 1) % c]))] == 1 || edgeCnt[(Math.Min(n, nn[(k + c - 1) % c]), Math.Max(n, nn[(k + c - 1) % c]))] == 1; });
    var cache = new Dictionary<int, double[]>(); double[] D0(int x) { if (!cache.TryGetValue(x, out var v)) cache[x] = v = DesA(x, 0); return v; }
    double[] genel = { Hdr(3), Hdr(2), Hdr(5), Hdr(4) }; string[] ad = { "X alt", "X ust", "Y alt", "Y ust" };
    foreach (int mode in new[] { 0, 1, 2 })
    {
        string lab = mode == 0 ? "ortalama" : mode == 1 ? "sinirda max(kendi,ort)" : "sinirda komsu max";
        for (int i = 0; i < 4; i++)
        {
            int tp = 0, fp = 0, fn = 0, w = 0, cnt = 0; double maxB = 0, maxG = 0;
            foreach (var kv in gA)
            {
                int n = kv.Key; if (conn[n].Count == 0) continue; cnt++;
                var s = Set(n, 6); double m = s.Average(x => D0(x)[i]);
                if (mode >= 1 && Sinir(n)) { double own = D0(n)[i]; if (mode == 2) own = s.Select(x => D0(x)[i]).OrderBy(v => i % 2 == 0 ? v : -v).Last(); m = i % 2 == 0 ? Math.Max(m, own) : Math.Min(m, own); }
                double ab = AsK(m, kv.Value[1]), ag = kv.Value[10 + i]; if (Math.Abs(ab - ag) <= 0.25) w++;
                maxB = Math.Max(maxB, ab); maxG = Math.Max(maxG, ag);
                bool g = ag > genel[i] + 1e-6, b = ab > genel[i] + 1e-6; if (g && b) tp++; else if (b) fp++; else if (g) fn++;
            }
            Console.WriteLine($"GUVEN {lab,-24} {ad[i]}: ±0.25 {w}/{cnt}; max {maxG:F2}/{maxB:F2}; ilave ikisi={tp} yalnizBiz={fp} yalnizGPR={fn}");
        }
    }
}

// ---- 23) eleman merkezi tasarim momenti -> dugumde eleman ortalamasi
if (Environment.GetEnvironmentVariable("ELMERK") == "1")
{
    var cl = Combos(0);
    double[] EnvS(double[] ax, double[] ay) { double xa = 0, xu = 0, ya = 0, yu = 0; foreach (var c in cl) { double mx = Dot(c, ax), my = Dot(c, ay); xa = Math.Max(xa, mx); xu = Math.Min(xu, mx); ya = Math.Max(ya, my); yu = Math.Min(yu, my); } return new[] { xa, xu, ya, yu }; }
    for (int src = 0; src < 3; src++)
    {
        var E = new double[nMesh][];
        for (int e = 0; e < nMesh; e++)
        {
            var nn = mesh[e].Where(x => x > 0).ToArray(); var ax = new double[15]; var ay = new double[15];
            for (int k = 0; k < nn.Length; k++)
            {
                double[] vx, vy;
                if (src == 0) { vx = Corner(e, k, 3); vy = Corner(e, k, 4); }          // eleman kose degerleri
                else if (src == 1) { vx = gMx[nn[k]]; vy = gMy[nn[k]]; }               // dugum statikleri (GPR)
                else { vx = Avg(nn[k], 3); vy = Avg(nn[k], 4); }                       // dugum statikleri (SO1)
                for (int i = 0; i < 15; i++) { ax[i] += vx[i] / nn.Length; ay[i] += vy[i] / nn.Length; }
            }
            E[e] = EnvS(ax, ay);
        }
        Score($"ELMERK src{src}", n => { var r = new double[4]; foreach (var (e, k) in conn[n]) for (int i = 0; i < 4; i++) r[i] += E[e][i] / conn[n].Count; return r; });
    }
}

// ---- 24) sinirda yalniz dortgen + basarisiz dugumler
if (Environment.GetEnvironmentVariable("SBAS") == "1")
{
    var edgeCnt = new Dictionary<(int, int), int>();
    foreach (var q in mesh) { var nn = q.Where(x => x > 0).ToArray(); for (int i = 0; i < nn.Length; i++) { int a = nn[i], b = nn[(i + 1) % nn.Length]; var key = (Math.Min(a, b), Math.Max(a, b)); edgeCnt[key] = edgeCnt.GetValueOrDefault(key) + 1; } }
    var cache = new Dictionary<int, double[]>(); double[] D0(int x) { if (!cache.TryGetValue(x, out var v)) cache[x] = v = DesA(x, 0); return v; }
    foreach (var kv in gA)
    {
        int n = kv.Key; if (conn[n].Count == 0) continue;
        var s = Set(n, 6); bool tri = s.Any(x => conn[x].Any(t => mesh[t.e][3] == 0)); if (tri) continue;
        var nb = s.Where(x => x != n).ToList(); if (!nb.Any(x => edgeCnt.GetValueOrDefault((Math.Min(n, x), Math.Max(n, x))) == 1)) continue;
        var bad = Enumerable.Range(0, 4).Where(i => Math.Abs(Math.Round(s.Average(x => D0(x)[i]), 2, MidpointRounding.AwayFromZero) - kv.Value[6 + i]) > 0.0101).ToList();
        if (bad.Count == 0) continue;
        Console.WriteLine($"SBAS n{n} el={conn[n].Count} ({kv.Value[3]},{kv.Value[4]}) bozuk={string.Join(",", bad)} GPR={kv.Value[6]} {kv.Value[7]} {kv.Value[8]} {kv.Value[9]} ort={string.Join(" ", Enumerable.Range(0, 4).Select(i => s.Average(x => D0(x)[i]).ToString("F3")))}");
        foreach (var x in s) Console.WriteLine($"SBAS     {(x == n ? "*" : " ")}n{x} ({gA[x][3] - kv.Value[3]:+0.00;-0.00},{gA[x][4] - kv.Value[4]:+0.00;-0.00}) kenar={edgeCnt.GetValueOrDefault((Math.Min(n, x), Math.Max(n, x)))} own={string.Join(" ", D0(x).Select(v => v.ToString("F3")))}");
    }
}

// ---- 25) ilave uyumsuz dugumler listesi
if (Environment.GetEnvironmentVariable("ILAVE") == "1")
{
    double AsK(double Mtm, double hcm) { if (Math.Abs(Mtm) < 1e-6) return 0; double d = hcm - 7, M = Math.Abs(Mtm) * 1e5, A = 158 * 100; double disc = d * d - 2 * M / A; double a = disc > 0 ? d - Math.Sqrt(disc) : d; return Math.Max(A * a / 2800, rhoMin * 100 * d); }
    double[] genel = { Hdr(3), Hdr(2), Hdr(5), Hdr(4) }; string[] ad = { "X alt", "X ust", "Y alt", "Y ust" };
    var cache = new Dictionary<int, double[]>(); double[] D0(int x) { if (!cache.TryGetValue(x, out var v)) cache[x] = v = DesA(x, 0); return v; }
    foreach (var kv in gA)
    {
        int n = kv.Key; if (conn[n].Count == 0) continue; var s = Set(n, 6);
        for (int i = 0; i < 4; i++) { double ab = AsK(s.Average(x => D0(x)[i]), 40), ag = kv.Value[10 + i]; bool g = ag > genel[i] + 1e-6, b = ab > genel[i] + 1e-6; if (g != b) Console.WriteLine($"ILAVE {ad[i]} n{n} ({kv.Value[3]},{kv.Value[4]}) GPR As={ag:F2} biz={ab:F2} genel={genel[i]:F2} ilave: GPR {Math.Max(0, ag - genel[i]):F2} / biz {Math.Max(0, ab - genel[i]):F2}"); }
    }
}

// ---- 26) kose-rol kural taramasi: her (dortgen/ucgen, kose k) icin {next, prev} alt kumesi
if (Environment.GetEnvironmentVariable("KURAL") == "1")
{
    int[] KC = (Environment.GetEnvironmentVariable("KC") ?? "0,1,2,3").Split(",").Select(int.Parse).ToArray();
    var cache = new Dictionary<int, double[]>(); double[] D0(int x) { if (!cache.TryGetValue(x, out var v)) cache[x] = v = DesA(x, 0); return v; }
    var ids = gA.Keys.Where(n => conn[n].Count > 0).ToList(); foreach (var n in Enumerable.Range(1, nNode)) if (conn[n].Count > 0) D0(n);
    int Eval(int qr, int tr, bool distinct)
    {
        int ok = 0;
        foreach (var n in ids)
        {
            var s = new List<int>();
            foreach (var (e, k) in conn[n])
            {
                var nn = mesh[e]; bool tri = nn[3] == 0; int c = tri ? 3 : 4; int bits = tri ? (tr >> (2 * k)) & 3 : (qr >> (2 * k)) & 3;
                if ((bits & 1) != 0) s.Add(nn[(k + 1) % c]); if ((bits & 2) != 0) s.Add(nn[(k + c - 1) % c]);
            }
            if (distinct) s = s.Distinct().ToList(); s.RemoveAll(x => x == n); s.Add(n);
            foreach (int i in KC) { double m = 0; foreach (var x in s) m += cache[x][i]; m /= s.Count; if (Math.Abs(Math.Round(m, 2, MidpointRounding.AwayFromZero) - gA[n][6 + i]) <= 0.0101) ok++; }
        }
        return ok;
    }
    var res = new List<(int ok, int q, int t, bool d)>();
    foreach (bool d in new[] { true, false })
        for (int q = 0; q < 256; q++) for (int t = 0; t < 64; t++) res.Add((Eval(q, t, d), q, t, d));
    string Q(int q, int c) => string.Join(" ", Enumerable.Range(0, c).Select(k => $"k{k}:{((q >> (2 * k) & 1) != 0 ? "N" : "-")}{((q >> (2 * k) & 2) != 0 ? "P" : "-")}"));
    foreach (var r in res.OrderByDescending(x => x.ok).Take(10)) Console.WriteLine($"KURAL {r.ok}/2064 distinct={r.d} dortgen[{Q(r.q, 4)}] ucgen[{Q(r.t, 3)}]");
}

// ---- 27) sinirda burulma (Mxy) eklemesi
if (Environment.GetEnvironmentVariable("SMXY") == "1")
{
    var cl = Combos(0);
    var edgeCnt = new Dictionary<(int, int), int>();
    foreach (var q in mesh) { var nn = q.Where(x => x > 0).ToArray(); for (int i = 0; i < nn.Length; i++) { int a = nn[i], b = nn[(i + 1) % nn.Length]; var key = (Math.Min(a, b), Math.Max(a, b)); edgeCnt[key] = edgeCnt.GetValueOrDefault(key) + 1; } }
    bool Sinir(int n) => conn[n].Any(t => { var nn = mesh[t.e].Where(x => x > 0).ToArray(); int c = nn.Length, k = Array.IndexOf(nn, n); return edgeCnt[(Math.Min(n, nn[(k + 1) % c]), Math.Max(n, nn[(k + 1) % c]))] == 1 || edgeCnt[(Math.Min(n, nn[(k + c - 1) % c]), Math.Max(n, nn[(k + c - 1) % c]))] == 1; });
    for (int comp = 5; comp <= 7; comp++)
    foreach (int scope in new[] { 0, 1 })
    foreach (double fac in new[] { 1.0, 0.5 })
    {
        var cache = new Dictionary<int, double[]>();
        double[] Own(int n)
        {
            if (cache.TryGetValue(n, out var r)) return r;
            var ax = Avg(n, 3); var ay = Avg(n, 4); var axy = Avg(n, comp); bool use = scope == 0 || Sinir(n);
            double xa = 0, xu = 0, ya = 0, yu = 0;
            foreach (var c in cl) { double mx = Dot(c, ax), my = Dot(c, ay), t = use ? fac * Math.Abs(Dot(c, axy)) : 0; xa = Math.Max(xa, mx + t); xu = Math.Min(xu, mx - t); ya = Math.Max(ya, my + t); yu = Math.Min(yu, my - t); }
            return cache[n] = new[] { xa, xu, ya, yu };
        }
        int okS = 0, totS = 0, okI = 0, totI = 0;
        foreach (var kv in gA)
        {
            int n = kv.Key; if (conn[n].Count == 0) continue; var s = Set(n, 6); bool sn = Sinir(n);
            for (int i = 0; i < 4; i++) { double m = s.Average(x => Own(x)[i]); bool ok = Math.Abs(Math.Round(m, 2, MidpointRounding.AwayFromZero) - kv.Value[6 + i]) <= 0.0101; if (sn) { totS++; if (ok) okS++; } else { totI++; if (ok) okI++; } }
        }
        Console.WriteLine($"SMXY comp{comp} {(scope == 0 ? "her yer" : "sinirda")} x{fac}: sinir {okS}/{totS}, ic {okI}/{totI}");
    }
}

// ---- 28) As formulu genel izgara (pas payi, k, fy) + oran
if (Environment.GetEnvironmentVariable("ASG") == "1")
{
    var best = new List<(int ok, int tot, double k, double fy, double c)>();
    foreach (double fy in new[] { 2800.0, 3652.17 })
    for (double cover = 4; cover <= 12; cover += 0.5)
    for (double k = 100; k <= 300; k += 1)
    {
        int ok = 0, tot = 0;
        foreach (var kv in gA)
        {
            var v = kv.Value; if (v[1] >= 500) continue; double hh = v[1], d = hh - cover;
            double[] cap = { Hdr(7), Hdr(6), Hdr(9), Hdr(8) };
            for (int i = 0; i < 4; i++) { double g = v[10 + i]; if (g <= 0.002 * 100 * hh || g >= cap[i] - 0.011) continue; tot++; double M = Math.Abs(v[6 + i]) * 1e5, A = k * 100, disc = d * d - 2 * M / A; if (disc < 0) continue; double a = d - Math.Sqrt(disc); if (Math.Abs(A * a / fy - g) <= 0.0151) ok++; }
        }
        best.Add((ok, tot, k, fy, cover));
    }
    foreach (var b in best.OrderByDescending(x => x.ok).Take(4)) Console.WriteLine($"ASG {b.ok}/{b.tot} k={b.k} fy={b.fy} pas={b.c}");
}

// ---- 29) her noktadan ic kol z = M/(As fy) -> z/d dagilimi
if (Environment.GetEnvironmentVariable("ZD") == "1")
{
    var rows = new List<(double mu, double zd, double As, double M, double h)>();
    foreach (var kv in gA)
    {
        var v = kv.Value; if (v[1] >= 500) continue; double hh = v[1], d = hh - 7;
        for (int i = 0; i < 4; i++) { double g = v[10 + i], Mt = Math.Abs(v[6 + i]); if (g <= 0.002 * 100 * hh || Mt < 0.5) continue; double z = Mt * 1e5 / (g * 2800); rows.Add((Mt * 1e5 / (100 * d * d), z / d, g, Mt, hh)); }
    }
    foreach (var r in rows.OrderBy(x => x.mu).Where((x, i) => i % Math.Max(1, rows.Count / 25) == 0)) Console.WriteLine($"ZD M={r.M,6:F2} As={r.As,6:F2} h={r.h} M/bd2={r.mu,6:F2} z/d={r.zd:F4}");
}

// ---- 30) As formulu basarisiz noktalar
if (Environment.GetEnvironmentVariable("ASF") is string asf)
{
    double k = double.Parse(asf, inv); double[] cap = { Hdr(7), Hdr(6), Hdr(9), Hdr(8) }; string[] ad = { "Xa", "Xu", "Ya", "Yu" };
    var rows = new List<string>();
    foreach (var kv in gA)
    {
        var v = kv.Value; if (v[1] >= 500) continue; double d = v[1] - 7;
        for (int i = 0; i < 4; i++) { double g = v[10 + i]; if (g <= 0.002 * 100 * v[1] || g >= cap[i] - 0.011) continue; double M = Math.Abs(v[6 + i]) * 1e5, A = k * 100, disc = d * d - 2 * M / A; double a = d - Math.Sqrt(Math.Max(0, disc)); double As = A * a / 2800; rows.Add($"{(Math.Abs(As - g) <= 0.0151 ? "OK " : "BAD")} n{kv.Key} {ad[i]} M={v[6 + i]} GPR={g} biz={As:F3} fark={g - As:+0.000;-0.000}"); }
    }
    foreach (var r in rows.Where(r => r.StartsWith("BAD")).Take(25)) Console.WriteLine("ASF " + r);
}

// ---- 31) iki dalli As formulu
if (Environment.GetEnvironmentVariable("AS2") == "1")
{
    double Blk(double M, double d, double k, double fy) { double A = k * 100, disc = d * d - 2 * M / A; double a = d - Math.Sqrt(Math.Max(0, disc)); return A * a / fy; }
    var best = new List<(int ok, int tot, double k1, double k2, double rho, double fy2)>();
    foreach (double rho in new[] { 0.0032858 })
    foreach (double fy2 in new[] { 3652.17, 3500.0, 3600.0, 3700.0 })
    for (double k1 = 120; k1 <= 260; k1 += 1)
    foreach (double k2 in new[] { -1.0 })
    {
        int ok = 0, tot = 0;
        foreach (var kv in gA)
        {
            var v = kv.Value; if (v[1] >= 500) continue; double d = v[1] - 7;
            for (int i = 0; i < 4; i++) { double g = v[10 + i]; if (g <= 0.002 * 100 * v[1]) continue; tot++; double M = Math.Abs(v[6 + i]) * 1e5; double a1 = Blk(M, d, k1, 2800), lim = rho * 100 * d; double As = a1 <= lim ? a1 : Math.Max(lim, Blk(M, d, k1, fy2)); if (Math.Abs(As - g) <= 0.0151) ok++; }
        }
        best.Add((ok, tot, k1, k2, rho, fy2));
    }
    foreach (var b in best.OrderByDescending(x => x.ok).Take(4)) Console.WriteLine($"AS2 {b.ok}/{b.tot} k={b.k1} fy2={b.fy2}");
}

// ---- 32) z = M/(As*2800) ~ d_eff - s*As regresyonu (sinir alti noktalar)
if (Environment.GetEnvironmentVariable("ZREG") == "1")
{
    var xs = new List<double>(); var ys = new List<double>(); double hh = 0;
    foreach (var kv in gA)
    {
        var v = kv.Value; if (v[1] >= 500) continue; hh = v[1]; double d = v[1] - 7, lim = 0.0032858 * 100 * d;
        for (int i = 0; i < 4; i++) { double g = v[10 + i], M = Math.Abs(v[6 + i]); if (g <= 0.002 * 100 * v[1] || g >= lim - 0.02 || M < 1) continue; xs.Add(g); ys.Add(M * 1e5 / (g * 2800)); }
    }
    int n = xs.Count; double mx = xs.Average(), my = ys.Average(); double sxy = 0, sxx = 0; for (int i = 0; i < n; i++) { sxy += (xs[i] - mx) * (ys[i] - my); sxx += (xs[i] - mx) * (xs[i] - mx); }
    double s = sxy / sxx, b0 = my - s * mx; double res = 0; for (int i = 0; i < n; i++) res = Math.Max(res, Math.Abs(ys[i] - (b0 + s * xs[i])));
    Console.WriteLine($"ZREG h={hh} n={n} d_eff={b0:F3} egim={s:F5} -> k=fy/(200*egim)={2800 / (200 * -s):F1}  max artik={res:F3} cm");
}

// ---- 33) fiziksel parametrelerle iki dalli As: k = 0.85*fck/1.5, d = h - c
if (Environment.GetEnvironmentVariable("AS3") is string fckS)
{
    double fck = double.Parse(fckS, inv);
    double Blk(double M, double d, double k, double fy) { double A = k * 100, disc = d * d - 2 * M / A; double a = d - Math.Sqrt(Math.Max(0, disc)); return A * a / fy; }
    foreach (double kf in new[] { 0.85 / 1.5, 0.85 * 0.85 / 1.5 * 1.0, 1.0 / 1.5 * 0.85 * 1.0 })
    for (double c = 6.0; c <= 7.51; c += 0.25)
    foreach (double fy2 in new[] { 3652.17 })
    {
        double k = kf * fck; int ok = 0, tot = 0; var bad = new List<string>();
        foreach (var kv in gA)
        {
            var v = kv.Value; if (v[1] >= 500) continue; double d = v[1] - c, dlim = v[1] - 7;
            for (int i = 0; i < 4; i++) { double g = v[10 + i]; if (g <= 0.002 * 100 * v[1]) continue; tot++; double M = Math.Abs(v[6 + i]) * 1e5; double a1 = Blk(M, d, k, 2800), lim = 0.0032858 * 100 * dlim; double As = a1 <= lim ? a1 : Math.Max(lim, Blk(M, d, k, fy2)); if (Math.Abs(As - g) <= 0.0151) ok++; else if (bad.Count < 3) bad.Add($"n{kv.Key} M={v[6 + i]} {As:F2}/{g}"); }
        }
        Console.WriteLine($"AS3 k={k:F1} c={c:F2}: {ok}/{tot}  {string.Join("; ", bad)}");
    }
}

// ---- 34) nihai As formulu: k=0.85 fck/1.5, d=h-6.8, fy=2800; As>0.0032858 b (h-7) ise max(sinir, As(fy=fyk/1.15))
if (Environment.GetEnvironmentVariable("ASN") is string fckN)
{
    double fck = double.Parse(fckN, inv), k = 0.85 * fck / 1.5;
    double Blk(double M, double d, double fy) { double A = k * 100, disc = d * d - 2 * M / A; double a = d - Math.Sqrt(Math.Max(0, disc)); return A * a / fy; }
    int t = 0, o15 = 0, o05 = 0, o25 = 0;
    foreach (var kv in gA)
    {
        var v = kv.Value; if (v[1] >= 500) continue;
        for (int i = 0; i < 4; i++) { double g = v[10 + i]; if (g <= 0.002 * 100 * v[1]) continue; t++; double M = Math.Abs(v[6 + i]) * 1e5, d = v[1] - 6.8, lim = 0.0032858 * 100 * (v[1] - 7); double a1 = Blk(M, d, 2800); double As = a1 <= lim ? a1 : Math.Max(lim, Blk(M, d, 4200 / 1.15)); double e = Math.Abs(As - g); if (e <= 0.0151) o15++; if (e <= 0.05) o05++; if (e <= 0.25) o25++; }
    }
    Console.WriteLine($"ASN fck={fck}: tam(±0.015) {o15}/{t}, ±0.05 {o05}/{t}, ±0.25 {o25}/{t}");
}

// ---- 35) uctan uca (nihai As formulu)
if (Environment.GetEnvironmentVariable("E2E") is string fckE)
{
    double fck = double.Parse(fckE, inv), k = 0.85 * fck / 1.5;
    double Blk(double M, double d, double fy) { double A = k * 100, disc = d * d - 2 * M / A; double a = d - Math.Sqrt(Math.Max(0, disc)); return A * a / fy; }
    double AsN(double Mtm, double h) { if (Math.Abs(Mtm) < 1e-6) return 0; double M = Math.Abs(Mtm) * 1e5, d = h - 6.8, lim = 0.0032858 * 100 * (h - 7); double a1 = Blk(M, d, 2800); double As = a1 <= lim ? a1 : Math.Max(lim, Blk(M, d, 4200 / 1.15)); return Math.Max(As, Math.Max(rhoMin, 0.001) * 100 * (h - 7)); }
    double[] genel = { Hdr(3), Hdr(2), Hdr(5), Hdr(4) }; string[] ad = { "X alt", "X ust", "Y alt", "Y ust" };
    var cache = new Dictionary<int, double[]>(); double[] D0(int x) { if (!cache.TryGetValue(x, out var v)) cache[x] = v = DesA(x, 0); return v; }
    for (int i = 0; i < 4; i++)
    {
        int tp = 0, fp = 0, fn = 0, w = 0, cnt = 0; double mg = 0, mb = 0, fnMax = 0;
        foreach (var kv in gA)
        {
            int n = kv.Key; if (conn[n].Count == 0 || kv.Value[1] >= 500) continue; cnt++;
            var s = Set(n, 6); double ab = AsN(s.Average(x => D0(x)[i]), kv.Value[1]), ag = kv.Value[10 + i];
            if (Math.Abs(ab - ag) <= 0.25) w++; mg = Math.Max(mg, ag); mb = Math.Max(mb, ab);
            bool g = ag > genel[i] + 1e-6, b = ab > genel[i] + 1e-6; if (g && b) tp++; else if (b) fp++; else if (g) { fn++; fnMax = Math.Max(fnMax, ag - genel[i]); }
        }
        Console.WriteLine($"E2E {ad[i]} genel={genel[i]:F2}: ±0.25 {w}/{cnt}; max GPR/biz {mg:F2}/{mb:F2}; ilave ikisi={tp} yalnizBiz={fp} yalnizGPR={fn} (en buyuk kacan ilave {fnMax:F2})");
    }
}

// ---- 36) varyant zarfi: STA'nin olasi ortalama kurallarinin zarfi
if (Environment.GetEnvironmentVariable("ZARFV") is string fckZ)
{
    double fck = double.Parse(fckZ, inv), k = 0.85 * fck / 1.5;
    double Blk(double M, double d, double fy) { double A = k * 100, disc = d * d - 2 * M / A; double a = d - Math.Sqrt(Math.Max(0, disc)); return A * a / fy; }
    double AsN(double Mtm, double h) { if (Math.Abs(Mtm) < 1e-6) return 0; double M = Math.Abs(Mtm) * 1e5, d = h - 6.8, lim = 0.0032858 * 100 * (h - 7); double a1 = Blk(M, d, 2800); double As = a1 <= lim ? a1 : Math.Max(lim, Blk(M, d, 4200 / 1.15)); return Math.Max(As, Math.Max(rhoMin, 0.001) * 100 * (h - 7)); }
    double[] genel = { Hdr(3), Hdr(2), Hdr(5), Hdr(4) }; double[] cap = { Hdr(7), Hdr(6), Hdr(9), Hdr(8) }; string[] ad = { "X alt", "X ust", "Y alt", "Y ust" };
    var cZ = new Dictionary<int, double[]>(); var cR = new Dictionary<int, double[]>();
    double[] DZ(int x) { if (!cZ.TryGetValue(x, out var v)) { TriZero = true; cZ[x] = v = DesA(x, 0); } return v; }
    double[] DR(int x) { if (!cR.TryGetValue(x, out var v)) { TriZero = false; cR[x] = v = DesA(x, 0); } return v; }
    double Pick(int i, IEnumerable<double> vals) => i % 2 == 0 ? vals.Max() : vals.Min();
    var variants = new (string ad, Func<int, int, double> f)[]
    {
        ("V1 N6 sifirli", (n, i) => Set(n, 6).Average(x => DZ(x)[i])),
        ("zarf N3/N6/N7 x sifirli/ham", (n, i) => Pick(i, new[] { 3, 6, 7 }.SelectMany(kd => new[] { Set(n, kd).Average(x => DZ(x)[i]), Set(n, kd).Average(x => DR(x)[i]) }))),
        ("zarf + birini-cikar (N6)", (n, i) => { var s = Set(n, 6); var vals = new List<double> { s.Average(x => DZ(x)[i]) }; if (s.Count > 2) foreach (var drop in s.Where(x => x != n)) vals.Add(s.Where(x => x != drop).Average(x => DZ(x)[i])); return Pick(i, vals); }),
    };
    bool gprVar = gA.Count > 0;
    var ids = Enumerable.Range(1, nNode).Where(n => conn[n].Count > 0).ToList();
    var hNode = new Dictionary<int, double>(); foreach (var n in ids) hNode[n] = conn[n].Average(t => meshD[t.e]) * 100;
    foreach (var (vad, f) in variants)
    {
        var line = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            int tp = 0, fp = 0, fn = 0, w = 0, cnt = 0, nb = 0; double mb = 0, mg = 0;
            foreach (var n in ids)
            {
                double ab = AsN(f(n, i), hNode[n]); if (cap[i] > 0) ab = Math.Min(ab, Math.Max(cap[i], genel[i])); mb = Math.Max(mb, ab); bool b = ab > genel[i] + 1e-6; if (b) nb++;
                if (!gprVar || !gA.ContainsKey(n)) continue; cnt++; double ag = gA[n][10 + i]; mg = Math.Max(mg, ag); if (Math.Abs(ab - ag) <= 0.25) w++;
                bool g = ag > genel[i] + 1e-6; if (g && b) tp++; else if (b) fp++; else if (g) fn++;
            }
            line.Add(gprVar ? $"{ad[i]}: kacan {fn} fazla {fp} (ortak {tp}) ±.25 {w}/{cnt}" : $"{ad[i]}: ilave dugum {nb}, max biz/SO1 {mb:F2}/{cap[i]:F2}");
        }
        Console.WriteLine($"ZARFV {vad,-28} | {string.Join(" | ", line)}");
    }
}

// ---- 37) kacan ilave dugumleri: bizim bir ilave dugumune uzaklik ve buyukluk (zarf varyanti)
if (Environment.GetEnvironmentVariable("KACAN") is string fckK)
{
    var edgeCntK = new Dictionary<(int, int), int>();
    foreach (var q in mesh) { var nn = q.Where(x => x > 0).ToArray(); for (int j = 0; j < nn.Length; j++) { int a = nn[j], b = nn[(j + 1) % nn.Length]; var key = (Math.Min(a, b), Math.Max(a, b)); edgeCntK[key] = edgeCntK.GetValueOrDefault(key) + 1; } }
    int EC(int a, int b) => edgeCntK.GetValueOrDefault((Math.Min(a, b), Math.Max(a, b)));
    double fck = double.Parse(fckK, inv), k = 0.85 * fck / 1.5;
    double Blk(double M, double d, double fy) { double A = k * 100, disc = d * d - 2 * M / A; double a = d - Math.Sqrt(Math.Max(0, disc)); return A * a / fy; }
    double AsN(double Mtm, double h) { if (Math.Abs(Mtm) < 1e-6) return 0; double M = Math.Abs(Mtm) * 1e5, d = h - 6.8, lim = 0.0032858 * 100 * (h - 7); double a1 = Blk(M, d, 2800); double As = a1 <= lim ? a1 : Math.Max(lim, Blk(M, d, 4200 / 1.15)); return Math.Max(As, Math.Max(rhoMin, 0.001) * 100 * (h - 7)); }
    double[] genel = { Hdr(3), Hdr(2), Hdr(5), Hdr(4) }; string[] ad = { "X alt", "X ust", "Y alt", "Y ust" };
    var cZ = new Dictionary<int, double[]>(); var cR = new Dictionary<int, double[]>();
    double[] DZ(int x) { if (!cZ.TryGetValue(x, out var v)) { TriZero = true; cZ[x] = v = DesA(x, 0); } return v; }
    double[] DR(int x) { if (!cR.TryGetValue(x, out var v)) { TriZero = false; cR[x] = v = DesA(x, 0); } return v; }
    int mode = int.Parse(Environment.GetEnvironmentVariable("KMODE") ?? "1");
    double F(int n, int i) { if (mode == 0) return Set(n, 6).Average(x => DZ(x)[i]); var vals = new[] { 3, 6, 7 }.SelectMany(kd => new[] { Set(n, kd).Average(x => DZ(x)[i]), Set(n, kd).Average(x => DR(x)[i]) }).ToList(); if (mode >= 2 && conn[n].All(t => mesh[t.e][3] == 0)) vals.Add(0.5 * (DZ(n)[i] + Set(n, 6).Average(x => DZ(x)[i])));
        bool triB = conn[n].Any(t => mesh[t.e][3] == 0) && Set(n, 3).Any(x => x != n && EC(n, x) == 1);
        if (mode == 3 || (mode == 4 && triB)) { var s3 = Set(n, 3).Where(x => x == n || EC(n, x) != 1).ToList(); if (s3.Count >= 2) { vals.Add(s3.Average(x => DZ(x)[i])); vals.Add(s3.Average(x => DR(x)[i])); } } return i % 2 == 0 ? vals.Max() : vals.Min(); }
    var ids = gA.Keys.Where(n => conn[n].Count > 0 && gA[n][1] < 500).ToList();
    for (int i = 0; i < 4; i++)
    {
        var ours = ids.Where(n => AsN(F(n, i), gA[n][1]) > genel[i] + 1e-6).ToList();
        int fpC = ours.Count(n => gA[n][10 + i] <= genel[i] + 1e-6);
        { var gpr = ids.Where(n => gA[n][10 + i] > genel[i] + 1e-6).ToList(); foreach (var n in ours.Where(n => gA[n][10 + i] <= genel[i] + 1e-6)) { double ex = AsN(F(n, i), gA[n][1]) - genel[i]; double dist = gpr.Count == 0 ? 99 : gpr.Min(x => Math.Sqrt(Math.Pow(gA[x][3] - gA[n][3], 2) + Math.Pow(gA[x][4] - gA[n][4], 2))); Console.WriteLine($"FAZLA {ad[i]} n{n} fazla {ex:F2} gprIlaveye {dist:F2}"); } } Console.WriteLine($"KOZET {ad[i]} fazla {fpC} bizim ilave {ours.Count}");
        foreach (var n in ids)
        {
            double ag = gA[n][10 + i]; if (ag <= genel[i] + 1e-6 || ours.Contains(n)) continue;
            double dist = ours.Count == 0 ? 99 : ours.Min(x => Math.Sqrt(Math.Pow(gA[x][3] - gA[n][3], 2) + Math.Pow(gA[x][4] - gA[n][4], 2)));
            Console.WriteLine($"KACAN {ad[i]} n{n} GPR ilave {ag - genel[i]:F2} biz As {AsN(F(n, i), gA[n][1]):F2} en yakin bizim ilave dugum {dist:F2} m {(dist <= 0.55 ? "(50 cm uzatmayla ortulur)" : dist <= 1.1 ? "(komsu)" : "(YALNIZ)")}");
        }
    }
}
