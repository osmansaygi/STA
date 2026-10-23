using System;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
public class RadyeReject
{
 [CommandMethod("RADYE_REJECT_CHECK")]
 public void Run()
 {
  var result=Environment.GetEnvironmentVariable("RADYE_RESULT");
  try {
   var asm=Assembly.LoadFrom(Environment.GetEnvironmentVariable("RADYE_PLUGIN"));
   var runner=asm.GetType("ST4Yardimci.RadyeOtomatikRunner",true);
   foreach(string fixture in new[]{"mismatch","missing"})using(var db=new Database(true,true)) {
    bool rejected=false;
    try {runner.GetMethod("Draw",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{db,Path.Combine(Environment.GetEnvironmentVariable("RADYE_FIXTURES"),fixture,"N_ULK_02.ST4"),Point3d.Origin,true,null});}
    catch(TargetInvocationException ex)when(ex.InnerException is InvalidDataException){rejected=true;}
    using(var tr=db.TransactionManager.StartTransaction()) {
     var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);int count=0;foreach(ObjectId id in ms)count++;
     if(!rejected || count!=0)throw new System.Exception("Invalid data modified drawing: "+fixture);
    }
    File.AppendAllText(result,fixture+" rejected; empty target unchanged"+Environment.NewLine);
   }
   File.AppendAllText(result,"COMPLETE"+Environment.NewLine);
  }catch(System.Exception ex){File.AppendAllText(result,ex.ToString()+Environment.NewLine);}
 }
}
