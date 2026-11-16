using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// KOLONOLCUDUZELT: Plan ID kolon aplikasyonunda (KOLON50/100ST4) akslar kaydırıldıktan sonra
    /// kolon yüz ölçülerini yeni aks yerine göre yeniden kurar. Kolonlar yerinde kalır.
    /// Yüz başına: 1. sıra = köşe izdüşümleri + aks istasyonu zinciri, 2. sıra = yüz toplamı
    /// (Plan ID DrawRectColumnDimensionWithAxisSplit / DrawPolygonFace2Tier ile aynı kural).
    /// Aks kolon dışına çıkarsa kolon yüzünden aksa kaçıklık ölçüsü zincire eklenir.
    /// </summary>
    internal static class KolonOlcuDuzeltRunner
    {
        private const double Tol = 0.5;          // nokta–çizgi (çizim birimi, cm)
        private const double MergeTol = 1.0;     // Plan ID KolonApplicationOlcuMinCm
        private const double AngTol = 0.01;      // ~0,57°

        private sealed class Kolon
        {
            public List<Point2d> V;
            public Point2d Merkez;
            public double Boyut;
            public List<Vector2d> Yonler = new List<Vector2d>();
        }

        private sealed class Aks
        {
            public Point2d A, B;
            public Vector2d Yon;
        }

        private sealed class Olcu
        {
            public ObjectId Id;
            public Point3d P1, P2, Pd;
            public Vector2d U;
        }

        private sealed class Yuz
        {
            public Kolon K;
            public Vector2d U, N;
            public double C;               // n · x = C yüz çizgisi
            public double TMin, TMax;
            public List<(Olcu O, double T1, double T2, double Ofs)> Olculer = new List<(Olcu, double, double, double)>();
        }

        public static void Run()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            var opt = new PromptSelectionOptions
            {
                MessageForAdding = "\nKolon aplikasyon planını seçin (kolon, aks ve ölçüler birlikte): "
            };
            PromptSelectionResult sel = ed.GetSelection(opt);
            if (sel.Status != PromptStatus.OK) return;

            string rapor;
            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                rapor = Uygula(tr, doc.Database, sel.Value.GetObjectIds());
                tr.Commit();
            }
            ed.WriteMessage("\nKOLONOLCUDUZELT: " + rapor);
        }

        internal static string Uygula(Transaction tr, Database db, IEnumerable<ObjectId> ids)
        {
            var kolonlar = new List<Kolon>();
            var akslar = new List<Aks>();
            var olculer = new List<Olcu>();
            ObjectId owner = ObjectId.Null;

            foreach (ObjectId id in ids)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                string ly = (ent.Layer ?? "").ToUpperInvariant();
                if (ent is Dimension)
                {
                    if (ly.Contains("AKS")) continue;
                    if (ent is AlignedDimension ad)
                        Ekle(olculer, id, ad.XLine1Point, ad.XLine2Point, ad.DimLinePoint, null);
                    else if (ent is RotatedDimension rd)
                        Ekle(olculer, id, rd.XLine1Point, rd.XLine2Point, rd.DimLinePoint, rd.Rotation);
                    if (owner.IsNull) owner = ent.OwnerId;
                    continue;
                }
                if (AksKatmani(ly))
                {
                    if (ent is Line ln) AksEkle(akslar, ln.StartPoint, ln.EndPoint);
                    else if (ent is Polyline apl && apl.NumberOfVertices == 2) AksEkle(akslar, apl.GetPoint3dAt(0), apl.GetPoint3dAt(1));
                    continue;
                }
                if (KolonKatmani(ly) && ent is Polyline pl)
                {
                    Kolon k = KolonOku(pl);
                    if (k != null) kolonlar.Add(k);
                }
            }

            if (kolonlar.Count == 0) return "seçimde kolon (KOLON katmanında kapalı çoklu çizgi) yok.";
            if (akslar.Count == 0) return "seçimde aks çizgisi yok.";
            if (olculer.Count == 0) return "seçimde kolon ölçüsü yok.";

            // Ölçüleri kolon yüzlerine ata.
            var yuzler = new Dictionary<string, Yuz>();
            foreach (Olcu o in olculer)
            {
                Yuz enIyi = null; double enIyiPuan = double.MaxValue; double t1b = 0, t2b = 0, ofsb = 0;
                foreach (Kolon k in kolonlar)
                {
                    double maxOfs = 200.0 + 2.0 * k.Boyut;
                    if (o.Pd.X < k.Merkez.X - k.Boyut - maxOfs || o.Pd.X > k.Merkez.X + k.Boyut + maxOfs
                        || o.Pd.Y < k.Merkez.Y - k.Boyut - maxOfs || o.Pd.Y > k.Merkez.Y + k.Boyut + maxOfs) continue;
                    foreach (Vector2d d in k.Yonler)
                    {
                        if (Math.Abs(d.X * o.U.Y - d.Y * o.U.X) > AngTol) continue;
                        Vector2d u = d;
                        foreach (int s in new[] { 1, -1 })
                        {
                            var n = new Vector2d(-u.Y * s, u.X * s);
                            double c = k.V.Max(v => n.X * v.X + n.Y * v.Y);
                            double s1 = Dot(n, o.P1) - c, s2 = Dot(n, o.P2) - c, sd = Dot(n, o.Pd) - c;
                            if (Math.Abs(s1) > Tol || Math.Abs(s2) > Tol) continue;
                            if (sd <= Tol || sd > maxOfs) continue;
                            double tMin = k.V.Min(v => u.X * v.X + u.Y * v.Y), tMax = k.V.Max(v => u.X * v.X + u.Y * v.Y);
                            double t1 = Dot(u, o.P1), t2 = Dot(u, o.P2);
                            bool ic1 = t1 >= tMin - Tol && t1 <= tMax + Tol, ic2 = t2 >= tMin - Tol && t2 <= tMax + Tol;
                            if (!ic1 && !ic2) continue;
                            double R = Math.Max(300.0, 3.0 * k.Boyut);
                            if (t1 < tMin - R || t1 > tMax + R || t2 < tMin - R || t2 > tMax + R) continue;
                            double puan = (ic1 && ic2 ? 0 : 1e6) + sd;
                            if (puan >= enIyiPuan) continue;
                            enIyiPuan = puan;
                            string key = kolonlar.IndexOf(k) + "|" + Math.Round(n.X, 3) + "|" + Math.Round(n.Y, 3);
                            if (!yuzler.TryGetValue(key, out Yuz y))
                                y = new Yuz { K = k, U = u, N = n, C = c, TMin = tMin, TMax = tMax };
                            enIyi = y; t1b = t1; t2b = t2; ofsb = sd;
                            yuzler[key] = y;
                        }
                    }
                }
                if (enIyi != null) enIyi.Olculer.Add((o, Math.Min(t1b, t2b), Math.Max(t1b, t2b), ofsb));
            }

            var gecerli = yuzler.Values.Where(y => y.Olculer.Count > 0).ToList();
            if (gecerli.Count == 0) return "kolon yüzlerine bağlı ölçü bulunamadı.";

            // İki sıralı yüzlerden ortak sıra aralığı (yoksa ilk ofset).
            var araliklar = new List<double>();
            foreach (Yuz y in gecerli)
            {
                double a = y.Olculer.Min(x => x.Ofs), b = y.Olculer.Max(x => x.Ofs);
                if (b - a > Tol) araliklar.Add(b - a);
            }
            araliklar.Sort();
            double ortakAralik = araliklar.Count > 0 ? araliklar[araliklar.Count / 2] : 0;

            List<Point2d> dugumler = Dugumler(akslar);
            var btr = (BlockTableRecord)tr.GetObject(owner.IsNull ? SymbolUtilityServices.GetBlockModelSpaceId(db) : owner, OpenMode.ForWrite);
            int duzeltilen = 0, ayni = 0, aksYok = 0, disaCikan = 0;
            foreach (Yuz y in gecerli)
            {
                double ilk = y.Olculer.Min(x => x.Ofs);
                double son = y.Olculer.Max(x => x.Ofs);
                double aralik = son - ilk > Tol ? son - ilk : (ortakAralik > 0 ? ortakAralik : ilk);

                // Köşe izdüşümleri.
                var kose = y.K.V.Select(v => y.U.X * v.X + y.U.Y * v.Y).ToList();
                // Eski aks istasyonu: köşeye denk gelmeyen ölçü ucu.
                var eskiAks = new List<double>();
                foreach (var x in y.Olculer)
                    foreach (double t in new[] { x.T1, x.T2 })
                        if (!kose.Any(q => Math.Abs(q - t) <= MergeTol) && !eskiAks.Any(q => Math.Abs(q - t) <= MergeTol)) eskiAks.Add(t);

                double? aks = AksBul(dugumler, y, eskiAks);
                if (aks == null) { aksYok++; continue; }
                double a0 = aks.Value;
                bool disarida = a0 < y.TMin - MergeTol || a0 > y.TMax + MergeTol;
                if (disarida)
                {
                    // Aks bitişik kolonun/perdenin içine düştüyse kaçıklık ölçüsü o elemanın yüzüne biner
                    // (bir sonraki çalıştırmada ona atanır): yalnız yüz ölçüsü bırakılır.
                    double uc = a0 < y.TMin ? y.TMin : y.TMax;
                    Point3d pm = Nokta(y, (a0 + uc) * 0.5, -1.0, 0);
                    if (kolonlar.Any(k2 => k2 != y.K && IcindeMi(k2.V, new Point2d(pm.X, pm.Y))))
                    {
                        disarida = false;
                        a0 = uc;
                    }
                }

                var ist = new List<double>(kose) { a0 };
                ist.Sort();
                var uniq = new List<double>();
                foreach (double v in ist)
                    if (uniq.Count == 0 || v - uniq[uniq.Count - 1] > MergeTol - 1e-9) uniq.Add(v);
                var yeni = new List<(double T1, double T2, double Ofs)>();
                var zincir = new List<(double, double)>();
                for (int i = 0; i + 1 < uniq.Count; i++) zincir.Add((uniq[i], uniq[i + 1]));
                int yuzParca = zincir.Count(z => z.Item1 >= y.TMin - MergeTol && z.Item2 <= y.TMax + MergeTol);
                if (zincir.Count <= 1)
                    yeni.Add((y.TMin, y.TMax, ilk));
                else if (disarida && yuzParca <= 1)
                    foreach (var z in zincir) yeni.Add((z.Item1, z.Item2, ilk));      // kaçıklık + kolon genişliği tek sırada
                else
                {
                    foreach (var z in zincir) yeni.Add((z.Item1, z.Item2, ilk));
                    yeni.Add((y.TMin, y.TMax, ilk + aralik));
                }

                if (AyniMi(y, yeni)) { ayni++; continue; }
                if (disarida) disaCikan++;

                // Şablon: en dıştaki (toplam) ölçü.
                Olcu sablonO = y.Olculer.OrderByDescending(x => x.Ofs).ThenByDescending(x => x.T2 - x.T1).First().O;
                var sablon = (Dimension)tr.GetObject(sablonO.Id, OpenMode.ForRead);
                double z0 = sablonO.P1.Z;
                foreach (var r in yeni)
                {
                    Point3d p1 = Nokta(y, r.T1, 0, z0), p2 = Nokta(y, r.T2, 0, z0), pd = Nokta(y, (r.T1 + r.T2) * 0.5, r.Ofs, z0);
                    AlignedDimension dim;
                    if (sablon is AlignedDimension sa)
                    {
                        dim = (AlignedDimension)sa.Clone();
                        dim.XLine1Point = p1; dim.XLine2Point = p2; dim.DimLinePoint = pd;
                    }
                    else
                    {
                        dim = new AlignedDimension(p1, p2, pd, "", sablon.DimensionStyle);
                        dim.SetPropertiesFrom(sablon);
                    }
                    dim.DimensionText = "";
                    btr.AppendEntity(dim);
                    tr.AddNewlyCreatedDBObject(dim, true);
                }
                foreach (var x in y.Olculer)
                    tr.GetObject(x.O.Id, OpenMode.ForWrite).Erase();
                duzeltilen++;
            }

            string msg = duzeltilen + " kolon yüzünün ölçüsü düzeltildi, " + ayni + " yüz zaten doğru.";
            if (disaCikan > 0) msg += " " + disaCikan + " yüzde aks kolon dışında (kaçıklık ölçüsü eklendi).";
            if (aksYok > 0) msg += " " + aksYok + " yüzde yakın aks bulunamadı (dokunulmadı).";
            return msg;
        }

        /// <summary>
        /// Plan ID kaçıklığı kolonun aks düğümüne (iki aksın kesişimi) göre verir: yüz istasyonu = düğümün
        /// yüz doğrultusundaki izdüşümü. Eğik akslarda da doğru. Kolona yakın düğümlerden eski aks istasyonuna
        /// en yakını seçilir; eski bölme yoksa (aks yüz ucunda/dışında idi) uca en yakını.
        /// Ortaya en yakını seçmek kolonu kesen başka aksı (ör. uzun perdede) yanlışlıkla alırdı.
        /// </summary>
        private static double? AksBul(List<Point2d> dugumler, Yuz y, List<double> eskiAks)
        {
            if (eskiAks.Count == 0) eskiAks = new List<double> { y.TMin, y.TMax };
            double limit = Math.Max(150.0, y.K.Boyut);
            double minX = y.K.V.Min(p => p.X), maxX = y.K.V.Max(p => p.X), minY = y.K.V.Min(p => p.Y), maxY = y.K.V.Max(p => p.Y);
            double? best = null; double bestD = double.MaxValue;
            foreach (Point2d q in dugumler)
            {
                double dx = Math.Max(0, Math.Max(minX - q.X, q.X - maxX)), dy = Math.Max(0, Math.Max(minY - q.Y, q.Y - maxY));
                if (dx > limit || dy > limit) continue;
                double t = Dot(y.U, q);
                double d = eskiAks.Min(e => Math.Abs(e - t));
                if (d < bestD - 1e-9) { bestD = d; best = t; }
            }
            return best;
        }

        /// <summary>Kesişen aks çiftlerinin düğümleri (doğrultular ≥ ~3° farklı, kesişim iki parçanın da yakınında).</summary>
        private static List<Point2d> Dugumler(List<Aks> akslar)
        {
            var l = new List<Point2d>();
            const double pay = 100.0;
            for (int i = 0; i < akslar.Count; i++)
                for (int j = i + 1; j < akslar.Count; j++)
                {
                    Aks a = akslar[i], b = akslar[j];
                    double cr = a.Yon.X * b.Yon.Y - a.Yon.Y * b.Yon.X;
                    if (Math.Abs(cr) < 0.05) continue;
                    Vector2d w = b.A - a.A;
                    double s = (w.X * b.Yon.Y - w.Y * b.Yon.X) / cr;
                    double t = (w.X * a.Yon.Y - w.Y * a.Yon.X) / cr;
                    if (s < -pay || s > a.A.GetDistanceTo(a.B) + pay || t < -pay || t > b.A.GetDistanceTo(b.B) + pay) continue;
                    l.Add(a.A + a.Yon * s);
                }
            return l;
        }

        private static bool IcindeMi(List<Point2d> v, Point2d p)
        {
            bool ic = false;
            for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
                if ((v[i].Y > p.Y) != (v[j].Y > p.Y) && p.X < (v[j].X - v[i].X) * (p.Y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X)
                    ic = !ic;
            return ic;
        }

        private static bool AyniMi(Yuz y, List<(double T1, double T2, double Ofs)> yeni)
        {
            if (y.Olculer.Count != yeni.Count) return false;
            var kalan = y.Olculer.ToList();
            foreach (var r in yeni)
            {
                int i = kalan.FindIndex(x => Math.Abs(x.T1 - r.T1) <= 0.05 && Math.Abs(x.T2 - r.T2) <= 0.05 && Math.Abs(x.Ofs - r.Ofs) <= Tol);
                if (i < 0) return false;
                kalan.RemoveAt(i);
            }
            return true;
        }

        private static Point3d Nokta(Yuz y, double t, double ofs, double z)
        {
            double s = y.C + ofs;
            return new Point3d(y.U.X * t + y.N.X * s, y.U.Y * t + y.N.Y * s, z);
        }

        private static void Ekle(List<Olcu> l, ObjectId id, Point3d p1, Point3d p2, Point3d pd, double? rot)
        {
            Vector2d u;
            if (rot.HasValue) u = new Vector2d(Math.Cos(rot.Value), Math.Sin(rot.Value));
            else
            {
                var d = new Vector2d(p2.X - p1.X, p2.Y - p1.Y);
                if (d.Length < 1e-6) return;
                u = d.GetNormal();
            }
            l.Add(new Olcu { Id = id, P1 = p1, P2 = p2, Pd = pd, U = u });
        }

        private static void AksEkle(List<Aks> l, Point3d a, Point3d b)
        {
            var d = new Vector2d(b.X - a.X, b.Y - a.Y);
            if (d.Length < 1.0) return;
            l.Add(new Aks { A = new Point2d(a.X, a.Y), B = new Point2d(b.X, b.Y), Yon = d.GetNormal() });
        }

        private static Kolon KolonOku(Polyline pl)
        {
            int n = pl.NumberOfVertices;
            if (n < 3) return null;
            var v = new List<Point2d>();
            for (int i = 0; i < n; i++)
            {
                if (Math.Abs(pl.GetBulgeAt(i)) > 1e-9) return null; // yay kenarlı (dairesel) kolon kapsam dışı
                Point2d p = pl.GetPoint2dAt(i);
                if (v.Count == 0 || v[v.Count - 1].GetDistanceTo(p) > 1e-6) v.Add(p);
            }
            if (v.Count > 1 && v[0].GetDistanceTo(v[v.Count - 1]) <= 1e-6) v.RemoveAt(v.Count - 1);
            else if (!pl.Closed) return null;
            if (v.Count < 3) return null;
            var k = new Kolon { V = v };
            double minX = v.Min(p => p.X), maxX = v.Max(p => p.X), minY = v.Min(p => p.Y), maxY = v.Max(p => p.Y);
            k.Merkez = new Point2d((minX + maxX) * 0.5, (minY + maxY) * 0.5);
            k.Boyut = Math.Max(maxX - minX, maxY - minY);
            if (k.Boyut < 1.0 || k.Boyut > 5000.0) return null;
            for (int i = 0; i < v.Count; i++)
            {
                var e = v[(i + 1) % v.Count] - v[i];
                if (e.Length < 1e-6) continue;
                Vector2d d = e.GetNormal();
                if (d.X < -1e-9 || (Math.Abs(d.X) <= 1e-9 && d.Y < 0)) d = d.Negate();
                if (!k.Yonler.Any(q => Math.Abs(q.X * d.Y - q.Y * d.X) <= AngTol)) k.Yonler.Add(d);
                var p = new Vector2d(-d.Y, d.X);
                if (p.X < -1e-9 || (Math.Abs(p.X) <= 1e-9 && p.Y < 0)) p = p.Negate();
                if (!k.Yonler.Any(q => Math.Abs(q.X * p.Y - q.Y * p.X) <= AngTol)) k.Yonler.Add(p);
            }
            return k;
        }

        private static bool AksKatmani(string ly)
            => ly.Contains("AKS") && !ly.Contains("OLCU") && !ly.Contains("ÖLÇÜ") && !ly.Contains("BALON")
               && !ly.Contains("YAZI") && !ly.Contains("ISIM") && !ly.Contains("İSİM");

        private static bool KolonKatmani(string ly)
            => (ly.Contains("KOLON") || ly.Contains("PERDE")) && !ly.Contains("YAZI") && !ly.Contains("ISIM")
               && !ly.Contains("İSİM") && !ly.Contains("TARAMA") && !ly.Contains("OLCU");

        private static double Dot(Vector2d a, Point2d p) => a.X * p.X + a.Y * p.Y;
        private static double Dot(Vector2d a, Point3d p) => a.X * p.X + a.Y * p.Y;
    }
}
