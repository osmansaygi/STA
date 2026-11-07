# Eğimli kapama perdesi: Plan DLL + Probe ile ARD dosyasında BINAKESITI/BINAGORUNUS/KAPAMADETAY; DWG + entity dökümü out\<Name>.*
param([string]$Name='yeni',[string]$PlanDll='',[string]$St4='D:\projeler\projeler2026\ser_mimarlik\ardicli\STATIK\ARD_OTP1_01.st4')
$ErrorActionPreference='Stop'
$d=$PSScriptRoot; $out="$d\out"; New-Item -ItemType Directory -Force $out | Out-Null
if(-not $PlanDll){$PlanDll=(Get-ChildItem "$d\..\KirisCiz20261101\buildPlan\ST4_Plan_ID_Ciz_2027_*.dll" | Sort-Object LastWriteTime | Select-Object -Last 1).FullName}
$probe="$d\Probe\bin\Debug\net10.0-windows\Probe.dll"
function F([string]$p){ '"' + $p.Replace([string][char]92,'/') + '"' }
$ent=("$out\$Name.ent.txt").Replace([string][char]92,'/')
$lisp='(progn (setq f (open "' + $ent + '" "w")) (setq ss (ssget "X")) (setq i 0) (while (and ss (< i (sslength ss))) (setq e (entget (ssname ss i))) (write-line (strcat (cdr (assoc 0 e)) "|" (cdr (assoc 8 e)) "|" (if (assoc 1 e) (cdr (assoc 1 e)) "")) f) (setq i (1+ i))) (close f) (princ))'
$dwg="$out\$Name.dwg"; Remove-Item $dwg -ErrorAction SilentlyContinue
$scr="$out\$Name.scr"
@('SECURELOAD','0','_.NETLOAD',(F $PlanDll),'_.NETLOAD',(F $probe),'EP_RUN',$lisp,'_.SAVEAS','2018',$dwg,'_.QUIT','_Y') | Set-Content -Encoding ascii $scr
$env:EP_RESULT="$out\$Name.txt"; $env:EP_ST4=$St4
Remove-Item $env:EP_RESULT -ErrorAction SilentlyContinue
& 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s $scr /l en-US *> "$out\$Name.console.log"
"exit=$LASTEXITCODE dll=$PlanDll"
if(Test-Path $env:EP_RESULT){Get-Content $env:EP_RESULT}else{'sonuc yok'}
