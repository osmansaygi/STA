using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Union;

namespace ST4Yardimci
{
    internal static partial class DosemeDonatiRunner
    {
        private const string LayerAltDuzAlan = "ALT DUZ ALAN (BEYKENT)";
        /// <summary>Kiriş çizgisi kolon köşesine bu kadar yakınsa aynı nokta sayılır (cm).</summary>
        private const double AltDuzAlanJointTolCm = 3.0;
        /// <summary>Boundary öncesi kapatılan kaçak. Bundan uzun aralık uzatılmaz.</summary>
        private const double BoundaryGapCloseCm = 2.0;
        /// <summary>Uç, başka bir mesnet kenarına bu kadar yakınsa arayı kapat (T birleşim, cm).</summary>
        private const double AltDuzAlanBridgeCm = 6.0;

        private struct RawSeg
        {
            public double X0, Y0, X1, Y1;
        }

        private sealed class AltDuzAlanModel
        {
            public GeometryFactory Gf;
            public List<Polygon> Faces;
            public Geometry Mesnet;
            public List<RawSeg> SupportSegs;
            /// <summary>Formdaki mesnet katmanları; döşeme boundary bunlarla kapanır.</summary>
            public List<RawSeg> MesnetOnlySegs;
            public List<RawSeg> NodedEdges;
            public List<Polygon> BoundFaces;
            public List<Geometry> Drawn = new List<Geometry>();
            public int DrawnCount;
        }

        /// <summary>Sanal hat çevresi (cm). Tüm kat poligonize edilmez; geç kalan bu.</summary>
        private const double AltDuzAlanCorridorCm = 2500.0;

        private static AltDuzAlanModel BuildAltDuzAlanModel(
            Transaction tr, BlockTableRecord btr, Point3d lineA, Point3d lineB)
        {
            var gf = new GeometryFactory();
            var raw = new List<RawSeg>();
            var mesnetOnly = new List<RawSeg>();
            var bodies = new List<Polygon>();
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || !ent.Visible) continue;
                string lyr = ent.Layer ?? "";
                bool mesnet = IsMesnetLayer(lyr, null);
                bool bos = IsBoslukLayer(lyr);
                if (!mesnet && !bos) continue;
                CollectBoundarySegs(ent, raw);
                // Poligon: LINE kadar eski polyline, yay, mline ve kolon bloğu da sınırdır.
                if (mesnet) CollectMesnetGeometry(tr, ent, mesnetOnly, Matrix3d.Identity, 0);
                TryAddClosedMesnetBody(ent, gf, bodies);
            }
            raw = KeepSegsNearLine(raw, lineA, lineB, AltDuzAlanCorridorCm);
            bodies = KeepBodiesNearLine(bodies, lineA, lineB, AltDuzAlanCorridorCm);
            if (raw.Count == 0) return new AltDuzAlanModel { Gf = gf, Faces = new List<Polygon>() };

            List<RawSeg> snapped = SnapSegJoints(raw, AltDuzAlanJointTolCm);
            AddBeamStrips(snapped, gf, bodies);
            Geometry mesnetBody = UnionMesnetBodies(bodies);
            snapped = OrthogonalizeLoneDiagonals(snapped);
            snapped = SnapSegJoints(snapped, AltDuzAlanJointTolCm);
            BridgeOpenEnds(snapped, AltDuzAlanBridgeCm);
            var lines = new List<Geometry>();
            foreach (RawSeg s in snapped)
            {
                if (Dist(s.X0, s.Y0, s.X1, s.Y1) < 0.4) continue;
                lines.Add(gf.CreateLineString(new[]
                {
                    new Coordinate(s.X0, s.Y0),
                    new Coordinate(s.X1, s.Y1)
                }));
            }
            if (lines.Count == 0) return new AltDuzAlanModel { Gf = gf, Faces = new List<Polygon>() };

            var faces = PolygonizeLinesSafe(lines);
            if (mesnetBody != null)
                faces = ClipFacesOutsideMesnet(faces, mesnetBody);
            List<RawSeg> mesnetNear = KeepSegsNearLine(mesnetOnly, lineA, lineB, AltDuzAlanCorridorCm);
            List<RawSeg> forBound = SnapSegJoints(mesnetNear, BoundaryGapCloseCm);
            CloseLeaksUnder(forBound, BoundaryGapCloseCm);
            return new AltDuzAlanModel
            {
                Gf = gf,
                Faces = faces,
                Mesnet = mesnetBody,
                SupportSegs = snapped,
                MesnetOnlySegs = mesnetNear,
                NodedEdges = forBound,
                BoundFaces = PolygonizeReady(forBound, gf)
            };
        }

        /// <summary>
        /// Yalnız çizilmiş mesnet çizgileri. Kesişen yerde bölünür.
        /// Yeni çizgi eklenmez; boundary açıklığın içindeki en küçük kapalı yüzdür.
        /// </summary>
        private static List<Polygon> PolygonizeMesnetBoundary(List<RawSeg> segs, GeometryFactory gf)
        {
            var faces = new List<Polygon>();
            if (segs == null || segs.Count == 0 || gf == null) return faces;
            List<RawSeg> snapped = SnapSegJoints(segs, AltDuzAlanJointTolCm);
            var lines = new List<Geometry>();
            foreach (RawSeg s in snapped)
            {
                if (Dist(s.X0, s.Y0, s.X1, s.Y1) < 0.4) continue;
                lines.Add(gf.CreateLineString(new[]
                {
                    new Coordinate(s.X0, s.Y0),
                    new Coordinate(s.X1, s.Y1)
                }));
            }
            if (lines.Count == 0) return faces;
            try
            {
                Geometry noded = UnaryUnionOp.Union(lines);
                if (noded == null || noded.IsEmpty) return faces;
                var polygonizer = new Polygonizer();
                polygonizer.Add(noded);
                foreach (object obj in polygonizer.GetPolygons())
                {
                    if (obj is Polygon p && !p.IsEmpty && p.Area >= 20.0)
                        faces.Add(p);
                }
            }
            catch { }
            return faces;
        }

        /// <summary>
        /// Tüm katı tek Polygonizer'a vermek ofiste yığın taşırıp AutoCAD'i düşürür.
        /// 800 cm karolarda aynı hücreler üretilir.
        /// </summary>
        private static List<Polygon> PolygonizeLinesSafe(List<Geometry> lines)
        {
            var faces = new List<Polygon>();
            if (lines == null || lines.Count == 0) return faces;
            const int directMax = 60;
            const double tileCm = 800.0;
            const double padCm = 40.0;
            try
            {
                if (lines.Count <= directMax)
                {
                    AddPolygonizerFaces(lines, faces);
                    return faces;
                }
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (Geometry g in lines)
                {
                    Envelope e;
                    try { e = g.EnvelopeInternal; }
                    catch { continue; }
                    if (e == null || e.IsNull) continue;
                    if (e.MinX < minX) minX = e.MinX;
                    if (e.MinY < minY) minY = e.MinY;
                    if (e.MaxX > maxX) maxX = e.MaxX;
                    if (e.MaxY > maxY) maxY = e.MaxY;
                }
                if (minX > maxX) return faces;
                for (double x = minX; x <= maxX + 1.0; x += tileCm)
                for (double y = minY; y <= maxY + 1.0; y += tileCm)
                {
                    double x1 = x + tileCm, y1 = y + tileCm;
                    var part = new List<Geometry>();
                    foreach (Geometry g in lines)
                    {
                        Envelope e;
                        try { e = g.EnvelopeInternal; }
                        catch { continue; }
                        if (e == null || e.IsNull) continue;
                        if (e.MaxX < x - padCm || e.MinX > x1 + padCm) continue;
                        if (e.MaxY < y - padCm || e.MinY > y1 + padCm) continue;
                        part.Add(g);
                    }
                    if (part.Count == 0) continue;
                    if (part.Count > 120)
                    {
                        SplitPolygonize(part, x, y, x1, y1, 0, faces);
                        continue;
                    }
                    AddPolygonizerFaces(part, faces);
                }
            }
            catch
            {
                faces.Clear();
            }
            return faces;
        }

        private static void SplitPolygonize(
            List<Geometry> part, double x0, double y0, double x1, double y1, int depth, List<Polygon> faces)
        {
            if (part == null || part.Count == 0) return;
            if (part.Count <= 80 || depth >= 3)
            {
                if (part.Count <= 80)
                    AddPolygonizerFaces(part, faces);
                return;
            }
            double mx = 0.5 * (x0 + x1), my = 0.5 * (y0 + y1);
            SplitPolygonize(FilterEnv(part, x0, y0, mx, my), x0, y0, mx, my, depth + 1, faces);
            SplitPolygonize(FilterEnv(part, mx, y0, x1, my), mx, y0, x1, my, depth + 1, faces);
            SplitPolygonize(FilterEnv(part, x0, my, mx, y1), x0, my, mx, y1, depth + 1, faces);
            SplitPolygonize(FilterEnv(part, mx, my, x1, y1), mx, my, x1, y1, depth + 1, faces);
        }

        private static List<Geometry> FilterEnv(List<Geometry> src, double x0, double y0, double x1, double y1)
        {
            var list = new List<Geometry>();
            const double pad = 20.0;
            foreach (Geometry g in src)
            {
                Envelope e;
                try { e = g.EnvelopeInternal; }
                catch { continue; }
                if (e == null || e.IsNull) continue;
                if (e.MaxX < x0 - pad || e.MinX > x1 + pad) continue;
                if (e.MaxY < y0 - pad || e.MinY > y1 + pad) continue;
                list.Add(g);
            }
            return list;
        }

        private static void AddPolygonizerFaces(List<Geometry> lines, List<Polygon> faces)
        {
            if (lines == null || lines.Count == 0) return;
            try
            {
                var polygonizer = new Polygonizer();
                foreach (Geometry line in lines)
                    polygonizer.Add(line);
                foreach (object obj in polygonizer.GetPolygons())
                {
                    if (obj is Polygon p && !p.IsEmpty && p.Area >= 20.0)
                        faces.Add(p);
                }
            }
            catch { }
        }

        /// <summary>Nokta kiriş/kolon gövdesinin içinde. Sınır çizgisinin üstü sayılmaz.</summary>
        private static bool PointInsideAltMesnet(AltDuzAlanModel model, Point3d p)
        {
            if (model == null) return false;
            if (model.Mesnet != null && !model.Mesnet.IsEmpty && model.Gf != null)
            {
                try
                {
                    var pt = model.Gf.CreatePoint(new Coordinate(p.X, p.Y));
                    if (model.Mesnet.Contains(pt)) return true;
                }
                catch { }
            }
            return PointInSupportStrip(model.SupportSegs, p.X, p.Y);
        }

        /// <summary>İki yakın paralel mesnet yüzünün arasında (kiriş kalınlığı).</summary>
        private static bool PointInSupportStrip(List<RawSeg> segs, double x, double y)
        {
            if (segs == null) return false;
            for (int i = 0; i < segs.Count; i++)
            {
                RawSeg a = segs[i];
                double la = SegLen(a);
                if (la < 40.0) continue;
                double ux = (a.X1 - a.X0) / la, uy = (a.Y1 - a.Y0) / la;
                double nx = -uy, ny = ux;
                double ta = (x - a.X0) * ux + (y - a.Y0) * uy;
                if (ta < -2.0 || ta > la + 2.0) continue;
                double da = (x - a.X0) * nx + (y - a.Y0) * ny;
                if (Math.Abs(da) < 0.8 || Math.Abs(da) > 70.0) continue;
                for (int j = 0; j < segs.Count; j++)
                {
                    if (j == i) continue;
                    RawSeg b = segs[j];
                    double lb = SegLen(b);
                    if (lb < 40.0) continue;
                    double cos = Math.Abs((b.X1 - b.X0) * ux + (b.Y1 - b.Y0) * uy) / lb;
                    if (cos < 0.996) continue;
                    double db = (x - b.X0) * nx + (y - b.Y0) * ny;
                    if (da * db >= 0) continue;
                    double sep = Math.Abs(da - db);
                    if (sep < 8.0 || sep > 70.0) continue;
                    double tb = (x - b.X0) * ux + (y - b.Y0) * uy;
                    if (tb < -2.0 || tb > lb + 2.0) continue;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Yazı kirişin içinde kaldıysa açıklık ortasına doğru çıkar.</summary>
        private static Point3d NudgeOutOfAltMesnet(AltDuzAlanModel model, Point3d from, Point3d toward)
        {
            if (!PointInsideAltMesnet(model, from)) return from;
            Vector3d d = toward - from;
            double len = d.Length;
            if (len < 1.0) return from;
            d = d / len;
            for (double s = 5.0; s <= len + 5.0; s += 5.0)
            {
                Point3d q = from + d * Math.Min(s, len);
                if (!PointInsideAltMesnet(model, q)) return q;
            }
            return toward;
        }

        /// <summary>
        /// Kolon/perde kapalı gövdesi ve kiriş şeridi. Poligon bu gövdenin içine ve ötesine taşmasın.
        /// </summary>
        private static void TryAddClosedMesnetBody(Entity ent, GeometryFactory gf, List<Polygon> bodies)
        {
            if (ent is Circle c)
            {
                if (c.Radius < 2.0 || c.Radius > 200.0) return;
                var ring = new Coordinate[25];
                for (int i = 0; i < 24; i++)
                {
                    double a = 2.0 * Math.PI * i / 24.0;
                    ring[i] = new Coordinate(c.Center.X + c.Radius * Math.Cos(a), c.Center.Y + c.Radius * Math.Sin(a));
                }
                ring[24] = ring[0];
                TryAddBody(gf, bodies, ring);
                return;
            }
            if (!(ent is Polyline pl) || !pl.Closed || pl.NumberOfVertices < 3) return;
            int n = pl.NumberOfVertices;
            var coords = new List<Coordinate>();
            for (int i = 0; i < n; i++)
            {
                Point2d a = pl.GetPoint2dAt(i);
                int j = (i + 1) % n;
                Point2d b = pl.GetPoint2dAt(j);
                AppendBulgeCoords(coords, a.X, a.Y, b.X, b.Y, pl.GetBulgeAt(i));
            }
            if (coords.Count < 3) return;
            if (!coords[0].Equals2D(coords[coords.Count - 1]))
                coords.Add(coords[0]);
            TryAddBody(gf, bodies, coords.ToArray());
        }

        private static void AppendBulgeCoords(List<Coordinate> coords, double x0, double y0, double x1, double y1, double bulge)
        {
            if (Math.Abs(bulge) < 1e-6)
            {
                coords.Add(new Coordinate(x0, y0));
                return;
            }
            double dx = x1 - x0, dy = y1 - y0;
            double chord = Math.Sqrt(dx * dx + dy * dy);
            if (chord < 0.2)
            {
                coords.Add(new Coordinate(x0, y0));
                return;
            }
            double theta = 4.0 * Math.Atan(bulge);
            double radius = chord / (2.0 * Math.Sin(Math.Abs(theta) / 2.0));
            double mx = (x0 + x1) * 0.5, my = (y0 + y1) * 0.5;
            double nx = -dy / chord, ny = dx / chord;
            double h = Math.Sqrt(Math.Max(0.0, radius * radius - chord * chord * 0.25));
            if (bulge < 0) { nx = -nx; ny = -ny; }
            double cx = mx + nx * h, cy = my + ny * h;
            double a0 = Math.Atan2(y0 - cy, x0 - cx);
            int steps = Math.Max(4, (int)Math.Ceiling(Math.Abs(theta) / (Math.PI / 8.0)));
            for (int s = 0; s < steps; s++)
            {
                double a = a0 + theta * s / steps;
                coords.Add(new Coordinate(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a)));
            }
        }

        private static void TryAddBody(GeometryFactory gf, List<Polygon> bodies, Coordinate[] ring)
        {
            if (ring == null || ring.Length < 4) return;
            try
            {
                var poly = gf.CreatePolygon(ring);
                if (poly == null || poly.IsEmpty || !poly.IsValid) return;
                if (poly.Area < 40.0 || poly.Area > 300000.0) return;
                bodies.Add(poly);
            }
            catch { }
        }

        /// <summary>İki uzun paralel kiriş yüzü ve kısa kapak → kiriş şeridi. Açıklık şerit sayılmaz.</summary>
        private static void AddBeamStrips(List<RawSeg> segs, GeometryFactory gf, List<Polygon> bodies)
        {
            if (segs == null || segs.Count < 2) return;
            var shorts = new List<int>();
            for (int i = 0; i < segs.Count; i++)
                if (SegLen(segs[i]) <= 120.0) shorts.Add(i);

            for (int i = 0; i < segs.Count; i++)
            {
                RawSeg a = segs[i];
                double la = SegLen(a);
                if (la < 40.0) continue;
                double ux = (a.X1 - a.X0) / la, uy = (a.Y1 - a.Y0) / la;
                double nx = -uy, ny = ux;
                for (int j = i + 1; j < segs.Count; j++)
                {
                    RawSeg b = segs[j];
                    double lb = SegLen(b);
                    if (lb < 40.0) continue;
                    double cos = Math.Abs((b.X1 - b.X0) * ux + (b.Y1 - b.Y0) * uy) / lb;
                    if (cos < 0.996) continue;
                    double dist = Math.Abs(((b.X0 + b.X1) * 0.5 - (a.X0 + a.X1) * 0.5) * nx
                        + ((b.Y0 + b.Y1) * 0.5 - (a.Y0 + a.Y1) * 0.5) * ny);
                    if (dist < 8.0 || dist > 70.0) continue;
                    double b0 = (b.X0 - a.X0) * ux + (b.Y0 - a.Y0) * uy;
                    double b1 = (b.X1 - a.X0) * ux + (b.Y1 - a.Y0) * uy;
                    if (b0 > b1) { double t = b0; b0 = b1; b1 = t; }
                    double ov0 = Math.Max(0.0, b0);
                    double ov1 = Math.Min(la, b1);
                    if (ov1 - ov0 < 40.0) continue;
                    if (!HasStripCap(segs, shorts, a, b)) continue;

                    double side = (b.X0 - a.X0) * nx + (b.Y0 - a.Y0) * ny;
                    var p0 = new Coordinate(a.X0 + ux * ov0, a.Y0 + uy * ov0);
                    var ring = new[]
                    {
                        p0,
                        new Coordinate(a.X0 + ux * ov1, a.Y0 + uy * ov1),
                        new Coordinate(a.X0 + ux * ov1 + nx * side, a.Y0 + uy * ov1 + ny * side),
                        new Coordinate(a.X0 + ux * ov0 + nx * side, a.Y0 + uy * ov0 + ny * side),
                        p0
                    };
                    TryAddBody(gf, bodies, ring);
                }
            }
        }

        private static bool HasStripCap(List<RawSeg> segs, List<int> shorts, RawSeg a, RawSeg b)
        {
            foreach (int k in shorts)
            {
                RawSeg s = segs[k];
                double l = SegLen(s);
                if (l < 4.0 || l > 120.0) continue;
                bool a0 = DistToSeg(s.X0, s.Y0, a) <= 6.0;
                bool a1 = DistToSeg(s.X1, s.Y1, a) <= 6.0;
                bool b0 = DistToSeg(s.X0, s.Y0, b) <= 6.0;
                bool b1 = DistToSeg(s.X1, s.Y1, b) <= 6.0;
                if ((a0 && b1) || (a1 && b0)) return true;
            }
            return false;
        }

        private static double DistToSeg(double x, double y, RawSeg o)
        {
            ClosestOnSeg(x, y, o, out _, out _, out double d2);
            return Math.Sqrt(d2);
        }

        private static double SegLen(RawSeg s)
        {
            return Dist(s.X0, s.Y0, s.X1, s.Y1);
        }

        private static Geometry UnionMesnetBodies(List<Polygon> bodies)
        {
            if (bodies == null || bodies.Count == 0) return null;
            try
            {
                // 30'luk parçalar: tek Union(tüm kat) GeometryGraph yığınını taşırır.
                const int chunk = 30;
                Geometry acc = null;
                for (int i = 0; i < bodies.Count; i += chunk)
                {
                    int n = Math.Min(chunk, bodies.Count - i);
                    var part = new List<Geometry>(n);
                    for (int k = 0; k < n; k++) part.Add(bodies[i + k]);
                    Geometry u = UnaryUnionOp.Union(part);
                    if (u == null || u.IsEmpty) continue;
                    acc = acc == null ? u : acc.Union(u);
                }
                if (acc == null || acc.IsEmpty) return null;
                return acc;
            }
            catch { return null; }
        }

        /// <summary>Hücre kiriş/kolon gövdesine veya onun ötesine taşmasın. Okun gösterdiği dış uç düşer.</summary>
        private static List<Polygon> ClipFacesOutsideMesnet(List<Polygon> faces, Geometry mesnet)
        {
            if (faces == null || faces.Count == 0 || mesnet == null || mesnet.IsEmpty) return faces;
            var result = new List<Polygon>();
            foreach (Polygon face in faces)
            {
                Geometry cut = null;
                try { cut = face.Difference(mesnet); }
                catch { cut = null; }
                if (cut == null)
                {
                    result.Add(face);
                    continue;
                }
                int before = result.Count;
                CollectPolygons(cut, result);
                if (result.Count == before && !cut.IsEmpty)
                    result.Add(face);
            }
            return result;
        }

        private static void CollectPolygons(Geometry geom, List<Polygon> dest)
        {
            if (geom == null || geom.IsEmpty) return;
            if (geom is Polygon poly)
            {
                if (!poly.IsEmpty && poly.Area >= 20.0) dest.Add(poly);
                return;
            }
            for (int i = 0; i < geom.NumGeometries; i++)
                CollectPolygons(geom.GetGeometryN(i), dest);
        }

        private static void DrawAltDuzAlanPolygon(
            Transaction tr, BlockTableRecord btr, Database db,
            AltDuzAlanModel model, Point3d seed)
        {
            if (model == null || model.Faces == null || model.Faces.Count == 0) return;
            if (!TryFaceContaining(model, seed, out Polygon cell)) return;
            // Daha önce çizilen büyük/taşkın yüz bu noktayı örtse de asıl hücre çizilsin.
            foreach (Geometry prev in model.Drawn)
            {
                if (ReferenceEquals(prev, cell)) return;
            }

            string layer = EnsureLayer(tr, db, LayerAltDuzAlan, 6, LineWeight.LineWeight020);
            if (DrawExteriorRings(tr, btr, cell, layer) <= 0) return;
            model.Drawn.Add(cell);
            model.DrawnCount++;
        }

        private static List<RawSeg> KeepSegsNearLine(List<RawSeg> segs, Point3d a, Point3d b, double pad)
        {
            if (segs == null || segs.Count == 0) return segs ?? new List<RawSeg>();
            var keep = new List<RawSeg>(segs.Count);
            foreach (RawSeg s in segs)
            {
                if (SegSegDist(s.X0, s.Y0, s.X1, s.Y1, a.X, a.Y, b.X, b.Y) <= pad)
                    keep.Add(s);
            }
            return keep;
        }

        private static List<Polygon> KeepBodiesNearLine(List<Polygon> bodies, Point3d a, Point3d b, double pad)
        {
            if (bodies == null || bodies.Count == 0) return bodies ?? new List<Polygon>();
            var keep = new List<Polygon>();
            foreach (Polygon p in bodies)
            {
                Envelope env;
                try { env = p?.EnvelopeInternal; }
                catch { continue; }
                if (env == null) continue;
                if (SegSegDist(env.MinX, env.MinY, env.MaxX, env.MinY, a.X, a.Y, b.X, b.Y) <= pad
                    || SegSegDist(env.MinX, env.MaxY, env.MaxX, env.MaxY, a.X, a.Y, b.X, b.Y) <= pad
                    || SegSegDist(env.MinX, env.MinY, env.MinX, env.MaxY, a.X, a.Y, b.X, b.Y) <= pad
                    || SegSegDist(env.MaxX, env.MinY, env.MaxX, env.MaxY, a.X, a.Y, b.X, b.Y) <= pad)
                    keep.Add(p);
            }
            return keep;
        }

        /// <summary>Kesişen parçalar 0. Uçlar uzaktayken ortadan kesen kiriş de kalır.</summary>
        private static double SegSegDist(
            double x1, double y1, double x2, double y2,
            double x3, double y3, double x4, double y4)
        {
            if (SegsCrossOrTouch(x1, y1, x2, y2, x3, y3, x4, y4)) return 0;
            double d = DistPointToSeg(x1, y1, x3, y3, x4, y4);
            d = Math.Min(d, DistPointToSeg(x2, y2, x3, y3, x4, y4));
            d = Math.Min(d, DistPointToSeg(x3, y3, x1, y1, x2, y2));
            d = Math.Min(d, DistPointToSeg(x4, y4, x1, y1, x2, y2));
            return d;
        }

        private static bool SegsCrossOrTouch(
            double x1, double y1, double x2, double y2,
            double x3, double y3, double x4, double y4)
        {
            double d1 = Cross(x3, y3, x4, y4, x1, y1);
            double d2 = Cross(x3, y3, x4, y4, x2, y2);
            double d3 = Cross(x1, y1, x2, y2, x3, y3);
            double d4 = Cross(x1, y1, x2, y2, x4, y4);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
                && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                return true;
            if (Math.Abs(d1) < 1e-6 && OnSeg(x1, y1, x3, y3, x4, y4)) return true;
            if (Math.Abs(d2) < 1e-6 && OnSeg(x2, y2, x3, y3, x4, y4)) return true;
            if (Math.Abs(d3) < 1e-6 && OnSeg(x3, y3, x1, y1, x2, y2)) return true;
            if (Math.Abs(d4) < 1e-6 && OnSeg(x4, y4, x1, y1, x2, y2)) return true;
            return false;
        }

        private static double Cross(double ax, double ay, double bx, double by, double cx, double cy)
        {
            return (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        }

        private static bool OnSeg(double px, double py, double ax, double ay, double bx, double by)
        {
            return px >= Math.Min(ax, bx) - 1e-6 && px <= Math.Max(ax, bx) + 1e-6
                && py >= Math.Min(ay, by) - 1e-6 && py <= Math.Max(ay, by) + 1e-6;
        }

        /// <summary>
        /// Açıklık poligonu bellekteki mesnet hücresinden gelir. Ekran kapanıp açılmaz.
        /// </summary>
        private static Polyline[] CollectSpanBoundaries(
            List<SpanRec> spans, AltDuzAlanModel model,
            Point3d origin, Vector3d u, Vector3d n)
        {
            var result = spans == null ? Array.Empty<Polyline>() : new Polyline[spans.Count];
            if (spans == null || spans.Count == 0 || model == null) return result;
            for (int i = 0; i < spans.Count; i++)
            {
                SpanRec sp = spans[i];
                if (!sp.InPick) continue;
                Point3d seed = AltDuzBarMid(sp, n, model);
                result[i] = MesnetBoundaryPolyline(model, seed, n, sp.Mid);
            }
            return result;
        }

        /// <summary>Alt düz çubuğun orta noktası. Kiriş gövdesindeyse dik yönde açıklığa çıkar.</summary>
        private static Point3d AltDuzBarMid(SpanRec sp, Vector3d n, AltDuzAlanModel model)
        {
            int idx = sp.SupportLeft >= 0 ? sp.SupportLeft : 0;
            Vector3d off = n * ((idx % 2) * StaggerCm - DuzBelowPilyeCm);
            Point3d mid = sp.BarL.DistanceTo(sp.BarR) >= 1.0
                ? Mid(sp.BarL, sp.BarR) + off
                : sp.Mid + off;
            if (!PointInsideAltMesnet(model, mid)) return mid;
            for (double s = 4.0; s <= 80.0; s += 4.0)
            {
                Point3d q = mid + n * s;
                if (!PointInsideAltMesnet(model, q)) return q;
                q = mid - n * s;
                if (!PointInsideAltMesnet(model, q)) return q;
            }
            return mid;
        }

        /// <summary>
        /// 2 cm altı kaçak kapatıldıktan sonra alt düz ortasını içeren en küçük kapalı yüz.
        /// </summary>
        private static Polyline BoundaryAtSeed(AltDuzAlanModel model, Point3d seed)
        {
            return SmallestMesnetFace(model, seed);
        }

        /// <summary>Poligonizer yüzü. Kiriş içi diye elenmez; noktayı içeren en küçük halka.</summary>
        private static Polyline SmallestMesnetFace(AltDuzAlanModel model, Point3d seed)
        {
            if (model?.BoundFaces == null || model.Gf == null) return null;
            Polygon best = null;
            foreach (Polygon face in model.BoundFaces)
            {
                if (!FaceCoversPoint(face, model.Gf, seed)) continue;
                if (best == null || face.Area < best.Area)
                    best = face;
            }
            return MemoryPolylineFromPolygon(best);
        }

        /// <summary>Uç, 2 cm içinde değdiği mesnet çizgisine oturur. Daha uzağa uzamaz.</summary>
        private static void CloseLeaksUnder(List<RawSeg> segs, double tolCm)
        {
            if (segs == null || segs.Count == 0 || tolCm <= 0) return;
            for (int pass = 0; pass < 3; pass++)
            {
                bool changed = false;
                for (int i = 0; i < segs.Count; i++)
                {
                    RawSeg s = segs[i];
                    if (MoveEndToLeak(segs, i, ref s, true, tolCm)) changed = true;
                    if (MoveEndToLeak(segs, i, ref s, false, tolCm)) changed = true;
                    segs[i] = s;
                }
                if (!changed) break;
            }
        }

        private static bool MoveEndToLeak(List<RawSeg> segs, int self, ref RawSeg s, bool atStart, double tolCm)
        {
            double px = atStart ? s.X0 : s.X1;
            double py = atStart ? s.Y0 : s.Y1;
            double ox = atStart ? s.X1 : s.X0;
            double oy = atStart ? s.Y1 : s.Y0;
            double dx = px - ox, dy = py - oy;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1.0) return false;
            dx /= len;
            dy /= len;
            double best = tolCm + 1e-6;
            double hx = 0, hy = 0;
            int bestJ = -1;
            for (int j = 0; j < segs.Count; j++)
            {
                if (j == self) continue;
                RawSeg o = segs[j];
                double minX = Math.Min(o.X0, o.X1) - tolCm, maxX = Math.Max(o.X0, o.X1) + tolCm;
                double minY = Math.Min(o.Y0, o.Y1) - tolCm, maxY = Math.Max(o.Y0, o.Y1) + tolCm;
                if (px < minX || px > maxX || py < minY || py > maxY) continue;
                ClosestOnSeg(px, py, o, out double cx, out double cy, out double d2);
                double d = Math.Sqrt(d2);
                if (d <= 0.05) return false;
                if (d <= tolCm)
                {
                    double along = (cx - px) * dx + (cy - py) * dy;
                    if (along >= -0.2 && d < best)
                    {
                        best = d;
                        hx = cx;
                        hy = cy;
                        bestJ = j;
                    }
                }
                if (RaySeg(px, py, dx, dy, new Point2d(o.X0, o.Y0), new Point2d(o.X1, o.Y1), out double t, out double u)
                    && t > 0.05 && t <= tolCm && u >= -0.001 && u <= 1.001 && t < best)
                {
                    best = t;
                    hx = px + dx * t;
                    hy = py + dy * t;
                    bestJ = j;
                }
            }
            if (bestJ < 0) return false;
            RawSeg hit = segs[bestJ];
            if (Dist(hit.X0, hit.Y0, hx, hy) <= 0.05)
            {
                hx = hit.X0;
                hy = hit.Y0;
            }
            else if (Dist(hit.X1, hit.Y1, hx, hy) <= 0.05)
            {
                hx = hit.X1;
                hy = hit.Y1;
            }
            else
            {
                segs[bestJ] = new RawSeg { X0 = hit.X0, Y0 = hit.Y0, X1 = hx, Y1 = hy };
                segs.Add(new RawSeg { X0 = hx, Y0 = hy, X1 = hit.X1, Y1 = hit.Y1 });
            }
            if (atStart) { s.X0 = hx; s.Y0 = hy; }
            else { s.X1 = hx; s.Y1 = hy; }
            return true;
        }

        private static List<Polygon> PolygonizeReady(List<RawSeg> segs, GeometryFactory gf)
        {
            var faces = new List<Polygon>();
            if (segs == null || segs.Count == 0 || gf == null) return faces;
            var lines = new List<Geometry>();
            foreach (RawSeg s in segs)
            {
                if (Dist(s.X0, s.Y0, s.X1, s.Y1) < 0.4) continue;
                lines.Add(gf.CreateLineString(new[]
                {
                    new Coordinate(s.X0, s.Y0),
                    new Coordinate(s.X1, s.Y1)
                }));
            }
            if (lines.Count == 0) return faces;
            try
            {
                Geometry noded = UnaryUnionOp.Union(lines);
                if (noded == null || noded.IsEmpty) return faces;
                var polygonizer = new Polygonizer();
                polygonizer.Add(noded);
                foreach (object obj in polygonizer.GetPolygons())
                {
                    if (obj is Polygon p && !p.IsEmpty && p.Area >= 20.0)
                        faces.Add(p);
                }
            }
            catch { }
            return faces;
        }

        private static void PullEndsOntoNearest(List<RawSeg> segs, double tolCm)
        {
            if (segs == null || segs.Count == 0) return;
            double tol2 = tolCm * tolCm;
            for (int i = 0; i < segs.Count; i++)
            {
                RawSeg s = segs[i];
                if (TryPullEnd(segs, i, s.X0, s.Y0, tol2, out double x, out double y))
                {
                    s.X0 = x;
                    s.Y0 = y;
                }
                if (TryPullEnd(segs, i, s.X1, s.Y1, tol2, out x, out y))
                {
                    s.X1 = x;
                    s.Y1 = y;
                }
                segs[i] = s;
            }
        }

        private static bool TryPullEnd(
            List<RawSeg> segs, int self, double px, double py, double tol2,
            out double x, out double y)
        {
            x = px;
            y = py;
            double best = tol2;
            bool hit = false;
            for (int j = 0; j < segs.Count; j++)
            {
                if (j == self) continue;
                ClosestOnSeg(px, py, segs[j], out double cx, out double cy, out double d2);
                if (d2 < 0.0025 || d2 >= best) continue;
                best = d2;
                x = cx;
                y = cy;
                hit = true;
            }
            return hit;
        }

        private static List<RawSeg> ExplodeNodedLines(List<RawSeg> segs, GeometryFactory gf)
        {
            var lines = new List<Geometry>();
            foreach (RawSeg s in segs)
            {
                if (Dist(s.X0, s.Y0, s.X1, s.Y1) < 0.2) continue;
                lines.Add(gf.CreateLineString(new[]
                {
                    new Coordinate(s.X0, s.Y0),
                    new Coordinate(s.X1, s.Y1)
                }));
            }
            if (lines.Count == 0) return segs;
            Geometry noded;
            try { noded = UnaryUnionOp.Union(lines); }
            catch { return segs; }
            var edges = new List<RawSeg>();
            CollectLinePieces(noded, edges);
            return edges.Count == 0 ? segs : edges;
        }

        private static void CollectLinePieces(Geometry g, List<RawSeg> dest)
        {
            if (g == null || g.IsEmpty) return;
            if (g is LineString ls && ls.NumPoints >= 2)
            {
                Coordinate[] cs = ls.Coordinates;
                for (int i = 0; i < cs.Length - 1; i++)
                {
                    if (cs[i].Distance(cs[i + 1]) < 0.2) continue;
                    dest.Add(new RawSeg
                    {
                        X0 = cs[i].X,
                        Y0 = cs[i].Y,
                        X1 = cs[i + 1].X,
                        Y1 = cs[i + 1].Y
                    });
                }
                return;
            }
            for (int i = 0; i < g.NumGeometries; i++)
                CollectLinePieces(g.GetGeometryN(i), dest);
        }

        private struct VKey : IEquatable<VKey>
        {
            public long X, Y;
            public VKey(double x, double y)
            {
                X = (long)Math.Round(x * 100.0);
                Y = (long)Math.Round(y * 100.0);
            }
            public bool Equals(VKey other) => X == other.X && Y == other.Y;
            public override bool Equals(object obj) => obj is VKey other && Equals(other);
            public override int GetHashCode() => unchecked(((int)X * 397) ^ (int)Y);
        }

        private struct RayHitRec
        {
            public double T;
            public int A, B;
            public double X, Y;
        }

        /// <summary>Mesnet kenarlarının düzlemsel grafı. Köşe anahtarı işaretli koordinatı da tutar.</summary>
        private sealed class FaceGraph
        {
            public readonly List<Point2d> V = new List<Point2d>();
            public readonly List<List<int>> Adj = new List<List<int>>();
            public readonly Dictionary<VKey, int> Index = new Dictionary<VKey, int>();

            public int Vert(double x, double y)
            {
                var k = new VKey(x, y);
                if (!Index.TryGetValue(k, out int id))
                {
                    id = V.Count;
                    V.Add(new Point2d(x, y));
                    Index[k] = id;
                    Adj.Add(new List<int>());
                }
                return id;
            }

            public void Link(int a, int b)
            {
                if (a == b || a < 0 || b < 0) return;
                if (!Adj[a].Contains(b)) Adj[a].Add(b);
                if (!Adj[b].Contains(a)) Adj[b].Add(a);
            }

            public void Unlink(int a, int b)
            {
                if (a < 0 || b < 0 || a >= Adj.Count || b >= Adj.Count) return;
                Adj[a].Remove(b);
                Adj[b].Remove(a);
            }

            public FaceGraph Clone()
            {
                var g = new FaceGraph();
                g.V.AddRange(V);
                foreach (KeyValuePair<VKey, int> kv in Index)
                    g.Index[kv.Key] = kv.Value;
                for (int i = 0; i < Adj.Count; i++)
                    g.Adj.Add(new List<int>(Adj[i]));
                return g;
            }
        }

        /// <summary>
        /// İç noktadan dört yöne ilk mesnet kenarları. En küçük, noktayı içeren yüz.
        /// Kapalı yüz yoksa açık uç kendi doğrultusunda, ışının gördüğü kutunun içinde ilk kesişene bağlanır.
        /// </summary>
        private static Polyline WalkContainingFace(List<RawSeg> edges, double sx, double sy)
        {
            if (edges == null || edges.Count == 0) return null;
            FaceGraph graph = BuildFaceGraph(edges);
            if (graph.V.Count < 2) return null;
            var hits = new List<RayHitRec>();
            double rx = 0, ry = 0;
            CollectDirHits(graph, sx, sy, 1, 0, hits, ref rx);
            CollectDirHits(graph, sx, sy, -1, 0, hits, ref rx);
            CollectDirHits(graph, sx, sy, 0, 1, hits, ref ry);
            CollectDirHits(graph, sx, sy, 0, -1, hits, ref ry);
            if (hits.Count == 0) return null;
            if (rx < 1) rx = ry;
            if (ry < 1) ry = rx;
            double maxR = Math.Sqrt(rx * rx + ry * ry) * 1.9;
            hits.Sort((p, q) => p.T.CompareTo(q.T));

            Polyline best = null;
            double bestArea = double.MaxValue;
            void Keep(Polyline pl)
            {
                if (pl == null) return;
                double area = RingArea(pl);
                if (area < 1.0 || area >= bestArea)
                {
                    pl.Dispose();
                    return;
                }
                best?.Dispose();
                best = pl;
                bestArea = area;
            }

            int nTry = Math.Min(hits.Count, 8);
            for (int i = 0; i < nTry; i++)
            {
                Keep(WalkOneHit(graph, sx, sy, hits[i], seedLeft: true, allowExtend: false, maxR));
                Keep(WalkOneHit(graph, sx, sy, hits[i], seedLeft: false, allowExtend: false, maxR));
            }
            if (best != null) return best;
            for (int i = 0; i < nTry; i++)
            {
                Keep(WalkOneHit(graph, sx, sy, hits[i], seedLeft: true, allowExtend: true, maxR));
                Keep(WalkOneHit(graph, sx, sy, hits[i], seedLeft: false, allowExtend: true, maxR));
            }
            return best;
        }

        private static FaceGraph BuildFaceGraph(List<RawSeg> edges)
        {
            var g = new FaceGraph();
            for (int i = 0; i < edges.Count; i++)
            {
                RawSeg s = edges[i];
                if (Dist(s.X0, s.Y0, s.X1, s.Y1) < 0.2) continue;
                g.Link(g.Vert(s.X0, s.Y0), g.Vert(s.X1, s.Y1));
            }
            return g;
        }

        private static void CollectDirHits(
            FaceGraph g, double sx, double sy, double dx, double dy,
            List<RayHitRec> dest, ref double far)
        {
            double best = double.MaxValue;
            RayHitRec pick = default;
            bool any = false;
            var seen = new HashSet<long>();
            for (int i = 0; i < g.Adj.Count; i++)
            {
                foreach (int j in g.Adj[i])
                {
                    if (j <= i) continue;
                    long key = ((long)i << 32) ^ (uint)j;
                    if (!seen.Add(key)) continue;
                    if (!RaySeg(sx, sy, dx, dy, g.V[i], g.V[j], out double t, out double u))
                        continue;
                    if (t <= 0.05 || u < -0.001 || u > 1.001 || t >= best) continue;
                    best = t;
                    any = true;
                    pick = new RayHitRec { T = t, A = i, B = j, X = sx + t * dx, Y = sy + t * dy };
                }
            }
            if (!any) return;
            if (pick.T > far) far = pick.T;
            dest.Add(pick);
            int skipA = pick.A, skipB = pick.B;
            double firstT = pick.T;
            // Aynı yöndeki ikinci kenar yalnızca yürüme adayı. Kutuyu büyütmez; yoksa komşu açıklığa taşar.
            best = double.MaxValue;
            any = false;
            RayHitRec second = default;
            seen.Clear();
            for (int i = 0; i < g.Adj.Count; i++)
            {
                foreach (int j in g.Adj[i])
                {
                    if (j <= i) continue;
                    long key = ((long)i << 32) ^ (uint)j;
                    if (!seen.Add(key)) continue;
                    if (i == skipA && j == skipB) continue;
                    if (!RaySeg(sx, sy, dx, dy, g.V[i], g.V[j], out double t, out double u))
                        continue;
                    if (t <= firstT + 0.5 || u < -0.001 || u > 1.001 || t >= best) continue;
                    best = t;
                    any = true;
                    second = new RayHitRec { T = t, A = i, B = j, X = sx + t * dx, Y = sy + t * dy };
                }
            }
            if (any) dest.Add(second);
        }

        private static bool RaySeg(
            double sx, double sy, double dx, double dy, Point2d a, Point2d b,
            out double t, out double u)
        {
            t = u = 0;
            double ex = b.X - a.X, ey = b.Y - a.Y;
            double den = dx * ey - dy * ex;
            if (Math.Abs(den) < 1e-8) return false;
            double qx = a.X - sx, qy = a.Y - sy;
            t = (qx * ey - qy * ex) / den;
            u = (qx * dy - qy * dx) / den;
            return true;
        }

        private static Polyline WalkOneHit(
            FaceGraph src, double sx, double sy, RayHitRec hit, bool seedLeft, bool allowExtend, double maxR)
        {
            FaceGraph g = src.Clone();
            int a = hit.A, b = hit.B;
            if (a < 0 || b < 0 || a >= g.V.Count || b >= g.V.Count) return null;
            int p = g.Vert(hit.X, hit.Y);
            if (p != a && p != b)
            {
                g.Unlink(a, b);
                g.Link(a, p);
                g.Link(b, p);
            }
            double cross = (g.V[b].X - g.V[a].X) * (sy - hit.Y) - (g.V[b].Y - g.V[a].Y) * (sx - hit.X);
            bool towardB = seedLeft ? cross >= 0 : cross < 0;
            int from = p;
            int to = towardB ? b : a;
            if (from == to) to = towardB ? a : b;
            if (from == to || g.Adj[from].Count == 0) return null;

            var loop = new List<int> { from };
            int curFrom = from, curTo = to;
            int guard = 0;
            int ext = 0;
            int limit = g.V.Count + 12;
            do
            {
                if (Dist(g.V[curTo].X, g.V[curTo].Y, sx, sy) > maxR)
                    return null;
                loop.Add(curTo);
                int next = NextCcw(g.V, g.Adj, curFrom, curTo);
                if (next < 0)
                {
                    if (!allowExtend || ext >= 8) return null;
                    next = ExtendForward(g, curFrom, curTo, sx, sy, maxR);
                    if (next < 0) return null;
                    ext++;
                    limit = g.V.Count + 12;
                }
                curFrom = curTo;
                curTo = next;
                if (++guard > limit) return null;
            }
            while (!(curFrom == from && curTo == to));

            if (loop.Count >= 2 && loop[0] == loop[loop.Count - 1])
                loop.RemoveAt(loop.Count - 1);
            if (loop.Count < 3) return null;
            var pl = new Polyline();
            for (int i = 0; i < loop.Count; i++)
                pl.AddVertexAt(i, g.V[loop[i]], 0, 0, 0);
            pl.Closed = true;
            DropCollinearVertices(pl);
            if (pl.NumberOfVertices < 3 || !PolyContains(pl, new Point3d(sx, sy, 0)))
            {
                pl.Dispose();
                return null;
            }
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point2d q = pl.GetPoint2dAt(i);
                if (Dist(q.X, q.Y, sx, sy) > maxR)
                {
                    pl.Dispose();
                    return null;
                }
            }
            return pl;
        }

        /// <summary>Açık uç, geldiği doğrultuda ilk mesnet kenarına. Yeni parça o doğrunun üstündedir.</summary>
        private static int ExtendForward(FaceGraph g, int prev, int cur, double sx, double sy, double maxR)
        {
            double dx = g.V[cur].X - g.V[prev].X;
            double dy = g.V[cur].Y - g.V[prev].Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) return -1;
            dx /= len;
            dy /= len;
            double ox = g.V[cur].X, oy = g.V[cur].Y;
            double best = double.MaxValue;
            int ha = -1, hb = -1;
            double hx = 0, hy = 0;
            var seen = new HashSet<long>();
            for (int i = 0; i < g.Adj.Count; i++)
            {
                foreach (int j in g.Adj[i])
                {
                    if (j <= i) continue;
                    if (i == cur || j == cur) continue;
                    long key = ((long)i << 32) ^ (uint)j;
                    if (!seen.Add(key)) continue;
                    if (!RaySeg(ox, oy, dx, dy, g.V[i], g.V[j], out double t, out double u))
                        continue;
                    if (t <= 0.4 || u < -0.001 || u > 1.001 || t >= best) continue;
                    best = t;
                    ha = i;
                    hb = j;
                    hx = ox + t * dx;
                    hy = oy + t * dy;
                }
            }
            if (ha < 0) return -1;
            if (Dist(hx, hy, sx, sy) > maxR) return -1;
            int p = g.Vert(hx, hy);
            if (p != ha && p != hb)
            {
                g.Unlink(ha, hb);
                g.Link(ha, p);
                g.Link(hb, p);
            }
            g.Link(cur, p);
            return p;
        }

        private static int NextCcw(List<Point2d> verts, List<List<int>> adj, int prev, int cur)
        {
            if (cur < 0 || cur >= adj.Count || prev < 0) return -1;
            double inx = verts[cur].X - verts[prev].X;
            double iny = verts[cur].Y - verts[prev].Y;
            double baseAng = Math.Atan2(-iny, -inx);
            int best = -1;
            double bestDelta = double.MaxValue;
            foreach (int nb in adj[cur])
            {
                if (nb == prev) continue;
                double ang = Math.Atan2(verts[nb].Y - verts[cur].Y, verts[nb].X - verts[cur].X);
                double delta = ang - baseAng;
                while (delta <= 1e-8) delta += Math.PI * 2.0;
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = nb;
                }
            }
            return best;
        }

        private static double RingArea(Polyline pl)
        {
            if (pl == null) return 0;
            int n = pl.NumberOfVertices;
            double a = 0;
            for (int i = 0; i < n; i++)
            {
                Point2d p = pl.GetPoint2dAt(i);
                Point2d q = pl.GetPoint2dAt((i + 1) % n);
                a += p.X * q.Y - q.X * p.Y;
            }
            return Math.Abs(a) * 0.5;
        }

        /// <summary>Mesnet çizgilerinin kapattığı, alt düz ortasını içeren yüz. Kiriş gövdesi alınmaz.</summary>
        private static Polyline MesnetBoundaryPolyline(
            AltDuzAlanModel model, Point3d seed, Vector3d n, Point3d toward)
        {
            if (model == null) return null;
            if (TrySlabFace(model, seed, n, toward, out Polygon face))
                return MemoryPolylineFromPolygon(face);
            if (model.MesnetOnlySegs == null || model.MesnetOnlySegs.Count == 0 || model.Gf == null)
                return null;
            var near = new List<RawSeg>();
            foreach (RawSeg s in model.MesnetOnlySegs)
            {
                if (DistPointToSeg(seed.X, seed.Y, s.X0, s.Y0, s.X1, s.Y1) <= 1800.0
                    || DistPointToSeg(toward.X, toward.Y, s.X0, s.Y0, s.X1, s.Y1) <= 1800.0)
                    near.Add(s);
            }
            List<Polygon> local = PolygonizeMesnetBoundary(near, model.Gf);
            face = SmallestCovering(local, model, seed, n, toward);
            return MemoryPolylineFromPolygon(face);
        }

        private static bool TrySlabFace(
            AltDuzAlanModel model, Point3d seed, Vector3d n, Point3d toward, out Polygon face)
        {
            face = SmallestCovering(model?.BoundFaces, model, seed, n, toward);
            return face != null;
        }

        /// <summary>Alt düz ortası, olmazsa açıklık ortasına doğru. Kiriş içindeki nokta döşeme sayılmaz.</summary>
        private static Polygon SmallestCovering(
            List<Polygon> faces, AltDuzAlanModel model, Point3d seed, Vector3d n, Point3d toward)
        {
            if (faces == null || model?.Gf == null) return null;
            Polygon best = null;
            void Consider(Point3d q)
            {
                if (PointInsideAltMesnet(model, q)) return;
                foreach (Polygon face in faces)
                {
                    if (!FaceCoversPoint(face, model.Gf, q)) continue;
                    if (best == null || face.Area < best.Area)
                        best = face;
                }
            }
            Consider(seed);
            if (best != null) return best;
            if (n.Length > 1e-9)
            {
                Vector3d nn = n.GetNormal();
                for (double s = 8.0; s <= 40.0 && best == null; s += 8.0)
                {
                    Consider(seed + nn * s);
                    Consider(seed - nn * s);
                }
            }
            if (best != null) return best;
            Vector3d d = toward - seed;
            double len = d.Length;
            if (len > 1.0)
            {
                Vector3d dir = d / len;
                double max = Math.Min(len, 80.0);
                for (double s = 10.0; s <= max && best == null; s += 10.0)
                    Consider(seed + dir * s);
                if (best == null) Consider(toward);
            }
            return best;
        }

        private static bool FaceCoversPoint(Polygon face, GeometryFactory gf, Point3d seed)
        {
            if (face == null || gf == null || face.IsEmpty) return false;
            try
            {
                var pt = gf.CreatePoint(new Coordinate(seed.X, seed.Y));
                return face.Covers(pt) || face.Contains(pt);
            }
            catch { return false; }
        }

        /// <summary>Ortadan, olmazsa 15 cm yukarı/aşağıdan bu açıklığa uyan hücre.</summary>
        private static Polyline FacePolylineForSpan(
            AltDuzAlanModel model, SpanRec sp,
            Point3d origin, Vector3d u, Vector3d n)
        {
            double clear = sp.LeftInner.DistanceTo(sp.RightInner);
            double tL = (sp.LeftInner - origin).DotProduct(u);
            double tR = (sp.RightInner - origin).DotProduct(u);
            if (tR < tL) { double tmp = tL; tL = tR; tR = tmp; }
            Point3d[] seeds =
            {
                sp.Mid,
                sp.Mid + n * 15.0,
                sp.Mid - n * 15.0
            };
            foreach (Point3d seed in seeds)
            {
                if (!TryFaceContaining(model, seed, out Polygon face)) continue;
                Polyline pl = MemoryPolylineFromPolygon(face);
                if (pl == null) continue;
                if (PolyContains(pl, seed) && BoundaryFitsOpening(pl, origin, u, tL, tR, clear))
                    return pl;
                pl.Dispose();
            }
            return null;
        }

        private static bool CommitSpanBoundary(
            Transaction tr, BlockTableRecord btr, Database db,
            Polyline pl, Point3d seed, List<Polyline> drawn)
        {
            if (pl == null) return false;
            if (drawn != null)
            {
                foreach (Polyline prev in drawn)
                {
                    if (prev != null && PolyContains(prev, seed))
                    {
                        pl.Dispose();
                        return false;
                    }
                }
            }
            pl.Layer = EnsureLayer(tr, db, LayerAltDuzAlan, 6, LineWeight.LineWeight020);
            pl.ColorIndex = 256;
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            if (drawn != null) drawn.Add(pl);
            return true;
        }

        /// <summary>Hattın kestiği açıklık: boy = iki yüz, dik = komşu kirişe kadar (GPR yığını bu bantta).</summary>
        private static Polyline SpanOpeningRect(SpanRec sp, Vector3d u, Vector3d n, List<RawSeg> segs)
        {
            if (u.Length < 1e-9) return null;
            Vector3d uu = u.GetNormal();
            Vector3d nn = n.Length > 1e-9 ? n.GetNormal() : new Vector3d(-uu.Y, uu.X, 0);
            if (nn.Length < 1e-9) nn = new Vector3d(-uu.Y, uu.X, 0);
            double halfPos, halfNeg;
            BaySidesCm(sp.Mid, uu, nn, segs, out halfPos, out halfNeg);
            Point3d p0 = sp.LeftInner;
            Point3d p1 = sp.RightInner;
            var pl = new Polyline();
            pl.AddVertexAt(0, To2d(p0 + nn * halfPos), 0, 0, 0);
            pl.AddVertexAt(1, To2d(p1 + nn * halfPos), 0, 0, 0);
            pl.AddVertexAt(2, To2d(p1 - nn * halfNeg), 0, 0, 0);
            pl.AddVertexAt(3, To2d(p0 - nn * halfNeg), 0, 0, 0);
            pl.Closed = true;
            return pl;
        }

        /// <summary>Hattın iki yanında, bu açıklığın kirişine kadar. GPR yığını hatta yapışık olmak zorunda değil.</summary>
        private static void BaySidesCm(Point3d mid, Vector3d uu, Vector3d nn, List<RawSeg> segs, out double pos, out double neg)
        {
            pos = 700.0;
            neg = 700.0;
            if (segs == null) return;
            foreach (RawSeg s in segs)
            {
                double dx = s.X1 - s.X0, dy = s.Y1 - s.Y0;
                double sl = Math.Sqrt(dx * dx + dy * dy);
                if (sl < 40.0) continue;
                if (Math.Abs(dx * uu.X + dy * uu.Y) / sl < 0.96) continue;
                double signed = (s.X0 - mid.X) * nn.X + (s.Y0 - mid.Y) * nn.Y;
                double ad = Math.Abs(signed);
                if (ad < 40.0 || ad > 1200.0) continue;
                double t = ((mid.X - s.X0) * dx + (mid.Y - s.Y0) * dy) / (sl * sl);
                if (t < -0.15 || t > 1.15) continue;
                if (signed > 0 && ad < pos) pos = ad;
                if (signed < 0 && ad < neg) neg = ad;
            }
            if (pos < 150.0) pos = 150.0;
            if (neg < 150.0) neg = 150.0;
            pos -= 3.0;
            neg -= 3.0;
        }

        private static Polyline MemoryPolylineFromPolygon(Polygon poly)
        {
            if (poly == null || poly.IsEmpty || poly.ExteriorRing == null) return null;
            Coordinate[] cs = poly.ExteriorRing.Coordinates;
            if (cs == null || cs.Length < 4) return null;
            int n = cs.Length;
            if (cs[0].Equals2D(cs[n - 1])) n--;
            if (n < 3) return null;
            var pl = new Polyline();
            int vi = 0;
            for (int i = 0; i < n; i++)
            {
                var p = new Point2d(cs[i].X, cs[i].Y);
                if (vi > 0 && pl.GetPoint2dAt(vi - 1).GetDistanceTo(p) < 0.4) continue;
                pl.AddVertexAt(vi, p, 0, 0, 0);
                vi++;
            }
            if (vi >= 2 && pl.GetPoint2dAt(0).GetDistanceTo(pl.GetPoint2dAt(vi - 1)) < 0.4)
            {
                pl.RemoveVertexAt(vi - 1);
                vi--;
            }
            if (vi < 3)
            {
                pl.Dispose();
                return null;
            }
            DropCollinearVertices(pl);
            if (pl.NumberOfVertices < 3)
            {
                pl.Dispose();
                return null;
            }
            pl.Closed = true;
            return pl;
        }

        /// <summary>Aynı doğru üstündeki ara nokta silinir. Köşe kalır.</summary>
        private static void DropCollinearVertices(Polyline pl)
        {
            if (pl == null) return;
            const double sinTol = 0.0175; // 1°
            bool changed = true;
            while (changed && pl.NumberOfVertices > 3)
            {
                changed = false;
                int n = pl.NumberOfVertices;
                for (int i = 0; i < n && pl.NumberOfVertices > 3; i++)
                {
                    int n2 = pl.NumberOfVertices;
                    Point2d prev = pl.GetPoint2dAt((i + n2 - 1) % n2);
                    Point2d p = pl.GetPoint2dAt(i % n2);
                    Point2d next = pl.GetPoint2dAt((i + 1) % n2);
                    double ax = p.X - prev.X, ay = p.Y - prev.Y;
                    double bx = next.X - p.X, by = next.Y - p.Y;
                    double la = Math.Sqrt(ax * ax + ay * ay);
                    double lb = Math.Sqrt(bx * bx + by * by);
                    if (la < 0.05 || lb < 0.05)
                    {
                        pl.RemoveVertexAt(i % n2);
                        changed = true;
                        break;
                    }
                    double cross = ax * by - ay * bx;
                    double dot = ax * bx + ay * by;
                    if (dot > 0 && Math.Abs(cross) <= sinTol * la * lb)
                    {
                        pl.RemoveVertexAt(i % n2);
                        changed = true;
                        break;
                    }
                }
            }
        }

        /// <summary>Boundary bu açıklığın boyunu aşmasın; komşu açıklığa veya tüm kata taşmasın.</summary>
        private static bool BoundaryFitsOpening(
            Polyline pl, Point3d origin, Vector3d u, double tL, double tR, double clear)
        {
            if (pl == null || clear < 20.0) return false;
            double minT = double.MaxValue, maxT = double.MinValue;
            int n = pl.NumberOfVertices;
            for (int i = 0; i < n; i++)
            {
                Point2d p = pl.GetPoint2dAt(i);
                double t = (p.X - origin.X) * u.X + (p.Y - origin.Y) * u.Y;
                if (t < minT) minT = t;
                if (t > maxT) maxT = t;
            }
            double len = maxT - minT;
            if (len < clear * 0.45) return false;
            if (len > clear * 1.65 + 80.0) return false;
            double overlap = Math.Min(maxT, tR) - Math.Max(minT, tL);
            return overlap >= clear * 0.5;
        }

        private static bool PolyContains(Polyline pl, Point3d p)
        {
            if (pl == null) return false;
            int n = pl.NumberOfVertices;
            if (n < 3) return false;
            // Veritabanına girmemiş poligonda IntersectWith AutoCAD'i düşürür.
            bool inside = false;
            double x = p.X, y = p.Y;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Point2d a = pl.GetPoint2dAt(i);
                Point2d b = pl.GetPoint2dAt(j);
                if ((a.Y > y) == (b.Y > y)) continue;
                double dy = b.Y - a.Y;
                if (Math.Abs(dy) < 1e-12) continue;
                double xHit = (b.X - a.X) * (y - a.Y) / dy + a.X;
                if (x < xHit) inside = !inside;
            }
            return inside;
        }

        private static double DistToPolyline(Polyline pl, Point3d p)
        {
            if (pl == null) return double.MaxValue;
            int n = pl.NumberOfVertices;
            if (n < 2) return double.MaxValue;
            int segs = pl.Closed ? n : n - 1;
            double best = double.MaxValue;
            for (int i = 0; i < segs; i++)
            {
                Point2d a = pl.GetPoint2dAt(i);
                Point2d b = pl.GetPoint2dAt((i + 1) % n);
                double d = DistPointToSeg(p.X, p.Y, a.X, a.Y, b.X, b.Y);
                if (d < best) best = d;
            }
            return best;
        }

        private static double DistPointToSeg(double px, double py, double ax, double ay, double bx, double by)
        {
            double vx = bx - ax, vy = by - ay;
            double len2 = vx * vx + vy * vy;
            double t = len2 < 1e-12 ? 0 : ((px - ax) * vx + (py - ay) * vy) / len2;
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            double dx = px - (ax + t * vx), dy = py - (ay + t * vy);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool TryFaceContaining(AltDuzAlanModel model, Point3d seed, out Polygon cell)
        {
            cell = null;
            if (model?.Faces == null || model.Gf == null) return false;
            var pt = model.Gf.CreatePoint(new Coordinate(seed.X, seed.Y));
            double best = double.MaxValue;
            foreach (Polygon face in model.Faces)
            {
                bool inside = false;
                try { inside = face.Covers(pt) || face.Contains(pt); }
                catch { continue; }
                if (!inside || face.Area >= best) continue;
                best = face.Area;
                cell = face;
            }
            return cell != null;
        }

        private static bool SpanMidInLabeledFace(AltDuzAlanModel model, Point3d mid, List<SlabTextRec> slabTexts)
        {
            return TryFaceContaining(model, mid, out Polygon face)
                && TryLabelInFace(model, face, slabTexts, out _);
        }

        /// <summary>
        /// Açıklık ortası etiketli mesnet hücresindeyse ve boyu hücreyle uyumluysa döşemeyi kabul et.
        /// Hücreden belirgin büyük açıklık burada alınmaz; o hücre ayrı span olarak eklenir.
        /// </summary>
        private static bool TrySlabFromLabeledFace(
            AltDuzAlanModel model, Point3d mid, Point3d origin, Vector3d u, Vector3d n, double spanLen,
            BoundaryIndex bounds, List<ClosedArea> areas, List<SlabTextRec> slabTexts, List<GprSteelRec> gprNotes,
            out SlabInfo slab)
        {
            slab = new SlabInfo { CapMm = 8, AralikCm = 20, HasLabel = false };
            if (!TryFaceContaining(model, mid, out Polygon face)) return false;
            if (!TryLabelInFace(model, face, slabTexts, out SlabTextRec label)) return false;
            if (!TryFaceChord(face, origin, u, out double t0, out double t1)) return false;
            double chord = Math.Abs(t1 - t0);
            if (chord < 20.0) return false;
            if (spanLen > chord * 1.45 + 15.0) return false;

            FillSlabInfoFromBlob(ref slab, label.Text);
            AttachUstKotFromPlanLabel(label, null, ref slab);
            Point3d left = origin + u * t0;
            Point3d right = origin + u * t1;
            AttachGprSteelInRegion(bounds, mid, gprNotes, areas, left, right, u, n,
                slabTexts, slab.Name, ref slab);
            slab.HasLabel = true;
            return true;
        }

        /// <summary>Hat iki yüze tam değmese de etiketli hücreyi çiz.</summary>
        private static void PromoteLabeledFacePicks(
            List<SpanRec> spans, AltDuzAlanModel model, List<SlabTextRec> slabTexts,
            Point3d origin, Vector3d u, double tPick0, double tPick1)
        {
            if (spans == null || model?.Faces == null) return;
            for (int i = 0; i < spans.Count; i++)
            {
                SpanRec sp = spans[i];
                if (sp.InPick) continue;
                double s0 = (sp.LeftInner - origin).DotProduct(u);
                double s1 = (sp.RightInner - origin).DotProduct(u);
                if (IntervalOverlap(s0, s1, tPick0, tPick1) < 20.0) continue;
                if (!TryFaceContaining(model, sp.Mid, out Polygon face)) continue;
                if (!TryLabelInFace(model, face, slabTexts, out _)) continue;
                if (!TryFaceChord(face, origin, u, out double t0, out double t1)) continue;
                double chord = Math.Abs(t1 - t0);
                if (sp.L > chord * 1.45 + 15.0) continue;
                // Hücreyi yalnız hat boydan kesiyorsa ya da hat hücrenin içindeyse çiz.
                // Kenardan girilen uç döşeme (hat orada biter) açılmaz.
                double c0 = Math.Min(t0, t1), c1 = Math.Max(t0, t1);
                bool crosses = tPick0 <= c0 + 20.0 && tPick1 >= c1 - 20.0;
                bool inside = tPick0 >= c0 - 20.0 && tPick1 <= c1 + 20.0;
                if (!crosses && !inside) continue;
                sp.InPick = true;
                sp.ReachLeft = true;
                sp.ReachRight = true;
                spans[i] = sp;
            }
        }

        /// <summary>
        /// Sanal hattın kestiği, içinde döşeme etiketi olan ve henüz açıklığı olmayan hücreye donatı span'i ekle.
        /// </summary>
        private static int AddSpansForLabeledFaces(
            List<SpanRec> spans, AltDuzAlanModel model, List<SlabTextRec> slabTexts,
            List<GprSteelRec> gprNotes, BoundaryIndex bounds, List<ClosedArea> areas,
            Point3d origin, Vector3d u, Vector3d n, double tPick0, double tPick1)
        {
            if (spans == null || model?.Faces == null || model.Faces.Count == 0) return 0;
            int added = 0;
            foreach (Polygon face in model.Faces)
            {
                if (!TryLabelInFace(model, face, slabTexts, out SlabTextRec label)) continue;
                if (!TryFaceChord(face, origin, u, out double t0, out double t1)) continue;
                if (IntervalOverlap(t0, t1, tPick0, tPick1) < 20.0) continue;
                // Hücreyi yalnız hat boydan kesiyorsa ya da hat hücrenin içindeyse ekle.
                double c0 = Math.Min(t0, t1), c1 = Math.Max(t0, t1);
                bool crosses = tPick0 <= c0 + 20.0 && tPick1 >= c1 - 20.0;
                bool inside = tPick0 >= c0 - 20.0 && tPick1 <= c1 + 20.0;
                if (!crosses && !inside) continue;
                bool covered = false;
                foreach (SpanRec sp in spans)
                {
                    if (!sp.InPick) continue;
                    if (SpanCoversChord(sp, origin, u, t0, t1) || PointOfFace(face, sp.Mid, 1.0))
                    {
                        covered = true;
                        break;
                    }
                }
                if (covered) continue;
                // Etiket zaten bir açıklığın içindeyse ikinci donatı/poligon üretme.
                if (LabelInsidePickedSpan(spans, origin, u, label)) continue;

                Point3d left = origin + u * t0;
                Point3d right = origin + u * t1;
                Point3d mid = Mid(left, right);
                if (PointInsideBosluk(areas, mid) || PointInsideAltMesnet(model, mid)) continue;
                Point3d barL = left + u * PaspayiCm;
                Point3d barR = right - u * PaspayiCm;
                if (barL.DistanceTo(barR) < 20.0) continue;

                var slab = new SlabInfo { CapMm = 8, AralikCm = 20, HasLabel = false };
                FillSlabInfoFromBlob(ref slab, label.Text);
                AttachUstKotFromPlanLabel(label, null, ref slab);
                AttachGprSteelInRegion(bounds, mid, gprNotes, areas, left, right, u, n,
                    slabTexts, slab.Name, ref slab);
                slab.HasLabel = true;

                spans.Add(new SpanRec
                {
                    SupportLeft = -3000 - spans.Count,
                    SupportRight = -1,
                    L = left.DistanceTo(right),
                    LeftInner = left,
                    RightInner = right,
                    LeftOuter = left,
                    RightOuter = right,
                    BarL = barL,
                    BarR = barR,
                    Mid = mid,
                    Slab = slab,
                    InPick = true,
                    ReachLeft = true,
                    ReachRight = true,
                    DuzOnly = false,
                    LeftIsMesnet = true,
                    RightIsMesnet = true
                });
                added++;
            }
            return added;
        }

        private static bool TryLabelInFace(
            AltDuzAlanModel model, Polygon face, List<SlabTextRec> slabTexts, out SlabTextRec label)
        {
            label = default;
            if (face == null || slabTexts == null) return false;
            Coordinate c = null;
            try { c = face.Centroid?.Coordinate; }
            catch { c = null; }
            var anchor = c != null ? new Point3d(c.X, c.Y, 0) : Point3d.Origin;
            double best = double.MaxValue;
            bool ok = false;
            foreach (SlabTextRec st in slabTexts)
            {
                bool inPos = PointOfFace(face, st.Pos, 12.0);
                bool inIns = PointOfFace(face, st.Insert, 12.0);
                if (!inPos && !inIns) continue;
                Point3d probe = inPos ? st.Pos : st.Insert;
                if (PointInsideAltMesnet(model, probe)) continue;
                if (CoveredBySmallerFace(model, face, probe)) continue;
                double d = probe.DistanceTo(anchor);
                if (d >= best) continue;
                best = d;
                label = st;
                ok = true;
            }
            return ok;
        }

        private static bool TryFaceChord(Polygon face, Point3d origin, Vector3d u, out double t0, out double t1)
        {
            t0 = 0;
            t1 = 0;
            if (face == null || face.IsEmpty) return false;
            const double big = 1.0e7;
            var line = face.Factory.CreateLineString(new[]
            {
                new Coordinate(origin.X - u.X * big, origin.Y - u.Y * big),
                new Coordinate(origin.X + u.X * big, origin.Y + u.Y * big)
            });
            Geometry inter;
            try { inter = face.Intersection(line); }
            catch { return false; }
            if (inter == null || inter.IsEmpty) return false;
            double min = double.MaxValue, max = double.MinValue;
            CollectChordT(inter, origin, u, ref min, ref max);
            if (max - min < 20.0) return false;
            t0 = min;
            t1 = max;
            return true;
        }

        private static void CollectChordT(Geometry g, Point3d origin, Vector3d u, ref double min, ref double max)
        {
            if (g == null || g.IsEmpty) return;
            if (g is Point pt)
            {
                double t = (pt.X - origin.X) * u.X + (pt.Y - origin.Y) * u.Y;
                if (t < min) min = t;
                if (t > max) max = t;
                return;
            }
            if (g is LineString ls)
            {
                foreach (Coordinate c in ls.Coordinates)
                {
                    double t = (c.X - origin.X) * u.X + (c.Y - origin.Y) * u.Y;
                    if (t < min) min = t;
                    if (t > max) max = t;
                }
                return;
            }
            for (int i = 0; i < g.NumGeometries; i++)
                CollectChordT(g.GetGeometryN(i), origin, u, ref min, ref max);
        }

        /// <summary>Etiket, seçili bir açıklığın iki yüzü arasındaysa o döşeme zaten çizilecek.</summary>
        private static bool LabelInsidePickedSpan(
            List<SpanRec> spans, Point3d origin, Vector3d u, SlabTextRec label)
        {
            if (spans == null) return false;
            double tl = (label.Pos - origin).DotProduct(u);
            foreach (SpanRec sp in spans)
            {
                if (!sp.InPick) continue;
                double s0 = (sp.LeftInner - origin).DotProduct(u);
                double s1 = (sp.RightInner - origin).DotProduct(u);
                double lo = Math.Min(s0, s1), hi = Math.Max(s0, s1);
                if (tl >= lo - 15.0 && tl <= hi + 15.0) return true;
            }
            return false;
        }

        /// <summary>
        /// Aynı döşeme adı iki açıklığa yazıldıysa kiriş açıklığını tut, taşan yüzden gelen kopyayı kapat.
        /// İkisi de kiriş açıklığıysa etikete yakın olan kalır.
        /// </summary>
        private static void DropDuplicateSlabSpans(List<SpanRec> spans, List<SlabTextRec> slabTexts)
        {
            if (spans == null) return;
            var best = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < spans.Count; i++)
            {
                if (!spans[i].InPick) continue;
                string name = spans[i].Slab.Name;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!best.TryGetValue(name, out int j))
                {
                    best[name] = i;
                    continue;
                }
                // Aynı isim başka açıklıktaysa (hat birden çok döşeme kesiyor) ikisi de kalsın.
                if (spans[i].Mid.DistanceTo(spans[j].Mid) > (spans[i].L + spans[j].L) * 0.45)
                    continue;
                int keep = PreferSlabSpan(spans[j], spans[i], slabTexts, name) ? j : i;
                int drop = keep == j ? i : j;
                SpanRec d = spans[drop];
                d.InPick = false;
                spans[drop] = d;
                best[name] = keep;
            }
        }

        private static bool PreferSlabSpan(SpanRec a, SpanRec b, List<SlabTextRec> slabTexts, string name)
        {
            bool aSyn = a.SupportLeft <= -2500;
            bool bSyn = b.SupportLeft <= -2500;
            if (aSyn != bSyn) return !aSyn;
            Point3d? lab = SlabLabelPos(slabTexts, name);
            if (!lab.HasValue) return a.L <= b.L;
            return a.Mid.DistanceTo(lab.Value) <= b.Mid.DistanceTo(lab.Value);
        }

        private static Point3d? SlabLabelPos(List<SlabTextRec> slabTexts, string name)
        {
            if (slabTexts == null || string.IsNullOrWhiteSpace(name)) return null;
            foreach (SlabTextRec st in slabTexts)
            {
                if (string.IsNullOrEmpty(st.Text)) continue;
                Match m = RxSlabName.Match(st.Text);
                if (!m.Success) continue;
                if (string.Equals(m.Groups[1].Value, name, StringComparison.OrdinalIgnoreCase))
                    return st.Pos;
            }
            return null;
        }

        private static bool SpanCoversChord(SpanRec sp, Point3d origin, Vector3d u, double t0, double t1)
        {
            double s0 = (sp.LeftInner - origin).DotProduct(u);
            double s1 = (sp.RightInner - origin).DotProduct(u);
            double len = Math.Abs(t1 - t0);
            if (len < 1.0) return false;
            return IntervalOverlap(s0, s1, t0, t1) >= len * 0.55;
        }

        private static double IntervalOverlap(double a0, double a1, double b0, double b1)
        {
            double lo = Math.Max(Math.Min(a0, a1), Math.Min(b0, b1));
            double hi = Math.Min(Math.Max(a0, a1), Math.Max(b0, b1));
            return hi - lo;
        }

        /// <summary>
        /// Normal döşeme: kot ve isim, alt düz alanının içindeki DOSEME ISMI / KOT YAZI.
        /// Komşu hücrenin kotu alınmaz. Kot farkı sonraki L/4 kararında kullanılır.
        /// </summary>
        private static void ApplyNormalSlabKotFromArea(
            List<SpanRec> spans, AltDuzAlanModel model, List<SlabTextRec> slabTexts)
        {
            if (spans == null || model?.Faces == null || model.Faces.Count == 0) return;
            List<KotTextRec> saved = _kotCache;
            for (int i = 0; i < spans.Count; i++)
            {
                SpanRec sp = spans[i];
                if (sp.DuzOnly || !sp.InPick) continue;
                if (!TryFaceContaining(model, sp.Mid, out Polygon face)) continue;

                bool hasLab = false;
                SlabTextRec label = default;
                double bestD = double.MaxValue;
                if (slabTexts != null)
                {
                    foreach (SlabTextRec st in slabTexts)
                    {
                        if (!PointOfFace(face, st.Pos, 12.0) && !PointOfFace(face, st.Insert, 12.0))
                            continue;
                        double d = st.Pos.DistanceTo(sp.Mid);
                        if (d >= bestD) continue;
                        bestD = d;
                        label = st;
                        hasLab = true;
                    }
                }

                SlabInfo info = sp.Slab;
                List<GprSteelRec> gpr = info.GprSteels;
                int cap = info.CapMm;
                int ar = info.AralikCm;
                int? adet = info.Adet;
                if (hasLab)
                    FillSlabInfoFromBlob(ref info, label.Text);
                info.GprSteels = gpr;
                if (info.CapMm <= 0) info.CapMm = cap;
                if (info.AralikCm <= 0) info.AralikCm = ar;
                if (!info.Adet.HasValue) info.Adet = adet;

                var inside = new List<KotTextRec>();
                if (saved != null)
                {
                    Point3d anchor = hasLab ? label.Insert : sp.Mid;
                    foreach (KotTextRec k in saved)
                    {
                        bool inFace = PointOfFace(face, k.Insert, 8.0) || PointOfFace(face, k.Pos, 8.0);
                        bool nearLabel = hasLab && k.Insert.DistanceTo(anchor) <= 90.0
                            && !CoveredBySmallerFace(model, face, k.Insert);
                        if (inFace || nearLabel)
                            inside.Add(k);
                    }
                }
                if (inside.Count > 0)
                {
                    double? oldKot = info.KotM;
                    info.KotM = null;
                    if (hasLab)
                    {
                        _kotCache = inside;
                        AttachUstKotFromPlanLabel(label, null, ref info);
                        _kotCache = saved;
                    }
                    if (!info.KotM.HasValue)
                        info.KotM = UstKotOfCluster(inside, hasLab ? label.Insert : sp.Mid) ?? oldKot;
                }
                if (hasLab) info.HasLabel = true;
                sp.Slab = info;
                spans[i] = sp;
            }
            _kotCache = saved;
        }

        private static bool PointOfFace(Polygon face, Point3d p, double extraCm)
        {
            if (face == null) return false;
            var pt = face.Factory.CreatePoint(new Coordinate(p.X, p.Y));
            try
            {
                if (face.Covers(pt) || face.Contains(pt)) return true;
                return extraCm > 0 && face.Distance(pt) <= extraCm;
            }
            catch { return false; }
        }

        private static bool CoveredBySmallerFace(AltDuzAlanModel model, Polygon self, Point3d p)
        {
            if (model?.Faces == null || model.Gf == null) return false;
            var pt = model.Gf.CreatePoint(new Coordinate(p.X, p.Y));
            foreach (Polygon f in model.Faces)
            {
                if (ReferenceEquals(f, self) || f.Area >= self.Area) continue;
                try { if (f.Covers(pt)) return true; }
                catch { }
            }
            return false;
        }

        /// <summary>Aynı etikete ait kot çiftinden üst kot (daha büyük m).</summary>
        private static double? UstKotOfCluster(List<KotTextRec> kots, Point3d near)
        {
            if (kots == null || kots.Count == 0) return null;
            int best = 0;
            double bestD = double.MaxValue;
            for (int i = 0; i < kots.Count; i++)
            {
                double d = kots[i].Insert.DistanceTo(near);
                if (d < bestD) { bestD = d; best = i; }
            }
            double ust = kots[best].ValueM;
            Point3d b = kots[best].Insert;
            for (int i = 0; i < kots.Count; i++)
            {
                if (kots[i].Insert.DistanceTo(b) > 35.0) continue;
                if (kots[i].ValueM > ust) ust = kots[i].ValueM;
            }
            return ust;
        }

        /// <summary>Alt düz etiketinin altında yalnız döşeme adı (D1-04). Çerçeve ve tarama yok.</summary>
        private static void PlaceAltDuzSlabMark(
            Transaction tr, BlockTableRecord btr, Database db,
            Point3d barRef, Vector3d along, Vector3d up, string slabName, ObjectId textStyleId)
        {
            if (string.IsNullOrWhiteSpace(slabName)) return;
            double ang = ReadableTextAngle(along);
            var textUp = new Vector3d(-Math.Sin(ang), Math.Cos(ang), 0);
            bool localAbove = textUp.DotProduct(up) >= 0;
            Vector3d labelSide = localAbove ? textUp : -textUp;
            Point3d labelBottom = barRef + labelSide * LabelGapCm;
            Point3d center = labelBottom - labelSide * (2.0 + 4.0);

            string isim = EnsureLayer(tr, db, "DOSEME ISMI (BEYKENT)", 3, LineWeight.LineWeight020);
            var t = new DBText();
            t.SetDatabaseDefaults();
            t.TextString = slabName.Trim();
            t.Height = 8.0;
            if (!textStyleId.IsNull) t.TextStyleId = textStyleId;
            t.Layer = isim;
            t.Rotation = ang;
            t.HorizontalMode = TextHorizontalMode.TextCenter;
            t.VerticalMode = TextVerticalMode.TextVerticalMid;
            t.AlignmentPoint = center;
            t.Position = center;
            t.AdjustAlignment(db);
            btr.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
        }

        /// <summary>Mesnet sınırına giren her eğri: düz çizgi, eski polyline, yay, mline, kolon bloğu.</summary>
        private static void CollectMesnetGeometry(
            Transaction tr, Entity ent, List<RawSeg> dest, Matrix3d xf, int depth)
        {
            if (ent == null || dest == null || depth > 8) return;
            try
            {
                if (ent is BlockReference br)
                {
                    CollectBlockMesnet(tr, br, dest, xf, depth);
                    return;
                }
                if (ent is Mline ml && depth == 0)
                {
                    var objs = new DBObjectCollection();
                    ml.Explode(objs);
                    foreach (DBObject o in objs)
                    {
                        try
                        {
                            if (o is Entity e)
                                CollectMesnetGeometry(tr, e, dest, xf, depth + 1);
                        }
                        finally { o.Dispose(); }
                    }
                    return;
                }
                var local = new List<RawSeg>();
                if (ent is Polyline2d p2)
                    AddOldPolylineSegs(tr, p2, local);
                else if (ent is Arc arc)
                    AddArcSegs(arc, local);
                else
                    CollectBoundarySegs(ent, local);
                AppendTransformed(dest, local, xf);
            }
            catch { }
        }

        private static void CollectBlockMesnet(
            Transaction tr, BlockReference br, List<RawSeg> dest, Matrix3d xf, int depth)
        {
            if (depth > 8) return;
            BlockTableRecord def;
            try { def = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord; }
            catch { return; }
            if (def == null) return;
            Matrix3d nxf = xf * br.BlockTransform;
            foreach (ObjectId id in def)
            {
                Entity nested;
                try { nested = tr.GetObject(id, OpenMode.ForRead) as Entity; }
                catch { continue; }
                if (nested == null) continue;
                if (nested is DBText || nested is MText || nested is Hatch
                    || nested is Autodesk.AutoCAD.DatabaseServices.Dimension
                    || nested is AttributeDefinition || nested is Leader || nested is MLeader
                    || nested is DBPoint || nested is ProxyEntity || nested is RasterImage)
                    continue;
                string layer = nested.Layer ?? "";
                bool layer0 = string.Equals(layer, "0", StringComparison.OrdinalIgnoreCase);
                if (!layer0 && !IsMesnetLayer(layer, null)) continue;
                CollectMesnetGeometry(tr, nested, dest, nxf, depth + 1);
            }
        }

        private static void AddOldPolylineSegs(Transaction tr, Polyline2d p2, List<RawSeg> dest)
        {
            var verts = new List<Point2d>();
            var bulges = new List<double>();
            foreach (ObjectId vid in p2)
            {
                try
                {
                    if (tr.GetObject(vid, OpenMode.ForRead) is Vertex2d v)
                    {
                        verts.Add(new Point2d(v.Position.X, v.Position.Y));
                        bulges.Add(v.Bulge);
                    }
                }
                catch { }
            }
            int n = verts.Count;
            if (n < 2) return;
            int segs = p2.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                Point2d a = verts[i];
                Point2d b = verts[(i + 1) % n];
                double bulge = i < bulges.Count ? bulges[i] : 0;
                AddBulgeSegs(dest, a.X, a.Y, b.X, b.Y, bulge);
            }
        }

        private static void AddArcSegs(Arc arc, List<RawSeg> dest)
        {
            if (arc == null || arc.Radius < 0.5) return;
            double a0 = arc.StartAngle;
            double sweep = arc.EndAngle - a0;
            while (sweep < 1e-9) sweep += Math.PI * 2.0;
            int steps = Math.Max(2, (int)Math.Ceiling(sweep / (Math.PI / 12.0)));
            double px = arc.Center.X + arc.Radius * Math.Cos(a0);
            double py = arc.Center.Y + arc.Radius * Math.Sin(a0);
            for (int s = 1; s <= steps; s++)
            {
                double a = a0 + sweep * s / steps;
                double qx = arc.Center.X + arc.Radius * Math.Cos(a);
                double qy = arc.Center.Y + arc.Radius * Math.Sin(a);
                if (s == steps)
                {
                    qx = arc.EndPoint.X;
                    qy = arc.EndPoint.Y;
                }
                AddSeg(dest, px, py, qx, qy);
                px = qx;
                py = qy;
            }
        }

        private static void AppendTransformed(List<RawSeg> dest, List<RawSeg> local, Matrix3d xf)
        {
            if (local == null || local.Count == 0) return;
            bool plain = xf.IsEqualTo(Matrix3d.Identity);
            for (int i = 0; i < local.Count; i++)
            {
                RawSeg s = local[i];
                if (plain)
                {
                    dest.Add(s);
                    continue;
                }
                Point3d a = new Point3d(s.X0, s.Y0, 0).TransformBy(xf);
                Point3d b = new Point3d(s.X1, s.Y1, 0).TransformBy(xf);
                AddSeg(dest, a.X, a.Y, b.X, b.Y);
            }
        }

        private static void CollectBoundarySegs(Entity ent, List<RawSeg> segs)
        {
            if (ent is Line ln)
            {
                AddSeg(segs, ln.StartPoint.X, ln.StartPoint.Y, ln.EndPoint.X, ln.EndPoint.Y);
                return;
            }
            if (ent is Polyline pl)
            {
                int n = pl.NumberOfVertices;
                if (n < 2) return;
                int segCount = pl.Closed ? n : n - 1;
                for (int i = 0; i < segCount; i++)
                {
                    Point2d a = pl.GetPoint2dAt(i);
                    int j = (i + 1) % n;
                    Point2d b = pl.GetPoint2dAt(j);
                    AddBulgeSegs(segs, a.X, a.Y, b.X, b.Y, pl.GetBulgeAt(i));
                }
                return;
            }
            if (ent is Circle c)
            {
                const int steps = 24;
                double r = c.Radius;
                if (r < 0.5) return;
                for (int i = 0; i < steps; i++)
                {
                    double a0 = 2.0 * Math.PI * i / steps;
                    double a1 = 2.0 * Math.PI * (i + 1) / steps;
                    AddSeg(segs,
                        c.Center.X + r * Math.Cos(a0), c.Center.Y + r * Math.Sin(a0),
                        c.Center.X + r * Math.Cos(a1), c.Center.Y + r * Math.Sin(a1));
                }
            }
        }

        private static void AddBulgeSegs(List<RawSeg> segs, double x0, double y0, double x1, double y1, double bulge)
        {
            if (Math.Abs(bulge) < 1e-6)
            {
                AddSeg(segs, x0, y0, x1, y1);
                return;
            }
            double dx = x1 - x0, dy = y1 - y0;
            double chord = Math.Sqrt(dx * dx + dy * dy);
            if (chord < 0.2) return;
            double theta = 4.0 * Math.Atan(bulge);
            double radius = chord / (2.0 * Math.Sin(Math.Abs(theta) / 2.0));
            double mx = (x0 + x1) * 0.5, my = (y0 + y1) * 0.5;
            double nx = -dy / chord, ny = dx / chord;
            double h = Math.Sqrt(Math.Max(0.0, radius * radius - chord * chord * 0.25));
            if (bulge < 0) { nx = -nx; ny = -ny; }
            double cx = mx + nx * h, cy = my + ny * h;
            double a0 = Math.Atan2(y0 - cy, x0 - cx);
            int steps = Math.Max(4, (int)Math.Ceiling(Math.Abs(theta) / (Math.PI / 8.0)));
            double px = x0, py = y0;
            for (int s = 1; s <= steps; s++)
            {
                double a = a0 + theta * s / steps;
                double qx = cx + radius * Math.Cos(a);
                double qy = cy + radius * Math.Sin(a);
                if (s == steps) { qx = x1; qy = y1; }
                AddSeg(segs, px, py, qx, qy);
                px = qx;
                py = qy;
            }
        }

        private static void AddSeg(List<RawSeg> segs, double x0, double y0, double x1, double y1)
        {
            if (Dist(x0, y0, x1, y1) < 0.2) return;
            segs.Add(new RawSeg { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 });
        }

        private static double Dist(double x0, double y0, double x1, double y1)
        {
            double dx = x1 - x0, dy = y1 - y0;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Yakın uçları tek noktaya toplar; kiriş LINE'ı kolon köşesine bağlansın.</summary>
        private static List<RawSeg> SnapSegJoints(List<RawSeg> segs, double tolCm)
        {
            var nodes = new List<Coordinate>();
            var buckets = new Dictionary<long, List<int>>();
            double tol2 = tolCm * tolCm;
            int Node(double x, double y)
            {
                int ix = (int)Math.Floor(x / tolCm);
                int iy = (int)Math.Floor(y / tolCm);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        long key = ((long)(ix + dx) << 32) ^ (uint)(iy + dy);
                        if (!buckets.TryGetValue(key, out List<int> hit)) continue;
                        foreach (int idx in hit)
                        {
                            double ddx = nodes[idx].X - x, ddy = nodes[idx].Y - y;
                            if (ddx * ddx + ddy * ddy <= tol2) return idx;
                        }
                    }
                }
                int id = nodes.Count;
                nodes.Add(new Coordinate(x, y));
                long k0 = ((long)ix << 32) ^ (uint)iy;
                if (!buckets.TryGetValue(k0, out List<int> list))
                {
                    list = new List<int>();
                    buckets[k0] = list;
                }
                list.Add(id);
                return id;
            }

            var outSegs = new List<RawSeg>(segs.Count);
            foreach (RawSeg s in segs)
            {
                int a = Node(s.X0, s.Y0);
                int b = Node(s.X1, s.Y1);
                if (a == b) continue;
                outSegs.Add(new RawSeg
                {
                    X0 = nodes[a].X, Y0 = nodes[a].Y,
                    X1 = nodes[b].X, Y1 = nodes[b].Y
                });
            }
            return outSegs;
        }

        /// <summary>
        /// Bay boyunca tek başına duran çapraz (paralel eşi yok) döşeme hücresini keser.
        /// Yatay/düşey perde ve kalınlığı olan eğik duvar (paralel çifti olan) kalır.
        /// Çapraz, kirişe oturan dik köşe ile değiştirilir; oturmuyorsa yalnız silinir.
        /// </summary>
        private static List<RawSeg> OrthogonalizeLoneDiagonals(List<RawSeg> segs)
        {
            if (segs == null || segs.Count < 3) return segs;
            var drop = new bool[segs.Count];
            var add = new List<RawSeg>();
            for (int i = 0; i < segs.Count; i++)
            {
                if (!IsLoneDiagonal(segs, i)) continue;
                drop[i] = true;
                RawSeg s = segs[i];
                double da = MinDistToOtherSeg(s.X0, s.Y1, segs, i);
                double db = MinDistToOtherSeg(s.X1, s.Y0, segs, i);
                bool aOn = da <= 25.0;
                bool bOn = db <= 25.0;
                if (!aOn && !bOn) continue;
                if (aOn && bOn && Math.Abs(da - db) < 8.0) continue;
                double cx, cy;
                if (!bOn || da <= db) { cx = s.X0; cy = s.Y1; }
                else { cx = s.X1; cy = s.Y0; }
                add.Add(new RawSeg { X0 = s.X0, Y0 = s.Y0, X1 = cx, Y1 = cy });
                add.Add(new RawSeg { X0 = cx, Y0 = cy, X1 = s.X1, Y1 = s.Y1 });
            }
            if (!drop.AnyTrue()) return segs;
            var result = new List<RawSeg>(segs.Count + add.Count);
            for (int i = 0; i < segs.Count; i++)
                if (!drop[i]) result.Add(segs[i]);
            result.AddRange(add);
            return result;
        }

        private static bool AnyTrue(this bool[] flags)
        {
            if (flags == null) return false;
            for (int i = 0; i < flags.Length; i++)
                if (flags[i]) return true;
            return false;
        }

        /// <summary>Uzun, eksenden kaçık ve paralel eşi olmayan kenar.</summary>
        private static bool IsLoneDiagonal(List<RawSeg> segs, int i)
        {
            RawSeg s = segs[i];
            double dx = s.X1 - s.X0, dy = s.Y1 - s.Y0;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 150.0) return false;
            double deg = Math.Abs(Math.Atan2(dy, dx) * 180.0 / Math.PI) % 180.0;
            if (deg > 90.0) deg = 180.0 - deg;
            if (Math.Min(deg, 90.0 - deg) < 8.0) return false;
            return !HasParallelMate(segs, i, dx / len, dy / len, len);
        }

        private static bool HasParallelMate(List<RawSeg> segs, int i, double ux, double uy, double len)
        {
            RawSeg s = segs[i];
            double nx = -uy, ny = ux;
            for (int j = 0; j < segs.Count; j++)
            {
                if (j == i) continue;
                RawSeg o = segs[j];
                double ex = o.X1 - o.X0, ey = o.Y1 - o.Y0;
                double el = Math.Sqrt(ex * ex + ey * ey);
                if (el < 80.0) continue;
                if (Math.Abs(ex * ux + ey * uy) / el < 0.995) continue;
                double mx = (o.X0 + o.X1) * 0.5 - (s.X0 + s.X1) * 0.5;
                double my = (o.Y0 + o.Y1) * 0.5 - (s.Y0 + s.Y1) * 0.5;
                double dist = Math.Abs(mx * nx + my * ny);
                if (dist < 6.0 || dist > 80.0) continue;
                double b0 = (o.X0 - s.X0) * ux + (o.Y0 - s.Y0) * uy;
                double b1 = (o.X1 - s.X0) * ux + (o.Y1 - s.Y0) * uy;
                if (b0 > b1) { double t = b0; b0 = b1; b1 = t; }
                double ov = Math.Min(len, b1) - Math.Max(0.0, b0);
                if (ov >= 80.0) return true;
            }
            return false;
        }

        private static double MinDistToOtherSeg(double x, double y, List<RawSeg> segs, int skip)
        {
            double best = double.MaxValue;
            for (int j = 0; j < segs.Count; j++)
            {
                if (j == skip) continue;
                ClosestOnSeg(x, y, segs[j], out _, out _, out double d2);
                if (d2 < best) best = d2;
            }
            return best == double.MaxValue ? double.MaxValue : Math.Sqrt(best);
        }

        /// <summary>
        /// Uç bir kenarın ortasına 6 cm'den yakınsa kısa bağ ekle. Köşe kaçıran kiriş hücresi kapansın.
        /// Neredeyse paralel uzun kenarlara (kiriş iki yüzü) bağ atılmaz.
        /// </summary>
        private static void BridgeOpenEnds(List<RawSeg> segs, double tolCm)
        {
            if (segs == null || segs.Count == 0) return;
            int n = segs.Count;
            var extra = new List<RawSeg>();
            double tol2 = tolCm * tolCm;
            for (int i = 0; i < n; i++)
            {
                RawSeg s = segs[i];
                TryBridgeEnd(segs, n, i, s.X0, s.Y0, s.X1, s.Y1, tolCm, tol2, extra);
                TryBridgeEnd(segs, n, i, s.X1, s.Y1, s.X0, s.Y0, tolCm, tol2, extra);
            }
            segs.AddRange(extra);
        }

        private static void TryBridgeEnd(
            List<RawSeg> segs, int n, int self,
            double px, double py, double ox, double oy,
            double tolCm, double tol2, List<RawSeg> extra)
        {
            double vx = ox - px, vy = oy - py;
            double slen = Math.Sqrt(vx * vx + vy * vy);
            if (slen < 1e-6) return;
            double best = tol2;
            double bx = 0, by = 0;
            bool hit = false;
            for (int j = 0; j < n; j++)
            {
                if (j == self) continue;
                RawSeg o = segs[j];
                ClosestOnSeg(px, py, o, out double cx, out double cy, out double d2);
                if (d2 < 0.04 || d2 >= best) continue;
                double ex = o.X1 - o.X0, ey = o.Y1 - o.Y0;
                double elen = Math.Sqrt(ex * ex + ey * ey);
                if (elen < 1e-6) continue;
                double cos = Math.Abs(ex * vx + ey * vy) / (elen * slen);
                double dEnd = Math.Min(Dist(cx, cy, o.X0, o.Y0), Dist(cx, cy, o.X1, o.Y1));
                if (cos > 0.94 && dEnd > tolCm) continue;
                best = d2;
                bx = cx;
                by = cy;
                hit = true;
            }
            if (!hit) return;
            // Köprü kirişin kendi ucu yönünde olsun. Yana kaçan bağ poligonu çapraz keser.
            double fx = px - ox, fy = py - oy;
            double fl = Math.Sqrt(fx * fx + fy * fy);
            double bxv = bx - px, byv = by - py;
            double bl = Math.Sqrt(bxv * bxv + byv * byv);
            if (fl < 1e-6 || bl < 1e-6) return;
            double align = (fx * bxv + fy * byv) / (fl * bl);
            if (align < 0.70) return;
            extra.Add(new RawSeg { X0 = px, Y0 = py, X1 = bx, Y1 = by });
        }

        private static void ClosestOnSeg(double px, double py, RawSeg o, out double cx, out double cy, out double d2)
        {
            double vx = o.X1 - o.X0, vy = o.Y1 - o.Y0;
            double len2 = vx * vx + vy * vy;
            double t = len2 < 1e-8 ? 0 : ((px - o.X0) * vx + (py - o.Y0) * vy) / len2;
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            cx = o.X0 + t * vx;
            cy = o.Y0 + t * vy;
            double dx = px - cx, dy = py - cy;
            d2 = dx * dx + dy * dy;
        }

        private static int DrawExteriorRings(Transaction tr, BlockTableRecord btr, Geometry geom, string layer)
        {
            if (geom == null || geom.IsEmpty) return 0;
            if (geom is Polygon poly)
                return DrawRing(tr, btr, poly.ExteriorRing, layer) ? 1 : 0;
            int n = 0;
            for (int i = 0; i < geom.NumGeometries; i++)
                n += DrawExteriorRings(tr, btr, geom.GetGeometryN(i), layer);
            return n;
        }

        private static bool DrawRing(Transaction tr, BlockTableRecord btr, LineString ring, string layer)
        {
            if (ring == null || ring.NumPoints < 4) return false;
            var pts = new List<Point2d>();
            Coordinate[] cs = ring.Coordinates;
            int n = cs.Length;
            if (n >= 2 && cs[0].Equals2D(cs[n - 1])) n--;
            for (int i = 0; i < n; i++)
            {
                var p = new Point2d(cs[i].X, cs[i].Y);
                if (pts.Count > 0 && pts[pts.Count - 1].GetDistanceTo(p) < 0.4) continue;
                pts.Add(p);
            }
            if (pts.Count >= 2 && pts[0].GetDistanceTo(pts[pts.Count - 1]) < 0.4)
                pts.RemoveAt(pts.Count - 1);
            pts = FillSingleAxisCut(pts);
            pts = DropCollinear(pts, 0.4);
            if (pts.Count < 3) return false;
            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            for (int i = 0; i < pts.Count; i++)
                pl.AddVertexAt(i, pts[i], 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layer;
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            return true;
        }

        /// <summary>
        /// Dikdörtgen hücrenin tek çaprazı, eksik köşeyi keser. Yalnız o kenar dik köşeye
        /// tamamlanır. Birden fazla çapraz (eğik döşeme) olduğu gibi kalır.
        /// </summary>
        private static List<Point2d> FillSingleAxisCut(List<Point2d> pts)
        {
            if (pts == null || pts.Count < 3) return pts;
            int n = pts.Count;
            int diag = -1;
            for (int i = 0; i < n; i++)
            {
                if (!EdgeOffAxis(pts[i], pts[(i + 1) % n], 40.0)) continue;
                if (diag >= 0) return pts;
                diag = i;
            }
            if (diag < 0) return pts;
            for (int i = 0; i < n; i++)
            {
                if (i == diag) continue;
                if (EdgeOffAxis(pts[i], pts[(i + 1) % n], 1.0)) return pts;
            }

            Point2d p = pts[diag];
            Point2d q = pts[(diag + 1) % n];
            double minX = pts[0].X, maxX = pts[0].X, minY = pts[0].Y, maxY = pts[0].Y;
            for (int i = 1; i < n; i++)
            {
                if (pts[i].X < minX) minX = pts[i].X;
                if (pts[i].X > maxX) maxX = pts[i].X;
                if (pts[i].Y < minY) minY = pts[i].Y;
                if (pts[i].Y > maxY) maxY = pts[i].Y;
            }

            Point2d c1 = new Point2d(p.X, q.Y);
            Point2d c2 = new Point2d(q.X, p.Y);
            bool OnBox(Point2d c) =>
                (Math.Abs(c.X - minX) < 1.0 || Math.Abs(c.X - maxX) < 1.0)
                && (Math.Abs(c.Y - minY) < 1.0 || Math.Abs(c.Y - maxY) < 1.0);
            bool Exists(Point2d c)
            {
                for (int i = 0; i < n; i++)
                    if (pts[i].GetDistanceTo(c) < 1.0) return true;
                return false;
            }

            Point2d corner = Point2d.Origin;
            bool found = false;
            if (OnBox(c1) && !Exists(c1)) { corner = c1; found = true; }
            else if (OnBox(c2) && !Exists(c2)) { corner = c2; found = true; }
            if (!found) return pts;

            var next = new List<Point2d>(n + 1);
            for (int i = 0; i < n; i++)
            {
                next.Add(pts[i]);
                if (i == diag) next.Add(corner);
            }
            return next;
        }

        private static bool EdgeOffAxis(Point2d a, Point2d b, double minLen)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < minLen) return false;
            double deg = Math.Abs(Math.Atan2(dy, dx) * 180.0 / Math.PI) % 180.0;
            if (deg > 90.0) deg = 180.0 - deg;
            return Math.Min(deg, 90.0 - deg) >= 8.0;
        }

        private static List<Point2d> DropCollinear(List<Point2d> pts, double tolCm)
        {
            if (pts == null || pts.Count < 4) return pts;
            var list = new List<Point2d>(pts);
            bool changed = true;
            while (changed && list.Count >= 4)
            {
                changed = false;
                int n = list.Count;
                var next = new List<Point2d>(n);
                for (int i = 0; i < n; i++)
                {
                    Point2d a = list[(i + n - 1) % n];
                    Point2d b = list[i];
                    Point2d c = list[(i + 1) % n];
                    double abx = b.X - a.X, aby = b.Y - a.Y;
                    double bcx = c.X - b.X, bcy = c.Y - b.Y;
                    double cross = Math.Abs(abx * bcy - aby * bcx);
                    double lab = Math.Sqrt(abx * abx + aby * aby);
                    double lbc = Math.Sqrt(bcx * bcx + bcy * bcy);
                    if (lab > 1e-6 && lbc > 1e-6 && cross <= tolCm * Math.Min(lab, lbc))
                    {
                        changed = true;
                        continue;
                    }
                    next.Add(b);
                }
                if (next.Count >= 3) list = next;
                else break;
            }
            return list;
        }

        private static Geometry ComponentContaining(Geometry geom, NetTopologySuite.Geometries.Point pt)
        {
            if (geom == null || geom.IsEmpty || pt == null) return null;
            if (geom is Polygon poly)
                return poly.Covers(pt) || poly.Contains(pt) ? poly : null;
            Geometry hit = null;
            double best = double.MaxValue;
            for (int i = 0; i < geom.NumGeometries; i++)
            {
                Geometry g = geom.GetGeometryN(i);
                Geometry sub = ComponentContaining(g, pt);
                if (sub == null) continue;
                if (sub.Area < best)
                {
                    best = sub.Area;
                    hit = sub;
                }
            }
            return hit;
        }

        private static Polygon SmallestContaining(List<Polygon> rings, Coordinate probe)
        {
            if (rings == null || probe == null) return null;
            Polygon best = null;
            double bestA = double.MaxValue;
            var pt = new GeometryFactory().CreatePoint(probe);
            foreach (Polygon p in rings)
            {
                if (p == null || !p.Covers(pt)) continue;
                if (p.Area < bestA)
                {
                    bestA = p.Area;
                    best = p;
                }
            }
            return best;
        }

        private static Coordinate CentroidOf(Geometry g)
        {
            if (g == null || g.IsEmpty) return new Coordinate(0, 0);
            NetTopologySuite.Geometries.Point c = g.Centroid;
            return c == null ? new Coordinate(0, 0) : c.Coordinate;
        }

        private static Polygon RingToPolygon(GeometryFactory gf, Point2d[] ring)
        {
            if (gf == null || ring == null || ring.Length < 3) return null;
            var coords = new List<Coordinate>(ring.Length + 1);
            for (int i = 0; i < ring.Length; i++)
            {
                var c = new Coordinate(ring[i].X, ring[i].Y);
                if (coords.Count > 0 && coords[coords.Count - 1].Distance(c) < 0.05) continue;
                coords.Add(c);
            }
            if (coords.Count < 3) return null;
            if (coords[0].Distance(coords[coords.Count - 1]) > 0.05)
                coords.Add(new Coordinate(coords[0].X, coords[0].Y));
            else
                coords[coords.Count - 1] = new Coordinate(coords[0].X, coords[0].Y);
            try
            {
                var poly = gf.CreatePolygon(coords.ToArray());
                if (poly.IsValid && poly.Area > 1.0) return poly;
                Geometry fixedG = poly.Buffer(0);
                if (fixedG is Polygon fp && fp.Area > 1.0) return fp;
                if (fixedG != null)
                {
                    Polygon best = null;
                    for (int i = 0; i < fixedG.NumGeometries; i++)
                    {
                        if (fixedG.GetGeometryN(i) is Polygon p && (best == null || p.Area > best.Area))
                            best = p;
                    }
                    return best;
                }
            }
            catch { }
            return null;
        }

        private static bool TryReadClosedRing(Entity ent, out Point2d[] ring)
        {
            ring = null;
            try
            {
                if (!(ent is Polyline pl) || pl.NumberOfVertices < 3) return false;
                int n = pl.NumberOfVertices;
                bool geoClosed = pl.Closed;
                if (!geoClosed && n >= 4)
                    geoClosed = pl.GetPoint2dAt(0).GetDistanceTo(pl.GetPoint2dAt(n - 1)) < 1.0;
                if (!geoClosed) return false;
                if (!pl.Closed && pl.GetPoint2dAt(0).GetDistanceTo(pl.GetPoint2dAt(n - 1)) < 1.0)
                    n--;
                if (n < 3) return false;
                ring = new Point2d[n];
                for (int i = 0; i < n; i++)
                    ring[i] = pl.GetPoint2dAt(i);
                return true;
            }
            catch { return false; }
        }
    }
}
