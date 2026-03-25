using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// iskele_plan.lsp çizim primitifleri (VLA/entmakex karşılığı).
    /// </summary>
    internal static class IskelePlanDrawing
    {
        public static void Append(Transaction tr, BlockTableRecord btr, Entity e)
        {
            btr.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
        }

        public static void DrawDikme(Transaction tr, BlockTableRecord btr, Point3d pt, string layerName)
        {
            double cx = pt.X, cy = pt.Y;
            void C(double r)
            {
                var c = new Circle(new Point3d(cx, cy, 0), Vector3d.ZAxis, r) { Layer = layerName };
                Append(tr, btr, c);
            }
            C(6.5); C(2.8); C(2.5);

            double bR = 4.337;
            for (int i = 0; i < 4; i++)
            {
                double posAng = (90.0 + i * 90.0) * Math.PI / 180.0;
                double rotAng = (i * 90.0) * Math.PI / 180.0;
                double bx = cx + bR * Math.Cos(posAng);
                double by = cy + bR * Math.Sin(posAng);

                var rp = IskelePlanGeometry.Rot2d(-0.000731, 0.134217, rotAng);
                var hc = new Circle(new Point3d(bx + rp.X, by + rp.Y, 0), Vector3d.ZAxis, 0.9978) { Layer = layerName };
                Append(tr, btr, hc);

                var pl = new Polyline { Layer = layerName };
                void V(double lx, double ly, double bulge)
                {
                    var r2 = IskelePlanGeometry.Rot2d(lx, ly, rotAng);
                    pl.AddVertexAt(pl.NumberOfVertices, new Point2d(bx + r2.X, by + r2.Y), bulge, 0, 0);
                }
                V(-2.103918, 0.713340, 0);
                V(-1.339525, -1.132067, -0.200347);
                V(1.336909, -1.131413, 0);
                V(2.103918, 0.715993, 0.198391);
                V(-2.103918, 0.713340, 0);
                Append(tr, btr, pl);
            }
        }

        public static void DrawYatay(Transaction tr, BlockTableRecord btr, Point3d p1, Point3d p2, string layerName)
        {
            double d = p1.DistanceTo(p2);
            if (d <= 13.0) return;
            var dir = IskelePlanGeometry.VUnit(IskelePlanGeometry.VSub(p2, p1));
            var perp = new Point3d(-dir.Y, dir.X, 0);
            var sp = IskelePlanGeometry.VAdd(p1, IskelePlanGeometry.VScale(dir, 6.0));
            var ep = IskelePlanGeometry.VSub(p2, IskelePlanGeometry.VScale(dir, 6.0));
            var s1 = IskelePlanGeometry.VAdd(sp, IskelePlanGeometry.VScale(perp, 2.5));
            var e1 = IskelePlanGeometry.VAdd(ep, IskelePlanGeometry.VScale(perp, 2.5));
            var s2 = IskelePlanGeometry.VSub(sp, IskelePlanGeometry.VScale(perp, 2.5));
            var e2 = IskelePlanGeometry.VSub(ep, IskelePlanGeometry.VScale(perp, 2.5));
            Append(tr, btr, new Line(s1, e1) { Layer = layerName });
            Append(tr, btr, new Line(s2, e2) { Layer = layerName });
        }

        public static void DrawAnkraj(Transaction tr, BlockTableRecord btr, Point3d dikmePt, Point3d buildPt,
            string ankrajLayer, string boltLayer)
        {
            double d = dikmePt.DistanceTo(buildPt);
            if (d < 8.0) return;
            var dir = IskelePlanGeometry.VUnit(IskelePlanGeometry.VSub(buildPt, dikmePt));
            var perp = new Point3d(-dir.Y, dir.X, 0);
            var bracketTop = buildPt;
            var bracketBot = IskelePlanGeometry.VSub(buildPt, IskelePlanGeometry.VScale(dir, 0.8));
            var bracketCen = IskelePlanGeometry.VSub(buildPt, IskelePlanGeometry.VScale(dir, 0.4));
            var sp = IskelePlanGeometry.VAdd(dikmePt, IskelePlanGeometry.VScale(dir, 6.0));
            var s1 = IskelePlanGeometry.VAdd(sp, IskelePlanGeometry.VScale(perp, 2.5));
            var e1 = IskelePlanGeometry.VAdd(bracketBot, IskelePlanGeometry.VScale(perp, 2.5));
            var s2 = IskelePlanGeometry.VSub(sp, IskelePlanGeometry.VScale(perp, 2.5));
            var e2 = IskelePlanGeometry.VSub(bracketBot, IskelePlanGeometry.VScale(perp, 2.5));
            Append(tr, btr, new Line(s1, e1) { Layer = ankrajLayer });
            Append(tr, btr, new Line(s2, e2) { Layer = ankrajLayer });
            var bl = IskelePlanGeometry.VSub(bracketBot, IskelePlanGeometry.VScale(perp, 7.5));
            var br = IskelePlanGeometry.VAdd(bracketBot, IskelePlanGeometry.VScale(perp, 7.5));
            var tl = IskelePlanGeometry.VSub(bracketTop, IskelePlanGeometry.VScale(perp, 7.5));
            var trp = IskelePlanGeometry.VAdd(bracketTop, IskelePlanGeometry.VScale(perp, 7.5));
            Append(tr, btr, new Line(bl, br) { Layer = ankrajLayer });
            Append(tr, btr, new Line(tl, trp) { Layer = ankrajLayer });
            Append(tr, btr, new Line(bl, tl) { Layer = ankrajLayer });
            Append(tr, btr, new Line(br, trp) { Layer = ankrajLayer });

            void BoltAt(Point3d boltC)
            {
                var bTop = IskelePlanGeometry.VAdd(boltC, IskelePlanGeometry.VScale(dir, 1.75));
                var bBot = IskelePlanGeometry.VSub(boltC, IskelePlanGeometry.VScale(dir, 1.75));
                Append(tr, btr, new Line(bTop, bBot) { Layer = boltLayer });
                var eL = IskelePlanGeometry.VSub(boltC, IskelePlanGeometry.VScale(perp, 0.9));
                var eR = IskelePlanGeometry.VAdd(boltC, IskelePlanGeometry.VScale(perp, 0.9));
                Append(tr, btr, new Line(
                    IskelePlanGeometry.VAdd(eL, IskelePlanGeometry.VScale(dir, 0.4)),
                    IskelePlanGeometry.VSub(eL, IskelePlanGeometry.VScale(dir, 0.4))) { Layer = boltLayer });
                Append(tr, btr, new Line(
                    IskelePlanGeometry.VAdd(eR, IskelePlanGeometry.VScale(dir, 0.4)),
                    IskelePlanGeometry.VSub(eR, IskelePlanGeometry.VScale(dir, 0.4))) { Layer = boltLayer });
                var p10 = IskelePlanGeometry.VAdd(eL, IskelePlanGeometry.VScale(dir, 0.4));
                var p11 = IskelePlanGeometry.VAdd(eR, IskelePlanGeometry.VScale(dir, 0.4));
                var p12 = IskelePlanGeometry.VSub(eL, IskelePlanGeometry.VScale(dir, 0.4));
                var p13 = IskelePlanGeometry.VSub(eR, IskelePlanGeometry.VScale(dir, 0.4));
                Append(tr, btr, new Solid(p10, p11, p12, p13) { Layer = boltLayer });
            }
            var boltC1 = IskelePlanGeometry.VSub(bracketCen, IskelePlanGeometry.VScale(perp, 4.0));
            var boltC2 = IskelePlanGeometry.VAdd(bracketCen, IskelePlanGeometry.VScale(perp, 4.0));
            BoltAt(boltC1);
            BoltAt(boltC2);
        }

        public static void DrawYatayDetay(Transaction tr, BlockTableRecord btr, Point3d basePt, double lengthCm, string label,
            ObjectId dimDetayStyleId, ObjectId yaziStyleId, string detayLayer, string detayGizliLayer, string olcuLayer, string yaziLayer)
        {
            double H = lengthCm * 5.0;
            double cx = basePt.X, by = basePt.Y;
            double R = 12.075, Ri = 10.825, fHW = 1.25, fH = 20.0;
            double eRS = 5.0, eRE = 15.0, eRiS = 4.696, eRiE = 15.303;
            double bT = -0.25, bI = -0.2357;

            var outer = new Polyline { Layer = detayLayer, Closed = true };
            void Vo(double x, double y, double bulge) => outer.AddVertexAt(outer.NumberOfVertices, new Point2d(x, y), bulge, 0, 0);
            Vo(cx - fHW, by + fH, 0);
            Vo(cx + fHW, by + fH, 0);
            Vo(cx + fHW, by, 0);
            Vo(cx + R, by, 0);
            Vo(cx + R, by + eRS, bT);
            Vo(cx + R, by + eRE, 0);
            Vo(cx + R, by + (H - eRE), bT);
            Vo(cx + R, by + (H - eRS), 0);
            Vo(cx + R, by + H, 0);
            Vo(cx + fHW, by + H, 0);
            Vo(cx + fHW, by + (H - fH), 0);
            Vo(cx - fHW, by + (H - fH), 0);
            Vo(cx - fHW, by + H, 0);
            Vo(cx - R, by + H, 0);
            Vo(cx - R, by + (H - eRS), bT);
            Vo(cx - R, by + (H - eRE), 0);
            Vo(cx - R, by + eRE, bT);
            Vo(cx - R, by + eRS, 0);
            Vo(cx - R, by, 0);
            Vo(cx - fHW, by, 0);
            Append(tr, btr, outer);

            var inner = new Polyline { Layer = detayGizliLayer, Closed = true };
            void Vi(double x, double y, double bulge) => inner.AddVertexAt(inner.NumberOfVertices, new Point2d(x, y), bulge, 0, 0);
            Vi(cx + Ri, by, 0);
            Vi(cx + Ri, by + eRiS, bI);
            Vi(cx + Ri, by + eRiE, 0);
            Vi(cx + Ri, by + (H - eRiE), bI);
            Vi(cx + Ri, by + (H - eRiS), 0);
            Vi(cx + Ri, by + H, 0);
            Vi(cx - Ri, by + H, 0);
            Vi(cx - Ri, by + (H - eRiS), bI);
            Vi(cx - Ri, by + (H - eRiE), 0);
            Vi(cx - Ri, by + eRiE, bI);
            Vi(cx - Ri, by + eRiS, 0);
            Vi(cx - Ri, by, 0);
            Append(tr, btr, inner);

            var d1 = new AlignedDimension(
                new Point3d(cx + R, by, 0), new Point3d(cx + R, by + H, 0),
                new Point3d(cx + R + 40, by + H * 0.5, 0), "", dimDetayStyleId) { Layer = olcuLayer };
            d1.DimensionText = Math.Round(lengthCm * 10.0, 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Append(tr, btr, d1);

            double dimY = by + H * 0.6;
            var d2 = new AlignedDimension(
                new Point3d(cx - R, dimY, 0), new Point3d(cx + R, dimY, 0),
                new Point3d(cx, dimY + 15, 0), "", dimDetayStyleId) { Layer = olcuLayer };
            d2.DimensionText = "48.3";
            Append(tr, btr, d2);

            var txt = new DBText
            {
                Layer = yaziLayer,
                TextString = label + " (D48.3/3.2)",
                Position = new Point3d(cx - R - 14, by + 82, 0),
                Height = 15.0,
                Rotation = Math.PI / 2.0,
                TextStyleId = yaziStyleId
            };
            Append(tr, btr, txt);
        }

        public static void DrawDikmeDetay(Transaction tr, BlockTableRecord btr, Point3d basePt, double tubeH, double tubeStartDy,
            double[] plateYs, bool hasBase, string label, ObjectId dimDetayStyleId, ObjectId yaziStyleId,
            string detayLayer, string detayGizliLayer, string olcuLayer, string yaziLayer)
        {
            double cx = basePt.X, by = basePt.Y;
            double R = 12.075, Ri = 10.825, flangeR = 10.825, flangeRi = 9.575, plateHW = 32.075;
            double flangeStart = tubeStartDy + tubeH - 25.0;
            double flangeEnd = tubeStartDy + tubeH + 100.0;
            double totalH = flangeEnd;

            var tube = new Polyline { Layer = detayLayer, Closed = true };
            tube.AddVertexAt(0, new Point2d(cx - R, by + tubeStartDy), 0, 0, 0);
            tube.AddVertexAt(1, new Point2d(cx + R, by + tubeStartDy), 0, 0, 0);
            tube.AddVertexAt(2, new Point2d(cx + R, by + tubeStartDy + tubeH), 0, 0, 0);
            tube.AddVertexAt(3, new Point2d(cx - R, by + tubeStartDy + tubeH), 0, 0, 0);
            Append(tr, btr, tube);

            var tubeIn = new Polyline { Layer = detayGizliLayer, Closed = true };
            tubeIn.AddVertexAt(0, new Point2d(cx - Ri, by + tubeStartDy), 0, 0, 0);
            tubeIn.AddVertexAt(1, new Point2d(cx + Ri, by + tubeStartDy), 0, 0, 0);
            tubeIn.AddVertexAt(2, new Point2d(cx + Ri, by + tubeStartDy + tubeH), 0, 0, 0);
            tubeIn.AddVertexAt(3, new Point2d(cx - Ri, by + tubeStartDy + tubeH), 0, 0, 0);
            Append(tr, btr, tubeIn);

            var fOut = new Polyline { Layer = detayLayer, Closed = true };
            fOut.AddVertexAt(0, new Point2d(cx - flangeR, by + flangeStart), 0, 0, 0);
            fOut.AddVertexAt(1, new Point2d(cx + flangeR, by + flangeStart), 0, 0, 0);
            fOut.AddVertexAt(2, new Point2d(cx + flangeR, by + flangeEnd), 0, 0, 0);
            fOut.AddVertexAt(3, new Point2d(cx - flangeR, by + flangeEnd), 0, 0, 0);
            Append(tr, btr, fOut);

            var fIn = new Polyline { Layer = detayGizliLayer, Closed = true };
            fIn.AddVertexAt(0, new Point2d(cx - flangeRi, by + flangeStart), 0, 0, 0);
            fIn.AddVertexAt(1, new Point2d(cx + flangeRi, by + flangeStart), 0, 0, 0);
            fIn.AddVertexAt(2, new Point2d(cx + flangeRi, by + flangeEnd), 0, 0, 0);
            fIn.AddVertexAt(3, new Point2d(cx - flangeRi, by + flangeEnd), 0, 0, 0);
            Append(tr, btr, fIn);

            foreach (var py in plateYs)
            {
                double y = by + py;
                var pl = new Polyline { Layer = detayLayer, Closed = true };
                pl.AddVertexAt(0, new Point2d(cx - plateHW, y + 1.25), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(cx + plateHW, y + 1.25), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(cx + plateHW, y - 1.25), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(cx - plateHW, y - 1.25), 0, 0, 0);
                Append(tr, btr, pl);
            }

            if (hasBase)
            {
                double y0 = by;
                Append(tr, btr, new Line(new Point3d(cx - 45, y0, 0), new Point3d(cx - 45, y0 + 5, 0)) { Layer = detayLayer });
                Append(tr, btr, new Line(new Point3d(cx + 45, y0, 0), new Point3d(cx + 45, y0 + 5, 0)) { Layer = detayLayer });
                Append(tr, btr, new Line(new Point3d(cx - 45, y0 + 5, 0), new Point3d(cx + 45, y0 + 5, 0)) { Layer = detayLayer });
                Append(tr, btr, new Line(new Point3d(cx - 45, y0, 0), new Point3d(cx + 45, y0, 0)) { Layer = detayLayer });
                Append(tr, btr, new Line(new Point3d(cx + 17.175, y0 + 5, 0), new Point3d(cx + 12.175, y0 + 10, 0)) { Layer = detayLayer });
                Append(tr, btr, new Line(new Point3d(cx + 22.175, y0 + 55, 0), new Point3d(cx + 42.075, y0 + 20, 0)) { Layer = detayLayer });
                Append(tr, btr, new Line(new Point3d(cx + 42.075, y0 + 20, 0), new Point3d(cx + 42.075, y0 + 5, 0)) { Layer = detayLayer });
                Append(tr, btr, new Line(new Point3d(cx + 12.175, y0 + 55, 0), new Point3d(cx + 22.175, y0 + 55, 0)) { Layer = detayLayer });
                Append(tr, btr, new Line(new Point3d(cx + 42.075, y0 + 5, 0), new Point3d(cx + 17.175, y0 + 5, 0)) { Layer = detayGizliLayer });
                Append(tr, btr, new Line(new Point3d(cx - 17.175, y0 + 5, 0), new Point3d(cx - 12.175, y0 + 10, 0)) { Layer = detayLayer });
                Append(tr, btr, new Line(new Point3d(cx - 12.175, y0 + 55, 0), new Point3d(cx - 22.175, y0 + 55, 0)) { Layer = detayLayer });
                Append(tr, btr, new Line(new Point3d(cx - 22.175, y0 + 55, 0), new Point3d(cx - 42.075, y0 + 20, 0)) { Layer = detayLayer });
                Append(tr, btr, new Line(new Point3d(cx - 42.075, y0 + 20, 0), new Point3d(cx - 42.075, y0 + 5, 0)) { Layer = detayLayer });
                Append(tr, btr, new Line(new Point3d(cx - 42.075, y0 + 5, 0), new Point3d(cx - 17.175, y0 + 5, 0)) { Layer = detayGizliLayer });
            }

            var dm1 = new AlignedDimension(
                new Point3d(cx + R + 5, by, 0), new Point3d(cx + R + 5, by + totalH, 0),
                new Point3d(cx + R + 45, by + totalH * 0.5, 0), "", dimDetayStyleId) { Layer = olcuLayer };
            dm1.DimensionText = Math.Round(totalH * 2.0, 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Append(tr, btr, dm1);

            double ty = by + totalH * 0.6;
            var dm2 = new AlignedDimension(
                new Point3d(cx - R, ty, 0), new Point3d(cx + R, ty, 0),
                new Point3d(cx, ty + 15, 0), "", dimDetayStyleId) { Layer = olcuLayer };
            dm2.DimensionText = "48.3";
            Append(tr, btr, dm2);

            var txt = new DBText
            {
                Layer = yaziLayer,
                TextString = label + " (D48.3/3.2)",
                Position = new Point3d(cx - R - 14, by + 82, 0),
                Height = 15.0,
                Rotation = Math.PI / 2.0,
                TextStyleId = yaziStyleId
            };
            Append(tr, btr, txt);
        }

        public static void DrawCaprazDetay(Transaction tr, BlockTableRecord btr, Point3d basePt, string label,
            ObjectId dimDetayStyleId, ObjectId yaziStyleId, string detayLayer, string detayGizliLayer, string olcuLayer, string yaziLayer)
        {
            double cx = basePt.X, by = basePt.Y;
            double R = 12.075, Ri = 10.825;
            double totalH = 1767.77;
            double tubeBot = 46.43, tubeTop = totalH - 46.43;
            double forkR = 14.808, arcR = 8.244, boltR = 6.315;
            double arcCenDx = 6.566, arcCenDy = 40.29;
            Vector3d z = Vector3d.ZAxis;

            void L(Point3d a, Point3d b, string layer)
            {
                Append(tr, btr, new Line(a, b) { Layer = layer });
            }

            L(new Point3d(cx - R, by + tubeBot, 0), new Point3d(cx - R, by + tubeTop, 0), detayLayer);
            L(new Point3d(cx + R, by + tubeBot, 0), new Point3d(cx + R, by + tubeTop, 0), detayLayer);
            L(new Point3d(cx - Ri, by + tubeBot, 0), new Point3d(cx - Ri, by + tubeTop, 0), detayGizliLayer);
            L(new Point3d(cx + Ri, by + tubeBot, 0), new Point3d(cx + Ri, by + tubeTop, 0), detayGizliLayer);
            L(new Point3d(cx - R, by + tubeBot, 0), new Point3d(cx + R, by + tubeBot, 0), detayLayer);
            L(new Point3d(cx - R, by + tubeTop, 0), new Point3d(cx + R, by + tubeTop, 0), detayLayer);

            L(new Point3d(cx + forkR, by + arcCenDy, 0), new Point3d(cx + forkR, by, 0), detayLayer);
            L(new Point3d(cx - forkR, by, 0), new Point3d(cx - forkR, by + arcCenDy, 0), detayLayer);
            Append(tr, btr, new Arc(new Point3d(cx, by, 0), z, forkR, Math.PI, Math.PI * 2) { Layer = detayLayer });
            Append(tr, btr, new Circle(new Point3d(cx, by, 0), z, boltR) { Layer = detayLayer });
            Append(tr, btr, new Arc(new Point3d(cx + arcCenDx, by + arcCenDy, 0), z, arcR, 1 * Math.PI / 180, 48 * Math.PI / 180) { Layer = detayLayer });
            Append(tr, btr, new Arc(new Point3d(cx - arcCenDx, by + arcCenDy, 0), z, arcR, 132 * Math.PI / 180, 179 * Math.PI / 180) { Layer = detayLayer });

            L(new Point3d(cx + forkR, by + totalH - arcCenDy, 0), new Point3d(cx + forkR, by + totalH, 0), detayLayer);
            L(new Point3d(cx - forkR, by + totalH, 0), new Point3d(cx - forkR, by + totalH - arcCenDy, 0), detayLayer);
            Append(tr, btr, new Arc(new Point3d(cx, by + totalH, 0), z, forkR, 0, Math.PI) { Layer = detayLayer });
            Append(tr, btr, new Circle(new Point3d(cx, by + totalH, 0), z, boltR) { Layer = detayLayer });
            Append(tr, btr, new Arc(new Point3d(cx + arcCenDx, by + totalH - arcCenDy, 0), z, arcR, 312 * Math.PI / 180, 359 * Math.PI / 180) { Layer = detayLayer });
            Append(tr, btr, new Arc(new Point3d(cx - arcCenDx, by + totalH - arcCenDy, 0), z, arcR, 181 * Math.PI / 180, 228 * Math.PI / 180) { Layer = detayLayer });

            var dm1 = new AlignedDimension(
                new Point3d(cx + R + 5, by, 0), new Point3d(cx + R + 5, by + totalH, 0),
                new Point3d(cx + R + 45, by + totalH * 0.5, 0), "", dimDetayStyleId) { Layer = olcuLayer };
            dm1.DimensionText = Math.Round(totalH * 2.0, 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Append(tr, btr, dm1);

            double my = by + totalH * 0.5;
            var dm2 = new AlignedDimension(
                new Point3d(cx - R, my, 0), new Point3d(cx + R, my, 0),
                new Point3d(cx, my + 15, 0), "", dimDetayStyleId) { Layer = olcuLayer };
            dm2.DimensionText = "48.3";
            Append(tr, btr, dm2);

            var txt = new DBText
            {
                Layer = yaziLayer,
                TextString = label + " (D48.3/3.2)",
                Position = new Point3d(cx - R - 14, by + 82, 0),
                Height = 15.0,
                Rotation = Math.PI / 2.0,
                TextStyleId = yaziStyleId
            };
            Append(tr, btr, txt);
        }

        public static void AddAnkrajLabel(Transaction tr, BlockTableRecord btr, Point3d closePt, Point3d p, ObjectId yaziStyleId, string yaziLayer)
        {
            var dir = IskelePlanGeometry.VUnit(IskelePlanGeometry.VSub(closePt, p));
            var perp = new Point3d(-dir.Y, dir.X, 0);
            var txtPt = IskelePlanGeometry.VAdd(closePt, IskelePlanGeometry.VScale(dir, 8.0));
            double ang = Math.Atan2(perp.Y, perp.X);
            if (ang > Math.PI / 2 || ang < -Math.PI / 2) ang += Math.PI;
            var t = new DBText
            {
                Layer = yaziLayer,
                TextString = "ANKRAJ",
                Height = 8.0,
                Rotation = ang,
                TextStyleId = yaziStyleId,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                AlignmentPoint = txtPt
            };
            Append(tr, btr, t);
        }

        public static void AddOuterSegmentLabel(Transaction tr, BlockTableRecord btr, Point3d txtPt, double ang, string text, ObjectId yaziStyleId, string yaziLayer)
        {
            var t = new DBText
            {
                Layer = yaziLayer,
                TextString = text,
                Height = 8.0,
                Rotation = ang,
                TextStyleId = yaziStyleId,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                AlignmentPoint = txtPt
            };
            Append(tr, btr, t);
        }
    }
}
