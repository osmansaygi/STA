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
    /// <summary>
    /// Kalıp planlarında guseli kiriş işareti: guse bitiş hattında (mesnet yüzünden La / Lb) kiriş genişliği boyunca enine çizgi,
    /// guse bölgesinin ortasında kirişin dışında "GUSE(Hg/L)" yazısı. Katman GUSE (BEYKENT). Temel ve kolon aplikasyon planlarında çizilmez.
    /// </summary>
    public sealed partial class PlanIdDrawingManager
    {
        private const string LayerKalipGuse = "GUSE (BEYKENT)";

        private void DrawKalipGuseIsaretleri(Transaction tr, BlockTableRecord btr, Database db, List<BeamInfo> beams,
            List<(int beamId, Geometry toDraw, int fixedAxisId)> beamGeoms, Geometry kolonPerdeUnion,
            double offsetX, double offsetY, double annotMul)
        {
            if (tr == null || btr == null || db == null || beams == null) return;
            if (IsTemelPlanDraw || _kolonApplicationScale.HasValue) return;
            var guseli = beams.Where(b => b != null && b.GuseHgCm > 0.5 && (b.GuseLaCm > 0.5 || b.GuseLbCm > 0.5)).ToList();
            if (guseli.Count == 0) return;
            var factory = _ntsDrawFactory ?? NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
            EnsurePlanLayer(tr, db, LayerKalipGuse, 30, LineWeight.LineWeight025, useDashed: false);
            double yaziH = BeamLabelRefHeightCm * annotMul * 0.85;

            foreach (var beam in guseli)
            {
                try
                {
                    if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p1)) continue;
                    if (!_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p2)) continue;
                    var a = new Point2d(p1.X + offsetX, p1.Y + offsetY);
                    var b = new Point2d(p2.X + offsetX, p2.Y + offsetY);
                    var aIlk = a;
                    NormalizeBeamDirection(beam.FixedAxisId, ref a, ref b);
                    bool tersMi = (a - aIlk).Length > 1e-6;
                    Vector2d dir = b - a;
                    double L = dir.Length;
                    if (L <= 1.0) continue;
                    Vector2d u = dir.GetNormal();
                    Vector2d perp = new Vector2d(-u.Y, u.X);
                    ComputeBeamEdgeOffsets(beam.OffsetRaw, beam.WidthCm / 2.0, out double ust, out double alt);
                    double pOrta = 0.5 * (ust + alt);
                    double pMax = Math.Max(ust, alt), pMin = Math.Min(ust, alt);

                    // Mesnet yüzleri: kiriş orta çizgisinden kolon/perde ve diğer kirişler çıkarılır; kalan en küçük/en büyük t.
                    Point2d c0 = a + perp.MultiplyBy(pOrta), c1 = b + perp.MultiplyBy(pOrta);
                    Geometry hat = factory.CreateLineString(new[] { new Coordinate(c0.X, c0.Y), new Coordinate(c1.X, c1.Y) });
                    Geometry kalan = hat;
                    if (kolonPerdeUnion != null && !kolonPerdeUnion.IsEmpty && kalan.Intersects(kolonPerdeUnion))
                        kalan = kalan.Difference(kolonPerdeUnion);
                    if (beamGeoms != null)
                    {
                        var ortaPt = factory.CreatePoint(new Coordinate(0.5 * (c0.X + c1.X), 0.5 * (c0.Y + c1.Y)));
                        foreach (var g in beamGeoms)
                        {
                            if (g.toDraw == null || g.toDraw.IsEmpty || kalan == null || kalan.IsEmpty) continue;
                            if (g.toDraw.Distance(ortaPt) < 1.0) continue; // kirişin kendisi
                            if (!g.toDraw.Intersects(kalan)) continue;
                            kalan = kalan.Difference(g.toDraw);
                        }
                    }
                    if (kalan == null || kalan.IsEmpty) continue;
                    double tYuz0 = double.MaxValue, tYuz1 = double.MinValue;
                    for (int i = 0; i < kalan.NumGeometries; i++)
                    {
                        var parca = kalan.GetGeometryN(i);
                        if (parca == null || parca.IsEmpty || parca.Length < 1.0) continue;
                        foreach (var k in parca.Coordinates)
                        {
                            double t = (new Point2d(k.X, k.Y) - c0).DotProduct(u);
                            tYuz0 = Math.Min(tYuz0, t);
                            tYuz1 = Math.Max(tYuz1, t);
                        }
                    }
                    if (tYuz1 - tYuz0 < 1.0) continue;

                    // La: 1. nokta (StartAxis) ucu, Lb: 2. nokta ucu. Yön çevrildiyse uçlar yer değiştirir.
                    double lBas = tersMi ? beam.GuseLbCm : beam.GuseLaCm;
                    double lSon = tersMi ? beam.GuseLaCm : beam.GuseLbCm;
                    double net = tYuz1 - tYuz0;
                    double aci = Math.Atan2(u.Y, u.X);
                    int hg = (int)Math.Round(beam.GuseHgCm);

                    void Uc(double yuzT, double boy, double yon)
                    {
                        if (boy <= 0.5) return;
                        double gBoy = Math.Min(boy, net * (lBas > 0.5 && lSon > 0.5 ? 0.5 : 1.0));
                        double tHat = yuzT + yon * gBoy;
                        Point2d e0 = a + u.MultiplyBy(tHat) + perp.MultiplyBy(pMin);
                        Point2d e1 = a + u.MultiplyBy(tHat) + perp.MultiplyBy(pMax);
                        var ln = new Line(new Point3d(e0.X, e0.Y, 0), new Point3d(e1.X, e1.Y, 0)) { Layer = LayerKalipGuse };
                        AppendEntity(tr, btr, ln);
                        string yazi = string.Format(CultureInfo.InvariantCulture, "GUSE({0}/{1})", hg, (int)Math.Round(boy));
                        double tOrta = yuzT + yon * gBoy * 0.5;
                        Point2d yp = a + u.MultiplyBy(tOrta) + perp.MultiplyBy(pMax + 2.0 * annotMul + yaziH * 0.5);
                        DrawBeamLabel(tr, btr, db, new Point3d(yp.X, yp.Y, 0), yazi, yaziH, aci, LayerKalipGuse, useMiddleCenter: true);
                    }

                    Uc(tYuz0, lBas, 1.0);
                    Uc(tYuz1, lSon, -1.0);
                }
                catch
                {
                    // Guse işareti yardımcı bilgidir; tek kirişteki geometri hatası planı durdurmasın.
                }
            }
        }
    }
}
