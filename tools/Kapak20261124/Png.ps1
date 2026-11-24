# Kapak DWG'sinin bir penceresini PNG'ye yazdırır (görsel kontrol).
#   powershell -NoProfile -File tools\Kapak20261124\Png.ps1 -Dwg <kapak.dwg> -Png <cikti.png> [-Pencere "x0,y0 x1,y1"]
param([string]$Dwg, [string]$Png, [string]$Pencere = '0,0 1050,1485')
$ErrorActionPreference = 'Stop'
function F([string]$p) { '"' + $p.Replace([string][char]92, '/') + '"' }
$k = $Pencere.Split(' ')
$scr = [IO.Path]::ChangeExtension($Png, '.scr')
Remove-Item $Png -ErrorAction SilentlyContinue
@('FILEDIA', '0', '_.-PLOT', '_Y', 'Model', 'PublishToWeb PNG.pc3', 'Sun Hi-Res (1600.00 x 1280.00 Pixels)', '_L', '_N', '_W', $k[0], $k[1],
  '_F', '_C', '_Y', '.', '_Y', '_A', (F $Png), '_N', '_Y', '_.QUIT', '_Y') | Set-Content -Encoding ascii $scr
& 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /i $Dwg /s $scr /l en-US *> ([IO.Path]::ChangeExtension($Png, '.log'))
"exit=$LASTEXITCODE png=$(Test-Path $Png)"
