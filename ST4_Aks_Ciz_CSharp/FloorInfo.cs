namespace ST4AksCizCSharp
{
    public sealed class FloorInfo
    {
        public FloorInfo(int floorNo, string name, string shortName, double elevationM)
        {
            FloorNo = floorNo;
            Name = name ?? string.Empty;
            ShortName = shortName ?? string.Empty;
            ElevationM = elevationM;
        }

        public int FloorNo { get; }
        public string Name { get; }
        public string ShortName { get; }
        public double ElevationM { get; }
    }
}
