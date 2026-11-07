namespace Autodesk.AutoCAD.Geometry
{
    public struct Point2d
    {
        public double X, Y;
        public Point2d(double x, double y) { X = x; Y = y; }
    }
}
namespace ST4AksCizCSharp
{
    internal static class AxisGeometryService
    {
        public static double GetLineAngleRad(AxisLine l) => 0.0;
    }
}
