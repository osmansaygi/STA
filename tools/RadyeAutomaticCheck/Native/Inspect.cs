using System;
using System.IO;
using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
public class RadyeInspect
{
 [CommandMethod("RADYE_INSPECT")]
 public void Run()
 {
  string output=Environment.GetEnvironmentVariable("RADYE_RESULT");
  try {
   using(var db=new Database(false,true)) {
    db.ReadDwgFile(Environment.GetEnvironmentVariable("RADYE_DWG"),FileOpenMode.OpenForReadAndAllShare,false,null);db.CloseInput(true);
    using(var tr=db.TransactionManager.StartTransaction()) {
     var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
     var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
     var counts=new Dictionary<string,int>();int texts=0;
     foreach(ObjectId id in ms) {
      var e=tr.GetObject(id,OpenMode.ForRead) as Entity;if(e==null)continue;
      string key=e.Layer+"|"+e.GetType().Name;counts[key]=counts.ContainsKey(key)?counts[key]+1:1;
      string s=e is DBText t?t.TextString:e is MText m?m.Contents:null;
      if(s!=null && texts++<5000) File.AppendAllText(output,"TEXT "+e.Layer+" | "+s+Environment.NewLine);
      if(e.Layer=="PENC2" || e.Layer=="PENC3" || e.Layer=="REBAR") {
       if(e is Line line)File.AppendAllText(output,"LINE "+e.Layer+" | "+line.StartPoint+" -> "+line.EndPoint+Environment.NewLine);
       if(e is DBText text)File.AppendAllText(output,"POSE "+text.TextString+" | "+text.Position+" rot="+text.Rotation+Environment.NewLine);
      }
      if(e is Polyline rp && e.Layer=="REBAR") {
       for(int j=0;j<rp.NumberOfVertices;j++)File.AppendAllText(output,"REBAR_POINT "+rp.GetPoint2dAt(j)+" b="+rp.GetBulgeAt(j)+Environment.NewLine);
      }
      if(e is Polyline p && p.Closed && e.Layer.IndexOf("ILAVE",StringComparison.OrdinalIgnoreCase)>=0) {
       var points=new List<string>();for(int i=0;i<p.NumberOfVertices;i++){var v=p.GetPoint2dAt(i);points.Add(v.X.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+v.Y.ToString("R",System.Globalization.CultureInfo.InvariantCulture));}
       File.AppendAllText(output,"POLY "+e.Layer+" | "+string.Join(";",points)+Environment.NewLine);
      }
     }
     foreach(var pair in counts)File.AppendAllText(output,"COUNT "+pair.Key+" = "+pair.Value+Environment.NewLine);
    }
   }
   File.AppendAllText(output,"COMPLETE"+Environment.NewLine);
  }catch(System.Exception ex){File.AppendAllText(output,ex.ToString());}
 }
}
