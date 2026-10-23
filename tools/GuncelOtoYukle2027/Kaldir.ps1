# Otomatik yukleyiciyi kaldirir; sonraki acilistan itibaren DLL'ler yine elle NETLOAD edilir.
$dest = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\ST4_GUNCEL_2027.bundle'
if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force; "Kaldirildi: $dest" } else { "Kurulu degil." }
