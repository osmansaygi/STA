using System.Globalization;
using System.Text;

namespace StatikRaporClaude.Veri
{
    public sealed class St4Kat
    {
        public string Ad, Kod;   // Kod: "S", "Z", "1"…
        public double Kot;
    }

    public sealed class St4Kiris
    {
        public int Sira;          // ST4 /Beams Data/ sırası (0 tabanlı)
        public int No;            // 103, 121… (katta 100'den çok kirişte 1023 → kat 1, 023)
        public int Kat, Yerel;    // no / taban, no % taban
        public int B, H;          // cm
        public int Aks, AksBas, AksSon;
        public double Kot1, Kot2;  // uçlardaki kot farkı (m; ST4 12. ve 13. alan, cm)
        public int I, J;          // analiz düğümleri (ikinci satır 5. ve 6. alan)
        public bool YYonu => Aks < 2000;   // 1xxx eksenleri Y doğrultusu (aks üzerinde ilerleyen kiriş)
        public string Ad;
    }

    public sealed class St4Kolon
    {
        public int Sira, No, Bx, By;
        public int Kat, Yerel;
        public double UstKot, AltKot;   // m; ST4'te yoksa 0
        public bool KotVar => UstKot > AltKot;
        public bool Poligon;            // /Polygon columns/: Bx/By kesitin sınır kutusu, Alan gerçek kesit alanı (cm²)
        public double Alan, KisaKenar;
        public double Ac => Poligon ? Alan : Bx * By;
        /// <summary>Poligonda perde ekseni: o yöndeki boy ≥ 6 × en kısa kenar (CP_03 112: yalnız Y; 116: hiçbiri; VKF_01 105: ikisi).</summary>
        public bool PerdeX => Poligon && KisaKenar > 0 && Bx >= 6 * KisaKenar;
        public bool PerdeY => Poligon && KisaKenar > 0 && By >= 6 * KisaKenar;
        public string Ad;
    }

    public sealed class St4Doseme
    {
        public int Sira, No, D;
        public int Kat, Yerel;
        public double G, Q;
        public string Ad;
    }

    /// <summary>ST4 metin modelinden rapor için gereken kısım: katlar, kiriş/kolon/döşeme listeleri ve adları, deprem katsayısı.</summary>
    public sealed class St4Model
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public readonly List<St4Kat> Katlar = new List<St4Kat>();
        public readonly List<St4Kiris> Kirisler = new List<St4Kiris>();
        public readonly List<St4Kolon> Kolonlar = new List<St4Kolon>();
        public readonly List<St4Doseme> Dosemeler = new List<St4Doseme>();
        public double Sds, Hbodrum;
        /// <summary>ST4 deprem parametreleri: 4. satır R, I (Onem); 7. satır eksantrisite, β; 14. satır D, Sds, Sd1, Ss, S1, enlem, boylam, DD3 Sds/Sd1.</summary>
        public double R, Onem, Eksantrisite, Beta, Dfazla, Sd1, Ss, S1, Enlem, Boylam, Sds3, Sd13;

        private static double D(string s) => double.TryParse(s, NumberStyles.Float, Inv, out var d) ? d : 0;
        private static int I(string s) => (int)Math.Round(D(s));

        /// <summary>
        /// Eleman numara tabanı: normalde 100 (103 → kat 1, 03). Bir katta 100'den çok eleman varsa STA4 1000 kullanır
        /// (1023 → kat 1, 023; K_BLG_03 kirişleri). En büyük no / 100 kat sayısına ulaşıyorsa taban 1000'dir.
        /// </summary>
        public int Taban(IEnumerable<int> nolar) => nolar.DefaultIfEmpty(0).Max() / 100 >= Math.Max(Katlar.Count, 1) ? 1000 : 100;

        /// <summary>STA4 eleman adı: 103 → KZ-03, tabanı 1000 olan listede 1023 → KZ-023 (kat kodu /Story/'den).</summary>
        public string Ad(char onEk, int no, int taban = 100)
        {
            int kat = no / taban;
            string kod = kat >= 0 && kat < Katlar.Count ? Katlar[kat].Kod : kat.ToString();
            return $"{onEk}{kod}-{(no % taban).ToString(taban == 1000 ? "000" : "00")}";
        }

        public static St4Model Oku(string yol)
        {
            var l = PaylasimliDosya.Lines(yol, Encoding.GetEncoding(1254));
            var m = new St4Model();
            // 14. satır: deprem katsayıları; 2. alan Sds (N_ULK_02: .4829 = GPR deprem raporu)
            if (l.Length > 13) m.Sds = D(l[13].Split(',').ElementAtOrDefault(1));
            // 10. satır 6. alan: bodrum yüksekliği (GPR "Hn=H-Hbodrum"; CP_03 4.0, FHM_GO_A 3.2, N_ULK_02 0)
            if (l.Length > 9) m.Hbodrum = D(l[9].Split(',').ElementAtOrDefault(5));
            // deprem parametreleri (N_ULK_02, CP_03, VKF_01, K_BLG_03 GPR deprem raporlarıyla eşlendi)
            if (l.Length > 13)
            {
                var a = l[3].Split(','); var e = l[6].Split(','); var d = l[13].Split(',');
                m.R = D(a.ElementAtOrDefault(4)); m.Onem = D(a.ElementAtOrDefault(5));
                m.Eksantrisite = D(e.ElementAtOrDefault(3)); m.Beta = D(e.ElementAtOrDefault(4));
                m.Dfazla = D(d.ElementAtOrDefault(0)); m.Sd1 = D(d.ElementAtOrDefault(2)); m.Ss = D(d.ElementAtOrDefault(3)); m.S1 = D(d.ElementAtOrDefault(4));
                m.Enlem = D(d.ElementAtOrDefault(6)); m.Boylam = D(d.ElementAtOrDefault(7)); m.Sds3 = D(d.ElementAtOrDefault(8)); m.Sd13 = D(d.ElementAtOrDefault(9));
            }

            int Bolum(string ad) => Array.FindIndex(l, s => s.StartsWith(ad));
            int k = Bolum("/Story/");
            if (k >= 0)
                for (int i = k + 1; i + 2 < l.Length && !l[i].StartsWith("/"); i += 3)
                    m.Katlar.Add(new St4Kat { Ad = l[i].Trim(), Kod = l[i + 1].Trim().TrimEnd('-'), Kot = D(l[i + 2].Split(',')[0]) });

            k = Bolum("/Beams Data/");
            if (k >= 0)
                for (int i = k + 1; i + 2 < l.Length && !l[i].StartsWith("/"); i += 3)
                {
                    var f = l[i].Split(',');
                    int no = I(f[0]);
                    if (no == 0) break;
                    var g = l[i + 1].Split(',');
                    var b = new St4Kiris
                    {
                        Sira = m.Kirisler.Count, No = no, B = I(f[1]), H = I(f[2]), Aks = I(f[4]), AksBas = I(f[5]), AksSon = I(f[6]),
                        I = I(g[4]), J = I(g[5]), Kot1 = D(f.ElementAtOrDefault(11)) / 100, Kot2 = D(f.ElementAtOrDefault(12)) / 100
                    };
                    m.Kirisler.Add(b);
                }

            k = Bolum("/Columns Data/");
            if (k >= 0)
                for (int i = k + 1; i + 2 < l.Length && !l[i].StartsWith("/"); i += 3)
                {
                    var f = l[i].Split(',');
                    int no = I(f[0]);
                    if (no == 0) break;
                    // ikinci satır 7./8. alan: kolon üst/alt kotu (kademeli kolonlarda kat kotundan farklı; K_BLG_03 419: 18.30/13.50)
                    var g = l[i + 1].Split(',');
                    m.Kolonlar.Add(new St4Kolon { Sira = m.Kolonlar.Count, No = no, Bx = I(f[1]), By = I(f[2]),
                        UstKot = D(g.ElementAtOrDefault(6)), AltKot = D(g.ElementAtOrDefault(7)) });
                }

            // Poligon kolonlar (L/T kesit): COL/SMX'te normal kolonlardan sonra bu sırayla gelir (VKF_01, CP_03).
            // 1. satır: no, kesit kimliği; 2. satır 3./4. alan: üst/alt kot. Kesit köşeleri /Polygon section/: x, y (cm), kimlik.
            var kesit = new Dictionary<int, List<(double x, double y)>>();
            k = Bolum("/Polygon section/");
            if (k >= 0)
                for (int i = k + 1; i < l.Length && !l[i].StartsWith("/"); i++)
                {
                    var f = l[i].Split(',');
                    int id = I(f.ElementAtOrDefault(2));
                    if (id == 0) continue;
                    if (!kesit.TryGetValue(id, out var p)) kesit[id] = p = new List<(double, double)>();
                    p.Add((D(f[0]), D(f[1])));
                }
            k = Bolum("/Polygon columns/");
            if (k >= 0)
                for (int i = k + 1; i + 1 < l.Length && !l[i].StartsWith("/"); i += 2)
                {
                    var f = l[i].Split(',');
                    int no = I(f[0]);
                    if (no == 0) break;
                    var g = l[i + 1].Split(',');
                    var p = kesit.GetValueOrDefault(I(f.ElementAtOrDefault(1))) ?? new List<(double x, double y)>();
                    double alan = 0, kisa = double.MaxValue;
                    for (int j = 0; j < p.Count; j++)
                    {
                        var a = p[j]; var b = p[(j + 1) % p.Count];
                        alan += a.x * b.y - b.x * a.y;
                        double u = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
                        if (u > 0.5) kisa = Math.Min(kisa, u);
                    }
                    m.Kolonlar.Add(new St4Kolon
                    {
                        Sira = m.Kolonlar.Count, No = no, Poligon = true, Alan = Math.Abs(alan) / 2,
                        Bx = p.Count > 0 ? (int)Math.Round(p.Max(q => q.x) - p.Min(q => q.x)) : 0,
                        By = p.Count > 0 ? (int)Math.Round(p.Max(q => q.y) - p.Min(q => q.y)) : 0,
                        UstKot = D(g.ElementAtOrDefault(2)), AltKot = D(g.ElementAtOrDefault(3)), KisaKenar = kisa == double.MaxValue ? 0 : kisa
                    });
                }

            k = Bolum("/Floors Data/");
            if (k >= 0)
                for (int i = k + 1; i + 1 < l.Length && !l[i].StartsWith("/"); i += 2)
                {
                    var f = l[i].Split(',');
                    int no = I(f[0]);
                    if (no == 0) break;
                    m.Dosemeler.Add(new St4Doseme { Sira = m.Dosemeler.Count, No = no, D = I(f[1]), G = D(f[2]), Q = D(f[3]) });
                }

            int tk = m.Taban(m.Kirisler.Select(x => x.No)), ts = m.Taban(m.Kolonlar.Select(x => x.No)), td = m.Taban(m.Dosemeler.Select(x => x.No));
            foreach (var x in m.Kirisler) { x.Ad = m.Ad('K', x.No, tk); x.Kat = x.No / tk; x.Yerel = x.No % tk; }
            foreach (var x in m.Kolonlar) { x.Ad = m.Ad('S', x.No, ts); x.Kat = x.No / ts; x.Yerel = x.No % ts; }
            foreach (var x in m.Dosemeler) { x.Ad = m.Ad('D', x.No, td); x.Kat = x.No / td; x.Yerel = x.No % td; }
            return m;
        }
    }
}
