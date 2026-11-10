namespace ST4AksCizCSharp
{
    public sealed class BeamInfo
    {
        public int BeamId { get; set; }
        public int FixedAxisId { get; set; }
        public int StartAxisId { get; set; }
        public int EndAxisId { get; set; }
        public double WidthCm { get; set; }
        public double HeightCm { get; set; }
        public int OffsetRaw { get; set; }
        /// <summary>1. noktanın kotu (cm), kat kotuna göre. Beams Data 12. sütun (p[11]).</summary>
        public double Point1KotCm { get; set; }
        /// <summary>2. noktanın kotu (cm), kat kotuna göre. Beams Data 13. sütun (p[12]).</summary>
        public double Point2KotCm { get; set; }
        public int IsWallFlag { get; set; }
        /// <summary>Guse yüksekliği Hg (cm). Beams Data 9. sütun (p[8]).</summary>
        public double GuseHgCm { get; set; }
        /// <summary>1. nokta (StartAxis) ucundaki guse boyu La (cm), mesnet yüzünden. Beams Data 10. sütun (p[9]).</summary>
        public double GuseLaCm { get; set; }
        /// <summary>2. nokta (EndAxis) ucundaki guse boyu Lb (cm), mesnet yüzünden. Beams Data 11. sütun (p[10]).</summary>
        public double GuseLbCm { get; set; }
    }
}
