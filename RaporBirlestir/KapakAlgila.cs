using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Outline;

namespace RaporBirlestir
{
    /// <summary>
    /// Bir rapor PDF'inin başındaki mevcut (eski) kapak sayfalarını bulur.
    /// 1) Bu programla birleştirilmiş dosya: "Kapak – …" yer imlerinin kapsadığı baştaki sayfalar (kesin).
    /// 2) Diğerleri: baştan itibaren proje kapağı alanlarını (YAPI NİTELİĞİ, PAFTA/ADA/PARSEL, YÜZÖLÇÜMÜ,
    ///    BİNA İNŞAAT ALANI, KAT ADEDİ…) taşıyan sayfalar; rapor başlığı (FİRMA : … PROJE :) görülünce durur.
    /// </summary>
    internal static class KapakAlgila
    {
        private static readonly string[] KapakAlanlari =
        {
            "YAPI NITELIGI", "PAFTA", "PARSEL", "YUZOLCUMU", "INSAAT ALANI", "KAT ADEDI", "KULLANIM AMACI", "IHTIVA ETTIGI"
        };

        public static int BastakiKapak(string yol)
        {
            using var d = PdfDocument.Open(yol);

            // 1) Yer imleri (Rapor Birleştir çıktısı): baştaki "Kapak – " düğümlerinin sayfa aralığı.
            if (d.TryGetBookmarks(out var yerImleri))
            {
                var dugumler = yerImleri.GetNodes().OfType<DocumentBookmarkNode>()
                    .Where(n => n.PageNumber > 0).OrderBy(n => n.PageNumber).ToList();
                var kapaklar = dugumler.Where(n => n.Title.StartsWith("Kapak –") || n.Title.StartsWith("Kapak -")).ToList();
                if (kapaklar.Count > 0 && kapaklar.Min(n => n.PageNumber) == 1)
                {
                    var sonraki = dugumler.Where(n => !kapaklar.Contains(n) && n.PageNumber > 1 && !n.Children.Any())
                        .Select(n => n.PageNumber).DefaultIfEmpty(0).Min();
                    if (sonraki > 1) return sonraki - 1;
                }
            }

            // 2) Metin izi: en fazla ilk 3 sayfa.
            int adet = 0;
            for (int i = 1; i <= Math.Min(3, d.NumberOfPages); i++)
            {
                var metin = Normal(string.Join(" ", d.GetPage(i).GetWords().Select(w => w.Text)));
                if (RaporSayfasi(metin)) break;
                int isabet = KapakAlanlari.Count(k => metin.Contains(k));
                if (isabet < 3) break;
                adet = i;
            }
            return adet < d.NumberOfPages ? adet : 0;
        }

        /// <summary>STA4CAD / STASteel / ProtaDetails rapor sayfası başlığı.</summary>
        private static bool RaporSayfasi(string m) =>
            (m.Contains("FIRMA :") && m.Contains("PROJE :")) || m.Contains("LISANS :") || m.Contains("YAPAN:");

        /// <summary>Büyük harf, Türkçe ve cp1252 bozulmuş (ý, þ, ð, Ý, Þ, Ð) harfleri ASCII'ye indirger.</summary>
        public static string Normal(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                sb.Append(c switch
                {
                    'İ' or 'ı' or 'Ý' or 'ý' or 'i' => 'I',
                    'Ğ' or 'ğ' or 'Ð' or 'ð' => 'G',
                    'Ü' or 'ü' => 'U',
                    'Ş' or 'ş' or 'Þ' or 'þ' => 'S',
                    'Ö' or 'ö' => 'O',
                    'Ç' or 'ç' => 'C',
                    _ => char.ToUpperInvariant(c)
                });
            }
            return sb.ToString();
        }
    }
}
