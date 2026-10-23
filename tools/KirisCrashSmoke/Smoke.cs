using System;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using ST4PlanIdCiz;
using ST4AksCizCSharp;
public class KirisSmoke {
 [CommandMethod("ST4_KIRIS_CRASH_SMOKE")]
 public void Run() {
  string output=Environment.GetEnvironmentVariable("ST4_KIRIS_TEST_OUTPUT");
  string source=Environment.GetEnvironmentVariable("ST4_KIRIS_TEST_SOURCE");
  File.WriteAllText(output,"START "+source+"\r\n");
  try {
   var asm=typeof(PlanIdDrawingManager).Assembly;
   asm.GetType("ST4PlanIdCiz.CommandEntry").GetMethod("ConfigureNtsNextGenOverlay",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
   var model=new St4Parser().Parse(source);
   var doc=Application.DocumentManager.MdiActiveDocument;
   for(int i=0;i<2;i++) {
    File.AppendAllText(output,"DRAW "+i+"\r\n");
    if(!new PlanIdDrawingManager(model).DrawKirisAcilimlari(doc.Database,doc.Editor,new Point3d(i*20000,0,0),source))throw new System.Exception("No drawing");
    doc.Editor.Regen();
    File.AppendAllText(output,"PASS draw/regen "+i+"\r\n");
   }
   using(var tr=doc.Database.TransactionManager.StartTransaction()){
    var bt=(BlockTable)tr.GetObject(doc.Database.BlockTableId,OpenMode.ForRead);
    var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
    int dims=0;
    foreach(ObjectId id in ms){var d=tr.GetObject(id,OpenMode.ForRead) as Dimension;if(d==null)continue;dims++;d.GeometricExtents.ToString();}
    File.AppendAllText(output,"PASS dimension extents "+dims+"\r\n");tr.Commit();
   }
   File.AppendAllText(output,"COMPLETE\r\n");
  }catch(System.Exception e){File.AppendAllText(output,"FAIL "+e);throw;}
 }
}
