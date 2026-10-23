using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.ApplicationServices;
using ST4Yardimci;
public class MoreTests {
 const string Root=@"C:\Users\BEYKENT\Documents\CURSOR\Osman_donusum\tools\PerdeBoslukSmoke\";
 static List<ObjectId> Holes(Database db){using(var tr=db.TransactionManager.StartTransaction()){
 var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);var b=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
 return b.Cast<ObjectId>().Where(id=>tr.GetObject(id,OpenMode.ForRead) is Polyline p&&p.Layer=="KALIP BOSLUK (BEYKENT)").ToList();}}
 static ObjectId Add(Database db,double x,double y,double width,double height,bool closed=true){using(var tr=db.TransactionManager.StartTransaction()){
 var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);var b=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForWrite);
 var p=new Polyline{Layer="KALIP BOSLUK (BEYKENT)",Closed=closed};foreach(var q in new[]{new Point2d(x,y),new Point2d(x+width,y),new Point2d(x+width,y+height),new Point2d(x,y+height)})p.AddVertexAt(p.NumberOfVertices,q,0,0,0);
 var id=b.AppendEntity(p);tr.AddNewlyCreatedDBObject(p,true);tr.Commit();return id;}}
 static List<Entity> Read(Database db,Transaction tr){var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);var b=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);return b.Cast<ObjectId>().Where(id=>!id.IsErased).Select(id=>(Entity)tr.GetObject(id,OpenMode.ForRead)).ToList();}
 static int Count(Database db){using(var tr=db.TransactionManager.StartTransaction())return Read(db,tr).Count;}
 static void Check(bool ok,string text,List<string> report){if(!ok)throw new System.Exception(text);report.Add("PASS "+text);}
 [CommandMethod("PERDE_MULTI_TEST")]
 public void Multi(){var db=Application.DocumentManager.MdiActiveDocument.Database;var report=new List<string>();try{
 var ids=Holes(db);double x,y;using(var tr=db.TransactionManager.StartTransaction()){var b=PerdeBoslukRunner.Rectangle((Polyline)tr.GetObject(ids[0],OpenMode.ForRead));x=b.L;y=b.B;}
 Check(PerdeBoslukRunner.Process(db,ids)==1,"initial opening",report);
 ids.Add(Add(db,x+180,y,70,100));
 Check(PerdeBoslukRunner.Process(db,ids)==1,"two openings same wall",report);
 using(var tr=db.TransactionManager.StartTransaction()){
 var all=Read(db,tr);Check(all.OfType<DBText>().Count(t=>t.TextString=="2x4ø12 L=469")==1,"shared lintel bars not duplicated",report);
 Check(all.OfType<Line>().Count(e=>e.Layer=="ETRIYE (BEYKENT)")==17,"10+7 stirrups for two widths",report);
 }
 ids.Add(Add(db,x+435,y,100,100));
 Check(PerdeBoslukRunner.Process(db,ids)==2,"multiple selected walls",report);
 int n=Count(db);Check(PerdeBoslukRunner.Process(db,ids)==2&&Count(db)==n,"multiple wall rerun stable",report);
 var invalid=Add(db,x+20,y-50,40,40,false);n=Count(db);bool rejected=false;
 try{PerdeBoslukRunner.Process(db,new[]{ids[0],invalid});}catch(InvalidOperationException){rejected=true;}
 Check(rejected&&Count(db)==n,"invalid batch aborts without partial changes",report);
 Application.DocumentManager.MdiActiveDocument.Editor.Regen();report.Add("PASS multi regen");
 db.SaveAs(Root+"MULTI.dwg",DwgVersion.Current);File.WriteAllLines(Root+"multi-result.txt",report);
 }catch(System.Exception e){report.Add("FAIL "+e);File.WriteAllLines(Root+"multi-result.txt",report);throw;}}
 [CommandMethod("PERDE_REOPEN_TEST")]
 public void Reopen(){var db=Application.DocumentManager.MdiActiveDocument.Database;var report=new List<string>();try{
 var ids=Holes(db);int n=Count(db);Check(PerdeBoslukRunner.Process(db,ids)==1&&Count(db)==n,"saved DWG source template survives reopen",report);
 using(var tr=db.TransactionManager.StartTransaction()){
 var p=(Polyline)tr.GetObject(ids[0],OpenMode.ForWrite);for(int i=0;i<p.NumberOfVertices;i++){var q=p.GetPoint2dAt(i);if(i==1||i==2)p.SetPointAt(i,new Point2d(q.X+20,q.Y));}tr.Commit();}
 Check(PerdeBoslukRunner.Process(db,ids)==1,"changed opening regenerated",report);
 using(var tr=db.TransactionManager.StartTransaction())Check(Read(db,tr).OfType<DBText>().Any(t=>t.TextString=="2x6ø12/20 L=255"),"new opening width updates bar count",report);
 Application.DocumentManager.MdiActiveDocument.Editor.Regen();File.WriteAllLines(Root+"reopen-result.txt",report);
 }catch(System.Exception e){report.Add("FAIL "+e);File.WriteAllLines(Root+"reopen-result.txt",report);throw;}}
}
