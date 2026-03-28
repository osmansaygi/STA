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
        private const string LayerFlans = "ISKELE FLANS (BEYKENT)";
        private const string LayerKotYazi = "KOT YAZI (BEYKENT)";
        private const string LayerKotCizgi = "KOT CIZGI (BEYKENT)";
        private const string LayerKatAksi = "KAT AKSI (BEYKENT)";
        private const string LayerIskeleKot = "ISKELE KOT (BEYKENT)";
        private const string LayerIskeleAlt = "ISKELE_ALT";
        private const string LayerIskeleUst = "ISKELE_UST";
        private const string LayerIskeleDikey = "ISKELE DIKEY (BEYKENT)";
        private const string LayerTopukTahtasi = "TOPUK TAHTASI (BEYKENT)";
        private const string LayerIskeleCapraz = "ISKELE CAPRAZ (BEYKENT)";
        private const string LayerAksOlcu = "AKS OLCU (BEYKENT)";
        private const string LayerKesitIsmi = "KESIT ISMI (BEYKENT)";
        private const string YaziBeykentTextStyleName = "YAZI (BEYKENT)";
        private const short KesitKotWedgeSolidHatchColorIndex = 250;
        private const double KesitKotTriHeightCm = 12.0;
        private const double KesitKotTriHalfWidthCm = 12.0;
        private const double KesitKotExtTowardSectionCm = 20.0;
        private const double KesitKotExtTopRightCm = 26.0;
        private const double KesitKotTextAboveExtensionCm = 0.0;
        private const double KesitKotTextHeightCm = 10.0;

        /// <summary>Modül yüksekliği (cm).</summary>
        private const double IskeleLiftCm = 250.0;

        /// <summary>Kesit boyutu olcumleri: iki uzatma noktasi ayni Y (yatay aks araligi).</summary>
        private const double DimSameYTolCm = 3.0;

        public static void Execute(Document doc)
        {
            var ed = doc.Editor;
            var db = doc.Database;

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

            var pDatum = new PromptDoubleOptions("\nReferans 0 kotunu girin (cm, or: -50 veya 23): ")
            {
                AllowNone = false,
                AllowNegative = true,
                AllowZero = true,
                AllowArbitraryInput = false,
                DefaultValue = 0.0,
                UseDefaultValue = true
            };
            var rDatum = ed.GetDouble(pDatum);
            if (rDatum.Status != PromptStatus.OK) return;
            double datumCm = rDatum.Value;
            double datumM = datumCm / 100.0;

            var floors = model.Floors.OrderBy(f => f.ElevationM).ToList();
            ed.WriteMessage("\nST4 bina taban kotu: {0} m", model.BuildingBaseKotu.ToString("0.###", CultureInfo.InvariantCulture));
            ed.WriteMessage("\nReferans 0 kotu: {0} m ({1} cm)", datumM.ToString("0.###", CultureInfo.InvariantCulture), datumCm.ToString("0.##", CultureInfo.InvariantCulture));
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

            var pp1 = new PromptPointOptions("\nKesit hattinin baslangic noktasini secin: ");
            var rp1 = ed.GetPoint(pp1);
            if (rp1.Status != PromptStatus.OK) return;
            var cutPt1 = new Point3d(rp1.Value.X, rp1.Value.Y, 0);

            var pp2 = new PromptPointOptions("\nKesit hattinin bitis noktasini secin: ")
            {
                UseBasePoint = true,
                BasePoint = cutPt1
            };
            var rp2 = ed.GetPoint(pp2);
            if (rp2.Status != PromptStatus.OK) return;
            var cutPt2 = new Point3d(rp2.Value.X, rp2.Value.Y, 0);

            var cutSegs = new List<(Point3d a, Point3d b)> { (cutPt1, cutPt2) };

            var yataySegs = new List<IskeleKesitSectionGeometry.Seg2>();
            var flansCenters = new List<Point3d>();
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
                const double flansTargetRadiusCm = 6.5;
                const double flansRadiusTolCm = 0.2;
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    if (!string.Equals(ent.Layer, LayerFlans, StringComparison.Ordinal)) continue;
                    if (ent is Circle c && Math.Abs(c.Radius - flansTargetRadiusCm) <= flansRadiusTolCm)
                        flansCenters.Add(c.Center);
                }
                tr.Commit();
            }

            var stations = ComputeStationsFromFlansCenters(cutSegs, flansCenters);
            if (stations.Count < 2)
                stations = IskeleKesitSectionGeometry.ComputeStationsFromYatayMidlines(yataySegs, cutSegs);
            var alongSorted = stations
                .Select(s => (s, d: DistAlongPolylinePath(cutSegs, s)))
                .OrderBy(x => x.d)
                .ToList();
            var bayWidthsCm = new List<double>();
            for (int i = 0; i < alongSorted.Count - 1; i++)
                bayWidthsCm.Add(alongSorted[i + 1].d - alongSorted[i].d);
            var stationAlongs = alongSorted.Select(x => x.d).ToList();
            var yatayLabels = CollectProgramLineLabels(db, cutSegs, stationAlongs, bayWidthsCm);

            ed.WriteMessage("\nProgram hattinda bulunan dikme aks adedi: {0}", alongSorted.Count.ToString(CultureInfo.InvariantCulture));
            if (bayWidthsCm.Count > 0)
                ed.WriteMessage("\nDikmeler arasi mesafeler (cm): {0}", string.Join(" + ", bayWidthsCm.Select(b => Math.Round(b).ToString(CultureInfo.InvariantCulture))));

            var ppo = new PromptPointOptions("\nKat kotlari icin referans noktasi (taban kotu): ");
            var prp = ed.GetPoint(ppo);
            if (prp.Status != PromptStatus.OK) return;
            var ins = new Point3d(prp.Value.X, prp.Value.Y, 0);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureKesitDrawLayers(tr, db);
                DrawOnlyFloorElevations(tr, db, ins, model, bayWidthsCm, datumM, yatayLabels, ed);
                tr.Commit();
            }
            ed.WriteMessage("\nISKELEKESIT: ST4 kat kotlari ve program hattindan hesaplanan dikme akslari cizildi.");
        }

        private static string GetIskeleKesitTemplatePath()
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(dir))
                {
                    // DWG: Insert/Wblock daha az eNotApplicable verir; DXF karmaşık sembol tablolarında sorun cikar.
                    string dwg = Path.Combine(dir, "ISKELE_KESIT.dwg");
                    if (File.Exists(dwg)) return dwg;
                    string dxf = Path.Combine(dir, "ISKELE_KESIT.dxf");
                    if (File.Exists(dxf)) return dxf;
                }
                string dxfWalk = FindIskelePieceTemplatePath("ISKELE_KESIT.dxf");
                if (!string.IsNullOrEmpty(dxfWalk) && File.Exists(dxfWalk)) return dxfWalk;
                string dwgWalk = FindIskeleKesitDwgPath();
                if (!string.IsNullOrEmpty(dwgWalk) && File.Exists(dwgWalk)) return dwgWalk;
                return IskeleKesitEmbeddedTemplates.TryExtractEmbeddedDxf("ISKELE_KESIT.dxf");
            }
            catch
            {
                return IskeleKesitEmbeddedTemplates.TryExtractEmbeddedDxf("ISKELE_KESIT.dxf");
            }
        }

        private static string FindIskeleKesitDwgPath()
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(dir)) return null;
                string cur = dir;
                for (int i = 0; i < 10 && !string.IsNullOrEmpty(cur); i++)
                {
                    string p = Path.Combine(cur, "ISKELE_KESIT.dwg");
                    if (File.Exists(p)) return p;
                    string inDosyalar = Path.Combine(cur, "dosyalar", "ISKELE_KESIT.dwg");
                    if (File.Exists(inDosyalar)) return inDosyalar;
                    cur = Directory.GetParent(cur)?.FullName;
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Şablon DXF: önce DLL klasörü, sonra üst dizinlerde doğrudan veya <c>dosyalar\</c> altında aranır.
        /// (Projede şablonlar genelde repo kökündeki <c>dosyalar\</c> klasöründedir; derleme çıktısına kopyalanmalıdır.)
        /// </summary>
        private static string FindIskelePieceTemplatePath(string fileName)
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(dir)) return null;

                string cur = dir;
                for (int i = 0; i < 10 && !string.IsNullOrEmpty(cur); i++)
                {
                    string p = Path.Combine(cur, fileName);
                    if (File.Exists(p)) return p;
                    string inDosyalar = Path.Combine(cur, "dosyalar", fileName);
                    if (File.Exists(inDosyalar)) return inDosyalar;

                    var parent = Directory.GetParent(cur);
                    cur = parent != null ? parent.FullName : null;
                }

                string local = Path.Combine(dir, fileName);
                return File.Exists(local) ? local : null;
            }
            catch { return null; }
        }

        private static string GetIskeleTabanTemplatePath()
            => IskeleKesitEmbeddedTemplates.ResolveDxf("ISKELE_TABAN.dxf", () => FindIskelePieceTemplatePath("ISKELE_TABAN.dxf"));

        private static string GetIskeleD1TemplatePath()
            => IskeleKesitEmbeddedTemplates.ResolveDxf("ISKELE_D1.dxf", () => FindIskelePieceTemplatePath("ISKELE_D1.dxf"));

        private static string GetIskeleD2TemplatePath()
            => IskeleKesitEmbeddedTemplates.ResolveDxf("ISKELE_D2.dxf", () => FindIskelePieceTemplatePath("ISKELE_D2.dxf"));

        private static string GetIskeleC1TemplatePath()
            => IskeleKesitEmbeddedTemplates.ResolveDxf("ISKELE_C1.dxf", () => FindIskelePieceTemplatePath("ISKELE_C1.dxf"));

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

        private static double MinDistanceToPolylinePath(List<(Point3d a, Point3d b)> segs, Point3d p)
        {
            double bestPerp = double.MaxValue;
            foreach (var (a, b) in segs)
            {
                var ab = b - a;
                double len2 = ab.X * ab.X + ab.Y * ab.Y;
                if (len2 < 1e-18) continue;
                var ap = p - a;
                double t = (ap.X * ab.X + ap.Y * ab.Y) / len2;
                t = Math.Max(0, Math.Min(1, t));
                var proj = new Point3d(a.X + t * ab.X, a.Y + t * ab.Y, 0);
                bestPerp = Math.Min(bestPerp, p.DistanceTo(proj));
            }
            return bestPerp;
        }

        private static List<Point3d> ComputeStationsFromFlansCenters(List<(Point3d a, Point3d b)> cutSegs, List<Point3d> centers)
        {
            var near = new List<(Point3d p, double along)>();
            const double maxDistFromCutCm = 8.0;
            foreach (var c in centers)
            {
                double dPerp = MinDistanceToPolylinePath(cutSegs, c);
                if (dPerp > maxDistFromCutCm) continue;
                double along = DistAlongPolylinePath(cutSegs, c);
                near.Add((c, along));
            }

            if (near.Count == 0) return new List<Point3d>();
            near = near.OrderBy(x => x.along).ToList();

            var result = new List<Point3d> { near[0].p };
            double lastAlong = near[0].along;
            const double dedupeAlongTolCm = 5.0;
            for (int i = 1; i < near.Count; i++)
            {
                if (near[i].along - lastAlong < dedupeAlongTolCm) continue;
                result.Add(near[i].p);
                lastAlong = near[i].along;
            }
            return result;
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
            if (lt.Has(name))
            {
                var recExist = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
                try { recExist.Color = Color.FromColorIndex(ColorMethod.ByAci, color); } catch { }
                try
                {
                    var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                    if (ltt.Has(linetype)) recExist.LinetypeObjectId = ltt[linetype];
                }
                catch { }
                try { recExist.LineWeight = lw; } catch { }
                return lt[name];
            }
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
            EnsureLayerKesit(tr, db, LayerKotCizgi, 7, "Continuous", LineWeight.LineWeight020);
            EnsureLayerKesit(tr, db, LayerKotYazi, 7, "Continuous", LineWeight.LineWeight020);
            TryLoadLinetype(tr, db, "DASHED");
            EnsureLayerKesit(tr, db, LayerKatAksi, 5, "DASHED", LineWeight.LineWeight020);
            EnsureLayerKesit(tr, db, LayerIskeleKot, 253, "DASHED", LineWeight.LineWeight020);
            EnsureLayerKesit(tr, db, LayerYatay, 210, "Continuous", LineWeight.LineWeight030);
            EnsureLayerKesit(tr, db, LayerTopukTahtasi, 9, "Continuous", LineWeight.LineWeight030);
            EnsureLayerKesit(tr, db, LayerIskeleCapraz, 140, "Continuous", LineWeight.LineWeight020);
            EnsureLayerKesit(tr, db, LayerAksOlcu, 6, "Continuous", LineWeight.LineWeight018);
            EnsureLayerKesit(tr, db, LayerKesitIsmi, 6, "Continuous", LineWeight.LineWeight020);
        }

        private static void AppendLine(Transaction tr, BlockTableRecord btr, Point3d a, Point3d b, string layer)
        {
            var ln = new Line(a, b) { Layer = layer };
            btr.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
        }

        private static void AppendKesitKotLineEntity(Transaction tr, BlockTableRecord btr, double x1, double y1, double x2, double y2)
        {
            var ln = new Line(new Point3d(x1, y1, 0), new Point3d(x2, y2, 0)) { Layer = LayerKotCizgi };
            btr.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
        }

        private static void AppendKesitKotLeftWedgeSolidHatch(Transaction tr, BlockTableRecord btr, Point3d pA, Point3d pTL, Point3d pTM)
        {
            var pl = new Polyline(3);
            pl.AddVertexAt(0, new Point2d(pA.X, pA.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(pTL.X, pTL.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(pTM.X, pTM.Y), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = LayerKotCizgi;
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);

            var hatch = new Hatch
            {
                Layer = LayerKotCizgi,
                Color = Color.FromColorIndex(ColorMethod.ByAci, KesitKotWedgeSolidHatchColorIndex)
            };
            btr.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            hatch.Associative = true;
            hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { pl.ObjectId });
            try { hatch.EvaluateHatch(true); } catch { try { hatch.EvaluateHatch(false); } catch { } }
        }

        private static void DrawKesitKotClassicSymbol(Transaction tr, BlockTableRecord btr, double apexX, double apexY, double rotationRad)
        {
            double h = KesitKotTriHeightCm;
            double w = KesitKotTriHalfWidthCm;
            double eSec = KesitKotExtTowardSectionCm;
            double eR = KesitKotExtTopRightCm;
            double c = Math.Cos(rotationRad);
            double s = Math.Sin(rotationRad);

            void Lw(double lx, double ly, out double wx, out double wy)
            {
                wx = apexX + c * lx - s * ly;
                wy = apexY + s * lx + c * ly;
            }

            Point3d P(double lx, double ly)
            {
                Lw(lx, ly, out double wx, out double wy);
                return new Point3d(wx, wy, 0);
            }

            Lw(-eSec, 0, out double x1, out double y1);
            Lw(0, 0, out double x2, out double y2);
            AppendKesitKotLineEntity(tr, btr, x1, y1, x2, y2);
            var pA = P(0, 0);
            var pTL = P(-w, h);
            var pTM = P(0, h);
            var pTR = P(w, h);
            AppendKesitKotLeftWedgeSolidHatch(tr, btr, pA, pTL, pTM);
            AppendKesitKotLineEntity(tr, btr, pTL.X, pTL.Y, pTR.X, pTR.Y);
            AppendKesitKotLineEntity(tr, btr, pA.X, pA.Y, pTR.X, pTR.Y);
            AppendKesitKotLineEntity(tr, btr, pTR.X, pTR.Y, pTM.X, pTM.Y);
            var pExt = P(w + eR, h);
            AppendKesitKotLineEntity(tr, btr, pTR.X, pTR.Y, pExt.X, pExt.Y);
        }

        private static void AppendKesitKotElevationDbText(Transaction tr, BlockTableRecord btr, Database db, string text, double x, double y, double rotationRad)
        {
            var txt = new DBText
            {
                Layer = LayerKotYazi,
                Height = KesitKotTextHeightCm,
                TextStyleId = GetOrCreateYaziBeykentTextStyle(tr, db),
                TextString = text ?? string.Empty,
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode = TextVerticalMode.TextBottom,
                Position = new Point3d(x, y, 0),
                AlignmentPoint = new Point3d(x, y, 0),
                Rotation = rotationRad
            };
            try { txt.AdjustAlignment(db); } catch { }
            btr.AppendEntity(txt);
            tr.AddNewlyCreatedDBObject(txt, true);
        }

        private static void DrawOnlyFloorElevations(Transaction tr, Database db, Point3d ins, St4Model model, List<double> bayWidthsCm, double datumM, List<string> yatayLabels, Editor ed)
        {
            // Sabit 0.00 zorlanmaz: cizimde referans kot + bu kotun ustundeki ST4 kat kotlari kullanilir.
            // Not: ST4 "kat kotu" listesi floor satirlarindan gelir; bina taban kotu burada otomatik satir yapilmaz.
            var st4FloorElevs = model.Floors
                .Select(f => model.BuildingBaseKotu + f.ElevationM)
                .Distinct()
                .OrderBy(z => z)
                .ToList();
            var drawElevs = st4FloorElevs
                .Where(z => z >= datumM - 1e-9)
                .ToList();
            if (!drawElevs.Any(z => Math.Abs(z - datumM) < 1e-9))
                drawElevs.Add(datumM);
            drawElevs = drawElevs.Distinct().OrderBy(z => z).ToList();
            if (drawElevs.Count == 0)
            {
                ed.WriteMessage("\nUyari: Referans kot ve ustunde kat cizgisi bulunamadi; cizim yapilmadi.");
                return;
            }

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            double elevMinM = drawElevs[0];
            double elevMaxM = drawElevs[drawElevs.Count - 1];
            const double gridLeftInsetCm = 20.0;
            const double symbolGapFromGridCm = 40.0;
            const double axisExtendLeftCm = 120.0;
            const double axisExtendRightCm = 260.0;
            const double axisExtendBottomCm = 120.0;
            const double rotAa = 0.0;
            double gridWidthCm = (bayWidthsCm != null && bayWidthsCm.Count > 0) ? bayWidthsCm.Sum() : 250.0;
            double xGridLeft = ins.X + gridLeftInsetCm;
            double xGridRight = xGridLeft + gridWidthCm;
            double xSymbolApex = xGridRight + axisExtendRightCm + symbolGapFromGridCm;
            double yGridBottom = ins.Y;
            double yGridTop = ins.Y + (elevMaxM - elevMinM) * 100.0;
            double yZero = ins.Y + (datumM - elevMinM) * 100.0;
            var axisXs = new List<double> { xGridLeft };
            if (bayWidthsCm != null)
            {
                double xAcc = xGridLeft;
                foreach (double w in bayWidthsCm)
                {
                    xAcc += w;
                    axisXs.Add(xAcc);
                }
            }

            var katAksiYs = new List<double>();
            foreach (double em in drawElevs)
            {
                double yy = ins.Y + (em - elevMinM) * 100.0;
                katAksiYs.Add(yy);
                AppendLine(tr, btr, new Point3d(xGridLeft - axisExtendLeftCm, yy, 0), new Point3d(xGridRight + axisExtendRightCm, yy, 0), LayerKatAksi);
                DrawKesitKotClassicSymbol(tr, btr, xSymbolApex, yy, rotAa);
                double textX = xSymbolApex + KesitKotTriHalfWidthCm;
                double textY = yy + KesitKotTriHeightCm + KesitKotTextAboveExtensionCm;
                AppendKesitKotElevationDbText(tr, btr, db, FormatElevationLabelM(em), textX, textY, rotAa);
            }

            const double iskeleKotExtendCm = 100.0;
            const double iskeleKotSymbolShiftLeftCm = 100.0;
            const double floorHeightCm = 250.0;
            double xIskeleSymbolApex = xGridRight + 200.0 + symbolGapFromGridCm - iskeleKotSymbolShiftLeftCm;
            double iskeleTextX = xIskeleSymbolApex + KesitKotTriHalfWidthCm;
            double yLastKatKot = ins.Y + (drawElevs[drawElevs.Count - 1] - elevMinM) * 100.0;

            const double capraz250MinCm = 240.0;
            const double capraz250MaxCm = 260.0;
            var longBayIndices = new List<int>();
            if (bayWidthsCm != null)
            {
                for (int bi = 0; bi < bayWidthsCm.Count; bi++)
                {
                    if (bayWidthsCm[bi] >= capraz250MinCm && bayWidthsCm[bi] <= capraz250MaxCm)
                        longBayIndices.Add(bi);
                }
            }

            string tabanPath = GetIskeleTabanTemplatePath();
            string d1Path = GetIskeleD1TemplatePath();
            string d2Path = GetIskeleD2TemplatePath();
            string c1Path = GetIskeleC1TemplatePath();

            if (string.IsNullOrEmpty(tabanPath) || !File.Exists(tabanPath))
                ed.WriteMessage("\nUyari: ISKELE_TABAN.dxf bulunamadi — taban parcasi cizilmez. DLL ile ayni klasore veya ust dizinde/dosyalar\\ altina koyun ya da derleyip cikti klasorune kopyalayin.");
            if (string.IsNullOrEmpty(d1Path) || !File.Exists(d1Path))
                ed.WriteMessage("\nUyari: ISKELE_D1.dxf bulunamadi — 1. kat D1 dikmeleri cizilmez.");
            if (string.IsNullOrEmpty(d2Path) || !File.Exists(d2Path))
                ed.WriteMessage("\nUyari: ISKELE_D2.dxf bulunamadi — ust kat D2 dikmeleri cizilmez.");

            int maxFloors = 100;
            for (int fi = 0; fi < maxFloors; fi++)
            {
                double floorBaseY = yZero + fi * floorHeightCm;
                double yDikme = floorBaseY + 30.0;
                double yKot2 = floorBaseY + 80.0;
                double yKot3 = floorBaseY + 130.0;
                double yKot4 = floorBaseY + 180.0;

                if (yKot4 > yLastKatKot + 1e-6) break;

                double absKot2M = datumM + (fi * floorHeightCm + 80.0) / 100.0;

                if (fi == 0)
                {
                    double absDikmeM = datumM + 30.0 / 100.0;
                    AppendLine(tr, btr, new Point3d(xGridLeft - iskeleKotExtendCm, yDikme, 0), new Point3d(xGridRight + iskeleKotExtendCm, yDikme, 0), LayerIskeleKot);
                    DrawKesitKotClassicSymbol(tr, btr, xIskeleSymbolApex, yDikme, rotAa);
                    AppendKesitKotElevationDbText(tr, btr, db, FormatElevationLabelM(absDikmeM), iskeleTextX, yDikme + KesitKotTriHeightCm + KesitKotTextAboveExtensionCm, rotAa);
                }

                AppendLine(tr, btr, new Point3d(xGridLeft - iskeleKotExtendCm, yKot2, 0), new Point3d(xGridRight + iskeleKotExtendCm, yKot2, 0), LayerIskeleKot);
                DrawKesitKotClassicSymbol(tr, btr, xIskeleSymbolApex, yKot2, rotAa);
                AppendKesitKotElevationDbText(tr, btr, db, FormatElevationLabelM(absKot2M), iskeleTextX, yKot2 + KesitKotTriHeightCm + KesitKotTextAboveExtensionCm, rotAa);

                AppendLine(tr, btr, new Point3d(xGridLeft - iskeleKotExtendCm, yKot3, 0), new Point3d(xGridRight + iskeleKotExtendCm, yKot3, 0), LayerIskeleKot);
                AppendLine(tr, btr, new Point3d(xGridLeft - iskeleKotExtendCm, yKot4, 0), new Point3d(xGridRight + iskeleKotExtendCm, yKot4, 0), LayerIskeleKot);

                if (fi == 0)
                {
                    if (!string.IsNullOrEmpty(tabanPath) && File.Exists(tabanPath))
                    {
                        if (!TryCopyIskeleTabanEntitiesAtAxes(tr, db, btr, tabanPath, axisXs, yZero, out string errTab))
                            ed.WriteMessage("\nUyari: ISKELE_TABAN.dxf yerlestirilemedi: {0}", errTab ?? "?");
                    }

                    if (!string.IsNullOrEmpty(d1Path) && File.Exists(d1Path))
                    {
                        if (!TryCopyIskeleTabanEntitiesAtAxes(tr, db, btr, d1Path, axisXs, yDikme, out string errD1, textLiftCm: 50.0))
                            ed.WriteMessage("\nUyari: ISKELE_D1.dxf yerlestirilemedi: {0}", errD1 ?? "?");
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(d2Path) && File.Exists(d2Path))
                    {
                        if (!TryCopyIskeleTabanEntitiesAtAxes(tr, db, btr, d2Path, axisXs, yDikme + 25.0, out string errD2))
                            ed.WriteMessage("\nUyari: ISKELE_D2.dxf kat {0} yerlestirilemedi: {1}", (fi + 1).ToString(), errD2 ?? "?");
                    }
                }

                DrawYatayTopukByBays(tr, db, btr, axisXs, yKot2, yKot3, yKot4, yatayLabels);

                if (!string.IsNullOrEmpty(c1Path) && File.Exists(c1Path) && longBayIndices.Count > 0)
                {
                    bool use45 = (fi % 2 == 0);
                    double caprazTargetY = use45 ? yKot2 : yKot2 + floorHeightCm;
                    for (int k = 0; k < longBayIndices.Count; k += 3)
                    {
                        int bi = longBayIndices[k];
                        if (!TryCopyIskeleCaprazAtPosition(tr, db, btr, c1Path, axisXs[bi], caprazTargetY, use45, out string errC))
                            ed.WriteMessage("\nUyari: ISKELE_C1.dxf kat {0} bay {1}: {2}", (fi + 1).ToString(), bi.ToString(), errC ?? "?");
                    }
                }
            }

            double yAxisBottom = yGridBottom - axisExtendBottomCm;
            if (bayWidthsCm != null && bayWidthsCm.Count > 0)
            {
                double y1top = Math.Max(yGridTop, yLastKatKot) + axisExtendBottomCm;
                foreach (double xAxis in axisXs)
                {
                    AppendLine(tr, btr, new Point3d(xAxis, yAxisBottom, 0), new Point3d(xAxis, y1top, 0), LayerKatAksi);
                }
            }

            const double dimInsetCm = 30.0;
            const double dimRowGapCm = 20.0;
            const double dimTextHeightCm = 12.0;
            ObjectId aksOlcuDimStyleId = PlanIdDrawingManager.GetOrCreateAksOlcuDimStyle(tr, db, dimTextHeightCm);

            if (katAksiYs.Count >= 2)
            {
                double xRefRight = xGridRight + axisExtendRightCm;
                double xTotalRight = xRefRight - dimInsetCm;
                double xIndRight = xRefRight - dimInsetCm - dimRowGapCm;
                double yFirst = katAksiYs[0];
                double yLast = katAksiYs[katAksiYs.Count - 1];

                var dimTotal = new AlignedDimension(
                    new Point3d(xTotalRight, yFirst, 0),
                    new Point3d(xTotalRight, yLast, 0),
                    new Point3d(xTotalRight, (yFirst + yLast) * 0.5, 0),
                    "", aksOlcuDimStyleId) { Layer = LayerAksOlcu };
                btr.AppendEntity(dimTotal);
                tr.AddNewlyCreatedDBObject(dimTotal, true);

                for (int i = 0; i < katAksiYs.Count - 1; i++)
                {
                    double ya = katAksiYs[i], yb = katAksiYs[i + 1];
                    var dimInd = new AlignedDimension(
                        new Point3d(xIndRight, ya, 0),
                        new Point3d(xIndRight, yb, 0),
                        new Point3d(xIndRight, (ya + yb) * 0.5, 0),
                        "", aksOlcuDimStyleId) { Layer = LayerAksOlcu };
                    btr.AppendEntity(dimInd);
                    tr.AddNewlyCreatedDBObject(dimInd, true);
                }
            }

            if (axisXs.Count >= 2)
            {
                double yTotalBot = yAxisBottom + dimInsetCm;
                double yIndBot = yAxisBottom + dimInsetCm + dimRowGapCm;
                double xFirst = axisXs[0];
                double xLast = axisXs[axisXs.Count - 1];

                var dimTotalX = new AlignedDimension(
                    new Point3d(xFirst, yTotalBot, 0),
                    new Point3d(xLast, yTotalBot, 0),
                    new Point3d((xFirst + xLast) * 0.5, yTotalBot, 0),
                    "", aksOlcuDimStyleId) { Layer = LayerAksOlcu };
                btr.AppendEntity(dimTotalX);
                tr.AddNewlyCreatedDBObject(dimTotalX, true);

                for (int i = 0; i < axisXs.Count - 1; i++)
                {
                    double xa = axisXs[i], xb = axisXs[i + 1];
                    var dimIndX = new AlignedDimension(
                        new Point3d(xa, yIndBot, 0),
                        new Point3d(xb, yIndBot, 0),
                        new Point3d((xa + xb) * 0.5, yIndBot, 0),
                        "", aksOlcuDimStyleId) { Layer = LayerAksOlcu };
                    btr.AppendEntity(dimIndX);
                    tr.AddNewlyCreatedDBObject(dimIndX, true);
                }
            }

            const double kesitTitleHeightCm = 20.0;
            const double titleGapBelowAxisEndCm = 50.0;
            double titleCx = (axisXs[0] + axisXs[axisXs.Count - 1]) * 0.5;
            double titleYTop = yAxisBottom - titleGapBelowAxisEndCm;
            var titleTxt = new DBText
            {
                Layer = LayerKesitIsmi,
                Height = kesitTitleHeightCm,
                TextStyleId = GetOrCreateYaziBeykentTextStyle(tr, db),
                TextString = "\u0130SKELE KES\u0130T\u0130 (1:50)",
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextTop,
                Position = new Point3d(titleCx, titleYTop, 0),
                AlignmentPoint = new Point3d(titleCx, titleYTop, 0),
                LineWeight = LineWeight.LineWeight020
            };
            try { titleTxt.AdjustAlignment(db); } catch { }
            btr.AppendEntity(titleTxt);
            tr.AddNewlyCreatedDBObject(titleTxt, true);
        }

        private static void DrawYatayTopukByBays(Transaction tr, Database db, BlockTableRecord btr, List<double> axisXs, double y1, double y2, double y3, List<string> labels)
        {
            if (axisXs == null || axisXs.Count < 2) return;
            ObjectId textStyleId = GetOrCreateYaziBeykentTextStyle(tr, db);

            const double dikmeHalfW = 2.415;
            const double pairHalfGap = 2.5;
            const double topukAboveUpperCm = 13.0;

            double[] targetRows = { y1, y2, y3 };
            for (int yi = 0; yi < targetRows.Length; yi++)
            {
                double yCenter = targetRows[yi];
                double yLo = yCenter - pairHalfGap;
                double yHi = yCenter + pairHalfGap;

                for (int i = 0; i < axisXs.Count - 1; i++)
                {
                    double axL = axisXs[i], axR = axisXs[i + 1];
                    double le = axL + dikmeHalfW;
                    double re = axR - dikmeHalfW;
                    if (re - le < 2.0) continue;

                    foreach (double yLine in new[] { yLo, yHi })
                    {
                        AppendLine(tr, btr, new Point3d(le, yLine, 0), new Point3d(le + 1, yLine, 0), LayerYatay);
                        AppendLine(tr, btr, new Point3d(le + 3, yLine, 0), new Point3d(re - 3, yLine, 0), LayerYatay);
                        AppendLine(tr, btr, new Point3d(re - 1, yLine, 0), new Point3d(re, yLine, 0), LayerYatay);
                    }

                    DrawYatayJointPocket(tr, btr, le, +1, yHi, +1);
                    DrawYatayJointPocket(tr, btr, le, +1, yLo, -1);
                    DrawYatayJointPocket(tr, btr, re, -1, yHi, +1);
                    DrawYatayJointPocket(tr, btr, re, -1, yLo, -1);

                    if (yi == 0)
                    {
                        double yTopuk = yHi + topukAboveUpperCm;
                        AppendLine(tr, btr, new Point3d(le, yTopuk, 0), new Point3d(re, yTopuk, 0), LayerTopukTahtasi);
                    }

                    if (labels != null && i < labels.Count && !string.IsNullOrEmpty(labels[i]))
                    {
                        string lbl = labels[i];
                        var txt = new DBText
                        {
                            Layer = "YAZI (BEYKENT)",
                            TextStyleId = textStyleId,
                            Height = 8.0,
                            TextString = lbl,
                            HorizontalMode = TextHorizontalMode.TextCenter,
                            VerticalMode = TextVerticalMode.TextBottom,
                            Position = new Point3d((axL + axR) * 0.5, yHi + 3.0, 0),
                            AlignmentPoint = new Point3d((axL + axR) * 0.5, yHi + 3.0, 0)
                        };
                        btr.AppendEntity(txt);
                        tr.AddNewlyCreatedDBObject(txt, true);
                    }
                }
            }
        }

        /// <summary>
        /// KESIT_YATAY.dxf geometrisine birebir uygun klip/kelepçe cep detayı.
        /// </summary>
        /// <param name="edge">Dikme dış kenarı X</param>
        /// <param name="hDir">+1 sol kenar (sağa doğru), -1 sağ kenar (sola doğru)</param>
        /// <param name="yBase">Yatay çizgi Y (yHi veya yLo)</param>
        /// <param name="vDir">+1 yukarı cep, -1 aşağı cep</param>
        private static void DrawYatayJointPocket(Transaction tr, BlockTableRecord btr,
            double edge, int hDir, double yBase, int vDir)
        {
            double x1 = edge + hDir * 1.0;
            double x3 = edge + hDir * 3.0;
            double x5 = edge + hDir * 5.0;
            double y1 = yBase + vDir * 1.0;
            double y2 = yBase + vDir * 2.0;

            AppendLine(tr, btr, new Point3d(edge, y1, 0), new Point3d(x1, y1, 0), LayerYatay);
            AppendLine(tr, btr, new Point3d(x1, yBase, 0), new Point3d(x1, y2, 0), LayerYatay);
            AppendLine(tr, btr, new Point3d(x1, y2, 0), new Point3d(x3, y2, 0), LayerYatay);
            AppendLine(tr, btr, new Point3d(x3, yBase, 0), new Point3d(x3, y2, 0), LayerYatay);
            AppendLine(tr, btr, new Point3d(x3, y1, 0), new Point3d(x5, y1, 0), LayerYatay);
            AppendLine(tr, btr, new Point3d(x5, yBase, 0), new Point3d(x5, y1, 0), LayerYatay);

            double cx = edge + hDir * 2.0;
            double cy = yBase + vDir * 1.875;
            const double r = 2.125;
            double startDeg = vDir > 0 ? 241.9275130641715 : 61.92751306417156;
            double endDeg = vDir > 0 ? 298.0724869358285 : 118.0724869358284;
            double startRad = startDeg * Math.PI / 180.0;
            double endRad = endDeg * Math.PI / 180.0;

            var arc = new Arc(new Point3d(cx, cy, 0), r, startRad, endRad) { Layer = LayerYatay };
            btr.AppendEntity(arc);
            tr.AddNewlyCreatedDBObject(arc, true);
        }

        private static List<string> CollectProgramLineLabels(Database db, List<(Point3d a, Point3d b)> cutSegs, List<double> stationAlongs, List<double> bayWidthsCm)
        {
            const double maxDistCm = 15.0;
            var candidates = new List<(double along, string text)>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    var t = tr.GetObject(id, OpenMode.ForRead) as DBText;
                    if (t == null) continue;
                    if (string.IsNullOrWhiteSpace(t.TextString)) continue;
                    if (!t.TextString.Contains("(") || !t.TextString.Contains(")")) continue;
                    double d = MinDistanceToPolylinePath(cutSegs, t.Position);
                    if (d > maxDistCm) continue;
                    double along = DistAlongPolylinePath(cutSegs, t.Position);
                    candidates.Add((along, t.TextString.Trim()));
                }
                tr.Commit();
            }

            int bayCount = stationAlongs.Count - 1;
            if (bayCount <= 0) return new List<string>();

            var directLabels = new string[bayCount];
            for (int i = 0; i < bayCount; i++)
            {
                double lo = stationAlongs[i];
                double hi = stationAlongs[i + 1];
                double mid = (lo + hi) * 0.5;
                var inBay = candidates
                    .Where(c => c.along >= lo - 1e-6 && c.along <= hi + 1e-6)
                    .OrderBy(c => Math.Abs(c.along - mid))
                    .ToList();
                directLabels[i] = inBay.Count > 0 ? inBay[0].text : null;
            }

            const double widthTolCm = 10.0;
            var result = new List<string>();
            for (int i = 0; i < bayCount; i++)
            {
                if (directLabels[i] != null)
                {
                    result.Add(directLabels[i]);
                    continue;
                }
                double w = (bayWidthsCm != null && i < bayWidthsCm.Count) ? bayWidthsCm[i] : -1;
                string fallback = null;
                if (w > 0)
                {
                    for (int j = 0; j < bayCount; j++)
                    {
                        if (directLabels[j] == null) continue;
                        double wj = (bayWidthsCm != null && j < bayWidthsCm.Count) ? bayWidthsCm[j] : -1;
                        if (wj > 0 && Math.Abs(wj - w) <= widthTolCm) { fallback = directLabels[j]; break; }
                    }
                }
                result.Add(fallback ?? "");
            }
            return result;
        }

        private static bool TryCopyIskeleTabanEntitiesAtAxes(Transaction tr, Database db, BlockTableRecord btr, string dxfPath, List<double> axisXs, double yZero, out string error, double textLiftCm = 0.0)
        {
            error = null;
            if (axisXs == null || axisXs.Count == 0) return true;
            Database srcDb = null;
            try
            {
                srcDb = new Database(false, true);
                srcDb.DxfIn(dxfPath, Path.Combine(Path.GetTempPath(), "ST4PlanIdCiz_iskele_taban_dxf.log"));

                double minX = srcDb.Extmin.X, minY = srcDb.Extmin.Y, maxX = srcDb.Extmax.X, maxY = srcDb.Extmax.Y;
                if (!(minX < maxX && minY < maxY))
                {
                    minX = 0; minY = 0; maxX = 0; maxY = 0;
                }
                // 0 kotunun ustune gelsin diye taban geometri alt siniri (minY) sifira oturur.
                double refY = minY;

                var srcIds = new ObjectIdCollection();
                using (var trSrc = srcDb.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)trSrc.GetObject(srcDb.CurrentSpaceId, OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsValid && !id.IsErased) srcIds.Add(id);
                    }
                    trSrc.Commit();
                }

                if (srcIds.Count == 0)
                {
                    error = "ISKELE_TABAN.dxf ModelSpace bos.";
                    return false;
                }

                foreach (double x in axisXs)
                {
                    var mapping = new IdMapping();
                    db.WblockCloneObjects(srcIds, btr.ObjectId, mapping, DuplicateRecordCloning.Ignore, false);
                    double clonedCenterX = double.NaN;
                    double dikeyLo = double.MaxValue, dikeyHi = double.MinValue;
                    foreach (IdPair pair in mapping)
                    {
                        if (!pair.Value.IsValid) continue;
                        var ent = tr.GetObject(pair.Value, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        if (!string.Equals(ent.Layer, LayerIskeleDikey, StringComparison.Ordinal)) continue;
                        try
                        {
                            var ex = ent.GeometricExtents;
                            dikeyLo = Math.Min(dikeyLo, ex.MinPoint.X);
                            dikeyHi = Math.Max(dikeyHi, ex.MaxPoint.X);
                        }
                        catch { }
                    }
                    if (dikeyLo < dikeyHi)
                    {
                        clonedCenterX = (dikeyLo + dikeyHi) * 0.5;
                    }
                    else
                    {
                        double lo = double.MaxValue, hi = double.MinValue;
                        foreach (IdPair pair in mapping)
                        {
                            if (!pair.Value.IsValid) continue;
                            var ent = tr.GetObject(pair.Value, OpenMode.ForRead) as Entity;
                            if (ent == null) continue;
                            try
                            {
                                var ex = ent.GeometricExtents;
                                lo = Math.Min(lo, ex.MinPoint.X);
                                hi = Math.Max(hi, ex.MaxPoint.X);
                            }
                            catch { }
                        }
                        if (lo < hi) clonedCenterX = (lo + hi) * 0.5;
                    }

                    double dx = double.IsNaN(clonedCenterX) ? 0.0 : (x - clonedCenterX);
                    double dy = yZero - refY;
                    var disp = Matrix3d.Displacement(new Vector3d(dx, dy, 0));

                    Matrix3d? textLift = Math.Abs(textLiftCm) > 1e-9
                        ? Matrix3d.Displacement(new Vector3d(0, textLiftCm, 0))
                        : (Matrix3d?)null;

                    foreach (IdPair pair in mapping)
                    {
                        if (!pair.Value.IsValid) continue;
                        var ent = tr.GetObject(pair.Value, OpenMode.ForWrite) as Entity;
                        if (ent == null) continue;
                        ent.TransformBy(disp);
                        if (textLift.HasValue && ent is DBText)
                            ent.TransformBy(textLift.Value);
                    }
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
                srcDb?.Dispose();
            }
        }

        /// <summary>
        /// ISKELE_C1.dxf'den 45° veya 135° çapraz grubunu okuyup hedef noktaya yerleştirir.
        /// Alt R≈1.263 dairenin merkezi hedef (targetX, targetY) konumuna oturur.
        /// </summary>
        /// <param name="use45">true → 45° çapraz, false → 135° çapraz</param>
        private static bool TryCopyIskeleCaprazAtPosition(Transaction tr, Database db, BlockTableRecord btr,
            string dxfPath, double targetX, double targetY, bool use45, out string error)
        {
            error = null;
            Database srcDb = null;
            try
            {
                srcDb = new Database(false, true);
                srcDb.DxfIn(dxfPath, Path.Combine(Path.GetTempPath(), "ST4PlanIdCiz_iskele_c1_dxf.log"));

                const double endCircleRadius = 1.263;
                const double radiusTol = 0.05;
                var endCircles = new List<(ObjectId id, double cx, double cy)>();
                var allIds = new List<(ObjectId id, double cx)>();

                using (var trSrc = srcDb.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)trSrc.GetObject(srcDb.CurrentSpaceId, OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (!id.IsValid || id.IsErased) continue;
                        var ent = trSrc.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        try
                        {
                            var ex = ent.GeometricExtents;
                            double ecx = (ex.MinPoint.X + ex.MaxPoint.X) * 0.5;
                            allIds.Add((id, ecx));
                            if (ent is Circle c && Math.Abs(c.Radius - endCircleRadius) < radiusTol)
                                endCircles.Add((id, c.Center.X, c.Center.Y));
                        }
                        catch { }
                    }
                    trSrc.Commit();
                }

                if (endCircles.Count < 2)
                {
                    error = "ISKELE_C1.dxf uc daireleri bulunamadi.";
                    return false;
                }

                double xMid = (endCircles.Min(c => c.cx) + endCircles.Max(c => c.cx)) * 0.5;
                var groupCircles = endCircles.Where(c => use45 ? c.cx < xMid : c.cx > xMid).ToList();
                if (groupCircles.Count < 2)
                {
                    error = "Secilen capraz grubu icin uc daireleri bulunamadi.";
                    return false;
                }

                var refCircle = use45
                    ? groupCircles.OrderBy(c => c.cy).First()
                    : groupCircles.OrderByDescending(c => c.cy).First();
                double srcRefX = refCircle.cx;
                double srcRefY = refCircle.cy;

                double groupMinX = groupCircles.Min(c => c.cx) - 15;
                double groupMaxX = groupCircles.Max(c => c.cx) + 15;

                var srcIds = new ObjectIdCollection();
                foreach (var (id, ecx) in allIds)
                {
                    if (ecx >= groupMinX && ecx <= groupMaxX)
                        srcIds.Add(id);
                }

                if (srcIds.Count == 0)
                {
                    error = "Secilen capraz grubu bos.";
                    return false;
                }

                var mapping = new IdMapping();
                db.WblockCloneObjects(srcIds, btr.ObjectId, mapping, DuplicateRecordCloning.Ignore, false);

                double dx = targetX - srcRefX;
                double dy = targetY - srcRefY;
                var disp = Matrix3d.Displacement(new Vector3d(dx, dy, 0));

                foreach (IdPair pair in mapping)
                {
                    if (!pair.Value.IsValid) continue;
                    var ent = tr.GetObject(pair.Value, OpenMode.ForWrite) as Entity;
                    if (ent == null) continue;
                    ent.TransformBy(disp);
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
                srcDb?.Dispose();
            }
        }

        // NOTE: Dikey merkez hizasi artik kaynak DB'den degil, her kopyalanan geometri setinden
        // anlik R=6.5 dairelerinden hesaplanir; bu sayede kayma birikimi engellenir.

        private static ObjectId GetOrCreateYaziBeykentTextStyle(Transaction tr, Database db)
        {
            var txtTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (txtTable.Has(YaziBeykentTextStyleName)) return txtTable[YaziBeykentTextStyleName];

            var rec = new TextStyleTableRecord { Name = YaziBeykentTextStyleName };
            try
            {
                rec.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Bahnschrift Light Condensed", false, false, 0, 0);
            }
            catch
            {
                try { rec.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Arial", false, false, 0, 0); } catch { }
            }
            try { rec.TextSize = 0.0; } catch { }
            try { rec.XScale = 1.0; } catch { }
            txtTable.UpgradeOpen();
            ObjectId id = txtTable.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            txtTable.DowngradeOpen();
            return id;
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
                AppendLine(tr, btr, new Point3d(xLeft, yy, 0), new Point3d(xRight, yy, 0), LayerKatAksi);
                DrawKesitKotClassicSymbol(tr, btr, xLeft, yy, 0.0);
                AppendKesitKotElevationDbText(tr, btr, db, FormatElevationLabelM(em), xLeft + KesitKotTriHalfWidthCm, yy + KesitKotTriHeightCm, 0.0);
            }
        }

        private static List<double> GetSortedAbsoluteElevationsM(St4Model model)
        {
            var set = new SortedSet<double>();
            set.Add(model.BuildingBaseKotu);
            set.Add(0.0);
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
