namespace ST4PlanIdCiz
{
    // Presentation only: command names point to the existing command entry points.
    internal static class BeykentRibbonCatalog
    {
        internal const string TabId = "BEYKENT_PLAN_TAB";
        internal sealed class Tool
        {
            internal readonly string Command, Label;
            internal Tool(string command, string label) { Command = command; Label = label; }
        }
        internal sealed class Group
        {
            internal readonly string Id, Title;
            internal readonly Tool[] Tools;
            internal Group(string id, string title, params Tool[] tools)
            { Id = "BEYKENT_PLAN_" + id; Title = title; Tools = tools; }
        }
        internal static readonly Group[] Groups =
        {
            new Group("TEMEL", "Temel",
                new Tool("TEMELST4", "Temel\nPlanı"),
                new Tool("SUREKLITEMELACILIMLARI", "Sürekli\nTemel"),
                new Tool("TEMELDONATI", "Radye\nDonatı"),
                new Tool("RADYEILAVEZARF", "Radye İlave\nZarf"),
                new Tool("RADYELEGANTKARSILASTIRMA", "Radye Legant\nKarşılaştırma"),
                new Tool("TEMELKIRISDUZELT", "Temel\nDüzelt")),
            new Group("KOLON", "Kolon",
                new Tool("KOLONST4", "Kolon\nAplikasyon"),
                new Tool("KOLONDUSEY", "Kolon\nDüşey"),
                new Tool("KOLONREVIZE", "Kolon\nRevize")),
            new Group("KIRIS", "Kiriş",
                new Tool("KIRISACILIMLARI", "Kiriş\nAçılımı"),
                new Tool("KIRISCIZ", "Kiriş\nÇiz"),
                new Tool("KIRISDUZELT", "Kiriş\nDüzelt")),
            new Group("YAPI", "Yapı",
                new Tool("ST4PLANID", "ST4 Plan\nID"),
                new Tool("KALIPST4", "Kalıp\nPlanı"),
                new Tool("MANTARDOSEME", "Mantar\nDöşeme"),
                new Tool("BINAGORUNUS", "Bina\nGörünüşü"),
                new Tool("BINAKESITI", "Bina\nKesiti"),
                new Tool("SERBESTKESIT", "Serbest\nKesit")),
            new Group("DETAY", "Detay / İskele",
                new Tool("KAPAMADETAY", "Kapama\nDetayı"),
                new Tool("ISKELECIZ", "İskele\nPlanı"),
                new Tool("ISKELEKESIT", "İskele\nKesiti")),
            new Group("METRAJ", "Metraj", new Tool("METRAJ", "Metraj")),
            new Group("DENEME", "Deneme",
                new Tool("DENEME1", "Kalıp\nDeneme"),
                new Tool("DENEME2", "Kalıp\nDeneme 2"))
        };

        internal static bool Contains(string command)
        {
            foreach (var group in Groups)
                foreach (var tool in group.Tools)
                    if (tool.Command == command) return true;
            return false;
        }
    }
}
