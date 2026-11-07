# Mantar zımbalama: Plan DLL + Probe ile MANTARDOSEME (SANER_01); DWG + döküm out\<Name>.*
param([string]$Name='saner',[string]$PlanDll='',[string]$St4='D:\projeler\projeler2026\oncan_mimarlik\saner\statik\SANER_01.ST4',[switch]$Bos)
$ErrorActionPreference='Stop'
$d=$PSScriptRoot; $out="$d\out"; New-Item -ItemType Directory -Force $out | Out-Null
if(-not $PlanDll){$PlanDll=(Get-ChildItem "$d\buildPlan\ST4_Plan_ID_Ciz_2027_*.dll" | Sort-Object Name | Select-Object -Last 1).FullName}
$probe="$d\Probe\bin\Debug\net10.0-windows\Probe.dll"
function F([string]$p){ '"' + $p.Replace([string][char]92,'/') + '"' }
$dwg="$out\$Name.dwg"; Remove-Item $dwg -ErrorAction SilentlyContinue
$scr="$out\$Name.scr"
@('SECURELOAD','0','_.NETLOAD',(F $PlanDll),'_.NETLOAD',(F $probe),'MZ_RUN','_.SAVEAS','2018',$dwg,'_.QUIT','_Y') | Set-Content -Encoding ascii $scr
$env:MZ_RESULT="$out\$Name.txt"; $env:MZ_ST4=$St4; $env:MZ_BOS=$(if($Bos){'1'}else{'0'})
Remove-Item $env:MZ_RESULT -ErrorAction SilentlyContinue
& 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s $scr /l en-US *> "$out\$Name.console.log"
"exit=$LASTEXITCODE dll=$PlanDll"
if(Test-Path $env:MZ_RESULT){Get-Content $env:MZ_RESULT}else{'sonuc yok'}
