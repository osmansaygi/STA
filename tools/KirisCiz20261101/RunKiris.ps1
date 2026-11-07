# KIRISACILIMLARI regresyonu: KesitKonum20261023 Native yansıma harness'i (KK_KIRIS) ile verilen Plan DLL'i Core Console 2027'de çalıştırır.
# Çıktı reg\<Name>.txt; çizim sonrası entity dökümü reg\<Name>.ent.txt (tür|katman|metin).
param([string]$Name='kiris',[string]$Antet='0',[string]$PlanDll='',[string]$St4='')
$ErrorActionPreference='Stop'
$d=$PSScriptRoot
$reg="$d\reg"; New-Item -ItemType Directory -Force $reg | Out-Null
if(-not $PlanDll){$PlanDll=(Get-ChildItem "$d\buildPlan\ST4_Plan_ID_Ciz_2027_*.dll" | Sort-Object Name | Select-Object -Last 1).FullName}
$native="$d\..\KesitKonum20261023\Native\bin\Debug\net10.0-windows\Native.dll"
function F([string]$p){ '"' + $p.Replace([string][char]92,'/') + '"' }
$ent=($reg + "\$Name.ent.txt").Replace([string][char]92,'/')
$lisp='(progn (setq f (open "' + $ent + '" "w")) (setq ss (ssget "X")) (setq i 0) (while (and ss (< i (sslength ss))) (setq e (entget (ssname ss i))) (write-line (strcat (cdr (assoc 0 e)) "|" (cdr (assoc 8 e)) "|" (if (assoc 1 e) (cdr (assoc 1 e)) "")) f) (setq i (1+ i))) (close f) (princ))'
$scr="$reg\$Name.scr"
@('SECURELOAD','0','_.NETLOAD',(F $PlanDll),'_.NETLOAD',(F $native),'KK_KIRIS',$lisp,'_.QUIT','_Y') | Set-Content -Encoding ascii $scr
$env:KK_RESULT="$reg\$Name.txt"; $env:KK_ANTET=$Antet
$env:KK_ST4="D:\projeler\projeler2026\k$([char]0x131)z$([char]0x131)lkaya\20827_2_TICARI\STATIK\CK_20827_2_01.st4"
if($St4){$env:KK_ST4=$St4}
Remove-Item $env:KK_RESULT -ErrorAction SilentlyContinue
& 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s $scr /l en-US *> "$reg\$Name.console.log"
"exit=$LASTEXITCODE  dll=$PlanDll"
if(Test-Path $env:KK_RESULT){Get-Content $env:KK_RESULT}else{'sonuc yok'}
