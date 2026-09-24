using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// TKIRIS_006.lsp (C:T_KIRIS) ile aynı iş akışı: STA temel kiriş/kesit DWG’de
    /// Beykent katman/stil düzenlemesi. DONATI / KOT / KES_DET dışarıdan blok alınmaz;
    /// <see cref="KirisDuzeltHardcodedSymbols"/> ile yeniden çizilir.
    /// Komut: ST4 + ham DWG → düzelt → ana antete (SheetViewOut sol-altın 50 cm solu) yerleştir.
    /// </summary>
    internal static class TemelKirisDuzeltRunner
    {
        private const string LyrTemel = "TEMEL (BEYKENT)";
        private const string LyrTemelIsmi = "TEMEL ISMI (BEYKENT)";
        private const string LyrKolonIsmi = "KOLON ISMI (BEYKENT)";
        private const string LyrKotYazi = "KOT YAZI (BEYKENT)";
        private const string LyrFiliz = "FILIZ (BEYKENT)";
        private const string LyrYazi = "YAZI (BEYKENT)";
        private const string LyrIDonatiOku = "I.DONATI OKU (BEYKENT)";
        private const string LyrKesitSiniri = "KESIT SINIRI (BEYKENT)";
        private const string LyrOlcu = "OLCU (BEYKENT)";
        private const string LyrOlcuYazisi = "OLCU YAZISI (BEYKENT)";
        private const string LyrDonatiYazisi = "DONATI YAZISI (BEYKENT)";
        private const string LyrDonati = "DONATI (BEYKENT)";
        private const string LyrKesitIsmi = "KESIT ISMI (BEYKENT)";
        private const string StlYazi = "YAZI (BEYKENT)";
        private const string StlKot = "KOT (BEYKENT)";
        private const string StlOlcu = "OLCU (BEYKENT)";

        private const string FilizTrimCharset = "\u00FFl=1234567890 ";

        /// <summary>
        /// ST4 + düzeltilmemiş temel kiriş DWG: ReadDwgFile ile oku → hosta klonla →
        /// aynı belgede düzelt (ikinci Open yok) → ana antete yerleştir.
        /// </summary>
        public static void ExecuteFromFiles(Document hostDoc, string st4Path, string dwgPath, Point3d insertLl)
        {
            if (hostDoc == null) return;
            var hostEd = hostDoc.Editor;
            if (string.IsNullOrWhiteSpace(st4Path) || !File.Exists(st4Path))
            {
                hostEd.WriteMessage("\nTEMELKIRISDUZELT: ST4 dosyasi yok.");
                return;
            }
            if (string.IsNullOrWhiteSpace(dwgPath) || !File.Exists(dwgPath))
            {
                hostEd.WriteMessage("\nTEMELKIRISDUZELT: DWG dosyasi yok.");
                return;
            }

            HashSet<ObjectId> idsBeforeImport = null;
            List<ObjectId> clonedIds = null;
            try
            {
                using (hostDoc.LockDocument())
                {
                    var db = hostDoc.Database;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        idsBeforeImport = SnapshotModelSpaceIds(tr, btr);
                        clonedIds = CloneDwgModelSpaceInto(dwgPath, db, tr, btr, hostEd);
                        if (clonedIds == null || clonedIds.Count == 0)
                        {
                            hostEd.WriteMessage("\nTEMELKIRISDUZELT: DWG Model Space bos veya okunamadi.");
                            tr.Abort();
                            return;
                        }
                        tr.Commit();
                    }
                }

                hostEd.WriteMessage("\nTEMELKIRISDUZELT: {0} oge klonlandi, duzeltme basliyor...", clonedIds.Count);
                Execute(hostDoc, new HashSet<ObjectId>(clonedIds));

                using (hostDoc.LockDocument())
                {
                    var db = hostDoc.Database;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        var contentIds = CollectNewModelSpaceIds(tr, btr, idsBeforeImport);
                        if (contentIds.Count == 0)
                        {
                            hostEd.WriteMessage("\nTEMELKIRISDUZELT: duzeltme sonrasi icerik yok.");
                            tr.Abort();
                            return;
                        }

                        St4Model model;
                        try { model = new St4Parser().Parse(st4Path); }
                        catch (System.Exception ex)
                        {
                            hostEd.WriteMessage("\nTEMELKIRISDUZELT ST4 okuma hatasi: {0}", ex.Message);
                            tr.Abort();
                            return;
                        }

                        var mgr = new PlanIdDrawingManager(model);
                        bool ok = mgr.PlaceContentInAnaAntet(
                            tr, btr, contentIds, insertLl, st4Path, hostEd, "TEMEL KIRIS ACILIMLARI");
                        if (ok)
                        {
                            tr.Commit();
                            hostEd.WriteMessage("\nTEMELKIRISDUZELT: duzeltilen acilim ana antete yerlestirildi ({0} oge).", contentIds.Count);
                        }
                        else
                        {
                            tr.Abort();
                            hostEd.WriteMessage("\nTEMELKIRISDUZELT: antet yerlesimi basarisiz (gomulu antet kontrol edin).");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                hostEd.WriteMessage("\nTEMELKIRISDUZELT hata: {0}", ex.Message);
            }
            finally
            {
                KirisDuzeltRunner.SelectionScope = null;
            }
        }

        internal static HashSet<ObjectId> SnapshotModelSpaceIds(Transaction tr, BlockTableRecord ms)
        {
            var set = new HashSet<ObjectId>();
            foreach (ObjectId id in ms)
            {
                if (id.IsValid && !id.IsErased)
                    set.Add(id);
            }
            return set;
        }

        internal static List<ObjectId> CollectNewModelSpaceIds(
            Transaction tr, BlockTableRecord ms, HashSet<ObjectId> before)
        {
            var list = new List<ObjectId>();
            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased) continue;
                if (before != null && before.Contains(id)) continue;
                list.Add(id);
            }
            return list;
        }

        internal static List<ObjectId> CloneDwgModelSpaceInto(
            string dwgPath, Database dstDb, Transaction dstTr, BlockTableRecord dstMs, Editor ed)
        {
            Database srcDb = null;
            try
            {
                srcDb = new Database(false, true);
                srcDb.ReadDwgFile(dwgPath, FileOpenMode.OpenForReadAndAllShare, true, null);
                srcDb.CloseInput(true);
                return CloneModelSpaceRootEntities(srcDb, dstDb, dstTr, dstMs);
            }
            catch (System.Exception ex)
            {
                ed?.WriteMessage("\nDWG okuma hatasi: {0}", ex.Message);
                return null;
            }
            finally
            {
                try { srcDb?.Dispose(); } catch { }
            }
        }

        private static List<ObjectId> CloneModelSpaceRootEntities(
            Database srcDb, Database dstDb, Transaction dstTr, BlockTableRecord dstMs)
        {
            var result = new List<ObjectId>();
            if (srcDb == null || dstDb == null || dstTr == null || dstMs == null)
                return result;

            var ids = new ObjectIdCollection();
            ObjectId srcMsId;
            using (var trSrc = srcDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)trSrc.GetObject(srcDb.BlockTableId, OpenMode.ForRead);
                srcMsId = bt[BlockTableRecord.ModelSpace];
                var ms = (BlockTableRecord)trSrc.GetObject(srcMsId, OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsValid && !id.IsErased)
                        ids.Add(id);
                }
                trSrc.Commit();
            }
            if (ids.Count == 0) return result;

            var mapping = new IdMapping();
            dstDb.WblockCloneObjects(ids, dstMs.ObjectId, mapping, DuplicateRecordCloning.Ignore, false);

            using (var trSrc = srcDb.TransactionManager.StartTransaction())
            {
                var sourceMs = (BlockTableRecord)trSrc.GetObject(srcMsId, OpenMode.ForRead);
                foreach (IdPair pair in mapping)
                {
                    if (!pair.Key.IsValid || !pair.Value.IsValid) continue;
                    var entSrc = trSrc.GetObject(pair.Key, OpenMode.ForRead) as Entity;
                    if (entSrc == null) continue;
                    if (!entSrc.BlockId.Equals(sourceMs.ObjectId)) continue;
                    if (pair.Value.IsErased) continue;
                    result.Add(pair.Value);
                }
                trSrc.Commit();
            }
            return result;
        }

        /// <summary>Execute başındaki MS anlığı; sonradan eklenen semboller SelectionScope’a alınır.</summary>
        private static HashSet<ObjectId> _msIdsAtExecuteStart;

        private static void AbsorbNewModelSpaceIntoSelectionScope(Database db)
        {
            if (KirisDuzeltRunner.SelectionScope == null || _msIdsAtExecuteStart == null) return;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!_msIdsAtExecuteStart.Contains(id))
                        KirisDuzeltRunner.SelectionScope.Add(id);
                }
                tr.Commit();
            }
        }

        public static void Execute(Document doc, HashSet<ObjectId> selectionScope = null)

        {
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            KirisDuzeltRunner.SelectionScope = selectionScope;
            _msIdsAtExecuteStart = null;
            object oldOsmode = null;
            object oldCmdecho = null;
            object oldBlip = null;
            try
            {
                using (var trSnap = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)trSnap.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)trSnap.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    _msIdsAtExecuteStart = SnapshotModelSpaceIds(trSnap, ms);
                    trSnap.Commit();
                }

                try { oldOsmode = Application.GetSystemVariable("OSMODE"); } catch { }
                try { oldCmdecho = Application.GetSystemVariable("CMDECHO"); } catch { }
                try { oldBlip = Application.GetSystemVariable("BLIPMODE"); } catch { }
                try { Application.SetSystemVariable("OSMODE", (short)0); } catch { }
                try { Application.SetSystemVariable("CMDECHO", (short)0); } catch { }
                try { Application.SetSystemVariable("BLIPMODE", (short)0); } catch { }

                ed.Command("_.UNDO", "_GROUP");

                AcadDocumentViewUtil.ZoomExtentsWithoutNestedCommand(doc);
                KirisDuzeltRunner.RunOverkillNotColor4(ed);
                KirisDuzeltRunner.EraseZeroLengthLines(db, ed);
                KirisDuzeltRunner.SetAllTextWidthFactorOne(ed);

                AcadDocumentViewUtil.ZoomExtentsWithoutNestedCommand(doc);
                try { Application.SetSystemVariable("LTSCALE", 0.5); } catch { }
                ed.Command("_.UNITS", "", "", "", "", "", "N");

                KirisDuzeltRunner.TryApplyStaDrawingScale(doc, db, ed);
                AcadDocumentViewUtil.ZoomExtentsWithoutNestedCommand(doc);

                List<ObjectId> axis2Ids;
                using (doc.LockDocument())
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        KirisDuzeltRunner.EnsureTextStyles(tr, db);
                        KirisDuzeltRunner.EnsureDashedLinetypeLoaded(db, tr);
                        tr.Commit();
                    }

                    axis2Ids = new List<ObjectId>();
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        // AKS balonu + aks ismi yazısı çizilmesin (sil).
                        EraseAxisBalloonsAndNames(tr, ed);
                        axis2Ids = KirisDuzeltRunner.ProcessAxis2Lines(tr, db, ed);
                        ProcessTemelIsmiBeam4(tr, db, ed);
                        ProcessTemelLinesBeam3(tr, db, ed);
                        ProcessKotDetail2(tr, db, ed);
                        ProcessKolonIsmiBeam4(tr, db, ed);
                        ProcessFilizTexts(tr, db, ed, "DETAIL3", 200.0, -5.0);
                        ProcessFilizTexts(tr, db, ed, "REBAR3", -50.0, 5.0);
                        ProcessExactText(tr, db, ed, "DETAIL3", 15.0, "BAP", LyrYazi, StlYazi, null, 140);
                        ProcessExactText(tr, db, ed, "BEAM3", 12.5, "TEMEL HATILI", LyrYazi, StlYazi, 15.0, 140);
                        tr.Commit();
                    }

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        RemapLines(tr, db, ed, "DETAIL2", LyrIDonatiOku, 160, LineWeight.LineWeight020);
                        RemapLines(tr, db, ed, "REBAR2", LyrIDonatiOku, 160, LineWeight.LineWeight020);
                        RemapLines(tr, db, ed, "BEAM2", LyrKesitSiniri, 241, LineWeight.LineWeight020, "DASHED");
                        RemapLines(tr, db, ed, "DIM_LAY", LyrOlcu, 14, LineWeight.LineWeight020);
                        ProcessDonatiYazisi(tr, db, ed, "REBAR3", 12.5, 10.0);
                        ProcessDonatiYazisi(tr, db, ed, "REBAR3", 10.0, null);
                        ProcessDonatiYazisi(tr, db, ed, "DETAIL3", 12.5, 10.0);
                        ProcessDonatiYazisi(tr, db, ed, "BEAM3", 12.5, 10.0);
                        ProcessDonatiYazisi(tr, db, ed, "REBAR_DET3", 15.0, 12.5);
                        ProcessDonatiYazisi(tr, db, ed, "REBAR_DET3", 12.5, 10.0);
                        ProcessOlcuYazisi(tr, db, ed, "DIM_LAY", 12.5, 10.0);
                        ProcessOlcuYazisi(tr, db, ed, "REBAR_DET2", 10.0, null);
                        ProcessOlcuYazisi(tr, db, ed, "REBAR2", 12.5, 10.0);
                        RemapLines(tr, db, ed, "REBAR", LyrDonati, 4, LineWeight.LineWeight035);
                        RemapLines(tr, db, ed, "REBAR_DET4", LyrDonati, 4, LineWeight.LineWeight035);
                        RemapLines(tr, db, ed, "BEAM4", LyrDonati, 4, LineWeight.LineWeight035);
                        RemapCircles(tr, db, ed, "DETAIL4", LyrDonati, 4, LineWeight.LineWeight035);
                        ProcessKesitiTexts(tr, db, ed);
                        ProcessKesitTemelYazisiVeOlcuKaydir(tr, db, ed);
                        ProcessKesitImi(tr, db, ed);
                        RemapLines(tr, db, ed, "DETAIL3", LyrTemel, 2, LineWeight.LineWeight030);
                        RemapLines(tr, db, ed, "DETAIL4", LyrDonati, 4, LineWeight.LineWeight035);
                        tr.Commit();
                    }

                    KirisDuzeltRunner.ReplaceDonatiCirclesWithBlock(db, ed);
                    KirisDuzeltRunner.InsertKesDetAtSectionLabels(db, ed);
                    KirisDuzeltRunner.InsertKotAtKotLabels(db, ed);
                    AbsorbNewModelSpaceIntoSelectionScope(db);
                    EraseOldKotLinesOnIDonatiOkuNearKotLabels(db, ed);
                    EraseOldKesitSymbolsOnDonatiNearKesitLabels(db, ed);
                    EraseKolonIsmiYanindakiDonatiOkuVeFiliz(db, ed);
                    EraseAcilimSagindakiMetrajVeTablolar(db, ed);
                    CompactTemelKirisRowsVertically(db, ed);

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        KirisDuzeltRunner.ApplyByLayerPropertiesToEntireDatabase(tr, db);
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

                ed.WriteMessage("\nTEMELKIRISDUZELT tamamlandi.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nTEMELKIRISDUZELT hata: {0}", ex.Message);
            }
            finally
            {
                try { if (oldOsmode != null) Application.SetSystemVariable("OSMODE", oldOsmode); } catch { }
                try { if (oldCmdecho != null) Application.SetSystemVariable("CMDECHO", oldCmdecho); } catch { }
                try { if (oldBlip != null) Application.SetSystemVariable("BLIPMODE", oldBlip); } catch { }
                try { ed.Command("_.UNDO", "_END"); } catch { }
                KirisDuzeltRunner.SelectionScope = null;
                _msIdsAtExecuteStart = null;
            }
        }

        /// <summary>STA AXIS3 daire (aks balonu) ve AXIS4 yazı (aks no) — çizilmez, silinir. AKS çizgisi kalır.</summary>
        internal static void EraseAxisBalloonsAndNames(Transaction tr, Editor ed)
        {
            var erase = new List<ObjectId>();
            foreach (var id in KirisDuzeltRunner.SelectAllIds(ed, new SelectionFilter(new[]
                     {
                         new TypedValue(0, "CIRCLE"),
                         new TypedValue(8, "AXIS3")
                     })))
                erase.Add(id);
            foreach (var id in KirisDuzeltRunner.SelectAllIds(ed, new SelectionFilter(
                         KirisDuzeltRunner.FilterTextOrMtext(new TypedValue(8, "AXIS4")))))
                erase.Add(id);
            // Daha önce Beykent'e çevrilmiş eski çalıştırmalar
            foreach (var id in KirisDuzeltRunner.SelectAllIds(ed, new SelectionFilter(new[]
                     {
                         new TypedValue(0, "CIRCLE"),
                         new TypedValue(8, "AKS BALONU (BEYKENT)")
                     })))
                erase.Add(id);
            foreach (var lyr in new[] { "AKS NO (BEYKENT)", "AKS YAZISI (BEYKENT)" })
            {
                foreach (var id in KirisDuzeltRunner.SelectAllIds(ed, new SelectionFilter(
                             KirisDuzeltRunner.FilterTextOrMtext(new TypedValue(8, lyr)))))
                    erase.Add(id);
            }

            foreach (var id in erase)
            {
                if (id.IsErased) continue;
                (tr.GetObject(id, OpenMode.ForWrite) as Entity)?.Erase();
            }
        }

        private static void ProcessTemelIsmiBeam4(Transaction tr, Database db, Editor ed)
        {
            var ids = KirisDuzeltRunner.SelectTextOrMtextOnLayerWithHeight(ed, tr, "BEAM4", 25.0);
            if (ids.Count == 0) return;
            KirisDuzeltRunner.EnsureLayer(tr, db, LyrTemelIsmi, 40, LineWeight.LineWeight020);
            var style = KirisDuzeltRunner.TextStyleId(tr, db, StlYazi);
            foreach (var id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    var p = t.Position;
                    t.Position = new Point3d(p.X, p.Y - 20.0, p.Z);
                    t.Layer = LyrTemelIsmi;
                    t.Height = 20.0;
                    if (!style.IsNull) t.TextStyleId = style;
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    var p = mt.Location;
                    mt.Location = new Point3d(p.X, p.Y - 20.0, p.Z);
                    mt.Layer = LyrTemelIsmi;
                    mt.TextHeight = 20.0;
                    if (!style.IsNull) mt.TextStyleId = style;
                }
            }
        }

        private static void ProcessTemelLinesBeam3(Transaction tr, Database db, Editor ed)
        {
            RemapLines(tr, db, ed, "BEAM3", LyrTemel, 2, LineWeight.LineWeight030);
        }

        private static void ProcessKotDetail2(Transaction tr, Database db, Editor ed)
        {
            var ids = KirisDuzeltRunner.SelectAllIds(ed, new SelectionFilter(
                KirisDuzeltRunner.FilterTextOrMtext(new TypedValue(8, "DETAIL2"))));
            if (ids.Count == 0) return;

            KirisDuzeltRunner.EnsureLayer(tr, db, LyrKotYazi, 7, LineWeight.LineWeight020);
            var kotStyle = KirisDuzeltRunner.TextStyleId(tr, db, StlKot);
            foreach (var id in ids)
            {
                Point3d p;
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    p = t.Position;
                    // LISP penceresi + sağdaki eski kot çatallanması (crossing).
                    KirisDuzeltRunner.TryEraseCurvesOnLayerInCrossingWindow(ed, tr,
                        new Point3d(p.X - 80.0, p.Y - 80.0, p.Z),
                        new Point3d(p.X + 90.0, p.Y + 20.0, p.Z),
                        "DETAIL2");
                    t.Position = new Point3d(p.X - 50.0, p.Y - 35.0, p.Z);
                    t.Layer = LyrKotYazi;
                    t.Height = 10.0;
                    if (!kotStyle.IsNull) t.TextStyleId = kotStyle;
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    p = mt.Location;
                    KirisDuzeltRunner.TryEraseCurvesOnLayerInCrossingWindow(ed, tr,
                        new Point3d(p.X - 80.0, p.Y - 80.0, p.Z),
                        new Point3d(p.X + 90.0, p.Y + 20.0, p.Z),
                        "DETAIL2");
                    mt.Location = new Point3d(p.X - 50.0, p.Y - 35.0, p.Z);
                    mt.Layer = LyrKotYazi;
                    mt.TextHeight = 10.0;
                    if (!kotStyle.IsNull) mt.TextStyleId = kotStyle;
                }
            }
        }

        /// <summary>
        /// Yeni KOT sembolü yanında kalan eski STA kot çizgileri DETAIL2→I.DONATI OKU’ya taşınmış olabilir.
        /// Yalnızca kot yazısı yakınındaki kısa çizgileri sil; uzak/uzun donatı oklarına dokunma.
        /// </summary>
        private const double OldKotCleanupPadLeftCm = 70.0;
        private const double OldKotCleanupPadRightCm = 100.0;
        private const double OldKotCleanupPadBelowCm = 50.0;
        private const double OldKotCleanupPadAboveCm = 35.0;
        /// <summary>Eski kot parçaları kısa; gerçek I.DONATI okları genelde daha uzun.</summary>
        private const double OldKotMaxLineLengthCm = 95.0;

        private static void EraseOldKotLinesOnIDonatiOkuNearKotLabels(Database db, Editor ed)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var kotIds = KirisDuzeltRunner.SelectAllIds(ed, new SelectionFilter(
                    KirisDuzeltRunner.FilterTextOrMtext(new TypedValue(8, LyrKotYazi))));
                var erase = new HashSet<ObjectId>();
                foreach (var kid in kotIds)
                {
                    Point3d p;
                    double th;
                    if (tr.GetObject(kid, OpenMode.ForRead) is DBText t)
                    {
                        p = t.Position;
                        th = t.Height;
                    }
                    else if (tr.GetObject(kid, OpenMode.ForRead) is MText mt)
                    {
                        p = mt.Location;
                        th = mt.TextHeight;
                    }
                    else continue;

                    double textSpan = Math.Max(th * 8.0, 35.0);
                    var w1 = new Point3d(p.X - OldKotCleanupPadLeftCm, p.Y - OldKotCleanupPadBelowCm, p.Z);
                    var w2 = new Point3d(p.X + textSpan + OldKotCleanupPadRightCm, p.Y + OldKotCleanupPadAboveCm, p.Z);

                    try
                    {
                        var filt = new SelectionFilter(new[]
                        {
                            new TypedValue(0, "LINE"),
                            new TypedValue(8, LyrIDonatiOku)
                        });
                        var r = ed.SelectCrossingWindow(w1, w2, filt);
                        if (r.Status != PromptStatus.OK || r.Value == null) continue;

                        double xMin = Math.Min(w1.X, w2.X);
                        double xMax = Math.Max(w1.X, w2.X);
                        double yMin = Math.Min(w1.Y, w2.Y);
                        double yMax = Math.Max(w1.Y, w2.Y);

                        foreach (ObjectId oid in r.Value.GetObjectIds())
                        {
                            if (oid.IsErased) continue;
                            var ln = tr.GetObject(oid, OpenMode.ForRead) as Line;
                            if (ln == null) continue;
                            double len = ln.StartPoint.DistanceTo(ln.EndPoint);
                            if (len > OldKotMaxLineLengthCm) continue;
                            // Orta nokta kutu içinde olmalı (uzun okun sadece ucu değmesin).
                            var mid = new Point3d(
                                0.5 * (ln.StartPoint.X + ln.EndPoint.X),
                                0.5 * (ln.StartPoint.Y + ln.EndPoint.Y),
                                0);
                            if (mid.X < xMin || mid.X > xMax || mid.Y < yMin || mid.Y > yMax)
                                continue;
                            erase.Add(oid);
                        }
                    }
                    catch { }
                }

                foreach (var oid in erase)
                {
                    if (oid.IsErased) continue;
                    (tr.GetObject(oid, OpenMode.ForWrite) as Entity)?.Erase();
                }
                tr.Commit();
            }
        }

        private static void ProcessKolonIsmiBeam4(Transaction tr, Database db, Editor ed)
        {
            var ids = KirisDuzeltRunner.SelectTextOrMtextOnLayerWithHeight(ed, tr, "BEAM4", 17.5);
            if (ids.Count == 0) return;
            KirisDuzeltRunner.EnsureLayer(tr, db, LyrKolonIsmi, 91, LineWeight.LineWeight020);
            var style = KirisDuzeltRunner.TextStyleId(tr, db, StlYazi);
            foreach (var id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    t.Layer = LyrKolonIsmi;
                    t.Height = 15.0;
                    if (!style.IsNull) t.TextStyleId = style;
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    mt.Layer = LyrKolonIsmi;
                    mt.TextHeight = 15.0;
                    if (!style.IsNull) mt.TextStyleId = style;
                }
            }
        }

        /// <summary>
        /// Kolon ismi (SB-xx) hemen altındaki kolon demiri ok çizgisi (I.DONATI OKU / DONATI OKU)
        /// ve sağındaki kolon filiz yazı+çizgileri (FILIZ). Altta etriye oklarına / uzak filizlere dokunma.
        /// </summary>
        private const double KolonOkuBelowMaxCm = 28.0;
        private const double KolonOkuAboveMaxCm = 12.0;
        private const double KolonOkuSidePadCm = 90.0;
        private const double KolonOkuMinLenCm = 15.0;
        private const double KolonOkuMaxLenCm = 280.0;
        /// <summary>Silinen yatay okun uçlarındaki &lt; şeklinde kısa ok başı parçaları.</summary>
        private const double KolonOkuArrowHeadMaxLenCm = 22.0;
        private const double KolonFilizRightCm = 220.0;
        private const double KolonFilizLeftCm = 40.0;
        private const double KolonFilizVertCm = 45.0;

        private static readonly string[] KolonDonatiOkuLayers =
        {
            "I.DONATI OKU (BEYKENT)",
            "DONATI OKU (BEYKENT)"
        };

        private static void EraseKolonIsmiYanindakiDonatiOkuVeFiliz(Database db, Editor ed)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var kolonIds = KirisDuzeltRunner.SelectAllIds(ed, new SelectionFilter(
                    KirisDuzeltRunner.FilterTextOrMtext(new TypedValue(8, LyrKolonIsmi))));
                var erase = new HashSet<ObjectId>();

                foreach (var kid in kolonIds)
                {
                    Point3d p;
                    double th;
                    string name;
                    if (tr.GetObject(kid, OpenMode.ForRead) is DBText t)
                    {
                        p = t.Position;
                        th = t.Height;
                        name = t.TextString ?? "";
                    }
                    else if (tr.GetObject(kid, OpenMode.ForRead) is MText mt)
                    {
                        p = mt.Location;
                        th = mt.TextHeight;
                        name = KirisDuzeltRunner.MTextPlainContents(mt);
                    }
                    else continue;

                    double nameW = Math.Max(th * Math.Max(name.Trim().Length, 4) * 0.55, th * 4.0);

                    // İsim hizası + hemen altı: yatay gövde + uçlardaki < ok başları
                    var okuW1 = new Point3d(p.X - KolonOkuSidePadCm, p.Y - KolonOkuBelowMaxCm, p.Z);
                    var okuW2 = new Point3d(p.X + nameW + KolonOkuSidePadCm, p.Y + KolonOkuAboveMaxCm, p.Z);
                    CollectKolonDonatiOkuLines(ed, tr, okuW1, okuW2, p, erase);

                    var filW1 = new Point3d(p.X - KolonFilizLeftCm, p.Y - KolonFilizVertCm, p.Z);
                    var filW2 = new Point3d(p.X + KolonFilizRightCm, p.Y + KolonFilizVertCm, p.Z);
                    CollectKolonFilizNear(ed, tr, filW1, filW2, erase);
                }

                foreach (var oid in erase)
                {
                    if (oid.IsErased) continue;
                    (tr.GetObject(oid, OpenMode.ForWrite) as Entity)?.Erase();
                }
                tr.Commit();
            }
        }

        private static void CollectKolonDonatiOkuLines(
            Editor ed, Transaction tr, Point3d w1, Point3d w2, Point3d kolonIns, HashSet<ObjectId> erase)
        {
            double xMin = Math.Min(w1.X, w2.X), xMax = Math.Max(w1.X, w2.X);
            double yMin = Math.Min(w1.Y, w2.Y), yMax = Math.Max(w1.Y, w2.Y);

            foreach (string lyr in KolonDonatiOkuLayers)
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
                        new TypedValue(-4, "OR>"),
                        new TypedValue(8, lyr),
                        new TypedValue(-4, "AND>")
                    });
                    var r = ed.SelectCrossingWindow(w1, w2, filt);
                    if (r.Status != PromptStatus.OK || r.Value == null) continue;

                    foreach (ObjectId oid in r.Value.GetObjectIds())
                    {
                        if (oid.IsErased || erase.Contains(oid)) continue;
                        var ent = tr.GetObject(oid, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        if (ent is Line ln)
                        {
                            if (!IsKolonDonatiOkuSegment(ln, kolonIns, xMin, xMax, yMin, yMax))
                                continue;
                            erase.Add(oid);
                        }
                        else if (ent is Polyline pl)
                        {
                            // Küçük < / ok başı polylinesi
                            try
                            {
                                var ex = pl.GeometricExtents;
                                double span = Math.Max(ex.MaxPoint.X - ex.MinPoint.X, ex.MaxPoint.Y - ex.MinPoint.Y);
                                var mid = new Point3d(
                                    0.5 * (ex.MinPoint.X + ex.MaxPoint.X),
                                    0.5 * (ex.MinPoint.Y + ex.MaxPoint.Y), 0);
                                if (span > KolonOkuArrowHeadMaxLenCm * 1.5) continue;
                                if (mid.X < xMin || mid.X > xMax || mid.Y < yMin || mid.Y > yMax) continue;
                                if (mid.Y > kolonIns.Y + KolonOkuAboveMaxCm) continue;
                                if (mid.Y < kolonIns.Y - KolonOkuBelowMaxCm) continue;
                                erase.Add(oid);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Yatay gövde (15–280 cm) veya uçtaki kısa &lt; ok başı parçası (≤22 cm, isim hizasında).
        /// </summary>
        private static bool IsKolonDonatiOkuSegment(
            Line ln, Point3d kolonIns, double xMin, double xMax, double yMin, double yMax)
        {
            double len = ln.StartPoint.DistanceTo(ln.EndPoint);
            if (len < 0.5) return false;

            var mid = new Point3d(
                0.5 * (ln.StartPoint.X + ln.EndPoint.X),
                0.5 * (ln.StartPoint.Y + ln.EndPoint.Y), 0);
            if (mid.X < xMin || mid.X > xMax || mid.Y < yMin || mid.Y > yMax) return false;
            if (mid.Y > kolonIns.Y + KolonOkuAboveMaxCm) return false;
            if (mid.Y < kolonIns.Y - KolonOkuBelowMaxCm) return false;

            double dx = Math.Abs(ln.EndPoint.X - ln.StartPoint.X);
            double dy = Math.Abs(ln.EndPoint.Y - ln.StartPoint.Y);

            // Uçtaki < : kısa, eğik veya kısa yatay/dik parçalar
            if (len <= KolonOkuArrowHeadMaxLenCm)
                return true;

            // Ana yatay gövde
            if (len < KolonOkuMinLenCm || len > KolonOkuMaxLenCm) return false;
            if (dx < dy * 2.0) return false;
            return true;
        }

        private static void CollectKolonFilizNear(
            Editor ed, Transaction tr, Point3d w1, Point3d w2, HashSet<ObjectId> erase)
        {
            try
            {
                var filt = new SelectionFilter(new[]
                {
                    new TypedValue(-4, "<AND"),
                    new TypedValue(-4, "<OR"),
                    new TypedValue(0, "LINE"),
                    new TypedValue(0, "LWPOLYLINE"),
                    new TypedValue(0, "TEXT"),
                    new TypedValue(0, "MTEXT"),
                    new TypedValue(-4, "OR>"),
                    new TypedValue(8, LyrFiliz),
                    new TypedValue(-4, "AND>")
                });
                var r = ed.SelectCrossingWindow(w1, w2, filt);
                if (r.Status != PromptStatus.OK || r.Value == null) return;

                double xMin = Math.Min(w1.X, w2.X), xMax = Math.Max(w1.X, w2.X);
                double yMin = Math.Min(w1.Y, w2.Y), yMax = Math.Max(w1.Y, w2.Y);

                foreach (ObjectId oid in r.Value.GetObjectIds())
                {
                    if (oid.IsErased || erase.Contains(oid)) continue;
                    var ent = tr.GetObject(oid, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    if (ent is DBText || ent is MText)
                    {
                        erase.Add(oid);
                        continue;
                    }
                    if (ent is Line ln)
                    {
                        var mid = new Point3d(
                            0.5 * (ln.StartPoint.X + ln.EndPoint.X),
                            0.5 * (ln.StartPoint.Y + ln.EndPoint.Y), 0);
                        if (mid.X < xMin || mid.X > xMax || mid.Y < yMin || mid.Y > yMax)
                            continue;
                        // Uzun yapı çizgisi değil; filiz kısa bağ çizgisi
                        if (ln.StartPoint.DistanceTo(ln.EndPoint) > 200.0) continue;
                        erase.Add(oid);
                    }
                    else if (ent is Polyline pl)
                    {
                        try
                        {
                            var ex = pl.GeometricExtents;
                            var mid = new Point3d(
                                0.5 * (ex.MinPoint.X + ex.MaxPoint.X),
                                0.5 * (ex.MinPoint.Y + ex.MaxPoint.Y), 0);
                            if (mid.X < xMin || mid.X > xMax || mid.Y < yMin || mid.Y > yMax)
                                continue;
                            double span = Math.Max(ex.MaxPoint.X - ex.MinPoint.X, ex.MaxPoint.Y - ex.MinPoint.Y);
                            if (span > 200.0) continue;
                            erase.Add(oid);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static void ProcessFilizTexts(Transaction tr, Database db, Editor ed,
            string layer, double dx, double dy)
        {
            var cand = KirisDuzeltRunner.SelectTextOrMtextOnLayerWithHeight(ed, tr, layer, 15.0);
            var ids = new List<ObjectId>();
            foreach (var id in cand)
            {
                string s = GetPlainText(tr, id);
                if (s.IndexOf('=') >= 0) ids.Add(id);
            }
            if (ids.Count == 0) return;

            KirisDuzeltRunner.EnsureLayer(tr, db, LyrFiliz, 60, LineWeight.LineWeight020);
            var style = KirisDuzeltRunner.TextStyleId(tr, db, StlYazi);
            foreach (var id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    t.Layer = LyrFiliz;
                    t.Height = 10.0;
                    if (!style.IsNull) t.TextStyleId = style;
                    t.TextString = CleanFilizText(t.TextString ?? "");
                    var p = t.Position;
                    t.Position = new Point3d(p.X + dx, p.Y + dy, p.Z);
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    mt.Layer = LyrFiliz;
                    mt.TextHeight = 10.0;
                    if (!style.IsNull) mt.TextStyleId = style;
                    mt.Contents = CleanFilizText(KirisDuzeltRunner.MTextPlainContents(mt));
                    var p = mt.Location;
                    mt.Location = new Point3d(p.X + dx, p.Y + dy, p.Z);
                }
            }
        }

        private static string CleanFilizText(string s)
        {
            s = RightTrimCharset(s, FilizTrimCharset);
            s = s.TrimEnd(' ');
            return s.Replace('\u0192', '\u00F8');
        }

        private static string RightTrimCharset(string s, string charset)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            while (s.Length > 0 && charset.IndexOf(s[s.Length - 1]) >= 0)
                s = s.Substring(0, s.Length - 1);
            return s;
        }

        private static void ProcessExactText(Transaction tr, Database db, Editor ed,
            string layer, double height, string exact, string targetLayer, string styleName,
            double? newHeight, short layerColor)
        {
            var cand = KirisDuzeltRunner.SelectTextOrMtextOnLayerWithHeight(ed, tr, layer, height);
            var ids = new List<ObjectId>();
            foreach (var id in cand)
            {
                if (string.Equals(GetPlainText(tr, id).Trim(), exact, StringComparison.OrdinalIgnoreCase))
                    ids.Add(id);
            }
            if (ids.Count == 0) return;

            KirisDuzeltRunner.EnsureLayer(tr, db, targetLayer, layerColor, LineWeight.LineWeight020);
            var style = KirisDuzeltRunner.TextStyleId(tr, db, styleName);
            foreach (var id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    t.Layer = targetLayer;
                    if (newHeight.HasValue) t.Height = newHeight.Value;
                    if (!style.IsNull) t.TextStyleId = style;
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    mt.Layer = targetLayer;
                    if (newHeight.HasValue) mt.TextHeight = newHeight.Value;
                    if (!style.IsNull) mt.TextStyleId = style;
                }
            }
        }

        private static void ProcessDonatiYazisi(Transaction tr, Database db, Editor ed,
            string layer, double height, double? newHeight)
        {
            var ids = KirisDuzeltRunner.SelectTextOrMtextOnLayerWithHeight(ed, tr, layer, height);
            if (ids.Count == 0) return;
            KirisDuzeltRunner.EnsureLayer(tr, db, LyrDonatiYazisi, 3, LineWeight.LineWeight020);
            var style = KirisDuzeltRunner.TextStyleId(tr, db, StlYazi);
            foreach (var id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    t.Layer = LyrDonatiYazisi;
                    if (!style.IsNull) t.TextStyleId = style;
                    var s = t.TextString ?? "";
                    while (s.Length > 0 && s[s.Length - 1] == '\u00FF')
                        s = s.Substring(0, s.Length - 1);
                    t.TextString = s.Replace('\u0192', '\u00F8');
                    if (newHeight.HasValue) t.Height = newHeight.Value;
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    mt.Layer = LyrDonatiYazisi;
                    if (!style.IsNull) mt.TextStyleId = style;
                    var s = KirisDuzeltRunner.MTextPlainContents(mt);
                    while (s.Length > 0 && s[s.Length - 1] == '\u00FF')
                        s = s.Substring(0, s.Length - 1);
                    mt.Contents = s.Replace('\u0192', '\u00F8');
                    if (newHeight.HasValue) mt.TextHeight = newHeight.Value;
                }
            }
        }

        private static void ProcessOlcuYazisi(Transaction tr, Database db, Editor ed,
            string layer, double height, double? newHeight)
        {
            var ids = KirisDuzeltRunner.SelectTextOrMtextOnLayerWithHeight(ed, tr, layer, height);
            if (ids.Count == 0) return;
            KirisDuzeltRunner.EnsureLayer(tr, db, LyrOlcuYazisi, 7, LineWeight.LineWeight020);
            var style = KirisDuzeltRunner.TextStyleId(tr, db, StlOlcu);
            foreach (var id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    t.Layer = LyrOlcuYazisi;
                    if (newHeight.HasValue) t.Height = newHeight.Value;
                    if (!style.IsNull) t.TextStyleId = style;
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    mt.Layer = LyrOlcuYazisi;
                    if (newHeight.HasValue) mt.TextHeight = newHeight.Value;
                    if (!style.IsNull) mt.TextStyleId = style;
                }
            }
        }

        private static void ProcessKesitiTexts(Transaction tr, Database db, Editor ed)
        {
            var cand = KirisDuzeltRunner.SelectTextOrMtextOnLayerWithHeight(ed, tr, "DETAIL4", 20.0);
            var ids = new List<ObjectId>();
            foreach (var id in cand)
            {
                var s = GetPlainText(tr, id);
                if (s.IndexOf("KESITI", StringComparison.OrdinalIgnoreCase) >= 0)
                    ids.Add(id);
            }
            if (ids.Count == 0) return;
            KirisDuzeltRunner.EnsureLayer(tr, db, LyrKesitIsmi, 6, LineWeight.LineWeight020);
            var style = KirisDuzeltRunner.TextStyleId(tr, db, StlYazi);
            foreach (var id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    t.Layer = LyrKesitIsmi;
                    t.Height = 15.0;
                    if (!style.IsNull) t.TextStyleId = style;
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    mt.Layer = LyrKesitIsmi;
                    mt.TextHeight = 15.0;
                    if (!style.IsNull) mt.TextStyleId = style;
                }
            }
        }

        /// <summary>DETAIL3 h=15 → TEMEL ISMI; alt/yan ölçü çizgi+yazılarını LISP ile aynı kaydır.</summary>
        private static void ProcessKesitTemelYazisiVeOlcuKaydir(Transaction tr, Database db, Editor ed)
        {
            var ids = KirisDuzeltRunner.SelectTextOrMtextOnLayerWithHeight(ed, tr, "DETAIL3", 15.0);
            if (ids.Count == 0) return;

            KirisDuzeltRunner.EnsureLayer(tr, db, LyrTemelIsmi, 40, LineWeight.LineWeight020);
            var style = KirisDuzeltRunner.TextStyleId(tr, db, StlYazi);

            foreach (var id in ids)
            {
                Point3d secOlcuKoor;
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    t.Layer = LyrTemelIsmi;
                    t.Height = 12.5;
                    if (!style.IsNull) t.TextStyleId = style;
                    secOlcuKoor = t.Position;
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    mt.Layer = LyrTemelIsmi;
                    mt.TextHeight = 12.5;
                    if (!style.IsNull) mt.TextStyleId = style;
                    secOlcuKoor = mt.Location;
                }
                else continue;

                if (!TryFindOlcuRefLine(ed, tr, secOlcuKoor, out Point3d secNoko1, out Point3d secNoko2))
                    continue;

                ShiftLinesInWindow(ed, tr,
                    new Point3d(secNoko1.X - 20, secNoko1.Y - 20, 0),
                    new Point3d(secNoko2.X + 20, secNoko2.Y + 20, 0),
                    LyrOlcu, 0, 60);
                ShiftTextsInWindow(ed, tr,
                    new Point3d(secNoko1.X - 20, secNoko1.Y - 20, 0),
                    new Point3d(secNoko2.X + 20, secNoko2.Y + 20, 0),
                    LyrOlcuYazisi, 10.0, 0, 60);
                ShiftLinesInWindow(ed, tr,
                    new Point3d(secNoko1.X - 85, secNoko1.Y, 0),
                    new Point3d(secOlcuKoor.X - 40, secOlcuKoor.Y, 0),
                    LyrOlcu, 40, 0);
                ShiftTextsInWindow(ed, tr,
                    new Point3d(secNoko1.X - 85, secNoko1.Y, 0),
                    new Point3d(secOlcuKoor.X - 40, secOlcuKoor.Y, 0),
                    LyrOlcuYazisi, 10.0, 40, 0);
            }
        }

        private static bool TryFindOlcuRefLine(Editor ed, Transaction tr, Point3d secOlcuKoor,
            out Point3d noko1, out Point3d noko2)
        {
            noko1 = noko2 = Point3d.Origin;
            var filt = new SelectionFilter(new[]
            {
                new TypedValue(0, "LINE"),
                new TypedValue(8, LyrOlcu)
            });
            for (double dy = 10.0; dy <= 500.0; dy += 10.0)
            {
                var pts = new Point3dCollection
                {
                    new Point3d(secOlcuKoor.X + 10.0, secOlcuKoor.Y - 60.0, 0),
                    new Point3d(secOlcuKoor.X + 10.0, secOlcuKoor.Y - 60.0 - dy, 0)
                };
                try
                {
                    var r = ed.SelectFence(pts, filt);
                    if (r.Status != PromptStatus.OK || r.Value == null || r.Value.Count == 0)
                        continue;
                    var ln = tr.GetObject(r.Value.GetObjectIds()[0], OpenMode.ForRead) as Line;
                    if (ln == null) continue;
                    noko1 = ln.StartPoint;
                    noko2 = ln.EndPoint;
                    return true;
                }
                catch { }
            }
            return false;
        }

        private static void ShiftLinesInWindow(Editor ed, Transaction tr,
            Point3d c1, Point3d c2, string layer, double dx, double dy)
        {
            try
            {
                var r = ed.SelectWindow(c1, c2, new SelectionFilter(new[]
                {
                    new TypedValue(0, "LINE"),
                    new TypedValue(8, layer)
                }));
                if (r.Status != PromptStatus.OK || r.Value == null) return;
                var offset = new Vector3d(dx, dy, 0);
                foreach (ObjectId oid in r.Value.GetObjectIds())
                {
                    if (tr.GetObject(oid, OpenMode.ForWrite) is Line ln)
                    {
                        ln.StartPoint += offset;
                        ln.EndPoint += offset;
                    }
                }
            }
            catch { }
        }

        private static void ShiftTextsInWindow(Editor ed, Transaction tr,
            Point3d c1, Point3d c2, string layer, double height, double dx, double dy)
        {
            try
            {
                var r = ed.SelectWindow(c1, c2, new SelectionFilter(
                    KirisDuzeltRunner.FilterTextOrMtext(
                        new TypedValue(8, layer),
                        new TypedValue(40, height))));
                if (r.Status != PromptStatus.OK || r.Value == null)
                {
                    // DXF filtre 40 bazen MTEXT’te tutmaz; yükseklik elle süz.
                    r = ed.SelectWindow(c1, c2, new SelectionFilter(
                        KirisDuzeltRunner.FilterTextOrMtext(new TypedValue(8, layer))));
                    if (r.Status != PromptStatus.OK || r.Value == null) return;
                    var offset = new Vector3d(dx, dy, 0);
                    foreach (ObjectId oid in r.Value.GetObjectIds())
                    {
                        if (tr.GetObject(oid, OpenMode.ForWrite) is DBText t
                            && Math.Abs(t.Height - height) < 0.02)
                            t.Position += offset;
                        else if (tr.GetObject(oid, OpenMode.ForWrite) is MText mt
                                 && Math.Abs(mt.TextHeight - height) < 0.02)
                            mt.Location += offset;
                    }
                    return;
                }
                {
                    var offset = new Vector3d(dx, dy, 0);
                    foreach (ObjectId oid in r.Value.GetObjectIds())
                    {
                        if (tr.GetObject(oid, OpenMode.ForWrite) is DBText t)
                            t.Position += offset;
                        else if (tr.GetObject(oid, OpenMode.ForWrite) is MText mt)
                            mt.Location += offset;
                    }
                }
            }
            catch { }
        }

        private static void ProcessKesitImi(Transaction tr, Database db, Editor ed)
        {
            var ids = KirisDuzeltRunner.SelectTextOrMtextOnLayerWithHeight(ed, tr, "DETAIL4", 20.0);
            if (ids.Count == 0) return;
            KirisDuzeltRunner.EnsureLayer(tr, db, LyrKesitIsmi, 6, LineWeight.LineWeight020);
            var style = KirisDuzeltRunner.TextStyleId(tr, db, StlYazi);
            foreach (var id in ids)
            {
                Point3d p;
                if (tr.GetObject(id, OpenMode.ForWrite) is DBText t)
                {
                    // *KESITI zaten işlendiyse tekrar dokunma
                    if (string.Equals(t.Layer, LyrKesitIsmi, StringComparison.OrdinalIgnoreCase)
                        && Math.Abs(t.Height - 15.0) < 0.02)
                        continue;
                    t.Layer = LyrKesitIsmi;
                    t.Height = 12.5;
                    t.Rotation = Math.PI / 2.0;
                    if (!style.IsNull) t.TextStyleId = style;
                    p = t.Position;
                }
                else if (tr.GetObject(id, OpenMode.ForWrite) is MText mt)
                {
                    if (string.Equals(mt.Layer, LyrKesitIsmi, StringComparison.OrdinalIgnoreCase)
                        && Math.Abs(mt.TextHeight - 15.0) < 0.02)
                        continue;
                    mt.Layer = LyrKesitIsmi;
                    mt.TextHeight = 12.5;
                    mt.Rotation = Math.PI / 2.0;
                    if (!style.IsNull) mt.TextStyleId = style;
                    p = mt.Location;
                }
                else continue;

                // LISP yalnız +X; eski kesit üçgeni genelde solda — her iki yan.
                KirisDuzeltRunner.TryEraseCurvesOnLayerInCrossingWindow(ed, tr,
                    new Point3d(p.X - 80.0, p.Y - 90.0, p.Z),
                    new Point3d(p.X + 60.0, p.Y + 90.0, p.Z),
                    "DETAIL4");
            }
        }

        /// <summary>
        /// Yeni KES_DET yanında kalan eski STA kesit sembolü (içi boş üçgen + dik çizgi),
        /// DETAIL4→DONATI taşınmış olabilir. Yalnız kısa/dik parçaları sil; uzun yatay donatıya dokunma.
        /// </summary>
        private const double OldKesitPadLeftCm = 55.0;
        private const double OldKesitPadRightCm = 50.0;
        private const double OldKesitPadVertCm = 60.0;
        private const double OldKesitMaxTriangleEdgeCm = 55.0;
        private const double OldKesitMaxVerticalCm = 220.0;
        private const double OldKesitVerticalMaxHorizSpanCm = 8.0;
        private const double OldKesitVerticalXTolCm = 45.0;

        private static void EraseOldKesitSymbolsOnDonatiNearKesitLabels(Database db, Editor ed)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var kesitIds = KirisDuzeltRunner.SelectTextOrMtextOnLayerWithHeight(ed, tr, LyrKesitIsmi, 12.5);
                var erase = new HashSet<ObjectId>();
                foreach (var kid in kesitIds)
                {
                    Point3d p;
                    if (tr.GetObject(kid, OpenMode.ForRead) is DBText t)
                        p = t.Position;
                    else if (tr.GetObject(kid, OpenMode.ForRead) is MText mt)
                        p = mt.Location;
                    else continue;

                    // Dik çizgi için dikeyde biraz daha geniş, yatayda dar kutu.
                    var w1 = new Point3d(p.X - OldKesitPadLeftCm, p.Y - OldKesitMaxVerticalCm * 0.55, p.Z);
                    var w2 = new Point3d(p.X + OldKesitPadRightCm, p.Y + OldKesitMaxVerticalCm * 0.55, p.Z);

                    try
                    {
                        var filt = new SelectionFilter(new[]
                        {
                            new TypedValue(-4, "<AND"),
                            new TypedValue(-4, "<OR"),
                            new TypedValue(0, "LINE"),
                            new TypedValue(0, "LWPOLYLINE"),
                            new TypedValue(0, "POLYLINE"),
                            new TypedValue(-4, "OR>"),
                            new TypedValue(8, LyrDonati),
                            new TypedValue(-4, "AND>")
                        });
                        var r = ed.SelectCrossingWindow(w1, w2, filt);
                        if (r.Status != PromptStatus.OK || r.Value == null) continue;

                        foreach (ObjectId oid in r.Value.GetObjectIds())
                        {
                            if (oid.IsErased) continue;
                            var ent = tr.GetObject(oid, OpenMode.ForRead) as Entity;
                            if (ent == null) continue;

                            if (ent is Line ln)
                            {
                                if (!IsOldKesitDonatiLine(ln, p))
                                    continue;
                                erase.Add(oid);
                            }
                            else if (ent is Polyline pl)
                            {
                                if (!IsOldKesitDonatiPolyline(pl, p))
                                    continue;
                                erase.Add(oid);
                            }
                            else if (ent is Autodesk.AutoCAD.DatabaseServices.Polyline2d)
                            {
                                try
                                {
                                    var ex = ent.GeometricExtents;
                                    double span = Math.Max(ex.MaxPoint.X - ex.MinPoint.X, ex.MaxPoint.Y - ex.MinPoint.Y);
                                    var mid = new Point3d(
                                        0.5 * (ex.MinPoint.X + ex.MaxPoint.X),
                                        0.5 * (ex.MinPoint.Y + ex.MaxPoint.Y), 0);
                                    if (span <= OldKesitMaxTriangleEdgeCm
                                        && Math.Abs(mid.X - p.X) <= OldKesitPadLeftCm
                                        && Math.Abs(mid.Y - p.Y) <= OldKesitPadVertCm)
                                        erase.Add(oid);
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                foreach (var oid in erase)
                {
                    if (oid.IsErased) continue;
                    (tr.GetObject(oid, OpenMode.ForWrite) as Entity)?.Erase();
                }
                tr.Commit();
            }
        }

        private static bool IsOldKesitDonatiLine(Line ln, Point3d kesitText)
        {
            double len = ln.StartPoint.DistanceTo(ln.EndPoint);
            if (len < 0.5) return false;
            var mid = new Point3d(
                0.5 * (ln.StartPoint.X + ln.EndPoint.X),
                0.5 * (ln.StartPoint.Y + ln.EndPoint.Y), 0);

            double dx = Math.Abs(ln.EndPoint.X - ln.StartPoint.X);
            double dy = Math.Abs(ln.EndPoint.Y - ln.StartPoint.Y);
            bool nearlyVertical = dx <= OldKesitVerticalMaxHorizSpanCm && dy >= dx * 2.0;
            bool nearlyHorizontal = dy <= OldKesitVerticalMaxHorizSpanCm && dx >= dy * 2.0;

            // Uzun yatay kiriş/donatı kenarı
            if (nearlyHorizontal && len > 40.0) return false;

            if (nearlyVertical)
            {
                if (len > OldKesitMaxVerticalCm) return false;
                if (Math.Abs(mid.X - kesitText.X) > OldKesitVerticalXTolCm) return false;
                if (Math.Abs(mid.Y - kesitText.Y) > OldKesitMaxVerticalCm * 0.55) return false;
                return true;
            }

            // Üçgen kenarı / kısa taban — yazıya yakın küçük kutu
            if (len > OldKesitMaxTriangleEdgeCm) return false;
            if (Math.Abs(mid.X - kesitText.X) > OldKesitPadLeftCm) return false;
            if (Math.Abs(mid.Y - kesitText.Y) > OldKesitPadVertCm) return false;
            return true;
        }

        private static bool IsOldKesitDonatiPolyline(Polyline pl, Point3d kesitText)
        {
            try
            {
                var ex = pl.GeometricExtents;
                double span = Math.Max(ex.MaxPoint.X - ex.MinPoint.X, ex.MaxPoint.Y - ex.MinPoint.Y);
                var mid = new Point3d(
                    0.5 * (ex.MinPoint.X + ex.MaxPoint.X),
                    0.5 * (ex.MinPoint.Y + ex.MaxPoint.Y), 0);
                if (Math.Abs(mid.X - kesitText.X) > OldKesitPadLeftCm) return false;
                if (Math.Abs(mid.Y - kesitText.Y) > OldKesitPadVertCm) return false;
                return span <= OldKesitMaxTriangleEdgeCm && pl.NumberOfVertices <= 8;
            }
            catch
            {
                return false;
            }
        }

        private static void RemapLines(Transaction tr, Database db, Editor ed,
            string fromLayer, string toLayer, short color, LineWeight lw, string linetype = null)
        {
            var ids = KirisDuzeltRunner.SelectAllIds(ed, new SelectionFilter(new[]
            {
                new TypedValue(0, "LINE"),
                new TypedValue(8, fromLayer)
            }));
            if (ids.Count == 0) return;
            KirisDuzeltRunner.EnsureLayer(tr, db, toLayer, color, lw, linetype);
            foreach (var id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is Line ln)
                    ln.Layer = toLayer;
            }
        }

        private static void RemapCircles(Transaction tr, Database db, Editor ed,
            string fromLayer, string toLayer, short color, LineWeight lw)
        {
            var ids = KirisDuzeltRunner.SelectAllIds(ed, new SelectionFilter(new[]
            {
                new TypedValue(0, "CIRCLE"),
                new TypedValue(8, fromLayer)
            }));
            if (ids.Count == 0) return;
            KirisDuzeltRunner.EnsureLayer(tr, db, toLayer, color, lw);
            foreach (var id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is Circle c)
                    c.Layer = toLayer;
            }
        }

        /// <summary>
        /// Açılımların sağındaki STA metraj/malzeme yazıları ve Kolon İlave Donatı Tablosu silinir.
        /// </summary>
        private static readonly string[] AcilimSagiSilIsaretleri =
        {
            "Kolon \u0130lave", "Kolon Ilave", "\u0130lave Donat", "Ilave Donat",
            "KALIP (m2)", "KALIP(m2)", "BETON (m3)", "BETON(m3)",
            "BETON SINIFI", "CELIK SINIFI", "\u00C7ELIK SINIFI", "\u00C7EL\u0130K SINIFI",
            "MALZEME", "Zemin Yatak", "ZEMIN YATAK", "Yatak Katsay"
        };

        private const double AcilimSagiCutPadLeftCm = 40.0;
        private const double AcilimSagiMaxSpanKeepCm = 2500.0;

        /// <summary>
        /// Yalnızca STA MALZEME bloğu: malzeme yazıları + MATERIAL* katmanı çizgileri.
        /// Kiriş kesiti / ölçü / donatı çizgilerine dokunulmaz (KIRISDUZELT).
        /// </summary>
        internal static void EraseMalzemeBloguOnly(Database db, Editor ed)
        {
            if (db == null) return;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                var textIds = KirisDuzeltRunner.SelectAllIds(ed, new SelectionFilter(
                    KirisDuzeltRunner.FilterTextOrMtext()));
                var seedPts = new List<Point3d>();
                var eraseTextIds = new HashSet<ObjectId>();

                foreach (var id in textIds)
                {
                    if (!TryGetTextInfo(tr, id, out string s, out Point3d p, out _)) continue;
                    if (!IsMalzemeBloguSeedText(s)) continue;
                    seedPts.Add(p);
                    eraseTextIds.Add(id);
                }

                if (seedPts.Count > 0)
                {
                    Point3d anchor = seedPts[0];
                    double ax = 0, ay = 0;
                    foreach (var p in seedPts) { ax += p.X; ay += p.Y; }
                    anchor = new Point3d(ax / seedPts.Count, ay / seedPts.Count, 0);
                    foreach (var id in textIds)
                    {
                        if (!TryGetTextInfo(tr, id, out string s, out Point3d p, out _)) continue;
                        if (!string.Equals((s ?? "").Trim(), "MALZEME", StringComparison.OrdinalIgnoreCase)) continue;
                        anchor = p;
                        break;
                    }

                    // Yakın malzeme satırları / tablo başlıkları — çıplak ölçü (30, 20) silinmez
                    const double nearX = 260.0;
                    const double nearY = 160.0;
                    foreach (var id in textIds)
                    {
                        if (eraseTextIds.Contains(id)) continue;
                        if (!TryGetTextInfo(tr, id, out string s, out Point3d p, out _)) continue;
                        if (Math.Abs(p.X - anchor.X) > nearX || Math.Abs(p.Y - anchor.Y) > nearY) continue;
                        if ((s ?? string.Empty).Length > 48) continue;
                        if (LooksLikeKirisOrKesitLabel(s)) continue;
                        if (!IsMalzemeBloguNearbyCellText(s)) continue;
                        eraseTextIds.Add(id);
                    }
                }

                int nText = 0;
                foreach (var id in eraseTextIds)
                {
                    if (id.IsNull || id.IsErased) continue;
                    try
                    {
                        var e = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                        if (e == null || e.IsErased) continue;
                        e.Erase();
                        nText++;
                    }
                    catch { }
                }

                // Tablo/altı çizgi yalnızca MATERIAL* katmanında (kesit OLCU/KESIT/DONATI korunur)
                int nMat = 0;
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (KirisDuzeltRunner.SelectionScope != null && KirisDuzeltRunner.SelectionScope.Count > 0
                        && !KirisDuzeltRunner.SelectionScope.Contains(id))
                        continue;
                    try
                    {
                        var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (e == null || e.IsErased) continue;
                        if (!IsMaterialStaLayer(e.Layer)) continue;
                        e.UpgradeOpen();
                        e.Erase();
                        nMat++;
                    }
                    catch { }
                }

                if (nText + nMat > 0)
                    ed?.WriteMessage("\nMALZEME blogu silindi (yazi={0}, MATERIAL*={1}).", nText, nMat);
                else
                    ed?.WriteMessage("\nMALZEME blogu bulunamadi (silinmedi).");

                tr.Commit();
            }
        }

        private static bool IsMaterialStaLayer(string layer)
        {
            if (string.IsNullOrWhiteSpace(layer)) return false;
            string u = layer.Trim().ToUpperInvariant();
            return u.StartsWith("MATERIAL", StringComparison.Ordinal);
        }

        /// <summary>MALZEME bloğu içi tablo başlık / sınıf — kesit ölçü sayısı (30, 20) değil.</summary>
        private static bool IsMalzemeBloguNearbyCellText(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            string t = s.Trim();
            if (IsMalzemeBloguSeedText(t)) return true;
            if (Regex.IsMatch(t, @"^(Ss|S1|YZS|BKS|BYS|DTS|Pas\s*p\.?)$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return true;
            if (t.Equals("ZD", StringComparison.OrdinalIgnoreCase)) return true;
            if (t.IndexOf("C25", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("B420", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("MPa", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("fck", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("fyk", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            // Tablo değeri: 0.3 / 0.07 / 1.0 / 4.0 / 2.5 / 4 cm (tam sayı cm kesit ölçüsü değil)
            if (Regex.IsMatch(t, @"^\d+[.,]\d+\s*(cm)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return true;
            if (Regex.IsMatch(t, @"^\d+\s*cm$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return true;
            return false;
        }

        private static bool TryGetTextInfo(Transaction tr, ObjectId id, out string s, out Point3d p, out Extents3d ex)
        {
            s = null; p = Point3d.Origin; ex = new Extents3d();
            if (tr.GetObject(id, OpenMode.ForRead) is DBText t)
            {
                s = t.TextString ?? "";
                p = t.Position;
                try { ex = t.GeometricExtents; return true; }
                catch { return false; }
            }
            if (tr.GetObject(id, OpenMode.ForRead) is MText mt)
            {
                s = KirisDuzeltRunner.MTextPlainContents(mt);
                p = mt.Location;
                try { ex = mt.GeometricExtents; return true; }
                catch { return false; }
            }
            return false;
        }

        private static bool IsMalzemeBloguSeedText(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            string t = s.Trim();
            if (t.Equals("MALZEME", StringComparison.OrdinalIgnoreCase)) return true;
            if (t.IndexOf("BETON SINIFI", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("CELIK SINIFI", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("\u00C7ELIK SINIFI", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("\u00C7EL\u0130K SINIFI", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("fck:", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("fyk:", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            // Tablo başlık satırı (Ss S1 YZS …)
            if (t.IndexOf("YZS", StringComparison.OrdinalIgnoreCase) >= 0
                && t.IndexOf("BKS", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (t.IndexOf("Pas p", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool LooksLikeKirisOrKesitLabel(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            string u = s.Trim().ToUpperInvariant();
            if (u.IndexOf("KESIT", StringComparison.Ordinal) >= 0) return true;
            if (u.IndexOf("KESİT", StringComparison.Ordinal) >= 0) return true;
            if (u.IndexOf("KIRIS", StringComparison.Ordinal) >= 0) return true;
            if (u.IndexOf("KİRİŞ", StringComparison.Ordinal) >= 0) return true;
            if (u.IndexOf("KIRISI", StringComparison.Ordinal) >= 0) return true;
            // KB-19 / KZ-05 vb.
            if (Regex.IsMatch(u, @"^K[A-Z0-9]+-\d+", RegexOptions.CultureInvariant)) return true;
            return false;
        }

        internal static void EraseAcilimSagindakiMetrajVeTablolar(Database db, Editor ed)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var markerXs = new List<double>();
                var textIds = KirisDuzeltRunner.SelectAllIds(ed, new SelectionFilter(
                    KirisDuzeltRunner.FilterTextOrMtext()));
                foreach (var id in textIds)
                {
                    string s;
                    Point3d p;
                    if (tr.GetObject(id, OpenMode.ForRead) is DBText t)
                    {
                        s = t.TextString ?? "";
                        p = t.Position;
                    }
                    else if (tr.GetObject(id, OpenMode.ForRead) is MText mt)
                    {
                        s = KirisDuzeltRunner.MTextPlainContents(mt);
                        p = mt.Location;
                    }
                    else continue;

                    if (!IsAcilimSagiMarkerText(s)) continue;
                    markerXs.Add(p.X);
                }

                double cutX;
                if (markerXs.Count > 0)
                {
                    cutX = markerXs.Min() - AcilimSagiCutPadLeftCm;
                }
                else if (!TryFindRightClusterCutX(tr, db, out cutX))
                {
                    ed?.WriteMessage("\nAcilim sagindaki metraj/tablo blogu bulunamadi.");
                    tr.Commit();
                    return;
                }

                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                int n = 0;
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (KirisDuzeltRunner.SelectionScope != null && KirisDuzeltRunner.SelectionScope.Count > 0
                        && !KirisDuzeltRunner.SelectionScope.Contains(id))
                        continue;
                    try
                    {
                        var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (e == null || e.IsErased) continue;
                        Extents3d ex;
                        try { ex = e.GeometricExtents; }
                        catch { continue; }
                        double spanX = ex.MaxPoint.X - ex.MinPoint.X;
                        if (spanX > AcilimSagiMaxSpanKeepCm) continue;
                        double midX = 0.5 * (ex.MinPoint.X + ex.MaxPoint.X);
                        // Tamamen veya ortasi kesitin saginda
                        if (midX < cutX && ex.MinPoint.X < cutX) continue;
                        if (midX < cutX) continue;

                        e.UpgradeOpen();
                        e.Erase();
                        n++;
                    }
                    catch { }
                }

                ed?.WriteMessage("\nAcilim sagindaki metraj/malzeme/tablo {0} oge silindi (cutX={1:0}).", n, cutX);
                tr.Commit();
            }
        }

        private static bool IsAcilimSagiMarkerText(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            // Tek basina "METRAJ" basligi (kisa)
            string t = s.Trim();
            if (t.Equals("METRAJ", StringComparison.OrdinalIgnoreCase)
                || t.Equals("MALZEME", StringComparison.OrdinalIgnoreCase))
                return true;
            foreach (var m in AcilimSagiSilIsaretleri)
            {
                if (s.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>Isaret yoksa: X yonunde buyuk bosluktan sonra gelen sag kume.</summary>
        private static bool TryFindRightClusterCutX(Transaction tr, Database db, out double cutX)
        {
            cutX = 0;
            var mids = new List<double>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased) continue;
                if (KirisDuzeltRunner.SelectionScope != null && KirisDuzeltRunner.SelectionScope.Count > 0
                    && !KirisDuzeltRunner.SelectionScope.Contains(id))
                    continue;
                try
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null) continue;
                    var ex = e.GeometricExtents;
                    if (ex.MaxPoint.X - ex.MinPoint.X > AcilimSagiMaxSpanKeepCm) continue;
                    mids.Add(0.5 * (ex.MinPoint.X + ex.MaxPoint.X));
                }
                catch { }
            }
            if (mids.Count < 20) return false;
            mids.Sort();
            double bestGap = 0;
            double bestCut = 0;
            for (int i = 1; i < mids.Count; i++)
            {
                double gap = mids[i] - mids[i - 1];
                // Sag kume: bosluk buyuk ve kesit cizimin sag yarısında
                if (gap > bestGap && mids[i - 1] > mids[0] + 0.3 * (mids[mids.Count - 1] - mids[0]))
                {
                    bestGap = gap;
                    bestCut = 0.5 * (mids[i - 1] + mids[i]);
                }
            }
            if (bestGap < 150.0) return false;
            cutX = bestCut;
            return true;
        }

        private static string GetPlainText(Transaction tr, ObjectId id)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is DBText t)
                return t.TextString ?? "";
            if (tr.GetObject(id, OpenMode.ForRead) is MText mt)
                return KirisDuzeltRunner.MTextPlainContents(mt);
            return "";
        }

        /// <summary>
        /// Temel kiriş açılımı yatay sıralarını düşeyde 200 cm birbirine yaklaştırır (üst sıra sabit).
        /// 5 sıra → 4 aralık × 200 = toplam yükseklik 800 cm azalır.
        /// </summary>
        private const double TemelKirisRowMergeYTolCm = 100.0;
        private const double TemelKirisRowCompactStepCm = 220.0;
        private const double TemelKirisRowTallExcludeCm = 400.0;

        private static void CompactTemelKirisRowsVertically(Database db, Editor ed)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                var all = new List<(ObjectId id, double minY, double maxY, double midY)>();
                var seeds = new List<(ObjectId id, double minY, double maxY, double midY)>();

                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (KirisDuzeltRunner.SelectionScope != null && KirisDuzeltRunner.SelectionScope.Count > 0
                        && !KirisDuzeltRunner.SelectionScope.Contains(id))
                        continue;
                    try
                    {
                        var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (e == null || e.IsErased) continue;
                        Extents3d ex;
                        try { ex = e.GeometricExtents; }
                        catch { continue; }
                        double minY = ex.MinPoint.Y, maxY = ex.MaxPoint.Y;
                        double midY = 0.5 * (minY + maxY);
                        var item = (id, minY, maxY, midY);
                        all.Add(item);
                        if (IsTemelKirisRowBandSeed(e, maxY - minY))
                            seeds.Add(item);
                    }
                    catch { }
                }

                if (seeds.Count == 0)
                    seeds.AddRange(all);
                if (seeds.Count == 0)
                {
                    tr.Commit();
                    return;
                }

                seeds.Sort((a, b) => a.minY.CompareTo(b.minY));

                // Alttan üste bantlar (Y overlap / yakınlık ile birleştir)
                var bands = new List<(double minY, double maxY, List<ObjectId> ids)>();
                foreach (var s in seeds)
                {
                    if (bands.Count == 0 || s.minY > bands[bands.Count - 1].maxY + TemelKirisRowMergeYTolCm)
                    {
                        bands.Add((s.minY, s.maxY, new List<ObjectId>()));
                    }
                    else
                    {
                        var last = bands[bands.Count - 1];
                        bands[bands.Count - 1] = (
                            Math.Min(last.minY, s.minY),
                            Math.Max(last.maxY, s.maxY),
                            last.ids);
                    }
                }

                if (bands.Count < 2)
                {
                    ed?.WriteMessage("\nTEMELKIRISDUZELT: dusey sira sikistirma atlandi ({0} sira).", bands.Count);
                    tr.Commit();
                    return;
                }

                // Her ogeyi merkez Y ile en yakin banda ata
                for (int bi = 0; bi < bands.Count; bi++)
                    bands[bi].ids.Clear();

                foreach (var it in all)
                {
                    double h = it.maxY - it.minY;
                    // Birden fazla sırayı kesen uzun aks vb. kaydırma — bozulmasın
                    if (h > TemelKirisRowTallExcludeCm)
                    {
                        int cover = 0;
                        for (int bj = 0; bj < bands.Count; bj++)
                        {
                            if (it.minY <= bands[bj].maxY && it.maxY >= bands[bj].minY)
                                cover++;
                        }
                        if (cover > 1) continue;
                    }

                    int best = 0;
                    double bestDist = double.MaxValue;
                    for (int bi = 0; bi < bands.Count; bi++)
                    {
                        double bMid = 0.5 * (bands[bi].minY + bands[bi].maxY);
                        double d = Math.Abs(it.midY - bMid);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            best = bi;
                        }
                    }
                    bands[best].ids.Add(it.id);
                }

                // Ust sira sabit (son bant); alttakiler yukari: shift = 200 * (ustIndex - i)
                int moved = 0;
                int top = bands.Count - 1;
                for (int i = 0; i < bands.Count; i++)
                {
                    double shiftY = TemelKirisRowCompactStepCm * (top - i);
                    if (shiftY < 1e-9) continue;
                    var mat = Matrix3d.Displacement(new Vector3d(0, shiftY, 0));
                    foreach (var oid in bands[i].ids)
                    {
                        if (!oid.IsValid || oid.IsErased) continue;
                        try
                        {
                            var e = tr.GetObject(oid, OpenMode.ForWrite) as Entity;
                            if (e == null) continue;
                            e.TransformBy(mat);
                            moved++;
                        }
                        catch { }
                    }
                }

                ed?.WriteMessage(
                    "\nTEMELKIRISDUZELT: {0} sira, her aralik -{1:0} cm (toplam -{2:0} cm), {3} oge kaydirildi.",
                    bands.Count,
                    TemelKirisRowCompactStepCm,
                    TemelKirisRowCompactStepCm * (bands.Count - 1),
                    moved);
                tr.Commit();
            }
        }

        private static bool IsTemelKirisRowBandSeed(Entity e, double heightCm)
        {
            if (heightCm > TemelKirisRowTallExcludeCm) return false;
            string lyr = e.Layer ?? "";
            if (lyr.Length == 0) return false;
            var u = lyr.ToUpperInvariant();
            if (u.Contains("AKS CIZGISI") || u.Contains("AXIS2") || u.Contains("AXIS3") || u.Contains("AXIS4"))
                return false;
            // Icerik katmanlari — sira ayrimini guclendirir
            return u.Contains("TEMEL") || u.Contains("DONATI") || u.Contains("KOLON")
                   || u.Contains("KESIT") || u.Contains("OLCU") || u.Contains("KOT")
                   || u.Contains("FILIZ") || u.Contains("YAZI") || u.Contains("BEAM")
                   || u.Contains("REBAR") || u.Contains("DETAIL") || u.Contains("DIM");
        }
    }
}
