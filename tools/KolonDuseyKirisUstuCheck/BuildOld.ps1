$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$obj=Join-Path $PSScriptRoot 'obj2027old/'
$bin=Join-Path $PSScriptRoot 'build2027old/'
New-Item -ItemType Directory -Force $obj,$bin | Out-Null
& dotnet build "$root/ST4_Plan_ID_Ciz/ST4_Plan_ID_Ciz.csproj" -c Debug -v minimal -p:AutoCAD2027Build=true "-p:BaseIntermediateOutputPath=$obj" "-p:MSBuildProjectExtensionsPath=$obj" "-p:OutputPath=$bin" "-p:CustomAfterMicrosoftCommonTargets=$PSScriptRoot/OldIsolated.targets"
if($LASTEXITCODE -ne 0){throw 'old build failed'}
