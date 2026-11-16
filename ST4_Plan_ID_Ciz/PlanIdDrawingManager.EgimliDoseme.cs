using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        /// <summary>
        /// Eğimli döşeme (merdiven değil): ST4 Floors Data p[14] eğim kodu, p[15] alçak kenar, p[5] yüksek kenar kotu (kattan, cm).
        /// Kod 1..4 alçak kenar +Y/−Y/−X/+X; kod ≥10 alçak yön açısı (kod−10)/10°. Döşeme aks dörtgeninin bu yöndeki
        /// en uç köşeleri alçak/yüksek kenardır, kot aradaki doğrultuda doğrusal değişir (tg_01, KAY_09, ASLIM, e_ktk kiriş uç kotlarıyla doğrulandı).
        /// Eğimsiz döşemede false — çağıran eski düz kotu kullanır.
        /// </summary>
        private bool TryGetEgimliDosemeKot(SlabInfo slab, out Func<Point2d, double> kotRelCm, out double cosAlpha)
        {
            kotRelCm = null;
            cosAlpha = 1.0;
            if (slab == null || slab.SlopeCode <= 0) return false;
            if (slab.StairKind == 1 || slab.StairKind == 3) return false;
            if (_model.StairSlabIds.Contains(slab.SlabId)) return false;
            double lo = slab.OffsetFromFloorCm, hi = slab.SlopeHighKotCm;
            if (Math.Abs(hi - lo) <= 0.5) return false;
            Vector2d d;
            switch (slab.SlopeCode)
            {
                case 1: d = new Vector2d(0, 1); break;
                case 2: d = new Vector2d(0, -1); break;
                case 3: d = new Vector2d(-1, 0); break;
                case 4: d = new Vector2d(1, 0); break;
                default:
                    if (slab.SlopeCode < 10) return false;
                    double ang = (slab.SlopeCode - 10) / 10.0 * Math.PI / 180.0;
                    d = new Vector2d(Math.Cos(ang), Math.Sin(ang));
                    break;
            }
            var poly = SlabFootprintPoly(slab);
            if (poly == null || poly.IsEmpty) return false;
            double sMin = double.MaxValue, sMax = double.MinValue;
            foreach (var c in poly.Coordinates)
            {
                double s = c.X * d.X + c.Y * d.Y;
                sMin = Math.Min(sMin, s);
                sMax = Math.Max(sMax, s);
            }
            double run = sMax - sMin;
            if (run < 1.0) return false;
            double g = Math.Abs(hi - lo) / run;
            cosAlpha = 1.0 / Math.Sqrt(1.0 + g * g);
            kotRelCm = p =>
            {
                double t = ((p.X * d.X + p.Y * d.Y) - sMin) / run;
                t = Math.Max(0.0, Math.Min(1.0, t));
                return hi + (lo - hi) * t;
            };
            return true;
        }

        /// <summary>İki uç kotu farklı (eğimli) kiriş; perde (kapama) <see cref="IsEgimliPerde"/> ile ayrı ele alınır.</summary>
        private static bool IsEgimliKiris(BeamInfo beam) =>
            beam != null && beam.IsWallFlag != 1 && Math.Abs(beam.Point1KotCm - beam.Point2KotCm) > 0.5;

        /// <summary>Eğimli kirişin plan noktasındaki üst kotu (kattan, cm) ve eğim kosinüsü (düşey yükseklik h/cos α).</summary>
        private bool TryGetEgimliKirisKot(BeamInfo beam, out Func<Point2d, double> kotRelCm, out double cosAlpha)
        {
            kotRelCm = null;
            cosAlpha = 1.0;
            if (!IsEgimliKiris(beam) || !TryPerdeUcNoktalari(beam, out Point2d p1, out Point2d p2)) return false;
            Vector2d v = p2 - p1;
            double L2 = v.DotProduct(v);
            double dk = beam.Point2KotCm - beam.Point1KotCm;
            cosAlpha = Math.Sqrt(L2) / Math.Sqrt(L2 + dk * dk);
            double k1 = beam.Point1KotCm;
            kotRelCm = p =>
            {
                double t = ((p.X - p1.X) * v.X + (p.Y - p1.Y) * v.Y) / L2;
                t = Math.Max(0.0, Math.Min(1.0, t));
                return k1 + dk * t;
            };
            return true;
        }

        /// <summary>
        /// Kesit/görünüşte arka planda görünen eğimli kiriş: yatay eksen u boyunca mutlak üst kot (mesnet kotları arası
        /// doğrusal) ve alt kot (üst − h/cos α). Kiriş bakışa dikse (iki uç aynı u) düz zarf: en alt uç − h/cos α … en üst uç.
        /// Eğimsiz kirişte false — çağıran eski dikdörtgeni kullanır.
        /// </summary>
        private bool TryEgimliKirisGorunusZ(BeamInfo beam, double floorLevelCm, double h, Func<double, double, double> uOf,
            out Func<double, double> zTop, out Func<double, double> zBot, out double z0, out double z1)
        {
            zTop = zBot = null;
            z0 = z1 = 0.0;
            if (uOf == null || !TryGetEgimliKirisKot(beam, out _, out double cosA) || !TryPerdeUcNoktalari(beam, out Point2d p1, out Point2d p2))
                return false;
            double k1 = floorLevelCm + beam.Point1KotCm, k2 = floorLevelCm + beam.Point2KotCm;
            double hv = h / cosA;
            z0 = Math.Min(k1, k2) - hv;
            z1 = Math.Max(k1, k2);
            double u1 = uOf(p1.X, p1.Y), u2 = uOf(p2.X, p2.Y);
            if (Math.Abs(u2 - u1) < 1.0) return true;
            Func<double, double> top = u => k1 + (k2 - k1) * Math.Max(0.0, Math.Min(1.0, (u - u1) / (u2 - u1)));
            zTop = top;
            zBot = u => top(u) - hv;
            return true;
        }

        /// <summary>
        /// Görünüş/arka plan için eğimli döşeme: plan noktasındaki mutlak üst kot fonksiyonu ve düşey kalınlık (t/cos α);
        /// z0/z1 zarf. Eğimsiz döşemede false — çağıran eski dikdörtgeni kullanır.
        /// </summary>
        private bool TryEgimliDosemeGorunusZ(SlabInfo slab, double floorLevelCm, out Func<double, double, double> zTopAt,
            out double tv, out double z0, out double z1)
        {
            zTopAt = null; tv = 0.0; z0 = z1 = 0.0;
            if (!TryGetEgimliDosemeKot(slab, out var kot, out double cosA)) return false;
            double th = slab.ThicknessCm > 0 ? slab.ThicknessCm : 15.0;
            tv = th / cosA;
            double lo = slab.OffsetFromFloorCm, hi = slab.SlopeHighKotCm;
            z0 = floorLevelCm + Math.Min(lo, hi) - tv;
            z1 = floorLevelCm + Math.Max(lo, hi);
            zTopAt = (x, y) => floorLevelCm + kot(new Point2d(x, y));
            return true;
        }

        /// <summary>
        /// Eğimli döşemenin görünüş düzlemine izdüşümü: plan köşelerinin üst ve alt (üst − tv) noktalarının (u, z)
        /// dışbükey zarfı (düzlemsel levha). [uMin, uMax] dışı kırpılır.
        /// </summary>
        private void AddEgimliDosemeGorunusFace(System.Collections.Generic.List<BinaGorunusFace> faces, Geometry planPoly,
            Func<double, double, double> zTopAt, double tv, Func<double, double, double> uOf, double depth, double uMin, double uMax)
        {
            var coords = planPoly?.Coordinates;
            if (coords == null || coords.Length == 0) return;
            var pts = new System.Collections.Generic.List<Coordinate>();
            foreach (var c in coords)
            {
                double u = uOf(c.X, c.Y), zt = zTopAt(c.X, c.Y);
                pts.Add(new Coordinate(u, zt));
                pts.Add(new Coordinate(u, zt - tv));
            }
            try
            {
                Geometry hull = _ntsDrawFactory.CreateMultiPointFromCoords(pts.ToArray()).ConvexHull();
                if (!double.IsInfinity(uMin) || !double.IsInfinity(uMax))
                {
                    var env = hull.EnvelopeInternal;
                    double a0 = Math.Max(uMin, env.MinX), a1 = Math.Min(uMax, env.MaxX);
                    if (a1 - a0 < 0.5) return;
                    var box = _ntsDrawFactory.ToGeometry(new Envelope(a0, a1, env.MinY - 1.0, env.MaxY + 1.0));
                    hull = hull.Intersection(box);
                }
                if (hull is Polygon hp && !hp.IsEmpty && hp.Area >= 1.0)
                    faces.Add(new BinaGorunusFace { Poly = hp, Depth = depth });
            }
            catch { /* bozuk zarf */ }
        }

        /// <summary>Kesit hattı boyunca a konumundaki (alt, üst) mutlak kot: plan noktası alongOrigin + du·a.</summary>
        private static Func<double, (double zb, double zt)> EgimliKesitZ(Func<Point2d, double> kotRelCm, double floorLevelCm,
            double verticalThicknessCm, Point2d alongOrigin, Vector2d du)
        {
            return a =>
            {
                double zt = floorLevelCm + kotRelCm(alongOrigin + du * a);
                return (zt - verticalThicknessCm, zt);
            };
        }

        /// <summary>Eğimli dilimin şema çokgeni (null: düz dilim, çağıran dikdörtgeni kullanır). map(a, z) şema koordinatı verir.</summary>
        private static Polygon CreateEgimliSliceSchematicPolygon(GeometryFactory gf, SectionSlice s, Func<double, double, Coordinate> map)
        {
            if (s == null || !s.Egimli) return null;
            if (s.UstA != null && s.AltA != null)
            {
                var pts = new System.Collections.Generic.List<Coordinate>();
                for (int i = 0; i < s.UstA.Length; i++) pts.Add(map(s.UstA[i], s.UstZ[i]));
                for (int i = s.AltA.Length - 1; i >= 0; i--) pts.Add(map(s.AltA[i], s.AltZ[i]));
                pts.Add(pts[0]);
                try
                {
                    var pg = gf.CreatePolygon(gf.CreateLinearRing(pts.ToArray()));
                    if (pg.IsValid) return pg;
                }
                catch { /* dörtgene düş */ }
            }
            var ring = gf.CreateLinearRing(new[]
            {
                map(s.A0, s.EZ0a), map(s.A1, s.EZ0b), map(s.A1, s.EZ1b), map(s.A0, s.EZ1a), map(s.A0, s.EZ0a)
            });
            return gf.CreatePolygon(ring);
        }

        /// <summary>
        /// Kesit şemasında eğimli döşeme diliminin üst yüzüne paralel eğim yazısı (%60). Aynı eğimle süren komşu
        /// döşemelerde (arada kiriş) tek yazı. Eğimli dilim yoksa hiçbir şey çizmez.
        /// </summary>
        private void DrawEgimliDosemeKesitEgimYazilari(Transaction tr, BlockTableRecord btr, System.Collections.Generic.List<SectionSlice> slices,
            ObjectId styleId, double labelH, double gap, Func<double, double, Coordinate> map)
        {
            if (slices == null) return;
            const double minBoy = 60.0, birlesmeBosluk = 80.0;
            int lastSign = 0, lastPct = -1;
            double lastEnd = double.MinValue;
            foreach (var s in System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Where(slices, x => x.Egimli && x.Layer == LayerDoseme), x => x.A0))
            {
                double run = s.A1 - s.A0;
                if (run < minBoy) continue;
                double g = (s.EZ1b - s.EZ1a) / run;
                int pct = (int)Math.Round(Math.Abs(g) * 100.0);
                int sign = Math.Sign(g);
                if (pct <= 0) continue;
                if (sign == lastSign && pct == lastPct && s.A0 - lastEnd < birlesmeBosluk)
                {
                    lastEnd = Math.Max(lastEnd, s.A1);
                    continue;
                }
                lastSign = sign; lastPct = pct; lastEnd = s.A1;
                double am = 0.5 * (s.A0 + s.A1);
                double zt = 0.5 * (s.EZ1a + s.EZ1b);
                var p0 = map(s.A0, s.EZ1a);
                var p1 = map(s.A1, s.EZ1b);
                var pm = map(am, zt);
                var pu = map(am, zt + 1.0);
                double nx = pu.X - pm.X, ny = pu.Y - pm.Y;
                double nl = Math.Sqrt(nx * nx + ny * ny);
                if (nl < 1e-9) continue;
                nx /= nl; ny /= nl;
                // Üst çizgiye dik dış normal (kot yukarı yönüyle aynı tarafta).
                double tx = p1.X - p0.X, ty = p1.Y - p0.Y, tl = Math.Sqrt(tx * tx + ty * ty);
                if (tl < 1e-9) continue;
                double ux = -ty / tl, uy = tx / tl;
                if (ux * nx + uy * ny < 0) { ux = -ux; uy = -uy; }
                nx = ux; ny = uy;
                // Yazı üst çizginin dışında, tabanı her zaman döşemeye bakar (yazının "yukarı"sı = döşemeden dışa normal).
                double rot = Math.Atan2(-nx, ny);
                var pos = new Point3d(pm.X + nx * gap, pm.Y + ny * gap, 0);
                AppendEntity(tr, btr, new DBText
                {
                    Layer = LayerDosemeIsmi,
                    Height = labelH,
                    TextStyleId = styleId,
                    TextString = "%" + pct.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Rotation = rot,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextBottom,
                    Position = pos,
                    AlignmentPoint = pos
                });
            }
        }

        /// <summary>
        /// Eğimli döşeme kalınlık ölçüsü eğime dik: üst çizgi orta noktası → alt çizgiye dik ayak. map(a, z) şema koordinatı;
        /// ölçü çizgisi üst çizgi doğrultusunda (pref yönüne yakın tarafa) off kadar kaydırılır. Eğimsiz dilimde false.
        /// </summary>
        private static bool EgimliDikKalinlikOlcu(SectionSlice s, Func<double, double, Coordinate> map, double off, double prefX, double prefY,
            out Point3d p1, out Point3d p2, out Point3d pDim)
        {
            p1 = p2 = pDim = Point3d.Origin;
            if (s == null || !s.Egimli || Math.Abs(s.EZ1b - s.EZ1a) < 0.05) return false;
            double am = 0.5 * (s.A0 + s.A1);
            var t0 = map(am, SliceUstZ(s, am));
            var t1 = map(am + 10.0, SliceUstZ(s, am + 10.0));
            var b0 = map(am, SliceAltZ(s, am));
            double tx = t1.X - t0.X, ty = t1.Y - t0.Y, tl = Math.Sqrt(tx * tx + ty * ty);
            if (tl < 1e-9) return false;
            tx /= tl; ty /= tl;
            if (tx * prefX + ty * prefY < 0) { tx = -tx; ty = -ty; }
            double nx = -ty, ny = tx;
            double d = (b0.X - t0.X) * nx + (b0.Y - t0.Y) * ny;
            if (d < 0) { nx = -nx; ny = -ny; d = -d; }
            if (d < 0.5) return false;
            p1 = new Point3d(t0.X, t0.Y, 0);
            p2 = new Point3d(t0.X + nx * d, t0.Y + ny * d, 0);
            pDim = new Point3d(0.5 * (p1.X + p2.X) + tx * off, 0.5 * (p1.Y + p2.Y) + ty * off, 0);
            return true;
        }

        /// <summary>
        /// Kesitte mahya noktaları: bitişik (miterli) iki döşemenin üst çizgisi biri yükselip diğeri alçalarak birleşiyorsa (a, z).
        /// </summary>
        private static System.Collections.Generic.List<(double a, double z)> EgimliMahyaNoktalari(System.Collections.Generic.List<SectionSlice> slices)
        {
            var res = new System.Collections.Generic.List<(double a, double z)>();
            if (slices == null) return res;
            var slabs = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(
                System.Linq.Enumerable.Where(slices, x => x.Layer == LayerDoseme && x.UstA != null), x => x.A0));
            for (int i = 0; i + 1 < slabs.Count; i++)
            {
                var l = slabs[i]; var r = slabs[i + 1];
                double la = l.UstA[l.UstA.Length - 1], lz = l.UstZ[l.UstZ.Length - 1];
                double ra = r.UstA[0], rz = r.UstZ[0];
                if (Math.Abs(la - ra) > 0.5 || Math.Abs(lz - rz) > 0.5) continue;
                DosemeDogru(l, true, out double gl, out _);
                DosemeDogru(r, true, out double gr, out _);
                if (gl > 1e-3 && gr < -1e-3) res.Add((0.5 * (la + ra), 0.5 * (lz + rz)));
            }
            return res;
        }

        /// <summary>Kesit ölçüsü için dilimin alt/üst kotu: düz dilimde Z0/Z1 (küçük/büyük), eğimli dilimde orta noktadaki
        /// alt/üst kot (zarf değil; ölçü düşey kalınlığı verir).</summary>
        private static void SliceOlcuZ(SectionSlice s, out double zb, out double zt)
        {
            if (s.Egimli)
            {
                double am = 0.5 * (s.A0 + s.A1);
                zb = SliceAltZ(s, am);
                zt = SliceUstZ(s, am);
                return;
            }
            zb = Math.Min(s.Z0, s.Z1);
            zt = Math.Max(s.Z0, s.Z1);
        }

        /// <summary>Eğimli dilimi [a0,a1] aralığına kırpar (köşe kotları doğrusal), zarf Z0/Z1 güncellenir.</summary>
        private static SectionSlice CloneEgimliSliceA(SectionSlice s, double a0, double a1)
        {
            var c = new SectionSlice { A0 = a0, A1 = a1, Z0 = s.Z0, Z1 = s.Z1, Layer = s.Layer, Order = s.Order, Etiket = s.Etiket };
            if (!s.Egimli) return c;
            double span = s.A1 - s.A0;
            double L(double za, double zb, double a) => Math.Abs(span) < 1e-9 ? za : za + (zb - za) * (a - s.A0) / span;
            c.Egimli = true;
            c.EZ0a = L(s.EZ0a, s.EZ0b, a0); c.EZ0b = L(s.EZ0a, s.EZ0b, a1);
            c.EZ1a = L(s.EZ1a, s.EZ1b, a0); c.EZ1b = L(s.EZ1a, s.EZ1b, a1);
            c.Z0 = Math.Min(c.EZ0a, c.EZ0b);
            c.Z1 = Math.Max(c.EZ1a, c.EZ1b);
            c.DikKalinlik = s.DikKalinlik;
            if (s.UstA != null && Math.Abs(a0 - s.A0) < 1e-6 && Math.Abs(a1 - s.A1) < 1e-6)
            {
                c.UstA = s.UstA; c.UstZ = s.UstZ; c.AltA = s.AltA; c.AltZ = s.AltZ;
                c.Z0 = s.Z0; c.Z1 = s.Z1;
            }
            return c;
        }

        /// <summary>Kırık çizgide a konumundaki z (uçlarda doğrusal uzatma).</summary>
        private static double KirikZ(double[] xa, double[] za, double a)
        {
            int n = xa.Length;
            if (n == 1) return za[0];
            int i = 0;
            while (i < n - 2 && a > xa[i + 1]) i++;
            double d = xa[i + 1] - xa[i];
            return Math.Abs(d) < 1e-9 ? za[i] : za[i] + (za[i + 1] - za[i]) * (a - xa[i]) / d;
        }

        /// <summary>Dilimin a konumundaki üst kotu (kırık çizgi, eğimli köşe kotları veya düz Z1).</summary>
        private static double SliceUstZ(SectionSlice s, double a)
        {
            if (s.UstA != null) return KirikZ(s.UstA, s.UstZ, a);
            if (!s.Egimli) return s.Z1;
            double span = s.A1 - s.A0;
            return Math.Abs(span) < 1e-9 ? s.EZ1a : s.EZ1a + (s.EZ1b - s.EZ1a) * (a - s.A0) / span;
        }

        /// <summary>Dilimin a konumundaki alt kotu.</summary>
        private static double SliceAltZ(SectionSlice s, double a)
        {
            if (s.AltA != null) return KirikZ(s.AltA, s.AltZ, a);
            if (!s.Egimli) return s.Z0;
            double span = s.A1 - s.A0;
            return Math.Abs(span) < 1e-9 ? s.EZ0a : s.EZ0a + (s.EZ0b - s.EZ0a) * (a - s.A0) / span;
        }

        /// <summary>Döşeme diliminin üst/alt doğrusu (z = z0 + g·(a − a0)), A0 noktasında.</summary>
        private static void DosemeDogru(SectionSlice s, bool ust, out double g, out double z0)
        {
            double span = s.A1 - s.A0;
            double za = ust ? (s.Egimli ? s.EZ1a : s.Z1) : (s.Egimli ? s.EZ0a : s.Z0);
            double zb = ust ? (s.Egimli ? s.EZ1b : s.Z1) : (s.Egimli ? s.EZ0b : s.Z0);
            g = Math.Abs(span) < 1e-9 ? 0.0 : (zb - za) / span;
            z0 = za - g * s.A0;
        }

        /// <summary>
        /// Eğimli döşemeli kesitte birleşim düzeltmesi (kesitte eğimli döşeme dilimi yoksa hiçbir şey yapmaz):
        /// 1) eğimli döşeme kalınlığı üst çizgiye dik (alt çizgi üst çizginin paralel ofseti);
        /// 2) bitişik döşemelerde (en az biri eğimli) üst çizgiler kendi kesişiminde, alt çizgiler kendi kesişiminde birleşir (miter);
        /// 3) yanındaki döşeme eğimliyse kirişin üstü döşeme üst çizgisini izler (kiriş çıkıntısı görünmez; ters kiriş korunur).
        /// </summary>
        private static void EgimliDosemeKesitBirlesim(System.Collections.Generic.List<SectionSlice> list)
        {
            if (list == null || list.Count == 0) return;
            const double dokunma = 2.0, kotTol = 2.0;
            var slabs = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(
                System.Linq.Enumerable.Where(list, x => x.Layer == LayerDoseme), x => x.A0));

            // 1) Dik kalınlık (eğimli döşemeden gelen dilimler; eğimsiz döşemede DikKalinlik = 0).
            foreach (var s in slabs)
            {
                if (s.DikKalinlik <= 0) continue;
                if (s.Egimli)
                {
                    double span = s.A1 - s.A0;
                    double g = Math.Abs(span) < 1e-9 ? 0.0 : (s.EZ1b - s.EZ1a) / span;
                    double tv = s.DikKalinlik * Math.Sqrt(1.0 + g * g);
                    s.EZ0a = s.EZ1a - tv; s.EZ0b = s.EZ1b - tv;
                    s.Z0 = Math.Min(s.EZ0a, s.EZ0b);
                }
                else
                    s.Z0 = s.Z1 - s.DikKalinlik;
            }
            if (!System.Linq.Enumerable.Any(slabs, x => x.Egimli)) return;

            // 2) Döşeme–döşeme miter birleşimi.
            int n = slabs.Count;
            var aTL = new double[n]; var aTR = new double[n]; var aBL = new double[n]; var aBR = new double[n];
            for (int i = 0; i < n; i++) { aTL[i] = aBL[i] = slabs[i].A0; aTR[i] = aBR[i] = slabs[i].A1; }
            var birlesik = new bool[n]; // i ile i+1 üstte birleşti
            double Kesisim(double g1, double c1, double g2, double c2, double aj, double lim)
            {
                double dg = g1 - g2;
                if (Math.Abs(dg) < 1e-6) return aj;
                double a = (c2 - c1) / dg;
                return Math.Abs(a - aj) <= lim ? a : aj;
            }
            for (int i = 0; i + 1 < n; i++)
            {
                var l = slabs[i]; var r = slabs[i + 1];
                if (!l.Egimli && !r.Egimli) continue;
                if (Math.Abs(l.A1 - r.A0) > dokunma) continue;
                double aj = 0.5 * (l.A1 + r.A0);
                DosemeDogru(l, true, out double gl, out double cl);
                DosemeDogru(r, true, out double gr, out double cr);
                if (Math.Abs((cl + gl * aj) - (cr + gr * aj)) > kotTol) continue;
                double tMax = Math.Max(SliceUstZ(l, aj) - SliceAltZ(l, aj), SliceUstZ(r, aj) - SliceAltZ(r, aj));
                double lim = Math.Max(40.0, 3.0 * tMax);
                double at = Kesisim(gl, cl, gr, cr, aj, lim);
                DosemeDogru(l, false, out double glb, out double clb);
                DosemeDogru(r, false, out double grb, out double crb);
                double ab = Kesisim(glb, clb, grb, crb, aj, lim);
                aTR[i] = at; aTL[i + 1] = at;
                aBR[i] = ab; aBL[i + 1] = ab;
                birlesik[i] = true;
            }
            for (int i = 0; i < n; i++)
            {
                bool solB = i > 0 && birlesik[i - 1], sagB = birlesik[i];
                if (!solB && !sagB) continue;
                var s = slabs[i];
                DosemeDogru(s, true, out double gt, out double ct);
                DosemeDogru(s, false, out double gb, out double cb);
                if (aTR[i] - aTL[i] < 0.5 || aBR[i] - aBL[i] < 0.5) continue;
                s.UstA = new[] { aTL[i], aTR[i] }; s.UstZ = new[] { ct + gt * aTL[i], ct + gt * aTR[i] };
                s.AltA = new[] { aBL[i], aBR[i] }; s.AltZ = new[] { cb + gb * aBL[i], cb + gb * aBR[i] };
                if (!s.Egimli)
                {
                    s.Egimli = true;
                    s.EZ0a = s.EZ0b = s.Z0; s.EZ1a = s.EZ1b = s.Z1;
                }
                s.Z0 = Math.Min(s.AltZ[0], s.AltZ[1]);
                s.Z1 = Math.Max(s.UstZ[0], s.UstZ[1]);
            }

            // 3) Kiriş üstü döşeme üst çizgisini izler.
            foreach (var b in list)
            {
                if (b.Layer != LayerKiris) continue;
                double b0 = Math.Min(b.A0, b.A1), b1 = Math.Max(b.A0, b.A1);
                if (b1 - b0 < 1.0) continue;
                SectionSlice L = null, R = null, C = null;
                foreach (var s in slabs)
                {
                    if (s.A1 < b0 - dokunma || s.A0 > b1 + dokunma) continue;
                    if (s.A0 <= b0 + 0.5 && s.A1 >= b1 - 0.5) C = s;
                    else if (s.A0 < b0 + 0.5) L = s;
                    else if (s.A1 > b1 - 0.5) R = s;
                }
                Func<double, double> S;
                double split = double.NaN;
                bool egimVar;
                if (C != null)
                {
                    DosemeDogru(C, true, out double g, out double c);
                    S = a => c + g * a; egimVar = C.Egimli && Math.Abs(g) > 1e-3;
                }
                else if (L != null && R != null)
                {
                    DosemeDogru(L, true, out double gl, out double cl);
                    DosemeDogru(R, true, out double gr, out double cr);
                    double aj = L.UstA != null ? L.UstA[L.UstA.Length - 1] : 0.5 * (L.A1 + R.A0);
                    if (Math.Abs((cl + gl * aj) - (cr + gr * aj)) > kotTol) continue;
                    split = aj;
                    double sp = aj;
                    S = a => a <= sp ? cl + gl * a : cr + gr * a;
                    egimVar = (L.Egimli && Math.Abs(gl) > 1e-3) || (R.Egimli && Math.Abs(gr) > 1e-3);
                }
                else if (L != null || R != null)
                {
                    var t = L ?? R;
                    DosemeDogru(t, true, out double g, out double c);
                    S = a => c + g * a; egimVar = t.Egimli && Math.Abs(g) > 1e-3;
                }
                else continue;
                if (!egimVar) continue;
                double bm = 0.5 * (b0 + b1);
                double gMax = Math.Max(Math.Abs(S(b1) - S(bm)), Math.Abs(S(bm) - S(b0))) / Math.Max(1e-6, 0.5 * (b1 - b0));
                // Ters (yukarı kalkık) kiriş veya döşemeden çok farklı kiriş: dokunma.
                if (Math.Abs(SliceUstZ(b, bm) - S(bm)) > 25.0 + gMax * (b1 - b0)) continue;
                var ua = new System.Collections.Generic.List<double> { b0 };
                if (!double.IsNaN(split) && split > b0 + 0.5 && split < b1 - 0.5) ua.Add(split);
                ua.Add(b1);
                var uz = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(ua, S));
                double zb0 = SliceAltZ(b, b0), zb1 = SliceAltZ(b, b1);
                bool ok = true;
                for (int k = 0; k < ua.Count; k++)
                    if (uz[k] <= KirikZ(new[] { b0, b1 }, new[] { zb0, zb1 }, ua[k]) + 1.0) { ok = false; break; }
                if (!ok) continue;
                b.A0 = b0; b.A1 = b1;
                b.Egimli = true;
                b.EZ1a = uz[0]; b.EZ1b = uz[uz.Length - 1]; b.EZ0a = zb0; b.EZ0b = zb1;
                b.UstA = ua.ToArray(); b.UstZ = uz;
                b.AltA = new[] { b0, b1 }; b.AltZ = new[] { zb0, zb1 };
                b.Z0 = Math.Min(zb0, zb1);
                b.Z1 = System.Linq.Enumerable.Max(uz);
            }
        }
    }
}
