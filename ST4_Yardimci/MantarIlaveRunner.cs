using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Mantar döşeme kolon üstü ilave (üst) donatısı. Seçilen her kolona ortalanmış
    /// X (yatay) ve Y (düşey) bant: X çubuğu XBoy uzunluğunda YBoy genişliğine dağıtılır.
    /// KAT SINIRI / KALIP BOSLUK çubuk ucunu veya bant kenarını keserse uç paspayı kadar
    /// içeride biter ve gönye (kalınlık − 2×paspayı) alır. Uç koşulu farklı sıralar ayrı alt bant;
    /// her alt bandın temsilci çubuğu, etiketi ve donatı oku (RADYEILAVE ile aynı stil) çizilir.
    /// </summary>
    internal static class MantarIlaveRunner
    {
        private const string LayerKatSiniri = "KAT SINIRI (BEYKENT)";
        private const string LayerKalipBosluk = "KALIP BOSLUK (BEYKENT)";
        private const string LayerDosemeIsmi = "DOSEME ISMI (BEYKENT)";
        private const string LayerDonati = "DONATI (BEYKENT)";
        private const string LayerDonatiYazi = "DONATI YAZISI (BEYKENT)";
        private const string LayerDonatiOku = "DONATI OKU (BEYKENT)";
        private const string LayerDonatiHalkasi = "DONATI HALKASI (BEYKENT)";
        private const string DimStyleDonatiOku = "DONATI OKU (BEYKENT)";
        private const short DonatiOkuAci = 150;
        private const short DonatiHalkasiAci = 2;
        private const double DonatiOkuCircleDiaCm = 4.0;
        private const double DonatiOkuCircleWidthCm = 4.0;
        private const double DonatiOkuTextClearCm = 20.0;
        private const double LabelHeightCm = 10.0;
        private const double LabelGapCm = 3.0;
        private const double GonyeLabelHeightCm = 8.0;
        private const double GonyeLabelClearCm = 8.0;
        private const double BendRadiusCm = 3.0;
        /// <summary>Temsilci çubuk kolon yüzünden bu kadar dışarıda (kolon çizgisine binmesin).</summary>
        private const double RepClearCm = 15.0;
        private const double MinBarCm = 30.0;
        private const double EndTolCm = 1.0;
        /// <summary>Kolon yüzünden çubuk ucuna bundan kısa yan: etiket/oku sığmaz, uzun yana alınır.</summary>
        private const double SideMinCm = 60.0;
        private const double ThicknessSearchCm = 3000.0;
        private static readonly Regex KalinlikRx = new Regex(@"\bd\s*=\s*(\d+(?:[.,]\d+)?)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private struct Seg { public Point2d A, B; }

        private sealed class Row
        {
            public double F, EndM, EndP;
            public bool HookM, HookP;
        }

        private sealed class Ctx
        {
            public Transaction Tr;
            public Database Db;
            public BlockTableRecord Btr;
            public List<Seg> Segs;
            public string DonatiLyr, YaziLyr, OkuLyr, HalkaLyr;
            public ObjectId YaziStyle, OkuStyle;
            public double Paspayi;
        }

        public static void RunInteractive()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { MantarIlaveForm.Shutdown(); return; }
            Editor ed = doc.Editor;
            Database db = doc.Database;
            try
            {
                MantarIlaveSettings s = MantarIlaveForm.Current ?? MantarIlaveSettings.Load();
                string error = s.Validate();
                if (error != null) { ed.WriteMessage("\nMANTARILAVE: " + error); return; }

                var filter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Operator, "<OR"),
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                    new TypedValue((int)DxfCode.Start, "CIRCLE"),
                    new TypedValue((int)DxfCode.Operator, "OR>"),
                    new TypedValue((int)DxfCode.LayerName, "*KOLON*")
                });
                var opt = new PromptSelectionOptions { MessageForAdding = "\nMantar ilave atılacak kolonları seçin: " };
                PromptSelectionResult sel = ed.GetSelection(opt, filter);
                if (sel.Status != PromptStatus.OK || sel.Value == null || sel.Value.Count == 0)
                {
                    ed.WriteMessage("\nMANTARILAVE: kolon seçilmedi.");
                    return;
                }

                int bant = 0, kolon = 0;
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ctx = new Ctx
                    {
                        Tr = tr, Db = db,
                        Btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite),
                        Segs = new List<Seg>(),
                        Paspayi = Math.Max(0, s.Paspayi)
                    };
                    var kalinliklar = new List<(Point2d p, double d)>();
                    CollectBoundary(tr, ctx.Btr, ctx.Segs, kalinliklar);

                    BeykentKatmanYukle.Ensure(tr, db, LayerDonati, 4, LineWeight.LineWeight040);
                    BeykentKatmanYukle.Ensure(tr, db, LayerDonatiYazi, 3, LineWeight.LineWeight020);
                    BeykentKatmanYukle.Ensure(tr, db, LayerDonatiOku, DonatiOkuAci, LineWeight.LineWeight020);
                    BeykentKatmanYukle.Ensure(tr, db, LayerDonatiHalkasi, DonatiHalkasiAci, LineWeight.LineWeight030, true);
                    ctx.DonatiLyr = LayerDonati; ctx.YaziLyr = LayerDonatiYazi;
                    ctx.OkuLyr = LayerDonatiOku; ctx.HalkaLyr = LayerDonatiHalkasi;
                    ctx.YaziStyle = BaDimensionStyles.EnsureYazi(tr, db);
                    ctx.OkuStyle = BaDimensionStyles.Ensure(tr, db, DimStyleDonatiOku);

                    foreach (SelectedObject so in sel.Value)
                    {
                        if (so == null) continue;
                        var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                        if (!TryColumnBox(ent, out Point2d c, out double hx, out double hy)) continue;

                        double h = s.DosemeKalinligi;
                        string hKaynak = "form";
                        if (s.KalinlikCizimden && TryNearestThickness(kalinliklar, c, out double hd))
                        {
                            h = hd;
                            hKaynak = "çizim";
                        }
                        double gonye = h - 2 * ctx.Paspayi;
                        if (gonye < 5) gonye = 5;

                        // X temsilci çubuk kotları: Y donatı oku bunlara binmesin.
                        var xRep = new List<double>();
                        int nx = DrawDirection(ctx, c, hx, hy, false, s.XBoy, s.YBoy, (int)Math.Round(s.XCap), s.XAralik, gonye, null, xRep, out int gx);
                        int ny = DrawDirection(ctx, c, hy, hx, true, s.YBoy, s.XBoy, (int)Math.Round(s.YCap), s.YAralik, gonye, xRep, null, out int gy);
                        bant += nx + ny;
                        kolon++;
                        ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                            "\n  Kolon ({0:0},{1:0}): d={2:0.#} ({3}), X {4} bant / {5} gönye, Y {6} bant / {7} gönye.",
                            c.X, c.Y, h, hKaynak, nx, gx, ny, gy));
                    }
                    tr.Commit();
                }
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\nMANTARILAVE: {0} kolon, {1} bant çizildi.", kolon, bant));
            }
            catch (System.Exception ex) { ed.WriteMessage("\nMANTARILAVE hata: " + ex.Message); }
            finally { MantarIlaveForm.Current = null; MantarIlaveForm.Shutdown(); }
        }

        /// <summary>Bir yön: alt bant sayısını döndürür; gonyeCount = gönyeli uç sayısı.</summary>
        private static int DrawDirection(
            Ctx ctx, Point2d c, double hAlong, double hAcross, bool isY,
            double length, double width, int cap, double aralik, double gonye,
            List<double> avoidAlong, List<double> repOut, out int gonyeCount)
        {
            gonyeCount = 0;
            Vector2d u = isY ? Vector2d.YAxis : Vector2d.XAxis;
            Vector2d v = isY ? Vector2d.XAxis : Vector2d.YAxis;
            double pas = ctx.Paspayi;
            double halfL = 0.5 * length, halfW = 0.5 * width;

            // Bant genişliği: kolon merkezinden dik yönde sınır kesişi.
            double wP = ClipHalf(ctx.Segs, c, v, halfW, pas);
            double wM = ClipHalf(ctx.Segs, c, -v, halfW, pas);
            double total = wP + wM;
            int n = Math.Max(1, (int)Math.Floor(total / aralik + 1e-9) + 1);
            double f0 = -wM + 0.5 * (total - (n - 1) * aralik);

            var rows = new List<Row>();
            for (int k = 0; k < n; k++)
            {
                double f = f0 + k * aralik;
                Point2d o = c + v * f;
                double tP = Ray(ctx.Segs, o, u, halfL);
                double tM = Ray(ctx.Segs, o, -u, halfL);
                var r = new Row
                {
                    F = f,
                    HookP = !double.IsInfinity(tP),
                    HookM = !double.IsInfinity(tM)
                };
                r.EndP = r.HookP ? Math.Max(0, tP - pas) : halfL;
                r.EndM = r.HookM ? Math.Max(0, tM - pas) : halfL;
                rows.Add(r.EndP + r.EndM >= MinBarCm ? r : null);
            }

            // Ardışık, aynı uç koşullu sıralar → alt bant.
            var groups = new List<List<Row>>();
            List<Row> cur = null;
            foreach (Row r in rows)
            {
                if (r == null) { cur = null; continue; }
                Row last = cur != null ? cur[cur.Count - 1] : null;
                bool same = last != null && last.HookP == r.HookP && last.HookM == r.HookM
                    && Math.Abs(last.EndP - r.EndP) < EndTolCm && Math.Abs(last.EndM - r.EndM) < EndTolCm;
                if (!same) { cur = new List<Row>(); groups.Add(cur); }
                cur.Add(r);
            }

            foreach (List<Row> g in groups)
            {
                double endP = double.MaxValue, endM = double.MaxValue;
                foreach (Row r in g) { endP = Math.Min(endP, r.EndP); endM = Math.Min(endM, r.EndM); }
                bool hookP = g[0].HookP, hookM = g[0].HookM;
                Row rep = PickRep(g, hAcross);
                double fr = rep.F;

                // Tek ucu gönyeli çubuk: toplam boy 1200'ün bölenine tamamlanır, fark serbest uca eklenir
                // (serbest uç sınıra ulaşırsa orada paspayıyla kesilir). İki ucu gönyeli çubukta uzatacak yer yok.
                if (hookP != hookM)
                {
                    double boy0 = endP + endM + gonye;
                    double hedef = StokBoyuTamamla(boy0);
                    if (hedef > boy0 + 0.01)
                    {
                        double serbest = hookP ? endM : endP;
                        Vector2d yon = hookP ? -u : u;
                        double tFree = Ray(ctx.Segs, c + v * fr, yon, serbest + (hedef - boy0));
                        double yeni = double.IsInfinity(tFree) ? serbest + (hedef - boy0) : Math.Max(serbest, tFree - pas);
                        if (hookP) endM = yeni; else endP = yeni;
                    }
                }

                // Çubuk + gönye (üst: X gönyesi aşağı, Y gönyesi sağa — RADYEILAVE/OTODONATI ile aynı).
                Vector2d hookDir = isY ? v : -v;
                Point2d s = c + u * (-endM) + v * fr;
                Point2d e = c + u * endP + v * fr;
                var pts = new List<Point2d>();
                if (hookM) pts.Add(s + hookDir * gonye);
                pts.Add(s);
                pts.Add(e);
                if (hookP) pts.Add(e + hookDir * gonye);
                PlacePolylineWithBends(ctx, pts);
                if (hookM) { gonyeCount++; PlaceGonyeLabel(ctx, s, s + hookDir * gonye, u, isY, gonye); }
                if (hookP) { gonyeCount++; PlaceGonyeLabel(ctx, e, e + hookDir * gonye, -u, isY, gonye); }

                double boy = endP + endM + (hookP ? gonye : 0) + (hookM ? gonye : 0);
                string label = g.Count > 1
                    ? string.Format(CultureInfo.InvariantCulture, "{0}ø{1}/{2:0.#} L={3} (üst)", g.Count, cap, aralik, (int)Math.Round(boy))
                    : string.Format(CultureInfo.InvariantCulture, "1ø{0} L={1} (üst)", cap, (int)Math.Round(boy));

                // İki yan da uzunsa etiket bir yanda, donatı oku öbür yanda (kolon yüzü–uç ortası).
                // Bir yan kısaysa (kenar/köşe kolon) ikisi de uzun yanda: oku kolona yakın, etiket ötesinde.
                double sideM = endM - hAlong, sideP = endP - hAlong;
                double aLabel, aOku;
                if (sideM >= SideMinCm && sideP >= SideMinCm)
                {
                    bool labelOnMinus = sideM >= sideP;
                    aLabel = labelOnMinus ? -0.5 * (hAlong + endM) : 0.5 * (hAlong + endP);
                    aOku = labelOnMinus ? 0.5 * (hAlong + endP) : -0.5 * (hAlong + endM);
                }
                else
                {
                    double sg = sideP >= sideM ? 1.0 : -1.0;
                    double span = Math.Max(0, sg > 0 ? sideP : sideM);
                    aOku = sg * (hAlong + Math.Min(25.0, 0.25 * span));
                    aLabel = sg * (hAlong + 0.6 * span);
                }
                aOku = AvoidAlong(aOku, c, isY, avoidAlong, -endM + 10, endP - 10);
                DBText lbl = PlaceLabel(ctx, c + u * aLabel + v * fr, isY, label);
                if (g.Count > 1) PushLabelOffOku(lbl, c, isY, aOku);
                repOut?.Add(isY ? c.X + fr : c.Y + fr);

                if (g.Count > 1)
                {
                    double ff = g[0].F, fl = g[g.Count - 1].F;
                    PlaceOku(ctx, c, u, v, isY, aOku, ff, fl, fr);
                }
            }
            return groups.Count;
        }

        /// <summary>1200 cm stok çubuğun bölenlerine yakın kesim boyları (kullanıcı listesi).</summary>
        private static readonly double[] StokBoylari = { 150, 200, 240, 300, 350, 400, 450, 500, 600, 700, 800, 900, 1000, 1200 };

        private static double StokBoyuTamamla(double boy)
        {
            foreach (double b in StokBoylari)
                if (b >= boy - 0.5) return b;
            return boy;
        }

        private static Row PickRep(List<Row> g, double hAcross)
        {
            double target = hAcross + RepClearCm;
            Row best = null, bestOut = null;
            double bd = double.MaxValue, bdOut = double.MaxValue;
            foreach (Row r in g)
            {
                double d = Math.Abs(r.F - target);
                if (d < bd) { bd = d; best = r; }
                if (Math.Abs(r.F) > hAcross + 5 && d < bdOut) { bdOut = d; bestOut = r; }
            }
            return bestOut ?? best;
        }

        private static double ClipHalf(List<Seg> segs, Point2d o, Vector2d d, double half, double pas)
        {
            double t = Ray(segs, o, d, half);
            if (double.IsInfinity(t)) return half;
            return Math.Max(0, Math.Min(half, t - pas));
        }

        /// <summary>o + d·t (0 &lt; t ≤ maxT) ile ilk sınır kesişimi; yoksa +∞.</summary>
        private static double Ray(List<Seg> segs, Point2d o, Vector2d d, double maxT)
        {
            double best = double.PositiveInfinity;
            foreach (Seg sg in segs)
            {
                Vector2d r = sg.B - sg.A;
                double den = d.X * r.Y - d.Y * r.X;
                if (Math.Abs(den) < 1e-12) continue;
                Vector2d w = sg.A - o;
                double t = (w.X * r.Y - w.Y * r.X) / den;
                double q = (w.X * d.Y - w.Y * d.X) / den;
                if (q < -1e-9 || q > 1 + 1e-9) continue;
                if (t > 0.01 && t <= maxT && t < best) best = t;
            }
            return best;
        }

        private static bool TryColumnBox(Entity ent, out Point2d c, out double hx, out double hy)
        {
            c = Point2d.Origin; hx = hy = 0;
            if (ent == null) return false;
            if (ent is Polyline pl)
            {
                bool closed = pl.Closed || (pl.NumberOfVertices > 2 && pl.GetPoint2dAt(0).GetDistanceTo(pl.GetPoint2dAt(pl.NumberOfVertices - 1)) < 0.01);
                if (!closed || pl.NumberOfVertices < 3) return false;
            }
            else if (!(ent is Circle)) return false;
            Extents3d ex;
            try { ex = ent.GeometricExtents; } catch { return false; }
            hx = 0.5 * (ex.MaxPoint.X - ex.MinPoint.X);
            hy = 0.5 * (ex.MaxPoint.Y - ex.MinPoint.Y);
            if (hx < 5 || hy < 5 || hx > 500 || hy > 500) return false;
            c = new Point2d(0.5 * (ex.MinPoint.X + ex.MaxPoint.X), 0.5 * (ex.MinPoint.Y + ex.MaxPoint.Y));
            return true;
        }

        private static void CollectBoundary(Transaction tr, BlockTableRecord ms, List<Seg> segs, List<(Point2d p, double d)> kalinliklar)
        {
            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                string lyr = ent.Layer ?? string.Empty;
                if (string.Equals(lyr, LayerDosemeIsmi, StringComparison.OrdinalIgnoreCase))
                {
                    string txt = null; Point3d at = Point3d.Origin;
                    if (ent is DBText t) { txt = t.TextString; at = t.Position; }
                    else if (ent is MText m) { txt = m.Text; at = m.Location; }
                    Match mt = txt != null ? KalinlikRx.Match(txt) : Match.Empty;
                    if (mt.Success && double.TryParse(mt.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                        && d >= 8 && d <= 200)
                        kalinliklar.Add((new Point2d(at.X, at.Y), d));
                    continue;
                }
                if (!string.Equals(lyr, LayerKatSiniri, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(lyr, LayerKalipBosluk, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (ent is Line ln)
                    Add(segs, new Point2d(ln.StartPoint.X, ln.StartPoint.Y), new Point2d(ln.EndPoint.X, ln.EndPoint.Y));
                else if (ent is Polyline pl)
                {
                    int nv = pl.NumberOfVertices;
                    int ns = pl.Closed ? nv : nv - 1;
                    for (int i = 0; i < ns; i++)
                    {
                        if (Math.Abs(pl.GetBulgeAt(i)) < 1e-9)
                        {
                            Add(segs, pl.GetPoint2dAt(i), pl.GetPoint2dAt((i + 1) % nv));
                            continue;
                        }
                        Point2d prev = pl.GetPoint2dAt(i);
                        for (int k = 1; k <= 8; k++)
                        {
                            Point3d p = pl.GetPointAtParameter(i + k / 8.0);
                            var p2 = new Point2d(p.X, p.Y);
                            Add(segs, prev, p2);
                            prev = p2;
                        }
                    }
                }
                else if (ent is Circle ci)
                {
                    Point2d prev = new Point2d(ci.Center.X + ci.Radius, ci.Center.Y);
                    for (int k = 1; k <= 32; k++)
                    {
                        double a = 2 * Math.PI * k / 32;
                        var p2 = new Point2d(ci.Center.X + ci.Radius * Math.Cos(a), ci.Center.Y + ci.Radius * Math.Sin(a));
                        Add(segs, prev, p2);
                        prev = p2;
                    }
                }
            }
        }

        private static void Add(List<Seg> segs, Point2d a, Point2d b)
        {
            if (a.GetDistanceTo(b) > 1e-6) segs.Add(new Seg { A = a, B = b });
        }

        private static bool TryNearestThickness(List<(Point2d p, double d)> list, Point2d c, out double d)
        {
            d = 0;
            double best = ThicknessSearchCm;
            foreach (var k in list)
            {
                double dist = k.p.GetDistanceTo(c);
                if (dist < best) { best = dist; d = k.d; }
            }
            return d > 0;
        }

        /// <summary>X: çubuğun üstünde yatay; Y: çubuğun solunda 90° döndürülmüş.</summary>
        private static DBText PlaceLabel(Ctx ctx, Point2d at, bool isY, string text)
        {
            var p = new Point3d(at.X, at.Y, 0);
            var t = new DBText();
            t.SetDatabaseDefaults(ctx.Db);
            t.TextString = text;
            t.Height = LabelHeightCm;
            if (!ctx.YaziStyle.IsNull) t.TextStyleId = ctx.YaziStyle;
            t.Layer = ctx.YaziLyr;
            t.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            t.HorizontalMode = TextHorizontalMode.TextCenter;
            t.VerticalMode = TextVerticalMode.TextBottom;
            if (isY) t.Rotation = Math.PI / 2.0;
            t.AlignmentPoint = isY ? new Point3d(p.X - LabelGapCm, p.Y, 0) : new Point3d(p.X, p.Y + LabelGapCm, 0);
            t.AdjustAlignment(ctx.Db);
            ctx.Btr.AppendEntity(t);
            ctx.Tr.AddNewlyCreatedDBObject(t, true);
            return t;
        }

        /// <summary>Etiket donatı oku hattına biniyorsa çubuk boyunca oku hattının ötesine kaydırılır.</summary>
        private static void PushLabelOffOku(DBText t, Point2d c, bool isY, double aOku)
        {
            try
            {
                Extents3d ex = t.GeometricExtents;
                double lo = isY ? ex.MinPoint.Y : ex.MinPoint.X;
                double hi = isY ? ex.MaxPoint.Y : ex.MaxPoint.X;
                double oku = (isY ? c.Y : c.X) + aOku;
                const double clear = 8.0;
                if (oku < lo - clear || oku > hi + clear) return;
                double shift = 0.5 * (lo + hi) >= oku ? oku + clear - lo : oku - clear - hi;
                t.TransformBy(Matrix3d.Displacement(isY ? new Vector3d(0, shift, 0) : new Vector3d(shift, 0, 0)));
            }
            catch { }
        }

        /// <summary>Oku hattı dik yöndeki temsilci çubuğa (±12 cm) binmesin: dışa doğru 15 cm adımlarla kaydır.</summary>
        private static double AvoidAlong(double aOku, Point2d c, bool isY, List<double> avoid, double lo, double hi)
        {
            if (avoid == null || avoid.Count == 0) return aOku;
            double baseAbs = isY ? c.Y : c.X;
            bool Clear(double a) { foreach (double x in avoid) if (Math.Abs(baseAbs + a - x) < 12.0) return false; return true; }
            if (Clear(aOku)) return aOku;
            double sg = aOku >= 0 ? 1.0 : -1.0;
            for (int k = 1; k <= 4; k++)
                foreach (double dir in new[] { sg, -sg })
                {
                    double a = aOku + dir * 15.0 * k;
                    if (a >= lo && a <= hi && Math.Sign(a) == Math.Sign(aOku) && Clear(a)) return a;
                }
            return aOku;
        }

        /// <summary>Gönye boyu yazısı, gönyenin çubuk tarafında (inward = çubuğun içine doğru birim).</summary>
        private static void PlaceGonyeLabel(Ctx ctx, Point2d end, Point2d tip, Vector2d inward, bool isY, double gonye)
        {
            Point2d mid = new Point2d(0.5 * (end.X + tip.X), 0.5 * (end.Y + tip.Y)) + inward * GonyeLabelClearCm;
            var t = new DBText();
            t.SetDatabaseDefaults(ctx.Db);
            t.TextString = ((int)Math.Round(gonye)).ToString(CultureInfo.InvariantCulture);
            t.Height = GonyeLabelHeightCm;
            if (!ctx.YaziStyle.IsNull) t.TextStyleId = ctx.YaziStyle;
            t.Layer = ctx.YaziLyr;
            t.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            t.HorizontalMode = TextHorizontalMode.TextCenter;
            t.VerticalMode = TextVerticalMode.TextVerticalMid;
            if (!isY) t.Rotation = Math.PI / 2.0; // yatay çubuğun düşey gönyesi → düşey yazı
            t.AlignmentPoint = new Point3d(mid.X, mid.Y, 0);
            t.AdjustAlignment(ctx.Db);
            ctx.Btr.AppendEntity(t);
            ctx.Tr.AddNewlyCreatedDBObject(t, true);
        }

        /// <summary>Donatı oku: çubuklara dik ölçü (ff→fl), temsilci çubuk kesişimine halka.</summary>
        private static void PlaceOku(Ctx ctx, Point2d c, Vector2d u, Vector2d v, bool isY, double aOku, double ff, double fl, double fr)
        {
            Point2d p1 = c + u * aOku + v * ff;
            Point2d p2 = c + u * aOku + v * fl;
            var dim = new RotatedDimension();
            dim.SetDatabaseDefaults(ctx.Db);
            dim.Rotation = isY ? 0.0 : Math.PI / 2.0;
            dim.XLine1Point = new Point3d(p1.X, p1.Y, 0);
            dim.XLine2Point = new Point3d(p2.X, p2.Y, 0);
            dim.DimLinePoint = new Point3d(p1.X, p1.Y, 0);
            if (!ctx.OkuStyle.IsNull) dim.DimensionStyle = ctx.OkuStyle;
            dim.Layer = ctx.OkuLyr;
            BaDimensionStyles.Apply(ctx.Tr, ctx.Db, dim, ctx.OkuStyle, DimStyleDonatiOku);
            ctx.Btr.AppendEntity(dim);
            ctx.Tr.AddNewlyCreatedDBObject(dim, true);

            // Yazı: düşey okta halkanın 20 cm üstü, yatayda 20 cm solu; sığmazsa ters yön.
            try
            {
                double lo = Math.Min(ff, fl), hi = Math.Max(ff, fl);
                double edge = Math.Max(8.0, LabelHeightCm);
                double pref = isY ? fr - DonatiOkuTextClearCm : fr + DonatiOkuTextClearCm;
                double flip = isY ? fr + DonatiOkuTextClearCm : fr - DonatiOkuTextClearCm;
                double at = pref >= lo + edge && pref <= hi - edge ? pref
                    : flip >= lo + edge && flip <= hi - edge ? flip
                    : 0.5 * (lo + hi);
                Point2d tp = c + u * aOku + v * at;
                dim.UsingDefaultTextPosition = false;
                dim.TextPosition = new Point3d(tp.X, tp.Y, 0);
                try { dim.Dimtad = 0; } catch { }
            }
            catch { }

            Point2d h = c + u * aOku + v * fr;
            double rr = DonatiOkuCircleDiaCm * 0.5;
            var pl = new Polyline();
            pl.SetDatabaseDefaults(ctx.Db);
            pl.AddVertexAt(0, new Point2d(h.X - rr, h.Y), 1.0, 0, 0);
            pl.AddVertexAt(1, new Point2d(h.X + rr, h.Y), 1.0, 0, 0);
            pl.Closed = true;
            pl.ConstantWidth = DonatiOkuCircleWidthCm;
            pl.Layer = ctx.HalkaLyr;
            ctx.Btr.AppendEntity(pl);
            ctx.Tr.AddNewlyCreatedDBObject(pl, true);
        }

        /// <summary>İç köşelere R=3 cm yay (RADYEILAVE ile aynı).</summary>
        private static void PlacePolylineWithBends(Ctx ctx, List<Point2d> pts)
        {
            if (pts == null || pts.Count < 2) return;
            var pl = new Polyline();
            pl.SetDatabaseDefaults(ctx.Db);
            int vi = 0;
            pl.AddVertexAt(vi++, pts[0], 0, 0, 0);
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector2d dIn = pts[i] - pts[i - 1];
                Vector2d dOut = pts[i + 1] - pts[i];
                double lIn = dIn.Length, lOut = dOut.Length;
                if (lIn < 1e-6 || lOut < 1e-6) continue;
                dIn = dIn / lIn;
                dOut = dOut / lOut;
                double cross = dIn.X * dOut.Y - dIn.Y * dOut.X;
                double theta = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dIn.DotProduct(dOut))));
                if (theta < 1e-4) { pl.AddVertexAt(vi++, pts[i], 0, 0, 0); continue; }
                double trim = Math.Min(BendRadiusCm * Math.Tan(theta / 2.0), Math.Min(lIn, lOut) * 0.45);
                double bulge = Math.Tan(theta / 4.0) * (cross >= 0 ? 1.0 : -1.0);
                pl.AddVertexAt(vi++, pts[i] - dIn * trim, bulge, 0, 0);
                pl.AddVertexAt(vi++, pts[i] + dOut * trim, 0, 0, 0);
            }
            pl.AddVertexAt(vi, pts[pts.Count - 1], 0, 0, 0);
            pl.Layer = ctx.DonatiLyr;
            pl.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            ctx.Btr.AppendEntity(pl);
            ctx.Tr.AddNewlyCreatedDBObject(pl, true);
        }
    }
}
