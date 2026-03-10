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
        public int IsWallFlag { get; set; }
    }
}
