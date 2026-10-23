# Core Console 2027: izole Plan DLL ile KK_SUREKLI / KK_KIRIS; KESIT_ISTASYON tanı satırlarını <Name>.kesit.txt'ye ayırır.
param([string]$Cmd='KK_SUREKLI',[string]$Name='surekli',[string]$Antet='0',[string]$PlanDll='',[string]$St4='')
$ErrorActionPreference='Stop'
$d=$PSScriptRoot
if(-not $PlanDll){$PlanDll=(Get-ChildItem "$d\build2027\ST4_Plan_ID_Ciz_2027_*.dll" | Sort-Object Name | Select-Object -Last 1).FullName}
function F([string]$p){ '"' + $p.Replace([string][char]92,'/') + '"' }
$scr="$d\$Name.scr"
@('SECURELOAD','0','_.NETLOAD',(F $PlanDll),'_.NETLOAD',(F "$d\Native\bin\Debug\net10.0-windows\Native.dll"),$Cmd,'_.QUIT','_Y') | Set-Content -Encoding ascii $scr
$env:KK_RESULT="$d\$Name.txt"; $env:KK_ANTET=$Antet
$env:KK_ST4="D:\projeler\projeler2026\k$([char]0x131)z$([char]0x131)lkaya\20827_2_TICARI\STATIK\CK_20827_2_01.st4"
if($St4){$env:KK_ST4=$St4}
Remove-Item $env:KK_RESULT -ErrorAction SilentlyContinue
$diag=Join-Path $env:LOCALAPPDATA 'ST4_Plan_ID_Ciz\kiris-acilim-diagnostic.log'
$n0=0; if(Test-Path $diag){$n0=@(Get-Content $diag).Count}
Push-Location $d
try { & 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s $scr /l en-US *> "$d\$Name.console.log" } finally { Pop-Location }
"exit=$LASTEXITCODE"
if(Test-Path $env:KK_RESULT){Get-Content $env:KK_RESULT}else{'sonuc yok'}
if(Test-Path $diag){ $yeni=@(Get-Content $diag) | Select-Object -Skip $n0; $yeni | Where-Object {$_ -match 'KESIT_ISTASYON'} | Set-Content -Encoding utf8 "$d\$Name.kesit.txt"; $yeni | Where-Object {$_ -match 'GOVDE_TAHVIL'} | Set-Content -Encoding utf8 "$d\$Name.govde.txt"; $yeni | Where-Object {$_ -match 'MESNET_BINDIRME kiris'} | Set-Content -Encoding utf8 "$d\$Name.mesnet.txt" }
