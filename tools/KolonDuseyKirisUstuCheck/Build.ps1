param([switch]$Plan2027)
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
if($Plan2027){
 # AutoCAD 2027 / net10 Plan ID: ayrı obj ve çıktı, kendi restore'u.
 $obj=Join-Path $PSScriptRoot 'obj2027/'
 $bin=Join-Path $PSScriptRoot 'build2027/'
 New-Item -ItemType Directory -Force $obj,$bin | Out-Null
 & dotnet build "$root/ST4_Plan_ID_Ciz/ST4_Plan_ID_Ciz.csproj" -c Debug -v minimal -p:AutoCAD2027Build=true "-p:BaseIntermediateOutputPath=$obj" "-p:MSBuildProjectExtensionsPath=$obj" "-p:OutputPath=$bin" "-p:CustomAfterMicrosoftCommonTargets=$PSScriptRoot/Isolated.targets"
 if($LASTEXITCODE -ne 0){throw 'KolonDusey 2027 build failed'}
 return
}
$obj=Join-Path $PSScriptRoot 'obj/'
$bin=Join-Path $PSScriptRoot 'build/'
New-Item -ItemType Directory -Force $obj,$bin | Out-Null
Get-ChildItem "$root/ST4_Plan_ID_Ciz/obj" -File | Where-Object {$_.Name -match '\.(json|props|targets|cache)$'} | Copy-Item -Destination $obj
& dotnet build "$root/ST4_Plan_ID_Ciz/ST4_Plan_ID_Ciz.csproj" -c Debug --no-restore -v minimal "-p:BaseIntermediateOutputPath=$obj" "-p:MSBuildProjectExtensionsPath=$obj" "-p:OutputPath=$bin" "-p:CustomAfterMicrosoftCommonTargets=$PSScriptRoot/Isolated.targets"
if($LASTEXITCODE -ne 0){throw 'KolonDusey build failed'}
