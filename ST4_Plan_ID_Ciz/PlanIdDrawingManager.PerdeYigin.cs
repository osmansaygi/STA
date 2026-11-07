using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Union;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        /// <summary>
        /// KAPAMADETAY / KOLON50 perde görünüşü yığını: her kat grubunun plan geometrisi ve yerleşimi.
        /// Alt/üst kat perdeleri numara yerine plandaki üst üste binmeyle eşlenir; numara eşleşmesi yalnız
        /// aynı aks üzerinde yedek olarak kalır (eski davranış).
        /// </summary>
        private sealed class PerdeStackAnchor
        {
            public int FloorNo;
            public int FixedAxisId;
            public double AxisAngle;
            public Geometry BasePlan;
            public AffineTransformation Rot;
            public double Dx;
            public double OffX;
            public double OffY;
            public int StackKey;
            public double MinWidthCm;
            public Dictionary<int, double> NoToPlacedMinX;
        }

        private object _perdeStackOwner;
        private List<PerdeStackAnchor> _perdeStackAnchors;
        private Dictionary<int, HashSet<int>> _perdeStackFloors;
        private int _perdeStackNextKey;

        private void ResetPerdeStackAnchorsIfNeeded(object owner)
        {
            if (ReferenceEquals(_perdeStackOwner, owner) && _perdeStackAnchors != null) return;
            _perdeStackOwner = owner;
            _perdeStackAnchors = new List<PerdeStackAnchor>();
            _perdeStackFloors = new Dictionary<int, HashSet<int>>();
            _perdeStackNextKey = 100000;
        }

        private int AllocatePerdeStackKey(int minNo)
        {
            if (minNo > 0 && !_perdeStackFloors.ContainsKey(minNo)) return minNo;
            while (_perdeStackFloors.ContainsKey(_perdeStackNextKey)) _perdeStackNextKey++;
            return _perdeStackNextKey++;
        }

        /// <summary>Grubun perdelerinin kat ofsetsiz plan birleşimi (katlar arası karşılaştırma için).</summary>
        private Geometry PerdeStackBasePlan(
            IEnumerable<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> group,
            double offX, double offY)
        {
            var back = AffineTransformation.TranslationInstance(-offX, -offY);
            var list = new List<Geometry>();
            foreach (var it in group)
            {
                if (it.wall == null || it.wall.IsEmpty) continue;
                try
                {
                    var g = back.Transform(it.wall);
                    if (g != null && !g.IsEmpty) list.Add(g);
                }
                catch { }
            }
            if (list.Count == 0) return null;
            if (list.Count == 1) return list[0];
            try { return CascadedPolygonUnion.Union(list); }
            catch
            {
                Geometry u = list[0];
                for (int i = 1; i < list.Count; i++)
                {
                    try { u = u.Union(list[i]); } catch { }
                }
                return u;
            }
        }

        private static double PerdeStackMinWidth(
            IEnumerable<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> group)
        {
            double w = double.MaxValue;
            foreach (var it in group)
            {
                if (it.beam != null && it.beam.WidthCm > 1.0) w = Math.Min(w, it.beam.WidthCm);
            }
            return w == double.MaxValue ? 20.0 : w;
        }

        private static double RotX(AffineTransformation rot, double x, double y)
        {
            var c = rot.Transform(new Coordinate(x, y), new Coordinate());
            return c.X;
        }

        /// <summary>
        /// Başka katta, bu katı henüz içermeyen yığından eşleşme arar. Önce aynı doğrultuda plan çakışması
        /// (kesişim alanı &gt; 1,5·t²; köşe birleşimi sayılmaz), yoksa aynı aks + ortak perde numarası.
        /// </summary>
        private bool TryFindPerdeStackAnchor(
            FloorInfo floor,
            List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> group,
            List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> rotatedGroup,
            Geometry basePlan,
            double axisAngle,
            double offX,
            double offY,
            AffineTransformation rot,
            double srcLeftX,
            out int stackKey,
            out double targetLeftX)
        {
            stackKey = 0;
            targetLeftX = 0.0;
            if (_perdeStackAnchors == null || _perdeStackAnchors.Count == 0 || floor == null) return false;
            double myW = PerdeStackMinWidth(group);

            PerdeStackAnchor best = null;
            double bestArea = 0.0;
            Coordinate bestRef = null;
            if (basePlan != null && !basePlan.IsEmpty)
            {
                foreach (var a in _perdeStackAnchors)
                {
                    if (a.FloorNo == floor.FloorNo || a.BasePlan == null || a.BasePlan.IsEmpty) continue;
                    if (_perdeStackFloors.TryGetValue(a.StackKey, out var fl) && fl.Contains(floor.FloorNo)) continue;
                    if (Math.Abs(Math.Sin(a.AxisAngle - axisAngle)) > 0.02) continue;
                    if (!a.BasePlan.EnvelopeInternal.Intersects(basePlan.EnvelopeInternal)) continue;
                    Geometry inter;
                    try { inter = a.BasePlan.Intersection(basePlan); }
                    catch { continue; }
                    if (inter == null || inter.IsEmpty) continue;
                    double t = Math.Min(myW, a.MinWidthCm);
                    double area = inter.Area;
                    if (area <= 1.5 * t * t || area <= bestArea) continue;
                    best = a;
                    bestArea = area;
                    bestRef = inter.Centroid?.Coordinate;
                }
            }
            if (best != null && bestRef != null)
            {
                double ancX = RotX(best.Rot, bestRef.X + best.OffX, bestRef.Y + best.OffY) + best.Dx;
                double myX = RotX(rot, bestRef.X + offX, bestRef.Y + offY);
                targetLeftX = srcLeftX + (ancX - myX);
                stackKey = best.StackKey;
                return true;
            }

            // Yedek: aynı aks üzerinde ortak perde numarası (eski numara eşlemesi).
            int fixedAxis = group.Count > 0 ? group[0].fixedAxisId : 0;
            foreach (var a in _perdeStackAnchors)
            {
                if (a.FloorNo == floor.FloorNo || a.FixedAxisId != fixedAxis || a.NoToPlacedMinX == null) continue;
                if (_perdeStackFloors.TryGetValue(a.StackKey, out var fl) && fl.Contains(floor.FloorNo)) continue;
                foreach (var it in rotatedGroup)
                {
                    if (it.beam == null || it.wall == null || it.wall.IsEmpty) continue;
                    if (!a.NoToPlacedMinX.TryGetValue(GetBeamNumero(it.beam.BeamId), out double mapX)) continue;
                    targetLeftX = mapX - (it.wall.EnvelopeInternal.MinX - srcLeftX);
                    stackKey = a.StackKey;
                    return true;
                }
            }
            return false;
        }

        private void RecordPerdeStackAnchor(
            FloorInfo floor,
            List<(Geometry wall, int fixedAxisId, BeamInfo beam, List<(Geometry geom, ColumnAxisInfo col, (double W, double H) dim, Point2d center, int polygonSectionId)> columns)> rotatedGroup,
            Geometry basePlan,
            double axisAngle,
            int fixedAxisId,
            double offX,
            double offY,
            AffineTransformation rot,
            double dx,
            int stackKey)
        {
            if (_perdeStackAnchors == null || floor == null) return;
            var nos = new Dictionary<int, double>();
            foreach (var it in rotatedGroup)
            {
                if (it.beam == null || it.wall == null || it.wall.IsEmpty) continue;
                int no = GetBeamNumero(it.beam.BeamId);
                if (!nos.ContainsKey(no)) nos[no] = it.wall.EnvelopeInternal.MinX + dx;
            }
            _perdeStackAnchors.Add(new PerdeStackAnchor
            {
                FloorNo = floor.FloorNo,
                FixedAxisId = fixedAxisId,
                AxisAngle = axisAngle,
                BasePlan = basePlan,
                Rot = rot,
                Dx = dx,
                OffX = offX,
                OffY = offY,
                StackKey = stackKey,
                MinWidthCm = PerdeStackMinWidth(rotatedGroup),
                NoToPlacedMinX = nos
            });
            if (!_perdeStackFloors.TryGetValue(stackKey, out var fl))
            {
                fl = new HashSet<int>();
                _perdeStackFloors[stackKey] = fl;
            }
            fl.Add(floor.FloorNo);
        }
    }
}
