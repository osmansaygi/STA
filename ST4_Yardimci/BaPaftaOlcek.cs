using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// 1/25 detay komutlarının pafta ölçeği (1/50 veya 1/100) — %LocalAppData%\ST4_Yardimci\pafta_olcek.ini, komut başına.
    /// 1/100 seçiliyse komutun çizdiği detay (yazı/ölçü dahil) ×2 büyütülür.
    /// </summary>
    internal static class BaPaftaOlcek
    {
        private const string Ini = "pafta_olcek.ini";

        internal static bool Yuz(string anahtar) => BaIni.GetB(BaIni.Read(Ini), anahtar, false);

        internal static void Kaydet(string anahtar, bool yuz)
        {
            var map = BaIni.Read(Ini);
            map[anahtar] = BaIni.B(yuz);
            BaIni.Write(Ini, map.Select(kv => kv.Key + "=" + kv.Value).ToArray());
        }

        /// <summary>Form seçeneği; değişince hemen kaydedilir (palet formları açık kalır).</summary>
        internal static RadioButton[] Secenekler(string anahtar)
        {
            RadioButton[] rb = BeykentOrtak.PaftaOlcek.Secenekler(Yuz(anahtar));
            rb[1].CheckedChanged += (s, e) => Kaydet(anahtar, rb[1].Checked);
            return rb;
        }

        /// <summary>Komut sonunda: 1/100 seçiliyse <paramref name="ilkHandle"/> sonrası çizilenleri köşesi sabit ×2 büyütür.</summary>
        internal static void Uygula(string anahtar, Document doc, long ilkHandle, bool ustKose)
        {
            if (doc == null || !Yuz(anahtar)) return;
            try
            {
                using (doc.LockDocument())
                {
                    int n = BeykentOrtak.PaftaOlcek.BuyutYeniKose(doc.Database, ilkHandle, ustKose);
                    if (n > 0) doc.Editor.WriteMessage("\n1/100 pafta: detay ×2 büyütüldü ({0} nesne).", n);
                }
            }
            catch (System.Exception ex) { doc.Editor.WriteMessage("\n1/100 pafta büyütme yapılamadı: {0}", ex.Message); }
        }

        internal static long Baslangic() => AcApp.DocumentManager.MdiActiveDocument?.Database.Handseed.Value ?? long.MaxValue;
    }
}
