using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Linq;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

public class GprSmoke
{
 static object Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);
 [CommandMethod("RADYE_GPR_CHECK")]
 public void Run()
 {
  string log=Environment.GetEnvironmentVariable("RADYE_RESULT");
  try {
   var asm=Assembly.LoadFrom(Environment.GetEnvironmentVariable("RADYE_PLUGIN"));
   var commandMethods=asm.GetType("ST4Yardimci.CommandEntry",true).GetMethods().SelectMany(m=>m.GetCustomAttributesData()).Where(a=>a.AttributeType.Name=="CommandMethodAttribute").ToArray();
   foreach(string name in new[]{"RADYETEMELPLANIOTOMATIK","RADYEOTOMATIK"})
    if(!commandMethods.Any(a=>a.ConstructorArguments.Any(v=>v.Value as string==name)))throw new System.Exception("Command registration missing: "+name);
   using(var icon=(System.Drawing.Image)asm.GetType("ST4Yardimci.BaCommandIcons",true).GetMethod("Load",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{"RADYETEMELPLANIOTOMATIK","ST4Yardimci.Icons.radye_otomatik_monolit.png"}))
    if(icon==null || icon.Width<28)throw new System.Exception("Monolit icon missing");
   File.AppendAllText(log,"COMMANDS and MONOLIT ICON PASS"+Environment.NewLine);
   var sourceType=asm.GetType("ST4Yardimci.RadyeOtomatikSource",true);
   var heatType=asm.GetType("ST4Yardimci.RadyeRecordedHeatmap",true);
   string project=Environment.GetEnvironmentVariable("RADYE_PROJECT");
   using(var source=(IDisposable)sourceType.GetMethod("Open",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{project})) {
    var heat=heatType.GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{source});
    var values=(double[,])Field(heat,"As");var jobs=(IList)Field(heat,"Jobs");
    if(values.GetLength(0)!=733 || jobs.Count!=5)throw new System.Exception("Wrong As/jobs count");
    if(values[187,1]!=8.38 || values[159,1]!=7.77 || values[146,1]!=10.84 || values[28,3]!=9.15)throw new System.Exception("Wrong recorded As");
    File.AppendAllText(log,"AS nodes="+values.GetLength(0)+" jobs="+jobs.Count+Environment.NewLine);
    foreach(var job in jobs)File.AppendAllText(log,"JOB Y="+Field(job,"IsY")+" upper="+Field(job,"Upper")+" diameter="+Field(job,"Diameter")+" spacing="+Field(job,"Spacing")+" ring="+string.Join(";",((IEnumerable)Field(job,"Ring")).Cast<object>())+Environment.NewLine);
   }
   var settings=asm.GetType("ST4Yardimci.RadyeTemelDonatiSettings",true);settings.GetMethod("EnsureLoaded").Invoke(null,null);
   object height=settings.GetProperty("TemelYuksekligiCm").GetValue(null),upper=settings.GetProperty("IlaveUst").GetValue(null);
   using(var db=new Database(true,true)) {
    var previous=HostApplicationServices.WorkingDatabase;HostApplicationServices.WorkingDatabase=db;
    try {
     var args=new object[]{db,project,Point3d.Origin,true,null};
     asm.GetType("ST4Yardimci.RadyeOtomatikRunner",true).GetMethod("Draw",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
     int solids=0,bars=0,labels=0,referenceLabels=0;bool alt=false,ust=false;int[] channelCounts=new int[3];
     using(var tr=db.TransactionManager.StartTransaction()) {
      var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
      foreach(ObjectId id in ms) {
       var ent=tr.GetObject(id,OpenMode.ForRead) as Entity;if(ent==null)continue;
       if(ent is Solid) {solids++;if(ent.Layer.Contains("X ALT"))channelCounts[0]++;if(ent.Layer.Contains("Y UST"))channelCounts[1]++;if(ent.Layer.Contains("Y ALT"))channelCounts[2]++;}
       if(ent.Layer=="DONATI (BEYKENT)" && (ent is Line || ent is Polyline))bars++;
       if(ent is DBText t) {
        if(t.Layer=="DONATI YAZISI (BEYKENT)") {labels++;alt|=t.TextString.Contains("(alt)");ust|=t.TextString.Contains("(üst)");File.AppendAllText(log,"NEW BAR "+t.TextString+" @"+t.Position+Environment.NewLine);}
        if(t.Layer.Contains("PENC3") && t.TextString.Contains("/33"))referenceLabels++;
       }
       var ext=ent.GeometricExtents;if(double.IsNaN(ext.MinPoint.X))throw new System.Exception("Invalid extent");
      }
     }
     if(solids!=3143 || bars!=5 || labels!=5 || referenceLabels!=5 || !alt || !ust || channelCounts.Any(n=>n==0))throw new System.Exception("Incomplete output: solids="+solids+" bars="+bars+" labels="+labels+" ref="+referenceLabels+" alt="+alt+" ust="+ust);
     if(!height.Equals(settings.GetProperty("TemelYuksekligiCm").GetValue(null)) || !upper.Equals(settings.GetProperty("IlaveUst").GetValue(null)))throw new System.Exception("Interactive settings leaked");
     string dwg=Environment.GetEnvironmentVariable("RADYE_OUTPUT_DWG");db.SaveAs(dwg,DwgVersion.Current);db.DxfOut(Path.ChangeExtension(dwg,".dxf"),16,DwgVersion.Current);
     File.AppendAllText(log,"DRAW "+args[4]+" solids="+solids+" bars="+bars+" labels="+labels+" reference labels="+referenceLabels+" channel solids="+string.Join(",",channelCounts)+Environment.NewLine+"COMPLETE"+Environment.NewLine);
    }finally{HostApplicationServices.WorkingDatabase=previous;}
   }
  } catch(System.Exception ex){File.AppendAllText(log,ex.ToString()+Environment.NewLine);}
 }
}
