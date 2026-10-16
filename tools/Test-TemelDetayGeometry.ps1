# Execute production geometry methods without loading AutoCAD.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$main = [IO.File]::ReadAllText((Join-Path $root 'ST4_Yardimci/TemelCekuruRunner.cs'))
$detail = [IO.File]::ReadAllText((Join-Path $root 'ST4_Yardimci/TemelCekuruRunner.Detay.cs'))
$cut = [IO.File]::ReadAllText((Join-Path $root 'ST4_Yardimci/TemelCekuruRunner.DetayCut.cs'))
function Extract-Method([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "Method missing: $signature" }
    $open = $source.IndexOf('{', $start)
    $depth = 1
    $end = $open + 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    return $source.Substring($start, $end - $start)
}
$constants = ([regex]::Matches($main, 'private const double \w+ = [^;]+;') | ForEach-Object Value) -join "`n"
$fields = @'
public double Dy1,Dy2,Dy3,Dy4,DRun,DW,DSide,Dy2L,Dy2R,Dy3L,Dy3R,DRunL,DRunR,StemSolCm,StemSagCm;
public bool SplitHeight,DetayBaslik,GeometryChanged;
public int StraightCm,LVert,YatayDuzCm,UstAdet,AltAdet,DisAdet,IcAdet,KarsiUstAdet,KarsiAltAdet,YanAdet,KesitNo,Carpan;
private double DetailDy1L,DetailDy1R;
private bool DetailLeft,DetailRight;
private bool DetailEnabled(bool right) => right ? DetailRight : DetailLeft;
'@
$methods = @(
    (Extract-Method $main 'public static Fit FromSettings('),
    (Extract-Method $main 'private static double Lerp('),
    (Extract-Method $detail 'private void ConfigureDetail('),
    (Extract-Method $detail 'private double DyDetail('),
    (Extract-Method $detail 'private double DyDetailSide('),
    (Extract-Method $detail 'private int StraightForX('),
    (Extract-Method $detail 'private static int DetailLengthAdet('),
    (Extract-Method $detail 'private double DetailCutX('),
    (Extract-Method $detail 'private bool KeepDetailX('),
    (Extract-Method $detail 'private double DetailMeasureLane('),
    (Extract-Method $detail 'private void DrawDetailDimensions('),
    (Extract-Method $main 'private static int ElliCapCm('),
    (Extract-Method $cut 'private static Point2d DetailIntersection('),
    (Extract-Method $cut 'private static List<Point2d> ClipDetailPolygon(')
) -join "`n"
$code = @'
using System;
using System.Collections.Generic;
public struct Point2d {
 public double X,Y;
 public Point2d(double x,double y){X=x;Y=y;}
}
public struct Point3d {
 public double X,Y,Z;
 public Point3d(double x,double y,double z){X=x;Y=y;Z=z;}
}
public class Transaction {}
public class BlockTableRecord {}
static class TemelCekuruSettings {
 public const double TemplateHatilYuksekligiCm=50,TemplateUstTemelYuksekligiCm=40,TemplateTemelYuksekligiCm=100,TemplateAltTemelYuksekligiCm=40,TemplateTemelGenisligiCm=200,TemplatePaspayiCm=3,TemplateGrobetonAcisi=80;
 public static double HatilYuksekligiCm=0,UstTemelYuksekligiCm=40,StemCm=100,AltTemelYuksekligiCm=40,GrobetonAcisi=80,PaspayiCm=3;
 public static int UstAralikCm=20,AltAralikCm=15,UstCapMm=14,AltCapMm=14;
}
static class TemelDetaySettings {
 public static bool SolKademe=true,SagKademe=true;
 public static double UstTemelYuksekligiCm=40,SagUstTemelYuksekligiCm=40;
 public static double KademeUzunluguCm=200;
}
public class Fit {
'@ + $constants + "`n" + $fields + "`n" + $methods + @'
 private bool SingleDetail => DetayBaslik && DetailLeft!=DetailRight;
 class DimensionCall { public bool Horizontal; public double A,B,Lane; }
 readonly List<DimensionCall> DimensionCalls=new List<DimensionCall>();
 Point3d MapPt(Point3d point) {
  return new Point3d(point.X+(point.X>=XInnerR?DW+DSide:-DSide),point.Y+Dy4+10,point.Z);
 }
 void AddVDim(Transaction tr,BlockTableRecord ms,double xMeas,double a,double b,double lane,string layer) {
  if(Math.Abs(b-a)<1)return;
  DimensionCalls.Add(new DimensionCall{A=Math.Min(a,b),B=Math.Max(a,b),Lane=lane});
 }
 void AddHDim(Transaction tr,BlockTableRecord ms,double a,double b,double yMeas,double lane,string layer) {
  if(Math.Abs(b-a)<1)return;
  DimensionCalls.Add(new DimensionCall{Horizontal=true,A=Math.Min(a,b),B=Math.Max(a,b),Lane=lane});
 }
 static void Near(double got,double expected,string name) {
  if(Math.Abs(got-expected)>0.000001) throw new Exception(name+": "+got+" != "+expected);
 }
 static Fit Make(double left,double right,bool enableLeft,bool enableRight,double leftThickness,double rightThickness,double bottom,double hatil) {
  TemelDetaySettings.SolKademe=enableLeft;TemelDetaySettings.SagKademe=enableRight;
  TemelDetaySettings.UstTemelYuksekligiCm=leftThickness;TemelDetaySettings.SagUstTemelYuksekligiCm=rightThickness;
  TemelCekuruSettings.StemCm=Math.Max(enableLeft?left:0,enableRight?right:0);
  TemelCekuruSettings.UstTemelYuksekligiCm=leftThickness;
  TemelCekuruSettings.AltTemelYuksekligiCm=bottom;TemelCekuruSettings.HatilYuksekligiCm=hatil;
  Fit f=FromSettings(200,1,1,1,1,1);f.ConfigureDetail(left,right);return f;
 }
 static void Check(Fit f,double left,double right,double lt,double rt,double bottom,bool el,bool er) {
  double floor=Y3+f.Dy3;
  Near(Y1+f.DyDetail(Y1,false)-floor,(el?left:0)*DuPerCm,"left height");
  Near(Y1+f.DyDetail(Y1,true)-floor,(er?right:0)*DuPerCm,"right height");
  Near(Y1+f.DyDetail(Y1,false)-Y2-f.DyDetail(Y2,false),(el?lt:bottom)*DuPerCm,"left thickness");
  Near(Y1+f.DyDetail(Y1,true)-Y2-f.DyDetail(Y2,true),(er?rt:bottom)*DuPerCm,"right thickness");
  Near(Y3+f.DyDetail(Y3,false),Y3+f.DyDetail(Y3,true),"shared floor");
  Near(Y4+f.DyDetail(Y4,false),Y4+f.DyDetail(Y4,true),"shared bottom");
  Near(Y3+f.Dy3-Y4-f.Dy4,bottom*DuPerCm,"bottom thickness");
 }
 public static string Run() {
  Check(Make(80,140,true,true,35,55,60,0),80,140,35,55,60,true,true);
  Check(Make(140,80,true,true,55,35,45,0),140,80,55,35,45,true,true);
  Check(Make(30,60,true,true,60,35,70,0),30,60,60,35,70,true,true);
  Check(Make(80,140,true,false,35,55,60,0),80,140,35,55,60,true,false);
  Check(Make(80,140,false,true,35,55,60,0),80,140,35,55,60,false,true);
  Fit plain=Make(80,140,true,true,35,55,60,0);
  double[] anchors={Y1,Y2,Y3,Y4};
  double[,] mapped=new double[2,4];
  for(int s=0;s<2;s++)for(int i=0;i<4;i++)mapped[s,i]=plain.DyDetail(anchors[i],s==1);
  Fit withHatil=Make(80,140,true,true,35,55,60,50);
  for(int s=0;s<2;s++)for(int i=0;i<4;i++)Near(withHatil.DyDetail(anchors[i],s==1),mapped[s,i],"hatil independence");
  Near(withHatil.StraightForX(1800),134,"left rebar length");
  Near(withHatil.StraightForX(2800),194,"right rebar length");
  Fit symmetric=Make(100,100,true,true,40,40,40,0);
  Fit original=FromSettings(200,1,1,1,1,1);
  Near(symmetric.DyDetail(Y1,false),original.Dy1,"symmetric top matches pit");
  Near(symmetric.DyDetail(Y2,false),original.Dy2,"symmetric underside matches pit");
  Near(symmetric.DyDetail(Y3,false),original.Dy3,"symmetric floor matches pit");
  Near(symmetric.DyDetail(Y4,false),original.Dy4,"symmetric bottom matches pit");
  Near(DetailLengthAdet(false),11,"upper length count");
  Near(DetailLengthAdet(true),14,"lower length count with different spacing");
  TemelDetaySettings.KademeUzunluguCm=206;
  Near(DetailLengthAdet(false),11,"exact spacing count");
  TemelDetaySettings.KademeUzunluguCm=207;
  Near(DetailLengthAdet(false),12,"count increases past spacing boundary");
  TemelDetaySettings.KademeUzunluguCm=200;
  var rectangle=new List<Point2d>{new Point2d(0,0),new Point2d(10,0),new Point2d(10,5),new Point2d(0,5)};
  Near(Area(ClipDetailPolygon(rectangle,6,true)),30,"left hatch crop area");
  Near(Area(ClipDetailPolygon(rectangle,6,false)),20,"right hatch crop area");
  Near(Area(ClipDetailPolygon(rectangle,0,true)),0,"edge coincidence crop");
  Near(Area(ClipDetailPolygon(rectangle,20,true)),50,"full inside hatch");
  Near(Area(ClipDetailPolygon(rectangle,-1,true)),0,"full outside hatch");
  Fit left=Make(80,140,true,false,35,55,60,0);
  Fit right=Make(80,140,false,true,35,55,60,0);
  Near(left.KeepDetailX(XInnerL)?1:0,1,"left active face remains");
  Near(left.KeepDetailX(XOutR+left.DW)?1:0,0,"left unused side cropped");
  Near(right.KeepDetailX(XInnerR+right.DW)?1:0,1,"right active face remains");
  Near(right.KeepDetailX(XOutL)?1:0,0,"right unused side cropped");
  Near(left.DetailMeasureLane(),10*DuPerCm,"dimension row gap is 10 cm");
  CheckDimensions(Make(150,100,true,true,40,40,40,50),false);
  Fit pit=Make(150,150,true,true,40,40,40,50);pit.DetayBaslik=false;
  CheckDimensions(pit,false);
  CheckDimensions(Make(150,100,true,false,40,40,40,50),true);
  CheckDimensions(Make(150,100,false,true,40,40,40,50),true);
  return "PASS: geometry, length counts, cuts, shared pit/detail dimension layout, concrete-only upper totals, lower grobeton widths, full-height second vertical chains.";
 }
 static void CheckDimensions(Fit f,bool single) {
  f.DrawDetailDimensions(new Transaction(),new BlockTableRecord());
  double top=Math.Max(Y1+(f.DetayBaslik?f.DetailDy1L:f.Dy1),Y1+(f.DetayBaslik?f.DetailDy1R:f.Dy1))+TemelCekuruSettings.HatilYuksekligiCm*DuPerCm;
  double gro=YGro+f.Dy4+10;
  double total=0;int upper=0,lower=0;
  foreach(var d in f.DimensionCalls)if(d.Horizontal) {
   if(d.Lane>top) {upper++;total=Math.Max(total,(d.B-d.A)/DuPerCm);}
   else {lower++;Near((d.B-d.A)/DuPerCm,10,"grobeton projection under drawing");if(d.Lane>=gro)throw new Exception("grobeton dimension not underneath");}
  }
  Near(upper,single?2:4,"upper chain/total count");Near(lower,single?1:2,"lower grobeton projection count");
  Near(total,single?40:280,"upper total excludes both grobeton projections");
  for(int side=0;side<2;side++) {
   bool right=side==1;if(f.DetayBaslik&&!f.DetailEnabled(right))continue;
   double lane=right?XInnerR+f.DW-40:XInnerL+40;
   double min=double.MaxValue,max=double.MinValue,sum=0;int count=0;
   foreach(var d in f.DimensionCalls)if(!d.Horizontal&&Math.Abs(d.Lane-lane)<0.001){min=Math.Min(min,d.A);max=Math.Max(max,d.B);sum+=d.B-d.A;count++;}
   double sideTop=Y1+(f.DetayBaslik?(right?f.DetailDy1R:f.DetailDy1L):f.Dy1)+TemelCekuruSettings.HatilYuksekligiCm*DuPerCm;
   Near(min,gro,"second vertical row reaches grobeton bottom");Near(max,sideTop,"second vertical row reaches section top");
   Near(sum,max-min,"second vertical row continuous without overlap");Near(count,3,"hatil/thickness/remainder in second row");
  }
 }
 static double Area(List<Point2d> points) {
  double area=0;
  for(int i=0;i<points.Count;i++){Point2d a=points[i],b=points[(i+1)%points.Count];area+=a.X*b.Y-b.X*a.Y;}
  return Math.Abs(area)/2;
 }
}
'@
Add-Type -TypeDefinition $code -WarningAction SilentlyContinue
[Fit]::Run()
