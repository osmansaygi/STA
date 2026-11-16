using System;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        /// <summary>
        /// Kapama perdesinde iki uç kotu (Beams Data p[11]/p[12]) farklı olabilir: üst kenar 1. noktadan (StartAxis)
        /// 2. noktaya doğrusal eğimli, alt kenar düz. ST4'te h alçak uçtaki perde yüksekliğidir (alt = alçak uç − h).
        /// Eğimsiz perde ve kirişlerde eski davranış (üst = büyük kot, alt = üst − h) aynen kalır.
        /// </summary>
        private static bool IsEgimliPerde(BeamInfo beam) =>
            beam != null && beam.IsWallFlag == 1 && Math.Abs(beam.Point1KotCm - beam.Point2KotCm) > 0.5;

        /// <summary>Kat kotuna göre alt kot (cm): eğimli perdede alçak uç − h, diğerlerinde büyük kot − h.</summary>
        private static double PerdeAltKotRelCm(BeamInfo beam, double h)
        {
            double k = IsEgimliPerde(beam)
                ? Math.Min(beam.Point1KotCm, beam.Point2KotCm)
                : Math.Max(beam.Point1KotCm, beam.Point2KotCm);
            return k - h;
        }

        private bool TryPerdeUcNoktalari(BeamInfo beam, out Point2d p1, out Point2d p2)
        {
            p1 = p2 = default;
            return beam != null && _axisService != null
                && _axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out p1)
                && _axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out p2)
                && p1.GetDistanceTo(p2) > 1.0;
        }

        /// <summary>Plan noktasındaki üst kot (kat kotuna göre, cm); eğimsizde büyük kot.</summary>
        private double PerdeUstKotRelCmAt(BeamInfo beam, Point2d p)
        {
            double kMax = Math.Max(beam.Point1KotCm, beam.Point2KotCm);
            if (!IsEgimliPerde(beam) || !TryPerdeUcNoktalari(beam, out Point2d p1, out Point2d p2)) return kMax;
            Vector2d d = p2 - p1;
            double t = ((p.X - p1.X) * d.X + (p.Y - p1.Y) * d.Y) / d.DotProduct(d);
            t = Math.Max(0.0, Math.Min(1.0, t));
            return beam.Point1KotCm + (beam.Point2KotCm - beam.Point1KotCm) * t;
        }

        /// <summary>
        /// Görünüş/kesit yatay ekseni u boyunca mutlak üst kot fonksiyonu; eğimsiz perdede veya perde bakışa dik
        /// (iki uç aynı u) ise null — çağıran sabit üst kotu kullanır.
        /// </summary>
        private Func<double, double> EgimliPerdeUstKotU(BeamInfo beam, double floorLevelCm, Func<double, double, double> uOf)
        {
            if (!IsEgimliPerde(beam) || uOf == null || !TryPerdeUcNoktalari(beam, out Point2d p1, out Point2d p2)) return null;
            double u1 = uOf(p1.X, p1.Y), u2 = uOf(p2.X, p2.Y);
            if (Math.Abs(u2 - u1) < 1.0) return null;
            double k1 = beam.Point1KotCm, k2 = beam.Point2KotCm;
            return u =>
            {
                double t = Math.Max(0.0, Math.Min(1.0, (u - u1) / (u2 - u1)));
                return floorLevelCm + k1 + (k2 - k1) * t;
            };
        }

        /// <summary>Kapama/perde görünüşü: model noktası → (öteleme, döndürme, yerleşim) → görünüş x.</summary>
        private static double PerdeGorunusX(Point2d p, double ox, double oy, AffineTransformation rot, AffineTransformation trf)
        {
            var c = new Coordinate(p.X + ox, p.Y + oy);
            var r = new Coordinate();
            rot.Transform(c, r);
            var v = new Coordinate();
            trf.Transform(r, v);
            return v.X;
        }

        /// <summary>u0..u1 aralığında alt kenarı z0, üst kenarı zTop(u) olan dörtgen halka (zTop null ise dikdörtgen z1).</summary>
        private static Coordinate[] EgimliUstHalka(double u0, double u1, double z0, double z1, Func<double, double> zTop, Func<double, double> zBot = null)
        {
            if (zBot != null && zTop != null)
            {
                // Eğimli kiriş görünüşü: alt ve üst kenar kendi eğimiyle.
                double b0 = zBot(u0), b1 = zBot(u1);
                double e0 = Math.Max(b0 + 0.5, zTop(u0)), e1 = Math.Max(b1 + 0.5, zTop(u1));
                return new[] { new Coordinate(u0, b0), new Coordinate(u1, b1), new Coordinate(u1, e1), new Coordinate(u0, e0), new Coordinate(u0, b0) };
            }
            double t0 = zTop != null ? Math.Max(z0, Math.Min(z1, zTop(u0))) : z1;
            double t1 = zTop != null ? Math.Max(z0, Math.Min(z1, zTop(u1))) : z1;
            return new[]
            {
                new Coordinate(u0, z0),
                new Coordinate(u1, z0),
                new Coordinate(u1, t1),
                new Coordinate(u0, t0),
                new Coordinate(u0, z0)
            };
        }
    }
}
