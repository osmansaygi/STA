using System;
using System.Drawing;
using System.Globalization;

namespace Beykent.Monolit.Girdi
{
    /// <summary>Girdi bileşenlerinin sabit renkleri (Standart A açık form ile uyumlu).</summary>
    internal static class GirdiTema
    {
        internal static readonly Color Zemin = Color.White;
        internal static readonly Color Cerceve = Color.FromArgb(171, 173, 179);
        internal static readonly Color Odak = Color.FromArgb(0, 120, 215);
        internal static readonly Color Hata = Color.FromArgb(196, 43, 28);
        internal static readonly Color HataZemin = Color.FromArgb(253, 236, 234);
        internal static readonly Color Uyari = Color.FromArgb(138, 90, 0);
        internal static readonly Color UyariZemin = Color.FromArgb(255, 244, 206);
        internal static readonly Color Tamam = Color.FromArgb(16, 124, 16);
        internal static readonly Color Soluk = Color.FromArgb(109, 109, 109);
        internal static readonly Color Secili = Color.FromArgb(204, 228, 247);
        internal static readonly Color SeciliCerceve = Color.FromArgb(0, 120, 215);

        internal static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

        /// <summary>Virgül veya nokta ondalık ayırıcı kabul eder; binlik ayırıcı kabul etmez.</summary>
        internal static bool SayiOku(string text, out double value)
        {
            value = 0;
            if (text == null) return false;
            string t = text.Trim().Replace(" ", "").Replace(',', '.');
            if (t.Length == 0 || t.IndexOf('.') != t.LastIndexOf('.')) return false;
            return double.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        internal static string Yaz(double value, int ondalik)
        {
            double r = Math.Round(value, ondalik, MidpointRounding.AwayFromZero);
            return r.ToString(ondalik > 0 ? "0." + new string('#', ondalik) : "0", Tr);
        }
    }
}
