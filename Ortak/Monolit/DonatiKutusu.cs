using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Beykent.Monolit.Girdi
{
    /// <summary>
    /// Donatıyı tek satırda alır. "ø" kutuda sabit önektir, kullanıcı yalnız "12/15" (veya "12 15", "12-15") yazar.
    /// Yapıştırılan ya da alışkanlıkla yazılan ø, Ø, Φ, %%c, f, fi önekleri de kabul edilir.
    /// Yanında 1 m için donatı alanını (cm²/m) gösterir. Çap yalnız standart çaplardan olabilir.
    /// </summary>
    internal sealed class DonatiKutusu : GirdiKutusu
    {
        internal static readonly int[] StandartCaplar = { 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 25, 26, 28, 30, 32, 36, 40 };
        private static readonly Regex Desen = new Regex(
            @"^\s*(?:[øØΦφ]|%%[cC]|[fF][iI]?)?\s*(\d{1,2})\s*(?:/|-|\*|x|X|\s)\s*(\d{1,3}(?:[.,]\d)?)\s*$", RegexOptions.CultureInvariant);

        private int _cap; private double _aralik;
        public int MinCap { get; }
        public int MaxCap { get; }
        public double MinAralik { get; }
        public double MaxAralik { get; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int VarsayilanCap { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public double VarsayilanAralik { get; set; }

        public DonatiKutusu(string ad, int cap, double aralik, int minCap = 8, int maxCap = 32, double minAralik = 5, double maxAralik = 50)
        {
            Ad = ad; MinCap = minCap; MaxCap = maxCap; MinAralik = minAralik; MaxAralik = maxAralik;
            VarsayilanCap = cap; VarsayilanAralik = aralik;
            OnekEkle("ø");
            Ayarla(cap, aralik);
        }

        public int CapMm => _cap;
        public double AralikCm => _aralik;

        /// <summary>1 m genişlikteki donatı alanı, cm²/m.</summary>
        public static double AlanCm2m(int capMm, double aralikCm)
            => aralikCm <= 0 ? 0 : Math.PI * Math.Pow(capMm / 10.0, 2) / 4.0 * 100.0 / aralikCm;

        public static string Yazim(int capMm, double aralikCm) => "ø" + KutuMetni(capMm, aralikCm);

        /// <summary>Kutuda görünen metin: önek ayrı çizildiği için ø içermez.</summary>
        internal static string KutuMetni(int capMm, double aralikCm) => capMm + "/" + GirdiTema.Yaz(aralikCm, 1);

        public void Ayarla(int capMm, double aralikCm)
        {
            _cap = capMm; _aralik = aralikCm;
            Metin.Text = KutuMetni(capMm, aralikCm);
        }

        /// <summary>Test ve form dışı kullanım için ayrıştırıcı. Hata yoksa null döner.</summary>
        internal static string Ayristir(string text, int minCap, int maxCap, double minAralik, double maxAralik,
            out int cap, out double aralik)
        {
            cap = 0; aralik = 0;
            var m = Desen.Match(text ?? "");
            if (!m.Success) return "12/15 biçiminde yazın (çap/aralık)";
            cap = int.Parse(m.Groups[1].Value);
            if (!GirdiTema.SayiOku(m.Groups[2].Value, out aralik)) return "aralık okunamadı";
            if (!StandartCaplar.Contains(cap)) return "ø" + cap + " standart çap değil";
            if (cap < minCap || cap > maxCap) return "çap ø" + minCap + "–ø" + maxCap + " arası olmalı";
            if (aralik < minAralik || aralik > maxAralik)
                return "aralık " + GirdiTema.Yaz(minAralik, 1) + "–" + GirdiTema.Yaz(maxAralik, 1) + " cm arası olmalı";
            return null;
        }

        protected override string Coz(string text)
        {
            string hata = Ayristir(text, MinCap, MaxCap, MinAralik, MaxAralik, out int cap, out double aralik);
            if (hata != null) { Bilgi.Text = "?"; return Ad + ": " + hata; }
            _cap = cap; _aralik = aralik;
            Bilgi.Text = GirdiTema.Yaz(AlanCm2m(cap, aralik), 2) + " cm²/m";
            return null;
        }

        protected override void Bicimle()
        {
            string t = KutuMetni(_cap, _aralik);
            if (Metin.Text != t) Metin.Text = t;
        }

        /// <summary>Ok tuşu: çapı bir standart kademe değiştirir (aralık sabit).</summary>
        protected override void Adim(int yon)
        {
            var liste = StandartCaplar.Where(c => c >= MinCap && c <= MaxCap).ToArray();
            int i = Array.IndexOf(liste, _cap);
            if (i < 0) i = 0;
            i = Math.Max(0, Math.Min(liste.Length - 1, i + yon));
            Ayarla(liste[i], _aralik);
            Metin.SelectAll();
        }

        public override void VarsayilanaDon() => Ayarla(VarsayilanCap, VarsayilanAralik);
    }
}
