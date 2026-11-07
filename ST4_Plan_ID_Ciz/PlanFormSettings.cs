using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;

namespace ST4PlanIdCiz
{
    /// <summary>Plan formlarının kullanıcıya özel, sürümden bağımsız son onaylı seçimleri.</summary>
    internal sealed class PlanFormSettings
    {
        private static readonly object Sync = new object();
        private static string _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ST4_Plan_ID_Ciz");
        private readonly string _path;
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private PlanFormSettings(string name) { _path = Path.Combine(_directory, name + ".ini"); }

        internal static PlanFormSettings Load(string name)
        {
            var settings = new PlanFormSettings(name);
            lock (Sync)
            {
                if (!settings.Read(settings._path)) settings.Read(settings._path + ".bak");
            }
            return settings;
        }

        private bool Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line.IndexOf('\0') >= 0) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    _values[line.Substring(0, eq).Trim()] = Uri.UnescapeDataString(line.Substring(eq + 1).Trim());
                }
                return _values.Count > 0;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException || ex is FormatException)
            { Trace.TraceWarning("Plan form ayarı okunamadı: " + ex.Message); return false; }
        }

        internal string Text(string key, string fallback = null) => _values.TryGetValue(key, out var value) ? value : fallback;
        internal bool Bool(string key, bool fallback)
        {
            string value = Text(key);
            if (value == "1") return true;
            if (value == "0") return false;
            return bool.TryParse(value, out bool parsed) ? parsed : fallback;
        }
        internal int Int(string key, int fallback, int min, int max)
            => int.TryParse(Text(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= min && value <= max ? value : fallback;
        internal int? NullableInt(string key, int? fallback)
            => int.TryParse(Text(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        internal decimal Decimal(string key, decimal fallback, decimal min, decimal max)
            => decimal.TryParse(Text(key), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) && value >= min && value <= max ? value : fallback;

        internal PlanFormSettings Set(string key, object value)
        {
            _values[key] = value is bool boolean ? (boolean ? "1" : "0") : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            return this;
        }

        internal void Save()
        {
            lock (Sync)
            {
                string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    File.WriteAllLines(temp, _values.OrderBy(v => v.Key, StringComparer.Ordinal)
                        .Select(v => v.Key + "=" + Uri.EscapeDataString(v.Value)), new UTF8Encoding(false));
                    if (File.Exists(_path)) File.Replace(temp, _path, _path + ".bak");
                    else File.Move(temp, _path);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
                { Trace.TraceWarning("Plan form ayarı kaydedilemedi: " + ex.Message); }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
                    { Trace.TraceWarning("Geçici form ayarı temizlenemedi: " + ex.Message); }
                }
            }
        }
    }
}
