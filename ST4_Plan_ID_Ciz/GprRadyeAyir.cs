using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace ST4PlanIdCiz
{
    /// <summary>GPR genel donatı. Alan cm²/m; sıra X üst, X alt, Y üst, Y alt.</summary>
    public sealed class GprRadyeGenelDonati
    {
        public int CapXUstMm, AralikXUstCm, CapXAltMm, AralikXAltCm;
        public int CapYUstMm, AralikYUstCm, CapYAltMm, AralikYAltCm;
        public double AsXUst, AsXAlt, AsYUst, AsYAlt;

        public string Yazi(int index)
        {
            switch (index)
            {
                case 0: return "ø" + CapXUstMm + "/" + AralikXUstCm;
                case 1: return "ø" + CapXAltMm + "/" + AralikXAltCm;
                case 2: return "ø" + CapYUstMm + "/" + AralikYUstCm;
                default: return "ø" + CapYAltMm + "/" + AralikYAltCm;
            }
        }

        public double As(int index)
        {
            switch (index)
            {
                case 0: return AsXUst;
                case 1: return AsXAlt;
                case 2: return AsYUst;
                default: return AsYAlt;
            }
        }
    }

    /// <summary>GPR radye mesh satırı. Asax/Asux/Asay/Asuy = X alt, X üst, Y alt, Y üst (cm²/m).</summary>
    public sealed class GprRadyeDugumAs
    {
        public int No;
        public double X;
        public double Y;
        public double Z;
        public double AsXAlt;
        public double AsXUst;
        public double AsYAlt;
        public double AsYUst;
    }

    /// <summary>
    /// Radye analiz raporu GPR sonuna eklenir. Kolon, döşeme, perde ve antet okuyucuları
    /// bu satırdan sonrasını görmez. Mesh As tablosu yalnız buradan okunur.
    /// </summary>
    public static class GprRadyeAyir
    {
        public static bool RaporBaslangici(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            if (line.IndexOf("RADYE PROGRAMI", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (line.IndexOf("RADYE ANALIZ", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (line.IndexOf("RADYE MAT TEMEL", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static int IlkRaporSatiri(IList<string> lines)
        {
            if (lines == null) return -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (RaporBaslangici(lines[i])) return i;
            }
            return -1;
        }

        /// <summary>Radye raporu varsa metni ondan keser. Yoksa metin aynı kalır.</summary>
        public static string YapidanOnce(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            int cut = IndexOfBanner(text);
            return cut >= 0 ? text.Substring(0, cut) : text;
        }

        public static bool TryReadMeshFile(string gprPath, out List<GprRadyeDugumAs> nodes, out string error)
        {
            nodes = null;
            error = null;
            if (string.IsNullOrWhiteSpace(gprPath) || !File.Exists(gprPath))
            {
                error = "GPR yok.";
                return false;
            }
            string[] lines;
            try
            {
                WindowsAnsiEncodings.EnsureRegistered();
                lines = WindowsAnsiEncodings.ReadAllLines(gprPath);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            return TryReadMesh(lines, out nodes, out error);
        }

        public static bool TryReadGenel(IList<string> lines, out GprRadyeGenelDonati genel, out string error)
        {
            genel = null;
            error = null;
            if (lines == null)
            {
                error = "GPR yok.";
                return false;
            }
            int bas = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i] ?? string.Empty;
                if (line.IndexOf("Genel donat", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bas = i;
                    break;
                }
            }
            if (bas < 0)
            {
                error = "GPR genel donati tablosu yok.";
                return false;
            }
            var rx = new Regex(@"(\d{1,2})\s*/\s*(\d{2,3})", RegexOptions.CultureInvariant);
            for (int i = bas + 1; i < lines.Count && i < bas + 20; i++)
            {
                MatchCollection matches = rx.Matches(lines[i] ?? string.Empty);
                if (matches.Count < 4) continue;
                int[] cap = new int[4];
                int[] aralik = new int[4];
                for (int k = 0; k < 4; k++)
                {
                    cap[k] = int.Parse(matches[k].Groups[1].Value, CultureInfo.InvariantCulture);
                    aralik[k] = int.Parse(matches[k].Groups[2].Value, CultureInfo.InvariantCulture);
                    if (cap[k] < 8 || cap[k] > 32 || aralik[k] < 5 || aralik[k] > 50)
                    {
                        error = "GPR genel donati cap/aralik okunamadi.";
                        return false;
                    }
                }
                genel = new GprRadyeGenelDonati
                {
                    CapXUstMm = cap[0], AralikXUstCm = aralik[0], AsXUst = AlanCm2Pm(cap[0], aralik[0]),
                    CapXAltMm = cap[1], AralikXAltCm = aralik[1], AsXAlt = AlanCm2Pm(cap[1], aralik[1]),
                    CapYUstMm = cap[2], AralikYUstCm = aralik[2], AsYUst = AlanCm2Pm(cap[2], aralik[2]),
                    CapYAltMm = cap[3], AralikYAltCm = aralik[3], AsYAlt = AlanCm2Pm(cap[3], aralik[3])
                };
                return true;
            }
            error = "GPR genel donati satiri yok.";
            return false;
        }

        public static double AlanCm2Pm(int capMm, int aralikCm)
        {
            double cubuk = Math.PI * (capMm / 10.0) * (capMm / 10.0) / 4.0;
            return cubuk * (100.0 / aralikCm);
        }

        public static bool TryReadMesh(IList<string> lines, out List<GprRadyeDugumAs> nodes, out string error)
        {
            nodes = new List<GprRadyeDugumAs>();
            error = null;
            int start = IlkRaporSatiri(lines);
            if (start < 0)
            {
                error = "GPR icinde radye analiz bolumu yok.";
                return false;
            }

            bool inTable = false;
            for (int i = start; i < lines.Count; i++)
            {
                string line = lines[i] ?? string.Empty;
                if (AsBasligi(line))
                {
                    inTable = true;
                    continue;
                }
                if (!inTable) continue;
                if (line.IndexOf("TEMEL DONATI", StringComparison.OrdinalIgnoreCase) >= 0) break;
                if (line.IndexOf("Nokta", StringComparison.OrdinalIgnoreCase) >= 0) break;
                if (line.IndexOf('\u00A1') < 0) continue;

                List<string> cells = AyirHucre(line);
                if (cells.Count != 14) continue;
                if (!TryDoubles(cells, out double[] values)) continue;
                int no = (int)Math.Round(values[0]);
                if (no != nodes.Count + 1)
                {
                    if (nodes.Count > 0) break;
                    continue;
                }
                nodes.Add(new GprRadyeDugumAs
                {
                    No = no,
                    X = values[3],
                    Y = values[4],
                    Z = values[5],
                    AsXAlt = values[10],
                    AsXUst = values[11],
                    AsYAlt = values[12],
                    AsYUst = values[13]
                });
            }

            if (nodes.Count == 0)
            {
                error = "Radye mesh As tablosu okunamadi.";
                return false;
            }
            return true;
        }

        private static bool AsBasligi(string line)
        {
            return line.IndexOf("Asax", StringComparison.OrdinalIgnoreCase) >= 0
                && line.IndexOf("Asux", StringComparison.OrdinalIgnoreCase) >= 0
                && line.IndexOf("Asay", StringComparison.OrdinalIgnoreCase) >= 0
                && line.IndexOf("Asuy", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<string> AyirHucre(string line)
        {
            string[] parts = line.Split('\u00A1');
            var cells = new List<string>(14);
            for (int i = 1; i < parts.Length - 1; i++)
                cells.Add((parts[i] ?? string.Empty).Trim());
            if (cells.Count == 0 && parts.Length > 1)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    string cell = (parts[i] ?? string.Empty).Trim();
                    if (cell.Length > 0) cells.Add(cell);
                }
            }
            return cells;
        }

        private static bool TryDoubles(List<string> cells, out double[] values)
        {
            values = new double[cells.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                string token = cells[i].Replace(',', '.');
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                    return false;
            }
            return true;
        }

        private static int IndexOfBanner(string text)
        {
            int a = text.IndexOf("RADYE PROGRAMI", StringComparison.OrdinalIgnoreCase);
            int b = text.IndexOf("RADYE ANALIZ", StringComparison.OrdinalIgnoreCase);
            int c = text.IndexOf("RADYE MAT TEMEL", StringComparison.OrdinalIgnoreCase);
            int cut = -1;
            if (a >= 0) cut = a;
            if (b >= 0 && (cut < 0 || b < cut)) cut = b;
            if (c >= 0 && (cut < 0 || c < cut)) cut = c;
            return cut;
        }
    }
}
