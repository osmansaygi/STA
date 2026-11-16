using System;
using System.IO;

namespace BeykentOrtak
{
    /// <summary>
    /// STA4CAD ikili sonuç dosyalarının kayıt düzeni (V14 / V15). Kod V14 ofsetleriyle yazılır; <see cref="Ofset"/> V15 karşılığını verir.
    /// V15 (14.11.2026, CK_20827_2_02 / CK_20827_2_02v14-V15 çifti; kayıt sayıları aynı):
    /// - BEA ve COL kaydı 900 → 1124 bayt. 22 elemanlı (88 B) 7 yük durumu dizisi 30 elemana (120 B) çıktı, yeni 8 eleman sonda.
    ///   Dizilerden önceki alanlar yerinde (BEA +0..+37, COL +0..+25); dizilerden sonrakiler +224 bayt kayar (BEA +684 → +908, +760 → +984).
    /// - THE kaydı 700 → 892 bayt. Doğrulanan alanlar (+10/+12 int16, +32..+44 float) yerinde; daha derin ofsetler doğrulanmadı.
    /// - KMX, SMX, DMX, FLO, LOA kayıt boyları değişmedi.
    /// </summary>
    public sealed class Sta4KayitDuzeni
    {
        private const int DiziV14 = 88, DiziV15 = 120, DiziAdet = 7;

        /// <summary>Dosyadaki gerçek kayıt boyu (bayt).</summary>
        public readonly int Boy;
        public readonly bool V15;
        private readonly int _diziBas;

        private Sta4KayitDuzeni(int boy, bool v15, int diziBas) { Boy = boy; V15 = v15; _diziBas = diziBas; }

        /// <param name="uzunluk">Dosya boyu (bayt).</param>
        /// <param name="st4Yolu">Belirsizlikte (boy iki kayıt boyuna da bölünürse) ST4 1. satırındaki sürüm kullanılır.</param>
        /// <param name="adet">Bilinen kayıt sayısı (yoksa -1).</param>
        public static Sta4KayitDuzeni Bea(long uzunluk, string st4Yolu = null, int adet = -1) => Sec(uzunluk, 900, 1124, 38, st4Yolu, adet);
        public static Sta4KayitDuzeni Col(long uzunluk, string st4Yolu = null, int adet = -1) => Sec(uzunluk, 900, 1124, 26, st4Yolu, adet);
        public static Sta4KayitDuzeni The(long uzunluk, string st4Yolu = null, int adet = -1) => Sec(uzunluk, 700, 892, 0, st4Yolu, adet);

        /// <summary>V14 ofsetinin bu dosyadaki karşılığı.</summary>
        public int Ofset(int v14Ofset)
        {
            if (!V15 || _diziBas <= 0 || v14Ofset < _diziBas) return v14Ofset;
            int son = _diziBas + DiziAdet * DiziV14;
            if (v14Ofset < son)
            {
                int k = (v14Ofset - _diziBas) / DiziV14, r = (v14Ofset - _diziBas) % DiziV14;
                return _diziBas + k * DiziV15 + r;
            }
            return v14Ofset + DiziAdet * (DiziV15 - DiziV14);
        }

        private static Sta4KayitDuzeni Sec(long n, int boy14, int boy15, int diziBas, string st4Yolu, int adet)
        {
            bool v15;
            if (adet > 0 && n == (long)adet * boy15) v15 = true;
            else if (adet > 0 && n == (long)adet * boy14) v15 = false;
            else
            {
                bool a = n > 0 && n % boy14 == 0, b = n > 0 && n % boy15 == 0;
                v15 = a != b ? b : St4Surumu(st4Yolu) >= 15;
            }
            return new Sta4KayitDuzeni(v15 ? boy15 : boy14, v15, diziBas);
        }

        /// <summary>ST4 1. satırı "STA4CAD VER.15.0" → 15; okunamazsa 0.</summary>
        public static int St4Surumu(string st4Yolu)
        {
            if (string.IsNullOrEmpty(st4Yolu) || !File.Exists(st4Yolu)) return 0;
            try
            {
                using (var fs = new FileStream(st4Yolu, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs))
                {
                    string s = sr.ReadLine() ?? "";
                    int i = s.IndexOf("VER.", StringComparison.OrdinalIgnoreCase);
                    if (i < 0) return 0;
                    int j = i + 4, v = 0;
                    while (j < s.Length && char.IsDigit(s[j])) v = v * 10 + (s[j++] - '0');
                    return v;
                }
            }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }
    }
}
