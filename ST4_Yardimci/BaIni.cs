using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ST4Yardimci
{
    /// <summary>%LocalAppData%\ST4_Yardimci altındaki anahtar=değer dosyaları.</summary>
    internal static class BaIni
    {
        public static string Dir()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ST4_Yardimci");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        public static Dictionary<string, string> Read(string fileName)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = Path.Combine(Dir(), fileName);
                if (!File.Exists(path)) return map;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return map;
        }

        public static void Write(string fileName, params string[] lines)
        {
            try
            {
                string path = Path.Combine(Dir(), fileName);
                var sb = new StringBuilder();
                if (lines != null)
                {
                    foreach (string line in lines)
                        sb.AppendLine(line);
                }
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static string Get(Dictionary<string, string> map, string key, string fallback)
        {
            if (map != null && map.TryGetValue(key, out string v) && !string.IsNullOrEmpty(v))
                return v;
            return fallback;
        }

        public static double GetD(Dictionary<string, string> map, string key, double fallback)
        {
            string s = Get(map, key, null);
            if (s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
            return fallback;
        }

        public static int GetI(Dictionary<string, string> map, string key, int fallback)
        {
            string s = Get(map, key, null);
            if (s != null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                return v;
            return fallback;
        }

        public static bool GetB(Dictionary<string, string> map, string key, bool fallback)
        {
            string s = Get(map, key, null);
            if (s == null) return fallback;
            if (s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s == "0" || string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        public static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        public static string B(bool v) => v ? "1" : "0";
    }
}
