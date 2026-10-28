using System;

namespace ST4Yardimci
{
    /// <summary>
    /// Plan ID'ye bağlanan Yardımcı DosemeDonatiRunner için köprü (DENEME2): form ve donatı boyu komutu Plan ID'ye taşınmaz.
    /// Katman listeleri Yardımcı ile aynı %LocalAppData%\ST4_Yardimci\doseme_donati.ini dosyasından okunur.
    /// </summary>
    internal static class DosemeDonatiForm
    {
        public static void ShowAfterPick() { }
    }

    /// <summary>Yardımcı DonatiBoyuForm.cs ile aynı değerler.</summary>
    internal enum DonatiBoyuYuvarlama
    {
        Yok = 0,
        BirCm = 1,
        BesCm = 5,
        OnCm = 10
    }

    /// <summary>Yardımcı DonatiBoyuRunner.RoundLen ile birebir (yalnız boy yuvarlama).</summary>
    internal static class DonatiBoyuRunner
    {
        internal static double RoundLen(double cm, DonatiBoyuYuvarlama y)
        {
            if (cm < 0) cm = 0;
            if (y != DonatiBoyuYuvarlama.BesCm)
                return Math.Round(cm, MidpointRounding.AwayFromZero);

            double floor5 = Math.Floor(cm / 5.0) * 5.0;
            double rem = cm - floor5;
            if (rem <= 1.0 + 1e-6)
                return floor5;
            return floor5 + 5.0;
        }
    }
}
