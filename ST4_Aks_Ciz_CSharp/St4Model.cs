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
        /// <summary>Döşemeler (Floors Data): 4 (veya 3) aks ile sınırlı alanlar.</summary>
        public List<SlabInfo> Slabs { get; } = new List<SlabInfo>();
        /// <summary>Floors Data 1. satır 25. sütunu "1" olan döşeme ID'leri (merdiven döşemesi).</summary>
        public HashSet<int> StairSlabIds { get; } = new HashSet<int>();
        /// <summary>Dosya başlığı 5. satır 3. sütun: 100 veya 1000. Kat = slabId / SlabFloorKeyStep.</summary>
        public int SlabFloorKeyStep { get; set; }
        /// <summary>5. satır 4. sütun: kiriş kat = beamId / BeamFloorKeyStep (100 veya 1000).</summary>
        public int BeamFloorKeyStep { get; set; }
        /// <summary>5. satır 5. sütun: kolon kesit şeması (100 veya 1000).</summary>
        public int ColumnFloorKeyStep { get; set; }
        /// <summary>Şu an çizimde kullanılmıyor; ileride başka kaynaktan doldurulabilir.</summary>
        public Dictionary<int, int> SlabIdToFloorNo { get; } = new Dictionary<int, int>();
        public Dictionary<int, (double W, double H)> ColumnDimsBySectionId { get; } = new Dictionary<int, (double W, double H)>();
        public Dictionary<int, List<Point2d>> PolygonSections { get; } = new Dictionary<int, List<Point2d>>();
        public Dictionary<int, int> PolygonColumnSectionByPositionSectionId { get; } = new Dictionary<int, int>();
    }
}
