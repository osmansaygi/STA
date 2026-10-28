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
                new Tool("TEMEL50ST4", "Temel\n1/50"),
                new Tool("TEMEL100ST4", "Temel\n1/100"),
                new Tool("SUREKLITEMELACILIMLARI", "Sürekli\nTemel"),
                new Tool("TEMELDONATI", "Radye\nDonatı"),
                new Tool("TEMELKIRISDUZELT", "Temel\nDüzelt")),
            new Group("KOLON", "Kolon",
                new Tool("KOLON50ST4", "Kolon\n1/50"),
                new Tool("KOLON100ST4", "Kolon\n1/100"),
                new Tool("KOLONDUSEY", "Kolon\nDüşey"),
                new Tool("KOLONREVIZE", "Kolon\nRevize")),
            new Group("KIRIS", "Kiriş",
                new Tool("KIRISACILIMLARI", "Kiriş\nAçılımı"),
                new Tool("KIRISDUZELT", "Kiriş\nDüzelt")),
            new Group("YAPI", "Yapı",
                new Tool("ST4PLANID", "ST4 Plan\nID"),
                new Tool("KALIP50ST4", "Kalıp\n1/50"),
                new Tool("KALIP100ST4", "Kalıp\n1/100"),
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
