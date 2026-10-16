using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Gömülü HASIR.dwg (500×215). Yatay + dik uzunluk → paspayı düşülür,
    /// bindirmeli dizilim; kenar kesilir; etiket diagonal üzerinde; bindirme PLAN_OLCU.
    /// Blok bırakılmaz — her hasır ayrı entity (yön işareti bozulmasın).
    /// </summary>
    internal static class HasirDonatiRunner
    {
        private const string EmbeddedResourceName = "ST4Yardimci.Hasir.HASIR.dwg";
        private const string LayerHasir = "HASIR (BEYKENT)";
        private const string LayerHasirCizgi = "HASIR CIZGI (BEYKENT)";
        private const string LayerHasirYazi = "HASIR YAZI (BEYKENT)";
        private const string LayerHasirYon = "HASIR YONU (BEYKENT)";
        private const string LayerOlcu = "OLCU (BEYKENT)";
        private const string TextStyleYazi = "YAZI (BEYKENT)";
        private const string DimStylePlanOlcu = "PLAN_OLCU";
        private const double LabelHeightCm = 10.0;
        /// <summary>Yazı kutusu alt kenarı ile diagonal arasındaki boşluk (cm).</summary>
        private const double LabelGapCm = 1.0;
        /// <summary>Yön işareti, levha ortasından. 80 sola, 15 aşağı, sonra 40 yukarı → net +25.</summary>
        private const double YonSolaCm = 80.0;
        private const double YonYukariCm = 25.0;
        private const double YonYaziCm = 8.0;
        private const double YonYaziGapCm = 3.0;
        private const double MinRemnantCm = 2.0;
        private const double NominalBoyCm = 500.0;
        private const double NominalEnCm = 215.0;

        private static string _tempDwg;
        private static readonly object TempLock = new object();

        private struct SheetCell
        {
            public double U0, V0, W, H;
            public int Col, Row;
        }

        public static void RunInteractive()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                HasirDonatiForm.Shutdown();
                return;
            }

            Editor ed = doc.Editor;
            HasirDonatiSettings.EnsureLoaded();
            try
            {
                string path = EnsureTempDwg();
                if (string.IsNullOrEmpty(path))
                {
                    ed.WriteMessage("\nHASIRDONATICIZ: gömülü HASIR.dwg bulunamadı.");
                    return;
                }

                var opt1 = new PromptPointOptions("\nHasır alanı — yatay uzunluk birinci nokta: ")
                {
                    AllowNone = false
                };
                PromptPointResult p1 = ed.GetPoint(opt1);
                if (p1.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nHASIRDONATICIZ: iptal.");
                    return;
                }

                var opt2 = new PromptPointOptions("\nHasır alanı — yatay uzunluk ikinci nokta: ")
                {
                    BasePoint = p1.Value,
                    UseBasePoint = true,
                    AllowNone = false
                };
                PromptPointResult p2 = ed.GetPoint(opt2);
                if (p2.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nHASIRDONATICIZ: iptal.");
                    return;
                }

                Vector3d along = p2.Value - p1.Value;
                double lenAlong = along.Length;
                if (lenAlong < 10)
                {
                    ed.WriteMessage("\nHASIRDONATICIZ: yatay uzunluk çok kısa.");
                    return;
                }

                var opt3 = new PromptPointOptions("\nHasır alanı — dik uzunluk (kenar tarafı): ")
                {
                    BasePoint = p1.Value,
                    UseBasePoint = true,
                    AllowNone = false
                };
                PromptPointResult p3 = ed.GetPoint(opt3);
                if (p3.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nHASIRDONATICIZ: iptal.");
                    return;
                }

                Vector3d u = along / lenAlong;
                // Sağa bakan u için sol el: dik v (ekran üstü / alan içi)
                Vector3d vCand = new Vector3d(-u.Y, u.X, 0);
                if (vCand.Length < 1e-9)
                    vCand = Vector3d.ZAxis.CrossProduct(u);
                vCand = vCand.GetNormal();

                Vector3d fromStart = p3.Value - p1.Value;
                double side = fromStart.DotProduct(vCand);
                Vector3d v = side >= 0 ? vCand : -vCand;
                double lenPerp = Math.Abs(fromStart.DotProduct(v));
                // Köşe seçildiyse u bileşenini yok say; yalnızca dik mesafe
                if (lenPerp < 10)
                {
                    Vector3d fromMid = p3.Value - (p1.Value + along * 0.5);
                    lenPerp = Math.Abs(fromMid.DotProduct(v));
                }
                if (lenPerp < 10)
                {
                    ed.WriteMessage("\nHASIRDONATICIZ: dik uzunluk çok kısa.");
                    return;
                }

                double pas = Math.Max(0, HasirDonatiSettings.PaspayiCm);
                double usableU = lenAlong - 2.0 * pas;
                double usableV = lenPerp - 2.0 * pas;
                if (usableU < MinRemnantCm || usableV < MinRemnantCm)
                {
                    ed.WriteMessage("\nHASIRDONATICIZ: paspayı sonrası alan çok küçük.");
                    return;
                }

                Point3d origin = p1.Value + u * pas + v * pas;
                List<SheetCell> sheets = BuildLayout(
                    usableU, usableV,
                    HasirDonatiSettings.BoyCm,
                    HasirDonatiSettings.EnCm,
                    HasirDonatiSettings.BindirmeCm);

                if (sheets.Count == 0)
                {
                    ed.WriteMessage("\nHASIRDONATICIZ: yerleştirilecek hasır yok.");
                    return;
                }

                // Güvenlik: hiçbir hücre alan dışına taşmasın
                foreach (SheetCell s in sheets)
                {
                    if (s.U0 < -1e-6 || s.V0 < -1e-6 ||
                        s.U0 + s.W > usableU + 1e-3 ||
                        s.V0 + s.H > usableV + 1e-3)
                    {
                        ed.WriteMessage("\nHASIRDONATICIZ: yerleşim alanı aşıldı (iç hata).");
                        return;
                    }
                }

                int n;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    n = PlaceSheets(tr, doc.Database, path, origin, u, v, sheets, usableU, usableV);
                    tr.Commit();
                }

                ed.WriteMessage(
                    "\nHASIRDONATICIZ: {0} hasır ({1}), alan {2:0.#}×{3:0.#} cm, pas {4:0.#} cm, bindirme {5:0.#} cm.",
                    n,
                    HasirDonatiSettings.Tip,
                    usableU, usableV,
                    pas,
                    HasirDonatiSettings.BindirmeCm);
            }
            catch (Exception ex)
            {
                try { ed.WriteMessage("\nHASIRDONATICIZ hata: {0}", ex.Message); }
                catch { }
            }
            finally
            {
                HasirDonatiForm.Shutdown();
            }
        }

        /// <summary>
        /// [0,usable] aralığını boy/en ile bindirmeli kapla; son hücre kesilir.
        /// </summary>
        private static List<SheetCell> BuildLayout(
            double usableU, double usableV,
            double boy, double en, double bindirme)
        {
            var list = new List<SheetCell>();
            if (boy < MinRemnantCm || en < MinRemnantCm) return list;
            if (bindirme < 0) bindirme = 0;
            if (bindirme >= boy - MinRemnantCm)
                bindirme = Math.Max(0, boy * 0.2);
            if (bindirme >= en - MinRemnantCm)
                bindirme = Math.Max(0, en * 0.2);

            List<double> uStarts = BuildStarts(usableU, boy, bindirme);
            List<double> vStarts = BuildStarts(usableV, en, bindirme);

            for (int row = 0; row < vStarts.Count; row++)
            {
                double v0 = vStarts[row];
                double h = Math.Min(en, usableV - v0);
                if (h < MinRemnantCm) continue;
                // Yuvarlama taşmasını kes
                if (v0 + h > usableV) h = usableV - v0;
                if (h < MinRemnantCm) continue;

                for (int col = 0; col < uStarts.Count; col++)
                {
                    double u0 = uStarts[col];
                    double w = Math.Min(boy, usableU - u0);
                    if (w < MinRemnantCm) continue;
                    if (u0 + w > usableU) w = usableU - u0;
                    if (w < MinRemnantCm) continue;

                    list.Add(new SheetCell
                    {
                        U0 = u0,
                        V0 = v0,
                        W = w,
                        H = h,
                        Col = col,
                        Row = row
                    });
                }
            }
            return list;
        }

        /// <summary>
        /// 0'dan step=(size−lap) ile; son levha kesilir, bindirme sabit kalır.
        /// Örnek usable=831, size=500, lap=45 → starts 0, 455 (ikinci 376 cm).
        /// </summary>
        private static List<double> BuildStarts(double usable, double size, double lap)
        {
            var starts = new List<double>();
            if (usable < MinRemnantCm) return starts;

            double step = size - lap;
            if (step < MinRemnantCm) step = MinRemnantCm;

            double pos = 0;
            for (int n = 0; n < 500; n++)
            {
                starts.Add(pos);
                if (pos + size >= usable - 1e-6)
                    break;
                double next = pos + step;
                if (next >= usable - MinRemnantCm)
                    break;
                pos = next;
            }
            return starts;
        }

        private static int PlaceSheets(
            Transaction tr,
            Database destDb,
            string path,
            Point3d origin,
            Vector3d u,
            Vector3d v,
            List<SheetCell> sheets,
            double usableU,
            double usableV)
        {
            EnsureLayer(tr, destDb, LayerHasir, 4, LineWeight.LineWeight035);
            EnsureLayer(tr, destDb, LayerHasirCizgi, 1, LineWeight.LineWeight020);
            string yaziLyr = EnsureLayer(tr, destDb, LayerHasirYazi, 3, LineWeight.LineWeight020);
            EnsureLayer(tr, destDb, LayerHasirYon, 5, LineWeight.LineWeight025);
            string olcuLyr = EnsureLayer(tr, destDb, LayerOlcu, 14, LineWeight.LineWeight020);
            ObjectId textStyle = GetOrCreateTextStyle(tr, destDb);
            ObjectId dimStyle = EnsurePlanOlcuDimStyle(tr, destDb, textStyle);

            if (!TryGetTemplateExtents(path, out double tw, out double th, out double minX, out double minY))
                return 0;

            // Şablon eksenleri form boy/en ile uyumlu değilse (döndürülmüş çizim) nominali tercih et
            if (Math.Abs(tw - NominalBoyCm) > 30 && Math.Abs(th - NominalBoyCm) < 30)
            {
                // X kısa / Y uzun — yerleştirmede W→Y, H→X map edilmez; extents olduğu gibi kullanılır
            }
            // Mantıksal şablon boyutu: ölçülen extents (entity → dünya map'i)
            if (tw < 10 || th < 10) return 0;

            var bt = (BlockTable)tr.GetObject(destDb.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            string tip = HasirDonatiSettings.Tip ?? "Q188/188";
            double bindirme = HasirDonatiSettings.BindirmeCm;
            int placed = 0;

            foreach (SheetCell s in sheets)
            {
                // Alan dışına taşma engeli (paspayılı usable)
                double w = Math.Min(s.W, usableU - s.U0);
                double h = Math.Min(s.H, usableV - s.V0);
                if (w < MinRemnantCm || h < MinRemnantCm) continue;

                Point3d bl = origin + u * s.U0 + v * s.V0;
                // Şablon diagonalı ters: üst-sol → alt-sağ
                Point3d diagA = bl + v * h;
                Point3d diagB = bl + u * w;
                Matrix3d xform = BuildSheetTransform(bl, u, v, w, h, tw, th, minX, minY);

                int nEnt = CloneSheetExploded(tr, destDb, ms, path, xform, diagA, diagB);
                if (nEnt <= 0)
                    DrawFallbackSheet(tr, ms, bl, u, v, w, h, out diagA, out diagB);

                Point3d yon = bl + u * (w * 0.5) + v * (h * 0.5);
                yon = new Point3d(yon.X - YonSolaCm, yon.Y + YonYukariCm, yon.Z);
                DrawYonIsareti(tr, ms, yon, u, v, Math.Min(w, h), textStyle);

                string label = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}{1} {2:0}x{3:0}",
                    HasirDonatiSettings.CiftHasir ? "2x" : "",
                    tip,
                    Math.Round(w, MidpointRounding.AwayFromZero),
                    Math.Round(h, MidpointRounding.AwayFromZero));
                PlaceDiagonalLabel(tr, ms, diagA, diagB, label, yaziLyr, textStyle);
                placed++;
            }

            PlaceBindirmeDims(tr, ms, origin, u, v, sheets, bindirme, dimStyle, olcuLyr, textStyle);
            return placed;
        }

        /// <summary>Şablon (x,y) → bl + u*((x-minX)/tw*w) + v*((y-minY)/th*h)</summary>
        private static Matrix3d BuildSheetTransform(
            Point3d bl, Vector3d u, Vector3d v,
            double w, double h, double tw, double th,
            double minX, double minY)
        {
            double sx = w / tw;
            double sy = h / th;
            // Kolonlar: yeniX = u*sx*(x-minX) + v*sy*(y-minY) + bl
            return new Matrix3d(new[]
            {
                u.X * sx, v.X * sy, 0, bl.X - u.X * sx * minX - v.X * sy * minY,
                u.Y * sx, v.Y * sy, 0, bl.Y - u.Y * sx * minX - v.Y * sy * minY,
                0, 0, 1, bl.Z,
                0, 0, 0, 1
            });
        }

        private static int CloneSheetExploded(
            Transaction tr,
            Database destDb,
            BlockTableRecord ms,
            string path,
            Matrix3d xform,
            Point3d diagA,
            Point3d diagB)
        {
            int count = 0;
            Line diagLine = null;
            Polyline diagPl = null;
            double diagLen = 0;

            using (var src = new Database(false, true))
            {
                src.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
                ObjectIdCollection ids;
                Database prev = HostApplicationServices.WorkingDatabase;
                HostApplicationServices.WorkingDatabase = src;
                try
                {
                    using (var trs = src.TransactionManager.StartTransaction())
                    {
                        var btSrc = (BlockTable)trs.GetObject(src.BlockTableId, OpenMode.ForRead);
                        var msSrc = (BlockTableRecord)trs.GetObject(btSrc[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                        ids = new ObjectIdCollection();
                        foreach (ObjectId id in msSrc)
                        {
                            if (!id.IsValid || id.IsErased) continue;
                            if (!(trs.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                            // Yazı ve yön — hedefte yeniden
                            if (ent is DBText || ent is MText) continue;
                            if (LayerStarts(ent.Layer, "HASIR YONU") || LayerStarts(ent.Layer, "HASIR YAZI"))
                                continue;
                            ids.Add(id);
                        }
                        trs.Commit();
                    }
                }
                finally
                {
                    HostApplicationServices.WorkingDatabase = prev;
                }

                if (ids.Count == 0) return 0;

                var mapping = new IdMapping();
                destDb.WblockCloneObjects(ids, ms.ObjectId, mapping, DuplicateRecordCloning.Ignore, false);

                var yonToErase = new List<Entity>();
                foreach (IdPair pair in mapping)
                {
                    if (!pair.IsCloned || !pair.Value.IsValid) continue;
                    if (!(tr.GetObject(pair.Value, OpenMode.ForWrite, false) is Entity ent)) continue;
                    if (ent.IsErased) continue;

                    try { ent.TransformBy(xform); }
                    catch { continue; }

                    // Yön artığı
                    if (LayerStarts(ent.Layer, "HASIR YONU"))
                    {
                        yonToErase.Add(ent);
                        continue;
                    }

                    AssignLayers(ent);
                    if (ent is Dimension importedDimension)
                        BaDimensionStyles.Apply(tr, destDb, importedDimension, BaDimensionStyles.Ensure(tr, destDb, DimStylePlanOlcu));
                    count++;

                    if (ent is Line ln && IsDiagonalLine(ln, out double llen) && llen > diagLen)
                    {
                        diagLine = ln;
                        diagPl = null;
                        diagLen = llen;
                    }
                    else if (ent is Polyline pl && pl.NumberOfVertices == 2 &&
                             IsDiagonalPoly(pl, out double plen) && plen > diagLen)
                    {
                        diagPl = pl;
                        diagLine = null;
                        diagLen = plen;
                    }
                }

                if (diagLine != null)
                {
                    diagLine.StartPoint = diagA;
                    diagLine.EndPoint = diagB;
                    diagLine.Layer = LayerHasirCizgi;
                }
                else if (diagPl != null)
                {
                    diagPl.SetPointAt(0, new Point2d(diagA.X, diagA.Y));
                    diagPl.SetPointAt(1, new Point2d(diagB.X, diagB.Y));
                    diagPl.Layer = LayerHasirCizgi;
                }

                foreach (Entity e in yonToErase)
                {
                    try { e.Erase(); } catch { }
                }
            }

            return count;
        }

        private static bool IsDiagonalLine(Line ln, out double len)
        {
            len = 0;
            if (ln == null) return false;
            Vector3d d = ln.EndPoint - ln.StartPoint;
            len = d.Length;
            if (len < 1) return false;
            bool onCizgi = LayerStarts(ln.Layer, "HASIR CIZGI");
            bool slanted = Math.Abs(d.X) > 1e-3 && Math.Abs(d.Y) > 1e-3;
            return onCizgi || slanted;
        }

        private static bool IsDiagonalPoly(Polyline pl, out double len)
        {
            len = 0;
            if (pl == null || pl.NumberOfVertices < 2) return false;
            Vector3d d = pl.GetPoint3dAt(1) - pl.GetPoint3dAt(0);
            len = d.Length;
            if (len < 1) return false;
            bool onCizgi = LayerStarts(pl.Layer, "HASIR CIZGI");
            bool slanted = Math.Abs(d.X) > 1e-3 && Math.Abs(d.Y) > 1e-3;
            return onCizgi || slanted;
        }

        private static void AssignLayers(Entity ent)
        {
            try
            {
                string lyr = ent.Layer ?? "";
                if (LayerStarts(lyr, "HASIR CIZGI"))
                    ent.Layer = LayerHasirCizgi;
                else if (LayerStarts(lyr, "HASIR"))
                    ent.Layer = LayerHasir;
                else
                    ent.Layer = LayerHasir;
            }
            catch { }
        }

        private static void DrawFallbackSheet(
            Transaction tr, BlockTableRecord ms,
            Point3d bl, Vector3d u, Vector3d v, double w, double h,
            out Point3d diagA, out Point3d diagB)
        {
            Point3d p00 = bl;
            Point3d p10 = bl + u * w;
            Point3d p11 = bl + u * w + v * h;
            Point3d p01 = bl + v * h;
            diagA = p01;
            diagB = p10;

            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(p00.X, p00.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(p10.X, p10.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(p11.X, p11.Y), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(p01.X, p01.Y), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = LayerHasir;
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);

            var diag = new Line(diagA, diagB) { Layer = LayerHasirCizgi };
            ms.AppendEntity(diag);
            tr.AddNewlyCreatedDBObject(diag, true);
        }

        private static void DrawYonIsareti(
            Transaction tr, BlockTableRecord ms,
            Point3d center, Vector3d u, Vector3d v, double sheetMin, ObjectId textStyle)
        {
            double arm = Math.Max(8.0, Math.Min(25.0, sheetMin * 0.08));
            double head = arm * 0.35;
            var mark = new List<Entity>();

            void Add(Entity ent)
            {
                ent.Layer = LayerHasirYon;
                ms.AppendEntity(ent);
                tr.AddNewlyCreatedDBObject(ent, true);
                mark.Add(ent);
            }

            void Arrow(Vector3d dir)
            {
                Vector3d n = dir.GetNormal();
                Point3d tip = center + n * arm;
                Point3d tail = center - n * (arm * 0.15);
                Add(new Line(tail, tip));

                Vector3d perp = new Vector3d(-n.Y, n.X, 0);
                Point3d h1 = tip - n * head + perp * (head * 0.45);
                Point3d h2 = tip - n * head - perp * (head * 0.45);
                Add(new Line(tip, h1));
                Add(new Line(tip, h2));
            }

            Arrow(u);
            Arrow(v);

            double c = arm * 0.2;
            Add(new Line(center - u * c, center + u * c));
            Add(new Line(center - v * c, center + v * c));
            PlaceYonYazisi(tr, ms, mark, textStyle);
        }

        /// <summary>Yön işaretinin üstü: yazı kutusu alt kenarı, işaretin en üst noktasının 3 cm yukarısı.</summary>
        private static void PlaceYonYazisi(Transaction tr, BlockTableRecord ms, List<Entity> mark, ObjectId textStyle)
        {
            double minX = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            bool any = false;
            foreach (Entity ent in mark)
            {
                if (ent == null || ent.IsErased) continue;
                try
                {
                    Extents3d e = ent.GeometricExtents;
                    if (e.MinPoint.X < minX) minX = e.MinPoint.X;
                    if (e.MaxPoint.X > maxX) maxX = e.MaxPoint.X;
                    if (e.MaxPoint.Y > maxY) maxY = e.MaxPoint.Y;
                    any = true;
                }
                catch { }
            }
            if (!any) return;

            double cx = (minX + maxX) * 0.5;
            var tx = new DBText();
            tx.SetDatabaseDefaults(ms.Database);
            tx.TextString = "Hasır Yonu";
            tx.Height = YonYaziCm;
            tx.Layer = LayerHasirYon;
            tx.HorizontalMode = TextHorizontalMode.TextCenter;
            tx.VerticalMode = TextVerticalMode.TextBottom;
            tx.AlignmentPoint = new Point3d(cx, maxY + YonYaziGapCm, 0);
            tx.Position = tx.AlignmentPoint;
            if (!textStyle.IsNull) tx.TextStyleId = textStyle;
            ms.AppendEntity(tx);
            tr.AddNewlyCreatedDBObject(tx, true);
            try { tx.AdjustAlignment(ms.Database); } catch { }
            try
            {
                Extents3d te = tx.GeometricExtents;
                double dx = cx - (te.MinPoint.X + te.MaxPoint.X) * 0.5;
                double dy = (maxY + YonYaziGapCm) - te.MinPoint.Y;
                if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                    tx.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
            }
            catch { }
        }

        private static void PlaceDiagonalLabel(
            Transaction tr,
            BlockTableRecord ms,
            Point3d a,
            Point3d b,
            string text,
            string layer,
            ObjectId textStyle)
        {
            if (a.DistanceTo(b) < 1e-3) return;
            Vector3d dir = b - a;
            double ang = Math.Atan2(dir.Y, dir.X);
            // Okunabilirlik: yazı baş aşağı olmasın
            if (ang > Math.PI / 2 || ang < -Math.PI / 2)
            {
                ang += Math.PI;
                Point3d tmp = a; a = b; b = tmp;
                dir = b - a;
            }

            Point3d mid = new Point3d(
                (a.X + b.X) * 0.5,
                (a.Y + b.Y) * 0.5,
                (a.Z + b.Z) * 0.5);
            // Okuma yönünün solu = yazının üstü
            Vector3d up = new Vector3d(-Math.Sin(ang), Math.Cos(ang), 0);

            var tx = new DBText();
            tx.SetDatabaseDefaults();
            tx.TextString = text;
            tx.Height = LabelHeightCm;
            tx.Rotation = ang;
            tx.HorizontalMode = TextHorizontalMode.TextCenter;
            tx.VerticalMode = TextVerticalMode.TextBottom;
            tx.AlignmentPoint = mid + up * LabelGapCm;
            tx.Position = tx.AlignmentPoint;
            tx.Layer = layer;
            if (!textStyle.IsNull)
                tx.TextStyleId = textStyle;
            ms.AppendEntity(tx);
            tr.AddNewlyCreatedDBObject(tx, true);
            try { tx.AdjustAlignment(tx.Database); } catch { }
        }

        private static void PlaceBindirmeDims(
            Transaction tr,
            BlockTableRecord ms,
            Point3d origin,
            Vector3d u,
            Vector3d v,
            List<SheetCell> sheets,
            double bindirme,
            ObjectId dimStyle,
            string olcuLyr,
            ObjectId textStyle)
        {
            double off = Math.Max(8.0, Math.Min(bindirme, 30.0) * 0.4);

            var byRow = new Dictionary<int, List<SheetCell>>();
            foreach (SheetCell s in sheets)
            {
                if (!byRow.TryGetValue(s.Row, out var list))
                {
                    list = new List<SheetCell>();
                    byRow[s.Row] = list;
                }
                list.Add(s);
            }
            foreach (var kv in byRow)
            {
                List<SheetCell> row = kv.Value;
                row.Sort((a, b) => a.Col.CompareTo(b.Col));
                for (int i = 0; i < row.Count - 1; i++)
                {
                    SheetCell a = row[i];
                    SheetCell b = row[i + 1];
                    double uLap0 = b.U0;
                    double uLap1 = a.U0 + a.W;
                    double overlap = uLap1 - uLap0;
                    if (overlap < MinRemnantCm) continue;
                    double vMid = (Math.Max(a.V0, b.V0) + Math.Min(a.V0 + a.H, b.V0 + b.H)) * 0.5;
                    Point3d x1 = origin + u * uLap0 + v * vMid;
                    Point3d x2 = origin + u * uLap1 + v * vMid;
                    Point3d dimPt = origin + u * ((uLap0 + uLap1) * 0.5) + v * (vMid + off);
                    OtoOlcuDimFactory.PlaceRotated(tr, ms, x1, x2, dimPt, dimStyle, olcuLyr,
                        null);
                }
            }

            var byCol = new Dictionary<int, List<SheetCell>>();
            foreach (SheetCell s in sheets)
            {
                if (!byCol.TryGetValue(s.Col, out var list))
                {
                    list = new List<SheetCell>();
                    byCol[s.Col] = list;
                }
                list.Add(s);
            }
            foreach (var kv in byCol)
            {
                List<SheetCell> col = kv.Value;
                col.Sort((a, b) => a.Row.CompareTo(b.Row));
                for (int i = 0; i < col.Count - 1; i++)
                {
                    SheetCell a = col[i];
                    SheetCell b = col[i + 1];
                    double vLap0 = b.V0;
                    double vLap1 = a.V0 + a.H;
                    double overlap = vLap1 - vLap0;
                    if (overlap < MinRemnantCm) continue;
                    double uMid = (Math.Max(a.U0, b.U0) + Math.Min(a.U0 + a.W, b.U0 + b.W)) * 0.5;
                    Point3d x1 = origin + u * uMid + v * vLap0;
                    Point3d x2 = origin + u * uMid + v * vLap1;
                    Point3d dimPt = origin + u * (uMid + off) + v * ((vLap0 + vLap1) * 0.5);
                    OtoOlcuDimFactory.PlaceRotated(tr, ms, x1, x2, dimPt, dimStyle, olcuLyr,
                        null);
                }
            }
        }
        

        private static ObjectId EnsurePlanOlcuDimStyle(Transaction tr, Database db, ObjectId textStyle)
        {
            return BaDimensionStyles.Ensure(tr, db, DimStylePlanOlcu);
        }

        private static bool TryGetTemplateExtents(
            string path,
            out double width, out double height,
            out double minX, out double minY)
        {
            width = NominalBoyCm;
            height = NominalEnCm;
            minX = 0;
            minY = 0;
            try
            {
                using (var src = new Database(false, true))
                {
                    src.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
                    Database prev = HostApplicationServices.WorkingDatabase;
                    HostApplicationServices.WorkingDatabase = src;
                    try
                    {
                        using (var trs = src.TransactionManager.StartTransaction())
                        {
                            var btSrc = (BlockTable)trs.GetObject(src.BlockTableId, OpenMode.ForRead);
                            var msSrc = (BlockTableRecord)trs.GetObject(btSrc[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                            // Önce kapalı polyline (hasır çerçevesi) ara
                            bool foundRect = false;
                            double rMinX = 0, rMinY = 0, rMaxX = 0, rMaxY = 0;
                            bool any = false;
                            double aMinX = 0, aMinY = 0, aMaxX = 0, aMaxY = 0;

                            foreach (ObjectId id in msSrc)
                            {
                                if (!id.IsValid || id.IsErased) continue;
                                if (!(trs.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                                if (ent is DBText || ent is MText) continue;
                                if (LayerStarts(ent.Layer, "HASIR YONU") || LayerStarts(ent.Layer, "HASIR YAZI"))
                                    continue;

                                try
                                {
                                    Extents3d e = ent.GeometricExtents;
                                    if (!any)
                                    {
                                        aMinX = e.MinPoint.X; aMinY = e.MinPoint.Y;
                                        aMaxX = e.MaxPoint.X; aMaxY = e.MaxPoint.Y;
                                        any = true;
                                    }
                                    else
                                    {
                                        if (e.MinPoint.X < aMinX) aMinX = e.MinPoint.X;
                                        if (e.MinPoint.Y < aMinY) aMinY = e.MinPoint.Y;
                                        if (e.MaxPoint.X > aMaxX) aMaxX = e.MaxPoint.X;
                                        if (e.MaxPoint.Y > aMaxY) aMaxY = e.MaxPoint.Y;
                                    }
                                }
                                catch { }

                                if (ent is Polyline pl && pl.Closed && pl.NumberOfVertices >= 4)
                                {
                                    try
                                    {
                                        Extents3d e = pl.GeometricExtents;
                                        double rw = e.MaxPoint.X - e.MinPoint.X;
                                        double rh = e.MaxPoint.Y - e.MinPoint.Y;
                                        if (rw > 50 && rh > 50)
                                        {
                                            // En büyük kapalı çerçeveyi al
                                            if (!foundRect || rw * rh > (rMaxX - rMinX) * (rMaxY - rMinY))
                                            {
                                                rMinX = e.MinPoint.X; rMinY = e.MinPoint.Y;
                                                rMaxX = e.MaxPoint.X; rMaxY = e.MaxPoint.Y;
                                                foundRect = true;
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                            trs.Commit();

                            if (foundRect)
                            {
                                minX = rMinX;
                                minY = rMinY;
                                width = Math.Max(1.0, rMaxX - rMinX);
                                height = Math.Max(1.0, rMaxY - rMinY);
                                return true;
                            }
                            if (any)
                            {
                                minX = aMinX;
                                minY = aMinY;
                                width = Math.Max(1.0, aMaxX - aMinX);
                                height = Math.Max(1.0, aMaxY - aMinY);
                                return true;
                            }
                        }
                    }
                    finally
                    {
                        HostApplicationServices.WorkingDatabase = prev;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool LayerStarts(string layer, string prefix)
        {
            if (string.IsNullOrEmpty(layer) || string.IsNullOrEmpty(prefix)) return false;
            return layer.Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static ObjectId GetOrCreateTextStyle(Transaction tr, Database db)
        {
            try
            {
                var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                if (tst.Has(TextStyleYazi))
                    return tst[TextStyleYazi];
                tst.UpgradeOpen();
                var rec = new TextStyleTableRecord
                {
                    Name = TextStyleYazi,
                    FileName = "txt.shx",
                    TextSize = 0
                };
                ObjectId id = tst.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
                return id;
            }
            catch
            {
                return ObjectId.Null;
            }
        }

        private static string EnsureLayer(Transaction tr, Database db, string name, short aci, LineWeight lw)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name))
                return name;
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

        private static string EnsureTempDwg()
        {
            lock (TempLock)
            {
                Assembly asm = typeof(HasirDonatiRunner).Assembly;
                using (Stream stream = asm.GetManifestResourceStream(EmbeddedResourceName))
                {
                    if (stream == null) return null;
                    string path = Path.Combine(Path.GetTempPath(),
                        "ST4Yardimci_hasir_" + asm.ManifestModule.ModuleVersionId.ToString("N") + ".dwg");
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
