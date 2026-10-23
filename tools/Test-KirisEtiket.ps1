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
$members = (Extract 'private static double KirisAcilimEtiketYatayKaydir(') + "`n" + (Extract 'private static bool KirisAcilimMesnetOlcuUstte(')
$code = @'
using System;
using System.Linq;
using System.Collections.Generic;
public static class BeamLabelTests {
'@ + $members + @'
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);}
 public static void Run(){
  for(int i=0;i<20;i++){
   Check(!KirisAcilimMesnetOlcuUstte(true,i),"Extra support dimensions always below regardless of stagger");
   Check(KirisAcilimMesnetOlcuUstte(false,i)==(i%2==0),"Other staggered bars retain dimension side");
  }
  var boxes=new List<(double X0,double X1,double Y0,double Y1)>();
  for(int i=0;i<30;i++){
   double x0=-12+i%3, x1=14+i%5, y0=i%2, y1=y0+5;
   double dx=KirisAcilimEtiketYatayKaydir(x0,x1,y0,y1,boxes);
   foreach(var b in boxes)
    Check(y0>=b.Y1 || y1<=b.Y0 || x0+dx>=b.X1+5-1e-7 || x1+dx<=b.X0-5+1e-7,"All overlapping label boxes have final 10 cm horizontal gap");
   boxes.Add((x0+dx,x1+dx,y0,y1));
  }
  Check(KirisAcilimEtiketYatayKaydir(-12,14,50,55,boxes)==0,"Vertically separated labels retain their intended horizontal station");
  Check(KirisAcilimEtiketYatayKaydir(1000,1020,0,5,boxes)==0,"Noncolliding label never moves");
 }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
[BeamLabelTests]::Run()
Write-Output 'PASS: extra dimensions always below, normal stagger preserved, actual-box horizontal separation 10 cm final, noncolliding labels retained.'
