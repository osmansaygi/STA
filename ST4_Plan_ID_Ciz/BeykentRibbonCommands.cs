#if BEYKENT_RIBBON
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(ST4PlanIdCiz.BeykentRibbonCommands))]
namespace ST4PlanIdCiz
{
    public class BeykentRibbonCommands
    {
        [CommandMethod("BEYKENT")]
        public void Show() { if (PlanLisans.EnsureOrWarn()) BeykentRibbonManager.Show(true); }

        [CommandMethod("BEYKENTKAPAT")]
        public void Hide() { BeykentRibbonManager.Hide(); }

        [CommandMethod("BEYKENTAUTO", CommandFlags.NoHistory)]
        public void InstallPending() { if (PlanLisans.IsAllowed()) BeykentRibbonManager.InstallPending(); }
    }
}
#endif
