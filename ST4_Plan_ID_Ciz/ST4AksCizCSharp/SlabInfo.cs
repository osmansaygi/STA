namespace ST4AksCizCSharp
{
    /// <summary>
    /// Döşeme alanı: genellikle 4 aks (2 X + 2 Y), nadiren 3 aks ile sınırlanır.
    /// ST4 Floors Data satırından parse edilir: slabId, ..., axis1, axis2, axis3, axis4 (0-tabanlı indeks 8–11 = 1-tabanlı sütun 9–12).
    /// </summary>
    public sealed class SlabInfo
    {
        public int SlabId { get; set; }
        /// <summary>Döşeme kalınlığı (cm). Floors Data p[1].</summary>
        public double ThicknessCm { get; set; }
        /// <summary>Ölü yük toplamı (kN/m²), beton ağırlığı dahil. Floors Data 3. sütun p[2], *10 ile (örn. .57 → 5.7).</summary>
        public double DeadLoadKNm2 { get; set; }
        /// <summary>Hareketli yük (kN/m²). Floors Data 4. sütun p[3], *10 ile.</summary>
        public double LiveLoadKNm2 { get; set; }
        /// <summary>Döşeme üst kotunun kattan farkı (cm). Floors Data 16. sütun p[15]; negatif = kat kotundan aşağı.</summary>
        public double OffsetFromFloorCm { get; set; }
        /// <summary>Eksen 1 (X veya Y)</summary>
        public int Axis1 { get; set; }
        /// <summary>Eksen 2 (X veya Y)</summary>
        public int Axis2 { get; set; }
        /// <summary>Eksen 3 (X veya Y)</summary>
        public int Axis3 { get; set; }
        /// <summary>Eksen 4 (X veya Y); 3 akslı döşemede 0 olabilir.</summary>
        public int Axis4 { get; set; }
        /// <summary>Eğimli döşeme kodu, Floors Data p[14]. 0 = düz; 1..4 = alçak kenar üst/alt/sol/sağ (+Y/−Y/−X/+X);
        /// ≥10 = alçak yön açısı (p[14]−10)/10 derece (910 → 90°). Eğimde p[15] alçak kenar, p[5] yüksek kenar kotudur.</summary>
        public int SlopeCode { get; set; }
        /// <summary>Eğimli döşemede yüksek kenarın kattan farkı (cm), Floors Data p[5].</summary>
        public double SlopeHighKotCm { get; set; }
        /// <summary>Floors Data p[24]: 1 merdiven, 3 merdiven sahanlığı.</summary>
        public int StairKind { get; set; }
    }
}
