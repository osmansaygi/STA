param([switch]$Render)
$ErrorActionPreference = 'Stop'
$project=Join-Path $PSScriptRoot 'HomeRibbonCheck/Probe/Probe.csproj'
& dotnet build $project -c Debug -v quiet -p:UseSharedCompilation=false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Home Ribbon probe build failed' }
$probe=Join-Path $PSScriptRoot 'HomeRibbonCheck/Probe/bin/Debug/net10.0-windows/Probe.dll'
$probeArgs=@($probe)
if ($Render) { $probeArgs += (Join-Path $PSScriptRoot 'HomeRibbonCheck/actual-ribbon.png') }
& dotnet @probeArgs
if ($LASTEXITCODE -ne 0) { throw 'Home Ribbon regression failed' }
