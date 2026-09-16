using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// METRAJ her çalıştığında ÇŞİDB YFK aylık inşaat birim fiyat PDF'sini kontrol eder.
    /// PDF adresi değişmediyse önbellek kullanılır; olmazsa gömülü Eylül 2026 fiyatına düşülür.
    /// </summary>
    internal static class CsbBirimFiyatCatalog
    {
        public const string SourceName = "Cevre Sehircilik YFK";
        private const string ListingUrl = "https://yfk.csb.gov.tr/aylik-guncel-rayic-ve-birim-fiyat-listeleri-113351";
        private const string CacheVer = "2";
        private const int TimeoutMs = 60000;

        private static readonly Dictionary<string, double> Fallback = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            { "15.150.1005", 4182.36 },
            { "15.150.1006", 4330.10 },
            { "15.180.1003", 1195.48 },
            { "15.160.1003", 54252.44 },
            { "15.160.1004", 52157.73 }
        };

        private static readonly object Gate = new object();
        private static Dictionary<string, double> _prices = new Dictionary<string, double>(Fallback, StringComparer.Ordinal);
        private static string _status = "Gomulu Eylul 2026 fiyatları (henuz web kontrolu yok).";
        private static string _cachedPdfUrl = "";

        private static readonly Regex Href = new Regex("href\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PozRx = new Regex(@"\b(\d{1,2}\.\d{3}\.\d{3,4})\b", RegexOptions.Compiled);
        private static readonly Regex MoneyAfterUnit = new Regex(
            @"(?:Ton|tn|m(?:3|\u00B3)|m(?:2|\u00B2)|m²|m³)\s*(\d{1,3}(?:\.\d{3})*,\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MoneyGrouped = new Regex(@"(\d{1,3}(?:\.\d{3})+,\d{2})", RegexOptions.Compiled);

        public static string LastStatus { get { lock (Gate) return _status; } }

        public static bool TryGetPrice(string poz, out double price)
        {
            price = 0;
            if (string.IsNullOrWhiteSpace(poz)) return false;
            lock (Gate)
            {
                return _prices.TryGetValue(poz.Trim(), out price);
            }
        }

        public static string RefreshFromWeb()
        {
            return RefreshFromWeb(null);
        }

        public static string RefreshFromWeb(ICollection<string> neededPoz)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch { }

            try
            {
                LoadCacheFile();
                string listing = DownloadString(ListingUrl);
                if (string.IsNullOrEmpty(listing))
                    return SetStatus(DescribeLookup("Web sayfasina ulasilamadi; kayitli/gomulu fiyatlar kullanildi.", neededPoz));

                string monthPage = FindLatestMonthPageUrl(listing);
                string pdfUrl = null;
                if (!string.IsNullOrEmpty(monthPage))
                {
                    if (monthPage.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        pdfUrl = monthPage;
                    else
                    {
                        string monthHtml = DownloadString(monthPage);
                        pdfUrl = FindInsaatBirimFiyatPdf(monthHtml);
                    }
                }
                if (string.IsNullOrEmpty(pdfUrl))
                    pdfUrl = FindInsaatBirimFiyatPdf(listing);

                if (string.IsNullOrEmpty(pdfUrl))
                    return SetStatus(DescribeLookup("Guncel PDF baglantisi bulunamadi; kayitli/gomulu fiyatlar kullanildi.", neededPoz));

                bool samePdf;
                bool haveNeeded;
                bool complete;
                lock (Gate)
                {
                    samePdf = string.Equals(_cachedPdfUrl, pdfUrl, StringComparison.OrdinalIgnoreCase);
                    haveNeeded = HasAllNeeded(neededPoz);
                    complete = _prices.Count >= 50;
                }
                if (samePdf && haveNeeded && complete)
                    return SetStatus(DescribeLookup("Guncel liste ayni PDF. Onbellekten poz fiyatları alindi.", neededPoz));

                byte[] pdf = DownloadBytes(pdfUrl);
                if (pdf == null || pdf.Length < 1000)
                    return SetStatus(DescribeLookup("PDF indirilemedi; kayitli/gomulu fiyatlar kullanildi.", neededPoz));

                var parsed = ParsePricesFromPdf(pdf);
                if (parsed.Count == 0)
                    return SetStatus(DescribeLookup("PDF okundu ama poz fiyati cikarilamadi; kayitli/gomulu fiyatlar kullanildi.", neededPoz));

                lock (Gate)
                {
                    foreach (var kv in parsed)
                        _prices[kv.Key] = kv.Value;
                    _cachedPdfUrl = pdfUrl;
                }
                SaveCacheFile(pdfUrl, parsed);
                return SetStatus(DescribeLookup("CŞIDB YFK guncel inşaat birim fiyat listesi alindi (" + parsed.Count + " poz).", neededPoz));
            }
            catch (Exception ex)
            {
                return SetStatus(DescribeLookup("Fiyat guncelleme hatasi: " + ex.Message + " Kayitli/gomulu fiyatlar kullanildi.", neededPoz));
            }
        }

        private static bool HasAllNeeded(ICollection<string> neededPoz)
        {
            if (neededPoz == null || neededPoz.Count == 0) return _prices.Count >= 50;
            foreach (string poz in neededPoz)
            {
                if (string.IsNullOrWhiteSpace(poz)) continue;
                if (!_prices.ContainsKey(poz.Trim())) return false;
            }
            return true;
        }

        private static string DescribeLookup(string prefix, ICollection<string> neededPoz)
        {
            if (neededPoz == null || neededPoz.Count == 0) return prefix;
            int ok = 0;
            var miss = new List<string>();
            foreach (string poz in neededPoz)
            {
                if (string.IsNullOrWhiteSpace(poz)) continue;
                if (TryGetPrice(poz.Trim(), out _)) ok++;
                else miss.Add(poz.Trim());
            }
            string msg = prefix + " KSF poz: " + ok + "/" + neededPoz.Count + " bulundu.";
            if (miss.Count > 0 && miss.Count <= 8)
                msg += " Eksik: " + string.Join(", ", miss.ToArray());
            return msg;
        }

        private static Dictionary<string, double> ParsePricesFromPdf(byte[] pdf)
        {
            var found = new Dictionary<string, double>(StringComparer.Ordinal);
            var sb = new StringBuilder();
            using (var ms = new MemoryStream(pdf, writable: false))
            using (var doc = PdfDocument.Open(ms))
            {
                foreach (var page in doc.GetPages())
                    sb.Append(page.Text).Append(' ');
            }
            string text = sb.ToString();
            MatchCollection matches = PozRx.Matches(text);
            for (int i = 0; i < matches.Count; i++)
            {
                string poz = matches[i].Groups[1].Value;
                if (found.ContainsKey(poz)) continue;
                int start = matches[i].Index;
                int end = (i + 1 < matches.Count) ? matches[i + 1].Index : Math.Min(text.Length, start + 480);
                if (end - start > 480) end = start + 480;
                if (end <= start) continue;
                string slice = text.Substring(start, end - start);
                if (TryExtractPrice(slice, out double val))
                    found[poz] = val;
            }
            return found;
        }

        private static bool TryExtractPrice(string slice, out double val)
        {
            val = 0;
            if (string.IsNullOrEmpty(slice)) return false;
            MatchCollection unitPrices = MoneyAfterUnit.Matches(slice);
            if (unitPrices.Count > 0)
                return TryParseTrMoney(unitPrices[unitPrices.Count - 1].Groups[1].Value, out val);
            MatchCollection grouped = MoneyGrouped.Matches(slice);
            if (grouped.Count > 0)
                return TryParseTrMoney(grouped[grouped.Count - 1].Groups[1].Value, out val);
            return false;
        }

        private static bool TryParseTrMoney(string raw, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim().Replace(".", "").Replace(',', '.');
            return double.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }

        private static string FindLatestMonthPageUrl(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            int year = DateTime.Now.Year;
            string[] months =
            {
                "aralik", "kasim", "ekim", "eylul", "agustos", "temmuz",
                "haziran", "mayis", "nisan", "mart", "subat", "ocak"
            };
            foreach (string month in months)
            {
                foreach (Match m in Href.Matches(html))
                {
                    string href = m.Groups[1].Value.Trim();
                    if (href.IndexOf("javascript", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    string n = href.ToLowerInvariant();
                    if (n.IndexOf("/" + year + "/", StringComparison.Ordinal) < 0
                        && n.IndexOf(year.ToString(CultureInfo.InvariantCulture) + "-yilina", StringComparison.Ordinal) < 0
                        && n.IndexOf("yfk.csb.gov.tr", StringComparison.Ordinal) < 0)
                        continue;
                    if (n.IndexOf(month, StringComparison.Ordinal) < 0) continue;
                    if (n.IndexOf("yfk.csb.gov.tr", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        return ToAbs(href);
                }
            }
            return null;
        }

        private static string FindInsaatBirimFiyatPdf(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            string best = null;
            foreach (Match m in Href.Matches(html))
            {
                string href = m.Groups[1].Value.Trim();
                if (!href.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                string n = href.ToLowerInvariant();
                if (n.IndexOf("elektrik", StringComparison.Ordinal) >= 0) continue;
                if (n.IndexOf("mekanik", StringComparison.Ordinal) >= 0 || n.IndexOf("makanik", StringComparison.Ordinal) >= 0) continue;
                if (n.IndexOf("rayi", StringComparison.Ordinal) >= 0) continue;
                bool looksInsaatBf = n.IndexOf("b-f", StringComparison.Ordinal) >= 0
                    || n.IndexOf("birim-fiyat", StringComparison.Ordinal) >= 0
                    || (n.IndexOf("n-aat", StringComparison.Ordinal) >= 0 && n.IndexOf("b-f", StringComparison.Ordinal) >= 0);
                if (!looksInsaatBf) continue;
                best = ToAbs(href);
                if (n.IndexOf("n-aat-b-f", StringComparison.Ordinal) >= 0 || n.IndexOf("nsaat-b-f", StringComparison.Ordinal) >= 0)
                    return best;
            }
            return best;
        }

        private static string ToAbs(string href)
        {
            if (string.IsNullOrEmpty(href)) return href;
            if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return href;
            if (href.StartsWith("//")) return "https:" + href;
            return "https://yfk.csb.gov.tr/" + href.TrimStart('/');
        }

        private static string DownloadString(string url)
        {
            byte[] raw = DownloadBytes(url);
            if (raw == null || raw.Length == 0) return null;
            return Encoding.UTF8.GetString(raw);
        }

        private static byte[] DownloadBytes(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;
            req.UserAgent = "Mozilla/5.0 ST4_Plan_ID_Ciz METRAJ";
            req.AllowAutoRedirect = true;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var s = resp.GetResponseStream())
            {
                if (s == null) return null;
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        private static string CachePath()
        {
            try
            {
                string loc = Assembly.GetExecutingAssembly().Location;
                string dir = Path.GetDirectoryName(loc);
                if (string.IsNullOrEmpty(dir)) return null;
                return Path.Combine(dir, "csb_insaat_birim_fiyat.cache.txt");
            }
            catch
            {
                return null;
            }
        }

        private static void LoadCacheFile()
        {
            try
            {
                string path = CachePath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                var map = new Dictionary<string, double>(StringComparer.Ordinal);
                string url = "";
                bool verOk = false;
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("VER=", StringComparison.OrdinalIgnoreCase))
                    {
                        verOk = string.Equals(line.Substring(4).Trim(), CacheVer, StringComparison.Ordinal);
                        continue;
                    }
                    if (line.StartsWith("PDF=", StringComparison.OrdinalIgnoreCase))
                    {
                        url = line.Substring(4).Trim();
                        continue;
                    }
                    if (line.StartsWith("UTC=", StringComparison.OrdinalIgnoreCase)) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    if (k.IndexOf('.') < 0) continue;
                    if (double.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out double p))
                        map[k] = p;
                }
                if (map.Count == 0) return;
                lock (Gate)
                {
                    foreach (var kv in map)
                        _prices[kv.Key] = kv.Value;
                    if (verOk && !string.IsNullOrEmpty(url))
                        _cachedPdfUrl = url;
                }
            }
            catch { }
        }

        private static void SaveCacheFile(string pdfUrl, Dictionary<string, double> parsed)
        {
            try
            {
                string path = CachePath();
                if (string.IsNullOrEmpty(path)) return;
                var sb = new StringBuilder();
                sb.Append("VER=").AppendLine(CacheVer);
                sb.Append("PDF=").AppendLine(pdfUrl ?? "");
                sb.Append("UTC=").AppendLine(DateTime.UtcNow.ToString("o"));
                foreach (var kv in parsed)
                    sb.Append(kv.Key).Append('=').AppendLine(kv.Value.ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        private static string SetStatus(string msg)
        {
            lock (Gate) _status = msg ?? "";
            return _status;
        }
    }
}
