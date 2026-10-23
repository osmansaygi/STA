using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace ST4Yardimci
{
    /// <summary>Mevcut parapet/temel hatılı tablolarını da LINE/polyline sınırlarından tanır.</summary>
    internal static class BaMetrajPozCell
    {
        private const string IcAntetLayer = "IC ANTET (BEYKENT)";

        internal struct Segment
        {
            public Point3d A, B;
            public Segment(Point3d a, Point3d b) { A=a; B=b; }
        }

        internal static List<Segment> Collect(Transaction tr, BlockTableRecord space)
        {
            var result = new List<Segment>();
            Database db = space.Database;
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            // Transaction içinde SelectAll yok — acdb çökmesi riski.
            if (!lt.Has(IcAntetLayer)) return result;

            RXClass rxLine = RXClass.GetClass(typeof(Line));
            RXClass rxPl = RXClass.GetClass(typeof(Polyline));
            foreach (ObjectId id in space)
            {
                if (!id.IsValid || id.IsErased) continue;
                RXClass rx = id.ObjectClass;
                if (rx != rxLine && rx != rxPl) continue;
                var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (e == null || e.IsErased ||
                    !string.Equals(e.Layer, IcAntetLayer, StringComparison.OrdinalIgnoreCase))
                    continue;
                AddSegments(e, result);
            }
            return result;
        }

        /// <summary>Antet yolu: yalnız pencereyle seçilmiş nesnelerden (tam MS taraması yok).</summary>
        internal static List<Segment> Collect(Transaction tr, IEnumerable<ObjectId> ids)
        {
            var result = new List<Segment>();
            foreach (ObjectId id in ids)
            {
                if (!id.IsValid || id.IsErased) continue;
                var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (e == null || e.IsErased ||
                    !string.Equals(e.Layer, IcAntetLayer, StringComparison.OrdinalIgnoreCase))
                    continue;
                AddSegments(e, result);
            }
            return result;
        }

        private static void AddSegments(Entity e, List<Segment> result)
        {
            if (e is Line line) result.Add(new Segment(line.StartPoint, line.EndPoint));
            else if (e is Polyline p)
            {
                int n = p.NumberOfVertices;
                for (int i = 0; i < (p.Closed ? n : n - 1); i++)
                    if (Math.Abs(p.GetBulgeAt(i)) < 1e-8)
                        result.Add(new Segment(p.GetPoint3dAt(i), p.GetPoint3dAt((i + 1) % n)));
            }
        }

        internal static bool TryFind(Entity label, List<Segment> lines, out Extents3d cell)
        {
            cell=default;
            string text; double rotation;
            if(label is DBText t) { text=t.TextString; rotation=t.Rotation; }
            else if(label is MText m) { text=m.Contents; rotation=m.Rotation; }
            else return false;
            if((text??"").IndexOf("YATAY",StringComparison.OrdinalIgnoreCase)<0 || Math.Abs(Math.Sin(rotation))>1e-6) return false;
            var box=label.GeometricExtents;
            double y=(box.MinPoint.Y+box.MaxPoint.Y)/2;
            const double tol=0.1;
            var vertical=lines.Where(s=>Math.Abs(s.A.X-s.B.X)<tol && Math.Min(s.A.Y,s.B.Y)<y-tol && Math.Max(s.A.Y,s.B.Y)>y+tol).ToList();
            var lefts=vertical.Select(s=>s.A.X).Where(x=>x<box.MinPoint.X-tol).Distinct().OrderByDescending(x=>x).ToList();
            if(lefts.Count<2 || !vertical.Any(s=>s.A.X>box.MaxPoint.X+tol)) return false;
            double divider=lefts[0], left=lefts[1];
            var horizontal=lines.Where(s=>Math.Abs(s.A.Y-s.B.Y)<tol && Math.Min(s.A.X,s.B.X)<=left+tol
                && Math.Max(s.A.X,s.B.X)>=box.MaxPoint.X-tol).ToList();
            var lows=horizontal.Where(s=>s.A.Y<box.MinPoint.Y-tol).Select(s=>s.A.Y).ToList();
            var highs=horizontal.Where(s=>s.A.Y>box.MaxPoint.Y+tol).Select(s=>s.A.Y).ToList();
            if(lows.Count==0 || highs.Count==0) return false;
            double low=lows.Max(), high=highs.Min();
            // Her iki sütun sınırı da satırın tamamını kapsamalı.
            foreach(double x in new[]{left,divider})
                if(!vertical.Any(s=>Math.Abs(s.A.X-x)<tol && Math.Min(s.A.Y,s.B.Y)<=low+tol && Math.Max(s.A.Y,s.B.Y)>=high-tol)) return false;
            cell=new Extents3d(new Point3d(left,low,label.GeometricExtents.MinPoint.Z),new Point3d(divider,high,label.GeometricExtents.MinPoint.Z));
            return true;
        }

        internal static void Center(Entity balloon, DBText text, Extents3d cell)
        {
            Matrix3d fit=Fit(balloon,cell,out _);
            balloon.TransformBy(fit); text.TransformBy(fit);
        }

        /// <summary>Balonu hücreye sığdırıp ortalayan dönüşüm (önce ölçek ≤1, sonra taşıma).</summary>
        internal static Matrix3d Fit(Entity balloon, Extents3d cell, out double factor)
        {
            var box=balloon.GeometricExtents;
            var origin=new Point3d((box.MinPoint.X+box.MaxPoint.X)/2,(box.MinPoint.Y+box.MaxPoint.Y)/2,box.MinPoint.Z);
            double width=box.MaxPoint.X-box.MinPoint.X, height=box.MaxPoint.Y-box.MinPoint.Y;
            factor=Math.Min(1,Math.Min((cell.MaxPoint.X-cell.MinPoint.X)*0.85/Math.Max(width,1e-6),
                (cell.MaxPoint.Y-cell.MinPoint.Y)*0.85/Math.Max(height,1e-6)));
            var target=new Point3d((cell.MinPoint.X+cell.MaxPoint.X)/2,(cell.MinPoint.Y+cell.MaxPoint.Y)/2,origin.Z);
            Matrix3d move=Matrix3d.Displacement(target-origin);
            return factor<1 ? move*Matrix3d.Scaling(factor,origin) : move;
        }
    }
}
