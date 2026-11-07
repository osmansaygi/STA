# Core Console 2027: izole Plan DLL ile KALIP50 veya DENEME2; sonuÃ§ <Mode>.txt
param([string]$Mode='DENEME2',[string]$PlanDll='',[string]$St4='')
$ErrorActionPreference='Stop'
$d=$PSScriptRoot
if(-not $PlanDll){$PlanDll=(Get-ChildItem "$d\..\PlanRibbonCheck\build\ST4_Plan_ID_Ciz_2027_*.dll" | Sort-Object Name | Select-Object -Last 1).FullName}
function F([string]$p){ '"' + $p.Replace([string][char]92,'/') + '"' }
$scr="$d\$Mode.scr"
@('SECURELOAD','0','_.NETLOAD',(F $PlanDll),'_.NETLOAD',(F "$d\Native\bin\Debug\net10.0-windows\Native.dll"),'KD_KALIP','_.QUIT','_Y') | Set-Content -Encoding ascii $scr
$env:KD_RESULT="$d\$Mode.txt"; $env:KD_MODE=$Mode; if(-not $env:KD_PNGNAME){$env:KD_PNGNAME=$Mode}; $env:KD_PNG="$d\$($env:KD_PNGNAME).png"
$env:KD_ST4="D:\projeler\projeler2026\k$([char]0x131)z$([char]0x131)lkaya\20827_2_TICARI\STATIK\CK_20827_2_01.st4"
if($St4){$env:KD_ST4=$St4}
Remove-Item $env:KD_RESULT -ErrorAction SilentlyContinue
Push-Location $d
try { & 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s $scr /l en-US *> "$d\$Mode.console.log" } finally { Pop-Location }
"exit=$LASTEXITCODE"
if(Test-Path $env:KD_RESULT){Get-Content $env:KD_RESULT | Where-Object { $_ -notmatch '^E ' }}else{'sonuc yok'}
