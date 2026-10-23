using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        private sealed class SurekliGorunusPrizma
        {
            public Geometry Plan;
            public double Alt, Ust;
            /// <summary>Temel hatılı: temel betonu değil; görünüşte kolon gibi çıkıntı.</summary>
            public bool Cikinti;
        }

        /// <summary>Başka temele saplanan uç: diğer temelin bu temel bandındaki izi mesnettir.</summary>
        private sealed class SurekliGorunusSaplama
        {
            public Geometry Plan;
            public Point2d Dugum;
            public int No;
            public SurekliGorunusTemel Temel;
            public double X0, X1, Alt, Ust;
        }

        /// <summary>Temel üstündeki temel hatılı / kapama perde çıkıntısı.</summary>
        private sealed class SurekliGorunusCikinti
        {
            public Geometry Plan;
            public bool Perde;
            public double X0, X1, Alt, Ust;
        }

        private sealed class SurekliGorunusTemel
        {
            public ContinuousFoundationInfo Kaynak;
            public int No;
            public Vector2d Yon;
            public Point2d A, B, Merkez, GorunusReferansi;
            public Vector2d GorunusYonu;
            public double Istasyon;
            public readonly List<SurekliGorunusPrizma> Parcalar = new List<SurekliGorunusPrizma>();
            public readonly HashSet<int> Kolonlar = new HashSet<int>();
            public readonly List<SurekliGorunusSaplama> Saplamalar = new List<SurekliGorunusSaplama>();
            public readonly List<SurekliGorunusCikinti> Cikintilar = new List<SurekliGorunusCikinti>();
        }

        private sealed class SurekliGorunusKolon
        {
            public ColumnAxisInfo Kaynak;
            public Geometry Plan;
            public double X0, X1, Alt, Ust;
            public double AltKot = double.NegativeInfinity, UstKot = double.PositiveInfinity;
        }

        private sealed class SurekliGorunusTakim
        {
            public Vector2d Yon;
            public Point2d Baslangic;
            public readonly List<SurekliGorunusTemel> Temeller = new List<SurekliGorunusTemel>();
            public readonly List<SurekliGorunusKolon> Kolonlar = new List<SurekliGorunusKolon>();
            public readonly List<SurekliGorunusSaplama> Saplamalar = new List<SurekliGorunusSaplama>();
            public readonly List<SurekliGorunusCikinti> Cikintilar = new List<SurekliGorunusCikinti>();
        }

        // Her parça kendi doğrultusunda açılır. Ortak kolonun plan merkezi,
        // kırık doğrultuların aynı açılım istasyonunda buluşmasını sağlar.
        private static void SurekliGorunusAralik(Geometry plan, Point2d origin, Vector2d u,
            out double min, out double max)
        {
            min = double.MaxValue; max = double.MinValue;
            foreach (Coordinate c in plan.Coordinates)
            {
                double x = (c.X - origin.X) * u.X + (c.Y - origin.Y) * u.Y;
                min = Math.Min(min, x); max = Math.Max(max, x);
            }
        }

        private List<SurekliGorunusTakim> BuildSurekliGorunusTakimlari(FloorInfo floor, bool birlestir = true)
        {
            var gf = _ntsDrawFactory ?? (_ntsDrawFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(new PrecisionModel(100)));
            var temeller = new List<SurekliGorunusTemel>();
            double baseCm = _model.BuildingBaseKotu * 100.0;
            for (int i = 0; i < _model.ContinuousFoundations.Count; i++)
            {
                var cf = _model.ContinuousFoundations[i];
                if (cf == null || cf.WidthCm <= 1 || cf.HeightCm <= 1) continue;
                if (!_axisService.TryIntersect(cf.FixedAxisId, cf.StartAxisId, out Point2d a)
                    || !_axisService.TryIntersect(cf.FixedAxisId, cf.EndAxisId, out Point2d b)
                    || a.GetDistanceTo(b) < 1) continue;
                Vector2d u = (b - a).GetNormal();
                Vector2d n = new Vector2d(-u.Y, u.X);
                Point2d p0 = a - u.MultiplyBy(cf.StartExtensionCm);
                Point2d p1 = b + u.MultiplyBy(cf.EndExtensionCm);
                int offset = cf.FixedAxisId >= 1001 && cf.FixedAxisId <= 1999 ? -cf.OffsetRaw : cf.OffsetRaw;
                ComputeBeamEdgeOffsets(offset, cf.WidthCm * 0.5, out double upper, out double lower);
                Geometry plan = RectToPolygon(gf, new[] { p0 + n.MultiplyBy(upper), p1 + n.MultiplyBy(upper),
                    p1 + n.MultiplyBy(lower), p0 + n.MultiplyBy(lower) });
                double alt = baseCm + cf.BottomKotBinaGoreCm;
                var temel = new SurekliGorunusTemel { Kaynak = cf, No = GetContinuousFoundationLabelNumber(cf, i),
                    Yon = u, A = a, B = b, Merkez = a + n.MultiplyBy((upper + lower) * 0.5) };
                temel.Parcalar.Add(new SurekliGorunusPrizma { Plan = plan, Alt = alt, Ust = alt + cf.HeightCm });
                if (cf.AmpatmanWidthCm > cf.WidthCm + 0.1 && cf.AmpatmanHeightCm > 0.1)
                {
                    var amp = AmpatmanFootprintPoly(cf);
                    if (amp != null && !amp.IsEmpty)
                        temel.Parcalar.Add(new SurekliGorunusPrizma { Plan = amp, Alt = alt,
                            Ust = alt + Math.Min(cf.HeightCm, cf.AmpatmanHeightCm) });
                }
                if (cf.TieBeamWidthCm > 0.1 && cf.HatilLabelHeightCm > 0.1)
                {
                    ComputeTieBeamEdgeOffsets(cf.FixedAxisId, cf.TieBeamOffsetRaw, cf.TieBeamWidthCm * 0.5,
                        out double hu, out double hl);
                    var hatil = RectToPolygon(gf, new[] { a + n.MultiplyBy(hu), b + n.MultiplyBy(hu),
                        b + n.MultiplyBy(hl), a + n.MultiplyBy(hl) });
                    temel.Parcalar.Add(new SurekliGorunusPrizma { Plan = hatil, Alt = alt + cf.HeightCm,
                        Ust = alt + cf.HeightCm + cf.HatilLabelHeightCm, Cikinti = true });
                }
                temeller.Add(temel);
            }

            var kolonlar = new Dictionary<int, SurekliGorunusKolon>();
            foreach (var col in _model.Columns ?? new List<ColumnAxisInfo>())
            {
                if (col == null || kolonlar.ContainsKey(col.ColumnNo)) continue;
                int section = col.ColumnType == 3 ? ResolvePolygonPositionSectionId(floor.FloorNo, col.ColumnNo)
                    : ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
                // Başka katta başlayan kolonun ColumnId yedeği temel katına taşınmaz.
                if (section <= 0) continue;
                Geometry plan = GetColumnPolygonForTable(floor, col, 0, 0, gf);
                if (plan == null || plan.IsEmpty || plan.Area < 0.01) continue;
                var column = new SurekliGorunusKolon { Kaynak = col, Plan = plan };
                var heights = col.ColumnType == 3 ? _model.PolygonColumnKotMFromBinaTabaniByPositionId
                    : _model.ColumnKotMFromBinaTabaniBySectionId;
                if (heights.TryGetValue(section, out var height))
                {
                    column.AltKot = baseCm + Math.Min(height.altM, height.ustM) * 100;
                    column.UstKot = baseCm + Math.Max(height.altM, height.ustM) * 100;
                }
                kolonlar.Add(col.ColumnNo, column);
            }
            // Düzeltme yalnız görünüş kopyasında yapılır; ST4 girdileri değişmez.
            SurekliGorunusUclariniTamamla(temeller, kolonlar.Values.ToList(), gf);
            foreach (var col in kolonlar.Values)
                foreach (var temel in temeller)
                    if (temel.Parcalar.Any(p => SurekliGorunusKolonTemasi(col, p))) temel.Kolonlar.Add(col.Kaynak.ColumnNo);
            SurekliGorunusSaplamalariBul(temeller, kolonlar, gf);
            SurekliGorunusCikintilariBul(temeller, floor);

            var kalan = new HashSet<SurekliGorunusTemel>(temeller);
            var result = new List<SurekliGorunusTakim>();
            foreach (var seed in temeller.OrderByDescending(t => t.Parcalar[0].Plan.Area).ThenBy(t => t.No))
            {
                if (!kalan.Remove(seed)) continue;
                Vector2d u = seed.Yon;
                bool reverse = Math.Abs(u.X) >= Math.Abs(u.Y) ? u.X < 0 : u.Y < 0;
                if (reverse) u = -u;
                var takim = new SurekliGorunusTakim { Yon = u };
                seed.GorunusYonu = u;
                seed.GorunusReferansi = Point2d.Origin;
                seed.Istasyon = 0;
                takim.Temeller.Add(seed);
                for (int at = 0; at < takim.Temeller.Count; at++)
                {
                    var here = takim.Temeller[at];
                    foreach (var other in temeller)
                    {
                        if (!birlestir || !kalan.Contains(other) || KirisAcilimHatAcisiDerece(u, other.Yon) > 25.0) continue;
                        bool ortakKolon = here.Kolonlar.Overlaps(other.Kolonlar);
                        bool bagli = here.Parcalar.Any(a => other.Parcalar.Any(b =>
                            Math.Min(a.Ust, b.Ust) >= Math.Max(a.Alt, b.Alt) - 0.01
                            && (a.Plan.Intersects(b.Plan) || ortakKolon)));
                        if (!bagli) continue;
                        other.GorunusYonu = other.Yon.DotProduct(u) < 0 ? -other.Yon : other.Yon;
                        Geometry anchor = ortakKolon
                            ? kolonlar[here.Kolonlar.Intersect(other.Kolonlar).OrderBy(n => n).First()].Plan
                            : here.Parcalar.SelectMany(a => other.Parcalar.Where(b =>
                                Math.Min(a.Ust, b.Ust) >= Math.Max(a.Alt, b.Alt) - 0.01 && a.Plan.Intersects(b.Plan))
                                .Select(b => a.Plan.Intersection(b.Plan))).FirstOrDefault(g => !g.IsEmpty);
                        if (anchor == null || anchor.IsEmpty) continue;
                        var c = anchor.Centroid.Coordinate;
                        var point = new Point2d(c.X, c.Y);
                        // Orta çizgiler birleşimde buluşmuyorsa (yanal kaçık paralel
                        // temeller, ör. CK T15/T16) aynı hizada değildir; ayrı açılım.
                        if (SurekliGorunusHizaFarki(here, other, point) > KirisAcilimAyniHatSapmaCm) continue;
                        other.GorunusReferansi = point;
                        other.Istasyon = SurekliGorunusIstasyonu(here, point);
                        kalan.Remove(other); takim.Temeller.Add(other);
                    }
                }
                double min = takim.Temeller.Min(t => {
                    SurekliGorunusParcaAraligi(t, t.Parcalar[0].Plan, out double lo, out _); return lo;
                });
                foreach (var t in takim.Temeller) t.Istasyon -= min;
                takim.Baslangic = new Point2d(u.X * min, u.Y * min);
                foreach (int no in takim.Temeller.SelectMany(t => t.Kolonlar).Distinct().OrderBy(n => n))
                {
                    var kaynak = kolonlar[no];
                    var temas = takim.Temeller.SelectMany(t => t.Parcalar)
                        .Where(p => SurekliGorunusKolonTemasi(kaynak, p)).ToList();
                    if (temas.Count == 0) continue;
                    // Ortak kolonda ana (en uzun) temelin bakışı seçilir; uç
                    // kolonunda mutlaka üzerine oturduğu parçanın kendi bakışı.
                    var owner = takim.Temeller.First(t => t.Kolonlar.Contains(no));
                    SurekliGorunusParcaAraligi(owner, kaynak.Plan, out double lo, out double hi);
                    // Hatıl çıkıntı olarak ayrı çizilir; kolon temel gövdesinin üstünden çıkar.
                    var govde = temas.Where(p => !p.Cikinti).ToList();
                    double oturma = govde.Count > 0 ? govde.Max(p => p.Ust) : temas.Max(p => p.Alt);
                    takim.Kolonlar.Add(new SurekliGorunusKolon { Kaynak = kaynak.Kaynak, Plan = kaynak.Plan,
                        X0 = lo, X1 = hi, Alt = oturma, Ust = oturma + SurekliGorunusCikintiCm });
                }
                takim.Kolonlar.Sort((a, b) => a.X0.CompareTo(b.X0));
                foreach (var t in takim.Temeller)
                {
                    foreach (var s in t.Saplamalar)
                    {
                        SurekliGorunusParcaAraligi(t, s.Plan, out double lo, out double hi);
                        takim.Saplamalar.Add(new SurekliGorunusSaplama { Plan = s.Plan, Dugum = s.Dugum, No = s.No,
                            Temel = t, X0 = lo, X1 = hi, Alt = s.Alt, Ust = s.Ust });
                    }
                    double ust = t.Parcalar[0].Ust;
                    foreach (var c in t.Cikintilar)
                    {
                        SurekliGorunusParcaAraligi(t, c.Plan, out double lo, out double hi);
                        if (hi - lo > 2.0)
                            takim.Cikintilar.Add(new SurekliGorunusCikinti { Plan = c.Plan, Perde = c.Perde,
                                X0 = lo, X1 = hi, Alt = ust, Ust = ust + SurekliGorunusCikintiCm });
                    }
                }
                SurekliGorunusCikintiBirlestir(takim.Cikintilar);
                result.Add(takim);
            }
            result.Sort((a, b) => a.Temeller.Min(t => t.No).CompareTo(b.Temeller.Min(t => t.No)));
            return result;
        }

        /// <summary>İki temelin orta çizgilerinin birleşim noktasındaki yanal farkı.</summary>
        private static double SurekliGorunusHizaFarki(SurekliGorunusTemel a, SurekliGorunusTemel b, Point2d p)
        {
            Point2d OrtaCizgide(SurekliGorunusTemel t)
            {
                var n = new Vector2d(-t.Yon.Y, t.Yon.X);
                return p - n.MultiplyBy((p - t.Merkez).DotProduct(n));
            }
            return OrtaCizgide(a).GetDistanceTo(OrtaCizgide(b));
        }

        // Kolonsuz uç enine başka temelin gövdesine saplanıyorsa o temel mesnettir;
        // beton ucu o temelin dış yüzüne kadar uzanır (kirişteki saplama ucu gibi).
        private void SurekliGorunusSaplamalariBul(List<SurekliGorunusTemel> temeller,
            Dictionary<int, SurekliGorunusKolon> kolonlar, GeometryFactory gf)
        {
            foreach (var t in temeller)
            {
                var body = t.Parcalar[0];
                var n = new Vector2d(-t.Yon.Y, t.Yon.X);
                SurekliGorunusAralik(body.Plan, t.A, n, out double yan0, out double yan1);
                double length = t.A.GetDistanceTo(t.B);
                foreach (bool start in new[] { true, false })
                {
                    Point2d node = start ? t.A : t.B;
                    var nodePoint = gf.CreatePoint(new Coordinate(node.X, node.Y));
                    if (t.Kolonlar.Any(no => kolonlar[no].Plan.Distance(nodePoint) < 0.1
                        || (_axisService.TryIntersect(kolonlar[no].Kaynak.AxisXId, kolonlar[no].Kaynak.AxisYId, out Point2d axis)
                            && axis.GetDistanceTo(node) < 0.1))) continue;
                    SurekliGorunusAralik(body.Plan, t.A, t.Yon, out double bLo, out double bHi);
                    double s = start ? 0 : length, end = start ? bLo : bHi;
                    Point2d P(double along, double side) => t.A + t.Yon.MultiplyBy(along) + n.MultiplyBy(side);
                    Geometry band = RectToPolygon(gf, new[] { P(s - 300, yan0), P(s + 300, yan0), P(s + 300, yan1), P(s - 300, yan1) });
                    SurekliGorunusSaplama best = null;
                    double bestD = double.MaxValue;
                    foreach (var o in temeller)
                    {
                        if (o == t || KirisAcilimHatAcisiDerece(t.Yon, o.Yon) <= 25.0) continue;
                        var ob = o.Parcalar[0];
                        if (Math.Min(body.Ust, ob.Ust) < Math.Max(body.Alt, ob.Alt) - 0.01) continue;
                        if (!band.EnvelopeInternal.Intersects(ob.Plan.EnvelopeInternal)) continue;
                        Geometry hit = band.Intersection(ob.Plan);
                        if (hit == null || hit.IsEmpty || hit.Area < 1.0) continue;
                        SurekliGorunusAralik(hit, t.A, t.Yon, out double lo, out double hi);
                        // Aks düğümü veya beton ucu diğer temelin izinde olmalı.
                        bool node0 = s >= lo - 5.0 && s <= hi + 5.0, end0 = end >= lo - 5.0 && end <= hi + 5.0;
                        if (!node0 && !end0) continue;
                        double d = Math.Abs((lo + hi) * 0.5 - s);
                        if (d >= bestD) continue;
                        bestD = d;
                        best = new SurekliGorunusSaplama { Plan = hit, Dugum = node, No = o.No, Alt = ob.Alt, Ust = ob.Ust };
                    }
                    if (best == null) continue;
                    t.Saplamalar.Add(best);
                    SurekliGorunusAralik(best.Plan, t.A, t.Yon, out double sLo, out double sHi);
                    if (start ? sLo >= bLo - 0.01 : sHi <= bHi + 0.01) continue;
                    if (start) bLo = sLo; else bHi = sHi;
                    body.Plan = RectToPolygon(gf, new[] { P(bLo, yan0), P(bHi, yan0), P(bHi, yan1), P(bLo, yan1) });
                }
            }
        }

        // Temel üstündeki hatıl (ST4 sürekli temel hatılı + temel içindeki Tie beams) ve
        // destek katının kapama perdeleri temel betonu sayılmaz; kolon gibi çıkıntıdır.
        private void SurekliGorunusCikintilariBul(List<SurekliGorunusTemel> temeller, FloorInfo floor)
        {
            var adaylar = new List<(Geometry Plan, bool Perde, double Alt, double Ust)>();
            foreach (var t in temeller)
                foreach (var p in t.Parcalar.Where(p => p.Cikinti)) adaylar.Add((p.Plan, false, p.Alt, p.Ust));
            foreach (var tb in _model.TieBeams ?? new List<TieBeamInfo>())
            {
                var poly = TieBeamFootprintPoly(tb);
                if (poly == null || poly.IsEmpty) continue;
                double z0 = (_model.BuildingBaseKotu + tb.BottomKotM) * 100.0;
                adaylar.Add((poly, false, z0, z0 + (tb.HeightCm > 0 ? tb.HeightCm : 25.0)));
            }
            // Açılım sırasında GetRawBeamsOnFloor sentetik temel kirişlerini döndürür; perde ST4'ten.
            foreach (var beam in _model.Beams ?? new List<BeamInfo>())
            {
                if (beam == null || beam.IsWallFlag != 1 || beam.WidthCm <= 1.0
                    || GetBeamFloorNo(beam.BeamId) != floor.FloorNo) continue;
                var poly = BeamFootprintPoly(beam);
                if (poly != null && !poly.IsEmpty) adaylar.Add((poly, true, double.NaN, double.NaN));
            }
            foreach (var t in temeller)
            {
                var body = t.Parcalar[0];
                foreach (var a in adaylar)
                {
                    // Hatıl gövde içinde başlayıp üst yüzü aşmalı; perde destek katından iner.
                    if (!double.IsNaN(a.Alt) && (a.Alt < body.Alt - 1.0 || a.Alt > body.Ust + 1.0 || a.Ust <= body.Ust + 1.0))
                        continue;
                    if (!a.Plan.EnvelopeInternal.Intersects(body.Plan.EnvelopeInternal)) continue;
                    Geometry hit = a.Plan.Intersection(body.Plan);
                    if (hit == null || hit.IsEmpty || hit.Area < 1.0) continue;
                    t.Cikintilar.Add(new SurekliGorunusCikinti { Plan = hit, Perde = a.Perde });
                }
            }
        }

        /// <summary>Aynı türden üst üste binen/bitişik çıkıntılar tek parça çizilir.</summary>
        private static void SurekliGorunusCikintiBirlestir(List<SurekliGorunusCikinti> list)
        {
            var result = new List<SurekliGorunusCikinti>();
            foreach (var c in list.OrderBy(c => c.Perde).ThenBy(c => c.X0))
            {
                var last = result.LastOrDefault();
                if (last != null && last.Perde == c.Perde && c.X0 <= last.X1 + 0.5)
                {
                    last.X1 = Math.Max(last.X1, c.X1);
                    last.Ust = Math.Max(last.Ust, c.Ust);
                    continue;
                }
                result.Add(c);
            }
            list.Clear();
            list.AddRange(result);
        }

        private static bool SurekliGorunusOturuyor(Geometry column, Geometry foundation)
        {
            if (!column.EnvelopeInternal.Intersects(foundation.EnvelopeInternal)) return false;
            return column.Intersection(foundation).Area > 0.01;
        }

        private static bool SurekliGorunusKolonTemasi(SurekliGorunusKolon column, SurekliGorunusPrizma foundation)
        {
            return column.AltKot <= foundation.Ust + 0.1 && column.UstKot > foundation.Ust + 0.01
                && SurekliGorunusOturuyor(column.Plan, foundation.Plan);
        }

        private static double SurekliGorunusIstasyonu(SurekliGorunusTemel t, Point2d point)
        {
            return t.Istasyon + (point - t.GorunusReferansi).DotProduct(t.GorunusYonu);
        }

        private static void SurekliGorunusParcaAraligi(SurekliGorunusTemel t, Geometry plan, out double lo, out double hi)
        {
            SurekliGorunusAralik(plan, t.GorunusReferansi, t.GorunusYonu, out lo, out hi);
            lo += t.Istasyon; hi += t.Istasyon;
        }

        private void SurekliGorunusUclariniTamamla(List<SurekliGorunusTemel> temeller,
            List<SurekliGorunusKolon> kolonlar, GeometryFactory gf)
        {
            // Diğer temellerin özgün ayakizleri kullanılır: bir uç düzeltmesi
            // sonraki temeli tekrar uzatarak zincirleme büyütmemeli.
            var plans = temeller.ToDictionary(t => t, t => t.Parcalar[0].Plan);
            foreach (var t in temeller)
            {
                var body = t.Parcalar[0];
                SurekliGorunusAralik(body.Plan, t.A, t.Yon, out double lo, out double hi);
                double length = t.A.GetDistanceTo(t.B);
                foreach (bool start in new[] { true, false })
                {
                    Point2d node = start ? t.A : t.B;
                    var nodePoint = gf.CreatePoint(new Coordinate(node.X, node.Y));
                    var endColumns = kolonlar.Where(c =>
                        c.AltKot <= body.Ust + 0.1 && c.UstKot > body.Ust + 0.01 &&
                        ((_axisService.TryIntersect(c.Kaynak.AxisXId, c.Kaynak.AxisYId, out Point2d axis)
                            && axis.GetDistanceTo(node) < 0.1)
                        // Aks dışı geniş kolon yüzü düğüme mm mertebesinde kaçabilir (MS T32/SB-26: 0,43 cm).
                        || c.Plan.Distance(nodePoint) < 1.0)).ToList();
                    foreach (var col in endColumns)
                    {
                        SurekliGorunusAralik(col.Plan, t.A, t.Yon, out double cl, out double ch);
                        if (start) lo = Math.Min(lo, cl); else hi = Math.Max(hi, ch);
                        // Üç kollu düğümde devam eden yakın doğrultulu temelin
                        // uzak kenarı çıkıntı sayılmaz. En enine gelen temel seçilir.
                        var candidates = temeller.Where(other => other != t
                            && KirisAcilimHatAcisiDerece(t.Yon, other.Yon) > 25
                            && Math.Min(body.Ust, other.Parcalar[0].Ust) >= Math.Max(body.Alt, other.Parcalar[0].Alt) - 0.01
                            && SurekliGorunusOturuyor(col.Plan, plans[other]))
                            .OrderByDescending(other => KirisAcilimHatAcisiDerece(t.Yon, other.Yon)).ThenBy(other => other.No);
                        foreach (var other in candidates)
                        {
                            // Sonsuz doğru yerine kolon ve aday temeli kapsayan
                            // sonlu kesit; yalnız merkez çizgisindeki gerçek sınır.
                            var env = plans[other].EnvelopeInternal;
                            double reach = node.GetDistanceTo(new Point2d(env.Centre.X, env.Centre.Y))
                                + env.Width + env.Height + length + 1;
                            var line = gf.CreateLineString(new[] {
                                new Coordinate(t.Merkez.X - t.Yon.X * reach, t.Merkez.Y - t.Yon.Y * reach),
                                new Coordinate(t.Merkez.X + t.Yon.X * reach, t.Merkez.Y + t.Yon.Y * reach) });
                            Geometry hit = line.Intersection(plans[other]);
                            bool connected = false;
                            // Ayrık geometride yalnız bu kolonla ilişkili kesiti al.
                            for (int i = 0; i < hit.NumGeometries; i++)
                            {
                                var part = hit.GetGeometryN(i);
                                if (part.IsEmpty) continue;
                                SurekliGorunusAralik(part, t.A, t.Yon, out double ol, out double oh);
                                if (oh < cl - 0.1 || ol > ch + 0.1) continue;
                                connected = true;
                                // Görünüş kolonun TAM izdüşümünü kullanır. Orta
                                // çizginin aksa göre mesafesini doğrudan buna eklemek,
                                // kaçıklıklı/eğik iki uçta farklı çıkıntı üretir.
                                // Karşı temelin kolon yüzü dışındaki net çıkıntısını
                                // kendi normalinde ölç, açılım boyuna açıyla dönüştür.
                                var normal = new Vector2d(-other.Yon.Y, other.Yon.X);
                                Vector2d outward = start ? -t.Yon : t.Yon;
                                if (normal.DotProduct(outward) < 0) normal = -normal;
                                SurekliGorunusAralik(plans[other], Point2d.Origin, normal, out _, out double edge);
                                SurekliGorunusAralik(col.Plan, Point2d.Origin, normal, out _, out double face);
                                double overhang = Math.Max(0, edge - face) / normal.DotProduct(outward);
                                if (start) lo = Math.Min(lo, cl - overhang); else hi = Math.Max(hi, ch + overhang);
                            }
                            if (connected) break;
                        }
                    }
                }
                SurekliGorunusAralik(body.Plan, t.A, t.Yon, out double oldLo, out double oldHi);
                if (lo >= oldLo - 0.01 && hi <= oldHi + 0.01) continue;
                var n = new Vector2d(-t.Yon.Y, t.Yon.X);
                SurekliGorunusAralik(body.Plan, t.A, n, out double lower, out double upper);
                body.Plan = RectToPolygon(gf, new[] { t.A + t.Yon.MultiplyBy(lo) + n.MultiplyBy(lower),
                    t.A + t.Yon.MultiplyBy(hi) + n.MultiplyBy(lower),
                    t.A + t.Yon.MultiplyBy(hi) + n.MultiplyBy(upper),
                    t.A + t.Yon.MultiplyBy(lo) + n.MultiplyBy(upper) });
            }
        }

        private bool DrawSurekliTemelGorunusleri(Database db, Editor ed, Point3d insert, string path, KirisAcilimSecenekleri options)
        {
            if (db == null || _axisService == null) return false;
            FloorInfo floor = SurekliTemelDestekKati();
            if (floor == null) { ed?.WriteMessage("\nSUREKLITEMELACILIMLARI: Destek katı yok."); return false; }
            var takimlar = BuildSurekliGorunusTakimlari(floor, options?.TemelBirlestir ?? true);
            // Kolon filizi: KOLONDUSEY ile aynı kaynak (GPR kolon betonarme, yoksa .SMX).
            _surekliFilizSahibi = new Dictionary<int, int>();
            try { PrepareKolonApplicationRebar(path, ed); }
            catch (System.Exception ex) { ed?.WriteMessage("\nKolon filiz donatısı okunamadı: {0}", ex.Message); }
            foreach (var t in takimlar)
                foreach (var c in t.Kolonlar)
                    if (!_surekliFilizSahibi.ContainsKey(c.Kaynak.ColumnNo))
                        _surekliFilizSahibi[c.Kaynak.ColumnNo] = t.Temeller.Min(f => f.No);
            if (takimlar.Count == 0) return false;
            var donati = SurekliTemelReadDonati(path);
            KirisAcilimKesitBaslat(floor);
            _kirisAcilimGorunum = null;
            _kirisAcilimGorunumOlcekMerkezi = insert;
            _kirisYerlesim = options?.Antetli == true ? new KirisYerlesimKaydi() : null;
            var ids = new List<ObjectId>();
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    EnsureLayers(tr, db);
                    EnsurePlanLayer(tr, db, "TEMEL (BEYKENT)", 7, LineWeight.LineWeight035, false);
                    EnsurePlanLayer(tr, db, LayerEtriye, 150, LineWeight.LineWeight035, false);
                    EnsurePlanLayer(tr, db, LayerKesitGorunus, 8, LineWeight.LineWeight020, false);
                    EnsurePlanLayer(tr, db, LayerIzdusum, 253, LineWeight.LineWeight020, true);
                    EnsurePlanLayer(tr, db, LayerEtiketCizgisi, 64, LineWeight.LineWeight015, false);
                    // Takımlar önce birbirinden uzak geçici satırlara çizilir; yer ayırma tek geçişte
                    // koordinatlardan yapılır. Yeni yazı/ölçülerde GeometricExtents (font motoru),
                    // takım başına modelspace taraması ve ikinci toplu TransformBy kullanılmaz
                    // (kirişte aynı acdb26+0x1e3131 / 0xc0000409 göçmesi için kaldırılan kalıplar).
                    const double geciciAra = 50000.0;
                    var kayitlar = new List<(SurekliGorunusTakim Takim, long Start, long End, double ShiftY, double Low, double High)>();
                    for (int k = 0; k < takimlar.Count; k++)
                    {
                        var takim = takimlar[k];
                        long start = KirisYerlesimSonrakiHandle(db);
                        double alt = takim.Temeller.SelectMany(t => t.Parcalar).Min(p => p.Alt);
                        double ust = Math.Max(takim.Temeller.SelectMany(t => t.Parcalar).Max(p => p.Ust),
                            takim.Kolonlar.Count == 0 ? alt : takim.Kolonlar.Max(c => c.Ust));
                        double shiftY = insert.Y + k * geciciAra - alt;
                        DrawSurekliGorunusTakim(tr, ms, db, floor, takim, insert.X, shiftY);
                        DrawSurekliGorunusDonati(tr, ms, db, floor, takim, insert.X, shiftY, donati);
                        kayitlar.Add((takim, start, KirisYerlesimSonrakiHandle(db), shiftY, shiftY + alt - 50, shiftY + ust));
                    }
                    long first = kayitlar.Count == 0 ? long.MaxValue : kayitlar[0].Start;
                    var yeni = new List<(ObjectId Id, int Takim)>();
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased || id.Handle.Value < first) continue;
                        long h = id.Handle.Value;
                        int k = kayitlar.FindIndex(r => h >= r.Start && h < r.End);
                        if (k < 0) continue;
                        var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (entity == null) continue;
                        yeni.Add((id, k));
                        if (!SurekliGorunusYaklasikSinir(entity, out double minX, out double minY, out double maxX, out double maxY)) continue;
                        var r0 = kayitlar[k];
                        kayitlar[k] = (r0.Takim, r0.Start, r0.End, r0.ShiftY, Math.Min(r0.Low, minY), Math.Max(r0.High, maxY));
                        _surekliGorunusGenislik = Math.Max(_surekliGorunusGenislik, maxX - insert.X);
                    }
                    double cursor = insert.Y;
                    var dys = new double[kayitlar.Count];
                    for (int k = 0; k < kayitlar.Count; k++)
                    {
                        var r = kayitlar[k];
                        dys[k] = cursor - r.Low;
                        if (_kirisYerlesim != null)
                            _kirisYerlesim.Takimlar.Add(new KirisYerlesimTakim { Floor = floor,
                                No = r.Takim.Temeller.Min(t => t.No), StartHandle = r.Start, EndHandle = r.End,
                                // Satır hizası temel gövdesinin üst yüzü: hatıl çıkıntısı olan temeller kaymasın.
                                BeamTop = r.ShiftY + dys[k] + r.Takim.Temeller.SelectMany(t => t.Parcalar)
                                    .Where(p => !p.Cikinti).DefaultIfEmpty(r.Takim.Temeller[0].Parcalar[0]).Max(p => p.Ust) });
                        cursor += r.High - r.Low + 80.0;
                    }
                    var scale = Matrix3d.Scaling(2.0, insert);
                    foreach (var item in yeni)
                    {
                        // Taşıma ve 2× ölçek tek dönüşüm.
                        var ent = (Entity)tr.GetObject(item.Id, OpenMode.ForWrite);
                        ent.TransformBy(scale * Matrix3d.Displacement(new Vector3d(0, dys[item.Takim], 0)));
                        ids.Add(item.Id);
                        if (ent is Autodesk.AutoCAD.DatabaseServices.Dimension dim) { dim.Dimlfac = 0.5; dim.Dimscale = 1; }
                    }
                    tr.Commit();
                    if (_kirisYerlesim == null)
                        KirisAcilimGorunumEkle(new Extents3d(new Point3d(insert.X - 100, insert.Y - 150, 0),
                            new Point3d(insert.X + 2 * _surekliGorunusGenislik + 150, insert.Y + 2 * (cursor - insert.Y), 0)));
                }
                if (_kirisYerlesim != null) KirisAcilimAnteteYerlestirKatKat(db, ed, insert, path, ids);
                SurekliTemelHatilDetaylari(db, ed, path);
                ed?.WriteMessage("\nSUREKLITEMELACILIMLARI: {0} temel açılımı. Donatı, etriye ve kesitler açık; düzeltilmiş beton ve kolon konumları korundu.", takimlar.Count);
                if (donati.Count == 0) ed?.WriteMessage("\nTMX/GPR donatı kaydı bulunamadı; donatı adedi ve çapı üretilmedi.");
                return true;
            }
            finally { _kirisYerlesim = null; _surekliGorunusIlkHandle = 0; _surekliGorunusGenislik = 0; }
        }

        /// <summary>
        /// Sürekli temel hatılı (ST4 12. sütun en, 13. sütun yükseklik) varsa antetin (antet yoksa açılımların) üstüne
        /// Yardımcı TEMELHATILI detayı ve adet/boy tablosu. Tip: en, yükseklik, temel yüksekliği, temel üst kotu;
        /// boy hatıl şeridinin aks kesişimleri arası. Donatı STA4CAD'den (TMX, yoksa GPR "HATIL:" satırı; temel başına),
        /// bulunamazsa Yardımcı temelhatili.ini; paspayı/döşeme ini (yoksa varsayılan).
        /// </summary>
        private void SurekliTemelHatilDetaylari(Database db, Editor ed, string st4Path)
        {
            if (!_kirisAcilimGorunum.HasValue || _model?.ContinuousFoundations == null || _axisService == null) return;
            var donatiByT = SurekliTemelHatilDonatiOku(st4Path, out string kaynak);
            // Boylar temel planındaki hatıl çizimi gibi: aks kesişimleri arası şerit − ilk kat kolon/perdeleri;
            // kalan her parça (planda ayrı "TH (en/yükseklik)" etiketli) bir boy, boy = alan / en.
            Geometry kolonPerde = _model.Floors.Count > 0 ? BuildKolonPerdeUnion(_model.Floors[0], 0, 0) : null;
            var gruplar = new Dictionary<(int W, int H, int T, int K, int DC, int DA, int YC, int YN), List<double>>();
            for (int ci = 0; ci < _model.ContinuousFoundations.Count; ci++)
            {
                var cf = _model.ContinuousFoundations[ci];
                if (cf == null || cf.TieBeamWidthCm <= 0 || cf.HatilLabelHeightCm <= 0 || cf.HeightCm <= 0) continue;
                var serit = HatilStripOnContinuousPoly(cf);
                if (serit == null || serit.IsEmpty) continue;
                Geometry kalan = serit;
                if (kolonPerde != null && !kolonPerde.IsEmpty)
                {
                    var fark = TryDifferenceSafe(serit, kolonPerde);
                    if (fark != null && !fark.IsEmpty) kalan = ReducePrecisionSafe(fark, 100) ?? fark;
                }
                var boylar = CleanGeometryToPolygons(kalan, _ntsDrawFactory, applySmallTriangleTrim: false)
                    .Where(p => p is Polygon && p.Area >= 1000.0)
                    .Select(p => Math.Round(p.Area / cf.TieBeamWidthCm)).Where(b => b >= 1.0).ToList();
                if (boylar.Count == 0) continue;
                donatiByT.TryGetValue(GetContinuousFoundationLabelNumber(cf, ci), out var dn);
                var key = ((int)Math.Round(cf.TieBeamWidthCm), (int)Math.Round(cf.HatilLabelHeightCm), (int)Math.Round(cf.HeightCm),
                    (int)Math.Round(_model.BuildingBaseKotu * 100.0 + cf.BottomKotBinaGoreCm + cf.HeightCm),
                    dn.DikeyCap, dn.DikeyAralik, dn.YatayCap, dn.YatayAdet);
                if (!gruplar.TryGetValue(key, out var list)) gruplar[key] = list = new List<double>();
                list.AddRange(boylar);
            }
            if (gruplar.Count == 0) return;
            // Etiket temel planıyla aynı: sürekli temel hatılı "TH (en/yükseklik)".
            var tipler = gruplar.OrderBy(g => g.Key.W).ThenBy(g => g.Key.H).ThenBy(g => g.Key.T).ThenBy(g => g.Key.K)
                .ThenBy(g => g.Key.DC).ThenBy(g => g.Key.DA).ThenBy(g => g.Key.YC).ThenBy(g => g.Key.YN)
                .Select((g, i) => (No: i + 1, W: (double)g.Key.W, H: (double)g.Key.H, TemelH: (double)g.Key.T,
                    // Tablo yalnız ardışık eş boyları birleştirir: büyükten küçüğe sıralı, eş boylar tek satır (NxL).
                    TemelUstKotCm: (double)g.Key.K, LengthsCm: g.Value.OrderByDescending(l => l).ToList(),
                    DikeyCapMm: (double)g.Key.DC, DikeyAralikCm: (double)g.Key.DA, YatayCapMm: (double)g.Key.YC, YatayAdet: g.Key.YN,
                    Ad: string.Format(CultureInfo.InvariantCulture, "TH ({0}/{1})", g.Key.W, g.Key.H)))
                .ToList();
            ST4Yardimci.TemelHatiliSettings.Yukle();
            if (tipler.Any(t => t.DikeyCapMm <= 0))
                ed?.WriteMessage("\nSUREKLITEMELACILIMLARI: bazı temel hatıllarında STA4CAD donatısı yok; Yardımcı TEMELHATILI ayarı kullanıldı.");
            if (kaynak != null) ed?.WriteMessage("\nSUREKLITEMELACILIMLARI: temel hatılı donatısı {0} dosyasından.", kaynak);
            var ext = _kirisAcilimGorunum.Value;
            // Detay kökü grobeton sol-altı; altındaki başlık için antet üstünden 200 (nihai cm) boşluk.
            var origin = new Point3d(ext.MinPoint.X + 100.0, ext.MaxPoint.Y + 200.0, 0);
            var notlar = new List<string>();
            double sag, ust;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                sag = ST4Yardimci.TemelHatiliRunner.DrawTipler(tr, db, origin, tipler, notlar, out ust);
                tr.Commit();
            }
            if (sag > origin.X)
                KirisAcilimGorunumEkle(new Extents3d(new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, 0), new Point3d(sag + 100.0, ust + 100.0, 0)));
            ed?.WriteMessage("\nSUREKLITEMELACILIMLARI: temel hatılı detayı — {0}", string.Join("; ", notlar));
        }

        /// <summary>Yer ayırma için yaklaşık sınır; yazı/ölçüde font motorunu çalıştırmaz.</summary>
        private static bool SurekliGorunusYaklasikSinir(Entity e, out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = minY = double.MaxValue; maxX = maxY = double.MinValue;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            void Ekle(Point3d p, double pad)
            {
                x0 = Math.Min(x0, p.X - pad); y0 = Math.Min(y0, p.Y - pad);
                x1 = Math.Max(x1, p.X + pad); y1 = Math.Max(y1, p.Y + pad);
            }
            switch (e)
            {
                case Line l: Ekle(l.StartPoint, 0); Ekle(l.EndPoint, 0); break;
                case Polyline pl:
                    for (int i = 0; i < pl.NumberOfVertices; i++) Ekle(pl.GetPoint3dAt(i), 0);
                    break;
                case Circle c: Ekle(c.Center, c.Radius); break;
                case Arc a: Ekle(a.Center, a.Radius); break;
                case DBText t:
                    // Hizalama noktasından metin boyu kadar her yöne pay: döndürülmüş yazı da kapsanır.
                    double len = t.Height * Math.Max(1, (t.TextString ?? "").Length) * 0.9;
                    Ekle(t.Position, t.Height * 1.5);
                    // Sol-taban hizalı yazıda AlignmentPoint (0,0) döner; kullanılmaz.
                    if (t.HorizontalMode != TextHorizontalMode.TextLeft || t.VerticalMode != TextVerticalMode.TextBase)
                        Ekle(t.AlignmentPoint, t.Height * 1.5);
                    if (Math.Abs(Math.Sin(t.Rotation)) > 0.1) { Ekle(t.Position + new Vector3d(0, len, 0), 0); Ekle(t.Position - new Vector3d(0, len, 0), 0); }
                    else { x1 = Math.Max(x1, t.Position.X + len); }
                    break;
                case MText mt: Ekle(mt.Location, mt.TextHeight * 3); break;
                case RotatedDimension rd: Ekle(rd.XLine1Point, 0); Ekle(rd.XLine2Point, 0); Ekle(rd.DimLinePoint, 12); break;
                case AlignedDimension ad: Ekle(ad.XLine1Point, 0); Ekle(ad.XLine2Point, 0); Ekle(ad.DimLinePoint, 12); break;
                default: return false;
            }
            if (x0 > x1 || y0 > y1 || double.IsNaN(x0 + y0 + x1 + y1)) return false;
            minX = x0; minY = y0; maxX = x1; maxY = y1;
            return true;
        }

        /// <summary>Kolon no → filiz adet/boy etiketinin yazıldığı açılımın temel no'su (metrajda çift sayılmasın).</summary>
        private Dictionary<int, int> _surekliFilizSahibi;

        /// <summary>
        /// Kolon filizi (yalnız kolon; perde/hatıl filizi yok). KOLONDUSEY kuralı: temel altından 5 cm,
        /// temel üstünden 1,5ℓb (≥30 cm), alt kanca 12φ ve a+b ≥ 0,75ℓb. Görünüşte kolon çıkıntısında kesilir.
        /// Filiz yazısı kolon üstünün hemen üstünde (kolon adı onun üstüne); yazıldıysa true.
        /// </summary>
        private bool SurekliGorunusKolonFiliz(Transaction tr, BlockTableRecord ms, Database db, FloorInfo floor,
            SurekliGorunusTakim takim, SurekliGorunusKolon col, double x, double y)
        {
            int fi = _model.Floors.IndexOf(floor);
            if (fi < 0 || col.X1 - col.X0 < 12.0) return false;
            string raw = KolonDuseyKatGprDonati(fi, col.Kaynak.ColumnNo);
            if (!KolonDonatiTableDrawer.TryParseKolonKesitDuseyDonatiByDia(raw, out var byDia) || byDia.Count == 0)
                return false; // Veri yoksa adet/çap uydurulmaz.
            // Kolonun altındaki temel gövdesinin (ampatman dahil) alt kotu.
            double alt = double.MaxValue;
            foreach (var t in takim.Temeller)
                foreach (var p in t.Parcalar.Where(p => !p.Cikinti))
                {
                    SurekliGorunusParcaAraligi(t, p.Plan, out double lo, out double hi);
                    if (hi > col.X0 + 0.1 && lo < col.X1 - 0.1) alt = Math.Min(alt, p.Alt);
                }
            if (alt == double.MaxValue || col.Alt - alt < 10.0) return false;
            double zBot = alt + 5.0, a = col.Alt - zBot;
            var parcalar = new List<string>();
            double kanca = 0;
            foreach (var kv in byDia)
            {
                int dia = kv.Key >= 6 ? kv.Key : 14;
                double lb = Ts500KenetlenmeLbCm(dia, _rebarFckMPa, _rebarFykMPa);
                double b = CeilTo5Cm(12.0 * dia / 10.0);
                if (a + b < 0.75 * lb) b = CeilTo5Cm(Math.Max(b, 0.75 * lb - a));
                kanca = Math.Max(kanca, b);
                parcalar.Add(string.Format(CultureInfo.InvariantCulture, "{0}ø{1}", kv.Value, dia));
            }
            // Kolonun iki yüzünde birer temsilî çubuk; kanca KOLONDUSEY'deki gibi kolon içine,
            // dar kolonda iki kanca çakışacaksa ST4CAD'deki gibi dışa.
            double mid = (col.X0 + col.X1) * 0.5;
            bool ice = 2.0 * kanca < col.X1 - col.X0 - 12.0;
            foreach (double fx in new[] { col.X0 + 5.0, col.X1 - 5.0 })
            {
                double dir = (fx < mid ? 1.0 : -1.0) * (ice ? 1.0 : -1.0);
                // KOLONDUSEY filizi: 2 cm büküm yarıçapı, 90° yay (bulge tan 22,5°).
                const double rBend = 2.0, k90 = 0.41421356237;
                double xb = x + fx;
                AppendDonatiPline(tr, ms, new[]
                {
                    new Point2d(xb + dir * kanca, y + zBot),
                    new Point2d(xb + dir * rBend, y + zBot),
                    new Point2d(xb, y + zBot + rBend),
                    new Point2d(xb, y + col.Ust)
                }, new[] { 0.0, dir > 0 ? -k90 : k90, 0.0, 0.0 }, KirisAcilimDonatiLayer);
            }
            SurekliGorunusTemelIciEtriye(tr, ms, fi, col, x, y, zBot);
            _surekliFilizSahibi.TryGetValue(col.Kaynak.ColumnNo, out int sahip);
            bool burada = takim.Temeller.Any(t => t.No == sahip);
            string yazi = burada ? string.Join(" + ", parcalar) + " FİLİZ"
                : string.Format(CultureInfo.InvariantCulture, "FİLİZ bkz. T-{0}", sahip);
            DrawBeamLabel(tr, ms, db, new Point3d(x + mid, y + col.Ust + SurekliGorunusFilizYaziDy, 0), yazi, 4, 0,
                KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
            return true;
        }

        /// <summary>Filiz yazısı ortası kolon üstünden (4 yükseklik, 5,5–9,5 arası); kolon adı altı +12'ye kalkar.</summary>
        private const double SurekliGorunusFilizYaziDy = 7.5, SurekliGorunusFilizliKolonAdiDy = 7.0;

        /// <summary>
        /// KOLONDUSEY temel içi etriyesi (TBDY 2018 7.3.4.1): temel üstünden aşağı kolon kısa kenarı
        /// kadar, alt örtüde (temel altı + 5 cm) durur; aralık sarılma s_max, GPR sık aralığı daha küçükse o.
        /// </summary>
        private void SurekliGorunusTemelIciEtriye(Transaction tr, BlockTableRecord ms, int fi,
            SurekliGorunusKolon col, double x, double y, double zCover)
        {
            if (_kolonDuseyGpr == null || !KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(
                    _kolonDuseyGpr, _model.Floors, fi, col.Kaynak.ColumnNo, out _, out string donati, out string etRaw))
                return;
            var env = col.Plan.EnvelopeInternal;
            double shortCm = Math.Min(env.Width, env.Height);
            if (shortCm < 10.0) return;
            int diaLong = 14;
            KolonDonatiTableDrawer.SumKolonKesitDuseyDonatiAdet(donati, out int d);
            if (d >= 6) diaLong = d;
            ParseKolonEtriyeAralikCm(etRaw, out _, out double sGprConf, out bool tekAralikGpr);
            double sUse = TbdY2018KolonSarilmaSMaxCm(shortCm, diaLong);
            if (!tekAralikGpr) sUse = Math.Min(sUse, sGprConf);
            int sCm = Math.Max(5, (int)Math.Floor(sUse + 1e-6));
            double z0 = col.Alt, zEnd = Math.Max(z0 - shortCm, zCover);
            for (double z = z0; z >= zEnd - 0.05; z -= sCm)
                KirisAcilimCizgi(tr, ms, x + col.X0 + 4.0, y + z, x + col.X1 - 4.0, y + z, LayerEtriye);
        }

        private long _surekliGorunusIlkHandle;
        private double _surekliGorunusGenislik;

        private void DrawSurekliGorunusTakim(Transaction tr, BlockTableRecord ms, Database db, FloorInfo floor,
            SurekliGorunusTakim takim, double x, double y)
        {
            if (_surekliGorunusIlkHandle == 0) _surekliGorunusIlkHandle = KirisYerlesimSonrakiHandle(db);
            var bodies = new List<Geometry>();
            foreach (var temel in takim.Temeller)
                foreach (var p in temel.Parcalar)
                {
                    if (p.Cikinti) continue;
                    SurekliGorunusParcaAraligi(temel, p.Plan, out double lo, out double hi);
                    bodies.Add(KirisAcilimRect(_ntsDrawFactory, x + lo, y + p.Alt, x + hi, y + p.Ust));
                }
            var concrete = NtsOverlaySafe.CascadedUnionSafe(bodies);
            var profileParts = new List<Geometry> { concrete };
            foreach (var col in takim.Kolonlar)
                profileParts.Add(KirisAcilimRect(_ntsDrawFactory, x + col.X0, y + col.Alt, x + col.X1, y + col.Ust));
            foreach (var c in takim.Cikintilar)
                profileParts.Add(KirisAcilimRect(_ntsDrawFactory, x + c.X0, y + c.Alt, x + c.X1, y + c.Ust));
            var profile = NtsOverlaySafe.CascadedUnionSafe(profileParts);
            var cuts = takim.Kolonlar.Select(c => new KirisAcilimKolonTrim { X0 = x + c.X0, X1 = x + c.X1,
                YTop = y + c.Ust, TrimTop = true }).ToList();
            cuts.AddRange(takim.Cikintilar.Select(c => new KirisAcilimKolonTrim { X0 = x + c.X0, X1 = x + c.X1,
                YTop = y + c.Ust, TrimTop = true }));
            bool Kenar(double ax, double bx, double ay, double by, double x0, double x1, double alt, double ust) =>
                Math.Abs(ax - bx) < 0.01 && (Math.Abs(ax - x - x0) < 0.1 || Math.Abs(ax - x - x1) < 0.1)
                && Math.Min(ay, by) >= y + alt - 0.1 && Math.Max(ay, by) <= y + ust + 0.1;
            // Önce ortak kontur ve standart 2/4 mm temizliği. Kolon-temel
            // iç çizgileri union'da kaybolur; sadece kütük üst ağzı açık bırakılır.
            DrawGeometryRingsAsPolylines(tr, ms, profile, "TEMEL (BEYKENT)", onClosedPolylineAppended: id =>
            {
                var pl = (Polyline)tr.GetObject(id, OpenMode.ForWrite);
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    Point2d a = pl.GetPoint2dAt(i), b = pl.GetPoint2dAt((i + 1) % pl.NumberOfVertices);
                    foreach (var bit in KirisAcilimKenarParca(new Coordinate(a.X, a.Y), new Coordinate(b.X, b.Y), cuts))
                    {
                        if (bit.A.GetDistanceTo(bit.B) < 0.4) continue;
                        string layer = "TEMEL (BEYKENT)";
                        if (takim.Kolonlar.Any(c => Kenar(bit.A.X, bit.B.X, bit.A.Y, bit.B.Y, c.X0, c.X1, c.Alt, c.Ust)))
                            layer = LayerKolon;
                        else
                        {
                            var cik = takim.Cikintilar.FirstOrDefault(c => Kenar(bit.A.X, bit.B.X, bit.A.Y, bit.B.Y, c.X0, c.X1, c.Alt, c.Ust));
                            if (cik != null) layer = cik.Perde ? LayerPerde : LayerTemelHatiliKesit;
                        }
                        KirisAcilimCizgi(tr, ms, bit.A.X, bit.A.Y, bit.B.X, bit.B.Y, layer);
                    }
                }
                pl.Erase();
            });
            foreach (var c in takim.Cikintilar)
            {
                KirisAcilimKesitSiniri(tr, ms, x + c.X0, x + c.X1, y + c.Ust, 0);
                // Uzun hatıl/perde altında temel üst yüzü (union'da kaybolur) kolonlar dışında çizilir.
                double bas = c.X0;
                foreach (var col in takim.Kolonlar.Where(k => k.X1 > c.X0 && k.X0 < c.X1).OrderBy(k => k.X0)
                    .Concat(new[] { new SurekliGorunusKolon { X0 = c.X1, X1 = c.X1 } }))
                {
                    if (col.X0 - bas > 0.5) KirisAcilimCizgi(tr, ms, x + bas, y + c.Alt, x + col.X0, y + c.Alt, "TEMEL (BEYKENT)");
                    bas = Math.Max(bas, col.X1);
                }
            }
            foreach (var col in takim.Kolonlar)
            {
                // Hatıl/perde çıkıntısının içinde kalan kolonun yüzleri union'da kaybolur.
                bool icinde = takim.Cikintilar.Any(c => c.X0 <= col.X0 + 0.1 && c.X1 >= col.X1 - 0.1
                    && Math.Abs(c.Ust - col.Ust) < 0.1 && Math.Abs(c.Alt - col.Alt) < 0.1);
                if (icinde)
                {
                    KirisAcilimCizgi(tr, ms, x + col.X0, y + col.Alt, x + col.X0, y + col.Ust, LayerKolon);
                    KirisAcilimCizgi(tr, ms, x + col.X1, y + col.Alt, x + col.X1, y + col.Ust, LayerKolon);
                }
                // Kolon yalnız bir kez: üzerine oturduğu parçanın bakış yönünde.
                else KirisAcilimKesitSiniri(tr, ms, x + col.X0, x + col.X1, y + col.Ust, 0);
                // Kolon adı üstte, filiz yazısı altta (kolon adı filiz varsa yukarı kalkar).
                bool filiz = SurekliGorunusKolonFiliz(tr, ms, db, floor, takim, col, x, y);
                KirisAcilimKolonYaz(tr, ms, db, floor, col.Kaynak.ColumnNo, x + (col.X0 + col.X1) * 0.5,
                    y + col.Ust + (filiz ? SurekliGorunusFilizliKolonAdiDy : 0.0), true);
            }
            foreach (var c in takim.Cikintilar)
            {
                // Etiket, çıkıntının kolonlarla örtülmeyen her boşluğuna (her temel kirişi açıklığı); yazı sığmıyorsa
                // yalnız en uzun boşluğa. Kapama perde 2× öncesi 5 (nihai 10 cm).
                const double minBosluk = 35.0;
                var bosluklar = new List<(double A, double B)>();
                double bas = c.X0;
                foreach (var col in takim.Kolonlar.Where(k => k.X1 > c.X0 && k.X0 < c.X1).OrderBy(k => k.X0)
                    .Concat(new[] { new SurekliGorunusKolon { X0 = c.X1, X1 = c.X1 } }))
                {
                    if (col.X0 - bas >= 1.0) bosluklar.Add((bas, col.X0));
                    bas = Math.Max(bas, col.X1);
                }
                if (bosluklar.Count == 0) continue;
                var yazilacak = bosluklar.Where(g => g.B - g.A >= minBosluk).ToList();
                if (yazilacak.Count == 0) yazilacak.Add(bosluklar.OrderByDescending(g => g.B - g.A).First());
                foreach (var g in yazilacak)
                    DrawBeamLabel(tr, ms, db, new Point3d(x + (g.A + g.B) * 0.5, y + c.Ust + 6.0, 0),
                        c.Perde ? "KAPAMA PERDE" : "TEMEL HATILI", c.Perde ? 5 : 4, 0,
                        c.Perde ? LayerPerdeYazisi : LayerTemelHatiliIsmi, useMiddleCenter: true);
            }
            foreach (var s in takim.Saplamalar)
            {
                // Saplanılan temelin izi: gövde içindeki yüzleri kesit görünüşü çizgisi.
                var body = s.Temel.Parcalar[0];
                double alt = Math.Max(s.Alt, body.Alt), ust = Math.Min(s.Ust, body.Ust);
                SurekliGorunusParcaAraligi(s.Temel, body.Plan, out double lo, out double hi);
                foreach (double face in new[] { s.X0, s.X1 })
                    if (face > lo + 0.5 && face < hi - 0.5 && ust - alt > 1.0)
                        KirisAcilimCizgi(tr, ms, x + face, y + alt, x + face, y + ust, LayerKesitGorunus);
            }
            foreach (var temel in takim.Temeller)
            {
                // Her kolon arası ayrı temel kirişi: aynı isim her açıklığa yazılır.
                var p = temel.Parcalar[0];
                string name = string.Format(CultureInfo.InvariantCulture, "T-{0} ({1:0.##}/{2:0.##})", temel.No,
                    temel.Kaynak.WidthCm, temel.Kaynak.HeightCm);
                var mesnet = SurekliGorunusMesnetleri(takim, temel);
                var araliklar = new List<(double Lo, double Hi)>();
                for (int i = 1; i < mesnet.Count; i++)
                    if (mesnet[i].X0 - mesnet[i - 1].X1 > 20.0) araliklar.Add((mesnet[i - 1].X1, mesnet[i].X0));
                if (araliklar.Count == 0)
                {
                    SurekliGorunusParcaAraligi(temel, p.Plan, out double lo, out double hi);
                    araliklar.Add((lo, hi));
                }
                // Ad görünüşün üstünde (kolon/perde çıkıntısı, kolon adı ve filiz yazısının üstü), 18 punto (2× öncesi 9).
                double adY = y + takim.Kolonlar.Select(c => c.Ust).Concat(takim.Cikintilar.Select(c => c.Ust))
                    .Concat(takim.Temeller.SelectMany(t => t.Parcalar).Where(q => !q.Cikinti).Select(q => q.Ust)).Max() + 34.0;
                foreach (var a in araliklar)
                    DrawBeamLabel(tr, ms, db, new Point3d(x + (a.Lo + a.Hi) * 0.5, adY, 0),
                        name, 9, 0, LayerTemelIsmi, useMiddleCenter: true);
            }
            var env = concrete.EnvelopeInternal;
            _surekliGorunusGenislik = Math.Max(_surekliGorunusGenislik, env.MaxX - x);
            ObjectId style = GetOrCreatePlanOlcuDimStyle(tr, db, 10, 1, "PLAN_OLCU_25");
            var faces = new List<double> { env.MinX, env.MaxX };
            foreach (var col in takim.Kolonlar) { faces.Add(x + col.X0); faces.Add(x + col.X1); }
            foreach (var s in takim.Saplamalar) { faces.Add(x + s.X0); faces.Add(x + s.X1); }
            faces.Sort();
            var distinct = new List<double>();
            foreach (double face in faces)
                if (distinct.Count == 0 || face - distinct[distinct.Count - 1] > 0.1) distinct.Add(face);
            // Temel kesitleriyle aynı: gövde altında 10 cm grobeton (AR-CONC) ve zemin çizgisi.
            var grobeton = new List<Geometry>();
            foreach (var temel in takim.Temeller)
                foreach (var p in temel.Parcalar.Where(p => !p.Cikinti))
                {
                    SurekliGorunusParcaAraligi(temel, p.Plan, out double lo, out double hi);
                    grobeton.Add(KirisAcilimRect(_ntsDrawFactory, x + lo, y + p.Alt - GrobetonUnderFoundationKesitHeightCm, x + hi, y + p.Alt));
                }
            var grobetonBirlesik = NtsOverlaySafe.CascadedUnionSafe(grobeton);
            if (grobetonBirlesik != null && !grobetonBirlesik.IsEmpty)
            {
                DrawGeometryRingsAsPolylines(tr, ms, grobetonBirlesik, LayerGrobeton, addHatch: true, hatchAngleRad: 0,
                    applySmallTriangleTrim: true, hatchPatternName: GrobetonHatchPatternName,
                    hatchPatternScale: GrobetonHatchPatternScale, hatchLayerOverride: LayerTarama);
                var ge = grobetonBirlesik.EnvelopeInternal;
                DrawZeminSingleLineAlongFoundationSection(tr, ms, ge.MinX, ge.MaxX, ge.MinY, ge.MaxY, true, false, SurekliTemelZeminCizgiUzantisiCm);
            }
            // Yüz ölçüleri grobeton altında; toplam ölçü yok.
            double olcuY = env.MinY - GrobetonUnderFoundationKesitHeightCm;
            for (int i = 1; i < distinct.Count; i++)
                AppendEntity(tr, ms, new RotatedDimension(0, new Point3d(distinct[i - 1], olcuY, 0),
                    new Point3d(distinct[i], olcuY, 0), new Point3d(distinct[i], olcuY - 25, 0), "", style)
                    { Layer = LayerOlcu, Dimlfac = 1, Dimscale = 1 });
        }
    }
}
