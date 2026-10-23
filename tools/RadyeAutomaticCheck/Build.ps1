param([string]$OutputName = 'before')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
$obj = Join-Path $PSScriptRoot 'obj/'
$bin = Join-Path $PSScriptRoot "build/$OutputName/"
New-Item -ItemType Directory -Force $obj,$bin | Out-Null
Get-ChildItem "$root/ST4_Yardimci/obj" -File | Where-Object { $_.Name -match '\.(json|props|targets|cache)$' } | Copy-Item -Destination $obj
& dotnet build "$root/ST4_Yardimci/ST4_Yardimci.csproj" -c Debug --no-restore -v minimal "-p:BaseIntermediateOutputPath=$obj" "-p:MSBuildProjectExtensionsPath=$obj" "-p:OutputPath=$bin" "-p:CustomAfterMicrosoftCommonTargets=$PSScriptRoot/Isolated.targets" -p:SkipObfuscar=true
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
