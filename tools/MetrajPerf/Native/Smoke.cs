using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcApp=Autodesk.AutoCAD.ApplicationServices.Core.Application;
public class MetrajSmoke
{
 const BindingFlags Private=BindingFlags.Static|BindingFlags.NonPublic;
 static string result;
 static void Log(string s) {File.AppendAllText(result,s+Environment.NewLine);}
 [CommandMethod("METRAJ_FULL")]
 public void Full()
 {
  result=Environment.GetEnvironmentVariable("METRAJ_RESULT");
  try {
   var asm=Assembly.LoadFrom(Environment.GetEnvironmentVariable("METRAJ_PLUGIN"));
   var settings=asm.GetType("ST4Yardimci.DonatiMetrajSettings",true);
   settings.GetField("_loaded",Private).SetValue(null,true);
   var layers=(IList)settings.GetProperty("Katmanlar").GetValue(null);
   layers.Clear();layers.Add("DONATI YAZISI (BEYKENT)");
   var runner=asm.GetType("ST4Yardimci.DonatiMetrajRunner",true);
   var watch=Stopwatch.StartNew();
   runner.GetMethod("RunByAntet",BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
   Log("ANTET_FULL ms="+watch.ElapsedMilliseconds);
   Log("COMPLETE");
  } catch(System.Exception ex) {Log(ex.ToString());}
 }
 [CommandMethod("METRAJ_PERF")]
 public void Run()
 {
  result=Environment.GetEnvironmentVariable("METRAJ_RESULT");
  try {
   var asm=Assembly.LoadFrom(Environment.GetEnvironmentVariable("METRAJ_PLUGIN"));
   var runner=asm.GetType("ST4Yardimci.DonatiMetrajRunner",true);
   var settings=asm.GetType("ST4Yardimci.DonatiMetrajSettings",true);
   var hitType=runner.GetNestedType("Hit",BindingFlags.NonPublic);
   var pozType=runner.GetNestedType("Poz",BindingFlags.NonPublic);
   var hitList=typeof(System.Collections.Generic.List<>).MakeGenericType(hitType);
   var pozList=typeof(System.Collections.Generic.List<>).MakeGenericType(pozType);
   int count=int.TryParse(Environment.GetEnvironmentVariable("METRAJ_COUNT"),out int requested)?requested:1000;
   foreach(bool color in new[]{false,true}) {
    using(var db=new Database(true,true)) {
     var prev=HostApplicationServices.WorkingDatabase;
     HostApplicationServices.WorkingDatabase=db;
     try {
      var hits=(IList)Activator.CreateInstance(hitList);
      var pozlar=(IList)Activator.CreateInstance(pozList);
      for(int i=0;i<count;i++) {var p=Activator.CreateInstance(pozType,true);Set(p,"No",i+1);Set(p,"Cap",8);Set(p,"BoyCm",200+i);Set(p,"Adet",2);Set(p,"Kg",Math.Round(2*(200+i)/100.0*0.395,2,MidpointRounding.AwayFromZero));pozlar.Add(p);}
      using(var tr=db.TransactionManager.StartTransaction()) {
       var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
       var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForWrite);
       for(int i=0;i<20000;i++) {var ln=new Line(new Point3d(i*2,500,0),new Point3d(i*2+1,501,0));ms.AppendEntity(ln);tr.AddNewlyCreatedDBObject(ln,true);}
       for(int i=0;i<count;i++) {
        var text=new DBText{TextString="2ø8 L="+(200+i%Math.Min(10,count)),Height=5,Position=new Point3d(i%50*150,i/50*40,0)};
        ms.AppendEntity(text);tr.AddNewlyCreatedDBObject(text,true);
        var h=Activator.CreateInstance(hitType,true);Set(h,"Id",text.ObjectId);Set(h,"Cap",8);Set(h,"Boy",200+i%Math.Min(10,count));Set(h,"Adet",2);hits.Add(h);
       }
       tr.Commit();
      }
      settings.GetProperty("Renkli").SetValue(null,color);
      var timer=Stopwatch.StartNew();
      var path=(string)runner.GetMethod("EnsureTempDwg",Private).Invoke(null,null);
      using(var tr=db.TransactionManager.StartTransaction()) {
       var a=new object[]{tr,db,path,new Point3d(10000,0,0),pozlar,0.0,0.0,true,0.0,null,null};
       runner.GetMethod("InsertFilled",Private).Invoke(null,a);
       Log("TABLE color="+color+" ms="+timer.ElapsedMilliseconds+" fine="+a[5]+" coarse="+a[6]);
       tr.Commit();
      }
      timer.Restart();
      using(var tr=db.TransactionManager.StartTransaction()) {
       runner.GetMethod("StampLabelsFast",Private).Invoke(null,new object[]{tr,db,hits,pozlar});
       Log("FAST color="+color+" stamp_ms="+timer.ElapsedMilliseconds);
       timer.Restart();tr.Commit();Log("FAST commit_ms="+timer.ElapsedMilliseconds);
      }
      timer.Restart();
      using(var tr=db.TransactionManager.StartTransaction()) {
       runner.GetMethod("ClearOldMarks",Private).Invoke(null,new object[]{tr,db,hits});
       Log("CLEAR ms="+timer.ElapsedMilliseconds);tr.Commit();
      }
      timer.Restart();
      using(var tr=db.TransactionManager.StartTransaction()) {
       runner.GetMethod("StampLabels",Private).Invoke(null,new object[]{tr,db,hits,pozlar});
       Log("DIRECT color="+color+" stamp_ms="+timer.ElapsedMilliseconds);tr.Commit();
      }
      Snapshot(db,color);
     } finally {HostApplicationServices.WorkingDatabase=prev;}
    }
   }
   Log("COMPLETE");
  } catch(System.Exception ex) {Log(ex.ToString());}
 }
 static void Set(object target,string name,object value) {target.GetType().GetField(name).SetValue(target,value);}
 static string Number(double n) {return n.ToString("F6",System.Globalization.CultureInfo.InvariantCulture);}
 static string Point(Point3d p) {return Number(p.X)+","+Number(p.Y)+","+Number(p.Z);}
 static void Snapshot(Database db,bool color)
 {
  Log("SNAPSHOT_BEGIN color="+color);
  using(var tr=db.TransactionManager.StartTransaction()) {
   var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
   var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
   var order=(DrawOrderTable)tr.GetObject(ms.DrawOrderTableId,OpenMode.ForRead);
   var lines=new System.Collections.Generic.List<string>();
   var sorted=new System.Collections.Generic.List<string>();
   var fullOrder=order.GetFullDrawOrder(0);Log("SNAPSHOT_ORDER count="+fullOrder.Count);
   int visited=0;
   foreach(ObjectId id in fullOrder) {
    if(id.IsErased)continue;
    var e=tr.GetObject(id,OpenMode.ForRead) as Entity;if(e==null)continue;
    string s=e.GetType().Name+"|"+e.Layer+"|"+e.Color.ToString();
    if(e is DBText t)s+="|"+t.TextString+"|"+Point(t.Position)+"|"+Point(t.AlignmentPoint)+"|"+Number(t.Height)+"|"+Number(t.Rotation)+"|"+t.Justify;
    else if(e is Polyline p){for(int i=0;i<p.NumberOfVertices;i++)s+="|"+Point(p.GetPoint3dAt(i))+","+Number(p.GetBulgeAt(i));s+="|closed="+p.Closed;}
    else if(e is Hatch h)s+="|"+h.PatternName+"|loops="+h.NumberOfLoops+"|"+h.Associative;
    else if(e is Line l)s+="|"+Point(l.StartPoint)+"|"+Point(l.EndPoint);
    using(var xdata=e.GetXDataForApplication("ST4METRAJPOZ"))
     if(xdata!=null)foreach(TypedValue value in xdata)s+="|"+value.TypeCode+":"+value.Value;
    lines.Add(s);sorted.Add(s);
    if(++visited%5000==0)Log("SNAPSHOT_VISITED="+visited);
   }
   File.WriteAllLines(result+"."+color+".order.txt",lines);
   sorted.Sort(StringComparer.Ordinal);File.WriteAllLines(result+"."+color+".content.txt",sorted);
   Log("SNAPSHOT color="+color+" count="+lines.Count);
   tr.Commit();
  }
 }
}
