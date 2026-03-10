using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace ST4AksCizCSharp
{
    public sealed class St4Model
    {
        public List<AxisLine> AxisX { get; } = new List<AxisLine>();
        public List<AxisLine> AxisY { get; } = new List<AxisLine>();
        public List<FloorInfo> Floors { get; } = new List<FloorInfo>();
        public List<ColumnAxisInfo> Columns { get; } = new List<ColumnAxisInfo>();
        public List<BeamInfo> Beams { get; } = new List<BeamInfo>();
        public Dictionary<int, (double W, double H)> ColumnDimsBySectionId { get; } = new Dictionary<int, (double W, double H)>();
        public Dictionary<int, List<Point2d>> PolygonSections { get; } = new Dictionary<int, List<Point2d>>();
        public Dictionary<int, int> PolygonColumnSectionByPositionSectionId { get; } = new Dictionary<int, int>();
    }
}
