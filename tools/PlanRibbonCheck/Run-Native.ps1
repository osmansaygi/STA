$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$plan = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'build') -Filter 'ST4_Plan_ID_Ciz_2027_*.dll' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (!$plan) { throw 'Build Plan first' }
$current = Join-Path $root 'NETLOAD/GUNCEL_2027'
$package = Join-Path $PSScriptRoot ('package-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $package | Out-Null
Get-ChildItem -LiteralPath $current -Filter '*.dll' | Where-Object { $_.Name -notlike 'ST4_Plan_ID_Ciz*' } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $package
}
Copy-Item -LiteralPath $plan.FullName -Destination $package
$yardimci = Get-ChildItem -LiteralPath $package -Filter 'ST4_Yardimci_2027_*.dll' | Select-Object -ExpandProperty FullName
$planPath = Join-Path $package $plan.Name
& dotnet build (Join-Path $PSScriptRoot 'Native/Native.csproj') -c Debug -v quiet "-p:PlanDll=$planPath" "-p:PackageDir=$package" -p:NuGetAudit=false -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Native probe build failed' }
$probe = Join-Path $PSScriptRoot 'Native/bin/Debug/net10.0-windows/Native.dll'
$env:ST4_CURRENT_PACKAGE = $package
$env:ST4_CURRENT_RESULT = $PSScriptRoot
$env:ST4_FOUNDATION_SOURCE = 'D:\projeler\projeler2026\kızılkaya\20827_2_TICARI\STATIK\CK_20827_2_01.st4'
if (!(Test-Path -LiteralPath $env:ST4_FOUNDATION_SOURCE)) { throw 'Reference ST4 input missing' }
$env:ST4_FOUNDATION_RESULT = Join-Path $PSScriptRoot 'core-foundation.txt'
$env:ST4_FOUNDATION_ANTET = '1'
$env:ST4_FOUNDATION_REPEAT = '1'
$env:ST4_FOUNDATION_DWG = ''
$script = Join-Path $PSScriptRoot 'native.scr'
@"
SECURELOAD
0
_.NETLOAD
$yardimci
_.NETLOAD
$planPath
_.NETLOAD
$probe
PLAN_RIBBON_CHECK
CURRENT_DLL_CHECK
FOUNDATION_VIEW_CHECK
_.QUIT
_Y
"@ | Set-Content -LiteralPath $script -Encoding ascii
Push-Location $PSScriptRoot
try {
    & 'C:/Program Files/Autodesk/AutoCAD 2027/accoreconsole.exe' /s $script /l en-US *> (Join-Path $PSScriptRoot 'core-console.log')
    if ($LASTEXITCODE -ne 0) { throw "Core Console exit=$LASTEXITCODE" }
} finally { Pop-Location }
foreach ($file in @('native-ribbon.txt', 'package-probe.txt', 'core-foundation.txt')) {
    $result = Join-Path $PSScriptRoot $file
    if (!(Test-Path -LiteralPath $result) -or !(Select-String -LiteralPath $result -Pattern '^COMPLETE$' -Quiet)) { throw "Native check incomplete: $file" }
    if (Select-String -LiteralPath $result -Pattern '^FAIL' -Quiet) { throw "Native check failed: $file" }
    "PASS $file"
}
'COMPLETE: isolated Core Console load/registration and repeated foundation drawing; no current-package changes.'
