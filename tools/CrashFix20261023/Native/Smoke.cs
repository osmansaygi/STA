using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using ST4AksCizCSharp;
using ST4PlanIdCiz;
using NetTopologySuite.Geometries;
using AcApp=Autodesk.AutoCAD.ApplicationServices.Core.Application;

public class FoundationViewSmoke
{
 static string result;
 static bool hasRebarRecords;
 const BindingFlags InstancePrivate=BindingFlags.NonPublic|BindingFlags.Instance;
 static void Log(string s){File.AppendAllText(result,s+Environment.NewLine);}
 static void Check(bool ok,string s){if(!ok)throw new System.Exception(s);Log("PASS "+s);}
 // CK sayısal STA4CAD anlık görüntüsü (22.10 çözümü): kullanıcı projeyi yeniden çözünce değişir; hata değil uyarı.
 static void Snap(bool ok,string s){Log((ok?"PASS ":"UYARI eski STA cozumu: ")+s);}
 static object Field(object o,string n){return o.GetType().GetField(n).GetValue(o);}
 static IList Teams(PlanIdDrawingManager manager,FloorInfo floor){return (IList)manager.GetType().GetMethod("BuildSurekliGorunusTakimlari",InstancePrivate).Invoke(manager,new object[]{floor,true});}
 static ContinuousFoundationInfo Cf(string name,int y,int start,int end,double bottom=-80){return new ContinuousFoundationInfo{Name=name,FixedAxisId=y,StartAxisId=start,EndAxisId=end,WidthCm=120,HeightCm=80,BottomKotBinaGoreCm=bottom};}
 static St4Model Model(bool reverse=false,bool rotate=false,bool kacik=false)
 {
  var m=new St4Model{ColumnFloorKeyStep=100,BeamFloorKeyStep=1000};
  m.AxisX.AddRange(new[]{new AxisLine(1001,AxisKind.X,0,0),new AxisLine(1002,AxisKind.X,500,0),new AxisLine(1003,AxisKind.X,1000,0)});
  m.AxisY.AddRange(new[]{new AxisLine(2001,AxisKind.Y,0,0),new AxisLine(2002,AxisKind.Y,40,0),new AxisLine(2003,AxisKind.Y,1000,0)});
  m.Floors.Add(new FloorInfo(1,"ZEMİN","Z",0));
  m.Columns.Add(new ColumnAxisInfo{ColumnNo=1,ColumnType=1,AxisXId=1002,AxisYId=2001,OffsetYRaw=-500,ColumnId=101,AngleDeg=rotate?30:0});
  m.ColumnDimsBySectionId[101]=(40,60);
  var a=Cf("T1",2001,1001,1002);
  var b=Cf("T2",kacik?2002:2001,reverse?1003:1002,reverse?1002:1003);
  if(reverse)m.ContinuousFoundations.AddRange(new[]{b,a});else m.ContinuousFoundations.AddRange(new[]{a,b});
  return m;
 }
 static void TestGeometry()
 {
  foreach(bool reverse in new[]{false,true})foreach(bool rotate in new[]{false,true}) {
   var m=Model(reverse,rotate);var manager=new PlanIdDrawingManager(m);var teams=Teams(manager,m.Floors[0]);
   Check(teams.Count==1,"aligned reversed/rotated connected T1 T2 form one view");
   var ayri=(IList)manager.GetType().GetMethod("BuildSurekliGorunusTakimlari",InstancePrivate).Invoke(manager,new object[]{m.Floors[0],false});
   Check(ayri.Count==2,"form 'Ayri': aligned T1/T2 drawn as separate views");
   var off=Model(reverse,rotate,true);
   Check(Teams(new PlanIdDrawingManager(off),off.Floors[0]).Count==2,"laterally offset (40 cm) parallel T1/T2 are not aligned: separate views (CK T15/T16)");
   var cols=(IList)Field(teams[0],"Kolonlar");Check(cols.Count==1,"shared S1 drawn once");
   double center=((double)Field(cols[0],"X0")+(double)Field(cols[0],"X1"))*0.5;
   double expected=500-20*Math.Sin(rotate?Math.PI/6:0);
   Check(Math.Abs(center-expected)<0.01,"S1 station follows actual rotated column offset");
   if(!rotate)Check(Math.Abs((double)Field(cols[0],"X1")-(double)Field(cols[0],"X0")-40)<0.01,"S1 keeps actual projected width");
  }
  var apart=Model();apart.ContinuousFoundations[1].FixedAxisId=2003;
  Check(Teams(new PlanIdDrawingManager(apart),apart.Floors[0]).Count==2,"parallel distant foundations remain separate");
  var z=Model();z.ContinuousFoundations[1].BottomKotBinaGoreCm=120;
  Check(Teams(new PlanIdDrawingManager(z),z.Floors[0]).Count==2,"disconnected elevations remain separate");
  var tiers=Model();tiers.ContinuousFoundations[0].AmpatmanWidthCm=200;tiers.ContinuousFoundations[0].AmpatmanHeightCm=25;
  tiers.ContinuousFoundations[0].TieBeamWidthCm=50;tiers.ContinuousFoundations[0].HatilLabelHeightCm=40;
  var tierTeam=Teams(new PlanIdDrawingManager(tiers),tiers.Floors[0]);
  var tierCol=(IList)Field(tierTeam[0],"Kolonlar");Check(Math.Abs((double)Field(tierCol[0],"Alt"))<0.01,"S1 stub starts at foundation top; hatil is not foundation concrete");
  var stubs=(IList)Field(tierTeam[0],"Cikintilar");
  Check(stubs.Count==1&&!(bool)Field(stubs[0],"Perde")&&Math.Abs((double)Field(stubs[0],"Alt"))<0.01&&Math.Abs((double)Field(stubs[0],"Ust")-37.5)<0.01
   &&Math.Abs((double)Field(stubs[0],"X1")-(double)Field(stubs[0],"X0")-500)<0.01,"hatil drawn as 37.5 cm (1/25: 75 cm) stub over its own A-B length like a column stub");
  TestSaplama();
  TestBentAndEnds();
 }
 static void TestSaplama()
 {
  foreach(bool reverse in new[]{false,true}) {
   var m=Model();m.ContinuousFoundations.RemoveAll(f=>f.Name=="T2");
   m.AxisY.Add(new AxisLine(2004,AxisKind.Y,-300,0));
   var t1=m.ContinuousFoundations[0];if(reverse)(t1.StartAxisId,t1.EndAxisId)=(t1.EndAxisId,t1.StartAxisId);
   var t3=Cf("T3",1001,2003,2004);t3.WidthCm=70;m.ContinuousFoundations.Add(t3);
   var manager=new PlanIdDrawingManager(m);var teams=Teams(manager,m.Floors[0]);
   var team=teams.Cast<object>().Single(t=>((IList)Field(t,"Temeller")).Cast<object>().Any(f=>(int)Field(f,"No")==1));
   var saps=(IList)Field(team,"Saplamalar");
   Check(saps.Count==1&&(int)Field(saps[0],"No")==3,"T1 end without column bears on transverse T3: saplama support");
   Check(Math.Abs((double)Field(saps[0],"X1")-(double)Field(saps[0],"X0")-70)<0.01,"saplama support width = T3 width");
   var f1=((IList)Field(team,"Temeller")).Cast<object>().Single(f=>(int)Field(f,"No")==1);
   var plan=(Geometry)Field(((IList)Field(f1,"Parcalar"))[0],"Plan");
   Check(Math.Abs(plan.EnvelopeInternal.MinX+35)<0.01,"T1 concrete reaches T3 far face (x=-35)");
   var sup=(IList)manager.GetType().GetMethod("SurekliGorunusMesnetleri",InstancePrivate).Invoke(manager,new object[]{team,f1});
   Check(sup.Count==2,"T1 bay spans saplama support to S1 (one temel kirisi)");
  }
  var free=Model();free.Columns.Add(new ColumnAxisInfo{ColumnNo=2,ColumnType=1,AxisXId=1001,AxisYId=2001,ColumnId=102});free.ColumnDimsBySectionId[102]=(40,40);
  free.ContinuousFoundations[1].EndExtensionCm=150;
  var fm=new PlanIdDrawingManager(free);int bays=0;
  foreach(var team in Teams(fm,free.Floors[0]))foreach(var f in (IList)Field(team,"Temeller"))
   bays+=((IList)fm.GetType().GetMethod("SurekliGorunusMesnetleri",InstancePrivate).Invoke(fm,new object[]{team,f})).Count-1;
  Check(bays==2,"S2-S1 and S1-free end: T1 one bay, single-column T2 keeps fallback bay; got "+bays);
 }
 static St4Model BentModel(bool reverse=false)
 {
  var m=Model();m.Columns.Clear();m.ContinuousFoundations.Clear();
  m.AxisY.Clear();m.AxisY.AddRange(new[]{new AxisLine(2001,AxisKind.Y,0,0),new AxisLine(2002,AxisKind.Y,-150,0.3),
   new AxisLine(2003,AxisKind.Y,300,0),new AxisLine(2004,AxisKind.Y,-300,0)});
  for(int i=0;i<3;i++) {
   m.Columns.Add(new ColumnAxisInfo{ColumnNo=i+1,ColumnType=1,AxisXId=1001+i,AxisYId=i==2?2002:2001,ColumnId=101+i});
   m.ColumnDimsBySectionId[101+i]=i==0?(25,60):(40,40);
  }
  var t5=Cf("T5",2001,reverse?1002:1001,reverse?1001:1002);
  if(reverse)t5.EndExtensionCm=-4.5;else t5.StartExtensionCm=-4.5;
  var t6=Cf("T6",2002,reverse?1003:1002,reverse?1002:1003);
  var left=Cf("T27",1001,2003,2004);left.OffsetRaw=600;
  var right=Cf("T29",1003,2003,2004);
  foreach(var cf in new[]{t5,t6,left,right}) {cf.WidthCm=80;cf.HeightCm=60;cf.BottomKotBinaGoreCm=-60;}
  if(reverse)m.ContinuousFoundations.AddRange(new[]{right,left,t6,t5});else m.ContinuousFoundations.AddRange(new[]{t5,t6,left,right});
  return m;
 }
 static void TestBentAndEnds()
 {
  foreach(bool reverse in new[]{false,true}) {
   var m=BentModel(reverse);var manager=new PlanIdDrawingManager(m);var teams=Teams(manager,m.Floors[0]);
   var team=teams.Cast<object>().Single(t=>((IList)Field(t,"Temeller")).Cast<object>().Any(f=>(int)Field(f,"No")==5));
   var foundations=((IList)Field(team,"Temeller")).Cast<object>().ToList();
   Check(foundations.Count==2,"bent T5/T6 connect; transverse foundations keep separate views");
   var cols=((IList)Field(team,"Kolonlar")).Cast<object>().ToList();
   Check(cols.Count==3,"bent view has one label for shared S2");
   var first=cols.Single(c=>((ColumnAxisInfo)Field(c,"Kaynak")).ColumnNo==1);
   Check(Math.Abs((double)Field(first,"X1")-(double)Field(first,"X0")-25)<0.02,"T5 end column retains own-angle 25 cm width");
   foreach(var t in foundations) {
    var cf=(ContinuousFoundationInfo)Field(t,"Kaynak");
    var plan=(Geometry)Field(((IList)Field(t,"Parcalar"))[0],"Plan");
    var axis=new AxisGeometryService(m);axis.TryIntersect(cf.FixedAxisId,cf.StartAxisId,out Point2d a);axis.TryIntersect(cf.FixedAxisId,cf.EndAxisId,out Point2d b);
    Vector2d u=((Vector2d)Field(t,"GorunusYonu"));var origin=(Point2d)Field(t,"GorunusReferansi");double station=(double)Field(t,"Istasyon");
    if((int)Field(t,"No")==5) {
     Check(Math.Abs(plan.EnvelopeInternal.MinX+60)<0.02,"T5 wrong extension repaired to transverse foundation edge x=-60");
     Check(Math.Abs(plan.EnvelopeInternal.Height-80)<0.02,"T5 plan width remains 80 cm");
    } else {
     double max=plan.Coordinates.Max(c=>(c.X-a.X)*u.X+(c.Y-a.Y)*u.Y);
     double min=plan.Coordinates.Min(c=>(c.X-a.X)*u.X+(c.Y-a.Y)*u.Y);
     double expected=520*Math.Sqrt(1.09)+40*(1+0.3)/Math.Sqrt(1.09);
     Check(Math.Abs(max-min-expected)<0.03,"T6 own-direction length keeps transverse overhang beyond full column face");
     double width=plan.Coordinates.Max(c=>-c.X*u.Y+c.Y*u.X)-plan.Coordinates.Min(c=>-c.X*u.Y+c.Y*u.X);
     Check(Math.Abs(width-80)<0.03,"T6 transverse width remains 80 cm");
    }
    var sharedPlan=(Geometry)Field(cols.Single(c=>((ColumnAxisInfo)Field(c,"Kaynak")).ColumnNo==2),"Plan");
    var center=sharedPlan.Centroid.Coordinate;
    double s=station+(new Point2d(center.X,center.Y)-origin).DotProduct(u);
    var shared=cols.Single(c=>((ColumnAxisInfo)Field(c,"Kaynak")).ColumnNo==2);
    Check(Math.Abs(s-((double)Field(shared,"X0")+(double)Field(shared,"X1"))*0.5)<0.02,"each local direction maps shared column to same station");
   }
   Check(m.ContinuousFoundations.Single(f=>f.Name=="T5").StartExtensionCm==(reverse?0:-4.5),"source ST4 extension remains unchanged");
  }
  foreach(string mode in new[]{"no-transverse","different-Z","valid-long-end","fully-missing-column"}) {
   var m=BentModel();var left=m.ContinuousFoundations.Single(f=>f.Name=="T27");var t5=m.ContinuousFoundations.Single(f=>f.Name=="T5");
   if(mode=="no-transverse")m.ContinuousFoundations.Remove(left);
   if(mode=="different-Z")left.BottomKotBinaGoreCm=200;
   if(mode=="valid-long-end")t5.StartExtensionCm=100;
   if(mode=="fully-missing-column")t5.StartExtensionCm=-40;
   var f=Teams(new PlanIdDrawingManager(m),m.Floors[0]).Cast<object>().SelectMany(t=>((IList)Field(t,"Temeller")).Cast<object>()).Single(t=>(int)Field(t,"No")==5);
   var plan=(Geometry)Field(((IList)Field(f,"Parcalar"))[0],"Plan");
   double expected=mode=="valid-long-end"?-100:mode=="fully-missing-column"?-60:-12.5;
   Check(Math.Abs(plan.EnvelopeInternal.MinX-expected)<0.02,"end repair: "+mode);
  }
 }
 [CommandMethod("FOUNDATION_STYLES")]
 public void Styles()
 {
  result=Environment.GetEnvironmentVariable("ST4_FOUNDATION_RESULT");
  var db=AcApp.DocumentManager.MdiActiveDocument.Database;
  using(var tr=db.TransactionManager.StartTransaction()) {
   foreach(ObjectId id in (TextStyleTable)tr.GetObject(db.TextStyleTableId,OpenMode.ForRead)) {
    var s=(TextStyleTableRecord)tr.GetObject(id,OpenMode.ForRead);
    Log($"STYLE {s.Name} | file={s.FileName} | big={s.BigFontFileName} | font={s.Font.TypeFace} | h={s.TextSize}");
   }
   foreach(ObjectId id in (DimStyleTable)tr.GetObject(db.DimStyleTableId,OpenMode.ForRead)) {
    var s=(DimStyleTableRecord)tr.GetObject(id,OpenMode.ForRead);
    string txt=s.Dimtxsty.IsNull?"":((TextStyleTableRecord)tr.GetObject(s.Dimtxsty,OpenMode.ForRead)).Name;
    Log($"DIMSTYLE {s.Name} | text={txt}");
   }
   int mt=0;foreach(ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db),OpenMode.ForRead))if(id.ObjectClass.DxfName=="MTEXT")mt++;
   Log("MTEXT in model="+mt);
   tr.Commit();
  }
  string dxf=Environment.GetEnvironmentVariable("ST4_STYLES_DXF");
  if(!string.IsNullOrEmpty(dxf))db.DxfOut(dxf,16,DwgVersion.Current);
  Log("COMPLETE");
 }
 // MText yerleşimini (TextEditor → setFont → mapFont) Core Console'da zorlar.
 [CommandMethod("FONT_PROBE")]
 public void FontProbe()
 {
  result=Environment.GetEnvironmentVariable("ST4_FOUNDATION_RESULT");
  string face=Environment.GetEnvironmentVariable("ST4_FONT_FACE")??"Bahnschrift Light Condensed";
  var db=AcApp.DocumentManager.MdiActiveDocument.Database;
  try {
   using(var tr=db.TransactionManager.StartTransaction()) {
    var tt=(TextStyleTable)tr.GetObject(db.TextStyleTableId,OpenMode.ForWrite);
    var st=new TextStyleTableRecord{Name="PROBE"};
    st.Font=new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor(face,false,false,0,0);
    var sid=tt.Add(st);tr.AddNewlyCreatedDBObject(st,true);
    Log("style file="+st.FileName+" face="+st.Font.TypeFace);
    var ms=(BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db),OpenMode.ForWrite);
    double h=0;
    for(int i=0;i<400;i++) {
     var mt=new MText{Location=new Point3d(i*10,0,0),TextHeight=2.5,TextStyleId=sid,Contents=(i%2==0?"2ø20 L=345 ÜST İLAVE":"KAPAMA PERDE Ş Ğ ı")+i};
     ms.AppendEntity(mt);tr.AddNewlyCreatedDBObject(mt,true);h+=mt.ActualHeight;
     var dim=new RotatedDimension(0,new Point3d(i*10,0,0),new Point3d(i*10+7.3,0,0),new Point3d(i*10,-5,0),"",db.Dimstyle);
     ms.AppendEntity(dim);tr.AddNewlyCreatedDBObject(dim,true);dim.TextStyleId=sid;dim.RecomputeDimensionBlock(true);
     h+=dim.GeometricExtents.MaxPoint.Y;
    }
    tr.Commit();Log("probe height sum="+h);
   }
   Log("COMPLETE");
  }catch(System.Exception ex){Log(ex.ToString());}
 }
 // Yalnız çizim (testsiz): eski/yeni DLL kararlılık karşılaştırması için.
 [CommandMethod("FOUNDATION_DRAW_ONLY")]
 public void DrawOnly()
 {
  result=Environment.GetEnvironmentVariable("ST4_FOUNDATION_RESULT");
  try {
   var doc=AcApp.DocumentManager.MdiActiveDocument;
   string source=Environment.GetEnvironmentVariable("ST4_FOUNDATION_SOURCE");
   string face=Environment.GetEnvironmentVariable("ST4_PRE_STYLE_FONT");
   if(!string.IsNullOrEmpty(face)) {
    // Eklentinin stilleri yoksa oluşturur, varsa dokunmaz: önceden verilen font korunur.
    using(var tr=doc.Database.TransactionManager.StartTransaction()) {
     var tt=(TextStyleTable)tr.GetObject(doc.Database.TextStyleTableId,OpenMode.ForWrite);
     foreach(string n in new[]{"YAZI (BEYKENT)","ETRIYE_OLCU_YAZI"}) {
      if(tt.Has(n))continue;
      var st=new TextStyleTableRecord{Name=n};st.Font=new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor(face,false,false,0,0);
      tt.Add(st);tr.AddNewlyCreatedDBObject(st,true);
     }
     tr.Commit();
    }
    Log("prestyle="+face);
   }
   var manager=new PlanIdDrawingManager(new St4Parser().Parse(source));
   var optionType=typeof(PlanIdDrawingManager).Assembly.GetType("ST4PlanIdCiz.KirisAcilimSecenekleri");
   var options=Activator.CreateInstance(optionType,true);
   optionType.GetField("Antetli").SetValue(options,Environment.GetEnvironmentVariable("ST4_FOUNDATION_ANTET")=="1");
   var draw=typeof(PlanIdDrawingManager).GetMethod("DrawSurekliTemelAcilimlariSecenekli",InstancePrivate);
   Log("draw="+draw.Invoke(manager,new object[]{doc.Database,doc.Editor,Point3d.Origin,source,options}));
   Log("COMPLETE");
  }catch(System.Exception ex){Log(ex.ToString());}
 }
 [CommandMethod("FOUNDATION_VIEW_CHECK")]
 public void Run()
 {
  result=Environment.GetEnvironmentVariable("ST4_FOUNDATION_RESULT");
  hasRebarRecords=false;
  try {
   TestGeometry();
   TestFoundationSectionAndColumns();
   var doc=AcApp.DocumentManager.MdiActiveDocument;
   string source=Environment.GetEnvironmentVariable("ST4_FOUNDATION_SOURCE");
   var m=string.IsNullOrEmpty(source)?BentModel():new St4Parser().Parse(source);
   Check(m.ContinuousFoundations.Count>0,"source has continuous foundations");
   var manager=new PlanIdDrawingManager(m);
   if(!string.IsNullOrEmpty(source)&&Path.GetFileName(source).Equals("CK_20827_2_01.st4",StringComparison.OrdinalIgnoreCase))TestCkSource(m,source);
   if(!string.IsNullOrEmpty(source))TestRebarCoverage(m,source);
   if(!string.IsNullOrEmpty(source)&&Path.GetFileName(source).Equals("MS_OTP_01.st4",StringComparison.OrdinalIgnoreCase)) {
    TestActualAngledEnds(m);
    var reversed=new St4Parser().Parse(source);
    foreach(var cf in reversed.ContinuousFoundations.Where(cf=>cf.Name=="T42"||cf.Name=="T45")) {
     (cf.StartAxisId,cf.EndAxisId)=(cf.EndAxisId,cf.StartAxisId);
     (cf.StartExtensionCm,cf.EndExtensionCm)=(cf.EndExtensionCm,cf.StartExtensionCm);
     cf.OffsetRaw=-cf.OffsetRaw;
    }
    TestActualAngledEnds(reversed);Log("PASS real angled ends invariant under reversed source endpoints");
   }
   var geometry=new List<string>();
   foreach(var team in Teams(manager,m.Floors.OrderBy(f=>f.ElevationM).First())) {
    geometry.Add("TEAM");
    var support=new List<(double lo,double hi)>();
    foreach(var foundation in (IList)Field(team,"Temeller")) {
     var cf=(ContinuousFoundationInfo)Field(foundation,"Kaynak");
     var p=(Geometry)Field(((IList)Field(foundation,"Parcalar"))[0],"Plan");
     var u=(Vector2d)Field(foundation,"GorunusYonu");var a=(Point2d)Field(foundation,"A");
     var origin=(Point2d)Field(foundation,"GorunusReferansi");double s=(double)Field(foundation,"Istasyon");
     double lo=p.Coordinates.Min(c=>(c.X-a.X)*u.X+(c.Y-a.Y)*u.Y);
     double hi=p.Coordinates.Max(c=>(c.X-a.X)*u.X+(c.Y-a.Y)*u.Y);
     double width=p.Coordinates.Max(c=>-c.X*u.Y+c.Y*u.X)-p.Coordinates.Min(c=>-c.X*u.Y+c.Y*u.X);
     support.Add((lo+(a-origin).DotProduct(u)+s,hi+(a-origin).DotProduct(u)+s));
     Check(Math.Abs(width-cf.WidthCm)<0.05,"own-direction width preserved "+cf.Name);
     geometry.Add($"{cf.Name}: direction={u.X:F6},{u.Y:F6}; width={width:F3}; ownLength={hi-lo:F3}; extensionInput={cf.StartExtensionCm},{cf.EndExtensionCm}; offsetRaw={cf.OffsetRaw}; stationRef={s:F3}; origin={origin.X:F3},{origin.Y:F3}; A={Field(foundation,"A")}; B={Field(foundation,"B")}; plan={p.AsText()}");
    }
    foreach(var column in (IList)Field(team,"Kolonlar")) {
     var col=(ColumnAxisInfo)Field(column,"Kaynak");var axis=new AxisGeometryService(m);axis.TryIntersect(col.AxisXId,col.AxisYId,out Point2d node);
     geometry.Add($"S{col.ColumnNo}: station={Field(column,"X0")},{Field(column,"X1")}; top={Field(column,"Alt")}; axis={node}; plan={((Geometry)Field(column,"Plan")).AsText()}");
    }
    foreach(var col in (IList)Field(team,"Kolonlar")) {
     double cursor=(double)Field(col,"X0"),end=(double)Field(col,"X1");
     foreach(var span in support.OrderBy(s=>s.lo))if(span.lo<=cursor+0.05 && span.hi>cursor)cursor=span.hi;
     Check(cursor>=end-0.05,"column underside fully supported S"+((ColumnAxisInfo)Field(col,"Kaynak")).ColumnNo);
    }
   }
   File.WriteAllLines(result+".geometry.txt",geometry);
   var optionType=typeof(PlanIdDrawingManager).Assembly.GetType("ST4PlanIdCiz.KirisAcilimSecenekleri");
   var options=Activator.CreateInstance(optionType,true);
   bool antet=Environment.GetEnvironmentVariable("ST4_FOUNDATION_ANTET")=="1";
   optionType.GetField("Antetli").SetValue(options,antet);
   var draw=typeof(PlanIdDrawingManager).GetMethod("DrawSurekliTemelAcilimlariSecenekli",InstancePrivate);
   int expectedColumns=Teams(manager,m.Floors.OrderBy(f=>f.ElevationM).First()).Cast<object>().Sum(t=>((IList)Field(t,"Kolonlar")).Count);
   Check((bool)draw.Invoke(manager,new object[]{doc.Database,doc.Editor,Point3d.Origin,source??"",options}),"view drawing completes antet="+antet);
   using(var tr=doc.Database.TransactionManager.StartTransaction()) {
    var bt=(BlockTable)tr.GetObject(doc.Database.BlockTableId,OpenMode.ForRead);
    var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
    int reinforcement=0,stirrups=0,barLabels=0,colLabels=0,upperExtras=0,lowerExtras=0;var entities=new List<string>();
    foreach(ObjectId id in ms) {
     if(id.IsErased)continue;var e=tr.GetObject(id,OpenMode.ForRead) as Entity;if(e==null)continue;
     if(e.Layer=="DONATI (BEYKENT)")reinforcement++;
     if(e.Layer=="ETRIYE (BEYKENT)")stirrups++;
     if(e is DBText&&e.Layer=="DONATI YAZISI (BEYKENT)")barLabels++;
     if(e is DBText extra&&extra.Layer=="DONATI YAZISI (BEYKENT)") {if(extra.TextString.Contains("ÜST İLAVE"))upperExtras++;if(extra.TextString.Contains("ALT İLAVE"))lowerExtras++;}
     if(e is Line et&&et.Layer=="ETRIYE (BEYKENT)"&&Math.Abs(et.StartPoint.X-et.EndPoint.X)<0.001)
      Check(m.ContinuousFoundations.Any(f=>Math.Abs(et.Length-2*(f.HeightCm-10))<0.02),"elevation stirrup has 5 cm top/bottom cover");
     if(e is DBText text&&e.Layer=="KOLON ISMI (BEYKENT)")colLabels++;
     entities.Add(e.GetType().Name+"|"+e.Layer+(e is DBText tx?"|"+tx.TextString:""));
    }
    File.WriteAllLines(result+".entities.txt",entities);
    Log($"Rebar entities={reinforcement}, stirrups={stirrups}, labels={barLabels}");
    Log($"Upper extras={upperExtras}, lower extras={lowerExtras}");
    var filiz=entities.Where(s=>s.Contains("FİLİZ")).ToList();
    var filizEtiket=filiz.Where(s=>s.Contains("ø")).ToList();
    Log($"Column starter labels={filizEtiket.Count}, references={filiz.Count-filizEtiket.Count}");
    if(!string.IsNullOrEmpty(source)&&Path.GetFileName(source).Equals("CK_20827_2_01.st4",StringComparison.OrdinalIgnoreCase))
     Check(filizEtiket.Count>0&&filizEtiket.Count<=expectedColumns,"CK column starter bars labelled once per column (no wall/hatil starters)");
    if(!string.IsNullOrEmpty(source)&&Path.GetFileName(source).Equals("CK_20827_2_01.st4",StringComparison.OrdinalIgnoreCase))Check(upperExtras>0&&lowerExtras>0,"CK draws both upper and lower additional bars");
    if(hasRebarRecords)Check(reinforcement>0&&stirrups>0&&barLabels>0,"source reinforcement, stirrups and bar labels drawn");
    else Check(reinforcement==0&&stirrups==0,"no invented reinforcement without source records");
    Check(colLabels==expectedColumns,"one label per column identity per view");
    tr.Commit();
   }
   if(Environment.GetEnvironmentVariable("ST4_FOUNDATION_REPEAT")=="1") {
    var before=new Dictionary<ObjectId,string>();
    using(var tr=doc.Database.TransactionManager.StartTransaction()) {
     var bt=(BlockTable)tr.GetObject(doc.Database.BlockTableId,OpenMode.ForRead);var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
     foreach(ObjectId id in ms)if(!id.IsErased) {var e=tr.GetObject(id,OpenMode.ForRead) as Entity;string state=State(e);if(state!=null)before.Add(id,state);}tr.Commit();
    }
    Check((bool)draw.Invoke(manager,new object[]{doc.Database,doc.Editor,new Point3d(20000,0,0),source??"",options}),"second command completes in same drawing");
    using(var tr=doc.Database.TransactionManager.StartTransaction()) {Check(before.All(p=>State(tr.GetObject(p.Key,OpenMode.ForRead) as Entity)==p.Value),"second command preserves existing concrete, reinforcement and labels");tr.Commit();}
   }
   string outDwg=Environment.GetEnvironmentVariable("ST4_FOUNDATION_DWG");
   if(!string.IsNullOrEmpty(outDwg)){doc.Database.SaveAs(outDwg,DwgVersion.Current);doc.Database.DxfOut(Path.ChangeExtension(outDwg,".dxf"),16,DwgVersion.Current);}
   Log("COMPLETE");
  }catch(System.Exception ex){Log(ex.ToString());}
 }
 static void TestActualAngledEnds(St4Model m)
 {
  foreach(int no in new[]{42,45}) {
   var teams=Teams(new PlanIdDrawingManager(m),m.Floors.OrderBy(f=>f.ElevationM).First());
   var team=teams.Cast<object>().Single(t=>((IList)Field(t,"Temeller")).Cast<object>().Any(f=>(int)Field(f,"No")==no));
   var f=((IList)Field(team,"Temeller")).Cast<object>().Single(f=>(int)Field(f,"No")==no);
   var plan=(Geometry)Field(((IList)Field(f,"Parcalar"))[0],"Plan");
   var u=(Vector2d)Field(f,"GorunusYonu");var origin=(Point2d)Field(f,"GorunusReferansi");double station=(double)Field(f,"Istasyon");
   double lo=plan.Coordinates.Min(c=>(c.X-origin.X)*u.X+(c.Y-origin.Y)*u.Y)+station;
   double hi=plan.Coordinates.Max(c=>(c.X-origin.X)*u.X+(c.Y-origin.Y)*u.Y)+station;
   var cols=((IList)Field(team,"Kolonlar")).Cast<object>().OrderBy(c=>(double)Field(c,"X0")).ToList();
   double left=(double)Field(cols.First(),"X0")-lo,right=hi-(double)Field(cols.Last(),"X1");
   Log($"T{no} gap left={left:F6}, right={right:F6}; normal-projected={left*Math.Abs(u.Y):F6},{right*Math.Abs(u.Y):F6}");
   Check(Math.Abs(left*Math.Abs(u.Y)-20)<0.03&&Math.Abs(right*Math.Abs(u.Y)-20)<0.03,"T"+no+" both transverse overhangs stay 20 cm; angles converted to own view");
  }
 }
 static void TestRebarCoverage(St4Model m,string source)
 {
  var manager=new PlanIdDrawingManager(m);var type=manager.GetType();
  var args=new object[]{null};
  var beams=type.GetMethod("BuildSurekliTemelSyntheticBeams",InstancePrivate).Invoke(manager,args);
  type.GetField("_surekliTemelBeams",InstancePrivate).SetValue(manager,beams);
  type.GetField("_surekliTemelCfByBeam",InstancePrivate).SetValue(manager,args[0]);
  var data=type.GetMethod("SurekliTemelReadDonati",InstancePrivate).Invoke(manager,new object[]{source});
  var records=(IDictionary)data;var covered=new HashSet<int>();var audit=new List<string>();var t23Duz=new List<int>();var t6Alt=new List<string>();var t14Alt=new List<string>();var t14Duz=new List<int>();var supLines=new List<string>();
  if(records.Count==0){Log("PASS no source reinforcement records to assign");return;}
  foreach(var team in Teams(manager,m.Floors.OrderBy(f=>f.ElevationM).First())) {
   var chains=(IList)type.GetMethod("BuildSurekliGorunusDonatiChains",InstancePrivate).Invoke(manager,new object[]{team,data});
   foreach(IList chain in chains)foreach(var span in chain) {
    var beam=(BeamInfo)Field(span,"Beam");int no=beam.BeamId%m.BeamFloorKeyStep;
    var record=records[beam];
    if(record==null){audit.Add("T"+no+" NO SOURCE");continue;}
    Check(true,"bay has source assignment T"+no);
    audit.Add($"T{no} X={Field(span,"X0")}..{Field(span,"X1")}: mon={Field(record,"Adet")}x{Field(record,"Cap")}; duz={Field(record,"DuzAdet")}x{Field(record,"DuzCap")}; gov={Field(record,"GovdeAdet")}x{Field(record,"GovdeCap")}; ilaveSol=ust {Field(record,"SolUstAdet")}x{Field(record,"SolUstCap")} alt {Field(record,"SolAltAdet")}x{Field(record,"SolAltCap")}; ilaveSag=ust {Field(record,"SagUstAdet")}x{Field(record,"SagUstCap")} alt {Field(record,"SagAltAdet")}x{Field(record,"SagAltCap")}; etr={Field(record,"EtriyeCap")}/{Field(record,"EtriyeSikCm")}/{Field(record,"EtriyeOrtaCm")}");
    if((int)Field(record,"Adet")>0||(int)Field(record,"DuzAdet")>0){covered.Add(no);hasRebarRecords=true;}
    if(no==23)t23Duz.Add((int)Field(record,"DuzAdet"));
    if(no==14){if(t14Alt.Count==0)t14Alt.Add($"{Field(record,"SolAltAdet")}x{Field(record,"SolAltCap")}");t14Alt.Add($"{Field(record,"SagAltAdet")}x{Field(record,"SagAltCap")}");t14Duz.Add((int)Field(record,"DuzAdet"));}
    if(no==6){if(t6Alt.Count==0)t6Alt.Add($"{Field(record,"SolAltAdet")}x{Field(record,"SolAltCap")}");t6Alt.Add($"{Field(record,"SagAltAdet")}x{Field(record,"SagAltCap")}");}
    // Mesnet bazında ilave (STA4CAD karşılaştırması için kolon adıyla).
    foreach(var (st,pre) in new[]{((double)Field(span,"X0"),"Sol"),((double)Field(span,"X1"),"Sag")}) {
     var col=((IList)Field(team,"Kolonlar")).Cast<object>().FirstOrDefault(c=>Math.Abs(((double)Field(c,"X0")+(double)Field(c,"X1"))*0.5-st)<0.1);
     string ad=col==null?"SAPLAMA@"+st.ToString("0"):(string)type.GetMethod("KirisAcilimKolonAdi",InstancePrivate).Invoke(manager,new object[]{m.Floors.OrderBy(f=>f.ElevationM).First(),((ColumnAxisInfo)Field(col,"Kaynak")).ColumnNo});
     string satir=$"SUP T{no} {ad} alt={Field(record,pre+"AltAdet")}x{Field(record,pre+"AltCap")} ust={Field(record,pre+"UstAdet")}x{Field(record,pre+"UstCap")}";
     if(!supLines.Contains(satir))supLines.Add(satir);
    }
   }
  }
  File.WriteAllLines(result+".support.txt",supLines);
  File.WriteAllLines(result+".baydata.txt",audit);
  if(Path.GetFileName(source).Equals("MS_OTP_01.st4",StringComparison.OrdinalIgnoreCase))
   Check(covered.Count==58&&covered.Contains(54),"all 58 foundations including single-column T54 retain source reinforcement");
  if(Path.GetFileName(source).Equals("CK_20827_2_01.st4",StringComparison.OrdinalIgnoreCase))
  {
   Check(covered.Count==m.ContinuousFoundations.Count,"all CK foundations mapped to TMX via GPR/THE: "+covered.Count);
   Log("CK T23 duz per bay="+string.Join(",",t23Duz));
   var t14=string.Join(",",t14Alt);Log("CK T14 alt ilave per support="+t14+" duz per bay="+string.Join(",",t14Duz));
   Snap(t14=="1x14,5x20,20x20,16x20,13x20,18x20,21x20,13x20,10x20,11x20,4x20,1x14"||t14=="1x14,4x20,11x20,10x20,13x20,21x20,18x20,13x20,16x20,20x20,5x20,1x14","CK T14 supports match STA4CAD t14.dwg (SB-55 1x14 ... SB-66 1x14; node 83 without column skipped)");
   Snap(string.Join(",",t14Duz)=="8,11,14,13,11,15,15,11,13,11,8"||string.Join(",",t14Duz)=="8,11,13,11,15,15,11,13,14,11,8","CK T14 bay straight bars follow TMX records by GPR column names");
   var sup=supLines;Snap(sup.Any(s=>s.StartsWith("SUP T11 SB-40 alt=1x20"))&&sup.Any(s=>s.StartsWith("SUP T15 SB-77 alt=4x20")),"CK start-node extras (TMX 116/118): T11 SB-40 1x20, T15 SB-77 4x20 as in STA4CAD");
   var t6=string.Join(",",t6Alt);Log("CK T6 alt ilave per support="+t6);
   Snap(t6.Contains("2x12,5x20,19x20,13x20")||t6.Contains("13x20,19x20,5x20,2x12"),"CK T6 lower extras at supports SB-31 2x12, SB-32 5x20, SB-33 19x20, SB-34 13x20 (TMX ilave = record end support)");
  }
 }
 static string State(Entity e)
 {
  if(e is Line l)return l.StartPoint+"/"+l.EndPoint;
  if(e is Polyline p)return string.Join("/",Enumerable.Range(0,p.NumberOfVertices).Select(i=>p.GetPoint2dAt(i).ToString()));
  if(e is DBText t)return t.TextString+"/"+t.Position+"/"+t.AlignmentPoint+"/"+t.Height;
  return null;
 }
 static void TestFoundationSectionAndColumns()
 {
  var m=Model();m.Columns.Add(new ColumnAxisInfo{ColumnNo=2,ColumnType=1,ColumnId=202,AxisXId=1002,AxisYId=2001});m.ColumnDimsBySectionId[202]=(100,100);
  m.Columns.Add(new ColumnAxisInfo{ColumnNo=3,ColumnType=1,ColumnId=103,AxisXId=1002,AxisYId=2001});m.ColumnDimsBySectionId[103]=(100,100);m.ColumnKotMFromBinaTabaniBySectionId[103]=(4,1);
  var teams=Teams(new PlanIdDrawingManager(m),m.Floors[0]);
  Check(teams.Cast<object>().SelectMany(t=>((IList)Field(t,"Kolonlar")).Cast<object>()).All(c=>((ColumnAxisInfo)Field(c,"Kaynak")).ColumnNo==1),"upper-floor fallback and raised-bottom columns do not touch foundation");
  foreach(int align in new[]{0,1,2}) {
   var amp=Model();var cf=amp.ContinuousFoundations[0];cf.AmpatmanWidthCm=200;cf.AmpatmanHeightCm=25;cf.AmpatmanAlign=align;
   var manager=new PlanIdDrawingManager(amp);Teams(manager,amp.Floors[0]);
   var shape=(Geometry)manager.GetType().GetMethod("BuildSurekliTemelEnKesit",InstancePrivate).Invoke(manager,new object[]{cf});
   Check(Math.Abs(shape.Area-11600)<0.01&&Math.Abs(shape.EnvelopeInternal.Width-200)<0.01,"ampatman union area/width align="+align);
   var gf=shape.Factory;
   double low=shape.Intersection(gf.CreateLineString(new[]{new Coordinate(-500,-70),new Coordinate(500,-70)})).Length;
   double high=shape.Intersection(gf.CreateLineString(new[]{new Coordinate(-500,-10),new Coordinate(500,-10)})).Length;
   Check(Math.Abs(low-200)<0.01&&Math.Abs(high-120)<0.01,"ampatman 25 cm bottom step and 120 cm stem align="+align);
  }
  var type=typeof(PlanIdDrawingManager);const BindingFlags sp=BindingFlags.NonPublic|BindingFlags.Static;
  var boxes=(IList)type.GetMethod("KirisKesitEtriyeKutulari",sp).Invoke(null,new object[]{100d,5d});
  Check(Math.Abs((double)Field(boxes[0],"Item1")-5)<0.001&&Math.Abs((double)Field(boxes[boxes.Count-1],"Item2")-95)<0.001,"section stirrup 5 cm side cover");
  var rowType=type.GetNestedType("KirisKesitSira",BindingFlags.NonPublic);var rows=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType));
  foreach(string face in new[]{"mon","duz"}) {var row=Activator.CreateInstance(rowType);rowType.GetField("Adet").SetValue(row,4);rowType.GetField("Cap").SetValue(row,20);rowType.GetField("Tur").SetValue(row,face);rows.Add(row);}
  foreach(double cover in new[]{5d,6d}) {
   var layout=(IList)type.GetMethod("KirisKesitCubukYerlesimi",sp).Invoke(null,new object[]{100d,90d,rows,false,cover});
   Check(layout.Cast<object>().All(c=>{var p=(Point2d)Field(c,"Merkez");return p.X>=cover-0.001&&p.X<=100-cover+0.001&&(Math.Abs(p.Y+cover)<0.001||Math.Abs(p.Y+90-cover)<0.001);}),"bar section cover "+cover+" cm (beam default/foundation)");
  }
 }
 static void TestCkSource(St4Model m,string source)
 {
  const BindingFlags sp=BindingFlags.NonPublic|BindingFlags.Static;var type=typeof(PlanIdDrawingManager);
  var records=(IList)type.GetMethod("SurekliTemelReadTmx",sp).Invoke(null,new object[]{source});
  // Proje 22.10'da yeniden çözüldü (192 → 186 kayıt); sabit kayıt indeksleri yerine dosya boyu.
  long tmxLen=new FileInfo(Path.ChangeExtension(source,".TMX")).Length;
  Check(records.Count==tmxLen/250,"CK TMX intact 250-byte records: "+records.Count);
  var spans=(IList)type.GetMethod("SurekliTemelLoadSpanKayitlar",sp).Invoke(null,new object[]{source});
  Check(spans.Count==records.Count,"CK GPR statics/THE map every TMX record to a T number");
  foreach(var s in spans.Cast<object>().Where(s=>(int)Field(s,"TNo")==26||(int)Field(s,"TNo")==14))
   Log($"REC T{Field(s,"TNo")} sol={Field(s,"SolKolon")} sag={Field(s,"SagKolon")} N={Field(s,"N1")}-{Field(s,"N2")} stub={Field(s,"UcStub")}");
  var manager=new PlanIdDrawingManager(m);var teams=Teams(manager,m.Floors.OrderBy(f=>f.ElevationM).First());
  var t15=teams.Cast<object>().Single(t=>((IList)Field(t,"Temeller")).Cast<object>().Any(f=>(int)Field(f,"No")==15));
  Check(((IList)Field(t15,"Temeller")).Cast<object>().All(f=>(int)Field(f,"No")!=16),"CK T15/T16 laterally offset: separate views");
  foreach(var team in teams) {
   var names=string.Join(",",((IList)Field(team,"Temeller")).Cast<object>().Select(f=>"T"+Field(f,"No")));
   var saps=((IList)Field(team,"Saplamalar")).Cast<object>().Select(s=>$"T{Field(Field(s,"Temel"),"No")}->T{Field(s,"No")}@{(double)Field(s,"X0"):F0}..{(double)Field(s,"X1"):F0}");
   var stubs=((IList)Field(team,"Cikintilar")).Cast<object>().Select(c=>((bool)Field(c,"Perde")?"PERDE":"HATIL")+$"@{(double)Field(c,"X0"):F0}..{(double)Field(c,"X1"):F0}");
   Log($"CK view {names}: saplama=[{string.Join(" ",saps)}] cikinti=[{string.Join(" ",stubs)}]");
  }
  var t23=teams.Cast<object>().Single(t=>((IList)Field(t,"Temeller")).Cast<object>().Any(f=>(int)Field(f,"No")==23));
  var cols=((IList)Field(t23,"Kolonlar")).Cast<object>().Select(c=>((ColumnAxisInfo)Field(c,"Kaynak")).ColumnNo).ToList();
  // 22.10 yeniden çözümde kolon numaraları değişti; eski SB43/SB44 iddiası artık kayıt.
  Log("CK T23 columns="+string.Join(",",cols));
  int amps=0;foreach(var cf in m.ContinuousFoundations.Where(f=>f.AmpatmanWidthCm>f.WidthCm+0.1&&f.AmpatmanHeightCm>0.1)) {
   var shape=(Geometry)type.GetMethod("BuildSurekliTemelEnKesit",InstancePrivate).Invoke(manager,new object[]{cf});
   double expected=cf.WidthCm*cf.HeightCm+(cf.AmpatmanWidthCm-cf.WidthCm)*Math.Min(cf.HeightCm,cf.AmpatmanHeightCm);
   Check(Math.Abs(shape.Area-expected)<0.1&&Math.Abs(shape.EnvelopeInternal.Width-cf.AmpatmanWidthCm)<0.02,"CK ampatman section matches ST4 "+cf.Name);amps++;
  }Log("CK ampatman sections="+amps);
 }
}
