using ST4Yardimci;
using Autodesk.AutoCAD.ApplicationServices;
public partial class PerdeTest {
 [Autodesk.AutoCAD.Runtime.CommandMethod("PERDE_TEST")]
 public void Run(){
  var doc=Application.DocumentManager.MdiActiveDocument;var db=doc.Database;
  string root=@"C:\Users\BEYKENT\Documents\CURSOR\Osman_donusum\tools\PerdeBoslukSmoke\";
  var report=new System.Collections.Generic.List<string>();
  try{
   System.Collections.Generic.List<Autodesk.AutoCAD.DatabaseServices.ObjectId> holes=new System.Collections.Generic.List<Autodesk.AutoCAD.DatabaseServices.ObjectId>();
   using(var tr=db.TransactionManager.StartTransaction()){
    var bt=(Autodesk.AutoCAD.DatabaseServices.BlockTable)tr.GetObject(db.BlockTableId,Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
    var ms=(Autodesk.AutoCAD.DatabaseServices.BlockTableRecord)tr.GetObject(bt[Autodesk.AutoCAD.DatabaseServices.BlockTableRecord.ModelSpace],Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
    foreach(Autodesk.AutoCAD.DatabaseServices.ObjectId id in ms){var e=tr.GetObject(id,Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Polyline;if(e!=null&&e.Layer=="KALIP BOSLUK (BEYKENT)")holes.Add(id);}
   }
   if(PerdeBoslukRunner.Process(db,holes)!=1)throw new System.Exception("expected one wall");
   report.Add("PASS first run");
   db.SaveAs(root+"ACTUAL.dwg",Autodesk.AutoCAD.DatabaseServices.DwgVersion.Current);
   System.Environment.SetEnvironmentVariable("PERDE_DUMP_NAME","ACTUAL"); new Smoke().Dump(); System.Environment.SetEnvironmentVariable("PERDE_DUMP_NAME",null);
   int count=Count(db);
   if(PerdeBoslukRunner.Process(db,holes)!=1||Count(db)!=count)throw new System.Exception("idempotence failed");
   report.Add("PASS repeated run no duplicates");
   doc.Editor.Regen();report.Add("PASS regeneration");
   db.SaveAs(root+"REPEATED.dwg",Autodesk.AutoCAD.DatabaseServices.DwgVersion.Current);
   System.IO.File.WriteAllLines(root+"result.txt",report);
  }catch(System.Exception e){report.Add("FAIL "+e);System.IO.File.WriteAllLines(root+"result.txt",report);throw;}
 }
 static int Count(Autodesk.AutoCAD.DatabaseServices.Database db){using(var tr=db.TransactionManager.StartTransaction()){
 var bt=(Autodesk.AutoCAD.DatabaseServices.BlockTable)tr.GetObject(db.BlockTableId,Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
 var ms=(Autodesk.AutoCAD.DatabaseServices.BlockTableRecord)tr.GetObject(bt[Autodesk.AutoCAD.DatabaseServices.BlockTableRecord.ModelSpace],Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
 int n=0;foreach(Autodesk.AutoCAD.DatabaseServices.ObjectId id in ms)if(!id.IsErased)n++;return n;}}
}
