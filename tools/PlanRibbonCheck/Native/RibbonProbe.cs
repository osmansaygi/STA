using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;

public class RibbonProbe
{
    [CommandMethod("PLAN_RIBBON_CHECK")]
    public static void Check()
    {
        var file = Path.Combine(Environment.GetEnvironmentVariable("ST4_CURRENT_RESULT"), "native-ribbon.txt");
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz_2027"));
            var commands = assembly.GetType("ST4PlanIdCiz.BeykentRibbonCommands", true).GetMethods()
                .SelectMany(m => m.GetCustomAttributes<CommandMethodAttribute>()).Select(a => a.GlobalName).ToArray();
            if (!new[] { "BEYKENT", "BEYKENTKAPAT", "BEYKENTAUTO" }.All(c => commands.Contains(c)))
                throw new System.Exception("Ribbon command registration is incomplete");
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "AcWindows"))
                throw new System.Exception("Core Console startup unexpectedly loaded the native palette host");
            File.WriteAllText(file, "PASS 2027 ribbon command registration; no native palette host during Core Console startup\nCOMPLETE\n");
        }
        catch (System.Exception ex) { File.WriteAllText(file, "FAIL " + ex); }
    }
}
