using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace RaporBirlestir
{
    /// <summary>Birleştirilecek tek PDF: türü (Kapak/Rapor/Ek), yolu ve alınacak sayfalar.</summary>
    internal sealed class Parca
    {
        public string Tur = "Rapor";
        public string Yol = "";
        public string Sayfalar = "";   // boş = tümü; "2-", "1,3-5", "-4", "son"
        public int SayfaSayisi;
        public bool PaspayKontrol;     // STA4CAD statik raporu: kolon paspayı satırını 4 / 5,5 yap
        public int MevcutKapak;        // dosyanın başındaki eski kapak sayfası adedi (algılanan)
        public bool KapakAtla;         // bölümde yeni kapak var: eski kapak sayfaları alınmaz
        public string Ad => Path.GetFileNameWithoutExtension(Yol);

        /// <summary>Gerçekte alınacak sayfalar: elle yazılmışsa o; değilse (KapakAtla) eski kapak hariç tümü.</summary>
        public List<int> EtkinSayfalar(int adet)
        {
            if (string.IsNullOrWhiteSpace(Sayfalar) && KapakAtla && MevcutKapak > 0 && MevcutKapak < adet)
                return Enumerable.Range(MevcutKapak, adet - MevcutKapak).ToList();
            return Birlestirici.SayfaListesi(Sayfalar, adet);
        }

        public bool EskiKapakAtlaniyor => string.IsNullOrWhiteSpace(Sayfalar) && KapakAtla && MevcutKapak > 0 && MevcutKapak < SayfaSayisi;

        /// <summary>Dosyayı okuyup sayfa sayısını ve baştaki eski kapağı belirler.</summary>
        public static Parca Oku(string yol, string tur)
        {
            var p = new Parca { Yol = yol, Tur = tur, SayfaSayisi = Birlestirici.SayfaSay(yol) };
            try { p.MevcutKapak = KapakAlgila.BastakiKapak(yol); } catch { p.MevcutKapak = 0; }
            return p;
        }
    }

    internal static class Birlestirici
    {
        public static readonly string[] Turler = { "Kapak", "Rapor", "Ek" };

        public static int SayfaSay(string yol)
        {
            using var d = PdfReader.Open(yol, PdfDocumentOpenMode.Import);
            return d.PageCount;
        }

        /// <summary>"1,3-5", "2-", "-4", "son", "1-son" → 0 tabanlı sayfa sırası. Boş = tümü.</summary>
        public static List<int> SayfaListesi(string ifade, int adet)
        {
            var liste = new List<int>();
            ifade = (ifade ?? "").Trim().ToLowerInvariant();
            if (ifade.Length == 0 || ifade == "tümü" || ifade == "tumu" || ifade == "*")
            {
                for (int i = 0; i < adet; i++) liste.Add(i);
                return liste;
            }
            foreach (var hamParca in ifade.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = hamParca.Trim();
                int Sayi(string s, int bos)
                {
                    if (s.Length == 0) return bos;
                    if (s == "son") return adet;
                    if (!int.TryParse(s, out var n)) throw new FormatException($"Geçersiz sayfa ifadesi \"{p}\".");
                    return n;
                }
                int bas, bit;
                int tire = p.IndexOf('-');
                if (tire < 0) bas = bit = Sayi(p, 1);
                else { bas = Sayi(p[..tire].Trim(), 1); bit = Sayi(p[(tire + 1)..].Trim(), adet); }
                if (bas < 1 || bit > adet || bas > bit)
                    throw new FormatException($"Geçersiz sayfa aralığı \"{p}\" (dosyada {adet} sayfa var).");
                for (int i = bas; i <= bit; i++) liste.Add(i - 1);
            }
            return liste;
        }

        /// <summary>Çıktıya eklenen rapor sayfalarında kolon paspayı satırını arar ve düzeltir.</summary>
        private static void PaspayIsle(PdfDocument hedef, int basIndeks, Parca p, List<string> notlar, string paspayHedef)
        {
            var bulunan = new List<PaspayDuzelt.Sonuc>();
            var tarama = new PaspayDuzelt.Tarama();
            for (int i = basIndeks; i < hedef.PageCount; i++)
                bulunan.AddRange(PaspayDuzelt.SayfaDuzelt(hedef.Pages[i], i + 1, tarama, paspayHedef));
            if (notlar == null) return;
            if (bulunan.Count == 0)
                notlar.Add($"UYARI {p.Ad}: KOLON/PERDE bloğunda \"Etriye paspayı / Boyuna donatı paspayı\" satırı bulunamadı (kolon paspayı kontrol edilemedi).");
            foreach (var s in bulunan)
                notlar.Add((s.Hata != null ? "UYARI " : "") + $"{p.Ad} kolon paspayı {s}");
        }

        /// <summary>
        /// Bölümleri sırayla tek PDF'e yazar; bölüm ve dosya başlarına yer imi ekler.
        /// Toplam sayfa sayısını döndürür. Hata olursa çıktı dosyası yazılmaz.
        /// </summary>
        public static int Birlestir(IList<(string Bolum, IList<Parca> Parcalar)> bolumler, string cikti,
            List<string> notlar = null, string paspayHedef = PaspayDuzelt.Hedef)
        {
            var tamCikti = Path.GetFullPath(cikti);
            foreach (var (_, parcalar) in bolumler)
                foreach (var p in parcalar)
                {
                    if (!File.Exists(p.Yol)) throw new FileNotFoundException("Dosya bulunamadı: " + p.Yol);
                }
            // Not: kaynaklar belleğe okunarak açılır; çıktı bir kaynakla aynı dosya olsa bile
            // (ör. statik rapor kendi adıyla kaydedilirken) geçici dosya + taşıma ile güvenle yazılır.

            var hedef = new PdfDocument();
            hedef.Info.Creator = "BEYKENT Rapor Birleştir";
            hedef.PageMode = PdfPageMode.UseOutlines;
            bool cokBolum = bolumler.Count(b => b.Parcalar.Count > 0) > 1;

            foreach (var (bolum, parcalar) in bolumler)
            {
                PdfOutline bolumIm = null;
                foreach (var p in parcalar)
                {
                    PdfDocument kaynak;
                    try { kaynak = PdfReader.Open(new MemoryStream(File.ReadAllBytes(p.Yol)), PdfDocumentOpenMode.Import); }
                    catch (Exception ex)
                    {
                        throw new IOException($"{Path.GetFileName(p.Yol)} açılamadı (şifreli veya bozuk olabilir): {ex.Message}", ex);
                    }
                    using (kaynak)
                    {
                        PdfPage ilk = null;
                        int basIndeks = hedef.PageCount;
                        foreach (var i in p.EtkinSayfalar(kaynak.PageCount))
                        {
                            var eklenen = hedef.AddPage(kaynak.Pages[i]);
                            ilk ??= eklenen;
                        }
                        if (ilk == null) continue;
                        if (p.PaspayKontrol) PaspayIsle(hedef, basIndeks, p, notlar, paspayHedef);
                        string baslik = $"{p.Tur} – {p.Ad}";
                        if (cokBolum)
                        {
                            bolumIm ??= hedef.Outlines.Add(bolum, ilk, true);
                            bolumIm.Outlines.Add(baslik, ilk, false);
                        }
                        else hedef.Outlines.Add(baslik, ilk, false);
                    }
                }
            }
            if (hedef.PageCount == 0) throw new InvalidOperationException("Birleştirilecek sayfa yok.");

            // Önce geçici dosyaya yaz; başarılıysa yerine taşı (açık PDF'in üstüne yarım yazmasın).
            var klasor = Path.GetDirectoryName(tamCikti);
            if (!string.IsNullOrEmpty(klasor)) Directory.CreateDirectory(klasor);
            var gecici = tamCikti + ".yaziliyor";
            int adet = hedef.PageCount;
            hedef.Save(gecici);
            try { File.Move(gecici, tamCikti, true); }
            catch (Exception ex)
            {
                try { File.Delete(gecici); } catch { }
                throw new IOException($"Çıktı yazılamadı; dosya başka programda (PDF görüntüleyici) açık olabilir:\n{tamCikti}\n{ex.Message}", ex);
            }
            return adet;
        }
    }
}
