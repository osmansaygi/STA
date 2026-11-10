using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// TBDY 2018 §7.3.3.2 kolon/perde düşey donatısı finişi (en üst kat veya üstte devam etmeyen çubuk).
    /// Kolon başına TEK TİP (kullanıcı kuralı 09.11.2026):
    /// L = max(1,5·ℓb ; 40φ). Kolona herhangi bir yüzden kiriş saplanıyorsa a = bükülme kotu − yüksekliği
    /// EN DÜŞÜK kirişin alt yüzü (emniyet); kiriş yoksa a = 0.
    /// Yatay kol (gönye) her zaman kesitin kısa boyutu − 2·pas.
    /// Kiriş varsa ve a + b ≥ L → gönye; kapama perdesine bağlı (kiriş yok) → gönye;
    /// aksi halde firkete c = max(12φ ; L − a − b).
    /// Toplam boy 5 cm katı: firketede c büyütülür; gönyede b paspayını korumak için kısaltılır
    /// (gereken boy sağlanmazsa büyütülür).
    /// </summary>
    public sealed partial class PlanIdDrawingManager
    {
        private const double KdFinisPasCm = 4.0;

        private sealed class KdFinisCtx
        {
            public Envelope E;
            public bool Perde;
            public double ZTop;
            public double Lreq;
            /// <summary>Kat üstünde kolona saplanan kirişlerden yüksekliği en düşük olanın alt kotu.</summary>
            public double ZSofEnDusuk = double.NaN;
            public double HEnDusuk = double.MaxValue;
            public bool KapamaPerde;
        }

        /// <summary>Finiş biçimi (açılım/görünüş).</summary>
        private struct KdFinis
        {
            public bool Firkete;
            public double B;
            public double C;
        }

        /// <summary>Kat üstündeki kiriş/kapama perdesi bağlantıları (kolon kesitine değenler).</summary>
        private KdFinisCtx BuildKolonFinisCtx(
            ColumnAxisInfo col, int floorIndex, Geometry storyPoly, AffineTransformation rot,
            double zTop, int diaMm, bool perde)
        {
            var ctx = new KdFinisCtx { ZTop = zTop, Perde = perde };
            Geometry g;
            try { g = rot != null ? rot.Transform(storyPoly) : storyPoly; }
            catch { g = storyPoly; }
            if (g == null || g.IsEmpty) return null;
            ctx.E = g.EnvelopeInternal;
            double fck = _rebarFckMPa > 16.0 ? _rebarFckMPa : 30.0;
            double fyk = _rebarFykMPa > 200.0 ? _rebarFykMPa : 420.0;
            double lb = Ts500KenetlenmeLbCm(diaMm, fck, fyk);
            ctx.Lreq = CeilTo5Cm(Math.Max(1.50 * lb, 40.0 * diaMm / 10.0));

            var floor = FloorAtIndex(floorIndex);
            if (floor == null || col == null) return ctx;
            Geometry full = TryGetKolonFloorPolygon(floorIndex, KdCol(col, floorIndex)) ?? storyPoly;
            if (full == null || full.IsEmpty) return ctx;
            try
            {
                ForEachKolonBagliKiris(floor, full, includeWalls: false, (beam, poly, zb, zt) =>
                {
                    if (poly == null || poly.IsEmpty) return;
                    // Yalnız kat üstünde kolona saplanan kiriş.
                    if (zt < zTop - 10.0 || zb > zTop - 1.0) return;
                    double h = zt - zb;
                    if (h < ctx.HEnDusuk - 0.01 || (Math.Abs(h - ctx.HEnDusuk) < 0.01 && zb > ctx.ZSofEnDusuk))
                    {
                        ctx.HEnDusuk = h;
                        ctx.ZSofEnDusuk = zb;
                    }
                });
                ForEachKolonBagliKiris(floor, full, includeWalls: true, (beam, poly, zb, zt) =>
                {
                    if (poly == null || poly.IsEmpty) return;
                    if (PolygonsMostlyOverlap(poly, full)) return;
                    if (zt < zTop - 10.0 || zb > zTop - 1.0) return;
                    ctx.KapamaPerde = true;
                });
            }
            catch { }
            return ctx;
        }

        /// <summary>
        /// Tek tip finiş. sCm: kanca dışındaki çubuk boyu (düşey + varsa alt kanca).
        /// kisaYedek: kesit kısa boyutu yerine kullanılacak kalınlık (poligon kolu); NaN ise zarfın kısa kenarı.
        /// </summary>
        private static KdFinis KdFinisTekTip(KdFinisCtx c, int diaMm, double zHoriz, double sCm, double kisaYedek)
        {
            double phi = diaMm / 10.0;
            double h12 = Math.Ceiling(12.0 * phi - 1e-9);
            double kisa = IsFiniteCoord(kisaYedek) ? kisaYedek : Math.Min(c.E.Width, c.E.Height);
            double b0 = Math.Max(h12, Math.Floor(kisa - 2.0 * KdFinisPasCm + 1e-9));
            bool kiris = IsFiniteCoord(c.ZSofEnDusuk);
            double a = kiris ? Math.Max(0.0, zHoriz - c.ZSofEnDusuk) : 0.0;
            double sR = Math.Round(sCm);
            var f = new KdFinis();
            bool gonye = (kiris && a + b0 >= c.Lreq - 1e-6) || (!kiris && c.KapamaPerde);
            if (gonye)
            {
                double gerek = kiris ? Math.Max(h12, c.Lreq - a) : h12;
                double tot = sR + b0;
                double mod = tot - Math.Floor(tot / 5.0 + 1e-9) * 5.0;
                double b = b0;
                if (mod > 1e-6)
                {
                    // Önce kısalt (paspayı korunur); gereken boyun altına düşerse 5'e tamamla.
                    if (b0 - mod >= gerek - 1e-6) b = b0 - mod;
                    else b = b0 + (5.0 - mod);
                }
                f.Firkete = false;
                f.B = b;
                f.C = 0.0;
            }
            else
            {
                double cc = Math.Max(h12, Math.Ceiling(c.Lreq - a - b0 - 1e-9));
                double tot = sR + b0 + cc;
                cc += CeilTo5Cm(tot) - tot;
                f.Firkete = true;
                f.B = b0;
                f.C = cc;
            }
            return f;
        }

        /// <summary>Görünüş çubuğu için finiş (kolon başına tek tip).</summary>
        private KdFinis KdFinisCubuk(KdFinisCtx c, int diaMm, double zHoriz, double x, double y, double sCm)
        {
            return KdFinisTekTip(c, diaMm, zHoriz, sCm, double.NaN);
        }

        /// <summary>Açılım: kolon başına tek tip, tek grup (n adet).</summary>
        private List<(int n, KdFinis f)> KdFinisGruplari(
            KdFinisCtx c, int diaMm, int n, double zHoriz, double sCm,
            List<(Point2d p, int dia)> pts, Func<Point2d, bool> filtre = null, double bendDimYedek = double.NaN)
        {
            var res = new List<(int n, KdFinis f)>();
            if (n <= 0 || c == null || c.E == null) return res;
            res.Add((n, KdFinisTekTip(c, diaMm, zHoriz, sCm, bendDimYedek)));
            return res;
        }
    }
}
