# KAPAK Core Console denemesi: -KAPAK ile kapağı çizer, DWG kaydeder ve PDF'e yazdırır.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Kapak20261124\Run.ps1 -Dwg <mimari.dwg> -Name <ad> [-Baslik Proje|Hesaplar|Rapor] [-Sira n]
param([string]$Dwg = '', [string]$Name = 'kapak', [string]$Baslik = 'Proje', [string]$Tadilat = '', [int]$Sira = 1, [string]$YardimciDll = '', [switch]$Plotsuz, [int]$SureSn = 240)
$ErrorActionPreference = 'Stop'
$d = $PSScriptRoot
$root = Resolve-Path "$d\..\.."
if (-not $YardimciDll) {
    $YardimciDll = (Get-ChildItem "$root\ST4_Yardimci\bin\Debug\net10.0-windows\ST4_Yardimci_2027_*.dll" | Sort-Object Name | Select-Object -Last 1).FullName
}
$cikti = "$d\cikti"
New-Item -ItemType Directory -Force $cikti | Out-Null
function F([string]$p) { '"' + $p.Replace([string][char]92, '/') + '"' }
$scr = "$cikti\$Name.scr"
$outDwg = "$cikti\$Name.dwg"
$outPdf = "$cikti\$Name.pdf"
$log = "$cikti\$Name.console.log"
Remove-Item $outDwg, $outPdf, $log -ErrorAction SilentlyContinue
$lines = @('SECURELOAD', '0', 'FILEDIA', '0', '_.NETLOAD', (F $YardimciDll), '-KAPAK')
# Mimari DWG verilirse komut kapak sırasını her zaman sorar (tek kapakta da).
if ($Dwg) { $lines += @($Dwg.Replace([string][char]92, '/'), "$Sira") } else { $lines += '' }
$lines += @($Baslik, $Tadilat, '0,0', '_.ZOOM', '_E')
if (-not $Plotsuz) {
    $lines += @('_.-PLOT', '_Y', 'Model', 'DWG To PDF.pc3', 'ISO full bleed A4 (210.00 x 297.00 MM)', '_M', '_P', '_N', '_W', '0,0', '1050,1485',
        '_F', '_C', '_Y', '.', '_Y', '_A', (F $outPdf), '_N', '_Y')
}
$lines += @('_.SAVEAS', '2018', (F $outDwg), '_.QUIT', '_Y')
$lines | Set-Content -Encoding Default $scr
# Geçersiz girdide Core Console sonsuza dek bekleyebilir: süre sınırı.
$p = Start-Process -FilePath 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' -ArgumentList @('/s', "`"$scr`"", '/l', 'en-US') `
    -RedirectStandardOutput $log -NoNewWindow -PassThru
if (-not $p.WaitForExit($SureSn * 1000)) { $p.Kill(); "SURE ASIMI ($SureSn sn)" } else { "exit=$($p.ExitCode)" }
"dll=$(Split-Path $YardimciDll -Leaf) dwg=$(Test-Path $outDwg) pdf=$(Test-Path $outPdf)"
