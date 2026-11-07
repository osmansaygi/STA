# AutoCAD 2027 / net10 Plan ID izole derleme: ortak bin/obj ve NETLOAD'a yazmaz.
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$obj=Join-Path $PSScriptRoot 'obj2027/'
$bin=Join-Path $PSScriptRoot 'build2027/'
New-Item -ItemType Directory -Force $obj,$bin | Out-Null
& dotnet build "$root/ST4_Plan_ID_Ciz/ST4_Plan_ID_Ciz.csproj" -c Debug -v minimal -p:AutoCAD2027Build=true "-p:BaseIntermediateOutputPath=$obj" "-p:MSBuildProjectExtensionsPath=$obj" "-p:OutputPath=$bin" "-p:CustomAfterMicrosoftCommonTargets=$PSScriptRoot/Isolated.targets"
if($LASTEXITCODE -ne 0){throw 'Plan 2027 build failed'}
