# Run production stirrup layout/count methods without an AutoCAD host.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = [IO.File]::ReadAllText((Join-Path $root 'ST4_Plan_ID_Ciz/PlanIdDrawingManager.KirisAcilim.cs'))
function Extract-Method([string]$signature) {
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
    $source.Substring($start, $end - $start)
}
$methods = @(
    (Extract-Method 'private static List<double> KirisAcilimEtriyeKonumlari('),
    (Extract-Method 'private static double KirisAcilimEtriyeOrtaAralik('),
    (Extract-Method 'private static void KirisAcilimEtriyeSaplamaYuzleri('),
    (Extract-Method 'private static (double Lo, double Hi) KirisAcilimEtriyeSaplamaBolgesi('),
    (Extract-Method 'private static bool KirisAcilimKonsolEtriyeUcIz('),
    (Extract-Method 'private static List<double> KirisAcilimKonsolEtriyeDizilimi('),
    (Extract-Method 'private static List<double> KirisAcilimEtriyeTamKonumlari('),
    (Extract-Method 'private static double KirisAcilimSarilmaTamBoy('),
    (Extract-Method 'private static List<(double Lo, double Hi, double Aralik, int Adet)> KirisAcilimEtriyeEtiketBolgeleri('),
    (Extract-Method 'private static void KirisAcilimEtriyeOlculeri(')
) -join "`n"
$code = @'
using System;
using System.Collections.Generic;
public class Transaction {}
public class BlockTableRecord {}
public class Database {}
public struct ObjectId {}
public struct Point3d {
 public double X,Y,Z;
 public Point3d(double x,double y,double z){X=x;Y=y;Z=z;}
}
public class RotatedDimension {
 public string Layer;
 public double Dimexo;
 public bool Dimse1,Dimse2;
 public Point3d A,B,Lane;
 public RotatedDimension(double r,Point3d a,Point3d b,Point3d lane,string t,ObjectId s){A=a;B=b;Lane=lane;}
}
public static class KirisEtriyeTest {
 struct KirisAcilimSaplama { public int BeamId; public bool IzdusumVar,SolUc,SagUc; public double X0,X1; }
 static string LayerOlcu="OLCU";
 static List<RotatedDimension> Dims=new List<RotatedDimension>();
 static ObjectId GetOrCreatePlanOlcuDimStyle(Transaction t,Database d,double a,double b,string n)=>new ObjectId();
 static void AppendEntity(Transaction t,BlockTableRecord b,RotatedDimension d){Dims.Add(d);}
'@ + $methods + @'
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);}
 public static void Run(){
  var sap=new List<(double Lo,double Hi)>{(230,280)};
  var xs=KirisAcilimEtriyeKonumlari(0,500,9,9,120,120,false,false,sap,out var zones);
  Check(zones.Count==0,"Single spacing must not create confinement zones, including at secondary beams");
  Check(xs.Count==4 && xs[0]==5 && xs[1]==14 && xs[2]==486 && xs[3]==495,"Both single-spacing end samples must show 9 cm; residual belongs inside the span");
  var tags=KirisAcilimEtriyeEtiketBolgeleri(5,495,9,9,zones);
  Check(tags.Count==1 && tags[0].Adet==56 && tags[0].Aralik==9,"Single label must count entire beam, not drawn samples");
  KirisAcilimEtriyeOlculeri(null,null,new Database(),0,0,0,500,xs,zones,9,9,x=>0,null,null);
  Check(Dims.FindAll(d=>d.Lane.Y==-20).Count==3,"Single spacing second dimension row must have no interior confinement boundaries");
  Check(Dims.FindAll(d=>d.Lane.Y==-10 && Math.Abs(d.B.X-d.A.X-9)<1e-6).Count==2,"Both first-row interval dimensions must measure actual 9 cm");
  Dims.Clear();
  xs=KirisAcilimEtriyeKonumlari(0,500,9,20,120,120,false,false,new List<(double,double)>(),out zones);
  Check(zones.Count==2 && zones[0].Lo==5 && zones[0].Hi==131 && zones[1].Lo==369,"Dual spacing must preserve 2h rounding");
  Check(xs.Contains(151) && xs.Contains(349),"Both transition samples must follow the actual 20 cm middle grid");
  tags=KirisAcilimEtriyeEtiketBolgeleri(5,495,9,20,zones);
  Check(tags.Count==3 && tags[0].Adet==15 && tags[0].Aralik==9 && tags[1].Adet==11 && tags[1].Aralik==20 && tags[2].Adet==15,"Dual labels must exclude shared boundary bars from middle count");
  KirisAcilimEtriyeOlculeri(null,null,new Database(),0,0,0,500,xs,zones,9,20,x=>0,null,null);
  Check(Dims.FindAll(d=>d.Lane.Y==-20).Count==5,"Dual dimension row must preserve confinement boundaries");
  // A 45 cm gap is absorbed, but the strict 60 cm boundary remains a normal zone.
  foreach(double length in new[]{307.0,321.999,322.0}){
   var visible=KirisAcilimEtriyeKonumlari(0,length,9,20,120,120,false,false,new List<(double,double)>(),out var z);
   Check(z.Count==(length<322?1:2),"Confinement merge must use strict gap < 60 cm");
   var all=KirisAcilimEtriyeTamKonumlari(5,length-5,9,20,z,out var labels);
   if(length<322)Check(labels.Count==1 && labels[0].Aralik==9,"Absorbed short gap must have one tight-spacing label");
   int total=0;foreach(var label in labels)total+=label.Adet;
   Check(total==all.Count,"Merge must retain exact total stirrup count");
  }
  xs=KirisAcilimEtriyeKonumlari(0,1200,9,20,120,120,false,false,new List<(double,double)>{(176,230),(275,329),(374,428)},out zones);
  Check(zones.Count==2 && zones[0].Lo==5 && zones[0].Hi>=428,"Short gaps must merge transitively between secondary and support confinement");
  // 155 cm middle region: both displayed transitions must be 20, never a 15 cm remainder.
  xs=KirisAcilimEtriyeKonumlari(0,417,9,20,120,120,false,false,new List<(double,double)>(),out zones);
  Check(zones.Count==2 && zones[1].Lo-zones[0].Hi==155,"Transition fixture middle length");
  Check(xs.Contains(151) && xs.Contains(266),"155 cm region must place samples 20 cm from both confinement boundaries");
  Dims.Clear();
  KirisAcilimEtriyeOlculeri(null,null,new Database(),0,0,0,417,xs,zones,9,20,x=>0,null,null);
  Check(Dims.Exists(d=>d.Lane.Y==-10 && d.A.X==131 && d.B.X==151) && Dims.Exists(d=>d.Lane.Y==-10 && d.A.X==266 && d.B.X==286),"Both transition dimensions must measure the actual 20 cm");
  for(int length=323;length<1000;length+=7){
   var visible=KirisAcilimEtriyeKonumlari(0,length,9,20,120,120,false,false,new List<(double,double)>(),out var z);
   var all=KirisAcilimEtriyeTamKonumlari(5,length-5,9,20,z,out var labels);
   foreach(var b in z){
    if(b.Lo>5)Check(all.Contains(b.Lo-20) && visible.Contains(b.Lo-20),"Right normal transition must have actual data spacing");
    if(b.Hi<length-5)Check(all.Contains(b.Hi+20) && visible.Contains(b.Hi+20),"Left normal transition must have actual data spacing");
   }
  }
  xs=KirisAcilimEtriyeKonumlari(0,500,9,20,120,120,false,false,sap,out zones);
  tags=KirisAcilimEtriyeEtiketBolgeleri(5,495,9,20,zones);
  Check(zones.Count==3 && tags.Count==5 && tags[2].Aralik==9 && tags[2].Adet==7,"Independent secondary beam zone gets its own label");
  xs=KirisAcilimEtriyeKonumlari(0,200,9,20,120,120,false,false,sap,out zones);
  tags=KirisAcilimEtriyeEtiketBolgeleri(5,195,9,20,zones);
  Check(zones.Count==1 && tags.Count==1 && tags[0].Adet==23,"Overlapping confinement must merge without duplicate labels");
  xs=KirisAcilimEtriyeKonumlari(0,500,9,9,120,120,true,true,sap,out zones);
  Check(zones.Count==0 && xs.Count==4,"Uniform spacing must also mark free ends");
  xs=KirisAcilimEtriyeKonumlari(0,500,9,20,120,120,false,true,new List<(double,double)>(),out zones);
  tags=KirisAcilimEtriyeEtiketBolgeleri(5,495,9,20,zones);
  double effective=KirisAcilimEtriyeOrtaAralik(9,20,false,true);
  var consoleAll=KirisAcilimKonsolEtriyeDizilimi(0,500,9,false,false,out tags);
  Check(zones.Count==0 && tags.Count==1 && tags[0].Aralik==9 && tags[0].Adet==56,"Bare cantilever count must include its cover-end stirrup");
  Check(consoleAll[0]==5 && consoleAll[55]==496,"Right cantilever must start at fixed support and end at cover");
  var rightTrace=KirisAcilimKonsolEtriyeDizilimi(0,160,9,false,true,out tags);
  Check(rightTrace[0]==5 && rightTrace[rightTrace.Count-1]==158 && tags[0].Adet==18,"Right terminal beam face must not force a 5 cm gap or a short added interval");
  var leftTrace=KirisAcilimKonsolEtriyeDizilimi(0,160,9,true,true,out tags);
  Check(leftTrace[0]==2 && leftTrace[leftTrace.Count-1]==155,"Left cantilever must mirror the fixed-end sequence");
  var bare=KirisAcilimKonsolEtriyeDizilimi(0,160,9,false,false,out tags);
  Check(bare[bare.Count-1]==156,"Without a terminal beam the last stirrup must sit exactly at the 4 cm cover limit");
  var izler=new List<KirisAcilimSaplama>{new KirisAcilimSaplama{IzdusumVar=true,SagUc=true,X0=160,X1=200}};
  Check(KirisAcilimKonsolEtriyeUcIz(izler,0,160,false,true),"Terminal trace inner face must be recognized");
  Check(!KirisAcilimKonsolEtriyeUcIz(izler,0,150,false,true) && !KirisAcilimKonsolEtriyeUcIz(izler,0,160,true,false),"Unrelated trace or opposite end must not change free-end cover");
  izler.Add(new KirisAcilimSaplama{IzdusumVar=true,SolUc=true,X0=-40,X1=0});
  Check(KirisAcilimKonsolEtriyeUcIz(izler,0,160,true,false),"Left terminal trace must use its right face");
  Dims.Clear();
  var rightXs=KirisAcilimEtriyeKonumlari(0,160,9,20,120,120,false,true,new List<(double,double)>(),out zones,true);
  KirisAcilimEtriyeOlculeri(null,null,new Database(),0,0,0,160,rightXs,zones,9,9,x=>0,null,null,false,true);
  Check(!Dims.Exists(d=>d.B.X==160),"Free tip must not get an artificial clearance dimension");
  Check(KirisAcilimEtriyeOrtaAralik(9,20,true,false)==9 && KirisAcilimEtriyeOrtaAralik(9,20,false,false)==20,"Only a flagged cantilever changes the middle spacing");
  xs=KirisAcilimEtriyeKonumlari(0,600,9,20,120,120,false,false,new List<(double,double)>{(270,330)},out zones);
  Check(zones.Count==3 && zones[1].Lo==268.5 && zones[1].Hi==331.5,"40 cm secondary beam plus 10 cm each side must round symmetrically to 63 cm");
  Check(xs.Contains(268.5) && xs.Contains(331.5),"Centered secondary region boundaries must be drawn");
  // A clipped split-span intersection must align to the same offset trace the user sees.
  var clipped=new KirisAcilimSaplama{BeamId=100,X0=300,X1=340};
  var drawn=new List<KirisAcilimSaplama>{new KirisAcilimSaplama{BeamId=100,X0=280,X1=320}};
  KirisAcilimEtriyeSaplamaYuzleri(clipped,drawn,out double face0,out double face1);
  Check(face0==280 && face1==320,"Confinement must use rendered trace faces, not clipped intersection center");
  var padded=KirisAcilimEtriyeSaplamaBolgesi(face0,face1,80);
  Check(padded.Lo==240 && padded.Hi==360,"Host height 80 must extend 40 cm from each trace face");
  xs=KirisAcilimEtriyeKonumlari(0,700,9,20,160,160,false,false,new List<(double,double)>{padded},out zones);
  Check(zones.Count==3 && zones[1].Lo==237 && zones[1].Hi==363,"Rounded ask region must remain centered at rendered trace midpoint 300");
  Check(xs.Contains(237) && xs.Contains(363),"Both centered region endpoints must be drawn");
  foreach(double x in xs)if(x>=237 && x<=363)Check(xs.Exists(y=>Math.Abs(y-(600-x))<1e-6),"Displayed ask stirrups must mirror around the drawn trace center");
  var h60=KirisAcilimEtriyeSaplamaBolgesi(150,170,60);
  Check(h60.Lo==120 && h60.Hi==200,"Host height 60 uses 30 cm each side");
  xs=KirisAcilimEtriyeKonumlari(0,700,9,20,120,120,false,false,new List<(double,double)>{h60},out zones);
  Check(zones[0].Lo==5 && zones[0].Hi>=200,"Secondary beam near left confinement must extend it past its face by at least h/2");
  xs=KirisAcilimEtriyeKonumlari(0,700,9,20,120,120,false,false,new List<(double,double)>{KirisAcilimEtriyeSaplamaBolgesi(530,550,60)},out zones);
  Check(zones[zones.Count-1].Lo<=500 && zones[zones.Count-1].Hi==695,"Same h/2 extension must work at right confinement");
  KirisAcilimEtriyeSaplamaYuzleri(clipped,new List<KirisAcilimSaplama>{new KirisAcilimSaplama{BeamId=101,X0=280,X1=320}},out face0,out face1);
  Check(face0==300 && face1==340,"Unrelated beam trace must not move a secondary region");
  // Non-multiple lengths: real full-layout samples show the specified step at both faces.
  for(int length=40;length<1000;length+=11)
   foreach(double spacing in new[]{8.0,9.0,12.0,20.0}){
    if(length-10<spacing*2)continue;
    var visible=KirisAcilimEtriyeKonumlari(0,length,spacing,spacing,120,120,false,false,new List<(double,double)>(),out var z);
    var all=KirisAcilimEtriyeTamKonumlari(5,length-5,spacing,spacing,z,out var labels);
    Check(Math.Abs(visible[1]-visible[0]-spacing)<1e-6 && Math.Abs(visible[visible.Count-1]-visible[visible.Count-2]-spacing)<1e-6,"Uniform beam end samples have misleading short interval");
    Check(labels[0].Adet==all.Count && all.Count==(int)Math.Ceiling((length-10)/spacing-1e-8)+1,"Uniform beam count changed while moving residual");
    foreach(double x in visible)Check(all.Contains(x),"End sample must correspond to an actual stirrup");
   }
  // Many lengths, offsets and merging arrangements: sum of labels equals unique placed stirrups.
  int cases=0;
  for(int length=30;length<=1200;length+=17)
   for(int mode=0;mode<5;mode++){
    bool left=mode==1,right=mode==2;
    double spacing=mode==3?9:20;
    var secondary=new List<(double,double)>();
    if(mode==4){secondary.Add((length*.45-30,length*.45+30));secondary.Add((length*.55-25,length*.55+25));}
    var visible=KirisAcilimEtriyeKonumlari(-23.5,length-23.5,9,spacing,120,100,left,right,secondary,out var z);
    double midSpacing=KirisAcilimEtriyeOrtaAralik(9,spacing,left,right);
    var all=KirisAcilimEtriyeTamKonumlari(-18.5,length-28.5,9,midSpacing,z,out var labels);
    if(left||right)all=KirisAcilimKonsolEtriyeDizilimi(-23.5,length-23.5,9,left,false,out labels);
    int sum=0;foreach(var tag in labels)sum+=tag.Adet;
    Check(sum==all.Count,"Label total differs from placed count at length="+length+" mode="+mode);
    foreach(double x in visible)Check(all.Contains(x),"Representative line has no actual stirrup");
    for(int j=1;j<all.Count;j++)Check(all[j]>all[j-1] && all[j]-all[j-1]<=midSpacing+1e-6,"Duplicate or over-spaced stirrups");
    foreach(var tag in labels){
     int count=0;
     foreach(double x in all){
      bool dense=false;foreach(var b in z)if(x>=b.Lo-1e-6 && x<=b.Hi+1e-6)dense=true;
      if(x>=tag.Lo-1e-6 && x<=tag.Hi+1e-6 && (tag.Aralik==9 || !dense))count++;
     }
     Check(count==tag.Adet,"Region count does not match actual coordinate membership");
    }
    cases++;
   }
  Console.WriteLine("PASS: "+cases+" full-layout/label-total comparisons.");
  for(int length=11;length<1000;length+=7)
   foreach(bool left in new[]{false,true})
    foreach(bool trace in new[]{false,true}){
     var all=KirisAcilimKonsolEtriyeDizilimi(0,length,9,left,trace,out var labels);
     double anchor=left?length-5:5;
     double limit=left?(trace?0:4):length-(trace?0:4);
     Check(labels.Count==1 && labels[0].Adet==all.Count,"Cantilever label/placement total mismatch");
     Check(left?all[all.Count-1]==anchor:all[0]==anchor,"Wrong fixed-end anchor");
     if(!trace)Check(left?all[0]==limit:all[all.Count-1]==limit,"Bare free-end stirrup must sit exactly at cover");
     foreach(double x in all)Check(left?x>=limit-1e-6:x<=limit+1e-6,"Stirrup enters terminal beam or cover");
     for(int j=1;j<all.Count;j++){
      double step=all[j]-all[j-1];
      bool coverEnd=!trace && (left?j==1:j==all.Count-1);
      Check(coverEnd?step>0 && step<=9:Math.Abs(step-9)<1e-6,"Only the bare cover-end step may shorten");
     }
    }
  Console.WriteLine("PASS: 568 mirrored cantilever/trace/cover cases.");
  xs=KirisAcilimEtriyeKonumlari(0,10,9,9,120,120,false,false,sap,out zones);
  Check(xs.Count==0,"Degenerate beam must remain empty");
 }
}
'@
# Windows PowerShell Add-Type eski C# derleyicisi ValueTuple/=> desteklemez; dotnet ile derle.
$work = Join-Path $env:TEMP ("Test-KirisEtriye-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force -Path $work | Out-Null
try {
    Set-Content -Path (Join-Path $work 'Program.cs') -Value ($code + "`nclass Program { static void Main(){ KirisEtriyeTest.Run(); } }") -Encoding UTF8
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>
'@ | Set-Content -Path (Join-Path $work 'Test-KirisEtriye.csproj') -Encoding UTF8
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    & $dotnet run --project (Join-Path $work 'Test-KirisEtriye.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Test-KirisEtriye dotnet run exit $LASTEXITCODE" }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
Write-Output 'PASS: single/dual spacing, actual coordinate counts, centered secondary regions, dimensions and full-length cantilever confinement.'
