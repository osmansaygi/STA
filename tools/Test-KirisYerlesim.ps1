$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = [IO.File]::ReadAllText((Join-Path $root 'ST4_Plan_ID_Ciz/PlanIdDrawingManager.KirisAcilim.Yerlesim.cs'))
function Extract([string]$signature) {
    $start=$source.IndexOf($signature); if($start -lt 0){throw "Missing $signature"}
    $open=$source.IndexOf('{',$start);$end=$open+1;$depth=1
    while($depth -gt 0){if($source[$end] -eq '{'){$depth++};if($source[$end] -eq '}'){$depth--};$end++}
    $source.Substring($start,$end-$start)
}
$members=@(
 (Extract 'private sealed class KirisYerlesimBoyut'),
 (Extract 'private sealed class KirisYerlesimKonum'),
 (Extract 'private static List<KirisYerlesimKonum> KirisYerlesimPlanla(')
) -join "`n"
$code=@'
using System;
using System.Linq;
using System.Collections.Generic;
public class BeamPackingTests {
'@+$members+@'
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
 static void Near(double a,double b,string text){Check(Math.Abs(a-b)<1e-6,text);}
 public static void Run(){
  var sizes=new List<KirisYerlesimBoyut>{
   new KirisYerlesimBoyut{Width=200,Above=40,Below=250},
   new KirisYerlesimBoyut{Width=250,Above=90,Below=180},
   new KirisYerlesimBoyut{Width=300,Above=30,Below=200}};
  var p=KirisYerlesimPlanla(sizes,600,900);
  Near(p[0].BeamY,p[1].BeamY,"Same row beam tops align despite differing section/rebar heights");
  Near(p[1].X-p[0].X-sizes[0].Width,80,"80cm final horizontal envelope gap");
  Near((p[2].BeamY-sizes[2].Below)-(p[0].BeamY+sizes[1].Above),80,"80cm final vertical row gap from bottom upward");
  Near(p.Min(v=>v.BeamY-sizes[v.Index].Below),0,"Layout starts at the bottom instead of the top");
  Check(p.Select(v=>v.Index).SequenceEqual(new[]{0,1,2}),"Numerical input order remains row order");
  p=KirisYerlesimPlanla(sizes,600,500);Check(p.All(v=>v.Page==0),"Overflow widens the single floor sheet instead of paging");
  Check(p.Max(v=>v.X+sizes[v.Index].Width)>600,"Width expands when existing height cannot fit more rows");
  sizes=new List<KirisYerlesimBoyut>{new KirisYerlesimBoyut{Width=1000,Above=50,Below=1500},new KirisYerlesimBoyut{Width=120,Above=50,Below=200}};
  p=KirisYerlesimPlanla(sizes,600,500);Check(p.Count==2&&p.All(v=>v.Page==0),"Oversize beam stays intact in one floor sheet");
  sizes=new[]{900.0,800,200,100}.Select(w=>new KirisYerlesimBoyut{Width=w,Above=40,Below=360}).ToList();
  p=KirisYerlesimPlanla(sizes,1000,880);
  Near(p.Max(v=>v.X+sizes[v.Index].Width),1080,"Reorder 900/800/200/100 into balanced rows of1080, not sequential1780");
  Check(!p.Select(v=>v.Index).SequenceEqual(new[]{0,1,2,3}),"Name order may change to reduce sheet width");
  sizes=new[]{400.0,500}.Select(w=>new KirisYerlesimBoyut{Width=w,Above=40,Below=160}).ToList();
  p=KirisYerlesimPlanla(sizes,2000,600);Near(p.Max(v=>v.X+sizes[v.Index].Width),500,"Narrow floor shrinks below template width");
  Check(KirisYerlesimPlanla(new List<KirisYerlesimBoyut>(),600,500).Count==0,"Empty floor has no page");
  for(int n=1;n<=100;n++){
   sizes=Enumerable.Range(0,n).Select(i=>new KirisYerlesimBoyut{Width=100+(i*137)%900,Above=30+(i*41)%200,Below=200+(i*79)%800}).ToList();
   p=KirisYerlesimPlanla(sizes,2400,1800);
   Check(p.Count==n&&p.Select(v=>v.Index).Distinct().Count()==n,"No lost/duplicate beam sets");
   Check(p.All(v=>v.Page==0),"Every floor is one sheet, even for100teams");
   Check(p.Max(v=>v.BeamY+sizes[v.Index].Above)<=Math.Max(1800,sizes.Max(v=>v.Above)+sizes.Max(v=>v.Below))+1e-6,"Existing sheet height respected");
   Near(p.Min(v=>v.BeamY-sizes[v.Index].Below),0,"Mixed layouts touch bottom baseline");
   for(int i=0;i<n;i++)for(int j=i+1;j<n;j++){
    var a=sizes[p[i].Index];var b=sizes[p[j].Index];
    double right=p[i].X+a.Width,left=p[j].X;
    double topI=p[i].BeamY+a.Above,botI=p[i].BeamY-a.Below;
    double topJ=p[j].BeamY+b.Above,botJ=p[j].BeamY-b.Below;
    Check(left>=right+80-1e-7||p[i].X>=p[j].X+b.Width+80-1e-7||botI>=topJ+80-1e-7||botJ>=topI+80-1e-7,"Every same-sheet pair has at least80cm gap");
   }
  }
 }
}
'@
# Windows PowerShell Add-Type eski C# derleyicisi yerel fonksiyonları desteklemez; dotnet ile derle.
$work = Join-Path $env:TEMP ("Test-KirisYerlesim-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force -Path $work | Out-Null
try {
    Set-Content -Path (Join-Path $work 'Program.cs') -Value ($code + "`nclass Program { static void Main(){ BeamPackingTests.Run(); System.Console.WriteLine(""PASS""); } }") -Encoding UTF8
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>
'@ | Set-Content -Path (Join-Path $work 'Test-KirisYerlesim.csproj') -Encoding UTF8
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    & $dotnet run --project (Join-Path $work 'Test-KirisYerlesim.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Test-KirisYerlesim dotnet run exit $LASTEXITCODE" }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
Write-Output 'PASS: one sheet per floor, balanced1080-width case, shrinking below template width, top alignment, 80cm gaps, height bounds, oversize beams and100mixed layouts.'
