using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using ST4PlanIdCiz;
using ST4AksCizCSharp;
public class Smoke {
 static void Check(bool ok,string message,string result) { if(!ok)throw new InvalidOperationException(message);File.AppendAllText(result,"PASS "+message+"\r\n"); }
 static List<Extents3d> Frames(Database db) {
  var frames=new List<Extents3d>();
  using(var tr=db.TransactionManager.StartTransaction()){
   var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
   var btr=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
   foreach(ObjectId id in btr){var p=tr.GetObject(id,OpenMode.ForRead) as Polyline;if(p!=null&&p.Layer=="SheetViewOut")frames.Add(p.GeometricExtents);}
  }
  return frames.OrderBy(e=>e.MinPoint.X).ToList();
 }
 [CommandMethod("ST4_APPLICATION_CRASH_SMOKE")]
 public void Run() {
  string output=Environment.GetEnvironmentVariable("ST4_SMOKE_OUTPUT");
  string source=Environment.GetEnvironmentVariable("ST4_SMOKE_SOURCE");
  string result=Path.Combine(output,"result.txt");
  try {
   File.WriteAllText(result,"START\r\n");
   var asm=typeof(PlanIdDrawingManager).Assembly;
   asm.GetType("ST4PlanIdCiz.CommandEntry")?.GetMethod("ConfigureNtsNextGenOverlay",BindingFlags.NonPublic|BindingFlags.Static)?.Invoke(null,null);
   var model=new St4Parser().Parse(source);
   File.AppendAllText(result,"PARSED: "+model.Floors.Count+" floors\r\n");
   var doc=Application.DocumentManager.MdiActiveDocument;
   new PlanIdDrawingManager(model).DrawColumnApplicationPlan(doc.Database,doc.Editor,Point3d.Origin,
    source,Environment.GetEnvironmentVariable("ST4_SMOKE_SCALE100")=="1" ? KolonApplicationPlanScale.Hundred : KolonApplicationPlanScale.Fifty,
    mergeSimilarFloors:true,drawInformationLabels:true,addVerticalDetails:true,addKapamaDetails:true);
   Check(true,"combined drawing completed",result);
   var frames=Frames(doc.Database);
   Check(frames.Count==13,"6 application + 1 kapama + 6 vertical outer frames",result);
   Check(Math.Abs(frames[0].MinPoint.Y-frames[1].MinPoint.Y)<0.01 && Math.Abs(frames[0].MinPoint.Y-frames[2].MinPoint.Y)<0.01,"first application / kapama / vertical bottom alignment",result);
   Check(frames[0].MaxPoint.X<frames[1].MinPoint.X && frames[1].MaxPoint.X<frames[2].MinPoint.X,"first application -> kapama -> first vertical ordering",result);
   for(int i=3;i<frames.Count;i+=2)Check(Math.Abs(frames[i].MinPoint.Y-frames[i+1].MinPoint.Y)<0.01,"remaining outer-frame bottom alignment "+i,result);
   using(var tr=doc.Database.TransactionManager.StartTransaction()){
    var bt=(BlockTable)tr.GetObject(doc.Database.BlockTableId,OpenMode.ForRead);
    var btr=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
    int info=0,rotatedNotes=0,kapamaTitles=0,kapamaExtraInfo=0;
    foreach(ObjectId id in btr){var text=tr.GetObject(id,OpenMode.ForRead) as DBText;if(text==null)continue;
     if(text.TextString.Contains("KAPAMA PERDE DETAYI")){kapamaTitles++;Check(text.Position.X>=frames[1].MinPoint.X&&text.Position.X<=frames[1].MaxPoint.X,"kapama title lies in middle sheet",result);}
     if(text.Layer!="YAZI (BEYKENT)")continue;
     if(text.TextString.Contains("etr.")&&text.Position.X>=frames[1].MinPoint.X&&text.Position.X<=frames[1].MaxPoint.X)kapamaExtraInfo++;
     if(text.TextString.Contains("etr."))info++;
     if(text.TextString.StartsWith("(not:")&&Math.Abs(text.Rotation)>0.001)rotatedNotes++;
    }
    Check(kapamaTitles==1,"kapama detail added exactly once",result);
    Check(kapamaExtraInfo==0,"application reinforcement labels do not leak into kapama",result);
    Check(info>0,"column reinforcement labels preserved",result);
    Check(rotatedNotes==0,"no vertical PB continuation notes",result);
   }
   doc.Editor.Regen();
   Check(true,"combined regeneration completed",result);
   foreach(bool scale25 in new[]{false,true}) {
    using(var tr=doc.Database.TransactionManager.StartTransaction()){
     var bt=(BlockTable)tr.GetObject(doc.Database.BlockTableId,OpenMode.ForRead);
     var btr=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForWrite);
     bool ok=new PlanIdDrawingManager(model).DrawKolonDuseyFromSt4(new Point3d(0,scale25?-30000:-15000,0),doc.Database,doc.Editor,tr,btr,source,
      olcek25:scale25,katKatCiz:true,benzerKatBirlestir:true);
     Check(ok,"standalone 1/"+(scale25?25:50)+" drawing completed",result);
     tr.Commit();
    }
    doc.Editor.Regen();
    Check(true,"standalone regeneration "+(scale25?25:50),result);
   }
   using(var tr=doc.Database.TransactionManager.StartTransaction()){
    var bt=(BlockTable)tr.GetObject(doc.Database.BlockTableId,OpenMode.ForRead);
    var btr=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForWrite);
    bool ok=new PlanIdDrawingManager(model).DrawKapamaDetayFromSt4(new Point3d(0,-50000,0),doc.Database,doc.Editor,tr,btr,source);
    Check(ok,"standalone KAPAMADETAY completed",result);
    tr.Commit();
   }
   doc.Editor.Regen();
   Check(true,"standalone KAPAMADETAY regeneration",result);
  }catch(System.Exception ex){File.AppendAllText(result,"FAIL "+ex);throw;}
 }
}