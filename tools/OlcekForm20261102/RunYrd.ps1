# Yardımcı 1/25 detay komutu Core Console'da (pafta_olcek.ini anahtarı 0/1); varlık dökümü out\<Name>.ent.txt
param([string]$Name,[string]$Cmd='KURANGLEZCIZ',[string]$Key='kuranglez',[string]$Yuz='0',[string]$Point='0,0')
$ErrorActionPreference='Stop'
$d=$PSScriptRoot; $out="$d\out"; New-Item -ItemType Directory -Force $out | Out-Null
$dll=(Get-ChildItem "$d\buildYrd\ST4_Yardimci_2027_*.dll" | Sort-Object LastWriteTime | Select-Object -Last 1).FullName
$ini=Join-Path $env:LOCALAPPDATA 'ST4_Yardimci\pafta_olcek.ini'
Set-Content -LiteralPath $ini -Value "$Key=$Yuz" -Encoding utf8
$ent=("$out\$Name.ent.txt").Replace([string][char]92,'/')
# tür|katman|yazı boyu|ölçü görünen değeri (DXF 1 / 42×DIMLFAC yerine blok yazısı yok: ölçü için 42 ham)
$lisp='(progn (setq f (open "' + $ent + '" "w")) (setq ss (ssget "X")) (setq i 0) (while (and ss (< i (sslength ss))) (setq e (entget (ssname ss i))) (write-line (strcat (cdr (assoc 0 e)) "|" (cdr (assoc 8 e)) "|" (if (assoc 40 e) (rtos (cdr (assoc 40 e)) 2 1) "") "|" (if (assoc 1 e) (cdr (assoc 1 e)) "")) f) (setq i (1+ i))) (close f) (princ))'
$scr="$out\$Name.scr"
@('SECURELOAD','0','_.NETLOAD',('"' + $dll.Replace([string][char]92,'/') + '"'),$Cmd,$Point,'_.ZOOM','_E',$lisp,'_.SAVEAS','2018',"$out\$Name.dwg",'_.QUIT','_Y') | Set-Content -Encoding ascii $scr
Remove-Item "$out\$Name.dwg" -ErrorAction SilentlyContinue
& 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s $scr /l en-US *> "$out\$Name.console.log"
Remove-Item -LiteralPath $ini -ErrorAction SilentlyContinue
"exit=$LASTEXITCODE dll=$dll"
if(Test-Path "$out\$Name.ent.txt"){ "ent=" + (Get-Content "$out\$Name.ent.txt").Count }else{'sonuc yok'}
