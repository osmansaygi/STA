#if AUTOCAD2027
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(ST4PlanIdCiz.BeykentRibbonCommands))]
namespace ST4PlanIdCiz
{
    public class BeykentRibbonCommands
    {
        [CommandMethod("BEYKENT")]
        public void Show() { BeykentRibbonManager.Show(true); }

        [CommandMethod("BEYKENTKAPAT")]
        public void Hide() { BeykentRibbonManager.Hide(); }

        [CommandMethod("BEYKENTAUTO", CommandFlags.NoHistory)]
        public void InstallPending() { BeykentRibbonManager.InstallPending(); }
    }
}
#endif
