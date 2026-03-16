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
        public List<int> ColumnIdsFromColumnsData { get; } = new List<int>();
        public Dictionary<int, ColumnPositionFromFloors> ColumnPositionBySectionId { get; } = new Dictionary<int, ColumnPositionFromFloors>();
        public List<SingleFootingInfo> SingleFootings { get; } = new List<SingleFootingInfo>();

        /// <summary>Sürekli temeller (Continuous foundations): sabit eksen üzerinde iki eksen arası şerit.</summary>
        public List<ContinuousFoundationInfo> ContinuousFoundations { get; } = new List<ContinuousFoundationInfo>();
        /// <summary>Radye temeller (Slab foundations): dört eksenle sınırlı dörtgen plak.</summary>
        public List<SlabFoundationInfo> SlabFoundations { get; } = new List<SlabFoundationInfo>();
        /// <summary>Bağ kirişi verisi (Tie beams): temel planında kiriş mantığında çizilir; 1. satır: genişlik, 3–5 sabit/başlangıç/bitiş aks, 6. sütun kaçıklık (mm). Çizimde: sürekli temel veya radye içinde ise TEMEL HATILI katmanına → "Radye temel temel hatılı"; dışında ise TEMEL katmanına → "Bağ kirişi".</summary>
        public List<TieBeamInfo> TieBeams { get; } = new List<TieBeamInfo>();

        /// <summary>Column axis data satır sırasıyla konumlar (tekil temel eşleştirmesi için; kolon listesinden bağımsız). Index 0 = 1. satır = ColumnRef 101.</summary>
        public List<ColumnAxisPositionEntry> ColumnAxisPositions { get; } = new List<ColumnAxisPositionEntry>();

        /// <summary>Bina taban kotu (m). Dosya 10. satır 1. sütun (örn. -3.31).</summary>
        public double BuildingBaseKotu { get; set; }
    }

    /// <summary>Column axis data tek satırı: aks kesişimi + kaçıklık (tekil temel merkezi için).</summary>
    public sealed class ColumnAxisPositionEntry
    {
        public int AxisXId { get; set; }
        public int AxisYId { get; set; }
        public int OffsetXRaw { get; set; }
        public int OffsetYRaw { get; set; }
        public double AngleDeg { get; set; }
    }

    /// <summary>Bağ kirişi kaydı (Tie beams bölümü): sabit aks üzerinde iki aks arası şerit, kaçıklık 6. sütun (mm). Çizimde katmana göre: TEMEL HATILI → radye temel temel hatılı; TEMEL → bağ kirişi.</summary>
    public sealed class TieBeamInfo
    {
        public string Name { get; set; }
        public int FixedAxisId { get; set; }
        public int StartAxisId { get; set; }
        public int EndAxisId { get; set; }
        public double WidthCm { get; set; }
        /// <summary>Kesit yüksekliği (cm); etiket yüksekliği = data yüksekliği (örn. radye kalınlığı) - HeightCm. 2. sütundan okunabilir.</summary>
        public double HeightCm { get; set; }
        /// <summary>6. sütun: kaçıklık (mm), kiriş kurallarıyla aynı.</summary>
        public int OffsetRaw { get; set; }
        /// <summary>7. sütun: radye temel hatılı alt kotu, bina tabanına göre (m). Örn: -.7 = taban altı 0.7 m.</summary>
        public double BottomKotM { get; set; }
    }

    public sealed class ContinuousFoundationInfo
    {
        public string Name { get; set; }
        /// <summary>Şeridin uzandığı eksen (kirişteki FixedAxis gibi).</summary>
        public int FixedAxisId { get; set; }
        public int StartAxisId { get; set; }
        public int EndAxisId { get; set; }
        /// <summary>Şerit genişliği (cm), dikdörtgen kesit.</summary>
        public double WidthCm { get; set; }
        /// <summary>2. satır 8. sütun: başlangıç uzatması (cm).</summary>
        public double StartExtensionCm { get; set; }
        /// <summary>2. satır 9. sütun: bitiş uzatması (cm).</summary>
        public double EndExtensionCm { get; set; }
        /// <summary>2. satır 10. sütun: dik kaçıklık (mm), kiriş kurallarıyla aynı.</summary>
        public int OffsetRaw { get; set; }
        /// <summary>2. satır 1. sütun: ampatman hizalama (0=ortada, 1=alt çizgi temel ile çakışık ampatman yukarı, 2=üst çizgi temel ile çakışık ampatman aşağı).</summary>
        public int AmpatmanAlign { get; set; }
        /// <summary>2. satır 4. sütun: ampatman genişliği (cm); 3. sütundan farklı ise çizilir.</summary>
        public double AmpatmanWidthCm { get; set; }
        /// <summary>2. satır 12. sütun: radye temel temel hatılı genişliği (cm); 0 ise çizilmez.</summary>
        public double TieBeamWidthCm { get; set; }
        /// <summary>2. satır 14. sütun: radye temel temel hatılı kaçıklığı (mm), X/Y aksına göre 0/±1/&gt;1/&lt;-1 kuralı.</summary>
        public int TieBeamOffsetRaw { get; set; }
    }

    public sealed class SlabFoundationInfo
    {
        public string Name { get; set; }
        public double ThicknessCm { get; set; }
        /// <summary>7. sütun (örn. 50) / 10 = hareketli yük kN/m².</summary>
        public double LiveLoadKNm2 { get; set; }
        /// <summary>İki X ekseni (1001..1999).</summary>
        public int AxisX1 { get; set; }
        public int AxisX2 { get; set; }
        /// <summary>İki Y ekseni (2001..2999).</summary>
        public int AxisY1 { get; set; }
        public int AxisY2 { get; set; }
    }

    public sealed class ColumnPositionFromFloors
    {
        public int AxisXId { get; set; }
        public int AxisYId { get; set; }
        public double OffsetXMm { get; set; }
        public double OffsetYMm { get; set; }
        public double AngleDeg { get; set; }
    }

    public sealed class SingleFootingInfo
    {
        public string Name { get; set; }
        public int ColumnRef { get; set; }
        public double SizeXCm { get; set; }
        public double SizeYCm { get; set; }
        public int AlignX { get; set; }
        public int AlignY { get; set; }
        public double HeightCm { get; set; }
        public double BottomLevelM { get; set; }
        public double AngleDeg { get; set; }
    }
}
