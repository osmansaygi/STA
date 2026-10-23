using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
public class Smoke {
 static string J(object x){
  if(x==null)return "null";
  if(x is string s)return "\""+s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n").Replace("\t","\\t")+"\"";
  if(x is bool b)return b?"true":"false";
  if(x is IDictionary d){var vs=new List<string>();foreach(DictionaryEntry e in d)vs.Add(J(e.Key)+":"+J(e.Value));return "{"+string.Join(",",vs)+"}";}
  if(x is IEnumerable a){var vs=new List<string>();foreach(var i in a)vs.Add(J(i));return "["+string.Join(",",vs)+"]";}
  return Convert.ToString(x,CultureInfo.InvariantCulture);
 }
 static double[] P(Point3d p)=>new[]{p.X,p.Y,p.Z};
 [CommandMethod("PERDE_DUMP")]
 public void Dump(){
  var db=Application.DocumentManager.MdiActiveDocument.Database; string name=Environment.GetEnvironmentVariable("PERDE_DUMP_NAME")??Path.GetFileNameWithoutExtension(db.Filename); {

   var list=new List<object>();
   using(var tr=db.TransactionManager.StartTransaction()){
    var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
    foreach(ObjectId id in ms){var e=tr.GetObject(id,OpenMode.ForRead) as Entity;if(e==null)continue;
     var d=new Dictionary<string,object>{{"id",e.Handle.ToString()},{"type",e.GetType().Name},{"layer",e.Layer},{"color",e.ColorIndex}};
     try{if(e is Dimension)throw new System.Exception();var b=e.GeometricExtents;d["bounds"]=new[]{b.MinPoint.X,b.MinPoint.Y,b.MaxPoint.X,b.MaxPoint.Y};}catch{}
     if(e is Polyline pl){d["closed"]=pl.Closed;d["pts"]=Enumerable.Range(0,pl.NumberOfVertices).Select(i=>new[]{pl.GetPoint2dAt(i).X,pl.GetPoint2dAt(i).Y,pl.GetBulgeAt(i)}).ToArray();}
     if(e is Line l){d["pts"]=new[]{P(l.StartPoint),P(l.EndPoint)};}
     if(e is DBText t){d["text"]=t.TextString;d["pos"]=P(t.Position);d["height"]=t.Height;d["rot"]=t.Rotation;}
     if(e is MText m){d["text"]=m.Contents;d["pos"]=P(m.Location);d["height"]=m.TextHeight;d["rot"]=m.Rotation;}
     if(e is Circle c){d["center"]=P(c.Center);d["r"]=c.Radius;}
     if(e is Arc a){d["center"]=P(a.Center);d["r"]=a.Radius;d["start"]=a.StartAngle;d["end"]=a.EndAngle;}
     if(e is Dimension dim){d["text"]=dim.DimensionText;}
     list.Add(d);
    }
   }
   var json=J(list);
   File.WriteAllText(@"C:\Users\BEYKENT\Documents\CURSOR\Osman_donusum\tools\PerdeBoslukSmoke\"+name+".json",json);
  }
 }
}
