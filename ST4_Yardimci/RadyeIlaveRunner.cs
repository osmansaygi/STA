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
    /// Radye İlave Donatı — TEMEL ILAVE X / Y ısı konturu.
    /// X: yatay bant+çubuk (KUTU X). Y: düşey bant+çubuk (KUTU Y, yazı sol-üst).
    /// X/Y bant+çubuk; TEMEL çukuru banda girerse trim (paspayı) + serbest dilimlere tam boy.
    /// </summary>
    internal static class RadyeIlaveRunner
    {
        private const string LayerHeatX = "TEMEL ILAVE X (BEYKENT)";
        private const string LayerHeatY = "TEMEL ILAVE Y (BEYKENT)";
        private const string LayerKutuX = "TEMEL ILAVE KUTU X (BEYKENT)";
        private const string LayerKutuY = "TEMEL ILAVE KUTU Y (BEYKENT)";
        private const string LayerCizgi = "CIZGI (BEYKENT)";
        private const string LayerDonati = "DONATI (BEYKENT)";
        private const string LayerDonatiYazi = "DONATI YAZISI (BEYKENT)";
        private const string LayerDonatiOku = "DONATI OKU (BEYKENT)";
        private const string LayerDonatiHalkasi = "DONATI HALKASI (BEYKENT)";
        private const string DimStyleDonatiOku = "DONATI OKU (BEYKENT)";
        private const string LayerTemel = "TEMEL (BEYKENT)";
        private const string TextStyleYazi = "YAZI (BEYKENT)";
        private const short DonatiOkuAci = 242;
        private const short DonatiHalkasiAci = 2; // sarı
        private const double DonatiOkuCircleDiaCm = 4.0;
        private const double DonatiOkuCircleWidthCm = 4.0; // global width
        private const double DonatiOkuTextClearCm = 20.0; // yazı: halkadan çizgi üzerinde 20 cm
        private const double DonatiOkuEndClearCm = 40.0; // uçtan min uzaklık
        private const double MinDonatiOkuGapCm = 1.0;
        private const double MinBarLengthCm = 240.0;
        private const double MinHeatAreaCm2 = 3000.0; // daha küçük ısı yok sayılır
        private const double MinSegAfterTrimCm = 20.0;
        private const int MinBandGaps = 7; // min bant = 7 donatı arası
        private const double DonatiLabelHeightCm = 10.0;
        private const double DonatiLabelGapCm = 3.0;
        /// <summary>Kolon/etiket kutusundan min uzaklık (cm) — çubuk kaydırma.</summary>
        private const double ObstacleClearCm = 12.0;
        private const double CleanShiftStepCm = 5.0;
        private const string LayerKolon = "KOLON (BEYKENT)";
        private const string LayerKolonIsmi = "KOLON ISMI (BEYKENT)";
        private const string LayerPerde = "PERDE (BEYKENT)";
        private const string LayerPerdeIsmi = "PERDE ISMI (BEYKENT)";
        private const string LayerKapama = "KAPAMA PERDESI (BEYKENT)";
        private const string LayerKapamaIsmi = "KAPAMA PERDESI ISMI (BEYKENT)";
        private static readonly double[] PreferStockDivisors =
        {
            240, 300, 400, 600, 1200
        };
        private static readonly Regex DonatiYaziRx = new Regex(
            @"(\d+)\s*[\u00D8\u00F8ØøΦφ]\s*(\d{1,2})\s*/\s*(\d{1,3})",
            RegexOptions.CultureInvariant);

        private struct KutuInfo
        {
            public bool Found;
            public double MinX, MaxX, MinY, MaxY;
            public int CapMm;
            public double AralikCm;
            public int AdetLabel;
            public string Label;
        }

        private struct CoverRect
        {
            public double X0, X1, Y0, Y1;
            public double StockW;
            public double Width => X1 - X0;
            public double Height => Y1 - Y0;
        }

        private struct KutuBox
        {
            public ObjectId Id;
            public ObjectId LabelId;
            public double MinX, MaxX, MinY, MaxY;
            public int CapMm;
            public double AralikCm;
            public int AdetLabel;
            public string Label;
            public bool HasLabel;
        }

        private struct HeatJob
        {
            public ObjectId PolyId;
            public ObjectId KutuPolyId;
            public ObjectId KutuLabelId;
            public List<Point2d> Ring;
            public Extents3d Ext;
            public KutuInfo Kutu;
            public bool IsY;
        }

        /// <summary>Ölçü hattının kestiği çubuk parçası (Along = çubuk boyu ekseni).</summary>
        private struct DonatiOkuSeg
        {
            public double Along0, Along1, Fixed;
        }

        /// <summary>Tüm çubuklar çizildikten sonra yerleştirilecek etiket (dik donatı engeline göre kayar).</summary>
        private struct PendingDonatiLabel
        {
            public double Along0, Along1, FixedCoord;
            public string Text;
            public bool IsY;
            public string Layer;
            public ObjectId TextStyleId;
        }

        public static void RunInteractive()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                RadyeTemelDonatiForm.ShowAfterPick();
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                ed.WriteMessage(
                    "\nRADYEILAVECIZ: {0} | ısı X/Y (çoklu) | Ø/aralık ← kendi KUTU X/Y",
                    RadyeTemelDonatiSettings.IlaveKatEtiketi);

                List<ObjectId> ids = PickHeatContours(ed);
                if (ids.Count == 0)
                {
                    ed.WriteMessage("\nRADYEILAVECIZ: seçim yok / iptal.");
                    return;
                }

                List<HeatJob> jobs = PrepareHeatJobs(doc, db, ed, ids);
                if (jobs.Count == 0)
                {
                    ed.WriteMessage("\nRADYEILAVECIZ: geçerli X/Y ısı konturu yok.");
                    return;
                }

                int ok = 0, fail = 0, totalBands = 0;
                var drawnJobs = new List<HeatJob>();
                var sharedObstacles = new List<(double x0, double x1, double y0, double y1)>();
                var pendingLabels = new List<PendingDonatiLabel>();

                // Önce mevcut kolon/donatı engelleri
                using (doc.LockDocument())
                using (var tr0 = db.TransactionManager.StartTransaction())
                {
                    CollectVisualObstacles(tr0, db, sharedObstacles);
                    tr0.Commit();
                }

                for (int i = 0; i < jobs.Count; i++)
                {
                    int n = DrawForJob(doc, db, ed, jobs[i], sharedObstacles, pendingLabels);
                    if (n < 0) fail++;
                    else
                    {
                        ok++;
                        totalBands += n;
                        drawnJobs.Add(jobs[i]);
                    }
                }

                // Tüm X+Y çubukları çizildikten sonra etiketleri kaydırarak yerleştir
                if (pendingLabels.Count > 0)
                    FlushPendingDonatiLabels(doc, db, pendingLabels, sharedObstacles);

                if (drawnJobs.Count > 0)
                    EraseHeatAndKutuAfterDraw(doc, db, ed, drawnJobs);

                ed.WriteMessage(
                    "\nRADYEILAVECIZ: {0} kontur OK ({1} bant), {2} atlandı.",
                    ok, totalBands, fail);
            }
            catch (System.Exception ex)
            {
                try { ed.WriteMessage("\nRADYEILAVECIZ hata: {0}", ex.Message); } catch { }
            }
            finally
            {
                RadyeTemelDonatiForm.ShowAfterPick();
            }
        }

        private static List<ObjectId> PickHeatContours(Editor ed)
        {
            var result = new List<ObjectId>();
            var opt = new PromptSelectionOptions
            {
                MessageForAdding = string.Format(
                    CultureInfo.InvariantCulture,
                    "\nIsı konturları seçin [{0} / {1}] (çoklu): ",
                    LayerHeatX, LayerHeatY),
                MessageForRemoval = "\nÇıkarılacak: ",
                AllowDuplicates = false
            };
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Operator, "<AND"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.LayerName, LayerHeatX),
                new TypedValue((int)DxfCode.LayerName, LayerHeatY),
                new TypedValue((int)DxfCode.Operator, "OR>"),
                new TypedValue((int)DxfCode.Operator, "AND>")
            });
            PromptSelectionResult res = ed.GetSelection(opt, filter);
            if (res.Status != PromptStatus.OK || res.Value == null) return result;

            ObjectId[] ids = res.Value.GetObjectIds();
            for (int i = 0; i < ids.Length; i++)
            {
                if (!ids[i].IsNull) result.Add(ids[i]);
            }
            return result;
        }

        /// <summary>
        /// Seçilen ısıları oku; X→KUTU X, Y→KUTU Y tekil eşle — karışmasın.
        /// </summary>
        private static List<HeatJob> PrepareHeatJobs(
            Document doc, Database db, Editor ed, List<ObjectId> polyIds)
        {
            var heatsX = new List<(ObjectId id, List<Point2d> ring, Extents3d ext)>();
            var heatsY = new List<(ObjectId id, List<Point2d> ring, Extents3d ext)>();
            var kutularX = new List<KutuBox>();
            var kutularY = new List<KutuBox>();

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < polyIds.Count; i++)
                {
                    var ent = tr.GetObject(polyIds[i], OpenMode.ForRead) as Entity;
                    if (!(ent is Polyline pl) || pl.NumberOfVertices < 3) continue;
                    bool isX = LayerEquals(pl.Layer, LayerHeatX);
                    bool isY = LayerEquals(pl.Layer, LayerHeatY);
                    if (!isX && !isY)
                    {
                        ed.WriteMessage("\n  Atlandı (katman≠X/Y): {0}", pl.Layer ?? "?");
                        continue;
                    }
                    List<Point2d> ring = ExtractRing(pl);
                    EnsureClosed(ring);
                    if (ring.Count < 3) continue;
                    double area = 0;
                    try { area = Math.Abs(pl.Area); } catch { area = PolygonAreaCm2(ring); }
                    if (area < MinHeatAreaCm2 - 1e-6)
                    {
                        ed.WriteMessage(
                            "\n  Atlandı (alan={0:0.#} < {1:0} cm²): {2}",
                            area, MinHeatAreaCm2, isY ? "Y" : "X");
                        continue;
                    }
                    Extents3d ext;
                    try { ext = pl.GeometricExtents; }
                    catch { continue; }
                    if (isY) heatsY.Add((polyIds[i], ring, ext));
                    else heatsX.Add((polyIds[i], ring, ext));
                }

                CollectKutuBoxes(tr, db, kutularX, LayerKutuX, labelNearTopLeft: false);
                CollectKutuBoxes(tr, db, kutularY, LayerKutuY, labelNearTopLeft: true);
                tr.Commit();
            }

            ed.WriteMessage(
                "\n  Isı X={0} KUTU X={1} | Isı Y={2} KUTU Y={3}",
                heatsX.Count, kutularX.Count, heatsY.Count, kutularY.Count);

            var jobs = new List<HeatJob>();
            AssignJobs(ed, heatsX, kutularX, isY: false, jobs);
            AssignJobs(ed, heatsY, kutularY, isY: true, jobs);
            return jobs;
        }

        private static void AssignJobs(
            Editor ed,
            List<(ObjectId id, List<Point2d> ring, Extents3d ext)> heats,
            List<KutuBox> kutular,
            bool isY,
            List<HeatJob> jobs)
        {
            var pairs = new List<(int hi, int ki, double score)>();
            for (int hi = 0; hi < heats.Count; hi++)
            {
                for (int ki = 0; ki < kutular.Count; ki++)
                {
                    double score = ScoreHeatToKutu(heats[hi].ext, kutular[ki]);
                    if (score > 0) pairs.Add((hi, ki, score));
                }
            }
            pairs.Sort((a, b) => b.score.CompareTo(a.score));

            var heatUsed = new bool[heats.Count];
            var kutuUsed = new bool[kutular.Count];
            var assign = new int[heats.Count];
            for (int i = 0; i < assign.Length; i++) assign[i] = -1;

            for (int p = 0; p < pairs.Count; p++)
            {
                int hi = pairs[p].hi, ki = pairs[p].ki;
                if (heatUsed[hi] || kutuUsed[ki]) continue;
                heatUsed[hi] = true;
                kutuUsed[ki] = true;
                assign[hi] = ki;
            }

            string yon = isY ? "Y" : "X";
            for (int hi = 0; hi < heats.Count; hi++)
            {
                var kutu = default(KutuInfo);
                if (assign[hi] >= 0)
                {
                    KutuBox box = kutular[assign[hi]];
                    kutu.Found = true;
                    kutu.MinX = box.MinX; kutu.MaxX = box.MaxX;
                    kutu.MinY = box.MinY; kutu.MaxY = box.MaxY;
                    kutu.CapMm = box.CapMm;
                    kutu.AralikCm = box.AralikCm;
                    kutu.AdetLabel = box.AdetLabel;
                    kutu.Label = box.Label;
                }
                else
                    ed.WriteMessage("\n  Uyarı: ısı{0}[{1}] için eşleşen KUTU {0} yok.", yon, hi + 1);

                jobs.Add(new HeatJob
                {
                    PolyId = heats[hi].id,
                    KutuPolyId = assign[hi] >= 0 ? kutular[assign[hi]].Id : ObjectId.Null,
                    KutuLabelId = assign[hi] >= 0 ? kutular[assign[hi]].LabelId : ObjectId.Null,
                    Ring = heats[hi].ring,
                    Ext = heats[hi].ext,
                    Kutu = kutu,
                    IsY = isY
                });
            }
        }

        /// <summary>Isıyı kapsayan / merkezini içeren kutu; daha dar kutu daha yüksek skor.</summary>
        private static double ScoreHeatToKutu(Extents3d heat, KutuBox box)
        {
            double hx0 = heat.MinPoint.X, hx1 = heat.MaxPoint.X;
            double hy0 = heat.MinPoint.Y, hy1 = heat.MaxPoint.Y;
            double hcx = 0.5 * (hx0 + hx1), hcy = 0.5 * (hy0 + hy1);
            double bx0 = box.MinX, bx1 = box.MaxX, by0 = box.MinY, by1 = box.MaxY;
            double kutuArea = Math.Max(1.0, (bx1 - bx0) * (by1 - by0));

            double ox0 = Math.Max(hx0, bx0), ox1 = Math.Min(hx1, bx1);
            double oy0 = Math.Max(hy0, by0), oy1 = Math.Min(hy1, by1);
            double overlap = (ox1 > ox0 && oy1 > oy0) ? (ox1 - ox0) * (oy1 - oy0) : 0;
            if (overlap < 1e-6) return -1;

            bool containsHeat = hx0 >= bx0 - 2 && hx1 <= bx1 + 2 && hy0 >= by0 - 2 && hy1 <= by1 + 2;
            bool containsCenter = hcx >= bx0 && hcx <= bx1 && hcy >= by0 && hcy <= by1;
            if (!containsHeat && !containsCenter) return -1;

            // Daha dar kapsayan kutu kazanır (çoklu seçimde karışmayı önler)
            double tightness = 1e6 / kutuArea;
            if (containsHeat) return 1e12 + tightness + overlap;
            return 1e9 + tightness + overlap;
        }

        private static void CollectKutuBoxes(
            Transaction tr, Database db, List<KutuBox> kutular, string kutuLayer, bool labelNearTopLeft)
        {
            kutular.Clear();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            var boxes = new List<(ObjectId id, double x0, double x1, double y0, double y1)>();
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (!(ent is Polyline pl) || !pl.Closed || pl.NumberOfVertices < 3) continue;
                if (!LayerEquals(pl.Layer, kutuLayer)) continue;
                Extents3d be;
                try { be = pl.GeometricExtents; } catch { continue; }
                double bx0 = be.MinPoint.X, bx1 = be.MaxPoint.X;
                double by0 = be.MinPoint.Y, by1 = be.MaxPoint.Y;
                if (bx1 - bx0 < 8 || by1 - by0 < 8) continue;
                boxes.Add((id, bx0, bx1, by0, by1));
            }

            var texts = new List<(ObjectId id, string text, Point3d pos)>();
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                string text = null;
                Point3d pos = default;
                if (ent is DBText dbText)
                {
                    if (!LayerEquals(dbText.Layer, kutuLayer)) continue;
                    text = dbText.TextString;
                    pos = dbText.Position;
                }
                else if (ent is MText mt)
                {
                    if (!LayerEquals(mt.Layer, kutuLayer)) continue;
                    text = mt.Contents;
                    pos = mt.Location;
                }
                else continue;
                if (string.IsNullOrWhiteSpace(text) || !DonatiYaziRx.IsMatch(text)) continue;
                texts.Add((id, text, pos));
            }

            var textAssign = new int[boxes.Count];
            for (int i = 0; i < textAssign.Length; i++) textAssign[i] = -1;
            var textPairs = new List<(int bi, int ti, double dist)>();
            for (int bi = 0; bi < boxes.Count; bi++)
            {
                var b = boxes[bi];
                for (int ti = 0; ti < texts.Count; ti++)
                {
                    Point3d pos = texts[ti].pos;
                    if (pos.X < b.x0 - 80 || pos.X > b.x1 + 80) continue;
                    if (pos.Y < b.y0 - 50) continue;
                    double dist;
                    if (labelNearTopLeft)
                    {
                        // Y kutu: sol üst
                        dist = Math.Abs(pos.Y - b.y1) + Math.Abs(pos.X - b.x0);
                        if (pos.X <= b.x0 + 0.4 * (b.x1 - b.x0) && pos.Y >= b.y1 - 150)
                            dist -= 500;
                    }
                    else
                    {
                        // X kutu: üst kenar ortası
                        dist = Math.Abs(pos.Y - b.y1)
                             + 0.02 * Math.Abs(pos.X - 0.5 * (b.x0 + b.x1));
                        if (pos.X >= b.x0 - 5 && pos.X <= b.x1 + 5 && pos.Y <= b.y1 + 120)
                            dist -= 500;
                    }
                    textPairs.Add((bi, ti, dist));
                }
            }
            textPairs.Sort((a, b) => a.dist.CompareTo(b.dist));
            var textUsed = new bool[texts.Count];
            var boxHasText = new bool[boxes.Count];
            for (int p = 0; p < textPairs.Count; p++)
            {
                int bi = textPairs[p].bi, ti = textPairs[p].ti;
                if (boxHasText[bi] || textUsed[ti]) continue;
                if (textPairs[p].dist > 1e7) continue;
                boxHasText[bi] = true;
                textUsed[ti] = true;
                textAssign[bi] = ti;
            }

            for (int bi = 0; bi < boxes.Count; bi++)
            {
                var b = boxes[bi];
                var box = new KutuBox
                {
                    Id = b.id,
                    MinX = b.x0, MaxX = b.x1,
                    MinY = b.y0, MaxY = b.y1
                };
                int ti = textAssign[bi];
                if (ti >= 0)
                {
                    box.LabelId = texts[ti].id;
                    box.Label = texts[ti].text;
                    box.HasLabel = true;
                    Match m = DonatiYaziRx.Match(box.Label);
                    if (m.Success)
                    {
                        int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out box.AdetLabel);
                        int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out box.CapMm);
                        double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out box.AralikCm);
                    }
                }
                kutular.Add(box);
            }
        }

        /// <summary>
        /// Çizim sonrası: ısı konturu + eşleşen KUTU + kutu donatı yazısı silinir.
        /// Stok bantlar (CIZGI) kalır; ana dış çerçeve zaten çizilmez.
        /// </summary>
        private static void EraseHeatAndKutuAfterDraw(
            Document doc, Database db, Editor ed, List<HeatJob> drawnJobs)
        {
            if (drawnJobs == null || drawnJobs.Count == 0) return;
            var toErase = new HashSet<ObjectId>();
            for (int i = 0; i < drawnJobs.Count; i++)
            {
                if (!drawnJobs[i].PolyId.IsNull) toErase.Add(drawnJobs[i].PolyId);
                if (!drawnJobs[i].KutuPolyId.IsNull) toErase.Add(drawnJobs[i].KutuPolyId);
                if (!drawnJobs[i].KutuLabelId.IsNull) toErase.Add(drawnJobs[i].KutuLabelId);
            }
            if (toErase.Count == 0) return;

            int erased = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in toErase)
                {
                    if (id.IsNull || id.IsErased) continue;
                    try
                    {
                        var ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                        if (ent == null || ent.IsErased) continue;
                        ent.Erase();
                        erased++;
                    }
                    catch { }
                }
                tr.Commit();
            }
            ed.WriteMessage("\n  Temizlik: {0} ısı/kutu/yazı silindi.", erased);
        }

        private static int DrawForJob(
            Document doc, Database db, Editor ed, HeatJob job,
            List<(double x0, double x1, double y0, double y1)> obstacles,
            List<PendingDonatiLabel> pendingLabels)
        {
            Extents3d ext = job.Ext;
            List<Point2d> ring = job.Ring;
            KutuInfo kutu = job.Kutu;
            bool isY = job.IsY;
            string yon = isY ? "Y" : "X";

            if (ring == null || ring.Count < 3) return -1;

            double aralik = kutu.Found && kutu.AralikCm >= 1.0
                ? kutu.AralikCm
                : RadyeTemelDonatiSettings.AralikCm;
            if (aralik < 1.0) aralik = 15.0;
            int capMm = kutu.Found && kutu.CapMm > 0 ? kutu.CapMm : 14;

            if (kutu.Found && !string.IsNullOrEmpty(kutu.Label))
            {
                ed.WriteMessage(
                    "\n  KUTU {0} yazı='{1}' → Ø{2}/{3}",
                    yon, kutu.Label, capMm,
                    aralik.ToString("0.##", CultureInfo.InvariantCulture));
            }
            else if (kutu.Found)
                ed.WriteMessage("\n  KUTU {0} bulundu ama yazı yok; form aralığı={1:0.##}", yon, aralik);
            else
                ed.WriteMessage("\n  Uyarı: KUTU {0} yok; form aralığı={1:0.##}", yon, aralik);

            CoverRect outer = BuildCoverRect(ext, aralik, isY);
            ed.WriteMessage(
                "\n  Dış çerçeve {0}: W={1:0.##} H={2:0.##} (ısı W={3:0.##} H={4:0.##}, aralık={5:0.##})",
                yon, outer.Width, outer.Height,
                ext.MaxPoint.X - ext.MinPoint.X,
                ext.MaxPoint.Y - ext.MinPoint.Y,
                aralik);

            List<CoverRect> bands = isY
                ? BuildHeatBandsY(ring, outer, aralik)
                : BuildHeatBandsX(ring, outer, aralik);
            ed.WriteMessage("\n  Bant sayısı={0}", bands.Count);

            double stockRef = isY ? outer.Height : outer.Width;
            double paspayi = RadyeTemelDonatiSettings.PaspayiCm;
            if (paspayi < 0) paspayi = 0;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                string donatiLyr = EnsureLayer(tr, db, LayerDonati, 4, LineWeight.LineWeight040);
                string yaziLyr = EnsureLayer(tr, db, LayerDonatiYazi, 3, LineWeight.LineWeight020);

                var temelRings = new List<List<Point2d>>();
                CollectTemelClosedRings(tr, db, temelRings);

                // Ana dış çerçeve (outer) çizilmez — yalnız stok bantlar kalır.
                ObjectId yaziStyleId = GetOrCreateYaziBeykentTextStyle(tr, db);
                string okuLyr = EnsureLayer(tr, db, LayerDonatiOku, DonatiOkuAci, LineWeight.LineWeight020);
                string halkaLyr = EnsureDonatiHalkasiLayer(tr, db);
                ObjectId okuDimStyleId = EnsureDonatiOkuDimStyle(tr, db, yaziStyleId);
                if (obstacles == null)
                    obstacles = new List<(double x0, double x1, double y0, double y1)>();
                if (pendingLabels == null)
                    pendingLabels = new List<PendingDonatiLabel>();

                var barSegs = new List<DonatiOkuSeg>();
                var pitEdgeCoords = new List<double>(); // çukur/TEMEL kenarları (ölçü ekseni)

                for (int b = 0; b < bands.Count; b++)
                {
                    CoverRect band = bands[b];
                    // Stok bant dikdörtgeni çizilmez — yalnız donatı + ölçü

                    double adetAxis = isY ? band.Width : band.Height;
                    int adetFull = AdetInBand(adetAxis, aralik);
                    List<Point2d> outerTemel = FindOuterTemel(temelRings, band);
                    var pits = FindPitsInBand(temelRings, band, outerTemel);
                    CollectPitEdgesForOku(pits, band, isY, pitEdgeCoords);
                    bool doGonye = RadyeTemelDonatiSettings.IlaveGonye;
                    double gonyeLen = RadyeTemelDonatiSettings.EffectiveGonyeBoyuCm;
                    bool isUst = RadyeTemelDonatiSettings.IlaveUst;
                    int nBars = isY
                        ? DrawBandBarsWithPitsY(tr, db, btr, donatiLyr, yaziLyr, yaziStyleId,
                            band, outerTemel, pits, paspayi, adetFull, capMm, aralik,
                            doGonye, gonyeLen, isUst, obstacles, pendingLabels, barSegs)
                        : DrawBandBarsWithPitsX(tr, db, btr, donatiLyr, yaziLyr, yaziStyleId,
                            band, outerTemel, pits, paspayi, adetFull, capMm, aralik,
                            doGonye, gonyeLen, isUst, obstacles, pendingLabels, barSegs);

                    ed.WriteMessage(
                        "\n    [{0}] dışTEMEL={1} çukur={2} çubuk={3}  (W={4:0.##} H={5:0.##})",
                        b + 1, outerTemel != null ? 1 : 0, pits.Count, nBars,
                        band.Width, band.Height);
                }

                DrawDonatiOkuAcikliklari(
                    tr, btr, outer, isY, bands, barSegs, pitEdgeCoords,
                    pendingLabels, okuLyr, halkaLyr, okuDimStyleId);

                tr.Commit();
            }

            return bands.Count;
        }

        /// <summary>Bant yüksekliği / aralık → uçlar dahil adet (⌊H/s⌋+1) — etiket için.</summary>
        private static int AdetInBand(double bandHeightCm, double aralikCm)
        {
            double s = aralikCm > 0.5 ? aralikCm : 15.0;
            if (bandHeightCm < 0.5) return 1;
            return Math.Max(1, (int)Math.Floor(bandHeightCm / s + 1e-9) + 1);
        }

        private static string FormatBandLabel(int adet, int capMm, double aralikCm, double boyCm)
        {
            int boy = (int)Math.Round(boyCm);
            int ar = (int)Math.Round(aralikCm);
            return string.Format(CultureInfo.InvariantCulture,
                "{0}ø{1}/{2} L={3} {4}",
                adet, capMm, ar, boy, RadyeTemelDonatiSettings.IlaveKatEtiketi);
        }

        private static void CollectTemelClosedRings(
            Transaction tr, Database db, List<List<Point2d>> rings)
        {
            rings.Clear();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (!(ent is Polyline pl) || pl.NumberOfVertices < 3) continue;
                if (!LayerEquals(pl.Layer, LayerTemel)) continue;
                List<Point2d> r = ExtractRing(pl);
                EnsureClosed(r);
                if (r.Count < 3) continue;
                bool closed = pl.Closed;
                if (!closed)
                {
                    Point2d a = r[0], b = r[r.Count - 1];
                    closed = Math.Abs(a.X - b.X) < 1.0 && Math.Abs(a.Y - b.Y) < 1.0;
                }
                if (!closed && r.Count < 4) continue;
                // Alan çok küçükse yok say (çizgi artığı)
                double area = 0;
                try { area = Math.Abs(pl.Area); } catch { area = PolygonAreaCm2(r); }
                if (area < 100) continue;
                rings.Add(r);
            }
        }

        /// <summary>Bant merkezini içeren en büyük TEMEL poligon = dış sınır.</summary>
        private static List<Point2d> FindOuterTemel(List<List<Point2d>> temelRings, CoverRect band)
        {
            if (temelRings == null || temelRings.Count == 0) return null;
            Point2d bandCtr = new Point2d(0.5 * (band.X0 + band.X1), 0.5 * (band.Y0 + band.Y1));
            List<Point2d> outer = null;
            double outerArea = -1;
            for (int i = 0; i < temelRings.Count; i++)
            {
                if (!PointInPolygon(bandCtr, temelRings[i])) continue;
                double a = PolygonAreaCm2(temelRings[i]);
                if (a > outerArea)
                {
                    outerArea = a;
                    outer = temelRings[i];
                }
            }
            return outer;
        }

        /// <summary>
        /// Bantla kesişen, dış sınır olmayan TEMEL poligonları = çukur.
        /// </summary>
        private static List<List<Point2d>> FindPitsInBand(
            List<List<Point2d>> temelRings, CoverRect band, List<Point2d> outer)
        {
            var pits = new List<List<Point2d>>();
            if (temelRings == null || temelRings.Count == 0) return pits;

            for (int i = 0; i < temelRings.Count; i++)
            {
                var ring = temelRings[i];
                if (outer != null && ReferenceEquals(ring, outer)) continue;
                if (!AabbOverlapsBand(ring, band)) continue;
                if (BandCornersInside(ring, band)) continue;
                if (!RingActuallyHitsBand(ring, band)) continue;
                pits.Add(ring);
            }
            return pits;
        }

        /// <summary>
        /// Çukur AABB kenarları — X bantta Y0/Y1, Y bantta X0/X1 (banda kırpılmış).
        /// Ölçüyü yukarı→çukur→alt diye böler.
        /// </summary>
        private static void CollectPitEdgesForOku(
            List<List<Point2d>> pits, CoverRect band, bool isY, List<double> edgeCoords)
        {
            if (pits == null || edgeCoords == null) return;
            for (int i = 0; i < pits.Count; i++)
            {
                if (!TryRingAabb(pits[i], out double px0, out double px1, out double py0, out double py1))
                    continue;
                if (isY)
                {
                    // Düşey çubuk / yatay ölçü → çukur X kenarları
                    double a = Math.Max(band.X0, px0);
                    double b = Math.Min(band.X1, px1);
                    if (b - a < 1e-3) continue;
                    AddUniqueFixed(edgeCoords, a);
                    AddUniqueFixed(edgeCoords, b);
                }
                else
                {
                    // Yatay çubuk / düşey ölçü → çukur Y kenarları
                    double a = Math.Max(band.Y0, py0);
                    double b = Math.Min(band.Y1, py1);
                    if (b - a < 1e-3) continue;
                    AddUniqueFixed(edgeCoords, a);
                    AddUniqueFixed(edgeCoords, b);
                }
            }
        }

        /// <summary>Köşe / kenar örneklemesi ile bant–poligon gerçek kesişim.</summary>
        private static bool RingActuallyHitsBand(List<Point2d> ring, CoverRect band)
        {
            // Bant köşesi çukurda
            if (PointInPolygon(new Point2d(band.X0, band.Y0), ring)
                || PointInPolygon(new Point2d(band.X1, band.Y0), ring)
                || PointInPolygon(new Point2d(band.X1, band.Y1), ring)
                || PointInPolygon(new Point2d(band.X0, band.Y1), ring))
                return true;

            // Çukur köşesi bantta
            for (int i = 0; i < ring.Count; i++)
            {
                Point2d p = ring[i];
                if (p.X >= band.X0 && p.X <= band.X1 && p.Y >= band.Y0 && p.Y <= band.Y1)
                    return true;
            }

            // Yatay kiriş: bant Y aralığında çukur kirişi bant X ile örtüşüyor mu?
            double[] ys =
            {
                band.Y0, band.Y1, 0.5 * (band.Y0 + band.Y1),
                band.Y0 + 0.25 * (band.Y1 - band.Y0),
                band.Y0 + 0.75 * (band.Y1 - band.Y0)
            };
            for (int i = 0; i < ys.Length; i++)
            {
                var chords = HorizontalChords(ring, ys[i]);
                for (int c = 0; c < chords.Count; c++)
                {
                    if (chords[c].b > band.X0 && chords[c].a < band.X1)
                        return true;
                }
            }
            return false;
        }

        private static bool AabbOverlapsBand(List<Point2d> ring, CoverRect band)
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            for (int i = 0; i < ring.Count; i++)
            {
                if (ring[i].X < minX) minX = ring[i].X;
                if (ring[i].X > maxX) maxX = ring[i].X;
                if (ring[i].Y < minY) minY = ring[i].Y;
                if (ring[i].Y > maxY) maxY = ring[i].Y;
            }
            return maxX >= band.X0 && minX <= band.X1 && maxY >= band.Y0 && minY <= band.Y1;
        }

        private static bool BandCornersInside(List<Point2d> ring, CoverRect band)
        {
            return PointInPolygon(new Point2d(band.X0, band.Y0), ring)
                && PointInPolygon(new Point2d(band.X1, band.Y0), ring)
                && PointInPolygon(new Point2d(band.X1, band.Y1), ring)
                && PointInPolygon(new Point2d(band.X0, band.Y1), ring);
        }

        private static bool PointInPolygon(Point2d p, List<Point2d> ring)
        {
            bool inside = false;
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double yi = ring[i].Y, yj = ring[j].Y;
                double xi = ring[i].X, xj = ring[j].X;
                if (((yi > p.Y) != (yj > p.Y)) &&
                    (p.X < (xj - xi) * (p.Y - yi) / (yj - yi + 1e-15) + xi))
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// Kolon/perde/isim + mevcut DONATI çizgi/yazıları (şişirilmiş) — etiket/çubuk kaydırma engeli.
        /// </summary>
        private static void CollectVisualObstacles(
            Transaction tr, Database db,
            List<(double x0, double x1, double y0, double y1)> boxes)
        {
            boxes.Clear();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            double pad = ObstacleClearCm;
            double donatiPad = Math.Max(8.0, ObstacleClearCm * 0.75);

            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                string lyr = ent.Layer ?? "";

                if (ent is Polyline pl && pl.NumberOfVertices >= 2)
                {
                    if (LayerEquals(lyr, LayerDonati))
                    {
                        // Dik geçen donatı çizgisi: etiket bundan uzak dursun
                        AddPolylineAsObstacle(boxes, pl, donatiPad);
                        continue;
                    }
                    if (pl.NumberOfVertices >= 3
                        && (LayerEquals(lyr, LayerKolon)
                            || LayerEquals(lyr, LayerPerde)
                            || LayerEquals(lyr, LayerKapama)))
                    {
                        Extents3d e;
                        try { e = pl.GeometricExtents; } catch { continue; }
                        boxes.Add((
                            e.MinPoint.X - pad, e.MaxPoint.X + pad,
                            e.MinPoint.Y - pad, e.MaxPoint.Y + pad));
                    }
                    continue;
                }

                if (LayerEquals(lyr, LayerKolonIsmi)
                    || LayerEquals(lyr, LayerPerdeIsmi)
                    || LayerEquals(lyr, LayerKapamaIsmi)
                    || LayerEquals(lyr, LayerDonatiYazi))
                {
                    Extents3d te;
                    try { te = ent.GeometricExtents; } catch { continue; }
                    boxes.Add((
                        te.MinPoint.X - pad, te.MaxPoint.X + pad,
                        te.MinPoint.Y - pad, te.MaxPoint.Y + pad));
                }
            }
        }

        private static void AddPolylineAsObstacle(
            List<(double x0, double x1, double y0, double y1)> boxes,
            Polyline pl, double pad)
        {
            if (boxes == null || pl == null || pl.NumberOfVertices < 2) return;
            // Segment bazlı ince şerit — gönye köşelerinde de çalışır
            for (int i = 0; i < pl.NumberOfVertices - 1; i++)
            {
                Point2d a = pl.GetPoint2dAt(i);
                Point2d b = pl.GetPoint2dAt(i + 1);
                double x0 = Math.Min(a.X, b.X) - pad;
                double x1 = Math.Max(a.X, b.X) + pad;
                double y0 = Math.Min(a.Y, b.Y) - pad;
                double y1 = Math.Max(a.Y, b.Y) + pad;
                // Neredeyse nokta (çok kısa) atla
                if (x1 - x0 < pad * 0.5 && y1 - y0 < pad * 0.5) continue;
                boxes.Add((x0, x1, y0, y1));
            }
            if (pl.Closed && pl.NumberOfVertices >= 3)
            {
                Point2d a = pl.GetPoint2dAt(pl.NumberOfVertices - 1);
                Point2d b = pl.GetPoint2dAt(0);
                boxes.Add((
                    Math.Min(a.X, b.X) - pad, Math.Max(a.X, b.X) + pad,
                    Math.Min(a.Y, b.Y) - pad, Math.Max(a.Y, b.Y) + pad));
            }
        }

        /// <summary>Yeni çizilen donatı gövdesini engel listesine ekle (sonraki etiketler kaçsın).</summary>
        private static void AddBarSegmentObstacle(
            List<(double x0, double x1, double y0, double y1)> boxes,
            bool horizontal, double along0, double along1, double fixedCoord)
        {
            if (boxes == null) return;
            double pad = Math.Max(8.0, ObstacleClearCm * 0.75);
            double a0 = Math.Min(along0, along1);
            double a1 = Math.Max(along0, along1);
            if (horizontal)
                boxes.Add((a0 - pad, a1 + pad, fixedCoord - pad, fixedCoord + pad));
            else
                boxes.Add((fixedCoord - pad, fixedCoord + pad, a0 - pad, a1 + pad));
        }

        /// <summary>
        /// X: biraz yukarı/aşağı; Y: biraz sağa/sola — boy/şekil aynı, engelden uzak hiza.
        /// </summary>
        private static double PickCleanFixedCoord(
            double preferred, double lo, double hi,
            bool horizontalBar, double along0, double along1,
            List<(double x0, double x1, double y0, double y1)> obstacles)
        {
            if (hi < lo + 1.0) return preferred;
            preferred = Math.Max(lo, Math.Min(hi, preferred));

            double best = preferred;
            double bestScore = ScoreLineVsObstacles(
                preferred, horizontalBar, along0, along1, obstacles);
            if (bestScore < 1e-3) return best;

            int nSteps = Math.Max(1, (int)Math.Ceiling((hi - lo) / CleanShiftStepCm));
            for (int i = 1; i <= nSteps; i++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    double c = preferred + s * i * CleanShiftStepCm;
                    if (c < lo - 1e-6 || c > hi + 1e-6) continue;
                    c = Math.Max(lo, Math.Min(hi, c));
                    double sc = ScoreLineVsObstacles(c, horizontalBar, along0, along1, obstacles);
                    if (sc + 1e-6 < bestScore
                        || (Math.Abs(sc - bestScore) < 1e-6 && Math.Abs(c - preferred) < Math.Abs(best - preferred)))
                    {
                        bestScore = sc;
                        best = c;
                        if (bestScore < 1e-3) return best;
                    }
                }
            }
            return best;
        }

        /// <summary>Çubuğun engel kutularıyla örtüşen uzunluğu (küçük = temiz).</summary>
        private static double ScoreLineVsObstacles(
            double fixedCoord, bool horizontalBar, double along0, double along1,
            List<(double x0, double x1, double y0, double y1)> obstacles)
        {
            if (obstacles == null || obstacles.Count == 0) return 0;
            double a0 = Math.Min(along0, along1);
            double a1 = Math.Max(along0, along1);
            double score = 0;
            for (int i = 0; i < obstacles.Count; i++)
            {
                var o = obstacles[i];
                if (horizontalBar)
                {
                    if (fixedCoord < o.y0 || fixedCoord > o.y1) continue;
                    double ox0 = Math.Max(a0, o.x0);
                    double ox1 = Math.Min(a1, o.x1);
                    if (ox1 > ox0) score += ox1 - ox0;
                }
                else
                {
                    if (fixedCoord < o.x0 || fixedCoord > o.x1) continue;
                    double oy0 = Math.Max(a0, o.y0);
                    double oy1 = Math.Min(a1, o.y1);
                    if (oy1 > oy0) score += oy1 - oy0;
                }
            }
            return score;
        }

        /// <summary>
        /// X bant: çukur/TEMEL trim (boy aynı); Y hizasını kolon/etiketten uzak seç.
        /// </summary>
        private static int DrawBandBarsWithPitsX(
            Transaction tr, Database db, BlockTableRecord btr,
            string donatiLyr, string yaziLyr, ObjectId yaziStyleId,
            CoverRect band, List<Point2d> outer, List<List<Point2d>> pits, double paspayi,
            int adetFull, int capMm, double aralik,
            bool doGonye, double gonyeLen, bool isUst,
            List<(double x0, double x1, double y0, double y1)> obstacles,
            List<PendingDonatiLabel> pendingLabels,
            List<DonatiOkuSeg> barSegs)
        {
            if (band.Width < MinBarLengthCm * 0.25) return 0;

            var pitYs = new List<(double a, double b)>();
            if (pits != null)
            {
                for (int i = 0; i < pits.Count; i++)
                {
                    if (!TryRingAabb(pits[i], out double _, out double __, out double py0, out double py1))
                        continue;
                    double y0 = Math.Max(band.Y0, py0);
                    double y1 = Math.Min(band.Y1, py1);
                    if (y1 - y0 > 1e-3) pitYs.Add((y0, y1));
                }
            }
            pitYs = MergeIntervals(pitYs);

            int nBars = 0;
            if (pitYs.Count == 0)
            {
                double yPref = 0.5 * (band.Y0 + band.Y1);
                double y = PickCleanFixedCoord(
                    yPref, band.Y0, band.Y1, horizontalBar: true,
                    band.X0, band.X1, obstacles);
                return DrawClippedHorizontalBars(
                    tr, db, btr, donatiLyr, yaziLyr, yaziStyleId,
                    band, outer, pits, paspayi, y, adetFull, capMm, aralik,
                    doGonye, gonyeLen, isUst, obstacles, pendingLabels, barSegs);
            }

            var freeYs = ComplementIntervals(band.Y0, band.Y1, pitYs);
            double minFreeH = Math.Max(5.0, aralik * 0.5);

            for (int i = 0; i < freeYs.Count; i++)
            {
                double h = freeYs[i].b - freeYs[i].a;
                if (h < minFreeH) continue;
                int adet = AdetInBand(h, aralik);
                double yPref = 0.5 * (freeYs[i].a + freeYs[i].b);
                double y = PickCleanFixedCoord(
                    yPref, freeYs[i].a, freeYs[i].b, horizontalBar: true,
                    band.X0, band.X1, obstacles);
                nBars += DrawClippedHorizontalBars(
                    tr, db, btr, donatiLyr, yaziLyr, yaziStyleId,
                    band, outer, pits, paspayi, y, adet, capMm, aralik,
                    doGonye, gonyeLen, isUst, obstacles, pendingLabels, barSegs);
            }

            for (int i = 0; i < pitYs.Count; i++)
            {
                double hPit = pitYs[i].b - pitYs[i].a;
                if (hPit < 1.0) continue;
                int adet = AdetInBand(hPit, aralik);
                double yPref = 0.5 * (pitYs[i].a + pitYs[i].b);
                double y = PickCleanFixedCoord(
                    yPref, pitYs[i].a, pitYs[i].b, horizontalBar: true,
                    band.X0, band.X1, obstacles);
                nBars += DrawClippedHorizontalBars(
                    tr, db, btr, donatiLyr, yaziLyr, yaziStyleId,
                    band, outer, pits, paspayi, y, adet, capMm, aralik,
                    doGonye, gonyeLen, isUst, obstacles, pendingLabels, barSegs);
            }
            return nBars;
        }

        /// <summary>
        /// Y bant: çukur/TEMEL trim (boy aynı); X hizasını kolon/etiketten uzak seç.
        /// </summary>
        private static int DrawBandBarsWithPitsY(
            Transaction tr, Database db, BlockTableRecord btr,
            string donatiLyr, string yaziLyr, ObjectId yaziStyleId,
            CoverRect band, List<Point2d> outer, List<List<Point2d>> pits, double paspayi,
            int adetFull, int capMm, double aralik,
            bool doGonye, double gonyeLen, bool isUst,
            List<(double x0, double x1, double y0, double y1)> obstacles,
            List<PendingDonatiLabel> pendingLabels,
            List<DonatiOkuSeg> barSegs)
        {
            if (band.Height < MinBarLengthCm * 0.25) return 0;

            var pitXs = new List<(double a, double b)>();
            if (pits != null)
            {
                for (int i = 0; i < pits.Count; i++)
                {
                    if (!TryRingAabb(pits[i], out double px0, out double px1, out double _, out double __))
                        continue;
                    double x0 = Math.Max(band.X0, px0);
                    double x1 = Math.Min(band.X1, px1);
                    if (x1 - x0 > 1e-3) pitXs.Add((x0, x1));
                }
            }
            pitXs = MergeIntervals(pitXs);

            int nBars = 0;
            if (pitXs.Count == 0)
            {
                double xPref = 0.5 * (band.X0 + band.X1);
                double x = PickCleanFixedCoord(
                    xPref, band.X0, band.X1, horizontalBar: false,
                    band.Y0, band.Y1, obstacles);
                return DrawClippedVerticalBars(
                    tr, db, btr, donatiLyr, yaziLyr, yaziStyleId,
                    band, outer, pits, paspayi, x, adetFull, capMm, aralik,
                    doGonye, gonyeLen, isUst, obstacles, pendingLabels, barSegs);
            }

            var freeXs = ComplementIntervals(band.X0, band.X1, pitXs);
            double minFreeW = Math.Max(5.0, aralik * 0.5);

            for (int i = 0; i < freeXs.Count; i++)
            {
                double w = freeXs[i].b - freeXs[i].a;
                if (w < minFreeW) continue;
                int adet = AdetInBand(w, aralik);
                double xPref = 0.5 * (freeXs[i].a + freeXs[i].b);
                double x = PickCleanFixedCoord(
                    xPref, freeXs[i].a, freeXs[i].b, horizontalBar: false,
                    band.Y0, band.Y1, obstacles);
                nBars += DrawClippedVerticalBars(
                    tr, db, btr, donatiLyr, yaziLyr, yaziStyleId,
                    band, outer, pits, paspayi, x, adet, capMm, aralik,
                    doGonye, gonyeLen, isUst, obstacles, pendingLabels, barSegs);
            }

            for (int i = 0; i < pitXs.Count; i++)
            {
                double wPit = pitXs[i].b - pitXs[i].a;
                if (wPit < 1.0) continue;
                int adet = AdetInBand(wPit, aralik);
                double xPref = 0.5 * (pitXs[i].a + pitXs[i].b);
                double x = PickCleanFixedCoord(
                    xPref, pitXs[i].a, pitXs[i].b, horizontalBar: false,
                    band.Y0, band.Y1, obstacles);
                nBars += DrawClippedVerticalBars(
                    tr, db, btr, donatiLyr, yaziLyr, yaziStyleId,
                    band, outer, pits, paspayi, x, adet, capMm, aralik,
                    doGonye, gonyeLen, isUst, obstacles, pendingLabels, barSegs);
            }
            return nBars;
        }

        private static int DrawClippedHorizontalBars(
            Transaction tr, Database db, BlockTableRecord btr,
            string donatiLyr, string yaziLyr, ObjectId yaziStyleId,
            CoverRect band, List<Point2d> outer, List<List<Point2d>> pits, double paspayi,
            double y, int adet, int capMm, double aralik,
            bool doGonye, double gonyeLen, bool isUst,
            List<(double x0, double x1, double y0, double y1)> obstacles,
            List<PendingDonatiLabel> pendingLabels,
            List<DonatiOkuSeg> barSegs)
        {
            var segs = ClipBarToTemelAndPits(
                outer, pits, horizontal: true, y, band.X0, band.X1, paspayi);
            int n = 0;
            for (int s = 0; s < segs.Count; s++)
            {
                double len = segs[s].b - segs[s].a;
                if (len < MinSegAfterTrimCm) continue;
                bool trimL = segs[s].a > band.X0 + 0.5;
                bool trimR = segs[s].b < band.X1 - 0.5;
                bool hookL = doGonye && trimL;
                bool hookR = doGonye && trimR;
                DrawHorizontalBar(tr, btr, donatiLyr, segs[s].a, segs[s].b, y,
                    hookL, hookR, gonyeLen, isUst);
                AddBarSegmentObstacle(obstacles, horizontal: true, segs[s].a, segs[s].b, y);
                if (barSegs != null)
                {
                    barSegs.Add(new DonatiOkuSeg
                    {
                        Along0 = segs[s].a,
                        Along1 = segs[s].b,
                        Fixed = y
                    });
                }
                if (pendingLabels != null)
                {
                    pendingLabels.Add(new PendingDonatiLabel
                    {
                        Along0 = segs[s].a,
                        Along1 = segs[s].b,
                        FixedCoord = y,
                        Text = FormatBandLabel(adet, capMm, aralik, len),
                        IsY = false,
                        Layer = yaziLyr,
                        TextStyleId = yaziStyleId
                    });
                }
                if (hookL)
                    PlaceIlaveGonyeLenLabel(tr, db, btr, yaziLyr, yaziStyleId,
                        segs[s].a, y, horizontalBar: true, isStart: true, gonyeLen, isUst);
                if (hookR)
                    PlaceIlaveGonyeLenLabel(tr, db, btr, yaziLyr, yaziStyleId,
                        segs[s].b, y, horizontalBar: true, isStart: false, gonyeLen, isUst);
                n++;
            }
            return n;
        }

        private static int DrawClippedVerticalBars(
            Transaction tr, Database db, BlockTableRecord btr,
            string donatiLyr, string yaziLyr, ObjectId yaziStyleId,
            CoverRect band, List<Point2d> outer, List<List<Point2d>> pits, double paspayi,
            double x, int adet, int capMm, double aralik,
            bool doGonye, double gonyeLen, bool isUst,
            List<(double x0, double x1, double y0, double y1)> obstacles,
            List<PendingDonatiLabel> pendingLabels,
            List<DonatiOkuSeg> barSegs)
        {
            var segs = ClipBarToTemelAndPits(
                outer, pits, horizontal: false, x, band.Y0, band.Y1, paspayi);
            int n = 0;
            for (int s = 0; s < segs.Count; s++)
            {
                double len = segs[s].b - segs[s].a;
                if (len < MinSegAfterTrimCm) continue;
                bool trimBot = segs[s].a > band.Y0 + 0.5;
                bool trimTop = segs[s].b < band.Y1 - 0.5;
                bool hookBot = doGonye && trimBot;
                bool hookTop = doGonye && trimTop;
                DrawVerticalBar(tr, btr, donatiLyr, x, segs[s].a, segs[s].b,
                    hookBot, hookTop, gonyeLen, isUst);
                AddBarSegmentObstacle(obstacles, horizontal: false, segs[s].a, segs[s].b, x);
                if (barSegs != null)
                {
                    barSegs.Add(new DonatiOkuSeg
                    {
                        Along0 = segs[s].a,
                        Along1 = segs[s].b,
                        Fixed = x
                    });
                }
                if (pendingLabels != null)
                {
                    pendingLabels.Add(new PendingDonatiLabel
                    {
                        Along0 = segs[s].a,
                        Along1 = segs[s].b,
                        FixedCoord = x,
                        Text = FormatBandLabel(adet, capMm, aralik, len),
                        IsY = true,
                        Layer = yaziLyr,
                        TextStyleId = yaziStyleId
                    });
                }
                if (hookBot)
                    PlaceIlaveGonyeLenLabel(tr, db, btr, yaziLyr, yaziStyleId,
                        x, segs[s].a, horizontalBar: false, isStart: true, gonyeLen, isUst);
                if (hookTop)
                    PlaceIlaveGonyeLenLabel(tr, db, btr, yaziLyr, yaziStyleId,
                        x, segs[s].b, horizontalBar: false, isStart: false, gonyeLen, isUst);
                n++;
            }
            return n;
        }

        private static void FlushPendingDonatiLabels(
            Document doc, Database db,
            List<PendingDonatiLabel> pending,
            List<(double x0, double x1, double y0, double y1)> obstacles)
        {
            if (pending == null || pending.Count == 0) return;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                for (int i = 0; i < pending.Count; i++)
                {
                    PendingDonatiLabel p = pending[i];
                    PlaceBandDonatiLabel(tr, db, btr, p.Layer, p.TextStyleId,
                        p.Along0, p.Along1, p.FixedCoord, p.Text, p.IsY, obstacles);
                }
                tr.Commit();
            }
        }

        /// <summary>
        /// İzinli dilimler = dış TEMEL kirişi − paspayı, ∩ [lo,hi], − (çukur + paspayı).
        /// </summary>
        private static List<(double a, double b)> ClipBarToTemelAndPits(
            List<Point2d> outer, List<List<Point2d>> pits,
            bool horizontal, double fixedCoord,
            double lo, double hi, double paspayi)
        {
            var allowed = new List<(double a, double b)>();
            if (outer != null && outer.Count >= 3)
            {
                var chords = horizontal
                    ? HorizontalChords(outer, fixedCoord)
                    : VerticalChords(outer, fixedCoord);
                for (int i = 0; i < chords.Count; i++)
                {
                    double a = Math.Max(lo, chords[i].a + paspayi);
                    double b = Math.Min(hi, chords[i].b - paspayi);
                    if (b - a >= MinSegAfterTrimCm)
                        allowed.Add((a, b));
                }
            }
            else if (hi - lo >= MinSegAfterTrimCm)
            {
                allowed.Add((lo, hi));
            }

            if (pits == null || pits.Count == 0)
                return allowed;

            var blocked = new List<(double a, double b)>();
            for (int p = 0; p < pits.Count; p++)
            {
                var ch = horizontal
                    ? HorizontalChords(pits[p], fixedCoord)
                    : VerticalChords(pits[p], fixedCoord);
                for (int c = 0; c < ch.Count; c++)
                {
                    double a = ch[c].a - paspayi;
                    double b = ch[c].b + paspayi;
                    if (b > a) blocked.Add((a, b));
                }
            }
            blocked = MergeIntervals(blocked);
            if (blocked.Count == 0) return allowed;

            var next = new List<(double a, double b)>();
            for (int i = 0; i < allowed.Count; i++)
                next.AddRange(SubtractIntervals(allowed[i].a, allowed[i].b, blocked));
            return next;
        }

        private const double IlaveGonyeLabelClearCm = 4.0;
        private const double IlaveGonyeLabelHeightCm = 8.0;
        private const double BendRadiusCm = 3.0;

        /// <summary>
        /// Gönye boyu yazısı (yazı kutusu gönyeye dik 4 cm dışarı).
        /// Yatay donatı → düşey yazı; düşey donatı → yatay yazı.
        /// </summary>
        private static void PlaceIlaveGonyeLenLabel(
            Transaction tr, Database db, BlockTableRecord btr, string layer,
            ObjectId textStyleId,
            double endS, double fixedCoord,
            bool horizontalBar, bool isStart,
            double gonyeLen, bool isUst)
        {
            if (gonyeLen < 0.5) return;
            string text = ((int)Math.Round(gonyeLen)).ToString(CultureInfo.InvariantCulture);
            Point2d tip = HookTip2d(
                horizontalBar ? new Point2d(endS, fixedCoord) : new Point2d(fixedCoord, endS),
                horizontalBar, isUst, gonyeLen);
            Point3d at = horizontalBar
                ? new Point3d(endS, 0.5 * (fixedCoord + tip.Y), 0)
                : new Point3d(0.5 * (fixedCoord + tip.X), endS, 0);

            double ang = horizontalBar ? Math.PI / 2.0 : 0.0;
            int outS = isStart ? -1 : 1;

            var t = new DBText();
            t.SetDatabaseDefaults();
            t.TextString = text;
            t.Height = IlaveGonyeLabelHeightCm;
            if (!textStyleId.IsNull)
                t.TextStyleId = textStyleId;
            t.Layer = layer;
            t.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            t.HorizontalMode = TextHorizontalMode.TextCenter;
            t.VerticalMode = TextVerticalMode.TextVerticalMid;
            t.AlignmentPoint = at;
            t.AdjustAlignment(db);
            btr.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);

            if (Math.Abs(ang) > 1e-12)
                t.TransformBy(Matrix3d.Rotation(ang, Vector3d.ZAxis, at));

            try
            {
                Extents3d ext = t.GeometricExtents;
                double dx = 0, dy = 0;
                if (horizontalBar)
                {
                    if (outS < 0)
                        dx = (endS - IlaveGonyeLabelClearCm) - ext.MaxPoint.X;
                    else
                        dx = (endS + IlaveGonyeLabelClearCm) - ext.MinPoint.X;
                    double cy = 0.5 * (ext.MinPoint.Y + ext.MaxPoint.Y);
                    dy = at.Y - cy;
                }
                else
                {
                    if (outS < 0)
                        dy = (endS - IlaveGonyeLabelClearCm) - ext.MaxPoint.Y;
                    else
                        dy = (endS + IlaveGonyeLabelClearCm) - ext.MinPoint.Y;
                    double cx = 0.5 * (ext.MinPoint.X + ext.MaxPoint.X);
                    dx = at.X - cx;
                }
                t.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
            }
            catch { }
        }

        private static bool TryRingAabb(
            List<Point2d> ring, out double minX, out double maxX, out double minY, out double maxY)
        {
            minX = double.MaxValue; maxX = double.MinValue;
            minY = double.MaxValue; maxY = double.MinValue;
            if (ring == null || ring.Count < 2) return false;
            for (int i = 0; i < ring.Count; i++)
            {
                if (ring[i].X < minX) minX = ring[i].X;
                if (ring[i].X > maxX) maxX = ring[i].X;
                if (ring[i].Y < minY) minY = ring[i].Y;
                if (ring[i].Y > maxY) maxY = ring[i].Y;
            }
            return maxX >= minX && maxY >= minY;
        }

        private static List<(double a, double b)> MergeIntervals(List<(double a, double b)> raw)
        {
            var list = new List<(double a, double b)>();
            if (raw == null || raw.Count == 0) return list;
            raw.Sort((u, v) => u.a.CompareTo(v.a));
            double ca = raw[0].a, cb = raw[0].b;
            for (int i = 1; i < raw.Count; i++)
            {
                if (raw[i].a <= cb + 1e-6)
                {
                    if (raw[i].b > cb) cb = raw[i].b;
                }
                else
                {
                    list.Add((ca, cb));
                    ca = raw[i].a;
                    cb = raw[i].b;
                }
            }
            list.Add((ca, cb));
            return list;
        }

        private static List<(double a, double b)> ComplementIntervals(
            double lo, double hi, List<(double a, double b)> blocked)
        {
            var free = new List<(double a, double b)>();
            double cur = lo;
            var m = MergeIntervals(new List<(double a, double b)>(blocked));
            for (int i = 0; i < m.Count; i++)
            {
                double a = Math.Max(lo, m[i].a);
                double b = Math.Min(hi, m[i].b);
                if (a > cur + 1e-6) free.Add((cur, a));
                if (b > cur) cur = b;
            }
            if (hi > cur + 1e-6) free.Add((cur, hi));
            return free;
        }

        private static List<(double a, double b)> SubtractIntervals(
            double lo, double hi, List<(double a, double b)> blocked)
            => ComplementIntervals(lo, hi, blocked);

        private static void DrawHorizontalBar(
            Transaction tr, BlockTableRecord btr, string layer,
            double x0, double x1, double y,
            bool hookStart, bool hookEnd, double gonyeLen, bool isUst)
        {
            bool useHook = gonyeLen > 0.5 && (hookStart || hookEnd);
            if (!useHook)
            {
                DrawHorizontalLine(tr, btr, layer, x0, x1, y);
                return;
            }

            var pts = new List<Point2d>();
            Point2d p0 = new Point2d(x0, y);
            Point2d p1 = new Point2d(x1, y);
            if (hookStart)
                pts.Add(HookTip2d(p0, horizontalBar: true, isUst, gonyeLen));
            pts.Add(p0);
            pts.Add(p1);
            if (hookEnd)
                pts.Add(HookTip2d(p1, horizontalBar: true, isUst, gonyeLen));
            PlacePolylineWithBends(tr, btr, layer, pts);
        }

        private static void DrawVerticalBar(
            Transaction tr, BlockTableRecord btr, string layer,
            double x, double y0, double y1,
            bool hookStart, bool hookEnd, double gonyeLen, bool isUst)
        {
            bool useHook = gonyeLen > 0.5 && (hookStart || hookEnd);
            if (!useHook)
            {
                DrawVerticalLine(tr, btr, layer, x, y0, y1);
                return;
            }

            var pts = new List<Point2d>();
            Point2d p0 = new Point2d(x, y0);
            Point2d p1 = new Point2d(x, y1);
            if (hookStart)
                pts.Add(HookTip2d(p0, horizontalBar: false, isUst, gonyeLen));
            pts.Add(p0);
            pts.Add(p1);
            if (hookEnd)
                pts.Add(HookTip2d(p1, horizontalBar: false, isUst, gonyeLen));
            PlacePolylineWithBends(tr, btr, layer, pts);
        }

        /// <summary>
        /// Oto Donatı ile aynı: yatay üst→aşağı / alt→yukarı; düşey üst→sağa / alt→sola.
        /// </summary>
        private static Point2d HookTip2d(Point2d end, bool horizontalBar, bool isUst, double len)
        {
            if (horizontalBar)
            {
                double dy = isUst ? -len : len;
                return new Point2d(end.X, end.Y + dy);
            }
            double dx = isUst ? len : -len;
            return new Point2d(end.X + dx, end.Y);
        }

        /// <summary>İç köşelere R=3 cm yay (bulge).</summary>
        private static void PlacePolylineWithBends(
            Transaction tr, BlockTableRecord btr, string layer, List<Point2d> pts)
        {
            if (pts == null || pts.Count < 2) return;
            if (pts.Count == 2)
            {
                DrawPolylinePts(tr, btr, layer, pts);
                return;
            }

            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            int vi = 0;
            pl.AddVertexAt(vi++, pts[0], 0, 0, 0);
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector2d dIn = pts[i] - pts[i - 1];
                Vector2d dOut = pts[i + 1] - pts[i];
                double lIn = dIn.Length, lOut = dOut.Length;
                if (lIn < 1e-6 || lOut < 1e-6)
                    continue;
                dIn = dIn / lIn;
                dOut = dOut / lOut;
                double cross = dIn.X * dOut.Y - dIn.Y * dOut.X;
                double dot = Math.Max(-1.0, Math.Min(1.0, dIn.DotProduct(dOut)));
                double theta = Math.Acos(dot);
                if (theta < 1e-4)
                {
                    pl.AddVertexAt(vi++, pts[i], 0, 0, 0);
                    continue;
                }

                double trim = BendRadiusCm * Math.Tan(theta / 2.0);
                double maxTrim = Math.Min(lIn, lOut) * 0.45;
                if (trim > maxTrim) trim = maxTrim;
                Point2d a = pts[i] - dIn * trim;
                Point2d b = pts[i] + dOut * trim;
                double bulge = Math.Tan(theta / 4.0) * (cross >= 0 ? 1.0 : -1.0);
                pl.AddVertexAt(vi++, a, bulge, 0, 0);
                pl.AddVertexAt(vi++, b, 0, 0, 0);
            }
            pl.AddVertexAt(vi, pts[pts.Count - 1], 0, 0, 0);
            pl.Closed = false;
            pl.Layer = layer;
            pl.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static void DrawPolylinePts(
            Transaction tr, BlockTableRecord btr, string layer, List<Point2d> pts)
        {
            if (pts == null || pts.Count < 2) return;
            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            for (int i = 0; i < pts.Count; i++)
                pl.AddVertexAt(i, pts[i], 0, 0, 0);
            pl.Closed = false;
            pl.Layer = layer;
            pl.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static void DrawHorizontalLine(
            Transaction tr, BlockTableRecord btr, string layer,
            double x0, double x1, double y)
        {
            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            pl.AddVertexAt(0, new Point2d(x0, y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x1, y), 0, 0, 0);
            pl.Closed = false;
            pl.Layer = layer;
            pl.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static void DrawVerticalLine(
            Transaction tr, BlockTableRecord btr, string layer,
            double x, double y0, double y1)
        {
            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            pl.AddVertexAt(0, new Point2d(x, y0), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x, y1), 0, 0, 0);
            pl.Closed = false;
            pl.Layer = layer;
            pl.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        /// <summary>
        /// Etiket çubuk üzerinde; kolon/isim engelinden uzak noktaya kaydırılır (bant yok sayılır).
        /// X: yatay üstte; Y: düşey solda (yazı kutusu).
        /// </summary>
        private static void PlaceBandDonatiLabel(
            Transaction tr, Database db, BlockTableRecord btr, string layer,
            ObjectId textStyleId,
            double along0, double along1, double fixedCoord,
            string label, bool isY,
            List<(double x0, double x1, double y0, double y1)> obstacles)
        {
            if (string.IsNullOrWhiteSpace(label)) return;
            double a0 = Math.Min(along0, along1);
            double a1 = Math.Max(along0, along1);
            double mid = 0.5 * (a0 + a1);
            Point3d barRef0 = isY
                ? new Point3d(fixedCoord, mid, 0)
                : new Point3d(mid, fixedCoord, 0);

            var t = new DBText();
            t.SetDatabaseDefaults();
            t.TextString = label;
            t.Height = DonatiLabelHeightCm;
            if (!textStyleId.IsNull)
                t.TextStyleId = textStyleId;
            t.Layer = layer;
            t.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            t.HorizontalMode = TextHorizontalMode.TextCenter;
            t.VerticalMode = TextVerticalMode.TextBottom;
            t.AlignmentPoint = barRef0;
            t.AdjustAlignment(db);
            btr.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);

            try
            {
                if (isY)
                    t.TransformBy(Matrix3d.Rotation(Math.PI / 2.0, Vector3d.ZAxis, barRef0));

                // Önce varsayılan hiza (üst / sol)
                Extents3d te = t.GeometricExtents;
                if (!isY)
                {
                    double cx = 0.5 * (te.MinPoint.X + te.MaxPoint.X);
                    double dx = barRef0.X - cx;
                    double dy = (barRef0.Y + DonatiLabelGapCm) - te.MinPoint.Y;
                    t.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
                }
                else
                {
                    Extents3d te2 = t.GeometricExtents;
                    double cy = 0.5 * (te2.MinPoint.Y + te2.MaxPoint.Y);
                    double dy = barRef0.Y - cy;
                    double dx = (barRef0.X - DonatiLabelGapCm) - te2.MaxPoint.X;
                    t.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
                }

                Extents3d teAligned = t.GeometricExtents;
                double halfAlong = isY
                    ? 0.5 * (teAligned.MaxPoint.Y - teAligned.MinPoint.Y)
                    : 0.5 * (teAligned.MaxPoint.X - teAligned.MinPoint.X);
                if (halfAlong < 5) halfAlong = 5;

                double lo = a0 + halfAlong;
                double hi = a1 - halfAlong;
                if (hi < lo) { lo = mid; hi = mid; }

                double bestAlong = mid;
                double bestScore = ScoreBoxVsObstacles(
                    teAligned.MinPoint.X, teAligned.MaxPoint.X,
                    teAligned.MinPoint.Y, teAligned.MaxPoint.Y, obstacles);
                double baseAlong = mid;

                if (bestScore > 1e-6 && hi > lo + 1)
                {
                    int nSteps = Math.Max(1, (int)Math.Ceiling((hi - lo) / CleanShiftStepCm));
                    for (int i = 0; i <= nSteps; i++)
                    {
                        for (int s = (i == 0 ? 0 : -1); s <= 1; s += (i == 0 ? 2 : 1))
                        {
                            if (i == 0 && s != 0) continue;
                            double along = i == 0 ? mid : mid + s * i * CleanShiftStepCm;
                            if (along < lo - 1e-6 || along > hi + 1e-6) continue;
                            along = Math.Max(lo, Math.Min(hi, along));
                            double shift = along - baseAlong;
                            double bx0 = teAligned.MinPoint.X + (isY ? 0 : shift);
                            double bx1 = teAligned.MaxPoint.X + (isY ? 0 : shift);
                            double by0 = teAligned.MinPoint.Y + (isY ? shift : 0);
                            double by1 = teAligned.MaxPoint.Y + (isY ? shift : 0);
                            double sc = ScoreBoxVsObstacles(bx0, bx1, by0, by1, obstacles);
                            if (sc + 1e-6 < bestScore
                                || (Math.Abs(sc - bestScore) < 1e-6
                                    && Math.Abs(along - mid) < Math.Abs(bestAlong - mid)))
                            {
                                bestScore = sc;
                                bestAlong = along;
                                if (bestScore < 1e-3) goto Applied;
                            }
                        }
                    }
                }

                Applied:
                double dAlong = bestAlong - baseAlong;
                if (Math.Abs(dAlong) > 1e-6)
                {
                    t.TransformBy(Matrix3d.Displacement(
                        isY ? new Vector3d(0, dAlong, 0) : new Vector3d(dAlong, 0, 0)));
                }

                // Sonraki etiketler bu kutuya binmesin
                if (obstacles != null)
                {
                    try
                    {
                        Extents3d fin = t.GeometricExtents;
                        double pad = ObstacleClearCm * 0.5;
                        obstacles.Add((
                            fin.MinPoint.X - pad, fin.MaxPoint.X + pad,
                            fin.MinPoint.Y - pad, fin.MaxPoint.Y + pad));
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static double ScoreBoxVsObstacles(
            double x0, double x1, double y0, double y1,
            List<(double x0, double x1, double y0, double y1)> obstacles)
        {
            if (obstacles == null || obstacles.Count == 0) return 0;
            double score = 0;
            for (int i = 0; i < obstacles.Count; i++)
            {
                var o = obstacles[i];
                double ox0 = Math.Max(x0, o.x0);
                double ox1 = Math.Min(x1, o.x1);
                double oy0 = Math.Max(y0, o.y0);
                double oy1 = Math.Min(y1, o.y1);
                if (ox1 > ox0 && oy1 > oy0)
                    score += (ox1 - ox0) * (oy1 - oy0);
            }
            return score;
        }

        private static ObjectId GetOrCreateYaziBeykentTextStyle(Transaction tr, Database db)
        {
            var txtTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (txtTable.Has(TextStyleYazi))
                return txtTable[TextStyleYazi];

            txtTable.UpgradeOpen();
            var rec = new TextStyleTableRecord { Name = TextStyleYazi };
            try
            {
                rec.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor(
                    "Bahnschrift Light Condensed", false, false, 0, 0);
            }
            catch
            {
                try
                {
                    rec.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor(
                        "Arial", false, false, 0, 0);
                }
                catch { }
            }
            try { rec.TextSize = 0.0; } catch { }
            txtTable.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return rec.ObjectId;
        }

        /// <summary>
        /// Isı AABB ortasına oturan dikdörtgen.
        /// X: W=stok, H=aralık katı. Y: H=stok, W=aralık katı.
        /// </summary>
        private static CoverRect BuildCoverRect(Extents3d heat, double aralik, bool isY)
        {
            double hx0 = heat.MinPoint.X, hx1 = heat.MaxPoint.X;
            double hy0 = heat.MinPoint.Y, hy1 = heat.MaxPoint.Y;
            double needW = Math.Max(0.0, hx1 - hx0);
            double needH = Math.Max(0.0, hy1 - hy0);
            double midX = 0.5 * (hx0 + hx1);
            double midY = 0.5 * (hy0 + hy1);

            double W, H;
            if (isY)
            {
                H = SnapCoverWidthCm(needH);           // düşey stok boy
                W = SnapCoverHeightCm(needW, aralik);  // yatay aralık katı
            }
            else
            {
                W = SnapCoverWidthCm(needW);
                H = SnapCoverHeightCm(needH, aralik);
            }

            return new CoverRect
            {
                X0 = midX - 0.5 * W,
                X1 = midX + 0.5 * W,
                Y0 = midY - 0.5 * H,
                Y1 = midY + 0.5 * H,
                StockW = isY ? H : W
            };
        }

        /// <summary>≥ need: mümkünse 1200 ortak böleni (≥240); yoksa 50 cm adım.</summary>
        internal static double SnapCoverWidthCm(double needCm)
        {
            double need = Math.Max(needCm, 0.0);
            foreach (double d in PreferStockDivisors)
            {
                if (d + 1e-6 >= need) return d;
            }
            // 1200'e sığmadı → 50 cm katları (ceil)
            double up50 = Math.Ceiling(need / 50.0 - 1e-9) * 50.0;
            if (up50 < MinBarLengthCm) up50 = MinBarLengthCm;
            if (up50 < need - 1e-9) up50 += 50.0;
            return up50;
        }

        /// <summary>≥ need: aralık katı (en az 1 aralık).</summary>
        internal static double SnapCoverHeightCm(double needCm, double aralikCm)
        {
            double s = aralikCm > 0.5 ? aralikCm : 15.0;
            double need = Math.Max(needCm, 0.0);
            int n = Math.Max(1, (int)Math.Ceiling(need / s - 1e-9));
            return n * s;
        }

        /// <summary>
        /// Tüm aralık-satırlarını ısı genişliğine göre stok W ile etiketle;
        /// bitişik aynı W → bant. Kısa bant (&lt;7·aralık) komşuyla birleşir (max W).
        /// Bant W nihai: Y aralığındaki ısı X birleşim span'ı (kayma yüzünden satır W yetmez).
        /// </summary>
        private static List<CoverRect> BuildHeatBandsX(
            List<Point2d> ring, CoverRect outer, double aralik)
        {
            var bands = new List<CoverRect>();
            if (aralik < 0.5 || ring == null || ring.Count < 3) return bands;

            int nRows = Math.Max(1, (int)Math.Round(outer.Height / aralik));
            var rowW = new double[nRows];
            var rowHx0 = new double[nRows];
            var rowHx1 = new double[nRows];

            for (int i = 0; i < nRows; i++)
            {
                double maxSpan = 0;
                double hx0 = double.MaxValue, hx1 = double.MinValue;
                bool any = false;
                // Daha sık örnek — satır içi max kiriş kaçmasın
                for (int k = 0; k <= 8; k++)
                {
                    double t = k / 8.0;
                    double y = outer.Y0 + (i + t) * aralik;
                    if (k == 8) y = Math.Min(y, outer.Y1) - (i == nRows - 1 ? 0 : 1e-4);
                    if (!TryHeatSpanX(ring, y, out double span, out double a, out double b))
                        continue;
                    any = true;
                    if (span > maxSpan) maxSpan = span;
                    if (a < hx0) hx0 = a;
                    if (b > hx1) hx1 = b;
                }

                if (!any)
                {
                    rowW[i] = outer.Width;
                    rowHx0[i] = outer.X0;
                    rowHx1[i] = outer.X1;
                }
                else
                {
                    double stock = SnapCoverWidthCm(maxSpan);
                    if (stock > outer.Width + 1e-6) stock = outer.Width;
                    rowW[i] = stock;
                    rowHx0[i] = hx0;
                    rowHx1[i] = hx1;
                }
            }

            // Ham koşular
            var runs = new List<(int i0, int i1, double w)>();
            int iRun = 0;
            while (iRun < nRows)
            {
                double w = rowW[iRun];
                int j = iRun;
                while (j + 1 < nRows && Math.Abs(rowW[j + 1] - w) < 1e-6) j++;
                runs.Add((iRun, j, w));
                iRun = j + 1;
            }

            // Min 7 donatı arası: kısa koşuları komşuyla birleştir (W = max)
            MergeShortRuns(runs, MinBandGaps);

            for (int r = 0; r < runs.Count; r++)
            {
                int i0 = runs[r].i0, i1 = runs[r].i1;

                double y0 = outer.Y0 + i0 * aralik;
                double y1 = outer.Y0 + (i1 + 1) * aralik;
                if (Math.Abs(y0 - outer.Y0) < 0.05) y0 = outer.Y0;
                if (Math.Abs(y1 - outer.Y1) < 0.05) y1 = outer.Y1;

                // Bant boyunca ısı X birleşimi (satır kayması → span > max satır W)
                double hx0 = double.MaxValue, hx1 = double.MinValue;
                for (int row = i0; row <= i1; row++)
                {
                    if (rowHx0[row] < hx0) hx0 = rowHx0[row];
                    if (rowHx1[row] > hx1) hx1 = rowHx1[row];
                }
                if (hx1 < hx0)
                {
                    hx0 = outer.X0;
                    hx1 = outer.X1;
                }

                double unionSpan = hx1 - hx0;
                double w = SnapCoverWidthCm(unionSpan);
                if (w < runs[r].w - 1e-6) w = runs[r].w; // birleştirme max W'sinden küçülmesin
                if (w > outer.Width + 1e-6) w = outer.Width;

                double x0, x1;
                if (w + 1e-6 >= outer.Width)
                {
                    x0 = outer.X0;
                    x1 = outer.X1;
                }
                else
                {
                    // Isıyı kapsayacak şekilde ortala; taşarsa ısıya yasla
                    double midX = 0.5 * (hx0 + hx1);
                    x0 = midX - 0.5 * w;
                    if (x0 > hx0 + 1e-6) x0 = hx0;           // sol ısı dışarıda kalmasın
                    if (x0 + w < hx1 - 1e-6) x0 = hx1 - w;   // sağ ısı dışarıda kalmasın
                    if (x0 < outer.X0) x0 = outer.X0;
                    if (x0 + w > outer.X1 + 1e-6) x0 = outer.X1 - w;
                    x1 = x0 + w;
                }

                bands.Add(new CoverRect
                {
                    X0 = x0,
                    X1 = x1,
                    Y0 = y0,
                    Y1 = y1,
                    StockW = w
                });
            }

            // Aynı W'ye düşen komşu bantları birleştir (yeniden snap sonrası)
            MergeAdjacentSameWidthBands(bands, alongY: true);
            return bands;
        }

        /// <summary>
        /// Y yönü: X boyunca aralık-sütunları; stok = düşey ısı span; kısa sütun birleşir.
        /// </summary>
        private static List<CoverRect> BuildHeatBandsY(
            List<Point2d> ring, CoverRect outer, double aralik)
        {
            var bands = new List<CoverRect>();
            if (aralik < 0.5 || ring == null || ring.Count < 3) return bands;

            int nCols = Math.Max(1, (int)Math.Round(outer.Width / aralik));
            var colH = new double[nCols];
            var colHy0 = new double[nCols];
            var colHy1 = new double[nCols];

            for (int i = 0; i < nCols; i++)
            {
                double maxSpan = 0;
                double hy0 = double.MaxValue, hy1 = double.MinValue;
                bool any = false;
                for (int k = 0; k <= 8; k++)
                {
                    double t = k / 8.0;
                    double x = outer.X0 + (i + t) * aralik;
                    if (k == 8) x = Math.Min(x, outer.X1) - (i == nCols - 1 ? 0 : 1e-4);
                    if (!TryHeatSpanY(ring, x, out double span, out double a, out double b))
                        continue;
                    any = true;
                    if (span > maxSpan) maxSpan = span;
                    if (a < hy0) hy0 = a;
                    if (b > hy1) hy1 = b;
                }

                if (!any)
                {
                    colH[i] = outer.Height;
                    colHy0[i] = outer.Y0;
                    colHy1[i] = outer.Y1;
                }
                else
                {
                    double stock = SnapCoverWidthCm(maxSpan);
                    if (stock > outer.Height + 1e-6) stock = outer.Height;
                    colH[i] = stock;
                    colHy0[i] = hy0;
                    colHy1[i] = hy1;
                }
            }

            var runs = new List<(int i0, int i1, double w)>();
            int iRun = 0;
            while (iRun < nCols)
            {
                double w = colH[iRun];
                int j = iRun;
                while (j + 1 < nCols && Math.Abs(colH[j + 1] - w) < 1e-6) j++;
                runs.Add((iRun, j, w));
                iRun = j + 1;
            }

            MergeShortRuns(runs, MinBandGaps);

            for (int r = 0; r < runs.Count; r++)
            {
                int i0 = runs[r].i0, i1 = runs[r].i1;

                double x0 = outer.X0 + i0 * aralik;
                double x1 = outer.X0 + (i1 + 1) * aralik;
                if (Math.Abs(x0 - outer.X0) < 0.05) x0 = outer.X0;
                if (Math.Abs(x1 - outer.X1) < 0.05) x1 = outer.X1;

                double hy0 = double.MaxValue, hy1 = double.MinValue;
                for (int col = i0; col <= i1; col++)
                {
                    if (colHy0[col] < hy0) hy0 = colHy0[col];
                    if (colHy1[col] > hy1) hy1 = colHy1[col];
                }
                if (hy1 < hy0)
                {
                    hy0 = outer.Y0;
                    hy1 = outer.Y1;
                }

                double unionSpan = hy1 - hy0;
                double h = SnapCoverWidthCm(unionSpan);
                if (h < runs[r].w - 1e-6) h = runs[r].w;
                if (h > outer.Height + 1e-6) h = outer.Height;

                double y0, y1;
                if (h + 1e-6 >= outer.Height)
                {
                    y0 = outer.Y0;
                    y1 = outer.Y1;
                }
                else
                {
                    double midY = 0.5 * (hy0 + hy1);
                    y0 = midY - 0.5 * h;
                    if (y0 > hy0 + 1e-6) y0 = hy0;
                    if (y0 + h < hy1 - 1e-6) y0 = hy1 - h;
                    if (y0 < outer.Y0) y0 = outer.Y0;
                    if (y0 + h > outer.Y1 + 1e-6) y0 = outer.Y1 - h;
                    y1 = y0 + h;
                }

                bands.Add(new CoverRect
                {
                    X0 = x0,
                    X1 = x1,
                    Y0 = y0,
                    Y1 = y1,
                    StockW = h
                });
            }

            MergeAdjacentSameWidthBands(bands, alongY: false);
            return bands;
        }

        /// <summary>Satır sayısı &lt; minRows olan koşuları komşuyla birleştir; W = max.</summary>
        private static void MergeShortRuns(List<(int i0, int i1, double w)> runs, int minRows)
        {
            if (runs == null || runs.Count <= 1 || minRows < 1) return;
            int guard = runs.Count * 4 + 8;
            while (guard-- > 0)
            {
                int shortIdx = -1;
                for (int i = 0; i < runs.Count; i++)
                {
                    int rows = runs[i].i1 - runs[i].i0 + 1;
                    if (rows < minRows) { shortIdx = i; break; }
                }
                if (shortIdx < 0) return;
                if (runs.Count == 1) return;

                int mergeWith;
                if (shortIdx == 0) mergeWith = 1;
                else if (shortIdx == runs.Count - 1) mergeWith = shortIdx - 1;
                else
                {
                    var L = runs[shortIdx - 1];
                    var R = runs[shortIdx + 1];
                    int lRows = L.i1 - L.i0 + 1;
                    int rRows = R.i1 - R.i0 + 1;
                    // Büyük genişlik tercih; eşitse daha uzun komşu
                    if (L.w > R.w + 1e-6) mergeWith = shortIdx - 1;
                    else if (R.w > L.w + 1e-6) mergeWith = shortIdx + 1;
                    else if (lRows >= rRows) mergeWith = shortIdx - 1;
                    else mergeWith = shortIdx + 1;
                }

                int a = Math.Min(shortIdx, mergeWith);
                int b = Math.Max(shortIdx, mergeWith);
                var ra = runs[a];
                var rb = runs[b];
                var merged = (
                    Math.Min(ra.i0, rb.i0),
                    Math.Max(ra.i1, rb.i1),
                    Math.Max(ra.w, rb.w));
                runs[a] = merged;
                runs.RemoveAt(b);
            }
        }

        /// <summary>Snap sonrası aynı StockW komşu bantları birleştir (alongY: X bantları dikey bitişik).</summary>
        private static void MergeAdjacentSameWidthBands(List<CoverRect> bands, bool alongY)
        {
            if (bands == null || bands.Count <= 1) return;
            int i = 0;
            while (i + 1 < bands.Count)
            {
                CoverRect a = bands[i], b = bands[i + 1];
                bool touch = alongY
                    ? (Math.Abs(a.Y1 - b.Y0) < 0.1 || Math.Abs(b.Y1 - a.Y0) < 0.1)
                    : (Math.Abs(a.X1 - b.X0) < 0.1 || Math.Abs(b.X1 - a.X0) < 0.1);
                bool sameW = Math.Abs(a.StockW - b.StockW) < 1e-3;
                if (!touch || !sameW) { i++; continue; }

                double y0 = Math.Min(a.Y0, b.Y0);
                double y1 = Math.Max(a.Y1, b.Y1);
                double x0 = Math.Min(a.X0, b.X0);
                double x1 = Math.Max(a.X1, b.X1);
                double w = Math.Max(a.StockW, b.StockW);
                double spanAlongBar = alongY ? (x1 - x0) : (y1 - y0);
                if (spanAlongBar > w + 1e-3)
                {
                    double mid = alongY ? 0.5 * (x0 + x1) : 0.5 * (y0 + y1);
                    w = SnapCoverWidthCm(spanAlongBar);
                    if (alongY)
                    {
                        x0 = mid - 0.5 * w;
                        x1 = x0 + w;
                    }
                    else
                    {
                        y0 = mid - 0.5 * w;
                        y1 = y0 + w;
                    }
                }
                bands[i] = new CoverRect
                {
                    X0 = x0, X1 = x1, Y0 = y0, Y1 = y1, StockW = w
                };
                bands.RemoveAt(i + 1);
            }
        }

        private static short BandColor(double stockW, double outerW)
        {
            if (stockW + 1e-6 >= outerW) return 4; // cyan — tam genişlik
            if (Math.Abs(stockW - 400) < 1e-3) return 30; // turuncu
            if (Math.Abs(stockW - 300) < 1e-3) return 2; // sarı
            if (Math.Abs(stockW - 240) < 1e-3) return 3; // yeşil
            return 6; // magenta — diğer stok
        }

        private static bool TryHeatSpanX(
            List<Point2d> ring, double y, out double span, out double minX, out double maxX)
        {
            span = 0;
            minX = 0;
            maxX = 0;
            var chords = HorizontalChords(ring, y);
            if (chords.Count == 0) return false;
            minX = chords[0].a;
            maxX = chords[0].b;
            for (int i = 1; i < chords.Count; i++)
            {
                if (chords[i].a < minX) minX = chords[i].a;
                if (chords[i].b > maxX) maxX = chords[i].b;
            }
            span = maxX - minX;
            return span > 1e-6;
        }

        private static bool TryHeatSpanY(
            List<Point2d> ring, double x, out double span, out double minY, out double maxY)
        {
            span = 0;
            minY = 0;
            maxY = 0;
            var chords = VerticalChords(ring, x);
            if (chords.Count == 0) return false;
            minY = chords[0].a;
            maxY = chords[0].b;
            for (int i = 1; i < chords.Count; i++)
            {
                if (chords[i].a < minY) minY = chords[i].a;
                if (chords[i].b > maxY) maxY = chords[i].b;
            }
            span = maxY - minY;
            return span > 1e-6;
        }

        private static void DrawClosedRect(
            Transaction tr, BlockTableRecord btr, string layer, CoverRect r, short colorIndex)
        {
            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            pl.AddVertexAt(0, new Point2d(r.X0, r.Y0), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(r.X1, r.Y0), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(r.X1, r.Y1), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(r.X0, r.Y1), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layer;
            pl.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static List<(double a, double b)> HorizontalChords(List<Point2d> ring, double y)
        {
            var xs = new List<double>();
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                Point2d p0 = ring[i], p1 = ring[(i + 1) % n];
                if (Math.Abs(p0.Y - p1.Y) < 1e-9)
                {
                    if (Math.Abs(p0.Y - y) < 1e-6) { xs.Add(p0.X); xs.Add(p1.X); }
                    continue;
                }
                if ((p0.Y > y) == (p1.Y > y)) continue;
                double t = (y - p0.Y) / (p1.Y - p0.Y);
                if (t < -1e-9 || t > 1 + 1e-9) continue;
                xs.Add(p0.X + t * (p1.X - p0.X));
            }
            xs.Sort();
            var chords = new List<(double a, double b)>();
            for (int i = 0; i + 1 < xs.Count; i += 2)
            {
                double a = xs[i], b = xs[i + 1];
                if (b - a >= 1.0) chords.Add((a, b));
            }
            return chords;
        }

        private static List<(double a, double b)> VerticalChords(List<Point2d> ring, double x)
        {
            var ys = new List<double>();
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                Point2d p0 = ring[i], p1 = ring[(i + 1) % n];
                if (Math.Abs(p0.X - p1.X) < 1e-9)
                {
                    if (Math.Abs(p0.X - x) < 1e-6) { ys.Add(p0.Y); ys.Add(p1.Y); }
                    continue;
                }
                if ((p0.X > x) == (p1.X > x)) continue;
                double t = (x - p0.X) / (p1.X - p0.X);
                if (t < -1e-9 || t > 1 + 1e-9) continue;
                ys.Add(p0.Y + t * (p1.Y - p0.Y));
            }
            ys.Sort();
            var chords = new List<(double a, double b)>();
            for (int i = 0; i + 1 < ys.Count; i += 2)
            {
                double a = ys[i], b = ys[i + 1];
                if (b - a >= 1.0) chords.Add((a, b));
            }
            return chords;
        }

        private static List<Point2d> ExtractRing(Polyline pl)
        {
            var pts = new List<Point2d>();
            for (int i = 0; i < pl.NumberOfVertices; i++)
                pts.Add(pl.GetPoint2dAt(i));
            return pts;
        }

        /// <summary>Shoelace alan (cm²); Area okunamazsa yedek.</summary>
        private static double PolygonAreaCm2(List<Point2d> ring)
        {
            if (ring == null || ring.Count < 3) return 0;
            double sum = 0;
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                Point2d a = ring[i], b = ring[(i + 1) % n];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return Math.Abs(sum) * 0.5;
        }

        private static void EnsureClosed(List<Point2d> ring)
        {
            if (ring == null || ring.Count < 2) return;
            Point2d a = ring[0], b = ring[ring.Count - 1];
            if (Math.Abs(a.X - b.X) > 1e-6 || Math.Abs(a.Y - b.Y) > 1e-6)
                ring.Add(a);
            if (ring.Count >= 2)
            {
                Point2d last = ring[ring.Count - 1], first = ring[0];
                if (Math.Abs(last.X - first.X) < 1e-6 && Math.Abs(last.Y - first.Y) < 1e-6)
                    ring.RemoveAt(ring.Count - 1);
            }
        }

        private static bool LayerEquals(string layer, string expected)
        {
            if (string.IsNullOrEmpty(layer) || string.IsNullOrEmpty(expected)) return false;
            return string.Equals(layer.Trim(), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static void AddUniqueFixed(List<double> list, double v)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (Math.Abs(list[i] - v) < 0.05) return;
            }
            list.Add(v);
        }

        /// <summary>Çubuk içi güvenli aralık: uçlardan ≥40 cm (kısa parçada ortaya iner).</summary>
        private static bool TrySegSafeInterval(DonatiOkuSeg s, out double a, out double b)
        {
            double len = s.Along1 - s.Along0;
            if (len < 1e-3) { a = b = 0; return false; }
            double clear = DonatiOkuEndClearCm;
            if (len < 2.0 * clear + 1.0)
                clear = Math.Max(1.0, len * 0.5 - 0.5);
            a = s.Along0 + clear;
            b = s.Along1 - clear;
            return b >= a - 1e-6;
        }

        private static bool AlongInSafe(DonatiOkuSeg s, double along)
        {
            if (!TrySegSafeInterval(s, out double a, out double b)) return false;
            return along >= a - 0.05 && along <= b + 0.05;
        }

        private static int CountSafeHits(List<DonatiOkuSeg> segs, double along)
        {
            int n = 0;
            for (int i = 0; i < segs.Count; i++)
            {
                if (AlongInSafe(segs[i], along)) n++;
            }
            return n;
        }

        private static int CountUniqueFixedSafeHits(List<DonatiOkuSeg> segs, double along)
        {
            var seen = new List<double>();
            for (int i = 0; i < segs.Count; i++)
            {
                if (!AlongInSafe(segs[i], along)) continue;
                AddUniqueFixed(seen, segs[i].Fixed);
            }
            return seen.Count;
        }

        /// <summary>
        /// Tüm (veya max) çubuk parçalarını uçtan ≥40 cm içeriden kesen hat.
        /// Çukur/TEMEL trim parçaları dahil; eşitlikte sağ (preferHigh) veya alt (!preferHigh).
        /// </summary>
        private static double PickDimLineAlongAllBars(
            List<DonatiOkuSeg> segs, double lo, double hi, bool preferHigh)
        {
            if (segs == null || segs.Count == 0)
                return preferHigh ? hi : lo;

            // 1) Tüm parçaların güvenli aralık kesişimi
            double interLo = double.NegativeInfinity;
            double interHi = double.PositiveInfinity;
            int validSegs = 0;
            for (int i = 0; i < segs.Count; i++)
            {
                if (!TrySegSafeInterval(segs[i], out double a, out double b)) continue;
                validSegs++;
                if (a > interLo) interLo = a;
                if (b < interHi) interHi = b;
            }

            double clipLo = lo;
            double clipHi = hi;
            if (interHi >= interLo && validSegs == segs.Count && validSegs > 0)
            {
                double a = Math.Max(clipLo, interLo);
                double b = Math.Min(clipHi, interHi);
                if (b >= a)
                    return preferHigh ? b : a;
                // clip dışına taştıysa kesişim içinde kal
                if (interHi >= interLo)
                    return preferHigh ? interHi : interLo;
            }

            // 2) Ortak kesişim yok (trim/çukur parçaları ayrık): max isabet ara
            double best = preferHigh ? hi : lo;
            int bestFix = -1;
            int bestSeg = -1;

            void Consider(double c)
            {
                if (double.IsNaN(c) || double.IsInfinity(c)) return;
                int nf = CountUniqueFixedSafeHits(segs, c);
                int ns = CountSafeHits(segs, c);
                if (nf < 1 && ns < 1) return;
                bool better = nf > bestFix
                    || (nf == bestFix && ns > bestSeg)
                    || (nf == bestFix && ns == bestSeg
                        && (preferHigh ? c > best + 0.05 : c < best - 0.05));
                if (better)
                {
                    bestFix = nf;
                    bestSeg = ns;
                    best = c;
                }
            }

            // Aday: her parçanın güvenli uçları + orta
            for (int i = 0; i < segs.Count; i++)
            {
                if (!TrySegSafeInterval(segs[i], out double a, out double b)) continue;
                Consider(a);
                Consider(b);
                Consider(0.5 * (a + b));
            }

            // İkili/üçlü kesişim örneklemesi: uzun parçaların ortak bölgesi
            for (int i = 0; i < segs.Count; i++)
            {
                if (!TrySegSafeInterval(segs[i], out double a0, out double b0)) continue;
                for (int j = i + 1; j < segs.Count; j++)
                {
                    if (!TrySegSafeInterval(segs[j], out double a1, out double b1)) continue;
                    double a = Math.Max(a0, a1);
                    double b = Math.Min(b0, b1);
                    if (b >= a)
                    {
                        Consider(preferHigh ? b : a);
                        Consider(0.5 * (a + b));
                    }
                }
            }

            double step = Math.Max(2.0, (hi - lo) / 80.0);
            for (double c = lo; c <= hi; c += step)
                Consider(c);

            if (bestFix < 0)
            {
                // Son çare: en uzun parçanın ortası
                double maxLen = -1;
                for (int i = 0; i < segs.Count; i++)
                {
                    double len = segs[i].Along1 - segs[i].Along0;
                    if (len > maxLen)
                    {
                        maxLen = len;
                        best = 0.5 * (segs[i].Along0 + segs[i].Along1);
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// X: etiketlerin sağına düşecek ölçü X alt sınırı (yazı kutusu sağ + boşluk).
        /// </summary>
        private static double EstimateLabelRightX(
            List<PendingDonatiLabel> pending, List<DonatiOkuSeg> segs, double fallback)
        {
            double maxRight = double.NegativeInfinity;
            if (pending != null)
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    PendingDonatiLabel p = pending[i];
                    if (p.IsY) continue;
                    double mid = 0.5 * (p.Along0 + p.Along1);
                    double halfW = Math.Max(15.0, (p.Text != null ? p.Text.Length : 12) * DonatiLabelHeightCm * 0.28);
                    double right = mid + halfW;
                    if (right > maxRight) maxRight = right;
                }
            }
            if (double.IsNegativeInfinity(maxRight) && segs != null)
            {
                for (int i = 0; i < segs.Count; i++)
                {
                    double mid = 0.5 * (segs[i].Along0 + segs[i].Along1);
                    if (mid + 40 > maxRight) maxRight = mid + 40;
                }
            }
            if (double.IsNegativeInfinity(maxRight)) return fallback;
            return maxRight + DonatiLabelGapCm + 5.0;
        }

        /// <summary>
        /// Bant kenarları + çukur/TEMEL kenarları arası açıklık.
        /// Çukur varsa örn. üst→çukur / çukur yüksekliği / çukur→alt (3 ölçü).
        /// Hat: tüm çubukları uçtan ≥40 cm keser; X’te mümkünse etiket sağı.
        /// </summary>
        private static void DrawDonatiOkuAcikliklari(
            Transaction tr, BlockTableRecord btr,
            CoverRect outer, bool isY, List<CoverRect> bands,
            List<DonatiOkuSeg> barSegs, List<double> pitEdgeCoords,
            List<PendingDonatiLabel> pendingLabels,
            string okuLayer, string halkaLayer, ObjectId dimStyleId)
        {
            if (bands == null || bands.Count == 0) return;
            if (barSegs == null) barSegs = new List<DonatiOkuSeg>();

            var pts = new List<double>();
            for (int i = 0; i < bands.Count; i++)
            {
                if (isY)
                {
                    AddUniqueFixed(pts, bands[i].X0);
                    AddUniqueFixed(pts, bands[i].X1);
                }
                else
                {
                    AddUniqueFixed(pts, bands[i].Y0);
                    AddUniqueFixed(pts, bands[i].Y1);
                }
            }
            if (pitEdgeCoords != null)
            {
                for (int i = 0; i < pitEdgeCoords.Count; i++)
                    AddUniqueFixed(pts, pitEdgeCoords[i]);
            }
            if (pts.Count < 2) return;
            pts.Sort();

            // Tarama aralığı: tüm çubuk uçlarını kapsasın (trim/TEMEL parçaları dahil)
            double spanLo = isY ? outer.Y0 : outer.X0;
            double spanHi = isY ? outer.Y1 : outer.X1;
            for (int i = 0; i < barSegs.Count; i++)
            {
                if (barSegs[i].Along0 < spanLo) spanLo = barSegs[i].Along0;
                if (barSegs[i].Along1 > spanHi) spanHi = barSegs[i].Along1;
            }

            if (isY)
            {
                double dimY = PickDimLineAlongAllBars(barSegs, spanLo, spanHi, preferHigh: false);
                var hitFixes = CollectHitFixes(barSegs, dimY);

                for (int i = 0; i < pts.Count - 1; i++)
                {
                    if (pts[i + 1] - pts[i] < MinDonatiOkuGapCm) continue;
                    PlaceDonatiOkuDim(
                        tr, btr, okuLayer, dimStyleId,
                        new Point3d(pts[i], dimY, 0),
                        new Point3d(pts[i + 1], dimY, 0),
                        new Point3d(0.5 * (pts[i] + pts[i + 1]), dimY, 0),
                        rotation: 0.0,
                        horizontalDimLine: true,
                        dimAlong: dimY,
                        hitFixes: hitFixes);
                }

                DrawDonatiOkuHitCircles(tr, btr, halkaLayer, hitFixes, dimY, fixedIsX: true);
            }
            else
            {
                double dimX = PickDimLineAlongAllBars(barSegs, spanLo, spanHi, preferHigh: true);
                double labelRight = EstimateLabelRightX(pendingLabels, barSegs, spanLo);
                if (dimX < labelRight)
                {
                    double nudged = PickDimLineAlongAllBars(
                        barSegs, Math.Max(spanLo, labelRight), spanHi, preferHigh: true);
                    var uniq = new List<double>();
                    for (int i = 0; i < barSegs.Count; i++)
                        AddUniqueFixed(uniq, barSegs[i].Fixed);
                    int allFix = uniq.Count;
                    if (CountUniqueFixedSafeHits(barSegs, nudged) >= allFix)
                        dimX = nudged;
                    else if (CountUniqueFixedSafeHits(barSegs, labelRight) >= CountUniqueFixedSafeHits(barSegs, dimX)
                             && CountSafeHits(barSegs, labelRight) >= CountSafeHits(barSegs, dimX))
                        dimX = labelRight;
                }

                var hitFixes = CollectHitFixes(barSegs, dimX);
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    if (pts[i + 1] - pts[i] < MinDonatiOkuGapCm) continue;
                    PlaceDonatiOkuDim(
                        tr, btr, okuLayer, dimStyleId,
                        new Point3d(dimX, pts[i], 0),
                        new Point3d(dimX, pts[i + 1], 0),
                        new Point3d(dimX, 0.5 * (pts[i] + pts[i + 1]), 0),
                        rotation: Math.PI * 0.5,
                        horizontalDimLine: false,
                        dimAlong: dimX,
                        hitFixes: hitFixes);
                }

                DrawDonatiOkuHitCircles(tr, btr, halkaLayer, hitFixes, dimX, fixedIsX: false);
            }
        }

        private static List<double> CollectHitFixes(List<DonatiOkuSeg> barSegs, double along)
        {
            var hitFixes = new List<double>();
            if (barSegs == null) return hitFixes;
            for (int i = 0; i < barSegs.Count; i++)
            {
                if (along < barSegs[i].Along0 - 0.05 || along > barSegs[i].Along1 + 0.05)
                    continue;
                AddUniqueFixed(hitFixes, barSegs[i].Fixed);
            }
            return hitFixes;
        }

        private static void DrawDonatiOkuHitCircles(
            Transaction tr, BlockTableRecord btr, string halkaLayer,
            List<double> hitFixes, double along, bool fixedIsX)
        {
            if (hitFixes == null) return;
            for (int i = 0; i < hitFixes.Count; i++)
            {
                if (fixedIsX)
                    DrawDonatiOkuCircle(tr, btr, halkaLayer, hitFixes[i], along);
                else
                    DrawDonatiOkuCircle(tr, btr, halkaLayer, along, hitFixes[i]);
            }
        }

        /// <summary>4 cm çap, global width 4; 2 vertex + 2 yay; DONATI HALKASI.</summary>
        private static void DrawDonatiOkuCircle(
            Transaction tr, BlockTableRecord btr, string layer, double cx, double cy)
        {
            double r = DonatiOkuCircleDiaCm * 0.5;
            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            pl.AddVertexAt(0, new Point2d(cx - r, cy), 1.0, 0, 0);
            pl.AddVertexAt(1, new Point2d(cx + r, cy), 1.0, 0, 0);
            pl.Closed = true;
            pl.ConstantWidth = DonatiOkuCircleWidthCm;
            pl.Layer = layer;
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static void PlaceDonatiOkuDim(
            Transaction tr, BlockTableRecord btr, string layer, ObjectId dimStyleId,
            Point3d x1, Point3d x2, Point3d dimLinePoint, double rotation,
            bool horizontalDimLine, double dimAlong, List<double> hitFixes)
        {
            var dim = new RotatedDimension();
            dim.SetDatabaseDefaults();
            dim.Rotation = rotation;
            dim.XLine1Point = x1;
            dim.XLine2Point = x2;
            dim.DimLinePoint = dimLinePoint;
            if (!dimStyleId.IsNull)
                dim.DimensionStyle = dimStyleId;
            dim.Layer = layer;

            try { dim.Dimse1 = true; } catch { }
            try { dim.Dimse2 = true; } catch { }
            try { dim.Dimasz = 10.0; } catch { }
            try { dim.Dimtxt = 10.0; } catch { }
            try { dim.Dimgap = 2.0; } catch { }
            try { dim.Dimtad = 0; } catch { } // Vertical: Centered (çizgi ortası)
            try { dim.Dimtih = false; } catch { }
            try { dim.Dimtoh = false; } catch { }
            try { dim.Dimdec = 0; } catch { }
            try { dim.Dimlfac = 1.0; } catch { }
            try { dim.Dimscale = 1.0; } catch { }

            btr.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);

            // Yazı Vertical=Centered kalsın; yalnız çizgi üzerinde kaydır.
            // Tercih: dikeyde halkadan 20 cm üst / yatayda 20 cm sol.
            // Ölçü kenarına sıkışırsa ters yön (aşağı / sağ).
            try
            {
                double clear = DonatiOkuTextClearCm;
                double edgeClear = Math.Max(8.0, DonatiLabelHeightCm); // yazı kutusu kenar payı
                double t0 = horizontalDimLine ? x1.X : x1.Y;
                double t1 = horizontalDimLine ? x2.X : x2.Y;
                double lo = Math.Min(t0, t1);
                double hi = Math.Max(t0, t1);
                double mid = 0.5 * (lo + hi);
                double anchor = mid;
                if (hitFixes != null && hitFixes.Count > 0)
                {
                    double bestD = double.MaxValue;
                    for (int i = 0; i < hitFixes.Count; i++)
                    {
                        double f = hitFixes[i];
                        if (f < lo - 0.5 || f > hi + 0.5) continue;
                        double d = Math.Abs(f - mid);
                        if (d < bestD)
                        {
                            bestD = d;
                            anchor = f;
                        }
                    }
                }

                double preferred = horizontalDimLine
                    ? anchor - clear   // yatay: sol
                    : anchor + clear;  // düşey: üst
                double flipped = horizontalDimLine
                    ? anchor + clear   // yatay: sağ
                    : anchor - clear;  // düşey: alt

                bool Fits(double t) => t >= lo + edgeClear && t <= hi - edgeClear;
                double alongText;
                if (Fits(preferred))
                    alongText = preferred;
                else if (Fits(flipped))
                    alongText = flipped;
                else
                {
                    // İkisi de sıkışık: kenara daha uzak olanı
                    double scorePref = Math.Min(preferred - lo, hi - preferred);
                    double scoreFlip = Math.Min(flipped - lo, hi - flipped);
                    alongText = scoreFlip > scorePref + 0.05 ? flipped : preferred;
                    alongText = Math.Max(lo + 1.0, Math.Min(hi - 1.0, alongText));
                }

                Point3d textPos = horizontalDimLine
                    ? new Point3d(alongText, dimAlong, 0)
                    : new Point3d(dimAlong, alongText, 0);

                dim.UsingDefaultTextPosition = false;
                dim.TextPosition = textPos;
                try { dim.Dimtad = 0; } catch { }
            }
            catch { }
        }

        /// <summary>DONATI HALKASI (BEYKENT): sarı, Continuous, 0.30.</summary>
        private static string EnsureDonatiHalkasiLayer(Transaction tr, Database db)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(LayerDonatiHalkasi))
            {
                // Mevcut katmanı Continuous + sarı yap (Dashed kalmasın)
                try
                {
                    var existing = (LayerTableRecord)tr.GetObject(lt[LayerDonatiHalkasi], OpenMode.ForWrite);
                    existing.Color = Color.FromColorIndex(ColorMethod.ByAci, DonatiHalkasiAci);
                    existing.LineWeight = LineWeight.LineWeight030;
                    var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                    if (ltt.Has("Continuous"))
                        existing.LinetypeObjectId = ltt["Continuous"];
                }
                catch { }
                return LayerDonatiHalkasi;
            }

            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            {
                Name = LayerDonatiHalkasi,
                Color = Color.FromColorIndex(ColorMethod.ByAci, DonatiHalkasiAci),
                LineWeight = LineWeight.LineWeight030
            };
            try
            {
                var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (ltt.Has("Continuous"))
                    rec.LinetypeObjectId = ltt["Continuous"];
            }
            catch { }

            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return LayerDonatiHalkasi;
        }

        /// <summary>
        /// DONATI OKU (BEYKENT): ekstansiyon kapalı, Closed filled ok 10, YAZI 10, gap 2.
        /// </summary>
        private static ObjectId EnsureDonatiOkuDimStyle(Transaction tr, Database db, ObjectId yaziStyleId)
        {
            var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (dst.Has(DimStyleDonatiOku))
            {
                try
                {
                    var existing = (DimStyleTableRecord)tr.GetObject(dst[DimStyleDonatiOku], OpenMode.ForWrite);
                    existing.Dimtad = 0; // Vertical: Centered
                    existing.Dimgap = 2.0;
                }
                catch { }
                return dst[DimStyleDonatiOku];
            }

            var rec = new DimStyleTableRecord { Name = DimStyleDonatiOku };
            try
            {
                if (dst.Has("Standard"))
                {
                    var std = tr.GetObject(dst["Standard"], OpenMode.ForRead) as DimStyleTableRecord;
                    if (std != null)
                    {
                        rec.CopyFrom(std);
                        rec.Name = DimStyleDonatiOku;
                    }
                }
            }
            catch { }

            try { rec.Dimscale = 1.0; } catch { }
            try { rec.Dimlfac = 1.0; } catch { }
            try { rec.Dimtxt = 10.0; } catch { }
            try { rec.Dimasz = 10.0; } catch { }
            try { rec.Dimtsz = 0.0; } catch { } // ok (tick değil)
            try { rec.Dimgap = 2.0; } catch { }
            try { rec.Dimexe = 5.0; } catch { }
            try { rec.Dimexo = 5.0; } catch { }
            try { rec.Dimse1 = true; } catch { }
            try { rec.Dimse2 = true; } catch { }
            try { rec.Dimsd1 = false; } catch { }
            try { rec.Dimsd2 = false; } catch { }
            try { rec.Dimtad = 0; } catch { } // Vertical: Centered
            try { rec.Dimjust = 0; } catch { } // Horizontal: Centered
            try { rec.Dimtih = false; } catch { }
            try { rec.Dimtoh = false; } catch { }
            try { rec.Dimtofl = true; } catch { }
            try { rec.Dimdec = 0; } catch { }
            try { rec.Dimtdec = 0; } catch { }
            try { rec.Dimlunit = 2; } catch { }
            try { rec.Dimcen = 0.0; } catch { } // Center marks: None
            // Closed filled: varsayılan (Null)
            try { rec.Dimblk = ObjectId.Null; } catch { }
            try { rec.Dimblk1 = ObjectId.Null; } catch { }
            try { rec.Dimblk2 = ObjectId.Null; } catch { }
            try { rec.Dimldrblk = ObjectId.Null; } catch { }
            if (!yaziStyleId.IsNull)
            {
                try { rec.Dimtxsty = yaziStyleId; } catch { }
            }

            dst.UpgradeOpen();
            ObjectId id = dst.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return id;
        }

        private static string EnsureLayer(Transaction tr, Database db, string name, short aci, LineWeight lw)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return name;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, aci),
                LineWeight = lw
            };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return name;
        }
    }
}
