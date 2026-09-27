using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ST4Yardimci
{
    /// <summary>
    /// Oto Ölçü ölçüleri her zaman RotatedDimension; uzatma (finiş) en az yazı/2.
    /// </summary>
    internal static class OtoOlcuDimFactory
    {
        /// <summary>
        /// RotatedDimension yazar. Ext çizgi (DIMEXE) ≥ yazı yüksekliği / 2; iki uç açık.
        /// </summary>
        public static void PlaceRotated(
            Transaction tr,
            BlockTableRecord btr,
            Point3d x1,
            Point3d x2,
            Point3d dimLinePoint,
            ObjectId dimStyleId,
            string layer,
            Action<Dimension> afterStyle = null,
            bool enforceMinExtension = true)
        {
            var dim = new RotatedDimension();
            dim.SetDatabaseDefaults();
            Vector3d v = x2 - x1;
            dim.Rotation = Math.Atan2(v.Y, v.X);
            dim.XLine1Point = x1;
            dim.XLine2Point = x2;
            dim.DimLinePoint = dimLinePoint;
            if (!dimStyleId.IsNull)
                dim.DimensionStyle = dimStyleId;
            dim.Layer = layer;

            afterStyle?.Invoke(dim);
            if (enforceMinExtension)
                EnforceMinExtensionBeyond(dim);

            btr.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);

            if (enforceMinExtension)
                EnforceMinExtensionBeyond(dim);
        }

        /// <summary>DIMEXE ≥ Dimtxt·Dimscale / 2; uzatma uçları kapalı olmasın.</summary>
        public static void EnforceMinExtensionBeyond(Dimension dim)
        {
            if (dim == null) return;
            try { dim.Dimse1 = false; } catch { }
            try { dim.Dimse2 = false; } catch { }
            try { dim.DimfxlenOn = false; } catch { }

            double minExe = GetEffectiveTextHeight(dim) * 0.5;
            if (minExe < 1e-9) minExe = 1.25;
            try
            {
                if (dim.Dimexe < minExe)
                    dim.Dimexe = minExe;
            }
            catch { }
        }

        public static double GetEffectiveTextHeight(Dimension dim)
        {
            double txt = 2.5;
            double scale = 1.0;
            try
            {
                txt = dim.Dimtxt;
                scale = dim.Dimscale;
            }
            catch { }
            if (txt <= 1e-9) txt = 2.5;
            if (scale <= 1e-9) scale = 1.0;
            return Math.Abs(txt * scale);
        }

        public static double GetDimTextHeight(Transaction tr, ObjectId dimStyleId)
        {
            double txt = 2.5;
            double scale = 1.0;
            try
            {
                if (!dimStyleId.IsNull)
                {
                    var rec = tr.GetObject(dimStyleId, OpenMode.ForRead) as DimStyleTableRecord;
                    if (rec != null)
                    {
                        txt = rec.Dimtxt;
                        scale = rec.Dimscale;
                        if (scale <= 1e-9) scale = 1.0;
                    }
                }
            }
            catch { }
            if (txt <= 1e-9) txt = 2.5;
            return Math.Abs(txt * scale);
        }
    }
}
