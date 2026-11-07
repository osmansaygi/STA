param([string]$Name='t1',[string]$Dll)
$ErrorActionPreference='Stop'
$d=$PSScriptRoot
if(-not $Dll){$Dll=(Get-ChildItem "$d\buildPlan\ST4_Plan_ID_Ciz_2027_*.dll" | Sort-Object LastWriteTime | Select-Object -Last 1).FullName}
$out="$d\out"; New-Item -ItemType Directory -Force $out | Out-Null
$dwg="$out\$Name.dwg"; Remove-Item $dwg -ErrorAction SilentlyContinue
$scr="$d\$Name.scr"
@"
SECURELOAD
0
_.NETLOAD
$($Dll.Replace('\','/'))
-KIRISCIZ
$d\$Name.kiris
0,0
(progn (setq f (open "$($out.Replace('\','/'))/$Name.txt" "w")) (setq ss (ssget "X")) (write-line (strcat "TOPLAM " (itoa (if ss (sslength ss) 0))) f) (setq i 0) (while (and ss (< i (sslength ss))) (setq e (entget (ssname ss i))) (write-line (strcat (cdr (assoc 0 e)) "|" (cdr (assoc 8 e)) "|" (if (assoc 1 e) (cdr (assoc 1 e)) "")) f) (setq i (1+ i))) (close f) (princ))
_.SAVEAS
2018
$dwg
_.QUIT
_Y
"@ | Set-Content -Encoding ascii $scr
& 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s $scr /l en-US *> "$out\$Name.console.log"
"DLL: $Dll"

