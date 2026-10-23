# ST4_GUNCEL_2027.bundle'i kullanici ApplicationPlugins klasorune kurar (AutoCAD 2027).
# AutoCAD acikken calisirsa bir sonraki acilista gecerli olur. Ayar/guvenlik degistirmez.
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'ST4_GUNCEL_2027.bundle'
$dest = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\ST4_GUNCEL_2027.bundle'
if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $dest 'Contents') | Out-Null
Copy-Item -LiteralPath (Join-Path $src 'PackageContents.xml') -Destination $dest -Force
Copy-Item -LiteralPath (Join-Path $src 'Contents\ST4_Guncel_Yukle.lsp') -Destination (Join-Path $dest 'Contents') -Force
Get-ChildItem -LiteralPath $dest -Recurse -File | ForEach-Object { '{0}  {1}' -f $_.FullName, (Get-FileHash $_.FullName).Hash }
