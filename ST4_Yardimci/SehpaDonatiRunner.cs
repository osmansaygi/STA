using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Gömülü SEHPA.dwg. Ölçüler PLAN_OLCU_DONATI 25, patlatılmaz.
    /// 1:25 = gerçek cm ×2. Temel kalınlığı, sehpa yüksekliği ve min genişlik formdan.
    /// Radye: yükseklik = kalınlık − (paspayı + 2). Ayak grobetonda kalır.
    /// Mantar: yükseklik = kalınlık − 2×paspayı − 2 cm.
    /// Kesitte üst çizgi döşeme üstünden (paspayı+3) cm altta, ayak alt kottan (paspayı+3) cm üstte.
    /// </summary>
    internal static class SehpaDonatiRunner
    {
        private const string EmbeddedResourceName = "ST4Yardimci.Sehpa.SEHPA.dwg";
        private const string LayerKotYazi = "KOT YAZI (BEYKENT)";
        private const string LayerDonati = "DONATI (BEYKENT)";
        private const string LayerDonatiIzi = "DONATI IZI (BEYKENT)";
        private const string DimStyleName = "PLAN_OLCU_DONATI 25";

        private const double Scale = 2.0;
        private const double TemplateKalinlikCm = 40.0;
        private const double TemplateSehpaYukCm = 33.0;
        private const double TemplateMinGenislikCm = 30.0;
        private const double TemplateAyakCm = 27.0;
        /// <summary>Sol ayak ucu − bir iç nokta (şablon, dışa doğru).</summary>
        private const double LeftFootDx = 1960.950836444688 - 2009.720298397104;
        private const double LeftFootDy = 1051.616346728539 - 1069.366979220396;
        /// <summary>Sağ ayak ucu − bir iç nokta (şablon, dışa doğru).</summary>
        private const double RightFootDx = 2118.81919673672 - 2072.101855644258;
        private const double RightFootDy = 1068.03331360817 - 1051.029592026452;
        private static readonly double LeftFootLen = Math.Sqrt(LeftFootDx * LeftFootDx + LeftFootDy * LeftFootDy);
        private static readonly double RightFootLen = Math.Sqrt(RightFootDx * RightFootDx + RightFootDy * RightFootDy);

        /// <summary>Şablon kesitinde bu Y ve üstü, kalınlık artınca yukarı gider.</summary>
        private const double YTop = 1110.0;
        private const double YMid = 1076.0;
        /// <summary>Kesit sehpasının sağ yarısı (üst tabla sağ ucu ve sağ ayak).</summary>
        private const double ChairRightX = 1680.0;
        /// <summary>Çubuk açılımında sağ taraf (üst tabla sağ ucu, sağ düşey, sağ ayak).</summary>
        private const double BarRightX = 2040.0;
        private const double ZoneBarX = 1940.0;
        private const double ZonePlanX = 2200.0;
        /// <summary>Şablon kesit: beton alt / üst ve sehpa ayak / üst tabla (paspayı 5 cm).</summary>
        private const double YConcreteBot = 1054.193120160993;
        private const double YConcreteTop = 1134.193120160993;
        private const double TemplatePasCm = 5.0;
        private const double TemplateChairFootY = 1056.193120160993;
        private const double TemplateChairTopY = 1118.193120160993;
        /// <summary>Mantar kesiti: sehpa, döşeme yüzünden paspayı+3 cm içeride.</summary>
        private const double MantarFaceExtraCm = 3.0;

        // Şablon çubuk üst tablası yönü (etiket 30). Uzunluk çizim birimi.
        private const double BarTopDx = 50.381557247154;
        private const double BarTopDy = -18.337387193944;
        private static readonly double BarTopLen = Math.Sqrt(BarTopDx * BarTopDx + BarTopDy * BarTopDy);

        private static string _tempDwg;
        private static readonly object TempLock = new object();

        public static void RunInteractive()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                SehpaDonatiForm.Shutdown();
                return;
            }

            Editor ed = doc.Editor;
            try
            {
                string path = EnsureTempDwg();
                if (string.IsNullOrEmpty(path))
                {
                    ed.WriteMessage("\nSEHPADONATICIZ: gömülü SEHPA.dwg bulunamadı.");
                    return;
                }

                var opt = new PromptPointOptions("\nSehpa — sol-üst köşe: ")
                {
                    AllowNone = false
                };
                PromptPointResult ppr = ed.GetPoint(opt);
                if (ppr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nSEHPADONATICIZ: iptal.");
                    return;
                }

                int n;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    n = InsertTemplate(tr, doc.Database, path, ppr.Value);
                    tr.Commit();
                }

                double sehpa = SehpaDonatiSettings.SehpaYuksekligiCm;
                ComputeBar(out _, out _, out int toplam, out int adet, out _, out _, out _);
                ed.WriteMessage(
                    "\nSEHPADONATICIZ: kalınlık {0:0.#} cm, paspayı {1:0.#} cm, sehpa yük. {2:0.#} cm, min genişlik {3} cm, L={4} cm, adet {5} ({6} nesne).",
                    SehpaDonatiSettings.KalinlikCm, SehpaDonatiSettings.PaspayiCm, sehpa,
                    SehpaDonatiSettings.MinGenislikCm, toplam, adet, n);
            }
            catch (Exception ex)
            {
                try { ed.WriteMessage("\nSEHPADONATICIZ hata: {0}", ex.Message); }
                catch { }
            }
            finally
            {
                SehpaDonatiForm.Shutdown();
            }
        }

        private static int InsertTemplate(Transaction tr, Database destDb, string path, Point3d pick)
        {
            var bt = (BlockTable)tr.GetObject(destDb.BlockTableId, OpenMode.ForRead);
            var msDest = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            double dH = (SehpaDonatiSettings.KalinlikCm - TemplateKalinlikCm) * Scale;
            double sehpaCm = Math.Round(SehpaDonatiSettings.SehpaYuksekligiCm, MidpointRounding.AwayFromZero);
            if (sehpaCm < 1) sehpaCm = 1;
            double dS = (sehpaCm - TemplateSehpaYukCm) * Scale;
            double dW = (SehpaDonatiSettings.MinGenislikCm - TemplateMinGenislikCm) * Scale;
            string ustS = FormatKot(SehpaDonatiSettings.UstKotM);
            string altS = FormatKot(SehpaDonatiSettings.UstKotM - SehpaDonatiSettings.KalinlikCm / 100.0);
            string sehpaS = Math.Round(sehpaCm, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
            string minS = Math.Round((double)SehpaDonatiSettings.MinGenislikCm, MidpointRounding.AwayFromZero)
                .ToString("0", CultureInfo.InvariantCulture);
            ComputeBar(out int ayakSol, out _, out _, out _, out string etiket, out double leftDu, out double rightDu);
            double ux = BarTopDx / BarTopLen;
            double uy = BarTopDy / BarTopLen;

            using (var src = new Database(false, true))
            {
                src.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
                Database prev = HostApplicationServices.WorkingDatabase;
                HostApplicationServices.WorkingDatabase = src;
                ObjectIdCollection ids;
                try
                {
                    using (var trs = src.TransactionManager.StartTransaction())
                    {
                        var btSrc = (BlockTable)trs.GetObject(src.BlockTableId, OpenMode.ForRead);
                        var ms = (BlockTableRecord)trs.GetObject(btSrc[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        ApplyGeometry(trs, ms, dH, dS, dW, ux, uy, ustS, altS, sehpaS, minS,
                            ayakSol.ToString(CultureInfo.InvariantCulture), etiket, leftDu, rightDu);
                        ids = new ObjectIdCollection();
                        foreach (ObjectId id in ms)
                        {
                            if (!id.IsValid || id.IsErased) continue;
                            if (trs.GetObject(id, OpenMode.ForRead) is Entity)
                                ids.Add(id);
                        }
                        trs.Commit();
                    }
                }
                finally
                {
                    HostApplicationServices.WorkingDatabase = prev;
                }

                if (ids.Count == 0)
                    return 0;

                var mapping = new IdMapping();
                destDb.WblockCloneObjects(ids, msDest.ObjectId, mapping, DuplicateRecordCloning.Ignore, false);

                var cloned = new List<Entity>();
                double minX = double.MaxValue, maxY = double.MinValue;
                bool any = false;
                foreach (IdPair pair in mapping)
                {
                    if (!pair.IsCloned || !pair.Value.IsValid) continue;
                    if (!(tr.GetObject(pair.Value, OpenMode.ForWrite, false) is Entity ent)) continue;
                    if (ent.IsErased) continue;
                    cloned.Add(ent);
                    try
                    {
                        Extents3d e = ent.GeometricExtents;
                        if (e.MinPoint.X < minX) minX = e.MinPoint.X;
                        if (e.MaxPoint.Y > maxY) maxY = e.MaxPoint.Y;
                        any = true;
                    }
                    catch { }
                }

                if (any && cloned.Count > 0)
                {
                    var move = Matrix3d.Displacement(new Vector3d(pick.X - minX, pick.Y - maxY, 0));
                    ObjectId dimStyle = LookUpDimStyle(tr, destDb);
                    foreach (Entity ent in cloned)
                    {
                        try { ent.TransformBy(move); } catch { }
                        if (ent is Dimension dim)
                            FinishDimension(dim, dimStyle);
                    }
                }
                return cloned.Count;
            }
        }

        private static void ComputeBar(
            out int ayakSol, out int ayakSag, out int toplam, out int adet,
            out string etiket, out double leftDu, out double rightDu)
        {
            int hCm = (int)Math.Round(SehpaDonatiSettings.SehpaYuksekligiCm, MidpointRounding.AwayFromZero);
            if (hCm < 1) hCm = 1;
            int wCm = (int)Math.Round((double)SehpaDonatiSettings.MinGenislikCm, MidpointRounding.AwayFromZero);
            int minAyak = IsMantar() ? 20 : 25;
            ayakSol = minAyak;
            ayakSag = minAyak;
            for (int i = 0; i < 40; i++)
            {
                toplam = ayakSol + ayakSag + 2 * hCm + wCm;
                if (toplam % 10 == 0) break;
                if (ayakSol <= ayakSag) ayakSol++;
                else ayakSag++;
            }
            toplam = ayakSol + ayakSag + 2 * hCm + wCm;
            adet = (int)Math.Ceiling(SehpaDonatiSettings.AlanM2 - 1e-9);
            if (adet < 1) adet = 1;
            etiket = string.Format(CultureInfo.InvariantCulture, "{0}\u00F8{1} L={2}", adet, SehpaDonatiSettings.CapMm, toplam);
            leftDu = (ayakSol - TemplateAyakCm) * Scale;
            rightDu = (ayakSag - TemplateAyakCm) * Scale;
        }

        private static void ApplyGeometry(
            Transaction trs, BlockTableRecord ms,
            double dH, double dS, double dW, double ux, double uy,
            string ustS, string altS, string sehpaS, string minS,
            string ayakS, string etiket, double leftDu, double rightDu)
        {
            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased) continue;
                DBObject obj = trs.GetObject(id, OpenMode.ForRead);
                if (IsMantar() && RemoveMantarGrobeton(obj))
                    continue;
                if (obj is DBText tx)
                    ApplyText(tx, dH, dS, dW, ux, uy, ustS, altS, sehpaS, minS, ayakS, etiket, leftDu);
                else if (obj is Line ln)
                    MapLine(ln, LineMap(ln, dH));
                else if (obj is Polyline pl)
                {
                    if (LayerEquals(pl.Layer, LayerDonatiIzi))
                        MapPolyline(pl, p => RebarPoint(p, dH));
                    else
                    {
                        MapPolyline(pl, PolyMap(pl, dH, dS, dW, ux, uy));
                        ExtendFeet(pl, leftDu, rightDu);
                    }
                }
                else if (obj is RotatedDimension rd)
                    StretchSectionDim(rd, dH);
                else if (obj is Hatch hatch)
                    StretchHatch(hatch, dH);
            }
        }

        /// <summary>
        /// Mantar kesitinde grobeton taraması ve alt çizgisi yok.
        /// Kesit sınırı döşeme altında kesilir. true = nesne silindi.
        /// </summary>
        private static bool RemoveMantarGrobeton(DBObject obj)
        {
            if (obj is Hatch hatch)
            {
                try
                {
                    Extents3d e = hatch.GeometricExtents;
                    if (e.MaxPoint.X >= ZoneBarX || e.MaxPoint.Y > YConcreteBot + 1.0)
                        return false;
                    if (!hatch.IsWriteEnabled) hatch.UpgradeOpen();
                    hatch.Erase();
                    return true;
                }
                catch { return false; }
            }

            if (obj is DBText tx)
            {
                string plain = (tx.TextString ?? "").Trim();
                if (!plain.Equals("GROBETON", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!tx.IsWriteEnabled) tx.UpgradeOpen();
                tx.Erase();
                return true;
            }

            if (!(obj is Line ln) || ln.StartPoint.X >= ZoneBarX || ln.EndPoint.X >= ZoneBarX)
                return false;

            double y0 = Math.Min(ln.StartPoint.Y, ln.EndPoint.Y);
            double y1 = Math.Max(ln.StartPoint.Y, ln.EndPoint.Y);
            if (y1 > YConcreteBot + 1.0 && y0 < YConcreteBot - 1.0)
            {
                if (!ln.IsWriteEnabled) ln.UpgradeOpen();
                if (ln.StartPoint.Y <= ln.EndPoint.Y)
                    ln.StartPoint = new Point3d(ln.StartPoint.X, YConcreteBot, ln.StartPoint.Z);
                else
                    ln.EndPoint = new Point3d(ln.EndPoint.X, YConcreteBot, ln.EndPoint.Z);
                return false;
            }
            if (y1 < YConcreteBot - 1.0)
            {
                if (!ln.IsWriteEnabled) ln.UpgradeOpen();
                ln.Erase();
                return true;
            }
            return false;
        }

        private static void ApplyText(
            DBText tx, double dH, double dS, double dW, double ux, double uy,
            string ustS, string altS, string sehpaS, string minS,
            string ayakS, string etiket, double leftDu)
        {
            if (!tx.IsWriteEnabled) tx.UpgradeOpen();
            double x0 = tx.Position.X;
            double y0 = tx.Position.Y;
            string plain = (tx.TextString ?? "").Trim();
            double x = x0, y = y0;

            if (plain == "33")
            {
                tx.TextString = sehpaS;
                y += dS * 0.5;
                if (x0 >= BarRightX)
                {
                    x += ux * dW;
                    y += uy * dW;
                }
            }
            else if (plain == "30" && x0 >= ZoneBarX && x0 < ZonePlanX)
            {
                tx.TextString = minS;
                y += dS;
                x += ux * dW * 0.5;
                y += uy * dW * 0.5;
            }
            else if (plain == "27" && x0 >= ZoneBarX && x0 < ZonePlanX)
            {
                tx.TextString = ayakS;
                double half = leftDu * 0.5;
                x += (LeftFootDx / LeftFootLen) * half;
                y += (LeftFootDy / LeftFootLen) * half;
            }
            else if (plain.IndexOf("L=", StringComparison.OrdinalIgnoreCase) >= 0
                && x0 >= ZoneBarX && x0 < ZonePlanX)
            {
                tx.TextString = etiket;
            }
            else if (plain.IndexOf("bindirilmemeli", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (IsMantar())
                    tx.TextString = "Sehpa, alt donatı üzerine oturtulmalıdır.";
            }
            else if (plain.IndexOf("oturtulmal", StringComparison.OrdinalIgnoreCase) >= 0
                && plain.IndexOf("grobeton", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (IsMantar())
                    tx.TextString = "";
            }
            else if (LayerEquals(tx.Layer, LayerKotYazi))
            {
                bool upper = y0 >= YTop;
                tx.TextString = upper ? ustS : altS;
                if (upper) y += dH;
            }
            else
            {
                Zone z = ZoneOf(x0);
                if (z == Zone.Section)
                {
                    if (IsMantar() && y0 >= YMid && y0 < YTop && x0 > 1600 && x0 < 1800)
                    {
                        MantarChairYs(dH, out double foot, out double top);
                        double mid0 = (TemplateChairFootY + TemplateChairTopY) * 0.5;
                        y += (foot + top) * 0.5 - mid0;
                        x += dW * 0.5;
                    }
                    else if (y0 >= YTop) y += dH;
                    else if (y0 >= YMid)
                    {
                        y += dH * 0.5;
                        if (x0 > 1600 && x0 < 1800)
                            x += dW * 0.5;
                    }
                }
            }

            double dx = x - x0, dy = y - y0;
            if (Math.Abs(dx) > 1e-6 || Math.Abs(dy) > 1e-6)
                tx.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
        }

        private static Func<Point2d, Point2d> PolyMap(Polyline pl, double dH, double dS, double dW, double ux, double uy)
        {
            double x = pl.NumberOfVertices > 0 ? pl.GetPoint2dAt(0).X : 0;
            Zone z = ZoneOf(x);
            bool chair = z == Zone.Section && LayerEquals(pl.Layer, LayerDonati) && pl.NumberOfVertices >= 8;
            bool bar = z == Zone.Bar && LayerEquals(pl.Layer, LayerDonati) && pl.NumberOfVertices >= 8;
            if (chair && IsMantar())
            {
                MantarChairYs(dH, out double foot, out double top);
                return p =>
                {
                    double py = p.Y >= YTop
                        ? top + (p.Y - TemplateChairTopY)
                        : foot + (p.Y - TemplateChairFootY);
                    double px = p.X >= ChairRightX ? p.X + dW : p.X;
                    return new Point2d(px, py);
                };
            }
            if (chair)
            {
                return p =>
                {
                    double py = p.Y >= YTop ? p.Y + dS : p.Y;
                    double px = p.X >= ChairRightX ? p.X + dW : p.X;
                    return new Point2d(px, py);
                };
            }
            if (bar)
            {
                return p =>
                {
                    double py = p.Y + (p.Y >= 1100.0 ? dS : 0);
                    double px = p.X;
                    if (p.X >= BarRightX)
                    {
                        px += ux * dW;
                        py += uy * dW;
                    }
                    return new Point2d(px, py);
                };
            }
            return MapFor(z, dH);
        }

        /// <summary>
        /// Ayak uçlarını nominal (ayak − 27) cm kadar dışa uzatır. Çizim birimi = cm × 2.
        /// Kesit ayakları yatay; çubuk açılımı şablon yönünde.
        /// </summary>
        private static void ExtendFeet(Polyline pl, double leftDu, double rightDu)
        {
            if (Math.Abs(leftDu) < 1e-6 && Math.Abs(rightDu) < 1e-6) return;
            if (!LayerEquals(pl.Layer, LayerDonati) || pl.NumberOfVertices < 8) return;
            Zone z = ZoneOf(pl.GetPoint2dAt(0).X);
            if (z == Zone.Plan) return;
            if (!pl.IsWriteEnabled) pl.UpgradeOpen();
            int last = pl.NumberOfVertices - 1;
            Point2d tipL = pl.GetPoint2dAt(0);
            Point2d tipR = pl.GetPoint2dAt(last);
            if (z == Zone.Section)
            {
                pl.SetPointAt(0, new Point2d(tipL.X - leftDu, tipL.Y));
                pl.SetPointAt(last, new Point2d(tipR.X + rightDu, tipR.Y));
                return;
            }
            pl.SetPointAt(0, new Point2d(
                tipL.X + LeftFootDx / LeftFootLen * leftDu,
                tipL.Y + LeftFootDy / LeftFootLen * leftDu));
            pl.SetPointAt(last, new Point2d(
                tipR.X + RightFootDx / RightFootLen * rightDu,
                tipR.Y + RightFootDy / RightFootLen * rightDu));
        }

        private static bool IsMantar() =>
            SehpaDonatiSettings.Tip == SehpaDonatiTip.MantarDoseme;

        /// <summary>
        /// Mantar kesiti: üst çizgi döşeme üstünden (paspayı+3) cm altta,
        /// ayak döşeme alt kotundan (paspayı+3) cm üstte.
        /// </summary>
        private static void MantarChairYs(double dH, out double foot, out double top)
        {
            double inset = (SehpaDonatiSettings.PaspayiCm + MantarFaceExtraCm) * Scale;
            top = YConcreteTop + dH - inset;
            foot = YConcreteBot + inset;
            if (top < foot + 8.0)
            {
                double mid = (YConcreteBot + YConcreteTop + dH) * 0.5;
                foot = mid - 4.0;
                top = mid + 4.0;
            }
        }

        private static double PasShift() =>
            (SehpaDonatiSettings.PaspayiCm - TemplatePasCm) * Scale;

        private static Func<Point2d, Point2d> LineMap(Line ln, double dH)
        {
            if (LayerEquals(ln.Layer, LayerDonatiIzi))
                return p => RebarPoint(p, dH);
            return MapFor(ZoneOf(ln.StartPoint.X), dH);
        }

        /// <summary>Donatı izi: alt çizgi beton altı + paspayı, üst çizgi beton üstü − paspayı.</summary>
        private static Point2d RebarPoint(Point2d p, double dH)
        {
            if (p.X >= ZoneBarX) return p;
            double dPas = PasShift();
            if (p.Y >= 1100.0)
                return new Point2d(p.X, p.Y + dH - dPas);
            if (p.Y >= 1040.0)
                return new Point2d(p.X, p.Y + dPas);
            return p;
        }

        private static Func<Point2d, Point2d> MapFor(Zone z, double dH)
        {
            if (z != Zone.Section)
                return p => p;
            return p => p.Y >= YTop ? new Point2d(p.X, p.Y + dH) : p;
        }

        private static void StretchSectionDim(RotatedDimension rd, double dH)
        {
            if (rd.XLine1Point.X >= ZoneBarX) return;
            if (!rd.IsWriteEnabled) rd.UpgradeOpen();
            rd.XLine1Point = Lift(rd.XLine1Point, dH);
            rd.XLine2Point = Lift(rd.XLine2Point, dH);
            rd.DimLinePoint = Lift(rd.DimLinePoint, dH);
            try { rd.DimensionText = "<>"; } catch { }
            try { rd.UsingDefaultTextPosition = true; } catch { }
            try { rd.RecomputeDimensionBlock(true); } catch { }
        }

        private static Point3d Lift(Point3d p, double dH)
        {
            if (p.Y >= YTop)
                return new Point3d(p.X, p.Y + dH, p.Z);
            return p;
        }

        private static void StretchHatch(Hatch hatch, double dH)
        {
            try
            {
                Extents3d e = hatch.GeometricExtents;
                if (e.MinPoint.Y < YTop || e.MaxPoint.X >= ZoneBarX) return;
                if (!hatch.IsWriteEnabled) hatch.UpgradeOpen();
                hatch.TransformBy(Matrix3d.Displacement(new Vector3d(0, dH, 0)));
            }
            catch { }
        }

        private static void MapLine(Line ln, Func<Point2d, Point2d> map)
        {
            var a = map(new Point2d(ln.StartPoint.X, ln.StartPoint.Y));
            var b = map(new Point2d(ln.EndPoint.X, ln.EndPoint.Y));
            if (Math.Abs(a.X - ln.StartPoint.X) < 1e-6 && Math.Abs(a.Y - ln.StartPoint.Y) < 1e-6
                && Math.Abs(b.X - ln.EndPoint.X) < 1e-6 && Math.Abs(b.Y - ln.EndPoint.Y) < 1e-6)
                return;
            if (!ln.IsWriteEnabled) ln.UpgradeOpen();
            ln.StartPoint = new Point3d(a.X, a.Y, ln.StartPoint.Z);
            ln.EndPoint = new Point3d(b.X, b.Y, ln.EndPoint.Z);
        }

        private static void MapPolyline(Polyline pl, Func<Point2d, Point2d> map)
        {
            if (map == null) return;
            bool changed = false;
            var next = new Point2d[pl.NumberOfVertices];
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point2d p = pl.GetPoint2dAt(i);
                next[i] = map(p);
                if (next[i].GetDistanceTo(p) > 1e-6) changed = true;
            }
            if (!changed) return;
            if (!pl.IsWriteEnabled) pl.UpgradeOpen();
            for (int i = 0; i < pl.NumberOfVertices; i++)
                pl.SetPointAt(i, next[i]);
        }

        private static void FinishDimension(Dimension dim, ObjectId dimStyle)
        {
            try
            {
                if (!dimStyle.IsNull)
                    dim.DimensionStyle = dimStyle;
            }
            catch { }
            try { dim.RecomputeDimensionBlock(true); } catch { }
        }

        private static ObjectId LookUpDimStyle(Transaction tr, Database db)
        {
            try
            {
                var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                if (dst.Has(DimStyleName))
                    return dst[DimStyleName];
            }
            catch { }
            return ObjectId.Null;
        }

        private enum Zone { Section, Bar, Plan }

        private static Zone ZoneOf(double x)
        {
            if (x < ZoneBarX) return Zone.Section;
            if (x < ZonePlanX) return Zone.Bar;
            return Zone.Plan;
        }

        private static string FormatKot(double m)
        {
            double r = Math.Round(m, 2, MidpointRounding.AwayFromZero);
            return r.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static bool LayerEquals(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string EnsureTempDwg()
        {
            lock (TempLock)
            {
                Assembly asm = typeof(SehpaDonatiRunner).Assembly;
                using (Stream stream = asm.GetManifestResourceStream(EmbeddedResourceName))
                {
                    if (stream == null) return null;
                    string path = Path.Combine(Path.GetTempPath(),
                        "ST4Yardimci_sehpa_" + asm.ManifestModule.ModuleVersionId.ToString("N") + ".dwg");
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
