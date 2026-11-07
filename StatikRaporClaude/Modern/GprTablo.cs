using StatikRaporClaude.Gpr;

namespace StatikRaporClaude.Modern
{
    /// <summary>GPR kutu-çizgili metin tablosunun bir satırı: hücreler '│' ile ayrılmış.</summary>
    public sealed class TabloSatiri
    {
        public GprSayfa Sayfa;
        public float Y;
        public string[] Hucreler;
        public bool Ayrac;   // ├──┼──┤ / └──┴──┘ / ┌──┬──┐
    }

    public static class GprTablo
    {
        /// <summary>Bölüm sayfalarındaki tablo satırlarını sayfa ve y sırasıyla verir.</summary>
        public static List<TabloSatiri> Satirlar(GprBolum bolum)
        {
            var liste = new List<TabloSatiri>();
            foreach (var s in bolum.Sayfalar)
            {
                foreach (var y in s.Yazilar.Where(t => t.Tip == 8 && t.Font == 1 && bolum.Icinde(s, t.Y)).OrderBy(t => t.Y).ThenBy(t => t.X))
                {
                    string m = y.Metin.TrimEnd();
                    string bas = m.TrimStart();
                    if (bas.Length == 0) continue;
                    char c = bas[0];
                    if (c == '├' || c == '└' || c == '┌')
                        liste.Add(new TabloSatiri { Sayfa = s, Y = y.Y, Ayrac = true, Hucreler = Array.Empty<string>() });
                    else if (c == '│')
                    {
                        var h = bas.Split('│');
                        // baştaki ve sondaki boş parça atılır
                        var hucre = h.Skip(1).Take(Math.Max(0, h.Length - 2)).Select(x => x.Trim()).ToArray();
                        if (h.Length >= 2 && h[^1].Trim().Length > 0) hucre = hucre.Append(h[^1].Trim()).ToArray();
                        liste.Add(new TabloSatiri { Sayfa = s, Y = y.Y, Hucreler = hucre });
                    }
                }
            }
            return liste;
        }

        /// <summary>Ayraçlarla bölünmüş veri blokları (başlık satırları dahil, sonra elenir).</summary>
        public static List<List<TabloSatiri>> Bloklar(List<TabloSatiri> satirlar)
        {
            var bloklar = new List<List<TabloSatiri>>();
            var aktif = new List<TabloSatiri>();
            foreach (var s in satirlar)
            {
                if (s.Ayrac) { if (aktif.Count > 0) bloklar.Add(aktif); aktif = new List<TabloSatiri>(); }
                else aktif.Add(s);
            }
            if (aktif.Count > 0) bloklar.Add(aktif);
            return bloklar;
        }

        public static string Hucre(TabloSatiri s, int i) => s != null && i < s.Hucreler.Length ? s.Hucreler[i] : "";
    }
}
