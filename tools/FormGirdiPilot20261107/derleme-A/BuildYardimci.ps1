# AutoCAD 2027 / net10 Yardimci izole derleme: ortak bin/obj ve NETLOAD'a yazmaz.
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../../..").Path
$obj=Join-Path $PSScriptRoot 'objYardimci/'
$bin=Join-Path $PSScriptRoot 'buildYardimci/'
New-Item -ItemType Directory -Force $obj,$bin | Out-Null
& dotnet build "$root/ST4_Yardimci/ST4_Yardimci.csproj" -c Release -v minimal -nologo -p:AutoCAD2027Build=true "-p:BaseIntermediateOutputPath=$obj" "-p:MSBuildProjectExtensionsPath=$obj" "-p:OutputPath=$bin" "-p:CustomAfterMicrosoftCommonTargets=$PSScriptRoot/Isolated.targets"
if($LASTEXITCODE -ne 0){throw 'Yardimci 2027 build failed'}
