param([string]$Name='t1',[string]$W1,[string]$W2,[string]$Tag='')
$d=$PSScriptRoot; $out="$d\out"
$pdf="$out\$Name$Tag.pdf"; Remove-Item $pdf -ErrorAction SilentlyContinue
$scr="$d\plot_$Name.scr"
$area = if($W1){"_W`r`n$W1`r`n$W2"}else{"_E"}
@"
_.ZOOM
_E
-PLOT
_Y

DWG To PDF.pc3
ISO full bleed A1 (841.00 x 594.00 MM)
_M
_L
_N
$area
_F
_C
_Y
monochrome.ctb
_Y
_A
$pdf
_N
_Y
_.QUIT
_Y
"@ | Set-Content -Encoding ascii $scr
& 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /i "$out\$Name.dwg" /s $scr /l en-US *> "$out\plot_$Name.console.log"
if(Test-Path $pdf){"PDF ok"}else{"PDF yok"}
