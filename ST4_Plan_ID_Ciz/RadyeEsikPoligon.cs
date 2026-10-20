using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Triangulate;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// Sabit donatıyı aşan As bölgesi. RAD eleman kenarında doğrusal enterpolasyon;
    /// dörtgenlerde merkez (köşe ortalaması) ile bölünür. Sonuç metre, GPR X/Y.
    /// </summary>
    public static class RadyeEsikPoligon
    {
        public static bool TryRadElemanlari(string radPath, IList<GprRadyeDugumAs> nodes, out List<int[]> elemanlar, out string error)
        {
            elemanlar = null;
            error = null;
            if (string.IsNullOrWhiteSpace(radPath) || !File.Exists(radPath))
            {
                error = "RAD yok.";
                return false;
            }
            if (nodes == null || nodes.Count == 0)
            {
                error = "Mesh yok.";
                return false;
            }

            string[] lines;
            try
            {
                WindowsAnsiEncodings.EnsureRegistered();
                lines = WindowsAnsiEncodings.ReadAllLines(radPath);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (lines.Length < 3 || !int.TryParse(Parca(lines[1], 0), NumberStyles.Integer, CultureInfo.InvariantCulture, out int plates)
                || !int.TryParse(Parca(lines[1], 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                || !int.TryParse(Parca(lines[1], 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int elements))
            {
                error = "RAD basligi okunamadi.";
                return false;
            }

            int offset = 7 + 2 * plates;
            if (offset < 0 || offset + elements + count > lines.Length)
            {
                error = "RAD eleman tablosu kisa.";
                return false;
            }

            var byNo = new GprRadyeDugumAs[nodes.Count + 2];
            int maxNo = 0;
            foreach (GprRadyeDugumAs node in nodes)
            {
                if (node.No > maxNo) maxNo = node.No;
            }
            if (maxNo != count || nodes.Count != count)
            {
                error = "RAD dugum sayisi GPR ile ayni degil.";
                return false;
            }
            byNo = new GprRadyeDugumAs[maxNo + 1];
            foreach (GprRadyeDugumAs node in nodes)
            {
                if (node.No >= 1 && node.No <= maxNo) byNo[node.No] = node;
            }

            int orta = count / 2;
            if (!KoordinatUyar(lines[offset + elements], byNo[1])
                || !KoordinatUyar(lines[offset + elements + orta - 1], byNo[orta])
                || !KoordinatUyar(lines[offset + elements + count - 1], byNo[count]))
            {
                error = "RAD koordinati GPR dugumu ile uyusmadi.";
                return false;
            }

            var liste = new List<int[]>(elements);
            for (int i = 0; i < elements; i++)
            {
                string[] hucre = (lines[offset + i] ?? string.Empty).Split(',');
                if (hucre.Length < 5) continue;
                if (!TryInt(hucre[1], out int n1) || !TryInt(hucre[2], out int n2) || !TryInt(hucre[3], out int n3))
                    continue;
                TryInt(hucre.Length > 4 ? hucre[4] : "0", out int n4);
                if (n1 < 1 || n2 < 1 || n3 < 1 || n1 > maxNo || n2 > maxNo || n3 > maxNo) continue;
                if (n4 == 0)
                {
                    if (n1 == n2 || n2 == n3 || n3 == n1) continue;
                    liste.Add(new[] { n1, n2, n3 });
                }
                else
                {
                    if (n4 < 1 || n4 > maxNo) continue;
                    if (n1 == n2 || n2 == n3 || n3 == n4 || n4 == n1) continue;
                    liste.Add(new[] { n1, n2, n3, n4 });
                }
            }
            if (liste.Count == 0)
            {
                error = "RAD elemani okunamadi.";
                return false;
            }
            elemanlar = liste;
            return true;
        }

        public static List<Polygon> Hesapla(
            IList<GprRadyeDugumAs> nodes,
            IList<int[]> elemanlar,
            Func<GprRadyeDugumAs, double> deger,
            double sinir,
            double kenarPay = 0,
            double disOffsetM = 0,
            bool sinirKutusu = false)
        {
            var sonuc = new List<Polygon>();
            if (nodes == null || nodes.Count == 0 || deger == null) return sonuc;

            int maxNo = 0;
            foreach (GprRadyeDugumAs node in nodes)
                if (node.No > maxNo) maxNo = node.No;
            var byNo = new GprRadyeDugumAs[maxNo + 1];
            var degerler = new double[maxNo + 1];
            foreach (GprRadyeDugumAs node in nodes)
            {
                if (node.No < 1 || node.No > maxNo) continue;
                byNo[node.No] = node;
                degerler[node.No] = deger(node);
            }

            var gf = new GeometryFactory(new PrecisionModel(1000.0));
            IList<int[]> ag = elemanlar;
            if (ag == null || ag.Count == 0)
                ag = DelaunayElemanlari(nodes, gf);

            var parcalar = new List<Geometry>();
            foreach (int[] eleman in ag)
            {
                if (eleman == null || eleman.Length < 3) continue;
                if (eleman.Length == 3)
                {
                    EkleParca(parcalar, gf, byNo, degerler, sinir, kenarPay, eleman[0], eleman[1], eleman[2]);
                    continue;
                }
                int a = eleman[0], b = eleman[1], c = eleman[2], d = eleman[3];
                if (byNo[a] == null || byNo[b] == null || byNo[c] == null || byNo[d] == null) continue;
                double ox = (byNo[a].X + byNo[b].X + byNo[c].X + byNo[d].X) / 4.0;
                double oy = (byNo[a].Y + byNo[b].Y + byNo[c].Y + byNo[d].Y) / 4.0;
                double ov = (degerler[a] + degerler[b] + degerler[c] + degerler[d]) / 4.0;
                var merkez = new Coordinate(ox, oy);
                EkleParcaKoordinat(parcalar, gf, Koord(byNo[a]), degerler[a], Koord(byNo[b]), degerler[b], merkez, ov, sinir, kenarPay);
                EkleParcaKoordinat(parcalar, gf, Koord(byNo[b]), degerler[b], Koord(byNo[c]), degerler[c], merkez, ov, sinir, kenarPay);
                EkleParcaKoordinat(parcalar, gf, Koord(byNo[c]), degerler[c], Koord(byNo[d]), degerler[d], merkez, ov, sinir, kenarPay);
                EkleParcaKoordinat(parcalar, gf, Koord(byNo[d]), degerler[d], Koord(byNo[a]), degerler[a], merkez, ov, sinir, kenarPay);
            }
            if (parcalar.Count == 0) return sonuc;

            Geometry birlesik;
            try
            {
                birlesik = new UnaryUnionOp(parcalar).Union();
            }
            catch (Exception)
            {
                birlesik = parcalar[0];
                for (int i = 1; i < parcalar.Count; i++)
                {
                    try { birlesik = birlesik.Union(parcalar[i]); }
                    catch (Exception) { }
                }
            }
            try
            {
                Geometry kapali = birlesik.Buffer(0.005).Buffer(-0.005);
                if (kapali != null && !kapali.IsEmpty) birlesik = kapali;
            }
            catch (Exception)
            {
            }
            if (disOffsetM > 0.001)
            {
                try
                {
                    Geometry genis = birlesik.Buffer(disOffsetM);
                    if (genis != null && !genis.IsEmpty) birlesik = genis;
                }
                catch (Exception)
                {
                }
            }
            Topla(birlesik, sonuc);
            if (sinirKutusu)
            {
                for (int i = 0; i < sonuc.Count; i++)
                    sonuc[i] = SinirKutusu(sonuc[i]);
            }
            sonuc.Sort(delegate (Polygon p, Polygon q) { return q.Area.CompareTo(p.Area); });
            return sonuc;
        }

        private static Polygon SinirKutusu(Polygon poly)
        {
            Envelope e = poly.EnvelopeInternal;
            return poly.Factory.CreatePolygon(new[]
            {
                new Coordinate(e.MinX, e.MinY),
                new Coordinate(e.MaxX, e.MinY),
                new Coordinate(e.MaxX, e.MaxY),
                new Coordinate(e.MinX, e.MaxY),
                new Coordinate(e.MinX, e.MinY)
            });
        }

        private static void EkleParca(List<Geometry> parcalar, GeometryFactory gf, GprRadyeDugumAs[] byNo, double[] degerler, double sinir, double kenarPay, int a, int b, int c)
        {
            if (a < 1 || b < 1 || c < 1 || a >= byNo.Length || b >= byNo.Length || c >= byNo.Length) return;
            if (byNo[a] == null || byNo[b] == null || byNo[c] == null) return;
            EkleParcaKoordinat(parcalar, gf, Koord(byNo[a]), degerler[a], Koord(byNo[b]), degerler[b], Koord(byNo[c]), degerler[c], sinir, kenarPay);
        }

        private static void EkleParcaKoordinat(
            List<Geometry> parcalar, GeometryFactory gf,
            Coordinate a, double va, Coordinate b, double vb, Coordinate c, double vc, double sinir, double kenarPay)
        {
            var halka = new List<Coordinate>(5);
            Yuru(a, va, b, vb, sinir, kenarPay, halka);
            Yuru(b, vb, c, vc, sinir, kenarPay, halka);
            Yuru(c, vc, a, va, sinir, kenarPay, halka);
            if (halka.Count < 3) return;
            var temiz = new List<Coordinate>(halka.Count);
            foreach (Coordinate p in halka)
            {
                if (temiz.Count == 0 || temiz[temiz.Count - 1].Distance(p) > 1e-4)
                    temiz.Add(p);
            }
            if (temiz.Count >= 2 && temiz[0].Distance(temiz[temiz.Count - 1]) <= 1e-4)
                temiz.RemoveAt(temiz.Count - 1);
            if (temiz.Count < 3) return;
            temiz.Add(new Coordinate(temiz[0].X, temiz[0].Y));
            Polygon poly;
            try
            {
                poly = gf.CreatePolygon(temiz.ToArray());
            }
            catch (Exception)
            {
                return;
            }
            if (poly.IsEmpty || poly.Area < 1e-4) return;
            parcalar.Add(poly);
        }

        private static void Yuru(Coordinate p, double vp, Coordinate q, double vq, double sinir, double kenarPay, List<Coordinate> halka)
        {
            bool icerde = vp > sinir;
            bool karsi = vq > sinir;
            if (icerde) halka.Add(new Coordinate(p.X, p.Y));
            if (icerde == karsi) return;
            double payda = vp - vq;
            double u = Math.Abs(payda) < 1e-12 ? 0.5 : (sinir - vq) / payda;
            if (kenarPay > 0)
            {
                if (icerde && !karsi) u *= (1.0 - kenarPay);
                else u += kenarPay * (1.0 - u);
            }
            if (u < 0) u = 0;
            if (u > 1) u = 1;
            halka.Add(new Coordinate(q.X + u * (p.X - q.X), q.Y + u * (p.Y - q.Y)));
        }

        private static void Topla(Geometry geom, List<Polygon> hedef)
        {
            if (geom == null || geom.IsEmpty) return;
            if (geom is Polygon poly)
            {
                if (poly.Area >= 0.02) hedef.Add(poly);
                return;
            }
            for (int i = 0; i < geom.NumGeometries; i++)
                Topla(geom.GetGeometryN(i), hedef);
        }

        private static List<int[]> DelaunayElemanlari(IList<GprRadyeDugumAs> nodes, GeometryFactory gf)
        {
            var liste = new List<int[]>();
            var sites = new List<Coordinate>(nodes.Count);
            var byKey = new Dictionary<string, int>();
            foreach (GprRadyeDugumAs node in nodes)
            {
                string key = Anahtar(node.X, node.Y);
                if (byKey.ContainsKey(key)) continue;
                byKey[key] = node.No;
                sites.Add(new Coordinate(node.X, node.Y));
            }
            if (sites.Count < 3) return liste;

            double[] yakin = new double[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                double en = double.MaxValue;
                for (int j = 0; j < nodes.Count; j++)
                {
                    if (i == j) continue;
                    double d = Math.Abs(nodes[i].X - nodes[j].X) + Math.Abs(nodes[i].Y - nodes[j].Y);
                    if (d > 1e-6 && d < en) en = d;
                }
                yakin[i] = en;
            }
            Array.Sort(yakin);
            double sinirKenar = yakin[yakin.Length / 2] * 2.2;
            if (sinirKenar < 0.2) sinirKenar = 0.2;

            var builder = new DelaunayTriangulationBuilder();
            builder.SetSites(sites);
            Geometry tris = builder.GetTriangles(gf);
            for (int i = 0; i < tris.NumGeometries; i++)
            {
                var poly = tris.GetGeometryN(i) as Polygon;
                if (poly == null) continue;
                Coordinate[] ring = poly.ExteriorRing.Coordinates;
                if (ring.Length < 4) continue;
                if (ring[0].Distance(ring[1]) > sinirKenar || ring[1].Distance(ring[2]) > sinirKenar || ring[2].Distance(ring[0]) > sinirKenar)
                    continue;
                if (!byKey.TryGetValue(Anahtar(ring[0].X, ring[0].Y), out int a)) continue;
                if (!byKey.TryGetValue(Anahtar(ring[1].X, ring[1].Y), out int b)) continue;
                if (!byKey.TryGetValue(Anahtar(ring[2].X, ring[2].Y), out int c)) continue;
                liste.Add(new[] { a, b, c });
            }
            return liste;
        }

        private static Coordinate Koord(GprRadyeDugumAs node)
        {
            return new Coordinate(node.X, node.Y);
        }

        private static string Anahtar(double x, double y)
        {
            return Math.Round(x, 3).ToString("0.000", CultureInfo.InvariantCulture)
                + "|" + Math.Round(y, 3).ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static bool KoordinatUyar(string satir, GprRadyeDugumAs node)
        {
            if (node == null || string.IsNullOrEmpty(satir)) return false;
            string[] hucre = satir.Split(',');
            if (hucre.Length < 3) return false;
            if (!double.TryParse(hucre[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) return false;
            if (!double.TryParse(hucre[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)) return false;
            return Math.Abs(x - node.X) < 0.02 && Math.Abs(y - node.Y) < 0.02;
        }

        private static string Parca(string satir, int index)
        {
            if (string.IsNullOrEmpty(satir)) return string.Empty;
            string[] hucre = satir.Split(',');
            return index < hucre.Length ? hucre[index].Trim() : string.Empty;
        }

        private static bool TryInt(string text, out int value)
        {
            return int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
