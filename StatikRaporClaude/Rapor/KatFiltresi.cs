using System.Text.RegularExpressions;
using StatikRaporClaude.Dizgi;

namespace StatikRaporClaude.Rapor
{
    /// <summary>
    /// STA4 "Kat yazdırma seçimi": eleman adındaki kat kodu (KZ-03 → Z, S1-01 → 1, D1-04 → 1) seçili değilse satır atlanır.
    /// Adı olmayan devam satırları, bağlı oldukları elemanla birlikte atlanır.
    /// </summary>
    public static class KatFiltresi
    {
        private static readonly Regex Eleman = new Regex(@"^(?:K|S|D|P)(?<kat>[A-Z0-9]{1,2})-\d+");

        /// <summary>Kat kodu; eleman adı değilse null.</summary>
        public static string Kod(string metin)
        {
            var m = Eleman.Match((metin ?? "").Trim());
            return m.Success ? m.Groups["kat"].Value : null;
        }

        public static bool Dahil(string elemanAdi, ISet<string> izin) =>
            izin == null || Kod(elemanAdi) is not string k || izin.Contains(k);

        public static void Uygula(TabloOgesi t, ISet<string> izin)
        {
            if (izin == null) return;
            bool dahil = true;
            var kalan = new List<TabloSatir>();
            foreach (var r in t.Govde)
            {
                var ilk = r.Hucreler.FirstOrDefault(h => h.Ilk == 0);
                string kod = Kod(ilk?.Metin);
                if (kod != null) dahil = izin.Contains(kod);
                else if (r.GrupBasi) dahil = true;
                if (dahil) kalan.Add(r);
            }
            t.Govde.Clear();
            t.Govde.AddRange(kalan);
        }
    }
}
