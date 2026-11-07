using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ST4PlanIdCiz
{
    internal enum KirisManuelMesnetTip { Kolon = 0, Kiris = 1, Serbest = 2 }

    internal sealed class KirisManuelMesnet
    {
        public double EniCm = 50;
        public KirisManuelMesnetTip Tip;
    }

    /// <summary>Bir açıklık. Donatı hücreleri "3ø16" biçiminde; etriye "ø10/10/20" = çap / sık / orta.</summary>
    internal sealed class KirisManuelAciklik
    {
        public string Ad = "K01";
        public double BCm = 30, HCm = 60, LnCm = 450, DkotCm, DosemeCm = 14;
        public string Ust = "3ø16", Alt = "3ø16", Govde = "", SolUst = "", SagUst = "", SolAlt = "", SagAlt = "", Etriye = "ø10/10/20";

        public KirisManuelAciklik Kopya() => (KirisManuelAciklik)MemberwiseClone();
    }

    /// <summary>KIRISCIZ girdisi: ST4'süz, formda yazılan sürekli kiriş (n açıklık, n+1 mesnet).</summary>
    internal sealed class KirisManuelVeri
    {
        public double PaspayiCm = 4, KatKotuCm;
        public int Fck = 30, Fyk = 420;
        public bool SonKat, IkinciGonyeKaldir;
        /// <summary>Geniş (bant) kirişte de tek kapalı etriye; kapalıysa kol arası 35 cm aşınca çift etriye.</summary>
        public bool TekEtriye;
        public List<KirisManuelMesnet> Mesnetler = new List<KirisManuelMesnet>();
        public List<KirisManuelAciklik> Acikliklar = new List<KirisManuelAciklik>();

        internal static KirisManuelVeri Varsayilan()
        {
            var v = new KirisManuelVeri();
            for (int i = 0; i < 3; i++)
                v.Acikliklar.Add(new KirisManuelAciklik { Ad = "K" + (i + 1).ToString("00", CultureInfo.InvariantCulture) });
            for (int i = 0; i < 4; i++) v.Mesnetler.Add(new KirisManuelMesnet());
            return v;
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string D(double v) => v.ToString("0.###", Inv);
        private static string Temiz(string s) => (s ?? "").Replace("|", " ").Replace("¦", " ").Replace("=", " ").Trim();

        /// <summary>Tek satırlık metin (PlanFormSettings değeri ve .kiris dosyası).</summary>
        internal string Yaz()
        {
            var sb = new StringBuilder();
            sb.Append("G|").Append(D(PaspayiCm)).Append('|').Append(D(KatKotuCm)).Append('|').Append(Fck).Append('|').Append(Fyk)
              .Append('|').Append(SonKat ? 1 : 0).Append('|').Append(IkinciGonyeKaldir ? 1 : 0).Append('|').Append(TekEtriye ? 1 : 0);
            foreach (var m in Mesnetler)
                sb.Append("¦M|").Append(D(m.EniCm)).Append('|').Append((int)m.Tip);
            foreach (var a in Acikliklar)
                sb.Append("¦A|").Append(Temiz(a.Ad)).Append('|').Append(D(a.BCm)).Append('|').Append(D(a.HCm)).Append('|').Append(D(a.LnCm))
                  .Append('|').Append(D(a.DkotCm)).Append('|').Append(D(a.DosemeCm))
                  .Append('|').Append(Temiz(a.Ust)).Append('|').Append(Temiz(a.Alt)).Append('|').Append(Temiz(a.Govde))
                  .Append('|').Append(Temiz(a.SolUst)).Append('|').Append(Temiz(a.SagUst)).Append('|').Append(Temiz(a.SolAlt))
                  .Append('|').Append(Temiz(a.SagAlt)).Append('|').Append(Temiz(a.Etriye));
            return sb.ToString();
        }

        internal static KirisManuelVeri Oku(string metin)
        {
            if (string.IsNullOrWhiteSpace(metin)) return null;
            var v = new KirisManuelVeri();
            double Sayi(string s, double d) => double.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, Inv, out double r) ? r : d;
            foreach (string kayit in metin.Replace("\r", "").Replace("\n", "¦").Split('¦'))
            {
                string[] p = kayit.Trim().Split('|');
                if (p.Length < 2) continue;
                switch (p[0])
                {
                    case "G" when p.Length >= 7:
                        v.PaspayiCm = Sayi(p[1], 4); v.KatKotuCm = Sayi(p[2], 0);
                        v.Fck = (int)Sayi(p[3], 30); v.Fyk = (int)Sayi(p[4], 420);
                        v.SonKat = p[5] == "1"; v.IkinciGonyeKaldir = p[6] == "1";
                        v.TekEtriye = p.Length > 7 && p[7] == "1";
                        break;
                    case "M" when p.Length >= 3:
                        int tip = (int)Sayi(p[2], 0);
                        v.Mesnetler.Add(new KirisManuelMesnet { EniCm = Sayi(p[1], 50), Tip = tip == 1 || tip == 2 ? (KirisManuelMesnetTip)tip : KirisManuelMesnetTip.Kolon });
                        break;
                    case "A" when p.Length >= 15:
                        v.Acikliklar.Add(new KirisManuelAciklik
                        {
                            Ad = p[1], BCm = Sayi(p[2], 30), HCm = Sayi(p[3], 60), LnCm = Sayi(p[4], 450), DkotCm = Sayi(p[5], 0), DosemeCm = Sayi(p[6], 14),
                            Ust = p[7], Alt = p[8], Govde = p[9], SolUst = p[10], SagUst = p[11], SolAlt = p[12], SagAlt = p[13], Etriye = p[14]
                        });
                        break;
                }
            }
            if (v.Acikliklar.Count == 0) return null;
            while (v.Mesnetler.Count < v.Acikliklar.Count + 1) v.Mesnetler.Add(new KirisManuelMesnet());
            if (v.Mesnetler.Count > v.Acikliklar.Count + 1) v.Mesnetler.RemoveRange(v.Acikliklar.Count + 1, v.Mesnetler.Count - v.Acikliklar.Count - 1);
            return v;
        }

        // Ayraç: ø işareti, f/fi, /, x, *, -, nokta, virgül, + veya boşluk (ø yazmak zorunlu değil).
        private static readonly Regex DonatiRx = new Regex(@"^\s*(\d{1,3})\s*(?:[øØΦφ/xX*\-.,+\s]|fi|FI|f|F)\s*(\d{1,2})\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex BitisikRx = new Regex(@"^\s*(\d{2,5})\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex EtriyeRx = new Regex(@"^\s*(?:[øØΦφ]|fi|FI|f|F)?\s*(\d{1,2})\s*[/xX*\-.,+\s]\s*(\d{1,3})\s*(?:[/xX*\-.,+\s]\s*(\d{1,3}))?\s*$", RegexOptions.CultureInvariant);
        /// <summary>Ayraçsız "316" yazımında sondan aranan çaplar (önce iki haneli).</summary>
        private static readonly int[] Caplar = { 32, 28, 25, 22, 20, 18, 16, 14, 12, 10, 8 };

        /// <summary>"3ø16", "3 16", "3/16", "3.16", "3x16", "3f16" veya ayraçsız "316" (= 3ø16, "1012" = 10ø12). Boş hücre = donatı yok.</summary>
        internal static bool TryDonati(string s, out int adet, out int cap)
        {
            adet = cap = 0;
            if (string.IsNullOrWhiteSpace(s) || s.Trim() == "0" || s.Trim() == "-") return true;
            var m = DonatiRx.Match(s);
            if (m.Success)
            {
                adet = int.Parse(m.Groups[1].Value, Inv);
                cap = int.Parse(m.Groups[2].Value, Inv);
            }
            else
            {
                var b = BitisikRx.Match(s);
                if (!b.Success) return false;
                string d = b.Groups[1].Value;
                bool bulundu = false;
                foreach (int c in Caplar)
                {
                    string cs = c.ToString(Inv);
                    if (d.Length <= cs.Length || !d.EndsWith(cs, StringComparison.Ordinal)) continue;
                    adet = int.Parse(d.Substring(0, d.Length - cs.Length), Inv);
                    cap = c;
                    bulundu = adet > 0;
                    break;
                }
                if (!bulundu) { adet = cap = 0; return false; }
            }
            if (adet == 0) { cap = 0; return true; }
            return cap >= 6 && cap <= 50;
        }

        /// <summary>"ø10/10/20" = çap/sık/orta; "10 10 20", "10.10.20", "10/15" (sık = orta) de olur.</summary>
        internal static bool TryEtriye(string s, out int cap, out int sik, out int orta)
        {
            cap = sik = orta = 0;
            var m = EtriyeRx.Match(s ?? "");
            if (!m.Success) return false;
            cap = int.Parse(m.Groups[1].Value, Inv);
            sik = int.Parse(m.Groups[2].Value, Inv);
            orta = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, Inv) : sik;
            return cap >= 6 && cap <= 20 && sik >= 3 && orta >= sik && orta <= 50;
        }

        /// <summary>Okunabilen donatıyı "3ø16" biçimine getirir; adet 0 → boş; okunamazsa aynen döner.</summary>
        internal static string DonatiBicim(string s)
        {
            if (!TryDonati(s, out int adet, out int cap)) return s;
            return adet <= 0 ? "" : adet.ToString(Inv) + "ø" + cap.ToString(Inv);
        }

        /// <summary>Okunabilen etriyeyi "ø10/10/20" biçimine getirir; okunamazsa aynen döner.</summary>
        internal static string EtriyeBicim(string s)
        {
            if (!TryEtriye(s, out int cap, out int sik, out int orta)) return s;
            return "ø" + cap.ToString(Inv) + "/" + sik.ToString(Inv) + "/" + orta.ToString(Inv);
        }

        internal static double AsCm2(int adet, int cap) => adet * Math.PI * cap * cap / 400.0;

        /// <summary>Çizimden önce bütün girdiyi denetler; ilk hatayı Türkçe döndürür.</summary>
        internal bool Dogrula(out string hata)
        {
            hata = null;
            int n = Acikliklar.Count;
            if (n == 0) { hata = "En az bir açıklık girin."; return false; }
            if (n > 30) { hata = "En çok 30 açıklık çizilebilir."; return false; }
            if (Mesnetler.Count != n + 1) { hata = "Mesnet sayısı açıklık sayısı + 1 olmalı."; return false; }
            if (PaspayiCm < 0.5 || PaspayiCm > 15) { hata = "Paspayı 0,5–15 cm arasında olmalı."; return false; }
            for (int k = 0; k <= n; k++)
            {
                var m = Mesnetler[k];
                bool uc = k == 0 || k == n;
                if (m.Tip == KirisManuelMesnetTip.Serbest && !uc) { hata = string.Format(Inv, "Mesnet {0}: serbest uç (konsol) yalnız ilk veya son mesnet olabilir.", k + 1); return false; }
                if (m.Tip != KirisManuelMesnetTip.Serbest && (m.EniCm < 10 || m.EniCm > 500)) { hata = string.Format(Inv, "Mesnet {0}: eni 10–500 cm olmalı.", k + 1); return false; }
            }
            if (Mesnetler[0].Tip == KirisManuelMesnetTip.Serbest && Mesnetler[n].Tip == KirisManuelMesnetTip.Serbest)
            { hata = "İki uç birden serbest olamaz."; return false; }
            var adlar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < n; i++)
            {
                var a = Acikliklar[i];
                string ad = string.IsNullOrWhiteSpace(a.Ad) ? "Açıklık " + (i + 1) : a.Ad.Trim();
                if (string.IsNullOrWhiteSpace(a.Ad)) { hata = ad + ": kiriş adı boş."; return false; }
                if (!adlar.Add(ad)) { hata = ad + ": aynı ad iki açıklıkta kullanılmış."; return false; }
                if (a.BCm < 2 * (PaspayiCm + 1.5) + 1 || a.BCm > 300) { hata = ad + ": genişlik (b) paspayına göre çok küçük veya 300 cm'den büyük."; return false; }
                if (a.HCm < 2 * (PaspayiCm + 1.5) + 1 || a.HCm > 400) { hata = ad + ": yükseklik (h) paspayına göre çok küçük veya 400 cm'den büyük."; return false; }
                if (a.LnCm < 20 || a.LnCm > 3000) { hata = ad + ": net açıklık 20–3000 cm olmalı."; return false; }
                // Bant kiriş: döşeme kiriş yüksekliğine eşit olabilir (sarkmasız).
                if (a.DosemeCm < 0 || a.DosemeCm > a.HCm + 0.01) { hata = ad + ": döşeme kalınlığı 0 ile h arasında olmalı (0 = döşeme yok, h = bant kiriş)."; return false; }
                if (Math.Abs(a.DkotCm) > 1000) { hata = ad + ": kot farkı ±1000 cm içinde olmalı."; return false; }
                foreach (var (alan, deger, zorunlu) in new[] { ("Üst", a.Ust, true), ("Alt", a.Alt, true), ("Gövde", a.Govde, false),
                    ("Sol üst ilave", a.SolUst, false), ("Sağ üst ilave", a.SagUst, false), ("Sol alt ilave", a.SolAlt, false), ("Sağ alt ilave", a.SagAlt, false) })
                {
                    if (!TryDonati(deger, out int ad1, out _)) { hata = string.Format(Inv, "{0}: {1} donatısı okunamadı ('{2}'). Örnek: 3ø16", ad, alan, deger); return false; }
                    if (zorunlu && ad1 < 2) { hata = string.Format(Inv, "{0}: {1} donatısı en az 2 çubuk olmalı.", ad, alan); return false; }
                }
                if (!TryEtriye(a.Etriye, out _, out _, out _)) { hata = string.Format(Inv, "{0}: etriye okunamadı ('{1}'). Örnek: ø10/10/20 (çap/sık/orta)", ad, a.Etriye); return false; }
            }
            return true;
        }
    }

}
