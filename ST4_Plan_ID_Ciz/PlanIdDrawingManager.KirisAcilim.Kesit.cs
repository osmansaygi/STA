using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        // Yerel kesit koordinatları; çizim sonunda 2x büyütüldüğünden 10 birim = nihai 20 cm.
        private static List<(double X0, double Y0, double X1, double Y1, double Lane)> KirisKesitYanOlculeri(
            double w, double h, (double Ust, double Alt)? sol, (double Ust, double Alt)? sag)
        {
            var result = new List<(double, double, double, double, double)>();
            double Kontur(double face, double tip, (double Ust, double Alt)? slab, double yy)
                => slab.HasValue && yy >= slab.Value.Alt - 1e-7 && yy <= slab.Value.Ust + 1e-7 ? tip : face;
            void Zincir(bool left, (double Ust, double Alt)? slab)
            {
                if (!slab.HasValue) return;
                double face = left ? 0 : w, tip = face + (left ? -15 : 15);
                double lane = tip + (left ? -10 : 10);
                var levels = new[] { -h, Math.Max(-h, Math.Min(0, slab.Value.Alt)),
                    Math.Max(-h, Math.Min(0, slab.Value.Ust)), 0.0 }.Distinct().OrderBy(v => v).ToList();
                for (int i = 1; i < levels.Count; i++)
                {
                    double a = levels[i - 1], b = levels[i];
                    if (b - a < 1e-7) continue;
                    result.Add((Kontur(face, tip, slab, a), a, Kontur(face, tip, slab, b), b, lane));
                }
            }
            Zincir(true, sol); Zincir(false, sag);
            result.Add((Kontur(w, w + 15, sag, -h), -h, Kontur(w, w + 15, sag, 0), 0,
                sag.HasValue ? w + 35 : w + 10));
            return result;
        }

        private sealed class KirisKesitDonati
        {
            public List<Point2d> Yol;
            public int Adet, Cap;
            public string Tur;
        }
        private sealed class KirisKesitSira
        {
            public int Adet, Cap;
            public string Tur;
            public double Derinlik;
        }
        private sealed class KirisKesitCubuk
        {
            public KirisKesitSira Donati;
            public Point2d Merkez;
            public int Sira;
        }

        private static List<double> KirisKesitSiraXleri(double b, int count, double diameter, List<double> fixedXs = null, double cover = 5)
        {
            var anchors = fixedXs == null
                ? KirisKesitEtriyeKutulari(b, cover - 1).SelectMany(v => new[] { v.Lo + 1, v.Hi - 1 }).OrderBy(v => v).ToList()
                : fixedXs.Concat(new[] { cover, b - cover }).Distinct().OrderBy(v => v).ToList();
            if (count <= 0 || anchors.Count == 0) return new List<double>();
            double pitch = 4.0 + diameter;
            if (count < anchors.Count)
            {
                int n = Math.Min(count, Math.Max(1, (int)Math.Floor((b - 2 * cover) / pitch + 1e-8) + 1));
                return Enumerable.Range(0, n).Select(i => n == 1 ? cover : cover + (b - 2 * cover) * i / (n - 1)).ToList();
            }
            var extra = new int[anchors.Count - 1];
            int placed = anchors.Count;
            while (placed < count)
            {
                int best = -1; double bestStep = 0;
                for (int i = 0; i < extra.Length; i++)
                {
                    double step = (anchors[i + 1] - anchors[i]) / (extra[i] + 2);
                    if (step >= pitch - 1e-8 && step > bestStep) { best = i; bestStep = step; }
                }
                if (best < 0) break;
                extra[best]++; placed++;
            }
            var xs = new List<double> { anchors[0] };
            for (int i = 0; i < extra.Length; i++)
            {
                for (int j = 1; j <= extra[i]; j++) xs.Add(anchors[i] + (anchors[i + 1] - anchors[i]) * j / (extra[i] + 1));
                xs.Add(anchors[i + 1]);
            }
            return xs;
        }

        private static List<KirisKesitCubuk> KirisKesitCubukYerlesimi(double b, double h, List<KirisKesitSira> bars, bool monUst = true, double cover = 5)
        {
            var result = new List<KirisKesitCubuk>();
            foreach (bool top in new[] { true, false })
            {
                // Kiriş: mon üst / düz alt. Sürekli temel: mon alt / düz üst.
                var pending = bars.Where(r =>
                    {
                        if (r.Tur == "gov") return false;
                        bool faceTop = r.Tur == "mon" ? monUst
                            : r.Tur == "duz" ? !monUst
                            : r.Derinlik < h * 0.5;
                        return faceTop == top;
                    })
                    .OrderBy(r => r.Tur == "mon" || r.Tur == "duz" ? 0 : 1)
                    .SelectMany(r => Enumerable.Repeat(r, Math.Max(0, r.Adet))).ToList();
                double depth = cover, previousDia = 0;
                int row = 0;
                while (pending.Count > 0)
                {
                    double maxDia = pending.Max(r => r.Cap) / 10.0;
                    int mainCount = pending.Count(r => r.Tur == "mon" || r.Tur == "duz");
                    var mainXs = mainCount > 0 ? KirisKesitSiraXleri(b, mainCount, maxDia, cover: cover) : new List<double>();
                    var xs = mainCount > 0 && mainXs.Count == mainCount && pending.Count > mainCount
                        ? KirisKesitSiraXleri(b, pending.Count, maxDia, mainXs, cover)
                        : KirisKesitSiraXleri(b, pending.Count, maxDia, cover: cover);
                    if (xs.Count == 0) break;
                    // Tek taşan çubuk yalnız sol köşeye düşmesin: 1. sıradan bir eksilt, 2. sırada iki köşe.
                    if (row == 0 && pending.Count > xs.Count && pending.Count - xs.Count == 1 && xs.Count >= 3)
                    {
                        int want = xs.Count - 1;
                        xs = mainCount > 0 && mainXs.Count == mainCount && want > mainCount
                            ? KirisKesitSiraXleri(b, want, maxDia, mainXs, cover)
                            : KirisKesitSiraXleri(b, want, maxDia, cover: cover);
                        if (xs.Count == 0) break;
                    }
                    var corners = KirisKesitEtriyeKutulari(b, cover - 1).SelectMany(v => new[] { v.Lo + 1, v.Hi - 1 }).ToList();
                    // Önce montaj/düz çubuklar tüm etriye köşelerini doldurur.
                    var slots = xs.OrderBy(xx => mainXs.Any(c => Math.Abs(c - xx) < 1e-7) ? 0 : 1)
                        .ThenBy(xx => corners.Any(c => Math.Abs(c - xx) < 1e-7) ? 0 : 1).ThenBy(xx => xx).ToList();
                    var selected = pending.Take(slots.Count).ToList();
                    double rowDia = selected.Max(r => r.Cap) / 10.0;
                    if (row > 0) depth += previousDia * 0.5 + 3.0 + rowDia * 0.5;
                    for (int i = 0; i < selected.Count; i++)
                        result.Add(new KirisKesitCubuk { Donati = selected[i], Sira = row,
                            Merkez = new Point2d(slots[i], top ? -depth : -h + depth) });
                    pending.RemoveRange(0, selected.Count);
                    previousDia = rowDia; row++;
                }
            }
            foreach (var web in bars.Where(r => r.Tur == "gov"))
                foreach (var p in KirisKesitCubukMerkezleri(b, h, web.Adet, web.Tur, web.Derinlik, cover))
                    result.Add(new KirisKesitCubuk { Donati = web, Merkez = p, Sira = 0 });
            return result;
        }
        private sealed class KirisKesitBilgi
        {
            public string Ad;
            public double B, H, Kot, X, Y;
            public int Etriye;
            public string AdetNotu;
            public (double Ust, double Alt)? Sol, Sag;
            public Geometry TemelKesit;
            public List<KirisKesitSira> Siralar = new List<KirisKesitSira>();
        }
        private readonly List<KirisKesitDonati> _kirisKesitDonatilari = new List<KirisKesitDonati>();
        private readonly List<KirisKesitBilgi> _kirisKesitler = new List<KirisKesitBilgi>();
        private readonly Dictionary<BeamInfo, string> _kirisKesitAdetNotlari = new Dictionary<BeamInfo, string>();
        private readonly List<(SlabInfo Slab, Geometry Poly)> _kirisKesitDosemeler = new List<(SlabInfo, Geometry)>();

        private void KirisAcilimKesitBaslat(FloorInfo floor)
        {
            _ntsDrawFactory = _ntsDrawFactory ?? NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
            _kirisKesitler.Clear();
            _kirisKesitDosemeler.Clear();
            if (_model.Slabs == null) return;
            foreach (var slab in _model.Slabs)
            {
                if (GetSlabFloorNo(slab.SlabId) != floor.FloorNo) continue;
                var poly = NtsOverlaySafe.PrepareForOverlay(SlabFootprintPoly(slab));
                if (poly != null && !poly.IsEmpty) _kirisKesitDosemeler.Add((slab, poly));
            }
        }

        private void KirisAcilimKesitDonatiKaydet(List<Point2d> pts, int adet, int cap, string tur)
        {
            if (pts == null || pts.Count < 2 || adet <= 0 || cap <= 0) return;
            _kirisKesitDonatilari.Add(new KirisKesitDonati { Yol = new List<Point2d>(pts), Adet = adet, Cap = cap, Tur = tur });
        }

        private void KirisAcilimKesitTakimBaslat()
        {
            _kirisKesitler.Clear();
            _kirisKesitDonatilari.Clear();
        }

        private static double KirisKesitSonrakiX(double x, double b)
        {
            // Gerçek sağ ölçü ve sol açıklama payları; arada nihai yaklaşık 50 cm.
            return x + b + 38.0 + 19.0 + 50.0 / 2.0;
        }

        private static double KirisKesitKartYuksekligi(double h, double slabRise, int names)
        {
            return 2.0 * h + 230.0 + Math.Max(0, slabRise) + names * 8.0;
        }

        // Çizimden önce üst sınır ayır: benzerlik birleşince gereken alan yalnız küçülür.
        // Yeni DBText/Dimension/Hatch nesnelerini extents okuyarak yeniden taşımaya gerek kalmaz.
        private static double KirisKesitYerlesimAlti(double minKot, double h, double slabRise, int count)
        {
            return minKot - KirisKesitKartYuksekligi(h, slabRise, count);
        }

        private static double KirisKesitIstasyon(double yuz0, double yuz1, bool konsol = false) => yuz0 + (yuz1 - yuz0) * (konsol ? 0.5 : 2.0 / 3.0);

        /// <summary>Montaj bindirme bölgesine denk gelirse kesiti sola kaydır (10–15 cm adım).</summary>
        private static double KirisKesitIstasyonBindirmedenKac(
            double yuz0, double yuz1, double station, Func<double, int> montajKesAdedi)
        {
            if (montajKesAdedi == null || yuz1 - yuz0 < 40.0) return station;
            const double adim = 12.5;
            double minS = yuz0 + (yuz1 - yuz0) * 0.15;
            double s = station;
            int guard = 0;
            while (s > minS + 0.5 && montajKesAdedi(s) >= 2 && guard++ < 20)
                s -= adim;
            if (s < minS) s = minS;
            return s;
        }

        private int KirisKesitMontajKesAdedi(double xWorld)
        {
            int n = 0;
            foreach (var don in _kirisKesitDonatilari)
            {
                if (don == null || don.Tur != "mon") continue;
                if (KirisKesitYolKes(don.Yol, xWorld, out _)) n++;
            }
            return n;
        }

        private static string KirisKesitEtriyeCarpan(int count)
        {
            return count > 1 ? count.ToString(CultureInfo.InvariantCulture) + "x" : "";
        }

        private void KirisAcilimKotlariCiz(Transaction tr, BlockTableRecord ms, Database db, FloorInfo floor,
            List<KirisAcilimSpan> chain, KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus, double x0, double yShift)
        {
            for (int i0 = 0; i0 < chain.Count;)
            {
                int i1 = i0;
                while (i1 + 1 < chain.Count && chain[i1 + 1].Beam.BeamId == chain[i0].Beam.BeamId) i1++;
                KirisAcilimKolonYuzu(chain, i0, i1, sup, out double a, out double b);
                KirisAcilimNumaraAraligi(chain, i0, i1, a, b, gorunus, out a, out b);
                double station = a + (b - a) * 0.75;
                int j = i0;
                while (j < i1 && station > chain[j].X1) j++;
                double top = KirisAcilimKotAt(chain, j, j, station);
                const double scale = 0.5;
                DrawKesitKotClassicSymbol(tr, ms, x0 + station, yShift + top, 0, scale);
                AppendKesitKotElevationDbText(tr, ms, db, GetOrCreateYaziBeykentTextStyle(tr, db),
                    FormatKesitKotElevationString((_model.BuildingBaseKotu + floor.ElevationM) * 100.0 + top),
                    x0 + station + KesitKotTriHalfWidthCm * scale,
                    yShift + top + (KesitKotTriHeightCm + KesitKotTextAboveExtensionCm) * scale, 0, KesitKotTextHeightCm * scale);
                i0 = i1 + 1;
            }
        }

        private static List<(double Lo, double Hi)> KirisKesitEtriyeKutulari(double b, double cover = 4)
        {
            double width = b - 2 * cover;
            var boxes = new List<(double Lo, double Hi)>();
            if (width <= 2.0) return boxes;
            int count = 1;
            while (width / (2 * count - 1) > 35.0 + 1e-8) count++;
            double step = width / (2 * count - 1);
            for (int i = 0; i < count; i++) boxes.Add((cover + i * step, cover + (i + count) * step));
            return boxes;
        }

        private void KirisKesitMontajHazirla(Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj)
        {
            _kirisKesitAdetNotlari.Clear();
            foreach (var beam in montaj.Keys.ToList())
            {
                var m = montaj[beam];
                if (m.EtriyeCap <= 0) continue;
                int need = 2 * KirisKesitEtriyeKutulari(beam.WidthCm).Count;
                string note = "";
                if (m.Cap > 0 && m.Adet > 0 && m.Adet < need)
                { note = "MON " + m.Adet + "→" + need; m.Adet = need; }
                if (m.DuzCap > 0 && m.DuzAdet > 0 && m.DuzAdet < need)
                { note += (note.Length == 0 ? "" : ", ") + "DUZ " + m.DuzAdet + "→" + need; m.DuzAdet = need; }
                if (note.Length > 0) _kirisKesitAdetNotlari[beam] = note;
                montaj[beam] = m;
            }
        }

        private static (double Ust, double Alt) KirisKesitDosemeKotlari(double katKotCm, double offsetCm, double kalinlikCm, double kirisKotCm)
        {
            double ust = katKotCm + offsetCm - kirisKotCm;
            return (ust, ust - kalinlikCm);
        }

        private static bool KirisKesitYolKes(List<Point2d> pts, double x, out double y)
        {
            y = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1]; var b = pts[i];
                if (Math.Abs(b.X - a.X) < 0.001) continue; // Uç gönyesini boyuna çubuk sayma.
                if (x < Math.Min(a.X, b.X) - 0.001 || x > Math.Max(a.X, b.X) + 0.001) continue;
                y = a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X);
                return true;
            }
            return false;
        }

        private void KirisAcilimKesitTopla(FloorInfo floor, List<KirisAcilimSpan> chain,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj, double x0, double yShift, int pad,
            Dictionary<BeamInfo, KirisAcilimUcBilgi> ucTipi)
        {
            for (int i0 = 0; i0 < chain.Count;)
            {
                int i1 = i0;
                while (i1 + 1 < chain.Count && chain[i1 + 1].Beam.BeamId == chain[i0].Beam.BeamId) i1++;
                KirisAcilimKolonYuzu(chain, i0, i1, sup, out double a, out double b);
                KirisAcilimNumaraAraligi(chain, i0, i1, a, b, gorunus, out a, out b);
                KirisAcilimKonsolUclari(chain, i0, i1, ucTipi, gorunus, x0, out bool solSerbest, out bool sagSerbest, out _, out _);
                double s = KirisKesitIstasyon(a, b, solSerbest || sagSerbest);
                s = KirisKesitIstasyonBindirmedenKac(a, b, s, local => KirisKesitMontajKesAdedi(x0 + local));
                int j = i0;
                while (j < i1 && s > chain[j].X1) j++;
                var span = chain[j];
                double top = yShift + KirisAcilimKotAt(chain, j, j, s);
                var info = new KirisKesitBilgi { B = span.Beam.WidthCm, H = span.Beam.HeightCm,
                    Kot = (_model.BuildingBaseKotu + floor.ElevationM) * 100.0 + top - yShift,
                    X = x0 + s, Y = top, Ad = KirisAcilimEtiket(span.Beam, pad) };
                if (montaj != null && montaj.TryGetValue(span.Beam, out var m)) info.Etriye = m.EtriyeCap;
                _kirisKesitAdetNotlari.TryGetValue(span.Beam, out info.AdetNotu);
                foreach (var don in _kirisKesitDonatilari)
                {
                    if (!KirisKesitYolKes(don.Yol, x0 + s, out double y)) continue;
                    double derinlik = top - y;
                    if (derinlik < 4.0 || derinlik > info.H - 4.0) continue;
                    info.Siralar.Add(new KirisKesitSira { Adet = don.Adet, Cap = don.Cap, Tur = don.Tur, Derinlik = derinlik });
                }
                // World0/1 aks üzerinde; kesit, gerçek kaçıklıklı gövdenin iki yüzünden döşemeye bakar.
                double u = (s - span.X0) / Math.Max(0.001, span.X1 - span.X0);
                var dir = (span.World1 - span.World0).GetNormal();
                var perp = new Vector2d(-dir.Y, dir.X);
                var p = span.World0 + (span.World1 - span.World0) * u;
                if (KirisAcilimBeamEkseni(span.Beam, out _, out _, out _, out _, out _, out var fizikselPerp))
                    p += fizikselPerp * KirisAcilimKaciklikMerkez(span.Beam);
                (double Ust, double Alt)? Doseme(double sign)
                {
                    var probe = p + perp * (sign * (info.B * 0.5 + 1.0));
                    var pt = _ntsDrawFactory.CreatePoint(new Coordinate(probe.X, probe.Y));
                    (double Ust, double Alt)? best = null;
                    foreach (var entry in _kirisKesitDosemeler)
                    {
                        if (!entry.Poly.Covers(pt)) continue;
                        var kot = KirisKesitDosemeKotlari((_model.BuildingBaseKotu + floor.ElevationM) * 100.0,
                            entry.Slab.OffsetFromFloorCm, entry.Slab.ThicknessCm, info.Kot);
                        double ust = kot.Ust, alt = kot.Alt;
                        if (entry.Slab.ThicknessCm <= 0 || alt >= 0 || ust <= -info.H) continue;
                        if (!best.HasValue || ust > best.Value.Ust) best = (ust, alt);
                    }
                    return best;
                }
                info.Sol = Doseme(-1); info.Sag = Doseme(1);
                if (_surekliTemelAcilim && _surekliTemelCfByBeam != null
                    && _surekliTemelCfByBeam.TryGetValue(span.Beam, out var temel))
                {
                    info.TemelKesit = BuildSurekliTemelEnKesit(temel);
                    info.Sol = info.Sag = null;
                }
                if (b > a && info.B > 11 && info.H > 11) _kirisKesitler.Add(info);
                i0 = i1 + 1;
            }
        }

        private static string KirisKesitHarf(int index)
        {
            string result = "";
            do { result = (char)('A' + index % 26) + result; index = index / 26 - 1; } while (index >= 0);
            return result;
        }

        private static List<Point2d> KirisKesitCubukMerkezleri(double b, double h, int n, string tur, double derinlik, double cover = 5)
        {
            var pts = new List<Point2d>();
            if (b <= 11 || h <= 11 || n <= 0) return pts;
            var xs = new List<double>();
            if (tur == "mon" || tur == "duz")
            {
                foreach (var box in KirisKesitEtriyeKutulari(b)) { xs.Add(box.Lo + 1.0); xs.Add(box.Hi - 1.0); }
                xs.Sort();
                if (n < xs.Count) xs.Clear();
                if (xs.Count > 0 && xs.Count < n)
                {
                    var extra = new int[xs.Count - 1];
                    for (int k = xs.Count; k < n; k++)
                    {
                        int gap = 0;
                        for (int i = 1; i < extra.Length; i++)
                            if ((xs[i + 1] - xs[i]) / (extra[i] + 1) > (xs[gap + 1] - xs[gap]) / (extra[gap] + 1) + 1e-8) gap = i;
                        extra[gap]++;
                    }
                    var spread = new List<double> { xs[0] };
                    for (int i = 0; i < extra.Length; i++)
                    {
                        for (int j = 1; j <= extra[i]; j++) spread.Add(xs[i] + (xs[i + 1] - xs[i]) * j / (extra[i] + 1));
                        spread.Add(xs[i + 1]);
                    }
                    xs = spread;
                }
            }
            for (int j = 0; j < n; j++)
            {
                if (tur == "gov")
                    pts.Add(new Point2d(j % 2 == 0 ? cover : b - cover, -cover - (h - 2 * cover) * (j / 2 + 1) / ((n + 1) / 2 + 1)));
                else
                {
                    double d = tur == "mon" ? 5.0 : tur == "duz" ? h - 5.0
                        : derinlik + (derinlik < h * 0.5 ? -0.5 : 0.5);
                    pts.Add(new Point2d(xs.Count == n ? xs[j] : 5.0 + (b - 10) * (n == 1 ? 0.5 : (double)j / (n - 1)), -d));
                }
            }
            return pts;
        }

        private static string KirisKesitImza(KirisKesitBilgi k)
        {
            string N(double n) => Math.Round(n, 2).ToString("0.##", CultureInfo.InvariantCulture);
            string S((double Ust, double Alt)? s) => s.HasValue ? N(s.Value.Ust) + ":" + N(s.Value.Alt) : "-";
            return N(k.B) + "/" + N(k.H) + "/" + N(k.Kot) + "/" + k.Etriye + "/" + S(k.Sol) + "/" + S(k.Sag) + "/" + k.TemelKesit?.AsText()
                + "/" + string.Join(";", k.Siralar.GroupBy(r => r.Tur + ":" + r.Cap + ":" + N(r.Derinlik))
                    .Select(g => g.Key + ":" + g.Sum(r => r.Adet)).OrderBy(t => t, StringComparer.Ordinal));
        }

        private double KirisAcilimKesitleriCiz(Transaction tr, BlockTableRecord ms, Database db, double x, double y)
        {
            var groups = _kirisKesitler.GroupBy(KirisKesitImza).Select(g => g.ToList()).ToList();
            if (groups.Count == 0) return y;
            double slabRise = groups.Max(g => Math.Max(g[0].Sol?.Ust ?? 0, g[0].Sag?.Ust ?? 0));
            double sectionX = x;
            for (int i = 0; i < groups.Count; i++)
            {
                string harf = KirisKesitHarf(i);
                foreach (var cut in groups[i])
                {
                    // Mevcut konuma nihai +45/-55 cm ek taşıma; çizim sonunda 2× ölçeklenir.
                    DrawKesitEtiketDxfStyle(tr, ms, db, new Point3d(cut.X, cut.Y + (10.0 + 45.0) / 2.0, 0),
                        new Vector2d(0, 1), new Vector2d(-1, 0), false, harf, 0.3, fullLetter: true);
                    DrawKesitEtiketDxfStyle(tr, ms, db, new Point3d(cut.X, cut.Y - cut.H - (20.0 + 55.0) / 2.0, 0),
                        new Vector2d(0, 1), new Vector2d(-1, 0), true, harf, 0.3, fullLetter: true);
                }
                var section = groups[i][0];
                double leftInset = Math.Min(0, section.TemelKesit?.EnvelopeInternal.MinX ?? 0);
                KirisAcilimTekKesitCiz(tr, ms, db, section, sectionX - leftInset, y, harf,
                    groups[i].Select(k => k.Ad + (string.IsNullOrEmpty(k.AdetNotu) ? "" : " (" + k.AdetNotu + ")")).Distinct().ToList());
                sectionX = KirisKesitSonrakiX(sectionX, Math.Max(section.B, section.TemelKesit?.EnvelopeInternal.Width ?? section.B));
            }
            return y + Math.Max(0, slabRise) + 35.0;
        }

        private void KirisAcilimTekKesitCiz(Transaction tr, BlockTableRecord ms, Database db,
            KirisKesitBilgi k, double x, double y, string harf, List<string> adlar)
        {
            double w = k.B, h = k.H;
            Geometry Rect(double l, double r, double bot, double top) => _ntsDrawFactory.CreatePolygon(new[] {
                new Coordinate(l, bot), new Coordinate(r, bot), new Coordinate(r, top), new Coordinate(l, top), new Coordinate(l, bot) });
            var rects = new List<Geometry> { Rect(x, x + w, y - h, y) };
            if (k.Sol.HasValue) rects.Add(Rect(x - 15, x, y + k.Sol.Value.Alt, y + k.Sol.Value.Ust));
            if (k.Sag.HasValue) rects.Add(Rect(x + w, x + w + 15, y + k.Sag.Value.Alt, y + k.Sag.Value.Ust));
            var shape = TryCascadedPolygonUnionSafe(rects);
            void Outline(Geometry g)
            {
                if (g is Polygon poly)
                {
                    var c = poly.ExteriorRing.Coordinates;
                    for (int i = 1; i < c.Length; i++)
                    {
                        if (Math.Abs(c[i].X - c[i - 1].X) < 0.001 &&
                            (Math.Abs(c[i].X - (x - 15)) < 0.001 || Math.Abs(c[i].X - (x + w + 15)) < 0.001)) continue;
                        KirisAcilimCizgi(tr, ms, c[i - 1].X, c[i - 1].Y, c[i].X, c[i].Y, LayerKiris);
                    }
                }
                else if (g is GeometryCollection col) for (int i = 0; i < col.NumGeometries; i++) Outline(col.GetGeometryN(i));
            }
            if (k.TemelKesit != null)
                DrawGeometryRingsAsPolylines(tr, ms,
                    NetTopologySuite.Geometries.Utilities.AffineTransformation.TranslationInstance(x, y).Transform(k.TemelKesit), LayerKiris);
            else if (shape != null) Outline(shape);
            void SlabEnd((double Ust, double Alt)? slab, double at)
            {
                if (!slab.HasValue) return;
                // Finiş = döşeme kalınlığı; yukarı/aşağı taşmasın.
                KirisAcilimCizgi(tr, ms, at, y + slab.Value.Alt, at, y + slab.Value.Ust, LayerKesitSiniri);
            }
            SlabEnd(k.Sol, x - 15); SlabEnd(k.Sag, x + w + 15);
            ObjectId stil = GetOrCreatePlanOlcuDimStyle(tr, db, 10, 1, "PLAN_OLCU_25");
            double ampLo = k.TemelKesit?.EnvelopeInternal.MinX ?? 0, ampHi = k.TemelKesit?.EnvelopeInternal.MaxX ?? w;
            // Alt en ölçüsü: 2× öncesi 22.5 → nihai 45 cm (eski 30 → 60).
            AppendEntity(tr, ms, new RotatedDimension(0, new Point3d(x + ampLo, y - h, 0), new Point3d(x + ampHi, y - h, 0), new Point3d(x, y - h - 22.5, 0), "", stil) { Layer = LayerOlcu });
            if (ampHi - ampLo > w + 0.1)
            {
                AppendEntity(tr, ms, new RotatedDimension(0, new Point3d(x, y, 0), new Point3d(x + w, y, 0),
                    new Point3d(x, y + 22.5, 0), "", stil) { Layer = LayerOlcu });
                double step = k.TemelKesit.Coordinates.Where(c => c.X < -0.1 || c.X > w + 0.1).Max(c => c.Y);
                foreach (var pair in new[] { (A: -h, B: step), (A: step, B: 0.0) })
                    AppendEntity(tr, ms, new RotatedDimension(Math.PI / 2, new Point3d(x + ampLo, y + pair.A, 0),
                        new Point3d(x + ampLo, y + pair.B, 0), new Point3d(x + ampLo - 12, y, 0), "", stil) { Layer = LayerOlcu });
            }
            foreach (var dim in KirisKesitYanOlculeri(w, h, k.Sol, k.Sag))
            {
                AppendEntity(tr, ms, new RotatedDimension(Math.PI / 2,
                    new Point3d(x + dim.X0, y + dim.Y0, 0), new Point3d(x + dim.X1, y + dim.Y1, 0),
                    new Point3d(x + (k.TemelKesit != null ? Math.Max(dim.Lane, ampHi + 10) : dim.Lane), y, 0), "", stil) { Layer = LayerOlcu });
            }
            Text(x + w * 0.5, y - h - 36, harf + " - " + harf + " KESİTİ", LayerKesitIsmi);
            for (int i = 0; i < adlar.Count; i++) Text(x + w * 0.5, y - h - 48 - i * 8, adlar[i], LayerKirisYazisi);
            void Text(double xx, double yy, string s, string layer = KirisAcilimDonatiYaziLayer)
                => DrawBeamLabel(tr, ms, db, new Point3d(xx, yy, 0), s, 5, 0, layer, useMiddleCenter: true);
            void Leader(List<Point2d> points, double lineY, string label, bool web, double textDy = 0)
            {
                if (points.Count == 0) return;
                var left = points.OrderBy(p => p.X).First();
                var right = points.OrderBy(p => p.X).Last();
                KirisAcilimCizgi(tr, ms, x + left.X, lineY, x + right.X, lineY, LayerEtiketCizgisi);
                foreach (var point in points)
                    KirisAcilimCizgi(tr, ms, x + point.X, y + point.Y, x + point.X, lineY, LayerEtiketCizgisi);
                if (web)
                {
                    double labelRight = x - 5.0 / 2.0;
                    KirisAcilimCizgi(tr, ms, labelRight, lineY, x + left.X, lineY, LayerEtiketCizgisi);
                    var text = new DBText { TextString = label, Height = 5,
                        Layer = KirisAcilimDonatiYaziLayer, TextStyleId = GetOrCreateYaziBeykentTextStyle(tr, db),
                        HorizontalMode = TextHorizontalMode.TextRight, VerticalMode = TextVerticalMode.TextBottom,
                        Position = new Point3d(labelRight, lineY + 1, 0), AlignmentPoint = new Point3d(labelRight, lineY + 1, 0) };
                    AppendEntity(tr, ms, text);
                    text.AdjustAlignment(db);
                }
                else Text(x + (left.X + right.X) * 0.5, lineY + textDy, label);
            }
            var labelRows = k.Siralar.GroupBy(r => r.Tur + ":" + r.Cap + ":" + Math.Round(r.Derinlik, 2))
                .Select(g => new KirisKesitSira { Tur = g.First().Tur, Cap = g.First().Cap,
                    Adet = g.Sum(r => r.Adet), Derinlik = g.First().Derinlik })
                .OrderBy(r => r.Tur == "mon" || r.Tur == "duz" ? 0 : 1).ToList();
            bool monUst = KirisAcilimMontajUstYuz;
            var layout = KirisKesitCubukYerlesimi(w, h, labelRows, monUst, _surekliTemelAcilim ? 6 : 5);
            int topLabels = 0, bottomLabels = 0;
            foreach (var r in labelRows)
            {
                var points = layout.Where(c => ReferenceEquals(c.Donati, r)).Select(c => c.Merkez).ToList();
                if (points.Count == 0) continue;
                string label = points.Count.ToString(CultureInfo.InvariantCulture) + "ø" + r.Cap;
                foreach (var p in points) DrawTwoArcCirclePline(tr, ms, x + p.X, y + p.Y, 0.5, LayerDonatiGovde);
                if (r.Tur == "gov") Leader(points, y + points.Min(p => p.Y) - 4, label, true);
                else
                {
                    bool top = r.Tur == "mon" ? monUst
                        : r.Tur == "duz" ? !monUst
                        : r.Derinlik < h * 0.5;
                    int index = top ? topLabels++ : bottomLabels++;
                    double lineY = index == 0 ? (top ? y + 7 : y - h - 6)
                        : top ? y + points.Min(p => p.Y) - 5 - (index - 1) * 10
                        : y + points.Max(p => p.Y) + 5 + (index - 1) * 10;
                    Leader(points, lineY, label, false, (top == (index == 0)) ? 5 : -5);
                }
            }
            if (k.Etriye > 0)
            {
                // Kolon etriyesinin aynı 135° kancalı geometri ve açılım yolu.
                double cover = _surekliTemelAcilim ? 5 : 4;
                var boxes = KirisKesitEtriyeKutulari(w, cover);
                foreach (var box in boxes)
                    AppendKolonKesitEtriyePline(tr, ms, x + box.Lo, y - h + cover, x + box.Hi, y - cover, 1.0, Math.Max(10.0, EtriyeAcilimKancaCm(k.Etriye)));
                var oldCols = _kolonDuseyGrupColNos; int oldKat = _kolonDuseyBenzerKatSayisi; bool old25 = _kolonDuseyOlcek25;
                try
                {
                    _kolonDuseyGrupColNos = null; _kolonDuseyBenzerKatSayisi = 1; _kolonDuseyOlcek25 = false;
                    DrawKolonKesitEtriyeAcilim(tr, ms, new Envelope(x, x + w, y - h, y),
                        new List<(double midX, double w, double h)> { (x + w * 0.5, boxes[0].Hi - boxes[0].Lo, h - 2 * cover) },
                        k.Etriye, 1, 0, false, out _, out _, yTopOverride: y - h - 72 - adlar.Count * 8,
                        minHookCm: 10.0, showQuantity: false, radiusCm: 1.0,
                        textHeightCm: 5.0, labelPrefix: KirisKesitEtriyeCarpan(boxes.Count));
                }
                finally { _kolonDuseyGrupColNos = oldCols; _kolonDuseyBenzerKatSayisi = oldKat; _kolonDuseyOlcek25 = old25; }
            }
        }
    }
}
