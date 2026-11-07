# Core Console 2027: sentetik mantar döşeme (KAT SINIRI + KALIP BOSLUK + 4 kolon) → MANTARILAVECIZ → döküm + DWG.
param([string]$Name='t1',[string]$Dll)
$ErrorActionPreference='Stop'
$d=$PSScriptRoot
if(-not $Dll){$Dll=(Get-ChildItem "$d\buildYrd\ST4_Yardimci_2027_*.dll" | Sort-Object LastWriteTime | Select-Object -Last 1).FullName}
$out="$d\out"; New-Item -ItemType Directory -Force $out | Out-Null
$dwg="$out\$Name.dwg"; Remove-Item $dwg -ErrorAction SilentlyContinue
$o=$out.Replace('\','/')
$scr="$d\$Name.scr"
@"
SECURELOAD
0
_.NETLOAD
$($Dll.Replace('\','/'))
(defun pl (lyr pts) (entmake (append (list '(0 . "LWPOLYLINE") '(100 . "AcDbEntity") (cons 8 lyr) '(100 . "AcDbPolyline") (cons 90 (length pts)) '(70 . 1)) (mapcar '(lambda (p) (cons 10 p)) pts))))
(defun kol (x y) (pl "KOLON (BEYKENT)" (list (list (- x 20) (- y 20)) (list (+ x 20) (- y 20)) (list (+ x 20) (+ y 20)) (list (- x 20) (+ y 20)))))
(pl "KAT SINIRI (BEYKENT)" '((0 0) (1200 0) (1200 1000) (0 1000)))
(pl "KALIP BOSLUK (BEYKENT)" '((700 750) (800 750) (800 900) (700 900)))
(kol 600 400)
(kol 20 500)
(kol 20 20)
(kol 900 820)
(entmake '((0 . "TEXT") (8 . "DOSEME ISMI (BEYKENT)") (10 300 300 0) (40 . 12) (1 . "D101 d=25cm")))
MANTARILAVECIZ
_ALL

(progn (setq f (open "$o/$Name.txt" "w")) (setq ss (ssget "X")) (write-line (strcat "TOPLAM " (itoa (if ss (sslength ss) 0))) f) (setq i 0) (while (and ss (< i (sslength ss))) (setq e (entget (ssname ss i))) (write-line (strcat (cdr (assoc 0 e)) "|" (cdr (assoc 8 e)) "|" (if (assoc 1 e) (cdr (assoc 1 e)) "") "|" (vl-princ-to-string (vl-remove-if-not '(lambda (x) (= (car x) 10)) e))) f) (setq i (1+ i))) (close f) (princ))
_.SAVEAS
2018
$dwg
_.QUIT
_Y
"@ | Set-Content -Encoding ascii $scr
& 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s $scr /l en-US *> "$out\$Name.console.log"
"DLL: $Dll"
