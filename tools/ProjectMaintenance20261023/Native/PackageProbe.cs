using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Autodesk.AutoCAD.Runtime;
public class PackageProbe {
 [CommandMethod("CURRENT_DLL_CHECK")]
 public static void Check() {
  var root=Path.GetFullPath(Environment.GetEnvironmentVariable("ST4_CURRENT_PACKAGE"));
  var result=Path.Combine(Environment.GetEnvironmentVariable("ST4_CURRENT_RESULT"),"package-probe.txt");
  File.WriteAllText(result, "BEGIN\n");
  try { foreach(var prefix in new[]{"ST4_Yardimci_2027","ST4_Plan_ID_Ciz_2027"}) {
   var list=AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
   if(list.Length!=1) throw new System.Exception("Expected one loaded module: "+prefix+" count="+list.Length);
   var a=list[0];
   if(!string.Equals(Path.GetDirectoryName(a.Location),root,StringComparison.OrdinalIgnoreCase)) throw new System.Exception("Wrong loaded path: "+a.Location);
   File.AppendAllText(result,"PASS loaded module "+a.Location+"\n");
  }
  var nts=Assembly.LoadFrom(Path.Combine(root,"NetTopologySuite.dll"));
  if(!string.Equals(Path.GetDirectoryName(nts.Location),root,StringComparison.OrdinalIgnoreCase)) throw new System.Exception("Wrong NTS path: "+nts.Location);
  File.AppendAllText(result,"PASS shared NTS "+nts.Location+"\nCOMPLETE\n");
  } catch(System.Exception ex) { File.AppendAllText(result,"FAIL "+ex+"\n"); }
 }
}
