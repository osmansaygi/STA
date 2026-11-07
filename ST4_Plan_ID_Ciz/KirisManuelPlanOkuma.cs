using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ST4PlanIdCiz
{
    /// <summary>Kalıp planından okunan bir açıklık: Ln (cm) her zaman; ad ve b/h yazı bulunursa.</summary>
    internal sealed class KirisPlanAciklik
    {
        public double LnCm;
        public string Ad;
        public double BCm = double.NaN, HCm = double.NaN;
    }

    /// <summary>
    /// KIRISCIZ "Kalıp planından al" sonucu (AutoCAD'siz kısım): tıklanan yüz noktalarından mesnet/açıklık aralıkları,
    /// "MC-01 (25/55)" etiket okuma ve forma birleştirme. Seçim/analiz <see cref="KirisManuelPlanSecim"/> içindedir.
    /// </summary>
    internal sealed class KirisPlanOkuma
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public List<KirisManuelMesnet> Mesnetler = new List<KirisManuelMesnet>();
        public List<KirisPlanAciklik> Acikliklar = new List<KirisPlanAciklik>();

        /// <summary>
        /// Kiriş ekseni boyunca sıralı yüz konumları (cm). Aralıklar mesnetle başlar ve mesnet/açıklık sırayla gelir;
        /// <paramref name="basSerbest"/> ise ilk nokta konsol ucudur (ilk aralık açıklık). Son aralık açıklıksa son uç serbesttir.
        /// </summary>
        internal static KirisPlanOkuma Araliklardan(IList<double> t, bool basSerbest, out string hata)
        {
            hata = null;
            if (t == null || t.Count < 2) { hata = "En az iki nokta gerekli."; return null; }
            for (int i = 1; i < t.Count; i++)
                if (!(t[i] - t[i - 1] > 0.5)) { hata = "Noktalar kiriş boyunca tek yönde ilerlemeli."; return null; }
            var o = new KirisPlanOkuma();
            if (basSerbest) o.Mesnetler.Add(new KirisManuelMesnet { Tip = KirisManuelMesnetTip.Serbest, EniCm = 0 });
            bool mesnet = !basSerbest;
            for (int i = 0; i + 1 < t.Count; i++)
            {
                double l = Math.Round(t[i + 1] - t[i], 1);
                if (mesnet) o.Mesnetler.Add(new KirisManuelMesnet { Tip = KirisManuelMesnetTip.Kolon, EniCm = l });
                else o.Acikliklar.Add(new KirisPlanAciklik { LnCm = l });
                mesnet = !mesnet;
            }
            if (mesnet) o.Mesnetler.Add(new KirisManuelMesnet { Tip = KirisManuelMesnetTip.Serbest, EniCm = 0 }); // son aralık açıklık → konsol ucu
            int n = o.Acikliklar.Count;
            if (n == 0) { hata = "En az bir açıklık seçilmedi (mesnet yüzü → açıklık sonu)."; return null; }
            if (o.Mesnetler.Count != n + 1) { hata = "Mesnet/açıklık sırası tamamlanmadı."; return null; }
            if (o.Mesnetler[0].Tip == KirisManuelMesnetTip.Serbest && o.Mesnetler[n].Tip == KirisManuelMesnetTip.Serbest)
            { hata = "İki uç birden serbest olamaz."; return null; }
            if (n > 30) { hata = "En çok 30 açıklık."; return null; }
            return o;
        }

        // "MC-01 (25/55)", "K101 (30x60)", "K-12 25/50", "K101(25*50)"
        private static readonly Regex EtiketRx = new Regex(
            @"^\s*([^\s(]{1,20})\s*(?:\(\s*(\d{1,3}(?:[.,]\d+)?)\s*[/xX×*]\s*(\d{1,3}(?:[.,]\d+)?)\s*\)|(\d{1,3}(?:[.,]\d+)?)\s*[/xX×*]\s*(\d{1,3}(?:[.,]\d+)?))\s*$",
            RegexOptions.CultureInvariant);

        /// <summary>Kiriş etiketinden ad ve b/h (cm). Ad en az bir harf içermeli.</summary>
        internal static bool TryEtiket(string metin, out string ad, out double b, out double h)
        {
            ad = null; b = h = double.NaN;
            if (string.IsNullOrWhiteSpace(metin)) return false;
            var m = EtiketRx.Match(metin.Replace("\\P", " ").Trim());
            if (!m.Success) return false;
            string ad0 = m.Groups[1].Value;
            bool harf = false;
            foreach (char c in ad0) if (char.IsLetter(c)) { harf = true; break; }
            if (!harf) return false;
            string sb = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[4].Value;
            string sh = m.Groups[2].Success ? m.Groups[3].Value : m.Groups[5].Value;
            if (!double.TryParse(sb.Replace(',', '.'), NumberStyles.Float, Inv, out b) || !double.TryParse(sh.Replace(',', '.'), NumberStyles.Float, Inv, out h)) return false;
            if (b < 5 || b > 300 || h < 5 || h > 400) return false;
            ad = ad0;
            return true;
        }

        private static string SonrakiAd(string ad)
        {
            var m = Regex.Match(ad ?? "", @"^(.*?)(\d+)$");
            if (!m.Success) return (ad ?? "K") + "2";
            string sayi = (int.Parse(m.Groups[2].Value, Inv) + 1).ToString(Inv).PadLeft(m.Groups[2].Value.Length, '0');
            return m.Groups[1].Value + sayi;
        }

        /// <summary>
        /// Okunanı mevcut form verisine yazar: mesnetler tamamen plandan; açıklıkta Ln (ve bulunduysa ad, b, h) plandan,
        /// donatı/etriye/döşeme/Δkot mevcut satırdan (fazla açıklıkta son satırın kopyası). Genel ayarlar korunur.
        /// </summary>
        internal static KirisManuelVeri Uygula(KirisManuelVeri mevcut, KirisPlanOkuma o)
        {
            mevcut = mevcut ?? KirisManuelVeri.Varsayilan();
            var v = new KirisManuelVeri
            {
                PaspayiCm = mevcut.PaspayiCm, KatKotuCm = mevcut.KatKotuCm, Fck = mevcut.Fck, Fyk = mevcut.Fyk,
                SonKat = mevcut.SonKat, IkinciGonyeKaldir = mevcut.IkinciGonyeKaldir, TekEtriye = mevcut.TekEtriye
            };
            foreach (var m in o.Mesnetler) v.Mesnetler.Add(new KirisManuelMesnet { Tip = m.Tip, EniCm = m.Tip == KirisManuelMesnetTip.Serbest ? 50 : m.EniCm });
            var adlar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            KirisManuelAciklik onceki = null;
            for (int i = 0; i < o.Acikliklar.Count; i++)
            {
                var p = o.Acikliklar[i];
                KirisManuelAciklik a;
                if (i < mevcut.Acikliklar.Count) a = mevcut.Acikliklar[i].Kopya();
                else
                {
                    a = (onceki ?? (mevcut.Acikliklar.Count > 0 ? mevcut.Acikliklar[mevcut.Acikliklar.Count - 1] : new KirisManuelAciklik())).Kopya();
                    a.Ad = SonrakiAd(a.Ad);
                }
                a.LnCm = p.LnCm;
                if (!string.IsNullOrWhiteSpace(p.Ad)) a.Ad = p.Ad.Trim();
                if (!double.IsNaN(p.BCm)) a.BCm = p.BCm;
                if (!double.IsNaN(p.HCm)) a.HCm = p.HCm;
                if (double.IsNaN(a.BCm)) a.BCm = 30;
                if (double.IsNaN(a.HCm)) a.HCm = 60;
                if (!double.IsNaN(a.DosemeCm) && a.DosemeCm > a.HCm) a.DosemeCm = a.HCm;
                if (string.IsNullOrWhiteSpace(a.Ad)) a.Ad = "K" + (i + 1).ToString("00", Inv);
                for (int g = 0; g < 100 && !adlar.Add(a.Ad); g++) a.Ad = SonrakiAd(a.Ad);
                v.Acikliklar.Add(a);
                onceki = a;
            }
            return v;
        }

        /// <summary>Komut satırı özeti: "Kolon 30 | MC-01 25/55 Ln=425 | Kiriş 30 | …".</summary>
        internal string Ozet()
        {
            var sb = new StringBuilder();
            for (int k = 0; k < Mesnetler.Count; k++)
            {
                var m = Mesnetler[k];
                sb.Append(m.Tip == KirisManuelMesnetTip.Serbest ? "Serbest uç" : (m.Tip == KirisManuelMesnetTip.Kolon ? "Kolon " : "Kiriş ") + m.EniCm.ToString("0.#", Inv));
                if (k >= Acikliklar.Count) break;
                var a = Acikliklar[k];
                sb.Append(" | ").Append(string.IsNullOrEmpty(a.Ad) ? "(ad yok)" : a.Ad);
                if (!double.IsNaN(a.BCm)) sb.Append(' ').Append(a.BCm.ToString("0.#", Inv)).Append('/').Append(a.HCm.ToString("0.#", Inv));
                sb.Append(" Ln=").Append(a.LnCm.ToString("0.#", Inv)).Append(" | ");
            }
            return sb.ToString();
        }
    }
}
