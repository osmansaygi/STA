$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = [IO.File]::ReadAllText((Join-Path $root 'ST4_Plan_ID_Ciz/PlanIdDrawingManager.KirisAcilim.cs'))
function Extract([string]$signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $open = $source.IndexOf('{', $start); $depth = 1; $end = $open + 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$members = @(
 (Extract 'private static double KirisAcilimDuzUcKesitOda('),
 (Extract 'private static void KirisAcilimMontajBesTamamla('),
 (Extract 'private static double KirisAcilimDonatiGercekBoy('),
 (Extract 'private static double KirisAcilimBesUzatmaOda('),
 (Extract 'private static double KirisAcilimYayBoy('),
 (Extract 'private static List<Point2d> KirisAcilimGonyeYayNokta('),
 (Extract 'private static void KirisAcilimDuzNoktaSil('),
 (Extract 'private static bool KirisAcilimNoktaDuz(')
) -join "`n"
$code = @'
using System;
using System.Collections.Generic;
using System.Globalization;
public struct Point2d {
 public double X,Y;
 public Point2d(double x,double y){X=x;Y=y;}
 public double GetDistanceTo(Point2d p)=>Math.Sqrt((X-p.X)*(X-p.X)+(Y-p.Y)*(Y-p.Y));
}
public static class BeamLengthTests {
 static void KirisAcilimTani(string message){}
 const double KirisAcilimYuvarlamaCm=5, KirisAcilimMontajMaxBoyCm=1200, KirisAcilimMontajPaspayiCm=5.5, KirisAcilimGonyeYaricapCm=3;
'@ + $members + @'
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);}
 static void Near(double a,double b,string name){Check(Math.Abs(a-b)<1e-6,name+": "+a+" vs "+b);}
 static List<Point2d> Bent(double span)=>new List<Point2d>{new Point2d(0,30),new Point2d(0,0),new Point2d(span,0),new Point2d(span,30)};
 public static void Run(){
  foreach(double raw in new[]{744.0,843.0}){
   var p=new List<Point2d>{new Point2d(0,-30),new Point2d(raw,-30)};
   double room=KirisAcilimDuzUcKesitOda(p[1],p[0],700,1000,0,0,60), actual=raw;
   Near(room,5,"Continuation beam has 5cm room despite initial support limit");
   KirisAcilimMontajBesTamamla(p,ref actual,false,false,0,raw,0,0,0,0,0,room);
   Near(actual,5*Math.Ceiling(raw/5),"744->745 and 843->845");Near(p[1].X,actual,"Geometry really extends");
  }
  Near(KirisAcilimDuzUcKesitOda(new Point2d(994,-30),new Point2d(900,-30),700,1000,0,0,60),0.5,"End face cover limits room");
  Near(KirisAcilimDuzUcKesitOda(new Point2d(843,-70),new Point2d(800,-70),700,1000,0,0,60),0,"Lower neighbouring beam excludes exposed bar");
  Near(KirisAcilimDuzUcKesitOda(new Point2d(843,-5.5),new Point2d(800,-5.5),700,1000,0,-30,60),0,"Falling beam top blocks horizontal extension");
  Near(KirisAcilimDuzUcKesitOda(new Point2d(843,-5.5),new Point2d(800,-5.5),700,1000,0,0,60),5,"Exactly covered level bar can extend");
  Near(KirisAcilimDuzUcKesitOda(new Point2d(843,-19.9),new Point2d(800,-19.9),700,1000,0,-30,60),1,"Sloping concrete cover clips available room to 1cm");
  foreach(double span in new[]{350.0,745.0,100.1,105.8}){
   var p=Bent(span);double original=KirisAcilimDonatiGercekBoy(p), boy=span+60;
   double target=5*Math.Ceiling(original/5-1e-9);
   KirisAcilimMontajBesTamamla(p,ref boy,true,true,0,span,50,50,40,40);
   Near(boy,target,"Target includes bend arc deductions");Near(KirisAcilimDonatiGercekBoy(p),target,"Actual geometry matches label");
   Near(p[1].X,0,"Left bend position retained");Near(p[2].X,span,"Right bend position retained");
   Check(boy>=original && boy-original<5,"Never shorten the original bar");
   var tip=p[p.Count-1];KirisAcilimMontajBesTamamla(p,ref boy,true,true,0,span,50,50,40,40);
   Near(p[p.Count-1].Y,tip.Y,"Repeated completion is idempotent");
  }
  var one=new List<Point2d>{new Point2d(0,30),new Point2d(0,0),new Point2d(190,0)};
  double len=220;KirisAcilimMontajBesTamamla(one,ref len,true,false,0,190,50,0,0,0);Near(len,220,"218 cm one-hook case extends to 220");
  var split=Bent(350);len=410;
  KirisAcilimMontajBesTamamla(split,ref len,true,true,0,350,32,31,0,0);
  Near(len,410,"Split extension between two constrained hooks");Check(split[0].Y<=32 && split[3].Y<=31,"Cover room respected at both ends");
  var blocked=Bent(350);len=410;double before=KirisAcilimDonatiGercekBoy(blocked);
  KirisAcilimMontajBesTamamla(blocked,ref len,true,true,0,350,30,30,0,0);
  Near(len,before,"No room: retain actual unrounded length");Near(blocked[0].Y,30,"No unapproved geometry on failure");
  var straight=new List<Point2d>{new Point2d(0,0),new Point2d(402,0)};len=402;
  KirisAcilimMontajBesTamamla(straight,ref len,false,false,0,402,0,0,10,10);Near(len,405,"Straight bar extends within support cover");
  Near(straight[1].X,405,"Endpoint moves, not just label");
  var slope=new List<Point2d>{new Point2d(0,0),new Point2d(402,20)};len=0;
  KirisAcilimMontajBesTamamla(slope,ref len,false,false,0,402,0,0,10,10);
  Near(len,405,"Sloped straight bar also extends along existing axis");
  Check(slope[1].X<=406.5 && Math.Abs(slope[1].Y/slope[1].X-20.0/402)<1e-8,"Preserve slope and support cover");
  var max=new List<Point2d>{new Point2d(0,0),new Point2d(1199.7,0)};len=1199.7;
  KirisAcilimMontajBesTamamla(max,ref len,false,false,0,1199.7,0,0,20,20);Near(len,1200,"1200 cm boundary");
  var over=new List<Point2d>{new Point2d(0,0),new Point2d(1200.1,0)};len=1200.1;
  KirisAcilimMontajBesTamamla(over,ref len,false,false,0,1200.1,0,0,20,20);Near(len,1200.1,"Do not extend beyond stock length");
 }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
[BeamLengthTests]::Run()
Write-Output 'PASS: 744->745 and 843->845 continuation geometry; adjacent beam cover/end face/slope limits; bend-arc length, 407/802/218, split hook room, idempotence and 1200 cm limit.'
