namespace ST4AksCizCSharp
{
    /// <summary>
    /// Döşeme alanı: genellikle 4 aks (2 X + 2 Y), nadiren 3 aks ile sınırlanır.
    /// ST4 Floors Data satırından parse edilir: slabId, ..., axis1, axis2, axis3, axis4 (indeks 8,9,10,11).
    /// </summary>
    public sealed class SlabInfo
    {
        public int SlabId { get; set; }
        /// <summary>Eksen 1 (X veya Y)</summary>
        public int Axis1 { get; set; }
        /// <summary>Eksen 2 (X veya Y)</summary>
        public int Axis2 { get; set; }
        /// <summary>Eksen 3 (X veya Y)</summary>
        public int Axis3 { get; set; }
        /// <summary>Eksen 4 (X veya Y); 3 akslı döşemede 0 olabilir.</summary>
        public int Axis4 { get; set; }
    }
}
