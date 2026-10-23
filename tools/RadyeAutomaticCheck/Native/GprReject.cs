using System;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
public class GprReject
{
 [CommandMethod("RADYE_GPR_REJECT")]
 public void Run()
 {
  string log=Environment.GetEnvironmentVariable("RADYE_RESULT");
  try {
   var asm=Assembly.LoadFrom(Environment.GetEnvironmentVariable("RADYE_PLUGIN"));
   var runner=asm.GetType("ST4Yardimci.RadyeOtomatikRunner",true).GetMethod("Draw",BindingFlags.Static|BindingFlags.NonPublic);
   foreach(string name in new[]{"gpr-missing","gpr-coordinate","gpr-peak"})using(var db=new Database(true,true)) {
    bool rejected=false;
    try {runner.Invoke(null,new object[]{db,Path.Combine(Environment.GetEnvironmentVariable("RADYE_FIXTURES"),name,"N_ULK_02.ST4"),Point3d.Origin,true,null});}
    catch(TargetInvocationException e)when(e.InnerException is InvalidDataException){rejected=true;File.AppendAllText(log,name+": "+e.InnerException.Message+Environment.NewLine);}
    using(var tr=db.TransactionManager.StartTransaction()) {
     var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
     int count=0;foreach(ObjectId id in ms)count++;
     if(!rejected || count!=0)throw new System.Exception("Invalid GPR modified target: "+name);
    }
   }
   string valid=Path.Combine(Environment.GetEnvironmentVariable("RADYE_FIXTURES"),"gpr-valid","N_ULK_02.ST4");
   var type=asm.GetType("ST4Yardimci.RadyeOtomatikSource",true);
   using(var source=(IDisposable)type.GetMethod("Open",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{valid})) {
    string p=Path.ChangeExtension(valid,".GPR");byte[] before=File.ReadAllBytes(p);bool rejected=false;
    try {
     File.AppendAllText(p,"\r\n");
     try {type.GetMethod("EnsureUnchanged",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(source,null);}
     catch(TargetInvocationException e)when(e.InnerException is InvalidDataException){rejected=true;}
    }finally{File.WriteAllBytes(p,before);}
    if(!rejected)throw new System.Exception("Changed input was not rejected");
   }
   File.AppendAllText(log,"REJECT_COMPLETE: three invalid sources rejected; targets unchanged; source-change hash guard passed"+Environment.NewLine);
  }catch(System.Exception e){File.AppendAllText(log,e.ToString()+Environment.NewLine);}
 }
}
