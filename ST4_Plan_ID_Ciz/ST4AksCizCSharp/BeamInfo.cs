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
    }
}
