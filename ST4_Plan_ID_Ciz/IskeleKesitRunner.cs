using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// Kesit cizgisi + ISKELE YATAY kesisimleri + ST4 kat kotlari; ISKELE_KESIT.dxf sablonunu modele klonlar.
    /// Kesit geometrisinin ST4 kotlariyla nasil eslestirilecegi kullanici tarafindan netlestirilene kadar
    /// ozet mesaj ve sablon yerlesimi yapilir.
    /// </summary>
    internal static class IskeleKesitRunner
    {
        public const string LayerYatay = "ISKELE YATAY (BEYKENT)";
        private const string LayerKotYazi = "KOT YAZI (BEYKENT)";
        private const string LayerKotCizgi = "KOT CIZGI (BEYKENT)";
        private const string LayerIskeleAlt = "ISKELE_ALT";
        private const string LayerIskeleUst = "ISKELE_UST";

        /// <summary>Modül yüksekliği (cm).</summary>
        private const double IskeleLiftCm = 250.0;

        /// <summary>Kesit boyutu olcumleri: iki uzatma noktasi ayni Y (yatay aks araligi).</summary>
        private const double DimSameYTolCm = 3.0;

        public static void Execute(Document doc)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            if (!IskeleCizContextStore.IsActive)
            {
                ed.WriteMessage("\nISKELEKESIT: Once bu oturumda ISKELECIZ komutunu basariyla tamamlayin.");
                return;
            }

            var peo = new PromptEntityOptions("\nKesit cizgisini secin (LINE veya LWPOLYLINE): ");
            peo.SetRejectMessage("\nSadece LINE veya LWPOLYLINE.");
            peo.AddAllowedClass(typeof(Line), false);
            peo.AddAllowedClass(typeof(Polyline), false);
            var re = ed.GetEntity(peo);
            if (re.Status != PromptStatus.OK) return;

            var pfo = new PromptOpenFileOptions("\nBinanin ST4 dosyasini secin (kat kotlari): ")
            {
                Filter = "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*"
            };
            var prf = ed.GetFileNameForOpen(pfo);
            if (prf.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(prf.StringResult)) return;

            St4Model model;
            try
            {
                model = new St4Parser().Parse(prf.StringResult);
            }
            catch (Exception ex)
            {
                ed.WriteMessage("\nST4 okunamadi: {0}", ex.Message);
                return;
            }

            var floors = model.Floors.OrderBy(f => f.ElevationM).ToList();
            ed.WriteMessage("\nST4 bina taban kotu: {0} m", model.BuildingBaseKotu.ToString("0.###", CultureInfo.InvariantCulture));
            ed.WriteMessage("\nST4: {0} kat (story) satiri.", floors.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var f in floors)
            {
                double absM = model.BuildingBaseKotu + f.ElevationM;
                ed.WriteMessage("\n  Kat {0} ({1}): goreli {2} m | mutlak {3} m",
                    f.FloorNo.ToString(CultureInfo.InvariantCulture),
                    f.ShortName,
                    f.ElevationM.ToString("0.###", CultureInfo.InvariantCulture),
                    absM.ToString("0.###", CultureInfo.InvariantCulture));
            }

            List<(Point3d a, Point3d b)> cutSegs;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = (Entity)tr.GetObject(re.ObjectId, OpenMode.ForRead);
                cutSegs = GetCutSegments(ent);
                tr.Commit();
            }

            if (cutSegs == null || cutSegs.Count == 0)
            {
                ed.WriteMessage("\nKesit cizgisi segment uretilemedi.");
                return;
            }

            var yataySegs = new List<IskeleKesitSectionGeometry.Seg2>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    var e = tr.GetObject(id, OpenMode.ForRead) as Line;
                    if (e == null) continue;
                    if (!string.Equals(e.Layer, LayerYatay, StringComparison.Ordinal)) continue;
                    if (e.StartPoint.DistanceTo(e.EndPoint) < 1.0) continue;
                    yataySegs.Add(new IskeleKesitSectionGeometry.Seg2(e.StartPoint, e.EndPoint));
                }
                tr.Commit();
            }

            var stations = IskeleKesitSectionGeometry.ComputeStationsFromYatayMidlines(yataySegs, cutSegs);
            var alongSorted = stations
                .Select(s => (s, d: DistAlongPolylinePath(cutSegs, s)))
                .OrderBy(x => x.d)
                .ToList();
            var bayWidthsCm = new List<double>();
            for (int i = 0; i < alongSorted.Count - 1; i++)
                bayWidthsCm.Add(alongSorted[i + 1].d - alongSorted[i].d);

            ed.WriteMessage("\nKesit: {0} dikme aks (yatay eleman 5cm ciftinin ORTA HAT kesisimi).",
                alongSorted.Count.ToString(CultureInfo.InvariantCulture));
            if (bayWidthsCm.Count > 0)
            {
                ed.WriteMessage("\nKesit aks araliklari (cm, kesit dogrultusunda): {0}",
                    string.Join(" + ", bayWidthsCm.Select(b => Math.Round(b).ToString(CultureInfo.InvariantCulture))));
            }

            string templatePath = GetIskeleKesitTemplatePath();
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
            {
                ed.WriteMessage("\nISKELE_KESIT.dxf bulunamadi (DLL klasorunde olmali). Sablon klonlanmadi.");
                return;
            }

            var ppo = new PromptPointOptions("\nIskele kesit sablonunun sol-alt kosesi icin yerlesim noktasi: ");
            var prp = ed.GetPoint(ppo);
            if (prp.Status != PromptStatus.OK) return;
            var ins = new Point3d(prp.Value.X, prp.Value.Y, 0);

            if (!TryImportIskeleKesitTemplate(db, templatePath, ins, out List<ObjectId> importedIds, out string err))
            {
                ed.WriteMessage("\nSablon klonlanamadi: {0}", err ?? "?");
                return;
            }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                ApplyKotTextsFromSt4(tr, importedIds, model, ed);
                ApplyHorizontalBayDimensions(tr, importedIds, bayWidthsCm, ed);
                EraseImportedTemplateScaffold(tr, importedIds);
                EnsureKesitDrawLayers(tr, db);
                DrawProceduralIskeleKesit(tr, db, ins, bayWidthsCm, model, ed);
                tr.Commit();
            }
            ed.WriteMessage("\nISKELE_KESIT: sablon + ST4 kot yazilari + olculer; iskele govdesi plan orta hat akslarina ve {0} cm modul ile yeniden cizildi.",
                ((long)Math.Round(IskeleLiftCm)).ToString(CultureInfo.InvariantCulture));
        }

        private static string GetIskeleKesitTemplatePath()
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(dir)) return null;
                // DWG: Insert/Wblock daha az eNotApplicable verir; DXF karmaşık sembol tablolarında sorun cikar.
                string dwg = Path.Combine(dir, "ISKELE_KESIT.dwg");
                if (File.Exists(dwg)) return dwg;
                string dxf = Path.Combine(dir, "ISKELE_KESIT.dxf");
                if (File.Exists(dxf)) return dxf;
                return null;
            }
            catch { return null; }
        }

        private static void LoadTemplateDatabase(Database db, string filePath)
        {
            if (filePath.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                db.ReadDwgFile(filePath, FileOpenMode.OpenForReadAndAllShare, true, null);
            else
            {
                string log = Path.Combine(Path.GetTempPath(), "ST4PlanIdCiz_iskelekesit_dxf.log");
                db.DxfIn(filePath, log);
            }
        }

        /// <summary>
        /// Once <see cref="Database.Insert"/> + BlockReference patlatma (dinamik blok / DXF icin WblockClone'dan guvenli).
        /// Olmazsa WblockClone + Replace, sonra Ignore + WorkingDatabase hedef.
        /// </summary>
        private static bool TryImportIskeleKesitTemplate(Database targetDb, string filePath, Point3d insertBottomLeft, out List<ObjectId> importedIds, out string error)
        {
            importedIds = new List<ObjectId>();
            if (TryInsertExplodeTemplate(targetDb, filePath, insertBottomLeft, out var ids1, out error))
            {
                importedIds = ids1;
                return true;
            }

            string err2 = error;
            if (TryWblockCloneTemplate(targetDb, filePath, insertBottomLeft, DuplicateRecordCloning.Replace, out var ids2, out error))
            {
                importedIds = ids2;
                return true;
            }

            error = string.Join(" | ", new[] { err2, error }.Where(s => !string.IsNullOrEmpty(s)).Distinct());
            return false;
        }

        /// <summary>
        /// Tum sablonu tek blok olarak <see cref="Database.Insert(string, Database, bool)"/> ile alir, patlatir (WblockClone eNotApplicable icin).
        /// </summary>
        private static bool TryInsertExplodeTemplate(Database targetDb, string filePath, Point3d insertBottomLeft, out List<ObjectId> importedIds, out string error)
        {
            importedIds = new List<ObjectId>();
            error = null;
            Database sourceDb = null;
            try
            {
                sourceDb = new Database(false, true);
                LoadTemplateDatabase(sourceDb, filePath);
                Point3d srcEmin = sourceDb.Extmin;

                string blkName = "ST4_ISKELE_KESIT_" + Guid.NewGuid().ToString("N").Substring(0, 12);
                ObjectId blkDefId = targetDb.Insert(blkName, sourceDb, false);

                using (var tr = targetDb.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    var pos = new Point3d(insertBottomLeft.X - srcEmin.X, insertBottomLeft.Y - srcEmin.Y, 0);
                    var br = new BlockReference(pos, blkDefId);
                    btr.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);

                    var exploded = new DBObjectCollection();
                    try
                    {
                        br.Explode(exploded);
                    }
                    catch (Exception ex)
                    {
                        try { tr.Abort(); } catch { /* yok */ }
                        error = ex.Message;
                        return false;
                    }

                    if (exploded.Count == 0)
                    {
                        try { tr.Abort(); } catch { }
                        error = "Insert sonrasi patlatma bos.";
                        return false;
                    }

                    br.Erase();
                    foreach (DBObject o in exploded)
                    {
                        if (o is Entity ent)
                        {
                            btr.AppendEntity(ent);
                            tr.AddNewlyCreatedDBObject(ent, true);
                            importedIds.Add(ent.ObjectId);
                        }
                    }

                    tr.Commit();
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                sourceDb?.Dispose();
            }
        }

        private static bool TryWblockCloneTemplate(Database targetDb, string filePath, Point3d insertBottomLeft, DuplicateRecordCloning cloning, out List<ObjectId> importedIds, out string error)
        {
            importedIds = new List<ObjectId>();
            error = null;
            Database sourceDb = null;
            try
            {
                sourceDb = new Database(false, true);
                LoadTemplateDatabase(sourceDb, filePath);

                ObjectIdCollection ids = new ObjectIdCollection();
                using (Transaction trSrc = sourceDb.TransactionManager.StartTransaction())
                {
                    var ms = trSrc.GetObject(sourceDb.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                    if (ms == null) { error = "Kaynak model uzayi yok."; return false; }
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsValid && !id.IsErased) ids.Add(id);
                    }
                    trSrc.Commit();
                }
                if (ids.Count == 0) { error = "Sablon model uzayinda nesne yok."; return false; }

                using (var tr = targetDb.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    var mapping = new IdMapping();
                    targetDb.WblockCloneObjects(ids, btr.ObjectId, mapping, cloning, false);

                    double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                    foreach (IdPair pair in mapping)
                    {
                        if (!pair.Value.IsValid) continue;
                        var ent = tr.GetObject(pair.Value, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        try
                        {
                            var ext = ent.GeometricExtents;
                            minX = Math.Min(minX, ext.MinPoint.X);
                            minY = Math.Min(minY, ext.MinPoint.Y);
                            maxX = Math.Max(maxX, ext.MaxPoint.X);
                            maxY = Math.Max(maxY, ext.MaxPoint.Y);
                        }
                        catch { }
                    }
                    if (minX > maxX || minY > maxY)
                    {
                        error = "Klonlanan geometri sinirlari okunamadi.";
                        tr.Abort();
                        return false;
                    }

                    var disp = Matrix3d.Displacement(new Vector3d(insertBottomLeft.X - minX, insertBottomLeft.Y - minY, 0));
                    foreach (IdPair pair in mapping)
                    {
                        if (!pair.Value.IsValid) continue;
                        var ent = tr.GetObject(pair.Value, OpenMode.ForWrite) as Entity;
                        if (ent != null)
                        {
                            ent.TransformBy(disp);
                            importedIds.Add(pair.Value);
                        }
                    }

                    tr.Commit();
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                sourceDb?.Dispose();
            }
        }

        private static List<(Point3d a, Point3d b)> GetCutSegments(Entity ent)
        {
            var list = new List<(Point3d, Point3d)>();
            switch (ent)
            {
                case Line ln:
                    list.Add((ln.StartPoint, ln.EndPoint));
                    break;
                case Polyline pl when pl.NumberOfVertices >= 2:
                {
                    int n = pl.NumberOfVertices;
                    int segCount = pl.Closed ? n : n - 1;
                    for (int i = 0; i < segCount; i++)
                    {
                        int j = pl.Closed ? (i + 1) % n : i + 1;
                        list.Add((pl.GetPoint3dAt(i), pl.GetPoint3dAt(j)));
                    }
                    break;
                }
            }
            return list;
        }

        private static bool TryIntersectSeg2d(Point3d a1, Point3d a2, Point3d b1, Point3d b2, out Point3d hit)
        {
            hit = Point3d.Origin;
            double x1 = a1.X, y1 = a1.Y, x2 = a2.X, y2 = a2.Y;
            double x3 = b1.X, y3 = b1.Y, x4 = b2.X, y4 = b2.Y;
            double d = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(d) < 1e-12) return false;
            double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / d;
            double u = -((x1 - x2) * (y1 - y3) - (y1 - y2) * (x1 - x3)) / d;
            if (t < -1e-9 || t > 1 + 1e-9 || u < -1e-9 || u > 1 + 1e-9) return false;
            hit = new Point3d(x1 + t * (x2 - x1), y1 + t * (y2 - y1), 0);
            return true;
        }

        /// <summary>Kesit yolunun baslangicina gore en yakin projeksiyon mesafesi (cm).</summary>
        private static double DistAlongPolylinePath(List<(Point3d a, Point3d b)> segs, Point3d p)
        {
            double accum = 0;
            double bestAlong = 0;
            double bestPerp = double.MaxValue;
            foreach (var (a, b) in segs)
            {
                var ab = b - a;
                double len2 = ab.X * ab.X + ab.Y * ab.Y;
                if (len2 < 1e-18)
                    continue;
                var ap = p - a;
                double t = (ap.X * ab.X + ap.Y * ab.Y) / len2;
                t = Math.Max(0, Math.Min(1, t));
                var proj = new Point3d(a.X + t * ab.X, a.Y + t * ab.Y, 0);
                double perp = p.DistanceTo(proj);
                if (perp < bestPerp)
                {
                    bestPerp = perp;
                    bestAlong = accum + a.DistanceTo(proj);
                }
                accum += Math.Sqrt(len2);
            }
            return bestAlong;
        }

        private static void EraseImportedTemplateScaffold(Transaction tr, List<ObjectId> importedIds)
        {
            if (importedIds == null) return;
            foreach (ObjectId id in importedIds)
            {
                if (!id.IsValid || id.IsErased) continue;
                var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                if (ent == null) continue;
                string ly = ent.Layer ?? "";
                // Sablon korkuluk (YATAY-KORKULUK) korunur; yalnizca ana iskele govdesi silinir.
                if (ly == LayerIskeleAlt || ly == LayerIskeleUst)
                {
                    try { ent.Erase(); } catch { /* bagli nesne */ }
                }
            }
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

        private static ObjectId EnsureLayerKesit(Transaction tr, Database db, string name, short color, string linetype, LineWeight lw)
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
            ObjectId lid = lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            lt.DowngradeOpen();
            return lid;
        }

        private static void EnsureKesitDrawLayers(Transaction tr, Database db)
        {
            EnsureLayerKesit(tr, db, LayerIskeleAlt, 4, "Continuous", LineWeight.LineWeight030);
            EnsureLayerKesit(tr, db, LayerIskeleUst, 210, "Continuous", LineWeight.LineWeight030);
            TryLoadLinetype(tr, db, "HIDDEN2");
            EnsureLayerKesit(tr, db, LayerKotCizgi, 8, "HIDDEN2", LineWeight.LineWeight015);
        }

        private static void AppendLine(Transaction tr, BlockTableRecord btr, Point3d a, Point3d b, string layer)
        {
            var ln = new Line(a, b) { Layer = layer };
            btr.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
        }

        /// <summary>
        /// Kesit düzlemi: X = aks mesafesi (cm), Y = kot (cm). ins sol-alt = en dusuk ST4 mutlak kotu.
        /// Dikmeler + 250 cm modul yataylar; kat kotlari KOT CIZGI.
        /// </summary>
        private static void DrawProceduralIskeleKesit(Transaction tr, Database db, Point3d ins, List<double> bayWidthsCm, St4Model model, Editor ed)
        {
            var absElevs = GetSortedAbsoluteElevationsM(model);
            if (absElevs.Count == 0)
            {
                ed.WriteMessage("\nUyari: ST4 kot listesi bos; iskele govdesi cizilmedi.");
                return;
            }

            double elevMinM = absElevs[0];
            double elevTopM = absElevs[absElevs.Count - 1];

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var colX = new List<double> { ins.X };
            double xAcc = ins.X;
            foreach (double w in bayWidthsCm)
            {
                xAcc += w;
                colX.Add(xAcc);
            }

            if (colX.Count < 2)
            {
                ed.WriteMessage("\nUyari: En az iki dikme aks gerekir; iskele govdesi cizilmedi.");
                return;
            }

            double y0 = ins.Y;
            double yTop = ins.Y + (elevTopM - elevMinM) * 100.0;
            double xLeft = colX[0];
            double xRight = colX[colX.Count - 1];

            foreach (double xc in colX)
                AppendLine(tr, btr, new Point3d(xc, y0, 0), new Point3d(xc, yTop, 0), LayerIskeleAlt);

            for (double y = y0 + IskeleLiftCm; y <= yTop + 1e-6; y += IskeleLiftCm)
            {
                for (int i = 0; i < colX.Count - 1; i++)
                    AppendLine(tr, btr, new Point3d(colX[i], y, 0), new Point3d(colX[i + 1], y, 0), LayerIskeleUst);
            }

            foreach (double em in absElevs)
            {
                double yy = ins.Y + (em - elevMinM) * 100.0;
                AppendLine(tr, btr, new Point3d(xLeft, yy, 0), new Point3d(xRight, yy, 0), LayerKotCizgi);
            }
        }

        private static List<double> GetSortedAbsoluteElevationsM(St4Model model)
        {
            var set = new SortedSet<double>();
            set.Add(model.BuildingBaseKotu);
            foreach (var f in model.Floors)
                set.Add(model.BuildingBaseKotu + f.ElevationM);
            return set.ToList();
        }

        private static string FormatElevationLabelM(double absM)
        {
            double m = absM;
            string s = string.Format(CultureInfo.InvariantCulture, "{0:+0.00;-0.00;0.00}", m);
            if (Math.Abs(m) < 1e-9)
                s = "±" + s.TrimStart('+');
            return KolonDonatiTableDrawer.NormalizeDiameterSymbol(s);
        }

        private static bool IsKotYaziPlaceholder(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            if (t.Equals("%%P0.00", StringComparison.OrdinalIgnoreCase)) return true;
            if (t.Length >= 4 && t[0] == '+' && char.IsDigit(t[1]) && t.Contains(".")) return true;
            if (t.Length >= 4 && t[0] == '-' && char.IsDigit(t[1]) && t.Contains(".")) return true;
            return false;
        }

        private static void ApplyKotTextsFromSt4(Transaction tr, List<ObjectId> importedIds, St4Model model, Editor ed)
        {
            if (importedIds == null || importedIds.Count == 0) return;
            var elevM = GetSortedAbsoluteElevationsM(model);
            if (elevM.Count == 0) return;

            var texts = new List<DBText>();
            foreach (ObjectId id in importedIds)
            {
                if (!id.IsValid || id.IsErased) continue;
                var dt = tr.GetObject(id, OpenMode.ForWrite) as DBText;
                if (dt == null) continue;
                if (!string.Equals(dt.Layer, LayerKotYazi, StringComparison.Ordinal)) continue;
                if (!IsKotYaziPlaceholder(dt.TextString)) continue;
                texts.Add(dt);
            }

            if (texts.Count == 0) return;

            double bucket(double y) => Math.Round(y * 20.0) / 20.0;
            var rows = texts.GroupBy(t => bucket(t.Position.Y)).OrderBy(g => g.Key).ToList();
            int ri = 0;
            foreach (var row in rows)
            {
                if (ri >= elevM.Count) break;
                string label = FormatElevationLabelM(elevM[ri]);
                ri++;
                foreach (var txt in row.OrderBy(t => t.Position.X))
                    txt.TextString = label;
            }

            if (ri < elevM.Count)
                ed.WriteMessage("\nUyari: Sablonda {0} kot yazisi satiri guncellendi; ST4'te {1} mutlak kot var (fazlalari icin sablon satiri yok).",
                    ri.ToString(CultureInfo.InvariantCulture), elevM.Count.ToString(CultureInfo.InvariantCulture));
        }

        private static bool TryGetDimXLinePoints(Dimension d, out Point3d x1, out Point3d x2)
        {
            x1 = x2 = Point3d.Origin;
            switch (d)
            {
                case AlignedDimension ad:
                    x1 = ad.XLine1Point;
                    x2 = ad.XLine2Point;
                    return true;
                case RotatedDimension rd:
                    x1 = rd.XLine1Point;
                    x2 = rd.XLine2Point;
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsHorizontalBayDimension(Dimension d)
        {
            if (d == null) return false;
            try
            {
                if (d.Measurement < 25 || d.Measurement > 520) return false;
                if (!TryGetDimXLinePoints(d, out Point3d p1, out Point3d p2)) return false;
                return Math.Abs(p1.Y - p2.Y) <= DimSameYTolCm;
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyHorizontalBayDimensions(Transaction tr, List<ObjectId> importedIds, List<double> bayWidthsCm, Editor ed)
        {
            if (importedIds == null || importedIds.Count == 0 || bayWidthsCm == null || bayWidthsCm.Count == 0) return;

            var dims = new List<Dimension>();
            foreach (ObjectId id in importedIds)
            {
                if (!id.IsValid || id.IsErased) continue;
                var d = tr.GetObject(id, OpenMode.ForWrite) as Dimension;
                if (d == null) continue;
                if (d.Layer == null || d.Layer.IndexOf("OLCU", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!IsHorizontalBayDimension(d)) continue;
                dims.Add(d);
            }

            if (dims.Count == 0) return;

            dims = dims
                .OrderBy(d =>
                {
                    try
                    {
                        return TryGetDimXLinePoints(d, out Point3d p1, out Point3d p2)
                            ? Math.Min(p1.X, p2.X)
                            : 0.0;
                    }
                    catch
                    {
                        return 0.0;
                    }
                })
                .ToList();

            int n = Math.Min(dims.Count, bayWidthsCm.Count);
            for (int i = 0; i < n; i++)
            {
                long r = (long)Math.Round(bayWidthsCm[i]);
                dims[i].DimensionText = r.ToString(CultureInfo.InvariantCulture);
            }

            if (dims.Count != bayWidthsCm.Count)
                ed.WriteMessage("\nUyari: OLCU yatay boyut sayisi ({0}) ile plan aks araligi ({1}) esit degil; ilk {2} olcu metni guncellendi.",
                    dims.Count.ToString(CultureInfo.InvariantCulture),
                    bayWidthsCm.Count.ToString(CultureInfo.InvariantCulture),
                    n.ToString(CultureInfo.InvariantCulture));
        }
    }
}
