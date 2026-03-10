namespace ST4AksCizCSharp
{
    public sealed class AxisLine
    {
        public AxisLine(int id, AxisKind kind, double valueCm, double slope)
        {
            Id = id;
            Kind = kind;
            ValueCm = valueCm;
            Slope = slope;
        }

        public int Id { get; }
        public AxisKind Kind { get; }
        public double ValueCm { get; }
        public double Slope { get; }
    }
}
