namespace ST4PlanIdCiz
{
    /// <summary>
    /// ISKELECIZ bu AutoCAD oturumunda basariyla tamamlandi mi (ISKELEKESIT icin onkosul).
    /// Cizim dosyasi kaydedilip acildiginda sifirlanir; gerekirse ISKELECIZ tekrar calistirilir.
    /// </summary>
    internal static class IskeleCizContextStore
    {
        public static bool IsActive { get; private set; }

        public static void SetActive() => IsActive = true;

        public static void Clear() => IsActive = false;
    }
}
