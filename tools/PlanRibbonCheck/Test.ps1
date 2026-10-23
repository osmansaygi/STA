param([switch]$Render)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Prepare.ps1')
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$catalog = [IO.File]::ReadAllText((Join-Path $root 'ST4_Plan_ID_Ciz/BeykentRibbonCatalog.cs'))
$productionCommands = Get-ChildItem (Join-Path $root 'ST4_Plan_ID_Ciz') -Filter '*.cs' | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }
foreach ($match in [regex]::Matches($catalog, 'new Tool\("([A-Z0-9]+)"')) {
    $command = $match.Groups[1].Value
    if (!(($productionCommands -join "`n") -match ('CommandMethod\("' + $command + '"'))) { throw "Unregistered command: $command" }
}
& dotnet build (Join-Path $PSScriptRoot 'Probe/Probe.csproj') -c Debug -v quiet -p:UseSharedCompilation=false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Plan ribbon probe build failed' }
$probeArgs = @((Join-Path $PSScriptRoot 'Probe/bin/Debug/net10.0-windows/Probe.dll'))
if ($Render) { $probeArgs += (Join-Path $PSScriptRoot 'actual-ribbon.png') }
& dotnet @probeArgs
if ($LASTEXITCODE -ne 0) { throw 'Plan ribbon regression failed' }
