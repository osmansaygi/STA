using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Gömülü DEMIR_TABLOSU.dwg. Aynı çap + aynı L boyu bir poz.
    /// Sol üst, sağ üst, solun altı… sırasıyla dolar.
    /// TOPLAM kg = adet × boy(m) × kg/m. kg/m tablosu çapa göredir.
    /// </summary>
    internal static class DonatiMetrajRunner
    {
        private const string EmbeddedResourceName = "ST4Yardimci.DemirTablosu.DEMIR_TABLOSU.dwg";
        private const string HasirMetrajResourceName = "ST4Yardimci.HasirMetraj.HASIRMETRAJ.dwg";
        /// <summary>Hasır tablosu, demir tablosunun alt kenarından bu kadar aşağıda (cm).</summary>
        private const double HasirTabloGapCm = 25.0;
        private const int PozSatir = 16;
        private const int OzetHazirSatir = 3;
        private const double Satir = 20.0;
        private const double PozUst = 1585.606;
        private const double PozAlt = 1265.606;
        private const double OzetUst = 1230.606;

        // Şablon koordinatları (DEMIR_TABLOSU.dwg, yerleştirmeden önce).
        private const double PozY0 = 1575.606;
        private const double OzetY0 = 1220.606;
        private const double OzetAlt = 1170.606;
        private const double SolX = 1839.321;
        private const double SagX = 2409.321;
        private const double PozSol1 = 2114.321;
        private const double PozSag0 = 2134.321;

        private static readonly double[] SolSutun = { 1859.321, 1899.321, 1939.321, 1998.071, 2076.821 };
        private static readonly double[] SagSutun = { 2154.321, 2194.321, 2234.321, 2293.071, 2371.821 };
        private static readonly double[] OzetSutun = { 1879.321, 1978.071, 2125.571, 2311.821 };
        private const double AltToplamX = 2266.821;
        private const double InceY = 1145.606;
        private const double KalinY = 1125.606;
        private const double GenelY = 1105.606;
        private const string LayerPozNo = "POZ NO (BEYKENT)";
        private const string LayerPozBalon = "POZ BALON (BEYKENT)";
        private const string LayerPozTarama = "POZ TARAMA (BEYKENT)";
        /// <summary>Kiriş açılım L= satırları SheetView altına taşabilir (cm).</summary>
        private const double AntetAcilimPadBelowCm = 320.0;
        private const double AntetAcilimPadXCm = 80.0;
        private const double AntetAcilimPadAboveCm = 40.0;

        /// <summary>kg/m. Liste: 8…40 mm (6, 34, 38 yok).</summary>
        private static readonly Dictionary<int, double> KgPerM = new Dictionary<int, double>
        {
            { 8, 0.395 }, { 10, 0.617 }, { 12, 0.888 }, { 14, 1.208 },
            { 16, 1.578 }, { 18, 1.998 }, { 20, 2.466 }, { 22, 2.984 },
            { 24, 3.551 }, { 25, 3.853 }, { 26, 4.168 }, { 28, 4.834 },
            { 30, 5.549 }, { 32, 6.313 }, { 36, 7.990 }, { 40, 9.865 }
        };

        /// <summary>Çap (mm) → pastel dolgu. Ardışık çaplar ayrı renk ailesinde.</summary>
        private static readonly Dictionary<int, int> PastelRgb = new Dictionary<int, int>
        {
            { 8, 0x7EC4F0 }, { 10, 0xF4A0B4 }, { 12, 0x8ED48A }, { 14, 0xC8A4F0 },
            { 16, 0xF6C07A }, { 18, 0x7EDDD4 }, { 20, 0xA4AEF4 }, { 22, 0xF49AC4 },
            { 24, 0xD4E070 }, { 25, 0xC4846A }, { 26, 0x6ECFB8 }, { 28, 0xE0A0D4 },
            { 30, 0xA8C878 }, { 32, 0xF0A090 }, { 36, 0x6A8EC4 }, { 40, 0xD4A070 }
        };

        private static readonly object TempLock = new object();
        private static string _tempDwg;

        private sealed class Poz
        {
            public int No;
            public int Cap;
            public int BoyCm;
            public int Adet;
            public int KatCarpan;
            public double Kg;
        }

        private sealed class Hit
        {
            public ObjectId Id;
            public int Cap;
            public int Boy;
            public int Adet;
            public double RawCm;
        }

        private sealed class HasirHit
        {
            public ObjectId Id;
            public string Tip;
            public int Kat;
            public int Wcm;
            public int Hcm;
        }

        private sealed class HasirPoz
        {
            public int No;
            public string Tip;
            public int Wcm;
            public int Hcm;
            public int Adet;
            public double KgM2;
            public double Kg;
        }

        private struct BalPt
        {
            public double X, Y, Bulge;
        }

        private static List<BalPt> _balLocal;

        public static void RunInteractive()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            DonatiMetrajSettings.EnsureLoaded();

            if (DonatiMetrajSettings.Katmanlar.Count == 0)
            {
                ed.WriteMessage("\nDONATIMETRAJ: Donatı etiketi katmanı ekleyin.");
                return;
            }

            var peo = new PromptSelectionOptions
            {
                MessageForAdding = "\nMetraja girecek donatı etiketlerini seçin: ",
                AllowDuplicates = false,
                RejectObjectsOnLockedLayers = false
            };
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "TEXT"),
                new TypedValue((int)DxfCode.Start, "MTEXT"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            });
            PromptSelectionResult psr = ed.GetSelection(peo, filter);
            if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
            {
                ed.WriteMessage("\nDONATIMETRAJ: Seçim yok.");
                return;
            }

            var groups = new Dictionary<long, Poz>();
            var hits = new List<Hit>();
            var hasirHits = new List<HasirHit>();
            int skipped = 0, labels = 0, over = 0;
            double maxBoy = DonatiMetrajSettings.MaxBoyCm;
            if (maxBoy < 1) maxBoy = 1200;
            var overLens = new List<int>();
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null || ent.IsErased) continue;
                    string raw = ent is DBText tx ? (tx.TextString ?? "") : (ent is MText mx ? (mx.Contents ?? "") : null);
                    if (raw == null) continue;
                    if (TryHasirLabel(StripMText(raw), out HasirHit hh))
                    {
                        hh.Id = so.ObjectId;
                        hasirHits.Add(hh);
                        continue;
                    }
                    if (!LayerOk(ent.Layer)) continue;
                    if (!TryParse(StripMText(raw), out int adet, out int cap, out int boy, out double rawCm))
                    {
                        skipped++;
                        continue;
                    }
                    labels++;
                    if (rawCm > maxBoy + 1e-6)
                    {
                        over++;
                        int shown = (int)Math.Round(rawCm, MidpointRounding.AwayFromZero);
                        if (overLens.Count < 8 && !overLens.Contains(shown))
                            overLens.Add(shown);
                    }
                    hits.Add(new Hit { Id = so.ObjectId, Cap = cap, Boy = boy, RawCm = rawCm });
                    long key = cap * 100000L + boy;
                    if (!groups.TryGetValue(key, out Poz p))
                    {
                        p = new Poz { Cap = cap, BoyCm = boy };
                        groups[key] = p;
                    }
                    p.Adet += adet;
                }
                tr.Commit();
            }

            if (over > 0)
            {
                ed.WriteMessage(
                    "\nDONATIMETRAJ: Uyarı — {0} etiketin L boyu {1:0.##} cm üstünde ({2}).",
                    over, maxBoy, string.Join(", ", overLens));
            }

            List<HasirPoz> hasirPoz = BuildHasirPozlar(hasirHits);
            if (groups.Count == 0 && hasirPoz.Count == 0)
            {
                ed.WriteMessage("\nDONATIMETRAJ: Seçimde donatı veya hasır etiketi yok.");
                return;
            }

            var pozlar = new List<Poz>(groups.Values);
            pozlar.Sort((a, b) =>
            {
                int c = a.Cap.CompareTo(b.Cap);
                return c != 0 ? c : a.BoyCm.CompareTo(b.BoyCm);
            });
            for (int i = 0; i < pozlar.Count; i++)
            {
                Poz p = pozlar[i];
                p.No = i + 1;
                p.Kg = Math.Round(p.Adet * p.BoyCm / 100.0 * BirimKg(p.Cap), 2, MidpointRounding.AwayFromZero);
            }
            if (pozlar.Count > 0)
                ContinueHasirPoz(hasirPoz, pozlar.Count);

            var ppo = new PromptPointOptions(groups.Count > 0
                ? "\nDemir tablosu yerleştirme noktası: "
                : "\nHasır çelik metraj tablosu yerleştirme noktası: ")
            {
                AllowNone = false
            };
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nDONATIMETRAJ: Yerleştirme iptal.");
                return;
            }

            bool hasHasir = hasirPoz.Count > 0;
            string path = groups.Count > 0 ? EnsureTempDwg() : null;
            string hasirPath = hasHasir ? EnsureHasirMetrajDwg() : null;
            if (groups.Count > 0 && string.IsNullOrEmpty(path))
            {
                ed.WriteMessage("\nDONATIMETRAJ: Gömülü demir tablosu bulunamadı.");
                return;
            }
            if (hasHasir && string.IsNullOrEmpty(hasirPath))
            {
                ed.WriteMessage("\nDONATIMETRAJ: Gömülü hasır metraj tablosu bulunamadı.");
                if (groups.Count == 0) return;
                hasHasir = false;
            }

            try
            {
                double ince = 0, kalin = 0, hasirKg = 0;
                int n = 0;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    if (groups.Count > 0)
                    {
                        ClearOldMarks(tr, doc.Database, hits);
                        n = InsertFilled(tr, doc.Database, path, ppr.Value, pozlar, out ince, out kalin, false, out _);
                        StampLabels(tr, doc.Database, hits, pozlar);
                        if (hasHasir)
                        {
                            var under = new Point3d(ppr.Value.X, ppr.Value.Y - HasirTabloGapCm, ppr.Value.Z);
                            InsertHasir(tr, doc.Database, hasirPath, under, true, hasirPoz, out hasirKg);
                            ClearOldMarks(tr, doc.Database, HasirAsHits(hasirHits));
                            StampHasirPoz(tr, doc.Database, hasirHits, hasirPoz);
                        }
                    }
                    else if (hasHasir)
                    {
                        InsertHasir(tr, doc.Database, hasirPath, ppr.Value, false, hasirPoz, out hasirKg);
                        ClearOldMarks(tr, doc.Database, HasirAsHits(hasirHits));
                        StampHasirPoz(tr, doc.Database, hasirHits, hasirPoz);
                    }
                    tr.Commit();
                }
                if (groups.Count > 0)
                    ed.WriteMessage(
                        "\nDONATIMETRAJ: {0} etiket → {1} poz. İnce {2} kg, kalın {3} kg, toplam {4} kg.{5}",
                        labels, pozlar.Count, F2(ince), F2(kalin), F2(ince + kalin),
                        hasHasir ? " Hasır " + F2(hasirKg) + " kg, tablo 25 cm altta." : "");
                else if (hasHasir)
                    ed.WriteMessage(
                        "\nDONATIMETRAJ: {0} hasır etiketi → {1} poz, {2} kg.",
                        hasirHits.Count, hasirPoz.Count, F2(hasirKg));
                if (skipped > 0)
                    ed.WriteMessage("\nDONATIMETRAJ: {0} yazı etiket sayılmadı.", skipped);
                if (DonatiMetrajSettings.OnizlemeMesaji)
                {
                    foreach (Poz p in pozlar)
                        ed.WriteMessage("\n  Poz {0}  {1} x {2}{3}  L={4}  {5} kg",
                            p.No, p.Adet, Fi(), p.Cap, p.BoyCm, F2(p.Kg));
                }
                if (groups.Count > 0 && n == 0)
                    ed.WriteMessage("\nDONATIMETRAJ: Tabloda çizilecek nesne yok.");
            }
            catch (System.Exception ex)
            {
                try { ed.WriteMessage("\nDONATIMETRAJ hata: {0}", ex.Message); }
                catch { }
            }
        }

        /// <summary>
        /// Antet metraj — hafif C#: pencere seçimi + yalnız L= etiketleri, benzeştir, tablo.
        /// Tam model taraması / çizgi boyu / poz balonu yok (çoklu antette çökme ve yavaşlığın kaynağıydı).
        /// </summary>
        public static void RunByAntet()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            DonatiMetrajSettings.EnsureLoaded();
            if (DonatiMetrajSettings.Katmanlar.Count == 0)
            {
                ed.WriteMessage("\nDONATIMETRAJ: Donatı etiketi katmanı ekleyin.");
                return;
            }

            string path = EnsureTempDwg();
            double maxBoy = DonatiMetrajSettings.MaxBoyCm;
            if (maxBoy < 1) maxBoy = 1200;

            var peo = new PromptSelectionOptions
            {
                MessageForAdding = "\nSheetView antet çerçevelerini seçin (SheetViewOut değil): ",
                MessageForRemoval = "\nSeçimden çıkarın: ",
                AllowDuplicates = false,
                RejectObjectsOnLockedLayers = false
            };
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Operator, "<AND"),
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "POLYLINE"),
                new TypedValue((int)DxfCode.Operator, "OR>"),
                new TypedValue((int)DxfCode.LayerName, "*SheetView*"),
                new TypedValue((int)DxfCode.Operator, "AND>")
            });
            PromptSelectionResult psr = ed.GetSelection(peo, filter);
            if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
            {
                ed.WriteMessage("\nDONATIMETRAJ: Antet seçimi yok.");
                return;
            }

            var frames = new List<AntetFrame>();
            int ignoredBad = 0, ignoredOut = 0;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    var pl = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Polyline;
                    if (pl == null || pl.IsErased || pl.NumberOfVertices < 3)
                    {
                        ignoredBad++;
                        continue;
                    }
                    if (IsSheetViewOutLayer(pl.Layer))
                    {
                        ignoredOut++;
                        continue;
                    }
                    if (!IsSheetViewLayer(pl.Layer))
                    {
                        ignoredBad++;
                        continue;
                    }
                    var ring = new List<Point2d>();
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                        ring.Add(pl.GetPoint2dAt(i));
                    bool closed = pl.Closed;
                    if (!closed)
                    {
                        double dx = ring[0].X - ring[ring.Count - 1].X;
                        double dy = ring[0].Y - ring[ring.Count - 1].Y;
                        closed = dx * dx + dy * dy <= 4.0;
                    }
                    if (!closed)
                    {
                        ignoredBad++;
                        continue;
                    }
                    AntetFrame f = MakeFrame(ring);
                    if (f == null) ignoredBad++;
                    else frames.Add(f);
                }
                tr.Commit();
            }

            if (frames.Count == 0)
            {
                ed.WriteMessage("\nDONATIMETRAJ: Seçilenler SheetView kapalı poligon değil.");
                return;
            }

            // Transaction dışında pencere seçimi — LISP gibi yalnız o bölgedeki TEXT/MTEXT.
            SelectionFilter textFilter = AntetTextFilter();
            var usedIds = new HashSet<ObjectId>();
            int over = 0;
            var overLens = new List<int>();
            int totalL = 0;

            foreach (AntetFrame frame in frames)
            {
                var p1 = new Point3d(frame.MinX - AntetAcilimPadXCm, frame.MinY - AntetAcilimPadBelowCm, 0);
                var p2 = new Point3d(frame.MaxX + AntetAcilimPadXCm, frame.MaxY + AntetAcilimPadAboveCm, 0);
                PromptSelectionResult win;
                try { win = ed.SelectCrossingWindow(p1, p2, textFilter); }
                catch { continue; }
                if (win.Status != PromptStatus.OK || win.Value == null || win.Value.Count == 0)
                    continue;

                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject so in win.Value)
                    {
                        if (so == null) continue;
                        ObjectId id = so.ObjectId;
                        if (!id.IsValid || id.IsErased || !usedIds.Add(id)) continue;
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null || ent.IsErased) continue;
                        if (!LayerOk(ent.Layer)) continue;

                        string raw = ent is DBText tx ? (tx.TextString ?? "") : (ent is MText mx ? (mx.Contents ?? "") : null);
                        if (raw == null) continue;
                        string plain = StripMText(raw);

                        NoteKalipBenzer(frame, plain);
                        int kot = CountKesitKotBenzer(plain);
                        if (kot > frame.KotBenzer) frame.KotBenzer = kot;

                        if (TryHasirLabel(plain, out HasirHit hh))
                        {
                            hh.Id = id;
                            frame.HasirHits.Add(hh);
                            continue;
                        }

                        // Yalnız L= — görünüş (duz/mon) ve çizgi ölçümü yok.
                        if (!TryParse(plain, out int adet, out int cap, out int boy, out double rawCm))
                            continue;
                        if (rawCm > maxBoy + 1e-6)
                        {
                            over++;
                            int shown = (int)Math.Round(rawCm, MidpointRounding.AwayFromZero);
                            if (overLens.Count < 8 && !overLens.Contains(shown))
                                overLens.Add(shown);
                        }
                        frame.Hits.Add(new Hit
                        {
                            Id = id,
                            Cap = cap,
                            Boy = boy,
                            Adet = adet,
                            RawCm = rawCm
                        });
                        totalL++;
                    }
                    tr.Commit();
                }
            }

            string hasirPath = null;
            foreach (AntetFrame frame in frames)
            {
                if (frame.HasirHits.Count == 0) continue;
                hasirPath = EnsureHasirMetrajDwg();
                if (string.IsNullOrEmpty(hasirPath))
                    ed.WriteMessage("\nDONATIMETRAJ: Gömülü hasır metraj tablosu bulunamadı.");
                break;
            }

            int tables = 0, labels = 0, insertFail = 0, hitFrames = 0;
            for (int fi = 0; fi < frames.Count; fi++)
            {
                AntetFrame frame = frames[fi];
                if (frame.Hits.Count == 0 && frame.HasirHits.Count == 0) continue;
                hitFrames++;
                int kat = 1;
                if (frame.KotBenzer > 0)
                    kat = frame.KotBenzer + 1;
                else if (frame.KalipPlani && frame.BenzerAdet > 0)
                    kat = frame.BenzerAdet + 1;

                List<Poz> pozlar = frame.Hits.Count > 0 ? PozlarFromHits(frame.Hits, kat) : new List<Poz>();
                bool bars = pozlar.Count > 0;
                List<HasirPoz> hasirPoz = BuildHasirPozlar(frame.HasirHits);
                if (kat > 1)
                {
                    foreach (HasirPoz hp in hasirPoz)
                    {
                        hp.Adet *= kat;
                        double alan = hp.Adet * (hp.Wcm / 100.0) * (hp.Hcm / 100.0);
                        hp.Kg = Math.Round(alan * hp.KgM2, 2, MidpointRounding.AwayFromZero);
                    }
                }
                if (pozlar.Count > 0)
                    ContinueHasirPoz(hasirPoz, pozlar.Count);
                bool mesh = hasirPoz.Count > 0 && !string.IsNullOrEmpty(hasirPath);
                if (!bars && !mesh) continue;
                if (bars && string.IsNullOrEmpty(path))
                {
                    ed.WriteMessage("\nDONATIMETRAJ: Gömülü demir tablosu bulunamadı.");
                    bars = false;
                    if (!mesh) continue;
                }

                var anchor = new Point3d(frame.MaxX + 25.0, frame.MaxY, 0);
                double ince = 0, kalin = 0;
                try
                {
                    // Eski poz marklarını pencereyle sil (tam MS tarama yok).
                    ErasePozMarksInFrame(ed, doc, frame);

                    using (doc.LockDocument())
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        if (bars)
                        {
                            InsertFilled(tr, doc.Database, path, anchor, pozlar, out ince, out kalin, true, out double demirH);
                            StampLabelsFast(tr, doc.Database, frame.Hits, pozlar);
                            if (mesh)
                            {
                                var under = new Point3d(anchor.X, anchor.Y - demirH - HasirTabloGapCm, 0);
                                InsertHasir(tr, doc.Database, hasirPath, under, true, hasirPoz, out _);
                                StampHasirPozFast(tr, doc.Database, frame.HasirHits, hasirPoz);
                            }
                        }
                        else
                        {
                            InsertHasir(tr, doc.Database, hasirPath, anchor, true, hasirPoz, out _);
                            StampHasirPozFast(tr, doc.Database, frame.HasirHits, hasirPoz);
                        }
                        tr.Commit();
                    }
                }
                catch (System.Exception ex)
                {
                    insertFail++;
                    ed.WriteMessage("\nDONATIMETRAJ: Antet {0}/{1} atlandı ({2}).", fi + 1, frames.Count, ex.Message);
                    continue;
                }

                tables++;
                labels += frame.Hits.Count;
                if (!bars)
                    ed.WriteMessage("\nDONATIMETRAJ: Antet {0} — yalnız hasır.", tables);
                else if (kat > 1)
                    ed.WriteMessage(
                        "\nDONATIMETRAJ: Antet {0} — {1} L= etiket → {2} poz. İnce {3} kg, kalın {4} kg. {5} kat.{6}",
                        tables, frame.Hits.Count, pozlar.Count, F2(ince), F2(kalin), kat,
                        mesh ? " +hasır" : "");
                else
                    ed.WriteMessage(
                        "\nDONATIMETRAJ: Antet {0} — {1} L= etiket → {2} poz. İnce {3} kg, kalın {4} kg.{5}",
                        tables, frame.Hits.Count, pozlar.Count, F2(ince), F2(kalin),
                        mesh ? " +hasır" : "");
            }

            if (over > 0)
            {
                ed.WriteMessage(
                    "\nDONATIMETRAJ: Uyarı — {0} etiketin L boyu {1:0.##} cm üstünde ({2}).",
                    over, maxBoy, string.Join(", ", overLens));
            }
            if (tables == 0 && insertFail > 0)
                ed.WriteMessage("\nDONATIMETRAJ: {0} antette L= vardı; tablo yazılamadı.", hitFrames);
            else if (tables == 0)
                ed.WriteMessage(
                    "\nDONATIMETRAJ: L= etiket yok. Açılım satırları (…ø… L=…) formdaki DONATI YAZISI katmanında olmalı.");
            else
                ed.WriteMessage("\nDONATIMETRAJ: {0} antet, {1} L= etiket (toplam tarama {2}).", tables, labels, totalL);
            if (ignoredOut > 0)
                ed.WriteMessage("\nDONATIMETRAJ: {0} SheetViewOut atlandı.", ignoredOut);
            if (ignoredBad > 0)
                ed.WriteMessage("\nDONATIMETRAJ: {0} seçim kapalı SheetView değildi.", ignoredBad);
        }

        private static SelectionFilter AntetTextFilter()
        {
            return new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "TEXT"),
                new TypedValue((int)DxfCode.Start, "MTEXT"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            });
        }

        private sealed class AntetFrame
        {
            public List<Point2d> Ring = new List<Point2d>();
            public double MinX, MinY, MaxX, MaxY, Area;
            public List<Hit> Hits = new List<Hit>();
            public List<HasirHit> HasirHits = new List<HasirHit>();
            public bool KalipPlani;
            public int BenzerAdet;
            public int KotBenzer;
        }

        private static bool IsSheetViewOutLayer(string layer)
        {
            return !string.IsNullOrWhiteSpace(layer) &&
                   layer.IndexOf("SheetViewOut", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSheetViewLayer(string layer)
        {
            if (string.IsNullOrWhiteSpace(layer)) return false;
            if (IsSheetViewOutLayer(layer)) return false;
            return layer.IndexOf("SheetView", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>L= veya çap işareti içeren yazılar; ölçü/başlık gürültüsünü ayırır.</summary>
        private static bool LooksLikeDonatiLabel(string plain)
        {
            if (string.IsNullOrWhiteSpace(plain)) return false;
            if (Regex.IsMatch(plain, @"[Ll]\s*=")) return true;
            if (plain.IndexOf('ø') >= 0 || plain.IndexOf('Ø') >= 0 || plain.IndexOf('⌀') >= 0) return true;
            if (plain.IndexOf("%%c", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (plain.IndexOf("%%C", StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static AntetFrame MakeFrame(List<Point2d> ring)
        {
            if (ring == null || ring.Count < 3) return null;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < ring.Count; i++)
            {
                if (ring[i].X < minX) minX = ring[i].X;
                if (ring[i].Y < minY) minY = ring[i].Y;
                if (ring[i].X > maxX) maxX = ring[i].X;
                if (ring[i].Y > maxY) maxY = ring[i].Y;
            }
            if (maxX - minX < 20 || maxY - minY < 20) return null;
            double area = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                Point2d a = ring[i];
                Point2d b = ring[(i + 1) % ring.Count];
                area += a.X * b.Y - b.X * a.Y;
            }
            area = Math.Abs(area) * 0.5;
            if (area < 100) return null;
            return new AntetFrame
            {
                Ring = ring,
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY,
                Area = area
            };
        }

        private static AntetFrame SmallestFrame(List<AntetFrame> frames, double x, double y)
        {
            AntetFrame best = null;
            double bestA = double.MaxValue;
            foreach (AntetFrame f in frames)
            {
                if (x < f.MinX || x > f.MaxX || y < f.MinY || y > f.MaxY) continue;
                if (!PointInRing(f.Ring, x, y)) continue;
                if (f.Area < bestA)
                {
                    best = f;
                    bestA = f.Area;
                }
            }
            return best;
        }

        /// <summary>
        /// Önce kapalı SheetView halkası; yoksa AABB + açılım payı (L= satırları çerçeve altında kalabilir).
        /// </summary>
        private static AntetFrame HostFrame(List<AntetFrame> frames, double x, double y)
        {
            AntetFrame inside = SmallestFrame(frames, x, y);
            if (inside != null) return inside;
            AntetFrame best = null;
            double bestA = double.MaxValue;
            foreach (AntetFrame f in frames)
            {
                if (x < f.MinX - AntetAcilimPadXCm || x > f.MaxX + AntetAcilimPadXCm) continue;
                if (y < f.MinY - AntetAcilimPadBelowCm || y > f.MaxY + AntetAcilimPadAboveCm) continue;
                if (f.Area < bestA)
                {
                    best = f;
                    bestA = f.Area;
                }
            }
            return best;
        }

        /// <summary>L= yoksa adet+çap (adet yoksa 1).</summary>
        private static bool TryParseAdetCap(string plain, out int adet, out int cap)
        {
            adet = 0;
            cap = 0;
            if (string.IsNullOrWhiteSpace(plain)) return false;
            plain = NormalizeDonatiPlain(plain);
            string rx = @"(?:(?<adet>\d+(?:\s*[xX×*]\s*\d+)*)\s*)?(?:" + PhiAlt() + @")\s*(?<cap>\d{1,2})"
                        + @"(?:\s*/\s*\d+(?:[.,]\d+)?)?";
            Match m = Regex.Match(plain, rx);
            if (!m.Success) return false;
            string adetRaw = m.Groups["adet"].Value;
            if (string.IsNullOrWhiteSpace(adetRaw))
                adet = 1;
            else if (!TryAdetProduct(adetRaw, out adet))
                return false;
            if (!int.TryParse(m.Groups["cap"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out cap) || cap < 1)
                return false;
            return true;
        }

        private static bool PointInRing(List<Point2d> ring, double x, double y)
        {
            bool inside = false;
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double yi = ring[i].Y, yj = ring[j].Y;
                double xi = ring[i].X, xj = ring[j].X;
                if (((yi > y) != (yj > y)) &&
                    (x < (xj - xi) * (y - yi) / ((yj - yi) + 1e-12) + xi))
                    inside = !inside;
            }
            return inside;
        }

        private static bool TryTextCenter(Entity ent, out double x, out double y)
        {
            x = y = 0;
            try
            {
                Extents3d e = ent.GeometricExtents;
                x = 0.5 * (e.MinPoint.X + e.MaxPoint.X);
                y = 0.5 * (e.MinPoint.Y + e.MaxPoint.Y);
                return true;
            }
            catch { return false; }
        }

        /// <summary>GeometricExtents açmadan yazı konumu (büyük DWG tarama için).</summary>
        private static bool TryTextAnchor(Entity ent, out double x, out double y)
        {
            x = y = 0;
            if (ent is DBText tx)
            {
                Point3d p = tx.Position;
                if (tx.Justify != AttachmentPoint.BaseLeft &&
                    tx.Justify != AttachmentPoint.BaseAlign &&
                    tx.Justify != AttachmentPoint.BaseFit)
                {
                    try
                    {
                        Point3d a = tx.AlignmentPoint;
                        if (!(a.X == 0 && a.Y == 0) || (p.X == 0 && p.Y == 0))
                            p = a;
                    }
                    catch { }
                }
                x = p.X;
                y = p.Y;
                return true;
            }
            if (ent is MText mx)
            {
                x = mx.Location.X;
                y = mx.Location.Y;
                return true;
            }
            return false;
        }

        private static AntetFrame FrameByInsertion(List<AntetFrame> frames, Entity ent)
        {
            if (!TryTextAnchor(ent, out double x, out double y)) return null;
            return SmallestFrame(frames, x, y);
        }

        private static AntetFrame NearestFrame(List<AntetFrame> frames, double x, double y, double margin)
        {
            AntetFrame best = null;
            double bestA = double.MaxValue;
            foreach (AntetFrame f in frames)
            {
                if (x < f.MinX - margin || x > f.MaxX + margin || y < f.MinY - margin || y > f.MaxY + margin)
                    continue;
                if (f.Area < bestA)
                {
                    best = f;
                    bestA = f.Area;
                }
            }
            return best;
        }

        private static void NoteKalipBenzer(AntetFrame host, string plain)
        {
            if (host == null || string.IsNullOrWhiteSpace(plain)) return;
            string f = FoldTr(plain);
            if (f.IndexOf("KALIP", StringComparison.Ordinal) >= 0 &&
                f.IndexOf("PLAN", StringComparison.Ordinal) >= 0)
                host.KalipPlani = true;
            int kot = CountKesitKotBenzer(plain);
            if (kot > host.KotBenzer) host.KotBenzer = kot;
            int n = CountBenzerKatlar(f);
            if (n > host.BenzerAdet) host.BenzerAdet = n;
        }

        /// <summary>Kesit kotu: +6.42 (+9.42, +12.42, …) — parantezdeki adet benzer kat.</summary>
        private static int CountKesitKotBenzer(string plain)
        {
            if (string.IsNullOrWhiteSpace(plain)) return 0;
            string s = plain.Replace('\u00A0', ' ').Replace('\u2009', ' ').Replace('\u202F', ' ');
            s = s.Replace('（', '(').Replace('）', ')').Replace('，', ',').Replace('、', ',');
            s = s.Replace('\uFF0B', '+').Replace('\u2212', '-').Replace('\u2013', '-');
            MatchCollection groups = Regex.Matches(
                s,
                @"[+\-\u00B1]?\d+[.,]\d{1,3}\s*\(\s*([+\-\u00B1]?\d+[.,]\d{1,3}(?:\s*,\s*[+\-\u00B1]?\d+[.,]\d{1,3})*)\s*\)");
            int best = 0;
            foreach (Match m in groups)
            {
                int c = Regex.Matches(m.Groups[1].Value, @"[+\-\u00B1]?\d+[.,]\d{1,3}").Count;
                if (c > best) best = c;
            }
            return best;
        }

        private static int CountBenzerKatlar(string folded)
        {
            Match m = Regex.Match(folded, @"BENZER\s+KATLAR?\s*:?\s*(.+)");
            if (!m.Success) return 0;
            string rest = m.Groups[1].Value;
            int cut = rest.IndexOf(')');
            if (cut >= 0) rest = rest.Substring(0, cut);
            string[] parts = Regex.Split(rest, @"[,;]+");
            if (parts.Length == 1 &&
                int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int only) &&
                only > 0)
                return only;
            int n = 0;
            foreach (string p in parts)
            {
                string t = p.Trim();
                if (t.Length == 0) continue;
                if (Regex.IsMatch(t, @"\d")) n++;
            }
            return n;
        }

        private static string FoldTr(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string u;
            try { u = s.ToUpper(CultureInfo.GetCultureInfo("tr-TR")); }
            catch { u = s.ToUpperInvariant(); }
            return u.Replace('İ', 'I').Replace('Ş', 'S').Replace('Ğ', 'G')
                .Replace('Ü', 'U').Replace('Ö', 'O').Replace('Ç', 'C');
        }

        private static List<Poz> PozlarFromHits(List<Hit> hits, int katCarpan = 1)
        {
            var groups = new Dictionary<long, Poz>();
            foreach (Hit h in hits)
            {
                long key = h.Cap * 100000L + h.Boy;
                if (!groups.TryGetValue(key, out Poz p))
                {
                    p = new Poz { Cap = h.Cap, BoyCm = h.Boy };
                    groups[key] = p;
                }
                p.Adet += h.Adet;
            }
            var pozlar = new List<Poz>(groups.Values);
            pozlar.Sort((a, b) =>
            {
                int c = a.Cap.CompareTo(b.Cap);
                return c != 0 ? c : a.BoyCm.CompareTo(b.BoyCm);
            });
            for (int i = 0; i < pozlar.Count; i++)
            {
                Poz p = pozlar[i];
                p.No = i + 1;
                p.KatCarpan = katCarpan;
                int eff = p.Adet * katCarpan;
                p.Kg = Math.Round(eff * p.BoyCm / 100.0 * BirimKg(p.Cap), 2, MidpointRounding.AwayFromZero);
            }
            return pozlar;
        }

        public static void RunToplam()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            var peo = new PromptSelectionOptions
            {
                MessageForAdding = "\nToplanacak metraj tablolarını seçin: ",
                MessageForRemoval = "\nSeçimden çıkarın: ",
                AllowDuplicates = false,
                RejectObjectsOnLockedLayers = false
            };
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "TEXT"),
                new TypedValue((int)DxfCode.Start, "MTEXT"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            });
            PromptSelectionResult psr = ed.GetSelection(peo, filter);
            if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
            {
                ed.WriteMessage("\nDONATIMETRAJ: Seçim yok.");
                return;
            }

            var notes = new List<TxtN>();
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null || ent.IsErased) continue;
                    string s = null;
                    double x = 0, y = 0;
                    if (ent is DBText tx)
                    {
                        s = tx.TextString ?? "";
                        Point3d a = Anchor(tx);
                        x = a.X;
                        y = a.Y;
                    }
                    else if (ent is MText mx)
                    {
                        s = StripMText(mx.Contents ?? "");
                        x = mx.Location.X;
                        y = mx.Location.Y;
                    }
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    notes.Add(new TxtN { S = s.Trim(), X = x, Y = y });
                }
                tr.Commit();
            }

            var byCap = SumSummaryRows(notes);
            if (byCap.Count == 0)
            {
                ed.WriteMessage("\nDONATIMETRAJ: Seçimde çap özeti (ø ve kg/m) bulunamadı.");
                return;
            }
            bool hasHasirToplam = TrySumHasirFooters(notes, out double hasirToplam);

            var ppo = new PromptPointOptions("\nToplam metraj sol üst noktası: ") { AllowNone = false };
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nDONATIMETRAJ: Yerleştirme iptal.");
                return;
            }

            string path = EnsureTempDwg();
            if (string.IsNullOrEmpty(path))
            {
                ed.WriteMessage("\nDONATIMETRAJ: Gömülü demir tablosu bulunamadı.");
                return;
            }

            try
            {
                double ince, kalin;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var place = new Point3d(ppr.Value.X + 25.0, ppr.Value.Y - 25.0, ppr.Value.Z);
                    InsertFilled(tr, doc.Database, path, place, null, out ince, out kalin, true, out _, byCap,
                        hasHasirToplam ? (double?)hasirToplam : null);
                    tr.Commit();
                }
                ed.WriteMessage(
                    "\nDONATIMETRAJ: {0} çap toplandı. İnce {1} kg, kalın {2} kg, toplam {3} kg.{4}",
                    byCap.Count, F2(ince), F2(kalin), F2(ince + kalin),
                    hasHasirToplam ? " Hasır " + F2(hasirToplam) + " kg, tablo 25 cm altta." : "");
            }
            catch (System.Exception ex)
            {
                try { ed.WriteMessage("\nDONATIMETRAJ hata: {0}", ex.Message); }
                catch { }
            }
        }

        private struct TxtN
        {
            public string S;
            public double X, Y;
        }

        private static List<CapOzet> SumSummaryRows(List<TxtN> notes)
        {
            var map = new Dictionary<int, CapOzet>();
            foreach (TxtN n in notes)
            {
                if (!TryCapToken(n.S, out int cap)) continue;
                var right = new List<TxtN>();
                foreach (TxtN o in notes)
                {
                    if (Math.Abs(o.Y - n.Y) > 8) continue;
                    if (o.X < n.X + 2) continue;
                    if (!TryPlainNum(o.S, out _)) continue;
                    right.Add(o);
                }
                right.Sort((a, b) => a.X.CompareTo(b.X));
                if (right.Count < 3) continue;
                if (!TryPlainNum(right[0].S, out double birim)) continue;
                if (Math.Abs(birim - BirimKg(cap)) > 0.03) continue;
                if (!TryPlainNum(right[1].S, out double boy)) continue;
                if (!TryPlainNum(right[2].S, out double kg)) continue;
                if (!map.TryGetValue(cap, out CapOzet c))
                {
                    c = new CapOzet { Cap = cap };
                    map[cap] = c;
                }
                c.BoyM = Math.Round(c.BoyM + boy, 2, MidpointRounding.AwayFromZero);
                c.Kg = Math.Round(c.Kg + kg, 2, MidpointRounding.AwayFromZero);
            }
            var list = new List<CapOzet>(map.Values);
            list.Sort((a, b) => a.Cap.CompareTo(b.Cap));
            return list;
        }

        private static bool TryCapToken(string s, out int cap)
        {
            cap = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            Match m = Regex.Match(s.Trim(), @"^(?:ø|Ø|⌀|%%[Cc])\s*(\d{1,2})$");
            if (!m.Success)
            {
                string fi = (Fi() ?? "").Trim();
                if (fi.Length > 0)
                    m = Regex.Match(s.Trim(), "^" + Regex.Escape(fi) + @"\s*(\d{1,2})$");
            }
            if (!m.Success) return false;
            return int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out cap) && cap > 0;
        }

        /// <summary>Seçili hasır tablolarındaki HASIR DEMIR TOPLAM satırlarını toplar.</summary>
        private static bool TrySumHasirFooters(List<TxtN> notes, out double kg)
        {
            kg = 0;
            bool any = false;
            if (notes == null) return false;
            foreach (TxtN n in notes)
            {
                string s = (n.S ?? "").ToUpperInvariant();
                if (s.IndexOf("HASIR DEMIR TOPLAM", StringComparison.Ordinal) < 0 &&
                    s.IndexOf("HASIR DEMİR TOPLAM", StringComparison.Ordinal) < 0 &&
                    s.IndexOf("TABLO TOPLAM", StringComparison.Ordinal) < 0)
                    continue;
                double bestX = double.MaxValue;
                double val = 0;
                bool found = false;
                foreach (TxtN o in notes)
                {
                    if (Math.Abs(o.Y - n.Y) > 8) continue;
                    if (o.X <= n.X + 2) continue;
                    if (!TryPlainNum(o.S, out double v)) continue;
                    if (o.X < bestX)
                    {
                        bestX = o.X;
                        val = v;
                        found = true;
                    }
                }
                if (!found) continue;
                kg += val;
                any = true;
            }
            kg = Math.Round(kg, 2, MidpointRounding.AwayFromZero);
            return any;
        }

        private static bool TryPlainNum(string s, out double v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            string t = s.Trim().Replace(',', '.');
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private static int InsertFilled(Transaction tr, Database destDb, string path, Point3d pick, List<Poz> pozlar, out double ince, out double kalin, bool topLeft, out double height, List<CapOzet> totals = null, double? hasirToplam = null)
        {
            ince = 0;
            kalin = 0;
            height = 0;
            // Yan DWG'de new DBText/Line için WorkingDatabase=src zorunlu; WblockClone öncesi geri alınır.
            using (var src = new Database(false, true))
            {
                src.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
                var ids = new ObjectIdCollection();
                Database prev = HostApplicationServices.WorkingDatabase;
                try
                {
                    HostApplicationServices.WorkingDatabase = src;
                    using (var trs = src.TransactionManager.StartTransaction())
                    {
                        var btSrc = (BlockTable)trs.GetObject(src.BlockTableId, OpenMode.ForRead);
                        var ms = (BlockTableRecord)trs.GetObject(btSrc[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        if (totals != null)
                            FillTotals(trs, src, ms, totals, out ince, out kalin, hasirToplam);
                        else
                            Fill(trs, src, ms, pozlar, out ince, out kalin);
                        CollectEntities(trs, btSrc, BlockTableRecord.ModelSpace, ids);
                        trs.Commit();
                    }
                }
                finally
                {
                    try { HostApplicationServices.WorkingDatabase = prev; } catch { }
                }
                if (ids.Count == 0)
                    return 0;

                var bt = (BlockTable)tr.GetObject(destDb.BlockTableId, OpenMode.ForRead);
                var msDest = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                return PlaceCloned(tr, destDb, msDest, ids, pick, topLeft, out height);
            }
        }

        /// <summary>Gömülü tabloyu doldurmadan yerleştirir. topLeft: pick = sol üst.</summary>
        private static int InsertPlain(Transaction tr, Database destDb, string path, Point3d pick, bool topLeft)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            using (var src = new Database(false, true))
            {
                src.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
                var ids = new ObjectIdCollection();
                Database prev = HostApplicationServices.WorkingDatabase;
                try
                {
                    HostApplicationServices.WorkingDatabase = src;
                    using (var trs = src.TransactionManager.StartTransaction())
                    {
                        var btSrc = (BlockTable)trs.GetObject(src.BlockTableId, OpenMode.ForRead);
                        CollectEntities(trs, btSrc, BlockTableRecord.ModelSpace, ids);
                        trs.Commit();
                    }
                }
                finally
                {
                    try { HostApplicationServices.WorkingDatabase = prev; } catch { }
                }
                if (ids.Count == 0) return 0;
                var bt = (BlockTable)tr.GetObject(destDb.BlockTableId, OpenMode.ForRead);
                var msDest = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                return PlaceCloned(tr, destDb, msDest, ids, pick, topLeft, out _);
            }
        }

        private static int PlaceCloned(Transaction tr, Database destDb, BlockTableRecord msDest, ObjectIdCollection ids, Point3d pick, bool topLeft, out double height)
        {
            height = 0;
            var mapping = new IdMapping();
            destDb.WblockCloneObjects(ids, msDest.ObjectId, mapping, DuplicateRecordCloning.Ignore, false);

            var cloned = new List<Entity>();
            double minX = double.MaxValue, minY = double.MaxValue, maxY = double.MinValue;
            bool any = false;
            foreach (IdPair pair in mapping)
            {
                if (!pair.IsCloned || !pair.Value.IsValid) continue;
                if (!(tr.GetObject(pair.Value, OpenMode.ForWrite, false) is Entity ent)) continue;
                if (ent.IsErased || ent.OwnerId != msDest.ObjectId) continue;
                cloned.Add(ent);
                try
                {
                    Extents3d e = ent.GeometricExtents;
                    if (e.MinPoint.X < minX) minX = e.MinPoint.X;
                    if (e.MinPoint.Y < minY) minY = e.MinPoint.Y;
                    if (e.MaxPoint.Y > maxY) maxY = e.MaxPoint.Y;
                    any = true;
                }
                catch { }
            }

            if (any && cloned.Count > 0)
            {
                height = maxY - minY;
                double dy = topLeft ? pick.Y - maxY : pick.Y - minY;
                var move = Matrix3d.Displacement(new Vector3d(pick.X - minX, dy, 0));
                foreach (Entity ent in cloned)
                {
                    try { ent.TransformBy(move); } catch { }
                }
            }
            return cloned.Count;
        }

        /// <summary>Q188/188 500x215 veya 2xQ188/188 500x215. Ebat cm, alan m2.</summary>
        private static bool TryHasirLabel(string plain, out HasirHit hit)
        {
            hit = null;
            if (string.IsNullOrWhiteSpace(plain)) return false;
            string s = plain.Replace('\r', ' ').Replace('\n', ' ').Replace('\u00D7', 'x').Replace('\u0445', 'x').Trim();
            Match m = Regex.Match(s,
                @"(?:(?<kat>\d+)\s*[xX*]\s*)?(?<tip>Q\s*\d{2,4}\s*/\s*\d{2,4}).*?(?<w>\d+(?:[.,]\d+)?)\s*[xX*]\s*(?<h>\d+(?:[.,]\d+)?)",
                RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            if (!TryCm(m.Groups["w"].Value, out int w) || !TryCm(m.Groups["h"].Value, out int h))
                return false;
            if (w < 5 || h < 5) return false;
            int kat = 1;
            if (m.Groups["kat"].Success &&
                int.TryParse(m.Groups["kat"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int k) &&
                k > 0)
                kat = k;
            string tip = Regex.Replace(m.Groups["tip"].Value, @"\s+", "");
            if (tip.Length > 0) tip = "Q" + tip.Substring(1);
            hit = new HasirHit { Tip = tip, Kat = kat, Wcm = w, Hcm = h };
            return true;
        }

        private static bool TryCm(string raw, out int cm)
        {
            cm = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            if (!double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return false;
            cm = (int)Math.Round(v, MidpointRounding.AwayFromZero);
            return cm > 0;
        }

        private static List<HasirPoz> BuildHasirPozlar(List<HasirHit> hits)
        {
            var map = new Dictionary<string, HasirPoz>(StringComparer.OrdinalIgnoreCase);
            if (hits != null)
            {
                foreach (HasirHit h in hits)
                {
                    if (h == null || string.IsNullOrEmpty(h.Tip)) continue;
                    string key = h.Tip + "|" + h.Wcm.ToString(CultureInfo.InvariantCulture) + "|" + h.Hcm.ToString(CultureInfo.InvariantCulture);
                    if (!map.TryGetValue(key, out HasirPoz p))
                    {
                        p = new HasirPoz
                        {
                            Tip = h.Tip,
                            Wcm = h.Wcm,
                            Hcm = h.Hcm,
                            KgM2 = HasirKgPerM2(h.Tip)
                        };
                        map[key] = p;
                    }
                    p.Adet += h.Kat < 1 ? 1 : h.Kat;
                }
            }
            var list = new List<HasirPoz>(map.Values);
            list.Sort((a, b) =>
            {
                int c = string.Compare(a.Tip, b.Tip, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                c = a.Wcm.CompareTo(b.Wcm);
                return c != 0 ? c : a.Hcm.CompareTo(b.Hcm);
            });
            for (int i = 0; i < list.Count; i++)
            {
                HasirPoz p = list[i];
                p.No = i + 1;
                double alan = p.Adet * (p.Wcm / 100.0) * (p.Hcm / 100.0);
                p.Kg = Math.Round(alan * p.KgM2, 2, MidpointRounding.AwayFromZero);
            }
            return list;
        }

        /// <summary>Hasır pozları normal donatının son numarasından devam eder.</summary>
        private static void ContinueHasirPoz(List<HasirPoz> hasir, int barCount)
        {
            if (hasir == null || barCount <= 0) return;
            foreach (HasirPoz p in hasir)
                p.No += barCount;
        }

        /// <summary>Q kodu cm2/m. Cap 0.5 mm yuvarlanir; aralik 150 mm (buyuk tiplerde 100 mm).</summary>
        private static double HasirKgPerM2(string tip)
        {
            Match m = Regex.Match(tip ?? "", @"Q\s*(\d+)\s*/\s*(\d+)", RegexOptions.IgnoreCase);
            if (!m.Success) return 0;
            if (!int.TryParse(m.Groups[1].Value, out int a)) return 0;
            if (!int.TryParse(m.Groups[2].Value, out int b)) return 0;
            double spacing = (a >= 500 || b >= 500) ? 100.0 : 150.0;
            double kg = DirKgPerM2(a, spacing) + DirKgPerM2(b, spacing);
            return Math.Round(kg, 2, MidpointRounding.AwayFromZero);
        }

        private static double DirKgPerM2(int code, double spacingMm)
        {
            if (code < 1 || spacingMm < 1) return 0;
            double barMm2 = (code / 100.0) * (spacingMm / 10.0);
            double d = Math.Sqrt(barMm2 * 4.0 / Math.PI);
            d = Math.Round(d * 2.0, MidpointRounding.AwayFromZero) / 2.0;
            if (d < 4) d = 4;
            return 0.006165 * d * d * (1000.0 / spacingMm);
        }

        private static List<Hit> HasirAsHits(List<HasirHit> hits)
        {
            var list = new List<Hit>();
            if (hits == null) return list;
            foreach (HasirHit h in hits)
            {
                if (h == null || h.Id.IsNull) continue;
                list.Add(new Hit { Id = h.Id });
            }
            return list;
        }

        private static int InsertHasir(
            Transaction tr, Database destDb, string path, Point3d pick, bool topLeft,
            List<HasirPoz> pozlar, out double toplamKg)
        {
            toplamKg = 0;
            if (pozlar != null)
            {
                foreach (HasirPoz p in pozlar) toplamKg += p.Kg;
                toplamKg = Math.Round(toplamKg, 2, MidpointRounding.AwayFromZero);
            }
            if (string.IsNullOrEmpty(path)) return 0;
            using (var src = new Database(false, true))
            {
                src.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
                var ids = new ObjectIdCollection();
                Database prev = HostApplicationServices.WorkingDatabase;
                try
                {
                    HostApplicationServices.WorkingDatabase = src;
                    using (var trs = src.TransactionManager.StartTransaction())
                    {
                        var btSrc = (BlockTable)trs.GetObject(src.BlockTableId, OpenMode.ForRead);
                        var ms = (BlockTableRecord)trs.GetObject(btSrc[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        FillHasir(trs, src, ms, pozlar, toplamKg);
                        CollectEntities(trs, btSrc, BlockTableRecord.ModelSpace, ids);
                        trs.Commit();
                    }
                }
                finally
                {
                    try { HostApplicationServices.WorkingDatabase = prev; } catch { }
                }
                if (ids.Count == 0) return 0;
                var bt = (BlockTable)tr.GetObject(destDb.BlockTableId, OpenMode.ForRead);
                var msDest = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                return PlaceCloned(tr, destDb, msDest, ids, pick, topLeft, out _);
            }
        }

        private const double HsRow = 20.0;
        private const double HsTop = 1452.23;
        private const double HsBot = 1352.23;
        private const double HsRowY0 = 1442.23;
        private const double HsLeft = 2717.64;
        private const double HsRight = 3292.64;
        private const double HsFootAlignX = 3150.14;
        private const double HsFootAlignY = 1327.23;
        private static readonly double[] HsColX = { 2738.89, 2813.39, 2919.89, 3026.39, 3132.89, 3239.39 };

        private static void FillHasir(Transaction tr, Database db, BlockTableRecord ms, List<HasirPoz> pozlar, double toplamKg)
        {
            if (pozlar == null) pozlar = new List<HasirPoz>();
            int n = Math.Max(1, pozlar.Count);
            var texts = new List<DBText>();
            var lines = new List<Line>();
            var balloons = new List<Polyline>();
            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead);
                if (ent is DBText t) texts.Add(t);
                else if (ent is Line ln) lines.Add(ln);
                else if (ent is Polyline pl && LayerHas(pl.Layer, "POZ BALON")) balloons.Add(pl);
            }
            DBText proto = null;
            DBText pozProto = null;
            foreach (DBText t in texts)
            {
                if (proto == null && LayerHas(t.Layer, "DONATI YAZISI") && Math.Abs(Anchor(t).Y - HsRowY0) < 8)
                    proto = (DBText)t.Clone();
                if (pozProto == null && LayerHas(t.Layer, "POZ NO") && Math.Abs(Anchor(t).Y - HsRowY0) < 8)
                    pozProto = (DBText)t.Clone();
                if (proto != null && pozProto != null) break;
            }
            Polyline balProto = null;
            foreach (Polyline pl in balloons)
            {
                if (pl.IsErased) continue;
                balProto = (Polyline)pl.Clone();
                break;
            }

            double newBot = FitHasirBand(tr, ms, lines, texts, balloons, n);
            SetHasirHeaders(db, texts);
            foreach (DBText t in texts)
            {
                if (t == null || t.IsErased) continue;
                string s = t.TextString ?? "";
                if (s.IndexOf("TABLO TOPLAM", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!t.IsWriteEnabled) t.UpgradeOpen();
                t.TextString = "HASIR DEMIR TOPLAM (kg)";
                try { t.AdjustAlignment(db); } catch { }
                break;
            }
            var used = new HashSet<ObjectId>();
            var usedBal = new HashSet<ObjectId>();
            double textH = pozProto != null && pozProto.Height > 1 ? pozProto.Height : 10;
            ObjectId style = pozProto != null ? pozProto.TextStyleId : ObjectId.Null;
            if (style.IsNull) style = BaDimensionStyles.EnsureYazi(tr, db);
            for (int i = 0; i < n; i++)
            {
                HasirPoz p = i < pozlar.Count ? pozlar[i] : null;
                double y = HsRowY0 - i * HsRow;
                string no = p == null ? "" : p.No.ToString(CultureInfo.InvariantCulture);
                Put(tr, db, ms, texts, pozProto ?? proto, HsColX[0], y, no, used);
                Put(tr, db, ms, texts, proto, HsColX[1], y, p == null ? "" : p.Adet.ToString(CultureInfo.InvariantCulture), used);
                Put(tr, db, ms, texts, proto, HsColX[2], y, p == null ? "" : p.Tip, used);
                Put(tr, db, ms, texts, proto, HsColX[3], y,
                    p == null ? "" : p.Wcm.ToString(CultureInfo.InvariantCulture) + "x" + p.Hcm.ToString(CultureInfo.InvariantCulture), used);
                Put(tr, db, ms, texts, proto, HsColX[4], y, p == null ? "" : F2(p.KgM2), used);
                Put(tr, db, ms, texts, proto, HsColX[5], y, p == null ? "" : F2(p.Kg), used);
                EnsureBalloon(tr, db, ms, balloons, balProto, HsColX[0], y, no, textH, style, usedBal, Find(texts, HsColX[0], y));
            }
            foreach (DBText t in texts)
            {
                if (t.IsErased || used.Contains(t.ObjectId)) continue;
                double y = Anchor(t).Y;
                if (y < HsTop - 2 && y > newBot - 2)
                    Erase(t);
            }
            foreach (Polyline pl in balloons)
            {
                if (pl.IsErased || usedBal.Contains(pl.ObjectId)) continue;
                Point3d c = Center(pl);
                if (c.Y < HsTop - 2 && c.Y > newBot - 2)
                    Erase(pl);
            }
            double footShift = newBot - HsBot;
            Put(tr, db, ms, texts, proto, HsFootAlignX, HsFootAlignY + footShift, F2(toplamKg), used);
            if (proto != null) proto.Dispose();
            if (pozProto != null) pozProto.Dispose();
            if (balProto != null) balProto.Dispose();
        }

        /// <summary>POZ, ADET, HASIR TIPI, EBAT, AĞIRLIK, TOPLAM.</summary>
        private static void SetHasirHeaders(Database db, List<DBText> texts)
        {
            string[] headers = { null, "ADET", "HASIR TIPI", "EBAT (cm)", null, null };
            foreach (DBText t in texts)
            {
                if (t == null || t.IsErased) continue;
                double y = Anchor(t).Y;
                if (y < 1460 || y > 1490) continue;
                int col = -1;
                double best = 35;
                for (int i = 0; i < HsColX.Length; i++)
                {
                    if (headers[i] == null) continue;
                    double d = Math.Abs(Anchor(t).X - HsColX[i]);
                    if (d < best)
                    {
                        best = d;
                        col = i;
                    }
                }
                if (col < 0) continue;
                if (!t.IsWriteEnabled) t.UpgradeOpen();
                t.TextString = headers[col];
                t.HorizontalMode = TextHorizontalMode.TextCenter;
                t.VerticalMode = TextVerticalMode.TextVerticalMid;
                t.AlignmentPoint = new Point3d(HsColX[col], y, 0);
                try { t.AdjustAlignment(db); } catch { }
            }
        }

        /// <summary>Veri bandını satır sayısına göre uzatır veya kısaltır. Footer 15 cm boşlukla aşağı kayar.</summary>
        private static double FitHasirBand(Transaction tr, BlockTableRecord ms, List<Line> lines, List<DBText> texts, List<Polyline> balloons, int rows)
        {
            if (rows < 1) rows = 1;
            double newBot = HsTop - rows * HsRow;
            double shift = newBot - HsBot;
            if (shift > 0.05)
            {
                EraseHoriz(lines, HsBot - 0.4, newBot - 0.4);
                SetVertLower(lines, HsBot, newBot);
                MoveBelow(lines, texts, balloons, HsBot - 1.0, shift);
            }
            else if (shift < -0.05)
            {
                MoveBelow(lines, texts, balloons, HsBot - 1.0, shift);
                SetVertLower(lines, HsBot, newBot);
                for (double y = HsBot - HsRow; y >= newBot - 0.2; y -= HsRow)
                {
                    var h = new Line(new Point3d(HsLeft, y, 0), new Point3d(HsRight, y, 0));
                    h.Layer = "IC ANTET (BEYKENT)";
                    ms.AppendEntity(h);
                    tr.AddNewlyCreatedDBObject(h, true);
                    lines.Add(h);
                }
            }
            return newBot;
        }

        private static void StampHasirPoz(Transaction tr, Database db, List<HasirHit> hits, List<HasirPoz> pozlar)
        {
            if (hits == null || pozlar == null || hits.Count == 0) return;
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            EnsureLayer(tr, db, LayerPozNo, 2);
            EnsureLayer(tr, db, LayerPozBalon, 4);
            EnsureRegApp(tr, db);
            var noOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (HasirPoz p in pozlar)
                noOf[p.Tip + "|" + p.Wcm.ToString(CultureInfo.InvariantCulture) + "|" + p.Hcm.ToString(CultureInfo.InvariantCulture)] = p.No;
            foreach (HasirHit h in hits)
            {
                if (h == null || h.Id.IsNull) continue;
                string key = h.Tip + "|" + h.Wcm.ToString(CultureInfo.InvariantCulture) + "|" + h.Hcm.ToString(CultureInfo.InvariantCulture);
                if (!noOf.TryGetValue(key, out int no)) continue;
                var ent = tr.GetObject(h.Id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased) continue;
                if (!TryLabelFrame(ent, out _, out Vector3d dir, out _, out double height, out ObjectId style))
                    continue;
                if (height < 1e-6) height = 10;
                Extents3d labelExt;
                try { labelExt = ent.GeometricExtents; }
                catch { continue; }

                string num = no.ToString(CultureInfo.InvariantCulture);
                double rot = Math.Atan2(dir.Y, dir.X);
                double guessW = MeasureTextWidth(tr, db, ms, style, height, rot, num);
                double guessBal = StadiumWidth(guessW, height);
                ProjRange(labelExt, dir, out double lmin, out _);
                Point3d mid = new Point3d(
                    0.5 * (labelExt.MinPoint.X + labelExt.MaxPoint.X),
                    0.5 * (labelExt.MinPoint.Y + labelExt.MaxPoint.Y), 0);
                double midProj = mid.X * dir.X + mid.Y * dir.Y;
                Point3d edge = mid - dir * (midProj - lmin);
                Point3d bal = edge - dir * (PozGap + guessBal * 0.5);

                var tx = new DBText
                {
                    TextString = num,
                    Height = height,
                    Rotation = rot,
                    Layer = LayerPozNo,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextVerticalMid,
                    AlignmentPoint = bal,
                    Position = bal
                };
                if (style.IsNull) style = BaDimensionStyles.EnsureYazi(tr, db);
                tx.TextStyleId = style;
                Color fill = null;
                if (DonatiMetrajSettings.Renkli)
                {
                    fill = PastelForHasir(h.Tip);
                    tx.Color = TextOnFill(fill);
                }
                ms.AppendEntity(tx);
                tr.AddNewlyCreatedDBObject(tx, true);
                try { tx.AdjustAlignment(db); } catch { }
                TagMetrajPoz(tx, ent.Handle.ToString());

                Extents3d te;
                try { te = tx.GeometricExtents; }
                catch { continue; }
                ProjRange(te, dir, out double tw0, out double tw1);
                double boxW = Math.Max(0.2, tw1 - tw0);
                Point3d boxC = new Point3d(
                    0.5 * (te.MinPoint.X + te.MaxPoint.X),
                    0.5 * (te.MinPoint.Y + te.MaxPoint.Y), 0);
                var pl = new Polyline();
                WriteStadium(pl, boxC, rot, boxW, height);
                pl.Layer = LayerPozBalon;
                if (fill != null)
                    pl.Color = fill;
                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                TagMetrajPoz(pl, ent.Handle.ToString());
                try
                {
                    ProjRange(ent.GeometricExtents, dir, out lmin, out _);
                    ProjRange(pl.GeometricExtents, dir, out _, out double bmax);
                    double shift = (lmin - PozGap) - bmax;
                    if (Math.Abs(shift) > 0.05)
                    {
                        var move = Matrix3d.Displacement(dir * shift);
                        pl.TransformBy(move);
                        tx.TransformBy(move);
                    }
                }
                catch { }

                if (fill != null)
                    FillPozBalloon(tr, db, ms, pl, tx, fill, ent.Handle.ToString());
            }
        }

        private static void Fill(Transaction tr, Database db, BlockTableRecord ms, List<Poz> pozlar, out double ince, out double kalin)
        {
            var texts = new List<DBText>();
            var balloons = new List<Polyline>();
            var lines = new List<Line>();
            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead);
                if (ent is DBText t) texts.Add(t);
                else if (ent is Polyline pl) balloons.Add(pl);
                else if (ent is Line ln) lines.Add(ln);
            }

            DBText pozProto = CloneFirst(texts, "POZ NO", 1260, 1590);
            DBText yaziProto = CloneFirst(texts, "DONATI YAZISI", 1260, 1590);
            Polyline balProto = null;
            foreach (Polyline pl in balloons)
            {
                if (pl.IsErased) continue;
                if (!LayerHas(pl.Layer, "POZ BALON")) continue;
                balProto = (Polyline)pl.Clone();
                CaptureBal(pl);
                break;
            }

            var used = new HashSet<ObjectId>();
            var usedBal = new HashSet<ObjectId>();
            int pozRows = Math.Max(1, (pozlar.Count + 1) / 2);
            int templateSlots = PozSatir * 2;
            YazPozlar(tr, db, ms, texts, balloons, pozProto, yaziProto, balProto, pozlar, 0, Math.Min(pozlar.Count, templateSlots), used, usedBal);
            EraseUnusedTexts(texts, used, 1260, 1590, null);
            foreach (Polyline pl in balloons)
            {
                if (pl.IsErased || usedBal.Contains(pl.ObjectId)) continue;
                if (!LayerHas(pl.Layer, "POZ BALON")) continue;
                Point3d c = Center(pl);
                if (c.Y > 1260 && c.Y < 1590)
                    Erase(pl);
            }

            double pozShift = FitBand(tr, ms, lines, texts, balloons, PozUst, PozAlt, pozRows, true);

            if (pozlar.Count > templateSlots)
                YazPozlar(tr, db, ms, texts, balloons, pozProto, yaziProto, balProto, pozlar, templateSlots, pozlar.Count, used, usedBal);

            var byCap = new List<CapOzet>();
            ince = 0;
            kalin = 0;
            var capOrder = new List<int>();
            var seenCap = new HashSet<int>();
            foreach (Poz p in pozlar)
                if (seenCap.Add(p.Cap)) capOrder.Add(p.Cap);
            capOrder.Sort();
            foreach (int cap in capOrder)
            {
                double boyM = 0, kg = 0;
                foreach (Poz p in pozlar)
                {
                    if (p.Cap != cap) continue;
                    int eff = p.Adet * (p.KatCarpan < 1 ? 1 : p.KatCarpan);
                    boyM += eff * p.BoyCm / 100.0;
                    kg += p.Kg;
                }
                byCap.Add(new CapOzet { Cap = cap, BoyM = boyM, Kg = kg });
                if (cap <= 12) ince += kg;
                else kalin += kg;
            }

            int capRows = Math.Max(1, byCap.Count);
            int hazir = Math.Min(OzetHazirSatir, byCap.Count);
            for (int i = 0; i < hazir; i++)
                YazOzetSatir(tr, db, ms, texts, yaziProto, OzetY0 + pozShift, i, byCap[i], used);
            EraseUnusedTexts(texts, used, OzetAlt + pozShift - 2, OzetUst + pozShift + 2, "DONATI YAZISI");

            double sumShift = FitBand(tr, ms, lines, texts, balloons, OzetUst + pozShift, OzetAlt + pozShift, capRows, false);
            for (int i = OzetHazirSatir; i < byCap.Count; i++)
                YazOzetSatir(tr, db, ms, texts, yaziProto, OzetY0 + pozShift, i, byCap[i], used);

            double foot = pozShift + sumShift;
            Put(tr, db, ms, texts, yaziProto, AltToplamX, InceY + foot, F2(ince), used);
            Put(tr, db, ms, texts, yaziProto, AltToplamX, KalinY + foot, F2(kalin), used);
            Put(tr, db, ms, texts, yaziProto, AltToplamX, GenelY + foot, F2(ince + kalin), used);
            WidenPozColumns(tr, ms);
            int katNot = pozlar != null && pozlar.Count > 0 ? pozlar[0].KatCarpan : 1;
            if (katNot > 1)
                YazKatNotu(tr, db, ms, yaziProto, katNot, lines);
        }

        private static void FillTotals(Transaction tr, Database db, BlockTableRecord ms, List<CapOzet> byCap, out double ince, out double kalin, double? hasirToplam = null)
        {
            var texts = new List<DBText>();
            var balloons = new List<Polyline>();
            var lines = new List<Line>();
            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead);
                if (ent is DBText t) texts.Add(t);
                else if (ent is Polyline pl) balloons.Add(pl);
                else if (ent is Line ln) lines.Add(ln);
            }

            DBText title = null;
            double titleY = double.MinValue;
            foreach (DBText t in texts)
            {
                if (t.IsErased) continue;
                double y = Anchor(t).Y;
                string s = (t.TextString ?? "").Trim();
                if (y < PozUst || s.Length < 6) continue;
                if (y > titleY)
                {
                    titleY = y;
                    title = t;
                }
            }
            if (title != null)
            {
                if (!title.IsWriteEnabled) title.UpgradeOpen();
                title.TextString = "TOPLAM DEMIR METRAJI";
                try { title.AdjustAlignment(db); } catch { }
            }

            double cut = title == null ? PozUst + 40 : titleY - 8;
            foreach (DBText t in texts)
            {
                if (t.IsErased || ReferenceEquals(t, title)) continue;
                double y = Anchor(t).Y;
                if (y > 1260 && y < cut)
                    Erase(t);
            }
            foreach (Polyline pl in balloons)
            {
                if (pl.IsErased) continue;
                double y = Center(pl).Y;
                if (y > 1260 && y < cut)
                    Erase(pl);
            }

            DBText yaziProto = CloneFirst(texts, "DONATI YAZISI", 1100, 1260);
            if (yaziProto == null)
                yaziProto = CloneFirst(texts, "DONATI YAZISI", 1260, 1590);

            double pozShift = FitBand(tr, ms, lines, texts, balloons, PozUst, PozAlt, 0, true, true);

            ince = 0;
            kalin = 0;
            if (byCap == null) byCap = new List<CapOzet>();
            foreach (CapOzet c in byCap)
            {
                if (c.Cap <= 12) ince += c.Kg;
                else kalin += c.Kg;
            }

            var used = new HashSet<ObjectId>();
            int capRows = Math.Max(1, byCap.Count);
            int hazir = Math.Min(OzetHazirSatir, byCap.Count);
            for (int i = 0; i < hazir; i++)
                YazOzetSatir(tr, db, ms, texts, yaziProto, OzetY0 + pozShift, i, byCap[i], used);
            EraseUnusedTexts(texts, used, OzetAlt + pozShift - 2, OzetUst + pozShift + 2, "DONATI YAZISI");

            double sumShift = FitBand(tr, ms, lines, texts, balloons, OzetUst + pozShift, OzetAlt + pozShift, capRows, false);
            for (int i = OzetHazirSatir; i < byCap.Count; i++)
                YazOzetSatir(tr, db, ms, texts, yaziProto, OzetY0 + pozShift, i, byCap[i], used);

            double foot = pozShift + sumShift;
            Put(tr, db, ms, texts, yaziProto, AltToplamX, InceY + foot, F2(ince), used);
            Put(tr, db, ms, texts, yaziProto, AltToplamX, KalinY + foot, F2(kalin), used);
            Put(tr, db, ms, texts, yaziProto, AltToplamX, GenelY + foot, F2(ince + kalin), used);
            WidenPozColumns(tr, ms);
            CloseTitleToCapGap(tr, ms, lines, texts, balloons, title);
            TrimTotalsOverhang(lines, texts, title);
            if (hasirToplam.HasValue)
                AddHasirToplamBand(tr, db, ms, lines, texts, yaziProto, hasirToplam.Value);
        }

        /// <summary>Toplam tablosunun 25 cm altına hasır kg satırı.</summary>
        private static void AddHasirToplamBand(Transaction tr, Database db, BlockTableRecord ms, List<Line> lines, List<DBText> texts, DBText yaziProto, double kg)
        {
            double left = double.MaxValue, right = double.MinValue, bottom = double.MaxValue;
            foreach (Line ln in lines)
            {
                if (ln == null || ln.IsErased || !Horiz(ln)) continue;
                double y = MidY(ln);
                double x0 = Math.Min(ln.StartPoint.X, ln.EndPoint.X);
                double x1 = Math.Max(ln.StartPoint.X, ln.EndPoint.X);
                if (x1 - x0 < 80) continue;
                if (x0 < left) left = x0;
                if (x1 > right) right = x1;
                if (y < bottom) bottom = y;
            }
            if (left > 1e8 || bottom > 1e8 || right - left < 40) return;

            double split = left + (right - left) * 0.62;
            DBText toplamTx = null;
            DBText degerTx = null;
            foreach (DBText t in texts)
            {
                if (t == null || t.IsErased) continue;
                string s = (t.TextString ?? "").Trim();
                if (s.StartsWith("TOPLAM", StringComparison.OrdinalIgnoreCase) && s.IndexOf("HASIR", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    if (toplamTx == null || Anchor(t).Y < Anchor(toplamTx).Y)
                        toplamTx = t;
                }
            }
            if (toplamTx != null)
            {
                double ty = Anchor(toplamTx).Y;
                double degerX = double.MinValue;
                foreach (DBText t in texts)
                {
                    if (t == null || t.IsErased || ReferenceEquals(t, toplamTx)) continue;
                    if (Math.Abs(Anchor(t).Y - ty) > 6) continue;
                    if (Anchor(t).X <= Anchor(toplamTx).X) continue;
                    if (!TryPlainNum(t.TextString, out _)) continue;
                    if (Anchor(t).X > degerX)
                    {
                        degerX = Anchor(t).X;
                        degerTx = t;
                    }
                }
                double best = double.MinValue;
                foreach (Line ln in lines)
                {
                    if (ln == null || ln.IsErased) continue;
                    if (Math.Abs(ln.StartPoint.X - ln.EndPoint.X) > 0.3) continue;
                    double x = 0.5 * (ln.StartPoint.X + ln.EndPoint.X);
                    double lo = Math.Min(ln.StartPoint.Y, ln.EndPoint.Y);
                    double hi = Math.Max(ln.StartPoint.Y, ln.EndPoint.Y);
                    if (ty < lo - 2 || ty > hi + 2) continue;
                    if (x <= Anchor(toplamTx).X + 8) continue;
                    if (degerTx != null && x >= degerX - 2) continue;
                    if (x <= left + 15 || x >= right - 15) continue;
                    if (x > best)
                    {
                        best = x;
                        split = x;
                    }
                }
            }

            const double rowH = 20.0;
            double top = bottom - HasirTabloGapCm;
            double bot = top - rowH;
            double midY = (top + bot) * 0.5;
            string lineLayer = "IC ANTET (BEYKENT)";
            foreach (Line ln in lines)
            {
                if (ln == null || ln.IsErased || !Horiz(ln)) continue;
                if (Math.Abs(MidY(ln) - bottom) < 1.5)
                {
                    lineLayer = ln.Layer;
                    break;
                }
            }

            void H(double y)
            {
                var ln = new Line(new Point3d(left, y, 0), new Point3d(right, y, 0)) { Layer = lineLayer };
                ms.AppendEntity(ln);
                tr.AddNewlyCreatedDBObject(ln, true);
            }
            void V(double x, double y0, double y1)
            {
                var ln = new Line(new Point3d(x, y0, 0), new Point3d(x, y1, 0)) { Layer = lineLayer };
                ms.AppendEntity(ln);
                tr.AddNewlyCreatedDBObject(ln, true);
            }
            H(top);
            H(bot);
            V(left, bot, top);
            V(right, bot, top);
            V(split, bot, top);

            DBText labelProto = toplamTx != null ? (DBText)toplamTx.Clone() : yaziProto;
            DBText valueProto = degerTx != null ? (DBText)degerTx.Clone() : yaziProto;
            PlaceCellText(tr, db, ms, labelProto, (left + split) * 0.5, midY, "HASIR DEMIR TOPLAM (kg)");
            PlaceCellText(tr, db, ms, valueProto, (split + right) * 0.5, midY, F2(kg));
            if (labelProto != null && !ReferenceEquals(labelProto, yaziProto) && !ReferenceEquals(labelProto, toplamTx))
                labelProto.Dispose();
            if (valueProto != null && !ReferenceEquals(valueProto, yaziProto) && !ReferenceEquals(valueProto, degerTx))
                valueProto.Dispose();
        }

        private static void PlaceCellText(Transaction tr, Database db, BlockTableRecord ms, DBText proto, double cx, double cy, string value)
        {
            if (proto == null || proto.IsErased) return;
            var t = (DBText)proto.Clone();
            t.TextString = value ?? "";
            t.HorizontalMode = TextHorizontalMode.TextCenter;
            t.VerticalMode = TextVerticalMode.TextVerticalMid;
            t.AlignmentPoint = new Point3d(cx, cy, 0);
            t.Position = t.AlignmentPoint;
            ms.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
            try { t.AdjustAlignment(db); } catch { }
            try
            {
                Extents3d e = t.GeometricExtents;
                double dx = cx - (e.MinPoint.X + e.MaxPoint.X) * 0.5;
                double dy = cy - (e.MinPoint.Y + e.MaxPoint.Y) * 0.5;
                if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                    t.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
            }
            catch { }
        }

        /// <summary>
        /// Toplam metrajda başlık alt çizgisi ile CAP satırı üst çizgisi arasında 15 cm bırakır.
        /// Aradaki boş poz satırının çizgilerini siler. Dış kenar düşeyleri aralıkta durur.
        /// </summary>
        private static void CloseTitleToCapGap(Transaction tr, BlockTableRecord ms, List<Line> lines, List<DBText> texts, List<Polyline> balloons, DBText title)
        {
            const double gapWant = 15.0;
            if (title == null || title.IsErased) return;
            double titleY = Anchor(title).Y;

            double capY = double.MaxValue;
            foreach (DBText t in texts)
            {
                if (t == null || t.IsErased) continue;
                string s = (t.TextString ?? "").Trim();
                if (!s.Equals("CAP", StringComparison.OrdinalIgnoreCase)) continue;
                double y = Anchor(t).Y;
                if (y < capY) capY = y;
            }
            if (capY > 1e8) return;

            double titleBottom = double.MinValue;
            double capTop = double.MaxValue;
            foreach (Line ln in lines)
            {
                if (!Horiz(ln)) continue;
                double y = MidY(ln);
                if (y < titleY - 1.0 && y > titleBottom) titleBottom = y;
                if (y > capY + 1.0 && y < capTop) capTop = y;
            }
            if (titleBottom < -1e8 || capTop > 1e8 || titleBottom < capTop - 0.5) return;

            foreach (Line ln in lines)
            {
                if (!Horiz(ln)) continue;
                double y = MidY(ln);
                if (y > capTop + 0.8 && y < titleBottom - 0.8)
                    Erase(ln);
            }

            double rightEdge = SagX + PozGrow * 2.0;
            foreach (Line ln in lines.ToArray())
            {
                if (ln == null || ln.IsErased) continue;
                if (Math.Abs(ln.StartPoint.X - ln.EndPoint.X) > 0.3) continue;
                double x = 0.5 * (ln.StartPoint.X + ln.EndPoint.X);
                double y1 = ln.StartPoint.Y, y2 = ln.EndPoint.Y;
                double lo = Math.Min(y1, y2), hi = Math.Max(y1, y2);
                bool startIsLo = y1 <= y2;
                bool outer = Math.Abs(x - SolX) < 2.0 || Math.Abs(x - rightEdge) < 2.0;

                if (hi <= capTop + 1.2 || lo >= titleBottom - 1.2) continue;
                if (outer) continue;

                if (lo >= capTop - 1.2 && hi <= titleBottom + 1.2)
                {
                    Erase(ln);
                    continue;
                }
                if (lo < capTop - 1.2 && hi > titleBottom + 1.2)
                {
                    double xLo = startIsLo ? ln.StartPoint.X : ln.EndPoint.X;
                    SetLineY(ln, startIsLo, titleBottom);
                    var low = new Line(new Point3d(xLo, lo, 0), new Point3d(x, capTop, 0));
                    low.Layer = ln.Layer;
                    ms.AppendEntity(low);
                    tr.AddNewlyCreatedDBObject(low, true);
                    lines.Add(low);
                }
                else if (lo < capTop - 1.2)
                    SetLineY(ln, !startIsLo, capTop);
                else
                    SetLineY(ln, startIsLo, titleBottom);
            }

            foreach (DBText t in texts)
            {
                if (t == null || t.IsErased || ReferenceEquals(t, title)) continue;
                double y = Anchor(t).Y;
                if (y > capTop + 1.2 && y < titleBottom - 1.2)
                    Erase(t);
            }
            foreach (Polyline pl in balloons)
            {
                if (pl == null || pl.IsErased) continue;
                double y = Center(pl).Y;
                if (y > capTop + 1.2 && y < titleBottom - 1.2)
                    Erase(pl);
            }

            double dy = (titleBottom - capTop) - gapWant;
            if (Math.Abs(dy) >= 0.05)
            {
                var move = Matrix3d.Displacement(new Vector3d(0, dy, 0));
                foreach (Line ln in lines)
                {
                    if (ln == null || ln.IsErased) continue;
                    if (Math.Max(ln.StartPoint.Y, ln.EndPoint.Y) <= capTop + 1.2)
                        Shift(ln, move);
                }
                foreach (DBText t in texts)
                {
                    if (t == null || t.IsErased) continue;
                    if (Anchor(t).Y <= capTop + 1.2)
                        Shift(t, move);
                }
                foreach (Polyline pl in balloons)
                {
                    if (pl == null || pl.IsErased) continue;
                    if (Center(pl).Y <= capTop + 1.2)
                        Shift(pl, move);
                }
                foreach (Line ln in lines)
                {
                    if (ln == null || ln.IsErased) continue;
                    if (Math.Abs(ln.StartPoint.X - ln.EndPoint.X) > 0.3) continue;
                    double x = 0.5 * (ln.StartPoint.X + ln.EndPoint.X);
                    if (Math.Abs(x - SolX) >= 2.0 && Math.Abs(x - rightEdge) >= 2.0) continue;
                    double y1 = ln.StartPoint.Y, y2 = ln.EndPoint.Y;
                    double lo = Math.Min(y1, y2), hi = Math.Max(y1, y2);
                    if (hi <= titleBottom + 1.2 || lo >= capTop - 1.2) continue;
                    SetLineY(ln, y1 <= y2, lo + dy);
                }
            }
            JoinOuterVertical(lines, SolX);
            JoinOuterVertical(lines, rightEdge);
        }

        /// <summary>
        /// Toplam metrajda başlık–CAP aralığı ve ince demir üst çizgisindeki
        /// yatayların sol/sağ taşmasını dış kenar düşeyine kadar keser.
        /// </summary>
        private static void TrimTotalsOverhang(List<Line> lines, List<DBText> texts, DBText title)
        {
            double left = double.MaxValue, right = double.MinValue;
            foreach (Line ln in lines)
            {
                if (ln == null || ln.IsErased) continue;
                if (Math.Abs(ln.StartPoint.X - ln.EndPoint.X) > 0.3) continue;
                double h = Math.Abs(ln.StartPoint.Y - ln.EndPoint.Y);
                if (h < 40) continue;
                double x = 0.5 * (ln.StartPoint.X + ln.EndPoint.X);
                if (x < left) left = x;
                if (x > right) right = x;
            }
            if (left > 1e8 || right - left < 20)
            {
                left = SolX;
                right = SagX + PozGrow * 2.0;
            }
            if (title != null && !title.IsErased)
            {
                double titleY = Anchor(title).Y;
                double capY = double.MaxValue;
                foreach (DBText t in texts)
                {
                    if (t == null || t.IsErased) continue;
                    if (!(t.TextString ?? "").Trim().Equals("CAP", StringComparison.OrdinalIgnoreCase)) continue;
                    double y = Anchor(t).Y;
                    if (y < capY) capY = y;
                }
                if (capY < 1e8)
                {
                    double titleBottom = double.MinValue, capTop = double.MaxValue;
                    foreach (Line ln in lines)
                    {
                        if (!Horiz(ln)) continue;
                        double y = MidY(ln);
                        if (y < titleY - 1.0 && y > titleBottom) titleBottom = y;
                        if (y > capY + 1.0 && y < capTop) capTop = y;
                    }
                    if (titleBottom > -1e8 && capTop < 1e8 && titleBottom >= capTop - 0.5)
                        ClipWideHoriz(lines, capTop - 0.6, titleBottom + 0.6, left, right);
                }
            }

            double inceY = double.MaxValue;
            foreach (DBText t in texts)
            {
                if (t == null || t.IsErased) continue;
                string f = FoldTr(t.TextString ?? "");
                if (f.IndexOf("INCE", StringComparison.Ordinal) < 0) continue;
                double y = Anchor(t).Y;
                if (y < inceY) inceY = y;
            }
            if (inceY > 1e8) return;
            double inceTop = double.MaxValue;
            foreach (Line ln in lines)
            {
                if (!Horiz(ln)) continue;
                double y = MidY(ln);
                if (y > inceY + 1.0 && y < inceTop) inceTop = y;
            }
            if (inceTop > 1e8) return;
            double above = double.MaxValue;
            foreach (Line ln in lines)
            {
                if (!Horiz(ln)) continue;
                double y = MidY(ln);
                if (y > inceTop + 1.0 && y < above) above = y;
            }
            double yHi = above < inceTop + 30.0 ? above : inceTop;
            ClipWideHoriz(lines, inceTop - 0.6, yHi + 0.6, left, right);
        }

        private static void ClipWideHoriz(List<Line> lines, double yMin, double yMax, double left, double right)
        {
            double minSpan = (right - left) * 0.5;
            foreach (Line ln in lines)
            {
                if (!Horiz(ln)) continue;
                double y = MidY(ln);
                if (y < yMin || y > yMax) continue;
                double x1 = ln.StartPoint.X, x2 = ln.EndPoint.X;
                double lo = Math.Min(x1, x2), hi = Math.Max(x1, x2);
                if (hi - lo < minSpan) continue;
                if (lo >= left - 0.3 && hi <= right + 0.3) continue;
                double nlo = Math.Max(lo, left);
                double nhi = Math.Min(hi, right);
                if (nhi - nlo < 1.0)
                {
                    Erase(ln);
                    continue;
                }
                bool startIsLo = x1 <= x2;
                SetLineX(ln, startIsLo, nlo);
                SetLineX(ln, !startIsLo, nhi);
            }
        }

        private static void SetLineX(Line ln, bool start, double x)
        {
            if (!ln.IsWriteEnabled) ln.UpgradeOpen();
            if (start)
                ln.StartPoint = new Point3d(x, ln.StartPoint.Y, ln.StartPoint.Z);
            else
                ln.EndPoint = new Point3d(x, ln.EndPoint.Y, ln.EndPoint.Z);
        }

        private static bool Horiz(Line ln)
        {
            return ln != null && !ln.IsErased && Math.Abs(ln.StartPoint.Y - ln.EndPoint.Y) < 0.3;
        }

        private static double MidY(Line ln) => 0.5 * (ln.StartPoint.Y + ln.EndPoint.Y);

        private static void SetLineY(Line ln, bool start, double y)
        {
            if (!ln.IsWriteEnabled) ln.UpgradeOpen();
            if (start)
                ln.StartPoint = new Point3d(ln.StartPoint.X, y, ln.StartPoint.Z);
            else
                ln.EndPoint = new Point3d(ln.EndPoint.X, y, ln.EndPoint.Z);
        }

        private static void JoinOuterVertical(List<Line> lines, double x)
        {
            Line keep = null;
            double lo = double.MaxValue, hi = double.MinValue;
            var group = new List<Line>();
            foreach (Line ln in lines)
            {
                if (ln == null || ln.IsErased) continue;
                if (Math.Abs(ln.StartPoint.X - ln.EndPoint.X) > 0.3) continue;
                double mx = 0.5 * (ln.StartPoint.X + ln.EndPoint.X);
                if (Math.Abs(mx - x) > 2.0) continue;
                double y1 = ln.StartPoint.Y, y2 = ln.EndPoint.Y;
                double a = Math.Min(y1, y2), b = Math.Max(y1, y2);
                if (b - a < 5) continue;
                group.Add(ln);
                if (a < lo) lo = a;
                if (b > hi) hi = b;
                if (keep == null || b - a > Math.Abs(keep.StartPoint.Y - keep.EndPoint.Y))
                    keep = ln;
            }
            if (keep == null || group.Count == 0) return;
            bool startIsLo = keep.StartPoint.Y <= keep.EndPoint.Y;
            SetLineY(keep, startIsLo, lo);
            SetLineY(keep, !startIsLo, hi);
            foreach (Line ln in group)
            {
                if (!ReferenceEquals(ln, keep))
                    Erase(ln);
            }
        }

        private static string AdetYazi(Poz p)
        {
            int kat = p == null || p.KatCarpan < 1 ? 1 : p.KatCarpan;
            string adet = (p == null ? 0 : p.Adet).ToString(CultureInfo.InvariantCulture);
            if (kat <= 1) return adet;
            return adet + "x" + kat.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Tablo alt çizgisinin 5 cm altına, ortaya “N kat içindir”.</summary>
        private static void YazKatNotu(Transaction tr, Database db, BlockTableRecord ms, DBText proto, int kat, List<Line> lines)
        {
            if (proto == null || kat < 2) return;
            double bottom = double.MaxValue;
            foreach (Line ln in lines)
            {
                if (ln == null || ln.IsErased) continue;
                if (Math.Abs(ln.StartPoint.Y - ln.EndPoint.Y) > 0.3) continue;
                double y = 0.5 * (ln.StartPoint.Y + ln.EndPoint.Y);
                if (y < bottom) bottom = y;
            }
            if (bottom > 1e8) return;
            double cx = (SolX + SagX + PozGrow * 2.0) * 0.5;
            var t = (DBText)proto.Clone();
            t.TextString = kat.ToString(CultureInfo.InvariantCulture) + " kat içindir";
            t.HorizontalMode = TextHorizontalMode.TextCenter;
            t.VerticalMode = TextVerticalMode.TextVerticalMid;
            t.AlignmentPoint = new Point3d(cx, bottom - 20, 0);
            t.Position = t.AlignmentPoint;
            ms.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
            try { t.AdjustAlignment(db); } catch { }
            try
            {
                Extents3d e = t.GeometricExtents;
                double dy = (bottom - 5.0) - e.MaxPoint.Y;
                double dx = cx - 0.5 * (e.MinPoint.X + e.MaxPoint.X);
                if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                    t.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
            }
            catch { }
        }

        private static void YazPozlar(Transaction tr, Database db, BlockTableRecord ms, List<DBText> texts, List<Polyline> balloons,
            DBText pozProto, DBText yaziProto, Polyline balProto, List<Poz> pozlar, int from, int to,
            HashSet<ObjectId> used, HashSet<ObjectId> usedBal)
        {
            for (int i = from; i < to; i++)
            {
                Poz p = pozlar[i];
                int row = i / 2;
                double[] cols = (i % 2 == 0) ? SolSutun : SagSutun;
                double y = PozY0 - row * Satir;
                Put(tr, db, ms, texts, pozProto, cols[0], y, p.No.ToString(CultureInfo.InvariantCulture), used);
                Put(tr, db, ms, texts, yaziProto, cols[1], y, AdetYazi(p), used);
                Put(tr, db, ms, texts, yaziProto, cols[2], y, Fi() + p.Cap.ToString(CultureInfo.InvariantCulture), used);
                Put(tr, db, ms, texts, yaziProto, cols[3], y, p.BoyCm.ToString(CultureInfo.InvariantCulture), used);
                Put(tr, db, ms, texts, yaziProto, cols[4], y, F2(p.Kg), used);
                EnsureBalloon(tr, db, ms, balloons, balProto, cols[0], y,
                    p.No.ToString(CultureInfo.InvariantCulture),
                    pozProto == null || pozProto.Height < 1e-6 ? 10 : pozProto.Height,
                    pozProto == null || pozProto.TextStyleId.IsNull
                        ? BaDimensionStyles.EnsureYazi(tr, db) : pozProto.TextStyleId,
                    usedBal, Find(texts, cols[0], y));
            }
        }

        /// <summary>
        /// Veri bandını satır sayısına göre kısaltır veya uzatır.
        /// Üst çizgi sabit. Alttaki her şey kayar. Dönüş: Y kayması (yukarı artı).
        /// </summary>
        private static double FitBand(Transaction tr, BlockTableRecord ms, List<Line> lines, List<DBText> texts, List<Polyline> balloons,
            double top, double bot, int rows, bool splitPoz, bool allowZero = false)
        {
            if (rows < 1)
            {
                if (!allowZero) rows = 1;
                else rows = 0;
            }
            double newBot = top - rows * Satir;
            double shift = newBot - bot;
            if (Math.Abs(shift) < 0.05)
                return 0;

            if (shift > 0)
            {
                EraseHoriz(lines, bot - 0.4, newBot - 0.4);
                SetVertLower(lines, bot, newBot);
                MoveBelow(lines, texts, balloons, bot - 1.0, shift);
            }
            else
            {
                MoveBelow(lines, texts, balloons, bot - 1.0, shift);
                SetVertLower(lines, bot, newBot);
                AddHoriz(tr, ms, lines, bot, newBot, splitPoz);
            }
            return shift;
        }

        private static void EraseHoriz(List<Line> lines, double yMin, double yMax)
        {
            foreach (Line ln in lines)
            {
                if (ln.IsErased) continue;
                if (Math.Abs(ln.StartPoint.Y - ln.EndPoint.Y) > 0.3) continue;
                double y = 0.5 * (ln.StartPoint.Y + ln.EndPoint.Y);
                if (y >= yMin && y < yMax)
                    Erase(ln);
            }
        }

        private static void SetVertLower(List<Line> lines, double oldBot, double newBot)
        {
            foreach (Line ln in lines)
            {
                if (ln.IsErased) continue;
                if (Math.Abs(ln.StartPoint.X - ln.EndPoint.X) > 0.3) continue;
                double y1 = ln.StartPoint.Y, y2 = ln.EndPoint.Y;
                double lo = Math.Min(y1, y2), hi = Math.Max(y1, y2);
                if (Math.Abs(lo - oldBot) > 1.2) continue;
                if (hi < oldBot + 30) continue;
                if (!ln.IsWriteEnabled) ln.UpgradeOpen();
                if (y1 <= y2)
                    ln.StartPoint = new Point3d(ln.StartPoint.X, newBot, 0);
                else
                    ln.EndPoint = new Point3d(ln.EndPoint.X, newBot, 0);
            }
        }

        private static void MoveBelow(List<Line> lines, List<DBText> texts, List<Polyline> balloons, double limit, double dy)
        {
            if (Math.Abs(dy) < 1e-6) return;
            var move = Matrix3d.Displacement(new Vector3d(0, dy, 0));
            foreach (Line ln in lines)
            {
                if (ln.IsErased) continue;
                double hi = Math.Max(ln.StartPoint.Y, ln.EndPoint.Y);
                if (hi <= limit)
                    Shift(ln, move);
            }
            foreach (DBText t in texts)
            {
                if (t.IsErased) continue;
                if (Anchor(t).Y <= limit)
                    Shift(t, move);
            }
            foreach (Polyline pl in balloons)
            {
                if (pl.IsErased) continue;
                Point3d c = Center(pl);
                if (c.Y <= limit)
                    Shift(pl, move);
            }
        }

        private static void Shift(Entity ent, Matrix3d move)
        {
            if (!ent.IsWriteEnabled) ent.UpgradeOpen();
            ent.TransformBy(move);
        }

        private static void AddHoriz(Transaction tr, BlockTableRecord ms, List<Line> lines, double oldBot, double newBot, bool splitPoz)
        {
            string layer = "IC ANTET (BEYKENT)";
            foreach (Line ln in lines)
            {
                if (ln.IsErased) continue;
                if (Math.Abs(ln.StartPoint.Y - ln.EndPoint.Y) > 0.3) continue;
                if (Math.Abs(0.5 * (ln.StartPoint.Y + ln.EndPoint.Y) - oldBot) < 1.0)
                {
                    layer = ln.Layer;
                    break;
                }
            }
            for (double y = oldBot - Satir; y >= newBot - 0.2; y -= Satir)
            {
                if (splitPoz)
                {
                    AddLine(tr, ms, lines, PozSol0(), y, PozSol1, y, layer);
                    AddLine(tr, ms, lines, PozSag0, y, SagX, y, layer);
                }
                else
                    AddLine(tr, ms, lines, SolX, y, SagX, y, layer);
            }
        }

        private static double PozSol0() => SolX;

        private static void AddLine(Transaction tr, BlockTableRecord ms, List<Line> lines, double x0, double y0, double x1, double y1, string layer)
        {
            var ln = new Line(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0));
            ln.Layer = layer;
            ms.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
            lines.Add(ln);
        }

        private sealed class CapOzet
        {
            public int Cap;
            public double BoyM;
            public double Kg;
        }

        private static void YazOzetSatir(Transaction tr, Database db, BlockTableRecord ms, List<DBText> texts, DBText proto, double y0, int i, CapOzet c, HashSet<ObjectId> used)
        {
            double y = y0 - i * Satir;
            Put(tr, db, ms, texts, proto, OzetSutun[0], y, Fi() + c.Cap.ToString(CultureInfo.InvariantCulture), used);
            Put(tr, db, ms, texts, proto, OzetSutun[1], y, F3(BirimKg(c.Cap)), used);
            Put(tr, db, ms, texts, proto, OzetSutun[2], y, F2(c.BoyM), used);
            Put(tr, db, ms, texts, proto, OzetSutun[3], y, F2(c.Kg), used);
        }

        private static void Put(Transaction tr, Database db, BlockTableRecord ms, List<DBText> texts, DBText proto, double x, double y, string value, HashSet<ObjectId> used)
        {
            DBText t = Find(texts, x, y);
            if (t != null)
            {
                if (!t.IsWriteEnabled) t.UpgradeOpen();
                t.TextString = value ?? "";
                t.HorizontalMode = TextHorizontalMode.TextCenter;
                t.VerticalMode = TextVerticalMode.TextVerticalMid;
                t.AlignmentPoint = new Point3d(x, y, 0);
                try { t.AdjustAlignment(db); } catch { }
                used.Add(t.ObjectId);
                return;
            }
            if (proto == null) return;
            t = (DBText)proto.Clone();
            t.TextString = value ?? "";
            t.HorizontalMode = TextHorizontalMode.TextCenter;
            t.VerticalMode = TextVerticalMode.TextVerticalMid;
            t.AlignmentPoint = new Point3d(x, y, 0);
            t.Position = new Point3d(x, y, 0);
            ms.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
            try { t.AdjustAlignment(db); } catch { }
            texts.Add(t);
            used.Add(t.ObjectId);
        }

        private const double PozGap = 5.0;
        private const string MetrajPozApp = "ST4METRAJPOZ";
        private const double LeftPozL = 1839.321;
        private const double LeftPozR = 1879.321;
        private const double RightPozL = 2134.321;
        private const double RightPozR = 2174.321;
        private const double PozGrow = 2.5;

        /// <summary>Sol ve sağ POZ sütunu 40 → 42.5 cm. Sağındaki her şey kayar.</summary>
        private static void WidenPozColumns(Transaction tr, BlockTableRecord ms)
        {
            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased) continue;
                var ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                if (ent == null || ent.IsErased) continue;
                if (ent is Line ln)
                {
                    double nx1 = ln.StartPoint.X + PozColShift(ln.StartPoint.X);
                    double nx2 = ln.EndPoint.X + PozColShift(ln.EndPoint.X);
                    if (Math.Abs(nx1 - ln.StartPoint.X) < 1e-6 && Math.Abs(nx2 - ln.EndPoint.X) < 1e-6)
                        continue;
                    ln.StartPoint = new Point3d(nx1, ln.StartPoint.Y, ln.StartPoint.Z);
                    ln.EndPoint = new Point3d(nx2, ln.EndPoint.Y, ln.EndPoint.Z);
                }
                else if (ent is DBText tx)
                {
                    double dx = PozColShift(Anchor(tx).X);
                    if (Math.Abs(dx) > 1e-6)
                        tx.TransformBy(Matrix3d.Displacement(new Vector3d(dx, 0, 0)));
                }
                else if (ent is Polyline pl)
                {
                    double dx = PozColShift(Center(pl).X);
                    if (Math.Abs(dx) > 1e-6)
                        pl.TransformBy(Matrix3d.Displacement(new Vector3d(dx, 0, 0)));
                }
                else
                {
                    try
                    {
                        Extents3d e = ent.GeometricExtents;
                        double dx = PozColShift(0.5 * (e.MinPoint.X + e.MaxPoint.X));
                        if (Math.Abs(dx) > 1e-6)
                            ent.TransformBy(Matrix3d.Displacement(new Vector3d(dx, 0, 0)));
                    }
                    catch { }
                }
            }
        }

        private static double PozColShift(double x)
        {
            if (x <= LeftPozL + 0.15) return 0;
            if (x < LeftPozR - 0.15)
            {
                double f = (x - LeftPozL) / (LeftPozR - LeftPozL);
                return f * PozGrow;
            }
            if (x <= LeftPozR + 0.15) return PozGrow;
            if (x < RightPozL - 0.15) return PozGrow;
            if (x < RightPozR - 0.15)
            {
                double f = (x - RightPozL) / (RightPozR - RightPozL);
                return PozGrow + f * PozGrow;
            }
            return PozGrow * 2;
        }

        private static void EnsureBalloon(Transaction tr, Database db, BlockTableRecord ms, List<Polyline> balloons, Polyline proto, double x, double y, string pozText, double textHeight, ObjectId style, HashSet<ObjectId> used, DBText pozTx)
        {
            Polyline pln = null;
            foreach (Polyline pl in balloons)
            {
                if (pl.IsErased) continue;
                Point3d c0 = Center(pl);
                if (Math.Abs(c0.X - x) < 8 && Math.Abs(c0.Y - y) < 8)
                {
                    pln = pl;
                    used.Add(pl.ObjectId);
                    break;
                }
            }
            if (pln == null && proto != null)
            {
                pln = (Polyline)proto.Clone();
                Point3d pc = Center(pln);
                pln.TransformBy(Matrix3d.Displacement(new Vector3d(x - pc.X, y - pc.Y, 0)));
                ms.AppendEntity(pln);
                tr.AddNewlyCreatedDBObject(pln, true);
                balloons.Add(pln);
                used.Add(pln.ObjectId);
            }
            if (pln == null) return;
            if (textHeight < 1e-6) textHeight = 10;
            double textW = MeasureTextWidth(tr, db, ms, style, textHeight, 0, pozText);
            Point3d c = new Point3d(x, y, 0);
            if (pozTx != null && !pozTx.IsErased)
            {
                try
                {
                    Extents3d e = pozTx.GeometricExtents;
                    textW = Math.Max(0.2, e.MaxPoint.X - e.MinPoint.X);
                    c = new Point3d(
                        0.5 * (e.MinPoint.X + e.MaxPoint.X),
                        0.5 * (e.MinPoint.Y + e.MaxPoint.Y), 0);
                }
                catch { }
            }
            WriteStadium(pln, c, 0, textW, textHeight);
        }

        private static void EraseUnusedTexts(List<DBText> texts, HashSet<ObjectId> used, double yMin, double yMax, string layerPart)
        {
            foreach (DBText t in texts)
            {
                if (t.IsErased || used.Contains(t.ObjectId)) continue;
                if (layerPart != null && !LayerHas(t.Layer, layerPart)) continue;
                if (layerPart == null && !LayerHas(t.Layer, "POZ NO") && !LayerHas(t.Layer, "DONATI YAZISI"))
                    continue;
                double y = Anchor(t).Y;
                if (y > yMin && y < yMax)
                    Erase(t);
            }
        }

        private static DBText CloneFirst(List<DBText> texts, string layerPart, double yMin, double yMax)
        {
            foreach (DBText t in texts)
            {
                if (t.IsErased || !LayerHas(t.Layer, layerPart)) continue;
                double y = Anchor(t).Y;
                if (y > yMin && y < yMax)
                    return (DBText)t.Clone();
            }
            return null;
        }

        private static DBText Find(List<DBText> texts, double x, double y)
        {
            foreach (DBText t in texts)
            {
                if (t.IsErased) continue;
                Point3d p = Anchor(t);
                if (Math.Abs(p.X - x) < 3.0 && Math.Abs(p.Y - y) < 3.0)
                    return t;
            }
            return null;
        }

        private static Point3d Anchor(DBText t)
        {
            try
            {
                if (t.Justify != AttachmentPoint.BaseLeft && t.Justify != AttachmentPoint.BaseAlign)
                    return t.AlignmentPoint;
            }
            catch { }
            return t.Position;
        }

        private static Point3d Center(Polyline pl)
        {
            if (pl.NumberOfVertices <= 0) return Point3d.Origin;
            double sx = 0, sy = 0;
            int n = pl.NumberOfVertices;
            for (int i = 0; i < n; i++)
            {
                Point2d p = pl.GetPoint2dAt(i);
                sx += p.X;
                sy += p.Y;
            }
            return new Point3d(sx / n, sy / n, 0);
        }

        private static void Erase(Entity ent)
        {
            if (ent == null || ent.IsErased) return;
            if (!ent.IsWriteEnabled) ent.UpgradeOpen();
            ent.Erase();
        }

        private static bool LayerOk(string layer)
        {
            var named = DonatiMetrajSettings.Katmanlar;
            if (named == null) return false;
            for (int i = 0; i < named.Count; i++)
            {
                if (string.Equals(named[i], layer, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool LayerHas(string layer, string part)
        {
            if (string.IsNullOrEmpty(layer) || string.IsNullOrEmpty(part)) return false;
            return layer.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeDonatiPlain(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            // Dar/boşluksuz NBSP vb. kiriş etiketlerinde parse kırılmasın.
            plain = plain.Replace('\u00A0', ' ').Replace('\u2007', ' ').Replace('\u202F', ' ')
                .Replace('\u2009', ' ').Replace('\r', ' ').Replace('\n', ' ');
            // Çap işaretlerini tek forma indir (⌀ diametre / boş küme karışıklığı).
            plain = plain.Replace('\u2300', 'ø').Replace('⌀', 'ø').Replace('Ø', 'ø');
            return plain;
        }

        private static bool TryParse(string plain, out int adet, out int cap, out int boyCm, out double rawCm)
        {
            adet = 0;
            cap = 0;
            boyCm = 0;
            rawCm = 0;
            if (string.IsNullOrWhiteSpace(plain)) return false;
            plain = NormalizeDonatiPlain(plain);
            // Adet isteğe bağlı (ø8 L=138 → 1). Çap ile L arasında ara yazı serbest.
            // Kiriş: 2x26ø8/9 L=220 MONTAJ — L= sonrası ek yazı serbest.
            string rx = @"(?:(?<adet>\d+(?:\s*[xX×*]\s*\d+)*)\s*)?(?:" + PhiAlt() + @")\s*(?<cap>\d{1,2})"
                        + @"(?:\s*/\s*\d+(?:[.,]\d+)?)?(?:\s+\S+){0,8}\s*"
                        + @"[Ll]\s*=\s*(?<boy>\d+(?:[.,]\d+)?)";
            Match m = Regex.Match(plain, rx);
            if (!m.Success)
            {
                // Phi ve L= ayrı yerdeyse (ara biçim bozuk) yine dene.
                if (!TryParseAdetCap(plain, out adet, out cap)) return false;
                Match mL = Regex.Match(plain, @"[Ll]\s*=\s*(?<boy>\d+(?:[.,]\d+)?)");
                if (!mL.Success) return false;
                string boyLoose = mL.Groups["boy"].Value.Replace(',', '.');
                if (!double.TryParse(boyLoose, NumberStyles.Float, CultureInfo.InvariantCulture, out double boyLooseV))
                    return false;
                rawCm = boyLooseV;
                boyCm = (int)Math.Round(boyLooseV, MidpointRounding.AwayFromZero);
                return boyCm > 0;
            }
            string adetRaw = m.Groups["adet"].Value;
            if (string.IsNullOrWhiteSpace(adetRaw))
                adet = 1;
            else if (!TryAdetProduct(adetRaw, out adet))
                return false;
            if (!int.TryParse(m.Groups["cap"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out cap) || cap < 1)
                return false;
            string boyRaw = m.Groups["boy"].Value.Replace(',', '.');
            if (!double.TryParse(boyRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double boy))
                return false;
            rawCm = boy;
            boyCm = (int)Math.Round(boy, MidpointRounding.AwayFromZero);
            return boyCm > 0;
        }

        /// <summary>2x4x65 → 520. Tek sayı da geçer.</summary>
        private static bool TryAdetProduct(string raw, out int adet)
        {
            adet = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string[] parts = Regex.Split(raw.Trim(), @"\s*[xX×*]\s*");
            long prod = 1;
            bool any = false;
            foreach (string p in parts)
            {
                if (p.Length == 0) return false;
                if (!int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 1)
                    return false;
                prod *= n;
                if (prod > int.MaxValue) return false;
                any = true;
            }
            if (!any || prod < 1) return false;
            adet = (int)prod;
            return true;
        }

        private static string PhiAlt()
        {
            string fi = (DonatiMetrajSettings.FiIsareti ?? "ø").Trim();
            var parts = new List<string>();
            if (fi.Length > 0)
                parts.Add(Regex.Escape(fi));
            if (fi.Length == 0 || IsPhi(fi))
            {
                AddPhi(parts, "ø");
                AddPhi(parts, "Ø");
                AddPhi(parts, "⌀");
                AddPhi(parts, "%%[cC]");
                AddPhi(parts, @"\\U\+00[Dd]8");
                AddPhi(parts, @"\\U\+00[Ff]8");
                AddPhi(parts, @"\\U\+2205");
            }
            return parts.Count == 0 ? "ø" : string.Join("|", parts);
        }

        private static void AddPhi(List<string> parts, string token)
        {
            if (!parts.Contains(token))
                parts.Add(token);
        }

        private static bool IsPhi(string fi)
        {
            string t = (fi ?? "").Trim();
            return t == "ø" || t == "Ø" || t == "⌀" ||
                   t.Equals("%%c", StringComparison.OrdinalIgnoreCase) ||
                   t.Equals("%%C", StringComparison.OrdinalIgnoreCase);
        }

        private static double BirimKg(int capMm)
        {
            if (KgPerM.TryGetValue(capMm, out double v))
                return v;
            return Math.Round(capMm * capMm * 0.00617, 3, MidpointRounding.AwayFromZero);
        }

        private static string Fi()
        {
            string fi = (DonatiMetrajSettings.FiIsareti ?? "ø").Trim();
            if (fi.Length == 0 || IsPhi(fi)) return "ø";
            return fi;
        }

        private static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        private static string F3(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);

        private static void CaptureBal(Polyline pl)
        {
            Point3d c = Center(pl);
            var list = new List<BalPt>();
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point2d p = pl.GetPoint2dAt(i);
                list.Add(new BalPt { X = p.X - c.X, Y = p.Y - c.Y, Bulge = pl.GetBulgeAt(i) });
            }
            if (list.Count >= 3)
                _balLocal = list;
        }

        /// <summary>
        /// Antet hızlı poz: balon+numara her zaman.
        /// Renkli=true → pastel kontur + SOLID tarama; false → kontur+numara.
        /// MeasureTextWidth / BaMetrajPozCell / DrawOrder / tam MS tarama yok.
        /// </summary>
        private static void StampLabelsFast(Transaction tr, Database db, List<Hit> hits, List<Poz> pozlar)
        {
            if (hits == null || hits.Count == 0 || pozlar == null || pozlar.Count == 0) return;
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            EnsureLayer(tr, db, LayerPozNo, 2);
            EnsureLayer(tr, db, LayerPozBalon, 4);
            EnsureRegApp(tr, db);
            bool renkli = DonatiMetrajSettings.Renkli;
            if (renkli)
                EnsureTaramaLayer(tr, db);
            ObjectId style = BaDimensionStyles.EnsureYazi(tr, db);
            var noOf = new Dictionary<long, int>();
            foreach (Poz p in pozlar)
                noOf[p.Cap * 100000L + p.BoyCm] = p.No;

            foreach (Hit h in hits)
            {
                if (!noOf.TryGetValue(h.Cap * 100000L + h.Boy, out int no)) continue;
                var ent = tr.GetObject(h.Id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased) continue;
                if (!TryFastLabelPose(ent, out Point3d anchor, out double rot, out double height))
                    continue;
                if (height < 1e-6) height = 5;

                string num = no.ToString(CultureInfo.InvariantCulture);
                double textW = Math.Max(height * 0.7, num.Length * height * 0.55);
                var dir = new Vector3d(Math.Cos(rot), Math.Sin(rot), 0);
                double labelHalf = EstimateLabelHalfWidth(ent, height);
                Point3d bal = anchor - dir * (labelHalf + PozGap + StadiumWidth(textW, height) * 0.5);
                string handle = ent.Handle.ToString();

                Color fill = renkli ? PastelForCap(h.Cap) : null;

                // Önce balon (+tarama), sonra numara → draw order şişirmesi yok.
                var pl = new Polyline();
                WriteStadium(pl, bal, rot, textW, height);
                pl.Layer = LayerPozBalon;
                if (fill != null)
                    pl.Color = fill;
                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                TagMetrajPoz(pl, handle);

                if (fill != null)
                    FillPozBalloonFast(tr, db, ms, pl, fill, handle);

                var tx = new DBText
                {
                    TextString = num,
                    Height = height,
                    Rotation = rot,
                    Layer = LayerPozNo,
                    TextStyleId = style,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextVerticalMid,
                    AlignmentPoint = bal,
                    Position = bal
                };
                if (fill != null)
                    tx.Color = TextOnFill(fill);
                ms.AppendEntity(tx);
                tr.AddNewlyCreatedDBObject(tx, true);
                TagMetrajPoz(tx, handle);
            }
        }

        private static void StampHasirPozFast(Transaction tr, Database db, List<HasirHit> hits, List<HasirPoz> pozlar)
        {
            if (hits == null || pozlar == null || hits.Count == 0) return;
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            EnsureLayer(tr, db, LayerPozNo, 2);
            EnsureLayer(tr, db, LayerPozBalon, 4);
            EnsureRegApp(tr, db);
            bool renkli = DonatiMetrajSettings.Renkli;
            if (renkli)
                EnsureTaramaLayer(tr, db);
            ObjectId style = BaDimensionStyles.EnsureYazi(tr, db);
            var noOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (HasirPoz p in pozlar)
                noOf[p.Tip + "|" + p.Wcm.ToString(CultureInfo.InvariantCulture) + "|" + p.Hcm.ToString(CultureInfo.InvariantCulture)] = p.No;

            foreach (HasirHit h in hits)
            {
                if (h == null || h.Id.IsNull) continue;
                string key = h.Tip + "|" + h.Wcm.ToString(CultureInfo.InvariantCulture) + "|" + h.Hcm.ToString(CultureInfo.InvariantCulture);
                if (!noOf.TryGetValue(key, out int no)) continue;
                var ent = tr.GetObject(h.Id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased) continue;
                if (!TryFastLabelPose(ent, out Point3d anchor, out double rot, out double height))
                    continue;
                if (height < 1e-6) height = 5;

                string num = no.ToString(CultureInfo.InvariantCulture);
                double textW = Math.Max(height * 0.7, num.Length * height * 0.55);
                var dir = new Vector3d(Math.Cos(rot), Math.Sin(rot), 0);
                double labelHalf = EstimateLabelHalfWidth(ent, height);
                Point3d bal = anchor - dir * (labelHalf + PozGap + StadiumWidth(textW, height) * 0.5);
                string handle = ent.Handle.ToString();

                Color fill = renkli ? PastelForHasir(h.Tip) : null;

                var pl = new Polyline();
                WriteStadium(pl, bal, rot, textW, height);
                pl.Layer = LayerPozBalon;
                if (fill != null)
                    pl.Color = fill;
                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                TagMetrajPoz(pl, handle);

                if (fill != null)
                    FillPozBalloonFast(tr, db, ms, pl, fill, handle);

                var tx = new DBText
                {
                    TextString = num,
                    Height = height,
                    Rotation = rot,
                    Layer = LayerPozNo,
                    TextStyleId = style,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextVerticalMid,
                    AlignmentPoint = bal,
                    Position = bal
                };
                if (fill != null)
                    tx.Color = TextOnFill(fill);
                ms.AppendEntity(tx);
                tr.AddNewlyCreatedDBObject(tx, true);
                TagMetrajPoz(tx, handle);
            }
        }

        private static bool TryFastLabelPose(Entity ent, out Point3d anchor, out double rot, out double height)
        {
            anchor = Point3d.Origin;
            rot = 0;
            height = 5;
            if (ent is DBText dbt)
            {
                height = dbt.Height > 0.1 ? dbt.Height : 5;
                rot = dbt.Rotation;
                anchor = dbt.Position;
                if (dbt.Justify != AttachmentPoint.BaseLeft &&
                    dbt.Justify != AttachmentPoint.BaseAlign &&
                    dbt.Justify != AttachmentPoint.BaseFit)
                {
                    try
                    {
                        Point3d a = dbt.AlignmentPoint;
                        if (!(a.X == 0 && a.Y == 0) || (anchor.X == 0 && anchor.Y == 0))
                            anchor = a;
                    }
                    catch { }
                }
                return true;
            }
            if (ent is MText mt)
            {
                height = mt.TextHeight > 0.1 ? mt.TextHeight : 5;
                rot = mt.Rotation;
                anchor = mt.Location;
                return true;
            }
            return false;
        }

        private static double EstimateLabelHalfWidth(Entity ent, double height)
        {
            string plain = "";
            if (ent is DBText dbt) plain = dbt.TextString ?? "";
            else if (ent is MText mt) plain = StripMText(mt.Contents ?? "");
            if (plain.Length == 0) return height * 3;
            return Math.Max(height * 2, plain.Length * height * 0.28);
        }

        /// <summary>Antet penceresindeki eski metraj poz marklarını siler (tam model taraması yok).</summary>
        private static void ErasePozMarksInFrame(Editor ed, Document doc, AntetFrame frame)
        {
            if (ed == null || doc == null || frame == null) return;
            var p1 = new Point3d(frame.MinX - AntetAcilimPadXCm, frame.MinY - AntetAcilimPadBelowCm, 0);
            var p2 = new Point3d(frame.MaxX + AntetAcilimPadXCm, frame.MaxY + AntetAcilimPadAboveCm, 0);
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Operator, "<AND"),
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "TEXT"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "HATCH"),
                new TypedValue((int)DxfCode.Operator, "OR>"),
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.LayerName, "POZ NO (BEYKENT)"),
                new TypedValue((int)DxfCode.LayerName, "POZ BALON (BEYKENT)"),
                new TypedValue((int)DxfCode.LayerName, "POZ TARAMA (BEYKENT)"),
                new TypedValue((int)DxfCode.LayerName, "HPOZ NO (BEYKENT)"),
                new TypedValue((int)DxfCode.LayerName, "HPOZ BALON (BEYKENT)"),
                new TypedValue((int)DxfCode.Operator, "OR>"),
                new TypedValue((int)DxfCode.Operator, "AND>")
            });
            PromptSelectionResult win;
            try { win = ed.SelectCrossingWindow(p1, p2, filter); }
            catch { return; }
            if (win.Status != PromptStatus.OK || win.Value == null || win.Value.Count == 0)
                return;

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                EnsureRegApp(tr, doc.Database);
                foreach (SelectedObject so in win.Value)
                {
                    if (so == null) continue;
                    var ent = tr.GetObject(so.ObjectId, OpenMode.ForWrite, false) as Entity;
                    if (ent == null || ent.IsErased) continue;
                    // Yalnız bu komutun bastığı marklar (XData) veya rakam POZ NO.
                    if (MetrajPozHandle(ent) != null)
                    {
                        Erase(ent);
                        continue;
                    }
                    if (ent is DBText tx && LayerHas(tx.Layer, "POZ NO"))
                    {
                        string s = (tx.TextString ?? "").Trim();
                        if (Regex.IsMatch(s, @"^\d{1,4}$"))
                            Erase(tx);
                    }
                }
                tr.Commit();
            }
        }

        private static void StampLabels(Transaction tr, Database db, List<Hit> hits, List<Poz> pozlar)
        {
            if (hits == null || hits.Count == 0 || _balLocal == null || _balLocal.Count < 3)
                return;
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            var quantityCells = BaMetrajPozCell.Collect(tr, ms);
            EnsureLayer(tr, db, LayerPozNo, 2);
            EnsureLayer(tr, db, LayerPozBalon, 4);
            EnsureRegApp(tr, db);
            var noOf = new Dictionary<long, int>();
            foreach (Poz p in pozlar)
                noOf[p.Cap * 100000L + p.BoyCm] = p.No;

            foreach (Hit h in hits)
            {
                if (!noOf.TryGetValue(h.Cap * 100000L + h.Boy, out int no)) continue;
                var ent = tr.GetObject(h.Id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased) continue;
                if (!TryLabelFrame(ent, out _, out Vector3d dir, out _, out double height, out ObjectId style))
                    continue;
                if (height < 1e-6) height = 10;
                Extents3d labelExt;
                try { labelExt = ent.GeometricExtents; }
                catch { continue; }

                string num = no.ToString(CultureInfo.InvariantCulture);
                double rot = Math.Atan2(dir.Y, dir.X);
                double guessW = MeasureTextWidth(tr, db, ms, style, height, rot, num);
                double guessBal = StadiumWidth(guessW, height);

                ProjRange(labelExt, dir, out double lmin, out _);
                Point3d mid = new Point3d(
                    0.5 * (labelExt.MinPoint.X + labelExt.MaxPoint.X),
                    0.5 * (labelExt.MinPoint.Y + labelExt.MaxPoint.Y), 0);
                double midProj = mid.X * dir.X + mid.Y * dir.Y;
                Point3d edge = mid - dir * (midProj - lmin);
                Point3d bal = edge - dir * (PozGap + guessBal * 0.5);

                var tx = new DBText
                {
                    TextString = num,
                    Height = height,
                    Rotation = rot,
                    Layer = LayerPozNo,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextVerticalMid,
                    AlignmentPoint = bal,
                    Position = bal
                };
                if (style.IsNull) style = BaDimensionStyles.EnsureYazi(tr, db);
                tx.TextStyleId = style;
                Color fill = null;
                if (DonatiMetrajSettings.Renkli)
                {
                    fill = PastelForCap(h.Cap);
                    tx.Color = TextOnFill(fill);
                }
                ms.AppendEntity(tx);
                tr.AddNewlyCreatedDBObject(tx, true);
                try { tx.AdjustAlignment(db); } catch { }
                TagMetrajPoz(tx, ent.Handle.ToString());

                Extents3d te;
                try { te = tx.GeometricExtents; }
                catch { continue; }
                ProjRange(te, dir, out double tw0, out double tw1);
                double boxW = Math.Max(0.2, tw1 - tw0);
                Point3d boxC = new Point3d(
                    0.5 * (te.MinPoint.X + te.MaxPoint.X),
                    0.5 * (te.MinPoint.Y + te.MaxPoint.Y), 0);

                var pl = new Polyline();
                WriteStadium(pl, boxC, rot, boxW, height);
                pl.Layer = LayerPozBalon;
                if (fill != null)
                    pl.Color = fill;
                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                TagMetrajPoz(pl, ent.Handle.ToString());

                try
                {
                    if (BaMetrajPozCell.TryFind(ent, quantityCells, out Extents3d cell))
                    {
                        BaMetrajPozCell.Center(pl, tx, cell);
                    }
                    else
                    {
                        ProjRange(ent.GeometricExtents, dir, out lmin, out _);
                        ProjRange(pl.GeometricExtents, dir, out _, out double bmax);
                        double shift = (lmin - PozGap) - bmax;
                        if (Math.Abs(shift) > 0.05)
                        {
                            var move = Matrix3d.Displacement(dir * shift);
                            pl.TransformBy(move);
                            tx.TransformBy(move);
                        }
                    }
                }
                catch { }

                if (fill != null)
                    FillPozBalloon(tr, db, ms, pl, tx, fill, ent.Handle.ToString());
            }
        }

        private static void FillPozBalloon(Transaction tr, Database db, BlockTableRecord ms, Polyline pl, DBText tx, Color fill, string handle)
        {
            try
            {
                EnsureTaramaLayer(tr, db);
                var hatch = new Hatch();
                hatch.SetDatabaseDefaults(db);
                hatch.Layer = LayerPozTarama;
                hatch.Color = fill;
                ms.AppendEntity(hatch);
                tr.AddNewlyCreatedDBObject(hatch, true);
                hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                hatch.Associative = true;
                hatch.HatchStyle = HatchStyle.Normal;
                hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { pl.ObjectId });
                hatch.EvaluateHatch(true);
                TagMetrajPoz(hatch, handle);

                var order = (DrawOrderTable)tr.GetObject(ms.DrawOrderTableId, OpenMode.ForWrite);
                order.MoveAbove(new ObjectIdCollection { tx.ObjectId }, hatch.ObjectId);
                order.MoveBelow(new ObjectIdCollection { hatch.ObjectId }, pl.ObjectId);
            }
            catch { }
        }

        /// <summary>Antet: SOLID tarama; DrawOrder yok (numara hatch’ten sonra eklenir).</summary>
        private static void FillPozBalloonFast(Transaction tr, Database db, BlockTableRecord ms, Polyline pl, Color fill, string handle)
        {
            if (pl == null || fill == null) return;
            try
            {
                var hatch = new Hatch();
                hatch.SetDatabaseDefaults(db);
                hatch.Layer = LayerPozTarama;
                hatch.Color = fill;
                ms.AppendEntity(hatch);
                tr.AddNewlyCreatedDBObject(hatch, true);
                hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                hatch.Associative = true;
                hatch.HatchStyle = HatchStyle.Normal;
                hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { pl.ObjectId });
                hatch.EvaluateHatch(true);
                TagMetrajPoz(hatch, handle);
            }
            catch { }
        }

        private static Color PastelForCap(int capMm)
        {
            if (!PastelRgb.TryGetValue(capMm, out int rgb))
            {
                int[] fallback = { 0x7EC4F0, 0xF4A0B4, 0x8ED48A, 0xC8A4F0, 0xF6C07A, 0x7EDDD4, 0xA4AEF4, 0xF49AC4 };
                rgb = fallback[Math.Abs(capMm) % fallback.Length];
            }
            return Rgb(rgb);
        }

        /// <summary>Aynı hasır tipi aynı pastel. Çap renklerinden ayrı aile.</summary>
        private static Color PastelForHasir(string tip)
        {
            int[] mesh = { 0xF6D56A, 0xE8A0C8, 0x8EC8F0, 0xB8E0A0, 0xD0B0F0, 0xF0B890, 0x90E0D8, 0xF0A0A8 };
            int h = 17;
            if (!string.IsNullOrEmpty(tip))
            {
                foreach (char ch in tip)
                    h = unchecked(h * 31 + char.ToUpperInvariant(ch));
            }
            if (h < 0) h = -h;
            return Rgb(mesh[h % mesh.Length]);
        }

        private static Color Rgb(int rgb)
        {
            return Color.FromRgb((byte)((rgb >> 16) & 255), (byte)((rgb >> 8) & 255), (byte)(rgb & 255));
        }

        /// <summary>Dolguya göre koyu veya açık rakam; kontrastı yüksek olan seçilir.</summary>
        private static Color TextOnFill(Color fill)
        {
            double bg = RelLuma(fill.Red, fill.Green, fill.Blue);
            double dark = RelLuma(28, 32, 38);
            double light = RelLuma(248, 248, 246);
            double cDark = Contrast(bg, dark);
            double cLight = Contrast(bg, light);
            if (cDark >= cLight)
                return Color.FromRgb(28, 32, 38);
            return Color.FromRgb(248, 248, 246);
        }

        private static double Contrast(double a, double b)
        {
            double hi = a > b ? a : b;
            double lo = a > b ? b : a;
            return (hi + 0.05) / (lo + 0.05);
        }

        private static double RelLuma(byte r, byte g, byte b)
        {
            return 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
        }

        private static double Lin(byte c)
        {
            double s = c / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        private static void EnsureTaramaLayer(Transaction tr, Database db)
        {
            BeykentKatmanYukle.Ensure(tr, db, LayerPozTarama, 8, LineWeight.LineWeight020);
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(LayerPozTarama)) return;
            var existing = (LayerTableRecord)tr.GetObject(lt[LayerPozTarama], OpenMode.ForRead);
            if (existing.IsPlottable)
            {
                existing.UpgradeOpen();
                existing.IsPlottable = false;
            }
        }

        private static bool TryLabelFrame(Entity ent, out Point3d center, out Vector3d dir, out double width, out double height, out ObjectId style)
        {
            center = Point3d.Origin;
            dir = Vector3d.XAxis;
            width = 0;
            height = 10;
            style = ObjectId.Null;
            double rot = 0;
            if (ent is DBText t)
            {
                rot = t.Rotation;
                height = t.Height > 1e-6 ? t.Height : 10;
                style = t.TextStyleId;
            }
            else if (ent is MText m)
            {
                rot = m.Rotation;
                height = m.TextHeight > 1e-6 ? m.TextHeight : 10;
                style = m.TextStyleId;
            }
            else return false;

            dir = new Vector3d(Math.Cos(rot), Math.Sin(rot), 0);
            Extents3d e;
            try { e = ent.GeometricExtents; }
            catch { return false; }
            center = new Point3d(
                0.5 * (e.MinPoint.X + e.MaxPoint.X),
                0.5 * (e.MinPoint.Y + e.MaxPoint.Y), 0);
            double dx = e.MaxPoint.X - e.MinPoint.X;
            double dy = e.MaxPoint.Y - e.MinPoint.Y;
            double c = Math.Abs(Math.Cos(rot));
            double s = Math.Abs(Math.Sin(rot));
            if (c >= s)
                width = c > 0.15 ? (dx - height * s) / c : dx;
            else
                width = s > 0.15 ? (dy - height * c) / s : dy;
            if (width < height * 0.4)
                width = Math.Max(dx, dy);
            if (width < height) width = height;
            return true;
        }

        private static void EnsureLayer(Transaction tr, Database db, string name, short aci)
        {
            BeykentKatmanYukle.Ensure(tr, db, name, aci, LineWeight.LineWeight020);
        }

        private static void ClearOldMarks(Transaction tr, Database db, List<Hit> hits)
        {
            if (hits == null || hits.Count == 0) return;
            EnsureRegApp(tr, db);
            var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pads = new List<Extents3d>();
            foreach (Hit h in hits)
            {
                var ent = tr.GetObject(h.Id, OpenMode.ForRead, false) as Entity;
                if (ent == null || ent.IsErased) continue;
                handles.Add(ent.Handle.ToString());
                if (TryTextAnchor(ent, out double ax, out double ay))
                {
                    pads.Add(new Extents3d(
                        new Point3d(ax - 40, ay - 40, 0),
                        new Point3d(ax + 40, ay + 40, 0)));
                    continue;
                }
                try
                {
                    Extents3d e = ent.GeometricExtents;
                    pads.Add(new Extents3d(
                        new Point3d(e.MinPoint.X - 25, e.MinPoint.Y - 25, 0),
                        new Point3d(e.MaxPoint.X + 25, e.MaxPoint.Y + 25, 0)));
                }
                catch { }
            }

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            var kill = new List<Entity>();
            var killedPts = new List<Point3d>();
            RXClass rxText = RXClass.GetClass(typeof(DBText));
            RXClass rxPl = RXClass.GetClass(typeof(Polyline));
            RXClass rxHatch = RXClass.GetClass(typeof(Hatch));
            // Transaction içinde SelectAll yok. Yalnız poz mark sınıfları.
            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased) continue;
                RXClass rx = id.ObjectClass;
                if (rx != rxText && rx != rxPl && rx != rxHatch) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased) continue;
                if (handles.Contains(ent.Handle.ToString())) continue;
                string linked = MetrajPozHandle(ent);
                if (linked != null)
                {
                    if (handles.Contains(linked))
                        kill.Add(ent);
                    continue;
                }
                if (ent is DBText tx)
                {
                    if (!LayerHas(tx.Layer, "POZ NO")) continue;
                    string s = (tx.TextString ?? "").Trim();
                    if (!Regex.IsMatch(s, @"^\d{1,4}$")) continue;
                    Point3d a = Anchor(tx);
                    var te = new Extents3d(
                        new Point3d(a.X - 8, a.Y - 8, 0),
                        new Point3d(a.X + 8, a.Y + 8, 0));
                    if (!HitsPad(te, pads)) continue;
                    kill.Add(tx);
                    killedPts.Add(a);
                }
            }
            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased) continue;
                if (id.ObjectClass != rxPl) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (!(ent is Polyline pl) || pl.IsErased) continue;
                if (MetrajPozHandle(pl) != null) continue;
                if (!LayerHas(pl.Layer, "POZ BALON")) continue;
                Point3d c = Center(pl);
                bool near = false;
                foreach (Point3d p in killedPts)
                {
                    if (Math.Abs(p.X - c.X) < 14 && Math.Abs(p.Y - c.Y) < 14)
                    {
                        near = true;
                        break;
                    }
                }
                if (near) kill.Add(pl);
            }
            foreach (Entity ent in kill)
                Erase(ent);
        }

        private static bool HitsPad(Extents3d e, List<Extents3d> pads)
        {
            foreach (Extents3d p in pads)
            {
                if (e.MaxPoint.X < p.MinPoint.X || e.MinPoint.X > p.MaxPoint.X) continue;
                if (e.MaxPoint.Y < p.MinPoint.Y || e.MinPoint.Y > p.MaxPoint.Y) continue;
                return true;
            }
            return false;
        }

        private static void EnsureRegApp(Transaction tr, Database db)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(MetrajPozApp)) return;
            rat.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = MetrajPozApp };
            rat.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        private static void TagMetrajPoz(Entity ent, string handle)
        {
            ent.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, MetrajPozApp),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, handle ?? ""));
        }

        private static string MetrajPozHandle(Entity ent)
        {
            ResultBuffer rb = null;
            try { rb = ent.GetXDataForApplication(MetrajPozApp); }
            catch { return null; }
            if (rb == null) return null;
            using (rb)
            {
                foreach (TypedValue tv in rb)
                {
                    if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        return tv.Value as string;
                }
            }
            return null;
        }

        private static double MeasureTextWidth(Transaction tr, Database db, BlockTableRecord ms, ObjectId style, double height, double rot, string text)
        {
            if (string.IsNullOrEmpty(text)) return height;
            var tx = new DBText
            {
                TextString = text,
                Height = height,
                Rotation = rot,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                AlignmentPoint = Point3d.Origin,
                Position = Point3d.Origin
            };
            if (style.IsNull) style = BaDimensionStyles.EnsureYazi(tr, db);
            tx.TextStyleId = style;
            ms.AppendEntity(tx);
            tr.AddNewlyCreatedDBObject(tx, true);
            try { tx.AdjustAlignment(db); } catch { }
            double w = height * Math.Max(1, text.Length) * 0.6;
            try
            {
                var dir = new Vector3d(Math.Cos(rot), Math.Sin(rot), 0);
                ProjRange(tx.GeometricExtents, dir, out double a, out double b);
                w = Math.Max(0.1, b - a);
            }
            catch { }
            Erase(tx);
            return w;
        }

        private static void StadiumSize(double textW, double textH, out double straight, out double radius)
        {
            if (textH < 1e-6) textH = 10;
            if (textW < 0.2) textW = 0.2;
            double vPad = Math.Max(0.4, 0.12 * textH);
            straight = textW * 0.25;
            radius = textH * 0.5 + vPad;
        }

        private static double StadiumWidth(double textW, double textH)
        {
            StadiumSize(textW, textH, out double s, out double r);
            return 2.0 * (s + r);
        }

        /// <summary>Üst ve alt düz kenar, yanlarda dışbükey yarım daire.</summary>
        private static List<BalPt> StadiumPts(double textW, double textH)
        {
            StadiumSize(textW, textH, out double s, out double r);
            return new List<BalPt>
            {
                new BalPt { X = -s, Y = -r, Bulge = 0 },
                new BalPt { X =  s, Y = -r, Bulge = 1 },
                new BalPt { X =  s, Y =  r, Bulge = 0 },
                new BalPt { X = -s, Y =  r, Bulge = 1 }
            };
        }

        private static void WriteStadium(Polyline pl, Point3d c, double rot, double textW, double textH)
        {
            if (pl == null || pl.IsErased) return;
            List<BalPt> fitted = StadiumPts(textW, textH);
            double cs = Math.Cos(rot), sn = Math.Sin(rot);
            if (pl.ObjectId.IsValid && !pl.IsWriteEnabled) pl.UpgradeOpen();
            int oldN = pl.NumberOfVertices;
            for (int i = 0; i < fitted.Count; i++)
            {
                double lx = fitted[i].X, ly = fitted[i].Y;
                double x = c.X + lx * cs - ly * sn;
                double y = c.Y + lx * sn + ly * cs;
                pl.AddVertexAt(pl.NumberOfVertices, new Point2d(x, y), fitted[i].Bulge, 0, 0);
            }
            for (int i = 0; i < oldN && pl.NumberOfVertices > 2; i++)
                pl.RemoveVertexAt(0);
            pl.Closed = true;
        }

        private static void ProjRange(Extents3d e, Vector3d dir, out double min, out double max)
        {
            Point3d[] c =
            {
                new Point3d(e.MinPoint.X, e.MinPoint.Y, 0),
                new Point3d(e.MaxPoint.X, e.MinPoint.Y, 0),
                new Point3d(e.MaxPoint.X, e.MaxPoint.Y, 0),
                new Point3d(e.MinPoint.X, e.MaxPoint.Y, 0)
            };
            min = double.MaxValue;
            max = double.MinValue;
            for (int i = 0; i < c.Length; i++)
            {
                double d = c[i].X * dir.X + c[i].Y * dir.Y;
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }

        private static string StripMText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, @"\\[PpLlOoKk]", " ");
            s = Regex.Replace(s, @"\{[^;]*;", "");
            s = s.Replace("{", "").Replace("}", "");
            return s;
        }

        private static void CollectEntities(Transaction trs, BlockTable btSrc, string spaceName, ObjectIdCollection ids)
        {
            if (!btSrc.Has(spaceName)) return;
            var ms = (BlockTableRecord)trs.GetObject(btSrc[spaceName], OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased) continue;
                if (trs.GetObject(id, OpenMode.ForRead) is Entity)
                    ids.Add(id);
            }
        }

        private static string EnsureHasirMetrajDwg()
        {
            return EnsureEmbeddedDwg(HasirMetrajResourceName, "ST4Yardimci_hasir_metraj_");
        }

        private static string EnsureTempDwg()
        {
            return EnsureEmbeddedDwg(EmbeddedResourceName, "ST4Yardimci_demir_tablosu_");
        }

        private static string EnsureEmbeddedDwg(string resourceName, string filePrefix)
        {
            lock (TempLock)
            {
                Assembly asm = typeof(DonatiMetrajRunner).Assembly;
                using (Stream stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    string path = Path.Combine(Path.GetTempPath(),
                        filePrefix + asm.ManifestModule.ModuleVersionId.ToString("N") + ".dwg");
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
                    _tempDwg = path;
                    return path;
                }
            }
        }
    }
}
