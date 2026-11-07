using System.Globalization;
using System.Text;

namespace StatikRaporClaude.Gpr
{
    /// <summary>GPR çizgi kaydı: x1,y1,x2,y2,stil,-1,kalınlık,..</summary>
    public sealed class GprCizgi
    {
        public float X1, Y1, X2, Y2;
        public int Stil;
        public int Kalinlik;
    }

    /// <summary>GPR yazı kaydı: x,y,a,b,tip,font satırı + altındaki metin.</summary>
    public sealed class GprYazi
    {
        public float X, Y;
        public float A, B;
        public int Tip;
        public int Font;
        /// <summary>Ham bayt metni 1254 ile çözülmüş hali (STA Coursta yazı tipi bu kodlarla çizer).</summary>
        public string Ham;
        /// <summary>Okunur Unicode metin (çerçeve karakterleri gerçek kutu çizgisine çevrilmiş).</summary>
        public string Metin;
    }

    /// <summary>GPR dikdörtgen kaydı: x1,y1,x2,y2,çerçeve,dolgu(≥0),kalınlık,mod. Mod 2 dolu; mod 3 daire (x1,y1 merkez, x2 yarıçap).</summary>
    public sealed class GprKutu
    {
        public float X1, Y1, X2, Y2;
        public int Cerceve, Dolgu, Mod;

        public System.Drawing.RectangleF Alan => Mod == 3
            ? new System.Drawing.RectangleF(X1 - X2, Y1 - X2, 2 * X2, 2 * X2)
            : System.Drawing.RectangleF.FromLTRB(Math.Min(X1, X2), Math.Min(Y1, Y2), Math.Max(X1, X2), Math.Max(Y1, Y2));
    }

    /// <summary>GPR resim ekleme kaydı: x,y,0,0,0,-3,0,4,no. Resmin kendisi GPR'de yok.</summary>
    public sealed class GprResim
    {
        public float X, Y;
        public int No;
    }

    public sealed class GprSayfa
    {
        public int Sira;          // dosyadaki sıra (0 tabanlı)
        public int No;            // STA4 sayfa numarası
        public string BaslikSatiri;
        public readonly List<GprCizgi> Cizgiler = new List<GprCizgi>();
        public readonly List<GprKutu> Kutular = new List<GprKutu>();
        public readonly List<GprYazi> Yazilar = new List<GprYazi>();
        public readonly List<GprResim> Resimler = new List<GprResim>();

        /// <summary>Bu sayfada başlayan içindekiler girdisi (varsa).</summary>
        public string Baslik;
        /// <summary>Sayfanın ait olduğu bölümler (başlık sayfa ortasındaysa iki bölüm).</summary>
        public readonly List<GprBolum> Bolumler = new List<GprBolum>();
    }

    public sealed class GprBelgesi
    {
        public string Yol;
        public DateTime Tarih;
        public readonly List<GprSayfa> Sayfalar = new List<GprSayfa>();
        public readonly List<GprBolum> Bolumler = new List<GprBolum>();
        public string Firma;
        public string Proje;
        public string RaporTarihi;
        public int OkunamayanSatir;

        public static GprBelgesi Oku(string yol)
        {
            var enc = Encoding.GetEncoding(1254);
            var satirlar = PaylasimliDosya.Lines(yol, enc);
            var b = new GprBelgesi { Yol = yol, Tarih = File.GetLastWriteTime(yol) };
            GprSayfa sayfa = null;

            for (int i = 0; i < satirlar.Length; i++)
            {
                string s = satirlar[i];
                var f = SayilariAyir(s);
                if (f == null)
                {
                    if (s.Length > 0) b.OkunamayanSatir++;
                    continue;
                }

                if (f.Length == 3)
                {
                    // "0,0,0" ayraç; gerçek sayfa başı "no,a,b"
                    if (f[0] == 0 && f[1] == 0 && f[2] == 0) continue;
                    sayfa = new GprSayfa { Sira = b.Sayfalar.Count, No = (int)f[0], BaslikSatiri = s };
                    b.Sayfalar.Add(sayfa);
                    continue;
                }
                if (sayfa == null) continue;

                if (f.Length == 6)
                {
                    string ham = i + 1 < satirlar.Length ? satirlar[i + 1] : "";
                    i++;
                    if (ham.StartsWith("@^#", StringComparison.Ordinal)) continue; // şifreli lisans satırı
                    sayfa.Yazilar.Add(new GprYazi
                    {
                        X = f[0], Y = f[1], A = f[2], B = f[3], Tip = (int)f[4], Font = (int)f[5],
                        Ham = ham, Metin = StaKodlama.Unicode(ham)
                    });
                }
                else if (f.Length == 9 && (int)f[5] == -3)
                {
                    sayfa.Resimler.Add(new GprResim { X = f[0], Y = f[1], No = (int)f[8] });
                }
                else if (f.Length >= 8 && f[5] >= 0)
                {
                    // x1,y1,x2,y2,çerçeveRengi,dolguRengi,kalınlık,mod: mod 2 = dolu dikdörtgen
                    sayfa.Kutular.Add(new GprKutu
                    {
                        X1 = f[0], Y1 = f[1], X2 = f[2], Y2 = f[3], Cerceve = (int)f[4], Dolgu = (int)f[5], Mod = (int)f[7]
                    });
                }
                else if (f.Length >= 7)
                {
                    sayfa.Cizgiler.Add(new GprCizgi
                    {
                        X1 = f[0], Y1 = f[1], X2 = f[2], Y2 = f[3], Stil = (int)f[4], Kalinlik = (int)f[6]
                    });
                }
            }

            b.BasligiOku();
            GprBolumleri.Ata(b);
            return b;
        }

        private void BasligiOku()
        {
            var ilk = Sayfalar.FirstOrDefault();
            if (ilk == null) return;
            foreach (var y in ilk.Yazilar.Where(t => t.Y < 120))
            {
                string m = y.Metin.Trim();
                if (m.StartsWith("FİRMA", StringComparison.OrdinalIgnoreCase)) Firma = m.Split(':').Last().Trim();
                else if (m.StartsWith("PROJE", StringComparison.OrdinalIgnoreCase)) Proje = m.Split(':').Last().Trim();
                else if (m.Length == 10 && m[2] == '-' && m[5] == '-') RaporTarihi = m;
            }
        }

        private static float[] SayilariAyir(string s)
        {
            if (s.Length == 0 || s.IndexOf(',') < 0) return null;
            var p = s.Split(',');
            var f = new float[p.Length];
            for (int k = 0; k < p.Length; k++)
                if (!float.TryParse(p[k], NumberStyles.Float, CultureInfo.InvariantCulture, out f[k])) return null;
            return f;
        }
    }
}
