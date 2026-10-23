param([string]$Prefix='bay',[switch]$Plan2027)
$ErrorActionPreference='Stop'
$d=$PSScriptRoot
$out=if($Plan2027){"$d\build2027"}else{"$d\build"}
Remove-Item "$out\ST4_Plan_ID_Ciz_*" -ErrorAction SilentlyContinue
& "$d\Build.ps1" -Plan2027:$Plan2027 | Out-Null
$plan=(Get-ChildItem "$out\ST4_Plan_ID_Ciz_*.dll").FullName
Remove-Item "$d\Native\bin\Debug\net10.0-windows\ST4_Plan_ID_Ciz_*" -ErrorAction SilentlyContinue
& dotnet build "$d\Native\Native.csproj" -c Debug -v quiet "-p:PlanDll=$plan" | Out-Null
if($LASTEXITCODE -ne 0){throw 'Native build failed'}
$root=(Resolve-Path "$d/../..").Path
$cases=@(
 @{n='ck-plain';s='D:\projeler\projeler2026\kızılkaya\20827_2_TICARI\STATIK\CK_20827_2_01.st4';a='0';r=''},
 @{n='ck-antet';s='D:\projeler\projeler2026\kızılkaya\20827_2_TICARI\STATIK\CK_20827_2_01.st4';a='1';r=''},
 @{n='ck-repeat';s='D:\projeler\projeler2026\kızılkaya\20827_2_TICARI\STATIK\CK_20827_2_01.st4';a='0';r='1'},
 @{n='ms';s='D:\projeler\projeler2026\hakan_yorgun\mehmet_sevic\statik\MS_OTP_01.st4';a='0';r=''},
 @{n='akdag';s="$root\dosyalar\AKDAG.st4";a='1';r=''},
 @{n='nodata';s='';a='0';r=''})
foreach($c in $cases){
 $base="$d\$Prefix-$($c.n)"
 Remove-Item "$base.*" -ErrorAction SilentlyContinue
 $env:ST4_FOUNDATION_SOURCE=$c.s;$env:ST4_FOUNDATION_RESULT="$base.txt";$env:ST4_FOUNDATION_DWG="$base.dwg"
 $env:ST4_FOUNDATION_ANTET=$c.a;$env:ST4_FOUNDATION_REPEAT=$c.r
 & 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s "$d\run.scr" /l en-US *> "$base.console.log"
 $ok=(Test-Path "$base.txt") -and (Select-String "$base.txt" -Pattern '^COMPLETE$' -Quiet)
 $fail=if(Test-Path "$base.txt"){(Get-Content "$base.txt" | Where-Object {$_ -notmatch '^(PASS|CK |COMPLETE|Rebar|Upper)'} | Select-Object -First 3) -join ' | '}else{'no result'}
 "{0,-10} {1}  {2}" -f $c.n,($(if($ok){'COMPLETE'}else{'FAIL'})),$fail
}
"Plan DLL: $plan"
