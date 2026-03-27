using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// iskele_plan.lsp → ISKELEPLAN ana akışı (C#).
    /// </summary>
    internal static class IskelePlanRunner
    {
        private const double DFar = 70.0;
        private const double Bay = 250.0;

        private const string LayerYazi = "YAZI (BEYKENT)";
        private const string LayerIskeleDetay = "ISKELE DETAY (BEYKENT)";
        private const string LayerOlcu = "OLCU (BEYKENT)";
        private const string LayerCapraz = "ISKELE CAPRAZ (BEYKENT)";
        private const string LayerDetayGizli = "ISKELE DETAY GIZLI (BEYKENT)";
        private const string LayerFlansDetay = "ISKELE FLANS DETAY (BEYKENT)";
        private const string LayerFlans = "ISKELE FLANS (BEYKENT)";
        private const string LayerYatay = "ISKELE YATAY (BEYKENT)";
        private const string LayerBolt = "ISKELE BOLT (BEYKENT)";
        private const string LayerAnkraj = "ISKELE ANKRAJ (BEYKENT)";
        private const string LayerDoseme = "DOSEME SINIRI (BEYKENT)";
        private const string LayerKesitIsmi = "KESIT ISMI (BEYKENT)";

        private const string DimPlanOlcu = "PLAN_OLCU";
        private const string DimPlanOlcuDetay = "PLAN_OLCU_DETAY";
        private const string TextStyleYazi = "YAZI (BEYKENT)";

        public static void Execute(Document doc)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            var peo1 = new PromptEntityOptions("\nKapali referans polyline secin (iskele poligonu): ");
            peo1.SetRejectMessage("\nSadece LWPOLYLINE.");
            peo1.AddAllowedClass(typeof(Polyline), false);
            var r1 = ed.GetEntity(peo1);
            if (r1.Status != PromptStatus.OK) return;

            var peo2 = new PromptEntityOptions("\nIc polyline secin (bina siniri - ankraj icin): ");
            peo2.SetRejectMessage("\nSadece LWPOLYLINE.");
            peo2.AddAllowedClass(typeof(Polyline), false);
            var r2 = ed.GetEntity(peo2);
            if (r2.Status != PromptStatus.OK) return;

            object oldLayer = null, oldCeColor = null, oldCeWeight = null, oldCeLtype = null;
            try { oldLayer = AcApp.GetSystemVariable("CLAYER"); } catch { }
            try { oldCeColor = AcApp.GetSystemVariable("CECOLOR"); } catch { }
            try { oldCeWeight = AcApp.GetSystemVariable("CELWEIGHT"); } catch { }
            try { oldCeLtype = AcApp.GetSystemVariable("CELTYPE"); } catch { }
            try
            {
                AcApp.SetSystemVariable("CECOLOR", "256");
                AcApp.SetSystemVariable("CELWEIGHT", (short)-1);
                AcApp.SetSystemVariable("CELTYPE", "ByLayer");
            }
            catch { }

            try
            {
            int ankCnt = 0;
            int ptsCount = 0, outerPtsCount = 0, hitCnt = 0, subCnt = 0;
            ObjectId outerPolyId = ObjectId.Null;
            ObjectId yaziStyleIdSaved = ObjectId.Null;
            ObjectId dimDetayIdSaved = ObjectId.Null;
            List<int> otherCatsSaved = null;

            using (doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    EnsureLayers(tr, db);
                    ObjectId yaziStyleId = EnsureYaziTextStyle(tr, db);
                    ObjectId dimPlanId = EnsurePlanOlcuDimStyle(tr, db, yaziStyleId);
                    ObjectId dimDetayId = EnsurePlanOlcuDetayDimStyle(tr, db, yaziStyleId);
                    yaziStyleIdSaved = yaziStyleId;
                    dimDetayIdSaved = dimDetayId;

                    var innerEnt = (Entity)tr.GetObject(r1.ObjectId, OpenMode.ForRead);
                    var buildEnt = (Entity)tr.GetObject(r2.ObjectId, OpenMode.ForRead);
                    var innerPl = innerEnt as Polyline;
                    var buildPl = buildEnt as Polyline;
                    if (innerPl == null || !innerPl.Closed)
                    {
                        ed.WriteMessage("\nPolyline kapali degil veya gecersiz.");
                        tr.Commit();
                        return;
                    }

                    var pts = IskelePlanGeometry.RemoveCollinear(IskelePlanGeometry.LwPolyVertices(innerPl));
                    if (pts == null || pts.Count == 0)
                    {
                        ed.WriteMessage("\nVertex bulunamadi.");
                        tr.Commit();
                        return;
                    }
                    ptsCount = pts.Count;
                    ed.WriteMessage("\nOrijinal vertex: {0}", IskelePlanGeometry.LwPolyVertices(innerPl).Count.ToString(CultureInfo.InvariantCulture));
                    ed.WriteMessage("\nTemizlenmis vertex: {0}", pts.Count.ToString(CultureInfo.InvariantCulture));

                    var cen = IskelePlanGeometry.CentroidAvg(pts);
                    // Lisp'teki dış taraf yönü hesabı: ilk kenara göre (CCW/CW flip dahil) outPt üret.
                    var pOn = pts[0];
                    var dir = IskelePlanGeometry.VSub(pts[1], pOn);
                    var nrm = IskelePlanGeometry.VUnit(new Point3d(-dir.Y, dir.X, 0));
                    if (IskelePlanGeometry.SignedArea(pts) > 0)
                        nrm = IskelePlanGeometry.VScale(nrm, -1);
                    var outPt = IskelePlanGeometry.VAdd(pOn, IskelePlanGeometry.VScale(nrm, 1000.0));

                    var outerPl = IskelePlanGeometry.OffsetOutside(innerPl, DFar, outPt);
                    if (outerPl == null)
                    {
                        ed.WriteMessage("\nOffset basarisiz!");
                        tr.Commit();
                        return;
                    }
                    ms.AppendEntity(outerPl);
                    tr.AddNewlyCreatedDBObject(outerPl, true);
                    outerPolyId = outerPl.ObjectId;

                    var outerPts = IskelePlanGeometry.LwPolyVertices(outerPl);
                    outerPtsCount = outerPts.Count;
                    double outerSA = IskelePlanGeometry.SignedArea(outerPts);
                    ed.WriteMessage("\nDis vertex: {0}", outerPts.Count.ToString(CultureInfo.InvariantCulture));

                    hitCnt = 0;
                    var allVertexPts = new List<Point3d>(pts);
                    allVertexPts.AddRange(outerPts);

                    foreach (var p in pts)
                        IskelePlanDrawing.DrawDikme(tr, ms, p, LayerFlans);
                    foreach (var p in outerPts)
                        IskelePlanDrawing.DrawDikme(tr, ms, p, LayerFlans);

                    foreach (var p in pts)
                    {
                        foreach (var pOut in IskelePlanGeometry.CirclePolyTangents(p, DFar, outerPts))
                        {
                            if (!IskelePlanGeometry.CanSkipTangent(pOut, outerPts, allVertexPts))
                            {
                                IskelePlanDrawing.DrawYatay(tr, ms, p, pOut, LayerYatay);
                                IskelePlanDrawing.DrawDikme(tr, ms, pOut, LayerFlans);
                                hitCnt++;
                            }
                        }
                        foreach (var pOut in IskelePlanGeometry.CirclePolyTangents(p, DFar, pts))
                        {
                            if (p.DistanceTo(pOut) > 5.0 && !IskelePlanGeometry.CanSkipTangent(pOut, pts, allVertexPts))
                            {
                                IskelePlanDrawing.DrawYatay(tr, ms, p, pOut, LayerYatay);
                                IskelePlanDrawing.DrawDikme(tr, ms, pOut, LayerFlans);
                                hitCnt++;
                            }
                        }
                    }

                    foreach (var p in outerPts)
                    {
                        foreach (var pOut in IskelePlanGeometry.CirclePolyTangents(p, DFar, pts))
                        {
                            if (!IskelePlanGeometry.CanSkipTangent(pOut, pts, allVertexPts))
                            {
                                IskelePlanDrawing.DrawYatay(tr, ms, p, pOut, LayerYatay);
                                IskelePlanDrawing.DrawDikme(tr, ms, pOut, LayerFlans);
                                hitCnt++;
                            }
                        }
                        foreach (var pOut in IskelePlanGeometry.CirclePolyTangents(p, DFar, outerPts))
                        {
                            if (p.DistanceTo(pOut) > 5.0 && !IskelePlanGeometry.CanSkipTangent(pOut, outerPts, allVertexPts))
                            {
                                IskelePlanDrawing.DrawYatay(tr, ms, p, pOut, LayerYatay);
                                IskelePlanDrawing.DrawDikme(tr, ms, pOut, LayerFlans);
                                hitCnt++;
                            }
                        }
                    }

                    var innerDikmePts = new List<Point3d>(pts);
                    var outerDikmePts = new List<Point3d>(outerPts);

                    foreach (var p in outerPts)
                    {
                        foreach (var pOut in IskelePlanGeometry.CirclePolyTangents(p, DFar, pts))
                        {
                            if (!IskelePlanGeometry.CanSkipTangent(pOut, pts, allVertexPts))
                                innerDikmePts.Add(pOut);
                        }
                    }
                    foreach (var p in pts)
                    {
                        foreach (var pOut in IskelePlanGeometry.CirclePolyTangents(p, DFar, outerPts))
                        {
                            if (!IskelePlanGeometry.CanSkipTangent(pOut, outerPts, allVertexPts))
                                outerDikmePts.Add(pOut);
                        }
                    }

                    var sortedDists = innerDikmePts
                        .Select(dp => (d: IskelePlanGeometry.DistAlongPoly(dp, pts), p: dp))
                        .OrderBy(x => x.d)
                        .ToList();
                    double totalLen = IskelePlanGeometry.TotalPolyLen(pts);
                    subCnt = 0;

                    for (int j = 0; j < sortedDists.Count; j++)
                    {
                        double d1 = sortedDists[j].d;
                        double d2 = j + 1 < sortedDists.Count
                            ? sortedDists[j + 1].d
                            : sortedDists[0].d + totalLen;
                        double gap = d2 - d1;
                        if (gap > Bay + 1.0)
                        {
                            double dd = d1 + Bay;
                            while (dd < d2 - 1.0)
                            {
                                double lastGap = d2 - dd;
                                if (lastGap < 70.0)
                                    dd = d2 - 70.0;
                                double rem = dd;
                                while (rem < 0) rem += totalLen;
                                while (rem >= totalLen) rem -= totalLen;
                                var newPt = IskelePlanGeometry.PtAtDistOnPoly(rem, pts);
                                var nrm2 = IskelePlanGeometry.OutwardNormalAt(newPt, pts, cen);
                                var outerPt = IskelePlanGeometry.VAdd(newPt, IskelePlanGeometry.VScale(nrm2, DFar));
                                IskelePlanDrawing.DrawDikme(tr, ms, newPt, LayerFlans);
                                IskelePlanDrawing.DrawDikme(tr, ms, outerPt, LayerFlans);
                                IskelePlanDrawing.DrawYatay(tr, ms, newPt, outerPt, LayerYatay);
                                innerDikmePts.Add(newPt);
                                outerDikmePts.Add(outerPt);
                                subCnt++;
                                if (lastGap < 70.0)
                                    dd = d2 + 1.0;
                                else
                                    dd += Bay;
                            }
                        }
                    }

                    var sortedInner = innerDikmePts
                        .Select(dp => (d: IskelePlanGeometry.DistAlongPoly(dp, pts), p: dp))
                        .OrderBy(x => x.d)
                        .ToList();

                    for (int k = 0; k < sortedInner.Count - 1; k++)
                        IskelePlanDrawing.DrawYatay(tr, ms, sortedInner[k].p, sortedInner[k + 1].p, LayerYatay);
                    if (sortedInner.Count > 1)
                        IskelePlanDrawing.DrawYatay(tr, ms, sortedInner[sortedInner.Count - 1].p, sortedInner[0].p, LayerYatay);

                    var sortedOuter = outerDikmePts
                        .Select(dp => (d: IskelePlanGeometry.DistAlongPoly(dp, outerPts), p: dp))
                        .OrderBy(x => x.d)
                        .ToList();

                    for (int k = 0; k < sortedOuter.Count - 1; k++)
                        IskelePlanDrawing.DrawYatay(tr, ms, sortedOuter[k].p, sortedOuter[k + 1].p, LayerYatay);
                    if (sortedOuter.Count > 1)
                        IskelePlanDrawing.DrawYatay(tr, ms, sortedOuter[sortedOuter.Count - 1].p, sortedOuter[0].p, LayerYatay);

                    var yataySegs = new List<(Point3d a, Point3d b, double len)>();
                    for (int k = 0; k < sortedOuter.Count; k++)
                    {
                        var pa = sortedOuter[k].p;
                        var pb = k + 1 < sortedOuter.Count ? sortedOuter[k + 1].p : sortedOuter[0].p;
                        yataySegs.Add((pa, pb, pa.DistanceTo(pb)));
                    }

                    var otherCats = new List<int>();
                    foreach (var seg in yataySegs)
                    {
                        double segLen = seg.len;
                        if ((segLen < 65 || segLen > 75) && (segLen < 245 || segLen > 255))
                        {
                            int roundLen = (int)Math.Round(seg.len);
                            if (!otherCats.Contains(roundLen))
                                otherCats.Add(roundLen);
                        }
                    }
                    otherCats.Sort();

                    var labelMap = new Dictionary<int, int>();
                    int nextYNum = 3;
                    foreach (int c in otherCats)
                        labelMap[c] = nextYNum++;

                    foreach (var seg in yataySegs)
                    {
                        var p = seg.a;
                        var pOut = seg.b;
                        double segLen = seg.len;
                        int roundLen = (int)Math.Round(segLen);
                        int yNum;
                        if (segLen >= 65 && segLen <= 75) yNum = 1;
                        else if (segLen >= 245 && segLen <= 255) yNum = 2;
                        else if (labelMap.TryGetValue(roundLen, out int yn)) yNum = yn;
                        else yNum = 0;

                        var dirU = IskelePlanGeometry.VUnit(IskelePlanGeometry.VSub(pOut, p));
                        var perp = new Point3d(-dirU.Y, dirU.X, 0);
                        if (outerSA > 0) perp = IskelePlanGeometry.VScale(perp, -1);
                        var midPt = new Point3d((p.X + pOut.X) * 0.5, (p.Y + pOut.Y) * 0.5, 0);
                        var txtPt = IskelePlanGeometry.VAdd(midPt, IskelePlanGeometry.VScale(perp, 10.0));
                        double ang = Math.Atan2(dirU.Y, dirU.X);
                        if (ang > Math.PI / 2 || ang < -Math.PI / 2) ang += Math.PI;
                        IskelePlanDrawing.AddOuterSegmentLabel(tr, ms, txtPt, ang, "Y" + yNum.ToString(CultureInfo.InvariantCulture) + " (D48.3/3.2)", yaziStyleId, LayerYazi);
                    }

                    for (int k = 0; k < sortedOuter.Count; k++)
                    {
                        var p = sortedOuter[k].p;
                        var pOut = k + 1 < sortedOuter.Count ? sortedOuter[k + 1].p : sortedOuter[0].p;
                        var midPt = new Point3d((p.X + pOut.X) * 0.5, (p.Y + pOut.Y) * 0.5, 0);
                        var dirU = IskelePlanGeometry.VUnit(IskelePlanGeometry.VSub(pOut, p));
                        var perp = new Point3d(-dirU.Y, dirU.X, 0);
                        if (outerSA > 0) perp = IskelePlanGeometry.VScale(perp, -1);
                        var dimLinePt = IskelePlanGeometry.VAdd(midPt, IskelePlanGeometry.VScale(perp, 40.0));
                        var dim = new AlignedDimension(p, pOut, dimLinePt, "", dimPlanId) { Layer = LayerOlcu };
                        IskelePlanDrawing.Append(tr, ms, dim);
                    }

                    if (buildPl != null)
                    {
                        var buildPts = IskelePlanGeometry.LwPolyVertices(buildPl);
                        var ankCandidates = new List<(Point3d p, Point3d closePt, int segIdx)>();
                        for (int k = 0; k < sortedInner.Count; k++)
                        {
                            var p = sortedInner[k].p;
                            var closePt = IskelePlanGeometry.ClosestPerpOnPoly(p, buildPts);
                            if (closePt.HasValue && p.DistanceTo(closePt.Value) > 8 && p.DistanceTo(closePt.Value) <= 45)
                            {
                                int segIdx = IskelePlanGeometry.FindSegIdx(closePt.Value, buildPts);
                                ankCandidates.Add((p, closePt.Value, segIdx));
                            }
                        }
                        foreach (var cand in ankCandidates)
                        {
                            IskelePlanDrawing.DrawAnkraj(tr, ms, cand.p, cand.closePt, LayerAnkraj, LayerBolt);
                            IskelePlanDrawing.AddAnkrajLabel(tr, ms, cand.closePt, cand.p, yaziStyleId, LayerYazi);
                            ankCnt++;
                        }
                        ed.WriteMessage("\nAnkraj: {0}", ankCnt.ToString(CultureInfo.InvariantCulture));
                    }

                    otherCatsSaved = otherCats;

                    if (allVertexPts.Count > 0)
                    {
                        double bMinX = allVertexPts.Min(v => v.X);
                        double bMaxX = allVertexPts.Max(v => v.X);
                        double bMinY = allVertexPts.Min(v => v.Y);
                        double titleCx = (bMinX + bMaxX) * 0.5;
                        double titleYTop = bMinY - 120.0;
                        var titleTxt = new DBText
                        {
                            Layer = LayerKesitIsmi,
                            Height = 20.0,
                            TextStyleId = yaziStyleId,
                            TextString = "\u0130SKELE PLANI (1:50)",
                            HorizontalMode = TextHorizontalMode.TextCenter,
                            VerticalMode = TextVerticalMode.TextTop,
                            Position = new Point3d(titleCx, titleYTop, 0),
                            AlignmentPoint = new Point3d(titleCx, titleYTop, 0),
                            LineWeight = LineWeight.LineWeight020
                        };
                        try { titleTxt.AdjustAlignment(db); } catch { }
                        ms.AppendEntity(titleTxt);
                        tr.AddNewlyCreatedDBObject(titleTxt, true);
                    }

                    tr.Commit();
                }

                var ppo = new PromptPointOptions("\nDetay cizimi icin baslangic noktasi secin (veya ESC): ");
                ppo.AllowNone = true;
                var prPt = ed.GetPoint(ppo);
                if (prPt.Status == PromptStatus.OK)
                {
                    var detayBase = new Point3d(prPt.Value.X, prPt.Value.Y, 0);
                    using (var tr2 = db.TransactionManager.StartTransaction())
                    {
                        var bt2 = (BlockTable)tr2.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var ms2 = (BlockTableRecord)tr2.GetObject(bt2[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        double detayXOff = 0;
                        var detPt = new Point3d(detayBase.X + detayXOff, detayBase.Y, 0);
                        IskelePlanDrawing.DrawDikmeDetay(tr2, ms2, detPt, 1370, 5, new[] { 250.0, 500, 750, 1000, 1250 }, true, "D1",
                            dimDetayIdSaved, yaziStyleIdSaved, LayerIskeleDetay, LayerDetayGizli, LayerOlcu, LayerYazi);
                        detayXOff += 250;
                        detPt = new Point3d(detayBase.X + detayXOff, detayBase.Y, 0);
                        IskelePlanDrawing.DrawDikmeDetay(tr2, ms2, detPt, 1250, 0, new[] { 125.0, 375, 625, 875, 1125 }, false, "D2",
                            dimDetayIdSaved, yaziStyleIdSaved, LayerIskeleDetay, LayerDetayGizli, LayerOlcu, LayerYazi);
                        detayXOff += 230;
                        detPt = new Point3d(detayBase.X + detayXOff, detayBase.Y, 0);
                        IskelePlanDrawing.DrawCaprazDetay(tr2, ms2, detPt, "C1", dimDetayIdSaved, yaziStyleIdSaved, LayerIskeleDetay, LayerDetayGizli, LayerOlcu, LayerYazi);
                        detayXOff += 200;
                        detPt = new Point3d(detayBase.X + detayXOff, detayBase.Y, 0);
                        IskelePlanDrawing.DrawYatayDetay(tr2, ms2, detPt, 70, "Y1", dimDetayIdSaved, yaziStyleIdSaved, LayerIskeleDetay, LayerDetayGizli, LayerOlcu, LayerYazi);
                        detayXOff += 160;
                        detPt = new Point3d(detayBase.X + detayXOff, detayBase.Y, 0);
                        IskelePlanDrawing.DrawYatayDetay(tr2, ms2, detPt, 250, "Y2", dimDetayIdSaved, yaziStyleIdSaved, LayerIskeleDetay, LayerDetayGizli, LayerOlcu, LayerYazi);
                        detayXOff += 160;
                        int detayYNum = 3;
                        foreach (int roundLen in otherCatsSaved ?? new List<int>())
                        {
                            detPt = new Point3d(detayBase.X + detayXOff, detayBase.Y, 0);
                            IskelePlanDrawing.DrawYatayDetay(tr2, ms2, detPt, roundLen, "Y" + detayYNum.ToString(CultureInfo.InvariantCulture),
                                dimDetayIdSaved, yaziStyleIdSaved, LayerIskeleDetay, LayerDetayGizli, LayerOlcu, LayerYazi);
                            detayXOff += 160;
                            detayYNum++;
                        }
                        tr2.Commit();
                    }
                    ed.WriteMessage("\nDetay: D1, D2, C1 + {0} adet yatay eleman.", (2 + (otherCatsSaved?.Count ?? 0)).ToString(CultureInfo.InvariantCulture));
                }

                using (var tr3 = db.TransactionManager.StartTransaction())
                {
                    var e1 = (Entity)tr3.GetObject(r1.ObjectId, OpenMode.ForWrite);
                    var e2 = (Entity)tr3.GetObject(outerPolyId, OpenMode.ForWrite);
                    e1.Erase();
                    e2.Erase();
                    tr3.Commit();
                }

                ed.WriteMessage("\n\nTamamlandi. Ana dikme: {0} | Ara dikme (250): {1} | Ankraj: {2}",
                    (ptsCount + outerPtsCount + hitCnt).ToString(CultureInfo.InvariantCulture),
                    subCnt.ToString(CultureInfo.InvariantCulture),
                    ankCnt.ToString(CultureInfo.InvariantCulture));

                IskeleCizContextStore.SetActive();
            }

            doc.SendStringToExecute("_.ZOOM _E ", true, false, false);
            }
            finally
            {
                try { if (oldLayer != null) AcApp.SetSystemVariable("CLAYER", oldLayer); } catch { }
                try { if (oldCeColor != null) AcApp.SetSystemVariable("CECOLOR", oldCeColor); } catch { }
                try { if (oldCeWeight != null) AcApp.SetSystemVariable("CELWEIGHT", oldCeWeight); } catch { }
                try { if (oldCeLtype != null) AcApp.SetSystemVariable("CELTYPE", oldCeLtype); } catch { }
            }
        }

        private static void EnsureLayers(Transaction tr, Database db)
        {
            EnsureLayer(tr, db, LayerYazi, 4, "Continuous", LineWeight.LineWeight020);
            EnsureLayer(tr, db, LayerIskeleDetay, 4, "Continuous", LineWeight.LineWeight025);
            EnsureLayer(tr, db, LayerOlcu, 14, "Continuous", LineWeight.LineWeight020);
            EnsureLayer(tr, db, LayerCapraz, 140, "Continuous", LineWeight.LineWeight020);
            TryLoadLinetype(tr, db, "HIDDEN2");
            EnsureLayer(tr, db, LayerDetayGizli, 8, "HIDDEN2", LineWeight.LineWeight015);
            EnsureLayer(tr, db, LayerFlansDetay, 3, "Continuous", LineWeight.LineWeight020);
            EnsureLayer(tr, db, LayerFlans, 160, "Continuous", LineWeight.LineWeight020);
            EnsureLayer(tr, db, LayerYatay, 210, "Continuous", LineWeight.LineWeight030);
            EnsureLayer(tr, db, LayerBolt, 5, "Continuous", LineWeight.LineWeight020);
            EnsureLayer(tr, db, LayerAnkraj, 95, "Continuous", LineWeight.LineWeight020);
            EnsureLayer(tr, db, LayerDoseme, 71, "Continuous", LineWeight.LineWeight030);
            EnsureLayer(tr, db, LayerKesitIsmi, 6, "Continuous", LineWeight.LineWeight020);
        }

        private static void TryLoadLinetype(Transaction tr, Database db, string name)
        {
            try
            {
                var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (lt.Has(name)) return;
                db.LoadLineTypeFile(name, "acadiso.lin");
            }
            catch { }
        }

        private static ObjectId EnsureLayer(Transaction tr, Database db, string name, short color, string linetype, LineWeight lw)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];
            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, color)
            };
            try
            {
                var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (ltt.Has(linetype)) rec.LinetypeObjectId = ltt[linetype];
            }
            catch { }
            try { rec.LineWeight = lw; } catch { }
            ObjectId id = lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            lt.DowngradeOpen();
            return id;
        }

        private static ObjectId EnsureYaziTextStyle(Transaction tr, Database db)
        {
            var ts = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (ts.Has(TextStyleYazi)) return ts[TextStyleYazi];
            ts.UpgradeOpen();
            var rec = new TextStyleTableRecord { Name = TextStyleYazi };
            try
            {
                rec.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Bahnschrift Light Condensed", false, false, 0, 0);
            }
            catch
            {
                try { rec.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Arial", false, false, 0, 0); } catch { }
            }
            ObjectId id = ts.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            ts.DowngradeOpen();
            return id;
        }

        private static ObjectId EnsurePlanOlcuDimStyle(Transaction tr, Database db, ObjectId yaziStyleId)
        {
            var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (dst.Has(DimPlanOlcu)) return dst[DimPlanOlcu];
            dst.UpgradeOpen();
            var rec = new DimStyleTableRecord { Name = DimPlanOlcu };
            try { if (!yaziStyleId.IsNull) rec.Dimtxsty = yaziStyleId; } catch { }
            try { rec.Dimasz = 3.0; } catch { }
            try { rec.Dimexo = 3.0; } catch { }
            try { rec.Dimdle = 0.5; } catch { }
            try { rec.Dimtih = false; } catch { }
            try { rec.Dimtoh = false; } catch { }
            try { rec.Dimtad = 1; } catch { }
            try { rec.Dimzin = 12; } catch { }
            try { rec.Dimtxt = 12.0; } catch { }
            try { rec.Dimtsz = 3.0; } catch { }
            try { rec.Dimgap = 2.0; } catch { }
            try { rec.Dimtofl = true; } catch { }
            try { rec.Dimtix = true; } catch { }
            try { rec.Dimclrt = Color.FromColorIndex(ColorMethod.ByAci, 7); } catch { }
            try { rec.Dimclrd = Color.FromColorIndex(ColorMethod.ByLayer, 256); } catch { }
            try { rec.Dimclre = Color.FromColorIndex(ColorMethod.ByLayer, 256); } catch { }
            try { rec.Dimdec = 0; } catch { }
            try { rec.Dimlfac = 1.0; } catch { }
            ObjectId id = dst.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            dst.DowngradeOpen();
            return id;
        }

        private static ObjectId EnsurePlanOlcuDetayDimStyle(Transaction tr, Database db, ObjectId yaziStyleId)
        {
            var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (dst.Has(DimPlanOlcuDetay)) return dst[DimPlanOlcuDetay];
            dst.UpgradeOpen();
            var rec = new DimStyleTableRecord { Name = DimPlanOlcuDetay };
            try { if (!yaziStyleId.IsNull) rec.Dimtxsty = yaziStyleId; } catch { }
            try { rec.Dimasz = 6.0; } catch { }
            try { rec.Dimexo = 3.0; } catch { }
            try { rec.Dimtih = false; } catch { }
            try { rec.Dimtoh = false; } catch { }
            try { rec.Dimtad = 1; } catch { }
            try { rec.Dimzin = 12; } catch { }
            try { rec.Dimtxt = 12.0; } catch { }
            try { rec.Dimlfac = 2.0; } catch { }
            try { rec.Dimgap = 2.0; } catch { }
            try { rec.Dimtofl = true; } catch { }
            try { rec.Dimtix = true; } catch { }
            try { rec.Dimclrt = Color.FromColorIndex(ColorMethod.ByAci, 7); } catch { }
            try { rec.Dimclrd = Color.FromColorIndex(ColorMethod.ByLayer, 256); } catch { }
            try { rec.Dimclre = Color.FromColorIndex(ColorMethod.ByLayer, 256); } catch { }
            try { rec.Dimdec = 1; } catch { }
            try { rec.Dimtdec = 1; } catch { }
            ObjectId id = dst.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            dst.DowngradeOpen();
            return id;
        }
    }
}
