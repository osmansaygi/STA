using System.Text;

namespace StatikRaporClaude.Proje
{
    public sealed class Kat
    {
        public string Ad;
        public string Kod;
        public double Kot;
    }

    public sealed class DosyaDurumu
    {
        public string Ad;
        public string Aciklama;
        public DateTime? Tarih;
        public string Uyari;
    }

    /// <summary>Seçilen ST4'ün klasöründeki rapor girdilerini bulur ve tarih tutarlılığını denetler.</summary>
    public sealed class ProjeKlasoru
    {
        public string St4Yolu;
        public string Klasor;
        public string TabanAd;
        public string Surum;
        public string Firma;
        public string ProjeAdi;
        public readonly List<Kat> Katlar = new List<Kat>();
        public readonly List<DosyaDurumu> Dosyalar = new List<DosyaDurumu>();
        public string GprYolu;
        public readonly List<string> EkPdfler = new List<string>();

        public static ProjeKlasoru Ac(string st4)
        {
            var p = new ProjeKlasoru
            {
                St4Yolu = st4,
                Klasor = Path.GetDirectoryName(st4),
                TabanAd = Path.GetFileNameWithoutExtension(st4)
            };
            p.St4Oku();
            p.DosyalariDenetle();
            return p;
        }

        private string[] _st4 = Array.Empty<string>();

        /// <summary>Klasörde taban adla bu uzantılı dosya var mı (ör. ".RAD", ".Fea3D").</summary>
        public bool DosyaVar(string uzanti) => File.Exists(Path.Combine(Klasor, TabanAd + uzanti));

        /// <summary>ST4 bölümünde (ör. "/Single footings/") tanımlı eleman var mı: ilk alanı "0" olmayan satır.</summary>
        public bool St4ElemanVar(string bolum)
        {
            int i = Array.FindIndex(_st4, s => s.StartsWith(bolum));
            if (i < 0) return false;
            for (int k = i + 1; k < _st4.Length && !_st4[k].StartsWith("/"); k++)
            {
                string ilk = _st4[k].Split(',')[0].Trim();
                if (ilk.Length > 0 && ilk != "0") return true;
            }
            return false;
        }

        private void St4Oku()
        {
            var l = PaylasimliDosya.Lines(St4Yolu, Encoding.GetEncoding(1254));
            _st4 = l;
            if (l.Length > 2) { Surum = l[0].Trim(); Firma = l[1].Trim(); ProjeAdi = l[2].Trim(); }
            int i = Array.FindIndex(l, s => s.StartsWith("/Story/"));
            if (i < 0) return;
            for (int k = i + 1; k + 2 < l.Length && !l[k].StartsWith("/"); k += 3)
            {
                var f = l[k + 2].Split(',');
                double.TryParse(f[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double kot);
                Katlar.Add(new Kat { Ad = l[k].Trim(), Kod = l[k + 1].Trim(), Kot = kot });
            }
        }

        private string Yol(string uzanti) => Path.Combine(Klasor, TabanAd + uzanti);

        private DateTime? Tarih(string uzanti)
        {
            string y = Yol(uzanti);
            return File.Exists(y) && new FileInfo(y).Length > 0 ? File.GetLastWriteTime(y) : (DateTime?)null;
        }

        private void DosyalariDenetle()
        {
            DateTime st4 = File.GetLastWriteTime(St4Yolu);
            Dosyalar.Add(new DosyaDurumu { Ad = TabanAd + ".ST4", Aciklama = "Model", Tarih = st4 });

            var gpr = Tarih(".GPR");
            GprYolu = gpr.HasValue ? Yol(".GPR") : null;
            Dosyalar.Add(new DosyaDurumu
            {
                Ad = TabanAd + ".GPR", Aciklama = "Hesap raporu (sayfalar)", Tarih = gpr,
                Uyari = !gpr.HasValue ? "Yok: STA4CAD'de rapor 'Grafiksel diske kaydet (.GPR)' ile alınmalı"
                    : gpr < st4 ? "Modelden eski: ST4 GPR'den sonra değişmiş" : null
            });

            var analiz = new[] { ".KMX", ".SMX", ".DMX", ".COL", ".BEA" }.Select(Tarih).Where(t => t.HasValue).Select(t => t.Value).DefaultIfEmpty().Max();
            Dosyalar.Add(new DosyaDurumu
            {
                Ad = "KMX / SMX / DMX", Aciklama = "Betonarme sonuçları",
                Tarih = analiz == default ? null : analiz,
                Uyari = gpr.HasValue && analiz > gpr.Value.AddMinutes(1) ? "GPR'den yeni: analiz rapordan sonra tekrar çalışmış" : null
            });

            var rad = Tarih(".RAD");
            if (rad.HasValue)
            {
                var radSonuc = new[] { ".ZEM", ".TMX", ".SO1", ".MATBQF" }.Select(Tarih).Where(t => t.HasValue).Select(t => t.Value).DefaultIfEmpty().Max();
                Dosyalar.Add(new DosyaDurumu
                {
                    Ad = TabanAd + ".RAD", Aciklama = "Radye analizi", Tarih = radSonuc == default ? rad : radSonuc,
                    Uyari = gpr.HasValue && radSonuc > gpr.Value.AddMinutes(1) ? "GPR'den yeni: radye rapordan sonra tekrar çözülmüş" : null
                });
            }

            // Proje kökünde ve rapor klasöründe eklenebilecek PDF'ler
            string kok = Directory.GetParent(Klasor)?.FullName;
            foreach (var d in new[] { Klasor, kok == null ? null : Path.Combine(kok, "CZM_RPR") })
            {
                if (d == null || !Directory.Exists(d)) continue;
                EkPdfler.AddRange(Directory.GetFiles(d, "*.pdf").OrderBy(x => x));
            }
        }
    }
}
