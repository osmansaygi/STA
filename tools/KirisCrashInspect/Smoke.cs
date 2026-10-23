using System;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;
using AcApp=Autodesk.AutoCAD.ApplicationServices.Core.Application;
public class BeamNativeSmoke
{
 [CommandMethod("ST4_BEAM_NATIVE_SMOKE")]
 public void Run()
 {
  string result=Environment.GetEnvironmentVariable("ST4_BEAM_SMOKE_RESULT");
  void Log(string message)=>File.AppendAllText(result,DateTime.Now.ToString("HH:mm:ss")+" "+message+Environment.NewLine);
  try
  {
   Log("START");
   string styleOnly=Environment.GetEnvironmentVariable("ST4_BEAM_SMOKE_STYLE_ONLY");
   if(!string.IsNullOrEmpty(styleOnly)){
    using(var inspect=new Database(false,true)){
     inspect.ReadDwgFile(styleOnly,FileOpenMode.OpenForReadAndAllShare,true,null);inspect.CloseInput(true);
     using(var tr=inspect.TransactionManager.StartTransaction()){
      var styles=(TextStyleTable)tr.GetObject(inspect.TextStyleTableId,OpenMode.ForRead);
      foreach(ObjectId id in styles){var st=(TextStyleTableRecord)tr.GetObject(id,OpenMode.ForRead);Log("STYLE "+st.Name+" FILE="+st.FileName+" BIG="+st.BigFontFileName+" FACE="+st.Font.TypeFace+" SHAPE="+st.IsShapeFile);}
      tr.Commit();
     }
    }
    Log("COMPLETE style inspection");return;
   }
   var asm=Assembly.LoadFrom(Environment.GetEnvironmentVariable("ST4_BEAM_SMOKE_PLUGIN"));
   asm.GetType("ST4PlanIdCiz.CommandEntry").GetMethod("ConfigureNtsNextGenOverlay",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
   var parser=Activator.CreateInstance(asm.GetType("ST4AksCizCSharp.St4Parser"));
   string source=Environment.GetEnvironmentVariable("ST4_BEAM_SMOKE_SOURCE");
   var model=parser.GetType().GetMethod("Parse").Invoke(parser,new object[]{source});
   var managerType=asm.GetType("ST4PlanIdCiz.PlanIdDrawingManager");
   var doc=AcApp.DocumentManager.MdiActiveDocument;
   string formImage=Environment.GetEnvironmentVariable("ST4_BEAM_SMOKE_FORM");
   if(!string.IsNullOrEmpty(formImage)){
    var formType=asm.GetType("ST4PlanIdCiz.KirisAcilimForm");
    using(var form=(System.Windows.Forms.Form)Activator.CreateInstance(formType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{model},null)){
     form.CreateControl();
     using(var bitmap=new System.Drawing.Bitmap(form.Width,form.Height)){
      form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height));bitmap.Save(formImage);
     }
     var groups=new System.Collections.Generic.List<System.Windows.Forms.Control>();
     void Collect(System.Windows.Forms.Control c){groups.Add(c);foreach(System.Windows.Forms.Control child in c.Controls)Collect(child);}
     Collect(form);
     var selected=groups.Find(c=>c.Text=="Seçili kat") as System.Windows.Forms.RadioButton;
     var combo=groups.Find(c=>c is System.Windows.Forms.ComboBox) as System.Windows.Forms.ComboBox;
     if(selected==null||combo==null||combo.Items.Count==0)throw new System.Exception("Form floor choices missing");
     selected.Checked=true;if(!combo.Enabled)throw new System.Exception("Selected floor dropdown disabled");
     var button=groups.Find(c=>c.Text=="Çiz") as System.Windows.Forms.Button;
     typeof(System.Windows.Forms.Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(button,new object[]{EventArgs.Empty});
     var options=formType.GetProperty("Secenekler").GetValue(form);
     if(options==null||options.GetType().GetField("SeciliKatNo").GetValue(options)==null)throw new System.Exception("Form failed to return floor selection");
     Log("PASS form options, floor dropdown and draw button");
    }
   }
   int runCount=int.TryParse(Environment.GetEnvironmentVariable("ST4_BEAM_SMOKE_RUNS"),out int requestedRuns)?Math.Max(1,requestedRuns):2;
   for(int run=0;run<runCount;run++)
   {
    Log("DRAW "+run);
    var manager=Activator.CreateInstance(managerType,new object[]{model});
    object ok;
    string mode=Environment.GetEnvironmentVariable("ST4_BEAM_SMOKE_LAYOUT");
    if(!string.IsNullOrEmpty(mode)){
     var optionType=asm.GetType("ST4PlanIdCiz.KirisAcilimSecenekleri");
     var options=Activator.CreateInstance(optionType,true);
     optionType.GetField("Antetli").SetValue(options,mode=="antet");
     string floorText=Environment.GetEnvironmentVariable("ST4_BEAM_SMOKE_FLOOR");
     if(!string.IsNullOrEmpty(floorText)) optionType.GetField("SeciliKatNo").SetValue(options,int.Parse(floorText));
     Log("OPTIONS "+mode+" floor="+(floorText??"ALL"));
     ok=managerType.GetMethod("DrawKirisAcilimlariSecenekli",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(manager,new object[]{doc.Database,doc.Editor,new Point3d(run*20000,0,0),source,options});
    }else ok=managerType.GetMethod("DrawKirisAcilimlari").Invoke(manager,new object[]{doc.Database,doc.Editor,new Point3d(run*20000,0,0),source});
    if(!(bool)ok)throw new System.Exception("Empty drawing");
    var numericView=managerType.GetMethod("KirisAcilimGorunumeAl",BindingFlags.Instance|BindingFlags.NonPublic);
    if(numericView!=null){Log("NUMERIC_VIEW "+run);numericView.Invoke(manager,new object[]{doc.Editor});}
    Log("UPDATE_EXT "+run);doc.Database.UpdateExt(true);
    Log("REGEN "+run);doc.Editor.Regen();
    Log("ENTITY_EXTENTS "+run);
    using(var tr=doc.Database.TransactionManager.StartTransaction())
    {
     var bt=(BlockTable)tr.GetObject(doc.Database.BlockTableId,OpenMode.ForRead);
     var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
     int count=0,barLabels=0,offGrid=0;
     foreach(ObjectId id in ms){
      var ent=tr.GetObject(id,OpenMode.ForRead) as Entity;if(ent==null)continue;ent.GeometricExtents.ToString();count++;
      if(ent is DBText label){
       var match=System.Text.RegularExpressions.Regex.Match(label.TextString,@"L=(\d+) (MONTAJ|ALT|GÖVDE|ÜST İLAVE)");
       if(match.Success){barLabels++;if(int.Parse(match.Groups[1].Value)%5!=0){offGrid++;Log("OFF_GRID "+label.TextString);}}
      }
     }
     Log("BAR_LABELS "+barLabels+" OFF_GRID "+offGrid);
     Log("PASS entity extents "+count);tr.Commit();
    }
   }
   Log("MOVE_COPY");
   using(var tr=doc.Database.TransactionManager.StartTransaction())
   {
    var bt=(BlockTable)tr.GetObject(doc.Database.BlockTableId,OpenMode.ForRead);
    var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForWrite);
    var ids=new System.Collections.Generic.List<ObjectId>();
    foreach(ObjectId id in ms)ids.Add(id);
    foreach(var id in ids)
    {
     var ent=tr.GetObject(id,OpenMode.ForWrite) as Entity;if(ent==null)continue;
     ent.TransformBy(Matrix3d.Displacement(new Vector3d(100,100,0)));
     var copy=(Entity)ent.Clone();
     try{copy.TransformBy(Matrix3d.Displacement(new Vector3d(40000,0,0)));ms.AppendEntity(copy);tr.AddNewlyCreatedDBObject(copy,true);}
     catch{if(copy.ObjectId.IsNull)copy.Dispose();throw;}
    }
    tr.Commit();
   }
   doc.Database.UpdateExt(true);doc.Editor.Regen();
   string output=Environment.GetEnvironmentVariable("ST4_BEAM_SMOKE_DWG");
   if(!string.IsNullOrEmpty(output)){Log("SAVE");doc.Database.SaveAs(output,DwgVersion.Current);}
   Log("COMPLETE");
  }
  catch(System.Exception ex){Log("FAIL "+ex);throw;}
 }
}
