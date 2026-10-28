using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Autodesk.AutoCAD.EditorInput;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// Kalıp planı çiziminin ilerleme durumu (oran, aşama) ve sonda süre mesajı. Pencereyi <see cref="CizimIzleyici"/> gösterir
    /// (çizim 5 sn'yi aşınca); bu sınıf yalnız durumu yazar. Bir komut içinde tek kapsam; iç çağrılar (plan, DENEME2 donatısı)
    /// <see cref="Bolum"/> ile kendi aralığına yazar. Yazan ana iş parçacığı, okuyan izleyici iş parçacığıdır.
    /// </summary>
    internal sealed class KalipIlerleme : IDisposable
    {
        private static volatile KalipIlerleme _aktif;

        private readonly string _baslik;
        private readonly Editor _ed;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private double _a, _b = 1.0;
        private volatile string _asama = string.Empty;
        private long _oranBinde;

        private KalipIlerleme(string baslik, Editor ed)
        {
            _baslik = baslik;
            _ed = ed;
        }

        /// <summary>Kapsam yoksa başlatır (sahibi döner, Dispose ile kapatılır); varsa null döner, çağıran mevcut kapsama yazar.</summary>
        internal static KalipIlerleme BaslatYoksa(string baslik, Editor ed)
        {
            if (_aktif != null) return null;
            var k = new KalipIlerleme(string.IsNullOrWhiteSpace(baslik) ? "KALIP" : baslik.Trim(), ed);
            _aktif = k;
            CizimIzleyici.KalipBasladi(k._baslik);
            return k;
        }

        /// <summary>Sonraki <see cref="Bildir"/> oranları [a, b] aralığına eşlenir (ör. DENEME2: plan 0–0,55, donatı 0,55–1).</summary>
        internal static void Bolum(double a, double b)
        {
            KalipIlerleme k = _aktif;
            if (k == null) return;
            k._a = Math.Max(0.0, Math.Min(1.0, a));
            k._b = Math.Max(k._a, Math.Min(1.0, b));
        }

        /// <summary>Etkin bölümde ilerleme (0–1) ve aşama adı. Kapsam yoksa bir şey yapmaz.</summary>
        internal static void Bildir(double oran, string asama)
        {
            KalipIlerleme k = _aktif;
            if (k == null) return;
            double o = double.IsNaN(oran) ? 0.0 : Math.Max(0.0, Math.Min(1.0, oran));
            long binde = (long)Math.Round((k._a + (k._b - k._a) * o) * 1000.0);
            if (binde > Interlocked.Read(ref k._oranBinde)) Interlocked.Exchange(ref k._oranBinde, binde); // geri gitmesin
            k._asama = asama ?? string.Empty;
        }

        /// <summary>İzleyici iş parçacığı için: etkin kapsam varsa oran (0–1), başlık ve aşama.</summary>
        internal static bool TryDurum(out double oran, out string baslik, out string asama)
        {
            KalipIlerleme k = _aktif;
            oran = 0;
            baslik = asama = null;
            if (k == null) return false;
            oran = Interlocked.Read(ref k._oranBinde) / 1000.0;
            baslik = k._baslik;
            asama = k._asama;
            return true;
        }

        public void Dispose()
        {
            if (ReferenceEquals(_aktif, this)) _aktif = null;
            CizimIzleyici.KalipBitti();
            try
            {
                _ed?.WriteMessage(string.Format(CultureInfo.InvariantCulture, "\n{0}: cizim suresi {1:0.0} sn.", _baslik, _sw.Elapsed.TotalSeconds));
            }
            catch { }
        }
    }
}
