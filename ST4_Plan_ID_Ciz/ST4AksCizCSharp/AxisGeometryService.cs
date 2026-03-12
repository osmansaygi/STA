using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Geometry;

namespace ST4AksCizCSharp
{
    public sealed class AxisGeometryService
    {
        private readonly Dictionary<int, AxisLine> _axisById;

        public AxisGeometryService(St4Model model)
        {
            _axisById = model.AxisX.Concat(model.AxisY).ToDictionary(a => a.Id, a => a);
        }

        public bool TryIntersect(int axisA, int axisB, out Point2d p)
        {
            p = default;
            if (!TryLineCoeff(axisA, out var l1) || !TryLineCoeff(axisB, out var l2)) return false;
            double d = l1.A * l2.B - l2.A * l1.B;
            if (Math.Abs(d) <= 1e-12) return false;
            double x = (l1.B * l2.C - l2.B * l1.C) / d;
            double y = (l2.A * l1.C - l1.A * l2.C) / d;
            p = new Point2d(x, y);
            return true;
        }

        private bool TryLineCoeff(int axisId, out (double A, double B, double C) coeff)
        {
            coeff = default;
            if (!_axisById.TryGetValue(axisId, out AxisLine axis)) return false;
            if (axis.Kind == AxisKind.X)
            {
                coeff = (1.0, -axis.Slope, -axis.ValueCm);
                return true;
            }

            coeff = (axis.Slope, 1.0, axis.ValueCm);
            return true;
        }
    }
}
