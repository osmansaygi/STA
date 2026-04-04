using System;
using System.Collections.Generic;
using System.Linq;

namespace ST4AksCizCSharp
{
    /// <summary>
    /// Floors Data tek satırında 9–12. sütunlardaki (1-tabanlı) bir köşe aksının doğrultusu:
    /// STA4 eksen çizgisinin eğimi ve çizimde kullanılan açı (PlanIdDrawingManager ile aynı tanım).
    /// </summary>
    public sealed class SlabAxisDirectionEntry
    {
        public int AxisId { get; set; }
        public AxisKind Kind { get; set; }
        /// <summary>Axis data doğrusunun STA4 eğimi (AxisLine.Slope).</summary>
        public double Slope { get; set; }
        /// <summary>Doğrunun yön açısı (rad), (-π/2, π/2] — GetLineAngleRad ile.</summary>
        public double LineAngleRad { get; set; }
    }

    /// <summary>
    /// Bir döşeme paneli için dört köşe aksının (Floors Data sırası) doğrultu bilgisi ve iki kenar öbeğinin ortalama açısı.
    /// </summary>
    public sealed class SlabAxesFrameInfo
    {
        public int SlabId { get; set; }
        /// <summary>p[8]..p[11] sırası (Axis1..Axis4).</summary>
        public SlabAxisDirectionEntry[] AxisCorners { get; set; }
        /// <summary>Axis1 ile Axis2 doğrultuları yeterince paralelse ortak açı (rad); aksi halde NaN.</summary>
        public double Span12MeanAngleRad { get; set; }
        /// <summary>Axis3 ile Axis4 doğrultuları yeterince paralelse ortak açı (rad); aksi halde NaN.</summary>
        public double Span34MeanAngleRad { get; set; }
    }

    /// <summary>
    /// DENEME1: <see cref="SlabAxesFrameInfo.AxisCorners"/> (9–12 sütun) EKSEN gruplaması için; Span12/34 kenar özetleri.
    /// <see cref="PopulateCornerAxisDirections"/> yalnızca ihtiyaç duyulan komutta çağrılmalı.
    /// </summary>
    public static class SlabAxesDirections
    {
        private const double ParallelTolRad = 2.0 * Math.PI / 180.0;

        /// <summary>
        /// <see cref="St4Model.SlabAxesFrameBySlabId"/> sözlüğünü <see cref="St4Model.Slabs"/> + eksen geometrisinden doldurur.
        /// </summary>
        public static void PopulateCornerAxisDirections(St4Model model)
        {
            if (model == null) return;
            model.SlabAxesFrameBySlabId.Clear();
            Dictionary<int, AxisLine> axisById = model.AxisX.Concat(model.AxisY).ToDictionary(a => a.Id, a => a);

            foreach (SlabInfo slab in model.Slabs)
            {
                var corners = new SlabAxisDirectionEntry[4];
                int[] ids = { slab.Axis1, slab.Axis2, slab.Axis3, slab.Axis4 };
                for (int i = 0; i < 4; i++)
                {
                    int id = ids[i];
                    if (id != 0 && axisById.TryGetValue(id, out AxisLine line))
                    {
                        corners[i] = new SlabAxisDirectionEntry
                        {
                            AxisId = id,
                            Kind = line.Kind,
                            Slope = line.Slope,
                            LineAngleRad = AxisGeometryService.GetLineAngleRad(line)
                        };
                    }
                    else
                    {
                        corners[i] = new SlabAxisDirectionEntry
                        {
                            AxisId = id,
                            Kind = AxisKind.X,
                            Slope = 0.0,
                            LineAngleRad = double.NaN
                        };
                    }
                }

                double m12 = MeanAngleIfParallel(corners[0].LineAngleRad, corners[1].LineAngleRad);
                double m34 = MeanAngleIfParallel(corners[2].LineAngleRad, corners[3].LineAngleRad);

                model.SlabAxesFrameBySlabId[slab.SlabId] = new SlabAxesFrameInfo
                {
                    SlabId = slab.SlabId,
                    AxisCorners = corners,
                    Span12MeanAngleRad = m12,
                    Span34MeanAngleRad = m34
                };
            }
        }

        private static double MeanAngleIfParallel(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
            if (Math.Abs(NormalizeAngleRad(a - b)) > ParallelTolRad) return double.NaN;
            return NormalizeAngleRad((a + b) * 0.5);
        }

        private static double NormalizeAngleRad(double x)
        {
            while (x > Math.PI) x -= 2.0 * Math.PI;
            while (x <= -Math.PI) x += 2.0 * Math.PI;
            return x;
        }
    }
}
