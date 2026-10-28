namespace ST4Yardimci
{
    internal static class BaHomeRibbonCatalog
    {
        // Arayüz sınıfına erişmeden okunur; Core Console'da AcWindows yüklenmemelidir.
        internal static bool LegacyPaletteEnabled
        {
            get
            {
#if BA_HOME_RIBBON
                return false;
#else
                return true;
#endif
            }
        }

        internal sealed class Group
        {
            internal readonly string Id;
            internal readonly string Title;
            internal readonly string[] Commands;
            internal Group(string id, string title, params string[] commands)
            {
                Id = "BEYKENT_YARDIMCI_HOME_" + id;
                Title = title;
                Commands = commands;
            }
        }

        internal static readonly Group[] Groups =
        {
            new Group("OLCU", "Ölçü", "OTOOLCU", "PLANOLCU"),
            new Group("DETAY", "Detay", "MERDIVEN", "TEMELHATILI", "PARAPET", "TEMELCEKURU",
                "TEMELDETAY", "KURANGLEZ", "PERDEBOSLUK", "SEHPADONATI"),
            new Group("DONATI", "Donatı", "DOSEMEDONATI", "OTODONATI", "HASIRDONATI", "RADYEILAVE"),
            new Group("GENEL", "Genel Düzenleme", "DONATIBOYU", "DONATIADET", "DONATIOKU",
                "KOTDEGISTIR", "DONATIMETRAJ", "BETONKALIP", "SEHIMTABLOSU", "KATMANYUKLE")
        };
    }
}
