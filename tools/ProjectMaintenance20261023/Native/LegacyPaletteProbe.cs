using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
public class LegacyPaletteProbe {
 [CommandMethod("LEGACY_PALETTE_CHECK")]
 public static void Check() {
  var file=Path.Combine(Environment.GetEnvironmentVariable("ST4_CURRENT_RESULT"),"legacy-palette.txt");
  var assembly=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name.StartsWith("ST4_Yardimci_2027"));
  var type=assembly.GetType("ST4Yardimci.BaHomeRibbonCatalog",true);
  var flags=BindingFlags.Static|BindingFlags.NonPublic;
  bool enabled=(bool)type.GetProperty("LegacyPaletteEnabled",flags).GetValue(null);
  bool nativeUiLoaded=AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="AcWindows");
  File.WriteAllText(file,(!enabled&&!nativeUiLoaded)?"PASS legacy panel disabled; three aliases did not load the native palette host\nCOMPLETE\n":"FAIL legacy palette enabled or native host loaded\n");
 }
}
