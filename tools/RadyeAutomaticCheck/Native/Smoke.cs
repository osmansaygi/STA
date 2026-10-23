using System;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

public class RadyeAutoSmoke
{
 [CommandMethod("RADYE_AUTO_CHECK")]
 public void Run()
 {
  string output=Environment.GetEnvironmentVariable("RADYE_RESULT");
  try {
   var asm=Assembly.LoadFrom(Environment.GetEnvironmentVariable("RADYE_PLUGIN"));
   var reader=asm.GetType("ST4Yardimci.RadyeOtomatikSource",true);
   using(var source=(IDisposable)reader.GetMethod("Open",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{Environment.GetEnvironmentVariable("RADYE_PROJECT")})) {
    foreach(string name in new[]{"Scale","PlateCount","NodeCount","ElementCount"})File.AppendAllText(output,name+"="+reader.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(source)+Environment.NewLine);
   }
   var runner=asm.GetType("ST4Yardimci.RadyeOtomatikRunner",true);
   using(var db=new Database(true,true)) {
    var previous=HostApplicationServices.WorkingDatabase;HostApplicationServices.WorkingDatabase=db;
    try {
     var args=new object[]{db,Environment.GetEnvironmentVariable("RADYE_PROJECT"),new Point3d(0,0,0),true,null};
     var count=runner.GetMethod("Draw",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
     File.AppendAllText(output,"DRAW count="+count+" "+args[4]+Environment.NewLine);
     int reference=0,automatic=0,bars=0;bool caution=false;
     using(var tr=db.TransactionManager.StartTransaction()) {
      var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
      foreach(ObjectId id in ms) {
       var e=tr.GetObject(id,OpenMode.ForRead) as Entity;if(e==null)continue;
       if(e is DBText text && text.TextString.Contains("henüz çözülmedi"))caution=true;
       using(var tag=e.GetXDataForApplication("RADYEOTOMATIK")) {
        if(tag==null)continue;string kind="";foreach(var value in tag)if(value.TypeCode==1000)kind=(string)value.Value;
        if(kind=="STA_REFERANS")reference++;
        if(kind=="STA_KAYITLI_ILAVE") {
         automatic++;
         if(e is DBText tx && tx.Layer=="DONATI YAZISI (BEYKENT)") {bars++;File.AppendAllText(output,"BAR "+tx.TextString+" rot="+tx.Rotation+Environment.NewLine);}
        }
        var ext=e.GeometricExtents;
        if(double.IsNaN(ext.MinPoint.X) || ext.MaxPoint.X>100000)throw new System.Exception("Invalid cm extents");
       }
      }
     }
     if(reference==0 || reference!=automatic || bars!=5 || !caution)throw new System.Exception("Wrong comparison/count/disclosure");
     var icons=asm.GetType("ST4Yardimci.BaCommandIcons",true);
     var load=icons.GetMethod("Load",BindingFlags.Static|BindingFlags.Public);
     using(var icon=(System.Drawing.Image)load.Invoke(null,new object[]{"RADYETEMELPLANIOTOMATIK","ST4Yardimci.Icons.ba_radyeilave.png"})) {
      if(icon==null)throw new System.Exception("No icon");
      File.AppendAllText(output,"ICON "+icon.Width+"x"+icon.Height+Environment.NewLine);
     }
     string dwg=Environment.GetEnvironmentVariable("RADYE_OUTPUT_DWG");
     db.SaveAs(dwg,DwgVersion.Current);db.DxfOut(Path.ChangeExtension(dwg,".dxf"),16,DwgVersion.Current);
     File.AppendAllText(output,"reference="+reference+" automatic="+automatic+" bars="+bars+Environment.NewLine+"COMPLETE"+Environment.NewLine);
    }finally{HostApplicationServices.WorkingDatabase=previous;}
   }
  }catch(System.Exception ex){File.AppendAllText(output,ex.ToString()+Environment.NewLine);}
 }
}
