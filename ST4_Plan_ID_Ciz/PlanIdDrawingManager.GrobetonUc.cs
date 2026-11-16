using System;
using NetTopologySuite.Geometries;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        /// <summary>Son çizilen temel kesiti grobetonunun uç kotları (drenaj/ampatman detay çapası). A-A: A başı/sonu ucundaki alt Y;
        /// B-B: alt/üst uçtaki dış X. NaN: hesaplanamadı (çağıran zarf değerini kullanır).</summary>
        private double _grobetonUc0 = double.NaN, _grobetonUc1 = double.NaN;

        /// <summary>
        /// Grobeton birleşiminin iki ucundaki yerel dış kenar: yatay kesitte en sol/en sağ X'teki en alt Y; düşey kesitte
        /// en alt/en üst Y'deki dış X (aynalı kesitte +X dışa). Lokal kalınlaşan/derin radye kesitin ortasındaysa uç detayı
        /// en derin grobetona değil, uçtaki temelin grobetonuna oturur.
        /// </summary>
        private static void GrobetonUcKotlari(Geometry merged, bool horizontalAlongX, bool mirrorElevationX, out double uc0, out double uc1)
        {
            uc0 = uc1 = double.NaN;
            var env = merged?.EnvelopeInternal;
            if (env == null || env.IsNull) return;
            const double eps = 0.5;
            foreach (var c in merged.Coordinates)
            {
                if (horizontalAlongX)
                {
                    if (c.X <= env.MinX + eps) uc0 = double.IsNaN(uc0) ? c.Y : Math.Min(uc0, c.Y);
                    if (c.X >= env.MaxX - eps) uc1 = double.IsNaN(uc1) ? c.Y : Math.Min(uc1, c.Y);
                }
                else
                {
                    if (c.Y <= env.MinY + eps) uc0 = double.IsNaN(uc0) ? c.X : (mirrorElevationX ? Math.Max(uc0, c.X) : Math.Min(uc0, c.X));
                    if (c.Y >= env.MaxY - eps) uc1 = double.IsNaN(uc1) ? c.X : (mirrorElevationX ? Math.Max(uc1, c.X) : Math.Min(uc1, c.X));
                }
            }
        }
    }
}
