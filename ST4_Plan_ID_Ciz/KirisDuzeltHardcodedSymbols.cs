using System;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// KOT / KES_DET / DONATI sembol geometrisi (DXF normalize); taramalar SOLID, renkler sabit RGB.
    /// </summary>
    internal static class KirisDuzeltHardcodedSymbols
    {
        public const string LayerKotCizgi = "KOT CIZGISI (BEYKENT)";
        public const string LayerKesit = "KESIT_(BEYKENT)";
        public const string LayerDonatiKesit = "DONATI KESIT (BEYKENT)";

        /// <summary>KES_DET.dxf LAYER tablosu 62.</summary>
        public const short LayerColorKesitDxf = 151;

        /// <summary>DONATI.dxf LAYER tablosu 62.</summary>
        public const short LayerColorDonatiKesitDxf = 7;

        /// <summary>KOT.dxf ucgen dikey boy (normalize).</summary>
        public const double KotReferenceHeight = 15.0;

        /// <summary>KES_DET.dxf normalize genislik (dik cizgi X - sol).</summary>
        public const double KesDetReferenceWidth = 13.5;

        /// <summary>KES_DET.dxf dik cizgi uzunlugu (ust uç - alt uç, normalize).</summary>
        public const double KesDetReferenceHeight = 17.320508075689;

        /// <summary>DONATI.dxf AcDbCircle 40.</summary>
        public const double DonatiReferenceRadius = 2.500000118743628;

        /// <summary>DONATI.dxf daire capi (2 * yaricap).</summary>
        public const double DonatiReferenceDiameter = 2.0 * DonatiReferenceRadius;

        private static readonly Color KotHatchSolidColor = Color.FromRgb(0, 0, 0);
        private static readonly Color KesDetDonatiHatchSolidColor = Color.FromRgb(255, 255, 255);

        /// <summary>Blok referansi ile ayni yerlestirme: olcek * don * otelenme.</summary>
        public static Matrix3d CreatePlacementMatrix(Point3d insert, double uniformScale, double rotationRad)
        {
            return Matrix3d.Displacement(insert.GetAsVector())
                * Matrix3d.Rotation(rotationRad, Vector3d.ZAxis, Point3d.Origin)
                * Matrix3d.Scaling(uniformScale, Point3d.Origin);
        }

        private static void AppendAndTransform(Transaction tr, BlockTableRecord ms, Entity ent, Matrix3d mat)
        {
            ent.TransformBy(mat);
            ms.AppendEntity(ent);
            tr.AddNewlyCreatedDBObject(ent, true);
        }

        /// <summary>KOT: ucgen + SOLID siyah tarama + dort LINE.</summary>
        public static void AppendKotSymbol(Transaction tr, Database db, BlockTableRecord ms, Matrix3d mat)
        {
            var tri = new Polyline(3);
            tri.AddVertexAt(0, new Point2d(15.0, -15.0), 0, 0, 0);
            tri.AddVertexAt(1, new Point2d(0.0, 0.0), 0, 0, 0);
            tri.AddVertexAt(2, new Point2d(15.0, 0.0), 0, 0, 0);
            tri.Closed = true;
            tri.Layer = LayerKotCizgi;
            tri.Color = Color.FromColorIndex(ColorMethod.ByLayer, 0);
            AppendAndTransform(tr, ms, tri, mat);

            var hatch = new Hatch();
            ms.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);
            hatch.SetDatabaseDefaults(db);
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            hatch.HatchStyle = HatchStyle.Normal;
            hatch.Layer = LayerKotCizgi;
            hatch.Color = KotHatchSolidColor;
            hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { tri.ObjectId });
            hatch.EvaluateHatch(true);

            void Ln(double x0, double y0, double x1, double y1)
            {
                var ln = new Line(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0))
                {
                    Layer = LayerKotCizgi
                };
                AppendAndTransform(tr, ms, ln, mat);
            }

            Ln(0, 0, 30, 0);
            Ln(15, -15, 30, 0);
            Ln(30, 0, 15, 0);
            Ln(30, 0, 62.5, 0);
        }

        /// <summary>
        /// KES_DET polyline + LINE + SOLID beyaz tarama.
        /// </summary>
        public static void AppendKesDetSymbol(Transaction tr, Database db, BlockTableRecord ms, Matrix3d mat)
        {
            const double ax = 2005.896355612611;
            const double ayTop = 1973.698897679875;

            double Nx(double xWorld) => xWorld - ax;
            double Ny(double yWorld) => yWorld - ayTop;

            var pl = new Polyline(6);
            pl.AddVertexAt(0, new Point2d(Nx(2015.396355612611), Ny(1971.389496603116)), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(Nx(2005.896355612611), Ny(1965.904669045815)), 0.5773502691896245, 0, 0);
            pl.AddVertexAt(2, new Point2d(Nx(2005.896355612611), Ny(1964.172618238246)), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(Nx(2015.396355612611), Ny(1958.687790680944)), 0.5773502691896273, 0, 0);
            pl.AddVertexAt(4, new Point2d(Nx(2016.896355612611), Ny(1959.553816084729)), 0, 0, 0);
            pl.AddVertexAt(5, new Point2d(Nx(2016.896355612611), Ny(1970.523471199332)), 0.5773502691896273, 0, 0);
            pl.Closed = true;
            pl.Layer = LayerKesit;
            pl.Color = Color.FromColorIndex(ColorMethod.ByLayer, 0);
            AppendAndTransform(tr, ms, pl, mat);

            var ln = new Line(new Point3d(13.5, 0.0, 0.0), new Point3d(13.5, -KesDetReferenceHeight, 0.0))
            {
                Layer = LayerKesit
            };
            AppendAndTransform(tr, ms, ln, mat);

            var hatch = new Hatch();
            ms.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);
            hatch.SetDatabaseDefaults(db);
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            hatch.HatchStyle = HatchStyle.Normal;
            hatch.Layer = LayerKesit;
            hatch.Color = KesDetDonatiHatchSolidColor;
            hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { pl.ObjectId });
            hatch.EvaluateHatch(true);
        }

        /// <summary>DONATI: daire + SOLID beyaz tarama.</summary>
        public static void AppendDonatiSymbol(Transaction tr, Database db, BlockTableRecord ms, Matrix3d mat)
        {
            var c = new Circle(new Point3d(2.5, -2.5, 0.0), Vector3d.ZAxis, DonatiReferenceRadius)
            {
                Layer = LayerDonatiKesit
            };
            AppendAndTransform(tr, ms, c, mat);

            var hatch = new Hatch();
            ms.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);
            hatch.SetDatabaseDefaults(db);
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            hatch.HatchStyle = HatchStyle.Normal;
            hatch.Layer = LayerDonatiKesit;
            hatch.Color = KesDetDonatiHatchSolidColor;
            hatch.LineWeight = LineWeight.LineWeight035;
            hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { c.ObjectId });
            hatch.EvaluateHatch(true);
        }
    }
}
