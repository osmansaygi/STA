# -KAPAKPDF Core Console denemesi: rapor kapaklarını klasöre A4 PDF basar; geçici kapakların silindiğini sayar.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Kapak20261124\Pdf.ps1 -Dwg <mimari.dwg> [-Sira n] [-Klasor <klasör>]
param([string]$Dwg = '', [int]$Sira = 1, [string]$Klasor = '', [string]$YardimciDll = '')
$ErrorActionPreference = 'Stop'
$d = $PSScriptRoot
$root = Resolve-Path "$d\..\.."
if (-not $YardimciDll) { $YardimciDll = (Get-ChildItem "$root\ST4_Yardimci\bin\Debug\net10.0-windows\ST4_Yardimci_2027_*.dll" | Sort-Object Name | Select-Object -Last 1).FullName }
if (-not $Klasor) { $Klasor = "$d\cikti\pdf" }
Remove-Item "$Klasor\*.pdf" -ErrorAction SilentlyContinue
function F([string]$p) { '"' + $p.Replace([string][char]92, '/') + '"' }
$scr = "$d\cikti\pdf.scr"; $log = "$d\cikti\pdf.console.log"
$lines = @('SECURELOAD', '0', 'FILEDIA', '0', '_.NETLOAD', (F $YardimciDll), '-KAPAKPDF')
if ($Dwg) { $lines += @($Dwg.Replace([string][char]92, '/'), "$Sira") } else { $lines += '' }
$lines += @($Klasor.Replace([string][char]92, '/'), '(princ (strcat "\nKALAN NESNE: " (itoa (if (setq ss (ssget "_X")) (sslength ss) 0))))', '_.QUIT', '_Y')
$lines | Set-Content -Encoding Default $scr
$p = Start-Process -FilePath 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' -ArgumentList @('/s', "`"$scr`"", '/l', 'en-US') -RedirectStandardOutput $log -NoNewWindow -PassThru
if (-not $p.WaitForExit(300000)) { $p.Kill(); 'SURE ASIMI' }
Get-Content $log -Encoding Unicode | Where-Object { $_ -match 'KAPAK|KALAN' } | ForEach-Object { $_.Trim() }
Get-ChildItem $Klasor -Filter *.pdf -ErrorAction SilentlyContinue | ForEach-Object { '{0,8:N0} B  {1}' -f $_.Length, $_.Name }
