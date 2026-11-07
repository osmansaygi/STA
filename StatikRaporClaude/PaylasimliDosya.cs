using System.Text;

namespace StatikRaporClaude
{
    /// <summary>
    /// STA4 açıkken proje dosyalarını (BEA, KMX, ST4 …) yazma kilidiyle tutar; File.ReadAll* yalnız FileShare.Read
    /// verdiği için "başka işlem kullanıyor" hatası alınır. Bu okuyucular yazana izin vererek okur.
    /// </summary>
    internal static class PaylasimliDosya
    {
        private static FileStream Ac(string yol) =>
            new FileStream(yol, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        public static byte[] Bytes(string yol)
        {
            using var fs = Ac(yol);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            return ms.ToArray();
        }

        public static string[] Lines(string yol) => Lines(yol, Encoding.UTF8);

        public static string[] Lines(string yol, Encoding enc)
        {
            using var r = new StreamReader(Ac(yol), enc);
            var l = new List<string>();
            for (string s; (s = r.ReadLine()) != null;) l.Add(s);
            return l.ToArray();
        }
    }
}
