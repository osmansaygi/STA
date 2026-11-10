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
    /// Kademeli radye (farklı alt kotlu komşu radyeler): alttaki radye, üstteki radyenin altına kendi kalınlığı kadar girer.
    /// Planda iki radyenin sınırı ayrı çizilir; kesitte bindirme bandı alt radye tabanından üst radye tabanına kadar betonla dolar,
    /// grobeton kademe yüzünde şevli olarak devam eder.
    /// </summary>
    public sealed partial class PlanIdDrawingManager
    {
        /// <summary>Kesit dilim sırası: kademe bindirmesinde alt radye üstü ile üst radye altı arasındaki beton.</summary>
        private const int SectionOrderRadyeKademeDolgu = 13;
        private const double RadyeKademeMinKotFarkiCm = 1.0;
        /// <summary>Kademe yüzünde grobeton şevi: yatay / düşey (kullanıcı BYS_T9 düzeltmesi ≈ 23/130).</summary>
        private const double RadyeKademeGrobetonSevOrani = 0.18;

        private static double RadyeKalinlikCm(SlabFoundationInfo sf) => sf.ThicknessCm > 0 ? sf.ThicknessCm : 40.0;

        /// <summary>Plan etiketi kotları (m): alt = bina taban kotu + ST4 alt kotu; üst = alt + kalınlık.
        /// ST4 alt kotu yoksa (0) eski davranış: üst = bina taban kotu.</summary>
        private static void RadyeEtiketKotlari(SlabFoundationInfo sf, double binaTabanKotu, out double ustM, out double altM)
        {
            double th = sf.ThicknessCm > 0 ? sf.ThicknessCm : 80.0;
            if (Math.Abs(sf.BottomLevelM) < 1e-6)
            {
                ustM = binaTabanKotu;
                altM = ustM - th / 100.0;
                return;
            }
            altM = binaTabanKotu + sf.BottomLevelM;
            ustM = altM + th / 100.0;
        }

        /// <summary>Her radyenin kademe bindirmesi uygulanmış taban izi (ofsetsiz). Kademe yoksa ham iz döner.</summary>
        private Dictionary<SlabFoundationInfo, Geometry> BuildRadyeKademeFootprints(out bool kademeVar)
            => BuildRadyeKademeFootprints(out kademeVar, null);

        /// <summary>Bina görünüş/kesit arka planı: bindirme bandındaki beton bloğu (plan izi, alt radye üstü → üst radye altı, cm).</summary>
        private List<(Geometry poly, double z0, double z1)> BuildRadyeKademeDolguBloklari()
        {
            var bloklar = new List<(Geometry poly, double z0, double z1)>();
            BuildRadyeKademeFootprints(out _, bloklar);
            return bloklar;
        }

        /// <summary>Bina görünüş/kesit arka planı için radyeler (kademe bindirmeli iz) + bindirme beton blokları (cm).</summary>
        private List<(Geometry poly, double z0, double z1)> BinaViewRadyeParcalari()
        {
            var bloklar = new List<(Geometry poly, double z0, double z1)>();
            var izler = BuildRadyeKademeFootprints(out _, bloklar);
            var res = new List<(Geometry poly, double z0, double z1)>();
            foreach (var sfd in _model.SlabFoundations)
            {
                double z0 = (_model.BuildingBaseKotu + sfd.BottomLevelM) * 100.0;
                var poly = izler.TryGetValue(sfd, out var iz) ? iz : SlabFoundationFootprintPoly(sfd);
                res.Add((poly, z0, z0 + RadyeKalinlikCm(sfd)));
            }
            res.AddRange(bloklar);
            return res;
        }

        private Dictionary<SlabFoundationInfo, Geometry> BuildRadyeKademeFootprints(out bool kademeVar, List<(Geometry poly, double z0, double z1)> dolguBloklari)
        {
            kademeVar = false;
            var res = new Dictionary<SlabFoundationInfo, Geometry>();
            var polys = new List<(SlabFoundationInfo sf, Geometry poly)>();
            foreach (var sf in _model.SlabFoundations)
            {
                var p = SlabFoundationFootprintPoly(sf);
                if (p == null || p.IsEmpty) continue;
                res[sf] = p;
                polys.Add((sf, p));
            }
            if (polys.Count < 2) return res;
            var bp = new BufferParameters { EndCapStyle = EndCapStyle.Flat, JoinStyle = NetTopologySuite.Operation.Buffer.JoinStyle.Mitre };
            foreach (var low in polys)
            {
                double zLow = low.sf.BottomLevelM * 100.0;
                double t = RadyeKalinlikCm(low.sf);
                var parts = new List<Geometry> { low.poly };
                foreach (var high in polys)
                {
                    if (ReferenceEquals(high.sf, low.sf)) continue;
                    if (high.sf.BottomLevelM * 100.0 <= zLow + RadyeKademeMinKotFarkiCm) continue;
                    try
                    {
                        var shared = low.poly.Boundary.Intersection(high.poly.Boundary);
                        if (shared == null || shared.IsEmpty) continue;
                        for (int i = 0; i < shared.NumGeometries; i++)
                        {
                            if (!(shared.GetGeometryN(i) is LineString ls) || ls.Length < 1.0) continue;
                            var ek = ls.Buffer(t, bp).Intersection(high.poly);
                            if (ek != null && !ek.IsEmpty && ek.Area > 1.0)
                            {
                                parts.Add(ek);
                                if (dolguBloklari != null)
                                {
                                    double zLowTop = (_model.BuildingBaseKotu * 100.0) + zLow + t;
                                    double zHighBot = (_model.BuildingBaseKotu + high.sf.BottomLevelM) * 100.0;
                                    if (zHighBot - zLowTop >= RadyeKademeMinKotFarkiCm)
                                        dolguBloklari.Add((ek, zLowTop, zHighBot));
                                }
                            }
                        }
                    }
                    catch { /* geometri hatası: bu komşu için bindirme yok */ }
                }
                if (parts.Count < 2) continue;
                try
                {
                    var u = UnionPolygonsKeepParts(parts);
                    if (u != null && !u.IsEmpty) { res[low.sf] = u; kademeVar = true; }
                }
                catch { }
            }
            return res;
        }

        /// <summary>Temel planı: kademeli radyelerde her kot grubunun (alt grup bindirmeli) sınırını, temel dış çizgisi hariç çizer.</summary>
        private void DrawRadyeKademeSinirlari(Transaction tr, BlockTableRecord btr, double offsetX, double offsetY, Geometry temelUnion)
        {
            if (_model.SlabFoundations == null || _model.SlabFoundations.Count < 2) return;
            var fp = BuildRadyeKademeFootprints(out bool kademeVar);
            if (!kademeVar) return;
            const string layer = "TEMEL (BEYKENT)";
            Geometry dis = null;
            try { dis = temelUnion?.Boundary?.Buffer(0.5); } catch { dis = null; }
            foreach (var grp in fp.GroupBy(kv => Math.Round(kv.Key.BottomLevelM * 100.0)))
            {
                try
                {
                    var moved = new List<Geometry>();
                    foreach (var kv in grp)
                    {
                        var g = kv.Value.Copy();
                        g.Apply(new KademeOtele(offsetX, offsetY));
                        g.GeometryChanged();
                        moved.Add(g);
                    }
                    var u = moved.Count == 1 ? moved[0] : UnionPolygonsKeepParts(moved);
                    if (u == null || u.IsEmpty) continue;
                    Geometry lines = u.Boundary;
                    if (dis != null && !dis.IsEmpty) lines = lines.Difference(dis);
                    if (lines == null || lines.IsEmpty) continue;
                    for (int i = 0; i < lines.NumGeometries; i++)
                    {
                        if (!(lines.GetGeometryN(i) is LineString ls) || ls.Length < 1.0) continue;
                        var c = ls.Coordinates;
                        var pts = new List<Autodesk.AutoCAD.Geometry.Point2d>();
                        foreach (var co in c) pts.Add(new Autodesk.AutoCAD.Geometry.Point2d(co.X, co.Y));
                        var pl = ToPolyline(pts, false);
                        pl.Layer = layer;
                        AppendEntity(tr, btr, pl);
                    }
                }
                catch { /* sınır çizgisi çizilemezse plan devam etsin */ }
            }
        }

        private sealed class KademeOtele : ICoordinateSequenceFilter
        {
            private readonly double _dx, _dy;
            public KademeOtele(double dx, double dy) { _dx = dx; _dy = dy; }
            public void Filter(CoordinateSequence seq, int i)
            {
                seq.SetX(i, seq.GetX(i) + _dx);
                seq.SetY(i, seq.GetY(i) + _dy);
            }
            public bool Done => false;
            public bool GeometryChanged => true;
        }

        /// <summary>Kesitte kademe bindirme aralığı. Yon=+1: üst radye +a yönünde (alt radye ucu <see cref="OHi"/>); -1: tersi.</summary>
        private struct RadyeKademeAraligi
        {
            public double OLo, OHi;
            public int Yon;
            public double LZ0, LZ1, HZ0;
        }

        private static List<RadyeKademeAraligi> FindRadyeKademeAraliklari(List<SectionSlice> slices)
        {
            var res = new List<RadyeKademeAraligi>();
            if (slices == null) return res;
            var slabs = slices.Where(s => s.Layer == "TEMEL (BEYKENT)" && s.Order == SectionOrderSlabFoundation).ToList();
            if (slabs.Count < 2) return res;
            foreach (var l in slabs)
            {
                double la0 = Math.Min(l.A0, l.A1), la1 = Math.Max(l.A0, l.A1);
                foreach (var h in slabs)
                {
                    if (ReferenceEquals(l, h) || h.Z0 <= l.Z0 + RadyeKademeMinKotFarkiCm) continue;
                    double ha0 = Math.Min(h.A0, h.A1), ha1 = Math.Max(h.A0, h.A1);
                    double lo = Math.Max(la0, ha0), hi = Math.Min(la1, ha1);
                    if (hi - lo < 1.0) continue;
                    int yon;
                    if (ha0 > la0 + 1.0 && ha1 > la1 + 1.0) yon = 1;
                    else if (ha0 < la0 - 1.0 && ha1 < la1 - 1.0) yon = -1;
                    else continue;
                    res.Add(new RadyeKademeAraligi { OLo = lo, OHi = hi, Yon = yon, LZ0 = l.Z0, LZ1 = l.Z1, HZ0 = h.Z0 });
                }
            }
            return res;
        }

        /// <summary>Bindirme bandında alt radye üstü ile üst radye altı arasını beton dilimiyle doldurur.</summary>
        private static void AddRadyeKademeDolguSlices(List<SectionSlice> slices)
        {
            foreach (var k in FindRadyeKademeAraliklari(slices))
            {
                if (k.HZ0 - k.LZ1 < RadyeKademeMinKotFarkiCm) continue;
                slices.Add(new SectionSlice { A0 = k.OLo, A1 = k.OHi, Z0 = k.LZ1, Z1 = k.HZ0, Layer = "TEMEL (BEYKENT)", Order = SectionOrderRadyeKademeDolgu });
            }
        }

        /// <summary>Üst radye grobetonundan alt radyenin bindirdiği aralığı çıkarır (along aralıkları).</summary>
        private static List<(double lo, double hi)> RadyeKademeGrobetonAraliklari(SectionSlice s, double aLo, double aHi, List<RadyeKademeAraligi> kademe)
        {
            var spans = new List<(double lo, double hi)> { (aLo, aHi) };
            if (kademe == null || kademe.Count == 0 || s.Order != SectionOrderSlabFoundation) return spans;
            foreach (var k in kademe)
            {
                if (Math.Abs(k.HZ0 - s.Z0) > 1e-3) continue;
                var next = new List<(double lo, double hi)>();
                foreach (var (lo, hi) in spans)
                {
                    if (k.OHi <= lo || k.OLo >= hi) { next.Add((lo, hi)); continue; }
                    if (k.OLo > lo) next.Add((lo, k.OLo));
                    if (k.OHi < hi) next.Add((k.OHi, hi));
                }
                spans = next;
            }
            return spans;
        }

        /// <summary>Kademe yüzündeki grobeton kaması (a,z): alt radye ucundan şevle üst radye grobeton altına.</summary>
        private static List<(double a, double z)> RadyeKademeGrobetonKama(RadyeKademeAraligi k, double h)
        {
            double e = k.Yon > 0 ? k.OHi : k.OLo;
            double s = Math.Max(0.0, (k.HZ0 - k.LZ0)) * RadyeKademeGrobetonSevOrani;
            double d = k.Yon > 0 ? 1.0 : -1.0;
            return new List<(double a, double z)>
            {
                (e, k.LZ0 - h),
                (e + d * h, k.LZ0 - h),
                (e + d * (h + s), k.HZ0 - h),
                (e, k.HZ0 - h)
            };
        }
    }
}
