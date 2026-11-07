# Ölçek formu: Plan DLL + Probe ile Core Console'da DENEME1/DENEME2/KALIP (1/50 veya 1/100); sonuç out\<Name>.*
param([string]$Name,[string]$Mode='deneme1',[string]$Scale='100',[string]$PlanDll='',[string]$Cmd='OF_RUN',[string]$St4='D:\projeler\projeler2026\ser_mimarlik\ardicli\STATIK\ARD_OTP1_01.st4')
$ErrorActionPreference='Stop'
$d=$PSScriptRoot; $out="$d\out"; New-Item -ItemType Directory -Force $out | Out-Null
if(-not $PlanDll){$PlanDll=(Get-ChildItem "$d\buildPlan\ST4_Plan_ID_Ciz_2027_*.dll" | Sort-Object LastWriteTime | Select-Object -Last 1).FullName}
$probe="$d\Probe\bin\Debug\net10.0-windows\Probe.dll"
function F([string]$p){ '"' + $p.Replace([string][char]92,'/') + '"' }
$dwg="$out\$Name.dwg"; Remove-Item $dwg -ErrorAction SilentlyContinue
$scr="$out\$Name.scr"
@('SECURELOAD','0','_.NETLOAD',(F $PlanDll),'_.NETLOAD',(F $probe),$Cmd,'_.SAVEAS','2018',$dwg,'_.QUIT','_Y') | Set-Content -Encoding ascii $scr
$env:OF_RESULT="$out\$Name.txt"; $env:OF_ST4=$St4; $env:OF_MODE=$Mode; $env:OF_SCALE=$Scale
Remove-Item $env:OF_RESULT -ErrorAction SilentlyContinue
& 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s $scr /l en-US *> "$out\$Name.console.log"
"exit=$LASTEXITCODE dll=$PlanDll"
if(Test-Path $env:OF_RESULT){Get-Content $env:OF_RESULT}else{'sonuc yok'}
