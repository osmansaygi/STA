# KAPAK formunu Core Console'da olusturup PNG'ye cizer (gorsel denetim).
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Kapak20261124\Form.ps1 [-Dwg <mimari.dwg>] [-Png <cikti.png>]
param([string]$Dwg = '', [string]$Png = '', [switch]$Hatirla)
$ErrorActionPreference = 'Stop'
$d = $PSScriptRoot
$root = Resolve-Path "$d\..\.."
$dll = (Get-ChildItem "$root\ST4_Yardimci\bin\Debug\net10.0-windows\ST4_Yardimci_2027_*.dll" | Sort-Object Name | Select-Object -Last 1).FullName
& dotnet build "$d\FormProbe\FormProbe.csproj" -c Debug -v quiet -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'FormProbe derlenemedi' }
$cikti = "$d\cikti"; New-Item -ItemType Directory -Force $cikti | Out-Null
if (-not $Png) { $Png = "$cikti\form.png" }
function F([string]$p) { '"' + $p.Replace([string][char]92, '/') + '"' }
$scr = "$cikti\form.scr"; $log = "$cikti\form.console.log"
Remove-Item $Png, $log -ErrorAction SilentlyContinue
@('SECURELOAD', '0', 'FILEDIA', '0', '_.NETLOAD', (F $dll), '_.NETLOAD', (F "$d\FormProbe\bin\Debug\net10.0-windows\FormProbe.dll"), 'KAPAKFORMPNG', '_.QUIT', '_Y') |
    Set-Content -Encoding Default $scr
$env:KAPAK_FORM_PNG = $Png; $env:KAPAK_FORM_DWG = $Dwg; if ($Hatirla) { $env:KAPAK_FORM_HATIRLA = "1" } else { $env:KAPAK_FORM_HATIRLA = $null }
$p = Start-Process -FilePath 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' -ArgumentList @('/s', "`"$scr`"", '/l', 'en-US') -RedirectStandardOutput $log -NoNewWindow -PassThru
if (-not $p.WaitForExit(180000)) { $p.Kill(); 'SURE ASIMI' }
$env:KAPAK_FORM_PNG = $null; $env:KAPAK_FORM_DWG = $null
"png=$(Test-Path $Png)"
Get-Content $log -Encoding Unicode | Where-Object { $_ -match 'KAPAKFORMPNG' } | ForEach-Object { $_.Trim() }
