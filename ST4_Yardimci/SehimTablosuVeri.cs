using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ST4Yardimci
{
    /// <summary>Tablo satırı: eleman adı ve uzun süreli (zamana bağlı toplam) sehim, mm.</summary>
    internal sealed class SehimSatiri
    {
        public string Ad;
        public int No;
        public double Mm;
    }

    internal sealed class SehimKat
    {
        public int Indeks;
        public string Ad, Kod;
        public readonly List<SehimSatiri> Kirisler = new List<SehimSatiri>();
        public readonly List<SehimSatiri> Dosemeler = new List<SehimSatiri>();
        public override string ToString() =>
            string.Format("{0} ({1})  —  {2} kiriş, {3} döşeme", Ad, Kod, Kirisler.Count, Dosemeler.Count);
    }

    /// <summary>
    /// STA4CAD proje klasöründen kat kat uzun süreli sehim (SEHIMTABLOSU), STA4 "UZUN SÜRELİ TERS SEHİM TABLOSU" ile aynı değer.
    /// Kiriş: BEA (900 B) +760 float, m → mm, işaretli (δi + δig·λ; GPR kiriş sehim tablosunun son sehim sütunu).
    /// Sürekli kirişin açıklıkları aynı adı taşır; mutlak değeri en büyük açıklık alınır.
    /// Döşeme: ani δi = |FLO (200 B) +136| ×1000; δt = δi·(1 + 2·G/(G+Q)), G/Q ST4 /Floors Data/ 3./4. alan
    /// (GPR döşeme statik tablosundaki Y satırı; X ani sehimden büyük olduğundan STA4 çizimi de bunu basar).
    /// </summary>
    internal sealed class SehimProjesi
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public string St4Yolu;
        public readonly List<SehimKat> Katlar = new List<SehimKat>();
        public bool KirisVar, DosemeVar;

        private sealed class Kiris
        {
            public int Sira, No, I, J, Kat;
            public bool Panel;
            public string Ad;
        }

        private sealed class Doseme
        {
            public int No;
            public double G, Q;
        }

        private sealed class Ikili
        {
            private readonly byte[] _b;
            public readonly int Boy;
            public Ikili(string yol, int boy)
            {
                Boy = boy;
                _b = File.Exists(yol) ? RadyeOtomatikSource.ReadShared(yol) : new byte[0];
            }
            public int Adet => _b.Length / Boy;
            public bool Var => _b.Length >= Boy;
            public float F(int kayit, int ofset)
            {
                int p = kayit * Boy + ofset;
                return p >= 0 && p + 4 <= _b.Length ? BitConverter.ToSingle(_b, p) : 0f;
            }
            public short S(int kayit, int ofset)
            {
                int p = kayit * Boy + ofset;
                return p >= 0 && p + 2 <= _b.Length ? BitConverter.ToInt16(_b, p) : (short)0;
            }
        }

        private static double D(string s) =>
            double.TryParse((s ?? "").Trim(), NumberStyles.Float, Inv, out double d) ? d : 0;
        private static int I(string s) => (int)Math.Round(D(s));
        private static string Alan(string[] f, int i) => f != null && i < f.Length ? f[i] : null;

        private static string[] Satirlar(string yol) =>
            RadyeOtomatikSource.ReadRadText(yol).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        /// <summary>STA4 eleman numara tabanı: bir katta 100'den çok eleman varsa 1000 (K_BLG_03 kirişleri).</summary>
        private static int Taban(IEnumerable<int> nolar, int katSayisi) =>
            nolar.DefaultIfEmpty(0).Max() / 100 >= Math.Max(katSayisi, 1) ? 1000 : 100;

        public static SehimProjesi Oku(string st4)
        {
            string taban = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(st4)), Path.GetFileNameWithoutExtension(st4));
            string[] l = Satirlar(st4);
            var p = new SehimProjesi { St4Yolu = st4 };
            int Bolum(string ad) => Array.FindIndex(l, s => s.StartsWith(ad, StringComparison.Ordinal));

            var katlar = new List<SehimKat>();
            int k = Bolum("/Story/");
            if (k >= 0)
                for (int i = k + 1; i + 2 < l.Length && !l[i].StartsWith("/"); i += 3)
                    katlar.Add(new SehimKat { Indeks = katlar.Count, Ad = l[i].Trim(), Kod = l[i + 1].Trim().TrimEnd('-') });

            var kirisler = new List<Kiris>();
            k = Bolum("/Beams Data/");
            if (k >= 0)
                for (int i = k + 1; i + 2 < l.Length && !l[i].StartsWith("/"); i += 3)
                {
                    string[] f = l[i].Split(',');
                    int no = I(Alan(f, 0));
                    if (no == 0) break;
                    string[] g = l[i + 1].Split(',');
                    kirisler.Add(new Kiris
                    {
                        Sira = kirisler.Count, No = no, I = I(Alan(g, 4)), J = I(Alan(g, 5)),
                        Panel = (Alan(f, 14) ?? "").Trim() == "1"   // bodrum perde paneli: sehim tablosunda yok
                    });
                }

            var dosemeler = new List<Doseme>();
            k = Bolum("/Floors Data/");
            if (k >= 0)
                for (int i = k + 1; i + 1 < l.Length && !l[i].StartsWith("/"); i += 2)
                {
                    string[] f = l[i].Split(',');
                    int no = I(Alan(f, 0));
                    if (no == 0) break;
                    dosemeler.Add(new Doseme { No = no, G = D(Alan(f, 2)), Q = D(Alan(f, 3)) });
                }

            string Ad(char onEk, int no, int t)
            {
                int kat = no / t;
                string kod = kat >= 0 && kat < katlar.Count ? katlar[kat].Kod : kat.ToString(Inv);
                return onEk.ToString() + kod + "-" + (no % t).ToString(t == 1000 ? "000" : "00", Inv);
            }
            int tk = Taban(kirisler.Select(x => x.No), katlar.Count), td = Taban(dosemeler.Select(x => x.No), katlar.Count);
            foreach (var x in kirisler) { x.Ad = Ad('K', x.No, tk); x.Kat = x.No / tk; }

            SehimKat Kat(int i)
            {
                while (katlar.Count <= i) katlar.Add(new SehimKat { Indeks = katlar.Count, Ad = "Kat " + katlar.Count, Kod = katlar.Count.ToString(Inv) });
                return katlar[i];
            }

            // Döşemeler: FLO kayıt sırası = ST4 /Floors Data/ sırası
            var flo = new Ikili(taban + ".FLO", 200);
            p.DosemeVar = flo.Var;
            if (flo.Var)
                for (int i = 0; i < dosemeler.Count && i < flo.Adet; i++)
                {
                    var d = dosemeler[i];
                    double ani = Math.Abs(flo.F(i, 136)) * 1000.0;
                    double mm = d.G + d.Q > 0 ? ani * (1 + 2 * d.G / (d.G + d.Q)) : ani;
                    Kat(d.No / td).Dosemeler.Add(new SehimSatiri { Ad = Ad('D', d.No, td), No = d.No, Mm = mm });
                }

            var bea = new Ikili(taban + ".BEA", 900);
            p.KirisVar = bea.Var;
            if (bea.Var)
            {
                var kayit = KirisleriEsle(kirisler, bea);
                foreach (var grp in kirisler.Where(x => !x.Panel && kayit.ContainsKey(x)).GroupBy(x => x.No))
                {
                    var g0 = grp.OrderByDescending(x => Math.Abs(bea.F(kayit[x], 760))).First();
                    Kat(g0.Kat).Kirisler.Add(new SehimSatiri { Ad = g0.Ad, No = g0.No, Mm = bea.F(kayit[g0], 760) * 1000.0 });
                }
            }

            foreach (var kat in katlar)
            {
                kat.Kirisler.Sort((a, b) => a.No.CompareTo(b.No));
                kat.Dosemeler.Sort((a, b) => a.No.CompareTo(b.No));
            }
            p.Katlar.AddRange(katlar.Where(x => x.Kirisler.Count + x.Dosemeler.Count > 0));
            return p;
        }

        /// <summary>
        /// BEA kayıt sırası ST4 sırasından farklı olabilir: BEA +0/+8 (int16) düğüm çifti = ST4 ikinci satır 5./6. alan.
        /// Düğümler sıfırsa (analizden sonra kaydedilmiş ST4) BEA sırası = ST4 sırası. Eşleşmeyen sırayla boştaki kaydı alır.
        /// </summary>
        private static Dictionary<Kiris, int> KirisleriEsle(List<Kiris> kirisler, Ikili bea)
        {
            var r = new Dictionary<Kiris, int>();
            if (kirisler.All(x => x.I == 0 && x.J == 0))
            {
                foreach (var x in kirisler.Where(x => x.Sira < bea.Adet)) r[x] = x.Sira;
                return r;
            }
            var bos = new SortedSet<int>(Enumerable.Range(0, bea.Adet));
            foreach (var x in kirisler)
            {
                int bulunan = bos.Where(i => bea.S(i, 0) == x.I && bea.S(i, 8) == x.J).DefaultIfEmpty(-1).First();
                if (bulunan >= 0) { r[x] = bulunan; bos.Remove(bulunan); }
            }
            foreach (var x in kirisler.Where(x => !r.ContainsKey(x)))
            {
                if (bos.Count == 0) break;
                r[x] = bos.Min; bos.Remove(bos.Min);
            }
            return r;
        }
    }
}
