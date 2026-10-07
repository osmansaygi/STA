using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Merdiven — plandan seçim: numara + çıkış hattı S grubu kadar beton kesit.
    /// Basamak = mod genişlik; rıht h = kat/adet (gerçek). 1:1.
    /// </summary>
    internal static class MerdivenRunner
    {
        private const string LayerMerdivenYazi = "MERDIVEN YAZI (BEYKENT)";
        private const string LayerKesitIsmi = "KESIT ISMI (BEYKENT)";
        private const string LayerMerdiven = "MERDIVEN (BEYKENT)";
        private const string TextStyleYazi = "YAZI (BEYKENT)";
        private const string LayerYazi = "YAZI (BEYKENT)";
        private const string LayerOlcu = "OLCU (BEYKENT)";
        private const string DimStylePlanOlcu25 = "PLAN_OLCU 25";

        private const double Tol = 0.05;
        private const double HitTol = 2.0;
        private const double NumaraPunto = 8.0;
        /// <summary>1/25 plan rıht numarası yazı yüksekliği.</summary>
        private const double PlanRihtNoPunto = 10.0;
        /// <summary>1/25 plan kolon/perde/kiriş etiket yüksekliği.</summary>
        private const double PlanEtiketPunto = 12.0;
        /// <summary>1/25 plan ölçü: çıkış hattından dışarı dik ofset (gerçek cm).</summary>
        private const double PlanOlcuOffsetCm = 40.0;
        /// <summary>Ölçü uzatma kolu (DIMEXE / DIMFXL) üst sınırı — çizim cm (1/25).</summary>
        private const double PlanOlcuDimExeCm = 5.0;
        /// <summary>1/25 plan rıht no: çıkış hattından dışarı dik (cm).</summary>
        private const double PlanRihtNoOutCm = 10.0;
        /// <summary>1/25 plan rıht no: S1→S2 yönünde rıht yanına kaydırma (cm).</summary>
        private const double PlanRihtNoAlongCm = 8.0;
        /// <summary>1/25 plan kol genişliği yazı yüksekliği.</summary>
        private const double PlanKolGenislikPunto = 12.0;
        private const double KolArasiCm = 80.0;
        /// <summary>Kutu kenarları arası boşluk (gerçek cm). 1/25 çizimde ×Scale.</summary>
        private const double KutuArasiCm = 30.0;
        /// <summary>1/25 çizim ölçeği (mantıksal cm → model; 1/50'ye göre ×2).</summary>
        private const double Scale = 2.0;
        /// <summary>Ölçekli plan kopyası ile kesitler arası boşluk (mantıksal cm).</summary>
        private const double PlanKesitGapCm = 80.0;
        private const double RayMaxCm = 500.0;
        /// <summary>Kolon/perde finiş: merdiven üst/alt yüzünden kesit sınırı ofseti (cm).</summary>
        private const double FinisKesitOffsetCm = 50.0;

        private const string LayerKiris = "KIRIS (BEYKENT)";
        private const string LayerPerde = "PERDE (BEYKENT)";
        private const string LayerKolon = "KOLON (BEYKENT)";
        private const string LayerKesitSiniri = "KESIT SINIRI (BEYKENT)";
        private const string LayerKesitCizgisi = "KESIT CIZGISI (BEYKENT)";
        private const string LayerDonati = "DONATI (BEYKENT)";
        private const string LayerDonatiYazi = "DONATI YAZISI (BEYKENT)";
        private const string LayerDosemeSiniri = "DOSEME SINIRI (BEYKENT)";
        private const string LayerKotCizgisi = "KOT CIZGISI (BEYKENT)";
        private const string LayerKotYazi = "KOT YAZI (BEYKENT)";
        private const double DonatiRadiusCm = 1.5;
        private const double DonatiAcilimOffsetCm = 120.0; // eski varsayılan; artık DonatiAcilimAsagiCm kullanılır

        /// <summary>Tevzi kesit çemberi çapı (cm).</summary>
        private const double TevziCircleDiaCm = 1.5;
        /// <summary>Tevzi çember polyline global width (cm).</summary>
        private const double TevziCircleWidthCm = 3.0;
        /// <summary>Tevzi baz: ana donatıdan kesit içine ofset (cm).</summary>
        private const double TevziBazOfsetCm = 1.5;

        /// <summary>Açılım etiketi adedi için aktif kol genişliği (cm).</summary>
        private static double _acilimKolGenislikCm = 120.0;

        /// <summary>
        /// Temelli kesitte tüm açılımlara eklenen aşağı ofset (cm).
        /// 1 ve 2. donatı tepesi temel alt çizgisinin 10 cm altında kalacak kadar.
        /// </summary>
        private static double _temelAcilimEkAsagiCm;

        /// <summary>Temelde 1 ve 2. açılımın kendi aşağı ofseti (ek ofset ayrıca eklenir).</summary>
        private static double _temelBar1AsagiCm = 100.0;
        private static double _temelBar2AsagiCm = 105.0;

        /// <summary>
        /// Basamak kesiti tevzi açılım adedi: ilk kolde 1–29, diğerinde 10–29
        /// (uçlar + ara kopyalar).
        /// </summary>
        private static int _basamakTevziAdet;

        // Tevzi baz numaraları (sabit — her tarif ayrı no, kırmızı yazı)
        private const int TevziNo_12Fillet = 1;
        private const int TevziNo_3Fillet = 2;
        private const int TevziNo_4Fillet = 3;
        private const int TevziNo_1Fillet = 4;
        private const int TevziNo_11x12Ofset = 5;
        private const int TevziNo_5Kirim = 6;
        private const int TevziNo_3x5Ofset = 7;
        private const int TevziNo_11Kirim = 8;
        private const int TevziNo_9KirimAlt = 9;
        private const int TevziNo_10Kirim = 10;
        private const int TevziNo_8KirimAlt = 11;
        private const int TevziNo_8KirimUst = 12;
        private const int TevziNo_9KirimUst = 13;
        private const int TevziNo_5x8Ofset = 14;
        private const int TevziNo_9x10Ofset = 15;
        private const int TevziNo_6GonyeSol = 16;
        private const int TevziNo_6GonyeSag = 17;
        private const int TevziNo_7GonyeSol = 18;
        private const int TevziNo_7GonyeSag = 19;
        private const int TevziNo_1Gonye = 20;
        private const int TevziNo_2Gonye = 21;
        private const int TevziNo_3Gonye = 22;
        private const int TevziNo_5Gonye = 23;
        private const int TevziNo_8GonyeSol = 24;
        private const int TevziNo_8GonyeSag = 25;
        private const int TevziNo_10Gonye = 26;
        private const int TevziNo_11KirimUstIz = 27; // 8'in izdüşümü: bölge1 üst − (pas+1.5)
        private const int TevziNo_12FilletUstIz = 28; // 1 → basamak üst (dik)
        private const int TevziNo_5KirimAltIz = 29;   // 6 → basamak alt (dik)
        private const int TevziNo_3FilletBotIz = 30;  // 2 → bölge3 alt
        private const int TevziNo_8KirimUstBotIz = 31; // 12 → bölge3 alt
        private const int TevziNo_10KirimUstIz = 32;   // 10 → basamak üst (dik)
        private const int TevziNo_9KirimAltUstIz = 33; // 9 → bölge1 üst

        private sealed class PathVert
        {
            public Point2d P;
            public double Bulge;
        }

        private sealed class PathHit
        {
            public double Dist;
            public Point2d Pt;
        }

        private sealed class PlanSupport
        {
            public bool IsKiris;
            public bool IsPerde;
            public bool IsKolon;
            public bool IsDosemeSiniri;
            /// <summary>Kiriş yüksekliği (cm); etiketten, yoksa 0.</summary>
            public double HeightCm;
            public List<(Point2d A, Point2d B)> Edges = new List<(Point2d, Point2d)>();
        }

        /// <summary>
        /// Sahanlık ucu: GapCm = ilk/son rıht → sınır yakın yüz (tam plan boyu).
        /// ThickCm = kolon/perde/kiriş kesit kalınlığı; döşeme sınırında 0.
        /// HeightCm = kiriş düşey boy (etiket veya form).
        /// </summary>
        private sealed class EndBoundary
        {
            public double GapCm;
            public double ThickCm;
            public double HeightCm;
            public bool IsKiris;
            public bool IsPerde;
            public bool IsKolon;
            public bool IsDosemeSiniri;
        }

        public static void RunInteractive()
        {
            MerdivenSettings.EnsureLoaded();

            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            try
            {
                var peo = new PromptSelectionOptions
                {
                    MessageForAdding = "\nMerdiven bölgesini seçin (çıkış + rıht/döşeme sınırı + kolon/perde/kiriş + kiriş ismi): ",
                    MessageForRemoval = "\nÇıkarılacak: ",
                    AllowDuplicates = false
                };
                PromptSelectionResult psr = ed.GetSelection(peo);
                if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
                {
                    ed.WriteMessage("\nMERDIVENCIZ: seçim iptal.");
                    return;
                }

                ObjectId[] selIds = psr.Value.GetObjectIds();
                List<PathVert> path;
                List<PathHit> hits;
                List<(int I0, int I1)> groups;
                List<PlanSupport> supports;
                List<(Point2d A, Point2d B)> rihtSegs;
                int nRiht;
                int nSeg;
                double basamakCm;
                double rihtH;

                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    if (!TryNumberSelection(tr, doc.Database, selIds,
                        out path, out hits, out groups, out supports, out rihtSegs,
                        out nRiht, out nSeg, out basamakCm, out rihtH, out string err))
                    {
                        ed.WriteMessage("\nMERDIVENCIZ: {0}", err ?? "numaralandırma başarısız.");
                        tr.Commit();
                        return;
                    }
                    tr.Commit();
                }

                ed.WriteMessage(
                    "\nMERDIVENCIZ: {0} rıht, {1} kol, basamak {2:0.#} cm, rıht h={3:0.00} cm, mesnet={4}.",
                    nRiht, nSeg, basamakCm, rihtH, supports != null ? supports.Count : 0);

                if (groups == null || groups.Count == 0)
                {
                    ed.WriteMessage("\nMERDIVENCIZ: kesit için S grubu yok.");
                    return;
                }

                var popt = new PromptPointOptions(
                    string.Format("\n{0} kesit + plan 1/25 — sol-alt köşe: ", groups.Count))
                {
                    AllowNone = false
                };
                PromptPointResult ppr = ed.GetPoint(popt);
                if (ppr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nMERDIVENCIZ: kesit iptal.");
                    return;
                }

                int drawn;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    EnsureLayer(tr, doc.Database, LayerKesitCizgisi, 5, LineWeight.LineWeight025, false);
                    EnsureLayer(tr, doc.Database, LayerDonati, 4, LineWeight.LineWeight040, false);
                    EnsureLayer(tr, doc.Database, LayerDonatiYazi, 3, LineWeight.LineWeight020, false);
                    EnsureLayer(tr, doc.Database, LayerKesitSiniri, 241, LineWeight.LineWeight020, true);
                    EnsureLayer(tr, doc.Database, LayerDosemeSiniri, 3, LineWeight.LineWeight030, false);

                    // Plan ve kesitler geçici yerde çizilir; sonra kutular sol-alta göre dizilir.
                    var btPlace = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                    var msPlace = (BlockTableRecord)tr.GetObject(btPlace[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    var beforePlan = SnapshotModelSpaceIds(msPlace);

                    Point3d kesitInsert = ppr.Value;
                    Matrix3d planXf = Matrix3d.Identity;
                    if (TryCopySelectionAsPlan125(tr, doc.Database, selIds, ppr.Value,
                            out planXf, out double planW, out double planH))
                    {
                        kesitInsert = new Point3d(
                            ppr.Value.X + planW + PlanKesitGapCm * Scale,
                            ppr.Value.Y, 0);
                        AnnotatePlan125RihtNos(tr, doc.Database, path, hits, planXf);
                        var sinirWorld = CollectKesitSiniriWorld(tr, msPlace);
                        AnnotatePlan125Olcu(
                            tr, doc.Database, path, hits, groups, supports, rihtSegs, planXf, sinirWorld);
                        AnnotatePlan125KolGenislik(
                            tr, doc.Database, path, hits, groups, supports, rihtSegs, planXf);
                        AnnotatePlan125KolIsimleri(
                            tr, doc.Database, path, groups, planXf, sinirWorld);
                    }

                    var planIds = new HashSet<ObjectId>();
                    foreach (ObjectId id in msPlace)
                    {
                        if (!beforePlan.Contains(id)) planIds.Add(id);
                    }

                    var sectionIds = new List<HashSet<ObjectId>>();
                    drawn = DrawKesitlerForGroups(
                        tr, doc.Database, kesitInsert, path, hits, groups, supports, rihtSegs,
                        basamakCm, rihtH, MerdivenSettings.DosemeKalinligiCm, sectionIds, beforePlan);

                    var stack = new List<HashSet<ObjectId>>();
                    if (planIds.Count > 0) stack.Add(planIds);
                    stack.AddRange(sectionIds);
                    ArrangeKutularBottomLeft(tr, ppr.Value, stack);
                    tr.Commit();
                }

                ed.WriteMessage("\nMERDIVENCIZ: {0} kesit + plan 1/25 çizildi (basamak {1:0.#}, rıht {2:0.00}).",
                    drawn, basamakCm, rihtH);
            }
            catch (Exception ex)
            {
                try { ed.WriteMessage("\nMERDIVENCIZ hata: {0}", ex.Message); }
                catch { }
            }
            // Form yeniden açılmaz — Enter/Space → MERDIVEN (form + çizim).
        }

        /// <summary>
        /// Seçimi DeepClone ile kopyalar, Scale (1/25) ile büyütür, sol-altı <paramref name="insert"/>'e oturtur.
        /// Kolon/perde/kiriş etiketleri 12 puntoya çekilir.
        /// </summary>
        private static bool TryCopySelectionAsPlan125(
            Transaction tr, Database db, ObjectId[] selIds, Point3d insert,
            out Matrix3d planXf, out double scaledWidth, out double scaledHeight)
        {
            planXf = Matrix3d.Identity;
            scaledWidth = 0;
            scaledHeight = 0;
            if (selIds == null || selIds.Length == 0) return false;

            var ids = new ObjectIdCollection();
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            bool any = false;

            foreach (ObjectId id in selIds)
            {
                if (id.IsNull || !id.IsValid) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null || ent.IsErased) continue;
                ids.Add(id);
                try
                {
                    Extents3d e = ent.GeometricExtents;
                    if (e.MinPoint.X < minX) minX = e.MinPoint.X;
                    if (e.MinPoint.Y < minY) minY = e.MinPoint.Y;
                    if (e.MaxPoint.X > maxX) maxX = e.MaxPoint.X;
                    if (e.MaxPoint.Y > maxY) maxY = e.MaxPoint.Y;
                    any = true;
                }
                catch { }
            }

            if (!any || ids.Count == 0) return false;

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var mapping = new IdMapping();
            db.DeepCloneObjects(ids, ms.ObjectId, mapping, false);

            // Önce min köşede Scale, sonra insert'e taşı
            planXf = Matrix3d.Displacement(new Vector3d(insert.X - minX, insert.Y - minY, 0))
                * Matrix3d.Scaling(Scale, new Point3d(minX, minY, 0));

            foreach (IdPair pair in mapping)
            {
                if (!pair.IsCloned || !pair.Value.IsValid) continue;
                if (!(tr.GetObject(pair.Value, OpenMode.ForWrite, false) is Entity clone)) continue;
                if (clone.IsErased) continue;
                try { clone.TransformBy(planXf); } catch { }
                try { ForcePlanEtiketHeight12(clone); } catch { }
            }

            scaledWidth = (maxX - minX) * Scale;
            scaledHeight = (maxY - minY) * Scale;
            return scaledWidth > Tol && scaledHeight > Tol;
        }

        /// <summary>
        /// Kutular yukarıdan aşağı: 1/25 plan, A, B, C… Sol kenarlar aynı hizada.
        /// Aralık 30 cm. Yerleşim noktası en alttaki kutunun sol-alt köşesi.
        /// </summary>
        private static void ArrangeKutularBottomLeft(
            Transaction tr, Point3d anchor, List<HashSet<ObjectId>> topToBottom)
        {
            if (tr == null || topToBottom == null || topToBottom.Count == 0) return;

            var boxes = new Extents3d[topToBottom.Count];
            var ok = new bool[topToBottom.Count];
            for (int i = 0; i < topToBottom.Count; i++)
                ok[i] = topToBottom[i] != null && TryOuterKutu(tr, topToBottom[i], out boxes[i]);

            double gap = KutuArasiCm * Scale;
            double y = anchor.Y;
            for (int i = topToBottom.Count - 1; i >= 0; i--)
            {
                if (!ok[i]) continue;
                double h = boxes[i].MaxPoint.Y - boxes[i].MinPoint.Y;
                double dx = anchor.X - boxes[i].MinPoint.X;
                double dy = y - boxes[i].MinPoint.Y;
                MoveIds(tr, topToBottom[i], dx, dy);
                y += h + gap;
            }
        }

        /// <summary>Grubun çizim çerçevesi. Orijine yapışık bozuk ölçü bloğu sayılmaz.</summary>
        private static bool TryOuterKutu(Transaction tr, HashSet<ObjectId> ids, out Extents3d box)
        {
            box = new Extents3d();
            if (ids == null || ids.Count == 0) return false;

            var parts = new List<Extents3d>();
            foreach (ObjectId id in ids)
            {
                if (!id.IsValid || id.IsErased) continue;
                Entity ent;
                try { ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity; }
                catch { continue; }
                if (ent == null || ent.IsErased) continue;
                if (ent is Dimension dim)
                {
                    try
                    {
                        if (!dim.IsWriteEnabled) dim.UpgradeOpen();
                        dim.RecomputeDimensionBlock(true);
                    }
                    catch { }
                }
                Extents3d ex;
                try { ex = ent.GeometricExtents; }
                catch { continue; }
                if (ex.MaxPoint.X - ex.MinPoint.X < 0.01 && ex.MaxPoint.Y - ex.MinPoint.Y < 0.01)
                    continue;
                parts.Add(ex);
            }
            if (parts.Count == 0) return false;

            bool away = false;
            foreach (var ex in parts)
            {
                if (!ExtentsTouchOrigin(ex)) { away = true; break; }
            }

            bool any = false;
            double minX = 0, minY = 0, maxX = 0, maxY = 0;
            foreach (var ex in parts)
            {
                if (away && ExtentsTouchOrigin(ex)) continue;
                if (!any)
                {
                    minX = ex.MinPoint.X; minY = ex.MinPoint.Y;
                    maxX = ex.MaxPoint.X; maxY = ex.MaxPoint.Y;
                    any = true;
                }
                else
                {
                    if (ex.MinPoint.X < minX) minX = ex.MinPoint.X;
                    if (ex.MinPoint.Y < minY) minY = ex.MinPoint.Y;
                    if (ex.MaxPoint.X > maxX) maxX = ex.MaxPoint.X;
                    if (ex.MaxPoint.Y > maxY) maxY = ex.MaxPoint.Y;
                }
            }
            if (!any) return false;
            box = new Extents3d(new Point3d(minX, minY, 0), new Point3d(maxX, maxY, 0));
            return true;
        }

        private static void MoveIds(Transaction tr, HashSet<ObjectId> ids, double dx, double dy)
        {
            if (ids == null) return;
            if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;
            var xf = Matrix3d.Displacement(new Vector3d(dx, dy, 0));
            foreach (ObjectId id in ids)
            {
                if (!id.IsValid || id.IsErased) continue;
                if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Entity ent) || ent.IsErased) continue;
                try { ent.TransformBy(xf); }
                catch { }
            }
        }

        private static bool IsPlanEtiketLayer(string lyr)
        {
            if (string.IsNullOrEmpty(lyr)) return false;
            if (LayerHas(lyr, "ISMI") || LayerHas(lyr, "ISIM")) return true;
            return false;
        }

        private static void ForcePlanEtiketHeight12(Entity ent)
        {
            if (ent == null) return;
            if (!IsPlanEtiketLayer(ent.Layer)) return;
            if (ent is DBText db)
            {
                db.Height = PlanEtiketPunto;
                try { db.AdjustAlignment(db.Database); } catch { }
            }
            else if (ent is MText mt)
            {
                mt.TextHeight = PlanEtiketPunto;
            }
        }

        /// <summary>
        /// 1/25 planda rıht no — YAZI 10pt;
        /// hattan 10 cm dışarı + S1→S2 yönünde 8 cm (rıht yanına).
        /// </summary>
        private static void AnnotatePlan125RihtNos(
            Transaction tr, Database db,
            List<PathVert> path, List<PathHit> hits, Matrix3d planXf)
        {
            if (path == null || hits == null || hits.Count == 0) return;
            EnsureLayer(tr, db, LayerYazi, 5, LineWeight.LineWeight020, false);
            ObjectId styleId = EnsureTextStyle(tr, db);
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            Point2d pathCen = default;
            if (path.Count > 0)
            {
                double sx = 0, sy = 0;
                for (int i = 0; i < path.Count; i++)
                {
                    sx += path[i].P.X;
                    sy += path[i].P.Y;
                }
                pathCen = new Point2d(sx / path.Count, sy / path.Count);
            }

            for (int i = 0; i < hits.Count; i++)
            {
                PointAtDist(path, hits[i].Dist, out Point2d onPath, out Vector2d tang);
                if (tang.Length < Tol) tang = new Vector2d(1, 0);
                else tang = tang.GetNormal();
                Vector2d nOut = OutwardNormalFromPath(tang, onPath, pathCen);
                // 10 cm dışarı + S1→S2 (artan Dist) yönünde 8 cm
                Point2d p = onPath + nOut * PlanRihtNoOutCm + tang * PlanRihtNoAlongCm;
                Point3d w = new Point3d(p.X, p.Y, 0).TransformBy(planXf);
                var t = new DBText
                {
                    TextString = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Height = PlanRihtNoPunto,
                    Layer = LayerYazi,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextVerticalMid,
                    AlignmentPoint = w,
                    Color = Color.FromColorIndex(ColorMethod.ByLayer, 256)
                };
                if (!styleId.IsNull)
                    t.TextStyleId = styleId;
                ms.AppendEntity(t);
                tr.AddNewlyCreatedDBObject(t, true);
                try
                {
                    t.AdjustAlignment(db);
                    Extents3d ext = t.GeometricExtents;
                    double cx = 0.5 * (ext.MinPoint.X + ext.MaxPoint.X);
                    double cy = 0.5 * (ext.MinPoint.Y + ext.MaxPoint.Y);
                    t.TransformBy(Matrix3d.Displacement(new Vector3d(w.X - cx, w.Y - cy, 0)));
                }
                catch { }
            }
        }

        /// <summary>
        /// Kol genişliği: basamaklı → rıht çizgi boylarının modu;
        /// basamaksız → max(önceki kol üst sahanlık, sonraki kol ilk sahanlık).
        /// 1/25 planda kol üzerine yazar.
        /// </summary>
        private static void AnnotatePlan125KolGenislik(
            Transaction tr, Database db,
            List<PathVert> path, List<PathHit> hits,
            List<(int I0, int I1)> groups, List<PlanSupport> supports,
            List<(Point2d A, Point2d B)> rihtSegs,
            Matrix3d planXf)
        {
            if (path == null || groups == null || groups.Count == 0) return;
            double[] widths = ComputeKolGenisliklerCm(path, hits, groups, supports, rihtSegs);
            if (widths == null) return;

            EnsureLayer(tr, db, LayerYazi, 5, LineWeight.LineWeight020, false);
            ObjectId styleId = EnsureTextStyle(tr, db);
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            Point2d pathCen = default;
            if (path.Count > 0)
            {
                double sx = 0, sy = 0;
                for (int i = 0; i < path.Count; i++)
                {
                    sx += path[i].P.X;
                    sy += path[i].P.Y;
                }
                pathCen = new Point2d(sx / path.Count, sy / path.Count);
            }

            for (int g = 0; g < groups.Count; g++)
            {
                double w = widths[g];
                if (w < 5.0) continue;
                int wCm = (int)Math.Round(w, MidpointRounding.AwayFromZero);

                GetGroupDistRange(path, groups[g].I0, groups[g].I1, out double d0, out double d1);
                PointAtDist(path, 0.5 * (d0 + d1), out Point2d mid, out Vector2d tang);
                if (tang.Length < Tol) tang = new Vector2d(1, 0);
                else tang = tang.GetNormal();
                Vector2d nOut = OutwardNormalFromPath(tang, mid, pathCen);
                Point2d labelPt = mid - nOut * 15.0;
                Point3d world = new Point3d(labelPt.X, labelPt.Y, 0).TransformBy(planXf);

                string txt = wCm.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var t = new DBText
                {
                    TextString = txt,
                    Height = PlanKolGenislikPunto,
                    Layer = LayerYazi,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextVerticalMid,
                    AlignmentPoint = world,
                    Color = Color.FromColorIndex(ColorMethod.ByLayer, 256)
                };
                if (!styleId.IsNull)
                    t.TextStyleId = styleId;
                double ang = Math.Atan2(tang.Y, tang.X);
                if (ang > Math.PI * 0.5 + 1e-6 || ang < -Math.PI * 0.5 - 1e-6)
                    ang += Math.PI;
                t.Rotation = ang;
                ms.AppendEntity(t);
                tr.AddNewlyCreatedDBObject(t, true);
                try
                {
                    t.AdjustAlignment(db);
                    Extents3d ext = t.GeometricExtents;
                    double cx = 0.5 * (ext.MinPoint.X + ext.MaxPoint.X);
                    double cy = 0.5 * (ext.MinPoint.Y + ext.MaxPoint.Y);
                    t.TransformBy(Matrix3d.Displacement(new Vector3d(world.X - cx, world.Y - cy, 0)));
                }
                catch { }
            }
        }

        /// <summary>Kol sırası: A, B, C, … Z, AA, AB, …</summary>
        private static string KolHarf(int index)
        {
            if (index < 0) index = 0;
            if (index < 26)
                return ((char)('A' + index)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            int hi = index / 26 - 1;
            int lo = index % 26;
            return ((char)('A' + hi)).ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ((char)('A' + lo)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 1/25 planda kesit sembolü (gömülü kesit_sembol.dwg), ölçek uygulanmaz.
        /// Harf = kol adı. Kesik çizgi kolun içine bakar; üçgen bakış yönünü gösterir.
        /// Daire dış kesit sınırının 5 cm dışında.
        /// </summary>
        private const string KesitSembolResourceName = "ST4Yardimci.KesitSembol.kesit_sembol.dwg";
        private const double KesitSembolOx = 1660.7;
        private const double KesitSembolOy = 1206.23;

        private static void AnnotatePlan125KolIsimleri(
            Transaction tr, Database db,
            List<PathVert> path, List<(int I0, int I1)> groups, Matrix3d planXf,
            List<(Point2d A, Point2d B)> sinirWorld)
        {
            if (path == null || groups == null || groups.Count == 0) return;
            string srcPath = EnsureKesitSembolTempDwg();
            if (string.IsNullOrEmpty(srcPath)) return;
            if (sinirWorld == null) sinirWorld = new List<(Point2d A, Point2d B)>();

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

                for (int g = 0; g < groups.Count; g++)
                {
                    int i0 = groups[g].I0;
                    int i1 = groups[g].I1;
                    if (i0 < 0 || i1 < i0 || i1 >= path.Count - 1) continue;
                    string name = KolHarf(g);
                    Vector2d t0 = TangentOnBulge(path[i0].P, path[i0 + 1].P, path[i0].Bulge, 0);
                    Vector2d t1 = TangentOnBulge(path[i1].P, path[i1 + 1].P, path[i1].Bulge, 1.0);
                    if (t0.Length > Tol) t0 = t0.GetNormal();
                    else t0 = new Vector2d(1, 0);
                    if (t1.Length > Tol) t1 = t1.GetNormal();
                    else t1 = t0;
                    Vector2d bakis = PreferBakısDir(GroupCutAxis(path, i0, i1));
                    PlacePlanKesitSembol(tr, db, src, srcIds, name, path[i0].P, t0, bakis, planXf, sinirWorld);
                    PlacePlanKesitSembol(tr, db, src, srcIds, name, path[i1 + 1].P, -t1, bakis, planXf, sinirWorld);
                }
            }
        }

        /// <summary>Şablondaki daire yarıçapı (çizim birimi; 1/25 ölçeği uygulanmaz).</summary>
        private const double KesitSembolYaricap = 20.0;
        /// <summary>Daire kenarı ile dış kesit sınırı arası (gerçek cm).</summary>
        private const double KesitSembolSinirPayCm = 5.0;

        /// <summary>
        /// Kesik çizgi (şablonda +X) <paramref name="inDir"/> yönüne bakar.
        /// Üçgen, kesitin bakış yönüne (<paramref name="bakis"/>) döner.
        /// Daire, dış kesit sınırının dışında; arada 5 cm. Sembol 2 kat büyütülmez.
        /// </summary>
        private static void PlacePlanKesitSembol(
            Transaction tr, Database db, Database src, ObjectIdCollection srcIds,
            string letter, Point2d endPt, Vector2d inDir, Vector2d bakis, Matrix3d planXf,
            List<(Point2d A, Point2d B)> sinirWorld)
        {
            if (src == null || srcIds == null || srcIds.Count == 0) return;
            if (inDir.Length < Tol) inDir = new Vector2d(1, 0);
            else inDir = inDir.GetNormal();

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            Point3d endW = new Point3d(endPt.X, endPt.Y, 0).TransformBy(planXf);
            Point3d centerW = endW;
            if (TryOuterKesitSiniri(sinirWorld, endW, inDir, out Point3d hitW))
            {
                double disari = KesitSembolYaricap + KesitSembolSinirPayCm * Scale;
                centerW = new Point3d(
                    hitW.X - inDir.X * disari,
                    hitW.Y - inDir.Y * disari,
                    0);
            }

            var mapping = new IdMapping();
            db.WblockCloneObjects(srcIds, ms.ObjectId, mapping, DuplicateRecordCloning.Ignore, false);

                var origin = new Point3d(KesitSembolOx, KesitSembolOy, 0);
                double ang = Math.Atan2(inDir.Y, inDir.X);
                // Şablonda üçgen yerel +Y'de. Kesik çizgi içeri bakınca bu taraf bakışla
                // ters ise X eksenine göre aynala; harf düz kalır.
                if (bakis.Length < Tol) bakis = new Vector2d(-inDir.Y, inDir.X);
                else bakis = bakis.GetNormal();
                var tri = new Vector2d(-inDir.Y, inDir.X);
                bool mirrorTri = tri.X * bakis.X + tri.Y * bakis.Y < 0;
                Matrix3d xf = Matrix3d.Displacement(centerW - origin)
                    * Matrix3d.Rotation(ang, Vector3d.ZAxis, origin);
                if (mirrorTri)
                    xf = xf * Matrix3d.Mirroring(new Plane(origin, Vector3d.YAxis));

                foreach (IdPair pair in mapping)
                {
                    if (!pair.IsCloned || !pair.Value.IsValid) continue;
                    if (!(tr.GetObject(pair.Value, OpenMode.ForWrite, false) is Entity ent)) continue;
                    if (ent.IsErased) continue;
                    if (ent is DBText txt)
                    {
                        txt.TextString = letter;
                        txt.Rotation = 0;
                        try { txt.AdjustAlignment(db); } catch { }
                        try { txt.TransformBy(Matrix3d.Displacement(centerW - origin)); } catch { }
                        continue;
                    }
                    try { ent.TransformBy(xf); } catch { }
                }
        }

        /// <summary>
        /// Kol ekseninin dış kesit sınırı ile kesişimi.
        /// İçeri (inDir) negatif parametre: kol ucundan dışarıdaki en yakın sınır.
        /// </summary>
        private static bool TryOuterKesitSiniri(
            List<(Point2d A, Point2d B)> sinirWorld, Point3d endW, Vector2d inDir, out Point3d hitW)
        {
            hitW = endW;
            if (sinirWorld == null || sinirWorld.Count == 0) return false;
            double bestOut = double.NegativeInfinity;
            double bestIn = double.PositiveInfinity;
            Point3d hitOut = endW, hitIn = endW;
            bool anyOut = false, anyIn = false;

            foreach (var seg in sinirWorld)
            {
                if (!TryAxisSegmentHit(endW, inDir, seg.A, seg.B, out double t, out Point3d hit))
                    continue;
                if (t < -0.5)
                {
                    if (!anyOut || t > bestOut) { bestOut = t; hitOut = hit; anyOut = true; }
                }
                else if (t > 0.5)
                {
                    if (!anyIn || t < bestIn) { bestIn = t; hitIn = hit; anyIn = true; }
                }
            }

            if (anyOut) { hitW = hitOut; return true; }
            if (anyIn) { hitW = hitIn; return true; }
            return false;
        }

        private static bool TryAxisSegmentHit(
            Point3d origin, Vector2d dir, Point2d a, Point2d b, out double t, out Point3d hit)
        {
            t = 0;
            hit = origin;
            double ex = b.X - a.X, ey = b.Y - a.Y;
            double den = dir.X * ey - dir.Y * ex;
            if (Math.Abs(den) < 1e-9) return false;
            double ox = a.X - origin.X, oy = a.Y - origin.Y;
            t = (ox * ey - oy * ex) / den;
            double u = (ox * dir.Y - oy * dir.X) / den;
            if (u < -0.02 || u > 1.02) return false;
            hit = new Point3d(origin.X + dir.X * t, origin.Y + dir.Y * t, 0);
            return true;
        }

        private static string EnsureKesitSembolTempDwg()
        {
            Assembly asm = typeof(MerdivenRunner).Assembly;
            using (Stream stream = asm.GetManifestResourceStream(KesitSembolResourceName))
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

        /// <summary>Her S grubu için kol genişliği (cm).</summary>
        private static double[] ComputeKolGenisliklerCm(
            List<PathVert> path, List<PathHit> hits,
            List<(int I0, int I1)> groups, List<PlanSupport> supports,
            List<(Point2d A, Point2d B)> rihtSegs)
        {
            if (groups == null || groups.Count == 0) return null;
            var groupHits = AssignHitsToGroups(path, hits, groups);
            int n = groups.Count;
            var widths = new double[n];
            var solGap = new double[n];
            var sagGap = new double[n];
            var isFlight = new bool[n];

            for (int g = 0; g < n; g++)
            {
                List<PathHit> gHits = groupHits[g];
                isFlight[g] = gHits != null && gHits.Count >= 1;
                if (isFlight[g])
                {
                    TryModeRihtBoyCm(gHits, rihtSegs, out widths[g]);
                    TryFlightSahanlikGaps(path, gHits, supports, out solGap[g], out sagGap[g]);
                }
            }

            for (int g = 0; g < n; g++)
            {
                if (isFlight[g]) continue;
                double prevUst = 0, nextIlk = 0;
                for (int p = g - 1; p >= 0; p--)
                {
                    if (!isFlight[p]) continue;
                    prevUst = sagGap[p];
                    break;
                }
                for (int q = g + 1; q < n; q++)
                {
                    if (!isFlight[q]) continue;
                    nextIlk = solGap[q];
                    break;
                }
                widths[g] = Math.Max(prevUst, nextIlk);
            }
            return widths;
        }

        /// <summary>Grup rıht çizgi boylarının modu (en çok tekrarlayan boy = kol genişliği).</summary>
        private static bool TryModeRihtBoyCm(
            List<PathHit> gHits,
            List<(Point2d A, Point2d B)> rihts,
            out double modeCm)
        {
            modeCm = 0;
            if (gHits == null || gHits.Count < 1 || rihts == null) return false;

            var counts = new Dictionary<int, int>();
            var sumByKey = new Dictionary<int, double>();

            foreach (var h in gHits)
            {
                if (!TryFindRihtSegAt(h.Pt, rihts, out var seg)) continue;
                double len = seg.A.GetDistanceTo(seg.B);
                if (len < 40.0 || len > 600.0) continue;
                int key = (int)Math.Round(len, MidpointRounding.AwayFromZero);
                if (key < 1) continue;
                if (!counts.ContainsKey(key))
                {
                    counts[key] = 0;
                    sumByKey[key] = 0;
                }
                counts[key]++;
                sumByKey[key] += len;
            }

            if (counts.Count == 0) return false;

            int bestKey = -1;
            int bestCnt = 0;
            foreach (var kv in counts)
            {
                if (kv.Value > bestCnt || (kv.Value == bestCnt && kv.Key > bestKey))
                {
                    bestCnt = kv.Value;
                    bestKey = kv.Key;
                }
            }
            if (bestKey < 0) return false;
            modeCm = sumByKey[bestKey] / bestCnt;
            modeCm = Math.Round(modeCm, MidpointRounding.AwayFromZero);
            return modeCm >= 40.0;
        }

        /// <summary>Basamaklı kol: sol (ilk) / sağ (üst) sahanlık GapCm.</summary>
        private static bool TryFlightSahanlikGaps(
            List<PathVert> path, List<PathHit> gHits, List<PlanSupport> supports,
            out double solGap, out double sagGap)
        {
            solGap = 0;
            sagGap = 0;
            if (path == null || gHits == null || gHits.Count < 1) return false;

            PathHit first = gHits[0];
            PathHit last = gHits[gHits.Count - 1];
            PointAtDist(path, first.Dist, out Point2d pFirst, out Vector2d tangFirst);
            PointAtDist(path, last.Dist, out Point2d pLast, out Vector2d tangLast);
            if (tangFirst.Length > 1e-12) tangFirst = tangFirst.GetNormal();
            else tangFirst = new Vector2d(1, 0);
            if (tangLast.Length > 1e-12) tangLast = tangLast.GetNormal();
            else tangLast = tangFirst;

            if (TryEndBoundary(pFirst, tangFirst, supports, forward: false, out EndBoundary sol) && sol != null)
                solGap = sol.GapCm;
            if (TryEndBoundary(pLast, tangLast, supports, forward: true, out EndBoundary sag) && sag != null)
                sagGap = sag.GapCm;
            return solGap > 0 || sagGap > 0;
        }

        /// <summary>
        /// 1/25 plan ölçü: çıkış dışı 40 cm; basamaksız dıştan-dışa;
        /// basamaklı kesit uzantısı (rıht dış + mesnet dış yüz).
        /// Bileşik kolda her düz segmentin kendi dışına; yaylara ölçü yok.
        /// </summary>
        private static void AnnotatePlan125Olcu(
            Transaction tr, Database db,
            List<PathVert> path, List<PathHit> hits,
            List<(int I0, int I1)> groups, List<PlanSupport> supports,
            List<(Point2d A, Point2d B)> rihtSegs,
            Matrix3d planXf, List<(Point2d A, Point2d B)> sinirWorld)
        {
            if (path == null || groups == null || groups.Count == 0) return;
            EnsureLayer(tr, db, LayerOlcu, 3, LineWeight.LineWeight030, false);
            ObjectId dimStyleId = ResolveOrCreatePlanOlcu25(tr, db);
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var supportEdges = new List<(Point2d A, Point2d B)>();
            if (supports != null)
            {
                foreach (var s in supports)
                {
                    if (s?.Edges == null) continue;
                    supportEdges.AddRange(s.Edges);
                }
            }
            List<PathHit> supportHits = CollectHitsAlongPath(path, supportEdges);
            var groupHits = AssignHitsToGroups(path, hits, groups);
            List<(Point2d A, Point2d B)> sinirLocal = CollectCopiedKesitSiniriLocal(sinirWorld, path, planXf);

            Point2d pathCen = default;
            if (path.Count > 0)
            {
                double sx = 0, sy = 0;
                for (int i = 0; i < path.Count; i++)
                {
                    sx += path[i].P.X;
                    sy += path[i].P.Y;
                }
                pathCen = new Point2d(sx / path.Count, sy / path.Count);
            }

            for (int g = 0; g < groups.Count; g++)
            {
                int i0 = groups[g].I0;
                int i1 = groups[g].I1;
                GetGroupDistRange(path, i0, i1, out double d0, out double d1);
                Vector2d cutAxis = GroupCutAxis(path, i0, i1);
                if (cutAxis.Length < 1e-12) cutAxis = new Vector2d(1, 0);
                else cutAxis = cutAxis.GetNormal();

                PointAtDist(path, 0.5 * (d0 + d1), out Point2d midPath, out _);
                Vector2d nOut = OutwardNormalFromPath(cutAxis, midPath, pathCen);

                List<PathHit> gHits = groupHits != null && g < groupHits.Length ? groupHits[g] : null;
                bool isLanding = gHits == null || gHits.Count < 1;

                var stations = new List<(double Dist, Point2d Pt)>();
                void AddSt(double d, Point2d p)
                {
                    for (int k = 0; k < stations.Count; k++)
                    {
                        if (Math.Abs(stations[k].Dist - d) <= 1.5) return;
                    }
                    stations.Add((d, p));
                }

                if (isLanding)
                {
                    // Basamaksız: kesit gibi düşey dıştan-dışa
                    MeasureLandingToSupportsCm(path, d0, d1, supports,
                        out EndBoundary endNeg, out EndBoundary endPos);
                    PointAtDist(path, d0, out Point2d p0, out _);
                    PointAtDist(path, d1, out Point2d p1, out _);
                    Point2d origin = new Point2d((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5);
                    var landAxis = new Vector2d(0, 1);
                    if (endNeg != null)
                    {
                        double gap = Math.Max(0, endNeg.GapCm);
                        double th = Math.Max(0, endNeg.ThickCm);
                        if (th > 0.5)
                            AddSt(-gap, origin - landAxis * gap);
                        AddSt(-(gap + th), origin - landAxis * (gap + th));
                    }
                    if (endPos != null)
                    {
                        double gap = Math.Max(0, endPos.GapCm);
                        double th = Math.Max(0, endPos.ThickCm);
                        if (th > 0.5)
                            AddSt(gap, origin + landAxis * gap);
                        AddSt(gap + th, origin + landAxis * (gap + th));
                    }
                    // Ara destek yüzleri (varsa)
                    if (supportHits != null)
                    {
                        foreach (var h in supportHits)
                        {
                            if (h.Dist < d0 - 2 || h.Dist > d1 + 2) continue;
                            // Düşey projeksiyon: origin'den Y
                            double ty = h.Pt.Y - origin.Y;
                            AddSt(ty, new Point2d(origin.X, h.Pt.Y));
                        }
                    }

                    if (stations.Count < 2) continue;
                    stations.Sort((a, b) => a.Dist.CompareTo(b.Dist));
                    Point2d dimLineLocal = midPath + nOut * PlanOlcuOffsetCm;
                    Point3d dimLineW = new Point3d(dimLineLocal.X, dimLineLocal.Y, 0).TransformBy(planXf);
                    double rotation = Math.PI * 0.5;
                    for (int i = 0; i < stations.Count - 1; i++)
                    {
                        Point2d a = stations[i].Pt;
                        Point2d b = stations[i + 1].Pt;
                        if (PlanOlcuShowsZero(a, b, landAxis)) continue;
                        Point3d wa = new Point3d(a.X, a.Y, 0).TransformBy(planXf);
                        Point3d wb = new Point3d(b.X, b.Y, 0).TransformBy(planXf);
                        PlacePlanOlcuDimFixedExt(
                            tr, ms, wa, wb, dimLineW, rotation, dimStyleId, LayerOlcu);
                    }
                    continue;
                }

                // Basamaklı zincir. Bileşikte her düz segment kendi ekseninde, kendi dışında.
                // Bileşikte zincir dış kesit sınırına kadar gider (sınır noktası yok);
                // bu hatta kesilen rıhtlar dış uçlarından eklenir.
                void PlaceFlightChain(double sd0, double sd1, Vector2d axis, bool includeSol, bool includeSag, bool extendToSinir)
                {
                    if (axis.Length < 1e-12) axis = new Vector2d(1, 0);
                    else axis = axis.GetNormal();
                    PointAtDist(path, 0.5 * (sd0 + sd1), out Point2d midSeg, out _);
                    Vector2d nSeg = OutwardNormalFromPath(axis, midSeg, pathCen);

                    var segStations = new List<(double Dist, Point2d Pt)>();
                    double Proj(Point2d p) =>
                        (p.X - midSeg.X) * axis.X + (p.Y - midSeg.Y) * axis.Y;
                    void AddSeg(Point2d p)
                    {
                        double d = Proj(p);
                        for (int k = 0; k < segStations.Count; k++)
                        {
                            if (Math.Abs(segStations[k].Dist - d) <= 1.5) return;
                        }
                        segStations.Add((d, p));
                    }

                    if (hits != null)
                    {
                        foreach (var h in hits)
                        {
                            if (h.Dist < sd0 - HitTol || h.Dist > sd1 + HitTol) continue;
                            AddSeg(OuterRihtVertex(h.Pt, rihtSegs, nSeg));
                        }
                    }
                    if (supportHits != null)
                    {
                        foreach (var h in supportHits)
                        {
                            if (h.Dist < sd0 - 2.0 || h.Dist > sd1 + 2.0) continue;
                            Point2d outer = SnapPointToOuterSide(h.Pt, nSeg, segStations);
                            if (segStations.Count == 0)
                                outer = OuterRihtVertex(h.Pt, rihtSegs, nSeg);
                            AddSeg(outer);
                        }
                    }

                    if (gHits != null && gHits.Count > 0 && (includeSol || includeSag))
                    {
                        if (includeSol)
                        {
                            PathHit firstHit = gHits[0];
                            PointAtDist(path, firstHit.Dist, out Point2d pFirst, out Vector2d tangFirst);
                            if (tangFirst.Length > 1e-12) tangFirst = tangFirst.GetNormal();
                            else tangFirst = axis;
                            if (TryEndBoundary(pFirst, tangFirst, supports, forward: false, out EndBoundary sol) && sol != null)
                            {
                                Point2d near = pFirst - tangFirst * sol.GapCm;
                                Point2d far = near - tangFirst * Math.Max(0, sol.ThickCm);
                                AddSeg(SnapPointToOuterSide(far, nSeg, segStations));
                                if (sol.ThickCm > 0.5)
                                    AddSeg(SnapPointToOuterSide(near, nSeg, segStations));
                            }
                        }
                        if (includeSag)
                        {
                            PathHit lastHit = gHits[gHits.Count - 1];
                            PointAtDist(path, lastHit.Dist, out Point2d pLast, out Vector2d tangLast);
                            if (tangLast.Length > 1e-12) tangLast = tangLast.GetNormal();
                            else tangLast = axis;
                            if (TryEndBoundary(pLast, tangLast, supports, forward: true, out EndBoundary sag) && sag != null)
                            {
                                Point2d near = pLast + tangLast * sag.GapCm;
                                Point2d far = near + tangLast * Math.Max(0, sag.ThickCm);
                                if (sag.ThickCm > 0.5)
                                    AddSeg(SnapPointToOuterSide(near, nSeg, segStations));
                                AddSeg(SnapPointToOuterSide(far, nSeg, segStations));
                            }
                        }
                    }

                    if (extendToSinir)
                        AddRihtsCutByExtendedOlcu(
                            segStations, midSeg, axis, nSeg, sinirLocal, rihtSegs, supports, AddSeg);

                    if (segStations.Count < 2) return;
                    segStations.Sort((a, b) => a.Dist.CompareTo(b.Dist));

                    Point2d dimLineLocal = midSeg + nSeg * PlanOlcuOffsetCm;
                    Point3d dimLineW = new Point3d(dimLineLocal.X, dimLineLocal.Y, 0).TransformBy(planXf);
                    double rotation = Math.Atan2(axis.Y, axis.X);
                    for (int s = 0; s < segStations.Count - 1; s++)
                    {
                        Point2d a = segStations[s].Pt;
                        Point2d b = segStations[s + 1].Pt;
                        if (PlanOlcuShowsZero(a, b, axis)) continue;
                        Point3d wa = new Point3d(a.X, a.Y, 0).TransformBy(planXf);
                        Point3d wb = new Point3d(b.X, b.Y, 0).TransformBy(planXf);
                        PlacePlanOlcuDimFixedExt(
                            tr, ms, wa, wb, dimLineW, rotation, dimStyleId, LayerOlcu);
                    }
                }

                if (GroupHasStraightAndArc(path, i0, i1))
                {
                    double acc = d0;
                    for (int i = i0; i <= i1; i++)
                    {
                        double len = SegLen(path, i);
                        if (IsStraightSeg(path, i) && len > Tol)
                        {
                            Vector2d segDir = path[i + 1].P - path[i].P;
                            PlaceFlightChain(acc, acc + len, segDir, i == i0, i == i1, true);
                        }
                        acc += len;
                    }
                }
                else
                    PlaceFlightChain(d0, d1, cutAxis, true, true, false);
            }
        }

        /// <summary>Düz + yay (bileşik kol): yay hariç her düz parçaya ayrı ölçü.</summary>
        private static bool GroupHasStraightAndArc(List<PathVert> path, int i0, int i1)
        {
            if (path == null || i1 <= i0) return false;
            bool straight = false, arc = false;
            for (int i = i0; i <= i1 && i < path.Count - 1; i++)
            {
                if (IsArcSeg(path, i)) arc = true;
                else straight = true;
            }
            return straight && arc;
        }

        private static List<(Point2d A, Point2d B)> CollectKesitSiniriWorld(
            Transaction tr, BlockTableRecord ms)
        {
            var list = new List<(Point2d A, Point2d B)>();
            if (tr == null || ms == null) return list;
            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased) continue;
                Entity ent;
                try { ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity; }
                catch { continue; }
                if (ent == null || ent.IsErased) continue;
                if (!string.Equals(ent.Layer, LayerKesitSiniri, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (ent is Line ln)
                    list.Add((new Point2d(ln.StartPoint.X, ln.StartPoint.Y), new Point2d(ln.EndPoint.X, ln.EndPoint.Y)));
                else if (ent is Polyline pl)
                {
                    int n = pl.NumberOfVertices;
                    int lim = pl.Closed ? n : n - 1;
                    for (int i = 0; i < lim; i++)
                        list.Add((pl.GetPoint2dAt(i), pl.GetPoint2dAt((i + 1) % n)));
                }
            }
            return list;
        }

        /// <summary>
        /// Kopyalanmış 1/25 planın kesit sınırı, yerel (seçim) koordinatında.
        /// Orijinal plandaki sınır, planXf tersinden sonra yol kutusunun dışında kalır.
        /// </summary>
        private static List<(Point2d A, Point2d B)> CollectCopiedKesitSiniriLocal(
            List<(Point2d A, Point2d B)> sinirWorld, List<PathVert> path, Matrix3d planXf)
        {
            var list = new List<(Point2d A, Point2d B)>();
            if (sinirWorld == null || sinirWorld.Count == 0 || path == null || path.Count == 0) return list;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < path.Count; i++)
            {
                if (path[i].P.X < minX) minX = path[i].P.X;
                if (path[i].P.Y < minY) minY = path[i].P.Y;
                if (path[i].P.X > maxX) maxX = path[i].P.X;
                if (path[i].P.Y > maxY) maxY = path[i].P.Y;
            }
            const double margin = 8000.0;
            minX -= margin;
            minY -= margin;
            maxX += margin;
            maxY += margin;

            Matrix3d inv;
            try { inv = planXf.Inverse(); }
            catch { return list; }

            bool Inside(Point3d p) =>
                p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY;

            foreach (var seg in sinirWorld)
            {
                var aL = new Point3d(seg.A.X, seg.A.Y, 0).TransformBy(inv);
                var bL = new Point3d(seg.B.X, seg.B.Y, 0).TransformBy(inv);
                if (!Inside(aL) && !Inside(bL)) continue;
                list.Add((new Point2d(aL.X, aL.Y), new Point2d(bL.X, bL.Y)));
            }
            return list;
        }

        /// <summary>Eksen boyu 0,5 cm altı PLAN_OLCU 25'te "0" yazar; çizilmez.</summary>
        private static bool PlanOlcuShowsZero(Point2d a, Point2d b, Vector2d axis)
        {
            if (axis.Length < 1e-12) return true;
            axis = axis.GetNormal();
            double along = (b.X - a.X) * axis.X + (b.Y - a.Y) * axis.Y;
            return Math.Abs(along) < 0.5;
        }

        /// <summary>
        /// Düz parçanın ölçü hattını iki yönde dış kesit sınırına kadar uzatır.
        /// Sınırın kendisi istasyon olmaz. Hatta kesilen rıht dış ucundan,
        /// kiriş / perde / kolon / döşeme sınırı ise kesişim noktasından eklenir.
        /// </summary>
        private static void AddRihtsCutByExtendedOlcu(
            List<(double Dist, Point2d Pt)> stations,
            Point2d midSeg, Vector2d axis, Vector2d nSeg,
            List<(Point2d A, Point2d B)> sinirLocal,
            List<(Point2d A, Point2d B)> rihts,
            List<PlanSupport> supports,
            Action<Point2d> add)
        {
            if (add == null) return;
            if (sinirLocal == null || sinirLocal.Count == 0) return;
            if (axis.Length < 1e-12) return;
            axis = axis.GetNormal();

            double off = PlanOlcuOffsetCm;
            if (stations != null && stations.Count > 0)
            {
                double best = double.MinValue;
                foreach (var st in stations)
                {
                    double o = st.Pt.X * nSeg.X + st.Pt.Y * nSeg.Y;
                    if (o > best) best = o;
                }
                double mid = midSeg.X * nSeg.X + midSeg.Y * nSeg.Y;
                if (best - mid > 1.0) off = best - mid;
            }

            var origin = new Point3d(
                midSeg.X + nSeg.X * off,
                midSeg.Y + nSeg.Y * off,
                0);

            double tPos = double.PositiveInfinity;
            double tNeg = double.NegativeInfinity;
            bool hasPos = false, hasNeg = false;
            foreach (var seg in sinirLocal)
            {
                if (!TryAxisSegmentHit(origin, axis, seg.A, seg.B, out double t, out _))
                    continue;
                if (t > 1.0 && t < tPos) { tPos = t; hasPos = true; }
                else if (t < -1.0 && t > tNeg) { tNeg = t; hasNeg = true; }
            }
            if (!hasPos && !hasNeg) return;
            if (hasPos) tPos -= 2.0;
            if (hasNeg) tNeg += 2.0;

            double spanMin = 0, spanMax = 0;
            bool anySpan = stations != null && stations.Count > 0;
            if (anySpan)
            {
                spanMin = spanMax = stations[0].Dist;
                for (int i = 1; i < stations.Count; i++)
                {
                    if (stations[i].Dist < spanMin) spanMin = stations[i].Dist;
                    if (stations[i].Dist > spanMax) spanMax = stations[i].Dist;
                }
            }
            double lo = hasNeg ? tNeg : (anySpan ? spanMin - 2.0 : -2.0);
            double hi = hasPos ? tPos : (anySpan ? spanMax + 2.0 : 2.0);
            if (hi <= lo) return;

            if (rihts != null)
            {
                foreach (var r in rihts)
                {
                    if (!TryAxisSegmentHit(origin, axis, r.A, r.B, out double t, out _))
                        continue;
                    if (t < lo || t > hi) continue;
                    double sa = r.A.X * nSeg.X + r.A.Y * nSeg.Y;
                    double sb = r.B.X * nSeg.X + r.B.Y * nSeg.Y;
                    Point2d outer = sa >= sb ? r.A : r.B;
                    double tOuter = (outer.X - midSeg.X) * axis.X + (outer.Y - midSeg.Y) * axis.Y;
                    if (tOuter < lo || tOuter > hi) continue;
                    add(outer);
                }
            }

            if (supports == null) return;
            foreach (var s in supports)
            {
                if (s?.Edges == null) continue;
                if (!s.IsKiris && !s.IsPerde && !s.IsKolon && !s.IsDosemeSiniri) continue;
                foreach (var e in s.Edges)
                {
                    if (!TryAxisSegmentHit(origin, axis, e.A, e.B, out double t, out Point3d hit))
                        continue;
                    if (t <= lo || t >= hi) continue;
                    add(new Point2d(hit.X, hit.Y));
                }
            }
        }

        /// <summary>Rıht segmentinin dışa (outward) bakan ucu.</summary>
        private static Point2d OuterRihtVertex(
            Point2d nearHit, List<(Point2d A, Point2d B)> rihts, Vector2d outward)
        {
            if (rihts == null || rihts.Count == 0) return nearHit;
            if (!TryFindRihtSegAt(nearHit, rihts, out var seg))
                return nearHit;
            double sa = seg.A.X * outward.X + seg.A.Y * outward.Y;
            double sb = seg.B.X * outward.X + seg.B.Y * outward.Y;
            return sa >= sb ? seg.A : seg.B;
        }

        /// <summary>Rıht segmentinin içe bakan ucu (dışın tersi).</summary>
        private static Point2d InnerRihtVertex(
            Point2d nearHit, List<(Point2d A, Point2d B)> rihts, Vector2d outward)
        {
            if (rihts == null || rihts.Count == 0) return nearHit;
            if (!TryFindRihtSegAt(nearHit, rihts, out var seg))
                return nearHit;
            double sa = seg.A.X * outward.X + seg.A.Y * outward.Y;
            double sb = seg.B.X * outward.X + seg.B.Y * outward.Y;
            return sa <= sb ? seg.A : seg.B;
        }

        /// <summary>Noktayı mevcut iç istasyonların iç hizasına taşı.</summary>
        private static Point2d SnapPointToInnerSide(
            Point2d p, Vector2d inward, List<(double Dist, Point2d Pt)> stations)
        {
            if (stations == null || stations.Count == 0) return p;
            double best = double.MinValue;
            foreach (var st in stations)
            {
                double o = st.Pt.X * inward.X + st.Pt.Y * inward.Y;
                if (o > best) best = o;
            }
            double cur = p.X * inward.X + p.Y * inward.Y;
            return p + inward * (best - cur);
        }

        /// <summary>Noktayı mevcut dış istasyonların dış hizasına taşı (yoksa olduğu gibi).</summary>
        private static Point2d SnapPointToOuterSide(
            Point2d p, Vector2d outward, List<(double Dist, Point2d Pt)> stations)
        {
            if (stations == null || stations.Count == 0) return p;
            double best = double.MinValue;
            foreach (var st in stations)
            {
                double o = st.Pt.X * outward.X + st.Pt.Y * outward.Y;
                if (o > best) best = o;
            }
            double cur = p.X * outward.X + p.Y * outward.Y;
            return p + outward * (best - cur);
        }

        /// <summary>
        /// RotatedDimension: ekledikten sonra DIMFXL=5 (sabit uzatma) + DIMEXE=5.
        /// Stil override’ı ancak append sonrası kalır.
        /// </summary>
        private static void PlacePlanOlcuDimFixedExt(
            Transaction tr, BlockTableRecord ms,
            Point3d x1, Point3d x2, Point3d dimLinePt, double rotation,
            ObjectId dimStyleId, string layer, string olcuMetni = null)
        {
            if (x1.DistanceTo(x2) < 1e-6) return;
            var dim = new RotatedDimension();
            dim.SetDatabaseDefaults();
            dim.Rotation = rotation;
            dim.XLine1Point = x1;
            dim.XLine2Point = x2;
            dim.DimLinePoint = dimLinePt;
            if (!dimStyleId.IsNull)
                dim.DimensionStyle = dimStyleId;
            dim.Layer = layer;
            ms.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
            try
            {
                dim.DimfxlenOn = true;
                dim.Dimfxlen = PlanOlcuDimExeCm;
                dim.Dimexe = PlanOlcuDimExeCm;
                dim.Dimse1 = false;
                dim.Dimse2 = false;
                if (!string.IsNullOrEmpty(olcuMetni))
                    dim.DimensionText = olcuMetni;
            }
            catch { }
        }

        /// <summary>Kola dik iki normalden çıkış hattı dışına (centroid’ten uzağa) olanı.</summary>
        private static Vector2d OutwardNormalFromPath(Vector2d cutAxis, Point2d midOnPath, Point2d pathCen)
        {
            if (cutAxis.Length < 1e-12) cutAxis = new Vector2d(1, 0);
            else cutAxis = cutAxis.GetNormal();
            var n1 = new Vector2d(-cutAxis.Y, cutAxis.X);
            var n2 = new Vector2d(cutAxis.Y, -cutAxis.X);
            Point2d p1 = midOnPath + n1 * PlanOlcuOffsetCm;
            Point2d p2 = midOnPath + n2 * PlanOlcuOffsetCm;
            double d1 = p1.GetDistanceTo(pathCen);
            double d2 = p2.GetDistanceTo(pathCen);
            return d1 >= d2 ? n1 : n2;
        }

        private static ObjectId ResolveOrCreatePlanOlcu25(Transaction tr, Database db)
        {
            var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (dst.Has(DimStylePlanOlcu25))
            {
                try
                {
                    var existing = (DimStyleTableRecord)tr.GetObject(dst[DimStylePlanOlcu25], OpenMode.ForWrite);
                    try { existing.Dimexe = PlanOlcuDimExeCm; } catch { }
                    try { existing.Dimfxlen = PlanOlcuDimExeCm; } catch { }
                    try { existing.DimfxlenOn = true; } catch { }
                }
                catch { }
                return dst[DimStylePlanOlcu25];
            }

            DimStyleTableRecord src = null;
            foreach (string tryName in new[]
            {
                "PLAN_OLCU_25", "PLAN_OLCU",
                OtoOlcuSettings.DefaultDimStyleName, "Standard"
            })
            {
                if (!dst.Has(tryName)) continue;
                src = tr.GetObject(dst[tryName], OpenMode.ForRead) as DimStyleTableRecord;
                if (src != null) break;
            }

            dst.UpgradeOpen();
            var rec = new DimStyleTableRecord { Name = DimStylePlanOlcu25 };
            if (src != null)
            {
                try { rec.CopyFrom(src); } catch { }
                try { rec.Name = DimStylePlanOlcu25; } catch { }
            }
            ObjectId yazi = EnsureTextStyle(tr, db);
            try { if (!yazi.IsNull) rec.Dimtxsty = yazi; } catch { }
            try { rec.Dimlfac = 1.0 / Scale; } catch { }
            try { rec.Dimscale = 1.0; } catch { }
            try { rec.Dimexe = PlanOlcuDimExeCm; } catch { }
            try { rec.Dimfxlen = PlanOlcuDimExeCm; } catch { }
            try { rec.DimfxlenOn = true; } catch { }
            try { rec.Dimdec = 0; } catch { }
            try { rec.Dimtdec = 0; } catch { }
            try { rec.Dimtih = false; } catch { }
            try { rec.Dimtoh = false; } catch { }
            try { rec.Dimtad = 1; } catch { }
            ObjectId id = dst.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            dst.DowngradeOpen();
            return id;
        }

        private static bool TryNumberSelection(
            Transaction tr, Database db, ObjectId[] selIds,
            out List<PathVert> path,
            out List<PathHit> hits,
            out List<(int I0, int I1)> groups,
            out List<PlanSupport> supports,
            out List<(Point2d A, Point2d B)> rihtSegs,
            out int nRiht, out int nSeg, out double basamakCm, out double rihtH, out string err)
        {
            path = null;
            hits = null;
            groups = null;
            supports = null;
            rihtSegs = null;
            nRiht = 0;
            nSeg = 0;
            basamakCm = 0;
            rihtH = 0;
            err = null;

            if (!TryBuildOrientedPath(tr, selIds,
                out path,
                out rihtSegs,
                out supports,
                out string buildErr))
            {
                err = buildErr;
                return false;
            }

            hits = CollectHitsAlongPath(path, rihtSegs);
            EnsureEndpointRihtNumbers(path, hits);
            if (hits.Count < 1)
            {
                err = "çıkış hattında rıht / uç noktası yok.";
                return false;
            }

            EnsureLayer(tr, db, LayerMerdivenYazi, 2, LineWeight.LineWeight020, false);

            nRiht = hits.Count;

            MerdivenSettings.EnsureLoaded();
            rihtH = MerdivenSettings.KatYuksekligiCm / nRiht;
            MerdivenSettings.RihtYuksekligiCm = rihtH;
            MerdivenSettings.RihtAdet = nRiht;

            if (TryModeBasamakGenislikCm(rihtSegs, hits, out basamakCm))
                MerdivenSettings.BasamakGenisligiCm = basamakCm;
            else
                basamakCm = MerdivenSettings.BasamakGenisligiCm;

            try { MerdivenSettings.Save(); } catch { }

            groups = BuildCikisHattiGroups(path);
            nSeg = groups != null ? groups.Count : 0;
            return true;
        }

        /// <summary>Her S grubu için bir kesit; uçlar plandaki kolon/perde/kiriş sınırına uzar.</summary>
        private static int DrawKesitlerForGroups(
            Transaction tr, Database db, Point3d insert,
            List<PathVert> path, List<PathHit> hits, List<(int I0, int I1)> groups,
            List<PlanSupport> supports, List<(Point2d A, Point2d B)> rihtSegs,
            double basamakCm, double rihtH, double dosemeCm,
            List<HashSet<ObjectId>> sectionIds, HashSet<ObjectId> drawingBefore)
        {
            if (groups == null || groups.Count == 0) return 0;
            if (basamakCm < 5) basamakCm = MerdivenSettings.BasamakGenisligiCm;
            if (rihtH < 1) rihtH = MerdivenSettings.RihtYuksekligiCm;
            dosemeCm = MerdivenSettings.DosemeKalinligiCm;
            if (dosemeCm < 5) dosemeCm = 15;

            // Her rıht yalnız bir gruba (sınırdaki rıht önceki kola — S1 son rıht S2'ye kaçmasın)
            var groupHits = AssignHitsToGroups(path, hits, groups);
            double[] kolWidths = ComputeKolGenisliklerCm(path, hits, groups, supports, rihtSegs);

            double ox = insert.X;
            double oy = insert.Y;
            double yOff = 0;
            int drawn = 0;
            int flightKolCount = 0;
            double kotCm = MerdivenSettings.AltKotCm;
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            for (int g = 0; g < groups.Count; g++)
            {
                var beforeSection = SnapshotModelSpaceIds(ms);
                GetGroupDistRange(path, groups[g].I0, groups[g].I1, out double d0, out double d1);
                List<PathHit> gHits = groupHits[g];
                int nStep = gHits != null ? gHits.Count : 0;

                PathHit firstHit = null;
                PathHit lastHit = null;
                if (gHits != null && gHits.Count > 0)
                {
                    firstHit = gHits[0];
                    lastHit = gHits[gHits.Count - 1];
                }

                // Sahanlık / sınır: rıht varsa rıht teğeti; yoksa (saf sahanlık) grup uç teğeti
                double dFirst = firstHit != null ? firstHit.Dist : d0;
                double dLast = lastHit != null ? lastHit.Dist : d1;
                PointAtDist(path, dFirst, out Point2d pFirst, out Vector2d tangFirst);
                PointAtDist(path, dLast, out Point2d pLast, out Vector2d tangLast);
                if (tangFirst.Length < 1e-12) tangFirst = pLast - pFirst;
                if (tangLast.Length < 1e-12) tangLast = pLast - pFirst;
                if (tangFirst.Length > 1e-12) tangFirst = tangFirst.GetNormal();
                else tangFirst = new Vector2d(1, 0);
                if (tangLast.Length > 1e-12) tangLast = tangLast.GetNormal();
                else tangLast = tangFirst;

                TryEndBoundary(pFirst, tangFirst, supports, forward: false, out EndBoundary sol);
                TryEndBoundary(pLast, tangLast, supports, forward: true, out EndBoundary sag);
                if (sol == null)
                    TryBoundaryAlongRay(pFirst, tangFirst * -1.0, supports, out sol);
                if (sag == null)
                    TryBoundaryAlongRay(pLast, tangLast, supports, out sag);

                // Bakış: aşağıdan yukarı veya sağdan sola (kola dik).
                // Bileşikte en uzun düz segment ekseni; gerekirse kesit X aynalanır.
                Vector2d cutAxis = GroupCutAxis(path, groups[g].I0, groups[g].I1);
                Vector2d pathDir = pLast - pFirst;
                if (pathDir.Length < Tol) pathDir = cutAxis;
                bool flipX = ShouldFlipSectionForBakıs(cutAxis, pathDir);

                double h;
                _acilimKolGenislikCm = (kolWidths != null && g < kolWidths.Length && kolWidths[g] >= 5.0)
                    ? kolWidths[g]
                    : MerdivenSettings.BasamakGenisligiCm * Math.Max(1, nStep);
                if (_acilimKolGenislikCm < 40) _acilimKolGenislikCm = 120;

                if (nStep < 1)
                {
                    // Saf sahanlık: düşey (ölçüm) ekseninde dış destekler = kesit uçları
                    double landSpan = MeasureLandingToSupportsCm(
                        path, d0, d1, supports,
                        out EndBoundary landNeg, out EndBoundary landPos);
                    EndBoundary landSol = landNeg ?? sol;
                    EndBoundary landSag = landPos ?? sag;
                    h = DrawLandingOnlyKesiti(
                        tr, db, ox, oy + yOff * Scale,
                        landSpan, dosemeCm, landSol, landSag, KolHarf(g), flipX, kotCm);
                }
                else
                {
                    bool isFirstFlightKol = flightKolCount == 0;
                    flightKolCount++;
                    double kotUst = kotCm + nStep * rihtH;
                    h = DrawUniformFlightKesiti(
                        tr, db, ox, oy + yOff * Scale,
                        nStep, basamakCm, rihtH, dosemeCm, sol, sag, KolHarf(g), flipX,
                        isFirstFlightKol, kotCm, kotUst, drawingBefore);
                    kotCm = kotUst;
                }
                yOff += h + KolArasiCm;
                drawn++;
                if (sectionIds != null)
                {
                    var mine = new HashSet<ObjectId>();
                    foreach (ObjectId id in ms)
                    {
                        if (!beforeSection.Contains(id)) mine.Add(id);
                    }
                    sectionIds.Add(mine);
                }
            }

            return drawn;
        }

        /// <summary>
        /// Her PathHit tam bir gruba. Sınırdaki rıht (d≈d1) önceki gruba yazılır —
        /// sonraki saf sahanlık koluna kaçmaz.
        /// </summary>
        private static List<PathHit>[] AssignHitsToGroups(
            List<PathVert> path, List<PathHit> hits, List<(int I0, int I1)> groups)
        {
            var result = new List<PathHit>[groups.Count];
            for (int g = 0; g < groups.Count; g++)
                result[g] = new List<PathHit>();

            if (hits == null || hits.Count == 0 || path == null) return result;

            var ranges = new (double D0, double D1)[groups.Count];
            for (int g = 0; g < groups.Count; g++)
            {
                GetGroupDistRange(path, groups[g].I0, groups[g].I1, out double d0, out double d1);
                ranges[g] = (d0, d1);
            }

            foreach (var hit in hits)
            {
                double d = hit.Dist;
                for (int g = 0; g < groups.Count; g++)
                {
                    double lo = g == 0 ? ranges[0].D0 - HitTol : ranges[g - 1].D1;
                    double hi = g == groups.Count - 1 ? ranges[g].D1 + HitTol : ranges[g].D1;
                    bool inside = g == 0 ? (d >= lo && d <= hi) : (d > lo && d <= hi);
                    if (!inside) continue;
                    result[g].Add(hit);
                    break;
                }
            }

            for (int g = 0; g < result.Length; g++)
                result[g].Sort((a, b) => a.Dist.CompareTo(b.Dist));

            return result;
        }

        private static void GetGroupDistRange(
            List<PathVert> path, int i0, int i1, out double d0, out double d1)
        {
            d0 = 0;
            for (int i = 0; i < i0; i++)
                d0 += SegLen(path, i);
            d1 = d0;
            for (int i = i0; i <= i1; i++)
                d1 += SegLen(path, i);
        }

        private static void PointAtDist(
            List<PathVert> path, double dist, out Point2d pt, out Vector2d tang)
        {
            pt = path != null && path.Count > 0 ? path[0].P : default;
            tang = new Vector2d(1, 0);
            if (path == null || path.Count < 2) return;

            double acc = 0;
            int segCount = path.Count - 1;
            if (dist <= 0)
            {
                pt = path[0].P;
                tang = TangentOnBulge(path[0].P, path[1].P, path[0].Bulge, 0);
                return;
            }

            for (int i = 0; i < segCount; i++)
            {
                double len = SegLen(path, i);
                if (acc + len < dist - 1e-9)
                {
                    acc += len;
                    continue;
                }
                double t = len < Tol ? 0 : (dist - acc) / len;
                if (t < 0) t = 0;
                if (t > 1) t = 1;
                pt = PointOnBulge(path[i].P, path[i + 1].P, path[i].Bulge, t);
                tang = TangentOnBulge(path[i].P, path[i + 1].P, path[i].Bulge, t);
                return;
            }

            pt = path[path.Count - 1].P;
            tang = TangentOnBulge(path[segCount - 1].P, path[segCount].P, path[segCount - 1].Bulge, 1);
        }

        /// <summary>
        /// Kesit ekseni koridorunda uç mesnet: forward=sağ (+axis), false=sol (−axis).
        /// Origin = ilk veya son rıht (sahanlık tam boyu için).
        /// </summary>
        private static bool TryEndBoundary(
            Point2d origin, Vector2d axis,
            List<PlanSupport> supports, bool forward, out EndBoundary b)
        {
            return TryBoundaryOnAxis(origin, axis, supports, forward, out b);
        }

        private static int BoundaryScore(EndBoundary b, bool forward)
        {
            if (b == null) return -1;
            if (forward) return b.IsKiris ? 3 : (b.IsKolon || b.IsPerde ? 2 : (b.IsDosemeSiniri ? 1 : 0));
            return (b.IsKolon || b.IsPerde) ? 3 : (b.IsKiris ? 2 : (b.IsDosemeSiniri ? 1 : 0));
        }

        private static bool TryBoundaryOnAxis(
            Point2d origin, Vector2d axis, List<PlanSupport> supports,
            bool forward, out EndBoundary b)
        {
            b = null;
            if (supports == null || supports.Count == 0) return false;
            axis = axis.GetNormal();
            const double latMax = 45.0; // yalnız kesit koridoru — S1 KZ-10 vb. elenir

            double bestGap = double.MaxValue;
            PlanSupport best = null;
            double bestThick = 25;

            foreach (var s in supports)
            {
                if (s?.Edges == null || s.Edges.Count == 0) continue;

                // Yalnız ekseni gerçekten kesen kenarlar (köşe projeksiyonu yok — yanlış kısa sahanlık)
                var hits = new List<double>();
                foreach (var e in s.Edges)
                {
                    if (!TryLineSegIntersectParam(origin, axis, e.A, e.B, out double tHit, out double latHit))
                        continue;
                    if (latHit > latMax) continue;
                    hits.Add(tHit);
                }
                if (hits.Count == 0) continue;
                hits.Sort();
                // Tekilleştir
                var uniq = new List<double>();
                foreach (double t in hits)
                {
                    if (uniq.Count == 0 || Math.Abs(t - uniq[uniq.Count - 1]) > 1.0)
                        uniq.Add(t);
                }

                double tMin = uniq[0];
                double tMax = uniq[uniq.Count - 1];
                double span = Math.Max(tMax - tMin, 0);
                bool thin = span < 5;

                double gap;
                double thick;
                if (forward)
                {
                    if (tMin < 2.0) continue;
                    gap = tMin;
                    thick = thin ? (s.IsDosemeSiniri ? 0 : 25) : Math.Min(200, Math.Max(span, 15));
                }
                else
                {
                    if (tMax > -2.0) continue;
                    gap = -tMax;
                    thick = thin ? (s.IsDosemeSiniri ? 0 : 25) : Math.Min(200, Math.Max(span, 15));
                }
                if (gap >= RayMaxCm) continue;

                // Yakın mesafede kiriş/kolon/perde, döşeme sınırına tercih edilir
                int score = BoundaryScore(s, forward);
                int bestScore = best == null ? -1 : BoundaryScore(best, forward);
                if (best != null)
                {
                    bool take =
                        (score > bestScore && gap <= bestGap + 8.0)
                        || (score == bestScore && gap < bestGap - 0.01)
                        || (score < bestScore && gap < bestGap - 8.0);
                    if (!take) continue;
                }

                bestGap = gap;
                bestThick = thick;
                best = s;
            }

            if (best == null) return false;

            b = new EndBoundary
            {
                GapCm = bestGap,
                ThickCm = best.IsDosemeSiniri ? 0 : bestThick,
                HeightCm = best.IsKiris ? best.HeightCm : 0,
                IsKiris = best.IsKiris,
                IsPerde = best.IsPerde,
                IsKolon = best.IsKolon,
                IsDosemeSiniri = best.IsDosemeSiniri
            };
            return true;
        }

        private static int BoundaryScore(PlanSupport s, bool forward)
        {
            if (s == null) return -1;
            if (forward) return s.IsKiris ? 3 : (s.IsKolon || s.IsPerde ? 2 : (s.IsDosemeSiniri ? 1 : 0));
            return (s.IsKolon || s.IsPerde) ? 3 : (s.IsKiris ? 2 : (s.IsDosemeSiniri ? 1 : 0));
        }

        private static void AccumulateProj(
            Point2d origin, Vector2d axis, Vector2d perp, Point2d p,
            ref double tMin, ref double tMax, ref double minLat, ref bool any)
        {
            Vector2d w = p - origin;
            double t = w.X * axis.X + w.Y * axis.Y;
            double lat = Math.Abs(w.X * perp.X + w.Y * perp.Y);
            if (!any) { tMin = t; tMax = t; minLat = lat; any = true; return; }
            if (t < tMin) tMin = t;
            if (t > tMax) tMax = t;
            if (lat < minLat) minLat = lat;
        }

        /// <summary>Sonsuz eksen çizgisi × segment; t = axis param, lat = dik mesafe.</summary>
        private static bool TryLineSegIntersectParam(
            Point2d origin, Vector2d axis, Point2d a, Point2d b,
            out double t, out double lat)
        {
            t = 0;
            lat = 0;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double den = axis.X * dy - axis.Y * dx;
            if (Math.Abs(den) < 1e-14)
            {
                // Paralel: segment orta noktasının lat'ı
                Point2d mid = new Point2d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
                Vector2d w = mid - origin;
                var perp = new Vector2d(-axis.Y, axis.X);
                lat = Math.Abs(w.X * perp.X + w.Y * perp.Y);
                t = w.X * axis.X + w.Y * axis.Y;
                return lat <= 100;
            }
            double ox = a.X - origin.X, oy = a.Y - origin.Y;
            double tRay = (ox * dy - oy * dx) / den;
            double u = (ox * axis.Y - oy * axis.X) / den;
            if (u < -1e-9 || u > 1 + 1e-9) return false;
            t = tRay;
            lat = 0;
            return true;
        }

        /// <summary>Işın yedek: dir yönünde (forward) ilk destek.</summary>
        private static bool TryBoundaryAlongRay(
            Point2d origin, Vector2d dir, List<PlanSupport> supports, out EndBoundary b)
        {
            if (dir.Length < 1e-12) { b = null; return false; }
            return TryBoundaryOnAxis(origin, dir.GetNormal(), supports, forward: true, out b);
        }

        private static bool TryRaySegIntersect(
            Point2d origin, Vector2d dir, Point2d a, Point2d b, out double tRay)
        {
            tRay = 0;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double den = dir.X * dy - dir.Y * dx;
            if (Math.Abs(den) < 1e-14) return false;
            double ox = a.X - origin.X, oy = a.Y - origin.Y;
            double t = (ox * dy - oy * dx) / den;
            double u = (ox * dir.Y - oy * dir.X) / den;
            if (t < -1e-9 || u < -1e-9 || u > 1 + 1e-9) return false;
            tRay = t;
            return true;
        }

        /// <summary>
        /// Saf sahanlık boyu = plandaki düşey ok: kol ortasından geçen doğrunun
        /// en alt / en üst destek yakın yüzleri arası. Uç EndBoundary de döner
        /// (kolon/perde/kiriş finiş için — path teğeti yerine).
        /// </summary>
        private static double MeasureLandingToSupportsCm(
            List<PathVert> path, double d0, double d1, List<PlanSupport> supports,
            out EndBoundary endNeg, out EndBoundary endPos)
        {
            endNeg = null;
            endPos = null;
            PointAtDist(path, d0, out Point2d p0, out _);
            PointAtDist(path, d1, out Point2d p1, out _);
            Point2d origin = new Point2d((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5);
            var axis = new Vector2d(0, 1);

            TryBoundaryOnAxis(origin, axis, supports, forward: false, out endNeg);
            TryBoundaryOnAxis(origin, axis, supports, forward: true, out endPos);

            double span;
            if (endNeg != null && endPos != null)
                span = endNeg.GapCm + endPos.GapCm;
            else if (!TryLandingOuterSpan(origin, axis, supports, out span))
            {
                double arm = Math.Abs(p1.Y - p0.Y);
                if (arm < 40) arm = Math.Max(d1 - d0, 40);
                span = arm;
            }
            if (span < 40) span = 40;
            return span;
        }

        /// <summary>Düşey doğru × destek kenarları; boy = en üst t − en alt t.</summary>
        private static bool TryLandingOuterSpan(
            Point2d origin, Vector2d axis, List<PlanSupport> supports, out double span)
        {
            span = 0;
            if (supports == null || supports.Count == 0) return false;
            axis = axis.GetNormal();
            double tMin = double.MaxValue;
            double tMax = double.MinValue;
            int n = 0;

            foreach (var s in supports)
            {
                if (s?.Edges == null) continue;
                foreach (var e in s.Edges)
                {
                    if (!TryCrossAxis(origin, axis, e.A, e.B, out double tHit))
                        continue;
                    if (Math.Abs(tHit) > RayMaxCm) continue;
                    if (tHit < tMin) tMin = tHit;
                    if (tHit > tMax) tMax = tHit;
                    n++;
                }
            }

            if (n < 2) return false;
            span = tMax - tMin;
            return span >= 40;
        }

        /// <summary>
        /// Eksen ile kenar gerçekten kesişiyorsa t. Paralel / yan yüz (≈20° altı) yok sayılır
        /// — aksi halde perde kenarının orta noktası kısa boy (80 cm) üretir.
        /// </summary>
        private static bool TryCrossAxis(
            Point2d origin, Vector2d axis, Point2d a, Point2d b, out double t)
        {
            t = 0;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double elen = Math.Sqrt(dx * dx + dy * dy);
            if (elen < Tol) return false;
            double den = axis.X * dy - axis.Y * dx;
            if (Math.Abs(den) / elen < Math.Sin(20.0 * Math.PI / 180.0))
                return false;
            double ox = a.X - origin.X, oy = a.Y - origin.Y;
            double u = (ox * axis.Y - oy * axis.X) / den;
            if (u < -1e-6 || u > 1.0 + 1e-6) return false;
            t = (ox * dy - oy * dx) / den;
            return true;
        }

        /// <summary>Saf sahanlık kolu (rıht yok): yatay plak; boy = döşeme/perde/kiriş/kolon yüzleri arası.</summary>
        private static double DrawLandingOnlyKesiti(
            Transaction tr, Database db, double ox, double oy,
            double spanCm, double dosemeCm,
            EndBoundary sol, EndBoundary sag, string title, bool flipX = false,
            double kotCm = 0)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            if (dosemeCm < 5) dosemeCm = 15;
            double len = Math.Max(spanCm, 40);

            // Basamaksız: kolon/perde GapCm şartı yok (uçlar ölçüm ekseninden, Gap = yarı boy)
            bool solCut = sol != null && (sol.IsPerde || sol.IsKolon);
            bool sagCut = sag != null && (sag.IsPerde || sag.IsKolon);
            bool solBeam = sol != null && sol.IsKiris;
            bool sagBeam = sag != null && sag.IsKiris;
            bool openSol = solCut || solBeam;
            bool openSag = sagCut || sagBeam;

            double solW = solCut || solBeam ? Math.Max(sol.ThickCm, 15) : 0;
            double sagW = sagCut || sagBeam ? Math.Max(sag.ThickCm, 15) : 0;
            if (sol != null && sol.IsDosemeSiniri) solW = 0;
            if (sag != null && sag.IsDosemeSiniri) sagW = 0;

            double x0 = 0;
            double colLeft = (solCut || solBeam) ? -solW : 0;
            double yTop = 0;
            double yBot = -dosemeCm;
            double landEndX = len;
            double xRight = sagBeam ? landEndX + sagW : landEndX;
            double colRight = sagCut ? landEndX + sagW : xRight;

            double xMin = Math.Min(colLeft, x0);
            if (solBeam) xMin = Math.Min(xMin, -solW);
            double xMax = Math.Max(colRight, xRight);
            Func<double, double> fx = u => FlipX(u, flipX, xMin, xMax);

            // Üst / alt — kiriş gövdesi DrawKirisFinisU (üstte uzatma yok)
            var top = new List<Point2d>
            {
                new Point2d(x0, yTop),
                new Point2d(landEndX, yTop)
            };
            AddPline(tr, ms, FlipPts(top, fx), false, LayerKesitCizgisi, Scale, ox, oy);

            var bot = new List<Point2d>
            {
                new Point2d(sagBeam || sagCut ? landEndX : xRight, yBot),
                new Point2d(x0, yBot)
            };
            AddPline(tr, ms, FlipPts(bot, fx), false, LayerKesitCizgisi, Scale, ox, oy);

            if (!openSol)
            {
                AddLineRaw(tr, ms,
                    ox + fx(x0) * Scale, oy + yTop * Scale,
                    ox + fx(x0) * Scale, oy + yBot * Scale,
                    LayerKesitCizgisi);
            }
            if (!openSag && !sagBeam)
            {
                AddLineRaw(tr, ms,
                    ox + fx(xRight) * Scale, oy + yTop * Scale,
                    ox + fx(xRight) * Scale, oy + yBot * Scale,
                    LayerKesitCizgisi);
            }

            if (solCut && solW > 0)
                DrawKolonPerdeFinis(tr, ms, ox, oy, fx(colLeft), fx(x0), yBot, yTop, LayerKesitCizgisi);
            else if (solBeam && solW > 0)
            {
                double h = ResolveKirisHeightCm(sol);
                DrawKirisFinisU(tr, ms, ox, oy, fx(-solW), fx(0), yTop, yTop - h, yBot);
            }

            if (sagBeam && sagW > 0)
            {
                double h = ResolveKirisHeightCm(sag);
                DrawKirisFinisU(tr, ms, ox, oy, fx(xRight), fx(landEndX), yTop, yTop - h, yBot);
            }
            else if (sagCut && sagW > 0)
                DrawKolonPerdeFinis(tr, ms, ox, oy, fx(colRight), fx(landEndX), yBot, yTop, LayerKesitCizgisi);

            DrawKesitDonatiLanding(
                tr, db, ms, ox, oy, fx,
                x0, landEndX, colLeft, colRight,
                yTop, yBot, dosemeCm,
                solCut || solBeam, sagCut || sagBeam);

            // Basmaksız kesit üst çizgisi kotu
            DrawMerdivenKot(tr, db, ms, kotCm,
                WorldX(ox, fx, x0), WorldX(ox, fx, landEndX), oy + yTop * Scale);

            DrawLandingOlcu(tr, db, ms, ox, oy, fx,
                x0, landEndX, yTop, yBot,
                solW, sagW, colLeft, sagCut ? colRight : xRight);

            double landXa = WorldX(ox, fx, solW > 0.5 ? colLeft : x0);
            double landXb = WorldX(ox, fx, sagW > 0.5 ? (sagCut ? colRight : xRight) : landEndX);
            DrawKesitAdiUnderDim(tr, db, ms, title, landXa, landXb, oy + (yBot - 10.0) * Scale);

            // Açılım 6+7 için pay (max 110)
            return dosemeCm + 2 * FinisKesitOffsetCm + 110 + 85;
        }

        /// <summary>
        /// Basmaksız kesit ölçüleri (PLAN_OLCU 25).
        /// Alt zincir: kesit altından 10 cm; kenar kolon/perde/kiriş kalınlığı dahil.
        /// Kalınlık: üst çizginin sağından boyun 1/4’ü içeride.
        /// </summary>
        private static void DrawLandingOlcu(
            Transaction tr, Database db, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            double x0, double landEndX, double yTop, double yBot,
            double solW, double sagW, double solOuterX, double sagOuterX)
        {
            ObjectId dimStyleId = ResolveOrCreatePlanOlcu25(tr, db);
            EnsureLayer(tr, db, LayerOlcu, 3, LineWeight.LineWeight030, false);

            var xs = new List<double>();
            if (solW > 0.5) xs.Add(solOuterX);
            xs.Add(x0);
            xs.Add(landEndX);
            if (sagW > 0.5 && Math.Abs(sagOuterX - landEndX) > 0.5)
                xs.Add(sagOuterX);
            DrawYatayOlcuZinciri(tr, ms, dimStyleId, ox, oy, fx, yBot, xs);

            double xTopL = WorldX(ox, fx, x0);
            double xTopR = WorldX(ox, fx, landEndX);
            double xRight = Math.Max(xTopL, xTopR);
            double xKal = xRight - 0.25 * Math.Abs(xTopR - xTopL);
            double y1 = oy + yTop * Scale;
            double y2 = oy + yBot * Scale;
            PlacePlanOlcuDimFixedExt(tr, ms,
                new Point3d(xKal, y1, 0),
                new Point3d(xKal, y2, 0),
                new Point3d(xKal, 0.5 * (y1 + y2), 0),
                Math.PI * 0.5, dimStyleId, LayerOlcu);
        }

        /// <summary>Yatay ölçü zinciri: çizginin 10 cm altında. xs yerel kesit X.</summary>
        private static void DrawYatayOlcuZinciri(
            Transaction tr, BlockTableRecord ms, ObjectId dimStyleId,
            double ox, double oy, Func<double, double> fx,
            double yLine, List<double> xsLocal)
        {
            if (xsLocal == null || xsLocal.Count < 2) return;
            var xs = new List<double>(xsLocal);
            xs.Sort((a, b) => WorldX(ox, fx, a).CompareTo(WorldX(ox, fx, b)));
            const double altOfsetCm = 10.0;
            double yDef = oy + yLine * Scale;
            double yDim = oy + (yLine - altOfsetCm) * Scale;
            for (int i = 0; i + 1 < xs.Count; i++)
            {
                double xa = WorldX(ox, fx, xs[i]);
                double xb = WorldX(ox, fx, xs[i + 1]);
                PlacePlanOlcuDimFixedExt(tr, ms,
                    new Point3d(xa, yDef, 0),
                    new Point3d(xb, yDef, 0),
                    new Point3d(0.5 * (xa + xb), yDim, 0),
                    0.0, dimStyleId, LayerOlcu);
            }
        }

        /// <summary>
        /// Basamaklı kesit: 1. ve 3. bölge altı (kenar kolon/perde/kiriş dahil) 10 cm altta.
        /// Kalınlık: sahanlık döşemesi, sağdan boyun 1/4’ü içeride. Temelde 1. bölge yok.
        /// Basamak alt ile sanal basamak üst arası bel kalınlığı.
        /// İlk ve son basamak genişliği çizginin 10 cm üstünde; rıht yüksekliği boş tarafa 10 cm yanda.
        /// </summary>
        private static void DrawFlightBasamakOlcu(
            Transaction tr, Database db, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            double x0, double flightStartX, double lastRiserX, double botRightX,
            double yTop, double yLandLo, double yLandHi, double landLoX, double landHiX,
            int nStep, double basamakCm, double rihtH,
            double solW, double sagW, double solOuterX, double sagOuterX,
            bool temelAktif,
            Point2d noseA, Point2d noseB, Point2d sofA, Point2d sofB)
        {
            ObjectId dimStyleId = ResolveOrCreatePlanOlcu25(tr, db);
            EnsureLayer(tr, db, LayerOlcu, 3, LineWeight.LineWeight030, false);

            if (!temelAktif)
            {
                var alt1 = new List<double>();
                if (solW > 0.5) alt1.Add(solOuterX);
                alt1.Add(x0);
                alt1.Add(landLoX);
                DrawYatayOlcuZinciri(tr, ms, dimStyleId, ox, oy, fx, yLandLo, alt1);
                PlaceZoneKalinlikOlcu(tr, ms, dimStyleId, ox, oy, fx, x0, flightStartX, 0, yLandLo);
            }

            var alt3 = new List<double>();
            alt3.Add(landHiX);
            alt3.Add(botRightX);
            if (sagW > 0.5 && Math.Abs(sagOuterX - botRightX) > 0.5)
                alt3.Add(sagOuterX);
            DrawYatayOlcuZinciri(tr, ms, dimStyleId, ox, oy, fx, yLandHi, alt3);
            PlaceZoneKalinlikOlcu(tr, ms, dimStyleId, ox, oy, fx, lastRiserX, botRightX, yTop, yLandHi);
            PlaceSoffitNoseOlcu(tr, ms, dimStyleId, ox, oy, fx, noseA, noseB, sofA, sofB);

            double xFirst = WorldX(ox, fx, flightStartX);
            double xLast = WorldX(ox, fx, lastRiserX);
            double ascend = xLast - xFirst;
            if (Math.Abs(ascend) < 1.0) ascend = 1.0;
            double openFirst = ascend > 0 ? -1.0 : 1.0;
            const double yanCm = 10.0;

            PlaceDuseyOlcu(tr, ms, dimStyleId, ox, oy, fx,
                flightStartX, 0, rihtH, openFirst * yanCm);
            if (nStep > 2)
            {
                // Bir alt rıht: ölçü çizgisi kesit dışına, rıhtın 10 cm yanında
                PlaceDuseyOlcu(tr, ms, dimStyleId, ox, oy, fx,
                    lastRiserX - basamakCm, yTop - 2.0 * rihtH, yTop - rihtH, openFirst * yanCm);
            }
            if (nStep > 1)
            {
                PlaceYatayUstOlcu(tr, ms, dimStyleId, ox, oy, fx,
                    flightStartX, flightStartX + basamakCm, rihtH, yanCm);
                bool ayniBasamak = Math.Abs((flightStartX + basamakCm) - (lastRiserX - basamakCm)) < 0.5
                    && Math.Abs(rihtH - (yTop - rihtH)) < 0.5;
                if (!ayniBasamak)
                    PlaceYatayUstOlcu(tr, ms, dimStyleId, ox, oy, fx,
                        lastRiserX - basamakCm, lastRiserX, yTop - rihtH, yanCm);
            }
        }

        /// <summary>Sahanlık döşeme kalınlığı: sağ uçtan boyun 1/4’ü içeride, düşey.</summary>
        private static void PlaceZoneKalinlikOlcu(
            Transaction tr, BlockTableRecord ms, ObjectId dimStyleId,
            double ox, double oy, Func<double, double> fx,
            double xA, double xB, double yTop, double yBot)
        {
            if (Math.Abs(xB - xA) < 8.0 || Math.Abs(yTop - yBot) < 1.0) return;
            double xa = WorldX(ox, fx, xA);
            double xb = WorldX(ox, fx, xB);
            double xRight = Math.Max(xa, xb);
            double xKal = xRight - 0.25 * Math.Abs(xb - xa);
            double y1 = oy + yTop * Scale;
            double y2 = oy + yBot * Scale;
            PlacePlanOlcuDimFixedExt(tr, ms,
                new Point3d(xKal, y1, 0),
                new Point3d(xKal, y2, 0),
                new Point3d(xKal, 0.5 * (y1 + y2), 0),
                Math.PI * 0.5, dimStyleId, LayerOlcu);
        }

        /// <summary>Basamak alt çizgisi ile sanal basamak üst çizgisi arası, ortaya dik.</summary>
        private static void PlaceSoffitNoseOlcu(
            Transaction tr, BlockTableRecord ms, ObjectId dimStyleId,
            double ox, double oy, Func<double, double> fx,
            Point2d noseA, Point2d noseB, Point2d sofA, Point2d sofB)
        {
            var nose = new Point2d(0.5 * (noseA.X + noseB.X), 0.5 * (noseA.Y + noseB.Y));
            var sof = new Point2d(0.5 * (sofA.X + sofB.X), 0.5 * (sofA.Y + sofB.Y));
            double nwx = WorldX(ox, fx, nose.X);
            double nwy = oy + nose.Y * Scale;
            double swx = WorldX(ox, fx, sof.X);
            double swy = oy + sof.Y * Scale;
            if (Math.Abs(nwx - swx) < 0.5 && Math.Abs(nwy - swy) < 0.5) return;
            double ang = Math.Atan2(nwy - swy, nwx - swx);
            PlacePlanOlcuDimFixedExt(tr, ms,
                new Point3d(swx, swy, 0),
                new Point3d(nwx, nwy, 0),
                new Point3d(0.5 * (swx + nwx), 0.5 * (swy + nwy), 0),
                ang, dimStyleId, LayerOlcu);
        }

        private static void PlaceYatayUstOlcu(
            Transaction tr, BlockTableRecord ms, ObjectId dimStyleId,
            double ox, double oy, Func<double, double> fx,
            double xA, double xB, double yLine, double ustCm)
        {
            double xa = WorldX(ox, fx, xA);
            double xb = WorldX(ox, fx, xB);
            double yDef = oy + yLine * Scale;
            double yDim = oy + (yLine + ustCm) * Scale;
            PlacePlanOlcuDimFixedExt(tr, ms,
                new Point3d(xa, yDef, 0),
                new Point3d(xb, yDef, 0),
                new Point3d(0.5 * (xa + xb), yDim, 0),
                0.0, dimStyleId, LayerOlcu);
        }

        private static void PlaceDuseyOlcu(
            Transaction tr, BlockTableRecord ms, ObjectId dimStyleId,
            double ox, double oy, Func<double, double> fx,
            double xLine, double yA, double yB, double yanCm)
        {
            double x = WorldX(ox, fx, xLine);
            double ya = oy + yA * Scale;
            double yb = oy + yB * Scale;
            double xDim = x + yanCm * Scale;
            string rihtTxt = Math.Abs(yB - yA).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            PlacePlanOlcuDimFixedExt(tr, ms,
                new Point3d(x, ya, 0),
                new Point3d(x, yb, 0),
                new Point3d(xDim, 0.5 * (ya + yb), 0),
                Math.PI * 0.5, dimStyleId, LayerOlcu, olcuMetni: rihtTxt);
        }

        /// <summary>
        /// Temel kalınlığı: dış yüzde, alt kot (y=0) ile temel altı arası. Ölçü dışarı 10 cm.
        /// </summary>
        private static void DrawTemelKalinlikOlcu(
            Transaction tr, Database db, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            double xDis, double yTop, double yBot)
        {
            ObjectId dimStyleId = ResolveOrCreatePlanOlcu25(tr, db);
            EnsureLayer(tr, db, LayerOlcu, 3, LineWeight.LineWeight030, false);
            const double yanCm = 10.0;
            double xDim = xDis - yanCm;
            PlacePlanOlcuDimFixedExt(tr, ms,
                new Point3d(WorldX(ox, fx, xDis), oy + yBot * Scale, 0),
                new Point3d(WorldX(ox, fx, xDis), oy + yTop * Scale, 0),
                new Point3d(WorldX(ox, fx, xDim), oy + 0.5 * (yBot + yTop) * Scale, 0),
                Math.PI * 0.5, dimStyleId, LayerOlcu);
        }

        /// <summary>
        /// Kol kesiti (örnek beyaz çizim):
        /// sol sahanlık (rıht→sınır) | rıht/basamak | sağ sahanlık (rıht→sınır);
        /// bel kalınlığı = döşeme (eğik soffit); sahanlık altı soffit kotunda yatay.
        /// flipX: bakış yönü (aşağı→yukarı / sağ→sola) için yatay ayna.
        /// </summary>
        private static double DrawUniformFlightKesiti(
            Transaction tr, Database db, double ox, double oy,
            int nStep, double basamakCm, double rihtH, double dosemeCm,
            EndBoundary sol, EndBoundary sag, string title, bool flipX = false,
            bool isFirstFlightKol = false,
            double kotAltCm = 0, double kotUstCm = 0,
            HashSet<ObjectId> drawingBefore = null)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            var beforeIds = SnapshotModelSpaceIds(ms);

            if (dosemeCm < 5) dosemeCm = 15;
            if (nStep < 1) nStep = 1;

            bool solCut = sol != null && (sol.IsPerde || sol.IsKolon);
            bool sagCut = sag != null && (sag.IsPerde || sag.IsKolon);
            bool solBeam = sol != null && sol.IsKiris;
            bool sagBeam = sag != null && sag.IsKiris;

            // Tam sahanlık: ilk/son rıht → sınır yakın yüz
            double solLand = sol != null ? Math.Max(sol.GapCm, 0) : Math.Max(basamakCm * 0.5, 20);
            double sagLand = sag != null ? Math.Max(sag.GapCm, 0) : Math.Max(basamakCm * 0.5, 20);
            double solW = sol != null && !sol.IsDosemeSiniri ? Math.Max(sol.ThickCm, 0) : 0;
            double sagW = sag != null && !sag.IsDosemeSiniri ? Math.Max(sag.ThickCm, 0) : 0;
            if (solCut && solW < 15) solW = 15;
            if (sagCut && sagW < 15) sagW = 15;
            if (solBeam && solW < 15) solW = 15;
            if (sagBeam && sagW < 15) sagW = 25;

            double x0 = 0; // sol sınır yakın yüz
            double colLeft = (solCut || solBeam) ? -solW : 0;

            var top = new List<Point2d>();
            double x = x0;
            double y = 0;

            // —— üst profil ——
            top.Add(new Point2d(x, y));
            if (solLand > Tol)
            {
                x += solLand;
                top.Add(new Point2d(x, y));
            }

            double flightStartX = x; // ilk rıht

            // n rıht + (n-1) basamak; son rıhttan sonra sahanlık
            for (int i = 0; i < nStep; i++)
            {
                y += rihtH;
                top.Add(new Point2d(x, y));
                if (i < nStep - 1)
                {
                    x += basamakCm;
                    top.Add(new Point2d(x, y));
                }
            }

            double lastRiserX = x;
            double yTop = y;

            if (sagLand > Tol)
            {
                x += sagLand;
                top.Add(new Point2d(x, yTop));
            }
            double landEndX = x; // sağ sınır yakın yüz
            double xRight = sagBeam && sagW > 0 ? landEndX + sagW : landEndX;
            double colRight = sagCut ? landEndX + sagW : xRight;
            bool openSol = solCut || solBeam;
            bool openSag = sagCut || sagBeam;

            // —— bel ——
            double yLastRiserBot = Math.Max(0, yTop - rihtH);
            Point2d noseA = new Point2d(flightStartX, 0);
            Point2d noseB = new Point2d(lastRiserX, yLastRiserBot);
            double ndx = noseB.X - noseA.X;
            double ndy = noseB.Y - noseA.Y;
            double nlen = Math.Sqrt(ndx * ndx + ndy * ndy);
            if (nlen < Tol)
            {
                ndx = 1;
                ndy = 0;
                nlen = 1;
            }
            double nx = ndy / nlen;
            double ny = -ndx / nlen;
            if (ny > 0) { nx = -nx; ny = -ny; }

            Point2d sofA = new Point2d(noseA.X + nx * dosemeCm, noseA.Y + ny * dosemeCm);
            Point2d sofB = new Point2d(noseB.X + nx * dosemeCm, noseB.Y + ny * dosemeCm);

            double yLandLo = -dosemeCm;
            double yLandHi = yTop - dosemeCm;
            Point2d landLo = IntersectSoffitAtY(sofA, sofB, yLandLo, sofA.X);
            Point2d landHi = IntersectSoffitAtY(sofA, sofB, yLandHi, sofB.X);

            bool temelAktif = isFirstFlightKol && MerdivenSettings.TemelVar
                && MerdivenSettings.TemelKalinligiCm >= 10;
            double temelKalCm = MerdivenSettings.TemelKalinligiCm;
            const double temelDisCm = 80.0;
            const double temelIcCm = 50.0;
            double xIkinciRiht = flightStartX + (nStep > 1 ? basamakCm : 0);
            double xDis = flightStartX - temelDisCm;
            double xIc = xIkinciRiht + temelIcCm;
            double yFoundTop = 0;
            double yFoundBot = yFoundTop - temelKalCm;
            if (temelAktif)
            {
                if (xDis < x0 - Tol)
                    top.Insert(0, new Point2d(xDis, yFoundTop));
                else if (xDis > x0 + Tol)
                {
                    while (top.Count > 1 && top[0].X < xDis - Tol && Math.Abs(top[0].Y - yFoundTop) < Tol)
                        top.RemoveAt(0);
                    if (Math.Abs(top[0].X - xDis) > Tol)
                        top.Insert(0, new Point2d(xDis, yFoundTop));
                }
            }

            double xMin = Math.Min(colLeft, Math.Min(x0, solBeam ? -solW : x0));
            double xMax = Math.Max(colRight, xRight);
            Func<double, double> fx = u => FlipX(u, flipX, xMin, xMax);

            AddPline(tr, ms, FlipPts(top, fx), false, LayerKesitCizgisi, Scale, ox, oy);

            // Alt / bel profili (sağ→sol). Temelde sahanlık altı ve soffitin temel içindeki kısmı silinir.
            var bot = new List<Point2d>();
            double botRightX = sagBeam || sagCut ? landEndX : xRight;
            bot.Add(new Point2d(botRightX, yLandHi));
            if (landHi.X < botRightX - Tol && landHi.X > x0 + Tol)
                bot.Add(landHi);
            if (!temelAktif)
            {
                bot.Add(landLo);
                bot.Add(new Point2d(x0, yLandLo));
            }
            else
            {
                double yIcUst = yFoundTop;
                if (TryIntersectY(landLo, landHi, yFoundTop, out Point2d sof0)
                    && sof0.X > landLo.X + Tol && sof0.X < landHi.X - Tol
                    && sof0.X < xIc - Tol)
                {
                    bot.Add(sof0);
                    bot.Add(new Point2d(xIc, yFoundTop));
                }
                else if (TryIntersectX(landLo, landHi, xIc, out Point2d sofIc)
                    && sofIc.Y > Math.Min(landLo.Y, landHi.Y) - Tol
                    && sofIc.Y < Math.Max(landLo.Y, landHi.Y) + Tol)
                {
                    bot.Add(sofIc);
                    yIcUst = sofIc.Y;
                }
                else
                {
                    bot.Add(landLo);
                    bot.Add(new Point2d(xIc, yFoundTop));
                }

                AddPline(tr, ms, FlipPts(bot, fx), false, LayerKesitCizgisi, Scale, ox, oy);

                AddLineRaw(tr, ms,
                    ox + fx(xIc) * Scale, oy + yFoundBot * Scale,
                    ox + fx(xDis) * Scale, oy + yFoundBot * Scale,
                    LayerKesitCizgisi);
                EnsureLayer(tr, db, LayerKesitSiniri, 241, LineWeight.LineWeight020, true);
                AddLineRaw(tr, ms,
                    ox + fx(xDis) * Scale, oy + yFoundBot * Scale,
                    ox + fx(xDis) * Scale, oy + yFoundTop * Scale,
                    LayerKesitSiniri);
                AddLineRaw(tr, ms,
                    ox + fx(xIc) * Scale, oy + yFoundBot * Scale,
                    ox + fx(xIc) * Scale, oy + yIcUst * Scale,
                    LayerKesitSiniri);
                DrawTemelKalinlikOlcu(tr, db, ms, ox, oy, fx, xDis, yFoundTop, yFoundBot);
            }
            if (!temelAktif)
                AddPline(tr, ms, FlipPts(bot, fx), false, LayerKesitCizgisi, Scale, ox, oy);

            if (!openSol && !temelAktif)
            {
                AddLineRaw(tr, ms,
                    ox + fx(x0) * Scale, oy + 0 * Scale,
                    ox + fx(x0) * Scale, oy + yLandLo * Scale,
                    LayerKesitCizgisi);
            }
            if (!openSag && !sagBeam)
            {
                AddLineRaw(tr, ms,
                    ox + fx(xRight) * Scale, oy + yTop * Scale,
                    ox + fx(xRight) * Scale, oy + yLandHi * Scale,
                    LayerKesitCizgisi);
            }

            if (!temelAktif && solCut && solW > 0)
                DrawKolonPerdeFinis(tr, ms, ox, oy, fx(colLeft), fx(x0), yLandLo, 0, LayerKesitCizgisi);
            else if (!temelAktif && solBeam && solW > 0)
            {
                double h = ResolveKirisHeightCm(sol);
                DrawKirisFinisU(tr, ms, ox, oy, fx(-solW), fx(0), 0, -h, yLandLo);
            }

            if (sagBeam && sagW > 0)
            {
                double h = ResolveKirisHeightCm(sag);
                DrawKirisFinisU(tr, ms, ox, oy, fx(xRight), fx(landEndX), yTop, yTop - h, yLandHi);
            }
            else if (sagCut && sagW > 0)
                DrawKolonPerdeFinis(tr, ms, ox, oy, fx(colRight), fx(landEndX), yLandHi, yTop, LayerKesitCizgisi);

            _temelAcilimEkAsagiCm = 0;
            _temelBar1AsagiCm = 100.0;
            _temelBar2AsagiCm = 105.0;
            try
            {
            DrawKesitDonatiFlight(
                tr, db, ms, ox, oy, fx,
                x0, landEndX, flightStartX, lastRiserX,
                yTop, yLandLo, yLandHi, rihtH, dosemeCm,
                noseA, noseB, sofA, sofB, landLo, landHi,
                isFirstFlightKol,
                solCut || solBeam, colLeft,
                sagCut || sagBeam, colRight,
                temelAktif, basamakCm, temelKalCm);

            // 1. bölge üst (alt sahanlık) ve 3. bölge üst (üst sahanlık)
            double kotXSol = temelAktif && xDis > x0 ? xDis : x0;
            DrawMerdivenKot(tr, db, ms, kotAltCm,
                WorldX(ox, fx, kotXSol), WorldX(ox, fx, flightStartX), oy + 0 * Scale);
            DrawMerdivenKot(tr, db, ms, kotUstCm,
                WorldX(ox, fx, lastRiserX), WorldX(ox, fx, landEndX), oy + yTop * Scale);

            DrawFlightBasamakOlcu(tr, db, ms, ox, oy, fx,
                x0, flightStartX, lastRiserX, botRightX,
                yTop, yLandLo, yLandHi, landLo.X, landHi.X,
                nStep, basamakCm, rihtH,
                solW, sagW, colLeft, sagCut ? colRight : xRight,
                temelAktif, noseA, noseB, sofA, sofB);

            double bolge3L = WorldX(ox, fx, landHi.X);
            double bolge3R = WorldX(ox, fx, botRightX);
            if (sagW > 0.5)
            {
                double sagX = WorldX(ox, fx, sagCut ? colRight : xRight);
                if (sagX < bolge3L) bolge3L = sagX;
                if (sagX > bolge3R) bolge3R = sagX;
            }
            DrawKesitAdiUnderDim(tr, db, ms, title, bolge3L, bolge3R, oy + (yLandHi - 10.0) * Scale);

            // Basamak kesiti: ait olduğu kesidin içinde, diğer keside girmeden
            var detBefore = SnapshotModelSpaceIds(ms);
            double flightW = Math.Max(xMax, xRight) - Math.Min(xMin, colLeft);
            double detOx = ox + (flightW + 80.0) * Scale;
            DrawBasamakKesitDetay(tr, db, ms, detOx, oy, _acilimKolGenislikCm, dosemeCm, _basamakTevziAdet);
            PlaceBasamakKesitOffNose(tr, ms, detBefore, beforeIds, drawingBefore, ox, oy, fx, sofA, sofB);

            double minY = yLandLo - FinisKesitOffsetCm;
            if (temelAktif) minY = Math.Min(minY, yFoundBot);
            if (solBeam) minY = Math.Min(minY, -ResolveKirisHeightCm(sol));
            if (sagBeam) minY = Math.Min(minY, yTop - ResolveKirisHeightCm(sag));
            // Açılım payı: kesitten aşağı max ofset + gönye payı
            double maxAcilim = isFirstFlightKol ? 160.0 : 170.0;
            minY = Math.Min(minY, yLandLo - maxAcilim - 90 - _temelAcilimEkAsagiCm);
            return yTop + FinisKesitOffsetCm - Math.Min(0, minY) + 20;
            }
            finally
            {
                _temelAcilimEkAsagiCm = 0;
                _temelBar1AsagiCm = 100.0;
                _temelBar2AsagiCm = 105.0;
            }
        }

        /// <summary>
        /// Kesit adı: ölçü çizgisinin 20 cm altında, yazı kutusunun üst kenarı.
        /// Örnek: A-A KESİTİ 1:25. Katman KESIT ISMI, 15 punto (1/25).
        /// </summary>
        private static void DrawKesitAdiUnderDim(
            Transaction tr, Database db, BlockTableRecord ms,
            string letter, double xL, double xR, double dimLineY)
        {
            if (string.IsNullOrWhiteSpace(letter)) letter = "A";
            EnsureLayer(tr, db, LayerKesitIsmi, 6, LineWeight.LineWeight020, true);
            string text = letter + "-" + letter + " KESİTİ 1:25";
            double midX = 0.5 * (Math.Min(xL, xR) + Math.Max(xL, xR));
            double topY = dimLineY - 20.0 * Scale;
            ObjectId styleId = EnsureTextStyle(tr, db);
            var t = new DBText
            {
                TextString = text,
                Height = 15.0,
                Layer = LayerKesitIsmi,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextTop,
                AlignmentPoint = new Point3d(midX, topY, 0),
                Color = Color.FromColorIndex(ColorMethod.ByLayer, 256)
            };
            if (!styleId.IsNull)
                t.TextStyleId = styleId;
            ms.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
            try
            {
                t.AdjustAlignment(db);
                Extents3d ex = t.GeometricExtents;
                double cx = 0.5 * (ex.MinPoint.X + ex.MaxPoint.X);
                t.TransformBy(Matrix3d.Displacement(new Vector3d(midX - cx, topY - ex.MaxPoint.Y, 0)));
            }
            catch { }
        }

        /// <summary>
        /// Basamak kesit kutusu: alt köşe, alt basamak (soffit) çizgisinin
        /// 40 cm yukarı ofsetinde. İdeal nokta alt 1/3; çakışırsa hat boyunca kayar.
        /// Kutu içine kendi elemanları dışında bir şey girmez.
        /// </summary>
        private static void PlaceBasamakKesitOffNose(
            Transaction tr, BlockTableRecord ms, HashSet<ObjectId> detBefore, HashSet<ObjectId> sectionBefore,
            HashSet<ObjectId> drawingBefore,
            double ox, double oy, Func<double, double> fx,
            Point2d sofA, Point2d sofB)
        {
            if (!TryUnionNewExtents(tr, ms, detBefore, ox, oy, out Extents3d box))
                return;

            double ax = WorldX(ox, fx, sofA.X);
            double ay = oy + sofA.Y * Scale;
            double bx = WorldX(ox, fx, sofB.X);
            double by = oy + sofB.Y * Scale;
            double dxL = bx - ax;
            double dyL = by - ay;
            double len = Math.Sqrt(dxL * dxL + dyL * dyL);
            if (len < 1.0) return;
            double dirX = dxL / len;
            double dirY = dyL / len;
            double upX = -dirY;
            double upY = dirX;
            if (upY < 0) { upX = -upX; upY = -upY; }

            const double offsetCm = 40.0;
            double off = offsetCm * Scale;
            // Hat sağa düşüyorsa sol-alt, sağa yükseliyorsa sağ-alt köşe hatta oturur;
            // kutu, hattın yukarı tarafında kalır.
            bool pinRight = dirX * dirY > 1e-6;
            double cornerX = pinRight ? box.MaxPoint.X : box.MinPoint.X;
            double cornerY = box.MinPoint.Y;

            double ideal = len / 3.0;
            double reach = Math.Max(box.MaxPoint.X - box.MinPoint.X, box.MaxPoint.Y - box.MinPoint.Y);
            double sMin = -reach;
            double sMax = len + reach;
            double step = 5.0 * Scale;

            var near = new Extents3d();
            near.AddPoint(new Point3d(ax, ay, 0));
            near.AddPoint(new Point3d(bx, by, 0));
            near.AddPoint(new Point3d(ax + upX * off, ay + upY * off, 0));
            near.AddPoint(new Point3d(bx + upX * off, by + upY * off, 0));
            var segs = new List<(Point2d A, Point2d B)>();
            var blocks = new List<Extents3d>();
            CollectBasamakBlockers(tr, ms, detBefore, sectionBefore, drawingBefore, ox, oy, near,
                0.5 * (ax + bx), 0.5 * (ay + by), segs, blocks);

            bool Clear(double mdx, double mdy)
            {
                double minX = box.MinPoint.X + mdx;
                double maxX = box.MaxPoint.X + mdx;
                double minY = box.MinPoint.Y + mdy;
                double maxY = box.MaxPoint.Y + mdy;
                const double tol = 0.5;
                foreach (var s in segs)
                {
                    if (DistSegToAabb(s.A, s.B, minX, minY, maxX, maxY, out _, out _) < tol)
                        return false;
                }
                foreach (var b in blocks)
                {
                    if (DistAabbToAabb(
                            minX, minY, maxX, maxY,
                            b.MinPoint.X, b.MinPoint.Y, b.MaxPoint.X, b.MaxPoint.Y,
                            out _, out _) < tol)
                        return false;
                }
                return true;
            }

            bool found = false;
            double useDx = 0, useDy = 0;
            int nTry = (int)Math.Ceiling((sMax - sMin) / step) + 2;
            for (int k = 0; k <= nTry; k++)
            {
                double s = k == 0
                    ? ideal
                    : ideal + ((k % 2 == 1) ? 1.0 : -1.0) * ((k + 1) / 2) * step;
                if (s < sMin || s > sMax) continue;
                double px = ax + dirX * s + upX * off;
                double py = ay + dirY * s + upY * off;
                double mdx = px - cornerX;
                double mdy = py - cornerY;
                if (!Clear(mdx, mdy)) continue;
                useDx = mdx;
                useDy = mdy;
                found = true;
                break;
            }
            if (!found)
            {
                double px = ax + dirX * ideal + upX * off;
                double py = ay + dirY * ideal + upY * off;
                useDx = px - cornerX;
                useDy = py - cornerY;
            }

            if (Math.Abs(useDx) < 0.01 && Math.Abs(useDy) < 0.01) return;
            var move = Matrix3d.Displacement(new Vector3d(useDx, useDy, 0));
            foreach (ObjectId id in ms)
            {
                if (id.IsNull || id.IsErased || detBefore.Contains(id)) continue;
                Entity ent;
                try { ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity; }
                catch { continue; }
                if (ent == null) continue;
                try { ent.TransformBy(move); } catch { }
            }
        }

        private static void ClampBasamakShift(
            Extents3d box, ref double dx, ref double dy,
            double iMinX, double iMinY, double iMaxX, double iMaxY)
        {
            double nMinX = box.MinPoint.X + dx;
            double nMinY = box.MinPoint.Y + dy;
            if (nMinX < iMinX) dx += iMinX - nMinX;
            if (box.MaxPoint.X + dx > iMaxX) dx -= (box.MaxPoint.X + dx) - iMaxX;
            if (nMinY < iMinY) dy += iMinY - nMinY;
            if (box.MaxPoint.Y + dy > iMaxY) dy -= (box.MaxPoint.Y + dy) - iMaxY;
        }

        private static bool TrySectionContentExtents(
            Transaction tr, BlockTableRecord ms,
            HashSet<ObjectId> detBefore, HashSet<ObjectId> sectionBefore,
            double ox, double oy, out Extents3d content)
        {
            content = new Extents3d();
            bool any = false;
            bool sectionAway = Math.Abs(ox) > 20 || Math.Abs(oy) > 20;
            if (detBefore == null || sectionBefore == null) return false;
            foreach (ObjectId id in detBefore)
            {
                if (id.IsNull || id.IsErased || sectionBefore.Contains(id)) continue;
                Entity ent;
                try { ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity; }
                catch { continue; }
                if (ent == null || ent.IsErased || ent.Layer == LayerKesitSiniri) continue;
                Extents3d ex;
                try { ex = ent.GeometricExtents; }
                catch { continue; }
                if (ex.MaxPoint.X - ex.MinPoint.X < 0.01 && ex.MaxPoint.Y - ex.MinPoint.Y < 0.01)
                    continue;
                if (sectionAway && ExtentsTouchOrigin(ex)) continue;
                if (!any) { content = ex; any = true; }
                else content.AddExtents(ex);
            }
            return any;
        }

        private static void CollectBasamakBlockers(
            Transaction tr, BlockTableRecord ms,
            HashSet<ObjectId> detBefore, HashSet<ObjectId> sectionBefore,
            HashSet<ObjectId> drawingBefore,
            double ox, double oy, Extents3d content, double noseX, double noseY,
            List<(Point2d A, Point2d B)> segs, List<Extents3d> blocks)
        {
            bool sectionAway = Math.Abs(ox) > 20 || Math.Abs(oy) > 20;
            const double near = 4000.0;
            double nMinX = content.MinPoint.X - near;
            double nMaxX = content.MaxPoint.X + near;
            double nMinY = content.MinPoint.Y - near;
            double nMaxY = content.MaxPoint.Y + near;
            if (detBefore == null) return;

            void Consider(ObjectId id)
            {
                if (id.IsNull || id.IsErased) return;
                bool foreign = sectionBefore != null && sectionBefore.Contains(id);
                Entity ent;
                try { ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity; }
                catch { return; }
                if (ent == null || ent.IsErased) return;

                if (string.Equals(ent.Layer, LayerKesitSiniri, StringComparison.OrdinalIgnoreCase)
                    && ent is Polyline kpl && kpl.Closed && kpl.NumberOfVertices >= 4)
                {
                    Extents3d kex;
                    try { kex = ent.GeometricExtents; }
                    catch { return; }
                    if (sectionAway && ExtentsTouchOrigin(kex)) return;
                    bool noseIn = noseX >= kex.MinPoint.X && noseX <= kex.MaxPoint.X
                        && noseY >= kex.MinPoint.Y && noseY <= kex.MaxPoint.Y;
                    if (!noseIn)
                        blocks.Add(kex);
                    return;
                }
                if (string.Equals(ent.Layer, LayerKesitSiniri, StringComparison.OrdinalIgnoreCase))
                    return;

                if (foreign)
                {
                    Extents3d fex;
                    try { fex = ent.GeometricExtents; }
                    catch { return; }
                    if (fex.MaxPoint.X < nMinX || fex.MinPoint.X > nMaxX
                        || fex.MaxPoint.Y < nMinY || fex.MinPoint.Y > nMaxY)
                        return;
                    if (sectionAway && ExtentsTouchOrigin(fex)) return;
                }

                if (ent is Line ln)
                {
                    segs.Add((new Point2d(ln.StartPoint.X, ln.StartPoint.Y), new Point2d(ln.EndPoint.X, ln.EndPoint.Y)));
                    return;
                }
                if (ent is Polyline pl)
                {
                    int n = pl.NumberOfVertices;
                    int lim = pl.Closed ? n : n - 1;
                    for (int i = 0; i < lim; i++)
                    {
                        Point2d a = pl.GetPoint2dAt(i);
                        Point2d b = pl.GetPoint2dAt((i + 1) % n);
                        if (a.GetDistanceTo(b) < 0.01) continue;
                        segs.Add((a, b));
                    }
                    return;
                }

                Extents3d ex;
                try { ex = ent.GeometricExtents; }
                catch { return; }
                if (ex.MaxPoint.X - ex.MinPoint.X < 0.01 && ex.MaxPoint.Y - ex.MinPoint.Y < 0.01)
                    return;
                if (sectionAway && ExtentsTouchOrigin(ex)) return;
                if (ex.MaxPoint.X < nMinX || ex.MinPoint.X > nMaxX
                    || ex.MaxPoint.Y < nMinY || ex.MinPoint.Y > nMaxY)
                    return;
                blocks.Add(ex);
            }

            foreach (ObjectId id in detBefore)
            {
                if (drawingBefore != null && drawingBefore.Contains(id)) continue;
                Consider(id);
            }
            AddExistingNearBlockers(tr, drawingBefore, nMinX, nMinY, nMaxX, nMaxY, Consider);
        }

        /// <summary>Eski çizimden yalnız soffit penceresine düşenler. Uzamsal seçim.</summary>
        private static void AddExistingNearBlockers(
            Transaction tr, HashSet<ObjectId> drawingBefore,
            double minX, double minY, double maxX, double maxY,
            Action<ObjectId> consider)
        {
            if (tr == null || drawingBefore == null || drawingBefore.Count == 0 || consider == null) return;
            if (maxX - minX < 1 || maxY - minY < 1) return;
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                var res = doc.Editor.SelectCrossingWindow(
                    new Point3d(minX, minY, 0),
                    new Point3d(maxX, maxY, 0),
                    new SelectionFilter(new[]
                    {
                        new TypedValue((int)DxfCode.Start,
                            "LINE,LWPOLYLINE,POLYLINE,ARC,CIRCLE,INSERT,HATCH,TEXT,MTEXT,DIMENSION,ELLIPSE,SPLINE")
                    }));
                if (res.Status != PromptStatus.OK || res.Value == null) return;
                foreach (ObjectId id in res.Value.GetObjectIds())
                {
                    if (drawingBefore.Contains(id)) consider(id);
                }
            }
            catch { }
        }

        /// <summary>En yakın engelden gap kadar uzaklaş. İterek ayıramazsa false.</summary>
        private static bool TryPushBasamakClear(
            Extents3d box, double dx, double dy,
            List<(Point2d A, Point2d B)> segs, List<Extents3d> blocks,
            double gap, double fxn, double fyn,
            out double px, out double py)
        {
            px = 0;
            py = 0;
            double minX = box.MinPoint.X + dx;
            double maxX = box.MaxPoint.X + dx;
            double minY = box.MinPoint.Y + dy;
            double maxY = box.MaxPoint.Y + dy;
            double rcx = 0.5 * (minX + maxX);
            double rcy = 0.5 * (minY + maxY);
            double worst = 0;
            double wx = fxn, wy = fyn;
            double ox = 0, oy = 0;

            void Consider(double dist, double ax, double ay, double exit)
            {
                double need = dist <= 0.05 ? exit + gap : gap - dist;
                if (need <= worst) return;
                double vx = rcx - ax;
                double vy = rcy - ay;
                double vl = Math.Sqrt(vx * vx + vy * vy);
                if (vl < 1e-6) { vx = fxn; vy = fyn; vl = 1; }
                else { vx /= vl; vy /= vl; }
                worst = need;
                wx = vx;
                wy = vy;
                ox = 0;
                oy = 0;
            }

            if (segs != null)
            {
                foreach (var s in segs)
                {
                    double d = DistSegToAabb(s.A, s.B, minX, minY, maxX, maxY, out double cx, out double cy);
                    if (d >= gap) continue;
                    double exit = 0;
                    if (d <= 0.05)
                    {
                        double dl = Math.Abs(cx - minX);
                        double dr = Math.Abs(maxX - cx);
                        double db = Math.Abs(cy - minY);
                        double dt = Math.Abs(maxY - cy);
                        exit = Math.Min(Math.Min(dl, dr), Math.Min(db, dt));
                    }
                    Consider(d, cx, cy, exit);
                }
            }
            if (blocks != null)
            {
                foreach (var b in blocks)
                {
                    if (!TrySeparateAabb(
                            minX, minY, maxX, maxY,
                            b.MinPoint.X, b.MinPoint.Y, b.MaxPoint.X, b.MaxPoint.Y,
                            gap, out double mx, out double my))
                        continue;
                    double len = Math.Sqrt(mx * mx + my * my);
                    if (len > worst)
                    {
                        worst = len;
                        wx = len > 1e-9 ? mx / len : fxn;
                        wy = len > 1e-9 ? my / len : fyn;
                        ox = mx;
                        oy = my;
                    }
                }
            }
            if (worst <= 0.05) return false;
            if (Math.Abs(ox) < 1e-9 && Math.Abs(oy) < 1e-9)
            {
                px = wx * (worst + 0.5);
                py = wy * (worst + 0.5);
            }
            else
            {
                px = ox;
                py = oy;
            }
            return true;
        }

        /// <summary>İki kutu çakışıyor veya gap’ten yakınsa, kutuyu ayıran en kısa ötelemeyi verir.</summary>
        private static bool TrySeparateAabb(
            double minX, double minY, double maxX, double maxY,
            double bMinX, double bMinY, double bMaxX, double bMaxY,
            double gap, out double mx, out double my)
        {
            mx = 0;
            my = 0;
            double sepX = 0, sepY = 0;
            if (maxX < bMinX) sepX = bMinX - maxX;
            else if (bMaxX < minX) sepX = minX - bMaxX;
            if (maxY < bMinY) sepY = bMinY - maxY;
            else if (bMaxY < minY) sepY = minY - bMaxY;
            if (sepX >= gap || sepY >= gap) return false;

            if (sepX > 0 && sepY == 0)
            {
                mx = (maxX < bMinX ? -1.0 : 1.0) * (gap - sepX);
                return true;
            }
            if (sepY > 0 && sepX == 0)
            {
                my = (maxY < bMinY ? -1.0 : 1.0) * (gap - sepY);
                return true;
            }

            double pushLeft = (bMinX - gap) - maxX;
            double pushRight = (bMaxX + gap) - minX;
            double pushDown = (bMinY - gap) - maxY;
            double pushUp = (bMaxY + gap) - minY;
            double best = Math.Abs(pushLeft);
            mx = pushLeft;
            my = 0;
            if (Math.Abs(pushRight) < best) { best = Math.Abs(pushRight); mx = pushRight; my = 0; }
            if (Math.Abs(pushDown) < best) { best = Math.Abs(pushDown); mx = 0; my = pushDown; }
            if (Math.Abs(pushUp) < best) { mx = 0; my = pushUp; }
            return true;
        }

        private static double DistSegToAabb(
            Point2d a, Point2d b,
            double minX, double minY, double maxX, double maxY,
            out double cx, out double cy)
        {
            cx = 0.5 * (a.X + b.X);
            cy = 0.5 * (a.Y + b.Y);
            double best = double.MaxValue;
            ConsiderPointToAabb(a.X, a.Y, minX, minY, maxX, maxY, ref best, ref cx, ref cy);
            ConsiderPointToAabb(b.X, b.Y, minX, minY, maxX, maxY, ref best, ref cx, ref cy);
            ConsiderPointToSeg(minX, minY, a, b, ref best, ref cx, ref cy);
            ConsiderPointToSeg(maxX, minY, a, b, ref best, ref cx, ref cy);
            ConsiderPointToSeg(minX, maxY, a, b, ref best, ref cx, ref cy);
            ConsiderPointToSeg(maxX, maxY, a, b, ref best, ref cx, ref cy);
            if (SegHitsAabb(a, b, minX, minY, maxX, maxY))
            {
                best = 0;
                cx = 0.5 * (Math.Max(minX, Math.Min(maxX, cx)));
                cy = 0.5 * (Math.Max(minY, Math.Min(maxY, cy)));
            }
            return best;
        }

        private static void ConsiderPointToAabb(
            double x, double y, double minX, double minY, double maxX, double maxY,
            ref double best, ref double cx, ref double cy)
        {
            double qx = Math.Max(minX, Math.Min(maxX, x));
            double qy = Math.Max(minY, Math.Min(maxY, y));
            double d = Math.Sqrt((x - qx) * (x - qx) + (y - qy) * (y - qy));
            if (d < best) { best = d; cx = x; cy = y; }
        }

        private static void ConsiderPointToSeg(
            double x, double y, Point2d a, Point2d b,
            ref double best, ref double cx, ref double cy)
        {
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double len2 = abx * abx + aby * aby;
            double t = len2 < 1e-12 ? 0 : ((x - a.X) * abx + (y - a.Y) * aby) / len2;
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            double qx = a.X + abx * t;
            double qy = a.Y + aby * t;
            double d = Math.Sqrt((x - qx) * (x - qx) + (y - qy) * (y - qy));
            if (d < best) { best = d; cx = qx; cy = qy; }
        }

        private static bool SegHitsAabb(
            Point2d a, Point2d b, double minX, double minY, double maxX, double maxY)
        {
            if (a.X >= minX && a.X <= maxX && a.Y >= minY && a.Y <= maxY) return true;
            if (b.X >= minX && b.X <= maxX && b.Y >= minY && b.Y <= maxY) return true;
            return SegCross(a, b, minX, minY, maxX, minY)
                || SegCross(a, b, maxX, minY, maxX, maxY)
                || SegCross(a, b, maxX, maxY, minX, maxY)
                || SegCross(a, b, minX, maxY, minX, minY);
        }

        private static bool SegCross(Point2d a, Point2d b, double x1, double y1, double x2, double y2)
        {
            double d1 = (x2 - x1) * (a.Y - y1) - (y2 - y1) * (a.X - x1);
            double d2 = (x2 - x1) * (b.Y - y1) - (y2 - y1) * (b.X - x1);
            double d3 = (b.X - a.X) * (y1 - a.Y) - (b.Y - a.Y) * (x1 - a.X);
            double d4 = (b.X - a.X) * (y2 - a.Y) - (b.Y - a.Y) * (x2 - a.X);
            return d1 * d2 < 0 && d3 * d4 < 0;
        }

        private static double DistAabbToAabb(
            double aMinX, double aMinY, double aMaxX, double aMaxY,
            double bMinX, double bMinY, double bMaxX, double bMaxY,
            out double cx, out double cy)
        {
            double dx = 0, dy = 0;
            if (aMaxX < bMinX) dx = bMinX - aMaxX;
            else if (bMaxX < aMinX) dx = aMinX - bMaxX;
            if (aMaxY < bMinY) dy = bMinY - aMaxY;
            else if (bMaxY < aMinY) dy = aMinY - bMaxY;
            cx = 0.5 * (Math.Max(aMinX, bMinX) + Math.Min(aMaxX, bMaxX));
            cy = 0.5 * (Math.Max(aMinY, bMinY) + Math.Min(aMaxY, bMaxY));
            if (dx == 0 && dy == 0) return 0;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool TryUnionNewExtents(
            Transaction tr, BlockTableRecord ms, HashSet<ObjectId> before,
            double ox, double oy, out Extents3d box)
        {
            box = new Extents3d();
            bool any = false;
            bool sectionAway = Math.Abs(ox) > 20 || Math.Abs(oy) > 20;
            foreach (ObjectId id in ms)
            {
                if (id.IsNull || id.IsErased || before.Contains(id)) continue;
                Entity ent;
                try { ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity; }
                catch { continue; }
                if (ent == null) continue;
                if (ent is Dimension dim)
                {
                    try
                    {
                        if (!dim.IsWriteEnabled) dim.UpgradeOpen();
                        dim.RecomputeDimensionBlock(true);
                    }
                    catch { }
                }
                Extents3d ex;
                try { ex = ent.GeometricExtents; }
                catch { continue; }
                if (ex.MaxPoint.X - ex.MinPoint.X < 0.01 && ex.MaxPoint.Y - ex.MinPoint.Y < 0.01)
                    continue;
                if (sectionAway && ExtentsTouchOrigin(ex))
                    continue;
                if (!any) { box = ex; any = true; }
                else box.AddExtents(ex);
            }
            return any;
        }

        private static HashSet<ObjectId> SnapshotModelSpaceIds(BlockTableRecord ms)
        {
            var ids = new HashSet<ObjectId>();
            foreach (ObjectId id in ms)
                ids.Add(id);
            return ids;
        }

        private static bool ExtentsTouchOrigin(Extents3d ex)
        {
            bool min0 = Math.Abs(ex.MinPoint.X) < 1.0 && Math.Abs(ex.MinPoint.Y) < 1.0;
            bool max0 = Math.Abs(ex.MaxPoint.X) < 1.0 && Math.Abs(ex.MaxPoint.Y) < 1.0;
            return min0 || max0;
        }

        /// <summary>
        /// Basamak enine kesit: W×H dikdörtgen, üst/alt tevzi gönyeli (R=1.5 dışbükey),
        /// ana çemberler gönye radius merkezleri arasında (kol genişliği adedi),
        /// altta tevzi açılımları — adet = uçuş kesiti 1–29 / 10–29 tevzi sayısı.
        /// </summary>
        private static void DrawBasamakKesitDetay(
            Transaction tr, Database db, BlockTableRecord ms,
            double ox, double oy, double kolW, double dosemeCm, int tevziAdet)
        {
            MerdivenSettings.EnsureLoaded();
            if (kolW < 40) kolW = 120;
            if (dosemeCm < 5) dosemeCm = 15;

            double pas = ClampDonati(MerdivenSettings.PaspayiCm, 1, 5, 3);
            double tevziAralik = MerdivenSettings.TevziAralikCm;
            if (tevziAralik < 5) tevziAralik = 20;
            double anaAralik = MerdivenSettings.AnaDonatiAralikCm;
            if (anaAralik < 5) anaAralik = 20;
            int anaAdet = CalcDonatiAdet(kolW, anaAralik, pas);
            // Tevzi açılım: 1–29 (ilk) / 10–29 (diğer) arası kopya sayısı
            if (tevziAdet < 1) tevziAdet = anaAdet;

            double phiTevziCm = Math.Max(0.6, MerdivenSettings.TevziDonatiCapMm / 10.0);
            double hook = Math.Max(8.0, Math.Round(12.0 * phiTevziCm, MidpointRounding.AwayFromZero));
            double maxHook = Math.Max(dosemeCm - 2.0 * pas - 0.5, 5.0);
            if (hook > maxHook) hook = maxHook;

            double w = kolW;
            double h = dosemeCm;
            double yTop = 0;
            double yBot = -h;
            double xL = 0;
            double xR = w;

            AddPline(tr, ms, new List<Point2d>
            {
                new Point2d(xL, yTop),
                new Point2d(xR, yTop),
                new Point2d(xR, yBot),
                new Point2d(xL, yBot)
            }, true, LayerKesitCizgisi, Scale, ox, oy);

            double barL = xL + pas;
            double barR = xR - pas;
            double yUst = yTop - pas;
            double yAlt = yBot + pas;

            // Üst/alt tevzi: gönye R=1.5 dış merkezli
            DrawGoneliTevziBar(tr, ms, ox, oy, barL, barR, yUst, hook, hooksDown: true);
            DrawGoneliTevziBar(tr, ms, ox, oy, barL, barR, yAlt, hook, hooksDown: false);

            // Çemberler: gönye fillet radius merkezleri arasında eşit dağılım
            if (TryTevziGonyeFilletCenters(barL, barR, yUst, hook, hooksDown: true,
                    out Point2d cUstL, out Point2d cUstR) &&
                TryTevziGonyeFilletCenters(barL, barR, yAlt, hook, hooksDown: false,
                    out Point2d cAltL, out Point2d cAltR))
            {
                for (int i = 0; i < anaAdet; i++)
                {
                    double t = anaAdet == 1 ? 0.5 : i / (double)(anaAdet - 1);
                    DrawAnaCircleAt(tr, ms, ox, oy, Lerp2(cUstL, cUstR, t));
                    DrawAnaCircleAt(tr, ms, ox, oy, Lerp2(cAltL, cAltR, t));
                }
            }

            ObjectId styleId = EnsureTextStyle(tr, db);
            ObjectId dimStyleId = ResolveOrCreatePlanOlcu25(tr, db);
            EnsureLayer(tr, db, LayerOlcu, 3, LineWeight.LineWeight030, false);
            // Ölçü: yazı kutusu / dim çizgisi kesitten 10 cm
            const double dimOfsetCm = 10.0;
            Point3d dimW = new Point3d(ox + (w * 0.5) * Scale, oy + (yBot - dimOfsetCm) * Scale, 0);
            PlacePlanOlcuDimFixedExt(tr, ms,
                new Point3d(ox + xL * Scale, oy + yBot * Scale, 0),
                new Point3d(ox + xR * Scale, oy + yBot * Scale, 0),
                dimW, 0.0, dimStyleId, LayerOlcu);
            Point3d dimH = new Point3d(ox + (xR + dimOfsetCm) * Scale, oy + ((yTop + yBot) * 0.5) * Scale, 0);
            PlacePlanOlcuDimFixedExt(tr, ms,
                new Point3d(ox + xR * Scale, oy + yTop * Scale, 0),
                new Point3d(ox + xR * Scale, oy + yBot * Scale, 0),
                dimH, Math.PI * 0.5, dimStyleId, LayerOlcu);

            double netBoy = Math.Max(barR - barL, 1);
            int netBoyCm = (int)Math.Round(netBoy, MidpointRounding.AwayFromZero);
            int hookCm = (int)Math.Round(hook, MidpointRounding.AwayFromZero);
            int Lcm = netBoyCm + 2 * hookCm;
            int aralikCm = (int)Math.Round(tevziAralik, MidpointRounding.AwayFromZero);
            string etiket = WithMerdivenCarpan(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}\u00F8{1}/{2} L={3}",
                tevziAdet, MerdivenSettings.TevziDonatiCapMm, aralikCm, Lcm));

            // Açılım: kesitteki üst/alt tevzinin kopyası — üst −40 cm, alt −55 cm
            const double ustAcilimAsagiCm = 40.0;
            const double altAcilimAsagiCm = 55.0;
            double acilimUstY = yUst - ustAcilimAsagiCm;
            double acilimAltY = yAlt - altAcilimAsagiCm;
            DrawTevziAcilimBar(tr, ms, styleId, ox, oy,
                barL, barR, midY: acilimUstY, hook: hook, hooksDown: true,
                etiket: etiket, faceNote: "üst");
            DrawTevziAcilimBar(tr, ms, styleId, ox, oy,
                barL, barR, midY: acilimAltY, hook: hook, hooksDown: false,
                etiket: etiket, faceNote: "alt");
        }

        private static Point2d Lerp2(Point2d a, Point2d b, double t)
        {
            return new Point2d(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        /// <summary>Uçuş kesiti tevzi gönyesi: keskin poligon → R=1.5 dışbükey fillet.</summary>
        private static void DrawGoneliTevziBar(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy,
            double barL, double barR, double yBar, double hook, bool hooksDown)
        {
            double yHook = hooksDown ? yBar - hook : yBar + hook;
            var sharp = new List<Point2d>
            {
                new Point2d(barL, yHook),
                new Point2d(barL, yBar),
                new Point2d(barR, yBar),
                new Point2d(barR, yHook)
            };
            var path = FilletDonatiPath(sharp, DonatiRadiusCm);
            if (path == null || path.Count < 2) return;
            AddPlineBulge(tr, ms, path, LayerDonati, Scale, ox, oy);
        }

        /// <summary>Sol/sağ gönye fillet merkezleri (çember uç noktaları).</summary>
        private static bool TryTevziGonyeFilletCenters(
            double barL, double barR, double yBar, double hook, bool hooksDown,
            out Point2d cL, out Point2d cR)
        {
            cL = default;
            cR = default;
            double yHook = hooksDown ? yBar - hook : yBar + hook;
            bool okL = TryFilletArcCenter(
                new Point2d(barL, yHook), new Point2d(barL, yBar), new Point2d(barR, yBar),
                DonatiRadiusCm, out cL);
            bool okR = TryFilletArcCenter(
                new Point2d(barL, yBar), new Point2d(barR, yBar), new Point2d(barR, yHook),
                DonatiRadiusCm, out cR);
            return okL && okR;
        }

        private static int CalcDonatiAdet(double kolGenislikCm, double aralikCm, double pasCm)
        {
            if (aralikCm < 1) aralikCm = 20;
            if (pasCm < 0) pasCm = 0;
            double net = kolGenislikCm - 2.0 * pasCm;
            if (net < aralikCm) net = aralikCm;
            int gaps = (int)Math.Ceiling(net / aralikCm);
            if (gaps < 1) gaps = 1;
            return gaps + 1;
        }

        private static void DrawAnaCircleAt(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Point2d centerLocal)
        {
            // Ana donatı kesit: tevzi çemberi ile aynı görsel (çap 1.5, width 3)
            double rr = TevziCircleDiaCm * 0.5;
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(ox + (centerLocal.X - rr) * Scale, oy + centerLocal.Y * Scale), 1.0, 0, 0);
            pl.AddVertexAt(1, new Point2d(ox + (centerLocal.X + rr) * Scale, oy + centerLocal.Y * Scale), 1.0, 0, 0);
            pl.Closed = true;
            pl.ConstantWidth = TevziCircleWidthCm * Scale;
            pl.Layer = LayerDonati;
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        /// <summary>
        /// Tevzi açılımı: kesitteki gönyeli tevzinin kopyası (aynı X).
        /// Etiket / kol boyu / üst-alt: yazı kutusu kenarı donatıdan 4 cm (1/25).
        /// </summary>
        private static void DrawTevziAcilimBar(
            Transaction tr, BlockTableRecord ms, ObjectId styleId,
            double ox, double oy,
            double barL, double barR, double midY, double hook, bool hooksDown,
            string etiket, string faceNote)
        {
            double yHook = hooksDown ? midY - hook : midY + hook;
            double midX = 0.5 * (barL + barR);

            DrawGoneliTevziBar(tr, ms, ox, oy, barL, barR, midY, hook, hooksDown);

            int netCm = (int)Math.Round(barR - barL, MidpointRounding.AwayFromZero);
            int hookCm = (int)Math.Round(hook, MidpointRounding.AwayFromZero);
            Point2d midBar = new Point2d(midX, midY);

            // Etiket + yatay kol boyu: yazı kutusu 4 cm üstte; yüz 4 cm altta
            Extents3d? etiketBox = null;
            DBText etiketTxt = AddAcilimEtiketAboveBar(tr, ms, styleId, etiket,
                midBar, ox, oy, height: 10.0, rotationRad: 0);
            if (etiketTxt != null && TryGetTextExtents(etiketTxt, out Extents3d eBox))
                etiketBox = eBox;

            DBText faceTxt = AddAcilimKolYazi(tr, ms, styleId, faceNote,
                midBar, ox, oy,
                height: 10.0, rotationRad: 0, aboveBar: false, byLayer: false);
            DBText boyTxt = AddAcilimKolYazi(tr, ms, styleId, netCm.ToString(),
                midBar, ox, oy,
                height: 10.0, rotationRad: 0, aboveBar: true, byLayer: true);

            // Boy etiketle çakışırsa alta, yüz yazısının soluna
            if (boyTxt != null && etiketBox.HasValue &&
                TryGetTextExtents(boyTxt, out Extents3d boyBox) &&
                BoxesOverlap2d(boyBox, etiketBox.Value))
            {
                try { boyTxt.Erase(); } catch { }
                boyTxt = AddAcilimKolYazi(tr, ms, styleId, netCm.ToString(),
                    midBar, ox, oy,
                    height: 10.0, rotationRad: 0, aboveBar: false, byLayer: true);
                if (boyTxt != null && faceTxt != null &&
                    TryGetTextExtents(boyTxt, out Extents3d boy2) &&
                    TryGetTextExtents(faceTxt, out Extents3d faceBox))
                {
                    double gapX = 4.0;
                    double targetRight = faceBox.MinPoint.X - gapX;
                    double dx = targetRight - boy2.MaxPoint.X;
                    if (Math.Abs(dx) > 0.1)
                        boyTxt.TransformBy(Matrix3d.Displacement(new Vector3d(dx, 0, 0)));
                }
            }

            // Gönye boyları: yazı kutusu kola 4 cm
            Point2d midHookL = new Point2d(barL, 0.5 * (midY + yHook));
            Point2d midHookR = new Point2d(barR, 0.5 * (midY + yHook));
            AddAcilimKolYazi(tr, ms, styleId, hookCm.ToString(),
                midHookL, ox, oy,
                height: 10.0, rotationRad: Math.PI * 0.5, aboveBar: true, byLayer: false, aci: 3);
            AddAcilimKolYazi(tr, ms, styleId, hookCm.ToString(),
                midHookR, ox, oy,
                height: 10.0, rotationRad: Math.PI * 0.5, aboveBar: true, byLayer: false, aci: 3);
        }

        private static bool BoxesOverlap2d(Extents3d a, Extents3d b)
        {
            return a.MinPoint.X < b.MaxPoint.X && a.MaxPoint.X > b.MinPoint.X
                && a.MinPoint.Y < b.MaxPoint.Y && a.MaxPoint.Y > b.MinPoint.Y;
        }

        /// <summary>
        /// Basamaklı kesit 3 bölgesi (yerel kesit cm; flip henüz uygulanmamış).
        /// 1 alt sahanlık, 2 basamak (alt = soffit kırım→üst, sanal üst = nose), 3 üst sahanlık.
        /// </summary>
        private sealed class FlightKesitZones
        {
            public double AltX0, AltX1;
            public double AltYTop, AltYBot;
            /// <summary>Basamak alt: kırım (landLo) → üst sahanlık birleşimi (landHi).</summary>
            public Point2d BasAltA, BasAltB;
            public Point2d BasUstA, BasUstB;
            public double UstX0, UstX1;
            public double UstYTop, UstYBot;
            public double DosemeCm;
        }

        private static FlightKesitZones BuildFlightKesitZones(
            double x0, double landEndX, double flightStartX, double lastRiserX,
            double yTop, double yLandLo, double yLandHi, double dosemeCm,
            Point2d noseA, Point2d noseB, Point2d landLo, Point2d landHi)
        {
            return new FlightKesitZones
            {
                // 1) Alt sahanlık: üst y=0, alt y=yLandLo — X: x0 → kırım
                AltX0 = x0,
                AltX1 = landLo.X,
                AltYTop = 0,
                AltYBot = yLandLo,
                // 2) Basamak alt = çizilen soffit (kırım→landHi); sanal üst = nose
                BasAltA = landLo,
                BasAltB = landHi,
                BasUstA = noseA,
                BasUstB = noseB,
                // 3) Üst sahanlık
                UstX0 = lastRiserX,
                UstX1 = landEndX,
                UstYTop = yTop,
                UstYBot = yLandHi,
                DosemeCm = dosemeCm
            };
        }

        /// <summary>
        /// Basamaksız kesit donatı:
        /// 6 — alt +pas, iki uç yukarı gönye (doseme−2pas);
        /// 7 — üst −pas, iki uç aşağı gönye (mesnette 12φ, yoksa doseme−2pas).
        /// </summary>
        private static void DrawKesitDonatiLanding(
            Transaction tr, Database db, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            double xFaceL, double xFaceR,
            double mesnetOuterL, double mesnetOuterR,
            double yTop, double yBot, double dosemeCm,
            bool solMesnet, bool sagMesnet)
        {
            MerdivenSettings.EnsureLoaded();
            double pas = ClampDonati(MerdivenSettings.PaspayiCm, 1, 5, 3);
            double phiCm = Math.Max(0.8, MerdivenSettings.AnaDonatiCapMm / 10.0);
            if (dosemeCm < 2 * pas + 1) return;

            double xL = solMesnet ? mesnetOuterL + pas : xFaceL + pas;
            double xR = sagMesnet ? mesnetOuterR - pas : xFaceR - pas;
            if (xR <= xL + Tol) return;

            ObjectId styleId = EnsureTextStyle(tr, db);

            // —— 6: alt ——
            double yAlt = yBot + pas;
            double gonyeAlt = Math.Max(dosemeCm - 2.0 * pas, pas);
            var sharp6 = new List<Point2d>
            {
                new Point2d(xL, yAlt + gonyeAlt),
                new Point2d(xL, yAlt),
                new Point2d(xR, yAlt),
                new Point2d(xR, yAlt + gonyeAlt)
            };
            DrawDonatiPathWithAcilim(
                tr, ms, ox, oy, fx, sharp6, yBot, yAlt, acilimSlot: 0, donatiNo: 6, styleId,
                true, null, pas, yTop);

            // —— 7: üst —— uç başına: o uçta kolon/kiriş/perde varsa 12φ, yoksa mer.kal−2pas
            // (serbest uçta uç Y = yAlt → alt donatı gönyesi ile aynı boy)
            double yUst = yTop - pas;
            double gonye12 = Math.Max(12.0 * phiCm, pas);
            // X konumundan çıkar: mesnette çubuk yüzün dışına taşar
            bool leftMesnetHook = xL < xFaceL - 0.01;
            bool rightMesnetHook = xR > xFaceR + 0.01;
            double tipYL = leftMesnetHook ? yUst - gonye12 : yAlt;
            double tipYR = rightMesnetHook ? yUst - gonye12 : yAlt;
            var sharp7 = new List<Point2d>
            {
                new Point2d(xL, tipYL),
                new Point2d(xL, yUst),
                new Point2d(xR, yUst),
                new Point2d(xR, tipYR)
            };
            DrawDonatiPathWithAcilim(
                tr, ms, ox, oy, fx, sharp7, yBot, yUst, acilimSlot: 1, donatiNo: 7, styleId,
                true, null, pas, yTop);

            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                doc?.Editor?.WriteMessage(
                    "\n[MERDIVEN] donati7 gonye: sol={0:0.#}cm ({1}) sag={2:0.#}cm ({3}) doseme={4:0.#} pas={5:0.#}",
                    yUst - tipYL, leftMesnetHook ? "12phi" : "kal-2pas",
                    yUst - tipYR, rightMesnetHook ? "12phi" : "kal-2pas",
                    dosemeCm, pas);
            }
            catch { }

            DrawLandingTevziGonye(
                tr, ms, ox, oy, fx, styleId,
                xL, xR, yAlt, yUst,
                solMesnet, sagMesnet, xFaceL, xFaceR, pas);
        }

        /// <summary>
        /// Basamaksız 6/7 gönye kırımları (90°): merkez = köşe ± R içeri.
        /// Kolon/kiriş/perde varsa X = kesit içine bakan yüz ± (1.5+pas) kesit tarafına.
        /// </summary>
        private static void DrawLandingTevziGonye(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx, ObjectId styleId,
            double xL, double xR, double yAlt, double yUst,
            bool solMesnet, bool sagMesnet,
            double xFaceL, double xFaceR, double pas)
        {
            _tevziMap = new Dictionary<int, Point2d>();
            try
            {
                double r = DonatiRadiusCm;
                double faceIn = TevziBazOfsetCm + pas; // 1.5 + pas

                // 6 alt — gönye yukarı: merkez köşenin sağ-üst / sol-üst içi
                {
                    Point2d cL = new Point2d(xL + r, yAlt + r);
                    Point2d cR = new Point2d(xR - r, yAlt + r);
                    if (solMesnet) cL = new Point2d(xFaceL + faceIn, cL.Y);
                    if (sagMesnet) cR = new Point2d(xFaceR - faceIn, cR.Y);
                    PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, cL, TevziNo_6GonyeSol);
                    PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, cR, TevziNo_6GonyeSag);
                }

                // 7 üst — gönye aşağı: merkez köşenin sağ-alt / sol-alt içi
                {
                    Point2d cL = new Point2d(xL + r, yUst - r);
                    Point2d cR = new Point2d(xR - r, yUst - r);
                    if (solMesnet) cL = new Point2d(xFaceL + faceIn, cL.Y);
                    if (sagMesnet) cR = new Point2d(xFaceR - faceIn, cR.Y);
                    PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, cL, TevziNo_7GonyeSol);
                    PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, cR, TevziNo_7GonyeSag);
                }

                FillTevziPairs(tr, ms, ox, oy, fx,
                    TevziNo_6GonyeSol, TevziNo_6GonyeSag,
                    TevziNo_7GonyeSol, TevziNo_7GonyeSag);
            }
            finally { _tevziMap = null; }
        }

        /// <summary>
        /// Kol donatısı — adım adım. 1. çubuk yalnız ilk basamaklı kol kesitinde.
        /// </summary>
        private static void DrawKesitDonatiFlight(
            Transaction tr, Database db, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            double xFaceL, double xFaceR,
            double flightStartX, double lastRiserX,
            double yTop, double yLandLo, double yLandHi,
            double rihtH, double dosemeCm,
            Point2d noseA, Point2d noseB, Point2d sofA, Point2d sofB,
            Point2d landLo, Point2d landHi,
            bool isFirstFlightKol,
            bool altMesnetVar, double altMesnetOuterX,
            bool ustMesnetVar, double ustMesnetOuterX,
            bool temelAktif = false, double basamakCm = 0, double temelKalCm = 50)
        {
            MerdivenSettings.EnsureLoaded();
            double pas = ClampDonati(MerdivenSettings.PaspayiCm, 1, 5, 3);
            double phiCm = Math.Max(0.8, MerdivenSettings.AnaDonatiCapMm / 10.0);
            if (dosemeCm < 2 * pas + 1) return;

            var z = BuildFlightKesitZones(
                xFaceL, xFaceR, flightStartX, lastRiserX,
                yTop, yLandLo, yLandHi, dosemeCm,
                noseA, noseB, landLo, landHi);

            ObjectId styleId = EnsureTextStyle(tr, db);

            if (temelAktif)
                SetTemelAcilimOfsetleri(z, pas, phiCm, flightStartX, basamakCm, temelKalCm);

            // 1–4, 11–12 yalnız ilk basamaklı kol
            if (isFirstFlightKol)
            {
                DrawFlightDonati_AltSahanlikAlt(
                    tr, ms, ox, oy, fx, z, pas, phiCm, altMesnetVar, altMesnetOuterX, styleId,
                    temelAktif, flightStartX, temelKalCm);
                DrawFlightDonati_AltSahanlikUst(
                    tr, ms, ox, oy, fx, z, pas, phiCm, altMesnetVar, altMesnetOuterX, styleId,
                    temelAktif, flightStartX, basamakCm, temelKalCm);
                DrawFlightDonati_BasamakToUstSahanlik(
                    tr, ms, ox, oy, fx, z, pas, phiCm, ustMesnetVar, ustMesnetOuterX, styleId,
                    temelAktif);
                DrawFlightDonati_BasamakUstToUstSahanlik(
                    tr, ms, ox, oy, fx, z, pas, phiCm, ustMesnetVar, ustMesnetOuterX, styleId,
                    temelAktif);
                if (!temelAktif)
                {
                    DrawFlightDonati_AltBotToBasamakUst40(
                        tr, ms, ox, oy, fx, z, pas, phiCm, styleId);
                    DrawFlightDonati_AltTopToBasamakAlt40(
                        tr, ms, ox, oy, fx, z, pas, phiCm, styleId);
                }
            }
            else
            {
                // 8–10: ilk kol hariç diğer basamaklı kesitler
                DrawFlightDonati_AltBasamakUstContinuous(
                    tr, ms, ox, oy, fx, z, pas, phiCm,
                    altMesnetVar, altMesnetOuterX, ustMesnetVar, ustMesnetOuterX, styleId);
                DrawFlightDonati_AltBasamakUstTopContinuous(
                    tr, ms, ox, oy, fx, z, pas, phiCm,
                    altMesnetVar, altMesnetOuterX, ustMesnetVar, ustMesnetOuterX, styleId);
                DrawFlightDonati_AltUstToBasamakAlt(
                    tr, ms, ox, oy, fx, z, pas, phiCm, altMesnetVar, altMesnetOuterX, styleId);
            }

            // 5: tüm basamaklı kesitlerde
            DrawFlightDonati_UstSahanlikAltToBasamak(
                tr, ms, ox, oy, fx, z, pas, phiCm, ustMesnetVar, ustMesnetOuterX, styleId,
                isFirstFlightKol);

            // Tevzi baz noktaları
            _basamakTevziAdet = 0;
            if (isFirstFlightKol)
                DrawFlightTevziBaz_IlkKol(
                    tr, ms, ox, oy, fx, z, pas, phiCm, styleId,
                    altMesnetVar, ustMesnetVar, temelAktif);
            else
                DrawFlightTevziBaz_DigerKol(
                    tr, ms, ox, oy, fx, z, pas, phiCm, styleId,
                    altMesnetVar, ustMesnetVar);
        }

        /// <summary>
        /// İlk basamaklı kesit tevzi bazları:
        /// 1–7 önceki; 8 = 11. donatı bölge1alt∩basamaküst kırım;
        /// 20–23 = 1/2/3/5 finiş gönye radius merkezleri.
        /// </summary>
        private static void DrawFlightTevziBaz_IlkKol(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            FlightKesitZones z, double pas, double phiCm, ObjectId styleId,
            bool altMesnetVar, bool ustMesnetVar, bool temelAktif = false)
        {
            _tevziMap = new Dictionary<int, Point2d>();
            try
            {
                DrawFlightTevziBaz_IlkKolCore(
                    tr, ms, ox, oy, fx, z, pas, phiCm, styleId,
                    altMesnetVar, ustMesnetVar, temelAktif);
                // Ara tevzi (isimsiz): 21-27, 20-8, 28-6, 1-29, 2-22, 30-23
                FillTevziPairs(tr, ms, ox, oy, fx,
                    TevziNo_2Gonye, TevziNo_11KirimUstIz,
                    TevziNo_1Gonye, TevziNo_11Kirim,
                    TevziNo_12FilletUstIz, TevziNo_5Kirim,
                    TevziNo_12Fillet, TevziNo_5KirimAltIz,
                    TevziNo_3Fillet, TevziNo_3Gonye,
                    TevziNo_3FilletBotIz, TevziNo_5Gonye);
                // Basamak tevzi adedi: 1…29 (dahil) + ara kopyalar
                _basamakTevziAdet = CountTevziPairInclusive(TevziNo_12Fillet, TevziNo_5KirimAltIz);
            }
            finally { _tevziMap = null; }
        }

        private static void DrawFlightTevziBaz_IlkKolCore(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            FlightKesitZones z, double pas, double phiCm, ObjectId styleId,
            bool altMesnetVar, bool ustMesnetVar, bool temelAktif = false)
        {
            // TevziNo 1–8
            {
                double enterCm = 40.0 * phiCm;
                double yBar = z.AltYTop - pas;
                if (TryBasamakDirs(z, out double bx, out double by, out double px, out double py))
                {
                    Point2d OffsetIn(Point2d p) => new Point2d(p.X + px * pas, p.Y + py * pas);
                    Point2d oA = OffsetIn(z.BasAltA);
                    Point2d oB = OffsetIn(z.BasAltB);
                    if (TryIntersectY(oA, oB, yBar, out Point2d kirim))
                    {
                        Point2d start = new Point2d(kirim.X - enterCm, yBar);
                        Point2d end = new Point2d(kirim.X + bx * enterCm, kirim.Y + by * enterCm);
                        if (TryFilletArcCenter(start, kirim, end, DonatiRadiusCm, out Point2d c12))
                        {
                            // Temelde 1. tevzi: basamak altına paralel, temel üstünün 4.5 cm üstü
                            if (temelAktif && Math.Abs(by) > Tol)
                            {
                                const double temelUstUstuCm = 4.5;
                                double t = (temelUstUstuCm - c12.Y) / by;
                                c12 = new Point2d(c12.X + bx * t, c12.Y + by * t);
                            }
                            PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c12, TevziNo_12Fillet);
                            // 1 → basamak üst, çizgiden (pas+1.5) içeri (dik)
                            double faceIn = pas + TevziBazOfsetCm;
                            Point2d uA = new Point2d(z.BasUstA.X - px * faceIn, z.BasUstA.Y - py * faceIn);
                            Point2d uB = new Point2d(z.BasUstB.X - px * faceIn, z.BasUstB.Y - py * faceIn);
                            if (TryProjectAlongDirToLine(c12, px, py, uA, uB, out Point2d c12u))
                                PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c12u, TevziNo_12FilletUstIz);
                        }
                    }
                }
            }

            // Baz 2: 3. donatı — basamak alt / bölge3 üst birleşim radius merkezi
            {
                double yTopBar = z.UstYTop - pas;
                if (TryBasamakDirs(z, out _, out _, out double px, out double py))
                {
                    Point2d OffsetIn(Point2d p) => new Point2d(p.X + px * pas, p.Y + py * pas);
                    Point2d oA = OffsetIn(z.BasAltA);
                    Point2d oB = OffsetIn(z.BasAltB);
                    if (TryIntersectY(oA, oB, yTopBar, out Point2d join))
                    {
                        double yLoBar = z.AltYBot + pas;
                        if (!TryIntersectY(oA, oB, yLoBar, out Point2d start))
                            start = oA;
                        double xVert = z.UstX1 - pas;
                        Point2d topEnd = join.X <= xVert + Tol
                            ? new Point2d(xVert, yTopBar)
                            : new Point2d(Math.Max(join.X, xVert), yTopBar);
                        if (TryFilletArcCenter(start, join, topEnd, DonatiRadiusCm, out Point2d c3))
                        {
                            PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c3, TevziNo_3Fillet);
                            // 2 → bölge3 alt, çizgiden (pas+1.5) içeri
                            PlaceTevziBaz(tr, ms, ox, oy, fx, styleId,
                                new Point2d(c3.X, z.UstYBot + pas + TevziBazOfsetCm), TevziNo_3FilletBotIz);
                        }
                    }
                }
            }

            // Baz 3: 4. donatı — bölge3 üst / basamak üst birleşim radius merkezi
            {
                double yTopBar = z.UstYTop - pas;
                if (TryBasamakDirs(z, out _, out _, out double px, out double py))
                {
                    Point2d OffsetFromUst(Point2d p) =>
                        new Point2d(p.X - px * pas, p.Y - py * pas);
                    Point2d uA = OffsetFromUst(z.BasUstA);
                    Point2d uB = OffsetFromUst(z.BasUstB);
                    if (TryIntersectY(uA, uB, yTopBar, out Point2d join))
                    {
                        double yLoBar = z.AltYTop - pas;
                        if (!TryIntersectY(uA, uB, yLoBar, out Point2d start))
                            start = uA;
                        double xVert = z.UstX1 - pas;
                        Point2d topEnd = join.X <= xVert + Tol
                            ? new Point2d(xVert, yTopBar)
                            : new Point2d(Math.Max(join.X, xVert), yTopBar);
                        if (TryFilletArcCenter(start, join, topEnd, DonatiRadiusCm, out Point2d c4))
                            PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c4, TevziNo_4Fillet);
                    }
                }
            }

            // Baz 4: 1. donatı kırım tevzisi. Temelde bu kırım yok.
            if (!temelAktif)
            {
                double enterCm = 75.0 * phiCm;
                double yBar = z.AltYBot + pas;
                if (TryBasamakDirs(z, out double bx, out double by, out double px, out double py))
                {
                    Point2d OffsetIn(Point2d p) => new Point2d(p.X + px * pas, p.Y + py * pas);
                    Point2d oA = OffsetIn(z.BasAltA);
                    Point2d oB = OffsetIn(z.BasAltB);
                    if (TryIntersectY(oA, oB, yBar, out Point2d kirim))
                    {
                        Point2d prev = new Point2d(kirim.X - Math.Max(10.0, enterCm), yBar);
                        Point2d end = new Point2d(kirim.X + bx * enterCm, kirim.Y + by * enterCm);
                        if (TryFilletArcCenter(prev, kirim, end, DonatiRadiusCm, out Point2d c1))
                            PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c1, TevziNo_1Fillet);
                    }
                }
            }

            // Baz 5: 11 ∩ 12 — temel varken 11 ve 12 yok
            if (!temelAktif && TryTevziBaz_11_12_OfsetKesisim(z, pas, phiCm, out Point2d c1112))
                PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c1112, TevziNo_11x12Ofset);

            // Baz 6: 5. donatı — bölge3 alt / basamak üst kırım radius merkezi
            {
                double enterCm = 75.0 * phiCm;
                double yBotBar = z.UstYBot + pas;
                if (TryBasamakDirs(z, out _, out _, out double px, out double py))
                {
                    Point2d OffsetFromUst(Point2d p) =>
                        new Point2d(p.X - px * pas, p.Y - py * pas);
                    Point2d uA = OffsetFromUst(z.BasUstA);
                    Point2d uB = OffsetFromUst(z.BasUstB);
                    if (TryIntersectY(uA, uB, yBotBar, out Point2d join))
                    {
                        double xVert = z.UstX1 - pas;
                        Point2d topEnd = new Point2d(xVert, yBotBar);
                        // Basamağa doğru (üst sahanlıktan uçuşa = uB→uA)
                        double ux = uA.X - uB.X;
                        double uy = uA.Y - uB.Y;
                        double ul = Math.Sqrt(ux * ux + uy * uy);
                        if (ul > Tol)
                        {
                            ux /= ul; uy /= ul;
                            Point2d aEnd = new Point2d(join.X + ux * enterCm, join.Y + uy * enterCm);
                            Point2d prev = join.X <= xVert + Tol ? topEnd : new Point2d(join.X + 10.0, yBotBar);
                            if (TryFilletArcCenter(prev, join, aEnd, DonatiRadiusCm, out Point2d c5))
                            {
                                PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c5, TevziNo_5Kirim);
                                // 6 → basamak alt, çizgiden (pas+1.5) içeri (dik)
                                double faceIn = pas + TevziBazOfsetCm;
                                Point2d oA = new Point2d(z.BasAltA.X + px * faceIn, z.BasAltA.Y + py * faceIn);
                                Point2d oB = new Point2d(z.BasAltB.X + px * faceIn, z.BasAltB.Y + py * faceIn);
                                if (TryProjectAlongDirToLine(c5, -px, -py, oA, oB, out Point2d c5a))
                                    PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c5a, TevziNo_5KirimAltIz);
                            }
                        }
                    }
                }
            }

            // Baz 7: 3 ∩ 5 — kesit içine 1.5 cm ofset kesişimi
            if (TryTevziBaz_3_5_OfsetKesisim(z, pas, out Point2d c35))
                PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c35, TevziNo_3x5Ofset);

            // Baz 8: 11. donatı — temel varken 11 yok (8 ve 27 tevzi de yok)
            if (!temelAktif)
            {
                double enterCm = 40.0 * phiCm;
                double yLo = z.AltYBot + pas;
                if (TryBasamakDirs(z, out double bx, out double by, out double px, out double py))
                {
                    Point2d OffsetFromUst(Point2d p) =>
                        new Point2d(p.X - px * pas, p.Y - py * pas);
                    Point2d uA = OffsetFromUst(z.BasUstA);
                    Point2d uB = OffsetFromUst(z.BasUstB);
                    if (TryIntersectY(uA, uB, yLo, out Point2d kirim))
                    {
                        double ux = z.BasUstB.X - z.BasUstA.X;
                        double uy = z.BasUstB.Y - z.BasUstA.Y;
                        double ul = Math.Sqrt(ux * ux + uy * uy);
                        if (ul < Tol) { ux = bx; uy = by; }
                        else { ux /= ul; uy /= ul; }
                        Point2d prev = new Point2d(kirim.X - enterCm, yLo);
                        Point2d end = new Point2d(kirim.X + ux * enterCm, kirim.Y + uy * enterCm);
                        if (TryFilletArcCenter(prev, kirim, end, DonatiRadiusCm, out Point2d c11))
                        {
                            PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c11, TevziNo_11Kirim);
                            double yUstIz = z.AltYTop - pas - TevziBazOfsetCm;
                            PlaceTevziBaz(tr, ms, ox, oy, fx, styleId,
                                new Point2d(c11.X, yUstIz), TevziNo_11KirimUstIz);
                        }
                    }
                }
            }

            // 20–23: 1/2/3/5 finiş gönye. Temelde 1 ve 2 sol gönyesi yok (20, 21).
            PlaceFlightFinisGonyeTevzi(
                tr, ms, ox, oy, fx, z, pas, styleId,
                altMesnetVar, ustMesnetVar, include123: true, skipSolGonye: temelAktif);
        }

        /// <summary>
        /// 1/2 (sol) ve 3/5 (sağ) finiş gönye 90° fillet merkezleri.
        /// Kolon/kiriş/perde: X = kesit içine bakan yüz ± (1.5+pas).
        /// </summary>
        private static void PlaceFlightFinisGonyeTevzi(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            FlightKesitZones z, double pas, ObjectId styleId,
            bool altMesnetVar, bool ustMesnetVar, bool include123, bool skipSolGonye = false)
        {
            double r = DonatiRadiusCm;
            double faceIn = TevziBazOfsetCm + pas; // 1.5 + pas

            if (include123 && !skipSolGonye)
            {
                // 1 — sol alt gönye yukarı: merkez = köşe + (R,R)
                {
                    double yBar = z.AltYBot + pas;
                    double xBar = z.AltX0 + pas;
                    Point2d c = new Point2d(xBar + r, yBar + r);
                    if (altMesnetVar)
                        c = new Point2d(z.AltX0 + faceIn, c.Y);
                    PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c, TevziNo_1Gonye);
                }

                // 2 — sol üst gönye aşağı: merkez = köşe + (R,−R)
                {
                    double yBar = z.AltYTop - pas;
                    double xBar = z.AltX0 + pas;
                    Point2d c = new Point2d(xBar + r, yBar - r);
                    if (altMesnetVar)
                        c = new Point2d(z.AltX0 + faceIn, c.Y);
                    PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c, TevziNo_2Gonye);
                }
            }

            if (include123)
            {
                // 3 — sağ üst gönye aşağı: merkez = köşe + (−R,−R)
                {
                    double yTopBar = z.UstYTop - pas;
                    double xBar = z.UstX1 - pas;
                    Point2d c = new Point2d(xBar - r, yTopBar - r);
                    if (ustMesnetVar)
                        c = new Point2d(z.UstX1 - faceIn, c.Y);
                    PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c, TevziNo_3Gonye);
                }
            }

            // 5 — sağ alt gönye yukarı: merkez = köşe + (−R,+R)
            {
                double yBotBar = z.UstYBot + pas;
                double xBar = z.UstX1 - pas;
                Point2d c = new Point2d(xBar - r, yBotBar + r);
                if (ustMesnetVar)
                    c = new Point2d(z.UstX1 - faceIn, c.Y);
                PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c, TevziNo_5Gonye);
            }
        }

        /// <summary>
        /// Diğer basamaklı kesit tevzi bazları (8–10, 5 kırımları):
        /// 1=9 bölge1alt∩basamaküst; 2=10 bölge1üst∩basamakalt;
        /// 3=8 bölge1alt∩basamakalt; 4=8 basamakalt∩bölge3üst;
        /// 5=9 üst kırım; 6=5 kırım; 7=5∩8 ofset; 8=9∩10 ofset.
        /// </summary>
        private static void DrawFlightTevziBaz_DigerKol(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            FlightKesitZones z, double pas, double phiCm, ObjectId styleId,
            bool altMesnetVar, bool ustMesnetVar)
        {
            _tevziMap = new Dictionary<int, Point2d>();
            try
            {
                DrawFlightTevziBaz_DigerKolCore(
                    tr, ms, ox, oy, fx, z, pas, phiCm, styleId,
                    altMesnetVar, ustMesnetVar);
                // Ara tevzi (isimsiz): 25-12, 23-31, 6-32, 29-10, 33-26, 9-24
                FillTevziPairs(tr, ms, ox, oy, fx,
                    TevziNo_8GonyeSag, TevziNo_8KirimUst,
                    TevziNo_5Gonye, TevziNo_8KirimUstBotIz,
                    TevziNo_5Kirim, TevziNo_10KirimUstIz,
                    TevziNo_5KirimAltIz, TevziNo_10Kirim,
                    TevziNo_9KirimAltUstIz, TevziNo_10Gonye,
                    TevziNo_9KirimAlt, TevziNo_8GonyeSol);
                // Basamak tevzi adedi: 10…29 (dahil) + ara kopyalar
                _basamakTevziAdet = CountTevziPairInclusive(TevziNo_10Kirim, TevziNo_5KirimAltIz);
            }
            finally { _tevziMap = null; }
        }

        private static void DrawFlightTevziBaz_DigerKolCore(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            FlightKesitZones z, double pas, double phiCm, ObjectId styleId,
            bool altMesnetVar, bool ustMesnetVar)
        {
            if (!TryBasamakDirs(z, out double bx, out double by, out double px, out double py))
                return;

            Point2d OffsetIn(Point2d p) => new Point2d(p.X + px * pas, p.Y + py * pas);
            Point2d OffsetFromUst(Point2d p) => new Point2d(p.X - px * pas, p.Y - py * pas);
            Point2d oA = OffsetIn(z.BasAltA);
            Point2d oB = OffsetIn(z.BasAltB);
            Point2d uA = OffsetFromUst(z.BasUstA);
            Point2d uB = OffsetFromUst(z.BasUstB);

            double yLo = z.AltYBot + pas;
            double yHiLand = z.AltYTop - pas;
            double yTop = z.UstYTop - pas;
            double yBotUst = z.UstYBot + pas;
            double xR = z.UstX1 - pas;
            double enter75 = 75.0 * phiCm;

            double ux = z.BasUstB.X - z.BasUstA.X;
            double uy = z.BasUstB.Y - z.BasUstA.Y;
            double ul = Math.Sqrt(ux * ux + uy * uy);
            if (ul < Tol) { ux = bx; uy = by; }
            else { ux /= ul; uy /= ul; }

            double faceIn = pas + TevziBazOfsetCm;

            // 1: 9. donatı — bölge1 alt ∩ basamak üst kırım
            // + izdüşüm → bölge1 üst − (pas+1.5)
            if (TryIntersectY(uA, uB, yLo, out Point2d lo9))
            {
                Point2d prev = new Point2d(lo9.X - 20.0, yLo);
                Point2d next = new Point2d(lo9.X + ux * 20.0, lo9.Y + uy * 20.0);
                if (TryFilletArcCenter(prev, lo9, next, DonatiRadiusCm, out Point2d c))
                {
                    PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c, TevziNo_9KirimAlt);
                    PlaceTevziBaz(tr, ms, ox, oy, fx, styleId,
                        new Point2d(c.X, z.AltYTop - faceIn), TevziNo_9KirimAltUstIz);
                }
            }

            // 2: 10. donatı — bölge1 üst ∩ basamak alt kırım
            // + izdüşüm → basamak üst (dik, pas+1.5 içeri)
            if (TryIntersectY(oA, oB, yHiLand, out Point2d k10))
            {
                Point2d prev = new Point2d(k10.X - enter75, yHiLand);
                Point2d next = new Point2d(k10.X + bx * enter75, k10.Y + by * enter75);
                if (TryFilletArcCenter(prev, k10, next, DonatiRadiusCm, out Point2d c))
                {
                    PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c, TevziNo_10Kirim);
                    Point2d uInA = new Point2d(z.BasUstA.X - px * faceIn, z.BasUstA.Y - py * faceIn);
                    Point2d uInB = new Point2d(z.BasUstB.X - px * faceIn, z.BasUstB.Y - py * faceIn);
                    if (TryProjectAlongDirToLine(c, px, py, uInA, uInB, out Point2d cUst))
                        PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, cUst, TevziNo_10KirimUstIz);
                }
            }

            // 3: 8. donatı — bölge1 alt ∩ basamak alt kırım
            if (TryIntersectY(oA, oB, yLo, out Point2d lo8))
            {
                Point2d prev = new Point2d(lo8.X - 20.0, yLo);
                Point2d next = new Point2d(lo8.X + bx * 20.0, lo8.Y + by * 20.0);
                if (TryFilletArcCenter(prev, lo8, next, DonatiRadiusCm, out Point2d c))
                    PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c, TevziNo_8KirimAlt);
            }

            // 4: 8. donatı — basamak alt ∩ bölge3 üst kırım
            // + izdüşüm → bölge3 alt +(pas+1.5)
            if (TryIntersectY(oA, oB, yTop, out Point2d hi8))
            {
                Point2d prev = new Point2d(hi8.X - bx * 20.0, hi8.Y - by * 20.0);
                Point2d topEnd = hi8.X <= xR + Tol
                    ? new Point2d(xR, yTop)
                    : new Point2d(Math.Max(hi8.X, xR), yTop);
                if (TryFilletArcCenter(prev, hi8, topEnd, DonatiRadiusCm, out Point2d c))
                {
                    PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c, TevziNo_8KirimUst);
                    PlaceTevziBaz(tr, ms, ox, oy, fx, styleId,
                        new Point2d(c.X, z.UstYBot + faceIn), TevziNo_8KirimUstBotIz);
                }
            }

            // 5: 9. donatı — basamak üst ∩ bölge3 üst kırım
            if (TryIntersectY(uA, uB, yTop, out Point2d hi9))
            {
                Point2d prev = new Point2d(hi9.X - ux * 20.0, hi9.Y - uy * 20.0);
                Point2d topEnd = hi9.X <= xR + Tol
                    ? new Point2d(xR, yTop)
                    : new Point2d(Math.Max(hi9.X, xR), yTop);
                if (TryFilletArcCenter(prev, hi9, topEnd, DonatiRadiusCm, out Point2d c))
                    PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c, TevziNo_9KirimUst);
            }

            // 6: 5. donatı — basamak üst ∩ bölge3 alt kırım
            if (TryIntersectY(uA, uB, yBotUst, out Point2d j5))
            {
                Point2d topEnd = new Point2d(xR, yBotUst);
                double vx = uA.X - uB.X;
                double vy = uA.Y - uB.Y;
                double vl = Math.Sqrt(vx * vx + vy * vy);
                if (vl > Tol)
                {
                    vx /= vl; vy /= vl;
                    Point2d aEnd = new Point2d(j5.X + vx * enter75, j5.Y + vy * enter75);
                    Point2d prev = j5.X <= xR + Tol ? topEnd : new Point2d(j5.X + 10.0, yBotUst);
                    if (TryFilletArcCenter(prev, j5, aEnd, DonatiRadiusCm, out Point2d c))
                    {
                        PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c, TevziNo_5Kirim);
                        Point2d oAltA = new Point2d(z.BasAltA.X + px * faceIn, z.BasAltA.Y + py * faceIn);
                        Point2d oAltB = new Point2d(z.BasAltB.X + px * faceIn, z.BasAltB.Y + py * faceIn);
                        if (TryProjectAlongDirToLine(c, -px, -py, oAltA, oAltB, out Point2d cAlt))
                            PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, cAlt, TevziNo_5KirimAltIz);
                    }
                }
            }

            // 7: 5 ∩ 8 — BasAlt (8) ∩ bölge3 alt yatay (5), 1.5 cm iç ofset
            if (TryTevziBaz_5_8_OfsetKesisim(z, pas, out Point2d c58))
                PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c58, TevziNo_5x8Ofset);

            if (TryTevziBaz_9_10_OfsetKesisim(z, pas, out Point2d c910))
                PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c910, TevziNo_9x10Ofset);

            // 23: 5 finiş gönye; 24–25: 8 iki uç; 26: 10 gönye
            PlaceFlightFinisGonyeTevzi(
                tr, ms, ox, oy, fx, z, pas, styleId,
                altMesnetVar, ustMesnetVar, include123: false);
            PlaceFlightFinisGonyeTevzi_8_10(
                tr, ms, ox, oy, fx, z, pas, styleId,
                altMesnetVar, ustMesnetVar);
        }

        /// <summary>
        /// 8 sol/sağ finiş gönye + 10 sol gönye (diğer basamaklı kol).
        /// Mesnette X = yüz ± (1.5+pas).
        /// </summary>
        private static void PlaceFlightFinisGonyeTevzi_8_10(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            FlightKesitZones z, double pas, ObjectId styleId,
            bool altMesnetVar, bool ustMesnetVar)
        {
            double r = DonatiRadiusCm;
            double faceIn = TevziBazOfsetCm + pas;

            // 8 sol — alt gönye yukarı (1 ile aynı)
            {
                double yBar = z.AltYBot + pas;
                double xBar = z.AltX0 + pas;
                Point2d c = new Point2d(xBar + r, yBar + r);
                if (altMesnetVar)
                    c = new Point2d(z.AltX0 + faceIn, c.Y);
                PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c, TevziNo_8GonyeSol);
            }

            // 8 sağ — üst gönye aşağı (3 ile aynı)
            {
                double yTopBar = z.UstYTop - pas;
                double xBar = z.UstX1 - pas;
                Point2d c = new Point2d(xBar - r, yTopBar - r);
                if (ustMesnetVar)
                    c = new Point2d(z.UstX1 - faceIn, c.Y);
                PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c, TevziNo_8GonyeSag);
            }

            // 10 — sol üst gönye aşağı (2 ile aynı)
            {
                double yBar = z.AltYTop - pas;
                double xBar = z.AltX0 + pas;
                Point2d c = new Point2d(xBar + r, yBar - r);
                if (altMesnetVar)
                    c = new Point2d(z.AltX0 + faceIn, c.Y);
                PlaceTevziBaz(tr, ms, ox, oy, fx, styleId, c, TevziNo_10Gonye);
            }
        }

        /// <summary>
        /// 5 (bölge3 alt yatay) ∩ 8 (basamak alt eğik) — 3∩5 ile aynı geometri.
        /// </summary>
        private static bool TryTevziBaz_5_8_OfsetKesisim(
            FlightKesitZones z, double pas, out Point2d center)
        {
            return TryTevziBaz_3_5_OfsetKesisim(z, pas, out center);
        }

        /// <summary>
        /// 9 (BasUst sürekli) ∩ 10 (bölge1 üst yatay + BasAlt): 11∩12 ofset mantığı;
        /// üst kesişimde yatay aşağı (yHi − off), alt dal yLo + off.
        /// </summary>
        private static bool TryTevziBaz_9_10_OfsetKesisim(
            FlightKesitZones z, double pas, out Point2d center)
        {
            center = new Point2d(0, 0);
            if (!TryBasamakDirs(z, out _, out _, out double px, out double py))
                return false;

            double yLo = z.AltYBot + pas;
            double yHi = z.AltYTop - pas;
            double off = TevziBazOfsetCm;

            Point2d OffsetIn(Point2d p) => new Point2d(p.X + px * pas, p.Y + py * pas);
            Point2d OffsetFromUst(Point2d p) => new Point2d(p.X - px * pas, p.Y - py * pas);

            Point2d oA = OffsetIn(z.BasAltA);
            Point2d oB = OffsetIn(z.BasAltB);
            Point2d uA = OffsetFromUst(z.BasUstA);
            Point2d uB = OffsetFromUst(z.BasUstB);

            // 9 eğik (BasUst) ∩ 10 yatay — BasUst içeri (−n), yatay aşağı
            if (TryIntersectY(uA, uB, yHi, out _))
            {
                Point2d ouA = new Point2d(uA.X - px * off, uA.Y - py * off);
                Point2d ouB = new Point2d(uB.X - px * off, uB.Y - py * off);
                if (TryIntersectY(ouA, ouB, yHi - off, out center))
                    return true;
            }

            // 10 eğik (BasAlt) ∩ 9 yatay alt — BasAlt içeri (+n), yatay yukarı
            if (TryIntersectY(oA, oB, yLo, out _))
            {
                Point2d ooA = new Point2d(oA.X + px * off, oA.Y + py * off);
                Point2d ooB = new Point2d(oB.X + px * off, oB.Y + py * off);
                if (TryIntersectY(ooA, ooB, yLo + off, out center))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 11 (bölge1 alt + basamak üst) ile 12 (bölge1 üst + basamak alt) kesişiminde,
        /// her iki hattan kesit içine TevziBazOfsetCm ofsetli hatların kesişim noktası.
        /// </summary>
        private static bool TryTevziBaz_11_12_OfsetKesisim(
            FlightKesitZones z, double pas, double phiCm, out Point2d center)
        {
            center = new Point2d(0, 0);
            if (!TryBasamakDirs(z, out double bx, out _, out double px, out double py))
                return false;

            double enterCm = 40.0 * phiCm;
            double yLo = z.AltYBot + pas;   // 11 yatay
            double yHi = z.AltYTop - pas;   // 12 yatay
            double off = TevziBazOfsetCm;

            Point2d OffsetIn(Point2d p) => new Point2d(p.X + px * pas, p.Y + py * pas);
            Point2d OffsetFromUst(Point2d p) => new Point2d(p.X - px * pas, p.Y - py * pas);

            Point2d oA = OffsetIn(z.BasAltA);
            Point2d oB = OffsetIn(z.BasAltB);
            Point2d uA = OffsetFromUst(z.BasUstA);
            Point2d uB = OffsetFromUst(z.BasUstB);

            if (!TryIntersectY(uA, uB, yLo, out Point2d kirim11)) return false;
            if (!TryIntersectY(oA, oB, yHi, out Point2d kirim12)) return false;

            // 11 eğik (BasUst) ∩ 12 yatay — ofset: BasUst içeri (−n), yatay aşağı
            if (TryIntersectY(uA, uB, yHi, out Point2d pCrossA))
            {
                bool on11 = pCrossA.X >= Math.Min(kirim11.X, kirim11.X + bx * enterCm) - Tol
                    && pCrossA.X <= Math.Max(kirim11.X, kirim11.X + bx * enterCm * 2) + Tol;
                bool on12 = pCrossA.X >= kirim12.X - enterCm - Tol && pCrossA.X <= kirim12.X + Tol;
                if (on11 || on12)
                {
                    Point2d ouA = new Point2d(uA.X - px * off, uA.Y - py * off);
                    Point2d ouB = new Point2d(uB.X - px * off, uB.Y - py * off);
                    if (TryIntersectY(ouA, ouB, yHi - off, out center))
                        return true;
                }
            }

            // 12 eğik (BasAlt) ∩ 11 yatay — ofset: BasAlt içeri (+n), yatay yukarı
            if (TryIntersectY(oA, oB, yLo, out _))
            {
                Point2d ooA = new Point2d(oA.X + px * off, oA.Y + py * off);
                Point2d ooB = new Point2d(oB.X + px * off, oB.Y + py * off);
                if (TryIntersectY(ooA, ooB, yLo + off, out center))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 3 (basamak alt eğik) ∩ 5 (bölge3 alt yatay) kesişimi;
        /// her iki hattan kesit içine TevziBazOfsetCm ofset. (BasUst∩bölge3 üst = baz3; kullanılmaz.)
        /// </summary>
        private static bool TryTevziBaz_3_5_OfsetKesisim(
            FlightKesitZones z, double pas, out Point2d center)
        {
            center = new Point2d(0, 0);
            if (!TryBasamakDirs(z, out _, out _, out double px, out double py))
                return false;

            double yBot = z.UstYBot + pas; // 5. donatı yatay
            double off = TevziBazOfsetCm;

            Point2d OffsetIn(Point2d p) => new Point2d(p.X + px * pas, p.Y + py * pas);
            Point2d oA = OffsetIn(z.BasAltA); // 3. donatı eğik (basamak alt)
            Point2d oB = OffsetIn(z.BasAltB);

            // Ham kesişim: 3 eğik ∩ 5 yatay
            if (!TryIntersectY(oA, oB, yBot, out _))
                return false;

            // Ofset: BasAlt içeri (+n), 5 yatay içeri (yukarı)
            Point2d ooA = new Point2d(oA.X + px * off, oA.Y + py * off);
            Point2d ooB = new Point2d(oB.X + px * off, oB.Y + py * off);
            return TryIntersectY(ooA, ooB, yBot + off, out center);
        }

        private static void PlaceTevziBaz(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx, ObjectId styleId,
            Point2d centerLocal, int tevziNo)
        {
            DrawTevziCircleAt(tr, ms, ox, oy, fx, centerLocal);
            if (_tevziMap != null)
                _tevziMap[tevziNo] = centerLocal;
        }

        /// <summary>İsimlendirilmiş tevzi merkezleri (ara doldurma için).</summary>
        private static Dictionary<int, Point2d> _tevziMap;

        /// <summary>
        /// İki numaralı tevzi arasını TevziAralikCm aşmayacak eşit aralıklarla
        /// isimsiz çemberlerle doldurur (uçlar dahil değil).
        /// </summary>
        private static void FillTevziPairs(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            params int[] pairNos)
        {
            if (_tevziMap == null || pairNos == null || pairNos.Length < 2) return;
            MerdivenSettings.EnsureLoaded();
            double aralik = MerdivenSettings.TevziAralikCm;
            if (aralik < 5) aralik = 5;

            for (int i = 0; i + 1 < pairNos.Length; i += 2)
            {
                int na = pairNos[i], nb = pairNos[i + 1];
                if (!_tevziMap.TryGetValue(na, out Point2d a)) continue;
                if (!_tevziMap.TryGetValue(nb, out Point2d b)) continue;
                double dist = a.GetDistanceTo(b);
                if (dist < Tol) continue;
                int nGaps = (int)Math.Ceiling(dist / aralik);
                if (nGaps < 2) continue;
                for (int k = 1; k < nGaps; k++)
                {
                    double t = k / (double)nGaps;
                    var p = new Point2d(
                        a.X + (b.X - a.X) * t,
                        a.Y + (b.Y - a.Y) * t);
                    DrawTevziCircleAt(tr, ms, ox, oy, fx, p);
                }
            }
        }

        /// <summary>
        /// İki tevzi no arası toplam adet (uçlar + ara kopyalar) —
        /// FillTevziPairs ile aynı aralık mantığı.
        /// </summary>
        private static int CountTevziPairInclusive(int na, int nb)
        {
            if (_tevziMap == null) return 0;
            if (!_tevziMap.TryGetValue(na, out Point2d a)) return 0;
            if (!_tevziMap.TryGetValue(nb, out Point2d b)) return 0;
            MerdivenSettings.EnsureLoaded();
            double aralik = MerdivenSettings.TevziAralikCm;
            if (aralik < 5) aralik = 5;
            double dist = a.GetDistanceTo(b);
            if (dist < Tol) return 1;
            int nGaps = (int)Math.Ceiling(dist / aralik);
            if (nGaps < 1) nGaps = 1;
            return nGaps + 1;
        }

        /// <summary>
        /// Tevzi kesit çemberi: çap = TevziCircleDiaCm (1.5), global width = 3;
        /// 2 vertex + 2 yay (bulge=1), kapalı.
        /// </summary>
        private static void DrawTevziCircleAt(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            Point2d centerLocal)
        {
            double rr = TevziCircleDiaCm * 0.5;
            double cx = fx(centerLocal.X);
            double cy = centerLocal.Y;
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(ox + (cx - rr) * Scale, oy + cy * Scale), 1.0, 0, 0);
            pl.AddVertexAt(1, new Point2d(ox + (cx + rr) * Scale, oy + cy * Scale), 1.0, 0, 0);
            pl.Closed = true;
            pl.ConstantWidth = TevziCircleWidthCm * Scale;
            pl.Layer = LayerDonati;
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        /// <summary>
        /// Noktadan (dx,dy) doğrultusunda sonsuz ışın × doğru (a–b) kesişimi.
        /// </summary>
        private static bool TryProjectAlongDirToLine(
            Point2d p, double dx, double dy, Point2d a, Point2d b, out Point2d hit)
        {
            hit = p;
            double ex = b.X - a.X, ey = b.Y - a.Y;
            double den = dx * ey - dy * ex;
            if (Math.Abs(den) < 1e-12) return false;
            double ox = a.X - p.X, oy = a.Y - p.Y;
            double t = (ox * ey - oy * ex) / den;
            hit = new Point2d(p.X + dx * t, p.Y + dy * t);
            return true;
        }

        /// <summary>
        /// FilletDonatiPath ile aynı dışbükey yay merkezi (köşe karşı tarafında).
        /// </summary>
        private static bool TryFilletArcCenter(
            Point2d prev, Point2d corner, Point2d next, double radius, out Point2d center)
        {
            center = corner;
            Vector2d vIn = corner - prev;
            Vector2d vOut = next - corner;
            double lenIn = vIn.Length;
            double lenOut = vOut.Length;
            if (lenIn < Tol || lenOut < Tol) return false;
            vIn = vIn.GetNormal();
            vOut = vOut.GetNormal();
            double dot = vIn.X * vOut.X + vIn.Y * vOut.Y;
            if (dot > 1) dot = 1;
            if (dot < -1) dot = -1;
            double ang = Math.Acos(dot);
            if (ang < 1e-4 || Math.PI - ang < 1e-4) return false;

            double r = radius;
            double cut = r * Math.Tan(ang * 0.5);
            double maxCut = Math.Min(lenIn, lenOut) * 0.45;
            if (cut > maxCut && maxCut > Tol)
            {
                cut = maxCut;
                r = cut / Math.Tan(ang * 0.5);
            }
            if (cut < Tol || r < Tol) return false;

            Point2d t1 = new Point2d(corner.X - vIn.X * cut, corner.Y - vIn.Y * cut);
            Point2d t2 = new Point2d(corner.X + vOut.X * cut, corner.Y + vOut.Y * cut);
            Vector2d chord = t2 - t1;
            double chordLen = chord.Length;
            if (chordLen < Tol) return false;
            double cornerCross = chord.X * (corner.Y - t1.Y) - chord.Y * (corner.X - t1.X);
            // Dışbükey: merkez köşenin karşı tarafında
            Vector2d perp = new Vector2d(-chord.Y / chordLen, chord.X / chordLen);
            if (cornerCross > 0) perp = new Vector2d(-perp.X, -perp.Y);

            double half = chordLen * 0.5;
            double d = Math.Sqrt(Math.Max(0.0, r * r - half * half));
            Point2d mid = new Point2d((t1.X + t2.X) * 0.5, (t1.Y + t2.Y) * 0.5);
            center = new Point2d(mid.X + perp.X * d, mid.Y + perp.Y * d);
            return true;
        }

        /// <summary>
        /// 1. donatı (yalnız ilk kol): sol uç — perde/kolon/kiriş varsa dış yüze paspayı + gönye yukarı;
        /// yoksa merdiven yüzüne paspayı + gönye. Bölge1 alt → basamak alt 75φ. R=1.5; açılım 120 cm.
        /// </summary>
        private static void DrawFlightDonati_AltSahanlikAlt(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            FlightKesitZones z, double pas, double phiCm,
            bool altMesnetVar, double altMesnetOuterX, ObjectId styleId,
            bool temelAktif = false, double flightStartX = 0, double temelKalCm = 50)
        {
            double enterCm = 75.0 * phiCm;
            double yBar = z.AltYBot + pas;
            double gonyeH = Math.Max(z.DosemeCm - 2.0 * pas, pas);
            double xVert = altMesnetVar
                ? altMesnetOuterX + pas
                : z.AltX0 + pas;

            if (!TryBasamakDirs(z, out double bx, out double by, out double px, out double py))
                return;

            Point2d OffsetIn(Point2d p) => new Point2d(p.X + px * pas, p.Y + py * pas);
            Point2d oA = OffsetIn(z.BasAltA);
            Point2d oB = OffsetIn(z.BasAltB);
            if (!TryIntersectY(oA, oB, yBar, out Point2d kirim)) return;
            Point2d aEnd = new Point2d(kirim.X + bx * enterCm, kirim.Y + by * enterCm);

            List<Point2d> sharp;
            if (temelAktif)
            {
                // Temel: dikey kol ilk rıht + pas. 75φ basamak üst çizgisinde.
                double xLeg = flightStartX + pas;
                double yFund = TemelDonatiY(z, temelKalCm);
                double g12 = Math.Max(12.0 * phiCm, pas);
                Point2d uA = new Point2d(z.BasUstA.X - px * pas, z.BasUstA.Y - py * pas);
                Point2d uB = new Point2d(z.BasUstB.X - px * pas, z.BasUstB.Y - py * pas);
                double ux = uB.X - uA.X;
                double uy = uB.Y - uA.Y;
                double ul = Math.Sqrt(ux * ux + uy * uy);
                if (ul < Tol) { ux = bx; uy = by; }
                else { ux /= ul; uy /= ul; }
                if (!TryIntersectX(uA, uB, xLeg, out Point2d topLeg))
                    topLeg = new Point2d(xLeg, z.AltYTop - pas);
                Point2d slopeEnd = new Point2d(topLeg.X + ux * enterCm, topLeg.Y + uy * enterCm);
                sharp = new List<Point2d>
                {
                    new Point2d(xLeg + g12, yFund),
                    new Point2d(xLeg, yFund),
                    topLeg,
                    slopeEnd
                };
            }
            else
            {
                sharp = new List<Point2d>
                {
                    new Point2d(xVert, yBar + gonyeH),
                    new Point2d(xVert, yBar),
                    kirim,
                    aEnd
                };
            }
            DrawDonatiPathWithAcilim(tr, ms, ox, oy, fx, sharp, z.AltYBot, yBar, acilimSlot: 0, donatiNo: 1, styleId,
                true, z, pas, z.UstYTop,
                acilimAsagiCm: temelAktif ? _temelBar1AsagiCm : (double?)null,
                acilimDisaCm: temelAktif ? 70.0 : 0);
        }

        /// <summary>
        /// 2. donatı (yalnız ilk kol): bölge1 üst → basamak sanal üst 75φ;
        /// mesnet ucu aşağı gönye — kiriş/perde/kolon varsa 12φ, yoksa doseme−2pas.
        /// </summary>
        private static void DrawFlightDonati_AltSahanlikUst(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            FlightKesitZones z, double pas, double phiCm,
            bool altMesnetVar, double altMesnetOuterX, ObjectId styleId,
            bool temelAktif = false, double flightStartX = 0, double basamakCm = 0, double temelKalCm = 50)
        {
            double enterCm = 75.0 * phiCm;
            double yBar = z.AltYTop - pas;
            double gonyeH = altMesnetVar
                ? Math.Max(12.0 * phiCm, pas)
                : Math.Max(z.DosemeCm - 2.0 * pas, pas);
            double xVert = altMesnetVar
                ? altMesnetOuterX + pas
                : z.AltX0 + pas;

            if (!TryBasamakDirs(z, out double bx, out double by, out double px, out double py))
                return;

            Point2d OffsetInFromUst(Point2d p) =>
                new Point2d(p.X - px * pas, p.Y - py * pas);
            Point2d uA = OffsetInFromUst(z.BasUstA);
            Point2d uB = OffsetInFromUst(z.BasUstB);
            if (!TryIntersectY(uA, uB, yBar, out Point2d kirim)) return;

            double ux = z.BasUstB.X - z.BasUstA.X;
            double uy = z.BasUstB.Y - z.BasUstA.Y;
            double ul = Math.Sqrt(ux * ux + uy * uy);
            if (ul < Tol) { ux = bx; uy = by; }
            else { ux /= ul; uy /= ul; }

            Point2d aEnd = new Point2d(kirim.X + ux * enterCm, kirim.Y + uy * enterCm);

            List<Point2d> sharp;
            if (temelAktif)
            {
                // Temel: dikey kol ikinci rıht − pas. 75φ basamak alt çizgisinde.
                double xLeg = flightStartX + Math.Max(basamakCm, pas) - pas;
                double yFund = TemelDonatiY(z, temelKalCm);
                double g12 = Math.Max(12.0 * phiCm, pas);
                Point2d oA = new Point2d(z.BasAltA.X + px * pas, z.BasAltA.Y + py * pas);
                Point2d oB = new Point2d(z.BasAltB.X + px * pas, z.BasAltB.Y + py * pas);
                if (!TryIntersectX(oA, oB, xLeg, out Point2d botLeg))
                    botLeg = new Point2d(xLeg, z.AltYBot + pas);
                Point2d slopeEnd = new Point2d(botLeg.X + bx * enterCm, botLeg.Y + by * enterCm);
                sharp = new List<Point2d>
                {
                    new Point2d(xLeg - g12, yFund),
                    new Point2d(xLeg, yFund),
                    botLeg,
                    slopeEnd
                };
            }
            else
            {
                sharp = new List<Point2d>
                {
                    new Point2d(xVert, yBar - gonyeH),
                    new Point2d(xVert, yBar),
                    kirim,
                    aEnd
                };
            }
            DrawDonatiPathWithAcilim(tr, ms, ox, oy, fx, sharp, z.AltYBot, yBar, acilimSlot: 1, donatiNo: 2, styleId,
                true, z, pas, z.UstYTop,
                acilimAsagiCm: temelAktif ? _temelBar2AsagiCm : (double?)null,
                acilimDisaCm: temelAktif ? 50.0 : 0);
        }

        /// <summary>
        /// 3. donatı (yalnız ilk kol): basamak alt (+pas) uzatılır, bölge3 üst (−pas) ile
        /// tek noktada birleşir → sağ sınır + aşağı gönye.
        /// </summary>
        private static void DrawFlightDonati_BasamakToUstSahanlik(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            FlightKesitZones z, double pas, double phiCm,
            bool ustMesnetVar, double ustMesnetOuterX, ObjectId styleId,
            bool temelAktif = false)
        {
            double yTopBar = z.UstYTop - pas;
            double gonyeH = ustMesnetVar
                ? Math.Max(12.0 * phiCm, pas)
                : Math.Max(z.DosemeCm - 2.0 * pas, pas);
            double xVert = ustMesnetVar
                ? ustMesnetOuterX - pas
                : z.UstX1 - pas;

            if (!TryBasamakDirs(z, out _, out _, out double px, out double py))
                return;

            Point2d OffsetIn(Point2d p) => new Point2d(p.X + px * pas, p.Y + py * pas);
            Point2d oA = OffsetIn(z.BasAltA);
            Point2d oB = OffsetIn(z.BasAltB);

            double yLoBar = z.AltYBot + pas;
            if (!TryIntersectY(oA, oB, yLoBar, out Point2d start))
                start = oA;

            if (!TryIntersectY(oA, oB, yTopBar, out Point2d join))
                return;

            Point2d topEnd = new Point2d(xVert, yTopBar);
            Point2d tip = new Point2d(xVert, yTopBar - gonyeH);

            List<Point2d> sharp;
            if (join.X <= xVert + Tol)
                sharp = new List<Point2d> { start, join, topEnd, tip };
            else
                sharp = new List<Point2d> { start, topEnd, tip };

            if (temelAktif)
                sharp = TrimSharpAboveY(sharp, z.AltYTop);
            if (sharp == null || sharp.Count < 2) return;

            DrawDonatiPathWithAcilim(tr, ms, ox, oy, fx, sharp, z.AltYBot, yTopBar, acilimSlot: 2, donatiNo: 3, styleId,
                true, z, pas, z.UstYTop);
        }

        /// <summary>
        /// 4. donatı (yalnız ilk kol): basamak sanal üst (−pas) → bölge3 üst (−pas) tek nokta →
        /// sağ sınır + aşağı gönye (3. donatı ile aynı).
        /// </summary>
        private static void DrawFlightDonati_BasamakUstToUstSahanlik(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            FlightKesitZones z, double pas, double phiCm,
            bool ustMesnetVar, double ustMesnetOuterX, ObjectId styleId,
            bool temelAktif = false)
        {
            double yTopBar = z.UstYTop - pas;
            double gonyeH = ustMesnetVar
                ? Math.Max(12.0 * phiCm, pas)
                : Math.Max(z.DosemeCm - 2.0 * pas, pas);
            double xVert = ustMesnetVar
                ? ustMesnetOuterX - pas
                : z.UstX1 - pas;

            if (!TryBasamakDirs(z, out _, out _, out double px, out double py))
                return;

            Point2d OffsetFromUst(Point2d p) =>
                new Point2d(p.X - px * pas, p.Y - py * pas);
            Point2d uA = OffsetFromUst(z.BasUstA);
            Point2d uB = OffsetFromUst(z.BasUstB);

            double yLoBar = z.AltYTop - pas;
            if (!TryIntersectY(uA, uB, yLoBar, out Point2d start))
                start = uA;

            if (!TryIntersectY(uA, uB, yTopBar, out Point2d join))
                return;

            Point2d topEnd = new Point2d(xVert, yTopBar);
            Point2d tip = new Point2d(xVert, yTopBar - gonyeH);

            List<Point2d> sharp;
            if (join.X <= xVert + Tol)
                sharp = new List<Point2d> { start, join, topEnd, tip };
            else
                sharp = new List<Point2d> { start, topEnd, tip };

            if (temelAktif)
                sharp = TrimSharpAboveY(sharp, z.AltYTop);
            if (sharp == null || sharp.Count < 2) return;

            DrawDonatiPathWithAcilim(tr, ms, ox, oy, fx, sharp, z.AltYBot, yTopBar, acilimSlot: 3, donatiNo: 4, styleId,
                true, z, pas, z.UstYTop);
        }

        /// <summary>Temel üst kotunun (yCut) altında kalan köşeleri atar; kesişime yeni uç koyar.</summary>
        private static List<Point2d> TrimSharpAboveY(List<Point2d> pts, double yCut)
        {
            if (pts == null || pts.Count < 2) return pts;
            var result = new List<Point2d>();
            for (int i = 0; i < pts.Count; i++)
            {
                Point2d cur = pts[i];
                bool curIn = cur.Y >= yCut - Tol;
                if (i > 0)
                {
                    Point2d prev = pts[i - 1];
                    bool prevIn = prev.Y >= yCut - Tol;
                    if (prevIn != curIn && Math.Abs(cur.Y - prev.Y) > Tol)
                    {
                        double t = (yCut - prev.Y) / (cur.Y - prev.Y);
                        var hit = new Point2d(prev.X + (cur.X - prev.X) * t, yCut);
                        if (result.Count == 0 || result[result.Count - 1].GetDistanceTo(hit) > Tol)
                            result.Add(hit);
                    }
                }
                if (curIn && (result.Count == 0 || result[result.Count - 1].GetDistanceTo(cur) > Tol))
                    result.Add(cur);
            }
            return result;
        }

        /// <summary>
        /// 8. donatı — ilk basamaklı kol hariç diğer basamaklı kesitler:
        /// bölge1 alt → basamak alt → bölge3 üst;
        /// sol gönye yukarı (doseme−2pas); sağ gönye aşağı (12φ / doseme−2pas).
        /// </summary>
        private static void DrawFlightDonati_AltBasamakUstContinuous(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            FlightKesitZones z, double pas, double phiCm,
            bool altMesnetVar, double altMesnetOuterX,
            bool ustMesnetVar, double ustMesnetOuterX, ObjectId styleId)
        {
            double yLo = z.AltYBot + pas;
            double yTopBar = z.UstYTop - pas;
            double gonyeUp = Math.Max(z.DosemeCm - 2.0 * pas, pas);
            double gonyeDown = ustMesnetVar
                ? Math.Max(12.0 * phiCm, pas)
                : Math.Max(z.DosemeCm - 2.0 * pas, pas);
            double xL = altMesnetVar ? altMesnetOuterX + pas : z.AltX0 + pas;
            double xR = ustMesnetVar ? ustMesnetOuterX - pas : z.UstX1 - pas;

            if (!TryBasamakDirs(z, out _, out _, out double px, out double py))
                return;

            Point2d OffsetIn(Point2d p) => new Point2d(p.X + px * pas, p.Y + py * pas);
            Point2d oA = OffsetIn(z.BasAltA);
            Point2d oB = OffsetIn(z.BasAltB);

            if (!TryIntersectY(oA, oB, yLo, out Point2d loJoin))
                loJoin = oA;
            if (!TryIntersectY(oA, oB, yTopBar, out Point2d hiJoin))
                return;

            var sharp = new List<Point2d>
            {
                new Point2d(xL, yLo + gonyeUp),
                new Point2d(xL, yLo),
                loJoin,
                hiJoin
            };
            if (hiJoin.X <= xR + Tol)
            {
                sharp.Add(new Point2d(xR, yTopBar));
                sharp.Add(new Point2d(xR, yTopBar - gonyeDown));
            }
            else
            {
                sharp.Add(new Point2d(xR, yTopBar - gonyeDown));
            }

            DrawDonatiPathWithAcilim(
                tr, ms, ox, oy, fx, sharp, z.AltYBot, yLo, acilimSlot: 0, donatiNo: 8, styleId,
                true, z, pas, z.UstYTop);
        }

        /// <summary>
        /// 9. donatı — ilk basamaklı kol hariç diğer basamaklı kesitler:
        /// bölge1 alt → basamak üst → bölge3 üst; finiş gönyeleri 8 ile aynı.
        /// </summary>
        private static void DrawFlightDonati_AltBasamakUstTopContinuous(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            FlightKesitZones z, double pas, double phiCm,
            bool altMesnetVar, double altMesnetOuterX,
            bool ustMesnetVar, double ustMesnetOuterX, ObjectId styleId)
        {
            double yLo = z.AltYBot + pas;
            double yTopBar = z.UstYTop - pas;
            double gonyeUp = Math.Max(z.DosemeCm - 2.0 * pas, pas);
            double gonyeDown = ustMesnetVar
                ? Math.Max(12.0 * phiCm, pas)
                : Math.Max(z.DosemeCm - 2.0 * pas, pas);
            double xL = altMesnetVar ? altMesnetOuterX + pas : z.AltX0 + pas;
            double xR = ustMesnetVar ? ustMesnetOuterX - pas : z.UstX1 - pas;

            if (!TryBasamakDirs(z, out _, out _, out double px, out double py))
                return;

            Point2d OffsetFromUst(Point2d p) =>
                new Point2d(p.X - px * pas, p.Y - py * pas);
            Point2d uA = OffsetFromUst(z.BasUstA);
            Point2d uB = OffsetFromUst(z.BasUstB);

            if (!TryIntersectY(uA, uB, yLo, out Point2d loJoin))
                loJoin = uA;
            if (!TryIntersectY(uA, uB, yTopBar, out Point2d hiJoin))
                return;

            var sharp = new List<Point2d>
            {
                new Point2d(xL, yLo + gonyeUp),
                new Point2d(xL, yLo),
                loJoin,
                hiJoin
            };
            if (hiJoin.X <= xR + Tol)
            {
                sharp.Add(new Point2d(xR, yTopBar));
                sharp.Add(new Point2d(xR, yTopBar - gonyeDown));
            }
            else
            {
                sharp.Add(new Point2d(xR, yTopBar - gonyeDown));
            }

            DrawDonatiPathWithAcilim(
                tr, ms, ox, oy, fx, sharp, z.AltYBot, yLo, acilimSlot: 1, donatiNo: 9, styleId,
                true, z, pas, z.UstYTop);
        }

        /// <summary>
        /// 10. donatı — ilk basamaklı kol hariç diğer basamaklı kesitler:
        /// bölge1 üst → basamak alt + 75φ; mesnet ucu aşağı gönye (doseme−2pas / 12φ).
        /// </summary>
        private static void DrawFlightDonati_AltUstToBasamakAlt(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            FlightKesitZones z, double pas, double phiCm,
            bool altMesnetVar, double altMesnetOuterX, ObjectId styleId)
        {
            double enterCm = 75.0 * phiCm;
            double yBar = z.AltYTop - pas;
            double gonyeH = altMesnetVar
                ? Math.Max(12.0 * phiCm, pas)
                : Math.Max(z.DosemeCm - 2.0 * pas, pas);
            double xVert = altMesnetVar
                ? altMesnetOuterX + pas
                : z.AltX0 + pas;

            if (!TryBasamakDirs(z, out double bx, out double by, out double px, out double py))
                return;

            Point2d OffsetIn(Point2d p) => new Point2d(p.X + px * pas, p.Y + py * pas);
            Point2d oA = OffsetIn(z.BasAltA);
            Point2d oB = OffsetIn(z.BasAltB);
            if (!TryIntersectY(oA, oB, yBar, out Point2d kirim)) return;
            Point2d aEnd = new Point2d(kirim.X + bx * enterCm, kirim.Y + by * enterCm);

            var sharp = new List<Point2d>
            {
                new Point2d(xVert, yBar - gonyeH),
                new Point2d(xVert, yBar),
                kirim,
                aEnd
            };
            DrawDonatiPathWithAcilim(
                tr, ms, ox, oy, fx, sharp, z.AltYBot, yBar, acilimSlot: 2, donatiNo: 10, styleId,
                true, z, pas, z.UstYTop);
        }

        /// <summary>
        /// 5. donatı (tüm basamaklı kesitler): bölge3 alt (+pas) → basamak sanal üst (−pas) 75φ;
        /// sağ mesnet gönye yukarı (doseme−2pas).
        /// </summary>
        private static void DrawFlightDonati_UstSahanlikAltToBasamak(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            FlightKesitZones z, double pas, double phiCm,
            bool ustMesnetVar, double ustMesnetOuterX, ObjectId styleId,
            bool isFirstFlightKol = true)
        {
            double enterCm = 75.0 * phiCm;
            double yBotBar = z.UstYBot + pas;
            double gonyeH = Math.Max(z.DosemeCm - 2.0 * pas, pas);
            double xVert = ustMesnetVar
                ? ustMesnetOuterX - pas
                : z.UstX1 - pas;

            if (!TryBasamakDirs(z, out _, out _, out double px, out double py))
                return;

            Point2d OffsetFromUst(Point2d p) =>
                new Point2d(p.X - px * pas, p.Y - py * pas);
            Point2d uA = OffsetFromUst(z.BasUstA);
            Point2d uB = OffsetFromUst(z.BasUstB);

            // Tek birleşim: sanal üst ∩ bölge3 alt ofset
            if (!TryIntersectY(uA, uB, yBotBar, out Point2d join))
                return;

            // Basamağa 75φ (üst sahanlıktan uçuşa doğru = uB→uA)
            double ux = uA.X - uB.X;
            double uy = uA.Y - uB.Y;
            double ul = Math.Sqrt(ux * ux + uy * uy);
            if (ul < Tol) return;
            ux /= ul; uy /= ul;
            Point2d aEnd = new Point2d(join.X + ux * enterCm, join.Y + uy * enterCm);

            Point2d topEnd = new Point2d(xVert, yBotBar);
            Point2d tip = new Point2d(xVert, yBotBar + gonyeH);

            List<Point2d> sharp;
            if (join.X <= xVert + Tol)
                sharp = new List<Point2d> { tip, topEnd, join, aEnd };
            else
                sharp = new List<Point2d> { tip, topEnd, aEnd };

            DrawDonatiPathWithAcilim(
                tr, ms, ox, oy, fx, sharp, z.AltYBot, yBotBar, acilimSlot: 4, donatiNo: 5, styleId,
                isFirstFlightKol, z, pas, z.UstYTop);
        }

        /// <summary>
        /// 11. donatı (yalnız ilk kol): bölge1 alt → basamak üst; iki tarafa 40φ girer (gönye yok).
        /// </summary>
        private static void DrawFlightDonati_AltBotToBasamakUst40(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            FlightKesitZones z, double pas, double phiCm, ObjectId styleId)
        {
            double enterCm = 40.0 * phiCm;
            double yLo = z.AltYBot + pas;

            if (!TryBasamakDirs(z, out double bx, out double by, out double px, out double py))
                return;

            Point2d OffsetFromUst(Point2d p) =>
                new Point2d(p.X - px * pas, p.Y - py * pas);
            Point2d uA = OffsetFromUst(z.BasUstA);
            Point2d uB = OffsetFromUst(z.BasUstB);
            if (!TryIntersectY(uA, uB, yLo, out Point2d kirim)) return;

            double ux = z.BasUstB.X - z.BasUstA.X;
            double uy = z.BasUstB.Y - z.BasUstA.Y;
            double ul = Math.Sqrt(ux * ux + uy * uy);
            if (ul < Tol) { ux = bx; uy = by; }
            else { ux /= ul; uy /= ul; }

            Point2d start = new Point2d(kirim.X - enterCm, yLo);
            Point2d end = new Point2d(kirim.X + ux * enterCm, kirim.Y + uy * enterCm);

            var sharp = new List<Point2d> { start, kirim, end };
            DrawDonatiPathWithAcilim(
                tr, ms, ox, oy, fx, sharp, z.AltYBot, yLo, acilimSlot: 5, donatiNo: 11, styleId,
                true, z, pas, z.UstYTop);
        }

        /// <summary>
        /// 12. donatı (yalnız ilk kol): bölge1 üst → basamak alt; iki tarafa 40φ girer (gönye yok).
        /// </summary>
        private static void DrawFlightDonati_AltTopToBasamakAlt40(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            FlightKesitZones z, double pas, double phiCm, ObjectId styleId)
        {
            double enterCm = 40.0 * phiCm;
            double yBar = z.AltYTop - pas;

            if (!TryBasamakDirs(z, out double bx, out double by, out double px, out double py))
                return;

            Point2d OffsetIn(Point2d p) => new Point2d(p.X + px * pas, p.Y + py * pas);
            Point2d oA = OffsetIn(z.BasAltA);
            Point2d oB = OffsetIn(z.BasAltB);
            if (!TryIntersectY(oA, oB, yBar, out Point2d kirim)) return;

            Point2d start = new Point2d(kirim.X - enterCm, yBar);
            Point2d end = new Point2d(kirim.X + bx * enterCm, kirim.Y + by * enterCm);

            var sharp = new List<Point2d> { start, kirim, end };
            DrawDonatiPathWithAcilim(
                tr, ms, ox, oy, fx, sharp, z.AltYBot, yBar, acilimSlot: 6, donatiNo: 12, styleId,
                true, z, pas, z.UstYTop);
        }

        private static bool TryBasamakDirs(
            FlightKesitZones z,
            out double bx, out double by, out double px, out double py)
        {
            bx = by = px = py = 0;
            bx = z.BasAltB.X - z.BasAltA.X;
            by = z.BasAltB.Y - z.BasAltA.Y;
            double bl = Math.Sqrt(bx * bx + by * by);
            if (bl < Tol) return false;
            bx /= bl; by /= bl;
            px = -by;
            py = bx;
            if (py < 0) { px = -px; py = -py; }
            return true;
        }

        private const double TemelPaspayiCm = 5.0;

        /// <summary>Temel donatısı: alt kotun (kalınlık − temel paspayı) kadar altı.</summary>
        private static double TemelDonatiY(FlightKesitZones z, double temelKalCm)
        {
            double kal = temelKalCm < 10 ? 50 : temelKalCm;
            return z.AltYTop - (kal - TemelPaspayiCm);
        }

        private static bool TryIntersectX(Point2d a, Point2d b, double x, out Point2d hit)
        {
            hit = a;
            double dx = b.X - a.X;
            if (Math.Abs(dx) < Tol)
            {
                hit = new Point2d(x, a.Y);
                return false;
            }
            double t = (x - a.X) / dx;
            hit = new Point2d(x, a.Y + (b.Y - a.Y) * t);
            return true;
        }

        private static bool TryIntersectY(Point2d a, Point2d b, double y, out Point2d hit)
        {
            hit = a;
            double dy = b.Y - a.Y;
            if (Math.Abs(dy) < Tol)
            {
                hit = new Point2d(a.X, y);
                return true;
            }
            double t = (y - a.Y) / dy;
            hit = new Point2d(a.X + (b.X - a.X) * t, y);
            return true;
        }

        /// <summary>
        /// Temelde 1 ve 2. açılımın tepe noktası temel alt çizgisinin 10 cm altında.
        /// Diğer açılımlar aynı ek miktar kadar iner; kesit kutusu hepsini sarar.
        /// </summary>
        private static void SetTemelAcilimOfsetleri(
            FlightKesitZones z, double pas, double phiCm,
            double flightStartX, double basamakCm, double temelKalCm)
        {
            const double temelAltPayCm = 10.0;
            const double bar1Taban = 100.0;
            const double bar2Taban = 105.0;
            double kal = temelKalCm < 10 ? 50 : temelKalCm;
            double hedefTepe = -kal - temelAltPayCm;
            double tepe1 = TemelBarAcilimTepeY(z, pas, phiCm, flightStartX, basamakCm, bar1: true);
            double tepe2 = TemelBarAcilimTepeY(z, pas, phiCm, flightStartX, basamakCm, bar1: false);
            double need1 = tepe1 - hedefTepe;
            double need2 = tepe2 - hedefTepe;
            double extra = Math.Max(0, Math.Max(need1 - bar1Taban, need2 - bar2Taban));
            _temelAcilimEkAsagiCm = extra;
            _temelBar1AsagiCm = need1 - extra;
            _temelBar2AsagiCm = need2 - extra;
        }

        /// <summary>Temelde 1. (basamak üst) veya 2. (basamak alt) donatının 75φ uç kotu.</summary>
        private static double TemelBarAcilimTepeY(
            FlightKesitZones z, double pas, double phiCm,
            double flightStartX, double basamakCm, bool bar1)
        {
            double enterCm = 75.0 * phiCm;
            if (!TryBasamakDirs(z, out double bx, out double by, out double px, out double py))
                return 0;

            if (bar1)
            {
                double xLeg = flightStartX + pas;
                Point2d uA = new Point2d(z.BasUstA.X - px * pas, z.BasUstA.Y - py * pas);
                Point2d uB = new Point2d(z.BasUstB.X - px * pas, z.BasUstB.Y - py * pas);
                double ux = uB.X - uA.X;
                double uy = uB.Y - uA.Y;
                double ul = Math.Sqrt(ux * ux + uy * uy);
                if (ul < Tol) { ux = bx; uy = by; }
                else { ux /= ul; uy /= ul; }
                if (!TryIntersectX(uA, uB, xLeg, out Point2d topLeg))
                    topLeg = new Point2d(xLeg, z.AltYTop - pas);
                return topLeg.Y + uy * enterCm;
            }

            double xLeg2 = flightStartX + Math.Max(basamakCm, pas) - pas;
            Point2d oA = new Point2d(z.BasAltA.X + px * pas, z.BasAltA.Y + py * pas);
            Point2d oB = new Point2d(z.BasAltB.X + px * pas, z.BasAltB.Y + py * pas);
            if (!TryIntersectX(oA, oB, xLeg2, out Point2d botLeg))
                botLeg = new Point2d(xLeg2, z.AltYBot + pas);
            return botLeg.Y + by * enterCm;
        }

        /// <summary>
        /// Açılım: kesitteki konumundan aşağı ofset (cm). İlk basamaklı kol +10 cm. 5: ilk kol 160, diğer 170.
        /// </summary>
        private static double DonatiAcilimAsagiCm(int donatiNo, bool isFirstFlightKol)
        {
            switch (donatiNo)
            {
                case 1: return 120;
                case 2: return 90;
                case 3: return 130;
                case 4: return 100;
                case 5: return isFirstFlightKol ? 160 : 170;
                case 6: return 110;
                case 7: return 80;
                case 8: return 140;
                case 9: return 120;
                case 10: return 80;
                case 11: return 45;
                case 12: return 65;
                default: return 100;
            }
        }

        private static void DrawDonatiPathWithAcilim(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx,
            List<Point2d> sharp, double altYBot, double yBarRef, int acilimSlot,
            int donatiNo, ObjectId styleId, bool isFirstFlightKol = true,
            FlightKesitZones z = null, double pas = 3.0, double yTopFace = 0.0,
            double? acilimAsagiCm = null, double acilimDisaCm = 0)
        {
            var path = FilletDonatiPath(sharp, DonatiRadiusCm);
            if (path == null || path.Count < 2) return;

            var flipped = FlipDonatiPath(path, fx);
            AddPlineBulge(tr, ms, flipped, LayerDonati, Scale, ox, oy);

            // Kesitteki yerinden aşağı; temelde ek ofset + dış çıkıntı yönü
            double dyAcilim = -(acilimAsagiCm ?? DonatiAcilimAsagiCm(donatiNo, isFirstFlightKol))
                - _temelAcilimEkAsagiCm;
            double dxAcilim = Math.Abs(acilimDisaCm) < Tol ? 0 : fx(0) - fx(acilimDisaCm);
            var acilim = new List<(Point2d P, double Bulge)>(flipped.Count);
            for (int i = 0; i < flipped.Count; i++)
            {
                var (p, b) = flipped[i];
                acilim.Add((new Point2d(p.X + dxAcilim, p.Y + dyAcilim), b));
            }
            AddPlineBulge(tr, ms, acilim, LayerDonati, Scale, ox, oy);

            // Açılım etiketi + kol boyları
            MerdivenSettings.EnsureLoaded();
            double totalBoy = 0;
            if (sharp != null)
            {
                for (int i = 0; i < sharp.Count - 1; i++)
                    totalBoy += sharp[i].GetDistanceTo(sharp[i + 1]);
            }
            int boyCm = (int)Math.Round(totalBoy, MidpointRounding.AwayFromZero);
            int aralikCm = (int)Math.Round(MerdivenSettings.AnaDonatiAralikCm, MidpointRounding.AwayFromZero);
            if (aralikCm < 1) aralikCm = 20;
            // Adet = ⌈(kol − 2·pas) / aralık⌉ + 1  (ör. 120−6=114 → ⌈114/20⌉=6 aralık → 7 donatı)
            double pasCm = MerdivenSettings.PaspayiCm;
            if (pasCm < 0) pasCm = 0;
            int adet = CalcDonatiAdet(_acilimKolGenislikCm, aralikCm, pasCm);
            string etiket = WithMerdivenCarpan(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}\u00F8{1}/{2} L={3}",
                adet, MerdivenSettings.AnaDonatiCapMm, aralikCm, boyCm));

            // Etiket: en uzun kol ortası, kol açısı; yazı kutusu alt kenarı donatıdan 4 cm
            Extents3d? etiketBox = null;
            DBText etiketTxt = null;
            if (TryLongestKolAcilim(sharp, fx, dyAcilim, out Point2d etiketPt, out double etiketAng, dxAcilim))
            {
                etiketTxt = AddAcilimEtiketAboveBar(tr, ms, styleId, etiket, etiketPt, ox, oy,
                    height: 10.0, rotationRad: etiketAng);
            }
            else
            {
                Point2d midAcilim = MidPathLocal(acilim);
                // MidPathLocal +4 offset'i kaldır — gap yazı kutusundan gelir
                midAcilim = new Point2d(midAcilim.X, midAcilim.Y - 4.0);
                etiketTxt = AddAcilimEtiketAboveBar(tr, ms, styleId, etiket, midAcilim, ox, oy,
                    height: 10.0, rotationRad: 0.0);
            }
            if (etiketTxt != null && TryGetTextExtents(etiketTxt, out Extents3d eBox))
                etiketBox = eBox;

            AnnotateAcilimKollar(
                tr, ms, ox, oy, fx, styleId, sharp, dyAcilim, z, pas, altYBot, yTopFace,
                etiketBox, dxAcilim);
        }

        /// <summary>En uzun sharp kol → açılım orta noktası + okunabilir açı.</summary>
        private static bool TryLongestKolAcilim(
            List<Point2d> sharp, Func<double, double> fx, double dyAcilim,
            out Point2d midAcilim, out double angRad, double dxAcilim = 0)
        {
            midAcilim = default;
            angRad = 0;
            if (sharp == null || sharp.Count < 2) return false;
            double best = 0;
            int bestI = -1;
            for (int i = 0; i < sharp.Count - 1; i++)
            {
                double len = sharp[i].GetDistanceTo(sharp[i + 1]);
                if (len > best) { best = len; bestI = i; }
            }
            if (bestI < 0 || best < 4.0) return false;

            Point2d a = sharp[bestI];
            Point2d b = sharp[bestI + 1];
            Point2d af = new Point2d(fx(a.X) + dxAcilim, a.Y + dyAcilim);
            Point2d bf = new Point2d(fx(b.X) + dxAcilim, b.Y + dyAcilim);
            midAcilim = new Point2d(0.5 * (af.X + bf.X), 0.5 * (af.Y + bf.Y));
            angRad = ReadableSegmentAngle(af, bf);
            return true;
        }

        private static double ReadableSegmentAngle(Point2d af, Point2d bf)
        {
            double ang = Math.Atan2(bf.Y - af.Y, bf.X - af.X);
            if (ang > Math.PI * 0.5 + 1e-6 || ang < -Math.PI * 0.5 - 1e-6)
                ang += Math.PI;
            return ang;
        }

        /// <summary>
        /// Açılım kolları: boy (cm, ByLayer) + alt/üst (gönye hariç);
        /// yazı kutusu kenarı kola 4 cm (1/25 çizim).
        /// Etiketle çakışan boy → alt tarafa, alt/üst yazısının soluna aynı hizada.
        /// </summary>
        private static void AnnotateAcilimKollar(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy, Func<double, double> fx, ObjectId styleId,
            List<Point2d> sharp, double dyAcilim,
            FlightKesitZones z, double pas, double yBotFace, double yTopFace,
            Extents3d? etiketBox, double dxAcilim = 0)
        {
            if (sharp == null || sharp.Count < 2) return;
            const double h = 10.0;

            for (int i = 0; i < sharp.Count - 1; i++)
            {
                Point2d a = sharp[i];
                Point2d b = sharp[i + 1];
                double len = a.GetDistanceTo(b);
                if (len < 4.0) continue;

                Point2d mid = new Point2d(0.5 * (a.X + b.X), 0.5 * (a.Y + b.Y));
                bool isGonye = IsGonyeSegment(a, b);

                Point2d af = new Point2d(fx(a.X) + dxAcilim, a.Y + dyAcilim);
                Point2d bf = new Point2d(fx(b.X) + dxAcilim, b.Y + dyAcilim);
                double ang = ReadableSegmentAngle(af, bf);
                Point2d midA = new Point2d(0.5 * (af.X + bf.X), 0.5 * (af.Y + bf.Y));

                int lenCm = (int)Math.Round(len, MidpointRounding.AwayFromZero);
                string lenStr = lenCm.ToString(System.Globalization.CultureInfo.InvariantCulture);

                // Önce alt/üst (boy çakışınca soluna alınacak)
                DBText faceTxt = null;
                if (!isGonye)
                {
                    string face = ClassifyKolAltUst(mid, a, b, z, pas, yBotFace, yTopFace);
                    if (!string.IsNullOrEmpty(face))
                    {
                        faceTxt = AddAcilimKolYazi(tr, ms, styleId, face, midA, ox, oy,
                            height: h, rotationRad: ang, aboveBar: false, byLayer: false);
                    }
                }

                DBText boyTxt = AddAcilimKolYazi(tr, ms, styleId, lenStr, midA, ox, oy,
                    height: h, rotationRad: ang, aboveBar: true, byLayer: true);

                if (boyTxt == null || !etiketBox.HasValue) continue;
                if (!TryGetTextExtents(boyTxt, out Extents3d boyBox)) continue;
                if (!BoxesOverlap(boyBox, etiketBox.Value)) continue;

                // Etiketle çakışıyor → üstten al, alt tarafa / alt-üst soluna
                try { boyTxt.Erase(); } catch { }
                if (faceTxt != null)
                {
                    PlaceBoyLeftOfFace(tr, ms, styleId, lenStr, midA, ox, oy,
                        height: h, rotationRad: ang, faceTxt);
                }
                else
                {
                    AddAcilimKolYazi(tr, ms, styleId, lenStr, midA, ox, oy,
                        height: h, rotationRad: ang, aboveBar: false, byLayer: true);
                }
            }
        }

        /// <summary>
        /// Boy yazısını alt/üst yazısının soluna, aynı hizada (yazı kutusu kenarı).
        /// </summary>
        private static void PlaceBoyLeftOfFace(
            Transaction tr, BlockTableRecord ms, ObjectId styleId,
            string lenStr, Point2d midLocal, double ox, double oy,
            double height, double rotationRad, DBText faceTxt)
        {
            DBText boy = AddAcilimKolYazi(tr, ms, styleId, lenStr, midLocal, ox, oy,
                height, rotationRad, aboveBar: false, byLayer: true);
            if (boy == null) return;
            if (!TryGetTextExtents(faceTxt, out Extents3d fe)) return;
            if (!TryGetTextExtents(boy, out Extents3d be)) return;

            double c = Math.Cos(rotationRad);
            double s = Math.Sin(rotationRad);
            var along = new Vector3d(c, s, 0);
            var nrm = new Vector3d(-s, c, 0);

            // Boy sağ kenarı = face sol kenarı − 8 cm (1/25; yazı kutuları arası)
            const double sideGap = 8.0;
            double faceMinAlong = MinCornerProj(fe, along);
            double boyMaxAlong = MaxCornerProj(be, along);
            double shiftAlong = (faceMinAlong - sideGap) - boyMaxAlong;
            boy.TransformBy(Matrix3d.Displacement(along.MultiplyBy(shiftAlong)));

            // Aynı hiza (nrm): merkezleri eşitle
            if (!TryGetTextExtents(boy, out be)) return;
            double faceCn = 0.5 * (MinCornerProj(fe, nrm) + MaxCornerProj(fe, nrm));
            double boyCn = 0.5 * (MinCornerProj(be, nrm) + MaxCornerProj(be, nrm));
            boy.TransformBy(Matrix3d.Displacement(nrm.MultiplyBy(faceCn - boyCn)));
        }

        private static bool TryGetTextExtents(Entity e, out Extents3d ext)
        {
            ext = default;
            if (e == null || e.IsErased) return false;
            try { ext = e.GeometricExtents; return true; }
            catch { return false; }
        }

        private static bool BoxesOverlap(Extents3d a, Extents3d b)
        {
            const double pad = 0.5;
            return a.MinPoint.X <= b.MaxPoint.X + pad
                && a.MaxPoint.X >= b.MinPoint.X - pad
                && a.MinPoint.Y <= b.MaxPoint.Y + pad
                && a.MaxPoint.Y >= b.MinPoint.Y - pad;
        }

        private static double MinCornerProj(Extents3d e, Vector3d axis)
        {
            double m = double.MaxValue;
            foreach (Point3d p in ExtentsCorners(e))
            {
                double d = p.X * axis.X + p.Y * axis.Y;
                if (d < m) m = d;
            }
            return m;
        }

        private static double MaxCornerProj(Extents3d e, Vector3d axis)
        {
            double m = double.MinValue;
            foreach (Point3d p in ExtentsCorners(e))
            {
                double d = p.X * axis.X + p.Y * axis.Y;
                if (d > m) m = d;
            }
            return m;
        }

        private static Point3d[] ExtentsCorners(Extents3d e)
        {
            return new[]
            {
                new Point3d(e.MinPoint.X, e.MinPoint.Y, 0),
                new Point3d(e.MaxPoint.X, e.MinPoint.Y, 0),
                new Point3d(e.MinPoint.X, e.MaxPoint.Y, 0),
                new Point3d(e.MaxPoint.X, e.MaxPoint.Y, 0)
            };
        }

        /// <summary>Finiş gönye: neredeyse düşey kısa/orta kol.</summary>
        private static bool IsGonyeSegment(Point2d a, Point2d b)
        {
            Vector2d v = b - a;
            double len = v.Length;
            if (len < Tol) return false;
            // Düşeye yakın (±~20° içi) → gönye
            return Math.Abs(v.X) < Math.Abs(v.Y) * 0.40;
        }

        /// <summary>Kol: alt/üst yüz hangisine yakınsa o not.</summary>
        private static string ClassifyKolAltUst(
            Point2d mid, Point2d a, Point2d b,
            FlightKesitZones z, double pas, double yBotFace, double yTopFace)
        {
            Vector2d v = b - a;
            bool nearlyVert = v.Length > Tol && Math.Abs(v.X) < Math.Abs(v.Y) * 0.35;
            if (nearlyVert)
            {
                // Düşey gönye: yataya bağlanan ucun yüzü
                string fa = FaceAffinity(a, z, pas, yBotFace, yTopFace);
                string fb = FaceAffinity(b, z, pas, yBotFace, yTopFace);
                if (fa == fb) return fa;
                if (fa.Length > 0 && fb.Length == 0) return fa;
                if (fb.Length > 0 && fa.Length == 0) return fb;
                // İki uç farklı: yatay kola daha yakın uç (daha küçük |dAlt-dUst| farkı değil — alt/üst skoru)
                double scoreA = FaceScoreAlt(a, z, pas, yBotFace, yTopFace);
                double scoreB = FaceScoreAlt(b, z, pas, yBotFace, yTopFace);
                return Math.Abs(scoreA) <= Math.Abs(scoreB)
                    ? (scoreA <= 0 ? "alt" : "üst")
                    : (scoreB <= 0 ? "alt" : "üst");
            }

            return FaceAffinity(mid, z, pas, yBotFace, yTopFace);
        }

        /// <summary>Negatif = alt yüzüne yakın, pozitif = üst.</summary>
        private static double FaceScoreAlt(
            Point2d p, FlightKesitZones z, double pas, double yBotFace, double yTopFace)
        {
            double dAlt = DistToHoriz(p.Y, yBotFace + pas);
            double dUst = DistToHoriz(p.Y, yTopFace - pas);
            if (z != null)
            {
                dAlt = Math.Min(dAlt, DistToHoriz(p.Y, z.AltYBot + pas));
                dAlt = Math.Min(dAlt, DistToHoriz(p.Y, z.UstYBot + pas));
                dUst = Math.Min(dUst, DistToHoriz(p.Y, z.AltYTop - pas));
                dUst = Math.Min(dUst, DistToHoriz(p.Y, z.UstYTop - pas));
                if (TryBasamakDirs(z, out _, out _, out double px, out double py))
                {
                    Point2d oA = new Point2d(z.BasAltA.X + px * pas, z.BasAltA.Y + py * pas);
                    Point2d oB = new Point2d(z.BasAltB.X + px * pas, z.BasAltB.Y + py * pas);
                    Point2d uA = new Point2d(z.BasUstA.X - px * pas, z.BasUstA.Y - py * pas);
                    Point2d uB = new Point2d(z.BasUstB.X - px * pas, z.BasUstB.Y - py * pas);
                    dAlt = Math.Min(dAlt, DistPointToSeg(p, oA, oB));
                    dUst = Math.Min(dUst, DistPointToSeg(p, uA, uB));
                }
            }
            return dAlt - dUst; // <0 → alt
        }

        private static string FaceAffinity(
            Point2d p, FlightKesitZones z, double pas, double yBotFace, double yTopFace)
        {
            double s = FaceScoreAlt(p, z, pas, yBotFace, yTopFace);
            if (Math.Abs(s) < 0.8) return "";
            return s < 0 ? "alt" : "üst";
        }

        private static double DistToHoriz(double y, double yFace) => Math.Abs(y - yFace);

        private static double DistPointToSeg(Point2d p, Point2d a, Point2d b)
        {
            Vector2d ab = b - a;
            double lab = ab.Length;
            if (lab < Tol) return p.GetDistanceTo(a);
            double t = ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / (lab * lab);
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            var q = new Point2d(a.X + ab.X * t, a.Y + ab.Y * t);
            return p.GetDistanceTo(q);
        }

        /// <summary>
        /// Açılım etiket: yatay yerleştir → yazı kutusu alt kenarı donatıdan 4 cm →
        /// kol açısında döndür (okunabilir açı “üstü”).
        /// </summary>
        private static DBText AddAcilimEtiketAboveBar(
            Transaction tr, BlockTableRecord ms, ObjectId styleId,
            string text, Point2d midLocal, double ox, double oy,
            double height, double rotationRad)
        {
            return AddAcilimKolYazi(tr, ms, styleId, text, midLocal, ox, oy,
                height, rotationRad, aboveBar: true, byLayer: false, aci: 3);
        }

        /// <summary>
        /// Kol boyu / alt-üst: yazı kutusu kenarı kola 4 cm (1/25 çizim), kol açısında.
        /// aboveBar=true → üst kenar boşluğu; false → alt.
        /// </summary>
        private static DBText AddAcilimKolYazi(
            Transaction tr, BlockTableRecord ms, ObjectId styleId,
            string text, Point2d midLocal, double ox, double oy,
            double height, double rotationRad, bool aboveBar, bool byLayer,
            short aci = 2)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            double barX = ox + midLocal.X * Scale;
            double barY = oy + midLocal.Y * Scale;
            var barRef = new Point3d(barX, barY, 0);
            const double gap = 4.0; // 1/25 çizimde 4 cm

            var t = new DBText
            {
                TextString = text,
                Height = height,
                Rotation = 0,
                Layer = LayerDonatiYazi,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = aboveBar
                    ? TextVerticalMode.TextBottom
                    : TextVerticalMode.TextTop,
                AlignmentPoint = barRef,
                Color = byLayer
                    ? Color.FromColorIndex(ColorMethod.ByLayer, 256)
                    : Color.FromColorIndex(ColorMethod.ByAci, aci)
            };
            if (!styleId.IsNull)
                t.TextStyleId = styleId;
            ms.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
            try
            {
                t.AdjustAlignment(ms.Database);
                Extents3d ext = t.GeometricExtents;
                double cx = 0.5 * (ext.MinPoint.X + ext.MaxPoint.X);
                double dy = aboveBar
                    ? (barY + gap) - ext.MinPoint.Y
                    : (barY - gap) - ext.MaxPoint.Y;
                t.TransformBy(Matrix3d.Displacement(new Vector3d(barX - cx, dy, 0)));
            }
            catch { }
            if (Math.Abs(rotationRad) > 1e-12)
                t.TransformBy(Matrix3d.Rotation(rotationRad, Vector3d.ZAxis, barRef));
            return t;
        }

        private static void PlaceAnaDonatiNo(
            Transaction tr, BlockTableRecord ms, ObjectId styleId,
            string noTxt, double ox, double oy, List<(Point2d P, double Bulge)> path)
        {
            if (path == null || path.Count < 2) return;
            Point2d a = path[0].P;
            Point2d b = path[1].P;
            double best = a.GetDistanceTo(b);
            for (int k = 2; k < path.Count; k++)
            {
                double lenK = path[k - 1].P.GetDistanceTo(path[k].P);
                if (lenK > best)
                {
                    best = lenK;
                    a = path[k - 1].P;
                    b = path[k].P;
                }
            }
            double mx = 0.5 * (a.X + b.X);
            double my = 0.5 * (a.Y + b.Y);
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            double nx = 0, ny = 1;
            if (len > Tol)
            {
                nx = -dy / len;
                ny = dx / len;
            }
            double cx = 0, cy = 0;
            for (int k = 0; k < path.Count; k++)
            {
                cx += path[k].P.X;
                cy += path[k].P.Y;
            }
            cx /= path.Count;
            cy /= path.Count;
            if ((cx - mx) * nx + (cy - my) * ny > 0)
            {
                nx = -nx;
                ny = -ny;
            }
            const double offCm = 8.0;
            AddNumText(tr, ms, styleId, noTxt,
                new Point2d(ox + (mx + nx * offCm) * Scale, oy + (my + ny * offCm) * Scale),
                NumaraPunto, red: false, aci: 5);
        }

        private static Point2d MidPathLocal(List<(Point2d P, double Bulge)> path)
        {
            if (path == null || path.Count == 0) return new Point2d(0, 0);
            int i = path.Count / 2;
            if (path.Count >= 2 && i > 0)
            {
                var a = path[i - 1].P;
                var b = path[i].P;
                return new Point2d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5 + 4.0);
            }
            return new Point2d(path[i].P.X, path[i].P.Y + 4.0);
        }

        /// <summary>
        /// Köşe fillet R=…: yay iki kola teğet, her zaman dışbükey (keskin köşenin
        /// içinden geçen yay — ters çentik yok). Dönüş yönünden bağımsız.
        /// </summary>
        private static List<(Point2d P, double Bulge)> FilletDonatiPath(
            List<Point2d> sharp, double radius)
        {
            if (sharp == null || sharp.Count < 2) return null;

            var pts = new List<Point2d> { sharp[0] };
            for (int i = 1; i < sharp.Count; i++)
            {
                if (pts[pts.Count - 1].GetDistanceTo(sharp[i]) > Tol)
                    pts.Add(sharp[i]);
            }
            if (pts.Count < 2) return null;
            if (pts.Count == 2)
                return new List<(Point2d, double)> { (pts[0], 0), (pts[1], 0) };

            var result = new List<(Point2d P, double Bulge)>();
            result.Add((pts[0], 0));

            for (int i = 1; i < pts.Count - 1; i++)
            {
                Point2d prev = pts[i - 1];
                Point2d corner = pts[i];
                Point2d next = pts[i + 1];
                Vector2d vIn = corner - prev;
                Vector2d vOut = next - corner;
                double lenIn = vIn.Length;
                double lenOut = vOut.Length;
                if (lenIn < Tol || lenOut < Tol)
                {
                    result.Add((corner, 0));
                    continue;
                }
                vIn = vIn.GetNormal();
                vOut = vOut.GetNormal();
                double dot = vIn.X * vOut.X + vIn.Y * vOut.Y;
                if (dot > 1) dot = 1;
                if (dot < -1) dot = -1;
                double ang = Math.Acos(dot);
                if (ang < 1e-4 || Math.PI - ang < 1e-4)
                {
                    result.Add((corner, 0));
                    continue;
                }

                double r = radius;
                double cut = r * Math.Tan(ang * 0.5);
                double maxCut = Math.Min(lenIn, lenOut) * 0.45;
                if (cut > maxCut && maxCut > Tol)
                {
                    cut = maxCut;
                    r = cut / Math.Tan(ang * 0.5);
                }
                if (cut < Tol || r < Tol)
                {
                    result.Add((corner, 0));
                    continue;
                }

                Point2d t1 = new Point2d(corner.X - vIn.X * cut, corner.Y - vIn.Y * cut);
                Point2d t2 = new Point2d(corner.X + vOut.X * cut, corner.Y + vOut.Y * cut);

                // Dışbükey = yay, keskin köşenin TERS tarafında (köşe ile yay kirişin zıt yanları)
                Vector2d chord = t2 - t1;
                double cornerCross = chord.X * (corner.Y - t1.Y) - chord.Y * (corner.X - t1.X);
                double bulge = Math.Tan(ang * 0.25);
                // Merkez köşenin karşı tarafında → dışbükey gönye
                if (cornerCross > 0) bulge = -bulge;

                result.Add((t1, bulge));
                result.Add((t2, 0));
            }

            result.Add((pts[pts.Count - 1], 0));
            return result;
        }

        private static List<(Point2d P, double Bulge)> FlipDonatiPath(
            List<(Point2d P, double Bulge)> path, Func<double, double> fx)
        {
            // Yalnızca gerçek X aynalamada bulge işareti terslenir (identity'de BOZULMAZ)
            bool mirror = fx(1.0) < fx(0.0);
            var r = new List<(Point2d P, double Bulge)>(path.Count);
            for (int i = 0; i < path.Count; i++)
            {
                var (p, b) = path[i];
                r.Add((new Point2d(fx(p.X), p.Y), mirror ? -b : b));
            }
            return r;
        }

        private static void AddPlineBulge(
            Transaction tr, BlockTableRecord ms,
            List<(Point2d P, double Bulge)> path,
            string layer, double scale, double ox, double oy)
        {
            if (path == null || path.Count < 2) return;
            var pl = new Polyline();
            for (int i = 0; i < path.Count; i++)
            {
                var (p, b) = path[i];
                // Bulge vertex i → i+1 arasında; son vertex bulge 0
                double bulge = i < path.Count - 1 ? b : 0;
                pl.AddVertexAt(i,
                    new Point2d(ox + p.X * scale, oy + p.Y * scale),
                    bulge, 0, 0);
            }
            pl.Closed = false;
            pl.Layer = layer;
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static double ClampDonati(double v, double lo, double hi, double def)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return def;
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        /// <summary>
        /// Kolon/perde finiş (hepsi Line). xOuter = merdiven dışı yüz, xInner = birleşim yüzü.
        /// Üst/alt: yalnız KESIT SINIRI. İç yüz slab aralığında trim.
        /// </summary>
        private static void DrawKolonPerdeFinis(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy,
            double xOuter, double xInner,
            double ySlabBot, double ySlabTop,
            string cutLayer)
        {
            double yLo = ySlabBot - FinisKesitOffsetCm;
            double yHi = ySlabTop + FinisKesitOffsetCm;
            double xL = Math.Min(xOuter, xInner);
            double xR = Math.Max(xOuter, xInner);

            AddLineRaw(tr, ms,
                ox + xL * Scale, oy + yHi * Scale,
                ox + xR * Scale, oy + yHi * Scale,
                LayerKesitSiniri);
            AddLineRaw(tr, ms,
                ox + xL * Scale, oy + yLo * Scale,
                ox + xR * Scale, oy + yLo * Scale,
                LayerKesitSiniri);

            AddLineRaw(tr, ms,
                ox + xOuter * Scale, oy + yHi * Scale,
                ox + xOuter * Scale, oy + yLo * Scale,
                cutLayer);

            if (yHi > ySlabTop + Tol)
            {
                AddLineRaw(tr, ms,
                    ox + xInner * Scale, oy + yHi * Scale,
                    ox + xInner * Scale, oy + ySlabTop * Scale,
                    cutLayer);
            }
            if (ySlabBot > yLo + Tol)
            {
                AddLineRaw(tr, ms,
                    ox + xInner * Scale, oy + ySlabBot * Scale,
                    ox + xInner * Scale, oy + yLo * Scale,
                    cutLayer);
            }
        }

        /// <summary>
        /// Kiriş finiş (Line): üst + dış + alt + iç düşey yalnız döşeme altına kadar
        /// (slab kalınlığında merdivenle monolitik — ara düşey yok).
        /// ySlabBot = merdiven alt yüzü (soffit); iç yüz yBot→ySlabBot.
        /// </summary>
        private static void DrawKirisFinisU(
            Transaction tr, BlockTableRecord ms,
            double ox, double oy,
            double xOuter, double xInner,
            double yTop, double yBot,
            double ySlabBot)
        {
            if (Math.Abs(xOuter - xInner) < Tol) return;
            if (yBot > yTop)
            {
                double t = yBot;
                yBot = yTop;
                yTop = t;
            }

            // Üst (dış→iç); merdiven üstü içten devam — birleşimde T yok
            AddLineRaw(tr, ms,
                ox + xOuter * Scale, oy + yTop * Scale,
                ox + xInner * Scale, oy + yTop * Scale,
                LayerKesitCizgisi);
            // Dış düşey
            AddLineRaw(tr, ms,
                ox + xOuter * Scale, oy + yTop * Scale,
                ox + xOuter * Scale, oy + yBot * Scale,
                LayerKesitCizgisi);
            // Alt
            AddLineRaw(tr, ms,
                ox + xOuter * Scale, oy + yBot * Scale,
                ox + xInner * Scale, oy + yBot * Scale,
                LayerKesitCizgisi);
            // Kesite bakan yan: yalnız kirişin döşeme altındaki kısmı (slab aralığı trim)
            double yInnerTop = Math.Min(ySlabBot, yTop);
            if (yInnerTop > yBot + Tol)
            {
                AddLineRaw(tr, ms,
                    ox + xInner * Scale, oy + yBot * Scale,
                    ox + xInner * Scale, oy + yInnerTop * Scale,
                    LayerKesitCizgisi);
            }
        }

        private static double ResolveKirisHeightCm(EndBoundary b)
        {
            if (b != null && b.HeightCm >= 20 && b.HeightCm <= 150)
                return b.HeightCm;
            MerdivenSettings.EnsureLoaded();
            double h = MerdivenSettings.KirisYuksekligiCm;
            if (h < 20) h = 50;
            if (h > 150) h = 150;
            return h;
        }

        private static double WorldX(double ox, Func<double, double> fx, double xLocal)
        {
            return ox + fx(xLocal) * Scale;
        }

        /// <summary>
        /// KOLONDUSEY2 kot sembolü. Tepe, kot çizgisinin ortasında.
        /// Yazı 1/25’te 10 punto.
        /// </summary>
        private static void DrawMerdivenKot(
            Transaction tr, Database db, BlockTableRecord ms,
            double kotCm, double xLineA, double xLineB, double yLine)
        {
            EnsureLayer(tr, db, LayerKotCizgisi, 7, LineWeight.LineWeight020, false);
            EnsureLayer(tr, db, LayerKotYazi, 7, LineWeight.LineWeight020, false);

            const double triH = 12.0;
            const double halfW = 12.0;
            const double extToward = 20.0;
            const double extRight = 26.0;
            const double textH = 10.0;
            const double textAbove = 5.7;

            double apexX = 0.5 * (xLineA + xLineB);
            double apexY = yLine;

            AddLineRaw(tr, ms, apexX - extToward, apexY, apexX, apexY, LayerKotCizgisi);

            var pA = new Point3d(apexX, apexY, 0);
            var pTL = new Point3d(apexX - halfW, apexY + triH, 0);
            var pTM = new Point3d(apexX, apexY + triH, 0);
            var pTR = new Point3d(apexX + halfW, apexY + triH, 0);
            var pExt = new Point3d(apexX + halfW + extRight, apexY + triH, 0);

            var wedge = new Polyline(3);
            wedge.AddVertexAt(0, new Point2d(pA.X, pA.Y), 0, 0, 0);
            wedge.AddVertexAt(1, new Point2d(pTL.X, pTL.Y), 0, 0, 0);
            wedge.AddVertexAt(2, new Point2d(pTM.X, pTM.Y), 0, 0, 0);
            wedge.Closed = true;
            wedge.Layer = LayerKotCizgisi;
            ms.AppendEntity(wedge);
            tr.AddNewlyCreatedDBObject(wedge, true);

            var hatch = new Hatch();
            hatch.SetDatabaseDefaults();
            hatch.Layer = LayerKotCizgisi;
            hatch.Color = Color.FromColorIndex(ColorMethod.ByAci, 250);
            ms.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);
            try
            {
                hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                hatch.Associative = false;
                hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { wedge.ObjectId });
                try { hatch.EvaluateHatch(true); } catch { try { hatch.EvaluateHatch(false); } catch { } }
            }
            catch
            {
                try { hatch.Erase(); } catch { }
            }

            AddLineRaw(tr, ms, pTL.X, pTL.Y, pTR.X, pTR.Y, LayerKotCizgisi);
            AddLineRaw(tr, ms, pA.X, pA.Y, pTR.X, pTR.Y, LayerKotCizgisi);
            AddLineRaw(tr, ms, pTR.X, pTR.Y, pTM.X, pTM.Y, LayerKotCizgisi);
            AddLineRaw(tr, ms, pTR.X, pTR.Y, pExt.X, pExt.Y, LayerKotCizgisi);

            string txt = FormatMerdivenKot(kotCm);

            ObjectId styleId = EnsureTextStyle(tr, db);
            double textX = apexX + halfW;
            double textY = apexY + triH;
            var t = new DBText
            {
                TextString = txt,
                Height = textH,
                Layer = LayerKotYazi,
                TextStyleId = styleId,
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = TextVerticalMode.TextBottom,
                Position = new Point3d(textX, textY, 0),
                AlignmentPoint = new Point3d(textX, textY, 0)
            };
            ms.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
            try
            {
                t.AdjustAlignment(db);
                Extents3d ext = t.GeometricExtents;
                double dy = (textY + textAbove) - ext.MinPoint.Y;
                if (Math.Abs(dy) > 0.01)
                    t.TransformBy(Matrix3d.Displacement(new Vector3d(0, dy, 0)));
            }
            catch { }

            StackBenzerKatKotlari(tr, db, ms, styleId, t, kotCm, textH);
        }

        /// <summary>Adet ve benzer kat 1'den büyükse L= etiketinin önüne katxadetx.</summary>
        private static string WithMerdivenCarpan(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return tag;
            int nAdet = MerdivenSettings.BenzerAdet > 1 ? MerdivenSettings.BenzerAdet : 1;
            int nKat = MerdivenSettings.BenzerKat > 1 ? MerdivenSettings.BenzerKat : 1;
            if (nKat > 1 && nAdet > 1)
                return nKat.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x"
                    + nAdet.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x" + tag;
            if (nKat > 1)
                return nKat.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x" + tag;
            if (nAdet > 1)
                return nAdet.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x" + tag;
            return tag;
        }

        /// <summary>
        /// Benzer katların kotu, mevcut kot yazısının üstünde. 1/25 çizimde yazı kutuları arası 15.
        /// Her kopya bir kat yüksekliği yukarıdadır.
        /// </summary>
        private static void StackBenzerKatKotlari(
            Transaction tr, Database db, BlockTableRecord ms, ObjectId styleId,
            DBText first, double kotCm, double textH)
        {
            int nKat = MerdivenSettings.BenzerKat;
            if (first == null || nKat < 2) return;
            double hKat = MerdivenSettings.KatYuksekligiCm;
            if (hKat < 1) hKat = 300;
            const double gap = 15.0;
            double yTop;
            try
            {
                Extents3d ex0 = first.GeometricExtents;
                yTop = ex0.MaxPoint.Y;
            }
            catch { return; }

            double textX = first.AlignmentPoint.X;
            for (int k = 1; k < nKat; k++)
            {
                string txt = FormatMerdivenKot(kotCm + k * hKat);
                var tk = new DBText
                {
                    TextString = txt,
                    Height = textH,
                    Layer = LayerKotYazi,
                    TextStyleId = styleId,
                    HorizontalMode = TextHorizontalMode.TextLeft,
                    VerticalMode = TextVerticalMode.TextBottom,
                    Position = new Point3d(textX, yTop + gap, 0),
                    AlignmentPoint = new Point3d(textX, yTop + gap, 0)
                };
                ms.AppendEntity(tk);
                tr.AddNewlyCreatedDBObject(tk, true);
                try
                {
                    tk.AdjustAlignment(db);
                    Extents3d ex = tk.GeometricExtents;
                    double dy = (yTop + gap) - ex.MinPoint.Y;
                    if (Math.Abs(dy) > 0.01)
                        tk.TransformBy(Matrix3d.Displacement(new Vector3d(0, dy, 0)));
                    ex = tk.GeometricExtents;
                    yTop = ex.MaxPoint.Y;
                }
                catch { yTop += gap + textH; }
            }
        }

        private static string FormatMerdivenKot(double kotCm)
        {
            double m = kotCm / 100.0;
            double rounded = Math.Round(m, 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(rounded) < 0.005) return "\u00B10.00";
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:+0.00;-0.00}", rounded);
        }

        /// <summary>
        /// Eğik soffit (sofA→sofB) üzerinde y=yTarget noktası.
        /// t dışarı taşabilir (sahanlık birleşimi için uzatma).
        /// </summary>
        private static Point2d IntersectSoffitAtY(Point2d sofA, Point2d sofB, double yTarget, double xFallback)
        {
            double dy = sofB.Y - sofA.Y;
            if (Math.Abs(dy) < 1e-9)
                return new Point2d(xFallback, yTarget);
            double t = (yTarget - sofA.Y) / dy;
            double x = sofA.X + t * (sofB.X - sofA.X);
            return new Point2d(x, yTarget);
        }

        private static void AddLineRaw(
            Transaction tr, BlockTableRecord ms,
            double x0, double y0, double x1, double y1, string layer)
        {
            var ln = new Line(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0));
            ln.Layer = layer;
            ms.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
        }

        private static void AddRectRaw(
            Transaction tr, BlockTableRecord ms, double ox, double oy,
            double x0, double y0, double x1, double y1, string layer)
        {
            AddPline(tr, ms, new List<Point2d>
            {
                new Point2d(x0, y0),
                new Point2d(x1, y0),
                new Point2d(x1, y1),
                new Point2d(x0, y1)
            }, true, layer, Scale, ox, oy);
        }

        private static void AddPline(
            Transaction tr, BlockTableRecord ms, List<Point2d> pts, bool closed,
            string layer, double scale, double ox, double oy)
        {
            if (pts == null || pts.Count < 2) return;
            var pl = new Polyline();
            for (int i = 0; i < pts.Count; i++)
            {
                pl.AddVertexAt(i,
                    new Point2d(ox + pts[i].X * scale, oy + pts[i].Y * scale),
                    0, 0, 0);
            }
            pl.Closed = closed;
            pl.Layer = layer;
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        /// <summary>
        /// Kol kesit ekseni: bileşikte en uzun düz segment; yoksa orta teğet.
        /// </summary>
        private static Vector2d GroupCutAxis(List<PathVert> path, int i0, int i1)
        {
            double bestLen = -1;
            Vector2d best = new Vector2d(1, 0);
            if (path != null && i0 >= 0 && i1 >= i0 && i1 < path.Count - 1)
            {
                for (int i = i0; i <= i1; i++)
                {
                    if (!IsStraightSeg(path, i)) continue;
                    Vector2d v = path[i + 1].P - path[i].P;
                    double len = v.Length;
                    if (len < Tol || len <= bestLen) continue;
                    bestLen = len;
                    best = v.GetNormal();
                }
            }
            if (bestLen > 0) return best;
            if (TryGroupMidPoint(path, i0, i1, out _, out Vector2d tang) && tang.Length > 1e-12)
                return tang.GetNormal();
            return new Vector2d(1, 0);
        }

        /// <summary>
        /// Kola dik bakış: iki normalden skoru yüksek olan
        /// (aşağıdan yukarı = +Y, sağdan sola = −X tercih).
        /// </summary>
        private static Vector2d PreferBakısDir(Vector2d cutAxis)
        {
            if (cutAxis.Length < 1e-12) cutAxis = new Vector2d(1, 0);
            else cutAxis = cutAxis.GetNormal();
            var n1 = new Vector2d(-cutAxis.Y, cutAxis.X);
            var n2 = new Vector2d(cutAxis.Y, -cutAxis.X);
            double s1 = n1.Y - n1.X;
            double s2 = n2.Y - n2.X;
            return s1 >= s2 ? n1 : n2;
        }

        /// <summary>
        /// Bakışta sağ yön ile hat yönü ters ise kesit X aynalanır
        /// (sol↔sağ = bakış yönüne uygun).
        /// </summary>
        private static bool ShouldFlipSectionForBakıs(Vector2d cutAxis, Vector2d pathDir)
        {
            Vector2d view = PreferBakısDir(cutAxis);
            // Bakışta sağ: view'i 90° CW
            var right = new Vector2d(view.Y, -view.X);
            if (pathDir.Length < 1e-12) return false;
            pathDir = pathDir.GetNormal();
            return pathDir.X * right.X + pathDir.Y * right.Y < 0;
        }

        private static double FlipX(double x, bool flip, double xMin, double xMax) =>
            flip ? (xMin + xMax - x) : x;

        private static List<Point2d> FlipPts(List<Point2d> pts, Func<double, double> fx)
        {
            if (pts == null) return null;
            var r = new List<Point2d>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
                r.Add(new Point2d(fx(pts[i].X), pts[i].Y));
            return r;
        }

        /// <summary>
        /// Düz + (yaya teğet) yay + (yaya teğet) düz → tek grup.
        /// Artan yay + teğet düz de tek grup. Aksi halde her segment ayrı.
        /// </summary>
        private static List<(int I0, int I1)> BuildCikisHattiGroups(List<PathVert> path)
        {
            var groups = new List<(int, int)>();
            if (path == null || path.Count < 2) return groups;

            int segCount = path.Count - 1;
            int i = 0;
            while (i < segCount)
            {
                // düz + yay + düz, birleşimler teğet
                if (i + 2 < segCount
                    && IsStraightSeg(path, i)
                    && IsArcSeg(path, i + 1)
                    && IsStraightSeg(path, i + 2)
                    && IsTangentJoin(path, i)
                    && IsTangentJoin(path, i + 1))
                {
                    groups.Add((i, i + 2));
                    i += 3;
                    continue;
                }

                // yay + teğet düz
                if (IsArcSeg(path, i)
                    && i + 1 < segCount
                    && IsStraightSeg(path, i + 1)
                    && IsTangentJoin(path, i))
                {
                    groups.Add((i, i + 1));
                    i += 2;
                    continue;
                }

                // düz + teğet yay (sonraki düz yok / birleşmez) — yalnız düz şimdilik; yay sonra işlenir
                groups.Add((i, i));
                i += 1;
            }

            return groups;
        }

        private static bool IsStraightSeg(List<PathVert> path, int i) =>
            i >= 0 && i < path.Count - 1 && Math.Abs(path[i].Bulge) < 1e-8;

        private static bool IsArcSeg(List<PathVert> path, int i) =>
            i >= 0 && i < path.Count - 1 && Math.Abs(path[i].Bulge) >= 1e-8;

        /// <summary>Segment i ile i+1 birleşiminde teğet süreklilik (yön aynı).</summary>
        private static bool IsTangentJoin(List<PathVert> path, int i)
        {
            if (i < 0 || i + 1 >= path.Count - 1) return false;
            Vector2d t0 = TangentOnBulge(path[i].P, path[i + 1].P, path[i].Bulge, 1.0);
            Vector2d t1 = TangentOnBulge(path[i + 1].P, path[i + 2].P, path[i + 1].Bulge, 0.0);
            if (t0.Length < 1e-12 || t1.Length < 1e-12) return false;
            t0 = t0.GetNormal();
            t1 = t1.GetNormal();
            double dot = t0.X * t1.X + t0.Y * t1.Y;
            // ~8° içinde aynı yön
            return dot >= Math.Cos(8.0 * Math.PI / 180.0);
        }

        private static bool TryGroupMidPoint(
            List<PathVert> path, int i0, int i1, out Point2d mid, out Vector2d tang)
        {
            mid = default;
            tang = default;
            if (path == null || i0 < 0 || i1 < i0 || i1 >= path.Count - 1) return false;

            double total = 0;
            for (int i = i0; i <= i1; i++)
                total += SegLen(path, i);
            if (total < Tol) return false;

            double half = total * 0.5;
            double acc = 0;
            for (int i = i0; i <= i1; i++)
            {
                double len = SegLen(path, i);
                if (acc + len < half - 1e-9)
                {
                    acc += len;
                    continue;
                }
                double t = len < Tol ? 0.5 : (half - acc) / len;
                if (t < 0) t = 0;
                if (t > 1) t = 1;
                mid = PointOnBulge(path[i].P, path[i + 1].P, path[i].Bulge, t);
                tang = TangentOnBulge(path[i].P, path[i + 1].P, path[i].Bulge, t);
                return true;
            }

            mid = PointOnBulge(path[i1].P, path[i1 + 1].P, path[i1].Bulge, 0.5);
            tang = TangentOnBulge(path[i1].P, path[i1 + 1].P, path[i1].Bulge, 0.5);
            return true;
        }

        /// <summary>
        /// Paralel ardışık rıht çizgileri arası dik mesafe → en çok tekrarlanan (mod) cm.
        /// </summary>
        private static bool TryModeBasamakGenislikCm(
            List<(Point2d A, Point2d B)> rihts,
            List<PathHit> hits,
            out double modeCm)
        {
            modeCm = 0;
            if (rihts == null || hits == null || hits.Count < 2) return false;

            var counts = new Dictionary<int, int>();
            var sumByKey = new Dictionary<int, double>();

            for (int i = 0; i < hits.Count - 1; i++)
            {
                if (!TryFindRihtSegAt(hits[i].Pt, rihts, out var s0)) continue;
                if (!TryFindRihtSegAt(hits[i + 1].Pt, rihts, out var s1)) continue;
                if (!AreAlmostParallel(s0, s1, 8.0)) continue;

                double w = PerpDistanceBetweenSegs(s0, s1);
                if (w < 8.0 || w > 60.0) continue;

                int key = (int)Math.Round(w * 2.0);
                if (key < 1) continue;
                if (!counts.ContainsKey(key))
                {
                    counts[key] = 0;
                    sumByKey[key] = 0;
                }
                counts[key]++;
                sumByKey[key] += w;
            }

            if (counts.Count == 0) return false;

            int bestKey = -1;
            int bestCnt = 0;
            foreach (var kv in counts)
            {
                if (kv.Value > bestCnt || (kv.Value == bestCnt && kv.Key > bestKey))
                {
                    bestCnt = kv.Value;
                    bestKey = kv.Key;
                }
            }
            if (bestKey < 0 || bestCnt < 1) return false;

            modeCm = sumByKey[bestKey] / bestCnt;
            modeCm = Math.Round(modeCm * 2.0, MidpointRounding.AwayFromZero) / 2.0;
            return modeCm >= 8.0;
        }

        private static bool TryFindRihtSegAt(
            Point2d pt, List<(Point2d A, Point2d B)> rihts, out (Point2d A, Point2d B) seg)
        {
            seg = default;
            double best = HitTol + 1;
            bool ok = false;
            foreach (var r in rihts)
            {
                double d = DistPointSeg(pt, r.A, r.B);
                if (d < best)
                {
                    best = d;
                    seg = r;
                    ok = true;
                }
            }
            return ok && best <= HitTol + 1.0;
        }

        private static bool AreAlmostParallel((Point2d A, Point2d B) s0, (Point2d A, Point2d B) s1, double maxDeg)
        {
            Vector2d v0 = s0.B - s0.A;
            Vector2d v1 = s1.B - s1.A;
            if (v0.Length < Tol || v1.Length < Tol) return false;
            v0 = v0.GetNormal();
            v1 = v1.GetNormal();
            double c = Math.Abs(v0.X * v1.X + v0.Y * v1.Y);
            if (c > 1.0) c = 1.0;
            double deg = Math.Acos(c) * (180.0 / Math.PI);
            return deg <= maxDeg;
        }

        private static double PerpDistanceBetweenSegs((Point2d A, Point2d B) s0, (Point2d A, Point2d B) s1)
        {
            Vector2d v = s0.B - s0.A;
            if (v.Length < Tol) return s0.A.GetDistanceTo(s1.A);
            v = v.GetNormal();
            Vector2d w = s1.A - s0.A;
            return Math.Abs(w.X * (-v.Y) + w.Y * v.X);
        }

        /// <summary>Daire (baş) ve üçgen (son) da rıht sırasına eklenir.</summary>
        private static void EnsureEndpointRihtNumbers(List<PathVert> path, List<PathHit> hits)
        {
            if (path == null || path.Count < 2 || hits == null) return;

            double total = 0;
            for (int i = 0; i < path.Count - 1; i++)
                total += SegLen(path, i);

            Point2d start = path[0].P;
            Point2d end = path[path.Count - 1].P;

            if (!NearExisting(hits, 0, HitTol))
                hits.Add(new PathHit { Dist = 0, Pt = start });
            if (!NearExisting(hits, total, HitTol))
                hits.Add(new PathHit { Dist = total, Pt = end });

            hits.Sort((x, y) => x.Dist.CompareTo(y.Dist));
        }

        private static Point2d OffsetAlongNormal(
            List<PathVert> path, double dist, Point2d hit, double offset, out Vector2d nrm)
        {
            nrm = new Vector2d(0, 1);
            double acc = 0;
            int segCount = path.Count - 1;
            for (int i = 0; i < segCount; i++)
            {
                double len = SegLen(path, i);
                if (acc + len < dist - Tol)
                {
                    acc += len;
                    continue;
                }
                double t = len < Tol ? 0 : (dist - acc) / len;
                if (t < 0) t = 0;
                if (t > 1) t = 1;
                Vector2d tang = TangentOnBulge(path[i].P, path[i + 1].P, path[i].Bulge, t);
                if (tang.Length < 1e-12) return hit;
                tang = tang.GetNormal();
                nrm = new Vector2d(-tang.Y, tang.X);
                return hit + nrm * offset;
            }
            return hit;
        }

        private static void AddNumText(
            Transaction tr, BlockTableRecord ms, ObjectId styleId,
            string text, Point2d pt, double height, bool red, short? aci = null)
        {
            var t = new DBText
            {
                TextString = text,
                Height = height,
                Layer = LayerMerdivenYazi,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                AlignmentPoint = new Point3d(pt.X, pt.Y, 0)
            };
            if (!styleId.IsNull)
                t.TextStyleId = styleId;
            if (aci.HasValue)
                t.Color = Color.FromColorIndex(ColorMethod.ByAci, aci.Value);
            else if (red)
                t.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
            ms.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
        }

        private static ObjectId EnsureTextStyle(Transaction tr, Database db)
        {
            var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (tst.Has(TextStyleYazi))
                return tst[TextStyleYazi];
            tst.UpgradeOpen();
            var rec = new TextStyleTableRecord { Name = TextStyleYazi };
            tst.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return rec.ObjectId;
        }

        private static bool TryBuildOrientedPath(
            Transaction tr, ObjectId[] ids,
            out List<PathVert> path,
            out List<(Point2d A, Point2d B)> rihtSegs,
            out List<PlanSupport> supports,
            out string err)
        {
            path = null;
            rihtSegs = new List<(Point2d A, Point2d B)>();
            supports = new List<PlanSupport>();
            err = null;

            Polyline cikisPl = null;
            Point2d? circlePt = null;
            Point2d? triPt = null;
            var skipIds = new HashSet<ObjectId>();

            foreach (ObjectId id in ids)
            {
                if (id.IsNull) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                if (!IsMerdivenYaziLayer(ent.Layer)) continue;

                if (ent is Circle cir)
                {
                    circlePt = To2(cir.Center);
                    skipIds.Add(id);
                    continue;
                }

                if (ent is Polyline pl)
                {
                    if (IsTriangleMarker(pl))
                    {
                        triPt = PolyCentroid(pl);
                        skipIds.Add(id);
                        continue;
                    }
                    if (cikisPl == null || pl.Length > cikisPl.Length)
                        cikisPl = pl;
                }
            }

            if (cikisPl == null)
            {
                err = "MERDIVEN YAZI katmanında çıkış hattı polilinesi yok.";
                return false;
            }
            if (!circlePt.HasValue)
            {
                err = "MERDIVEN YAZI katmanında başlangıç dairesi yok (alt kot).";
                return false;
            }
            if (!triPt.HasValue)
            {
                err = "MERDIVEN YAZI katmanında bitiş üçgeni yok (üst kot).";
                return false;
            }

            skipIds.Add(cikisPl.ObjectId);
            path = BuildOrientedVertices(cikisPl, circlePt.Value, triPt.Value);

            var kirisLabels = new List<(Point2d Pos, double WidthCm, double HeightCm)>();

            foreach (ObjectId id in ids)
            {
                if (id.IsNull || skipIds.Contains(id)) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                string lyr = ent.Layer ?? "";

                if (IsMerdivenYaziLayer(lyr)) continue;

                // Kiriş etiketi: KZ-51 (25/50) → genişlik/yükseklik
                if (LayerHas(lyr, "KIRIS ISMI") || (LayerHas(lyr, "KIRIS") && LayerHas(lyr, "ISMI")))
                {
                    if (TryReadKirisLabel(ent, out Point2d lp, out double wCm, out double hCm))
                        kirisLabels.Add((lp, wCm, hCm));
                    continue;
                }

                // Rıht adayı: MERDIVEN veya DOSEME SINIRI (hattın kestiği çizgiler)
                if (IsMerdivenLayer(lyr) || IsDosemeSiniriLayer(lyr))
                {
                    if (ent is Line ln)
                        rihtSegs.Add((To2(ln.StartPoint), To2(ln.EndPoint)));
                    else if (ent is Polyline pl2)
                        AddPolylineStraightSegs(pl2, rihtSegs);
                    else if (ent is Arc arc)
                        AddArcChords(arc, rihtSegs);

                    // Döşeme sınırı aynı zamanda mesnet/sahanlık sınırı olabilir
                    if (IsDosemeSiniriLayer(lyr))
                        TryAddPlanSupport(ent, lyr, supports);
                    continue;
                }

                TryAddPlanSupport(ent, lyr, supports);
            }

            AssignKirisHeightsFromLabels(supports, kirisLabels);
            return true;
        }

        private static bool TryReadKirisLabel(Entity ent, out Point2d pos, out double widthCm, out double heightCm)
        {
            pos = default;
            widthCm = 0;
            heightCm = 0;
            string txt = null;
            if (ent is DBText db)
            {
                txt = db.TextString;
                pos = To2(db.Position);
            }
            else if (ent is MText mt)
            {
                txt = mt.Contents ?? mt.Text;
                pos = To2(mt.Location);
            }
            if (string.IsNullOrWhiteSpace(txt)) return false;
            // KZ-51 (25/50) veya 25/50
            var m = System.Text.RegularExpressions.Regex.Match(
                txt.Replace(',', '.'),
                @"\(\s*([0-9]+(?:\.[0-9]+)?)\s*/\s*([0-9]+(?:\.[0-9]+)?)\s*\)");
            if (!m.Success)
            {
                m = System.Text.RegularExpressions.Regex.Match(
                    txt.Replace(',', '.'),
                    @"([0-9]+(?:\.[0-9]+)?)\s*/\s*([0-9]+(?:\.[0-9]+)?)");
            }
            if (!m.Success) return false;
            if (!double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out widthCm))
                return false;
            if (!double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out heightCm))
                return false;
            return widthCm >= 5 && heightCm >= 15;
        }

        private static void AssignKirisHeightsFromLabels(
            List<PlanSupport> supports,
            List<(Point2d Pos, double WidthCm, double HeightCm)> labels)
        {
            if (supports == null || labels == null || labels.Count == 0) return;
            foreach (var s in supports)
            {
                if (!s.IsKiris || s.Edges == null || s.Edges.Count == 0) continue;
                double cx = 0, cy = 0;
                int n = 0;
                foreach (var e in s.Edges)
                {
                    cx += e.A.X + e.B.X;
                    cy += e.A.Y + e.B.Y;
                    n += 2;
                }
                if (n < 1) continue;
                var c = new Point2d(cx / n, cy / n);
                double best = 120.0; // cm
                double bestH = 0;
                foreach (var lb in labels)
                {
                    double d = c.GetDistanceTo(lb.Pos);
                    if (d < best)
                    {
                        best = d;
                        bestH = lb.HeightCm;
                    }
                }
                if (bestH >= 15) s.HeightCm = bestH;
            }
        }

        private static void TryAddPlanSupport(Entity ent, string lyr, List<PlanSupport> supports)
        {
            if (LayerHas(lyr, "ISMI") || LayerHas(lyr, "YAZI"))
                return;

            bool isDosSin = LayerHas(lyr, "DOSEME SINIRI");
            bool isKiris = !isDosSin && LayerHas(lyr, "KIRIS");
            bool isPerde = !isDosSin && LayerHas(lyr, "PERDE");
            bool isKolon = !isDosSin && LayerHas(lyr, "KOLON");
            if (!isKiris && !isPerde && !isKolon && !isDosSin) return;

            // Saf tarama katmanı atlanır; KOLON/PERDE tarama hatch'i extents ile alınır
            if (LayerHas(lyr, "TARAMA") && !isKolon && !isPerde && !isKiris && !isDosSin)
                return;

            var edges = new List<(Point2d A, Point2d B)>();
            if (ent is Polyline pl)
            {
                AddPolylineAllSegs(pl, edges);
            }
            else if (ent is Line ln)
            {
                edges.Add((To2(ln.StartPoint), To2(ln.EndPoint)));
            }
            else if (ent is Arc arc)
            {
                AddArcChords(arc, edges);
            }
            else
            {
                try
                {
                    Extents3d ex = ent.GeometricExtents;
                    Point2d a = new Point2d(ex.MinPoint.X, ex.MinPoint.Y);
                    Point2d b = new Point2d(ex.MaxPoint.X, ex.MinPoint.Y);
                    Point2d c = new Point2d(ex.MaxPoint.X, ex.MaxPoint.Y);
                    Point2d d = new Point2d(ex.MinPoint.X, ex.MaxPoint.Y);
                    edges.Add((a, b));
                    edges.Add((b, c));
                    edges.Add((c, d));
                    edges.Add((d, a));
                }
                catch
                {
                    return;
                }
            }

            if (edges.Count == 0) return;
            supports.Add(new PlanSupport
            {
                IsKiris = isKiris,
                IsPerde = isPerde,
                IsKolon = isKolon,
                IsDosemeSiniri = isDosSin,
                Edges = edges
            });
        }

        private static void AddPolylineAllSegs(Polyline pl, List<(Point2d A, Point2d B)> segs)
        {
            if (pl == null) return;
            int n = pl.NumberOfVertices;
            int segCount = pl.Closed ? n : n - 1;
            for (int i = 0; i < segCount; i++)
            {
                Point2d a = pl.GetPoint2dAt(i);
                Point2d b = pl.GetPoint2dAt((i + 1) % n);
                double bulge = pl.GetBulgeAt(i);
                if (Math.Abs(bulge) < 1e-8)
                {
                    if (a.GetDistanceTo(b) > Tol)
                        segs.Add((a, b));
                }
                else
                {
                    // Yay → kısa chord’lar (ışın kesişimi için)
                    double c = a.GetDistanceTo(b);
                    double ang = 4.0 * Math.Atan(Math.Abs(bulge));
                    double r = c * (1.0 + bulge * bulge) / (4.0 * Math.Abs(bulge));
                    double arcLen = r * ang;
                    int samples = Math.Max(4, (int)Math.Ceiling(arcLen / 5.0));
                    Point2d prev = a;
                    for (int s = 1; s <= samples; s++)
                    {
                        Point2d p = PointOnBulge(a, b, bulge, s / (double)samples);
                        segs.Add((prev, p));
                        prev = p;
                    }
                }
            }
        }

        private static bool IsMerdivenLayer(string lyr)
        {
            if (string.IsNullOrEmpty(lyr)) return false;
            if (IsMerdivenYaziLayer(lyr)) return false;
            return lyr.IndexOf("MERDIVEN", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsDosemeSiniriLayer(string lyr)
        {
            if (string.IsNullOrEmpty(lyr)) return false;
            return lyr.IndexOf("DOSEME SINIRI", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<PathVert> BuildOrientedVertices(
            Polyline pl, Point2d circle, Point2d triangle)
        {
            int n = pl.NumberOfVertices;
            var raw = new List<PathVert>(n);
            int segCount = pl.Closed ? n : n - 1;
            for (int i = 0; i < n; i++)
            {
                double bulge = i < segCount ? pl.GetBulgeAt(i) : 0;
                raw.Add(new PathVert { P = pl.GetPoint2dAt(i), Bulge = bulge });
            }
            if (pl.Closed && n > 0)
                raw[n - 1].Bulge = 0;

            Point2d start = raw[0].P;
            Point2d end = raw[raw.Count - 1].P;
            double costKeep = start.GetDistanceTo(circle) + end.GetDistanceTo(triangle);
            double costRev = start.GetDistanceTo(triangle) + end.GetDistanceTo(circle);
            if (costRev + 1e-6 < costKeep)
                return ReversePath(raw);
            return raw;
        }

        private static List<PathVert> ReversePath(List<PathVert> src)
        {
            int n = src.Count;
            var dst = new List<PathVert>(n);
            for (int i = 0; i < n; i++)
            {
                int j = n - 1 - i;
                double bulge = j > 0 ? -src[j - 1].Bulge : 0;
                dst.Add(new PathVert { P = src[j].P, Bulge = bulge });
            }
            if (dst.Count > 0) dst[dst.Count - 1].Bulge = 0;
            return dst;
        }

        private static double SegLen(List<PathVert> path, int i)
        {
            Point2d a = path[i].P;
            Point2d b = path[i + 1].P;
            double bulge = path[i].Bulge;
            if (Math.Abs(bulge) < 1e-10)
                return a.GetDistanceTo(b);
            double c = a.GetDistanceTo(b);
            if (c < 1e-12) return 0;
            double ang = 4.0 * Math.Atan(Math.Abs(bulge));
            double r = c * (1.0 + bulge * bulge) / (4.0 * Math.Abs(bulge));
            return r * ang;
        }

        /// <summary>
        /// Çıkış hattını kesen rıht adayları (MERDIVEN + DOSEME SINIRI).
        /// Etiket noktası her zaman hat üzerindedir.
        /// </summary>
        private static List<PathHit> CollectHitsAlongPath(
            List<PathVert> path,
            List<(Point2d A, Point2d B)> rihts)
        {
            var hits = new List<PathHit>();
            if (path == null || path.Count < 2 || rihts == null || rihts.Count == 0)
                return hits;

            int segCount = path.Count - 1;
            double distAcc = 0;
            for (int i = 0; i < segCount; i++)
            {
                Point2d a = path[i].P;
                Point2d b = path[i + 1].P;
                double bulge = path[i].Bulge;
                double segLen = SegLen(path, i);
                if (segLen < Tol)
                {
                    distAcc += segLen;
                    continue;
                }

                // Düz: chord = hat. Yay: kısa chord’lara böl — yalnız gerçek kesişim.
                int samples = Math.Abs(bulge) < 1e-8
                    ? 1
                    : Math.Max(8, (int)Math.Ceiling(segLen / 5.0));

                for (int s = 0; s < samples; s++)
                {
                    double t0 = s / (double)samples;
                    double t1 = (s + 1) / (double)samples;
                    Point2d p0 = PointOnBulge(a, b, bulge, t0);
                    Point2d p1 = PointOnBulge(a, b, bulge, t1);
                    double subLen = p0.GetDistanceTo(p1);
                    if (subLen < 1e-9) continue;

                    foreach (var r in rihts)
                    {
                        if (!TryIntersectSegSeg(p0, p1, r.A, r.B, out Point2d ip))
                            continue;
                        // Kesişim çıkış hattı chord’u üzerinde olmalı
                        if (DistPointSeg(ip, p0, p1) > HitTol) continue;

                        double localT = ProjectParam(p0, p1, ip);
                        double d = distAcc + t0 * segLen + localT * (t1 - t0) * segLen;
                        // Etiket her zaman hat üzerinde
                        Point2d onPath = PointOnBulge(a, b, bulge, t0 + localT * (t1 - t0));
                        if (!NearExisting(hits, d, HitTol))
                            hits.Add(new PathHit { Dist = d, Pt = onPath });
                    }
                }

                distAcc += segLen;
            }

            hits.Sort((x, y) => x.Dist.CompareTo(y.Dist));
            return hits;
        }

        private static Point2d PointOnBulge(Point2d a, Point2d b, double bulge, double t)
        {
            if (Math.Abs(bulge) < 1e-10)
                return new Point2d(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

            double c = a.GetDistanceTo(b);
            if (c < 1e-12) return a;
            double ang = 4.0 * Math.Atan(bulge);
            double absB = Math.Abs(bulge);
            double r = c * (1.0 + bulge * bulge) / (4.0 * absB);
            Vector2d ab = b - a;
            Vector2d mid = new Vector2d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
            Vector2d perp = new Vector2d(-ab.Y, ab.X);
            if (perp.Length < 1e-12) return a;
            perp = perp.GetNormal();
            double d = Math.Sqrt(Math.Max(0, r * r - (c * 0.5) * (c * 0.5)));
            if (bulge < 0) d = -d;
            Point2d center = new Point2d(mid.X + perp.X * d, mid.Y + perp.Y * d);
            double a0 = Math.Atan2(a.Y - center.Y, a.X - center.X);
            double a1 = a0 + ang * t;
            return new Point2d(center.X + r * Math.Cos(a1), center.Y + r * Math.Sin(a1));
        }

        private static Vector2d TangentOnBulge(Point2d a, Point2d b, double bulge, double t)
        {
            const double eps = 1e-3;
            Point2d p0 = PointOnBulge(a, b, bulge, Math.Max(0, t - eps));
            Point2d p1 = PointOnBulge(a, b, bulge, Math.Min(1, t + eps));
            return p1 - p0;
        }

        private static void AddPolylineStraightSegs(Polyline pl, List<(Point2d A, Point2d B)> segs)
        {
            int n = pl.NumberOfVertices;
            int last = pl.Closed ? n : n - 1;
            for (int i = 0; i < last; i++)
            {
                Point2d p0 = pl.GetPoint2dAt(i);
                Point2d p1 = pl.GetPoint2dAt((i + 1) % n);
                if (p0.GetDistanceTo(p1) < Tol) continue;
                segs.Add((p0, p1));
            }
        }

        private static void AddArcChords(Arc arc, List<(Point2d A, Point2d B)> segs)
        {
            double a0 = arc.StartAngle;
            double a1 = arc.EndAngle;
            double sweep = a1 - a0;
            while (sweep < 0) sweep += Math.PI * 2;
            int n = Math.Max(2, (int)Math.Ceiling(sweep / (Math.PI / 6)));
            Point2d prev = default;
            for (int i = 0; i <= n; i++)
            {
                double ang = a0 + sweep * (i / (double)n);
                Point2d p = new Point2d(
                    arc.Center.X + arc.Radius * Math.Cos(ang),
                    arc.Center.Y + arc.Radius * Math.Sin(ang));
                if (i > 0) segs.Add((prev, p));
                prev = p;
            }
        }

        private static bool IsTriangleMarker(Polyline pl)
        {
            if (pl == null) return false;
            int n = pl.NumberOfVertices;
            if (pl.Closed && n == 3) return true;
            if (pl.Closed && n == 4)
                return pl.GetPoint2dAt(0).GetDistanceTo(pl.GetPoint2dAt(3)) < Tol;
            if (!pl.Closed && n == 3)
                return pl.GetPoint2dAt(0).GetDistanceTo(pl.GetPoint2dAt(2)) < HitTol;
            if (pl.Closed && n <= 4 && pl.Length < 120) return true;
            return false;
        }

        private static Point2d PolyCentroid(Polyline pl)
        {
            double sx = 0, sy = 0;
            int n = pl.NumberOfVertices;
            for (int i = 0; i < n; i++)
            {
                Point2d p = pl.GetPoint2dAt(i);
                sx += p.X; sy += p.Y;
            }
            return n == 0 ? default : new Point2d(sx / n, sy / n);
        }

        private static bool IsMerdivenYaziLayer(string lyr)
        {
            if (string.IsNullOrEmpty(lyr)) return false;
            if (string.Equals(lyr, LayerMerdivenYazi, StringComparison.OrdinalIgnoreCase))
                return true;
            return lyr.IndexOf("MERDIVEN YAZI", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void EnsureLayer(Transaction tr, Database db, string name, short aci,
            LineWeight lw, bool dashed)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, aci),
                LineWeight = lw
            };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        private static bool LayerHas(string lyr, string key) =>
            !string.IsNullOrEmpty(lyr)
            && lyr.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;

        private static Point2d To2(Point3d p) => new Point2d(p.X, p.Y);

        private static bool NearExisting(List<PathHit> list, double v, double tol)
        {
            for (int i = 0; i < list.Count; i++)
                if (Math.Abs(list[i].Dist - v) <= tol) return true;
            return false;
        }

        private static double ProjectParam(Point2d a, Point2d b, Point2d p)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double den = dx * dx + dy * dy;
            if (den < 1e-18) return 0;
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / den;
            if (t < 0) return 0;
            if (t > 1) return 1;
            return t;
        }

        private static double DistPointSeg(Point2d p, Point2d a, Point2d b)
        {
            double t = ProjectParam(a, b, p);
            double qx = a.X + t * (b.X - a.X);
            double qy = a.Y + t * (b.Y - a.Y);
            double dx = p.X - qx, dy = p.Y - qy;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool TryIntersectSegSeg(
            Point2d a, Point2d b, Point2d c, Point2d d, out Point2d ip)
        {
            ip = default;
            double x1 = a.X, y1 = a.Y, x2 = b.X, y2 = b.Y;
            double x3 = c.X, y3 = c.Y, x4 = d.X, y4 = d.Y;
            double den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(den) < 1e-14) return false;
            double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / den;
            double u = ((x1 - x3) * (y1 - y2) - (y1 - y3) * (x1 - x2)) / den;
            if (t < -1e-6 || t > 1 + 1e-6 || u < -1e-6 || u > 1 + 1e-6) return false;
            ip = new Point2d(x1 + t * (x2 - x1), y1 + t * (y2 - y1));
            return true;
        }
    }
}
