using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// KIRISDUZ_002.lsp (C:KIRIS_D) ile aynı iş akışı: STA kesit/kiris DWG’de katman,
    /// yazı stili ve öğe düzenlemeleri; çizim birimi ve küçük ölçek düzeltmesi.
    /// </summary>
    internal static class KirisDuzeltRunner
    {
        /// <summary>PlanIdDrawingManager / KolonDonatiTableDrawer ile aynı Beykent katman adları.</summary>
        private const string LyrAksBalonu = "AKS BALONU (BEYKENT)";
        private const string LyrAksCizgisi = "AKS CIZGISI (BEYKENT)";
        private const string LyrAksYazisi = "AKS YAZISI (BEYKENT)";
        private const string LyrKiris = "KIRIS (BEYKENT)";
        private const string LyrKirisIsmi = "KIRIS ISMI (BEYKENT)";
        private const string LyrKolonIsmi = "KOLON ISMI (BEYKENT)";
        private const string LyrKotYazi = "KOT YAZI (BEYKENT)";
        private const string LyrKesitIsmi = "KESIT ISMI (BEYKENT)";
        private const string LyrKesitSiniri = "KESIT SINIRI (BEYKENT)";
        private const string LyrOlcu = "OLCU (BEYKENT)";
        private const string LyrOlcuYazisi = "OLCU YAZISI (BEYKENT)";
        private const string LyrDonatiYazisi = "DONATI YAZISI (BEYKENT)";
        private const string LyrDonati = "DONATI (BEYKENT)";
        private const string LyrIDonatiOku = "I.DONATI OKU (BEYKENT)";
        private const string StlYazi = "YAZI (BEYKENT)";
        private const string StlKot = "KOT (BEYKENT)";
        private const string StlOlcu = "OLCU (BEYKENT)";

        private const double ZeroLineTol = 0.001;
        private const double TinyAxisCircleRadius = 0.01;
        private const double ScaleUpFactor = 5000.0;
        /// <summary>AXIS3 tetikleyicisi yoksa: model alanı bu değerden küçükse ve STA katmanları varsa x5000 uygulanır (cm cinsinden mikro DXF).</summary>
        private const double StaModelMaxSpanForAutoScale = 120.0;
        /// <summary>KOT sembolu (gömülü geometri) yazi yuksekligine gore olcek: sembol yuksekligi ~ bu carpani * textHeight.</summary>
        private const double KotSymbolHeightVsText = 1.2;
        /// <summary>Kot yazisinin tabanina gore asagi kaydirma (yazi yuksekligi carpani).</summary>
        private const double KotGapBelowTextInTextHeights = 0.28;
        /// <summary>KOT sembolu WCS ofset: sol (-X) ve asagi (-Y), cm (cizim birimi cm kabul).</summary>
        private const double KotPlacementOffsetLeftCm = 20.0;
        private const double KotPlacementOffsetDownCm = 5.2002;
        /// <summary>KES_DET sembolu, kesit yazisi yuksekligine gore olcek.</summary>
        private const double KesDetSymbolHeightVsText = 1.2;
        /// <summary>KES_DET: yaziya ek donus (AutoCAD CCW derece).</summary>
        private const double KesDetExtraRotationDeg = 270.0;
        /// <summary>KES_DET sembolu WCS ofset: sag (+X), yukari (+Y), cm.</summary>
        private const double KesDetPlacementOffsetRightCm = 5.0;
        private const double KesDetPlacementOffsetUpCm = 11.0;
        /// <summary>Donati isaretci <c>arrowblock</c> INSERT icin yari cap (cm), daire yoksa olcek.</summary>
        private const double DonatiArrowMarkerEquivalentRadiusCm = 2.5;
        /// <summary>Donati sembolu (gömülü geometri) WCS sol (-X) ve yukari (+Y), cm.</summary>
        private const double KirisBundleOffsetLeftCm = 2.5;
        private const double KirisBundleOffsetUpCm = 2.5;
        /// <summary>KOT ve KES_DET: WCS Y ofseti (+ yukari). Once -2.5 idi; simdi 0 (2.5 cm yukari alindi).</summary>
        private const double KirisKotKesOffsetVerticalCm = 0.0;
        /// <summary>KOT sembolu + yazisi cevresinde OLCU temizlik (50 cm sol / ust hedefi; genis kutu).</summary>
        private const double OlcuCleanupPadLeftOfAnchorCm = 95.0;
        private const double OlcuCleanupPadRightOfAnchorCm = 35.0;
        private const double OlcuCleanupPadBelowAnchorCm = 40.0;
        private const double OlcuCleanupPadAboveAnchorCm = 60.0;
        /// <summary>KES_DET yerel dik KESIT cizgisinin sagina temizlik seridi genisligi (cm).</summary>
        private const double KesDetDonatiStaJunkStripWidthCm = 25.0;

        /// <summary>Dik cizgi Y araligina cm cinsinden kucuk pad (tarama/olcek).</summary>
        private const double KesDetDonatiStaJunkStripYPadCm = 0.5;

        /// <summary>STA artik kare/X: silinecek DONATI LINE uzunluklari (cm); buna uymayanlara dokunulmaz.</summary>
        private static readonly double[] KesDetStaDonatiJunkLineLengthsCm = { 55.0002, 25.0, 16.0079, 16.0079 };

        private const double KesDetStaDonatiJunkLengthTolCm = 0.02;

        private static readonly Regex KotDetail3Pattern = new Regex(@"[`\.\,]", RegexOptions.Compiled);
        private static readonly Regex EtrDotPattern = new Regex(@"etr\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static void Execute(Document doc)
        {
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            object oldOsmode = null;
            object oldCmdecho = null;
            object oldBlip = null;
            try
            {
                try { oldOsmode = Application.GetSystemVariable("OSMODE"); } catch { }
                try { oldCmdecho = Application.GetSystemVariable("CMDECHO"); } catch { }
                try { oldBlip = Application.GetSystemVariable("BLIPMODE"); } catch { }
                try { Application.SetSystemVariable("OSMODE", (short)0); } catch { }
                try { Application.SetSystemVariable("CMDECHO", (short)0); } catch { }
                try { Application.SetSystemVariable("BLIPMODE", (short)0); } catch { }

                ed.Command("_.UNDO", "_GROUP");

                ed.Command("_.ZOOM", "_E");

                RunOverkillNotColor4(ed);

                EraseZeroLengthLines(db, ed);
                SetAllTextWidthFactorOne(ed);

                ed.Command("_.ZOOM", "_E");
                try { Application.SetSystemVariable("LTSCALE", 0.5); } catch { }
                ed.Command("_.UNITS", "", "", "", "", "", "N");

                TryApplyStaDrawingScale(doc, db, ed);

                ed.Command("_.ZOOM", "_E");

                List<ObjectId> axis2Ids;
                using (doc.LockDocument())
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        EnsureTextStyles(tr, db);
                        EnsureDashedLinetypeLoaded(db, tr);
                        tr.Commit();
                    }

                    axis2Ids = new List<ObjectId>();
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        ProcessAxis3Circles(tr, db, ed);
                        axis2Ids = ProcessAxis2Lines(tr, db, ed);
                        ProcessAxis4Texts(tr, db, ed);
                        ProcessBeam4Texts(tr, db, ed);
                        ProcessBeam3Lines(tr, db, ed);
                        ProcessDetail3KotTexts(tr, db, ed);
                        ProcessPenc3Texts(tr, db, ed);
                        tr.Commit();
                    }

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        ProcessDetail4KesitiSection(tr, db, ed);
                        tr.Commit();
                    }

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        ProcessDetail2RebarTextArrows(tr, db, ed);
                        ProcessRebar2Lines(tr, db, ed);
                        ProcessBeam2Lines(tr, db, ed);
                        ProcessDimLayLines(tr, db, ed);
                        ProcessRebarSymbolTexts(tr, db, ed);
                        ProcessDimLayTexts(tr, db, ed);
                        ProcessDetail4KesitImi(tr, db, ed);
                        ProcessRebarGeometry(tr, db, ed);
                        ProcessDetail3KirisTexts(tr, db, ed);
                        ProcessDetail3KirisLines(tr, db, ed);
                        tr.Commit();
                    }

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        KirisDuzeltEmbeddedBlocks.TryEnsureAllBlocks(tr, db, ed);
                        tr.Commit();
                    }

                    ReplaceDonatiCirclesWithBlock(db, ed);

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        KirisDuzeltEmbeddedBlocks.TryPurgeArrowBlockDefinition(tr, db, ed);
                        tr.Commit();
                    }

                    InsertKesDetAtSectionLabels(db, ed);
                    InsertKotAtKotLabels(db, ed);
                    EraseOlcuClutterNearKotLabels(db, ed);
                    EraseDonatiClutterRightOfKesDetLabels(db, ed);

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        ApplyByLayerPropertiesToEntireDatabase(tr, db);
                        tr.Commit();
                    }
                }

                foreach (var id in axis2Ids)
                {
                    try
                    {
                        ed.SetImpliedSelection(new[] { id });
                        ed.Command("_.DRAWORDER", "", "");
                    }
                    catch { }
                }
                try { ed.Regen(); } catch { }

                ed.WriteMessage("\nKIRISDUZELT tamamlandi.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nKIRISDUZELT hata: {0}", ex.Message);
            }
            finally
            {
                try
                {
                    if (oldOsmode != null) Application.SetSystemVariable("OSMODE", oldOsmode);
                }
                catch { }
                try
                {
                    if (oldCmdecho != null) Application.SetSystemVariable("CMDECHO", oldCmdecho);
                }
                catch { }
                try
                {
                    if (oldBlip != null) Application.SetSystemVariable("BLIPMODE", oldBlip);
                }
                catch { }
                try { ed.Command("_.UNDO", "_END"); } catch { }
            }
        }

        private static List<ObjectId> SelectAllIds(Editor ed, SelectionFilter filt)
        {
            var r = ed.SelectAll(filt);
            if (r.Status != PromptStatus.OK || r.Value == null)
                return new List<ObjectId>();
            return new List<ObjectId>(r.Value.GetObjectIds());
        }

        private static void RunOverkillNotColor4(Editor ed)
        {
            var filt = new SelectionFilter(new[]
            {
                new TypedValue(-4, "<NOT"),
                new TypedValue(62, 4),
                new TypedValue(-4, "NOT>")
            });
            var r = ed.SelectAll(filt);
            if (r.Status != PromptStatus.OK || r.Value == null || r.Value.Count == 0)
                return;
            try
            {
                ed.SetImpliedSelection(r.Value.GetObjectIds());
                ed.Command("_.-OVERKILL", "", "", "");
            }
            catch { }
        }

        private static void EraseZeroLengthLines(Database db, Editor ed)
        {
            var filt = new SelectionFilter(new[] { new TypedValue(0, "LINE") });
            var r = ed.SelectAll(filt);
            if (r.Status != PromptStatus.OK || r.Value == null) return;

            var toErase = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in r.Value.GetObjectIds())
                {
                    var ln = tr.GetObject(id, OpenMode.ForRead) as Line;
                    if (ln == null) continue;
                    if (ln.StartPoint.DistanceTo(ln.EndPoint) < ZeroLineTol)
                        toErase.Add(id);
                }
                tr.Commit();
            }
            if (toErase.Count == 0) return;
            try
            {
                ed.SetImpliedSelection(toErase.ToArray());
                ed.Command("_.ERASE", "");
            }
            catch { }
        }

        private static void SetAllTextWidthFactorOne(Editor ed)
        {
            var filt = new SelectionFilter(new[] { new TypedValue(0, "TEXT") });
            var r = ed.SelectAll(filt);
            if (r.Status != PromptStatus.OK || r.Value == null) return;

            var db = ed.Document.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in r.Value.GetObjectIds())
                {
                    var dt = tr.GetObject(id, OpenMode.ForWrite) as DBText;
                    if (dt == null) continue;
                    dt.WidthFactor = 1.0;
                }
                tr.Commit();
            }
        }

        /// <summary>
        /// Lisp: AXIS3’te ilk daire &lt; 0,01 ise tüm çizim SCALE 5000 (0,0).
        /// Ek: SCALE komutu güvenilir olmadığı için <see cref="Entity.TransformBy"/> ile model uzayı ölçeklenir;
        /// AXIS3 yok / daire büyükse ama model çok küçükse ve STA katmanları varsa yine x5000.
        /// </summary>
        private static void TryApplyStaDrawingScale(Document doc, Database db, Editor ed)
        {
            if (!NeedsSta5000Scale(db, ed, out string reason))
                return;

            int n;
            using (doc.LockDocument())
            {
                n = ScaleAllModelSpaceEntities(db, ScaleUpFactor, Point3d.Origin);
            }
            ed.WriteMessage("\nKIRISDUZELT: Cizim olcegi x{0} ({1}), {2} oge guncellendi.", ScaleUpFactor, reason, n);
        }

        private static bool NeedsSta5000Scale(Database db, Editor ed, out string reason)
        {
            reason = "";
            var axisFilt = new SelectionFilter(new[]
            {
                new TypedValue(0, "CIRCLE"),
                new TypedValue(8, "AXIS3")
            });
            var rAxis = ed.SelectAll(axisFilt);
            if (rAxis.Status == PromptStatus.OK && rAxis.Value != null && rAxis.Value.Count > 0)
            {
                var firstId = rAxis.Value.GetObjectIds()[0];
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var c = tr.GetObject(firstId, OpenMode.ForRead) as Circle;
                    tr.Commit();
                    if (c != null && c.Radius < TinyAxisCircleRadius)
                    {
                        reason = "AXIS3 daire (lisp: ilki) yaricapi < 0.01";
                        return true;
                    }
                }
            }

            if (!TryGetModelSpaceMaxSpan(db, out double maxSpan))
                return false;

            if (maxSpan < StaModelMaxSpanForAutoScale && CountStaLikeLayerEntities(db) >= 3)
            {
                reason = "model boyutu kucuk (" + maxSpan.ToString("F2") + " <= " + StaModelMaxSpanForAutoScale + "), STA katmanlari";
                return true;
            }

            return false;
        }

        private static int CountStaLikeLayerEntities(Database db)
        {
            int n = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null || e.IsErased) continue;
                    if (IsStaExportLayerName(e.Layer))
                    {
                        n++;
                        if (n >= 3) break;
                    }
                }
                tr.Commit();
            }
            return n;
        }

        private static bool IsStaExportLayerName(string layer)
        {
            if (string.IsNullOrEmpty(layer)) return false;
            var u = layer.ToUpperInvariant();
            return u.Contains("AXIS") || u.Contains("BEAM") || u.Contains("DETAIL")
                   || u.Contains("REBAR") || u.Contains("PENC") || u.Contains("DIM_LAY");
        }

        private static bool TryGetModelSpaceMaxSpan(Database db, out double maxSpan)
        {
            maxSpan = 0;
            var ok = false;
            double minX = 0, minY = 0, maxX = 0, maxY = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    try
                    {
                        var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (e == null) continue;
                        var ex = e.GeometricExtents;
                        if (!ok)
                        {
                            minX = ex.MinPoint.X;
                            minY = ex.MinPoint.Y;
                            maxX = ex.MaxPoint.X;
                            maxY = ex.MaxPoint.Y;
                            ok = true;
                        }
                        else
                        {
                            if (ex.MinPoint.X < minX) minX = ex.MinPoint.X;
                            if (ex.MinPoint.Y < minY) minY = ex.MinPoint.Y;
                            if (ex.MaxPoint.X > maxX) maxX = ex.MaxPoint.X;
                            if (ex.MaxPoint.Y > maxY) maxY = ex.MaxPoint.Y;
                        }
                    }
                    catch { }
                }
                tr.Commit();
            }
            if (!ok) return false;
            maxSpan = Math.Max(maxX - minX, maxY - minY);
            return true;
        }

        /// <summary>Tüm model uzayı öğelerini (INSERT/XREF dahil) kök noktadan ölçekler.</summary>
        private static int ScaleAllModelSpaceEntities(Database db, double factor, Point3d basePoint)
        {
            int n = 0;
            var mat = Matrix3d.Scaling(factor, basePoint);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    var e = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (e == null) continue;
                    try
                    {
                        e.TransformBy(mat);
                        n++;
                    }
                    catch
                    {
                        if (e is BlockReference br)
                        {
                            try
                            {
                                br.BlockTransform = mat * br.BlockTransform;
                                n++;
                            }
                            catch { }
                        }
                    }
                }
                tr.Commit();
            }
            return n;
        }

        /// <summary>
        /// Model + tüm yerleşimler + blok tanımları içindeki öğeler (xref tanımı hariç):
        /// renk, çizgi tipi ve kalınlık BYLAYER.
        /// </summary>
        private static void ApplyByLayerPropertiesToEntireDatabase(Transaction tr, Database db)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId btrId in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                if (btr.IsFromExternalReference)
                    continue;

                foreach (ObjectId eid in btr)
                {
                    if (!eid.IsValid || eid.IsErased) continue;
                    try
                    {
                        var ent = tr.GetObject(eid, OpenMode.ForWrite) as Entity;
                        TrySetEntityColorLinetypeLineweightByLayer(ent);
                    }
                    catch { }
                }
            }
        }

        private static void TrySetEntityColorLinetypeLineweightByLayer(Entity ent)
        {
            if (ent == null || ent.IsErased) return;
            try
            {
                ent.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            }
            catch { }
            try
            {
                ent.LineWeight = LineWeight.ByLayer;
            }
            catch { }
            try
            {
                ent.Linetype = "ByLayer";
            }
            catch { }
        }

        private static TypedValue[] FilterTextOrMtext(params TypedValue[] rest)
        {
            var list = new List<TypedValue>
            {
                new TypedValue(-4, "<OR"),
                new TypedValue(0, "TEXT"),
                new TypedValue(0, "MTEXT"),
                new TypedValue(-4, "OR>")
            };
            list.AddRange(rest);
            return list.ToArray();
        }

        private static TypedValue[] FilterTextOrMtextLayerOr(params string[] layers)
        {
            var list = new List<TypedValue>
            {
                new TypedValue(-4, "<OR"),
                new TypedValue(0, "TEXT"),
                new TypedValue(0, "MTEXT"),
                new TypedValue(-4, "OR>"),
                new TypedValue(-4, "<OR")
            };
            foreach (var ly in layers)
                list.Add(new TypedValue(8, ly));
            list.Add(new TypedValue(-4, "OR>"));
            return list.ToArray();
        }

        private static void AppendDetail2MTextWithChar(Transaction tr, Editor ed, List<ObjectId> ids, char ch)
        {
            var have = new HashSet<ObjectId>(ids);
            var cand = SelectAllIds(ed, new SelectionFilter(new[]
            {
                new TypedValue(0, "MTEXT"),
                new TypedValue(8, "DETAIL2")
            }));
            foreach (var id in cand)
            {
                if (have.Contains(id)) continue;
                if (tr.GetObject(id, OpenMode.ForRead) is MText mt && MTextPlainContents(mt).IndexOf(ch) >= 0)
                {
                    ids.Add(id);
                    have.Add(id);
                }
            }
        }

        private static void EnsureTextStyles(Transaction tr, Database db)
        {
            EnsureTextStyle(tr, db, StlYazi, "Bahnschrift Light Condensed");
            EnsureTextStyle(tr, db, StlKot, "Bahnschrift Light Condensed");
            EnsureTextStyle(tr, db, StlOlcu, "Bahnschrift Light Condensed");
        }

        private static void EnsureTextStyle(Transaction tr, Database db, string styleName, string typeface)
        {
            var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (tst.Has(styleName)) return;
            tst.UpgradeOpen();
            var tsr = new TextStyleTableRecord { Name = styleName };
            try
            {
                tsr.Font = new FontDescriptor(typeface, false, false, 0, 0);
            }
            catch
            {
                try { tsr.Font = new FontDescriptor("Arial", false, false, 0, 0); } catch { }
            }
            tst.Add(tsr);
            tr.AddNewlyCreatedDBObject(tsr, true);
        }

        private static void EnsureDashedLinetypeLoaded(Database db, Transaction tr)
        {
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has("DASHED") || ltt.Has("Dashed")) return;
            try { db.LoadLineTypeFile("DASHED", "acad.lin"); } catch { }
            try { db.LoadLineTypeFile("Dashed", "acad.lin"); } catch { }
            try { db.LoadLineTypeFile("DASHED", "acadiso.lin"); } catch { }
            try { db.LoadLineTypeFile("Dashed", "acadiso.lin"); } catch { }
        }

        private static ObjectId GetLinetypeId(Transaction tr, Database db, string name)
        {
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has(name)) return ltt[name];
            if (string.Equals(name, "DASHED", StringComparison.OrdinalIgnoreCase) && ltt.Has("Dashed"))
                return ltt["Dashed"];
            return ltt["Continuous"];
        }

        private static void EnsureLayer(Transaction tr, Database db, string name, short colorIndex, LineWeight lw, string linetypeName = null)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            LayerTableRecord rec;
            if (lt.Has(name))
            {
                rec = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
            }
            else
            {
                lt.UpgradeOpen();
                rec = new LayerTableRecord
                {
                    Name = name,
                    Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex),
                    LineWeight = lw
                };
                lt.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }
            rec.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
            rec.LineWeight = lw;
            if (!string.IsNullOrEmpty(linetypeName))
                rec.LinetypeObjectId = GetLinetypeId(tr, db, linetypeName);
        }

        private static void ProcessAxis3Circles(Transaction tr, Database db, Editor ed)
        {
            var ids = SelectAllIds(ed, new SelectionFilter(new[]
            {
                new TypedValue(0, "CIRCLE"),
                new TypedValue(8, "AXIS3")
            }));
            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrAksBalonu, 7, LineWeight.LineWeight020);
            foreach (var id in ids)
            {
                var c = tr.GetObject(id, OpenMode.ForWrite) as Circle;
                if (c == null) continue;
                c.Layer = LyrAksBalonu;
            }
        }

        private static List<ObjectId> ProcessAxis2Lines(Transaction tr, Database db, Editor ed)
        {
            var ids = SelectAllIds(ed, new SelectionFilter(new[]
            {
                new TypedValue(0, "LINE"),
                new TypedValue(8, "AXIS2")
            }));
            if (ids.Count == 0) return ids;

            EnsureLayer(tr, db, LyrAksCizgisi, 252, LineWeight.LineWeight020, "DASHED");
            foreach (var id in ids)
            {
                var ln = tr.GetObject(id, OpenMode.ForWrite) as Line;
                if (ln == null) continue;
                ln.Layer = LyrAksCizgisi;
            }
            return ids;
        }

        private static ObjectId TextStyleId(Transaction tr, Database db, string styleName)
        {
            var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            return tst.Has(styleName) ? tst[styleName] : ObjectId.Null;
        }

        private static void ProcessAxis4Texts(Transaction tr, Database db, Editor ed)
        {
            var ids = SelectAllIds(ed, new SelectionFilter(FilterTextOrMtext(new TypedValue(8, "AXIS4"))));
            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrAksYazisi, 3, LineWeight.LineWeight020);
            var yazı = TextStyleId(tr, db, StlYazi);
            foreach (var id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    t.Layer = LyrAksYazisi;
                    if (!yazı.IsNull) t.TextStyleId = yazı;
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    mt.Layer = LyrAksYazisi;
                    if (!yazı.IsNull) mt.TextStyleId = yazı;
                }
            }
        }

        private static void ProcessBeam4Texts(Transaction tr, Database db, Editor ed)
        {
            var ids = SelectTextOrMtextOnLayerWithHeight(ed, tr, "BEAM4", 25.0);
            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrKirisIsmi, 40, LineWeight.LineWeight020);
            var yazı = TextStyleId(tr, db, StlYazi);
            foreach (var id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    t.Layer = LyrKirisIsmi;
                    t.Height = 20.0;
                    if (!yazı.IsNull) t.TextStyleId = yazı;
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    mt.Layer = LyrKirisIsmi;
                    mt.TextHeight = 20.0;
                    if (!yazı.IsNull) mt.TextStyleId = yazı;
                }
            }
        }

        private static List<ObjectId> SelectTextOrMtextOnLayerWithHeight(Editor ed, Transaction tr, string layer, double height, double tol = 0.02)
        {
            var cand = SelectAllIds(ed, new SelectionFilter(FilterTextOrMtext(new TypedValue(8, layer))));
            var r = new List<ObjectId>();
            foreach (var id in cand)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is DBText dt && Math.Abs(dt.Height - height) < tol)
                    r.Add(id);
                else if (tr.GetObject(id, OpenMode.ForRead) is MText mt && Math.Abs(mt.TextHeight - height) < tol)
                    r.Add(id);
            }
            return r;
        }

        private static void ProcessBeam3Lines(Transaction tr, Database db, Editor ed)
        {
            var ids = SelectAllIds(ed, new SelectionFilter(new[]
            {
                new TypedValue(0, "LINE"),
                new TypedValue(8, "BEAM3")
            }));
            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrKiris, 2, LineWeight.LineWeight030);
            foreach (var id in ids)
            {
                var ln = tr.GetObject(id, OpenMode.ForWrite) as Line;
                if (ln == null) continue;
                ln.Layer = LyrKiris;
            }
        }

        private static void ProcessDetail3KotTexts(Transaction tr, Database db, Editor ed)
        {
            var ids = SelectAllIds(ed, new SelectionFilter(FilterTextOrMtext(
                new TypedValue(8, "DETAIL3"),
                new TypedValue(1, "*`.*"))));
            if (ids.Count == 0)
                ids = CollectDetail3KotLikeTexts(tr, ed);

            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrKotYazi, 7, LineWeight.LineWeight020);
            var kotStyle = TextStyleId(tr, db, StlKot);

            foreach (var id in ids)
            {
                Point3d p;
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    p = t.Position;
                    var p1 = new Point3d(p.X - 70.0, p.Y - 55.0, p.Z);
                    var p2 = new Point3d(p.X + 70.0, p.Y, p.Z);
                    EraseLinesInWindow(ed, p1, p2, "DETAIL2");
                    t.Position = new Point3d(p.X, p.Y - 35.0, p.Z);
                    t.Layer = LyrKotYazi;
                    if (!kotStyle.IsNull) t.TextStyleId = kotStyle;
                    t.Height = 10.0;
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    p = mt.Location;
                    var p1 = new Point3d(p.X - 70.0, p.Y - 55.0, p.Z);
                    var p2 = new Point3d(p.X + 70.0, p.Y, p.Z);
                    EraseLinesInWindow(ed, p1, p2, "DETAIL2");
                    mt.Location = new Point3d(p.X, p.Y - 35.0, p.Z);
                    mt.Layer = LyrKotYazi;
                    if (!kotStyle.IsNull) mt.TextStyleId = kotStyle;
                    mt.TextHeight = 10.0;
                }
            }
        }

        private static List<ObjectId> CollectDetail3KotLikeTexts(Transaction tr, Editor ed)
        {
            var list = new List<ObjectId>();
            var ids = SelectAllIds(ed, new SelectionFilter(FilterTextOrMtext(new TypedValue(8, "DETAIL3"))));
            foreach (var id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is DBText t)
                {
                    var s = t.TextString ?? "";
                    if (KotDetail3Pattern.IsMatch(s))
                        list.Add(id);
                }
                else if (tr.GetObject(id, OpenMode.ForRead) is MText mt)
                {
                    var s = MTextPlainContents(mt);
                    if (KotDetail3Pattern.IsMatch(s))
                        list.Add(id);
                }
            }
            return list;
        }

        private static string MTextPlainContents(MText mt)
        {
            if (mt == null) return "";
            var c = mt.Contents ?? "";
            return c.Replace("\\P", " ");
        }

        private static void EraseLinesInWindow(Editor ed, Point3d corner1, Point3d corner2, string layer)
        {
            var filt = new SelectionFilter(new[]
            {
                new TypedValue(0, "LINE"),
                new TypedValue(8, layer)
            });
            try
            {
                var r = ed.SelectWindow(corner1, corner2, filt);
                if (r.Status == PromptStatus.OK && r.Value != null && r.Value.Count > 0)
                {
                    ed.SetImpliedSelection(r.Value.GetObjectIds());
                    ed.Command("_.ERASE", "");
                }
            }
            catch { }
        }

        private static void ProcessPenc3Texts(Transaction tr, Database db, Editor ed)
        {
            var ids = SelectTextOrMtextOnLayerWithHeight(ed, tr, "PENC3", 12.5);
            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrKolonIsmi, 91, LineWeight.LineWeight020);
            var yazı = TextStyleId(tr, db, StlYazi);
            foreach (var id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    t.Layer = LyrKolonIsmi;
                    if (!yazı.IsNull) t.TextStyleId = yazı;
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    mt.Layer = LyrKolonIsmi;
                    if (!yazı.IsNull) mt.TextStyleId = yazı;
                }
            }
        }

        private static void ProcessDetail4KesitiSection(Transaction tr, Database db, Editor ed)
        {
            var filt = new SelectionFilter(FilterTextOrMtext(
                new TypedValue(1, "*KESITI*"),
                new TypedValue(8, "DETAIL4")));
            var ids = SelectAllIds(ed, filt);
            AppendDetail4KesitiMtextFallback(tr, ed, ids);
            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrIDonatiOku, 160, LineWeight.LineWeight020);
            EnsureLayer(tr, db, LyrKesitIsmi, 6, LineWeight.LineWeight020);
            var yazı = TextStyleId(tr, db, StlYazi);

            foreach (var id in ids)
            {
                Point3d ins;
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    t.Layer = LyrKesitIsmi;
                    t.Height = 15.0;
                    if (!yazı.IsNull) t.TextStyleId = yazı;
                    ins = t.Position;
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    mt.Layer = LyrKesitIsmi;
                    mt.TextHeight = 15.0;
                    if (!yazı.IsNull) mt.TextStyleId = yazı;
                    ins = mt.Location;
                }
                else
                    continue;

                var refNok1 = new Point3d(ins.X + 40.0, ins.Y + 60.0, ins.Z);
                var refNok2 = new Point3d(refNok1.X, refNok1.Y + 30.0, refNok1.Z);

                ObjectId cizgiAltId = ObjectId.Null;
                var fenceLow = SelectFenceLineOnLayer(ed, refNok1, refNok2, "DETAIL3");
                if (fenceLow != null && fenceLow.Length > 0)
                    cizgiAltId = fenceLow[0];

                ObjectId cizgiUstId = ObjectId.Null;
                if (!cizgiAltId.IsNull)
                {
                    for (double dy = 60.0; dy < 12000.0; dy += 60.0)
                    {
                        var pEnd = new Point3d(refNok2.X, refNok2.Y + dy, refNok2.Z);
                        var fenceU = SelectFenceLineOnLayer(ed, refNok2, pEnd, "DETAIL3");
                        if (fenceU != null && fenceU.Length > 0)
                        {
                            cizgiUstId = fenceU[0];
                            break;
                        }
                    }
                }

                if (!cizgiAltId.IsNull && !cizgiUstId.IsNull)
                {
                    var lnA = tr.GetObject(cizgiAltId, OpenMode.ForRead) as Line;
                    var lnU = tr.GetObject(cizgiUstId, OpenMode.ForRead) as Line;
                    if (lnA != null && lnU != null)
                    {
                        var nok1 = new Point3d(lnA.StartPoint.X, lnA.StartPoint.Y - 10.0, 0.0);
                        var endA = lnA.EndPoint;
                        var endU = lnU.EndPoint;
                        var nok2 = new Point3d(endA.X, endU.Y + 10.0, 0.0);
                        CrossingSetLayerDetail2Lines(ed, tr, nok1, nok2, LyrIDonatiOku);
                    }
                }

                if (tr.GetObject(id, OpenMode.ForWrite) is Entity moved)
                    moved.TransformBy(Matrix3d.Displacement(new Vector3d(25.0, 0, 0)));
            }
        }

        private static void AppendDetail4KesitiMtextFallback(Transaction tr, Editor ed, List<ObjectId> ids)
        {
            var have = new HashSet<ObjectId>(ids);
            var cand = SelectAllIds(ed, new SelectionFilter(FilterTextOrMtext(new TypedValue(8, "DETAIL4"))));
            foreach (var id in cand)
            {
                if (have.Contains(id)) continue;
                if (tr.GetObject(id, OpenMode.ForRead) is MText mt
                    && MTextPlainContents(mt).IndexOf("KESITI", StringComparison.OrdinalIgnoreCase) >= 0)
                    ids.Add(id);
            }
        }

        private static ObjectId[] SelectFenceLineOnLayer(Editor ed, Point3d a, Point3d b, string layer)
        {
            try
            {
                var pts = new Point3dCollection { a, b };
                var filt = new SelectionFilter(new[]
                {
                    new TypedValue(0, "LINE"),
                    new TypedValue(8, layer)
                });
                var r = ed.SelectFence(pts, filt);
                if (r.Status == PromptStatus.OK && r.Value != null && r.Value.Count > 0)
                    return r.Value.GetObjectIds();
            }
            catch { }
            return Array.Empty<ObjectId>();
        }

        private static void CrossingSetLayerDetail2Lines(Editor ed, Transaction tr, Point3d nok1, Point3d nok2, string newLayer)
        {
            try
            {
                var filt = new SelectionFilter(new[]
                {
                    new TypedValue(0, "LINE"),
                    new TypedValue(8, "DETAIL2")
                });
                var r = ed.SelectCrossingWindow(nok1, nok2, filt);
                if (r.Status != PromptStatus.OK || r.Value == null) return;
                foreach (var lid in r.Value.GetObjectIds())
                {
                    var ln = tr.GetObject(lid, OpenMode.ForWrite) as Line;
                    if (ln == null) continue;
                    ln.Layer = newLayer;
                }
            }
            catch { }
        }

        private static void ProcessDetail2RebarTextArrows(Transaction tr, Database db, Editor ed)
        {
            var filt = new SelectionFilter(FilterTextOrMtext(
                new TypedValue(1, "*\u0192*"),
                new TypedValue(8, "DETAIL2")));
            var ids = SelectAllIds(ed, filt);
            AppendDetail2MTextWithChar(tr, ed, ids, '\u0192');
            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrIDonatiOku, 160, LineWeight.LineWeight020);

            foreach (var id in ids)
            {
                Point3d p;
                if (tr.GetObject(id, OpenMode.ForRead) is DBText t)
                    p = t.Position;
                else if (tr.GetObject(id, OpenMode.ForRead) is MText mt)
                    p = mt.Location;
                else
                    continue;
                var ref1 = new Point3d(p.X + 5.0, p.Y - 5.0, p.Z);
                var ref2 = new Point3d(p.X - 220.0, p.Y + 5.0, p.Z);
                WindowSetLayerForLines(ed, tr, ref2, ref1, "DETAIL2", LyrIDonatiOku);
            }
        }

        private static void WindowSetLayerForLines(Editor ed, Transaction tr, Point3d c1, Point3d c2, string fromLayer, string toLayer)
        {
            try
            {
                var filt = new SelectionFilter(new[]
                {
                    new TypedValue(0, "LINE"),
                    new TypedValue(8, fromLayer)
                });
                var r = ed.SelectWindow(c1, c2, filt);
                if (r.Status != PromptStatus.OK || r.Value == null) return;
                foreach (var lid in r.Value.GetObjectIds())
                {
                    var ln = tr.GetObject(lid, OpenMode.ForWrite) as Line;
                    if (ln == null) continue;
                    ln.Layer = toLayer;
                }
            }
            catch { }
        }

        private static void ProcessRebar2Lines(Transaction tr, Database db, Editor ed)
        {
            var filt = new SelectionFilter(new[]
            {
                new TypedValue(0, "LINE"),
                new TypedValue(-4, "<OR"),
                new TypedValue(8, "REBAR2"),
                new TypedValue(8, "REBAR_DET2"),
                new TypedValue(-4, "OR>")
            });
            var ids = SelectAllIds(ed, filt);
            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrIDonatiOku, 160, LineWeight.LineWeight020);
            foreach (var id in ids)
            {
                var ln = tr.GetObject(id, OpenMode.ForWrite) as Line;
                if (ln == null) continue;
                ln.Layer = LyrIDonatiOku;
            }
        }

        private static void ProcessBeam2Lines(Transaction tr, Database db, Editor ed)
        {
            var ids = SelectAllIds(ed, new SelectionFilter(new[]
            {
                new TypedValue(0, "LINE"),
                new TypedValue(8, "BEAM2")
            }));
            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrKesitSiniri, 241, LineWeight.LineWeight020, "DASHED");
            foreach (var id in ids)
            {
                var ln = tr.GetObject(id, OpenMode.ForWrite) as Line;
                if (ln == null) continue;
                ln.Layer = LyrKesitSiniri;
            }
        }

        private static void ProcessDimLayLines(Transaction tr, Database db, Editor ed)
        {
            var filt = new SelectionFilter(new[]
            {
                new TypedValue(0, "LINE"),
                new TypedValue(-4, "<OR"),
                new TypedValue(8, "DIM_LAY"),
                new TypedValue(8, "DETAIL2"),
                new TypedValue(-4, "OR>")
            });
            var ids = SelectAllIds(ed, filt);
            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrOlcu, 14, LineWeight.LineWeight020);
            foreach (var id in ids)
            {
                var ln = tr.GetObject(id, OpenMode.ForWrite) as Line;
                if (ln == null) continue;
                ln.Layer = LyrOlcu;
            }
        }

        private static void ProcessRebarSymbolTexts(Transaction tr, Database db, Editor ed)
        {
            var filt = new SelectionFilter(new[]
            {
                new TypedValue(0, "TEXT"),
                new TypedValue(-4, "<OR"),
                new TypedValue(1, "*\u0192*"),
                new TypedValue(1, "*\u00F8*"),
                new TypedValue(-4, "OR>")
            });
            var ids = SelectAllIds(ed, filt);
            AppendMTextDonatiSymbolCandidates(tr, ed, ids);

            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrDonatiYazisi, 3, LineWeight.LineWeight020);
            var yazı = TextStyleId(tr, db, StlYazi);

            foreach (var id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    t.Layer = LyrDonatiYazisi;
                    if (!yazı.IsNull) t.TextStyleId = yazı;
                    var s = t.TextString ?? "";
                    while (s.Length > 0 && s[s.Length - 1] == '\u00FF')
                        s = s.Substring(0, s.Length - 1);
                    s = s.Replace('\u0192', '\u00F8');
                    t.TextString = s;
                    t.Height = 10.0;
                    if (EtrDotPattern.IsMatch(s))
                    {
                        var p = t.Position;
                        t.Position = new Point3d(p.X + 45.0, p.Y, p.Z);
                    }
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    mt.Layer = LyrDonatiYazisi;
                    if (!yazı.IsNull) mt.TextStyleId = yazı;
                    var s = MTextPlainContents(mt);
                    while (s.Length > 0 && s[s.Length - 1] == '\u00FF')
                        s = s.Substring(0, s.Length - 1);
                    s = s.Replace('\u0192', '\u00F8');
                    mt.Contents = s;
                    mt.TextHeight = 10.0;
                    if (EtrDotPattern.IsMatch(s))
                    {
                        var p = mt.Location;
                        mt.Location = new Point3d(p.X + 45.0, p.Y, p.Z);
                    }
                }
            }
        }

        private static void AppendMTextDonatiSymbolCandidates(Transaction tr, Editor ed, List<ObjectId> ids)
        {
            var have = new HashSet<ObjectId>(ids);
            var cand = SelectAllIds(ed, new SelectionFilter(new[] { new TypedValue(0, "MTEXT") }));
            foreach (var id in cand)
            {
                if (have.Contains(id)) continue;
                if (tr.GetObject(id, OpenMode.ForRead) is MText mt)
                {
                    var s = MTextPlainContents(mt);
                    if (s.IndexOf('\u0192') >= 0 || s.IndexOf('\u00F8') >= 0)
                    {
                        ids.Add(id);
                        have.Add(id);
                    }
                }
            }
        }

        private static void ProcessDimLayTexts(Transaction tr, Database db, Editor ed)
        {
            var filt = new SelectionFilter(FilterTextOrMtextLayerOr("DIM_LAY", "DETAIL2", "REBAR2", "REBAR_DET2"));
            var ids = SelectAllIds(ed, filt);
            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrOlcuYazisi, 7, LineWeight.LineWeight020);
            var olcuStyle = TextStyleId(tr, db, StlOlcu);
            foreach (var id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    t.Layer = LyrOlcuYazisi;
                    t.Height = 10.0;
                    if (!olcuStyle.IsNull) t.TextStyleId = olcuStyle;
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    mt.Layer = LyrOlcuYazisi;
                    mt.TextHeight = 10.0;
                    if (!olcuStyle.IsNull) mt.TextStyleId = olcuStyle;
                }
            }
        }

        private static void ProcessDetail4KesitImi(Transaction tr, Database db, Editor ed)
        {
            var ids = SelectTextOrMtextOnLayerWithHeight(ed, tr, "DETAIL4", 20.0);
            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrKesitIsmi, 6, LineWeight.LineWeight020);
            var yazı = TextStyleId(tr, db, StlYazi);

            foreach (var id in ids)
            {
                Point3d p;
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    t.Layer = LyrKesitIsmi;
                    t.Height = 12.5;
                    t.Rotation = Math.PI / 2.0;
                    if (!yazı.IsNull) t.TextStyleId = yazı;
                    p = t.Position;
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    mt.Layer = LyrKesitIsmi;
                    mt.TextHeight = 12.5;
                    mt.Rotation = Math.PI / 2.0;
                    if (!yazı.IsNull) mt.TextStyleId = yazı;
                    p = mt.Location;
                }
                else
                    continue;

                var w1 = new Point3d(p.X, p.Y + 70.0, p.Z);
                var w2 = new Point3d(p.X + 50.0, p.Y - 50.0, p.Z);
                EraseLinesInWindow(ed, w1, w2, "DETAIL4");
            }
        }

        private static void ProcessRebarGeometry(Transaction tr, Database db, Editor ed)
        {
            var filt = new SelectionFilter(new[]
            {
                new TypedValue(-4, "<OR"),
                new TypedValue(0, "LINE"),
                new TypedValue(0, "CIRCLE"),
                new TypedValue(-4, "OR>"),
                new TypedValue(-4, "<OR"),
                new TypedValue(8, "REBAR"),
                new TypedValue(8, "DETAIL4"),
                new TypedValue(8, "REBAR_DET4"),
                new TypedValue(8, "BEAM4"),
                new TypedValue(-4, "OR>")
            });
            var ids = SelectAllIds(ed, filt);
            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrDonati, 4, LineWeight.LineWeight035);
            foreach (var id in ids)
            {
                var e = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                if (e == null) continue;
                e.Layer = LyrDonati;
            }
        }

        private static void ProcessDetail3KirisTexts(Transaction tr, Database db, Editor ed)
        {
            var ids = SelectTextOrMtextOnLayerWithHeight(ed, tr, "DETAIL3", 15.0);
            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrKirisIsmi, 40, LineWeight.LineWeight020);
            var yazı = TextStyleId(tr, db, StlYazi);
            foreach (var id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    t.Layer = LyrKirisIsmi;
                    t.Height = 12.5;
                    if (!yazı.IsNull) t.TextStyleId = yazı;
                    var p = t.Position;
                    t.Position = new Point3d(p.X + 25.0, p.Y + 7.5, p.Z);
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    mt.Layer = LyrKirisIsmi;
                    mt.TextHeight = 12.5;
                    if (!yazı.IsNull) mt.TextStyleId = yazı;
                    var p = mt.Location;
                    mt.Location = new Point3d(p.X + 25.0, p.Y + 7.5, p.Z);
                }
            }
        }

        private static void ProcessDetail3KirisLines(Transaction tr, Database db, Editor ed)
        {
            var ids = SelectAllIds(ed, new SelectionFilter(new[]
            {
                new TypedValue(0, "LINE"),
                new TypedValue(8, "DETAIL3")
            }));
            if (ids.Count == 0) return;

            EnsureLayer(tr, db, LyrKiris, 2, LineWeight.LineWeight030);
            foreach (var id in ids)
            {
                var ln = tr.GetObject(id, OpenMode.ForWrite) as Line;
                if (ln == null) continue;
                ln.Layer = LyrKiris;
            }
        }

        /// <summary>Belge kilidi çağıranın sorumluluğundadır (Execute tek LockDocument altında).</summary>
        private static void ReplaceDonatiCirclesWithBlock(Database db, Editor ed)
        {
            var circleIds = new HashSet<ObjectId>();
            foreach (var id in SelectAllIds(ed, new SelectionFilter(new[]
                     {
                         new TypedValue(0, "CIRCLE"),
                         new TypedValue(8, LyrDonati)
                     })))
                circleIds.Add(id);
            foreach (var id in SelectAllIds(ed, new SelectionFilter(new[]
                     {
                         new TypedValue(0, "CIRCLE"),
                         new TypedValue(8, LyrIDonatiOku)
                     })))
                circleIds.Add(id);

            var insertIds = new List<ObjectId>();
            foreach (var id in SelectAllIds(ed, new SelectionFilter(new[] { new TypedValue(0, "INSERT") })))
                insertIds.Add(id);

            if (circleIds.Count == 0 && insertIds.Count == 0)
                return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                EnsureKotKesDetDonatiSymbolLayers(tr, db);
                double refD = KirisDuzeltHardcodedSymbols.DonatiReferenceDiameter;
                if (refD < 1e-9)
                    refD = 1.0;

                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                var arrowMarkers = new List<ObjectId>();
                foreach (var iid in insertIds)
                {
                    var br = tr.GetObject(iid, OpenMode.ForRead, false) as BlockReference;
                    if (br == null)
                        continue;
                    string defName = BlockDefinitionRecordName(tr, br);
                    if (string.Equals(defName, "arrowblock", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(defName, "_Open30", StringComparison.OrdinalIgnoreCase))
                        arrowMarkers.Add(iid);
                }

                foreach (var cid in circleIds)
                {
                    var c = tr.GetObject(cid, OpenMode.ForRead, false) as Circle;
                    if (c == null)
                        continue;
                    double r = Math.Max(c.Radius, 0.01);
                    double s = (2.0 * r) / refD;
                    Point3d ins = c.Center + KirisBundleWcsOffset();
                    var mat = KirisDuzeltHardcodedSymbols.CreatePlacementMatrix(ins, s, 0.0);
                    KirisDuzeltHardcodedSymbols.AppendDonatiSymbol(tr, db, ms, mat);
                }

                foreach (var cid in circleIds)
                {
                    if (cid.IsErased)
                        continue;
                    (tr.GetObject(cid, OpenMode.ForWrite) as Entity)?.Erase();
                }

                foreach (var aid in arrowMarkers)
                {
                    var br = tr.GetObject(aid, OpenMode.ForRead, false) as BlockReference;
                    if (br == null)
                        continue;
                    double s = (2.0 * DonatiArrowMarkerEquivalentRadiusCm) / refD;
                    Point3d ins = br.Position + KirisBundleWcsOffset();
                    var mat = KirisDuzeltHardcodedSymbols.CreatePlacementMatrix(ins, s, br.Rotation);
                    KirisDuzeltHardcodedSymbols.AppendDonatiSymbol(tr, db, ms, mat);
                }

                foreach (var aid in arrowMarkers)
                {
                    if (aid.IsErased)
                        continue;
                    (tr.GetObject(aid, OpenMode.ForWrite) as Entity)?.Erase();
                }

                tr.Commit();
            }
        }

        private static string BlockDefinitionRecordName(Transaction tr, BlockReference br)
        {
            if (br == null)
                return "";
            try
            {
                ObjectId defId = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
                var btr = (BlockTableRecord)tr.GetObject(defId, OpenMode.ForRead);
                return btr.Name ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static void EnsureKotKesDetDonatiSymbolLayers(Transaction tr, Database db)
        {
            EnsureLayer(tr, db, KirisDuzeltHardcodedSymbols.LayerKotCizgi, 7, LineWeight.LineWeight020);
            EnsureLayer(tr, db, KirisDuzeltHardcodedSymbols.LayerKesit, KirisDuzeltHardcodedSymbols.LayerColorKesitDxf, LineWeight.LineWeight025);
            EnsureLayer(tr, db, KirisDuzeltHardcodedSymbols.LayerDonatiKesit, KirisDuzeltHardcodedSymbols.LayerColorDonatiKesitDxf, LineWeight.LineWeight020);
        }

        private static Vector3d KirisBundleWcsOffset()
        {
            return new Vector3d(-KirisBundleOffsetLeftCm, KirisBundleOffsetUpCm, 0.0);
        }

        private static Vector3d KirisKotKesDetPlacementOffset()
        {
            return new Vector3d(-KirisBundleOffsetLeftCm, KirisKotKesOffsetVerticalCm, 0.0);
        }

        /// <summary>KOT sembolu (InsertKot ile ayni); temizlik penceresi icin.</summary>
        private static Point3d ComputeKotSymbolInsertPoint(
            Point3d textIns,
            double textHeight,
            double rotationRad)
        {
            var rotMat = Matrix3d.Rotation(rotationRad, Vector3d.ZAxis, Point3d.Origin);
            Vector3d textUp = Vector3d.YAxis.TransformBy(rotMat);
            Vector3d textDown = -textUp;
            Point3d ins = textIns + textDown * (textHeight * KotGapBelowTextInTextHeights);
            ins = ins + new Vector3d(-KotPlacementOffsetLeftCm, -KotPlacementOffsetDownCm, 0.0);
            return ins + KirisKotKesDetPlacementOffset();
        }

        /// <summary>KES_DET sembolu (InsertKesDet ile ayni konum).</summary>
        private static Point3d ComputeKesDetSymbolInsertPoint(Point3d textIns)
        {
            Point3d ins = textIns + new Vector3d(KesDetPlacementOffsetRightCm, KesDetPlacementOffsetUpCm, 0.0);
            return ins + KirisKotKesDetPlacementOffset();
        }

        /// <summary>KOT yazisi + KOT sembolu cevresinde OLCU (BEYKENT) egri temizligi.</summary>
        private static void EraseOlcuClutterNearKotLabels(Database db, Editor ed)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ids = SelectTextOrMtextOnLayerWithHeight(ed, tr, LyrKotYazi, 10.0);
                foreach (var id in ids)
                {
                    Point3d p;
                    double th;
                    double rot;
                    if (tr.GetObject(id, OpenMode.ForRead) is DBText t)
                    {
                        p = t.Position;
                        th = t.Height;
                        rot = t.Rotation;
                    }
                    else if (tr.GetObject(id, OpenMode.ForRead) is MText mt)
                    {
                        p = mt.Location;
                        th = mt.TextHeight;
                        rot = mt.Rotation;
                    }
                    else
                        continue;

                    Point3d kIns = ComputeKotSymbolInsertPoint(p, th, rot);
                    double textSpanX = Math.Max(th * 8.0, 40.0);
                    double xMin = Math.Min(p.X - th, kIns.X) - OlcuCleanupPadLeftOfAnchorCm;
                    double xMax = Math.Max(p.X + textSpanX, kIns.X) + OlcuCleanupPadRightOfAnchorCm;
                    double yMin = Math.Min(p.Y - th * 2.0, kIns.Y) - OlcuCleanupPadBelowAnchorCm;
                    double yMax = Math.Max(p.Y + th * 2.5, kIns.Y) + OlcuCleanupPadAboveAnchorCm;
                    var w1 = new Point3d(xMin, yMin, p.Z);
                    var w2 = new Point3d(xMax, yMax, p.Z);
                    TryEraseCurvesOnLayerInCrossingWindow(ed, tr, w1, w2, LyrOlcu);
                }

                tr.Commit();
            }
        }

        /// <summary>
        /// STA donati artigi: yalnizca KES_DET yerel dik cizgisinin (KESIT katmani, x=13.5) sagindaki
        /// <see cref="KesDetDonatiStaJunkStripWidthCm"/> cm seritteki DONATI LINE ogeleri (WCS kutusu donusumle).
        /// </summary>
        private static void EraseDonatiClutterRightOfKesDetLabels(Database db, Editor ed)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ids = SelectTextOrMtextOnLayerWithHeight(ed, tr, LyrKesitIsmi, 12.5);
                foreach (var id in ids)
                {
                    Point3d p;
                    double th;
                    double textRot;
                    if (tr.GetObject(id, OpenMode.ForRead) is DBText t)
                    {
                        p = t.Position;
                        th = t.Height;
                        textRot = t.Rotation;
                    }
                    else if (tr.GetObject(id, OpenMode.ForRead) is MText mt)
                    {
                        p = mt.Location;
                        th = mt.TextHeight;
                        textRot = mt.Rotation;
                    }
                    else
                        continue;

                    TryEraseStaDonatiLinesInKesDetVerticalStrip(ed, tr, p, th, textRot);
                }

                tr.Commit();
            }
        }

        /// <summary>InsertKesDetBlockAtSectionText ile ayni matris; dik cizgi segmentinin sagina dar serit.</summary>
        private static void TryEraseStaDonatiLinesInKesDetVerticalStrip(
            Editor ed,
            Transaction tr,
            Point3d textIns,
            double textHeight,
            double rotationRad)
        {
            double hContent = KirisDuzeltHardcodedSymbols.KesDetReferenceHeight;
            if (hContent < 1e-9)
                hContent = 1.0;
            double s = (textHeight * KesDetSymbolHeightVsText) / hContent;
            if (s < 1e-12)
                return;

            Point3d ins = ComputeKesDetSymbolInsertPoint(textIns);
            double rot = rotationRad + KesDetExtraRotationDeg * (Math.PI / 180.0);
            var mat = KirisDuzeltHardcodedSymbols.CreatePlacementMatrix(ins, s, rot);

            double xLine = KirisDuzeltHardcodedSymbols.KesDetReferenceWidth;
            double dxDef = KesDetDonatiStaJunkStripWidthCm / s;
            double padDef = KesDetDonatiStaJunkStripYPadCm / s;
            double y0 = -KirisDuzeltHardcodedSymbols.KesDetReferenceHeight - padDef;
            double y1 = padDef;

            var c0 = new Point3d(xLine, y0, 0.0).TransformBy(mat);
            var c1 = new Point3d(xLine + dxDef, y0, 0.0).TransformBy(mat);
            var c2 = new Point3d(xLine + dxDef, y1, 0.0).TransformBy(mat);
            var c3 = new Point3d(xLine, y1, 0.0).TransformBy(mat);

            double xmin = Math.Min(Math.Min(c0.X, c1.X), Math.Min(c2.X, c3.X));
            double xmax = Math.Max(Math.Max(c0.X, c1.X), Math.Max(c2.X, c3.X));
            double ymin = Math.Min(Math.Min(c0.Y, c1.Y), Math.Min(c2.Y, c3.Y));
            double ymax = Math.Max(Math.Max(c0.Y, c1.Y), Math.Max(c2.Y, c3.Y));
            var w1 = new Point3d(xmin, ymin, c0.Z);
            var w2 = new Point3d(xmax, ymax, c0.Z);
            TryEraseStaDonatiJunkLinesInCrossingWindow(ed, tr, w1, w2, LyrDonati);
        }

        private static bool MatchesStaDonatiJunkLineLength(double lengthCm)
        {
            if (lengthCm <= 0.0 || double.IsNaN(lengthCm) || double.IsInfinity(lengthCm))
                return false;
            for (int i = 0; i < KesDetStaDonatiJunkLineLengthsCm.Length; i++)
            {
                if (Math.Abs(lengthCm - KesDetStaDonatiJunkLineLengthsCm[i]) <= KesDetStaDonatiJunkLengthTolCm)
                    return true;
            }
            return false;
        }

        private static void TryEraseStaDonatiJunkLinesInCrossingWindow(
            Editor ed,
            Transaction tr,
            Point3d corner1,
            Point3d corner2,
            string layerName)
        {
            try
            {
                var filt = new SelectionFilter(new[]
                {
                    new TypedValue(-4, "<AND"),
                    new TypedValue(0, "LINE"),
                    new TypedValue(8, layerName),
                    new TypedValue(-4, "AND>")
                });
                var r = ed.SelectCrossingWindow(corner1, corner2, filt);
                if (r.Status != PromptStatus.OK || r.Value == null)
                    return;
                foreach (var oid in r.Value.GetObjectIds())
                {
                    if (oid.IsErased)
                        continue;
                    var ln = tr.GetObject(oid, OpenMode.ForRead, false) as Line;
                    if (ln == null)
                        continue;
                    double len = ln.StartPoint.DistanceTo(ln.EndPoint);
                    if (!MatchesStaDonatiJunkLineLength(len))
                        continue;
                    (tr.GetObject(oid, OpenMode.ForWrite) as Entity)?.Erase();
                }
            }
            catch
            {
                // ignored
            }
        }

        private static void TryEraseCurvesOnLayerInCrossingWindow(
            Editor ed,
            Transaction tr,
            Point3d corner1,
            Point3d corner2,
            string layerName)
        {
            try
            {
                var filt = new SelectionFilter(new[]
                {
                    new TypedValue(-4, "<AND"),
                    new TypedValue(-4, "<OR"),
                    new TypedValue(0, "LINE"),
                    new TypedValue(0, "LWPOLYLINE"),
                    new TypedValue(0, "POLYLINE"),
                    new TypedValue(0, "ARC"),
                    new TypedValue(0, "SPLINE"),
                    new TypedValue(0, "ELLIPSE"),
                    new TypedValue(-4, "OR>"),
                    new TypedValue(8, layerName),
                    new TypedValue(-4, "AND>")
                });
                var r = ed.SelectCrossingWindow(corner1, corner2, filt);
                if (r.Status != PromptStatus.OK || r.Value == null)
                    return;
                foreach (var oid in r.Value.GetObjectIds())
                {
                    if (oid.IsErased)
                        continue;
                    (tr.GetObject(oid, OpenMode.ForWrite) as Entity)?.Erase();
                }
            }
            catch
            {
                // ignored
            }
        }

        private static void InsertKesDetAtSectionLabels(Database db, Editor ed)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                EnsureKotKesDetDonatiSymbolLayers(tr, db);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                var ids = SelectTextOrMtextOnLayerWithHeight(ed, tr, LyrKesitIsmi, 12.5);
                foreach (var id in ids)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is DBText t)
                        InsertKesDetBlockAtSectionText(tr, db, ms, t.Position, t.Height, t.Rotation);
                    else if (tr.GetObject(id, OpenMode.ForRead) is MText mt)
                        InsertKesDetBlockAtSectionText(tr, db, ms, mt.Location, mt.TextHeight, mt.Rotation);
                }
                tr.Commit();
            }
        }

        private static void InsertKotAtKotLabels(Database db, Editor ed)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                EnsureKotKesDetDonatiSymbolLayers(tr, db);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                var ids = SelectTextOrMtextOnLayerWithHeight(ed, tr, LyrKotYazi, 10.0);
                foreach (var id in ids)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is DBText t)
                        InsertKotBlockBelowText(tr, db, ms, t.Position, t.Height, t.Rotation);
                    else if (tr.GetObject(id, OpenMode.ForRead) is MText mt)
                        InsertKotBlockBelowText(tr, db, ms, mt.Location, mt.TextHeight, mt.Rotation);
                }
                tr.Commit();
            }
        }

        /// <summary>KOT sembolu (gömülü geometri); yazinin metin yonunde asagisina olceklenir.</summary>
        private static void InsertKotBlockBelowText(
            Transaction tr,
            Database db,
            BlockTableRecord ms,
            Point3d textIns,
            double textHeight,
            double rotationRad)
        {
            double hContent = KirisDuzeltHardcodedSymbols.KotReferenceHeight;
            if (hContent < 1e-9)
                hContent = 1.0;
            double s = (textHeight * KotSymbolHeightVsText) / hContent;

            Point3d ins = ComputeKotSymbolInsertPoint(textIns, textHeight, rotationRad);
            var mat = KirisDuzeltHardcodedSymbols.CreatePlacementMatrix(ins, s, rotationRad);
            KirisDuzeltHardcodedSymbols.AppendKotSymbol(tr, db, ms, mat);
        }

        /// <summary>KES_DET: gömülü geometri; yazinin insert noktasinda, yazi yuksekligine gore olcekli.</summary>
        private static void InsertKesDetBlockAtSectionText(
            Transaction tr,
            Database db,
            BlockTableRecord ms,
            Point3d textIns,
            double textHeight,
            double rotationRad)
        {
            double hContent = KirisDuzeltHardcodedSymbols.KesDetReferenceHeight;
            if (hContent < 1e-9)
                hContent = 1.0;
            double s = (textHeight * KesDetSymbolHeightVsText) / hContent;

            Point3d ins = ComputeKesDetSymbolInsertPoint(textIns);
            double rot = rotationRad + KesDetExtraRotationDeg * (Math.PI / 180.0);
            var mat = KirisDuzeltHardcodedSymbols.CreatePlacementMatrix(ins, s, rot);
            KirisDuzeltHardcodedSymbols.AppendKesDetSymbol(tr, db, ms, mat);
        }
    }
}
