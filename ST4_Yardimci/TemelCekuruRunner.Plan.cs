using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    internal static partial class TemelCekuruRunner
    {
        private const string LayerTemelIsmi = "TEMEL ISMI (BEYKENT)";
        private const string LayerHatilIsmi = "TEMEL HATILI ISMI (BEYKENT)";
        private const string LayerDosemeIsmi = "DOSEME ISMI (BEYKENT)";
        private const string LayerKirisIsmi = "KIRIS ISMI (BEYKENT)";

        private static readonly Regex PlanRxKot = new Regex(
            @"^[+\-\u00B1]?\d+[.,]\d{1,3}$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex PlanRxTh = new Regex(
            @"TH(?:-(\d+))?\s*\(\s*(\d+)\s*/\s*(\d+)\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex PlanRxTemelEtiket = new Regex(
            @"^(RD\b|T\s*-?\s*\d+|d\s*=)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Dış temel poligonunun içindeki TEMEL çukurlarını okur.
        /// Benzer olanlar tek çift kesit ve çarpan; farklı olanlar ayrı çizilir.
        /// Kot, kalınlık ve hatıl yüksekliği plandan; paspayı, donatı, grobeton, subasman ve hatıl genişliği formdan.
        /// </summary>
        public static void RunFromPlan()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                TemelCekuruForm.Shutdown();
                return;
            }

            Editor ed = doc.Editor;
            double sKot = 0, sUst = 0, sAlt = 0, sCukur = 0, sHatil = 0, sGen = 0, sEn = 0;
            bool touched = false;
            try
            {
                TemelCekuruSettings.EnsureLoaded();
                if (!TryReadPlanGroups(doc, ed, out List<PlanGroup> groups, out string note))
                {
                    ed.WriteMessage("\nTEMELCEKURU: {0}", string.IsNullOrEmpty(note) ? "plan okunamadı." : note);
                    return;
                }

                ed.WriteMessage("\nTEMELCEKURU: {0}", note);
                for (int i = 0; i < groups.Count; i++)
                    ed.WriteMessage("\n  {0}", PlanPreview(groups[i]));
                string path = EnsureTempDwg();
                if (string.IsNullOrEmpty(path))
                {
                    ed.WriteMessage("\nTEMELCEKURU: gömülü TEMEL_CUKURU.dwg bulunamadı.");
                    return;
                }

                var opt = new PromptPointOptions("\nTemel çukuru — sol üst köşe: ") { AllowNone = false };
                PromptPointResult ppr = ed.GetPoint(opt);
                if (ppr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nTEMELCEKURU: iptal.");
                    return;
                }

                sKot = TemelCekuruSettings.TemelUstKotuCm;
                sUst = TemelCekuruSettings.UstTemelYuksekligiCm;
                sAlt = TemelCekuruSettings.AltTemelYuksekligiCm;
                sCukur = TemelCekuruSettings.CukurDerinligiCm;
                sHatil = TemelCekuruSettings.HatilYuksekligiCm;
                sGen = TemelCekuruSettings.TemelGenisligiCm;
                sEn = TemelCekuruSettings.DigerGenislikCm;
                touched = true;

                int n = 0;
                int drawnGroups = 0;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    double x = ppr.Value.X;
                    double y = ppr.Value.Y;
                    double gap = 100.0 * DuPerCm;
                    int kesitNo = 1;
                    for (int i = 0; i < groups.Count; i++)
                    {
                        PlanGroup g = groups[i];
                        ApplyPlanGroup(g);
                        if (!PlanGroupFits(out string why))
                        {
                            ed.WriteMessage(
                                "\nTEMELCEKURU: {0}×{1} cm ({2} adet) atlandı — {3}",
                                g.GenislikCm, g.EnCm, g.Carpan, why);
                            continue;
                        }

                        Fit.PeekAdet(TemelCekuruSettings.TemelGenisligiCm, out int ust1, out int alt1);
                        Fit.PeekAdet(TemelCekuruSettings.DigerGenislikCm, out int ust2, out int alt2);
                        int yan1 = Fit.YanAcilimAdet(TemelCekuruSettings.DigerGenislikCm);
                        int yan2 = Fit.YanAcilimAdet(TemelCekuruSettings.TemelGenisligiCm);
                        int noGen = kesitNo++;
                        int noEn = kesitNo++;
                        double right;
                        n += Insert(tr, doc.Database, path, new Point3d(x, y, 0),
                            TemelCekuruSettings.TemelGenisligiCm, noGen, ust2, alt2, yan1, g.Carpan, out right);
                        double right2;
                        n += Insert(tr, doc.Database, path, new Point3d(right + gap, y, 0),
                            TemelCekuruSettings.DigerGenislikCm, noEn, ust1, alt1, yan2, g.Carpan, out right2);
                        x = right2 + gap;
                        try { DrawPlanKesitSymbols(tr, doc.Database, g, noGen, noEn); }
                        catch { }
                        drawnGroups++;
                        ed.WriteMessage(
                            "\n  {0}×{1} cm  ×{2}  üst {3:0} / {4:0} cm  alt {5:0} / {6:0} cm  hatıl {7:0} cm",
                            g.GenislikCm, g.EnCm, g.Carpan,
                            TemelCekuruSettings.TemelUstKotuCm, TemelCekuruSettings.UstTemelYuksekligiCm,
                            g.AltKnown ? g.AltKotCm : TemelCekuruSettings.TemelUstKotuCm - TemelCekuruSettings.StemCm,
                            TemelCekuruSettings.AltTemelYuksekligiCm,
                            TemelCekuruSettings.HatilYuksekligiCm);
                    }
                    tr.Commit();
                }

                ed.WriteMessage("\nTEMELCEKURU: {0} tip, {1} nesne.", drawnGroups, n);
            }
            catch (System.Exception ex)
            {
                try { ed.WriteMessage("\nTEMELCEKURU hata: {0}", ex.Message); }
                catch { }
            }
            finally
            {
                if (touched)
                {
                    TemelCekuruSettings.TemelUstKotuCm = sKot;
                    TemelCekuruSettings.UstTemelYuksekligiCm = sUst;
                    TemelCekuruSettings.AltTemelYuksekligiCm = sAlt;
                    TemelCekuruSettings.CukurDerinligiCm = sCukur;
                    TemelCekuruSettings.HatilYuksekligiCm = sHatil;
                    TemelCekuruSettings.TemelGenisligiCm = sGen;
                    TemelCekuruSettings.DigerGenislikCm = sEn;
                }
                TemelCekuruForm.Shutdown();
            }
        }

        private static void ApplyPlanGroup(PlanGroup g)
        {
            TemelCekuruSettings.TemelGenisligiCm = g.GenislikCm;
            TemelCekuruSettings.DigerGenislikCm = g.EnCm;
            if (g.HatilKnown)
                TemelCekuruSettings.HatilYuksekligiCm = g.HatilCm;
            if (g.UstKnown)
            {
                TemelCekuruSettings.TemelUstKotuCm = g.UstKotCm;
                TemelCekuruSettings.UstTemelYuksekligiCm = g.UstKalinlikCm;
            }
            if (g.AltKnown)
                TemelCekuruSettings.AltTemelYuksekligiCm = g.AltKalinlikCm;
            if (g.UstKnown && g.AltKnown)
            {
                double stem = g.UstKotCm - g.AltKotCm;
                TemelCekuruSettings.CukurDerinligiCm = stem + TemelCekuruSettings.HatilYuksekligiCm;
            }
        }

        private static bool PlanGroupFits(out string why)
        {
            why = null;
            if (TemelCekuruSettings.PaspayiCm * 2.0 + 2.0 > TemelCekuruSettings.AltTemelYuksekligiCm)
            {
                why = "paspayı alt temel kalınlığına sığmıyor";
                return false;
            }
            if (TemelCekuruSettings.HatilYuksekligiCm > 0.5
                && TemelCekuruSettings.SubasmanYuksekligiCm + 10.0 > TemelCekuruSettings.HatilYuksekligiCm)
            {
                why = "subasman, hatıl yüksekliğinin en az 10 cm altında olmalı";
                return false;
            }
            if (TemelCekuruSettings.StemCm + 0.5 < TemelCekuruSettings.UstTemelYuksekligiCm)
            {
                why = "üst ve alt kot arası, üst temelden küçük";
                return false;
            }
            return true;
        }

        private static bool TryReadPlanGroups(
            Autodesk.AutoCAD.ApplicationServices.Document doc, Editor ed,
            out List<PlanGroup> groups, out string note)
        {
            groups = new List<PlanGroup>();
            note = null;

            var eopt = new PromptEntityOptions("\nTemel dış poligonunu seçin: ") { AllowNone = false };
            eopt.SetRejectMessage("\nKapalı poligon seçin.");
            eopt.AddAllowedClass(typeof(Polyline), true);
            eopt.AddAllowedClass(typeof(Polyline2d), true);
            PromptEntityResult per = ed.GetEntity(eopt);
            if (per.Status != PromptStatus.OK)
            {
                note = "iptal.";
                return false;
            }

            var gf = new GeometryFactory();
            var kots = new List<PlanKot>();
            var labels = new List<PlanTag>();
            var hatils = new List<PlanTag>();
            var candidates = new List<PlanPoly>();
            Polygon outer = null;

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var picked = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Entity;
                if (!TryPolygon(tr, gf, picked, out outer) || outer == null)
                {
                    note = "seçilen nesne kapalı poligon değil.";
                    return false;
                }

                var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                Envelope env = outer.EnvelopeInternal;
                const double pad = 220.0;
                foreach (ObjectId id in ms)
                {
                    Entity ent;
                    try { ent = tr.GetObject(id, OpenMode.ForRead) as Entity; }
                    catch { continue; }
                    if (ent == null) continue;
                    if (!ExtentsNear(ent, env, pad)) continue;

                    if (ent is DBText || ent is MText)
                    {
                        if (!TryTextPoint(ent, out Point3d pt)) continue;
                        string text = FlattenPlan(EntityText(ent));
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        string layer = ent.Layer ?? "";
                        if (LayerIs(layer, LayerKotYazi) && TryParseKotM(text, out double elev))
                            kots.Add(new PlanKot { X = pt.X, Y = pt.Y, ElevM = elev });
                        else if (LayerIs(layer, LayerHatilIsmi) && TryParseHatilH(text, out double h))
                            hatils.Add(new PlanTag { X = pt.X, Y = pt.Y, Value = h, Text = text });
                        else if (LayerIs(layer, LayerTemelIsmi) || LayerIs(layer, LayerDosemeIsmi) || LayerIs(layer, LayerKirisIsmi))
                            labels.Add(new PlanTag { X = pt.X, Y = pt.Y, Text = text, Layer = layer });
                        continue;
                    }

                    if (id == per.ObjectId) continue;
                    if (!LayerIs(ent.Layer, LayerTemel)) continue;
                    if (!TryPolygon(tr, gf, ent, out Polygon poly) || poly == null) continue;
                    if (poly.Area >= outer.Area * 0.92) continue;
                    var cen = poly.Centroid;
                    if (cen == null || cen.IsEmpty) continue;
                    if (!outer.Contains(gf.CreatePoint(cen.Coordinate))) continue;
                    if (!TryPitSides(poly, out int gen, out int en, out PitMark mark)) continue;
                    candidates.Add(new PlanPoly
                    {
                        Poly = poly,
                        GenislikCm = gen,
                        EnCm = en,
                        Cx = cen.X,
                        Cy = cen.Y,
                        Mark = mark
                    });
                }
                tr.Commit();
            }

            var pits = new List<PlanPoly>();
            for (int i = 0; i < candidates.Count; i++)
            {
                bool holdsAnother = false;
                var ci = gf.CreatePoint(new Coordinate(candidates[i].Cx, candidates[i].Cy));
                for (int j = 0; j < candidates.Count; j++)
                {
                    if (i == j) continue;
                    if (candidates[i].Poly.Contains(gf.CreatePoint(new Coordinate(candidates[j].Cx, candidates[j].Cy))))
                    {
                        holdsAnother = true;
                        break;
                    }
                }
                if (!holdsAnother && outer.Contains(ci))
                    pits.Add(candidates[i]);
            }

            if (pits.Count == 0)
            {
                note = "seçilen poligonun içinde TEMEL katmanında çukur poligonu yok.";
                return false;
            }

            var pairs = PairKots(kots, outer, gf);
            ScorePairs(pairs, labels, pits, gf);
            var hatilUse = hatils.FindAll(h => outer.Distance(gf.CreatePoint(new Coordinate(h.X, h.Y))) <= 120.0);
            bool oneHatil = hatilUse.Count > 0 && SameHatil(hatilUse);

            var raw = new List<PlanGroup>();
            for (int i = 0; i < pits.Count; i++)
            {
                PlanPoly pit = pits[i];
                var pool = PoolForPit(pairs, pit, pits, gf);
                var g = new PlanGroup
                {
                    GenislikCm = pit.GenislikCm,
                    EnCm = pit.EnCm,
                    Carpan = 1,
                    SortX = pit.Cx,
                    SortY = pit.Cy,
                    Marks = new List<PitMark>()
                };
                if (pit.Mark != null) g.Marks.Add(pit.Mark);
                PickLevels(pool, g);
                if (oneHatil)
                {
                    g.HatilKnown = true;
                    g.HatilCm = hatilUse[0].Value;
                }
                else if (TryNearestHatil(hatilUse, pit, gf, out double hh))
                {
                    g.HatilKnown = true;
                    g.HatilCm = hh;
                }
                raw.Add(g);
            }

            groups = MergeGroups(raw);
            note = Describe(groups, pits.Count);
            return groups.Count > 0;
        }

        private static List<PlanPair> PoolForPit(List<PlanPair> pairs, PlanPoly pit, List<PlanPoly> pits, GeometryFactory gf)
        {
            var strong = new List<PlanPair>();
            var wide = new List<PlanPair>();
            for (int i = 0; i < pairs.Count; i++)
            {
                PlanPair p = pairs[i];
                if (p.Score < 0) continue;
                bool inside = pit.Poly.Covers(gf.CreatePoint(p.Mid));
                bool shared = !InsideAnyPit(p.Mid, pits, gf);
                if (!inside && !shared) continue;
                wide.Add(p);
                if (p.Score > 0) strong.Add(p);
            }
            List<PlanPair> pool = strong.Count > 0 ? strong : wide;
            if (LevelCount(pool) < 2 && LevelCount(wide) >= 2)
                pool = wide;
            return pool;
        }

        private static bool InsideAnyPit(Coordinate c, List<PlanPoly> pits, GeometryFactory gf)
        {
            var pt = gf.CreatePoint(c);
            for (int i = 0; i < pits.Count; i++)
            {
                if (pits[i].Poly.Covers(pt)) return true;
            }
            return false;
        }

        private static void PickLevels(List<PlanPair> pool, PlanGroup g)
        {
            if (pool == null || pool.Count == 0) return;
            double topMax = pool[0].TopCm;
            for (int i = 1; i < pool.Count; i++)
                if (pool[i].TopCm > topMax) topMax = pool[i].TopCm;
            var ust = new List<PlanPair>();
            for (int i = 0; i < pool.Count; i++)
            {
                if (Math.Abs(pool[i].TopCm - topMax) <= 2.0)
                    ust.Add(pool[i]);
            }
            g.UstKnown = true;
            g.UstKotCm = MedianTop(ust);
            g.UstKalinlikCm = MedianThick(ust);

            double low = double.MaxValue;
            bool anyLow = false;
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i].TopCm < g.UstKotCm - 8.0 && pool[i].TopCm < low)
                {
                    low = pool[i].TopCm;
                    anyLow = true;
                }
            }
            if (!anyLow) return;
            var alt = new List<PlanPair>();
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i].TopCm < g.UstKotCm - 8.0 && Math.Abs(pool[i].TopCm - low) <= 2.0)
                    alt.Add(pool[i]);
            }
            if (alt.Count == 0) return;
            g.AltKnown = true;
            g.AltKotCm = MedianTop(alt);
            g.AltKalinlikCm = MedianThick(alt);
        }

        private static int LevelCount(List<PlanPair> pool)
        {
            if (pool == null || pool.Count == 0) return 0;
            var tops = new List<double>();
            for (int i = 0; i < pool.Count; i++)
            {
                bool seen = false;
                for (int j = 0; j < tops.Count; j++)
                {
                    if (Math.Abs(tops[j] - pool[i].TopCm) <= 8.0) { seen = true; break; }
                }
                if (!seen) tops.Add(pool[i].TopCm);
            }
            return tops.Count;
        }

        private static List<PlanPair> PairKots(List<PlanKot> kots, Polygon outer, GeometryFactory gf)
        {
            var pairs = new List<PlanPair>();
            var used = new bool[kots.Count];
            while (true)
            {
                int ia = -1, ib = -1;
                double best = 30.0;
                for (int i = 0; i < kots.Count; i++)
                {
                    if (used[i]) continue;
                    for (int j = i + 1; j < kots.Count; j++)
                    {
                        if (used[j]) continue;
                        double dx = kots[i].X - kots[j].X;
                        double dy = kots[i].Y - kots[j].Y;
                        double d = Math.Sqrt(dx * dx + dy * dy);
                        if (d > best) continue;
                        double thick = Math.Abs(kots[i].ElevM - kots[j].ElevM) * 100.0;
                        if (thick < 8.0 || thick > 250.0) continue;
                        best = d;
                        ia = i;
                        ib = j;
                    }
                }
                if (ia < 0) break;
                used[ia] = used[ib] = true;
                double top = Math.Max(kots[ia].ElevM, kots[ib].ElevM);
                double bot = Math.Min(kots[ia].ElevM, kots[ib].ElevM);
                var mid = new Coordinate((kots[ia].X + kots[ib].X) * 0.5, (kots[ia].Y + kots[ib].Y) * 0.5);
                if (outer.Distance(gf.CreatePoint(mid)) > 100.0) continue;
                pairs.Add(new PlanPair
                {
                    Mid = mid,
                    TopCm = Math.Round(top * 100.0, MidpointRounding.AwayFromZero),
                    ThickCm = Math.Round((top - bot) * 100.0, MidpointRounding.AwayFromZero)
                });
            }
            return pairs;
        }

        private static void ScorePairs(List<PlanPair> pairs, List<PlanTag> labels, List<PlanPoly> pits, GeometryFactory gf)
        {
            for (int i = 0; i < pairs.Count; i++)
            {
                PlanPair p = pairs[i];
                int score = 0;
                if (InsideAnyPit(p.Mid, pits, gf)) score += 1;
                for (int k = 0; k < labels.Count; k++)
                {
                    double dx = labels[k].X - p.Mid.X;
                    double dy = labels[k].Y - p.Mid.Y;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    string tx = (labels[k].Text ?? "").Trim();
                    bool kiris = LayerIs(labels[k].Layer, LayerKirisIsmi);
                    bool doseme = LayerIs(labels[k].Layer, LayerDosemeIsmi);
                    bool rd = tx.StartsWith("RD", StringComparison.OrdinalIgnoreCase);
                    if (kiris && d <= 80.0) score -= 3;
                    else if (doseme && d <= 100.0 && !rd) score -= 2;
                    else if (d <= 180.0 && PlanRxTemelEtiket.IsMatch(tx)) score += 2;
                }
                p.Score = score;
            }
        }

        private static bool SameHatil(List<PlanTag> hats)
        {
            int h0 = (int)Math.Round(hats[0].Value, MidpointRounding.AwayFromZero);
            for (int i = 1; i < hats.Count; i++)
            {
                int h = (int)Math.Round(hats[i].Value, MidpointRounding.AwayFromZero);
                if (h != h0) return false;
            }
            return true;
        }

        private static bool TryNearestHatil(List<PlanTag> hats, PlanPoly pit, GeometryFactory gf, out double hCm)
        {
            hCm = 0;
            double best = 250.0;
            bool found = false;
            for (int i = 0; i < hats.Count; i++)
            {
                double d = pit.Poly.Distance(gf.CreatePoint(new Coordinate(hats[i].X, hats[i].Y)));
                if (d < best)
                {
                    best = d;
                    hCm = hats[i].Value;
                    found = true;
                }
            }
            return found;
        }

        private static List<PlanGroup> MergeGroups(List<PlanGroup> raw)
        {
            var groups = new List<PlanGroup>();
            for (int i = 0; i < raw.Count; i++)
            {
                PlanGroup g = raw[i];
                PlanGroup hit = null;
                for (int j = 0; j < groups.Count; j++)
                {
                    if (SameGroup(groups[j], g)) { hit = groups[j]; break; }
                }
                if (hit == null)
                {
                    g.Carpan = 1;
                    groups.Add(g);
                }
                else
                {
                    hit.Carpan++;
                    if (g.SortX < hit.SortX) hit.SortX = g.SortX;
                    if (g.Marks != null)
                    {
                        if (hit.Marks == null) hit.Marks = new List<PitMark>();
                        hit.Marks.AddRange(g.Marks);
                    }
                }
            }
            groups.Sort((a, b) =>
            {
                int c = a.SortX.CompareTo(b.SortX);
                return c != 0 ? c : a.SortY.CompareTo(b.SortY);
            });
            return groups;
        }

        private static bool SameGroup(PlanGroup a, PlanGroup b)
        {
            if (a.GenislikCm != b.GenislikCm || a.EnCm != b.EnCm) return false;
            if (a.UstKnown != b.UstKnown || a.AltKnown != b.AltKnown || a.HatilKnown != b.HatilKnown) return false;
            if (a.UstKnown && (a.UstKotCm != b.UstKotCm || a.UstKalinlikCm != b.UstKalinlikCm)) return false;
            if (a.AltKnown && (a.AltKotCm != b.AltKotCm || a.AltKalinlikCm != b.AltKalinlikCm)) return false;
            if (a.HatilKnown && a.HatilCm != b.HatilCm) return false;
            return true;
        }

        private static string PlanPreview(PlanGroup g)
        {
            string ust = g.UstKnown
                ? string.Format(CultureInfo.InvariantCulture, "üst {0:0}/{1:0} cm", g.UstKotCm, g.UstKalinlikCm)
                : "üst formdan";
            string alt = g.AltKnown
                ? string.Format(CultureInfo.InvariantCulture, "alt {0:0}/{1:0} cm", g.AltKotCm, g.AltKalinlikCm)
                : "alt formdan";
            string hat = g.HatilKnown
                ? string.Format(CultureInfo.InvariantCulture, "hatıl {0:0} cm", g.HatilCm)
                : "hatıl formdan";
            string der = "";
            if (g.UstKnown && g.AltKnown)
            {
                double hatil = g.HatilKnown ? g.HatilCm : TemelCekuruSettings.HatilYuksekligiCm;
                der = string.Format(
                    CultureInfo.InvariantCulture,
                    ", çukur derinliği {0:0} cm",
                    g.UstKotCm - g.AltKotCm + hatil);
            }
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}×{1} cm ×{2}: {3}, {4}, {5}{6}",
                g.GenislikCm, g.EnCm, g.Carpan, ust, alt, hat, der);
        }

        private static string Describe(List<PlanGroup> groups, int pitCount)
        {
            int withKot = 0;
            for (int i = 0; i < groups.Count; i++)
                if (groups[i].UstKnown) withKot++;
            string kot = withKot == 0
                ? "Kot çifti bulunamadı; üst/alt kot formdan."
                : string.Format(CultureInfo.InvariantCulture, "{0} tipte kot okundu.", withKot);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} çukur, {1} tip. {2} Yerleştirme noktasını verin.",
                pitCount, groups.Count, kot);
        }

        private static double MedianTop(List<PlanPair> list)
        {
            var v = new List<double>();
            for (int i = 0; i < list.Count; i++) v.Add(list[i].TopCm);
            return Median(v);
        }

        private static double MedianThick(List<PlanPair> list)
        {
            var v = new List<double>();
            for (int i = 0; i < list.Count; i++) v.Add(list[i].ThickCm);
            return Median(v);
        }

        private static double Median(List<double> v)
        {
            v.Sort();
            int n = v.Count;
            if (n == 0) return 0;
            if ((n & 1) == 1) return v[n / 2];
            return Math.Round((v[n / 2 - 1] + v[n / 2]) * 0.5, MidpointRounding.AwayFromZero);
        }

        private static bool TryPitSides(Polygon poly, out int gen, out int en, out PitMark mark)
        {
            gen = 0;
            en = 0;
            mark = null;
            Coordinate[] c = poly.ExteriorRing == null ? null : poly.ExteriorRing.Coordinates;
            if (c == null || c.Length < 4)
            {
                mark = null;
                return false;
            }
            double best = 0, ux = 1, uy = 0;
            for (int i = 1; i < c.Length; i++)
            {
                double dx = c[i].X - c[i - 1].X;
                double dy = c[i].Y - c[i - 1].Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len <= best) continue;
                best = len;
                ux = dx / len;
                uy = dy / len;
            }
            if (best < 15.0) return false;
            double vx = -uy, vy = ux;
            double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            for (int i = 0; i < c.Length; i++)
            {
                double u = c[i].X * ux + c[i].Y * uy;
                double v = c[i].X * vx + c[i].Y * vy;
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }
            double su = maxU - minU;
            double sv = maxV - minV;
            if (su < 20.0 || sv < 20.0) return false;
            double a = Math.Min(su, sv);
            double b = Math.Max(su, sv);
            gen = (int)Math.Round(a, MidpointRounding.AwayFromZero);
            en = (int)Math.Round(b, MidpointRounding.AwayFromZero);
            if (gen < 20 || en < 20) return false;
            double cu = (minU + maxU) * 0.5;
            double cv = (minV + maxV) * 0.5;
            mark = new PitMark { Cx = cu * ux + cv * vx, Cy = cu * uy + cv * vy };
            if (su >= sv)
            {
                mark.LongX = ux;
                mark.LongY = uy;
                mark.ShortX = vx;
                mark.ShortY = vy;
                mark.HalfEn = su * 0.5;
                mark.HalfGen = sv * 0.5;
            }
            else
            {
                mark.LongX = vx;
                mark.LongY = vy;
                mark.ShortX = ux;
                mark.ShortY = uy;
                mark.HalfEn = sv * 0.5;
                mark.HalfGen = su * 0.5;
            }
            return true;
        }

        private static bool TryPolygon(Transaction tr, GeometryFactory gf, Entity ent, out Polygon poly)
        {
            poly = null;
            if (!TryRing(tr, ent, out List<Coordinate> ring)) return false;
            poly = ToPolygon(gf, ring);
            return poly != null;
        }

        private static bool TryRing(Transaction tr, Entity ent, out List<Coordinate> pts)
        {
            pts = new List<Coordinate>();
            if (ent is Polyline pl)
            {
                int n = pl.NumberOfVertices;
                if (n < 3) return false;
                for (int i = 0; i < n; i++)
                {
                    Point2d p = pl.GetPoint2dAt(i);
                    pts.Add(new Coordinate(p.X, p.Y));
                }
                if (!pl.Closed && pts[0].Distance(pts[pts.Count - 1]) > 1.0) return false;
                return true;
            }
            if (ent is Polyline2d pl2)
            {
                foreach (ObjectId vid in pl2)
                {
                    if (tr.GetObject(vid, OpenMode.ForRead) is Vertex2d v)
                        pts.Add(new Coordinate(v.Position.X, v.Position.Y));
                }
                if (pts.Count < 3) return false;
                if (!pl2.Closed && pts[0].Distance(pts[pts.Count - 1]) > 1.0) return false;
                return true;
            }
            return false;
        }

        private static Polygon ToPolygon(GeometryFactory gf, List<Coordinate> raw)
        {
            var pts = new List<Coordinate>();
            for (int i = 0; i < raw.Count; i++)
            {
                if (pts.Count > 0 && pts[pts.Count - 1].Distance(raw[i]) < 0.5) continue;
                pts.Add(raw[i]);
            }
            if (pts.Count >= 2 && pts[0].Distance(pts[pts.Count - 1]) < 0.5)
                pts.RemoveAt(pts.Count - 1);
            if (pts.Count < 3) return null;
            pts.Add(new Coordinate(pts[0].X, pts[0].Y));
            try
            {
                var poly = gf.CreatePolygon(gf.CreateLinearRing(pts.ToArray()));
                if (poly == null || poly.IsEmpty || poly.Area < 1.0) return null;
                if (!poly.IsValid)
                    poly = LargestPolygon(poly.Buffer(0));
                return poly != null && !poly.IsEmpty && poly.Area >= 1.0 ? poly : null;
            }
            catch
            {
                return null;
            }
        }

        private static Polygon LargestPolygon(NetTopologySuite.Geometries.Geometry g)
        {
            if (g == null || g.IsEmpty) return null;
            if (g is Polygon p) return p;
            Polygon best = null;
            for (int i = 0; i < g.NumGeometries; i++)
            {
                if (g.GetGeometryN(i) is Polygon pi && (best == null || pi.Area > best.Area))
                    best = pi;
            }
            return best;
        }

        private static bool ExtentsNear(Entity ent, Envelope env, double pad)
        {
            try
            {
                Extents3d e = ent.GeometricExtents;
                if (e.MaxPoint.X < env.MinX - pad || e.MinPoint.X > env.MaxX + pad) return false;
                if (e.MaxPoint.Y < env.MinY - pad || e.MinPoint.Y > env.MaxY + pad) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryTextPoint(Entity ent, out Point3d p)
        {
            if (ent is DBText t)
            {
                bool plain = t.HorizontalMode == TextHorizontalMode.TextLeft
                    && t.VerticalMode == TextVerticalMode.TextBase;
                p = plain ? t.Position : t.AlignmentPoint;
                return true;
            }
            if (ent is MText m)
            {
                p = m.Location;
                return true;
            }
            p = Point3d.Origin;
            return false;
        }

        private static string EntityText(Entity ent)
        {
            if (ent is DBText t) return t.TextString;
            if (ent is MText m) return m.Contents;
            return null;
        }

        private static string FlattenPlan(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = s.Replace("\\P", " ").Replace("\\p", " ");
            t = Regex.Replace(t, @"\\[A-Za-z][^;]*;", "");
            t = Regex.Replace(t, "[{}]", "");
            return t.Trim();
        }

        private static bool TryParseKotM(string raw, out double elevM)
        {
            elevM = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim().Replace(',', '.');
            if (t.Length > 0 && t[0] == '\u00B1')
                t = "+" + t.Substring(1);
            else
                t = t.Replace("\u00B1", "+");
            if (!PlanRxKot.IsMatch(t)) return false;
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out elevM))
                return false;
            return elevM >= -50.0 && elevM <= 50.0;
        }

        private static bool TryParseHatilH(string text, out double hCm)
        {
            hCm = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            Match m = PlanRxTh.Match(text);
            if (!m.Success) return false;
            if (!int.TryParse(m.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
                return false;
            if (h < 10 || h > 400) return false;
            hCm = h;
            return true;
        }

        private const string KesitSembolResource = "ST4Yardimci.KesitSembol.kesit_sembol.dwg";
        private const double KesitSembolOx = 1660.7;
        private const double KesitSembolOy = 1206.23;
        private const double KesitSembolYaricap = 20.0;
        private const double KesitSembolPay = 5.0;

        /// <summary>
        /// Planda, çukurun iki doğrultusuna merdiven kesit sembolü.
        /// Kısa kenar = genişlik kesiti, uzun kenar = en kesiti. Yazı: 1-1 KESİTİ.
        /// </summary>
        private static void DrawPlanKesitSymbols(Transaction tr, Database db, PlanGroup g, int noGen, int noEn)
        {
            if (g == null || g.Marks == null || g.Marks.Count == 0) return;
            string srcPath = EnsureKesitSembolTemp();
            if (string.IsNullOrEmpty(srcPath)) return;
            using (var src = new Database(false, true))
            {
                src.ReadDwgFile(srcPath, FileOpenMode.OpenForReadAndAllShare, true, null);
                var srcIds = new ObjectIdCollection();
                using (var trs = src.TransactionManager.StartTransaction())
                {
                    var btSrc = (BlockTable)trs.GetObject(src.BlockTableId, OpenMode.ForRead);
                    var msSrc = (BlockTableRecord)trs.GetObject(btSrc[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId id in msSrc)
                    {
                        if (!id.IsValid || id.IsErased) continue;
                        if (trs.GetObject(id, OpenMode.ForRead) is Entity)
                            srcIds.Add(id);
                    }
                    trs.Commit();
                }
                if (srcIds.Count == 0) return;
                for (int i = 0; i < g.Marks.Count; i++)
                {
                    PitMark m = g.Marks[i];
                    PlacePlanCut(tr, db, src, srcIds, noGen, m.Cx, m.Cy, m.ShortX, m.ShortY, m.HalfGen);
                    PlacePlanCut(tr, db, src, srcIds, noEn, m.Cx, m.Cy, m.LongX, m.LongY, m.HalfEn);
                }
            }
        }

        private static void PlacePlanCut(
            Transaction tr, Database db, Database src, ObjectIdCollection srcIds,
            int no, double cx, double cy, double ax, double ay, double half)
        {
            double len = Math.Sqrt(ax * ax + ay * ay);
            if (len < 1e-9 || half < 1.0) return;
            ax /= len;
            ay /= len;
            double outD = half + 90.0 + KesitSembolYaricap;
            double bx = -ay, by = ax;
            string num = no.ToString(CultureInfo.InvariantCulture);
            PlacePlanKesitSembol(tr, db, src, srcIds, num,
                cx + ax * outD, cy + ay * outD, -ax, -ay, bx, by);
            PlacePlanKesitSembol(tr, db, src, srcIds, num,
                cx - ax * outD, cy - ay * outD, ax, ay, bx, by);
        }

        /// <summary>Kesik çizgi içeri bakar; üçgen bakış yönüne döner. Dairedeki numara düz kalır.</summary>
        private static void PlacePlanKesitSembol(
            Transaction tr, Database db, Database src, ObjectIdCollection srcIds,
            string num, double cx, double cy, double inX, double inY, double bakX, double bakY)
        {
            if (srcIds == null || srcIds.Count == 0) return;
            double inLen = Math.Sqrt(inX * inX + inY * inY);
            if (inLen < 1e-9) { inX = 1; inY = 0; }
            else { inX /= inLen; inY /= inLen; }
            double bLen = Math.Sqrt(bakX * bakX + bakY * bakY);
            if (bLen < 1e-9) { bakX = -inY; bakY = inX; }
            else { bakX /= bLen; bakY /= bLen; }

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            var mapping = new IdMapping();
            db.WblockCloneObjects(srcIds, ms.ObjectId, mapping, DuplicateRecordCloning.Ignore, false);

            var origin = new Point3d(KesitSembolOx, KesitSembolOy, 0);
            var center = new Point3d(cx, cy, 0);
            double ang = Math.Atan2(inY, inX);
            double triX = -inY, triY = inX;
            bool mirrorTri = triX * bakX + triY * bakY < 0;
            Matrix3d xf = Matrix3d.Displacement(center - origin)
                * Matrix3d.Rotation(ang, Vector3d.ZAxis, origin);
            if (mirrorTri)
                xf = xf * Matrix3d.Mirroring(new Plane(origin, Vector3d.YAxis));

            foreach (IdPair pair in mapping)
            {
                if (!pair.IsCloned || !pair.Value.IsValid) continue;
                if (!(tr.GetObject(pair.Value, OpenMode.ForWrite, false) is Entity ent) || ent.IsErased) continue;
                if (ent is DBText txt)
                {
                    txt.TextString = num;
                    txt.Rotation = 0;
                    try { txt.AdjustAlignment(db); } catch { }
                    try { txt.TransformBy(Matrix3d.Displacement(center - origin)); } catch { }
                    continue;
                }
                try { ent.TransformBy(xf); } catch { }
                if (IsKesikCizgi(tr, ent, inX, inY))
                {
                    try { ent.Erase(); } catch { }
                }
            }
        }

        /// <summary>Şablondaki içeri bakan kesik çizgi. Daire, yazı ve üçgen kalır.</summary>
        private static bool IsKesikCizgi(Transaction tr, Entity ent, double inX, double inY)
        {
            if (ent == null || ent.IsErased) return false;
            if (ent is DBText || ent is MText || ent is Circle || ent is Arc || ent is Hatch || ent is Solid)
                return false;
            if (LinetypeIsDashed(tr, ent)) return ent is Line || ent is Polyline;
            if (ent is Line ln)
                return AlongInDir(ln.StartPoint, ln.EndPoint, inX, inY, 30.0);
            if (ent is Polyline pl && !pl.Closed)
            {
                int n = pl.NumberOfVertices;
                double best = 0;
                Point2d a = Point2d.Origin, b = Point2d.Origin;
                for (int i = 0; i < n - 1; i++)
                {
                    Point2d p = pl.GetPoint2dAt(i);
                    Point2d q = pl.GetPoint2dAt(i + 1);
                    double dx = q.X - p.X, dy = q.Y - p.Y;
                    double L = Math.Sqrt(dx * dx + dy * dy);
                    if (L <= best) continue;
                    best = L;
                    a = p;
                    b = q;
                }
                return AlongInDir(new Point3d(a.X, a.Y, 0), new Point3d(b.X, b.Y, 0), inX, inY, 30.0);
            }
            return false;
        }

        private static bool AlongInDir(Point3d a, Point3d b, double inX, double inY, double minLen)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double L = Math.Sqrt(dx * dx + dy * dy);
            if (L < minLen) return false;
            double dot = Math.Abs(dx * inX + dy * inY) / L;
            return dot > 0.96;
        }

        private static bool LinetypeIsDashed(Transaction tr, Entity ent)
        {
            string lt = ent.Linetype ?? "";
            if (lt.Equals("ByLayer", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var rec = tr.GetObject(ent.LayerId, OpenMode.ForRead) as LayerTableRecord;
                    if (rec != null && !rec.LinetypeObjectId.IsNull)
                    {
                        var ltr = tr.GetObject(rec.LinetypeObjectId, OpenMode.ForRead) as LinetypeTableRecord;
                        if (ltr != null) lt = ltr.Name ?? "";
                    }
                }
                catch { return false; }
            }
            if (lt.Length == 0
                || lt.Equals("Continuous", StringComparison.OrdinalIgnoreCase)
                || lt.Equals("ByBlock", StringComparison.OrdinalIgnoreCase)
                || lt.Equals("ByLayer", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        private static void PlaceKesitEtiket(Transaction tr, Database db, string text, double x, double y)
        {
            const string layer = "KESIT ISMI (BEYKENT)";
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layer))
            {
                lt.UpgradeOpen();
                var rec = new LayerTableRecord
                {
                    Name = layer,
                    Color = Color.FromColorIndex(ColorMethod.ByAci, 6),
                    LineWeight = LineWeight.LineWeight020
                };
                var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (ltt.Has("DASHED")) rec.LinetypeObjectId = ltt["DASHED"];
                else if (ltt.Has("Dashed")) rec.LinetypeObjectId = ltt["Dashed"];
                lt.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            ObjectId style = db.Textstyle;
            var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (tst.Has(TextStyleYazi)) style = tst[TextStyleYazi];
            var t = new DBText
            {
                TextString = text,
                Height = 12.0,
                Layer = layer,
                Color = Color.FromColorIndex(ColorMethod.ByLayer, 256),
                TextStyleId = style,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                AlignmentPoint = new Point3d(x, y, 0)
            };
            ms.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
            try
            {
                t.AdjustAlignment(db);
                Extents3d e = t.GeometricExtents;
                double dx = x - (e.MinPoint.X + e.MaxPoint.X) * 0.5;
                double dy = y - (e.MinPoint.Y + e.MaxPoint.Y) * 0.5;
                if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                    t.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
            }
            catch { }
        }

        private static string EnsureKesitSembolTemp()
        {
            Assembly asm = typeof(TemelCekuruRunner).Assembly;
            using (Stream stream = asm.GetManifestResourceStream(KesitSembolResource))
            {
                if (stream == null) return null;
                string path = Path.Combine(Path.GetTempPath(),
                    "ST4Yardimci_kesit_sembol_" + asm.ManifestModule.ModuleVersionId.ToString("N") + ".dwg");
                if (!File.Exists(path) || new FileInfo(path).Length != stream.Length)
                {
                    string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.Read))
                        stream.CopyTo(fs);
                    try
                    {
                        if (File.Exists(path)) File.Delete(path);
                        File.Move(tmp, path);
                    }
                    catch
                    {
                        try { File.Delete(tmp); } catch { }
                        if (!File.Exists(path)) return null;
                    }
                }
                return path;
            }
        }

        private sealed class PlanKot
        {
            public double X, Y, ElevM;
        }

        private sealed class PlanTag
        {
            public double X, Y, Value;
            public string Text;
            public string Layer;
        }

        private sealed class PlanPair
        {
            public Coordinate Mid;
            public double TopCm;
            public double ThickCm;
            public int Score;
        }

        private sealed class PlanPoly
        {
            public Polygon Poly;
            public int GenislikCm;
            public int EnCm;
            public double Cx, Cy;
            public PitMark Mark;
        }

        private sealed class PitMark
        {
            public double Cx, Cy;
            public double LongX, LongY;
            public double ShortX, ShortY;
            public double HalfEn, HalfGen;
        }

        private sealed class PlanGroup
        {
            public int GenislikCm;
            public int EnCm;
            public int Carpan;
            public bool UstKnown;
            public double UstKotCm;
            public double UstKalinlikCm;
            public bool AltKnown;
            public double AltKotCm;
            public double AltKalinlikCm;
            public bool HatilKnown;
            public double HatilCm;
            public double SortX, SortY;
            public List<PitMark> Marks;
        }
    }
}
