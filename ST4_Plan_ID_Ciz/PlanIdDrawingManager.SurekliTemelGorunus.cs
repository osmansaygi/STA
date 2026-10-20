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

        private List<SurekliGorunusTakim> BuildSurekliGorunusTakimlari(FloorInfo floor)
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
                        Ust = alt + cf.HeightCm + cf.HatilLabelHeightCm });
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
                        if (!kalan.Contains(other) || KirisAcilimHatAcisiDerece(u, other.Yon) > 25.0) continue;
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
                    double oturma = temas.Max(p => p.Ust);
                    takim.Kolonlar.Add(new SurekliGorunusKolon { Kaynak = kaynak.Kaynak, Plan = kaynak.Plan,
                        X0 = lo, X1 = hi, Alt = oturma, Ust = oturma + 30.0 });
                }
                takim.Kolonlar.Sort((a, b) => a.X0.CompareTo(b.X0));
                result.Add(takim);
            }
            result.Sort((a, b) => a.Temeller.Min(t => t.No).CompareTo(b.Temeller.Min(t => t.No)));
            return result;
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
                        || c.Plan.Distance(nodePoint) < 0.1)).ToList();
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
            var takimlar = BuildSurekliGorunusTakimlari(floor);
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
                    double cursor = insert.Y;
                    foreach (var takim in takimlar)
                    {
                        long start = KirisYerlesimSonrakiHandle(db);
                        double alt = takim.Temeller.SelectMany(t => t.Parcalar).Min(p => p.Alt);
                        double ust = Math.Max(takim.Temeller.SelectMany(t => t.Parcalar).Max(p => p.Ust),
                            takim.Kolonlar.Count == 0 ? alt : takim.Kolonlar.Max(c => c.Ust));
                        double shiftY = cursor - alt;
                        DrawSurekliGorunusTakim(tr, ms, db, floor, takim, insert.X, shiftY);
                        DrawSurekliGorunusDonati(tr, ms, db, floor, takim, insert.X, shiftY, donati);
                        // Açılım satırlarının gerçek yüksekliğiyle yer ayır;
                        // yeni donatı satırları alt/üst takıma taşmasın.
                        double low = cursor - 50, high = shiftY + ust;
                        var teamEntities = new List<Entity>();
                        foreach (ObjectId id in ms)
                        {
                            if (id.IsErased || id.Handle.Value < start) continue;
                            var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            if (entity == null) continue;
                            teamEntities.Add(entity);
                            try
                            {
                                var bounds = entity.GeometricExtents;
                                low = Math.Min(low, bounds.MinPoint.Y); high = Math.Max(high, bounds.MaxPoint.Y);
                                _surekliGorunusGenislik = Math.Max(_surekliGorunusGenislik, bounds.MaxPoint.X - insert.X);
                            }
                            catch (Autodesk.AutoCAD.Runtime.Exception) { }
                        }
                        double dy = cursor - low;
                        var move = Matrix3d.Displacement(new Vector3d(0, dy, 0));
                        foreach (var entity in teamEntities)
                        {
                            if (!entity.IsWriteEnabled) entity.UpgradeOpen();
                            entity.TransformBy(move);
                        }
                        if (_kirisYerlesim != null)
                            _kirisYerlesim.Takimlar.Add(new KirisYerlesimTakim { Floor = floor,
                                No = takim.Temeller.Min(t => t.No), StartHandle = start,
                                EndHandle = KirisYerlesimSonrakiHandle(db),
                                BeamTop = shiftY + dy + takim.Temeller.SelectMany(t => t.Parcalar).Max(p => p.Ust) });
                        cursor += high - low + 80.0;
                    }
                    long first = _kirisYerlesim != null ? _kirisYerlesim.Takimlar.Min(t => t.StartHandle) : 0;
                    // Antetsiz çizimde de sadece bu komutun yeni nesneleri ölçeklenir.
                    if (_kirisYerlesim == null) first = _surekliGorunusIlkHandle;
                    var scale = Matrix3d.Scaling(2.0, insert);
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased || id.Handle.Value < first) continue;
                        var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (ent == null) continue;
                        ent.TransformBy(scale); ids.Add(id);
                        if (ent is Autodesk.AutoCAD.DatabaseServices.Dimension dim) { dim.Dimlfac = 0.5; dim.Dimscale = 1; }
                    }
                    tr.Commit();
                    if (_kirisYerlesim == null)
                        KirisAcilimGorunumEkle(new Extents3d(new Point3d(insert.X - 100, insert.Y - 150, 0),
                            new Point3d(insert.X + 2 * _surekliGorunusGenislik + 150, insert.Y + 2 * (cursor - insert.Y), 0)));
                }
                if (_kirisYerlesim != null) KirisAcilimAnteteYerlestirKatKat(db, ed, insert, path, ids);
                ed?.WriteMessage("\nSUREKLITEMELACILIMLARI: {0} temel açılımı. Donatı, etriye ve kesitler açık; düzeltilmiş beton ve kolon konumları korundu.", takimlar.Count);
                if (donati.Count == 0) ed?.WriteMessage("\nTMX/GPR donatı kaydı bulunamadı; donatı adedi ve çapı üretilmedi.");
                return true;
            }
            finally { _kirisYerlesim = null; _surekliGorunusIlkHandle = 0; _surekliGorunusGenislik = 0; }
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
                    SurekliGorunusParcaAraligi(temel, p.Plan, out double lo, out double hi);
                    bodies.Add(KirisAcilimRect(_ntsDrawFactory, x + lo, y + p.Alt, x + hi, y + p.Ust));
                }
            var concrete = NtsOverlaySafe.CascadedUnionSafe(bodies);
            var profileParts = new List<Geometry> { concrete };
            foreach (var col in takim.Kolonlar)
                profileParts.Add(KirisAcilimRect(_ntsDrawFactory, x + col.X0, y + col.Alt, x + col.X1, y + col.Ust));
            var profile = NtsOverlaySafe.CascadedUnionSafe(profileParts);
            var cuts = takim.Kolonlar.Select(c => new KirisAcilimKolonTrim { X0 = x + c.X0, X1 = x + c.X1,
                YTop = y + c.Ust, TrimTop = true }).ToList();
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
                        bool columnSide = takim.Kolonlar.Any(c => Math.Abs(bit.A.X - bit.B.X) < 0.01
                            && (Math.Abs(bit.A.X - x - c.X0) < 0.1 || Math.Abs(bit.A.X - x - c.X1) < 0.1)
                            && Math.Min(bit.A.Y, bit.B.Y) >= y + c.Alt - 0.1 && Math.Max(bit.A.Y, bit.B.Y) <= y + c.Ust + 0.1);
                        KirisAcilimCizgi(tr, ms, bit.A.X, bit.A.Y, bit.B.X, bit.B.Y, columnSide ? LayerKolon : "TEMEL (BEYKENT)");
                    }
                }
                pl.Erase();
            });
            foreach (var col in takim.Kolonlar)
            {
                // Kolon yalnız bir kez: üzerine oturduğu parçanın bakış yönünde.
                KirisAcilimKesitSiniri(tr, ms, x + col.X0, x + col.X1, y + col.Ust, 0);
                KirisAcilimKolonYaz(tr, ms, db, floor, col.Kaynak.ColumnNo, x + (col.X0 + col.X1) * 0.5, y + col.Ust, true);
            }
            foreach (var temel in takim.Temeller)
            {
                var p = temel.Parcalar[0];
                SurekliGorunusParcaAraligi(temel, p.Plan, out double lo, out double hi);
                string name = string.Format(CultureInfo.InvariantCulture, "T-{0} ({1:0.##}/{2:0.##})", temel.No,
                    temel.Kaynak.WidthCm, temel.Kaynak.HeightCm);
                DrawBeamLabel(tr, ms, db, new Point3d(x + (lo + hi) * 0.5, y + (p.Alt + p.Ust) * 0.5, 0),
                    name, 5, 0, LayerTemelIsmi, useMiddleCenter: true);
            }
            var env = concrete.EnvelopeInternal;
            _surekliGorunusGenislik = Math.Max(_surekliGorunusGenislik, env.MaxX - x);
            ObjectId style = GetOrCreatePlanOlcuDimStyle(tr, db, 10, 1, "PLAN_OLCU_25");
            var faces = new List<double> { env.MinX, env.MaxX };
            foreach (var col in takim.Kolonlar) { faces.Add(x + col.X0); faces.Add(x + col.X1); }
            faces.Sort();
            var distinct = new List<double>();
            foreach (double face in faces)
                if (distinct.Count == 0 || face - distinct[distinct.Count - 1] > 0.1) distinct.Add(face);
            for (int i = 1; i < distinct.Count; i++)
                AppendEntity(tr, ms, new RotatedDimension(0, new Point3d(distinct[i - 1], env.MinY, 0),
                    new Point3d(distinct[i], env.MinY, 0), new Point3d(distinct[i], env.MinY - 25, 0), "", style)
                    { Layer = LayerOlcu, Dimlfac = 1, Dimscale = 1 });
            AppendEntity(tr, ms, new RotatedDimension(0, new Point3d(env.MinX, env.MinY, 0),
                new Point3d(env.MaxX, env.MinY, 0), new Point3d(env.MaxX, env.MinY - 45, 0), "", style)
                { Layer = LayerOlcu, Dimlfac = 1, Dimscale = 1 });
        }
    }
}
