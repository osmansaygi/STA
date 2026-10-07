using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DosemeDonatiDump
{
    internal static class Program
    {
        private const int RecordBytes = 250;
        private const int RebarOffset = 96;

        private static readonly Regex RxSlabIdX = new Regex(
            @"(D(?:\d+[A-Z]{1,2}|[A-Z]{1,2}\d?|\d+)-\d+)\s+X",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex RxSlabY = new Regex(
            @"d\s*=\s*\d+\s*cm[\s\u00A1\uFFFD]{0,12}Y\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex RxCap = new Regex(
            @"(\d+)\s*/\s*(\d+)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex RxParen = new Regex(
            @"\(([^)]*)\)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private sealed class Bar
        {
            public int? Dia;
            public int? Spacing;
            public string Kind;
            public string Text;
        }

        private sealed class AxisRec
        {
            public List<Bar> X = new List<Bar>();
            public List<Bar> Y = new List<Bar>();
            public string XText = "";
            public string YText = "";
            public string Id = "";
        }

        private static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: DosemeDonatiDump <gpr> <dmx> <out.json>");
                return 1;
            }
            string gprPath = args[0];
            string dmxPath = args[1];
            string outJson = args[2];

            var dmx = ReadDmx(dmxPath);
            var gpr = ReadGpr(gprPath);
            var rows = new List<object>();
            int max = Math.Max(dmx.Count, gpr.Count);
            int sameX = 0, diffX = 0, cutX = 0, sameY = 0, diffY = 0, cutY = 0;

            for (int i = 0; i < max; i++)
            {
                AxisRec d = i < dmx.Count ? dmx[i] : null;
                AxisRec g = i < gpr.Count ? gpr[i] : null;
                bool both = d != null && g != null;
                string xs = Status(both ? g.X : null, both ? d.X : null, both);
                string ys = Status(both ? g.Y : null, both ? d.Y : null, both);
                CountStatus(xs, ref sameX, ref diffX, ref cutX);
                CountStatus(ys, ref sameY, ref diffY, ref cutY);

                rows.Add(new Dictionary<string, object>
                {
                    ["i"] = i + 1,
                    ["id"] = g != null ? g.Id : ("DMX#" + (i + 1)),
                    ["gprX"] = g != null ? FormatParts(g.X, g.XText) : "",
                    ["gprY"] = g != null ? FormatParts(g.Y, g.YText) : "",
                    ["dmxX"] = d != null ? FormatParts(d.X, d.XText) : "",
                    ["dmxY"] = d != null ? FormatParts(d.Y, d.YText) : "",
                    ["xStatus"] = xs,
                    ["yStatus"] = ys,
                    ["gprXRaw"] = g?.XText ?? "",
                    ["gprYRaw"] = g?.YText ?? "",
                });
            }

            var result = new Dictionary<string, object>
            {
                ["project"] = Path.GetFileNameWithoutExtension(gprPath),
                ["gprPath"] = gprPath,
                ["dmxPath"] = dmxPath,
                ["gprCount"] = gpr.Count,
                ["dmxCount"] = dmx.Count,
                ["summary"] = new Dictionary<string, object>
                {
                    ["sameX"] = sameX,
                    ["diffX"] = diffX,
                    ["cutX"] = cutX,
                    ["sameY"] = sameY,
                    ["diffY"] = diffY,
                    ["cutY"] = cutY,
                },
                ["rows"] = rows,
            };

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outJson)) ?? ".");
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            File.WriteAllText(outJson, ser.Serialize(result), new UTF8Encoding(false));
            Console.WriteLine(
                "Wrote {0} GPR={1} DMX={2} sameX={3} diffX={4} cutX={5} sameY={6} diffY={7} cutY={8}",
                outJson, gpr.Count, dmx.Count, sameX, diffX, cutX, sameY, diffY, cutY);
            return 0;
        }

        private static void CountStatus(string s, ref int same, ref int diff, ref int cut)
        {
            if (s == "ayni") same++;
            else if (s == "farkli") diff++;
            else if (s == "GPR kesik / DMX tamamlar") cut++;
        }

        private static string FormatParts(List<Bar> bars, string fallback)
        {
            if (bars == null || bars.Count == 0) return fallback ?? "";
            return string.Join("+", bars.Select(b => b.Text));
        }

        private static string Status(List<Bar> gpr, List<Bar> dmx, bool both)
        {
            if (!both) return "-";
            var cg = CapSeq(gpr);
            var cd = CapSeq(dmx);
            if (cg.Count == 0 && cd.Count == 0) return "bos";
            if (cg.Count == 0) return "sadece DMX";
            if (cd.Count == 0) return "sadece GPR";
            bool ok = true;
            for (int k = 0; k < cg.Count; k++)
            {
                if (k >= cd.Count || cg[k] != cd[k]) { ok = false; break; }
            }
            if (ok && cg.Count < cd.Count) return "GPR kesik / DMX tamamlar";
            if (ok && cg.Count == cd.Count) return "ayni";
            return "farkli";
        }

        private static List<string> CapSeq(List<Bar> bars)
        {
            var list = new List<string>();
            if (bars == null) return list;
            foreach (var b in bars)
            {
                if (b.Dia.HasValue && b.Spacing.HasValue)
                    list.Add(b.Dia.Value + "/" + b.Spacing.Value);
            }
            return list;
        }

        private static List<AxisRec> ReadDmx(string path)
        {
            var list = new List<AxisRec>();
            byte[] b = File.ReadAllBytes(path);
            int n = b.Length / RecordBytes;
            for (int r = 0; r < n; r++)
            {
                int o = r * RecordBytes + RebarOffset;
                var rec = new AxisRec { Id = "DMX#" + (r + 1) };
                AddDmx(rec.X, b, o, "duz");
                AddDmx(rec.X, b, o + 6, "pilye");
                AddDmx(rec.X, b, o + 10, "sol ilave");
                AddDmx(rec.X, b, o + 14, "sag ilave");
                AddDmx(rec.Y, b, o + 18, "duz");
                AddDmx(rec.Y, b, o + 24, "pilye");
                AddDmx(rec.Y, b, o + 32, "sol ilave");
                AddDmx(rec.Y, b, o + 28, "sag ilave");
                rec.XText = FormatParts(rec.X, "");
                rec.YText = FormatParts(rec.Y, "");
                list.Add(rec);
            }
            return list;
        }

        private static void AddDmx(List<Bar> dst, byte[] b, int o, string kind)
        {
            if (o + 4 > b.Length) return;
            short dia = BitConverter.ToInt16(b, o);
            short sp = BitConverter.ToInt16(b, o + 2);
            if (dia < 6 || dia > 40 || sp < 1 || sp > 200) return;
            dst.Add(new Bar
            {
                Dia = dia,
                Spacing = sp,
                Kind = kind,
                Text = string.Format(CultureInfo.InvariantCulture, "ø{0}/{1}({2})", dia, sp, kind),
            });
        }

        private static List<AxisRec> ReadGpr(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Encoding enc = Encoding.GetEncoding(1254);
            string[] lines = enc.GetString(bytes).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.IndexOf("BETONARME", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (l.IndexOf("HESAP", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (l.IndexOf("SONU", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (l.IndexOf("KOLON", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                bool hasDoseme =
                    l.IndexOf("DÖŞEME", StringComparison.OrdinalIgnoreCase) >= 0
                    || l.IndexOf("DŞEME", StringComparison.OrdinalIgnoreCase) >= 0
                    || l.IndexOf("EME BETONARME", StringComparison.OrdinalIgnoreCase) >= 0
                    || (l.IndexOf("DEME", StringComparison.OrdinalIgnoreCase) >= 0
                        && l.IndexOf("KİRİŞ", StringComparison.OrdinalIgnoreCase) < 0
                        && l.IndexOf("KIRIS", StringComparison.OrdinalIgnoreCase) < 0);
                if (hasDoseme) { start = i; break; }
            }
            if (start < 0) throw new Exception("GPR DOSEME BETONARME bulunamadi: " + path);

            int end = -1;
            for (int i = start + 1; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.IndexOf("PANEL", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (l.IndexOf(" VE ", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (l.IndexOf("DUVAR", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (l.IndexOf("BILG", StringComparison.OrdinalIgnoreCase) >= 0
                    || l.IndexOf("BİLG", StringComparison.OrdinalIgnoreCase) >= 0
                    || l.IndexOf("BÝLG", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    end = i;
                    break;
                }
            }
            if (end < 0) throw new Exception("GPR KIRIS VE PANEL bitisi bulunamadi: " + path);

            var ordered = new List<AxisRec>();
            var dict = new Dictionary<string, AxisRec>(StringComparer.OrdinalIgnoreCase);
            string pendingKey = null;
            string pendingX = null;

            for (int i = start; i < end; i++)
            {
                string raw = lines[i];
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (raw.Length > 4 && char.IsDigit(raw[0]) && raw.StartsWith("130,", StringComparison.Ordinal))
                {
                    if (i + 1 < end) { raw = lines[++i]; }
                    else continue;
                }

                string lineForMatch = StripMarkers(raw);
                var mX = RxSlabIdX.Match(lineForMatch);
                if (mX.Success)
                {
                    string key = mX.Groups[1].Value.ToUpperInvariant();
                    string don = ExtractDonatiCell(raw);
                    if (pendingKey != null)
                        AddGpr(ordered, dict, pendingKey, pendingX, "");
                    pendingKey = key;
                    pendingX = don ?? "";
                    continue;
                }

                bool yRow = RxSlabY.IsMatch(lineForMatch)
                    || (pendingKey != null
                        && lineForMatch.IndexOf("d=", StringComparison.OrdinalIgnoreCase) >= 0
                        && Regex.IsMatch(lineForMatch, @"\bY\b", RegexOptions.IgnoreCase));
                if (yRow && pendingKey != null)
                {
                    string yDon = ExtractDonatiCell(raw) ?? "";
                    AddGpr(ordered, dict, pendingKey, pendingX, yDon);
                    pendingKey = null;
                    pendingX = null;
                }
            }
            if (pendingKey != null)
                AddGpr(ordered, dict, pendingKey, pendingX, "");

            return ordered;
        }

        private static void AddGpr(
            List<AxisRec> ordered,
            Dictionary<string, AxisRec> dict,
            string key,
            string xText,
            string yText)
        {
            var rec = new AxisRec
            {
                Id = key,
                XText = xText ?? "",
                YText = yText ?? "",
                X = ParseCell(xText),
                Y = ParseCell(yText),
            };
            if (dict.ContainsKey(key))
            {
                int idx = ordered.FindIndex(r => string.Equals(r.Id, key, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) ordered[idx] = rec;
                dict[key] = rec;
            }
            else
            {
                dict[key] = rec;
                ordered.Add(rec);
            }
        }

        private static string StripMarkers(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                if (c == '\u00A1' || c == '\uFFFD' || c == '\uFEFF' || char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '?' && i + 1 < line.Length && line[i + 1] == 'D') { i++; continue; }
                break;
            }
            return i > 0 ? line.Substring(i) : line;
        }

        private static string ExtractDonatiCell(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            string[] parts = line.Split('\u00A1');
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                string t = parts[i].Trim();
                if (t.Length == 0) continue;
                if (RxCap.IsMatch(t)) return t;
                if (t.IndexOf('\u00F8') >= 0 || t.IndexOf('\u00D8') >= 0) return t;
                if (t.IndexOf("(d", StringComparison.OrdinalIgnoreCase) >= 0 && t.IndexOf(')') >= 0) return t;
            }
            return null;
        }

        private static List<Bar> ParseCell(string cell)
        {
            var list = new List<Bar>();
            if (string.IsNullOrWhiteSpace(cell)) return list;
            string collapsed = Regex.Replace(cell.Trim(), @"[\r\n]+", "");
            foreach (string seg0 in SplitPlus(collapsed))
            {
                string seg = seg0.Trim();
                if (seg.Length == 0) continue;
                string kind = "bilinmiyor";
                var m = RxParen.Match(seg);
                if (m.Success) kind = KindFromParen(m.Groups[1].Value);
                else if (Regex.IsMatch(seg, @"\(\s*sa?\s*$", RegexOptions.IgnoreCase)) kind = "sag ilave";
                else if (Regex.IsMatch(seg, @"\(\s*(?:Mon\.?|Mo|M)\s*$", RegexOptions.IgnoreCase)) kind = "montaj";

                int? dia = null, sp = null;
                var cm = RxCap.Match(seg);
                if (cm.Success)
                {
                    dia = int.Parse(cm.Groups[1].Value, CultureInfo.InvariantCulture);
                    sp = int.Parse(cm.Groups[2].Value, CultureInfo.InvariantCulture);
                }
                list.Add(new Bar
                {
                    Dia = dia,
                    Spacing = sp,
                    Kind = kind,
                    Text = dia.HasValue
                        ? string.Format(CultureInfo.InvariantCulture, "ø{0}/{1}({2})", dia, sp, kind)
                        : seg,
                });
            }
            return list;
        }

        private static IEnumerable<string> SplitPlus(string cell)
        {
            var list = new List<string>();
            int depth = 0;
            var sb = new StringBuilder();
            foreach (char c in cell ?? "")
            {
                if (c == '(') depth++;
                else if (c == ')') depth = Math.Max(0, depth - 1);
                if (c == '+' && depth == 0)
                {
                    if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); }
                    continue;
                }
                sb.Append(c);
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            return list;
        }

        private static string KindFromParen(string inner)
        {
            if (string.IsNullOrWhiteSpace(inner)) return "bilinmiyor";
            string n = Normalize(inner);
            if (n.Contains("sol") && (n.Contains("ila") || n == "sol")) return "sol ilave";
            if (n == "sol") return "sol ilave";
            if ((n.Contains("sag") || n.Contains("sağ")) && n.Contains("ila")) return "sag ilave";
            if (n == "s" || n == "sa" || n == "sag") return "sag ilave";
            if (n.Contains("mon") || n == "m" || n == "mo") return "montaj";
            if (n.Contains("pil")) return "pilye";
            if (n.Contains("duz") || n == "dz") return "duz";
            return "bilinmiyor";
        }

        private static string Normalize(string s)
        {
            var b = new StringBuilder(s.Length);
            foreach (char c in s.Trim().ToLowerInvariant())
            {
                switch (c)
                {
                    case 'ı': case 'İ': b.Append('i'); break;
                    case 'ğ': b.Append('g'); break;
                    case 'ü': b.Append('u'); break;
                    case 'ş': b.Append('s'); break;
                    case 'ö': b.Append('o'); break;
                    case 'ç': b.Append('c'); break;
                    default: b.Append(c); break;
                }
            }
            return b.ToString();
        }
    }
}
