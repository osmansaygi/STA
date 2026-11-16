using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// Zımbalama için lokal radye kalınlaştırması (kademeli radye DEĞİL): ST4'te komşu radyelerle aynı üst kotta, daha kalın
    /// (alt kotu daha derin) ayrı radye. Kalın bölge altında 45° şevle düşer: şev komşu ince radyeye kalınlık farkı kadar taşar.
    /// Planda düşen kısmın izi KESIT SINIRI katmanında (iç = kalın radye izi, dış = şev başlangıcı, köşelerde şev çizgisi);
    /// kesitte şev kaması betonla, grobeton şev yüzünde miterli bantla çizilir.
    /// </summary>
    public sealed partial class PlanIdDrawingManager
    {
        private static double RadyeUstCm(SlabFoundationInfo sf) => sf.BottomLevelM * 100.0 + RadyeKalinlikCm(sf);

        /// <summary>Kalın radye → şev genişliği (komşu ince radyelerle en büyük kalınlık farkı, cm). Lokal kalınlaştırma yoksa boş.</summary>
        private Dictionary<SlabFoundationInfo, double> RadyeLokalKalinlastirmalar()
        {
            var res = new Dictionary<SlabFoundationInfo, double>();
            var list = _model?.SlabFoundations;
            if (list == null || list.Count < 2) return res;
            var polys = new List<(SlabFoundationInfo sf, Geometry p)>();
            foreach (var sf in list)
            {
                var p = SlabFoundationFootprintPoly(sf);
                if (p != null && !p.IsEmpty) polys.Add((sf, p));
            }
            foreach (var t in polys)
            {
                double dMax = 0.0;
                foreach (var n in polys)
                {
                    if (ReferenceEquals(n.sf, t.sf)) continue;
                    if (Math.Abs(RadyeUstCm(n.sf) - RadyeUstCm(t.sf)) >= RadyeKademeMinKotFarkiCm) continue;
                    double d = n.sf.BottomLevelM * 100.0 - t.sf.BottomLevelM * 100.0;
                    if (d < RadyeKademeMinKotFarkiCm) continue;
                    try
                    {
                        var shared = t.p.Boundary.Intersection(n.p.Boundary);
                        if (shared == null || shared.IsEmpty || shared.Length < 1.0) continue;
                    }
                    catch { continue; }
                    dMax = Math.Max(dMax, d);
                }
                if (dMax > 0) res[t.sf] = dMax;
            }
            return res;
        }

        /// <summary>Temel planı: lokal kalınlaştırmanın düşen kısım izi (KESIT SINIRI). Temel dışına taşan şev kırpılır.</summary>
        private void DrawRadyeLokalKalinlastirmaIzleri(Transaction tr, BlockTableRecord btr, double offsetX, double offsetY, Geometry temelUnion)
        {
            var lokal = RadyeLokalKalinlastirmalar();
            if (lokal.Count == 0) return;
            var bp = new BufferParameters { EndCapStyle = EndCapStyle.Flat, JoinStyle = NetTopologySuite.Operation.Buffer.JoinStyle.Mitre, MitreLimit = 10.0 };
            void Cizgi(Geometry g)
            {
                if (g == null || g.IsEmpty) return;
                for (int i = 0; i < g.NumGeometries; i++)
                {
                    if (!(g.GetGeometryN(i) is LineString ls) || ls.Length < 1.0) continue;
                    var pts = ls.Coordinates.Select(c => new Autodesk.AutoCAD.Geometry.Point2d(c.X, c.Y)).ToList();
                    var pl = ToPolyline(pts, false);
                    pl.Layer = LayerKesitSiniri;
                    AppendEntity(tr, btr, pl);
                }
            }
            foreach (var kv in lokal)
            {
                try
                {
                    var ic = SlabFoundationFootprintPoly(kv.Key).Copy();
                    ic.Apply(new KademeOtele(offsetX, offsetY));
                    ic.GeometryChanged();
                    var dis = ic.Buffer(kv.Value, bp);
                    if (temelUnion != null && !temelUnion.IsEmpty) dis = dis.Intersection(temelUnion);
                    Cizgi(ic.Boundary);
                    Geometry disSinir = dis.Boundary;
                    // Temel dış çizgisiyle çakışan şev kenarı yeniden çizilmez.
                    try { var kenar = temelUnion?.Boundary?.Buffer(0.5); if (kenar != null && !kenar.IsEmpty) disSinir = disSinir.Difference(kenar); } catch { }
                    Cizgi(disSinir);
                    // Köşe şev çizgileri: iç köşe → en yakın dış köşe (yaklaşık √2·şev uzaklıkta).
                    var disKoseler = dis.Coordinates;
                    foreach (var c in ic.Coordinates.Distinct())
                    {
                        Coordinate best = null; double bd = double.MaxValue;
                        foreach (var o in disKoseler)
                        {
                            double d = c.Distance(o);
                            if (d < bd) { bd = d; best = o; }
                        }
                        if (best == null || bd < 1.0 || bd > kv.Value * 1.6) continue;
                        Cizgi(_ntsDrawFactory.CreateLineString(new[] { new Coordinate(c.X, c.Y), new Coordinate(best.X, best.Y) }));
                    }
                }
                catch { /* iz çizilemezse plan devam etsin */ }
            }
        }

        /// <summary>Kesitte lokal kalınlaştırma eki: kalın dilimin ince komşuya baktığı uç.</summary>
        private struct RadyeLokalUc
        {
            /// <summary>Kalın/ince sınırı (A).</summary>
            public double E;
            /// <summary>+1: kalın dilim +A yönünde (ince solda); −1 tersi.</summary>
            public int D;
            /// <summary>Şev genişliği (ince tarafa taşma, cm).</summary>
            public double W;
            public double NZ0, TZ0;
        }

        private static List<RadyeLokalUc> FindRadyeLokalUclari(List<SectionSlice> slices)
        {
            var res = new List<RadyeLokalUc>();
            if (slices == null) return res;
            var slabs = slices.Where(s => s.Layer == "TEMEL (BEYKENT)" && s.Order == SectionOrderSlabFoundation).ToList();
            if (slabs.Count < 2) return res;
            const double dokunma = 2.0;
            foreach (var t in slabs)
            {
                double t0 = Math.Min(t.A0, t.A1), t1 = Math.Max(t.A0, t.A1);
                foreach (var n in slabs)
                {
                    if (ReferenceEquals(n, t)) continue;
                    if (Math.Abs(n.Z1 - t.Z1) >= RadyeKademeMinKotFarkiCm) continue;
                    double d = n.Z0 - t.Z0;
                    if (d < RadyeKademeMinKotFarkiCm) continue;
                    double n0 = Math.Min(n.A0, n.A1), n1 = Math.Max(n.A0, n.A1);
                    double w = Math.Min(d, n1 - n0);
                    if (w < 0.5) continue;
                    if (Math.Abs(n1 - t0) <= dokunma)
                        res.Add(new RadyeLokalUc { E = 0.5 * (n1 + t0), D = 1, W = w, NZ0 = n.Z0, TZ0 = t.Z0 });
                    else if (Math.Abs(n0 - t1) <= dokunma)
                        res.Add(new RadyeLokalUc { E = 0.5 * (n0 + t1), D = -1, W = w, NZ0 = n.Z0, TZ0 = t.Z0 });
                }
            }
            return res;
        }

        /// <summary>Kesit: lokal kalınlaştırmanın şev kaması (ince radye altında, sınırdan W kadar) beton dilimi olarak eklenir.</summary>
        private static void AddRadyeLokalKamaSlices(List<SectionSlice> slices)
        {
            foreach (var u in FindRadyeLokalUclari(slices))
            {
                double aDis = u.E - u.D * u.W;
                var s = new SectionSlice { Layer = "TEMEL (BEYKENT)", Order = SectionOrderRadyeKademeDolgu, Egimli = true, Z0 = u.TZ0, Z1 = u.NZ0 };
                if (u.D > 0)
                {
                    s.A0 = aDis; s.A1 = u.E;
                    s.UstA = new[] { aDis, u.E }; s.UstZ = new[] { u.NZ0, u.NZ0 };
                    s.AltA = new[] { aDis, u.E }; s.AltZ = new[] { u.NZ0, u.TZ0 };
                    s.EZ1a = s.EZ1b = u.NZ0; s.EZ0a = u.NZ0; s.EZ0b = u.TZ0;
                }
                else
                {
                    s.A0 = u.E; s.A1 = aDis;
                    s.UstA = new[] { u.E, aDis }; s.UstZ = new[] { u.NZ0, u.NZ0 };
                    s.AltA = new[] { u.E, aDis }; s.AltZ = new[] { u.TZ0, u.NZ0 };
                    s.EZ1a = s.EZ1b = u.NZ0; s.EZ0a = u.TZ0; s.EZ0b = u.NZ0;
                }
                slices.Add(s);
            }
        }

        /// <summary>İnce radye grobetonundan şev kamasının altındaki aralığı çıkarır.</summary>
        private static List<(double lo, double hi)> RadyeLokalGrobetonAraliklari(SectionSlice s, List<(double lo, double hi)> spans, List<RadyeLokalUc> uclar)
        {
            if (uclar == null || uclar.Count == 0 || s.Order != SectionOrderSlabFoundation) return spans;
            foreach (var u in uclar)
            {
                if (Math.Abs(u.NZ0 - s.Z0) > 1e-3) continue;
                double lo = Math.Min(u.E, u.E - u.D * u.W), hi = Math.Max(u.E, u.E - u.D * u.W);
                var next = new List<(double lo, double hi)>();
                foreach (var (a, b) in spans)
                {
                    if (hi <= a || lo >= b) { next.Add((a, b)); continue; }
                    if (lo > a) next.Add((a, lo));
                    if (hi < b) next.Add((hi, b));
                }
                spans = next;
            }
            return spans;
        }

        /// <summary>Şev yüzündeki grobeton bandı (a, z): dış kenar miterli (ince ve kalın grobeton alt çizgileriyle kesişir).</summary>
        private static List<(double a, double z)> RadyeLokalGrobetonBandi(RadyeLokalUc u, double h)
        {
            double g = (u.NZ0 - u.TZ0) / Math.Max(1e-6, u.W);
            double k = h * (Math.Sqrt(1.0 + g * g) - 1.0) / Math.Max(1e-6, g);
            double d = u.D;
            return new List<(double a, double z)>
            {
                (u.E - d * (u.W + k), u.NZ0),
                (u.E - d * u.W, u.NZ0),
                (u.E, u.TZ0),
                (u.E, u.TZ0 - h),
                (u.E - d * k, u.TZ0 - h),
                (u.E - d * (u.W + k), u.NZ0 - h)
            };
        }
    }
}
