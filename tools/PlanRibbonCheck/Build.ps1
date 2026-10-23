$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Prepare.ps1')
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$argsForBuild = @('build', (Join-Path $root 'ST4_Plan_ID_Ciz/ST4_Plan_ID_Ciz.csproj'), '-c', 'Debug', '-v', 'minimal',
    '-p:AutoCAD2027Build=true', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
    "-p:BaseIntermediateOutputPath=$PSScriptRoot/obj/", "-p:OutputPath=$PSScriptRoot/build/",
    "-p:CustomAfterMicrosoftCommonTargets=$PSScriptRoot/Isolated.targets")
& dotnet @argsForBuild
if ($LASTEXITCODE -ne 0) { throw 'Isolated Plan ribbon build failed' }
'No deployment performed. Build uses the production lifecycle and isolated output paths.'
